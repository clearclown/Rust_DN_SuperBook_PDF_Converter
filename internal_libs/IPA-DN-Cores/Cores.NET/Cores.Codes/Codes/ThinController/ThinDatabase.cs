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

#if CORES_CODES_THINCONTROLLER

using System;
using System.Buffers;
using System.Data;
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
using System.Runtime.Serialization;
using System.Net;
using System.Net.Sockets;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;


using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

using IPA.Cores.Codes;
using IPA.Cores.Helper.Codes;
using static IPA.Cores.Globals.Codes;

using Newtonsoft.Json;

namespace IPA.Cores.Codes;

public class ThinDbVar : INormalizable // 注意! MemDb ファイル保存するため、むやみに [JsonIgnore] を書かないこと！！
{
    [EasyKey]
    public int VAR_ID { get; set; }
    public string VAR_NAME { get; set; } = "";
    public string VAR_VALUE1 { get; set; } = "";
    public string? VAR_VALUE2 { get; set; }
    public string? VAR_VALUE3 { get; set; }
    public string? VAR_VALUE4 { get; set; }
    public string? VAR_VALUE5 { get; set; }
    public string? VAR_VALUE6 { get; set; }

    public void Normalize()
    {
        this.VAR_NAME = this.VAR_NAME._NonNullTrim();
        this.VAR_VALUE1 = this.VAR_VALUE1._NonNullTrim();
        this.VAR_VALUE2 = this.VAR_VALUE2._TrimIfNonNull();
        this.VAR_VALUE3 = this.VAR_VALUE3._TrimIfNonNull();
        this.VAR_VALUE4 = this.VAR_VALUE4._TrimIfNonNull();
        this.VAR_VALUE5 = this.VAR_VALUE5._TrimIfNonNull();
        this.VAR_VALUE6 = this.VAR_VALUE6._TrimIfNonNull();
    }
}

public class ThinDbSvc // 注意! MemDb ファイル保存するため、むやみに [JsonIgnore] を書かないこと！！
{
    [EasyManualKey]
    public string SVC_NAME { get; set; } = "";
    public string SVC_TITLE { get; set; } = "";
}

public class ThinDbMachine // 注意! MemDb ファイル保存するため、むやみに [JsonIgnore] を書かないこと！！
{
    [SimpleTableOrder(1)]
    public int MACHINE_ID { get; set; }
    [SimpleTableIgnore]
    public string SVC_NAME { get; set; } = "";
    [EasyManualKey]
    [SimpleTableOrder(3)]
    public string MSID { get; set; } = "";
    [SimpleTableOrder(2)]
    public string PCID { get; set; } = "";
    [SimpleTableIgnore]
    public int PCID_VER { get; set; }
    [SimpleTableIgnore]
    public DateTime PCID_UPDATE_DATE { get; set; } = Util.ZeroDateTimeValue;
    [SimpleTableIgnore]
    public string CERT_HASH { get; set; } = "";
    [SimpleTableIgnore]
    [NoDebugDump]
    public string HOST_SECRET2 { get; set; } = "";
    [SimpleTableOrder(5)]
    public DateTime CREATE_DATE { get; set; } = Util.ZeroDateTimeValue;
    [SimpleTableIgnore]
    public DateTime UPDATE_DATE { get; set; } = Util.ZeroDateTimeValue;
    [SimpleTableOrder(7)]
    public DateTime LAST_SERVER_DATE { get; set; } = Util.ZeroDateTimeValue;
    [SimpleTableOrder(8)]
    public DateTime LAST_CLIENT_DATE { get; set; } = Util.ZeroDateTimeValue;
    [SimpleTableOrder(9)]
    public int NUM_SERVER { get; set; }
    [SimpleTableOrder(10)]
    public int NUM_CLIENT { get; set; }
    [SimpleTableOrder(4)]
    public string CREATE_IP { get; set; } = "";
    [SimpleTableIgnore]
    public string CREATE_HOST { get; set; } = "";
    [SimpleTableOrder(6)]
    public string LAST_IP { get; set; } = "";
    [SimpleTableOrder(6.1)]
    public string LAST_CLIENT_IP { get; set; } = "";
    [SimpleTableIgnore]
    public string REAL_PROXY_IP { get; set; } = "";
    [SimpleTableIgnore]
    public string LAST_FLAG { get; set; } = "";
    [SimpleTableIgnore]
    public DateTime FIRST_CLIENT_DATE { get; set; } = Util.ZeroDateTimeValue;
    [SimpleTableIgnore]
    public string WOL_MACLIST { get; set; } = "";
    [SimpleTableIgnore]
    public long SERVERMASK64 { get; set; }
    [SimpleTableIgnore]
    public string JSON_ATTRIBUTES { get; set; } = "";
}

public class ThinMemoryDb // 注意! MemDb ファイル保存するため、むやみに [JsonIgnore] を書かないこと！！
{
    // データベースからもらってきたデータ
    public List<ThinDbSvc> SvcList = new List<ThinDbSvc>();
    public List<ThinDbVar> VarList = new List<ThinDbVar>();
    public List<ThinDbMachine> MachineList = new List<ThinDbMachine>();

    // 以下は [JsonIgnore] を付けること！！

    // 上記データをもとにハッシュ化したデータ
    [JsonIgnore]
    public Dictionary<string, ThinDbSvc> SvcBySvcName = new Dictionary<string, ThinDbSvc>(StrComparer.IgnoreCaseComparer);

    [JsonIgnore]
    public Dictionary<string, List<ThinDbVar>> VarByName = new Dictionary<string, List<ThinDbVar>>(StrComparer.IgnoreCaseComparer);

