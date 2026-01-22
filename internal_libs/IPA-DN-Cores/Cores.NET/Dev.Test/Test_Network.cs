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

using System;
using System.IO;
using System.IO.Enumeration;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

#pragma warning disable CS0162
#pragma warning disable CS0219

namespace IPA.TestDev;

class TestHttpServerBuilder : HttpServerStartupBase
{
    public TestHttpServerBuilder(IConfiguration configuration) : base(configuration)
    {
    }

    protected override void ConfigureImpl_BeforeHelper(HttpServerStartupConfig cfg, IApplicationBuilder app, IWebHostEnvironment env, IHostApplicationLifetime lifetime)
    {
    }

    protected override void ConfigureImpl_AfterHelper(HttpServerStartupConfig cfg, IApplicationBuilder app, IWebHostEnvironment env, IHostApplicationLifetime lifetime)
    {
    }
}

partial class TestDevCommands
{
    public class FqdnScanResult
    {
        public string IpSortKey = "";
        public string Ip = "";
        public string FqdnSortKey = "";
        public string FqdnList = "";
        public string TcpPortList = "";
    }



    // SNMP Worker CGI ハンドラ
    public class FakeHttpServer_CgiHandler : CgiHandlerBase
    {
        public FakeHttpServer_CgiHandler()
        {
        }

        protected override void InitActionListImpl(CgiActionList noAuth, CgiActionList reqAuth)
        {
            try
            {
                noAuth.AddAction("/", WebMethodBits.GET | WebMethodBits.HEAD, async (ctx) =>
                {
                    await Task.CompletedTask;

                    StringWriter w = new StringWriter();
                    w.WriteLine(DtOffsetNow._ToDtStr(true));
                    w.WriteLine();
                    for (int i = 0; i < 10; i++)
                    {
                        w.WriteLine($"Hello World Neko Neko Neko {i}");
                    }

                    return new HttpStringResult(w.ToString()._NormalizeCrlf(CrlfStyle.Lf, true));
                });

                noAuth.AddAction("/push/", WebMethodBits.GET | WebMethodBits.HEAD, async (ctx) =>
                {
                    await Task.CompletedTask;

                    PipeStreamPairWithSubTask streamPair = new PipeStreamPairWithSubTask(async (writeMe) =>
                    {
                        StreamWriter w = new StreamWriter(writeMe);
                        w.AutoFlush = true;

                        w.WriteLine(DtOffsetNow._ToDtStr(true));
                        w.WriteLine();
                        for (int i = 0; ; i++)
                        {
                            w.WriteLine($"Hello World Neko Neko Neko {i}");

                            await w.FlushAsync();
                            await writeMe.FlushAsync();

                            await Task.Delay(100);
                        }
                    });

                    List<KeyValuePair<string, string>> headers = new List<KeyValuePair<string, string>>();

                    return new HttpResult(streamPair.StreamA, 0, null, Consts.MimeTypes.TextUtf8, additionalHeaders: headers, onDisposeAsync: () => streamPair._DisposeSafeAsync2());
                });


                noAuth.AddAction("/exe/", WebMethodBits.GET | WebMethodBits.HEAD, async (ctx) =>
                {
                    await Task.CompletedTask;

                    string srcFileName = Env.Win32_SystemDir._CombinePath("ca.exe");

                    var file = Lfs.Open(srcFileName, cancel: ctx.Cancel);

                    return new HttpFileResult(file, 0, await file.GetFileSizeAsync(cancel: ctx.Cancel), filename: PP.GetFileName(srcFileName));
                });
            }
            catch
            {
                this._DisposeSafe();
                throw;
            }
        }
    }

    [ConsoleCommand(
    "HTTPS Fake Cert Server",
    "HttpsFakeCertServer [commonName]",
    "HTTPS Fake Cert Server")]
    static int HttpsFakeCertServer(ConsoleService c, string cmdName, string str)
    {
        ConsoleParam[] args =
        {
        };

        ConsoleParamValueList vl = c.ParseCommandList(cmdName, str, args);

        var ca = DevTools.CoresDebugCACert_20221125;

        var certSingleton = new Singleton<string, PalX509Certificate>(hostname =>
        {
            PkiUtil.GenerateRsaKeyPair(2048, out PrivKey newKey, out _);

            Certificate newCert = new Certificate(newKey, ca, new CertificateOptions(PkiAlgorithm.RSA, cn: hostname, c: "US", type: CertificateOptionsType.ServerCertificate, expires: DtOffsetNow.AddDays(30)));
            CertificateStore newCertStore = new CertificateStore(newCert, newKey);

            var cert = newCertStore.X509Certificate;

            return cert;
        });

        // HTTP サーバーを立ち上げる
        using var cgi = new CgiHttpServer(new FakeHttpServer_CgiHandler(), new HttpServerOptions()
        {
            AutomaticRedirectToHttpsIfPossible = false,
            DisableHiveBasedSetting = true,
            DenyRobots = false,
            UseGlobalCertVault = false,
            LocalHostOnly = false,
            HttpPortsList = new int[] { 80 }.ToList(),
            HttpsPortsList = new int[] { 443 }.ToList(),
            UseKestrelWithIPACoreStack = true,
            ServerCertSelector = (p, hostname) => certSingleton[hostname._NormalizeFqdn()],
        },
        true);

        Con.ReadLine("Enter to Stop>");

        return 0;
    }


