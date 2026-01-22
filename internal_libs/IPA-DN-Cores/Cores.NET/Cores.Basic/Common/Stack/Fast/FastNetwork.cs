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
using System.Net;
using System.Net.Sockets;
using System.Collections.Generic;
using System.Linq;
using System.IO.Pipelines;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using System.IO;
using System.Buffers;
using System.Runtime.CompilerServices;

namespace IPA.Cores.Basic;

public class LayerInfo
{
    SharedHierarchy<LayerInfoBase> Hierarchy = new SharedHierarchy<LayerInfoBase>();

    public void Install(LayerInfoBase info, LayerInfoBase? targetLayer, bool joinAsSuperior)
    {
        Debug.Assert(info.IsInstalled == false); Debug.Assert(info._InternalHierarchyBodyItem == null); Debug.Assert(info._InternalLayerStack == null);

        info._InternalHierarchyBodyItem = Hierarchy.Join(targetLayer?.Position, joinAsSuperior, info, info.Position);
        info._InternalLayerStack = this;

        Debug.Assert(info.IsInstalled); Debug.Assert(info._InternalHierarchyBodyItem != null);
    }

    public void Uninstall(LayerInfoBase info)
    {
        Debug.Assert(info.IsInstalled); Debug.Assert(info._InternalHierarchyBodyItem != null); Debug.Assert(info._InternalLayerStack != null);

        Hierarchy.Resign(info._InternalHierarchyBodyItem);

        info._InternalHierarchyBodyItem = null;
        info._InternalLayerStack = null;

        Debug.Assert(info.IsInstalled == false);
    }

    public T[] GetValues<T>() where T : class
    {
        var items = Hierarchy.ItemsWithPositionsReadOnly;

        return items.Select(x => x.Value as T).Where(x => x != null).ToArray()!;
    }

    public T GetValue<T>(int index = 0) where T : class
    {
        return GetValues<T>()[index];
    }

    public void Encounter(LayerInfo other) => this.Hierarchy.Encounter(other.Hierarchy);

    public ILayerInfoSsl Ssl => GetValue<ILayerInfoSsl>();
    public ILayerInfoIpEndPoint Ip => GetValue<ILayerInfoIpEndPoint>();
    public ILayerInfoTcpEndPoint Tcp => GetValue<ILayerInfoTcpEndPoint>();

    public LogDefSocket FillSocketLogDef(LogDefSocket log)
    {
        ILayerInfoIpEndPoint ip = this.Ip;
        ILayerInfoTcpEndPoint tcp = this.Tcp;

        log.LocalIP = ip?.LocalIPAddress?.ToString();
        log.RemoteIP = ip?.RemoteIPAddress?.ToString();
        log.NativeHandle = ip?.NativeHandle ?? 0;

        log.Direction = tcp?.Direction.ToString() ?? null;
        log.LocalPort = tcp?.LocalPort;
        log.RemotePort = tcp?.RemotePort;

        return log;
    }
}

public interface ILayerInfoSsl
{
    bool IsServerMode { get; }
    string? SslProtocol { get; }
    string? CipherAlgorithm { get; }
    int CipherStrength { get; }
    string? HashAlgorithm { get; }
    int HashStrength { get; }
    string? KeyExchangeAlgorithm { get; }
    int KeyExchangeStrength { get; }
    PalX509Certificate? LocalCertificate { get; }
    PalX509Certificate? RemoteCertificate { get; }
}

public interface ILayerInfoIpEndPoint
{
    long NativeHandle { get; }
    IPAddress? LocalIPAddress { get; }
    IPAddress? RemoteIPAddress { get; }
}

[Flags]
public enum TcpDirectionType
{
    Unknown = 0, // Must be zero
    Client,
    Server,
}

public interface ILayerInfoTcpEndPoint : ILayerInfoIpEndPoint
{
    TcpDirectionType Direction { get; }
    int LocalPort { get; }
    int RemotePort { get; }
}

public abstract class LayerInfoBase
{
    public HierarchyPosition Position { get; } = new HierarchyPosition();
    internal SharedHierarchy<LayerInfoBase>.HierarchyBodyItem? _InternalHierarchyBodyItem = null;
    internal LayerInfo? _InternalLayerStack = null;

    public NetStackBase? ProtocolStack { get; private set; } = null;

    public bool IsInstalled => Position.IsInstalled;

    public void Install(LayerInfo info, LayerInfoBase targetLayer, bool joinAsSuperior)
        => info.Install(this, targetLayer, joinAsSuperior);

    public void Uninstall()
        => _InternalLayerStack?.Uninstall(this);

    internal void _InternalSetProtocolStack(NetStackBase protocolStack)
        => ProtocolStack = protocolStack;
}

public class DuplexPipe : AsyncService
{
    FastStreamBuffer StreamAtoB;
    FastStreamBuffer StreamBtoA;
    FastDatagramBuffer DatagramAtoB;
    FastDatagramBuffer DatagramBtoA;

    public ExceptionQueue ExceptionQueue { get; } = new ExceptionQueue();
    public LayerInfo LayerInfo { get; } = new LayerInfo();

    public PipePoint A_LowerSide { get; }
    public PipePoint B_UpperSide { get; }

    public PipePoint this[PipePointSide side]
    {
        get
        {
            if (side == PipePointSide.A_LowerSide)
                return A_LowerSide;
            else if (side == PipePointSide.B_UpperSide)
                return B_UpperSide;
            else
                throw new ArgumentOutOfRangeException("side");
        }
    }

    public List<Func<Task>> OnDisconnected { get; } = new List<Func<Task>>();

    public AsyncManualResetEvent OnDisconnectedEvent { get; } = new AsyncManualResetEvent();

    public DuplexPipe(CancellationToken cancel = default, long? thresholdLengthStream = null, long? thresholdLengthDatagram = null) : base(cancel)
    {
        try
        {
            if (thresholdLengthStream == null) thresholdLengthStream = CoresConfig.PipeConfig.MaxStreamBufferLength;
            if (thresholdLengthDatagram == null) thresholdLengthDatagram = CoresConfig.PipeConfig.MaxDatagramQueueLength;

            StreamAtoB = new FastStreamBuffer(true, thresholdLengthStream);
            StreamBtoA = new FastStreamBuffer(true, thresholdLengthStream);

            DatagramAtoB = new FastDatagramBuffer(true, thresholdLengthDatagram);
            DatagramBtoA = new FastDatagramBuffer(true, thresholdLengthDatagram);

            StreamAtoB.ExceptionQueue.Encounter(ExceptionQueue);
            StreamBtoA.ExceptionQueue.Encounter(ExceptionQueue);

            DatagramAtoB.ExceptionQueue.Encounter(ExceptionQueue);
            DatagramBtoA.ExceptionQueue.Encounter(ExceptionQueue);

            StreamAtoB.Info.Encounter(LayerInfo);
            StreamBtoA.Info.Encounter(LayerInfo);

            DatagramAtoB.Info.Encounter(LayerInfo);
            DatagramBtoA.Info.Encounter(LayerInfo);

            StreamAtoB.OnDisconnected.Add(() => this.CancelAsync(new DisconnectedException()));
            StreamBtoA.OnDisconnected.Add(() => this.CancelAsync(new DisconnectedException()));

            DatagramAtoB.OnDisconnected.Add(() => this.CancelAsync(new DisconnectedException()));
            DatagramBtoA.OnDisconnected.Add(() => this.CancelAsync(new DisconnectedException()));

            A_LowerSide = new PipePoint(this, PipePointSide.A_LowerSide, CancelWatcher, StreamAtoB, StreamBtoA, DatagramAtoB, DatagramBtoA);
            B_UpperSide = new PipePoint(this, PipePointSide.B_UpperSide, CancelWatcher, StreamBtoA, StreamAtoB, DatagramBtoA, DatagramAtoB);

            A_LowerSide._InternalSetCounterPart(B_UpperSide);
            B_UpperSide._InternalSetCounterPart(A_LowerSide);
        }
        catch
        {
            this._DisposeSafe();
            throw;
        }
    }

    readonly CriticalSection LayerInfoLock = new CriticalSection<DuplexPipe>();

    public LayerInfoBase? LayerInfo_A_LowerSide { get; private set; } = null;
    public LayerInfoBase? LayerInfo_B_UpperSide { get; private set; } = null;

    public class InstalledLayerHolder : Holder<LayerInfoBase>
    {
        internal InstalledLayerHolder(Action<LayerInfoBase> disposeProc, LayerInfoBase? userData = null) : base(disposeProc, userData) { }
    }

    internal InstalledLayerHolder _InternalInstallLayerInfo(PipePointSide side, LayerInfoBase info, bool uninstallOnDispose)
    {
        if (info == null)
            throw new ArgumentNullException("info");

        lock (LayerInfoLock)
        {
            if (side == PipePointSide.A_LowerSide)
            {
                if (LayerInfo_A_LowerSide != null) throw new ApplicationException("LayerInfo_A_LowerSide is already installed.");
                LayerInfo.Install(info, LayerInfo_B_UpperSide, false);
                LayerInfo_A_LowerSide = info;
            }
            else
            {
                if (LayerInfo_B_UpperSide != null) throw new ApplicationException("LayerInfo_B_UpperSide is already installed.");
                LayerInfo.Install(info, LayerInfo_A_LowerSide, true);
                LayerInfo_B_UpperSide = info;
            }

            return new InstalledLayerHolder(x =>
            {
                lock (LayerInfoLock)
                {
                    if (side == PipePointSide.A_LowerSide)
                    {
                        Debug.Assert(LayerInfo_A_LowerSide != null);

                        if (uninstallOnDispose)
                            LayerInfo.Uninstall(LayerInfo_A_LowerSide);

                        LayerInfo_A_LowerSide = null;
                    }
                    else
                    {
                        Debug.Assert(LayerInfo_B_UpperSide != null);

                        if (uninstallOnDispose)
                            LayerInfo.Uninstall(LayerInfo_B_UpperSide);

                        LayerInfo_B_UpperSide = null;
                    }
                }
            },
            info);
        }
    }

