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
// Snmp Work Daemon Util Main

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

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

namespace IPA.Cores.Basic;

[Serializable]
[DataContract]
public class SnmpWorkSensorSetting
{
    [DataMember]
    public string SensorName = "";

    [DataMember]
    public string SensorTitle = "";

    [DataMember]
    public string SensorArguments = "";
}

[Serializable]
[DataContract]
public class SnmpWorkSettings : INormalizable
{
    [DataMember]
    public string PingTargets = "";

    [DataMember]
    public string SpeedTargets = "";

    [DataMember]
    public int SpeedIntervalsSec = 0;

    [DataMember]
    public int SpeedSpanSec = 0;

    [DataMember]
    public int SpeedTryCount = 0;

    [DataMember]
    public int PollingIntervalsSec = 0;

    [DataMember]
    public int PktLossTryCount = 0;

    [DataMember]
    public int PktLossIntervalMsec = 0;

    [DataMember]
    public int PktLossTimeoutMsecs = 0;

    [DataMember]
    public int HttpPort = 0;

    [DataMember]
    public int PingNumTry = 0;

    [DataMember]
    public bool HopsToTTL = false;

    [DataMember]
    public List<SnmpWorkSensorSetting> SensorList = new List<SnmpWorkSensorSetting>();

    public void Normalize()
    {
        if (PingTargets._IsEmpty()) PingTargets = SnmpWorkConfig.DefaultPingTarget;
        if (SpeedTargets._IsEmpty()) SpeedTargets = SnmpWorkConfig.DefaultSpeedTarget;
        if (SpeedIntervalsSec <= 0) SpeedIntervalsSec = SnmpWorkConfig.DefaultSpeedIntervalSecs;
        if (PollingIntervalsSec <= 0) PollingIntervalsSec = SnmpWorkConfig.DefaultPollingIntervalSecs;
        if (SpeedSpanSec <= 0) SpeedSpanSec = SnmpWorkConfig.DefaultSpeedSpanSecs;
        if (SpeedTryCount <= 0) SpeedTryCount = SnmpWorkConfig.DefaultSpeedTryCount;
        if (PktLossTryCount <= 0) PktLossTryCount = SnmpWorkConfig.DefaultPktLossTryCount;
        if (PktLossTimeoutMsecs <= 0) PktLossTimeoutMsecs = SnmpWorkConfig.DefaultPktLossTimeoutMsecs;
        if (PktLossIntervalMsec <= 0) PktLossIntervalMsec = SnmpWorkConfig.DefaultPktLossIntervalMsec;
        if (HttpPort <= 0) HttpPort = Consts.Ports.SnmpWorkHttp;
        if (PingNumTry <= 0) PingNumTry = SnmpWorkConfig.DefaultPingNumTry;
        if (SensorList == null || SensorList.Count == 0)
        {
            SensorList = new List<SnmpWorkSensorSetting>();
            SensorList.Add(new SnmpWorkSensorSetting { SensorName = Consts.Strings.Sample, SensorArguments = "", SensorTitle = Consts.Strings.Sample });
        }
    }
}

// 内部データ
public class SnmpWorkInternalDb : INormalizable
{
    public Dictionary<string, int> IndexTable = new Dictionary<string, int>();

    public void Normalize()
    {
        if (this.IndexTable == null) this.IndexTable = new Dictionary<string, int>();
    }
}

public static partial class SnmpWorkConfig
{
    public static readonly Copenhagen<int> DefaultPollingIntervalSecs = 60;
    public static readonly Copenhagen<int> TruncatedNameStrLen = 14;

    public static readonly Copenhagen<string> DefaultPingTarget = "ping4.test.sehosts.com=IPv4 Internet,ping6.test.sehosts.com=IPv6 Internet";
    public static readonly Copenhagen<string> DefaultSpeedTarget = "";// speed4.test.sehosts.com|9821=IPv4 Internet,speed6.test.sehosts.com|9821=IPv6 Internet";  2020/08/18 disable by default !!!
    public static readonly Copenhagen<int> DefaultSpeedIntervalSecs = 600;
    public static readonly Copenhagen<int> DefaultSpeedSpanSecs = 7;
    public static readonly Copenhagen<int> DefaultSpeedTryCount = 5;
    public static readonly Copenhagen<int> DefaultPktLossTryCount = 100;
    public static readonly Copenhagen<int> DefaultPktLossIntervalMsec = 100;
    public static readonly Copenhagen<int> DefaultPktLossTimeoutMsecs = 500;
    public static readonly Copenhagen<int> DefaultPingNumTry = 3;
}

