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

#if true

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
using System.Security.Cryptography.X509Certificates;
using System.Net.Security;

namespace IPA.Cores.Basic;


public class LtsOpenSslTool
{
    // -- 定数部分  https://lts.dn.ipantt.net/d/211117_002_lts_openssl_exesuite_09221/ を参照のこと --
    public const string BaseUrl = "https://static.lts.dn.ipantt.net/d/211117_002_lts_openssl_exesuite_09221/";
    public const string BaseUrlCertHash = "DD6668C8F3DB6B53C593B83E9511ECFB5A9FDEFD";

    public const string VersionYymmdd = "211117";

    public const int CommandTimeoutMsecs = 5 * 1000;

    public const int RetryCount = 5;

    public const string Def_OpenSslExeNamesAndSslVers = @"
lts_openssl_exesuite_0.9.8zh                ssl3 tls1                           -nonoservername -nosni
lts_openssl_exesuite_1.0.2u                 ssl3 tls1 tls1_1 tls1_2             -nonoservername
lts_openssl_exesuite_1.1.1l                 ssl3 tls1 tls1_1 tls1_2             
lts_openssl_exesuite_3.0.0                  ssl3 tls1 tls1_1 tls1_2 tls1_3      
";

    public const string Def_Ciphers = @"
default                                     ssl3 tls1 tls1_1 tls1_2     lts_openssl_exesuite_0.9.8zh lts_openssl_exesuite_1.0.2u lts_openssl_exesuite_1.1.1l lts_openssl_exesuite_3.0.0
RC4-SHA                                     ssl3 tls1 tls1_1 tls1_2     lts_openssl_exesuite_0.9.8zh lts_openssl_exesuite_1.0.2u lts_openssl_exesuite_1.1.1l lts_openssl_exesuite_3.0.0
DES-CBC3-SHA                                ssl3 tls1 tls1_1 tls1_2     lts_openssl_exesuite_0.9.8zh lts_openssl_exesuite_1.0.2u lts_openssl_exesuite_1.1.1l lts_openssl_exesuite_3.0.0
AES128-SHA                                  ssl3 tls1 tls1_1 tls1_2     lts_openssl_exesuite_0.9.8zh lts_openssl_exesuite_1.0.2u lts_openssl_exesuite_1.1.1l lts_openssl_exesuite_3.0.0
AES256-SHA                                  ssl3 tls1 tls1_1 tls1_2     lts_openssl_exesuite_0.9.8zh lts_openssl_exesuite_1.0.2u lts_openssl_exesuite_1.1.1l lts_openssl_exesuite_3.0.0
DHE-RSA-AES128-SHA                          ssl3 tls1 tls1_1 tls1_2     lts_openssl_exesuite_0.9.8zh lts_openssl_exesuite_1.0.2u lts_openssl_exesuite_1.1.1l lts_openssl_exesuite_3.0.0
DHE-RSA-AES256-SHA                          ssl3 tls1 tls1_1 tls1_2     lts_openssl_exesuite_0.9.8zh lts_openssl_exesuite_1.0.2u lts_openssl_exesuite_1.1.1l lts_openssl_exesuite_3.0.0
AES128-GCM-SHA256                           tls1_2                      lts_openssl_exesuite_1.0.2u lts_openssl_exesuite_1.1.1l lts_openssl_exesuite_3.0.0
AES256-GCM-SHA384                           tls1_2                      lts_openssl_exesuite_1.0.2u lts_openssl_exesuite_1.1.1l lts_openssl_exesuite_3.0.0
AES256-SHA256                               tls1_2                      lts_openssl_exesuite_1.0.2u lts_openssl_exesuite_1.1.1l lts_openssl_exesuite_3.0.0
DHE-RSA-AES128-GCM-SHA256                   tls1_2                      lts_openssl_exesuite_1.0.2u lts_openssl_exesuite_1.1.1l lts_openssl_exesuite_3.0.0
DHE-RSA-AES128-SHA256                       tls1_2                      lts_openssl_exesuite_1.0.2u lts_openssl_exesuite_1.1.1l lts_openssl_exesuite_3.0.0
DHE-RSA-AES256-GCM-SHA384                   tls1_2                      lts_openssl_exesuite_1.0.2u lts_openssl_exesuite_1.1.1l lts_openssl_exesuite_3.0.0
DHE-RSA-AES256-SHA256                       tls1_2                      lts_openssl_exesuite_1.0.2u lts_openssl_exesuite_1.1.1l lts_openssl_exesuite_3.0.0
ECDHE-RSA-AES128-GCM-SHA256                 tls1_2                      lts_openssl_exesuite_1.0.2u lts_openssl_exesuite_1.1.1l lts_openssl_exesuite_3.0.0
ECDHE-RSA-AES128-SHA256                     tls1_2                      lts_openssl_exesuite_1.0.2u lts_openssl_exesuite_1.1.1l lts_openssl_exesuite_3.0.0
ECDHE-RSA-AES256-GCM-SHA384                 tls1_2                      lts_openssl_exesuite_1.0.2u lts_openssl_exesuite_1.1.1l lts_openssl_exesuite_3.0.0
ECDHE-RSA-AES256-SHA384                     tls1_2                      lts_openssl_exesuite_1.0.2u lts_openssl_exesuite_1.1.1l lts_openssl_exesuite_3.0.0
DHE-RSA-CHACHA20-POLY1305                   tls1_2                      lts_openssl_exesuite_1.1.1l lts_openssl_exesuite_3.0.0
ECDHE-RSA-CHACHA20-POLY1305                 tls1_2                      lts_openssl_exesuite_1.1.1l lts_openssl_exesuite_3.0.0
TLS_CHACHA20_POLY1305_SHA256                tls1_3                      lts_openssl_exesuite_1.0.2u lts_openssl_exesuite_1.1.1l lts_openssl_exesuite_3.0.0
TLS_AES_256_GCM_SHA384                      tls1_3                      lts_openssl_exesuite_1.1.1l lts_openssl_exesuite_3.0.0
TLS_AES_128_GCM_SHA256                      tls1_3                      lts_openssl_exesuite_1.1.1l lts_openssl_exesuite_3.0.0
";