    protected override async Task CancelImplAsync(Exception? ex)
    {
        try
        {
            if (ex != null)
            {
                ExceptionQueue.Add(ex);
            }

            Func<Task>[] evList;
            lock (OnDisconnected)
                evList = OnDisconnected.ToArray();

            foreach (var ev in evList)
            {
                try
                {
                    await ev();
                }
                catch { }
            }

            StreamAtoB.Disconnect();
            StreamBtoA.Disconnect();

            DatagramAtoB.Disconnect();
            DatagramBtoA.Disconnect();

            OnDisconnectedEvent.Set(true);
        }
        finally
        {
            await base.CancelImplAsync(ex);
        }
    }

    protected override Task CleanupImplAsync(Exception? ex)
        => base.CleanupImplAsync(ex);

    public void CheckDisconnectedAndNoMoreData()
    {
        if (this.StreamAtoB.IsReadyToRead() == false &&
            this.StreamBtoA.IsReadyToRead() == false &&
            this.DatagramAtoB.IsReadyToRead() == false &&
            this.DatagramBtoA.IsReadyToRead() == false)
        {
            CheckDisconnected();
        }
    }

    public void CheckDisconnected()
    {
        StreamAtoB.CheckDisconnected();
        StreamBtoA.CheckDisconnected();
        DatagramAtoB.CheckDisconnected();
        DatagramBtoA.CheckDisconnected();
        this.GrandCancel.ThrowIfCancellationRequested();
    }
}

[Flags]
public enum PipePointSide
{
    A_LowerSide,
    B_UpperSide,
}

[Flags]
public enum AttachDirection
{
    NoAttach,
    A_LowerSide,
    B_UpperSide,
}

public sealed class PipePoint : IAsyncService
{
    public DuplexPipe Pipe { get; }

    public PipePointSide Side { get; }

    public FastStreamBuffer StreamWriter { get; }
    public FastStreamBuffer StreamReader { get; }
    public FastDatagramBuffer DatagramWriter { get; }
    public FastDatagramBuffer DatagramReader { get; }

    public PipePoint? CounterPart { get; private set; }

    public AsyncManualResetEvent OnDisconnectedEvent { get => Pipe.OnDisconnectedEvent; }

    public ExceptionQueue ExceptionQueue { get => Pipe.ExceptionQueue; }
    public LayerInfo LayerInfo { get => Pipe.LayerInfo; }

    public bool IsCanceled { get => this.Pipe.IsCanceled; }
    public void AddOnDisconnected(Func<Task> action)
    {
        lock (Pipe.OnDisconnected)
            Pipe.OnDisconnected.Add(action);
    }

    public static PipePoint NewDuplexPipeAndGetOneSide(PipePointSide createNewPipeAndReturnThisSide, CancellationToken cancel = default, long? thresholdLengthStream = null, long? thresholdLengthDatagram = null)
    {
        var pipe = new DuplexPipe(cancel, thresholdLengthStream, thresholdLengthDatagram);
        return pipe[createNewPipeAndReturnThisSide];
    }

    internal PipePoint(DuplexPipe pipe, PipePointSide side,
        CancelWatcher cancelWatcher,
        FastStreamBuffer streamToWrite, FastStreamBuffer streamToRead,
        FastDatagramBuffer datagramToWrite, FastDatagramBuffer datagramToRead)
    {
        this.Side = side;
        this.Pipe = pipe;
        this.StreamWriter = streamToWrite;
        this.StreamReader = streamToRead;
        this.DatagramWriter = datagramToWrite;
        this.DatagramReader = datagramToRead;
    }

    internal void _InternalSetCounterPart(PipePoint p)
        => this.CounterPart = p;

    readonly internal CriticalSection _InternalAttachHandleLock = new CriticalSection<PipePoint>();
    internal AttachHandle? _InternalCurrentAttachHandle = null;

    public AttachHandle Attach(AttachDirection attachDirection, object? userState = null, bool noCheckDisconnected = false) => new AttachHandle(this, attachDirection, userState, noCheckDisconnected);

    internal PipeStream _InternalGetStream(bool autoFlush = true, bool noCheckDisconnected = false)
        => new PipeStream(this, autoFlush, noCheckDisconnected: noCheckDisconnected);

    public NetAppStub GetNetAppProtocolStub(CancellationToken cancel = default, bool noCheckDisconnected = false)
        => new NetAppStub(this, cancel, noCheckDisconnected: noCheckDisconnected);

    public void CheckCanceled() => Pipe.CheckDisconnected();
    public void CheckCanceledAndNoMoreData() => Pipe.CheckDisconnectedAndNoMoreData();

    public void Disconnect()
    {
        TaskUtil.StartAsyncTaskAsync(DisconnectAsync())._LaissezFaire(true);
    }

    public async Task DisconnectAsync()
    {
        await CancelAsync(new DisconnectedException());
    }

    public Task CancelAsync(Exception? ex = null) { return this.Pipe.CancelAsync(ex); }

    public Task CleanupAsync(Exception? ex = null) => this.Pipe.CleanupAsync(ex);

    public void Dispose() => this.Pipe.Dispose();
    public void Dispose(Exception? ex = null) => this.Pipe.Dispose(ex);
    public Task DisposeWithCleanupAsync(Exception? ex = null) => this.Pipe.DisposeWithCleanupAsync(ex);
    public ValueTask DisposeAsync(Exception? ex) => this.Pipe.DisposeAsync(ex);
    public ValueTask DisposeAsync() => DisposeAsync(null);
}

public class AttachHandle : AsyncService
{
    public PipePoint PipePoint { get; }
    public object? UserState { get; }
    public AttachDirection Direction { get; }

    DuplexPipe.InstalledLayerHolder? InstalledLayerHolder = null;

    IHolder Leak;
    readonly CriticalSection LockObj = new CriticalSection<AttachHandle>();

    public AttachHandle(PipePoint end, AttachDirection attachDirection, object? userState = null, bool noCheckDisconnected = false) : base()
    {
        try
        {
            if (end.Side == PipePointSide.A_LowerSide)
                Direction = AttachDirection.A_LowerSide;
            else
                Direction = AttachDirection.B_UpperSide;

            if (attachDirection != Direction)
                throw new ArgumentException($"attachDirection ({attachDirection}) != {Direction}");

            if (noCheckDisconnected == false)
            {
                end.CheckCanceledAndNoMoreData();
            }

            lock (end._InternalAttachHandleLock)
            {
                if (end._InternalCurrentAttachHandle != null)
                    throw new ApplicationException("The PipePoint is already attached.");

                this.UserState = userState;
                this.PipePoint = end;
                this.PipePoint._InternalCurrentAttachHandle = this;
            }

            Leak = LeakChecker.Enter();
        }
        catch (Exception ex)
        {
            ex._Debug();
            this._DisposeSafe();
            throw;
        }
    }

    public void SetLayerInfo(LayerInfoBase info, NetStackBase protocolStack, bool uninstallOnDetach)
    {
        lock (LockObj)
        {
            if (this.IsCanceled) return;

            if (InstalledLayerHolder != null)
                throw new ApplicationException("LayerInfo is already set.");

            info._InternalSetProtocolStack(protocolStack);

            InstalledLayerHolder = PipePoint.Pipe._InternalInstallLayerInfo(PipePoint.Side, info, uninstallOnDetach);
        }
    }

    int receiveTimeoutProcId = 0;
    TimeoutDetector? receiveTimeoutDetector = null;

    public async Task SetStreamTimeoutAsync(int recvTimeout = Timeout.Infinite, int sendTimeout = Timeout.Infinite)
    {
        await SetStreamReceiveTimeoutAsync(recvTimeout);
        await SetStreamSendTimeoutAsync(sendTimeout);
    }

    readonly AsyncLock AsyncLock = new AsyncLock();

    public async Task SetStreamReceiveTimeoutAsync(int timeout = Timeout.Infinite)
    {
        if (Direction == AttachDirection.A_LowerSide)
            throw new ApplicationException("The attachment direction is From_Lower_To_A_LowerSide.");

        using (await AsyncLock.LockWithAwait())
        {
            if (timeout < 0 || timeout == int.MaxValue)
            {
                if (receiveTimeoutProcId != 0)
                {
                    PipePoint.StreamReader.EventListeners.UnregisterCallback(receiveTimeoutProcId);
                    receiveTimeoutProcId = 0;
                    await receiveTimeoutDetector._DisposeSafeAsync();
                }
            }
            else
            {
                CheckNotCanceled();

                if (receiveTimeoutProcId != 0)
                {
                    PipePoint.StreamReader.EventListeners.UnregisterCallback(receiveTimeoutProcId);
                    receiveTimeoutProcId = 0;
                    await receiveTimeoutDetector._DisposeSafeAsync();
                }

                receiveTimeoutDetector = new TimeoutDetector(timeout, callback: async (x) =>
                {
                    if (PipePoint.StreamReader.IsReadyToWrite() == false)
                        return true;

                        // パイプを切断する。念のためデッドロック防止のため非同期とする。
                        TaskUtil.StartAsyncTaskAsync(async () => await PipePoint.Pipe.CancelAsync(new TimeoutException("StreamReceiveTimeout")))._LaissezFaire();

                    await Task.CompletedTask;

                    return false;
                });

                receiveTimeoutProcId = PipePoint.StreamReader.EventListeners.RegisterCallback((buffer, type, state, eventState) =>
                {
                    if (type == FastBufferCallbackEventType.Written || type == FastBufferCallbackEventType.NonEmptyToEmpty)
                        receiveTimeoutDetector.Keep();
                });
            }
        }
    }

    int sendTimeoutProcId = 0;
    TimeoutDetector? sendTimeoutDetector = null;