    [ConsoleCommand(
    "DNS FQDN Scanner",
    "FqdnScan [subnets] [/servers:8.8.8.8,8.8.4.4] [/threads:64] [/interval:100] [/try:1] [/shuffle:yes] [/fqdnorder:yes] [/dest:csv]",
    "DNS FQDN Scanner")]
    static async Task FqdnScanAsync(ConsoleService c, string cmdName, string str)
    {
        ConsoleParam[] args =
        {
            new ConsoleParam("[subnets]", ConsoleService.Prompt, "Subnets: ", ConsoleService.EvalNotEmpty, null),
            new ConsoleParam("servers"),
            new ConsoleParam("threads"),
            new ConsoleParam("interval"),
            new ConsoleParam("try"),
            new ConsoleParam("shuffle"),
            new ConsoleParam("fqdnorder"),
            new ConsoleParam("dest"),
            new ConsoleParam("ports"),
        };

        ConsoleParamValueList vl = c.ParseCommandList(cmdName, str, args);

        string subnets = vl.DefaultParam.StrValue;
        string servers = vl["servers"].StrValue;
        int threads = vl["threads"].IntValue;
        int interval = vl["interval"].IntValue;
        int numtry = vl["try"].IntValue;
        bool shuffle = vl["shuffle"].StrValue._ToBool(true);
        bool fqdnorder = vl["fqdnorder"].StrValue._ToBool(true);
        string csv = vl["dest"].StrValue;
        string portsStr = vl["ports"].StrValue;

        PortRange portRange = new PortRange(portsStr);

        var serversList = servers._Split(StringSplitOptions.RemoveEmptyEntries, " ", "　", ",", "|");
        if (serversList._IsEmpty())
        {
            serversList = new string[] { "8.8.8.8", "8.8.4.4", "1.1.1.1", "3.3.3.3" };
        }

        List<IPEndPoint> endPointsList = new List<IPEndPoint>();

        serversList._DoForEach(x => endPointsList.Add(new IPEndPoint(x._ToIPAddress()!, 53)));

        using DnsHostNameScanner scan = new DnsHostNameScanner(
            settings: new DnsHostNameScannerSettings { Interval = interval, NumThreads = threads, NumTry = numtry, PrintStat = true, RandomInterval = true, Shuffle = shuffle, PrintOrderByFqdn = fqdnorder, TcpPorts = portRange.ToArray(), },
            dnsSettings: new DnsResolverSettings(dnsServersList: endPointsList, flags: DnsResolverFlags.UdpOnly | DnsResolverFlags.RoundRobinServers));

        var list = await scan.PerformAsync(subnets);

        if (csv._IsFilled())
        {
            using var csvWriter = Lfs.WriteCsv<FqdnScanResult>(csv, false, true, writeBom: false, flags: FileFlags.AutoCreateDirectory);

            foreach (var item in list)
            {
                if (item.HostnameList._IsFilled())
                {
                    FqdnScanResult r = new FqdnScanResult();

                    r.IpSortKey = IPAddr.FromAddress(item.Ip).GetZeroPaddingFullString();
                    r.Ip = item.Ip.ToString();
                    r.FqdnSortKey = Str.ReverseFqdnStr(item.HostnameList.First()).ToLowerInvariant();
                    r.FqdnList = item.HostnameList._Combine(" / ");
                    r.TcpPortList = item.TcpPorts.Select(x => x.ToString())._Combine(" / ");

                    csvWriter.WriteData(r);
                }
            }
        }
    }


    [ConsoleCommand(
    "指定された URL から次々に辿ってファイルをダウンロードする (ダウンロードされたファイルはメモリに保存しない)",
    "HttpFileSpider [url] [/threads:num]",
    "指定された URL から次々に辿ってファイルをダウンロードする (ダウンロードされたファイルはメモリに保存しない)")]
    static int HttpFileSpider(ConsoleService c, string cmdName, string str)
    {
        ConsoleParam[] args =
        {
                new ConsoleParam("[url]", ConsoleService.Prompt, "Input URL: ", ConsoleService.EvalNotEmpty, null),
                new ConsoleParam("threads"),
            };

        ConsoleParamValueList vl = c.ParseCommandList(cmdName, str, args);

        int threads = vl["threads"].IntValue;
        if (threads <= 0) threads = 1;

        string url = vl.DefaultParam.StrValue;

        Async(async () =>
        {
            await MiscUtil.HttpFileSpiderAsync(url, new FileDownloadOption(threads, threads,
                webApiOptions: new WebApiOptions(new WebApiSettings { AllowAutoRedirect = true, MaxConnectionPerServer = 1_000_000, SslAcceptAnyCerts = true }, doNotUseTcpStack: true)));
        });

        return 0;
    }



    [ConsoleCommand(
    "指定された URL (のテキストファイル) をダウンロードし、その URL に記載されているすべてのファイルをダウンロードする",
    "DownloadUrlListedAsync [url] [/dest:dir] [/ext:tar.gz,zip,exe,...]",
    "指定された URL (のテキストファイル) をダウンロードし、その URL に記載されているすべてのファイルをダウンロードする")]
    static int DownloadUrlListedAsync(ConsoleService c, string cmdName, string str)
    {
        ConsoleParam[] args =
        {
                new ConsoleParam("[url]", ConsoleService.Prompt, "Input URL: ", ConsoleService.EvalNotEmpty, null),
                new ConsoleParam("dest", ConsoleService.Prompt, "Input dest directory: ", ConsoleService.EvalNotEmpty, null),
                new ConsoleParam("ext", ConsoleService.Prompt),
                new ConsoleParam("threads", ConsoleService.Prompt),
                new ConsoleParam("parts", ConsoleService.Prompt),
                new ConsoleParam("retry", ConsoleService.Prompt),
                new ConsoleParam("ignorerrror", ConsoleService.Prompt),
            };

        ConsoleParamValueList vl = c.ParseCommandList(cmdName, str, args);

        string extList = vl["ext"].StrValue;
        if (extList._IsEmpty()) extList = "tar.gz,zip,exe";

        var option = new FileDownloadOption(maxConcurrentThreads: vl["threads"].IntValue, maxConcurrentFiles: vl["parts"].IntValue, retryIntervalMsecs: vl["retry"].IntValue, ignoreErrorInMultiFileDownload: vl["ignorerrror"].BoolValue);

        var ok = FileDownloader.DownloadUrlListedAsync(vl.DefaultParam.StrValue,
            vl["dest"].StrValue,
            extList,
            reporterFactory: new ProgressFileDownloadingReporterFactory(ProgressReporterOutputs.Console, options: ProgressReporterOptions.EnableThroughput | ProgressReporterOptions.ShowThroughputBps),
            option: option
            )._GetResult();

        if (ok == false)
        {
            Con.WriteError("Error occured.");
            return -1;
        }

        return 0;
    }

