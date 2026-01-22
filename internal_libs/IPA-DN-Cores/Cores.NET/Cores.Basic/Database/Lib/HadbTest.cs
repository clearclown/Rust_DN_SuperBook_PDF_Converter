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

#if CORES_BASIC_DATABASE

#pragma warning disable CA2235 // Mark all non-serializable fields
#pragma warning disable CS0162 // 到達できないコードが検出されました

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

namespace IPA.Cores.Basic;

public static class HadbCodeTest2
{
    public class Dyn : HadbDynamicConfig
    {
        public string Hello { get; set; } = "";
    }

    public class Sys : HadbSqlBase<Mem, Dyn>
    {
        public Sys(HadbSqlSettings settings, Dyn dynamicConfig) : base(settings, dynamicConfig) { }
    }

    public class Mem : HadbMemDataBase
    {
        protected override List<Type> GetDefinedUserDataTypesImpl()
        {
            List<Type> ret = new List<Type>();
            ret.Add(typeof(Record));
            return ret;
        }

        protected override List<Type> GetDefinedUserLogTypesImpl()
        {
            List<Type> ret = new List<Type>();
            //ret.Add(typeof(Log));
            return ret;
        }
    }

    public class Record : HadbData
    {
        public string HostName { get; set; } = "";
        public string IpAddress1 { get; set; } = "";
        public string IpAddress2 { get; set; } = "";
        public string AuthKey { get; set; } = "";

        public override void Normalize()
        {
            this.HostName = this.HostName._NormalizeKey(true);
            this.IpAddress1 = this.IpAddress1._NormalizeIp();
            this.IpAddress2 = this.IpAddress2._NormalizeIp();
            this.AuthKey = this.AuthKey._NormalizeKey(true);
        }

        public override HadbKeys GetKeys() => new HadbKeys(this.HostName, this.AuthKey);
        public override HadbLabels GetLabels() => new HadbLabels(this.IpAddress1, this.IpAddress2);

        public override int GetMaxArchivedCount() => 5;
    }

    public static async Task ManyUpdatesTestAsync(HadbSqlSettings settings, int count)
    {
        await using Sys sys1 = new Sys(settings, new Dyn() { Hello = "Hello World" });

        sys1.Start();
        await sys1.WaitUntilReadyForAtomicAsync(2);

        HadbObject obj = null!;
        Record rec = null!;

        await sys1.TranAsync(true, async tran =>
        {
            Record r = new Record { HostName = "host" + Str.GenRandStr(), AuthKey = Str.GenRandStr(), IpAddress1 = Str.GenRandStr(), IpAddress2 = Str.GenRandStr() };

            obj = await tran.AtomicAddAsync(r);
            rec = r;
            return true;
        });

        for (int i = 0; i < count; i++)
        {
            //i._Print();
            await sys1.TranAsync(true, async tran =>
            {
                //var obj2 = await tran.AtomicSearchByKeyAsync(new Record { AuthKey = rec.AuthKey });
                var obj2 = await tran.AtomicGetAsync<Record>(obj.Uid);
                var r = obj2!.GetData();

                r.IpAddress1 = Str.GenRandStr();

                await tran.AtomicUpdateAsync(obj2);

                Measure1?.Add(1);

                return true;
            });
        }
    }

    public static async Task DummyInsertsTestAsync(HadbSqlSettings settings, int count, int thread_id)
    {
        await using Sys sys1 = new Sys(settings, new Dyn() { Hello = "Hello World" });

        sys1.Start();
        await sys1.WaitUntilReadyForAtomicAsync(2);

        HadbObject obj = null!;
        Record rec = null!;

        for (int i = 0; i < count; i++)
        {
            await sys1.TranAsync(true, async tran =>
            {
                Record r = new Record { HostName = "host_" + thread_id.ToString("D10") + "_" + i.ToString("D10"), AuthKey = "auth_" + i.ToString("D10") + "_" + thread_id.ToString("D10"), IpAddress1 = Str.GenRandStr().Substring(0, 4), IpAddress2 = Str.GenRandStr().Substring(0, 6) };

                obj = await tran.AtomicAddAsync(r);
                rec = r;
                Measure1?.Add(1);
                return true;
            });
        }
    }

    static ThroughputMeasuse? Measure1 = null;

    public static void StartMeasure()
    {
        if (Measure1 == null)
        {
            Measure1 = new ThroughputMeasuse(1000, 1000);
            Measure1.StartPrinter(intervalMsecs: 1000, toStr3: true);
        }
    }

    public static async Task OnMemoryQueryTestLoopAsync(Sys sys, int maxCounts, int maxThreadId)
    {
        await Task.Yield();
        while (true)
        {
            int thread_id = (Util.RandSInt31() % maxThreadId) + 1;
            int i = Util.RandSInt31() % maxCounts;

            Record byHostName = new Record { HostName = "host_" + thread_id.ToString("D10") + "_" + i.ToString("D10") };
            //Record byAuthKey = new Record { AuthKey = "auth_" + i.ToString("D10") + "_" + thread_id.ToString("D10") };

            var obj1 = sys.FastSearchByKey(byHostName);
            if (obj1 == null)
            {
                Environment.FailFast("NULL!");
            }
            Measure1?.Add(1);
        }
    }

    public static async Task DummyRandomUpdatesTestAsync(HadbSqlSettings settings, int maxCounts, int maxThreadId)
    {
        await using Sys sys1 = new Sys(settings, new Dyn() { Hello = "Hello World" });

        sys1.Start();
        await sys1.WaitUntilReadyForAtomicAsync(2);

        for (int j = 0; ; j++)
        {
            await sys1.TranAsync(true, async tran =>
            {
                //for (int k = 0; k < 100; k++)
                {
                    int thread_id = (Util.RandSInt31() % maxThreadId) + 1;
                    int i = Util.RandSInt31() % maxCounts;

                    Record byHostName = new Record { HostName = "host_" + thread_id.ToString("D10") + "_" + i.ToString("D10") };
                    //Record byAuthKey = new Record { AuthKey = "auth_" + i.ToString("D10") + "_" + thread_id.ToString("D10") };

                    var obj1 = await tran.AtomicSearchByKeyAsync(byHostName);
                    //var obj2 = await tran.AtomicSearchByKeyAsync(byAuthKey);

                    //if (obj1 == null) byHostName.GetUserDataJsonString()._Print();
                    //if (obj2 == null) byAuthKey.GetUserDataJsonString()._Print();

                    //obj1!.GetUserDataJsonString()._Print();
                    //obj2!.GetUserDataJsonString()._Print();

                    var r1 = obj1!.GetData();
                    //var r2 = obj2.GetData<Record>();

                    r1.IpAddress1 = Str.GenRandStr().Substring(0, 4);
                    r1.IpAddress2 = Str.GenRandStr().Substring(0, 6);

                    //r2.IpAddress1 = Str.GenRandStr().Substring(0, 4);
                    //r2.IpAddress2 = Str.GenRandStr().Substring(0, 6);

                    if (tran.IsWriteMode)
                    {
                        await tran.AtomicUpdateAsync(obj1);
                        Measure1?.Add(1);
                    }
                    //await tran.AtomicUpdateAsync(obj2);
                }
                return true;
            });
        }
    }
}

public static class HadbCodeTest
{
    public class Dyn : HadbDynamicConfig
    {
        public Dyn()
        {
            this.HadbAutomaticSnapshotIntervalMsecs = 0;
        }

        public string Hello { get; set; } = "";
    }

    public class Sys : HadbSqlBase<Mem, Dyn>
    {
        public Sys(HadbSqlSettings settings, Dyn dynamicConfig) : base(settings, dynamicConfig) { }
    }

    [Flags]
    public enum LogEvent : long
    {
        None = 0,
        Hello,
        Dog,
        Cat,
        Mouse,
    }

    public class Log : HadbLog
    {
        [JsonConverter(typeof(StringEnumConverter))]
        public LogEvent Event { get; set; } = LogEvent.None;

        public string Str { get; set; } = "";
        public int Int { get; set; } = 0;

        public override void Normalize()
        {
            this.Str = this.Str._NonNullTrim();
        }

        public override HadbLabels GetLabels()
            => new HadbLabels(this.Event.ToString(), this.Str, this.Int == 0 ? "" : this.Int.ToString());
    }

