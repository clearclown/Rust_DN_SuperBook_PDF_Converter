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

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using Microsoft.Extensions.FileProviders;

namespace IPA.Cores.Basic;

public class ReadOnlyCacheFileSystemParam : ViewFileSystemParams
{
    public int MaxFiles { get; }
    public int MaxDirs { get; }
    public long MaxSingleFileSize { get; }
    public int PollingIntervalMsec { get; }

    public ReadOnlyCacheFileSystemParam(FileSystem underlayFileSystem, bool disposeUnderlay = false,
        int maxFiles = Consts.Numbers.ReadOnlyCacheFileSystem_DefaultMaxFiles,
        int maxDirs = Consts.Numbers.ReadOnlyCacheFileSystem_DefaultMaxDirs,
        long maxSingleFileSize = Consts.Numbers.ReadOnlyCacheFileSystem_DefaultMaxSingleFileSize,
        int pollingIntervalMsec = Consts.Numbers.ReadOnlyCacheFileSystem_DefaultPollIntervalMsecs)
        : base(underlayFileSystem, underlayFileSystem.PathParser.Style == FileSystemStyle.Windows ? PathParser.GetInstance(FileSystemStyle.Mac) : underlayFileSystem.PathParser, FileSystemMode.ReadOnly, disposeUnderlay)
    // Use the Mac OS X path parser if the underlay file system is Windows
    {
        // Windows ファイルシステムではそのままでは利用できません (Chroot すれば OK です)
        if (underlayFileSystem.PathParser.Style == FileSystemStyle.Windows)
        {
            throw new CoresLibException("underlayFileSystem.PathParser.Style must not be FileSystemStyle.Windows");
        }

        this.MaxFiles = maxFiles;
        this.MaxDirs = maxDirs;
        this.MaxSingleFileSize = maxSingleFileSize;
        this.PollingIntervalMsec = Math.Max(pollingIntervalMsec, 500);
    }
}

public class ReadOnlyCacheFileSystemFile : FileObject
{
    readonly HugeMemoryBuffer<byte> MemoryBuffer;

    public ReadOnlyCacheFileSystemFile(FileSystem fileSystem, FileParameters fileParams, HugeMemoryBuffer<byte> dataBuffer) : base(fileSystem, fileParams)
    {
        try
        {
            this.MemoryBuffer = dataBuffer;
            this.InitAndCheckFileSizeAndPosition(0, this.MemoryBuffer.Length);
        }
        catch
        {
            this._DisposeSafe();
            throw;
        }
    }

    protected override Task<int> ReadRandomImplAsync(long position, Memory<byte> data, CancellationToken cancel = default)
    {
        return this.MemoryBuffer.ReadRandom(position, data, cancel)._TR();
    }

    protected override Task WriteRandomImplAsync(long position, ReadOnlyMemory<byte> data, CancellationToken cancel = default)
        => throw new NotImplementedException();

    protected override Task<long> GetFileSizeImplAsync(CancellationToken cancel = default)
        => this.MemoryBuffer.Length._TR();

    protected override Task SetFileSizeImplAsync(long size, CancellationToken cancel = default)
        => throw new NotImplementedException();

    protected override Task FlushImplAsync(CancellationToken cancel = default)
        => TR();

    protected override Task CloseImplAsync()
        => TR();
}

public class ReadOnlyCacheFileSystem : ViewFileSystem
{
    protected new ReadOnlyCacheFileSystemParam Params => (ReadOnlyCacheFileSystemParam)base.Params;

    FsCacheRoot CacheData;

    IHolder? Leak = null;

    Task? MainLoopTask;

    public ReadOnlyCacheFileSystem(ReadOnlyCacheFileSystemParam param) : base(param)
    {
        try
        {
            // 空のキャッシュ
            this.CacheData = CreateCacheCoreAsync(true)._GetResult();

            this.MainLoopTask = TaskUtil.StartAsyncTaskAsync(c => UpdateCacheLoopTaskAsync((CancellationToken)c!), this.GrandCancel);
            Leak = LeakChecker.Enter(LeakCounterKind.AsyncServiceWithMainLoop);
        }
        catch (Exception ex)
        {
            this._DisposeSafe(ex);
            throw;
        }
    }

    protected override Task CreateDirectoryImplAsync(string directoryPath, FileFlags flags = FileFlags.None, CancellationToken cancel = default)
        => throw new NotImplementedException();

