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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Sockets;
using IPA.Cores.Basic.DnsLib;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
//using Microsoft.EntityFrameworkCore.Query.Internal;

namespace IPA.Cores.Basic;

public static partial class DevCoresConfig
{
    public static partial class IpaDnsServiceSettings
    {
        public static readonly Copenhagen<string> Default_Ns1 = "ns01.example.org";
        public static readonly Copenhagen<string> Default_Ns2 = "ns02.example.org";
        public static readonly Copenhagen<string> Default_Responsible = "nobody.example.org";
        public static readonly Copenhagen<int> Default_NegativeCacheTtl = 13;
        public static readonly Copenhagen<int> Default_RefreshInterval = 60;
        public static readonly Copenhagen<int> Default_RetryInterval = 60;
        public static readonly Copenhagen<int> Default_ExpireInterval = 88473600;
        public static readonly Copenhagen<int> Default_DefaultTtl = 60;
    }
}

public static class IpaDnsServiceGlobal
{
    // デフォルトの static 証明書
    // SHA1: 80E38CE13AE4FCB033439A6C17B0145D77AD4960
    // SHA256: 35188460BC0767A6B738D118442EFEE5711CC5A8CB62387249E12D459D8E65A4
    static readonly Singleton<PalX509Certificate> IpaDnsServerSampleStaticCert_Singleton = new Singleton<PalX509Certificate>(() => new PalX509Certificate(new FilePath(Res.Cores, "SampleDefaultCert/221125MikakaDDnsServerSampleStaticCert-20221125.pfx")));
    public static PalX509Certificate IpaDnsServerSampleStaticCert => IpaDnsServerSampleStaticCert_Singleton;

