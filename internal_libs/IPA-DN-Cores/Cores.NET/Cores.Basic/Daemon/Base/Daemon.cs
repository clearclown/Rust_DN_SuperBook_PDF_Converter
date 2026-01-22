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

#if CORES_BASIC_DAEMON

#pragma warning disable CA2235 // Mark all non-serializable fields

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.ServiceProcess;
using System.Diagnostics.CodeAnalysis;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using System.Net;
using System.Runtime.Serialization;
using IPA.Cores.Basic.App.DaemonCenterLib;

namespace IPA.Cores.Helper.Basic
{
    public static class DaemonHelper
    {
        public static void Start(this Daemon daemon, DaemonStartupMode startupMode, object? param = null) => daemon.StartAsync(startupMode, param)._GetResult();
        public static void Stop(this Daemon daemon, bool silent = false) => daemon.StopAsync(silent)._GetResult();
    }
}

namespace IPA.Cores.Basic
{
    // グローバルな Daemon 状態の管理
    public static class GlobalDaemonStateManager
    {
        // 現在の Daemon 設定のキャッシュ
        static DaemonSettings CurrentDaemonSettingsInternal = new DaemonSettings();
        public static DaemonSettings CurrentDaemonSettings => CurrentDaemonSettingsInternal._CloneDeep();
        public static void SetCurrentDaemonSettings(DaemonSettings settings)
        {
            CurrentDaemonSettingsInternal = settings._CloneDeep();
        }

        // Startup arguments
        public static string StartupArguments => CurrentDaemonSettingsInternal.DaemonStartupArgument._NonNullTrim();

        // Daemon Secret
        public static string DaemonSecret => CurrentDaemonSettingsInternal.DaemonSecret._NonNullTrim();

        // Daemon ZIP 暗号化パスワード
        public static string DaemonZipEncryptPassword => CurrentDaemonSettingsInternal.DaemonZipEncryptPassword._NonNullTrim();

        // DaemonCenter に伝えたいメタデータの Dictionary
        public static readonly ConcurrentDictionary<string, string> MetaStatusDictionary = new ConcurrentDictionary<string, string>();

        // DaemonCenter に接続を行なう際の自分自身の IP アドレス
        public static IPAddress DaemonClientLocalIpAddress { get; private set; } = IPAddress.Any;

        // DaemonCenter に接続を行なう際の自分自身の IP アドレスはグローバル IP か?
        public static bool IsDaemonClientLocalIpAddressGlobal { get; private set; } = false;

        // FileBrowser の HTTPS ポート番号
        public static int FileBrowserHttpsPortNumber { get; set; } = 0;

        // RazorRuntimeCompilation を有効化するかどうか
        public static bool EnableAspNetEnableRazorRuntimeCompilation { get; set; } = false;

        public static void SetDaemonClientLocalIpAddress(string ipAddress)
        {
            ipAddress = ipAddress._NonNullTrim();

            if (ipAddress._IsFilled() && IPAddress.TryParse(ipAddress, out IPAddress? ip))
            {
                // 変数に入れる
                DaemonClientLocalIpAddress = ip;

                // DaemonCenter に接続を行なう際の自分自身の IP アドレスがグローバル IP かどうか判定をする
                IPAddressType type = ip._GetIPAddressType();

                IsDaemonClientLocalIpAddressGlobal = type.BitAny(IPAddressType.GlobalIp);

                // MetaStat に入れる
                MetaStatusDictionary[Consts.DaemonMetaStatKeys.CurrentDaemonClientLocalIp] = ip.ToString();
            }
            else
            {
                IsDaemonClientLocalIpAddressGlobal = false;
            }
        }
    }

    // Daemon 抽象クラス (具体的な Daemon 動作はこのクラスを継承して実装すること)
    public abstract class Daemon
    {
        public DaemonOptions Options { get; }
        public string Name => Options.Name;

        public DaemonStatus Status { get; private set; }
        public FastEventListenerList<Daemon, DaemonStatus> StatusChangedEvent { get; }

        readonly CriticalSection StatusLock = new CriticalSection<Daemon>();

        AsyncLock AsyncLock = new AsyncLock();

        SingleInstance? SingleInstance = null;

        IHolder? Leak;

        object? Param = null;

        public Daemon(DaemonOptions options)
        {
            this.Options = options;
            this.Status = DaemonStatus.Stopped;
            this.StatusChangedEvent = new FastEventListenerList<Daemon, DaemonStatus>();
        }

        protected abstract Task StartImplAsync(DaemonStartupMode startupMode, object? param);
        protected abstract Task StopImplAsync(object? param);

        public bool IsInstanceRunning()
        {
            try
            {
                SingleInstance = new SingleInstance($"svc_instance_{Options.Name}", true);
                SingleInstance._DisposeSafe();
                return false;
            }
            catch { }

            return true;
        }

        public async Task StartAsync(DaemonStartupMode startupMode, object? param = null)
        {
            await Task.Yield();
            using (await AsyncLock.LockWithAwait())
            {
                Leak = LeakChecker.Enter(LeakCounterKind.StartDaemon);

                try
                {
                    if (this.Status != DaemonStatus.Stopped)
                        throw new ApplicationException($"The status of the daemon \"{Options.Name}\" ({Options.FriendlyName}) is '{this.Status}'.");

                    if (this.Options.SingleInstance)
                    {
                        try
                        {
                            SingleInstance = new SingleInstance($"svc_instance_{Options.Name}", true);
                        }
                        catch
                        {
                            throw new ApplicationException($"Another instance of the daemon \"{Options.Name}\" ({Options.FriendlyName}) has been already running.");
                        }
                    }

                    Con.WriteLine($"Starting the daemon \"{Options.Name}\" ({Options.FriendlyName}) ...");

                    this.Status = DaemonStatus.Starting;
                    this.StatusChangedEvent.Fire(this, this.Status);

                    try
                    {
                        await StartImplAsync(startupMode, param);
                    }
                    catch (Exception ex)
                    {
                        Con.WriteError($"Starting the daemon \"{Options.Name}\" ({Options.FriendlyName}) failed.");
                        Con.WriteError($"Error: {ex.ToString()}");

                        this.Status = DaemonStatus.Stopped;
                        this.StatusChangedEvent.Fire(this, this.Status);
                        throw;
                    }

                    Con.WriteLine($"The daemon \"{Options.Name}\" ({Options.FriendlyName}) is now running.");

                    this.Param = param;

                    this.Status = DaemonStatus.Running;
                    this.StatusChangedEvent.Fire(this, this.Status);
                }
                catch
                {
                    Leak._DisposeSafe();
                    throw;
                }
            }
        }