    [ConsoleCommand(
    "指定された URL のファイルをダウンロードする",
    "DownloadFile [url] [/dest:dir] [/ext:tar.gz,zip,exe,...]",
    "指定された URL (のテキストファイル) をダウンロードし、その URL に記載されているすべてのファイルをダウンロードする")]
    static int DownloadFile(ConsoleService c, string cmdName, string str)
    {
        ConsoleParam[] args =
        {
                new ConsoleParam("[url]", ConsoleService.Prompt, "Input URL: ", ConsoleService.EvalNotEmpty, null),
                new ConsoleParam("dest", ConsoleService.Prompt, "Input dest file name: ", ConsoleService.EvalNotEmpty, null),
            };

        ConsoleParamValueList vl = c.ParseCommandList(cmdName, str, args);

        string url = vl.DefaultParam.StrValue;

        string destFileName = vl["dest"].StrValue;

        Async(async () =>
        {
            var option = new FileDownloadOption();
            var reporterFactory = new ProgressFileDownloadingReporterFactory(ProgressReporterOutputs.Console, options: ProgressReporterOptions.EnableThroughput | ProgressReporterOptions.ShowThroughputBps);

            using var reporter = reporterFactory.CreateNewReporter(destFileName);

            await using var file = await Lfs.CreateAsync(destFileName, false, FileFlags.AutoCreateDirectory);
            await using var fileStream = file.GetStream();

            await FileDownloader.DownloadFileParallelAsync(url, fileStream, option, progressReporter: reporter);
        });

        return 0;
    }

    [ConsoleCommand(
        "SslCertListCollector command",
        "SslCertListCollector [dnsZonesDir] [/OUT:outDir]",
        "SslCertListCollector command")]
    static int SslCertListCollector(ConsoleService c, string cmdName, string str)
    {
        ConsoleParam[] args =
        {
                new ConsoleParam("[dnsZonesDir]", ConsoleService.Prompt, "dnsZonesDir: ", ConsoleService.EvalNotEmpty, null),
                new ConsoleParam("OUT", ConsoleService.Prompt, "outDir: ", ConsoleService.EvalNotEmpty, null),
                new ConsoleParam("HASH"),
            };
        ConsoleParamValueList vl = c.ParseCommandList(cmdName, str, args);

        string dirZonesDir = vl.DefaultParam.StrValue;
        string outDir = vl["OUT"].StrValue;
        string hashliststr = vl["HASH"].StrValue;
        string[] hashlist = hashliststr._Split(StringSplitOptions.RemoveEmptyEntries, ";", "/", ",", ":", " ");

        var dirList = Lfs.EnumDirectory(dirZonesDir, false);
        if (dirList.Where(x => x.IsFile && (IgnoreCaseTrim)Lfs.PathParser.GetExtension(x.Name) == ".dns").Any() == false)
        {
            // 指定されたディレクトリに *.dns ファイルが 1 つもない場合は子ディレクトリ名をソートして一番大きいものを選択する
            dirZonesDir = dirList.OrderByDescending(x => x.Name).Where(x => x.IsDirectory).Select(x => x.FullPath).First();
        }

        Con.WriteLine($"Target directory: '{dirZonesDir}'");

        // 1. DNS ゾーンファイルを入力してユニークな FQDN レコードの一覧を生成する
        DnsFlattenUtil flat = new DnsFlattenUtil();

        foreach (FileSystemEntity ent in Lfs.EnumDirectory(dirZonesDir, true))
        {
            if (ent.IsFile)
            {
                string fn = ent.FullPath;
                fn._Print();

                flat.InputZoneFile(Lfs.PathParser.GetFileNameWithoutExtension(fn), Lfs.ReadDataFromFile(fn).Span);
            }
        }

        // 2. FQDN の一覧を入力して FQDN と IP アドレスのペアの一覧を生成する
        DnsIpPairGeneratorUtil pairGenerator = new DnsIpPairGeneratorUtil(100, flat.FqdnSet);

        List<SniHostnameIpAddressPair> list = pairGenerator.ExecuteAsync()._GetResult().ToList();

        // 3. FQDN と IP アドレスのペアの一覧を入力して SSL 証明書一覧を出力する
        SslCertCollectorUtil col = new SslCertCollectorUtil(1000, list);

        IReadOnlyList<SslCertCollectorItem> ret = col.ExecuteAsync()._GetResult();

        if (hashlist.Length >= 1)
        {
            List<SslCertCollectorItem> filtered = new List<SslCertCollectorItem>();
            ret.Where(x => hashlist.Where(y => y._IsSameHex(x.CertHashSha1)).Any())._DoForEach(x => filtered.Add(x));
            ret = filtered;
        }

        ret = ret.OrderBy(x => x.FriendName._NonNullTrim()._Split(StringSplitOptions.RemoveEmptyEntries, ".").Reverse()._Combine("."), StrComparer.IgnoreCaseTrimComparer).ToList();

        // 結果表示
        Con.WriteLine($"Results: {ret.Count} endpoints");

        // 結果保存
        string csv = ret._ObjectArrayToCsv(withHeader: true);

        XmlAndXsd xmlData = Util.GenerateXmlAndXsd(ret);

        string dir = outDir;

        Lfs.WriteDataToFile(Lfs.PathParser.Combine(dir, xmlData.XmlFileName), xmlData.XmlData, flags: FileFlags.AutoCreateDirectory);
        Lfs.WriteDataToFile(Lfs.PathParser.Combine(dir, xmlData.XsdFileName), xmlData.XsdData, flags: FileFlags.AutoCreateDirectory);
        Lfs.WriteStringToFile(Lfs.PathParser.Combine(dir, "csv.csv"), csv, flags: FileFlags.AutoCreateDirectory, writeBom: true);

        return 0;
    }