    public async Task SetStreamSendTimeoutAsync(int timeout = Timeout.Infinite)
    {
        if (Direction == AttachDirection.A_LowerSide)
            throw new ApplicationException("The attachment direction is From_Lower_To_A_LowerSide.");
        using (await AsyncLock.LockWithAwait())
        {
            if (timeout < 0 || timeout == int.MaxValue)
            {
                if (sendTimeoutProcId != 0)
                {
                    PipePoint.StreamWriter.EventListeners.UnregisterCallback(sendTimeoutProcId);
                    sendTimeoutProcId = 0;
                    await sendTimeoutDetector._DisposeSafeAsync();
                }
            }
            else
            {
                CheckNotCanceled();

                if (sendTimeoutProcId != 0)
                {
                    PipePoint.StreamWriter.EventListeners.UnregisterCallback(sendTimeoutProcId);
                    sendTimeoutProcId = 0;
                    await sendTimeoutDetector._DisposeSafeAsync();
                }

                sendTimeoutDetector = new TimeoutDetector(timeout, callback: async (x) =>
                {
                    if (PipePoint.StreamWriter.IsReadyToRead() == false)
                        return true;

                        // パイプを切断する。念のためデッドロック防止のため非同期とする。
                        TaskUtil.StartAsyncTaskAsync(async () => await PipePoint.Pipe.CancelAsync(new TimeoutException("StreamSendTimeout")))._LaissezFaire();

                    await Task.CompletedTask;

                    return false;
                });

                sendTimeoutProcId = PipePoint.StreamWriter.EventListeners.RegisterCallback((buffer, type, state, eventState) =>
                {
                        //                            WriteLine($"{type}  {buffer.Length}  {buffer.IsReadyToWrite}");
                        if (type == FastBufferCallbackEventType.Read || type == FastBufferCallbackEventType.EmptyToNonEmpty)
                        sendTimeoutDetector.Keep();
                });
            }
        }
    }

    public PipeStream GetStream(bool autoFlush = true, bool noCheckDisconnected = false)
        => PipePoint._InternalGetStream(autoFlush, noCheckDisconnected: noCheckDisconnected);

    protected override async Task CancelImplAsync(Exception? ex)
    {
        try
        {
            if (Direction == AttachDirection.B_UpperSide)
            {
                await SetStreamReceiveTimeoutAsync(Timeout.Infinite);
                await SetStreamSendTimeoutAsync(Timeout.Infinite);
            }

            if (PipePoint != null)
            {
                lock (PipePoint._InternalAttachHandleLock)
                {
                    PipePoint._InternalCurrentAttachHandle = null;
                }
            }
        }
        finally
        {
            await base.CancelImplAsync(ex);
        }
    }

    protected override Task CleanupImplAsync(Exception? ex) => base.CleanupImplAsync(ex);

    protected override void DisposeImpl(Exception? ex)
    {
        Leak._DisposeSafe();

        InstalledLayerHolder._DisposeSafe();
    }
}

// test

public class PipeStream : StreamImplBase
{
    public bool AutoFlush { get; set; }
    public PipePoint Point { get; private set; }

    public PipeStream(PipePoint pipePoint, bool autoFlush = true, bool noCheckDisconnected = false)
    {
        if (noCheckDisconnected == false)
        {
            pipePoint.CheckCanceledAndNoMoreData();
        }

        Point = pipePoint;
        AutoFlush = autoFlush;

        ReadTimeout = Timeout.Infinite;
        WriteTimeout = Timeout.Infinite;
    }

    #region Stream
    public bool IsReadyToSend() => Point.StreamReader.IsReadyToWrite();
    public bool IsReadyToReceive(int sizeToRead = 1) => Point.StreamReader.IsReadyToRead(sizeToRead);
    public bool IsReadyToSendTo() => Point.DatagramReader.IsReadyToWrite();
    public bool IsReadyToReceiveFrom() => Point.DatagramReader.IsReadyToRead();
    public bool IsDisconnected => Point.StreamReader.IsDisconnected || Point.DatagramReader.IsDisconnected;

    public void CheckDisconnect()
    {
        Point.StreamReader.CheckDisconnected();
        Point.DatagramReader.CheckDisconnected();
    }

    public Task WaitReadyToSendAsync(CancellationToken cancel, int timeout, bool noTimeoutException = false, AsyncAutoResetEvent? cancelEvent = null)
    {
        cancel.ThrowIfCancellationRequested();

        if (Point.StreamWriter.IsReadyToWrite()) return Task.CompletedTask;

        return Point.StreamWriter.WaitForReadyToWriteAsync(cancel, timeout, noTimeoutException, cancelEvent);
    }

    public Task WaitReadyToReceiveAsync(CancellationToken cancel, int timeout, int sizeToRead = 1, bool noTimeoutException = false, AsyncAutoResetEvent? cancelEvent = null)
    {
        cancel.ThrowIfCancellationRequested();

        if (Point.StreamReader.IsReadyToRead(sizeToRead)) return Task.CompletedTask;

        return Point.StreamReader.WaitForReadyToReadAsync(cancel, timeout, sizeToRead, noTimeoutException, cancelEvent);
    }

    public void FastSendNonBlock(Memory<ReadOnlyMemory<byte>> items, bool flush = true)
    {
        Point.StreamWriter.EnqueueAllWithLock(items.Span);

        if (flush) FastFlush(true, false, checkDisconnect: false);
    }

    public async Task FastSendAsync(Memory<ReadOnlyMemory<byte>> items, CancellationToken cancel = default, bool flush = true)
    {
        await WaitReadyToSendAsync(cancel, WriteTimeout);

        Point.StreamWriter.EnqueueAllWithLock(items.Span);

        if (flush) FastFlush(true, false, checkDisconnect: false);
    }

    public void FastSendNonBlock(Memory<byte> item, bool flush = true)
    {
        lock (Point.StreamWriter.LockObj)
        {
            Point.StreamWriter.Enqueue(item);
        }

        if (flush) FastFlush(true, false, checkDisconnect: false);
    }

    public async Task FastSendAsync(Memory<byte> item, CancellationToken cancel = default, bool flush = true)
    {
        await WaitReadyToSendAsync(cancel, WriteTimeout);

        lock (Point.StreamWriter.LockObj)
        {
            Point.StreamWriter.Enqueue(item);
        }

        if (flush) FastFlush(true, false, checkDisconnect: false);
    }

