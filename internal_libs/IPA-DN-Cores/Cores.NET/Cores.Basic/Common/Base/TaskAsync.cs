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
using System.Diagnostics;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.Concurrent;
using System.Reflection;
using System.Linq;
using System.Runtime.CompilerServices;
using System.IO;
using System.Diagnostics.CodeAnalysis;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

namespace IPA.Cores.Basic;

public static partial class CoresConfig
{
    public static partial class ThreadPoolConfig
    {
        public static readonly Copenhagen<int> DefaultMinWorkerThreads = 0;
        public static readonly Copenhagen<int> DefaultMinIoCompletionThreads = 0;

        public static readonly Copenhagen<int> DefaultMaxWorkerThreads = 0;
        public static readonly Copenhagen<int> DefaultMaxIoCompletionThreads = 0;

        // 重いサーバー (大量のインスタンスや大量のコンテナが稼働、または大量のコネクションを処理) における定数変更
        public static void ApplyHeavyLoadServerConfig()
        {
            DefaultMinWorkerThreads.TrySet(512);
            DefaultMinIoCompletionThreads.TrySet(256);

            DefaultMaxWorkerThreads.TrySet(4096);
            DefaultMaxIoCompletionThreads.TrySet(2048);
        }
    }
}

public static class ThreadPoolConfigUtil
{
    public static StaticModule Module { get; } = new StaticModule(ModuleInit, ModuleFree);

    static int InitialMinWorkerThreads = 0;
    static int InitialMinIoCompletionThreads = 0;

    static int InitialMaxWorkerThreads = 0;
    static int InitialMaxIoCompletionThreads = 0;

    static void ModuleInit()
    {
        ThreadPool.GetMaxThreads(out InitialMaxWorkerThreads, out InitialMaxIoCompletionThreads);
        ThreadPool.GetMinThreads(out InitialMinWorkerThreads, out InitialMinIoCompletionThreads);

        // 設定を変更する

        // 1 回目
        if (CoresConfig.ThreadPoolConfig.DefaultMinWorkerThreads != 0 && CoresConfig.ThreadPoolConfig.DefaultMinIoCompletionThreads != 0)
        {
            try
            {
                ThreadPool.SetMinThreads(CoresConfig.ThreadPoolConfig.DefaultMinWorkerThreads, CoresConfig.ThreadPoolConfig.DefaultMinIoCompletionThreads);
            }
            catch { }
        }
        if (CoresConfig.ThreadPoolConfig.DefaultMaxWorkerThreads != 0 && CoresConfig.ThreadPoolConfig.DefaultMaxIoCompletionThreads != 0)
        {
            try
            {
                ThreadPool.SetMaxThreads(CoresConfig.ThreadPoolConfig.DefaultMaxWorkerThreads, CoresConfig.ThreadPoolConfig.DefaultMaxIoCompletionThreads);
            }
            catch { }
        }

        // 2 回目
        if (CoresConfig.ThreadPoolConfig.DefaultMinWorkerThreads != 0 && CoresConfig.ThreadPoolConfig.DefaultMinIoCompletionThreads != 0)
        {
            try
            {
                ThreadPool.SetMinThreads(CoresConfig.ThreadPoolConfig.DefaultMinWorkerThreads, CoresConfig.ThreadPoolConfig.DefaultMinIoCompletionThreads);
            }
            catch { }
        }
        if (CoresConfig.ThreadPoolConfig.DefaultMaxWorkerThreads != 0 && CoresConfig.ThreadPoolConfig.DefaultMaxIoCompletionThreads != 0)
        {
            try
            {
                ThreadPool.SetMaxThreads(CoresConfig.ThreadPoolConfig.DefaultMaxWorkerThreads, CoresConfig.ThreadPoolConfig.DefaultMaxIoCompletionThreads);
            }
            catch { }
        }
    }

    static void ModuleFree()
    {
        // 設定を復元する

        // 1 回目
        if (InitialMinWorkerThreads != 0 && InitialMinIoCompletionThreads != 0)
        {
            try
            {
                ThreadPool.SetMinThreads(InitialMinWorkerThreads, InitialMinIoCompletionThreads);
            }
            catch { }
        }
        if (InitialMaxWorkerThreads != 0 && InitialMaxIoCompletionThreads != 0)
        {
            try
            {
                ThreadPool.SetMaxThreads(InitialMaxWorkerThreads, InitialMaxIoCompletionThreads);
            }
            catch { }
        }

        // 2 回目
        if (InitialMinWorkerThreads != 0 && InitialMinIoCompletionThreads != 0)
        {
            try
            {
                ThreadPool.SetMinThreads(InitialMinWorkerThreads, InitialMinIoCompletionThreads);
            }
            catch { }
        }
        if (InitialMaxWorkerThreads != 0 && InitialMaxIoCompletionThreads != 0)
        {
            try
            {
                ThreadPool.SetMaxThreads(InitialMaxWorkerThreads, InitialMaxIoCompletionThreads);
            }
            catch { }
        }

        InitialMinWorkerThreads = 0;
        InitialMinIoCompletionThreads = 0;

        InitialMaxWorkerThreads = 0;
        InitialMaxIoCompletionThreads = 0;
    }
}

public static partial class CoresConfig
{
    public static partial class TaskAsyncSettings
    {
        public static readonly Copenhagen<int> WaitTimeoutUntilPendingTaskFinish = 1 * 1000;

        // AsyncEvent で新しい TaskCompletionSource を作成する際のオプション。
        // すなわち Set() すると同一スレッドで継続するか、別スレッドになるか、の違いである。
        // TaskCreationOptions.RunContinuationsAsynchronously を指定すると、別スレッドで作成されるようになる。
        // デッドロック問題がある場合は、TaskCreationOptions.RunContinuationsAsynchronously にすると解決される場合がある。
        public static readonly Copenhagen<TaskCreationOptions> AsyncEventTaskCreationOption = TaskCreationOptions.RunContinuationsAsynchronously;

        // 何個以上の場合に並列処理するか
        public static readonly Copenhagen<int> ParallelProcessingMinCountThreshold = 128;
    }
}

// 指定した数までタスクを並行して実行するクラス。
// 指定した数以上のタスクがすでに実行されている場合は、実行数が指定数以下になるまで新しいタスクの登録を待機する。
public class AsyncConcurrentTask
{
    public int MaxConcurrentTasks { get; }

    readonly SemaphoreSlim Sem;

    public AsyncConcurrentTask(int maxConcurrentTasks)
    {
        this.MaxConcurrentTasks = Math.Max(maxConcurrentTasks, 1);

        this.Sem = new SemaphoreSlim(this.MaxConcurrentTasks, this.MaxConcurrentTasks);
    }

    readonly CriticalSection Lock = new CriticalSection<AsyncConcurrentTask>();

    readonly AsyncPulse Pulse = new AsyncPulse();

    public int CurrentConcurrentTasks => this.MaxConcurrentTasks - this.Sem.CurrentCount;

    readonly AsyncPulse CurrentRunningTasksChangedInternal = new AsyncPulse();
    volatile int CurrentRunningTasksInternal = 0;
    public int CurrentRunningTasks => CurrentRunningTasksInternal;

    // すべての動作中のタスクが終了するまで待機する
    public async Task<bool> WaitAllTasksFinishAsync(int timeout = Timeout.Infinite, CancellationToken cancel = default)
    {
        var pulseWaiter = CurrentRunningTasksChangedInternal.GetPulseWaiter();

        while (this.CurrentConcurrentTasks >= 1)
        {
            if (await pulseWaiter.WaitAsync(timeout, cancel) == false)
            {
                // タイムアウト
                break;
            }
        }

        if (this.CurrentConcurrentTasks >= 1)
        {
            return false;
        }
        else
        {
            return true;
        }
    }

    // タスクを新たに追加し実行開始する。ただし、すでに実行中のタスク数が上限以上の場合は、上限以下になるまで非同期ブロックする
    public async Task<Task<TResult>> StartTaskAsync<TParam, TResult>(Func<TParam, CancellationToken, Task<TResult>> targetTask, TParam param, CancellationToken cancel = default)
    {
        Interlocked.Increment(ref CurrentRunningTasksInternal);

        try
        {
            cancel.ThrowIfCancellationRequested();

            await this.Sem.WaitAsync(cancel);

            // ターゲットタスクを開始する。
            // ターゲットタスクが完了したら、セマフォを解放するようにする。
            Task<TResult> ret = TaskUtil.StartAsyncTaskAsync<TResult>(async () =>
            {
                Interlocked.Increment(ref CurrentRunningTasksInternal);

                try
                {
                    return await targetTask(param, cancel);
                }
                finally
                {
                    this.Sem.Release();
                    Interlocked.Decrement(ref CurrentRunningTasksInternal);
                    CurrentRunningTasksChangedInternal.FirePulse();
                }
            });

            return ret;
        }
        finally
        {
            Interlocked.Decrement(ref CurrentRunningTasksInternal);
            CurrentRunningTasksChangedInternal.FirePulse();
        }
    }
}

// Pulse Waiter
public class AsyncPulseWaiter
{
    volatile int LastVersion = 0;

    public AsyncPulse Pulse { get; }

    internal AsyncPulseWaiter(AsyncPulse pulse)
    {
        this.Pulse = pulse;
    }

    public async Task<bool> WaitAsync(int timeout = Timeout.Infinite, CancellationToken cancel = default)
    {
        using (var eventHolder = Pulse.RegisterAndObtainEventInternal())
        {
            if (LastVersion != Pulse.Version)
            {
                // Register 処理中に 1 回でも Fire された場合
                LastVersion = Pulse.Version;
                return true;
            }

            bool ret = await eventHolder.Value.WaitAsync(timeout, cancel);

            bool changed = false;

            if (LastVersion != Pulse.Version)
            {
                LastVersion = Pulse.Version;
                changed = true;
            }

            return changed;
        }
    }
}

// 複数のタスクが非同期に待機することができるパルス。パルスは誰でも Fire することができ、Fire がある毎に、待機しているすべてのタスクの待機が解除される。
public class AsyncPulse
{
    readonly CriticalSection Lock = new CriticalSection<AsyncPulse>();

    readonly HashSet<AsyncManualResetEvent> EventList = new HashSet<AsyncManualResetEvent>();

    volatile int _Version = 0;

    public int Version => _Version;

    public AsyncPulseWaiter GetPulseWaiter()
    {
        return new AsyncPulseWaiter(this);
    }

    internal Holder<AsyncManualResetEvent> RegisterAndObtainEventInternal()
    {
        AsyncManualResetEvent newEvent = new AsyncManualResetEvent();

        lock (Lock)
        {
            EventList.Add(newEvent);
        }

        return new Holder<AsyncManualResetEvent>(x =>
        {
            lock (Lock)
            {
                EventList.Remove(x);
            }
        },
        newEvent,
        LeakCounterKind.AsyncPulseRegisteredEvent);
    }

    public void FirePulse(bool softly = false)
    {
        AsyncManualResetEvent[] eventArray;

        lock (Lock)
        {
            eventArray = this.EventList.ToArray();
            this._Version++;
        }

        foreach (AsyncManualResetEvent e in eventArray)
        {
            e.Set(softly);
        }
    }
}

public sealed class NamedAsyncLocks
{
    readonly Dictionary<string, AsyncLock> List;
    readonly CriticalSection LockObj = new CriticalSection<NamedAsyncLocks>();

    readonly RefInt CurrentProcessing = new RefInt();

    int currentLockCountToGc = 0;

    public NamedAsyncLocks(IEqualityComparer<string>? comparer = null)
    {
        if (comparer == null) comparer = StrComparer.SensitiveCaseComparer;

        List = new Dictionary<string, AsyncLock>(comparer);
    }

    public async Task<AsyncLock.LockHolder> LockWithAwait(string name, CancellationToken cancel = default)
    {
        AsyncLock targetLock;

        currentLockCountToGc++;
        if (currentLockCountToGc >= 1000)
        {
            // 1000 回のロック試行ごとに Gc を実行する。適当ちゃん!
            if (Gc())
            {
                currentLockCountToGc = 0;
            }
        }

        CurrentProcessing.Increment();
        try
        {
            lock (LockObj)
            {
                if (this.List.ContainsKey(name) == false)
                {
                    targetLock = new AsyncLock();
                    this.List.Add(name, targetLock);
                }
                else
                {
                    targetLock = this.List[name];
                }
            }

            return await targetLock.LockWithAwait(cancel);
        }
        finally
        {
            CurrentProcessing.Decrement();
        }
    }

    bool Gc()
    {
        List<AsyncLock> deleteObjects = new List<AsyncLock>();

        if (CurrentProcessing != 0)
        {
            return false;
        }

        lock (LockObj)
        {
            if (CurrentProcessing != 0)
            {
                return false;
            }

            List<string> deleteNames = new List<string>();

            foreach (var kv in this.List)
            {
                if (kv.Value.IsLocked == false)
                {
                    deleteNames.Add(kv.Key);
                    deleteObjects.Add(kv.Value);
                }
            }

            foreach (string name in deleteNames)
            {
                this.List.Remove(name);
            }
        }

        foreach (AsyncLock obj in deleteObjects)
        {
            obj._DisposeSafe();
        }

        return true;
    }
}

// 注意: Dispose をしなくてもメモリリークは発生しないのである。
public sealed class AsyncLock : IDisposable
{
    public sealed class LockHolder : IDisposable
    {
        AsyncLock Parent;
        public LockHolder(AsyncLock parent)
        {
            this.Parent = parent;
        }

        Once DisposeFlag;
        public void Dispose()
        {
            if (DisposeFlag.IsFirstCall())
            {
                this.Parent.Unlock();
            }
        }
    }

    SemaphoreSlim? Semaphone = new SemaphoreSlim(1, 1);
    Once DisposeFlag;
    int _NumWaitingInternal = 0;

    public int NumWaiting => this._NumWaitingInternal;

    public async Task<LockHolder> LockWithAwait(CancellationToken cancel = default)
    {
        await _LockAsync(cancel);

        return new LockHolder(this);
    }

    public LockHolder LockLegacy(CancellationToken cancel = default)
    {
        _Lock(cancel);
        return new LockHolder(this);
    }

    public async Task _LockAsync(CancellationToken cancel = default)
    {
        Interlocked.Increment(ref this._NumWaitingInternal);
        try
        {
            await Semaphone!.WaitAsync(cancel);
        }
        finally
        {
            Interlocked.Decrement(ref this._NumWaitingInternal);
        }
    }
    public void _Lock(CancellationToken cancel = default)
    {
        Interlocked.Increment(ref this._NumWaitingInternal);
        try
        {
            Semaphone!.Wait(cancel);
        }
        finally
        {
            Interlocked.Decrement(ref this._NumWaitingInternal);
        }
    }
    public void Unlock() => Semaphone!.Release();

    public bool IsLocked => ((Semaphone?.CurrentCount ?? 1) == 0);

    public void Dispose()
    {
        if (DisposeFlag.IsFirstCall())
        {
            Semaphone._DisposeSafe();
            Semaphone = null;
        }
    }
}

public static class AsyncPreciseDelay
{
    static SortedList<long, AsyncManualResetEvent> WaitList = new SortedList<long, AsyncManualResetEvent>();

    static Stopwatch stopWatch;

    static Thread BackgroundThread;

    static AutoResetEvent ev = new AutoResetEvent(false);

    static List<Thread> WorkerThreadList = new List<Thread>();

    static Queue<AsyncManualResetEvent> QueuedManualResetEvents = new Queue<AsyncManualResetEvent>();

    static AutoResetEvent QueuedAutoResetEvents = new AutoResetEvent(false);

    static AsyncPreciseDelay()
    {
        stopWatch = new Stopwatch();
        stopWatch.Start();

        BackgroundThread = new Thread(BackgroundThreadProc);
        try
        {
            BackgroundThread.Priority = ThreadPriority.Highest;
        }
        catch { }
        BackgroundThread.IsBackground = true;
        BackgroundThread.Start();
    }

    static volatile int NumBusyWorkerThreads = 0;
    static volatile int NumWorkerThreads = 0;

    static void WorkerThreadProc()
    {
        while (true)
        {
            Interlocked.Increment(ref NumBusyWorkerThreads);
            while (true)
            {
                AsyncManualResetEvent? tcs = null;
                lock (QueuedManualResetEvents)
                {
                    if (QueuedManualResetEvents.Count != 0)
                    {
                        tcs = QueuedManualResetEvents.Dequeue();
                    }
                }

                if (tcs != null)
                {
                    tcs.Set();
                }
                else
                {
                    break;
                }
            }
            Interlocked.Decrement(ref NumBusyWorkerThreads);

            QueuedAutoResetEvents.WaitOne();
        }
    }

    static void FireWorkerThread(AsyncManualResetEvent tc)
    {
        if (NumBusyWorkerThreads == NumWorkerThreads)
        {
            Interlocked.Increment(ref NumWorkerThreads);
            Thread t = new Thread(WorkerThreadProc);
            t.IsBackground = true;
            t.Start();
            //Console.WriteLine($"num_worker_threads = {num_worker_threads}");
        }

        lock (QueuedManualResetEvents)
        {
            QueuedManualResetEvents.Enqueue(tc);
        }
        QueuedAutoResetEvents.Set();
    }

    static void BackgroundThreadProc()
    {
        //Benchmark b1 = new Benchmark("num_fired");
        //Benchmark b2 = new Benchmark("num_loop");
        //Benchmark b3 = new Benchmark("num_removed");
        while (true)
        {
            long now = Tick;
            long nextWaitTarget = -1;

            List<AsyncManualResetEvent> fireEventList = new List<AsyncManualResetEvent>();

            lock (WaitList)
            {
                List<long> pastTargetList = new List<long>();
                List<long> futureTargetList = new List<long>();

                foreach (long target in WaitList.Keys)
                {
                    if (now >= target)
                    {
                        pastTargetList.Add(target);
                        nextWaitTarget = 0;
                    }
                    else
                    {
                        futureTargetList.Add(target);
                    }
                }

                foreach (long target in pastTargetList)
                {
                    AsyncManualResetEvent e = WaitList[target];

                    WaitList.Remove(target);

                    fireEventList.Add(e);
                }

                if (nextWaitTarget == -1)
                {
                    if (WaitList.Count >= 1)
                    {
                        nextWaitTarget = WaitList.Keys[0];
                    }
                }
            }

            int n = 0;
            foreach (AsyncManualResetEvent tc in fireEventList)
            {
                //tc.TrySetResult(0);
                //Task.Factory.StartNew(() => tc.TrySetResult(0));
                FireWorkerThread(tc);
                n++;
                //b1.IncrementMe++;
            }
            //n.Print();
            //b2.IncrementMe++;

            now = Tick;
            long nextWaitTick = (Math.Max(nextWaitTarget - now, 0));
            if (nextWaitTarget == -1)
            {
                nextWaitTick = -1;
            }
            if (nextWaitTick >= 1 || nextWaitTick == -1)
            {
                if (nextWaitTick == -1 || nextWaitTick >= 100)
                {
                    nextWaitTick = 100;
                }
                ev.WaitOne((int)nextWaitTick);
            }
        }
    }

    public static long Tick
    {
        get
        {
            lock (stopWatch)
            {
                return stopWatch.ElapsedMilliseconds + 1L;
            }
        }
    }

    public static Task PreciseDelay(int msec)
    {
        if (msec == Timeout.Infinite)
        {
            return Task.Delay(Timeout.Infinite);
        }
        if (msec <= 0)
        {
            return Task.CompletedTask;
        }

        long targetTime = Tick + (long)msec;

        AsyncManualResetEvent? tc = null;

        bool setEvent = false;

        lock (WaitList)
        {
            long firstTargetBefore = -1;
            long firstTargetAfter = -1;

            if (WaitList.Count >= 1)
            {
                firstTargetBefore = WaitList.Keys[0];
            }

            if (WaitList.ContainsKey(targetTime) == false)
            {
                tc = new AsyncManualResetEvent();
                WaitList.Add(targetTime, tc);
            }
            else
            {
                tc = WaitList[targetTime];
            }

            firstTargetAfter = WaitList.Keys[0];

            if (firstTargetBefore != firstTargetAfter)
            {
                setEvent = true;
            }
        }

        if (setEvent)
        {
            ev.Set();
        }

        return tc.WaitAsync();
    }
}

