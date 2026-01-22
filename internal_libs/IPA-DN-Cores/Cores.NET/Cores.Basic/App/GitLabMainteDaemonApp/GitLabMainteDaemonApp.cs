// IPA Cores.NET
// 
// Copyright (c) 2019- IPA CyberLab.
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

// Author: Daiyuu Nobori
// Description

#if CORES_BASIC_JSON && (CORES_BASIC_WEBAPP || CORES_BASIC_HTTPSERVER) && CORES_BASIC_SECURITY

#pragma warning disable CA2235 // Mark all non-serializable fields

using System;
using System.Buffers;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Serialization;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Routing;

namespace IPA.Cores.Basic;

public static partial class CoresConfig
{
    public static partial class GitLabMainteDaemonHost
    {
        public static readonly Copenhagen<string> _Test = "Hello";
        public static readonly Copenhagen<int> MaxPaging = 50000 - 1;
    }
}

public class GitLabMainteClientSettings : INormalizable
{
    public string GitLabBaseUrl = "";
    public string PrivateToken = "";
    public string BasicAuthUsername = "";
    public string BasicAuthPassword = "";

    public void Normalize()
    {
        if (GitLabBaseUrl._IsEmpty()) GitLabBaseUrl = "https://git-lab-address-here/";
        if (PrivateToken._IsEmpty()) PrivateToken = "PrivateTokenHere";

        this.BasicAuthUsername = this.BasicAuthUsername._NonNullTrim();
        this.BasicAuthPassword = this.BasicAuthPassword._NonNullTrim();
    }

    public string GitLabBaseUrlWithBasicAuth
    {
        get
        {
            if (this.BasicAuthUsername._IsEmpty() || this.BasicAuthPassword._IsEmpty())
            {
                return this.GitLabBaseUrl;

            }

            var uri = this.GitLabBaseUrl._ParseUrl();

            UriBuilder b = new UriBuilder(uri.Scheme, uri.Host, uri.Port, uri.AbsolutePath);

            b.UserName = this.BasicAuthUsername;
            b.Password = this.BasicAuthPassword;

            return b.Uri.ToString();
        }
    }
}

public class GitLabMainteClient : AsyncService
{
    public class Project
    {
        public int id;
        public string? description;
        public string? name;
        public string? path;
        public string? path_with_namespace;
        public string? default_branch;
        public string? visibility;
        public bool empty_repo;
        public DateTimeOffset last_activity_at;

        public string GenerateDirName() => path_with_namespace._NonNullTrim()._ReplaceStr("/", "_")._MakeSafeFileName().ToLowerInvariant();
    }

    public class User
    {
        public int id;
        public string? username;
        public string? name;
        public string? state; // active or blocked_pending_approval
        public string? commit_email;
        public bool bot;
        public bool is_admin;

        public bool IsSystemUser()
        {
            if (this.bot || this.username._IsSamei("ghost"))
            {
                return true;
            }

            return false;
        }
    }

    public class Group
    {
        public int id;
        public string? name;
        public string? path;
    }

    public class GroupMember
    {
        public int id;
        public string? username;
    }

    public GitLabMainteClientSettings Settings { get; }
    public WebApi Web { get; }
    public string GitExe { get; }

    public GitLabMainteClient(GitLabMainteClientSettings settings)
    {
        try
        {
            this.Settings = settings;

            if (Env.IsWindows)
            {
                this.GitExe = Util.GetGitForWindowsExeFileName();
            }
            else
            {
                this.GitExe = Lfs.UnixGetFullPathFromCommandName("git");
            }

            this.Web = new WebApi(new WebApiOptions(new WebApiSettings { SslAcceptAnyCerts = true, }));
        }
        catch
        {
            this._DisposeSafe();
            throw;
        }
    }

    protected override async Task CleanupImplAsync(Exception? ex)
    {
        try
        {
            await this.Web._DisposeSafeAsync();
        }
        finally
        {
            await base.CleanupImplAsync(ex);
        }
    }

    public async Task<List<Project>> EnumProjectsAsync(CancellationToken cancel = default)
    {
        string url = this.Settings.GitLabBaseUrlWithBasicAuth._CombineUrl($"/api/v4/projects?private_token={this.Settings.PrivateToken}&per_page={CoresConfig.GitLabMainteDaemonHost.MaxPaging.Value}").ToString();

        var res = await this.Web.SimpleQueryAsync(WebMethods.GET, url, cancel);

        return res.ToString()._JsonToObject<List<Project>>()!;
    }

