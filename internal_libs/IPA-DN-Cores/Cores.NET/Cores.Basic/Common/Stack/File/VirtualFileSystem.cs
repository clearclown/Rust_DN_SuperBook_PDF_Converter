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
using System.Collections.Immutable;
using Microsoft.Extensions.FileProviders;
using System.Diagnostics.CodeAnalysis;

#pragma warning disable CS1998

namespace IPA.Cores.Basic;

public class VfsException : Exception
{
    public VfsException(string path, string message) : base($"Entity name \"{path}\": {message}") { }
}

public class VfsNotFoundException : VfsException
{
    public VfsNotFoundException(string path, string message) : base(path, message) { }
}

public abstract class VfsEntity
{
    public VirtualFileSystem FileSystem { get; }
    readonly RefInt LinkRef = new RefInt();
    readonly RefInt HandleRef = new RefInt();
    protected readonly CriticalSection LockObj = new CriticalSection<VfsEntity>();

    public abstract string Name { get; protected set; }
    public abstract FileAttributes Attributes { get; protected set; }
    public abstract DateTimeOffset CreationTime { get; protected set; }
    public abstract DateTimeOffset LastWriteTime { get; protected set; }
    public abstract DateTimeOffset LastAccessTime { get; protected set; }

    public virtual Task<FileMetadata> GetMetadataAsync(CancellationToken cancel = default) => throw new NotSupportedException();
    public virtual Task SetMetadataAsync(FileMetadata metadata, CancellationToken cancel = default) => throw new NotSupportedException();

    volatile bool ReleasedFlag = false;
    public bool IsReleased => this.LinkRef.Value <= 0 || ReleasedFlag;

    public VfsEntity(VirtualFileSystem fileSystem)
    {
        this.FileSystem = fileSystem;
    }

    protected abstract Task ReleaseLinkImplAsync();

    public async Task<int> ReleaseLinkAsync(bool force = false)
    {
        if (force)
        {
            ReleasedFlag = true;
            try
            {
                await ReleaseLinkImplAsync();
            }
            catch { }
            return 0;
        }

        int r;

        lock (LockObj)
        {
            Debug.Assert(this.HandleRef.Value >= 0);
            if (this.HandleRef.Value >= 1)
            {
                throw new VfsException(this.Name, "The object handle is still opened.");
            }

            r = LinkRef.Decrement();
            Debug.Assert(r >= 0);
        }

        if (r == 0)
        {
            try
            {
                await ReleaseLinkImplAsync();
                ReleasedFlag = true;
            }
            catch
            {
                LinkRef.Increment();
                throw;
            }
        }
        return r;
    }

    public int AddLinkRef()
    {
        lock (LockObj)
        {
            int r = LinkRef.Increment();
            Debug.Assert(r >= 1);
            return r;
        }
    }

    public int AddHandleRef()
    {
        lock (LockObj)
        {
            if (this.IsReleased)
                throw new VfsException(this.Name, "The object is already released.");

            int r = HandleRef.Increment();
            Debug.Assert(r >= 1);
            return r;
        }
    }

    public int ReleaseHandleRef()
    {
        lock (LockObj)
        {
            int r = HandleRef.Decrement();
            if (r < 0)
                Dbg.Break();
            Debug.Assert(r >= 0);
            return r;
        }
    }
}

public abstract class VfsFile : VfsEntity
{
    public VfsFile(VirtualFileSystem fileSystem) : base(fileSystem)
    {
    }

    public abstract long Size { get; protected set; }
    public abstract long PhysicalSize { get; protected set; }

    public abstract Task<FileObject> OpenAsync(FileParameters option, string fullPath, CancellationToken cancel = default);

    public override async Task<FileMetadata> GetMetadataAsync(CancellationToken cancel = default)
    {
        lock (this.LockObj)
        {
            return new FileMetadata(
                isDirectory: false,
                attributes: (this.Attributes) & ~FileAttributes.Directory,
                creationTime: this.CreationTime,
                lastWriteTime: this.LastWriteTime,
                lastAccessTime: this.LastAccessTime,
                size: this.Size,
                physicalSize: this.PhysicalSize
                );
        }
    }
}

