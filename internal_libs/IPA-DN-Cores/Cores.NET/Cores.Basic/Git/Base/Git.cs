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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using LibGit2Sharp;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace IPA.Cores.Basic;

public static partial class GitUtil
{
    public static void Clone(string destDir, string srcUrl, CloneOptions? options = null)
    {
        if (options == null) options = new CloneOptions()
        {
            IsBare = true,
            RecurseSubmodules = false,
            FetchOptions = new FetchOptions()
            {
                Prune = true,
                TagFetchMode = TagFetchMode.All,
            },
        };

        Repository.Clone(srcUrl, destDir, options);
    }

    public static bool IsCommitId(string str)
    {
        try
        {
            if (str.Length == 40)
            {
                if (str._GetHexBytes().Length == 20)
                {
                    return true;
                }
            }
        }
        catch { }
        return false;
    }
}

public class GitCommit
{
    public GitRepository Repository { get; }
    public Commit CommitObj { get; }
    public string CommitId { get; }
    public DateTimeOffset TimeStamp => CommitObj.Author.When;
    public Tree RootTree => CommitObj.Tree;
    public string Message => CommitObj.Message;
    public string MessageOneLine => CommitObj.Message._OneLine(" ");

    readonly Singleton<string, Tree> DirectoryTreeCache;
    readonly Singleton<string, Blob> FileTreeCache;
    readonly Singleton<string, FileSystemEntity[]> DirectoryItemsCache;
    readonly Singleton<string, FileMetadata> DirectoryMetadataCache;
    readonly Singleton<string, FileMetadata> FileMetadataCache;

    public GitCommit(GitRepository repository, Commit commit)
    {
        this.Repository = repository;
        this.CommitObj = commit;
        this.CommitId = this.CommitObj.Id.Sha;

        this.DirectoryTreeCache = new Singleton<string, Tree>(path => GetDirectoryInternal(path), GitRepository.PathParser.PathStringComparer);
        this.DirectoryItemsCache = new Singleton<string, FileSystemEntity[]>(path => GetDirectoryItemsInternal(path), GitRepository.PathParser.PathStringComparer);

        this.FileTreeCache = new Singleton<string, Blob>(path => GetFileInternal(path), GitRepository.PathParser.PathStringComparer);

        this.DirectoryMetadataCache = new Singleton<string, FileMetadata>(path => GetDirectoryMetadataInternal(path), GitRepository.PathParser.PathStringComparer);
        this.FileMetadataCache = new Singleton<string, FileMetadata>(path => GetFileMetadataInternal(path), GitRepository.PathParser.PathStringComparer);
    }

    public List<GitCommit> GetCommitLogs()
    {
        HashSet<string> idSet = new HashSet<string>();
        List<Commit> list = new List<Commit>();
        Queue<Commit> queue = new Queue<Commit>();

        Enqueue(this.CommitObj);

        while (true)
        {
            if (queue.TryDequeue(out Commit? current) == false)
            {
                break;
            }

            current._MarkNotNull();

            foreach (Commit next in current.Parents)
            {
                Enqueue(next);
            }
        }

        List<GitCommit> ret = new List<GitCommit>();

        foreach (Commit c in list)
        {
            ret.Add(new GitCommit(this.Repository, c));
        }

        ret.Sort((x, y) => (x.TimeStamp.CompareTo(y.TimeStamp)));

        return ret;

        void Enqueue(Commit c)
        {
            if (idSet.Add(c.Id.Sha))
            {
                queue.Enqueue(c);
                list.Add(c);
            }
        }
    }

    public FileSystemEntity[] GetDirectoryItems(string dirPath)
        => this.DirectoryItemsCache[dirPath];

    FileSystemEntity[] GetDirectoryItemsInternal(string dirPath)
    {
        Tree dir = GetDirectory(dirPath);

        return GetDirectoryItemsInternal(dirPath, dir);
    }

    FileSystemEntity[] GetDirectoryItemsInternal(string dirPath, Tree tree)
    {
        dirPath = GitRepository.NormalizePath(dirPath);

        List<FileSystemEntity> ret = new List<FileSystemEntity>();

        var curLastCommit = GetLastCommitForObject(dirPath);

        FileSystemEntity cur = new FileSystemEntity(
            fullPath: dirPath,
            name: ".",
            attributes: FileAttributes.Directory,
            creationTime: curLastCommit.Author.When,
            lastWriteTime: curLastCommit.Author.When,
            lastAccessTime: curLastCommit.Author.When
            );

        ret.Add(cur);

        foreach (var item in tree)
        {
            string fullPath = GitRepository.PathParser.Combine(dirPath, item.Name);

            var lastCommit = GetLastCommitForObject(fullPath);

            if (item.TargetType == TreeEntryTargetType.Tree)
            {
                Tree subTree = (Tree)item.Target;
                FileSystemEntity e = new FileSystemEntity(
                    fullPath: fullPath,
                    name: item.Name,
                    attributes: FileAttributes.Directory,
                    creationTime: lastCommit.Author.When,
                    lastWriteTime: lastCommit.Author.When,
                    lastAccessTime: lastCommit.Author.When
                );
                ret.Add(e);
            }
            else if (item.TargetType == TreeEntryTargetType.Blob)
            {
                Blob blob = (Blob)item.Target;
                FileSystemEntity e = new FileSystemEntity(
                    fullPath: fullPath,
                    name: item.Name,
                    attributes: FileAttributes.Normal,
                    size: blob.Size,
                    physicalSize: blob.Size,
                    creationTime: lastCommit.Author.When,
                    lastWriteTime: lastCommit.Author.When,
                    lastAccessTime: lastCommit.Author.When
                );
                ret.Add(e);
            }
        }

        return ret.ToArray();
    }

