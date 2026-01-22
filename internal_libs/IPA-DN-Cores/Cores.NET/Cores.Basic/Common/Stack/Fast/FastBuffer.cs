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
using System.Runtime.CompilerServices;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Collections.Generic;
using System.Linq;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

namespace IPA.Cores.Basic;

public static partial class CoresConfig
{
    public static partial class FastBufferConfig
    {
        public static readonly Copenhagen<long> DefaultFastStreamBufferThreshold = 524288;
        public static readonly Copenhagen<long> DefaultFastDatagramBufferThreshold = 65536;

        // 重いサーバー (大量のインスタンスや大量のコンテナが稼働、または大量のコネクションを処理) における定数変更
        public static void ApplyHeavyLoadServerConfig()
        {
            DefaultFastStreamBufferThreshold.TrySet(65536);
            DefaultFastDatagramBufferThreshold.TrySet(65536);
        }
    }
}

public enum FastBufferCallbackEventType
{
    Init,
    Written,
    Read,
    EmptyToNonEmpty,
    NonEmptyToEmpty,
    Disconnected,
}

public interface IFastBufferState
{
    long Id { get; }

    long PinHead { get; }
    long PinTail { get; }
    long Length { get; }

    long Threshold { get; }

    ExceptionQueue ExceptionQueue { get; }
    LayerInfo Info { get; }

    CriticalSection LockObj { get; }

    bool IsReadyToWrite();
    bool IsReadyToRead(int size = 1);
    bool IsEventsEnabled { get; }

    AsyncAutoResetEvent? EventWriteReady { get; }
    AsyncAutoResetEvent? EventReadReady { get; }

    FastEventListenerList<IFastBufferState, FastBufferCallbackEventType> EventListeners { get; }

    void CompleteRead(bool checkDisconnect = false, bool softly = true);
    void CompleteWrite(bool checkDisconnect = true, bool softly = true);
}

public static class IFastBufferStateHelper
{
    static readonly int PollingTimeout = CoresConfig.PipeConfig.PollingTimeout;

    public static async Task WaitForReadyToWriteAsync(this IFastBufferState writer, CancellationToken cancel, int timeout, bool noTimeoutException = false, AsyncAutoResetEvent? cancelEvent = null)
    {
        LocalTimer timer = new LocalTimer();

        int cancelEventInitialStateVer = cancelEvent?.StateVersion ?? 0;

        timer.AddTimeout(PollingTimeout);
        long timeoutTick = timer.AddTimeout(timeout);

        while (writer.IsReadyToWrite() == false)
        {
            if (FastTick64.Now >= timeoutTick)
            {
                if (noTimeoutException == false)
                {
                    throw new TimeoutException();
                }
                else
                {
                    return;
                }
            }

            cancel.ThrowIfCancellationRequested();

            if (cancelEvent?.IsSet ?? false) return;
            if ((cancelEvent?.StateVersion ?? 0) != cancelEventInitialStateVer) return;

            await TaskUtil.WaitObjectsAsync(
                cancels: new CancellationToken[] { cancel },
                events: new AsyncAutoResetEvent?[] { writer.EventWriteReady, cancelEvent },
                timeout: timer.GetNextInterval()
                );
        }

        cancel.ThrowIfCancellationRequested();
    }

    public static async Task<bool> WaitForReadyToReadAsync(this IFastBufferState reader, CancellationToken cancel, int timeout, int sizeToRead = 1, bool noTimeoutException = false, AsyncAutoResetEvent? cancelEvent = null)
    {
        sizeToRead = Math.Max(sizeToRead, 1);

        int cancelEventInitialStateVer = cancelEvent?.StateVersion ?? 0;

        if (sizeToRead > reader.Threshold) throw new ArgumentException("WaitForReadyToReadAsync: sizeToRead > reader.Threshold");

        LocalTimer timer = new LocalTimer();

        timer.AddTimeout(PollingTimeout);
        long timeoutTick = timer.AddTimeout(timeout);

        while (reader.IsReadyToRead(sizeToRead) == false)
        {
            if (FastTick64.Now >= timeoutTick)
            {
                if (noTimeoutException == false)
                {
                    throw new TimeoutException();
                }
                else
                {
                    return false;
                }
            }

            cancel.ThrowIfCancellationRequested();

            if (cancelEvent?.IsSet ?? false) return false;
            if ((cancelEvent?.StateVersion ?? 0) != cancelEventInitialStateVer) return false;

            await TaskUtil.WaitObjectsAsync(
                cancels: new CancellationToken[] { cancel },
                events: new AsyncAutoResetEvent?[] { reader.EventReadReady, cancelEvent },
                timeout: timer.GetNextInterval()
                );
        }

        cancel.ThrowIfCancellationRequested();

        return true;
    }
}

public interface IFastBuffer<T> : IFastBufferState
{
    void Clear();
    long Enqueue(T item);
    long EnqueueAll(ReadOnlySpan<T> itemList);
    long EnqueueAllWithLock(ReadOnlySpan<T> itemList, bool completeWrite = false);
    List<T> Dequeue(long minReadSize, out long totalReadSize, bool allowSplitSegments = true);
    List<T> DequeueWithLock(long minReadSize, out long totalReadSize, bool allowSplitSegments = true);
    List<T> DequeueAll(out long totalReadSize);
    List<T> DequeueAllWithLock(out long totalReadSize);
    long DequeueAllAndEnqueueToOther(IFastBuffer<T> other);
}

public readonly struct FastBufferSegment<T>
{
    public readonly T Item;
    public readonly long Pin;
    public readonly long RelativeOffset;

    public FastBufferSegment(T item, long pin, long relativeOffset)
    {
        Item = item;
        Pin = pin;
        RelativeOffset = relativeOffset;
    }
}

internal static class FastBufferGlobalIdCounter
{
    static long Id = 0;
    public static long NewId() => Interlocked.Increment(ref Id);
}

public class FastStreamBuffer<T> : IFastBuffer<ReadOnlyMemory<T>>
{
    FastLinkedList<ReadOnlyMemory<T>> List = new FastLinkedList<ReadOnlyMemory<T>>();
    public long PinHead { get; private set; } = 0;
    public long PinTail { get; private set; } = 0;
    public long Length { get { long ret = checked(PinTail - PinHead); Debug.Assert(ret >= 0); return ret; } }
    public long Threshold { get; set; }
    public long Id { get; }

    public FastEventListenerList<IFastBufferState, FastBufferCallbackEventType> EventListeners { get; }
        = new FastEventListenerList<IFastBufferState, FastBufferCallbackEventType>();

    public bool IsReadyToWrite()
    {
        if (IsDisconnected) return true;
        if (Length <= Threshold) return true;
        CompleteWrite(false, true);
        return false;
    }