    public async Task<List<User>> EnumUsersAsync(CancellationToken cancel = default)
    {
        string url = this.Settings.GitLabBaseUrlWithBasicAuth._CombineUrl($"/api/v4/users?private_token={this.Settings.PrivateToken}&per_page={CoresConfig.GitLabMainteDaemonHost.MaxPaging.Value}").ToString();

        var res = await this.Web.SimpleQueryAsync(WebMethods.GET, url, cancel);

        return res.ToString()._JsonToObject<List<User>>()!;
    }

    public async Task<List<Group>> EnumGroupsAsync(CancellationToken cancel = default)
    {
        string url = this.Settings.GitLabBaseUrlWithBasicAuth._CombineUrl($"/api/v4/groups?private_token={this.Settings.PrivateToken}&per_page={CoresConfig.GitLabMainteDaemonHost.MaxPaging.Value}").ToString();

        var res = await this.Web.SimpleQueryAsync(WebMethods.GET, url, cancel);

        return res.ToString()._JsonToObject<List<Group>>()!;
    }

    public async Task<List<GroupMember>> EnumGroupMembersAsync(int groupId, CancellationToken cancel = default)
    {
        string url = this.Settings.GitLabBaseUrlWithBasicAuth._CombineUrl($"/api/v4/groups/{groupId}/members?private_token={this.Settings.PrivateToken}&per_page={CoresConfig.GitLabMainteDaemonHost.MaxPaging.Value}").ToString();

        var res = await this.Web.SimpleQueryAsync(WebMethods.GET, url, cancel);

        return res.ToString()._JsonToObject<List<GroupMember>>()!;
    }

    public async Task JoinUserToGroupAsync(int userId, int groupId, int accessLevel = 30, CancellationToken cancel = default)
    {
        string url = this.Settings.GitLabBaseUrlWithBasicAuth._CombineUrl($"/api/v4/groups/{groupId}/members").ToString();

        var res = await this.Web.SimpleQueryAsync(WebMethods.POST, url, cancel, Consts.MimeTypes.FormUrlEncoded,
            ("private_token", this.Settings.PrivateToken),
            ("user_id", userId.ToString()),
            ("expires_at", accessLevel.ToString()),
            ("access_level", accessLevel.ToString()),
            ("expires_at", "9931-12-21")
            );
    }

    readonly AsyncLock Lock1 = new AsyncLock();

    public async Task GitPullFromRepositoryAsync(string repositoryPath, string localDir, string branch, CancellationToken cancel = default)
    {
        string gitUrl = this.Settings.GitLabBaseUrl._CombineUrl(repositoryPath + ".git").ToString();

        gitUrl = gitUrl._ReplaceStr("https://", $"https://oauth2:{this.Settings.PrivateToken}@");

        await Lfs.CreateDirectoryAsync(localDir);

        // localDir ディレクトリに .git ディレクトリは存在するか?
        string dotgitDir = localDir._CombinePath(".git");

        bool init = false;

        StrDictionary<string> envVars = new StrDictionary<string>();

        // empty config
        string emptyCfgPath = Env.MyLocalTempDir._CombinePath("empty.txt");

        using (await Lock1.LockWithAwait(cancel))
        {
            if (await Lfs.IsFileExistsAsync(emptyCfgPath, cancel) == false)
            {
                await Lfs.WriteStringToFileAsync(emptyCfgPath, "\n\n", cancel: cancel);
            }
        }

        envVars.Add("GIT_CONFIG_GLOBAL", emptyCfgPath);
        envVars.Add("GIT_CONFIG_SYSTEM", emptyCfgPath);

        if (await Lfs.IsDirectoryExistsAsync(dotgitDir, cancel))
        {
            try
            {
                // update を試みる
                await EasyExec.ExecAsync(this.GitExe, $"pull origin {branch}", localDir, cancel: cancel, additionalEnvVars: envVars);
            }
            catch (Exception ex)
            {
                ex._Error();
                init = true;
            }
        }
        else
        {
            init = true;
        }

        if (init)
        {
            // 初期化する
            await Lfs.DeleteDirectoryAsync(localDir, true, cancel: cancel, forcefulUseInternalRecursiveDelete: true);

            // git clone をする
            await EasyExec.ExecAsync(this.GitExe, $"clone {gitUrl} {localDir}", cancel: cancel, additionalEnvVars: envVars);

            // update を試みる
            await EasyExec.ExecAsync(this.GitExe, $"pull origin {branch}", localDir, cancel: cancel, additionalEnvVars: envVars);
        }
    }
}


public class GitLabMainteDaemonSettings : INormalizable
{
    public GitLabMainteClientSettings GitLabClientSettings = null!;

    public SmtpClientSettings SmtpSettings = null!;

    public SmtpBassicSettings MailSettings = null!;

