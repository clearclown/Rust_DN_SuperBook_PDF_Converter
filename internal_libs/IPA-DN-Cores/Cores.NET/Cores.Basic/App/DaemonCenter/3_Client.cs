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

#if CORES_BASIC_JSON && CORES_BASIC_DAEMON

#pragma warning disable CA2235 // Mark all non-serializable fields

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

using IPA.Cores.Basic.App.DaemonCenterLib;
using System.Net;

namespace IPA.Cores.Basic
{
    public static partial class CoresConfig
    {
        public static partial class DaemonCenterLibSettings
        {
            public static readonly Copenhagen<int> RetryIntervalMsecsStandard = 1 * 1000;
            public static readonly Copenhagen<int> RetryIntervalMsecsMax = 30 * 1000;
        }
    }
}

namespace IPA.Cores.Basic.App.DaemonCenterLib
{
    // 固定的な設定
    [Serializable]
    public class ClientSettings : IValidatable
    {
        public string? ServerUrl;
        public string? ServerCertSha;
        public bool UseProxy;

        public string? AppId;
        public string? HostName;
        public string? HostGuid;
        public string? DaemonName;

        public void Validate()
        {
            if (ServerUrl._IsEmpty()) throw new ArgumentNullException(nameof(ServerUrl));
            if (AppId._IsEmpty()) throw new ArgumentNullException(nameof(AppId));
            if (HostName._IsEmpty()) throw new ArgumentNullException(nameof(HostName));
            if (HostGuid._IsEmpty()) throw new ArgumentNullException(nameof(HostGuid));
            if (DaemonName._IsEmpty()) throw new ArgumentNullException(nameof(DaemonName));
        }
    }

    // 値がその都度コロコロ変わる変数データ
    [Serializable]
    public class ClientVariables
    {
        public string? CurrentCommitId;
        public DbgGitCommitInfo? CurrentCommitInfo;
        public string? CurrentInstanceArguments;
        public StatFlag StatFlag;
        public PauseFlag PauseFlag;
    }

    public delegate void RestartCallback(ResponseMsg res);

    // クライアント
    public class Client : AsyncServiceWithMainLoop
    {
        // 固定的な設定
        readonly ClientSettings Settings;

        // 起動時の可変データ
        readonly ClientVariables Variables;

        readonly JsonRpcHttpClient RpcClient;

        readonly IRpc Rpc;

        readonly RestartCallback RestartCb;

        readonly OneLineParams StartupParams;

        readonly bool Report_ForceGc;

        public Client(ClientSettings settings, ClientVariables variables, RestartCallback restartCb, WebApiOptions? webOptions = null, CancellationToken cancel = default) : base(cancel)
        {
            settings.Validate();

            // 設定とデータをコピーする
            this.Settings = settings._CloneDeep();
            this.Variables = variables._CloneDeep();

            this.StartupParams = new OneLineParams(variables.CurrentInstanceArguments._NonNull());

            this.Report_ForceGc = this.StartupParams._HasKey(Consts.DaemonArgKeys.ForceGc);

            // Web オプションを設定する
            if (webOptions == null) webOptions = new WebApiOptions();

            if (settings.ServerCertSha._IsEmpty())
            {
                webOptions.Settings.SslAcceptAnyCerts = true;
            }
            else
            {
                webOptions.Settings.SslAcceptCertSHAHashList.Clear();
                webOptions.Settings.SslAcceptCertSHAHashList.Add(settings.ServerCertSha);
                webOptions.Settings.SslAcceptAnyCerts = false;
            }

            // Proxy の利用設定
            webOptions.Settings.UseProxy = settings.UseProxy;

            // RPC Client を作成する
            this.RpcClient = new JsonRpcHttpClient(this.Settings.ServerUrl._NullCheck(), webOptions);

            // RPC インターフェイスを作成する
            this.Rpc = this.RpcClient.GenerateRpcInterface<IRpc>();

            this.RestartCb = restartCb;

            // メインループを開始する
            this.StartMainLoop(MainLoopAsync);
        }

        // メインループ
        async Task MainLoopAsync(CancellationToken cancel)
        {
            int numRetry = 0;

            int numOk = 0;

            int intervalCache = 0;

            string lastError = "";

            bool firstConnected = false;

            Con.WriteInfo($"DaemonCenterClient: Trying to connect to the DaemonCenter Server '{this.Settings.ServerUrl}' ...");

            while (this.GrandCancel.IsCancellationRequested == false)
            {
                int nextInterval;

                try
                {
                    nextInterval = await PerformOnceAsync(cancel);

                    if (firstConnected == false)
                    {
                        firstConnected = true;

                        Con.WriteInfo($"DaemonCenterClient: Connected OK.");
                    }

                    if (nextInterval == -1)
                    {
                        // 再起動が要求されたら、ここに飛ぶ。
                        // メインループを直ちに終了する。
                        break;
                    }
                    else
                    {
                        intervalCache = nextInterval;

                        nextInterval = Util.GenRandInterval(nextInterval);
                    }

                    numRetry = 0;

                    numOk++;
                    if (numOk == 1)
                    {
                        // 初回のみ nextInterval を 100 msec にする (直ちに再試行するようにする)
                        nextInterval = 100;
                    }
                }
                catch (Exception ex)
                {
                    firstConnected = false;

                    string errStr = ex._GetSingleException().ToString();

                    if (lastError != errStr)
                    {
                        lastError = errStr;
                        Con.WriteError($"DaemonCenterClient Error: {errStr}");
                    }

                    numRetry++;

                    numOk = 0;

                    if (intervalCache == 0)
                    {
                        nextInterval = Util.GenRandIntervalWithRetry(CoresConfig.DaemonCenterLibSettings.RetryIntervalMsecsStandard, numRetry,
                            CoresConfig.DaemonCenterLibSettings.RetryIntervalMsecsMax);
                    }
                    else
                    {
                        nextInterval = Util.GenRandInterval(intervalCache);
                    }
                }

                await cancel._WaitUntilCanceledAsync(nextInterval);
            }
        }

