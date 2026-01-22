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

#if CORES_BASIC_JSON && (CORES_BASIC_WEBAPP || CORES_BASIC_HTTPSERVER) && CORES_BASIC_SECURITY

using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Linq;
using System.Text;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using System.Security.AccessControl;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;

namespace IPA.Cores.Basic;

public static partial class CoresConfig
{
    public static partial class DataVaultServerApp
    {
        public static readonly Copenhagen<string> DefaultDestDirString = @"Local/DataVault/";
        public static readonly Copenhagen<string> DefaultDataVaultServerCertVaultDirString = @"Local/DataVaultServer_CertVault/";

        public static readonly Copenhagen<string> DefaultDataVaultServerPortsString = Consts.Ports.DataVaultServerDefaultServicePort.ToString();
        public static readonly Copenhagen<string> DefaultHttpServerPortsString = Consts.Ports.DataVaultServerDefaultHttpPort.ToString();
        public static readonly Copenhagen<string> DefaultHttpsServerPortsString = Consts.Ports.DataVaultServerDefaultHttpsPort.ToString();

        public static readonly Copenhagen<int> MaxHttpPostRecvData = 10 * 1024 * 1024;
    }
}

public class DataVaultLogBrowserHttpServerOptions : LogBrowserHttpServerOptions
{
    public DataVaultServerApp App { get; }
    public DataVaultServer Vault { get; }

    public DataVaultLogBrowserHttpServerOptions(LogBrowserOptions options, string absolutePrefixPath, DataVaultServerApp app, DataVaultServer vault) : base(options, absolutePrefixPath)
    {
        this.App = app;
        this.Vault = vault;
    }
}

public class DataVaultLogBrowserHttpServerBuilder : LogBrowserHttpServerBuilder
{
    public new DataVaultLogBrowserHttpServerOptions Options => (DataVaultLogBrowserHttpServerOptions)this.Param!;

    public static new HttpServer<DataVaultLogBrowserHttpServerBuilder> StartServer(HttpServerOptions httpCfg, LogBrowserHttpServerOptions options, CancellationToken cancel = default)
        => new HttpServer<DataVaultLogBrowserHttpServerBuilder>(httpCfg, options, cancel);

    public DataVaultLogBrowserHttpServerBuilder(IConfiguration configuration) : base(configuration)
    {
    }

    protected override void ConfigureImpl_BeforeHelper(HttpServerStartupConfig cfg, IApplicationBuilder app, IWebHostEnvironment env, IHostApplicationLifetime lifetime)
    {
        RouteBuilder rb = new RouteBuilder(app);

        rb._MapPostStandardHandler("/stat/", PostHandlerAsync, this.CancelToken);

        IRouter router = rb.Build();
        app.UseRouter(router);

        base.ConfigureImpl_BeforeHelper(cfg, app, env, lifetime);
    }

