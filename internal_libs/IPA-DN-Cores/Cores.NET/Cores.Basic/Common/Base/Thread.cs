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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;

using IPA.Cores.Basic;
using IPA.Cores.Basic.Legacy;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using System.Diagnostics;

#pragma warning disable 0618

namespace IPA.Cores.Basic
{
    public static partial class CoresConfig
    {
        public static partial class BasicConfig
        {
            public static readonly Copenhagen<int> MaxPossibleConcurrentProcessCounts = 1024;
        }
    }

    public class BatchQueueItem<T>
    {
        internal BatchQueueItem(T item)
        {
            this.UserItem = item;
            this.IsCompleted = false;
            this.CompletedEvent = new AsyncManualResetEvent();
        }

        public T UserItem { get; }
        public bool IsCompleted { get; internal set; }
        public AsyncManualResetEvent CompletedEvent { get; }

        public void SetCompleted()
        {
            this.IsCompleted = true;
            this.CompletedEvent.Set();
        }
    }

    public class BatchQueue<T>
    {
        readonly CriticalSection LockObj = new CriticalSection<BatchQueue<T>>();
        Queue<BatchQueueItem<T>> ItemQueue = new Queue<BatchQueueItem<T>>();
        AutoResetEvent NewEventSignal = new AutoResetEvent(false);
        public int IdleThreadRemainTimeMsecs { get; }
        public const int DefaultIdleThreadRemainTimeMsecs = 1000;
        Action<BatchQueueItem<T>[]> ProcessItemsProc;

        public BatchQueue(Action<BatchQueueItem<T>[]> process_items_proc, int idle_thread_remain_time_msecs = DefaultIdleThreadRemainTimeMsecs)
        {
            this.IdleThreadRemainTimeMsecs = idle_thread_remain_time_msecs;
            this.ProcessItemsProc = process_items_proc;
        }

        void DoProcessList(List<BatchQueueItem<T>> current)
        {
            try
            {
                this.ProcessItemsProc(current.ToArray());
            }
            catch (Exception ex)
            {
                Dbg.WriteLine(ex.ToString());
            }

            foreach (var q in current)
            {
                q.SetCompleted();
            }
        }

        int threadMode = 0;

        static Benchmark th = new Benchmark("num_thread_created", disabled: true);
        void BackgroundThreadProc(object? param)
        {
            Thread.CurrentThread.IsBackground = true;
            long lastQueueProcTick = 0;

            //Dbg.WhereThread($"BatchQueue<{typeof(T).Name}>: Start background thread.");
            th.IncrementMe++;

            while (true)
            {
                List<BatchQueueItem<T>> current = new List<BatchQueueItem<T>>();

                lock (LockObj)
                {
                    while (this.ItemQueue.Count >= 1)
                    {
                        BatchQueueItem<T> item = this.ItemQueue.Dequeue();
                        current.Add(item);
                    }
                }

                if (current.Count >= 1)
                {
                    DoProcessList(current);

                    lastQueueProcTick = Time.Tick64;
                }

                if (this.ItemQueue.Count >= 1)
                {
                    continue;
                }

                long now = Time.Tick64;
                long remainTime = lastQueueProcTick + (long)this.IdleThreadRemainTimeMsecs - now;
                if (remainTime >= 1)
                {
                    NewEventSignal.WaitOne((int)remainTime);
                }
                else
                {
                    lock (LockObj)
                    {
                        if (this.ItemQueue.Count >= 1)
                        {
                            continue;
                        }

                        threadMode = 0;

                        //Dbg.WhereThread($"BatchQueue<{typeof(T).Name}>: Stop background thread.");

                        return;
                    }
                }
            }
        }

        public BatchQueueItem<T> Add(T item)
        {
            BatchQueueItem<T> q = new BatchQueueItem<T>(item);

            lock (LockObj)
            {
                this.ItemQueue.Enqueue(q);

                if (threadMode == 0)
                {
                    threadMode = 1;

                    ThreadObj t = new ThreadObj(BackgroundThreadProc);
                }

            }

            NewEventSignal.Set();

            return q;
        }
    }

    public class GlobalLockHandle : IDisposable
    {
        public GlobalLock GlobalLock { get; }
        Mutant? mutant;

