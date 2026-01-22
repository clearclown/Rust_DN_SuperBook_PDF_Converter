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
using System.Reflection;
using System.Collections.Immutable;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using System.Collections;
using System.Collections.Concurrent;

namespace IPA.Cores.Basic;

public class FsBasedFileProviderFileInfoImpl : IFileInfo
{
    public FileSystemBasedProvider Provider { get; }
    public string FullPath { get; }
    public FileSystem FileSystem => Provider.FileSystem;

    public bool Exists { get; }
    public bool IsDirectory { get; }
    public long Length { get; }
    public string? PhysicalPath { get; } = null!;
    public string Name { get; } = null!;
    public DateTimeOffset LastModified { get; }

    internal FsBasedFileProviderFileInfoImpl(EnsureInternal yes, FileSystemBasedProvider provider, string fullPath, bool exists, bool isDirectroy, long length, string? physicalPath, string? name, DateTimeOffset lastModified)
    {
        this.Provider = provider;
        this.FullPath = fullPath;

        this.Exists = exists;

        if (this.Exists)
        {
            this.IsDirectory = isDirectroy;

            if (this.IsDirectory == false)
            {
                this.Length = length;
            }

            this.PhysicalPath = physicalPath;
            this.Name = name._NullCheck();
            this.LastModified = lastModified;
        }
    }

    public Stream CreateReadStream()
    {
        if (this.Exists && this.IsDirectory == false)
        {
            FileObject obj = FileSystem.Open(this.FullPath);

            try
            {
                return obj.GetStream(true);
            }
            catch
            {
                obj._DisposeSafe();
                throw;
            }
        }
        else
        {
            throw new FileNotFoundException(this.FullPath);
        }
    }
}

public class FsBasedFileProviderDirectoryContentsImpl : IDirectoryContents
{
    public bool Exists { get; } = false;

    IEnumerable<IFileInfo> List;

    public FsBasedFileProviderDirectoryContentsImpl()
    {
        this.Exists = false;
        this.List = new List<IFileInfo>();
    }

    public FsBasedFileProviderDirectoryContentsImpl(IEnumerable<IFileInfo> list)
    {
        this.Exists = true;
        this.List = list;
    }