    public List<string> DefaultGroupsAllUsersWillJoin = new List<string>();

    public int ForceRepositoryUpdateIntervalMsecs = 0;
    public int UsersListMainteIntervalMsecs = 0;

    public int ForceRepositoryUpdateIntervalMsecs_InErrorMax = 0;
    public int UsersListMainteIntervalMsecs_InErrorMax = 0;

    public string GitMirrorDataRootDir = "";
    public string GitWebDataRootDir = "";

    public int HttpTimeoutMsecs = 0;

    public List<string> ExtsAsMimeTypeUtf8 = new List<string>();

    public string Title = "";

    public string AdminNameStr = "";

    public long MaxAccessLogFileSizeInSpecificDir = 0;

    public int NewUserSaisokuIntervalSecs = 0;
    public Dictionary<string, DateTimeOffset> LastNewUserSaisokuSentTime = new(StrCmpi);

    public void Normalize()
    {
        this.GitLabClientSettings ??= new GitLabMainteClientSettings();
        this.GitLabClientSettings.Normalize();

        this.SmtpSettings ??= new SmtpClientSettings();
        this.SmtpSettings.Normalize();

        this.MailSettings ??= new SmtpBassicSettings();
        this.MailSettings.Normalize();

        if (this.AdminNameStr._IsEmpty())
        {
            this.AdminNameStr = "本システムの管理者: XXX YYY  連絡方法: 電子メール xxx@example.org  Slack: 「aaa」というワークスペースの「#aaa」というスレッド";
        }

        this.DefaultGroupsAllUsersWillJoin ??= new List<string>();
        if (this.DefaultGroupsAllUsersWillJoin.Count == 0)
        {
            this.DefaultGroupsAllUsersWillJoin.Add("__default_groups_here__");
        }
        this.DefaultGroupsAllUsersWillJoin = this.DefaultGroupsAllUsersWillJoin.Distinct(StrCmpi).ToList();

        if (this.ForceRepositoryUpdateIntervalMsecs <= 0) this.ForceRepositoryUpdateIntervalMsecs = 3 * 60 * 1000;
        if (this.ForceRepositoryUpdateIntervalMsecs_InErrorMax <= 0) this.ForceRepositoryUpdateIntervalMsecs_InErrorMax = 3 * 60 * 60 * 1000;

        if (this.UsersListMainteIntervalMsecs <= 0) this.UsersListMainteIntervalMsecs = 15 * 1000;
        if (this.UsersListMainteIntervalMsecs_InErrorMax <= 0) this.UsersListMainteIntervalMsecs_InErrorMax = 3 * 60 * 60 * 1000;

        if (GitMirrorDataRootDir._IsEmpty())
        {
            if (Env.IsWindows)
            {
                GitMirrorDataRootDir = @"c:\tmp2\git_mirror_root\";
            }
            else
            {
                GitMirrorDataRootDir = @"/data1/git_mirror_root/";
            }
        }

        if (GitWebDataRootDir._IsEmpty())
        {
            if (Env.IsWindows)
            {
                GitWebDataRootDir = @"c:\tmp2\git_web_root\";
            }
            else
            {
                GitWebDataRootDir = @"/data1/git_web_root/";
            }
        }

        if (this.HttpTimeoutMsecs <= 0)
        {
            this.HttpTimeoutMsecs = 30 * 1000;
        }

        if (this.Title._IsEmpty())
        {
            this.Title = "GitLabMainteDaemon";
        }

        this.ExtsAsMimeTypeUtf8 ??= new List<string>();

        if (this.ExtsAsMimeTypeUtf8.Any() == false)
        {
            this.ExtsAsMimeTypeUtf8.Add(".txt");
            this.ExtsAsMimeTypeUtf8.Add(".cfg");
            this.ExtsAsMimeTypeUtf8.Add(".config");
            this.ExtsAsMimeTypeUtf8.Add(".dat");
            this.ExtsAsMimeTypeUtf8.Add(".xml");
        }

        LastNewUserSaisokuSentTime ??= new(StrCmpi);

        if (NewUserSaisokuIntervalSecs <= 0)
        {
            NewUserSaisokuIntervalSecs = 73 * 60 * 60;
        }

        this.ExtsAsMimeTypeUtf8 = this.ExtsAsMimeTypeUtf8.Distinct().OrderBy(x => x).ToList();

        if (MaxAccessLogFileSizeInSpecificDir <= 0)
        {
            MaxAccessLogFileSizeInSpecificDir = 1000 * 1000 * 1000;
        }
    }
}

public class GitLabMainteDaemonApp : AsyncService
{
    public class PublishConfigData
    {
        public string Username = "";
        public string Password = "";
    }