        internal GlobalLockHandle(GlobalLock g)
        {
            this.GlobalLock = g;

            mutant = new Mutant(this.GlobalLock.Name, false, false);
            mutant.Lock();
        }

        void unlock_main()
        {
            if (mutant != null)
            {
                mutant.Unlock();
                mutant._DisposeSafe();
                mutant = null;
            }
        }

        public void Unlock()
        {
            unlock_main();
        }

        public void Dispose() { this.Dispose(true); GC.SuppressFinalize(this); }

        Once DisposeFlag;
        protected virtual void Dispose(bool disposing)
        {
            if (!disposing || DisposeFlag.IsFirstCall() == false) return;
            unlock_main();
        }
    }

    public class GlobalLock
    {
        public string Name { get; }

        public GlobalLock(string name)
        {
            this.Name = name;
        }

        public GlobalLockHandle Lock()
        {
            GlobalLockHandle h = new GlobalLockHandle(this);

            return h;
        }
    }

    public class SingleInstance : IDisposable
    {
        readonly string NameOfMutant;
        MutantBase? Mutant;

        static readonly CriticalSection LockObj = new CriticalSection<SingleInstance>();

        static readonly HashSet<string> ProcessWideSingleInstanceHashSet = new HashSet<string>();

        public static bool IsExistsAndLocked(string name, bool ignoreCase = true)
        {
            SingleInstance? si = TryGet(name, ignoreCase);

            if (si == null) return true;

            si._DisposeSafe();

            return false;
        }

        public static SingleInstance? TryGet(string name, bool ignoreCase = true)
        {
            try
            {
                return new SingleInstance(name, ignoreCase);
            }
            catch
            {
                return null;
            }
        }

        public SingleInstance(string name, bool ignoreCase = true)
        {
            NameOfMutant = $"SingleInstance_" + name._NonNullTrim();

            if (ignoreCase)
                NameOfMutant = NameOfMutant.ToLowerInvariant();

            lock (LockObj)
            {
                if (ProcessWideSingleInstanceHashSet.Contains(NameOfMutant))
                    throw new ApplicationException($"The single instance is already existing with this process.");

                MutantBase mb = MutantBase.Create(NameOfMutant, true);
                try
                {
                    mb.Lock(true);

                    Mutant = mb;
                }
                catch
                {
                    mb._DisposeSafe();
                    throw;
                }

                ProcessWideSingleInstanceHashSet.Add(NameOfMutant);
            }
        }

        public void Dispose() { this.Dispose(true); GC.SuppressFinalize(this); }
        Once DisposeFlag;
        protected virtual void Dispose(bool disposing)
        {
            if (!disposing || DisposeFlag.IsFirstCall() == false) return;

            try
            {
                if (this.Mutant != null)
                {
                    this.Mutant.Unlock();
                    this.Mutant._DisposeSafe();
                    this.Mutant = null;
                }
            }
            finally
            {
                ProcessWideSingleInstanceHashSet.Remove(NameOfMutant);
            }
        }
    }

    public class Mutant : IDisposable
    {
        public string Name { get; }
        public bool NonBlock { get; }

        readonly IHolder Leak;

        readonly SingleThreadWorker Worker;

        MutantBase? MutantBase;

        readonly AsyncLock LockObj = new AsyncLock();

        volatile bool _IsLocked = false;
        public bool IsLocked => _IsLocked;

        public Mutant(string name, bool nonBlock, bool selfThread)
        {
            this.Name = name;
            this.NonBlock = nonBlock;
            this.Leak = LeakChecker.Enter(LeakCounterKind.Mutant);

            try
            {

                Worker = new SingleThreadWorker($"Mutant - '{this.Name}'", selfThread);

                Worker.ExecAsync(p =>
                {
                    MutantBase = MutantBase.Create(name);
                }, 0)._GetResult();
            }
            catch
            {
                this._DisposeSafe();
                throw;
            }
        }

        public async Task LockAsync()
        {
            using (await LockObj.LockWithAwait())
            {
                if (_IsLocked)
                    throw new ApplicationException($"The mutex \"{Name}\" is already locked.");

                await Worker.ExecAsync(p =>
                {
                    MutantBase!.Lock(this.NonBlock);
                }, 0);

                _IsLocked = true;
            }
        }
        public void Lock() => LockAsync()._GetResult();