    public const string Def_SelfTest_Ignore_For_Windows_Before_2022 = "@ssl3@,DHE-RSA-AES128-SHA@,DHE-RSA-AES256-SHA@,DHE-RSA-AES128-SHA256@,DHE-RSA-AES256-SHA256@,DHE-RSA-CHACHA20-POLY1305@,ECDHE-RSA-CHACHA20-POLY1305@,RC4-SHA@,@tls1_3@";
    public const string Def_SelfTest_Ignore_For_Windows_After_2022 = "@ssl3@,DHE-RSA-AES128-SHA@,DHE-RSA-AES256-SHA@,DHE-RSA-AES128-SHA256@,DHE-RSA-AES256-SHA256@,DHE-RSA-CHACHA20-POLY1305@,ECDHE-RSA-CHACHA20-POLY1305@,RC4-SHA@,TLS_CHACHA20_POLY1305_SHA256@";
    public const string Def_SelfTest_Ignore_For_Windows_After_2022_GitHubActions = "@ssl3@,@tls1@,@tls1_1@,DHE-RSA-AES128-SHA@,DHE-RSA-AES256-SHA@,DHE-RSA-AES128-SHA256@,DHE-RSA-AES256-SHA256@,DHE-RSA-CHACHA20-POLY1305@,ECDHE-RSA-CHACHA20-POLY1305@,RC4-SHA@,AES128-GCM-SHA256@,AES128-SHA@,AES256-GCM-SHA384@,AES256-SHA@,AES256-SHA256@,DES-CBC3-SHA@,DHE-RSA-AES128-GCM-SHA256@,DHE-RSA-AES256-GCM-SHA384@,TLS_CHACHA20_POLY1305_SHA256@";

    public const string Def_SelfTest_Ignore_For_Unix = "RC4-SHA@,@ssl3@,DES-CBC3-SHA@,DHE-RSA-AES128-GCM-SHA256@,DHE-RSA-AES128-SHA@,DHE-RSA-AES128-SHA256@,DHE-RSA-AES256-GCM-SHA384@,DHE-RSA-AES256-SHA@,DHE-RSA-AES256-SHA256@,DHE-RSA-CHACHA20-POLY1305@";

    public const string Def_SelfTest_Expected_Certs_For_Windows = "old=01_TestHost_RSA1024_SHA1_2036;new=02_TestHost_RSA4096_SHA256_2099";
    public const string Def_SelfTest_Expected_Certs_For_Unix = "old=01_TestHost_RSA1024_SHA1_2036,test.sample.certificate;new=02_TestHost_RSA4096_SHA256_2099,test.sample.certificate";

    public enum Arch
    {
        linux_arm64,
        linux_x64,
        windows_x64,
    }

    public record Version(string ExeName, Arch Arch, IReadOnlySet<string> SslVersions, IReadOnlySet<string> Options);
    public record Cipher(string Name, IReadOnlySet<string> SslVersions, IReadOnlySet<string> ExeNames);

    public static readonly IReadOnlyList<Version> VersionList;
    public static readonly IReadOnlyList<Cipher> CipherList;