    public static void Init()
    {
        GlobalCertVault.SetDefaultCertificateGenerator(_ => IpaDnsServiceGlobal.IpaDnsServerSampleStaticCert);

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

public class IpaDnsServiceStartupParam : HadbBasedServiceStartupParam
{
    public IpaDnsServiceStartupParam(string hiveDataName = "IpaDnsService", string hadbSystemName = "IPA_DNS")
    {
        this.HiveDataName = hiveDataName;
        this.HadbSystemName = hadbSystemName;
    }
}

public class IpaDnsServiceDynamicResponderInitParam
{
    public IpaDnsService Service { get; }
    public EasyDnsResponderZone Zone { get; }
    public EasyDnsResponderRecord Record { get; }
    public IpaDnsService.CustomRecord CustomRecordDef { get; }
    public string Id { get; }

    public QueryStringList DynamicRecordDefParams => this.CustomRecordDef.DynamicRecordParamsList!;
    public EasyDnsResponderRecordType RecordType => this.Record.Type;

    public IpaDnsServiceDynamicResponderInitParam(IpaDnsService service, EasyDnsResponderZone zone, EasyDnsResponderRecord record, IpaDnsService.CustomRecord customRecordDef, string id)
    {
        Service = service;
        Zone = zone;
        Record = record;
        CustomRecordDef = customRecordDef;
        Id = id;
    }
}

public delegate IpaDnsServiceDynamicResponderBase IpaDnsServiceDynamicResponderFactory(IpaDnsServiceDynamicResponderInitParam initParam);

public class IpaDnsServiceDynamicResponderCallParam
{
    public EasyDnsResponderDynamicRecordCallbackResult EditMe { get; }
    public EasyDnsResponderDynamicRecordCallbackRequest Request { get; }

    public IpaDnsServiceDynamicResponderCallParam(EasyDnsResponderDynamicRecordCallbackResult editMe, EasyDnsResponderDynamicRecordCallbackRequest request)
    {
        EditMe = editMe;
        Request = request;
    }
}

public abstract class IpaDnsServiceDynamicResponderBase
{
    public IpaDnsServiceDynamicResponderInitParam Param { get; }

    public IpaDnsServiceDynamicResponderBase(IpaDnsServiceDynamicResponderInitParam initParam)
    {
        this.Param = initParam;
    }

    public abstract bool ResolveImpl(IpaDnsServiceDynamicResponderCallParam param);
}

public class IpaDnsServiceHook : HadbBasedServiceHookBase
{
    public virtual void InitDynamicResponderFactoryList(Dictionary<string, IpaDnsServiceDynamicResponderFactory> list) { }
}

public class IpaDnsService : HadbBasedSimpleServiceBase<IpaDnsService.MemDb, IpaDnsService.DynConfig, IpaDnsService.HiveSettings, IpaDnsServiceHook>, IpaDnsService.IRpc
{
    [Flags]
    public enum ZoneDefType
    {
        Forward = 0,
        Reverse,
    }

    public class Vars
    {
        public StrDictionary<List<string>> VarsList = new StrDictionary<List<string>>(StrCmpi);

        public void Set(string name, string value)
        {
            name = name._NonNullTrim();
            value = value._NonNullTrim();

            if (name._IsEmpty()) return;

            if (this.VarsList.ContainsKey(name) == false)
            {
                this.VarsList.Add(name, new List<string>());
            }

            this.VarsList[name].Add(value);
        }

        public void Unset(string name)
        {
            this.VarsList.Remove(name);
        }
    }

    public class ZoneDefOptions : INormalizable
    {
        public List<string> NameServersFqdnList = new List<string>();
        public string Responsible = "";
        public int NegativeCacheTtl;
        public int RefreshInterval;
        public int RetryInterval;
        public int ExpireInterval;
        public int DefaultTtl;
        public string TcpAxfrAllowedAcl = "";
        public string NotifyServers = "";
        public Dictionary<string, string> VarsList = new Dictionary<string, string>(StrCmpi);

        public ZoneDefOptions() { }

        public ZoneDefOptions(Vars vars)
        {
            var o = vars.VarsList;

            string[] nsList = (o._GetOrDefault("Ns")?.FirstOrDefault())._NonNull()._Split(StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.None, " ", "\t", "　", ",", ";");

            nsList._DoForEach(x => this.NameServersFqdnList.Add(x));

            this.Responsible = (o._GetOrDefault("Responsible")?.FirstOrDefault())._NonNull();
            this.NegativeCacheTtl = (o._GetOrDefault("NegativeCacheTtl")?.FirstOrDefault())._NonNull()._ToInt();
            this.RefreshInterval = (o._GetOrDefault("RefreshInterval")?.FirstOrDefault())._NonNull()._ToInt();
            this.RetryInterval = (o._GetOrDefault("RetryInterval")?.FirstOrDefault())._NonNull()._ToInt();
            this.ExpireInterval = (o._GetOrDefault("ExpireInterval")?.FirstOrDefault())._NonNull()._ToInt();
            this.DefaultTtl = (o._GetOrDefault("DefaultTtl")?.FirstOrDefault())._NonNull()._ToInt();
            this.TcpAxfrAllowedAcl = (o._GetOrDefault("TcpAxfrAllowedAcl")?.FirstOrDefault())._NonNull();
            this.NotifyServers = (o._GetOrDefault("NotifyServers")?.FirstOrDefault())._NonNull();

            List<string>? variablesSrcStrList = o._GetOrDefault("Vars");

            if (variablesSrcStrList != null)
            {
                foreach (string variablesSrcStr in variablesSrcStrList)
                {
                    if (variablesSrcStr._IsFilled())
                    {
                        if (variablesSrcStr._GetKeyAndValue(out string key, out string value))
                        {
                            this.VarsList[key] = value;
                        }
                    }
                }
            }

            this.Normalize();
        }

        public void Normalize()
        {
            if (this.NameServersFqdnList == null)
            {
                this.NameServersFqdnList = new List<string>();
            }

            HashSet<string> o = new HashSet<string>(StrCmpi);
            foreach (var ns in this.NameServersFqdnList)
            {
                string tmp = ns._NormalizeFqdn();
                if (tmp._IsFilled())
                {
                    o.Add(tmp);
                }
            }
            this.NameServersFqdnList = o.Distinct(StrCmpi).ToList();

            if (this.NameServersFqdnList.Any() == false)
            {
                this.NameServersFqdnList.Add(DevCoresConfig.IpaDnsServiceSettings.Default_Ns1);
                this.NameServersFqdnList.Add(DevCoresConfig.IpaDnsServiceSettings.Default_Ns2);
            }

            if (this.Responsible._IsEmpty()) this.Responsible = DevCoresConfig.IpaDnsServiceSettings.Default_Responsible;
            if (this.NegativeCacheTtl <= 0) this.NegativeCacheTtl = DevCoresConfig.IpaDnsServiceSettings.Default_NegativeCacheTtl;
            if (this.RefreshInterval <= 0) this.RefreshInterval = DevCoresConfig.IpaDnsServiceSettings.Default_RefreshInterval;
            if (this.RetryInterval <= 0) this.RetryInterval = DevCoresConfig.IpaDnsServiceSettings.Default_RetryInterval;
            if (this.ExpireInterval <= 0) this.ExpireInterval = DevCoresConfig.IpaDnsServiceSettings.Default_ExpireInterval;
            if (this.DefaultTtl <= 0) this.DefaultTtl = DevCoresConfig.IpaDnsServiceSettings.Default_DefaultTtl;

            TcpAxfrAllowedAcl = TcpAxfrAllowedAcl._NonNullTrim();
            NotifyServers = NotifyServers._NonNullTrim();
        }
    }

    public class CustomRecord
    {
        public string Label = "";
        public string Data = "";
        public bool IsDynamicRecord;
        public EasyDnsResponderRecordType Type = EasyDnsResponderRecordType.None;
        public EasyDnsResponderRecordSettings? Settings;
        public QueryStringList? DynamicRecordParamsList;
    }

    [Flags]
    public enum StandardRecordType
    {
        ForwardSingle = 0,      // aaa.example.org -> 1.2.3.4
        ForwardSubnet,          // 1-*-*-*.aaa.example.org (データ上は *.aaa.example.org) -> 1.0.0.0/24 とかの特定のサブネット
        ReverseSingle,          // 1.2.3.4 -> aaa.example.org
        ReverseSubnet,          // 1.0.0.0/24 -> 1-*-*-*.aaa.example.org
        ReverseCNameSingle,     // 1.2.3.4 -> CNAME 4.3.2.1.aaa.example.org
        ReverseCNameSubnet,     // 1.0.0.0/24 -> CNAME 4.3.2.1.aaa.example.org
    }

    public class StandardRecord
    {
        public StandardRecordType Type;
        public bool ReverseCName_HyphenMode;
        public string Fqdn = "";
        public IPAddress IpNetwork = IPAddress.Any;
        public IPAddress IpSubnetMask = IPAddress.Any;
        public int SubnetLength;
        public string FirstTokenWildcardBefore = "";
        public string FirstTokenWildcardAfter = "";
        public EasyDnsResponderRecordSettings? Settings;
    }

    public class ForwarderDef
    {
        public string Selector = "";
        public string ForwarderList = "";
        public QueryStringList ArgsList = new QueryStringList();
        public int TimeoutMsecs = CoresConfig.EasyDnsResponderSettings.Default_ForwarderTimeoutMsecs;

        public ForwarderDef() { }

        public ForwarderDef(string str, QueryStringList argsList)
        {
            var tokenList = str._Split(StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries, " ", "\t", "　", ",", ";");

            this.Selector = tokenList.ElementAtOrDefault(0)._NonNullTrim();

            if (this.Selector._IsEmpty())
            {
                throw new CoresException("Invalid forwarder definition");
            }

            this.ForwarderList = tokenList.Skip(1)._Combine(" ");
            this.ArgsList = argsList;

            this.TimeoutMsecs = this.ArgsList._GetIntFirst("timeout", CoresConfig.EasyDnsResponderSettings.Default_ForwarderTimeoutMsecs);
        }
    }

    public class ZoneDef
    {
        public ZoneDefType Type;
        public ZoneDefOptions Options = new ZoneDefOptions();
        public string Fqdn = "";
        public IPAddress ReverseIpNetwork = IPAddress.Any;
        public IPAddress ReverseIpSubnetMask = IPAddress.Any;
        public int ReverseSubnetLength;

        public List<CustomRecord> CustomRecordList = new List<CustomRecord>();
        public List<StandardRecord> StandardForwardRecordList = new List<StandardRecord>();

        public ZoneDef() { }

        public ZoneDef(string str, Vars vars, bool reverseNoConvertToPtrFqdn = false)
        {
            Fqdn = str._NormalizeFqdn();

            if (Fqdn._InStri("/"))
            {
                // 逆引きゾーン: サブネット表記
                IPUtil.ParseIPAndSubnetMask(Fqdn, out IPAddress net, out IPAddress subnet);
                int subnetLen = IPUtil.SubnetMaskToInt(subnet);
                this.Type = ZoneDefType.Reverse;

                this.ReverseIpNetwork = net;
                this.ReverseIpSubnetMask = subnet;
                this.ReverseSubnetLength = subnetLen;

                if (reverseNoConvertToPtrFqdn == false)
                {
                    this.Fqdn = IPUtil.IPAddressOrSubnetToPtrZoneOrFqdn(net, subnetLen);
                }
            }
            else if (Fqdn.EndsWith("in-addr.arpa", StrCmpi) || Fqdn.EndsWith("ip6.arpa", StrCmpi))
            {
                if (Str.CheckFqdn(Fqdn) == false)
                {
                    throw new CoresException($"Zone string '{Fqdn}' is invalid");
                }

                // 逆引きゾーン: in-addr.arpa または ip6.arpa 表記
                var ipAndSubnet = IPUtil.PtrZoneOrFqdnToIpAddressAndSubnet(Fqdn);

                this.Type = ZoneDefType.Reverse;
                this.ReverseIpNetwork = ipAndSubnet.Item1;
                this.ReverseSubnetLength = ipAndSubnet.Item2;
                this.ReverseIpSubnetMask = IPUtil.IntToSubnetMask(ipAndSubnet.Item1.AddressFamily, ipAndSubnet.Item2);
            }
            else
            {
                if (Str.CheckFqdn(Fqdn) == false)
                {
                    throw new CoresException($"Zone string '{Fqdn}' is invalid");
                }

                // 正引きゾーン
                this.Type = ZoneDefType.Forward;
            }

            this.Options = new ZoneDefOptions(vars);
        }
    }

    public class Config
    {
        public StrDictionary<ZoneDef> ZoneList = new StrDictionary<ZoneDef>();
        public List<ForwarderDef> ForwarderList = new List<ForwarderDef>();
        public FullRoute46<StandardRecord> ReverseRadixTrie = new FullRoute46<StandardRecord>();
        public List<StandardRecord> ReverseRecordsList = new List<StandardRecord>();
        public Dictionary<string, IpaDnsServiceDynamicResponderFactory> DynamicResponderFactoryDict = new Dictionary<string, IpaDnsServiceDynamicResponderFactory>(StrCmpi);
        public Dictionary<string, IpaDnsServiceDynamicResponderBase> DynamicResponderDict = new Dictionary<string, IpaDnsServiceDynamicResponderBase>();

        public Config() { }

        public Config(string body, StringWriter err)
        {
            var lines = body._GetLines(false, false, null, false, false);

            Vars vars = new Vars();

            List<ZoneDef>? currentLimitZonesList = null;

            for (int j = 0; j < 2; j++) // 走査は、2 回行なう。1 回目は、変数とゾーン名定義の読み込みである。2 回目は、ゾーンのレコード情報の読み込みである。
            {
                for (int i = 0; i < lines.Length; i++)
                {
                    string lineSrc = lines[i];

                    try
                    {
                        // コメント除去
                        string line = lineSrc.Trim()._StripCommentFromLine(new[] { "#" }).Trim();

                        if (line._IsFilled() && line._GetKeyAndValue(out string key, out string value, " \t"))
                        {
                            key = key.Trim();
                            value = value.Trim();

                            if (key._IsSamei("!Unlimit"))
                            {
                                // 制限の解除
                                currentLimitZonesList = null;
                            }
                            else if (key._IsFilled() && value._IsFilled())
                            {
                                if (j == 0)
                                {
                                    // 1 回目の走査
                                    if (key.StartsWith("!"))
                                    {
                                        if (key._IsSamei("!Set"))
                                        {
                                            // 変数の設定
                                            if (value._GetKeyAndValue(out string varKey, out string varValue, " \t"))
                                            {
                                                vars.Set(varKey, varValue);
                                            }
                                        }
                                        else if (key._IsSamei("!Unset"))
                                        {
                                            // 変数の削除
                                            if (value._GetKeyAndValue(out string varKey, out string varValue, " \t"))
                                            {
                                                vars.Unset(varKey);
                                            }
                                        }
                                        else if (key._IsSamei("!DefineZone"))
                                        {
                                            // ゾーンの定義
                                            var zonesTokenList = value._Split(StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries, " ", "\t", "　", ",", ";");
                                            foreach (var zoneToken in zonesTokenList)
                                            {
                                                var zone = new ZoneDef(zoneToken, vars);
                                                this.ZoneList.Add(zone.Fqdn, zone);
                                            }
                                        }
                                        else if (key._IsSamei("!DefineForwarder"))
                                        {
                                            // フォワーダの定義
                                            string str1 = key;
                                            string str2;
                                            string paramsStr = "";

                                            // value 部の ! で続くパラメータ制御文字列を除く
                                            int startParamsIndex = value._Search("!");
                                            if (startParamsIndex == -1)
                                            {
                                                // ! がない
                                                str2 = value;
                                            }
                                            else
                                            {
                                                // ! がある
                                                str2 = value.Substring(0, startParamsIndex);
                                                paramsStr = value.Substring(startParamsIndex + 1);
                                            }

                                            QueryStringList argsList = new QueryStringList(paramsStr, splitChar: ',', trimKeyAndValue: true);

                                            var forwarderDef = new ForwarderDef(str2, argsList);

                                            this.ForwarderList.Add(forwarderDef);
                                        }
                                        else if (key._IsSamei("!Limit"))
                                        {
                                            // 制限の定義
                                            List<ZoneDef> tmp = new List<ZoneDef>();
                                            try
                                            {
                                                var limitsTokenList = value._Split(StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries, " ", "\t", "　", ",", ";");
                                                foreach (var limit in limitsTokenList)
                                                {
                                                    // パース
                                                    var limitDef = new ZoneDef(limit, new Vars(), true);
                                                    tmp.Add(limitDef);
                                                }
                                            }
                                            catch
                                            {
                                                // パースで例外が発生したら、安全のために空リストを設定したとみなす
                                                currentLimitZonesList = new List<ZoneDef>();
                                                throw;
                                            }
                                            currentLimitZonesList = tmp;
                                        }
                                        else
                                        {
                                            throw new CoresException($"Invalid instruction");
                                        }
                                    }
                                }
                                else
                                {
                                    // 2 回目の走査
                                    if (key.StartsWith("!") == false)
                                    {
                                        string str1 = key;
                                        string str2;
                                        string paramsStr = "";

                                        // value 部の ! で続くパラメータ制御文字列を除く
                                        int startParamsIndex = value._Search("!");
                                        if (startParamsIndex == -1)
                                        {
                                            // ! がない
                                            str2 = value;
                                        }
                                        else
                                        {
                                            // ! がある
                                            str2 = value.Substring(0, startParamsIndex);
                                            paramsStr = value.Substring(startParamsIndex + 1);
                                        }

                                        QueryStringList paramsList = new QueryStringList(paramsStr, splitChar: ',', trimKeyAndValue: true);
                                        
                                        EasyDnsResponderRecordSettings? settings = null;
                                        string ttlStr = paramsList._GetFirstValueOrDefault("ttl");
                                        if (ttlStr._IsFilled())
                                        {
                                            settings = new EasyDnsResponderRecordSettings
                                            {
                                                TtlSecs = ttlStr._ToInt(),
                                            };
                                        }

                                        if (str1._InStr(":") == false && str1._InStr(".") == false && str1._InStr("/") == false)
                                        {
                                            string recordTypeStr = str1; // "MX" とか
                                            string fqdnAndData = str2;

                                            // FQDN 部分とデータ部分に分ける
                                            if (fqdnAndData._GetKeysListAndValue(1, out var tmp1, out string data) == false)
                                            {
                                                throw new CoresException($"DNS Record String: Invalid Format. Str = '{fqdnAndData}'");
                                            }
                                            else
                                            {
                                                bool isDynamic = false;
                                                if (recordTypeStr._IsSamei("Dynamic"))
                                                {
                                                    recordTypeStr = tmp1[0];
                                                    // ダイナミックレコード
                                                    if (data._GetKeysListAndValue(1, out tmp1, out data) == false)
                                                    {
                                                        throw new CoresException($"DNS Record String: Invalid Format. Str = '{fqdnAndData}'");
                                                    }

                                                    isDynamic = true;
                                                }

                                                data = data.Trim();

                                                EasyDnsResponderRecordType recordType = EasyDnsResponderRecordType.None;

                                                // MX とかの手動レコード設定である。
                                                recordType = EasyDnsResponderRecord.StrToRecordType(recordTypeStr);
                                                if (recordType == EasyDnsResponderRecordType.None)
                                                {
                                                    // レコードタイプ文字列が不正である
                                                    throw new CoresException($"Specified manual DNS record type '{recordTypeStr}' is invalid");
                                                }
                                                if (recordType.EqualsAny(EasyDnsResponderRecordType.Any, EasyDnsResponderRecordType.SOA))
                                                {
                                                    // 手動指定できない特殊なレコードタイプである
                                                    throw new CoresException($"Specified manual DNS record type '{recordType}' cannot be specified here");
                                                }

                                                if (isDynamic && recordType == EasyDnsResponderRecordType.PTR)
                                                {
                                                    // ダイナミックレコードでは PTR に対応していない
                                                    throw new CoresException($"Specified dynamic DNS record type '{recordType}' cannot be specified here");
                                                }

                                                // FQDN 部の検査
                                                if (recordType == EasyDnsResponderRecordType.PTR)
                                                {
                                                    // PTR は特別処理を行なう (ゾーンが /24 の広さで、PTR のワイルドカード指定が /22 の広さ、というようなことが起こり得るので、ゾーンとは直接関連付けない)
                                                    // PTR の場合、FQDN 部は IP アドレスまたは IP サブネット、または in-addr.arpa または ip6.arpa 形式でなければならない
                                                    string ipOrSubnetStr = tmp1[0]._NonNullTrim();
                                                    string fqdn = data._NormalizeFqdn();
                                                    int subnetLength;
                                                    IPAddress ipOrSubnet;

                                                    if (ipOrSubnetStr == "in-addr.arpa" || ipOrSubnetStr.EndsWith(".in-addr.arpa") || ipOrSubnetStr == "ip6.addr" || ipOrSubnetStr.EndsWith(".ip6.addr"))
                                                    {
                                                        var tmp = IPUtil.PtrZoneOrFqdnToIpAddressAndSubnet(ipOrSubnetStr);
                                                        ipOrSubnet = tmp.Item1;
                                                        subnetLength = tmp.Item2;
                                                    }
                                                    else
                                                    {
                                                        IPUtil.ParseIPAndMask(ipOrSubnetStr, out ipOrSubnet, out IPAddress subnetMask);
                                                        subnetLength = IPUtil.SubnetMaskToInt(subnetMask);
                                                    }

                                                    bool isHostAddress = IPUtil.IsSubnetLenHostAddress(ipOrSubnet.AddressFamily, subnetLength);

                                                    // 1.0.0.0/24 -> aaa*bbb.aaa.example.org のようなホストアドレスが指定される場合は、FQDN は Wildcard 可である。
                                                    if (fqdn._IsValidFqdn(true, false) == false)
                                                    {
                                                        throw new CoresException("PTR record's target hostname must be a valid single host FQDN or wildcard FQDN");
                                                    }

                                                    (string beforeOfFirst, string afterOfFirst, string suffix) wildcardInfo = ("", "", "");

                                                    if (fqdn._InStr("*"))
                                                    {
                                                        // ワイルドカード FQDN の場合、aa*bb のようなものも許容する。
                                                        if (Str.TryParseFirstWildcardFqdnSandwitched(fqdn, out wildcardInfo) == false)
                                                        {
                                                            // おかしな FQDN である
                                                            throw new CoresException($"FQDN '{fqdn}''s wildcard form style must be like '*', 'abc*' or '*abc'");
                                                        }
                                                    }

                                                    string fqdn2 = fqdn;
                                                    if (fqdn._InStr("*"))
                                                    {
                                                        // abc*def.example.org -> StandardRecord 上は、*.example.org が指定されたとみなして登録する。
                                                        fqdn2 = "*" + wildcardInfo.suffix;
                                                    }

                                                    StandardRecord r = new StandardRecord
                                                    {
                                                        Type = isHostAddress ? StandardRecordType.ReverseSingle : StandardRecordType.ReverseSubnet,
                                                        Fqdn = fqdn2,
                                                        IpNetwork = ipOrSubnet,
                                                        IpSubnetMask = IPUtil.IntToSubnetMask(ipOrSubnet.AddressFamily, subnetLength),
                                                        SubnetLength = subnetLength,
                                                        FirstTokenWildcardBefore = wildcardInfo.beforeOfFirst,
                                                        FirstTokenWildcardAfter = wildcardInfo.afterOfFirst,
                                                        Settings = settings,
                                                    };

                                                    if (IsReverseFqdnAllowedByLimitList(r.IpNetwork, r.IpSubnetMask))
                                                    {
                                                        this.ReverseRecordsList.Add(r);
                                                    }
                                                }
                                                else
                                                {
                                                    // パースの試行 (ここでは、単に文法チェックためにパースを試行するだけであり、結果は不要である)
                                                    if (isDynamic == false)
                                                    {
                                                        EasyDnsResponderRecord.TryParseFromString(recordTypeStr + " " + fqdnAndData);
                                                    }

                                                    string fqdn = tmp1[0]._NormalizeFqdn();

                                                    if (fqdn._IsValidFqdn(recordType != EasyDnsResponderRecordType.NS) == false) // NS レコードではワイルドカードは使用できない
                                                    {
                                                        throw new CoresException($"DNS Record String: Invalid FQDN: '{fqdn}'");
                                                    }

                                                    // FQDN 部分を元に、DefineZone 済みのゾーンのいずれに一致するか検索する (最長一致)
                                                    var zone = DnsUtil.SearchLongestMatchDnsZone<ZoneDef>(this.ZoneList, fqdn, out string hostLabel, out _);

                                                    if (zone == null)
                                                    {
                                                        // 対応するゾーンがない
                                                        throw new CoresException($"Specified DNS record doesn't match any DefineZone zones");
                                                    }

                                                    if (recordType == EasyDnsResponderRecordType.NS)
                                                    {
                                                        // NS レコードの場合は、必ず、定義済み Zone レコードの FQDN よりも長くなければならない (サブドメインでなければならない)。
                                                        // (つまり、定義済み Zone レコードの FQDN と完全一致してはならない。)
                                                        if (zone.Fqdn._NormalizeFqdn() == fqdn._NormalizeFqdn())
                                                        {
                                                            throw new CoresException($"NS record's target FQDN must be a subdomain of the parent domain (meaning that it must not be exact match to the parent domain)");
                                                        }
                                                    }

                                                    QueryStringList? dynamicRecordQsList = null;

                                                    if (isDynamic)
                                                    {
                                                        // ダイナミックレコードの場合の変数処理
                                                        dynamicRecordQsList = new QueryStringList();
                                                        foreach (var kv in paramsList)
                                                        {
                                                            if (kv.Value.StartsWith("$"))
                                                            {
                                                                if (zone.Options.VarsList.TryGetValue(kv.Value.Substring(1), out string? valueTmp))
                                                                {
                                                                    dynamicRecordQsList.Add(kv.Key, valueTmp);
                                                                }
                                                                else
                                                                {
                                                                    throw new CoresException($"Specified variable '{kv.Value.Substring(1)}' is not found on the 'Vars' variables list in zone '{zone.Fqdn}'.");
                                                                }
                                                            }
                                                            else
                                                            {
                                                                dynamicRecordQsList.Add(kv.Key, kv.Value);
                                                            }
                                                        }
                                                    }

                                                    if (IsForwardFqdnAllowedByLimitList(fqdn))
                                                    {
                                                        // ゾーンのカスタムレコードとして追加
                                                        zone.CustomRecordList.Add(new CustomRecord
                                                        {
                                                            Label = hostLabel,
                                                            Data = data,
                                                            Type = recordType,
                                                            Settings = settings,
                                                            IsDynamicRecord = isDynamic,
                                                            DynamicRecordParamsList = dynamicRecordQsList
                                                        });
                                                    }
                                                }
                                            }
                                        }
                                        else
                                        {
                                            // 普通の正引きまたは逆引きレコード
                                            // 1.2.3.0/24   *.aaa.example.org
                                            // 1.2.3.4      abc.example.org とか
                                            string ipMaskListStr;
                                            string fqdnListStr;

                                            // str1 と str2 は入れ換え可能である。
                                            // 1 と 2 のいずれに IP アドレスが含まれているのか判別をする。
                                            if (Str.IsIpSubnetStr(str1))
                                            {
                                                ipMaskListStr = str1;
                                                fqdnListStr = str2;
                                            }
                                            else
                                            {
                                                fqdnListStr = str1;
                                                ipMaskListStr = str2;
                                            }

                                            // 複数書いてあることもあるので、パースを行なう
                                            string[] ipMaskList = ipMaskListStr._Split(StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries, " ", "\t", "　", ",", ";");
                                            string[] fqdnList = fqdnListStr._Split(StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries, " ", "\t", "　", ",", ";");

                                            foreach (var ipMask in ipMaskList)
                                            {
                                                foreach (var tmp2 in fqdnList)
                                                {
                                                    string tmp1 = tmp2;
                                                    int mode = 0;
                                                    IPUtil.ParseIPAndSubnetMask(ipMask, out IPAddress ipOrSubnet, out IPAddress mask);
                                                    int subnetLength = IPUtil.SubnetMaskToInt(mask);
                                                    bool isHostAddress = IPUtil.IsSubnetLenHostAddress(ipOrSubnet.AddressFamily, subnetLength);

                                                    if (tmp1.StartsWith("+"))
                                                    {
                                                        // +aaa.example.org のような形式である
                                                        // これは、1.2.3.4 -> CNAME 4.3.2.1.aaa.example.org のような特殊な CNAME 変換を意味する
                                                        mode = 1;
                                                        tmp1 = tmp1.Substring(1);
                                                    }
                                                    else if (tmp1.StartsWith("-"))
                                                    {
                                                        // -aaa.example.org のような形式である
                                                        // これは、1.2.3.4 -> CNAME 1-2-3-4.aaa.example.org のような特殊な CNAME 変換を意味する
                                                        mode = 2;
                                                        tmp1 = tmp1.Substring(1);
                                                    }
                                                    else if (tmp1.StartsWith("@"))
                                                    {
                                                        // @ns01.example.org のような形式である。
                                                        // これは、1.2.3.0/24 -> NS ns01.example.org のような NS Delegate を意味する
                                                        mode = 3;
                                                        tmp1 = tmp1.Substring(1);
                                                    }
                                                    string fqdn = tmp1._NormalizeFqdn();
                                                    if (fqdn._IsValidFqdn(mode == 0, false) == false)
                                                    {
                                                        // おかしな FQDN である
                                                        throw new CoresException($"FQDN '{fqdn}' is not a valid single host or wildcard FQDN");
                                                    }

                                                    if (mode == 1 || mode == 2)
                                                    {
                                                        // CNAME 特殊処理
                                                        StandardRecord r = new StandardRecord
                                                        {
                                                            Type = isHostAddress ? StandardRecordType.ReverseCNameSingle : StandardRecordType.ReverseCNameSubnet,
                                                            ReverseCName_HyphenMode = (mode == 2),
                                                            Fqdn = fqdn,
                                                            IpNetwork = ipOrSubnet,
                                                            IpSubnetMask = IPUtil.IntToSubnetMask(ipOrSubnet.AddressFamily, subnetLength),
                                                            SubnetLength = subnetLength,
                                                            Settings = settings,
                                                        };

                                                        if (IsReverseFqdnAllowedByLimitList(r.IpNetwork, r.IpSubnetMask))
                                                        {
                                                            this.ReverseRecordsList.Add(r);
                                                        }
                                                    }
                                                    else if (mode == 3)
                                                    {
                                                        // NS 特殊処理
                                                        // NS の場合、内部的に in-addr.arpa または ip6.arpa に変換する
                                                        string reverseFqdn = IPUtil.IPAddressOrSubnetToPtrZoneOrFqdn(ipOrSubnet, subnetLength);

                                                        // reverseFqdn を元に、DefineZone 済みのゾーンのいずれに一致するか検索する (最長一致)
                                                        var zone = DnsUtil.SearchLongestMatchDnsZone<ZoneDef>(this.ZoneList, reverseFqdn, out string hostLabel, out _);

                                                        if (zone == null)
                                                        {
                                                            // 対応するゾーンがない
                                                            throw new CoresException($"Specified reverse DNS zone '{reverseFqdn}' doesn't match any DefineZone zones");
                                                        }

                                                        // NS レコードの場合は、必ず、定義済み Zone レコードの FQDN よりも長くなければならない (サブドメインでなければならない)。
                                                        // (つまり、定義済み Zone レコードの FQDN と完全一致してはならない。)
                                                        if (zone.Fqdn._NormalizeFqdn() == reverseFqdn._NormalizeFqdn())
                                                        {
                                                            throw new CoresException($"Reverse DNS zone '{reverseFqdn}' must be a subdomain of the parent domain '{zone.Fqdn}' (meaning that it must not be exact match to the parent domain)");
                                                        }

                                                        if (IsReverseFqdnAllowedByLimitList(ipOrSubnet, IPUtil.IntToSubnetMask(ipOrSubnet.AddressFamily, subnetLength)))
                                                        {
                                                            // ゾーンのカスタムレコードとして追加
                                                            zone.CustomRecordList.Add(new CustomRecord { Label = hostLabel, Data = fqdn, Type = EasyDnsResponderRecordType.NS, Settings = settings, });
                                                        }
                                                    }
                                                    else
                                                    {
                                                        (string beforeOfFirst, string afterOfFirst, string suffix) wildcardInfo = ("", "", "");

                                                        if (fqdn._InStr("*"))
                                                        {
                                                            // ワイルドカード FQDN の場合、aa*bb のようなものも許容する。
                                                            if (Str.TryParseFirstWildcardFqdnSandwitched(fqdn, out wildcardInfo) == false)
                                                            {
                                                                // おかしな FQDN である
                                                                throw new CoresException($"FQDN '{fqdn}''s wildcard form style must be like '*', 'abc*' or '*abc'");
                                                            }
                                                        }

                                                        string fqdn2 = fqdn;
                                                        if (fqdn._InStr("*"))
                                                        {
                                                            // abc*def.example.org -> StandardRecord 上は、*.example.org が指定されたとみなして登録する。
                                                            fqdn2 = "*" + wildcardInfo.suffix;
                                                        }

                                                        // 普通の正引き + 逆引き同時定義レコード
                                                        // 最初に、正引きの処理をする。
                                                        // ゾーン検索
                                                        var zone = DnsUtil.SearchLongestMatchDnsZone<ZoneDef>(this.ZoneList, fqdn, out string hostLabel, out _);
                                                        if (zone != null)
                                                        {
                                                            // 対応するゾーンがある場合のみ処理を行なう。対応するゾーンがない場合は、何もエラーを出さずに無視する。
                                                            StandardRecord r = new StandardRecord
                                                            {
                                                                Type = isHostAddress ? StandardRecordType.ForwardSingle : StandardRecordType.ForwardSubnet,
                                                                Fqdn = fqdn2,
                                                                IpNetwork = ipOrSubnet,
                                                                IpSubnetMask = IPUtil.IntToSubnetMask(ipOrSubnet.AddressFamily, subnetLength),
                                                                SubnetLength = subnetLength,
                                                                FirstTokenWildcardBefore = wildcardInfo.beforeOfFirst,
                                                                FirstTokenWildcardAfter = wildcardInfo.afterOfFirst,
                                                                Settings = settings,
                                                            };

                                                            // ゾーンの通常レコードとして追加
                                                            if (IsForwardFqdnAllowedByLimitList(r.Fqdn))
                                                            {
                                                                zone.StandardForwardRecordList.Add(r);
                                                            }
                                                        }

                                                        // 次に、逆引きの処理をする。
                                                        // これは、ゾーンに無関係に登録をすればよい。
                                                        if (true)
                                                        {
                                                            StandardRecord r = new StandardRecord
                                                            {
                                                                Type = isHostAddress ? StandardRecordType.ReverseSingle : StandardRecordType.ReverseSubnet,
                                                                Fqdn = fqdn2,
                                                                IpNetwork = ipOrSubnet,
                                                                IpSubnetMask = IPUtil.IntToSubnetMask(ipOrSubnet.AddressFamily, subnetLength),
                                                                SubnetLength = subnetLength,
                                                                FirstTokenWildcardBefore = wildcardInfo.beforeOfFirst,
                                                                FirstTokenWildcardAfter = wildcardInfo.afterOfFirst,
                                                                Settings = settings,
                                                            };

                                                            if (IsReverseFqdnAllowedByLimitList(r.IpNetwork, r.IpSubnetMask))
                                                            {
                                                                this.ReverseRecordsList.Add(r);
                                                            }
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                            else
                            {
                                if (j == 0) throw new CoresException("Invalid syntax");
                            }
                        }
                        else if (line._IsFilled())
                        {
                            if (j == 0) throw new CoresException("Invalid syntax");
                        }
                    }
                    catch (Exception ex)
                    {
                        err.WriteLine($"Line #{i + 1}: '{lineSrc}'");
                        err.WriteLine($"  Error: {ex.Message}");
                    }
                }
            }

            // すべてのレコードのデータ登録が終わったら、まとめて、逆引きロンゲストマッチ用 Radix Trie を生成する。
            foreach (var r in this.ReverseRecordsList.Where(x => x.Type.EqualsAny(StandardRecordType.ReverseCNameSingle, StandardRecordType.ReverseCNameSubnet, StandardRecordType.ReverseSingle, StandardRecordType.ReverseSubnet)))
            {
                // 上から順に優先して登録する。全く同一のサブネットについては最初の 1 つしか登録されないが、これは正常である。
                // (PTR レコードで 2 つ以上の互いに異なる応答があるのはおかしい)
                this.ReverseRadixTrie.Insert(r.IpNetwork, r.SubnetLength, r);
            }

            // ユーティリティ関数: 指定した正引きレコードが Limit の範囲内かどうか検査
            bool IsForwardFqdnAllowedByLimitList(string normalizedFqdn)
            {
                if (currentLimitZonesList == null) return true;

                return currentLimitZonesList.Where(x => x.Type == ZoneDefType.Forward).Any(x => Str.IsEqualToOrSubdomainOf(EnsureSpecial.Yes, normalizedFqdn, x.Fqdn, out _));
            }

            // ユーティリティ関数: 指定した逆引きレコードが Limit の範囲内かどうか検査
            bool IsReverseFqdnAllowedByLimitList(IPAddress network, IPAddress subnetMask)
            {
                if (currentLimitZonesList == null) return true;

                var ipStart = IPUtil.GetPrefixAddress(network, subnetMask);
                var ipEnd = IPUtil.GetBroadcastAddress(network, subnetMask);
                foreach (var def in currentLimitZonesList.Where(x => x.Type == ZoneDefType.Reverse))
                {
                    if (IPUtil.IsInSameNetwork(ipStart, def.ReverseIpNetwork, def.ReverseIpSubnetMask, true) &&
                        IPUtil.IsInSameNetwork(ipEnd, def.ReverseIpNetwork, def.ReverseIpSubnetMask, true))
                    {
                        return true;
                    }
                }

                return false;
            }
        }
    }

    public class DynConfig : HadbBasedServiceDynConfig
    {
        [SimpleComment("Set true to make DNS server to write DNS debug access logs to the Log/Access directory")]
        public bool Dns_SaveDnsQueryAccessLogForDebug = false;

        [SimpleComment("Copy query packet's Additional Records field to the response packet (including EDNS fields). This might confuse DNS cache server like dnsdist.")]
        public bool Dns_Protocol_CopyQueryAdditionalRecordsToResponse = false;

        [SimpleComment("Set true if you want this DDNS server to accept UDP Proxy Protocol Version 2.0 (defined by haproxy and supported by some DNS proxies such as dnsdist)")]
        public bool Dns_Protocol_ParseUdpProxyProtocolV2 = false;

        [SimpleComment("If DDns_Protocol_AcceptUdpProxyProtocolV2 is true you can specify the source IP address ACL to accept UDP Proxy Protocol (You can specify multiple items. e.g. 127.0.0.0/8,1.2.3.0/24)")]
        public string Dns_Protocol_ProxyProtocolAcceptSrcIpAcl = "";

        public int Dns_Protocol_TcpAxfrMaxRecordsPerMessage = 32;

        public int Dns_ZoneForceReloadIntervalMsecs = 5 * 60 * 1000;

        public string Dns_ZoneDefFilePathOrUrl = "";


        protected override void NormalizeImpl()
        {
            Dns_Protocol_ProxyProtocolAcceptSrcIpAcl = EasyIpAcl.NormalizeRules(Dns_Protocol_ProxyProtocolAcceptSrcIpAcl, false, true);

            if (Dns_ZoneDefFilePathOrUrl._IsEmpty())
            {
                Dns_ZoneDefFilePathOrUrl = Lfs.PathParser.Combine(Env.AppRootDir, "ZoneDef.config");
            }

            if (Dns_Protocol_TcpAxfrMaxRecordsPerMessage <= 0)
            {
                Dns_Protocol_TcpAxfrMaxRecordsPerMessage = 32;
            }

            if (Dns_ZoneForceReloadIntervalMsecs <= 0)
            {
                Dns_ZoneForceReloadIntervalMsecs = 5 * 60 * 1000;
            }

            base.NormalizeImpl();
        }
    }

    public class MemDb : HadbBasedServiceMemDb
    {
        protected override void AddDefinedUserDataTypesImpl(List<Type> ret)
        {
        }

        protected override void AddDefinedUserLogTypesImpl(List<Type> ret)
        {
        }
    }

    public class HiveSettings : HadbBasedServiceHiveSettingsBase
    {
        public int Dns_UdpListenPort;
        public int Dns_TcpListenPort;

        public override void NormalizeImpl()
        {
            if (Dns_UdpListenPort <= 0) Dns_UdpListenPort = Consts.Ports.Dns;
            if (Dns_TcpListenPort <= 0) Dns_TcpListenPort = Consts.Ports.Dns;
        }
    }

    [RpcInterface]
    public interface IRpc : IHadbBasedServiceRpcBase
    {
        [RpcMethodHelp("テスト関数。パラメータで int 型で指定された値を文字列に変換し、Hello という文字列を前置して返却します。RPC を呼び出すためのテストコードを実際に記述する際のテストとして便利です。", "Hello 123")]
        public Task<string> Test_HelloWorld([RpcParamHelp("テスト入力整数値", 123)] int i);
    }

    public EasyDnsResponderBasedDnsServer DnsServer { get; private set; } = null!;

    AsyncLoopManager LoopManager = null!;

    public IpaDnsService(IpaDnsServiceStartupParam startupParam, IpaDnsServiceHook hook) : base(startupParam, hook)
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
                UdpPort = this.SettingsFastSnapshot.Dns_UdpListenPort,
                TcpPort = this.SettingsFastSnapshot.Dns_TcpListenPort,
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
                    this.LoopManager.Fire();
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

    string LastConfigBody = " ";

    // Config 読み込み
    async Task<Config?> LoadZoneConfigAsync(EasyDnsResponderSettings dst, StringWriter err, bool forceReload, CancellationToken cancel)
    {
        var config = this.Hadb.CurrentDynamicConfig;

        // ファイル読み込み
        string body = await Lfs.ReadStringFromFileAsync(config.Dns_ZoneDefFilePathOrUrl, cancel: cancel);

        if (body == LastConfigBody && forceReload == false)
        {
            // 前回ゾーンを構築した時からファイル内容に変化がなければ何もしない
            return null;
        }

        Con.WriteLine($"Zone Def File is {(forceReload ? "being reloaded forcefully" : "changed")}. Body size = {body._GetBytes_UTF8().Length._ToString3()} bytes. Reloading...");

        Config cfg = new Config(body, err);

        this.Hook.InitDynamicResponderFactoryList(cfg.DynamicResponderFactoryDict);

        // ゾーンの定義
        foreach (var zoneDef in cfg.ZoneList.Values)
        {
            EasyDnsResponderZone zone = new EasyDnsResponderZone
            {
                DomainName = zoneDef.Fqdn,
                DefaultSettings = new EasyDnsResponderRecordSettings
                {
                    TtlSecs = zoneDef.Options.DefaultTtl,
                },
                TcpAxfrAllowedAcl = zoneDef.Options.TcpAxfrAllowedAcl._NonNullTrim(),
                NotifyServers = zoneDef.Options.NotifyServers._NonNullTrim(),
            };

            // SOA レコードを定義
            zone.RecordList.Add(new EasyDnsResponderRecord
            {
                Type = EasyDnsResponderRecordType.SOA,
                Contents = $"{zoneDef.Options.NameServersFqdnList._ElementAtOrDefaultStr(0, "unknown.example.org")._NonNullTrim()._Split(StringSplitOptions.None, "=").FirstOrDefault()} {zoneDef.Options.Responsible._FilledOrDefault("unknown.example.org")} {Consts.Numbers.MagicNumber_u32} {zoneDef.Options.RefreshInterval} {zoneDef.Options.RetryInterval} {zoneDef.Options.ExpireInterval} {zoneDef.Options.NegativeCacheTtl}",
            });

            // NS レコードを定義
            foreach (var ns in zoneDef.Options.NameServersFqdnList)
            {
                zone.RecordList.Add(new EasyDnsResponderRecord
                {
                    Type = EasyDnsResponderRecordType.NS,
                    Contents = ns,
                });
            }

            // カスタムレコードを登録
            foreach (var customDef in zoneDef.CustomRecordList)
            {
                if (customDef.IsDynamicRecord == false)
                {
                    // 普通のカスタムレコード
                    zone.RecordList.Add(EasyDnsResponderRecord.FromString(customDef.Type.ToString() + " " + customDef.Label._FilledOrDefault("@") + " " + customDef.Data, settings: customDef.Settings));
                }
                else
                {
                    // ダイナミックレコード
                    var record = new EasyDnsResponderRecord
                    {
                        Type = customDef.Type,
                        Name = customDef.Label,
                        Settings = customDef.Settings,
                        Contents = "_this_is_custom_dynamic_record:" + Str.GenRandStr(),
                        Attribute = EasyDnsResponderRecordAttribute.DynamicRecord,
                    };

                    if (cfg.DynamicResponderFactoryDict.TryGetValue(customDef.Data, out var factory))
                    {
                        IpaDnsServiceDynamicResponderBase responder = factory(new IpaDnsServiceDynamicResponderInitParam(this, zone, record, customDef, customDef.Data));

                        cfg.DynamicResponderDict[record.Contents] = responder;
                    }

                    zone.RecordList.Add(record);
                }
            }

            if (zoneDef.Type == ZoneDefType.Forward)
            {
                // 正引きゾーンの場合は、通常の正引きレコードを定義
                foreach (var recordDef in zoneDef.StandardForwardRecordList.Where(x => x.Type.EqualsAny(StandardRecordType.ForwardSingle, StandardRecordType.ForwardSubnet)))
                {
                    string label = Str.GetSubdomainLabelFromParentAndSubFqdn(zone.DomainName, recordDef.Fqdn);

                    if (recordDef.Fqdn._InStr("*") == false)
                    {
                        // 非ワイルドカード
                        // 仮にサブネット型であっても、最初の 1 個目の IP アドレスを返せば良い
                        zone.RecordList.Add(new EasyDnsResponderRecord
                        {
                            Type = recordDef.IpNetwork.AddressFamily == AddressFamily.InterNetwork ? EasyDnsResponderRecordType.A : EasyDnsResponderRecordType.AAAA,
                            Name = label,
                            Contents = recordDef.IpNetwork._RemoveScopeId().ToString(),
                            Settings = recordDef.Settings,
                        });
                    }
                    else
                    {
                        // ワイルドカード
                        EasyJsonStrAttributes attributes = new EasyJsonStrAttributes();
                        attributes["wildcard_before_str"] = recordDef.FirstTokenWildcardBefore;
                        attributes["wildcard_after_str"] = recordDef.FirstTokenWildcardAfter;

                        zone.RecordList.Add(new EasyDnsResponderRecord
                        {
                            Type = recordDef.IpNetwork.AddressFamily == AddressFamily.InterNetwork ? EasyDnsResponderRecordType.A : EasyDnsResponderRecordType.AAAA,
                            Name = label,
                            Contents = $"{recordDef.IpNetwork._RemoveScopeId().ToString()}/{recordDef.SubnetLength}",
                            Settings = recordDef.Settings,
                            Param = attributes,
                        });
                    }
                }
            }
            else
            {
                // 逆引きゾーンの場合は、ワイルドカードのダイナミックレコードを定義 (実際の結果はコールバック関数で計算して応答する)
                zone.RecordList.Add(new EasyDnsResponderRecord
                {
                    Type = EasyDnsResponderRecordType.PTR,
                    Name = "*",
                    Contents = "_this_is_ptr_record_refer_ReverseRecordsList",
                    Attribute = EasyDnsResponderRecordAttribute.DynamicRecord,
                });

                // 逆引きゾーンであっても、ゾーンのシリアル番号の計算 (Digest の変化の検出) のために、この逆引きゾーンの範囲内に含まれる可能性のあるすべての逆引きレコードを列挙し、そのダイジェスト値をダミー的に計算する
                List<StandardRecord> listForCalcDigest = new List<StandardRecord>();

                if (Str.IsEqualToOrSubdomainOf(zone.DomainName, "in-addr.arpa", out _) ||
                    Str.IsEqualToOrSubdomainOf(zone.DomainName, "ip6.arpa", out _))
                {
                    bool ok = false;
                    (IPAddress, int) targetSubnetInfo = default;

                    try
                    {
                        targetSubnetInfo = IPUtil.PtrZoneOrFqdnToIpAddressAndSubnet(zone.DomainName);
                        ok = true;
                    }
                    catch { }

                    if (ok)
                    {
                        var ipCmp = IpComparer.ComparerWithIgnoreScopeId;
                        var zoneIpStart = IPUtil.GetPrefixAddress(targetSubnetInfo.Item1!, targetSubnetInfo.Item2);
                        var zoneIpEnd = IPUtil.GetBroadcastAddress(targetSubnetInfo.Item1!, targetSubnetInfo.Item2);

                        // すべての逆引きゾーン定義を走査
                        // Longest Match を実現するため、サブネット長の長い順に安定ソート
                        foreach (var record in cfg.ReverseRecordsList
                            .Where(x => x.Type.EqualsAny(StandardRecordType.ReverseCNameSingle, StandardRecordType.ReverseCNameSubnet, StandardRecordType.ReverseSingle, StandardRecordType.ReverseSubnet))
                            .OrderByDescending(x => x.SubnetLength))
                        {
                            IPAddress recordIpStart, recordIpEnd;

                            // サブネットレコードの範囲をゾーンの範囲で分割
                            if (record.Type == StandardRecordType.ReverseCNameSubnet || record.Type == StandardRecordType.ReverseSubnet)
                            {
                                recordIpStart = IPUtil.GetPrefixAddress(record.IpNetwork, record.IpSubnetMask);
                                recordIpEnd = IPUtil.GetBroadcastAddress(record.IpNetwork, record.IpSubnetMask);
                            }
                            else
                            {
                                recordIpStart = record.IpNetwork;
                                recordIpEnd = record.IpNetwork;
                            }

                            bool addThisRow = false;

                            IPAddress scopeStart = ipCmp.Max(zoneIpStart, recordIpStart);
                            IPAddress scopeEnd = ipCmp.Min(zoneIpEnd, recordIpEnd);

                            if (record.Type == StandardRecordType.ReverseCNameSubnet || record.Type == StandardRecordType.ReverseSubnet)
                            {
                                if (ipCmp.Compare(scopeStart, scopeEnd) <= 0)
                                {
                                    IPAddr scopeStartEx = IPAddr.FromAddress(scopeStart);
                                    IPAddr scopeEndEx = IPAddr.FromAddress(scopeEnd);

                                    BigNumber scopeStartBn = scopeStartEx.GetBigNumber();
                                    BigNumber scopeEndBn = scopeEndEx.GetBigNumber();

                                    BigNumber num = scopeEndBn - scopeStartBn + 1;
                                    if (num >= 1)
                                    {
                                        addThisRow = true;
                                    }
                                }
                            }
                            else
                            {
                                if (ipCmp.Compare(scopeStart, scopeEnd) == 0)
                                {
                                    addThisRow = true;
                                }
                            }

                            if (addThisRow)
                            {
                                listForCalcDigest.Add(record);
                            }
                        }
                    }
                }

                zone.AdditionalDigestSeedStr = listForCalcDigest._CalcObjectDigestAsJson()._GetHexString();
            }

            dst.ZoneList.Add(zone);
        }

        // フォワーダの定義
        foreach (var forwarderDef in cfg.ForwarderList)
        {
            try
            {
                EasyDnsResponderForwarder fwd = new EasyDnsResponderForwarder
                {
                    CallbackId = "_this_is_static_defined_forwarder_callback",
                    Selector = forwarderDef.Selector,
                    TargetServers = await LocalNet.ResolveHostAndPortListStrToIpAndPortListStrAsync(forwarderDef.ForwarderList, Consts.Ports.Dns, cancel: cancel),
                    TimeoutMsecs = forwarderDef.TimeoutMsecs,
                    ArgsList = forwarderDef.ArgsList,
                };

                dst.ForwarderList.Add(fwd);
            }
            catch (Exception ex)
            {
                ex._Error();
            }
        }

        LastConfigBody = body;

        string errorStr = err.ToString();

        Con.WriteLine($"Zone Def File load completed.");

        return cfg;
    }

    string LastConfigJson = " ";

    long LastConfigReloadTick = 0;

    // HADB の DynamicConfig を元に DDNS サーバーの設定を構築してリロードする
    async Task ReloadLoopTaskAsync(AsyncLoopManager manager, CancellationToken cancel)
    {
        StringWriter err = new StringWriter();

        await Task.CompletedTask;

        var config = this.Hadb.CurrentDynamicConfig;

        string configJson = config._ObjectToJson(compact: true);

        EasyDnsResponderSettings settings = new EasyDnsResponderSettings
        {
            DefaultSettings = new EasyDnsResponderRecordSettings
            {
                TtlSecs = 1234,
            },
        };

        settings.SaveAccessLogForDebug = config.Dns_SaveDnsQueryAccessLogForDebug;
        settings.CopyQueryAdditionalRecordsToResponse = config.Dns_Protocol_CopyQueryAdditionalRecordsToResponse;

        Config? cfg = await LoadZoneConfigAsync(settings, err, LastConfigJson != configJson || (TickNow > LastConfigReloadTick + config.Dns_ZoneForceReloadIntervalMsecs), cancel);

        if (cfg == null)
        {
            // Dynamic Config にも Zone Config にも変化がないので、何もしない
            return;
        }

        LastConfigReloadTick = TickNow;

        LastConfigJson = configJson;

        try
        {
            this.DnsServer.ApplySetting(settings);

            var currentDynOptions = this.DnsServer.DnsServer.GetCurrentDynOptions();

            currentDynOptions.ParseUdpProxyProtocolV2 = config.Dns_Protocol_ParseUdpProxyProtocolV2;
            currentDynOptions.DnsProxyProtocolAcceptSrcIpAcl = config.Dns_Protocol_ProxyProtocolAcceptSrcIpAcl;
            currentDynOptions.DnsTcpAxfrMaxRecordsPerMessage = config.Dns_Protocol_TcpAxfrMaxRecordsPerMessage;

            this.DnsServer.DnsServer.SetCurrentDynOptions(currentDynOptions);

            this.DnsServer.DnsResponder.ForwarderRequestTransformerCallback = (inData) =>
            {
                if (inData.CallbackId == "_this_is_static_defined_forwarder_callback")
                {
                    // フォワーダで Modifier が指定されている場合、その Modifier を用いてフォワーダのリクエストとレスポンスを加工する
                    var args = inData.ArgsList;
                    string modifier = args._GetStrFirst("modifier").ToLowerInvariant();

                    switch (modifier)
                    {
                        case "replace_query_domain":
                            // ドメイン名部分の文字列置換
                            var srcList = args.Where(x => x.Key._IsSamei("src")).Select(x => x.Value).ToList();
                            var dstList = args.Where(x => x.Key._IsSamei("dst")).Select(x => x.Value).ToList();
                            int numSrcDest = Math.Min(srcList.Count, dstList.Count);

                            List<Tuple<string, string>> replaceStringList = new List<Tuple<string, string>>();

                            // DNS クエリ / レスポンス変形のサンプルコード
                            var src = (DnsMessage)inData.OriginalRequestPacket.Message;
                            for (int i = 0; i < src.Questions.Count; i++)
                            {
                                var q = src.Questions[i];
                                var fqdn = q.Name.ToNormalizedFqdnFast();

                                for (int j = 0; j < numSrcDest; j++)
                                {
                                    if (srcList[j]._IsFilled() && dstList[j]._IsFilled())
                                    {
                                        if (Str.IsEqualToOrSubdomainOf(fqdn, srcList[j], out string hostLabel))
                                        {
                                            string fqdn2 = Str.CombineFqdn(hostLabel, dstList[j]);
                                            replaceStringList.Add(new Tuple<string, string>(fqdn, fqdn2));
                                            q.Name = DomainName.Parse(fqdn2);
                                            break;
                                        }
                                    }
                                }
                            }

                            return new EasyDnsResponderForwarderRequestTransformerCallbackResult
                            {
                                ModifiedDnsRequestMessage = src,
                                ForwarderResponseTransformerCallback = inData2 =>
                                {
                                    var src = (DnsMessage)inData2.OriginalResponsePacket.Message;
                                    for (int i = 0; i < src.Questions.Count; i++)
                                    {
                                        var q = src.Questions[i];
                                        var fqdn = q.Name.ToNormalizedFqdnFast();
                                        for (int j = 0; j < numSrcDest; j++)
                                        {
                                            if (srcList[j]._IsFilled() && dstList[j]._IsFilled())
                                            {
                                                if (Str.IsEqualToOrSubdomainOf(fqdn, dstList[j], out string hostLabel))
                                                {
                                                    string fqdn2 = Str.CombineFqdn(hostLabel, srcList[j]);
                                                    replaceStringList.Add(new Tuple<string, string>(fqdn, fqdn2));
                                                    q.Name = DomainName.Parse(fqdn2);
                                                    break;
                                                }
                                            }
                                        }
                                    }
                                    for (int i = 0; i < src.AnswerRecords.Count; i++)
                                    {
                                        var answer = src.AnswerRecords[i];
                                        var fqdn = answer.Name.ToNormalizedFqdnFast();
                                        for (int j = 0; j < numSrcDest; j++)
                                        {
                                            if (srcList[j]._IsFilled() && dstList[j]._IsFilled())
                                            {
                                                if (Str.IsEqualToOrSubdomainOf(fqdn, dstList[j], out string hostLabel))
                                                {
                                                    string fqdn2 = Str.CombineFqdn(hostLabel, srcList[j]);
                                                    replaceStringList.Add(new Tuple<string, string>(fqdn, fqdn2));
                                                    answer.Name = DomainName.Parse(fqdn2);
                                                    break;
                                                }
                                            }
                                        }
                                    }
                                    return new EasyDnsResponderForwarderResponseTransformerCallbackResult
                                    {
                                        ModifiedDnsResponseMessage = src,
                                    };
                                },
                            };
                    }
                }
                return new EasyDnsResponderForwarderRequestTransformerCallbackResult
                {
                };
            };

            this.DnsServer.DnsResponder.TcpAxfrCallback = async (req) =>
            {
                // 標準的な静的レコードリストの構築
                List<Tuple<EasyDnsResponder.Record, string?>> list = await req.GenerateStandardStaticRecordsListFromZoneDataAsync(req.Cancel);

                var rootVirtualZone = new EasyDnsResponder.Zone(isVirtualRootZone: EnsureSpecial.Yes, req.ZoneInternal);

                HashSet<string> ipHashSet = new HashSet<string>();

                // 動的レコードリストの構築 (逆引きゾーン)
                if (Str.IsEqualToOrSubdomainOf(req.Zone.DomainName, "in-addr.arpa", out _) ||
                    Str.IsEqualToOrSubdomainOf(req.Zone.DomainName, "ip6.arpa", out _))
                {
                    bool ok = false;
                    (IPAddress, int) targetSubnetInfo = default;

                    try
                    {
                        targetSubnetInfo = IPUtil.PtrZoneOrFqdnToIpAddressAndSubnet(req.Zone.DomainName);
                        ok = true;
                    }
                    catch (Exception ex)
                    {
                        ex._Error();
                    }

                    if (ok)
                    {
                        var ipCmp = IpComparer.ComparerWithIgnoreScopeId;
                        var zoneIpStart = IPUtil.GetPrefixAddress(targetSubnetInfo.Item1!, targetSubnetInfo.Item2);
                        var zoneIpEnd = IPUtil.GetBroadcastAddress(targetSubnetInfo.Item1!, targetSubnetInfo.Item2);

                        // すべての逆引きゾーン定義を走査
                        // Longest Match を実現するため、サブネット長の長い順に安定ソート
                        foreach (var record in cfg.ReverseRecordsList
                            .Where(x => x.Type.EqualsAny(StandardRecordType.ReverseCNameSingle, StandardRecordType.ReverseCNameSubnet, StandardRecordType.ReverseSingle, StandardRecordType.ReverseSubnet))
                            .OrderByDescending(x => x.SubnetLength))
                        {
                            IPAddress recordIpStart, recordIpEnd;

                            // サブネットレコードの範囲をゾーンの範囲で分割
                            if (record.Type == StandardRecordType.ReverseCNameSubnet || record.Type == StandardRecordType.ReverseSubnet)
                            {
                                recordIpStart = IPUtil.GetPrefixAddress(record.IpNetwork, record.IpSubnetMask);
                                recordIpEnd = IPUtil.GetBroadcastAddress(record.IpNetwork, record.IpSubnetMask);
                            }
                            else
                            {
                                recordIpStart = record.IpNetwork;
                                recordIpEnd = record.IpNetwork;
                            }

                            List<IPAddress> targetIpList = new List<IPAddress>();

                            IPAddress scopeStart = ipCmp.Max(zoneIpStart, recordIpStart);
                            IPAddress scopeEnd = ipCmp.Min(zoneIpEnd, recordIpEnd);

                            if (ipCmp.Compare(scopeStart, scopeEnd) <= 0)
                            {
                                IPAddr scopeStartEx = IPAddr.FromAddress(scopeStart);
                                IPAddr scopeEndEx = IPAddr.FromAddress(scopeEnd);

                                BigNumber scopeStartBn = scopeStartEx.GetBigNumber();
                                BigNumber scopeEndBn = scopeEndEx.GetBigNumber();

                                BigNumber num = scopeEndBn - scopeStartBn + 1;

                                if (num <= 16777216)
                                {
                                    for (int i = 0; i < num; i++)
                                    {
                                        var ip = scopeStartEx.Add(i).GetIPAddress();

                                        string ipStr = ip._RemoveScopeId().ToString();

                                        if (ipHashSet.Add(ipStr))
                                        {
                                            List<Tuple<EasyDnsResponder.Record, string?>> tmpList = new List<Tuple<EasyDnsResponder.Record, string?>>();

                                            string ptrStr = IPUtil.IPAddressOrSubnetToPtrZoneOrFqdn(ip);
                                            string ptrStrForSort = IPUtil.IPAddressOrSubnetToPtrZoneOrFqdn(ip, ipv4AllDigitsForSortKey: true);

                                            EasyDnsResponderRecordSettings settings = record.Settings ?? req.ZoneInternal.Settings;

                                            if (record.Type == StandardRecordType.ReverseSingle || record.Type == StandardRecordType.ReverseSubnet)
                                            {
                                                if (record.Fqdn.StartsWith("*.", StringComparison.Ordinal))
                                                {
                                                    // Subnet Wildcard PTR
                                                    string baseFqdn = record.Fqdn.Substring(2);
                                                    string fqdn = IPUtil.GenerateWildCardDnsFqdn(ip, baseFqdn, record.FirstTokenWildcardBefore, record.FirstTokenWildcardAfter);

                                                    tmpList.Add(new Tuple<EasyDnsResponder.Record, string?>(new EasyDnsResponder.Record_PTR(rootVirtualZone, settings, ptrStr, DomainName.Parse(fqdn)), ptrStrForSort));
                                                }
                                                else
                                                {
                                                    // PTR
                                                    string fqdn = record.Fqdn;

                                                    tmpList.Add(new Tuple<EasyDnsResponder.Record, string?>(new EasyDnsResponder.Record_PTR(rootVirtualZone, settings, ptrStr, DomainName.Parse(fqdn)), ptrStrForSort));
                                                }
                                            }
                                            else
                                            {
                                                // Subnet CNAME
                                                string fqdn;

                                                if (record.ReverseCName_HyphenMode == false)
                                                {
                                                    // 1.2.3.4 -> 4.3.2.1.target.domain
                                                    fqdn = IPUtil.IPAddressOrSubnetToPtrZoneOrFqdn(ip, withSuffix: false) + "." + record.Fqdn;
                                                }
                                                else
                                                {
                                                    // 1.2.3.4 -> 1-2-3-4.target.domain
                                                    fqdn = IPUtil.GenerateWildCardDnsFqdn(ip, record.Fqdn);
                                                }

                                                tmpList.Add(new Tuple<EasyDnsResponder.Record, string?>(new EasyDnsResponder.Record_CNAME(rootVirtualZone, settings, ptrStr, DomainName.Parse(fqdn)), ptrStrForSort));
                                            }

                                            // 件数が膨大であるので、戻り値リストに追加せず、ここで直ちに送信をする
                                            if (tmpList.Any())
                                            {
                                                await req.SendBufferedAsync(tmpList, cancel);
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
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
                    PtrFqdnList = new List<DomainName>(),
                    CNameFqdnList = new List<DomainName>(),
                };

                string targetLabel = req.RequestHostName;

                if (req.CallbackId.StartsWith("_this_is_custom_dynamic_record:"))
                {
                    var responder = cfg.DynamicResponderDict._GetOrDefault(req.CallbackId);
                    if (responder != null)
                    {
                        var callParam = new IpaDnsServiceDynamicResponderCallParam(ret, req);
                        if (responder.ResolveImpl(callParam) == false)
                        {
                            ret.NotFound = true;
                        }
                    }
                    else
                    {
                        throw new CoresException($"Callback ID '{req.CallbackId}' not found");
                    }
                }
                else if (req.CallbackId == "_this_is_ptr_record_refer_ReverseRecordsList")
                {
                    try
                    {
                        // 逆引きの処理 (逆引きは、すべてダイナミックレコードを用いて、Longest Match の計算をその都度自前で行なう)
                        var targetIpInfo = IPUtil.PtrZoneOrFqdnToIpAddressAndSubnet(req.RequestFqdn);
                        if (IPUtil.IsSubnetLenHostAddress(targetIpInfo.Item1.AddressFamily, targetIpInfo.Item2))
                        {
                            var longest = cfg.ReverseRadixTrie.Lookup(targetIpInfo.Item1, out _, out _);
                            if (longest != null)
                            {
                                string fqdn = longest.Fqdn;
                                if (longest.Type == StandardRecordType.ReverseSingle || longest.Type == StandardRecordType.ReverseSubnet)
                                {
                                    if (fqdn.StartsWith("*."))
                                    {
                                        string baseFqdn = fqdn.Substring(2);
                                        fqdn = IPUtil.GenerateWildCardDnsFqdn(targetIpInfo.Item1, baseFqdn, longest.FirstTokenWildcardBefore, longest.FirstTokenWildcardAfter);
                                    }
                                    ret.PtrFqdnList.Add(DomainName.Parse(fqdn));
                                    ret.Settings = longest.Settings;
                                }
                                else if (longest.Type == StandardRecordType.ReverseCNameSingle || longest.Type == StandardRecordType.ReverseCNameSubnet)
                                {
                                    if (fqdn.StartsWith("*.") == false)
                                    {
                                        string label;
                                        if (longest.ReverseCName_HyphenMode == false)
                                        {
                                            // 1.2.3.4 -> 4.3.2.1.target.domain
                                            string ipAddressLabelPart = IPUtil.IPAddressOrSubnetToPtrZoneOrFqdn(targetIpInfo.Item1, withSuffix: false);
                                            label = ipAddressLabelPart + "." + fqdn;
                                        }
                                        else
                                        {
                                            // 1.2.3.4 -> 1-2-3-4.target.domain
                                            label = IPUtil.GenerateWildCardDnsFqdn(targetIpInfo.Item1, fqdn);
                                        }
                                        ret.CNameFqdnList.Add(DomainName.Parse(label));
                                        ret.Settings = longest.Settings;
                                    }
                                }
                            }
                        }
                    }
                    catch { }
                }

                return ret;
            };

        }
        catch (Exception ex)
        {
            err.WriteLine(ex.ToString());
        }

        string errorStr = err.ToString();

        if (errorStr._IsFilled())
        {
            errorStr._Error();
        }

        //settings._ObjectToJson()._Print();
    }

    protected override DynConfig CreateInitialDynamicConfigImpl()
    {
        return new DynConfig();
    }

    public Task<string> Test_HelloWorld(int i) => $"Hello {i}"._TaskResult();
}







#endif