    public async Task<HttpResult> PostHandlerAsync(WebMethods method, string path, QueryStringList queryString, HttpContext context, RouteData routeData, IPEndPoint local, IPEndPoint remote, CancellationToken cancel = default)
    {
        var request = context.Request;

        string str = await request._RecvStringContentsAsync(CoresConfig.DataVaultServerApp.MaxHttpPostRecvData, cancel: cancel);


        DataVaultData? recv = str._JsonToObject<DataVaultData>();

        List<DataVaultData> list = new List<DataVaultData>();

        if (recv != null)
        {
            recv.NormalizeReceivedData();

            recv.StatGitCommitId = recv.StatGitCommitId._NonNullTrim();

            recv.StatAppVer = recv.StatAppVer._NonNullTrim();

            recv.TimeStamp = DtOffsetNow;

            recv.StatGlobalIp = remote.Address.ToString();
            recv.StatGlobalPort = remote.Port;

            recv.StatGlobalFqdn = await LocalNet.GetHostNameSingleOrIpAsync(recv.StatGlobalIp, cancel);

            recv.StatLocalIp = recv.StatLocalIp._NonNullTrim();
            if (recv.StatLocalIp._IsEmpty()) recv.StatLocalIp = "127.0.0.1";

            recv.StatLocalFqdn = recv.StatLocalFqdn._NonNullTrim();
            if (recv.StatLocalFqdn._IsEmpty()) recv.StatLocalFqdn = "localhost";

            recv.StatUid = recv.StatUid._NonNullTrim();

            if (recv.SystemName._IsFilled() && recv.LogName._IsFilled())
            {
                // キー無し 1 つのディレクトリに全部書き込み
                try
                {
                    DataVaultData d = recv._CloneIfClonable();
                    d.KeyType = "all";
                    d.KeyShortValue = "all";
                    d.KeyFullValue = "all";

                    list.Add(d);
                }
                catch (Exception ex)
                {
                    ex._Debug();
                }

                // UID からキーを生成
                try
                {
                    DataVaultData d = recv._CloneIfClonable();
                    d.KeyType = "by_uid";
                    d.KeyShortValue = recv.StatUid._TruncStr(2);
                    d.KeyFullValue = recv.StatUid._TruncStr(4);

                    list.Add(d);
                }
                catch (Exception ex)
                {
                    ex._Debug();
                }

                // グローバル IP からキーを生成
                try
                {
                    DataVaultData d = recv._CloneIfClonable();
                    d.KeyType = "by_global_ip";
                    d.KeyShortValue = IPUtil.GetHead1BytesIPString(recv.StatGlobalIp);
                    d.KeyFullValue = IPUtil.GetHead2BytesIPString(recv.StatGlobalIp);

                    list.Add(d);
                }
                catch (Exception ex)
                {
                    ex._Debug();
                }

                // グローバル FQDN からキーを生成
                try
                {
                    string shortKey, longKey;

                    if (IPUtil.IsStrIP(recv.StatGlobalFqdn) == false)
                    {
                        // FQDN
                        if (MasterData.DomainSuffixList.ParseDomainBySuffixList(recv.StatGlobalFqdn, out string tld, out string domain, out string hostname))
                        {
                            // 正しい TLD 配下のドメイン
                            // 例: 12345.abc.example.org の場合
                            //     Short key は org.example.ab
                            //     Long key は  org.example.abc.1
                            string domainReverse = domain._Split(StringSplitOptions.RemoveEmptyEntries, '.').Reverse()._Combine(".");
                            string hostnameReverse = hostname._Split(StringSplitOptions.RemoveEmptyEntries, '.').Reverse()._Combine(".");

                            shortKey = new string[] { domainReverse, hostnameReverse._TruncStr(2) }._Combine(".");
                            longKey = new string[] { domainReverse, hostnameReverse._TruncStr(5) }._Combine(".");
                        }
                        else
                        {
                            // おかしなドメイン
                            shortKey = recv.StatGlobalFqdn._TruncStr(2);
                            longKey = recv.StatGlobalFqdn._TruncStr(4);
                        }
                    }
                    else
                    {
                        // IP アドレス
                        shortKey = IPUtil.GetHead1BytesIPString(recv.StatGlobalIp);
                        longKey = IPUtil.GetHead1BytesIPString(recv.StatGlobalIp);
                    }

                    DataVaultData d = recv._CloneIfClonable();
                    d.KeyType = "by_global_fqdn";
                    d.KeyShortValue = shortKey;
                    d.KeyFullValue = longKey;

                    list.Add(d);
                }
                catch (Exception ex)
                {
                    ex._Debug();
                }

                // ローカル IP からキーを生成
                try
                {
                    DataVaultData d = recv._CloneIfClonable();
                    d.KeyType = "by_local_ip";
                    d.KeyShortValue = IPUtil.GetHead1BytesIPString(recv.StatLocalIp);
                    d.KeyFullValue = IPUtil.GetHead2BytesIPString(recv.StatLocalIp);

                    list.Add(d);
                }
                catch (Exception ex)
                {
                    ex._Debug();
                }

                // グローバル FQDN からキーを生成
                try
                {
                    string shortKey, longKey;

                    if (IPUtil.IsStrIP(recv.StatLocalFqdn) == false)
                    {
                        // FQDN
                        if (MasterData.DomainSuffixList.ParseDomainBySuffixList(recv.StatLocalFqdn, out string tld, out string domain, out string hostname))
                        {
                            // 正しい TLD 配下のドメイン
                            // 例: 12345.abc.example.org の場合
                            //     Short key は org.example.ab
                            //     Long key は  org.example.abc.1
                            string domainReverse = domain._Split(StringSplitOptions.RemoveEmptyEntries, '.').Reverse()._Combine(".");
                            string hostnameReverse = hostname._Split(StringSplitOptions.RemoveEmptyEntries, '.').Reverse()._Combine(".");

                            shortKey = new string[] { domainReverse, hostnameReverse._TruncStr(2) }._Combine(".");
                            longKey = new string[] { domainReverse, hostnameReverse._TruncStr(5) }._Combine(".");
                        }
                        else
                        {
                            // おかしなドメイン
                            shortKey = recv.StatLocalFqdn._TruncStr(2);
                            longKey = recv.StatLocalFqdn._TruncStr(4);
                        }
                    }
                    else
                    {
                        // IP アドレス
                        shortKey = IPUtil.GetHead1BytesIPString(recv.StatLocalIp);
                        longKey = IPUtil.GetHead1BytesIPString(recv.StatLocalIp);
                    }

                    DataVaultData d = recv._CloneIfClonable();
                    d.KeyType = "by_local_fqdn";
                    d.KeyShortValue = shortKey;
                    d.KeyFullValue = longKey;

                    list.Add(d);
                }
                catch (Exception ex)
                {
                    ex._Debug();
                }
            }
        }

        List<DataVaultServerReceivedData> list2 = new List<DataVaultServerReceivedData>();

        foreach (var d in list)
        {
            DataVaultServerReceivedData d2 = new DataVaultServerReceivedData()
            {
                BinaryData = d._ObjectToJson(compact: true)._GetBytes_UTF8(),
                JsonData = d,
            };

            list2.Add(d2);
        }

        await this.Options.Vault.DataVaultReceiveAsync(list2);

        return new HttpStringResult("ok");
    }
}

