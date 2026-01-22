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
using System.Buffers;
using System.Diagnostics;
using System.Security.Cryptography;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using System.Text;
using System.Diagnostics.CodeAnalysis;

namespace IPA.Cores.Basic;

[Flags]
public enum ReadParseFlags
{
    None = 0,
    ForceInitOnParseError = 1,
    ForceRewrite = 2,

    Both = ForceInitOnParseError | ForceRewrite,
}

public abstract partial class FileSystem
{
    readonly NamedAsyncLocks ConcurrentAppendLock = new NamedAsyncLocks(StrComparer.IgnoreCaseComparer);

    public async Task<string> GenerateUniqueTempFilePathAsync(string fileNameBase = "", string ext = "", string dirName = "", FileFlags createDirFlags = FileFlags.None, CancellationToken cancel = default)
    {
        if (dirName._IsEmpty())
        {
            if (this is LocalFileSystem)
            {
                dirName = Env.MyLocalTempDir;
            }
            else
            {
                throw new CoresLibException(nameof(dirName) + " is empty.");
            }
        }

        ext = ext._NonNull().Trim('.');
        if (ext._IsEmpty()) ext = "tmp";
        ext = PPWin.MakeSafeFileName(ext, true, true, true);
        if (ext._IsEmpty()) ext = "tmp";

        if (fileNameBase._IsEmpty())
        {
            fileNameBase = "temp_";
        }
        else
        {
            fileNameBase = PPWin.MakeSafeFileName(PPWin.GetFileNameWithoutExtension(fileNameBase), true, true, true);
        }

        var now = DtNow;
        string dtStr = now._ToYymmddStr(yearTwoDigits: true) + "_" + now._ToHhmmssStr(millisecs: true);

        await this.CreateDirectoryAsync(dirName, createDirFlags, cancel);

        while (true)
        {
            string rand = Str.GenRandStr().Substring(0, 16).ToLowerInvariant();
            string generatedFileName = dtStr + "_" + fileNameBase + "_" + rand + "." + ext;
            string generatedFullPath = PathParser.Combine(dirName, generatedFileName);

            if (await this.IsFileExistsAsync(generatedFullPath, cancel) == false)
            {
                return generatedFullPath;
            }
        }
    }