        public async Task StopAsync(bool silent = false)
        {
            await Task.Yield();

            using (await AsyncLock.LockWithAwait())
            {
                if (this.Status != DaemonStatus.Running)
                {
                    if (silent == false)
                        throw new ApplicationException($"The status of the daemon \"{Options.Name}\" ({Options.FriendlyName}) is '{this.Status}'.");
                    else
                        return;
                }

                this.Status = DaemonStatus.Stopping;
                this.StatusChangedEvent.Fire(this, this.Status);

                try
                {
                    Task stopTask = StopImplAsync(this.Param);

                    if (await TaskUtil.WaitObjectsAsync(tasks: stopTask._SingleArray(), timeout: this.Options.StopTimeout, exceptions: ExceptionWhen.None) == ExceptionWhen.TimeoutException)
                    {
                        // Timeouted
                        string msg = $"Error! The StopImplAsync() routine of the daemon \"{Options.Name}\" ({Options.FriendlyName}) has been timed out ({Options.StopTimeout} msecs). Terminating the process forcefully.";
                        Kernel.SelfKill(msg);
                    }

                    this.Param = null;
                }
                catch (Exception ex)
                {
                    Con.WriteLine($"Stopping the daemon \"{Options.Name}\" ({Options.FriendlyName}) failed.");
                    Con.WriteLine($"Error: {ex.ToString()}");

                    this.Status = DaemonStatus.Running;
                    this.StatusChangedEvent.Fire(this, this.Status);
                    throw;
                }

                if (SingleInstance != null)
                {
                    SingleInstance._DisposeSafe();
                    SingleInstance = null;
                }

                Leak._DisposeSafe();

                Con.WriteLine("Flushing local logs...");

                // LogClient を Flush する
                try
                {
                    await TaskUtil.DoAsyncWithTimeout(async c =>
                    {
                        await LocalLogRouter.FlushAsync(cancel: c);
                        return 0;
                    },
                    timeout: CoresConfig.Timeouts.DaemonStopLogFinish);
                }
                catch { }

                Con.WriteLine("Flushing local logs completed.");

                Con.WriteLine($"The daemon \"{Options.Name}\" ({Options.FriendlyName}) is stopped successfully.");

                this.Status = DaemonStatus.Stopped;
                this.StatusChangedEvent.Fire(this, this.Status);
            }
        }

        public override string ToString() => $"\"{Options.Name}\" ({Options.FriendlyName})";

        public void Dispose() { this.Dispose(true); GC.SuppressFinalize(this); }
        protected virtual void Dispose(bool disposing)
        {
            StopAsync(true)._TryGetResult();
        }
    }

    // Daemon 設定
    [Serializable]
    [DataContract]
    public class DaemonSettings : INormalizable
    {
        [DataMember]
        public bool DaemonCenterEnable = false;

        [DataMember]
        public string DaemonCenterRpcUrl = "";

        [DataMember]
        public bool DaemonCenterUseProxy = false;

        [DataMember]
        public string DaemonCenterCertSha = "";

        [DataMember]
        public int DaemonTelnetLogWatcherPort = Consts.Ports.TelnetLogWatcher;

        [DataMember]
        public string DaemonStartupArgument = "";

        [DataMember]
        public string DaemonSecret = "";

        [DataMember]
        public string DaemonZipEncryptPassword = "";

        [DataMember]
        public string DaemonCenterAppId = "";

        [DataMember]
        public string DaemonCenterInstanceGuid = "";

        [DataMember]
        public PauseFlag DaemonPauseFlag = PauseFlag.Run;

        [DataMember]
        public bool LogServerEnable = false;

        [DataMember]
        public string LogServerHost = "";

        [DataMember]
        public int LogServerPort = 0;

        [DataMember]
        public string LogServerCertSha = "";

        [DataMember]
        public string LogServerFilter = "";

        [DataMember]
        public string LogServerMinimalPriority = "";

        [DataMember]
        public bool SyslogEnable = false;

        [DataMember]
        public string SyslogServerHost = "";

        [DataMember]
        public int SyslogServerPort = 0;

        [DataMember]
        public string SyslogServerFilter = "";

        public void Normalize()
        {
            this.DaemonCenterRpcUrl = this.DaemonCenterRpcUrl._NonNullTrim();
            this.DaemonCenterCertSha = this.DaemonCenterCertSha._NonNullTrim();
            this.DaemonStartupArgument = this.DaemonStartupArgument._NonNullTrim();
            this.DaemonCenterAppId = this.DaemonCenterAppId._NonNullTrim();

            this.LogServerHost = this.LogServerHost._NonNullTrim();
            this.LogServerCertSha = this.LogServerCertSha._NonNullTrim();

            if (this.LogServerFilter._IsEmpty())
            {
                this.LogServerFilter = BufferedLogRoute.DefaultFilter;
            }

            LogServerMinimalPriority = LogPriority.Debug.ParseAsDefault(this.LogServerMinimalPriority).ToString();

            // 新しいシークレットを作成する
            if (this.DaemonSecret._IsEmpty()) this.DaemonSecret = Str.GenRandPassword(32);

            // 新しい ZIP 暗号化パスワードを作成する
            if (this.DaemonZipEncryptPassword._IsEmpty()) this.DaemonZipEncryptPassword = Str.GenRandPassword(32);

            // 新しい GUID を作成する
            if (this.DaemonCenterInstanceGuid._IsEmpty()) this.DaemonCenterInstanceGuid = Str.NewGuid();

            if (this.DaemonPauseFlag != PauseFlag.Pause)
                this.DaemonPauseFlag = PauseFlag.Run;

            if (this.LogServerPort == 0)
            {
                this.LogServerPort = Consts.Ports.LogServerDefaultServicePort;
            }

            if (this.SyslogServerPort == 0)
            {
                this.SyslogServerPort = Consts.Ports.SyslogServerPort;
            }

            this.SyslogServerHost = this.SyslogServerHost._NonNullTrim();

            if (this.SyslogServerFilter._IsEmpty())
            {
                this.SyslogServerFilter = BufferedLogRoute.DefaultFilterForSyslog;
            }
        }
    }

