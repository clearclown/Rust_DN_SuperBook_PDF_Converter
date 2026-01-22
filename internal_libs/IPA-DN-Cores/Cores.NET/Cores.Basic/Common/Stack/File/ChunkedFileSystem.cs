// IPA Cores.NET
// 
// Copyright (c) 2018- IPA CyberLab.
// Copyright (c) 2003-2018 Daiyuu Nobori.
// Copyright (c) 2013-2018 SoftEther VPN Project, University of Tsukuba, Japan.
// All Rights Reserved.
// 
// License: The Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
// 
// THIS SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.
// 
// THIS SOFTWARE IS DEVELOPED IN JAPAN, AND DISTRIBUTED FROM JAPAN, UNDER
// JAPANESE LAWS. YOU MUST AGREE IN ADVANCE TO USE, COPY, MODIFY, MERGE, PUBLISH,
// DISTRIBUTE, SUBLICENSE, AND/OR SELL COPIES OF THIS SOFTWARE, THAT ANY
// JURIDICAL DISPUTES WHICH ARE CONCERNED TO THIS SOFTWARE OR ITS CONTENTS,
// AGAINST US (IPA CYBERLAB, DAIYUU NOBORI, SOFTETHER VPN PROJECT OR OTHER
// SUPPLIERS), OR ANY JURIDICAL DISPUTES AGAINST US WHICH ARE CAUSED BY ANY KIND
// OF USING, COPYING, MODIFYING, MERGING, PUBLISHING, DISTRIBUTING, SUBLICENSING,
// AND/OR SELLING COPIES OF THIS SOFTWARE SHALL BE REGARDED AS BE CONSTRUED AND
// CONTROLLED BY JAPANESE LAWS, AND YOU MUST FURTHER CONSENT TO EXCLUSIVE
// JURISDICTION AND VENUE IN THE COURTS SITTING IN TOKYO, JAPAN. YOU MUST WAIVE
// ALL DEFENSES OF LACK OF PERSONAL JURISDICTION AND FORUM NON CONVENIENS.
// PROCESS MAY BE SERVED ON EITHER PARTY IN THE MANNER AUTHORIZED BY APPLICABLE
// LAW OR COURT RULE.

using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;
using System.Buffers;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using Microsoft.Extensions.FileProviders;

namespace IPA.Cores.Basic;

public static partial class CoresConfig
{
    public static partial class LocalChunkedFileSystemSettings
    {
        public static readonly Copenhagen<long> MaxSingleFileSize = 1_000_000_000; // 1GB
        public static readonly Copenhagen<long> LogicalMaxSize = 1_000_000_000_000_000_000; // 1 EB
        //public static readonly Copenhagen<long> MaxSingleFileSize = 1_000_000; // 1MB
        //public static readonly Copenhagen<long> LogicalMaxSize = 1_000_000_000_000_000; // 1 TB
        public static readonly Copenhagen<string> SplitStr = ".part";
    }
}

public class ChunkedFileObject : FileObject
{
    public class Cursor
    {
        public long LogicalPosition { get; }
        public long PhysicalFileNumber { get; }
        public long PhysicalPosition { get; }
        public long PhysicalRemainingLength { get; }
        public long PhysicalDataLength { get; }

        readonly ChunkedFileObject Lfo;

        public Cursor(ChunkedFileObject o, long logicalPosision, long dataLength = 0)
        {
            checked
            {
                Lfo = o;

                if (logicalPosision < 0)
                    throw new ArgumentOutOfRangeException("logicalPosision < 0");

                if (dataLength < 0)
                    throw new ArgumentOutOfRangeException("dataLength < 0");

                var p = Lfo.ChunkedFileSystem.Params;
                if (logicalPosision > p.MaxLogicalFileSize)
                    throw new ArgumentOutOfRangeException("logicalPosision > MaxLogicalFileSize");

                this.LogicalPosition = logicalPosision;
                this.PhysicalFileNumber = this.LogicalPosition / p.MaxSinglePhysicalFileSize;

                if (this.PhysicalFileNumber > p.MaxFileNumber)
                    throw new ArgumentOutOfRangeException($"this.PhysicalFileNumber ({this.PhysicalFileNumber}) > p.MaxFileNumber ({p.MaxFileNumber})");

                this.PhysicalPosition = this.LogicalPosition % p.MaxSinglePhysicalFileSize;
                this.PhysicalRemainingLength = p.MaxSinglePhysicalFileSize - this.PhysicalPosition;
                this.PhysicalDataLength = Math.Min(this.PhysicalRemainingLength, dataLength);
            }
        }