    readonly HiveData<GitLabMainteDaemonSettings> SettingsHive;

    // 'Config\GitLabMainteDaemon' のデータ
    public GitLabMainteDaemonSettings Settings => SettingsHive.ManagedData;

    readonly CriticalSection LockList = new CriticalSection<GitLabMainteDaemonApp>();

    public GitLabMainteClient GitLabClient { get; }

    Task MainLoop1Task;
    Task MainLoop2Task;

    public CgiHttpServer Cgi { get; }

    public LogBrowser LogBrowser { get; }

    public long HookFiredTick { get; set; } = 1;

    public GitLabMainteDaemonApp()
    {
        try
        {
            // Settings を読み込む
            this.SettingsHive = new HiveData<GitLabMainteDaemonSettings>(Hive.SharedLocalConfigHive, $"GitLabMainteDaemon", null, HiveSyncPolicy.AutoReadWriteFile);

            this.GitLabClient = new GitLabMainteClient(this.Settings.GitLabClientSettings);

            // TODO: ここでサーバーを立ち上げるなどの初期化処理を行なう

            this.MainLoop1Task = TaskUtil.StartAsyncTaskAsync(Loop1_MainteUsersAsync(this.GrandCancel));

            this.MainLoop2Task = TaskUtil.StartAsyncTaskAsync(Loop2_MainteRepositoriesAsync(this.GrandCancel));

            // Log Browser を立ち上げる
            var logBrowserOptions = new LogBrowserOptions(
                this.Settings.GitWebDataRootDir, this.Settings.Title, flags: LogBrowserFlags.SecureJson | LogBrowserFlags.SecureJson_FlatDir | LogBrowserFlags.NoRootDirectory,
                extsAsMimeTypeUtf8: this.Settings.ExtsAsMimeTypeUtf8, logFileMaxSizePerDir: this.Settings.MaxAccessLogFileSizeInSpecificDir);

            this.LogBrowser = new LogBrowser(logBrowserOptions, "/d");

            // HTTP サーバーを立ち上げる
            this.Cgi = new CgiHttpServer(new CgiHandler(this), new HttpServerOptions()
            {
                AutomaticRedirectToHttpsIfPossible = false,
                UseKestrelWithIPACoreStack = false,
                HttpPortsList = new int[] { 80 }.ToList(),
                HttpsPortsList = new int[] { 443 }.ToList(),
                UseStaticFiles = false,
                MaxRequestBodySize = 32 * 1024,
                ReadTimeoutMsecs = this.Settings.HttpTimeoutMsecs,
                DenyRobots = true,
            },
            true);
        }
        catch
        {
            this._DisposeSafe();
            throw;
        }
    }


    protected override async Task CleanupImplAsync(Exception? ex)
    {
        try
        {
            // TODO: ここでサーバーを終了するなどのクリーンアップ処理を行なう
            await this.LogBrowser._DisposeSafeAsync(ex);
            await this.Cgi._DisposeSafeAsync(ex);

            await this.MainLoop1Task._TryWaitAsync();
            await this.MainLoop2Task._TryWaitAsync();

            await this.GitLabClient._DisposeSafeAsync(ex);

            this.SettingsHive._DisposeSafe();
        }
        finally
        {
            await base.CleanupImplAsync(ex);
        }
    }

    public class CgiHandler : CgiHandlerBase
    {
        public readonly GitLabMainteDaemonApp App;

        public CgiHandler(GitLabMainteDaemonApp app)
        {
            this.App = app;
        }

        protected override void InitActionListImpl(CgiActionList noAuth, CgiActionList reqAuth)
        {
            try
            {
                noAuth.AddAction("/", WebMethodBits.GET | WebMethodBits.HEAD, async (ctx) =>
                {
                    await Task.CompletedTask;

                    string password = ctx.QueryString.ToString();

                    if (password._IsEmpty())
                    {
                        return new HttpStringResult("Hello");
                    }
                    else
                    {
                        PublishConfigData data = new PublishConfigData
                        {
                            Username = "user",
                            Password = Secure.SaltPassword(password),
                        };

                        return new HttpStringResult(data._ObjectToJson(), Consts.MimeTypes.Json);
                    }
                });

                noAuth.AddAction("/hook", WebMethodBits.GET | WebMethodBits.HEAD | WebMethodBits.POST, async (ctx) =>
                {
                    await Task.CompletedTask;
                    this.App.HookFiredTick = TickNow;
                    return new HttpStringResult("OK");
                });
            }
            catch
            {
                this._DisposeSafe();
                throw;
            }
        }