    [ConsoleCommand(
        "SslCertChecker command",
        "SslCertChecker [dnsZonesDir] [/INCLUDE:includeFile]",
        "SslCertChecker command")]
    static int SslCertChecker(ConsoleService c, string cmdName, string str)
    {
        ConsoleParam[] args =
        {
            new ConsoleParam("[dnsZonesDir]", ConsoleService.Prompt, "dnsZonesDir: ", ConsoleService.EvalNotEmpty, null),
            new ConsoleParam("INCLUDE", ConsoleService.Prompt, "includeFile: "),
        };
        ConsoleParamValueList vl = c.ParseCommandList(cmdName, str, args);

        string dirZonesDir = vl.DefaultParam.StrValue;//@"C:\Users\yagi\Desktop\dnstest";//
        string includeFile = vl["INCLUDE"].StrValue;

        var dirList = Lfs.EnumDirectory(dirZonesDir, false);
        if (dirList.Where(x => x.IsFile && (IgnoreCaseTrim)Lfs.PathParser.GetExtension(x.Name) == ".dns").Any() == false)
        {
            // 指定されたディレクトリに *.dns ファイルが 1 つもない場合は子ディレクトリ名をソートして一番大きいものを選択する
            dirZonesDir = dirList.OrderByDescending(x => x.Name).Where(x => x.IsDirectory).Select(x => x.FullPath).First();
        }

        //Con.WriteLine($"Target directory: '{dirZonesDir}'");

        // 1. DNS ゾーンファイルを入力してユニークな FQDN レコードの一覧を生成する
        DnsFlattenUtil flat = new DnsFlattenUtil();

        foreach (FileSystemEntity ent in Lfs.EnumDirectory(dirZonesDir, true))
        {
            if (ent.IsFile)
            {
                string fn = ent.FullPath;
                //fn._Print();

                flat.InputZoneFile(Lfs.PathParser.GetFileNameWithoutExtension(fn), Lfs.ReadDataFromFile(fn).Span);
            }
        }

        // 1-2. include ファイルが指定されていればその include ファイル内の URL 内の txt をすべて取得して FQDN レコードを追加する
        if (includeFile._IsFilled())
        {
            List<string> fqdnList = new List<string>();

            Async(async () =>
            {
                var lines = await MiscUtil.ReadIncludesFileLinesAsync(includeFile, new ExpandIncludesSettings { MaxIncludes = 128 });

                foreach (var line in lines.Select(x => x._StripCommentFromLine()).Where(x => x._IsFilled()))
                {
                    string[] tokens = line._Split(StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries, ' ', '　', '\t', ',');

                    foreach (var token in tokens)
                    {
                        if (token._IsStrIP())
                        {
                            fqdnList.Add(token);
                        }
                        else if (token.StartsWith("http://", StrCmpi) || token.StartsWith("https://", StrCmpi) || token.StartsWith("ws://", StrCmpi) || token.StartsWith("ftp://", StrCmpi))
                        {
                            if (token._TryParseUrl(out var uri, out _))
                            {
                                fqdnList.Add(uri.Host);
                            }
                        }
                        else if (token._IsValidFqdn(true, true) && token._Split(StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries, ".").Length >= 2)
                        {
                            string fqdn = token._NormalizeFqdn();

                            if (fqdn.StartsWith("*."))
                            {
                                List<string> wildCandidates = new List<string>();
                                wildCandidates.Add("1-2-3-4");
                                wildCandidates.Add("wildcard-dummy1");
                                foreach (var candidate in wildCandidates)
                                {
                                    string tmp = candidate + "." + fqdn.Substring(2);
                                    fqdnList.Add(tmp);
                                }
                            }
                            else
                            {
                                fqdnList.Add(fqdn);
                            }
                        }
                    }
                }
            });

            foreach (var fqdn in fqdnList)
            {
                flat.InputFqdn(fqdn);
            }
        }

        //flat.FqdnSet.OrderBy(x => x, FqdnReverseStrComparer.Comparer)._DoForEach(x => x._Print());

        // 2. FQDN の一覧を入力して FQDN と IP アドレスのペアの一覧を生成する
        DnsIpPairGeneratorUtil pairGenerator = new DnsIpPairGeneratorUtil(100, flat.FqdnSet);

        List<SniHostnameIpAddressPair> list = pairGenerator.ExecuteAsync()._GetResult().ToList();

        List<int> ports_FastDebug = new();
        ports_FastDebug.Add(443);

        // 3. FQDN と IP アドレスのペアの一覧を入力して SSL 証明書一覧を出力する
        SslCertCollectorUtil col = new SslCertCollectorUtil(1000, list,
            new SslCertCollectorUtilSettings
            {
                //TryCount = 1,
                //PotentialHttpsPorts = portsTmp,
                DoNotIgnoreLetsEncrypt = true,
                Silent = true,
            });

        long startTick = Time.Tick64;

        IReadOnlyList<SslCertCollectorItem> ret = col.ExecuteAsync()._GetResult();

        long endTick = Time.Tick64;

        DateTimeOffset now = DtOffsetNow;
        DateTimeOffset threshold2 = now.AddDays(20);

        // 証明書が古くなっていれば警告を出す
        // IP アドレスごとに整理
        SortedDictionary<string, List<SslCertCollectorItem>> dictSoon = new SortedDictionary<string, List<SslCertCollectorItem>>(StrComparer.IpAddressStrComparer);
        SortedDictionary<string, List<SslCertCollectorItem>> dictExpired = new SortedDictionary<string, List<SslCertCollectorItem>>(StrComparer.IpAddressStrComparer);

        foreach (var item in ret.Where(x => x.IpAddress._IsFilled()))
        {
            var span = item.CertNotAfter - item.CertNotBefore;

            // もともと有効期限が 約 3 年間よりも長い証明書が登録されている場合は、意図的に登録されているオレオレ証明書であるので、更新対象としてマークしない
            if (span < Consts.Numbers.MaxCertExpireSpanTargetForUpdate)
            {
                // 古いかどうか確認
                if (item.CertNotAfter < threshold2)
                {
                    if (item.CertNotAfter < now)
                    {
                        // すでに有効期限超過
                        var o = dictExpired._GetOrNew(item.IpAddress!, () => new List<SslCertCollectorItem>());
                        o.Add(item);
                    }
                    else
                    {
                        // 有効期限 間もなく
                        var o = dictSoon._GetOrNew(item.IpAddress!, () => new List<SslCertCollectorItem>());
                        o.Add(item);
                    }
                }
            }
        }

        dictSoon.Values._DoForEach(x => x._DoSortBy(x => x.OrderBy(x => x.FriendName, StrComparer.FqdnReverseStrComparer).OrderBy(x => x.SniHostName, StrComparer.FqdnReverseStrComparer).OrderBy(x => x.Port)));

        int soonHosts = dictSoon.Count();
        int expiredHosts = dictExpired.Count();

        Con.WriteLine();

        Con.WriteLine("----");

        if (soonHosts >= 1)
        {
            Con.WriteLine($"Result: WARNING: Total {soonHosts} host's SSL certificates are expiring soon. Please check!", flags: LogFlags.Heading);
        }
        else
        {
            Con.WriteLine($"Result: OK: No host's SSL certificates are expiring soon. Good.", flags: LogFlags.Heading);
        }
        Con.WriteLine("", flags: LogFlags.Heading);

        if (expiredHosts >= 1)
        {
            Con.WriteLine($"Result: Info: Total {expiredHosts} host's SSL certificates are already expired.", flags: LogFlags.Heading);
        }
        else
        {
            Con.WriteLine($"Result: Info: No host's SSL certificates are already expired.", flags: LogFlags.Heading);
        }
        Con.WriteLine("", flags: LogFlags.Heading);


        Con.WriteLine($" Total SniHostnameIpAddressPairs: {list.Count._ToString3()}", flags: LogFlags.Heading);
        Con.WriteLine($" Total SslCertCollectorItems: {ret.Count._ToString3()}", flags: LogFlags.Heading);
        Con.WriteLine($" Took time: {(endTick - startTick)._ToTimeSpanMSecs()._ToTsStr()}", flags: LogFlags.Heading);
        Con.WriteLine();

        if (soonHosts >= 1)
        {
            CoresLib.Report_SimpleResult += $" (WARN! Expiring Soon: {soonHosts} hosts)";

            Con.WriteLine();
            Con.WriteLine("=========== WARN: Expiring Soon Hosts Summary ===========");


            int index = 0;
            foreach (var host in dictSoon)
            {
                string ip = host.Key;
                string fqdns = host.Value.Select(x => x.FriendName).Distinct(StrCmpi)._OrderByValue(StrComparer.FqdnReverseStrComparer)._Combine(", ");

                index++;
                Con.WriteLine($"Host #{index}/{dictSoon.Count}: IP = '{ip}', FQDNs = '{fqdns}'");
            }

            Con.WriteLine();
        }


        if (expiredHosts >= 1)
        {
            Con.WriteLine();
            Con.WriteLine("=========== INFO: Already Expired Hosts Summary ===========");


            int index = 0;
            foreach (var host in dictExpired)
            {
                string ip = host.Key;
                string fqdns = host.Value.Select(x => x.FriendName).Distinct(StrCmpi)._OrderByValue(StrComparer.FqdnReverseStrComparer)._Combine(", ");

                index++;
                Con.WriteLine($"Host #{index}/{dictExpired.Count}: IP = '{ip}', FQDNs = '{fqdns}'");
            }

            Con.WriteLine();
        }


        if (soonHosts >= 1)
        {
            string printStr = dictSoon._ObjectToJson();

            Con.WriteLine();
            Con.WriteLine("=========== WARN: Expiring Soon Certificates Details ===========");
            Con.WriteLine(printStr);
            Con.WriteLine();
        }


        if (expiredHosts >= 1)
        {
            string printStr = dictExpired._ObjectToJson();

            Con.WriteLine();
            Con.WriteLine("=========== INFO: Already Expired Certificates Details ===========");
            Con.WriteLine(printStr);
            Con.WriteLine();
        }

        return 0;
    }
    [ConsoleCommand(
        "TcpStressServer command",
        "TcpStressServer [port]",
        "TcpStressServer test")]
    static int TcpStressServer(ConsoleService c, string cmdName, string str)
    {
        // .NET 3.0 @ Linux, cpu 56 cores で csproj に以下を投入して TcpStressClient で大量に接続をして
        // GC で短時間フリーズ現象が発生しなければ OK!!
        // .NET 2.1, 2.2 は現象が発生する。
        // 
        // <ServerGarbageCollection>true</ServerGarbageCollection>
        // <ConcurrentGarbageCollection>true</ConcurrentGarbageCollection>

        ConsoleParam[] args = { };
        ConsoleParamValueList vl = c.ParseCommandList(cmdName, str, args);

        int port = 80;

        if (vl.DefaultParam.StrValue._IsEmpty() == false)
        {
            port = vl.DefaultParam.IntValue;
        }

        Net_Test12_AcceptLoop2(port);

        return 0;
    }