    public async Task SendAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancel = default)
    {
        Memory<byte> sendData = buffer.ToArray();

        await FastSendAsync(sendData, cancel);

        if (AutoFlush) FastFlush(true, false, checkDisconnect: false);
    }

    public void Send(ReadOnlyMemory<byte> buffer, CancellationToken cancel = default)
        => SendAsync(buffer, cancel)._GetResult();

    Once receiveAllAsyncRaiseExceptionFlag;

    public async Task ReceiveAllAsync(Memory<byte> buffer, CancellationToken cancel = default)
    {
        while (buffer.Length >= 1)
        {
            int r = await ReceiveAsync(buffer, cancel);
            if (r <= 0)
            {
                Point.StreamReader.CheckDisconnected();

                if (receiveAllAsyncRaiseExceptionFlag.IsFirstCall())
                {
                    Point.StreamReader.ExceptionQueue.Raise(new FastBufferDisconnectedException());
                }
                else
                {
                    Point.StreamReader.ExceptionQueue.ThrowFirstExceptionIfExists();
                    throw new FastBufferDisconnectedException();
                }
            }
            buffer._Walk(r);
        }
    }

    public async Task<Memory<byte>> ReceiveAllAsync(int size, CancellationToken cancel = default)
    {
        Memory<byte> buffer = MemoryHelper.FastAllocMemory<byte>(size);
        await ReceiveAllAsync(buffer, cancel);
        return buffer;
    }

    public async Task<int> ReceiveAsync(Memory<byte> buffer, CancellationToken cancel = default)
    {
        try
        {
            LABEL_RETRY:
            await WaitReadyToReceiveAsync(cancel, ReadTimeout, 1);

            int ret = 0;

            ret = Point.StreamReader.DequeueContiguousSlowWithLock(buffer);

            if (ret == 0)
            {
                await Task.Yield();
                goto LABEL_RETRY;
            }

            Debug.Assert(ret <= buffer.Length);

            Point.StreamReader.CompleteRead();

            return ret;
        }
        catch (DisconnectedException)
        {
            return 0;
        }
    }

    public async Task<ReadOnlyMemory<byte>> ReceiveAsync(int maxSize = int.MaxValue, CancellationToken cancel = default)
    {
        try
        {
            LABEL_RETRY:
            await WaitReadyToReceiveAsync(cancel, ReadTimeout, 1);

            ReadOnlyMemory<byte> ret;

            lock (Point.StreamReader.LockObj)
                ret = Point.StreamReader.DequeueContiguousSlow(maxSize);

            if (ret.Length == 0)
            {
                await Task.Yield();
                goto LABEL_RETRY;
            }

            Point.StreamReader.CompleteRead();

            return ret;
        }
        catch (DisconnectedException)
        {
            return Memory<byte>.Empty;
        }
    }

    public void ReceiveAll(Memory<byte> buffer, CancellationToken cancel = default)
        => ReceiveAllAsync(buffer, cancel)._GetResult();

    public Memory<byte> ReceiveAll(int size, CancellationToken cancel = default)
        => ReceiveAllAsync(size, cancel)._GetResult();

    public int Receive(Memory<byte> buffer, CancellationToken cancel = default)
        => ReceiveAsync(buffer, cancel)._GetResult();

    public ReadOnlyMemory<byte> Receive(int maxSize = int.MaxValue, CancellationToken cancel = default)
        => ReceiveAsync(maxSize, cancel)._GetResult();

    public IReadOnlyList<ReadOnlyMemory<byte>> FastReceiveNonBlock(out int totalRecvSize, int maxSize = int.MaxValue)
    {
        totalRecvSize = 0;

        try
        {
            IReadOnlyList<ReadOnlyMemory<byte>> ret = Point.StreamReader.DequeueWithLock(maxSize, out long totalReadSize);

            totalRecvSize = (int)totalReadSize;

            if (totalRecvSize >= 1)
            {
                Point.StreamReader.CompleteRead();
            }

            return ret;
        }
        catch (DisconnectedException)
        {
            return new List<ReadOnlyMemory<byte>>();
        }
    }

    public async Task<IReadOnlyList<ReadOnlyMemory<byte>>> FastReceiveAsync(CancellationToken cancel = default, RefInt? totalRecvSize = null, int maxSize = int.MaxValue)
    {
        try
        {
            LABEL_RETRY:
            await WaitReadyToReceiveAsync(cancel, ReadTimeout, 1);

            IReadOnlyList<ReadOnlyMemory<byte>> ret = Point.StreamReader.DequeueWithLock(maxSize, out long totalReadSize);

            if (totalRecvSize != null)
                totalRecvSize.Set((int)totalReadSize);

            if (totalReadSize == 0)
            {
                await Task.Yield();
                goto LABEL_RETRY;
            }

            Point.StreamReader.CompleteRead();

            return ret;
        }
        catch (DisconnectedException)
        {
            return new List<ReadOnlyMemory<byte>>();
        }
    }

    public async Task<IReadOnlyList<ReadOnlyMemory<byte>>> FastPeekAsync(int maxSize = int.MaxValue, CancellationToken cancel = default, RefInt? totalRecvSize = null, bool all = false)
    {
        LABEL_RETRY:
        CheckDisconnect();
        await WaitReadyToReceiveAsync(cancel, ReadTimeout, all ? maxSize : 1);
        CheckDisconnect();

        long totalReadSize;
        FastBufferSegment<ReadOnlyMemory<byte>>[] tmp;
        lock (Point.StreamReader.LockObj)
        {
            tmp = Point.StreamReader.GetSegmentsFast(Point.StreamReader.PinHead, maxSize, out totalReadSize, !all);
        }

        if (totalRecvSize != null)
            totalRecvSize.Set((int)totalReadSize);

        if (totalReadSize == 0)
        {
            await Task.Yield();
            goto LABEL_RETRY;
        }

        List<ReadOnlyMemory<byte>> ret = new List<ReadOnlyMemory<byte>>();
        foreach (FastBufferSegment<ReadOnlyMemory<byte>> item in tmp)
            ret.Add(item.Item);

        return ret;
    }

    public async Task<ReadOnlyMemory<byte>> FastPeekContiguousAsync(int maxSize = int.MaxValue, CancellationToken cancel = default, bool all = false)
    {
        LABEL_RETRY:
        CheckDisconnect();
        await WaitReadyToReceiveAsync(cancel, ReadTimeout, all ? maxSize : 1);
        CheckDisconnect();

        ReadOnlyMemory<byte> ret;

        lock (Point.StreamReader.LockObj)
        {
            ret = Point.StreamReader.GetContiguous(Point.StreamReader.PinHead, maxSize, !all);
        }

        if (ret.Length == 0)
        {
            await Task.Yield();
            goto LABEL_RETRY;
        }

        return ret;
    }

    public async Task<Memory<byte>> PeekAsync(int maxSize = int.MaxValue, CancellationToken cancel = default, bool all = false)
        => (await FastPeekContiguousAsync(maxSize, cancel, all)).ToArray();

    public async Task<int> PeekAsync(Memory<byte> buffer, CancellationToken cancel = default, bool all = false)
    {
        var tmp = await PeekAsync(buffer.Length, cancel, all);
        tmp.CopyTo(buffer);
        return tmp.Length;
    }

    #endregion

    #region Datagram
    public Task WaitReadyToSendToAsync(CancellationToken cancel, int timeout, bool noTimeoutException = false, AsyncAutoResetEvent? cancelEvent = null)
    {
        cancel.ThrowIfCancellationRequested();

        if (Point.DatagramWriter.IsReadyToWrite()) return Task.CompletedTask;

        return Point.DatagramWriter.WaitForReadyToWriteAsync(cancel, timeout, noTimeoutException, cancelEvent);
    }

    public Task WaitReadyToReceiveFromAsync(CancellationToken cancel, int timeout, int sizeToRead = 1, bool noTimeoutException = false, AsyncAutoResetEvent? cancelEvent = null)
    {
        cancel.ThrowIfCancellationRequested();

        if (Point.DatagramReader.IsReadyToRead(sizeToRead)) return Task.CompletedTask;

        return Point.DatagramReader.WaitForReadyToReadAsync(cancel, timeout, sizeToRead, noTimeoutException, cancelEvent);
    }

    public async Task FastSendToAsync(Memory<Datagram> items, CancellationToken cancel = default, bool flush = true)
    {
        await WaitReadyToSendToAsync(cancel, WriteTimeout);

        Point.DatagramWriter.EnqueueAllWithLock(items.Span);

        if (flush) FastFlush(false, true, checkDisconnect: false);
    }

    public async Task FastSendToAsync(Datagram item, CancellationToken cancel = default, bool flush = true)
    {
        await WaitReadyToSendToAsync(cancel, WriteTimeout);

        lock (Point.StreamWriter.LockObj)
        {
            Point.DatagramWriter.Enqueue(item);
        }

        if (flush) FastFlush(false, true, checkDisconnect: false);
    }

    public async Task SendToAsync(ReadOnlyMemory<byte> buffer, EndPoint remoteEndPoint, EndPoint? localEndPoint, DatagramFlag flag = DatagramFlag.None, CancellationToken cancel = default)
    {
        Datagram sendData = new Datagram(buffer.Span.ToArray(), remoteEndPoint, localEndPoint, flag);

        await FastSendToAsync(sendData, cancel);

        if (AutoFlush) FastFlush(false, true, checkDisconnect: false);
    }

    public void SendTo(ReadOnlyMemory<byte> buffer, EndPoint remoteEndPoint, EndPoint? localEndPoint, DatagramFlag flag = DatagramFlag.None, CancellationToken cancel = default)
        => SendToAsync(buffer, remoteEndPoint, localEndPoint, flag, cancel)._GetResult();

    public async Task<IReadOnlyList<Datagram>> FastReceiveFromAsync(CancellationToken cancel = default)
    {
        LABEL_RETRY:
        await WaitReadyToReceiveFromAsync(cancel, ReadTimeout, 1);

        var ret = Point.DatagramReader.DequeueAllWithLock(out long totalReadSize);
        if (totalReadSize == 0)
        {
            await Task.Yield();
            goto LABEL_RETRY;
        }

        Point.DatagramReader.CompleteRead();

        return ret;
    }

    public async Task<Datagram> ReceiveFromAsync(CancellationToken cancel = default)
    {
        LABEL_RETRY:
        await WaitReadyToReceiveFromAsync(cancel, ReadTimeout, 1);

        IReadOnlyList<Datagram> dataList;

        long totalReadSize;

        dataList = Point.DatagramReader.DequeueWithLock(1, out totalReadSize);

        if (totalReadSize == 0)
        {
            await Task.Yield();
            goto LABEL_RETRY;
        }

        Debug.Assert(dataList.Count == 1);

        Point.DatagramReader.CompleteRead();

        return dataList[0];
    }

    public async Task<PalSocketReceiveFromResult> ReceiveFromAsync(Memory<byte> buffer, CancellationToken cancel = default)
    {
        var datagram = await ReceiveFromAsync(cancel);
        datagram.Data.CopyTo(buffer);

        PalSocketReceiveFromResult ret = new PalSocketReceiveFromResult();
        ret.ReceivedBytes = datagram.Data.Length;
        ret.RemoteEndPoint = datagram.RemoteEndPoint!;
        ret.LocalEndPoint = datagram.LocalEndPoint;
        return ret;
    }

    public int ReceiveFrom(Memory<byte> buffer, out EndPoint remoteEndPoint, out EndPoint? localEndPoint, CancellationToken cancel = default)
    {
        PalSocketReceiveFromResult r = ReceiveFromAsync(buffer, cancel)._GetResult();

        remoteEndPoint = r.RemoteEndPoint!;
        localEndPoint = r.LocalEndPoint;

        return r.ReceivedBytes;
    }

    #endregion

    public void FastFlush(bool stream = true, bool datagram = true, bool checkDisconnect = true)
    {
        if (stream)
            Point.StreamWriter.CompleteWrite(checkDisconnect: checkDisconnect);

        if (datagram)
            Point.DatagramWriter.CompleteWrite(checkDisconnect: checkDisconnect);
    }

    public void Disconnect()
    {
        TaskUtil.StartAsyncTaskAsync(this.DisconnectAsync())._LaissezFaire(true);
    }

    public async Task DisconnectAsync()
    {
        await Point.CancelAsync(new DisconnectedException());
    }

    public override bool DataAvailable => IsReadyToReceive(1);

    protected override Task FlushImplAsync(CancellationToken cancellationToken)
    {
        FastFlush(true, true, checkDisconnect: false);
        return Task.CompletedTask;
    }

    protected override async ValueTask<int> ReadImplAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        => await ReceiveAsync(buffer, cancellationToken);

    protected override async ValueTask WriteImplAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        => await this.SendAsync(buffer, cancellationToken);

    Once DisposeFlag;
    protected override void Dispose(bool disposing)
    {
        try
        {
            if (!disposing || DisposeFlag.IsFirstCall() == false) return;
            Disconnect();
        }
        finally { base.Dispose(disposing); }
    }

    protected override long GetLengthImpl() => throw new NotImplementedException();
    protected override void SetLengthImpl(long length) => throw new NotImplementedException();
    protected override long GetPositionImpl() => throw new NotImplementedException();
    protected override void SetPositionImpl(long position) => throw new NotImplementedException();
    protected override long SeekImpl(long offset, SeekOrigin origin) => throw new NotImplementedException();
}

public class FastNonBlockStateHelper
{
    byte[] LastState = new byte[0];

    public FastNonBlockStateHelper() { }
    public FastNonBlockStateHelper(IFastBufferState reader, IFastBufferState writer, CancellationToken cancel = default) : this()
    {
        AddWatchReader(reader);
        AddWatchWriter(writer);
        AddCancel(cancel);
    }