// SNMP 用に、ある特定の値を取得するための抽象クラス
public abstract class SnmpWorkFetcherBase : AsyncServiceWithMainLoop
{
    protected abstract Task GetValueAsync(SortedDictionary<string, string> ret, RefInt nextPollingInterval, CancellationToken cancel = default);

    public IEnumerable<KeyValuePair<string, string>> CurrentValues { get; private set; }

    protected abstract void InitImpl(object? param = null);

    public readonly SnmpWorkHost Host;

    public SnmpWorkFetcherBase(SnmpWorkHost host, object? param = null)
    {
        try
        {
            this.Host = host;

            this.CurrentValues = new SortedDictionary<string, string>();

            InitImpl(param);

            this.StartMainLoop(MainLoopAsync);
        }
        catch
        {
            this._DisposeSafe();
            throw;
        }
    }

    async Task MainLoopAsync(CancellationToken cancel)
    {
        while (cancel.IsCancellationRequested == false)
        {
            RefInt nextPollingInterval = Host.Settings.PollingIntervalsSec * 1000;

            try
            {
                SortedDictionary<string, string> ret = new SortedDictionary<string, string>(StrComparer.IgnoreCaseTrimComparer);

                await GetValueAsync(ret, nextPollingInterval, cancel);

                this.CurrentValues = ret;
            }
            catch (Exception ex)
            {
                this.CurrentValues = new SortedDictionary<string, string>();

                ex._Debug();
            }

            int interval = nextPollingInterval;
            if (interval <= 0) interval = Host.Settings.PollingIntervalsSec * 1000;

            await cancel._WaitUntilCanceledAsync(Util.GenRandInterval(interval));
        }
    }

    // 値を正規化する (一旦 double に変換してから、小数点以下 3 桁の数値に変換する)
    public virtual string NormalizeDoubleValue(string src)
    {
        double d = double.Parse(src);

        return d.ToString("F3");
    }

    // 長すぎる名前を短くする
    public virtual string TruncateName(string src, int maxLen = 0) => src._TruncStrMiddle(maxLen == 0 ? SnmpWorkConfig.TruncatedNameStrLen.Value : maxLen);

    // ターゲット文字列からホスト名とエイリアスを導出する
    public virtual void ParseTargetString(string src, out string hostname, out string alias)
    {
        hostname = "";
        alias = "";

        string[] tokens = src._NonNullTrim()._Split(StringSplitOptions.RemoveEmptyEntries, "=");

        string a, b;

        if (tokens.Length == 0) return;

        if (tokens.Length == 1)
        {
            a = tokens[0];
            b = tokens[0];
        }
        else
        {
            a = tokens[0];
            b = tokens[1];
        }

        if (a._IsEmpty()) a = b;

        hostname = a;
        alias = b;

        tokens = alias._Split(StringSplitOptions.RemoveEmptyEntries, "|");
        if (tokens.Length >= 1 && tokens[0]._IsFilled())
        {
            alias = tokens[0];
        }
    }
}

// SNMP Worker CGI ハンドラ
public class SnmpWorkCgiHandler : CgiHandlerBase
{
    public readonly SnmpWorkHost Host;

    public SnmpWorkCgiHandler(SnmpWorkHost host)
    {
        this.Host = host;
    }

    protected override void InitActionListImpl(CgiActionList noAuth, CgiActionList reqAuth)
    {
        try
        {
            noAuth.AddAction("/", WebMethodBits.GET | WebMethodBits.HEAD, async (ctx) =>
            {
                await Task.CompletedTask;
                var method = ctx.QueryString._GetStrFirst("method")._ParseEnum(SnmpWorkGetMethod.Get);
                return new HttpStringResult(Host.GetSnmpBody(method, ctx.QueryString._GetStrFirst("oid"), ctx.QueryString._GetStrFirst("retnone")._ToBool())._NormalizeCrlf(CrlfStyle.Lf, true));
            });
        }
        catch
        {
            this._DisposeSafe();
            throw;
        }
    }
}

[Flags]
public enum SnmpWorkGetMethod
{
    Get = 0,
    GetNext,
    GetAll,
}