    [Flags]
    public enum DaemonMode
    {
        UserMode = 0,
        WindowsServiceMode = 1,
    }

    // 何もしない Daemon
    public class PausingDaemon : Daemon
    {
        public PausingDaemon(DaemonOptions options) : base(options)
        {
        }

        protected override Task StartImplAsync(DaemonStartupMode startupMode, object? param)
        {
            return Task.CompletedTask;
        }

        protected override Task StopImplAsync(object? param)
        {
            return Task.CompletedTask;
        }
    }

    // Daemon をホストするユーティリティクラス (このクラス自体は Daemon の実装ではない)
    public sealed class DaemonHost
    {
        public Daemon Daemon { get; }
        public object? Param { get; }
        public DaemonSettings DefaultDaemonSettings { get; }

        readonly HiveData<DaemonSettings> SettingsHive;

        // 'Config\DaemonSettings' のデータ
        DaemonSettings Settings => SettingsHive.GetManagedDataSnapshot();

        // DaemonCenter クライアントを有効化すべきかどうかのフラグ
        bool IsDaemonCenterEnabled() => (this.Mode == DaemonMode.UserMode &&
            Dbg.IsGitCommandSupported() &&
            Dbg.GetCurrentGitCommitId()._IsFilled() &&
            this.Settings.DaemonCenterEnable && this.Settings.DaemonCenterRpcUrl._IsFilled() &&
            Env.IsWindows == false &&
            Env.IsDotNetCore && Env.IsHostedByDotNetProcess);

        // LogClient を有効化すべきかどうかのフラグ
        bool IsLogServerEnabled() => (this.Settings.LogServerEnable && this.Settings.DaemonPauseFlag != PauseFlag.Pause && this.Settings.LogServerHost._IsFilled());

        // SyslogClient を有効化すべきかどうかのフラグ
        bool IsSyslogEnabled() => (this.Settings.SyslogEnable && this.Settings.DaemonPauseFlag != PauseFlag.Pause && this.Settings.SyslogServerHost._IsFilled());

        IService? CurrentRunningService = null;

        public DaemonHost(Daemon daemon, DaemonSettings defaultDaemonSettings, object? param = null)
        {
            this.DefaultDaemonSettings = (defaultDaemonSettings ?? throw new ArgumentNullException(nameof(defaultDaemonSettings)))._CloneDeep();
            this.Param = param;

            // DaemonSettings を読み込む
            this.SettingsHive = new HiveData<DaemonSettings>(Hive.SharedLocalConfigHive, $"DaemonSettings/{daemon.Name}", () => this.DefaultDaemonSettings, HiveSyncPolicy.None);

            // 現在の Daemon 設定をグローバル変数に適用する
            GlobalDaemonStateManager.SetCurrentDaemonSettings(this.Settings);

            if (this.Settings.DaemonPauseFlag == PauseFlag.Pause)
            {
                // 一時停止状態の場合は「何もしない Daemon」を代わりにロードする
                daemon = new PausingDaemon(new DaemonOptions(daemon.Name, daemon.Options.FriendlyName, false));
            }

            this.Daemon = daemon;
        }

        Once StartedOnce;

        // テスト動作させる
        public void TestRun(bool stopDebugHost = false, string? appId = null, bool aspNetEnableRazorRuntimeCompilation = false)
        {
            if (StartedOnce.IsFirstCall() == false) throw new ApplicationException("DaemonHost is already started.");

            if (stopDebugHost && appId._IsFilled())
            {
                try
                {
                    DebugHostUtil.Stop(appId);
                }
                catch (Exception ex)
                {
                    ex._Debug();
                }
            }

            GlobalDaemonStateManager.EnableAspNetEnableRazorRuntimeCompilation = aspNetEnableRazorRuntimeCompilation;

            TelnetLocalLogWatcher? telnetWatcher = null;

            // Start the TelnetLogWatcher
            List<IPEndPoint> telnetWatcherEpList = new List<IPEndPoint>();

            int localLogWatchPort = 50000 + Util.RandSInt31() % 10000;
            telnetWatcherEpList.Add(new IPEndPoint(IPAddress.Loopback, localLogWatchPort));
            telnetWatcherEpList.Add(new IPEndPoint(IPAddress.IPv6Loopback, localLogWatchPort));

            if (this.Settings.DaemonTelnetLogWatcherPort != 0 && CoresLib.Options.NoTelnetMode == false)
            {
                telnetWatcherEpList.Add(new IPEndPoint(IPAddress.Any, this.Settings.DaemonTelnetLogWatcherPort));
                telnetWatcherEpList.Add(new IPEndPoint(IPAddress.IPv6Any, this.Settings.DaemonTelnetLogWatcherPort));
            }

            telnetWatcher = new TelnetLocalLogWatcher(new TelnetStreamWatcherOptions((ip) => ip._GetIPAddressType().BitAny(IPAddressType.LocalUnicast | IPAddressType.Loopback), null, telnetWatcherEpList.ToArray()));

            try
            {
                // LogClient を起動する
                using (IDisposable logClient = StartLogClientInstallerIfEnabled())
                using (IDisposable syslogClient = StartSyslogClientInstallerIfEnabled())
                {
#if (CORES_BASIC_WEBAPP || CORES_BASIC_HTTPSERVER)
                    using (DaemonUtil util = new DaemonUtil(this.Daemon.Name))
#endif // (CORES_BASIC_WEBAPP || CORES_BASIC_HTTPSERVER)
                    {
                        this.Daemon.Start(DaemonStartupMode.ForegroundTestMode, this.Param);

                        if (CoresLib.Options.NohupMode == false)
                        {
                            // 通常モード
                            Con.ReadLine($"[ Press Enter key to stop the {this.Daemon.Name} daemon ]\n");
                        }
                        else
                        {
                            Con.WriteLine("Running forever...");
                            // NoHup モード
                            Sleep(Timeout.Infinite);
                        }

                        this.Daemon.Stop(false);
                    }
                }
            }
            finally
            {
                telnetWatcher._DisposeSafe();
            }
        }


        DaemonMode Mode;