    [JsonIgnore]
    public Dictionary<string, ThinDbMachine> MachineByPcidAndSvcName = new Dictionary<string, ThinDbMachine>(StrComparer.IgnoreCaseComparer);
    [JsonIgnore]
    public Dictionary<string, ThinDbMachine> MachineByMsid = new Dictionary<string, ThinDbMachine>(StrComparer.IgnoreCaseComparer);
    [JsonIgnore]
    public Dictionary<string, ThinDbMachine> MachineByCertHashAndHostSecret2 = new Dictionary<string, ThinDbMachine>(StrComparer.IgnoreCaseComparer);

    [JsonIgnore]
    public int MaxSessionsPerGate;
    [JsonIgnore]
    public string ControllerGateSecretKey = "";
    [JsonIgnore]
    public int ControllerMaxConcurrentWpcRequestProcessingForUsers;
    [JsonIgnore]
    public int ControllerDbFullReloadIntervalMsecs;
    [JsonIgnore]
    public int ControllerDbWriteUpdateIntervalMsecs;
    [JsonIgnore]
    public int ControllerDbBackupFileWriteIntervalMsecs;
    [JsonIgnore]
    public int ControllerRecordStatIntervalMsecs;
    [JsonIgnore]
    public string WildCardDnsDomainName = "";

    public ThinMemoryDb() { }

    // データベースから構築
    public ThinMemoryDb(IEnumerable<ThinDbSvc> svcTable, IEnumerable<ThinDbMachine> machineTable, IEnumerable<ThinDbVar> varTable, IEnumerable<ThinDatabasePcidChangeHistory?>? pcidChangeHistory = null)
    {
        this.SvcList = svcTable.OrderBy(x => x.SVC_NAME, StrComparer.IgnoreCaseComparer).ToList();
        this.VarList = varTable.OrderBy(x => x.VAR_NAME).ThenBy(x => x.VAR_ID).ToList();
        this.MachineList = machineTable.OrderBy(x => x.MACHINE_ID).ToList();

        BuildOnMemoryData(pcidChangeHistory);
    }

    // ファイルから復元
    public ThinMemoryDb(string filePath)
    {
        ThinMemoryDb? tmp = Lfs.ReadJsonFromFile<ThinMemoryDb>(filePath, nullIfError: true);
        if (tmp == null)
        {
            tmp = Lfs.ReadJsonFromFile<ThinMemoryDb>(filePath + ".bak", nullIfError: false);
        }

        this.SvcList = tmp.SvcList;
        this.MachineList = tmp.MachineList;
        this.VarList = tmp.VarList;

        BuildOnMemoryData();
    }

    // オンメモリデータの構築
    void BuildOnMemoryData(IEnumerable<ThinDatabasePcidChangeHistory?>? pcidChangeHistory = null)
    {
        this.SvcList.ForEach(x => this.SvcBySvcName.TryAdd(x.SVC_NAME, x));

        this.VarList.ForEach(v =>
        {
            v.Normalize();

            this.VarByName._GetOrNew(v.VAR_NAME, () => new List<ThinDbVar>()).Add(v);
        });

        // MSID または認証キーによるハッシュを生成
        this.MachineList.ForEach(x =>
        {
            this.MachineByMsid.TryAdd(x.MSID, x);
            this.MachineByCertHashAndHostSecret2.TryAdd(x.CERT_HASH + "@" + x.HOST_SECRET2, x);
        });

        if (pcidChangeHistory != null)
        {
            // PCID 変更履歴を適用
            foreach (var hist in pcidChangeHistory)
            {
                if (hist != null)
                {
                    var machine = this.MachineByMsid._GetOrDefault(hist.Msid);
                    if (machine != null)
                    {
                        if (hist.Ver > machine.PCID_VER)
                        {
                            // PCID 変更履歴のほうがより新しいので、メモリ上の PCID を書換える
                            machine.PCID = hist.Pcid;
                            machine.PCID_VER = hist.Ver;
                            machine.PCID_UPDATE_DATE = hist.UpdateDateTime;
                        }
                    }
                }
            }
        }

        // PCID 名によるハッシュを生成
        RebuildPcidListOnMemory();

        // 頻繁にアクセスされる変数を予め読み出しておく
        this.MaxSessionsPerGate = this.VarByName._GetOrDefault("MaxSessionsPerGate")?.FirstOrDefault()?.VAR_VALUE1._ToInt() ?? 0;
        this.ControllerGateSecretKey = this.VarByName._GetOrDefault("ControllerGateSecretKey")?.FirstOrDefault()?.VAR_VALUE1._NonNullTrim() ?? "";

        this.ControllerMaxConcurrentWpcRequestProcessingForUsers = this.VarByName._GetOrDefault("ControllerMaxConcurrentWpcRequestProcessingForUsers")?.FirstOrDefault()?.VAR_VALUE1._ToInt() ?? 0;
        this.ControllerDbFullReloadIntervalMsecs = this.VarByName._GetOrDefault("ControllerDbFullReloadIntervalMsecs")?.FirstOrDefault()?.VAR_VALUE1._ToInt() ?? 0;
        this.ControllerDbWriteUpdateIntervalMsecs = this.VarByName._GetOrDefault("ControllerDbWriteUpdateIntervalMsecs")?.FirstOrDefault()?.VAR_VALUE1._ToInt() ?? 0;
        this.ControllerDbBackupFileWriteIntervalMsecs = this.VarByName._GetOrDefault("ControllerDbBackupFileWriteIntervalMsecs")?.FirstOrDefault()?.VAR_VALUE1._ToInt() ?? 0;
        this.ControllerRecordStatIntervalMsecs = this.VarByName._GetOrDefault("ControllerRecordStatIntervalMsecs")?.FirstOrDefault()?.VAR_VALUE1._ToInt() ?? 0;
        this.WildCardDnsDomainName = this.VarByName._GetOrDefault("WildCardDnsDomainName")?.FirstOrDefault()?.VAR_VALUE1._NonNullTrim() ?? ThinControllerConsts.Default_WildCardDnsDomainName;
    }

