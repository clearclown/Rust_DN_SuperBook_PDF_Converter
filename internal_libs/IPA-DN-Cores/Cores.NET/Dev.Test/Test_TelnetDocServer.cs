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

namespace IPA.TestDev;

public class TelnetDocServerDaemonApp : AsyncServiceWithMainLoop
{
    NetTcpListener? RawListener = null;
    NetTcpListener? SslListener = null;

    public TelnetDocServerDaemonApp()
    {
        try
        {
            this.StartMainLoop(MainLoopAsync);
        }
        catch
        {
            this.DisposeAsync();
            throw;
        }
    }

    readonly HiveData<HiveKeyValue> Settings = Hive.LocalAppSettingsEx["TelnetDocServerAccessCounter"];

    public class Hatsugen
    {
        public string Line = "";
        public string SrcHost = "";
        public DateTimeOffset Dt;
        public string Ip = "";
        public int Port;
        public string Fqdn = "";
        public bool FakeFqdn;
        public long MessageId;
    }

    public class AccessPropa
    {
        public string OriginalUrl = "";
        public string SrcHost = "";
        public string Ip = "";
        public int Port;
        public string Fqdn = "";
        public bool FakeFqdn;
    }

    public class Propa
    {
        public string OriginalBody = "";
        public string OriginalUrl = "";
        public string Body = "";
        public string SrcHost = "";
        public string Ip = "";
        public int Port;
        public string Fqdn = "";
        public bool FakeFqdn;
    }

    readonly ConcurrentHashSet<ConcurrentQueue<Hatsugen>> HatsugenQueueList = new ConcurrentHashSet<ConcurrentQueue<Hatsugen>>();

    readonly ConcurrentHashSet<string> propaHistory = new ConcurrentHashSet<string>();

    readonly RateLimiter<string> propaRateLimiter1 = new RateLimiter<string>(new RateLimiterOptions(burst: 1.0, limitPerSecond: 1.0 / 100.0, mode: RateLimiterMode.NoPenalty));
    readonly RateLimiter<string> propaRateLimiter2 = new RateLimiter<string>(new RateLimiterOptions(burst: 100.0, limitPerSecond: 1.0 / (24.0 * 3600.0), mode: RateLimiterMode.NoPenalty));

    readonly RateLimiter<string> hatsugenRateLimiter = new RateLimiter<string>(new RateLimiterOptions(burst: 10.0, limitPerSecond: 0.2, mode: RateLimiterMode.Penalty));

    Propa? CurrentPropa = null;

