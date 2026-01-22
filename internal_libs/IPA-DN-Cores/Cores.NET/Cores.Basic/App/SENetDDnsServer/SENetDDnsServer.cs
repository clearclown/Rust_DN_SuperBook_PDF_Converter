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
using IPA.Cores.Basic.Legacy;
using IPA.Cores.Basic.DnsLib;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using System.Net;
using System.Net.Sockets;

namespace IPA.Cores.Basic.SENetDDnsServer;

// 移植 2021/09/01 from SENet\DDNSServerTest\DDNSServerLib\DDNSServerLib.cs
public class LastAccess
{
    public string Name = "";
    public DateTime LastAccess_IPv4;
    public DateTime LastAccess_Azure;
}

public class Host
{
    public string Name = "";
    public string IPv4 = "", IPv6 = "", AzureIPv4 = "";
}

public class Ban
{
    public string Key = "";
    public string RedirectTo = "";
}

[EasyTable("HOSTS")]
public class Hosts2Table
{
    [EasyKey]
    public long HOST_ID { get; set; }
    public string HOST_NAME { get; set; } = "";
    public string HOST_LAST_IPV4 { get; set; } = "";
    public string HOST_LAST_IPV6 { get; set; } = "";
    public DateTime HOST_UPDATE_DATE { get; set; }
    public string HOST_AZURE_IP { get; set; } = "";
}

[EasyTable("BAN")]
public class BanTable
{
    public int BAN_ID { get; set; }
    public string BAN_KEY { get; set; } = "";
    public string BAN_REDIRECT_TO { get; set; } = "";
}

public class HostsCache : AsyncServiceWithMainLoop
{
    object LockObj = new object();
    object HaltLock = new object();
    Dictionary<string, Host> hosts = new Dictionary<string, Host>();
    Dictionary<string, LastAccess> lastaccesses = new Dictionary<string, LastAccess>();
    Dictionary<string, Ban> ban_list = new Dictionary<string, Ban>();
    //FileLogger log;

    //Event halt_event = new Event();
    //volatile bool halt_flag = false;

    readonly TimeSpan interval_full_update = new TimeSpan(1, 0, 0);
    readonly TimeSpan interval_full_update_on_error = new TimeSpan(0, 5, 0);
    readonly TimeSpan interval_diff_update = new TimeSpan(0, 0, 1);
    readonly TimeSpan interval_clock_margin = new TimeSpan(0, 5, 0);
    const int SqlTimeoutSecs = 3 * 60;

    public bool IsEmpty = true;

    public string DbConnectionString { get; }

    public readonly Ref<DateTimeOffset> LastDbReadTime = new Ref<DateTimeOffset>(DnsUtil.DnsDtStartDay);
    public readonly Ref<DateTimeOffset> LastDbWriteTime = new Ref<DateTimeOffset>(DnsUtil.DnsDtStartDay);

    public DateTimeOffset LastHealthyTime => new DateTime(Math.Min(LastDbReadTime.Value.Ticks, LastDbWriteTime.Value.Ticks));

    public HostsCache(string dbConnectionString)
    {
        try
        {
            this.DbConnectionString = dbConnectionString;

            //log = new FileLogger(@"c:\tmp\DDNS\DBLog");
            //halt_flag = false;
            //halt_event = new Event();
            //thread = new ThreadObj(main_thread);

            this.StartMainLoop(main_thread);
        }
        catch
        {
            this._DisposeSafe();
            throw;
        }
    }

    void write_log(string str)
    {
        //log.Flush = true;
        //log.Write(str);

        //Con.WriteLine(DateTime.Now.ToString() + ": " + str);

        Con.WriteLine(str);
    }

    public void AddLastAccess(string name, DateTime ipv4, DateTime azurev4)
    {
        name = name.ToLowerInvariant().Trim();

        lock (LockObj)
        {
            LastAccess a;

            if (lastaccesses.ContainsKey(name))
            {
                a = lastaccesses[name];
            }
            else
            {
                a = new LastAccess();
                a.Name = name;
                lastaccesses.Add(name, a);
            }

            if (a.LastAccess_IPv4 < ipv4)
            {
                a.LastAccess_IPv4 = ipv4;
            }

            if (a.LastAccess_Azure < azurev4)
            {
                a.LastAccess_Azure = azurev4;
            }
        }
    }