    // PCID 一覧のリビルド (PCID に変更が発生したので Dictionary をリビルドする)
    public void RebuildPcidListOnMemory()
    {
        Dictionary<string, ThinDbMachine> tmp = new Dictionary<string, ThinDbMachine>(StrComparer.IgnoreCaseComparer);

        // 重複発生時は PCID_UPDATE_DATE が大きいほど優先
        this.MachineList.OrderByDescending(x => x.PCID_UPDATE_DATE).ThenBy(x => x.MACHINE_ID)._DoForEach(x =>
        {
            var svc = this.SvcBySvcName[x.SVC_NAME];

            tmp.TryAdd(x.PCID + "@" + svc.SVC_NAME, x);
        });

        this.MachineByPcidAndSvcName = tmp;
    }

    public long SaveToFile(string filePath)
    {
        string backupPath = filePath + ".bak";

        try
        {
            if (Lfs.IsFileExists(filePath))
            {
                Lfs.CopyFile(filePath, backupPath);
            }
        }
        catch (Exception ex)
        {
            ex._Error();
        }

        return Lfs.WriteJsonToFile(filePath, this, flags: FileFlags.AutoCreateDirectory | FileFlags.OnCreateSetCompressionFlag);
    }
}

public class ThinDatabaseUpdateJob
{
    public Func<Database, CancellationToken, Task> ProcAsync { get; }

    public ThinDatabaseUpdateJob(Func<Database, CancellationToken, Task> procAsync)
    {
        ProcAsync = procAsync;
    }
}

public class ThinDatabasePcidChangeHistory
{
    public string Msid { get; }
    public int Ver { get; }
    public string Pcid { get; } = "";
    public DateTime UpdateDateTime { get; } = ZeroDateTimeValue;

    public ThinDatabasePcidChangeHistory(string msid, int ver, string pcid, DateTime dt)
    {
        Msid = msid;
        Ver = ver;
        Pcid = pcid;
        UpdateDateTime = dt;
    }
}

public class ThinDatabase : AsyncServiceWithMainLoop
{
    Task ReadMainLoopTask, WriteMainLoopTask;

    public ThinController Controller { get; }

    public ThinMemoryDb? MemDb { get; private set; }

    public bool IsLoaded => MemDb != null; // 一度でもメモリロードされたら true

    public bool IsDatabaseConnected { get; private set; } // 読み出しメインループからの最後のデータベース接続に成功していれば true、それ以外の場合は false。DB で不具合が発生していることを検出するため

    public string BackupFileName { get; }

    readonly FastCache<string, ThinDatabasePcidChangeHistory> PcidChangeHistoryCache = new FastCache<string, ThinDatabasePcidChangeHistory>(ThinControllerConsts.Max_ControllerDbReadFullReloadIntervalMsecs * 4, comparer: StrComparer.IgnoreCaseComparer);
    readonly CriticalSection PcidChangeLock = new CriticalSection<ThinDatabasePcidChangeHistory>();

    readonly ConcurrentQueue<ThinDatabaseUpdateJob> LazyUpdateJobQueue = new ConcurrentQueue<ThinDatabaseUpdateJob>();

    public int LazyUpdateJobQueueLength => LazyUpdateJobQueue.Count;

    public bool IsStarted { get; private set; }

    public ThinDatabase(ThinController controller)
    {
        try
        {
            this.BackupFileName = PP.Combine(Env.AppLocalDir, "Config", "ThinControllerDatabaseBackupCache", "DatabaseBackupCache.json");

            this.Controller = controller;

            this.ReadMainLoopTask = ReadMainLoopAsync(this.GrandCancel)._LeakCheck();
            this.WriteMainLoopTask = WriteMainLoopAsync(this.GrandCancel)._LeakCheck();
        }
        catch
        {
            this._DisposeSafe();
            throw;
        }
    }

    public void StartLoop()
    {
        this.IsStarted = true;
    }

    public void EnqueueUpdateJob(Func<Database, CancellationToken, Task> proc)
    {
        int maxQueueLength = Math.Max(ThinControllerConsts.ControllerMaxDatabaseWriteQueueLength, 1);

        // キューがいっぱいの場合は古いものから削除する
        while (LazyUpdateJobQueue.Count >= maxQueueLength)
        {
            LazyUpdateJobQueue.TryDequeue(out _);
        }

        LazyUpdateJobQueue.Enqueue(new ThinDatabaseUpdateJob(proc));

        Controller.StatMan?.AddReport("EnqueueUpdateJob_Total", 1);
    }

    public async Task<Database> OpenDatabaseForReadAsync(CancellationToken cancel = default)
    {
        Database db = new Database(this.Controller.SettingsFastSnapshot.DbConnectionString_Read, defaultIsolationLevel: ThinControllerConsts.Controller_DatabaseIsolationLevel_Read);

        try
        {
            await db.EnsureOpenAsync(cancel);

            Controller.StatMan?.AddReport("OpenDatabaseForReadAsync_Total", 1);

            return db;
        }
        catch
        {
            await db._DisposeSafeAsync();
            throw;
        }
    }