    public static readonly CertificateStore TestCert_00_TestRoot_RSA1024_SHA1_Expired;
    public static readonly CertificateStore TestCert_01_TestHost_RSA1024_SHA1_2036;
    public static readonly CertificateStore TestCert_02_TestHost_RSA4096_SHA256_2099;


    // テスト用の何もしない Web サーバー
    public class SslTestSuiteWebServer : CgiHandlerBase
    {
        protected override void InitActionListImpl(CgiActionList noAuth, CgiActionList reqAuth)
        {
        }
    };


    static LtsOpenSslTool()
    {
        // テスト用証明書の読み込み
        TestCert_00_TestRoot_RSA1024_SHA1_Expired = new CertificateStore(
            Res.AppRoot["SslSuiteTests/00_TestRoot_RSA1024_SHA1_Expired.cer"].Binary.Span,
            Res.AppRoot["SslSuiteTests/00_TestRoot_RSA1024_SHA1_Expired.key"].Binary.Span
            );

        TestCert_01_TestHost_RSA1024_SHA1_2036 = new CertificateStore(
            Res.AppRoot["SslSuiteTests/01_TestHost_RSA1024_SHA1_2036.cer"].Binary.Span,
            Res.AppRoot["SslSuiteTests/01_TestHost_RSA1024_SHA1_2036.key"].Binary.Span
            );

        TestCert_02_TestHost_RSA4096_SHA256_2099 = new CertificateStore(
            Res.AppRoot["SslSuiteTests/02_TestHost_RSA4096_SHA256_2099.cer"].Binary.Span,
            Res.AppRoot["SslSuiteTests/02_TestHost_RSA4096_SHA256_2099.key"].Binary.Span
            );


        List<Version> verList = new();
        List<Cipher> cipherList = new();

        string[] lines = Def_OpenSslExeNamesAndSslVers._GetLines(true, true);
        var archList = Arch.linux_arm64.GetEnumValuesList();

        foreach (var arch in archList)
        {
            foreach (var line in lines)
            {
                string[] tokens = line._Split(StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries, " ", "\t", "　");
                if (tokens.Length >= 2)
                {
                    HashSet<string> sslVerList = new HashSet<string>(StrComparer.IgnoreCaseComparer);
                    HashSet<string> options = new HashSet<string>(StrCmpi);
                    for (int i = 1; i < tokens.Length; i++)
                    {
                        string s = tokens[i];
                        if (s._TryTrimStartWith(out string tmp, StrCmpi, "-"))
                        {
                            options.Add(tmp);
                        }
                        else
                        {
                            sslVerList.Add(s);
                        }
                    }
                    verList.Add(new Version(tokens[0], arch, sslVerList, options));
                }
            }
        }

        VersionList = verList;


        lines = Def_Ciphers._GetLines(true, true);

        foreach (var line in lines)
        {
            string[] tokens = line._Split(StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries, " ", "\t", "　");
            if (tokens.Length >= 3)
            {
                HashSet<string> sslVerList = new HashSet<string>(StrComparer.IgnoreCaseComparer);
                HashSet<string> exeNamesList = new HashSet<string>(StrComparer.IgnoreCaseComparer);

                for (int i = 1; i < tokens.Length; i++)
                {
                    string a = tokens[i];
                    if (a._StartsWithMulti(StringComparison.OrdinalIgnoreCase, "ssl", "tls"))
                    {
                        sslVerList.Add(a);
                    }
                    else if (a.StartsWith("lts_", StringComparison.OrdinalIgnoreCase))
                    {
                        exeNamesList.Add(a);
                    }
                    else
                    {
                        throw new CoresLibException();
                    }
                }

                cipherList.Add(new Cipher(tokens[0], sslVerList, exeNamesList));
            }
        }

        CipherList = cipherList;
    }

    public static Arch GetCurrentArchiecture()
    {
        if (Env.IsWindows) return Arch.windows_x64;
        if (Env.IsLinux)
        {
            if (Env.CpuInfo == Architecture.Arm64) return Arch.linux_arm64;
            if (Env.CpuInfo == Architecture.X64) return Arch.linux_x64;
        }

        throw new CoresLibException("Suitable architecture not found.");
    }

    public static IEnumerable<Version> GetCurrentSuitableVersions()
    {
        var arch = GetCurrentArchiecture();

        return VersionList.Where(x => x.Arch == arch).OrderBy(x => x.ExeName, StrCmpi);
    }

    public record TestSuiteTarget(string HostPort, Version Ver, string SslVer, string CipherName, Ref<string> Error, string Sni, List<string> ExpectedCertsList);