        public ChunkedFileSystem.ParsedPath GetParsedPath()
        {
            return new ChunkedFileSystem.ParsedPath(Lfo.ChunkedFileSystem, Lfo.FileParams.Path, this.PhysicalFileNumber);
        }
    }

    readonly ChunkedFileSystem ChunkedFileSystem;
    readonly FileSystem UnderlayFileSystem;
    readonly ChunkedFileSystem.ParsedPath[] InitialRelatedFiles;

    long CurrentFileSize;

    protected ChunkedFileObject(FileSystem fileSystem, FileParameters fileParams, ChunkedFileSystem.ParsedPath[] relatedFiles) : base(fileSystem, fileParams)
    {
        this.ChunkedFileSystem = (ChunkedFileSystem)fileSystem;
        this.UnderlayFileSystem = this.ChunkedFileSystem.UnderlayFileSystem;
        this.InitialRelatedFiles = relatedFiles;
    }

    public static async Task<FileObject> CreateFileAsync(ChunkedFileSystem fileSystem, FileParameters fileParams, ChunkedFileSystem.ParsedPath[] relatedFiles, CancellationToken cancel = default)
    {
        cancel.ThrowIfCancellationRequested();

        ChunkedFileObject f = new ChunkedFileObject(fileSystem, fileParams, relatedFiles);

        try
        {
            await f.InternalInitAsync(cancel);
        }
        catch
        {
            f._DisposeSafe();
            throw;
        }

        return f;
    }

    protected async Task InternalInitAsync(CancellationToken cancel = default)
    {
        cancel.ThrowIfCancellationRequested();

        try
        {
            ChunkedFileSystem.ParsedPath? lastFileParsed = InitialRelatedFiles.OrderBy(x => x.FileNumber).LastOrDefault();

            bool newFile = false;

            if (lastFileParsed == null)
            {
                // New file
                CurrentFileSize = 0;

                if (FileParams.Mode == FileMode.Open || FileParams.Mode == FileMode.Truncate)
                {
                    throw new IOException($"The file '{FileParams.Path}' not found.");
                }

                newFile = true;
            }
            else
            {
                // File exists
                if (FileParams.Mode == FileMode.CreateNew)
                {
                    throw new IOException($"The file '{FileParams.Path}' already exists.");
                }

                checked
                {
                    long sizeOfLastFile = (await UnderlayFileSystem.GetFileMetadataAsync(lastFileParsed.PhysicalFilePath, FileMetadataGetFlags.NoAlternateStream | FileMetadataGetFlags.NoSecurity, cancel)).Size;
                    sizeOfLastFile = Math.Min(sizeOfLastFile, ChunkedFileSystem.Params.MaxSinglePhysicalFileSize);
                    CurrentFileSize = lastFileParsed.FileNumber * ChunkedFileSystem.Params.MaxSinglePhysicalFileSize + sizeOfLastFile;
                }
            }

            if (FileParams.Mode == FileMode.Create || FileParams.Mode == FileMode.CreateNew || FileParams.Mode == FileMode.Truncate)
            {
                if (lastFileParsed != null)
                {
                    // Delete the files first
                    await ChunkedFileSystem.DeleteFileAsync(FileParams.Path, FileFlags.ForceClearReadOnlyOrHiddenBitsOnNeed, cancel);

                    lastFileParsed = null;
                    CurrentFileSize = 0;
                }

                newFile = true;
            }

            long currentPosition = 0;
            if (FileParams.Mode == FileMode.Append)
                currentPosition = this.CurrentFileSize;

            InitAndCheckFileSizeAndPosition(currentPosition, await GetFileSizeImplAsync(cancel), cancel);

            if (newFile)
            {
                await GetUnderlayFileObjectAsyncForLogicalPositionAsync(0, cancel);
            }
        }
        catch
        {
            throw;
        }
    }