        public async Task UnlockAsync()
        {
            using (await LockObj.LockWithAwait())
            {
                await Worker.ExecAsync(p =>
                {
                    MutantBase!.Unlock();
                }, 0);

                _IsLocked = false;
            }
        }
        public void Unlock() => this.UnlockAsync()._GetResult();

        public void Dispose() { this.Dispose(true); GC.SuppressFinalize(this); }
        Once DisposeFlag;
        protected virtual void Dispose(bool disposing)
        {
            if (!disposing || DisposeFlag.IsFirstCall() == false) return;

            if (this.Worker != null)
            {
                Worker.ExecAsync(p =>
                {
                    MutantBase._DisposeSafe();
                }, 0);

                Worker._DisposeSafe();
            }

            this.Leak._DisposeSafe();
        }
    }

    public class MutantUnixImpl : MutantBase
    {
        const string Extension = ".lock";

        string Filename;
        int LockedCount = 0;
        IntPtr FileHandle;

        public MutantUnixImpl(string name)
        {
            Filename = Path.Combine(Env.UnixMutantDir, MutantBase.GenerateInternalName(name) + Extension);
            IO.MakeDirIfNotExists(Env.UnixMutantDir);
        }

        public override void Lock(bool nonBlock = false)
        {
            if (LockedCount == 0)
            {
                IO.MakeDirIfNotExists(Env.UnixMutantDir);

                UnixPermissions perm = UnixPermissions.S_IRUSR | UnixPermissions.S_IWUSR | UnixPermissions.S_IRGRP | UnixPermissions.S_IWGRP | UnixPermissions.S_IROTH | UnixPermissions.S_IWOTH;

                IntPtr fd = UnixApi.Open(Filename, UnixApi.OpenFlags.O_CREAT | UnixApi.OpenFlags.O_CLOEXEC, (int)perm);
                if (fd.ToInt64() < 0)
                {
                    throw new IOException("Open failed.");
                }

                try
                {
                    if (UnixApi.FLock(fd, UnixApi.LockOperations.LOCK_EX | (nonBlock ? UnixApi.LockOperations.LOCK_NB : 0)) == -1)
                    {
                        throw new IOException("FLock failed.");
                    }
                }
                catch
                {
                    UnixApi.Close(fd);

                    throw;
                }

                this.FileHandle = fd;

                LockedCount++;
            }
        }

        public override void Unlock()
        {
            if (LockedCount <= 0) throw new ApplicationException("locked_count <= 0");
            if (LockedCount == 1)
            {
                try
                {
                    UnixApi.FLock(this.FileHandle, UnixApi.LockOperations.LOCK_UN);
                }
                catch { }

                UnixApi.Close(this.FileHandle);

                this.FileHandle = IntPtr.Zero;
            }
            LockedCount--;
        }