    public static async Task TestSslSniCertSelectionAsync(string hostPort, CancellationToken cancel = default)
    {
        bool isSelf = hostPort._IsSamei("self");

        CgiHttpServer? webServer = null;

        if (isSelf)
        {
            hostPort = $"127.0.0.1:{Consts.Ports.SslTestSuitePort}";

            webServer = CreateTestCgiHttpServer();
        }

        var hostAndPortTuple = hostPort._ParseHostnaneAndPort(443);

        try
        {
            // ポートに接続できるようになるまで一定時間トライする
            await TaskUtil.RetryAsync(async c =>
            {
                await using var sock = await LocalNet.ConnectIPv4v6DualAsync(new TcpConnectParam(hostAndPortTuple.Item1, hostAndPortTuple.Item2));
                await using var sslSock = await sock.SslStartClientAsync(new PalSslClientAuthenticationOptions(true), cancel);

                return true;
            },
            1000,
            60,
            cancel,
            true);

            await TestOneAsync(hostAndPortTuple, "old", 0, cancel);
            await TestOneAsync(hostAndPortTuple, "new", 1, cancel);

            static async Task TestOneAsync(Tuple<string, int> hostAndPortTuple, string sni, int certType, CancellationToken cancel = default)
            {
                await using var sock = await LocalNet.ConnectIPv4v6DualAsync(new TcpConnectParam(hostAndPortTuple.Item1, hostAndPortTuple.Item2));
                await using var sslSock = await sock.SslStartClientAsync(new PalSslClientAuthenticationOptions(sni, true), cancel);

                var serverCert = sslSock.Info.Ssl.RemoteCertificate!;

                if (serverCert.PkiCertificate.VerifySignedByCertificate(TestCert_00_TestRoot_RSA1024_SHA1_Expired.PrimaryCertificate) == false)
                {
                    throw new CoresLibException("VerifySignedByCertificate error.");
                }

                if (certType == 0)
                {
                    if (serverCert.HashSHA1._IsSameHex(TestCert_01_TestHost_RSA1024_SHA1_2036.DigestSHA1Str) == false)
                    {
                        throw new CoresLibException("TestCert_01_TestHost_RSA1024_SHA1_2036 hash different.");
                    }
                }
                else
                {
                    if (serverCert.HashSHA1._IsSameHex(TestCert_02_TestHost_RSA4096_SHA256_2099.DigestSHA1Str) == false)
                    {
                        throw new CoresLibException("TestCert_02_TestHost_RSA4096_SHA256_2099 hash different.");
                    }
                }
            }
        }
        finally
        {
            await webServer._DisposeSafeAsync();
        }

        Con.WriteLine();
        Con.WriteLine("Test OK.");
        Con.WriteLine();
    }

    public static CgiHttpServer CreateTestCgiHttpServer()
    {
        return new CgiHttpServer(new SslTestSuiteWebServer(), new HttpServerOptions()
        {
            AutomaticRedirectToHttpsIfPossible = false,
            UseKestrelWithIPACoreStack = true,
            HttpPortsList = new int[] { }.ToList(),
            HttpsPortsList = new int[] { Consts.Ports.SslTestSuitePort }.ToList(),
            UseStaticFiles = false,
            MaxRequestBodySize = 32 * 1024,
            ReadTimeoutMsecs = 5 * 1000,
            DisableHiveBasedSetting = true,
            UseGlobalCertVault = false,
            ServerCertSelector2Async = async (p, sni) =>
            {
                await Task.CompletedTask;

                CertificateStore? cert;

                if (sni._InStr("new"))
                {
                    cert = TestCert_02_TestHost_RSA4096_SHA256_2099;
                }
                else
                {
                    cert = TestCert_01_TestHost_RSA1024_SHA1_2036;
                }

                X509Certificate2Collection col = new X509Certificate2Collection(DevTools.TestSampleCert.NativeCertificate2._SingleArray());

                SslStreamCertificateContext context = Secure.CreateSslCreateCertificateContextWithFullChain(cert.X509Certificate, col, true, true);

                return context;
            },
        },
        true);
    }