[Flags]
public enum ExceptionWhen
{
    None = 0,
    TaskException = 1,
    CancelException = 2,
    TimeoutException = 4,
    All = 0x7FFFFFFF,
}
public static partial class TaskUtil
{
    static int NumPendingAsyncTasks = 0;

    const bool YieldOnStartDefault = true;

    public static int GetNumPendingAsyncTasks() => NumPendingAsyncTasks;

    public static async Task StartSyncTaskAsync(Action action, bool yieldOnStart = YieldOnStartDefault, bool leakCheck = true)
    { if (leakCheck) Interlocked.Increment(ref NumPendingAsyncTasks); try { if (yieldOnStart) await Task.Yield(); await Task.Factory.StartNew(action)._LeakCheck(!leakCheck); } finally { if (leakCheck) Interlocked.Decrement(ref NumPendingAsyncTasks); } }
    public static async Task<T> StartSyncTaskAsync<T>(Func<T> action, bool yieldOnStart = YieldOnStartDefault, bool leakCheck = true)
    { if (leakCheck) Interlocked.Increment(ref NumPendingAsyncTasks); try { if (yieldOnStart) await Task.Yield(); return await Task.Factory.StartNew(action)._LeakCheck(!leakCheck); } finally { if (leakCheck) Interlocked.Decrement(ref NumPendingAsyncTasks); } }

    public static async Task StartAsyncTaskAsync(Func<Task> action, bool yieldOnStart = YieldOnStartDefault, bool leakCheck = true)
    { if (leakCheck) Interlocked.Increment(ref NumPendingAsyncTasks); try { if (yieldOnStart) await Task.Yield(); await action()._LeakCheck(!leakCheck); } finally { if (leakCheck) Interlocked.Decrement(ref NumPendingAsyncTasks); } }
    public static async Task<T> StartAsyncTaskAsync<T>(Func<Task<T>> action, bool yieldOnStart = YieldOnStartDefault, bool leakCheck = true)
    { if (leakCheck) Interlocked.Increment(ref NumPendingAsyncTasks); try { if (yieldOnStart) await Task.Yield(); return await action()._LeakCheck(!leakCheck); } finally { if (leakCheck) Interlocked.Decrement(ref NumPendingAsyncTasks); } }

    public static async Task StartAsyncTaskSlowAsync(Func<Task, Task> action, Action<Task>? procRunBeforeTaskStart = null, bool leakCheck = true)
    {
        if (leakCheck) Interlocked.Increment(ref NumPendingAsyncTasks);

        try
        {
            Ref<Task> taskRef = new Ref<Task>();

            AsyncManualResetEvent taskRefSetEvent = new AsyncManualResetEvent();

            Task task = StartAsyncTaskAsync(async () =>
            {
                try
                {
                    await taskRefSetEvent.WaitAsync();

                    Task? currentTask = taskRef.Value;

#pragma warning disable CS4014 // この呼び出しは待機されなかったため、現在のメソッドの実行は呼び出しの完了を待たずに続行されます
                    currentTask._NullCheck("currentTask");
#pragma warning restore CS4014 // この呼び出しは待機されなかったため、現在のメソッドの実行は呼び出しの完了を待たずに続行されます

                    await action(currentTask)._LeakCheck(!leakCheck);
                }
                finally
                {
                    if (leakCheck) Interlocked.Decrement(ref NumPendingAsyncTasks);
                }
            },
            yieldOnStart: false, leakCheck: false);

            taskRef.Set(task);

            if (procRunBeforeTaskStart != null)
            {
                try
                {
                    procRunBeforeTaskStart(task);
                }
                catch (Exception ex)
                {
                    ex._Debug();
                }
            }

            taskRefSetEvent.Set(softly: false);

            await task;
        }
        catch
        {
            if (leakCheck) Interlocked.Decrement(ref NumPendingAsyncTasks);
            throw;
        }
    }

    public static Task<T> StartAsyncTaskAsync<T>(Func<Task, Task<T>> action, bool leakCheck = true)
    {
        if (leakCheck) Interlocked.Increment(ref NumPendingAsyncTasks);

        try
        {
            Ref<Task<T>> taskRef = new Ref<Task<T>>();

            AsyncManualResetEvent taskRefSetEvent = new AsyncManualResetEvent();

            Task<T> task = StartAsyncTaskAsync(async() =>
            {
                try
                {
                    await taskRefSetEvent.WaitAsync();
                    Task? currentTask = taskRef.Value;

#pragma warning disable CS4014 // この呼び出しは待機されなかったため、現在のメソッドの実行は呼び出しの完了を待たずに続行されます
                    currentTask._NullCheck("currentTask");
#pragma warning restore CS4014 // この呼び出しは待機されなかったため、現在のメソッドの実行は呼び出しの完了を待たずに続行されます

                    return await action(currentTask)._LeakCheck(!leakCheck);
                }
                finally
                {
                    if (leakCheck) Interlocked.Decrement(ref NumPendingAsyncTasks);
                }
            },
            yieldOnStart: false, leakCheck: false);

            taskRef.Set(task);

            taskRefSetEvent.Set(softly: false);

            return task;
        }
        catch
        {
            if (leakCheck) Interlocked.Decrement(ref NumPendingAsyncTasks);
            throw;
        }
    }

    public static async Task StartAsyncTaskAsync(Task task, bool yieldOnStart = YieldOnStartDefault, bool leakCheck = true)
    { if (leakCheck) Interlocked.Increment(ref NumPendingAsyncTasks); try { if (yieldOnStart) await Task.Yield(); await task._LeakCheck(!leakCheck); } finally { if (leakCheck) Interlocked.Decrement(ref NumPendingAsyncTasks); } }
    public static async Task<T> StartAsyncTaskAsync<T>(Task<T> task, bool yieldOnStart = YieldOnStartDefault, bool leakCheck = true)
    { if (leakCheck) Interlocked.Increment(ref NumPendingAsyncTasks); try { if (yieldOnStart) await Task.Yield(); return await task._LeakCheck(!leakCheck); } finally { if (leakCheck) Interlocked.Decrement(ref NumPendingAsyncTasks); } }


    public static async Task StartSyncTaskAsync(Action<object?> action, object? param, bool yieldOnStart = YieldOnStartDefault, bool leakCheck = true)
    { if (leakCheck) Interlocked.Increment(ref NumPendingAsyncTasks); try { if (yieldOnStart) await Task.Yield(); await Task.Factory.StartNew(action, param)._LeakCheck(!leakCheck); } finally { if (leakCheck) Interlocked.Decrement(ref NumPendingAsyncTasks); } }
    public static async Task<T> StartSyncTaskAsync<T>(Func<object?, T> action, object? param, bool yieldOnStart = YieldOnStartDefault, bool leakCheck = true)
    { if (leakCheck) Interlocked.Increment(ref NumPendingAsyncTasks); try { if (yieldOnStart) await Task.Yield(); return await Task.Factory.StartNew(action, param)._LeakCheck(!leakCheck); } finally { if (leakCheck) Interlocked.Decrement(ref NumPendingAsyncTasks); } }

    public static async Task StartAsyncTaskAsync(Func<object?, Task> action, object? param, bool yieldOnStart = YieldOnStartDefault, bool leakCheck = true)
    { if (leakCheck) Interlocked.Increment(ref NumPendingAsyncTasks); try { if (yieldOnStart) await Task.Yield(); await action(param)._LeakCheck(!leakCheck); } finally { if (leakCheck) Interlocked.Decrement(ref NumPendingAsyncTasks); } }
    public static async Task<T> StartAsyncTaskAsync<T>(Func<object?, Task<T>> action, object? param, bool yieldOnStart = YieldOnStartDefault, bool leakCheck = true)
    { if (leakCheck) Interlocked.Increment(ref NumPendingAsyncTasks); try { if (yieldOnStart) await Task.Yield(); return await action(param)._LeakCheck(!leakCheck); } finally { if (leakCheck) Interlocked.Decrement(ref NumPendingAsyncTasks); } }

    [MethodImpl(Inline)]
    public static void Sync(Action action) { action(); }

    [MethodImpl(Inline)]
    public static T Sync<T>(Func<T> func) { T ret = func(); return ret; }

    public static int WaitUntilAllPendingAsyncTasksFinish(int? timeout = null, CancellationToken cancel = default, int targetCount = 0)
    {
        int timeout2 = timeout ?? CoresConfig.TaskAsyncSettings.WaitTimeoutUntilPendingTaskFinish;

        WaitWithPoll(timeout2, 10, () => GetNumPendingAsyncTasks() <= targetCount, cancel);

        return GetNumPendingAsyncTasks();
    }

    public static bool WaitWithPoll(int timeout, int pollInterval, Func<bool> pollProc, CancellationToken cancel = default)
    {
        return WaitWithPoll(timeout, pollInterval, pollProc,
            interval => cancel.WaitHandle.WaitOne(interval));
    }

    public static bool WaitWithPoll(int timeout, int pollInterval, Func<bool> pollProc, Func<int, bool> waitProc)
    {
        long end_tick = Time.Tick64 + (long)timeout;

        if (timeout == Timeout.Infinite)
        {
            end_tick = long.MaxValue;
        }

        while (true)
        {
            long now = Time.Tick64;
            if (timeout != Timeout.Infinite)
            {
                if (now >= end_tick)
                {
                    return false;
                }
            }

            long next_wait = (end_tick - now);
            next_wait = Math.Min(next_wait, (long)pollInterval);
            next_wait = Math.Max(next_wait, 1);

            if (pollProc != null)
            {
                if (pollProc())
                {
                    return true;
                }
            }

            if (waitProc((int)next_wait))
            {
                return true;
            }
        }
    }

    public static async Task<bool> AwaitWithPollAsync(int timeout, int pollInterval, Func<bool> pollProc, CancellationToken cancel = default, bool randInterval = false)
    {
        long end_tick = Time.Tick64 + (long)timeout;

        if (timeout == Timeout.Infinite)
        {
            end_tick = long.MaxValue;
        }

        while (true)
        {
            long now = Time.Tick64;
            if (timeout != Timeout.Infinite)
            {
                if (now >= end_tick)
                {
                    return false;
                }
            }

            long next_wait = (end_tick - now);
            int val = randInterval ? Util.GenRandInterval(pollInterval) : pollInterval;
            next_wait = Math.Min(next_wait, (long)val);
            next_wait = Math.Max(next_wait, 1);

            if (pollProc != null)
            {
                if (pollProc())
                {
                    return true;
                }
            }

            if (await cancel._WaitUntilCanceledAsync((int)next_wait))
            {
                // Canceled
                return false;
            }
        }
    }

    public static async Task<bool> AwaitWithPollAsync(int timeout, int pollInterval, Func<Task<bool>> pollProc, CancellationToken cancel = default, bool randInterval = false)
    {
        long end_tick = Time.Tick64 + (long)timeout;

        if (timeout == Timeout.Infinite)
        {
            end_tick = long.MaxValue;
        }

        while (true)
        {
            long now = Time.Tick64;
            if (timeout != Timeout.Infinite)
            {
                if (now >= end_tick)
                {
                    return false;
                }
            }

            long next_wait = (end_tick - now);
            int val = randInterval ? Util.GenRandInterval(pollInterval) : pollInterval;
            next_wait = Math.Min(next_wait, (long)val);
            next_wait = Math.Max(next_wait, 1);

            if (pollProc != null)
            {
                if (await pollProc())
                {
                    return true;
                }
            }

            if (await cancel._WaitUntilCanceledAsync((int)next_wait))
            {
                // Canceled
                return false;
            }
        }
    }

    // ForEach 非同期実行。1 つでも失敗したら例外を発生させ、すべてキャンセルする
    public static async Task ForEachAsync<T>(int maxConcurrentTasks, IEnumerable<T> items, Func<T, int, CancellationToken, Task> func, CancellationToken cancel = default, int intervalBetweenTasks = 0)
    {
        AsyncConcurrentTask at = new AsyncConcurrentTask(maxConcurrentTasks);

        await using var cancel2 = new CancelWatcher(cancel);

        List<Exception> exceptionList = new List<Exception>();

        RefBool hasError = false;

        int taskIndex = 0;

        foreach (var item in items)
        {
            var task = await at.StartTaskAsync<int, int>(async (taskIndex2, c) =>
             {
                 try
                 {
                     await Task.Yield();
                     await func(item, taskIndex2, c);
                     if (intervalBetweenTasks >= 1)
                     {
                         await cancel2.CancelToken._WaitUntilCanceledAsync(intervalBetweenTasks);
                     }
                 }
                 catch (Exception ex)
                 {
                     lock (exceptionList)
                         exceptionList.Add(ex);

                     hasError.Set(true);
                     cancel2.Cancel();
                 }
                 return 0;
             },
            taskIndex,
            cancel2);

            if (hasError)
            {
                break;
            }

            taskIndex++;
        }

        await at.WaitAllTasksFinishAsync();

        if (hasError)
        {
            lock (exceptionList)
                throw new AggregateException(exceptionList.ToArray());
        }
    }

    public static void ForEach<T>(int maxConcurrentTasks, IEnumerable<T> items, Func<T, int, CancellationToken, Task> func, CancellationToken cancel = default, int intervalBetweenTasks = 0)
        => ForEachAsync(maxConcurrentTasks, items, func, cancel, intervalBetweenTasks)._GetResult();

    public static int GetMinTimeout(params int[] values)
    {
        long minValue = long.MaxValue;
        foreach (int v in values)
        {
            long vv;
            if (v < 0)
                vv = long.MaxValue;
            else
                vv = v;
            minValue = Math.Min(minValue, vv);
        }
        if (minValue == long.MaxValue)
            return Timeout.Infinite;
        else
            return (int)minValue;
    }

    public static async Task<T> FlushOtherStreamIfRecvPendingAsync<T>(Task<T> recvTask, PipeStream otherStream)
    {
        Task<T> task = recvTask;

        if (task.IsCanceled || task.IsCompleted || task.IsFaulted)
        {
            return task.Result;
        }
        else
        {
            otherStream.FastFlush(true, true);

            return await task;
        }
    }

    public static async Task<TResult> DoAsyncWithTimeout<TResult>(Func<CancellationToken, Task<TResult>> mainProc, Action? cancelProc = null, int timeout = Timeout.Infinite, CancellationToken cancel = default, params CancellationToken[] cancelTokens)
    {
        if (timeout < 0) timeout = Timeout.Infinite;
        if (timeout == 0) throw new TimeoutException("timeout == 0");

        if (timeout == Timeout.Infinite && cancel.CanBeCanceled == false && (cancelTokens == null || cancelTokens.Length == 0))
            return await mainProc(default);

        List<Task> waitTasks = new List<Task>();
        List<IDisposable> disposes = new List<IDisposable>();
        Task? timeoutTask = null;
        CancellationTokenSource? timeoutCancelSources = null;
        CancellationTokenSource cancelLocal = new CancellationTokenSource();

        if (timeout != Timeout.Infinite)
        {
            timeoutCancelSources = new CancellationTokenSource();
            timeoutTask = Task.Delay(timeout, timeoutCancelSources.Token);
            disposes.Add(timeoutCancelSources);

            waitTasks.Add(timeoutTask);
        }

        try
        {
            if (cancel.CanBeCanceled)
            {
                cancel.ThrowIfCancellationRequested();

                Task t = WhenCanceled(cancel, out CancellationTokenRegistration reg);
                disposes.Add(reg);
                waitTasks.Add(t);
            }

            foreach (CancellationToken c in cancelTokens)
            {
                if (c.CanBeCanceled)
                {
                    c.ThrowIfCancellationRequested();

                    Task t = WhenCanceled(c, out CancellationTokenRegistration reg);
                    disposes.Add(reg);
                    waitTasks.Add(t);
                }
            }

            Task<TResult> procTask = mainProc(cancelLocal.Token);

            if (procTask.IsCompleted)
            {
                return procTask._GetResult();
            }

            waitTasks.Add(procTask);

            await Task.WhenAny(waitTasks.ToArray());

            foreach (CancellationToken c in cancelTokens)
            {
                c.ThrowIfCancellationRequested();
            }

            cancel.ThrowIfCancellationRequested();

            if (procTask.IsCompleted)
            {
                return procTask._GetResult();
            }

            throw new TimeoutException();
        }
        catch
        {
            try
            {
                cancelLocal.Cancel();
            }
            catch { }
            try
            {
                if (cancelProc != null) cancelProc();
            }
            catch
            {
            }
            throw;
        }
        finally
        {
            if (timeoutCancelSources != null)
            {
                try
                {
                    timeoutCancelSources.Cancel();
                }
                catch
                {
                }
            }
            foreach (IDisposable i in disposes)
            {
                i._DisposeSafe();
            }
        }
    }

    static readonly Type? TimerQueueType = Type.GetType("System.Threading.TimerQueue");
    static readonly FieldReaderWriter? TimerQueueReaderWriter = TimerQueueType != null ? new FieldReaderWriter(TimerQueueType, true) : null;

    static readonly Type? TimerQueueTimerType = Type.GetType("System.Threading.TimerQueueTimer");
    static readonly FieldInfo[]? TimerQueueTimersFieldList = TimerQueueReaderWriter != null ? TimerQueueReaderWriter.MetadataTable.Values.OfType<FieldInfo>().Where(x => x.FieldType == TimerQueueTimerType).ToArray() : null;

    static bool FailedFlag_GetScheduledTimersCount = false;

    static readonly FieldInfo? TimerQueueTimer_Next_FieldInfo1 = TimerQueueTimerType != null ? TimerQueueTimerType.GetField("m_next", BindingFlags.Instance | BindingFlags.NonPublic) : null; // .NET Core 2.x
    static readonly FieldInfo? TimerQueueTimer_Next_FieldInfo2 = TimerQueueTimerType != null ? TimerQueueTimerType.GetField("_next", BindingFlags.Instance | BindingFlags.NonPublic) : null; // .NET Core 3.x

    public static int GetScheduledTimersCount(int failDefaultValue = -1)
    {
#pragma warning disable CS0162 // 到達できないコードが検出されました
        if (FailedFlag_GetScheduledTimersCount) return failDefaultValue;
#pragma warning restore CS0162 // 到達できないコードが検出されました

        if (TimerQueueType == null || TimerQueueTimersFieldList == null)
        {
            FailedFlag_GetScheduledTimersCount = true;
            return failDefaultValue;
        }

        FieldInfo? Timer_Next_Field_info = TimerQueueTimer_Next_FieldInfo1;
        if (Timer_Next_Field_info == null) Timer_Next_Field_info = TimerQueueTimer_Next_FieldInfo2;

        if (Timer_Next_Field_info == null)
        {
            FailedFlag_GetScheduledTimersCount = true;
            return failDefaultValue;
        }

        bool fatalError = true;

        try
        {
            int num = 0;

            Array? timerQueueInstanceList = (Array?)TimerQueueType.GetProperty("Instances", BindingFlags.Static | BindingFlags.Public)?.GetValue(null);

            if (timerQueueInstanceList == null)
            {
                FailedFlag_GetScheduledTimersCount = true;
                return failDefaultValue;
            }

            foreach (object? timerQueueInstance in timerQueueInstanceList)
            {
                if (timerQueueInstance != null)
                {
                    foreach (FieldInfo timerField in TimerQueueTimersFieldList)
                    {
                        object? timer = timerField.GetValue(timerQueueInstance);

                        if (timer != null)
                        {
                            // 2020/9/14 万一のデッドロックのおそれがあるため、ロック取得はしないようにした
                            //lock (timerQueueInstance)
                            {
                                int c = 0;

                                while (timer != null)
                                {
                                    timer = Timer_Next_Field_info.GetValue(timer);
                                    fatalError = false;
                                    num++;

                                    c++;
                                    if (c >= 100000)
                                    {
                                        // Infinite loop?
                                        break;
                                    }
                                }
                            }
                        }
                    }
                }
            }

            return num;
        }
        catch
        {
            if (fatalError)
            {
                FailedFlag_GetScheduledTimersCount = true;
            }
            return failDefaultValue;
        }
    }

