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
// Snmp Work Daemon Util 実際の値を取得するクラス群

#if CORES_BASIC_HTTPSERVER || CORES_BASIC_WEBAPP

#pragma warning disable CA2235 // Mark all non-serializable fields

using System;
using System.Buffers;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Serialization;
using System.Net;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

namespace IPA.Cores.Basic;

public class SnmpWorkFetcherSensor : SnmpWorkFetcherBase
{
    Sensor Sensor = null!;

    public SnmpWorkFetcherSensor(SnmpWorkHost host, SnmpWorkSensorSetting setting) : base(host, setting)
    {
    }

    protected override void InitImpl(object? param = null)
    {
        SnmpWorkSensorSetting setting = (SnmpWorkSensorSetting)param!;

        Sensor sensor = SensorsFactory.Create(setting.SensorName, setting.SensorTitle, setting.SensorArguments);

        this.Sensor = sensor;

        Sensor.StartAsync()._GetResult();
    }

    protected override async Task GetValueAsync(SortedDictionary<string, string> ret, RefInt nextPollingInterval, CancellationToken cancel = default)
    {
        await Task.Yield();

        var data = Sensor.CurrentData;

        foreach (var item in data.ValueList)
        {
            if (item.Value._IsFilled())
            {
                string key = "value";

                if (item.Key._IsFilled())
                {
                    key = item.Key.Trim();
                }

                ret.TryAdd(key, item.Value);
            }
        }
    }

    protected override async Task CleanupImplAsync(Exception? ex)
    {
        try
        {
            await this.Sensor._DisposeSafeAsync();
        }
        finally
        {
            await base.CleanupImplAsync(ex);
        }
    }
}

public class SnmpWorkFetcherBird : SnmpWorkFetcherBase
{
    bool isBirdcExists = false;
    bool isBirdc6Exists = false;

    public SnmpWorkFetcherBird(SnmpWorkHost host) : base(host)
    {
    }

    protected override void InitImpl(object? param = null)
    {
        isBirdcExists = Lfs.IsFileExists(Consts.LinuxCommands.Birdc);
        isBirdc6Exists = Lfs.IsFileExists(Consts.LinuxCommands.Birdc6);
    }

    protected override async Task GetValueAsync(SortedDictionary<string, string> ret, RefInt nextPollingInterval, CancellationToken cancel = default)
    {
        if (isBirdcExists)
        {
            await RunAndParseBirdAsync(Consts.LinuxCommands.Birdc, ret, 4, cancel)._TryAwait();
        }

        if (isBirdc6Exists)
        {
            await RunAndParseBirdAsync(Consts.LinuxCommands.Birdc6, ret, 6, cancel)._TryAwait();
        }
    }