    List<IFastBufferState> ReaderList = new List<IFastBufferState>();
    List<IFastBufferState> WriterList = new List<IFastBufferState>();
    List<AsyncAutoResetEvent> WaitEventList = new List<AsyncAutoResetEvent>();
    List<CancellationToken> WaitCancelList = new List<CancellationToken>();

    public void AddWatchReader(IFastBufferState obj)
    {
        if (ReaderList.Contains(obj) == false)
            ReaderList.Add(obj);
        AddEvent(obj.EventReadReady);
    }

    public void AddWatchWriter(IFastBufferState obj)
    {
        if (WriterList.Contains(obj) == false)
            WriterList.Add(obj);
        AddEvent(obj.EventWriteReady);
    }

    public void AddEvent(AsyncAutoResetEvent? ev) => AddEvents(new AsyncAutoResetEvent?[] { ev });
    public void AddEvents(params AsyncAutoResetEvent?[] events)
    {
        foreach (var ev in events)
            if (ev != null)
                if (WaitEventList.Contains(ev) == false)
                    WaitEventList.Add(ev);
    }

    public void AddCancel(CancellationToken c) => AddCancels(new CancellationToken[] { c });
    public void AddCancels(params CancellationToken[] cancels)
    {
        foreach (var cancel in cancels)
            if (cancel.CanBeCanceled)
                if (WaitCancelList.Contains(cancel) == false)
                    WaitCancelList.Add(cancel);
    }

    public byte[] SnapshotState(long salt = 0)
    {
        SpanBuffer<byte> ret = new SpanBuffer<byte>();
        ret.WriteSInt64(salt);
        foreach (var s in ReaderList)
        {
            lock (s.LockObj)
            {
                ret.WriteUInt8((byte)(s.IsReadyToRead() ? 1 : 0));
                ret.WriteSInt64(s.PinTail);
            }
        }
        foreach (var s in WriterList)
        {
            lock (s.LockObj)
            {
                ret.WriteUInt8((byte)(s.IsReadyToWrite() ? 1 : 0));
                ret.WriteSInt64(s.PinHead);
            }
        }
        return ret.Span.ToArray();
    }

    public bool IsStateChanged(int salt = 0)
    {
        byte[] newState = SnapshotState(salt);
        if (LastState.SequenceEqual(newState))
            return false;
        LastState = newState;
        return true;
    }

    static readonly int PollingTimeout = CoresConfig.PipeConfig.PollingTimeout;

    public async Task<bool> WaitUntilNextStateChangeAsync(int timeout = Timeout.Infinite, int salt = 0, CancellationToken cancel = default)
    {
        timeout = TaskUtil.GetMinTimeout(timeout, PollingTimeout);
        if (timeout == 0) return false;
        if (IsStateChanged(salt)) return false;

        CancellationToken[] cancels = cancel.CanBeCanceled ? WaitCancelList.Append(cancel).ToArray() : WaitCancelList.ToArray();

        await TaskUtil.WaitObjectsAsync(
            cancels: cancels,
            events: WaitEventList.ToArray(),
            timeout: timeout);

        return true;
    }
}


[Flags]
public enum PipeSupportedDataTypes
{
    Stream = 1,
    Datagram = 2,
}

public abstract class PipePointAsyncObjectWrapperBase : AsyncServiceWithMainLoop
{
    public PipePoint PipePoint { get; }
    public abstract PipeSupportedDataTypes SupportedDataTypes { get; }

    public ExceptionQueue ExceptionQueue { get => PipePoint.ExceptionQueue; }
    public LayerInfo LayerInfo { get => PipePoint.LayerInfo; }

    public PipePointAsyncObjectWrapperBase(PipePoint pipePoint, CancellationToken cancel = default) : base(cancel)
    {
        PipePoint = AddIndirectDisposeLink(pipePoint);
    }

    protected Task StartBaseAsyncLoops()
    {
        return StartMainLoop(MainLoopsAsync);
    }

    async Task MainLoopsAsync(CancellationToken cancel)
    {
        await Task.Yield();

        try
        {
            List<Task> tasks = new List<Task>();

            if (SupportedDataTypes.Bit(PipeSupportedDataTypes.Stream))
            {
                tasks.Add(StreamReadFromPipeLoopAsync());
                tasks.Add(StreamWriteToPipeLoopAsync());
            }

            if (SupportedDataTypes.Bit(PipeSupportedDataTypes.Datagram))
            {
                tasks.Add(DatagramReadFromPipeLoopAsync());
                tasks.Add(DatagramWriteToPipeLoopAsync());
            }

            await Task.WhenAll(tasks.ToArray());
        }
        catch (Exception ex)
        {
            await this.CancelAsync(ex);
        }
        finally
        {
            await this.CancelAsync();
        }
    }

    protected abstract Task StreamWriteToObjectImplAsync(FastStreamBuffer fifo, CancellationToken cancel);
    protected abstract Task<bool> StreamReadFromObjectImplAsync(FastStreamBuffer fifo, CancellationToken cancel);

    protected abstract Task DatagramWriteToObjectImplAsync(FastDatagramBuffer fifo, CancellationToken cancel);
    protected abstract Task DatagramReadFromObjectImplAsync(FastDatagramBuffer fifo, CancellationToken cancel);

    static readonly int PollingTimeout = CoresConfig.PipeConfig.PollingTimeout;

    async Task StreamReadFromPipeLoopAsync()
    {
        using (LeakChecker.Enter())
        {
            try
            {
                var reader = PipePoint.StreamReader;
                while (CancelWatcher.CancelToken.IsCancellationRequested == false)
                {
                    bool stateChanged;
                    bool isDisconnectedNow = false;
                    do
                    {
                        stateChanged = false;

                        if (CancelWatcher.CancelToken.IsCancellationRequested)
                        {
                            break;
                        }

                        while (reader.IsReadyToRead())
                        {
                            if (reader.IsDisconnected)
                            {
                                isDisconnectedNow = true;
                                break;
                            }

                            if (CancelWatcher.CancelToken.IsCancellationRequested)
                            {
                                break;
                            }

                            await StreamWriteToObjectImplAsync(reader, CancelWatcher.CancelToken);
                            stateChanged = true;
                        }

                        if (isDisconnectedNow)
                        {
                            break;
                        }
                    }
                    while (stateChanged);

                    if (CancelWatcher.CancelToken.IsCancellationRequested)
                    {
                        break;
                    }

                    if (isDisconnectedNow)
                    {
                        await this.CancelAsync(new DisconnectedException());
                        break;
                    }

                    await TaskUtil.WaitObjectsAsync(
                        events: new AsyncAutoResetEvent?[] { reader.EventReadReady },
                        cancels: new CancellationToken[] { CancelWatcher.CancelToken },
                        timeout: PollingTimeout
                        );
                }
            }
            catch (Exception ex)
            {
                ExceptionQueue.Raise(ex);
                await this.CancelAsync(ex);
            }
            finally
            {
                await this.CancelAsync();
            }
        }
    }

    async Task StreamWriteToPipeLoopAsync()
    {
        using (LeakChecker.Enter())
        {
            try
            {
                var writer = PipePoint.StreamWriter;
                while (CancelWatcher.CancelToken.IsCancellationRequested == false)
                {
                    bool stateChanged;
                    bool isDisconnectedNow = false;
                    do
                    {
                        stateChanged = false;

                        if (CancelWatcher.CancelToken.IsCancellationRequested)
                        {
                            break;
                        }

                        if (writer.IsReadyToWrite())
                        {
                            long lastTail = writer.PinTail;
                            if (await StreamReadFromObjectImplAsync(writer, CancelWatcher.CancelToken) == false)
                            {
                                isDisconnectedNow = true;
                                break;
                            }

                            if (writer.PinTail != lastTail)
                            {
                                stateChanged = true;
                            }
                        }

                    }
                    while (stateChanged);

                    if (CancelWatcher.CancelToken.IsCancellationRequested)
                    {
                        break;
                    }

                    if (isDisconnectedNow)
                    {
                        await this.CancelAsync(new DisconnectedException());
                        break;
                    }

                    await TaskUtil.WaitObjectsAsync(
                        events: new AsyncAutoResetEvent?[] { writer.EventWriteReady },
                        cancels: new CancellationToken[] { CancelWatcher.CancelToken },
                        timeout: PollingTimeout
                        );
                }
            }
            catch (Exception ex)
            {
                ExceptionQueue.Raise(ex);
                await this.CancelAsync(ex);
            }
            finally
            {
                await this.CancelAsync();
            }
        }
    }

    async Task DatagramReadFromPipeLoopAsync()
    {
        using (LeakChecker.Enter())
        {
            try
            {
                var reader = PipePoint.DatagramReader;
                while (true)
                {
                    bool stateChanged;
                    do
                    {
                        stateChanged = false;

                        CancelWatcher.CancelToken.ThrowIfCancellationRequested();

                        while (reader.IsReadyToRead())
                        {
                            await DatagramWriteToObjectImplAsync(reader, CancelWatcher.CancelToken);
                            stateChanged = true;
                        }
                    }
                    while (stateChanged);

                    await TaskUtil.WaitObjectsAsync(
                        events: new AsyncAutoResetEvent?[] { reader.EventReadReady },
                        cancels: new CancellationToken[] { CancelWatcher.CancelToken },
                        timeout: PollingTimeout
                        );
                }
            }
            catch (Exception ex)
            {
                ExceptionQueue.Raise(ex);
                await this.CancelAsync(ex);
            }
            finally
            {
                await this.CancelAsync();
            }
        }
    }