    public static int GetQueuedTasksCount(int failDefaultValue = 0)
    {
        try
        {
            FieldInfo? workQueueInfo = null;
            Type? threadPoolInfo = Type.GetType("System.Threading.ThreadPool");
            Type? threadPoolGlobalsInfo = Type.GetType("System.Threading.ThreadPoolGlobals");

            if (threadPoolGlobalsInfo != null)
            {
                workQueueInfo = threadPoolGlobalsInfo.GetField("workQueue");
            }

            if (workQueueInfo == null)
            {
                if (threadPoolInfo != null)
                {
                    workQueueInfo = threadPoolInfo.GetField("s_workQueue", BindingFlags.Static | BindingFlags.NonPublic);
                }
            }

            if (workQueueInfo == null) return failDefaultValue;

            object? o = workQueueInfo.GetValue(null);
            if (o == null) return failDefaultValue;

            Type? t2 = o.GetType();
            if (t2 == null) return failDefaultValue;

            FieldInfo? f2 = t2.GetField("workItems", BindingFlags.Instance | BindingFlags.NonPublic);
            if (f2 == null) return failDefaultValue;

            object? o2 = f2.GetValue(o);
            if (o2 == null) return failDefaultValue;

            Type? t3 = o2.GetType();
            if (t3 == null) return failDefaultValue;

            PropertyInfo? f3 = t3.GetProperty("Count");
            if (f3 == null) return failDefaultValue;

            int ret = (int)(f3.GetValue(o2) ?? failDefaultValue);

            return ret;
        }
        catch
        {
            return failDefaultValue;
        }
    }

    public static Task PreciseDelay(int msec)
    {
        return AsyncPreciseDelay.PreciseDelay(msec);
    }

    public static async Task<ExceptionWhen> WaitObjectsAsync(IEnumerable<Task>? tasks = null, IEnumerable<CancellationToken>? cancels = null, IEnumerable<AsyncAutoResetEvent?>? events = null,
        IEnumerable<AsyncManualResetEvent?>? manualEvents = null, int timeout = Timeout.Infinite,
        ExceptionWhen exceptions = ExceptionWhen.None, LeakCounterKind leakCounterKind = LeakCounterKind.WaitObjectsAsync)
    {
        LeakChecker.IncrementLeakCounter(leakCounterKind);
        try
        {
            if (tasks == null) tasks = new Task[0];
            if (cancels == null) cancels = new CancellationToken[0];
            if (events == null) events = new AsyncAutoResetEvent[0];
            if (manualEvents == null) manualEvents = new AsyncManualResetEvent[0];
            if (timeout == 0)
            {
                if (exceptions.Bit(ExceptionWhen.TimeoutException))
                    throw new TimeoutException();

                return ExceptionWhen.TimeoutException;
            }

            if (exceptions.Bit(ExceptionWhen.TaskException))
            {
                foreach (Task t in tasks)
                {
                    if (t != null)
                    {
                        if (t.IsFaulted) t.Exception._ReThrow();
                        if (t.IsCanceled) throw new TaskCanceledException();
                    }
                }
            }
            else
            {
                foreach (Task t in tasks)
                {
                    if (t != null)
                    {
                        if (t.IsFaulted) return ExceptionWhen.TaskException;
                        if (t.IsCanceled) return ExceptionWhen.TaskException;
                    }
                }
            }

            if (exceptions.Bit(ExceptionWhen.CancelException))
            {
                foreach (CancellationToken c in cancels)
                    c.ThrowIfCancellationRequested();
            }
            else
            {
                foreach (CancellationToken c in cancels)
                    if (c.IsCancellationRequested)
                        return ExceptionWhen.CancelException;
            }

            List<Task> taskList = new List<Task>();
            List<CancellationTokenRegistration> regList = new List<CancellationTokenRegistration>();
            List<Action> undoList = new List<Action>();

            foreach (Task t in tasks)
            {
                if (t != null)
                {
                    taskList.Add(t);
                }
            }

            foreach (CancellationToken c in cancels)
            {
                taskList.Add(WhenCanceled(c, out CancellationTokenRegistration reg));
                regList.Add(reg);
            }

            foreach (AsyncAutoResetEvent? ev in events)
            {
                if (ev != null)
                {
                    taskList.Add(ev.WaitOneAsync(out Action undo));
                    undoList.Add(undo);
                }
            }

            foreach (AsyncManualResetEvent? ev in manualEvents)
            {
                if (ev != null)
                {
                    taskList.Add(ev.WaitAsync());
                }
            }

            CancellationTokenSource delayCancel = new CancellationTokenSource();

            Task? timeoutTask = null;
            bool timedOut = false;

            if (timeout >= 1)
            {
                timeoutTask = Task.Delay(timeout, delayCancel.Token);
                taskList.Add(timeoutTask);
            }

            try
            {
                Task r = await Task.WhenAny(taskList.ToArray());
                if (r == timeoutTask) timedOut = true;
            }
            catch { }

            foreach (Action undo in undoList)
                undo();

            foreach (CancellationTokenRegistration reg in regList)
            {
                reg.Dispose();
            }

            if (delayCancel != null)
            {
                delayCancel.Cancel();
                delayCancel.Dispose();
            }

            if (exceptions.Bit(ExceptionWhen.TimeoutException))
            {
                if (timedOut)
                    throw new TimeoutException();
            }
            else
            {
                if (timedOut)
                    return ExceptionWhen.TimeoutException;
            }

            if (exceptions.Bit(ExceptionWhen.TaskException))
            {
                foreach (Task t in tasks)
                {
                    if (t != null)
                    {
                        if (t.IsFaulted) t.Exception._ReThrow();
                        if (t.IsCanceled) throw new TaskCanceledException();
                    }
                }
            }
            else
            {
                foreach (Task t in tasks)
                {
                    if (t != null)
                    {
                        if (t.IsFaulted) return ExceptionWhen.TaskException;
                        if (t.IsCanceled) return ExceptionWhen.TaskException;
                    }
                }
            }

            if (exceptions.Bit(ExceptionWhen.CancelException))
            {
                foreach (CancellationToken c in cancels)
                    c.ThrowIfCancellationRequested();
            }
            else
            {
                foreach (CancellationToken c in cancels)
                    if (c.IsCancellationRequested)
                        return ExceptionWhen.CancelException;
            }

            return ExceptionWhen.None;
        }
        finally
        {
            LeakChecker.DecrementLeakCounter(leakCounterKind);
        }
    }

    public static Task WhenCanceled(CancellationToken cancel, out CancellationTokenRegistration registration)
    {
        TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>(CoresConfig.TaskAsyncSettings.AsyncEventTaskCreationOption);

        registration = cancel.Register(() =>
        {
            Task.Factory.StartNew(() =>
            {
                tcs.SetResult(true);
            });
        });

        return tcs.Task;
    }

    public static TaskVm<TResult, TIn> GetCurrentTaskVm<TResult, TIn>()
    {
        TaskVm<TResult, TIn>.TaskVmSynchronizationContext ctx = (TaskVm<TResult, TIn>.TaskVmSynchronizationContext)SynchronizationContext.Current!;

        return ctx.Vm;
    }

    public static async Task CancelAsync(CancellationTokenSource cts, bool throwOnFirstException = false)
    {
        await Task.Run(() => cts.Cancel(throwOnFirstException));
    }

    public static async Task TryCancelAsync(CancellationTokenSource? cts)
    {
        await Task.Run(() => TryCancel(cts));
    }

    public static void TryCancel(CancellationTokenSource? cts)
    {
        try
        {
            cts?.Cancel();
        }
        catch
        {
        }
    }

    public static void TryCancelNoBlock(CancellationTokenSource? cts)
        => cts._TryCancelAsync()._LaissezFaire(true);

    public static async Task<T> TryWaitAsync<T>(Task<T>? t, bool noDebugMessage = false)
    {
        if (t == null) return default!;
        try
        {
            return await t;
        }
        catch (Exception ex)
        {
            if (noDebugMessage == false)
                Dbg.WriteLine("Task exception: " + ex._GetSingleException().ToString());

            return default!;
        }
    }

    public static async Task TryWaitAsync(Task? t, bool noDebugMessage = false)
    {
        if (t == null) return;
        try
        {
            await t;
        }
        catch (Exception ex)
        {
            if (noDebugMessage == false)
                Dbg.WriteLine("Task exception: " + ex._GetSingleException().ToString());
        }
    }

    public static void TryWait(Task? t, bool noDebugMessage = false)
    {
        if (t == null) return;
        try
        {
            t._GetResult();
        }
        catch (Exception ex)
        {
            if (noDebugMessage == false)
                Dbg.WriteLine("Task exception: " + ex.ToString());
        }
    }

    public static CancellationToken CurrentTaskVmGracefulCancel => (CancellationToken)ThreadLocalStorage.CurrentThreadData["taskvm_current_graceful_cancel"];

    public static ValueHolder EnterCriticalCounter(RefInt counter)
    {
        counter.Increment();
        return new ValueHolder(() =>
        {
            counter.Decrement();
        },
        LeakCounterKind.EnterCriticalCounter);
    }

    public static async Task<int> DoMicroReadOperations(Func<Memory<byte>, long, CancellationToken, Task<int>> microWriteOperation, Memory<byte> data, int maxSingleSize,
        long startPosition, CancellationToken cancel = default, long fixedTotalSize = -1)
    {
        checked
        {
            if (data.Length == 0) return 0;

            maxSingleSize = Math.Max(maxSingleSize, 1);
            int totalProcessedSize = 0;

            if (fixedTotalSize >= 0)
            {
                long remainSize = Math.Max(fixedTotalSize - startPosition, 0);
                if (remainSize == 0) return 0;

                if (data.Length > remainSize)
                {
                    data = data.Slice(0, (int)remainSize);
                }
            }

            while (data.Length >= 1)
            {
                cancel.ThrowIfCancellationRequested();

                int targetSize = Math.Min(maxSingleSize, data.Length);
                Memory<byte> target = data.Slice(0, targetSize);

                int r = await microWriteOperation(target, startPosition + totalProcessedSize, cancel);

                if (r < 0)
                    throw new ApplicationException($"microWriteOperation returned '{r}'.");

                if (r == 0)
                    break;

                data = data.Slice(r);

                totalProcessedSize += r;
            }

            return totalProcessedSize;
        }
    }

    public static async Task DoMicroWriteOperations(Func<ReadOnlyMemory<byte>, long, CancellationToken, Task> microReadOperation, ReadOnlyMemory<byte> data, int maxSingleSize, long currentPosition, CancellationToken cancel = default)
    {
        checked
        {
            if (data.Length == 0) return;
            maxSingleSize = Math.Max(maxSingleSize, 1);
            int totalSize = 0;

            while (data.Length >= 1)
            {
                cancel.ThrowIfCancellationRequested();

                int targetSize = Math.Min(maxSingleSize, data.Length);
                ReadOnlyMemory<byte> target = data.Slice(0, targetSize);
                data = data.Slice(targetSize);

                await microReadOperation(target, currentPosition + totalSize, cancel);

                totalSize += targetSize;
            }
        }
    }

    class CombinedCancelContext
    {
        public CancellationTokenSource Cts = new CancellationTokenSource();
        public List<CancellationTokenRegistration> RegList = new List<CancellationTokenRegistration>();
    }

    public static AsyncHolder<object> CreateCombinedCancellationToken(out CancellationToken combinedToken, params CancellationToken[] cancels)
    {
        if (cancels == null)
        {
            combinedToken = default;
            return new AsyncHolder<object>(null, null, LeakCounterKind.CreateCombinedCancellationToken);
        }

        cancels = cancels.Where(x => x.CanBeCanceled).ToArray();

        if (cancels.Length == 0)
        {
            combinedToken = default;
            return new AsyncHolder<object>(null, null, LeakCounterKind.CreateCombinedCancellationToken);
        }

        if (cancels.Length == 1)
        {
            combinedToken = cancels[0];
            return new AsyncHolder<object>(null, null, LeakCounterKind.CreateCombinedCancellationToken);
        }

        foreach (CancellationToken c in cancels)
        {
            if (c.IsCancellationRequested)
            {
                combinedToken = new CancellationToken(true);
                return new AsyncHolder<object>(null, null, LeakCounterKind.CreateCombinedCancellationToken);
            }
        }

        CombinedCancelContext ctx = new CombinedCancelContext();

        foreach (CancellationToken c in cancels)
        {
            if (c != default)
            {
                var reg = c.Register(() =>
                {
                    ctx.Cts.Cancel();
                });

                ctx.RegList.Add(reg);
            }
        }

        combinedToken = ctx.Cts.Token;

        return new AsyncHolder<object>(async obj =>
        {
            CombinedCancelContext x = (CombinedCancelContext)obj;

            foreach (var reg in x.RegList)
            {
                await reg._DisposeSafeAsync();
            }
        },
        ctx, LeakCounterKind.CreateCombinedCancellationToken);
    }

    public static async Task<T> RetryAsync<T>(Func<Task<T>> proc, int retryInterval, int tryCount, CancellationToken cancel = default, bool randomInterval = false, bool onlyRetryableException = false, bool noDebugMessage = false)
    {
        RetryHelper<T> retry = new RetryHelper<T>(retryInterval, tryCount, randomInterval, onlyRetryableException: onlyRetryableException, noDebugMessage: noDebugMessage);
        return await retry.RunAsync(proc, cancel: cancel);
    }

    public static async Task<T> RetryAsync<T>(Func<CancellationToken, Task<T>> proc, int retryInterval, int tryCount, CancellationToken cancel = default, bool randomInterval = false, bool onlyRetryableException = false, bool noDebugMessage = false)
    {
        RetryHelper<T> retry = new RetryHelper<T>(retryInterval, tryCount, randomInterval, onlyRetryableException: onlyRetryableException, noDebugMessage: noDebugMessage);
        return await retry.RunAsync(proc, cancel: cancel);
    }

    public static async Task ForEachExAsync<T>(IEnumerable<T> targetList, Func<T, CancellationToken, Task> eachProc, int maxConcurrent = int.MaxValue,
        ForEachExAsyncFlags flags = ForEachExAsyncFlags.None, CancellationToken cancel = default)
    {
        maxConcurrent = Math.Max(maxConcurrent, 1);

        CancellationTokenSource ctsOnError = new CancellationTokenSource();

        await using (TaskUtil.CreateCombinedCancellationToken(out CancellationToken combinedCancel, cancel, ctsOnError.Token))
        {
            using (SemaphoreSlim sem = new SemaphoreSlim(maxConcurrent, maxConcurrent))
            {
                List<Task> taskList = new List<Task>();

                foreach (T t in targetList)
                {
                    Task task = TaskUtil.StartAsyncTaskAsync(async (obj) =>
                    {
                        await sem.WaitAsync(combinedCancel);

                        combinedCancel.ThrowIfCancellationRequested();

                        try
                        {
                            await Task.Yield();
                            await eachProc((T)obj!, combinedCancel);
                        }
                        catch
                        {
                            if (flags.Bit(ForEachExAsyncFlags.CancelAllIfOneFail))
                            {
                                ctsOnError._TryCancelNoBlock();
                            }
                            throw;
                        }
                        finally
                        {
                            sem.Release();
                        }
                    }, t, false, false);

                    taskList.Add(task);
                }

                Task ret = Task.WhenAll(taskList);

                await ret;
            }
        }
    }
}

[Flags]
public enum ForEachExAsyncFlags
{
    None = 0,
    CancelAllIfOneFail = 1,
}

public class AsyncCallbackList
{
    List<(Action<object?> action, object? state)> HardCallbackList = new List<(Action<object?> action, object? state)>();
    List<(Action<object?> action, object? state)> SoftCallbackList = new List<(Action<object?> action, object? state)>();

    public void AddHardCallback(Action<object?> action, object? state = null)
    {
        lock (HardCallbackList)
            HardCallbackList.Add((action, state));
    }

    public void AddSoftCallback(Action<object?> action, object? state = null)
    {
        lock (SoftCallbackList)
            SoftCallbackList.Add((action, state));
    }

    public void Invoke()
    {
        (Action<object?> action, object? state)[] arrayCopy;

        if (HardCallbackList.Count >= 1)
        {
            lock (HardCallbackList)
            {
                arrayCopy = HardCallbackList.ToArray();
            }
            foreach (var v in arrayCopy)
            {
                try
                {
                    v.action(v.state);
                }
                catch { }
            }
        }

        if (SoftCallbackList.Count >= 1)
        {
            lock (SoftCallbackList)
            {
                arrayCopy = SoftCallbackList.ToArray();
            }
            foreach (var v in arrayCopy)
            {
                try
                {
                    Task.Factory.StartNew(() =>
                    {
                        try
                        {
                            v.action(v.state);
                        }
                        catch { }
                    });
                }
                catch { }
            }
        }
    }
}

public class AsyncAutoResetEvent
{
    object lockobj = new object();
    List<AsyncManualResetEvent> eventQueue = new List<AsyncManualResetEvent>();
    volatile bool isSet = false;
    volatile int stateVersion = 1;

    public int StateVersion
    {
        get
        {
            lock (lockobj)
            {
                return this.stateVersion;
            }
        }
    }

    public bool IsSet
    {
        get
        {
            lock (lockobj)
            {
                return this.isSet;
            }
        }
    }

    public AsyncCallbackList CallbackList { get; } = new AsyncCallbackList();

    public AsyncAutoResetEvent(bool initialState = false)
    {
        if (initialState)
        {
            this.Set();
        }
    }

    public async Task<bool> WaitOneAsync(int timeout = Timeout.Infinite, CancellationToken cancel = default, LeakCounterKind leakCounterKind = LeakCounterKind.WaitObjectsAsync)
    {
        try
        {
            var reason = await TaskUtil.WaitObjectsAsync(cancels: cancel._SingleArray(),
                events: this._SingleArray(),
                timeout: timeout,
                exceptions: ExceptionWhen.None,
                leakCounterKind: leakCounterKind);

            return (reason != ExceptionWhen.None);
        }
        catch
        {
            return false;
        }
    }

    public Task WaitOneAsync(out Action cancel)
    {
        lock (lockobj)
        {
            if (isSet)
            {
                Interlocked.Increment(ref this.stateVersion);

                isSet = false;
                cancel = () => { };
                return Task.CompletedTask;
            }

            AsyncManualResetEvent e = new AsyncManualResetEvent();

            Task ret = e.WaitAsync();

            eventQueue.Add(e);

            cancel = () =>
            {
                lock (lockobj)
                {
                    eventQueue.Remove(e);
                }
            };

            return ret;
        }
    }

    volatile int lazyQueuedSet = 0;

    public void SetLazyEnqueue() => Interlocked.Exchange(ref lazyQueuedSet, 1);

    public void SetIfLazyQueued(bool softly = false)
    {
        if (Interlocked.CompareExchange(ref lazyQueuedSet, 0, 1) == 1)
        {
            Set(softly);
        }
    }

    public void Set(bool softly = false)
    {
        AsyncManualResetEvent? ev = null;
        lock (lockobj)
        {
            Interlocked.Increment(ref this.stateVersion);

            if (eventQueue.Count >= 1)
            {
                ev = eventQueue[eventQueue.Count - 1];
                eventQueue.Remove(ev);
            }

            if (ev == null)
            {
                isSet = true;
            }
        }

        if (ev != null)
        {
            ev.Set(softly);
        }

        CallbackList.Invoke();
    }
}

public class AsyncManualResetEvent
{
    object lockobj = new object();
    volatile TaskCompletionSource<bool> tcs = null!;
    bool isSet = false;

    public AsyncCallbackList CallbackList { get; } = new AsyncCallbackList();

    public AsyncManualResetEvent()
    {
        init();
    }

    void init()
    {
        this.tcs = new TaskCompletionSource<bool>(CoresConfig.TaskAsyncSettings.AsyncEventTaskCreationOption);
    }

    public bool IsSet
    {
        get
        {
            return this.isSet;
        }
    }

    public async Task<bool> WaitAsync(int timeout, CancellationToken cancel = default, LeakCounterKind leakCounterKind = LeakCounterKind.WaitObjectsAsync)
    {
        try
        {
            var reason = await TaskUtil.WaitObjectsAsync(cancels: cancel._SingleArray(),
                manualEvents: this._SingleArray(),
                timeout: timeout,
                leakCounterKind: leakCounterKind);

            return (reason == ExceptionWhen.None);
        }
        catch
        {
            return false;
        }
    }
    public bool Wait(int timeout, CancellationToken cancel = default, LeakCounterKind leakCounterKind = LeakCounterKind.WaitObjectsAsync)
        => WaitAsync(timeout, cancel, leakCounterKind)._GetResult();