    public class User : HadbData
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string AuthKey { get; set; } = "";
        public string FullName { get; set; } = "";
        public string Company { get; set; } = "";
        public string LastIp { get; set; } = "";

        public int Int1 { get; set; } = 0;

        public override void Normalize()
        {
            this.Id = this.Id._NonNullTrim();
            this.Name = this.Name._NonNullTrim();
            this.AuthKey = this.AuthKey._NonNullTrim();
            this.Company = this.Company._NonNullTrim();
            this.FullName = this.FullName._NonNullTrim();
            this.LastIp = this.LastIp._NormalizeIp();
        }

        public override HadbKeys GetKeys()
        {
            return new HadbKeys(this.Id, this.Name, this.AuthKey);
        }

        public override HadbLabels GetLabels()
        {
            return new HadbLabels(this.Company, this.LastIp);
        }

        public override int GetMaxArchivedCount() => 10;
        //public override int GetMaxArchivedCount() => int.MaxValue;
    }

    public class Mem : HadbMemDataBase
    {
        protected override List<Type> GetDefinedUserDataTypesImpl()
        {
            List<Type> ret = new List<Type>();
            ret.Add(typeof(User));
            return ret;
        }

        protected override List<Type> GetDefinedUserLogTypesImpl()
        {
            List<Type> ret = new List<Type>();
            ret.Add(typeof(Log));
            return ret;
        }
    }


    public static async Task Test1Async(HadbSqlSettings settings, string systemName, bool backupTest, bool parallel)
    {
        await using Sys sys1 = new Sys(settings, new Dyn() { Hello = "Hello World" });
        await using Sys sys2 = new Sys(settings, new Dyn() { Hello = "Hello World" });

        sys1.Start();
        await sys1.WaitUntilReadyForAtomicAsync(2);

        sys2.Start();
        await sys2.WaitUntilReadyForAtomicAsync(2);
        //return;
        // Dynamic Config が DB に正しく反映されているか

        if (settings.OptionFlags.Bit(HadbOptionFlags.NoInitConfigDb) == false)
        {
            await sys1.TranAsync(true, async tran =>
            {
                var db = (tran as HadbSqlBase<Mem, Dyn>.HadbSqlTran)!.Db;

                var rows = await db.EasySelectAsync<HadbSqlConfigRow>("select * from HADB_CONFIG where CONFIG_SYSTEMNAME = @CONFIG_SYSTEMNAME", new
                {
                    CONFIG_SYSTEMNAME = systemName,
                });

                Dbg.TestTrue((rows.Where(x => x.CONFIG_NAME == "HadbReloadIntervalMsecsLastOk").Single()).CONFIG_VALUE._ToInt() == Consts.HadbDynamicConfigDefaultValues.HadbReloadIntervalMsecsLastOk);
                Dbg.TestTrue((rows.Where(x => x.CONFIG_NAME == "Hello").Single()).CONFIG_VALUE == "Hello World");

                var helloRow = rows.Where(x => x.CONFIG_NAME == "Hello").Single();
                helloRow.CONFIG_VALUE = "Neko";
                await db.EasyUpdateAsync(helloRow);

                return true;
            });

            // DB の値を変更した後、Dynamic Config が正しくメモリに反映されるか
            await sys1.ReloadCoreAsync(EnsureSpecial.Yes);
            Dbg.TestTrue(sys1.CurrentDynamicConfig.Hello == "Neko");

            await sys2.ReloadCoreAsync(EnsureSpecial.Yes);
            Dbg.TestTrue(sys2.CurrentDynamicConfig.Hello == "Neko");

            await sys1.TranAsync(true, async tran =>
            {
                string s = await tran.AtomicGetKvAsync(" inchiki");
                Dbg.TestTrue(s == "");

                await tran.AtomicSetKvAsync("inchiki ", "123");

                return true;
            });

            await sys2.TranAsync(false, async tran =>
            {
                string s = await tran.AtomicGetKvAsync("inchiki  ");
                Dbg.TestTrue(s == "123");
                return true;
            });

            await sys1.TranAsync(true, async tran =>
            {
                string s = await tran.AtomicGetKvAsync("   inchiki");
                Dbg.TestTrue(s == "123");

                await tran.AtomicSetKvAsync(" inchiki", "456");

                return true;
            });

            await sys2.TranAsync(false, async tran =>
            {
                var db = (tran as HadbSqlBase<Mem, Dyn>.HadbSqlTran)!.Db;

                var test = await db.QueryWithValueAsync("select count(*) from HADB_KV where KV_SYSTEM_NAME = @ and KV_KEY = @", sys1.SystemName, "inchiki");

                Dbg.TestTrue(test.Int == 1);

                return true;
            });
        }

        for (int i = 0; i < 2; i++)
        {
            string nameSpace = $"__NameSpace_{i}__";
            string nameSpace2 = $"__NekoSpace_{i}__";
            string u1_uid = "";
            string u2_uid = "";
            string u3_uid = "";

            Con.WriteLine($"--- Namespace: {nameSpace} ---");


            for (int k = 0; k < 10; k++)
            {
                // 以下、Quick のテスト
                await sys1.TranAsync(true, async tran =>
                {
                    Dbg.TestFalse(await tran.AtomicAddOrUpdateQuickAsync("neko1", "Hello", true, nameSpace));
                    Dbg.TestFalse(await tran.AtomicAddOrUpdateQuickAsync("neko2", "World", true, nameSpace));
                    Dbg.TestFalse(await tran.AtomicAddOrUpdateQuickAsync("neko3", 123, true, nameSpace));
                    Dbg.TestFalse(await tran.AtomicAddOrUpdateQuickAsync("apple1", "Dog", true, nameSpace));
                    return true;
                }, options: HadbTranOptions.NoTransactionOnWrite);

                await Dbg.TestExceptionAsync(async () =>
                {
                    await sys2.TranAsync(true, async tran =>
                    {
                        Dbg.TestFalse(await tran.AtomicAddOrUpdateQuickAsync(" NEKO1 ", "Hello", true, nameSpace));
                        return true;
                    }, options: HadbTranOptions.NoTransactionOnWrite);
                });

                await sys2.TranAsync(true, async tran =>
                {
                    Dbg.TestTrue(await tran.AtomicAddOrUpdateQuickAsync(" NEKO1 ", "Hello2", false, nameSpace));
                    return true;
                }, options: HadbTranOptions.NoTransactionOnWrite);

                await sys2.TranAsync(false, async tran =>
                {
                    string? s1 = await tran.AtomicSearchQuickValueAsync<string>("neko", nameSpace);
                    Dbg.TestNull(s1);

                    string? neko1 = await tran.AtomicSearchQuickValueAsync<string>("neko1", nameSpace);
                    Dbg.TestTrue(neko1 == "Hello2");

                    List<string> list1 = await tran.AtomicSearchQuickStartWithValueAsync<string>("neko", nameSpace);

                    Dbg.TestTrue(list1.Count == 2);
                    Dbg.TestTrue(list1.Contains("Hello2"));
                    Dbg.TestTrue(list1.Contains("World"));

                    List<int> list2 = await tran.AtomicSearchQuickStartWithValueAsync<int>("neko", nameSpace);

                    Dbg.TestTrue(list2.Count == 1);
                    Dbg.TestTrue(list2[0] == 123);

                    return false;
                }, options: HadbTranOptions.NoTransactionOnWrite);

                await sys1.TranAsync(true, async tran =>
                {
                    int r = await tran.AtomicDeleteQuickAsync<string>(" NEKO ", false, nameSpace);
                    Dbg.TestTrue(r == 0);

                    if (parallel == false)
                    {
                        r = await tran.AtomicDeleteQuickAsync<string>(" NEKO ", true, nameSpace);
                        Dbg.TestTrue(r == 2);
                    }
                    else
                    {
                        r = await tran.AtomicDeleteQuickAsync<string>("neko1", false, nameSpace);
                        Dbg.TestTrue(r == 1);

                        r = await tran.AtomicDeleteQuickAsync<string>("neko2", false, nameSpace);
                        Dbg.TestTrue(r == 1);
                    }

                    return true;
                }, options: HadbTranOptions.NoTransactionOnWrite);

                await sys2.TranAsync(false, async tran =>
                {
                    string? neko1 = await tran.AtomicSearchQuickValueAsync<string>("neko1", nameSpace);
                    Dbg.TestTrue(neko1 == null);

                    List<string> list1 = await tran.AtomicSearchQuickStartWithValueAsync<string>("neko", nameSpace);

                    Dbg.TestTrue(list1.Count == 0);

                    List<int> list2 = await tran.AtomicSearchQuickStartWithValueAsync<int>("neko", nameSpace);

                    Dbg.TestTrue(list2.Count == 1);
                    Dbg.TestTrue(list2[0] == 123);

                    return false;
                }, options: HadbTranOptions.NoTransactionOnWrite);

                await sys1.TranAsync(true, async tran =>
                {
                    if (parallel == false)
                    {
                        int r = await tran.AtomicDeleteQuickAsync<int>(" NEKO ", true, nameSpace);
                        Dbg.TestTrue(r == 1);

                        r = await tran.AtomicDeleteQuickAsync<string>(" Apple ", true, nameSpace);
                        Dbg.TestTrue(r == 1);
                    }
                    else
                    {
                        int r = await tran.AtomicDeleteQuickAsync<int>(" neko3 ", false, nameSpace);
                        Dbg.TestTrue(r == 1);

                        r = await tran.AtomicDeleteQuickAsync<string>(" Apple1 ", false, nameSpace);
                        Dbg.TestTrue(r == 1);
                    }
                    return true;
                }, options: HadbTranOptions.NoTransactionOnWrite);
            }


            // 以下、普通のデータのテスト

            Dbg.TestNull(sys1.FastSearchByKey(new User() { Name = "User1" }, nameSpace));
            Dbg.TestNull(sys1.FastSearchByKey(new User() { Name = "User2" }, nameSpace));
            Dbg.TestNull(sys1.FastSearchByKey(new User() { Name = "User3" }, nameSpace));

            Dbg.TestNull(sys2.FastSearchByKey(new User() { Name = "User1" }, nameSpace));
            Dbg.TestNull(sys2.FastSearchByKey(new User() { Name = "User2" }, nameSpace));
            Dbg.TestNull(sys2.FastSearchByKey(new User() { Name = "User3" }, nameSpace));

            await sys1.TranAsync(true, async tran =>
            {
                await tran.AtomicAddLogAsync(new Log { Event = LogEvent.Cat, Int = 333, Str = "Hello333" }, nameSpace: nameSpace);
                await tran.AtomicAddLogAsync(new Log { Event = LogEvent.Mouse, Int = 444, Str = "Hello444" }, nameSpace: nameSpace);
                await tran.AtomicAddLogAsync(new Log { Event = LogEvent.Dog, Int = 555, Str = "Hello555" }, nameSpace: nameSpace);
                await tran.AtomicAddLogAsync(new Log { Event = LogEvent.Cat, Int = 555, Str = "Hello555" }, nameSpace: nameSpace);
                return true;
            });

            await sys1.TranAsync(false, async tran =>
            {
                HadbLogQueryResponse logs;

                logs = await tran.AtomicSearchLogAsync<Log>(new HadbLogQuery { FullTextFlags = FullTextSearchFlags.None, FullTextQuery = "o and !mouse" }, nameSpace: nameSpace);
                Dbg.TestTrue(logs.Count == 3);

                logs = await tran.AtomicSearchLogAsync<Log>(new HadbLogQuery { /*SearchTemplate = new Log { Event = LogEvent.Cat } */ }, nameSpace: nameSpace);
                Dbg.TestTrue(logs.Count == 4);

                logs = await tran.AtomicSearchLogAsync<Log>(new HadbLogQuery { SearchTemplate = new Log { Event = LogEvent.Cat } }, nameSpace: nameSpace);
                Dbg.TestTrue(logs.Count == 2);

                logs = await tran.AtomicSearchLogAsync<Log>(new HadbLogQuery { FullTextFlags = FullTextSearchFlags.None, FullTextQuery = "cat" }, nameSpace: nameSpace);
                Dbg.TestTrue(logs.Count == 2);

                logs = await tran.AtomicSearchLogAsync<Log>(new HadbLogQuery { FullTextFlags = FullTextSearchFlags.None, FullTextQuery = "ca AND at" }, nameSpace: nameSpace);
                Dbg.TestTrue(logs.Count == 2);

                logs = await tran.AtomicSearchLogAsync<Log>(new HadbLogQuery { FullTextFlags = FullTextSearchFlags.None, FullTextQuery = "ca OR do" }, nameSpace: nameSpace);
                Dbg.TestTrue(logs.Count == 3);

                logs = await tran.AtomicSearchLogAsync<Log>(new HadbLogQuery { FullTextFlags = FullTextSearchFlags.None, FullTextQuery = "ouse" }, nameSpace: nameSpace);
                Dbg.TestTrue(logs.Count == 1);

                logs = await tran.AtomicSearchLogAsync<Log>(new HadbLogQuery { FullTextFlags = FullTextSearchFlags.WordMode, FullTextQuery = "ouse" }, nameSpace: nameSpace);
                Dbg.TestTrue(logs.Count == 0);

                logs = await tran.AtomicSearchLogAsync<Log>(new HadbLogQuery { FullTextFlags = FullTextSearchFlags.FieldNameMode, FullTextQuery = "Event=Cat" }, nameSpace: nameSpace);
                Dbg.TestTrue(logs.Count == 2);

                return false;
            });

            string neko_uid = "";

            await sys1.TranAsync(true, async tran =>
            {
                var obj = await tran.AtomicAddAsync(new User { Id = "NekoSan", AuthKey = "Neko123", Company = "University of Tsukuba", FullName = "Neko YaHoo!", Int1 = 0, LastIp = "9.3.1.7", Name = "Super-San" },
                    nameSpace2);
                neko_uid = obj.Uid;
                return true;
            });


            //await sys1.TranAsync(true, async tran =>
            //{
            //    for (int i = 0; i < 100; i++)
            //    {
            //        var obj = await tran.AtomicAddAsync(new User { Id = $"BulkUser{i}", AuthKey = $"SuperTomato{i}", Company = $"Neko Inu {i}", FullName = $"Neko {i} YaHoo!", Int1 = i, LastIp = $"9.{i}.1.7", Name = $"Super-Tomato {i}" },
            //            nameSpace2);
            //    }
            //    return true;
            //});

            for (int k = 0; k < 20; k++)
            {
                await sys1.TranAsync(true, async tran =>
                {
                    //neko_uid._Print();
                    var obj = await tran.AtomicGetAsync<User>(neko_uid, nameSpace2);
                    var user = obj!.GetData();
                    //user.Name = "Super-Oracle" + i.ToString();
                    user.LastIp = "0.0.0." + i.ToString();

                    await tran.AtomicUpdateAsync(obj);
                    return true;
                });
            }

            await sys1.TranAsync(false, async tran =>
            {
                var list = await tran.AtomicGetArchivedAsync<User>(neko_uid, nameSpace: nameSpace2);
                if ((new User()).GetMaxArchivedCount() != int.MaxValue)
                {
                    Dbg.TestTrue(list.Count() == 11);
                }
                return true;
            });

            RefLong snapshot0 = new RefLong();

            await sys1.TranAsync(true, async tran =>
            {
                User u = new User() { Id = "u1", Name = "User1", AuthKey = "a001", Company = "NTT", LastIp = "A123:b456:0001::c789", FullName = "Tanaka", Int1 = 100 };
                var obj = await tran.AtomicAddAsync(u, nameSpace, "a", "1");
                u1_uid = obj.Uid;
                return true;
            }, takeSnapshot: true, snapshotNoRet: snapshot0);

            var ftResults = sys1.FastSearchByFullText("a123:b456:1::c789", "", false, false, nameof(User), nameSpace);
            Dbg.TestTrue(ftResults.Count == 1);

            var test2 = sys1.FastSearchByKey(new User() { Name = "User1" }, nameSpace);
            Dbg.TestNotNull(test2);
            Dbg.TestTrue(test2.Ft1._InStr("a123:b456:1::c789"));
            Dbg.TestTrue(test2.Ft1._InStr("tanaka"));
            Dbg.TestTrue(test2.Ft1._InStr("a001"));

            {
                await sys2.ReloadCoreAsync(EnsureSpecial.Yes);
                var test3 = sys2.FastSearchByKey(new User() { Name = "User1" }, nameSpace);
                Dbg.TestNotNull(test2);
                var obj = sys2.FastGet<User>(u1_uid, nameSpace);
                Dbg.TestTrue(obj!.Ft1._InStr("a123:b456:1::c789"));
                Dbg.TestTrue(obj.Ft1._InStr("tanaka"));
                Dbg.TestTrue(obj.Ft1._InStr("a001"));
                var u = obj!.GetData();
                Dbg.TestTrue(u.Id == "u1");
                Dbg.TestTrue(u.Name == "User1");
                Dbg.TestTrue(u.AuthKey == "a001");
                Dbg.TestTrue(u.Company == "NTT");
                Dbg.TestTrue(u.LastIp == "a123:b456:1::c789");
                Dbg.TestTrue(u.Int1 == 100);
                Dbg.TestTrue(obj.Ext1 == "a");
                Dbg.TestTrue(obj.Ext2 == "1");
            }

            await sys2.TranAsync(false, async tran =>
            {
                var obj = await tran.AtomicGetAsync<User>(u1_uid, nameSpace);
                Dbg.TestTrue(obj!.SnapshotNo == snapshot0);
                return true;
            });

            await sys1.TranAsync(true, async tran =>
            {
                User u = new User() { Id = "u2", Name = "User2", AuthKey = "a002", Company = "NTT", LastIp = "af80:b456:0001::c789", FullName = "Yamada", Int1 = 200 };
                var obj = await tran.AtomicAddAsync(u, nameSpace, "b", "2");
                u2_uid = obj.Uid;
                return true;
            });

            RefLong snapshot1 = new RefLong();

            await sys1.TranAsync(true, async tran =>
            {
                User u = new User() { Id = "u3", Name = "User3", AuthKey = "a003", Company = "IPA", LastIp = "A123:b456:0001::c789", FullName = "Unagi", Int1 = 300 };
                var obj = await tran.AtomicAddAsync(u, nameSpace, "c", "3");
                u3_uid = obj.Uid;
                return true;
            }, takeSnapshot: true, snapshotNoRet: snapshot1);

            {
                await sys2.ReloadCoreAsync(EnsureSpecial.Yes);
                var obj = sys2.FastSearchByKey<User>(new HadbKeys("", "", "A003"), nameSpace);
                var u = obj!.GetData();
                Dbg.TestTrue(u.Id == "u3");
                Dbg.TestTrue(u.Name == "User3");
                Dbg.TestTrue(u.AuthKey == "a003");
                Dbg.TestTrue(u.Company == "IPA");
                Dbg.TestTrue(u.LastIp == "a123:b456:1::c789");
                Dbg.TestTrue(obj.Ext1 == "c");
                Dbg.TestTrue(obj.Ext2 == "3");
                Dbg.TestTrue(obj.SnapshotNo == snapshot1);
                Dbg.TestTrue(obj.Ft1._InStr("a123:b456:1::c789"));
                Dbg.TestTrue(obj.Ft1._InStr("unagi"));
                Dbg.TestTrue(obj.Ft1._InStr("ipa"));
            }

            if (backupTest)
            {
                $"Local Backup Read Test #1"._Print();
                await using Sys sys3_fromBackup = new Sys(settings, new Dyn() { Hello = "Hello World" });
                sys3_fromBackup.DebugFlags |= HadbDebugFlags.CauseErrorOnDatabaseReload;
                sys3_fromBackup.Start();
                await sys3_fromBackup.WaitUntilReadyForFastAsync();
                $"Local Backup Read OK"._Print();

                var test3 = sys3_fromBackup.FastSearchByKey(new User() { Name = "User1" }, nameSpace);
                Dbg.TestNotNull(test2);
                var obj = sys3_fromBackup.FastGet<User>(u1_uid, nameSpace);
                var u = obj!.GetData();
                Dbg.TestTrue(u.Id == "u1");
                Dbg.TestTrue(u.Name == "User1");
                Dbg.TestTrue(u.AuthKey == "a001");
                Dbg.TestTrue(u.Company == "NTT");
                Dbg.TestTrue(u.LastIp == "a123:b456:1::c789");
                Dbg.TestTrue(u.Int1 == 100);
                Dbg.TestTrue(obj.Ext1 == "a");
                Dbg.TestTrue(obj.Ext2 == "1");

                var x = sys3_fromBackup.FastEnumObjects<User>(nameSpace).ToList();

                Dbg.TestTrue(sys3_fromBackup.FastEnumObjects<User>(nameSpace).Count() == 3);

                if (settings.OptionFlags.Bit(HadbOptionFlags.NoInitConfigDb) == false)
                {
                    Dbg.TestTrue(sys3_fromBackup.CurrentDynamicConfig.Hello == "Neko");
                }

                await Dbg.TestExceptionAsync(async () =>
                {
                    await sys3_fromBackup.TranAsync(true, async tran =>
                    {
                        await tran.AtomicAddAsync(new User { AuthKey = "fbi", Company = "cia", FullName = "dnobori", Id = "nekosanz", Int1 = 123, LastIp = "1.2.3.4", Name = "Filesan" }, nameSpace: nameSpace);
                        return true;
                    });
                });
            }

            await sys2.ReloadCoreAsync(EnsureSpecial.Yes);

            if (backupTest)
            {
                $"Local Backup Read Test #2"._Print();
                await using Sys sys3_fromBackup = new Sys(settings, new Dyn() { Hello = "Hello World" });

                FilePath backupDatabasePath = sys3_fromBackup.Settings.BackupDataFile;
                FilePath backupDynamicConfigPath = sys3_fromBackup.Settings.BackupDynamicConfigFile;

                var fileData1 = await backupDatabasePath.ReadDataFromFileAsync();
                var fileData2 = await backupDynamicConfigPath.ReadDataFromFileAsync();
                fileData1 = fileData1.Slice(0, fileData1.Length - 16);
                fileData2 = fileData2.Slice(0, fileData2.Length - 16);
                await backupDatabasePath.WriteDataToFileAsync(fileData1);
                await backupDynamicConfigPath.WriteDataToFileAsync(fileData2);

                sys3_fromBackup.DebugFlags |= HadbDebugFlags.CauseErrorOnDatabaseReload;
                sys3_fromBackup.Start();
                await sys3_fromBackup.WaitUntilReadyForFastAsync();
                $"Local Backup Read OK"._Print();

                var test3 = sys3_fromBackup.FastSearchByKey(new User() { Name = "User1" }, nameSpace);
                Dbg.TestNotNull(test2);
                var obj = sys3_fromBackup.FastGet<User>(u1_uid, nameSpace);
                var u = obj!.GetData();
                Dbg.TestTrue(u.Id == "u1");
                Dbg.TestTrue(u.Name == "User1");
                Dbg.TestTrue(u.AuthKey == "a001");
                Dbg.TestTrue(u.Company == "NTT");
                Dbg.TestTrue(u.LastIp == "a123:b456:1::c789");
                Dbg.TestTrue(u.Int1 == 100);
                Dbg.TestTrue(obj.Ext1 == "a");
                Dbg.TestTrue(obj.Ext2 == "1");

                var x = sys3_fromBackup.FastEnumObjects<User>(nameSpace).ToList();

                Dbg.TestTrue(sys3_fromBackup.FastEnumObjects<User>(nameSpace).Count() == 3);

                if (settings.OptionFlags.Bit(HadbOptionFlags.NoInitConfigDb) == false)
                {
                    Dbg.TestTrue(sys3_fromBackup.CurrentDynamicConfig.Hello == "Neko");
                }
            }

            if (backupTest)
            {
                $"Local Backup Failure Test"._Print();
                await using Sys sys3_fromBackup = new Sys(settings, new Dyn() { Hello = "Hello World" });

                FilePath backupDatabasePath = sys3_fromBackup.Settings.BackupDataFile;
                FilePath backupDynamicConfigPath = sys3_fromBackup.Settings.BackupDynamicConfigFile;

                // メインデータを壊す
                var fileData1 = await backupDatabasePath.ReadDataFromFileAsync();
                var fileData2 = await backupDynamicConfigPath.ReadDataFromFileAsync();
                fileData1 = fileData1.Slice(0, fileData1.Length - 16);
                fileData2 = fileData2.Slice(0, fileData2.Length - 16);
                await backupDatabasePath.WriteDataToFileAsync(fileData1);
                await backupDynamicConfigPath.WriteDataToFileAsync(fileData2);

                // バックアップデータも壊す
                backupDatabasePath = sys3_fromBackup.Settings.BackupDataFile.PathString + Consts.Extensions.Backup;
                backupDynamicConfigPath = sys3_fromBackup.Settings.BackupDynamicConfigFile.PathString + Consts.Extensions.Backup;

                fileData1 = await backupDatabasePath.ReadDataFromFileAsync();
                fileData2 = await backupDynamicConfigPath.ReadDataFromFileAsync();
                fileData1 = fileData1.Slice(0, fileData1.Length - 16);
                fileData2 = fileData2.Slice(0, fileData2.Length - 16);
                await backupDatabasePath.WriteDataToFileAsync(fileData1);
                await backupDynamicConfigPath.WriteDataToFileAsync(fileData2);

                sys3_fromBackup.DebugFlags |= HadbDebugFlags.CauseErrorOnDatabaseReload;
                sys3_fromBackup.Start();

                await Dbg.TestExceptionAsync(async () =>
                {
                    await sys3_fromBackup.WaitUntilReadyForFastAsync();
                    $"Local Backup Read OK"._Print();
                });

                Dbg.TestTrue(sys3_fromBackup.CurrentDynamicConfig.Hello == "Hello World");
            }

            await sys1.TranAsync(false, async tran =>
            {
                var obj = await tran.AtomicGetAsync<User>(u1_uid, nameSpace);
                var u = obj!.GetData();
                Dbg.TestTrue(u.Id == "u1");
                Dbg.TestTrue(u.Name == "User1");
                Dbg.TestTrue(u.AuthKey == "a001");
                Dbg.TestTrue(u.Company == "NTT");
                Dbg.TestTrue(u.LastIp == "a123:b456:1::c789");
                Dbg.TestTrue(u.Int1 == 100);
                Dbg.TestTrue(obj.Ext1 == "a");
                Dbg.TestTrue(obj.Ext2 == "1");
                Dbg.TestTrue(obj.SnapshotNo == snapshot0);
                Dbg.TestTrue(obj.Ft1._InStr("a123:b456:1::c789"));
                Dbg.TestTrue(obj.Ft1._InStr("a001"));
                Dbg.TestTrue(obj.Ft1._InStr("ntt"));
                return false;
            });

            await sys1.TranAsync(false, async tran =>
            {
                var obj = await tran.AtomicSearchByKeyAsync<User>(new HadbKeys("", "uSER2"), nameSpace);
                var u = obj!.GetData();
                Dbg.TestTrue(u.Id == "u2");
                Dbg.TestTrue(u.Name == "User2");
                Dbg.TestTrue(u.AuthKey == "a002");
                Dbg.TestTrue(u.Company == "NTT");
                Dbg.TestTrue(u.LastIp == "af80:b456:1::c789");
                Dbg.TestTrue(obj.Ext1 == "b");
                Dbg.TestTrue(obj.Ext2 == "2");
                Dbg.TestTrue(obj.SnapshotNo == snapshot0);
                Dbg.TestTrue(obj.Ft1._InStr("af80:b456:1::c789"));
                Dbg.TestTrue(obj.Ft1._InStr("a002"));
                Dbg.TestTrue(obj.Ft1._InStr("ntt"));
                return false;
            });

            await sys1.TranAsync(false, async tran =>
            {
                var obj = await tran.AtomicSearchByLabelsAsync(new User { Company = "ntt" }, nameSpace);
                var list = obj.Select(x => x.GetData()).OrderBy(x => x.Id, StrComparer.IgnoreCaseTrimComparer);
                Dbg.TestTrue(list.Count() == 2);
                var list2 = list.ToArray();
                Dbg.TestTrue(list2[0].Id == "u1");
                Dbg.TestTrue(list2[1].Id == "u2");

                Dbg.TestTrue(obj.Where(x => x.SnapshotNo == snapshot0).Count() == 2);
                Dbg.TestTrue(obj.Where(x => x.SnapshotNo != snapshot0).Count() == 0);
                return false;
            });


            // 高速更新のテスト
            {
                await sys2.ReloadCoreAsync(EnsureSpecial.Yes);
                var obj = sys2.FastSearchByLabels(new User { Company = "ipa" }, nameSpace).Single();
                var u = obj.GetData();
                Dbg.TestTrue(u.Id == "u3");
                Dbg.TestTrue(u.Name == "User3");
                Dbg.TestTrue(u.AuthKey == "a003");
                Dbg.TestTrue(u.Company == "IPA");
                Dbg.TestTrue(u.LastIp == "a123:b456:1::c789");
                Dbg.TestTrue(obj.Ext1 == "c");
                Dbg.TestTrue(obj.Ext2 == "3");
                Dbg.TestTrue(obj.SnapshotNo == snapshot1);
                Dbg.TestTrue(obj.Ft1._InStr("| 300 |"));
                obj.FastUpdate(x =>
                {
                    x.Int1++;
                    return true;
                });
                Dbg.TestTrue(obj.Ft1._InStr("| 301 |"));

                await sys2.LazyUpdateCoreAsync(EnsureSpecial.Yes);
            }

            {
                await sys2.ReloadCoreAsync(EnsureSpecial.Yes);
                var obj = sys2.FastSearchByLabels(new User { Company = "ntt", LastIp = "A123:b456:0001:0000:0000:0000:0000:c789" }, nameSpace).Single();
                var u = obj.GetData();
                Dbg.TestTrue(u.Id == "u1");
                Dbg.TestTrue(u.Name == "User1");
                Dbg.TestTrue(u.AuthKey == "a001");
                Dbg.TestTrue(u.Company == "NTT");
                Dbg.TestTrue(u.LastIp == "a123:b456:1::c789");
                Dbg.TestTrue(obj.Ext1 == "a");
                Dbg.TestTrue(obj.Ext2 == "1");
                Dbg.TestTrue(obj.SnapshotNo == snapshot0);
                Dbg.TestTrue(obj.Ft1._InStr("| 100 |"));
                obj.FastUpdate(x =>
                {
                    x.Int1++;
                    return true;
                });
                Dbg.TestTrue(obj.Ft1._InStr("| 101 |"));
                await sys2.LazyUpdateCoreAsync(EnsureSpecial.Yes);
            }

            {
                await sys1.ReloadCoreAsync(EnsureSpecial.Yes, fullReloadMode: false);
                var obj = sys1.FastSearchByLabels(new User { Company = "ipa" }, nameSpace).Single();
                var u = obj.GetData();
                Dbg.TestTrue(u.Id == "u3");
                Dbg.TestTrue(u.Name == "User3");
                Dbg.TestTrue(u.AuthKey == "a003");
                Dbg.TestTrue(u.Company == "IPA");
                Dbg.TestTrue(u.LastIp == "a123:b456:1::c789");
                Dbg.TestTrue(u.Int1 == 301);
                Dbg.TestTrue(obj.Ext1 == "c");
                Dbg.TestTrue(obj.Ext2 == "3");
                Dbg.TestTrue(obj.SnapshotNo == snapshot1);
                Dbg.TestTrue(obj.Ft1._InStr("| 301 |"));
                obj.FastUpdate(x =>
                {
                    x.Int1++;
                    return true;
                });
                Dbg.TestTrue(obj.Ft1._InStr("| 302 |"));
                await sys1.LazyUpdateCoreAsync(EnsureSpecial.Yes);
            }

            {
                await sys1.ReloadCoreAsync(EnsureSpecial.Yes);
                var obj = sys1.FastSearchByLabels(new User { Company = "ntt", LastIp = "A123:b456:0001:0000:0000:0000:0000:c789" }, nameSpace).Single();
                var u = obj.GetData();
                Dbg.TestTrue(u.Id == "u1");
                Dbg.TestTrue(u.Name == "User1");
                Dbg.TestTrue(u.AuthKey == "a001");
                Dbg.TestTrue(u.Company == "NTT");
                Dbg.TestTrue(u.LastIp == "a123:b456:1::c789");

                if (u.Int1 != 101)
                {
                    $"------ WRONG 4 ! ------\nuid = {obj.Uid}\nu = {u.GetUserDataJsonString()}\n"._Error();
                }
                Dbg.TestTrue(u.Int1 == 101);
                Dbg.TestTrue(obj.Ext1 == "a");
                Dbg.TestTrue(obj.Ext2 == "1");
                Dbg.TestTrue(obj.SnapshotNo == snapshot0);
                Dbg.TestTrue(obj.Ft1._InStr("| 101 |"));
                obj.FastUpdate(x =>
                {
                    x.Int1++;
                    return true;
                });
                Dbg.TestTrue(obj.Ft1._InStr("| 102 |"));
                await sys1.LazyUpdateCoreAsync(EnsureSpecial.Yes);
            }

            // sys2 でデータ編集 途中で Abort してメモリに影響がないかどうか確認
            try
            {
                await sys2.TranAsync(true, async tran =>
                {
                    var obj = await tran.AtomicSearchByKeyAsync(new User { AuthKey = "  A001  " }, nameSpace);
                    var u = obj!.GetData();
                    Dbg.TestTrue(u.FullName == "Tanaka");

                    if (u.Int1 != 102)
                    {
                        $"------ WRONG 3 ! ------\nuid = {obj.Uid}\nu = {u.GetUserDataJsonString()}\n"._Error();
                    }
                    Dbg.TestTrue(u.Int1 == 102);

                    obj.Ext1 = "k";
                    obj.Ext2 = "1234";

                    u.Company = "SoftEther";
                    u.AuthKey = "x001";
                    u.Id = "new_u1";
                    u.Name = "User1New";
                    u.FullName = "Neko";
                    u.LastIp = "2001:af80:0000::0001";
                    u.Int1++;

                    throw new CoresException("Abort!");
                    //return true;
                });
            }
            catch { }

            Dbg.TestTrue(sys2.MemDb!.InternalData.IndexedKeysTable.Where(x => x.Key._InStri(nameSpace)).Count() == (4 * 3));
            Dbg.TestTrue(sys2.MemDb!.InternalData.IndexedLabelsTable.Where(x => x.Key._InStri(nameSpace)).Sum(x => x.Value.Count) == (2 * 3));

            {
                // AtomicSearchByKeyAsync を実行後 abort した結果 sys2 のメモリが更新されていないことを検査
                var obj = sys2.FastSearchByLabels(new User { Company = "ntt", LastIp = "A123:b456:0001:0000:0000:0000:0000:c789" }, nameSpace).Single();
                var u = obj.GetData();
                Dbg.TestTrue(u.Id == "u1");
                Dbg.TestTrue(u.Name == "User1");
                Dbg.TestTrue(u.AuthKey == "a001");
                Dbg.TestTrue(u.Company == "NTT");
                Dbg.TestTrue(u.LastIp == "a123:b456:1::c789");

                if (u.Int1 != 101)
                {
                    $"------ WRONG 2 ! ------\nuid = {obj.Uid}\nu = {u.GetUserDataJsonString()}\n"._Error();
                }
                Dbg.TestTrue(u.Int1 == 101);
                Dbg.TestTrue(obj.Ext1 == "a");
                Dbg.TestTrue(obj.Ext2 == "1");
                Dbg.TestTrue(obj.SnapshotNo == snapshot0);
                Dbg.TestTrue(obj.Ft1._InStr("| 101 |"));
                Dbg.TestTrue(obj.Ft1._InStr("| a001 |"));
                Dbg.TestTrue(obj.Ft1._InStr("| ntt |"));
            }

            RefLong snapshot2 = new RefLong();

            // sys2 でデータ編集コミット --> sys1 でメモリ更新 --> sys1 で高速更新 --> sys2 で観測できるか?
            await sys2.TranAsync(true, async tran =>
            {
                var obj = await tran.AtomicSearchByKeyAsync(new User { AuthKey = "  A001  " }, nameSpace);
                var u = obj!.GetData();
                Dbg.TestTrue(u.FullName == "Tanaka");

                if (u.Int1 != 102)
                {
                    $"------ WRONG 1 ! ------\nuid = {obj.Uid}\nu = {u.GetUserDataJsonString()}\n"._Error();
                }
                Dbg.TestTrue(u.Int1 == 102);
                Dbg.TestTrue(obj.Ft1._InStr("| 102 |"));
                Dbg.TestTrue(obj.Ft1._InStr("| a001 |"));
                Dbg.TestTrue(obj.Ft1._InStr("| ntt |"));

                u.Company = "SoftEther";
                u.AuthKey = "x001";
                u.Id = "new_u1";
                u.Name = "User1New";
                u.FullName = "Neko";
                u.LastIp = "2001:af80:0000::0001";
                u.Int1++;

                obj.Ext1 = "z";
                obj.Ext2 = "99";

                var x = await tran.AtomicUpdateAsync(obj);

                Dbg.TestTrue(u.Int1 == 103);
                Dbg.TestTrue(obj.Ft1._InStr("| 103 |"));
                Dbg.TestTrue(obj.Ft1._InStr("| x001 |"));
                Dbg.TestTrue(obj.Ft1._InStr("| softether |"));

                return true;
            }, takeSnapshot: true, snapshotNoRet: snapshot2);

            // アーカイブ取得実験
            await sys1.TranAsync(false, async tran =>
            {
                var archiveList = await tran.AtomicGetArchivedAsync<User>(u1_uid, nameSpace: nameSpace);
                Dbg.TestTrue(archiveList.Count() == 2);
                Dbg.TestTrue(archiveList.ElementAt(0).SnapshotNo == snapshot2);
                Dbg.TestTrue(archiveList.ElementAt(1).SnapshotNo == snapshot0);
                return false;
            });

            Dbg.TestTrue(sys2.MemDb!.InternalData.IndexedKeysTable.Where(x => x.Key._InStri(nameSpace)).Count() == (4 * 3));
            Dbg.TestTrue(sys2.MemDb!.InternalData.IndexedLabelsTable.Where(x => x.Key._InStri(nameSpace)).Sum(x => x.Value.Count) == (2 * 3));
            Dbg.TestTrue(sys2.MemDb!.InternalData.IndexedLabelsTable.Where(x => x.Key._InStri(nameSpace)).Count() == (2 * 3));

            {
                // AtomicSearchByKeyAsync を実行した結果 sys2 のメモリも自動で更新されたかどうか検査
                var obj = sys2.FastSearchByLabels(new User { Company = "softether", LastIp = "2001:af80:0000:0000:0000:0000:0000:0001" }, nameSpace).Single();
                var u = obj.GetData();
                Dbg.TestTrue(u.FullName == "Neko");
                Dbg.TestTrue(u.Int1 == 103);
                Dbg.TestTrue(obj.Ext1 == "z");
                Dbg.TestTrue(obj.Ext2 == "99");
                Dbg.TestTrue(obj.SnapshotNo == snapshot2);
                Dbg.TestTrue(obj.Ft1._InStr("| 103 |"));
                Dbg.TestTrue(obj.Ft1._InStr("| x001 |"));
                Dbg.TestTrue(obj.Ft1._InStr("| softether |"));
            }

            Dbg.TestTrue(sys1.MemDb!.InternalData.IndexedKeysTable.Where(x => x.Key._InStri(nameSpace)).Count() == (4 * 3));
            Dbg.TestTrue(sys1.MemDb!.InternalData.IndexedLabelsTable.Where(x => x.Key._InStri(nameSpace)).Sum(x => x.Value.Count) == (2 * 3));
            Dbg.TestTrue(sys1.MemDb!.InternalData.IndexedLabelsTable.Where(x => x.Key._InStri(nameSpace)).Count() == 4);

            await sys1.ReloadCoreAsync(EnsureSpecial.Yes);

            Dbg.TestTrue(sys1.MemDb!.InternalData.IndexedKeysTable.Where(x => x.Key._InStri(nameSpace)).Count() == (4 * 3));
            Dbg.TestTrue(sys1.MemDb!.InternalData.IndexedLabelsTable.Where(x => x.Key._InStri(nameSpace)).Sum(x => x.Value.Count) == (2 * 3));
            Dbg.TestTrue(sys1.MemDb!.InternalData.IndexedLabelsTable.Where(x => x.Key._InStri(nameSpace)).Count() == (2 * 3));

            {
                await sys1.ReloadCoreAsync(EnsureSpecial.Yes);
                var obj = sys1.FastGet<User>(u1_uid, nameSpace);
                var u = obj!.GetData();
                Dbg.TestTrue(u.Id == "new_u1");
                Dbg.TestTrue(u.Name == "User1New");
                Dbg.TestTrue(u.AuthKey == "x001");
                Dbg.TestTrue(u.Company == "SoftEther");
                Dbg.TestTrue(u.LastIp == "2001:af80::1");
                Dbg.TestTrue(u.Int1 == 103);
                Dbg.TestTrue(obj.Ext1 == "z");
                Dbg.TestTrue(obj.Ext2 == "99");
                Dbg.TestTrue(obj.SnapshotNo == snapshot2);
                Dbg.TestTrue(obj.Ft1._InStr("| 103 |"));
                Dbg.TestTrue(obj.Ft1._InStr("| x001 |"));
                Dbg.TestTrue(obj.Ft1._InStr("| softether |"));

                obj.FastUpdate(x =>
                {
                    x.Int1++;
                    return true;
                });
                await sys1.LazyUpdateCoreAsync(EnsureSpecial.Yes);

                Dbg.TestTrue(obj.Ft1._InStr("| 104 |"));
                Dbg.TestTrue(obj.Ft1._InStr("| x001 |"));
                Dbg.TestTrue(obj.Ft1._InStr("| softether |"));
            }

            await sys2.ReloadCoreAsync(EnsureSpecial.Yes);

            {
                await sys2.ReloadCoreAsync(EnsureSpecial.Yes);
                var obj = sys2.FastGet<User>(u1_uid, nameSpace);
                var u = obj!.GetData();
                Dbg.TestTrue(u.Id == "new_u1");
                Dbg.TestTrue(u.Name == "User1New");
                Dbg.TestTrue(u.AuthKey == "x001");
                Dbg.TestTrue(u.Company == "SoftEther");
                Dbg.TestTrue(u.LastIp == "2001:af80::1");
                Dbg.TestTrue(u.Int1 == 104);
                Dbg.TestTrue(obj.Ext1 == "z");
                Dbg.TestTrue(obj.Ext2 == "99");
                Dbg.TestTrue(obj.SnapshotNo == snapshot2);

                Dbg.TestTrue(obj.Ft1._InStr("| 104 |"));
                Dbg.TestTrue(obj.Ft1._InStr("| x001 |"));
                Dbg.TestTrue(obj.Ft1._InStr("| softether |"));

                // 追加テスト: キー値またはラベル値の変更でエラーが発生することを確認
                obj.FastUpdate(x =>
                {
                    x.AuthKey = " X001 ";
                    return true;
                });
                obj.FastUpdate(x =>
                {
                    x.Company = "SOFTETHER";
                    return true;
                });
                Dbg.TestException(() =>
                {
                    obj.FastUpdate(x =>
                    {
                        x.AuthKey = "CAT";
                        return true;
                    });
                });
                Dbg.TestException(() =>
                {
                    obj.FastUpdate(x =>
                    {
                        x.Company = "NEKO";
                        return true;
                    });
                });

                Dbg.TestTrue(obj.Ft1._InStr("| 104 |"));
                Dbg.TestTrue(obj.Ft1._InStr("| x001 |"));
                Dbg.TestTrue(obj.Ft1._InStr("| softether |"));
            }

            await sys1.ReloadCoreAsync(EnsureSpecial.Yes);
            await sys2.ReloadCoreAsync(EnsureSpecial.Yes);

            RefLong snapshot3 = new RefLong();

            // sys2 でコミット編集 -> sys1 で高速編集 -> sys1 で遅延コミット -> sys1 を更新し sys1 の高速コミットが失われ sys2 のコミットが適用されていることを確認
            await sys2.TranAsync(true, async tran =>
            {
                var obj = await tran.AtomicSearchByKeyAsync(new User { AuthKey = "  X001  " }, nameSpace);
                var u = obj!.GetData();
                Dbg.TestTrue(u.FullName == "Neko");
                Dbg.TestTrue(u.Int1 == 104);
                obj.Ext1 = "p";
                obj.Ext2 = "1234";

                u.Int1++;

                await tran.AtomicUpdateAsync(obj);

                Dbg.TestTrue(obj.Ft1._InStr("| 105 |"));
                Dbg.TestTrue(obj.Ft1._InStr("| x001 |"));
                Dbg.TestTrue(obj.Ft1._InStr("| softether |"));

                return true;
            }, takeSnapshot: true, snapshotNoRet: snapshot3);

            // アーカイブ取得実験
            await sys1.TranAsync(false, async tran =>
            {
                var archiveList = await tran.AtomicGetArchivedAsync<User>(u1_uid, nameSpace: nameSpace);
                Dbg.TestTrue(archiveList.Count() == 3);
                Dbg.TestTrue(archiveList.ElementAt(0).SnapshotNo == snapshot3);
                Dbg.TestTrue(archiveList.ElementAt(1).SnapshotNo == snapshot2);
                Dbg.TestTrue(archiveList.ElementAt(2).SnapshotNo == snapshot0);
                return false;
            });

            {
                var obj = sys1.FastSearchByLabels<User>(new User { Company = " softETHER " }, nameSpace).Single();
                var u = obj!.GetData();
                Dbg.TestTrue(u.Id == "new_u1");
                Dbg.TestTrue(u.Name == "User1New");
                Dbg.TestTrue(u.AuthKey == "x001");
                Dbg.TestTrue(u.Company == "SoftEther");
                Dbg.TestTrue(u.LastIp == "2001:af80::1");
                Dbg.TestTrue(u.Int1 == 104);
                Dbg.TestTrue(obj.Ext1 == "z");
                Dbg.TestTrue(obj.Ext2 == "99");
                Dbg.TestTrue(obj.SnapshotNo == snapshot2);

                Dbg.TestTrue(obj.Ft1._InStr("| 104 |"));
                Dbg.TestTrue(obj.Ft1._InStr("| x001 |"));
                Dbg.TestTrue(obj.Ft1._InStr("| softether |"));

                obj.FastUpdate(x =>
                {
                    x.Int1 = 555;
                    return true;
                });

                await sys1.LazyUpdateCoreAsync(EnsureSpecial.Yes);

                Dbg.TestTrue(obj.Ft1._InStr("| 555 |"));
                Dbg.TestTrue(obj.Ft1._InStr("| x001 |"));
                Dbg.TestTrue(obj.Ft1._InStr("| softether |"));
            }

            await sys1.ReloadCoreAsync(EnsureSpecial.Yes);
            await sys2.ReloadCoreAsync(EnsureSpecial.Yes);

            {
                var obj = sys1.FastSearchByLabels<User>(new User { Company = " softETHER " }, nameSpace).Single();
                var u = obj!.GetData();
                Dbg.TestTrue(u.Int1 == 105);
                Dbg.TestTrue(obj.Ext1 == "p");
                Dbg.TestTrue(obj.Ext2 == "1234");
                Dbg.TestTrue(obj.SnapshotNo == snapshot3);

                Dbg.TestTrue(obj.Ft1._InStr("| 105 |"));
                Dbg.TestTrue(obj.Ft1._InStr("| x001 |"));
                Dbg.TestTrue(obj.Ft1._InStr("| softether |"));
            }

            {
                var obj = sys2.FastSearchByLabels<User>(new User { Company = " softETHER " }, nameSpace).Single();
                var u = obj!.GetData();
                Dbg.TestTrue(u.Int1 == 105);
                Dbg.TestTrue(obj.Ext1 == "p");
                Dbg.TestTrue(obj.Ext2 == "1234");
                Dbg.TestTrue(obj.SnapshotNo == snapshot3);

                Dbg.TestTrue(obj.Ft1._InStr("| 105 |"));
                Dbg.TestTrue(obj.Ft1._InStr("| x001 |"));
                Dbg.TestTrue(obj.Ft1._InStr("| softether |"));
            }

            // キーが重複するような更新に失敗するかどうか (メモリデータベースの検査)
            await sys1.TranAsync(true, async tran =>
            {
                var obj = await tran.AtomicSearchByKeyAsync(new User { AuthKey = "  X001  " }, nameSpace);
                var u = obj!.GetData();
                Dbg.TestTrue(u.Id == "new_u1");
                Dbg.TestTrue(u.Name == "User1New");
                Dbg.TestTrue(u.AuthKey == "x001");
                Dbg.TestTrue(u.Company == "SoftEther");
                Dbg.TestTrue(u.LastIp == "2001:af80::1");
                Dbg.TestTrue(u.Int1 == 105);
                Dbg.TestTrue(obj.Ext1 == "p");
                Dbg.TestTrue(obj.Ext2 == "1234");
                Dbg.TestTrue(obj.SnapshotNo == snapshot3);

                u.AuthKey = " A002 ";

                await Dbg.TestExceptionAsync(async () =>
                {
                    await tran.AtomicUpdateAsync(obj);
                });

                return false;
            });

            sys1.DebugFlags = sys1.DebugFlags.BitAdd(HadbDebugFlags.NoCheckMemKeyDuplicate);

            // キーが重複するような更新に失敗するかどうか (物理データベースの検査)
            await sys1.TranAsync(true, async tran =>
            {
                var obj = await tran.AtomicSearchByKeyAsync(new User { AuthKey = "  X001  " }, nameSpace);
                var u = obj!.GetData();
                Dbg.TestTrue(u.Id == "new_u1");
                Dbg.TestTrue(u.Name == "User1New");
                Dbg.TestTrue(u.AuthKey == "x001");
                Dbg.TestTrue(u.Company == "SoftEther");
                Dbg.TestTrue(u.LastIp == "2001:af80::1");
                Dbg.TestTrue(u.Int1 == 105);
                Dbg.TestTrue(obj.Ext1 == "p");
                Dbg.TestTrue(obj.Ext2 == "1234");
                Dbg.TestTrue(obj.SnapshotNo == snapshot3);

                u.AuthKey = " A002 ";

                await Dbg.TestExceptionAsync(async () =>
                {
                    await tran.AtomicUpdateAsync(obj);
                });

                return false;
            });


            sys1.DebugFlags = sys1.DebugFlags.BitRemove(HadbDebugFlags.NoCheckMemKeyDuplicate);



            // キーが重複するような追加に失敗するかどうか (メモリデータベースの検査)
            await sys1.TranAsync(true, async tran =>
            {
                User u = new User() { Id = "u2", Name = "User2", AuthKey = "zcas", Company = "NTT", LastIp = "af80:b456:0001::c789", FullName = "Yamada", Int1 = 200 };
                await Dbg.TestExceptionAsync(async () =>
                {
                    var obj = await tran.AtomicAddAsync(u, nameSpace, "e", "22");
                });
                return false;
            });

            sys1.DebugFlags = sys1.DebugFlags.BitAdd(HadbDebugFlags.NoCheckMemKeyDuplicate);

            // キーが重複するような追加に失敗するかどうか (物理データベースの検査)
            await sys1.TranAsync(true, async tran =>
            {
                User u = new User() { Id = "u2", Name = "User2", AuthKey = "zcas", Company = "NTT", LastIp = "af80:b456:0001::c789", FullName = "Yamada", Int1 = 200 };
                await Dbg.TestExceptionAsync(async () =>
                {
                    var obj = await tran.AtomicAddAsync(u, nameSpace, "e", "22");
                });
                return false;
            });

            sys1.DebugFlags = sys1.DebugFlags.BitRemove(HadbDebugFlags.NoCheckMemKeyDuplicate);


            await sys1.ReloadCoreAsync(EnsureSpecial.Yes);
            await sys2.ReloadCoreAsync(EnsureSpecial.Yes);


            // sys1 のメモリ検索上まだいるか
            {
                var obj = sys1.FastSearchByLabels<User>(new User { Company = " softETHER " }, nameSpace).SingleOrDefault();
                Dbg.TestNotNull(obj);
                Dbg.TestFalse(obj!.Deleted);
                obj = sys1.FastGet<User>(u1_uid, nameSpace);
                Dbg.TestNotNull(obj);
                Dbg.TestFalse(obj!.Deleted);
            };

            // sys1 の DB 検索上まだいるか
            await sys1.TranAsync(false, async tran =>
            {
                var obj = await tran.AtomicGetAsync<User>(u1_uid, nameSpace);
                Dbg.TestNotNull(obj);
                Dbg.TestFalse(obj!.Deleted);
                obj = (await tran.AtomicSearchByLabelsAsync<User>(new User { Company = " softETHER " }, nameSpace)).SingleOrDefault();
                Dbg.TestNotNull(obj);
                Dbg.TestFalse(obj!.Deleted);
                return false;
            });



            RefLong snapshot4 = new RefLong();

            // sys1 で削除コミットし、sys2 に反映されるかどうか
            await sys1.TranAsync(true, async tran =>
            {
                await tran.AtomicDeleteByKeyAsync(new User { AuthKey = "X001" }, nameSpace);
                return true;
            }, takeSnapshot: true, snapshotNoRet: snapshot4);

            // アーカイブ取得実験
            await sys2.TranAsync(false, async tran =>
            {
                var archiveList = await tran.AtomicGetArchivedAsync<User>(u1_uid, nameSpace: nameSpace);
                Dbg.TestTrue(archiveList.Count() == 4);
                Dbg.TestTrue(archiveList.ElementAt(0).SnapshotNo == snapshot4);
                Dbg.TestTrue(archiveList.ElementAt(1).SnapshotNo == snapshot3);
                Dbg.TestTrue(archiveList.ElementAt(2).SnapshotNo == snapshot2);
                Dbg.TestTrue(archiveList.ElementAt(3).SnapshotNo == snapshot0);
                return false;
            });

            // sys1 のメモリ検索上なくなったか
            {
                var obj = sys1.FastSearchByLabels<User>(new User { Company = " softETHER " }, nameSpace).SingleOrDefault();
                Dbg.TestNull(obj);
                obj = sys1.FastGet<User>(u1_uid, nameSpace);
                Dbg.TestNull(obj);
            }

            // sys1 の DB 検索上なくなったか
            await sys1.TranAsync(false, async tran =>
            {
                var obj = await tran.AtomicGetAsync<User>(u1_uid, nameSpace);
                Dbg.TestNull(obj);
                obj = (await tran.AtomicSearchByLabelsAsync<User>(new User { Company = " softETHER " }, nameSpace)).SingleOrDefault();
                Dbg.TestNull(obj);
                return false;
            });


            // sys2 のメモリ検索上まだいるか
            {
                var obj = sys2.FastSearchByLabels<User>(new User { Company = " softETHER " }, nameSpace).SingleOrDefault();
                Dbg.TestNotNull(obj);
                Dbg.TestFalse(obj!.Deleted);
                obj = sys2.FastGet<User>(u1_uid, nameSpace);
                Dbg.TestNotNull(obj);
                Dbg.TestFalse(obj!.Deleted);
            };

            await sys2.ReloadCoreAsync(EnsureSpecial.Yes, fullReloadMode: false);

            // sys2 のメモリ上からいなくなったか
            {
                var obj = sys2.FastSearchByLabels<User>(new User { Company = " softETHER " }, nameSpace).SingleOrDefault();
                Dbg.TestNull(obj);
                obj = sys2.FastGet<User>(u1_uid, nameSpace);
                Dbg.TestNull(obj);
            };

            await sys2.ReloadCoreAsync(EnsureSpecial.Yes, fullReloadMode: false);



            if (backupTest)
            {
                // ローカルバックアップ JSON データからデータベースに書き戻しをする実験

                // まずデータを消す
                await sys2.TranAsync(true, async tran =>
                {
                    var db = (tran as HadbSqlBase<Mem, Dyn>.HadbSqlTran)!.Db;

                    await db.QueryWithNoReturnAsync("delete from HADB_DATA where DATA_SYSTEMNAME = @", sys2.SystemName);

                    return true;
                });

                // 復活させる
                await sys2.RestoreDataFromHadbObjectListAsync(sys2.Settings.BackupDataFile);

                // 書き戻し後にデータが復活していることを確認
                {
                    await sys1.ReloadCoreAsync(EnsureSpecial.Yes);
                    var obj = sys1.FastSearchByKey<User>(new HadbKeys("", "", "A003"), nameSpace);
                    var u = obj!.GetData();
                    Dbg.TestTrue(u.Id == "u3");
                    Dbg.TestTrue(u.Name == "User3");
                    Dbg.TestTrue(u.AuthKey == "a003");
                    Dbg.TestTrue(u.Company == "IPA");
                    Dbg.TestTrue(u.LastIp == "a123:b456:1::c789");
                    Dbg.TestTrue(obj.Ext1 == "c");
                    Dbg.TestTrue(obj.Ext2 == "3");
                }
            }
        }
    }
}


#endif