    FileObject? InternalCurrentFileObject = null;
    string InternalCurrentFilePhysicalPath = "";

    async Task<FileObject> GetUnderlayFileObjectAsyncForLogicalPositionAsync(long logicalPosition, CancellationToken cancel)
    {
        var cursor = new Cursor(this, logicalPosition);

        var parsed = new ChunkedFileSystem.ParsedPath(ChunkedFileSystem, this.FileParams.Path, cursor.PhysicalFileNumber);

        string physicalPath = parsed.PhysicalFilePath;

        if (this.InternalCurrentFileObject == null || this.InternalCurrentFilePhysicalPath._IsDiff(physicalPath))
        {
            await this.InternalCurrentFileObject._DisposeSafeAsync();

            this.InternalCurrentFileObject = null;

            var ret = await OpenUnderlayFileObjecForLogicalPositionCoreAsync(physicalPath, cancel);

            this.InternalCurrentFilePhysicalPath = physicalPath;
            this.InternalCurrentFileObject = ret;

            return ret;
        }
        else
        {
            return this.InternalCurrentFileObject;
        }
    }

    async Task<FileObject> OpenUnderlayFileObjecForLogicalPositionCoreAsync(string physicalPath, CancellationToken cancel)
    {
        FileObject underlayFileObj;

        if (FileParams.Access.Bit(FileAccess.Write))
        {
            underlayFileObj = await ChunkedFileSystem.UnderlayFileSystem.OpenOrCreateAsync(physicalPath, cancel: cancel, flags: FileFlags.RandomAccessOnly | this.FileParams.Flags);
        }
        else
        {
            underlayFileObj = await ChunkedFileSystem.UnderlayFileSystem.OpenAsync(physicalPath, cancel: cancel, flags: FileFlags.RandomAccessOnly | this.FileParams.Flags);
        }

        return underlayFileObj;
    }

    protected override async Task CloseImplAsync()
    {
        await this.InternalCurrentFileObject._DisposeSafeAsync();
        this.InternalCurrentFileObject = null;
        this.InternalCurrentFilePhysicalPath = "";
    }

    protected override async Task FlushImplAsync(CancellationToken cancel = default)
    {
        var currentFile = await GetUnderlayFileObjectAsyncForLogicalPositionAsync(this.CurrentFileSize, cancel);

        await currentFile.FlushAsync(cancel);
    }

    protected override Task<long> GetFileSizeImplAsync(CancellationToken cancel = default)
    {
        return this.CurrentFileSize._TR();
    }

    List<Cursor> GenerateCursorList(long position, long length, bool writeMode)
    {
        checked
        {
            if (position < 0) throw new ArgumentOutOfRangeException("position");
            if (length < 0) throw new ArgumentOutOfRangeException("length");

            if (writeMode == false)
            {
                if (position > this.CurrentFileSize)
                    throw new ApplicationException("position > this.CurrentFileSize");

                if (position + length > this.CurrentFileSize)
                {
                    length = this.CurrentFileSize - position;
                }
            }

            if (length == 0)
            {
                return new List<Cursor>();
            }

            List<Cursor> ret = new List<Cursor>();

            long eof = position + length;

            while (position < eof)
            {
                Cursor cursor = new Cursor(this, position, eof - position);
                ret.Add(cursor);

                position += cursor.PhysicalDataLength;
            }

            return ret;
        }
    }

