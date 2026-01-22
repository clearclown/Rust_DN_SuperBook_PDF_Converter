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
using System.Net;
using System.Net.Sockets;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using IPA.Cores.Basic.Legacy;
using static IPA.Cores.Globals.Basic;

namespace IPA.Cores.Basic.SEPingerService;

// 移植 2022/07/13 from Pinger\current\Pinger\PingerEngine\Pinger.cs

public class Target
{
    public readonly string HostName;
    public readonly int[] Ports;

    public Target(string str)
    {
        StrToken t = new StrToken(str);

        if (t.NumTokens >= 1)
        {
            HostName = t[0];

            int numPorts = (int)t.NumTokens - 1;
            int i;

            List<int> ports = new List<int>();

            for (i = 0; i < numPorts; i++)
            {
                string s = t[(uint)i + 1];
                bool tcp_check = false;

                if (s.StartsWith("@"))
                {
                    tcp_check = true;
                    s = s.Substring(1);
                }

                int port = Str.StrToInt(s);

                if (port != 0 && tcp_check)
                {
                    port += 100000;
                }

                ports.Add(port);
            }

            ports.Sort();

            Ports = ports.ToArray();
        }
        else
        {
            throw new FormatException();
        }
    }
}

// 設定
public class Config
{
    public readonly string SmtpServerName;
    public readonly int SmtpPort;
    public readonly string SmtpUsername, SmtpPassword;
    public readonly int SmtpNumTry;

    public readonly string SmtpServerName2;
    public readonly int SmtpPort2;
    public readonly string SmtpUsername2, SmtpPassword2;
    public readonly int SmtpNumTry2;

    public readonly string SubjectPrefix;

    public readonly string SmtpFrom;
    public readonly string SmtpFromAlive;
    public readonly string[] SmtpToList;
    public readonly bool SaveLog;
    public readonly int Interval;
    public readonly int Timeout;
    public readonly Target[] TargetList;
    public readonly Target[] TargetListLastOkHost;
    public readonly Target[] TargetListLastNgHost;
    public readonly int NumErrors;
    public readonly bool SendAliveMessage;
    public readonly int TcpListen;
    public readonly string DefaultOptions;

    public readonly string DnsServer;

    public readonly string Footer;

    public List<KeyValuePair<string, string>> SuffixReplaceList = new List<KeyValuePair<string, string>>();

    public readonly bool TcpSendData;

    public const int MinInterval = 1000;
    public const int MinTimeout = 1000;

    public Config(string configFileName, List<string> last_ok_hosts)
    {
        ReadIni ini = new ReadIni(configFileName, true);

        TcpListen = (int)ini["TcpListen"].IntValue;

        SmtpServerName = ini["SmtpServerName"].StrValue;
        SmtpPort = (int)ini["SmtpPort"].IntValue;
        if (SmtpPort == 0)
        {
            SmtpPort = 25;
        }
        SmtpNumTry = (int)ini["SmtpNumTry"].IntValue;
        if (SmtpNumTry == 0)
        {
            SmtpNumTry = 1;
        }
        SmtpUsername = ini["SmtpUsername"].StrValue;
        SmtpPassword = ini["SmtpPassword"].StrValue;

        SmtpServerName2 = ini["SmtpServerName2"].StrValue;
        SmtpPort2 = (int)ini["SmtpPort2"].IntValue;
        if (SmtpPort2 == 0)
        {
            SmtpPort2 = 25;
        }
        SmtpNumTry2 = (int)ini["SmtpNumTry2"].IntValue;
        if (SmtpNumTry2 == 0)
        {
            SmtpNumTry2 = 1;
        }
        SmtpUsername2 = ini["SmtpUsername2"].StrValue;
        SmtpPassword2 = ini["SmtpPassword2"].StrValue;

        SmtpFrom = ini["SmtpFrom"].StrValue;
        SmtpFromAlive = ini["SmtpFromAlive"].StrValue;

        SubjectPrefix = ini["SubjectPrefix"].StrValue;

        DnsServer = ini["DnsServer"].StrValue;

        string s = ini["SmtpTo"].StrValue;
        SmtpToList = new StrToken(s).Tokens;

        SaveLog = ini["SaveLog"].BoolValue;
        SendAliveMessage = ini["SendAliveMessage"].BoolValue;
        TcpSendData = ini["TcpSendData"].BoolValue;

        Interval = Math.Max(MinInterval, (int)ini["Interval"].IntValue * 1000);
        Timeout = Math.Max(MinTimeout, (int)ini["Timeout"].IntValue * 1000);
        NumErrors = Math.Max(1, (int)ini["NumErrors"].IntValue);

        DefaultOptions = ini["DefaultOptions"].StrValue;
        Footer = ini["Footer"].StrValue;

        string[] keys = ini.GetKeys();

        List<Target> o = new List<Target>();
        List<Target> last_ok = new List<Target>();
        List<Target> last_ng = new List<Target>();

        foreach (string key in keys)
        {
            if (key.StartsWith("Target", StringComparison.CurrentCultureIgnoreCase))
            {
                string str = ini[key].StrValue;

                Target t = new Target(str);

                o.Add(t);

                bool last_ok_flag = false;

                if (last_ok_hosts != null)
                {
                    if (last_ok_hosts.Contains(t.HostName))
                    {
                        last_ok_flag = true;
                    }
                }

                if (last_ok_flag == false)
                {
                    last_ng.Add(t);
                }
                else
                {
                    last_ok.Add(t);
                }
            }

            if (key.StartsWith("SuffixReplace", StringComparison.CurrentCultureIgnoreCase))
            {
                string str = ini[key].StrValue;

                char[] sps = { ' ', '\t' };

                string[] tokens = str.Split(sps, StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length == 2)
                {
                    this.SuffixReplaceList.Add(new KeyValuePair<string, string>(tokens[0], tokens[1]));
                }
            }
        }

        TargetList = o.ToArray();
        TargetListLastOkHost = last_ok.ToArray();
        TargetListLastNgHost = last_ng.ToArray();
    }