    public async Task<Database> OpenDatabaseForWriteAsync(CancellationToken cancel = default)
    {
        Database db = new Database(this.Controller.SettingsFastSnapshot.DbConnectionString_Write, defaultIsolationLevel: ThinControllerConsts.Controller_DatabaseIsolationLevel_Write);

        try
        {
            await db.EnsureOpenAsync(cancel);

            Controller.StatMan?.AddReport("OpenDatabaseForWriteAsync_Total", 1);

            return db;
        }
        catch
        {
            await db._DisposeSafeAsync();
            throw;
        }
    }

    long LastBackupSaveTick = 0;

    // データベースから最新の情報取得メイン
    async Task ReadCoreAsync(CancellationToken cancel)
    {
        try
        {
            IEnumerable<ThinDbSvc> allSvcs = null!;
            IEnumerable<ThinDbMachine> allMachines = null!;
            IEnumerable<ThinDbVar> allVars = null!;

            try
            {
                await using var db = await OpenDatabaseForReadAsync(cancel);

                Controller.Throughput_DatabaseRead.Add(1);

                await db.TranReadSnapshotIfNecessaryAsync(async () =>
                {
                    allSvcs = await db.EasySelectAsync<ThinDbSvc>("select * from SVC", cancel: cancel);
                    allMachines = await db.EasySelectAsync<ThinDbMachine>("select * from MACHINE", cancel: cancel);
                    allVars = await db.EasySelectAsync<ThinDbVar>("select * from VAR", cancel: cancel);
                });

                IsDatabaseConnected = true;
                $"ThinDatabase.ReadCoreAsync Read All Records from DB: {allMachines.Count()}"._Debug();
            }
            catch (Exception ex)
            {
                IsDatabaseConnected = false;
                $"ThinDatabase.ReadCoreAsync Database Connect Error: {ex.ToString()}"._Error();
                throw;
            }

            ThinMemoryDb mem = null!;

            // 最近このサーバーで PCID 変更がなされた履歴のほうが新しい場合はダウンロードしたデータ中における PCID の変更を行なう
            // この処理は PCID 変更処理と排他である
            using (await RegisterOrRenamePcidAsyncLock.LockWithAwait(cancel))
            {
                // PCID 変更履歴ヒストリを取得
                IEnumerable<ThinDatabasePcidChangeHistory?> historyItems = PcidChangeHistoryCache.GetValues();

                // メモリデータベースを構築
                mem = new ThinMemoryDb(allSvcs, allMachines, allVars, historyItems);

                this.MemDb = mem;
            }

            // 構築したメモリデータベースをバックアップファイルに保存
            long now = TickNow;

            if (LastBackupSaveTick == 0 || now >= (LastBackupSaveTick + Controller.CurrentValue_ControllerDbBackupFileWriteIntervalMsecs))
            {
                try
                {
                    long size = mem.SaveToFile(this.BackupFileName);

                    $"ThinDatabase.ReadCoreAsync Save to the backup file: {size._ToString3()} bytes, filename = '{this.BackupFileName}'"._Debug();

                    LastBackupSaveTick = now;
                }
                catch (Exception ex)
                {
                    ex._Error();
                }
            }

            Controller.StatMan?.AddReport("ReadFromDb_OK_Total", 1);
        }
        catch
        {
            // データベースからもバックアップファイルからもまだデータが読み込まれていない場合は、バックアップファイルから読み込む
            if (this.MemDb == null)
            {
                this.MemDb = new ThinMemoryDb(this.BackupFileName);
            }

            Controller.StatMan?.AddReport("ReadFromDb_Error_Total", 1);

            // バックアップファイルの読み込みを行なった上で、DB 例外はちゃんと throw する
            throw;
        }
    }

    public int LastDbReadTookMsecs { get; private set; } = 0;

    // データベースから最新の情報を取得するタスク
    async Task ReadMainLoopAsync(CancellationToken cancel)
    {
        int numCycle = 0;
        int numError = 0;

        $"ThinDatabase.ReadMainLoopAsync: Waiting for start."._Debug();
        await TaskUtil.AwaitWithPollAsync(Timeout.Infinite, 1000, () => this.IsStarted, cancel);
        $"ThinDatabase.ReadMainLoopAsync: Started."._Debug();

        while (cancel.IsCancellationRequested == false)
        {
            numCycle++;

            $"ThinDatabase.ReadMainLoopAsync numCycle={numCycle}, numError={numError} Start."._Debug();

            long startTick = Time.HighResTick64;

            bool ok = false;

            try
            {
                await ReadCoreAsync(cancel);
                ok = true;
            }
            catch (Exception ex)
            {
                ex._Error();
            }

            long endTick = Time.HighResTick64;

            if (ok)
            {
                LastDbReadTookMsecs = (int)(endTick - startTick);
            }
            else
            {
                LastDbReadTookMsecs = 0;
            }

            $"ThinDatabase.ReadMainLoopAsync numCycle={numCycle}, numError={numError} End. Took time: {endTick - startTick}"._Debug();

            await cancel._WaitUntilCanceledAsync(Util.GenRandInterval(Controller.CurrentValue_ControllerDbFullReloadIntervalMsecs));
        }
    }

    // データベース更新メイン
    async Task<int> WriteCoreAsync(CancellationToken cancel)
    {
        int num = 0;
        await using var db = await OpenDatabaseForWriteAsync(cancel);

        await db.EnsureOpenAsync(cancel);

        // キューが空になるまで 実施 いたします
        while (cancel.IsCancellationRequested == false)
        {
            ThinDatabaseUpdateJob? queue = null;

            if (LazyUpdateJobQueue.TryDequeue(out queue) == false)
            {
                break;
            }

            Controller.Throughput_DatabaseWrite.Add(1);

            RetryHelper<int> r = new RetryHelper<int>(100, 10);

            await r.RunAsync(async c =>
            {
                await queue.ProcAsync(db, c);

                return 0;
            }, cancel: cancel);

            num++;
        }

        return num;
    }