    public static async Task<bool> TestSuiteAsync(string hostPort, int maxConcurrentTasks, int intervalMsecs, string ignoresList = "", CancellationToken cancel = default, string sniAndExpectedStrList = "")
    {
        bool ret = false;

        bool isSelf = hostPort._IsSamei("self");

        CgiHttpServer? webServer = null;

        if (isSelf)
        {
            hostPort = $"127.0.0.1:{Consts.Ports.SslTestSuitePort}";

            webServer = CreateTestCgiHttpServer();

            if (sniAndExpectedStrList._IsSamei("default"))
            {
                sniAndExpectedStrList = Env.IsWindows ? Def_SelfTest_Expected_Certs_For_Windows : Def_SelfTest_Expected_Certs_For_Unix;
            }

            if (ignoresList._IsSamei("default"))
            {
                if (Env.IsWindows)
                {
                    ignoresList = Def_SelfTest_Ignore_For_Windows_Before_2022;

                    if (Env.WindowsFamily.IsOrGreater(WindowsFamily.WindowsServer2022))
                    {
                        ignoresList = Def_SelfTest_Ignore_For_Windows_After_2022;

                        if (Env.IsOnGitHubActions)
                        {
                            // メモ 2021/11/26: GitHub Actions が提供する Windows Server 2022 の OS イメージは、なぜか、TLS 1.0, TLS 1.1 がレジストリ上で無効化されている。
                            // これが原因で SslSelfTest の TLS 1.0, TLS 1.1 系 + 一部のアルゴリズムのテストが失敗してしまうようである。
                            // "Enable TLS 1.0 and 1.1 (disabled since windows-2022 GitHub custom images) " のレジストリ変更を実装したが、
                            // Windows の再起動が必要なようであり、GitHub Actions ではうまく利用できなかった。

                            ignoresList = Def_SelfTest_Ignore_For_Windows_After_2022_GitHubActions;
                        }
                    }
                }
                else
                {
                    ignoresList = Def_SelfTest_Ignore_For_Unix;
                }
            }

            Con.WriteLine("Self test: Host and port: " + hostPort);
            Con.WriteLine("Self test: ignoresList: " + ignoresList);
        }

        var hostAndPortTuple = hostPort._ParseHostnaneAndPort(443);

        try
        {
            // ポートに接続できるようになるまで一定時間トライする
            await TaskUtil.RetryAsync(async c =>
            {
                await using var sock = await LocalNet.ConnectIPv4v6DualAsync(new TcpConnectParam(hostAndPortTuple.Item1, hostAndPortTuple.Item2));
                await using var sslSock = await sock.SslStartClientAsync(new PalSslClientAuthenticationOptions(true), cancel);

                return true;
            },
            1000,
            60,
            cancel,
            true);

            Con.WriteLine("Port ready OK.");

            ret = await TestSuiteCoreAsync(hostPort, maxConcurrentTasks, intervalMsecs, isSelf, ignoresList, cancel, sniAndExpectedStrList);
        }
        finally
        {
            await webServer._DisposeSafeAsync();
        }

        return ret;
    }

    record SniAndExpected(string Sni, string[] ExpectedList);