    public long SizeWantToBeWritten
    {
        get
        {
            long ret = 0;
            if (IsDisconnected == false) ret = Threshold - Length;
            return Math.Max(ret, 0);
        }
    }

    public bool IsReadyToRead(int size = 1)
    {
        size = Math.Max(size, 1);
        if (IsDisconnected) return true;
        if (Length >= 1) return true;
        CompleteRead();
        return false;
    }
    public bool IsEventsEnabled { get; }

    Once internalDisconnectedFlag;
    public bool IsDisconnected { get => internalDisconnectedFlag.IsSet; }

    public AsyncAutoResetEvent? EventWriteReady { get; } = null;
    public AsyncAutoResetEvent? EventReadReady { get; } = null;

    public static readonly long DefaultThreshold = CoresConfig.FastBufferConfig.DefaultFastStreamBufferThreshold;

    public List<Func<Task>> OnDisconnected { get; } = new List<Func<Task>>();

    public CriticalSection LockObj { get; } = new CriticalSection<FastStreamBuffer<T>>();

    public ExceptionQueue ExceptionQueue { get; } = new ExceptionQueue();
    public LayerInfo Info { get; } = new LayerInfo();

    public FastStreamBuffer(bool enableEvents = false, long? thresholdLength = null)
    {
        if (thresholdLength < 0) throw new ArgumentOutOfRangeException("thresholdLength < 0");

        Threshold = thresholdLength ?? DefaultThreshold;
        IsEventsEnabled = enableEvents;

        if (IsEventsEnabled)
        {
            EventWriteReady = new AsyncAutoResetEvent();
            EventReadReady = new AsyncAutoResetEvent();
            Id = FastBufferGlobalIdCounter.NewId();
        }

        EventListeners.Fire(this, FastBufferCallbackEventType.Init);
    }

    Once checkDisconnectFlag;

    public void CheckDisconnected()
    {
        if (IsDisconnected)
        {
            if (checkDisconnectFlag.IsFirstCall())
            {
                ExceptionQueue.Raise(new FastBufferDisconnectedException());
            }
            else
            {
                ExceptionQueue.ThrowFirstExceptionIfExists();
                throw new FastBufferDisconnectedException();
            }
        }
    }

    public void Disconnect()
    {
        if (internalDisconnectedFlag.IsFirstCall())
        {
            foreach (var ev in OnDisconnected)
            {
                try
                {
                    // ev();
                    TaskUtil.StartSyncTaskAsync(ev, true)._LaissezFaire(true);
                }
                catch { }
            }
            EventReadReady?.Set(softly: true);
            EventWriteReady?.Set(softly: true);

            EventListeners.FireSoftly(this, FastBufferCallbackEventType.Disconnected);
        }
    }

    long LastHeadPin = long.MinValue;

    public void CompleteRead(bool checkDisconnect = false, bool softly = true)
    {
        if (IsEventsEnabled)
        {
            bool setFlag = false;

            lock (LockObj)
            {
                long current = PinHead;
                if (LastHeadPin != current)
                {
                    LastHeadPin = current;
                    if (IsReadyToWrite())
                        setFlag = true;
                }
                if (IsDisconnected)
                    setFlag = true;
            }

            if (setFlag)
            {
                EventWriteReady?.Set(softly);
            }
        }

        if (checkDisconnect)
            CheckDisconnected();
    }

    long LastTailPin = long.MinValue;

    public void CompleteWrite(bool checkDisconnect = true, bool softly = true)
    {
        if (IsEventsEnabled)
        {
            bool setFlag = false;
            lock (LockObj)
            {
                long current = PinTail;
                if (LastTailPin != current)
                {
                    LastTailPin = current;
                    setFlag = true;
                }
                if (IsDisconnected)
                    setFlag = true;
            }
            if (setFlag)
            {
                EventReadReady?.Set(softly);
            }
        }

        if (checkDisconnect)
            CheckDisconnected();
    }

    public void Clear()
    {
        checked
        {
            List.Clear();
            PinTail = PinHead;
        }
    }

    public void InsertBefore(ReadOnlyMemory<T> item)
    {
        CheckDisconnected();
        checked
        {
            if (item.IsEmpty) return;
            List.AddFirst(item);
            PinHead -= item.Length;
        }
    }

    public void InsertHead(ReadOnlyMemory<T> item)
    {
        CheckDisconnected();
        checked
        {
            if (item.IsEmpty) return;
            List.AddFirst(item);
            PinTail += item.Length;
        }
    }

    public void InsertTail(ReadOnlyMemory<T> item)
    {
        CheckDisconnected();
        checked
        {
            if (item.IsEmpty) return;
            List.AddLast(item);
            PinTail += item.Length;
        }
    }

    public void Insert(long pin, ReadOnlyMemory<T> item, bool appendIfOverrun = false)
    {
        CheckDisconnected();
        checked
        {
            if (item.IsEmpty) return;

            if (List.First == null)
            {
                InsertHead(item);
                return;
            }

            if (appendIfOverrun)
            {
                if (pin < PinHead)
                    InsertBefore(new T[PinHead - pin]);

                if (pin > PinTail)
                    InsertTail(new T[pin - PinTail]);
            }
            else
            {
                if (List.First == null) throw new ArgumentOutOfRangeException("Buffer is empty.");
                if (pin < PinHead) throw new ArgumentOutOfRangeException("pin < PinHead");
                if (pin > PinTail) throw new ArgumentOutOfRangeException("pin > PinTail");
            }

            var node = GetNodeWithPin(pin, out int offsetInSegment, out _);
            Debug.Assert(node != null);
            if (offsetInSegment == 0)
            {
                var newNode = List.AddBefore(node, item);
                PinTail += item.Length;
            }
            else if (node.Value.Length == offsetInSegment)
            {
                var newNode = List.AddAfter(node, item);
                PinTail += item.Length;
            }
            else
            {
                ReadOnlyMemory<T> sliceBefore = node.Value.Slice(0, offsetInSegment);
                ReadOnlyMemory<T> sliceAfter = node.Value.Slice(offsetInSegment);

                node.Value = sliceBefore;
                var newNode = List.AddAfter(node, item);
                List.AddAfter(newNode, sliceAfter);
                PinTail += item.Length;
            }
        }
    }

