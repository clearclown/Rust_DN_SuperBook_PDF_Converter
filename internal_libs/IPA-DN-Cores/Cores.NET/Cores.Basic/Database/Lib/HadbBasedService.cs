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
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using System.Net;

namespace IPA.Cores.Basic;


public static partial class CoresConfig
{
    public static partial class Hadb
    {
        public static readonly Copenhagen<int> DefaultHadbDatabaseCommandTimeoutSecs = 3 * 60;
    }
}



public class HadbBasedServiceStartupParam : INormalizable
{
    public string HiveDataName = "DefaultApp";
    public string HadbSystemName = "DEFAULT_HADB";
    public double HeavyRequestRateLimiter_LimitPerSecond = 50.0;
    public double HeavyRequestRateLimiter_Burst = 5.0;

    public virtual void Normalize() { }
}

public abstract class HadbBasedServiceHiveSettingsBase : INormalizable
{
    public string HadbSystemName = "";

    [JsonConverter(typeof(StringEnumConverter))]
    public HadbOptionFlags HadbOptionFlags = HadbOptionFlags.None;

    public int HadbDebugMaxFetchItems = 0;

    public string HadbSqlServerHostname = "";
    public int HadbSqlServerPort = 0;
    public int HadbDatabaseCommandTimeoutSecs = 0;
    public bool HadbSqlServerDisableConnectionPooling = false;

    public string HadbSqlDatabaseName = "";

    public string HadbSqlDatabaseReaderUsername = "";
    public string HadbSqlDatabaseReaderPassword = "";

    public string HadbSqlDatabaseWriterUsername = "";
    public string HadbSqlDatabaseWriterPassword = "";

    public string HadbBackupFilePathOverride = "";
    public string HadbBackupDynamicConfigFilePathOverride = "";

    public int LazyUpdateParallelQueueCount = 0;

    public List<string> DnsResolverServerIpAddressList = new List<string>();

    public virtual void NormalizeImpl() { }

    public void Normalize()
    {
        this.NormalizeImpl();

        this.HadbSystemName = this.HadbSystemName._FilledOrDefault(this.GetType().Name);

        this.HadbSqlServerHostname = this.HadbSqlServerHostname._FilledOrDefault("127.0.0.1"); ;

        if (this.HadbSqlServerPort <= 0) this.HadbSqlServerPort = Consts.Ports.MsSqlServer;

        if (this.HadbDatabaseCommandTimeoutSecs <= 0) this.HadbDatabaseCommandTimeoutSecs = CoresConfig.Hadb.DefaultHadbDatabaseCommandTimeoutSecs;

        this.HadbSqlDatabaseName = this.HadbSqlDatabaseName._FilledOrDefault("HADB001");

        this.HadbSqlDatabaseReaderUsername = this.HadbSqlDatabaseReaderUsername._FilledOrDefault("sql_hadb001_reader");
        this.HadbSqlDatabaseReaderPassword = this.HadbSqlDatabaseReaderPassword._FilledOrDefault("sql_hadb_reader_default_password");

        this.HadbSqlDatabaseWriterUsername = this.HadbSqlDatabaseWriterUsername._FilledOrDefault("sql_hadb001_writer");
        this.HadbSqlDatabaseWriterPassword = this.HadbSqlDatabaseWriterPassword._FilledOrDefault("sql_hadb_writer_default_password");

        if (this.DnsResolverServerIpAddressList == null || DnsResolverServerIpAddressList.Any() == false)
        {
            this.DnsResolverServerIpAddressList = new List<string>();

            this.DnsResolverServerIpAddressList.Add("8.8.8.8");
            this.DnsResolverServerIpAddressList.Add("1.1.1.1");
        }

        if (this.LazyUpdateParallelQueueCount <= 0) this.LazyUpdateParallelQueueCount = 32;
        if (this.LazyUpdateParallelQueueCount > 256) this.LazyUpdateParallelQueueCount = Consts.Numbers.HadbMaxLazyUpdateParallelQueueCount;
    }
}

public class HadbBasedService_BasicLogBasedQuota : HadbLog
{
    public string QuotaName = "";
    public string MatchKey = "";

    public override void Normalize()
    {
        QuotaName = QuotaName._NormalizeKey(true);
        MatchKey = MatchKey._NormalizeKey(true);
    }

    public override HadbLabels GetLabels()
        => new HadbLabels(this.QuotaName, this.MatchKey);
}

public abstract class HadbBasedServiceMemDb : HadbMemDataBase
{
    protected abstract void AddDefinedUserDataTypesImpl(List<Type> ret);
    protected abstract void AddDefinedUserLogTypesImpl(List<Type> ret);

    protected sealed override List<Type> GetDefinedUserDataTypesImpl()
    {
        List<Type> ret = new List<Type>();

        this.AddDefinedUserDataTypesImpl(ret);

        return ret;
    }

