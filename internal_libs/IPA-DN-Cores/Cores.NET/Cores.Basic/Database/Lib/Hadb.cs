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
// Hadb Library

// メモ HADB 物理 DB 消費領域
//      30 万 DDNS 模擬レコード時  データ領域 300MB、インデックス領域 600 MB
//      つまり 1 レコードあたり デーサイズ 1.0KB、インデックスサイズ 2.0 KB (だいたいの目安)
//      SQL Server Express の DB は 10GB までなので 300 万レコードくらいが限度か?
//      .NET オンメモリ上で MemDb に展開すると 10GB くらいのメモリを消費した。数分かかった。
//      
// メモ 2  2022/01/06 模擬 DDNS 実験結果  300 万 DDNS 模擬レコード実験
//      Atomic 追加: 6000 レコード / 秒 (ランダムレコード)
//      Atomic 更新: 2700 レコード / 秒 (ランダムレコード)
//      
//      オンメモリで消費するプロセスメモリ: 約 8GB (余裕をもって 10GB と考えること)
//      FastSearchByKey の速度: 4,500,000 qps
//      
//      SQL DB: データ領域 2.0 GB, インデックス領域 7.1 GB
//      SQL サーバーからのフルリロード時間: 30 秒 (ネットワーク経由、700Mbps くらい出る)
//      リロードした SQL Row を HadbObject に変換する時間: 36 秒
//      HadbObject を MemDb に注入する時間: 150 秒
//      ロードにかかる合計時間: 220 秒程度
//      
//      SQL サーバープロセスの消費メモリ: 10GB
//      SQL サーバーの tempdb のサイズ: 2.5GB くらいまで拡大した
//      
//      .NET の GC の種類を Workstation から Server に変更すると、
//      ロード時間は若干高速になり、GC による一時停止も少なくなった一方で、
//      メモリ消費量は 1.5 倍くらいに増加した。 (約 15GB 余裕をもって 20GB と考えること)
//
//      ローカルバックアップの JSON ファイルの読み書きにかかる時間もだいたい同じ

#if CORES_BASIC_DATABASE

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
using System.Reflection;
using System.Data;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using Dapper;

namespace IPA.Cores.Basic;

public static partial class CoresConfig
{
    public static partial class Hadb
    {
        public static readonly Copenhagen<int> DefaultMaxArchiveCount = 20;
    }
}

public class HadbSimpleSettings : HadbSettingsBase
{
    public HadbSimpleSettings(string systemName, HadbOptionFlags optionFlags = HadbOptionFlags.None, FilePath? backupDataFile = null, FilePath? backupDynamicConfigFile = null, int lazyUpdateParallelQueueCount = Consts.Numbers.HadbDefaultLazyUpdateParallelQueueCount, int commandTimeoutSecs = 0, int debugMaxFetchItems = 0)
        : base(systemName, optionFlags, backupDataFile, backupDynamicConfigFile, lazyUpdateParallelQueueCount, commandTimeoutSecs, debugMaxFetchItems)
    {
    }
}

public class HadbSimpleBase<TMem, TDynamicConfig> : HadbBase<TMem, TDynamicConfig>
    where TMem : HadbMemDataBase, new()
    where TDynamicConfig : HadbDynamicConfig, new()
{
    string SimpleConfigFilePath;

    protected HadbSimpleBase(HadbSettingsBase settings, TDynamicConfig initialDynamicConfig) : base(settings, initialDynamicConfig)
    {
        try
        {
            this.SimpleConfigFilePath = Path.Combine(Env.AppLocalDir, "Config", "HadbSimpleSettings", settings.SystemName + ".config");
        }
        catch
        {
            this._DisposeSafe();
            throw;
        }
    }

    public new HadbSimpleSettings Settings => (HadbSimpleSettings)base.Settings;

    protected override async Task<KeyValueList<string, string>> LoadDynamicConfigFromDatabaseImplAsync(CancellationToken cancel = default)
    {
        return await Lfs.ReadKeyValueStrListFromConfigFileAsync(this.SimpleConfigFilePath, cancel: cancel);
    }

    protected override async Task AppendMissingDynamicConfigToDatabaseImplAsync(KeyValueList<string, string> missingValues, bool replaceAll, CancellationToken cancel = default)
    {
        KeyValueList<string, string>? tmp = null;

        if (replaceAll == false)
        {
            tmp = await Lfs.ReadKeyValueStrListFromConfigFileAsync(this.SimpleConfigFilePath, cancel: cancel);
        }

        if (tmp == null)
        {
            tmp = new KeyValueList<string, string>();
        }

        foreach (var missing in missingValues)
        {
            if (tmp.Where(x => StrComparer.IgnoreCaseTrimComparer.Equals(x.Key, missing.Key)).Any() == false)
            {
                tmp.Add(missing.Key, missing.Value);
            }
        }

        await Lfs.WriteKeyValueStrListToConfigFileAsync(this.SimpleConfigFilePath, tmp, this.DynamicConfigSampleObject.GetCommentsDictionary(), flags: FileFlags.AutoCreateDirectory | FileFlags.WriteOnlyIfChanged, writeBom: true, overwriteAndTruncate: true);
    }

    protected override Task<HadbTran> BeginDatabaseTransactionImplAsync(bool writeMode, bool isTransaction, HadbTranOptions options, CancellationToken cancel = default)
    {
        throw new NotImplementedException();
    }

    protected override Task<List<HadbStatJson>> EnumStatImplAsync(HadbTran tran, DateTimeOffset dtStart, DateTimeOffset dtEnd, int maxCount, CancellationToken cancel = default)
    {
        throw new NotImplementedException();
    }

    protected override bool IsDeadlockExceptionImpl(Exception ex)
    {
        return false;
    }

    protected override Task<List<HadbObject>> ReloadDataFromDatabaseImplAsync(bool fullReloadMode, DateTimeOffset partialReloadMinUpdateTime, Ref<DateTimeOffset>? lastStatTimestamp, CancellationToken cancel = default)
    {
        List<HadbObject> ret = new List<HadbObject>();

        return ret._TR();
    }

    protected override Task RestoreDataFromHadbObjectListImplAsync(List<HadbObject> objectList, CancellationToken cancel = default)
    {
        throw new NotImplementedException();
    }

    protected override Task WriteStatImplAsync(HadbTran tran, DateTimeOffset dt, string generator, string value, string ext1, string ext2, CancellationToken cancel = default)
    {
        throw new NotImplementedException();
    }

    protected internal override Task AtomicAddDataListToDatabaseImplAsync(HadbTran tran, IEnumerable<HadbObject> dataList, CancellationToken cancel = default)
    {
        throw new NotImplementedException();
    }

    protected internal override Task AtomicAddLogImplAsync(HadbTran tran, HadbLog log, string nameSpace, string ext1, string ext2, string ft1, string ft2, CancellationToken cancel = default)
    {
        throw new NotImplementedException();
    }

    protected internal override Task<bool> AtomicAddOrUpdateQuickByKeyOnDatabaseImplAsync<T>(HadbTran tran, string key, string nameSpace, T userData, bool doNotOverwrite, CancellationToken cancel = default)
    {
        throw new NotImplementedException();
    }

    protected internal override Task AtomicAddSnapImplAsync(HadbTran tran, HadbSnapshot snap, CancellationToken cancel = default)
    {
        throw new NotImplementedException();
    }

    protected internal override Task<HadbObject> AtomicDeleteDataFromDatabaseImplAsync(HadbTran tran, string uid, string typeName, string nameSpace, int maxArchive, CancellationToken cancel = default)
    {
        throw new NotImplementedException();
    }

    protected internal override Task<int> AtomicDeleteQuickByKeyOnDatabaseImplAsync<T>(HadbTran tran, string key, bool startWith, string nameSpace, CancellationToken cancel = default)
    {
        throw new NotImplementedException();
    }

    protected internal override Task<bool> AtomicDeleteQuickByUidOnDatabaseImplAsync<T>(HadbTran tran, string uid, string nameSpace, CancellationToken cancel = default)
    {
        throw new NotImplementedException();
    }

    protected internal override Task<IEnumerable<HadbObject>> AtomicGetArchivedDataFromDatabaseImplAsync(HadbTran tran, int maxItems, string uid, string typeName, string nameSpace, CancellationToken cancel = default, bool noCheckTypeIdAndNameSpace = false)
    {
        throw new NotImplementedException();
    }

    protected internal override Task<HadbObject?> AtomicGetDataFromDatabaseImplAsync(HadbTran tran, string uid, string typeName, string nameSpace, CancellationToken cancel = default, bool noCheckTypeIdAndNameSpace = false)
    {
        throw new NotImplementedException();
    }

    protected internal override Task<string> AtomicGetKvImplAsync(HadbTran tran, string key, CancellationToken cancel = default)
    {
        throw new NotImplementedException();
    }

    protected internal override Task<StrDictionary<string>> AtomicGetKvListImplAsync(HadbTran tran, CancellationToken cancel = default)
    {
        throw new NotImplementedException();
    }

    protected internal override Task<HadbQuick<T>?> AtomicGetQuickFromDatabaseImplAsync<T>(HadbTran tran, string uid, string nameSpace, CancellationToken cancel = default)
    {
        throw new NotImplementedException();
    }

    protected internal override Task<HadbObject?> AtomicSearchDataByKeyFromDatabaseImplAsync(HadbTran tran, HadbKeys keys, string typeName, string nameSpace, bool and, CancellationToken cancel = default)
    {
        throw new NotImplementedException();
    }

    protected internal override Task<IEnumerable<HadbObject>> AtomicSearchDataListByLabelsFromDatabaseImplAsync(HadbTran tran, HadbLabels labels, string typeName, string nameSpace, CancellationToken cancel = default)
    {
        throw new NotImplementedException();
    }

    protected internal override Task<HadbLogQueryResponse> AtomicSearchLogImplAsync(HadbTran tran, string typeName, HadbLogQuery query, string nameSpace, CancellationToken cancel = default)
    {
        throw new NotImplementedException();
    }

    protected internal override Task<IEnumerable<HadbQuick<T>>> AtomicSearchQuickByKeyOnDatabaseImplAsync<T>(HadbTran tran, string key, bool startWith, string nameSpace, CancellationToken cancel = default)
    {
        throw new NotImplementedException();
    }

    protected internal override Task AtomicSetKvImplAsync(HadbTran tran, string key, string value, CancellationToken cancel = default)
    {
        throw new NotImplementedException();
    }

    protected internal override Task<HadbObject> AtomicUpdateDataOnDatabaseImplAsync(HadbTran tran, HadbObject data, CancellationToken cancel = default)
    {
        throw new NotImplementedException();
    }

    protected internal override Task AtomicUpdateQuickByUidOnDatabaseImplAsync<T>(HadbTran tran, string uid, string nameSpace, T userData, CancellationToken cancel = default)
    {
        throw new NotImplementedException();
    }

    protected internal override Task<bool> LazyUpdateImplAsync(HadbTran tran, HadbObject data, CancellationToken cancel = default)
    {
        throw new NotImplementedException();
    }
}

public class HadbSearchResultJsonObject
{
    public string Uid = "";
    public long Ver;
    public DateTimeOffset CreateDt;
    public DateTimeOffset UpdateDt;
    public DateTimeOffset DeleteDt;
    public bool Deleted;
    public bool Archived;
    public string NameSpace = "";
    public string TypeName = "";
    //public string? Ext1;
    //public string? Ext2;
    public HadbData Data = null!;

    public HadbSearchResultJsonObject() { }

    public HadbSearchResultJsonObject(HadbObject src)
    {
        this.Uid = src.Uid;
        this.Ver = src.Ver;
        this.CreateDt = src.CreateDt;
        this.UpdateDt = src.UpdateDt;
        this.DeleteDt = src.DeleteDt;
        this.Archived = src.Archive;
        this.Deleted = src.Deleted;
        this.NameSpace = src.NameSpace;
        this.TypeName = src.UserDataTypeName;
        //this.Ext1 = src.Ext1._NullIfEmpty();
        //this.Ext2 = src.Ext2._NullIfEmpty();
        this.Data = src.UserData;
    }
}

public class HadbTestData : HadbData
{
    public string HostName = "";
    public string IPv4Address = "";
    public string IPv6Address = "";
    public int TestInt = 0;

    public override HadbKeys GetKeys() => new HadbKeys(this.HostName);

    public override HadbLabels GetLabels() => new HadbLabels(this.IPv4Address, this.IPv6Address);

    public override void Normalize()
    {
        this.HostName = this.HostName._NonNullTrim().ToLowerInvariant();
        this.IPv4Address = this.IPv4Address._NormalizeIp();
        this.IPv6Address = this.IPv6Address._NormalizeIp();
    }

}

public class HadbTestDynamicConfig : HadbDynamicConfig
{
    public int TestAbc;
    public string[] TestDef = EmptyOf<string>();
    public string TestStr1 = "";
    public string TestStr2 = "";

    protected override void NormalizeImpl()
    {
        this.TestAbc = this.TestAbc._ZeroToDefault(100, max: 1000);
    }
}

public class HadbTestMem : HadbMemDataBase
{
    protected override List<Type> GetDefinedUserDataTypesImpl()
    {
        List<Type> ret = new List<Type>();
        ret.Add(typeof(HadbTestData));
        return ret;
    }

    protected override List<Type> GetDefinedUserLogTypesImpl()
    {
        throw new NotImplementedException();
    }
}

public class HadbTest : HadbSqlBase<HadbTestMem, HadbTestDynamicConfig>
{
    public HadbTest(HadbSqlSettings settings, HadbTestDynamicConfig dynamicConfig) : base(settings, dynamicConfig) { }
}

public class HadbDynamicConfig : INormalizable
{
    protected virtual void NormalizeImpl() { }

    [SimpleComment("Database partial reload interval when last load was OK (milliseconds)")]
    public int HadbReloadIntervalMsecsLastOk = Consts.HadbDynamicConfigDefaultValues.HadbReloadIntervalMsecsLastOk;

    [SimpleComment("Database partial reload interval when last load was ERROR (milliseconds)")]
    public int HadbReloadIntervalMsecsLastError = Consts.HadbDynamicConfigDefaultValues.HadbReloadIntervalMsecsLastError;

    [SimpleComment("Database partial reload time-shift margin (milliseconds)")]
    public int HadbReloadTimeShiftMarginMsecs = Consts.HadbDynamicConfigDefaultValues.HadbReloadTimeShiftMarginMsecs;

    [SimpleComment("Database full reload interval (milliseconds)")]
    public int HadbFullReloadIntervalMsecs = Consts.HadbDynamicConfigDefaultValues.HadbFullReloadIntervalMsecs;

    [SimpleComment("Database lazy update routine interval (milliseconds)")]
    public int HadbLazyUpdateIntervalMsecs = Consts.HadbDynamicConfigDefaultValues.HadbLazyUpdateIntervalMsecs;

    [SimpleComment("Database snapshot interval (milliseconds)")]
    public int HadbAutomaticSnapshotIntervalMsecs = Consts.HadbDynamicConfigDefaultValues.HadbAutomaticSnapshotIntervalMsecs;

    [SimpleComment("Database statistics interval (milliseconds)")]
    public int HadbAutomaticStatIntervalMsecs = Consts.HadbDynamicConfigDefaultValues.HadbAutomaticStatIntervalMsecs;

    [SimpleComment("Database concurrent write transations limitation (total)")]
    public int HadbMaxDbConcurrentWriteTransactionsTotal = Consts.HadbDynamicConfigDefaultValues.HadbMaxDbConcurrentWriteTransactionsTotal;

    [SimpleComment("Database concurrent read transations limitation (total)")]
    public int HadbMaxDbConcurrentReadTransactionsTotal = Consts.HadbDynamicConfigDefaultValues.HadbMaxDbConcurrentReadTransactionsTotal;

    [SimpleComment("Database concurrent write transations limitation (per client IP address)")]
    public int HadbMaxDbConcurrentWriteTransactionsPerClient = Consts.HadbDynamicConfigDefaultValues.HadbMaxDbConcurrentWriteTransactionsPerClient;

    [SimpleComment("Database concurrent read transations limitation (per client IP address)")]
    public int HadbMaxDbConcurrentReadTransactionsPerClient = Consts.HadbDynamicConfigDefaultValues.HadbMaxDbConcurrentReadTransactionsPerClient;

    [SimpleComment("Threshold (in seconds) to regard object as stale (format: ObjectName + ' ' + seconds). You can define multiple fields of this line")]
    public string[] Hadb_ObjectStaleMarker_ObjNames_and_Seconds = new string[0];

    public HadbDynamicConfig()
    {
        this.Normalize();
    }

    public void Normalize()
    {
        this.HadbReloadIntervalMsecsLastOk = this.HadbReloadIntervalMsecsLastOk._ZeroToDefault(Consts.HadbDynamicConfigDefaultValues.HadbReloadIntervalMsecsLastOk, max: Consts.HadbDynamicConfigMaxValues.HadbReloadIntervalMsecsLastOk);
        this.HadbReloadIntervalMsecsLastError = this.HadbReloadIntervalMsecsLastError._ZeroToDefault(Consts.HadbDynamicConfigDefaultValues.HadbReloadIntervalMsecsLastError, max: Consts.HadbDynamicConfigMaxValues.HadbReloadIntervalMsecsLastError);
        this.HadbReloadTimeShiftMarginMsecs = this.HadbReloadTimeShiftMarginMsecs._ZeroToDefault(Consts.HadbDynamicConfigDefaultValues.HadbReloadTimeShiftMarginMsecs, max: Consts.HadbDynamicConfigMaxValues.HadbReloadTimeShiftMarginMsecs);
        this.HadbFullReloadIntervalMsecs = this.HadbFullReloadIntervalMsecs._ZeroToDefault(Consts.HadbDynamicConfigDefaultValues.HadbFullReloadIntervalMsecs, max: Consts.HadbDynamicConfigMaxValues.HadbFullReloadIntervalMsecs);
        this.HadbLazyUpdateIntervalMsecs = this.HadbLazyUpdateIntervalMsecs._ZeroToDefault(Consts.HadbDynamicConfigDefaultValues.HadbLazyUpdateIntervalMsecs, max: Consts.HadbDynamicConfigMaxValues.HadbLazyUpdateIntervalMsecs);

        this.HadbAutomaticSnapshotIntervalMsecs = Math.Max(this.HadbAutomaticSnapshotIntervalMsecs, 0);
        this.HadbAutomaticStatIntervalMsecs = Math.Max(this.HadbAutomaticStatIntervalMsecs, 0);

        this.HadbMaxDbConcurrentWriteTransactionsTotal = this.HadbMaxDbConcurrentWriteTransactionsTotal._ZeroToDefault(Consts.HadbDynamicConfigDefaultValues.HadbMaxDbConcurrentWriteTransactionsTotal);
        this.HadbMaxDbConcurrentReadTransactionsTotal = this.HadbMaxDbConcurrentReadTransactionsTotal._ZeroToDefault(Consts.HadbDynamicConfigDefaultValues.HadbMaxDbConcurrentReadTransactionsTotal);
        this.HadbMaxDbConcurrentWriteTransactionsPerClient = this.HadbMaxDbConcurrentWriteTransactionsPerClient._ZeroToDefault(Consts.HadbDynamicConfigDefaultValues.HadbMaxDbConcurrentWriteTransactionsPerClient);
        this.HadbMaxDbConcurrentReadTransactionsPerClient = this.HadbMaxDbConcurrentReadTransactionsPerClient._ZeroToDefault(Consts.HadbDynamicConfigDefaultValues.HadbMaxDbConcurrentReadTransactionsPerClient);

        this.NormalizeImpl();
    }

    public StrDictionary<string> GetCommentsDictionary()
    {
        StrDictionary<string> ret = new StrDictionary<string>();

        var rw = this.GetType()._GetFieldReaderWriter();

        var fields = rw.MetadataTable.Where(x => x.Value.MemberType.IsAnyOfThem(MemberTypes.Field, MemberTypes.Property));

        foreach (var field in fields)
        {
            string name = field.Key;
            var metaInfo = field.Value;
            Type? type = metaInfo.GetFieldOrPropertyInfo();
            var commentAttribute = metaInfo.GetCustomAttribute<SimpleCommentAttribute>();
            string commentStr = commentAttribute?.Comment._NonNullTrim() ?? "";
            ret[name] = commentStr;
        }

        return ret;
    }

    public KeyValueList<string, string> UpdateFromKeyValueListAndReturnKeyValueList(KeyValueList<string, string> dataListFromDb, bool returnOnlyMissing)
    {
        var rw = this.GetType()._GetFieldReaderWriter();

        var fields = rw.MetadataTable.Where(x => x.Value.MemberType.IsAnyOfThem(MemberTypes.Field, MemberTypes.Property));

        HashSet<string> consumedList = new HashSet<string>(StrComparer.IgnoreCaseTrimComparer);

        foreach (var field in fields)
        {
            string name = field.Key;
            var metaInfo = field.Value;
            Type? type = metaInfo.GetFieldOrPropertyInfo();

            bool consumed = false;

            if (type == typeof(int))
            {
                if (dataListFromDb._TryGetFirstValue(name, out string valueStr, StrComparer.IgnoreCaseTrimComparer))
                {
                    rw.SetValue(this, name, valueStr._ToInt());
                    consumed = true;
                }
            }
            else if (type == typeof(bool))
            {
                if (dataListFromDb._TryGetFirstValue(name, out string valueStr, StrComparer.IgnoreCaseTrimComparer))
                {
                    rw.SetValue(this, name, valueStr._ToBool());
                    consumed = true;
                }
            }
            else if (type == typeof(string))
            {
                if (dataListFromDb._TryGetFirstValue(name, out string valueStr, StrComparer.IgnoreCaseTrimComparer))
                {
                    rw.SetValue(this, name, valueStr._NonNullTrim());
                    consumed = true;
                }
            }
            else if (type == typeof(string[]))
            {
                string[] newArray = dataListFromDb.Where(x => x.Key._IsSameTrimi(name) && x.Value._IsFilled()).Select(x => x.Value._NonNullTrim()).ToArray();
                if (newArray.Any())
                {
                    rw.SetValue(this, name, newArray);
                    consumed = true;
                }
            }
            else
            {
                continue;
            }

            if (consumed)
            {
                consumedList.Add(name);
            }
        }

        this.Normalize();

        KeyValueList<string, string> ret = new KeyValueList<string, string>();

        int newIndex = 0;

        StrDictionary<int> orderList = new StrDictionary<int>(StrCmpi);

        foreach (var field in fields)
        {
            newIndex++;

            string name = field.Key;
            var metaInfo = field.Value;
            Type? type = metaInfo.GetFieldOrPropertyInfo();

            int originalIndex = dataListFromDb._IndexOfKey(name, StrCmpi);
            if (originalIndex < 0) originalIndex = 1000000000 + newIndex;
            orderList[name] = originalIndex;

            if (returnOnlyMissing == false || consumedList.Contains(name) == false)
            {
                if (type == typeof(int))
                {
                    ret.Add(name, ((int)rw.GetValue(this, name)!).ToString());
                }
                else if (type == typeof(bool))
                {
                    ret.Add(name, ((bool)rw.GetValue(this, name)!)._ToBoolStrLower());
                }
                else if (type == typeof(string))
                {
                    ret.Add(name, ((string?)rw.GetValue(this, name))._NonNullTrim());
                }
                else if (type == typeof(string[]))
                {
                    string[]? currentArray = (string[]?)rw.GetValue(this, name);
                    if (currentArray == null)
                    {
                        currentArray = EmptyOf<string>();
                    }

                    var tmp = currentArray.Where(x => x._IsFilled()).Select(x => x._NonNullTrim());

                    if (tmp.Any())
                    {
                        foreach (var a in currentArray)
                        {
                            ret.Add(name, a);
                        }
                    }
                    else
                    {
                        ret.Add(name, "");
                    }
                }
            }
        }

        if (returnOnlyMissing == false)
        {
            int index = 0;

            foreach (var kv in dataListFromDb)
            {
                if (fields.Where(x => x.Key._IsSamei(kv.Key)).Any() == false)
                {
                    ret.Add(kv.Key, kv.Value);

                    orderList[kv.Key] = index;
                }

                index++;
            }
        }

        ret = ret.OrderBy(x => orderList._GetOrDefault(x.Key, int.MaxValue))._ToKeyValueList();

        return ret;
    }
}

public class HadbSqlSettings : HadbSettingsBase
{
    public string SqlConnectStringForRead { get; }
    public string SqlConnectStringForWrite { get; }

    public IsolationLevel IsolationLevelForRead { get; }
    public IsolationLevel IsolationLevelForWrite { get; }

    public HadbSqlSettings(string systemName, string sqlConnectStringForRead, string sqlConnectStringForWrite, IsolationLevel isoLevelForRead = IsolationLevel.Snapshot, IsolationLevel isoLevelForWrite = IsolationLevel.Serializable, HadbOptionFlags optionFlags = HadbOptionFlags.None, FilePath? backupDataFile = null, FilePath? backupDynamicConfigFile = null, int lazyUpdateParallelQueueCount = Consts.Numbers.HadbDefaultLazyUpdateParallelQueueCount, int commandTimeoutSecs = 0, int debugMaxFetchItems = 0)
        : base(systemName, optionFlags, backupDataFile, backupDynamicConfigFile, lazyUpdateParallelQueueCount, commandTimeoutSecs, debugMaxFetchItems)
    {
        this.SqlConnectStringForRead = sqlConnectStringForRead;
        this.SqlConnectStringForWrite = sqlConnectStringForWrite;

        this.IsolationLevelForRead = isoLevelForRead;
        this.IsolationLevelForWrite = isoLevelForWrite;
    }
}

[EasyTable("HADB_STAT")]
public sealed class HadbSqlStatRow : INormalizable
{
    [EasyManualKey]
    public string STAT_UID { get; set; } = "";
    public string STAT_SYSTEMNAME { get; set; } = "";
    public long STAT_SNAPSHOT_NO { get; set; } = 0;
    public DateTimeOffset STAT_DT { get; set; } = Util.ZeroDateTimeOffsetValue;
    public string STAT_GENERATOR { get; set; } = "";
    public string STAT_VALUE { get; set; } = "";
    public string STAT_EXT1 { get; set; } = "";
    public string STAT_EXT2 { get; set; } = "";

    public void Normalize()
    {
        this.STAT_UID = this.STAT_UID._NormalizeUid();
        this.STAT_SYSTEMNAME = this.STAT_SYSTEMNAME._NormalizeKey(true);
        this.STAT_DT = this.STAT_DT._NormalizeDateTimeOffset();
        this.STAT_GENERATOR = this.STAT_GENERATOR._NonNullTrim().ToLowerInvariant();
        this.STAT_VALUE = this.STAT_VALUE._NonNull();
        this.STAT_EXT1 = this.STAT_EXT1._NonNull();
        this.STAT_EXT2 = this.STAT_EXT2._NonNull();
    }
}

[EasyTable("HADB_SNAPSHOT")]
public sealed class HadbSqlSnapshotRow : INormalizable
{
    [EasyManualKey]
    public string SNAPSHOT_UID { get; set; } = "";
    public string SNAPSHOT_SYSTEM_NAME { get; set; } = "";
    public long SNAPSHOT_NO { get; set; } = 0;
    public DateTimeOffset SNAPSHOT_DT { get; set; } = Util.ZeroDateTimeOffsetValue;
    public string SNAPSHOT_DESCRIPTION { get; set; } = "";
    public string SNAPSHOT_EXT1 { get; set; } = "";
    public string SNAPSHOT_EXT2 { get; set; } = "";

    public void Normalize()
    {
        this.SNAPSHOT_UID = this.SNAPSHOT_UID._NormalizeUid();
        this.SNAPSHOT_SYSTEM_NAME = this.SNAPSHOT_SYSTEM_NAME._NormalizeKey(true);
        this.SNAPSHOT_DT = this.SNAPSHOT_DT._NormalizeDateTimeOffset();
        this.SNAPSHOT_DESCRIPTION = this.SNAPSHOT_DESCRIPTION._NonNull();
        this.SNAPSHOT_EXT1 = this.SNAPSHOT_EXT1._NonNull();
        this.SNAPSHOT_EXT2 = this.SNAPSHOT_EXT2._NonNull();
    }
}

[EasyTable("HADB_KV")]
public sealed class HadbSqlKvRow : INormalizable
{
    [EasyKey]
    public long KV_ID { get; set; } = 0;
    public string KV_SYSTEM_NAME { get; set; } = "";
    public string KV_KEY { get; set; } = "";
    public string KV_VALUE { get; set; } = "";
    public bool KV_DELETED { get; set; } = false;
    public DateTimeOffset KV_CREATE_DT { get; set; } = Util.ZeroDateTimeOffsetValue;
    public DateTimeOffset KV_UPDATE_DT { get; set; } = Util.ZeroDateTimeOffsetValue;

    public void Normalize()
    {
        this.KV_SYSTEM_NAME = this.KV_SYSTEM_NAME._NormalizeKey(true);
        this.KV_KEY = this.KV_KEY._NormalizeKey(true, Consts.Numbers.MaxKeyOrLabelStrLength);
        this.KV_VALUE = this.KV_VALUE._NonNull();
        this.KV_CREATE_DT = this.KV_CREATE_DT._NormalizeDateTimeOffset();
        this.KV_UPDATE_DT = this.KV_UPDATE_DT._NormalizeDateTimeOffset();
    }
}

[EasyTable("HADB_LOG")]
public sealed class HadbSqlLogRow : INormalizable
{
    [EasyKey]
    public long LOG_ID { get; set; } = 0;
    public string LOG_UID { get; set; } = "";
    public string LOG_SYSTEM_NAME { get; set; } = "";
    public string LOG_TYPE { get; set; } = "";
    public string LOG_NAMESPACE { get; set; } = "";
    public DateTimeOffset LOG_DT { get; set; } = Util.ZeroDateTimeOffsetValue;
    public long LOG_SNAP_NO { get; set; } = 0;
    public bool LOG_DELETED { get; set; } = false;
    public string LOG_LABEL1 { get; set; } = "";
    public string LOG_LABEL2 { get; set; } = "";
    public string LOG_LABEL3 { get; set; } = "";
    public string LOG_LABEL4 { get; set; } = "";
    public string LOG_LABEL5 { get; set; } = "";
    public string LOG_VALUE { get; set; } = "";
    public string LOG_EXT1 { get; set; } = "";
    public string LOG_EXT2 { get; set; } = "";
    public string LOG_FT1 { get; set; } = "";
    public string LOG_FT2 { get; set; } = "";

    public void Normalize()
    {
        this.LOG_UID = this.LOG_UID._NormalizeUid();
        this.LOG_SYSTEM_NAME = this.LOG_SYSTEM_NAME._NormalizeKey(true);
        this.LOG_TYPE = this.LOG_TYPE._NonNullTrim();
        this.LOG_NAMESPACE = this.LOG_NAMESPACE._NormalizeKey(true);
        this.LOG_DT = this.LOG_DT._NormalizeDateTimeOffset();
        this.LOG_LABEL1 = this.LOG_LABEL1._NormalizeKey(false);
        this.LOG_LABEL2 = this.LOG_LABEL2._NormalizeKey(false);
        this.LOG_LABEL3 = this.LOG_LABEL3._NormalizeKey(false);
        this.LOG_LABEL4 = this.LOG_LABEL4._NormalizeKey(false);
        this.LOG_LABEL5 = this.LOG_LABEL5._NormalizeKey(false);
        this.LOG_VALUE = this.LOG_VALUE._NonNull();
        this.LOG_EXT1 = this.LOG_EXT1._NonNull();
        this.LOG_EXT2 = this.LOG_EXT2._NonNull();
        this.LOG_FT1 = this.LOG_FT1._NonNull();
        this.LOG_FT2 = this.LOG_FT2._NonNull();
    }
}

[EasyTable("HADB_CONFIG")]
public sealed class HadbSqlConfigRow : INormalizable
{
    [EasyKey]
    public long CONFIG_ID { get; set; } = 0;
    public string CONFIG_SYSTEMNAME { get; set; } = "";
    public string CONFIG_NAME { get; set; } = "";
    public string CONFIG_VALUE { get; set; } = "";
    public string CONFIG_EXT { get; set; } = "";

    public void Normalize()
    {
        this.CONFIG_SYSTEMNAME = this.CONFIG_SYSTEMNAME._NormalizeKey(true);
        this.CONFIG_NAME = this.CONFIG_NAME._NonNullTrim();
        this.CONFIG_VALUE = this.CONFIG_VALUE._NonNull();
        this.CONFIG_EXT = this.CONFIG_EXT._NonNull();
    }
}

[EasyTable("HADB_QUICK")]
public sealed class HadbSqlQuickRow : INormalizable
{
    [EasyManualKey]
    public string QUICK_UID { get; set; } = "";
    public string QUICK_SYSTEMNAME { get; set; } = "";
    public string QUICK_TYPE { get; set; } = "";
    public string QUICK_NAMESPACE { get; set; } = "";
    public bool QUICK_DELETED { get; set; }
    public string QUICK_KEY { get; set; } = "";
    public string QUICK_VALUE { get; set; } = "";
    public DateTimeOffset QUICK_CREATE_DT { get; set; } = Util.ZeroDateTimeOffsetValue;
    public DateTimeOffset QUICK_UPDATE_DT { get; set; } = Util.ZeroDateTimeOffsetValue;
    public DateTimeOffset QUICK_DELETE_DT { get; set; } = Util.ZeroDateTimeOffsetValue;
    public long QUICK_SNAPSHOT_NO { get; set; }
    public long QUICK_UPDATE_COUNT { get; set; }

