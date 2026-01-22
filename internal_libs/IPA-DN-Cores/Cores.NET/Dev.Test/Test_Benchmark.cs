// IPA Cores.NET
// 
// Copyright (c) 2018- IPA CyberLab.
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

using System;
using System.IO;
using System.IO.Enumeration;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Collections;

using IPA.Cores.Basic.DnsLib;
using System.Net;

#pragma warning disable CS0162
#pragma warning disable CS0219
#pragma warning disable CS1998
#pragma warning disable CS0414

namespace IPA.TestDev;

public class HadbBenchTest
{
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
            return ret;
        }
    }

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
}

static class BenchmarkTestTarget1
{
    public static readonly CriticalSection LockObj_ReadOnly = new CriticalSection();
    public static CriticalSection LockObj_Property { get; } = new CriticalSection();

    public static readonly int Int_ReadOnly = 123;
    public static int Int_GetOnly { get; private set; } = 123;
    public static readonly Copenhagen<int> Int_Copenhagen_ReadOnly = 123;
    public static Copenhagen<int> Int_Copenhagen_GetOnly { get; private set; } = 123;

    public static readonly string String_ReadOnly = "Hello";
    public static string String_GetOnly { get; private set; } = "Hello";
    public static readonly Copenhagen<string> String_Copenhagen_ReadOnly = "Hello";
    public static Copenhagen<string> String_Copenhagen_GetOnly { get; private set; } = "Hello";

    public static async Task<int> SampleAsyncMethod()
    {
        Limbo.SInt32Volatile++;

        return Limbo.SInt32Volatile;
    }

    public static async Task EmptyAsyncMethod() { }

    public static async Task AsyncCallMethodFromAsyncMethod(int count)
    {
        for (int i = 0; i < count; i++)
            await SampleAsyncMethod();
    }

    public static void AsyncCallMethodFromSyncMethod(int count)
    {
        for (int i = 0; i < count; i++)
            SampleAsyncMethod()._GetResult();
    }

    public static async Task<int> SimpleAsyncMethod()
    {
        return Limbo.SInt32Volatile;
    }

    public static async Task<int> CallAsyncWithAwait()
    {
        return await SimpleAsyncMethod();
    }

    public static Task<int> CallAsyncWithNonAwait()
    {
        return SimpleAsyncMethod();
    }

    public static async Task CallAsyncWithAwaitLoop(int count)
    {
        for (int i = 0; i < count; i++)
            await CallAsyncWithAwait();
    }

    public static async Task CallAsyncWithNonAwaitLoop(int count)
    {
        for (int i = 0; i < count; i++)
            await SimpleAsyncMethod();
    }
}

class TestClass1
{
    public int A, B, C;
}

class TestClass2<TKey> where TKey : unmanaged, Enum
{
    public long GetValue(TKey src)
    {
        return src.GetHashCode();
    }
}


public unsafe struct TestSt2
{
    public fixed byte Data[8];

    public TestSt2(string str)
    {
        str = str + "_____";
        byte[] src = str._GetBytes_UTF8();

        fixed (byte* p = Data)
        {
            Util.CopyByte(p, src.AsSpan().Slice(0, 5));
        }
    }
}

public readonly struct TestSt3 : IEquatable<TestSt3>
{
    public readonly ulong Data1;
    public readonly int Hash;

    public TestSt3(string str)
    {
        str = str + "_________";
        byte[] src = str._GetBytes_UTF8();

        ReadOnlySpan<byte> span = src;
        this.Data1 = span._RawReadValueUInt64();

        this.Hash = this.Data1._HashMarvin();
    }

    public override int GetHashCode()
    {
        return this.Hash;
    }

    public bool Equals(TestSt3 other)
    {
        return this.Data1 == other.Data1;
    }
}

public class TestSt1 : IEquatable<TestSt1>
{
    public readonly ReadOnlyMemory<byte> Data;

    public TestSt1(ReadOnlyMemory<byte> data)
    {
        this.Data = data;
    }

    public bool Equals(TestSt1? other)
    {
        return Data._MemEquals(other!.Data);
    }

    public override int GetHashCode()
    {
        return Data._HashMarvin();
    }
}

public struct TestSt4
{
    public int IntValue;
    public long v1;
    public long v2;
    public long v3;
    public long v4;
}

public class TestSt5 : IEquatable<TestSt5>, IComparable<TestSt5>
{
    public int IntValue;
    public long v1;
    public long v2;
    public long v3;
    public long v4;

    public int CompareTo(TestSt5? other)
    {
        int r;
        if ((r = this.v1.CompareTo(other!.v1)) != 0) return r;
        if ((r = this.v2.CompareTo(other.v2)) != 0) return r;
        if ((r = this.v3.CompareTo(other.v3)) != 0) return r;
        if ((r = this.v4.CompareTo(other.v4)) != 0) return r;
        if ((r = this.IntValue.CompareTo(other.IntValue)) != 0) return r;
        return 0;
    }

    public bool Equals(TestSt5? other)
    {
        return
            this.v1.Equals(other!.v1) &&
            this.v2.Equals(other.v2) &&
            this.v3.Equals(other.v3) &&
            this.v4.Equals(other.v4) &&
            this.IntValue.Equals(other.IntValue);
    }

    public override bool Equals(object? obj)
    {
        return Equals((TestSt5)obj!);
    }

    public override int GetHashCode()
    {
        return System.HashCode.Combine(
            IntValue,
            v1,
            v2,
            v3,
            v4);
    }

    public override string ToString()
    {
        return this.IntValue.ToString();
    }
}

class BMTestClass1<T>
{
    public static bool IsByte()
    {
        return GenericInfo<T>.IsByte;
    }
}

public class BMTest_SimpleAsyncService : AsyncService
{
    public Task TestAsync() => TR();

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
}

public class BMTest_SimpleAsyncService2 : IDisposable, IAsyncDisposable
{
    static long IdSeed = 0;
    public long AsyncServiceId { get; }
    public string AsyncServiceObjectName { get; }
    public CancelWatcher? CancelWatcher { get; } = null;

    public BMTest_SimpleAsyncService2(CancellationToken cancel = default)
    {
        this.AsyncServiceId = Interlocked.Increment(ref IdSeed);
        this.AsyncServiceObjectName = this.ToString() ?? "null";

        this.CancelWatcher = new CancelWatcher(cancel);

    }

    public Task TestAsync() => TR();

    public void Dispose() { this.Dispose(true); GC.SuppressFinalize(this); }
    Once DisposeFlag;
    public async ValueTask DisposeAsync()
    {
        if (DisposeFlag.IsFirstCall() == false) return;
        await DisposeInternalAsync();
    }
    protected virtual void Dispose(bool disposing)
    {
        if (!disposing || DisposeFlag.IsFirstCall() == false) return;
        DisposeInternalAsync()._GetResult();
    }
    async Task DisposeInternalAsync()
    {
        // Here
        this.CancelWatcher?._DisposeSafe();
    }
}


public static class BmTest_DeepClone
{
    [Serializable]
    public class ElementClass
    {
        public int Int1;
        public string? Str1;

        public RootClass? Root;
    }

    [Serializable]
    public class RootClass
    {
        public string? Str1;
        public int Int1;

        public Dictionary<int, ElementClass>? Dict;
    }

    public static RootClass CreateSampleObject()
    {
        RootClass a = new RootClass();

        a.Int1 = Util.RandSInt31();
        a.Str1 = a.Int1.ToString();

        a.Dict = new Dictionary<int, ElementClass>();

        for (int i = 0; i < 1000; i++)
        {
            ElementClass e = new ElementClass();
            e.Int1 = Util.RandSInt31();
            e.Str1 = e.Int1.ToString();

            e.Root = a;

            a.Dict.Add(i, e);
        }

        return a;
    }
}

partial class TestDevCommands
{
    public class BM_HadbTestData : HadbData
    {
        public int Abc;

        public override void Normalize()
        {
        }
    }

    const int Benchmark_CountForVeryFast = 200000000;
    const int Benchmark_CountForFast = 10000000;
    const int Benchmark_CountForNormal = 10000;
    const int Benchmark_CountForSlow = 100;
    const int Benchmark_CountForVerySlow = 10;

    static void BenchMask_BoostUp_PacketParser(string name)
    {
        var packetMem = Res.AppRoot[name].HexParsedBinary;

        Packet packet = new Packet(default, packetMem._CloneSpan());

        for (int c = 0; c < 100; c++)
        {
            new PacketParsed(ref packet);
        }
    }

    static volatile string TestString1 = "Hello World Nekosan";
    static volatile string TestString2 = "hello world nekosan";
    static volatile string TestString3 = "    aaaa    ";
    static volatile string TestString4 = "            ";

    [MethodImpl(NoInline)]
    static async Task TestFunc1Async(int r)
    {
        for (int i = 0; i < r; i++)
        {
            Limbo.SInt32Volatile += i;
        }
    }

    [MethodImpl(NoInline)]
    static async Task TestFunc2Async(int r)
    {
        for (int i = 0; i < r; i++)
        {
            Limbo.SInt32Volatile += i;
        }
        await Task.CompletedTask;
    }

    [MethodImpl(NoInline)]
    static async Task TestFunc3Async(int r)
    {
        await Task.CompletedTask;
        for (int i = 0; i < r; i++)
        {
            Limbo.SInt32Volatile += i;
        }
    }

    [MethodImpl(NoInline)]
    static async Task TestFunc4Async(int r)
    {
        for (int i = 0; i < r; i++)
        {
            Limbo.SInt32Volatile += i;
            await Task.CompletedTask;
        }
    }

    [MethodImpl(NoInline)]
    static async Task TestFunc5Async(int r)
    {
        for (int i = 0; i < r; i++)
        {
            Limbo.SInt32Volatile += i;
            await ValueTask.CompletedTask;
        }
    }