    protected override Task DeleteDirectoryImplAsync(string directoryPath, bool recursive, CancellationToken cancel = default)
        => throw new NotImplementedException();

    protected override Task DeleteFileImplAsync(string path, FileFlags flags = FileFlags.None, CancellationToken cancel = default)
        => throw new NotImplementedException();

    protected override Task MoveDirectoryImplAsync(string srcPath, string destPath, CancellationToken cancel = default)
        => throw new NotImplementedException();

    protected override Task MoveFileImplAsync(string srcPath, string destPath, CancellationToken cancel = default)
        => throw new NotImplementedException();

    protected override Task SetDirectoryMetadataImplAsync(string path, FileMetadata metadata, CancellationToken cancel = default)
        => throw new NotImplementedException();

    protected override Task SetFileMetadataImplAsync(string path, FileMetadata metadata, CancellationToken cancel = default)
        => throw new NotImplementedException();

    protected override Task<string> NormalizePathImplAsync(string path, CancellationToken cancel = default)
    {
        return this.PathParser.NormalizeUnixStylePathWithRemovingRelativeDirectoryElements(path)._TR();
    }

    protected override Task<bool> IsFileExistsImplAsync(string path, CancellationToken cancel = default)
    {
        // Where(path);
        var cache = this.CacheData;

        cancel.ThrowIfCancellationRequested();

        return cache.FilesTable.TryGetValue(path, out var entity)._TR();
    }

    protected override Task<bool> IsDirectoryExistsImplAsync(string path, CancellationToken cancel = default)
    {
        // Where(path);
        var cache = this.CacheData;

        cancel.ThrowIfCancellationRequested();

        return cache.DirsTable.TryGetValue(path, out var entity)._TR();
    }

    protected override async Task<FileMetadata> GetFileMetadataImplAsync(string path, FileMetadataGetFlags flags = FileMetadataGetFlags.DefaultAll, CancellationToken cancel = default)
    {
        // Where(path);
        var cache = this.CacheData;

        cancel.ThrowIfCancellationRequested();

        if (cache.FilesTable.TryGetValue(path, out var entity) == false)
        {
            throw new FileNotFoundException($"ReadOnlyCacheFileSystem: File \"{path}\" not found.");
        }

        if (cache.FileMetadataCache.TryGetValue(path, out var contents) == false)
        {
            var contents2 = new FsCacheMetadata();

            try
            {
                contents2.MetaData = await this.UnderlayFileSystem.GetFileMetadataAsync(path, cancel: cancel);
            }
            catch (Exception ex)
            {
                ex._Error();
                contents2.Exception = ex;
            }

            cache.FileMetadataCache[path] = contents2;

            contents = contents2;
        }

        if (contents.Exception != null)
        {
            throw contents.Exception;
        }

        return contents.MetaData;
    }

    protected override async Task<FileMetadata> GetDirectoryMetadataImplAsync(string path, FileMetadataGetFlags flags = FileMetadataGetFlags.DefaultAll, CancellationToken cancel = default)
    {
        // Where(path);

        var cache = this.CacheData;

        cancel.ThrowIfCancellationRequested();

        if (cache.DirsTable.TryGetValue(path, out var entity) == false)
        {
            throw new FileNotFoundException($"ReadOnlyCacheFileSystem: Directory \"{path}\" not found.");
        }

        if (cache.DirMetadataCache.TryGetValue(path, out var contents) == false)
        {
            var contents2 = new FsCacheMetadata();

            try
            {
                contents2.MetaData = await this.UnderlayFileSystem.GetDirectoryMetadataAsync(path, cancel: cancel);
            }
            catch (Exception ex)
            {
                ex._Error();
                contents2.Exception = ex;
            }

            cache.DirMetadataCache[path] = contents2;

            contents = contents2;
        }

        if (contents.Exception != null)
        {
            throw contents.Exception;
        }

        return contents.MetaData;
    }