    [ConsoleCommand(
        "TcpStressClient command",
        "TcpStressClient [url]",
        "TcpStressClient test")]
    static int TcpStressClient(ConsoleService c, string cmdName, string str)
    {
        ConsoleParam[] args =
        {
                new ConsoleParam("[url]"),
            };
        ConsoleParamValueList vl = c.ParseCommandList(cmdName, str, args);

        string url = vl.DefaultParam.StrValue;

        if (url._IsEmpty())
        {
            url = "http://dn-lxd-vm2-test1/favicon.ico";
        }

        int count = 0;
        if (true)
        {
            while (true)
            {
                try
                {
                    while (true)
                    {
                        using (WebApi api = new WebApi(new WebApiOptions(new WebApiSettings { SslAcceptAnyCerts = true })))
                        {
                            count++;
                            Con.WriteLine($"Count : {count}");
                            long start = Time.HighResTick64;
                            var ret = api.SimpleQueryAsync(WebMethods.GET, url)._GetResult();
                            long end = Time.HighResTick64;

                            Con.WriteLine(end - start);
                        }
                    }
                }
                catch (Exception ex)
                {
                    ex._Debug();
                    Thread.Sleep(10);
                }
            }
            return 0;
        }


        return 0;
    }

    [ConsoleCommand(
        "Net command",
        "Net [arg]",
        "Net test")]
    static int Net(ConsoleService c, string cmdName, string str)
    {
        ConsoleParam[] args = { };
        ConsoleParamValueList vl = c.ParseCommandList(cmdName, str, args);

        TcpIpSystemHostInfo hostInfo = LocalNet.GetHostInfo(true);

        Net_Test1_PlainTcp_Client();
        return 0;

        //Net_Test2_Ssl_Client();
        //return 0;

        //Net_Test3_PlainTcp_Server();
        //return 0;

        //Net_Test3_2_PlainTcp_Server_AcceptQueue();
        //return 0;


        //while (true)
        //{
        //    try
        //    {
        //        Net_Test4_SpeedTest_Client();
        //    }
        //    catch (Exception ex)
        //    {
        //        ex.ToString()._Print();
        //    }
        //}

        //Net_Test5_SpeedTest_Server();

        //Net_Test6_DualStack_Client();

        //Net_Test7_Http_Download_Async()._GetResult();

        //Net_Test8_Http_Upload_Async()._GetResult();

        //Net_Test9_WebServer();

        //Net_Test10_SslServer();

        //Net_Test11_AcceptLoop();

        //Net_Test12_AcceptLoop2();

        //Net_Test13_WebSocketClientAsync()._GetResult();

        //Net_Test14_WebSocketClient2Async()._GetResult();

        return 0;
    }

