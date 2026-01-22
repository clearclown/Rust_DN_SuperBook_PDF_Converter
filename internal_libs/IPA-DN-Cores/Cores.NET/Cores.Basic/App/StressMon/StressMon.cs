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
// StressMon Server

#if CORES_BASIC_HTTPSERVER || CORES_BASIC_WEBAPP

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

namespace IPA.Cores.Basic
{
    public static partial class CoresConfig
    {
        public static partial class StressMonServer
        {
            public static readonly Copenhagen<int> MaxHistoryPerServer = 5;

            public static readonly Copenhagen<int> MaxBodySize = 100_000;
        }
    }
}

namespace IPA.Cores.Basic.StressMon
{
    public class ReportedItem
    {
        public DateTimeOffset Timestamp { get; }
        public string Hostname { get; }
        public string IpAddress { get; }
        public string Body { get; }

        public ReportedItem(DateTimeOffset timestamp, string hostname, string ipAddress, string body)
        {
            Timestamp = timestamp;
            Hostname = hostname._NonNullTrim();
            IpAddress = ipAddress._NonNullTrim();
            Body = body._NonNull()._NormalizeCrlf();
        }
    }

    public class StressMonServerCore : CgiHandlerBase
    {
        public readonly StressMonServer Server;

        readonly CriticalSection<StressMonServerCore> LockObj = new CriticalSection<StressMonServerCore>();

        public Dictionary<string, Queue<ReportedItem>> ReportTable = new Dictionary<string, Queue<ReportedItem>>(StrComparer.IgnoreCaseTrimComparer);

        public void AddReport(ReportedItem r)
        {
            lock (LockObj)
            {
                var q = ReportTable._GetOrNew(r.Hostname, () => new Queue<ReportedItem>());

                bool changed = true;

                var last = q.LastOrDefault();
                if (last != null)
                {
                    if (last.Body._IsSame(r.Body))
                    {
                        changed = false;
                    }
                }

                if (changed)
                {
                    q.Enqueue(r);

                    while (q.Count > CoresConfig.StressMonServer.MaxHistoryPerServer)
                    {
                        q.Dequeue();
                    }
                }
            }
        }

        public class HostSummary
        {
            public string HostName = "";
            public DateTimeOffset LastAlive;
            public DateTimeOffset LastChanged;
            public string IpAddress = "";
        }

        public void Reset()
        {
            lock (LockObj)
            {
                this.ReportTable.Clear();
            }
        }

        public string GenerateReportString()
        {
            StringWriter w = new StringWriter();

            lock (LockObj)
            {
                List<HostSummary> summaryList = new List<HostSummary>();

                foreach (var hostname in this.ReportTable.Keys)
                {
                    var list = this.ReportTable[hostname].ToList();

                    HostSummary h = new HostSummary
                    {
                        HostName = hostname,
                        LastAlive = list.LastOrDefault()?.Timestamp ?? Util.ZeroDateTimeOffsetValue,
                        LastChanged = Util.ZeroDateTimeOffsetValue,
                        IpAddress = list.LastOrDefault()?.IpAddress ?? "",
                    };

                    h.LastChanged = list.Max(x => x.Timestamp);

                    summaryList.Add(h);
                }

                w.WriteLine($"Stress Mon: {DateTimeOffset.Now._ToDtStr(true, DtStrOption.All)}");
                w.WriteLine($"Build: {Dbg.GetCurrentGitCommitId()}, Boot at {Env.BootTime._ToDtStr()}");
                w.WriteLine();
                w.WriteLine($"合計サーバー数: {summaryList.Count}");
                w.WriteLine();
                w.WriteLine();

                w.WriteLine($"■ サーバー数: {summaryList.Count} - 最終変更時刻が古い順");
                var servers1 = summaryList.OrderBy(x => x.LastChanged).ThenBy(x => x.HostName, StrComparer.IgnoreCaseTrimComparer).ToList();
                for (int i = 0; i < servers1.Count; i++)
                {
                    var svr = servers1[i];
                    w.WriteLine($"{i + 1}/{servers1.Count}: Hostname = {svr.HostName}, IP = {svr.IpAddress}, Last Changed = {svr.LastChanged._ToDtStr(zeroDateTimeStr: "none")}, Last Alive = {svr.LastAlive._ToDtStr(zeroDateTimeStr: "none")}");
                }
                w.WriteLine();
                w.WriteLine();

                w.WriteLine($"■ サーバー数: {summaryList.Count} - 最終報告時刻が古い順");
                var servers2 = summaryList.OrderBy(x => x.LastAlive).ThenBy(x => x.HostName, StrComparer.IgnoreCaseTrimComparer).ToList();
                for (int i = 0; i < servers2.Count; i++)
                {
                    var svr = servers2[i];
                    w.WriteLine($"{i + 1}/{servers2.Count}: Hostname = {svr.HostName}, IP = {svr.IpAddress}, Last Changed = {svr.LastChanged._ToDtStr(zeroDateTimeStr: "none")}, Last Alive = {svr.LastAlive._ToDtStr(zeroDateTimeStr: "none")}");
                }
                w.WriteLine();
                w.WriteLine();


                w.WriteLine($"■ サーバー数: {summaryList.Count} - 詳細すべて");
                w.WriteLine();

                var servers3 = summaryList.OrderBy(x => x.HostName, StrComparer.IgnoreCaseTrimComparer).ToList();
                for (int i = 0; i < servers3.Count; i++)
                {
                    var svr = servers3[i];

                    w.WriteLine($"===== {i + 1}/{servers2.Count}: Hostname = {svr.HostName}, IP = {svr.IpAddress}, Last Changed = {svr.LastChanged._ToDtStr(zeroDateTimeStr: "none")}, Last Alive = {svr.LastAlive._ToDtStr(zeroDateTimeStr: "none")} =====");

                    int index = 0;
                    int count = this.ReportTable[svr.HostName].Count;
                    foreach (var hist in this.ReportTable[svr.HostName].OrderByDescending(x=>x.Timestamp))
                    {
                        w.WriteLine($"--- History {(index++) + 1}/{count} of Hostname = {svr.HostName} ({hist.Timestamp._ToDtStr(zeroDateTimeStr: "none")}) ---");
                        w.WriteLine(hist.Body._NormalizeCrlf(CrlfStyle.CrLf, true));
                    }

                    w.WriteLine();
                    w.WriteLine();
                }
            }

            return w.ToString();
        }