// SNMP Worker ホストクラス
public class SnmpWorkHost : AsyncService
{
    readonly HiveData<SnmpWorkSettings> SettingsHive;

    // 'Config\SnmpWork' のデータ
    public SnmpWorkSettings Settings => SettingsHive.GetManagedDataSnapshot();

    // 内部データベース (index 管理)
    readonly HiveData<SnmpWorkInternalDb> InternalDbHive;

    // データベースへのアクセスを容易にするための自動プロパティ
    CriticalSection InternalDbLock => InternalDbHive.DataLock;
    SnmpWorkInternalDb InternalDb => InternalDbHive.ManagedData;
    SnmpWorkInternalDb InternalDbSnapshot => InternalDbHive.GetManagedDataSnapshot();

    readonly CriticalSection LockList = new CriticalSection<SnmpWorkHost>();

    readonly SortedDictionary<string, KeyValuePair<SnmpWorkFetcherBase, int>> CurrentFetcherList = new SortedDictionary<string, KeyValuePair<SnmpWorkFetcherBase, int>>(StrComparer.IgnoreCaseTrimComparer);

    readonly CgiHttpServer Cgi;

    public SnmpWorkHost()
    {
        try
        {
            // SnmpWorkSettings を読み込む
            this.SettingsHive = new HiveData<SnmpWorkSettings>(Hive.SharedLocalConfigHive, $"SnmpWork", null, HiveSyncPolicy.AutoReadFromFile);

            // データベース
            this.InternalDbHive = new HiveData<SnmpWorkInternalDb>(Hive.SharedLocalConfigHive, "InternalDatabase/SnmpWorkInternalDb",
                getDefaultDataFunc: () => new SnmpWorkInternalDb(),
                policy: HiveSyncPolicy.AutoReadWriteFile,
                serializer: HiveSerializerSelection.RichJson);

            // HTTP サーバーを立ち上げる
            this.Cgi = new CgiHttpServer(new SnmpWorkCgiHandler(this), new HttpServerOptions()
            {
                AutomaticRedirectToHttpsIfPossible = false,
                DisableHiveBasedSetting = true,
                DenyRobots = true,
                UseGlobalCertVault = false,
                LocalHostOnly = true,
                HttpPortsList = new int[] { Settings.HttpPort }.ToList(),
                HttpsPortsList = new List<int>(),
                UseKestrelWithIPACoreStack = true,
            },
            true);
        }
        catch
        {
            this._DisposeSafe();
            throw;
        }
    }