    public Ban? SearchBan(string key)
    {
        try
        {
            Str.NormalizeString(ref key);

            if (Str.IsEmptyStr(key))
            {
                return null;
            }

            key = key.ToLowerInvariant();
            lock (LockObj)
            {
                if (ban_list.ContainsKey(key))
                {
                    return ban_list[key];
                }
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    public Host? SearchHost(string name)
    {
        name = name.ToLowerInvariant().Trim();

        try
        {
            lock (LockObj)
            {
                Ban? ban = SearchBan(name);
                if (ban != null)
                {
                    return new Host() { AzureIPv4 = "", IPv4 = ban.RedirectTo, IPv6 = "" };
                }

                if (hosts.ContainsKey(name) == false)
                {
                    return null;
                }

                Host h = hosts[name];

                ban = SearchBan(h.IPv4);
                if (ban == null)
                {
                    ban = SearchBan(h.IPv6);
                }
                if (ban != null)
                {
                    return new Host() { AzureIPv4 = "", IPv4 = ban.RedirectTo, IPv6 = "" };
                }

                return h;
            }
        }
        catch
        {
            return null;
        }
    }

    async Task<Database> OpenDbAsync(CancellationToken cancel)
    {
        var db = new Database(this.DbConnectionString);
        try
        {
            await db.EnsureOpenAsync(cancel);

            return db;
        }
        catch
        {
            await db._DisposeSafeAsync();
            throw;
        }
    }

    async Task main_thread(CancellationToken cancel)
    {
        await Task.Yield();

        DateTime next_full_update = new DateTime(0);
        DateTime next_diff_update = new DateTime(0);
        DateTime last_update = new DateTime(0);
        bool full_ok = false;

        write_log(string.Format("DBTHREAD: main thread loop inited."));

        while (cancel.IsCancellationRequested == false)
        {
            DateTime now = DateTime.Now;

            if (now >= next_full_update)
            {
                write_log(string.Format("DBTHREAD: full update started."));
                next_full_update = now + Util.GenRandInterval(interval_full_update);

                try
                {
                    await using var db = await OpenDbAsync(cancel);

                    db.CommandTimeoutSecs = SqlTimeoutSecs;

                    var table = await db.EasySelectAsync<Hosts2Table>(@"
SELECT                HOST_ID, HOST_NAME, HOST_LAST_IPV4, HOST_LAST_IPV6, HOST_UPDATE_DATE, HOST_AZURE_IP
FROM                       HOSTS 
where HOST_LOGIN_DATE >= @BEGIN_DATE or HOST_NUM_ACCESS != 0
",
new
{
BEGIN_DATE = DateTime.Now.AddMonths(-1),
},
cancel: cancel);

                    //HOSTS2TableAdapter ta = new HOSTS2TableAdapter();
                    //SetSqlTimeout(ta, SqlTimeout);
                    //DDNS.HOSTS2DataTable table = ta.GetData2(DateTime.Now.AddMonths(-1));
                    Dictionary<string, Host> hosts_tmp = new Dictionary<string, Host>();

                    write_log(string.Format("DBTHREAD: full db query: {0} records.", table.Count()));

                    DateTime max_update_dt = new DateTime(0);

                    foreach (var r in table)
                    {
                        Host h = new Host();

                        h.Name = r.HOST_NAME.ToLowerInvariant();
                        h.IPv4 = r.HOST_LAST_IPV4;
                        h.IPv6 = r.HOST_LAST_IPV6;
                        h.AzureIPv4 = r.HOST_AZURE_IP;

                        if (max_update_dt < r.HOST_UPDATE_DATE)
                        {
                            max_update_dt = r.HOST_UPDATE_DATE;
                        }

                        if (hosts_tmp.ContainsKey(h.Name) == false)
                        {
                            hosts_tmp.Add(h.Name, h);
                        }
                    }

                    lock (LockObj)
                    {
                        this.hosts = hosts_tmp;
                    }

                    last_update = now;
                    if (max_update_dt.Ticks != 0 && max_update_dt < last_update)
                    {
                        last_update = max_update_dt;
                    }

                    full_ok = true;

                    this.LastDbReadTime.Set(DateTimeOffset.Now);

                    this.IsEmpty = false;
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.ToString());
                    write_log(string.Format("DBTHREAD: DBERROR (Full Update): {0}", ex.Message));
                    next_full_update = now + Util.GenRandInterval(interval_full_update_on_error);
                }
                //write_log(string.Format("DBTHREAD: next_full_update = {0}", next_full_update));
            }

            if (now >= next_diff_update && full_ok)
            {
                next_diff_update = now + Util.GenRandInterval(interval_diff_update);

                try
                {
                    //write_log(string.Format("DBTHREAD: diff update started, diffs since {0}.", last_update - interval_clock_margin));

                    await using var db = await OpenDbAsync(cancel);

                    db.CommandTimeoutSecs = SqlTimeoutSecs;

                    var table = await db.EasySelectAsync<Hosts2Table>(@"
SELECT  HOST_AZURE_IP, HOST_ID, HOST_LAST_IPV4, HOST_LAST_IPV6, HOST_NAME, HOST_UPDATE_DATE FROM HOSTS WITH (NOLOCK) WHERE (HOST_UPDATE_DATE >= @DT)",
new
{
DT = last_update - interval_clock_margin,
},
cancel: cancel);

                    //                  HOSTS2TableAdapter ta = new HOSTS2TableAdapter();
                    //SetSqlTimeout(ta, SqlTimeout);
                    //DDNS.HOSTS2DataTable table = ta.GetDataByUpdateDate(last_update - interval_clock_margin);

                    var ban_table = await db.EasySelectAsync<BanTable>(@"
SELECT                    BAN_ID, BAN_KEY, BAN_REDIRECT_TO
FROM                       BAN");

                    //BANTableAdapter ban_ta = new BANTableAdapter();
                    //SetSqlTimeout(ban_ta, SqlTimeout);
                    //DDNS.BANDataTable ban_table = ban_ta.GetData();

                    write_log(string.Format("DBTHREAD: diff db query: {0} records. ban: {1} records.", table.Count(), ban_table.Count()));

                    DateTime max_update_dt = new DateTime(0);

                    lock (LockObj)
                    {
                        foreach (var r in table)
                        {
                            Host h = new Host();

                            h.Name = r.HOST_NAME.ToLowerInvariant();
                            h.IPv4 = r.HOST_LAST_IPV4;
                            h.IPv6 = r.HOST_LAST_IPV6;
                            h.AzureIPv4 = r.HOST_AZURE_IP;

                            if (max_update_dt < r.HOST_UPDATE_DATE)
                            {
                                max_update_dt = r.HOST_UPDATE_DATE;
                            }

                            if (this.hosts.ContainsKey(h.Name) == false)
                            {
                                this.hosts.Add(h.Name, h);
                            }
                            else
                            {
                                this.hosts[h.Name] = h;
                            }
                        }

                        this.ban_list.Clear();
                        foreach (var r in ban_table)
                        {
                            try
                            {
                                if (IPUtil.IsStrIPv4(r.BAN_REDIRECT_TO.Trim()))
                                {
                                    string key = r.BAN_KEY;

                                    Str.NormalizeString(ref key);

                                    key = key.ToLowerInvariant();

                                    if (ban_list.ContainsKey(key) == false)
                                    {
                                        ban_list.Add(key, new Ban() { Key = r.BAN_KEY.Trim(), RedirectTo = r.BAN_REDIRECT_TO.Trim(), });
                                    }
                                }
                            }
                            catch
                            {
                            }
                        }
                    }

                    last_update = now;
                    if (max_update_dt.Ticks != 0 && max_update_dt < last_update)
                    {
                        last_update = max_update_dt;
                    }

                    this.LastDbReadTime.Set(DateTimeOffset.Now);
                }
                catch (Exception ex)
                {
                    write_log(string.Format("DBTHREAD: DBERROR (Diff Update): {0}", ex.Message));
                }
                //write_log(string.Format("DBTHREAD: next_diff_update = {0}", next_diff_update));

                // write last access
                try
                {
                    await using var db = await OpenDbAsync(cancel);
                    //HOSTSTableAdapter ta = new HOSTSTableAdapter();

                    //SetSqlTimeout(ta, SqlTimeout);
                    db.CommandTimeoutSecs = SqlTimeoutSecs;

                    Dictionary<string, LastAccess> a2 = null!;

                    lock (LockObj)
                    {
                        a2 = lastaccesses;
                        lastaccesses = new Dictionary<string, LastAccess>();
                    }

                    int num_v4 = 0, num_azure = 0;

                    foreach (string name in a2.Keys)
                    {
                        LastAccess a = a2[name];

                        if (a.LastAccess_IPv4.Ticks != 0)
                        {
                            //ta.WriteLastAccess(a.LastAccess_IPv4, a.Name);
                            await db.EasyExecuteAsync(@"
UPDATE                  HOSTS
SET                            HOST_ACCESS_DATE = @NOW, HOST_NUM_ACCESS = HOST_NUM_ACCESS + 1
WHERE HOST_NAME = @NAME",
new
{
NOW = a.LastAccess_IPv4,
NAME = a.Name,
}
);

                            num_v4++;
                        }

                        if (a.LastAccess_Azure.Ticks != 0)
                        {
                            //ta.WriteAzureLastAccess(a.LastAccess_Azure, a.Name);
                            await db.EasyExecuteAsync(@"
UPDATE                  HOSTS
SET                            HOST_AZURE_ACCESS_DATE = @NOW, HOST_AZURE_NUM_ACCESS = HOST_AZURE_NUM_ACCESS + 1
WHERE HOST_NAME = @NAME",
new
{
NOW = a.LastAccess_Azure,
NAME = a.Name,
}
);
                            num_azure++;
                        }
                    }

                    if (num_v4 != 0 || num_azure != 0)
                    {
                        write_log(string.Format("DBTHREAD: Last Access Updated, IPv4={0}, Azure={1}",
                            num_v4, num_azure));
                    }

                    this.LastDbWriteTime.Set(DateTimeOffset.Now);
                }
                catch (Exception ex)
                {
                    write_log(string.Format("DBTHREAD: DBERROR (Update LastAccess): {0}", ex.Message));
                }
            }

            //halt_event.Wait(100);
            await cancel._WaitUntilCanceledAsync(100);
        }

        write_log(string.Format("DBTHREAD: main thread loop stopped."));
    }

    protected override async Task CleanupImplAsync(Exception? ex)
    {
        try
        {
        }
        finally
        {
            await base.CleanupImplAsync(ex);
        }
    }

    //public static void SetSqlTimeout(object adapter, int timeout)
    //{
    //	object commands = adapter.GetType().InvokeMember(
    //			"CommandCollection",
    //			BindingFlags.GetProperty | BindingFlags.Instance | BindingFlags.NonPublic,
    //			null, adapter, new object[0]);
    //	SqlCommand[] sqlCommand = (SqlCommand[])commands;
    //	foreach (SqlCommand cmd in sqlCommand)
    //	{
    //		cmd.CommandTimeout = timeout;
    //	}
    //}

}

public class DDNSServer : AsyncService
{
    object lockObj = new object();
    //bool inited = false;
    //DnsServer server4 = null;
    //DnsServer server6 = null;
    EasyDnsServer server = null!;
    //bool is_server4_ok = false, is_server6_ok = false;
    //const int numTasks = 256;
    readonly string configFileName;
    HostsCache hc = null!;

    //const string logDir = @"c:\tmp\DDNS\Log";

    //FileLogger log;

    //object logWriteLock = new object();

    //DateTime lastFlashDateTime;

    //readonly TimeSpan LogFlushSpan = new TimeSpan(0, 0, 15);

    Cache<int, ReadIni> config_cache = new Cache<int, ReadIni>(new TimeSpan(0, 1, 0), CacheType.DoNotUpdateExpiresWhenAccess);

    ReadIni Ini
    {
        get
        {
            ReadIni r = config_cache[0];
            if (r == null)
            {
                ReadIni i = new ReadIni(configFileName);
                config_cache.Add(0, i);
                return i;
            }
            return r;
        }
    }

    public DDNSServer(string configFileName) : base()
    {
        try
        {
            this.configFileName = configFileName;

            //IO.MakeDir(logDir);

            //lastFlashDateTime = DateTime.Now;

            //log = new FileLogger(logDir);

            string dbConnectStr = Ini["DbConnectString"].StrValue;
            int portNumber = (int)Ini["Port"].IntValue;
            if (portNumber == 0)
            {
                portNumber = Consts.Ports.Dns;
            }

            this.hc = new HostsCache(dbConnectStr);

            this.server = new EasyDnsServer(new EasyDnsServerSetting(ProcessQueryList, null, null, portNumber));

            var opt = this.server.GetCurrentDynOptions();
            opt.ParseUdpProxyProtocolV2 = true;
            this.server.SetCurrentDynOptions(opt);
        }
        catch
        {
            this._DisposeSafe();
            throw;
        }
    }

    List<DnsUdpPacket> ProcessQueryList(EasyDnsServer dnsServer, List<DnsUdpPacket> requestList)
    {
        List<DnsUdpPacket> replyList = new List<DnsUdpPacket>(requestList.Count);

        foreach (var request in requestList)
        {
            try
            {
                var reply = this.processDnsQuery(request.Message, request.RemoteEndPoint.Address);

                if (reply == null)
                {
                    // エラー発生
                    DnsMessage? q = request.Message as DnsMessage;
                    if (q != null)
                    {
                        q.IsQuery = false;
                        q.ReturnCode = ReturnCode.ServerFailure;
                        q.IsRecursionAllowed = false;
                        reply = q;
                    }
                }

                if (reply != null)
                {
                    replyList.Add(new DnsUdpPacket(request.RemoteEndPoint, request.LocalEndPoint, reply));
                }
            }
            catch (Exception ex)
            {
                ex._Debug();
            }
        }

        return replyList;
    }

    void write(string str)
    {
        str._PostAccessLog("ddns");
    }

    protected override async Task CleanupImplAsync(Exception? ex)
    {
        try
        {
            await this.server._DisposeSafeAsync();

            await this.hc._DisposeSafeAsync();
        }
        finally
        {
            await base.CleanupImplAsync(ex);
        }
    }

    public string[] ValidDomainNames
    {
        get
        {
            List<string> ret = new List<string>();
            string[] keys = Ini.GetKeys();

            foreach (string key in keys)
            {
                if (key.StartsWith("DomainName", StringComparison.OrdinalIgnoreCase))
                {
                    ret.Add(Ini[key].StrValue);
                }
            }

            return ret.ToArray();
        }
    }

    IPAddress? nameToIPAddress(string name, bool v6, bool azure)
    {
        // 2019.12.23
        // _unregistered_ で開始されるホスト名は、存在していないフリをする。
        if (name.StartsWith("_unregistered_", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        List<string> ret = new List<string>();
        string[] keys = Ini.GetKeys();

        if (azure)
        {
            v6 = false;
        }

        // 設定ファイルの文字列を優先する
        foreach (string key in keys)
        {
            if (v6 == false)
            {
                if (key.StartsWith("A", StringComparison.OrdinalIgnoreCase))
                {
                    string line = Ini[key].StrValue;
                    char[] sps = { ' ', '\t' };

                    string[] tokens = line.Split(sps, StringSplitOptions.RemoveEmptyEntries);

                    if (tokens.Length == 2)
                    {
                        if (Str.StrCmpi(tokens[0], name))
                        {
                            try
                            {
                                IPAddress? addr = IPUtil.StrToIP(tokens[1], noExceptionAndReturnNull: true);

                                if (addr != null)
                                {
                                    if (addr.AddressFamily == AddressFamily.InterNetwork)
                                    {
                                        return addr;
                                    }
                                }
                            }
                            catch
                            {
                            }
                        }
                    }
                }
            }
            else
            {
                if (key.StartsWith("AAAA", StringComparison.OrdinalIgnoreCase))
                {
                    string line = Ini[key].StrValue;
                    char[] sps = { ' ', '\t' };

                    string[] tokens = line.Split(sps, StringSplitOptions.RemoveEmptyEntries);

                    if (tokens.Length == 2)
                    {
                        if (Str.StrCmpi(tokens[0], name))
                        {
                            try
                            {
                                IPAddress? addr = IPUtil.StrToIP(tokens[1], noExceptionAndReturnNull: true);

                                if (addr != null)
                                {
                                    if (addr.AddressFamily == AddressFamily.InterNetworkV6)
                                    {
                                        return addr;
                                    }
                                }
                            }
                            catch
                            {
                            }
                        }
                    }
                }
            }
        }

        // DB にクエリを出して取得する
        string retstr = "";

        Host? h = hc.SearchHost(name);

        if (h != null)
        {
            if (v6 == false)
            {
                if (azure == false)
                {
                    retstr = h.IPv4;
                }
                else
                {
                    retstr = h.AzureIPv4;
                }
            }
            else
            {
                retstr = h.IPv6;
            }
        }

        if (Str.IsEmptyStr(retstr))
        {
            if (v6 == false)
            {
                // "vpngate-" で始まる数字かどうか
                if (name.StartsWith("vg", StringComparison.OrdinalIgnoreCase))
                {
                    string num_str = name.Substring(2);
                    uint num = (uint)Str.StrToLong(num_str);
                    Buf buf = new Buf();
                    buf.WriteInt(num);
                    IPAddress addr = new IPAddress(buf.ByteData);

                    return addr;
                }
            }
        }

        if (Str.IsEmptyStr(retstr) == false)
        {
            try
            {
                IPAddress? addr = IPUtil.StrToIP(retstr, noExceptionAndReturnNull: true);

                if (addr != null)
                {
                    if (azure == false)
                    {
                        hc.AddLastAccess(name, DateTime.Now, new DateTime(0));
                    }
                    else
                    {
                        hc.AddLastAccess(name, new DateTime(0), DateTime.Now);
                    }

                    return addr;
                }
            }
            catch
            {
            }
        }

        return null;
    }

    List<IPAddress>? nameToIpAddresses(string name, RecordType type, out string domainname, out bool fix_record)
    {
        List<IPAddress> ret = new List<IPAddress>();
        bool v4 = false, v6 = false;
        name = name.Trim();
        domainname = "";
        bool azure = false;

        fix_record = false;

        if (name.StartsWith("."))
        {
            name = name.Substring(1);
        }
        if (name.EndsWith("."))
        {
            name = name.Substring(0, name.Length - 1);
        }

        int i = Str.SearchStr(name, ".", 0);
        if (i == -1)
        {
            // ドメイン名が不正
            return null;
        }

        // special に完全一致するかどうか
        string[] keys = Ini.GetKeys();

        foreach (string key in keys)
        {
            if (key.StartsWith("special", StringComparison.OrdinalIgnoreCase))
            {
                string value = Ini[key].StrValue;

                char[] sps = { '\t', ' ', };
                string[] tokens = value.Split(sps, StringSplitOptions.RemoveEmptyEntries);

                if (tokens.Length == 3)
                {
                    if (Str.StrCmpi(tokens[0], name))
                    {
                        IPAddress? retAddress = IPUtil.StrToIP(tokens[2], noExceptionAndReturnNull: true);
                        if (retAddress != null)
                        {
                            domainname = tokens[1];

                            ret.Add(retAddress);

                            fix_record = true;

                            return ret;
                        }
                    }
                }
            }
        }

        string hostname = name.Substring(0, i);
        domainname = name.Substring(i + 1);

        bool ipv4_only = false;

        if (Str.InStr(domainname, "opengw.net"))
        {
            ipv4_only = true;
        }

        // ドメイン名を検査
        foreach (string domain in ValidDomainNames)
        {
            bool is_azure = false;

            if (Str.InStr(domain, "vpnazure"))
            {
                is_azure = true;
            }

            string domain_v4 = "v4." + domain;
            string domain_v6 = "v6." + domain;


            if (is_azure == false)
            {
                if (Str.StrCmpi(domainname, domain))
                {
                    // IPv4 と IPv6 の双方を返す
                    v4 = true;

                    if (ipv4_only == false)
                    {
                        v6 = true;
                    }
                }
                else if (Str.StrCmpi(domainname, domain_v4))
                {
                    // IPv4 のみ返す
                    v4 = true;
                }
                else if (Str.StrCmpi(domainname, domain_v6))
                {
                    // IPv6 のみ返す
                    v6 = true;
                }
            }
            else
            {
                if (Str.StrCmpi(domainname, domain))
                {
                    v4 = true;
                    v6 = false;

                    azure = true;
                }
            }
        }

        if (v4 == false && v6 == false)
        {
            // ドメイン名が不正
            return null;
        }

        if (v4)
        {
            IPAddress? addr = nameToIPAddress(hostname, false, azure);

            if (addr != null)
            {
                ret.Add(addr);
            }
        }
        if (v6)
        {
            IPAddress? addr = nameToIPAddress(hostname, true, azure);

            if (addr != null)
            {
                ret.Add(addr);
            }
        }

        return ret;
    }

    DnsMessageBase? processDnsQuery(DnsMessageBase message, IPAddress clientAddress)
    {
        try
        {
            bool is_fix_record = false;
            DnsMessage? q = message as DnsMessage;
            string? domainname = null;

            List<string> matchIPList = new List<string>();

            if (q == null)
            {
                return null;
            }

            if (hc.IsEmpty)
            {
                return null;
            }

            q.IsQuery = false;
            q.ReturnCode = ReturnCode.ServerFailure;

            // 2022.8.18 dnsdist のキャッシュを効かせるためすべての AdditionalRecords を無視
            if (Ini["CopyQueryAdditionalRecordsToResponse"].BoolValue == false)
            {
                q.AdditionalRecords.Clear();
            }

            List<DnsRecordBase> answers = new List<DnsRecordBase>();
            int num_match = 0;



            DnsQuestion? question = q.Questions.ElementAtOrDefault(0);
            if (question == null)
            {
                return null;
            }

            string queryDomainName = question.Name.ToString();

            do
            {
                var validDomainNames = ValidDomainNames;

                q.ReturnCode = ReturnCode.NoError;

                if (queryDomainName._InStri("_acme-challenge"))
                {
                    foreach (string dom in validDomainNames)
                    {
                        List<string> acme_challenge_list = new List<string>();
                        acme_challenge_list.Add("_acme-challenge." + dom + ".");
                        acme_challenge_list.Add("_acme-challenge.v4." + dom + ".");
                        acme_challenge_list.Add("_acme-challenge.v6." + dom + ".");

                        foreach (var test1 in acme_challenge_list)
                        {
                            if (queryDomainName.Equals(test1, StringComparison.OrdinalIgnoreCase) || queryDomainName.EndsWith("." + test1, StringComparison.OrdinalIgnoreCase))
                            {
                                var nsRecord = new NsRecord(DomainName.Parse(test1), (int)Ini["TtlFix"].IntValue, DomainName.Parse("secertsvr1.sehosts.com"));
                                q.AuthorityRecords.Add(nsRecord);
                                q.IsRecursionAllowed = false;
                                return q;
                            }
                        }
                    }
                }


                if (queryDomainName._InStri("_psl"))
                {
                    foreach (string dom in validDomainNames)
                    {
                        List<string> psl_list = new List<string>();
                        psl_list.Add("_psl." + dom + ".");
                        psl_list.Add("_psl.v4." + dom + ".");
                        psl_list.Add("_psl.v6." + dom + ".");

                        foreach (var test1 in psl_list)
                        {
                            if (queryDomainName.Equals(test1, StringComparison.OrdinalIgnoreCase) || queryDomainName.EndsWith("." + test1, StringComparison.OrdinalIgnoreCase))
                            {
                                var txtRecord = new TxtRecord(DomainName.Parse(queryDomainName), (int)Ini["TtlFix"].IntValue, "https://github.com/publicsuffix/list/pull/100");
                                q.AnswerRecords.Add(txtRecord);
                                q.IsAuthoritiveAnswer = true;
                                q.IsRecursionAllowed = false;
                                return q;
                            }
                        }
                    }
                }


                string tmp;

                //Con.WriteLine("{0}: Query - {2} '{1}'", Str.DateTimeToStrShort(DateTime.Now), question.Name, question.RecordType);

                if (question.RecordType == RecordType.Ns)
                {
                    bool b = false;

                    foreach (string dom in validDomainNames)
                    {
                        string qname = queryDomainName;

                        if (qname.EndsWith("."))
                        {
                            qname = qname.Substring(0, qname.Length - 1);
                        }

                        if (Str.StrCmpi(qname, dom))
                        {
                            string[] keys = Ini.GetKeys();
                            int num_ns = 0;

                            foreach (string key in keys)
                            {
                                if (key.StartsWith("NS", StringComparison.OrdinalIgnoreCase))
                                {
                                    string ns = Ini[key].StrValue;
                                    answers.Add(new NsRecord(DomainName.Parse(dom), (int)Ini["TtlFix"].IntValue, DomainName.Parse(ns)));
                                    num_ns++;
                                }
                            }

                            if (num_ns == 0)
                            {
                                answers.Add(new NsRecord(DomainName.Parse(dom), (int)Ini["TtlFix"].IntValue, DomainName.Parse(Ini["PrimaryServer"].StrValue)));
                            }

                            num_match++;
                            b = true;
                        }

                        if (qname.EndsWith(dom) && (Str.StrCmpi(qname, dom) == false))
                        {
                            string[] keys = Ini.GetKeys();
                            //int num_ns = 0;

                            q.IsAuthoritiveAnswer = true;

                            q.AuthorityRecords.Add(new SoaRecord(DomainName.Parse(dom), (int)Ini["Ttl"].IntValue, DomainName.Parse(Ini["PrimaryServer"].StrValue),
                                DomainName.Parse(Ini["EMail"].StrValue), DnsUtil.GenerateSoaSerialNumberFromDateTime(this.hc.LastHealthyTime), (int)Ini["Ttl"].IntValue, (int)Ini["Ttl"].IntValue,
                                88473600, (int)Ini["Ttl"].IntValue));
                            num_match++;

                            /*	if (num_ns == 0)
                                {
                                    answers.Add(new NsRecord(dom, (int)Ini["TtlFix"].IntValue, Ini["PrimaryServer"].StrValue));
                                } */

                            num_match++;
                            b = true;
                        }
                    }

                    if (b)
                    {
                        break;
                    }
                }


                List<IPAddress>? ret = nameToIpAddresses(queryDomainName, question.RecordType, out tmp, out is_fix_record);

                if (ret == null)
                {
                    q.ReturnCode = ReturnCode.Refused;
                    break;
                }
                else
                {
                    if (question.RecordType == RecordType.A || question.RecordType == RecordType.Any)
                    {
                        if (ret.Count == 0)
                        {
                            if (Ini["DisableReturnRandomWhenNotFound"].BoolValue == false)
                            {
                                // 約 6% の確率でランダム IP アドレスを返す
                                string name = queryDomainName.ToLowerInvariant().Split('.')[0];

                                ulong t1 = Str.HashStrToLong(name + "0") % 100;

                                if (t1 <= 6)
                                {
                                    byte b1 = (byte)(Str.HashStrToLong(name + "1") % 221 + 1);
                                    byte b2 = (byte)(Str.HashStrToLong(name + "2") & (ulong)0xff);
                                    byte b3 = (byte)(Str.HashStrToLong(name + "3") & (ulong)0xff);
                                    byte b4 = (byte)(Str.HashStrToLong(name + "4") & (ulong)0xff);
                                    IPAddress randIp = new IPAddress(new byte[] { b1, b2, b3, b4, });
                                    ret.Add(randIp);
                                }
                            }
                        }
                    }

                    if (Str.IsEmptyStr(tmp) == false)
                    {
                        domainname = tmp;
                    }

                    foreach (IPAddress addr in ret)
                    {
                        num_match++;

                        int ttl = (int)Ini["Ttl"].IntValue;
                        int ttl_fix = (int)Ini["TtlFix"].IntValue;

                        if (is_fix_record)
                        {
                            ttl = ttl_fix;
                        }

                        if (addr.AddressFamily == AddressFamily.InterNetwork && (question.RecordType == RecordType.A || question.RecordType == RecordType.Any))
                        {
                            answers.Add(new ARecord(question.Name, ttl, addr));
                        }
                        else if (addr.AddressFamily == AddressFamily.InterNetworkV6 && (question.RecordType == RecordType.Aaaa || question.RecordType == RecordType.Any))
                        {
                            answers.Add(new AaaaRecord(question.Name, ttl, addr));
                        }
                        else if (question.RecordType == RecordType.Ns)
                        {
                            answers.Add(new NsRecord(DomainName.Parse(domainname), ttl_fix, DomainName.Parse(Ini["PrimaryServer"].StrValue)));
                        }

                        // ログ
                        if (addr.AddressFamily == AddressFamily.InterNetwork || addr.AddressFamily == AddressFamily.InterNetworkV6)
                        {
                            matchIPList.Add(addr.ToString());
                        }
                    }
                }
            }
            while (false);

            if (q.ReturnCode == ReturnCode.NoError)
            {
                if (num_match >= 1)
                {
                    q.AnswerRecords = answers;
                    q.IsAuthoritiveAnswer = true;
                }
                else
                {
                    q.ReturnCode = ReturnCode.NxDomain;
                }

                if (Ini["SaveAccessLog"].BoolValue)
                {
                    string logStr = string.Format("Query Domain={0} From={1} ", queryDomainName, clientAddress.ToString());

                    if (matchIPList.Count == 0)
                    {
                        logStr += "Result: Not Found";
                    }
                    else
                    {
                        logStr += "Result: " + Str.CombineStringArray(matchIPList.ToArray(), " ");
                    }

                    write(logStr);
                }
            }

            // SOA
            if (Str.IsEmptyStr(domainname) == false)
            {
                q.IsAuthoritiveAnswer = true;

                q.AuthorityRecords.Add(new SoaRecord(DomainName.Parse(domainname), (int)Ini["Ttl"].IntValue, DomainName.Parse(Ini["PrimaryServer"].StrValue),
                    DomainName.Parse(Ini["EMail"].StrValue), DnsUtil.GenerateSoaSerialNumberFromDateTime(this.hc.LastHealthyTime), (int)Ini["Ttl"].IntValue, (int)Ini["Ttl"].IntValue,
                    88473600, (int)Ini["Ttl"].IntValue));
            }

            q.IsRecursionAllowed = false;

            return q;
        }
        catch (Exception ex)
        {
            ex._Debug();

            return null;
        }
    }
}

#endif

