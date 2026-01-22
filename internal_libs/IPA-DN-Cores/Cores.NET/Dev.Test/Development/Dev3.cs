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
// 開発中のクラスの一時置き場

#if true

#pragma warning disable CA2235 // Mark all non-serializable fields

using System;
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
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Runtime.CompilerServices;

using IPA.Cores.Basic;
using IPA.Cores.Basic.DnsLib;
using IPA.Cores.Basic.Internal;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using Microsoft.AspNetCore.Server.IIS.Core;
//using Microsoft.EntityFrameworkCore.Query.Internal;
using System.Net.Sockets;

using Newtonsoft.Json;
using System.Data;
using System.Reflection;
using System.Security.Authentication;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;

namespace IPA.Cores.Basic;

public static partial class DevCoresConfig
{
    public static partial class MikakaDDnsServiceSettings
    {
        public static readonly Copenhagen<int> HostRecordMaxArchivedCount = 10;
        public static readonly Copenhagen<int> HostRecord_StaleMarker_Secs = 3600 * 24 * 365 * 3;
    }
}

public static class MikakaDDnsServiceGlobal
{
    // デフォルトの static 証明書
    // SHA1: 80E38CE13AE4FCB033439A6C17B0145D77AD4960
    // SHA256: 35188460BC0767A6B738D118442EFEE5711CC5A8CB62387249E12D459D8E65A4
    static readonly Singleton<PalX509Certificate> MikakaDDnsServerSampleStaticCert_Singleton = new Singleton<PalX509Certificate>(() => new PalX509Certificate(new FilePath(Res.Cores, "SampleDefaultCert/221125MikakaDDnsServerSampleStaticCert-20221125.pfx")));
    public static PalX509Certificate MikakaDDnsServerSampleStaticCert => MikakaDDnsServerSampleStaticCert_Singleton;

    public static void Init()
    {
        GlobalCertVault.SetDefaultCertificateGenerator(_ => MikakaDDnsServiceGlobal.MikakaDDnsServerSampleStaticCert);

        GlobalCertVault.SetDefaultSettingsGenerator(() =>
        {
            var s = new CertVaultSettings(EnsureSpecial.Yes);

            s.MaxAcmeCerts = 16;
            s.NonAcmeEnableAutoGenerateSubjectNameCert = false;
            s.ForceUseDefaultCertFqdnList = new string[] { "*-static.*", "*-static-v4.*", "*-static-v6.*" };

            return s;
        });
    }
}

public class MikakaDDnsServiceStartupParam : HadbBasedServiceStartupParam
{
    public MikakaDDnsServiceStartupParam(string hiveDataName = "MikakaDDnsService", string hadbSystemName = "MIKAKA_DDNS")
    {
        this.HiveDataName = hiveDataName;
        this.HadbSystemName = hadbSystemName;
    }
}

public class MikakaDDnsServiceHook : HadbBasedServiceHookBase
{
    public virtual async Task DDNS_SendRecoveryMailAsync(MikakaDDnsService svc, List<MikakaDDnsService.Host> hostList, string email, string requestIpStr, string requestFqdnStr, CancellationToken cancel = default)
    {
        StringWriter w = new StringWriter();

        w.WriteLine($"{email} 様");
        w.WriteLine("");
        w.WriteLine($"{svc.CurrentDynamicConfig.DDns_DomainNamePrimary} DDNS サービスをご利用いただき、ありがとうございます。");
        w.WriteLine("DDNS ホスト回復メールをお送りします。");
        w.WriteLine();
        w.WriteLine($"あなたの指定したメールアドレス {email} で登録されている");
        w.WriteLine($"DDNS ホストの一覧は以下のとおりです。");
        w.WriteLine();
        w.WriteLine($"回答日時: {DtOffsetNow._ToDtStr()}");
        w.WriteLine($"照会元 IP アドレス: {requestIpStr}");
        w.WriteLine($"照会元 DNS ホスト名: {requestFqdnStr}");
        w.WriteLine($"一致件数: {hostList.Count._ToString3()} 件");
        w.WriteLine();

        for (int i = 0; i < hostList.Count; i++)
        {
            var h = hostList[i];

            w.WriteLine($"■ ホスト {i + 1}");
            w.WriteLine("ホストラベル: " + h.HostLabel);
            w.WriteLine("ホストシークレットキー: " + h.HostSecretKey);
            w.WriteLine("作成日時: " + h.CreatedTime._ToDtStr());
            w.WriteLine("クエリ件数: " + h.DnsQuery_Count._ToString3());
            w.WriteLine("最終クエリ日時: " + h.DnsQuery_LastAccessTime._ToDtStr(zeroDateTimeStr: "(なし)"));
            w.WriteLine();
        }

        w.WriteLine("以上");
        w.WriteLine();

        await svc.Basic_SendMailAsync(email,
            $"DDNS ホスト回復メール - {svc.CurrentDynamicConfig.DDns_DomainNamePrimary}",
            w.ToString(),
            cancel: cancel);
    }
}

public class MikakaDDnsService : HadbBasedServiceBase<MikakaDDnsService.MemDb, MikakaDDnsService.DynConfig, MikakaDDnsService.HiveSettings, MikakaDDnsServiceHook>, MikakaDDnsService.IRpc
{
    public class DynConfig : HadbBasedServiceDynConfig
    {
        [SimpleComment("Set true to make DDNS server to write DNS debug access logs to the Log/Access directory")]
        public bool DDns_SaveDnsQueryAccessLogForDebug = false;

        [SimpleComment("Cumulative quota of the DDNS host count per specific client IP address (Host address exact match)")]
        public int DDns_MaxHostPerCreateClientIpAddress_Total;

        [SimpleComment("Cumulative quota of the DDNS host count per specific client IP address (Subnet address match)")]
        public int DDns_MaxHostPerCreateClientIpNetwork_Total;

        [SimpleComment("Disable all quotas of the DDNS host count per specific client IP address / network")]
        public bool DDns_DisableMaxHostPerCreateClientIpQuota = false;

        [SimpleComment("Daily quota of the DDNS host count per specific client IP address (Host address exact match)")]
        public int DDns_MaxHostPerCreateClientIpAddress_Daily;

        [SimpleComment("Daily quota of the DDNS host count per specific client IP address (Subnet address match)")]
        public int DDns_MaxHostPerCreateClientIpNetwork_Daily;

        [SimpleComment("List of prohibited hostnames starts with (split multiple items with ',' character)")]
        public string DDns_ProhibitedHostnamesStartWith = "_initial_";

        [SimpleComment("List of prohibited hostnames ends with (split multiple items with ',' character)")]
        public string DDns_ProhibitedHostnamesEndsWith = "_initial_";

        [SimpleComment("'userData' DDNS record field max size limitation")]
        public int DDns_MaxUserDataJsonStrLength = 10 * 1024;

        [SimpleComment("Set true to enable the 'Unlock Key' feature (Described in the API help)")]
        public bool DDns_RequireUnlockKey = false;

        [SimpleComment("New hostname prefix")]
        public string DDns_NewHostnamePrefix = "";

        [SimpleComment("How many digits in new hostname prefix")]
        public int DDns_NewHostnameRandomDigits = 12;

        [SimpleComment("Set true to disallow IPv4 address regisitration")]
        public bool DDns_Prohibit_IPv4AddressRegistration = false;

        [SimpleComment("Set true to disallow IPv6 address regisitration")]
        public bool DDns_Prohibit_IPv6AddressRegistration = false;

        [SimpleComment("Set true to disallow any Let's Encrypt (or other ACME protocol-based) certificate issue on any DDNS host records")]
        public bool DDns_Prohibit_Acme_CertIssue_For_Hosts = false;

        [SimpleComment("Min DDNS host label length")]
        public int DDns_MinHostLabelLen;

        [SimpleComment("Max DDNS host label length")]
        public int DDns_MaxHostLabelLen;

        [SimpleComment("TTL seconds for DDNS dynamic host records")]
        public int DDns_Protocol_Ttl_Secs;

        [SimpleComment("TTL seconds for DDNS static host records")]
        public int DDns_Protocol_Ttl_Secs_Static_Record;

        [SimpleComment("TTL seconds for DDNS static host records (applied to only records where health_check_url is specified)")]
        public int DDns_Protocol_Ttl_Secs_Static_Record_With_HealthCheck;

        [SimpleComment("Master NS record FQDN in DNS SOA response")]
        public string DDns_Protocol_SOA_MasterNsServerFqdn = "";

        [SimpleComment("Responsible field record FQDN in DNS SOA response")]
        public string DDns_Protocol_SOA_ResponsibleFieldFqdn = "";

        [SimpleComment("Negative cache TTL seconds in DNS SOA response")]
        public int DDns_Protocol_SOA_NegativeCacheTtlSecs;

        [SimpleComment("Refresh interval seconds in DNS SOA response")]
        public int DDns_Protocol_SOA_RefreshIntervalSecs;

        [SimpleComment("Retry interval seconds in DNS SOA response")]
        public int DDns_Protocol_SOA_RetryIntervalSecs;

        [SimpleComment("Expire interval seconds in DNS SOA response")]
        public int DDns_Protocol_SOA_ExpireIntervalSecs;

        [SimpleComment("Copy query packet's Additional Records field to the response packet (including EDNS fields). This might confuse DNS cache server like dnsdist.")]
        public bool DDns_Protocol_CopyQueryAdditionalRecordsToResponse;

        [SimpleComment("Set true if you want this DDNS server to accept UDP Proxy Protocol Version 2.0 (defined by haproxy and supported by some DNS proxies such as dnsdist)")]
        public bool DDns_Protocol_ParseUdpProxyProtocolV2;

        [SimpleComment("If DDns_Protocol_AcceptUdpProxyProtocolV2 is true you can specify the source IP address ACL to accept UDP Proxy Protocol (You can specify multiple items. e.g. 127.0.0.0/8,1.2.3.0/24)")]
        public string DDns_Protocol_ProxyProtocolAcceptSrcIpAcl = "";

        public string DDns_TcpAxfrAllowedAcl = "127.0.0.0/8; 1.2.3.0/24";

        [SimpleComment("Set a specific secret string to enable the 'License String' feature (Described in the API help)")]
        public string DDns_RequiredLicenseString = "";

        [SimpleComment("Max DDNS host count per email recovery request")]
        public int DDns_Enum_By_Email_MaxCount;

        [SimpleComment("Max DDNS host count per a specific userGroupSecretKey")]
        public int DDns_Enum_By_UserGroupSecretKey_MaxCount;

        [SimpleComment("DDNS_Host API rate limit: duration seconds")]
        public int DDns_HostApi_RateLimit_Duration_Secs = 3600 * 24;

        [SimpleComment("DDNS_Host API rate limit: max counter per duration")]
        public int DDns_HostApi_RateLimit_MaxCounts_Per_Duration = 24;

        [SimpleComment("Set true to ignore any string after double hyphon '--' in the host label at DNS request packets")]
        public bool DDns_HostLabelLookup_IgnoreAfterDoubleHyphon = true;

        [SimpleComment("Ignore these prefix strings in the host label at DNS request packets (split multiple items with ',' character)")]
        public string DDns_HostLabelLookup_IgnorePrefixStrings = "_initial_";

        [SimpleComment("Ignore these suffix strings in the host label at DNS request packets (split multiple items with ',' character)")]
        public string DDns_HostLabelLookup_IgnoreSuffixStrings = "_initial_";

        [SimpleComment("Suffix list to recognize that the DNS client prefers just A (IPv4) records in the response. A suffix is ignored in parsing of host label. (split multiple items with ',' character)")]
        public string DDns_HostLabelLookup_DummySuffixListForIPv4Only = "_initial_";

        [SimpleComment("Suffix list to recognize that the DNS client prefers just AAAA (IPv6) records in the response. A suffix is ignored in parsing of host label. (split multiple items with ',' character)")]
        public string DDns_HostLabelLookup_DummySuffixListForIPv6Only = "_initial_";

        [SimpleComment("The base (parent) domain name of this DDNS server. You can define multiple items")]
        public string[] DDns_DomainName = new string[0];

        [SimpleComment("The primary base (parent) domain name of this DDNS server. You have to choose one specific domain in the DDns_DomainName lists")]
        public string DDns_DomainNamePrimary = "";

        [SimpleComment("DNS static (pre-defined, fixed) records in this DDNS server. Static records have higher priority than DDNS dynamic records, and always be responsed. You can define multiple items")]
        public string[] DDns_StaticRecord = new string[0];

        [SimpleComment("Health check interval count for defined static records in seconds")]
        public int DDns_HealthCheck_IntervalSecs = 0;

        [SimpleComment("Health check try count for defined static records")]
        public int DDns_HealthCheck_NumTry = 0;

        [SimpleComment("Health check TCP and SSL connection giveup timeout for defined static records in milliseconds")]
        public int DDns_HealthCheck_TimeoutMsecs = 0;

        [SimpleComment("Health check max allowed concurrent connections")]
        public int DDns_HealthCheck_NumConcurrentConnections = 0;

        [SimpleComment("DDNS host record statistics rapid update quota for read-only query: duration in milliseconds")]
        public int DDns_HostFastUpdateQuota_DurationMsecs = 5000;

        [SimpleComment("DDNS host record statistics rapid update quota for read-only query: max update count in duration")]
        public int DDns_HostFastUpdateQuota_MaxFastUpdateCountPerDuration = 20;