    protected override async Task<int> ReadRandomImplAsync(long position, Memory<byte> data, CancellationToken cancel = default)
    {
        checked
        {
            if (data.Length == 0) return 0;

            int totalLength = 0;

            List<Cursor> cursorList = GenerateCursorList(position, data.Length, false);

            foreach (Cursor cursor in cursorList)
            {
                bool isLast = (cursor == cursorList.Last());

                FileObject? currentFile = null;

                try
                {
                    currentFile = await GetUnderlayFileObjectAsyncForLogicalPositionAsync(cursor.LogicalPosition, cancel);
                }
                catch (FileNotFoundException) { }

                var subMemory = data.Slice((int)(cursor.LogicalPosition - position), (int)cursor.PhysicalDataLength);

                if (currentFile != null)
                {
                    int r = await currentFile.ReadRandomAsync(cursor.PhysicalPosition, subMemory, cancel);

                    Debug.Assert(r <= (int)cursor.PhysicalDataLength);

                    if (r < (int)cursor.PhysicalDataLength)
                    {
                        var zeroClearMemory = subMemory.Slice(r);
                        zeroClearMemory.Span.Fill(0);
                    }

                    totalLength += (int)cursor.PhysicalDataLength;
                }
                else
                {
                    subMemory.Span.Fill(0);

                    totalLength += (int)cursor.PhysicalDataLength;
                }
            }

            return totalLength;
        }
    }

    Cursor? lastWriteCursor = null;

    protected override async Task WriteRandomImplAsync(long position, ReadOnlyMemory<byte> data, CancellationToken cancel = default)
    {
        checked
        {
            if (data.Length == 0) return;

            List<Cursor> cursorList = GenerateCursorList(position, data.Length, true);

            if (cursorList.Count >= 1 && lastWriteCursor != null)
            {
                if (cursorList[0].PhysicalFileNumber != lastWriteCursor.PhysicalFileNumber)
                {
                    var currentFile = await GetUnderlayFileObjectAsyncForLogicalPositionAsync(lastWriteCursor.LogicalPosition, cancel);

                    await currentFile.FlushAsync();

                    lastWriteCursor = null;
                }
            }

            // Normal write
            for (int i = 0; i < cursorList.Count; i++)
            {
                Cursor cursor = cursorList[i];
                bool isPastFile = (i != cursorList.Count - 1);

                var currentFile = await GetUnderlayFileObjectAsyncForLogicalPositionAsync(cursor.LogicalPosition, cancel);
                await currentFile.WriteRandomAsync(cursor.PhysicalPosition, data.Slice((int)(cursor.LogicalPosition - position), (int)cursor.PhysicalDataLength), cancel);

                if (isPastFile)
                    await currentFile.FlushAsync(cancel);

                this.lastWriteCursor = cursor;
            }

            this.CurrentFileSize = Math.Max(this.CurrentFileSize, position + data.Length);
        }
    }

    protected override Task SetFileSizeImplAsync(long size, CancellationToken cancel = default)
    {
        if (this.CurrentFileSize == size)
        {
            return TR();
        }

        throw new CoresLibException($"this.CurrentFileSize ({this.CurrentFileSize}) != size ({size})");
    }
}

public class ChunkedFileSystemParams : FileSystemParams
{
    public long MaxSinglePhysicalFileSize { get; }
    public long MaxLogicalFileSize { get; }
    public int NumDigits { get; }
    public string SplitStr { get; }
    public long MaxFileNumber { get; }

    public FileSystem UnderlayFileSystem { get; }

    public ChunkedFileSystemParams(FileSystem underlayFileSystem, long maxSingleFileSize = -1, long logicalMaxSize = -1, string? splitStr = null, FileSystemMode mode = FileSystemMode.Default)
        : base(underlayFileSystem.PathParser, mode)
    {
        checked
        {
            if (maxSingleFileSize <= 0) maxSingleFileSize = CoresConfig.LocalChunkedFileSystemSettings.MaxSingleFileSize.Value;
            if (logicalMaxSize <= 0) logicalMaxSize = CoresConfig.LocalChunkedFileSystemSettings.LogicalMaxSize.Value;
            if (splitStr._IsEmpty()) splitStr = CoresConfig.LocalChunkedFileSystemSettings.SplitStr.Value;

            this.UnderlayFileSystem = underlayFileSystem;
            this.SplitStr = splitStr._NonNullTrim()._FilledOrDefault(".part");
            this.MaxSinglePhysicalFileSize = Math.Min(Math.Max(maxSingleFileSize, 1), int.MaxValue);
            this.MaxLogicalFileSize = logicalMaxSize;

            long i = (this.MaxLogicalFileSize / this.MaxSinglePhysicalFileSize);

            i = Math.Max(i, 1);
            i = (int)Math.Log10(i);
            NumDigits = (int)i;

            this.MaxFileNumber = Str.MakeCharArray('9', NumDigits)._ToLong();
            this.MaxLogicalFileSize = (MaxSinglePhysicalFileSize * (this.MaxFileNumber + 1)) - 1;
        }
    }
}

