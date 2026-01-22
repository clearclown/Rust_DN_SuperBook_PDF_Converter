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

#if CORES_BASIC_GIT

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using LibGit2Sharp;
using Microsoft.Extensions.FileProviders;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

namespace IPA.Cores.Basic;

public class GitFileObject : RandomAccessFileObject
{
    public new GitFileSystem FileSystem => (GitFileSystem)base.FileSystem;
    public GitRepository Repository => FileSystem.Repository;
    public GitCommit Commit => FileSystem.Commit;
    public DateTimeOffset TimeStamp => FileSystem.TimeStamp;

    protected GitFileObject(GitFileSystem fileSystem, FileParameters fileParams, IRandomAccess<byte> baseRandomAccess) : base(fileSystem, fileParams, baseRandomAccess) { }

    public static async Task<FileObject> CreateFileAsync(GitFileSystem fileSystem, FileParameters fileParams, CancellationToken cancel = default)
    {
        if (fileParams.Access.Bit(FileAccess.Write)) throw new FileException(fileParams.Path, "The git filesystem is read-only.");
        if (fileParams.Mode != FileMode.Open) throw new FileException(fileParams.Path, "The git filesystem is read-only.");

        cancel.ThrowIfCancellationRequested();

        Blob blob = fileSystem.Commit.GetFile(fileParams.Path);

        Stream stream = blob.GetContentStream();
        IRandomAccess<byte> randomAccess = new SeekableStreamBasedRandomAccess(stream, true);
        try
        {
            GitFileObject f = new GitFileObject(fileSystem, fileParams, randomAccess);

            await Task.CompletedTask;

            return f;
        }
        catch
        {
            randomAccess._DisposeSafe();
            stream._DisposeSafe();
            throw;
        }
    }
}

public class GitFileSystemParams : FileSystemParams
{
    public GitRepository Repository { get; }
    public string CommitId { get; }

    public GitFileSystemParams(GitRepository repository, string? commitId = null)
        : base(PathParser.GetInstance(FileSystemStyle.Linux), FileSystemMode.ReadOnly)
    {
        this.Repository = repository;
        if (commitId._IsEmpty()) commitId = repository.OriginMasterBranchCommitId;

        this.CommitId = commitId;
    }
}

public class GitFileSystem : FileSystem
{
    protected new GitFileSystemParams Params => (GitFileSystemParams)base.Params;
    public GitRepository Repository => Params.Repository;
    public GitCommit Commit { get; }
    public DateTimeOffset TimeStamp => Commit.TimeStamp;

    static readonly PathParser Parser = PathParser.GetInstance(FileSystemStyle.Linux);

    public GitFileSystem(GitFileSystemParams param) : base(param)
    {
        try
        {
            this.Commit = this.Repository.FindCommitAsync(param.CommitId)._GetResult();
        }
        catch (Exception ex)
        {
            this._DisposeSafe(ex);
            throw;
        }
    }

    protected override Task<FileSystemEntity[]> EnumDirectoryImplAsync(string directoryPath, EnumDirectoryFlags flags, CancellationToken cancel = default)
    {
        return Task.FromResult(this.Commit.GetDirectoryItems(directoryPath));
    }

    protected override Task<FileMetadata> GetDirectoryMetadataImplAsync(string path, FileMetadataGetFlags flags = FileMetadataGetFlags.DefaultAll, CancellationToken cancel = default)
    {
        return Task.FromResult(this.Commit.GetDirectoryMetadata(path));
    }

    protected override Task<FileMetadata> GetFileMetadataImplAsync(string path, FileMetadataGetFlags flags = FileMetadataGetFlags.DefaultAll, CancellationToken cancel = default)
    {
        return Task.FromResult(this.Commit.GetFileMetadata(path));
    }

    protected override Task<bool> IsDirectoryExistsImplAsync(string path, CancellationToken cancel = default)
    {
        try
        {
            this.Commit.GetDirectoryMetadata(path);
            return Task.FromResult(true);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    protected override Task<bool> IsFileExistsImplAsync(string path, CancellationToken cancel = default)
    {
        try
        {
            this.Commit.GetFileMetadata(path);
            return Task.FromResult(true);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    protected override Task<string> NormalizePathImplAsync(string path, CancellationToken cancel = default)
    {
        return Task.FromResult(Parser.NormalizeUnixStylePathWithRemovingRelativeDirectoryElements(path));
    }

    protected override Task<FileObject> CreateFileImplAsync(FileParameters option, CancellationToken cancel = default)
        => GitFileObject.CreateFileAsync(this, option, cancel);

    protected override Task CreateDirectoryImplAsync(string directoryPath, FileFlags flags = FileFlags.None, CancellationToken cancel = default) => throw new NotImplementedException();
    protected override Task DeleteDirectoryImplAsync(string directoryPath, bool recursive, CancellationToken cancel = default) => throw new NotImplementedException();
    protected override Task DeleteFileImplAsync(string path, FileFlags flags = FileFlags.None, CancellationToken cancel = default) => throw new NotImplementedException();
    protected override Task MoveDirectoryImplAsync(string srcPath, string destPath, CancellationToken cancel = default) => throw new NotImplementedException();
    protected override Task MoveFileImplAsync(string srcPath, string destPath, CancellationToken cancel = default) => throw new NotImplementedException();
    protected override Task SetDirectoryMetadataImplAsync(string path, FileMetadata metadata, CancellationToken cancel = default) => throw new NotImplementedException();
    protected override Task SetFileMetadataImplAsync(string path, FileMetadata metadata, CancellationToken cancel = default) => throw new NotImplementedException();
    protected override IFileProvider CreateFileProviderForWatchImpl(string root) => base.CreateDefaultNullFileProvider();
}

#endif // CORES_BASIC_GIT