        protected override void NormalizeImpl()
        {
            DDns_HostFastUpdateQuota_DurationMsecs = Math.Min(DDns_HostFastUpdateQuota_DurationMsecs, 60 * 60 * 1000);

            DDns_TcpAxfrAllowedAcl = DDns_TcpAxfrAllowedAcl._NonNullTrim();

            DDns_Protocol_ProxyProtocolAcceptSrcIpAcl = EasyIpAcl.NormalizeRules(DDns_Protocol_ProxyProtocolAcceptSrcIpAcl, false, true);

            if (Hadb_ObjectStaleMarker_ObjNames_and_Seconds.Length == 0)
            {
                var tmpList = new List<string>();
                tmpList.Add($"{typeof(MikakaDDnsService.Host).Name} {DevCoresConfig.MikakaDDnsServiceSettings.HostRecord_StaleMarker_Secs}");
                this.Hadb_ObjectStaleMarker_ObjNames_and_Seconds = tmpList.ToArray();
            }

            if (DDns_Enum_By_Email_MaxCount <= 0) DDns_Enum_By_Email_MaxCount = 3000;
            if (DDns_Enum_By_UserGroupSecretKey_MaxCount <= 0) DDns_Enum_By_UserGroupSecretKey_MaxCount = 5000;

            if (DDns_DomainName == null || DDns_DomainName.Any() == false)
            {
                var tmpList = new List<string>();
                tmpList.Add("ddns_example.net");
                tmpList.Add("ddns_example.org");
                tmpList.Add("ddns_example.com");
                DDns_DomainName = tmpList.ToArray();
                DDns_DomainNamePrimary = "ddns_example.net";
            }

            HashSet<string> tmp = new HashSet<string>();
            foreach (var domainName in DDns_DomainName)
            {
                string domainName2 = domainName._NormalizeFqdn();
                if (domainName2._IsFilled())
                {
                    tmp.Add(domainName2);
                }
            }
            DDns_DomainName = tmp.Distinct(StrCmpi).ToArray();

            DDns_DomainNamePrimary = DDns_DomainNamePrimary._NormalizeFqdn();

            if (DDns_DomainName.Contains(DDns_DomainNamePrimary) == false || DDns_DomainNamePrimary._IsEmpty())
            {
                if (DDns_DomainName.Length >= 1)
                {
                    DDns_DomainNamePrimary = DDns_DomainName[0];
                }
                else
                {
                    DDns_DomainNamePrimary = "";
                }
            }

            if (DDns_MaxHostPerCreateClientIpAddress_Total <= 0) DDns_MaxHostPerCreateClientIpAddress_Total = 1000;
            if (DDns_MaxHostPerCreateClientIpNetwork_Total <= 0) DDns_MaxHostPerCreateClientIpNetwork_Total = 10000;

            if (DDns_MaxHostPerCreateClientIpAddress_Daily <= 0) DDns_MaxHostPerCreateClientIpAddress_Daily = 100;
            if (DDns_MaxHostPerCreateClientIpNetwork_Daily <= 0) DDns_MaxHostPerCreateClientIpNetwork_Daily = 1000;

            if (DDns_NewHostnameRandomDigits <= 0 || DDns_NewHostnameRandomDigits >= 32) DDns_NewHostnameRandomDigits = 12;

            if (DDns_ProhibitedHostnamesStartWith._IsSamei("_initial_"))
                DDns_ProhibitedHostnamesStartWith = new string[] {
                   "ddns",
                   "api",
                   "rpc",
                   "www",
                   "dns",
                   "register",
                   "admin",
                   "ns0",
                   "sample",
                   "subdomain",
                   "ws-",
                   "websocket-",
                   "webapp-",
                   "app-",
                   "web-",
                   "login-",
                   "telework-",
                   "ipv4",
                   "ipv6",
                   "v4",
                   "v6",
                   "getmyip",
                   "ssl-cert-server",
                   "_acme",
                }._Combine(",");

            DDns_ProhibitedHostnamesStartWith = DDns_ProhibitedHostnamesStartWith._NonNullTrim()
                ._Split(StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries, ' ', ';', ',', '/', '\t')._Combine(",").ToLowerInvariant();




            if (DDns_ProhibitedHostnamesEndsWith._IsSamei("_initial_"))
                DDns_ProhibitedHostnamesEndsWith = new string[] {
                   "-v4",
                   "-v6",
                   "-ipv4",
                   "-ipv6",
                   "-ws",
                   "-websocket",
                   "-webapp",
                   "-app",
                   "-web",
                   "-login",
                   "-telework",
                }._Combine(",");

            DDns_ProhibitedHostnamesEndsWith = DDns_ProhibitedHostnamesEndsWith._NonNullTrim()
                ._Split(StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries, ' ', ';', ',', '/', '\t')._Combine(",").ToLowerInvariant();


            if (DDns_HostLabelLookup_IgnorePrefixStrings._IsSamei("_initial_"))
                DDns_HostLabelLookup_IgnorePrefixStrings = new string[] {
                   "ws-",
                   "websocket-",
                   "webapp-",
                   "app-",
                   "web-",
                   "login-",
                   "telework-",
                }._Combine(",");

            DDns_HostLabelLookup_IgnorePrefixStrings = DDns_HostLabelLookup_IgnorePrefixStrings._NonNullTrim()
                ._Split(StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries, ' ', ';', ',', '/', '\t')._Combine(",").ToLowerInvariant();





            if (DDns_HostLabelLookup_IgnoreSuffixStrings._IsSamei("_initial_"))
                DDns_HostLabelLookup_IgnoreSuffixStrings = new string[] {
                   "-ws",
                   "-websocket",
                   "-webapp",
                   "-app",
                   "-web",
                   "-login",
                   "-telework",
                }._Combine(",");

            DDns_HostLabelLookup_IgnoreSuffixStrings = DDns_HostLabelLookup_IgnoreSuffixStrings._NonNullTrim()
                ._Split(StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries, ' ', ';', ',', '/', '\t')._Combine(",").ToLowerInvariant();




            if (DDns_HostLabelLookup_DummySuffixListForIPv4Only._IsSamei("_initial_"))
                DDns_HostLabelLookup_DummySuffixListForIPv4Only = new string[] {
                   "-ipv4",
                   "-ipv4only",
                   "-v4",
                   "-v4only",
                }._Combine(",");

            DDns_HostLabelLookup_DummySuffixListForIPv4Only = DDns_HostLabelLookup_DummySuffixListForIPv4Only._NonNullTrim()
                ._Split(StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries, ' ', ';', ',', '/', '\t')._Combine(",").ToLowerInvariant();

            if (DDns_HostLabelLookup_DummySuffixListForIPv6Only._IsSamei("_initial_"))
                DDns_HostLabelLookup_DummySuffixListForIPv6Only = new string[] {
                   "-ipv6",
                   "-ipv6only",
                   "-v6",
                   "-v6only",
                }._Combine(",");

            DDns_HostLabelLookup_DummySuffixListForIPv6Only = DDns_HostLabelLookup_DummySuffixListForIPv6Only._NonNullTrim()
                ._Split(StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries, ' ', ';', ',', '/', '\t')._Combine(",").ToLowerInvariant();



            if (DDns_NewHostnamePrefix._IsEmpty()) DDns_NewHostnamePrefix = "neko";
            DDns_NewHostnamePrefix = DDns_NewHostnamePrefix._NonNullTrim().ToLowerInvariant()._MakeStringUseOnlyChars("0123456789abcdefghijklmnopqrstuvwxyz");

            if (DDns_MinHostLabelLen <= 0) DDns_MinHostLabelLen = 3;
            if (DDns_MaxHostLabelLen <= 0) DDns_MaxHostLabelLen = 32;
            if (DDns_MaxHostLabelLen >= 64) DDns_MaxHostLabelLen = 63;

            if (DDns_Protocol_Ttl_Secs <= 0) DDns_Protocol_Ttl_Secs = 60;
            if (DDns_Protocol_Ttl_Secs >= 3600) DDns_Protocol_Ttl_Secs = 3600;

            if (DDns_Protocol_Ttl_Secs_Static_Record <= 0) DDns_Protocol_Ttl_Secs_Static_Record = 15 * 60;
            if (DDns_Protocol_Ttl_Secs_Static_Record >= 3600) DDns_Protocol_Ttl_Secs_Static_Record = 3600;

            if (DDns_Protocol_Ttl_Secs_Static_Record_With_HealthCheck <= 0) DDns_Protocol_Ttl_Secs_Static_Record_With_HealthCheck = 2 * 60;
            if (DDns_Protocol_Ttl_Secs_Static_Record_With_HealthCheck >= 3600) DDns_Protocol_Ttl_Secs_Static_Record_With_HealthCheck = 3600;

            if (DDns_Protocol_SOA_NegativeCacheTtlSecs <= 0) DDns_Protocol_SOA_NegativeCacheTtlSecs = 13;
            if (DDns_Protocol_SOA_NegativeCacheTtlSecs >= 3600) DDns_Protocol_SOA_NegativeCacheTtlSecs = 3600;

            if (DDns_Protocol_SOA_RefreshIntervalSecs <= 0) DDns_Protocol_SOA_RefreshIntervalSecs = 60;
            if (DDns_Protocol_SOA_RefreshIntervalSecs >= 3600) DDns_Protocol_SOA_RefreshIntervalSecs = 3600;

            if (DDns_Protocol_SOA_RetryIntervalSecs <= 0) DDns_Protocol_SOA_RetryIntervalSecs = 60;
            if (DDns_Protocol_SOA_RetryIntervalSecs >= 3600) DDns_Protocol_SOA_RetryIntervalSecs = 3600;

            if (DDns_Protocol_SOA_ExpireIntervalSecs <= 0) DDns_Protocol_SOA_ExpireIntervalSecs = 3600 * 24 * 1024;
            if (DDns_Protocol_SOA_ExpireIntervalSecs >= 3600 * 24 * 365 * 5) DDns_Protocol_SOA_ExpireIntervalSecs = 3600 * 24 * 365 * 5;

            if (DDns_Protocol_SOA_MasterNsServerFqdn._IsEmpty()) DDns_Protocol_SOA_MasterNsServerFqdn = "ns01.ddns_example.net";
            DDns_Protocol_SOA_MasterNsServerFqdn = DDns_Protocol_SOA_MasterNsServerFqdn._NormalizeFqdn();

            if (DDns_Protocol_SOA_ResponsibleFieldFqdn._IsEmpty()) DDns_Protocol_SOA_ResponsibleFieldFqdn = "nobody.ddns_example.net";
            DDns_Protocol_SOA_ResponsibleFieldFqdn = DDns_Protocol_SOA_ResponsibleFieldFqdn._NormalizeFqdn();

            if (DDns_StaticRecord.Length == 0)
            {
                string myGlobalIPv4 = "";
                string myGlobalIPv6 = "";

                string healthCheckIPv4 = " ! health_check_url=https://<THIS_IP>/health_check/";
                string healthCheckIPv6 = " ! health_check_url=https://[<THIS_IP>]/health_check/";

                try
                {
                    myGlobalIPv4 = GetMyIpClient.GetMyIpSimpleAsync(IPVersion.IPv4)._GetResult().ToString();
                }
                catch { }

                try
                {
                    myGlobalIPv6 = GetMyIpClient.GetMyIpSimpleAsync(IPVersion.IPv6)._GetResult().ToString();
                }
                catch { }

                if (myGlobalIPv4._IsEmpty())
                {
                    myGlobalIPv4 = "1.2.3.4 ! this is default sample IP. change it.";
                }

                string initialRecordsList = $@"
NS @ ns01.@
NS @ ns02.@

A ns01 {myGlobalIPv4} ! do not specify health check in this record.
A ns02 {myGlobalIPv4} ! do not specify health check in this record.

A @ {myGlobalIPv4}{healthCheckIPv4}
A v4 {myGlobalIPv4}{healthCheckIPv4}

"

+

(myGlobalIPv6._IsFilled() ?

$@"

AAAA @ {myGlobalIPv6}{healthCheckIPv6}
AAAA v6 {myGlobalIPv6}{healthCheckIPv6}

" : "")

+

$@"

A ssl-cert-server 4.3.2.1 ! this is default sample IP. change it.
A ssl-cert-server-v4 4.3.2.1 ! this is default sample IP. change it.
AAAA ssl-cert-server 2001:af80::4321 ! this is default sample IP. change it.
AAAA ssl-cert-server-v6 2001:af80::4321 ! this is default sample IP. change it.

CNAME www @
CNAME api @
CNAME ipv4 v4.@
CNAME api-v4 v4.@
CNAME api-v4-static v4.@
CNAME ddns-api-v4 v4.@
CNAME ddns-api-v4-static v4.@
CNAME ipv6 v6.@
CNAME api-v6 v6.@
CNAME api-v6-static v6.@
CNAME ddns-api-v6 v6.@
CNAME ddns-api-v6-static v6.@
CNAME getmyip @
CNAME getmyip-v4 v4.@
CNAME getmyip-v6 v6.@
CNAME getmyip-v4-static v4.@
CNAME getmyip-v6-static v6.@

A sample1 5.9.6.3
A sample2 5.6.7.8
AAAA sample2 2401:af80::1234

A sample3 1.1.1.1
A sample3 2.2.2.2
A sample3 3.3.3.3
A sample3 4.4.4.4
AAAA sample3 2401:af80::dead:beef
AAAA sample3 2401:af80::cafe:8945
AAAA sample3 2401:af80::abcd:1234
AAAA sample3 2401:af80::5678:cafe

A *.sample4 5.9.6.1
A *.sample4 5.9.6.2
AAAA *.sample4 2401:af80::1234:cafe:8945
AAAA *.sample4 2401:af80::1234:5678:cafe

A sample4 8.0.0.1
AAAA sample4 2401:af80::1:2:3:4

CNAME sample4 www1.your_company.net

CNAME sample5 www2.your_company.net
CNAME sample5 www3.your_company.net

NS subdomain1 subdomain_ns1.your_company.com
NS subdomain1 subdomain_ns2.your_company.net

NS subdomain2 subdomain_ns3.your_company.co.jp
NS subdomain2 subdomain_ns4.your_company.ad.jp

NS _acme-challenge ssl-cert-server-v4.@

MX @ mail1.your_company.net 100
MX @ mail2.your_company.net 200
TXT @ v=spf1 ip4:130.158.0.0/16 ip4:133.51.0.0/16 ip6:2401:af80::/32 include:spf2.@ ?all
TXT @ v=spf2 ip4:8.8.8.0/24 ip6:2401:5e40::/32 ?all


MX sample2 mail3.your_company.net 100
MX sample2 mail4.your_company.net 200
TXT sample2 v=spf1 ip4:130.158.0.0/16 ip4:133.51.0.0/16 ip6:2401:af80::/32 include:spf2.sample2.@ ?all
TXT sample2 v=spf2 ip4:8.8.8.0/24 ip6:2401:5e40::/32 ?all

MX sample3 mail5.your_company.net 100
MX sample3 mail6.your_company.net 200
TXT sample3 v=spf1 ip4:130.158.0.0/16 ip4:133.51.0.0/16 ip6:2401:af80::/32 include:spf2.sample3.@ ?all
TXT sample3 v=spf2 ip4:8.8.8.0/24 ip6:2401:5e40::/32 ?all

SRV _ldap._tcp.abc 0 100 123 ldap_server.your_company.net


";

                List<string> records = new List<string>();
                foreach (var line in initialRecordsList._GetLines(removeEmpty: true, trim: true))
                {
                    records.Add(line);
                }

                this.DDns_StaticRecord = records.ToArray();
            }

            if (DDns_HealthCheck_IntervalSecs <= 0) DDns_HealthCheck_IntervalSecs = 60;

            if (DDns_HealthCheck_TimeoutMsecs <= 0) DDns_HealthCheck_TimeoutMsecs = 5 * 1000;
            if (DDns_HealthCheck_TimeoutMsecs >= 60 * 1000) DDns_HealthCheck_TimeoutMsecs = 60 * 1000;

            if (DDns_HealthCheck_NumTry <= 0) DDns_HealthCheck_NumTry = 3;
            if (DDns_HealthCheck_NumTry >= 10) DDns_HealthCheck_NumTry = 10;

            if (DDns_HealthCheck_NumConcurrentConnections <= 0) DDns_HealthCheck_NumConcurrentConnections = 8;
            if (DDns_HealthCheck_NumConcurrentConnections >= 256) DDns_HealthCheck_NumConcurrentConnections = 256;

            base.NormalizeImpl();
        }
    }