    static async Task<bool> TestSuiteCoreAsync(string hostPort, int maxConcurrentTasks, int intervalMsecs, bool selfTest, string ignoresList, CancellationToken cancel, string sniAndExpectedStrList)
    {
        if (maxConcurrentTasks <= 0) maxConcurrentTasks = 1;

        List<TestSuiteTarget> targets = new List<TestSuiteTarget>();

        var currentArch = GetCurrentArchiecture();

        var ignores = ignoresList._Split(StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries, ",", " ", "|", "/", ";", "\r", "\n");

        var exps = sniAndExpectedStrList._Split(StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries, ";", "\r", "\n");

        List<SniAndExpected> sniAndExpectedList = new List<SniAndExpected>();

        foreach (var exp in exps)
        {
            if (exp._GetKeyAndValue(out string sni, out string expStrs, "="))
            {
                sni = sni._NonNullTrim();
                var expStrLists = expStrs._Split(StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries, ",");

                //if (Env.IsWindows && selfTest)
                //{
                //    // IIS では複数証明書の明示的応答をサポートしていない
                //    expStrLists = expStrLists.FirstOrDefault()._NonNullTrim()._SingleArray();
                //}

                if (sni._IsFilled())
                {
                    SniAndExpected r = new SniAndExpected(sni, expStrLists);

                    sniAndExpectedList.Add(r);
                }
            }
        }

        // ターゲット一覧を生成
        foreach (var ver in VersionList.Where(x => x.Arch == currentArch).OrderBy(x => x.ExeName, StrCmpi))
        {
            foreach (var cipher in CipherList.Where(x => x.ExeNames.Contains(ver.ExeName)))
            {
                foreach (var sslVer in cipher.SslVersions.Where(x => cipher.SslVersions.Contains(x)).Where(x => ver.SslVersions.Contains(x)).OrderBy(x => x, StrCmpi))
                {
                    string tmp = cipher.Name + "@" + sslVer + "@" + ver.ExeName;

                    if (ignores.Where(x => x._IsSamei(tmp) || (x.StartsWith("@") && x.EndsWith("@") && tmp._InStr(x, true)) || (x.EndsWith("@") && tmp.StartsWith(x, StrCmpi))).Any() == false)
                    {
                        if (sniAndExpectedList.Any() == false)
                        {
                            targets.Add(new TestSuiteTarget(hostPort, ver, sslVer, cipher.Name, "", "", new List<string>()));
                        }
                        else
                        {
                            foreach (var exp in sniAndExpectedList)
                            {
                                targets.Add(new TestSuiteTarget(hostPort, ver, sslVer, cipher.Name, "", exp.Sni,
                                    exp.ExpectedList.ToList()));

                                if (ver.Options.Contains("nosni"))
                                {
                                    // old OpenSSL versions don't support sni. force use first test.
                                    break;
                                }
                            }
                        }
                    }
                }
            }
        }

        // テストの実行
        targets = targets._Shuffle().ToList();
        //targets = targets.ToList();

        RefInt x = new RefInt();

        CriticalSection printLock = new CriticalSection();

        await TaskUtil.ForEachAsync(maxConcurrentTasks, targets, async (t, taskIndex, c) =>
        {
            int currentNum = x.Increment();
            Con.WriteLine($"Processing {currentNum} / {targets.Count} ... {t.Ver.ExeName} {t.SslVer} {t.CipherName}");
            try
            {
                await TaskUtil.RetryAsync<int>(async c2 =>
                {
                    await TestSuiteRunCoreAsync(t, c2);

                    return 0;
                }, intervalMsecs, RetryCount, c, true);
            }
            catch (Exception ex)
            {
                t.Error.Set(ex.Message);
            }
        },
        cancel,
        intervalMsecs);

        // 結果を表示
        var list = targets.OrderBy(x => x.HostPort, StrCmpi).ThenBy(x => x.Ver.ExeName).ThenBy(x => x.CipherName, StrCmpi).ThenBy(x => x.SslVer, StrCmpi);

        int numTotal = list.Count();

        var errorList = list.Where(x => x.Error._IsFilled()).ToArray();
        var okList = list.Where(x => x.Error._IsEmpty()).ToArray();

        int numError = errorList.Count();
        int numOk = okList.Count();

        Con.WriteLine($"");
        Con.WriteLine($"");
        Con.WriteLine($"--- All finished !!! ---");
        Con.WriteLine($"");
        Con.WriteLine($"OK: {numOk} / {numTotal} tests");
        Con.WriteLine($"Error: {numError} / {numTotal} tests");


        Con.WriteLine();
        Con.WriteLine();
        Con.WriteLine($"===== OK lists =====");
        for (int i = 0; i < okList.Length; i++)
        {
            var item = okList[i];
            Con.WriteLine($"*** OK #{i + 1} / {numOk}:");
            Con.WriteLine($"   Version:  {item.Ver.ExeName}");
            Con.WriteLine($"   Cipher:   {item.CipherName}");
            Con.WriteLine($"   SslVer:   {item.SslVer}");
            Con.WriteLine($"   Host:     {item.HostPort}");
            if (item.Sni._IsFilled())
            {
                Con.WriteLine($"   Sni:      {item.Sni}");
                Con.WriteLine($"   ExpectedCertsList: {item.ExpectedCertsList._OnlyFilled()._Combine(" && ")}");
            }
            Con.WriteLine();
        }


        Con.WriteLine();
        Con.WriteLine();
        Con.WriteLine($"===== Error lists =====");
        for (int i = 0; i < errorList.Length; i++)
        {
            var item = errorList[i];
            Con.WriteLine($"*** Error #{i + 1} / {numError}:");
            Con.WriteLine($"   Version:  {item.Ver.ExeName}");
            Con.WriteLine($"   Cipher:   {item.CipherName}");
            Con.WriteLine($"   SslVer:   {item.SslVer}");
            Con.WriteLine($"   Host:     {item.HostPort}");
            if (item.Sni._IsFilled())
            {
                Con.WriteLine($"   Sni:      {item.Sni}");
                Con.WriteLine($"   ExpectedCertsList:      {item.ExpectedCertsList._OnlyFilled()._Combine(" && ")}");
            }
            Con.WriteLine($"   Error:    {item.Error}");
            Con.WriteLine();
        }


        if (numError >= 1)
        {
            Con.WriteLine();
            Con.WriteLine();
            Con.WriteLine("===== Ignores List Hint =====");
            List<string> ignoreList = new List<string>();
            errorList.OrderBy(x => x.CipherName, StrCmpi).ThenBy(x => x.SslVer, StrCmpi).ThenBy(x => x.Ver.ExeName, StrCmpi)
                ._DoForEach(x => ignoreList.Add(x.CipherName + "@" + x.SslVer + "@" + x.Ver.ExeName));
            Con.WriteLine(ignoreList.Distinct(StrCmpi)._Combine(","));
        }

        Con.WriteLine($"");
        Con.WriteLine($"");
        Con.WriteLine($"--- Total ---");
        Con.WriteLine($"OK: {numOk} / {numTotal} tests");
        Con.WriteLine($"Error: {numError} / {numTotal} tests");
        Con.WriteLine($"");

        return numError == 0;
    }