    async Task RunAndParseBirdAsync(string birdExeName, SortedDictionary<string, string> ret, int cmdIpVersion, CancellationToken cancel = default)
    {
        var result = await EasyExec.ExecAsync(birdExeName, "show protocol all", cancel: cancel);

        string body = result.OutputStr;

        string[] lines = body._GetLines(true);

        string first = lines.FirstOrDefault()._NonNullTrim();

        if (first.StartsWith("BIRD", StringComparison.OrdinalIgnoreCase) == false)
        {
            // BIRD 文字列が見つからない
            return;
        }

        if (first.StartsWith("BIRD 1.", StringComparison.OrdinalIgnoreCase))
        {
            // BIRD 1.x
        }
        else
        {
            // BIRD 2.x or later
            cmdIpVersion = 0;
        }

        string name = "";
        string protocol = "";
        string table = "";
        int ipVer = 0;

        foreach (string line in lines)
        {
            string[] tokens = line._Split(StringSplitOptions.RemoveEmptyEntries, " ", "\t");

            if (line.StartsWith(" ") == false)
            {
                bool ok = false;
                // あるプロトコルの開始
                if (tokens.Length >= 5)
                {
                    if (tokens[3]._IsSamei("up") || tokens[3]._IsSamei("start"))
                    {
                        name = tokens[0];
                        protocol = tokens[1];
                        table = tokens[2];

                        if (cmdIpVersion != 0)
                        {
                            ipVer = cmdIpVersion;
                        }

                        ok = true;
                    }
                }

                if (ok == false)
                {
                    name = protocol = table = "";
                    ipVer = 0;
                }
            }
            else
            {
                // プロトコルに関する情報
                if (name._IsFilled())
                {
                    if (tokens.Length >= 2)
                    {
                        if (cmdIpVersion == 0)
                        {
                            if (tokens[0]._IsSamei("Channel"))
                            {
                                string verstr = tokens[1];

                                if (verstr._IsSamei("ipv4"))
                                {
                                    ipVer = 4;
                                }
                                else if (verstr._IsSamei("ipv6"))
                                {
                                    ipVer = 6;
                                }
                                else
                                {
                                    ipVer = 0;
                                }
                            }
                        }
                    }

                    if (tokens.Length >= 5)
                    {
                        if (tokens[0]._IsSamei("Routes:"))
                        {
                            int imported = tokens[1]._ToInt();
                            int exported = tokens[3]._ToInt();
                            int preferred = 0;

                            if (tokens.Length >= 7)
                            {
                                preferred = tokens[5]._ToInt();
                            }

                            if (ipVer != 0)
                            {
                                string key = $"{protocol} - {name} - IPv{ipVer}";

                                ret.TryAdd($"{key} - Import", imported.ToString());
                                ret.TryAdd($"{key} - Export", exported.ToString());
                                ret.TryAdd($"{key} - Prefer", preferred.ToString());
                            }
                        }
                    }
                }
            }
        }
    }
}

public class SnmpWorkFetcherPktQuality : SnmpWorkFetcherBase
{
    public SnmpWorkFetcherPktQuality(SnmpWorkHost host) : base(host)
    {
    }

    protected override void InitImpl(object? param = null)
    {
    }

    int numPerform = 0;

    protected override async Task GetValueAsync(SortedDictionary<string, string> ret, RefInt nextPollingInterval, CancellationToken cancel = default)
    {
        SnmpWorkSettings settings = Host.Settings;

        if (settings.PingTargets._IsSamei("none") || settings.PingTargets._IsSamei("null"))
        {
            return;
        }

        string[] pingTargets = settings.PingTargets._Split(StringSplitOptions.RemoveEmptyEntries, ",");

        KeyValueList<string, IPAddress> kvList = new KeyValueList<string, IPAddress>();

        // 名前解決
        foreach (string pingTarget in pingTargets)
        {
            cancel.ThrowIfCancellationRequested();

            try
            {
                ParseTargetString(pingTarget, out string hostname, out string alias);

                IPAddress ip = await LocalNet.GetIpAsync(hostname, cancel: cancel);

                kvList.Add(alias, ip);
            }
            catch (Exception ex)
            {
                ex._Debug();
            }
        }

        List<Task<double>> taskList = new List<Task<double>>();

        int interval = 0;
        int count = 3;

        // SpeedTest が動作中の場合は SpeedTest が完了するまで待機する
        numPerform++;
        if (numPerform >= 2)
        {
            interval = settings.PktLossIntervalMsec;
            count = settings.PktLossTryCount;

            await TaskUtil.AwaitWithPollAsync(Timeout.Infinite, 10, () => !SpeedTestClient.IsInProgress, cancel);
        }

        // 並列実行の開始
        foreach (var kv in kvList)
        {
            taskList.Add(PerformOneAsync(kv.Value, count, settings.PktLossTimeoutMsecs, interval, cancel));
        }

        // すべて終了するまで待機し、結果を整理
        for (int i = 0; i < kvList.Count; i++)
        {
            var kv = kvList[i];
            var task = taskList[i];

            double lossRate = await task._TryAwait();

            double quality = 1.0 - lossRate;

            quality = Math.Max(quality, 0.0);
            quality = Math.Min(quality, 1.0);

            ret.TryAdd($"{kv.Key}", ((double)quality * 100.0).ToString("F3"));
        }
    }