public class ChunkedFileSystem : FileSystem
{
    public class ParsedPath
    {
        public string DirectoryPath { get; }
        public string OriginalFileName { get; }
        public long FileNumber { get; }
        public string PhysicalFilePath { get; }
        public string LogicalFilePath { get; }

        string? _PhysicalFileNameCache = null;
        string? _LogicalFileName = null;
        public string PhysicalFileName => _PhysicalFileNameCache ?? (_PhysicalFileNameCache = ChunkedFileSystem.PathParser.GetFileName(this.PhysicalFilePath));
        public string LogicalFileName => _LogicalFileName ?? (_LogicalFileName = ChunkedFileSystem.PathParser.GetFileName(this.LogicalFilePath));

        public FileSystemEntity? PhysicalEntity { get; }

        readonly ChunkedFileSystem ChunkedFileSystem;

        public ParsedPath(ChunkedFileSystem fs, string logicalFilePath, long fileNumber)
        {
            this.ChunkedFileSystem = fs;

            this.LogicalFilePath = logicalFilePath;

            string dir = fs.PathParser.GetDirectoryName(logicalFilePath);
            string filename = fs.PathParser.GetFileName(logicalFilePath);

            this.DirectoryPath = dir;
            this.OriginalFileName = filename;
            this.FileNumber = fileNumber;
            this.PhysicalFilePath = GeneratePhysicalPath();
        }

        public ParsedPath(ChunkedFileSystem fs, string physicalFilePath, FileSystemEntity? physicalEntity = null)
        {
            this.ChunkedFileSystem = fs;

            this.PhysicalEntity = physicalEntity;
            this.PhysicalFilePath = physicalFilePath;

            string dir = fs.PathParser.GetDirectoryName(physicalFilePath);
            string fn = fs.PathParser.GetFileName(physicalFilePath);

            bool isDigitedFile = false;

            if (fn.Length >= (fs.Params.SplitStr.Length + fs.Params.NumDigits + 1))
            {
                // originalname.part000000001
                string partStr = fn.Substring(fn.Length - (fs.Params.SplitStr.Length + fs.Params.NumDigits));

                string partStr1 = partStr.Substring(0, fs.Params.SplitStr.Length);
                string partStr2 = partStr.Substring(fs.Params.SplitStr.Length);

                if (partStr1._IsSame(fs.Params.SplitStr, fs.PathParser.PathStringComparison))
                {
                    long n = partStr2._ToLong();
                    if (n >= 1)
                    {
                        if (partStr2._IsNumber())
                        {
                            this.FileNumber = n;

                            isDigitedFile = true;

                            this.OriginalFileName = fn.Substring(0, fn.Length - (fs.Params.SplitStr.Length + fs.Params.NumDigits));
                        }
                    }
                }
            }

            if (isDigitedFile == false)
            {
                this.FileNumber = 0;
                this.OriginalFileName = fn;
            }

            this.DirectoryPath = dir;

            this.OriginalFileName._MarkNotNull();

            this.LogicalFilePath = fs.PathParser.Combine(this.DirectoryPath, this.OriginalFileName!);
        }

        public string GeneratePhysicalPath(long? fileNumberOverwrite = null)
        {
            long fileNumber = fileNumberOverwrite ?? FileNumber;
            string fileNumberStr = fileNumber.ToString($"D{ChunkedFileSystem.Params.NumDigits}");
            Debug.Assert(fileNumberStr.Length == ChunkedFileSystem.Params.NumDigits);

            string filename;

            if (fileNumber == 0)
            {
                filename = OriginalFileName;
            }
            else
            {
                filename = OriginalFileName + ChunkedFileSystem.Params.SplitStr + fileNumberStr;
            }

            return ChunkedFileSystem.PathParser.Combine(DirectoryPath, filename);
        }
    }