    // 古いログファイルを削除する
    public async Task DeleteOldFilesAsync(string dirName, string extensionList, long maxTotalSize, CancellationToken cancel = default(CancellationToken))
    {
        try
        {
            // 列挙
            var list = (await this.EnumDirectoryAsync(dirName, true, cancel: cancel)).Where(x => x.IsFile && x.Name._IsExtensionMatch(extensionList)).ToList();

            // 更新日時でソート
            list.Sort((x, y) => (x.LastWriteTime.CompareTo(y.LastWriteTime)));

            // 合計サイズを取得
            long totlSize = 0;
            foreach (var v in list) if (v.IsDirectory == false) totlSize += v.Size;

            List<FileSystemEntity> delete_files = new List<FileSystemEntity>();

            // 削除をしていきます
            long deleteSize = totlSize - maxTotalSize;
            foreach (var v in list)
            {
                if (deleteSize <= 0) break;
                if (v.IsFile)
                {
                    try
                    {
                        await this.DeleteFileAsync(v.FullPath);

                        //Dbg.WriteLine($"OldFileEraser: File '{v.FullPath}' deleted.");

                        // 削除に成功したら delete_size を減じる
                        deleteSize -= v.Size;

                        // 親ディレクトリが削除できる場合は削除する
                        // (検索した対象ディレクトリは削除しない)
                        string parentDir = this.PathParser.GetDirectoryName(v.FullPath);

                        string parentDirRelative = this.PathParser.GetRelativeDirectoryName(parentDir, dirName);

                        if (parentDirRelative._IsSamei("./") == false || parentDirRelative._IsSamei(@".\") == false)
                        {
                            await this.DeleteDirectoryAsync(parentDir);

                            //Dbg.WriteLine($"OldFileEraser: Directory '{v.FullPath._GetDirectoryName()}' deleted.");
                        }
                    }
                    catch
                    {
                    }
                }
            }
        }
        catch
        {
        }
    }

    // Git コミット ID を取得する
    public async Task<string> GetCurrentGitCommitIdAsync(string targetDir, CancellationToken cancel = default)
    {
        string tmpPath = targetDir;

        // Get the HEAD contents
        while (true)
        {
            string headFilename = this.PathParser.Combine(tmpPath, ".git/HEAD");

            try
            {
                string headContents = await this.ReadStringFromFileAsync(headFilename, cancel: cancel);
                foreach (string line in headContents._GetLines())
                {
                    if (Str.TryNormalizeGitCommitId(line, out string commitId))
                    {
                        return commitId;
                    }

                    if (line._GetKeyAndValue(out string key, out string value, ":"))
                    {
                        if (key._IsSamei("ref"))
                        {
                            string refFilename = value.Trim();
                            string refFullPath = Path.Combine(Path.GetDirectoryName(headFilename)._NonNull(), refFilename);

                            return (await this.ReadStringFromFileAsync(refFullPath, cancel: cancel))._GetLines().Where(x => x._IsFilled()).Single();
                        }
                    }
                }
            }
            catch { }

            string? parentPath = this.PathParser.GetDirectoryName(tmpPath);
            if (tmpPath._IsSamei(parentPath)) return "";

            if (parentPath._IsEmpty()) return "";

            tmpPath = parentPath;
        }
    }

    readonly NamedAsyncLocks LockFor_CheckFreeDiskSpaceByTestFileAsync = new();

    // 指定された長さのファイルを保存してみることによって、ディスクに空き容量があることを確認する
    public async Task<bool> CheckFreeDiskSpaceByTestFileAsync(string dirPath, int size, CancellationToken cancel = default)
    {
        if (size < 0) throw new CoresLibException("size < 0");

        string machineHex = Secure.HashSHA1(Env.MachineName.ToLowerInvariant()._GetBytes_UTF8()).AsSpan(0, 8)._GetHexString().ToLowerInvariant();

        string filePath = this.PathParser.Combine(dirPath, $"_space_check_{machineHex}_{size}bytes.dat");

        using (await LockFor_CheckFreeDiskSpaceByTestFileAsync.LockWithAwait(filePath, cancel))
        {
            bool ret = false;

            try
            {
                await this.DeleteFileAsync(filePath, cancel: cancel);
            }
            catch { }

            try
            {
                await this.CreateDirectoryAsync(dirPath, cancel: cancel);
            }
            catch { }

            try
            {
                await this.DeleteFileAsync(filePath, cancel: cancel);
            }
            catch { }

            SeedBasedRandomGenerator rand = new SeedBasedRandomGenerator(Str.GenRandStr());

            try
            {
                await using (var file = await this.CreateAsync(filePath, flags: FileFlags.AutoCreateDirectory, cancel: cancel))
                {
                    int currentPos = 0;

                    while (currentPos < size)
                    {
                        cancel.ThrowIfCancellationRequested();

                        int thisTimeSize = size - currentPos;

                        thisTimeSize = Math.Min(thisTimeSize, Consts.Numbers.DefaultLargeBufferSize);

                        await file.WriteAsync(rand.GetBytes(thisTimeSize), cancel: cancel);

                        currentPos += thisTimeSize;
                    }

                    await file.FlushAsync(cancel);

                    ret = true;
                }
            }
            catch
            {
            }
            finally
            {
                try
                {
                    await this.DeleteFileAsync(filePath, cancel: cancel);
                }
                catch { }
            }

            return ret;
        }
    }

    public async Task<int> ConcurrentSafeAppendDataToFileAsync(string path, ReadOnlyMemory<byte> data, FileFlags additionalFileFlags = FileFlags.None, CancellationToken cancel = default)
    {
        using (await ConcurrentAppendLock.LockWithAwait(path, cancel))
        {
            return await this.AppendDataToFileAsync(path, data, flags: FileFlags.AutoCreateDirectory | additionalFileFlags, cancel);
        }
    }

    public async Task<bool> TryAddOrRemoveAttributeFromExistingFileAsync(string path, FileAttributes attributesToAdd = 0, FileAttributes attributesToRemove = 0, CancellationToken cancel = default)
    {
        try
        {
            if (File.Exists(path) == false)
                return false;

            var existingFileMetadata = await this.GetFileMetadataAsync(path, FileMetadataGetFlags.NoAlternateStream | FileMetadataGetFlags.NoSecurity | FileMetadataGetFlags.NoTimes, cancel);
            var currentAttributes = existingFileMetadata.Attributes ?? 0;
            if (currentAttributes.Bit(FileAttributes.Hidden) || currentAttributes.Bit(FileAttributes.ReadOnly) || attributesToAdd.Bit(FileAttributes.Hidden) || attributesToAdd.Bit(FileAttributes.ReadOnly))
            {
                var newAttributes = (currentAttributes & ~(attributesToRemove)) | attributesToAdd;
                if (currentAttributes != newAttributes)
                {
                    try
                    {
                        await this.SetFileMetadataAsync(path, new FileMetadata(false, attributes: newAttributes), cancel);

                        return true;
                    }
                    catch { }
                }
                else
                {
                    return true;
                }
            }
        }
        catch
        {
        }

        return false;
    }

    public async Task<bool> TryAddOrRemoveAttributeFromExistingDirAsync(string path, FileAttributes attributesToAdd = 0, FileAttributes attributesToRemove = 0, CancellationToken cancel = default)
    {
        try
        {
            if (Directory.Exists(path) == false)
                return false;

            var existingFileMetadata = await this.GetDirectoryMetadataAsync(path, FileMetadataGetFlags.NoAlternateStream | FileMetadataGetFlags.NoSecurity | FileMetadataGetFlags.NoTimes, cancel);
            var currentAttributes = existingFileMetadata.Attributes ?? 0;
            if (currentAttributes.Bit(FileAttributes.Hidden) || currentAttributes.Bit(FileAttributes.ReadOnly) || attributesToAdd.Bit(FileAttributes.Hidden) || attributesToAdd.Bit(FileAttributes.ReadOnly))
            {
                var newAttributes = (currentAttributes & ~(attributesToRemove)) | attributesToAdd;
                if (currentAttributes != newAttributes)
                {
                    try
                    {
                        await this.SetDirectoryMetadataAsync(path, new FileMetadata(true, attributes: newAttributes), cancel);

                        return true;
                    }
                    catch { }
                }
                else
                {
                    return true;
                }
            }
        }
        catch
        {
        }

        return false;
    }

    public async Task<long> WriteHugeMemoryBufferToFileAsync(string path, HugeMemoryBuffer<byte> hugeMemoryBuffer, FileFlags flags = FileFlags.None, bool doNotOverwrite = false, CancellationToken cancel = default)
    {
        if (flags.Bit(FileFlags.WriteOnlyIfChanged))
        {
            if (hugeMemoryBuffer.Length >= int.MaxValue)
            {
                throw new ArgumentException(nameof(flags));
            }
            else
            {
                return await this.WriteDataToFileAsync(path, hugeMemoryBuffer.ToArray(), flags, doNotOverwrite, cancel);
            }
        }

        long size = hugeMemoryBuffer.LongLength;

        await using (var file = await CreateAsync(path, false, flags & ~FileFlags.WriteOnlyIfChanged, doNotOverwrite, cancel))
        {
            try
            {
                IReadOnlyList<SparseChunk<byte>> dataList = hugeMemoryBuffer.ReadRandomFast(0, size, out long readSize, false);

                foreach (SparseChunk<byte> chunk in dataList)
                {
                    await file.WriteRandomAsync(chunk.Offset, chunk.GetMemoryOrGenerateSparse(), cancel);
                }
            }
            finally
            {
                await file.CloseAsync();
            }
        }

        return size;
    }
    public long WriteHugeMemoryBufferToFile(string path, HugeMemoryBuffer<byte> hugeMemoryBuffer, FileFlags flags = FileFlags.None, bool doNotOverwrite = false, CancellationToken cancel = default)
        => WriteHugeMemoryBufferToFileAsync(path, hugeMemoryBuffer, flags, doNotOverwrite, cancel)._GetResult();

    public async Task<int> WriteDataToFileAsync(string path, ReadOnlyMemory<byte> srcMemory, FileFlags flags = FileFlags.None, bool doNotOverwrite = false, CancellationToken cancel = default, bool overwriteAndTruncate = false, RefBool? changed = null)
    {
        changed?.Set(false);

        if (flags.Bit(FileFlags.WriteOnlyIfChanged))
        {
            try
            {
                if (await IsFileExistsAsync(path, cancel))
                {
                    Memory<byte> existingData = await ReadDataFromFileAsync(path, srcMemory.Length, flags, cancel);
                    if (existingData.Length == srcMemory.Length && existingData.Span.SequenceEqual(srcMemory.Span))
                    {
                        return srcMemory.Length;
                    }
                }
            }
            catch { }
        }

        if (overwriteAndTruncate && await IsFileExistsAsync(path, cancel))
        {
            await using (var file = await OpenAsync(path, true, false, false, flags & ~FileFlags.WriteOnlyIfChanged, cancel))
            {
                try
                {
                    await file.SeekToBeginAsync(cancel);

                    long fileSize = await file.GetFileSizeAsync(cancel: cancel);

                    changed?.Set(true);

                    await file.WriteAsync(srcMemory, cancel);

                    if (fileSize > srcMemory.Length)
                    {
                        await file.SetFileSizeAsync(srcMemory.Length, cancel);
                    }

                    return srcMemory.Length;
                }
                finally
                {
                    await file.CloseAsync();
                }
            }
        }
        else
        {
            await using (var file = await CreateAsync(path, false, flags & ~FileFlags.WriteOnlyIfChanged, doNotOverwrite, cancel))
            {
                changed?.Set(true);

                try
                {
                    await file.WriteAsync(srcMemory, cancel);
                    return srcMemory.Length;
                }
                finally
                {
                    await file.CloseAsync();
                }
            }
        }
    }
    public int WriteDataToFile(string path, ReadOnlyMemory<byte> data, FileFlags flags = FileFlags.None, bool doNotOverwrite = false, CancellationToken cancel = default, bool overwriteAndTruncate = false, RefBool? changed = null)
        => WriteDataToFileAsync(path, data, flags, doNotOverwrite, cancel, overwriteAndTruncate, changed)._GetResult();

    public Task<int> WriteStringToFileAsync(string path, string srcString, FileFlags flags = FileFlags.None, bool doNotOverwrite = false, Encoding? encoding = null, bool writeBom = false, CancellationToken cancel = default, bool overwriteAndTruncate = false, RefBool? changed = null)
    {
        checked
        {
            if (encoding == null) encoding = Str.Utf8Encoding;
            MemoryBuffer<byte> buf = new MemoryBuffer<byte>();

            ReadOnlySpan<byte> bomSpan = default;

            if (writeBom)
                bomSpan = Str.GetBOMSpan(encoding);

            buf.Write(bomSpan);

            int sizeReserved = srcString.Length * 4 + 128;
            int encodedSize = encoding.GetBytes(srcString, buf.Walk(sizeReserved));
            buf.SetLength(bomSpan.Length + encodedSize);

            return WriteDataToFileAsync(path, buf.Memory, flags, doNotOverwrite, cancel, overwriteAndTruncate, changed);
        }
    }
    public int WriteStringToFile(string path, string srcString, FileFlags flags = FileFlags.None, bool doNotOverwrite = false, Encoding? encoding = null, bool writeBom = false, CancellationToken cancel = default, bool overwriteAndTruncate = false, RefBool? changed = null)
        => WriteStringToFileAsync(path, srcString, flags, doNotOverwrite, encoding, writeBom, cancel, overwriteAndTruncate, changed)._GetResult();


    public Task<int> WriteStringToFileEncryptedAsync(string path, string srcString, string password, FileFlags flags = FileFlags.None, bool doNotOverwrite = false, Encoding? encoding = null, bool writeBom = false, CancellationToken cancel = default)
    {
        checked
        {
            if (encoding == null) encoding = Str.Utf8Encoding;
            MemoryBuffer<byte> buf = new MemoryBuffer<byte>();

            ReadOnlySpan<byte> bomSpan = default;

            if (writeBom)
                bomSpan = Str.GetBOMSpan(encoding);

            buf.Write(bomSpan);

            int sizeReserved = srcString.Length * 4 + 128;
            int encodedSize = encoding.GetBytes(srcString, buf.Walk(sizeReserved));
            buf.SetLength(bomSpan.Length + encodedSize);

            return WriteDataToFileEncryptedAsync(path, buf.Memory, password, flags, doNotOverwrite, cancel);
        }
    }
    public int WriteStringToFileEncrypted(string path, string srcString, string password, FileFlags flags = FileFlags.None, bool doNotOverwrite = false, Encoding? encoding = null, bool writeBom = false, CancellationToken cancel = default)
        => WriteStringToFileEncryptedAsync(path, srcString, password, flags, doNotOverwrite, encoding, writeBom, cancel)._GetResult();


    public Task<int> AppendStringToFileAsync(string path, string srcString, FileFlags flags = FileFlags.None, Encoding? encoding = null, bool writeBom = false, CancellationToken cancel = default)
    {
        checked
        {
            if (encoding == null) encoding = Str.Utf8Encoding;
            MemoryBuffer<byte> buf = new MemoryBuffer<byte>();

            ReadOnlyMemory<byte> bomSpan = default;

            if (writeBom)
                bomSpan = Str.GetBOM(encoding);

            int sizeReserved = srcString.Length * 4 + 128;
            int encodedSize = encoding.GetBytes(srcString, buf.Walk(sizeReserved));
            buf.SetLength(encodedSize);

            return AppendDataToFileAsync(path, buf.Memory, flags, cancel, bomSpan);
        }
    }
    public int AppendStringToFile(string path, string srcString, FileFlags flags = FileFlags.None, Encoding? encoding = null, bool writeBom = false, CancellationToken cancel = default)
        => AppendStringToFileAsync(path, srcString, flags, encoding, writeBom, cancel)._GetResult();

    public async Task<int> AppendDataToFileAsync(string path, ReadOnlyMemory<byte> srcMemory, FileFlags flags = FileFlags.None, CancellationToken cancel = default, ReadOnlyMemory<byte> prefixDataForNewFile = default)
    {
        await using (var file = await OpenOrCreateAppendAsync(path, false, flags, cancel))
        {
            try
            {
                if (file.Size == 0)
                {
                    await file.WriteAsync(prefixDataForNewFile, cancel);
                }

                await file.WriteAsync(srcMemory, cancel);

                return checked(srcMemory.Length + prefixDataForNewFile.Length);
            }
            finally
            {
                await file.CloseAsync();
            }
        }
    }
    public int AppendDataToFile(string path, ReadOnlyMemory<byte> srcMemory, FileFlags flags = FileFlags.None, CancellationToken cancel = default, ReadOnlyMemory<byte> prefixDataForNewFile = default)
        => AppendDataToFileAsync(path, srcMemory, flags, cancel, prefixDataForNewFile)._GetResult();

    public async Task<T> ReadAndParseDataFileAsync<T>(string path, ReadParseFlags readParseFlags, Func<ReadOnlyMemory<byte>, T> parseProc, Func<ReadOnlyMemory<byte>> createProc, Func<T, ReadOnlyMemory<byte>>? serializeProc = null, int maxSize = int.MaxValue, FileFlags flags = FileFlags.None, CancellationToken cancel = default)
    {
        if (parseProc == null || createProc == null) throw new ArgumentNullException();

        bool forceInitMode = false;

        L_RETRY:
        if (forceInitMode || (await this.IsFileExistsAsync(path, cancel)) == false)
        {
            ReadOnlyMemory<byte> initialData = createProc();

            T ret = parseProc(initialData);

            await this.WriteDataToFileAsync(path, initialData, flags | FileFlags.WriteOnlyIfChanged, false, cancel);

            return ret;
        }
        else
        {
            ReadOnlyMemory<byte> existingData = await this.ReadDataFromFileAsync(path, maxSize, flags, cancel);

            try
            {
                T ret = parseProc(existingData);

                if (readParseFlags.Bit(ReadParseFlags.ForceRewrite) && serializeProc != null)
                {
                    try
                    {
                        await this.WriteDataToFileAsync(path, serializeProc(ret), flags | FileFlags.WriteOnlyIfChanged, false, cancel);
                    }
                    catch { }
                }

                return ret;
            }
            catch
            {
                if (readParseFlags.Bit(ReadParseFlags.ForceInitOnParseError) == false) throw;

                forceInitMode = true;

                goto L_RETRY;
            }
        }
    }
    public T ReadAndParseDataFile<T>(string path, ReadParseFlags readParseFlags, Func<ReadOnlyMemory<byte>, T> parseProc, Func<ReadOnlyMemory<byte>> createProc, Func<T, ReadOnlyMemory<byte>>? serializeProc = null, int maxSize = int.MaxValue, FileFlags flags = FileFlags.None, CancellationToken cancel = default)
        => ReadAndParseDataFileAsync(path, readParseFlags, parseProc, createProc, serializeProc, maxSize, flags, cancel)._GetResult();

    public async Task<int> ReadDataFromFileAsync(string path, Memory<byte> destMemory, FileFlags flags = FileFlags.None, CancellationToken cancel = default)
    {
        await using (var file = await OpenAsync(path, false, false, false, flags, cancel))
        {
            try
            {
                return await file.ReadAsync(destMemory, cancel);
            }
            finally
            {
                await file.CloseAsync();
            }
        }
    }
    public int ReadDataFromFile(string path, Memory<byte> destMemory, FileFlags flags = FileFlags.None, CancellationToken cancel = default)
        => ReadDataFromFileAsync(path, destMemory, flags, cancel)._GetResult();

    SyncCache<string, byte[]> FullBodyReadDataFromFileCache = new SyncCache<string, byte[]>(0);

    public async Task<Memory<byte>> ReadDataFromFileAsync(string path, int maxSize = int.MaxValue, FileFlags flags = FileFlags.None, CancellationToken cancel = default)
    {
        if (flags.Bit(FileFlags.ReadDataFileCache))
        {
            var flags2 = flags.BitRemove(FileFlags.ReadDataFileCache);

            string cacheKey = $"{path}: {maxSize}: {(ulong)flags2}";

            byte[]? cachedData = FullBodyReadDataFromFileCache.Get(cacheKey);
            if (cachedData != null)
            {
                return cachedData.ToArray();
            }

            var tmp = await ReadDataFromFileAsync(path, maxSize, flags2, cancel);

            FullBodyReadDataFromFileCache.Set(cacheKey, tmp.ToArray());

            return tmp;
        }

        await using (var file = await OpenAsync(path, false, false, false, flags, cancel))
        {
            try
            {
                return await file.GetStream()._ReadToEndAsync(maxSize, cancel);
            }
            finally
            {
                await file.CloseAsync();
            }
        }
    }
    public Memory<byte> ReadDataFromFile(string path, int maxSize = int.MaxValue, FileFlags flags = FileFlags.None, CancellationToken cancel = default)
        => ReadDataFromFileAsync(path, maxSize, flags, cancel)._GetResult();

    public async Task<HugeMemoryBuffer<byte>> ReadHugeMemoryBufferFromFileAsync(string path, long maxSize = int.MaxValue, FileFlags flags = FileFlags.None, CancellationToken cancel = default)
    {
        await using (var file = await OpenAsync(path, false, false, false, flags, cancel))
        {
            try
            {
                HugeMemoryBuffer<byte> ret = new HugeMemoryBuffer<byte>();

                byte[] tmp = MemoryHelper.FastAllocMoreThan<byte>(Consts.Numbers.DefaultVeryLargeBufferSize);
                try
                {
                    while (true)
                    {
                        cancel.ThrowIfCancellationRequested();
                        int r = await file.ReadAsync(tmp, cancel);
                        if (r == 0)
                        {
                            break;
                        }
                        ret.Write(tmp, 0, r);
                        if (ret.Length > maxSize) throw new OverflowException("ReadHugeMemoryBufferFromFileAsync: too large data");
                    }
                }
                finally
                {
                    MemoryHelper.FastFree(tmp);
                }

                ret.SeekToBegin();

                return ret;
            }
            finally
            {
                await file.CloseAsync();
            }
        }
    }
    public HugeMemoryBuffer<byte> ReadHugeMemoryBufferFromFile(string path, long maxSize = int.MaxValue, FileFlags flags = FileFlags.None, CancellationToken cancel = default)
        => ReadHugeMemoryBufferFromFileAsync(path, maxSize, flags, cancel)._GetResult();

    public async Task<KeyValueList<string, string>> ReadKeyValueStrListFromConfigFileAsync(string path, Encoding? encoding = null, FileFlags flags = FileFlags.None, CancellationToken cancel = default)
    {
        if (await Lfs.IsFileExistsAsync(path) == false)
        {
            return new KeyValueList<string, string>();
        }

        try
        {
            string body = await this.ReadStringFromFileAsync(path, encoding, flags: flags, cancel: cancel);

            return Str.ConfigFileBodyToKeyValueList(body);
        }
        catch (Exception ex)
        {
            ex._Error();

            return new KeyValueList<string, string>();
        }
    }

    public async Task WriteKeyValueStrListToConfigFileAsync(string path, KeyValueList<string, string> list, StrDictionary<string>? commentsDict = null, FileFlags flags = FileFlags.None, bool doNotOverwrite = false, Encoding? encoding = null, bool writeBom = false, bool overwriteAndTruncate = false, CancellationToken cancel = default)
    {
        string body = Str.KeyValueStrListToConfigFileBody(list, commentsDict);

        await this.WriteStringToFileAsync(path, body, flags, doNotOverwrite, encoding, writeBom, cancel, overwriteAndTruncate);
    }

    public async Task ReadCsvFromFileAsync<T>(string path, Func<List<T>, bool> proc, Encoding? encoding = null, long startPosition = 0, bool trimStr = false, FileFlags flags = FileFlags.None, int maxBytesPerLine = Consts.Numbers.DefaultMaxBytesPerLine, int bufferSize = Consts.Numbers.DefaultLargeBufferSize, CancellationToken cancel = default)
         where T : notnull, new()
    {
        T obj = new T();
        FieldReaderWriter rw = obj._GetFieldReaderWriter(false);

        await ReadTextLinesFromFileAsync(path,
            (lineList, pos1, pos2) =>
            {
                List<T> list = new List<T>();

                foreach (string line in lineList)
                {
                    if (line._IsFilled())
                    {
                        T data = Str.CsvToObjectData<T>(line, trimStr, rw);

                        list.Add(data);
                    }
                }

                return proc(list);
            },
            encoding, startPosition, flags, maxBytesPerLine, bufferSize, cancel);
    }

    public void ReadCsvFromFile<T>(string path, Func<List<T>, bool> proc, Encoding? encoding = null, long startPosition = 0, bool trimStr = false, FileFlags flags = FileFlags.None, int maxBytesPerLine = Consts.Numbers.DefaultMaxBytesPerLine, int bufferSize = Consts.Numbers.DefaultLargeBufferSize, CancellationToken cancel = default)
        where T : notnull, new()
        => ReadCsvFromFileAsync<T>(path, proc, encoding, startPosition, trimStr, flags, maxBytesPerLine, bufferSize, cancel)._GetResult();

    public async Task<List<T>> ReadCsvFromFileAsync<T>(string path, Encoding? encoding = null, long startPosition = 0, bool trimStr = false, FileFlags flags = FileFlags.None, int maxBytesPerLine = Consts.Numbers.DefaultMaxBytesPerLine, int bufferSize = Consts.Numbers.DefaultLargeBufferSize, CancellationToken cancel = default)
         where T : notnull, new()
    {
        List<T> ret = new List<T>();

        await ReadCsvFromFileAsync<T>(path,
            (list) =>
            {
                ret.AddRange(list);
                return true;
            },
            encoding, startPosition, trimStr, flags, maxBytesPerLine, bufferSize, cancel);

        return ret;
    }

    public List<T> ReadCsvFromFile<T>(string path, Encoding? encoding = null, long startPosition = 0, bool trimStr = false, FileFlags flags = FileFlags.None, int maxBytesPerLine = Consts.Numbers.DefaultMaxBytesPerLine, int bufferSize = Consts.Numbers.DefaultLargeBufferSize, CancellationToken cancel = default)
        where T : notnull, new()
        => ReadCsvFromFileAsync<T>(path, encoding, startPosition, trimStr, flags, maxBytesPerLine, bufferSize, cancel)._GetResult();

    public CsvWriter<T> WriteCsv<T>(string path, bool printToConsole = true, bool writeHeader = true, int bufferSize = Consts.Numbers.DefaultLargeBufferSize, FileFlags flags = FileFlags.None, Encoding? encoding = null, bool writeBom = true)
        where T : notnull, new()
    {
        return new CsvWriter<T>(path, printToConsole, writeHeader, this, bufferSize, flags, encoding, writeBom);
    }

    public async Task<byte[]> CalcFileHashAsync(string path, HashAlgorithm? hash = null, FileFlags flags = FileFlags.None, long truncateSize = long.MaxValue,
        int bufferSize = Consts.Numbers.DefaultLargeBufferSize, RefLong? totalReadSize = null, CancellationToken cancel = default,
        ProgressReporterBase? progressReporter = null, string? progressReporterAdditionalInfo = null, long progressReporterCurrentSizeOffset = 0, long? progressReporterTotalSizeHint = null,
        bool progressReporterFinalize = true,
        ThroughputMeasuse? measure = null)
    {
        if (hash == null)
        {
            hash = MD5.Create();
        }

        await using var file = await this.OpenAsync(path, flags: flags, cancel: cancel);

        await using var stream = file.GetStream(true);

        return await Secure.CalcStreamHashAsync(stream, hash, truncateSize, bufferSize, totalReadSize, cancel, progressReporter, progressReporterAdditionalInfo,
            progressReporterCurrentSizeOffset, progressReporterTotalSizeHint, progressReporterFinalize, measure);
    }

    public async Task ReadTextLinesFromFileAsync(string path, Func<List<string>, long, long, bool> proc, Encoding? encoding = null, long startPosition = 0, FileFlags flags = FileFlags.None, int maxBytesPerLine = Consts.Numbers.DefaultMaxBytesPerLine, int bufferSize = Consts.Numbers.DefaultLargeBufferSize, CancellationToken cancel = default)
    {
        await using (var file = await OpenAsync(path, false, false, false, flags, cancel))
        {
            try
            {
                if (encoding == null)
                {
                    Memory<byte> bomData = await file.ReadAsync(4);

                    encoding = Str.CheckBOM(bomData.Span, out int skipBytes);

                    if (encoding == null)
                    {
                        encoding = Str.Utf8Encoding;
                    }

                    if (startPosition < skipBytes)
                        startPosition = skipBytes;
                }

                startPosition = Math.Max(startPosition, 0);
                startPosition = Math.Min(startPosition, file.Size);

                await file.SeekAsync(startPosition, SeekOrigin.Begin, cancel);

                await using var rawStream = file.GetStream(false);

                var reader = new BinaryLineReader(rawStream, bufferSize);

                long lastPosition = 0;

                while (true)
                {
                    var lines = await reader.ReadLinesAsync(maxBytesPerLine, cancel);
                    if (lines == null) break;

                    List<string> strList = new List<string>();

                    lines.ForEach(x => strList.Add(encoding.GetString(x.Span)));

                    if (proc(strList, lastPosition + startPosition, reader.CurrentRelativePosition + startPosition) == false)
                    {
                        break;
                    }

                    lastPosition = reader.CurrentRelativePosition;
                }
            }
            finally
            {
                await file.CloseAsync();
            }
        }
    }

    public async Task<string> ReadStringFromFileAsync(string path, Encoding? encoding = null, int maxSize = int.MaxValue, FileFlags flags = FileFlags.None, bool oneLine = false, CancellationToken cancel = default, ExpandIncludesSettings? expandIncludesSettings = null)
    {
        Memory<byte> data = await ReadDataFromFileAsync(path, maxSize, flags, cancel);

        string str;

        if (encoding == null)
            str = Str.DecodeStringAutoDetect(data.Span, out _);
        else
            str = Str.DecodeString(data.Span, encoding, out _);

        if (flags.Bit(FileFlags.ReadStr_ExpandIncludes))
        {
            str = await MiscUtil.ExpandIncludesToStrAsync(str, new FilePath(path, this, flags), expandIncludesSettings, cancel);
        }

        if (oneLine)
        {
            str = str._OneLine("");
        }

        return str;
    }
    public string ReadStringFromFile(string path, Encoding? encoding = null, int maxSize = int.MaxValue, FileFlags flags = FileFlags.None, bool oneLine = false, CancellationToken cancel = default, ExpandIncludesSettings? expandIncludesSettings = null)
        => ReadStringFromFileAsync(path, encoding, maxSize, flags, oneLine, cancel, expandIncludesSettings)._GetResult();


    public async Task<string> ReadStringFromFileEncryptedAsync(string path, string password, Encoding? encoding = null, int maxSize = int.MaxValue, FileFlags flags = FileFlags.None, bool oneLine = false, CancellationToken cancel = default)
    {
        if (flags.Bit(FileFlags.ReadStr_ExpandIncludes)) throw new CoresLibException("ReadStr_ExpandIncludes is not supported on encryped files.");

        Memory<byte> data = await ReadDataFromFileEncryptedAsync(path, password, maxSize, flags, cancel);

        string str;

        if (encoding == null)
            str = Str.DecodeStringAutoDetect(data.Span, out _);
        else
            str = Str.DecodeString(data.Span, encoding, out _);

        if (oneLine)
        {
            str = str._OneLine("");
        }

        return str;
    }
    public string ReadStringFromFileEncrypted(string path, string password, Encoding? encoding = null, int maxSize = int.MaxValue, FileFlags flags = FileFlags.None, bool oneLine = false, CancellationToken cancel = default)
        => ReadStringFromFileEncryptedAsync(path, password, encoding, maxSize, flags, oneLine, cancel)._GetResult();


    public async Task CreateZipArchiveAsync(FilePath destZipFilePath, string srcRootDir,
        FileContainerEntityParam? paramTemplate = null,
        Func<FileSystemEntity, bool>? fileFilter = null,
        Func<DirectoryPathInfo, Exception, CancellationToken, Task<bool>>? exceptionHandler = null,
        string? directoryPrefix = null,
        CancellationToken cancel = default)
    {
        await using var outFile = await destZipFilePath.CreateAsync(cancel: cancel);
        await using var zip = new ZipWriter(new ZipContainerOptions(outFile));

        await zip.ImportDirectoryAsync(srcRootDir, paramTemplate, fileFilter, exceptionHandler, directoryPrefix, cancel);

        await zip.FinishAsync();
    }
    public void CreateZipArchive(FilePath destZipFilePath, string srcRootDir,
        FileContainerEntityParam? paramTemplate = null,
        Func<FileSystemEntity, bool>? fileFilter = null,
        Func<DirectoryPathInfo, Exception, CancellationToken, Task<bool>>? exceptionHandler = null,
        string? directoryPrefix = null,
        CancellationToken cancel = default)
        => CreateZipArchiveAsync(destZipFilePath, srcRootDir, paramTemplate, fileFilter, exceptionHandler, directoryPrefix, cancel)._GetResult();

    class FindSingleFileData
    {
        public string FullPath;
        public double MatchRate;

        public FindSingleFileData(string fullPath, double matchRate)
        {
            FullPath = fullPath;
            MatchRate = matchRate;
        }
    }

    public async Task<string> EasyReadStringAsync(string partOfFileName, bool exact = false, string rootDir = "/", Encoding? encoding = null, int maxSize = int.MaxValue, FileFlags flags = FileFlags.None, bool oneLine = false, CancellationToken cancel = default, ExpandIncludesSettings? expandIncludesSettings = null)
        => await ReadStringFromFileAsync(await EasyFindSingleFileAsync(partOfFileName, exact, rootDir, cancel), encoding, maxSize, flags, oneLine, cancel, expandIncludesSettings);
    public string EasyReadString(string partOfFileName, bool exact = false, string rootDir = "/", Encoding? encoding = null, int maxSize = int.MaxValue, FileFlags flags = FileFlags.None, bool oneLine = false, CancellationToken cancel = default, ExpandIncludesSettings? expandIncludesSettings = null)
        => EasyReadStringAsync(partOfFileName, exact, rootDir, encoding, maxSize, flags, oneLine, cancel, expandIncludesSettings)._GetResult();

    public async Task<Memory<byte>> EasyReadDataAsync(string partOfFileName, bool exact = false, string rootDir = "/", int maxSize = int.MaxValue, FileFlags flags = FileFlags.None, CancellationToken cancel = default)
        => await ReadDataFromFileAsync(await EasyFindSingleFileAsync(partOfFileName, exact, rootDir, cancel), maxSize, flags, cancel);
    public Memory<byte> EasyReadData(string partOfFileName, bool exact = false, string rootDir = "/", int maxSize = int.MaxValue, FileFlags flags = FileFlags.None, CancellationToken cancel = default)
        => EasyReadDataAsync(partOfFileName, exact, rootDir, maxSize, flags, cancel)._GetResult();

#pragma warning disable CS1998
    public async Task<string> EasyFindSingleFileAsync(string partOfFileName, bool exact = false, string rootDir = "/", CancellationToken cancel = default)
    {
        if (partOfFileName._IsEmpty())
        {
            throw new ArgumentNullException(nameof(partOfFileName));
        }

        partOfFileName = PathParser.Mac.NormalizeDirectorySeparator(partOfFileName);

        bool partOfFileNameContainsDirName = partOfFileName.IndexOf(PathParser.Mac.DirectorySeparator) != -1;

        DirectoryWalker walk = new DirectoryWalker(this, EnumDirectoryFlags.NoGetPhysicalSize);
        string? exactFile = null;

        int numExactMatch = 0;

        List<FindSingleFileData> candidates = new List<FindSingleFileData>();

        await walk.WalkDirectoryAsync(rootDir,
            async (info, entities, c) =>
            {
                foreach (var file in entities.Where(x => x.IsDirectory == false))
                {
                    string fullPathTmp = PathParser.Mac.NormalizeDirectorySeparator(file.FullPath);

                    if (partOfFileName._IsSamei(file.Name))
                    {
                        // Exact match
                        exactFile = file.FullPath;
                        numExactMatch++;
                    }
                    else if (partOfFileNameContainsDirName && fullPathTmp.EndsWith(partOfFileName, StringComparison.OrdinalIgnoreCase))
                    {
                        // Exact match
                        exactFile = file.FullPath;
                        numExactMatch++;
                    }
                    else if (fullPathTmp._Search(partOfFileName) != -1)
                    {
                        int originalLen = fullPathTmp.Length;
                        if (originalLen >= 1)
                        {
                            int replacedLen = fullPathTmp._ReplaceStr(partOfFileName, "").Length;
                            int matchLen = originalLen - replacedLen;
                            FindSingleFileData d = new FindSingleFileData
                            (
                                fullPath: file.FullPath,
                                matchRate: (double)matchLen / (double)originalLen
                            );
                            candidates.Add(d);
                        }
                    }
                }
                return true;
            },
            cancel: cancel);

        if (exactFile._IsFilled())
        {
            if (exact && numExactMatch >= 2)
            {
                throw new FileException(partOfFileName, "Two or more files matched while exact flag is set.");
            }
            return exactFile;
        }

        if (exact && candidates.Count >= 2)
            throw new FileException(partOfFileName, "Two or more files matched while exact flag is set.");

        var match = candidates.OrderByDescending(x => x.MatchRate).FirstOrDefault();

        if (match == null)
            throw new FileException(partOfFileName, "The name did not match to any existing files.");

        return match.FullPath;
    }
    public string EasyFindSingleFile(string fileName, bool exact = false, string rootDir = "/", CancellationToken cancel = default)
        => EasyFindSingleFileAsync(fileName, exact, rootDir, cancel)._GetResult();
#pragma warning restore CS1998

    protected async Task DeleteDirectoryRecursiveInternalAsync(string directoryPath, CancellationToken cancel = default)
    {
        DirectoryWalker walker = new DirectoryWalker(this, EnumDirectoryFlags.NoGetPhysicalSize);
        await walker.WalkDirectoryAsync(directoryPath,
            async (info, entities, c) =>
            {
                foreach (var file in entities.Where(x => x.IsDirectory == false))
                {
                    await this.DeleteFileImplAsync(file.FullPath, FileFlags.ForceClearReadOnlyOrHiddenBitsOnNeed, cancel);
                }

                return true;
            },
            async (info, entities, c) =>
            {
                await this.DeleteDirectoryImplAsync(info.FullPath, false, cancel);
                return true;
            },
            cancel: cancel);
    }

    Singleton<string, EasyFileAccess>? EasyFileAccessSingleton;
    Singleton<string, string>? EasyAccessFileNameCache;
    void InitEasyFileAccessSingleton()
    {
        EasyFileAccessSingleton = new Singleton<string, EasyFileAccess>(filePath => new EasyFileAccess(this, filePath));
        EasyAccessFileNameCache = new Singleton<string, string>(name => FindEasyAccessFilePathFromNameImpl(name));
    }

    protected virtual string FindEasyAccessFilePathFromNameImpl(string name)
    {
        EasyAccessPathFindMode mode = this.Params.EasyAccessPathFindMode.Value;
        switch (mode)
        {
            case EasyAccessPathFindMode.MostMatch:
                return this.EasyFindSingleFile(name, false);

            case EasyAccessPathFindMode.MostMatchExact:
                return this.EasyFindSingleFile(name, true);

            case EasyAccessPathFindMode.ExactFullPath:
                return name;

            default:
                throw new NotSupportedException();
        }
    }

    public virtual EasyFileAccess GetEasyAccess(string name)
    {
        if (EasyAccessFileNameCache == null || EasyFileAccessSingleton == null)
            throw new CoresException("Easy access is not initialized.");

        string fullPath = EasyAccessFileNameCache[name];

        return this.EasyFileAccessSingleton[fullPath];
    }

    public virtual EasyFileAccess this[string name] => GetEasyAccess(name);


    public async Task<int> WriteDataToFileEncryptedAsync(string path, ReadOnlyMemory<byte> srcMemory, string password, FileFlags flags = FileFlags.None, bool doNotOverwrite = false, CancellationToken cancel = default, bool overwriteAndTruncate = false, RefBool? changed = null)
    {
        return await this.WriteDataToFileAsync(path, ChaChaPoly.EasyEncryptWithPassword(srcMemory, password), flags, doNotOverwrite, cancel, overwriteAndTruncate, changed);
    }
    public int WriteDataToFileEncrypted(string path, ReadOnlyMemory<byte> data, string password, FileFlags flags = FileFlags.None, bool doNotOverwrite = false, CancellationToken cancel = default, bool overwriteAndTruncate = false, RefBool? changed = null)
        => WriteDataToFileEncryptedAsync(path, data, password, flags, doNotOverwrite, cancel, overwriteAndTruncate, changed)._GetResult();

    public async Task<Memory<byte>> ReadDataFromFileEncryptedAsync(string path, string password, int maxSize = int.MaxValue, FileFlags flags = FileFlags.None, CancellationToken cancel = default)
    {
        var data = await this.ReadDataFromFileAsync(path, maxSize, flags, cancel);

        var res = ChaChaPoly.EasyDecryptWithPassword(data, password);

        return res;
    }
    public Memory<byte> ReadDataFromFileEncrypted(string path, string password, int maxSize = int.MaxValue, FileFlags flags = FileFlags.None, CancellationToken cancel = default)
        => ReadDataFromFileEncryptedAsync(path, password, maxSize, flags, cancel)._GetResult();

}

public class DirectoryPathInfo
{
    public FileSystem FileSystem { get; }
    public bool IsRoot { get; }
    public string FullPath { get; }
    public string RelativePath { get; }
    public FileSystemEntity Entity { get; }

    public DirectoryPathInfo(FileSystem fileSystem, bool isRoot, string fullPath, string relativePath, FileSystemEntity entity)
    {
        this.FileSystem = fileSystem;
        this.IsRoot = isRoot;
        this.FullPath = fullPath;
        this.RelativePath = relativePath;
        this.Entity = entity;
    }

    public override string? ToString()
        => this.FullPath;
}

public class CoresNoSubDirException : CoresException { }

public class DirectoryWalker
{
    public FileSystem FileSystem { get; }
    public EnumDirectoryFlags Flags { get; }

    public DirectoryWalker(FileSystem? fileSystem = null, EnumDirectoryFlags flags = EnumDirectoryFlags.None)
    {
        fileSystem ??= Lfs;

        this.FileSystem = fileSystem;
        this.Flags = (flags | EnumDirectoryFlags.IncludeCurrentDirectory).BitRemove(EnumDirectoryFlags.IncludeParentDirectory);

    }

    async Task<bool> WalkDirectoryInternalAsync(string directoryFullPath, string directoryRelativePath,
        Func<DirectoryPathInfo, FileSystemEntity[], CancellationToken, Task<bool>> callback,
        Func<DirectoryPathInfo, FileSystemEntity[], CancellationToken, Task<bool>>? callbackForDirectoryAgain,
        Func<DirectoryPathInfo, Exception, CancellationToken, Task<bool>>? exceptionHandler,
        bool recursive, CancellationToken opCancel, FileSystemEntity? dirEntity = null,
        Func<DirectoryPathInfo, FileSystemEntity, bool>? entityFilter = null)
    {
        opCancel.ThrowIfCancellationRequested();

        FileSystemEntity[] entityList;

        bool isRootDir = false;

        if (dirEntity == null)
        {
            isRootDir = true;

            dirEntity = new FileSystemEntity(
                fullPath: directoryFullPath,
                name: this.FileSystem.PathParser.GetFileName(directoryFullPath),
                attributes: FileAttributes.Directory,
                creationTime: default,
                lastWriteTime: default,
                lastAccessTime: default
            );
        }

        DirectoryPathInfo currentDirInfo = new DirectoryPathInfo(this.FileSystem, isRootDir, directoryFullPath, directoryRelativePath, dirEntity);

        try
        {
            entityList = await FileSystem.EnumDirectoryAsync(directoryFullPath, false, this.Flags, null, opCancel);
        }
        catch (Exception ex)
        {
            if (exceptionHandler == null)
            {
                throw;
            }

            if (isRootDir)
            {
                // ルートディレクトリがそもそも存在しないような場合は例外を出す
                throw;
            }

            if (await exceptionHandler(currentDirInfo, ex, opCancel) == false)
            {
                return false;
            }
            else
            {
                return true;
            }
        }

        if (entityFilter != null)
        {
            // フィルタ
            List<FileSystemEntity> newEntityList = new List<FileSystemEntity>(entityList.Length);
            foreach (var e in entityList)
            {
                if (e.IsCurrentOrParentDirectory || entityFilter(currentDirInfo, e))
                {
                    newEntityList.Add(e);
                }
            }

            entityList = newEntityList.ToArray();
        }

        if (isRootDir)
        {
            var rootDirEntry = entityList.Where(x => x.IsCurrentDirectory).Single();
            currentDirInfo = new DirectoryPathInfo(this.FileSystem, true, directoryFullPath, directoryRelativePath, rootDirEntry);
        }

        bool noSubDir = false;

        try
        {
            if (await callback(currentDirInfo, entityList, opCancel) == false)
            {
                return false;
            }
        }
        catch (CoresNoSubDirException)
        {
            // CoresNoSubDirException 例外が発生した場合は、サブディレクトリには突入しない
            noSubDir = true;
        }

        if (recursive && noSubDir == false)
        {
            // Deep directory
            foreach (FileSystemEntity entity in entityList.Where(x => x.IsCurrentOrParentDirectory == false))
            {
                if (entity.IsDirectory)
                {
                    opCancel.ThrowIfCancellationRequested();

                    if (await WalkDirectoryInternalAsync(entity.FullPath, FileSystem.PathParser.Combine(directoryRelativePath, entity.Name), callback, callbackForDirectoryAgain, exceptionHandler, true, opCancel, entity, entityFilter) == false)
                    {
                        return false;
                    }
                }
            }
        }

        if (callbackForDirectoryAgain != null)
        {
            if (await callbackForDirectoryAgain(currentDirInfo, entityList, opCancel) == false)
            {
                return false;
            }
        }

        return true;
    }

    public async Task<bool> WalkDirectoryAsync(string rootDirectory,
        Func<DirectoryPathInfo, FileSystemEntity[], CancellationToken, Task<bool>> callback,
        Func<DirectoryPathInfo, FileSystemEntity[], CancellationToken, Task<bool>>? callbackForDirectoryAgain = null,
        Func<DirectoryPathInfo, Exception, CancellationToken, Task<bool>>? exceptionHandler = null,
        bool recursive = true,
        CancellationToken cancel = default,
        Func<DirectoryPathInfo, FileSystemEntity, bool>? entityFilter = null
        )
    {
        cancel.ThrowIfCancellationRequested();

        rootDirectory = await FileSystem.NormalizePathAsync(rootDirectory, cancel: cancel);

        return await WalkDirectoryInternalAsync(rootDirectory, "", callback, callbackForDirectoryAgain, exceptionHandler, recursive, cancel, entityFilter: entityFilter);
    }

#pragma warning disable CS1998
    public bool WalkDirectory(string rootDirectory, Func<DirectoryPathInfo, FileSystemEntity[], CancellationToken, bool> callback, Func<DirectoryPathInfo, FileSystemEntity[], CancellationToken, bool>? callbackForDirectoryAgain = null, Func<DirectoryPathInfo, Exception, CancellationToken, bool>? exceptionHandler = null, bool recursive = true, CancellationToken cancel = default)
        => WalkDirectoryAsync(rootDirectory,
            async (dirInfo, entity, c) => { return callback(dirInfo, entity, c); },
            async (dirInfo, entity, c) => { return (callbackForDirectoryAgain != null) ? callbackForDirectoryAgain(dirInfo, entity, c) : true; },
            async (dirInfo, exception, c) => { if (exceptionHandler == null) throw exception; return exceptionHandler(dirInfo, exception, c); },
            recursive, cancel)._GetResult();
#pragma warning restore CS1998

    public async Task<bool> WalkDirectoriesAsync(string[] rootDirectoryList,
        Func<DirectoryPathInfo, FileSystemEntity[], CancellationToken, Task<bool>> callback,
        Func<DirectoryPathInfo, FileSystemEntity[], CancellationToken, Task<bool>>? callbackForDirectoryAgain = null,
        Func<DirectoryPathInfo, Exception, CancellationToken, Task<bool>>? exceptionHandler = null,
        bool recursive = true,
        CancellationToken cancel = default,
        Func<DirectoryPathInfo, FileSystemEntity, bool>? entityFilter = null
        )
    {
        if (rootDirectoryList.Length == 0)
        {
            return false;
        }

        foreach (var element in rootDirectoryList)
        {
            cancel.ThrowIfCancellationRequested();

            var rootDirectory = await FileSystem.NormalizePathAsync(element, cancel: cancel);

            bool ret = await WalkDirectoryInternalAsync(rootDirectory, "", callback, callbackForDirectoryAgain, exceptionHandler, recursive, cancel, entityFilter: entityFilter);

            if (ret == false)
            {
                return false;
            }
        }

        return true;
    }

}

public enum EasyFileAccessType
{
    String,
    Binary,
    HexParsedBinary,
}

public class EasyFileAccess
{
    // Properties
    public string String => (string)this[EasyFileAccessType.String];
    public ReadOnlyMemory<byte> Binary => (ReadOnlyMemory<byte>)this[EasyFileAccessType.Binary];
    public ReadOnlyMemory<byte> HexParsedBinary => (ReadOnlyMemory<byte>)this[EasyFileAccessType.HexParsedBinary];

    // Implementation
    public FileSystem FileSystem { get; }
    public string FilePath { get; }

    readonly Singleton<EasyFileAccessType, object> CachedData;

    public object this[EasyFileAccessType type] => this.GetData(type);

    public EasyFileAccess(FileSystem fileSystem, string filePath)
    {
        this.FileSystem = fileSystem;
        this.FilePath = filePath;
        this.CachedData = new Singleton<EasyFileAccessType, object>(type => this.InternalReadData(type));
    }

    public object GetData(EasyFileAccessType type) => this.CachedData[type];

    object InternalReadData(EasyFileAccessType type)
    {
        switch (type)
        {
            case EasyFileAccessType.String:
                return FileSystem.ReadStringFromFile(this.FilePath);

            case EasyFileAccessType.Binary:
                return (ReadOnlyMemory<byte>)FileSystem.ReadDataFromFile(this.FilePath);

            case EasyFileAccessType.HexParsedBinary:
                return (ReadOnlyMemory<byte>)FileSystem.ReadStringFromFile(this.FilePath)._GetHexBytes();

            default:
                throw new ArgumentOutOfRangeException("type");
        }
    }

    public static implicit operator string(EasyFileAccess access) => access.String;
    public static implicit operator ReadOnlyMemory<byte>(EasyFileAccess access) => access.Binary;
    public static implicit operator ReadOnlySpan<byte>(EasyFileAccess access) => access.Binary.Span;
    public static implicit operator byte[](EasyFileAccess access) => access.Binary.ToArray();
}

public abstract class FileObjectRandomAccessWrapperBase : FileObject
{
    protected readonly ConcurrentRandomAccess<byte> BaseAccess;

    public FileObjectRandomAccessWrapperBase(ConcurrentRandomAccess<byte> sharedBaseAccess, FileSystem fileSystem, FileParameters fileParams) : base(fileSystem, fileParams)
    {
        this.BaseAccess = sharedBaseAccess;

        long initialPosition = 0;

        if (fileParams.Mode == FileMode.Create || fileParams.Mode == FileMode.CreateNew || fileParams.Mode == FileMode.Truncate)
        {
            this.BaseAccess.SetFileSize(0);
        }

        long initialFileSize = this.BaseAccess.GetFileSize(true);

        if (fileParams.Mode == FileMode.Append)
        {
            initialPosition = initialFileSize;
        }

        InitAndCheckFileSizeAndPosition(initialPosition, initialFileSize);
    }

    protected abstract void OnCloseImpl();

    protected override Task CloseImplAsync()
    {
        try
        {
            OnCloseImpl();
        }
        catch { }

        return Task.CompletedTask;
    }

    protected override Task FlushImplAsync(CancellationToken cancel = default)
        => this.BaseAccess.FlushAsync(cancel);

    protected override Task<long> GetFileSizeImplAsync(CancellationToken cancel = default)
        => this.BaseAccess.GetFileSizeAsync(cancel: cancel);

    protected override Task<int> ReadRandomImplAsync(long position, Memory<byte> data, CancellationToken cancel = default)
        => this.BaseAccess.ReadRandomAsync(position, data, cancel);

    protected override Task SetFileSizeImplAsync(long size, CancellationToken cancel = default)
        => this.BaseAccess.SetFileSizeAsync(size, cancel);

    protected override Task WriteRandomImplAsync(long position, ReadOnlyMemory<byte> data, CancellationToken cancel = default)
        => this.BaseAccess.WriteRandomAsync(position, data, cancel);
}

public abstract class FileSystemPath : IEquatable<FileSystemPath> // CloneDeep 禁止
{
    public string PathString { get; }
    public FileSystem FileSystem { get; }
    public FileFlags Flags { get; }
    public PathParser PathParser => this.FileSystem.PathParser;

    public FileSystemPath(string pathString, FileSystem? fileSystem = null, FileFlags flags = FileFlags.None, bool allowRelativePath = false)
    {
        if (pathString == null || pathString == "") throw new ArgumentNullException("pathString");

        if (fileSystem == null) fileSystem = Lfs;

        if (allowRelativePath)
        {
            pathString = fileSystem.GetAbsolutePathFromRelativePath(pathString, flags);
        }

        this.FileSystem = fileSystem;
        this.Flags = flags;

        this.PathString = this.FileSystem.NormalizePath(pathString);
    }

    public bool IsAbsolutePath() => PathParser.IsAbsolutePath(this.PathString);

    public DirectoryPath GetParentDirectory() => new DirectoryPath(PathParser.GetDirectoryName(this.PathString), this.FileSystem, this.Flags);

    public override string ToString() => this.PathString;

    public virtual bool Equals(FileSystemPath? other)
    {
        if (other == null) return false;
        if (this.FileSystem != other.FileSystem) return false;
        return this.PathString.Equals(other.PathString, this.PathParser.PathStringComparison);
    }

    public static implicit operator string(FileSystemPath path) => path.ToString();
}

public class DirectoryPath : FileSystemPath // CloneDeep 禁止
{
    public DirectoryPath(string pathString, FileSystem? fileSystem = null, FileFlags flags = FileFlags.None, bool allowRelativePath = false) : base(pathString, fileSystem, flags, allowRelativePath)
    {
    }

    public Task<bool> CheckFreeDiskSpaceByTestFileAsync(int size, CancellationToken cancel = default)
        => this.FileSystem.CheckFreeDiskSpaceByTestFileAsync(this.PathString, size, cancel);

    public Task CreateDirectoryAsync(CancellationToken cancel = default)
        => this.FileSystem.CreateDirectoryAsync(this.PathString, this.Flags, cancel);
    public void CreateDirectory(CancellationToken cancel = default)
        => CreateDirectoryAsync(cancel)._GetResult();

    public Task DeleteDirectoryAsync(bool recursive = false, CancellationToken cancel = default)
        => this.FileSystem.DeleteDirectoryAsync(this.PathString, recursive, cancel);
    public void DeleteDirectory(bool recursive = false, CancellationToken cancel = default)
        => DeleteDirectoryAsync(recursive, cancel)._GetResult();

    public Task<FileSystemEntity[]> EnumDirectoryAsync(bool recursive = false, EnumDirectoryFlags flags = EnumDirectoryFlags.None, CancellationToken cancel = default)
        => this.FileSystem.EnumDirectoryAsync(this.PathString, recursive, flags, null, cancel);
    public FileSystemEntity[] EnumDirectory(bool recursive = false, EnumDirectoryFlags flags = EnumDirectoryFlags.None, CancellationToken cancel = default)
        => EnumDirectoryAsync(recursive, flags, cancel)._GetResult();

    public async Task<DirectoryPath[]> GetDirectoriesAsync(EnumDirectoryFlags flags = EnumDirectoryFlags.None, CancellationToken cancel = default)
    {
        var ents = await this.FileSystem.EnumDirectoryAsync(this.PathString, cancel: cancel, flags: flags);
        List<DirectoryPath> ret = new List<DirectoryPath>();
        foreach (var dir in ents.Where(x => x.IsDirectory))
        {
            ret.Add(new DirectoryPath(dir.FullPath, this.FileSystem, this.Flags));
        }
        return ret.ToArray();
    }
    public DirectoryPath[] GetDirectories(EnumDirectoryFlags flags = EnumDirectoryFlags.None, CancellationToken cancel = default)
        => GetDirectoriesAsync(flags, cancel)._GetResult();

    public async Task<FilePath[]> GetFilesAsync(EnumDirectoryFlags flags = EnumDirectoryFlags.None, CancellationToken cancel = default)
    {
        var ents = await this.FileSystem.EnumDirectoryAsync(this.PathString, cancel: cancel, flags: flags);
        List<FilePath> ret = new List<FilePath>();
        foreach (var dir in ents.Where(x => x.IsDirectory == false))
        {
            ret.Add(new FilePath(dir.FullPath, this.FileSystem, this.Flags));
        }
        return ret.ToArray();
    }
    public FilePath[] GetFiles(EnumDirectoryFlags flags = EnumDirectoryFlags.None, CancellationToken cancel = default)
        => GetFilesAsync(flags, cancel)._GetResult();

    public Task<FileMetadata> GetDirectoryMetadataAsync(FileMetadataGetFlags flags = FileMetadataGetFlags.DefaultAll, CancellationToken cancel = default)
        => this.FileSystem.GetDirectoryMetadataAsync(this.PathString, flags, cancel);
    public FileMetadata GetDirectoryMetadata(FileMetadataGetFlags flags = FileMetadataGetFlags.DefaultAll, CancellationToken cancel = default)
        => GetDirectoryMetadataAsync(flags, cancel)._GetResult();

    public Task<bool> IsDirectoryExistsAsync(CancellationToken cancel = default)
        => this.FileSystem.IsDirectoryExistsAsync(this.PathString, cancel);
    public bool IsDirectoryExists(CancellationToken cancel = default)
        => IsDirectoryExistsAsync(cancel)._GetResult();

    public Task SetDirectoryMetadataAsync(FileMetadata metadata, CancellationToken cancel = default)
         => this.FileSystem.SetDirectoryMetadataAsync(this.PathString, metadata, cancel);
    public void SetDirectoryMetadata(FileMetadata metadata, CancellationToken cancel = default)
        => SetDirectoryMetadataAsync(metadata, cancel)._GetResult();

    public Task MoveDirectoryAsync(string destPath, CancellationToken cancel = default)
        => this.FileSystem.MoveDirectoryAsync(this.PathString, destPath, cancel);
    public void MoveDirectory(string destPath, CancellationToken cancel = default)
        => MoveDirectoryAsync(destPath, cancel)._GetResult();

    public string GetParentDirectoryName() => PathParser.GetDirectoryName(this.PathString);
    public string GetThisDirectoryName() => PathParser.GetFileName(this.PathString);
    public void SepareteParentDirectoryAndThisDirectory(out string parentPath, out string thisDirectoryName) => PathParser.SepareteDirectoryAndFileName(this.PathString, out parentPath, out thisDirectoryName);

    public FilePath Combine(string path2) => new FilePath(PathParser.Combine(this.PathString, path2), this.FileSystem, this.Flags);
    public FilePath Combine(string path2, bool path2NeverAbsolutePath = false) => new FilePath(PathParser.Combine(this.PathString, path2, path2NeverAbsolutePath), this.FileSystem, this.Flags);
    public FilePath Combine(params string[] pathList)
    {
        if (pathList == null || pathList.Length == 0) return new FilePath(this.PathString, this.FileSystem, this.Flags);
        return new FilePath(PathParser.Combine(this.PathString._SingleArray().Concat(pathList)._ToArrayList()), this.FileSystem, this.Flags);
    }

    public DirectoryPath GetSubDirectory(string path2) => new DirectoryPath(PathParser.Combine(this.PathString, path2), this.FileSystem, this.Flags);
    public DirectoryPath GetSubDirectory(string path2, bool path2NeverAbsolutePath = false) => new DirectoryPath(PathParser.Combine(this.PathString, path2, path2NeverAbsolutePath), this.FileSystem, this.Flags);
    public DirectoryPath GetSubDirectory(params string[] pathList)
    {
        if (pathList == null || pathList.Length == 0) return new DirectoryPath(this.PathString, this.FileSystem, this.Flags);
        return new DirectoryPath(PathParser.Combine(this.PathString._SingleArray().Concat(pathList)._ToArrayList()), this.FileSystem, this.Flags);
    }

    public static implicit operator DirectoryPath(string directoryName) => new DirectoryPath(directoryName);

    public bool IsRootDirectory => this.PathParser.IsRootDirectory(this.PathString);

    public List<DirectoryPath> GetBreadCrumbList()
    {
        DirectoryPath current = this;

        List<DirectoryPath> ret = new List<DirectoryPath>();

        while (true)
        {
            ret.Add(current);

            if (current.IsRootDirectory)
            {
                break;
            }

            current = current.GetParentDirectory();
        }

        ret.Reverse();

        return ret;
    }
}

public class FilePath : FileSystemPath // CloneDeep 禁止
{
    readonly Singleton<EasyFileAccess> EasyAccessSingleton;

    public EasyFileAccess EasyAccess => this.EasyAccessSingleton;

    public FilePath(ResourceFileSystem resFs, string partOfPath, bool exact = false, string rootDir = "/", FileFlags operationFlags = FileFlags.None, CancellationToken cancel = default)
        : this((resFs ?? Res.Cores).EasyFindSingleFile(partOfPath, exact, rootDir, cancel), (resFs ?? Res.Cores)) { }

    public FilePath(string pathString, FileSystem? fileSystem = null, FileFlags flags = FileFlags.None, bool allowRelativePath = false)
         : base(pathString, fileSystem, flags, allowRelativePath)
    {
        this.EasyAccessSingleton = new Singleton<EasyFileAccess>(() => new EasyFileAccess(this.FileSystem, this.PathString));
    }

    public static implicit operator FilePath(string fileName) => new FilePath(fileName, flags: FileFlags.AutoCreateDirectory);

    public FilePath GetPath(string pathString) => new FilePath(pathString, this.FileSystem, this.Flags);

    public FilePath Concat(string concatStr) => new FilePath(this.PathString + concatStr._NonNull(), this.FileSystem, this.Flags);

    public Task<FileObject> CreateFileAsync(FileMode mode = FileMode.Open, FileAccess access = FileAccess.Read, FileShare share = FileShare.Read, FileFlags additionalFlags = FileFlags.None, CancellationToken cancel = default)
        => this.FileSystem.CreateFileAsync(new FileParameters(this.PathString, mode, access, share, this.Flags | additionalFlags), cancel);

    public FileObject CreateFile(FileMode mode = FileMode.Open, FileAccess access = FileAccess.Read, FileShare share = FileShare.Read, FileFlags additionalFlags = FileFlags.None, CancellationToken cancel = default)
        => CreateFileAsync(mode, access, share, additionalFlags, cancel)._GetResult();

    public Task<FileObject> CreateAsync(bool noShare = false, FileFlags additionalFlags = FileFlags.None, bool doNotOverwrite = false, CancellationToken cancel = default)
        => CreateFileAsync(doNotOverwrite ? FileMode.CreateNew : FileMode.Create, FileAccess.ReadWrite, noShare ? FileShare.None : FileShare.Read, additionalFlags, cancel);

    public FileObject Create(bool noShare = false, FileFlags additionalFlags = FileFlags.None, bool doNotOverwrite = false, CancellationToken cancel = default)
        => CreateAsync(noShare, additionalFlags, doNotOverwrite, cancel)._GetResult();

    public Task<FileObject> OpenAsync(bool writeMode = false, bool noShare = false, bool readLock = false, FileFlags additionalFlags = FileFlags.None, CancellationToken cancel = default)
        => CreateFileAsync(FileMode.Open, (writeMode ? FileAccess.ReadWrite : FileAccess.Read),
            (noShare ? FileShare.None : ((writeMode || readLock) ? FileShare.Read : (FileShare.ReadWrite | FileShare.Delete))), additionalFlags, cancel);

    public FileObject Open(bool writeMode = false, bool noShare = false, bool readLock = false, FileFlags additionalFlags = FileFlags.None, CancellationToken cancel = default)
        => OpenAsync(writeMode, noShare, readLock, additionalFlags, cancel)._GetResult();

    public Task<FileObject> OpenOrCreateAsync(bool noShare = false, FileFlags additionalFlags = FileFlags.None, CancellationToken cancel = default)
        => CreateFileAsync(FileMode.OpenOrCreate, FileAccess.ReadWrite, noShare ? FileShare.None : FileShare.Read, additionalFlags, cancel);

    public FileObject OpenOrCreate(bool noShare = false, FileFlags additionalFlags = FileFlags.None, CancellationToken cancel = default)
        => OpenOrCreateAsync(noShare, additionalFlags, cancel)._GetResult();

    public Task<FileObject> OpenOrCreateAppendAsync(bool noShare = false, FileFlags additionalFlags = FileFlags.None, CancellationToken cancel = default)
        => CreateFileAsync(FileMode.Append, FileAccess.Write, noShare ? FileShare.None : FileShare.Read, additionalFlags, cancel);

    public FileObject OpenOrCreateAppend(bool noShare = false, FileFlags additionalFlags = FileFlags.None, CancellationToken cancel = default)
        => OpenOrCreateAppendAsync(noShare, additionalFlags, cancel)._GetResult();

    public Task<FileMetadata> GetFileMetadataAsync(FileMetadataGetFlags flags = FileMetadataGetFlags.DefaultAll, CancellationToken cancel = default)
        => this.FileSystem.GetFileMetadataAsync(this.PathString, flags, cancel);

    public FileMetadata GetFileMetadata(FileMetadataGetFlags flags = FileMetadataGetFlags.DefaultAll, CancellationToken cancel = default)
        => GetFileMetadataAsync(flags, cancel)._GetResult();

    public Task<bool> IsFileExistsAsync(CancellationToken cancel = default)
        => this.FileSystem.IsFileExistsAsync(this.PathString, cancel);

    public bool IsFileExists(CancellationToken cancel = default)
        => IsFileExistsAsync(cancel)._GetResult();

    public Task SetFileMetadataAsync(FileMetadata metadata, CancellationToken cancel = default)
        => this.FileSystem.SetFileMetadataAsync(this.PathString, metadata, cancel);

    public void SetFileMetadata(FileMetadata metadata, CancellationToken cancel = default)
        => SetFileMetadataAsync(metadata, cancel)._GetResult();

    public Task DeleteFileAsync(FileFlags additionalFlags = FileFlags.None, CancellationToken cancel = default)
        => this.FileSystem.DeleteFileAsync(this.PathString, this.Flags | additionalFlags, cancel);

    public void DeleteFile(FileFlags additionalFlags = FileFlags.None, CancellationToken cancel = default)
        => DeleteFileAsync(this.Flags | additionalFlags, cancel)._GetResult();

    public Task MoveFileAsync(string destPath, CancellationToken cancel = default)
        => this.FileSystem.MoveFileAsync(this.PathString, destPath, cancel);

    public void MoveFile(string destPath, CancellationToken cancel = default)
        => MoveFileAsync(destPath, cancel)._GetResult();

    public Task<bool> TryAddOrRemoveAttributeFromExistingFile(FileAttributes attributesToAdd = 0, FileAttributes attributesToRemove = 0, CancellationToken cancel = default)
        => this.FileSystem.TryAddOrRemoveAttributeFromExistingFileAsync(this.PathString, attributesToAdd, attributesToRemove, cancel);

    public Task<int> WriteDataToFileAsync(ReadOnlyMemory<byte> srcMemory, FileFlags additionalFlags = FileFlags.None, bool doNotOverwrite = false, CancellationToken cancel = default, bool overwriteAndTruncate = false)
        => this.FileSystem.WriteDataToFileAsync(this.PathString, srcMemory, this.Flags | additionalFlags, doNotOverwrite, cancel, overwriteAndTruncate);

    public int WriteDataToFile(ReadOnlyMemory<byte> data, FileFlags additionalFlags = FileFlags.None, bool doNotOverwrite = false, CancellationToken cancel = default, bool overwriteAndTruncate = false)
        => WriteDataToFileAsync(data, additionalFlags, doNotOverwrite, cancel, overwriteAndTruncate)._GetResult();

    public Task<int> WriteStringToFileAsync(string srcString, FileFlags additionalFlags = FileFlags.None, bool doNotOverwrite = false, Encoding? encoding = null, bool writeBom = false, CancellationToken cancel = default, bool overwriteAndTruncate = false)
        => this.FileSystem.WriteStringToFileAsync(this.PathString, srcString, this.Flags | additionalFlags, doNotOverwrite, encoding, writeBom, cancel, overwriteAndTruncate);

    public int WriteStringToFile(string srcString, FileFlags additionalFlags = FileFlags.None, bool doNotOverwrite = false, Encoding? encoding = null, bool writeBom = false, CancellationToken cancel = default, bool overwriteAndTruncate = false)
        => WriteStringToFileAsync(srcString, additionalFlags, doNotOverwrite, encoding, writeBom, cancel, overwriteAndTruncate)._GetResult();

    public Task AppendDataToFileAsync(Memory<byte> srcMemory, FileFlags additionalFlags = FileFlags.None, CancellationToken cancel = default)
        => this.FileSystem.AppendDataToFileAsync(this.PathString, srcMemory, this.Flags | additionalFlags, cancel);

    public void AppendDataToFile(Memory<byte> srcMemory, FileFlags additionalFlags = FileFlags.None, CancellationToken cancel = default)
        => AppendDataToFileAsync(srcMemory, additionalFlags, cancel)._GetResult();

    public Task<int> ReadDataFromFileAsync(Memory<byte> destMemory, FileFlags additionalFlags = FileFlags.None, CancellationToken cancel = default)
        => this.FileSystem.ReadDataFromFileAsync(this.PathString, destMemory, this.Flags | additionalFlags, cancel);

    public int ReadDataFromFile(Memory<byte> destMemory, FileFlags additionalFlags = FileFlags.None, CancellationToken cancel = default)
        => ReadDataFromFileAsync(destMemory, additionalFlags, cancel)._GetResult();

    public Task<Memory<byte>> ReadDataFromFileAsync(int maxSize = int.MaxValue, FileFlags additionalFlags = FileFlags.None, CancellationToken cancel = default)
        => this.FileSystem.ReadDataFromFileAsync(this.PathString, maxSize, this.Flags | additionalFlags, cancel);

    public Memory<byte> ReadDataFromFile(int maxSize = int.MaxValue, FileFlags additionalFlags = FileFlags.None, CancellationToken cancel = default)
        => ReadDataFromFileAsync(maxSize, additionalFlags, cancel)._GetResult();

    public Task<string> ReadStringFromFileAsync(Encoding? encoding = null, int maxSize = int.MaxValue, FileFlags additionalFlags = FileFlags.None, bool oneLine = false, CancellationToken cancel = default, ExpandIncludesSettings? expandIncludesSettings = null)
        => this.FileSystem.ReadStringFromFileAsync(this.PathString, encoding, maxSize, this.Flags | additionalFlags, oneLine, cancel, expandIncludesSettings);

    public string ReadStringFromFile(Encoding? encoding = null, int maxSize = int.MaxValue, FileFlags additionalFlags = FileFlags.None, bool oneLine = false, CancellationToken cancel = default)
        => ReadStringFromFileAsync(encoding, maxSize, additionalFlags, oneLine, cancel)._GetResult();

    public Task<T> ReadAndParseDataFileAsync<T>(ReadParseFlags readParseFlags, Func<ReadOnlyMemory<byte>, T> parseProc, Func<ReadOnlyMemory<byte>> createProc, Func<T, ReadOnlyMemory<byte>>? serializeProc = null, int maxSize = int.MaxValue, FileFlags flags = FileFlags.None, CancellationToken cancel = default)
        => this.FileSystem.ReadAndParseDataFileAsync(this.PathString, readParseFlags, parseProc, createProc, serializeProc, maxSize, flags, cancel);

    public T ReadAndParseDataFile<T>(ReadParseFlags readParseFlags, Func<ReadOnlyMemory<byte>, T> parseProc, Func<ReadOnlyMemory<byte>> createProc, Func<T, ReadOnlyMemory<byte>>? serializeProc = null, int maxSize = int.MaxValue, FileFlags flags = FileFlags.None, CancellationToken cancel = default)
        => ReadAndParseDataFileAsync(readParseFlags, parseProc, createProc, serializeProc, maxSize, flags, cancel)._GetResult();

    public string GetFileNameWithoutExtension(bool longExtension = false) => this.PathParser.GetFileNameWithoutExtension(this.PathString, longExtension);
    public string GetExtension(string path, bool longExtension = false) => this.PathParser.GetExtension(this.PathString, longExtension);
    public string GetFileName() => PathParser.GetFileName(this.PathString);
    public void SepareteDirectoryAndFileName(out string dirPath, out string fileName) => PathParser.SepareteDirectoryAndFileName(this.PathString, out dirPath, out fileName);

    public Task<T> ReadJsonFromFileAsync<T>(FileFlags flags = FileFlags.None, CancellationToken cancel = default,
        bool includeNull = false, int? maxDepth = Json.DefaultMaxDepth, bool nullIfError = false, JsonFlags jsonFlags = JsonFlags.None)
        => this.FileSystem.ReadJsonFromFileAsync<T>(this.PathString, flags, cancel, includeNull, maxDepth, nullIfError, false, jsonFlags);

    public Task<long> WriteJsonToFileAsync<T>([AllowNull] T obj, FileFlags flags = FileFlags.None, bool doNotOverwrite = false, CancellationToken cancel = default,
        bool includeNull = false, bool escapeHtml = false, int? maxDepth = Json.DefaultMaxDepth, bool compact = false, bool referenceHandling = false, JsonFlags jsonFlags = JsonFlags.None)
        => this.FileSystem.WriteJsonToFileAsync<T>(this.PathString, obj, flags, doNotOverwrite, cancel, includeNull, escapeHtml, maxDepth, compact, referenceHandling, false, jsonFlags);

    public Task<T> ReadJsonFromFileEncryptedAsync<T>(string password, int maxSize = int.MaxValue, FileFlags flags = FileFlags.None, CancellationToken cancel = default,
        bool includeNull = false, int? maxDepth = Json.DefaultMaxDepth, bool nullIfError = false, JsonFlags jsonFlags = JsonFlags.None)
        => this.FileSystem.ReadJsonFromFileEncryptedAsync<T>(this.PathString, password, maxSize, flags, cancel, includeNull, maxDepth, nullIfError, jsonFlags);

    public Task<long> WriteJsonToFileEncryptedAsync<T>([AllowNull] T obj, string password, FileFlags flags = FileFlags.None, bool doNotOverwrite = false, CancellationToken cancel = default,
        bool includeNull = false, bool escapeHtml = false, int? maxDepth = Json.DefaultMaxDepth, bool compact = false, bool referenceHandling = false, JsonFlags jsonFlags = JsonFlags.None)
        => this.FileSystem.WriteJsonToFileEncryptedAsync<T>(this.PathString, obj, password, flags, doNotOverwrite, cancel, includeNull, escapeHtml, maxDepth, compact, referenceHandling, jsonFlags);

    public T ReadJsonFromFile<T>(FileFlags flags = FileFlags.None, CancellationToken cancel = default,
        bool includeNull = false, int? maxDepth = Json.DefaultMaxDepth, bool nullIfError = false, JsonFlags jsonFlags = JsonFlags.None)
        => ReadJsonFromFileAsync<T>(flags, cancel, includeNull, maxDepth, nullIfError, jsonFlags)._GetResult();

    public long WriteJsonToFile<T>([AllowNull] T obj, FileFlags flags = FileFlags.None, bool doNotOverwrite = false, CancellationToken cancel = default,
        bool includeNull = false, bool escapeHtml = false, int? maxDepth = Json.DefaultMaxDepth, bool compact = false, bool referenceHandling = false, JsonFlags jsonFlags = JsonFlags.None)
        => WriteJsonToFileAsync<T>(obj, flags, doNotOverwrite, cancel, includeNull, escapeHtml, maxDepth, compact, referenceHandling, jsonFlags)._GetResult();

    public T ReadJsonFromFileEncrypted<T>(string password, int maxSize = int.MaxValue, FileFlags flags = FileFlags.None, CancellationToken cancel = default,
        bool includeNull = false, int? maxDepth = Json.DefaultMaxDepth, bool nullIfError = false, JsonFlags jsonFlags = JsonFlags.None)
        => ReadJsonFromFileEncryptedAsync<T>(password, maxSize, flags, cancel, includeNull, maxDepth, nullIfError, jsonFlags)._GetResult();

    public long WriteJsonToFileEncrypted<T>([AllowNull] T obj, string password, FileFlags flags = FileFlags.None, bool doNotOverwrite = false, CancellationToken cancel = default,
        bool includeNull = false, bool escapeHtml = false, int? maxDepth = Json.DefaultMaxDepth, bool compact = false, bool referenceHandling = false, JsonFlags jsonFlags = JsonFlags.None)
        => WriteJsonToFileEncryptedAsync<T>(obj, password, flags, doNotOverwrite, cancel, includeNull, escapeHtml, maxDepth, compact, referenceHandling, jsonFlags)._GetResult();

}


public class AutoArchiverOptions
{
    public DirectoryPath RootDir { get; }
    public DirectoryPath ArchiveDestDir { get; }
    public FileHistoryManagerPolicy HistoryPolicy { get; }
    public int PollingInterval { get; }

    public AutoArchiverOptions(DirectoryPath rootDir, FileHistoryManagerPolicy historyPolicy, string subDirName = Consts.FileNames.AutoArchiveSubDirName, int pollingInterval = Consts.Intervals.AutoArchivePollingInterval)
    {
        this.RootDir = rootDir;
        this.ArchiveDestDir = this.RootDir.GetSubDirectory(subDirName, path2NeverAbsolutePath: true);
        this.HistoryPolicy = historyPolicy;
        this.PollingInterval = pollingInterval._Max(1000);
    }
}

// 自動アーカイバ。指定したディレクトリ内のファイル (主に設定ファイル) を定期的に ZIP 圧縮して履歴ファイルとして世代保存する。
public class AutoArchiver : AsyncServiceWithMainLoop
{
    public AutoArchiverOptions Options { get; }

    FileSystem FileSystem => Options.RootDir.FileSystem;
    PathParser Parser => FileSystem.PathParser;

    readonly FileHistoryManager History;

    public AutoArchiver(AutoArchiverOptions option)
    {
        try
        {
            this.Options = option;

            this.History = new FileHistoryManager(new FileHistoryManagerOptions(PathToDateTime, Options.HistoryPolicy));

            this.StartMainLoop(MainLoop);
        }
        catch
        {
            this._DisposeSafe();
            throw;
        }
    }

    // .zip ファイルのパスを入力して日時を返す関数
    ResultOrError<DateTimeOffset> PathToDateTime(string path)
    {
        string ext = Parser.GetExtension(path);
        string fileName = Parser.GetFileNameWithoutExtension(path);

        // 拡張子の検査
        if (ext._IsSamei(Consts.Extensions.Zip) == false)
            return false;

        // ファイル名の検査 ("20190924-123456+0900" という形式のファイル名をパースする)
        return Str.FileNameStrToDateTimeOffset(fileName);
    }

    async Task MainLoop(CancellationToken cancel)
    {
        while (cancel.IsCancellationRequested == false)
        {
            // 同一ディレクトリを対象とした他プロセスによる活動がある場合は本プロセスは何も実施をしない
            SingleInstance? singleInstance = SingleInstance.TryGet("Archiver_" + Options.RootDir.ToString());

            if (singleInstance != null)
            {
                try
                {
                    await Poll(cancel);
                }
                finally
                {
                    singleInstance._DisposeSafe();
                }
            }

            await cancel._WaitUntilCanceledAsync(Util.GenRandInterval(Options.PollingInterval));
        }
    }

    async Task Poll(CancellationToken cancel)
    {
        DateTimeOffset now = DateTimeOffset.Now;

        if ((await Options.RootDir.IsDirectoryExistsAsync(cancel)) == false)
        {
            // 入力元ディレクトリ未存在
            return;
        }

        FileSystemEntity[] zipFiles = new FileSystemEntity[0];

        if ((await Options.ArchiveDestDir.IsDirectoryExistsAsync(cancel)))
        {
            // バックアップ先のディレクトリのファイル名一覧 (.zip の一覧) を取得する
            zipFiles = await Options.ArchiveDestDir.EnumDirectoryAsync(false, EnumDirectoryFlags.NoGetPhysicalSize, cancel);
        }

        // 新しいバックアップを実施すると仮定した場合の出力先ファイル名を決定する
        FilePath destZipFile = Options.ArchiveDestDir.Combine(Str.DateTimeOffsetToFileNameStr(now) + Consts.Extensions.Zip);

        // .ziptmp ファイル名を決定する
        FilePath destZipTempPath = Options.ArchiveDestDir.Combine("_output.zip.tmp");

        // 今すぐ新しいバックアップを実施すべきかどうか判断をする
        if (History.DetermineIsNewFileToCreate(zipFiles.Where(x => x.IsFile).Select(x => x.FullPath), destZipFile, now) == false)
        {
            // 実施しない
            return;
        }

        // 入力元のディレクトリを列挙して 1 つでもファイルが存在するかどうか確認する
        DirectoryWalker walker = new DirectoryWalker(FileSystem, EnumDirectoryFlags.NoGetPhysicalSize);

        RefBool isAnyFileExists = new RefBool(false);

        await walker.WalkDirectoryAsync(Options.RootDir,
            (pathInfo, dir, c) =>
            {
                if (pathInfo.FullPath._IsSamei(Options.ArchiveDestDir.PathString) == false)
                {
                    if (dir.Where(x => x.IsFile).Any())
                    {
                        isAnyFileExists.Set(true);
                        return TR(false);
                    }
                }

                return TR(true);
            },
            exceptionHandler: (pathInfo, ex, c) =>
            {
                ex._Debug();
                return TR(false);
            },
            cancel: cancel);

        if (isAnyFileExists == false)
        {
            // ファイルが 1 つも存在していないのでバックアップは実施いたしません
            return;
        }

        // 出力先ディレクトリに .gitignore を作成する
        // (ディレクトリがまだ存在しない場合は作成する)
        Util.PutGitIgnoreFileOnDirectory(Options.ArchiveDestDir, FileFlags.AutoCreateDirectory);

        // バックアップを実施いたします
        // (まずは .zip.tmp ファイルに出力をいたします)
        try
        {
            await Lfs.CreateZipArchiveAsync(destZipTempPath, Options.RootDir, new FileContainerEntityParam(flags: FileContainerEntityFlags.EnableCompression),
                (entity) =>
                {
                    if (Parser.NormalizeDirectorySeparator(entity.FullPath, true).StartsWith(Parser.NormalizeDirectorySeparator(Options.ArchiveDestDir, true), StringComparison.OrdinalIgnoreCase))
                    {
                        // 出力先ディレクトリに含まれているファイルはバックアップいたしません (無限肥大が発生してしまうことを避けるため)
                        return false;
                    }

                    return true;
                },
                (pathinfo, ex, c) =>
                {
                    ex._Debug();
                    return TR(true);
                },
                cancel: cancel);
        }
        catch
        {
            // 例外が発生しました。一時的に出力中であった .zip.tmp ファイルを削除いたします
            try
            {
                await destZipTempPath.DeleteFileAsync(cancel: cancel);
            }
            catch { }
            throw;
        }

        // .zip.tmp ファイルを .zip ファイルにリネームしてバックアップ処理を完了します
        await destZipTempPath.MoveFileAsync(destZipFile, cancel);

        // 古くなった履歴ファイルを削除します
        List<string> deleteList = History.GenerateFileListToDelete(zipFiles.Where(x => x.IsFile).Select(x => x.FullPath), now);

        foreach (string deleteFile in deleteList)
        {
            cancel.ThrowIfCancellationRequested();

            try
            {
                await FileSystem.DeleteFileAsync(deleteFile, cancel: cancel);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                ex._Debug();
            }
        }
    }
}

public class FileHistoryManagerPolicy : IValidatable
{
    IReadOnlyList<PolicyEntry> List => InternalList;

    List<PolicyEntry> InternalList = new List<PolicyEntry>();

    public class PolicyEntry
    {
        public TimeSpan ThresholdSinceNow { get; }
        public TimeSpan IntervalBetweenNextFile { get; }
        public string Guid { get; }

        public PolicyEntry(TimeSpan thresholdSinceNow, TimeSpan intervalBetweenNextFile)
        {
            if (thresholdSinceNow < new TimeSpan(0)) throw new ArgumentOutOfRangeException(nameof(thresholdSinceNow));
            if (intervalBetweenNextFile < new TimeSpan(0, 0, 1)) throw new ArgumentOutOfRangeException(nameof(intervalBetweenNextFile));

            ThresholdSinceNow = thresholdSinceNow;
            IntervalBetweenNextFile = intervalBetweenNextFile;
            Guid = Str.NewGuid();
        }

        public override bool Equals(object? obj)
        {
            return this.Guid.Equals(((PolicyEntry)obj!).Guid);
        }

        public override int GetHashCode()
            => this.Guid.GetHashCode();
    }

    // 空ポリシー
    public FileHistoryManagerPolicy() { }

    // 標準的ポリシー
    public FileHistoryManagerPolicy(EnsureSpecial standard)
    {
        Add(new PolicyEntry(new TimeSpan(24, 0, 0), new TimeSpan(1, 0, 0)));

        Add(new PolicyEntry(new TimeSpan(7, 0, 0, 0), new TimeSpan(1, 0, 0, 0)));

        Add(new PolicyEntry(new TimeSpan(30, 0, 0, 0), new TimeSpan(7, 0, 0, 0)));

        Add(new PolicyEntry(TimeSpan.MaxValue, new TimeSpan(30, 0, 0, 0)));
    }

    public void Add(PolicyEntry e)
    {
        InternalList.Add(e);

        InternalList.Sort((x, y) => x.ThresholdSinceNow.CompareTo(y.ThresholdSinceNow));
    }

    public void Validate()
    {
        if (this.List.Where(x => x.ThresholdSinceNow == TimeSpan.MaxValue).Any() == false)
            throw new CoresException("There is no policy entry to describe TimeSpan.MaxValue.");
    }

    public PolicyEntry GetPolicyEntry(TimeSpan thresholdSinceNow)
    {
        foreach (var e in this.InternalList)
        {
            if (thresholdSinceNow <= e.ThresholdSinceNow)
                return e;
        }

        throw new CoresException("There is no policy entry to describe TimeSpan.MaxValue.");
    }

    public static FileHistoryManagerPolicy GetTestPolicy()
    {
        FileHistoryManagerPolicy ret = new FileHistoryManagerPolicy();

        ret.Add(new PolicyEntry(new TimeSpan(0, 0, 10), new TimeSpan(0, 0, 3)));
        ret.Add(new PolicyEntry(new TimeSpan(0, 0, 30), new TimeSpan(0, 0, 10)));
        ret.Add(new PolicyEntry(new TimeSpan(0, 2, 0), new TimeSpan(0, 0, 30)));
        ret.Add(new PolicyEntry(TimeSpan.MaxValue, new TimeSpan(0, 1, 0)));

        return ret;
    }
}

public class FileHistoryManagerOptions
{
    public FileHistoryManagerPolicy Policy { get; }
    public Func<string, ResultOrError<DateTimeOffset>> PathToDateTime { get; }

    public FileHistoryManagerOptions(Func<string, ResultOrError<DateTimeOffset>> pathToDateTime, FileHistoryManagerPolicy? policy = null)
    {
        if (policy == null) policy = new FileHistoryManagerPolicy(EnsureSpecial.Yes);

        policy.Validate();

        this.Policy = policy;

        this.PathToDateTime = pathToDateTime;
    }
}

// 時刻が進むにつれて良い具合に適当に古い不要ファイルを間引いて消してくれる履歴ファイルマネージャ
public class FileHistoryManager
{
    public FileHistoryManagerOptions Options { get; }

    public FileHistoryManager(FileHistoryManagerOptions options)
    {
        this.Options = options;
    }

    class Entry
    {
        public DateTimeOffset TimeStamp { get; }
        public string FullPath { get; }
        public TimeSpan TimeSpanSinceNow { get; }

        public Entry(DateTimeOffset timeStamp, string fullPath, DateTimeOffset now)
        {
            TimeStamp = timeStamp;
            FullPath = fullPath;
            TimeSpanSinceNow = (now - timeStamp);
        }
    }

    // ファイルパス一覧からエントリ一覧の生成
    List<Entry> GetEntryListFromFilePathList(IEnumerable<string> pathList, DateTimeOffset now)
    {
        List<Entry> ret = new List<Entry>();

        foreach (string path in pathList.Distinct())
        {
            ResultOrError<DateTimeOffset>? result = null;

            try
            {
                result = Options.PathToDateTime(path);
            }
            catch { }

            if (result != null && result.IsOk)
            {
                // 現在時刻よりも古いファイルのみを列挙の対象とする (現在時刻よりも新しいファイルは存在しないものとみなす)
                if (result.Value <= now)
                {
                    Entry e = new Entry(result.Value, path, now);

                    ret.Add(e);
                }
            }
        }

        return ret;
    }

    // 現在存在しているファイル一覧と新たに作成しようとしている最新のファイル名を入力し、そのファイルを作成するべきかどうかを判断するメソッド
    public bool DetermineIsNewFileToCreate(IEnumerable<string> existingPathList, string newPath, DateTimeOffset now = default)
    {
        if (now == default) now = DateTimeOffset.Now;

        var currentList = GetEntryListFromFilePathList(existingPathList, now).OrderByDescending(x => x.TimeStamp);

        ResultOrError<DateTimeOffset>? newDt = null;
        try
        {
            newDt = Options.PathToDateTime(newPath);
        }
        catch { }

        if (newDt != null && newDt.IsOk)
        {
            // 現存ファイルリスト中にある一番新しいファイル
            Entry? latest = currentList.FirstOrDefault();

            if (latest == null) return true; // ファイルが 1 つもない

            if (latest.TimeStamp >= newDt)
            {
                // 現存するファイルのほうが新しい
                return false;
            }

            // 現在時刻からの経過時間をもとに適用すべきポリシーを取得する
            var policy = this.Options.Policy.GetPolicyEntry(newDt.Value - DateTime.Now);

            if ((newDt - latest.TimeStamp) < policy.IntervalBetweenNextFile)
            {
                // ポリシーで指定された間隔以下しか時間差がないので保存をしない
                return false;
            }

            // 保存をする
            return true;
        }
        else
        {
            // パースに失敗
            return false;
        }
    }

    // 現在存在しているファイル一覧を入力し、削除すべきファイル一覧のリストを出力するメソッド
    public List<string> GenerateFileListToDelete(IEnumerable<string> existingPathList, DateTimeOffset now = default)
    {
        List<string> ret = new List<string>();

        if (now == default) now = DateTimeOffset.Now;

        var currentList = GetEntryListFromFilePathList(existingPathList, now).OrderByDescending(x => x.TimeStamp);

        // 現在存在する全ファイルに対して適用されるポリシーごとにリストを作成する
        // (時刻の逆順になっているはずである)
        Dictionary<FileHistoryManagerPolicy.PolicyEntry, List<Entry>> groupByPolicy = new Dictionary<FileHistoryManagerPolicy.PolicyEntry, List<Entry>>();

        foreach (Entry e in currentList)
        {
            FileHistoryManagerPolicy.PolicyEntry policy = this.Options.Policy.GetPolicyEntry(e.TimeSpanSinceNow);

            if (groupByPolicy.TryGetValue(policy, out List<Entry>? list) == false)
            {
                list = new List<Entry>();
                groupByPolicy.Add(policy, list);
            }

            list!.Add(e);
        }

        // それぞれのポリシー内で各ファイルを削除するかどうかの判定を行なう
        foreach (var policy in groupByPolicy.Keys)
        {
            List<Entry> list = groupByPolicy[policy];

            // 古い順
            List<Entry> list2 = list.OrderBy(x => x.TimeStamp).ToList();

            Entry? std = null;

            foreach (var cur in list2)
            {
                if (std == null)
                {
                    // 基準ファイルの選定
                    std = cur;
                }
                else
                {
                    // 基準ファイルとの時差を計算する
                    TimeSpan interval = cur.TimeStamp - std.TimeStamp;

                    if (interval < policy.IntervalBetweenNextFile)
                    {
                        // 時差が少なすぎるのでこのファイルは削除リストに投入する
                        ret.Add(cur.FullPath);
                    }
                    else
                    {
                        // 時差が大きいので このファイルを新たな基準ファイルとして選定し、削除はしない
                        std = cur;
                    }
                }
            }
        }

        // 念のため Distinct をしてから返す
        ret = ret.Distinct().ToList();

        return ret;
    }
}


public class EasyJsonDb<T>
{
    public FilePath FilePath { get; }
    Func<T> NewProc { get; }

    readonly AsyncLock ALock = new();

    public EasyJsonDb(FilePath filePath, Func<T> newProc)
    {
        this.FilePath = filePath;
        this.NewProc = newProc;
    }

    public async Task<T> GetAsync(CancellationToken cancel = default)
    {
        using (await ALock.LockWithAwait(cancel))
        {
            var data = await this.FilePath.FileSystem.ReadJsonFromFileAsync<T>(this.FilePath.PathString, flags: FilePath.Flags, withBackup: true, nullIfError: true);
            if (data == null)
            {
                data = this.NewProc();

                await this.FilePath.FileSystem.WriteJsonToFileAsync(this.FilePath.PathString, data, flags: FilePath.Flags | FileFlags.AutoCreateDirectory, withBackup: true);
            }

            return data;
        }
    }
    public T Get(CancellationToken cancel = default)
        => GetAsync()._GetResult();

    public async Task SetAsync(T data, CancellationToken cancel = default)
    {
        using (await ALock.LockWithAwait(cancel))
        {
            await this.FilePath.FileSystem.WriteJsonToFileAsync(this.FilePath.PathString, data, flags: FilePath.Flags | FileFlags.AutoCreateDirectory, withBackup: true);
        }
    }
    public void Set(T data, CancellationToken cancel = default)
        => SetAsync(data, cancel)._GetResult();

    public async Task<TResult> EditAsync<TResult>(Func<T, Task<TResult>> editProcAsync, CancellationToken cancel = default)
    {
        var data = await GetAsync(cancel);

        TResult ret = await editProcAsync(data);

        await SetAsync(data, cancel);

        return ret;
    }

    public TResult Edit<TResult>(Func<T, TResult> editProc, CancellationToken cancel = default)
        => EditAsync(data => editProc(data)._TR(), cancel)._GetResult();
}


// CSV ライター
public class CsvWriter<T> : AsyncService where T : notnull, new()
{
    readonly FieldReaderWriter Rw;
    readonly Stream FileStream;
    readonly BufferedStream BufferedStream;
    readonly Encoding Encoding;
    readonly bool PrintToConsole;

    public CsvWriter(string path, bool printToConsole = true, bool writeHeader = true, FileSystem? fs = null, int bufferSize = Consts.Numbers.DefaultLargeBufferSize, FileFlags flags = FileFlags.None, Encoding? encoding = null, bool writeBom = true)
    {
        try
        {
            if (fs == null) fs = Lfs;
            if (encoding == null) encoding = Str.Utf8Encoding;

            PrintToConsole = printToConsole;

            Encoding = encoding;

            T sample = new T();

            Rw = sample._GetFieldReaderWriter();

            var file = fs.Create(path, false, flags);

            FileStream = file.GetStream(true);

            BufferedStream = new BufferedStream(FileStream, bufferSize);

            if (writeBom)
            {
                var bom = Str.GetBOMSpan(encoding);

                if (bom.IsEmpty == false)
                    BufferedStream.Write(bom);
            }

            if (writeHeader)
            {
                string line = Str.ObjectHeaderToCsv<T>();

                WriteLine(line);
            }
        }
        catch
        {
            this._DisposeSafe();
            throw;
        }
    }

    public void WriteData(T data, params string[] additionalStrList)
    {
        WriteData(data, false, additionalStrList);
    }

    public void WriteData(T data, bool flush, params string[] additionalStrList)
    {
        WriteData(data, flush, additionalStrList);
    }

    public void WriteData(T data, IEnumerable<string>? additionalStrList)
    {
        WriteData(data, false, additionalStrList);
    }

    public void WriteData(T data, bool flush, IEnumerable<string>? additionalStrList)
    {
        string line = Str.ObjectDataToCsv(data, this.Rw, additionalStrList);

        WriteLine(line, flush);
    }

    void WriteLine(string line, bool flush = false)
    {
        if (PrintToConsole)
        {
            lock (Con.ConsoleWriteLock)
            {
                Console.WriteLine(line);
            }
        }

        line = line + Env.NewLine;

        var data = line._GetBytes(this.Encoding);

        this.BufferedStream.Write(data);

        if (flush)
        {
            this.BufferedStream.Flush();
        }
    }

    protected override async Task CleanupImplAsync(Exception? ex)
    {
        try
        {
            await BufferedStream._DisposeSafeAsync();
            await FileStream._DisposeSafeAsync();
        }
        finally
        {
            await base.CleanupImplAsync(ex);
        }
    }
}