public abstract class VfsDirectory : VfsEntity
{
    public VfsDirectory(VirtualFileSystem fileSystem) : base(fileSystem)
    {
    }

    public virtual bool IsRoot { get; protected set; }
    public abstract Task<VfsEntity[]> EnumEntitiesAsync(EnumDirectoryFlags flags, CancellationToken cancel = default);
    public abstract Task<ResultOrExeption<ValueHolder<VfsEntity>>> OpenEntityAsync(string name, CancellationToken cancel = default);
    public abstract Task AddDirectoryAsync(VfsDirectory directory, CancellationToken cancel = default);
    public abstract Task RemoveDirectoryAsync(VfsDirectory directory, CancellationToken cancel = default);
    public abstract Task AddFileAsync(VfsFile file, CancellationToken cancel = default);
    public abstract Task RemoveFileAsync(VfsFile file, CancellationToken cancel = default);

    public virtual async Task ParseAsync(VfsPathParserContext ctx, CancellationToken cancel = default)
    {
        if (ctx.RemainingPathElements.TryPeek(out string? nextName) == false)
        {
            // This is the final entity
            ctx.Exception = null;
            return;
        }

        nextName._MarkNotNull();

        try
        {
            ResultOrExeption<ValueHolder<VfsEntity>> result = await this.OpenEntityAsync(nextName, cancel);

            if (result.IsError && result.Exception is VfsNotFoundException)
            {
                ctx.Exception = new VfsNotFoundException(ctx.SpecifiedPath,
                    $"The object \"{nextName}\" is not found on the directory \"{this.FileSystem.PathParser.BuildAbsolutePathStringFromElements(ctx.NormalizedPathStack)}\".");

                return;
            }

            using (var nextEntityHolder = result.Value)
            {
                ctx.RemainingPathElements.Dequeue();

                VfsEntity nextEntity = nextEntityHolder.Value;

                if (nextEntity is VfsDirectory nextDirectory)
                {
                    ctx.AddToEntityStack(nextDirectory);

                    await nextDirectory.ParseAsync(ctx, cancel);
                }
                else if (nextEntity is VfsFile nextFile)
                {
                    if (ctx.RemainingPathElements.Count >= 1)
                    {
                        throw new VfsNotFoundException(ctx.SpecifiedPath,
                            $"Invalid directory name. The file \"{nextName}\" is found on the directory \"{this.FileSystem.PathParser.BuildAbsolutePathStringFromElements(ctx.NormalizedPathStack)}\".");
                    }

                    ctx.AddToEntityStack(nextFile);
                    ctx.Exception = null;

                    return;
                }
                else
                {
                    throw new VfsException(ctx.SpecifiedPath,
                        $"The object \"{nextName}\" is unknown type \"{nextEntity.GetType().ToString()}\".");
                }
            }
        }
        catch (VfsNotFoundException)
        {
            ctx.Exception = new VfsNotFoundException(ctx.SpecifiedPath,
                $"The object \"{nextName}\" is not found on the directory \"{this.FileSystem.PathParser.BuildAbsolutePathStringFromElements(ctx.NormalizedPathStack)}\".");

            return;
        }
    }

    public override async Task<FileMetadata> GetMetadataAsync(CancellationToken cancel = default)
    {
        lock (this.LockObj)
        {
            return new FileMetadata(
                isDirectory: true,
                attributes: (this.Attributes | FileAttributes.Directory) & ~FileAttributes.Normal,
                creationTime: this.CreationTime,
                lastWriteTime: this.LastWriteTime,
                lastAccessTime: this.LastAccessTime
                );
        }
    }
}

public class VfsRamDirectory : VfsDirectory
{
    readonly Dictionary<string, VfsEntity> EntityTable;
    readonly AsyncLock AsyncLock = new AsyncLock();

    public override string Name { get; protected set; }
    public override FileAttributes Attributes { get; protected set; }
    public override DateTimeOffset CreationTime { get; protected set; }
    public override DateTimeOffset LastWriteTime { get; protected set; }
    public override DateTimeOffset LastAccessTime { get; protected set; }