    public string ConvertHostnameToOkHostname(string str)
    {
        foreach (KeyValuePair<string, string> t in this.SuffixReplaceList)
        {
            if (str.EndsWith(t.Key, StringComparison.CurrentCultureIgnoreCase))
            {
                str = str.Substring(0, str.Length - t.Key.Length) + t.Value;
            }
        }

        return str;
    }

    public static string NormalizeHostNameForSort(string str)
    {
        str = str.Trim();

        if (Domain.IsIPAddress(str) == false)
        {
            StrToken t = new StrToken(str, ".");
            List<string> o = new List<string>();
            string[] strs = t.Tokens;
            Array.Reverse(strs);

            int i;
            string ret = "";
            for (i = 0; i < strs.Length; i++)
            {
                ret += strs[i];

                if (i != (strs.Length - 1))
                {
                    ret += ".";
                }
            }

            return ret;
        }
        else
        {
            return str;
        }
    }
}

// 送信の実行
public class PingerTask
{
    //public readonly Event EndEvent;
    public readonly string TargetHost;
    public readonly string TargetHostForOk;
    public readonly string TargetHostNormalized;
    public readonly int TargetPort;
    public readonly int Timeout;
    public readonly bool TcpSendData;
    public readonly string DefaultOptions;
    public readonly DnsResolver DnsClient;

    object lockObj;

    ThreadObj thread;

    bool finished;
    public bool Finished
    {
        get
        {
            lock (lockObj)
            {
                return finished;
            }
        }
    }

    bool ok;
    public bool Ok
    {
        get
        {
            lock (lockObj)
            {
                return ok;
            }
        }

        set
        {
            lock (lockObj)
            {
                ok = value;
            }
        }
    }

    public bool TcpNg;

    // 実行開始
    public PingerTask(DnsResolver dnsClient, string targetHost, string targetHostForOk, int targetPort, int timeout, bool tcp_send_data, string defaultOptions)
    {
        //EndEvent = new Event(true);
        DnsClient = dnsClient;
        TargetHost = targetHost;
        TargetHostForOk = targetHostForOk;
        TargetHostNormalized = Config.NormalizeHostNameForSort(targetHost);
        TargetPort = targetPort;
        Timeout = timeout;
        TcpSendData = tcp_send_data;
        DefaultOptions = defaultOptions._NonNull();

        finished = false;
        ok = false;
        lockObj = new object();

        ThreadObj t = new ThreadObj(new ThreadProc(pingerThreadProc), null);

        thread = t;
    }