        IService CreateService(DaemonMode mode)
        {
            IService service;

            if (this.Mode != DaemonMode.WindowsServiceMode)
            {
                // Usermode
                service = new UserModeService(
                    this.Daemon.Name,
                    () => this.Daemon.Start(DaemonStartupMode.BackgroundServiceMode, this.Param),
                    () => this.Daemon.Stop(true),
                     CoresLib.Options.NoTelnetMode == false ? this.Settings.DaemonTelnetLogWatcherPort : 0);
            }
            else
            {
                // Windows service mode
                service = new WindowsService(
                    this.Daemon.Name,
                    () => this.Daemon.Start(DaemonStartupMode.BackgroundServiceMode, this.Param),
                    () => this.Daemon.Stop(true),
                    CoresLib.Options.NoTelnetMode == false ? this.Settings.DaemonTelnetLogWatcherPort : 0);
            }

            return service;
        }

        // 子プロセスとして起動させられた自らを Daemon としてサービス機能を動作させる
        public void ExecMain(DaemonMode mode)
        {
            CoresLib.RunAllEssentialLibraryHealthCheckTest(); // 各種ライブラリの動作チェック (デーモンとして動作を開始する前にテストする)

            if (Env.IsWindows == false && mode == DaemonMode.WindowsServiceMode)
                throw new ArgumentException("Env.IsWindows == false && mode == DaemonMode.WindowsServiceMode");

            if (StartedOnce.IsFirstCall() == false) throw new ApplicationException("DaemonHost is already started.");

            DaemonSettings settingsCopy = this.Settings._CloneDeep();
            settingsCopy.DaemonSecret = Consts.Strings.HidePassword;
            settingsCopy.DaemonZipEncryptPassword = Consts.Strings.HidePassword;

            lock (Con.ConsoleWriteLock)
            {
                Console.WriteLine($"DaemonHost: Parameters: {settingsCopy._ObjectToRuntimeJsonStr()}");
            }

            this.Mode = mode;

            IService service = CreateService(mode);

            CurrentRunningService = service;

            // LogClient を起動する
            using (IDisposable logClient = StartLogClientInstallerIfEnabled())
            using (IDisposable syslogClient = StartSyslogClientInstallerIfEnabled())
            {
                // DaemonUtil クラスを起動する
#if (CORES_BASIC_WEBAPP || CORES_BASIC_HTTPSERVER)
                using (DaemonUtil util = new DaemonUtil(this.Daemon.Name))
#endif // (CORES_BASIC_WEBAPP || CORES_BASIC_HTTPSERVER)
                {
                    // DaemonCenter クライアントを起動する (有効な場合)
                    using (IDisposable client = StartDaemonCenterClientIfEnabled())
                    {
                        // サービス本体処理を実施する
                        service.ExecMain();
                    }
                }
            }
        }

        // 子プロセスとして稼働している Daemon プロセスの動作を停止させる
        public void StopService(DaemonMode mode)
        {
            IService service = CreateService(mode);

            Con.WriteLine($"Stopping the daemon {Daemon.ToString()} ...");

            service.StopService(Daemon.Options.StopTimeout);

            Con.WriteLine();
            Con.WriteLine($"The daemon {Daemon.ToString()} is stopped successfully.");
        }

        // 子プロセスとして稼働している Daemon プロセスに接続して現在の状態を表示する
        public void Show(DaemonMode mode)
        {
            IService service = CreateService(mode);

            service.Show();
        }

        // DaemonCenter クライアントを起動する (有効な場合)
        IDisposable StartDaemonCenterClientIfEnabled()
        {
            if (IsDaemonCenterEnabled() == false)
            {
                Dbg.Where("IsDaemonCenterEnabled() == false");
                return new EmptyDisposable();
            }

            TcpIpHostDataJsonSafe hostData = new TcpIpHostDataJsonSafe(getThisHostInfo: EnsureSpecial.Yes, true);
            
            ClientSettings cs = new ClientSettings
            {
                AppId = Settings.DaemonCenterAppId,
                DaemonName = this.Daemon.Name,
                HostGuid = Settings.DaemonCenterInstanceGuid,
                HostName = hostData.FqdnHostName,
                ServerUrl = Settings.DaemonCenterRpcUrl,
                ServerCertSha = Settings.DaemonCenterCertSha,
                UseProxy = Settings.DaemonCenterUseProxy,
            };

            ClientVariables vars = new ClientVariables
            {
                CurrentCommitId = Dbg.GetCurrentGitCommitId(),
                CurrentCommitInfo = Dbg.GetCurrentGitCommitInfo(),
                StatFlag = StatFlag.OnGit,
                CurrentInstanceArguments = Settings.DaemonStartupArgument,
                PauseFlag = Settings.DaemonPauseFlag,
            };
            
            Client cli = new Client(cs, vars, DaemonCenterRestartRequestedCallback);

            Dbg.Where("StartDaemonCenterClientIfEnabled() completed.");

            return cli;
        }

        // SyslogClient を起動する (有効な場合)
        IDisposable StartSyslogClientInstallerIfEnabled()
        {
            if (IsSyslogEnabled() == false) return new EmptyDisposable();

            SyslogClientInstaller installer = new SyslogClientInstaller(new SyslogClientOptions(null, Settings.SyslogServerHost, Settings.SyslogServerPort),
                this.Daemon.Name,
                Settings.SyslogServerFilter,
                LogPriority.Debug.ParseAsDefault(Settings.LogServerMinimalPriority));

            return installer;
        }


        // LogClient を起動する (有効な場合)
        IDisposable StartLogClientInstallerIfEnabled()
        {
            if (IsLogServerEnabled() == false) return new EmptyDisposable();

            PalSslClientAuthenticationOptions cliSsl = new PalSslClientAuthenticationOptions(false, null, Settings.LogServerCertSha);

            LogClientInstaller installer = new LogClientInstaller(new LogClientOptions(null, cliSsl, Settings.LogServerHost, Settings.LogServerPort),
                this.Daemon.Name,
                Settings.LogServerFilter,
                LogPriority.Debug.ParseAsDefault(Settings.LogServerMinimalPriority));

            return installer;
        }


        Once RebootRequestedOnce;