    public static async Task TestSuiteRunCoreAsync(TestSuiteTarget target, CancellationToken cancel = default)
    {
        var hostPort = target.HostPort._ParseHostnaneAndPort(443);

        await ExecOpenSslClientConnectTest(target.Ver, hostPort.Item1, hostPort.Item2, target.SslVer, target.Sni, target.ExpectedCertsList, target.CipherName, cancel);
    }

    // OpenSSL での SSL 接続テストの実行
    public static async Task ExecOpenSslClientConnectTest(Version ver, string host, int port, string protoVerStr, string sni, List<string> expectCertStrList, string? cipher = null, CancellationToken cancel = default)
    {
        if (cipher._IsSamei("default"))
        {
            cipher = "";
        }

        string args = $"s_client -connect {host}:{port} -{protoVerStr} -showcerts";
        if (cipher._IsFilled())
        {
            if (protoVerStr._IsSamei("tls1_3"))
            {
                args += $" -ciphersuites {cipher}";
            }
            else
            {
                args += $" -cipher {cipher}";
            }
        }

        if (ver.Options.Contains("nosni") == false)
        {
            if (sni._IsFilled())
            {
                args += $" -servername {sni}";
            }
            else
            {
                if (ver.Options.Contains("nonoservername") == false)
                {
                    args += $" -noservername";
                }
            }
        }

        bool ok = false;
        string error = "";

        try
        {
            var res = await ExecOpenSslCommandAsync(ver, args,
                async (stdout, stderr) =>
                {
                    // 現在までに届いている stdout と stderr の内容を文字列に変換する
                    var lines = (stdout._GetString_UTF8() + "\r\n" + stderr._GetString_UTF8())._GetLines(trim: true);

                    // エラーが来ていないかどうか確認
                    foreach (var tmp in lines)
                    {
                        var tokens = tmp._Split(StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries, ":");
                        if (tokens.ElementAtOrDefault(1)._NonNull().StartsWith("error", StringComparison.OrdinalIgnoreCase))
                        {
                            // RC4-MD5 で発生するような SSL エラーが発生している。
                            // その後 Session-ID: が一見正常に届くように見えるが、このエラーが発生している時点で
                            // 通信はうまくできないのである。
                            ok = false;
                            error = tmp;

                            // もうプロセスは終了してよい
                            return true;
                        }
                    }

                    foreach (var tmp in lines)
                    {
                        if (tmp._InStr("Cipher is (NONE)", ignoreCase: true))
                        {
                            // 暗号化アルゴリズムが選定されなかった
                            ok = false;

                            error = tmp;
                            // もうプロセスは終了してよい
                            return true;
                        }
                    }

                    bool flag1 = false;

                    bool allCertsOk = true;

                    // showcerts 結果文字列検索
                    if (expectCertStrList._OnlyFilled().Any())
                    {
                        foreach (var test in expectCertStrList._OnlyFilled())
                        {
                            bool singleOk = false;
                            foreach (var tmp in lines)
                            {
                                if (tmp._InStri(test))
                                {
                                    singleOk = true;
                                    break;
                                }
                            }
                            if (singleOk == false)
                            {
                                allCertsOk = false;
                            }
                        }
                    }

                    var sni2 = sni;

                    // 成功状態になっていないか確認 (これらの条件が満たされた場合でも、エラーが来ていれば NG である。エラーは上のコードで検出されるのである。)
                    foreach (var tmp in lines)
                    {
                        if (tmp._TryTrimStartWith(out string tmp2, StringComparison.OrdinalIgnoreCase, "Session-ID:"))
                        {
                            tmp2 = tmp2._NonNullTrim();
                            byte[] sessionKey = tmp2._GetHexBytes();
                            if (sessionKey._IsZero() == false && sessionKey.Length >= 16)
                            {
                                if (allCertsOk)
                                {
                                    // OK
                                    ok = true;
                                }
                                else
                                {
                                    // 期待している証明書がきていない
                                    ok = false;
                                    error = "Some expected certificates didn't come.";
                                }

                                // もうプロセスは終了してよい
                                return true;
                            }
                        }

                        if (tmp.StartsWith("New, TLSv1.3, Cipher is", StrCmpi))
                        {
                            flag1 = true;
                        }

                        if (flag1)
                        {
                            if (tmp._InStr("Secure Renegotiation IS", true) && tmp._InStr("supported"))
                            {
                                // TLS 1.3 でセッションキー等は届いていない (サーバー側で送出してこない) が、ネゴシエーションには成功したものと思われる。
                                // www.google.com 等でこの挙動がある。
                                if (allCertsOk)
                                {
                                    // OK
                                    ok = true;
                                }
                                else
                                {
                                    // 期待している証明書がきていない
                                    ok = false;
                                    error = "Some expected certificates didn't come.";
                                }

                                // もうプロセスは終了してよい
                                return true;
                            }
                        }
                    }

                    await Task.CompletedTask;

                    return false;
                },
                cancel);
        }
        catch (Exception ex)
        {
            if (ok == false)
            {
                if (error._IsEmpty())
                {
                    error = ex.Message;
                }
            }
        }

        if (ok == false)
        {
            if (error._IsEmpty())
            {
                error = "Unknown error";
            }
        }

        if (error._IsFilled())
        {
            throw new CoresException(error);
        }
    }