    protected sealed override List<Type> GetDefinedUserLogTypesImpl()
    {
        List<Type> ret = new List<Type>();

        ret.Add(typeof(HadbBasedService_BasicLogBasedQuota));

        this.AddDefinedUserLogTypesImpl(ret);

        return ret;
    }
}

public class HadbBasedServiceDynConfig : HadbDynamicConfig
{
    [SimpleComment("Set true to hide error details (e.g. source code call stacks and filenames) in error details")]
    public bool Service_HideJsonRpcErrorDetails = false;

    [SimpleComment("Administration config pages username for HTTP basic authentication")]
    public string Service_AdminBasicAuthUsername = "";

    [SimpleComment("Administration config pages password for HTTP basic authentication")]
    public string Service_AdminBasicAuthPassword = "";

    [SimpleComment("IP address ACL rule to exempt heavy request rate limit (You can specify multiple items. e.g. 127.0.0.0/8,1.2.3.0/24)")]
    public string Service_HeavyRequestRateLimiterExemptAcl = "_initial_";

    [SimpleComment("IP address ACL rule to allow accessing to the administration config pages (You can specify multiple items. e.g. 127.0.0.0/8,1.2.3.0/24)")]
    public string Service_AdminPageAcl = "_initial_";

    [SimpleComment("Client IP address subnet length for rate limit calculation (IPv4)")]
    public int Service_ClientIpRateLimit_SubnetLength_IPv4 = 0;

    [SimpleComment("Client IP address subnet length for rate limit calculation (IPv6)")]
    public int Service_ClientIpRateLimit_SubnetLength_IPv6 = 0;

    [SimpleComment("SMTP server hostname for email submission")]
    public string Service_SendMail_SmtpServer_Hostname = "";

    [SimpleComment("SMTP server TCP port number for email submission")]
    public int Service_SendMail_SmtpServer_Port = 0;

    [SimpleComment("Set true to enable SSL when connecting to the SMTP server")]
    public bool Service_SendMail_SmtpServer_EnableSsl;

    [SimpleComment("SMTP server user authentication username")]
    public string Service_SendMail_SmtpServer_Username = "";

    [SimpleComment("SMTP server user authentication password")]
    public string Service_SendMail_SmtpServer_Password = "";

    [SimpleComment("SMTP server email sending 'From:' address")]
    public string Service_SendMail_MailFromAddress = "";

    [SimpleComment("Duration seconds for basic rate limit quota per IP address exact match")]
    public int Service_BasicQuota_PerClientIpExact_DurationSecs = 3600;

    [SimpleComment("Limitation count for basic rate limit quota per IP address exact match per duration")]
    public int Service_BasicQuota_PerClientIpExact_LimitationCount = 10;

    [SimpleComment("Duration seconds for basic rate limit quota per IP subnet exact match")]
    public int Service_BasicQuota_PerClientIpSubnet_DurationSecs = 3600;

    [SimpleComment("Limitation count for basic rate limit quota per IP subnet exact match per duration")]
    public int Service_BasicQuota_PerClientIpSubnet_LimitationCount = 300;

    [SimpleComment("Full text search results count limitation (hard limit)")]
    public int Service_FullTextSearchResultsCountMax = 0;

    [SimpleComment("Full text search results count limitation (default limit)")]
    public int Service_FullTextSearchResultsCountStandard = 0;

    [SimpleComment("Full text search results count limitation (internal limitation)")]
    public int Service_FullTextSearchResultsCountInternalMemory = 0;

    [SimpleComment("Service frieldly name")]
    public string Service_FriendlyName = "";

    [SimpleComment("Set true to disallow cross site requesting")]
    public bool Service_Security_ProhibitCrossSiteRequest = true;

    [SimpleComment("Cookie domain name")]
    public string Service_CookieDomainName = "";

    [SimpleComment("Cookie encryption password")]
    public string Service_CookieEncryptPassword = Consts.Strings.EasyEncryptDefaultPassword;


