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

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace Microsoft.Extensions.Primitives
{
    public class InplaceStringBuilder
    {
    }
}

namespace IPA.Cores.Basic
{
    public static partial class CoresConfig
    {
        public static partial class LogServerApp
        {
            public static readonly Copenhagen<string> DefaultDestDirString = @"Local/LogDest/";
            public static readonly Copenhagen<string> DefaultLogServerCertVaultDirString = @"Local/LogServer_CertVault/";

            public static readonly Copenhagen<string> DefaultLogServerPortsString = Consts.Ports.LogServerDefaultServicePort.ToString();
            public static readonly Copenhagen<string> DefaultHttpServerPortsString = Consts.Ports.LogServerDefaultHttpPort.ToString();
            public static readonly Copenhagen<string> DefaultHttpsServerPortsString = Consts.Ports.LogServerDefaultHttpsPort.ToString();
        }
    }

    public class LogServerApp : AsyncService
    {
        readonly HiveData<HiveKeyValue> Settings = Hive.LocalAppSettingsEx["LogServerApp"];

        LogServer? LogServer = null;

        CertVault? CertVault = null;

        HttpServer<LogBrowserHttpServerBuilder>? LogBrowserHttpServer = null;

        public LogServerApp()
        {
            try
            {
                this.Settings.AccessData(true, k =>
                {
                    string logDestDir = k.GetStr("DestDir", CoresConfig.LogServerApp.DefaultDestDirString);
                    string certVaultDir = k.GetStr("LogServerCertVaultDirString", CoresConfig.LogServerApp.DefaultLogServerCertVaultDirString);

                    string servicePortsStr = k.GetStr("LogServerPorts", CoresConfig.LogServerApp.DefaultLogServerPortsString);

                    string httpPortsStr = k.GetStr("WebServerHttpPorts", CoresConfig.LogServerApp.DefaultHttpServerPortsString);
                    string httpsPortsStr = k.GetStr("WebServerHttpsPorts", CoresConfig.LogServerApp.DefaultHttpsServerPortsString);

                    string mustIncludeHostnameStr = k.GetStr("MustIncludeHostname", "*");

                    string zipEncryptPassword = k.GetStr("ZipEncryptPassword", Str.GenRandPassword(mustHaveOneUnderBar: false, count: 32));

                    logDestDir = Lfs.ConfigPathStringToPhysicalDirectoryPath(logDestDir);
                    certVaultDir = Lfs.ConfigPathStringToPhysicalDirectoryPath(certVaultDir);


                    // Start Log Server
                    this.CertVault = new CertVault(certVaultDir,
                        new CertVaultSettings(EnsureSpecial.Yes)
                        {
                            ReloadIntervalMsecs = 3600 * 1000,
                            UseAcme = false,
                            NonAcmeEnableAutoGenerateSubjectNameCert = false,
                        });

                    Lfs.CreateDirectory(logDestDir, FileFlags.OnCreateSetCompressionFlag);

                    PalSslServerAuthenticationOptions sslOptions = new PalSslServerAuthenticationOptions(this.CertVault.X509CertificateSelector("dummy", true, EnsureOk.Ok), true, null);

                    this.LogServer = new LogServer(new LogServerOptions(null, logDestDir,
                        FileFlags.AutoCreateDirectory | FileFlags.OnCreateSetCompressionFlag | FileFlags.LargeFs_ProhibitWriteWithCrossBorder,
                        setDestinationProc: null,
                        sslAuthOptions: sslOptions,
                        tcpIp: LocalNet,
                        ports: Str.ParsePortsList(servicePortsStr),
                        rateLimiterConfigName: "LogServer"
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

                    LogBrowserOptions browserOptions = new LogBrowserOptions(logDestDir, zipEncryptPassword: zipEncryptPassword, regardAllLogFilesUtfs: true);

                    this.LogBrowserHttpServer = LogBrowserHttpServerBuilder.StartServer(httpServerOptions, new LogBrowserHttpServerOptions(browserOptions, ""));
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

                await this.LogServer._DisposeSafeAsync();

                await this.CertVault._DisposeSafeAsync();
            }
            finally
            {
                await base.CleanupImplAsync(ex);
            }
        }
    }
}

#endif