    class SslServerTest : SslServerBase
    {
        public SslServerTest(SslServerOptions options) : base(options)
        {
        }

        protected override async Task SslAcceptedImplAsync(NetTcpListenerPort listener, SslSock sock)
        {
            using (var stream = sock.GetStream())
            using (var r = new StreamReader(stream))
            using (var w = new StreamWriter(stream))
            {
                while (true)
                {
                    string? recv = await r.ReadLineAsync();
                    if (recv == null)
                        return;

                    Con.WriteLine(recv);

                    await w.WriteLineAsync("[" + recv + "]\r\n");
                    await w.FlushAsync();
                }
            }
        }
    }

    static async Task Net_Test14_WebSocketClient2Async()
    {
        string target = "wss://echo.websocket.org";

        using (WebSocket ws = await WebSocket.ConnectAsync(target))
        {
            using (var stream = ws.GetStream())
            using (var r = new StreamReader(stream))
            using (var w = new StreamWriter(stream))
            {
                w.AutoFlush = true;

                for (int i = 1; i < 3; i++)
                {
                    string src = Str.MakeCharArray((char)('a' + (i % 25)), i * 1000);

                    Con.WriteLine(src.Length);

                    //await w.WriteLineAsync(src);

                    await stream.WriteAsync((src + "\r\n")._GetBytes_Ascii());

                    string? dst = await r.ReadLineAsync();

                    //Con.WriteLine(dst);

                    Con.WriteLine(i);

                    Debug.Assert(src == dst);
                }
            }
        }
    }

    static async Task Net_Test13_WebSocketClientAsync()
    {
        using (ConnSock sock = LocalNet.Connect(new TcpConnectParam("echo.websocket.org", 80)))
        {
            using (WebSocket ws = new WebSocket(sock))
            {
                await ws.StartWebSocketClientAsync("wss://echo.websocket.org");

                using (var stream = ws.GetStream())
                using (var r = new StreamReader(stream))
                using (var w = new StreamWriter(stream))
                {
                    w.AutoFlush = true;

                    for (int i = 1; i < 20; i++)
                    {
                        string src = Str.MakeCharArray((char)('a' + (i % 25)), i * 100);

                        Con.WriteLine(src.Length);

                        //await w.WriteLineAsync(src);

                        await stream.WriteAsync((src + "\r\n")._GetBytes_Ascii());

                        string? dst = await r.ReadLineAsync();

                        //Con.WriteLine(dst);

                        Con.WriteLine(i);

                        Debug.Assert(src == dst);
                    }
                }
            }
        }
    }

    static void Net_Test10_SslServer()
    {
        SslServerOptions opt = new SslServerOptions(LocalNet, new PalSslServerAuthenticationOptions()
        {
            AllowAnyClientCert = true,
            ServerCertificate = DevTools.TestSampleCert,
        },
        null,
        IPUtil.GenerateListeningEndPointsList(false, 444));

        using (SslServerTest svr = new SslServerTest(opt))
        {
            Con.ReadLine("Enter to quit:");
        }
    }

    static void Net_Test9_WebServer()
    {
        var cfg = new HttpServerOptions();
        using (HttpServer<TestHttpServerBuilder> svr = new HttpServer<TestHttpServerBuilder>(cfg, "Hello"))
        {
            Con.ReadLine(">");
        }
    }

