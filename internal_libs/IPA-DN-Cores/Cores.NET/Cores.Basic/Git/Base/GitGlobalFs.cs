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

#pragma warning disable CA2235 // Mark all non-serializable fields

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Diagnostics.CodeAnalysis;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

namespace IPA.Cores.Basic;

public static partial class CoresConfig
{
    public static partial class GitGlobalFsSettings
    {
        public static readonly Copenhagen<int> AutoFetchInterval = 1 * 1000;
        public static readonly Copenhagen<Func<string>> GetLocalCacheRootDirProc = new Func<string>(() => Env.LocalPathParser.Combine(Env.AppRootDir, "Local", "Git"));
    }
}

[Serializable]
[DataContract]
public class GitGlobalFsRepository
{
    [DataMember]
    public string? Url;

    [DataMember]
    public string? LocalWorkDir;

    [NonSerialized]
    public GitRepository? Repository;
}


[Serializable]
[DataContract]
public class GitGlobalFsState
{
    [DataMember]
    public List<GitGlobalFsRepository> RepositoryList = new List<GitGlobalFsRepository>();
}

public static class GitGlobalFs
{
    public static StaticModule Module = new StaticModule(InitModule, FreeModule);

    public static readonly string LocalCacheRootDir = CoresConfig.GitGlobalFsSettings.GetLocalCacheRootDirProc.Value();
    public static readonly string HiveDir = Env.LocalPathParser.Combine(LocalCacheRootDir, "State");
    public static readonly string RepoDir = Env.LocalPathParser.Combine(LocalCacheRootDir, "Repo");

    static readonly PathParser LinuxParser = PathParser.GetInstance(FileSystemStyle.Linux);

    public const string HiveDataName = "RepositoryList";

    static Singleton<Hive> HiveSingleton = null!;
    static Hive Hive => HiveSingleton;

    static Singleton<HiveData<GitGlobalFsState>> DataSingleton = null!;
    static HiveData<GitGlobalFsState> Data => DataSingleton;

    static Singleton<string, GitFileSystem> FileSystemSingleton = null!;

    static readonly CriticalSection RepositoryUpdateLock = new CriticalSection();

    static Task? UpdateMainLoopTask = null;
    static CancellationTokenSource? UpdateMainLoopTaskCancel = null;

    static void InitModule()
    {
        HiveSingleton = new Singleton<Hive>(() =>
        {
            Util.PutGitIgnoreFileOnDirectory(LocalCacheRootDir, FileFlags.AutoCreateDirectory);
            return new Hive(new HiveOptions(HiveDir, singleInstance: true));
        });
        DataSingleton = new Singleton<HiveData<GitGlobalFsState>>(() => new HiveData<GitGlobalFsState>(GitGlobalFs.Hive, HiveDataName, () => new GitGlobalFsState(), HiveSyncPolicy.None));

        FileSystemSingleton = new Singleton<string, GitFileSystem>(key =>
        {
            Str.GetKeyAndValue(key, out string repoUrl, out string commitId, "@");
            GitRepository repository = GetRepository(repoUrl);
            return new GitFileSystem(new GitFileSystemParams(repository, commitId));
        },
        StrComparer.IgnoreCaseComparer);
    }

    static void FreeModule()
    {
        StopUpdateLoop();

        FileSystemSingleton._DisposeSafe();
        FileSystemSingleton = null!;

        if (DataSingleton.IsCreated)
        {
            lock (Data.DataLock)
            {
                foreach (var repo in Data.ManagedData.RepositoryList)
                {
                    repo.Repository._DisposeSafe();
                }
            }
        }

        DataSingleton._DisposeSafe();
        DataSingleton = null!;

        HiveSingleton._DisposeSafe();
        HiveSingleton = null!;
    }

    static void StartUpdateLoop()
    {
        UpdateMainLoopTaskCancel = new CancellationTokenSource();

        UpdateMainLoopTask = TaskUtil.StartAsyncTaskAsync(UpdateRepositoryMainLoopAsync);
    }

    static void StopUpdateLoop()
    {
        UpdateMainLoopTaskCancel._TryCancel();

        UpdateMainLoopTask._TryGetResult(true);

        UpdateMainLoopTaskCancel = null;
        UpdateMainLoopTask = null;
    }

    static async Task UpdateRepositoryMainLoopAsync()
    {
        while (UpdateMainLoopTaskCancel!.IsCancellationRequested == false)
        {
            UpdateAllRepository(UpdateMainLoopTaskCancel.Token);

            await UpdateMainLoopTaskCancel._WaitUntilCanceledAsync(CoresConfig.GitGlobalFsSettings.AutoFetchInterval);
        }
    }

    static string GenerateNewRepositoryDirName(string repoUrl)
    {
        repoUrl = repoUrl.Trim().ToLowerInvariant();

        string repoName = LinuxParser.GetFileNameWithoutExtension(LinuxParser.RemoveLastSeparatorChar(repoUrl));

        DateTime now = DateTime.Now;
        for (int i = 1; ; i++)
        {
            string name = $"{Lfs.PathParser.MakeSafeFileName(repoName)}_{now.Year:D4}{now.Month:D2}{now.Day:D2}_{Env.ProcessId}_{i:D5}";
            string fullPath = Lfs.PathParser.Combine(RepoDir, name);

            if (Lfs.IsDirectoryExists(fullPath) == false)
            {
                return name;
            }
        }
    }