public class DataVaultServerApp : AsyncService
{
    readonly HiveData<HiveKeyValue> Settings = Hive.LocalAppSettingsEx["DataVaultServerApp"];

    DataVaultServer? DataVaultServer = null;

    CertVault? CertVault = null;

    HttpServer<DataVaultLogBrowserHttpServerBuilder>? LogBrowserHttpServer = null;

    public DataVaultServerApp()
    {
        try
        {
            this.Settings.AccessData(true, k =>
            {
                string dataDestDir = k.GetStr("DestDir", CoresConfig.DataVaultServerApp.DefaultDestDirString);
                string certVaultDir = k.GetStr("DataVaultServerCertVaultDirString", CoresConfig.DataVaultServerApp.DefaultDataVaultServerCertVaultDirString);

                string servicePortsStr = k.GetStr("DataVaultServerPorts", CoresConfig.DataVaultServerApp.DefaultDataVaultServerPortsString);

                string httpPortsStr = k.GetStr("WebServerHttpPorts", CoresConfig.DataVaultServerApp.DefaultHttpServerPortsString);
                string httpsPortsStr = k.GetStr("WebServerHttpsPorts", CoresConfig.DataVaultServerApp.DefaultHttpsServerPortsString);

                string mustIncludeHostnameStr = k.GetStr("MustIncludeHostname", "*");

                string accessKey = k.GetStr("AccessKey", Str.GenRandPassword(mustHaveOneUnderBar: false, count: 32));

                string zipEncryptPassword = k.GetStr("ZipEncryptPassword", Str.GenRandPassword(mustHaveOneUnderBar: false, count: 32));

                dataDestDir = Lfs.ConfigPathStringToPhysicalDirectoryPath(dataDestDir);
                certVaultDir = Lfs.ConfigPathStringToPhysicalDirectoryPath(certVaultDir);


                    // Start DataVault Server
                    this.CertVault = new CertVault(certVaultDir,
                    new CertVaultSettings(EnsureSpecial.Yes)
                    {
                        ReloadIntervalMsecs = 3600 * 1000,
                        UseAcme = false,
                        NonAcmeEnableAutoGenerateSubjectNameCert = false,
                    });

                Lfs.CreateDirectory(dataDestDir, FileFlags.OnCreateSetCompressionFlag);

                PalSslServerAuthenticationOptions sslOptions = new PalSslServerAuthenticationOptions(this.CertVault.X509CertificateSelector("dummy", true, EnsureOk.Ok), true, null);

                this.DataVaultServer = new DataVaultServer(new DataVaultServerOptions(null, dataDestDir,
                    FileFlags.AutoCreateDirectory | FileFlags.OnCreateSetCompressionFlag | FileFlags.LargeFs_ProhibitWriteWithCrossBorder,
                    setDestinationProc: null,
                    sslAuthOptions: sslOptions,
                    tcpIp: LocalNet,
                    ports: Str.ParsePortsList(servicePortsStr),
                    rateLimiterConfigName: "DataVaultServer",
                    accessKey: accessKey
                    ));

                    // Start HTTP Server-based Web log browser
                    HttpServerOptions httpServerOptions = new HttpServerOptions
                {
                    UseStaticFiles = false,
                    UseSimpleBasicAuthentication = true,
                    HttpPortsList = Str.ParsePortsList(httpPortsStr).ToList(),
                    HttpsPortsList = Str.ParsePortsList(httpsPortsStr).ToList(),
                    DebugKestrelToConsole = true,
                    UseKestrelWithIPACoreStack = true,
                    AutomaticRedirectToHttpsIfPossible = true,
                    LocalHostOnly = false,
                };

                if (mustIncludeHostnameStr._IsFilled() && mustIncludeHostnameStr._IsSamei("*") == false)
                {
                    string[] tokens = mustIncludeHostnameStr.Split(new char[] { ' ', '　', ';', '/', ',' }, StringSplitOptions.RemoveEmptyEntries);
                    tokens._DoForEach(x => httpServerOptions.MustIncludeHostnameStrList.Add(x));
                }

                LogBrowserOptions browserOptions = new LogBrowserOptions(dataDestDir, zipEncryptPassword: zipEncryptPassword);

                this.LogBrowserHttpServer = DataVaultLogBrowserHttpServerBuilder.StartServer(httpServerOptions, new DataVaultLogBrowserHttpServerOptions(browserOptions, "", this, this.DataVaultServer));
            });
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
            await this.LogBrowserHttpServer._DisposeSafeAsync();

            await this.DataVaultServer._DisposeSafeAsync();

            await this.CertVault._DisposeSafeAsync();
        }
        finally
        {
            await base.CleanupImplAsync(ex);
        }
    }
}

#endif
