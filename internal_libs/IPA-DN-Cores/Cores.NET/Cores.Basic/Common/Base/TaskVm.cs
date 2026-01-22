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
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Diagnostics.CodeAnalysis;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

namespace IPA.Cores.Basic;

public class TaskVmAbortException : Exception
{
    public TaskVmAbortException(string message) : base(message) { }
}

public static class AbortedTaskExecuteThreadPrivate
{
    static object LockObj = new object();
    static Dictionary<object, Queue<(SendOrPostCallback callback, object? args)>> DispatchQueueList = new Dictionary<object, Queue<(SendOrPostCallback, object?)>>();
    static object dummy_orphants = new object();
    static AutoResetEvent ev = new AutoResetEvent(true);

    static AbortedTaskExecuteThreadPrivate()
    {
        Thread t = new Thread(ThreadProc);
        t.IsBackground = true;
        t.Start();
    }

    static void ThreadProc(object? param)
    {
        SynchronizationContext.SetSynchronizationContext(new AbortedTaskExecuteThreadSynchronizationContext());

        while (true)
        {
            var actions = new List<(SendOrPostCallback callback, object? args)>();

            lock (LockObj)
            {
                foreach (object ctx in DispatchQueueList.Keys)
                {
                    var queue = DispatchQueueList[ctx];

                    while (queue.Count >= 1)
                    {
                        actions.Add(queue.Dequeue());
                    }
                }
            }

            foreach (var action in actions)
            {
                try
                {
                    //Dbg.WriteCurrentThreadId("aborted_call");
                    action.callback(action.args);
                }
                catch (Exception ex)
                {
                    ex.ToString()._Debug();
                }
            }

            ev.WaitOne();
        }
    }

    public static void PostAction(object ctx, SendOrPostCallback callback, object? arg)
    {
        lock (LockObj)
        {
            if (DispatchQueueList.ContainsKey(ctx) == false)
            {
                DispatchQueueList.Add(ctx, new Queue<(SendOrPostCallback, object?)>());
            }

            DispatchQueueList[ctx].Enqueue((callback, arg));
        }

        ev.Set();
    }

    public static void RemoveContext(object ctx)
    {
        lock (LockObj)
        {
            if (DispatchQueueList.ContainsKey(ctx))
            {
                var queue = DispatchQueueList[ctx];

                while (queue.Count >= 1)
                {
                    var q = queue.Dequeue();
                    PostAction(dummy_orphants, q.callback, q.args);
                }

                DispatchQueueList.Remove(ctx);
            }
        }

        ev.Set();
    }

    class AbortedTaskExecuteThreadSynchronizationContext : SynchronizationContext
    {
        volatile int num_operations = 0;
        volatile int num_operations_total = 0;

        public bool IsAllOperationsCompleted => (num_operations_total >= 1 && num_operations == 0);

        public override void Post(SendOrPostCallback d, object? state)
        {
            //Dbg.WriteCurrentThreadId("aborted_call_post");
            AbortedTaskExecuteThreadPrivate.PostAction(AbortedTaskExecuteThreadPrivate.dummy_orphants, d, state);
        }
    }
}

public class TaskVm<TResult, TIn>
{
    public ThreadObj ThreadObj { get; }

    Func<TIn?, Task<TResult>> RootFunction;
    Task? RootTask;

    Queue<(SendOrPostCallback callback, object? args)> DispatchQueue = new Queue<(SendOrPostCallback callback, object? args)>();
    AutoResetEvent DispatchQueueEvent = new AutoResetEvent(false);

    public TIn? InputParameter { get; }

    CancellationToken GracefulCancel { get; }
    CancellationToken AbortCancel { get; }

    object ResultLock = new object();
    public Exception? Error { get; private set; } = null;

    public TResult Result => this.GetResult(out _)!;

    [AllowNull]
    TResult result = default(TResult)!;
    public bool IsCompleted { get; private set; } = false;
    public bool IsAborted { get; private set; } = false;
    public bool HasError => this.Error != null;
    public bool Ok => !HasError;