    static void BenchMark_Test1()
    {
        using (var proc = Process.GetCurrentProcess())
        {
            try
            {
                proc.PriorityClass = ProcessPriorityClass.High;
                proc.PriorityClass = ProcessPriorityClass.RealTime;
            }
            catch
            {
                Con.WriteDebug("Failed to set the process realtime priority.");
            }
        }

        MemoryBuffer<byte> sparseTest1 = new MemoryBuffer<byte>();
        for (int i = 0; i < 10; i++)
        {
            sparseTest1.Write("Hello World"._GetBytes_Ascii());
            sparseTest1.WriteZero(1_000_000);
            sparseTest1.Write("Hello World2"._GetBytes_Ascii());
        }

        int memcopyLength = 10_000_000;
        MemoryBuffer<byte> memcopySrc = new MemoryBuffer<byte>(memcopyLength);
        MemoryBuffer<byte> memcopyDst = new MemoryBuffer<byte>(memcopyLength);
        Span<byte> memcopySrcSpan = memcopySrc.Span;
        for (int i = 0; i < memcopyLength; i++)
        {
            memcopySrcSpan[i] = (byte)i;
        }
        byte[] byteArrayCopy = memcopySrcSpan.ToArray();
        byte[] byteArrayCopy2 = memcopySrcSpan.ToArray();

        int sparseTestSize = 1024 * 1024;

        MemoryBuffer<byte> nonSparse = new byte[sparseTestSize];
        Util.Rand(nonSparse.Span);

        MemoryBuffer<byte> sparse = new byte[sparseTestSize];
        Util.Rand(sparse.Span);
        for (int i = 0; i < 100; i++)
        {
            int pos = Util.RandSInt31() % (sparse.Length - 6000);
            sparse.Span.Slice(pos, 6000).Fill(0);
        }

        MemoryBuffer<byte> allZeroSparse = new byte[sparseTestSize];

        int isZeroTestSize = 4;

        FileStream nativeFile1 = new FileStream(Lfs.PathParser.Combine(Env.MyLocalTempDir, $"native_{Str.NewGuid()}.dat"), FileMode.Create, FileAccess.ReadWrite, FileShare.ReadWrite, 4096, false);
        FileStream nativeFile2 = new FileStream(Lfs.PathParser.Combine(Env.MyLocalTempDir, $"native_{Str.NewGuid()}.dat"), FileMode.Create, FileAccess.ReadWrite, FileShare.ReadWrite, 4096, false);

        FileObject coresFile1 = Lfs.Create(Lfs.PathParser.Combine(Env.MyLocalTempDir, $"native_{Str.NewGuid()}.dat"), flags: FileFlags.None);
        FileObject coresFile2 = Lfs.Create(Lfs.PathParser.Combine(Env.MyLocalTempDir, $"native_{Str.NewGuid()}.dat"), flags: FileFlags.None);

        Memory<byte> testFileWriteData = new byte[4096];
        Util.Rand(testFileWriteData.Span);

        Dictionary<string, int> testDic1 = new Dictionary<string, int>();
        for (int i = 0; i < 65536; i++)
        {
            testDic1.Add("Tes" + i.ToString(), i);
        }

        Dictionary<long, int> testDic2 = new Dictionary<long, int>();
        for (int i = 0; i < 65536; i++)
        {
            testDic2.Add(i, i);
        }

        Dictionary<ReadOnlyMemory<byte>, int> testDic3 = new Dictionary<ReadOnlyMemory<byte>, int>(MemoryComparers.ReadOnlyMemoryComparer);
        for (int i = 0; i < 65536; i++)
        {
            MemoryBuffer<byte> buf = new MemoryBuffer<byte>();
            buf.WriteSInt32(i);
            //var mem = buf.Memory;
            //mem._RawWriteValueSInt32(i);
            testDic3.Add(buf.Slice(0, 4).Clone(), i);
        }

        Dictionary<TestSt1, int> testDic4 = new Dictionary<TestSt1, int>();
        for (int i = 0; i < 65536; i++)
        {
            MemoryBuffer<byte> buf = new MemoryBuffer<byte>();
            buf.WriteSInt32(i);
            //var mem = buf.Memory;
            //mem._RawWriteValueSInt32(i);
            testDic4.Add(new TestSt1(buf.Memory), i);
        }

        Dictionary<byte[], int> testDic5 = new Dictionary<byte[], int>(MemoryComparers.ArrayComparer);
        for (int i = 0; i < 65536; i++)
        {

            testDic5.Add(("Test_" + i.ToString())._GetBytes_UTF8(), i);
        }

        Dictionary<TestSt2, int> testDic6 = new Dictionary<TestSt2, int>(StructComparers<TestSt2>.StructBitComparer);
        for (int i = 0; i < 65536; i++)
        {
            string str = i.ToString();
            testDic6.Add(new TestSt2(str), i);
        }

        Dictionary<TestSt3, int> testDic7 = new Dictionary<TestSt3, int>();
        for (int i = 0; i < 65536; i++)
        {
            string str = i.ToString();
            testDic7.Add(new TestSt3(str), i);
        }

        Dictionary<BitStructKey<TestSt4>, int> testDic8 = new Dictionary<BitStructKey<TestSt4>, int>();
        for (int i = 0; i < 65536; i++)
        {
            testDic8.Add(new BitStructKey<TestSt4>(new TestSt4 { IntValue = i }), i);
        }

        Dictionary<TestSt5, int> testDic9 = new Dictionary<TestSt5, int>();
        for (int i = 0; i < 65536; i++)
        {
            testDic9.Add(new TestSt5 { IntValue = i }, i);
        }

        BM_HadbTestData hadbTestData = new BM_HadbTestData { Abc = 123 };
        HadbObject<BM_HadbTestData> hadbTestObj = new HadbObject(hadbTestData, 0, "abc", false, HadbOptionFlags.None);

        BenchMask_BoostUp_PacketParser("190527_novlan_simple_udp");
        BenchMask_BoostUp_PacketParser("190527_novlan_simple_tcp");
        BenchMask_BoostUp_PacketParser("190527_vlan_simple_udp");
        BenchMask_BoostUp_PacketParser("190527_vlan_simple_tcp");
        BenchMask_BoostUp_PacketParser("190531_vlan_pppoe_l2tp_tcp");
        BenchMask_BoostUp_PacketParser("190531_vlan_pppoe_l2tp_udp");
        BenchMask_BoostUp_PacketParser("210613_novlan_dns_query_simple");

        Memory<byte> allZeroTest = new byte[4000];

        string aclStr = "192.168.3.0/24, 192.168.4.0/24, 2001:c90::/32, !192.168.5.0/24, 10.0.0.0/8, 172.16.0.0/12";
        var aclSampleIp = "192.168.5.0"._ToIPAddress()!;
        var cloneDeepSampleObj = BmTest_DeepClone.CreateSampleObject();
        HadbTestData cloneDeepSampleObj2 = new HadbTestData() { HostName = "abc", IPv4Address = "123", IPv6Address = "456", TestInt = 789 };


        //string databaseBackupTmpFilePath = Dbg.DownloadWebFileLocalCache("http://lts.dn.ipantt.net/d/211230_001_86373/Database.json", 6044349);


        //HadbSqlSettings hadbBenchSettings = new HadbSqlSettings("_Dummy",
        //    new SqlDatabaseConnectionSetting("", "", "", "", true),
        //    new SqlDatabaseConnectionSetting("", "", "", "", true),
        //    optionFlags: HadbOptionFlags.NoAutoDbReloadAndUpdate, backupDataFile: databaseBackupTmpFilePath);


        //using HadbBenchTest.Sys hadbBenchSys = new HadbBenchTest.Sys(hadbBenchSettings, new HadbBenchTest.Dyn());
        //hadbBenchSys.DebugFlags |= HadbDebugFlags.NoDbLoadAndUseOnlyLocalBackup;
        //hadbBenchSys.WaitUntilReadyForFastAsync()._GetResult();


        ImmutableDictionary<string, string> immDictTest = ImmutableDictionary<string, string>.Empty.WithComparers(StrCmpi);

        int immDictCount = 10000;

        for (int i = 0; i < immDictCount; i++)
        {
            immDictTest = immDictTest.Add("Key1" + ":" + "User" + ":" + "DEFAULT_NS" + ":" + i.ToString(), i.ToString());
        }

        string testStr = "こんにちはabcABCａｂｃＡＢＣ123１２３こんにちは";

        SeedBasedRandomGenerator seedRand1 = new SeedBasedRandomGenerator("HelloWorld");
        ReadOnlyMemory<byte> rand1mb = seedRand1.GetBytes(1_000_000);
        var randCompressedFastest = rand1mb._EasyCompress(System.IO.Compression.CompressionLevel.Fastest);
        var randCompressedOptimal = rand1mb._EasyCompress(System.IO.Compression.CompressionLevel.Optimal);
        var randCompressedSmallestSize = rand1mb._EasyCompress(System.IO.Compression.CompressionLevel.SmallestSize);


        MikakaDDnsService.Host ddnsHost = new MikakaDDnsService.Host()
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

        FullRoute46<string> fullRouteTest = new FullRoute46<string>();

        SeedBasedRandomGenerator seedRand = new SeedBasedRandomGenerator("Hello");
        for (int i = 0; i < 10000; i++)
        {
            var addr = IPAddress.Parse($"{seedRand.GetSInt31() % 256}.{seedRand.GetSInt31() % 256}.{seedRand.GetSInt31() % 256}.0");
            int subnet = seedRand.GetSInt31() % 24 + 8;
            fullRouteTest.Insert(addr, subnet, "test");
        }

        fullRouteTest.Insert(IPAddress.Parse("0.0.0.0"), 0, "test");

        fullRouteTest.Lookup(IPAddress.Parse("12.34.56.78"), out _, out _);

        IPAddress[] ipList = new IPAddress[256];

        for (int i = 0; i < 256; i++)
        {
            ipList[i] = IPAddress.Parse($"{seedRand.GetSInt31() % 256}.{seedRand.GetSInt31() % 256}.{seedRand.GetSInt31() % 256}.{seedRand.GetSInt31() % 256}");
        }

        var queue = new MicroBenchmarkQueue()

        .Add(new MicroBenchmark($"Sample Loop (Do Nothing)", Benchmark_CountForVeryFast, count =>
        {
            Async(async () =>
            {
                for (int c = 0; c < count; c++)
                {
                    Limbo.SInt32Volatile++;
                }
            });
        }), enabled: true, priority: 999999)

        .Add(new MicroBenchmark($"DNS Packet Clone", Benchmark_CountForNormal, count =>
        {
            var packetMem = Res.AppRoot["210613_novlan_dns_query_simple.txt"].HexParsedBinary;
            Packet packet = new Packet(default, packetMem._CloneSpan());
            var parsed = new PacketParsed(ref packet);
            var dnsPacket = parsed.L7.Generic.GetSpan(ref packet);

            var array = dnsPacket.ToArray().AsSpan();

            var msg = DnsUtil.ParsePacket(array);

            for (int c = 0; c < count; c++)
            {
                msg._CloneDeep();
            }
        }), enabled: true, priority: 221229)

        .Add(new MicroBenchmark($"Wildcard Match", Benchmark_CountForNormal, count =>
        {
            Async(async () =>
            {
                for (int c = 0; c < count; c++)
                {
                    Str.WildcardMatch("abc-12345-6789.example.org", "abc-12345-*.example.org");
                }
            });
        }), enabled: true, priority: 221229)


        .Add(new MicroBenchmark($"Radix Trie", Benchmark_CountForNormal, count =>
        {
            Async(async () =>
            {
                for (int c = 0; c < count; c++)
                {
                    var ip = ipList[c % 256];
                    if (fullRouteTest.Lookup(ip, out _, out _) == null)
                    {
                        throw new CoresException();
                    }
                }
            });
        }), enabled: true, priority: 221229)


        .Add(new MicroBenchmark($"ByteToHex (New) - No Padding", Benchmark_CountForNormal, count =>
        {
            byte[] src = new byte[16];
            for (int i = 0; i < src.Length; i++)
            {
                src[i] = (byte)Util.ComputeGoldenHash32(i + 1);
            }
            Async(async () =>
            {
                for (int c = 0; c < count; c++)
                {
                    Str.ByteToHex(src);
                }
            });
        }), enabled: true, priority: 221229)

        .Add(new MicroBenchmark($"ByteToHex (New) - Padding", Benchmark_CountForNormal, count =>
        {
            byte[] src = new byte[16];
            for (int i = 0; i < src.Length; i++)
            {
                src[i] = (byte)Util.ComputeGoldenHash32(i + 1);
            }
            Async(async () =>
            {
                for (int c = 0; c < count; c++)
                {
                    Str.ByteToHex(src, " ");
                }
            });
        }), enabled: true, priority: 221229)

        .Add(new MicroBenchmark($"throw Exception", Benchmark_CountForNormal, count =>
        {
            Async(async () =>
            {
                var test1 = new byte[384];
                for (int c = 0; c < count; c++)
                {
                    try
                    {
                        test1[Limbo.SInt32Volatile + 12345] = (byte)Limbo.UInt32Volatile;
                    }
                    catch
                    {
                    }
                }
            });
        }), enabled: true, priority: 211227)

        .Add(new MicroBenchmark($"StartAsyncTaskAsync Normal (TaskResult)", Benchmark_CountForSlow, count =>
        {
            Async(async () =>
            {
                for (int c = 0; c < count; c++)
                {
                    Task t = TaskUtil.StartAsyncTaskAsync(() =>
                    {
                        return TR(Limbo.SInt32);
                    });

                    //await t;
                }
            });
        }), enabled: true, priority: 211227)

        .Add(new MicroBenchmark($"StartAsyncTaskAsync Normal (async)", Benchmark_CountForSlow, count =>
        {
            Async(async () =>
            {
                for (int c = 0; c < count; c++)
                {
                    Task t = TaskUtil.StartAsyncTaskAsync(async () =>
                    {
                        return Limbo.SInt32;
                    });

                    await t;
                }
            });
        }), enabled: true, priority: 211227)

        .Add(new MicroBenchmark($"StartAsyncTaskAsync with Task Param", Benchmark_CountForSlow, count =>
        {
            Async(async () =>
            {
                for (int c = 0; c < count; c++)
                {
                    Task t = TaskUtil.StartAsyncTaskAsync(task =>
                    {
                        return TR(0);
                    });

                    await t;
                    //t._GetResult();
                }
            });
        }), enabled: true, priority: 211227)

        .Add(new MicroBenchmark($"DeflateStream compress 1MB Optimal", Benchmark_CountForVerySlow, count =>
        {
            Async(async () =>
            {
                for (int c = 0; c < count; c++)
                {
                    Limbo.ObjectSlow = rand1mb._EasyCompress(System.IO.Compression.CompressionLevel.Optimal);
                }
            });
        }), enabled: true, priority: 211227)

        .Add(new MicroBenchmark($"DeflateStream compress 1MB Fastest", Benchmark_CountForVerySlow, count =>
        {
            Async(async () =>
            {
                for (int c = 0; c < count; c++)
                {
                    Limbo.ObjectSlow = rand1mb._EasyCompress(System.IO.Compression.CompressionLevel.Fastest);
                }
            });
        }), enabled: true, priority: 211227)

        .Add(new MicroBenchmark($"DeflateStream compress 1MB SmallestSize", Benchmark_CountForVerySlow, count =>
        {
            Async(async () =>
            {
                for (int c = 0; c < count; c++)
                {
                    Limbo.ObjectSlow = rand1mb._EasyCompress(System.IO.Compression.CompressionLevel.SmallestSize);
                }
            });
        }), enabled: true, priority: 211227)

        .Add(new MicroBenchmark($"DeflateStream decompress 1MB Optimal", Benchmark_CountForVerySlow, count =>
        {
            Async(async () =>
            {
                for (int c = 0; c < count; c++)
                {
                    Limbo.ObjectSlow = randCompressedOptimal._EasyDecompress();
                }
            });
        }), enabled: true, priority: 211227)

        .Add(new MicroBenchmark($"DeflateStream decompress 1MB Fastest", Benchmark_CountForVerySlow, count =>
        {
            Async(async () =>
            {
                for (int c = 0; c < count; c++)
                {
                    Limbo.ObjectSlow = randCompressedFastest._EasyDecompress();
                }
            });
        }), enabled: true, priority: 211227)

        .Add(new MicroBenchmark($"DeflateStream decompress 1MB SmallestSize", Benchmark_CountForVerySlow, count =>
        {
            Async(async () =>
            {
                for (int c = 0; c < count; c++)
                {
                    Limbo.ObjectSlow = randCompressedSmallestSize._EasyDecompress();
                }
            });
        }), enabled: true, priority: 211227)


        // メモ
        // ToUpperInvariant() が一番速い！？
        // Str ToUpper(): 149.54 ns, 6,686,980 / sec
        // Str ToUpperInvariant(): 120.73 ns, 8,282,892 / sec
        // Str ToLower(): 128.30 ns, 7,794,358 / sec
        // Str ToLowerInvariant(): 152.64 ns, 6,551,327 / sec

        //.Add(new MicroBenchmark($"Str ToUpper()", Benchmark_CountForFast, count =>
        //{
        //    Async(async () =>
        //    {
        //        for (int c = 0; c < count; c++)
        //        {
        //            Limbo.ObjectSlow = testStr.ToUpper();
        //        }
        //    });
        //}), enabled: true, priority: 211227)

        //.Add(new MicroBenchmark($"Str ToUpperInvariant()", Benchmark_CountForFast, count =>
        //{
        //    Async(async () =>
        //    {
        //        for (int c = 0; c < count; c++)
        //        {
        //            Limbo.ObjectSlow = testStr.ToUpperInvariant();
        //        }
        //    });
        //}), enabled: true, priority: 211227)

        //.Add(new MicroBenchmark($"Str ToLower()", Benchmark_CountForFast, count =>
        //{
        //    Async(async () =>
        //    {
        //        for (int c = 0; c < count; c++)
        //        {
        //            Limbo.ObjectSlow = testStr.ToLower();
        //        }
        //    });
        //}), enabled: true, priority: 211227)

        //.Add(new MicroBenchmark($"Str ToLowerInvariant()", Benchmark_CountForFast, count =>
        //{
        //    Async(async () =>
        //    {
        //        for (int c = 0; c < count; c++)
        //        {
        //            Limbo.ObjectSlow = testStr.ToLowerInvariant();
        //        }
        //    });
        //}), enabled: true, priority: 211227)


        .Add(new MicroBenchmark($"ImmutableDictionary Search " + immDictCount, immDictCount, count =>
        {
            // メモ
            // ImmutableDictionary Search 10000: 176.78 ns, 5,656,631 / sec
            // ImmutableDictionary Search 100000: 401.02 ns, 2,493,644 / sec
            // ImmutableDictionary Search 1000000: 880.13 ns, 1,136,194 / sec
            // ImmutableDictionary Search 10000000: 1,021.03 ns, 979,400 / sec
            Async(async () =>
            {
                for (int c = 0; c < immDictCount; c++)
                {
                    int index = c;
                    immDictTest.TryGetValue(index.ToString() + ":" + "Key1" + ":" + "User" + ":" + "DEFAULT_NS", out string? x);
                    Limbo.ObjectSlow = x;
                }
            });
        }), enabled: true, priority: 211227)

        //.Add(new MicroBenchmark($"HADB FastSearch", Benchmark_CountForNormal * 10, count =>
        //{
        //    // メモ
        //    // HADB FastSearch: 378.18 ns, 2,644,233 / sec (Direct Search)
        //    // HADB FastSearch: 547.06 ns, 1,827,967 / sec (Generics Search)
        //    Async(async () =>
        //    {
        //        for (int c = 0; c < count; c++)
        //        {
        //            int index = count % 10000;
        //            var obj = hadbBenchSys.FastSearchByKey(new HadbKeys(index.ToString()), "User");
        //            //obj._NullCheck();
        //            //var obj = hadbBenchSys.FastSearchByKey(new HadbBenchTest.User { AuthKey = index.ToString() });
        //        }
        //    });
        //}), enabled: true, priority: 211227)

        //.Add(new MicroBenchmark($"HADB FastSearch 2", Benchmark_CountForNormal * 10, count =>
        //{
        //    // メモ
        //    // HADB FastSearch 2: 216.59 ns, 4,616,981 / sec
        //    Async(async () =>
        //    {
        //        for (int c = 0; c < count; c++)
        //        {
        //            int index = count % 10000;
        //            var keys = new HadbKeys(index.ToString());
        //            if (keys.Key1._IsFilled())
        //            {
        //                hadbBenchSys.MemDb!.InternalData.IndexedKeysTable.TryGetValue("Key1" + ":" + "User" + ":" + "DEFAULT_NS" + ":" + keys.Key1, out HadbObject? ret);
        //                Limbo.ObjectSlow = ret;
        //                //var obj = hadbBenchSys.FastSearchByKey(new HadbBenchTest.User { AuthKey = index.ToString() });
        //            }
        //        }
        //    });
        //}), enabled: true, priority: 211227)

        //.Add(new MicroBenchmark($"HADB GetTypeName", Benchmark_CountForFast, count =>
        //{
        //    Async(async () =>
        //    {
        //        for (int c = 0; c < count; c++)
        //        {
        //            Limbo.ObjectSlow = hadbTestObj.GetUserDataTypeName();
        //        }
        //    });
        //}), enabled: true, priority: 211227)

        .Add(new MicroBenchmark($"Async call with no async func #1: Nothing", Benchmark_CountForFast, count =>
        {
            Async(async () =>
            {
                for (int c = 0; c < count; c++)
                {
                    await TestFunc1Async(10);
                }
            });
        }), enabled: true, priority: 211124)

        .Add(new MicroBenchmark($"Async call with no async func #2: Task.CompletedTask after", Benchmark_CountForFast, count =>
        {
            Async(async () =>
            {
                for (int c = 0; c < count; c++)
                {
                    await TestFunc2Async(10);
                }
            });
        }), enabled: true, priority: 211124)

        .Add(new MicroBenchmark($"Async call with no async func #3: Task.CompletedTask before", Benchmark_CountForFast, count =>
        {
            Async(async () =>
            {
                for (int c = 0; c < count; c++)
                {
                    await TestFunc3Async(10);
                }
            });
        }), enabled: true, priority: 211124)

        .Add(new MicroBenchmark($"Async call with no async func #4: Task.CompletedTask many times", Benchmark_CountForFast, count =>
        {
            Async(async () =>
            {
                for (int c = 0; c < count; c++)
                {
                    await TestFunc4Async(10);
                }
            });
        }), enabled: true, priority: 211124)


        .Add(new MicroBenchmark($"Async call with no async func #5: Task.CompletedTask many times ValueTask", Benchmark_CountForFast, count =>
        {
            Async(async () =>
            {
                for (int c = 0; c < count; c++)
                {
                    await TestFunc5Async(10);
                }
            });
        }), enabled: true, priority: 211124)

        .Add(new MicroBenchmark($"FastTick64.Now", Benchmark_CountForVeryFast, count =>
        {
            Async(async () =>
            {
                for (int c = 0; c < count; c++)
                {
                    Limbo.SInt64 = FastTick64.Now;
                }
            });
        }), enabled: true, priority: 211004)

        .Add(new MicroBenchmark($"CloneDeep_BinaryFormatter", Benchmark_CountForSlow, count =>
        {
            Async(async () =>
            {
                for (int c = 0; c < count; c++)
                {
                    cloneDeepSampleObj._CloneDeep(DeepCloneMethod.BinaryFormatter);
                }
            });
        }), enabled: true, priority: 210731)

        .Add(new MicroBenchmark($"CloneDeep_DeepCloner", Benchmark_CountForSlow, count =>
        {
            Async(async () =>
            {
                for (int c = 0; c < count; c++)
                {
                    cloneDeepSampleObj._CloneDeep(DeepCloneMethod.DeepCloner);
                }
            });
        }), enabled: true, priority: 210731)

        .Add(new MicroBenchmark($"CloneDeep_DeepCloner_cloneDeepSampleObj2", Benchmark_CountForSlow, count =>
        {
            Async(async () =>
            {
                for (int c = 0; c < count; c++)
                {
                    cloneDeepSampleObj2._CloneDeep(DeepCloneMethod.DeepCloner);
                }
            });
        }), enabled: true, priority: 210731)

        .Add(new MicroBenchmark($"DnsTools_Build", Benchmark_CountForNormal, count =>
        {
            // 元: DnsTools_Build: 537.80 ns, 1,859,419 / sec
            // 先: DnsTools_Build: 327.78 ns, 3,050,848 / sec

            var packetMem = Res.AppRoot["210613_novlan_dns_query_simple.txt"].HexParsedBinary;
            Packet packet = new Packet(default, packetMem._CloneSpan());
            var parsed = new PacketParsed(ref packet);
            var dnsPacket = parsed.L7.Generic.GetSpan(ref packet);

            var array = dnsPacket.ToArray().AsSpan();

            var msg = DnsUtil.ParsePacket(array);

            for (int c = 0; c < count; c++)
            {
                msg.BuildPacket();
            }
        }), enabled: true, priority: 210613)

        .Add(new MicroBenchmark($"DnsTools_Parse", Benchmark_CountForNormal, count =>
        {
            // 元: DnsTools_Parse: 208.81 ns, 4,789,072 / sec
            // 先: DnsTools_Parse: 193.02 ns, 5,180,911 / sec

            var packetMem = Res.AppRoot["210613_novlan_dns_query_simple.txt"].HexParsedBinary;
            Packet packet = new Packet(default, packetMem._CloneSpan());
            var parsed = new PacketParsed(ref packet);
            var dnsPacket = parsed.L7.Generic.GetSpan(ref packet);

            var array = dnsPacket.ToArray().AsSpan();

            for (int c = 0; c < count; c++)
            {
                DnsUtil.ParsePacket(dnsPacket);
            }
        }), enabled: true, priority: 210613)

        .Add(new MicroBenchmark($"EvaluateAclNormal", Benchmark_CountForNormal, count =>
        {
            Async(async () =>
            {
                for (int c = 0; c < count; c++)
                {
                    EasyIpAcl.Evaluate(aclStr, aclSampleIp);
                }
            });
        }), enabled: true, priority: 201212)

        .Add(new MicroBenchmark($"EvaluateAclCached1", Benchmark_CountForNormal, count =>
        {
            Async(async () =>
            {
                for (int c = 0; c < count; c++)
                {
                    EasyIpAcl.Evaluate(aclStr, aclSampleIp, enableCache: true);
                }
            });
        }), enabled: true, priority: 201212)

        .Add(new MicroBenchmark($"EvaluateAclCached2", Benchmark_CountForNormal, count =>
        {
            Async(async () =>
            {
                var acl = new EasyIpAcl(aclStr, enableCache: true);
                for (int c = 0; c < count; c++)
                {
                    acl.Evaluate(aclSampleIp);
                }
            });
        }), enabled: true, priority: 201212)


        .Add(new MicroBenchmark($"SimpleAsyncService2", Benchmark_CountForNormal, count =>
        {
            Async(async () =>
            {
                for (int c = 0; c < count; c++)
                {
                    await using var obj = new BMTest_SimpleAsyncService2();

                    await obj.TestAsync();
                }
            });
        }), enabled: true, priority: 201212)

        .Add(new MicroBenchmark($"SimpleAsynService", Benchmark_CountForNormal, count =>
        {
            Async(async () =>
            {
                for (int c = 0; c < count; c++)
                {
                    await using var obj = new BMTest_SimpleAsyncService();

                    await obj.TestAsync();
                }
            });
        }), enabled: true, priority: 201212)

        .Add(new MicroBenchmark($"WpcPacket", Benchmark_CountForNormal, count =>
        {
            unsafe
            {
                for (int c = 0; c < count; c++)
                {
                    WpcItemList l = new WpcItemList();
                    l.Add("test", "Hello"._GetBytes_Ascii());
                    l.Add("Tes", "Hello2"._GetBytes_Ascii());
                    string str = l.ToPacketString();

                    var l2 = WpcItemList.Parse(str);
                }
            }
        }), enabled: true, priority: 201212)

        .Add(new MicroBenchmark($"WpcPacket2", Benchmark_CountForNormal, count =>
        {
            unsafe
            {
                Pack p = new Pack();
                p.AddStr("1", "Hello");
                p.AddStr("2", "World");
                WpcPack wp = new WpcPack(p, "1122334455667788990011223344556677889900", "0011223344556677889900112233445566778899");
                string str = wp.ToPacketString();

                for (int c = 0; c < count; c++)
                {
                    var wp2 = WpcPack.Parse(str, true);
                }
            }
        }), enabled: true, priority: 201212)

        .Add(new MicroBenchmark($"SyncEvent", Benchmark_CountForNormal, count =>
        {
            Async(async () =>
            {
                Event ev1 = new Event();
                Event ev2 = new Event();

                CancellationTokenSource cts = new CancellationTokenSource();

                var thread = ThreadObj.Start(p =>
                {
                    while (cts.IsCancellationRequested == false)
                    {
                        ev1.Wait();

                        ev2.Set();
                    }
                });

                for (int i = 0; i < count; i++)
                {
                    ev1.Set();

                    ev2.Wait();
                }

                cts.Cancel();

                ev1.Set();

                thread.WaitForEnd();
            });
        }), enabled: true, priority: 200830)

        .Add(new MicroBenchmark($"AsyncEvent", Benchmark_CountForNormal, count =>
        {
            Async(async () =>
            {
                AsyncAutoResetEvent ev1 = new AsyncAutoResetEvent();
                AsyncAutoResetEvent ev2 = new AsyncAutoResetEvent();

                CancellationTokenSource cts = new CancellationTokenSource();

                Task t2 = AsyncAwait(async () =>
                {
                    while (cts.IsCancellationRequested == false)
                    {
                        await ev1.WaitOneAsync(cancel: cts.Token);

                        ev2.Set();
                    }
                });

                for (int i = 0; i < count; i++)
                {
                    ev1.Set();

                    await ev2.WaitOneAsync();
                }

                cts.Cancel();

                await t2;
            });
        }), enabled: true, priority: 200831)

        .Add(new MicroBenchmark($"IsSpanAllZero", Benchmark_CountForNormal, count =>
        {
            unsafe
            {
                Span<byte> span = allZeroTest.Span;
                for (int c = 0; c < count; c++)
                {
                    Limbo.BoolVolatile = Util.IsSpanAllZero(span);
                }
            }
        }), enabled: true, priority: 200810)


        .Add(new MicroBenchmark($"CalcCrc32ForZipEncryption", Benchmark_CountForNormal, count =>
        {
            unsafe
            {
                for (int c = 0; c < count; c++)
                {
                    Limbo.UInt32 += ZipCrc32.CalcCrc32ForZipEncryption(1, 2);
                }
            }
        }), enabled: true, priority: 190901)

        .Add(new MicroBenchmark($"Sync()", Benchmark_CountForNormal, count =>
        {
            localTestAsync()._GetResult();

            async Task localTestAsync()
            {
                for (int c = 0; c < count; c++)
                {
                    Sync(() =>
                    {
                        Packet p = new Packet();
                        Limbo.SInt32++;
                    });
                }
            }
        }), enabled: true, priority: 190802)

        .Add(new MicroBenchmark($"RateLimiter", Benchmark_CountForVerySlow, count =>
        {
            RateLimiter<int> rl = new RateLimiter<int>(new RateLimiterOptions(3, 1, mode: RateLimiterMode.Penalty));
            for (int c = 0; c < count; c++)
            {
                rl.TryInput(1, out _);
            }
        }), enabled: true, priority: 190802)


        //.Add(new MicroBenchmark($"Compare Test #1", Benchmark_CountForNormal, count =>
        //{
        //    byte[] b1 = "Hello World Neko Test"._GetBytes_UTF8();
        //    byte[] b2 = "Hello World Neko Test"._GetBytes_UTF8();
        //    for (int c = 0; c < count; c++)
        //    {
        //        Limbo.BoolVolatile = Util.MemEquals(b1, b2);
        //    }
        //}), enabled: true, priority: 999999)

        //.Add(new MicroBenchmark($"Compare Test #2", Benchmark_CountForNormal, count =>
        //{
        //    byte[] b1 = "Hello World Neko Test"._GetBytes_UTF8();
        //    byte[] b2 = "Hello World Neko Test"._GetBytes_UTF8();
        //    string s1 = b1._GetString_UTF8();
        //    string s2 = b2._GetString_UTF8();
        //    for (int c = 0; c < count; c++)
        //    {
        //        Limbo.BoolVolatile = s1.Equals(s2);
        //    }
        //}), enabled: true, priority: 999999)

        //.Add(new MicroBenchmark($"Compare Test #3", Benchmark_CountForNormal, count =>
        //{
        //    byte[] b1 = "Hello World Neko Test"._GetBytes_UTF8();
        //    byte[] b2 = "Hello World Neko Test"._GetBytes_UTF8();
        //    ReadOnlyMemory<byte> span1 = b1;
        //    ReadOnlyMemory<byte> span2 = b2;
        //    for (int c = 0; c < count; c++)
        //    {
        //        Limbo.Bool = span1._MemEquals(span2);
        //    }
        //}), enabled: true, priority: 999999)

        //.Add(new MicroBenchmark($"Compare Test #3", Benchmark_CountForNormal, count =>
        //{
        //    byte[] b1 = "Hello World Neko Test"._GetBytes_UTF8();
        //    byte[] b2 = "Hello World Neko Test"._GetBytes_UTF8();
        //    ReadOnlyMemory<byte> span1 = b1;
        //    ReadOnlyMemory<byte> span2 = b2;
        //    for (int c = 0; c < count; c++)
        //    {
        //        Limbo.Bool = MemoryComparers<byte>.ReadOnlyMemoryComparer.Equals(span1, span2);
        //    }
        //}), enabled: true, priority: 999999)

        //.Add(new MicroBenchmark($"GetHashCode #1", Benchmark_CountForNormal, count =>
        //{
        //    byte[] b1 = Str.MakeCharArray('x',32)._GetBytes_UTF8();
        //    byte[] b2 = "Hello World Neko Test"._GetBytes_UTF8();
        //    Span<byte> span1 = b1;
        //    Span<byte> span2 = b2;

        //    for (int c = 0; c < count; c++)
        //    {
        //        //b1[1] = (byte)c;
        //        Limbo.SInt32 += span1._ComputeHash32();
        //    }
        //}), enabled: true, priority: 999999)

        //.Add(new MicroBenchmark($"GetHashCode #2", Benchmark_CountForNormal, count =>
        //{
        //    byte[] b1 = "Hello World Neko Test"._GetBytes_UTF8();
        //    byte[] b2 = "Hello World Neko Test"._GetBytes_UTF8();
        //    string b1s = b1._GetString_UTF8();

        //    for (int c = 0; c < count; c++)
        //    {
        //        Limbo.SInt32 += b1s._ComputeHash32();
        //    }
        //}), enabled: true, priority: 999999)

        //.Add(new MicroBenchmark($"Struct equals", Benchmark_CountForNormal, count =>
        //{
        //    TestSt2 s1 = new TestSt2("Hello");
        //    TestSt2 s2 = new TestSt2("Helxo");

        //    for (int c = 0; c < count; c++)
        //    {
        //        Limbo.SInt32 = Util.StructBitCompare(s1, s2);
        //    }
        //}), enabled: true, priority: 999999)

        //.Add(new MicroBenchmark($"Struct hash", Benchmark_CountForNormal, count =>
        //{
        //    TestSt2 s1 = new TestSt2("Hello");
        //    TestSt2 s2 = new TestSt2("Helxo");

        //    for (int c = 0; c < count; c++)
        //    {
        //        Limbo.SInt32 = s1._HashMarvin();
        //        Limbo.SInt32 = s2._HashMarvin();
        //    }
        //}), enabled: true, priority: 999999)

        //.Add(new MicroBenchmark($"Generic clear #1", Benchmark_CountForNormal, count =>
        //{
        //    int size = 256;
        //    byte[] tmp1 = new byte[size];
        //    for (int c = 0; c < count; c++)
        //    {
        //        Unsafe.InitBlock(ref tmp1[0], 0, (uint)size);
        //    }
        //}), enabled: true, priority: 999999)

        //.Add(new MicroBenchmark($"Generic copy #1", Benchmark_CountForNormal, count =>
        //{
        //    int size = 256;
        //    byte[] tmp1 = new byte[size];
        //    byte[] tmp2 = new byte[size];
        //    for (int c = 0; c < count; c++)
        //    {
        //        Util.CopyByte(tmp2, 0, tmp1, 0, size);
        //    }
        //}), enabled: true, priority: 999999)

        //.Add(new MicroBenchmark($"Generic #1", Benchmark_CountForNormal, count =>
        //{
        //    int target = 32767;
        //    string tstr = "Tes" + target;
        //    for (int c = 0; c < count; c++)
        //    {
        //        Limbo.SInt32 += testDic1[tstr];
        //    }
        //}), enabled: true, priority: 999999)

        //.Add(new MicroBenchmark($"Generic #2", Benchmark_CountForNormal, count =>
        //{
        //    for (int c = 0; c < count; c++)
        //    {
        //        Limbo.SInt32 += testDic2[32767];
        //    }
        //}), enabled: true, priority: 999999)

        //.Add(new MicroBenchmark($"Generic #4", Benchmark_CountForNormal, count =>
        //{
        //    MemoryBuffer<byte> targetBuffer = new MemoryBuffer<byte>();
        //    targetBuffer.WriteSInt32(((int)32767));

        //    ReadOnlyMemory<byte> rm = targetBuffer.Memory.Slice(0, 4)._CloneMemory();

        //    TestSt1 st1 = new TestSt1(rm);
        //    for (int c = 0; c < count; c++)
        //    {
        //        Limbo.SInt32 += testDic4[st1];
        //    }
        //}), enabled: true, priority: 999999)

        //.Add(new MicroBenchmark($"Generic #3", Benchmark_CountForNormal, count =>
        //{
        //    MemoryBuffer<byte> targetBuffer = new MemoryBuffer<byte>();
        //    targetBuffer.WriteSInt32(((int)32767));

        //    ReadOnlyMemory<byte> rm = targetBuffer.Memory.Slice(0, 4)._CloneMemory();
        //    ReadOnlyMemory<byte> rm2 = targetBuffer.Memory.Slice(0, 4)._CloneMemory();

        //    var cc = MemoryComparers.ReadOnlyMemoryComparer;
        //    for (int c = 0; c < count; c++)
        //    {
        //        Limbo.SInt32 += testDic3[rm];
        //        //cc.GetHashCode(rm);
        //        //cc.GetHashCode(rm);
        //        //cc.Equals(rm, rm2);
        //    }
        //}), enabled: true, priority: 999999)

        //.Add(new MicroBenchmark($"Generic #5", Benchmark_CountForNormal, count =>
        //{
        //    byte[] rma = "Test_32767"._GetBytes_UTF8();

        //    for (int c = 0; c < count; c++)
        //    {
        //        Limbo.SInt32 += testDic5[rma];
        //    }
        //}), enabled: true, priority: 999999)

        //.Add(new MicroBenchmark($"Generic #6", Benchmark_CountForNormal, count =>
        //{
        //    TestSt2 target = new TestSt2("32767");

        //    for (int c = 0; c < count; c++)
        //    {
        //        Limbo.SInt32 += testDic6[target];
        //    }
        //}), enabled: true, priority: 999999)

        //.Add(new MicroBenchmark($"Generic #7", Benchmark_CountForNormal, count =>
        //{
        //    TestSt3 target = new TestSt3("32767");

        //    for (int c = 0; c < count; c++)
        //    {
        //        Limbo.SInt32 += testDic7[target];
        //    }
        //}), enabled: true, priority: 999999)

        //.Add(new MicroBenchmark($"Generic #8", Benchmark_CountForNormal, count =>
        //{
        //    TestSt4 st4 = new TestSt4 { IntValue = 32767 };

        //    var key = new BitStructKey<TestSt4>(st4);

        //    for (int c = 0; c < count; c++)
        //    {
        //        Limbo.SInt32 += testDic8[key];
        //    }
        //}), enabled: true, priority: 999999)

        //.Add(new MicroBenchmark($"Generic #9", Benchmark_CountForNormal, count =>
        //{
        //    TestSt5 st5 = new TestSt5 { IntValue = 32767 };

        //    for (int c = 0; c < count; c++)
        //    {
        //        Limbo.SInt32 += testDic9[st5];
        //    }
        //}), enabled: true, priority: 999999)

        //.Add(new MicroBenchmark($"isempty 1", Benchmark_CountForSlow, count =>
        //{
        //    for (int c = 0; c < count; c++)
        //    {
        //        Limbo.SInt32 = (IsEmpty)TestString3 ? 1 : 0;
        //        Limbo.SInt32 = (IsEmpty)TestString4 ? 1 : 0;
        //    }
        //}), enabled: true, priority: 190802)

        //.Add(new MicroBenchmark($"isempty 2", Benchmark_CountForSlow, count =>
        //{
        //    for (int c = 0; c < count; c++)
        //    {
        //        Limbo.SInt32 = TestString3._IsEmpty() ? 1 : 0;
        //        Limbo.SInt32 = TestString4._IsEmpty() ? 1 : 0;
        //    }
        //}), enabled: true, priority: 190802)

        .Add(new MicroBenchmark($"ignore case compare string 1", Benchmark_CountForSlow, count =>
        {
            for (int c = 0; c < count; c++)
            {
                Limbo.SInt32 = ((IgnoreCase)TestString1 == TestString2)._BoolToInt();
            }
        }), enabled: true, priority: 190802)

        .Add(new MicroBenchmark($"ignore case compare string 2", Benchmark_CountForSlow, count =>
        {
            for (int c = 0; c < count; c++)
            {
                Limbo.SInt32 = (TestString1._IsSamei(TestString2))._BoolToInt();
            }
        }), enabled: true, priority: 190802)

        .Add(new MicroBenchmark($"isemptystr #1", Benchmark_CountForSlow, count =>
        {
            string s = "  Hello_World  ";
            for (int c = 0; c < count; c++)
            {
                Limbo.SInt32 = Str.IsEmptyStr(s)._BoolToInt();
            }
        }), enabled: true, priority: 190801)

        .Add(new MicroBenchmark($"isemptystr #2", Benchmark_CountForSlow, count =>
        {
            string s = "  Hello_World  ";
            for (int c = 0; c < count; c++)
            {
                Limbo.SInt32 = string.IsNullOrWhiteSpace(s)._BoolToInt();
            }
        }), enabled: true, priority: 190802)

        .Add(new MicroBenchmark($"string test", Benchmark_CountForSlow, count =>
        {
            string s = "  Hello_World  ";
            string t = "  Hella World ";
            for (int c = 0; c < count; c++)
            {
                Limbo.SInt32 = Str.IsEmptyStr(s)._BoolToInt();
            }
        }), enabled: true, priority: 190728)

        .Add(new MicroBenchmark($"string CmpTrim new", Benchmark_CountForSlow, count =>
        {
            string s = "  Hello World  ";
            string t = "  Hella World ";
            for (int c = 0; c < count; c++)
            {
                Limbo.SInt32 = s._CmpTrim(t);
            }
        }), enabled: true, priority: 190729)

        .Add(new MicroBenchmark($"string SameTrim new", Benchmark_CountForSlow, count =>
        {
            string s = "  Hello World  ";
            string t = "  Hella World ";
            for (int c = 0; c < count; c++)
            {
                Limbo.SInt32 = s._IsSameTrim(t) ? 1 : 0;
            }
        }), enabled: true, priority: 190729)

        .Add(new MicroBenchmark($"string SameTrim old", Benchmark_CountForSlow, count =>
        {
            string s = "  Hello World  ";
            string t = "  Hella World ";
            for (int c = 0; c < count; c++)
            {
                Limbo.SInt32 = s.Trim()._IsSame(t.Trim()) ? 1 : 0;
            }
        }), enabled: true, priority: 190729)


        .Add(new MicroBenchmark($"StringWriter", Benchmark_CountForSlow, count =>
        {
            StringWriter w = new StringWriter();
            for (int i = 0; i < 10000; i++)
            {
                w.WriteLine("Hello World");
            }
        }), enabled: true, priority: 190728)

        .Add(new MicroBenchmark($"StringBuilder", Benchmark_CountForSlow, count =>
        {
            StringBuilder w = new StringBuilder();
            for (int i = 0; i < 10000; i++)
            {
                w.AppendLine("Hello World");
            }
        }), enabled: true, priority: 190728)

        .Add(new MicroBenchmark($"Span memory copy 1600 bytes", Benchmark_CountForFast, count =>
        {
            int size = 1600;
            Span<byte> src = new byte[size * 4];
            src.Fill(3);
            Span<byte> dst = new byte[size * 4];
            for (int c = 0; c < count; c++)
            {
                src.Slice(size / 2, size).CopyTo(dst.Slice(size / 2));
            }

        }), enabled: true, priority: 190528)

        .Add(new MicroBenchmark($"Span memory copy 400 bytes", Benchmark_CountForFast, count =>
        {
            int size = 400;
            Span<byte> src = new byte[size * 4];
            src.Fill(3);
            Span<byte> dst = new byte[size * 4];
            for (int c = 0; c < count; c++)
            {
                src.Slice(size / 2, size).CopyTo(dst.Slice(size / 2));
            }

        }), enabled: true, priority: 190528)

        .Add(new MicroBenchmark($"Generic Test", Benchmark_CountForNormal, count =>
        {
            unsafe
            {
                for (int c = 0; c < count; c++)
                {
                }
            }

        }), enabled: true, priority: 190531)


        .Add(new MicroBenchmark($"IpChecksum", Benchmark_CountForNormal, count =>
        {
            unsafe
            {
                Span<byte> src = Secure.Rand(48);
                int len = src.Length;
                fixed (byte* ptr = &src[0])
                {
                    for (int c = 0; c < count; c++)
                    {
                        Limbo.SInt32Volatile += IPUtil.IpChecksum(ptr, len);
                    }
                }
            }

        }), enabled: true, priority: 190531)

        .Add(new MicroBenchmark($"CalcIPv4Checksum", Benchmark_CountForNormal, count =>
        {
            unsafe
            {
                IPv4Header h = new IPv4Header();
                h.HeaderLen = 5;
                for (int c = 0; c < count; c++)
                {
                    Limbo.SInt32Volatile += h.CalcIPv4Checksum();
                }
            }

        }), enabled: true, priority: 190531)

        .Add(new MicroBenchmark($"BuildPacket #1 - Memory", Benchmark_CountForNormal, count =>
        {
            unsafe
            {
                Span<byte> initialData = "Hello"._GetBytes_Ascii();
                for (int c = 0; c < count; c++)
                {
                    Packet p = new Packet(default, initialData);

                    ref var tcpHeader = ref p.PrependSpan<TCPHeader>(sizeof(TCPHeader) + 4);

                    tcpHeader.AckNumber = 123U._Endian32_U();
                    tcpHeader.SeqNumber = 456U._Endian32_U();
                    tcpHeader.Checksum = 0x1234U._Endian16_U();
                    tcpHeader.SrcPort = 80U._Endian16_U();
                    tcpHeader.DstPort = 443U._Endian16_U();
                    tcpHeader.Flag = TCPFlags.Ack | TCPFlags.Fin | TCPFlags.Psh | TCPFlags.Rst;
                    tcpHeader.HeaderLen = (byte)((sizeof(TCPHeader) + 4) / 4);
                    tcpHeader.WindowSize = 1234U._Endian16_U();

                    ref var v4Hedaer = ref p.PrependSpan<IPv4Header>();

                    v4Hedaer.SrcIP = 0x12345678;
                    v4Hedaer.DstIP = 0xdeadbeef;
                    v4Hedaer.Checksum = 0x1234U._Endian16_U();
                    v4Hedaer.Flags = IPv4Flags.DontFragment | IPv4Flags.MoreFragments;
                    v4Hedaer.HeaderLen = (byte)(sizeof(IPv4Header) / 4);
                    v4Hedaer.Identification = 0x1234U._Endian16_U();
                    v4Hedaer.Protocol = IPProtocolNumber.TCP;
                    v4Hedaer.TimeToLive = 12;
                    v4Hedaer.TotalLength = (ushort)(sizeof(IPv4Header) + sizeof(TCPHeader) + 4);
                    v4Hedaer.Version = 4;

                    //ref var vlanHeader = ref p.PrependHeader<VLanHeader>();

                    //vlanHeader.VLanId = 12345U._Endian16();
                    //vlanHeader.Protocol = EthernetProtocolId.IPv4._Endian16();

                    ref var etherHeaderData = ref p.PrependSpan<EthernetHeader>();

                    etherHeaderData.Protocol = EthernetProtocolId.VLan._Endian16();

                    unsafe
                    {
                        etherHeaderData.SrcAddress[0] = 0x00; etherHeaderData.SrcAddress[1] = 0xAC; etherHeaderData.SrcAddress[2] = 0x01;
                        etherHeaderData.SrcAddress[3] = 0x23; etherHeaderData.SrcAddress[4] = 0x45; etherHeaderData.SrcAddress[5] = 0x47;

                        etherHeaderData.DestAddress[0] = 0x00; etherHeaderData.DestAddress[1] = 0x98; etherHeaderData.DestAddress[2] = 0x21;
                        etherHeaderData.DestAddress[3] = 0x33; etherHeaderData.DestAddress[4] = 0x89; etherHeaderData.DestAddress[5] = 0x01;
                    }
                }
            }

        }), enabled: true, priority: 190531)

        .Add(new MicroBenchmark($"ParsePacket #7 - 190531_vlan_pppoe_l2tp_udp", Benchmark_CountForNormal, count =>
        {
            var packetMem = Res.AppRoot["190531_vlan_pppoe_l2tp_udp.txt"].HexParsedBinary;

            Packet packet = new Packet(default, packetMem._CloneSpan());

            for (int c = 0; c < count; c++)
            {
                new PacketParsed(ref packet);
            }

        }), enabled: true, priority: 190531)

        .Add(new MicroBenchmark($"ParsePacket #6 - 190531_vlan_pppoe_l2tp_tcp", Benchmark_CountForNormal, count =>
        {
            var packetMem = Res.AppRoot["190531_vlan_pppoe_l2tp_tcp.txt"].HexParsedBinary;

            Packet packet = new Packet(default, packetMem._CloneSpan());

            for (int c = 0; c < count; c++)
            {
                new PacketParsed(ref packet);
            }

        }), enabled: true, priority: 190531)

        .Add(new MicroBenchmark($"ParsePacket #5 - 190531_vlan_pppoe_tcp", Benchmark_CountForNormal, count =>
        {
            var packetMem = Res.AppRoot["190531_vlan_pppoe_tcp.txt"].HexParsedBinary;

            Packet packet = new Packet(default, packetMem._CloneSpan());

            for (int c = 0; c < count; c++)
            {
                new PacketParsed(ref packet);
            }

        }), enabled: true, priority: 190531)


        .Add(new MicroBenchmark($"ParsePacket #4 - 190527_vlan_simple_udp", Benchmark_CountForNormal, count =>
        {
            var packetMem = Res.AppRoot["190527_vlan_simple_udp.txt"].HexParsedBinary;

            Packet packet = new Packet(default, packetMem._CloneSpan());

            for (int c = 0; c < count; c++)
            {
                new PacketParsed(ref packet);
            }

        }), enabled: true, priority: 190531)



        .Add(new MicroBenchmark($"ParsePacket #3 - 190527_vlan_simple_tcp", Benchmark_CountForNormal, count =>
        {
            var packetMem = Res.AppRoot["190527_vlan_simple_tcp.txt"].HexParsedBinary;

            Packet packet = new Packet(default, packetMem._CloneSpan());

            for (int c = 0; c < count; c++)
            {
                new PacketParsed(ref packet);
            }

        }), enabled: true, priority: 190531)



        .Add(new MicroBenchmark($"ParsePacket #2 - 190527_novlan_simple_udp", Benchmark_CountForNormal, count =>
        {
            var packetMem = Res.AppRoot["190527_novlan_simple_udp.txt"].HexParsedBinary;

            Packet packet = new Packet(default, packetMem._CloneSpan());

            for (int c = 0; c < count; c++)
            {
                new PacketParsed(ref packet);
            }

        }), enabled: true, priority: 190531)


        .Add(new MicroBenchmark($"ParsePacket #1 - 190527_novlan_simple_tcp", Benchmark_CountForNormal, count =>
        {
            var packetMem = Res.AppRoot["190527_novlan_simple_tcp.txt"].HexParsedBinary;

            Packet packet = new Packet(default, packetMem._CloneSpan());

            for (int c = 0; c < count; c++)
            {
                new PacketParsed(ref packet);
            }

        }), enabled: true, priority: 190531)

        .Add(new MicroBenchmark($"_IsZeroFastStruct", Benchmark_CountForFast, count =>
        {
            TCPHeader h = new TCPHeader();
            //h.SrcPort = 3;
            for (int c = 0; c < count; c++)
            {
                h._IsZeroStruct();
            }

        }), enabled: true, priority: 190528)


        .Add(new MicroBenchmark($"sizeof(T)", Benchmark_CountForVeryFast, count =>
        {
            unsafe
            {
                for (int c = 0; c < count; c++)
                {
                    Limbo.SInt32 += sizeof(IPv4Header);
                }
            }

        }), enabled: true, priority: 190528)
        .Add(new MicroBenchmark($"Util.SizeOfStruct(T)", Benchmark_CountForVeryFast, count =>
        {
            unsafe
            {
                for (int c = 0; c < count; c++)
                {
                    Limbo.SInt32 += Util.SizeOfStruct<IPv4Header>();
                }
            }

        }), enabled: true, priority: 190528)

        .Add(new MicroBenchmark($"File Write Small - Cores Object - Async", Benchmark_CountForNormal, count =>
        {
            TaskUtil.StartAsyncTaskAsync(async () =>
            {
                for (int c = 0; c < count; c++)
                {
                    await coresFile1.WriteAsync(testFileWriteData);
                }
            }, false, false)._GetResult();

        }), enabled: true, priority: 190521)


        .Add(new MicroBenchmark($"File Write Small - .NET Native - Sync", Benchmark_CountForNormal, count =>
        {
            TaskUtil.StartSyncTaskAsync(() =>
            {
                for (int c = 0; c < count; c++)
                {
                    nativeFile1.Write(testFileWriteData.Span);
                }
            }, false, false)._GetResult();

        }), enabled: true, priority: 190521)

        .Add(new MicroBenchmark($"BitAny", Benchmark_CountForVeryFast, count =>
        {
            var t1 = IPAddressType.GlobalUnicast | IPAddressType.IPv4_APIPA | IPAddressType.IPv6_AllRouterMulticast;
            for (int c = 0; c < count; c++)
            {
                Limbo.BoolVolatile = t1.BitAny(IPAddressType.GlobalUnicast);
            }
        }), enabled: true, priority: 190520)

        .Add(new MicroBenchmark($"Convert Enum to Long", Benchmark_CountForVeryFast, count =>
        {
            var value1 = IPAddressType.IPv4_APIPA;
            var value2 = LeakCounterKind.CancelWatcher;
            for (int c = 0; c < count; c++)
            {
                Limbo.SInt32 += (int)value1._RawReadValueSInt64();
                Limbo.SInt32 += (int)value2._RawReadValueSInt64();
            }
        }), enabled: true, priority: 190520)

        .Add(new MicroBenchmark($"SingletonFastArraySlim Get", Benchmark_CountForFast, count =>
        {
            SingletonFastArraySlim<LeakCounterKind, string> singletonObj = new SingletonFastArraySlim<LeakCounterKind, string>((x) => "Hello");
            for (int c = 0; c < count; c++)
            {
                string str = singletonObj[LeakCounterKind.StartDaemon];
            }
        }), enabled: true, priority: 190521)

        .Add(new MicroBenchmark($"SingletonFastArray Get", Benchmark_CountForFast, count =>
        {
            SingletonFastArray<LeakCounterKind, string> singletonObj = new SingletonFastArray<LeakCounterKind, string>((x) => "Hello");
            for (int c = 0; c < count; c++)
            {
                string str = singletonObj[LeakCounterKind.StartDaemon];
            }
        }), enabled: true, priority: 190521)

        .Add(new MicroBenchmark($"SingletonSlim Get", Benchmark_CountForFast, count =>
        {
            SingletonSlim<int, string> singletonObj = new SingletonSlim<int, string>((x) => "Hello");
            for (int c = 0; c < count; c++)
            {
                string str = singletonObj[0];
            }
        }), enabled: true, priority: 190521)

        .Add(new MicroBenchmark($"Singleton2 Get", Benchmark_CountForFast, count =>
        {
            Singleton<int, string> singletonObj = new Singleton<int, string>((x) => "Hello");
            for (int c = 0; c < count; c++)
            {
                string str = singletonObj[0];
            }
        }), enabled: true, priority: 190521)

        .Add(new MicroBenchmark($"Singleton1 Get", Benchmark_CountForFast, count =>
        {
            Singleton<string> singletonObj = new Singleton<string>(() => "Hello");
            for (int c = 0; c < count; c++)
            {
                string str = singletonObj;
            }
        }), enabled: true, priority: 190521)

        .Add(new MicroBenchmark($"New MemoryBuffer", Benchmark_CountForFast, count =>
        {
            for (int c = 0; c < count; c++)
            {
                new MemoryBuffer<byte>();
            }
        }), enabled: true, priority: 190521)

        .Add(new MicroBenchmark($"MemoryBuffer Pin Lock / Unlock", Benchmark_CountForNormal, count =>
        {
            var buf = new MemoryBuffer<byte>();
            for (int c = 0; c < count; c++)
            {
                using (buf.PinLock())
                {
                }
            }
        }), enabled: true, priority: 190521)

        //.Add(new MicroBenchmark($"New Packet", Benchmark_CountForFast, count =>
        //{
        //    for (int c = 0; c < count; c++)
        //    {
        //        Packet newPacket = new Packet();
        //        Limbo.ObjectVolatileSlow = newPacket;
        //    }
        //}), enabled: true, priority: 190528)

        //.Add(new MicroBenchmark($"New Packet with Data", Benchmark_CountForFast, count =>
        //{
        //    Memory<byte> hello = new byte[64];// "Hello World Hello World Hello World Hello World Hello World Hello World "._GetBytes_Ascii();
        //    for (int c = 0; c < count; c++)
        //    {
        //        Packet newPacket = new Packet(hello);
        //        Limbo.ObjectVolatileSlow = newPacket;
        //    }
        //}), enabled: true, priority: 190519)

        //.Add(new MicroBenchmark($"Packet struct I/O", Benchmark_CountForFast, count =>
        //{
        //    Memory<byte> hello = "Hello World Hello World Hello World Hello World Hello World Hello World "._GetBytes_Ascii();
        //    long v = 8;
        //    for (int c = 0; c < count; c++)
        //    {
        //        Packet newPacket = new Packet(hello);
        //        newPacket.InsertHeaderHead(v);
        //    }
        //}), enabled: true, priority: 190519)

        .Add(new MicroBenchmark($"Util.Rand()", Benchmark_CountForSlow, count =>
        {
            Span<byte> dest = new byte[100];

            for (int c = 0; c < count; c++)
            {
                Util.Rand(dest);
            }
        }), enabled: true, priority: 190518)

        .Add(new MicroBenchmark($"Secure.Rand()", Benchmark_CountForSlow, count =>
        {
            Span<byte> dest = new byte[100];

            for (int c = 0; c < count; c++)
            {
                Secure.Rand(dest);
            }
        }), enabled: true, priority: 190518)

        .Add(new MicroBenchmark($"IsZero - NonSparse", Benchmark_CountForSlow, count =>
        {
            ReadOnlySpan<byte> data = nonSparse.Slice(0, isZeroTestSize);

            for (int c = 0; c < count; c++)
            {
                Util.IsZero(data);
            }
        }), enabled: true, priority: 190519)

        .Add(new MicroBenchmark($"IsZero - All Zero Sparse", Benchmark_CountForSlow, count =>
        {
            ReadOnlySpan<byte> data = allZeroSparse.Slice(0, isZeroTestSize);

            for (int c = 0; c < count; c++)
            {
                Util.IsZero(data);
            }
        }), enabled: true, priority: 190519)


        .Add(new MicroBenchmark($"GetSparseChunks - NonSparse", Benchmark_CountForSlow, count =>
        {
            ReadOnlyMemory<byte> data = nonSparse;

            for (int c = 0; c < count; c++)
            {
                Util.GetSparseChunks(data, 4096);
            }
        }), enabled: true, priority: 190519)

        .Add(new MicroBenchmark($"GetSparseChunks - Sparse", Benchmark_CountForSlow, count =>
        {
            ReadOnlyMemory<byte> data = sparse;

            for (int c = 0; c < count; c++)
            {
                Util.GetSparseChunks(data, 4096);
            }
        }), enabled: true, priority: 190519)

        .Add(new MicroBenchmark($"GetSparseChunks - All Zero Sparse", Benchmark_CountForSlow, count =>
        {
            ReadOnlyMemory<byte> data = allZeroSparse;

            for (int c = 0; c < count; c++)
            {
                Util.GetSparseChunks(data, 4096);
            }
        }), enabled: true, priority: 190519)

        .Add(new MicroBenchmark($"ParseEnum", Benchmark_CountForNormal, count =>
        {
            string str = LogPriority.Info.ToString();

            for (int c = 0; c < count; c++)
            {
                str._ParseEnum(LogPriority.Trace);
            }
        }), enabled: true, priority: 190518)

        .Add(new MicroBenchmark($"Memory Copy by Array.CopyTo (Util.CopyByte) {memcopyLength._ToString3()} bytes", Benchmark_CountForSlow, count =>
        {
            for (int c = 0; c < count; c++)
            {
                Util.CopyByte(byteArrayCopy2, 0, byteArrayCopy, 0, byteArrayCopy.Length);
            }
        }), enabled: true, priority: 190505)

        .Add(new MicroBenchmark($"Memory Copy by Buffer.BlockCopy {memcopyLength._ToString3()} bytes", Benchmark_CountForSlow, count =>
        {
            for (int c = 0; c < count; c++)
            {
                Buffer.BlockCopy(byteArrayCopy2, 0, byteArrayCopy, 0, byteArrayCopy.Length);
            }
        }), enabled: true, priority: 190505)

        .Add(new MicroBenchmark($"Memory Copy by Span.CopyTo {memcopyLength._ToString3()} bytes", Benchmark_CountForSlow, count =>
        {
            for (int c = 0; c < count; c++)
            {
                var span1 = memcopySrc.Span;
                var span2 = memcopyDst.Span;
                span1.CopyTo(span2);
            }
        }), enabled: true, priority: 190505)

        .Add(new MicroBenchmark($"Memory Copy by Span.ToArray {memcopyLength._ToString3()} bytes", Benchmark_CountForSlow, count =>
        {
            for (int c = 0; c < count; c++)
            {
                var span1 = memcopySrc.Span;
                span1.ToArray();
            }
        }), enabled: true, priority: 190505)

        .Add(new MicroBenchmark($"Memory Copy by Span loop {memcopyLength._ToString3()} bytes", Benchmark_CountForSlow, count =>
        {
            for (int c = 0; c < count; c++)
            {
                var span1 = memcopySrc.Span;
                var span2 = memcopyDst.Span;
                for (int i = 0; i < memcopyLength; i++)
                {
                    span1[i] = span2[i];
                }
            }
        }), enabled: true, priority: 190505)

        .Add(new MicroBenchmark($"Memory Fill by Array loop #1 {memcopyLength._ToString3()} bytes", Benchmark_CountForSlow, count =>
        {
            for (int c = 0; c < count; c++)
            {
                for (int i = 0; i < byteArrayCopy.Length; i++)
                {
                    byteArrayCopy[i] = 0;
                }
            }
        }), enabled: true, priority: 190505)

        .Add(new MicroBenchmark($"Memory Fill by Array loop #2 {memcopyLength._ToString3()} bytes", Benchmark_CountForSlow, count =>
        {
            for (int c = 0; c < count; c++)
            {
                int len = byteArrayCopy.Length;
                for (int i = 0; i < len; i++)
                {
                    byteArrayCopy[i] = 0;
                }
            }
        }), enabled: true, priority: 190505)

        .Add(new MicroBenchmark($"Memory Fill by Span Loop {memcopyLength._ToString3()} bytes", Benchmark_CountForSlow, count =>
        {
            for (int c = 0; c < count; c++)
            {
                Span<byte> span = memcopySrc.Span;
                for (int i = 0; i < memcopyLength; i++)
                {
                    span[i] = 0;
                }
            }
        }), enabled: true, priority: 190505)

        .Add(new MicroBenchmark($"Memory Fill by unsafe pointer loop {memcopyLength._ToString3()} bytes", Benchmark_CountForSlow, count =>
        {
            for (int c = 0; c < count; c++)
            {
                Span<byte> span = memcopySrc.Span;
                unsafe
                {
                    fixed (byte* ptr = &span[0])
                    {
                        for (int i = 0; i < memcopyLength; i++)
                        {
                            ptr[i] = 0;
                        }
                    }
                }
            }
        }), enabled: true, priority: 190505)

        .Add(new MicroBenchmark($"Memory Fill by Span.Fill {memcopyLength._ToString3()} bytes", Benchmark_CountForSlow, count =>
        {
            for (int c = 0; c < count; c++)
            {
                Span<byte> span = memcopySrc.Span;
                span.Fill(0);
            }
        }), enabled: true, priority: 190505)

        .Add(new MicroBenchmark($"Memory Fill by Array.Fill {memcopyLength._ToString3()} bytes", Benchmark_CountForSlow, count =>
        {
            for (int c = 0; c < count; c++)
            {
                Array.Fill<byte>(byteArrayCopy, 0);
            }
        }), enabled: true, priority: 190505)

        .Add(new MicroBenchmark("GetSparseChunks", Benchmark_CountForSlow, count =>
        {
            for (int c = 0; c < count; c++)
                Util.GetSparseChunks(sparseTest1.Memory._AsReadOnlyMemory(), 10_000);
        }), enabled: true, priority: 190506)

        .Add(new MicroBenchmark("CallAsyncWithAwait", Benchmark_CountForNormal, count =>
        {
            BenchmarkTestTarget1.CallAsyncWithAwaitLoop(count)._GetResult();
        }), enabled: true, priority: 190505)

        .Add(new MicroBenchmark("CallAsyncWithNonAwait", Benchmark_CountForNormal, count =>
        {
            BenchmarkTestTarget1.CallAsyncWithNonAwaitLoop(count)._GetResult();
        }), enabled: true, priority: 190505)

        .Add(new MicroBenchmark("Rand[16]", Benchmark_CountForNormal, count =>
        {
            for (int c = 0; c < count; c++)
                Util.Rand(16);
        }), enabled: true, priority: 190505)

        .Add(new MicroBenchmark("Guid.NewGuid().ToString()", Benchmark_CountForNormal, count =>
        {
            for (int c = 0; c < count; c++)
                Guid.NewGuid().ToString();
        }), enabled: true, priority: 190505)

        .Add(new MicroBenchmark("Read the String_ReadOnly value", Benchmark_CountForFast, count =>
        {
            for (int c = 0; c < count; c++)
                Limbo.ObjectVolatileSlow = BenchmarkTestTarget1.String_ReadOnly;
        }), enabled: true, priority: 190505)

        .Add(new MicroBenchmark("Read the String_GetOnly value", Benchmark_CountForFast, count =>
        {
            for (int c = 0; c < count; c++)
                Limbo.ObjectVolatileSlow = BenchmarkTestTarget1.String_GetOnly;
        }), enabled: true, priority: 190505)

        .Add(new MicroBenchmark("Read the String_Copenhagen_ReadOnly value", Benchmark_CountForFast, count =>
        {
            for (int c = 0; c < count; c++)
                Limbo.ObjectVolatileSlow = BenchmarkTestTarget1.String_Copenhagen_ReadOnly;
        }), enabled: true, priority: 190505)

        .Add(new MicroBenchmark("Read the String_Copenhagen_GetOnly value", Benchmark_CountForFast, count =>
        {
            for (int c = 0; c < count; c++)
                Limbo.ObjectVolatileSlow = BenchmarkTestTarget1.String_Copenhagen_GetOnly;
        }), enabled: true, priority: 190505)

        .Add(new MicroBenchmark("Read the Int_ReadOnly value", Benchmark_CountForVeryFast, count =>
        {
            for (int c = 0; c < count; c++)
                Limbo.SInt32Volatile += BenchmarkTestTarget1.Int_ReadOnly;
        }), enabled: true, priority: 190505)

        .Add(new MicroBenchmark("Read the Int_GetOnly value", Benchmark_CountForVeryFast, count =>
        {
            for (int c = 0; c < count; c++)
                Limbo.SInt32Volatile += BenchmarkTestTarget1.Int_GetOnly;
        }), enabled: true, priority: 190505)

        .Add(new MicroBenchmark("Read the Int_Copenhagen_ReadOnly value", Benchmark_CountForFast, count =>
        {
            for (int c = 0; c < count; c++)
                Limbo.SInt32Volatile += BenchmarkTestTarget1.Int_Copenhagen_ReadOnly;
        }), enabled: true, priority: 190505)

        .Add(new MicroBenchmark("Read the Int_Copenhagen_GetOnly value", Benchmark_CountForFast, count =>
        {
            for (int c = 0; c < count; c++)
                Limbo.SInt32Volatile += BenchmarkTestTarget1.Int_Copenhagen_GetOnly;
        }), enabled: true, priority: 190505)

        .Add(new MicroBenchmark("AsyncCallMethodFromAsyncMethod", Benchmark_CountForNormal, count =>
        {
            BenchmarkTestTarget1.AsyncCallMethodFromAsyncMethod(count)._GetResult();
        }), enabled: true, priority: 190505)

        .Add(new MicroBenchmark("AsyncCallMethodFromSyncMethod", Benchmark_CountForNormal, count =>
        {
            BenchmarkTestTarget1.AsyncCallMethodFromSyncMethod(count);
        }), enabled: true, priority: 190505)

        .Add(new MicroBenchmark("Lock for readonly object", Benchmark_CountForFast, count =>
        {
            for (int c = 0; c < count; c++)
            {
                lock (BenchmarkTestTarget1.LockObj_ReadOnly)
                    Limbo.SInt32Volatile++;
            }
        }), enabled: true, priority: 190505)

        .Add(new MicroBenchmark("Lock for get property object", Benchmark_CountForFast, count =>
        {
            for (int c = 0; c < count; c++)
            {
                lock (BenchmarkTestTarget1.LockObj_Property)
                    Limbo.SInt32Volatile++;
            }
        }), enabled: true, priority: 190505);

        queue.Run();
    }

    [ConsoleCommand(
        "BenchMark command",
        "BenchMark [arg]",
        "BenchMark test")]
    static int BenchMark(ConsoleService c, string cmdName, string str)
    {
        ConsoleParam[] args = { };
        ConsoleParamValueList vl = c.ParseCommandList(cmdName, str, args);

        bool enableRecords = true;

        if (Env.IsCoresLibraryDebugBuild || IsDebugBuildChecker.IsDebugBuild || Env.IsDebuggerAttached || CoresConfig.DebugSettings.LeakCheckerFullStackLog)
        {
            Con.WriteLine("*** Warning: The benchmark is under the debug mode or full stack trace mode. We do not record to files.");
            Con.WriteLine();

            enableRecords = false;
        }

        Con.WriteLine();

        GlobalMicroBenchmark.SetParameters(enableRecords);

        GlobalMicroBenchmark.RecordStart();

        BenchMark_Test1();

        return 0;
    }
}