    public static GitRepository GetRepository(string repoUrl)
    {
        Module.CheckInitalized();

        lock (Data.DataLock)
        {
            GitGlobalFsRepository? repoData = Data.ManagedData.RepositoryList.Where(x => x.Url._IsSamei(repoUrl)).SingleOrDefault();

            if (repoData == null || repoData.Repository == null)
                throw new ApplicationException($"The repository \"{repoUrl}\" is not found by the registered list with StartRepository().");

            return repoData.Repository;
        }
    }

    public static GitFileSystem GetFileSystem(string repoUrl, string commitIdOrRefName)
    {
        Module.CheckInitalized();

        if (commitIdOrRefName._IsEmpty()) commitIdOrRefName = "refs/remotes/origin/master";

        string commitId;
        if (GitUtil.IsCommitId(commitIdOrRefName))
        {
            commitId = commitIdOrRefName;
        }
        else
        {
            GitRepository repository = GetRepository(repoUrl);
            GitRef? reference = repository.EnumRef().Where(x => x.Name._IsSamei(commitIdOrRefName)).SingleOrDefault();
            if (reference == null)
            {
                throw new ArgumentException($"The reference name \"{commitIdOrRefName}\" not found.");
            }
            commitId = reference.CommitId;
        }

        return FileSystemSingleton[$"{repoUrl}@{commitId}"];
    }

    public static void StartRepository(string repoUrl)
    {
        Module.CheckInitalized();

        lock (RepositoryUpdateLock)
        {
            if (repoUrl.IndexOf("@") != -1) throw new ArgumentException($"The repository name \'{repoUrl}\' must not contain '@'.");

            if (Data.IsReadOnly) throw new ApplicationException("Data.IsReadOnly");

            lock (Data.DataLock)
            {
                GitGlobalFsRepository? repoData = Data.ManagedData.RepositoryList.Where(x => x.Url._IsSamei(repoUrl)).SingleOrDefault();

                L_RETRY:

                if (repoData == null)
                {
                    string dirName = GenerateNewRepositoryDirName(repoUrl);
                    string dirFullPath = Lfs.PathParser.Combine(RepoDir, dirName);

                    Con.WriteDebug($"Clone the repository \"{repoUrl}\" to \"{dirFullPath}\" ...");
                    try
                    {
                        GitUtil.Clone(dirFullPath, repoUrl);
                    }
                    catch (Exception ex)
                    {
                        Con.WriteError($"GitUtil.Clone error: {ex.ToString()}");
                        throw;
                    }
                    Con.WriteDebug("Done.");
                    GitRepository gitRepo = new GitRepository(dirFullPath);
                    repoData = new GitGlobalFsRepository()
                    {
                        Url = repoUrl,
                        LocalWorkDir = dirName,
                        Repository = gitRepo,
                    };

                    Data.ManagedData.RepositoryList.Add(repoData);
                }
                else
                {
                    repoData.Url = repoUrl;

                    if (repoData.Repository == null)
                    {
                        string dirName = repoData.LocalWorkDir._NullCheck();
                        string dirFullPath = Lfs.PathParser.Combine(RepoDir, dirName);

                        try
                        {
                            GitRepository gitRepo = new GitRepository(dirFullPath);
                            repoData.Repository = gitRepo;
                        }
                        catch (Exception ex)
                        {
                            Con.WriteDebug($"Repository local dir \"{dirFullPath}\" load error: {ex.ToString()}");
                            Con.WriteDebug($"Trying to clone as a new local dir.");

                            Data.ManagedData.RepositoryList.Remove(repoData);
                            Data.SyncWithStorage(HiveSyncFlags.SaveToFile, false);

                            repoData = null;
                            goto L_RETRY;
                        }
                    }
                }
            }

            Data.SyncWithStorage(HiveSyncFlags.SaveToFile, false);

            StartUpdateLoop();
        }
    }

    public static void UpdateAllRepository(CancellationToken cancel = default)
    {
        Module.CheckInitalized();

        List<string?> urlList;
        lock (Data.DataLock)
        {
            urlList = Data.ManagedData.RepositoryList.Select(x => x.Url).ToList();
        }

        foreach (string? url in urlList)
        {
            Module.CheckInitalized();

            cancel.ThrowIfCancellationRequested();

            try
            {
                if (url != null) UpdateRepository(url);
            }
            catch { }
        }
    }

    public static void UpdateRepository(string repoUrl)
    {
        Module.CheckInitalized();

        lock (RepositoryUpdateLock)
        {
            GitGlobalFsRepository? repoData;
            lock (Data.DataLock)
            {
                repoData = Data.ManagedData.RepositoryList.Where(x => x.Url._IsSamei(repoUrl)).SingleOrDefault();
            }
            if (repoData == null || repoData.Repository == null)
                throw new ApplicationException($"The repository \"{repoUrl}\" is not found by the registered list with StartRepository().");

            try
            {
                repoData.Repository.Fetch(repoData.Url!);
            }
            catch (Exception ex)
            {
                Con.WriteError($"repoData.Repository.Fetch error: {ex.ToString()}");
                throw;
            }
        }
    }
}

#endif // CORES_BASIC_GIT