        // 1 回の処理
        async Task<int> PerformOnceAsync(CancellationToken cancel = default)
        {
            CoresRuntimeStat runtimeStat = new CoresRuntimeStat();

            runtimeStat.Refresh(forceGc: this.Report_ForceGc);

            string[]? globalIpList = null;

            try
            {
                globalIpList = (await LocalNet.GetLocalHostPossibleIpAddressListAsync(cancel)).Select(x => x.ToString()).ToArray();
            }
            catch { }

            InstanceStat stat = new InstanceStat
            {
                DaemonName = this.Settings.DaemonName,
                CommitId = this.Variables.CurrentCommitId,
                CommitInfo = this.Variables.CurrentCommitInfo,
                InstanceArguments = this.Variables.CurrentInstanceArguments,
                RuntimeStat = runtimeStat,
                EnvInfo = new EnvInfoSnapshot(),
                StatFlag = this.Variables.StatFlag,
                TcpIpHostData = LocalNet.GetTcpIpHostDataJsonSafe(true),
                GlobalIpList = globalIpList,
                PauseFlag = Variables.PauseFlag,
                MetaStatusDictionary = GlobalDaemonStateManager.MetaStatusDictionary,
                DaemonSecret = GlobalDaemonStateManager.DaemonSecret,
            };

            // リクエストメッセージの組立て
            RequestMsg req = new RequestMsg
            {
                AppId = this.Settings.AppId,
                HostName = this.Settings.HostName,
                Guid = this.Settings.HostGuid,
                Stat = stat,
            };

            // サーバーに送付し応答を受信
            ResponseMsg res = await Rpc.KeepAliveAsync(req);

            // 応答メッセージの分析
            res.Normalize();

            // ローカル IP アドレスを覚える
            GlobalDaemonStateManager.SetDaemonClientLocalIpAddress(RpcClient.LastLocalIp);

            // FileBrowser の URL が分かればこれを DaemonCenter に送付する
            if (GlobalDaemonStateManager.FileBrowserHttpsPortNumber != 0)
            {
                IPAddress ip = GlobalDaemonStateManager.DaemonClientLocalIpAddress;
                string hostname = ip.ToString();

                if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
                {
                    // ipv6
                    hostname = $"[{hostname}]";
                }

                try
                {
                    // FQDN を DNS 解決する
                    string? fqdn = req.Stat.TcpIpHostData.FqdnHostName;

                    if (fqdn._IsFilled())
                    {
                        DnsResponse dnsReply = await LocalNet.QueryDnsAsync(new DnsGetIpQueryParam(fqdn, timeout: Consts.Timeouts.Rapid), cancel);
                        if (dnsReply.IPAddressList.Where(x => x == ip).Any())
                        {
                            // DNS 解決に成功し、同一の IP アドレスを指していることが分かったので URL には FQDN を埋め込む
                            hostname = fqdn;
                        }
                    }
                }
                catch { }

                // url
                string url = $"https://{hostname}:{GlobalDaemonStateManager.FileBrowserHttpsPortNumber}/{GlobalDaemonStateManager.DaemonSecret}/";

                GlobalDaemonStateManager.MetaStatusDictionary[Consts.DaemonMetaStatKeys.CurrentLogFileBrowserUrl] = url;
            }
            else
            {
                GlobalDaemonStateManager.MetaStatusDictionary.TryRemove(Consts.DaemonMetaStatKeys.CurrentLogFileBrowserUrl, out _);
            }

            if (res.OsRebootRequested)
            {
                // OS そのものの再起動が要求されたので再起動を行なう
                if (Env.IsAdmin)
                {
                    try
                    {
                        Kernel.RebootOperatingSystemForcefullyDangerous();
                    }
                    catch { }
                }
            }

            if (res.NextCommitId._IsFilled() || res.NextInstanceArguments._IsFilled() || res.NextPauseFlag != PauseFlag.None || res.RebootRequested)
            {
                // 再起動が要求された
                this.RestartCb(res);

                return -1;
            }

            // 次回 KeepAlive 間隔の応答
            return res.NextKeepAliveMsec;
        }

        protected override void DisposeImpl(Exception? ex)
        {
            try
            {
                this.RpcClient._DisposeSafe();
            }
            finally
            {
                base.DisposeImpl(ex);
            }
        }
    }
}

#endif