    // 実行スレッド
    void pingerThreadProc(object? param)
    {
        string optionsStr = this.DefaultOptions; // まずデフォルトのオプション文字列を読む

        string hostname = TargetHost;
        if (hostname._InStri("@"))
        {
            if (hostname._GetKeyAndValue(out string hostname2, out string tmp, "@"))
            {
                hostname = hostname2;

                // オプション文字列が個別に指定されていた場合は、それを先に付ける。同じ項目について上書きをすることになる、
                optionsStr = tmp._NonNull() + "," + optionsStr;
            }
        }

        // オプション文字列のパース
        var options = QueryStringList.Parse(optionsStr, splitChar: ',', trimKeyAndValue: true);

        // IP アドレスバージョンの決定
        AllowedIPVersions allowedIPVersions = AllowedIPVersions.All;

        string ipVerStr = options._GetStrFirst("ipver");
        if (ipVerStr._InStr("4") && ipVerStr._InStr("6") == false)
        {
            allowedIPVersions = AllowedIPVersions.IPv4;
        }
        else if (ipVerStr._InStr("6") && ipVerStr._InStr("4") == false)
        {
            allowedIPVersions = AllowedIPVersions.IPv6;
        }

        bool preferV6 = false;

        string ipperfer = options._GetStrFirst("ipprefer");
        if (ipperfer._InStri("6"))
        {
            preferV6 = true;
        }

        IPAddress GetIpAddress(string hostname, AllowedIPVersions allowedIPVersions)
        {
            var ip = hostname._ToIPAddress(allowedIPVersions, true);

            if (ip != null)
            {
                return ip;
            }

            return this.DnsClient.GetIpAddressAsync(hostname, allowedIPVersions, preferV6, noCache: true)._GetResult();
        }

        if (TargetPort == 0)
        {
            // ping の実行
            try
            {
                IPAddress ip = GetIpAddress(hostname, allowedIPVersions);

                SendPingReply ret = SendPing.Send(ip, null, Timeout);

                lock (lockObj)
                {
                    ok = ret.Ok;
                    finished = true;
                }
            }
            catch
            {
                lock (lockObj)
                {
                    ok = false;
                    finished = true;
                }
            }

            Console.Write(ok ? "+" : "-");
        }
        else
        {
            int port = TargetPort;
            bool tcp_check = false;

            if (port >= 100000)
            {
                port -= 100000;
                tcp_check = true;
            }

            // TCP Connect の実行
            try
            {
                if (tcp_check == false)
                {
                    IPAddress ip = GetIpAddress(hostname, allowedIPVersions);

                    Sock s = Sock.Connect(ip.ToString(), port, Timeout, true, true);

                    try
                    {
                        if (TcpSendData)
                        {
                            s.SetTimeout(Timeout);

                            long end_tick = Time.Tick64 + (long)Timeout;
                            int num_packets = 0;

                            while (true)
                            {
                                if (Time.Tick64 > end_tick)
                                {
                                    break;
                                }

                                if (num_packets >= 16)
                                {
                                    break;
                                }

                                byte[] data = new byte[1];
                                data[0] = (byte)'a';
                                if (s.SendAll(data) == false)
                                {
                                    break;
                                }

                                ThreadObj.Sleep(1);
                                num_packets++;
                            }
                        }

                        s.Disconnect();
                    }
                    catch
                    {
                    }
                }
                else
                {
                    IPAddress ip = GetIpAddress(hostname, allowedIPVersions);

                    if (tcp_check_do(ip.ToString(), port, Timeout) == false)
                    {
                        throw new ApplicationException();
                    }
                }

                lock (lockObj)
                {
                    ok = true;
                    finished = true;
                }
            }
            catch
            {
                lock (lockObj)
                {
                    ok = false;
                    finished = true;
                }
            }

            Console.Write(ok ? "*" : "-");
        }

        //EndEvent.Set();
    }

    static bool tcp_check_do(string host, int port, int timeout)
    {
        Sock s = Sock.Connect(host, port, timeout, true, true);
        try
        {
            byte[] d = new byte[1];
            d[0] = (byte)'a';

            int i;
            int num = 3;

            for (i = 0; i < num; i++)
            {
                if (s.SendAll(d) == false)
                {
                    return false;
                }

                if (i <= (num - 1))
                {
                    ThreadObj.Sleep(300);
                }
            }

            return true;
        }
        finally
        {
            s.Disconnect();
        }
    }