    public class UnlockKey : HadbData
    {
        public string Key = "";
        public DateTimeOffset CreatedTime = DtOffsetZero;
        public string CreateRequestedIpAddress = "";
        public string CreateRequestedFqdn = "";

        public override void Normalize()
        {
            Key = Key._NormalizeKey(true);
            CreatedTime = CreatedTime._NormalizeDateTimeOffset();
            CreateRequestedIpAddress = CreateRequestedIpAddress._NormalizeIp();
            CreateRequestedFqdn = CreateRequestedFqdn._NormalizeFqdn();
        }

        public static UnlockKey _Sample => new UnlockKey
        {
            CreatedTime = DtOffsetSample(0.1),
            CreateRequestedFqdn = "abc.example.org",
            CreateRequestedIpAddress = "1.2.3.4",
            Key = "012345678901234567890123456789012345",
        };

        public override int GetMaxArchivedCount() => 3;
        public override HadbKeys GetKeys() => new HadbKeys(this.Key);
    }

    [Flags]
    public enum HostApiResult
    {
        NoChange = 0,
        Created,
        Modified,
    }

    public class Host_Return : JsonRpcSingleReturnWithMetaData<Host>
    {
        [JsonConverter(typeof(StringEnumConverter))]
        public HostApiResult _ApiResult { get; }

        public string[] _HostFqdn { get; }

        public string _HostEasyGetInfoUrl { get; }
        public string _HostEasyUpdateUrl { get; }

        public override Host Data { get; }

        public Host_Return(Host data, HostApiResult apiResult, string[] hostFqdn, string hostEasyGetInfoUrl, string hostEasyUpdateUrl)
        {
            _ApiResult = apiResult;
            _HostFqdn = hostFqdn;
            Data = data;
            _HostEasyGetInfoUrl = hostEasyGetInfoUrl;
            _HostEasyUpdateUrl = hostEasyUpdateUrl;
        }

        public static Host_Return _Sample => new Host_Return(Host._Sample, HostApiResult.Created, new string[] { Host._Sample.HostLabel + ".ddns_example.org" }, "https://ddns_example.org/rpc/DDNS_Host/?secretKey=00112233445566778899AABBCCDDEEFF01020304", "https://ddns_example.org/rpc/DDNS_Host/?ip=myip&secretKey=00112233445566778899AABBCCDDEEFF01020304");
    }

    public class Host : HadbData
    {
        public string HostLabel = "";

        public string HostAddress_IPv4 = "";
        public string HostAddress_IPv6 = "";
        public string HostSecretKey = "";

        public DateTimeOffset CreatedTime = DtOffsetZero;

        public string AuthLogin_LastIpAddress = "";
        public string AuthLogin_LastFqdn = "";
        public long AuthLogin_Count = 0;

        public DateTimeOffset AuthLogin_FirstTime = DtOffsetZero;
        public DateTimeOffset AuthLogin_LastTime = DtOffsetZero;

        public DateTimeOffset ApiRateLimit_StartTime = DtOffsetZero;
        public long ApiRateLimit_CurrentCount = 0;
        public bool ApiRateLimit_Disabled = false;

        public string CreateRequestedIpAddress = "";
        public string CreateRequestedFqdn = "";
        public string CreateRequestedIpNetwork = "";

        public long HostLabel_NumUpdates = 0;
        public DateTimeOffset HostLabel_LastUpdateTime = DtOffsetZero;

        public DateTimeOffset HostAddress_IPv4_FirstUpdateTime = DtOffsetZero;
        public DateTimeOffset HostAddress_IPv4_LastUpdateTime = DtOffsetZero;
        public long HostAddress_IPv4_NumUpdates = 0;

        public DateTimeOffset HostAddress_IPv6_FirstUpdateTime = DtOffsetZero;
        public DateTimeOffset HostAddress_IPv6_LastUpdateTime = DtOffsetZero;
        public long HostAddress_IPv6_NumUpdates = 0;

        public DateTimeOffset DnsQuery_FirstAccessTime = DtOffsetZero;
        public DateTimeOffset DnsQuery_LastAccessTime = DtOffsetZero;
        public DateTimeOffset DnsQuery_LastAccessWithEDnsClientSubnetInfoTime = DtOffsetZero;
        public long DnsQuery_Count = 0;
        public string DnsQuery_FirstAccessDnsResolver = "";
        public string DnsQuery_FirstAccessEDnsClientSubnetInfo = "";
        public string DnsQuery_LastAccessDnsResolver = "";
        public string DnsQuery_LastAccessEDnsClientSubnetInfo = "";

        public string UsedUnlockKey = "";

        public string UserGroupSecretKey = "";
        public string Email = "";

        public JObject UserData = Json.NewJsonObject();

        public override HadbKeys GetKeys() => new HadbKeys(this.HostSecretKey, this.HostLabel, this.UsedUnlockKey);
        public override HadbLabels GetLabels() => new HadbLabels(this.UserGroupSecretKey, this.CreateRequestedIpAddress, this.CreateRequestedIpNetwork, this.Email);

        public override void Normalize()
        {
            this.HostSecretKey = this.HostSecretKey._NormalizeKey(true);
            this.HostLabel = this.HostLabel._NormalizeFqdn();
            this.UserGroupSecretKey = this.UserGroupSecretKey._NormalizeKey(true);

            this.CreatedTime = this.CreatedTime._NormalizeDateTimeOffset();

            this.CreateRequestedIpAddress = this.CreateRequestedIpAddress._NormalizeIp();
            this.CreateRequestedFqdn = this.CreateRequestedFqdn._NormalizeFqdn();
            this.CreateRequestedIpNetwork = this.CreateRequestedIpNetwork._NormalizeIp();

            this.HostAddress_IPv4 = this.HostAddress_IPv4._NormalizeIp();
            this.HostAddress_IPv4_LastUpdateTime = this.HostAddress_IPv4_LastUpdateTime._NormalizeDateTimeOffset();
            this.HostAddress_IPv4_FirstUpdateTime = this.HostAddress_IPv4_FirstUpdateTime._NormalizeDateTimeOffset();

            this.HostAddress_IPv6 = this.HostAddress_IPv6._NormalizeIp();
            this.HostAddress_IPv6_LastUpdateTime = this.HostAddress_IPv6_LastUpdateTime._NormalizeDateTimeOffset();
            this.HostAddress_IPv6_FirstUpdateTime = this.HostAddress_IPv6_FirstUpdateTime._NormalizeDateTimeOffset();

            this.UsedUnlockKey = this.UsedUnlockKey._NonNull();

            this.UserData = this.UserData._NormalizeEasyJsonStrAttributes();

            this.HostLabel_LastUpdateTime = this.HostLabel_LastUpdateTime._NormalizeDateTimeOffset();

            this.AuthLogin_LastIpAddress = this.AuthLogin_LastIpAddress._NormalizeIp();
            this.AuthLogin_LastFqdn = this.AuthLogin_LastFqdn._NormalizeFqdn();

            this.DnsQuery_FirstAccessTime = this.DnsQuery_FirstAccessTime._NormalizeDateTimeOffset();
            this.DnsQuery_LastAccessTime = this.DnsQuery_LastAccessTime._NormalizeDateTimeOffset();
            this.DnsQuery_LastAccessWithEDnsClientSubnetInfoTime = this.DnsQuery_LastAccessWithEDnsClientSubnetInfoTime._NormalizeDateTimeOffset();

            this.DnsQuery_FirstAccessDnsResolver = this.DnsQuery_FirstAccessDnsResolver._NonNullTrim();
            this.DnsQuery_FirstAccessEDnsClientSubnetInfo = this.DnsQuery_FirstAccessEDnsClientSubnetInfo._NonNullTrim();
            this.DnsQuery_LastAccessDnsResolver = this.DnsQuery_LastAccessDnsResolver._NonNullTrim();
            this.DnsQuery_LastAccessEDnsClientSubnetInfo = this.DnsQuery_LastAccessEDnsClientSubnetInfo._NonNullTrim();

            this.AuthLogin_FirstTime = this.AuthLogin_FirstTime._NormalizeDateTimeOffset();
            this.AuthLogin_LastTime = this.AuthLogin_LastTime._NormalizeDateTimeOffset();

            this.Email = this.Email._NonNullTrim();
        }

        public override int GetMaxArchivedCount() => DevCoresConfig.MikakaDDnsServiceSettings.HostRecordMaxArchivedCount;

        public static Host _Sample => new Host
        {
            HostLabel = "tanaka001",
            HostSecretKey = "00112233445566778899AABBCCDDEEFF01020304",
            AuthLogin_LastIpAddress = "10.20.30.40",
            AuthLogin_LastFqdn = "host1.example.org",
            CreateRequestedIpAddress = "3.4.5.6",
            CreateRequestedIpNetwork = "3.4.5.0",
            HostLabel_NumUpdates = 123,
            HostLabel_LastUpdateTime = DtOffsetSample(0.5),
            HostAddress_IPv4 = "8.9.3.1",
            HostAddress_IPv4_FirstUpdateTime = DtOffsetSample(0.1),
            HostAddress_IPv4_LastUpdateTime = DtOffsetSample(0.3),
            HostAddress_IPv4_NumUpdates = 384,
            HostAddress_IPv6 = "2401:AF80:1234:5678:dead:beef:cafe:8945",
            HostAddress_IPv6_FirstUpdateTime = DtOffsetSample(0.1),
            HostAddress_IPv6_LastUpdateTime = DtOffsetSample(0.3),
            HostAddress_IPv6_NumUpdates = 5963,
            DnsQuery_FirstAccessTime = DtOffsetSample(0.1),
            DnsQuery_LastAccessTime = DtOffsetSample(0.5),
            DnsQuery_Count = 12345,
            DnsQuery_FirstAccessDnsResolver = "1.9.8.4:1234",
            DnsQuery_LastAccessDnsResolver = "5.9.6.3:5678",
            DnsQuery_FirstAccessEDnsClientSubnetInfo = "5.6.7.0/24",
            DnsQuery_LastAccessEDnsClientSubnetInfo = "130.158.6.0/24",
            ApiRateLimit_CurrentCount = 123,
            ApiRateLimit_StartTime = DtOffsetSample(0.45),
            DnsQuery_LastAccessWithEDnsClientSubnetInfoTime = DtOffsetSample(0.48),
            UsedUnlockKey = "012345-678901-234567-890123-456789-012345",
            AuthLogin_Count = 121,
            AuthLogin_FirstTime = DtOffsetSample(0.05),
            AuthLogin_LastTime = DtOffsetSample(0.6),
            Email = "optos@example.org",
            UserGroupSecretKey = "33884422AAFFCCBB66992244AAAABBBBCCCCDDDD",
            UserData = "{'key1' : 'value1', 'key2' : 'value2'}"._JsonToJsonObject()!,
            CreatedTime = DtOffsetSample(0.1),
            CreateRequestedFqdn = "abc123.pppoe.example.org",
        };
    }

    public class MemDb : HadbBasedServiceMemDb
    {
        protected override void AddDefinedUserDataTypesImpl(List<Type> ret)
        {
            ret.Add(typeof(Host));
            ret.Add(typeof(UnlockKey));
        }

        protected override void AddDefinedUserLogTypesImpl(List<Type> ret)
        {
        }
    }

    public class HiveSettings : HadbBasedServiceHiveSettingsBase
    {
        public int DDns_UdpListenPort;
        public int DDns_TcpListenPort;

        public override void NormalizeImpl()
        {
            if (DDns_UdpListenPort <= 0) DDns_UdpListenPort = Consts.Ports.Dns;
            if (DDns_TcpListenPort <= 0) DDns_TcpListenPort = Consts.Ports.Dns;
        }
    }

    public class HostHistoryRecord
    {
        public long Version;
        public DateTimeOffset TimeStamp;
        public Host HostData = null!;