    public Task<bool> WaitAsync()
    {
        lock (lockobj)
        {
            if (isSet)
            {
                return Task<bool>.FromResult(true);
            }
            else
            {
                return tcs.Task;
            }
        }
    }
    public bool Wait() => WaitAsync()._GetResult();

    public void Set(bool softly = false)
    {
        if (softly)
        {
            if (isSet == false)
            {
                Task.Factory.StartNew(() => Set(false));
            }
        }
        else
        {
            bool flag = false;

            lock (lockobj)
            {
                if (isSet == false)
                {
                    isSet = true;
                    flag = true;
                }
            }

            if (flag)
            {
                tcs.TrySetResult(true);

                this.CallbackList.Invoke();
            }
        }
    }

    public void Reset()
    {
        lock (lockobj)
        {
            if (isSet)
            {
                isSet = false;
                init();
            }
        }
    }
}

public interface IAsyncService : IDisposable, IAsyncDisposable
{
    Task CleanupAsync(Exception? ex = null);
    Task DisposeWithCleanupAsync(Exception? ex = null);
    Task CancelAsync(Exception? ex = null);
    void Dispose(Exception? ex = null);
    ValueTask DisposeAsync(Exception? ex = null);
}

#pragma warning disable CA1063 // Implement IDisposable Correctly
public abstract class AsyncService : IAsyncService, IAsyncDisposable
#pragma warning restore CA1063 // Implement IDisposable Correctly
{
    static long IdSeed = 0;


    public CancelWatcher CancelWatcher { get; }
    public CancellationToken GrandCancel { get => CancelWatcher.CancelToken; }

    readonly RefInt CriticalCounter = new RefInt();

    readonly List<Action> OnDisposeList = new List<Action>();
    readonly List<Func<Task>> OnCancelList = new List<Func<Task>>();

    readonly List<IAsyncService> DirectDisposeList = new List<IAsyncService>();
    readonly List<IAsyncService> IndirectDisposeList = new List<IAsyncService>();

    readonly CriticalSection LockObj = new CriticalSection<AsyncService>();

    public long AsyncServiceId { get; }
    public string AsyncServiceObjectName { get; }

    protected AsyncService(CancellationToken cancel = default)
    {
        this.AsyncServiceId = Interlocked.Increment(ref IdSeed);
        this.AsyncServiceObjectName = this.ToString() ?? "null";

        this.CancelWatcher = new CancelWatcher(cancel);

        this.CancelWatcher.AsyncEventList.RegisterCallback(CanceledCallbackAsync);
    }

    public async Task AddOnCancelAction(Func<Task> proc)
    {
        if (proc != null)
        {
            bool callNow = false;

            lock (LockObj)
            {
                if (this.IsCanceled)
                {
                    callNow = true;
                }
                else
                {
                    this.OnCancelList.Add(proc);
                }
            }

            if (callNow)
            {
                // 既にキャンセルされているので今すぐ呼び出す
                try
                {
                    await proc();
                }
                catch { }
            }
        }
    }

    public void AddOnDisposeAction(Action proc)
    {
        if (proc != null)
        {
            bool callNow = false;

            lock (LockObj)
            {
                if (this.Disposed.IsSet)
                {
                    callNow = true;
                }
                else
                {
                    this.OnDisposeList.Add(proc);
                }
            }

            if (callNow)
            {
                // 既に Dispose されているので今すぐ呼び出す
                try
                {
                    proc();
                }
                catch { }
            }
        }
    }

    [return: NotNullIfNotNull("service")]
    public T? AddDirectDisposeLink<T>(T? service) where T : class, IAsyncService
    {
        if (service != null)
        {
            bool disposeNow = false;

            lock (DirectDisposeList)
            {
                if (this.IsCanceled || this.IsDisposed || this.IsCleanuped)
                {
                    disposeNow = true;
                }
                else
                {
                    DirectDisposeList.Add(service);
                }
            }

            if (disposeNow)
            {
                // すぐに Dispose する (安全のため非同期に実行する)
                TaskUtil.StartSyncTaskAsync(() => service._DisposeSafe())._LaissezFaire(noDebugMessage: true);
            }
        }
        return service;
    }

    [return: NotNullIfNotNull("service")]
    public T? AddIndirectDisposeLink<T>(T? service) where T : class, IAsyncService
    {
        if (service != null)
        {
            bool disposeNow = false;

            lock (IndirectDisposeList)
            {
                if (this.IsCanceled || this.IsDisposed || this.IsCleanuped)
                {
                    disposeNow = true;
                }
                else
                {
                    IndirectDisposeList.Add(service);
                }
            }

            if (disposeNow)
            {
                // すぐに Dispose する (安全のため非同期に実行する)
                TaskUtil.StartSyncTaskAsync(() => service._DisposeSafe())._LaissezFaire(noDebugMessage: true);
            }
        }
        return service;
    }

    IAsyncService[] GetDirectDisposeLinkList()
    {
        lock (DirectDisposeList)
            return DirectDisposeList.ToArray();
    }

    IAsyncService[] GetIndirectDisposeLinkList()
    {
        lock (IndirectDisposeList)
            return IndirectDisposeList.ToArray();
    }

    async Task CancelDisposeLinks(Exception? ex)
    {
        // Direct
        foreach (var obj in GetDirectDisposeLinkList())
            await obj._CancelSafeAsync(ex);

        // Indirect
        foreach (var obj in GetIndirectDisposeLinkList())
            TaskUtil.StartAsyncTaskAsync(() => obj._CancelSafeAsync(ex))._LaissezFaire();
    }

    async Task CleanupDisposeLinksAsync(Exception? ex)
    {
        // Direct
        foreach (var obj in GetDirectDisposeLinkList())
            await obj._CleanupSafeAsync(ex);

        // Indirect
        foreach (var obj in GetIndirectDisposeLinkList())
            TaskUtil.StartAsyncTaskAsync(() => obj._CleanupSafeAsync(ex))._LaissezFaire();
    }

    void DisposeLinks(Exception? ex)
    {
        // Direct
        foreach (var obj in GetDirectDisposeLinkList())
            obj._DisposeSafe(ex);

        // Indirect
        foreach (var obj in GetIndirectDisposeLinkList())
            TaskUtil.StartSyncTaskAsync(() => obj._DisposeSafe(ex))._LaissezFaire();
    }

    readonly AsyncLock CriticalProcessAsyncLock = new AsyncLock();
    protected async Task<TResult> RunCriticalProcessAsync<TResult>(bool obtainLock, CancellationToken cancel, Func<CancellationToken, Task<TResult>> func)
    {
        using (EnterCriticalCounter())
        {
            AsyncLock.LockHolder? lockHolder = null;

            if (obtainLock)
                lockHolder = await CriticalProcessAsyncLock.LockWithAwait(cancel);

            try
            {
                await using (CreatePerTaskCancellationToken(out CancellationToken opCancel, cancel))
                {
                    opCancel.ThrowIfCancellationRequested();

                    return await func(opCancel);
                }
            }
            finally
            {
                if (lockHolder != null)
                    lockHolder.Dispose();
            }
        }
    }
    protected async Task RunCriticalProcessAsync(bool obtainLock, CancellationToken cancel, Func<CancellationToken, Task> func)
    {
        using (EnterCriticalCounter())
        {
            AsyncLock.LockHolder? lockHolder = null;

            if (obtainLock)
                lockHolder = await CriticalProcessAsyncLock.LockWithAwait(cancel);

            try
            {
                await using (CreatePerTaskCancellationToken(out CancellationToken opCancel, cancel))
                {
                    opCancel.ThrowIfCancellationRequested();

                    await func(opCancel);
                }
            }
            finally
            {
                if (lockHolder != null)
                    lockHolder.Dispose();
            }
        }
    }

    protected Task<AsyncLock.LockHolder> CriticalProcessLockWithAwait(CancellationToken cancel = default)
        => CriticalProcessAsyncLock.LockWithAwait(cancel);

    protected AsyncHolder<object> CreatePerTaskCancellationToken(out CancellationToken combinedToken, params CancellationToken[] cancels)
        => TaskUtil.CreateCombinedCancellationToken(out combinedToken, this.GrandCancel._SingleArray().Concat(cancels).ToArray());

    protected ValueHolder EnterCriticalCounter()
    {
        ValueHolder ret = TaskUtil.EnterCriticalCounter(CriticalCounter);
        try
        {
            CheckNotCanceled();

            return ret;
        }
        catch
        {
            ret._DisposeSafe();
            throw;
        }
    }

    bool IsCanceledPrivateFlag = false;
    public bool IsCanceled => (IsCanceledPrivateFlag || this.GrandCancel.IsCancellationRequested);

    protected void CheckNotCanceled()
    {
        if (this.IsCanceled)
            throw CancelReason;
    }

    Exception CancelReason = new OperationCanceledException();

    public Task DisconnectAsync() => CancelAsync(new DisconnectedException());

    Once Canceled;
    public async Task CancelAsync(Exception? ex = null)
    {
        if (Canceled.IsFirstCall() == false) return;

        if (ex != null)
            CancelReason = ex;

        this.CancelWatcher.Cancel();

        await CancelInternalMainAsync();
    }

    protected virtual Task CancelImplAsync(Exception? ex) => Task.CompletedTask;
    protected virtual Task CleanupImplAsync(Exception? ex) => Task.CompletedTask;
    protected virtual void DisposeImpl(Exception? ex) { }

    Task CanceledCallbackAsync(CancelWatcher caller, NonsenseEventType type, object? userState, object? eventState)
        => CancelInternalMainAsync();

    Once CanceledInternal;
    async Task CancelInternalMainAsync()
    {
        IsCanceledPrivateFlag = true;

        if (CanceledInternal.IsFirstCall())
        {
            await CancelDisposeLinks(CancelReason);

            try
            {
                await CancelImplAsync(CancelReason);
            }
            catch (Exception ex)
            {
                Dbg.WriteLine("CancelInternal exception: " + ex._GetSingleException().ToString());
            }

            Func<Task>[]? procList = null;

            lock (LockObj)
            {
                procList = OnCancelList.ToArray();
                OnCancelList.Clear();
            }

            foreach (Func<Task> proc in procList)
            {
                try
                {
                    await proc();
                }
                catch { }
            }
        }
    }

    Once Cleanuped;
    public bool IsCleanuped => Cleanuped.IsSet;
    public async Task CleanupAsync(Exception? ex = null)
    {
        IsCanceledPrivateFlag = true;

        await CancelAsync(ex);

        if (Cleanuped.IsFirstCall())
        {
            while (CriticalCounter.Value >= 1)
            {
                await Task.Delay(10);
            }

            await CleanupDisposeLinksAsync(ex);

            await CleanupImplAsync(ex)._TryWaitAsync();
        }
    }

    Once Disposed;
    public bool IsDisposed => Disposed.IsSet;
#pragma warning disable CA1063 // Implement IDisposable Correctly
    public void Dispose() => Dispose(null);
#pragma warning restore CA1063 // Implement IDisposable Correctly
    public void Dispose(Exception? ex)
    {
        DisposeAsync(ex)._GetResult();
    }

    public async Task DisposeWithCleanupAsync(Exception? ex = null)
    {
        await this._CancelSafeAsync(ex);
        await this._CleanupSafeAsync(ex);
        this._DisposeSafe(ex);
    }

    // 非同期 Dispose
    public ValueTask DisposeAsync() => DisposeAsync(null);

    public async ValueTask DisposeAsync(Exception? ex)
    {
        // まず非同期 Cleanup をする
        try
        {
            await CleanupAsync(ex);
        }
        catch (Exception ex2)
        {
            Dbg.WriteLine("Cleanup exception: " + ex2._GetSingleException().ToString());
        }

        // 次に Dispose のメイン処理を実施する
        IsCanceledPrivateFlag = true;

        if (Disposed.IsFirstCall())
        {
            //await CleanupAsync(ex);

            DisposeLinks(ex);

            try
            {
                DisposeImpl(ex);
            }
            catch (Exception ex2)
            {
                Dbg.WriteLine("Dispose exception: " + ex2._GetSingleException().ToString());
            }

            Action[]? procList = null;

            lock (LockObj)
            {
                procList = OnDisposeList.ToArray();
                OnDisposeList.Clear();
            }

            foreach (Action proc in procList)
            {
                try
                {
                    proc();
                }
                catch { }
            }

            await this.CancelWatcher._DisposeSafeAsync();
        }
    }
}

public abstract class AsyncServiceWithMainLoop : AsyncService
{
    IHolder? Leak = null;

    public AsyncServiceWithMainLoop(CancellationToken cancel = default) : base(cancel)
    {
    }

    Task? MainLoopTask = null;

    public Task? MainLoopToWaitComplete { get; private set; } = null;

    Once once;

    protected Task StartMainLoop(Func<Task> mainLoopProc, bool noLeakCheck = false)
        => StartMainLoop((c) => mainLoopProc(), noLeakCheck);

    protected Task StartMainLoop(Func<CancellationToken, Task> mainLoopProc, bool noLeakCheck = false)
    {
        if (once.IsFirstCall() == false)
            throw new Exception("StartMainLoop is already called.");

        MainLoopTask = TaskUtil.StartAsyncTaskAsync(o => mainLoopProc((CancellationToken)o!), this.GrandCancel, leakCheck: !noLeakCheck);

        if (noLeakCheck == false)
            Leak = LeakChecker.Enter(LeakCounterKind.AsyncServiceWithMainLoop);

        this.MainLoopToWaitComplete = MainLoopTask;

        return MainLoopTask; // For reference. Not needed for most of all applications.
    }

    protected override async Task CleanupImplAsync(Exception? ex)
    {
        try
        {
            try
            {
                if (MainLoopTask != null)
                {
                    try
                    {
                        await MainLoopTask;
                    }
                    finally
                    {
                        MainLoopTask = null;
                    }
                }
            }
            catch (TaskCanceledException) { }
            catch (OperationCanceledException) { }
        }
        finally
        {
            Leak._DisposeSafe();

            await base.CleanupImplAsync(ex);
        }
    }
}

public struct FastReadList<T>
{
    readonly static CriticalSection GlobalWriteLock = new CriticalSection<FastReadList<T>>();
    static volatile int IdSeed = 0;

    SortedDictionary<int, T> Hash;

    volatile T[]? InternalFastList;

    public T[]? GetListFast() => InternalFastList;

    [MethodImpl(Inline)]
    public bool HasAnyListeners() => (this.InternalFastList?.Length ?? 0) >= 1;

    public int Add(T value)
    {
        lock (GlobalWriteLock)
        {
            if (Hash == null)
                Hash = new SortedDictionary<int, T>();

            int id = ++IdSeed;
            Hash.Add(id, value);
            Update();
            return id;
        }
    }

    public bool Delete(int id)
    {
        lock (GlobalWriteLock)
        {
            if (Hash == null)
                return false;

            bool ret = Hash.Remove(id);
            if (ret)
            {
                Update();
            }
            return ret;
        }
    }

    void Update()
    {
        if (Hash.Count == 0)
            InternalFastList = null;
        else
            InternalFastList = Hash.Values.ToArray();
    }
}

public struct ValueHolder : IHolder
{
    Action DisposeProc;
    IHolder? Leak;
    LeakCounterKind LeakKind;

    static readonly bool FullStackTrace = CoresConfig.DebugSettings.LeakCheckerFullStackLog;

    [MethodImpl(Inline)]
    public ValueHolder(Action disposeProc, LeakCounterKind leakCheckKind = LeakCounterKind.OthersCounter)
    {
        this.DisposeProc = disposeProc;

        this.LeakKind = leakCheckKind;

        if (FullStackTrace && leakCheckKind != LeakCounterKind.DoNotTrack)
        {
            Leak = LeakChecker.Enter(leakCheckKind);
        }
        else
        {
            LeakChecker.IncrementLeakCounter(this.LeakKind);
            Leak = null;
        }

        DisposeFlag = new Once();
    }

    Once DisposeFlag;
    public void Dispose()
    {
        if (DisposeFlag.IsFirstCall())
        {
            try
            {
                if (DisposeProc != null)
                    DisposeProc();
            }
            finally
            {
                if (Leak != null)
                    Leak._DisposeSafe();
                else
                    LeakChecker.DecrementLeakCounter(this.LeakKind);
            }
        }
    }
}

public class Holder : Holder<int>
{
    public Holder(Action disposeProc, LeakCounterKind leakCheckKind = LeakCounterKind.OthersCounter)
        : base(_ => disposeProc(), default, leakCheckKind) { }
}

public class Holder<T> : IHolder
{
    [AllowNull]
    public T Value { get; }

    Action<T> DisposeProc;
    IHolder? Leak = null;
    LeakCounterKind LeakKind;

    static readonly bool FullStackTrace = CoresConfig.DebugSettings.LeakCheckerFullStackLog;

    public Holder(Action<T> disposeProc, [AllowNull] T value = default(T), LeakCounterKind leakCheckKind = LeakCounterKind.OthersCounter)
    {
        this.Value = value;
        this.DisposeProc = disposeProc;

        this.LeakKind = leakCheckKind;

        if (FullStackTrace && leakCheckKind != LeakCounterKind.DoNotTrack)
        {
            Leak = LeakChecker.Enter(leakCheckKind);
        }
        else
        {
            LeakChecker.IncrementLeakCounter(this.LeakKind);
        }
    }

    Once DisposeFlag;
    public void Dispose() { this.Dispose(true); GC.SuppressFinalize(this); }
    protected virtual void Dispose(bool disposing)
    {
        if (disposing && DisposeFlag.IsFirstCall())
        {
            try
            {
                if (DisposeProc != null)
                    DisposeProc(Value);
            }
            finally
            {
                if (Leak != null)
                    Leak._DisposeSafe();
                else
                    LeakChecker.DecrementLeakCounter(this.LeakKind);
            }
        }
    }
}

public class AsyncHolder : AsyncHolder<int>
{
    public AsyncHolder(Func<Task> disposeProcAsync, LeakCounterKind leakCheckKind = LeakCounterKind.OthersCounter)
        : base(_ => disposeProcAsync(), default, leakCheckKind) { }
}

public class AsyncHolder<T> : IAsyncHolder
{
    [AllowNull]
    public T Value { get; }

    Func<T, Task>? DisposeProcAsync;
    IHolder? Leak = null;
    LeakCounterKind LeakKind;

    static readonly bool FullStackTrace = CoresConfig.DebugSettings.LeakCheckerFullStackLog;

    public AsyncHolder(Func<T, Task>? disposeProcAsync, [AllowNull] T value = default(T), LeakCounterKind leakCheckKind = LeakCounterKind.OthersCounter)
    {
        this.Value = value;
        this.DisposeProcAsync = disposeProcAsync;

        this.LeakKind = leakCheckKind;

        if (FullStackTrace && leakCheckKind != LeakCounterKind.DoNotTrack)
        {
            Leak = LeakChecker.Enter(leakCheckKind);
        }
        else
        {
            LeakChecker.IncrementLeakCounter(this.LeakKind);
        }
    }


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
        try
        {
            if (DisposeProcAsync != null)
                await DisposeProcAsync(Value);
        }
        finally
        {
            if (Leak != null)
                Leak._DisposeSafe();
            else
                LeakChecker.DecrementLeakCounter(this.LeakKind);
        }
    }
}

public struct ValueHolder<T> : IHolder
{
    [AllowNull]
    public T Value { get; }

    Action<T>? DisposeProc;
    IHolder? Leak;
    LeakCounterKind LeakKind;

    static readonly bool FullStackTrace = CoresConfig.DebugSettings.LeakCheckerFullStackLog;

    public ValueHolder(Action<T>? disposeProc, [AllowNull] T value = default(T), LeakCounterKind leakCheckKind = LeakCounterKind.OthersCounter)
    {
        this.Value = value;
        this.DisposeProc = disposeProc;

        this.LeakKind = leakCheckKind;

        if (FullStackTrace && leakCheckKind != LeakCounterKind.DoNotTrack)
        {
            Leak = LeakChecker.Enter(leakCheckKind);
        }
        else
        {
            LeakChecker.IncrementLeakCounter(this.LeakKind);
            Leak = null;
        }

        DisposeFlag = new Once();
    }