        // DaemonCenter サーバーによって何らかの原因で再起動要求が送付されてきたので指示に従って再起動を実施する
        void DaemonCenterRestartRequestedCallback(ResponseMsg res)
        {
            if (RebootRequestedOnce.IsFirstCall() == false) return;

            Con.WriteInfo($"The DaemonCenter Server requested rebooting.\r\nMessage = '{res._ObjectToRuntimeJsonStr()}'");

            // 次回起動引数が指定されている場合は設定ファイルを更新する
            if (res.NextInstanceArguments._IsFilled())
            {
                this.SettingsHive.AccessData(true, data =>
                {
                    data.DaemonStartupArgument = res.NextInstanceArguments;
                });
            }

            // Pause Flag が変更されている場合は設定ファイルを更新する
            if (res.NextPauseFlag != PauseFlag.None)
            {
                this.SettingsHive.AccessData(true, data =>
                {
                    data.DaemonPauseFlag = res.NextPauseFlag;
                });
            }

            // Daemon の正常停止を試行する
            Con.WriteInfo($"Shutting down the daemon service normally...");

            ThreadObj stopThread = new ThreadObj((obj) =>
            {
                try
                {
                    (this.CurrentRunningService as UserModeService)?.InternalStop();

                    Con.WriteInfo("Stopping the daemon service completed.");
                }
                catch (Exception ex)
                {
                    Con.WriteError($"Stopping the daemon service caused an exception: {ex.ToString()}");
                }
            });

            if (stopThread.WaitForEnd(CoresConfig.Timeouts.DaemonCenterRebootRequestTimeout) == false)
            {
                // タイムアウトが発生した
                Con.WriteError("Stopping the daemon service caused timed out.");
            }

            // ローカルログを Flush する
            try
            {
                Con.WriteInfo($"Calling LocalLogRouter.FlushAsync() ...");
                if (LocalLogRouter.FlushAsync().Wait(CoresConfig.Timeouts.DaemonStopLogFinish))
                {
                    Con.WriteInfo($"LocalLogRouter.FlushAsync() completed.");
                }
                else
                {
                    Con.WriteError($"LocalLogRouter.FlushAsync() timed out.");
                }
            }
            catch (Exception ex)
            {
                Con.WriteError($"LocalLogRouter.FlushAsync() caused an exception: {ex.ToString()}");
            }

            Thread.Sleep(300);

            if (Str.TryNormalizeGitCommitId(res.NextCommitId, out string commitId) == false || res.NextCommitId._IsEmpty())
            {
                // Git Commit ID に変化がない場合:
                // 単にプロセスを異常終了させる。親プロセスがこれに気付いてプロセスを再起動するはずである
                Environment.Exit(Consts.ExitCodes.DaemonCenterRebootRequestd_Normal);
            }
            else
            {
                // 以下のログは、ログファイルへの保存が不能であるため
                // 代わりに Log/DaemonUpdate/YYYYMMDD_HH.log に保存する
                string logFileName = Path.Combine(CoresConfig.LocalLogRouterSettings.LogDaemonUpdateDir.Value(), DateTime.Now.ToString("yyyyMMdd_HH") + ".log");

                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(logFileName)!);
                }
                catch { }

                StreamWriter log;

                try
                {
                    log = new StreamWriter(logFileName, true, Str.Utf8Encoding);
                    log.AutoFlush = true;
                }
                catch
                {
                    log = StreamWriter.Null;
                }

                log.WriteLine("----------------------------------------");
                log.WriteLine(DateTimeOffset.Now._ToDtStr());
                log.WriteLine("DaemonCenter: DaemonCenterRestartRequestedCallback() Update Routine");

                // Git Commit ID に変化がある場合
                log.WriteLine($"DaemonCenter Client: NextCommitId = '{commitId}'");

                // git のローカルリポジトリの update を試みる
                // Prepare update_daemon_git.sh
                string body = CoresRes["CoresInternal/190816_update_daemon_git_sh.txt"].String._NormalizeCrlf(CrlfStyle.Lf);
                string fn = Env.AppLocalDir._CombinePath("daemon_helper", "update_daemon_git.sh");
                Lfs.WriteStringToFile(fn, body, FileFlags.AutoCreateDirectory);

                ProcessStartInfo info = new ProcessStartInfo()
                {
                    FileName = "bash",
                    Arguments = $"\"{fn}\" {commitId}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    RedirectStandardInput = false,
                    CreateNoWindow = true,
                    WorkingDirectory = Env.AppRootDir,
                };

                log.WriteLine($"DaemonCenter Client: Trying to execute {info.FileName} {info.Arguments} ...");
                try
                {
                    using (Process p = Process.Start(info)!)
                    {
                        string err1 = p.StandardError.ReadToEnd();
                        string err2 = p.StandardError.ReadToEnd();
                        p.WaitForExit(CoresConfig.Timeouts.DaemonCenterGitUpdateTimeout);
                        try
                        {
                            p.Kill();
                        }
                        catch { }

                        if (p.ExitCode == 0)
                        {
                            // Git 更新に成功した場合はプロセスを再起動する
                            log.WriteLine($"Git command '{info.Arguments}' result:\n{err1}\n{err2}");
                            log.WriteLine("DaemonCenter Client: Update completed. Rebooting...");

                            try
                            {
                                log.Flush();
                            }
                            catch { }

                            Environment.Exit(Consts.ExitCodes.DaemonCenterRebootRequestd_GitUpdated);
                        }
                        else
                        {
                            log.WriteLine($"Git command '{info.Arguments}' execution error code: {p.ExitCode}");
                            log.WriteLine($"Git command '{info.Arguments}' result:\n{err1}\n{err2}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    log.WriteLine($"Git command '{info.Arguments}' execution error: {ex.Message}");
                }

                log.WriteLine("Exiting the process with code 0...");

                try
                {
                    log.Flush();
                }
                catch { }

                // 失敗した場合はエラーコード 0 で終了する (Daemon を完全終了し、再起動させない)
                Environment.Exit(Consts.ExitCodes.NoError);
            }
        }
    }

    [Flags]
    public enum DaemonCmdType
    {
        Unknown = 0,
        Start,
        Stop,
        Test,
        TestDebug,
        TestWeb,
        Show,
        ExecMain,
        Install,
        Uninstall,

        WinStart,
        WinStop,
        WinInstall,
        WinUninstall,
        WinExecSvc,
    }