    public string GetSnmpBody(SnmpWorkGetMethod method, string requestedOid, bool returnNone)
    {
        string nullReturnStr = returnNone ? "NONE" : "";

        SortedDictionary<string, KeyValuePair<string, int>> values = GetValues();

        KeyValueList<int, string> namesList = new KeyValueList<int, string>();
        KeyValueList<int, string> valuesList = new KeyValueList<int, string>();

        // index 順にソート
        foreach (var kv in values.OrderBy(x => x.Value.Value))
        {
            int index = kv.Value.Value;
            string name = kv.Key;
            string value = kv.Value.Key;

            namesList.Add(index, name);
            valuesList.Add(index, value);
        }

        KeyValueList<int, string>? list = null;

        int specifiedIndex = -1;

        string oidPrefix = "";

        if (requestedOid.StartsWith(Consts.SnmpOids.SnmpWorkNames))
        {
            list = namesList;
            string remain = requestedOid.Substring(Consts.SnmpOids.SnmpWorkNames.Length);
            specifiedIndex = 0;
            if (remain.StartsWith("."))
            {
                specifiedIndex = remain.Substring(1)._ToInt();
            }
            oidPrefix = Consts.SnmpOids.SnmpWorkNames;
        }
        else if (requestedOid.StartsWith(Consts.SnmpOids.SnmpWorkValues))
        {
            list = valuesList;
            string remain = requestedOid.Substring(Consts.SnmpOids.SnmpWorkValues.Length);
            specifiedIndex = 0;
            if (remain.StartsWith("."))
            {
                specifiedIndex = remain.Substring(1)._ToInt();
            }
            oidPrefix = Consts.SnmpOids.SnmpWorkValues;
        }

        if (method == SnmpWorkGetMethod.GetAll)
        {
            // すべてのオブジェクトを列挙する
            StringWriter w = new StringWriter();
            for (int i = 0; i < namesList.Count; i++)
            {
                w.WriteLine($"{namesList[i].Value} ({namesList[i].Key}): {valuesList[i].Value}");
            }
            return w.ToString();
        }

        if (specifiedIndex < 0 || list == null)
        {
            // 不正
            return nullReturnStr;
        }

        if (method == SnmpWorkGetMethod.GetNext)
        {
            // 指定された index よりも 1 つ次のオブジェクトを返す
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].Key > specifiedIndex)
                {
                    //                        return oidPrefix + "." + list[i].Key + "\nobjectid\n" + oidPrefix + "." + list[i].Key;
                    return oidPrefix + "." + list[i].Key + "\nstring\n" + list[i].Value._FilledOrDefault("-");
                }
            }

            return nullReturnStr;
        }
        else
        {
            // 指定された index のオブジェクトを返す
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].Key == specifiedIndex)
                {
                    return oidPrefix + "." + list[i].Key + "\nstring\n" + list[i].Value._FilledOrDefault("-");
                }
            }

            return nullReturnStr;
        }
    }

    public int GetOrCreateIndex(int baseIndex, string str)
    {
        str = str.ToLowerInvariant().Trim();

        lock (InternalDbLock)
        {
            if (InternalDb.IndexTable.TryGetValue(str, out int index))
            {
                // すでに存在
                return index;
            }

            // まだ存在しない
            // baseIndex 以上で重複しない 1 つの値を選定する
            for (int i = baseIndex + 1; ; i++)
            {
                if (InternalDb.IndexTable.Values.Where(x => x == i).Any() == false)
                {
                    // 選定した
                    InternalDb.IndexTable.Add(str, i);

                    return i;
                }
            }
        }
    }

    public SortedDictionary<string, KeyValuePair<string, int>> GetValues()
    {
        SortedDictionary<string, KeyValuePair<string, int>> ret = new SortedDictionary<string, KeyValuePair<string, int>>();

        lock (LockList)
        {
            foreach (string name in CurrentFetcherList.Keys)
            {
                var fetcher = CurrentFetcherList[name];

                IEnumerable<KeyValuePair<string, string>> values = fetcher.Key.CurrentValues;

                foreach (var kv in values)
                {
                    string name2 = $"{name} - {kv.Key}";
                    string value2 = kv.Value;

                    ret.Add(name2, new KeyValuePair<string, int>(value2, GetOrCreateIndex(fetcher.Value, name2)));
                }
            }
        }

        return ret;
    }

    public void RegisterSensors(int snmpIndexBase)
    {
        var settings = this.Settings;

        foreach (var sensorItem in settings.SensorList)
        {
            if (sensorItem.SensorTitle._IsFilled() && sensorItem.SensorTitle._IsSamei(Consts.Strings.Sample) == false) // Sample config を除外する
            {
                if (sensorItem.SensorName._IsFilled() && sensorItem.SensorName._IsSamei(Consts.Strings.Sample) == false)
                {
                    this.Register(sensorItem.SensorTitle, snmpIndexBase, new SnmpWorkFetcherSensor(this, sensorItem));
                }
            }
        }
    }

    public void Register(string name, int snmpIndexBase, SnmpWorkFetcherBase fetcher)
    {
        using (EnterCriticalCounter())
        {
            try
            {
                name._NotEmptyCheck(nameof(name));

                lock (LockList)
                {
                    CurrentFetcherList.Add(name, new KeyValuePair<SnmpWorkFetcherBase, int>(fetcher, snmpIndexBase));
                }
            }
            catch
            {
                fetcher._DisposeSafe();

                throw;
            }
        }
    }

    protected override async Task CleanupImplAsync(Exception? ex)
    {
        try
        {
            await this.Cgi._DisposeSafeAsync();

            List<SnmpWorkFetcherBase> o = new List<SnmpWorkFetcherBase>();

            lock (LockList)
            {
                o = CurrentFetcherList.Values.Select(x => x.Key).ToList();
                CurrentFetcherList.Clear();
            }

            foreach (var fetcher in o)
            {
                await fetcher._DisposeSafeAsync();
            }

            this.SettingsHive._DisposeSafe();

            this.InternalDbHive._DisposeSafe();
        }
        finally
        {
            await base.CleanupImplAsync(ex);
        }
    }
}

#endif