        public override void ConfigureImpl_BeforeHelper(HttpServerStartupConfig cfg, IApplicationBuilder app, IWebHostEnvironment env, IHostApplicationLifetime lifetime, RouteBuilder rb)
        {
            rb.MapGet("/d/{*path}", async (req, res, route) =>
            {
                await this.App.LogBrowser.GetRequestHandlerAsync(req, res, route);
            });
        }

    }


    // Git リポジトリの自動ダウンロード
    async Task Loop2_MainteRepositoriesAsync(CancellationToken cancel = default)
    {
        long lastHookTick = -1;

        int nextTimeWaitInterval = this.Settings.ForceRepositoryUpdateIntervalMsecs;

        while (cancel.IsCancellationRequested == false)
        {
            bool hasError = false;

            try
            {
                await Lfs.CreateDirectoryAsync(this.Settings.GitMirrorDataRootDir);
                await Lfs.CreateDirectoryAsync(this.Settings.GitWebDataRootDir);
            }
            catch
            {
            }

            try
            {
                // Git リポジトリを列挙
                var projects = await this.GitLabClient.EnumProjectsAsync(cancel);

                ConcurrentHashSet<string> dirNames = new ConcurrentHashSet<string>(StrCmpi);

                // メモ: last_activity_at の値を信用してはならない。これは GitLab がキャッシュしているので、なかなか更新されない。

                var targetProjects = projects.Where(p => p.empty_repo == false && p.path_with_namespace._IsFilled() && p.default_branch._IsFilled()).OrderByDescending(x => x.last_activity_at).ThenBy(x => x.path_with_namespace, StrCmpi);

                await TaskUtil.ForEachAsync(8, targetProjects, async (proj, index, cancel) =>
                {
                    cancel.ThrowIfCancellationRequested();

                    try
                    {
                        string dirname = proj.GenerateDirName();

                        dirNames.Add(dirname);

                        string gitRoot = this.Settings.GitMirrorDataRootDir._CombinePath(dirname);
                        string webRoot = this.Settings.GitWebDataRootDir._CombinePath(dirname);

                        await this.GitLabClient.GitPullFromRepositoryAsync(proj.path_with_namespace!, gitRoot, proj.default_branch!, cancel);

                        await this.SyncGitLocalRepositoryDirToWebRootDirAsync(gitRoot, webRoot, cancel);
                    }
                    catch (Exception ex)
                    {
                        hasError = true;
                        ex._Error();
                    }
                }, cancel);

                // GitLab 上に存在せず local に存在する gitRoot を列挙して削除する
                var existingLocalGitDirs = await Lfs.EnumDirectoryAsync(this.Settings.GitMirrorDataRootDir, cancel: cancel);
                foreach (var d in existingLocalGitDirs.Where(x => x.IsDirectory && dirNames.Contains(x.Name) == false))
                {
                    cancel.ThrowIfCancellationRequested();

                    try
                    {
                        $"Deleting '{d.FullPath}' in existingLocalGitDirs ..."._Error();
                        await Lfs.DeleteDirectoryAsync(d.FullPath, true, cancel, true);
                    }
                    catch (Exception ex)
                    {
                        hasError = true;
                        ex._Error();
                    }
                }

                // GitLab 上に存在せず local に存在する webRoot を列挙して削除する
                var existingLocalWebDirs = await Lfs.EnumDirectoryAsync(this.Settings.GitWebDataRootDir, cancel: cancel);
                foreach (var d in existingLocalWebDirs.Where(x => x.IsDirectory && dirNames.Contains(x.Name) == false))
                {
                    cancel.ThrowIfCancellationRequested();

                    try
                    {
                        await Lfs.DeleteFileAsync(d.FullPath._CombinePath(Consts.FileNames.LogBrowserSecureJson), cancel: cancel);
                    }
                    catch (Exception ex)
                    {
                        hasError = true;
                        ex._Error();
                    }
                }
            }
            catch (Exception ex)
            {
                hasError = true;
                ex._Error();
            }

            if (hasError)
            {
                nextTimeWaitInterval = Math.Min(nextTimeWaitInterval * 2, this.Settings.ForceRepositoryUpdateIntervalMsecs_InErrorMax);
                $"nextTimeWaitInterval = {nextTimeWaitInterval._ToString3()}"._Error();
            }
            else
            {
                nextTimeWaitInterval = this.Settings.ForceRepositoryUpdateIntervalMsecs;
            }

            await TaskUtil.AwaitWithPollAsync(nextTimeWaitInterval, 500, () =>
            {
                long currentHookTick = this.HookFiredTick;

                if (lastHookTick != currentHookTick)
                {
                    lastHookTick = currentHookTick;
                    return true;
                }

                return false;
            },
            cancel,
            true);
        }
    }