    Once DisposeFlag;
    public void Dispose()
    {
        if (DisposeFlag.IsFirstCall())
        {
            try
            {
                if (DisposeProc != null)
                    DisposeProc(Value);
            }
            finally
            {
                if (Leak != null)
                    Leak._DisposeSafe();
                else
                    LeakChecker.DecrementLeakCounter(this.LeakKind);
            }
        }
    }
}

public interface IAsyncHolder : IDisposable, IAsyncDisposable { }

public interface IHolder : IDisposable { }

public delegate void FastEventCallback<TCaller, TEventType>(TCaller caller, TEventType type, object? userState, object? eventParam);

public class FastEvent<TCaller, TEventType>
{
    public FastEventCallback<TCaller, TEventType> Proc { get; }
    public object? UserState { get; }

    public FastEvent(FastEventCallback<TCaller, TEventType> proc, object? userState)
    {
        this.Proc = proc;
        this.UserState = userState;
    }

    public void CallSafe(TCaller buffer, TEventType type, object? eventParam, bool debugLog = false)
    {
        try
        {
            this.Proc(buffer, type, UserState, eventParam);
        }
        catch (Exception ex)
        {
            if (debugLog)
            {
                ex._Debug();
            }
        }
    }
}

[Flags]
public enum NonsenseEventType
{
    Nonsense,
}

public class FastEventListenerList<TCaller, TEventType>
{
    FastReadList<FastEvent<TCaller, TEventType>> ListenerList;
    FastReadList<AsyncAutoResetEvent> AsyncEventList;

    [MethodImpl(Inline)]
    public bool HasAnyListeners() => ListenerList.HasAnyListeners() || AsyncEventList.HasAnyListeners();

    public int RegisterCallback(FastEventCallback<TCaller, TEventType> proc, object? userState = null)
    {
        if (proc == null) return 0;
        return ListenerList.Add(new FastEvent<TCaller, TEventType>(proc, userState));
    }

    public bool UnregisterCallback(int id)
    {
        return ListenerList.Delete(id);
    }

    public ValueHolder<int> RegisterCallbackWithUsing(FastEventCallback<TCaller, TEventType> proc, object? userState = null)
        => new ValueHolder<int>(id => UnregisterCallback(id), RegisterCallback(proc, userState));

    public int RegisterAsyncEvent(AsyncAutoResetEvent ev)
    {
        if (ev == null) return 0;
        return AsyncEventList.Add(ev);
    }

    public bool UnregisterAsyncEvent(int id)
    {
        return AsyncEventList.Delete(id);
    }

    public ValueHolder<int> RegisterAsyncEventWithUsing(AsyncAutoResetEvent ev)
        => new ValueHolder<int>(id => UnregisterAsyncEvent(id), RegisterAsyncEvent(ev));

    public void Fire(TCaller caller, TEventType type, object? eventState = null, bool debugLog = false)
    {
        var listenerList = ListenerList.GetListFast();
        if (listenerList != null)
            foreach (var e in listenerList)
                e.CallSafe(caller, type, eventState, debugLog);

        var asyncEventList = AsyncEventList.GetListFast();
        if (asyncEventList != null)
            foreach (var e in asyncEventList)
                e.Set();
    }

    public void FireSoftly(TCaller caller, TEventType type, object? eventState = null)
    {
        TaskUtil.StartSyncTaskAsync(() => Fire(caller, type, eventState), true, false)._LaissezFaire();
    }
}

public delegate Task AsyncEventCallback<TCaller, TEventType>(TCaller caller, TEventType type, object? userState, object? eventParam);

public class AsyncEvent<TCaller, TEventType>
{
    public AsyncEventCallback<TCaller, TEventType> Proc { get; }
    public object? UserState { get; }

    public AsyncEvent(AsyncEventCallback<TCaller, TEventType> proc, object? userState)
    {
        this.Proc = proc;
        this.UserState = userState;
    }

    public async Task CallSafeAsync(TCaller buffer, TEventType type, object? eventState, bool debugLog = false)
    {
        try
        {
            await this.Proc(buffer, type, UserState, eventState);
        }
        catch (Exception ex)
        {
            if (debugLog)
            {
                ex._Debug();
            }
        }
    }
}

public class AsyncEventListenerList<TCaller, TEventType>
{
    FastReadList<AsyncEvent<TCaller, TEventType>> ListenerList;
    FastReadList<AsyncAutoResetEvent> AsyncEventList;

    public int RegisterCallback(AsyncEventCallback<TCaller, TEventType> proc, object? userState = null)
    {
        if (proc == null) return 0;
        return ListenerList.Add(new AsyncEvent<TCaller, TEventType>(proc, userState));
    }

    public bool UnregisterCallback(int id)
    {
        return ListenerList.Delete(id);
    }

    public ValueHolder<int> RegisterCallbackWithUsing(AsyncEventCallback<TCaller, TEventType> proc, object? userState = null)
        => new ValueHolder<int>(id => UnregisterCallback(id), RegisterCallback(proc, userState));

    public int RegisterAsyncEvent(AsyncAutoResetEvent ev)
    {
        if (ev == null) return 0;
        return AsyncEventList.Add(ev);
    }

    public bool UnregisterAsyncEvent(int id)
    {
        return AsyncEventList.Delete(id);
    }

    public ValueHolder<int> RegisterAsyncEventWithUsing(AsyncAutoResetEvent ev)
        => new ValueHolder<int>(id => UnregisterAsyncEvent(id), RegisterAsyncEvent(ev));

    public async Task FireAsync(TCaller caller, TEventType type, object? eventState = null, bool debugLog = false)
    {
        var listenerList = ListenerList.GetListFast();
        if (listenerList != null)
            foreach (var e in listenerList)
                await e.CallSafeAsync(caller, type, eventState, debugLog);

        var asyncEventList = AsyncEventList.GetListFast();
        if (asyncEventList != null)
            foreach (var e in asyncEventList)
                e.Set();
    }
}

public class CancelWatcher : IDisposable, IAsyncDisposable
{
    readonly CancellationTokenSource GrandCancelTokenSource;
    readonly IDisposable LeakHolder;
    readonly IAsyncDisposable ObjectHolder;
    readonly IAsyncDisposable RegisterHolder;

    public FastEventListenerList<CancelWatcher, NonsenseEventType> EventList { get; } = new FastEventListenerList<CancelWatcher, NonsenseEventType>();
    public AsyncEventListenerList<CancelWatcher, NonsenseEventType> AsyncEventList { get; } = new AsyncEventListenerList<CancelWatcher, NonsenseEventType>();

    public CancellationToken CancelToken { get; }

    public bool Canceled => this.CancelToken.IsCancellationRequested;
    public bool IsCancellationRequested => this.CancelToken.IsCancellationRequested;

    public CancelWatcher(params CancellationToken[] cancels)
    {
        this.GrandCancelTokenSource = new CancellationTokenSource();

        CancellationToken[] tokens = this.GrandCancelTokenSource.Token._SingleArray().Concat(cancels).ToArray();

        this.ObjectHolder = TaskUtil.CreateCombinedCancellationToken(out CancellationToken cancelToken, tokens);
        this.RegisterHolder = cancelToken.Register(() =>
        {
            this.EventList.Fire(this, NonsenseEventType.Nonsense);
            this.AsyncEventList.FireAsync(this, NonsenseEventType.Nonsense)._LaissezFaire();
        });
        this.CancelToken = cancelToken;

        this.LeakHolder = LeakChecker.Enter(LeakCounterKind.CancelWatcher);
    }

    Once CancelFlag;
    public void Cancel()
    {
        if (CancelFlag.IsFirstCall())
        {
            this.GrandCancelTokenSource._TryCancelNoBlock();
        }
    }


    public void Dispose() { this.Dispose(true); GC.SuppressFinalize(this); }
    Once DisposeFlag;
    public virtual async ValueTask DisposeAsync()
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
        Cancel();

        await this.RegisterHolder._DisposeSafeAsync();
        await this.ObjectHolder._DisposeSafeAsync();
        this.LeakHolder._DisposeSafe();
    }


    public static implicit operator CancellationToken(CancelWatcher c) => c.CancelToken;

    public void ThrowIfCancellationRequested() => this.CancelToken.ThrowIfCancellationRequested();
}

public delegate Task<bool> TimeoutDetectorAsyncCallback(TimeoutDetector detector);

public class TimeoutDetector : AsyncService
{
    Task? MainTask;

    readonly CriticalSection LockObj = new CriticalSection<TimeoutDetector>();

    public long Timeout { get; }

    long NextTimeout;

    AsyncAutoResetEvent ev = new AsyncAutoResetEvent();

    AsyncAutoResetEvent? eventAuto;
    AsyncManualResetEvent? eventManual;

    public object? UserState { get; }

    public bool TimedOut { get; private set; } = false;

    CancelWatcher? cancelWatcherToCancel;

    TimeoutDetectorAsyncCallback? CallbackAsync;

    public TimeoutDetector(int timeout, CancelWatcher? watcher = null, AsyncAutoResetEvent? eventAuto = null, AsyncManualResetEvent? eventManual = null,
        TimeoutDetectorAsyncCallback? callback = null, object? userState = null)
    {
        if (timeout == System.Threading.Timeout.Infinite || timeout == int.MaxValue)
        {
            return;
        }

        this.cancelWatcherToCancel = watcher;
        this.Timeout = timeout;
        this.eventAuto = eventAuto;
        this.eventManual = eventManual;
        this.CallbackAsync = callback;
        this.UserState = userState;

        NextTimeout = FastTick64.Now + this.Timeout;
        MainTask = TimeoutDetectorMainLoopAsync();
    }

    public void Keep()
    {
        Interlocked.Exchange(ref this.NextTimeout, FastTick64.Now + this.Timeout);
    }

    async Task TimeoutDetectorMainLoopAsync()
    {
        await Task.Yield();
        using (LeakChecker.Enter())
        {
            while (true)
            {
                long nextTimeout = Interlocked.Read(ref this.NextTimeout);

                long now = FastTick64.Now;

                long remainTime = nextTimeout - now;

                if (remainTime <= 0)
                {
                    if (CallbackAsync != null && await CallbackAsync(this))
                    {
                        Keep();
                    }
                    else
                    {
                        this.TimedOut = true;
                    }
                }

                if (this.TimedOut || this.GrandCancel.IsCancellationRequested)
                {
                    if (this.cancelWatcherToCancel != null) this.cancelWatcherToCancel.Cancel();
                    if (this.eventAuto != null) this.eventAuto.Set();
                    if (this.eventManual != null) this.eventManual.Set();

                    return;
                }
                else
                {
                    await TaskUtil.WaitObjectsAsync(
                        events: new AsyncAutoResetEvent[] { ev },
                        cancels: new CancellationToken[] { this.GrandCancel },
                        timeout: (int)remainTime);
                }
            }
        }
    }

    protected override async Task CleanupImplAsync(Exception? ex)
    {
        try
        {
            if (MainTask != null)
                await MainTask;
        }
        finally
        {
            await base.CleanupImplAsync(ex);
        }
    }
}

public struct LazyCriticalSection
{
    CriticalSection _LockObj;

    public void EnsureCreated()
    {
        Limbo.ObjectVolatileSlow = this.LockObj;
    }

    public CriticalSection LockObj
    {
        get
        {
            if (_LockObj == null)
            {
                _LockObj = new CriticalSection<LazyCriticalSection>();
            }
            return _LockObj;
        }
    }
}

// 名前無し
public class CriticalSection { }

// 名前付き: デバッガでわかりやすいようにするため
public class CriticalSection<T> : CriticalSection { }

public class WhenAll : IDisposable
{
    public Task WaitMe { get; }
    public bool AllOk { get; private set; } = false;
    public bool HasError { get => !AllOk; }

    CancellationTokenSource CancelSource = new CancellationTokenSource();

    public WhenAll(IEnumerable<Task> tasks, bool throwException = false) : this(throwException, tasks.ToArray()) { }

    public WhenAll(Task t, bool throwException = false) : this(throwException, t._SingleArray()) { }

    public static Task Await(IEnumerable<Task> tasks, bool throwException = false)
        => Await(throwException, tasks.ToArray());

    public static Task Await(Task t, bool throwException = false)
        => Await(throwException, t._SingleArray());

    public static async Task Await(bool throwException = false, params Task[] tasks)
    {
        using (var w = new WhenAll(throwException, tasks))
            await w.WaitMe;
    }

    public WhenAll(bool throwException = false, params Task[] tasks)
    {
        this.WaitMe = WaitMain(tasks, throwException);
    }

    async Task WaitMain(Task[] tasks, bool throwException)
    {
        Task cancelTask = TaskUtil.WhenCanceled(CancelSource.Token, out CancellationTokenRegistration reg);
        await using (reg)
        {
            bool allOk = true;
            foreach (Task t in tasks)
            {
                if (t != null)
                {
                    try
                    {
                        await Task.WhenAny(t, cancelTask);
                    }
                    catch { }

                    if (throwException)
                    {
                        if (t.IsFaulted)
                            t.Exception._ReThrow();
                        if (t.IsCanceled)
                            throw new TaskCanceledException();
                    }

                    if (t.IsCompletedSuccessfully == false)
                        allOk = false;

                    if (CancelSource.Token.IsCancellationRequested)
                    {
                        allOk = false;
                        return;
                    }
                }
            }

            AllOk = allOk;
        }
    }

    public void Dispose() { this.Dispose(true); GC.SuppressFinalize(this); }
    Once DisposeFlag;
    protected virtual void Dispose(bool disposing)
    {
        if (!disposing || DisposeFlag.IsFirstCall() == false) return;
        CancelSource.Cancel();
    }
}

public class GroupManager<TKey, TGroupContext> : IDisposable
    where TKey : notnull
{
    public class GroupHandle : Holder<GroupInstance>
    {
        public TKey Key { get; }
        public TGroupContext Context { get; }
        public GroupInstance Instance { get; }

        internal GroupHandle(Action<GroupInstance> disposeProc, GroupInstance groupInstance, TKey key) : base(disposeProc, groupInstance)
        {
            this.Instance = groupInstance;
            this.Context = this.Instance.Context;
            this.Key = key;
        }
    }

    public class GroupInstance
    {
        public TKey Key = default!;
        public TGroupContext Context = default!;
        public int Num;
    }

    public delegate TGroupContext NewGroupContextCallback(TKey key, object? userState);
    public delegate void DeleteGroupContextCallback(TKey key, TGroupContext groupContext, object? userState);

    public object? UserState { get; }

    NewGroupContextCallback NewGroupContextProc;
    DeleteGroupContextCallback DeleteGroupContextProc;

    Dictionary<TKey, GroupInstance> Hash = new Dictionary<TKey, GroupInstance>();

    readonly CriticalSection LockObj = new CriticalSection<GroupManager<TKey, TGroupContext>>();

    public GroupManager(NewGroupContextCallback onNewGroup, DeleteGroupContextCallback onDeleteGroup, object? userState = null)
    {
        NewGroupContextProc = onNewGroup;
        DeleteGroupContextProc = onDeleteGroup;
        UserState = userState;
    }

    public GroupHandle Enter(TKey key)
    {
        lock (LockObj)
        {
            GroupInstance? g = null;
            if (Hash.TryGetValue(key, out g) == false)
            {
                var context = NewGroupContextProc(key, UserState);
                g = new GroupInstance()
                {
                    Key = key,
                    Context = context,
                    Num = 0,
                };
                Hash.Add(key, g);
            }

            Debug.Assert(g!.Num >= 0);
            g.Num++;

            return new GroupHandle(x =>
            {
                lock (LockObj)
                {
                    x.Num--;
                    Debug.Assert(x.Num >= 0);

                    if (x.Num == 0)
                    {
                        Hash.Remove(x.Key);

                        DeleteGroupContextProc(x.Key, x.Context, this.UserState);
                    }
                }
            }, g, key);
        }
    }

    public void Dispose() { this.Dispose(true); GC.SuppressFinalize(this); }
    Once DisposeFlag;
    protected virtual void Dispose(bool disposing)
    {
        if (!disposing || DisposeFlag.IsFirstCall() == false) return;
        lock (LockObj)
        {
            foreach (var v in Hash.Values)
            {
                try
                {
                    DeleteGroupContextProc(v.Key, v.Context, this.UserState);
                }
                catch { }
            }

            Hash.Clear();
        }
    }
}

public class DelayAction : AsyncService
{
    public Func<object?, Task> ActionAsync { get; }
    public object? UserState { get; }
    public int Timeout { get; }

    Task MainTask;

    public bool IsCompleted = false;
    public bool IsCompletedSuccessfully = false;
    public bool DoNotBlockOnDispose = false;

    public Exception? Exception { get; private set; } = null;

    public DelayAction(int timeout, Func<object?, Task> actionAsync, object? userState = null, bool doNotBlockOnDispose = false)
    {
        if (timeout < 0 || timeout == int.MaxValue) timeout = System.Threading.Timeout.Infinite;

        this.DoNotBlockOnDispose = doNotBlockOnDispose;
        this.Timeout = timeout;
        this.ActionAsync = actionAsync;
        this.UserState = userState;

        this.MainTask = MainTaskProcAsync();
    }

    async Task InternalInvokeActionAsync()
    {
        try
        {
            await this.ActionAsync(this.UserState);

            IsCompleted = true;
            IsCompletedSuccessfully = true;
        }
        catch (Exception ex)
        {
            IsCompleted = true;
            IsCompletedSuccessfully = false;

            Exception = ex;
        }
    }

    async Task MainTaskProcAsync()
    {
        using (LeakChecker.Enter())
        {
            try
            {
                await TaskUtil.WaitObjectsAsync(timeout: this.Timeout,
                    cancels: this.GrandCancel._SingleArray(),
                    exceptions: ExceptionWhen.CancelException);

                await InternalInvokeActionAsync();
            }
            catch
            {
                IsCompleted = true;
                IsCompletedSuccessfully = false;
            }
        }
    }

    protected override async Task CleanupImplAsync(Exception? ex)
    {
        if (this.DoNotBlockOnDispose == false)
        {
            await this.MainTask;
        }
    }
}

public struct ValueOrClosed<T>
{
    bool InternalIsOpen;
    public T Value;

    public bool IsOpen { get => InternalIsOpen; }
    public bool IsClosed { get => !InternalIsOpen; }

    public ValueOrClosed(T value)
    {
        InternalIsOpen = true;
        Value = value;
    }
}

public class AsyncBulkReceiver<TUserReturnElement, TUserState>
{
    public delegate Task<ValueOrClosed<TUserReturnElement>> AsyncReceiveCallback(TUserState state, CancellationToken cancel);

    public int DefaultMaxCount { get; }

    AsyncReceiveCallback AsyncReceiveProc;

    public AsyncBulkReceiver(AsyncReceiveCallback asyncReceiveProc, int defaultMaxCount = 256 /* 256 = TCP optimized */)
    {
        DefaultMaxCount = defaultMaxCount;
        AsyncReceiveProc = asyncReceiveProc;
    }

    Task<ValueOrClosed<TUserReturnElement>>? pushedUserTask = null;

    public async Task<TUserReturnElement[]?> RecvAsync(CancellationToken cancel, TUserState state = default(TUserState)!, int? maxCount = null)
    {
        if (maxCount == null) maxCount = DefaultMaxCount;
        if (maxCount <= 0) maxCount = int.MaxValue;
        List<TUserReturnElement> ret = new List<TUserReturnElement>();

        while (true)
        {
            cancel.ThrowIfCancellationRequested();

            Task<ValueOrClosed<TUserReturnElement>> userTask;
            if (pushedUserTask != null)
            {
                userTask = pushedUserTask;
                pushedUserTask = null;
            }
            else
            {
                userTask = AsyncReceiveProc(state, cancel);
            }
            if (userTask.IsCompleted == false)
            {
                if (ret.Count >= 1)
                {
                    pushedUserTask = userTask;
                    break;
                }
                else
                {
                    await TaskUtil.WaitObjectsAsync(
                        tasks: new Task[] { userTask },
                        cancels: new CancellationToken[] { cancel });

                    cancel.ThrowIfCancellationRequested();

                    var result = userTask._GetResult();

                    if (result.IsOpen)
                    {
                        ret.Add(result.Value);
                    }
                    else
                    {
                        pushedUserTask = userTask;
                        break;
                    }
                }
            }
            else
            {
                var result = userTask._GetResult();

                if (result.IsOpen)
                {
                    ret.Add(result.Value);
                }
                else
                {
                    break;
                }
            }
            if (ret.Count >= maxCount) break;
        }

        if (ret.Count >= 1)
            return ret.ToArray();
        else
            return null; // Disconnected
    }
}

