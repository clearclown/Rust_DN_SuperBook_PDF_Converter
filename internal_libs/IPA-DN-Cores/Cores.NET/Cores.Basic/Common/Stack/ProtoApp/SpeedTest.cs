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

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

namespace IPA.Cores.Basic;

[Flags]
public enum SpeedTestModeFlag
{
    Upload,
    Download,
    Both,
}

public class SpeedTestServer : AsyncServiceWithMainLoop
{
    Memory<byte> SendData;

    int[] ServerPorts;

    int RecvTimeout = 5000;

    TcpIpSystem System;

    public SpeedTestServer(TcpIpSystem system, params int[] ports)
    {
        this.System = system;
        this.ServerPorts = ports;

        InitSendData();

        StartMainLoop(ServerMainLoop);
    }

    void InitSendData()
    {
        int size = 65536;
        byte[] data = Util.Rand(size);
        for (int i = 0; i < data.Length; i++)
        {
            if (data[i] == (byte)'!')
                data[i] = (byte)'*';
        }
        SendData = data;
    }

    Once Once;

    public class SessionData
    {
        public bool NoMoreData = false;
    }

    async Task ServerMainLoop(CancellationToken mainLoopCancel)
    {
        if (Once.IsFirstCall() == false)
            throw new ApplicationException("You cannot reuse the object.");

        using (var sessions = new GroupManager<ulong, SessionData>(
            onNewGroup: (key, state) =>
            {
                Dbg.Where($"New session: {key}");
                return new SessionData();
            },
            onDeleteGroup: (key, ctx, state) =>
            {
                Dbg.Where($"Delete session: {key}");
            }))
        {
            var listener = System.CreateTcpListener(new TcpListenParam(async (lx, sock) =>
            {
                Con.WriteLine($"Connected {sock.Info.Tcp.RemoteIPAddress}:{sock.Info.Tcp.RemotePort} -> {sock.Info.Tcp.LocalIPAddress}:{sock.Info.Tcp.LocalPort}");

                var app = sock.GetNetAppProtocolStub();

                var st = app.GetStream();

                var attachHandle = app.AttachHandle;

                await attachHandle.SetStreamReceiveTimeoutAsync(RecvTimeout);

                await st.SendAsync("TrafficServer\r\n\0"._GetBytes_Ascii());

                ReadOnlyMemoryBuffer<byte> buf = await st.ReceiveAsync(17);

                Direction dir = buf.ReadBool8() ? Direction.Send : Direction.Recv;
                ulong sessionId = 0;
                long timespan = 0;

                try
                {
                    sessionId = buf.ReadUInt64();
                    timespan = buf.ReadSInt64();
                }
                catch { }

                long recvEndTick = FastTick64.Now + timespan;
                if (timespan == 0) recvEndTick = long.MaxValue;

                using (var session = sessions.Enter(sessionId))
                {
                    await using (var delay = new DelayAction((int)(Math.Min(timespan * 3 + 180 * 1000, int.MaxValue)), x => app._CancelSafeAsync(new TimeoutException())))
                    {
                        if (dir == Direction.Recv)
                        {
                            RefInt refTmp = new RefInt();
                            long totalSize = 0;

                            while (true)
                            {
                                var ret = await st.FastReceiveAsync(totalRecvSize: refTmp);
                                if (ret.Count == 0)
                                {
                                    break;
                                }
                                totalSize += refTmp;

                                if (ret[0].Span[0] == (byte)'!')
                                    break;

                                if (FastTick64.Now >= recvEndTick)
                                    break;
                            }

                            await attachHandle.SetStreamReceiveTimeoutAsync(Timeout.Infinite);
                            await attachHandle.SetStreamSendTimeoutAsync(60 * 5 * 1000);

                            session.Context.NoMoreData = true;

                            while (true)
                            {
                                MemoryBuffer<byte> sendBuf = new MemoryBuffer<byte>();
                                sendBuf.WriteSInt64(totalSize);

                                await st.SendAsync(sendBuf);

                                await Task.Delay(100);
                            }
                        }
                        else
                        {
                            await attachHandle.SetStreamReceiveTimeoutAsync(Timeout.Infinite);
                            await attachHandle.SetStreamSendTimeoutAsync(Timeout.Infinite);

                            while (true)
                            {
                                if (sessionId == 0 || session.Context.NoMoreData == false)
                                {
                                    await st.SendAsync(SendData);
                                }
                                else
                                {
                                    var recvMemory = await st.ReceiveAsync();

                                    if (recvMemory.Length == 0)
                                        break;
                                }
                            }
                        }
                    }
                }
            },
            "SpeedTest",
            this.ServerPorts));

            try
            {
                Con.WriteLine("Listening.");

                await TaskUtil.WaitObjectsAsync(cancels: mainLoopCancel._SingleArray());
            }
            finally
            {
                await listener._DisposeWithCleanupSafeAsync();
            }
        }
    }
}