    async Task<double> PerformOneAsync(IPAddress ipAddress, int count, int timeout, int interval, CancellationToken cancel = default)
    {
        await LocalNet.SendPingAsync(ipAddress, timeout: timeout, pingCancel: cancel);

        int numOk = 0;

        for (int i = 0; i < count; i++)
        {
            cancel.ThrowIfCancellationRequested();

            var result = await LocalNet.SendPingAsync(ipAddress, timeout: timeout, pingCancel: cancel);

            if (result.Ok) numOk++;

            if (interval >= 1)
            {
                await cancel._WaitUntilCanceledAsync(interval);
            }
        }

        return (double)(count - numOk) / (double)count;
    }
}

public class SnmpWorkFetcherSpeed : SnmpWorkFetcherBase
{

    public SnmpWorkFetcherSpeed(SnmpWorkHost host) : base(host)
    {
    }

    protected override void InitImpl(object? param = null)
    {
    }

    int count = 0;

    protected override async Task GetValueAsync(SortedDictionary<string, string> ret, RefInt nextPollingInterval, CancellationToken cancel = default)
    {
        SnmpWorkSettings settings = Host.Settings;

        if (settings.SpeedTargets._IsSamei("none") || settings.SpeedTargets._IsSamei("null"))
        {
            return;
        }

        string[] speedTargets = settings.SpeedTargets._Split(StringSplitOptions.RemoveEmptyEntries, ",");

        count++;


        foreach (string target in speedTargets)
        {
            ParseTargetString(target, out string hostnameAndPort, out string alias);

            cancel.ThrowIfCancellationRequested();

            string[] tokens = hostnameAndPort._Split(StringSplitOptions.RemoveEmptyEntries, '|');
            string host;
            int port = 9821;
            host = tokens[0];
            if (tokens.Length >= 2)
            {
                port = tokens[1]._ToInt();
            }

            //long downloadBps_1 = 0;
            //long uploadBps_1 = 0;
            long downloadBps_32 = 0;
            long uploadBps_32 = 0;

            int intervalBetween = settings.SpeedIntervalsSec * 1000;
            if (count <= 1) intervalBetween = 0;

            int numTry = settings.SpeedTryCount;
            if (count <= 1) numTry = 1;

            int span = settings.SpeedSpanSec * 1000;
            if (count <= 1) span = 2000;

            try
            {
                IPAddress ipAddress = await LocalNet.GetIpAsync(host, cancel: cancel);

                try
                {
                    var downloadResult_32 = await SpeedTestClient.RunSpeedTestWithMultiTryAsync(LocalNet, ipAddress, port, 32, span, SpeedTestModeFlag.Download, numTry, intervalBetween, cancel);

                    downloadBps_32 = downloadResult_32.Select(x => x.BpsDownload).OrderByDescending(x => x).FirstOrDefault();
                }
                catch (Exception ex)
                {
                    ex._Debug();
                }
                try
                {
                    var uploadResult_32 = await SpeedTestClient.RunSpeedTestWithMultiTryAsync(LocalNet, ipAddress, port, 32, span, SpeedTestModeFlag.Upload, numTry, intervalBetween, cancel);

                    uploadBps_32 = uploadResult_32.Select(x => x.BpsUpload).OrderByDescending(x => x).FirstOrDefault();
                }
                catch (Exception ex)
                {
                    ex._Debug();
                }


                //try
                //{
                //    var downloadResult_1 = await SpeedTestClient.RunSpeedTestWithMultiTryAsync(LocalNet, ipAddress, port, 1, span, SpeedTestModeFlag.Download, numTry, intervalBetween, cancel);

                //    downloadBps_1 = downloadResult_1.Select(x => x.BpsDownload).OrderByDescending(x => x).FirstOrDefault();
                //}
                //catch (Exception ex)
                //{
                //    ex._Debug();
                //}


                //try
                //{
                //    var uploadResult_1 = await SpeedTestClient.RunSpeedTestWithMultiTryAsync(LocalNet, ipAddress, port, 1, span, SpeedTestModeFlag.Upload, numTry, intervalBetween, cancel);

                //    uploadBps_1 = uploadResult_1.Select(x => x.BpsUpload).OrderByDescending(x => x).FirstOrDefault();
                //}
                //catch (Exception ex)
                //{
                //    ex._Debug();
                //}
            }
            catch (Exception ex)
            {
                ex._Debug();
            }

            ret.TryAdd($"{alias} - RX (Mbps)", ((double)downloadBps_32 / 1000.0 / 1000.0).ToString("F3"));
            ret.TryAdd($"{alias} - TX (Mbps)", ((double)uploadBps_32 / 1000.0 / 1000.0).ToString("F3"));

            //ret.TryAdd($"{alias} - 01_RX (Mbps)", ((double)downloadBps_1 / 1000.0 / 1000.0).ToString("F3"));
            //ret.TryAdd($"{alias} - 01_TX (Mbps)", ((double)uploadBps_1 / 1000.0 / 1000.0).ToString("F3"));
        }
    }
}