    async Task ResponseMainAsync(CancellationToken cancel, NetTcpListenerPort listener, Stream st, string replaceStrEncoding, string replaceStrVersion, Encoding encoding, ConnSock sock)
    {
        long lastRecvTick = 0;

        RefBool noTalk = false;
        RefBool noBody = false;

        var myHatsugenRecvQueue = new ConcurrentQueue<Hatsugen>();
        var myHatsugenSentQueue = new ConcurrentQueue<Hatsugen>();

        HatsugenQueueList.Add(myHatsugenRecvQueue);

        var recvTask = TaskUtil.StartAsyncTaskAsync(async () =>
        {
            try
            {
                var lineReader = new BinaryLineReader(st);

                while (true)
                {
                    var bytes = await lineReader.ReadSingleLineAsync(1000, cancel);

                    if (bytes == null)
                    {
                        break;
                    }

                    string line = "";

                    if (bytes.HasValue)
                    {
                        var clientInfo = sock.EndPointInfo;

                        var maskedFqdn = await LocalNet.DnsResolver.GetHostNameOrIpAsync(clientInfo.RemoteIP, cancel);

                        string originalFqdn = maskedFqdn;

                        var tokens = maskedFqdn._Split(StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries, '.');

                        List<string> tokens2 = new List<string>();

                        for (int i = 0; i < tokens.Length; i++)
                        {
                            string tmp1 = tokens[i];
                            if (i == 0)
                            {
                                StringBuilder sb1 = new StringBuilder();
                                foreach (char c in tmp1)
                                {
                                    if (c == '-' || c == ':')
                                    {
                                        sb1.Append(c);
                                    }
                                    else
                                    {
                                        sb1.Append('*');
                                    }
                                }
                                tmp1 = sb1.ToString();
                            }
                            tokens2.Add(tmp1);
                        }

                        maskedFqdn = Str.CombineFqdn(tokens2.ToArray());

                        bool isFakeFqdn = true;

                        try
                        {
                            var ipFromFqdnList = await LocalNet.DnsResolver.GetIpAddressListSingleStackAsync(originalFqdn, DnsResolverQueryType.A, cancel: cancel);

                            if (ipFromFqdnList != null)
                            {
                                foreach (var ip1 in ipFromFqdnList)
                                {
                                    if (ip1.ToString() == clientInfo.RemoteIP?.ToString())
                                    {
                                        isFakeFqdn = false;
                                    }
                                }
                            }
                        }
                        catch
                        {
                        }

                        line = Str.DecodeStringAutoDetect(bytes.Value.Span, out _, true).Trim();

                        line = Str.ShiftJisEncoding.GetBytes(line)._GetString_ShiftJis(true).Trim();

                        StringBuilder sb = new StringBuilder();

                        foreach (var c in line)
                        {
                            if (Str.IsPrintable(c))
                            {
                                sb.Append(c);
                            }
                        }

                        line = sb.ToString().Trim();

                        if (line.StartsWith("http://") || line.StartsWith("https://"))
                        {
                            try
                            {
                                if (line._InStri("?") == false)
                                {
                                    if (line._TryParseUrl(out var uri, out var qs))
                                    {
                                        if (uri.UserInfo._IsEmpty())
                                        {
                                            AccessPropa a = new AccessPropa
                                            {
                                                FakeFqdn = isFakeFqdn,
                                                Fqdn = originalFqdn,
                                                SrcHost = maskedFqdn,
                                                Ip = clientInfo!.RemoteIP?.ToString() ?? "",
                                                Port = clientInfo!.RemotePort,
                                                OriginalUrl = line,
                                            };

                                            a._PostAccessLog("AccessPropa");

                                            var httpResult = await SimpleHttpDownloader.DownloadAsync(line, WebMethods.GET,
                                                options: new WebApiOptions(new WebApiSettings { SslAcceptAnyCerts = true, MaxRecvSize = 1_000_000, AllowAutoRedirect = false, }, doNotUseTcpStack: true),
                                                cancel: cancel);

                                            if (httpResult.ContentType._InStri("text/plain"))
                                            {
                                                string downloadedText = Str.DecodeStringAutoDetect(httpResult.Data, out _, true);

                                                var downloadedLines = downloadedText._GetLines();

                                                string contentsForHash = "";

                                                bool ok = true;

                                                List<string> tmpLinesList = new List<string>();

                                                bool lastLineIsEmpty = true;

                                                string[] youbi =
                                                {
                                                    "日", "月", "火", "水", "木", "金", "土",
                                                };

                                                string dtStr = $"令和 {(DtNow.Year - 2019 + 1)} 年 {DtNow.Month} 月 {DtNow.Day} 日 " + youbi[(int)DtNow.DayOfWeek] + "曜 " + DtNow.ToString("HH:mm");

                                                string fakeStr = "";

                                                if (isFakeFqdn)
                                                {
                                                    fakeStr = " (※ 贋作 DNS 逆引の疑い)";
                                                }

                                                string headerStr = $"{(char)7}★☆★☆ 臨時ニユ{(char)7}ース 緊急放送 ☆★☆★{(char)7} {dtStr}\r\n★ たった今、{maskedFqdn}{fakeStr} 君よりプロパガンダ演説状が発せられましたから満場孜々として謹聴いたしましょう ★";

                                                string[] sepLines2 = ConsoleService.SeparateStringByWidth(headerStr, 76);
                                                tmpLinesList.Add("");
                                                tmpLinesList.Add("");
                                                foreach (var line4 in sepLines2)
                                                {
                                                    tmpLinesList.Add(" " + line4);
                                                }

                                                tmpLinesList.Add("");

                                                tmpLinesList.Add(" ＝＝＝ 大演説の開闢 ＝＝＝");
                                                tmpLinesList.Add("");

                                                bool isPropa = false;

                                                int totalChars = 0;

                                                int numLines = 0;
                                                foreach (var line2 in downloadedLines)
                                                {
                                                    string line3 = line2.TrimEnd();
                                                    if (line3._GetBytes_ShiftJis()._GetString_ShiftJis(true) != line3)
                                                    {
                                                        ok = false;
                                                    }
                                                    else
                                                    {
                                                        if (isPropa == false && line3.Trim()._IsSamei("propa"))
                                                        {
                                                            isPropa = true;
                                                        }
                                                        else
                                                        {
                                                            line3 = line3._NormalizeSoftEther();
                                                            string[] sepLines = ConsoleService.SeparateStringByWidth(line3, 76);
                                                            foreach (var line4 in sepLines)
                                                            {
                                                                if (lastLineIsEmpty && line4._IsEmpty()) { }
                                                                else
                                                                {
                                                                    numLines++;
                                                                    if (numLines > 30)
                                                                    {
                                                                        break;
                                                                    }
                                                                    totalChars += line4.Length;
                                                                    if (totalChars > 1200)
                                                                    {
                                                                        break;
                                                                    }
                                                                    tmpLinesList.Add(" " + line4);
                                                                    lastLineIsEmpty = line4._IsEmpty();
                                                                    contentsForHash += "\r\n" + (line4._ReplaceStr(" ", ""));
                                                                }
                                                            }
                                                        }
                                                    }
                                                }

                                                if (isPropa == false)
                                                {
                                                    ok = false;
                                                }

                                                if (downloadedText.Length >= 1)
                                                {
                                                    if (((double)downloadedText._GetWidth() / (double)downloadedText.Length) < 1.3)
                                                    {
                                                        ok = false;
                                                    }
                                                }
                                                else
                                                {
                                                    ok = false;
                                                }

                                                if (lastLineIsEmpty == false)
                                                {
                                                    tmpLinesList.Add("");
                                                }

                                                tmpLinesList.Add(" ＝＝＝ 大演説の終焉 ＝＝＝");
                                                tmpLinesList.Add("");
                                                tmpLinesList.Add("");

                                                if (ok)
                                                {
                                                    string propaBody = tmpLinesList._Combine(Str.NewLine_Str_Windows);

                                                    string propaHash = Str.HashStrSHA1(contentsForHash)._GetHexString();

                                                    if (this.propaHistory.Add(propaHash))
                                                    {
                                                        if (propaRateLimiter1.TryInput(clientInfo.RemoteIP!, out _))
                                                        {
                                                            if (propaRateLimiter2.TryInput(clientInfo.RemoteIP!, out _))
                                                            {
                                                                Propa p = new Propa
                                                                {
                                                                    OriginalBody = downloadedText,
                                                                    FakeFqdn = isFakeFqdn,
                                                                    Fqdn = originalFqdn,
                                                                    SrcHost = maskedFqdn,
                                                                    Ip = clientInfo!.RemoteIP?.ToString() ?? "",
                                                                    Port = clientInfo!.RemotePort,
                                                                    Body = propaBody,
                                                                    OriginalUrl = line,
                                                                };

                                                                p._PostAccessLog("PostPropa");

                                                                this.CurrentPropa = p;
                                                            }
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                ex._Debug();
                            }
                        }
                        else if (line._InStri("nobody"))
                        {
                            noBody.Set(true);
                        }
                        else if (line._InStri("body"))
                        {
                            noBody.Set(false);
                        }
                        else if (line._InStri("notalk"))
                        {
                            noTalk.Set(true);
                        }
                        else if (line._InStri("talk"))
                        {
                            noTalk.Set(false);
                        }
                        else if (noTalk == false &&
                            line._IsFilled() &&
                            Str.ShiftJisEncoding.GetBytes(line).Length != line.Length &&
                            line._InStri(": ") == false &&
                            line._InStri("??") == false &&
                            line._InStri("<script>") == false &&
                            line._IsSamei("q") == false &&
                            line.StartsWith("get", StringComparison.OrdinalIgnoreCase) == false &&
                            line.StartsWith("post", StringComparison.OrdinalIgnoreCase) == false &&
                            line.StartsWith("head", StringComparison.OrdinalIgnoreCase) == false &&
                            line._InStri("exit") == false &&
                            line._InStri("quit") == false &&
                            line._InStri("logout") == false)
                        {
                            lastRecvTick = Time.Tick64;

                            //Str.NormalizeString(ref line, true, false, true, true);
                            line = line._NormalizeSoftEther(true);

                            line = line.Replace("「", "『");
                            line = line.Replace("」", "』");

                            if (hatsugenRateLimiter.TryInput(clientInfo.RemoteIP ?? "", out var rateLimitEntry))
                            {

                                long messageId = 0;
                                await Settings.AccessDataAsync(true, async k =>
                                {
                                    long c = k.GetSInt64("MessageId");

                                    c++;

                                    k.SetSInt64("MessageId", c);

                                    messageId = c;

                                    await Task.CompletedTask;
                                });

                                Hatsugen item = new Hatsugen
                                {
                                    Dt = DtOffsetNow,
                                    Line = line,
                                    SrcHost = maskedFqdn,
                                    Ip = clientInfo.RemoteIP?.ToString() ?? "",
                                    Port = clientInfo.RemotePort,
                                    FakeFqdn = isFakeFqdn,
                                    Fqdn = originalFqdn,
                                    MessageId = messageId,
                                };

                                item._PostAccessLog("Hatsugen");

                                var queueArray = HatsugenQueueList.ToArray();

                                foreach (var q in queueArray)
                                {
                                    if (q != myHatsugenRecvQueue)
                                    {
                                        if (q.Count >= 100)
                                        {
                                            q.TryDequeue(out _);
                                        }
                                        q.Enqueue(item);
                                    }
                                }

                                myHatsugenSentQueue.Enqueue(item);
                            }

                            //rateLimitEntry._Debug();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ex._Debug();
            }
        });

        try
        {
            long access_counter = 0;
            await Settings.AccessDataAsync(true, async k =>
            {
                long c = k.GetSInt64("Counter");

                c++;

                k.SetSInt64("Counter", c);

                access_counter = c;

                await Task.CompletedTask;
            });

            string counter2 = access_counter.ToString()._Normalize(false, false, true);
            string counter1 = access_counter.ToString().ToString()._Normalize(false, false, false);

            string fn = Env.AppRootDir._CombinePath("TelnetBody.txt");

            st.WriteTimeout = 60 * 1000;
            st.ReadTimeout = Timeout.Infinite;

            StreamWriter w = new StreamWriter(st, encoding);
            w.NewLine = Str.CrLf_Str;
            w.AutoFlush = true;

            bool exit = false;

            lastRecvTick = Time.Tick64;

            w.Write((char)0x1b + "[0m");

            Ref<Propa?> myReadingPropa = new Ref<Propa?>(this.CurrentPropa);

            while (exit == false)
            {
                DateTimeOffset lastUpdate = Util.ZeroDateTimeOffsetValue;

                try
                {
                    var info = await Lfs.GetFileMetadataAsync(fn);
                    lastUpdate = info.LastWriteTime ?? Util.ZeroDateTimeOffsetValue;
                }
                catch { }

                var dt = lastUpdate.LocalDateTime;

                string[] youbi =
                {
                    "日", "月", "火", "水", "木", "金", "土",
                };

                var updateStr = dt.ToString("yyyy/MM/dd") + " (" + youbi[(int)dt.DayOfWeek] + ") " + dt.ToString("HH:mm");

                string body = Lfs.ReadStringFromFile(fn);

                body = body._ReplaceStr("_COUNTER2_", counter2);
                body = body._ReplaceStr("_COUNTER1_", counter1);
                body = body._ReplaceStr("_ENCODING_", replaceStrEncoding);
                body = body._ReplaceStr("_VERSION_", replaceStrVersion);
                body = body._ReplaceStr("_UPDATE_", updateStr);

                StringWriter tmp1 = new StringWriter();

                tmp1.NewLine = Str.CrLf_Str;

                tmp1.WriteLine();

                foreach (var line in body._GetLines())
                {
                    tmp1.WriteLine(" " + line);
                }

                body = tmp1.ToString();

                long recvTimeout = 1000L * 3600 * 24 * 365;// 3 * 60 * 60 * 1000;

                foreach (char c in body)
                {
                    cancel.ThrowIfCancellationRequested();

                    if (Time.Tick64 >= (lastRecvTick + recvTimeout))
                    {
                        exit = true;
                        break;
                    }

                    if (noBody)
                    {
                        break;
                    }

                    await SendQueuedLinesIfExistsAsync(w, myHatsugenSentQueue, false, true, false);

                    await w.WriteAsync(c);
                    if (c == '\n')
                    {
                        if (noTalk == false)
                        {
                            await SendQueuedLinesIfExistsAsync(w, myHatsugenRecvQueue, false, false, false);

                            await SendPropaAsync(w, myHatsugenSentQueue, myHatsugenRecvQueue, recvTimeout);
                        }
                        else
                        {
                            myHatsugenRecvQueue.Clear();
                        }
                    }

                    await Task.Delay(Util.RandSInt15() % 100);
                }

                long sleepEndTick = Time.Tick64 + 3 * 60 * 1000;

                bool lastNoBody = noBody;

                while (Time.Tick64 <= sleepEndTick)
                {
                    cancel.ThrowIfCancellationRequested();

                    if (Time.Tick64 >= (lastRecvTick + recvTimeout) || recvTask.IsCompleted || sock.IsCanceled)
                    {
                        exit = true;
                        break;
                    }

                    if (noBody == false && lastNoBody)
                    {
                        break;
                    }

                    await SendQueuedLinesIfExistsAsync(w, myHatsugenSentQueue, true, true, noBody);

                    if (noTalk == false)
                    {
                        await SendQueuedLinesIfExistsAsync(w, myHatsugenRecvQueue, true, false, noBody);

                        await SendPropaAsync(w, myHatsugenSentQueue, myHatsugenRecvQueue, recvTimeout);
                    }
                    else
                    {
                        myHatsugenRecvQueue.Clear();
                    }

                    await Task.Delay(Util.RandSInt15() % 200);
                }

                //Memory<byte> recvBuf = new byte[1];

                //lastRecvTick = Time.Tick64;

                //while (true)
                //{
                //    if (recvTask.IsCompleted || sock.IsCanceled || (Time.Tick64 >= (lastRecvTick + 5 * 60 * 1000)))
                //    {
                //        break;
                //    }

                //    await SendQueuedLinesIfExistsAsync(w, hatsugenReadQueue);

                //    await Task.Delay(Util.RandSInt15() % 256);
                //}
            }

            async Task SendPropaAsync(StreamWriter dst, ConcurrentQueue<Hatsugen> myHatsugenSentQueue, ConcurrentQueue<Hatsugen> myHatsugenRecvQueue, long recvTimeout)
            {
                var currentPropa2 = this.CurrentPropa;

                if (myReadingPropa.Value != currentPropa2 && currentPropa2 != null)
                {
                    myReadingPropa.Set(currentPropa2);

                    string propaBody = myReadingPropa.Value!.Body;

                    foreach (char c2 in propaBody)
                    {
                        cancel.ThrowIfCancellationRequested();

                        if (Time.Tick64 >= (lastRecvTick + recvTimeout))
                        {
                            exit = true;
                            break;
                        }

                        await SendQueuedLinesIfExistsAsync(w, myHatsugenSentQueue, false, true, false);

                        await w.WriteAsync(c2);

                        if (c2 == '\n')
                        {
                            if (noTalk == false)
                            {
                                await SendQueuedLinesIfExistsAsync(w, myHatsugenRecvQueue, false, false, false);
                            }
                            else
                            {
                                myHatsugenRecvQueue.Clear();
                            }
                        }

                        await Task.Delay(Util.RandSInt15() % 100);
                    }
                }
            }

            async Task SendQueuedLinesIfExistsAsync(StreamWriter dst, ConcurrentQueue<Hatsugen> queue, bool isInSilentMode, bool isMyself, bool noColor)
            {
                while (queue.TryDequeue(out var item))
                {
                    await PrintHatsugenAsync(item);
                }

                async Task PrintHatsugenAsync(Hatsugen item)
                {
                    StringWriter w = new StringWriter();

                    w.NewLine = Str.CrLf_Str;

                    //w.WriteLine();

                    string[] youbi =
                    {
                        "日", "月", "火", "水", "木", "金", "土",
                    };

                    string dtStr = item.Dt.ToString("MM/dd") + " (" + youbi[(int)item.Dt.DayOfWeek] + ") " + item.Dt.ToString("HH:mm:ss");

                    string fakeStr = "";

                    if (item.FakeFqdn)
                    {
                        fakeStr = " (※ 贋作 DNS 逆引の疑い)";
                    }

                    string anataStr = "";

                    if (isMyself)
                    {
                        anataStr = " 〈＊あなた様＊〉";
                    }

                    w.WriteLine("");

                    if (noColor == false)
                    {
                        w.Write((char)0x1b + "[0m");

                        w.Write((char)0x1b + "[1m");

                        if (isMyself)
                        {
                            w.Write((char)0x1b + "[32m");
                        }
                        else
                        {
                            w.Write((char)0x1b + "[31m");
                        }
                    }

                    string line = $">> 「 {item.Line} 」(チャット放話 - {dtStr} by {item.SrcHost}{fakeStr} 君{anataStr}) <<";

                    string[] lines = ConsoleService.SeparateStringByWidth(line, 77);

                    foreach (var line2 in lines)
                    {
                        w.WriteLine(line2);
                    }

                    if (noColor == false)
                    {
                        w.Write((char)0x1b + "[0m");
                    }

                    if (isMyself == false)
                    {
                        await w.WriteAsync((char)7);
                    }

                    if (isInSilentMode == false)
                    {
                        w.WriteLine("");
                    }

                    await dst.WriteAsync(w.ToString());
                }
            }
        }
        finally
        {
            try
            {
                st.Close();
            }
            catch { }
            HatsugenQueueList.Remove(myHatsugenRecvQueue);
            await recvTask._TryAwait(true);
        }
    }

    async Task MainLoopAsync(CancellationToken cancel = default)
    {
        await Task.CompletedTask;

        this.RawListener = LocalNet.CreateTcpListener(new TcpListenParam(async (listener, sock) =>
        {
            await using var st = sock.GetStream();

            await ResponseMainAsync(cancel, listener, st, "Shift_JIS Encoding to view this page", "Pure TELNET (TCP Port 23) edition", Str.ShiftJisEncoding, sock);
        }, ports: new int[] { 23 }, rateLimiterConfigName: "telnet_raw"));

        this.SslListener = LocalNet.CreateTcpListener(new TcpListenParam(async (listener, sock) =>
        {
            var cert = GlobalCertVault.GetGlobalCertVault().CertificateStoreSelector("_dummy_", false);

            await using SslSock sslSock = new SslSock(sock);

            await sslSock.StartSslServerAsync(new PalSslServerAuthenticationOptions(cert.X509Certificate, true, null), cancel);

            await using var st = sslSock.GetStream();

            await ResponseMainAsync(cancel, listener, st, "UTF-8 Encoding to view this page    ", "TELNET over TLS/SSL (TCP Port 992) secure edition", Str.Utf8Encoding, sock);
        }, ports: new int[] { 992 }, rateLimiterConfigName: "telnet_ssl"));
    }

    protected async override Task CleanupImplAsync(Exception? ex)
    {
        try
        {
            if (RawListener != null)
            {
                await RawListener.DisposeAsync(ex);
            }
            if (SslListener != null)
            {
                await SslListener.DisposeAsync(ex);
            }
        }
        finally
        {
            await base.CleanupImplAsync(ex);
        }
    }
}

class TelnetDocServerDaemon : Daemon
{
    TelnetDocServerDaemonApp? app = null;

    public TelnetDocServerDaemon() : base(new DaemonOptions("TelnetDocServerDaemon", "TelnetDocServerDaemon Service", true))
    {
    }

    protected override async Task StartImplAsync(DaemonStartupMode startupMode, object? param)
    {
        Con.WriteLine("TelnetDocServerDaemon: Starting...");

        app = new TelnetDocServerDaemonApp();

        await Task.CompletedTask;

        try
        {
            Con.WriteLine("TelnetDocServerDaemon: Started.");
        }
        catch
        {
            await app._DisposeSafeAsync();
            app = null;
            throw;
        }
    }

    protected override async Task StopImplAsync(object? param)
    {
        Con.WriteLine("TelnetDocServerDaemon: Stopping...");

        if (app != null)
        {
            await app.DisposeWithCleanupAsync();

            app = null;
        }

        Con.WriteLine("TelnetDocServerDaemon: Stopped.");
    }
}

partial class TestDevCommands
{
    [ConsoleCommand(
        "Start or stop the TelnetDocServerDaemon daemon",
        "TelnetDocServerDaemon [command]",
        "Start or stop the TelnetDocServerDaemon daemon",
        @"[command]:The control command.

[UNIX / Windows common commands]
start        - Start the daemon in the background mode.
stop         - Stop the running daemon in the background mode.
show         - Show the real-time log by the background daemon.
test         - Start the daemon in the foreground testing mode.

[Windows specific commands]
winstart     - Start the daemon as a Windows service.
winstop      - Stop the running daemon as a Windows service.
wininstall   - Install the daemon as a Windows service.
winuninstall - Uninstall the daemon as a Windows service.")]
    static int TelnetDocServerDaemon(ConsoleService c, string cmdName, string str)
    {
        return DaemonCmdLineTool.EntryPoint(c, cmdName, str, new TelnetDocServerDaemon(), new DaemonSettings());
    }

}