    public VfsRamDirectory(VirtualFileSystem fileSystem, string name, bool isRoot = false) : base(fileSystem)
    {
        if (isRoot)
            name = "";
        else
            fileSystem.PathParser.ValidateFileOrDirectoryName(name);

        this.IsRoot = isRoot;

        this.Name = name;
        this.Attributes = FileAttributes.Directory;
        this.CreationTime = this.LastAccessTime = this.LastWriteTime = DateTimeOffset.Now;

        this.EntityTable = new Dictionary<string, VfsEntity>(fileSystem.PathParser.PathStringComparer);
    }

    public override async Task AddDirectoryAsync(VfsDirectory directory, CancellationToken cancel = default)
    {
        using (await AsyncLock.LockWithAwait(cancel))
        {
            int r = directory.AddLinkRef();
            try
            {
                if (r >= 2) throw new VfsException(directory.Name, "The directory object is already referenced by other directory.");
                if (directory.FileSystem != this.FileSystem) throw new VfsException(directory.Name, "directory.FileSystem != this.FileSystem");
                FileSystem.PathParser.ValidateFileOrDirectoryName(directory.Name);
                if (EntityTable.ContainsKey(directory.Name))
                    throw new VfsException(directory.Name, "The same name already exists.");
                EntityTable.Add(directory.Name, directory);
            }
            catch
            {
                await directory.ReleaseLinkAsync();
                throw;
            }
        }
    }

    public override async Task AddFileAsync(VfsFile file, CancellationToken cancel = default)
    {
        using (await AsyncLock.LockWithAwait(cancel))
        {
            int r = file.AddLinkRef();
            try
            {
                if (file.FileSystem != this.FileSystem) throw new VfsException(file.Name, "file.FileSystem != this.FileSystem");
                FileSystem.PathParser.ValidateFileOrDirectoryName(file.Name);
                if (EntityTable.ContainsKey(file.Name))
                    throw new VfsException(file.Name, "The same name already exists.");
                EntityTable.Add(file.Name, file);
            }
            catch
            {
                await file.ReleaseLinkAsync();
                throw;
            }
        }
    }

    public override async Task<VfsEntity[]> EnumEntitiesAsync(EnumDirectoryFlags flags, CancellationToken cancel = default)
    {
        using (await AsyncLock.LockWithAwait(cancel))
        {
            var ret = this.EntityTable.Values.OrderBy(x => x.Name, this.FileSystem.PathParser.PathStringComparer).ToArray();

            return ret;
        }
    }

    public override async Task<ResultOrExeption<ValueHolder<VfsEntity>>> OpenEntityAsync(string name, CancellationToken cancel = default)
    {
        using (await AsyncLock.LockWithAwait(cancel))
        {
            if (this.EntityTable.TryGetValue(name, out VfsEntity? entity) == false)
            {
                // エラー
                return new VfsNotFoundException(name, $"The object \"{name}\" not found on the directory.");
            }

            entity._MarkNotNull();

            entity.AddHandleRef();

            return new ValueHolder<VfsEntity>(
                (e) =>
                {
                    e.ReleaseHandleRef();
                },
                entity,
                leakCheckKind: LeakCounterKind.VfsOpenEntity);
        }
    }

    public override async Task RemoveDirectoryAsync(VfsDirectory directory, CancellationToken cancel = default)
    {
        using (await AsyncLock.LockWithAwait(cancel))
        {
            if (EntityTable.ContainsValue(directory) == false)
                throw new VfsException(directory.Name, $"The object is not contained on the parent directory \"{this.Name}\".");

            await directory.ReleaseLinkAsync();

            EntityTable.Remove(directory.Name);
        }
    }

    public override async Task RemoveFileAsync(VfsFile file, CancellationToken cancel = default)
    {
        using (await AsyncLock.LockWithAwait(cancel))
        {
            if (EntityTable.ContainsValue(file) == false)
                throw new VfsException(file.Name, $"The object is not contained on the parent directory \"{this.Name}\".");

            await file.ReleaseLinkAsync();

            EntityTable.Remove(file.Name);
        }
    }

