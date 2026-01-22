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
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography.X509Certificates;
using System.Reflection;
using System.Linq;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using System.Diagnostics.CodeAnalysis;

namespace IPA.Cores.Globals;

public static partial class Basic
{
    static volatile int VolatileZero = 0;

    public static readonly CriticalSection GlobalSuperLock = new CriticalSection();

    public static int NoOp()
    {
        VolatileZero = 0;
        VolatileZero = VolatileZero & VolatileZero;
        return 0;
    }

    public const MethodImplOptions NoOptimization = MethodImplOptions.NoOptimization;

    public const MethodImplOptions NoInline = MethodImplOptions.NoInlining;

    public const MethodImplOptions Inline = MethodImplOptions.AggressiveInlining | (MethodImplOptions)512 /* AggressiveOptimization */;

    public static int DbgBreak()
    {
        Dbg.Break();
        return 0;
    }
    public static int BreakPoint()
    {
        Dbg.Break();
        return 0;
    }

    public static int DoNothing() => NoOp();

    public static void Sleep(int msecs) => Kernel.SleepThread(msecs);

    public static void SleepInfinite() => Kernel.SleepThreadInfinite();

    public static Task SleepAsync(int msecs) => Task.Delay(msecs);

    public static Task SleepInfiniteAsync() => SleepAsync(-1);

    public static async Task SleepRandIntervalAsync(int standard = 1000, double plusMinusPercentage = 30.0)
    {
        await SleepAsync(Util.GenRandInterval(standard, plusMinusPercentage));
    }

    public static T UnixOrWindows<T>(T unix, T windows) => Env.IsUnix ? unix : windows;

    public static string BuildLines(params string[] lines)
        => BuildLines(CrlfStyle.LocalPlatform, lines);

    public static string BuildLines(CrlfStyle crlfStyle, params string[] lines)
    {
        StringWriter w = new StringWriter();
        w.NewLine = Str.GetCrlfStr(crlfStyle);
        foreach (var line in lines)
        {
            foreach (var line2 in line._GetLines())
            {
                w.WriteLine(line2);
            }
        }
        return w.ToString();
    }

    public static DateTime DtUtcNow
    {
        [MethodImpl(Inline)]
        get => DateTime.UtcNow;
    }

    public static DateTime DtNow
    {
        [MethodImpl(Inline)]
        get => DateTime.Now;
    }

    public static DateTime DtZero
    {
        [MethodImpl(Inline)]
        get => Util.ZeroDateTimeValue;
    }

    public static DateTimeOffset DtOffsetNow
    {
        [MethodImpl(Inline)]
        get => DateTimeOffset.Now;
    }

    public static DateTimeOffset DtOffsetZero
    {
        [MethodImpl(Inline)]
        get => Util.ZeroDateTimeOffsetValue;
    }

    public static long TickNow
    {
        [MethodImpl(Inline)]
        get => Time.Tick64;
    }

    public static long TickHighresNow
    {
        [MethodImpl(Inline)]
        get => Time.HighResTick64;
    }

    public static T _DefaultIfException<T>(Func<T> getValueProc, T defaultValue)
    {
        try
        {
            return getValueProc();
        }
        catch
        {
            return defaultValue;
        }
    }

    [MethodImpl(Inline)]
    public static void Where(object? message = null, [CallerFilePath] string filename = "", [CallerLineNumber] int line = 0, [CallerMemberName] string? caller = null, bool printThreadId = false)
        => Dbg.Where(message, filename, line, caller, printThreadId);

    [MethodImpl(Inline)]
    public static void Sync(Action action) => TaskUtil.Sync(action);

    [MethodImpl(Inline)]
    public static T Sync<T>(Func<T> func) => TaskUtil.Sync(func);

    [MethodImpl(Inline)]
    public static void Async(Func<Task> asyncFunc) => TaskUtil.StartAsyncTaskAsync(asyncFunc, leakCheck: false)._GetResult();

    [MethodImpl(Inline)]
    public static T Async<T>(Func<Task<T>> asyncFunc) => TaskUtil.StartAsyncTaskAsync(asyncFunc, leakCheck: false)._GetResult();

    [MethodImpl(Inline)]
    public static Task AsyncAwait(Func<Task> asyncFunc) => TaskUtil.StartAsyncTaskAsync(asyncFunc, leakCheck: true);

    [MethodImpl(Inline)]
    public static Task<T> AsyncAwait<T>(Func<Task<T>> asyncFunc) => TaskUtil.StartAsyncTaskAsync(asyncFunc, leakCheck: true);

    [MethodImpl(Inline)]
    public static IAsyncDisposable AsyncAwaitScoped(Func<CancellationToken, Task> asyncFunc, bool noErrorMessage = false)
    {
        CancellationTokenSource cts = new CancellationTokenSource();

        Task task = asyncFunc(cts.Token);

        AsyncHolder h = new AsyncHolder(async () =>
        {
            cts._TryCancelNoBlock();

            try
            {
                await task;
            }
            catch (Exception ex)
            {
                if (noErrorMessage == false)
                {
                    ex._Debug();
                }
            }
        },
        LeakCounterKind.AsyncAwaitScoped);

        return h;
    }