    // データベースを更新するタスク
    async Task WriteMainLoopAsync(CancellationToken cancel)
    {
        int numCycle = 0;
        int numError = 0;

        $"ThinDatabase.WriteMainLoopAsync: Waiting for start."._Debug();
        await TaskUtil.AwaitWithPollAsync(Timeout.Infinite, 1000, () => this.IsStarted, cancel);
        $"ThinDatabase.WriteMainLoopAsync: Started."._Debug();

        while (cancel.IsCancellationRequested == false)
        {
            if (this.LazyUpdateJobQueue.Count >= 1)
            {
                numCycle++;

                $"ThinDatabase.WriteMainLoopAsync numCycle={numCycle}, numError={numError} Start."._Debug();

                long startTick = Time.HighResTick64;
                int num = 0;

                try
                {
                    num = await WriteCoreAsync(cancel);
                }
                catch (Exception ex)
                {
                    ex._Error();
                }

                long endTick = Time.HighResTick64;

                $"ThinDatabase.WriteMainLoopAsync numCycle={numCycle}, numError={numError} End. Written items: {num}, Took time: {endTick - startTick}"._Debug();
            }

            await cancel._WaitUntilCanceledAsync(Util.GenRandInterval(Controller.CurrentValue_ControllerDbWriteUpdateIntervalMsecs));
        }
    }

    // 便利な Var 取得ルーチン集
    public IEnumerable<ThinDbVar>? GetVars(string name)
    {
        var db = this.MemDb;
        if (db == null) return null;

        if (db.VarByName.TryGetValue(name, out List<ThinDbVar>? list) == false)
        {
            return new ThinDbVar[0];
        }

        return list;
    }
    public ThinDbVar? GetVar(string name)
        => GetVars(name)?.FirstOrDefault();

    [return: NotNullIfNotNull("defaultValue")]
    public string? GetVarString(string name, string? defaultValue = null)
        => GetVar(name)?.VAR_VALUE1 ?? defaultValue;

    public int GetVarInt(string name, int defaultValue = 0)
        => GetVarString(name)?._ToInt() ?? defaultValue;

    public bool GetVarBool(string name, bool defaultValue = false)
        => GetVarString(name)?._ToBool(defaultValue) ?? defaultValue;

    protected override async Task CleanupImplAsync(Exception? ex)
    {
        try
        {
            await this.ReadMainLoopTask._TryWaitAsync();
            await this.WriteMainLoopTask._TryWaitAsync();
        }
        finally
        {
            await base.CleanupImplAsync(ex);
        }
    }

    readonly AsyncLock RegisterOrRenamePcidAsyncLock = new AsyncLock();
    readonly AsyncLock PaidApiAsyncLock = new AsyncLock();

    // WoL MAC の即時更新
    public async Task UpdateDbForWolMacAsync(string msid, string wolMacList, long serverMask64, DateTime now, CancellationToken cancel)
    {
        msid = msid._NonNullTrim();
        now = now._NormalizeDateTime();
        wolMacList = wolMacList._NonNull();

        if (this.IsDatabaseConnected == false)
        {
            // データベースエラー発生中はこの処理は実行できない (スキップいたします)
            return;
        }

        await using var db = await OpenDatabaseForWriteAsync(cancel);

        Controller.Throughput_DatabaseWrite.Add(1);

        await db.QueryWithNoReturnAsync("UPDATE MACHINE SET LAST_SERVER_DATE = @, WOL_MACLIST = @, SERVERMASK64 = @ WHERE MSID = @",
            now, wolMacList, serverMask64, msid);
    }

    // ClientConnect によるデータベース情報更新
    public void UpdateDbForClientConnect(string msid, DateTime now, string lastClientIp)
    {
        msid = msid._NonNullTrim();
        now = now._NormalizeDateTime();
        lastClientIp = lastClientIp._NonNullTrim();

        EnqueueUpdateJob(async (db, c) =>
        {
            await db.QueryWithNoReturnAsync(
                "UPDATE MACHINE SET NUM_CLIENT = NUM_CLIENT + 1, LAST_CLIENT_DATE = @, LAST_CLIENT_IP = @ WHERE MSID = @ " +
                "UPDATE MACHINE SET FIRST_CLIENT_DATE = @ WHERE MSID = @ AND (FIRST_CLIENT_DATE IS NULL OR FIRST_CLIENT_DATE <= '1900/01/01')",
                now, lastClientIp, msid,
                now, msid);
        });
    }

    // ServerConnect によるデータベース情報更新
    public void UpdateDbForServerConnect(string msid, DateTime now, string lastIp, string realProxyIp, string lastFlag, string wolMacList, long serverMask64)
    {
        msid = msid._NonNullTrim();
        now = now._NormalizeDateTime();
        lastIp = lastIp._NonNullTrim();
        realProxyIp = realProxyIp._NonNullTrim();
        lastFlag = lastFlag._NonNullTrim();
        wolMacList = wolMacList._NonNull();

        EnqueueUpdateJob(async (db, c) =>
        {
            await db.QueryWithNoReturnAsync("UPDATE MACHINE SET NUM_SERVER = NUM_SERVER + 1, LAST_SERVER_DATE = @, LAST_IP = @, REAL_PROXY_IP = @, LAST_FLAG = @, WOL_MACLIST = @, SERVERMASK64 = @ WHERE MSID = @",
                now, lastIp, realProxyIp, lastFlag, wolMacList, serverMask64, msid);
        });
    }