    static async Task Net_Test8_Http_Upload_Async()
    {
        string url = "https://httpbin.org/anything";

        MemoryBuffer<byte> uploadData = new MemoryBuffer<byte>("Hello World"._GetBytes_Ascii());
        var stream = uploadData._AsDirectStream();
        stream._SeekToBegin();

        using (WebApi api = new WebApi())
        {
            Dbg.Where();
            var res = await api.HttpSendRecvDataAsync(new WebSendRecvRequest(WebMethods.POST, url, uploadStream: stream));
            MemoryBuffer<byte> downloadData = new MemoryBuffer<byte>();
            using (MemoryHelper.FastAllocMemoryWithUsing<byte>(4 * 1024 * 1024, out Memory<byte> tmp))
            {
                long total = 0;
                while (true)
                {
                    int r = await res.DownloadStream.ReadAsync(tmp);
                    if (r <= 0) break;

                    total += r;

                    downloadData.Write(tmp.Slice(0, r));

                    Con.WriteLine($"{total._ToString3()} / {res.DownloadContentLength.GetValueOrDefault()._ToString3()}");
                }
            }
            downloadData.Span._GetString_Ascii()._Print();
            Dbg.Where();
        }
    }

    static async Task Net_Test7_Http_Download_Async()
    {
        //string url = "https://codeload.github.com/xelerance/xl2tpd/zip/master";
        //string url = "http://speed.softether.com/001.1Mbytes.dat";
        string url = "http://speed.softether.com/008.10Tbytes.dat";

        for (int j = 0; j < 1; j++)
        {
            await using (WebApi api = new WebApi())
            {
                //for (int i = 0; ; i++)
                {
                    Dbg.Where();
                    await using var res = await api.HttpSendRecvDataAsync(new WebSendRecvRequest(WebMethods.GET, url));
                    using (MemoryHelper.FastAllocMemoryWithUsing<byte>(4 * 1024 * 1024, out Memory<byte> tmp))
                    {
                        long total = 0;
                        while (true)
                        {
                            int r = await res.DownloadStream.ReadAsync(tmp);
                            if (r <= 0) break;
                            total += r;

                            //Con.WriteLine($"{total._ToString3()} / {res.DownloadContentLength.GetValueOrDefault()._ToString3()}");
                        }
                    }
                    Dbg.Where();
                }
            }
        }

        await Task.Delay(100);
    }

    static void Net_Test6_DualStack_Client()
    {
        string hostname = "www.google.com";

        using (var tcp = LocalNet.ConnectIPv4v6Dual(new TcpConnectParam(hostname, 443, connectTimeout: 5 * 1000)))
        {
            tcp.Info.GetValue<ILayerInfoIpEndPoint>().RemoteIPAddress!.AddressFamily.ToString()._Print();

            using (SslSock ssl = new SslSock(tcp))
            {
                var sslClientOptions = new PalSslClientAuthenticationOptions()
                {
                    TargetHost = hostname,
                    ValidateRemoteCertificateProc = (cert) => { return true; },
                };

                ssl.StartSslClient(sslClientOptions);

                var st = ssl.GetStream();

                var w = new StreamWriter(st);
                var r = new StreamReader(st);

                w.WriteLine("GET / HTTP/1.0");
                w.WriteLine($"HOST: {hostname}");
                w.WriteLine();
                w.WriteLine();
                w.Flush();

                while (true)
                {
                    string? s = r.ReadLine();
                    if (s == null)
                    {
                        break;
                    }

                    Con.WriteLine(s);
                }
            }
        }
    }

    static void Net_Test5_SpeedTest_Server()
    {
        using (var server = new SpeedTestServer(LocalNet, 9821))
        {
            Con.ReadLine("Enter to stop>");
        }
    }

    static void Net_Test4_SpeedTest_Client()
    {
        string hostname = "speed.coe.ad.jp";

        CancellationTokenSource cts = new CancellationTokenSource();

        var client = new SpeedTestClient(LocalNet, LocalNet.GetIp(hostname), 9821, 32, 1 * 1000, SpeedTestModeFlag.Both, cts.Token);

        var task = client.RunClientAsync();

        //Con.ReadLine("Enter to stop>");

        ////int wait = 2000 + Util.RandSInt32_Caution() % 1000;
        ////Con.WriteLine("Waiting for " + wait);
        ////ThreadObj.Sleep(wait);

        //Con.WriteLine("Stopping...");
        //cts._TryCancelNoBlock();

        task._GetResult()._PrintAsJson();

        Con.WriteLine("Stopped.");
    }

    static void Net_Test12_AcceptLoop2(int port = 80)
    {

        new ThreadObj(param =>
        {
            ThreadObj.Current.Thread.Priority = System.Threading.ThreadPriority.Highest;
            int last = 0;
            while (true)
            {
                int value = Environment.TickCount;
                int span = value - last;
                last = value;
                long mem = mem = GC.GetTotalMemory(false);
                try
                {
                    Console.WriteLine("tick: " + span + "   mem = " + mem / 1024 / 1024 + "    sock = " + LocalNet.GetOpenedSockCount());
                }
                catch { }
                ThreadObj.Sleep(100);
            }
        });

        if (true)
        {
            NetTcpListener listener = LocalNet.CreateTcpListener(new TcpListenParam(
                    async (listener2, sock) =>
                    {
                        while (true)
                        {
                            var stream = sock.GetStream();
                            StreamReader r = new StreamReader(stream);

                            while (true)
                            {
                                string? line = await r.ReadLineAsync();

                                if (line._IsEmpty())
                                {
                                    break;
                                }
                            }
                            int segmentSize = 400;
                            int numSegments = 1000;
                            int totalSize = segmentSize * numSegments;

                            string ret =
                            $@"HTTP/1.1 200 OK
Content-Length: {totalSize}

";

                            await stream.WriteAsync(ret._GetBytes_Ascii());

                            byte[] buf = Util.Rand(numSegments);
                            for (int i = 0; i < numSegments; i++)
                            {
                                await stream.WriteAsync(buf);
                            }
                        }
                    },
                    null,
                    port));

            listener.HideAcceptProcError = true;

            ThreadObj.Sleep(-1);
        }
    }