    protected override async Task ReleaseLinkImplAsync()
    {
        using (await AsyncLock.LockWithAwait())
        {
            if (EntityTable.Count > 0)
                throw new VfsException(this.Name, $"This directory has one or more entities.");
        }
    }

    public override async Task SetMetadataAsync(FileMetadata metadata, CancellationToken cancel = default)
    {
        lock (this.LockObj)
        {
            if (metadata.Attributes.HasValue)
                this.Attributes = metadata.Attributes.Value | FileAttributes.Directory & ~FileAttributes.Normal;

            if (metadata.CreationTime.HasValue)
                this.CreationTime = metadata.CreationTime.Value;

            if (metadata.LastWriteTime.HasValue)
                this.LastWriteTime = metadata.LastWriteTime.Value;

            if (metadata.LastAccessTime.HasValue)
                this.LastAccessTime = metadata.LastAccessTime.Value;
        }
    }
}

public abstract class VfsRandomAccessFile : VfsFile
{
    public override string Name { get; protected set; }
    public override FileAttributes Attributes { get; protected set; }
    public override DateTimeOffset CreationTime { get; protected set; }
    public override DateTimeOffset LastWriteTime { get; protected set; }
    public override DateTimeOffset LastAccessTime { get; protected set; }

    public class FileImpl : FileObjectRandomAccessWrapperBase
    {
        readonly VfsRandomAccessFile File;
        readonly string FullPath;

        public FileImpl(VfsRandomAccessFile file, IRandomAccess<byte> randomAccessBase, string fullPath, FileSystem fileSystem, FileParameters fileParams)
            : base(new ConcurrentRandomAccess<byte>(randomAccessBase), fileSystem, fileParams)
        {
            this.File = file;
            this.File.AddHandleRef();

            try
            {
                this.FullPath = fullPath;
            }
            catch
            {
                this.File.ReleaseHandleRef();
                throw;
            }
        }

        public override string FinalPhysicalPath => base.FinalPhysicalPath;

        protected override void OnCloseImpl()
        {
            this.File.ReleaseHandleRef();
        }

        protected override Task<int> ReadRandomImplAsync(long position, Memory<byte> data, CancellationToken cancel = default)
        {
            this.File.LastAccessTime = DateTimeOffset.Now;
            return base.ReadRandomImplAsync(position, data, cancel);
        }

        protected override Task SetFileSizeImplAsync(long size, CancellationToken cancel = default)
        {
            this.File.LastWriteTime = DateTimeOffset.Now;
            return base.SetFileSizeImplAsync(size, cancel);
        }

        protected override async Task WriteRandomImplAsync(long position, ReadOnlyMemory<byte> data, CancellationToken cancel = default)
        {
            await base.WriteRandomImplAsync(position, data, cancel);

            this.File.LastWriteTime = DateTimeOffset.Now;
        }
    }

    public override long Size
    {
        get
        {
            using (var access = CreateConcurrentRandomAccess())
                return access.GetFileSize();
        }
        protected set => throw new NotSupportedException();
    }

    public override long PhysicalSize
    {
        get
        {
            using (var access = CreateConcurrentRandomAccess())
                return access.GetPhysicalSize();
        }
        protected set => throw new NotSupportedException();
    }

    protected abstract IRandomAccess<byte> GetSharedRandomAccessBaseImpl();
    protected ConcurrentRandomAccess<byte> CreateConcurrentRandomAccess() => new ConcurrentRandomAccess<byte>(GetSharedRandomAccessBaseImpl());

    public VfsRandomAccessFile(VirtualFileSystem fileSystem, string fileName) : base(fileSystem)
    {
        fileSystem.PathParser.ValidateFileOrDirectoryName(fileName);

        this.Name = fileName;

        this.Attributes = FileAttributes.Normal;

        this.CreationTime = this.LastAccessTime = this.LastWriteTime = DateTimeOffset.Now;
    }

    public override async Task<FileObject> OpenAsync(FileParameters option, string fullPath, CancellationToken cancel = default)
    {
        this.LastAccessTime = DateTimeOffset.Now;

        FileImpl impl = new FileImpl(this, GetSharedRandomAccessBaseImpl(), fullPath, this.FileSystem, option);

        return impl;
    }