    [MethodImpl(Inline)]
    FastLinkedListNode<ReadOnlyMemory<T>>? GetNodeWithPin(long pin, out int offsetInSegment, out long nodePin)
    {
        checked
        {
            offsetInSegment = 0;
            nodePin = 0;
            if (List.First == null)
            {
                if (pin != PinHead) throw new ArgumentOutOfRangeException("List.First == null, but pin != PinHead");
                return null;
            }
            if (pin < PinHead) throw new ArgumentOutOfRangeException("pin < PinHead");
            if (pin == PinHead)
            {
                nodePin = pin;
                return List.First;
            }
            if (pin > PinTail) throw new ArgumentOutOfRangeException("pin > PinTail");
            if (pin == PinTail)
            {
                var last = List.Last;
                if (last != null)
                {
                    offsetInSegment = last.Value.Length;
                    nodePin = PinTail - last.Value.Length;
                }
                else
                {
                    nodePin = PinTail;
                }
                return last;
            }
            long currentPin = PinHead;
            FastLinkedListNode<ReadOnlyMemory<T>>? node = List.First;
            while (node != null)
            {
                if (pin >= currentPin && pin < (currentPin + node.Value.Length))
                {
                    offsetInSegment = (int)(pin - currentPin);
                    nodePin = currentPin;
                    return node;
                }
                currentPin += node.Value.Length;
                node = node.Next;
            }
            throw new ApplicationException("GetNodeWithPin: Bug!");
        }
    }

    [MethodImpl(Inline)]
    void GetOverlappedNodes(long pinStart, long pinEnd,
        out FastLinkedListNode<ReadOnlyMemory<T>> firstNode, out int firstNodeOffsetInSegment, out long firstNodePin,
        out FastLinkedListNode<ReadOnlyMemory<T>> lastNode, out int lastNodeOffsetInSegment, out long lastNodePin,
        out int nodeCounts, out int lackRemainLength)
    {
        checked
        {
            if (pinStart > pinEnd) throw new ArgumentOutOfRangeException("pinStart > pinEnd");

            firstNode = GetNodeWithPin(pinStart, out firstNodeOffsetInSegment, out firstNodePin)!;

            Debug.Assert(firstNode != null);

            if (pinEnd > PinTail)
            {
                lackRemainLength = (int)checked(pinEnd - PinTail);
                pinEnd = PinTail;
            }

            FastLinkedListNode<ReadOnlyMemory<T>>? node = firstNode;
            long currentPin = pinStart - firstNodeOffsetInSegment;
            nodeCounts = 0;
            while (true)
            {
                Debug.Assert(node != null, "node == null");

                nodeCounts++;
                if (pinEnd <= (currentPin + node.Value.Length))
                {
                    lastNodeOffsetInSegment = (int)(pinEnd - currentPin);
                    lastNode = node;
                    lackRemainLength = 0;
                    lastNodePin = currentPin;

                    Debug.Assert(firstNodeOffsetInSegment != firstNode.Value.Length);
                    Debug.Assert(lastNodeOffsetInSegment != 0);

                    return;
                }
                currentPin += node.Value.Length;
                node = node.Next;
            }
        }
    }

    public ReadOnlySpan<ReadOnlyMemory<T>> GetAllFast()
    {
        FastBufferSegment<ReadOnlyMemory<T>>[] segments = GetSegmentsFast(this.PinHead, this.Length, out long readSize, true);

        Span<ReadOnlyMemory<T>> ret = new ReadOnlyMemory<T>[segments.Length];

        for (int i = 0; i < segments.Length; i++)
        {
            ret[i] = segments[i].Item;
        }

        return ret;
    }

    public FastBufferSegment<ReadOnlyMemory<T>>[] GetSegmentsFast(long pin, long size, out long readSize, bool allowPartial = false)
    {
        checked
        {
            if (size < 0) throw new ArgumentOutOfRangeException("size < 0");
            if (size == 0)
            {
                readSize = 0;
                return new FastBufferSegment<ReadOnlyMemory<T>>[0];
            }
            if (pin > PinTail)
            {
                throw new ArgumentOutOfRangeException("pin > PinTail");
            }
            if ((pin + size) > PinTail)
            {
                if (allowPartial == false)
                    throw new ArgumentOutOfRangeException("(pin + size) > PinTail");
                size = PinTail - pin;
            }

            FastBufferSegment<ReadOnlyMemory<T>>[] ret = GetUncontiguousSegments(pin, pin + size, false);
            readSize = size;
            return ret;
        }
    }

    public FastBufferSegment<ReadOnlyMemory<T>>[] ReadForwardFast(ref long pin, long size, out long readSize, bool allowPartial = false)
    {
        checked
        {
            FastBufferSegment<ReadOnlyMemory<T>>[] ret = GetSegmentsFast(pin, size, out readSize, allowPartial);
            pin += readSize;
            return ret;
        }
    }

    public ReadOnlyMemory<T> GetContiguous(long pin, long size, bool allowPartial = false)
    {
        checked
        {
            if (size < 0) throw new ArgumentOutOfRangeException("size < 0");
            if (size == 0)
            {
                return new ReadOnlyMemory<T>();
            }
            if (pin > PinTail)
            {
                throw new ArgumentOutOfRangeException("pin > PinTail");
            }
            if ((pin + size) > PinTail)
            {
                if (allowPartial == false)
                    throw new ArgumentOutOfRangeException("(pin + size) > PinTail");
                size = PinTail - pin;
            }
            ReadOnlyMemory<T> ret = GetContiguousReadOnlyMemory(pin, pin + size, false, false);
            return ret;
        }
    }

    public ReadOnlyMemory<T> ReadForwardContiguous(ref long pin, long size, bool allowPartial = false)
    {
        checked
        {
            ReadOnlyMemory<T> ret = GetContiguous(pin, size, allowPartial);
            pin += ret.Length;
            return ret;
        }
    }

    public Memory<T> PutContiguous(long pin, long size, bool appendIfOverrun = false)
    {
        checked
        {
            if (size < 0) throw new ArgumentOutOfRangeException("size < 0");
            if (size == 0)
            {
                return new Memory<T>();
            }
            Memory<T> ret = GetContiguousWritableMemory(pin, pin + size, appendIfOverrun);
            return ret;
        }
    }

    public Memory<T> WriteForwardContiguous(ref long pin, long size, bool appendIfOverrun = false)
    {
        checked
        {
            Memory<T> ret = PutContiguous(pin, size, appendIfOverrun);
            pin += ret.Length;
            return ret;
        }
    }

    public long Enqueue(ReadOnlyMemory<T> item)
    {
        CheckDisconnected();
        long oldLen = Length;
        if (item.Length == 0) return Length;
        InsertTail(item);
        EventListeners.Fire(this, FastBufferCallbackEventType.Written);
        if (Length != 0 && oldLen == 0)
            EventListeners.Fire(this, FastBufferCallbackEventType.EmptyToNonEmpty);
        return Length;
    }

    public long EnqueueWithLock(ReadOnlyMemory<T> item, bool completeWrite = false)
    {
        long ret = 0;

        lock (LockObj)
        {
            ret = Enqueue(item);
        }

        if (completeWrite)
            CompleteWrite();

        return ret;
    }

    public long EnqueueAllWithLock(ReadOnlySpan<ReadOnlyMemory<T>> itemList, bool completeWrite = false)
    {
        long ret = 0;

        lock (LockObj)
        {
            ret = EnqueueAll(itemList);
        }

        if (completeWrite)
            CompleteWrite();

        return ret;
    }

