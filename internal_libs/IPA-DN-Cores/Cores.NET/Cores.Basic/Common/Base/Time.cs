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
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

namespace IPA.Cores.Basic;

public static partial class CoresConfig
{
    public static partial class TimeAdjustSettings
    {
        public static readonly Copenhagen<bool> DisableTimeAdjustThread = true;
        public static readonly Copenhagen<int> DiffThresholdMsecs = 1000;
        public static readonly Copenhagen<int> MaxNumHistory = 1000;
    }
}

public class TimeHelper
{
    internal Stopwatch Sw;
    internal long Freq;
    internal DateTimeOffset FirstDateTimeOffset;

    public TimeHelper()
    {
        FirstDateTimeOffset = DateTimeOffset.Now;
        //FirstDateTimeOffset = 
        Sw = new Stopwatch();
        Sw.Start();
        Freq = Stopwatch.Frequency;
    }

    public DateTimeOffset GetDateTimeOffset()
    {
        return FirstDateTimeOffset + this.Sw.Elapsed;
    }
}

public abstract class TimeAdjustHistoryBase : IDisposable
{
    class History
    {
        public long Tick, Time;
    }

    public const int DefaultInterval = 1000;

    public int CurrentTimeZoneDiffMSec { get; private set; } = (int)(TimeZoneInfo.Local.GetUtcOffset(DateTime.UtcNow).Ticks / 10000);

    long Time64;
    long Tick64WithTime64;
    readonly CriticalSection Lock = new CriticalSection<TimeAdjustHistoryBase>();
    ImmutableList<History> HistoryList = ImmutableList<History>.Empty;
    History LatestHistory = new History();
    public int Interval { get; }
    AsyncManualResetEvent HaltEvent = new AsyncManualResetEvent();
    bool HaltFlag = false;
    Event InitCompletedEvent = new Event(true);

    Task MainTask;

    public TimeAdjustHistoryBase(int interval = DefaultInterval)
    {
        Init();
        this.Interval = interval;
        MainTask = TimeAdjustHistoryThreadAsync();
        InitCompletedEvent.Wait();
    }

    public void Dispose() { this.Dispose(true); GC.SuppressFinalize(this); }
    Once DisposeFlag;
    protected virtual void Dispose(bool disposing)
    {
        if (!disposing || DisposeFlag.IsFirstCall() == false) return;
        HaltFlag = true;
        HaltEvent.Set();
        MainTask._GetResult();
    }

    protected abstract void Init();
    protected abstract long GetTick();

    async Task TimeAdjustHistoryThreadAsync()
    {
        int n = 0;
        bool first = false;
        bool createFirstEntry = true;
        int tickSpan = this.Interval;
        int diffThreshold = Math.Max(CoresConfig.TimeAdjustSettings.DiffThresholdMsecs, 100);

        while (true)
        {
            long tick64 = GetTick();

            if (createFirstEntry)
            {
                History t = new History() { Tick = tick64, Time = Time.SystemTime64 };
                this.Tick64WithTime64 = tick64;
                this.Time64 = t.Time;
                HistoryList = HistoryList.Add(t);
                LatestHistory = t;

                InitCompletedEvent.Set();
                createFirstEntry = false;

                if (CoresConfig.TimeAdjustSettings.DisableTimeAdjustThread)
                {
                    // Disabled
                    return;
                }
            }

            // Time correction
            n += tickSpan;
            if (n >= 1000 || first == false)
            {
                long now = Time.SystemTime64;
                CurrentTimeZoneDiffMSec = (int)(TimeZoneInfo.Local.GetUtcOffset(DateTime.UtcNow).Ticks / 10000);
                long diff = Math.Abs(((now - this.Time64) + this.Tick64WithTime64) - tick64);

                if (now < this.Time64 || diff >= diffThreshold)
                {
                    History t = new History();
                    t.Tick = tick64;
                    t.Time = now;

                    HistoryList = HistoryList.Add(t);
                    LatestHistory = t;

                    Dbg.WriteLine(new { AdjustTime = new { Diff = diff, Tick = t.Tick, Time = t.Time, HistoryCount = HistoryList.Count } });

                    // To prevent consuming memory infinite on a system that clock is skewd
                    if (this.HistoryList.Count >= CoresConfig.TimeAdjustSettings.MaxNumHistory)
                    {
                        // Remove the second
                        this.HistoryList = this.HistoryList.RemoveAt(1);
                    }

                    this.Time64 = now;
                    this.Tick64WithTime64 = tick64;
                }
                first = true;
                n = 0;
            }

            if (this.HaltFlag)
                break;

            await this.HaltEvent.WaitAsync(tickSpan);
        }
    }

    public long Tick64ToTime64(long tick)
    {
        long ret = 0;
        if (tick <= 0) return 0;
        var List = this.HistoryList;
        for (int i = List.Count - 1; i >= 0; i--)
        {
            History t = List[i];
            if (t.Tick <= tick)
            {
                ret = t.Time + (tick - t.Tick);
                break;
            }
        }
        if (ret == 0) ret = 1;
        return ret;
    }