    public static ChunkedFileSystem Local { get; private set; } = null!;
    public static ChunkedFileSystem LocalUtf8 { get; private set; } = null!;

    public static StaticModule Module { get; } = new StaticModule(ModuleInit, ModuleFree);

    static void ModuleInit()
    {
        Local = new ChunkedFileSystem(new ChunkedFileSystemParams(LocalFileSystem.Local));

        LocalUtf8 = new ChunkedFileSystem(new ChunkedFileSystemParams(LocalFileSystem.LocalUtf8));
    }

    static void ModuleFree()
    {
        LocalUtf8._DisposeSafe();
        LocalUtf8 = null!;

        Local._DisposeSafe();
        Local = null!;
    }



    public FileSystem UnderlayFileSystem { get; }
    public new ChunkedFileSystemParams Params => (ChunkedFileSystemParams)base.Params;

    AsyncLock AsyncLockObj = new AsyncLock();

    public ChunkedFileSystem(ChunkedFileSystemParams param) : base(param)
    {
        this.UnderlayFileSystem = param.UnderlayFileSystem;
    }

    protected override Task<string> NormalizePathImplAsync(string path, CancellationToken cancel = default)
        => this.UnderlayFileSystem.NormalizePathAsync(path, cancel: cancel);

    protected override async Task<FileObject> CreateFileImplAsync(FileParameters option, CancellationToken cancel = default)
    {
        string fileName = this.PathParser.GetFileName(option.Path);
        if (fileName.IndexOf(Params.SplitStr) != -1)
            throw new ApplicationException($"The original filename '{fileName}' contains '{Params.SplitStr}'.");

        await using (CreatePerTaskCancellationToken(out CancellationToken operationCancel, cancel))
        {
            using (await AsyncLockObj.LockWithAwait(operationCancel))
            {
                cancel.ThrowIfCancellationRequested();

                ParsedPath[] relatedFiles = await GetPhysicalFileStateInternalAsync(option.Path, operationCancel);

                return await ChunkedFileObject.CreateFileAsync(this, option, relatedFiles, operationCancel);
            }
        }
    }

    public async Task<ParsedPath[]> GetPhysicalFileStateInternalAsync(string logicalFilePath, CancellationToken cancel)
    {
        List<ParsedPath> ret = new List<ParsedPath>();

        ParsedPath parsed = new ParsedPath(this, logicalFilePath, 0);

        FileSystemEntity[] dirEntities = await UnderlayFileSystem.EnumDirectoryAsync(parsed.DirectoryPath, false, EnumDirectoryFlags.NoGetPhysicalSize, null, cancel);

        var relatedFiles = dirEntities.Where(x => x.IsDirectory == false);
        foreach (var f in relatedFiles)
        {
            if (f.Name._IsSame(parsed.OriginalFileName) || f.Name.StartsWith(parsed.OriginalFileName + this.Params.SplitStr, PathParser.PathStringComparison))
            {
                try
                {
                    ParsedPath parsedForFile = new ParsedPath(this, f.FullPath, f);
                    if (parsed.LogicalFilePath._IsSame(parsedForFile.LogicalFilePath, PathParser.PathStringComparison))
                    {
                        ret.Add(parsedForFile);
                    }
                }
                catch { }
            }
        }

        ret.Sort((x, y) => x.FileNumber.CompareTo(y.FileNumber));

        return ret.ToArray();
    }