        public static HostHistoryRecord _Sample =>
            new HostHistoryRecord { HostData = Host._Sample, TimeStamp = DtOffsetSample(0.2), Version = 1, };
    }

    public class Test2Input
    {
        public int IntParam;
        public string StrParam = "";

        public static Test2Input _Sample => new Test2Input { IntParam = 32, StrParam = "Neko" };
    }

    public class Test2Output
    {
        public int IntParam;
        public string StrParam = "";

        public static Test2Output _Sample => new Test2Output { IntParam = 64, StrParam = "Hello" };
    }

    [RpcInterface]
    public interface IRpc : IHadbBasedServiceRpcBase
    {
        [RpcMethodHelp("テスト関数。パラメータで int 型で指定された値を文字列に変換し、Hello という文字列を前置して返却します。RPC を呼び出すためのテストコードを実際に記述する際のテストとして便利です。", "Hello 123")]
        public Task<string> Test_HelloWorld([RpcParamHelp("テスト入力整数値", 123)] int i);

        //[RpcMethodHelp("テスト関数2。")]
        //public Task<Test2Output> Test2([RpcParamHelp("テスト入力値1")] Test2Input in1, [RpcParamHelp("テスト入力値2")] string in2, [RpcParamHelp("テスト入力値3", HostApiResult.Modified)] HostApiResult in3);

        [RpcMethodHelp("DDNS ホストレコードを作成、更新または取得します。DDNS ホストレコードに関連付けられている IP アドレスの更新も、この API を呼び出して行ないます。")]
        public Task<Host_Return> DDNS_Host(
            [RpcParamHelp("ホストシークレットキーを指定します。新しいホストを作成する際には、新しいキーを指定してください。キーはクライアント側でランダムな 40 文字以内の半角英数字 (0-9、A-Z、ハイフン、アンダーバー の 38 種類の文字) を指定してください。通常は、20 バイトの乱数で生成したユニークなバイナリデータを 16 進数に変換したものを使用してください。既存のホストを更新する際には、既存のキーを指定してください。すでに存在するホストのキーが正確に指定された場合は、そのホストに関する情報の更新を希望するとみなされます。それ以外の場合は、新しいホストの作成を希望するとみなされます。ホストシークレットキーは、大文字・小文字を区別しません。ホストシークレットキーを省略した場合は、新たなホストを作成するものとみなされ、DDNS サーバー側でランダムでユニークなホストシークレットキーが新規作成されます。", "00112233445566778899AABBCCDDEEFF01020304")]
                string secretKey = "",

            [RpcParamHelp("新しいホストラベル (ホストラベルとは、ダイナミック DNS のホスト FQDN の先頭部分のホスト名に相当します。) を作成するか、または既存のホスト名を変更する場合は、作成または変更後の希望ホスト名を指定します。新しくホストを登録する場合で、かつ、希望ホスト名が指定されていない場合は、ランダムな文字列で新しいホスト名が作成されます。", "tanaka001")]
            string label = "",

            [RpcParamHelp("この DDNS サーバーで新しいホストを作成する際に、DDNS サーバーの運営者によって、登録キーの指定を必須としている場合は、未使用の登録キーを 1 つ指定します。登録キーは 36 桁の半角数字です。ハイフンは省略できます。登録キーは DDNS サーバーの運営者から発行されます。一度使用された登録キーは、再度利用することができなくなります。ただし、登録キーを用いて作成されたホストが削除された場合は、その登録キーを再び使用することができるようになります。ホストの更新時には、登録キーは省略できます。この DDNS サーバーが登録キーを不要としている場合は、登録キーは省略できます。", "012345-678901-234567-890123-456789-012345")]
            string unlockKey = "",

            [RpcParamHelp("この DDNS サーバーで新しいホストの作成または既存ホストの変更操作を行なう際に、DDNS サーバーの運営者によって、固定使用許諾文字列の指定を必須としている場合は、固定使用許諾文字列を指定します。固定使用許諾文字列は DDNS サーバーの運営者から通知されます。この DDNS サーバーに、そのような概念がない場合は、指定不要です。", "Hello")]
            string licenseString = "",

            [RpcParamHelp("ホストの IP アドレスを登録または更新するには、登録したい新しい IP アドレスを指定します。IP アドレスは明示的に文字列で指定することもできますが、\"myip\" という固定文字列を指定すると、この API の呼び出し元であるホストのグローバル IP アドレスを指定したものとみなされます。なお、IPv4 アドレスと IPv6 アドレスの両方を登録することも可能です。この場合は、IPv4 アドレスと IPv6 アドレスの両方を表記し、その間をカンマ文字 ',' で区切ります。IPv4 アドレスと IPv6 アドレスは、1 つずつしか指定できません。", "myip")]
            string ip = "",

            [RpcParamHelp("ユーザーグループシークレットキーを設定または変更する場合は、ユーザーグループシークレットキーを指定します。ユーザーグループシークレットキーはクライアント側でランダムな 40 文字以内の半角英数字 (0-9、A-Z、ハイフン、アンダーバー の 38 種類の文字) を指定してください。大文字・小文字を区別しません。通常は、20 バイトの乱数で生成したユニークなバイナリデータを 16 進数に変換したものを使用してください。複数のホストで、同一のユーザーグループシークレットキーを指定することが可能ですし、それが便利です。ユーザーグループシークレットキーを指定している場合、ユーザーグループシークレットキーを用いたホストレコードの列挙 API を使用すると、同じユーザーグループシークレットキーが登録されているすべてのホストレコードの情報 (各ホストのシークレットキーを含む) を列挙することができます。そのため、ユーザーグループシークレットキーを登録しておけば、同じユーザーグループシークレットキーを有する各ホストレコードの一覧やホストシークレットキーを保持していなくても、いつでも紐付けられたホストレコードにアクセスすることができて便利です。したがって、ユーザーグループシークレットキーは厳重に秘密に管理する必要があります。 \"delete\" という文字列を指定すると、すでに登録されているユーザーグループシークレットキーを消去することができます。", "33884422AAFFCCBB66992244AAAABBBBCCCCDDDD")]
            string userGroupSecretKey = "",

            [RpcParamHelp("このホストレコードの所有者の連絡先メールアドレスを設定するためには、メールアドレス文字列を指定します。メールアドレスは省略することができます。\"delete\" という文字列を指定すると、すでに登録されているメールアドレスを消去することができます。メールアドレスは、この DDNS サーバーのシステム管理者のみが閲覧することができます。DDNS サーバーのシステム管理者は、DDNS サーバーに関するメンテナンスのお知らせを、指定したメールアドレス宛に送付することができます。また、メールアドレスを登録しておけば、ホストキーの回復用 API を呼び出すことにより、そのメールアドレスに紐付いたすべてのホストシークレットキーの一覧を電子メールで送付することができます。これにより、ホストシークレットキーを紛失した場合でも、予めメールアドレスを登録しておくことにより、救済が可能です。", "optos@example.org")]
            string email = "",

            [RpcParamHelp("このパラメータを指定すると、DDNS ホストレコードに付随する永続的なユーザーデータとして、任意の JSON データを記録することができます。記録される JSON データの内容は、DDNS の動作に影響を与えません。たとえば、個人的なメモ等を記録することができます。記録内容は、Key-Value 形式の文字列である必要があります。Key の値は、重複してはなりません。", "{'key1' : 'value1', 'key2' : 'value2'}")]
                JObject? userData = null);

        [RpcMethodHelp("DDNS ホストレコードの最近 10 件の更新履歴を取得します。更新履歴は、ホストレコードの主要情報 (ホストラベル、関連付けられている IP アドレス、その他の登録情報) が変更されるたびに追加されます。")]
        public Task<HostHistoryRecord[]> DDNS_HostGetHistory(
            [RpcParamHelp("履歴を取得したいホストレコードのシークレットキーを指定します。", "00112233445566778899AABBCCDDEEFF01020304")]
            string secretKey
            );

        [RpcMethodHelp("DDNS ホストレコードを削除します。ホストレコードの削除操作は回復することができません。十分注意してください。ただし、一度削除したホストレコードのホストラベルと同じ名前のホストラベルを有するホストレコードを再度作成することは可能です。")]
        public Task DDNS_HostDelete(
            [RpcParamHelp("削除したいホストレコードのシークレットキーを指定します。", "00112233445566778899AABBCCDDEEFF01020304")]
            string secretKey
            );

        [RpcMethodHelp("登録済みの DDNS ホストレコードのホストシークレットキーを紛失した場合に、電子メールを用いて回復をします。予め DDNS_Host API を用いてメールアドレスが登録されている必要があります。", "Email sent to your mail address 'optos@example.org'. Please check your inbox.")]
        public Task<string> DDNS_HostRecoveryByEmail(
            [RpcParamHelp("DDNS_Host API で予め設定されているホストレコードの所有者の連絡先メールアドレス文字列を指定します。このメールアドレス宛にホストシークレットキーを含んだ回復メールが送信されます。ただし、大量のレコードが登録されている場合、メールで送信される件数は最大 3000 件 (デフォルト設定の場合) です。", "optos@example.org")]
            string email
            );

        [RpcMethodHelp("ホストレコードに予めユーザーグループシークレットキー文字列が指定されている場合、その文字列に一致するすべてのホストを列挙し、ホストキーも取得します。")]
        public Task<Host[]> DDNS_HostEnumByUserGroupSecretKey(
            [RpcParamHelp("DDNS_Host API で予め設定されているユーザーグループシークレットキー文字列を指定します。このキーが一致するすべてのホストが列挙され、ホストキーも取得できます。ただし、列挙可能なレコード数は最大 5000 件 (デフォルト設定の場合) です。ユーザーグループシークレットキー文字列は、大文字・小文字を区別しません。", "33884422AAFFCCBB66992244AAAABBBBCCCCDDDD")]
            string userGroupSecretKey
            );

        [RpcRequireAuth]
        [RpcMethodHelp("新しい登録キーを作成します。登録キーを作成すると、作成された登録キーの一覧が JSON 形式で返却されます。同時に多数個の登録キーを作成することも可能です。")]
        public Task<UnlockKey[]> DDNSAdmin_UnlockKeyCreate(
            [RpcParamHelp("作成したい登録キーの個数を指定します。指定しない場合は、1 個の登録キーを作成します。多数の登録キーを作成しようとすると、時間がかかる場合があります。", "28")]
            int count = 1
            );

    }

    public EasyDnsResponderBasedDnsServer DnsServer { get; private set; } = null!;

    AsyncLoopManager LoopManager = null!;

    public MikakaDDnsService(MikakaDDnsServiceStartupParam startupParam, MikakaDDnsServiceHook hook) : base(startupParam, hook)
    {
        try
        {
        }
        catch
        {
            this._DisposeSafe();
            throw;
        }
    }

    protected override void StartImpl()
    {
        this.DnsServer = new EasyDnsResponderBasedDnsServer(
            new EasyDnsResponderBasedDnsServerSettings
            {
                UdpPort = this.SettingsFastSnapshot.DDns_UdpListenPort,
                TcpPort = this.SettingsFastSnapshot.DDns_TcpListenPort,
            }
            );

        this.LoopManager = new AsyncLoopManager(new AsyncLoopManagerSettings(this.ReloadLoopTaskAsync));

        this.HadbEventListenerList.RegisterCallback(async (caller, type, state, param) =>
        {
            switch (type)
            {
                case HadbEventType.DynamicConfigChanged:
                    this.LoopManager.Fire();
                    break;

                case HadbEventType.ReloadDataFull:
                case HadbEventType.ReloadDataPartially:
                case HadbEventType.DatabaseStateChangedToRecovery:
                    break;
            }

            if (type == HadbEventType.DatabaseStateChangedToFailure || type == HadbEventType.DatabaseStateChangedToRecovery)
            {
                // DB への接続状態が変化した (エラー → 成功 / 成功 → エラー) の場合は直ちに LoopManager を Fire して Config を Reload する
                this.LoopManager.Fire();
            }

            await Task.CompletedTask;
        });
    }

    protected override async Task StopImplAsync(Exception? ex)
    {
        await this.LoopManager._DisposeSafeAsync(ex);

        await this.DnsServer._DisposeSafeAsync(ex);
    }

    protected override async Task<OkOrExeption> HealthCheckImplAsync(CancellationToken cancel)
    {
        await Task.CompletedTask;

        this.Hadb.CheckIfReady(true);

        return new OkOrExeption();
    }

    string MyGlobalIPv4 = "";
    string MyGlobalIPv6 = "";