public class SharedQueue<T>
{
    class QueueBody
    {
        static long globalTimestamp;

        public QueueBody? Next;

        public SortedList<long, T>? _InternalList = new SortedList<long, T>();
        public readonly int MaxItems;

        public QueueBody(int maxItems)
        {
            if (maxItems <= 0) maxItems = int.MaxValue;
            MaxItems = maxItems;
        }

        public void Enqueue(T item, bool distinct = false)
        {
            lock (_InternalList!)
            {
                if (_InternalList.Count > MaxItems) return;
                if (distinct && _InternalList.ContainsValue(item)) return;
                long ts = Interlocked.Increment(ref globalTimestamp);
                _InternalList.Add(ts, item);
            }
        }

        [return: MaybeNull]
        public T Dequeue()
        {
            lock (_InternalList!)
            {
                if (_InternalList.Count == 0) return default(T)!;
                long ts = _InternalList.Keys[0];
                T ret = _InternalList[ts];
                _InternalList.Remove(ts);
                return ret;
            }
        }

        public T[] ToArray()
        {
            lock (_InternalList!)
            {
                return _InternalList.Values.ToArray();
            }
        }

        public static void Merge(QueueBody q1, QueueBody q2)
        {
            if (q1 == q2) return;
            Debug.Assert(q1._InternalList != null);
            Debug.Assert(q2._InternalList != null);

            lock (q1._InternalList)
            {
                lock (q2._InternalList)
                {
                    QueueBody q3 = new QueueBody(Math.Max(q1.MaxItems, q2.MaxItems));
                    foreach (long ts in q1._InternalList.Keys)
                        q3._InternalList!.Add(ts, q1._InternalList[ts]);
                    foreach (long ts in q2._InternalList.Keys)
                        q3._InternalList!.Add(ts, q2._InternalList[ts]);
                    if (q3._InternalList!.Count > q3.MaxItems)
                    {
                        int num = 0;
                        List<long> removeList = new List<long>();
                        foreach (long ts in q3._InternalList.Keys)
                        {
                            num++;
                            if (num > q3.MaxItems)
                                removeList.Add(ts);
                        }
                        foreach (long ts in removeList)
                            q3._InternalList.Remove(ts);
                    }
                    q1._InternalList = null;
                    q2._InternalList = null;
                    Debug.Assert(q1.Next == null);
                    Debug.Assert(q2.Next == null);
                    q1.Next = q3;
                    q2.Next = q3;
                }
            }
        }

        public QueueBody GetLast()
        {
            if (Next == null)
                return this;
            else
                return Next.GetLast();
        }
    }

    QueueBody First;

    public static readonly CriticalSection GlobalLock = new CriticalSection<SharedQueue<T>>();

    public bool Distinct { get; }

    public SharedQueue(int maxItems = 0, bool distinct = false)
    {
        Distinct = distinct;
        First = new QueueBody(maxItems);
    }

    public void Encounter(SharedQueue<T> other)
    {
        if (this == other) return;

        lock (GlobalLock)
        {
            QueueBody last1 = this.First.GetLast();
            QueueBody last2 = other.First.GetLast();
            if (last1 == last2) return;

            QueueBody.Merge(last1, last2);
        }
    }

    public void Enqueue(T value)
    {
        lock (GlobalLock)
            this.First.GetLast().Enqueue(value, Distinct);
    }

    [return: MaybeNull]
    public T Dequeue()
    {
        lock (GlobalLock)
            return this.First.GetLast().Dequeue();
    }

    public T[] ToArray()
    {
        lock (GlobalLock)
            return this.First.GetLast().ToArray();
    }

    public int CountFast
    {
        get
        {
            var q = this.First.GetLast();
            var list = q._InternalList;
            if (list == null) return 0;
            lock (list)
                return list.Count;
        }
    }

    public T[] ItemsReadOnly { get => ToArray(); }
}

public class ExceptionQueue
{
    public const int MaxItems = 128;
    SharedQueue<Exception> Queue = new SharedQueue<Exception>(MaxItems, true);
    public AsyncManualResetEvent WhenExceptionAdded { get; } = new AsyncManualResetEvent();

    HashSet<Task> WatchedTasks = new HashSet<Task>();

    public void Raise(Exception ex) => Add(ex, true);

    public void Add(Exception ex, bool raiseFirstException = false, bool doNotCheckWatchedTasks = false)
    {
        if (ex == null)
            ex = new Exception("null exception");

        if (doNotCheckWatchedTasks == false)
            CheckWatchedTasks();

        Exception? throwingException = null;

        AggregateException? aex = ex as AggregateException;

        if (aex != null)
        {
            var exp = aex.Flatten().InnerExceptions;

            lock (SharedQueue<Exception>.GlobalLock)
            {
                foreach (var expi in exp)
                    Queue.Enqueue(expi);

                if (raiseFirstException)
                    throwingException = Queue.ItemsReadOnly[0];
            }
        }
        else
        {
            lock (SharedQueue<Exception>.GlobalLock)
            {
                Queue.Enqueue(ex);
                if (raiseFirstException)
                    throwingException = Queue.ItemsReadOnly[0];
            }
        }

        WhenExceptionAdded.Set(true);

        if (throwingException != null)
            throwingException._ReThrow();
    }

    public void Encounter(ExceptionQueue other) => this.Queue.Encounter(other.Queue);

    public Exception[] GetExceptions()
    {
        CheckWatchedTasks();
        return this.Queue.ItemsReadOnly;
    }
    public Exception[] Exceptions => GetExceptions();
    public Exception? FirstException => Exceptions.FirstOrDefault();

    public void ThrowFirstExceptionIfExists()
    {
        Exception? ex = null;
        lock (SharedQueue<Exception>.GlobalLock)
        {
            if (HasError)
                ex = FirstException;
        }

        if (ex != null)
            ex._ReThrow();
    }

    public bool HasError => Exceptions.Length != 0;
    public bool IsOk => !HasError;

    public bool RegisterWatchedTask(Task t)
    {
        if (t.IsCompleted)
        {
            if (t.IsFaulted)
                Add(t.Exception!);
            else if (t.IsCanceled)
                Add(new TaskCanceledException());

            return true;
        }

        bool ret;

        lock (SharedQueue<Exception>.GlobalLock)
        {
            ret = WatchedTasks.Add(t);
        }

        t.ContinueWith(x =>
        {
            CheckWatchedTasks();
        });

        return ret;
    }

    public bool UnregisterWatchedTask(Task t)
    {
        lock (SharedQueue<Exception>.GlobalLock)
            return WatchedTasks.Remove(t);
    }

    void CheckWatchedTasks()
    {
        List<Task> o = new List<Task>();

        List<Exception> expList = new List<Exception>();

        lock (SharedQueue<Exception>.GlobalLock)
        {
            foreach (Task t in WatchedTasks)
            {
                if (t.IsCompleted)
                {
                    if (t.IsFaulted)
                        expList.Add(t.Exception!);
                    else if (t.IsCanceled)
                        expList.Add(new TaskCanceledException());

                    o.Add(t);
                }
            }

            foreach (Task t in o)
                WatchedTasks.Remove(t);
        }

        foreach (Exception ex in expList)
            Add(ex, doNotCheckWatchedTasks: true);
    }
}

public class HierarchyPosition : RefInt
{
    public HierarchyPosition() : base(-1) { }
    public bool IsInstalled { get => (this.Value != -1); }
}

public class SharedHierarchy<T>
{
    public class HierarchyBodyItem : IComparable<HierarchyBodyItem>, IEquatable<HierarchyBodyItem>
    {
        public HierarchyPosition Position;
        public T Value;
        public HierarchyBodyItem(HierarchyPosition position, T value)
        {
            this.Position = position;
            this.Value = value;
        }

        public int CompareTo(HierarchyBodyItem? other) => this.Position.CompareTo(other!.Position);
        public bool Equals(HierarchyBodyItem? other) => this.Position.Equals(other!.Position);
        public override bool Equals(object? obj) => (obj is HierarchyBodyItem) ? this.Position.Equals((obj as HierarchyBodyItem)!.Position) : false;
        public override int GetHashCode() => this.Position.GetHashCode();
    }

    class HierarchyBody
    {
        public List<HierarchyBodyItem>? _InternalList = new List<HierarchyBodyItem>();

        public HierarchyBody? Next = null;

        public HierarchyBody() { }

        public HierarchyBodyItem[] ToArray()
        {
            lock (_InternalList!)
                return _InternalList.ToArray();
        }

        public HierarchyBodyItem Join(HierarchyPosition? targetPosition, bool joinAsSuperior, T value, HierarchyPosition myPosition)
        {
            lock (_InternalList!)
            {
                if (targetPosition == null)
                {
                    var me = new HierarchyBodyItem(myPosition, value);
                    var current = _InternalList;

                    current = current.Append(me).ToList();

                    int positionIncrement = 0;
                    current.ForEach(x => x.Position.Set(++positionIncrement));

                    _InternalList.Clear();
                    _InternalList.AddRange(current);

                    return me;
                }
                else
                {
                    var current = _InternalList;

                    var inferiors = current.Where(x => joinAsSuperior ? (x.Position <= targetPosition) : (x.Position < targetPosition));
                    var me = new HierarchyBodyItem(myPosition, value);
                    var superiors = current.Where(x => joinAsSuperior ? (x.Position > targetPosition) : (x.Position >= targetPosition));

                    current = inferiors.Append(me).Concat(superiors).ToList();

                    int positionIncrement = 0;
                    current.ForEach(x => x.Position.Set(++positionIncrement));

                    _InternalList.Clear();
                    _InternalList.AddRange(current);

                    return me;
                }
            }
        }

        public void Resign(HierarchyBodyItem me)
        {
            lock (_InternalList!)
            {
                var current = _InternalList;

                if (current.Contains(me))
                {
                    current.Remove(me);

                    int positionIncrement = 0;
                    current.ForEach(x => x.Position.Set(++positionIncrement));

                    Debug.Assert(me.Position.IsInstalled);

                    me.Position.Set(-1);
                }
                else
                {
                    Debug.Assert(false);
                }
            }
        }

        public static void Merge(HierarchyBody inferiors, HierarchyBody superiors)
        {
            if (inferiors == superiors) return;

            Debug.Assert(inferiors._InternalList != null);
            Debug.Assert(superiors._InternalList != null);

            lock (inferiors._InternalList)
            {
                lock (superiors._InternalList)
                {
                    HierarchyBody merged = new HierarchyBody();
                    merged._InternalList!.AddRange(inferiors._InternalList.Concat(superiors._InternalList));

                    int positionIncrement = 0;
                    merged._InternalList.ForEach(x => x.Position.Set(++positionIncrement));

                    inferiors._InternalList = superiors._InternalList = null;
                    Debug.Assert(inferiors.Next == null); Debug.Assert(superiors.Next == null);
                    inferiors.Next = superiors.Next = merged;
                }
            }
        }

        public HierarchyBody GetLast()
        {
            if (Next == null)
                return this;
            else
                return Next.GetLast();
        }
    }

    HierarchyBody First;

    public static readonly CriticalSection GlobalLock = new CriticalSection<SharedHierarchy<T>>();

    public SharedHierarchy()
    {
        First = new HierarchyBody();
    }

    public void Encounter(SharedHierarchy<T> inferiors)
    {
        if (this == inferiors) return;

        lock (GlobalLock)
        {
            HierarchyBody inferiorsBody = inferiors.First.GetLast();
            HierarchyBody superiorsBody = this.First.GetLast();
            if (inferiorsBody == superiorsBody) return;

            HierarchyBody.Merge(inferiorsBody, superiorsBody);
        }
    }

    public HierarchyBodyItem Join(HierarchyPosition? targetPosition, bool joinAsSuperior, T value, HierarchyPosition myPosition)
    {
        Debug.Assert(myPosition.IsInstalled == false);

        lock (GlobalLock)
            return this.First.GetLast().Join(targetPosition, joinAsSuperior, value, myPosition);
    }

    public void Resign(HierarchyBodyItem me)
    {
        Debug.Assert(me.Position.IsInstalled);

        lock (GlobalLock)
            this.First.GetLast().Resign(me);
    }

    public HierarchyBodyItem[] ToArrayWithPositions()
    {
        lock (GlobalLock)
            return this.First.GetLast().ToArray();
    }

    public HierarchyBodyItem[] ItemsWithPositionsReadOnly { get => ToArrayWithPositions(); }

    public T[] ToArray() => ToArrayWithPositions().Select(x => x.Value).ToArray();
    public T[] ItemsReadOnly { get => ToArray(); }
}

public class LocalTimer
{
    SortedSet<long> List = new SortedSet<long>();
    HashSet<long> Hash = new HashSet<long>();
    public long Now { get; private set; } = FastTick64.Now;
    public bool AutomaticUpdateNow { get; }

    int AddCount = 0;

    public LocalTimer(bool automaticUpdateNow = true)
    {
        AutomaticUpdateNow = automaticUpdateNow;
    }

    public void UpdateNow() => Now = FastTick64.Now;
    public void UpdateNow(long nowTick) => Now = nowTick;

    public long AddTick(long tick)
    {
        if (Hash.Add(tick))
            List.Add(tick);

        if (((AddCount++) % 100) == 0)
        {
            // 100 回に 1 回くらいは Gc をいたします
            GcInternal(true);
        }

        return tick;
    }

    public long AddTimeout(int interval)
    {
        if (interval == Timeout.Infinite) return long.MaxValue;
        interval = Math.Max(interval, 0);
        if (AutomaticUpdateNow) UpdateNow();
        long v = Now + interval;
        AddTick(v);
        return v;
    }

    int GcInternal(bool remainOneAtLeast = false)
    {
        int ret = Timeout.Infinite;
        if (AutomaticUpdateNow) UpdateNow();
        long now = Now;
        List<long>? deleteList = null;
        int n = 0;

        foreach (long v in List)
        {
            if (now >= v)
            {
                ret = 0;

                n++;
                if (remainOneAtLeast == false || (n >= 2))
                {
                    if (deleteList == null) deleteList = new List<long>();
                    deleteList.Add(v);
                }
            }
            else
            {
                break;
            }
        }

        if (deleteList != null)
        {
            foreach (long v in deleteList)
            {
                List.Remove(v);
                Hash.Remove(v);
            }
        }

        return ret;
    }

    public int GetNextInterval()
    {
        int ret = Timeout.Infinite;
        if (AutomaticUpdateNow) UpdateNow();
        long now = Now;

        ret = GcInternal();

        if (ret == Timeout.Infinite)
        {
            if (List.Count >= 1)
            {
                long v = List.First();
                ret = (int)(v - now);
                Debug.Assert(ret > 0);
                if (ret <= 0) ret = 0;
            }
        }

        return ret;
    }
}

public class AsyncLocalTimer
{
    LocalTimer Timer = new LocalTimer(true);
    readonly CriticalSection LockObj = new CriticalSection<AsyncLocalTimer>();
    public long Now => Timer.Now;

    AsyncAutoResetEvent TimerChangedEvent = new AsyncAutoResetEvent();

    public long AddTick(long tick)
    {
        long ret;

        lock (LockObj)
            ret = Timer.AddTick(tick);

        TimerChangedEvent.Set();

        return ret;
    }

    public long AddTimeout(int interval)
    {
        long ret;

        lock (LockObj)
            ret = Timer.AddTimeout(interval);

        TimerChangedEvent.Set();

        return ret;
    }

    public bool RepeatIntervalTimer(int interval, ref long nextFireTick, bool beginNow = true)
    {
        long now = Now;

        if (nextFireTick == 0 || now >= nextFireTick)
        {
            if (nextFireTick == 0)
                nextFireTick = now;

            if (beginNow)
                nextFireTick = now + interval;
            else
                nextFireTick = Math.Max(nextFireTick + interval, now);

            if (interval < 0)
                nextFireTick = long.MaxValue;

            if (nextFireTick != long.MaxValue)
            {
                AddTick(nextFireTick);
            }

            return true;
        }

        return false;
    }

    public int GetNextInterval()
    {
        lock (LockObj)
            return Timer.GetNextInterval();
    }

    public async Task<bool> WaitUntilNextTickAsync(CancellationToken cancel = default)
    {
        while (true)
        {
            int nextInterval = GetNextInterval();

            if (nextInterval == 0)
                return true;

            var reason = await TaskUtil.WaitObjectsAsync(cancels: cancel._SingleArray(), events: this.TimerChangedEvent._SingleArray(), timeout: nextInterval);

            if (reason == ExceptionWhen.CancelException)
                return false;

            if (reason == ExceptionWhen.TimeoutException)
                return true;
        }
    }
}

public readonly struct BackgroundStateDataUpdatePolicy
{
    public readonly int InitialPollingInterval;
    public readonly int MaxPollingInterval;
    public readonly int IdleTimeoutToFreeThreadInterval;

    public const int DefaultInitialPollingInterval = 1 * 1000;
    public const int DefaultMaxPollingInterval = 60 * 1000;
    public const int DefaultIdleTimeoutToFreeThreadInterval = 180 * 1000;

    public BackgroundStateDataUpdatePolicy(int initialPollingInterval = DefaultInitialPollingInterval,
        int maxPollingInterval = DefaultMaxPollingInterval,
        int timeoutToStopThread = DefaultIdleTimeoutToFreeThreadInterval)
    {
        InitialPollingInterval = initialPollingInterval;
        MaxPollingInterval = maxPollingInterval;
        IdleTimeoutToFreeThreadInterval = timeoutToStopThread;
    }

    public static BackgroundStateDataUpdatePolicy Default { get; }
        = new BackgroundStateDataUpdatePolicy(1 * 1000, 60 * 1000, 30 * 1000);

    public BackgroundStateDataUpdatePolicy SafeValue
    {
        get
        {
            return new BackgroundStateDataUpdatePolicy(
                Math.Max(this.InitialPollingInterval, 1 * 100),
                Math.Max(this.MaxPollingInterval, 1 * 500),
                Math.Max(Math.Max(this.IdleTimeoutToFreeThreadInterval, 1 * 500), this.MaxPollingInterval)
                );
        }
    }
}

public abstract class BackgroundStateDataBase : IEquatable<BackgroundStateDataBase>
{
    public DateTimeOffset TimeStamp { get; } = DateTimeOffset.Now;
    public long TickTimeStamp { get; } = FastTick64.Now;

    public abstract BackgroundStateDataUpdatePolicy DataUpdatePolicy { get; }

    public abstract bool Equals(BackgroundStateDataBase? other);

    public abstract void RegisterSystemStateChangeNotificationCallbackOnlyOnceImpl(Action callMe);
}