    public void Normalize()
    {
        this.QUICK_UID = this.QUICK_UID._NormalizeUid();
        this.QUICK_SYSTEMNAME = this.QUICK_SYSTEMNAME._NormalizeKey(true);
        this.QUICK_TYPE = this.QUICK_TYPE._NonNullTrim();
        this.QUICK_NAMESPACE = this.QUICK_NAMESPACE._NormalizeKey(true);
        this.QUICK_KEY = this.QUICK_KEY._NormalizeKey(true);
        this.QUICK_VALUE = this.QUICK_VALUE._NonNull();
        this.QUICK_CREATE_DT = this.QUICK_CREATE_DT._NormalizeDateTimeOffset();
        this.QUICK_UPDATE_DT = this.QUICK_UPDATE_DT._NormalizeDateTimeOffset();
        this.QUICK_DELETE_DT = this.QUICK_DELETE_DT._NormalizeDateTimeOffset();
    }
}

[EasyTable("HADB_DATA")]
public sealed class HadbSqlDataRow : INormalizable
{
    [EasyManualKey]
    public string DATA_UID { get; set; } = "";
    public string DATA_SYSTEMNAME { get; set; } = "";
    public string DATA_TYPE { get; set; } = "";
    public string DATA_NAMESPACE { get; set; } = "";
    public long DATA_VER { get; set; } = 0;
    public bool DATA_DELETED { get; set; } = false;
    public bool DATA_ARCHIVE { get; set; } = false;
    public long DATA_SNAPSHOT_NO { get; set; } = 0;
    public DateTimeOffset DATA_CREATE_DT { get; set; } = Util.ZeroDateTimeOffsetValue;
    public DateTimeOffset DATA_UPDATE_DT { get; set; } = Util.ZeroDateTimeOffsetValue;
    public DateTimeOffset DATA_DELETE_DT { get; set; } = Util.ZeroDateTimeOffsetValue;
    public string DATA_KEY1 { get; set; } = "";
    public string DATA_KEY2 { get; set; } = "";
    public string DATA_KEY3 { get; set; } = "";
    public string DATA_KEY4 { get; set; } = "";
    public string DATA_KEY5 { get; set; } = "";
    public string DATA_LABEL1 { get; set; } = "";
    public string DATA_LABEL2 { get; set; } = "";
    public string DATA_LABEL3 { get; set; } = "";
    public string DATA_LABEL4 { get; set; } = "";
    public string DATA_LABEL5 { get; set; } = "";
    public string DATA_VALUE { get; set; } = "";
    public string DATA_EXT1 { get; set; } = "";
    public string DATA_EXT2 { get; set; } = "";
    public string DATA_FT1 { get; set; } = "";
    public string DATA_FT2 { get; set; } = "";
    public long DATA_LAZY_COUNT1 { get; set; } = 0;
    public long DATA_LAZY_COUNT2 { get; set; } = 0;
    public string DATA_UID_ORIGINAL { get; set; } = "";

    public void Normalize()
    {
        this.DATA_UID = this.DATA_UID._NormalizeUid();
        this.DATA_SYSTEMNAME = this.DATA_SYSTEMNAME._NormalizeKey(true);
        this.DATA_TYPE = this.DATA_TYPE._NonNullTrim();
        this.DATA_NAMESPACE = this.DATA_NAMESPACE._NormalizeKey(true);
        this.DATA_CREATE_DT = this.DATA_CREATE_DT._NormalizeDateTimeOffset();
        this.DATA_UPDATE_DT = this.DATA_UPDATE_DT._NormalizeDateTimeOffset();
        this.DATA_DELETE_DT = this.DATA_DELETE_DT._NormalizeDateTimeOffset();
        this.DATA_KEY1 = this.DATA_KEY1._NormalizeKey(true);
        this.DATA_KEY2 = this.DATA_KEY2._NormalizeKey(true);
        this.DATA_KEY3 = this.DATA_KEY3._NormalizeKey(true);
        this.DATA_KEY4 = this.DATA_KEY4._NormalizeKey(true);
        this.DATA_KEY5 = this.DATA_KEY5._NormalizeKey(true);
        this.DATA_LABEL1 = this.DATA_LABEL1._NormalizeKey(false);
        this.DATA_LABEL2 = this.DATA_LABEL2._NormalizeKey(false);
        this.DATA_LABEL3 = this.DATA_LABEL3._NormalizeKey(false);
        this.DATA_LABEL4 = this.DATA_LABEL4._NormalizeKey(false);
        this.DATA_LABEL5 = this.DATA_LABEL5._NormalizeKey(false);
        this.DATA_VALUE = this.DATA_VALUE._NonNull();
        this.DATA_EXT1 = this.DATA_EXT1._NonNull();
        this.DATA_EXT2 = this.DATA_EXT2._NonNull();
        this.DATA_FT1 = this.DATA_FT1._NonNull();
        this.DATA_FT2 = this.DATA_FT2._NonNull();
        this.DATA_UID_ORIGINAL = this.DATA_UID_ORIGINAL._NormalizeUid();
    }
}

[Flags]
public enum HadbTranOptions : long
{
    None = 0,
    UseStrictLock = 1,
    NoTransactionOnWrite = 2,

    Default = None,
}

[Flags]
public enum HadbSqlDbCaps : long
{
    None = 0,
    HadbLog_HasLogDtIndex = 1,
}

