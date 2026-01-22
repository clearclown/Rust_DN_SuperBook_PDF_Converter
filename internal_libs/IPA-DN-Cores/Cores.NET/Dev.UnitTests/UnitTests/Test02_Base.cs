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
// Unit Test #1

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

using Xunit;
using Xunit.Abstractions;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

namespace IPA.UnitTest;

public class Test02_Base : IClassFixture<CoresLibUnitTestFixtureInstance>
{
    private readonly ITestOutputHelper Con;
    void Where([CallerFilePath] string fn = "", [CallerLineNumber] int l = 0, [CallerMemberName] string? f = null) => Con.WriteLine($"|{UnitTestTicks.TickString}: {Path.GetFileName(fn)}:{l} {f}() P: {Process.GetCurrentProcess().Id} T: {Thread.CurrentThread.ManagedThreadId}");

    public Test02_Base(ITestOutputHelper output)
    {
        CoresLibUnitTestShared.Init();

        Con = output;
    }

    [Fact]
    public void DotNetHostedProcessEnvInfoTest()
    {
        if (Env.IsUnix)
        {
            Assert.True(Env.IsHostedByDotNetProcess);
            Assert.True(Env.DotNetHostProcessExeName._IsFilled());
        }

        Assert.True(Env.IsDotNetCore);
    }

    [Fact]
    public void DotNetUnixConsoleModeChangePreventTest()
    {
        if (Env.IsUnix)
        {
            UnixConsoleSpecialUtil.Test_DisableDotNetConsoleModeChange();
        }
    }

    [Fact]
    public void MemoryHelperTest()
    {
        Where();

        LeakChecker.Enter();

        Assert.True(MemoryHelper._UseFast);

        string rand = Str.GenRandStr();

        Memory<byte> mem1 = rand._GetBytes_Ascii();

        var seg1 = mem1._AsSegment();
        var data1 = seg1.ToArray();
        Assert.Equal(0, mem1.Span.SequenceCompareTo(data1));

        var seg1Slow = mem1._AsSegmentSlow();
        var data1Slow = seg1Slow.ToArray();
        Assert.Equal(0, mem1.Span.SequenceCompareTo(data1Slow));

        ReadOnlyMemory<byte> mem2 = rand._GetBytes_Ascii();

        var seg2 = mem2._AsSegment();
        var data2 = seg2.ToArray();
        Assert.Equal(0, mem2.Span.SequenceCompareTo(data2));

        var seg2Slow = mem2._AsSegmentSlow();
        var data2Slow = seg2Slow.ToArray();
        Assert.Equal(0, mem2.Span.SequenceCompareTo(data2Slow));

        Where();
    }

    [Fact]
    public void InternalTaskStructureAccessd()
    {
        Where();

        int num_queued = TaskUtil.GetQueuedTasksCount(-1);
        Assert.True(num_queued >= 0);

        int num_timered = TaskUtil.GetScheduledTimersCount(-1);
        Assert.True(num_timered >= 0);

        Where();
    }

    [Fact]
    public void StrLibTest()
    {
        Assert.False(""._IsNumber());
        Assert.False("A"._IsNumber());
        Assert.True("1"._IsNumber());
        Assert.True("-1"._IsNumber());
        Assert.True("１"._IsNumber());

        var hostAndPort = Str.ParseHostnaneAndPort("abc", 80);
        Assert.Equal("abc", hostAndPort.Item1);
        Assert.Equal(80, hostAndPort.Item2);

        hostAndPort = Str.ParseHostnaneAndPort(" 1.2.3.4 :9821", 80);
        Assert.Equal("1.2.3.4", hostAndPort.Item1);
        Assert.Equal(9821, hostAndPort.Item2);

        hostAndPort = Str.ParseHostnaneAndPort("2001:1:2:3::4:9821", 80);
        Assert.Equal("2001:1:2:3::4", hostAndPort.Item1);
        Assert.Equal(9821, hostAndPort.Item2);

        hostAndPort = Str.ParseHostnaneAndPort("2001:1:2:3::4:fffe", 80);
        Assert.Equal("2001:1:2:3::4:fffe", hostAndPort.Item1);
        Assert.Equal(80, hostAndPort.Item2);

        hostAndPort = Str.ParseHostnaneAndPort("", 80);
        Assert.Equal("", hostAndPort.Item1);
        Assert.Equal(80, hostAndPort.Item2);
    }