    protected override Task ReleaseLinkImplAsync()
    {
        return Task.CompletedTask;
    }
}

public class VfsRamFile : VfsRandomAccessFile
{
    HugeMemoryBuffer<byte> Buffer;

    public VfsRamFile(VirtualFileSystem fileSystem, string fileName) : base(fileSystem, fileName)
    {
        Buffer = new HugeMemoryBuffer<byte>();
    }

    protected override IRandomAccess<byte> GetSharedRandomAccessBaseImpl()
        => this.Buffer;


    public override async Task SetMetadataAsync(FileMetadata metadata, CancellationToken cancel = default)
    {
        lock (this.LockObj)
        {
            if (metadata.Attributes.HasValue)
                this.Attributes = metadata.Attributes.Value | FileAttributes.Directory & ~FileAttributes.Normal;

            if (metadata.CreationTime.HasValue)
                this.CreationTime = metadata.CreationTime.Value;

            if (metadata.LastWriteTime.HasValue)
                this.LastWriteTime = metadata.LastWriteTime.Value;

            if (metadata.LastAccessTime.HasValue)
                this.LastAccessTime = metadata.LastAccessTime.Value;
        }
    }
}

public class VfsPathParserContext : IDisposable
{
    readonly PathParser Parser;
    public readonly string SpecifiedPath;
    public readonly Queue<string> RemainingPathElements = new Queue<string>();

    public VfsPathParserContext(PathParser parser, string[] pathElements, VfsEntity root)
    {
        this.Parser = parser;
        foreach (string element in pathElements)
        {
            this.RemainingPathElements.Enqueue(element);
        }
        this.SpecifiedPath = this.Parser.BuildAbsolutePathStringFromElements(pathElements);

        Exception = new VfsException(SpecifiedPath, "Unknown path parser error.");

        AddToEntityStack(root);
    }

    public readonly List<VfsEntity> EntityStack = new List<VfsEntity>();
    public readonly List<string> NormalizedPathStack = new List<string>();
    public string NormalizedPath => Parser.BuildAbsolutePathStringFromElements(NormalizedPathStack);
    public Exception? Exception = null;
    public VfsEntity? LastEntity => EntityStack.LastOrDefault();

    public void AddToEntityStack(VfsEntity entity)
    {
        entity.AddHandleRef();
        EntityStack.Add(entity);
        NormalizedPathStack.Add(entity.Name);
    }

    public void Dispose() { this.Dispose(true); GC.SuppressFinalize(this); }
    Once DisposeFlag;
    protected virtual void Dispose(bool disposing)
    {
        if (!disposing || DisposeFlag.IsFirstCall() == false) return;

        foreach (VfsEntity entity in EntityStack)
            entity.ReleaseHandleRef();
    }
}

public class VirtualFileSystemParams : FileSystemParams
{
    public VirtualFileSystemParams(FileSystemMode mode = FileSystemMode.Default) : base(PathParser.GetInstance(FileSystemStyle.Linux), mode) { }
}

public class VirtualFileSystem : FileSystem
{
    protected new VirtualFileSystemParams Params => (VirtualFileSystemParams)base.Params;

    readonly VfsDirectory Root;

    public VirtualFileSystem(VirtualFileSystemParams param) : base(param)
    {
        var rootDir = new VfsRamDirectory(this, "/", true);
        rootDir.AddLinkRef();

        this.Root = rootDir;
    }

    async Task<VfsPathParserContext> ParsePathInternalAsync(string path, CancellationToken cancel = default)
    {
        string[] pathStack = this.PathParser.SplitAbsolutePathToElementsUnixStyle(path);

        VfsPathParserContext ctx = new VfsPathParserContext(this.PathParser, pathStack, Root);
        try
        {
            await Root.ParseAsync(ctx, cancel);

            return ctx;
        }
        catch
        {
            ctx._DisposeSafe();
            throw;
        }
    }