public class SnmpWorkFetcherPing : SnmpWorkFetcherBase
{
    static Once FirstPing = new Once();

    public SnmpWorkFetcherPing(SnmpWorkHost host) : base(host)
    {
    }

    protected override void InitImpl(object? param = null)
    {
    }

    int numPerform = 0;

    protected override async Task GetValueAsync(SortedDictionary<string, string> ret, RefInt nextPollingInterval, CancellationToken cancel = default)
    {
        SnmpWorkSettings settings = Host.Settings;

        if (settings.PingTargets._IsSamei("none") || settings.PingTargets._IsSamei("null"))
        {
            return;
        }

        string hopsStr = "Hops";

        if (settings.HopsToTTL)
        {
            hopsStr = "TTL";
        }

        string[] pingTargets = settings.PingTargets._Split(StringSplitOptions.RemoveEmptyEntries, ",");

        numPerform++;

        foreach (string pingTarget in pingTargets)
        {
            cancel.ThrowIfCancellationRequested();

            ParseTargetString(pingTarget, out string hostname, out string alias);

            bool ok = false;

            try
            {
                IPAddress ipAddress = await LocalNet.GetIpAsync(hostname, cancel: cancel);

                if (FirstPing.IsFirstCall())
                {
                    // JIT 対策
                    try
                    {
                        await LocalNet.SendPingAsync(ipAddress, pingCancel: cancel);
                    }
                    catch { }
                }

                int numTry = 3;

                if (numPerform >= 2)
                {
                    // SpeedTest が動作中の場合は SpeedTest が完了するまで待機する
                    await TaskUtil.AwaitWithPollAsync(Timeout.Infinite, 10, () => !SpeedTestClient.IsInProgress, cancel);

                    // 試行回数を指定する
                    numTry = Math.Min(Math.Max(settings.PingNumTry, 1), 100);
                }

                SendPingReply reply = await LocalNet.SendPingAndGetBestResultAsync(ipAddress, pingCancel: cancel, numTry: numTry);

                if (reply.Ok)
                {
                    double rtt = reply.RttDouble;

                    rtt = Math.Min(rtt, 2.0);

                    int ttl = reply.Ttl;

                    bool ttl_ok = false;

                    if (ttl == 0)
                    {
                        // Use ping command to get TTL
                        try
                        {
                            var result = await EasyExec.ExecAsync(ipAddress.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 ? Consts.LinuxCommands.Ping6 : Consts.LinuxCommands.Ping,
                                $"-W 1 -c 1 {ipAddress.ToString()}",
                                cancel: cancel,
                                throwOnErrorExitCode: false);

                            string[] lines = result.OutputStr._GetLines(true);

                            foreach (string line in lines)
                            {
                                OneLineParams param = new OneLineParams(line, ' ', false);
                                string ttlStr = param._GetStrFirst("ttl");
                                if (ttlStr._IsFilled())
                                {
                                    ttl = ttlStr._ToInt();
                                    ttl_ok = true;
                                    break;
                                }
                            }

                        }
                        catch (Exception ex)
                        {
                            ex._Debug();
                        }
                    }
                    else
                    {
                        ttl_ok = true;
                    }

                    ret.TryAdd($"Time - {alias}", (rtt * 1000.0).ToString("F3"));


                    if (ttl > 128)
                    {
                        ttl -= 128;
                    }
                    else if (ttl > 64)
                    {
                        ttl -= 64;
                    }

                    int hops = 64 - ttl;

                    if (ttl_ok == false)
                    {
                        hops = 0;
                    }

                    hops._SetMax(0);
                    hops._SetMin(64);

                    ret.TryAdd($"{hopsStr} - {alias}", hops.ToString());

                    ok = true;
                }
            }
            catch (Exception ex)
            {
                ex._Debug();
            }

            if (ok == false)
            {
                ret.TryAdd($"Time - {alias}", "");
                ret.TryAdd($"{hopsStr} - {alias}", "0");
            }
        }
    }
}