    public long EnqueueAll(ReadOnlySpan<ReadOnlyMemory<T>> itemList)
    {
        CheckDisconnected();
        checked
        {
            int num = 0;
            long oldLen = Length;
            foreach (ReadOnlyMemory<T> t in itemList)
            {
                if (t.Length != 0)
                {
                    List.AddLast(t);
                    PinTail += t.Length;
                    num++;
                }
            }
            if (num >= 1)
            {
                EventListeners.Fire(this, FastBufferCallbackEventType.Written);

                if (Length != 0 && oldLen == 0)
                    EventListeners.Fire(this, FastBufferCallbackEventType.EmptyToNonEmpty);
            }

            return Length;
        }
    }

    public int DequeueContiguousSlowWithLock(Memory<T> dest, int size = int.MaxValue)
    {
        lock (this.LockObj)
            return DequeueContiguousSlow(dest, size);
    }

    public int DequeueContiguousSlow(Memory<T> dest, int size = int.MaxValue)
    {
        if (IsDisconnected && this.Length == 0) CheckDisconnected();
        checked
        {
            long oldLen = Length;
            if (size < 0) throw new ArgumentOutOfRangeException("size < 0");
            size = Math.Min(size, dest.Length);
            Debug.Assert(size >= 0);
            if (size == 0) return 0;
            var memarray = Dequeue(size, out long totalSize, true);
            Debug.Assert(totalSize <= size);
            if (totalSize > int.MaxValue) throw new IndexOutOfRangeException("totalSize > int.MaxValue");
            if (dest.Length < totalSize) throw new ArgumentOutOfRangeException("dest.Length < totalSize");
            int pos = 0;
            foreach (var mem in memarray)
            {
                mem.CopyTo(dest.Slice(pos, mem.Length));
                pos += mem.Length;
            }
            Debug.Assert(pos == totalSize);
            EventListeners.Fire(this, FastBufferCallbackEventType.Read);
            if (Length == 0 && oldLen != 0)
                EventListeners.Fire(this, FastBufferCallbackEventType.NonEmptyToEmpty);
            return (int)totalSize;
        }
    }

    public ReadOnlyMemory<T> DequeueContiguousSlowWithLock(int size = int.MaxValue)
    {
        lock (this.LockObj)
            return DequeueContiguousSlow(size);
    }

    public ReadOnlyMemory<T> DequeueContiguousSlow(int size = int.MaxValue)
    {
        if (IsDisconnected && this.Length == 0) CheckDisconnected();
        checked
        {
            long oldLen = Length;
            if (size < 0) throw new ArgumentOutOfRangeException("size < 0");
            if (size == 0) return ReadOnlyMemory<T>.Empty;
            int readSize = (int)Math.Min(size, Length);
            Memory<T> ret = new T[readSize];
            int r = DequeueContiguousSlow(ret, readSize);
            Debug.Assert(r <= readSize);
            ret = ret.Slice(0, r);
            EventListeners.Fire(this, FastBufferCallbackEventType.Read);
            if (Length == 0 && oldLen != 0)
                EventListeners.Fire(this, FastBufferCallbackEventType.NonEmptyToEmpty);
            return ret;
        }
    }

    public List<ReadOnlyMemory<T>> DequeueAllWithLock(out long totalReadSize)
    {
        lock (this.LockObj)
            return DequeueAll(out totalReadSize);
    }
    public List<ReadOnlyMemory<T>> DequeueAll(out long totalReadSize) => Dequeue(long.MaxValue, out totalReadSize);

    public List<ReadOnlyMemory<T>> DequeueWithLock(long minReadSize, out long totalReadSize, bool allowSplitSegments = true)
    {
        lock (this.LockObj)
            return Dequeue(minReadSize, out totalReadSize, allowSplitSegments);
    }

    public List<ReadOnlyMemory<T>> Dequeue(long minReadSize, out long totalReadSize, bool allowSplitSegments = true)
    {
        if (IsDisconnected && this.Length == 0) CheckDisconnected();
        checked
        {
            if (minReadSize < 1) throw new ArgumentOutOfRangeException("minReadSize < 1");

            totalReadSize = 0;
            if (List.First == null)
            {
                return new List<ReadOnlyMemory<T>>();
            }

            long oldLen = Length;

            FastLinkedListNode<ReadOnlyMemory<T>>? node = List.First;
            List<ReadOnlyMemory<T>> ret = new List<ReadOnlyMemory<T>>();
            while (true)
            {
                if ((totalReadSize + node.Value.Length) >= minReadSize)
                {
                    if (allowSplitSegments && (totalReadSize + node.Value.Length) > minReadSize)
                    {
                        int lastSegmentReadSize = (int)(minReadSize - totalReadSize);
                        Debug.Assert(lastSegmentReadSize <= node.Value.Length);
                        ret.Add(node.Value.Slice(0, lastSegmentReadSize));
                        if (lastSegmentReadSize == node.Value.Length)
                            List.Remove(node);
                        else
                            node.Value = node.Value.Slice(lastSegmentReadSize);
                        totalReadSize += lastSegmentReadSize;
                        PinHead += totalReadSize;
                        Debug.Assert(minReadSize >= totalReadSize);
                        EventListeners.Fire(this, FastBufferCallbackEventType.Read);
                        if (Length == 0 && oldLen != 0)
                            EventListeners.Fire(this, FastBufferCallbackEventType.NonEmptyToEmpty);
                        return ret;
                    }
                    else
                    {
                        ret.Add(node.Value);
                        totalReadSize += node.Value.Length;
                        List.Remove(node);
                        PinHead += totalReadSize;
                        Debug.Assert(minReadSize <= totalReadSize);
                        EventListeners.Fire(this, FastBufferCallbackEventType.Read);
                        if (Length == 0 && oldLen != 0)
                            EventListeners.Fire(this, FastBufferCallbackEventType.NonEmptyToEmpty);
                        return ret;
                    }
                }
                else
                {
                    ret.Add(node.Value);
                    totalReadSize += node.Value.Length;

                    FastLinkedListNode<ReadOnlyMemory<T>> deleteNode = node;
                    node = node.Next;

                    List.Remove(deleteNode);

                    if (node == null)
                    {
                        PinHead += totalReadSize;
                        EventListeners.Fire(this, FastBufferCallbackEventType.Read);
                        if (Length == 0 && oldLen != 0)
                            EventListeners.Fire(this, FastBufferCallbackEventType.NonEmptyToEmpty);
                        return ret;
                    }
                }
            }
        }
    }

    public long DequeueAllAndEnqueueToOther(IFastBuffer<ReadOnlyMemory<T>> other) => DequeueAllAndEnqueueToOther((FastStreamBuffer<T>)other);