    public FileMetadata GetDirectoryMetadata(string path) => this.DirectoryMetadataCache[path];

    FileMetadata GetDirectoryMetadataInternal(string path)
    {
        Tree tree = GetDirectory(path);

        Commit commit = GetLastCommitForObject(path);

        return new FileMetadata(true,
            attributes: FileAttributes.Directory,
            creationTime: commit.Author.When,
            lastWriteTime: commit.Author.When,
            lastAccessTime: commit.Author.When,
            author: new FileAuthorMetadata()
            {
                AuthorEmail = commit.Author.Email._NonNull(),
                AuthorName = commit.Author.Name._NonNull(),
                AuthorTimeStamp = commit.Author.When,
                CommitterEmail = commit.Committer.Email._NonNull(),
                CommitterName = commit.Committer.Name._NonNull(),
                CommitterTimeStamp = commit.Committer.When,
                Message = commit.Message._NonNull(),
                CommitId = commit.Id.Sha,
            },
            size: 0,
            physicalSize: 0);
    }

    public FileMetadata GetFileMetadata(string path) => this.FileMetadataCache[path];

    FileMetadata GetFileMetadataInternal(string path)
    {
        Blob blob = GetFile(path);

        Commit commit = GetLastCommitForObject(path);

        return new FileMetadata(true,
            attributes: FileAttributes.Directory,
            creationTime: commit.Author.When,
            lastWriteTime: commit.Author.When,
            lastAccessTime: commit.Author.When,
            author: new FileAuthorMetadata()
            {
                AuthorEmail = commit.Author.Email._NonNull(),
                AuthorName = commit.Author.Name._NonNull(),
                AuthorTimeStamp = commit.Author.When,
                CommitterEmail = commit.Committer.Email._NonNull(),
                CommitterName = commit.Committer.Name._NonNull(),
                CommitterTimeStamp = commit.Committer.When,
                Message = commit.Message._NonNull(),
                CommitId = commit.Id.Sha,
            },
            size: blob.Size,
            physicalSize: blob.Size);
    }

    public Tree GetDirectory(string path)
        => DirectoryTreeCache[path];

    Tree GetDirectoryInternal(string path)
    {
        string path2 = GitRepository.NormalizePathToGit(path);

        if (path2 == "") return this.RootTree;

        TreeEntry entry = this.RootTree[path2];

        if (entry == null)
            throw new ArgumentException($"The path \"{path}\" not found.");

        if (entry.TargetType != TreeEntryTargetType.Tree)
        {
            throw new ArgumentException($"The path \"{path}\" is not a directory.");
        }

        return (Tree)entry.Target;
    }

    public Blob GetFile(string path)
        => FileTreeCache[path];

    Blob GetFileInternal(string path)
    {
        string path2 = GitRepository.NormalizePathToGit(path);

        TreeEntry entry = this.RootTree[path2];

        if (entry == null)
            throw new ArgumentException($"The path \"{path}\" not found.");

        if (entry.TargetType != TreeEntryTargetType.Blob)
        {
            throw new ArgumentException($"The path \"{path}\" is not a file.");
        }

        return (Blob)entry.Target;
    }

    public Commit GetLastCommitForObject(string path)
    {
        path = GitRepository.NormalizePathToGit(path);

        // The below code was written by refering to: https://github.com/libgit2/libgit2sharp/issues/89#issuecomment-38380873
        Commit commit = this.CommitObj;
        GitObject gitObj;

        if (path != "")
        {
            gitObj = commit[path].Target;
        }
        else
        {
            gitObj = commit.Tree;
        }

        HashSet<string> commitUniqueHashSet = new HashSet<string>();
        Queue<Commit> queue = new Queue<Commit>();

        queue.Enqueue(commit);
        commitUniqueHashSet.Add(commit.Sha);

        while (queue.Count > 0)
        {
            commit = queue.Dequeue();
            bool flag = false;

            foreach (Commit parent in commit.Parents)
            {
                var tree = parent[path];
                if (tree == null)
                    continue;
                bool isSameHash = tree.Target.Sha._IsSamei(gitObj.Sha);
                if (isSameHash && commitUniqueHashSet.Add(parent.Sha))
                    queue.Enqueue(parent);
                flag = flag || isSameHash;
            }

            if (flag == false)
            {
                break;
            }
        }

        return commit;
    }
}