    public IEnumerator<IFileInfo> GetEnumerator()
    {
        return this.List.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

public class FileSystemBasedProvider : AsyncService, IFileProvider
{
    public FileSystem FileSystem { get; }
    public PathParser Parser => FileSystem.PathParser;
    public bool IgnoreCase { get; }

    DisposableFileProvider ProviderForWatch;

    internal FileSystemBasedProvider(EnsureInternal yes, FileSystem underlayFileSystem, string rootDirectory, bool ignoreCase = true)
    {
        try
        {
            this.IgnoreCase = ignoreCase;
            this.FileSystem = new ReadOnlyCacheFileSystem(new ReadOnlyCacheFileSystemParam(
                new ChrootFileSystem(new ChrootFileSystemParam(underlayFileSystem, rootDirectory, FileSystemMode.ReadOnly)),
                disposeUnderlay: true
                ));
            ProviderForWatch = this.FileSystem._CreateFileProviderForWatchInternal(EnsureInternal.Yes, "/");
        }
        catch
        {
            this._DisposeSafe();
            throw;
        }
    }

    string NormalizeSubPath(string subpath, NormalizePathOption options)
    {
        Debug.Assert(Parser.Style.EqualsAny(FileSystemStyle.Linux, FileSystemStyle.Mac));

        subpath = Parser.NormalizeDirectorySeparatorIncludeWindowsBackslash(subpath);

        if (Parser.IsAbsolutePath(subpath) == false)
        {
            subpath = "/" + subpath;
        }

        subpath = Parser.NormalizeUnixStylePathWithRemovingRelativeDirectoryElements(subpath);

        subpath = FileSystem.NormalizePath(subpath, options);

        return subpath;
    }

    public IDirectoryContents GetDirectoryContents(string subpath)
    {
        bool isRetry = false;

        L_RETRY:
        subpath = NormalizeSubPath(subpath, NormalizePathOption.NormalizeCaseDirectory);

        if (FileSystem.IsDirectoryExists(subpath) == false)
        {
            if (isRetry == false)
            {
                isRetry = true;
                FileSystem.FlushNormalizedCaseCorrectionCache();
                goto L_RETRY;
            }
            else
            {
                return new FsBasedFileProviderDirectoryContentsImpl();
            }
        }
        else
        {
            string? physicalDirPath = null;

            var fsIf = FileSystem as IRewriteVirtualPhysicalPath;
            if (fsIf != null)
            {
                physicalDirPath = fsIf.MapPathVirtualToPhysical(subpath);
            }

            FileSystemEntity[] enums = FileSystem.EnumDirectory(subpath, flags: EnumDirectoryFlags.NoGetPhysicalSize);

            List<FsBasedFileProviderFileInfoImpl> o = new List<FsBasedFileProviderFileInfoImpl>();

            foreach (FileSystemEntity e in enums)
            {
                if (e.IsCurrentOrParentDirectory == false)
                {
                    FsBasedFileProviderFileInfoImpl d = new FsBasedFileProviderFileInfoImpl(EnsureInternal.Yes, this, e.FullPath, true, e.IsDirectory, e.Size,
                        (physicalDirPath != null && FileSystem is IOverlayFileSystem) ? ((IOverlayFileSystem)FileSystem).UnderlayFileSystem.PathParser.Combine(physicalDirPath, e.Name) : null, e.Name, e.LastWriteTime);

                    o.Add(d);
                }
            }

            return new FsBasedFileProviderDirectoryContentsImpl(o);
        }
    }

    public IFileInfo GetFileInfo(string subpath)
    {
        bool isRetry = false;

        L_RETRY:

        subpath = NormalizeSubPath(subpath, NormalizePathOption.NormalizeCaseFileName);

        bool exists = false;
        bool isDirectroy = default;
        long length = default;
        string? physicalPath = null;
        string? name = null;
        DateTimeOffset lastModified = default;

        if (FileSystem.IsFileExists(subpath))
        {
            exists = true;

            FileMetadata meta = FileSystem.GetFileMetadata(subpath, FileMetadataGetFlags.NoAlternateStream | FileMetadataGetFlags.NoAttributes | FileMetadataGetFlags.NoPhysicalFileSize | FileMetadataGetFlags.NoAuthor | FileMetadataGetFlags.NoSecurity);
            length = meta.Size;
            lastModified = meta.LastWriteTime ?? default;
        }
        else if (FileSystem.IsDirectoryExists(subpath))
        {
            exists = true;
            isDirectroy = true;

            FileMetadata meta = FileSystem.GetDirectoryMetadata(subpath, FileMetadataGetFlags.NoAlternateStream | FileMetadataGetFlags.NoAttributes | FileMetadataGetFlags.NoPhysicalFileSize | FileMetadataGetFlags.NoAuthor | FileMetadataGetFlags.NoSecurity);
            lastModified = meta.LastWriteTime ?? default;
        }
        else
        {
            if (isRetry == false)
            {
                isRetry = true;
                FileSystem.FlushNormalizedCaseCorrectionCache();
                goto L_RETRY;
            }
        }

        if (exists)
        {
            var fsIf = FileSystem as IRewriteVirtualPhysicalPath;
            if (fsIf != null)
            {
                physicalPath = fsIf.MapPathVirtualToPhysical(subpath);
            }
            name = Parser.GetFileName(subpath);
        }

        return new FsBasedFileProviderFileInfoImpl(EnsureInternal.Yes, this, subpath, exists, isDirectroy, length, physicalPath, name, lastModified);
    }

    public IChangeToken Watch(string filter)
    {
        return this.ProviderForWatch.Watch(filter);
    }

    protected override async Task CleanupImplAsync(Exception? ex)
    {
        try
        {
            await this.ProviderForWatch._DisposeSafeAsync();
            await this.FileSystem._DisposeSafeAsync();
        }
        finally
        {
            await base.CleanupImplAsync(ex);
        }
    }
}