    [MethodImpl(Inline)]
    public static IAsyncDisposable AsyncAwaitScoped<T>(Func<CancellationToken, Task<T>> asyncFunc, bool noErrorMessage = false)
    {
        CancellationTokenSource cts = new CancellationTokenSource();

        Task<T> task = asyncFunc(cts.Token);

        AsyncHolder h = new AsyncHolder(async () =>
        {
            cts._TryCancelNoBlock();

            try
            {
                await task;
            }
            catch (Exception ex)
            {
                if (noErrorMessage == false)
                {
                    ex._Debug();
                }
            }
        },
        LeakCounterKind.AsyncAwaitScoped);

        return h;
    }

    [MethodImpl(Inline)]
    public static IDisposable AsyncScoped(Func<CancellationToken, Task> asyncFunc, bool noErrorMessage = false)
    {
        CancellationTokenSource cts = new CancellationTokenSource();

        Task task = asyncFunc(cts.Token);

        AsyncHolder h = new AsyncHolder(async () =>
        {
            cts._TryCancelNoBlock();

            try
            {
                await task;
            }
            catch (Exception ex)
            {
                if (noErrorMessage == false)
                {
                    ex._Debug();
                }
            }
        },
        LeakCounterKind.AsyncScoped);

        return h;
    }

    [MethodImpl(Inline)]
    public static IDisposable AsyncScoped<T>(Func<CancellationToken, Task<T>> asyncFunc, bool noErrorMessage = false)
    {
        CancellationTokenSource cts = new CancellationTokenSource();

        Task<T> task = asyncFunc(cts.Token);

        AsyncHolder h = new AsyncHolder(async () =>
        {
            cts._TryCancelNoBlock();

            try
            {
                await task;
            }
            catch (Exception ex)
            {
                if (noErrorMessage == false)
                {
                    ex._Debug();
                }
            }
        },
        LeakCounterKind.AsyncScoped);

        return h;
    }

    public static DateTimeOffset DtOffsetSample(double percentageOfYear = 0.0, [CallerFilePath] string filename = "", [CallerLineNumber] int line = 0)
    {
        var now = DtNow;

        percentageOfYear = Math.Max(percentageOfYear, 0.0);
        percentageOfYear = Math.Min(percentageOfYear, 1.0);

        string randomStr = $"neko_{percentageOfYear}_{filename}_{line}_{now.Year}";
        long longValue = Secure.HashSHA1AsSInt63(randomStr._GetBytes_UTF8()) % 1_0000_0000L;
        double addDays = 30.0 * (double)longValue / (double)1_0000_0000L;
        double days = (30.0 * 11) * percentageOfYear + addDays;

        return (new DateTime(now.Year, 1, 1, 0, 0, 0))._AsDateTimeOffset(true).AddDays(days);
    }

    public static bool TryRetBool(Action action, bool noDebugMessage = false, [CallerFilePath] string filename = "", [CallerLineNumber] int line = 0, [CallerMemberName] string? caller = null, bool printThreadId = false)
    {
        try
        {
            action();
            return true;
        }
        catch (Exception ex)
        {
            if (noDebugMessage == false)
            {
                DebugWhereContainer c = new DebugWhereContainer(ex, filename, line, printThreadId ? Environment.CurrentManagedThreadId : 0, caller);
                Dbg.WriteLine(c);
            }
            return false;
        }
    }

    [return: NotNullIfNotNull("defaultValue")]
    public static T? TryIfErrorRetDefault<T>(Func<T> func, T? defaultValue = default, bool noDebugMessage = false, [CallerFilePath] string filename = "", [CallerLineNumber] int line = 0, [CallerMemberName] string? caller = null, bool printThreadId = false)
    {
        try
        {
            return func();
        }
        catch (Exception ex)
        {
            if (noDebugMessage == false)
            {
                DebugWhereContainer c = new DebugWhereContainer(ex, filename, line, printThreadId ? Environment.CurrentManagedThreadId : 0, caller);
                Dbg.WriteLine(c);
            }
            return defaultValue;
        }
    }

    public static async Task<T?> TryIfErrorRetDefaultAsync<T>(Func<Task<T>> func, T? defaultValue = default, bool noDebugMessage = false, [CallerFilePath] string filename = "", [CallerLineNumber] int line = 0, [CallerMemberName] string? caller = null, bool printThreadId = false)
    {
        try
        {
            return await func();
        }
        catch (Exception ex)
        {
            if (noDebugMessage == false)
            {
                DebugWhereContainer c = new DebugWhereContainer(ex, filename, line, printThreadId ? Environment.CurrentManagedThreadId : 0, caller);
                Dbg.WriteLine(c);
            }
            return defaultValue;
        }
    }