        public static void DeleteUnusedMutantFiles()
        {
            if (Env.IsUnix == false) return;

            try
            {
                string[] fileFullPathList = Directory.GetFiles(Env.UnixMutantDir);

                foreach (string fileFullPath in fileFullPathList)
                {
                    try
                    {
                        if (fileFullPath.EndsWith(Extension, StringComparison.OrdinalIgnoreCase))
                        {
                            UnixPermissions perm = UnixPermissions.S_IRUSR | UnixPermissions.S_IWUSR | UnixPermissions.S_IRGRP | UnixPermissions.S_IWGRP | UnixPermissions.S_IROTH | UnixPermissions.S_IWOTH;

                            bool okToDelete = false;

                            IntPtr fd = UnixApi.Open(fileFullPath, UnixApi.OpenFlags.O_CREAT | UnixApi.OpenFlags.O_CLOEXEC, (int)perm);

                            if (fd.ToInt64() >= 0)
                            {
                                try
                                {
                                    if (UnixApi.FLock(fd, UnixApi.LockOperations.LOCK_EX | UnixApi.LockOperations.LOCK_NB) != -1)
                                    {
                                        okToDelete = true;
                                    }
                                }
                                finally
                                {
                                    try
                                    {
                                        UnixApi.FLock(fd, UnixApi.LockOperations.LOCK_UN);
                                    }
                                    catch { }

                                    try
                                    {
                                        UnixApi.Close(fd);
                                    }
                                    catch { }
                                }
                            }

                            if (okToDelete)
                            {
                                try
                                {
                                    File.Delete(fileFullPath);
                                }
                                catch { }
                            }
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }
    }

    public class MutantWin32ForSingleInstanceImpl : MutantBase
    {
        readonly string InternalName;

        int LockedCount = 0;

        Mutex? CurrentMutexObj = null;

        readonly static ConcurrentHashSet<object> MutexList = new ConcurrentHashSet<object>();

        public MutantWin32ForSingleInstanceImpl(string name)
        {
            InternalName = @"Global\si_" + MutantBase.GenerateInternalName(name);
        }

        public override void Lock(bool nonBlock = false)
        {
            if (nonBlock == false) throw new ArgumentException("nonBlock must be true.");

            if (LockedCount == 0)
            {
                Mutex mutex = new Mutex(false, InternalName, out bool createdNew);

                if (createdNew == false)
                {
                    mutex._DisposeSafe();
                    throw new ApplicationException($"Cannot create the new mutex object.");
                }

                MutexList.Add(mutex);

                CurrentMutexObj = mutex;
            }

            LockedCount++;
        }

        public override void Unlock()
        {
            if (LockedCount <= 0) throw new ApplicationException("locked_count <= 0");
            if (LockedCount == 1)
            {
                MutexList.Remove(CurrentMutexObj!);

                CurrentMutexObj._DisposeSafe();
                CurrentMutexObj = null;
            }
            LockedCount--;
        }
    }

    public class MutantWin32Impl : MutantBase
    {
        Mutex? MutexObj;
        int LockedCount = 0;
        readonly string InternalName;

        public MutantWin32Impl(string name)
        {
            InternalName = @"Global\" + MutantBase.GenerateInternalName(name);
            MutexObj = new Mutex(false, InternalName, out _);
        }

        public override void Lock(bool nonBlock = false)
        {
            if (LockedCount == 0)
            {
                int numRetry = 0;
                LABEL_RETRY:

                try
                {
                    if (MutexObj!.WaitOne(nonBlock ? 0 : Timeout.Infinite) == false)
                    {
                        throw new ApplicationException("Cannot obtain the mutex object.");
                    }
                }
                catch (AbandonedMutexException)
                {
                    MutexObj._DisposeSafe();
                    MutexObj = null;

                    if (numRetry >= 100) throw;

                    MutexObj = new Mutex(false, InternalName, out _);
                    numRetry++;
                    goto LABEL_RETRY;
                }
            }
            LockedCount++;
        }

        public override void Unlock()
        {
            if (LockedCount <= 0) throw new ApplicationException("locked_count <= 0");
            if (LockedCount == 1)
            {
                try
                {
                    MutexObj!.ReleaseMutex();
                }
                catch (Exception ex)
                {
                    ex._Debug();
                }
            }
            LockedCount--;
        }

        Once DisposeFlag;
        protected override void Dispose(bool disposing)
        {
            try
            {
                if (!disposing || DisposeFlag.IsFirstCall() == false) return;
                MutexObj._DisposeSafe();
            }
            finally { base.Dispose(disposing); }
        }
    }

    public abstract class MutantBase : IDisposable
    {
        readonly static bool IsWindows = Environment.OSVersion.Platform == PlatformID.Win32NT;

        public static string GenerateInternalName(string name)
        {
            name = name.Trim().ToLowerInvariant();
            return "Cores_DotNet_Mutex_" + Str.ByteToStr(Str.HashStr(name)).ToLowerInvariant();
        }

        public abstract void Lock(bool nonBlock = false);
        public abstract void Unlock();

        public static MutantBase Create(string name, bool forSingleInstance = false)
        {
            if (IsWindows)
            {
                if (forSingleInstance == false)
                    return new MutantWin32Impl(name);
                else
                    return new MutantWin32ForSingleInstanceImpl(name);
            }
            else
            {
                return new MutantUnixImpl(name);
            }
        }

        public void Dispose() { this.Dispose(true); GC.SuppressFinalize(this); }
        Once DisposeFlag;
        protected virtual void Dispose(bool disposing)
        {
            if (!disposing || DisposeFlag.IsFirstCall() == false) return;
            // Here
        }
    }

    public class SemaphoneArrayItem
    {
        public Semaphore Semaphone;
        public int MaxCount;
        public int CurrentWaitThreads;

        public SemaphoneArrayItem(int max)
        {
            this.Semaphone = new Semaphore(max - 1, max);
            this.MaxCount = max;
        }
    }

    namespace Legacy
    {
        public class SemaphoneArray
        {
            Dictionary<string, SemaphoneArrayItem> array = new Dictionary<string, SemaphoneArrayItem>();

            string NormalizeName(string name)
            {
                Str.NormalizeString(ref name);
                name = name.ToUpperInvariant();
                return name;
            }

            public int GetNumSemaphone
            {
                get
                {
                    lock (array)
                    {
                        return array.Count;
                    }
                }
            }

            public void Release(string name)
            {
                name = NormalizeName(name);

                SemaphoneArrayItem sem;

                lock (array)
                {
                    if (array.ContainsKey(name) == false)
                    {
                        throw new ApplicationException("invalid semaphone name: " + name);
                    }

                    sem = array[name];

                    int c = sem.Semaphone.Release();

                    if ((c + 1) == sem.MaxCount)
                    {
                        if (sem.CurrentWaitThreads == 0)
                        {
                            //sem.Semaphone.Dispose();

                            array.Remove(name);
                        }
                    }
                }

            }

            public bool Wait(string name, int max_count, int timeout)
            {
                if (max_count <= 0 || (timeout < 0 && timeout != -1))
                {
                    throw new ApplicationException("Invalid args.");
                }

                name = NormalizeName(name);

                SemaphoneArrayItem? sem = null;

                lock (array)
                {
                    if (array.ContainsKey(name) == false)
                    {
                        sem = new SemaphoneArrayItem(max_count);

                        array.Add(name, sem);

                        return true;
                    }
                    else
                    {
                        sem = array[name];

                        if (sem.MaxCount != max_count)
                        {
                            throw new ApplicationException("max_count != current database value.");
                        }

                        if (sem.Semaphone.WaitOne(0))
                        {
                            return true;
                        }

                        Interlocked.Increment(ref sem.CurrentWaitThreads);
                    }
                }

                bool ret = sem.Semaphone.WaitOne(timeout);

                Interlocked.Decrement(ref sem.CurrentWaitThreads);

                return ret;
            }
        }

        public static class BackgroundWorker
        {
            static volatile int NumBusyWorkerThreads = 0;
            static volatile int NumWorkerThreads = 0;

            static Queue<Tuple<Action<object>, object>> ActionQueue = new Queue<Tuple<Action<object>, object>>();

            static AutoResetEvent signal = new AutoResetEvent(false);

            static void WorkerThreadProc()
            {
                while (true)
                {
                    Interlocked.Increment(ref NumBusyWorkerThreads);
                    while (true)
                    {
                        Tuple<Action<object>, object>? work = null;
                        lock (ActionQueue)
                        {
                            if (ActionQueue.Count != 0)
                            {
                                work = ActionQueue.Dequeue();
                            }
                        }

                        if (work != null)
                        {
                            try
                            {
                                work.Item1(work.Item2);
                            }
                            catch (Exception ex)
                            {
                                Dbg.WriteLine(ex.ToString());
                            }
                        }
                        else
                        {
                            break;
                        }
                    }
                    Interlocked.Decrement(ref NumBusyWorkerThreads);

                    signal.WaitOne();
                }
            }

            public static void Run(Action<object> action, object arg)
            {
                if (NumBusyWorkerThreads == NumWorkerThreads)
                {
                    Interlocked.Increment(ref NumWorkerThreads);
                    Thread t = new Thread(WorkerThreadProc);
                    t.IsBackground = true;
                    t.Start();
                }

                lock (ActionQueue)
                {
                    ActionQueue.Enqueue(new Tuple<Action<object>, object>(action, arg));
                }

                signal.Set();
            }

        }

        public class WorkerQueuePrivate
        {
            readonly CriticalSection LockObj = new CriticalSection<WorkerQueuePrivate>();

            List<ThreadObj> ThreadList;
            ThreadProc ThreadProc;
            int NumWorkerThreads;
            Queue<object> TaskQueue = new Queue<object>();
            Exception? RaisedException = null;

            void WorkerThread(object? param)
            {
                while (true)
                {
                    object? task = null;

                    // キューから 1 個取得する
                    lock (LockObj)
                    {
                        if (TaskQueue.Count == 0)
                        {
                            return;
                        }
                        task = TaskQueue.Dequeue();
                    }

                    // タスクを処理する
                    try
                    {
                        this.ThreadProc(task);
                    }
                    catch (Exception ex)
                    {
                        if (RaisedException == null)
                        {
                            RaisedException = ex;
                        }

                        Dbg.WriteLine(ex.Message);
                    }
                }
            }

            public WorkerQueuePrivate(ThreadProc threadProc, int numWorkerThreads, object[] tasks)
            {
                ThreadList = new List<ThreadObj>();
                int i;

                this.ThreadProc = threadProc;
                this.NumWorkerThreads = numWorkerThreads;

                foreach (object task in tasks)
                {
                    TaskQueue.Enqueue(task);
                }

                RaisedException = null;

                for (i = 0; i < numWorkerThreads; i++)
                {
                    ThreadObj t = new ThreadObj(WorkerThread);

                    ThreadList.Add(t);
                }

                foreach (ThreadObj t in ThreadList)
                {
                    t.WaitForEnd();
                }

                if (RaisedException != null)
                {
                    throw RaisedException;
                }
            }
        }
    }

    public static class Tick64
    {
        public static long Value => FastTick64.Now;
        public static long Now => FastTick64.Now;

        public static uint ValueUInt32
        {
            get
            {
                unchecked
                {
                    return (uint)((ulong)Value);
                }
            }
        }
    }

    public class Event
    {
        EventWaitHandle EventObj = null!;
        public const int Infinite = Timeout.Infinite;

        public Event(bool manualReset = false)
        {
            EventObj = new EventWaitHandle(false, (manualReset ? EventResetMode.ManualReset : EventResetMode.AutoReset));
        }

        public void Set()
        {
            EventObj.Set();
        }

        public bool Wait()
        {
            return Wait(Infinite);
        }
        public bool Wait(int millisecs)
        {
            return EventObj.WaitOne(millisecs, false);
        }

        public bool WaitWithPoll(int waitMillisecs, int pollInterval, Func<bool> proc)
        {
            long end_tick = Time.Tick64 + (long)waitMillisecs;

            if (waitMillisecs == Infinite)
            {
                end_tick = long.MaxValue;
            }

            while (true)
            {
                long now = Time.Tick64;
                if (waitMillisecs != Infinite)
                {
                    if (now >= end_tick)
                    {
                        return false;
                    }
                }

                long next_wait = (end_tick - now);
                next_wait = Math.Min(next_wait, (long)pollInterval);
                next_wait = Math.Max(next_wait, 1);

                if (proc != null)
                {
                    if (proc())
                    {
                        return true;
                    }
                }

                if (Wait((int)next_wait))
                {
                    return true;
                }
            }
        }

        static EventWaitHandle[] ToArray(Event[] events)
        {
            List<EventWaitHandle> list = new List<EventWaitHandle>();

            foreach (Event e in events)
            {
                list.Add(e.EventObj);
            }

            return list.ToArray();
        }

        public static bool WaitAll(Event[] events)
        {
            return WaitAll(events, Infinite);
        }
        public static bool WaitAll(Event[] events, int millisecs)
        {
            if (events.Length <= 64)
            {
                return WaitAllInner(events, millisecs);
            }
            else
            {
                return WaitAllMulti(events, millisecs);
            }
        }

        static bool WaitAllMulti(Event[] events, int millisecs)
        {
            int numBlocks = (events.Length + 63) / 64;
            List<Event>[] list = new List<Event>[numBlocks];
            int i;
            for (i = 0; i < numBlocks; i++)
            {
                list[i] = new List<Event>();
            }
            for (i = 0; i < events.Length; i++)
            {
                list[i / 64].Add(events[i]);
            }

            double start = Time.NowHighResDouble;
            double giveup = start + (double)millisecs / 1000.0;
            foreach (List<Event> o in list)
            {
                double now = Time.NowHighResDouble;
                if (now <= giveup || millisecs < 0)
                {
                    int waitmsecs;
                    if (millisecs >= 0)
                    {
                        waitmsecs = (int)((giveup - now) * 1000.0);
                    }
                    else
                    {
                        waitmsecs = Timeout.Infinite;
                    }

                    bool ret = WaitAllInner(o.ToArray(), waitmsecs);
                    if (ret == false)
                    {
                        return false;
                    }
                }
                else
                {
                    return false;
                }
            }

            return true;
        }

        static bool WaitAllInner(Event[] events, int millisecs)
        {
            if (events.Length == 1)
            {
                return events[0].Wait(millisecs);
            }
            return EventWaitHandle.WaitAll(ToArray(events), millisecs, false);
        }

        public static bool WaitAny(Event[] events)
        {
            return WaitAny(events, Infinite);
        }
        public static bool WaitAny(Event[] events, int millisecs)
        {
            if (events.Length == 1)
            {
                return events[0].Wait(millisecs);
            }
            return ((WaitHandle.WaitTimeout == EventWaitHandle.WaitAny(ToArray(events), millisecs, false)) ? false : true);
        }

        public IntPtr Handle
        {
            get
            {
                return EventObj.Handle;
            }
        }
    }

    public class ThreadLocalStorage
    {
        static LocalDataStoreSlot Slot = Thread.AllocateDataSlot();

        public static ConcurrentDictionary<string, object> CurrentThreadData
        {
            get
            {
                return GetCurrentThreadData();
            }
        }

        public static ConcurrentDictionary<string, object> GetCurrentThreadData()
        {
            ConcurrentDictionary<string, object>? t;

            try
            {
                t = (ConcurrentDictionary<string, object> ?)Thread.GetData(Slot);
            }
            catch
            {
                t = null;
            }

            if (t == null)
            {
                t = new ConcurrentDictionary<string, object>();

                Thread.SetData(Slot, t);
            }

            return t;
        }
    }

    public delegate void ThreadProc(object? userObject);

    public class ThreadObj
    {
        public readonly static RefInt NumCurrentThreads = new RefInt();

        static Once Global_DebugReportNumCurrentThreads_Flag;
        public static void DebugReportNumCurrentThreads()
        {
            if (Global_DebugReportNumCurrentThreads_Flag.IsFirstCall())
            {
                Legacy.GlobalIntervalReporter.Singleton.ReportRefObject("NumThreads", NumCurrentThreads);
            }
        }

        static LocalDataStoreSlot CurrentObjSlot = Thread.AllocateDataSlot();

        public const int Infinite = Timeout.Infinite;

        public bool IsFinished { get; private set; } = false;

        ThreadProc Proc;
        EventWaitHandle WaitEnd;
        AsyncManualResetEvent WaitEndAsync;
        EventWaitHandle WaitInitForUser;
        AsyncManualResetEvent WaitInitForUserAsync;
        public Thread Thread { get; }
        object? UserObject;
        public int Index { get; }
        public string Name { get; }

        public ThreadObj(ThreadProc threadProc, object? userObject = null, int stacksize = 0, int index = 0, string? name = null, bool isBackground = false, ThreadPriority priority = ThreadPriority.Normal)
        {
            if (stacksize == 0)
            {
                stacksize = DefaultStackSize;
            }

            if (name._IsEmpty())
            {
                try
                {
                    name = threadProc.Target?.ToString() ?? "(unknown)" + "." + threadProc.Method.Name + "()";
                }
                catch
                {
                    try
                    {
                        name = threadProc.Method.DeclaringType + "." + threadProc.Method.Name + "()";
                    }
                    catch
                    {
                    }
                }
            }

            this.Name = name ?? "(unknown)";

            this.Proc = threadProc;
            this.UserObject = userObject;
            this.Index = index;
            WaitEnd = new EventWaitHandle(false, EventResetMode.ManualReset);
            WaitEndAsync = new AsyncManualResetEvent();
            WaitInitForUser = new EventWaitHandle(false, EventResetMode.ManualReset);
            WaitInitForUserAsync = new AsyncManualResetEvent();
            NumCurrentThreads.Increment();
            this.Thread = new Thread(new ParameterizedThreadStart(commonThreadProc), stacksize);
            if (this.Name._IsFilled())
            {
                this.Thread.Name = this.Name;
            }
            this.Thread.IsBackground = isBackground;
            if (priority != ThreadPriority.Normal)
            {
                this.Thread.Priority = priority;
            }
            this.Thread.Start(this);
        }

        public static ThreadObj Start(ThreadProc proc, object? param = null, bool isBackground = false)
        {
            return new ThreadObj(proc, param, isBackground: isBackground);
        }

        public static ThreadObj[] StartMany(int num, ThreadProc proc, object? param = null, bool isBackground = false)
        {
            List<ThreadObj> ret = new List<ThreadObj>();
            for (int i = 0; i < num; i++)
            {
                ThreadObj t = new ThreadObj(proc, param, 0, i, isBackground: isBackground);
                ret.Add(t);
            }
            return ret.ToArray();
        }

        public static int DefaultStackSize { get; set; } = 100000;

        void commonThreadProc(object? obj)
        {
            Thread.SetData(CurrentObjSlot, this);

            try
            {
                this.Proc(this.UserObject);
            }
            finally
            {
                IsFinished = true;
                NumCurrentThreads.Decrement();
                WaitEnd.Set();
                WaitEndAsync.Set();
            }
        }

        public static ThreadObj Current => GetCurrentThreadObj();
        public static int CurrentThreadId => Thread.CurrentThread.ManagedThreadId;

        public static ThreadObj GetCurrentThreadObj()
        {
            object? data = Thread.GetData(CurrentObjSlot);
            if (data == null) throw new ApplicationException("Not a ThreadObj() managed thread.");

            return (ThreadObj)data;
        }

        public static void NoticeInited()
        {
            ThreadObj threadObj = GetCurrentThreadObj();

            threadObj.WaitInitForUser.Set();
            threadObj.WaitInitForUserAsync.Set();
        }

        public void WaitForInit()
        {
            WaitInitForUser.WaitOne();
        }

        public Task WaitForInitAsync()
        {
            return WaitInitForUserAsync.WaitAsync();
        }

        public bool WaitForEnd(int timeout)
        {
            return WaitEnd.WaitOne(timeout, false);
        }
        public bool WaitForEnd()
        {
            return WaitEnd.WaitOne();
        }

        public Task<bool> WaitForEndAsync()
        {
            return WaitEndAsync.WaitAsync();
        }

        public static void Sleep(int millisec)
        {
            if (millisec == 0x7fffffff)
            {
                millisec = ThreadObj.Infinite;
            }

            Thread.Sleep(millisec);
        }

        public static void SleepInfinite() => Sleep(Timeout.Infinite);

        public static void Yield()
        {
            Thread.Sleep(0);
        }

        public static void ProcessWorkQueue(ThreadProc threadProc, int numWorkerThreads, object[] tasks)
        {
            WorkerQueuePrivate q = new WorkerQueuePrivate(threadProc, numWorkerThreads, tasks);
        }
    }

    namespace Legacy
    {
        public static class StillRunningThreadRegister
        {
            public static int RegularWatchInterval = 1 * 1000;

            static ConcurrentDictionary<string, Func<bool>> CallbacksList = new ConcurrentDictionary<string, Func<bool>>();

            static AutoResetEvent ev = new AutoResetEvent(true);

            static StillRunningThreadRegister()
            {
                ThreadObj t = new ThreadObj(ThreadProc);
            }

            public static string RegisterRefInt(RefInt r)
            {
                return RegisterCallback(() =>
                {
                    return (r.Value != 0);
                });
            }

            public static string RegisterRefBool(RefBool r)
            {
                return RegisterCallback(() =>
                {
                    return r.Value;
                });
            }

            public static string RegisterCallback(Func<bool> proc)
            {
                string key = Str.NewGuid();

                ManualResetEventSlim ev = new ManualResetEventSlim(false);
                Once once_flag = new Once();

                CallbacksList[key] = new Func<bool>(() =>
                {
                    if (once_flag.IsFirstCall())
                    {
                        ev.Set();
                    }
                    return proc();
                });

                NotifyStatusChanges();

                ev.Wait();

                return key;
            }

            public static void Unregister(string key)
            {
                CallbacksList.TryRemove(key, out _);
            }

            public static void NotifyStatusChanges()
            {
                ev.Set();
            }

            static void ThreadProc(object? param)
            {
                Thread.CurrentThread.Priority = ThreadPriority.Highest;

                while (true)
                {
                    var procs = CallbacksList.Values;

                    bool prevent = false;

                    foreach (var proc in procs)
                    {
                        bool ret = false;
                        try
                        {
                            ret = proc();
                        }
                        catch
                        {
                        }

                        if (ret)
                        {
                            prevent = true;
                        }
                    }

                    Thread.CurrentThread.IsBackground = !prevent;

                    ev.WaitOne(RegularWatchInterval);
                }
            }
        }
    }
}