    // PCID 変更実行
    public async Task<VpnError> RenamePcidAsync(string msid, string newPcid, DateTime now, CancellationToken cancel = default)
    {
        // この関数は同時に 1 ユーザーからしか実行されないようにする
        // (ローカルメモリデータベースをいじるため)
        using var asyncLock = await RegisterOrRenamePcidAsyncLock.LockWithAwait(cancel);

        msid = msid._NonNullTrim();
        newPcid = newPcid._NonNullTrim();

        VpnError err2 = ThinController.CheckPCID(newPcid);
        if (err2 != VpnError.ERR_NO_ERROR) return err2;

        VpnError err = VpnError.ERR_INTERNAL_ERROR;

        // ローカルメモリデータベース上の PCID 情報を確認
        var memDb = this.MemDb!;

        if (memDb.MachineList.Where(x => x.PCID._IsSamei(newPcid)).Any())
        {
            // ローカルメモリデータベース上で重複
            return VpnError.ERR_PCID_ALREADY_EXISTS;
        }

        if (this.IsDatabaseConnected == false)
        {
            // データベースエラー発生中はこの処理は実行できない
            return VpnError.ERR_TEMP_ERROR;
        }

        await using var db = await OpenDatabaseForWriteAsync(cancel);

        Controller.Throughput_DatabaseWrite.Add(1);

        ThinDbMachine? updatedMachine = null;

        // トランザクションを確立し厳格なチェックを実施
        // (DB サーバー側で一意インデックスによりチェックするが、インデックスが間違っていた場合に備えて、トランザクションでも厳密にチェックするのである)
        if (await db.TranAsync(async () =>
        {
            // MACHINE を取得
            var machine = await db.EasySelectSingleAsync<ThinDbMachine>("SELECT * FROM MACHINE WHERE MSID = @MSID", new { MSID = msid }, false, true, cancel);
            if (machine == null)
            {
                // おかしいな
                err = VpnError.ERR_SECURITY_ERROR;
                return false;
            }

            // 同一 PCID が存在しないかどうかチェック
            if ((await db.QueryWithValueAsync("SELECT COUNT(MACHINE_ID) FROM MACHINE WHERE PCID = @ AND SVC_NAME = @", newPcid, machine.SVC_NAME)).Int != 0)
            {
                err = VpnError.ERR_PCID_ALREADY_EXISTS;
                return false;
            }

            // 変更の実行
            await db.QueryWithNoReturnAsync("UPDATE MACHINE SET PCID = @, UPDATE_DATE = @, PCID_UPDATE_DATE = @, PCID_VER = PCID_VER + 1 WHERE MSID = @",
            newPcid, now, now, msid);

            // 変更した結果を取得
            updatedMachine = await db.EasySelectSingleAsync<ThinDbMachine>("SELECT * FROM MACHINE WHERE MSID = @MSID", new { MSID = msid }, false, true, cancel);
            if (updatedMachine == null)
            {
                // おかしいな
                err = VpnError.ERR_SECURITY_ERROR;
                return false;
            }

            return true;
        }) == false)
        {
            return err;
        }

        updatedMachine._MarkNotNull();

        Controller.AddPcidToRecentPcidCandidateCache(newPcid);

        // ローカルメモリデータベース上の PCID 情報を変更
        var machine = memDb.MachineByMsid._GetOrDefault(msid);

        if (machine != null)
        {
            machine.PCID = newPcid;
            machine.PCID_UPDATE_DATE = updatedMachine.PCID_UPDATE_DATE;
            machine.PCID_VER = updatedMachine.PCID_VER;

            // メモリ上の PCID Dictionary をリビルド
            memDb.RebuildPcidListOnMemory();

            // PCID 変更履歴の更新
            this.PcidChangeHistoryCache.Add(machine.MSID, new ThinDatabasePcidChangeHistory(machine.MSID, machine.PCID_VER, newPcid, updatedMachine.PCID_UPDATE_DATE));
        }

        return VpnError.ERR_NO_ERROR;
    }

    async Task AddApiLogAsync(Database db, string type, object data, CancellationToken cancel = default)
    {
        JsonRpcClientInfo? clientInfo = TaskVar<JsonRpcClientInfo>.Value;

        await db.EasyExecuteAsync("insert into APILOG (APILOG_DT, APILOG_CLIENT, APILOG_TYPE, APILOG_DATA) values (@APILOG_DT, @APILOG_CLIENT, @APILOG_TYPE, @APILOG_DATA)",
            new
            {
                APILOG_DT = DtOffsetNow,
                APILOG_CLIENT = clientInfo?._ObjectToJson(compact: true) ?? "",
                APILOG_TYPE = type._NonNullTrim(),
                APILOG_DATA = data._ObjectToJson(compact: true),
            }
            );
    }