public class SnmpWorkFetcherNetwork : SnmpWorkFetcherBase
{
    bool IsConnTrackOk = false;

    public SnmpWorkFetcherNetwork(SnmpWorkHost host) : base(host)
    {
    }

    protected override void InitImpl(object? param = null)
    {
        // ConnTrack コマンドが利用可能かどうか確認
        if (EasyExec.ExecAsync(Consts.LinuxCommands.ConnTrack, "-C")._TryGetResult() != default)
        {
            IsConnTrackOk = true;
        }
    }

    protected override async Task GetValueAsync(SortedDictionary<string, string> ret, RefInt nextPollingInterval, CancellationToken cancel = default)
    {
        if (IsConnTrackOk)
        {
            try
            {
                // ConnTrack
                var result = await EasyExec.ExecAsync(Consts.LinuxCommands.ConnTrack, "-C");

                string valueStr = result.OutputStr._GetFirstFilledLineFromLines();

                ret.TryAdd($"ConnTrack Sessions", valueStr._ToInt().ToString());
            }
            catch (Exception ex)
            {
                ex._Debug();
            }
        }

        if (true)
        {
            try
            {
                // Threads
                var result = EasyExec.ExecBashAsync("ps -eo nlwp | tail -n +2 | awk '{ num_threads += $1 } END { print num_threads }'")._GetResult();

                string valueStr = result.OutputStr._GetFirstFilledLineFromLines();

                ret.TryAdd($"Threads", valueStr._ToInt().ToString());
            }
            catch (Exception ex)
            {
                ex._Debug();
            }
        }

        if (true)
        {
            try
            {
                // FDs
                string result = await Lfs.ReadStringFromFileAsync(Consts.LinuxPaths.FileNr, flags: FileFlags.NoCheckFileSize);

                string valueStr = result._GetFirstFilledLineFromLines();

                string[] tokens = valueStr._Split(StringSplitOptions.RemoveEmptyEntries, " ", "\t");

                int numFd = -1;

                if (tokens.Length >= 1)
                {
                    numFd = tokens[0]._ToInt();
                }

                ret.TryAdd($"FDs", numFd.ToString());
            }
            catch (Exception ex)
            {
                ex._Debug();
            }
        }

        if (true)
        {
            try
            {
                // Sockets
                string[] lines = (await Lfs.ReadStringFromFileAsync(Consts.LinuxPaths.SockStat, flags: FileFlags.NoCheckFileSize))._GetLines();

                int numSockets = -1;
                int numTcp = -1;
                int numUdp = -1;

                foreach (string line in lines)
                {
                    string[] tokens = line._Split(StringSplitOptions.RemoveEmptyEntries, " ");

                    if (tokens.Length >= 3)
                    {
                        if (tokens[0]._IsSamei("sockets:"))
                        {
                            numSockets = tokens[2]._ToInt();
                        }

                        if (tokens[0]._IsSamei("TCP:"))
                        {
                            numTcp = tokens[2]._ToInt();
                        }

                        if (tokens[0]._IsSamei("UDP:"))
                        {
                            numUdp = tokens[2]._ToInt();
                        }
                    }
                }

                if (numSockets >= 0 && numTcp >= 0 && numUdp >= 0)
                {
                    ret.TryAdd($"Sockets", numSockets.ToString());
                    ret.TryAdd($"TCP", numTcp.ToString());
                    ret.TryAdd($"UDP", numUdp.ToString());
                }
            }
            catch (Exception ex)
            {
                ex._Debug();
            }
        }
    }
}