    protected override async Task CreateDirectoryImplAsync(string directoryPath, FileFlags flags = FileFlags.None, CancellationToken cancel = default)
    {
        using (VfsPathParserContext ctx = await ParsePathInternalAsync(directoryPath, cancel))
        {
            if (ctx.Exception == null)
            {
                if (ctx.LastEntity is VfsDirectory)
                {
                    // Already exists
                    return;
                }
                else
                {
                    // There is existing another type object
                    throw new VfsException(directoryPath, $"There are existing object at the speficied path.");
                }
            }

            if (ctx.Exception is VfsNotFoundException && ctx.RemainingPathElements.Count >= 1 && ctx.LastEntity is VfsDirectory)
            {
                var lastDir = (VfsDirectory)ctx.LastEntity;

                while (true)
                {
                    if (ctx.RemainingPathElements.TryDequeue(out string? nextDirName) == false)
                        return;

                    nextDirName._MarkNotNull();

                    var newDirectory = new VfsRamDirectory(this, nextDirName);
                    try
                    {
                        await lastDir.AddDirectoryAsync(newDirectory);
                    }
                    catch
                    {
                        await newDirectory.ReleaseLinkAsync(true);
                        throw;
                    }

                    lastDir = newDirectory;
                }
            }
            else
            {
                throw ctx.Exception;
            }
        }
    }

    protected override async Task<FileObject> CreateFileImplAsync(FileParameters option, CancellationToken cancel = default)
    {
        return await this.AddFileAsync(option,
            async (newFilename, newFileOption, c) =>
            {
                return new VfsRamFile(this, newFilename);
            },
            cancel);
    }

    public delegate Task<VfsFile> CreateFileCallback(string newFilename, FileParameters newFileOption, CancellationToken cancel);

    protected async Task<FileObject> AddFileAsync(FileParameters option, CreateFileCallback createFileCallback, CancellationToken cancel = default, bool noOpen = false)
    {
        using (VfsPathParserContext ctx = await ParsePathInternalAsync(option.Path, cancel))
        {
            if (ctx.Exception == null)
            {
                if (ctx.LastEntity is VfsFile file)
                {
                    // Already exists
                    if (option.Mode == FileMode.CreateNew)
                    {
                        throw new VfsException(option.Path, $"The file already exists.");
                    }

                    if (noOpen == false)
                    {
                        return await file.OpenAsync(option, ctx.NormalizedPath, cancel);
                    }
                    else
                    {
                        return null!;
                    }
                }
                else
                {
                    // There is existing another type object
                    throw new VfsException(option.Path, $"There are existing object at the speficied path.");
                }
            }

            if (ctx.Exception is VfsNotFoundException && ctx.RemainingPathElements.Count == 1 && ctx.LastEntity is VfsDirectory && option.Mode != FileMode.Open)
            {
                // Create new RAM file
                VfsDirectory lastDir = (VfsDirectory)ctx.LastEntity;

                string fileName = ctx.RemainingPathElements.Peek();

                var newFile = await createFileCallback(fileName, option, cancel);

                string fullPath = PathParser.Combine(ctx.NormalizedPath, fileName);

                try
                {
                    await lastDir.AddFileAsync(newFile);
                }
                catch
                {
                    await newFile.ReleaseLinkAsync(true);
                    throw;
                }

                if (noOpen == false)
                {
                    return await newFile.OpenAsync(option, fullPath, cancel);
                }
                else
                {
                    return null!;
                }
            }
            else
            {
                throw ctx.Exception;
            }
        }
    }

    protected override async Task DeleteDirectoryImplAsync(string directoryPath, bool recursive, CancellationToken cancel = default)
    {
        if (recursive)
        {
            await this.DeleteDirectoryRecursiveInternalAsync(directoryPath, cancel);
            return;
        }

        using (VfsPathParserContext ctx = await ParsePathInternalAsync(directoryPath, cancel))
        {
            if (ctx.Exception != null)
                throw ctx.Exception;

            if (ctx.LastEntity is VfsDirectory dir)
            {
                if (dir.IsRoot)
                    throw new VfsException(directoryPath, "The root directory cannot be removed.");

                Debug.Assert(ctx.EntityStack.Count >= 2);
                Debug.Assert(ctx.EntityStack.Last() == dir);
                VfsDirectory parentDir = (ctx.EntityStack[ctx.EntityStack.Count - 2] as VfsDirectory)!;
                Debug.Assert(parentDir != null);

                dir.ReleaseHandleRef();
                ctx.EntityStack.RemoveAt(ctx.EntityStack.Count - 1);

                await parentDir.RemoveDirectoryAsync(dir, cancel);
            }
            else
            {
                throw new VfsNotFoundException(directoryPath, "Directory not found.");
            }
        }
    }