    public class DaemonCmdLineTool
    {
        public static int EntryPoint(ConsoleService c, string cmdName, string str, Daemon daemon, DaemonSettings defaultDaemonSettings)
        {
            ConsoleParam[] args =
            {
                new ConsoleParam("[command]"),
                new ConsoleParam("APPID", null, null, null, null),
            };
            ConsoleParamValueList vl = c.ParseCommandList(cmdName, str, args);

            string appId = vl["APPID"].StrValue._NonNullTrim();

            Con.WriteLine();
            Con.WriteLine($"Daemon {daemon.ToString()} control command");
            Con.WriteLine();

            string command = vl.DefaultParam.StrValue;

            if (command._IsEmpty())
            {
                Con.WriteError($"You must specify the [command] argument.\nFor details please enter \"{cmdName} /help\".");
                return 1;
            }

            DaemonCmdType cmdType = command._ParseEnum(DaemonCmdType.Unknown);

            if (cmdType == DaemonCmdType.Unknown)
            {
                Con.WriteError($"Invalid command \"{command}\".\nFor details please enter \"cmdName\" /help.");
                return 1;
            }

            Con.WriteLine($"Executing the {cmdType.ToString().ToLowerInvariant()} command.");
            Con.WriteLine();

            DaemonHost host = new DaemonHost(daemon, defaultDaemonSettings);

            switch (cmdType)
            {
                case DaemonCmdType.Install: // Linux systemd インストール
                case DaemonCmdType.Uninstall: // Linux systemd アンインストール
                    if (Env.IsLinux)
                    {
                        if (Env.IsAdmin == false)
                        {
                            Con.WriteError($"Install / Uninstall commands require the 'root' system privilege. Use sudo or become a root to execute the command.");
                            return 1;
                        }
                        else
                        {
                            if (Lfs.IsDirectoryExists(Consts.FileNames.SystemdConfigDirName) == false)
                            {
                                Con.WriteError($"Install / Uninstall commands require the modern 'systemd' framework on the system.");
                                return 1;
                            }
                            else
                            {
                                if (cmdType == DaemonCmdType.Install)
                                {
                                    CoresLib.RunAllEssentialLibraryHealthCheckTest(); // 各種ライブラリの動作チェック
                                }

                                // 定義ファイル名
                                string defFileName = PP.Combine(Consts.FileNames.SystemdConfigDirName, $"{daemon.Name}.service");

                                // 定義ファイルの内容を生成
                                string templateBody = CoresRes["CoresInternal/190819_daemon_systemd.txt"].String._NormalizeCrlf(CrlfStyle.LocalPlatform);

                                string startCommand, stopCommand;

                                if (Env.IsHostedByDotNetProcess)
                                {
                                    // dotnet ホストプロセスを用いて実行
                                    startCommand = $"{Env.DotNetHostProcessExeName} run -c {Env.BuildConfigurationName} /cmd:{cmdName} start";
                                    stopCommand = $"{Env.DotNetHostProcessExeName} run --no-build -c {Env.BuildConfigurationName} /cmd:{cmdName} stop";
                                }
                                else
                                {
                                    // 直接ネイティブ ELF を起動
                                    startCommand = $"{Env.AppRealProcessExeFileName} /cmd:{cmdName} start";
                                    stopCommand = $"{Env.AppRealProcessExeFileName} /cmd:{cmdName} stop";
                                }

                                StringWriter envListStr = new StringWriter();

                                string[] envNamesList = { "GIT_SSH", "HTTPS_PROXY", "HTTP_PROXY", "LANG", "PATH" };

                                foreach (string envName in envNamesList)
                                {
                                    string? envValue = Environment.GetEnvironmentVariable(envName);
                                    if (envValue._IsFilled())
                                    {
                                        envValue = envValue.Trim();
                                        if (envValue._IsFilled())
                                        {
                                            if (envValue._NormalizeCrlf()._InStr("\n") == false && envValue._InStr("=") == false)
                                            {
                                                envListStr.WriteLine($"Environment={envName}={envValue}");
                                            }
                                        }
                                    }
                                }

                                templateBody = templateBody._ReplaceStrWithReplaceClass(new
                                {
                                    _EXE_ = Env.AppExecutableExeOrDllFileName,
                                    _DATE_ = DateTimeOffset.Now._ToDtStr(),
                                    _DESCRIPTION_ = daemon.Options.FriendlyName,
                                    _START_ = startCommand,
                                    _STOP_ = stopCommand,
                                    _PIDFILE_ = PP.Combine(Env.AppRootDir, $@"Local/DaemonPid/{daemon.Name}.pid"),
                                    _DIR_ = Env.AppRootDir,
                                    _ADDITIONAL_ENV_ = envListStr.ToString(),
                                    _TIMEOUTSTART_ = CoresConfig.Timeouts.DaemonSystemdStartTimeoutSecs.ToString(),
                                    _TIMEOUTSTOP_ = CoresConfig.Timeouts.DaemonSystemdStopTimeoutSecs.ToString(),
                                });

                                // すでに以前の定義ファイルが存在しているか?
                                string? currentFileBody = null;
                                try
                                {
                                    currentFileBody = Lfs.ReadStringFromFile(defFileName);
                                }
                                catch { }

                                if (currentFileBody._IsFilled() && currentFileBody._InStr(Consts.Strings.DaemonDefFileMarker) == false)
                                {
                                    Con.WriteError($"The file '{defFileName}' is already existing on the system and the file is not auto-generated by this command.");
                                    return 1;
                                }
                                else
                                {
                                    bool changed = false;

                                    if (cmdType == DaemonCmdType.Install)
                                    {
                                        // ファイル作成
                                        Lfs.WriteStringToFile(defFileName, templateBody, FileFlags.AutoCreateDirectory);

                                        Con.WriteLine($"The file '{defFileName}' is created successfully.");
                                        Con.WriteLine();
                                        Con.WriteLine($"Usage of the {daemon.Options.FriendlyName} daemon:");
                                        Con.WriteLine();
                                        Con.WriteLine($"systemctl enable {daemon.Name}   - Enable the {daemon.Options.FriendlyName} daemon on system startup.");
                                        Con.WriteLine($"systemctl disable {daemon.Name}  - Disable the {daemon.Options.FriendlyName} daemon on system startup.");
                                        Con.WriteLine($"systemctl start {daemon.Name}    - Start the {daemon.Options.FriendlyName} daemon.");
                                        Con.WriteLine($"systemctl stop {daemon.Name}     - Stop the {daemon.Options.FriendlyName} daemon.");
                                        Con.WriteLine($"systemctl restart {daemon.Name}  - Reload the {daemon.Options.FriendlyName} daemon.");
                                        Con.WriteLine($"systemctl status {daemon.Name}   - Show the current status of the {daemon.Options.FriendlyName} daemon.");
                                        Con.WriteLine();

                                        changed = true;
                                    }
                                    else
                                    {
                                        // ファイル削除
                                        if (Lfs.IsFileExists(defFileName))
                                        {
                                            Lfs.DeleteFile(defFileName);

                                            Con.WriteLine($"The file '{defFileName}' is removed successfully.");
                                            Con.WriteLine();

                                            changed = true;
                                        }
                                        else
                                        {
                                            // ファイルがない
                                            Con.WriteLine($"The file '{defFileName}' doesn't exist on the system.");
                                            Con.WriteLine();
                                        }
                                    }

                                    if (changed)
                                    {
                                        // 定義ファイルが作成または削除されたので systemd をリロードする
                                        ProcessStartInfo info = new ProcessStartInfo()
                                        {
                                            FileName = "systemctl",
                                            Arguments = "daemon-reload",
                                            UseShellExecute = false,
                                            RedirectStandardOutput = false,
                                            RedirectStandardError = false,
                                            RedirectStandardInput = false,
                                            CreateNoWindow = true,
                                            WorkingDirectory = Env.AppRootDir,
                                        };

                                        using (Process p = Process.Start(info)!)
                                        {
                                            Con.WriteLine("Reloading the systemd framework...");
                                            p.WaitForExit();
                                            Con.WriteLine("Done.");
                                            Con.WriteLine();
                                            Con.WriteLine($"Have a nice day! Today is {DateTime.Now._ToDtStr(option: DtStrOption.DateOnly)}.");
                                            Con.WriteLine();
                                        }
                                    }

                                    return 0;
                                }
                            }
                        }
                    }
                    else
                    {
                        Con.WriteError($"Install / Uninstall commands are available only on Linux systems.");
                        return 1;
                    }

                case DaemonCmdType.Start: // Daemon を開始する (子プロセスを起動する)
                    if (daemon.IsInstanceRunning())
                    {
                        Con.WriteError($"The {daemon.ToString()} is already running.");
                        return 1;
                    }
                    else
                    {
                        string exe;
                        string arguments;

                        if (Env.IsUnix)
                        {
                            CoresLib.RunAllEssentialLibraryHealthCheckTest(); // 各種ライブラリの動作チェック

                            // UNIX の場合はシェルスクリプトを起動する
                            exe = "nohup";
                            arguments = (Env.IsHostedByDotNetProcess ? Env.DotNetHostProcessExeName : $"\"{Env.AppRealProcessExeFileName}\"") + " " + (Env.IsHostedByDotNetProcess ? $"exec \"{Env.AppExecutableExeOrDllFileName}\" /cmd:{cmdName} {DaemonCmdType.ExecMain}" : $"/cmd:{cmdName} {DaemonCmdType.ExecMain}");

                            // Prepare run_daemon.sh
                            string body = CoresRes["CoresInternal/190714_run_daemon_sh.txt"].String._NormalizeCrlf(CrlfStyle.Lf);
                            string fn = Env.AppLocalDir._CombinePath("daemon_helper", "run_daemon.sh");
                            Lfs.WriteStringToFile(fn, body, FileFlags.AutoCreateDirectory);

                            arguments = $"bash \"{fn}\" \"{arguments}\"";
                        }
                        else
                        {
                            // Windows の場合は普通にプロセスを起動する
                            exe = (Env.IsHostedByDotNetProcess ? Env.DotNetHostProcessExeName : $"\"{Env.AppRealProcessExeFileName}\"");
                            arguments = (Env.IsHostedByDotNetProcess ? $"exec \"{Env.AppExecutableExeOrDllFileName}\" /cmd:{cmdName} {DaemonCmdType.ExecMain}" : $"/cmd:{cmdName} {DaemonCmdType.ExecMain}");
                        }

                        ProcessStartInfo info = new ProcessStartInfo()
                        {
                            FileName = exe,
                            Arguments = arguments,
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            RedirectStandardInput = false,
                            CreateNoWindow = true,
                            WorkingDirectory = Env.AppRootDir,
                        };

                        info.EnvironmentVariables.Add("IPA_DN_CORES_DOTNET_EXE", Env.DotNetHostProcessExeName);
                        info.EnvironmentVariables.Add("IPA_DN_CORES_BUILD_CONFIGURATION", Env.BuildConfigurationName);

                        try
                        {
                            using (Process p = Process.Start(info)!)
                            {
                                CancellationTokenSource cts = new CancellationTokenSource();

                                StringWriter stdOut = new StringWriter();
                                StringWriter stdErr = new StringWriter();

                                Task outputReaderTask = TaskUtil.StartAsyncTaskAsync(async () =>
                                {
                                    while (true)
                                    {
                                        cts.Token.ThrowIfCancellationRequested();

                                        string? line = await p.StandardOutput.ReadLineAsync();
                                        if (line == null)
                                            throw new ApplicationException("StandardOutput is disconnected.");

                                        stdOut.WriteLine(line);

                                        if (line._InStr(UserModeService.ExecMainSignature, false))
                                        {
                                            return;
                                        }
                                    }
                                });

                                Task errorReaderTask = TaskUtil.StartAsyncTaskAsync(async () =>
                                {
                                    while (true)
                                    {
                                        cts.Token.ThrowIfCancellationRequested();

                                        string? line = await p.StandardError.ReadLineAsync();

                                        if (line == null)
                                            throw new ApplicationException("StandardError is disconnected.");

                                        stdErr.WriteLine(line);
                                    }
                                }, leakCheck: false);

                                var result = TaskUtil.WaitObjectsAsync(new Task[] { outputReaderTask, errorReaderTask }, timeout: CoresConfig.Timeouts.DaemonStartExecTimeout)._GetResult();

                                cts.Cancel();

                                bool isError = false;

                                if (result == ExceptionWhen.TimeoutException)
                                {
                                    // Error
                                    Con.WriteError($"Failed to start the {daemon.ToString()}. Child process timed out.");
                                    isError = true;
                                }
                                else if (result == ExceptionWhen.TaskException)
                                {
                                    // Error
                                    Con.WriteError($"Failed to start the {daemon.ToString()}. Error occured in the child process.");
                                    isError = true;
                                }

                                if (isError)
                                {
                                    Con.WriteError();
                                    Con.WriteError("--- Standard output ---");
                                    Con.WriteError(stdOut.ToString());
                                    Con.WriteError();
                                    Con.WriteError("--- Standard error ---");
                                    Con.WriteError(stdErr.ToString());
                                    Con.WriteError();

                                    // Terminate the process
                                    try
                                    {
                                        p.Kill();
                                        p.WaitForExit();
                                    }
                                    catch { }
                                }
                                else
                                {
                                    // OK
                                    Con.WriteLine($"The {daemon.ToString()} is started successfully. pid = {p.Id}");
                                }

                                outputReaderTask._TryWait(true);

                                if (isError)
                                {
                                    return 1;
                                }
                                else
                                {
                                    return 0;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Con.WriteError($"File name: '{info.FileName}'");
                            Con.WriteError($"Arguments: '{info.Arguments}'");
                            Con.WriteError(ex.Message);
                            return 1;
                        }
                    }

                case DaemonCmdType.Stop: // 子プロセスとして動作している Daemon を停止させる
                    host.StopService(DaemonMode.UserMode);
                    break;

                case DaemonCmdType.Show: // 現在起動している Daemon の状態を表示する
                    host.Show(DaemonMode.UserMode);
                    break;

                case DaemonCmdType.ExecMain: // 子プロセスとして自分自身が起動さられたので、目的とするサービス動作を開始する
                    host.ExecMain(DaemonMode.UserMode);
                    break;

                case DaemonCmdType.Test: // テストモードとして自分自身が起動させられたので、目的とするサービス動作を開始する
                    host.TestRun(false);
                    break;

                case DaemonCmdType.TestDebug: // テストモードとして自分自身が起動させられたので、目的とするサービス動作を開始する (同一 appId の他インスタンスを強制終了する)
                    host.TestRun(true, appId);
                    break;

                case DaemonCmdType.TestWeb: // テストモード ただし ASP.NET Core の AddRazorRuntimeCompilation を有効にする 
                    host.TestRun(false, aspNetEnableRazorRuntimeCompilation: true);
                    break;

                case DaemonCmdType.WinExecSvc: // Windows サービスプロセスとして自分自身が起動させられたので、目的とするサービス動作を開始する
                    if (Env.IsWindows == false) throw new PlatformNotSupportedException();
                    host.ExecMain(DaemonMode.WindowsServiceMode);
                    break;

                case DaemonCmdType.WinInstall: // Windows サービスプロセスとして自分自身をレジストリにインストールする
                    {
                        if (Env.IsWindows == false) throw new PlatformNotSupportedException();

                        CoresLib.RunAllEssentialLibraryHealthCheckTest(); // 各種ライブラリの動作チェック

                        string exe;
                        string arguments;

                        exe = (Env.IsHostedByDotNetProcess ? Env.DotNetHostProcessExeName : $"{Env.AppRealProcessExeFileName}");
                        arguments = (Env.IsHostedByDotNetProcess ? $"exec \"{Env.AppExecutableExeOrDllFileName}\" /cmd:{cmdName} {DaemonCmdType.WinExecSvc}" : $"/cmd:{cmdName} {DaemonCmdType.WinExecSvc}");

                        string path = $"\"{exe}\" {arguments}";

                        if (Win32ApiUtil.IsServiceInstalled(daemon.Options.Name))
                        {
                            Con.WriteError($"The Windows service {daemon.ToString()} has already been installed.");
                            return 1;
                        }

                        Con.WriteLine($"Installing the Windows service {daemon.ToString()} ...");

                        Win32ApiUtil.InstallService(daemon.Options.Name, daemon.Options.FriendlyName, daemon.Options.FriendlyName, path);

                        Con.WriteLine($"The Windows service {daemon.ToString()} is successfully installed.");

                        Con.WriteLine();

                        Con.WriteLine($"Starting the Windows service {daemon.ToString()} ...");

                        Win32ApiUtil.StartService(daemon.Options.Name);

                        Con.WriteLine($"The Windows service {daemon.ToString()} is successfully started.");

                        return 0;
                    }

                case DaemonCmdType.WinUninstall: // Windows サービスプロセスをアンインストールする
                    if (Env.IsWindows == false) throw new PlatformNotSupportedException();

                    if (Win32ApiUtil.IsServiceInstalled(daemon.Options.Name) == false)
                    {
                        Con.WriteError($"The Windows service {daemon.ToString()} is not installed.");
                        return 1;
                    }

                    if (Win32ApiUtil.IsServiceRunning(daemon.Options.Name))
                    {
                        Con.WriteLine($"Stopping the Windows service {daemon.ToString()} ...");

                        Win32ApiUtil.StopService(daemon.Options.Name);

                        Con.WriteLine($"The Windows service {daemon.ToString()} is successfully stopped.");

                        Con.WriteLine();
                    }

                    Con.WriteLine($"Uninstalling the Windows service {daemon.ToString()} ...");

                    Win32ApiUtil.UninstallService(daemon.Options.Name);

                    Con.WriteLine($"The Windows service {daemon.ToString()} is successfully uninstalled.");

                    return 0;

                case DaemonCmdType.WinStart: // Windows サービスプロセスを起動する

                    if (Env.IsWindows == false) throw new PlatformNotSupportedException();

                    if (Win32ApiUtil.IsServiceInstalled(daemon.Options.Name) == false)
                    {
                        Con.WriteError($"The Windows service {daemon.ToString()} is not installed.");
                        return 1;
                    }

                    if (Win32ApiUtil.IsServiceRunning(daemon.Options.Name))
                    {
                        Con.WriteError($"The Windows service {daemon.ToString()} is already running");
                        return 1;
                    }

                    Con.WriteLine($"Starting the Windows service {daemon.ToString()} ...");

                    Win32ApiUtil.StartService(daemon.Options.Name);

                    Con.WriteLine($"The Windows service {daemon.ToString()} is successfully started.");

                    return 0;

                case DaemonCmdType.WinStop: // Windows サービスプロセスを終了する

                    if (Env.IsWindows == false) throw new PlatformNotSupportedException();

                    if (Win32ApiUtil.IsServiceInstalled(daemon.Options.Name) == false)
                    {
                        Con.WriteError($"The Windows service {daemon.ToString()} is not installed.");
                        return 1;
                    }

                    if (Win32ApiUtil.IsServiceRunning(daemon.Options.Name) == false)
                    {
                        Con.WriteError($"The Windows service {daemon.ToString()} is not started.");
                        return 1;
                    }

                    Con.WriteLine($"Stopping the Windows service {daemon.ToString()} ...");

                    Win32ApiUtil.StopService(daemon.Options.Name);

                    Con.WriteLine($"The Windows service {daemon.ToString()} is successfully stopped.");

                    return 0;
            }

            return 0;
        }
    }
}

#endif // CORES_BASIC_DAEMON