    public ManualResetEventSlim CompletedEvent { get; } = new ManualResetEventSlim();

    bool abort_flag = false;
    bool no_more_enqueue = false;

    TaskVmSynchronizationContext? syncCtx;

    public TaskVm(Func<TIn?, Task<TResult>> rootAction, TIn? input_parameter = default(TIn), CancellationToken gracefulCancel = default(CancellationToken), CancellationToken abortCancel = default(CancellationToken))
    {
        this.InputParameter = input_parameter;
        this.RootFunction = rootAction;
        this.GracefulCancel = gracefulCancel;
        this.AbortCancel = abortCancel;

        this.AbortCancel.Register(() =>
        {
            Abort(true);
        });

        this.ThreadObj = new ThreadObj(this.ThreadProc);
        this.ThreadObj.WaitForInit();
    }

    public static Task<TResult> NewTaskVm(Func<TIn?, Task<TResult>> rootAction, TIn? inputParameter = default, CancellationToken gracefulCancel = default(CancellationToken), CancellationToken abortCancel = default(CancellationToken))
    {
        TaskVm<TResult, TIn> vm = new TaskVm<TResult, TIn>(rootAction, inputParameter, gracefulCancel, abortCancel);

        return Task<TResult>.Run(new Func<TResult>(vm.GetResultSimple));
    }

    TResult GetResultSimple()
    {
        return this.GetResult()!;
    }

    public bool Abort(bool noWait = false)
    {
        this.abort_flag = true;

        this.DispatchQueueEvent.Set();

        if (noWait)
        {
            return this.IsAborted;
        }

        this.ThreadObj.WaitForEnd();

        return this.IsAborted;
    }

    public bool Wait(bool ignoreError = false, int timeout = Timeout.Infinite, CancellationToken cancel = default(CancellationToken))
    {
        TResult? ret = GetResult(out bool timeouted, ignoreError, timeout, cancel);

        return !timeouted;
    }

    [return: MaybeNull]
    public TResult GetResult(bool ignoreError = false, int timeout = Timeout.Infinite, CancellationToken cancel = default(CancellationToken)) => GetResult(out _, ignoreError, timeout, cancel);

    [return: MaybeNull]
    public TResult GetResult(out bool timeouted, bool ignoreError = false, int timeout = Timeout.Infinite, CancellationToken cancel = default(CancellationToken))
    {
        CompletedEvent.Wait(timeout, cancel);

        if (this.IsCompleted == false)
        {
            timeouted = true;
            return default(TResult)!;
        }

        timeouted = false;

        if (this.Error != null)
        {
            if (ignoreError == false)
            {
                throw this.Error;
            }
            else
            {
                return default(TResult)!;
            }
        }

        return this.result;
    }

    void ThreadProc(object? param)
    {
        syncCtx = new TaskVmSynchronizationContext(this);
        SynchronizationContext.SetSynchronizationContext(syncCtx);

        ThreadLocalStorage.CurrentThreadData["taskvm_current_graceful_cancel"] = this.GracefulCancel;

        //Dbg.WriteCurrentThreadId("before task_proc()");

        this.RootTask = TaskProc();

        DispatcherLoop();
    }

    void SetResult(Exception? ex = null, [AllowNull] TResult result = default(TResult))
    {
        lock (this.ResultLock)
        {
            if (this.IsCompleted == false)
            {
                this.IsCompleted = true;

                if (ex == null)
                {
                    this.result = result;
                }
                else
                {
                    this.Error = ex;

                    if (ex is TaskVmAbortException)
                    {
                        this.IsAborted = true;
                    }
                }

                this.DispatchQueueEvent.Set();
                this.CompletedEvent.Set();
            }
        }
    }

    async Task TaskProc()
    {
        //Dbg.WriteCurrentThreadId("task_proc: before yield");

        await Task.Yield();

        //Dbg.WriteCurrentThreadId("task_proc: before await");

        TResult ret = default(TResult)!;

        try
        {
            ret = await this.RootFunction(this.InputParameter);

            //Dbg.WriteCurrentThreadId("task_proc: after await");
            SetResult(null, ret);
        }
        catch (Exception ex)
        {
            SetResult(ex);
        }
    }