    protected override async Task DeleteFileImplAsync(string path, FileFlags flags = FileFlags.None, CancellationToken cancel = default)
    {
        using (VfsPathParserContext ctx = await ParsePathInternalAsync(path, cancel))
        {
            if (ctx.Exception != null)
                throw ctx.Exception;

            if (ctx.LastEntity is VfsFile file)
            {
                Debug.Assert(ctx.EntityStack.Count >= 2);
                Debug.Assert(ctx.EntityStack.Last() == file);
                VfsDirectory parentDir = (ctx.EntityStack[ctx.EntityStack.Count - 2] as VfsDirectory)!;
                Debug.Assert(parentDir != null);

                file.ReleaseHandleRef();
                ctx.EntityStack.RemoveAt(ctx.EntityStack.Count - 1);

                await parentDir.RemoveFileAsync(file, cancel);
            }
            else
            {
                throw new VfsNotFoundException(path, "File not found.");
            }
        }
    }

    protected override async Task<FileSystemEntity[]> EnumDirectoryImplAsync(string directoryPath, EnumDirectoryFlags flags, string wildcard, CancellationToken cancel = default)
    {
        using (VfsPathParserContext ctx = await ParsePathInternalAsync(directoryPath, cancel))
        {
            if (ctx.Exception != null)
                throw ctx.Exception;

            if (ctx.LastEntity is VfsDirectory thisDirObject)
            {
                var entities = await thisDirObject.EnumEntitiesAsync(flags, cancel);
                List<FileSystemEntity> ret = new List<FileSystemEntity>();

                FileSystemEntity thisDir = new FileSystemEntity(
                    fullPath: ctx.NormalizedPath,
                    name: ".",
                    attributes: thisDirObject.Attributes,
                    creationTime: thisDirObject.CreationTime,
                    lastWriteTime: thisDirObject.LastWriteTime,
                    lastAccessTime: thisDirObject.LastAccessTime
                    );

                ret.Add(thisDir);

                foreach (var entity in entities)
                {
                    if (entity is VfsDirectory dirObject)
                    {
                        FileMetadata meta = await dirObject.GetMetadataAsync(cancel);

                        FileSystemEntity dir = new FileSystemEntity(
                            fullPath: PathParser.Combine(ctx.NormalizedPath, entity.Name),
                            name: entity.Name,
                            attributes: meta.Attributes ?? FileAttributes.Directory,
                            creationTime: meta.CreationTime ?? Util.ZeroDateTimeOffsetValue,
                            lastWriteTime: meta.LastWriteTime ?? Util.ZeroDateTimeOffsetValue,
                            lastAccessTime: meta.LastAccessTime ?? Util.ZeroDateTimeOffsetValue
                            );

                        if (this.PathParser.WildcardMatch(dir.Name, wildcard))
                        {
                            ret.Add(dir);
                        }
                    }
                    else if (entity is VfsFile fileObject)
                    {
                        FileMetadata meta = await fileObject.GetMetadataAsync(cancel);
                        FileSystemEntity file = new FileSystemEntity(
                            fullPath: PathParser.Combine(ctx.NormalizedPath, entity.Name),
                            name: entity.Name,
                            size: meta.Size,
                            physicalSize: meta.PhysicalSize,
                            attributes: meta.Attributes ?? FileAttributes.Directory,
                            creationTime: meta.CreationTime ?? Util.ZeroDateTimeOffsetValue,
                            lastWriteTime: meta.LastWriteTime ?? Util.ZeroDateTimeOffsetValue,
                            lastAccessTime: meta.LastAccessTime ?? Util.ZeroDateTimeOffsetValue
                        );

                        if (this.PathParser.WildcardMatch(file.Name, wildcard))
                        {
                            ret.Add(file);
                        }
                    }
                }

                return ret.ToArray();
            }
            else
            {
                throw new VfsNotFoundException(directoryPath, "Directory not found.");
            }
        }
    }