    async Task DatagramWriteToPipeLoopAsync()
    {
        using (LeakChecker.Enter())
        {
            try
            {
                var writer = PipePoint.DatagramWriter;
                while (true)
                {
                    bool stateChanged;
                    do
                    {
                        stateChanged = false;

                        CancelWatcher.CancelToken.ThrowIfCancellationRequested();

                        if (writer.IsReadyToWrite())
                        {
                            long lastTail = writer.PinTail;
                            await DatagramReadFromObjectImplAsync(writer, CancelWatcher.CancelToken);
                            if (writer.PinTail != lastTail)
                            {
                                stateChanged = true;
                            }
                        }

                    }
                    while (stateChanged);

                    await TaskUtil.WaitObjectsAsync(
                        events: new AsyncAutoResetEvent?[] { writer.EventWriteReady },
                        cancels: new CancellationToken[] { CancelWatcher.CancelToken },
                        timeout: PollingTimeout
                        );
                }
            }
            catch (Exception ex)
            {
                ExceptionQueue.Raise(ex);
                await this.CancelAsync(ex);
            }
            finally
            {
                await this.CancelAsync();
            }
        }
    }
}

public class PipePointSocketWrapper : PipePointAsyncObjectWrapperBase
{
    public PalSocket Socket { get; }
    public int RecvTmpBufferSize { get; private set; }
    public override PipeSupportedDataTypes SupportedDataTypes { get; }

    public PipePointSocketWrapper(PipePoint pipePoint, PalSocket socket, CancellationToken cancel = default) : base(pipePoint, cancel)
    {
        this.Socket = socket;
        SupportedDataTypes = (Socket.SocketType == SocketType.Stream) ? PipeSupportedDataTypes.Stream : PipeSupportedDataTypes.Datagram;
        if (Socket.SocketType == SocketType.Stream)
        {
            Socket.LingerTime.Value = 0;
            Socket.NoDelay.Value = true;
        }

        this.StartBaseAsyncLoops();
    }

    protected override async Task StreamWriteToObjectImplAsync(FastStreamBuffer fifo, CancellationToken cancel)
    {
        if (SupportedDataTypes.Bit(PipeSupportedDataTypes.Stream) == false) throw new NotSupportedException();

        IReadOnlyList<ReadOnlyMemory<byte>> sendArray = fifo.DequeueAllWithLock(out long totalReadSize);
        fifo.CompleteRead();

        await TaskUtil.DoAsyncWithTimeout(
            async c =>
            {
                int ret = await Socket.SendAsync(sendArray);
                return 0;
            },
            cancel: cancel);
    }

    FastMemoryPool<byte> FastMemoryAllocatorForStream = new FastMemoryPool<byte>();

    static readonly int MaxStreamBufferLength = Math.Min(CoresConfig.PipeConfig.MaxStreamBufferLength, CoresConfig.BufferSizes.MaxNetworkStreamSendRecvBufferSize);

    AsyncBulkReceiver<ReadOnlyMemory<byte>, PipePointSocketWrapper> StreamBulkReceiver = new AsyncBulkReceiver<ReadOnlyMemory<byte>, PipePointSocketWrapper>(async (me, cancel) =>
    {
        if (me.RecvTmpBufferSize == 0)
        {
            int i = me.Socket.ReceiveBufferSize;
            if (i <= 0) i = CoresConfig.BufferSizes.MaxNetworkStreamSendRecvBufferSize;
            me.RecvTmpBufferSize = Math.Min(i, MaxStreamBufferLength);
        }

        Memory<byte> tmp = me.FastMemoryAllocatorForStream.Reserve(me.RecvTmpBufferSize);
        int r = await me.Socket.ReceiveAsync(tmp);
        if (r < 0) throw new SocketDisconnectedException();
        me.FastMemoryAllocatorForStream.Commit(ref tmp, r);
        if (r == 0) return new ValueOrClosed<ReadOnlyMemory<byte>>();
        return new ValueOrClosed<ReadOnlyMemory<byte>>(tmp);
    });

    protected override async Task<bool> StreamReadFromObjectImplAsync(FastStreamBuffer fifo, CancellationToken cancel)
    {
        if (SupportedDataTypes.Bit(PipeSupportedDataTypes.Stream) == false) throw new NotSupportedException();

        ReadOnlyMemory<byte>[]? recvList = await StreamBulkReceiver.RecvAsync(cancel, this);

        if (recvList == null)
        {
            // disconnected
            fifo.Disconnect();
            return false;
        }

        fifo.EnqueueAllWithLock(recvList);

        fifo.CompleteWrite();

        return true;
    }

    protected override async Task DatagramWriteToObjectImplAsync(FastDatagramBuffer fifo, CancellationToken cancel)
    {
        if (SupportedDataTypes.Bit(PipeSupportedDataTypes.Datagram) == false) throw new NotSupportedException();

        IReadOnlyList<Datagram> sendList;

        sendList = fifo.DequeueAllWithLock(out _);
        fifo.CompleteRead();

        await TaskUtil.DoAsyncWithTimeout(
            async c =>
            {
                foreach (Datagram data in sendList)
                {
                    cancel.ThrowIfCancellationRequested();
                    await Socket.SendToAsync(data.Data._AsSegment(), data.RemoteEndPoint!);
                }
                return 0;
            },
            cancel: cancel);
    }

    FastMemoryPool<byte> FastMemoryAllocatorForDatagram = new FastMemoryPool<byte>();

    AsyncBulkReceiver<Datagram, PipePointSocketWrapper> DatagramBulkReceiver = new AsyncBulkReceiver<Datagram, PipePointSocketWrapper>(async (me, cancel) =>
    {
        Memory<byte> tmp = me.FastMemoryAllocatorForDatagram.Reserve(65536);

        var ret = await me.Socket.ReceiveFromAsync(tmp);

        me.FastMemoryAllocatorForDatagram.Commit(ref tmp, ret.ReceivedBytes);

        Datagram pkt = new Datagram(tmp, ret.RemoteEndPoint, ret.LocalEndPoint);
        return new ValueOrClosed<Datagram>(pkt);
    });

    protected override async Task DatagramReadFromObjectImplAsync(FastDatagramBuffer fifo, CancellationToken cancel)
    {
        if (SupportedDataTypes.Bit(PipeSupportedDataTypes.Datagram) == false) throw new NotSupportedException();

        Datagram[]? pkts = await DatagramBulkReceiver.RecvAsync(cancel, this);

        if (pkts == null)
        {
            // disconnected
            fifo.Disconnect();
            return;
        }

        fifo.EnqueueAllWithLock(pkts);

        fifo.CompleteWrite();
    }

    protected override async Task CancelImplAsync(Exception? ex)
    {
        Socket._DisposeSafe();

        await base.CancelImplAsync(ex);
    }

    protected override void DisposeImpl(Exception? ex)
    {
        Socket._DisposeSafe();

        base.DisposeImpl(ex);
    }
}

public class PipePointStreamWrapper : PipePointAsyncObjectWrapperBase
{
    public Stream ReadStream { get; }
    public Stream WriteStream { get; }
    public int RecvTmpBufferSize { get; private set; }
    public static readonly int SendTmpBufferSize = CoresConfig.BufferSizes.MaxNetworkStreamSendRecvBufferSize;
    public override PipeSupportedDataTypes SupportedDataTypes { get; }

    public PipePointStreamWrapper(PipePoint pipePoint, Stream readWriteStream, CancellationToken cancel = default) : this(pipePoint, readWriteStream, readWriteStream, cancel) { }
    public PipePointStreamWrapper(PipePoint pipePoint, Stream readStream, Stream writeStream, CancellationToken cancel = default) : base(pipePoint, cancel)
    {
        this.ReadStream = readStream;
        this.WriteStream = writeStream;

        SupportedDataTypes = PipeSupportedDataTypes.Stream;

        if (ReadStream.CanTimeout) ReadStream.ReadTimeout = Timeout.Infinite;
        if (WriteStream.CanTimeout) WriteStream.WriteTimeout = Timeout.Infinite;

        this.StartBaseAsyncLoops();
    }

    protected override async Task StreamWriteToObjectImplAsync(FastStreamBuffer fifo, CancellationToken cancel)
    {
        if (SupportedDataTypes.Bit(PipeSupportedDataTypes.Stream) == false) throw new NotSupportedException();

        await TaskUtil.DoAsyncWithTimeout(
            async c =>
            {
                bool flush = false;

                using (MemoryHelper.FastAllocMemoryWithUsing(SendTmpBufferSize, out Memory<byte> buffer))
                {
                    while (true)
                    {
                        int size = fifo.DequeueContiguousSlowWithLock(buffer);
                        if (size == 0)
                            break;

                        var sendData = buffer.Slice(0, size);

                        await WriteStream.WriteAsync(sendData, cancel);
                        flush = true;
                    }
                }

                if (flush)
                    await WriteStream.FlushAsync(cancel);

                return 0;
            },
            cancel: cancel);
    }

    FastMemoryPool<byte> FastMemoryAllocatorForStream = new FastMemoryPool<byte>();

    static readonly int MaxStreamBufferLength = CoresConfig.PipeConfig.MaxStreamBufferLength;

    AsyncBulkReceiver<ReadOnlyMemory<byte>, PipePointStreamWrapper> StreamBulkReceiver = new AsyncBulkReceiver<ReadOnlyMemory<byte>, PipePointStreamWrapper>(async (me, cancel) =>
    {
        if (me.RecvTmpBufferSize == 0)
        {
            int i = CoresConfig.BufferSizes.MaxNetworkStreamSendRecvBufferSize;
            me.RecvTmpBufferSize = Math.Min(i, MaxStreamBufferLength);
        }

        Memory<byte> tmp = me.FastMemoryAllocatorForStream.Reserve(me.RecvTmpBufferSize);
        int r = await me.ReadStream.ReadAsync(tmp, cancel);
        if (r < 0) throw new BaseStreamDisconnectedException();
        me.FastMemoryAllocatorForStream.Commit(ref tmp, r);
        if (r == 0) return new ValueOrClosed<ReadOnlyMemory<byte>>();
        return new ValueOrClosed<ReadOnlyMemory<byte>>(tmp);
    });