    // 商用サービス開発者向け: サーバー情報の取得、アクティベーション、アクティベーション解除処理
    public async Task<ThinControllerRpcServerObjectInfo?> Paid_GetOrSetServerObjectInfoAsync(string hostKey, bool? newState = null, string tag = "", CancellationToken cancel = default)
    {
        hostKey = hostKey._NonNullTrim()._NormalizeHexString();

        // データベースエラー時は処理禁止
        if (IsDatabaseConnected == false)
        {
            throw new VpnException(VpnError.ERR_TEMP_ERROR);
        }

        await using var db = await OpenDatabaseForWriteAsync(cancel);

        // この関数は同時に 1 ユーザーからしか実行されないようにする (高負荷防止のため)
        using var asyncLock = await RegisterOrRenamePcidAsyncLock.LockWithAwait(cancel);

        ThinControllerRpcServerObjectInfo? ret = null;

        await db.TranAsync(async () =>
        {
            var machine = await db.EasySelectSingleAsync<ThinDbMachine>("select * from MACHINE where CERT_HASH = @CERT_HASH",
                new
                {
                    CERT_HASH = hostKey,
                }, false, true, cancel);

            if (machine == null)
            {
                return false;
            }

            var oldStatus = this.Controller.Paid_CalcServerLicenseStatus(machine.PCID, machine.JSON_ATTRIBUTES, machine.FIRST_CLIENT_DATE._AsDateTimeOffset(true, true));

            if (newState.HasValue == false || newState.Value == oldStatus.IsCurrentActivated)
            {
                // 状態の変更なし
                ret = oldStatus;
                return false;
            }

            bool activate = newState.Value;

            EasyJsonStrAttributes json = new EasyJsonStrAttributes(machine.JSON_ATTRIBUTES);

            json["Paid_IsCurrentActivated"] = activate._ToBoolStrLower();
            if (activate)
            {
                json["Paid_ActivatedOnceInPast"] = true._ToBoolStrLower();
                json["Paid_ActivatedDateTime"] = DtOffsetNow._ToDtStr(true);
            }
            else
            {
                json["Paid_DeactivatedDateTime"] = DtOffsetNow._ToDtStr(true);
            }
            json["Paid_Tag"] = tag;

            string newJsonStr = json.ToString();

            var newStatus = this.Controller.Paid_CalcServerLicenseStatus(machine.PCID, newJsonStr, machine.FIRST_CLIENT_DATE._AsDateTimeOffset(true, true));

            int i = await db.EasyExecuteAsync("update MACHINE set JSON_ATTRIBUTES = @JSON_ATTRIBUTES where CERT_HASH = @CERT_HASH",
                new
                {
                    JSON_ATTRIBUTES = newJsonStr,
                    CERT_HASH = machine.CERT_HASH,
                });

            if (i != 1)
            {
                throw new CoresLibException("Database update error. i != 1.");
            }

            await this.AddApiLogAsync(db, activate ? "Activate" : "Deactivate",
                new
                {
                    UniqueId = machine.CERT_HASH,
                    Pcid = machine.PCID,
                    OldStatus = oldStatus,
                    NewStatus = newStatus,
                });

            ret = newStatus;

            return true;
        });

        return ret;
    }

    // サーバー登録実行
    public async Task<VpnError> RegisterMachineAsync(string svcName, string msid, string pcid, string hostKey, string hostSecret2, DateTime now, string ip, string fqdn, string initialJsonAttributes, CancellationToken cancel = default)
    {
        svcName = svcName._NonNullTrim();
        msid = msid._NonNullTrim();
        pcid = pcid._NonNullTrim();
        hostKey = hostKey._NonNullTrim();
        hostSecret2 = hostSecret2._NonNullTrim();
        ip = ip._NonNullTrim();
        fqdn = fqdn._NonNullTrim();
        initialJsonAttributes = initialJsonAttributes._NonNullTrim();

        VpnError err2 = ThinController.CheckPCID(pcid);
        if (err2 != VpnError.ERR_NO_ERROR) return err2;

        // データベースエラー時は処理禁止
        if (IsDatabaseConnected == false)
        {
            return VpnError.ERR_TEMP_ERROR;
        }

        VpnError err = VpnError.ERR_INTERNAL_ERROR;

        await using var db = await OpenDatabaseForWriteAsync(cancel);

        // この関数は同時に 1 ユーザーからしか実行されないようにする
        // (ローカルメモリデータベースをいじるため)
        using var asyncLock = await RegisterOrRenamePcidAsyncLock.LockWithAwait(cancel);

        Controller.Throughput_DatabaseWrite.Add(1);

        // トランザクションを確立し厳格なチェックを実施
        // (DB サーバー側で一意インデックスによりチェックするが、インデックスが間違っていた場合に備えて、トランザクションでも厳密にチェックするのである)
        if (await db.TranAsync(async () =>
        {
            // 同一 hostKey が存在しないかどうか確認
            if ((await db.QueryWithValueAsync("SELECT COUNT(MACHINE_ID) FROM MACHINE WHERE CERT_HASH = @", hostKey)).Int != 0)
            {
                err = VpnError.ERR_SECURITY_ERROR;
                return false;
            }

            // 同一シークレットが存在しないかどうかチェック
            if ((await db.QueryWithValueAsync("SELECT COUNT(MACHINE_ID) FROM MACHINE WHERE HOST_SECRET2 = @", hostSecret2)).Int != 0)
            {
                err = VpnError.ERR_SECURITY_ERROR;
                return false;
            }

            // 同一 PCID が存在しないかどうかチェック
            if ((await db.QueryWithValueAsync("SELECT COUNT(MACHINE_ID) FROM MACHINE WHERE PCID = @ AND SVC_NAME = @", pcid, svcName)).Int != 0)
            {
                err = VpnError.ERR_PCID_ALREADY_EXISTS;
                return false;
            }

            // 登録の実行
            await db.QueryWithNoReturnAsync("INSERT INTO MACHINE (SVC_NAME, MSID, PCID, CERT_HASH, CREATE_DATE, UPDATE_DATE, LAST_SERVER_DATE, LAST_CLIENT_DATE, NUM_SERVER, NUM_CLIENT, CREATE_IP, CREATE_HOST, HOST_SECRET2, PCID_UPDATE_DATE, JSON_ATTRIBUTES) " +
                            "VALUES (@, @, @, @, @, @, @, @, @, @, @, @, @, @, @)",
                            svcName, msid, pcid, hostKey,
                            now, now, now, now,
                            0, 0,
                            ip, fqdn,
                            hostSecret2,
                            now, initialJsonAttributes);

            return true;
        }) == false)
        {
            return err;
        }

        Controller.AddPcidToRecentPcidCandidateCache(pcid);

        return VpnError.ERR_NO_ERROR;
    }