public class SpeedTestClient
{
    public class Result
    {
        public long NumBytesUpload;      // Uploaded size
        public long NumBytesDownload;    // Downloaded size
        public long NumBytesTotal;       // Total size
        public long Span;                // Period (in milliseconds)
        public long BpsUpload;           // Upload throughput
        public long BpsDownload;         // Download throughput
        public long BpsTotal;            // Total throughput
    }

    Memory<byte> SendData;

    IPAddress ServerIP;
    int ServerPort;
    int NumConnection;
    SpeedTestModeFlag Mode;
    int TimeSpan;
    ulong SessionId;
    CancellationToken Cancel;
    ExceptionQueue? ExceptionQueue;
    AsyncManualResetEvent ClientStartEvent;
    TcpIpSystem System;

    int ConnectTimeout = 10 * 1000;
    int RecvTimeout = 5000;

    static readonly RefInt CurrentCount = new RefInt();

    public static bool IsInProgress => CurrentCount.Get() >= 1;

    static public async Task<List<SpeedTestClient.Result>> RunSpeedTestWithMultiTryAsync(TcpIpSystem system, IPAddress ip, int port, int numConnection, int timespan, SpeedTestModeFlag mode, int numTry, int interval, CancellationToken cancel = default)
    {
        List<SpeedTestClient.Result> ret = new List<Result>();

        numTry = Math.Max(numTry, 1);

        for (int i = 0; i < numTry; i++)
        {
            if (cancel.IsCancellationRequested) return new List<Result>();

            SpeedTestClient tc = new SpeedTestClient(system, ip, port, numConnection, timespan, mode, cancel);

            var result = await tc.RunClientAsync();

            if (result != null)
            {
                ret.Add(result);
            }

            await cancel._WaitUntilCanceledAsync(Util.GenRandInterval(interval));
        }

        return ret;
    }

    public SpeedTestClient(TcpIpSystem system, IPAddress ip, int port, int numConnection, int timespan, SpeedTestModeFlag mode, CancellationToken cancel = default)
    {
        this.System = system;
        this.ServerIP = ip;
        this.ServerPort = port;
        this.Cancel = cancel;
        this.NumConnection = Math.Max(numConnection, 1);
        this.TimeSpan = Math.Max(timespan, 1000);
        this.Mode = mode;
        if (Mode == SpeedTestModeFlag.Both)
        {
            this.NumConnection = Math.Max(NumConnection, 2);
        }
        this.ClientStartEvent = new AsyncManualResetEvent();
        InitSendData();
    }

    void InitSendData()
    {
        int size = 65536;
        byte[] data = Util.Rand(size);
        for (int i = 0; i < data.Length; i++)
        {
            if (data[i] == (byte)'!')
                data[i] = (byte)'*';
        }
        SendData = data;
    }

    Once Once;