    // 終了待機
    public void WaitForEnd()
    {
        thread.WaitForEnd();
    }
}

// 結果クラス
public class PingerResult
{
    public readonly string HostName;
    public readonly string HostNameNormalized;
    public readonly string HostNameForOk;
    List<int> okPorts;
    List<int> badPorts;
    DateTime firstErrorTime;
    public bool LastOkStatus = true;

    public bool Ok
    {
        get
        {
            return (badPorts.Count == 0);
        }
    }

    public PingerResult(string hostName, string hostNameForOk)
    {
        HostName = hostName;
        HostNameForOk = hostNameForOk;
        HostNameNormalized = Config.NormalizeHostNameForSort(hostName);
        okPorts = new List<int>();
        badPorts = new List<int>();
        firstErrorTime = new DateTime(0);
    }

    public void ClearPort()
    {
        badPorts.Clear();
        okPorts.Clear();
    }

    public void AddPort(int port, bool ok)
    {
        if (ok == false)
        {
            badPorts.Add(port);
        }
        else
        {
            okPorts.Add(port);
        }
    }

    public static string GetPortListString(int[] ports)
    {
        string ret = "";
        int i;
        for (i = 0; i < ports.Length; i++)
        {
            int port = ports[i];
            bool tcp_check = false;

            if (port >= 100000)
            {
                port -= 100000;
                tcp_check = true;
            }

            string s = port.ToString();

            if (ports[i] == 0)
            {
                s = "ping";
            }

            if (tcp_check)
            {
                s = "@" + s;
            }

            ret += s;

            if (i != (ports.Length - 1))
            {
                ret += ",";
            }
        }
        return ret;
    }

    public string GetString(bool ok_mode)
    {
        string hostname = HostName;

        if (ok_mode)
        {
            hostname = HostNameForOk;
        }

        string ret = "[" + hostname + "]\n";
        okPorts.Sort();
        badPorts.Sort();

        if (badPorts.Count >= 1)
        {
            ret += "NG: " + GetPortListString(badPorts.ToArray()) + "\n";

            if (firstErrorTime.Ticks == 0)
            {
                firstErrorTime = DateTime.Now;
            }
        }
        else
        {
            firstErrorTime = new DateTime(0);
        }

        if (okPorts.Count >= 1)
        {
            ret += "OK: " + GetPortListString(okPorts.ToArray()) + "\n";
        }

        if (firstErrorTime.Ticks != 0)
        {
            ret += "発生: " + firstErrorTime._ToDtStr() + "\n";
        }

        return ret;
    }

    public byte[] GetHash()
    {
        string s = "";
        foreach (int p in okPorts)
        {
            s += p.ToString();
        }
        foreach (int p in badPorts)
        {
            s += p.ToString();
        }

        return Str.HashStr(s);
    }
}

// 結果一覧クラス
public class PingerResults
{
    SortedList<string, PingerResult> o;

    public PingerResults()
    {
        o = new SortedList<string, PingerResult>();
    }

    public void Merge(PingerTask[] tasks)
    {
        List<string> deleteList = new List<string>();

        // 現在リストにあってタスク一覧にないものをすべて削除する
        foreach (PingerResult res in o.Values)
        {
            bool b = false;
            foreach (PingerTask t in tasks)
            {
                if (t.TargetHostNormalized == res.HostNameNormalized)
                {
                    b = true;
                    break;
                }
            }
            if (b == false)
            {
                deleteList.Add(res.HostNameNormalized);
            }
        }
        foreach (string deleteStr in deleteList)
        {
            o.Remove(deleteStr);
        }

        // すべてのポートをクリアする
        foreach (PingerResult res in o.Values)
        {
            res.ClearPort();
        }

        // 結果を追加する
        foreach (PingerTask t in tasks)
        {
            PingerResult res;
            if (o.ContainsKey(t.TargetHostNormalized) == false)
            {
                res = new PingerResult(t.TargetHost, t.TargetHostForOk);

                o.Add(t.TargetHostNormalized, res);
            }
            else
            {
                res = o[t.TargetHostNormalized];
            }

            res.AddPort(t.TargetPort, t.Ok);
        }
    }

    public int NumOk
    {
        get
        {
            int num = 0;
            foreach (PingerResult res in o.Values)
            {
                if (res.Ok == true)
                {
                    num++;
                }
            }
            return num;
        }
    }