    // MSID によるデータベースからの最新の Machine の取得
    public async Task<ThinDbMachine?> SearchMachineByMsidFromDbForceAsync(string msid, CancellationToken cancel = default)
    {
        if (IsDatabaseConnected == false)
        {
            // データベース読み込みメインループでエラーが発生している場合は、データベース接続を試行しない (大量の試行がなされ不具合が発生するおそれがあるため)
            return null;
        }

        cancel.ThrowIfCancellationRequested();

        Controller.Throughput_DatabaseRead.Add(1);

        // ローカルメモリデータベースの検索でヒットしなかった場合 (たとえば、最近作成されたホストの場合) は、マスタデータベースを物理的に検索する
        await using var db = await OpenDatabaseForReadAsync(cancel);

        var foundMachine = await db.TranReadSnapshotIfNecessaryAsync(async () =>
        {
            return await db.EasySelectSingleAsync<ThinDbMachine>("select * from MACHINE where MSID = @MSID",
                new
                {
                    MSID = msid,
                },
                throwErrorIfMultipleFound: true, throwErrorIfNotFound: false, cancel: cancel);
        });

        return foundMachine;
    }

    // SvcName および Pcid による Machine の検索試行
    public async Task<ThinDbMachine?> SearchMachineByPcidAsync(string svcName, string pcid, CancellationToken cancel = default)
    {
        // まずローカルメモリデータベースを検索する
        var mem = this.MemDb;
        if (mem != null)
        {
            var foundMachine = mem.MachineByPcidAndSvcName._GetOrDefault(pcid + "@" + svcName);
            if (foundMachine != null)
            {
                // 発見
                return foundMachine;
            }
        }

        if (IsDatabaseConnected == false)
        {
            // データベース読み込みメインループでエラーが発生している場合は、データベース接続を試行しない (大量の試行がなされ不具合が発生するおそれがあるため)
            return null;
        }

        cancel.ThrowIfCancellationRequested();

        // ローカルメモリデータベースの検索でヒットしなかった場合 (たとえば、最近作成されたホストの場合) は、マスタデータベースを物理的に検索する
        await using var db = await OpenDatabaseForReadAsync(cancel);

        Controller.Throughput_DatabaseRead.Add(1);

        var foundMachine2 = await db.TranReadSnapshotIfNecessaryAsync(async () =>
        {
            return await db.EasySelectSingleAsync<ThinDbMachine>("select * from MACHINE where PCID = @PCID and SVC_NAME = @SVC_NAME",
                new
                {
                    PCID = pcid,
                    SVC_NAME = svcName,
                },
                throwErrorIfMultipleFound: true, throwErrorIfNotFound: false, cancel: cancel);
        });

        if (foundMachine2 != null)
        {
            // 発見
            return foundMachine2;
        }

        // 未発見
        return null;
    }

    // HostKey および HostSecret2 による認証試行
    public async Task<ThinDbMachine?> AuthMachineAsync(string hostKey, string hostSecret2, CancellationToken cancel = default)
    {
        // まずローカルメモリデータベースを検索する
        var mem = this.MemDb;
        if (mem != null)
        {
            var foundMachine = mem.MachineByCertHashAndHostSecret2._GetOrDefault(hostKey + "@" + hostSecret2);
            if (foundMachine != null)
            {
                // 発見
                return foundMachine;
            }

            // 2020/04 頃に登録された古いマシンは hostSecret2 がデータベースに登録されていない場合がある
            foundMachine = mem.MachineByCertHashAndHostSecret2._GetOrDefault(hostKey + "@");
            if (foundMachine != null)
            {
                // 発見
                // データベースに hostSecret2 を登録する (つまり、アップグレード)
                await using var db2 = await OpenDatabaseForWriteAsync(cancel);

                Controller.Throughput_DatabaseWrite.Add(1);

                await db2.QueryWithNoReturnAsync("UPDATE MACHINE SET HOST_SECRET2 = @ WHERE MSID = @ and HOST_SECRET2 = ''",
                    hostSecret2, foundMachine.MSID);

                $"AuthMachineAsync: Upgrade hostSecret2: MSID = {foundMachine.MSID}"._Debug();

                return foundMachine;
            }
        }

        if (IsDatabaseConnected == false)
        {
            // データベース読み込みメインループでエラーが発生している場合は、データベース接続を試行しない (大量の試行がなされ不具合が発生するおそれがあるため)
            return null;
        }

        cancel.ThrowIfCancellationRequested();

        // ローカルメモリデータベースの検索でヒットしなかった場合 (たとえば、最近作成されたホストの場合) は、マスタデータベースを物理的に検索する
        await using var db = await OpenDatabaseForReadAsync(cancel);

        Controller.Throughput_DatabaseRead.Add(1);

        var foundMachine2 = await db.TranReadSnapshotIfNecessaryAsync(async () =>
        {
            // 2020/04 頃に登録された古いマシンは hostSecret2 がデータベースに登録されていない場合がある
            return await db.EasySelectSingleAsync<ThinDbMachine>("select * from MACHINE where CERT_HASH = @CERT_HASH and (HOST_SECRET2 = @HOST_SECRET2 OR HOST_SECRET2 = '')",
            new
            {
                CERT_HASH = hostKey,
                HOST_SECRET2 = hostSecret2,
            },
            throwErrorIfMultipleFound: true, throwErrorIfNotFound: false, cancel: cancel);
        });

        if (foundMachine2 != null)
        {
            // 発見
            return foundMachine2;
        }

        // 未発見
        return null;
    }
}

#endif