    protected override void NormalizeImpl()
    {
        Service_AdminBasicAuthUsername = Service_AdminBasicAuthUsername._FilledOrDefault(Consts.Strings.DefaultAdminUsername);
        Service_AdminBasicAuthPassword = Service_AdminBasicAuthPassword._FilledOrDefault(Consts.Strings.DefaultAdminPassword);

        if (Service_ClientIpRateLimit_SubnetLength_IPv4 <= 0 || Service_ClientIpRateLimit_SubnetLength_IPv4 > 32) Service_ClientIpRateLimit_SubnetLength_IPv4 = 24;
        if (Service_ClientIpRateLimit_SubnetLength_IPv6 <= 0 || Service_ClientIpRateLimit_SubnetLength_IPv6 > 128) Service_ClientIpRateLimit_SubnetLength_IPv6 = 56;

        if (Service_AdminPageAcl == "_initial_")
        {
            this.Service_AdminPageAcl = "0.0.0.0/0; ::/0";
        }

        if (Service_HeavyRequestRateLimiterExemptAcl == "_initial_")
        {
            Service_HeavyRequestRateLimiterExemptAcl = "127.0.0.0/8; 192.168.0.0/16; 172.16.0.0/12; 10.0.0.0/8; 1.2.3.4/32; 2041:af80:1234::/48";
        }

        if (Service_SendMail_SmtpServer_Port <= 0) Service_SendMail_SmtpServer_Port = Consts.Ports.Smtp;
        Service_SendMail_SmtpServer_Hostname = Service_SendMail_SmtpServer_Hostname._FilledOrDefault("your-smtp-server-address.your_company.org");

        Service_SendMail_MailFromAddress = Service_SendMail_MailFromAddress._FilledOrDefault("noreply@your_company.org");

        if (Service_BasicQuota_PerClientIpExact_DurationSecs <= 0) Service_BasicQuota_PerClientIpExact_DurationSecs = 3600;
        if (Service_BasicQuota_PerClientIpExact_LimitationCount <= 0) Service_BasicQuota_PerClientIpExact_LimitationCount = 10;

        if (Service_BasicQuota_PerClientIpSubnet_DurationSecs <= 0) Service_BasicQuota_PerClientIpSubnet_DurationSecs = 3600;
        if (Service_BasicQuota_PerClientIpSubnet_LimitationCount <= 0) Service_BasicQuota_PerClientIpSubnet_LimitationCount = 300;

        if (this.Service_FullTextSearchResultsCountMax <= 0) Service_FullTextSearchResultsCountMax = Consts.Numbers.HadbFullTextSearchResultsMaxDefault;
        if (this.Service_FullTextSearchResultsCountStandard <= 0) Service_FullTextSearchResultsCountStandard = Consts.Numbers.HadbFullTextSearchResultsStandardDefault;
        if (this.Service_FullTextSearchResultsCountInternalMemory <= 0) Service_FullTextSearchResultsCountInternalMemory = Consts.Numbers.HadbFullTextSearchResultsInternalMemoryDefault;

        if (this.Service_FriendlyName._IsEmpty()) this.Service_FriendlyName = Env.ApplicationNameSupposed;

        this.Service_CookieDomainName = this.Service_CookieDomainName._NormalizeFqdn();
        if (this.Service_CookieEncryptPassword._IsNullOrZeroLen()) this.Service_CookieEncryptPassword = Consts.Strings.EasyEncryptDefaultPassword;

        base.NormalizeImpl();
    }
}

public abstract class JsonRpcSingleReturnWithMetaData<T> : IToJsonString
    where T : HadbData
{
    public abstract T Data { get; }

    public string ToJsonString(bool includeNull = false, bool escapeHtml = false, int? maxDepth = 8, bool compact = false, bool referenceHandling = false, bool base64url = false, Type? type = null, JsonFlags jsonFlags = JsonFlags.None)
    {
        string tmp = this.Data._ObjectToJson(includeNull, escapeHtml, maxDepth, compact, referenceHandling, base64url, type);

        JObject jobject = tmp._JsonToObject<JObject>(includeNull, maxDepth, base64url)!;

        var rw = FieldReaderWriter.GetCached(this.GetType());

        JsonSerializerSettings setting = new JsonSerializerSettings()
        {
            MaxDepth = maxDepth,
            NullValueHandling = includeNull ? NullValueHandling.Include : NullValueHandling.Ignore,
            ReferenceLoopHandling = ReferenceLoopHandling.Error,
            PreserveReferencesHandling = referenceHandling ? PreserveReferencesHandling.All : PreserveReferencesHandling.None,
            StringEscapeHandling = escapeHtml ? StringEscapeHandling.EscapeHtml : StringEscapeHandling.Default,
            Formatting = compact ? Formatting.None : Formatting.Indented,
        };

        Json.AddStandardSettingsToJsonConverter(setting, jsonFlags, null);

        setting.Converters.Add(new StringEnumConverter());

        JsonSerializer serializer = JsonSerializer.Create(setting);

        var dataProperties = jobject.Properties().ToList();
        foreach (var p in dataProperties)
        {
            p.Remove();
        }

        foreach (var name in rw.FieldOrPropertyNamesList.Where(x => x._IsDiff(nameof(Data))))
        {
            object? obj = rw.GetValue(this, name);

            var jtoken = JToken.FromObject(obj!, serializer);

            jobject.Add(name, jtoken);
        }

        foreach (var p in dataProperties)
        {
            jobject.Add(p);
        }

        return jobject._ObjectToJson(includeNull, escapeHtml, maxDepth, compact, referenceHandling, base64url);
    }
}

public abstract class HadbBasedServiceHookBase
{
}