    protected override async Task<bool> StreamReadFromObjectImplAsync(FastStreamBuffer fifo, CancellationToken cancel)
    {
        if (SupportedDataTypes.Bit(PipeSupportedDataTypes.Stream) == false) throw new NotSupportedException();

        ReadOnlyMemory<byte>[]? recvList = await StreamBulkReceiver.RecvAsync(cancel, this);

        if (recvList == null)
        {
            // disconnected
            fifo.Disconnect();
            return false;
        }

        fifo.EnqueueAllWithLock(recvList);

        fifo.CompleteWrite();

        return true;
    }

    protected override Task DatagramWriteToObjectImplAsync(FastDatagramBuffer fifo, CancellationToken cancel)
        => throw new NotSupportedException();

    protected override Task DatagramReadFromObjectImplAsync(FastDatagramBuffer fifo, CancellationToken cancel)
        => throw new NotSupportedException();


    protected override async Task CancelImplAsync(Exception? ex)
    {
        await ReadStream._DisposeSafeAsync();
        if (WriteStream != ReadStream) await WriteStream._DisposeSafeAsync();

        await base.CancelImplAsync(ex);
    }

    protected override void DisposeImpl(Exception? ex)
    {
        ReadStream._DisposeSafe();
        if (WriteStream != ReadStream) WriteStream._DisposeSafe();

        base.DisposeImpl(ex);
    }
}

public class PipePointDuplexPipeWrapper : PipePointAsyncObjectWrapperBase
{
    public override PipeSupportedDataTypes SupportedDataTypes { get; }

    public static readonly int SendTmpBufferSize = CoresConfig.BufferSizes.MaxNetworkStreamSendRecvBufferSize;
    public int RecvTmpBufferSize { get; private set; }

    readonly PipeWriter PipeToWrite;
    readonly PipeReader PipeToRead;

    public PipePointDuplexPipeWrapper(PipePoint pipePoint, IDuplexPipe duplexPipe, CancellationToken cancel = default) : base(pipePoint, cancel)
    {
        this.SupportedDataTypes = PipeSupportedDataTypes.Stream;

        this.PipeToWrite = duplexPipe.Output;
        this.PipeToRead = duplexPipe.Input;

        this.StartBaseAsyncLoops();
    }

    protected override async Task StreamWriteToObjectImplAsync(FastStreamBuffer fifo, CancellationToken cancel)
    {
        Memory<byte> writeMeBuffer = PipeToWrite.GetMemory();

        int writtenSize = fifo.DequeueContiguousSlowWithLock(writeMeBuffer);

        fifo.CompleteRead();

        PipeToWrite.Advance(writtenSize);

        await PipeToWrite.FlushAsync(cancel);
    }

    protected override async Task<bool> StreamReadFromObjectImplAsync(FastStreamBuffer fifo, CancellationToken cancel)
    {
        if (SupportedDataTypes.Bit(PipeSupportedDataTypes.Stream) == false) throw new NotSupportedException();

        ReadResult r = await this.PipeToRead.ReadAsync();

        if (r.IsCanceled) return false;
        if (r.IsCompleted && r.Buffer.Length == 0) return false;

        int sizeToRead = checked((int)(Math.Min(fifo.SizeWantToBeWritten, r.Buffer.Length)));

        var consumedBuffer = r.Buffer.Slice(0, sizeToRead);

        List<ReadOnlyMemory<byte>> memoryList = new List<ReadOnlyMemory<byte>>();

        foreach (ReadOnlyMemory<byte> memory in consumedBuffer)
        {
            // Copy is needed because AdvanceTo() will return the buffer for recycle
            memoryList.Add(memory._CloneMemory());
        }

        fifo.EnqueueAllWithLock(memoryList.ToArray());

        fifo.CompleteWrite();

        this.PipeToRead.AdvanceTo(consumedBuffer.End);

        return true;
    }

    protected override Task DatagramReadFromObjectImplAsync(FastDatagramBuffer fifo, CancellationToken cancel) => throw new NotImplementedException();
    protected override Task DatagramWriteToObjectImplAsync(FastDatagramBuffer fifo, CancellationToken cancel) => throw new NotImplementedException();

    Once DisconnectFlag;
    async Task InternalDisconnectAsync(Exception? ex)
    {
        if (DisconnectFlag.IsFirstCall())
        {
            try { await PipeToRead.CompleteAsync(); } catch { }
            try { PipeToRead.CancelPendingRead(); } catch { }
            try { await PipeToWrite.CompleteAsync(); } catch { }
            try { PipeToWrite.CancelPendingFlush(); } catch { }
        }
    }

    protected override async Task CancelImplAsync(Exception? ex)
    {
        try
        {
            await InternalDisconnectAsync(ex);
        }
        finally
        {
            await base.CancelImplAsync(ex);
        }
    }

    protected override async Task CleanupImplAsync(Exception? ex)
    {
        try
        {
            await InternalDisconnectAsync(ex);
        }
        finally
        {
            await base.CleanupImplAsync(ex);
        }
    }

    public static void RunStartupTest()
    {
        using MemoryStream ms = new MemoryStream(Str.MakeCharArray('x', 100)._GetBytes());
        PipeReader r = PipeReader.Create(ms);
    }
}

public class StreamImplBaseOptions
{
    public bool CanRead { get; }
    public bool CanWrite { get; }
    public bool CanSeek { get; }

    public StreamImplBaseOptions(bool canRead = true, bool canWrite = true, bool canSeek = false)
    {
        CanRead = canRead;
        CanWrite = canWrite;
        CanSeek = canSeek;
    }
}

public abstract class WrapperStreamImplBase : StreamImplBase
{
    public Stream BaseStream { get; }
    public bool LeaveStreamOpen { get; }

    protected abstract Task InitImplAsync(CancellationToken cancel = default);

    public WrapperStreamImplBase(Stream baseStream, bool leaveStreamOpen, StreamImplBaseOptions? options = null) : base(options)
    {
        this.BaseStream = baseStream;
        this.LeaveStreamOpen = leaveStreamOpen;
    }

    Once Inited;
    public async Task InitAsync(CancellationToken cancel = default)
    {
        if (Inited.IsFirstCall() == false) return;

        await InitImplAsync(cancel);
    }

    Once DisposeFlag;
    public override async ValueTask DisposeAsync()
    {
        try
        {
            if (DisposeFlag.IsFirstCall() == false) return;
            await DisposeInternalAsync();
        }
        finally
        {
            await base.DisposeAsync();
        }
    }
    protected override void Dispose(bool disposing)
    {
        try
        {
            if (!disposing || DisposeFlag.IsFirstCall() == false) return;
            DisposeInternalAsync()._GetResult();
        }
        finally { base.Dispose(disposing); }
    }
    async Task DisposeInternalAsync()
    {
        if (this.LeaveStreamOpen == false)
        {
            await this.BaseStream._DisposeSafeAsync();
        }
    }
}

public abstract class StreamImplBase : Stream
{
    public abstract bool DataAvailable { get; }
    public StreamImplBaseOptions StreamImplOptions { get; }

    public StreamImplBase(StreamImplBaseOptions? options = null)
    {
        StreamImplOptions = options ?? new StreamImplBaseOptions();
    }

    Once DisposeFlag;
    public override async ValueTask DisposeAsync()
    {
        try
        {
            if (DisposeFlag.IsFirstCall() == false) return;
            await DisposeInternalAsync();
        }
        finally
        {
            await base.DisposeAsync();
        }
    }
    protected override void Dispose(bool disposing)
    {
        try
        {
            if (!disposing || DisposeFlag.IsFirstCall() == false) return;
            DisposeInternalAsync()._GetResult();
        }
        finally { base.Dispose(disposing); }
    }
    Task DisposeInternalAsync()
    {
        return Task.CompletedTask;
    }

    // 以下の段落の項目は CanSeek が有効なときのみ実装すればよい
    protected abstract long GetLengthImpl();
    protected abstract void SetLengthImpl(long length);
    protected abstract long GetPositionImpl();
    protected abstract void SetPositionImpl(long position);
    protected abstract long SeekImpl(long offset, SeekOrigin origin);