    void DispatcherLoop()
    {
        int NumExecutedTasks = 0;

        while (this.IsCompleted == false)
        {
            this.DispatchQueueEvent.WaitOne();

            if (this.abort_flag)
            {
                SetResult(new TaskVmAbortException("aborted."));
            }

            while (true)
            {
                (SendOrPostCallback callback, object? args) queuedItem;

                lock (this.DispatchQueue)
                {
                    if (this.DispatchQueue.Count == 0)
                    {
                        break;
                    }
                    queuedItem = this.DispatchQueue.Dequeue();
                }

                if (NumExecutedTasks == 0)
                {
                    ThreadObj.NoticeInited();
                }
                NumExecutedTasks++;

                if (this.abort_flag)
                {
                    SetResult(new TaskVmAbortException("aborted."));
                }

                try
                {
                    queuedItem.callback(queuedItem.args);
                }
                catch (Exception ex)
                {
                    ex.ToString()._Debug();
                }
            }
        }

        no_more_enqueue = true;

        List<(SendOrPostCallback callback, object? args)> remainingTasks = new List<(SendOrPostCallback callback, object? args)>();
        lock (this.DispatchQueue)
        {
            while (true)
            {
                if (this.DispatchQueue.Count == 0)
                {
                    break;
                }
                remainingTasks.Add(this.DispatchQueue.Dequeue());
            }
            foreach (var x in remainingTasks)
            {
                AbortedTaskExecuteThreadPrivate.PostAction(this.syncCtx!, x.callback, x.args);
            }
        }
    }

    public class TaskVmSynchronizationContext : SynchronizationContext
    {
        public readonly TaskVm<TResult, TIn> Vm;
        volatile int NumOperations = 0;
        volatile int NumOperationsTotal = 0;


        public bool IsAllOperationsCompleted => (NumOperationsTotal >= 1 && NumOperations == 0);

        public TaskVmSynchronizationContext(TaskVm<TResult, TIn> vm)
        {
            this.Vm = vm;
        }

        public override SynchronizationContext CreateCopy()
        {
            //Dbg.WriteCurrentThreadId("CreateCopy");
            return base.CreateCopy();
        }

        public override void OperationCompleted()
        {
            base.OperationCompleted();

            Interlocked.Decrement(ref NumOperations);
            //Dbg.WriteCurrentThreadId("OperationCompleted. num_operations = " + num_operations);
            Vm.DispatchQueueEvent.Set();
        }

        public override void OperationStarted()
        {
            base.OperationStarted();

            Interlocked.Increment(ref NumOperations);
            Interlocked.Increment(ref NumOperationsTotal);
            //Dbg.WriteCurrentThreadId("OperationStarted. num_operations = " + num_operations);
            Vm.DispatchQueueEvent.Set();
        }

        public override void Post(SendOrPostCallback d, object? state)
        {
            //Dbg.WriteCurrentThreadId("Post: " + this.Vm.halt);
            //base.Post(d, state);
            //d(state);

            bool ok = false;

            lock (Vm.DispatchQueue)
            {
                if (Vm.no_more_enqueue == false)
                {
                    Vm.DispatchQueue.Enqueue((d, state));
                    ok = true;
                }
            }

            if (ok)
            {
                Vm.DispatchQueueEvent.Set();
            }
            else
            {
                AbortedTaskExecuteThreadPrivate.PostAction(this, d, state);
            }
        }

        public override void Send(SendOrPostCallback d, object? state)
        {
            //Dbg.WriteCurrentThreadId("Send");
            base.Send(d, state);
        }

        public override int Wait(IntPtr[] waitHandles, bool waitAll, int millisecondsTimeout)
        {
            //Dbg.WriteCurrentThreadId("Wait");
            return base.Wait(waitHandles, waitAll, millisecondsTimeout);
        }
    }
}