    // HADB の DynamicConfig を元に DDNS サーバーの設定を構築してリロードする
    async Task ReloadLoopTaskAsync(AsyncLoopManager manager, CancellationToken cancel)
    {
        try
        {
            this.MyGlobalIPv4 = (await GetMyIpClient.GetMyIpSimpleAsync(IPVersion.IPv4)).ToString();
        }
        catch { }

        try
        {
            this.MyGlobalIPv6 = (await GetMyIpClient.GetMyIpSimpleAsync(IPVersion.IPv6)).ToString();
        }
        catch { }

        var config = this.Hadb.CurrentDynamicConfig;

        EasyDnsResponderSettings settings = new EasyDnsResponderSettings
        {
            DefaultSettings = new EasyDnsResponderRecordSettings
            {
                TtlSecs = config.DDns_Protocol_Ttl_Secs,
            },
        };

        // DDns_StaticRecord に付いている ! のコメント文により Health Check 等の条件分岐を行なう
        var staticRecordsList_Original = config.DDns_StaticRecord.ToList();

        List<string> staticRecordsList = new();

        HashSetDictionary<string, string> healthCheckTargetUrlDict = new();

        foreach (var item in staticRecordsList_Original)
        {
            staticRecordsList.Add(item);

            string comment = "";
            string staticRecordStr;

            int commentCharIndex = item._Search("!");
            if (commentCharIndex != -1)
            {
                comment = item.Substring(commentCharIndex + 1).Trim();
                staticRecordStr = item.Substring(0, commentCharIndex).Trim();
            }
            else
            {
                staticRecordStr = item.Trim();
            }

            if (comment._IsFilled())
            {
                QueryStringList qs = new QueryStringList(comment, splitChar: ',', trimKeyAndValue: true);

                string url = qs._GetFirstValueOrDefault("health_check_url", StrCmpi);

                if (url._IsFilled())
                {
                    EasyDnsResponderRecord tmp = EasyDnsResponderRecord.FromString(staticRecordStr, config.DDns_DomainNamePrimary);

                    var ip = tmp.Contents._ToIPAddress(noExceptionAndReturnNull: true);
                    if (ip != null)
                    {
                        string ipstr = (ip.AddressFamily == AddressFamily.InterNetworkV6 ? "[" + ip.ToString() + "]" : ip.ToString());

                        url = url._ReplaceStr("[<THIS_IP>]", ipstr, false);
                        url = url._ReplaceStr("<THIS_IP>", ipstr, false);

                        healthCheckTargetUrlDict.Add(url, item);
                    }
                }
            }
        }

        if (healthCheckTargetUrlDict.Any())
        {
            ConcurrentDictionary<string, ResultOrExeption<string>> healthCheckResults = new();

            var isThisServerReady = this.Hadb.CheckIfReady(true, EnsureSpecial.Yes);

            // Health Check を実施する
            await TaskUtil.ForEachAsync(config.DDns_HealthCheck_NumConcurrentConnections, healthCheckTargetUrlDict.Keys,
                async (targetUrl, index, cancel) =>
                {
                    try
                    {
                        if (targetUrl._TryParseUrl(out Uri uri, out _))
                        {
                            string ip = uri.Host._RemoveQuotation('[', ']');
                            if ((MyGlobalIPv4._IsFilled() && ip._IsSameIPAddress(MyGlobalIPv4, noException: true)) || (MyGlobalIPv6._IsFilled() && ip._IsSameIPAddress(MyGlobalIPv6, noException: true)))
                            {
                                if (isThisServerReady.IsOk)
                                {
                                    healthCheckResults[targetUrl] = "Skip (regard as OK because the target IP address is this host's global IP address)";
                                }
                                else
                                {
                                    healthCheckResults[targetUrl] = isThisServerReady.Exception;
                                }
                                return;
                            }
                        }

                        var result = await MiscUtil.HttpHealthCheckAsync(targetUrl, config.DDns_HealthCheck_NumTry, config.DDns_HealthCheck_TimeoutMsecs, cancel);

                        if (result.IsError)
                        {
                            healthCheckResults[targetUrl] = result.Exception;
                        }
                        else
                        {
                            healthCheckResults[targetUrl] = "OK";
                        }
                    }
                    catch (Exception ex)
                    {
                        // 本来ここには到達しないはずだか念のため例外キャッチする
                        healthCheckResults[targetUrl] = ex;
                    }
                },
                cancel);

            // Health check の結果をデバッグログまたはエラーログに出力する
            StringWriter w = new StringWriter();
            w.WriteLine($"--- Health check results: OK = {healthCheckResults.Values.Where(x => x.IsOk).Count()}, Error = {healthCheckResults.Values.Where(x => x.IsError).Count()}");
            int index = 0;
            bool hasAnyError = false;
            foreach (var kv in healthCheckResults.OrderBy(x => x.Key, StrCmpi))
            {
                index++;
                if (kv.Value.IsOk)
                {
                    w.WriteLine($"URL {index} ( {kv.Key} ): Result = {kv.Value.Value}.");
                }
                else
                {
                    w.WriteLine($"URL {index} ( {kv.Key} ): Result = Error. Details = {kv.Value.Exception.ToString()._OneLine()}");

                    var tmp = healthCheckTargetUrlDict._GetOrDefault(kv.Key, null);
                    if (tmp != null)
                    {
                        // Health check が失敗したものと関連する項目を staticRecordsList から削除する
                        tmp._DoForEach(x => staticRecordsList.Remove(x));
                    }

                    hasAnyError = true;
                }
            }
            w.WriteLine("--- Health check results end");

            // ログに出力
            if (hasAnyError == false)
            {
                w.ToString()._Debug();
            }
            else
            {
                w.ToString()._Error();
            }
        }

        foreach (var domainFqdn in config.DDns_DomainName)
        {
            var zone = new EasyDnsResponderZone
            {
                DefaultSettings = settings.DefaultSettings,
                DomainName = domainFqdn,
                TcpAxfrAllowedAcl = config.DDns_TcpAxfrAllowedAcl,
            };

            foreach (var item in staticRecordsList)
            {
                // 再度コメント除去
                string comment = "";
                string staticRecordStr;
                int ttl = config.DDns_Protocol_Ttl_Secs_Static_Record;

                int commentCharIndex = item._Search("!");
                if (commentCharIndex != -1)
                {
                    comment = item.Substring(commentCharIndex + 1).Trim();
                    staticRecordStr = item.Substring(0, commentCharIndex).Trim();

                    if (comment._IsFilled())
                    {
                        QueryStringList qs = new QueryStringList(comment, splitChar: ',', trimKeyAndValue: true);

                        string url = qs._GetFirstValueOrDefault("health_check_url", StrCmpi);

                        if (url._IsFilled())
                        {
                            // health_check が記載されている場合は TTL を短くする
                            ttl = config.DDns_Protocol_Ttl_Secs_Static_Record_With_HealthCheck;
                        }
                    }
                }
                else
                {
                    staticRecordStr = item.Trim();
                }

                try
                {
                    EasyDnsResponderRecord rec = EasyDnsResponderRecord.FromString(staticRecordStr, domainFqdn);

                    rec.Settings = new EasyDnsResponderRecordSettings { TtlSecs = ttl };

                    zone.RecordList.Add(rec);
                }
                catch (Exception ex)
                {
                    ex._Error();
                }
            }

            zone.RecordList.Add(new EasyDnsResponderRecord { Type = EasyDnsResponderRecordType.Any, Name = "*", Contents = "ddns_ipv4", Attribute = EasyDnsResponderRecordAttribute.DynamicRecord });

            zone.RecordList.Add(new EasyDnsResponderRecord
            {
                Type = EasyDnsResponderRecordType.SOA,
                Contents = $"{config.DDns_Protocol_SOA_MasterNsServerFqdn} {config.DDns_Protocol_SOA_ResponsibleFieldFqdn} {Consts.Numbers.MagicNumberB_u32} {config.DDns_Protocol_SOA_RefreshIntervalSecs} {config.DDns_Protocol_SOA_RetryIntervalSecs} {config.DDns_Protocol_SOA_ExpireIntervalSecs} {config.DDns_Protocol_SOA_NegativeCacheTtlSecs}",
            });

            settings.ZoneList.Add(zone);
        }

        settings.SaveAccessLogForDebug = config.DDns_SaveDnsQueryAccessLogForDebug;
        settings.CopyQueryAdditionalRecordsToResponse = config.DDns_Protocol_CopyQueryAdditionalRecordsToResponse;

        this.DnsServer.ApplySetting(settings);

        var currentDynOptions = this.DnsServer.DnsServer.GetCurrentDynOptions();

        currentDynOptions.ParseUdpProxyProtocolV2 = config.DDns_Protocol_ParseUdpProxyProtocolV2;
        currentDynOptions.DnsProxyProtocolAcceptSrcIpAcl = config.DDns_Protocol_ProxyProtocolAcceptSrcIpAcl;

        this.DnsServer.DnsServer.SetCurrentDynOptions(currentDynOptions);

        string[]? prefixIgnoreList = null;
        if (config.DDns_HostLabelLookup_IgnorePrefixStrings._IsFilled())
        {
            prefixIgnoreList = config.DDns_HostLabelLookup_IgnorePrefixStrings._Split(StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries, ',')
                .OrderByDescending(x => x.Length).ToArray();
        }

        string[]? suffixIgnoreList = null;
        if (config.DDns_HostLabelLookup_IgnoreSuffixStrings._IsFilled())
        {
            suffixIgnoreList = config.DDns_HostLabelLookup_IgnoreSuffixStrings._Split(StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries, ',')
                .OrderByDescending(x => x.Length).ToArray();
        }

        string[]? v4onlySuffixList = null;
        if (config.DDns_HostLabelLookup_DummySuffixListForIPv4Only._IsFilled())
        {
            v4onlySuffixList = config.DDns_HostLabelLookup_DummySuffixListForIPv4Only._Split(StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries, ',')
                .OrderByDescending(x => x.Length).ToArray();
        }

        string[]? v6onlySuffixList = null;
        if (config.DDns_HostLabelLookup_DummySuffixListForIPv6Only._IsFilled())
        {
            v6onlySuffixList = config.DDns_HostLabelLookup_DummySuffixListForIPv6Only._Split(StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries, ',')
                .OrderByDescending(x => x.Length).ToArray();
        }


        this.DnsServer.DnsResponder.TcpAxfrCallback = async (req) =>
        {
            // 標準的な静的レコードリストの構築
            List<Tuple<EasyDnsResponder.Record, string?>> list = await req.GenerateStandardStaticRecordsListFromZoneDataAsync(req.Cancel);

            var rootVirtualZone = new EasyDnsResponder.Zone(isVirtualRootZone: EnsureSpecial.Yes, req.ZoneInternal);

            var hostList = this.Hadb.FastEnumObjects<Host>();

            List<string> tmpSuffixList = new List<string>();
            tmpSuffixList.Add("");
            suffixIgnoreList?._DoForEach(x => tmpSuffixList.Add(x));

            List<string> tmpPrefixList = new List<string>();
            tmpPrefixList.Add("");
            prefixIgnoreList?._DoForEach(x => tmpPrefixList.Add(x));

            List<string> tmpIPv4OnlySuffixList = new List<string>();
            tmpIPv4OnlySuffixList.Add("");
            v4onlySuffixList?._DoForEach(x => tmpIPv4OnlySuffixList.Add(x));

            List<string> tmpIPv6OnlySuffixList = new List<string>();
            tmpIPv6OnlySuffixList.Add("");
            v6onlySuffixList?._DoForEach(x => tmpIPv6OnlySuffixList.Add(x));

            foreach (var host in hostList)
            {
                List<Tuple<EasyDnsResponder.Record, string?>> tmpList = new List<Tuple<EasyDnsResponder.Record, string?>>();

                var data = host.GetData();

                string ipv4Str = data.HostAddress_IPv4;
                string ipv6Str = data.HostAddress_IPv6;

                string basicLabel = data.HostLabel;

                if (ipv4Str._IsFilled() && IPAddress.TryParse(ipv4Str, out var ipv4))
                {
                    HashSet<string> labels = new HashSet<string>(StrCmpi);

                    foreach (var suffix in tmpSuffixList)
                    {
                        foreach (var prefix in tmpPrefixList)
                        {
                            foreach (var onlySuffix in tmpIPv4OnlySuffixList)
                            {
                                labels.Add(prefix + basicLabel + onlySuffix + suffix);
                            }
                        }
                    }

                    foreach (var label in labels)
                    {
                        tmpList.Add(new Tuple<EasyDnsResponder.Record, string?>(new EasyDnsResponder.Record_A(req.ZoneInternal, req.ZoneInternal.Settings, label, ipv4), null));
                    }
                }

                if (ipv6Str._IsFilled() && IPAddress.TryParse(ipv6Str, out var ipv6))
                {
                    HashSet<string> labels = new HashSet<string>(StrCmpi);

                    foreach (var suffix in tmpSuffixList)
                    {
                        foreach (var prefix in tmpPrefixList)
                        {
                            foreach (var onlySuffix in tmpIPv6OnlySuffixList)
                            {
                                labels.Add(prefix + basicLabel + onlySuffix);
                            }
                        }
                    }

                    foreach (var label in labels)
                    {
                        tmpList.Add(new Tuple<EasyDnsResponder.Record, string?>(new EasyDnsResponder.Record_AAAA(req.ZoneInternal, req.ZoneInternal.Settings, label, ipv6), null));
                    }
                }

                await req.SendBufferedAsync(tmpList, cancel: cancel);
            }


            // 送付
            await req.SendBufferedAsync(list, req.Cancel, distinct: true, sort: true);
        };

        this.DnsServer.DnsResponder.DynamicRecordCallback = (req) =>
        {
            var config = this.Hadb.CurrentDynamicConfig;

            var ret = new EasyDnsResponderDynamicRecordCallbackResult
            {
                IPAddressList = new List<IPAddress>(),
                MxFqdnList = new List<DomainName>(),
                MxPreferenceList = new List<ushort>(),
                TextList = new List<string>(),
                CaaList = new List<Tuple<byte, string, string>>(),
            };

            string targetLabel = req.RequestHostName;

            if (config.DDns_HostLabelLookup_IgnoreAfterDoubleHyphon)
            {
                int i = targetLabel.IndexOf("--");
                if (i != -1)
                {
                    targetLabel = targetLabel.Substring(0, i);
                }
            }

            if (prefixIgnoreList != null)
            {
                foreach (string ign in prefixIgnoreList)
                {
                    if (targetLabel.StartsWith(ign, StringComparison.OrdinalIgnoreCase))
                    {
                        targetLabel = targetLabel.Substring(ign.Length);
                        break; // 省略は 1 回限り
                    }
                }
            }

            if (suffixIgnoreList != null)
            {
                foreach (string ign in suffixIgnoreList)
                {
                    if (targetLabel.EndsWith(ign, StringComparison.OrdinalIgnoreCase))
                    {
                        targetLabel = targetLabel.Substring(0, targetLabel.Length - ign.Length);
                        break; // 省略は 1 回限り
                    }
                }
            }

            bool flag_v4only = false;
            bool flag_v6only = false;

            if (v4onlySuffixList != null)
            {
                foreach (string suffix in v4onlySuffixList)
                {
                    if (targetLabel.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    {
                        targetLabel = targetLabel.Substring(0, targetLabel.Length - suffix.Length);

                        flag_v4only = true;
                        break;
                    }
                }
            }

            if (v6onlySuffixList != null && flag_v4only == false)
            {
                foreach (string suffix in v6onlySuffixList)
                {
                    if (targetLabel.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    {
                        targetLabel = targetLabel.Substring(0, targetLabel.Length - suffix.Length);

                        flag_v6only = true;
                        break;
                    }
                }
            }

            // ラベル名からレコードを解決
            HadbObject<Host>? host = null;

            if (targetLabel._IsFilled())
            {
                host = this.Hadb.FastSearchByKey<Host>(new Host { HostLabel = targetLabel });
            }

            if (host != null)
            {
                var data = host.Data;

                List<string> spfText = new List<string>();
                spfText.Add("v=spf1");

                if (data.HostAddress_IPv4._IsFilled())
                {
                    if (config.DDns_Prohibit_IPv4AddressRegistration == false && flag_v6only == false)
                    {
                        var ip = data.HostAddress_IPv4._StrToIP(AllowedIPVersions.IPv4, true);
                        if (ip != null)
                        {
                            ret.IPAddressList.Add(ip);
                            spfText.Add($"ip4:{ip}/32");
                        }
                    }
                }

                if (data.HostAddress_IPv6._IsFilled())
                {
                    if (config.DDns_Prohibit_IPv6AddressRegistration == false && flag_v4only == false)
                    {
                        var ip = data.HostAddress_IPv6._StrToIP(AllowedIPVersions.IPv6, true);
                        if (ip != null)
                        {
                            ret.IPAddressList.Add(ip);
                            spfText.Add($"ip6:{ip}/128");
                        }
                    }
                }

                spfText.Add("?all");

                ret.MxPreferenceList.Add(100);
                ret.MxFqdnList.Add(DomainName.Parse(req.RequestFqdn));
                ret.TextList.Add(spfText._Combine(" "));

                if (config.DDns_Prohibit_Acme_CertIssue_For_Hosts)
                {
                    ret.CaaList.Add(new Tuple<byte, string, string>(0, "issue", ";"));
                    ret.CaaList.Add(new Tuple<byte, string, string>(0, "issuewild", ";"));
                }

                var now = DtOffsetNow;

                host.FastUpdate(h =>
                {
                    h.DnsQuery_Count++;
                    if (h.DnsQuery_FirstAccessTime._IsZeroDateTime())
                    {
                        h.DnsQuery_FirstAccessTime = now;
                    }
                    if (h.DnsQuery_LastAccessTime < now)
                    {
                        h.DnsQuery_LastAccessTime = now;
                    }
                    if (req.RequestPacket != null)
                    {
                        string dnsResolver = req.RequestPacket.DnsResolver._NonNullTrim();
                        if (h.DnsQuery_FirstAccessDnsResolver._IsEmpty())
                        {
                            h.DnsQuery_FirstAccessDnsResolver = dnsResolver;
                        }
                        h.DnsQuery_LastAccessDnsResolver = dnsResolver;

                        string? subnetInfo = req.RequestPacket.EdnsClientSubnet;
                        if (subnetInfo._IsFilled())
                        {
                            if (h.DnsQuery_FirstAccessEDnsClientSubnetInfo._IsEmpty())
                            {
                                h.DnsQuery_FirstAccessEDnsClientSubnetInfo = subnetInfo;
                            }
                            h.DnsQuery_LastAccessEDnsClientSubnetInfo = subnetInfo;

                            if (h.DnsQuery_LastAccessWithEDnsClientSubnetInfoTime < now)
                            {
                                h.DnsQuery_LastAccessWithEDnsClientSubnetInfoTime = now;
                            }
                        }
                    }
                    return true;
                },
                true,
                new HadbFastUpdateOptions { QuotaDurationMsecs = config.DDns_HostFastUpdateQuota_DurationMsecs, QuotaMaxFastUpdateCountPerDuration = config.DDns_HostFastUpdateQuota_MaxFastUpdateCountPerDuration });
            }
            else
            {
                ret.NotFound = true;
            }

            return ret;
        };

        if (config.DDns_HealthCheck_IntervalSecs >= 1)
        {
            manager.LoopIntervalMsecs = config.DDns_HealthCheck_IntervalSecs * 1000;
        }
    }

    protected override DynConfig CreateInitialDynamicConfigImpl()
    {
        return new DynConfig();
    }

    public List<string> GetDomainNamesList()
    {
        List<string> ret = new List<string>();

        foreach (var x in Hadb.CurrentDynamicConfig.DDns_DomainName)
        {
            if (x._IsSamei(Hadb.CurrentDynamicConfig.DDns_DomainNamePrimary))
            {
                ret.Add(x);
            }
        }

        foreach (var x in Hadb.CurrentDynamicConfig.DDns_DomainName)
        {
            if (x._IsDiffi(Hadb.CurrentDynamicConfig.DDns_DomainNamePrimary))
            {
                ret.Add(x);
            }
        }

        return ret;
    }

    public Task<string> Test_HelloWorld(int i) => $"Hello {i}"._TaskResult();

    public async Task<Host_Return> DDNS_Host(string secretKey = "", string label = "", string unlockKey = "", string licenseString = "", string ip = "", string userGroupSecretKey = "", string email = "", JObject? userData = null)
    {
        var config = Hadb.CurrentDynamicConfig;

        var client = this.GetClientInfo();

        var now = DtOffsetNow;

        IPAddress clientIp = this.GetClientIpAddress();
        IPAddress clientNetwork = this.GetClientIpNetworkForRateLimit();
        string clientNameStr = clientIp.ToString();

        // パラメータの検査と正規化
        secretKey = secretKey._NonNullTrim().ToUpperInvariant();

        if (secretKey._IsFilled())
        {
            secretKey._CheckUseOnlyChars($"Specified {nameof(secretKey)} contains invalid character.", "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ-_");
            secretKey._CheckStrLenException(40, $"Specified {nameof(secretKey)} is too long.");
        }
        else
        {
            // hostSecretKey が指定されていない場合は、新たに乱数で指定されたものとみなす
            secretKey = Str.GenRandStr();
        }

        var uri = client.HttpUrl._ParseUrl();
        string easyUpdateUrl = uri._CombineUrl($"/rpc/{nameof(DDNS_Host)}/?ip=myip&secretKey={secretKey}").ToString();
        string easyGetInfoUrl = uri._CombineUrl($"/rpc/{nameof(DDNS_Host)}/?secretKey={secretKey}").ToString();

        label = label._NonNullTrim().ToLowerInvariant();

        if (label._IsFilled())
        {
            label._CheckUseOnlyChars($"Specified {nameof(label)} contains invalid character", "0123456789abcdefghijklmnopqrstuvwxyz-");

            if (label.Length < config.DDns_MinHostLabelLen)
                throw new CoresException($"Specified {nameof(label)} is too short. {nameof(label)} must be longer or equal than {config.DDns_MinHostLabelLen} letters.");

            label._CheckStrLenException(config.DDns_MaxHostLabelLen, $"Specified {nameof(label)} is too long. {nameof(label)} must be shorter or equal than {config.DDns_MaxHostLabelLen} letters.");

            foreach (var item in config.DDns_ProhibitedHostnamesStartWith._NonNullTrim()
                ._Split(StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries, ' ', ';', ',', '/', '\t'))
            {
                if (item._IsFilled())
                {
                    string tmp = item.ToLowerInvariant();
                    if (label.StartsWith(tmp, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new CoresException($"Specified {nameof(label)} contains prohibited strings. Please try to use another.");
                    }
                }
            }

            foreach (var item in config.DDns_ProhibitedHostnamesEndsWith._NonNullTrim()
                ._Split(StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries, ' ', ';', ',', '/', '\t'))
            {
                if (item._IsFilled())
                {
                    string tmp = item.ToLowerInvariant();
                    if (label.EndsWith(tmp, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new CoresException($"Specified {nameof(label)} contains prohibited strings. Please try to use another.");
                    }
                }
            }


            if (label.StartsWith("-", StringComparison.OrdinalIgnoreCase) ||
                label.EndsWith("-", StringComparison.OrdinalIgnoreCase))
            {
                throw new CoresException($"Specified {nameof(label)} must not start with or end with hyphon (-) character.");
            }

            if (label._InStri("--"))
                throw new CoresException($"Specified {nameof(label)} must not contain double hyphon (--) string.");
        }

        unlockKey = unlockKey._MakeStringUseOnlyChars("0123456789");
        licenseString = licenseString._NonNull();

        bool requireUnlockKey = config.DDns_RequireUnlockKey;
        if (requireUnlockKey == false)
        {
            unlockKey = "";
        }

        ip = ip._NonNullTrim();

        IPAddress? ipv4 = null;
        IPAddress? ipv6 = null;
        string ipv4str = "";
        string ipv6str = "";

        void InitIpAddressVariable()
        {
            if (ip._IsFilled())
            {
                if (ip.StartsWith("my", StringComparison.OrdinalIgnoreCase))
                {
                    if (clientIp.AddressFamily == AddressFamily.InterNetwork)
                        ipv4 = clientIp;
                    else if (clientIp.AddressFamily == AddressFamily.InterNetworkV6)
                        ipv6 = clientIp;
                    else
                        throw new CoresException("clientIp has invalid network family.");
                }
                else
                {
                    var ipTokens = ip._Split(StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries, ',', ' ', '\t', ';');

                    if (ipTokens.Length == 0)
                    {
                        ip = "";
                    }
                    else if (ipTokens.Length == 1)
                    {
                        var ip = ipTokens[0]._ToIPAddress(AllowedIPVersions.All, false)!;

                        if (ip.AddressFamily == AddressFamily.InterNetwork)
                            ipv4 = ip;
                        else if (ip.AddressFamily == AddressFamily.InterNetworkV6)
                            ipv6 = ip;
                        else
                            throw new CoresException($"{nameof(ip)} has invalid network family.");
                    }
                    else if (ipTokens.Length == 2)
                    {
                        for (int i = 0; i < ipTokens.Length; i++)
                        {
                            var ip = ipTokens[i]._ToIPAddress(AllowedIPVersions.All, false)!;

                            if (ip.AddressFamily == AddressFamily.InterNetwork)
                            {
                                if (ipv4 == null)
                                {
                                    ipv4 = ip;
                                }
                                else
                                {
                                    throw new CoresException($"{nameof(ip)} contains two or more IPv4 addresses");
                                }
                            }
                            else if (ip.AddressFamily == AddressFamily.InterNetworkV6)
                            {
                                if (ipv6 == null)
                                {
                                    ipv6 = ip;
                                }
                                else
                                {
                                    throw new CoresException($"{nameof(ip)} contains two or more IPv6 addresses");
                                }
                            }
                            else
                            {
                                throw new CoresException($"{nameof(ip)} has invalid network family.");
                            }
                        }
                    }
                    else
                    {
                        throw new CoresException($"{nameof(ip)} has invalid format.");
                    }
                }
            }

            ipv4 = ipv4._RemoveScopeId();
            ipv6 = ipv6._RemoveScopeId();

            ipv4str = ipv4?.ToString() ?? "";
            ipv6str = ipv6?.ToString() ?? "";

            if (ipv4 != null)
            {
                if (config.DDns_Prohibit_IPv4AddressRegistration)
                {
                    throw new CoresException($"You specified the IPv4 address '{ipv4.ToString()}', however this DDNS server is prohibiting any IPv4 address registration.");
                }
            }

            if (ipv6 != null)
            {
                if (config.DDns_Prohibit_IPv6AddressRegistration)
                {
                    throw new CoresException($"You specified the IPv6 address '{ipv6.ToString()}', however this DDNS server is prohibiting any IPv6 address registration.");
                }
            }
        }

        InitIpAddressVariable();

        userGroupSecretKey = userGroupSecretKey._NonNullTrim().ToUpperInvariant();

        if (userGroupSecretKey._IsFilled())
        {
            userGroupSecretKey._CheckUseOnlyChars($"Specified {nameof(userGroupSecretKey)} contains invalid character.", "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ-_");
            userGroupSecretKey._CheckStrLenException(40, $"Specified {nameof(userGroupSecretKey)} is too long.");
        }

        email = email._NonNullTrim();
        email._CheckUseOnlyChars($"Specified {nameof(email)} contains invalid character", "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ-_@.");

        if (email._IsFilled() && email._IsSamei("delete") == false)
        {
            if (Str.CheckMailAddress(email) == false)
            {
                throw new CoresException($"{nameof(email)} has invalid email address format.");
            }
        }

        if (userData != null) userData = userData._NormalizeEasyJsonStrAttributes();

        string testJsonStr = userData._ObjectToJson(compact: true);
        if (testJsonStr.Length > config.DDns_MaxUserDataJsonStrLength)
        {
            throw new CoresException($"{nameof(userData)} is too large.");
        }

        HadbObject<Host>? retCurrentHostObj = null;

        // まず、指定されたホストシークレットキーに該当するホストがメモリデータベース上にあるかどうか調べる
        var memoryObj = Hadb.FastSearchByKey(new Host { HostSecretKey = secretKey });

        bool anyChangePossibility = true;
        bool needToCreate = false;

        if (memoryObj != null)
        {
            retCurrentHostObj = memoryObj;

            // メモリデータベースを指定したホストキーで検索してみる。
            // すでにメモリデータベース上にあれば、変更点がありそうかどうか確認する。もしメモリデータベース上で変更点がないのに、物理データベース上へのクエリを発生させても、CPU 時間が無駄になるだけの可能性があるためである。
            // ※ メモリデータベース上になければ、変更点がありそうかどうかの検査を保留する。つまり、物理データベースを叩いてみなければ分からない。
            anyChangePossibility = false;

            var current = memoryObj.Data;

            if (label._IsFilled() && current.HostLabel._IsSamei(label) == false) anyChangePossibility = true;
            if (ipv4str._IsFilled() || ipv6str._IsFilled())
            {
                if (current.HostAddress_IPv4._IsSamei(ipv4str) == false) anyChangePossibility = true;
                if (current.HostAddress_IPv6._IsSamei(ipv6str) == false) anyChangePossibility = true;
            }
            if (userGroupSecretKey._IsSamei("delete"))
            {
                if (current.UserGroupSecretKey._IsFilled()) anyChangePossibility = true;
            }
            else if (userGroupSecretKey._IsFilled())
            {
                if (current.UserGroupSecretKey._IsSamei(userGroupSecretKey) == false) anyChangePossibility = true;
            }
            if (email._IsSamei("delete"))
            {
                if (current.Email._IsFilled()) anyChangePossibility = true;
            }
            else if (email._IsFilled())
            {
                if (current.Email._IsSame(email) == false) anyChangePossibility = true;
            }

            if (userData != null)
            {
                if (current.UserData._ObjectToJson(compact: true)._IsSame(userData._ObjectToJson(compact: true)) == false) anyChangePossibility = true;
            }
        }

        DateTimeOffset newApiRateLimitStartDt = DtOffsetZero;

        if (memoryObj != null && anyChangePossibility)
        {
            // すでにオブジェクトが存在し、かつ、内容を変更することになる予定の場合、
            // これ以下の処理は DB への物理アクセスを伴いリソースを消費するため、
            // 1 時間あたりの変更の回数に制限を設ける。
            if (config.DDns_HostApi_RateLimit_Duration_Secs >= 1 && config.DDns_HostApi_RateLimit_MaxCounts_Per_Duration >= 1)
            {
                var host = memoryObj.Data;

                if (host.ApiRateLimit_Disabled == false)
                {
                    var expires = host.ApiRateLimit_StartTime.AddSeconds(config.DDns_HostApi_RateLimit_Duration_Secs);

                    if (host.ApiRateLimit_StartTime._IsZeroDateTime() || expires <= now)
                    {
                        newApiRateLimitStartDt = now;
                    }
                    else
                    {
                        if (host.ApiRateLimit_CurrentCount >= config.DDns_HostApi_RateLimit_MaxCounts_Per_Duration)
                        {
                            throw new CoresException($"Host API rate limit reached. Please wait for {(long)((expires - now).TotalSeconds)} seconds for next retry.");
                        }
                    }
                }
            }
        }

        if (anyChangePossibility)
        {
            // メモリデータベース上での変更点があるか、または、メモリデータベース上でオブジェクトが見つからないならば、物理データベースを指定されたホストキーで検索してみる。
            // ここでは、まだ、スナップショット読み取りモードで取得するのである。
            anyChangePossibility = false;

            HadbObject<Host>? dbObj = null;

            await Hadb.TranAsync(false, async tran =>
            {
                dbObj = null;

                dbObj = await tran.AtomicSearchByKeyAsync(new Host { HostSecretKey = secretKey });

                bool checkNewHostKey = false;

                if (dbObj != null)
                {
                    var current = dbObj.Data;
                    if (label._IsFilled() && current.HostLabel._IsSamei(label) == false)
                    {
                        checkNewHostKey = true;
                    }
                }
                else
                {
                    checkNewHostKey = true;

                    if (label._IsEmpty())
                    {
                        // 新しいホストを作成する場合で、newHostLabel が明示的に指定されていない場合は、prefix + 12 桁 (デフォルト) のランダムホスト名を使用する。
                        int numTry = 0;
                        while (true)
                        {
                            string candidate = config.DDns_NewHostnamePrefix + Str.GenerateRandomDigit(config.DDns_NewHostnameRandomDigits);

                            var existing = await tran.AtomicSearchByKeyAsync(new Host { HostLabel = candidate });

                            if (existing == null)
                            {
                                // 既存のホストと一致しない場合は、これに決める。
                                label = candidate;
                                break;
                            }

                            // 既存のホストと一致する場合は、一致しなくなるまで乱数で新たに生成を試みる。
                            numTry++;
                            if (numTry >= 10)
                            {
                                // 10 回トライして失敗したら諦める。
                                throw new CoresException("Failed to generate an unique hostname. Please try again later.");
                            }
                        }
                    }
                }

                if (checkNewHostKey)
                {
                    // ホストラベルの変更または新規作成を要求された場合は、新しいホストラベルが既存のものと重複しないかどうか確認する。
                    // 重複する場合は、ここで例外を発生させる。
                    // (一意キー制約によって重複は阻止されるが、ここで一応手動でチェックし、重複することが明らかである場合はわかりやすい例外を発生して要求を拒否するのである。)
                    var sameHostLabelExists = await tran.AtomicSearchByKeyAsync(new Host { HostLabel = label });

                    if (sameHostLabelExists != null)
                    {
                        throw new CoresException($"The same {nameof(label)} '{label}' already exists on the DDNS server. Please consider to choose another {nameof(label)}.");
                    }
                }
                return false;
            },
            clientName: clientNameStr);

            if (dbObj == null)
            {
                // 物理データベース上にもレコードが存在しない。新規作成する必要があるようだ。
                anyChangePossibility = true;
                needToCreate = true;
            }
            else
            {
                // 物理データベース上のレコードの取得に成功した。同様に変更の可能性があるかどうか確認する。
                var current = dbObj.Data;

                if (label._IsFilled() && current.HostLabel._IsSamei(label) == false) anyChangePossibility = true;
                if (ipv4str._IsFilled() || ipv6str._IsFilled())
                {
                    if (current.HostAddress_IPv4._IsSamei(ipv4str) == false) anyChangePossibility = true;
                    if (current.HostAddress_IPv6._IsSamei(ipv6str) == false) anyChangePossibility = true;
                }
                if (userGroupSecretKey._IsSamei("delete"))
                {
                    if (current.UserGroupSecretKey._IsFilled()) anyChangePossibility = true;
                }
                else if (userGroupSecretKey._IsFilled())
                {
                    if (current.UserGroupSecretKey._IsSamei(userGroupSecretKey) == false) anyChangePossibility = true;
                }
                if (email._IsSamei("delete"))
                {
                    if (current.Email._IsFilled()) anyChangePossibility = true;
                }
                else if (email._IsFilled())
                {
                    if (current.Email._IsSame(email) == false) anyChangePossibility = true;
                }

                if (userData != null)
                {
                    if (current.UserData._ObjectToJson(compact: true)._IsSame(userData._ObjectToJson(compact: true)) == false) anyChangePossibility = true;
                }

                if (anyChangePossibility)
                {
                    retCurrentHostObj = dbObj;
                }
            }
        }

        if (needToCreate)
        {
            // 新規作成のようだぞ
            if (requireUnlockKey && unlockKey._IsEmpty())
            {
                // 登録キーを要求している際に登録キーが指定されていない場合エラーにする
                throw new CoresException($"The parameter {nameof(unlockKey)} is missing. This DDNS server requires the parameter {nameof(unlockKey)} when creating a new DDNS host object.");
            }

            // 固有使用許諾文字列のチェック
            if (config.DDns_RequiredLicenseString._IsFilled())
            {
                if (licenseString._IsEmpty())
                {
                    throw new CoresException($"The parameter {nameof(licenseString)} is not specified. This DDNS server requires the {nameof(licenseString)} parameter. Please add this parameter.");
                }

                if (licenseString._IsDiff(config.DDns_RequiredLicenseString))
                {
                    throw new CoresException($"The sepcified value of {nameof(licenseString)} is invalid. Please check the string and try again.");
                }
            }

            // クォータをチェック。なお、オンメモリ上の DB 上でチェックはするものの、DB の物理的な検索は実施しない。
            // したがって、API サーバーが複数ある場合で、各 API サーバーで一時期に大量にホストを作成した場合は、
            // 若干超過して作成に成功する場合もあるのである。
            // なお、明示的な ACL で除外されている場合は、このチェックを実施しない。
            if (config.DDns_DisableMaxHostPerCreateClientIpQuota == false &&
                EasyIpAcl.Evaluate(config.Service_HeavyRequestRateLimiterExemptAcl, clientIp.ToString(), EasyIpAclAction.Deny, EasyIpAclAction.Deny, enableCache: true) == EasyIpAclAction.Deny)
            {
                var existingHostsSameClientIpAddr = Hadb.FastSearchByLabels(new Host { CreateRequestedIpAddress = clientIp.ToString() });
                if (existingHostsSameClientIpAddr.Count() >= config.DDns_MaxHostPerCreateClientIpAddress_Total)
                {
                    throw new CoresException($"The host record registration quota (total) exceeded (Limitation type #1). Your IP address {clientIp} cannot create more host records on this DDNS server. Complaint should be submitted to the DDNS server administrator.");
                }
                if (existingHostsSameClientIpAddr.Where(x => x.CreateDt.Date == now.Date).Count() >= config.DDns_MaxHostPerCreateClientIpAddress_Daily)
                {
                    throw new CoresException($"The host record registration quota (daily) exceeded (Limitation type #2). Your IP address {clientIp} cannot create more host records on this DDNS server today. Please wait for one or more days and try again. Complaint should be submitted to the DDNS server administrator.");
                }

                var existingHostsSameClientIpNetwork = Hadb.FastSearchByLabels(new Host { CreateRequestedIpNetwork = clientNetwork.ToString() });
                if (existingHostsSameClientIpNetwork.Count() >= config.DDns_MaxHostPerCreateClientIpNetwork_Total)
                {
                    throw new CoresException($"The host record registration quota (total) exceeded (Limitation type #3). Your IP address {clientIp} cannot create more host records on this DDNS server. Complaint should be submitted to the DDNS server administrator.");
                }
                if (existingHostsSameClientIpNetwork.Where(x => x.CreateDt.Date == now.Date).Count() >= config.DDns_MaxHostPerCreateClientIpNetwork_Daily)
                {
                    throw new CoresException($"The host record registration quota (daily) exceeded (Limitation type #4). Your IP address {clientIp} cannot create more host records on this DDNS server today. Please wait for one or more days and try again. Complaint should be submitted to the DDNS server administrator.");
                }
            }

            if (requireUnlockKey)
            {
                await Hadb.TranAsync(false, async tran =>
                {
                    // 登録キーが必要な場合は、同一の登録キーを用いたホストがすでに登録されていないかどうか調べる。
                    // なお、ここではまず読み取り専用モードで DB を検索する。
                    // その後、DB への書き込みの際に一意キー制約で重複は再度厳重に検出されるのである。
                    var exist = await tran.AtomicSearchByKeyAsync(new Host { UsedUnlockKey = unlockKey });

                    if (exist != null)
                    {
                        // すでに使用されている!
                        throw new CoresException($"The specified {nameof(unlockKey)} ('{unlockKey}') is already used. It is unable to create two or more DDNS hosts with a single {nameof(unlockKey)}. Please contact the DDNS service administrator to request another {nameof(unlockKey)}.");
                    }

                    // そもそも指定された登録キーが存在するか調べる
                    var exist2 = await tran.AtomicSearchByKeyAsync(new UnlockKey { Key = unlockKey });

                    if (exist2 == null)
                    {
                        // 存在しない!
                        throw new CoresException($"The specified {nameof(unlockKey)} ('{unlockKey}') is wrong. Please contact the DDNS service administrator to request a valid {nameof(unlockKey)}.");
                    }

                    return false;
                },
                clientName: clientNameStr);
            }

            // 新規作成の必要がある場合は、データベース上で新規作成をする。
            // なお、前回の DB 読み取りの際にはホストシークレットキーを持つレコードが存在せず、この DB 書き込みの際には存在する可能性
            // もタイミングによっては生じるが、この場合は、DB の一意キー制約によりエラーになるので、二重にホストシークレットキーを有する
            // レコードが存在することとなるおそれはない。

            string clientFqdn = await this.GetClientFqdnAsync();

            if (userData == null) userData = Json.NewJsonObject();

            Host newHost = null!;

            if (ipv4str._IsEmpty() && ipv6str._IsEmpty())
            {
                // IP アドレスが IPv4, IPv6 の両方とも指定されていない場合は、クライアントの IP アドレスが改めて指定されたものとみなす。
                ip = "myip";
                InitIpAddressVariable();
            }

            this.Basic_Check_HeavyRequestRateLimiter();

            await Hadb.TranAsync(true, async tran =>
            {
                // クォータ制限をいたします
                await this.Basic_CheckAndAddLogBasedQuotaByClientIpAndSubnetEasyAsync("HostAdd", reentrantTran: tran);

                newHost = new Host
                {
                    HostLabel = label,
                    HostSecretKey = secretKey,
                    AuthLogin_LastIpAddress = clientIp.ToString(),
                    AuthLogin_LastFqdn = clientFqdn,
                    AuthLogin_Count = 1,
                    AuthLogin_FirstTime = now,
                    AuthLogin_LastTime = now,
                    CreatedTime = now,
                    CreateRequestedIpAddress = clientIp.ToString(),
                    CreateRequestedFqdn = clientFqdn,
                    CreateRequestedIpNetwork = clientNetwork.ToString(),
                    HostLabel_NumUpdates = 1,
                    HostLabel_LastUpdateTime = now,
                    HostAddress_IPv4 = ipv4str,
                    HostAddress_IPv4_FirstUpdateTime = ipv4str._IsFilled() ? now : DtOffsetZero,
                    HostAddress_IPv4_LastUpdateTime = ipv4str._IsFilled() ? now : DtOffsetZero,
                    HostAddress_IPv4_NumUpdates = ipv4str._IsFilled() ? 1 : 0,
                    HostAddress_IPv6 = ipv6str,
                    HostAddress_IPv6_FirstUpdateTime = ipv6str._IsFilled() ? now : DtOffsetZero,
                    HostAddress_IPv6_LastUpdateTime = ipv6str._IsFilled() ? now : DtOffsetZero,
                    HostAddress_IPv6_NumUpdates = ipv6str._IsFilled() ? 1 : 0,
                    UsedUnlockKey = unlockKey,
                    UserGroupSecretKey = userGroupSecretKey,
                    Email = email,
                    UserData = userData,
                };

                await tran.AtomicAddAsync(newHost);

                return true;
            },
            clientName: clientNameStr);

            // 作成に成功したので、ホストオブジェクトを返却する。
            return new Host_Return(newHost, HostApiResult.Created, GetDomainNamesList().Select(x => newHost.HostLabel + "." + x).ToArray(), easyGetInfoUrl, easyUpdateUrl);
        }

        if (anyChangePossibility)
        {
            // データベース上で変更をする。
            Host current = null!;

            this.Basic_Check_HeavyRequestRateLimiter();

            await Hadb.TranAsync(true, async tran =>
            {
                // まず最新の DB 上のデータを取得する。これは上の事前の確認時から変更されている可能性もある。
                var currentObj = await tran.AtomicSearchByKeyAsync(new Host { HostSecretKey = secretKey });

                if (currentObj == null)
                {
                    // ここで hostSecretKey が見つからないケースは稀であるが、分散データベースのタイミング上あり得る。
                    // この場合は、再試行するようエラーを出す。
                    throw new CoresException($"{nameof(secretKey)} is not found on the database. Please try again later.");
                }

                current = currentObj.Data;

                if (label._IsFilled() && current.HostLabel._IsSamei(label) == false)
                {
                    current.HostLabel = label;
                    current.HostLabel_LastUpdateTime = now;
                    current.HostLabel_NumUpdates++;
                }

                if (ipv4str._IsFilled() || ipv6str._IsFilled())
                {
                    if (current.HostAddress_IPv4._IsSamei(ipv4str) == false)
                    {
                        current.HostAddress_IPv4 = ipv4str;
                        current.HostAddress_IPv4_LastUpdateTime = now;
                        if (current.HostAddress_IPv4_FirstUpdateTime._IsZeroDateTime())
                        {
                            current.HostAddress_IPv4_FirstUpdateTime = now;
                        }
                        current.HostAddress_IPv4_NumUpdates++;
                    }

                    if (current.HostAddress_IPv6._IsSamei(ipv6str) == false)
                    {
                        current.HostAddress_IPv6 = ipv6str;
                        current.HostAddress_IPv6_LastUpdateTime = now;
                        if (current.HostAddress_IPv6_FirstUpdateTime._IsZeroDateTime())
                        {
                            current.HostAddress_IPv6_FirstUpdateTime = now;
                        }
                        current.HostAddress_IPv6_NumUpdates++;
                    }
                }

                if (userGroupSecretKey._IsSamei("delete"))
                {
                    current.UserGroupSecretKey = "";
                }
                else if (userGroupSecretKey._IsFilled())
                {
                    current.UserGroupSecretKey = userGroupSecretKey;
                }

                if (email._IsSamei("delete"))
                {
                    current.Email = "";
                }
                else if (email._IsFilled())
                {
                    current.Email = email;
                }

                if (userData != null)
                {
                    current.UserData = userData;
                }

                current.AuthLogin_LastIpAddress = clientIp.ToString();
                current.AuthLogin_LastTime = now;
                current.AuthLogin_Count++;

                if (newApiRateLimitStartDt._IsZeroDateTime() == false)
                {
                    current.ApiRateLimit_StartTime = newApiRateLimitStartDt;
                    current.ApiRateLimit_CurrentCount = 1;
                }
                else
                {
                    current.ApiRateLimit_CurrentCount++;
                }

                // DB 上のデータを書き込みする。
                await tran.AtomicUpdateAsync(currentObj);

                return true;
            },
            clientName: clientNameStr);

            // 変更に成功したので、ホストオブジェクトを返却する。
            return new Host_Return(current, HostApiResult.Modified, GetDomainNamesList().Select(x => current.HostLabel + "." + x).ToArray(), easyGetInfoUrl, easyUpdateUrl);
        }

        retCurrentHostObj._NullCheck(nameof(retCurrentHostObj));

        var retCurrentHost = retCurrentHostObj.Data;
        string clientFqdn2 = "";

        if (retCurrentHost.AuthLogin_LastIpAddress != clientIp.ToString())
        {
            // Last FQDN は、Last IP Address が変更になった場合のみ DNS を用いて取得する。
            // IP アドレスが前回から変更になっていない場合は、更新しない。
            clientFqdn2 = await this.GetClientFqdnAsync();
        }

        if (memoryObj != null)
        {
            // 特に変更がないので、ステータスを Lazy 更新する。(Memory Object がメモリ上に存在する場合のみ)
            var r2 = retCurrentHostObj.FastUpdate(h =>
            {
                h.AuthLogin_Count++;
                h.AuthLogin_LastIpAddress = clientIp.ToString();
                if (clientFqdn2._IsFilled())
                {
                    h.AuthLogin_LastFqdn = clientFqdn2;
                }
                h.AuthLogin_LastTime = now;
                return true;
            },
            true,
            new HadbFastUpdateOptions { QuotaDurationMsecs = config.DDns_HostFastUpdateQuota_DurationMsecs, QuotaMaxFastUpdateCountPerDuration = config.DDns_HostFastUpdateQuota_MaxFastUpdateCountPerDuration });

            if (r2 != null)
            {
                retCurrentHost = r2;
            }
        }
        // 現在のオブジェクト情報を返却する。
        retCurrentHost._NullCheck(nameof(retCurrentHost));
        return new Host_Return(retCurrentHost, HostApiResult.NoChange, GetDomainNamesList().Select(x => retCurrentHost.HostLabel + "." + x).ToArray(), easyGetInfoUrl, easyUpdateUrl);
    }

    public async Task DDNS_HostDelete(string secretKey)
    {
        IPAddress clientIp = this.GetClientIpAddress();
        IPAddress clientNetwork = this.GetClientIpNetworkForRateLimit();
        string clientNameStr = clientIp.ToString();

        var now = DtOffsetNow;

        // パラメータの検査と正規化
        secretKey = secretKey._NonNullTrim().ToUpperInvariant();

        if (secretKey._IsFilled())
        {
            secretKey._CheckUseOnlyChars($"Specified {nameof(secretKey)} contains invalid character.", "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ-_");
            secretKey._CheckStrLenException(40, $"Specified {nameof(secretKey)} is too long.");
        }
        else
        {
            throw new CoresException($"{nameof(secretKey)} is not specified.");
        }

        string uid = "";

        // まずホストをデータベースから検索する
        await Hadb.TranAsync(false, async tran =>
        {
            var existObj = await tran.AtomicSearchByKeyAsync<Host>(new Host { HostSecretKey = secretKey });
            if (existObj != null)
            {
                uid = existObj.Uid;
            }

            return false;
        }, clientName: clientNameStr);

        if (uid._IsEmpty())
        {
            // 存在しないか、すでに削除されている
            throw new CoresException($"The host object with specified {nameof(secretKey)} is not found.");
        }

        await Hadb.TranAsync(true, async tran =>
        {
            // 削除を実行する
            await tran.AtomicDeleteByKeyAsync(new Host { HostSecretKey = secretKey });

            return true;
        });
    }

    public async Task<string> DDNS_HostRecoveryByEmail(string email)
    {
        IPAddress clientIp = this.GetClientIpAddress();
        IPAddress clientNetwork = this.GetClientIpNetworkForRateLimit();
        string clientNameStr = clientIp.ToString();

        email = email._NonNullTrim();
        if (Str.CheckMailAddress(email) == false)
        {
            throw new CoresException($"{nameof(email)} has invalid email address format.");
        }

        this.Basic_Check_HeavyRequestRateLimiter(5.0);

        List<Host> list = null!;

        await Hadb.TranAsync(false, async tran =>
        {
            var objList = await tran.AtomicSearchByLabelsAsync(new Host { Email = email });

            list = objList.Select(x => x.Data).OrderByDescending(x => x.DnsQuery_LastAccessTime).ThenByDescending(x => x.CreatedTime).Take(CurrentDynamicConfig.DDns_Enum_By_Email_MaxCount).ToList();

            return false;
        },
        clientName: clientNameStr);

        if (list.Any() == false)
        {
            throw new CoresException($"Specified email address '{email}' has no registered DNS records on this DDNS server.");
        }

        await this.Basic_CheckAndAddLogBasedQuotaByClientIpAndSubnetEasyAsync("HostRecovery_by_ip");
        await this.Basic_CheckAndAddLogBasedQuotaAsync("HostRecovery_by_email", email);

        await this.Hook.DDNS_SendRecoveryMailAsync(this, list, email, this.GetClientIpStr(), await this.GetClientFqdnAsync());

        return $"Email sent to your mail address '{email}'. Please check your inbox.";
    }

    public async Task<Host[]> DDNS_HostEnumByUserGroupSecretKey(string userGroupSecretKey)
    {
        IPAddress clientIp = this.GetClientIpAddress();
        IPAddress clientNetwork = this.GetClientIpNetworkForRateLimit();
        string clientNameStr = clientIp.ToString();

        userGroupSecretKey = userGroupSecretKey._NormalizeKey(true);

        if (userGroupSecretKey._IsFilled())
        {
            userGroupSecretKey._CheckUseOnlyChars($"Specified {nameof(userGroupSecretKey)} contains invalid character.", "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ-_");
            userGroupSecretKey._CheckStrLenException(40, $"Specified {nameof(userGroupSecretKey)} is too long.");
        }
        else
        {
            throw new CoresException($"The parameter {nameof(userGroupSecretKey)} is missing.");
        }

        this.Basic_Check_HeavyRequestRateLimiter(5.0);

        List<Host> list = null!;

        await Hadb.TranAsync(false, async tran =>
        {
            var objList = await tran.AtomicSearchByLabelsAsync(new Host { UserGroupSecretKey = userGroupSecretKey });

            list = objList.Select(x => x.Data).OrderByDescending(x => x.DnsQuery_LastAccessTime).ThenByDescending(x => x.CreatedTime).Take(CurrentDynamicConfig.DDns_Enum_By_UserGroupSecretKey_MaxCount).ToList();

            return false;
        },
        clientName: clientNameStr);

        return list.ToArray();
    }

    public async Task<HostHistoryRecord[]> DDNS_HostGetHistory(string secretKey)
    {
        IPAddress clientIp = this.GetClientIpAddress();
        IPAddress clientNetwork = this.GetClientIpNetworkForRateLimit();
        string clientNameStr = clientIp.ToString();

        // パラメータの検査と正規化
        secretKey = secretKey._NonNullTrim().ToUpperInvariant();

        if (secretKey._IsFilled())
        {
            secretKey._CheckUseOnlyChars($"Specified {nameof(secretKey)} contains invalid character.", "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ-_");
            secretKey._CheckStrLenException(40, $"Specified {nameof(secretKey)} is too long.");
        }
        else
        {
            throw new CoresException($"{nameof(secretKey)} is not specified.");
        }

        List<HostHistoryRecord> ret = new List<HostHistoryRecord>();

        await Hadb.TranAsync(false, async tran =>
        {
            var obj = await tran.AtomicSearchByKeyAsync(new Host { HostSecretKey = secretKey });
            if (obj == null)
            {
                // 存在しないか、すでに削除されている
                throw new CoresException($"The host object with specified {nameof(secretKey)} is not found.");
            }

            string uid = obj.Uid;

            var archives = await tran.AtomicGetArchivedAsync<Host>(uid, DevCoresConfig.MikakaDDnsServiceSettings.HostRecordMaxArchivedCount);

            foreach (var a in archives)
            {
                ret.Add(new HostHistoryRecord
                {
                    HostData = a.Data,
                    TimeStamp = a.UpdateDt,
                    Version = a.Ver,
                });
            }

            return false;
        },
        clientName: clientNameStr);

        ret._DoSortBy(x => x.OrderByDescending(a => a.Version));

        return ret.ToArray();
    }

    public async Task<UnlockKey[]> DDNSAdmin_UnlockKeyCreate(int count)
    {
        await this.Basic_Require_AdminBasicAuthAsync();

        if (this.CurrentDynamicConfig.DDns_RequireUnlockKey == false)
        {
            throw new CoresException("The unlock key feature is not enabled in this DDNS system. You need not to create unlock keys.");
        }

        IPAddress clientIp = this.GetClientIpAddress();
        IPAddress clientNetwork = this.GetClientIpNetworkForRateLimit();
        string clientFqdn = await this.GetClientFqdnAsync();

        if (count <= 0) count = 1;

        if (count > Consts.Numbers.MikakaDDns_MaxUnlockKeyCountOnce)
        {
            throw new CoresException($"Specified {nameof(count)} value ({count}) is too much. {nameof(count)} must be equal or less than {Consts.Numbers.MikakaDDns_MaxUnlockKeyCountOnce}.");
        }

        var now = DtOffsetNow;

        List<UnlockKey> ret = new List<UnlockKey>();

        await Hadb.TranAsync(true, async tran =>
        {
            for (int i = 0; i < count; i++)
            {
                UnlockKey k = new UnlockKey
                {
                    CreatedTime = now,
                    CreateRequestedFqdn = clientFqdn,
                    CreateRequestedIpAddress = clientIp.ToString(),
                    Key = Str.GenerateRandomDigit(36),
                };

                k.Normalize();

                await tran.AtomicAddAsync(k);

                var k2 = k._CloneDeep();

                k2.Key = k2.Key._Slice(0, 6) + "-" + k2.Key._Slice(6, 6) + "-" + k2.Key._Slice(12, 6) + "-" + k2.Key._Slice(18, 6) + "-" + k2.Key._Slice(24, 6) + "-" + k2.Key._Slice(30, 6);

                ret.Add(k2);
            }

            return true;
        });

        return ret.ToArray();
    }

    //public async Task<Test2Output> Test2(Test2Input in1, string in2, HostApiResult in3)
    //{
    //    await Task.CompletedTask;

    //    return new Test2Output { IntParam = in1.IntParam * 2, StrParam = in1.StrParam + "_test_" + in2 + "_" + in3.ToString()};
    //}
}







#endif