        public StressMonServerCore(StressMonServer host)
        {
            this.Server = host;
        }

        protected override void InitActionListImpl(CgiActionList noAuth, CgiActionList reqAuth)
        {
            try
            {
                noAuth.AddAction("/", WebMethodBits.GET | WebMethodBits.HEAD, async (ctx) =>
                {
                    await Task.CompletedTask;

                    var hostname = ctx.QueryString._GetStrFirst("hostname");
                    var body = ctx.QueryString._GetStrFirst("body");

                    if (hostname._IsFilled() && body._IsFilled())
                    {
                        ReportedItem r = new ReportedItem(DateTimeOffset.Now, hostname, ctx.ClientIpAddress.ToString(), body);

                        this.AddReport(r);

                        return new HttpStringResult("ok");
                    }

                    return new HttpStringResult(GenerateReportString());
                });

                noAuth.AddAction("/reset", WebMethodBits.GET | WebMethodBits.HEAD, async (ctx) =>
                {
                    await Task.CompletedTask;
                    this.Reset();

                    return new HttpStringResult(GenerateReportString());
                });

                noAuth.AddAction("/", WebMethodBits.POST, async (ctx) =>
                {
                    await Task.CompletedTask;
                    var hostname = ctx.QueryString._GetStrFirst("hostname");

                    if (hostname._IsFilled())
                    {
                        ReportedItem r = new ReportedItem(DateTimeOffset.Now, hostname, ctx.ClientIpAddress.ToString(), await ctx.Request._RecvStringContentsAsync(CoresConfig.StressMonServer.MaxBodySize, cancel: ctx.Cancel));

                        this.AddReport(r);

                        return new HttpStringResult("ok");
                    }

                    return new HttpStringResult("error");
                });
            }
            catch
            {
                this._DisposeSafe();
                throw;
            }
        }
    }

    public class StressMonServer : AsyncService
    {
        readonly CgiHttpServer Cgi;

        public StressMonServer()
        {
            try
            {
                this.Cgi = new CgiHttpServer(new StressMonServerCore(this), new HttpServerOptions()
                {
                    AutomaticRedirectToHttpsIfPossible = false,
                    DisableHiveBasedSetting = true,
                    DenyRobots = true,
                    UseGlobalCertVault = false,
                    LocalHostOnly = false,
                    HttpPortsList = new int[] { Consts.Ports.StressMonServerPort }.ToList(),
                    HttpsPortsList = new List<int>(),
                    UseKestrelWithIPACoreStack = false,
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
                await this.Cgi._DisposeSafeAsync();
            }
            finally
            {
                await base.CleanupImplAsync(ex);
            }
        }
    }
}

#endif