    // GitLab のユーザーメンテナンス
    async Task Loop1_MainteUsersAsync(CancellationToken cancel = default)
    {
        long lastHookTick = -1;

        List<GitLabMainteClient.User> lastPendingUsers = new List<GitLabMainteClient.User>();

        int nextTimeWaitInterval = this.Settings.UsersListMainteIntervalMsecs;

        while (cancel.IsCancellationRequested == false)
        {
            bool hasError = false;

            try
            {
                // 新規申請中のユーザーが増えたら管理者にメールで知らせる
                var users = await this.GitLabClient.EnumUsersAsync(cancel);

                var pendingUsers = users.Where(x => x.IsSystemUser() == false && x.state == "blocked_pending_approval").OrderBy(x => x.id);

                var newPendingUsers = pendingUsers.Where(u => lastPendingUsers.Where(a => a.id == u.id).Any() == false);

                try
                {
                    StringWriter w = new StringWriter();

                    string url = this.Settings.GitLabClientSettings.GitLabBaseUrl._CombineUrl("/admin/users?filter=blocked_pending_approval").ToString();

                    string subject = $"{url._ParseUrl().Host} にユーザー {newPendingUsers.Take(2).Select(x => ("[" + x.commit_email._NonNullTrim() + " " + x.name + " " + x.username + "]"))._Combine(" ,")} の参加申請がありました";

                    w.WriteLine(subject + "。");
                    w.WriteLine();

                    w.WriteLine($"GitLab のアドレス: {url}");
                    w.WriteLine();

                    w.WriteLine($"現在時刻: {DtOffsetNow._ToDtStr()}");

                    w.WriteLine();

                    w.WriteLine($"新しい申請中のユーザー ({newPendingUsers.Count()}):");

                    int num = 0;

                    foreach (var user in newPendingUsers)
                    {
                        lastPendingUsers.Add(user._CloneDeep());

                        w.WriteLine("- " + user.name + " " + user.username + " " + user.commit_email);

                        num++;
                    }

                    w.WriteLine();

                    w.WriteLine($"現在申請中のユーザー一覧 ({pendingUsers.Count()})");

                    foreach (var user in pendingUsers)
                    {
                        w.WriteLine("- " + user.name + " " + user.username + " " + user.commit_email);
                    }

                    w.WriteLine();

                    w.WriteLine($"GitLab のアドレス: {url}");
                    w.WriteLine();
                    w.WriteLine();

                    //Dbg.Where();
                    if (num >= 1)
                    {
                        await this.SendMailAsync(subject, w.ToString(), cancel);
                    }
                }
                catch (Exception ex)
                {
                    ex._Error();
                }

                var now = DtOffsetNow;

                // 新規申請中のユーザーが増えたら各ユーザーにもメールで管理者に連絡するよう催促する
                foreach (var pendingUser in pendingUsers.Where(x => x.commit_email._CheckMailAddress()).OrderBy(x => x.commit_email, StrCmpi).ToList())
                {
                    try
                    {
                        bool sendNow = false;
                        bool isFirstTime = false;

                        if (this.Settings.LastNewUserSaisokuSentTime.TryGetValue(pendingUser.commit_email!, out var lastDt) == false)
                        {
                            sendNow = true;
                            isFirstTime = true;
                        }

                        else if (lastDt.AddSeconds(this.Settings.NewUserSaisokuIntervalSecs) < now)
                        {
                            sendNow = true;
                        }

                        if (sendNow)
                        {
                            this.Settings.LastNewUserSaisokuSentTime[pendingUser.commit_email!] = now; // タイムスタンプの更新を最初にする

                            using StringWriter writer = new();

                            string subject = $"{this.Settings.GitLabClientSettings.GitLabBaseUrl._ParseUrl().Host} の Git システムへの参加ご登録について";

                            if (isFirstTime == false)
                            {
                                subject += " [再送]";
                            }

                            writer.WriteLine($"{subject}");
                            writer.WriteLine();
                            writer.WriteLine($"{pendingUser.commit_email} 様");
                            writer.WriteLine();
                            writer.WriteLine($"このたびは、{this.Settings.GitLabClientSettings.GitLabBaseUrl} の Git システムへの参加登録申請をいただき、誠にありがとうございます。");
                            writer.WriteLine();
                            writer.WriteLine();
                            writer.WriteLine("本メールは、Git システムによって自動的に送付しております。");
                            writer.WriteLine("本メールの送信元メールアドレスには、返信できません。");
                            writer.WriteLine();
                            writer.WriteLine();
                            writer.WriteLine("【参加登録申請の後、登録承認には、管理者への連絡が必ず必要です】");
                            writer.WriteLine();
                            writer.WriteLine("上記の URL のトップ画面にもご案内を掲載しておりますが、");
                            writer.WriteLine("本 Git システムへの参加登録申請をいただいた後、");
                            writer.WriteLine("この Git サーバーの管理者 (以下に記載) に、");
                            writer.WriteLine("必ず、参加登録しようとした Google アカウントの ID を伝達していただく");
                            writer.WriteLine("必要があります。電子メールまたは Slack 等の既存の一応信頼できる");
                            writer.WriteLine("連絡手段で連絡ください。管理者がその内容を確認し、問題無い場合は、");
                            writer.WriteLine("できるだけ早くアクセス権を付与します。");
                            writer.WriteLine();
                            writer.WriteLine("上記のご連絡がない場合、管理者においては、参加登録があった ID が");
                            writer.WriteLine("真の関係者の ID であるかどうか判断することができないため、");
                            writer.WriteLine("アカウント登録は Pending 状態となったままとなり、");
                            writer.WriteLine("Git システムへのログインができない状態が続きます。");
                            writer.WriteLine();
                            writer.WriteLine();
                            writer.WriteLine("なお、長期間ご連絡がない Pending 状態のアカウントの登録申請は、");
                            writer.WriteLine("定期的に削除させていただく場合があります。");
                            writer.WriteLine();
                            writer.WriteLine("後日、その場合も、再度、参加登録を申請いただくことができます。");
                            writer.WriteLine();
                            writer.WriteLine();
                            writer.WriteLine("ご不明な点は、管理者までご連絡いただくか、Slack やメール等で");
                            writer.WriteLine("すでに Git システムに参加されていると思われる方に相談してみて");
                            writer.WriteLine("ください。");
                            writer.WriteLine();
                            writer.WriteLine("よろしくお願い申し上げます。");
                            writer.WriteLine();
                            writer.WriteLine();
                            writer.WriteLine($"[{this.Settings.GitLabClientSettings.GitLabBaseUrl} システムの管理者情報]");
                            writer.WriteLine();
                            writer.WriteLine($"{this.Settings.AdminNameStr}");
                            writer.WriteLine();
                            writer.WriteLine();

                            writer.NewLine = Str.NewLine_Str_Windows;

                            string writerBody = writer.ToString();

                            // ユーザーに送付
                            try
                            {
                                await SmtpUtil.SendAsync(this.Settings.SmtpSettings, this.Settings.MailSettings.MailFrom, pendingUser.commit_email!, subject, writerBody, true, cancel);
                            }
                            catch (Exception ex)
                            {
                                ex._Error();
                            }

                            if (isFirstTime)
                            {
                                // 管理者に送付
                                try
                                {
                                    await this.SendMailAsync(subject, writerBody, cancel);
                                }
                                catch (Exception ex)
                                {
                                    ex._Error();
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        ex._Error();

                        hasError = true;
                    }
                }

            }
            catch (Exception ex)
            {
                ex._Error();

                hasError = true;
            }

            // すべてのユーザーをデフォルトグループに自動追加する
            try
            {
                await this.JoinAllUsersToSpecificGroupAsync(this.Settings.DefaultGroupsAllUsersWillJoin, cancel);
            }
            catch (Exception ex)
            {
                ex._Error();

                hasError = true;
            }

            if (hasError)
            {
                nextTimeWaitInterval = Math.Min(nextTimeWaitInterval * 2, this.Settings.UsersListMainteIntervalMsecs_InErrorMax);
                $"nextTimeWaitInterval = {nextTimeWaitInterval._ToString3()}"._Error();
            }
            else
            {
                nextTimeWaitInterval = this.Settings.UsersListMainteIntervalMsecs;
            }

            await TaskUtil.AwaitWithPollAsync(nextTimeWaitInterval, 500, () =>
            {
                long currentHookTick = this.HookFiredTick;

                if (lastHookTick != currentHookTick)
                {
                    lastHookTick = currentHookTick;
                    return true;
                }

                return false;
            },
            cancel,
            true);
        }
    }

    public async Task SendMailAsync(string subject, string body, CancellationToken cancel = default)
    {
        foreach (var dest in this.Settings.MailSettings.MailToList.Distinct(StrCmpi).OrderBy(x => x, StrCmpi))
        {
            await SmtpUtil.SendAsync(this.Settings.SmtpSettings, this.Settings.MailSettings.MailFrom, dest, subject, body, true, cancel);
        }
    }

    public async Task JoinAllUsersToSpecificGroupAsync(IEnumerable<string> groupNamesList, CancellationToken cancel = default)
    {
        var groups = await this.GitLabClient.EnumGroupsAsync(cancel);

        var users = await this.GitLabClient.EnumUsersAsync(cancel);

        var activeUsers = users.Where(u => u.state._IsSamei("active") && u.IsSystemUser() == false);

        var targetGroups = groups.Where(g => groupNamesList.Where(s => s._IsSamei(g.name)).Any()).OrderBy(g => g.id);

        foreach (var group in targetGroups)
        {
            var members = await this.GitLabClient.EnumGroupMembersAsync(group.id, cancel);

            var usersNotInThisGroup = activeUsers.Where(u => members.Where(m => m.id == u.id).Any() == false).OrderBy(u => u.id);

            foreach (var user in usersNotInThisGroup)
            {
                Con.WriteLine($"Joining User {user.name} ({user.commit_email}) to Group {group.name} ...");

                try
                {
                    await this.GitLabClient.JoinUserToGroupAsync(user.id, group.id, cancel: cancel);

                    Con.WriteLine("OK.");
                }
                catch (Exception ex)
                {
                    ex._Error();
                }
            }
        }
    }

    public async Task SyncGitLocalRepositoryDirToWebRootDirAsync(string gitLocalRootDir, string webLocalRootDir, CancellationToken cancel = default)
    {
        string gitCommitId = await Lfs.GetCurrentGitCommitIdAsync(gitLocalRootDir, cancel);

        var param = new CopyDirectoryParams(
              CopyDirectoryFlags.AsyncCopy | CopyDirectoryFlags.Overwrite | CopyDirectoryFlags.Recursive | CopyDirectoryFlags.DeleteNotExistDirs | CopyDirectoryFlags.DeleteNotExistFiles | CopyDirectoryFlags.SilenceSuccessfulReport,
              FileFlags.WriteOnlyIfChanged,
              new FileMetadataCopier(FileMetadataCopyMode.TimeAll),
              new FileMetadataCopier(FileMetadataCopyMode.TimeAll),
              determineToCopyCallback: (d, e) =>
              {
                  if (e.IsFile && e.Name._IsSamei(Consts.FileNames.GitLabMainte_PublishFileName)) return false;
                  if (e.IsDirectory && e.Name._IsSamei(".git")) return false;
                  return true;
              },
              determineToDeleteCallback: (e) =>
              {
                  if (e.IsFile && e.Name._IsSamei(Consts.FileNames.GitLabMainte_CommitIdFileName)) return false;
                  if (e.IsFile && e.Name._IsSamei(Consts.FileNames.LogBrowserSecureJson)) return false;
                  if (e.IsDirectory && e.Name._IsSamei(Consts.FileNames.LogBrowserAccessLogDirName)) return false;
                  return true;
              }
              );

        string publishPath = gitLocalRootDir._CombinePath(Consts.FileNames.GitLabMainte_PublishFileName);

        await Lfs.CopyDirAsync(gitLocalRootDir, webLocalRootDir, param: param, cancel: cancel);

        await Lfs.WriteStringToFileAsync(webLocalRootDir._CombinePath(Consts.FileNames.GitLabMainte_CommitIdFileName),
            gitCommitId + "\n",
            FileFlags.WriteOnlyIfChanged,
            cancel: cancel);

        PublishConfigData? config = await Lfs.ReadJsonFromFileAsync<PublishConfigData>(publishPath, cancel: cancel, nullIfError: true);

        if (config == null)
        {
            if (await Lfs.IsFileExistsAsync(publishPath))
            {
                config = new PublishConfigData();
            }
        }

        string secureJsonPath = webLocalRootDir._CombinePath(Consts.FileNames.LogBrowserSecureJson);

        if (config == null)
        {
            try
            {
                await Lfs.DeleteFileAsync(secureJsonPath, cancel: cancel);
            }
            catch (Exception ex)
            {
                ex._Error();
            }
        }
        else
        {
            bool requireAuth = config.Username._IsFilled() && config.Password._IsFilled();

            LogBrowserSecureJson sj = new LogBrowserSecureJson();

            sj.AuthRequired = requireAuth;

            if (requireAuth)
            {
                sj.AuthDatabase = new KeyValueList<string, string>();
                sj.AuthDatabase.Add(config.Username, config.Password);
                sj.AuthSubDirName = "auth";
            }
            sj.DisableAccessLog = false;
            sj.AllowZipDownload = true;

            await Lfs.WriteJsonToFileAsync(secureJsonPath, sj, FileFlags.WriteOnlyIfChanged, cancel: cancel);
        }
    }
}

#endif