    public async Task<Result?> RunClientAsync()
    {
        CurrentCount.Increment();

        try
        {
            if (Once.IsFirstCall() == false)
                throw new ApplicationException("You cannot reuse the object.");

            Con.WriteLine("Client mode start");

            ExceptionQueue = new ExceptionQueue();
            SessionId = Util.RandUInt64();

            List<Task<Result>> tasks = new List<Task<Result>>();
            List<AsyncManualResetEvent> readyEvents = new List<AsyncManualResetEvent>();

            await using (CancelWatcher cancelWatcher = new CancelWatcher(this.Cancel))
            {
                for (int i = 0; i < NumConnection; i++)
                {
                    Direction dir;
                    if (Mode == SpeedTestModeFlag.Download)
                        dir = Direction.Recv;
                    else if (Mode == SpeedTestModeFlag.Upload)
                        dir = Direction.Send;
                    else
                        dir = ((i % 2) == 0) ? Direction.Recv : Direction.Send;

                    AsyncManualResetEvent readyEvent = new AsyncManualResetEvent();
                    var t = ClientSingleConnectionAsync(dir, readyEvent, cancelWatcher.CancelToken);
                    ExceptionQueue.RegisterWatchedTask(t);
                    tasks.Add(t);
                    readyEvents.Add(readyEvent);
                }

                try
                {
                    using (var whenAllReady = new WhenAll(readyEvents.Select(x => x.WaitAsync())))
                    {
                        await TaskUtil.WaitObjectsAsync(
                            tasks: tasks.Append(whenAllReady.WaitMe).ToArray(),
                            cancels: cancelWatcher.CancelToken._SingleArray(),
                            manualEvents: ExceptionQueue.WhenExceptionAdded._SingleArray());
                    }

                    Cancel.ThrowIfCancellationRequested();
                    ExceptionQueue.ThrowFirstExceptionIfExists();

                    ExceptionQueue.WhenExceptionAdded.CallbackList.AddSoftCallback(x =>
                    {
                        cancelWatcher.Cancel();
                    });

                    await using (new DelayAction(TimeSpan * 3 + 180 * 1000, x =>
                    {
                        cancelWatcher.Cancel();
                        return Task.CompletedTask;
                    }, doNotBlockOnDispose: true))
                    {
                        ClientStartEvent.Set(true);

                        using (var whenAllCompleted = new WhenAll(tasks))
                        {
                            await TaskUtil.WaitObjectsAsync(
                                tasks: whenAllCompleted.WaitMe._SingleArray(),
                                cancels: cancelWatcher.CancelToken._SingleArray()
                                );

                            await whenAllCompleted.WaitMe;
                        }
                    }

                    Result ret = new Result();

                    ret.Span = TimeSpan;

                    foreach (var r in tasks.Select(x => x._GetResult()))
                    {
                        ret.NumBytesDownload += r.NumBytesDownload;
                        ret.NumBytesUpload += r.NumBytesUpload;
                    }

                    ret.NumBytesTotal = ret.NumBytesUpload + ret.NumBytesDownload;

                    ret.BpsUpload = (long)((double)ret.NumBytesUpload * 1000.0 * 8.0 / (double)ret.Span * 1514.0 / 1460.0);
                    ret.BpsDownload = (long)((double)ret.NumBytesDownload * 1000.0 * 8.0 / (double)ret.Span * 1514.0 / 1460.0);
                    ret.BpsTotal = ret.BpsUpload + ret.BpsDownload;

                    return ret;
                }
                catch (Exception ex)
                {
                    await Task.Yield();
                    ExceptionQueue.Add(ex);
                }
                finally
                {
                    cancelWatcher.Cancel();
                    try
                    {
                        await Task.WhenAll(tasks);
                    }
                    catch { }
                }

                ExceptionQueue.ThrowFirstExceptionIfExists();
            }

            return null;
        }
        finally
        {
            CurrentCount.Decrement();
        }
    }