public static class BackgroundState<TData>
    where TData : BackgroundStateDataBase, new()
{
    public struct CurrentData
    {
        public int Version;
        public TData? Data;
    }

    public static CurrentData Current
    {
        get
        {
            CurrentData d = new CurrentData();
            d.Data = GetState();
            d.Version = InternalVersion;
            return d;
        }
    }

    public static TData GetOnce()
    {
        return new TData();
    }

    static volatile TData? CacheData = null;

    static volatile int NumRead = 0;

    static volatile int InternalVersion = 1;

    static bool CallbackIsRegistered = false;

    static readonly CriticalSection LockObj = new CriticalSection();
    static Task? task = null;
    static AsyncAutoResetEvent threadSignal = new AsyncAutoResetEvent();
    static bool callbackIsCalled = false;

    public static FastEventListenerList<TData, int> EventListener { get; } = new FastEventListenerList<TData, int>();

    static TData? TryGetTData()
    {
        try
        {
            TData ret = new TData();

            if (CallbackIsRegistered == false)
            {
                try
                {
                    ret.RegisterSystemStateChangeNotificationCallbackOnlyOnceImpl(() =>
                    {
                        callbackIsCalled = true;
                        GetState();
                        threadSignal.Set();
                    });

                    CallbackIsRegistered = true;
                }
                catch { }
            }

            return ret;
        }
        catch
        {
            return null;
        }
    }

    static TData? GetState()
    {
        NumRead++;

        if (CacheData != null)
        {
            if (task == null)
            {
                EnsureStartTaskIfStopped(CacheData.DataUpdatePolicy);
            }

            return CacheData;
        }
        else
        {
            BackgroundStateDataUpdatePolicy updatePolicy = BackgroundStateDataUpdatePolicy.Default;
            TData? data = TryGetTData();
            if (data != null)
            {
                updatePolicy = data.DataUpdatePolicy;

                bool inc = false;
                if (CacheData == null)
                {
                    inc = true;
                }
                else
                {
                    if (CacheData.Equals(data) == false)
                        inc = true;
                }
                CacheData = data;

                if (inc)
                {
                    InternalVersion++;
                    EventListener.Fire(CacheData, 0, null);
                }
            }

            EnsureStartTaskIfStopped(updatePolicy);

            return CacheData;
        }
    }

    static void EnsureStartTaskIfStopped(BackgroundStateDataUpdatePolicy updatePolicy)
    {
        lock (LockObj)
        {
            if (task == null)
            {
                task = TaskUtil.StartAsyncTaskAsync(MaintainTaskProcAsync, updatePolicy, leakCheck: false);
            }
        }
    }

    static int nextInterval = 0;

    static async Task MaintainTaskProcAsync(object? param)
    {
        BackgroundStateDataUpdatePolicy policy = (BackgroundStateDataUpdatePolicy)param!;
        policy = policy.SafeValue;

        LocalTimer tm = new LocalTimer();

        if (nextInterval == 0)
        {
            nextInterval = policy.InitialPollingInterval;
        }

        long nextGetDataTick = tm.AddTimeout(nextInterval);

        long nextIdleDetectTick = tm.AddTimeout(policy.IdleTimeoutToFreeThreadInterval);

        int lastNumRead = NumRead;

        while (true)
        {
            if (FastTick64.Now >= nextGetDataTick || callbackIsCalled)
            {
                TData? data = TryGetTData();

                nextInterval = Math.Min(nextInterval + policy.InitialPollingInterval, policy.MaxPollingInterval);
                bool inc = false;

                if (data != null)
                {
                    if (data.Equals(CacheData!) == false)
                    {
                        nextInterval = policy.InitialPollingInterval;
                        inc = true;
                    }
                    CacheData = data;
                }
                else
                {
                    nextInterval = policy.InitialPollingInterval;
                }

                if (callbackIsCalled)
                {
                    nextInterval = policy.InitialPollingInterval;
                }

                if (inc)
                {
                    InternalVersion++;
                    EventListener.Fire(CacheData!, 0, null);
                }

                nextGetDataTick = tm.AddTimeout(nextInterval);

                callbackIsCalled = false;
            }

            if (FastTick64.Now >= nextIdleDetectTick)
            {
                int numread = NumRead;
                if (lastNumRead != numread)
                {
                    lastNumRead = numread;
                    nextIdleDetectTick = tm.AddTimeout(policy.IdleTimeoutToFreeThreadInterval);
                }
                else
                {
                    task = null;
                    return;
                }
            }

            int i = tm.GetNextInterval();

            i = Math.Max(i, 100);

            await threadSignal.WaitOneAsync(i, leakCounterKind: LeakCounterKind.DoNotTrack);
        }
    }
}

public class TaskVarObject
{
    Dictionary<string, object> data = new Dictionary<string, object>();

    public object? Get(Type type) => Get(type.AssemblyQualifiedName!);
    public void Set(Type type, object? obj) => Set(type.AssemblyQualifiedName!, obj);

    public object? Get(string key)
    {
        lock (data)
        {
            if (data.ContainsKey(key))
                return data[key];
            else
                return null;
        }
    }
    public void Set(string key, object? obj)
    {
        lock (data)
        {
            if (obj != null)
            {
                if (data.ContainsKey(key) == false)
                    data.Add(key, obj);
                else
                    data[key] = obj;
            }
            else
            {
                if (data.ContainsKey(key))
                    data.Remove(key);
            }
        }
    }
}

public static class TaskVar<T>
{
    public static T Value { get => TaskVar.Get<T>()!; set => TaskVar.Set<T>(value); }
}

public static class TaskVar
{
    internal static readonly AsyncLocal<TaskVarObject> AsyncLocalObj = new AsyncLocal<TaskVarObject>();

    [return: MaybeNull]
    public static T Get<T>()
    {
        var v = AsyncLocalObj.Value;
        if (v == null) return default(T)!;

        T ret = (T)v.Get(typeof(T))!;
        return ret;
    }
    public static void Set<T>([AllowNull] T obj)
    {
        if (AsyncLocalObj.Value == null) AsyncLocalObj.Value = new TaskVarObject();
        AsyncLocalObj.Value.Set(typeof(T), obj);
    }

    public static object? Get(string name) => AsyncLocalObj.Value!.Get(name);
    public static void Set(string name, object? obj) => AsyncLocalObj.Value!.Set(name, obj);
}

public class AsyncOneShotTester : AsyncService
{
    Task t;
    public AsyncOneShotTester(Func<CancellationToken, Task> Proc)
    {
        t = Proc(this.GrandCancel);
        t._TryWaitAsync(false)._LaissezFaire();
    }

    protected override async Task CleanupImplAsync(Exception? ex)
    {
        try
        {
            await t;
        }
        finally
        {
            await base.CleanupImplAsync(ex);
        }
    }
}

public class RefCounterHolder : IDisposable
{
    RefCounter Counter;
    public RefCounterHolder(RefCounter counter)
    {
        Counter = counter;

        Counter.InternalIncrement();
    }

    public void Dispose() { this.Dispose(true); GC.SuppressFinalize(this); }
    Once DisposeFlag;
    protected virtual void Dispose(bool disposing)
    {
        if (!disposing || DisposeFlag.IsFirstCall() == false) return;

        Counter.InternalDecrement();
    }
}

public interface IAsyncClosable : IAsyncDisposable, IDisposable
{
    Exception? LastError { get; }
}

public enum RefCounterEventType
{
    Increment,
    Decrement,
    AllReleased,
}

public class RefCounter
{
    int counter = 0;

    public int Counter => counter;
    public bool IsZero => (counter == 0);

    readonly CriticalSection LockObj = new CriticalSection<RefCounter>();

    public FastEventListenerList<RefCounter, RefCounterEventType> EventListener = new FastEventListenerList<RefCounter, RefCounterEventType>();

    internal void InternalIncrement()
    {
        lock (LockObj)
        {
            Interlocked.Increment(ref counter);
            EventListener.Fire(this, RefCounterEventType.Increment, null);
        }
    }

    internal void InternalDecrement()
    {
        lock (LockObj)
        {
            int r = Interlocked.Decrement(ref counter);
            Debug.Assert(r >= 0);
            EventListener.Fire(this, RefCounterEventType.Decrement, null);

            if (r == 0)
                EventListener.Fire(this, RefCounterEventType.AllReleased, null);
        }
    }

    public RefCounterHolder AddRef() => new RefCounterHolder(this);
}

public class RefCounterObjectHandle<T> : IDisposable
{
    public T Object { get; }
    readonly RefCounterHolder CounterHolder;

    public RefCounterObjectHandle(T obj, Action onDispose)
    {
        this.Object = obj;
        RefCounter counter = new RefCounter();
        counter.EventListener.RegisterCallback((caller, eventType, state, evState) =>
        {
            switch (eventType)
            {
                case RefCounterEventType.AllReleased:
                    onDispose();
                    break;
            }
        });
        this.CounterHolder = counter.AddRef();
    }

    public RefCounterObjectHandle(RefCounter counter, T obj)
    {
        this.Object = obj;
        this.CounterHolder = counter.AddRef();
    }

    public void Dispose() { this.Dispose(true); GC.SuppressFinalize(this); }
    Once DisposeFlag;
    protected virtual void Dispose(bool disposing)
    {
        if (!disposing || DisposeFlag.IsFirstCall() == false) return;
        this.CounterHolder.Dispose();
    }
}

public abstract class ObjectPoolBase<TObject, TParam> : IDisposable, IAsyncDisposable
    where TObject : IAsyncClosable
{
    long AccessCounter = 0;

    public class ObjectEntry : IDisposable, IAsyncDisposable
    {
        public RefCounter Counter { get; } = new RefCounter();
        public TObject Object { get; }
        public string Key { get; }
        public long LastReleasedTick { get; private set; } = 0;
        public long LastAccessCounterValue = 0;

        readonly CriticalSection LockObj = new CriticalSection<ObjectEntry>();

        readonly ObjectPoolBase<TObject, TParam> PoolBase;

        public ObjectEntry(ObjectPoolBase<TObject, TParam> poolBase, TObject targetObject, string key)
        {
            this.Object = targetObject;
            this.PoolBase = poolBase;
            this.Key = key;

            this.Counter.EventListener.RegisterCallback((caller, eventType, state, evState) =>
            {
                lock (LockObj)
                {
                    switch (eventType)
                    {
                        case RefCounterEventType.AllReleased:
                            this.LastReleasedTick = Tick64.Now;
                            break;
                    }
                }
            });
        }

        public RefCounterObjectHandle<TObject>? TryGetIfAlive()
        {
            try
            {
                long now = Tick64.Now;

                lock (LockObj)
                {
                    if (this.Object.LastError == null)
                    {
                        this.LastReleasedTick = 0;

                        return new RefCounterObjectHandle<TObject>(this.Counter, this.Object);
                    }
                }
            }
            catch { }

            return null;
        }

        public async Task CloseAndDisposeAsync()
        {
            try
            {
                await this.Object.DisposeAsync();
            }
            catch { }

            await this._DisposeSafeAsync();
        }

        public void Dispose() { this.Dispose(true); GC.SuppressFinalize(this); }
        Once DisposeFlag;
        public virtual async ValueTask DisposeAsync()
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
            await this.Object._DisposeSafeAsync();
        }
    }

    readonly Dictionary<string, ObjectEntry> ObjectList;
    readonly AsyncLock LockAsyncObj = new AsyncLock();
    readonly CancellationTokenSource CancelSource = new CancellationTokenSource();

    public int LifeTime { get; }
    public int MaxObjects { get; }

    readonly AsyncAutoResetEvent FireGcNowEvent = new AsyncAutoResetEvent();

    Task GcTask;

    public ObjectPoolBase(int lifeTime, int maxObjects, IEqualityComparer<string> comparer)
    {
        this.LifeTime = Math.Max(lifeTime, 0);
        this.MaxObjects = maxObjects;

        ObjectList = new Dictionary<string, ObjectEntry>(comparer);
        GcTask = GcTaskProc();
    }

    protected abstract Task<TObject> OpenImplAsync(string key, TParam param, CancellationToken cancel);

    public async Task<RefCounterObjectHandle<TObject>> OpenOrGetAsync(string key, TParam param, CancellationToken cancel = default)
    {
        await using (TaskUtil.CreateCombinedCancellationToken(out CancellationToken cancelOp, CancelSource.Token, cancel))
        {
            int numRetry = 0;
            if (this.DisposeFlag.IsSet)
                throw new ObjectDisposedException($"ObjectPoolBase<T>");

            L_RETRY:

            if (numRetry >= 1)
                await Task.Yield();

            RefCounterObjectHandle<TObject>? ret;

            using (await LockAsyncObj.LockWithAwait(cancelOp))
            {
                cancelOp.ThrowIfCancellationRequested();

                if (this.DisposeFlag.IsSet)
                    throw new ObjectDisposedException($"ObjectPoolBase<T>");

                if (ObjectList.TryGetValue(key, out ObjectEntry? entry) == false)
                {
                    await Task.Yield();
                    TObject t = await OpenImplAsync(key, param, cancelOp);
                    entry = new ObjectEntry(this, t, key);
                    ObjectList.Add(key, entry);
                }

                entry!.LastAccessCounterValue = Interlocked.Increment(ref this.AccessCounter);

                ret = entry.TryGetIfAlive();
                if (ret == null)
                {
                    ObjectList.Remove(key);
                    await entry.CloseAndDisposeAsync()._TryWaitAsync();
                    numRetry++;
                    if (numRetry >= 100)
                        throw new ApplicationException("numRetry >= 100");
                    goto L_RETRY;
                }
            }

            if (ObjectList.Count > this.MaxObjects)
            {
                FireGcNowEvent.Set(true);
            }

            return ret;
        }
    }

    public async Task EnumAndCloseHandlesAsync(Func<string, TObject, Task<bool>> enumProc, Func<Task>? afterClosedProc = null, Comparison<TObject>? sortProc = null, CancellationToken cancel = default)
    {
        await using (TaskUtil.CreateCombinedCancellationToken(out CancellationToken cancelOp, CancelSource.Token, cancel))
        {
            using (await LockAsyncObj.LockWithAwait(cancelOp))
            {
                cancelOp.ThrowIfCancellationRequested();

                if (this.DisposeFlag.IsSet)
                    throw new ObjectDisposedException($"ObjectPoolBase<T>");

                List<ObjectEntry> closeList = new List<ObjectEntry>();

                foreach (ObjectEntry entry in this.ObjectList.Values)
                {
                    cancelOp.ThrowIfCancellationRequested();

                    try
                    {
                        if (await enumProc(entry.Key, entry.Object) == true)
                            closeList.Add(entry);
                    }
                    catch { }
                }

                if (sortProc != null)
                    closeList.Sort((x, y) => sortProc(x.Object, y.Object));

                foreach (ObjectEntry entry in closeList)
                {
                    cancelOp.ThrowIfCancellationRequested();

                    try
                    {
                        await entry.CloseAndDisposeAsync();
                    }
                    catch { }

                    ObjectList.Remove(entry.Key);
                }

                if (afterClosedProc != null)
                {
                    await afterClosedProc();
                }
            }
        }
    }

    async Task GcTaskProc()
    {
        CancellationToken cancel = this.CancelSource.Token;

        while (true)
        {
            try
            {
                cancel.ThrowIfCancellationRequested();

                await TaskUtil.WaitObjectsAsync(cancels: cancel._SingleArray(), events: FireGcNowEvent._SingleArray(), timeout: Math.Max(LifeTime / 2, 100));

                cancel.ThrowIfCancellationRequested();


                long now = Tick64.Now;

                using (await LockAsyncObj.LockWithAwait(cancel))
                {
                    HashSet<ObjectEntry> gcTargetList = new HashSet<ObjectEntry>();

                    foreach (ObjectEntry entry in this.ObjectList.Values)
                    {
                        if (entry.LastReleasedTick != 0)
                        {
                            if ((entry.LastReleasedTick + LifeTime) < now)
                            {
                                gcTargetList.Add(entry);
                            }
                            else if (entry.Object.LastError != null)
                            {
                                gcTargetList.Add(entry);
                            }
                        }
                    }

                    int numRemains = this.ObjectList.Count - gcTargetList.Count;
                    if (numRemains > this.MaxObjects)
                    {
                        int numToDelete = numRemains - this.MaxObjects;

                        //Con.WriteLine(numToDelete);

                        this.ObjectList.Values.Where(x => gcTargetList.Contains(x) == false).OrderBy(x => x.LastAccessCounterValue).Take(numToDelete)._DoForEach(x => gcTargetList.Add(x));
                    }

                    foreach (ObjectEntry deleteTargetEntry in gcTargetList)
                    {
                        cancel.ThrowIfCancellationRequested();

                        this.ObjectList.Remove(deleteTargetEntry.Key);

                        try
                        {
                            await deleteTargetEntry.CloseAndDisposeAsync();
                        }
                        catch (Exception ex)
                        {
                            Con.WriteDebug(ex.ToString());
                        }
                    }
                }
            }
            catch (Exception ex) when (!(ex is OperationCanceledException || ex is TaskCanceledException))
            {
                Con.WriteDebug("GcTaskProc: " + ex.ToString());
                await Task.Delay(10);
            }
        }
    }

    public void Dispose() { this.Dispose(true); GC.SuppressFinalize(this); }
    Once DisposeFlag;
    public virtual async ValueTask DisposeAsync()
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
        this.CancelSource.Cancel();

        await GcTask._TryWaitAsync(true);

        using (LockAsyncObj.LockLegacy())
        {
            foreach (ObjectEntry entry in this.ObjectList.Values)
            {
                await entry._DisposeSafeAsync();
            }
            this.ObjectList.Clear();
        }
    }
}

public class SingleThreadWorker : IDisposable
{
    class Job
    {
        public Func<object?, object?> Proc;
        public object? Param;
        public AsyncManualResetEvent Completed = new AsyncManualResetEvent();
        public Exception? Error = null;
        public object? Result = null;

        public Job(Func<object?, object?> proc, object? param)
        {
            this.Proc = proc;
            this.Param = param;
        }
    }

    readonly ThreadObj? Thread;
    readonly CriticalSection LockObj = new CriticalSection<SingleThreadWorker>();
    readonly Queue<Job> Queue = new Queue<Job>();
    readonly Event QueueInsertedEvent = new Event(false);
    readonly CancellationTokenSource CancalSource = new CancellationTokenSource();

    readonly IHolder Leak;

    public bool SelfThread { get; }
    public int SelfThreadId { get; }

    public SingleThreadWorker(string name = "", bool selfThread = false)
    {
        this.SelfThread = selfThread;

        if (this.SelfThread == false)
        {
            Thread = new ThreadObj(ThreadProc, name: name, isBackground: true);
        }
        else
        {
            if (System.Threading.Thread.CurrentThread.IsThreadPoolThread)
            {
                throw new ArgumentException("selfThread == true while CurrentThread.IsThreadPoolThread");
            }

            this.SelfThreadId = Environment.CurrentManagedThreadId;
        }

        Leak = LeakChecker.Enter(LeakCounterKind.SingleThreadWorker);
    }

    public async Task<TResult> ExecAsync<TResult, TParam>(Func<TParam, TResult> proc, [AllowNull] TParam param, int timeout = Timeout.Infinite, CancellationToken cancel = default)
    {
        await Task.Yield();

        if (DisposeFlag.IsSet) throw new ObjectDisposedException("SingleWorkerThread");

        if (this.SelfThread)
        {
            if (timeout != Timeout.Infinite) throw new ArgumentException("this.SelfThread == true && timeout != Timeout.Infinite");
            if (cancel.CanBeCanceled) throw new ArgumentException("this.SelfThread == true && cancel.CanBeCanceled == true");
            if (this.SelfThreadId != Environment.CurrentManagedThreadId) throw new ApplicationException("this.SelfThreadId != Environment.CurrentManagedThreadId");

            return proc(param!);
        }

        Job job = new Job((p) => proc((TParam)p!), param);

        lock (this.LockObj)
        {
            this.Queue.Enqueue(job);
        }

        this.QueueInsertedEvent.Set();

        await TaskUtil.WaitObjectsAsync(cancels: new CancellationToken[] { cancel, this.CancalSource.Token },
                manualEvents: job.Completed._SingleArray(),
                timeout: timeout, exceptions: ExceptionWhen.All);

        if (job.Error != null) throw job.Error;

        return (TResult)job.Result!;
    }

    public Task ExecAsync<TParam>(Action<TParam> proc, [AllowNull] TParam param, int timeout = Timeout.Infinite, CancellationToken cancel = default)
        => ExecAsync<int, TParam>(p => { proc(p); return 0; }, param, timeout, cancel)!;

    void ThreadProc(object? param)
    {
        while (CancalSource.IsCancellationRequested == false)
        {
            if (CancalSource.IsCancellationRequested)
            {
                return;
            }

            Job? nextJob = null;
            bool wait = false;

            lock (this.LockObj)
            {
                if (this.Queue.TryDequeue(out nextJob) == false)
                {
                    wait = true;
                }
            }

            if (nextJob != null)
            {
                try
                {
                    object? ret = nextJob.Proc(nextJob.Param);

                    nextJob.Result = ret;
                }
                catch (Exception ex)
                {
                    nextJob.Error = ex;
                }

                nextJob.Completed.Set(true);
            }

            if (wait)
            {
                QueueInsertedEvent.Wait();
            }
        }
    }

    public void Dispose() { this.Dispose(true); GC.SuppressFinalize(this); }
    Once DisposeFlag;
    protected virtual void Dispose(bool disposing)
    {
        if (!disposing || DisposeFlag.IsFirstCall() == false) return;

        if (this.SelfThread == false)
        {
            CancalSource.Cancel();
            this.QueueInsertedEvent.Set();
            this.Thread!.WaitForEnd();
        }

        Leak._DisposeSafe();
    }
}
public class DialogSessionOptions
{
    public int MaxSessionsPerQuotaKey { get; }
    public Func<DialogSession, CancellationToken, Task> MainAsyncCallback { get; }
    public object? Param { get; }