    public int NumErrorNew
    {
        get
        {
            int num = 0;
            foreach (PingerResult res in o.Values)
            {
                if (res.Ok == false && res.LastOkStatus == true)
                {
                    num++;
                }
            }
            return num;
        }
    }

    public int NumErrorOld
    {
        get
        {
            int num = 0;
            foreach (PingerResult res in o.Values)
            {
                if (res.Ok == false && res.LastOkStatus == false)
                {
                    num++;
                }
            }
            return num;
        }
    }

    public int NumError
    {
        get
        {
            int num = 0;
            foreach (PingerResult res in o.Values)
            {
                if (res.Ok == false)
                {
                    num++;
                }
            }
            return num;
        }
    }

    public string GetString()
    {
        string ret = "";

        if (NumErrorNew >= 1)
        {
            ret += "■ 新規障害発生のホストの一覧\n";
            foreach (PingerResult res in o.Values)
            {
                if (res.Ok == false && res.LastOkStatus)
                {
                    ret += res.GetString(false) + "\n";
                }
            }
        }

        if (NumErrorOld >= 1)
        {
            ret += "■ 障害継続中のホストの一覧\n";
            foreach (PingerResult res in o.Values)
            {
                if (res.Ok == false && res.LastOkStatus == false)
                {
                    ret += res.GetString(false) + "\n";
                }
            }
        }

        if (NumOk >= 1)
        {
            ret += "■ 正常なホストの一覧\n";
            foreach (PingerResult res in o.Values)
            {
                if (res.Ok == true)
                {
                    ret += res.GetString(true) + "\n";
                }
            }
        }

        foreach (PingerResult res in o.Values)
        {
            res.LastOkStatus = res.Ok;
        }

        return Str.NormalizeCrlf(ret);
    }

    public byte[] GetHash()
    {
        Buf b = new Buf();

        foreach (PingerResult res in o.Values)
        {
            if (res.Ok == false)
            {
                b.Write(res.GetHash());
            }
        }

        return b.ByteData;
    }
}

// Pinger クラス
public class Pinger
{
    public const string ConfigFileName = "@Pinger.config";
    public const string LogDirName = "@Log";
    PingerResults res;
    byte[] lastHash;
    FileLogger logger = null!;
    DateTime lastDate = new DateTime();
    Listener tl = null!;

    void tcpListenerAcceptProc(Listener listener, Sock sock, object param)
    {
    }

    // コンストラクタ
    public Pinger()
    {
        res = new PingerResults();
        lastHash = new byte[1];

        try
        {
            Config cfg;

            try
            {
                cfg = new Config(ConfigFileName, null!);
            }
            catch (Exception ex)
            {
                ex._Error();

                Environment.Exit(-1);
                throw;
            }

            int port = cfg.TcpListen;
            if (port != 0)
            {
                tl = new Listener(port, tcpListenerAcceptProc, null!);
            }
        }
        catch (Exception ex)
        {
            ex._Error();
        }
    }

    // 前回何らかの応答があったホストの一覧
    public List<string> LastOkHostList = new List<string>();
    public List<string> CurrentOkHostList = new List<string>();

    // 継続実行
    public void ExecEndless()
    {
        while (true)
        {
            try
            {
                CurrentOkHostList = new List<string>();

                ExecOnce();

                LastOkHostList = CurrentOkHostList;
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
                Kernel.SleepThread(10 * 1000);
            }
        }
    }

    DnsResolver DnsClient = null!;