    static bool test11_flag = false;
    static void Net_Test11_AcceptLoop()
    {
        if (test11_flag == false)
        {
            test11_flag = true;

            new ThreadObj(param =>
            {
                ThreadObj.Current.Thread.Priority = System.Threading.ThreadPriority.Highest;
                int last = 0;
                while (true)
                {
                    int value = Environment.TickCount;
                    int span = value - last;
                    last = value;
                    Console.WriteLine("tick: " + span);
                    ThreadObj.Sleep(100);
                }
            });
        }

        using (var listener = LocalNet.CreateTcpListener(new TcpListenParam(
                async (listener2, sock) =>
                {
                    while (true)
                    {
                        var stream = sock.GetStream();
                        StreamReader r = new StreamReader(stream);

                        while (true)
                        {
                            string? line = await r.ReadLineAsync();

                            if (line._IsEmpty())
                            {
                                break;
                            }
                        }
                        int segmentSize = 400;
                        int numSegments = 1000;
                        int totalSize = segmentSize * numSegments;

                        string ret =
                        $@"HTTP/1.1 200 OK
Content-Length: {totalSize}

";

                        await stream.WriteAsync(ret._GetBytes_Ascii());

                        byte[] buf = Util.Rand(numSegments);
                        for (int i = 0; i < numSegments; i++)
                        {
                            await stream.WriteAsync(buf);
                        }
                    }
                },
                null,
                80)))
        {
            Con.ReadLine(" > ");
        }
    }

    static void Net_Test3_PlainTcp_Server()
    {
        using (var listener = LocalNet.CreateTcpListener(new TcpListenParam(
                async (listener2, sock) =>
                {
                    sock.StartPCapRecorder(new PCapFileEmitter(new PCapFileEmitterOptions(new FilePath(@"c:\tmp\190611\" + Str.DateTimeToStrShortWithMilliSecs(DateTime.Now) + ".pcapng", flags: FileFlags.AutoCreateDirectory))));
                    var stream = sock.GetStream();
                    StreamWriter w = new StreamWriter(stream);
                    while (true)
                    {
                        w.WriteLine(DateTimeOffset.Now._ToDtStr(true));
                        await w.FlushAsync();
                        await Task.Delay(100);
                    }
                },
                null,
                9821)))
        {
            Con.ReadLine(">");
        }
    }

    static void Net_Test3_2_PlainTcp_Server_AcceptQueue()
    {
        using (var listener = LocalNet.CreateTcpListener(new TcpListenParam(null, null, 9821)))
        {
            Task acceptTask = TaskUtil.StartAsyncTaskAsync(async () =>
            {
                // Accept ループタスク
                while (true)
                {
                    ConnSock sock = await listener.AcceptNextSocketFromQueueUtilAsync();

                    TaskUtil.StartAsyncTaskAsync(async (obj) =>
                    {
                        using (ConnSock s = (ConnSock)obj!)
                        {
                            var stream = s.GetStream();
                            StreamWriter w = new StreamWriter(stream);
                            while (true)
                            {
                                w.WriteLine(DateTimeOffset.Now._ToDtStr(true));
                                await w.FlushAsync();
                                await Task.Delay(100);
                            }
                        }
                    }, sock)._LaissezFaire();
                }
            });

            Con.ReadLine(">");
        }
    }

    static void Net_Test2_Ssl_Client()
    {
        string hostname = "www.google.co.jp";

        using (ConnSock sock = LocalNet.Connect(new TcpConnectParam(hostname, 443)))
        {
            using (SslSock ssl = new SslSock(sock))
            {
                //ssl.StartPCapRecorder(new PCapFileEmitter(new PCapFileEmitterOptions(new FilePath(@"c:\tmp\190610\test1.pcapng", flags: FileFlags.AutoCreateDirectory), false)));
                var sslClientOptions = new PalSslClientAuthenticationOptions()
                {
                    TargetHost = hostname,
                    ValidateRemoteCertificateProc = (cert) => { return true; },
                };

                ssl.StartSslClient(sslClientOptions);

                var st = ssl.GetStream();

                var w = new StreamWriter(st);
                var r = new StreamReader(st);

                w.WriteLine("GET / HTTP/1.0");
                w.WriteLine($"HOST: {hostname}");
                w.WriteLine();
                w.WriteLine();
                w.Flush();

                while (true)
                {
                    string? s = r.ReadLine();
                    if (s == null)
                    {
                        break;
                    }

                    Con.WriteLine(s);
                }
            }
        }
    }

    static void Net_Test1_PlainTcp_Client()
    {
        for (int i = 0; i < 1; i++)
        {
            ConnSock sock = LocalNet.Connect(new TcpConnectParam("dnobori.cs.tsukuba.ac.jp", 80));

            sock.StartPCapRecorder(new PCapFileEmitter(new PCapFileEmitterOptions(new FilePath(@"c:\tmp\190610\test1.pcapng", flags: FileFlags.AutoCreateDirectory), true)));
            {
                var st = sock.GetStream();
                //sock.DisposeSafe();
                var w = new StreamWriter(st);
                var r = new StreamReader(st);

                w.WriteLine("GET /ja/ HTTP/1.0");
                w.WriteLine("HOST: dnobori.cs.tsukuba.ac.jp");
                w.WriteLine();
                w.Flush();

                while (true)
                {
                    string? s = r.ReadLine();
                    if (s == null)
                    {
                        break;
                    }

                    Con.WriteLine(s);
                }

                st.Dispose();
            }
        }
    }
}