    public static LocalFileSystem Lfs => LocalFileSystem.Local;
    public static LargeFileSystem LLfs => LargeFileSystem.Local;
    public static Utf8BomFileSystem LfsUtf8 => LocalFileSystem.LocalUtf8;
    public static LargeFileSystem LLfsUtf8 => LargeFileSystem.LocalUtf8;
    public static ResourceFileSystem CoresRes => Res.Cores;

    public static PathParser PP => PathParser.Local;
    public static PathParser PPLinux => PathParser.Linux;
    public static PathParser PPMac => PathParser.Mac;
    public static PathParser PPWin => PathParser.Windows;

    public static string _RandStr => Str.GenRandStr();

#if CORES_BASIC_GIT
        public static GitFileSystem GitFs(string url, string commitIdOrRefName = "") => GitGlobalFs.GetFileSystem(url, commitIdOrRefName);
#endif // CORES_BASIC_GIT

    public static partial class Res
    {
        public static readonly ResourceFileSystem Cores = ResourceFileSystem.CreateOrGet(
            new AssemblyWithSourceInfo(typeof(Res), new SourceCodePathAndMarkerFileName(CoresLib.CoresLibSourceCodeFileName, Consts.FileNames.RootMarker_Library_CoresBasic)));

        public static readonly FileSystem AppRoot = LocalFileSystem.AppRoot;
    }

    public static LocalTcpIpSystem LocalNet => LocalTcpIpSystem.Local;

    public const int DefaultSize = int.MinValue;

    [MethodImpl(Inline)]
    public static Task<TResult> TR<TResult>(TResult result) => Task.FromResult(result);

    [MethodImpl(Inline)]
    public static Task TR() => Task.CompletedTask;

    public static DateTime ZeroDateTimeValue
    {
        [MethodImpl(Inline)]
        get => Util.ZeroDateTimeValue;
    }

    public static DateTime MaxDateTimeValue
    {
        [MethodImpl(Inline)]
        get => Util.MaxDateTimeValue;
    }

    public static DateTimeOffset ZeroDateTimeOffsetValue
    {
        [MethodImpl(Inline)]
        get => Util.ZeroDateTimeOffsetValue;
    }

    public static DateTimeOffset MaxDateTimeOffsetValue
    {
        [MethodImpl(Inline)]
        get => Util.MaxDateTimeOffsetValue;
    }

    public static Task TaskCompleted
    {
        [MethodImpl(Inline)]
        get => Task.CompletedTask;
    }

    public static StrComparer StrCmp
    {
        [MethodImpl(Inline)]
        get => StrComparer.SensitiveCaseComparer;
    }

    public static ExtendedStrComparer StrTrimCmp
    {
        [MethodImpl(Inline)]
        get => StrComparer.SensitiveCaseTrimComparer;
    }

    public static StrComparer StrCmpi
    {
        [MethodImpl(Inline)]
        get => StrComparer.IgnoreCaseComparer;
    }

    public static ExtendedStrComparer StrTrimCmpi
    {
        [MethodImpl(Inline)]
        get => StrComparer.IgnoreCaseTrimComparer;
    }

    [MethodImpl(NoInline | NoOptimization)]
    public static string StackInfo([CallerFilePath] string filename = "", [CallerLineNumber] int line = 0, [CallerMemberName] string? caller = null)
    {
        return Dbg.GetCurrentExecutingPositionInfoString(1, filename, line, caller);
    }

    public static void For(int count, Action<int> action)
    {
        for (int i = 0; i < count; i++)
            action(i);
    }

    public static void For(int count, Action action)
    {
        for (int i = 0; i < count; i++)
            action();
    }

    public static Memory<byte> Load(string path, int maxSize = int.MaxValue, FileFlags flags = FileFlags.None, FileSystem? fs = null, CancellationToken cancel = default) =>
        path._Load(maxSize, flags, fs, cancel);

    public static Memory<byte> Load(string path, FileSystem? fs, FileFlags flags = FileFlags.None, int maxSize = int.MaxValue, CancellationToken cancel = default) =>
        path._Load(maxSize, flags, fs, cancel);

    public static Memory<byte> Load(string path, FileFlags flags, FileSystem? fs = null, int maxSize = int.MaxValue, CancellationToken cancel = default) =>
        path._Load(maxSize, flags, fs, cancel);

    public static List<string> StrList() => StrList(new string[0]);
    public static List<string> StrList(params object?[] args)
    {
        List<string> ret = new List<string>();
        foreach (object? obj in args)
        {
            if (obj is string str)
                ret.Add(str._NonNull());
            else
                ret.Add(obj?.ToString()._NonNull() ?? "");
        }
        return ret;
    }

    [MethodImpl(Inline)]
    public static T[] EmptyOf<T>() => Array.Empty<T>();
}