[Flags]
public enum GitRefType
{
    LocalBranch,
    RemoteBranch,
    Tag,
}

public class GitRef
{
    public string Name { get; }
    public string CommitId { get; }
    public DateTimeOffset TimeStamp { get; }
    public GitRefType Type { get; }

    readonly GitRepository Repository;
    Singleton<GitCommit> CommitSingleton;

    public GitCommit Commit => CommitSingleton;

    public GitRef(GitRepository repository, string name, string commitId, DateTimeOffset timeStamp, GitRefType type)
    {
        this.Repository = repository;
        this.Name = name;
        this.CommitId = commitId;
        this.TimeStamp = timeStamp;
        this.Type = type;

        this.CommitSingleton = new Singleton<GitCommit>(() => this.Repository.FindCommit(this.CommitId));
    }
}

public class GitRepository : AsyncService
{
    public string WorkDir { get; }

    readonly Repository Repository;

    public static readonly PathParser PathParser = PathParser.GetInstance(FileSystemStyle.Linux);

    public string OriginMasterBranchCommitId => this.EnumRef().GetOriginMasterBranch().CommitId;

    public GitRef GetOriginRef(string name) => this.EnumRef().GetOriginRef(name);

    public GitRef GetOriginMasterBranch() => this.EnumRef().GetOriginMasterBranch();

    readonly Singleton<GitRef[]> RefCache;

    public GitRepository(string workDir, CancellationToken cancel = default) : base(cancel)
    {
        try
        {
            this.WorkDir = Env.LocalPathParser.NormalizeDirectorySeparatorAndCheckIfAbsolutePath(workDir);

            this.Repository = new Repository(this.WorkDir);

            this.RefCache = new Singleton<GitRef[]>(() =>
            {
                List<GitRef> ret = new List<GitRef>();

                foreach (var e in this.Repository.Refs)
                {
                    Commit commit = this.Repository.Lookup<Commit>(e.TargetIdentifier);
                    if (commit != null)
                    {
                        GitRefType type = GitRefType.LocalBranch;
                        if (e.IsRemoteTrackingBranch) type = GitRefType.RemoteBranch;
                        if (e.IsTag) type = GitRefType.Tag;
                        ret.Add(new GitRef(this, e.CanonicalName, e.TargetIdentifier, commit.Author.When, type));
                    }
                }

                return ret.ToArray();
            });
        }
        catch (Exception ex)
        {
            this._DisposeSafe(ex);
            throw;
        }
    }

    public Task<GitCommit> FindCommitAsync(string commitId, CancellationToken cancel = default)
    {
        return this.RunCriticalProcessAsync(true, cancel, c =>
        {
            ObjectId objId = new ObjectId(commitId);

            Commit commit = this.Repository.Lookup<Commit>(objId);

            if (commit == null)
                throw new ArgumentException($"Commit \"{objId.Sha}\" not found.");

            GitCommit ret = new GitCommit(this, commit);

            return Task.FromResult(ret);
        });
    }

    public GitCommit FindCommit(string commitId, CancellationToken cancel = default)
        => FindCommitAsync(commitId, cancel)._GetResult();

    public Task FetchAsync(string url, CancellationToken cancel = default)
    {
        return this.RunCriticalProcessAsync(true, cancel, c =>
        {
            this.Repository.Network.Fetch("origin", new string[0]);

            this.RefCache.Clear();

            return Task.CompletedTask;
        });
    }

    public void Fetch(string url, CancellationToken cancel = default)
        => FetchAsync(url, cancel)._GetResult();

    public Task<GitRef[]> EnumRefAsync(CancellationToken cancel = default)
    {
        return this.RunCriticalProcessAsync(true, cancel, c =>
        {
            return Task.FromResult<GitRef[]>(this.RefCache);
        });
    }

    public GitRef[] EnumRef(CancellationToken cancel = default)
        => EnumRefAsync(cancel)._GetResult();

    protected override void DisposeImpl(Exception? ex)
    {
        try
        {
            this.Repository._DisposeSafe();
        }
        finally
        {
            base.DisposeImpl(ex);
        }
    }

    public static string NormalizePath(string path)
    {
        return PathParser.NormalizeUnixStylePathWithRemovingRelativeDirectoryElements(path);
    }

    public static string NormalizePathToGit(string path)
    {
        path = NormalizePath(path);
        if (path.StartsWith("/")) path = path.Substring(1);
        return path;
    }
}

#endif // CORES_BASIC_GIT