    // 1 回実行
    public void ExecOnce()
    {
        this.DnsClient = null!;

        Config config = new Config(ConfigFileName, this.LastOkHostList);

        if (config.DnsServer._IsEmpty())
        {
            this.DnsClient = DnsResolver.CreateDnsResolverIfSupported(
                new DnsResolverSettings(null, DnsResolverFlags.DisableCache | DnsResolverFlags.UdpOnly | DnsResolverFlags.UseSystemDnsClientSettings, 3000, 2));
        }
        else
        {
            this.DnsClient = DnsResolver.CreateDnsResolverIfSupported(
                new DnsResolverSettings(null, DnsResolverFlags.DisableCache | DnsResolverFlags.UdpOnly, 3000, 2,
                dnsServersList: config.DnsServer._Split(StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries, ",", " ").Select(x => new IPEndPoint(x._ToIPAddress()!, Consts.Ports.Dns))));
        }

        try
        {
            PingerTask[] tasks = ExecTasksMulti(config);

            string prefix = config.SubjectPrefix + " ";
            if (config.SubjectPrefix._IsEmpty()) prefix = "";

            DateTime now = DateTime.Now;
            if (lastDate.Day != now.Day)
            {
                if (lastDate.Ticks != 0)
                {
                    if (config.SendAliveMessage)
                    {
                        string text = "報告日時: " + DateTime.Now.ToString() + "\n\n";
                        Mail(config, config.SmtpFromAlive, prefix + "生きています", text);
                    }
                }

                lastDate = now;
            }

            res.Merge(tasks);

            byte[] hash = res.GetHash();

            string str = res.GetString();

            // ログ保存
            if (config.SaveLog)
            {
                try
                {
                    string saveStr = Str.NormalizeCrlf(str).Replace(Env.NewLine, "\\n");

                    if (logger == null)
                    {
                        logger = new FileLogger(LogDirName);
                        logger.Flush = true;
                    }

                    logger.Write(saveStr);
                }
                catch
                {
                }
            }

            if (Util.MemEquals(lastHash, hash) == false)
            {
                lastHash = hash;

                string okngStr = $"NG:{res.NumError} OK:{res.NumOk}";

                var progamInfo = Env.GenerateMyProgramAndEnvironmentReport()._GetResult();

                // メール送信
                string mailStr = "【" + okngStr + "】\r\n" + "報告日時: " + DateTime.Now._ToDtStr() + "\r\n\r\n" + str + "\r\n\r\n" +
                    $"Pinger version: {Env.BuildTimeStamp._ToDtStr()}\r\n" +
                    $"Pinger machine: {Env.DnsFqdnHostName}\r\n\r\n" +
                    $"Program Info:\r\n" +
                    progamInfo._ObjectToJson() + "\r\n" +
                    config.Footer._NonNull()._DecodeCEscape() + "\r\n";

                mailStr = Str.NormalizeCrlf(mailStr);

                Mail(config, config.SmtpFrom, prefix + okngStr, mailStr);

                //Console.WriteLine("----------");
                //Console.WriteLine(mailStr);

                Console.WriteLine("Mail Sent.");
            }
        }
        finally
        {
            this.DnsClient._DisposeSafe();
            this.DnsClient = null!;
        }
    }

    // メール送信
    public void Mail(Config config, string from, string subject, string body)
    {
        foreach (string to in config.SmtpToList)
        {
            int i;

            bool ok = false;

            for (i = 0; i < config.SmtpNumTry; i++)
            {
                SendMail sm = new SendMail(config.SmtpServerName, SendMailVersion.Ver2_With_NetMail, config.SmtpUsername, config.SmtpPassword);

                sm.SmtpPort = config.SmtpPort;

                if (sm.Send(from, to, subject, body, true))
                {
                    ok = true;
                    break;
                }
            }

            if (ok == false)
            {
                for (i = 0; i < config.SmtpNumTry2; i++)
                {
                    SendMail sm = new SendMail(config.SmtpServerName2, SendMailVersion.Ver2_With_NetMail, config.SmtpUsername2, config.SmtpPassword2);

                    sm.SmtpPort = config.SmtpPort2;

                    if (sm.Send(from, to, subject, body, true))
                    {
                        break;
                    }
                }
            }
        }
    }