public class SnmpWorkFetcherDisk : SnmpWorkFetcherBase
{
    public SnmpWorkFetcherDisk(SnmpWorkHost host) : base(host)
    {
    }

    protected override void InitImpl(object? param = null) { }

    protected override async Task GetValueAsync(SortedDictionary<string, string> ret, RefInt nextPollingInterval, CancellationToken cancel = default)
    {
        var result = await EasyExec.ExecAsync(Consts.LinuxCommands.Df, "-a -B1");

        string[] lines = result.OutputStr._GetLines();

        string[] ignores = "/dev /run /sys /snap /tmp /proc"._Split(StringSplitOptions.RemoveEmptyEntries, " ");

        foreach (string line in lines)
        {
            string[] tokens = line._Split(StringSplitOptions.RemoveEmptyEntries, ' ', '\t');

            if (tokens.Length >= 6)
            {
                string totalStr = tokens[1];
                string availableStr = tokens[3];
                string path = tokens[5];

                if (totalStr != "-" && availableStr != "-")
                {
                    if (path.StartsWith("/") && ignores.Where(x => path.StartsWith(x, StringComparison.OrdinalIgnoreCase)).Any() == false)
                    {
                        long total = totalStr._ToLong();
                        long available = availableStr._ToLong();

                        if (total > 0 && available >= 0)
                        {
                            available = Math.Min(available, total);
                            ret.TryAdd($"available - {path}", NormalizeDoubleValue(((double)available * 100.0 / (double)total).ToString("F3")));
                        }
                    }
                }
            }
        }
    }
}

public class SnmpWorkFetcherMemory : SnmpWorkFetcherBase
{
    public SnmpWorkFetcherMemory(SnmpWorkHost host) : base(host)
    {
    }

    protected override void InitImpl(object? param = null) { }

    protected override async Task GetValueAsync(SortedDictionary<string, string> ret, RefInt nextPollingInterval, CancellationToken cancel = default)
    {
        var result = await EasyExec.ExecAsync(Consts.LinuxCommands.Free, "-b -w");

        string[] lines = result.OutputStr._GetLines();

        List<string> headers = new List<string>();

        KeyValueList<string, string> dataList = new KeyValueList<string, string>();

        foreach (string line in lines)
        {
            string[] tokens = line._Split(StringSplitOptions.RemoveEmptyEntries, ' ', '\t');

            if (tokens.Length >= 2)
            {
                if (headers.Count == 0)
                {
                    if (tokens[0]._IsSamei("total"))
                    {
                        // ヘッダ行
                        foreach (string token in tokens)
                        {
                            headers.Add(token);
                        }
                    }
                }
                else
                {
                    // データ行
                    if (tokens[0]._IsSamei("Mem:"))
                    {
                        for (int i = 1; i < tokens.Length; i++)
                        {
                            if (headers.Count >= (i - 1))
                            {
                                dataList.Add(headers[i - 1], tokens[i]);
                            }
                        }
                    }
                }
            }
        }

        // total
        long total = dataList._GetStrFirst("total", "-1")._ToLong();
        long available = dataList._GetStrFirst("available", "-1")._ToLong();

        if (total >= 0 && available >= 0)
        {
            available = Math.Min(available, total);
            ret.TryAdd($"available", NormalizeDoubleValue(((double)available * 100.0 / (double)total).ToString("F3")));
        }
    }
}

public class SnmpWorkFetcherTemperature : SnmpWorkFetcherBase
{
    bool IsSensorsCommandOk = false;

    readonly KeyValueList<string, string> ThermalFiles = new KeyValueList<string, string>();

    public SnmpWorkFetcherTemperature(SnmpWorkHost host) : base(host)
    {
    }