public abstract class HadbSqlBase<TMem, TDynamicConfig> : HadbBase<TMem, TDynamicConfig>
    where TMem : HadbMemDataBase, new()
    where TDynamicConfig : HadbDynamicConfig, new()
{
    public new HadbSqlSettings Settings => (HadbSqlSettings)base.Settings;

    public HadbSqlDbCaps SqlDbCaps { get; private set; } = HadbSqlDbCaps.None;

    public HadbSqlBase(HadbSqlSettings settings, TDynamicConfig initialDynamicConfig) : base(settings, initialDynamicConfig)
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

    protected override bool IsDeadlockExceptionImpl(Exception ex)
        => Database.IsDeadlockException(ex);

    public async Task<Database> OpenSqlDatabaseAsync(bool writeMode, CancellationToken cancel = default)
    {
        Database db = new Database(
            writeMode ? this.Settings.SqlConnectStringForWrite : this.Settings.SqlConnectStringForRead,
            defaultIsolationLevel: writeMode ? this.Settings.IsolationLevelForWrite : this.Settings.IsolationLevelForRead);

        try
        {
            db.CommandTimeoutSecs = this.Settings.CommandTimeoutSecs;

            await db.EnsureOpenAsync(cancel);

            return db;
        }
        catch
        {
            await db._DisposeSafeAsync();
            throw;
        }
    }

    protected override async Task<KeyValueList<string, string>> LoadDynamicConfigFromDatabaseImplAsync(CancellationToken cancel = default)
    {
        IEnumerable<HadbSqlConfigRow> configList = null!;

        try
        {
            await using var dbReader = await this.OpenSqlDatabaseAsync(false, cancel);

            await dbReader.TranReadSnapshotIfNecessaryAsync(async () =>
            {
                configList = await dbReader.EasySelectAsync<HadbSqlConfigRow>("select * from HADB_CONFIG where CONFIG_SYSTEMNAME = @CONFIG_SYSTEMNAME", new { CONFIG_SYSTEMNAME = this.SystemName });
            });

            // DB の Caps の取得
            HadbSqlDbCaps caps = HadbSqlDbCaps.None;

            try
            {
                int hasIndex = await dbReader.ExecuteScalarAsync<int>("(SELECT COUNT(*) as HAS_INDEX FROM sys.indexes WHERE object_id = OBJECT_ID('HADB_LOG') AND name='LOG_DT')");
                if (hasIndex >= 1)
                {
                    caps |= HadbSqlDbCaps.HadbLog_HasLogDtIndex;
                }
            }
            catch { }

            this.SqlDbCaps = caps;
        }
        catch (Exception ex)
        {
            Debug($"LoadDynamicConfigFromDatabaseImplAsync: Database Connect Error for Read: {ex.ToString()}");
            throw;
        }

        configList._NormalizeAll();

        KeyValueList<string, string> ret = new KeyValueList<string, string>();
        foreach (var configRow in configList)
        {
            ret.Add(configRow.CONFIG_NAME, configRow.CONFIG_VALUE);
        }

        return ret;
    }

    protected override async Task<List<HadbStatJson>> EnumStatImplAsync(HadbTran tran, DateTimeOffset dtStart, DateTimeOffset dtEnd, int maxCount, CancellationToken cancel = default)
    {
        var dbReader = ((HadbSqlTran)tran).Db;

        if (dtStart._IsZeroDateTime())
        {
            dtStart = Util.ZeroDateTimeOffsetValue;
        }

        if (dtEnd._IsZeroDateTime())
        {
            dtEnd = Util.MaxDateTimeOffsetValue;
        }

        string topStr = "";

        if (maxCount >= 1 && maxCount < int.MaxValue)
        {
            topStr = $"top {maxCount}";
        }

        var rows = await dbReader.EasySelectAsync<HadbSqlStatRow>($"select {topStr} * from HADB_STAT where STAT_SYSTEMNAME = @STAT_SYSTEMNAME and STAT_DT >= @DT_START and STAT_DT <= @DT_END order by STAT_DT desc",
            new
            {
                STAT_SYSTEMNAME = this.SystemName,
                DT_START = dtStart,
                DT_END = dtEnd,
            },
            cancel: cancel);

        List<HadbStatJson> ret = new List<HadbStatJson>();

        foreach (var row in rows)
        {
            ret.Add(new HadbStatJson
            {
                TimeStamp = row.STAT_DT,
                SnapshotNo = row.STAT_SNAPSHOT_NO,
                JsonData = row.STAT_VALUE,
                Generator = row.STAT_GENERATOR,
                Ext1 = row.STAT_EXT1,
                Ext2 = row.STAT_EXT2,
            });
        }

        return ret;
    }

    protected override async Task WriteStatImplAsync(HadbTran tran, DateTimeOffset dt, string generator, string value, string ext1, string ext2, CancellationToken cancel = default)
    {
        var dbWriter = ((HadbSqlTran)tran).Db;
        tran.CheckIsWriteMode();

        // 現在の Snapshot No を取得
        long snapNo = (await this.AtomicGetKvImplAsync(tran, "_HADB_SYS_CURRENT_SNAPSHOT_NO", cancel))._ToLong();

        // STAT を書き込み
        HadbSqlStatRow r = new HadbSqlStatRow
        {
            STAT_UID = Str.NewUid("STAT", '_'),
            STAT_SYSTEMNAME = this.SystemName,
            STAT_SNAPSHOT_NO = snapNo,
            STAT_DT = dt,
            STAT_GENERATOR = generator._NonNullTrim()._NormalizeFqdn().ToLowerInvariant(),
            STAT_VALUE = value._NonNull(),
            STAT_EXT1 = ext1._NonNull(),
            STAT_EXT2 = ext2._NonNull(),
        };

        await dbWriter.EasyInsertAsync(r, cancel);

        // 最後に STAT を書き込んだ日時を書き込み
        await this.AtomicSetKvImplAsync(tran, "_HADB_SYS_LAST_STAT_WRITE_DT", dt._ToDtStr(withMSsecs: true, withNanoSecs: true));
    }

    protected override async Task AppendMissingDynamicConfigToDatabaseImplAsync(KeyValueList<string, string> missingValues, bool replaceAll, CancellationToken cancel = default)
    {
        if (missingValues.Any() == false) return;

        await using var dbWriter = await this.OpenSqlDatabaseAsync(true, cancel);

        if (replaceAll == false)
        {
            // DB にまだ存在しないが定義されるべき TDynamicConfig のフィールドがある場合は DB に初期値を書き込む
            foreach (var missingValueName in missingValues.Select(x => x.Key._NonNullTrim()).Distinct(StrComparer.IgnoreCaseComparer))
            {
                await dbWriter.TranAsync(async () =>
                {
                    // DB にまだ値がない場合のみ書き込む。
                    // すでにある場合は書き込みしない。

                    var tmp = await dbWriter.EasySelectAsync<HadbSqlConfigRow>("select * from HADB_CONFIG where CONFIG_SYSTEMNAME = @CONFIG_SYSTEMNAME and CONFIG_NAME = @CONFIG_NAME",
                        new
                        {
                            CONFIG_SYSTEMNAME = this.SystemName,
                            CONFIG_NAME = missingValueName,
                        },
                        cancel: cancel);

                    if (tmp.Any())
                    {
                        return false;
                    }

                    var valuesList = missingValues.Where(x => x.Key._IsSameiTrim(missingValueName)).Select(x => x.Value);

                    foreach (var value in valuesList)
                    {
                        await dbWriter.EasyInsertAsync(new HadbSqlConfigRow
                        {
                            CONFIG_SYSTEMNAME = this.SystemName,
                            CONFIG_NAME = missingValueName,
                            CONFIG_VALUE = value,
                            CONFIG_EXT = "",
                        }, cancel);
                    }

                    return true;
                });
            }
        }
        else
        {
            // すべて一度削除して新規に追加する
            await dbWriter.TranAsync(async () =>
            {
                // 既存のものを全部削除
                await dbWriter.EasyExecuteAsync("delete from HADB_CONFIG where CONFIG_SYSTEMNAME = @CONFIG_SYSTEMNAME",
                    new
                    {
                        CONFIG_SYSTEMNAME = this.SystemName,
                    });

                // 追加
                var bulkInsertTable = new DataTable("HADB_CONFIG");

                // 注意: Bulk Insert の API には癖がある。カラムの順番は物理的 DB が優先され、プログラム上の名前はあまり意味がない。なんとひどいことだ。
                bulkInsertTable.Columns.Add("CONFIG_ID");
                bulkInsertTable.Columns.Add("CONFIG_SYSTEMNAME");
                bulkInsertTable.Columns.Add("CONFIG_NAME");
                bulkInsertTable.Columns.Add("CONFIG_VALUE");
                bulkInsertTable.Columns.Add("CONFIG_EXT");

                foreach (var kv in missingValues)
                {
                    if (kv.Key._IsFilled())
                    {
                        var row = bulkInsertTable.NewRow();

                        row["CONFIG_ID"] = DBNull.Value;
                        row["CONFIG_SYSTEMNAME"] = this.SystemName;
                        row["CONFIG_NAME"] = kv.Key;
                        row["CONFIG_VALUE"] = kv.Value;
                        row["CONFIG_EXT"] = "";

                        bulkInsertTable.Rows.Add(row);
                    }
                }

                await dbWriter.SqlBulkWriteAsync("HADB_CONFIG", bulkInsertTable, cancel);

                return true;
            });
        }
    }

    protected override async Task RestoreDataFromHadbObjectListImplAsync(List<HadbObject> objectList, CancellationToken cancel = default)
    {
        await using var db = await this.OpenSqlDatabaseAsync(true, cancel);

        DateTimeOffset now = DtOffsetNow;

        await db.TranAsync(async () =>
        {
            string postfix = "_OLD_" + Str.DateTimeToYymmddHHmmssLong(now.LocalDateTime).ToString();
            string newSystemName = this.SystemName + postfix;

            // 古いデータのシステム名カラムを変更し、事実上削除したことと同一効果とする
            await db.EasyExecuteAsync("update HADB_DATA set DATA_SYSTEMNAME = @NEW_NAME, DATA_UID = DATA_UID + @POSTFIX where DATA_SYSTEMNAME = @OLD_NAME",
                new
                {
                    NEW_NAME = newSystemName,
                    OLD_NAME = this.SystemName,
                    POSTFIX = postfix,
                });

            await db.EasyExecuteAsync("update HADB_KV set KV_SYSTEM_NAME = @NEW_NAME where KV_SYSTEM_NAME = @OLD_NAME",
                new
                {
                    NEW_NAME = newSystemName,
                    OLD_NAME = this.SystemName,
                });

            await db.EasyExecuteAsync("update HADB_SNAPSHOT set SNAPSHOT_SYSTEM_NAME = @NEW_NAME where SNAPSHOT_SYSTEM_NAME = @OLD_NAME",
                new
                {
                    NEW_NAME = newSystemName,
                    OLD_NAME = this.SystemName,
                });

            await db.EasyExecuteAsync("update HADB_STAT set STAT_SYSTEMNAME = @NEW_NAME where STAT_SYSTEMNAME = @OLD_NAME",
                new
                {
                    NEW_NAME = newSystemName,
                    OLD_NAME = this.SystemName,
                });

            await db.EasyExecuteAsync("update HADB_LOG set LOG_SYSTEM_NAME = @NEW_NAME where LOG_SYSTEM_NAME = @OLD_NAME",
                new
                {
                    NEW_NAME = newSystemName,
                    OLD_NAME = this.SystemName,
                });

            HashSet<long> snapshotSet = new HashSet<long>();

            // データを書き戻す
            foreach (var obj in objectList)
            {
                if (obj.Deleted == false)
                {
                    var keys = obj.GetKeys();
                    var labels = obj.GetLabels();

                    HadbSqlDataRow row = new HadbSqlDataRow
                    {
                        DATA_UID = obj.Uid,
                        DATA_SYSTEMNAME = this.SystemName,
                        DATA_TYPE = obj.UserDataTypeName,
                        DATA_NAMESPACE = obj.NameSpace,
                        DATA_VER = obj.Ver,
                        DATA_DELETED = false,
                        DATA_ARCHIVE = false,
                        DATA_SNAPSHOT_NO = obj.SnapshotNo,
                        DATA_CREATE_DT = obj.CreateDt,
                        DATA_UPDATE_DT = obj.UpdateDt,
                        DATA_DELETE_DT = obj.DeleteDt,
                        DATA_KEY1 = keys.Key1,
                        DATA_KEY2 = keys.Key2,
                        DATA_KEY3 = keys.Key3,
                        DATA_KEY4 = keys.Key4,
                        DATA_KEY5 = keys.Key5,
                        DATA_LABEL1 = labels.Label1,
                        DATA_LABEL2 = labels.Label2,
                        DATA_LABEL3 = labels.Label3,
                        DATA_LABEL4 = labels.Label4,
                        DATA_LABEL5 = labels.Label5,
                        DATA_VALUE = obj.GetUserDataJsonString(),
                        DATA_EXT1 = obj.Ext1,
                        DATA_EXT2 = obj.Ext2,
                        DATA_FT1 = obj.Ft1,
                        DATA_FT2 = obj.Ft2,
                        DATA_UID_ORIGINAL = obj.Uid,
                    };

                    snapshotSet.Add(obj.SnapshotNo);

                    await db.EasyInsertAsync(row, cancel);
                }
            }

            // 対応するスナップショットを一気に自動生成する
            var snapshots = snapshotSet.OrderBy(x => x);
            if (snapshots.Any() == false)
            {
                snapshots = (new long[] { 1 }).ToList().OrderBy(x => x);
            }

            long maxSnapshotNo = 0;
            string maxSnapshotUid = "";
            DateTimeOffset maxSnapshotTs = DtOffsetZero;

            foreach (var snapshot in snapshots)
            {
                HadbSqlSnapshotRow row = new HadbSqlSnapshotRow
                {
                    SNAPSHOT_UID = Str.NewUid("SNAPSHOT", '_'),
                    SNAPSHOT_SYSTEM_NAME = this.SystemName,
                    SNAPSHOT_NO = snapshot,
                    SNAPSHOT_DT = DtOffsetNow,
                    SNAPSHOT_DESCRIPTION = $"Restored Dummy Snapshot " + snapshot.ToString(),
                    SNAPSHOT_EXT1 = "",
                    SNAPSHOT_EXT2 = "",
                };

                maxSnapshotNo = row.SNAPSHOT_NO;
                maxSnapshotUid = row.SNAPSHOT_UID;
                maxSnapshotTs = row.SNAPSHOT_DT;

                await db.EasyInsertAsync(row, cancel);
            }

            await db.EasyInsertAsync(new HadbSqlKvRow
            {
                KV_SYSTEM_NAME = this.SystemName,
                KV_KEY = "_HADB_SYS_CURRENT_SNAPSHOT_NO",
                KV_VALUE = maxSnapshotNo.ToString(),
                KV_DELETED = false,
                KV_CREATE_DT = DtOffsetNow,
                KV_UPDATE_DT = DtOffsetNow,
            }, cancel);

            await db.EasyInsertAsync(new HadbSqlKvRow
            {
                KV_SYSTEM_NAME = this.SystemName,
                KV_KEY = "_HADB_SYS_CURRENT_SNAPSHOT_UID",
                KV_VALUE = maxSnapshotUid,
                KV_DELETED = false,
                KV_CREATE_DT = DtOffsetNow,
                KV_UPDATE_DT = DtOffsetNow,
            }, cancel);

            await db.EasyInsertAsync(new HadbSqlKvRow
            {
                KV_SYSTEM_NAME = this.SystemName,
                KV_KEY = "_HADB_SYS_CURRENT_SNAPSHOT_TIMESTAMP",
                KV_VALUE = maxSnapshotTs._ToDtStr(withMSsecs: true, withNanoSecs: true),
                KV_DELETED = false,
                KV_CREATE_DT = DtOffsetNow,
                KV_UPDATE_DT = DtOffsetNow,
            }, cancel);

            return true;
        });
    }

    protected override async Task<List<HadbObject>> ReloadDataFromDatabaseImplAsync(bool fullReloadMode, DateTimeOffset partialReloadMinUpdateTime, Ref<DateTimeOffset>? lastStatTimestamp, CancellationToken cancel = default)
    {
        IEnumerable<HadbSqlDataRow> rows = null!;

        //Dbg.Where();
        try
        {
            await using var dbReader = await this.OpenSqlDatabaseAsync(false, cancel);

            await dbReader.TranReadSnapshotIfNecessaryAsync(async () =>
            {
                var dbParams = new DynamicParameters();

                string topStr = "";
                if (this.Settings.DebugMaxFetchItems >= 1)
                {
                    topStr = $"top {this.Settings.DebugMaxFetchItems}";
                }

                string sql = $"select {topStr} * from HADB_DATA where DATA_SYSTEMNAME = @DATA_SYSTEMNAME and DATA_ARCHIVE = 0";

                dbParams.Add("DATA_SYSTEMNAME", this.SystemName);

                if (fullReloadMode == false)
                {
                    // 部分的なリロード
                    sql += " and DATA_UPDATE_DT >= @DT_MIN";
                    dbParams.Add("DT_MIN", partialReloadMinUpdateTime);
                }
                else
                {
                    // フルリロード
                    sql += " and DATA_DELETED = 0";

                    var now = DtOffsetNow;

                    int index = 0;

                    foreach (var staleConfig in this.CurrentDynamicConfig.Hadb_ObjectStaleMarker_ObjNames_and_Seconds)
                    {
                        if (staleConfig._GetKeyAndValue(out string typeName, out string secsStr))
                        {
                            if (typeName._IsFilled() && secsStr._IsFilled())
                            {
                                int secsInt = secsStr._ToInt();
                                if (secsInt >= 1 && secsInt < int.MaxValue)
                                {
                                    long msecs = secsInt * 1000L;
                                    if (msecs >= 1)
                                    {
                                        var threshold = now.AddMilliseconds(-msecs);
                                        if (threshold._IsZeroDateTime() == false)
                                        {
                                            index++;
                                            sql += $" and (DATA_UPDATE_DT >= @THRESHOLD_{index} or DATA_TYPE != @TYPE_{index})";

                                            dbParams.Add($"@TYPE_{index}", typeName);
                                            dbParams.Add($"@THRESHOLD_{index}", threshold);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                Debug($"ReloadDataFromDatabaseImplAsync: Start DB Select.");

                rows = await dbReader.EasySelectAsync<HadbSqlDataRow>(sql, dbParams);

                Debug($"ReloadDataFromDatabaseImplAsync: Finished DB Select. Rows = {rows.Count()._ToString3()}");

                HadbSqlKvRow? kv = await dbReader.EasySelectSingleAsync<HadbSqlKvRow>("select top 1 * from HADB_KV where KV_SYSTEM_NAME = @KV_SYSTEM_NAME and KV_KEY = @KV_KEY and KV_DELETED = 0 order by KV_ID desc",
                    new
                    {
                        KV_SYSTEM_NAME = this.SystemName,
                        KV_KEY = "_HADB_SYS_LAST_STAT_WRITE_DT",
                    });

                DateTimeOffset lastStat = Util.ZeroDateTimeOffsetValue;

                if (kv != null) lastStat = Str.DtstrToDateTimeOffset(kv.KV_VALUE);

                lastStatTimestamp?.Set(lastStat);
            });
        }
        catch (Exception ex)
        {
            Debug($"ReloadDataFromDatabaseImplAsync: Database Connect Error for Read: {ex.ToString()}");
            throw;
        }

        //Dbg.Where();

        var rowsList = rows.ToList();

        //rowsList.Count._Print();

        await rowsList._NormalizeAllParallelAsync(cancel: cancel);

        //Dbg.Where();

        Debug($"ReloadDataFromDatabaseImplAsync: Start Processing DB Rows to HadbObject convert. Rows = {rows.Count()._ToString3()}");

        ProgressReporter? reporter = null;

        if (fullReloadMode)
        {
            reporter = new ProgressReporter(new ProgressReporterSetting(ProgressReporterOutputs.Debug, toStr3: true, showEta: true,
                options: ProgressReporterOptions.EnableThroughput | ProgressReporterOptions.ShowThroughputAsInt,
                reportTimingSetting: new ProgressReportTimingSetting(false, 1000)));
        }

        long currentCount = 0;
        long totalCount = rowsList.Count;

        try
        {
            List<HadbObject> ret = await rowsList._ProcessParallelAndAggregateAsync((srcList, taskIndex) =>
            {
                List<HadbObject> tmp = new List<HadbObject>();
                int i = 0;
                foreach (var row in srcList)
                {
                    Type? type = this.GetDataTypeByTypeName(row.DATA_TYPE);
                    if (type != null)
                    {
                        HadbData? data = (HadbData?)row.DATA_VALUE._JsonToObject(type);
                        if (data != null)
                        {
                            HadbObject obj = new HadbObject(data, this.Settings.OptionFlags, row.DATA_EXT1, row.DATA_EXT2, row.DATA_FT1, row.DATA_FT2, row.DATA_UID, row.DATA_VER, false, row.DATA_SNAPSHOT_NO, row.DATA_NAMESPACE, row.DATA_DELETED, row.DATA_CREATE_DT, row.DATA_UPDATE_DT, row.DATA_DELETE_DT);

                            tmp.Add(obj);

                            i++;

                            if ((i % 100) == 0)
                            {
                                long current = Interlocked.Add(ref currentCount, 100);
                                reporter?.ReportProgress(new ProgressData(current, totalCount, false, "DB Rows to HADB Object"));
                            }
                        }
                    }
                }
                return TR(tmp);
            }, cancel: cancel);

            reporter?.ReportProgress(new ProgressData(ret.Count, ret.Count, true, "DB Rows to HADB Object"));

            Debug($"ReloadDataFromDatabaseImplAsync: Finished Processing DB Rows to HadbObject convert. Objects = {ret.Count._ToString3()}");

            //Dbg.Where();

            //ret.Count._Print();

            return ret;
        }
        finally
        {
            reporter._DisposeSafe();
        }
    }

    protected override async Task<HadbTran> BeginDatabaseTransactionImplAsync(bool writeMode, bool isTransaction, HadbTranOptions options, CancellationToken cancel = default)
    {
        Database db = await this.OpenSqlDatabaseAsync(writeMode, cancel);

        try
        {
            if (isTransaction)
            {
                await db.BeginAsync(cancel: cancel);
            }

            bool isDbSnapshotReadMode = (writeMode == false) && db.DefaultIsolationLevel == IsolationLevel.Snapshot;

            HadbSqlTran ret = new HadbSqlTran(writeMode, isTransaction, this, db, isDbSnapshotReadMode, options);

            return ret;
        }
        catch (Exception ex)
        {
            await db._DisposeSafeAsync(ex);
            throw;
        }
    }

    protected internal override async Task<HadbQuick<T>?> AtomicGetQuickFromDatabaseImplAsync<T>(HadbTran tran, string uid, string nameSpace, CancellationToken cancel = default)
    {
        uid = uid._NormalizeUid();
        nameSpace = nameSpace._HadbNameSpaceNormalize();
        string typeName = typeof(T).Name;

        var db = ((HadbSqlTran)tran).Db;

        // READCOMMITTEDLOCK, ROWLOCK は、「トランザクション分離レベルが Snapshot かつ読み取り専用の場合」以外に付ける。
        string query = $"select * from HADB_QUICK {(tran.ShouldUseLightLock ? "with (READCOMMITTEDLOCK, ROWLOCK)" : "")} where QUICK_UID = @QUICK_UID and QUICK_SYSTEMNAME = @QUICK_SYSTEMNAME and QUICK_NAMESPACE = @QUICK_NAMESPACE and QUICK_TYPE = @QUICK_TYPE and QUICK_DELETED = 0";

        var row = await db.EasySelectSingleAsync<HadbSqlQuickRow>(query,
            new
            {
                QUICK_UID = uid,
                QUICK_SYSTEMNAME = this.SystemName,
                QUICK_NAMESPACE = nameSpace,
                QUICK_TYPE = typeName,
            },
            throwErrorIfMultipleFound: true,
            cancel: cancel);

        if (row == null)
        {
            return null;
        }

        HadbQuick<T> ret = new HadbQuick<T>(
            row.QUICK_VALUE._JsonToObject<T>()!,
            row.QUICK_UID,
            row.QUICK_NAMESPACE,
            row.QUICK_KEY,
            row.QUICK_CREATE_DT,
            row.QUICK_UPDATE_DT,
            row.QUICK_SNAPSHOT_NO,
            row.QUICK_UPDATE_COUNT);

        return ret;
    }

    protected internal override async Task<IEnumerable<HadbQuick<T>>> AtomicSearchQuickByKeyOnDatabaseImplAsync<T>(HadbTran tran, string key, bool startWith, string nameSpace, CancellationToken cancel = default)
    {
        key = key._NormalizeKey(true);
        nameSpace = nameSpace._HadbNameSpaceNormalize();
        string typeName = typeof(T).Name;

        var db = ((HadbSqlTran)tran).Db;

        // READCOMMITTEDLOCK, ROWLOCK は、「トランザクション分離レベルが Snapshot かつ読み取り専用の場合」以外に付ける。
        string query = $"select * from HADB_QUICK  {(tran.ShouldUseLightLock ? "with (READCOMMITTEDLOCK, ROWLOCK)" : "")} " +
            $"where {(key._IsEmpty() ? "" : (startWith ? "QUICK_KEY like @QUICK_KEY escape '?' and" : "QUICK_KEY = @QUICK_KEY and"))} " +
            "QUICK_SYSTEMNAME = @QUICK_SYSTEMNAME and QUICK_NAMESPACE = @QUICK_NAMESPACE and QUICK_TYPE = @QUICK_TYPE and QUICK_DELETED = 0";

        var rows = await db.EasySelectAsync<HadbSqlQuickRow>(query,
            new
            {
                QUICK_KEY = (startWith ? Str.SqlServerEscapeLikeToken(key, '?') + "%" : key),
                QUICK_SYSTEMNAME = this.SystemName,
                QUICK_NAMESPACE = nameSpace,
                QUICK_TYPE = typeName,
            },
            cancel: cancel);

        return await rows.ToList()._ProcessParallelAndAggregateAsync((rowPartList, taskIndex) =>
        {
            List<HadbQuick<T>> tmp = new List<HadbQuick<T>>();
            foreach (var row in rowPartList)
            {
                tmp.Add(new HadbQuick<T>(
                    row.QUICK_VALUE._JsonToObject<T>()!,
                    row.QUICK_UID,
                    row.QUICK_NAMESPACE,
                    row.QUICK_KEY,
                    row.QUICK_CREATE_DT,
                    row.QUICK_UPDATE_DT,
                    row.QUICK_SNAPSHOT_NO,
                    row.QUICK_UPDATE_COUNT));
            }
            return TR(tmp);
        },
        cancel: cancel);
    }

    protected internal override async Task AtomicUpdateQuickByUidOnDatabaseImplAsync<T>(HadbTran tran, string uid, string nameSpace, T userData, CancellationToken cancel = default)
    {
        uid = uid._NormalizeUid();
        nameSpace = nameSpace._HadbNameSpaceNormalize();
        string typeName = typeof(T).Name;

        tran.CheckIsWriteMode();
        var db = ((HadbSqlTran)tran).Db;

        // デッドロックを防ぐため、where 句が重要。手動で実行するのである。
        // READCOMMITTEDLOCK にしないと、パーティション分割している場合にデッドロックが頻発する。
        string query = "update HADB_QUICK with (READCOMMITTEDLOCK, ROWLOCK) " +
            "set QUICK_VALUE = @QUICK_VALUE, QUICK_UPDATE_DT = @QUICK_UPDATE_DT, QUICK_SNAPSHOT_NO = @QUICK_SNAPSHOT_NO, QUICK_UPDATE_COUNT = QUICK_UPDATE_COUNT + 1 " +
            "where QUICK_UID = @QUICK_UID and QUICK_SYSTEMNAME = @QUICK_SYSTEMNAME and QUICK_NAMESPACE = @QUICK_NAMESPACE and QUICK_TYPE = @QUICK_TYPE and QUICK_DELETED = 0";

        int r = await db.EasyExecuteAsync(query,
            new
            {
                QUICK_VALUE = userData._ObjectToJson(compact: true),
                QUICK_UPDATE_DT = DtOffsetNow,
                QUICK_UID = uid,
                QUICK_SYSTEMNAME = this.SystemName,
                QUICK_TYPE = typeName,
                QUICK_NAMESPACE = nameSpace,
                QUICK_SNAPSHOT_NO = tran.CurrentSnapNoForWriteMode,
            });

        if (r == 0)
        {
            throw new CoresLibException($"The specified UID '{uid}' not found on the database.");
        }
    }

    protected internal override async Task<bool> AtomicAddOrUpdateQuickByKeyOnDatabaseImplAsync<T>(HadbTran tran, string key, string nameSpace, T userData, bool doNotOverwrite, CancellationToken cancel = default)
    {
        key = key._NormalizeKey(true);
        nameSpace = nameSpace._HadbNameSpaceNormalize();
        string typeName = typeof(T).Name;

        var json = userData._ObjectToJson(compact: true);

        tran.CheckIsWriteMode();
        var db = ((HadbSqlTran)tran).Db;

        var now = DtOffsetNow;

        L_ADD:
        if (doNotOverwrite)
        {
            // 新規追加 (KEY が重複する場合は SQL Server 側でインデックス一意エラーになるので、KEY の一意性はチェックしなくてよい)
            HadbSqlQuickRow r = new HadbSqlQuickRow
            {
                QUICK_UID = Str.NewUid("QUICK", '_', this.Settings.OptionFlags.Bit(HadbOptionFlags.DataUidForPartitioningByUidOptimized)),
                QUICK_SYSTEMNAME = this.SystemName,
                QUICK_TYPE = typeName,
                QUICK_NAMESPACE = nameSpace,
                QUICK_DELETED = false,
                QUICK_KEY = key,
                QUICK_VALUE = json,
                QUICK_CREATE_DT = now,
                QUICK_UPDATE_DT = now,
                QUICK_DELETE_DT = DtOffsetZero,
                QUICK_SNAPSHOT_NO = tran.CurrentSnapNoForWriteMode,
                QUICK_UPDATE_COUNT = 1,
            };

            await db.EasyInsertAsync(r, cancel);

            return false;
        }

        // 既に存在する一致行を上書き
        // デッドロックを防ぐため、where 句が重要。手動で実行するのである。
        // READCOMMITTEDLOCK にしないと、パーティション分割している場合にデッドロックが頻発する。
        string query = "update HADB_QUICK with (READCOMMITTEDLOCK, ROWLOCK) " +
            "set QUICK_VALUE = @QUICK_VALUE, QUICK_UPDATE_DT = @QUICK_UPDATE_DT, QUICK_SNAPSHOT_NO = @QUICK_SNAPSHOT_NO, QUICK_UPDATE_COUNT = QUICK_UPDATE_COUNT + 1 " +
            "where QUICK_KEY = @QUICK_KEY and QUICK_SYSTEMNAME = @QUICK_SYSTEMNAME and QUICK_NAMESPACE = @QUICK_NAMESPACE and QUICK_TYPE = @QUICK_TYPE and QUICK_DELETED = 0";

        int i = await db.EasyExecuteAsync(
            query,
            new
            {
                QUICK_VALUE = json,
                QUICK_UPDATE_DT = now,
                QUICK_KEY = key,
                QUICK_SYSTEMNAME = this.SystemName,
                QUICK_NAMESPACE = nameSpace,
                QUICK_TYPE = typeName,
                QUICK_SNAPSHOT_NO = tran.CurrentSnapNoForWriteMode,
            });

        if (i <= 0)
        {
            // まだ存在しないので作成する
            doNotOverwrite = true;
            goto L_ADD;
        }

        return true;
    }

    protected internal override async Task<bool> AtomicDeleteQuickByUidOnDatabaseImplAsync<T>(HadbTran tran, string uid, string nameSpace, CancellationToken cancel = default)
    {
        uid = uid._NormalizeUid();
        nameSpace = nameSpace._HadbNameSpaceNormalize();
        string typeName = typeof(T).Name;

        tran.CheckIsWriteMode();
        var db = ((HadbSqlTran)tran).Db;

        // 既に存在する一致行を上書き
        // デッドロックを防ぐため、where 句が重要。手動で実行するのである。
        // READCOMMITTEDLOCK にしないと、パーティション分割している場合にデッドロックが頻発する。
        string query = $"update HADB_QUICK with (READCOMMITTEDLOCK, ROWLOCK) " +
            "set QUICK_DELETED = 1, QUICK_DELETE_DT = @QUICK_DELETE_DT " +
            "where QUICK_UID = @QUICK_UID and QUICK_SYSTEMNAME = @QUICK_SYSTEMNAME and QUICK_NAMESPACE = @QUICK_NAMESPACE and QUICK_TYPE = @QUICK_TYPE and QUICK_DELETED = 0";

        int i = await db.EasyExecuteAsync(query,
            new
            {
                QUICK_DELETE_DT = DtOffsetNow,
                QUICK_UID = uid,
                QUICK_SYSTEMNAME = this.SystemName,
                QUICK_NAMESPACE = nameSpace,
                QUICK_TYPE = typeName,
            });

        return i >= 1;
    }

    protected internal override async Task<int> AtomicDeleteQuickByKeyOnDatabaseImplAsync<T>(HadbTran tran, string key, bool startWith, string nameSpace, CancellationToken cancel = default)
    {
        key = key._NormalizeKey(true);
        nameSpace = nameSpace._HadbNameSpaceNormalize();
        string typeName = typeof(T).Name;

        tran.CheckIsWriteMode();
        var db = ((HadbSqlTran)tran).Db;

        // 既に存在する一致行を上書き
        // デッドロックを防ぐため、where 句が重要。手動で実行するのである。
        // READCOMMITTEDLOCK にしないと、パーティション分割している場合にデッドロックが頻発する。
        string query = $"update HADB_QUICK with (READCOMMITTEDLOCK, ROWLOCK) " +
        "set QUICK_DELETED = 1, QUICK_DELETE_DT = @QUICK_DELETE_DT " +
        $"where {(key._IsEmpty() ? "" : (startWith ? "QUICK_KEY like @QUICK_KEY escape '?' and" : "QUICK_KEY = @QUICK_KEY and"))} " +
        "QUICK_SYSTEMNAME = @QUICK_SYSTEMNAME and QUICK_NAMESPACE = @QUICK_NAMESPACE and QUICK_TYPE = @QUICK_TYPE and QUICK_DELETED = 0";

        int i = await db.EasyExecuteAsync(query,
            new
            {
                QUICK_DELETE_DT = DtOffsetNow,
                QUICK_KEY = (startWith ? Str.SqlServerEscapeLikeToken(key, '?') + "%" : key),
                QUICK_SYSTEMNAME = this.SystemName,
                QUICK_NAMESPACE = nameSpace,
                QUICK_TYPE = typeName,
            });

        return i;
    }

    protected async Task<HadbSqlDataRow?> GetRowByKeyAsync(Database db, string typeName, string nameSpace, HadbKeys key, bool lightLock, bool and, CancellationToken cancel = default)
    {
        nameSpace = nameSpace._HadbNameSpaceNormalize();

        List<string> conditions = new List<string>();

        if (key.Key1._IsFilled()) conditions.Add("DATA_ARCHIVE = 0 and DATA_DELETED = 0 and DATA_KEY1 != '' and DATA_KEY1 = @DATA_KEY1 and DATA_SYSTEMNAME = @DATA_SYSTEMNAME and DATA_NAMESPACE = @DATA_NAMESPACE and DATA_TYPE = @DATA_TYPE");
        if (key.Key2._IsFilled()) conditions.Add("DATA_ARCHIVE = 0 and DATA_DELETED = 0 and DATA_KEY2 != '' and DATA_KEY2 = @DATA_KEY2 and DATA_SYSTEMNAME = @DATA_SYSTEMNAME and DATA_NAMESPACE = @DATA_NAMESPACE and DATA_TYPE = @DATA_TYPE");
        if (key.Key3._IsFilled()) conditions.Add("DATA_ARCHIVE = 0 and DATA_DELETED = 0 and DATA_KEY3 != '' and DATA_KEY3 = @DATA_KEY3 and DATA_SYSTEMNAME = @DATA_SYSTEMNAME and DATA_NAMESPACE = @DATA_NAMESPACE and DATA_TYPE = @DATA_TYPE");
        if (key.Key4._IsFilled()) conditions.Add("DATA_ARCHIVE = 0 and DATA_DELETED = 0 and DATA_KEY4 != '' and DATA_KEY4 = @DATA_KEY4 and DATA_SYSTEMNAME = @DATA_SYSTEMNAME and DATA_NAMESPACE = @DATA_NAMESPACE and DATA_TYPE = @DATA_TYPE");
        if (key.Key5._IsFilled()) conditions.Add("DATA_ARCHIVE = 0 and DATA_DELETED = 0 and DATA_KEY5 != '' and DATA_KEY5 = @DATA_KEY5 and DATA_SYSTEMNAME = @DATA_SYSTEMNAME and DATA_NAMESPACE = @DATA_NAMESPACE and DATA_TYPE = @DATA_TYPE");

        if (conditions.Count == 0)
        {
            return null;
        }

        // READCOMMITTEDLOCK, ROWLOCK は、「トランザクション分離レベルが Snapshot かつ読み取り専用の場合」以外に付ける。
        string query = $"select * from HADB_DATA  {(lightLock ? "with (READCOMMITTEDLOCK, ROWLOCK)" : "")} where {conditions.Select(x => $" ( {x} )")._Combine(and ? " and " : " or ")}";

        return await db.EasySelectSingleAsync<HadbSqlDataRow>(query,
            new
            {
                DATA_KEY1 = key.Key1,
                DATA_KEY2 = key.Key2,
                DATA_KEY3 = key.Key3,
                DATA_KEY4 = key.Key4,
                DATA_KEY5 = key.Key5,
                DATA_SYSTEMNAME = this.SystemName,
                DATA_TYPE = typeName,
                DATA_NAMESPACE = nameSpace,
            },
            cancel: cancel,
            throwErrorIfMultipleFound: true);
    }

    protected async Task<IEnumerable<HadbSqlDataRow>> GetRowsByLabelsAsync(Database db, string typeName, string nameSpace, HadbLabels labels, bool lightLock, CancellationToken cancel = default)
    {
        nameSpace = nameSpace._HadbNameSpaceNormalize();
        List<string> conditions = new List<string>();

        if (labels.Label1._IsFilled()) conditions.Add("DATA_LABEL1 != '' and DATA_DELETED = 0 and DATA_ARCHIVE = 0 and DATA_LABEL1 = @DATA_LABEL1");
        if (labels.Label2._IsFilled()) conditions.Add("DATA_LABEL2 != '' and DATA_DELETED = 0 and DATA_ARCHIVE = 0 and DATA_LABEL2 = @DATA_LABEL2");
        if (labels.Label3._IsFilled()) conditions.Add("DATA_LABEL3 != '' and DATA_DELETED = 0 and DATA_ARCHIVE = 0 and DATA_LABEL3 = @DATA_LABEL3");
        if (labels.Label4._IsFilled()) conditions.Add("DATA_LABEL4 != '' and DATA_DELETED = 0 and DATA_ARCHIVE = 0 and DATA_LABEL4 = @DATA_LABEL4");
        if (labels.Label5._IsFilled()) conditions.Add("DATA_LABEL5 != '' and DATA_DELETED = 0 and DATA_ARCHIVE = 0 and DATA_LABEL5 = @DATA_LABEL5");

        if (conditions.Count == 0)
        {
            return EmptyOf<HadbSqlDataRow>();
        }

        // READCOMMITTEDLOCK, ROWLOCK は、「トランザクション分離レベルが Snapshot かつ読み取り専用の場合」以外に付ける。
        return await db.EasySelectAsync<HadbSqlDataRow>($"select * from HADB_DATA {(lightLock ? "with (READCOMMITTEDLOCK, ROWLOCK)" : "")} where ({conditions._Combine(" and ")}) and DATA_SYSTEMNAME = @DATA_SYSTEMNAME and DATA_TYPE = @DATA_TYPE and DATA_NAMESPACE = @DATA_NAMESPACE",
            new
            {
                DATA_LABEL1 = labels.Label1,
                DATA_LABEL2 = labels.Label2,
                DATA_LABEL3 = labels.Label3,
                DATA_LABEL4 = labels.Label4,
                DATA_LABEL5 = labels.Label5,
                DATA_SYSTEMNAME = this.SystemName,
                DATA_TYPE = typeName,
                DATA_NAMESPACE = nameSpace,
            },
            cancel: cancel);
    }

    protected async Task<HadbSqlDataRow?> GetRowByUidAsync(Database db, string typeName, string nameSpace, string uid, bool lightLock, CancellationToken cancel = default, bool noCheckTypeIdAndNameSpace = false)
    {
        nameSpace = nameSpace._HadbNameSpaceNormalize();

        uid = uid._NormalizeUid();

        if (uid._IsEmpty()) return null;

        string query =
            $"select * from HADB_DATA {(lightLock ? "with(READCOMMITTEDLOCK, ROWLOCK)" : "")} where DATA_UID = @DATA_UID and DATA_SYSTEMNAME = @DATA_SYSTEMNAME and DATA_DELETED = 0 and DATA_ARCHIVE = 0 ";

        if (noCheckTypeIdAndNameSpace == false)
        {
            query += " and DATA_TYPE = @DATA_TYPE and DATA_NAMESPACE = @DATA_NAMESPACE";
        }

        // READCOMMITTEDLOCK, ROWLOCK は、「トランザクション分離レベルが Snapshot かつ読み取り専用の場合」以外に付ける。
        return await db.EasySelectSingleAsync<HadbSqlDataRow>(query,
            new
            {
                DATA_UID = uid,
                DATA_SYSTEMNAME = this.SystemName,
                DATA_TYPE = typeName,
                DATA_NAMESPACE = nameSpace,
            },
            cancel: cancel,
            throwErrorIfMultipleFound: true);
    }

    protected internal override async Task<string> AtomicGetKvImplAsync(HadbTran tran, string key, CancellationToken cancel = default)
    {
        key = key._NormalizeKey(true);

        Database? dbReader = ((HadbSqlTran)tran).Db;

        var existingRow = await dbReader.EasySelectSingleAsync<HadbSqlKvRow>("select * from HADB_KV where KV_KEY = @KV_KEY and KV_SYSTEM_NAME = @KV_SYSTEM_NAME and KV_DELETED = 0",
            new
            {
                KV_KEY = key,
                KV_SYSTEM_NAME = this.SystemName,
            },
            cancel: cancel,
            throwErrorIfMultipleFound: true);

        if (existingRow != null)
        {
            return existingRow.KV_VALUE._NonNull();
        }

        return "";
    }

    protected internal override async Task<StrDictionary<string>> AtomicGetKvListImplAsync(HadbTran tran, CancellationToken cancel = default)
    {
        Database? dbReader = ((HadbSqlTran)tran).Db;

        var rows = await dbReader.EasySelectAsync<HadbSqlKvRow>("select * from HADB_KV where KV_SYSTEM_NAME = @KV_SYSTEM_NAME and KV_DELETED = 0",
            new
            {
                KV_SYSTEM_NAME = this.SystemName,
            },
            cancel: cancel);

        StrDictionary<string> ret = new StrDictionary<string>(StrCmpi);

        foreach (var row in rows)
        {
            ret.Add(row.KV_KEY, row.KV_VALUE);
        }

        return ret;
    }

    protected internal override async Task AtomicSetKvImplAsync(HadbTran tran, string key, string value, CancellationToken cancel = default)
    {
        key = key._NormalizeKey(true);
        value = value._NonNull();

        Database? dbWriter = ((HadbSqlTran)tran).Db;
        tran.CheckIsWriteMode();

        var now = DtOffsetNow;

        var row = await dbWriter.EasyFindOrInsertAsync<HadbSqlKvRow>("select * from HADB_KV where KV_KEY = @KV_KEY and KV_SYSTEM_NAME = @KV_SYSTEM_NAME and KV_DELETED = 0",
            new
            {
                KV_KEY = key,
                KV_SYSTEM_NAME = this.SystemName,
            },
            new HadbSqlKvRow
            {
                KV_SYSTEM_NAME = this.SystemName,
                KV_KEY = key,
                KV_VALUE = value,
                KV_DELETED = false,
                KV_CREATE_DT = now,
                KV_UPDATE_DT = now,
            }
        );

        if (row.KV_VALUE != value)
        {
            row.KV_UPDATE_DT = now;
            row.KV_VALUE = value;

            await dbWriter.EasyUpdateAsync(row, true, cancel);
        }
    }

    protected internal override async Task AtomicAddSnapImplAsync(HadbTran tran, HadbSnapshot snap, CancellationToken cancel = default)
    {
        tran.CheckIsWriteMode();

        var dbWriter = ((HadbSqlTran)tran).Db;

        HadbSqlSnapshotRow r = new HadbSqlSnapshotRow
        {
            SNAPSHOT_UID = snap.Uid,
            SNAPSHOT_SYSTEM_NAME = snap.SystemName,
            SNAPSHOT_NO = snap.Number,
            SNAPSHOT_DT = snap.TimeStamp,
            SNAPSHOT_DESCRIPTION = snap.Description,
            SNAPSHOT_EXT1 = snap.Ext1,
            SNAPSHOT_EXT2 = snap.Ext2,
        };

        await dbWriter.EasyInsertAsync(r, cancel);
    }

    protected internal override async Task AtomicAddDataListToDatabaseImplAsync(HadbTran tran, IEnumerable<HadbObject> dataList, CancellationToken cancel = default)
    {
        tran.CheckIsWriteMode();
        var dbWriter = ((HadbSqlTran)tran).Db;

        foreach (HadbObject data in dataList)
        {
            if (data.Deleted) throw new CoresLibException("data.Deleted == true");

            var keys = data.GetKeys();

            var labels = data.GetLabels();

            HadbSqlDataRow row = new HadbSqlDataRow
            {
                DATA_UID = data.Uid,
                DATA_SYSTEMNAME = this.SystemName,
                DATA_TYPE = data.GetUserDataTypeName(),
                DATA_VER = data.Ver,
                DATA_DELETED = false,
                DATA_ARCHIVE = false,
                DATA_SNAPSHOT_NO = data.SnapshotNo,
                DATA_NAMESPACE = data.NameSpace,
                DATA_CREATE_DT = data.CreateDt,
                DATA_UPDATE_DT = data.UpdateDt,
                DATA_DELETE_DT = data.DeleteDt,
                DATA_KEY1 = keys.Key1,
                DATA_KEY2 = keys.Key2,
                DATA_KEY3 = keys.Key3,
                DATA_KEY4 = keys.Key4,
                DATA_KEY5 = keys.Key5,
                DATA_LABEL1 = labels.Label1,
                DATA_LABEL2 = labels.Label2,
                DATA_LABEL3 = labels.Label3,
                DATA_LABEL4 = labels.Label4,
                DATA_LABEL5 = labels.Label5,
                DATA_VALUE = data.GetUserDataJsonString(),
                DATA_EXT1 = data.Ext1,
                DATA_EXT2 = data.Ext2,
                DATA_FT1 = data.Ft1,
                DATA_FT2 = data.Ft2,
                DATA_LAZY_COUNT1 = 0,
                DATA_LAZY_COUNT2 = 0,
                DATA_UID_ORIGINAL = data.Uid,
            };

            //// DB に書き込む前に DB 上で KEY1 ～ KEY5 の重複を検査する
            //var existingRow = await GetRowByKeyAsync(dbWriter, row.DATA_TYPE, row.DATA_NAMESPACE, keys, cancel);

            //if (existingRow != null)
            //{
            //    throw new CoresLibException($"Duplicated key in the physical database. Namespace = {data.NameSpace}, Keys = {keys._ObjectToJson(compact: true)}");
            //}

            // DB に書き込む
            await dbWriter.EasyInsertAsync(row, cancel);
        }
    }

    protected internal override async Task AtomicAddLogImplAsync(HadbTran tran, HadbLog log, string nameSpace, string ext1, string ext2, string ft1, string ft2, CancellationToken cancel = default)
    {
        nameSpace = nameSpace._HadbNameSpaceNormalize();
        tran.CheckIsWriteMode();
        var dbWriter = ((HadbSqlTran)tran).Db;

        string typeName = log.GetLogDataTypeName();
        string uid = Str.NewUid("LOG_" + typeName, '_');
        uid = uid._NormalizeUid();

        var label = log.GetLabels();

        HadbSqlLogRow row = new HadbSqlLogRow
        {
            LOG_UID = uid,
            LOG_SYSTEM_NAME = this.SystemName,
            LOG_TYPE = typeName,
            LOG_NAMESPACE = nameSpace,
            LOG_DT = DtOffsetNow,
            LOG_SNAP_NO = tran.CurrentSnapNoForWriteMode,
            LOG_DELETED = false,
            LOG_LABEL1 = label.Label1,
            LOG_LABEL2 = label.Label2,
            LOG_LABEL3 = label.Label3,
            LOG_LABEL4 = label.Label4,
            LOG_LABEL5 = label.Label5,
            LOG_VALUE = log.GetLogDataJsonString(),
            LOG_EXT1 = ext1._NonNull(),
            LOG_EXT2 = ext2._NonNull(),
            LOG_FT1 = ft1._NonNull(),
            LOG_FT2 = ft2._NonNull(),
        };

        row.Normalize();

        await dbWriter.EasyInsertAsync(row, cancel);
    }

    protected internal override async Task<HadbLogQueryResponse> AtomicSearchLogImplAsync(HadbTran tran, string typeName, HadbLogQuery query, string nameSpace, CancellationToken cancel = default)
    {
        typeName = typeName._NonNullTrim();
        query.Normalize();
        nameSpace = nameSpace._HadbNameSpaceNormalize();
        var db = ((HadbSqlTran)tran).Db;

        var paramList = new KeyValueList<string, object>();

        List<string> conditions = new List<string>();

        if (query.Uid._IsFilled()) conditions.Add("LOG_UID = @LOG_UID");
        if (query.TimeStart._IsZeroDateTime() == false) conditions.Add("LOG_DT >= @DT_START");
        if (query.TimeEnd._IsZeroDateTime() == false) conditions.Add("LOG_DT <= @DT_END");
        if (query.SnapshotNoStart > 0) conditions.Add("LOG_SNAP_NO >= @SNAP_START");
        if (query.SnapshotNoEnd > 0) conditions.Add("LOG_SNAP_NO <= @SNAP_END");

        var labels = query.Labels;
        if (labels.Label1._IsFilled()) conditions.Add("LOG_LABEL1 = @LOG_LABEL1");
        if (labels.Label2._IsFilled()) conditions.Add("LOG_LABEL2 = @LOG_LABEL2");
        if (labels.Label3._IsFilled()) conditions.Add("LOG_LABEL3 = @LOG_LABEL3");
        if (labels.Label4._IsFilled()) conditions.Add("LOG_LABEL4 = @LOG_LABEL4");
        if (labels.Label5._IsFilled()) conditions.Add("LOG_LABEL5 = @LOG_LABEL5");

        var labels2 = query.SearchTemplate?.GetLabels() ?? new HadbLabels("");
        if (labels2.Label1._IsFilled()) conditions.Add("LOG_LABEL1 = @LOG_LABEL1_02");
        if (labels2.Label2._IsFilled()) conditions.Add("LOG_LABEL2 = @LOG_LABEL2_02");
        if (labels2.Label3._IsFilled()) conditions.Add("LOG_LABEL3 = @LOG_LABEL3_02");
        if (labels2.Label4._IsFilled()) conditions.Add("LOG_LABEL4 = @LOG_LABEL4_02");
        if (labels2.Label5._IsFilled()) conditions.Add("LOG_LABEL5 = @LOG_LABEL5_02");

        paramList.Add("LOG_UID", query.Uid);
        paramList.Add("DT_START", query.TimeStart);
        paramList.Add("DT_END", query.TimeEnd);
        paramList.Add("SNAP_START", query.SnapshotNoStart);
        paramList.Add("SNAP_END", query.SnapshotNoEnd);

        paramList.Add("LOG_LABEL1", labels.Label1._NonNull());
        paramList.Add("LOG_LABEL2", labels.Label2._NonNull());
        paramList.Add("LOG_LABEL3", labels.Label3._NonNull());
        paramList.Add("LOG_LABEL4", labels.Label4._NonNull());
        paramList.Add("LOG_LABEL5", labels.Label5._NonNull());

        paramList.Add("LOG_LABEL1_02", labels2.Label1._NonNull());
        paramList.Add("LOG_LABEL2_02", labels2.Label2._NonNull());
        paramList.Add("LOG_LABEL3_02", labels2.Label3._NonNull());
        paramList.Add("LOG_LABEL4_02", labels2.Label4._NonNull());
        paramList.Add("LOG_LABEL5_02", labels2.Label5._NonNull());

        paramList.Add("LOG_SYSTEM_NAME", this.SystemName);
        paramList.Add("LOG_TYPE", typeName);
        paramList.Add("LOG_NAMESPACE", nameSpace);

        if (query.FullTextQuery._IsFilled())
        {
            var ft = FullTextSearchQuery.ParseText(query.FullTextQuery, query.FullTextFlags);

            ft.GenerateSqlLikeConditions(query.FullTextFlags.Bit(FullTextSearchFlags.FieldNameMode) ? "LOG_FT2" : "LOG_FT1", out var ftConditionStr, out var ftParamList);

            conditions.Add(ftConditionStr);
            ftParamList.ForEach(x => paramList.Add(x.Key, x.Value));
        }

        if (conditions.Count == 0) conditions.Add("1 = 1");

        List<string> sqlWithStrList = new List<string>();

        if (query.Flags.Bit(HadbLogQueryFlags.UseIndexTime) && this.SqlDbCaps.Bit(HadbSqlDbCaps.HadbLog_HasLogDtIndex))
        {
            sqlWithStrList.Add("index(LOG_DT)");
        }

        if (query.Flags.Bit(HadbLogQueryFlags.WithNoLock))
        {
            sqlWithStrList.Add("nolock");
        }

        string sqlWithStr = "";
        if (sqlWithStrList.Any())
        {
            sqlWithStr = $"with({sqlWithStrList._Combine(",")})";
        }

        string qstr = $"select {(query.MaxReturmItems >= 1 ? $"top {query.MaxReturmItems}" : "")} {(query.RetOnlyCount ? "count(LOG_ID) as RET_COUNT" : "*")} from HADB_LOG {sqlWithStr} where {conditions._Combine(" and ")} and LOG_SYSTEM_NAME = @LOG_SYSTEM_NAME and LOG_TYPE = @LOG_TYPE and LOG_NAMESPACE = @LOG_NAMESPACE and LOG_DELETED = 0 {(query.RetOnlyCount ? " " : "order by LOG_ID desc")}";

        if (query.RetOnlyCount == false)
        {
            var rows = await db.EasySelectAsync<HadbSqlLogRow>(qstr,
                paramList,
                cancel: cancel);

            List<HadbLog> ret = new List<HadbLog>();

            foreach (var row in rows)
            {
                ret.Add(this.JsonToHadbLog(row.LOG_VALUE, typeName));
            }

            return new HadbLogQueryResponse(ret);
        }
        else
        {
            int count = await db.ExecuteScalarAsync<int>(qstr, paramList);

            return new HadbLogQueryResponse(count);
        }
    }

    protected internal override async Task<bool> LazyUpdateImplAsync(HadbTran tran, HadbObject data, CancellationToken cancel = default)
    {
        data.CheckIsNotMemoryDbObject();
        tran.CheckIsWriteMode();
        var dbWriter = ((HadbSqlTran)tran).Db;

        string typeName = data.GetUserDataTypeName();

        var keys = data.GetKeys();
        var labels = data.GetLabels();
        if (data.Deleted) throw new CoresLibException("data.Deleted == true");

        string query = "update HADB_DATA with (ROWLOCK) set DATA_VALUE = @DATA_VALUE, DATA_FT1 = @DATA_FT1, DATA_FT2 = @DATA_FT2, DATA_UPDATE_DT = @DATA_UPDATE_DT, DATA_LAZY_COUNT1 = DATA_LAZY_COUNT1 + 1, DATA_LAZY_COUNT2 = DATA_LAZY_COUNT2 + 1 " +
            "where DATA_UID = @DATA_UID and DATA_SYSTEMNAME = @DATA_SYSTEMNAME and DATA_VER = @DATA_VER and DATA_UPDATE_DT < @DATA_UPDATE_DT and DATA_TYPE = @DATA_TYPE and DATA_ARCHIVE = 0 and DATA_DELETED = 0 and " +
            "DATA_KEY1 = @DATA_KEY1 and DATA_KEY2 = @DATA_KEY2 and DATA_KEY3 = @DATA_KEY3 and DATA_KEY4 = @DATA_KEY4 and DATA_KEY5 = @DATA_KEY5 and " +
            "DATA_LABEL1 = @DATA_LABEL1 and DATA_LABEL2 = @DATA_LABEL2 and DATA_LABEL3 = @DATA_LABEL3 and DATA_LABEL4 = @DATA_LABEL4 and DATA_LABEL5 = @DATA_LABEL5";

        // 毎回、大変短いトランザクションを実行したことにする
        query = "BEGIN TRANSACTION \n" + query + "\n COMMIT TRANSACTION\n";

        int ret = await dbWriter.EasyExecuteAsync(query,
            new
            {
                DATA_VALUE = data.GetUserDataJsonString(),
                DATA_UPDATE_DT = DtOffsetNow,
                DATA_UID = data.Uid,
                DATA_VER = data.Ver,
                DATA_SYSTEMNAME = this.SystemName,
                DATA_TYPE = typeName,
                DATA_KEY1 = keys.Key1,
                DATA_KEY2 = keys.Key2,
                DATA_KEY3 = keys.Key3,
                DATA_KEY4 = keys.Key4,
                DATA_KEY5 = keys.Key5,
                DATA_LABEL1 = labels.Label1,
                DATA_LABEL2 = labels.Label2,
                DATA_LABEL3 = labels.Label3,
                DATA_LABEL4 = labels.Label4,
                DATA_LABEL5 = labels.Label5,
                DATA_FT1 = data.Ft1,
                DATA_FT2 = data.Ft2,
            });

        return ret >= 1;
    }

    protected internal override async Task<HadbObject> AtomicUpdateDataOnDatabaseImplAsync(HadbTran tran, HadbObject data, CancellationToken cancel = default)
    {
        int maxArchive = Math.Max(data.GetData<HadbData>().GetMaxArchivedCount(), 0);

        data.CheckIsNotMemoryDbObject();
        tran.CheckIsWriteMode();
        var dbWriter = ((HadbSqlTran)tran).Db;

        string typeName = data.GetUserDataTypeName();

        var keys = data.GetKeys();

        var labels = data.GetLabels();

        if (data.Deleted) throw new CoresLibException("data.Deleted == true");

        // 現在のデータを取得
        var row = await this.GetRowByUidAsync(dbWriter, typeName, data.NameSpace, data.Uid, tran.ShouldUseLightLock, cancel);
        if (row == null)
        {
            // 現在のデータがない
            throw new CoresLibException($"No data existing in the physical database. Uid = {data.Uid}, TypeName = {typeName}");
        }

        long newVer = row.DATA_VER + 1;

        if (true)
        {
            //// 現在のアーカイブを一段繰り上げる
            //await dbWriter.EasyExecuteAsync("update HADB_DATA set DATA_ARCHIVE = DATA_ARCHIVE + 1 where DATA_UID like @DATA_UID and DATA_SYSTEMNAME = @DATA_SYSTEMNAME and DATA_TYPE = @DATA_TYPE and DATA_ARCHIVE >= 1 and DATA_NAMESPACE = @DATA_NAMESPACE",
            //    new
            //    {
            //        DATA_UID = data.Uid + ":%",
            //        DATA_SYSTEMNAME = this.SystemName,
            //        DATA_TYPE = typeName,
            //        DATA_NAMESPACE = data.NameSpace,
            //    });

            if (maxArchive >= 1)
            {
                // 現在のデータをアーカイブ化する
                HadbSqlDataRow rowOld = row._CloneDeep();

                rowOld.DATA_UID_ORIGINAL = rowOld.DATA_UID;
                rowOld.DATA_UID += ":" + rowOld.DATA_VER.ToString("D20");
                rowOld.DATA_ARCHIVE = true;
                rowOld.Normalize();

                await dbWriter.EasyInsertAsync(rowOld, cancel);

                // 古いアーカイブを物理的に消す
                if (maxArchive != int.MaxValue)
                {
                    long threshold = newVer - maxArchive;

                    if (threshold >= 1)
                    {
                        // 物理的なデータベースから古いアーカイブを削除する。
                        // ここで、DELETE 句で where 条件式で広く指定するとロック範囲が広範囲となりデッドロック発生の原因となるので、
                        // 代わりに nolock の select で削除すべきデータを列挙してから主キーを用いて手動で 1 つずつ削除を実施する。
                        // (通常は削除されるデータは 1 個だけなので問題にはならないはずである。)
                        var physicalDeleteUidList = await dbWriter.EasyQueryAsync<string>("select DATA_UID from HADB_DATA with (NOLOCK) where DATA_UID != @DATA_UID and DATA_ARCHIVE = 1 and DATA_SYSTEMNAME = @DATA_SYSTEMNAME and DATA_UID_ORIGINAL = @DATA_UID and DATA_VER < @DATA_VER_THRESHOLD and DATA_SNAPSHOT_NO = @DATA_SNAPSHOT_NO",
                            new
                            {
                                DATA_UID = data.Uid,
                                DATA_SYSTEMNAME = this.SystemName,
                                DATA_TYPE = typeName,
                                DATA_NAMESPACE = data.NameSpace,
                                DATA_VER_THRESHOLD = threshold,
                                DATA_SNAPSHOT_NO = tran.CurrentSnapNoForWriteMode,
                            }
                        );

                        foreach (var physicalDeleteUid in physicalDeleteUidList.Where(x => x._InStr(":")))
                        {
                            await dbWriter.EasyExecuteAsync("delete from HADB_DATA with (READCOMMITTEDLOCK, ROWLOCK) where DATA_UID = @DATA_UID and DATA_SYSTEMNAME = @DATA_SYSTEMNAME",
                                new
                                {
                                    DATA_UID = physicalDeleteUid,
                                    DATA_SYSTEMNAME = this.SystemName,
                                });
                        }
                    }
                }
            }
        }

        // DB に書き込む前に DB 上で KEY1 ～ KEY4 の重複を検査する (当然、更新しようとしている自分自身への重複は例外的に許可する)
        //var existingRow = await GetRowByKeyAsync(dbWriter, typeName, data.NameSpace, keys, cancel, excludeUid: row.DATA_UID);

        //if (existingRow != null)
        //{
        //    throw new CoresLibException($"Duplicated key in the physical database. Namespace = {data.NameSpace}, Keys = {keys._ObjectToJson(compact: true)}");
        //}

        // データの内容を更新する
        row.DATA_VER = newVer;
        row.DATA_UPDATE_DT = DtOffsetNow;
        row.DATA_KEY1 = keys.Key1;
        row.DATA_KEY2 = keys.Key2;
        row.DATA_KEY3 = keys.Key3;
        row.DATA_KEY4 = keys.Key4;
        row.DATA_KEY5 = keys.Key5;
        row.DATA_LABEL1 = labels.Label1;
        row.DATA_LABEL2 = labels.Label2;
        row.DATA_LABEL3 = labels.Label3;
        row.DATA_LABEL4 = labels.Label4;
        row.DATA_LABEL5 = labels.Label5;
        row.DATA_VALUE = data.GetUserDataJsonString();
        row.DATA_EXT1 = data.Ext1;
        row.DATA_EXT2 = data.Ext2;
        row.DATA_FT1 = data.Ft1;
        row.DATA_FT2 = data.Ft2;
        row.DATA_LAZY_COUNT1 = 0;
        row.DATA_SNAPSHOT_NO = tran.CurrentSnapNoForWriteMode;
        row.DATA_NAMESPACE = data.NameSpace;

        //await dbWriter.EasyUpdateAsync(row, true, cancel);

        // デッドロックを防ぐため、where 句が重要。手動で実行するのである。
        // READCOMMITTEDLOCK にしないと、パーティション分割している場合にデッドロックが頻発する。
        await dbWriter.EasyExecuteAsync("update HADB_DATA with (READCOMMITTEDLOCK, ROWLOCK) set DATA_VER = @DATA_VER, DATA_UPDATE_DT = @DATA_UPDATE_DT, " +
            "DATA_KEY1 = @DATA_KEY1, DATA_KEY2 = @DATA_KEY2, DATA_KEY3 = @DATA_KEY3, DATA_KEY4 = @DATA_KEY4, DATA_KEY5 = @DATA_KEY5, " +
            "DATA_LABEL1 = @DATA_LABEL1, DATA_LABEL2 = @DATA_LABEL2, DATA_LABEL3 = @DATA_LABEL3, DATA_LABEL4 = @DATA_LABEL4, DATA_LABEL5 = @DATA_LABEL5, " +
            "DATA_VALUE = @DATA_VALUE, DATA_EXT1 = @DATA_EXT1, DATA_EXT2 = @DATA_EXT2, DATA_FT1 = @DATA_FT1, DATA_FT2 = @DATA_FT2, " +
            "DATA_LAZY_COUNT1 = @DATA_LAZY_COUNT1, DATA_SNAPSHOT_NO = @DATA_SNAPSHOT_NO, DATA_NAMESPACE = @DATA_NAMESPACE " +
            "where DATA_UID = @DATA_UID and DATA_SYSTEMNAME = @DATA_SYSTEMNAME and DATA_DELETED = 0 and DATA_ARCHIVE = 0 and DATA_TYPE = @DATA_TYPE and DATA_NAMESPACE = @DATA_NAMESPACE",
            new
            {
                DATA_VER = row.DATA_VER,
                DATA_UPDATE_DT = row.DATA_UPDATE_DT,
                DATA_KEY1 = row.DATA_KEY1,
                DATA_KEY2 = row.DATA_KEY2,
                DATA_KEY3 = row.DATA_KEY3,
                DATA_KEY4 = row.DATA_KEY4,
                DATA_KEY5 = row.DATA_KEY5,
                DATA_LABEL1 = row.DATA_LABEL1,
                DATA_LABEL2 = row.DATA_LABEL2,
                DATA_LABEL3 = row.DATA_LABEL3,
                DATA_LABEL4 = row.DATA_LABEL4,
                DATA_LABEL5 = row.DATA_LABEL5,
                DATA_VALUE = row.DATA_VALUE,
                DATA_EXT1 = row.DATA_EXT1,
                DATA_EXT2 = row.DATA_EXT2,
                DATA_FT1 = row.DATA_FT1,
                DATA_FT2 = row.DATA_FT2,
                DATA_LAZY_COUNT1 = row.DATA_LAZY_COUNT1,
                DATA_SNAPSHOT_NO = row.DATA_SNAPSHOT_NO,

                DATA_UID = row.DATA_UID,
                DATA_SYSTEMNAME = row.DATA_SYSTEMNAME,
                DATA_TYPE = row.DATA_TYPE,
                DATA_NAMESPACE = row.DATA_NAMESPACE,
            });

        return new HadbObject(this.JsonToHadbData(row.DATA_VALUE, typeName), this.Settings.OptionFlags, row.DATA_EXT1, row.DATA_EXT2, row.DATA_FT1, row.DATA_FT2, row.DATA_UID, row.DATA_VER, row.DATA_ARCHIVE, row.DATA_SNAPSHOT_NO, row.DATA_NAMESPACE, row.DATA_DELETED, row.DATA_CREATE_DT, row.DATA_UPDATE_DT, row.DATA_DELETE_DT);
    }

    protected internal override async Task<HadbObject?> AtomicGetDataFromDatabaseImplAsync(HadbTran tran, string uid, string typeName, string nameSpace, CancellationToken cancel = default, bool noCheckTypeIdAndNameSpace = false)
    {
        nameSpace = nameSpace._HadbNameSpaceNormalize();
        typeName = typeName._NonNullTrim();

        var dbReader = ((HadbSqlTran)tran).Db;

        HadbSqlDataRow? row = await GetRowByUidAsync(dbReader, typeName, nameSpace, uid, tran.ShouldUseLightLock, cancel, noCheckTypeIdAndNameSpace);

        if (row == null) return null;

        if (noCheckTypeIdAndNameSpace && typeName._IsEmpty())
        {
            typeName = row.DATA_TYPE;
        }

        HadbObject ret = new HadbObject(this.JsonToHadbData(row.DATA_VALUE, typeName), this.Settings.OptionFlags, row.DATA_EXT1, row.DATA_EXT2, row.DATA_FT1, row.DATA_FT2, row.DATA_UID, row.DATA_VER, row.DATA_ARCHIVE, row.DATA_SNAPSHOT_NO, row.DATA_NAMESPACE, row.DATA_DELETED, row.DATA_CREATE_DT, row.DATA_UPDATE_DT, row.DATA_DELETE_DT);

        return ret;
    }

    protected internal override async Task<IEnumerable<HadbObject>> AtomicGetArchivedDataFromDatabaseImplAsync(HadbTran tran, int maxItems, string uid, string typeName, string nameSpace, CancellationToken cancel = default, bool noCheckTypeIdAndNameSpace = false)
    {
        nameSpace = nameSpace._HadbNameSpaceNormalize();
        typeName = typeName._NonNullTrim();

        if (maxItems <= 0) return EmptyOf<HadbObject>();

        var db = ((HadbSqlTran)tran).Db;

        uid = uid._NormalizeUid();

        if (uid._IsEmpty()) return EmptyOf<HadbObject>();

        string query =
            $"select top {maxItems} * from HADB_DATA where (DATA_UID = @DATA_UID or DATA_UID_ORIGINAL = @DATA_UID) and DATA_SYSTEMNAME = @DATA_SYSTEMNAME ";

        if (noCheckTypeIdAndNameSpace == false)
        {
            query += " and DATA_TYPE = @DATA_TYPE and DATA_NAMESPACE = @DATA_NAMESPACE ";
        }

        query += "order by DATA_VER desc";

        var rows = await db.EasySelectAsync<HadbSqlDataRow>(query,
            new
            {
                DATA_UID = uid,
                DATA_SYSTEMNAME = this.SystemName,
                DATA_TYPE = typeName,
                DATA_NAMESPACE = nameSpace,
            },
            cancel: cancel);

        rows = rows.OrderByDescending(x => x.DATA_VER); // 念のため

        return await rows.ToList()._ProcessParallelAndAggregateAsync((partOfRows, taskIndex) =>
        {
            List<HadbObject> tmp = new List<HadbObject>();
            foreach (var row in partOfRows)
            {
                if (typeName._IsEmpty() && noCheckTypeIdAndNameSpace)
                {
                    typeName = row.DATA_TYPE;
                }

                tmp.Add(new HadbObject(this.JsonToHadbData(row.DATA_VALUE, typeName), this.Settings.OptionFlags, row.DATA_EXT1, row.DATA_EXT2, row.DATA_FT1, row.DATA_FT2, row.DATA_UID, row.DATA_VER, row.DATA_ARCHIVE, row.DATA_SNAPSHOT_NO, row.DATA_NAMESPACE, row.DATA_DELETED, row.DATA_CREATE_DT, row.DATA_UPDATE_DT, row.DATA_DELETE_DT));
            }
            return TR(tmp);
        },
        cancel: cancel);
    }

    protected internal override async Task<HadbObject?> AtomicSearchDataByKeyFromDatabaseImplAsync(HadbTran tran, HadbKeys key, string typeName, string nameSpace, bool and, CancellationToken cancel = default)
    {
        nameSpace = nameSpace._HadbNameSpaceNormalize();
        typeName = typeName._NonNullTrim();

        var dbReader = ((HadbSqlTran)tran).Db;

        var row = await GetRowByKeyAsync(dbReader, typeName, nameSpace, key, tran.ShouldUseLightLock, and, cancel);

        if (row == null) return null;

        HadbObject ret = new HadbObject(this.JsonToHadbData(row.DATA_VALUE, typeName), this.Settings.OptionFlags, row.DATA_EXT1, row.DATA_EXT2, row.DATA_FT1, row.DATA_FT2, row.DATA_UID, row.DATA_VER, row.DATA_ARCHIVE, row.DATA_SNAPSHOT_NO, row.DATA_NAMESPACE, row.DATA_DELETED, row.DATA_CREATE_DT, row.DATA_UPDATE_DT, row.DATA_DELETE_DT);

        return ret;
    }

    protected internal override async Task<IEnumerable<HadbObject>> AtomicSearchDataListByLabelsFromDatabaseImplAsync(HadbTran tran, HadbLabels labels, string typeName, string nameSpace, CancellationToken cancel = default)
    {
        nameSpace = nameSpace._HadbNameSpaceNormalize();

        typeName = typeName._NonNullTrim();

        var dbReader = ((HadbSqlTran)tran).Db;

        IEnumerable<HadbSqlDataRow> rows = await GetRowsByLabelsAsync(dbReader, typeName, nameSpace, labels, tran.ShouldUseLightLock, cancel);

        if (rows == null) return EmptyOf<HadbObject>();

        return await rows.ToList()._ProcessParallelAndAggregateAsync((partOfRows, taskIndex) =>
        {
            List<HadbObject> tmp = new List<HadbObject>();
            foreach (var row in partOfRows)
            {
                tmp.Add(new HadbObject(this.JsonToHadbData(row.DATA_VALUE, typeName), this.Settings.OptionFlags, row.DATA_EXT1, row.DATA_EXT2, row.DATA_FT1, row.DATA_FT2, row.DATA_UID, row.DATA_VER, row.DATA_ARCHIVE, row.DATA_SNAPSHOT_NO, row.DATA_NAMESPACE, row.DATA_DELETED, row.DATA_CREATE_DT, row.DATA_UPDATE_DT, row.DATA_DELETE_DT));
            }
            return TR(tmp);
        },
        cancel: cancel);
    }

    protected internal override async Task<HadbObject> AtomicDeleteDataFromDatabaseImplAsync(HadbTran tran, string uid, string typeName, string nameSpace, int maxArchive, CancellationToken cancel = default)
    {
        nameSpace = nameSpace._HadbNameSpaceNormalize();
        typeName = typeName._NonNullTrim();
        uid = uid._NormalizeUid();

        tran.CheckIsWriteMode();
        var dbWriter = ((HadbSqlTran)tran).Db;

        HadbSqlDataRow? row = await GetRowByUidAsync(dbWriter, typeName, nameSpace, uid, tran.ShouldUseLightLock, cancel);

        if (row == null)
        {
            // 現在のデータがない
            throw new CoresLibException($"No data existing in the physical database. Uid = {uid}, TypeName = {typeName}");
        }

        long newVer = row.DATA_VER + 1;

        if (true)
        {
            //// 現在のアーカイブを一段繰り上げる
            //await dbWriter.EasyExecuteAsync("update HADB_DATA set DATA_ARCHIVE = DATA_ARCHIVE + 1 where DATA_UID like @DATA_UID and DATA_SYSTEMNAME = @DATA_SYSTEMNAME and DATA_TYPE = @DATA_TYPE and DATA_ARCHIVE >= 1 and DATA_NAMESPACE = @DATA_NAMESPACE",
            //    new
            //    {
            //        DATA_UID = data.Uid + ":%",
            //        DATA_SYSTEMNAME = this.SystemName,
            //        DATA_TYPE = typeName,
            //        DATA_NAMESPACE = data.NameSpace,
            //    });

            if (maxArchive >= 1)
            {
                // 現在のデータをアーカイブ化する
                HadbSqlDataRow rowOld = row._CloneDeep();

                rowOld.DATA_UID_ORIGINAL = rowOld.DATA_UID;
                rowOld.DATA_UID += ":" + rowOld.DATA_VER.ToString("D20");
                rowOld.DATA_ARCHIVE = true;
                rowOld.Normalize();

                await dbWriter.EasyInsertAsync(rowOld, cancel);

                // 古いアーカイブを物理的に消す
                if (maxArchive != int.MaxValue)
                {
                    long threshold = newVer - maxArchive;

                    if (threshold >= 1)
                    {
                        // 物理的なデータベースから古いアーカイブを削除する。
                        // ここで、DELETE 句で where 条件式で広く指定するとロック範囲が広範囲となりデッドロック発生の原因となるので、
                        // 代わりに nolock の select で削除すべきデータを列挙してから主キーを用いて手動で 1 つずつ削除を実施する。
                        // (通常は削除されるデータは 1 個だけなので問題にはならないはずである。)
                        var physicalDeleteUidList = await dbWriter.EasyQueryAsync<string>("select DATA_UID from HADB_DATA with (NOLOCK) where DATA_UID != @DATA_UID and DATA_ARCHIVE = 1 and DATA_SYSTEMNAME = @DATA_SYSTEMNAME and DATA_UID_ORIGINAL = @DATA_UID and DATA_VER < @DATA_VER_THRESHOLD and DATA_SNAPSHOT_NO = @DATA_SNAPSHOT_NO",
                            new
                            {
                                DATA_UID = uid,
                                DATA_SYSTEMNAME = this.SystemName,
                                DATA_TYPE = typeName,
                                DATA_NAMESPACE = nameSpace,
                                DATA_VER_THRESHOLD = threshold,
                                DATA_SNAPSHOT_NO = tran.CurrentSnapNoForWriteMode,
                            }
                        );

                        foreach (var physicalDeleteUid in physicalDeleteUidList.Where(x => x._InStr(":")))
                        {
                            await dbWriter.EasyExecuteAsync("delete from HADB_DATA with (READCOMMITTEDLOCK, ROWLOCK) where DATA_UID = @DATA_UID and DATA_SYSTEMNAME = @DATA_SYSTEMNAME",
                                new
                                {
                                    DATA_UID = physicalDeleteUid,
                                    DATA_SYSTEMNAME = this.SystemName,
                                });
                        }
                    }
                }
            }
        }

        // データを削除済みにする
        row.DATA_VER = newVer;
        row.DATA_UPDATE_DT = row.DATA_DELETE_DT = DtOffsetNow;
        row.DATA_DELETED = true;
        row.DATA_LAZY_COUNT1 = 0;
        row.DATA_SNAPSHOT_NO = tran.CurrentSnapNoForWriteMode;

        //await dbWriter.EasyUpdateAsync(row, true, cancel);

        // デッドロックを防ぐため、where 句が重要。手動で実行するのである。
        await dbWriter.EasyExecuteAsync("update HADB_DATA with (ROWLOCK) set DATA_VER = @DATA_VER, DATA_UPDATE_DT = @DATA_UPDATE_DT, DATA_DELETE_DT = @DATA_DELETE_DT, DATA_DELETED = @DATA_DELETED, " +
            "DATA_LAZY_COUNT1 = @DATA_LAZY_COUNT1, DATA_SNAPSHOT_NO = @DATA_SNAPSHOT_NO " +
            "where DATA_UID = @DATA_UID and DATA_SYSTEMNAME = @DATA_SYSTEMNAME and DATA_DELETED = 0 and DATA_ARCHIVE = 0 and DATA_TYPE = @DATA_TYPE and DATA_NAMESPACE = @DATA_NAMESPACE",
            new
            {
                DATA_VER = row.DATA_VER,
                DATA_UPDATE_DT = row.DATA_UPDATE_DT,
                DATA_DELETE_DT = row.DATA_DELETE_DT,
                DATA_DELETED = row.DATA_DELETED,
                DATA_LAZY_COUNT1 = row.DATA_LAZY_COUNT1,
                DATA_SNAPSHOT_NO = row.DATA_SNAPSHOT_NO,
                DATA_UID = row.DATA_UID,
                DATA_SYSTEMNAME = row.DATA_SYSTEMNAME,
                DATA_TYPE = row.DATA_TYPE,
                DATA_NAMESPACE = row.DATA_NAMESPACE,
            }
            );

        return new HadbObject(this.JsonToHadbData(row.DATA_VALUE, typeName), this.Settings.OptionFlags, row.DATA_EXT1, row.DATA_EXT2, row.DATA_FT1, row.DATA_FT2, row.DATA_UID, row.DATA_VER, row.DATA_ARCHIVE, row.DATA_SNAPSHOT_NO, row.DATA_NAMESPACE, row.DATA_DELETED, row.DATA_CREATE_DT, row.DATA_UPDATE_DT, row.DATA_DELETE_DT);
    }





    public class HadbSqlTran : HadbTran
    {
        public Database Db { get; }

        public HadbSqlTran(bool writeMode, bool isTransaction, HadbSqlBase<TMem, TDynamicConfig> hadbSql, Database db, bool isDbSnapshotReadMode, HadbTranOptions options) : base(writeMode, isTransaction, isDbSnapshotReadMode, options, hadbSql)
        {
            try
            {
                this.Db = db;
            }
            catch (Exception ex)
            {
                this._DisposeSafe(ex);
                throw;
            }
        }

        protected override async Task CommitImplAsync(CancellationToken cancel)
        {
            await this.Db.CommitAsync(cancel);
        }

        protected override async Task CleanupImplAsync(Exception? ex)
        {
            try
            {
                await this.Db._DisposeSafeAsync();
            }
            finally
            {
                await base.CleanupImplAsync(ex);
            }
        }
    }
}

[Flags]
public enum HadbOptionFlags : long
{
    None = 0,
    NoAutoDbReloadAndUpdate = 1,
    NoInitConfigDb = 2,
    NoInitSnapshot = 4,
    DoNotTakeSnapshotAtAll = 8,
    NoMemDb = 16,
    DoNotSaveStat = 32,
    NoLocalBackup = 64,
    DataUidForPartitioningByUidOptimized = 128,
    BuildFullTextSearchText = 256,
}

[Flags]
public enum HadbDebugFlags : long
{
    None = 0,
    NoCheckMemKeyDuplicate = 1,
    CauseErrorOnDatabaseReload = 2,
    NoDbLoadAndUseOnlyLocalBackup = 4,
}

public abstract class HadbSettingsBase // CloneDeep 禁止
{
    public string SystemName { get; }
    public HadbOptionFlags OptionFlags { get; }
    public FilePath BackupDataFile { get; }
    public FilePath BackupDynamicConfigFile { get; }
    public int LazyUpdateParallelQueueCount { get; }
    public int CommandTimeoutSecs { get; }
    public int DebugMaxFetchItems { get; }

    public HadbSettingsBase(string systemName, HadbOptionFlags optionFlags = HadbOptionFlags.None, FilePath? backupDataFile = null, FilePath? backupDynamicConfigFile = null, int lazyUpdateParallelQueueCount = Consts.Numbers.HadbDefaultLazyUpdateParallelQueueCount, int commandTimeoutSecs = 0, int debugMaxFetchItems = 0)
    {
        if (systemName._IsEmpty()) throw new CoresLibException("systemName is empty.");
        this.SystemName = systemName._NonNullTrim().ToUpperInvariant();
        this.OptionFlags = optionFlags;

        if (lazyUpdateParallelQueueCount <= 0) lazyUpdateParallelQueueCount = 1;
        if (lazyUpdateParallelQueueCount > 256) lazyUpdateParallelQueueCount = Consts.Numbers.HadbMaxLazyUpdateParallelQueueCount;

        this.LazyUpdateParallelQueueCount = lazyUpdateParallelQueueCount;

        if (backupDataFile == null)
        {
            backupDataFile = new FilePath(Env.AppLocalDir._CombinePath("HadbBackup", this.SystemName._MakeVerySafeAsciiOnlyNonSpaceFileName())._CombinePath(Consts.FileNames.HadbBackupDatabaseFileName));
        }

        if (backupDynamicConfigFile == null)
        {
            backupDynamicConfigFile = new FilePath(Env.AppLocalDir._CombinePath("HadbBackup", this.SystemName._MakeVerySafeAsciiOnlyNonSpaceFileName())._CombinePath(Consts.FileNames.HadbBackupDynamicConfigFileName));
        }

        this.BackupDataFile = backupDataFile;
        this.BackupDynamicConfigFile = backupDynamicConfigFile;

        this.CommandTimeoutSecs = (commandTimeoutSecs <= 0 ? CoresConfig.Hadb.DefaultHadbDatabaseCommandTimeoutSecs : commandTimeoutSecs);

        this.DebugMaxFetchItems = debugMaxFetchItems;
        if (this.DebugMaxFetchItems < 0) this.DebugMaxFetchItems = 0;
    }
}

public struct HadbKeys : IEquatable<HadbKeys>
{
    public string Key1 { get; }
    public string Key2 { get; }
    public string Key3 { get; }
    public string Key4 { get; }
    public string Key5 { get; }

    public HadbKeys(string key1, string? key2 = null, string? key3 = null, string? key4 = null, string? key5 = null)
    {
        this.Key1 = key1._NormalizeKey(true);
        this.Key2 = key2._NormalizeKey(true);
        this.Key3 = key3._NormalizeKey(true);
        this.Key4 = key4._NormalizeKey(true);
        this.Key5 = key5._NormalizeKey(true);
    }

    public bool Equals(HadbKeys other)
    {
        if (this.Key1._IsSamei(other.Key1) == false) return false;
        if (this.Key2._IsSamei(other.Key2) == false) return false;
        if (this.Key3._IsSamei(other.Key3) == false) return false;
        if (this.Key4._IsSamei(other.Key4) == false) return false;
        if (this.Key5._IsSamei(other.Key5) == false) return false;
        return true;
    }
}

public struct HadbLabels : IEquatable<HadbLabels>
{
    public string Label1 { get; }
    public string Label2 { get; }
    public string Label3 { get; }
    public string Label4 { get; }
    public string Label5 { get; }

    public HadbLabels(string label1, string? label2 = null, string? label3 = null, string? label4 = null, string? label5 = null)
    {
        this.Label1 = label1._NormalizeKey(false);
        this.Label2 = label2._NormalizeKey(false);
        this.Label3 = label3._NormalizeKey(false);
        this.Label4 = label4._NormalizeKey(false);
        this.Label5 = label5._NormalizeKey(false);
    }

    public bool Equals(HadbLabels other)
    {
        if (this.Label1._IsSamei(other.Label1) == false) return false;
        if (this.Label2._IsSamei(other.Label2) == false) return false;
        if (this.Label3._IsSamei(other.Label3) == false) return false;
        if (this.Label4._IsSamei(other.Label4) == false) return false;
        if (this.Label5._IsSamei(other.Label5) == false) return false;
        return true;
    }
}

public abstract class HadbLog : INormalizable
{
    public virtual HadbLabels GetLabels() => new HadbLabels("");
    public virtual HadbLogFlags GetLogFlags() => HadbLogFlags.None;
    public virtual string GenerateFt1() => Str.GenerateSearchableStrFromObject(this, SearchableStrFlag.Default);
    public virtual string GenerateFt2() => Str.GenerateSearchableStrFromObject(this, SearchableStrFlag.Default | SearchableStrFlag.PrependFieldName);

    public abstract void Normalize();

    public Type GetLogDataType() => this.GetType();
    public string GetLogDataTypeName() => this.GetType().Name;
    public string GetLogDataJsonString()
    {
        try
        {
            this.Normalize();
        }
        catch (Exception ex)
        {
            ex._Debug();
        }
        return this._ObjectToJson(compact: true);
    }

    public T GetData<T>() where T : HadbLog
        => (T)this;
}

[Flags]
public enum HadbDataFlags : long
{
    None = 0,
    NoFullTextSearch = 1,
}

[Flags]
public enum HadbLogFlags : long
{
    None = 0,
    NoFullTextSearch = 1,
}

public abstract class HadbData : INormalizable
{
    public virtual HadbKeys GetKeys() => new HadbKeys("");
    public virtual HadbLabels GetLabels() => new HadbLabels("");
    public virtual int GetMaxArchivedCount() => CoresConfig.Hadb.DefaultMaxArchiveCount;
    public virtual string GenerateFt1(bool fastMode = false) => Str.GenerateSearchableStrFromObject(this, SearchableStrFlag.Default | (fastMode ? SearchableStrFlag.FastMode : SearchableStrFlag.None));
    public virtual string GenerateFt2(bool fastMode = false) => Str.GenerateSearchableStrFromObject(this, SearchableStrFlag.Default | SearchableStrFlag.PrependFieldName | (fastMode ? SearchableStrFlag.FastMode : SearchableStrFlag.None));
    public virtual HadbDataFlags GetDataFlags() => HadbDataFlags.None;

    public abstract void Normalize();

    public HadbObject ToNewObject(long snapshotNo, string nameSpace, bool prependAtoZHashChar, HadbOptionFlags hadbOptions, string ext1 = "", string ext2 = "", string ft1 = "", string ft2 = "") => new HadbObject(this, snapshotNo, nameSpace, prependAtoZHashChar, hadbOptions, ext1, ext2, ft1, ft2);

    //public static implicit operator HadbObject(HadbData data) => data.ToNewObject();

    Type _Type => this.GetType();
    string _TypeName => this._Type.Name;

    public Type GetUserDataType() => this._Type;
    public string GetUserDataTypeName() => this._TypeName;

    public string GetUserDataJsonString()
    {
        try
        {
            this.Normalize();
        }
        catch (Exception ex)
        {
            ex._Debug();
        }
        return this._ObjectToJson(compact: true);
    }

    public T GetData<T>() where T : HadbData
        => (T)this;
}

public sealed class HadbSnapshot
{
    public string Uid { get; }
    public string SystemName { get; }
    public long Number { get; }
    public DateTimeOffset TimeStamp { get; }
    public string Description { get; }
    public string Ext1 { get; }
    public string Ext2 { get; }

    public HadbSnapshot(string uid, string systemName, long number, DateTimeOffset timeStamp, string description, string ext1, string ext2)
    {
        Uid = uid._NormalizeUid();
        SystemName = systemName._NormalizeKey(true);
        Number = number;
        TimeStamp = timeStamp._NormalizeDateTimeOffset();
        Description = description._NonNull();
        Ext1 = ext1._NonNull();
        Ext2 = ext2._NonNull();
    }
}

public sealed class HadbObject<T> : INormalizable // 単なるラッパー
    where T : HadbData
{
    public HadbObject TargetObject { get; }

    public CriticalSection<HadbObject> Lock => TargetObject.Lock;
    public HadbMemDataBase? MemDb => TargetObject.MemDb;
    public bool IsMemoryDbObject => TargetObject.IsMemoryDbObject;
    public string Uid => TargetObject.Uid;
    public long Ver => TargetObject.Ver;
    public bool Deleted => TargetObject.Deleted;
    public DateTimeOffset CreateDt => TargetObject.CreateDt;
    public DateTimeOffset UpdateDt => TargetObject.UpdateDt;
    public DateTimeOffset DeleteDt => TargetObject.DeleteDt;
    public bool Archive => TargetObject.Archive;
    public long SnapshotNo => TargetObject.SnapshotNo;
    public string NameSpace => TargetObject.NameSpace;
    public T UserData => (T)TargetObject.UserData;
    public string Ext1 { get => TargetObject.Ext1; set => TargetObject.Ext1 = value; }
    public string Ext2 { get => TargetObject.Ext2; set => TargetObject.Ext2 = value; }
    public string Ft1 { get => TargetObject.Ft1; set => TargetObject.Ft1 = value; }
    public string Ft2 { get => TargetObject.Ft2; set => TargetObject.Ft2 = value; }
    public long InternalFastUpdateVersion => TargetObject.InternalFastUpdateVersion;
    public string GetUserDataJsonString() => TargetObject.GetUserDataJsonString();
    public void CheckIsMemoryDbObject() => TargetObject.CheckIsMemoryDbObject();
    public void CheckIsNotMemoryDbObject() => TargetObject.CheckIsNotMemoryDbObject();
    public T FastUpdate(Func<T, bool> updateFunc, bool noReturn = false, HadbFastUpdateOptions option = default) => TargetObject.FastUpdate(updateFunc, noReturn, option);
    public Type GetUserDataType() => TargetObject.GetUserDataType();
    public string GetUserDataTypeName() => TargetObject.GetUserDataTypeName();
    public string GetUidPrefix() => TargetObject.GetUidPrefix();
    public HadbKeys GetKeys() => TargetObject.GetKeys();
    public HadbLabels GetLabels() => TargetObject.GetLabels();
    public T GetData() => TargetObject.GetData<T>();
    public T Data => GetData();
    public void UpdateFullText() => TargetObject.UpdateFullText();

    public HadbObject<T> CloneObject() => new HadbObject<T>(TargetObject.CloneObject());

    public HadbObject(HadbObject targetObject)
    {
        this.TargetObject = targetObject;
    }

    public void Normalize() => TargetObject.Normalize();

    [return: NotNullIfNotNull("src")]
    public static implicit operator HadbObject<T>?(HadbObject? src)
    {
        if (src == null) return null;
        return new HadbObject<T>(src);
    }

    [return: NotNullIfNotNull("src")]
    public static implicit operator HadbObject?(HadbObject<T>? src)
    {
        if (src == null) return null;
        return src.TargetObject;
    }

    [return: NotNullIfNotNull("src")]
    public static implicit operator T?(HadbObject<T>? src)
    {
        if (src == null) return null;
        return src.Data;
    }
}

public sealed class HadbSerializedObject
{
    public string Uid { get; set; } = "";

    public long Ver { get; set; }

    public DateTimeOffset CreateDt { get; set; }

    public DateTimeOffset UpdateDt { get; set; }

    public DateTimeOffset DeleteDt { get; set; }

    public long SnapshotNo { get; set; }

    public string NameSpace { get; set; } = "";

    public string Ext1 { get; set; } = "";

    public string Ext2 { get; set; } = "";

    public string Ft1 { get; set; } = "";

    public string Ft2 { get; set; } = "";

    public object UserData { get; set; } = null!;

    public string UserDataTypeName { get; set; } = "";
}

public sealed class HadbQuick<T>
{
    public string Uid { get; }

    public string NameSpace { get; }

    public string Key { get; }

    public DateTimeOffset CreateDt { get; }

    public DateTimeOffset UpdateDt { get; }

    public long SnapshotNo { get; }

    public long UpdateCount { get; }

    public T Data { get; }

    public HadbQuick(T userData, string uid, string nameSpace, string key, DateTimeOffset createDt, DateTimeOffset updateDt, long snapshotNo, long updateCount)
    {
        if (userData is object x)
        {
            x._NullCheck(nameof(userData));
        }

        this.Uid = uid._NormalizeUid();
        this.NameSpace = nameSpace._NormalizeKey(true);
        this.Key = key._NormalizeKey(true);
        this.CreateDt = createDt._NormalizeDateTimeOffset();
        this.UpdateDt = updateDt._NormalizeDateTimeOffset();
        this.SnapshotNo = Math.Max(snapshotNo, 0);
        this.UpdateCount = Math.Max(updateCount, 0);
        this.Data = userData;
    }

    public static implicit operator T?(HadbQuick<T>? src)
    {
        if (src == null) return default;
        return src.Data;
    }
}

public struct HadbFastUpdateOptions
{
    public int QuotaDurationMsecs;
    public int QuotaMaxFastUpdateCountPerDuration;
}

public sealed class HadbObject : INormalizable
{
    public readonly CriticalSection<HadbObject> Lock = new CriticalSection<HadbObject>();

    public HadbMemDataBase? MemDb { get; }

    public bool IsMemoryDbObject => MemDb != null;

    public string Uid { get; }

    public HadbOptionFlags HadbOptions { get; }

    public long Ver { get; private set; }

    public bool Deleted { get; private set; }

    public DateTimeOffset CreateDt { get; private set; }

    public DateTimeOffset UpdateDt { get; private set; }

    public DateTimeOffset DeleteDt { get; private set; }

    public bool Archive { get; }

    public long SnapshotNo { get; private set; }

    public string NameSpace { get; }

    public HadbData UserData { get; private set; }

    long FastUpdate_QuotaCounterStartTick;
    int FastUpdate_CurrentQuota;

    string _Ext1;
    string _Ext2;

    public string Ext1 { get => this._Ext1; set { this.CheckIsNotMemoryDbObject(); this._Ext1 = value._NonNull(); } }
    public string Ext2 { get => this._Ext2; set { this.CheckIsNotMemoryDbObject(); this._Ext2 = value._NonNull(); } }

    string _Ft1;
    string _Ft2;

    public string Ft1 { get => this._Ft1; set { this.CheckIsNotMemoryDbObject(); this._Ft1 = value._NonNull(); } }
    public string Ft2 { get => this._Ft2; set { this.CheckIsNotMemoryDbObject(); this._Ft2 = value._NonNull(); } }

    public long InternalFastUpdateVersion { get; private set; }

    public string GetUserDataJsonString() => this.UserData.GetUserDataJsonString();

    public Type UserDataType { get; }
    public string UserDataTypeName { get; }

    public HadbObject(HadbData userData, long snapshotNo, string nameSpace, bool prependAtoZHashChar, HadbOptionFlags hadbOptions, string ext1 = "", string ext2 = "", string ft1 = "", string ft2 = "") : this(userData, hadbOptions, ext1, ext2, ft1, ft2, Str.NewUid(userData.GetUserDataTypeName(), '_', prependAtoZHashChar), 1, false, snapshotNo, nameSpace, false, DtOffsetNow, DtOffsetNow, DtOffsetZero) { }

    public HadbObject(HadbData userData, HadbOptionFlags hadbOptions, string ext1, string ext2, string ft1, string ft2, string uid, long ver, bool archive, long snapshotNo, string nameSpace, bool deleted, DateTimeOffset createDt, DateTimeOffset updateDt, DateTimeOffset deleteDt, HadbMemDataBase? memDb = null)
    {
        nameSpace = nameSpace._HadbNameSpaceNormalize();

        nameSpace = nameSpace._NormalizeKey(true);

        this.Uid = uid._NormalizeUid();

        if (this.Uid._IsEmpty())
        {
            throw new CoresLibException("uid is empty.");
        }

        userData._NullCheck(nameof(userData));

        this.UserData = userData._CloneDeep();

        this.SnapshotNo = snapshotNo;
        this.NameSpace = nameSpace;
        this.Ver = Math.Max(ver, 1);
        this.Archive = archive;
        this.Deleted = deleted;
        this.CreateDt = createDt._NormalizeDateTimeOffset();
        this.UpdateDt = updateDt._NormalizeDateTimeOffset();
        this.DeleteDt = deleteDt._NormalizeDateTimeOffset();
        this._Ext1 = ext1._NonNull();
        this._Ext2 = ext2._NonNull();
        this.HadbOptions = hadbOptions;

        if (this.HadbOptions.Bit(HadbOptionFlags.BuildFullTextSearchText))
        {
            this._Ft1 = ft1._NonNull();
            this._Ft2 = ft2._NonNull();
        }
        else
        {
            this._Ft1 = "";
            this._Ft2 = "";
        }

        this.MemDb = memDb;
        if (this.MemDb != null)
        {
            if (this.Archive) throw new CoresLibException("this.Archive == true");
        }

        this.UserDataType = this.UserData.GetUserDataType();
        this.UserDataTypeName = this.UserData.GetUserDataTypeName();

        this.Normalize();
    }

    public HadbObject ToMemoryDbObject(HadbMemDataBase memDb)
    {
        memDb._NullCheck();

        CheckIsNotMemoryDbObject();
        if (this.Archive) throw new CoresLibException("this.Archive == true");

        return new HadbObject(this.UserData, this.HadbOptions, this.Ext1, this.Ext2, this.Ft1, this.Ft2, this.Uid, this.Ver, this.Archive, this.SnapshotNo, this.NameSpace, this.Deleted, this.CreateDt, this.UpdateDt, this.DeleteDt, memDb);
    }

    public HadbObject ToNonMemoryDbObject()
    {
        CheckIsMemoryDbObject();

        lock (this.Lock)
        {
            var q = new HadbObject(this.UserData, this.HadbOptions, this.Ext1, this.Ext2, this.Ft1, this.Ft2, this.Uid, this.Ver, this.Archive, this.SnapshotNo, this.NameSpace, this.Deleted, this.CreateDt, this.UpdateDt, this.DeleteDt);

            q.InternalFastUpdateVersion = this.InternalFastUpdateVersion;

            return q;
        }
    }

    public HadbObject CloneObject()
    {
        CheckIsNotMemoryDbObject();
        return new HadbObject(this.UserData, this.HadbOptions, this.Ext1, this.Ext2, this.Ft1, this.Ft2, this.Uid, this.Ver, this.Archive, this.SnapshotNo, this.NameSpace, this.Deleted, this.CreateDt, this.UpdateDt, this.DeleteDt);
    }

    [MethodImpl(Inline)]
    public void CheckIsMemoryDbObject()
    {
        if (this.IsMemoryDbObject == false) throw new CoresLibException("this.IsMemoryDbObject == false");
    }

    public void CheckIsNotMemoryDbObject()
    {
        if (this.IsMemoryDbObject) throw new CoresLibException("this.IsMemoryDbObject == true");
    }

    public T FastUpdate<T>(Func<T, bool> updateFunc, bool noReturn = false, HadbFastUpdateOptions option = default) where T : HadbData
    {
        CheckIsMemoryDbObject();

        if (option.QuotaMaxFastUpdateCountPerDuration >= 1 && option.QuotaDurationMsecs >= 1)
        {
            if (this.Deleted) throw new CoresLibException($"this.Deleted == true");
            if (this.Archive) throw new CoresLibException("this.Archive == true");

            // 頻繁に高速な Update が行なわれた場合に無視する
            long now = Time.Tick64;

            long currentStartTick = this.FastUpdate_QuotaCounterStartTick;

            bool reset = false;

            if (currentStartTick == 0)
            {
                currentStartTick = now;
                reset = true;
            }
            else
            {
                if (now > (currentStartTick + option.QuotaDurationMsecs))
                {
                    currentStartTick = now;
                    reset = true;
                }
            }

            if (reset)
            {
                this.FastUpdate_QuotaCounterStartTick = currentStartTick;
                this.FastUpdate_CurrentQuota = 0;
            }

            this.FastUpdate_CurrentQuota++;

            if (this.FastUpdate_CurrentQuota > option.QuotaMaxFastUpdateCountPerDuration)
            {
                if (noReturn)
                {
                    return default!;
                }
                else
                {
                    T ret;
                    lock (this.Lock)
                    {
                        ret = this.UserData._CloneDeep().GetData<T>();
                    }
                    return ret;
                }
            }
        }

        T retNewObj;

        lock (this.Lock)
        {
            if (this.Deleted) throw new CoresLibException($"this.Deleted == true");
            if (this.Archive) throw new CoresLibException("this.Archive == true");

            var oldKeys = this.GetKeys();
            var oldLabels = this.GetLabels();

            T userData = this.UserData._CloneDeep().GetData<T>();

            string oldJson = userData.GetUserDataJsonString();

            bool ret = updateFunc(userData);

            if (ret == false)
            {
                // 変更なし
                return this.UserData._CloneDeep().GetData<T>();
            }

            try
            {
                userData.Normalize();
            }
            catch (Exception ex)
            {
                ex._Debug();
            }

            string newJson = userData.GetUserDataJsonString();

            if (oldJson._IsSame(newJson))
            {
                // 変更なし
                return this.UserData._CloneDeep().GetData<T>();
            }

            var newKeys = userData.GetKeys();
            var newLables = userData.GetLabels();

            if (oldKeys.Equals(newKeys) == false)
            {
                throw new CoresLibException($"FastUpdate: updateFunc changed the key value. Old keys = {oldKeys._ObjectToJson(compact: true)}, New keys = {newKeys._ObjectToJson(compact: true)}");
            }

            if (oldLabels.Equals(newLables) == false)
            {
                throw new CoresLibException($"FastUpdate: updateFunc changed the label value. Old labels = {oldLabels._ObjectToJson(compact: true)}, New labels = {newLables._ObjectToJson(compact: true)}");
            }

            string newFt1 = "";
            string newFt2 = "";

            if (this.UserData.GetDataFlags().Bit(HadbDataFlags.NoFullTextSearch) == false && this.HadbOptions.Bit(HadbOptionFlags.BuildFullTextSearchText))
            {
                newFt1 = userData.GenerateFt1();
                newFt2 = userData.GenerateFt2();
            }

            this.UserData = userData._CloneDeep();
            this.UpdateDt = DtOffsetNow;

            this._Ft1 = newFt1;
            this._Ft2 = newFt2;

            this.InternalFastUpdateVersion++;

            retNewObj = this.UserData.GetData<T>();
        }

        this.MemDb!.AddToLazyUpdateQueueInternal(this);

        return retNewObj._CloneDeep();
    }

    internal bool Internal_UpdateIfNew(EnsureSpecial yes, HadbObject newObj, out HadbKeys oldKeys, out HadbLabels oldLabels)
    {
        CheckIsMemoryDbObject();

        lock (this.Lock)
        {
            if (this.Uid._IsSamei(newObj.Uid) == false)
            {
                throw new CoresLibException($"this.Uid '{this.Uid}' != obj.Uid '{newObj.Uid}'");
            }

            if (this.NameSpace._IsSamei(newObj.NameSpace) == false)
            {
                throw new CoresLibException($"this.NameSpace '{this.NameSpace}' != obj.NameSpace '{newObj.NameSpace}'");
            }

            if (this.Archive)
            {
                throw new CoresLibException($"this.Archive == true");
            }

            if (newObj.Archive)
            {
                throw new CoresLibException($"obj.Archive == true");
            }

            bool update = false;

            if (this.Ver < newObj.Ver)
            {
                update = true;
            }
            else if (this.Ver == newObj.Ver && this.UpdateDt < newObj.UpdateDt)
            {
                update = true;
            }

            if (update)
            {
                oldKeys = this.GetKeys();
                oldLabels = this.GetLabels();

                this.Deleted = newObj.Deleted;
                this.CreateDt = newObj.CreateDt;
                this.UpdateDt = newObj.UpdateDt;
                this.DeleteDt = newObj.DeleteDt;
                this.UserData = newObj.UserData._CloneDeep();
                this._Ext1 = newObj.Ext1;
                this._Ext2 = newObj.Ext2;
                this._Ft1 = newObj.Ft1;
                this._Ft2 = newObj.Ft2;
                this.Ver = newObj.Ver;
                this.SnapshotNo = newObj.SnapshotNo;
            }
            else
            {
                oldKeys = default;
                oldLabels = default;
            }

            return update;
        }
    }

    public Type GetUserDataType() => this.UserDataType;
    public string GetUserDataTypeName() => this.UserDataTypeName;
    public string GetUidPrefix() => this.GetUserDataTypeName().ToUpperInvariant();

    public HadbKeys GetKeys() => this.Deleted == false ? this.UserData.GetKeys() : new HadbKeys("");
    public HadbLabels GetLabels() => this.Deleted == false ? this.UserData.GetLabels() : new HadbLabels("");

    public T GetData<T>() where T : HadbData
        => (T)this.UserData;

    public HadbObject<T> GetGenerics<T>() where T : HadbData
        => new HadbObject<T>(this);

    public void UpdateFullText()
    {
        string newFt1 = "";
        string newFt2 = "";

        if (this.UserData.GetDataFlags().Bit(HadbDataFlags.NoFullTextSearch) == false && this.HadbOptions.Bit(HadbOptionFlags.BuildFullTextSearchText))
        {
            newFt1 = this.UserData.GenerateFt1();
            newFt2 = this.UserData.GenerateFt2();
        }

        this._Ft1 = newFt1;
        this._Ft2 = newFt2;
    }

    public void Normalize()
    {
        try
        {
            this.UserData.Normalize();
        }
        catch (Exception ex)
        {
            ex._Debug();
        }
    }

    public HadbSerializedObject ToSerializedObject()
    {
        CheckIsNotMemoryDbObject();
        if (this.Deleted) throw new CoresLibException("this.Deleted == true");

        HadbSerializedObject ret = new HadbSerializedObject
        {
            Uid = this.Uid,
            Ver = this.Ver,
            CreateDt = this.CreateDt,
            UpdateDt = this.UpdateDt,
            DeleteDt = this.DeleteDt,
            SnapshotNo = this.SnapshotNo,
            NameSpace = this.NameSpace,
            UserData = this.UserData,
            Ext1 = this.Ext1,
            Ext2 = this.Ext2,
            Ft1 = this.Ft1,
            Ft2 = this.Ft2,
            UserDataTypeName = this.UserDataTypeName,
        };

        return ret;
    }

    public void ReplaceData(HadbData newData)
    {
        this.CheckIsNotMemoryDbObject();

        if (this.UserDataType != newData.GetType())
        {
            throw new CoresLibException($"this.UserDataType ({this.UserDataType.ToString()}) != newData.GetType() ({newData.GetType().ToString()})");
        }

        newData = newData._CloneDeep();

        this.UserData = newData;

        this.Normalize();
        this.UpdateFullText();
    }
}

public enum HadbIndexColumn
{
    Uid,
    Key1,
    Key2,
    Key3,
    Key4,
    Key5,
    Label1,
    Label2,
    Label3,
    Label4,
    Label5,
}

public abstract class HadbMemDataBase
{
    public class DataSet
    {
        public ConcurrentStrDictionary<HadbObject> AllObjectsDict = new ConcurrentStrDictionary<HadbObject>(StrComparer.SensitiveCaseComparer);
        public ImmutableDictionary<string, HadbObject> IndexedKeysTable = ImmutableDictionary<string, HadbObject>.Empty.WithComparers(StrComparer.SensitiveCaseComparer);
        public ImmutableDictionary<string, ConcurrentHashSet<HadbObject>> IndexedLabelsTable = ImmutableDictionary<string, ConcurrentHashSet<HadbObject>>.Empty.WithComparers(StrComparer.SensitiveCaseComparer);

        public void IndexedTable_DeleteObject_Critical(HadbObject obj, HadbKeys oldKeys, HadbLabels oldLabels)
        {
            obj.CheckIsMemoryDbObject();

            string typeName = obj.GetUserDataTypeName();

            IndexedKeysTable_DeleteInternal_Critical(obj.Uid + ":" + HadbIndexColumn.Uid.ToString() + ":" + typeName + ":" + obj.NameSpace);

            if (oldKeys.Key1._IsFilled()) IndexedKeysTable_DeleteInternal_Critical(oldKeys.Key1 + ":" + HadbIndexColumn.Key1.ToString() + ":" + typeName + ":" + obj.NameSpace);
            if (oldKeys.Key2._IsFilled()) IndexedKeysTable_DeleteInternal_Critical(oldKeys.Key2 + ":" + HadbIndexColumn.Key2.ToString() + ":" + typeName + ":" + obj.NameSpace);
            if (oldKeys.Key3._IsFilled()) IndexedKeysTable_DeleteInternal_Critical(oldKeys.Key3 + ":" + HadbIndexColumn.Key3.ToString() + ":" + typeName + ":" + obj.NameSpace);
            if (oldKeys.Key4._IsFilled()) IndexedKeysTable_DeleteInternal_Critical(oldKeys.Key4 + ":" + HadbIndexColumn.Key4.ToString() + ":" + typeName + ":" + obj.NameSpace);
            if (oldKeys.Key5._IsFilled()) IndexedKeysTable_DeleteInternal_Critical(oldKeys.Key5 + ":" + HadbIndexColumn.Key5.ToString() + ":" + typeName + ":" + obj.NameSpace);

            if (oldLabels.Label1._IsFilled()) IndexedLabelsTable_DeleteInternal_Critical(oldLabels.Label1 + ":" + HadbIndexColumn.Label1.ToString() + ":" + typeName + ":" + obj.NameSpace, obj);
            if (oldLabels.Label2._IsFilled()) IndexedLabelsTable_DeleteInternal_Critical(oldLabels.Label2 + ":" + HadbIndexColumn.Label2.ToString() + ":" + typeName + ":" + obj.NameSpace, obj);
            if (oldLabels.Label3._IsFilled()) IndexedLabelsTable_DeleteInternal_Critical(oldLabels.Label3 + ":" + HadbIndexColumn.Label3.ToString() + ":" + typeName + ":" + obj.NameSpace, obj);
            if (oldLabels.Label4._IsFilled()) IndexedLabelsTable_DeleteInternal_Critical(oldLabels.Label4 + ":" + HadbIndexColumn.Label4.ToString() + ":" + typeName + ":" + obj.NameSpace, obj);
            if (oldLabels.Label5._IsFilled()) IndexedLabelsTable_DeleteInternal_Critical(oldLabels.Label5 + ":" + HadbIndexColumn.Label5.ToString() + ":" + typeName + ":" + obj.NameSpace, obj);
        }

        public void IndexedTable_UpdateObject_Critical(HadbObject obj, HadbKeys oldKeys, HadbLabels oldLabels)
        {
            obj.CheckIsMemoryDbObject();

            HadbKeys newKeys = obj.GetKeys();
            HadbLabels newLabels = obj.GetLabels();

            if (oldKeys.Key1._IsSamei(newKeys.Key1) == false) IndexedKeysTable_ReplaceObject_Critical(obj, HadbIndexColumn.Key1, oldKeys.Key1, newKeys.Key1);
            if (oldKeys.Key2._IsSamei(newKeys.Key2) == false) IndexedKeysTable_ReplaceObject_Critical(obj, HadbIndexColumn.Key2, oldKeys.Key2, newKeys.Key2);
            if (oldKeys.Key3._IsSamei(newKeys.Key3) == false) IndexedKeysTable_ReplaceObject_Critical(obj, HadbIndexColumn.Key3, oldKeys.Key3, newKeys.Key3);
            if (oldKeys.Key4._IsSamei(newKeys.Key4) == false) IndexedKeysTable_ReplaceObject_Critical(obj, HadbIndexColumn.Key4, oldKeys.Key4, newKeys.Key4);
            if (oldKeys.Key5._IsSamei(newKeys.Key5) == false) IndexedKeysTable_ReplaceObject_Critical(obj, HadbIndexColumn.Key5, oldKeys.Key5, newKeys.Key5);

            if (oldLabels.Label1._IsSamei(newLabels.Label1) == false) IndexedLabelsTable_ReplaceObject_Critical(obj, HadbIndexColumn.Label1, oldLabels.Label1, newLabels.Label1);
            if (oldLabels.Label2._IsSamei(newLabels.Label2) == false) IndexedLabelsTable_ReplaceObject_Critical(obj, HadbIndexColumn.Label2, oldLabels.Label2, newLabels.Label2);
            if (oldLabels.Label3._IsSamei(newLabels.Label3) == false) IndexedLabelsTable_ReplaceObject_Critical(obj, HadbIndexColumn.Label3, oldLabels.Label3, newLabels.Label3);
            if (oldLabels.Label4._IsSamei(newLabels.Label4) == false) IndexedLabelsTable_ReplaceObject_Critical(obj, HadbIndexColumn.Label4, oldLabels.Label4, newLabels.Label4);
            if (oldLabels.Label5._IsSamei(newLabels.Label5) == false) IndexedLabelsTable_ReplaceObject_Critical(obj, HadbIndexColumn.Label5, oldLabels.Label5, newLabels.Label5);
        }

        public void IndexedTable_AddObject_Critical(HadbObject obj)
        {
            obj.CheckIsMemoryDbObject();

            var newKeys = obj.GetKeys();
            var newLabels = obj.GetLabels();

            string typeName = obj.GetUserDataTypeName();

            IndexedKeysTable_AddOrUpdateInternal_Critical(obj.Uid + ":" + HadbIndexColumn.Uid.ToString() + ":" + typeName + ":" + obj.NameSpace, obj);

            if (newKeys.Key1._IsFilled()) IndexedKeysTable_AddOrUpdateInternal_Critical(newKeys.Key1 + ":" + HadbIndexColumn.Key1.ToString() + ":" + typeName + ":" + obj.NameSpace, obj);
            if (newKeys.Key2._IsFilled()) IndexedKeysTable_AddOrUpdateInternal_Critical(newKeys.Key2 + ":" + HadbIndexColumn.Key2.ToString() + ":" + typeName + ":" + obj.NameSpace, obj);
            if (newKeys.Key3._IsFilled()) IndexedKeysTable_AddOrUpdateInternal_Critical(newKeys.Key3 + ":" + HadbIndexColumn.Key3.ToString() + ":" + typeName + ":" + obj.NameSpace, obj);
            if (newKeys.Key4._IsFilled()) IndexedKeysTable_AddOrUpdateInternal_Critical(newKeys.Key4 + ":" + HadbIndexColumn.Key4.ToString() + ":" + typeName + ":" + obj.NameSpace, obj);
            if (newKeys.Key5._IsFilled()) IndexedKeysTable_AddOrUpdateInternal_Critical(newKeys.Key5 + ":" + HadbIndexColumn.Key5.ToString() + ":" + typeName + ":" + obj.NameSpace, obj);

            if (newLabels.Label1._IsFilled()) IndexedLabelsTable_AddInternal_Critical(newLabels.Label1 + ":" + HadbIndexColumn.Label1.ToString() + ":" + typeName + ":" + obj.NameSpace, obj);
            if (newLabels.Label2._IsFilled()) IndexedLabelsTable_AddInternal_Critical(newLabels.Label2 + ":" + HadbIndexColumn.Label2.ToString() + ":" + typeName + ":" + obj.NameSpace, obj);
            if (newLabels.Label3._IsFilled()) IndexedLabelsTable_AddInternal_Critical(newLabels.Label3 + ":" + HadbIndexColumn.Label3.ToString() + ":" + typeName + ":" + obj.NameSpace, obj);
            if (newLabels.Label4._IsFilled()) IndexedLabelsTable_AddInternal_Critical(newLabels.Label4 + ":" + HadbIndexColumn.Label4.ToString() + ":" + typeName + ":" + obj.NameSpace, obj);
            if (newLabels.Label5._IsFilled()) IndexedLabelsTable_AddInternal_Critical(newLabels.Label5 + ":" + HadbIndexColumn.Label5.ToString() + ":" + typeName + ":" + obj.NameSpace, obj);
        }

        public void IndexedKeysTable_ReplaceObject_Critical(HadbObject obj, HadbIndexColumn column, string? oldKey, string? newKey)
        {
            obj.CheckIsMemoryDbObject();

            oldKey = oldKey._NormalizeKey(true);
            newKey = newKey._NormalizeKey(true);

            if (newKey._IsSamei(oldKey) == false)
            {
                if (newKey._IsFilled())
                {
                    IndexedKeysTable_AddOrUpdateInternal_Critical(newKey + ":" + column.ToString() + ":" + obj.GetUserDataTypeName() + ":" + obj.NameSpace, obj);
                }

                if (oldKey._IsFilled())
                {
                    IndexedKeysTable_DeleteInternal_Critical(oldKey + ":" + column.ToString() + ":" + obj.GetUserDataTypeName() + ":" + obj.NameSpace);
                }
            }
        }

        public HadbObject? IndexedKeysTable_SearchObject(HadbIndexColumn column, string typeName, string key, string nameSpace)
        {
            nameSpace = nameSpace._HadbNameSpaceNormalize();
            key = key._NormalizeKey(true);
            typeName = typeName._NonNullTrim();

            if (key._IsEmpty()) return null;

            return this.IndexedKeysTable._GetOrDefault(key + ":" + column.ToString() + ":" + typeName + ":" + nameSpace);
        }

        public void IndexedKeysTable_AddOrUpdateInternal_Critical(string keyStr, HadbObject obj)
        {
            ImmutableInterlocked.AddOrUpdate(ref this.IndexedKeysTable, keyStr, obj, (k, old) => obj);
        }

        public bool IndexedKeysTable_DeleteInternal_Critical(string keyStr)
        {
            return ImmutableInterlocked.TryRemove(ref this.IndexedKeysTable, keyStr, out _);
        }

        public void IndexedLabelsTable_ReplaceObject_Critical(HadbObject obj, HadbIndexColumn column, string? oldLabel, string? newLabel)
        {
            obj.CheckIsMemoryDbObject();

            oldLabel = oldLabel._NormalizeKey(false);
            newLabel = newLabel._NormalizeKey(false);

            if (oldLabel._IsSamei(newLabel) == false)
            {
                string typeName = obj.GetUserDataTypeName();

                if (newLabel._IsFilled())
                {
                    IndexedLabelsTable_AddInternal_Critical(newLabel + ":" + column.ToString() + ":" + typeName + ":" + obj.NameSpace, obj);
                }

                if (oldLabel._IsFilled())
                {
                    IndexedLabelsTable_DeleteInternal_Critical(oldLabel + ":" + column.ToString() + ":" + typeName + ":" + obj.NameSpace, obj);
                }
            }
        }

        public IEnumerable<HadbObject> IndexedLabelsTable_SearchObjects(HadbIndexColumn column, string typeName, string label, string nameSpace)
        {
            nameSpace = nameSpace._HadbNameSpaceNormalize();
            label = label._NormalizeKey(false);
            typeName = typeName._NonNullTrim();

            if (label._IsEmpty()) return EmptyOf<HadbObject>();

            var list = this.IndexedLabelsTable._GetOrDefault(label + ":" + column.ToString() + ":" + typeName + ":" + nameSpace);
            if (list == null) return EmptyOf<HadbObject>();

            return list.Keys;
        }

        public void IndexedLabelsTable_AddInternal_Critical(string labelKeyStr, HadbObject obj)
        {
            var list = ImmutableInterlocked.GetOrAdd(ref this.IndexedLabelsTable, labelKeyStr, k => new ConcurrentHashSet<HadbObject>());

            list.Add(obj);
        }

        public bool IndexedLabelsTable_DeleteInternal_Critical(string labelKeyStr, HadbObject obj)
        {
            var list = this.IndexedLabelsTable._GetOrDefault(labelKeyStr);
            if (list == null) return false;

            if (list.Remove(obj) == false) return false;

            if (list.Count == 0)
            {
                ImmutableInterlocked.TryRemove(ref this.IndexedLabelsTable, labelKeyStr, out _);
            }

            return true;
        }
    }

    protected abstract List<Type> GetDefinedUserDataTypesImpl();
    protected abstract List<Type> GetDefinedUserLogTypesImpl();

    public readonly AsyncLock DatabaseReloadLock = new AsyncLock();

    public DataSet InternalData = new DataSet();

    readonly CriticalSection<HadbMemDataBase> LazyUpdateQueueLock = new CriticalSection<HadbMemDataBase>();
    internal ImmutableDictionary<HadbObject, int> _LazyUpdateQueue = ImmutableDictionary<HadbObject, int>.Empty;

    public int GetLazyUpdateQueueLength() => this._LazyUpdateQueue.Count;

    public void Debug(string str)
    {
        string pos = Dbg.GetCurrentExecutingPositionInfoString();
        $"{pos}: {str}"._Debug();
    }

    public StrDictionary<Type> GetDefinedUserDataTypesByName()
    {
        StrDictionary<Type> ret = new StrDictionary<Type>(StrComparer.SensitiveCaseComparer);

        List<Type> tmp = GetDefinedUserDataTypesImpl();

        foreach (var type in tmp)
        {
            if (type.IsSubclassOf(typeof(HadbData)) == false)
            {
                throw new CoresLibException($"type {type.ToString()} is not a subclass of {nameof(HadbData)}.");
            }

            ret.Add(type.Name, type);
        }

        return ret;
    }

    public StrDictionary<Type> GetDefinedUserLogTypesByName()
    {
        StrDictionary<Type> ret = new StrDictionary<Type>(StrComparer.SensitiveCaseComparer);

        List<Type> tmp = GetDefinedUserLogTypesImpl();

        foreach (var type in tmp)
        {
            if (type.IsSubclassOf(typeof(HadbLog)) == false)
            {
                throw new CoresLibException($"type {type.ToString()} is not a subclass of {nameof(HadbLog)}.");
            }

            ret.Add(type.Name, type);
        }

        return ret;
    }

    public ConcurrentStrDictionary<HadbObject> GetAllObjects()
    {
        DataSet data = this.InternalData;

        return data.AllObjectsDict;
    }

    public HadbCurrentMetrics GetCurrentMetrics()
    {
        DataSet data = this.InternalData;

        HadbCurrentMetrics ret = new HadbCurrentMetrics
        {
            NumMemoryObjects = data.AllObjectsDict.Count,
        };

        return ret;
    }

    public async Task ReloadFromDatabaseIntoMemoryDbAsync(IEnumerable<HadbObject> objectList, bool fullReloadMode, DateTimeOffset partialReloadMinUpdateTime, string dataSourceInfo, CancellationToken cancel = default)
    {
        int countInserted = 0;
        int countUpdated = 0;
        int countRemoved = 0;
        List<HadbObject> objectList2;

        using (await this.DatabaseReloadLock.LockWithAwait(cancel))
        {
            DataSet data = this.InternalData;

            if (fullReloadMode)
            {
                data = new DataSet();
            }

            objectList2 = objectList.ToList();

            ProgressReporter? reporter = null;

            if (fullReloadMode)
            {
                reporter = new ProgressReporter(new ProgressReporterSetting(ProgressReporterOutputs.Debug, toStr3: true, showEta: true,
                    options: ProgressReporterOptions.EnableThroughput | ProgressReporterOptions.ShowThroughputAsInt,
                    reportTimingSetting: new ProgressReportTimingSetting(false, 1000)));
            }

            long currentCount = 0;
            long totalCount = objectList2.Count;

            try
            {
                // 並列化の試み 2021/12/06 うまくいくか未定
                await objectList2._ProcessParallelAsync((someObjects, taskIndex) =>
                {
                    foreach (var newObj in someObjects)
                    {
                        long currentCount2 = Interlocked.Increment(ref currentCount);

                        if ((currentCount2 % 100) == 0)
                        {
                            reporter?.ReportProgress(new ProgressData(currentCount2, totalCount, false, "Reload HADB Object Into Memory DB"));
                        }

                        try
                        {
                            if (data.AllObjectsDict.TryGetValue(newObj.Uid, out HadbObject? currentObj))
                            {
                                lock (currentObj.Lock)
                                {
                                    if (currentObj.Internal_UpdateIfNew(EnsureSpecial.Yes, newObj, out HadbKeys oldKeys, out HadbLabels oldLabels))
                                    {
                                        if (currentObj.Deleted == false)
                                        {
                                            Interlocked.Increment(ref countUpdated);
                                            data.IndexedTable_UpdateObject_Critical(currentObj, oldKeys, oldLabels);
                                        }
                                        else
                                        {
                                            Interlocked.Increment(ref countRemoved);
                                            data.IndexedTable_DeleteObject_Critical(currentObj, oldKeys, oldLabels); // おそらく正しい? 2021/12/06
                                        }
                                    }
                                }
                            }
                            else
                            {
                                var obj2 = newObj.ToMemoryDbObject(this);

                                lock (obj2.Lock)
                                {
                                    data.AllObjectsDict[newObj.Uid] = obj2;

                                    data.IndexedTable_AddObject_Critical(obj2);
                                    Interlocked.Increment(ref countInserted);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            ex._Debug();
                        }
                    }

                    return TR();
                });

                reporter?.ReportProgress(new ProgressData(totalCount, totalCount, true, "Reload HADB Object Into Memory DB"));

                this.InternalData = data;
            }
            finally
            {
                reporter._DisposeSafe();
            }
        }

        Debug($"ReloadFromDatabaseIntoMemoryDbAsync: Update Local Memory from {dataSourceInfo}: Mode={(fullReloadMode ? "FullReload" : $"PartialReload since '{partialReloadMinUpdateTime._ToDtStr()}'")}, DB_ReadObjs={objectList2.Count._ToString3()}, New={countInserted._ToString3()}, Update={countUpdated._ToString3()}, Remove={countRemoved._ToString3()}");
    }

    public HadbObject ApplyObjectToMemDb(HadbObject newObj)
    {
        newObj.Normalize();
        if (newObj.Archive) throw new CoresLibException("obj.Archive == true");

        var data = this.InternalData;

        if (data.AllObjectsDict.TryGetValue(newObj.Uid, out HadbObject? currentObject))
        {
            lock (currentObject.Lock)
            {
                if (currentObject.Internal_UpdateIfNew(EnsureSpecial.Yes, newObj, out HadbKeys oldKeys, out HadbLabels oldLabels))
                {
                    if (currentObject.Deleted == false)
                    {
                        data.IndexedTable_UpdateObject_Critical(currentObject, oldKeys, oldLabels);
                    }
                    else
                    {
                        data.IndexedTable_DeleteObject_Critical(currentObject, oldKeys, oldLabels);
                    }
                }

                return currentObject;
            }
        }
        else
        {
            if (newObj.Deleted == false)
            {
                var newObj2 = newObj.ToMemoryDbObject(this);

                lock (newObj2.Lock)
                {
                    data.AllObjectsDict[newObj.Uid] = newObj2;
                    data.IndexedTable_AddObject_Critical(newObj2);
                    return newObj2;
                }
            }
            else
            {
                return newObj.ToMemoryDbObject(this);
            }
        }
    }

    internal void AddToLazyUpdateQueueInternal(HadbObject obj)
    {
        if (obj.Deleted) return;

        ImmutableInterlocked.TryAdd(ref this._LazyUpdateQueue, obj, 0);
    }

    internal void DeleteFromLazyUpdateQueueInternal(HadbObject obj)
    {
        ImmutableInterlocked.TryRemove(ref this._LazyUpdateQueue, obj, out _);
    }

    public HadbObject? IndexedKeysTable_SearchByUid(string uid, string typeName, string nameSpace)
    {
        nameSpace = nameSpace._HadbNameSpaceNormalize();
        uid = uid._NormalizeUid();

        typeName = typeName._NonNullTrim();

        if (uid._IsEmpty()) return null;

        HadbObject? ret = this.InternalData.IndexedKeysTable_SearchObject(HadbIndexColumn.Uid, typeName, uid, nameSpace);

        return ret;
    }

    public HadbObject? IndexedKeysTable_SearchByKey(HadbKeys key, string typeName, string nameSpace)
    {
        nameSpace = nameSpace._HadbNameSpaceNormalize();
        typeName = typeName._NonNullTrim();

        var data = this.InternalData;

        HadbObject? ret = null;

        if (key.Key1._IsFilled())
        {
            var obj = data.IndexedKeysTable_SearchObject(HadbIndexColumn.Key1, typeName, key.Key1, nameSpace);
            if (obj != null) if (ret == null) ret = obj; else throw new CoresLibException($"Two or more objects match. Key1 = '{key.Key1}'");
        }

        if (key.Key2._IsFilled())
        {
            var obj = data.IndexedKeysTable_SearchObject(HadbIndexColumn.Key2, typeName, key.Key2, nameSpace);
            if (obj != null) if (ret == null) ret = obj; else throw new CoresLibException($"Two or more objects match. Key2 = '{key.Key2}'");
        }

        if (key.Key3._IsFilled())
        {
            var obj = data.IndexedKeysTable_SearchObject(HadbIndexColumn.Key3, typeName, key.Key3, nameSpace);
            if (obj != null) if (ret == null) ret = obj; else throw new CoresLibException($"Two or more objects match. Key3 = '{key.Key3}'");
        }

        if (key.Key4._IsFilled())
        {
            var obj = data.IndexedKeysTable_SearchObject(HadbIndexColumn.Key4, typeName, key.Key4, nameSpace);
            if (obj != null) if (ret == null) ret = obj; else throw new CoresLibException($"Two or more objects match. Key4 = '{key.Key4}'");
        }

        if (key.Key5._IsFilled())
        {
            var obj = data.IndexedKeysTable_SearchObject(HadbIndexColumn.Key5, typeName, key.Key5, nameSpace);
            if (obj != null) if (ret == null) ret = obj; else throw new CoresLibException($"Two or more objects match. Key5 = '{key.Key5}'");
        }

        return ret;
    }

    public IEnumerable<HadbObject> IndexedKeysTable_SearchByKeys(HadbKeys key, string typeName, string nameSpace)
    {
        nameSpace = nameSpace._HadbNameSpaceNormalize();
        typeName = typeName._NonNullTrim();

        List<HadbObject> list = new List<HadbObject>();

        var data = this.InternalData;

        if (key.Key1._IsFilled())
        {
            var obj = data.IndexedKeysTable_SearchObject(HadbIndexColumn.Key1, typeName, key.Key1, nameSpace);
            if (obj != null) list.Add(obj);
        }

        if (key.Key2._IsFilled())
        {
            var obj = data.IndexedKeysTable_SearchObject(HadbIndexColumn.Key2, typeName, key.Key2, nameSpace);
            if (obj != null) list.Add(obj);
        }

        if (key.Key3._IsFilled())
        {
            var obj = data.IndexedKeysTable_SearchObject(HadbIndexColumn.Key3, typeName, key.Key3, nameSpace);
            if (obj != null) list.Add(obj);
        }

        if (key.Key4._IsFilled())
        {
            var obj = data.IndexedKeysTable_SearchObject(HadbIndexColumn.Key4, typeName, key.Key4, nameSpace);
            if (obj != null) list.Add(obj);
        }

        if (key.Key5._IsFilled())
        {
            var obj = data.IndexedKeysTable_SearchObject(HadbIndexColumn.Key5, typeName, key.Key5, nameSpace);
            if (obj != null) list.Add(obj);
        }

        return list;
    }

    public IEnumerable<HadbObject> IndexedLabelsTable_SearchByLabels(HadbLabels labels, string typeName, string nameSpace)
    {
        nameSpace = nameSpace._HadbNameSpaceNormalize();
        typeName = typeName._NonNullTrim();

        IEnumerable<HadbObject>? ret = null;

        IEnumerable<HadbObject>? tmp1 = null;
        IEnumerable<HadbObject>? tmp2 = null;
        IEnumerable<HadbObject>? tmp3 = null;
        IEnumerable<HadbObject>? tmp4 = null;
        IEnumerable<HadbObject>? tmp5 = null;

        var data = this.InternalData;

        if (labels.Label1._IsFilled())
        {
            tmp1 = data.IndexedLabelsTable_SearchObjects(HadbIndexColumn.Label1, typeName, labels.Label1, nameSpace);
            if (ret == null) ret = tmp1;
        }

        if (labels.Label2._IsFilled())
        {
            tmp2 = data.IndexedLabelsTable_SearchObjects(HadbIndexColumn.Label2, typeName, labels.Label2, nameSpace);
            if (ret == null) ret = tmp2;
        }

        if (labels.Label3._IsFilled())
        {
            tmp3 = data.IndexedLabelsTable_SearchObjects(HadbIndexColumn.Label3, typeName, labels.Label3, nameSpace);
            if (ret == null) ret = tmp3;
        }

        if (labels.Label4._IsFilled())
        {
            tmp4 = data.IndexedLabelsTable_SearchObjects(HadbIndexColumn.Label4, typeName, labels.Label4, nameSpace);
            if (ret == null) ret = tmp4;
        }

        if (labels.Label5._IsFilled())
        {
            tmp4 = data.IndexedLabelsTable_SearchObjects(HadbIndexColumn.Label5, typeName, labels.Label5, nameSpace);
            if (ret == null) ret = tmp5;
        }

        if (ret == null) return EmptyOf<HadbObject>();

        if (tmp1 != null && object.ReferenceEquals(ret, tmp1) == false) ret = ret.Intersect(tmp1);
        if (tmp2 != null && object.ReferenceEquals(ret, tmp2) == false) ret = ret.Intersect(tmp2);
        if (tmp3 != null && object.ReferenceEquals(ret, tmp3) == false) ret = ret.Intersect(tmp3);
        if (tmp4 != null && object.ReferenceEquals(ret, tmp4) == false) ret = ret.Intersect(tmp4);
        if (tmp5 != null && object.ReferenceEquals(ret, tmp5) == false) ret = ret.Intersect(tmp5);

        return ret;
    }
}

public class HadbLogQueryResponse
{
    public IEnumerable<HadbLog> Data;
    public int Count;

    public HadbLogQueryResponse(IEnumerable<HadbLog> data)
    {
        this.Data = data;
        this.Count = data.Count();
    }

    public HadbLogQueryResponse(int count)
    {
        this.Data = null!;
        this.Count = count;
    }
}

[Flags]
public enum HadbLogQueryFlags : long
{
    None = 0,
    UseIndexTime = 1,
    WithNoLock = 2,
}

public class HadbLogQuery : INormalizable
{
    public int MaxReturmItems { get; set; } = 0;
    public string Uid { get; set; } = "";
    public DateTimeOffset TimeStart { get; set; } = Util.ZeroDateTimeOffsetValue;
    public DateTimeOffset TimeEnd { get; set; } = Util.ZeroDateTimeOffsetValue;
    public long SnapshotNoStart { get; set; } = 0;
    public long SnapshotNoEnd { get; set; } = 0;
    public HadbLabels Labels { get; set; } = new HadbLabels("");
    public HadbLog? SearchTemplate { get; set; } = null;
    public FullTextSearchFlags FullTextFlags { get; set; } = FullTextSearchFlags.None;
    public string FullTextQuery { get; set; } = "";
    public bool RetOnlyCount { get; set; } = false;
    public HadbLogQueryFlags Flags { get; set; } = HadbLogQueryFlags.None;

    public void Normalize()
    {
        this.Uid = this.Uid._NormalizeUid();
        this.TimeStart = this.TimeStart._NormalizeDateTimeOffset();
        this.TimeEnd = this.TimeEnd._NormalizeDateTimeOffset();
        this.FullTextQuery = this.FullTextQuery._NonNull();
    }
}

public class HadbBackupDatabase
{
    public DateTimeOffset TimeStamp = ZeroDateTimeOffsetValue;
    public List<HadbSerializedObject> ObjectsList = null!;
}

[Flags]
public enum HadbEventType
{
    DynamicConfigChanged = 0,
    ReloadDataFull,
    ReloadDataPartially,
    DatabaseStateChangedToRecovery,
    DatabaseStateChangedToFailure,
}

[Flags]
public enum HadbSearchFlags : long
{
    None = 0,
    WordMode = 1,
}

public class HadbCurrentMetrics
{
    public int NumMemoryObjects;
}

public class HadbStatJson
{
    public DateTimeOffset TimeStamp;
    public long SnapshotNo;
    public string JsonData = "";
    public string Generator = "";
    public string Ext1 = "";
    public string Ext2 = "";
}


public class HadbStat
{
    public DateTimeOffset TimeStamp;
    public long SnapshotNo;
    public string Generator = "";
    public JObject? UserData = null;

    public HadbStat() { }

    public HadbStat(HadbStatJson src)
    {
        this.TimeStamp = src.TimeStamp;
        this.SnapshotNo = src.SnapshotNo;
        this.Generator = src.Generator;
        this.UserData = src.JsonData._JsonToJsonObject();
    }
}


public abstract class HadbBase<TMem, TDynamicConfig> : AsyncService
    where TMem : HadbMemDataBase, new()
    where TDynamicConfig : HadbDynamicConfig, new()
{
    Task ReloadMainLoopTask;
    Task LazyUpdateMainLoopTask;

    public bool IsLoopStarted { get; private set; } = false;

    public int DbLastFullReloadTookMsecs { get; private set; } = 0;
    public int DbLastPartialReloadTookMsecs { get; private set; } = 0;

    public int MemLastFullReloadTookMsecs { get; private set; } = 0;
    public int MemLastPartialReloadTookMsecs { get; private set; } = 0;

    public long MemCurrentBackupFileSizeBytes { get; private set; } = 0;

    public int LastLazyUpdateTookMsecs { get; private set; } = 0;
    public bool IsDatabaseConnectedForReload { get; private set; } = false;
    public Exception LastDatabaseConnectForReloadError { get; private set; } = new CoresException("Unknown error.");
    public int DatabaseConnectForReloadErrorCount { get; private set; } = 0;
    public bool IsDatabaseConnectedForLazyWrite { get; private set; } = false;

    public long LastDatabaseReloadTick { get; private set; } = 0;
    public long LastDatabaseFullReloadTick { get; private set; } = 0;
    public DateTimeOffset LastDatabaseReloadTime { get; private set; } = ZeroDateTimeOffsetValue;
    public DateTimeOffset LastDatabaseFullReloadTime { get; private set; } = ZeroDateTimeOffsetValue;

    public Exception? LastMemDbBackupLocalLoadError { get; private set; } = null;

    public AsyncEventListenerList<HadbBase<TMem, TDynamicConfig>, HadbEventType> EventListenerList = new AsyncEventListenerList<HadbBase<TMem, TDynamicConfig>, HadbEventType>();

    public HadbSettingsBase Settings { get; }
    public string SystemName => Settings.SystemName;
    bool IsEnabled_NoMemDb => Settings.OptionFlags.Bit(HadbOptionFlags.NoMemDb);

    TDynamicConfig _CurrentDynamicConfig;
    bool _IsDynamicConfigInited = false;

    public TDynamicConfig CurrentDynamicConfig
    {
        [MethodImpl(Inline)]
        get
        {
            if (_IsDynamicConfigInited == false)
            {
                throw new CoresLibException("CurrentDynamicConfig is not loaded yet.");
            }

            return this._CurrentDynamicConfig;
        }
    }

    public bool IsDynamicConfigInited => _IsDynamicConfigInited;

    public TMem? MemDb { get; private set; } = null;

    public int GetLazyUpdateQueueLength() => this.MemDb?.GetLazyUpdateQueueLength() ?? 0;

    readonly AsyncLock DynamicConfigValueDbLockAsync = new AsyncLock();

    protected abstract Task<HadbTran> BeginDatabaseTransactionImplAsync(bool writeMode, bool isTransaction, HadbTranOptions options, CancellationToken cancel = default);

    protected internal abstract Task AtomicAddDataListToDatabaseImplAsync(HadbTran tran, IEnumerable<HadbObject> dataList, CancellationToken cancel = default);
    protected internal abstract Task<HadbObject?> AtomicGetDataFromDatabaseImplAsync(HadbTran tran, string uid, string typeName, string nameSpace, CancellationToken cancel = default, bool noCheckTypeIdAndNameSpace = false);
    protected internal abstract Task<IEnumerable<HadbObject>> AtomicGetArchivedDataFromDatabaseImplAsync(HadbTran tran, int maxItems, string uid, string typeName, string nameSpace, CancellationToken cancel = default, bool noCheckTypeIdAndNameSpace = false);
    protected internal abstract Task<HadbObject?> AtomicSearchDataByKeyFromDatabaseImplAsync(HadbTran tran, HadbKeys keys, string typeName, string nameSpace, bool and, CancellationToken cancel = default);
    protected internal abstract Task<IEnumerable<HadbObject>> AtomicSearchDataListByLabelsFromDatabaseImplAsync(HadbTran tran, HadbLabels labels, string typeName, string nameSpace, CancellationToken cancel = default);
    protected internal abstract Task<HadbObject> AtomicDeleteDataFromDatabaseImplAsync(HadbTran tran, string uid, string typeName, string nameSpace, int maxArchive, CancellationToken cancel = default);
    protected internal abstract Task<HadbObject> AtomicUpdateDataOnDatabaseImplAsync(HadbTran tran, HadbObject data, CancellationToken cancel = default);
    protected internal abstract Task<string> AtomicGetKvImplAsync(HadbTran tran, string key, CancellationToken cancel = default);
    protected internal abstract Task<StrDictionary<string>> AtomicGetKvListImplAsync(HadbTran tran, CancellationToken cancel = default);
    protected internal abstract Task AtomicSetKvImplAsync(HadbTran tran, string key, string value, CancellationToken cancel = default);
    protected internal abstract Task AtomicAddSnapImplAsync(HadbTran tran, HadbSnapshot snap, CancellationToken cancel = default);

    protected internal abstract Task<HadbQuick<T>?> AtomicGetQuickFromDatabaseImplAsync<T>(HadbTran tran, string uid, string nameSpace, CancellationToken cancel = default);
    protected internal abstract Task<IEnumerable<HadbQuick<T>>> AtomicSearchQuickByKeyOnDatabaseImplAsync<T>(HadbTran tran, string key, bool startWith, string nameSpace, CancellationToken cancel = default);
    protected internal abstract Task AtomicUpdateQuickByUidOnDatabaseImplAsync<T>(HadbTran tran, string uid, string nameSpace, T userData, CancellationToken cancel = default);
    protected internal abstract Task<bool> AtomicAddOrUpdateQuickByKeyOnDatabaseImplAsync<T>(HadbTran tran, string key, string nameSpace, T userData, bool doNotOverwrite, CancellationToken cancel = default);
    protected internal abstract Task<bool> AtomicDeleteQuickByUidOnDatabaseImplAsync<T>(HadbTran tran, string uid, string nameSpace, CancellationToken cancel = default);
    protected internal abstract Task<int> AtomicDeleteQuickByKeyOnDatabaseImplAsync<T>(HadbTran tran, string key, bool startWith, string nameSpace, CancellationToken cancel = default);

    protected internal abstract Task AtomicAddLogImplAsync(HadbTran tran, HadbLog log, string nameSpace, string ext1, string ext2, string ft1, string ft2, CancellationToken cancel = default);
    protected internal abstract Task<HadbLogQueryResponse> AtomicSearchLogImplAsync(HadbTran tran, string typeName, HadbLogQuery query, string nameSpace, CancellationToken cancel = default);

    protected internal abstract Task<bool> LazyUpdateImplAsync(HadbTran tran, HadbObject data, CancellationToken cancel = default);

    protected abstract Task<KeyValueList<string, string>> LoadDynamicConfigFromDatabaseImplAsync(CancellationToken cancel = default);
    protected abstract Task AppendMissingDynamicConfigToDatabaseImplAsync(KeyValueList<string, string> missingValues, bool replaceAll, CancellationToken cancel = default);
    protected abstract Task<List<HadbObject>> ReloadDataFromDatabaseImplAsync(bool fullReloadMode, DateTimeOffset partialReloadMinUpdateTime, Ref<DateTimeOffset>? lastStatTimestamp, CancellationToken cancel = default);
    protected abstract Task RestoreDataFromHadbObjectListImplAsync(List<HadbObject> objectList, CancellationToken cancel = default);

    protected abstract Task WriteStatImplAsync(HadbTran tran, DateTimeOffset dt, string generator, string value, string ext1, string ext2, CancellationToken cancel = default);
    protected abstract Task<List<HadbStatJson>> EnumStatImplAsync(HadbTran tran, DateTimeOffset dtStart, DateTimeOffset dtEnd, int maxCount, CancellationToken cancel = default);

    protected abstract bool IsDeadlockExceptionImpl(Exception ex);


    protected virtual Task AddAdditionalStatAsync(EasyJsonStrAttributes stat, List<HadbObject> objectList, CancellationToken cancel = default) => TaskCompleted;

    public StrDictionary<Type> DefinedDataTypesByName { get; }
    public StrDictionary<Type> DefinedLogTypesByName { get; }

    public DeadlockRetryConfig DefaultDeadlockRetryConfig { get; set; }

    public HadbDebugFlags DebugFlags { get; set; } = HadbDebugFlags.None;

    public EasyJsonStrAttributes? LatestStatData = null;

    public HadbBase(HadbSettingsBase settings, TDynamicConfig initialDynamicConfig)
    {
        try
        {
            settings._NullCheck(nameof(settings));

            if (settings.SystemName._IsEmpty()) throw new CoresLibException("SystemName is empty.");

            this.Settings = settings; // CloneDeep 禁止

            initialDynamicConfig._NullCheck(nameof(initialDynamicConfig));
            this._CurrentDynamicConfig = initialDynamicConfig;
            this._CurrentDynamicConfig.Normalize();

            this.ReloadMainLoopTask = ReloadMainLoopAsync(this.GrandCancel)._LeakCheck();
            this.LazyUpdateMainLoopTask = LazyUpdateMainLoopAsync(this.GrandCancel)._LeakCheck();

            TMem tmpMem = new TMem();
            this.DefinedDataTypesByName = tmpMem.GetDefinedUserDataTypesByName();
            this.DefinedLogTypesByName = tmpMem.GetDefinedUserLogTypesByName();

            this.DefaultDeadlockRetryConfig = new DeadlockRetryConfig(CoresConfig.Database.DefaultDatabaseTransactionRetryAverageIntervalSecs, CoresConfig.Database.DefaultDatabaseTransactionRetryCount, CoresConfig.Database.DefaultDatabaseTransactionRetryIntervalMaxFactor);
        }
        catch
        {
            this._DisposeSafe();
            throw;
        }
    }

    public Type? GetDataTypeByTypeName(string name) => this.DefinedDataTypesByName._GetOrDefault(name);

    public Type GetDataTypeByTypeName(string name, EnsureSpecial notFoundError)
    {
        Type? ret = GetDataTypeByTypeName(name);

        if (ret == null)
        {
            throw new CoresException($"Type name '{name}' not found.");
        }

        return ret;
    }

    public HadbData JsonToHadbData(string json, string typeName)
    {
        Type t = GetDataTypeByTypeName(typeName, EnsureSpecial.Yes);

        HadbData? ret = (HadbData?)json._JsonToObject(t);
        if (ret == null)
        {
            throw new CoresLibException("_JsonToObject() returned null.");
        }

        return ret;
    }


    public Type? GetLogTypeByTypeName(string name) => this.DefinedLogTypesByName._GetOrDefault(name);

    public Type GetLogTypeByTypeName(string name, EnsureSpecial notFoundError)
    {
        Type? ret = GetLogTypeByTypeName(name);

        if (ret == null)
        {
            throw new CoresException($"Type name '{name}' not found.");
        }

        return ret;
    }

    public HadbLog JsonToHadbLog(string json, string typeName)
    {
        Type t = GetLogTypeByTypeName(typeName, EnsureSpecial.Yes);

        HadbLog? ret = (HadbLog?)json._JsonToObject(t);
        if (ret == null)
        {
            throw new CoresLibException("_JsonToObject() returned null.");
        }

        return ret;
    }


    public void Start()
    {
        this.IsLoopStarted = true;
    }

    public HadbCurrentMetrics GetCurrentMetrics()
    {
        this.CheckIfReady(false);
        return this.MemDb!.GetCurrentMetrics();
    }

    readonly AsyncLock Lock_ReloadDynamicConfigValuesAsync = new AsyncLock();

    Once ReloadDynamicConfigOnce;

    public async Task SetDynamincConfigStringAsync(string newConfig, CancellationToken cancel = default)
    {
        var lines = newConfig._GetLines(true, true);

        KeyValueList<string, string> configList = new KeyValueList<string, string>();

        foreach (var line in lines)
        {
            string line2 = line.TrimStart();

            if (line2._GetKeyAndValue(out string key, out string value))
            {
                if (key._IsFilled())
                {
                    configList.Add(key, value.Trim());
                }
            }
        }

        // 正規化
        TDynamicConfig dynConfig = new TDynamicConfig();
        configList = dynConfig.UpdateFromKeyValueListAndReturnKeyValueList(configList, false);

        await this.AppendMissingDynamicConfigToDatabaseImplAsync(configList, true, cancel);

        // 早速リロードして適用する
        try
        {
            await this.ReloadDynamicConfigValuesAsync(cancel);
        }
        catch (Exception ex)
        {
            ex._Error();
        }
    }

    readonly Singleton<TDynamicConfig> DynamicConfigSampleObjectSingleton = new Singleton<TDynamicConfig>(() => new TDynamicConfig());
    public TDynamicConfig DynamicConfigSampleObject => DynamicConfigSampleObjectSingleton;

    public async Task<string> GetDynamicConfigStringAsync(CancellationToken cancel = default)
    {
        await ReloadDynamicConfigValuesAsync(cancel);

        var commentsDict = DynamicConfigSampleObject.GetCommentsDictionary();

        var config = await this.LoadDynamicConfigFromDatabaseImplAsync(cancel);

        StringWriter w = new StringWriter();

        int keyStandardLength = 50;

        int keyStandardLength2 = config.Max(x => x.Key.Length);

        keyStandardLength = Math.Max(keyStandardLength, keyStandardLength2);

        w.WriteLine("### Configuration Text");

        string lastKey = "";

        foreach (var kv in config)
        {
            int len1 = kv.Key.Length;
            string padding = "";

            if (len1 < keyStandardLength)
            {
                padding = Str.MakeCharArray(' ', keyStandardLength - len1);
            }

            string line = $"{kv.Key}{padding} {kv.Value}";

            if (lastKey._IsDiffi(kv.Key))
            {
                lastKey = kv.Key;
                w.WriteLine();

                string? commentStr = commentsDict._GetOrDefault(kv.Key, "");
                if (commentStr._IsFilled())
                {
                    foreach (string commentLine in commentStr._GetLines())
                    {
                        if (commentLine._IsFilled())
                        {
                            w.WriteLine("# " + commentLine.TrimEnd());
                        }
                    }
                }
            }

            w.WriteLine(line);
        }

        w.WriteLine();
        w.WriteLine();

        return w.ToString();
    }

    async Task ReloadDynamicConfigValuesAsync(CancellationToken cancel)
    {
        string oldDynConfigJson = this._CurrentDynamicConfig._ObjectToJson();

        using (await Lock_ReloadDynamicConfigValuesAsync.LockWithAwait(cancel))
        {
            using (await DynamicConfigValueDbLockAsync.LockWithAwait(cancel))
            {
                // DynamicConfig の最新値を DB から読み込む
                var loadedDynamicConfigValues = await this.LoadDynamicConfigFromDatabaseImplAsync(cancel);

                // 読み込んだ DynamicConfig の最新値を適用する
                var missingDynamicConfigValues = this._CurrentDynamicConfig.UpdateFromKeyValueListAndReturnKeyValueList(loadedDynamicConfigValues, true);

                this._IsDynamicConfigInited = true;

                // 不足している DynamicConfig のデフォルト値を DB に書き込む
                if (missingDynamicConfigValues.Any())
                {
                    if (this.Settings.OptionFlags.Bit(HadbOptionFlags.NoInitConfigDb) == false) // オプションで書き込まない設定になっていない限り
                    {
                        await this.AppendMissingDynamicConfigToDatabaseImplAsync(missingDynamicConfigValues, false, cancel);
                    }
                }
            }
        }

        string newDynConfigJson = this._CurrentDynamicConfig._ObjectToJson();

        if (ReloadDynamicConfigOnce.IsFirstCall() || oldDynConfigJson != newDynConfigJson)
        {
            await this.EventListenerList.FireAsync(this, HadbEventType.DynamicConfigChanged, debugLog: true);
        }
    }

    readonly AsyncLock Lock_UpdateCoreAsync = new AsyncLock();

    public async Task LazyUpdateCoreAsync(EnsureSpecial yes, CancellationToken cancel = default)
    {
        //using (await Lock_UpdateCoreAsync.LockWithAwait(cancel)) // ロック不要? 2022/02/16 登
        {
            try
            {
                Ref<bool> anySqlConnectionError = false;

                // 現在 キューに入っている項目に対する Lazy Update の実行
                // キューは Immutable なので、現在の Queue を取得する
                var queue = this.MemDb!._LazyUpdateQueue;

                // キューに入っている全項目を並列化して処理する
                var queuedObjectsList = queue.Keys.ToList();

                // 非トランザクションの SQL 接続のリスト
                int numTasks = this.Settings.LazyUpdateParallelQueueCount;
                if (numTasks <= 0) numTasks = 1;

                HadbTran?[] tranArray = new HadbTran[numTasks];

                try
                {
                    await queuedObjectsList._DoForEachParallelAsync(async (q, taskIndex) =>
                    {
                        // このタスク用の SQL 接続が未接続の場合、接続を実施する
                        if (tranArray[taskIndex] == null)
                        {
                            try
                            {
                                tranArray[taskIndex] = await this.BeginDatabaseTransactionImplAsync(true, false, HadbTranOptions.None, cancel);
                            }
                            catch (Exception ex)
                            {
                                // SQL 接続に失敗
                                ex._Error();

                                anySqlConnectionError.Set(true);

                                return;
                            }
                        }

                        HadbTran tran = tranArray[taskIndex]!;

                        var copyOfQ = q.ToNonMemoryDbObject();

                        bool ok = true;

                        if (copyOfQ.Deleted == false)
                        {
                            try
                            {
                                // 1 つの要素について DB 更新を行なう
                                await this.LazyUpdateImplAsync(tran, copyOfQ, cancel);
                            }
                            catch (Exception ex)
                            {
                                ok = false;

                                ex._Debug();
                            }
                        }

                        // DB 更新に成功した場合は、DB 更新中にこのオブジェクトの内容の変更があったかどうか確認する
                        if (ok)
                        {
                            if (copyOfQ.InternalFastUpdateVersion == q.InternalFastUpdateVersion)
                            {
                                // このオブジェクトの内容の変化がなければキューからこのオブジェクトを削除する
                                this.MemDb!.DeleteFromLazyUpdateQueueInternal(q);
                            }
                        }
                    },
                    numTasks,
                    MultitaskDivideOperation.RoundRobin,
                    cancel: cancel);
                }
                finally
                {
                    // SQL 接続をすべて解放する
                    foreach (var tran in tranArray)
                    {
                        if (tran != null)
                        {
                            await tran._DisposeSafeAsync();
                        }
                    }
                }

                this.IsDatabaseConnectedForLazyWrite = !anySqlConnectionError;
            }
            catch
            {
                this.IsDatabaseConnectedForLazyWrite = false;

                throw;
            }
        }
    }

    public void UpdateSystemRelatimeStat(EasyJsonStrAttributes stat)
    {
        // 追加の統計情報 (標準)
        long processMemory = 0;
        int numThreads = 0;
        int numHandles = 0;
        try
        {
            var proc = Process.GetCurrentProcess();
            processMemory = proc.PrivateMemorySize64;
            numThreads = proc.Threads.Count;
            numHandles = proc.HandleCount;
        }
        catch { }

        CoresRuntimeStat sys = new CoresRuntimeStat();
        sys.Refresh(forceGc: false);
        stat.Set("Sys/DotNet/NumRunningTasks", sys.Task);
        stat.Set("Sys/DotNet/NumDelayedTasks", sys.D);
        stat.Set("Sys/DotNet/NumTimerTasks", sys.Q);
        stat.Set("Sys/DotNet/NumObjects", sys.Obj);
        stat.Set("Sys/DotNet/CpuUsage", sys.Cpu);
        stat.Set("Sys/DotNet/ManagedMemory_MBytes", (double)sys.Mem / 1024.0);
        stat.Set("Sys/DotNet/ProcessMemory_MBytes", (double)processMemory / 1024.0 / 1024.0);
        stat.Set("Sys/DotNet/NumNativeThreads", numThreads);
        stat.Set("Sys/DotNet/NumNativeHandles", numHandles);
        stat.Set("Sys/DotNet/GcTotal", sys.Gc);
        stat.Set("Sys/DotNet/Gc0", sys.Gc0);
        stat.Set("Sys/DotNet/Gc1", sys.Gc1);
        stat.Set("Sys/DotNet/Gc2", sys.Gc2);
        stat.Set("Sys/DotNet/BootDays", (double)Time.Tick64 / (double)(24 * 60 * 60 * 1000));
        stat.Set("Hadb/Sys/DbLazyUpdateQueueLength", this.GetLazyUpdateQueueLength());
        stat.Set("Hadb/Sys/DbConcurrentTranRead", this.DbConcurrentTranReadTotal);
        stat.Set("Hadb/Sys/DbConcurrentTranWrite", this.DbConcurrentTranWriteTotal);
        stat.Set("Hadb/Sys/DbTotalDeadlockCount", this.DbTotalDeadlockCountTotal);
        stat.Set("Hadb/Sys/DbLastFullReloadTookMsecs", this.DbLastFullReloadTookMsecs);
        stat.Set("Hadb/Sys/DbLastPartialReloadTookMsecs", this.DbLastPartialReloadTookMsecs);
        stat.Set("Hadb/Sys/IsDatabaseConnectedForLazyWrite", this.IsDatabaseConnectedForLazyWrite);
        stat.Set("Hadb/Sys/IsDatabaseConnectedForReload", this.IsDatabaseConnectedForReload);
        stat.Set("Hadb/Sys/MemCurrentBackupFileSize_MBytes", (double)this.MemCurrentBackupFileSizeBytes / 1024.0 / 1024.0);
        stat.Set("Hadb/Sys/MemLastFullReloadTookMsecs", this.MemLastFullReloadTookMsecs);
        stat.Set("Hadb/Sys/MemLastPartialReloadTookMsecs", this.MemLastPartialReloadTookMsecs);
    }

    async Task<EasyJsonStrAttributes> GenerateStatInternalAsync(List<HadbObject> objectList, CancellationToken cancel = default)
    {
        EasyJsonStrAttributes stat = new EasyJsonStrAttributes();

        stat.Set("Stat/TimeStamp", DtOffsetNow._ToDtStr(true));

        Dictionary<string, long> dict = new Dictionary<string, long>(StrCmpi);

        long totalCount = 0;

        // オブジェクトごとの個数のカウント
        // hadb/objcount/NameSpace/TypeName
        foreach (var obj in objectList)
        {
            if (obj.Deleted == false && obj.Archive == false)
            {
                string key = $"Hadb/ObjCount/{obj.NameSpace}/{obj.GetUserDataTypeName()}";

                dict._Inc(key);

                totalCount++;
            }
        }

        // すべてのオブジェクト総数
        dict["Hadb/ObjCount/_AllObjects"] = totalCount;

        foreach (var kv in dict)
        {
            stat.TryAdd(kv.Key, kv.Value.ToString());
        }

        // 追加の統計情報 (開発者が定義)
        await this.AddAdditionalStatAsync(stat, objectList, cancel);

        // リアルタイム統計 (システム状態) の更新
        UpdateSystemRelatimeStat(stat);

        return stat;
    }

    bool StatSaveNotSupported = false;

    public async Task ReloadCoreAsync(EnsureSpecial yes, bool fullReloadMode = true, DateTimeOffset partialReloadMinUpdateTime = default, CancellationToken cancel = default)
    {
        if (fullReloadMode == false)
        {
            if (partialReloadMinUpdateTime._IsZeroDateTime())
            {
                partialReloadMinUpdateTime = this.LastDatabaseReloadTime - this._CurrentDynamicConfig.HadbReloadTimeShiftMarginMsecs._ToTimeSpanMSecs();
            }
        }

        FilePath backupDatabasePath = this.Settings.BackupDataFile;
        FilePath backupDynamicConfigPath = this.Settings.BackupDynamicConfigFile;

        try
        {
            long nowTick;
            DateTimeOffset nowTime;

            if (this.DebugFlags.Bit(HadbDebugFlags.CauseErrorOnDatabaseReload) || this.DebugFlags.Bit(HadbDebugFlags.NoDbLoadAndUseOnlyLocalBackup))
            {
                throw new CoresException("Dummy Exception for Debug: CauseErrorOnDatabaseReload");
            }

            // Dynamic Config の値の読み込み
            await ReloadDynamicConfigValuesAsync(cancel);

            using (await Lock_UpdateCoreAsync.LockWithAwait(cancel))
            {
                nowTick = Time.Tick64;
                nowTime = DtOffsetNow;

                if (this.IsEnabled_NoMemDb == false)
                {
                    // DB からオブジェクト一覧を読み込む
                    Ref<DateTimeOffset> lastStatWrittenTimeStamp = new Ref<DateTimeOffset>(ZeroDateTimeOffsetValue);

                    Debug($"ReloadCoreAsync: Start Reload Data From Database. fullReloadMode = {fullReloadMode}, partialReloadMinUpdateTime = {partialReloadMinUpdateTime._ToDtStr(true)}");

                    long dbStartTick = Time.HighResTick64;
                    List<HadbObject> loadedObjectsList = await this.ReloadDataFromDatabaseImplAsync(fullReloadMode, partialReloadMinUpdateTime, lastStatWrittenTimeStamp, cancel);
                    long dbEndTick = Time.HighResTick64;

                    Debug($"ReloadCoreAsync: Finished Reload Data From Database. fullReloadMode = {fullReloadMode}, Objects = {loadedObjectsList.Count._ToString3()}, Took time: {((int)(dbEndTick - dbStartTick))._ToTimeSpanMSecs()._ToTsStr(true)}");

                    if (fullReloadMode)
                    {
                        DbLastFullReloadTookMsecs = (int)(dbEndTick - dbStartTick);
                    }
                    else
                    {
                        DbLastPartialReloadTookMsecs = (int)(dbEndTick - dbStartTick);
                    }

                    TMem? currentMemDb = this.MemDb;
                    if (currentMemDb == null) currentMemDb = new TMem();

                    Debug($"ReloadCoreAsync: Start Reload Data Into Memory. fullReloadMode = {fullReloadMode}, Objects = {loadedObjectsList.Count._ToString3()}, partialReloadMinUpdateTime = {partialReloadMinUpdateTime._ToDtStr(true)}");

                    long memStartTick = Time.HighResTick64;
                    await currentMemDb.ReloadFromDatabaseIntoMemoryDbAsync(loadedObjectsList, fullReloadMode, partialReloadMinUpdateTime, "Database", cancel);
                    long memEndTick = Time.HighResTick64;

                    Debug($"ReloadCoreAsync: Finished Reload Data From Database. fullReloadMode = {fullReloadMode}, Objects = {loadedObjectsList.Count._ToString3()}, Took time: {((int)(memEndTick - memStartTick))._ToTimeSpanMSecs()._ToTsStr(true)}");

                    if (fullReloadMode)
                    {
                        MemLastFullReloadTookMsecs = (int)(memEndTick - memStartTick);
                    }
                    else
                    {
                        MemLastPartialReloadTookMsecs = (int)(memEndTick - memStartTick);
                    }

                    this.MemDb = currentMemDb;

                    if (fullReloadMode)
                    {
                        if (this.Settings.OptionFlags.Bit(HadbOptionFlags.NoLocalBackup) == false)
                        {
                            try
                            {
                                // DB のデータをローカルバックアップファイルに書き込む
                                await backupDynamicConfigPath.FileSystem.WriteJsonToFileAsync(backupDynamicConfigPath.PathString, this._CurrentDynamicConfig,
                                    backupDynamicConfigPath.Flags | FileFlags.AutoCreateDirectory | FileFlags.OnCreateSetCompressionFlag,
                                    cancel: cancel, withBackup: true);

                                Debug($"ReloadCoreAsync: Start Local Backup Memory Processing. Objects = {loadedObjectsList.Count._ToString3()}");

                                HadbBackupDatabase backupData = new HadbBackupDatabase
                                {
                                    ObjectsList = await loadedObjectsList._ProcessParallelAndAggregateAsync((x, taskIndex) =>
                                    {
                                        List<HadbSerializedObject> ret = new List<HadbSerializedObject>();

                                        foreach (var obj in x)
                                        {
                                            if (obj.Archive == false && obj.Deleted == false)
                                            {
                                                ret.Add(obj.ToSerializedObject());
                                            }
                                        }

                                        return TR(ret);
                                    }, cancel: cancel),
                                    TimeStamp = nowTime,
                                };

                                Debug($"ReloadCoreAsync: Start Local Backup Physical File Write. Objects = {backupData.ObjectsList.Count._ToString3()}");
                                long size = await backupDatabasePath.FileSystem.WriteJsonToFileAsync(backupDatabasePath.PathString, backupData,
                                    backupDynamicConfigPath.Flags | FileFlags.AutoCreateDirectory | FileFlags.OnCreateSetCompressionFlag,
                                    cancel: cancel, withBackup: true);

                                this.MemCurrentBackupFileSizeBytes = size;

                                Debug($"ReloadCoreAsync: Finished Local Backup Physical File Write. File Size: {size._ToString3()} bytes, Objects = {backupData.ObjectsList.Count._ToString3()}, File Path = '{backupDatabasePath.PathString}'");
                            }
                            catch (Exception ex)
                            {
                                // ローカルバックアップへの書き込みに失敗してもサービスは続行する
                                ex._Error();
                            }
                        }
                    }

                    this.IsDatabaseConnectedForReload = true;

                    if (fullReloadMode)
                    {
                        // stat を定期的に保存する
                        bool saveStat = false;

                        // stat のデータそのものは毎回生成する
                        var statData = await this.GenerateStatInternalAsync(loadedObjectsList, cancel);

                        this.LatestStatData = statData;

                        if (this._CurrentDynamicConfig.HadbAutomaticStatIntervalMsecs >= 1 && (lastStatWrittenTimeStamp.Value._IsZeroDateTime() || (DtOffsetNow >= (lastStatWrittenTimeStamp.Value + this._CurrentDynamicConfig.HadbAutomaticStatIntervalMsecs._ToTimeSpanMSecs()))))
                        {
                            saveStat = true;
                        }

                        if (this.Settings.OptionFlags.Bit(HadbOptionFlags.DoNotSaveStat))
                        {
                            saveStat = false;
                        }

                        if (StatSaveNotSupported)
                        {
                            saveStat = false;
                        }

                        if (saveStat)
                        {
                            // stat をデータベースに追記する
                            try
                            {
                                await this.TranAsync(true, async tran =>
                                {
                                    await this.WriteStatImplAsync(tran, DtOffsetNow, Env.DnsFqdnHostName, statData.ToJsonString(), "", "", cancel);

                                    return true;
                                },
                                cancel: cancel,
                                ignoreQuota: true);
                            }
                            catch (NotImplementedException)
                            {
                                StatSaveNotSupported = true;
                            }
                            catch (Exception ex)
                            {
                                ex._Error();
                            }
                        }
                    }
                }
                else
                {
                    // DB からオブジェクト一覧は読み込まない
                    TMem? currentMemDb = this.MemDb;
                    if (currentMemDb == null) currentMemDb = new TMem(); // 適当に空を食わせておく
                    this.MemDb = currentMemDb;
                }
            }

            // 成功した場合は内部変数を更新
            this.LastDatabaseReloadTick = nowTick;
            this.LastDatabaseReloadTime = nowTime;

            if (fullReloadMode)
            {
                this.LastDatabaseFullReloadTick = nowTick;
                this.LastDatabaseFullReloadTime = nowTime;
            }

            this.IsDatabaseConnectedForReload = true;
        }
        catch (Exception ex)
        {
            if (this.DebugFlags.Bit(HadbDebugFlags.NoDbLoadAndUseOnlyLocalBackup) == false)
            {
                ex._Debug();
            }

            this.DatabaseConnectForReloadErrorCount++;
            this.LastDatabaseConnectForReloadError = ex;
            this.IsDatabaseConnectedForReload = false;

            if (fullReloadMode)
            {
                DbLastFullReloadTookMsecs = 0;
                MemLastFullReloadTookMsecs = 0;
            }
            else
            {
                DbLastPartialReloadTookMsecs = 0;
                MemLastPartialReloadTookMsecs = 0;
            }

            // データベースからもバックアップファイルからもまだデータが読み込まれていない場合は、バックアップファイルから読み込む
            if (this.MemDb == null)
            {
                if (this.IsEnabled_NoMemDb == false && this.Settings.OptionFlags.Bit(HadbOptionFlags.NoLocalBackup) == false)
                {
                    try
                    {
                        try
                        {
                            // DynamicConfig
                            TDynamicConfig loadDynamicConfig = await backupDynamicConfigPath.FileSystem.ReadJsonFromFileAsync<TDynamicConfig>(backupDynamicConfigPath.PathString,
                                cancel: cancel,
                                withBackup: true);

                            loadDynamicConfig.Normalize();

                            this._CurrentDynamicConfig = loadDynamicConfig;
                            this._IsDynamicConfigInited = true;
                        }
                        catch (Exception ex2)
                        {
                            // DynamicConfig の読み込みには失敗しても良いことにする
                            // ただし、エラーはログに出力する
                            if (this.DebugFlags.Bit(HadbDebugFlags.NoDbLoadAndUseOnlyLocalBackup) == false)
                            {
                                ex2._Error();
                            }
                        }

                        await this.EventListenerList.FireAsync(this, HadbEventType.DynamicConfigChanged, debugLog: true);

                        // データ本体
                        Debug($"ReloadCoreAsync: Start LoadLocalBackupDataAsync. Path = '{backupDatabasePath.PathString}'");
                        List<HadbObject> loadedObjectsList = await LoadLocalBackupDataAsync(backupDatabasePath, cancel);
                        Debug($"ReloadCoreAsync: Finished LoadLocalBackupDataAsync. loadedObjectsList Count = {loadedObjectsList.Count._ToString3()}");

                        TMem? currentMemDb = new TMem();

                        Debug($"ReloadCoreAsync: Start Restoring Local Backup Data Into Memory. Objects = {loadedObjectsList.Count._ToString3()}");

                        long memStartTick = Time.HighResTick64;
                        await currentMemDb.ReloadFromDatabaseIntoMemoryDbAsync(loadedObjectsList, true, default, $"Local Database Backup File '{backupDatabasePath.PathString}'", cancel);
                        long memEndTick = Time.HighResTick64;

                        Debug($"ReloadCoreAsync: Finished Restoring Local Backup Data Into Memory. Objects = {loadedObjectsList.Count._ToString3()}, Took time: {((int)(memEndTick - memStartTick))._ToTimeSpanMSecs()._ToTsStr(true)}");

                        this.MemDb = currentMemDb;
                    }
                    catch (Exception ex3)
                    {
                        // ローカルファイルからのデータベース読み込みに失敗
                        LastMemDbBackupLocalLoadError = ex3;
                    }

                }
                else
                {
                    // ローカルファイルからのデータベース読み込みがそもそも禁止されている
                    LastMemDbBackupLocalLoadError = ex;
                }
            }

            // バックアップファイルの読み込みを行なった上で、DB 例外はちゃんと throw する
            if (this.DebugFlags.Bit(HadbDebugFlags.NoDbLoadAndUseOnlyLocalBackup) == false)
            {
                throw;
            }
        }
    }


    // JSON のデシリアイズ時に型ごとに適切な変換を行なうクラス
    public class HadbSerializedObjectJsonReadConverter : JsonConverter
    {
        JsonSerializerSettings Settings;
        JsonSerializer Serializer = Json.CreateSerializer();

        public HadbBase<TMem, TDynamicConfig> Hadb { get; }
        public HadbSerializedObjectJsonReadConverter(HadbBase<TMem, TDynamicConfig> hadb) : base()
        {
            this.Hadb = hadb;

            this.Settings = new JsonSerializerSettings()
            {
                MaxDepth = Json.DefaultMaxDepth,
                NullValueHandling = NullValueHandling.Ignore,
                ObjectCreationHandling = ObjectCreationHandling.Replace,
                ReferenceLoopHandling = ReferenceLoopHandling.Error,
            };

            Json.AddStandardSettingsToJsonConverter(this.Settings);
        }

        public override bool CanConvert(Type objectType) => objectType == typeof(HadbSerializedObject);

        public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
            => throw new NotImplementedException();

        public override object ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
        {
            var jobject = JObject.ReadFrom(reader);

            if (jobject != null)
            {
                var obj = (HadbSerializedObject?)jobject.ToObject(typeof(HadbSerializedObject), this.Serializer);

                if (obj != null)
                {
                    Type type = this.Hadb.GetDataTypeByTypeName(obj.UserDataTypeName, EnsureSpecial.Yes);

                    var userData = (JObject)obj.UserData;

                    HadbData data = (HadbData)userData.ToObject(type, serializer)!;

                    obj.UserData = data;

                    return obj;
                }
            }

            throw new CoresLibException();
        }
    }


    // JSON 形式のローカルバックアップデータから HadbObject のリストを読み込む
    async Task<List<HadbObject>> LoadLocalBackupDataAsync(FilePath backupDatabasePath, CancellationToken cancel = default)
    {
        // 型ごとに適切な変換を行なう
        JsonConverter conv = new HadbSerializedObjectJsonReadConverter(this);

        HadbBackupDatabase loadData = await backupDatabasePath.FileSystem.ReadJsonFromFileAsync<HadbBackupDatabase>(backupDatabasePath.PathString,
            cancel: cancel,
            withBackup: true,
            converters: conv._SingleList());

        List<HadbObject> ret = new List<HadbObject>();

        foreach (var obj in loadData.ObjectsList)
        {
            HadbObject a = new HadbObject((HadbData)obj.UserData, this.Settings.OptionFlags, obj.Ext1, obj.Ext2, obj.Ft1, obj.Ft2, obj.Uid, obj.Ver, false, obj.SnapshotNo, obj.NameSpace, false, obj.CreateDt, obj.UpdateDt, obj.DeleteDt);
            ret.Add(a);
        }

        return ret;
    }

    // JSON 形式のローカルバックアップデータからデータベースに書き戻しをする
    public async Task RestoreDataFromHadbObjectListAsync(FilePath backupDatabasePath, CancellationToken cancel = default)
    {
        var list = await this.LoadLocalBackupDataAsync(backupDatabasePath, cancel);

        await this.RestoreDataFromHadbObjectListImplAsync(list, cancel);
    }

    async Task ReloadMainLoopAsync(CancellationToken cancel)
    {
        int numCycle = 0;
        int numError = 0;

        await Task.Yield();
        Debug($"ReloadMainLoopAsync: Waiting for start.");
        await TaskUtil.AwaitWithPollAsync(Timeout.Infinite, 100, () => this.IsLoopStarted, cancel, true);
        Debug($"ReloadMainLoopAsync: Started.");

        bool lastOkState = false;

        while (cancel.IsCancellationRequested == false)
        {
            numCycle++;
            Debug($"ReloadMainLoopAsync: numCycle={numCycle}, numError={numError} Start.");

            long startTick = Time.HighResTick64;
            bool ok = false;

            long now = Time.Tick64;

            bool fullReloadMode = false;

            if (this.LastDatabaseFullReloadTick == 0 || (now >= (this.LastDatabaseFullReloadTick + this._CurrentDynamicConfig.HadbFullReloadIntervalMsecs)) || this.MemDb == null)
            {
                fullReloadMode = true;
            }

            try
            {
                // リロード
                await ReloadCoreAsync(EnsureSpecial.Yes, fullReloadMode, cancel: cancel);
                ok = true;

                await this.EventListenerList.FireAsync(this, fullReloadMode ? HadbEventType.ReloadDataFull : HadbEventType.ReloadDataPartially, debugLog: true);
            }
            catch (Exception ex)
            {
                ex._Error();
            }

            if (lastOkState != ok)
            {
                lastOkState = ok;
                try
                {
                    // DB 接続状態が変化した (エラー → 正常 または 正常 → エラー)
                    await this.EventListenerList.FireAsync(this, ok ? HadbEventType.DatabaseStateChangedToRecovery : HadbEventType.DatabaseStateChangedToFailure, debugLog: true);
                }
                catch { }
            }

            if (this.DebugFlags.Bit(HadbDebugFlags.NoDbLoadAndUseOnlyLocalBackup)) return;

            long endTick = Time.HighResTick64;

            Debug($"ReloadMainLoopAsync: numCycle={numCycle}, numError={numError} End. Took time: {(endTick - startTick)._ToString3()} msecs.");

            if (this.IsDatabaseConnectedForReload)
            {
                if (this.Settings.OptionFlags.Bit(HadbOptionFlags.NoAutoDbReloadAndUpdate)) return;
            }


            int nextWaitTime = Util.GenRandInterval(ok ? this._CurrentDynamicConfig.HadbReloadIntervalMsecsLastOk : this._CurrentDynamicConfig.HadbReloadIntervalMsecsLastError);
            Debug($"ReloadMainLoopAsync: Waiting for {nextWaitTime._ToString3()} msecs for next DB read.");
            await cancel._WaitUntilCanceledAsync(nextWaitTime);
        }

        Debug($"ReloadMainLoopAsync: Finished.");
    }

    async Task LazyUpdateMainLoopAsync(CancellationToken cancel)
    {
        if (this.Settings.OptionFlags.Bit(HadbOptionFlags.NoAutoDbReloadAndUpdate)) return;

        int numCycle = 0;
        int numError = 0;
        await Task.Yield();
        Debug($"LazyUpdateMainLoopAsync: Waiting for start.");
        await TaskUtil.AwaitWithPollAsync(Timeout.Infinite, 1000, () => (this.CheckIfReady(requireDatabaseConnected: true, EnsureSpecial.Yes).IsOk), cancel, true);
        Debug($"LazyUpdateMainLoopAsync: Started.");

        while (cancel.IsCancellationRequested == false)
        {
            if (this.MemDb!.GetLazyUpdateQueueLength() >= 1)
            {
                // キューに 1 つ以上入っている場合のみ実行する
                numCycle++;
                Debug($"LazyUpdateMainLoopAsync: numCycle={numCycle}, numError={numError} Start.");

                long startTick = Time.HighResTick64;
                bool ok = false;

                try
                {
                    await LazyUpdateCoreAsync(EnsureSpecial.Yes, cancel);
                    ok = true;
                }
                catch (Exception ex)
                {
                    if (this.DebugFlags.Bit(HadbDebugFlags.NoDbLoadAndUseOnlyLocalBackup) == false)
                    {
                        ex._Error();
                    }
                }

                long endTick = Time.HighResTick64;
                if (ok)
                {
                    LastLazyUpdateTookMsecs = (int)(endTick - startTick);
                }
                else
                {
                    LastLazyUpdateTookMsecs = 0;
                }

                Debug($"LazyUpdateMainLoopAsync: numCycle={numCycle}, numError={numError} End. Took time: {(endTick - startTick)._ToString3()} msecs.");
            }

            int nextWaitTime = Util.GenRandInterval(this._CurrentDynamicConfig.HadbLazyUpdateIntervalMsecs);

            await cancel._WaitUntilCanceledAsync(nextWaitTime);
        }

        Debug($"LazyUpdateMainLoopAsync: Finished.");
    }

    public void Debug(string str)
    {
        if (this.DebugFlags.Bit(HadbDebugFlags.NoDbLoadAndUseOnlyLocalBackup) == false)
        {
            $"{this.GetType().Name}: {str}"._Debug();
        }
    }

    public void CheckIfReady(bool requireDatabaseConnected = true)
    {
        var ret = CheckIfReady(requireDatabaseConnected, doNotThrowError: EnsureSpecial.Yes);
        ret.ThrowIfException();
    }

    public void CheckMemDb()
    {
        if (this.IsEnabled_NoMemDb) throw new CoresLibException("NoMemDB option is enabled.");
    }

    public OkOrExeption CheckIfReady(bool requireDatabaseConnected, EnsureSpecial doNotThrowError)
    {
        if (requireDatabaseConnected)
        {
            if (this.IsDatabaseConnectedForReload == false)
            {
                return new CoresLibException("IsDatabaseConnectedForReload == false");
            }
        }

        if (this.MemDb == null)
        {
            return new CoresLibException("MemDb is not loaded yet.");
        }

        return new OkOrExeption();
    }

    public async Task WaitUntilReadyForAtomicAsync(int maxDbTryCountError = 1, CancellationToken cancel = default)
    {
        this.Start();
        maxDbTryCountError = Math.Max(maxDbTryCountError, 1);
        await Task.Yield();
        int startErrorCount = this.DatabaseConnectForReloadErrorCount;
        await TaskUtil.AwaitWithPollAsync(Timeout.Infinite, 100, () =>
        {
            bool ret = CheckIfReady(requireDatabaseConnected: true, doNotThrowError: EnsureSpecial.Yes).IsOk;
            if (ret == false)
            {
                if (this.DatabaseConnectForReloadErrorCount >= (startErrorCount + maxDbTryCountError))
                {
                    throw this.LastDatabaseConnectForReloadError;
                }
            }
            return ret;
        }, cancel, true);
    }

    public async Task WaitUntilReadyForFastAsync(CancellationToken cancel = default)
    {
        this.Start();
        await Task.Yield();
        await TaskUtil.AwaitWithPollAsync(Timeout.Infinite, 100, () =>
        {
            bool ret = CheckIfReady(requireDatabaseConnected: false, doNotThrowError: EnsureSpecial.Yes).IsOk;
            if (ret == false)
            {
                if (this.LastMemDbBackupLocalLoadError != null)
                {
                    throw this.LastMemDbBackupLocalLoadError;
                }
            }
            return ret;
        }, cancel, true);
    }


    public int DbConcurrentTranWriteTotal => _DbConcurrentTranWriteTotal;
    public int DbConcurrentTranReadTotal => _DbConcurrentTranReadTotal;
    public int DbTotalDeadlockCountTotal => _DbTotalDeadlockCountTotal;

    int _DbConcurrentTranWriteTotal = 0;
    int _DbConcurrentTranReadTotal = 0;
    int _DbTotalDeadlockCountTotal = 0;

    readonly ConcurrentLimiter<string> DbConcurrentTranLimiterWritePerClient = new ConcurrentLimiter<string>();
    readonly ConcurrentLimiter<string> DbConcurrentTranLimiterReadPerClient = new ConcurrentLimiter<string>();

    public async Task<bool> TranAsync(bool writeMode, Func<HadbTran, Task<bool>> task, HadbTranOptions options = HadbTranOptions.Default, bool takeSnapshot = false, RefLong? snapshotNoRet = null, CancellationToken cancel = default, DeadlockRetryConfig? retryConfig = null, bool ignoreQuota = false, string clientName = "", object? reentrantTran = null)
    {
        if (reentrantTran != null)
        {
            HadbTran reentrantTran2 = (HadbTran)reentrantTran;
            // すでにトランザクションが開始されているので、その中で実行する
            bool ret = await task(reentrantTran2);

            if (writeMode)
            {
                if (reentrantTran2.IsWriteMode == false)
                {
                    throw new CoresLibException("Reentrant Transaction is not in write mode.");
                }

                if (ret == false)
                {
                    throw new CoresLibException("Reentrant Transaction Write Callback returned false.");
                }

                return true;
            }
            else
            {
                return false;
            }
        }

        if (options.Bit(HadbTranOptions.UseStrictLock) && options.Bit(HadbTranOptions.NoTransactionOnWrite))
        {
            throw new ArgumentException("options.Bit(HadbTranOptions.UseStrictLock) && options.Bit(HadbTranOptions.NoTransactionOnWrite)");
        }

        CheckIfReady();

        retryConfig ??= this.DefaultDeadlockRetryConfig;
        int numRetry = 0;

        // 同時並列実行トランザクション数の制限
        using IDisposable concurrentLimitEntry = (ignoreQuota || clientName._IsEmpty()) ? new EmptyDisposable() :
            (writeMode ? this.DbConcurrentTranLimiterWritePerClient.EnterWithUsing(clientName, out _, this._CurrentDynamicConfig.HadbMaxDbConcurrentWriteTransactionsPerClient) :
                         this.DbConcurrentTranLimiterReadPerClient.EnterWithUsing(clientName, out _, this._CurrentDynamicConfig.HadbMaxDbConcurrentReadTransactionsPerClient));

        if (writeMode)
        {
            int current = Interlocked.Increment(ref _DbConcurrentTranWriteTotal);
            if (ignoreQuota == false && current > this._CurrentDynamicConfig.HadbMaxDbConcurrentWriteTransactionsTotal)
            {
                Interlocked.Decrement(ref _DbConcurrentTranWriteTotal);
                throw new CoresException("Too many concurrent write transactions running. Please wait for moment and try again later.");
            }
        }
        else
        {
            int current = Interlocked.Increment(ref _DbConcurrentTranReadTotal);
            if (ignoreQuota == false && current > this._CurrentDynamicConfig.HadbMaxDbConcurrentReadTransactionsTotal)
            {
                Interlocked.Decrement(ref _DbConcurrentTranReadTotal);
                throw new CoresException("Too many concurrent read transactions running. Please wait for moment and try again later.");
            }
        }

        try
        {
            LABEL_RETRY:
            try
            {
                bool isTransaction;

                if (writeMode == false)
                {
                    isTransaction = true;
                }
                else
                {
                    isTransaction = !options.Bit(HadbTranOptions.NoTransactionOnWrite);
                }

                await using var tran = await this.BeginDatabaseTransactionImplAsync(writeMode, isTransaction, options, cancel);

                await tran.BeginAsync(takeSnapshot, cancel);

                if (await task(tran))
                {
                    await tran.CommitAsync(cancel);

                    snapshotNoRet?.Set(tran.CurrentSnapNoForWriteMode);
                    return true;
                }
                else
                {
                    return false;
                }
            }
            catch (Exception ex)
            {
                if (this.IsDeadlockExceptionImpl(ex))
                {
                    // デッドロック発生
                    Interlocked.Increment(ref _DbTotalDeadlockCountTotal);

                    numRetry++;
                    if (numRetry <= retryConfig.RetryCount)
                    {
                        int nextInterval = Util.GenRandIntervalWithRetry(retryConfig.RetryAverageInterval, numRetry, retryConfig.RetryAverageInterval * retryConfig.RetryIntervalMaxFactor, 60.0);

                        $"Deadlock retry occured. numRetry = {numRetry}. Waiting for {nextInterval} msecs. {ex.ToString()}"._Debug();

                        await Task.Delay(nextInterval);

                        goto LABEL_RETRY;
                    }

                    throw;
                }
                else
                {
                    throw;
                }
            }
        }
        finally
        {
            if (writeMode)
            {
                Interlocked.Decrement(ref _DbConcurrentTranWriteTotal);
            }
            else
            {
                Interlocked.Decrement(ref _DbConcurrentTranReadTotal);
            }
        }
    }


    public HadbObject<T>? FastGet<T>(string uid, string nameSpace = Consts.Strings.HadbDefaultNameSpace) where T : HadbData
        => FastGet(uid, typeof(T).Name, nameSpace);

    public HadbObject? FastGet(string uid, string typeName, string nameSpace = Consts.Strings.HadbDefaultNameSpace)
    {
        this.CheckIfReady(requireDatabaseConnected: false);
        this.CheckMemDb();
        nameSpace = nameSpace._HadbNameSpaceNormalize();

        var mem = this.MemDb!;

        var ret = mem.IndexedKeysTable_SearchByUid(uid, typeName, nameSpace);

        if (ret == null) return null;

        if (ret.Deleted) return null;

        return ret;
    }

    public HadbObject<T>? FastSearchByKey<T>(T model, string nameSpace = Consts.Strings.HadbDefaultNameSpace) where T : HadbData
    {
        nameSpace = nameSpace._HadbNameSpaceNormalize();
        model.Normalize();
        return FastSearchByKey<T>(model.GetKeys(), nameSpace);
    }

    public HadbObject<T>? FastSearchByKey<T>(HadbKeys keys, string nameSpace = Consts.Strings.HadbDefaultNameSpace) where T : HadbData
        => FastSearchByKey(keys, typeof(T).Name, nameSpace);

    public HadbObject? FastSearchByKey(HadbKeys keys, string typeName, string nameSpace = Consts.Strings.HadbDefaultNameSpace)
    {
        nameSpace = nameSpace._HadbNameSpaceNormalize();
        this.CheckIfReady(requireDatabaseConnected: false);
        this.CheckMemDb();

        var mem = this.MemDb!;

        var ret = mem.IndexedKeysTable_SearchByKey(keys, typeName, nameSpace);

        if (ret == null) return null;

        if (ret.Deleted) return null;

        return ret;
    }

    public List<HadbObject> FastSearchByFullText(string queryString, string sortFields, bool wordMode, bool fieldMode, string typeName, string nameSpace = Consts.Strings.HadbDefaultNameSpace, int maxResults = int.MaxValue, int maxResultsBeforeSortInternal = int.MaxValue, FullTextSearchFlags optionalFlags = FullTextSearchFlags.None, RefBool? hasMore = null)
    {
        FullTextSearchFlags flags2 = FullTextSearchFlags.None;

        if (wordMode)
        {
            flags2 |= FullTextSearchFlags.WordMode;
        }

        if (fieldMode)
        {
            flags2 |= FullTextSearchFlags.FieldNameMode;
        }

        flags2 |= optionalFlags;

        FullTextSearchQuery q = FullTextSearchQuery.ParseText(queryString, flags2);

        q.MaxResults = maxResults;
        q.MaxResultsBeforeSortInternal = maxResultsBeforeSortInternal;
        q.SortFields = sortFields;

        return this.FastSearchByFullText(q, typeName, nameSpace, hasMore);
    }

    public List<HadbObject> FastSearchByFullText(FullTextSearchQuery query, string typeName, string nameSpace = Consts.Strings.HadbDefaultNameSpace, RefBool? hasMore = null)
    {
        hasMore?.Set(false);

        nameSpace = nameSpace._HadbNameSpaceNormalize();
        this.CheckIfReady(requireDatabaseConnected: false);
        this.CheckMemDb();

        var mem = this.MemDb!;

        var allObjects = mem.GetAllObjects();

        var objList = allObjects.Values.ToList();

        int numCpus = Env.NumCpus;

        RefInt currentCounter = new RefInt();

        var matchList = objList._ProcessParallelAndAggregateAsync((srcList, taskIndex) =>
        {
            List<HadbObject> tmp = new List<HadbObject>();

            foreach (var item in srcList)
            {
                bool b2 = true;

                if (typeName._IsFilled())
                {
                    if (item.UserDataTypeName._IsSamei(typeName) == false)
                    {
                        b2 = false;
                    }
                }

                if (nameSpace._IsFilled())
                {
                    if (item.NameSpace != nameSpace)
                    {
                        b2 = false;
                    }
                }

                if (b2)
                {
                    bool b;

                    string ft;

                    if (this.Settings.OptionFlags.Bit(HadbOptionFlags.BuildFullTextSearchText))
                    {
                        if (query.Flags.Bit(FullTextSearchFlags.FieldNameMode) == false)
                        {
                            ft = item.Ft1;
                        }
                        else
                        {
                            ft = item.Ft2;
                        }
                    }
                    else
                    {
                        if (query.Flags.Bit(FullTextSearchFlags.FieldNameMode) == false)
                        {
                            ft = item.UserData.GenerateFt1(true);
                        }
                        else
                        {
                            ft = item.UserData.GenerateFt2(true);
                        }
                    }

                    b = query.IsMatch(ft, item.Uid);

                    if (b)
                    {
                        tmp.Add(item);

                        if (query.MaxResultsBeforeSortInternal > 0 && query.MaxResultsBeforeSortInternal != int.MaxValue)
                        {
                            if (currentCounter.Increment() >= query.MaxResultsBeforeSortInternal)
                            {
                                break;
                            }
                        }
                    }
                }
            }

            return TR(tmp);
        }, numCpus, MultitaskDivideOperation.Split, onlyIfMany: false)._GetResult();

        int count1 = matchList.Count;

        // ソート前の件数足切り
        if (query.MaxResultsBeforeSortInternal > 0 && query.MaxResultsBeforeSortInternal != int.MaxValue)
        {
            if (matchList.Count > query.MaxResultsBeforeSortInternal)
            {
                matchList = matchList.Take(query.MaxResultsBeforeSortInternal).ToList();
            }
        }

        // ソートの実施
        matchList._DoSortBy(x => x.OrderByDescending(z => z.UpdateDt));
        matchList._DoSortBy(query.SortFields);

        // ソートの実施結果を足切り
        if (query.MaxResults > 0 && query.MaxResults != int.MaxValue)
        {
            if (matchList.Count > query.MaxResults)
            {
                matchList = matchList.Take(query.MaxResults).ToList();
            }
        }

        if (matchList.Count != count1)
        {
            hasMore?.Set(true);
        }

        return matchList;
    }

    public IEnumerable<HadbObject<T>> FastSearchByLabels<T>(T model, string nameSpace = Consts.Strings.HadbDefaultNameSpace) where T : HadbData
    {
        model.Normalize();
        return FastSearchByLabels(model.GetLabels(), typeof(T).Name, nameSpace).Select(x => x.GetGenerics<T>());
    }

    public IEnumerable<HadbObject<T>> FastSearchByLabels<T>(HadbLabels labels, string nameSpace = Consts.Strings.HadbDefaultNameSpace) where T : HadbData
        => FastSearchByLabels(labels, typeof(T).Name, nameSpace).Select(x => x.GetGenerics<T>());

    public IEnumerable<HadbObject> FastSearchByLabels(HadbLabels labels, string typeName, string nameSpace = Consts.Strings.HadbDefaultNameSpace)
    {
        nameSpace = nameSpace._HadbNameSpaceNormalize();
        this.CheckIfReady(requireDatabaseConnected: false);
        this.CheckMemDb();

        var mem = this.MemDb!;

        var items = mem.IndexedLabelsTable_SearchByLabels(labels, typeName, nameSpace);

        return items.Where(x => x.Deleted == false);
    }

    public ConcurrentStrDictionary<HadbObject> FastGetAllObjects()
    {
        this.CheckIfReady(requireDatabaseConnected: false);
        this.CheckMemDb();

        var mem = this.MemDb!;

        return mem.GetAllObjects();
    }

    public IEnumerable<HadbObject<T>> FastEnumObjects<T>(string nameSpace = Consts.Strings.HadbDefaultNameSpace) where T : HadbData
        => FastEnumObjects(typeof(T).Name, nameSpace).Select(x => x.GetGenerics<T>());

    public IEnumerable<HadbObject> FastEnumObjects(string typeName, string nameSpace = Consts.Strings.HadbDefaultNameSpace)
    {
        nameSpace = nameSpace._HadbNameSpaceNormalize();
        this.CheckIfReady(requireDatabaseConnected: false);
        this.CheckMemDb();

        ConcurrentStrDictionary<HadbObject> dict = FastGetAllObjects();

        List<HadbObject> ret = new List<HadbObject>();

        foreach (var obj in dict.Values)
        {
            if (obj.UserDataTypeName == typeName && obj.NameSpace == nameSpace && obj.Deleted == false)
            {
                ret.Add(obj);
            }
        }

        return ret;
    }


    protected override async Task CleanupImplAsync(Exception? ex)
    {
        try
        {
            await ReloadMainLoopTask._TryAwait();
            await LazyUpdateMainLoopTask._TryAwait();
        }
        finally
        {
            await base.CleanupImplAsync(ex);
        }
    }

    public async Task<string> AdminForm_DirectGetObjectExAsync(string uid, int maxItems = int.MaxValue, HadbObjectGetExFlag flag = HadbObjectGetExFlag.None, CancellationToken cancel = default)
    {
        string ret = "";
        uid = uid._NormalizeUid();

        await this.TranAsync(false, async tran =>
        {
            if (flag.Bit(HadbObjectGetExFlag.WithArchive))
            {
                var objectList = await tran.AtomicGetArchivedAsync(uid, "", maxItems, "", cancel, noCheckTypeIdAndNameSpace: true);

                if (objectList.Any() == false)
                {
                    throw new CoresException($"UID {uid} not found on the database.");
                }

                List<HadbSearchResultJsonObject> list = new List<HadbSearchResultJsonObject>();
                objectList._DoForEach(x => list.Add(new HadbSearchResultJsonObject(x)));

                ret = list._ObjectToJson(includeNull: true);
            }
            else
            {
                var obj = await tran.AtomicGetAsync(uid, "", "", cancel, noCheckTypeIdAndNameSpace: true);

                if (obj == null)
                {
                    throw new CoresException($"UID {uid} not found on the database.");
                }

                var tmp = new HadbSearchResultJsonObject(obj);

                ret = tmp._ObjectToJson(includeNull: true);
            }

            return false;
        },
        cancel: cancel);

        return ret;
    }

    public async Task<HadbObject?> DirectGetObjectAsync(string uid, CancellationToken cancel = default)
    {
        uid = uid._NormalizeUid();
        HadbObject? ret = null;

        await this.TranAsync(false, async tran =>
        {
            ret = await tran.AtomicGetAsync(uid, "", "", cancel, noCheckTypeIdAndNameSpace: true);

            return false;
        },
        cancel: cancel);

        return ret;
    }

    public async Task<HadbObject> DirectSetObjectAsync(string uid, string jsonData, HadbObjectSetFlag flag, string typeName, string nameSpace, CancellationToken cancel = default)
    {
        HadbObject ret = null!;

        uid = uid._NormalizeUid();
        typeName = typeName._NonNull();
        nameSpace = nameSpace._HadbNameSpaceNormalize();

        await this.TranAsync(true, async tran =>
        {
            if (flag.Bit(HadbObjectSetFlag.New) == false)
            {
                // 新規作成以外の場合、現在のオブジェクトを取得する
                var current = await tran.AtomicGetAsync(uid, "", "", cancel, noCheckTypeIdAndNameSpace: true);

                if (current == null)
                {
                    throw new CoresLibException($"Object UID '{uid}' not found.");
                }

                if (typeName != current.UserDataTypeName)
                {
                    throw new CoresLibException($"TypeName is different. Specified: '{typeName}', realObject: '{current.UserDataTypeName}'");
                }

                if (nameSpace._IsSamei(current.NameSpace) == false)
                {
                    throw new CoresLibException($"NameSpace is different. Specified: '{nameSpace}', realObject: '{current.NameSpace}'");
                }

                typeName = current.UserDataTypeName;
                nameSpace = current.NameSpace;

                if (flag.Bit(HadbObjectSetFlag.Delete))
                {
                    // 削除する
                    await tran.AtomicDeleteAsync(current.Uid, current.UserDataTypeName, current.NameSpace, cancel: cancel);

                    ret = current;

                    return true;
                }
                else if (flag.Bit(HadbObjectSetFlag.Update))
                {
                    // 更新する
                    var newData = this.JsonToHadbData(jsonData, typeName);

                    current.ReplaceData(newData);

                    await tran.AtomicUpdateAsync(current, cancel);

                    // 更新結果を取得する
                    ret = (await tran.AtomicGetAsync(current.Uid, current.UserDataTypeName, current.NameSpace, cancel))!;

                    return true;
                }
                else
                {
                    // フラグ不正
                    throw new CoresLibException($"Invalid flag: {flag}");
                }
            }
            else
            {
                // 新規作成する
                var userType = this.GetDataTypeByTypeName(typeName, EnsureSpecial.Yes);
                typeName = userType.ToString();

                var newData = this.JsonToHadbData(jsonData, typeName);

                var newObj = (await tran.AtomicAddAsync(newData._SingleList(), nameSpace, "", "", cancel)).Single();

                ret = newObj;

                return true;
            }
        },
        cancel: cancel);

        return ret;
    }

    public abstract class HadbTran : AsyncService
    {
        public bool IsWriteMode { get; }
        public bool IsTransaction { get; }
        public bool TakeSnapshot { get; private set; }
        public bool IsDbSnapshotReadMode { get; }
        public HadbTranOptions Options { get; }
        public long CurrentSnapNoForWriteMode { get; private set; }
        public HadbMemDataBase MemDb { get; }
        List<HadbObject> ApplyObjectsList = new List<HadbObject>();

        public bool ShouldUseLightLock => !this.IsDbSnapshotReadMode && !this.Options.Bit(HadbTranOptions.UseStrictLock);

        public IReadOnlyList<HadbObject> GetApplyObjectsList() => this.ApplyObjectsList;

        protected abstract Task CommitImplAsync(CancellationToken cancel);

        public HadbBase<TMem, TDynamicConfig> Hadb;

        public HadbTran(bool writeMode, bool isTransaction, bool isDbSnapshotReadMode, HadbTranOptions options, HadbBase<TMem, TDynamicConfig> hadb)
        {
            try
            {
                this.IsDbSnapshotReadMode = isDbSnapshotReadMode;
                this.IsWriteMode = writeMode;
                this.IsTransaction = isTransaction;
                this.Options = options;
                this.Hadb = hadb;
                this.MemDb = this.Hadb.MemDb!;
            }
            catch (Exception ex)
            {
                this._DisposeSafe(ex);
                throw;
            }
        }

        Once BeganFlag = new Once();

        void CheckBegan()
        {
            if (BeganFlag.IsSet == false)
            {
                throw new CoresLibException("Transaction is not began.");
            }
            if (CommitedFlag.IsSet || finished.IsSet)
            {
                throw new CoresLibException("Transaction is already finished.");
            }
        }

        public async Task BeginAsync(bool takeSnapshot, CancellationToken cancel = default)
        {
            long autoSnapshotInterval = this.Hadb._CurrentDynamicConfig.HadbAutomaticSnapshotIntervalMsecs;

            if (BeganFlag.IsFirstCall())
            {
                if (this.IsWriteMode)
                {
                    var now = DtOffsetNow;

                    this.TakeSnapshot = takeSnapshot;

                    // Snapshot 処理をする。
                    // まず現在の Snapshot 番号を取得する。
                    var kvList = await this.AtomicGetKvListAsync(cancel);
                    this.CurrentSnapNoForWriteMode = kvList.SingleOrDefault(x => x.Key._IsSamei("_hadb_sys_current_snapshot_no")).Value._ToLong();

                    // 最後に Snapshot が撮られた日時を取得する
                    var lastTaken = Str.DtstrToDateTimeOffset(kvList.SingleOrDefault(x => x.Key._IsSamei("_hadb_sys_current_snapshot_timestamp")).Value);

                    string snapshotDescrption = "Normal Snapshot";

                    if (this.CurrentSnapNoForWriteMode == 0)
                    {
                        // 初めての場合は、必ず Snapshot を撮る。
                        this.TakeSnapshot = true;
                        snapshotDescrption = "Initial Snapshot";

                        if (this.Hadb.Settings.OptionFlags.Bit(HadbOptionFlags.NoInitSnapshot))
                        {
                            // NoInitSnapshot が設定されている場合は、擬似的にスナップショットを撮るが、実際には撮らない。(主にデバッグ用である。)
                            this.CurrentSnapNoForWriteMode = 1;
                            return;
                        }
                    }

                    if (this.Hadb.Settings.OptionFlags.Bit(HadbOptionFlags.DoNotTakeSnapshotAtAll))
                    {
                        // スナップショットを全く撮らない。(主にデバッグ用である。)
                        if (this.CurrentSnapNoForWriteMode == 0)
                        {
                            this.CurrentSnapNoForWriteMode = 1;
                        }
                        return;
                    }

                    if (this.TakeSnapshot == false && autoSnapshotInterval != 0 && lastTaken.AddMilliseconds(autoSnapshotInterval) < now)
                    {
                        // 自動スナップショットを撮る。
                        this.TakeSnapshot = true;
                        snapshotDescrption = $"Automatic Snapshot (Interval = {Str.TimeSpanToTsStr(autoSnapshotInterval._ToTimeSpanMSecs(), true)})";
                    }

                    if (this.TakeSnapshot)
                    {
                        // Snapshot を撮る場合、Snapshot 番号をインクリメントする。
                        this.CurrentSnapNoForWriteMode++;

                        // インクリメントされた Snapshot 番号を書き込む。
                        await this.AtomicSetKvAsync("_hadb_sys_current_snapshot_no", this.CurrentSnapNoForWriteMode.ToString(), cancel);

                        var snap = new HadbSnapshot(Str.NewUid("SNAPSHOT", '_'), Hadb.SystemName, this.CurrentSnapNoForWriteMode, now, snapshotDescrption, "", "");

                        // 最後の Snapshot 情報を書き込む。
                        await this.AtomicSetKvAsync("_hadb_sys_current_snapshot_uid", snap.Uid, cancel);
                        await this.AtomicSetKvAsync("_hadb_sys_current_snapshot_timestamp", snap.TimeStamp._ToDtStr(withMSsecs: true, withNanoSecs: true), cancel);

                        // Snapshot テーブルを追記する。
                        await this.AtomicAddSnapshotInternalAsync(EnsureInternal.Yes, snap, cancel);
                    }
                }
            }
            else
            {
                throw new CoresLibException("Transaction already began.");
            }
        }

        public void CheckIsWriteMode()
        {
            if (this.IsWriteMode == false)
            {
                throw new CoresLibException("Database transaction is read only.");
            }
        }

        public void AddApplyObject(HadbObject obj)
            => AddApplyObjects(obj._SingleArray());

        public void AddApplyObjects(IEnumerable<HadbObject> objs)
        {
            List<HadbObject> tmp = new List<HadbObject>();

            foreach (var obj in objs)
            {
                obj.CheckIsNotMemoryDbObject();
                tmp.Add(obj.CloneObject());
            }

            this.ApplyObjectsList.AddRange(tmp);
        }

        Once CommitedFlag = new Once();

        public async Task CommitAsync(CancellationToken cancel = default)
        {
            if (finished == false)
            {
                if (CommitedFlag.IsFirstCall())
                {
                    if (this.IsWriteMode)
                    {
                        await this.CommitImplAsync(cancel);

                        await FinishInternalAsync(cancel);
                    }
                }
                else
                {
                    throw new CoresLibException("Transaction already committed.");
                }
            }
            else
            {
                throw new CoresLibException("Transaction already finished.");
            }
        }

        Once finished = new Once();

        Task FinishInternalAsync(CancellationToken cancel = default)
        {
            if (finished.IsFirstCall())
            {
                //using (await this.MemDb.CriticalLockAsync.LockWithAwait(cancel))
                {
                    foreach (var obj in this.ApplyObjectsList)
                    {
                        try
                        {
                            this.MemDb.ApplyObjectToMemDb(obj);
                        }
                        catch (Exception ex)
                        {
                            ex._Debug();
                        }
                    }
                }
            }

            return TR();
        }

        protected override async Task CleanupImplAsync(Exception? ex)
        {
            try
            {
                if (this.IsWriteMode == false)
                {
                    await this.FinishInternalAsync();
                }
            }
            finally
            {
                await base.CleanupImplAsync(ex);
            }
        }

        public async Task<HadbObject<T>> AtomicAddAsync<T>(T data, string nameSpace = Consts.Strings.HadbDefaultNameSpace, string ext1 = "", string ext2 = "", CancellationToken cancel = default)
            where T : HadbData
            => (await AtomicAddAsync(data._SingleArray(), nameSpace, ext1, ext2, cancel)).Single();

        public async Task<List<HadbObject>> AtomicAddAsync(IEnumerable<HadbData> dataList, string nameSpace = Consts.Strings.HadbDefaultNameSpace, string ext1 = "", string ext2 = "", CancellationToken cancel = default)
        {
            nameSpace = nameSpace._HadbNameSpaceNormalize();

            CheckBegan();
            Hadb.CheckIfReady();

            List<HadbObject> objList = new List<HadbObject>();

            foreach (var _data in dataList)
            {
                var data = _data;

                data._NullCheck(nameof(data));

                data = data._CloneDeep();
                data.Normalize();

                var keys = data.GetKeys();

                if (Hadb.DebugFlags.Bit(HadbDebugFlags.NoCheckMemKeyDuplicate) == false)
                {
                    var existingList = this.MemDb!.IndexedKeysTable_SearchByKeys(keys, data.GetUserDataTypeName(), nameSpace);

                    if (existingList.Any())
                    {
                        throw new CoresLibException($"Duplicated key in the memory database. Namespace = {nameSpace}, Keys = {keys._ObjectToJson(compact: true)}");
                    }
                }

                string ft1 = "";
                string ft2 = "";

                if (data.GetDataFlags().Bit(HadbDataFlags.NoFullTextSearch) == false && this.Hadb.Settings.OptionFlags.Bit(HadbOptionFlags.BuildFullTextSearchText))
                {
                    ft1 = data.GenerateFt1();
                    ft2 = data.GenerateFt2();
                }

                objList.Add(data.ToNewObject(this.CurrentSnapNoForWriteMode, nameSpace, this.Hadb.Settings.OptionFlags.Bit(HadbOptionFlags.DataUidForPartitioningByUidOptimized), this.Hadb.Settings.OptionFlags, ext1, ext2, ft1, ft2));
            }

            await Hadb.AtomicAddDataListToDatabaseImplAsync(this, objList, cancel);

            this.AddApplyObjects(objList);

            return objList;
        }

        public async Task<HadbObject<T>?> AtomicGetAsync<T>(string uid, string nameSpace = Consts.Strings.HadbDefaultNameSpace, CancellationToken cancel = default, bool noCheckTypeIdAndNameSpace = false) where T : HadbData
            => await AtomicGetAsync(uid, typeof(T).Name, nameSpace, cancel, noCheckTypeIdAndNameSpace);

        public async Task<HadbObject?> AtomicGetAsync(string uid, string typeName, string nameSpace = Consts.Strings.HadbDefaultNameSpace, CancellationToken cancel = default, bool noCheckTypeIdAndNameSpace = false)
        {
            nameSpace = nameSpace._HadbNameSpaceNormalize();

            CheckBegan();
            Hadb.CheckIfReady();

            HadbObject? ret = await Hadb.AtomicGetDataFromDatabaseImplAsync(this, uid, typeName, nameSpace, cancel, noCheckTypeIdAndNameSpace);

            if (ret == null) return null;

            this.AddApplyObject(ret);

            if (ret.Deleted) return null;

            return ret;
        }

        public async Task<IEnumerable<HadbObject<T>>> AtomicGetArchivedAsync<T>(string uid, int maxItems = int.MaxValue, string nameSpace = Consts.Strings.HadbDefaultNameSpace, CancellationToken cancel = default, bool noCheckTypeIdAndNameSpace = false) where T : HadbData
            => (await AtomicGetArchivedAsync(uid, typeof(T).Name, maxItems, nameSpace, cancel, noCheckTypeIdAndNameSpace)).Select(x => x.GetGenerics<T>());

        public async Task<IEnumerable<HadbObject>> AtomicGetArchivedAsync(string uid, string typeName, int maxItems = int.MaxValue, string nameSpace = Consts.Strings.HadbDefaultNameSpace, CancellationToken cancel = default, bool noCheckTypeIdAndNameSpace = false)
        {
            nameSpace = nameSpace._HadbNameSpaceNormalize();

            CheckBegan();
            Hadb.CheckIfReady();

            return await Hadb.AtomicGetArchivedDataFromDatabaseImplAsync(this, maxItems, uid, typeName, nameSpace, cancel, noCheckTypeIdAndNameSpace);
        }

        public async Task<HadbObject> AtomicUpdateAsync(HadbObject obj, CancellationToken cancel = default)
        {
            CheckBegan();
            Hadb.CheckIfReady();

            obj._NullCheck(nameof(obj));

            obj.CheckIsNotMemoryDbObject();
            obj.Normalize();

            obj.UpdateFullText();

            var keys = obj.GetKeys();

            if (Hadb.DebugFlags.Bit(HadbDebugFlags.NoCheckMemKeyDuplicate) == false)
            {
                var existingList = this.MemDb!.IndexedKeysTable_SearchByKeys(keys, obj.GetUserDataTypeName(), obj.NameSpace);

                if (existingList.Where(x => x.Uid._IsSamei(obj.Uid) == false).Any())
                {
                    throw new CoresLibException($"Duplicated key in the memory database. Namespace = {obj.NameSpace}, Keys = {keys._ObjectToJson(compact: true)}");
                }
            }

            var obj2 = await Hadb.AtomicUpdateDataOnDatabaseImplAsync(this, obj, cancel);

            this.AddApplyObject(obj2);

            return obj2;
        }

        public async Task<HadbObject<T>?> AtomicSearchByKeyAsync<T>(T model, string nameSpace = Consts.Strings.HadbDefaultNameSpace, bool and = false, CancellationToken cancel = default) where T : HadbData
        {
            nameSpace = nameSpace._HadbNameSpaceNormalize();
            CheckBegan();
            model.Normalize();
            return await AtomicSearchByKeyAsync<T>(model.GetKeys(), nameSpace, and, cancel);
        }

        public async Task<HadbObject<T>?> AtomicSearchByKeyAsync<T>(HadbKeys keys, string nameSpace = Consts.Strings.HadbDefaultNameSpace, bool and = false, CancellationToken cancel = default) where T : HadbData
            => await AtomicSearchByKeyAsync(keys, typeof(T).Name, nameSpace, and, cancel);

        public async Task<HadbObject?> AtomicSearchByKeyAsync(HadbKeys keys, string typeName, string nameSpace = Consts.Strings.HadbDefaultNameSpace, bool and = false, CancellationToken cancel = default)
        {
            nameSpace = nameSpace._HadbNameSpaceNormalize();
            CheckBegan();
            Hadb.CheckIfReady();

            HadbObject? ret = await Hadb.AtomicSearchDataByKeyFromDatabaseImplAsync(this, keys, typeName, nameSpace, and, cancel);

            if (ret == null) return null;

            this.AddApplyObject(ret);

            if (ret.Deleted) return null;

            return ret;
        }

        public async Task<IEnumerable<HadbObject<T>>> AtomicSearchByLabelsAsync<T>(T model, string nameSpace = Consts.Strings.HadbDefaultNameSpace, CancellationToken cancel = default) where T : HadbData
        {
            nameSpace = nameSpace._HadbNameSpaceNormalize();
            model.Normalize();
            return (await AtomicSearchByLabelsAsync(model.GetLabels(), typeof(T).Name, nameSpace, cancel)).Select(x => x.GetGenerics<T>());
        }

        public async Task<IEnumerable<HadbObject<T>>> AtomicSearchByLabelsAsync<T>(HadbLabels labels, string nameSpace = Consts.Strings.HadbDefaultNameSpace, CancellationToken cancel = default) where T : HadbData
            => (await AtomicSearchByLabelsAsync(labels, typeof(T).Name, nameSpace, cancel)).Select(x => x.GetGenerics<T>());

        public async Task<IEnumerable<HadbObject>> AtomicSearchByLabelsAsync(HadbLabels labels, string typeName, string nameSpace = Consts.Strings.HadbDefaultNameSpace, CancellationToken cancel = default)
        {
            nameSpace = nameSpace._HadbNameSpaceNormalize();
            CheckBegan();
            Hadb.CheckIfReady();

            IEnumerable<HadbObject> items = await Hadb.AtomicSearchDataListByLabelsFromDatabaseImplAsync(this, labels, typeName, nameSpace, cancel);

            this.AddApplyObjects(items);

            return items.Where(x => x.Deleted == false);
        }

        public async Task<HadbObject<T>> AtomicDeleteByKeyAsync<T>(T model, string nameSpace = Consts.Strings.HadbDefaultNameSpace, bool and = false, CancellationToken cancel = default) where T : HadbData
        {
            CheckBegan();
            model.Normalize();
            return await AtomicDeleteByKeyAsync<T>(model.GetKeys(), nameSpace, model.GetMaxArchivedCount(), and, cancel);
        }

        public async Task<HadbObject<T>> AtomicDeleteByKeyAsync<T>(HadbKeys keys, string nameSpace = Consts.Strings.HadbDefaultNameSpace, int maxArchive = int.MaxValue, bool and = false, CancellationToken cancel = default) where T : HadbData
            => await AtomicDeleteByKeyAsync(keys, typeof(T).Name, nameSpace, maxArchive, and, cancel);

        public async Task<HadbObject> AtomicDeleteByKeyAsync(HadbKeys keys, string typeName, string nameSpace = Consts.Strings.HadbDefaultNameSpace, int maxArchive = int.MaxValue, bool and = false, CancellationToken cancel = default)
        {
            nameSpace = nameSpace._HadbNameSpaceNormalize();
            CheckBegan();
            var obj = await this.AtomicSearchByKeyAsync(keys, typeName, nameSpace, and, cancel);
            if (obj == null)
            {
                throw new CoresLibException($"Object not found. keys = {keys._ObjectToJson(compact: true)}");
            }

            return await AtomicDeleteAsync(obj.Uid, typeName, nameSpace, maxArchive, cancel);
        }

        public async Task<HadbObject<T>> AtomicDeleteAsync<T>(string uid, string nameSpace = Consts.Strings.HadbDefaultNameSpace, int maxArchive = int.MaxValue, CancellationToken cancel = default) where T : HadbData
            => await AtomicDeleteAsync(uid, typeof(T).Name, nameSpace, maxArchive, cancel);

        public async Task<HadbObject> AtomicDeleteAsync(string uid, string typeName, string nameSpace = Consts.Strings.HadbDefaultNameSpace, int maxArchive = int.MaxValue, CancellationToken cancel = default)
        {
            nameSpace = nameSpace._HadbNameSpaceNormalize();
            CheckBegan();
            Hadb.CheckIfReady();

            HadbObject ret = await Hadb.AtomicDeleteDataFromDatabaseImplAsync(this, uid, typeName, nameSpace, maxArchive, cancel);

            this.AddApplyObject(ret);

            return ret;
        }

        public async Task<string> AtomicGetKvAsync(string key, CancellationToken cancel = default)
        {
            CheckBegan();
            Hadb.CheckIfReady();

            string ret = await Hadb.AtomicGetKvImplAsync(this, key, cancel);

            return ret;
        }

        public async Task<StrDictionary<string>> AtomicGetKvListAsync(CancellationToken cancel = default)
        {
            CheckBegan();
            Hadb.CheckIfReady();

            return await Hadb.AtomicGetKvListImplAsync(this, cancel);
        }

        public async Task AtomicSetKvAsync(string key, string value, CancellationToken cancel = default)
        {
            CheckBegan();
            Hadb.CheckIfReady();

            await Hadb.AtomicSetKvImplAsync(this, key, value, cancel);
        }

        internal async Task AtomicAddSnapshotInternalAsync(EnsureInternal yes, HadbSnapshot snap, CancellationToken cancel = default)
        {
            CheckBegan();
            Hadb.CheckIfReady();

            await Hadb.AtomicAddSnapImplAsync(this, snap, cancel);
        }

        public async Task AtomicAddLogAsync(HadbLog log, string nameSpace = Consts.Strings.HadbDefaultNameSpace, string ext1 = "", string ext2 = "", CancellationToken cancel = default)
        {
            nameSpace = nameSpace._HadbNameSpaceNormalize();
            CheckBegan();
            Hadb.CheckIfReady();

            string ft1 = "";
            string ft2 = "";

            var logFlags = log.GetLogFlags();

            if (logFlags.Bit(HadbLogFlags.NoFullTextSearch) == false && this.Hadb.Settings.OptionFlags.Bit(HadbOptionFlags.BuildFullTextSearchText))
            {
                ft1 = log.GenerateFt1();
                ft2 = log.GenerateFt2();
            }

            await Hadb.AtomicAddLogImplAsync(this, log, nameSpace, ext1, ext2, ft1, ft2, cancel);
        }

        public async Task<HadbLogQueryResponse> AtomicSearchLogAsync<T>(HadbLogQuery query, string nameSpace = Consts.Strings.HadbDefaultNameSpace, CancellationToken cancel = default) where T : HadbLog
            => await AtomicSearchLogAsync(typeof(T).Name, query, nameSpace, cancel);

        public async Task<HadbLogQueryResponse> AtomicSearchLogAsync(string typeName, HadbLogQuery query, string nameSpace = Consts.Strings.HadbDefaultNameSpace, CancellationToken cancel = default)
        {
            nameSpace = nameSpace._HadbNameSpaceNormalize();
            CheckBegan();
            Hadb.CheckIfReady();

            return await Hadb.AtomicSearchLogImplAsync(this, typeName, query, nameSpace, cancel);
        }

        public async Task<IEnumerable<HadbStat>> EnumStatAsync(DateTimeOffset dtStart, DateTimeOffset dtEnd, int maxCount, CancellationToken cancel = default)
        {
            CheckBegan();
            Hadb.CheckIfReady();

            List<HadbStat> ret = new List<HadbStat>();

            var list = await Hadb.EnumStatImplAsync(this, dtStart, dtEnd, maxCount, cancel);

            foreach (var item in list)
            {
                ret.Add(new HadbStat(item));
            }

            return ret;
        }

        //public async Task<bool> LazyUpdateAsync(HadbObject obj, CancellationToken cancel = default)
        //{
        //    CheckBegan();
        //    Hadb.CheckIfReady();
        //    if (obj.Deleted || obj.Archive) return false;
        //    obj.CheckIsNotMemoryDbObject();

        //    return await Hadb.LazyUpdateImplAsync(this, obj, cancel);
        //}

        public async Task<HadbQuick<T>?> AtomicGetQuickAsync<T>(string uid, string nameSpace = Consts.Strings.HadbDefaultNameSpace, CancellationToken cancel = default)
        {
            CheckBegan();
            Hadb.CheckIfReady();

            return await Hadb.AtomicGetQuickFromDatabaseImplAsync<T>(this, uid, nameSpace, cancel);
        }

        public async Task<T?> AtomicGetQuickValueAsync<T>(string uid, string nameSpace = Consts.Strings.HadbDefaultNameSpace, CancellationToken cancel = default)
        {
            var obj = await AtomicGetQuickAsync<T>(uid, nameSpace, cancel);
            if (obj == null) return default;
            return obj.Data;
        }

        public async Task<IEnumerable<HadbQuick<T>>> AtomicSearchQuickStartWithAsync<T>(string key, string nameSpace = Consts.Strings.HadbDefaultNameSpace, CancellationToken cancel = default)
        {
            CheckBegan();
            Hadb.CheckIfReady();

            return await Hadb.AtomicSearchQuickByKeyOnDatabaseImplAsync<T>(this, key, true, nameSpace, cancel);
        }

        public async Task<List<T>> AtomicSearchQuickStartWithValueAsync<T>(string key, string nameSpace = Consts.Strings.HadbDefaultNameSpace, CancellationToken cancel = default)
        {
            var obj = await AtomicSearchQuickStartWithAsync<T>(key, nameSpace, cancel);
            if (obj == null) return new List<T>();

            List<T> ret = new List<T>();
            foreach (var item in obj)
            {
                ret.Add(item.Data);
            }
            return ret;
        }

        public async Task<HadbQuick<T>?> AtomicSearchQuickAsync<T>(string key, string nameSpace = Consts.Strings.HadbDefaultNameSpace, CancellationToken cancel = default)
        {
            CheckBegan();
            Hadb.CheckIfReady();

            var list = await Hadb.AtomicSearchQuickByKeyOnDatabaseImplAsync<T>(this, key, false, nameSpace, cancel);

            return list.SingleOrDefault();
        }

        public async Task<T?> AtomicSearchQuickValueAsync<T>(string key, string nameSpace = Consts.Strings.HadbDefaultNameSpace, CancellationToken cancel = default)
        {
            var obj = await AtomicSearchQuickAsync<T>(key, nameSpace, cancel);
            if (obj == null) return default;

            return obj.Data;
        }

        public async Task AtomicUpdateQuickAsync<T>(string uid, T userData, string nameSpace = Consts.Strings.HadbDefaultNameSpace, CancellationToken cancel = default)
        {
            CheckBegan();
            Hadb.CheckIfReady();

            await Hadb.AtomicUpdateQuickByUidOnDatabaseImplAsync<T>(this, uid, nameSpace, userData, cancel);
        }

        public async Task<bool> AtomicAddOrUpdateQuickAsync<T>(string key, T userData, bool doNotOverwrite = false, string nameSpace = Consts.Strings.HadbDefaultNameSpace, CancellationToken cancel = default)
        {
            CheckBegan();
            Hadb.CheckIfReady();

            return await Hadb.AtomicAddOrUpdateQuickByKeyOnDatabaseImplAsync(this, key, nameSpace, userData, doNotOverwrite, cancel);
        }

        public async Task<bool> AtomicDeleteQuickAsync<T>(string uid, string nameSpace = Consts.Strings.HadbDefaultNameSpace, CancellationToken cancel = default)
        {
            CheckBegan();
            Hadb.CheckIfReady();

            return await Hadb.AtomicDeleteQuickByUidOnDatabaseImplAsync<T>(this, uid, nameSpace, cancel);
        }

        public async Task<int> AtomicDeleteQuickAsync<T>(string key, bool startWith, string nameSpace = Consts.Strings.HadbDefaultNameSpace, CancellationToken cancel = default)
        {
            CheckBegan();
            Hadb.CheckIfReady();

            return await Hadb.AtomicDeleteQuickByKeyOnDatabaseImplAsync<T>(this, key, startWith, nameSpace, cancel);
        }
    }
}


#endif