    public DialogSessionOptions(Func<DialogSession, CancellationToken, Task> mainAsyncCallback, int maxSessionsPerQuotaKey, object? param = null)
    {
        MainAsyncCallback = mainAsyncCallback;
        this.Param = param;

        if (maxSessionsPerQuotaKey <= 0) maxSessionsPerQuotaKey = int.MaxValue;
        this.MaxSessionsPerQuotaKey = maxSessionsPerQuotaKey;
    }
}

public interface IDialogRequestData { }

public interface IDialogResponseData { }

public class EmptyDialogResponseData : IDialogResponseData { }

[Flags]
public enum DialogRequestStatus
{
    Running = 0,
    Ok,
    Error,
}

public class DialogRequest
{
    public string RequestId { get; }
    public DialogRequestStatus Status { get; private set; } = DialogRequestStatus.Running;
    public DialogSession Session { get; }
    public int HardTimeout { get; }
    public int SoftTimeout { get; }
    public IDialogRequestData RequestData { get; }
    public IDialogResponseData? ResponseData { get; private set; }
    public Exception? Exception { get; private set; }
    public bool IsFinalAnswer { get; }
    readonly RefInt HeartBeatCounter = new RefInt();

    readonly AsyncManualResetEvent FinishedEvent = new AsyncManualResetEvent();

    internal DialogRequest(EnsureSpecial internalOnly, DialogSession session, IDialogRequestData requestData, int hardTimeout, int? softTimeout = null, bool isFinalAnswer = false)
    {
        this.RequestId = Str.NewUid("REQUEST", '_');
        this.Session = session;
        this.RequestData = requestData;
        this.HardTimeout = hardTimeout;
        this.SoftTimeout = softTimeout ?? hardTimeout;
        this.IsFinalAnswer = isFinalAnswer;
    }

    Once SetResponseOrErrorOnce;

    public void SetResponseDataEmpty() => SetResponseData(new EmptyDialogResponseData());

    public void SetResponseData(IDialogResponseData response)
    {
        response._NullCheck();

        if (SetResponseOrErrorOnce.IsFirstCall() == false) return;

        this.ResponseData = response;
        this.Status = DialogRequestStatus.Ok;
        this.FinishedEvent.Set(true);
        this.Session.NoticeResponseFulfilledInternal(this);
    }

    public void SetResponseException(Exception exception)
    {
        exception._NullCheck();

        if (SetResponseOrErrorOnce.IsFirstCall() == false) return;

        this.Exception = exception;
        this.Status = DialogRequestStatus.Error;
        this.FinishedEvent.Set(true);
        this.Session.NoticeResponseFulfilledInternal(this);
    }

    public void SetResponseCancel()
    {
        SetResponseException(new OperationCanceledException());
    }

    public void HeartBeat()
    {
        this.HeartBeatCounter.Increment();
    }

    public async Task<IDialogResponseData> WaitForResponseAsync(CancellationToken cancel = default)
    {
        if (this.Status != DialogRequestStatus.Running)
        {
            if (this.Status == DialogRequestStatus.Error)
            {
                throw this.Exception._NullCheck();
            }
            else
            {
                return this.ResponseData._NullCheck();
            }
        }

        long now = TickNow;

        long hardTimeoutGiveupTime = this.HardTimeout >= 1 ? now + this.HardTimeout : long.MaxValue;
        int lastHeartBeatCounter = -1;

        try
        {
            LABEL_TIMEOUT_RETRY:
            now = TickNow;

            int timeout = int.MaxValue;

            if (hardTimeoutGiveupTime != long.MaxValue)
            {
                int hardTimeoutInterval = (int)(hardTimeoutGiveupTime - now);
                if (hardTimeoutInterval <= 0)
                {
                    throw new TimeoutException();
                }

                timeout = Math.Min(timeout, hardTimeoutInterval);
            }

            if (this.SoftTimeout > 0)
            {
                timeout = Math.Min(timeout, this.SoftTimeout);

                if (lastHeartBeatCounter == this.HeartBeatCounter)
                {
                    throw new TimeoutException();
                }

                lastHeartBeatCounter = this.HeartBeatCounter;
            }

            var result = await TaskUtil.WaitObjectsAsync(cancels: cancel._SingleArray(), manualEvents: FinishedEvent._SingleArray(), timeout: timeout, exceptions: ExceptionWhen.CancelException | ExceptionWhen.TaskException);

            if (result == ExceptionWhen.TimeoutException)
            {
                goto LABEL_TIMEOUT_RETRY;
            }

            if (this.Status == DialogRequestStatus.Running)
            {
                throw new CoresLibException("this.Status == DialogRequestStatus.Running");
            }

            if (this.Status == DialogRequestStatus.Error)
            {
                throw this.Exception._NullCheck();
            }
            else
            {
                return this.ResponseData._NullCheck();
            }
        }
        catch (Exception ex)
        {
            SetResponseException(ex);
            throw;
        }
    }
}

public class DialogSession : AsyncService
{
    public string SessionId { get; }
    public string QuotaKey { get; }
    public DialogSessionManager Manager { get; }
    public DialogSessionOptions Options { get; }
    public object? Param => Options.Param;
    public Task? MainTask { get; private set; } = null;
    public ConcurrentDictionary<string, object?> AppItemsList { get; } = new ConcurrentDictionary<string, object?>();

    public Exception? Exception { get; private set; } = null;
    public bool IsFinished { get; private set; } = false;

    public long Expires { get; private set; } = 0;

    readonly CriticalSection<DialogSession> RequestListLockObj = new CriticalSection<DialogSession>();
    readonly List<DialogRequest> RequestList = new List<DialogRequest>();

    internal DialogSession(EnsureSpecial internalOnly, DialogSessionManager manager, DialogSessionOptions options, string quotaKey)
    {
        try
        {
            this.SessionId = Str.NewUid(manager.Options.SessionIdPrefix, '_');
            this.Manager = manager;
            this.Options = options;
            this.QuotaKey = quotaKey._NonNull();
        }
        catch (Exception ex)
        {
            this._DisposeSafe(ex);
            throw;
        }
    }

    Once Started;

    public void Debug(object? obj)
    {
        string str = obj._GetObjectDump()._NonNullTrim();

        $"Session '{this.SessionId}': {str}"._Debug();
    }

    public void Error(object? obj)
    {
        string str = obj._GetObjectDump()._NonNullTrim();

        $"Session '{this.SessionId}': {str}"._Error();
    }

    internal void StartInternal()
    {
        if (Started.IsFirstCall())
        {
            this.MainTask = TaskUtil.StartAsyncTaskAsync(async () =>
            {
                try
                {
                    await Options.MainAsyncCallback(this, this.GrandCancel);
                }
                catch (Exception ex)
                {
                    this.Exception = ex;
                    ex._Error();
                }
                finally
                {
                    this.IsFinished = true;
                    if (this.Manager.Options.ExpiresAfterFinishedMsec >= 0)
                    {
                        this.Expires = TickNow + this.Manager.Options.ExpiresAfterFinishedMsec;
                    }
                    this.Pulse.FirePulse(true);
                }
            });
        }
    }

    internal void NoticeResponseFulfilledInternal(DialogRequest request)
    {
        if (request.IsFinalAnswer == false)
        {
            lock (this.RequestListLockObj)
            {
                this.RequestList.Remove(request);
            }
        }

        Pulse.FirePulse(true);
    }

    public DialogRequest Request(IDialogRequestData requestData, int hardTimeout, int? softTimeout = null, bool isFinalAnswer = false)
    {
        this.GrandCancel.ThrowIfCancellationRequested();

        DialogRequest request = new DialogRequest(EnsureSpecial.Yes, this, requestData, hardTimeout, softTimeout, isFinalAnswer);

        lock (this.RequestListLockObj)
        {
            this.RequestList.Add(request);
        }

        Pulse.FirePulse(true);

        return request;
    }

    public async Task<IDialogResponseData> RequestAndWaitResponseAsync(IDialogRequestData requestData, int hardTimeout, int? softTimeout = null, CancellationToken cancel = default, bool isFinalAnswer = false)
    {
        try
        {
            this.Debug($"RequestAndWaitResponseAsync start. Requested Data = {requestData.GetType().ToString()}");

            cancel.ThrowIfCancellationRequested();
            this.GrandCancel.ThrowIfCancellationRequested();

            var req = this.Request(requestData, hardTimeout, softTimeout, isFinalAnswer);

            await using (TaskUtil.CreateCombinedCancellationToken(out CancellationToken cancel2, cancel, this.GrandCancel))
            {
                var ret = await req.WaitForResponseAsync(cancel2);

                this.Debug($"RequestAndWaitResponseAsync finished. Response Data = {requestData.GetType().ToString()}");

                return ret;
            }
        }
        catch (Exception ex)
        {
            this.Error($"RequestAndWaitResponseAsync Error. Exception = {ex._GetObjectDump()}");
            throw;
        }
    }

    readonly AsyncPulse Pulse = new AsyncPulse();

    public DialogRequest? GetFinalAnswerRequest()
    {
        if (this.IsFinished == false)
        {
            lock (this.RequestListLockObj)
            {
                if (this.RequestList.Count >= 1)
                {
                    var r = this.RequestList[0];
                    if (r.IsFinalAnswer)
                    {
                        return r;
                    }
                }
            }
        }

        if (this.Exception != null)
        {
            throw this.Exception;
        }
        else
        {
            return null;
        }
    }

    public async Task<DialogRequest?> GetNextRequestAsync(int timeout = Timeout.Infinite, CancellationToken cancel = default)
    {
        await Task.Yield();

        await using (this.CreatePerTaskCancellationToken(out CancellationToken cancel2, cancel))
        {
            var waiter = Pulse.GetPulseWaiter();

            long giveupTick = (timeout < 0) ? long.MaxValue : TickNow + timeout;

            while (cancel2.IsCancellationRequested == false && this.IsFinished == false)
            {
                lock (this.RequestListLockObj)
                {
                    if (this.RequestList.Count >= 1)
                    {
                        return this.RequestList[0];
                    }
                }

                int timeout2 = giveupTick == long.MaxValue ? int.MaxValue : (int)(giveupTick - TickNow);

                if (timeout2 <= 0) throw new TimeoutException();

                await waiter.WaitAsync(timeout2, cancel2);
            }

            if (this.IsFinished == false)
            {
                throw new OperationCanceledException();
            }
            else if (this.Exception != null)
            {
                throw this.Exception;
            }
            else
            {
                return null;
            }
        }
    }

    public DialogRequest? GetRequestById(string requestId)
    {
        lock (this.RequestListLockObj)
        {
            return this.RequestList.Where(x => x.RequestId._IsSamei(requestId)).FirstOrDefault();
        }
    }

    public bool SetResponseData(string requestId, IDialogResponseData response)
    {
        var request = GetRequestById(requestId);
        if (request != null)
        {
            request.SetResponseData(response);
            return true;
        }

        return false;
    }

    public bool SetResponseException(string requestId, Exception exception)
    {
        var request = GetRequestById(requestId);
        if (request != null)
        {
            request.SetResponseException(exception);
            return true;
        }

        return false;
    }

    public bool SetResponseCancel(string requestId)
    {
        var request = GetRequestById(requestId);
        if (request != null)
        {
            request.SetResponseCancel();
            return true;
        }

        return false;
    }

    public bool SendHeartBeat(string requestId)
    {
        var request = GetRequestById(requestId);
        if (request != null)
        {
            request.HeartBeat();
            return true;
        }

        return false;
    }
}

public class DialogSessionManagerOptions
{
    public int ExpiresAfterFinishedMsec { get; }
    public int GcIntervals { get; }
    public string SessionIdPrefix { get; }

    public DialogSessionManagerOptions(int sessionExpiresAfterFinishedMsecs = Consts.Timeouts.DefaultDialogSessionExpiresAfterFinishedMsecs, int gcIntervals = Consts.Timeouts.DefaultDialogSessionGcIntervals,
        string sessionIdPrefix = Consts.Strings.DefaultSessionIdPrefix)
    {
        if (sessionIdPrefix._IsEmpty()) sessionIdPrefix = Consts.Strings.DefaultSessionIdPrefix;
        ExpiresAfterFinishedMsec = sessionExpiresAfterFinishedMsecs;
        if (gcIntervals < 0) gcIntervals = Consts.Timeouts.DefaultDialogSessionGcIntervals;
        this.GcIntervals = gcIntervals;
        this.SessionIdPrefix = sessionIdPrefix;
    }
}

public class DialogSessionManager : AsyncService
{
    public DialogSessionManagerOptions Options { get; }

    ImmutableDictionary<string, DialogSession> SessionList = ImmutableDictionary<string, DialogSession>.Empty.WithComparers(StrComparer.IgnoreCaseComparer);

    public DialogSessionManager(DialogSessionManagerOptions? options = null, CancellationToken cancel = default) : base(cancel)
    {
        try
        {
            options ??= new DialogSessionManagerOptions();

            this.Options = options;
        }
        catch (Exception ex)
        {
            this._DisposeSafe(ex);
            throw;
        }
    }

    public DialogSession StartNewSession(DialogSessionOptions options, string quotaKey)
    {
        this.CheckNotCanceled();

        // Quota の検査
        if (quotaKey._IsFilled())
        {
            Gc(true);

            int currentSessionsForThisKey = 0;

            var snapshot = this.SessionList;
            foreach (var sess2 in snapshot.Values.ToList())
            {
                if (sess2.IsFinished == false)
                {
                    if (sess2.QuotaKey._IsSamei(quotaKey))
                    {
                        currentSessionsForThisKey++;
                    }
                }
            }

            if (currentSessionsForThisKey >= options.MaxSessionsPerQuotaKey)
            {
                throw new CoresException($"Session quota exceeded for the quota key '{quotaKey}'. Too many attemps from the same client. Please wait for seconds and try again.");
            }
        }

        DialogSession sess = new DialogSession(EnsureSpecial.Yes, this, options, quotaKey);

        if (ImmutableInterlocked.TryAdd(ref this.SessionList, sess.SessionId, sess) == false)
        {
            sess._DisposeSafe();
            throw new CoresLibException("ImmutableInterlocked.TryAdd failed. Unknown reason!");
        }

        try
        {
            sess.StartInternal();

            return sess;
        }
        catch
        {
            sess._DisposeSafe();
            ImmutableInterlocked.TryRemove(ref this.SessionList, sess.SessionId, out _);
            throw;
        }
    }

    public DialogSession? GetSessionById(string sessionId)
    {
        Gc();
        return this.SessionList.GetValueOrDefault(sessionId);
    }

    public bool SetResponseData(string sessionId, string requestId, IDialogResponseData response)
    {
        var session = GetSessionById(sessionId);
        if (session != null)
        {
            return session.SetResponseData(requestId, response);
        }
        else
        {
            return false;
        }
    }

    public bool SetResponseException(string sessionId, string requestId, Exception exception)
    {
        var session = GetSessionById(sessionId);
        if (session != null)
        {
            return session.SetResponseException(requestId, exception);
        }
        else
        {
            return false;
        }
    }

    public bool SetResponseCancel(string sessionId, string requestId)
    {
        var session = GetSessionById(sessionId);
        if (session != null)
        {
            return session.SetResponseCancel(requestId);
        }
        else
        {
            return false;
        }
    }

    public bool SendHeartBeat(string sessionId, string requestId)
    {
        var session = GetSessionById(sessionId);
        if (session != null)
        {
            return session.SendHeartBeat(requestId);
        }
        else
        {
            return false;
        }
    }

    public bool CheckSessionHealth(string sessionId)
    {
        var session = GetSessionById(sessionId);
        if (session != null)
        {
            return !session.IsFinished;
        }
        else
        {
            return false;
        }
    }

    public async Task<DialogRequest?> GetNextRequestAsync(string sessionId, int timeout = Timeout.Infinite, CancellationToken cancel = default)
    {
        await using (base.CreatePerTaskCancellationToken(out CancellationToken cancel2, cancel))
        {
            var session = GetSessionById(sessionId);
            if (session != null)
            {
                return await session.GetNextRequestAsync(timeout, cancel2);
            }
            else
            {
                throw new CoresException($"Session ID '{sessionId}' not found.");
            }
        }
    }

    long lastGcTick = 0;
    void Gc(bool force = false)
    {
        long now = TickNow;

        if (lastGcTick == 0 || now >= (lastGcTick + Options.GcIntervals) || force)
        {
            lastGcTick = now;

            DeleteOldSessions();
        }
    }

    void DeleteOldSessions()
    {
        long now = TickNow;

        foreach (var session in this.SessionList.Values.ToList())
        {
            if (session.Expires != 0 && now >= session.Expires)
            {
                session._DisposeSafe();
                ImmutableInterlocked.TryRemove(ref this.SessionList, session.SessionId, out _);
            }
        }
    }

    protected override async Task CleanupImplAsync(Exception? ex)
    {
        try
        {
            foreach (var session in this.SessionList.Values.ToList())
            {
                await session._DisposeSafeAsync();
                ImmutableInterlocked.TryRemove(ref this.SessionList, session.SessionId, out _);
            }
        }
        finally
        {
            await base.CleanupImplAsync(ex);
        }
    }
}



public class AsyncLoopManagerSettings
{
    public Func<AsyncLoopManager, CancellationToken, Task> ProcAsync { get; }
    public AsyncPulse Pulse { get; }
    public AsyncPulseWaiter PulseWaiter { get; }
    public bool RunAfterInitialize { get; }
    public int InitialLoopIntervalMsecs { get; }

    public AsyncLoopManagerSettings(Func<AsyncLoopManager, CancellationToken, Task> proc, AsyncPulse? pulse = null, bool runAfterInitialize = true, int initialLoopIntervalMsecs = -1)
    {
        if (initialLoopIntervalMsecs <= 0) initialLoopIntervalMsecs = -1;

        this.ProcAsync = proc;
        this.Pulse = pulse ?? new AsyncPulse();
        this.PulseWaiter = this.Pulse.GetPulseWaiter();
        this.RunAfterInitialize = runAfterInitialize;
        this.InitialLoopIntervalMsecs = initialLoopIntervalMsecs;
    }
}

public class AsyncLoopManager : AsyncServiceWithMainLoop
{
    public AsyncLoopManagerSettings Settings { get; }

    public LocalTimer Timer { get; }

    private int LoopIntervalMsecs_Internal;

    public bool IsInLoop { get; private set; } = false;

    public int LoopIntervalMsecs
    {
        get => LoopIntervalMsecs_Internal;
        set
        {
            if (value <= 0) value = -1;

            if (this.LoopIntervalMsecs_Internal != value)
            {
                this.LoopIntervalMsecs_Internal = value;

                if (this.IsInLoop == false)
                {
                    this.Fire();
                }
            }
        }
    }

    public AsyncLoopManager(AsyncLoopManagerSettings settings)
    {
        try
        {
            this.Settings = settings;
            this.Timer = new LocalTimer();

            if (this.Settings.RunAfterInitialize)
            {
                this.Timer.AddTimeout(0);
            }

            this.LoopIntervalMsecs_Internal = this.Settings.InitialLoopIntervalMsecs;

            this.StartMainLoop(MainLoopAsync);
        }
        catch
        {
            this._DisposeSafe();
            throw;
        }
    }

    public void Fire()
    {
        this.Settings.Pulse.FirePulse(true);
    }

    async Task MainLoopAsync(CancellationToken cancel)
    {
        await Task.Yield();

        while (cancel.IsCancellationRequested == false)
        {
            await Task.Yield();

            int waitTick = this.Timer.GetNextInterval();

            int tmp = this.LoopIntervalMsecs_Internal;

            if (tmp > 0)
            {
                if (waitTick < 0 || waitTick > tmp)
                {
                    waitTick = tmp;
                }
            }

            await this.Settings.PulseWaiter.WaitAsync(waitTick, cancel);

            await Task.Yield();

            if (cancel.IsCancellationRequested) break;

            try
            {
                IsInLoop = true;
                await this.Settings.ProcAsync(this, cancel);
            }
            catch (Exception ex)
            {
                ex._Error();
            }
            finally
            {
                IsInLoop = false;
            }
        }
    }

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