    public long DequeueAllAndEnqueueToOther(FastStreamBuffer<T> other)
    {
        if (IsDisconnected && this.Length == 0) CheckDisconnected();
        other.CheckDisconnected();
        checked
        {
            if (this == other) throw new ArgumentException("this == other");

            if (this.Length == 0)
            {
                Debug.Assert(this.List.Count == 0);
                return 0;
            }

            if (other.Length == 0)
            {
                long length = this.Length;
                long otherOldLen = other.Length;
                long oldLen = Length;
                Debug.Assert(other.List.Count == 0);
                other.List = this.List;
                this.List = new FastLinkedList<ReadOnlyMemory<T>>();
                this.PinHead = this.PinTail;
                other.PinTail += length;
                EventListeners.Fire(this, FastBufferCallbackEventType.Read);
                if (Length == 0 && oldLen != 0)
                    EventListeners.Fire(this, FastBufferCallbackEventType.NonEmptyToEmpty);
                other.EventListeners.Fire(other, FastBufferCallbackEventType.Written);
                if (other.Length != 0 && otherOldLen == 0)
                    other.EventListeners.Fire(other, FastBufferCallbackEventType.EmptyToNonEmpty);
                return length;
            }
            else
            {
                long length = this.Length;
                long oldLen = Length;
                long otherOldLen = other.Length;
                var chainFirst = this.List.First;
                var chainLast = this.List.Last;
                other.List.AddLast(this.List.First!, this.List.Last!, this.List.Count);
                this.List.Clear();
                this.PinHead = this.PinTail;
                other.PinTail += length;
                EventListeners.Fire(this, FastBufferCallbackEventType.Read);
                if (Length == 0 && oldLen != 0)
                    EventListeners.Fire(this, FastBufferCallbackEventType.NonEmptyToEmpty);
                other.EventListeners.Fire(other, FastBufferCallbackEventType.Written);
                if (other.Length != 0 && otherOldLen == 0)
                    other.EventListeners.Fire(other, FastBufferCallbackEventType.EmptyToNonEmpty);
                return length;
            }
        }
    }

    FastBufferSegment<ReadOnlyMemory<T>>[] GetUncontiguousSegments(long pinStart, long pinEnd, bool appendIfOverrun)
    {
        checked
        {
            if (pinStart == pinEnd) return new FastBufferSegment<ReadOnlyMemory<T>>[0];
            if (pinStart > pinEnd) throw new ArgumentOutOfRangeException("pinStart > pinEnd");

            if (appendIfOverrun)
            {
                if (List.First == null)
                {
                    InsertHead(new T[pinEnd - pinStart]);
                    PinHead = pinStart;
                    PinTail = pinEnd;
                }

                if (pinStart < PinHead)
                    InsertBefore(new T[PinHead - pinStart]);

                if (pinEnd > PinTail)
                    InsertTail(new T[pinEnd - PinTail]);
            }
            else
            {
                if (List.First == null) throw new ArgumentOutOfRangeException("Buffer is empty.");
                if (pinStart < PinHead) throw new ArgumentOutOfRangeException("pinStart < PinHead");
                if (pinEnd > PinTail) throw new ArgumentOutOfRangeException("pinEnd > PinTail");
            }

            GetOverlappedNodes(pinStart, pinEnd,
                out FastLinkedListNode<ReadOnlyMemory<T>> firstNode, out int firstNodeOffsetInSegment, out long firstNodePin,
                out FastLinkedListNode<ReadOnlyMemory<T>> lastNode, out int lastNodeOffsetInSegment, out long lastNodePin,
                out int nodeCounts, out int lackRemainLength);

            Debug.Assert(lackRemainLength == 0, "lackRemainLength != 0");

            if (firstNode == lastNode)
                return new FastBufferSegment<ReadOnlyMemory<T>>[1]{ new FastBufferSegment<ReadOnlyMemory<T>>(
                    firstNode.Value.Slice(firstNodeOffsetInSegment, lastNodeOffsetInSegment - firstNodeOffsetInSegment), pinStart, 0) };

            FastBufferSegment<ReadOnlyMemory<T>>[] ret = new FastBufferSegment<ReadOnlyMemory<T>>[nodeCounts];

            FastLinkedListNode<ReadOnlyMemory<T>>? prevNode = firstNode.Previous;
            FastLinkedListNode<ReadOnlyMemory<T>>? nextNode = lastNode.Next;

            FastLinkedListNode<ReadOnlyMemory<T>>? node = firstNode;
            int count = 0;
            long currentOffset = 0;

            while (true)
            {
                Debug.Assert(node != null, "node == null");

                int sliceStart = (node == firstNode) ? firstNodeOffsetInSegment : 0;
                int sliceLength = (node == lastNode) ? lastNodeOffsetInSegment : node.Value.Length - sliceStart;

                ret[count] = new FastBufferSegment<ReadOnlyMemory<T>>(node.Value.Slice(sliceStart, sliceLength), currentOffset + pinStart, currentOffset);
                count++;

                Debug.Assert(count <= nodeCounts, "count > nodeCounts");

                currentOffset += sliceLength;

                if (node == lastNode)
                {
                    Debug.Assert(count == ret.Length, "count != ret.Length");
                    break;
                }

                node = node.Next;
            }

            return ret;
        }
    }

    public void Remove(long pinStart, long length)
    {
        checked
        {
            if (length == 0) return;
            if (length < 0) throw new ArgumentOutOfRangeException("length < 0");
            long pinEnd = checked(pinStart + length);
            if (List.First == null) throw new ArgumentOutOfRangeException("Buffer is empty.");
            if (pinStart < PinHead) throw new ArgumentOutOfRangeException("pinStart < PinHead");
            if (pinEnd > PinTail) throw new ArgumentOutOfRangeException("pinEnd > PinTail");

            GetOverlappedNodes(pinStart, pinEnd,
                out FastLinkedListNode<ReadOnlyMemory<T>> firstNode, out int firstNodeOffsetInSegment, out long firstNodePin,
                out FastLinkedListNode<ReadOnlyMemory<T>> lastNode, out int lastNodeOffsetInSegment, out long lastNodePin,
                out int nodeCounts, out int lackRemainLength);

            Debug.Assert(lackRemainLength == 0, "lackRemainLength != 0");

            if (firstNode == lastNode)
            {
                Debug.Assert(firstNodeOffsetInSegment < lastNodeOffsetInSegment);
                if (firstNodeOffsetInSegment == 0 && lastNodeOffsetInSegment == lastNode.Value.Length)
                {
                    Debug.Assert(firstNode.Value.Length == length, "firstNode.Value.Length != length");
                    List.Remove(firstNode);
                    PinTail -= length;
                    return;
                }
                else
                {
                    Debug.Assert((lastNodeOffsetInSegment - firstNodeOffsetInSegment) == length);
                    ReadOnlyMemory<T> slice1 = firstNode.Value.Slice(0, firstNodeOffsetInSegment);
                    ReadOnlyMemory<T> slice2 = firstNode.Value.Slice(lastNodeOffsetInSegment);
                    Debug.Assert(slice1.Length != 0 || slice2.Length != 0);
                    if (slice1.Length == 0)
                    {
                        firstNode.Value = slice2;
                    }
                    else if (slice2.Length == 0)
                    {
                        firstNode.Value = slice1;
                    }
                    else
                    {
                        firstNode.Value = slice1;
                        List.AddAfter(firstNode, slice2);
                    }
                    PinTail -= length;
                    return;
                }
            }
            else
            {
                firstNode.Value = firstNode.Value.Slice(0, firstNodeOffsetInSegment);
                lastNode.Value = lastNode.Value.Slice(lastNodeOffsetInSegment);

                var node = firstNode.Next;
                while (node != lastNode)
                {
                    var nodeToDelete = node;

                    Debug.Assert(node!.Next != null);
                    node = node.Next;

                    List.Remove(nodeToDelete!);
                }

                if (lastNode.Value.Length == 0)
                    List.Remove(lastNode);

                if (firstNode.Value.Length == 0)
                    List.Remove(firstNode);

                PinTail -= length;
                return;
            }
        }
    }