    protected override void InitImpl(object? param = null)
    {
        // sensors コマンドが利用可能かどうか確認
        if (EasyExec.ExecAsync(Consts.LinuxCommands.Sensors, "-u")._TryGetResult() != default)
        {
            IsSensorsCommandOk = true;
        }

        // /sys/class/thermal/ から取得可能な値一覧を列挙
        FileSystemEntity[]? dirList = null;
        try
        {
            dirList = Lfs.EnumDirectory(Consts.LinuxPaths.SysThermal);
        }
        catch { }

        if (dirList != null)
        {
            foreach (var dir in dirList)
            {
                string fileName = Lfs.PathParser.Combine(dir.FullPath, "temp");

                if (Lfs.IsFileExists(fileName))
                {
                    try
                    {
                        Lfs.ReadStringFromFile(fileName);

                        ThermalFiles.Add(dir.Name, fileName);
                    }
                    catch
                    {
                    }
                }
            }
        }
    }

    protected override async Task GetValueAsync(SortedDictionary<string, string> ret, RefInt nextPollingInterval, CancellationToken cancel = default)
    {
        // sys/class/thermal/ から温度取得
        foreach (var thermalFile in this.ThermalFiles)
        {
            string value = (await Lfs.ReadStringFromFileAsync(thermalFile.Value))._GetFirstFilledLineFromLines();

            double d = ((double)value._ToInt()) / 1000.0;

            ret.TryAdd($"{thermalFile.Key}", NormalizeDoubleValue(d.ToString("F3")));
        }

        if (IsSensorsCommandOk)
        {
            try
            {
                // Sensors コマンドで温度取得
                var result = await EasyExec.ExecAsync(Consts.LinuxCommands.Sensors, "-u", cancel: cancel);

                string[] lines = result.OutputStr._GetLines(true);

                string groupName = "";
                string fieldName = "";

                foreach (string line2 in lines)
                {
                    string line = line2.TrimEnd();

                    if (line.StartsWith(" ") == false && line._InStr(":") == false)
                    {
                        // グループ名
                        groupName = line.Trim();
                    }
                    else if (line.StartsWith(" ") == false && line.EndsWith(":"))
                    {
                        // 値名
                        fieldName = line.Substring(0, line.Length - 1);
                    }
                    else if (line.StartsWith(" ") && line._GetKeyAndValue(out string key, out string value, ":"))
                    {
                        // 値サブ名 : 値
                        key = key.Trim();
                        value = value.Trim();

                        if (key.EndsWith("_input", StringComparison.OrdinalIgnoreCase))
                        {
                            ret.TryAdd($"{groupName}/{fieldName}", NormalizeDoubleValue(value));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ex._Debug();
            }
        }
    }
}

public class SnmpWorkFetcherHttpKeyValue : SnmpWorkFetcherBase
{
    public string Url { get; }

    readonly WebApi Web;

    public SnmpWorkFetcherHttpKeyValue(SnmpWorkHost host, string url) : base(host)
    {
        try
        {
            this.Url = url;

            this.Web = new WebApi();
        }
        catch
        {
            this._DisposeSafe();
            throw;
        }
    }

    protected override async Task GetValueAsync(SortedDictionary<string, string> ret, RefInt nextPollingInterval, CancellationToken cancel = default)
    {
        if (this.Web == null)
        {
            nextPollingInterval.Set(100);
            return;
        }

        var webret = await this.Web.SimpleQueryAsync(WebMethods.GET, this.Url, cancel);

        string body = webret.Data._GetString_UTF8();

        string[] lines = body._GetLines(true);

        foreach (string line in lines)
        {
            string line2 = line.Trim();
            if (line2.StartsWith("#") == false)
            {
                if (line2._GetKeyAndValue(out string key, out string value, ":"))
                {
                    key = key.Trim();
                    value = value.Trim();

                    if (key._IsFilled() && value._IsFilled())
                    {
                        ret.TryAdd(key, value);
                    }
                }
            }
        }

        DoNothing();
    }

    protected override void InitImpl(object? param = null)
    {
    }

    protected override void DisposeImpl(Exception? ex)
    {
        try
        {
            this.Web._DisposeSafe();
        }
        catch
        {
            base.DisposeImpl(ex);
        }
    }
}

#endif