    protected override async Task<FileObject> CreateFileImplAsync(FileParameters option, CancellationToken cancel = default)
    {
        var cache = this.CacheData;

        // Where(option.Path);

        cancel.ThrowIfCancellationRequested();

        if (option.Mode != FileMode.Open)
            throw new CoresLibException("ReadOnlyCacheFileSystem: option.Mode != FileMode.Open");
        if (option.Access != FileAccess.Read)
            throw new CoresLibException("ReadOnlyCacheFileSystem: option.Access != FileAccess.Read");

        if (cache.FilesTable.TryGetValue(option.Path, out var entity) == false)
            throw new FileNotFoundException($"ReadOnlyCacheFileSystem: File \"{option.Path}\" not found.");

        if (cache.FileContentsCache.TryGetValue(option.Path, out var contents) == false)
        {
            var contents2 = new FsCacheFile();

            try
            {
                if (entity.Size > this.Params.MaxSingleFileSize)
                {
                    throw new CoresException($"File \"{option.Path}\" size ({entity.Size}) > max size ({this.Params.MaxSingleFileSize})");
                }

                contents2.Body = await this.UnderlayFileSystem.ReadHugeMemoryBufferFromFileAsync(option.Path, this.Params.MaxSingleFileSize, cancel: cancel);
            }
            catch (Exception ex)
            {
                ex._Error();
                contents2.Exception = ex;
            }

            cache.FileContentsCache[option.Path] = contents2;

            contents = contents2;
        }

        if (contents.Exception != null)
        {
            throw contents.Exception;
        }

        return new ReadOnlyCacheFileSystemFile(this, option, contents.Body);
    }

    protected override Task<FileSystemEntity[]> EnumDirectoryImplAsync(string directoryPath, EnumDirectoryFlags flags, string wildcard, CancellationToken cancel = default)
    {
        // Where(directoryPath);

        if (flags.Bit(EnumDirectoryFlags.IncludeParentDirectory))
            throw new CoresLibException($"ReadOnlyCacheFileSystem: EnumDirectoryFlags.IncludeParentDirectory is not supported.");
        if (flags.Bit(EnumDirectoryFlags.AllowRelativePath))
            throw new CoresLibException($"ReadOnlyCacheFileSystem: EnumDirectoryFlags.AllowRelativePath is not supported.");

        var cache = this.CacheData;

        cancel.ThrowIfCancellationRequested();

        directoryPath = this.PathParser.RemoveLastSeparatorChar(directoryPath);

        if (cache.PerDirTable.TryGetValue(directoryPath, out var entities) == false)
            throw new FileNotFoundException($"ReadOnlyCacheFileSystem: Directory \"{directoryPath}\" not found.");

        if (cache.DirsTable.TryGetValue(directoryPath, out var thisDirEntity) == false)
            throw new FileNotFoundException($"ReadOnlyCacheFileSystem: Directory \"{directoryPath}\" not found.");

        List<FileSystemEntity> ret = new();

        if (flags.Bit(EnumDirectoryFlags.IncludeCurrentDirectory))
        {
            var e = thisDirEntity._CloneDeep();
            e.Name = ".";
            ret.Add(e);
        }

        foreach (var entity in entities)
        {
            var e = entity._CloneDeep();
            ret.Add(e);
        }

        return ret.ToArray()._TR();
    }

    // 定期的にディレクトリ内容物を列挙するタスク
    async Task UpdateCacheLoopTaskAsync(CancellationToken cancel = default)
    {
        // ファイルシステム watcher の作成
        await using var watcher = this.UnderlayFileSystem.CreateFileSystemEventWatcher("/");

        var waiter = watcher.AsyncPulse.GetPulseWaiter();

        while (true)
        {
            if (cancel.IsCancellationRequested)
            {
                break;
            }

            try
            {
                // キャッシュ更新
                this.CacheData = await CreateCacheCoreAsync(false, cancel);
            }
            catch (Exception ex)
            {
                ex._Error();
            }

            await waiter.WaitAsync(this.Params.PollingIntervalMsec, cancel);

            //await cancel._WaitUntilCanceledAsync(this.Params.PollingIntervalMsec);
        }
    }

    protected override async Task CleanupImplAsync(Exception? ex)
    {
        try
        {
            try
            {
                if (MainLoopTask != null)
                {
                    try
                    {
                        await MainLoopTask;
                    }
                    finally
                    {
                        MainLoopTask = null;
                    }
                }
            }
            catch (TaskCanceledException) { }
            catch (OperationCanceledException) { }
        }
        finally
        {
            Leak._DisposeSafe();

            await base.CleanupImplAsync(ex);
        }
    }

    public class FsCacheFile
    {
        public Exception? Exception = null;
        public HugeMemoryBuffer<byte> Body = null!;
    }

    public class FsCacheMetadata
    {
        public Exception? Exception = null;
        public FileMetadata MetaData = null!;
    }