    public T[] ToArray() => GetContiguousReadOnlyMemory(PinHead, PinTail, false, true).ToArray();

    public T[] ItemsSlow { get => ToArray(); }

    Memory<T> GetContiguousWritableMemory(long pinStart, long pinEnd, bool appendIfOverrun)
    {
        checked
        {
            if (pinStart == pinEnd) return new Memory<T>();
            if (pinStart > pinEnd) throw new ArgumentOutOfRangeException("pinStart > pinEnd");

            if (appendIfOverrun)
            {
                if (List.First == null)
                {
                    InsertHead(new T[pinEnd - pinStart]);
                    PinHead = pinStart;
                    PinTail = pinEnd;
                }

                if (pinStart < PinHead)
                    InsertBefore(new T[PinHead - pinStart]);

                if (pinEnd > PinTail)
                    InsertTail(new T[pinEnd - PinTail]);
            }
            else
            {
                if (List.First == null) throw new ArgumentOutOfRangeException("Buffer is empty.");
                if (pinStart < PinHead) throw new ArgumentOutOfRangeException("pinStart < PinHead");
                if (pinEnd > PinTail) throw new ArgumentOutOfRangeException("pinEnd > PinTail");
            }

            GetOverlappedNodes(pinStart, pinEnd,
                out FastLinkedListNode<ReadOnlyMemory<T>> firstNode, out int firstNodeOffsetInSegment, out long firstNodePin,
                out FastLinkedListNode<ReadOnlyMemory<T>> lastNode, out int lastNodeOffsetInSegment, out long lastNodePin,
                out int nodeCounts, out int lackRemainLength);

            Debug.Assert(lackRemainLength == 0, "lackRemainLength != 0");

            if (firstNode == lastNode)
            {
                Memory<T> newMemory2 = firstNode.Value.ToArray();
                firstNode.Value = newMemory2;
                return newMemory2.Slice(firstNodeOffsetInSegment, lastNodeOffsetInSegment - firstNodeOffsetInSegment);
            }

            FastLinkedListNode<ReadOnlyMemory<T>>? prevNode = firstNode.Previous;
            FastLinkedListNode<ReadOnlyMemory<T>>? nextNode = lastNode.Next;

            Memory<T> newMemory = new T[lastNodePin + lastNode.Value.Length - firstNodePin];
            FastLinkedListNode<ReadOnlyMemory<T>>? node = firstNode;
            int currentWritePointer = 0;

            while (true)
            {
                Debug.Assert(node != null, "node == null");

                bool finish = false;
                node.Value.CopyTo(newMemory.Slice(currentWritePointer));

                if (node == lastNode) finish = true;

                FastLinkedListNode<ReadOnlyMemory<T>> nodeToDelete = node;
                currentWritePointer += node.Value.Length;

                node = node.Next;

                List.Remove(nodeToDelete);

                if (finish) break;
            }

            if (prevNode != null)
                List.AddAfter(prevNode, newMemory);
            else if (nextNode != null)
                List.AddBefore(nextNode, newMemory);
            else
                List.AddFirst(newMemory);

            var ret = newMemory.Slice(firstNodeOffsetInSegment, newMemory.Length - (lastNode.Value.Length - lastNodeOffsetInSegment) - firstNodeOffsetInSegment);
            Debug.Assert(ret.Length == (pinEnd - pinStart), "ret.Length");
            return ret;
        }
    }

    ReadOnlyMemory<T> GetContiguousReadOnlyMemory(long pinStart, long pinEnd, bool appendIfOverrun, bool noReplace)
    {
        checked
        {
            if (pinStart == pinEnd) return new ReadOnlyMemory<T>();
            if (pinStart > pinEnd) throw new ArgumentOutOfRangeException("pinStart > pinEnd");

            if (appendIfOverrun)
            {
                if (List.First == null)
                {
                    InsertHead(new T[pinEnd - pinStart]);
                    PinHead = pinStart;
                    PinTail = pinEnd;
                }

                if (pinStart < PinHead)
                    InsertBefore(new T[PinHead - pinStart]);

                if (pinEnd > PinTail)
                    InsertTail(new T[pinEnd - PinTail]);
            }
            else
            {
                if (List.First == null) throw new ArgumentOutOfRangeException("Buffer is empty.");
                if (pinStart < PinHead) throw new ArgumentOutOfRangeException("pinStart < PinHead");
                if (pinEnd > PinTail) throw new ArgumentOutOfRangeException("pinEnd > PinTail");
            }

            GetOverlappedNodes(pinStart, pinEnd,
                out FastLinkedListNode<ReadOnlyMemory<T>> firstNode, out int firstNodeOffsetInSegment, out long firstNodePin,
                out FastLinkedListNode<ReadOnlyMemory<T>> lastNode, out int lastNodeOffsetInSegment, out long lastNodePin,
                out int nodeCounts, out int lackRemainLength);

            Debug.Assert(lackRemainLength == 0, "lackRemainLength != 0");

            if (firstNode == lastNode)
                return firstNode.Value.Slice(firstNodeOffsetInSegment, lastNodeOffsetInSegment - firstNodeOffsetInSegment);

            FastLinkedListNode<ReadOnlyMemory<T>>? prevNode = firstNode.Previous;
            FastLinkedListNode<ReadOnlyMemory<T>>? nextNode = lastNode.Next;

            Memory<T> newMemory = new T[lastNodePin + lastNode.Value.Length - firstNodePin];
            FastLinkedListNode<ReadOnlyMemory<T>>? node = firstNode;
            int currentWritePointer = 0;

            while (true)
            {
                Debug.Assert(node != null, "node == null");

                bool finish = false;
                node.Value.CopyTo(newMemory.Slice(currentWritePointer));

                if (node == lastNode) finish = true;

                FastLinkedListNode<ReadOnlyMemory<T>> nodeToDelete = node;
                currentWritePointer += node.Value.Length;

                node = node.Next;

                if (noReplace == false)
                    List.Remove(nodeToDelete);

                if (finish) break;
            }

            if (noReplace == false)
            {
                if (prevNode != null)
                    List.AddAfter(prevNode, newMemory);
                else if (nextNode != null)
                    List.AddBefore(nextNode, newMemory);
                else
                    List.AddFirst(newMemory);
            }

            var ret = newMemory.Slice(firstNodeOffsetInSegment, newMemory.Length - (lastNode.Value.Length - lastNodeOffsetInSegment) - firstNodeOffsetInSegment);
            Debug.Assert(ret.Length == (pinEnd - pinStart), "ret.Length");
            return ret;
        }
    }