    protected abstract Task FlushImplAsync(CancellationToken cancellationToken = default);
    protected abstract ValueTask<int> ReadImplAsync(Memory<byte> buffer, CancellationToken cancellationToken = default);
    protected abstract ValueTask WriteImplAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default);

    public sealed override bool CanRead => StreamImplOptions.CanRead;
    public sealed override bool CanSeek => StreamImplOptions.CanSeek;
    public sealed override bool CanWrite => StreamImplOptions.CanWrite;

    public sealed override long Length => GetLengthImpl();
    public sealed override long Position { get => GetPositionImpl(); set => SetPositionImpl(value); }
    public sealed override long Seek(long offset, SeekOrigin origin) => SeekImpl(offset, origin);
    public sealed override void SetLength(long value) => SetLengthImpl(value);

    public sealed override bool CanTimeout => true;
    public override int ReadTimeout { get; set; }
    public override int WriteTimeout { get; set; }

    public sealed override void Flush() => FlushAsync()._GetResult();

    public sealed override Task FlushAsync(CancellationToken cancellationToken = default) => FlushImplAsync(cancellationToken);

    public sealed override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken = default)
        => await WriteAsync(buffer._AsReadOnlyMemory(offset, count), cancellationToken);

    public sealed override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken = default)
        => await ReadAsync(buffer.AsMemory(offset, count), cancellationToken);

    public sealed override void Write(byte[] buffer, int offset, int count) => WriteAsync(buffer, offset, count, default)._GetResult();
    public sealed override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer, offset, count, default)._GetResult();

    public sealed override IAsyncResult BeginRead(byte[] buffer, int offset, int count, AsyncCallback? callback, object? state)
        => ReadAsync(buffer, offset, count, default)._AsApm(callback, state);
    public sealed override IAsyncResult BeginWrite(byte[] buffer, int offset, int count, AsyncCallback? callback, object? state)
        => WriteAsync(buffer, offset, count, default)._AsApm(callback, state);
    public sealed override int EndRead(IAsyncResult asyncResult) => ((Task<int>)asyncResult)._GetResult();
    public sealed override void EndWrite(IAsyncResult asyncResult) => ((Task)asyncResult)._GetResult();

    public sealed override bool Equals(object? obj) => object.Equals(this, obj);
    public sealed override int GetHashCode() => 0;

    string? MyNameCache = null;

    public override string ToString()
    {
        MyNameCache ??= this.GetType().ToString();
        return MyNameCache;
    }
    [Obsolete]
    public sealed override object InitializeLifetimeService() => base.InitializeLifetimeService();
    public sealed override void Close() => Dispose(true);

    public sealed override void CopyTo(Stream destination, int bufferSize)
    {
        byte[] array = ArrayPool<byte>.Shared.Rent(bufferSize);
        try
        {
            int count;
            while ((count = this.Read(array, 0, array.Length)) != 0)
            {
                destination.Write(array, 0, count);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(array, false);
        }
    }

    public sealed override async Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
        try
        {
            for (; ; )
            {
                int num = await this.ReadAsync(new Memory<byte>(buffer), cancellationToken).ConfigureAwait(false);
                int num2 = num;
                if (num2 == 0)
                {
                    break;
                }
                await destination.WriteAsync(new ReadOnlyMemory<byte>(buffer, 0, num2), cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer, false);
        }
    }

    [Obsolete]
    protected sealed override WaitHandle CreateWaitHandle() => new ManualResetEvent(false);

    [Obsolete]
    protected sealed override void ObjectInvariant() { }

    override public int Read(Span<byte> buffer)
    {
        byte[] array = ArrayPool<byte>.Shared.Rent(buffer.Length);
        int result;
        try
        {
            int num = this.Read(array, 0, buffer.Length);
            if ((ulong)num > (ulong)((long)buffer.Length))
            {
                throw new IOException("StreamTooLong");
            }
            new Span<byte>(array, 0, num).CopyTo(buffer);
            result = num;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(array, false);
        }
        return result;
    }

    override public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        => await ReadImplAsync(buffer, cancellationToken);

    public sealed override int ReadByte()
    {
        byte[] array = new byte[1];
        if (this.Read(array, 0, 1) == 0)
        {
            return -1;
        }
        return (int)array[0];
    }

    override public void Write(ReadOnlySpan<byte> buffer)
    {
        byte[] array = ArrayPool<byte>.Shared.Rent(buffer.Length);
        try
        {
            buffer.CopyTo(array);
            this.Write(array, 0, buffer.Length);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(array, false);
        }
    }

    override public async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        => await WriteImplAsync(buffer, cancellationToken);

    public sealed override void WriteByte(byte value)
        => this.Write(new byte[] { value }, 0, 1);
}

public class DatagramExchangePoint : IDisposable, IAsyncDisposable
{
    public int NumPipes { get; }
    public PipePointSide Side { get; }
    public DatagramExchange Exchange { get; }
    public IReadOnlyList<PipePoint> PipePointList { get; }

    internal DatagramExchangePoint(EnsureInternal yes, DatagramExchange exchange, PipePointSide side, IReadOnlyList<PipePoint> pipePointList)
    {
        this.Side = side;
        this.Exchange = exchange;
        this.PipePointList = pipePointList;
        this.NumPipes = this.PipePointList.Count;
    }

    public PipePoint this[int index]
    {
        [MethodImpl(Inline)]
        get => this.PipePointList[(int)((uint)index % (uint)NumPipes)];
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
        foreach (PipePoint pp in this.PipePointList)
        {
            await pp.DisposeAsync();
        }
    }
}

public class DatagramExchange : IDisposable, IAsyncDisposable
{
    public DatagramExchangePoint A { get; }
    public DatagramExchangePoint B { get; }
    public int NumPipes { get; }
    public int MaxQueueLength { get; }

    public DatagramExchange(int numPipes, CancellationToken grandCancel = default, int? maxQueueLength = null)
    {
        if (numPipes <= 0) throw new ArgumentOutOfRangeException("numPipes <= 0");

        this.NumPipes = numPipes;
        this.MaxQueueLength = maxQueueLength ?? CoresConfig.SpanBasedQueueSettings.DefaultMaxQueueLength;
        if (this.MaxQueueLength <= 0) this.MaxQueueLength = int.MaxValue;

        List<PipePoint> List_A = new List<PipePoint>();
        List<PipePoint> List_B = new List<PipePoint>();

        for (int i = 0; i < numPipes; i++)
        {
            PipePoint pe_A = PipePoint.NewDuplexPipeAndGetOneSide(PipePointSide.A_LowerSide, grandCancel, null, this.MaxQueueLength);
            PipePoint pe_B = pe_A.CounterPart!;

            List_A.Add(pe_A);
            List_B.Add(pe_B);
        }

        this.A = new DatagramExchangePoint(EnsureInternal.Yes, this, PipePointSide.A_LowerSide, List_A);
        this.B = new DatagramExchangePoint(EnsureInternal.Yes, this, PipePointSide.B_UpperSide, List_B);
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
        await this.A._DisposeSafeAsync();
        await this.B._DisposeSafeAsync();
    }
}

// 片方の Stream から書き込むともう 1 つの Stream から出てくるパイプの終端 Stream ペア。不要になった場合は 2 回リリースすること!!
public class PipeStreamPair
{
    readonly RefInt Counter = new RefInt(2);

    readonly PipePoint PointA, PointB;

    public PipeStream StreamA { get; }
    public PipeStream StreamB { get; }

    public PipeStreamPair(bool autoFlush = true)
    {
        PointA = PipePoint.NewDuplexPipeAndGetOneSide(PipePointSide.A_LowerSide);
        PointB = PointA.CounterPart!;

        StreamA = PointA._InternalGetStream(autoFlush);
        StreamB = PointB!._InternalGetStream(autoFlush);
    }

    public async Task ReleaseAsync()
    {
        if (Counter.Decrement() == 0)
        {
            await this.PointA._DisposeSafeAsync();
            await this.PointB._DisposeSafeAsync();

            await this.StreamA._DisposeSafeAsync();
            await this.StreamB._DisposeSafeAsync();
        }
    }
}

// PipeStreamPair で、かつ StreamB のほうを処理できる新たなタスクを立ち上げる。そのタスクが終了すると 1 回 Release をする。もう 1 回リリースするには、Dispose を呼び出す。
public class PipeStreamPairWithSubTask : IDisposable, IAsyncDisposable
{
    readonly PipeStreamPair Pair;

    public PipeStream StreamA { get; }

    public Task TaskForStreamB { get; } // 終了待機に利用可能 (利用しなくても良い)

    public PipeStreamPairWithSubTask(Func<PipeStream, Task> taskForStreamB, bool autoFlush = true, bool noDebugMessage = false)
    {
        this.Pair = new PipeStreamPair(autoFlush);

        this.StreamA = this.Pair.StreamA;

        this.TaskForStreamB = AsyncAwait(async () =>
        {
            try
            {
                await taskForStreamB(this.Pair.StreamB);
            }
            catch (Exception ex)
            {
                if (noDebugMessage == false)
                {
                    ex._Debug();
                }
            }
            finally
            {
                await this.Pair.StreamB.DisconnectAsync()._TryAwait();
                await this.Pair.ReleaseAsync()._TryAwait();
            }
        });
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
        await this.Pair.StreamA.DisconnectAsync()._TryAwait();
        await this.Pair.ReleaseAsync()._TryAwait();
    }
}

public class WebSocketStream : StreamImplBase
{
    public bool AutoDispose { get; }
    public System.Net.WebSockets.WebSocket BaseSocket;

    public WebSocketStream(System.Net.WebSockets.WebSocket webSocket, bool autoDispose = false) : base(new StreamImplBaseOptions(true, true, false))
    {
        try
        {
            this.AutoDispose = autoDispose;
            this.BaseSocket = webSocket;
        }
        catch
        {
            this._DisposeSafe();
            throw;
        }
    }

    Once DisposeFlag;
    public override async ValueTask DisposeAsync()
    {
        try
        {
            if (DisposeFlag.IsFirstCall() == false) return;
            await DisposeInternalAsync();
        }
        finally
        {
            await base.DisposeAsync();
        }
    }
    protected override void Dispose(bool disposing)
    {
        try
        {
            if (!disposing || DisposeFlag.IsFirstCall() == false) return;
            DisposeInternalAsync()._GetResult();
        }
        finally { base.Dispose(disposing); }
    }
    async Task DisposeInternalAsync()
    {
        RecvDisconnected.Set();
        SendDisconnected.Set();

        if (this.AutoDispose)
        {
            try
            {
                await this.BaseSocket.CloseAsync(System.Net.WebSockets.WebSocketCloseStatus.NormalClosure, null, default);
            }
            catch { }

            await this.BaseSocket._DisposeSafeAsync2();
        }
    }

    public override bool DataAvailable => throw new NotImplementedException();
    protected override Task FlushImplAsync(CancellationToken cancellationToken = default) => TaskCompleted;
    protected override long GetLengthImpl() => throw new NotImplementedException();
    protected override long GetPositionImpl() => throw new NotImplementedException();
    protected override void SetLengthImpl(long length) => throw new NotImplementedException();
    protected override void SetPositionImpl(long position) => throw new NotImplementedException();
    protected override long SeekImpl(long offset, SeekOrigin origin) => throw new NotImplementedException();

    Once RecvDisconnected;
    Once SendDisconnected;

    protected override async ValueTask<int> ReadImplAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (RecvDisconnected.IsSet) return 0;

        var ret = await this.BaseSocket.ReceiveAsync(buffer, cancellationToken);
        if (ret.Count == 0 || ret.MessageType == System.Net.WebSockets.WebSocketMessageType.Close)
        {
            RecvDisconnected.Set();

            return 0;
        }

        return ret.Count;
    }

    protected override async ValueTask WriteImplAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (SendDisconnected.IsSet) throw new DisconnectedException();

        await this.BaseSocket.SendAsync(buffer, System.Net.WebSockets.WebSocketMessageType.Text, false, cancellationToken);
    }
}