public class HadbFullTextSearchResult
{
    public int NumResultObjects;
    public int NumTotalObjects;
    public bool HasMore;
    public double TookTime_Secs;
    public List<HadbSearchResultJsonObject> ObjectsList = new List<HadbSearchResultJsonObject>();
}

[RpcInterface]
public interface IHadbBasedServiceRpcBase
{
    [RpcRequireAuth]
    [RpcMethodHelp("HADB システムの現在の内部統計情報を表示します。")]
    public Task<EasyJsonStrAttributes> ServiceAdmin_GetStatCurrent();

    [RpcRequireAuth]
    [RpcMethodHelp("この HADB システムの過去の内部統計情報の履歴を表示します。")]
    public Task<IEnumerable<HadbStat>> ServiceAdmin_GetStatHistory(int maxCount = Consts.Numbers.HadbHistoryMaxCount);

    [RpcRequireAuth]
    [RpcMethodHelp("この HADB システムに存在するデータベースオブジェクトに対してフルテキスト検索を実施します。")]
    public Task<HadbFullTextSearchResult> ServiceAdmin_FullTextSearch(
        [RpcParamHelp("検索文字列", "hello AND world")]
        string queryText = "",
        [RpcParamHelp("ソートする変数名", "Age")]
        string sortBy = "",
        [RpcParamHelp("単語単位で検索", false)]
        bool wordMode = false,
        [RpcParamHelp("変数名を含めた検索モード", false)]
        bool fieldNameMode = false,
        [RpcParamHelp("対象型名", "User")]
        string typeName = "",
        [RpcParamHelp("対象名前空間", "NS_TEST1")]
        string nameSpace = "",
        [RpcParamHelp("最大検索個数", 100)]
        int maxResults = 0);
}

[Flags]
public enum HadbObjectSetFlag
{
    New = 1,
    Update = 2,
    Delete = 4,
}

[Flags]
public enum HadbObjectGetExFlag
{
    None = 0,
    WithArchive = 1,
}


public interface IHadbBasedServicePoint
{
    public Task Basic_Require_AdminBasicAuthAsync(string realm = "");

    public HadbBasedServiceDynConfig? AdminForm_GetCurrentDynamicConfig();

    public Task<string> AdminForm_GetDynamicConfigTextAsync(CancellationToken cancel = default);
    public Task AdminForm_SetDynamicConfigTextAsync(string newConfig, CancellationToken cancel = default);

    public Task<HadbObject?> AdminForm_DirectGetObjectAsync(string uid, CancellationToken cancel = default);
    public Task<HadbObject> AdminForm_DirectSetObjectAsync(string uid, string jsonData, HadbObjectSetFlag flag, string typeName, string nameSpace, CancellationToken cancel = default);
    public Task<string> AdminForm_DirectGetObjectExAsync(string uid, int maxItems = int.MaxValue, HadbObjectGetExFlag flag = HadbObjectGetExFlag.None, CancellationToken cancel = default);

    public Task<HadbFullTextSearchResult> ServiceAdmin_FullTextSearch(string queryText, string sortBy, bool wordMode, bool fieldNameMode, string typeName, string nameSpace, int maxResults);

    public Task<bool> AdminForm_AdminPasswordAuthAsync(string username, string password, CancellationToken cancel = default);

    public Task<OkOrExeption> HealthCheck_GetCurrentHealthStatus(CancellationToken cancel = default);

    public string AdminForm_GetWebFormSecretKey();

    public StrTableLanguageList LanguageList { get; }
    public HadbBasedServiceHiveSettingsBase SettingsFastSnapshotBase { get; }
}


public abstract class HadbBasedSimpleServiceBase<TMemDb, TDynConfig, THiveSettings, THook> : HadbBasedServiceBase<TMemDb, TDynConfig, THiveSettings, THook>
    where TMemDb : HadbBasedServiceMemDb, new()
    where TDynConfig : HadbBasedServiceDynConfig, new()
    where THiveSettings : HadbBasedServiceHiveSettingsBase, new()
    where THook : HadbBasedServiceHookBase
{
    protected HadbBasedSimpleServiceBase(HadbBasedServiceStartupParam startupParam, THook hook) : base(startupParam, hook)
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

    public class HadbSimpleSys : HadbSimpleBase<TMemDb, TDynConfig>
    {
        public HadbSimpleSys(HadbSimpleSettings settings, TDynConfig dynamicConfig) : base(settings, dynamicConfig) { }
    }

    protected override HadbBase<TMemDb, TDynConfig> CreateHadb()
    {
        var s = this.SettingsFastSnapshot;

        HadbSimpleSettings simpleSettings = new HadbSimpleSettings(s.HadbSystemName, optionFlags: s.HadbOptionFlags);

        return new HadbSimpleSys(simpleSettings, CreateInitialDynamicConfigImpl());
    }
}