    public static implicit operator FastStreamBuffer<T>(ReadOnlyMemory<T> memory)
    {
        FastStreamBuffer<T> ret = new FastStreamBuffer<T>(false, null);
        ret.Enqueue(memory);
        return ret;
    }

    public static implicit operator FastStreamBuffer<T>(Span<T> span) => span.ToArray()._AsReadOnlyMemory();
    public static implicit operator FastStreamBuffer<T>(ReadOnlySpan<T> span) => span.ToArray()._AsReadOnlyMemory();
    public static implicit operator FastStreamBuffer<T>(T[] data) => data._AsReadOnlyMemory();
}

public class FastDatagramBuffer<T> : IFastBuffer<T>
{
    Fifo<T> Fifo = new Fifo<T>(clearUnused: true);

    public long PinHead { get; private set; } = 0;
    public long PinTail { get; private set; } = 0;
    public long Length { get { long ret = checked(PinTail - PinHead); Debug.Assert(ret >= 0); return ret; } }
    public long Threshold { get; set; }
    public long Id { get; }

    public FastEventListenerList<IFastBufferState, FastBufferCallbackEventType> EventListeners { get; }
        = new FastEventListenerList<IFastBufferState, FastBufferCallbackEventType>();

    public bool IsReadyToWrite()
    {
        if (IsDisconnected) return true;
        if (Length <= Threshold) return true;
        CompleteWrite(false, true);
        return false;
    }

    public bool IsReadyToRead(int size = 1)
    {
        size = Math.Max(size, 1);
        if (IsDisconnected) return true;
        if (Length >= size) return true;
        CompleteRead();
        return false;
    }

    public bool IsEventsEnabled { get; }

    public AsyncAutoResetEvent? EventWriteReady { get; } = null;
    public AsyncAutoResetEvent? EventReadReady { get; } = null;

    Once internalDisconnectedFlag;
    public bool IsDisconnected { get => internalDisconnectedFlag.IsSet; }

    public List<Func<Task>> OnDisconnected { get; } = new List<Func<Task>>();

    public static readonly long DefaultThreshold = CoresConfig.FastBufferConfig.DefaultFastDatagramBufferThreshold;

    public CriticalSection LockObj { get; } = new CriticalSection<FastDatagramBuffer<T>>();

    public ExceptionQueue ExceptionQueue { get; } = new ExceptionQueue();
    public LayerInfo Info { get; } = new LayerInfo();

    public FastDatagramBuffer(bool enableEvents = false, long? thresholdLength = null)
    {
        if (thresholdLength < 0) throw new ArgumentOutOfRangeException("thresholdLength < 0");

        Threshold = thresholdLength ?? DefaultThreshold;
        IsEventsEnabled = enableEvents;
        if (IsEventsEnabled)
        {
            EventWriteReady = new AsyncAutoResetEvent();
            EventReadReady = new AsyncAutoResetEvent();
        }

        Id = FastBufferGlobalIdCounter.NewId();

        EventListeners.Fire(this, FastBufferCallbackEventType.Init);
    }

    Once checkDisconnectFlag;

    public void CheckDisconnected()
    {
        if (IsDisconnected)
        {
            if (checkDisconnectFlag.IsFirstCall())
                ExceptionQueue.Raise(new FastBufferDisconnectedException());
            else
            {
                ExceptionQueue.ThrowFirstExceptionIfExists();
                throw new FastBufferDisconnectedException();
            }
        }
    }

    public void Disconnect()
    {
        if (internalDisconnectedFlag.IsFirstCall())
        {
            foreach (var ev in OnDisconnected)
            {
                try
                {
                    ev();
                }
                catch { }
            }
            EventReadReady?.Set(softly: true);
            EventWriteReady?.Set(softly: true);

            EventListeners.Fire(this, FastBufferCallbackEventType.Disconnected);
        }
    }

    long LastHeadPin = long.MinValue;

    public void CompleteRead(bool checkDisconnect = false, bool softly = true)
    {
        if (IsEventsEnabled)
        {
            bool setFlag = false;

            lock (LockObj)
            {
                long current = PinHead;
                if (LastHeadPin != current)
                {
                    LastHeadPin = current;
                    if (IsReadyToWrite())
                        setFlag = true;
                }
                if (IsDisconnected)
                    setFlag = true;
            }

            if (setFlag)
            {
                EventWriteReady?.Set(softly);
            }
        }

        if (checkDisconnect)
            CheckDisconnected();
    }

    long LastTailPin = long.MinValue;

    public void CompleteWrite(bool checkDisconnect = true, bool softly = true)
    {
        if (IsEventsEnabled)
        {
            bool setFlag = false;

            lock (LockObj)
            {
                long current = PinTail;
                if (LastTailPin != current)
                {
                    LastTailPin = current;
                    setFlag = true;
                }
            }

            if (setFlag)
            {
                EventReadReady?.Set(softly);
            }
        }
        if (checkDisconnect)
            CheckDisconnected();
    }

    public void Clear()
    {
        checked
        {
            Fifo.Clear();
            PinTail = PinHead;
        }
    }

    public long Enqueue(T item)
    {
        CheckDisconnected();
        checked
        {
            long oldLen = Length;
            Fifo.Write(item);
            PinTail++;
            EventListeners.Fire(this, FastBufferCallbackEventType.Written);
            if (Length != 0 && oldLen == 0)
                EventListeners.Fire(this, FastBufferCallbackEventType.EmptyToNonEmpty);
            return Length;
        }
    }

    public long EnqueueAllWithLock(ReadOnlySpan<T> itemList, bool completeWrite = false)
    {
        long ret = 0;

        lock (LockObj)
        {
            ret = EnqueueAll(itemList);
        }

        if (completeWrite)
        {
            this.CompleteWrite();
        }

        return ret;
    }