    // OpenSSL コマンドの実行
    public static async Task<EasyExecResult> ExecOpenSslCommandAsync(Version ver, string args,
            Func<ReadOnlyMemory<byte>, ReadOnlyMemory<byte>, Task<bool>>? easyRealtimeRecvBufCallbackAsync = null, CancellationToken cancel = default)
    {
        var exePath = await PrepareOpenSslExeAsync(ver, cancel);

        return await EasyExec.ExecAsync(exePath, args, PP.GetDirectoryName(exePath),
            ExecFlags.EasyInputOutputMode /*| ExecFlags.EasyPrintRealtimeStdErr | ExecFlags.EasyPrintRealtimeStdOut*/,
            10_000_000,
            cancel: cancel,
            printTag: ver.ExeName,
            timeout: CommandTimeoutMsecs,
            easyRealtimeRecvBufCallbackAsync: easyRealtimeRecvBufCallbackAsync,
            easyRealtimeRecvBufCallbackDelayTickMsecs: 1000);
    }

    // OpenSSL コマンドの実行
    public static async Task<string> PrepareOpenSslExeAsync(Version ver, CancellationToken cancel = default)
    {
        // URL を生成する
        string exeName = ver.ExeName;
        if (ver.Arch == Arch.windows_x64) exeName += ".exe";
        string url = BaseUrl._CombineUrlDir(VersionYymmdd)._CombineUrlDir("lts_openssl_exesuite")._CombineUrlDir(ver.Arch.ToString())._CombineUrl(exeName).ToString();

        // 一時ディレクトリ名を生成する
        string tmpDirPath = PP.Combine(Env.AppLocalDir, "Temp", "lts_openssl_cache", VersionYymmdd);
        string tmpExePath = PP.Combine(tmpDirPath, exeName);

        string lockName = tmpDirPath.ToLowerInvariant() + "_lock_" + ver._ObjectToJson()._HashSHA1()._GetHexString();

        GlobalLock lockObject = new GlobalLock(lockName);

        // 以下の操作はロック下で実施する
        using (lockObject.Lock())
        {
            bool exists = false;

            // ファイルが存在しているか?
            if (Lfs.IsFileExists(tmpExePath, cancel))
            {
                var info = Lfs.GetFileMetadata(tmpExePath, cancel: cancel);
                if (info.Size >= 1_000_000)
                {
                    exists = true;
                }
            }

            if (exists == false)
            {
                // ファイルをダウンロードする
                var res = await SimpleHttpDownloader.DownloadAsync(url, sslServerCertValicationCallback: (sender, cert, chain, err) =>
                {
                    return cert!.GetCertHashString()._IsSameHex(BaseUrlCertHash);
                });
                if (res.DataSize < 1_000_000)
                {
                    throw new CoresLibException($"URL {url} file size too small: {res.DataSize} bytes");
                }

                // ダウンロードしたファイルを一時ファイルに保存する
                string tmpExePath2 = tmpExePath + ".tmp";

                Lfs.WriteDataToFile(tmpExePath2, res.Data, FileFlags.AutoCreateDirectory, cancel: cancel);

                // 保存が完了したらリネームする
                try
                {
                    Lfs.DeleteFile(tmpExePath, cancel: cancel);
                }
                catch { }

                Lfs.MoveFile(tmpExePath2, tmpExePath, cancel);
            }

            // パーミッションの設定
            try
            {
                Lfs.UnixSetPermissions(tmpExePath, UnixPermissions.S_IROTH | UnixPermissions.S_IWOTH | UnixPermissions.S_IXOTH | UnixPermissions.S_IRGRP | UnixPermissions.S_IXGRP | UnixPermissions.S_IRUSR | UnixPermissions.S_IXUSR);
            }
            catch (Exception ex)
            {
                ex._Debug();
            }
        }

        return tmpExePath;
    }
}


#endif