    protected override async Task<FileSystemEntity[]> EnumDirectoryImplAsync(string directoryPath, EnumDirectoryFlags flags, string wildcard, CancellationToken cancel = default)
    {
        checked
        {
            FileSystemEntity[] dirEntities = await UnderlayFileSystem.EnumDirectoryAsync(directoryPath, false, flags, null, cancel);

            var filesList = dirEntities.Where(x => x.IsDirectory == false);

            var sortedRelatedFiles = filesList.ToList();
            sortedRelatedFiles.Sort((x, y) => x.Name._Cmp(y.Name, PathParser.PathStringComparison));
            sortedRelatedFiles.Reverse();

            Dictionary<string, FileSystemEntity> parsedFileDictionaly = new Dictionary<string, FileSystemEntity>(PathParser.PathStringComparer);

            List<ParsedPath> parsedList = new List<ParsedPath>();

            foreach (FileSystemEntity f in sortedRelatedFiles)
            {
                try
                {
                    // Split files
                    ParsedPath parsed = new ParsedPath(this, f.FullPath, f);

                    parsedList.Add(parsed);

                }
                catch (Exception ex)
                {
                    ex._Error();
                }
            }

            foreach (var parsed in parsedList.OrderByDescending(x => x.FileNumber))
            {
                var f = parsed.PhysicalEntity!;

                if (parsedFileDictionaly.ContainsKey(parsed.LogicalFileName) == false)
                {
                    FileSystemEntity newFileEntity = new FileSystemEntity(
                        fullPath: parsed.LogicalFilePath,
                        name: PathParser.GetFileName(parsed.LogicalFileName),
                        size: f.Size + parsed.FileNumber * Params.MaxSinglePhysicalFileSize,
                        physicalSize: f.PhysicalSize,
                        attributes: f.Attributes,
                        creationTime: f.CreationTime,
                        lastWriteTime: f.LastWriteTime,
                        lastAccessTime: f.LastAccessTime
                        );

                    parsedFileDictionaly.Add(parsed.LogicalFileName, newFileEntity);
                }
                else
                {
                    var fileEntity = parsedFileDictionaly[parsed.LogicalFileName];

                    fileEntity.PhysicalSize += f.PhysicalSize;

                    if (fileEntity.CreationTime > f.CreationTime) fileEntity.CreationTime = f.CreationTime;
                    if (fileEntity.LastWriteTime < f.LastWriteTime) fileEntity.LastWriteTime = f.LastWriteTime;
                    if (fileEntity.LastAccessTime < f.LastAccessTime) fileEntity.LastAccessTime = f.LastAccessTime;
                }
            }

            var logicalFiles = parsedFileDictionaly.Values;//.Where(x => normalFileHashSet.Contains(x.Name) == false);

            var retList = dirEntities
                .Where(x => x.IsDirectory && x.IsCurrentDirectory == false)
                .Concat(logicalFiles)
                .OrderByDescending(x => x.IsDirectory)
                .ThenBy(x => x.Name);

            return dirEntities.Where(x => x.IsCurrentDirectory)
                .Concat(retList).ToArray();
        }
    }

    protected override Task CreateDirectoryImplAsync(string directoryPath, FileFlags flags = FileFlags.None, CancellationToken cancel = default)
        => UnderlayFileSystem.CreateDirectoryAsync(directoryPath, flags, cancel);

    protected override Task DeleteDirectoryImplAsync(string directoryPath, bool recursive, CancellationToken cancel = default)
        => UnderlayFileSystem.DeleteDirectoryAsync(directoryPath, recursive);

    public bool TryParseOriginalPath(string physicalPath, [NotNullWhen(true)] out ParsedPath? parsed)
    {
        try
        {
            parsed = new ParsedPath(this, physicalPath);
            return true;
        }
        catch
        {
            parsed = null;
            return false;
        }
    }

    protected override async Task<bool> IsFileExistsImplAsync(string path, CancellationToken cancel = default)
    {
        ChunkedFileSystem.ParsedPath[] physicalFiles = await GetPhysicalFileStateInternalAsync(path, cancel);

        var lastFileParsed = physicalFiles.OrderBy(x => x.FileNumber).LastOrDefault();

        return lastFileParsed != null;
    }

    protected override Task<bool> IsDirectoryExistsImplAsync(string path, CancellationToken cancel = default)
        => UnderlayFileSystem.IsDirectoryExistsAsync(path, cancel);