    // タスクを実行
    public PingerTask[] ExecTasks(Config config, PingerTask[] baseTaskList)
    {
        double startTime = Time.NowHighResDouble;
        List<PingerTask> taskList = new List<PingerTask>();
        List<PingerTask> taskList2 = new List<PingerTask>();

        int num = 0;

        if (config.TcpSendData)
        {
            Con.WriteLine("\n  starting TCP send data...\n");

            foreach (Target target in config.TargetListLastOkHost)
            {
                foreach (int port in target.Ports)
                {
                    if (port != 0)
                    {
                        PingerTask task = new PingerTask(this.DnsClient, target.HostName, config.ConvertHostnameToOkHostname(target.HostName), port, config.Timeout, true, config.DefaultOptions);

                        taskList2.Add(task);
                    }
                }
            }
            foreach (Target target in config.TargetListLastNgHost)
            {
                foreach (int port in target.Ports)
                {
                    if (port != 0)
                    {
                        PingerTask task = new PingerTask(this.DnsClient, target.HostName, config.ConvertHostnameToOkHostname(target.HostName), port, config.Timeout, true, config.DefaultOptions);

                        taskList2.Add(task);
                    }
                }
            }
            foreach (PingerTask task in taskList2)
            {
                if (task != null)
                {
                    task.WaitForEnd();
                }
            }
            Con.WriteLine("\n  TCP send data ended.\n");
        }

        // 各タスクの開始
        foreach (Target target in config.TargetListLastOkHost)
        {
            foreach (int port in target.Ports)
            {
                PingerTask task = null!;
                bool ok = false;
                if (baseTaskList != null)
                {
                    if (baseTaskList[num].Ok)
                    {
                        if (config.TcpSendData == false || port == 0)
                        {
                            // すでに前回で Ok が出ている
                            ok = true;
                        }
                    }
                }

                if (ok == false)
                {
                    task = new PingerTask(this.DnsClient, target.HostName, config.ConvertHostnameToOkHostname(target.HostName), port, config.Timeout, config.TcpSendData, config.DefaultOptions);
                }

                taskList.Add(task);

                num++;
            }
        }
        ThreadObj.Sleep(1000);
        foreach (Target target in config.TargetListLastNgHost)
        {
            foreach (int port in target.Ports)
            {
                PingerTask task = null!;
                bool ok = false;
                if (baseTaskList != null)
                {
                    if (baseTaskList[num].Ok)
                    {
                        if (config.TcpSendData == false || port == 0)
                        {
                            // すでに前回で Ok が出ている
                            ok = true;
                        }
                    }
                }

                if (ok == false)
                {
                    task = new PingerTask(this.DnsClient, target.HostName, config.ConvertHostnameToOkHostname(target.HostName), port, config.Timeout, config.TcpSendData, config.DefaultOptions);
                }

                taskList.Add(task);

                num++;
            }
        }

        // タスクの完了の待機
        foreach (PingerTask task in taskList)
        {
            if (task != null)
            {
                task.WaitForEnd();

                if (config.TcpSendData)
                {
                    if (task.TargetPort != 0)
                    {
                        task.TcpNg = !task.Ok;
                    }
                }

                if (task.Ok)
                {
                    CurrentOkHostList.Add(task.TargetHost);
                }
            }
        }

        double endTime = Time.NowHighResDouble;

        return taskList.ToArray();
    }

    // タスクを複数回実行
    public PingerTask[] ExecTasksMulti(Config config)
    {
        PingerTask[] baseTaskList = null!;

        Console.WriteLine(DateTime.Now.ToString() + " -----------------------");
        Console.WriteLine(DateTime.Now.ToString() + " ExecTasksMulti Started.");
        Console.WriteLine(DateTime.Now.ToString() + " LastOK Hosts: " + config.TargetListLastOkHost.Length);
        Console.WriteLine(DateTime.Now.ToString() + " LastNG Hosts: " + config.TargetListLastNgHost.Length);

        int i;
        for (i = 0; i < config.NumErrors; i++)
        {
            Console.Write(DateTime.Now.ToString() + " ExecTasks {0} / {1} ...", i + 1, config.NumErrors);
            PingerTask[] taskList = ExecTasks(config, baseTaskList);
            Console.WriteLine("\nOk.");

            if (baseTaskList == null)
            {
                baseTaskList = taskList;
            }
            else
            {
                int j;

                for (j = 0; j < baseTaskList.Length; j++)
                {
                    if (taskList[j] == null || taskList[j].Ok == true)
                    {
                        baseTaskList[j].Ok = true;
                    }

                    if (taskList[j] != null && taskList[j].TargetPort != 0 && config.TcpSendData && taskList[j].TcpNg)
                    {
                        baseTaskList[j].TcpNg = true;
                    }
                }
            }

            if (i != (config.NumErrors - 1))
            {
                Kernel.SleepThread(config.Interval);
            }
        }

        Console.WriteLine(DateTime.Now.ToString() + " ExecTasksMulti Finished.");
        Console.WriteLine(DateTime.Now.ToString() + " -----------------------");

        /*
		foreach (PingerTask task in baseTaskList)
		{
			if (task.TcpNg)
			{
				task.Ok = false;
			}
		}
		 * */

        Kernel.SleepThread(config.Interval);

        return baseTaskList;
    }
}


#endif