    protected override async Task<FileMetadata> GetDirectoryMetadataImplAsync(string path, FileMetadataGetFlags flags = FileMetadataGetFlags.DefaultAll, CancellationToken cancel = default)
    {
        using (VfsPathParserContext ctx = await ParsePathInternalAsync(path, cancel))
        {
            if (ctx.Exception != null)
                throw ctx.Exception;

            if (ctx.LastEntity is VfsDirectory dir)
            {
                return await dir.GetMetadataAsync(cancel);
            }
            else
            {
                throw new VfsNotFoundException(path, "Directory not found.");
            }
        }
    }

    protected override async Task<FileMetadata> GetFileMetadataImplAsync(string path, FileMetadataGetFlags flags = FileMetadataGetFlags.DefaultAll, CancellationToken cancel = default)
    {
        using (VfsPathParserContext ctx = await ParsePathInternalAsync(path, cancel))
        {
            if (ctx.Exception != null)
                throw ctx.Exception;

            if (ctx.LastEntity is VfsFile file)
            {
                return await file.GetMetadataAsync(cancel);
            }
            else
            {
                throw new VfsNotFoundException(path, "File not found.");
            }
        }
    }

    protected override async Task<bool> IsDirectoryExistsImplAsync(string path, CancellationToken cancel = default)
    {
        using (VfsPathParserContext ctx = await ParsePathInternalAsync(path, cancel))
        {
            if (ctx.Exception == null)
                if (ctx.LastEntity is VfsDirectory)
                    return true;
        }

        return false;
    }

    protected override async Task<bool> IsFileExistsImplAsync(string path, CancellationToken cancel = default)
    {
        using (VfsPathParserContext ctx = await ParsePathInternalAsync(path, cancel))
        {
            if (ctx.Exception == null)
                if (ctx.LastEntity is VfsFile)
                    return true;
        }

        return false;
    }

    protected override Task MoveDirectoryImplAsync(string srcPath, string destPath, CancellationToken cancel = default)
    {
        throw new NotImplementedException();
    }

    protected override Task MoveFileImplAsync(string srcPath, string destPath, CancellationToken cancel = default)
    {
        throw new NotImplementedException();
    }

    protected override async Task<string> NormalizePathImplAsync(string path, CancellationToken cancel = default)
    {
        using (VfsPathParserContext ctx = await ParsePathInternalAsync(path, cancel))
        {
            string ret = this.PathParser.BuildAbsolutePathStringFromElements(ctx.NormalizedPathStack.Concat(ctx.RemainingPathElements));

            return ret;
        }
    }

    protected override async Task SetDirectoryMetadataImplAsync(string path, FileMetadata metadata, CancellationToken cancel = default)
    {
        using (VfsPathParserContext ctx = await ParsePathInternalAsync(path, cancel))
        {
            if (ctx.Exception != null)
                throw ctx.Exception;

            if (ctx.LastEntity is VfsDirectory dir)
            {
                await dir.SetMetadataAsync(metadata, cancel);
            }
            else
            {
                throw new VfsNotFoundException(path, "Directory not found.");
            }
        }
    }

    protected override async Task SetFileMetadataImplAsync(string path, FileMetadata metadata, CancellationToken cancel = default)
    {
        using (VfsPathParserContext ctx = await ParsePathInternalAsync(path, cancel))
        {
            if (ctx.Exception != null)
                throw ctx.Exception;

            if (ctx.LastEntity is VfsFile file)
            {
                await file.SetMetadataAsync(metadata, cancel);
            }
            else
            {
                throw new VfsNotFoundException(path, "Directory not found.");
            }
        }
    }

    protected override IFileProvider CreateFileProviderForWatchImpl(string root)
    {
        // ToDo: Implement a file watcher here
        return base.CreateDefaultNullFileProvider();
    }
}
