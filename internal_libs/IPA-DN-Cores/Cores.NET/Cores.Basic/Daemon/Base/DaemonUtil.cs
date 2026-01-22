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

// Author: Daiyuu Nobori
// Daemon と共に付随して動作する管理補助的ユーティリティ

#if CORES_BASIC_DAEMON && (CORES_BASIC_WEBAPP || CORES_BASIC_HTTPSERVER)

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.ServiceProcess;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using System.Net;
using System.Runtime.Serialization;
using IPA.Cores.Basic.App.DaemonCenterLib;

namespace IPA.Cores.Basic;

// Daemon と共に付随して動作する管理補助的ユーティリティ
public class DaemonUtil : AsyncService
{
    readonly OneLineParams Params;

    List<IAsyncDisposable> DisposeList = new List<IAsyncDisposable>();

    public DaemonUtil(string daemonName, CancellationToken cancel = default) : base(cancel)
    {
        if (daemonName._IsEmpty()) throw new ArgumentNullException(nameof(daemonName));

        daemonName = daemonName._NonNullTrim();

        try
        {
            // 起動パラメータ
            this.Params = new OneLineParams(GlobalDaemonStateManager.StartupArguments);

            if (Params._HasKey(Consts.DaemonArgKeys.StartLogFileBrowser))
            {
                // Log Browser で利用されるべきポート番号の決定
                int httpPort = Params._GetFirstValueOrDefault(Consts.DaemonArgKeys.LogFileBrowserPort, StrComparer.IgnoreCaseComparer)._ToInt();
                if (httpPort == 0) httpPort = Util.GenerateDynamicListenableTcpPortWithSeed(Env.DnsFqdnHostName + "_seed_daemonutil_logbrowser_http" + Env.AppRootDir + "@" + daemonName);

                int httpsPort = Params._GetFirstValueOrDefault(Consts.DaemonArgKeys.LogFileBrowserPort, StrComparer.IgnoreCaseComparer)._ToInt();
                if (httpsPort == 0) httpsPort = Util.GenerateDynamicListenableTcpPortWithSeed(Env.DnsFqdnHostName + "_seed_daemonutil_logbrowser_https" + Env.AppRootDir + "@" + daemonName, excludePorts: httpPort._SingleArray());

                // Log Browser 用の CertVault の作成
                CertVault certVault = new CertVault(PP.Combine(Env.AppLocalDir, "Config/DaemonUtil_LogBrowser/CertVault"),
                    new CertVaultSettings(defaultSetting: EnsureSpecial.Yes) { UseAcme = false });

                DisposeList.Add(certVault);

                // Log Browser の起動
                HttpServerOptions httpServerOptions = new HttpServerOptions
                {
                    UseStaticFiles = false,
                    UseSimpleBasicAuthentication = false,
                    HttpPortsList = httpPort._SingleList(),
                    HttpsPortsList = httpsPort._SingleList(),
                    DebugKestrelToConsole = false,
                    UseKestrelWithIPACoreStack = true,
                    AutomaticRedirectToHttpsIfPossible = false,
                    LocalHostOnly = false,
                    UseGlobalCertVault = false, // Disable Global CertVault
                    DisableHiveBasedSetting = true, // Disable Hive based settings
                    ServerCertSelector = certVault.X509CertificateSelectorForHttpsServerNoAcme,
                    DenyRobots = true, // Deny robots
                };

                LogBrowserOptions browserOptions = new LogBrowserOptions(
                    Env.AppRootDir,
                    systemTitle: $"{Env.DnsFqdnHostName}",
                    zipEncryptPassword: GlobalDaemonStateManager.DaemonZipEncryptPassword,
                    clientIpAcl: (ip) =>
                    {
                            // 接続元 IP アドレスの種類を取得
                            IPAddressType type = ip._GetIPAddressType();

                        if (type.Bit(IPAddressType.GlobalIp))
                        {
                                // 接続元がグローバル IP の場合
                                if (GlobalDaemonStateManager.IsDaemonClientLocalIpAddressGlobal == false)
                            {
                                    // DaemonCenter との接続にプライベート IP を利用している場合: 接続拒否
                                    return false;
                            }
                        }

                            // それ以外の場合: 接続許可
                            return true;
                    }
                    );

                DisposeList.Add(LogBrowserHttpServerBuilder.StartServer(httpServerOptions, new LogBrowserHttpServerOptions(browserOptions, "/" + GlobalDaemonStateManager.DaemonSecret)));

                GlobalDaemonStateManager.FileBrowserHttpsPortNumber = httpsPort;
            }
        }
        catch (Exception ex)
        {
            ex._Debug();

            this._DisposeSafe();

            throw;
        }
    }

    protected override async Task CleanupImplAsync(Exception? ex)
    {
        try
        {
            await DisposeList._DoForEachAsync(async x => await x._DisposeSafeAsync());
        }
        finally
        {
            await base.CleanupImplAsync(ex);
        }
    }
}

#endif