public abstract class HadbBasedServiceBase<TMemDb, TDynConfig, THiveSettings, THook> : AsyncService, IHadbBasedServiceRpcBase, IHadbBasedServicePoint
where TMemDb : HadbBasedServiceMemDb, new()
where TDynConfig : HadbBasedServiceDynConfig, new()
where THiveSettings : HadbBasedServiceHiveSettingsBase, new()
where THook : HadbBasedServiceHookBase
{
    public DateTimeOffset BootDateTime { get; } = DtOffsetNow; // サービス起動日時
    public HadbBase<TMemDb, TDynConfig> Hadb { get; }
    public AsyncEventListenerList<HadbBase<TMemDb, TDynConfig>, HadbEventType> HadbEventListenerList => this.Hadb.EventListenerList;

    HadbBasedServiceStartupParam ServiceStartupParam { get; } // copy

    public DnsResolver DnsResolver { get; }

    // Hive
    readonly HiveData<THiveSettings> _SettingsHive;

    // Hive 設定へのアクセスを容易にするための自動プロパティ
    CriticalSection ManagedSettingsLock => this._SettingsHive.DataLock;
    THiveSettings ManagedSettings => this._SettingsHive.ManagedData;
    public THiveSettings SettingsFastSnapshot => this._SettingsHive.CachedFastSnapshot;
    public HadbBasedServiceHiveSettingsBase SettingsFastSnapshotBase => this.SettingsFastSnapshot;

    public RateLimiter<string> HeavyRequestRateLimiter { get; }
    public TDynConfig CurrentDynamicConfig => Hadb.CurrentDynamicConfig;

    public THook Hook { get; }

    public StrTableLanguageList LanguageList { get; private set; } = null!;

    protected HadbBasedServiceBase(HadbBasedServiceStartupParam startupParam, THook hook)
    {
        try
        {
            this.Hook = hook;

            this.ServiceStartupParam = startupParam._CloneDeep();

            this.HeavyRequestRateLimiter = new RateLimiter<string>(new RateLimiterOptions(burst: this.ServiceStartupParam.HeavyRequestRateLimiter_Burst, limitPerSecond: this.ServiceStartupParam.HeavyRequestRateLimiter_LimitPerSecond, mode: RateLimiterMode.NoPenalty));

            this._SettingsHive = new HiveData<THiveSettings>(Hive.SharedLocalConfigHive,
                this.ServiceStartupParam.HiveDataName,
                () => new THiveSettings { HadbSystemName = this.ServiceStartupParam.HadbSystemName },
                HiveSyncPolicy.AutoReadWriteFile,
                HiveSerializerSelection.RichJson);

            this.DnsResolver = new DnsClientLibBasedDnsResolver(new DnsResolverSettings(flags: DnsResolverFlags.RoundRobinServers,
                dnsServersList: this.SettingsFastSnapshot.DnsResolverServerIpAddressList.Select(x => x._ToIPEndPoint(Consts.Ports.Dns, noExceptionAndReturnNull: true))
                .Where(x => x != null)!));

            this.Hadb = this.CreateHadb();
        }
        catch
        {
            this._DisposeSafe();
            throw;
        }
    }


    public class HadbSys : HadbSqlBase<TMemDb, TDynConfig>
    {
        public HadbSys(HadbSqlSettings settings, TDynConfig dynamicConfig) : base(settings, dynamicConfig) { }
    }

    protected abstract TDynConfig CreateInitialDynamicConfigImpl();

    protected virtual HadbBase<TMemDb, TDynConfig> CreateHadb()
    {
        var s = this.SettingsFastSnapshot;

        HadbSqlSettings sqlSettings = new HadbSqlSettings(
            s.HadbSystemName,
            new SqlDatabaseConnectionSetting(s.HadbSqlServerHostname, s.HadbSqlDatabaseName, s.HadbSqlDatabaseReaderUsername, s.HadbSqlDatabaseReaderPassword, !s.HadbSqlServerDisableConnectionPooling, s.HadbSqlServerPort),
            new SqlDatabaseConnectionSetting(s.HadbSqlServerHostname, s.HadbSqlDatabaseName, s.HadbSqlDatabaseWriterUsername, s.HadbSqlDatabaseWriterPassword, !s.HadbSqlServerDisableConnectionPooling, s.HadbSqlServerPort),
            optionFlags: s.HadbOptionFlags,
            backupDataFile: s.HadbBackupFilePathOverride._IsFilled() ? new FilePath(s.HadbBackupFilePathOverride) : null,
            backupDynamicConfigFile: s.HadbBackupDynamicConfigFilePathOverride._IsFilled() ? new FilePath(s.HadbBackupDynamicConfigFilePathOverride) : null,
            lazyUpdateParallelQueueCount: s.LazyUpdateParallelQueueCount,
            commandTimeoutSecs: s.HadbDatabaseCommandTimeoutSecs,
            debugMaxFetchItems: s.HadbDebugMaxFetchItems
            );

        return new HadbSys(sqlSettings, CreateInitialDynamicConfigImpl());
    }

    Once StartedFlag;

    protected abstract void StartImpl();
    protected abstract Task StopImplAsync(Exception? ex);
    protected abstract Task<OkOrExeption> HealthCheckImplAsync(CancellationToken cancel);

    protected JsonRpcClientInfo GetClientInfo() => JsonRpcServerApi.GetCurrentRpcClientInfo();
    protected IPAddress GetClientIpAddress() => GetClientInfo().RemoteIP._ToIPAddress()!._RemoveScopeId();
    protected IPAddress GetClientIpNetworkForRateLimit() => IPUtil.NormalizeIpNetworkAddressIPv4v6(GetClientIpAddress(), Hadb.CurrentDynamicConfig.Service_ClientIpRateLimit_SubnetLength_IPv4, Hadb.CurrentDynamicConfig.Service_ClientIpRateLimit_SubnetLength_IPv6);

    protected string GetClientIpStr() => GetClientIpAddress().ToString();
    protected string GetClientIpNetworkForRateLimitStr() => GetClientIpNetworkForRateLimit().ToString();

    protected Task<string> GetClientFqdnAsync(CancellationToken cancel = default, bool noCache = false)
        => this.DnsResolver.GetHostNameOrIpAsync(GetClientIpAddress(), cancel, noCache);

    public void SetLanguageList(StrTableLanguageList list)
    {
        this.LanguageList = list;
    }

    public HadbBasedServiceDynConfig? AdminForm_GetCurrentDynamicConfig()
    {
        if (this.Hadb.IsDynamicConfigInited)
        {
            return this.CurrentDynamicConfig;
        }
        else
        {
            return null;
        }
    }

    public async Task<HadbObject?> AdminForm_DirectGetObjectAsync(string uid, CancellationToken cancel = default)
        => await this.Hadb.DirectGetObjectAsync(uid, cancel);

    public async Task<HadbObject> AdminForm_DirectSetObjectAsync(string uid, string jsonData, HadbObjectSetFlag flag, string typeName, string nameSpace, CancellationToken cancel = default)
        => await this.Hadb.DirectSetObjectAsync(uid, jsonData, flag, typeName, nameSpace, cancel);

    public async Task<string> AdminForm_DirectGetObjectExAsync(string uid, int maxItems = int.MaxValue, HadbObjectGetExFlag flag = HadbObjectGetExFlag.None, CancellationToken cancel = default)
        => await this.Hadb.AdminForm_DirectGetObjectExAsync(uid, maxItems, flag, cancel);

    public async Task<string> AdminForm_GetDynamicConfigTextAsync(CancellationToken cancel = default)
    {
        return await this.Hadb.GetDynamicConfigStringAsync(cancel);
    }

    public string AdminForm_GetWebFormSecretKey()
    {
        string tmp = this.CurrentDynamicConfig.Service_AdminBasicAuthUsername + "---@---" + this.CurrentDynamicConfig.Service_AdminBasicAuthPassword;

        return tmp._HashSHA256()._GetHexString();
    }

    public async Task<bool> AdminForm_AdminPasswordAuthAsync(string username, string password, CancellationToken cancel = default)
    {
        if (this.CurrentDynamicConfig.Service_AdminBasicAuthUsername._IsSamei(username))
        {
            if (this.CurrentDynamicConfig.Service_AdminBasicAuthPassword._IsSame(password))
            {
                return true;
            }
        }

        await Task.CompletedTask;

        return false;
    }

    public async Task<IEnumerable<HadbStat>> ServiceAdmin_GetStatHistory(int maxCount)
    {
        await this.Basic_Require_AdminBasicAuthAsync();

        IEnumerable<HadbStat> ret = null!;
        await this.Hadb.TranAsync(false, async tran =>
        {
            ret = await tran.EnumStatAsync(default, default, maxCount);
            return false;
        });
        return ret;
    }

    public async Task AdminForm_SetDynamicConfigTextAsync(string newConfig, CancellationToken cancel = default)
    {
        await this.Hadb.SetDynamincConfigStringAsync(newConfig, cancel);
    }

    public Task Basic_Require_AdminBasicAuthAsync(string realm = "")
    {
        if (realm._IsEmpty())
        {
            realm = "Basic auth for " + this.GetType().Name;
        }

        var config = Hadb.CurrentDynamicConfig;

        // クライアント IP アドレスに基づく ACL 認証
        if (config.Service_AdminPageAcl._IsFilled())
        {
            var clientInfo = JsonRpcServerApi.GetCurrentRpcClientInfo();

            if (EasyIpAcl.Evaluate(config.Service_AdminPageAcl, clientInfo.RemoteIP, enableCache: true, permitLocalHost: true) != EasyIpAclAction.Permit)
            {
                throw new CoresException($"Client IP address '{clientInfo.RemoteIP.ToString()}' is not allowed to access to the administration page by the server ACL settings.");
            }
        }

        // ユーザー認証
        JsonRpcServerApi.TryAuth((user, pass) =>
        {
            return user._IsSamei(config.Service_AdminBasicAuthUsername) && pass._IsSame(config.Service_AdminBasicAuthPassword);
        }, realm);

        return Task.CompletedTask;
    }

    protected void Basic_Check_HeavyRequestRateLimiter(double amount = 1.0)
    {
        if (EasyIpAcl.Evaluate(this.Hadb.CurrentDynamicConfig.Service_HeavyRequestRateLimiterExemptAcl, this.GetClientIpAddress(), EasyIpAclAction.Deny, EasyIpAclAction.Deny, true) == EasyIpAclAction.Deny)
        {
            if (this.HeavyRequestRateLimiter.TryInput(this.GetClientIpNetworkForRateLimitStr(), out var e, amount) == false)
            {
                throw new CoresException($"Request rate limit exceeded with your IP address {this.GetClientIpAddress()} and your request is rejected. Too many requests from your IP address or your network. Please wait for minutes. If this issue remains, please concact to the server administrator.");
            }
        }
    }

    public async Task Basic_CheckAndAddLogBasedQuotaByClientIpAndSubnetEasyAsync(string quotaName, CancellationToken cancel = default, object? reentrantTran = null)
    {
        await Basic_CheckAndAddLogBasedQuotaByClientIpOrSubnetAsync(quotaName + "@by_ip", subnetMode: false, cancel: cancel, reentrantTran: reentrantTran);
        await Basic_CheckAndAddLogBasedQuotaByClientIpOrSubnetAsync(quotaName + "@by_subnet", subnetMode: true, cancel: cancel, reentrantTran: reentrantTran);
    }

    public async Task Basic_CheckAndAddLogBasedQuotaByClientIpOrSubnetAsync(string quotaName, int? allowedMax = null, int? durationSecs = null, bool subnetMode = false, CancellationToken cancel = default, object? reentrantTran = null)
    {
        string key;

        var ip = this.GetClientIpAddress();

        if (subnetMode == false)
        {
            key = ip.ToString();
        }
        else
        {
            key = this.GetClientIpNetworkForRateLimitStr();
        }

        if (EasyIpAcl.Evaluate(this.Hadb.CurrentDynamicConfig.Service_HeavyRequestRateLimiterExemptAcl, ip, EasyIpAclAction.Deny, EasyIpAclAction.Deny, true) == EasyIpAclAction.Deny)
        {
            await Basic_CheckAndAddLogBasedQuotaAsync(quotaName, key, allowedMax, durationSecs, subnetMode, cancel, reentrantTran);
        }
    }

    public async Task Basic_CheckAndAddLogBasedQuotaAsync(string quotaName, string matchKey, int? allowedMax = null, int? durationSecs = null, bool subnetMode = false, CancellationToken cancel = default, object? reentrantTran = null)
    {
        if (subnetMode == false)
        {
            allowedMax ??= CurrentDynamicConfig.Service_BasicQuota_PerClientIpExact_LimitationCount;
            durationSecs ??= CurrentDynamicConfig.Service_BasicQuota_PerClientIpExact_DurationSecs;
        }
        else
        {
            allowedMax ??= CurrentDynamicConfig.Service_BasicQuota_PerClientIpSubnet_LimitationCount;
            durationSecs ??= CurrentDynamicConfig.Service_BasicQuota_PerClientIpSubnet_DurationSecs;
        }

        string quotaName2 = quotaName._NormalizeKey(true);
        matchKey = matchKey._NormalizeKey(true);

        if (allowedMax <= 0 || quotaName2._IsEmpty() || matchKey._IsEmpty()) return;

        bool ok = true;

        await this.Hadb.TranAsync(false, async tran =>
        {
            HadbLogQuery query = new HadbLogQuery
            {
                SearchTemplate = new HadbBasedService_BasicLogBasedQuota { QuotaName = quotaName2, MatchKey = matchKey },
                TimeStart = DtOffsetNow.AddSeconds(-durationSecs.Value),
                RetOnlyCount = true,
                Flags = HadbLogQueryFlags.UseIndexTime | HadbLogQueryFlags.WithNoLock,
            };

            var logs = await tran.AtomicSearchLogAsync<HadbBasedService_BasicLogBasedQuota>(query, cancel: cancel);
            if (logs.Count >= allowedMax)
            {
                ok = false;
            }

            return false;
        }, reentrantTran: reentrantTran);

        if (ok == false)
        {
            throw new CoresException($"Request quota exceeded ({quotaName}). Please wait for minutes and try again. If problem remains please contact to the service administrator.");
        }

        await this.Hadb.TranAsync(true, async tran =>
        {
            await tran.AtomicAddLogAsync(new HadbBasedService_BasicLogBasedQuota { QuotaName = quotaName2, MatchKey = matchKey }, cancel: cancel);

            return true;
        }, options: HadbTranOptions.NoTransactionOnWrite, reentrantTran: reentrantTran);
    }

    public async Task Basic_SendMailAsync(string to, string subject, string body, string? from = null, CancellationToken cancel = default)
    {
        if (from._IsEmpty())
        {
            from = CurrentDynamicConfig.Service_SendMail_MailFromAddress;
        }

        if (from._IsEmpty())
        {
            from = "noreply@example.org";
        }

        to = to._NonNull();
        subject = subject._NonNull();
        body = body._NonNull();

        var smtpConfig = new SmtpConfig(CurrentDynamicConfig.Service_SendMail_SmtpServer_Hostname,
            CurrentDynamicConfig.Service_SendMail_SmtpServer_Port,
            CurrentDynamicConfig.Service_SendMail_SmtpServer_EnableSsl,
            CurrentDynamicConfig.Service_SendMail_SmtpServer_Username,
            CurrentDynamicConfig.Service_SendMail_SmtpServer_Password);

        await SmtpUtil.SendAsync(smtpConfig, from, to, subject, body, true, cancel);
    }

    public void Start()
    {
        StartedFlag.FirstCallOrThrowException();

        StartImpl(); // HADB の開始前でなければならない！！

        // HADB からの特殊なコールバック
        this.HadbEventListenerList.RegisterCallback(async (caller, type, state, param) =>
        {
            switch (type)
            {
                case HadbEventType.DynamicConfigChanged: // DynamicConfig の設定が変更された
                    break;
            }

            if (this.Hadb.IsDynamicConfigInited)
            {
                // JSON-RPC サーバーの詳細エラーを隠すかどうかのフラグ
                JsonRpcHttpServer.HideErrorDetails = this.Hadb.CurrentDynamicConfig.Service_HideJsonRpcErrorDetails;
            }

            await Task.CompletedTask;
        });

        this.Hadb.Start();
    }

    protected override async Task CleanupImplAsync(Exception? ex)
    {
        try
        {
            try
            {
                // HADB の終了前でなければならない！！
                await this.StopImplAsync(ex);
            }
            catch (Exception ex2)
            {
                ex2._Error();
            }

            await this.Hadb._DisposeSafeAsync(ex);

            await this._SettingsHive._DisposeSafeAsync2();

            await this.DnsResolver._DisposeSafeAsync();
        }
        finally
        {
            await base.CleanupImplAsync(ex);
        }
    }

    public async Task<EasyJsonStrAttributes> ServiceAdmin_GetStatCurrent()
    {
        await this.Basic_Require_AdminBasicAuthAsync();

        var stat = this.Hadb.LatestStatData!;

        stat._CloneDeep();

        // リアルタイム統計 (システム状態) の更新
        this.Hadb.UpdateSystemRelatimeStat(stat);

        return stat;
    }

    public async Task<OkOrExeption> HealthCheck_GetCurrentHealthStatus(CancellationToken cancel = default)
    {
        return await this.HealthCheckImplAsync(cancel);
    }

    public async Task<HadbFullTextSearchResult> ServiceAdmin_FullTextSearch(string queryText, string sortBy, bool wordMode, bool fieldNameMode, string typeName, string nameSpace, int maxResults)
    {
        await this.Basic_Require_AdminBasicAuthAsync();

        if (maxResults <= 0 || maxResults == int.MaxValue)
        {
            maxResults = Hadb.CurrentDynamicConfig.Service_FullTextSearchResultsCountStandard;
        }
        else
        {
            if (maxResults > Hadb.CurrentDynamicConfig.Service_FullTextSearchResultsCountMax)
            {
                throw new CoresException($"FullTextSearch: {nameof(maxResults)} must be equal or less than {Hadb.CurrentDynamicConfig.Service_FullTextSearchResultsCountMax}.");
            }
        }

        RefBool hasMore = new RefBool();

        long startTick = Time.NowHighResLong100Usecs;
        var objList = Hadb.FastSearchByFullText(queryText, sortBy, wordMode, fieldNameMode, typeName, nameSpace, maxResults, Hadb.CurrentDynamicConfig.Service_FullTextSearchResultsCountInternalMemory, hasMore: hasMore);
        long endTick = Time.NowHighResLong100Usecs;

        double tookTime = ((endTick - startTick) / 10) / 1000000.0;

        var metrics = Hadb.GetCurrentMetrics();

        HadbFullTextSearchResult ret = new HadbFullTextSearchResult
        {
            NumTotalObjects = metrics.NumMemoryObjects,
            NumResultObjects = objList.Count,
            HasMore = hasMore,
            TookTime_Secs = tookTime,
        };

        foreach (var obj in objList)
        {
            ret.ObjectsList.Add(new HadbSearchResultJsonObject(obj));
        }

        return ret;
    }
}














#endif