    async Task<Result> ClientSingleConnectionAsync(Direction dir, AsyncManualResetEvent fireMeWhenReady, CancellationToken cancel)
    {
        Result ret = new Result();

        await using (NetPalTcpProtocolStub tcp = new NetPalTcpProtocolStub(cancel: cancel))
        {
            await tcp.ConnectAsync(ServerIP, ServerPort, cancel, ConnectTimeout);

            await using (ConnSock sock = new ConnSock(tcp))
            {
                NetAppStub app = sock.GetNetAppProtocolStub();

                AttachHandle attachHandle = app.AttachHandle;

                PipeStream st = app.GetStream();

                if (dir == Direction.Recv)
                    await app.AttachHandle.SetStreamReceiveTimeoutAsync(RecvTimeout);

                try
                {
                    var hello = await st.ReceiveAllAsync(16);

                    if (hello.Span.ToArray()._GetString_Ascii().StartsWith("TrafficServer\r\n") == false)
                        throw new ApplicationException("Target server is not a Traffic Server.");

                    //throw new ApplicationException("aaaa" + dir.ToString());

                    fireMeWhenReady.Set();

                    cancel.ThrowIfCancellationRequested();

                    await TaskUtil.WaitObjectsAsync(
                        manualEvents: ClientStartEvent._SingleArray(),
                        cancels: cancel._SingleArray()
                        );

                    long tickStart = FastTick64.Now;
                    long tickEnd = tickStart + this.TimeSpan;

                    var sendData = new MemoryBuffer<byte>();
                    sendData.WriteBool8(dir == Direction.Recv);
                    sendData.WriteUInt64(SessionId);
                    sendData.WriteSInt64(TimeSpan);

                    await st.SendAsync(sendData);

                    if (dir == Direction.Recv)
                    {
                        RefInt totalRecvSize = new RefInt();
                        while (true)
                        {
                            long now = FastTick64.Now;

                            if (now >= tickEnd)
                                break;

                            await TaskUtil.WaitObjectsAsync(
                                tasks: st.FastReceiveAsync(totalRecvSize: totalRecvSize)._SingleArray(),
                                timeout: (int)(tickEnd - now),
                                exceptions: ExceptionWhen.TaskException | ExceptionWhen.CancelException);

                            ret.NumBytesDownload += totalRecvSize;
                        }
                    }
                    else
                    {
                        await attachHandle.SetStreamReceiveTimeoutAsync(Timeout.Infinite);

                        while (true)
                        {
                            long now = FastTick64.Now;

                            if (now >= tickEnd)
                                break;

                            /*await WebSocketHelper.WaitObjectsAsync(
                                tasks: st.FastSendAsync(SendData, flush: true).ToSingleArray(),
                                timeout: (int)(tick_end - now),
                                exceptions: ExceptionWhen.TaskException | ExceptionWhen.CancelException);*/

                            await st.FastSendAsync(SendData, flush: true);
                        }

                        Task recvResult = Task.Run(async () =>
                        {
                            var recvMemory = await st.ReceiveAllAsync(8);

                            MemoryBuffer<byte> recvMemoryBuf = recvMemory;
                            ret.NumBytesUpload = recvMemoryBuf.ReadSInt64();

                            st.Disconnect();
                        });

                        Task sendSurprise = Task.Run(async () =>
                        {
                            byte[] surprise = new byte[260];
                            surprise.AsSpan().Fill((byte)'!');
                            while (true)
                            {
                                await st.SendAsync(surprise);

                                await TaskUtil.WaitObjectsAsync(
                                    manualEvents: sock.Pipe.OnDisconnectedEvent._SingleArray(),
                                    timeout: 200);
                            }
                        });

                        await WhenAll.Await(false, recvResult, sendSurprise);

                        await recvResult;
                    }

                    st.Disconnect();
                    return ret;
                }
                catch (Exception ex)
                {
                    ExceptionQueue!.Add(ex);
                    throw;
                }
            }
        }
    }
}