    protected override async Task<FileMetadata> GetFileMetadataImplAsync(string path, FileMetadataGetFlags flags = FileMetadataGetFlags.DefaultAll, CancellationToken cancel = default)
    {
        ChunkedFileSystem.ParsedPath[] physicalFiles = await GetPhysicalFileStateInternalAsync(path, cancel);

        var lastFileParsed = physicalFiles.OrderBy(x => x.FileNumber).LastOrDefault();

        if (lastFileParsed == null)
        {
            // File not found
            throw new IOException($"The file '{path}' not found.");
        }
        else
        {
            // Chunked file exists
            checked
            {
                FileMetadata ret = await UnderlayFileSystem.GetFileMetadataAsync(lastFileParsed.PhysicalFilePath, flags | FileMetadataGetFlags.NoSecurity | FileMetadataGetFlags.NoAlternateStream, cancel);
                long sizeOfLastFile = ret.Size;
                sizeOfLastFile = Math.Min(sizeOfLastFile, Params.MaxSinglePhysicalFileSize);

                long currentFileSize = lastFileParsed.FileNumber * Params.MaxSinglePhysicalFileSize + sizeOfLastFile;

                ret.Size = currentFileSize;
                ret.PhysicalSize = physicalFiles.Sum(x => x.PhysicalEntity!.PhysicalSize);
                ret.CreationTime = physicalFiles.Min(x => x.PhysicalEntity!.CreationTime);
                ret.LastWriteTime = physicalFiles.Max(x => x.PhysicalEntity!.LastWriteTime);
                ret.LastAccessTime = physicalFiles.Max(x => x.PhysicalEntity!.LastAccessTime);

                return ret;
            }
        }
    }

    protected override async Task DeleteFileImplAsync(string path, FileFlags flags = FileFlags.None, CancellationToken cancel = default)
    {
        ChunkedFileSystem.ParsedPath[] physicalFiles = await GetPhysicalFileStateInternalAsync(path, cancel);

        if (physicalFiles._IsEmpty())
        {
            // File not found
            return;
        }

        foreach (ChunkedFileSystem.ParsedPath deleteFile in physicalFiles.OrderByDescending(x => x.PhysicalFilePath))
        {
            await UnderlayFileSystem.DeleteFileAsync(deleteFile.PhysicalFilePath, FileFlags.ForceClearReadOnlyOrHiddenBitsOnNeed, cancel);
        }
    }

    protected override async Task SetFileMetadataImplAsync(string path, FileMetadata metadata, CancellationToken cancel = default)
    {
        ChunkedFileSystem.ParsedPath[] physicalFiles = await GetPhysicalFileStateInternalAsync(path, cancel);

        if (physicalFiles._IsEmpty())
        {
            // File not found
            throw new IOException($"The file '{path}' not found.");
        }

        Exception? exception = null;

        foreach (var file in physicalFiles.OrderBy(x => x.PhysicalFilePath, PathParser.PathStringComparer))
        {
            cancel.ThrowIfCancellationRequested();

            try
            {
                await UnderlayFileSystem.SetFileMetadataAsync(file.PhysicalFilePath, metadata, cancel);
            }
            catch (Exception ex)
            {
                if (exception == null)
                    exception = ex;
            }
        }

        if (exception != null)
            throw exception;
    }

    protected override Task SetDirectoryMetadataImplAsync(string path, FileMetadata metadata, CancellationToken cancel = default)
        => UnderlayFileSystem.SetDirectoryMetadataAsync(path, metadata, cancel);

    protected override Task<FileMetadata> GetDirectoryMetadataImplAsync(string path, FileMetadataGetFlags flags = FileMetadataGetFlags.DefaultAll, CancellationToken cancel = default)
        => UnderlayFileSystem.GetDirectoryMetadataAsync(path, flags | FileMetadataGetFlags.NoAlternateStream | FileMetadataGetFlags.NoSecurity, cancel);

    protected override Task MoveFileImplAsync(string srcPath, string destPath, CancellationToken cancel = default)
        => throw new NotImplementedException();

    protected override Task MoveDirectoryImplAsync(string srcPath, string destPath, CancellationToken cancel = default)
        => throw new NotImplementedException();

    protected override IFileProvider CreateFileProviderForWatchImpl(string root) => UnderlayFileSystem._CreateFileProviderForWatchInternal(EnsureInternal.Yes, root);
}