    [Fact]
    public void SslLibTest()
    {
        Secure.CreateSslCreateCertificateContextWithFullChain(
            DevTools.TestSampleCert,
            new System.Security.Cryptography.X509Certificates.X509Certificate2Collection(DevTools.CoresDebugCACert_20221125.NativeCertificate2._SingleArray()),
            offline: true,
            errorWhenFailed: true);
    }

    [Fact]
    public void SingleInstanceTest()
    {
        Event startEvent = new Event(true);
        RefBool fail = new RefBool();
        Event event2 = new Event(true);
        Event event3 = new Event(true);
        Event event4 = new Event(true);
        Event event5 = new Event(true);
        Event event6 = new Event(true);

        var thread1 = ThreadObj.Start(x =>
        {
            try
            {
                startEvent.Wait();

                using (var si2 = new SingleInstance("si_test"))
                {
                    event2.Set();

                    event3.Wait();
                }

                event4.Set();

                event5.Wait();

                var si = SingleInstance.TryGet("si_test");
                if (si != null)
                {
                    fail.Set(true);
                }

                event6.Set();
            }
            catch
            {
                fail.Set(true);
            }
        });

        var thread2 = ThreadObj.Start(x =>
        {
            try
            {
                startEvent.Wait();

                event2.Wait();

                var si = SingleInstance.TryGet("si_test");
                if (si != null)
                {
                    fail.Set(true);
                }

                event3.Set();

                event4.Wait();

                si = SingleInstance.TryGet("si_test");
                if (si == null)
                {
                    fail.Set(true);
                }

                event5.Set();

                event6.Wait();

                si._DisposeSafe();
            }
            catch
            {
                fail.Set(true);
            }
        });

        startEvent.Set();

        Assert.True(thread1.WaitForEnd(5000));
        Assert.True(thread2.WaitForEnd(5000));

        Assert.False(fail.Value);
    }

    [Fact]
    public void GlobalLockTest()
    {
        GlobalLock k = new GlobalLock("test_lock1");

        Event startEvent = new Event(true);
        Event event2 = new Event(true);
        Event event3 = new Event(true);
        RefBool fail = new RefBool();

        var thread1 = ThreadObj.Start(x =>
        {
            try
            {
                startEvent.Wait();

                using (k.Lock())
                {
                    event2.Set();

                    if (event3.Wait(100))
                    {
                        fail.Set(true);
                    }
                }
            }
            catch
            {
                fail.Set(true);
            }
        });

        var thread2 = ThreadObj.Start(x =>
        {
            try
            {
                startEvent.Wait();

                event2.Wait();

                using (k.Lock())
                {
                    event3.Set();
                }
            }
            catch
            {
                fail.Set(true);
            }
        });

        startEvent.Set();

        Assert.True(thread1.WaitForEnd(5000));
        Assert.True(thread2.WaitForEnd(5000));

        Assert.False(fail.Value);
    }

    [Fact]
    public void MimeLookup()
    {
        Assert.True(MasterData.ExtensionToMime.Get(".xlsx")._IsSamei("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"));
    }