    [MethodImpl(Inline)]
    public long Tick64ToTime64Latest(long tick)
    {
        long ret = 0;
        if (tick <= 0) return 0;
        History t = this.LatestHistory;
        if (t.Tick <= tick)
        {
            ret = t.Time + (tick - t.Tick);
        }
        if (ret == 0) ret = 1;
        return ret;
    }
}

public static class FastTick64
{
    static FastTick64()
    {
        Init();
    }

    class AdjustHistory : TimeAdjustHistoryBase
    {
        protected override void Init() => FastTick64.Init();
        protected override long GetTick() => FastTick64.Now;
    }

    static AdjustHistory History = new AdjustHistory();

    public static long Now { [MethodImpl(Inline)] get => GetTick64() - Base; }

    static long Base;
    static void Init()
    {
        Base = GetTick64() - 1;
    }

    [MethodImpl(Inline)]
    static long GetTick64()
    {
        return Environment.TickCount64 + 1000;
    }

    public static int CurrentTimeZoneDiffMSec => History.CurrentTimeZoneDiffMSec;

    public static long SystemTimeNow_Fast
    {
        [MethodImpl(Inline)]
        get => History.Tick64ToTime64Latest(Now);
    }

    public static DateTime DateTimeNow_Fast
    {
        [MethodImpl(Inline)]
        get => Time.Time64ToDateTime(History.Tick64ToTime64Latest(Now));
    }

    public static DateTimeOffset DateTimeOffsetUtcNow_Fast
    {
        [MethodImpl(Inline)]
        get => Time.Time64ToDateTime(History.Tick64ToTime64Latest(Now))._AsDateTimeOffset(false);
    }

    public static DateTimeOffset DateTimeOffsetLocalNow_Fast
    {
        [MethodImpl(Inline)]
        get => Time.Time64ToDateTime(History.Tick64ToTime64Latest(Now))._AsDateTimeOffset(true);
    }

    public static long Tick64ToTime64(long tick) => History.Tick64ToTime64(tick);
    public static DateTime Tick64ToDateTime(long tick) => Time.Time64ToDateTime(Tick64ToTime64(tick));
    public static DateTimeOffset Tick64ToDateTimeOffsetUtc(long tick) => Time.Time64ToDateTime(Tick64ToTime64(tick))._AsDateTimeOffset(false);
    public static DateTimeOffset Tick64ToDateTimeOffsetLocal(long tick) => Time.Time64ToDateTime(Tick64ToTime64(tick)).ToLocalTime()._AsDateTimeOffset(true);
}

public static class Time
{
    static TimeHelper h = new TimeHelper();
    static TimeSpan baseTimeSpan = new TimeSpan(0, 0, 1);

    static public TimeSpan NowHighResTimeSpan => h.Sw.Elapsed.Add(baseTimeSpan);
    static public long NowHighResLong100Usecs => NowHighResTimeSpan.Ticks;
    static public long NowHighResLongMillisecs => NowHighResLong100Usecs / 10000;

    static public long Tick64 => FastTick64.Now;

    static public long HighResTick64 => NowHighResLongMillisecs;

    static public double NowHighResDouble => (double)NowHighResLong100Usecs / (double)10000000.0;
    static public DateTime NowHighResDateTimeLocal => h.GetDateTimeOffset().LocalDateTime;
    static public DateTime NowHighResDateTimeUtc => h.GetDateTimeOffset().UtcDateTime;
    static public DateTimeOffset NowHighResDateTimeOffset => h.GetDateTimeOffset();

    public static int CurrentTimeZoneDiffMSec => FastTick64.CurrentTimeZoneDiffMSec;

    public static long DateTimeToTime64(DateTime dt) => (long)Util.ConvertDateTime(dt);
    public static DateTime Time64ToDateTime(long time64) => Util.ConvertDateTime((ulong)time64);
    public static DateTimeOffset Time64ToDateTimeOffsetUtc(long time64) => Time64ToDateTime(time64)._AsDateTimeOffset(false);
    public static DateTimeOffset Time64ToDateTimeOffsetLocal(long time64) => Time64ToDateTime(time64).ToLocalTime()._AsDateTimeOffset(true);

    public static long SystemTime64 => DateTimeToTime64(DateTime.UtcNow);
    public static long LocalTime64 => DateTimeToTime64(DateTime.Now);

    public static long Tick64ToTime64(long tick) => FastTick64.Tick64ToTime64(tick);
    public static DateTime Tick64ToDateTime(long tick) => FastTick64.Tick64ToDateTime(tick);
    public static DateTimeOffset Tick64ToDateTimeOffsetUtc(long tick) => FastTick64.Tick64ToDateTimeOffsetUtc(tick);
    public static DateTimeOffset Tick64ToDateTimeOffsetLocal(long tick) => FastTick64.Tick64ToDateTimeOffsetLocal(tick);

    public static long SystemTimeNow_Fast => FastTick64.Tick64ToTime64(FastTick64.Now);
    public static DateTime DateTimeNow_Fast => FastTick64.Tick64ToDateTime(FastTick64.Now);
    public static DateTimeOffset DateTimeOffsetUtcNow_Fast => FastTick64.Tick64ToDateTimeOffsetUtc(FastTick64.Now);
    public static DateTimeOffset DateTimeOffsetLocalNow_Fast => FastTick64.Tick64ToDateTimeOffsetLocal(FastTick64.Now);
}