    public long EnqueueAll(ReadOnlySpan<T> itemList)
    {
        CheckDisconnected();
        checked
        {
            long oldLen = Length;
            Fifo.Write(itemList);
            PinTail += itemList.Length;
            EventListeners.Fire(this, FastBufferCallbackEventType.Written);
            if (Length != 0 && oldLen == 0)
                EventListeners.Fire(this, FastBufferCallbackEventType.EmptyToNonEmpty);
            return Length;
        }
    }

    public List<T> DequeueWithLock(long minReadSize, out long totalReadSize, bool allowSplitSegments = true)
    {
        lock (this.LockObj)
            return Dequeue(minReadSize, out totalReadSize, allowSplitSegments);
    }

    public List<T> Dequeue(long minReadSize, out long totalReadSize, bool allowSplitSegments = true)
    {
        if (IsDisconnected && this.Length == 0) CheckDisconnected();
        checked
        {
            if (minReadSize < 1) throw new ArgumentOutOfRangeException("size < 1");
            if (minReadSize >= int.MaxValue) minReadSize = int.MaxValue;

            long oldLen = Length;

            totalReadSize = 0;
            if (Fifo.Size == 0)
            {
                return new List<T>();
            }

            T[] tmp = Fifo.Read((int)minReadSize);

            totalReadSize = tmp.Length;
            List<T> ret = new List<T>(tmp);

            PinHead += totalReadSize;

            EventListeners.Fire(this, FastBufferCallbackEventType.Read);

            if (Length == 0 && oldLen != 0)
                EventListeners.Fire(this, FastBufferCallbackEventType.NonEmptyToEmpty);

            return ret;
        }
    }

    public List<T> DequeueAll(out long totalReadSize) => Dequeue(long.MaxValue, out totalReadSize);

    public List<T> DequeueAllWithLock(out long totalReadSize)
    {
        lock (LockObj)
            return DequeueAll(out totalReadSize);
    }

    public long DequeueAllAndEnqueueToOther(IFastBuffer<T> other) => DequeueAllAndEnqueueToOther((FastDatagramBuffer<T>)other);

    public long DequeueAllAndEnqueueToOther(FastDatagramBuffer<T> other)
    {
        if (IsDisconnected && this.Length == 0) CheckDisconnected();
        other.CheckDisconnected();
        checked
        {
            if (this == other) throw new ArgumentException("this == other");

            if (this.Length == 0)
            {
                Debug.Assert(this.Fifo.Size == 0);
                return 0;
            }

            if (other.Length == 0)
            {
                long oldLen = Length;
                long length = this.Length;
                Debug.Assert(other.Fifo.Size == 0);
                other.Fifo = this.Fifo;
                this.Fifo = new Fifo<T>(clearUnused: true);
                this.PinHead = this.PinTail;
                other.PinTail += length;
                EventListeners.Fire(this, FastBufferCallbackEventType.Read);
                other.EventListeners.Fire(other, FastBufferCallbackEventType.Written);
                if (Length != 0 && oldLen == 0)
                    EventListeners.Fire(this, FastBufferCallbackEventType.EmptyToNonEmpty);
                return length;
            }
            else
            {
                long oldLen = Length;
                long length = this.Length;
                var data = this.Fifo.Read();
                other.Fifo.Write(data);
                this.PinHead = this.PinTail;
                other.PinTail += length;
                EventListeners.Fire(this, FastBufferCallbackEventType.Read);
                other.EventListeners.Fire(other, FastBufferCallbackEventType.Written);
                if (Length != 0 && oldLen == 0)
                    EventListeners.Fire(this, FastBufferCallbackEventType.EmptyToNonEmpty);
                return length;
            }
        }
    }

    public T[] ToArray() => Fifo.Span.ToArray();

    public T[] ItemsSlow { get => ToArray(); }
}

public class FastStreamBuffer : FastStreamBuffer<byte>
{
    public FastStreamBuffer(bool enableEvents = false, long? thresholdLength = null)
        : base(enableEvents, thresholdLength) { }
}

public class FastDatagramBuffer : FastDatagramBuffer<Datagram>
{
    public FastDatagramBuffer(bool enableEvents = false, long? thresholdLength = null)
        : base(enableEvents, thresholdLength) { }
}

[Flags]
public enum FastStreamNonStopWriteMode
{
    DiscardExistingData = 0,
    DiscardWritingData,
    ForceWrite,
}

public static class FastStreamBufferHelper
{
    public static long NonStopWriteWithLock<T>(this FastStreamBuffer<T> buffer, ReadOnlyMemory<T> item, bool completeWrite = true,
        FastStreamNonStopWriteMode mode = FastStreamNonStopWriteMode.DiscardWritingData, bool doNotSplitSegment = false)
    {
        ReadOnlySpan<ReadOnlyMemory<T>> itemList = new ReadOnlyMemory<T>[] { item };

        return NonStopWriteWithLock(buffer, itemList, completeWrite, mode, doNotSplitSegment);
    }

    public static long NonStopWriteWithLock<T>(this FastStreamBuffer<T> buffer, ReadOnlySpan<ReadOnlyMemory<T>> itemList, bool completeWrite = true,
        FastStreamNonStopWriteMode mode = FastStreamNonStopWriteMode.DiscardWritingData, bool doNotSplitSegment = false)
    {
        long ret = 0;

        checked
        {
            lock (buffer.LockObj)
            {
                if (mode == FastStreamNonStopWriteMode.DiscardExistingData)
                {
                    ReadOnlySpan<ReadOnlyMemory<T>> itemToInsert = Util.GetTailOfReadOnlyMemoryArray(itemList, buffer.Threshold, out long totalSizeToInsert, doNotSplitSegment);

                    long existingDataMaxLength = buffer.Threshold - totalSizeToInsert;
                    if (existingDataMaxLength < buffer.Length)
                    {
                        long sizeToRemove = buffer.Length - existingDataMaxLength;

                        buffer.Dequeue(sizeToRemove, out _, !doNotSplitSegment);
                    }

                    buffer.EnqueueAll(itemToInsert);

                    ret = totalSizeToInsert;
                }
                else if (mode == FastStreamNonStopWriteMode.DiscardWritingData)
                {
                    long freeSpace = buffer.SizeWantToBeWritten;
                    if (freeSpace == 0) return 0;

                    ReadOnlySpan<ReadOnlyMemory<T>> itemToInsert = Util.GetHeadOfReadOnlyMemoryArray(itemList, freeSpace, out long totalSizeToInsert, doNotSplitSegment);

                    buffer.EnqueueAll(itemToInsert);

                    ret = totalSizeToInsert;
                }
                else
                {
                    ret = 0;
                    foreach (var m in itemList)
                    {
                        ret += m.Length;
                    }

                    buffer.EnqueueAll(itemList);
                }
            }
        }

        if (ret >= 1 && completeWrite)
            buffer.CompleteWrite(true);

        return ret;
    }
}