    [Fact]
    public void Win32FileTest()
    {
        if (Env.IsWindows == false) return;

        Async(async () =>
        {
            const string dirAcl = "D:PAI(A;OICI;FA;;;BU)(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)(A;OICI;FA;;;S-1-5-21-2439965180-1288029102-2284794580-1001)";
            const string fileAcl = "D:PAI(A;;FA;;;WD)(A;;FA;;;SY)(A;;FA;;;BA)(A;;FA;;;BU)(A;;FA;;;S-1-5-21-2439965180-1288029102-2284794580-1001)";
            const string altStream = "W1pvbmVUcmFuc2Zlcl0NClpvbmVJZD0zDQpSZWZlcnJlclVybD1odHRwczovL3N0YXRpYy5sdHMuZG4uaXBhbnR0Lm5ldC9kLzIxMDExMV8wMDNfdWJ1bnR1X3NldHVwX3NjcmlwdHNfNTk4NjcvDQpIb3N0VXJsPWh0dHBzOi8vc3RhdGljLmx0cy5kbi5pcGFudHQubmV0L2QvMjEwMTExXzAwM191YnVudHVfc2V0dXBfc2NyaXB0c181OTg2Ny8yMTAxMTFfYXB0X3VidW50dV9qYXBhbl9zZXJ2ZXIuc2gNCg==";

            DirectoryPath dirPath = Env.MyLocalTempDir._CombinePath(Str.GenRandStr());

            try
            {
                dirPath.DeleteDirectory(true);
            }
            catch { }

            dirPath.CreateDirectory();

            FileMetadata dirMeta1 = new FileMetadata(
                    securityData: new FileSecurityMetadata()
                    {
                        Acl = new FileSecurityAcl
                        {
                            Win32AclSddl = dirAcl
                        },
                    }
                );

            dirPath.SetDirectoryMetadata(dirMeta1);
            
            FilePath filePath1 = dirPath.Combine("test1.txt");
            FilePath filePath2 = dirPath.Combine("test2.txt");

            await filePath1.WriteDataToFileAsync("Hello"._GetBytes_Ascii(), FileFlags.Async | FileFlags.AutoCreateDirectory);

            await filePath2.WriteDataToFileAsync("Hello"._GetBytes_Ascii(), FileFlags.Async | FileFlags.AutoCreateDirectory);

            FileMetadata fileMeta1 = new FileMetadata(specialOperation: FileSpecialOperationFlags.SetCompressionFlag,
                securityData: new FileSecurityMetadata()
                {
                    Acl = new FileSecurityAcl
                    {
                        Win32AclSddl = fileAcl
                    },
                },
                alternateStream: new FileAlternateStreamMetadata { Items = new FileAlternateStreamItemMetadata { Name = ":Zone.Identifier:$DATA", Data = altStream._Base64Decode() }._SingleArray() }
                );

            await filePath1.SetFileMetadataAsync(fileMeta1);

            await Lfs.CopyFileAsync(filePath1, filePath2, new CopyFileParams(flags: FileFlags.Async, metadataCopier: new FileMetadataCopier(FileMetadataCopyMode.All)));

            FileMetadata fileMeta2 = await filePath2.GetFileMetadataAsync();

            if (fileMeta1.Security!.Acl!.Win32AclSddl._IsSamei(fileMeta2.Security!.Acl!.Win32AclSddl) == false)
            {
                throw new CoresException($"Different ACL. '{fileMeta1.Security!.Acl!.Win32AclSddl}' != '{fileMeta2.Security!.Acl!.Win32AclSddl}'");
            }

            if (fileMeta2.AlternateStream!.Items![0].Data!._MemEquals(altStream._Base64Decode()) == false)
            {
                throw new CoresException("Different alternative stream.");
            }

            var fileMeta1_2 = await filePath1.GetFileMetadataAsync();
            if (fileMeta1_2.Attributes!.Value.Bit(FileAttributes.Compressed) == false)
            {
                throw new CoresException("FileAttributes.Compressed not set.");
            }
        });
    }

    [Fact]
    public void TestDateTimeOffsetStr()
    {
        for (int i = 0; i < 10; i++)
        {
            DateTimeOffset src = DtOffsetNow.AddTicks(Secure.RandSInt63() % 1000000000);
            if ((i % 3) == 0)
            {
                src = src.ToUniversalTime();
            }
            else if ((i % 3) == 1)
            {
                src = src.ToOffset(new TimeSpan(-7, 30, 0));
            }
            string dtStr = src._ToDtStr(true, withNanoSecs: true);
            DateTimeOffset dst = Str.DtstrToDateTimeOffset(dtStr);
            string dtStr2 = dst._ToDtStr(true, withNanoSecs: true);
            Dbg.TestTrue(dtStr == dtStr2);
            Dbg.TestTrue(src.Ticks == dst.Ticks);
        }
    }

    public class TestFuncObjectUniqueClass
    {
        ConcurrentHashSet<object> TestSet = new ConcurrentHashSet<object>();

        public bool TryAdd(object proc)
        {
            return TestSet.Add(proc);
        }
    }

    [Fact]
    public void TestDnsEasyResponder()
    {
    }

    [Fact]
    public void TestObjectSearchStrGenerator()
    {
        Str.Test_SearchableStr();
    }

}