    public class FsCacheRoot
    {
        public readonly StrDictionary<FileSystemEntity> FilesTable; // 存在するすべてのファイル情報
        public readonly StrDictionary<FileSystemEntity> DirsTable; // 存在するすべてのディレクトリ情報
        public readonly StrDictionary<List<FileSystemEntity>> PerDirTable; // ディレクトリごとの内容物の情報
        public readonly ConcurrentStrDictionary<FsCacheFile> FileContentsCache; // ファイルコンテンツ
        public readonly ConcurrentStrDictionary<FsCacheMetadata> FileMetadataCache; // ファイルメタデータキャッシュ
        public readonly ConcurrentStrDictionary<FsCacheMetadata> DirMetadataCache; // ディレクトリメタデータキャッシュ

        public FsCacheRoot(IEqualityComparer<string> cmp)
        {
            this.FilesTable = new(cmp);
            this.DirsTable = new(cmp);
            this.PerDirTable = new(cmp);
            this.FileContentsCache = new(cmp);
            this.FileMetadataCache = new(cmp);
            this.DirMetadataCache = new(cmp);
        }
    }

    // キャッシュを生成する
    public async Task<FsCacheRoot> CreateCacheCoreAsync(bool dummyEmpty, CancellationToken cancel = default)
    {
        var options = new FileSystemEnumDirectoryRecursiveOptions
        {
            DoNotThrowExceptionWhenMaxExceeds = true,
            MaxDirs = this.Params.MaxDirs,
            MaxFiles = this.Params.MaxFiles,
        };

        FileSystemEntity[] entities;

        if (dummyEmpty == false)
        {
            entities = await this.UnderlayFileSystem.EnumDirectoryAsync("/", options, true, flags: EnumDirectoryFlags.IncludeCurrentDirectory | EnumDirectoryFlags.IgnoreErrorDuringRecursiveEnum, cancel: cancel);
        }
        else
        {
            // ダミー空っぽ
            FileSystemEntity root = new FileSystemEntity("/", "/", FileAttributes.Directory, ZeroDateTimeOffsetForFileSystem, ZeroDateTimeOffsetForFileSystem, ZeroDateTimeOffsetForFileSystem);
            entities = root._SingleArray();
        }

        FsCacheRoot ret = new(this.PathParser.PathStringComparer);

        // まず、すべてのファイルとディレクトリを列挙
        foreach (var entity in entities)
        {
            if (entity.IsDirectory)
            {
                ret.DirsTable.Add(entity.FullPath, entity);

                ret.PerDirTable.Add(entity.FullPath, new());
            }
            else if (entity.IsFile)
            {
                ret.FilesTable.Add(entity.FullPath, entity);
            }
        }

        // 次に、すべてのファイルおよびディレクトリを列挙しつつ、それらのファイルが格納されているディレクトリごとに構造を構成する
        foreach (var entity in entities)
        {
            if (entity.FullPath != "/")
            {
                string parentDirPath = this.UnderlayPathParser.GetDirectoryName(entity.FullPath);

                var parentDirObj = ret.PerDirTable._GetOrDefault(parentDirPath);
                if (parentDirObj == null)
                {
                    // なぜか親ディレクトリのオブジェクトが見つからない!!
                    throw new CoresLibException($"CacheFs: PerDirTable._GetOrDefault(parentDirPath (\"{parentDirPath}\")) not found.");
                }
                else
                {
                    // 追加
                    parentDirObj.Add(entity);
                }
            }
        }

        return ret;
    }

    //public string MapPathVirtualToPhysical(string virtualPath)
    //{
    //    return virtualPath;
    //    //IRewriteVirtualPhysicalPath? underlayIf = this.UnderlayFileSystem as IRewriteVirtualPhysicalPath;
    //    //if (underlayIf == null)
    //    //    throw new NotImplementedException();

    //    //return underlayIf.MapPathVirtualToPhysical(virtualPath);
    //}

    //public string MapPathPhysicalToVirtual(string physicalPath)
    //{
    //    return physicalPath;
    //    //IRewriteVirtualPhysicalPath? underlayIf = this.UnderlayFileSystem as IRewriteVirtualPhysicalPath;
    //    //if (underlayIf == null)
    //    //    throw new NotImplementedException();

    //    //return underlayIf.MapPathPhysicalToVirtual(physicalPath);
    //}
}


