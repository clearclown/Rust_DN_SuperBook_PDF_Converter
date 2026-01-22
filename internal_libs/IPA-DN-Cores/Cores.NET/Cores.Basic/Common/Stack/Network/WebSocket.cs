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
using System.Net.Http;
using System.IO;
using System.Security.Cryptography;
using Castle.Core.Internal;

namespace IPA.Cores.Basic;

public static partial class CoresConfig
{

}

[Flags]
public enum WebSocketOpcode : byte
{
    Continue = 0x00,
    Text = 0x01,
    Bin = 0x02,
    Close = 0x08,
    Ping = 0x09,
    Pong = 0x0A,
}

public class WebSocketOptions : NetMiddleProtocolOptionsBase
{
    public string UserAgent { get; set; } = "Mozilla/5.0 (WebSocket) WebSocket Client";
    public int TimeoutOpen { get; set; } = 10 * 1000;
    public int TimeoutComm { get; set; } = 10 * 1000;
    public int SendPingInterval { get; set; } = 1 * 1000;
    public int RecvMaxPayloadLenPerFrame { get; set; } = (8 * 1024 * 1024);
    public int SendSingleFragmentSize { get; set; } = (32 * 1024);
    public int RecvMaxTotalFragmentSize { get; set; } = (16 * 1024 * 1024);

    public int MaxBufferSize { get; set; } = (1600 * 1600);
    public bool RespectMessageDelimiter { get; set; } = false;
}

public class NetWebSocketProtocolStack : NetMiddleProtocolStackBase
{
    public new WebSocketOptions Options => (WebSocketOptions)base.Options;

    PipeStream LowerStream;
    PipeStream UpperStream;

    readonly AsyncAutoResetEvent SendPongEvent = new AsyncAutoResetEvent();
    readonly CriticalSection PongQueueLock = new CriticalSection<NetWebSocketProtocolStack>();
    readonly Queue<ReadOnlyMemory<byte>> PongQueue = new Queue<ReadOnlyMemory<byte>>();


    public NetWebSocketProtocolStack(PipePoint lower, PipePoint? upper, NetMiddleProtocolOptionsBase options, CancellationToken cancel = default) : base(lower, upper, options, cancel)
    {
        this.LowerStream = this.LowerAttach.GetStream();
        this.UpperStream = this.UpperAttach.GetStream();
    }

    Once Started;

    Task? SendTask;
    Task? RecvTask;

    public async Task StartWebSocketClientAsync(string uri, CancellationToken cancel = default)
    {
        if (Started.IsFirstCall() == false)
        {
            throw new ApplicationException("Already started.");
        }

        LowerStream.ReadTimeout = Options.TimeoutOpen;
        LowerStream.WriteTimeout = Options.TimeoutOpen;

        Uri u = new Uri(uri);
        HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Get, uri);

        byte[] nonce = Secure.Rand(16);
        string requestKey = Convert.ToBase64String(nonce);

        req.Headers.Add("Host", u.Host);
        req.Headers.Add("User-Agent", Options.UserAgent);
        req.Headers.Add("Accept", Consts.MimeTypes.Html);
        req.Headers.Add("Sec-WebSocket-Version", "13");
        req.Headers.Add("Origin", "null");
        req.Headers.Add("Sec-WebSocket-Key", requestKey);
        req.Headers.Add("Connection", "Upgrade");
        req.Headers.Add("Pragma", "no-cache");
        req.Headers.Add("Cache-Control", "no-cache");
        req.Headers.Add("Upgrade", "websocket");

        StringWriter tmpWriter = new StringWriter();
        tmpWriter.WriteLine($"{req.Method} {req.RequestUri!.PathAndQuery} HTTP/1.1");
        tmpWriter.WriteLine(req.Headers.ToString());

        await LowerStream.WriteAsync(tmpWriter.ToString()._GetBytes_UTF8(), cancel);
        Dictionary<string, string> headers = new Dictionary<string, string>(StrComparer.IgnoreCaseComparer);
        int num = 0;
        int responseCode = 0;

        StreamReader tmpReader = new StreamReader(LowerStream);
        while (true)
        {
            string? line = await TaskUtil.DoAsyncWithTimeout((procCancel) => tmpReader.ReadLineAsync(),
                timeout: Options.TimeoutOpen,
                cancel: cancel);

            if (line._IsNullOrZeroLen())
            {
                break;
            }

            if (num == 0)
            {
                string[] tokens = line.Split(' ');
                if (tokens[0] != "HTTP/1.1") throw new ApplicationException($"Cannot establish the WebSocket Protocol. Response: \"{tokens}\"");
                responseCode = int.Parse(tokens[1]);
            }
            else
            {
                string[] tokens = line.Split(':');
                string name = tokens[0].Trim();
                string value = tokens[1].Trim();
                headers[name] = value;
            }

            num++;
        }

        if (responseCode != 101)
        {
            throw new ApplicationException($"Cannot establish the WebSocket Protocol. Perhaps the destination host does not support WebSocket. Wrong response code: \"{responseCode}\"");
        }

        if (headers["Upgrade"].Equals("websocket", StringComparison.InvariantCultureIgnoreCase) == false)
        {
            throw new ApplicationException($"Wrong Upgrade header: \"{headers["Upgrade"]}\"");
        }

        string acceptKey = headers["Sec-WebSocket-Accept"];
        string keyCalcStr = requestKey + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
        SHA1 sha1 = SHA1.Create();
        string acceptKey2 = Convert.ToBase64String(sha1.ComputeHash(keyCalcStr._GetBytes_Ascii()));

        if (acceptKey != acceptKey2)
        {
            throw new ApplicationException($"Wrong accept_key: \'{acceptKey}\'");
        }

        this.SendTask = SendLoopAsync();
        this.RecvTask = RecvLoopAsync();
    }

    async Task SendLoopAsync()
    {
        try
        {
            CancellationToken cancel = this.GrandCancel;

            LocalTimer timer = new LocalTimer();

            long nextPingTick = timer.AddTimeout(0);

            while (true)
            {
                await LowerStream.WaitReadyToSendAsync(cancel, Timeout.Infinite);

                await UpperStream.WaitReadyToReceiveAsync(cancel, timer.GetNextInterval(), noTimeoutException: true, cancelEvent: this.SendPongEvent);

                MemoryBuffer<byte> sendBuffer = new MemoryBuffer<byte>();

                IReadOnlyList<ReadOnlyMemory<byte>> userDataList = UpperStream.FastReceiveNonBlock(out int totalRecvSize, maxSize: Options.MaxBufferSize);

                if (totalRecvSize >= 1)
                {
                    // Send data
                    if (Options.RespectMessageDelimiter == false)
                    {
                        userDataList = Util.DefragmentMemoryArrays(userDataList, Options.SendSingleFragmentSize);
                    }

                    foreach (ReadOnlyMemory<byte> userData in userDataList)
                    {
                        if (userData.Length >= 1)
                        {
                            BuildAndAppendFrame(sendBuffer, true, WebSocketOpcode.Bin, userData);
                        }
                    }
                }

                if (timer.Now >= nextPingTick)
                {
                    // Send ping
                    nextPingTick = timer.AddTimeout(Util.GenRandInterval(Options.SendPingInterval));

                    BuildAndAppendFrame(sendBuffer, true, WebSocketOpcode.Ping, "[WebSocketPing]"._GetBytes_Ascii());
                }

                lock (this.PongQueueLock)
                {
                    // Send pong
                    while (true)
                    {
                        if (this.PongQueue.TryDequeue(out ReadOnlyMemory<byte> data) == false)
                            break;

                        BuildAndAppendFrame(sendBuffer, true, WebSocketOpcode.Pong, data);
                    }
                }

                if (sendBuffer.Length >= 1)
                {
                    LowerStream.FastSendNonBlock(sendBuffer.Memory);
                }
            }
        }
        catch (Exception ex)
        {
            this.UpperStream.Disconnect();
            await this.CancelAsync(ex);
        }
    }

    async Task RecvLoopAsync()
    {
        try
        {
            CancellationToken cancel = this.GrandCancel;

            LowerStream.ReadTimeout = Options.TimeoutComm;

            MemoryBuffer<byte> currentRecvingMessage = new MemoryBuffer<byte>();

            while (true)
            {
                byte b1 = await LowerStream.ReceiveByteAsync(cancel).FlushOtherStreamIfPending(UpperStream);

                WebSocketOpcode opcode = (WebSocketOpcode)(b1 & 0x0f);

                byte b2 = await LowerStream.ReceiveByteAsync(cancel);

                bool isMasked = (b2 & 0b10000000)._ToBool();
                int tmp = (b2 & 0b01111111);

                ulong payloadLen64 = 0;
                if (tmp <= 125)
                {
                    payloadLen64 = (ulong)tmp;
                }
                else if (tmp == 126)
                {
                    payloadLen64 = await LowerStream.ReceiveUInt16Async(cancel).FlushOtherStreamIfPending(UpperStream);
                }
                else if (tmp == 127)
                {
                    payloadLen64 = await LowerStream.ReceiveUInt64Async(cancel).FlushOtherStreamIfPending(UpperStream);
                }

                if (payloadLen64 > (ulong)Options.RecvMaxPayloadLenPerFrame)
                {
                    throw new ApplicationException($"payloadLen64 {payloadLen64} > Options.RecvMaxPayloadLenPerFrame {Options.RecvMaxPayloadLenPerFrame}");
                }

                int payloadLen = (int)payloadLen64;

                Memory<byte> maskKey = default;

                if (isMasked)
                {
                    maskKey = await LowerStream.ReceiveAllAsync(4, cancel).FlushOtherStreamIfPending(UpperStream);
                }

                Memory<byte> data = await LowerStream.ReceiveAllAsync(payloadLen, cancel).FlushOtherStreamIfPending(UpperStream);

                if (isMasked)
                {
                    TaskUtil.Sync(() =>
                    {
                        Span<byte> maskKeySpan = maskKey.Span;
                        Span<byte> dataSpan = data.Span;
                        for (int i = 0; i < dataSpan.Length; i++)
                        {
                            dataSpan[i] = (byte)(dataSpan[i] ^ maskKeySpan[i % 4]);
                        }
                    });
                }

                if (opcode.EqualsAny(WebSocketOpcode.Text, WebSocketOpcode.Bin, WebSocketOpcode.Continue))
                {
                    if (Options.RespectMessageDelimiter == false)
                    {
                        if (data.Length >= 1)
                        {
                            await UpperStream.WaitReadyToSendAsync(cancel, Timeout.Infinite);

                            UpperStream.FastSendNonBlock(data, false);
                        }
                    }
                    else
                    {
                        bool isFin = (b1 & 0b10000000)._ToBool();

                        if (isFin && opcode.EqualsAny(WebSocketOpcode.Text, WebSocketOpcode.Bin))
                        {
                            // Single message
                            if (data.Length >= 1)
                            {
                                await UpperStream.WaitReadyToSendAsync(cancel, Timeout.Infinite);

                                UpperStream.FastSendNonBlock(data, false);
                            }
                        }
                        else if (isFin == false && opcode.EqualsAny(WebSocketOpcode.Text, WebSocketOpcode.Bin))
                        {
                            // First message
                            currentRecvingMessage.Clear();

                            if ((currentRecvingMessage.Length + data.Length) >= Options.RecvMaxTotalFragmentSize)
                                throw new ApplicationException("WebSocket: Exceeding Options.RecvMaxTotalFragmentSize.");

                            currentRecvingMessage.Write(data);
                        }
                        else if (isFin && opcode == WebSocketOpcode.Continue)
                        {
                            // Final continuous message
                            if ((currentRecvingMessage.Length + data.Length) >= Options.RecvMaxTotalFragmentSize)
                                throw new ApplicationException("WebSocket: Exceeding Options.RecvMaxTotalFragmentSize.");

                            currentRecvingMessage.Write(data);

                            if (currentRecvingMessage.Length >= 1)
                            {
                                await UpperStream.WaitReadyToSendAsync(cancel, Timeout.Infinite);

                                UpperStream.FastSendNonBlock(data, false);
                            }

                            currentRecvingMessage.Clear();
                        }
                        else if (isFin == false && opcode == WebSocketOpcode.Continue)
                        {
                            // Intermediate continuous message
                            if ((currentRecvingMessage.Length + data.Length) >= Options.RecvMaxTotalFragmentSize)
                                throw new ApplicationException("WebSocket: Exceeding Options.RecvMaxTotalFragmentSize.");

                            currentRecvingMessage.Write(data);
                        }
                    }
                }
                else if (opcode == WebSocketOpcode.Pong)
                {
                    lock (this.PongQueueLock)
                    {
                        this.PongQueue.Enqueue(data);
                    }
                    this.SendPongEvent.Set(true);
                }
                else if (opcode == WebSocketOpcode.Ping)
                {
                    lock (this.PongQueueLock)
                    {
                        this.PongQueue.Enqueue(data);
                    }
                    this.SendPongEvent.Set(true);
                }
                else if (opcode == WebSocketOpcode.Close)
                {
                    throw new DisconnectedException();
                }
                else
                {
                    throw new ApplicationException($"WebSocket: Unknown Opcode: {(int)opcode}");
                }
            }
        }
        catch (Exception ex)
        {
            this.UpperStream.Disconnect();
            await this.CancelAsync(ex);
        }
    }

    public static void BuildAndAppendFrame(MemoryBuffer<byte> dest, bool clientMode, WebSocketOpcode opcode, ReadOnlyMemory<byte> data)
    {
        // Header
        byte b0 = (byte)(0b10000000 | (byte)opcode);
        dest.WriteByte(b0);

        // Payload size
        byte b1 = 0;

        int len = data.Length;

        if (len <= 125)
        {
            b1 = (byte)len;
        }
        else if (len <= 65536)
        {
            b1 = 126;
        }
        else
        {
            b1 = 127;
        }

        b1 = (byte)(b1 | (clientMode ? 0b10000000 : 0));

        dest.WriteByte(b1);

        if (len <= 125) { }
        else if (len >= 126 && len <= 65536)
        {
            dest.WriteUInt16((ushort)len);
        }
        else
        {
            dest.WriteUInt64((ulong)len);
        }

        if (clientMode)
        {
            // Masked data
            byte[] maskKey = Util.Rand(4);

            dest.Write(maskKey);

            byte[] maskedData = new byte[data.Length];

            if (data.Length >= 1)
            {
                ReadOnlySpan<byte> dataSpan = data.Span;
                for (int i = 0; i < data.Length; i++)
                {
                    maskedData[i] = (byte)(dataSpan[i] ^ maskKey[i % 4]);
                }
            }

            dest.Write(maskedData);
        }
        else
        {
            // Raw data
            dest.Write(data);
        }
    }

    protected override Task CancelImplAsync(Exception? ex)
    {
        return base.CancelImplAsync(ex);
    }


    protected override async Task CleanupImplAsync(Exception? ex)
    {
        try
        {
            await this.LowerStream._DisposeSafeAsync();
            await this.UpperStream._DisposeSafeAsync();
        }
        finally
        {
            await base.CleanupImplAsync(ex);
        }
    }
}

public class WebSocketConnectOptions
{
    public TcpIpSystem TcpIp { get; }
    public WebSocketOptions WebSocketOptions { get; }
    public PalSslClientAuthenticationOptions SslOptions { get; }

    public WebSocketConnectOptions(WebSocketOptions? wsOptions = null, PalSslClientAuthenticationOptions? sslOptions = null, TcpIpSystem? tcpIp = null)
    {
        this.TcpIp = tcpIp ?? LocalNet;
        this.WebSocketOptions = wsOptions ?? new WebSocketOptions();

        if (sslOptions == null)
        {
            this.SslOptions = new PalSslClientAuthenticationOptions(true);
        }
        else
        {
            this.SslOptions = (PalSslClientAuthenticationOptions)sslOptions.Clone();
        }
    }
}

public class WebSocket : MiddleConnSock
{
    protected new NetWebSocketProtocolStack Stack => (NetWebSocketProtocolStack)base.Stack;

    public WebSocket(ConnSock lowerSock, WebSocketOptions? options = null) : base(new NetWebSocketProtocolStack(lowerSock.UpperPoint, null, options._FilledOrDefault(new WebSocketOptions())))
    {
    }

    public async Task StartWebSocketClientAsync(string uri, CancellationToken cancel = default)
        => await Stack.StartWebSocketClientAsync(uri, cancel);

    public static async Task<WebSocket> ConnectAsync(string uri, WebSocketConnectOptions? options = null, CancellationToken cancel = default)
    {
        if (options == null) options = new WebSocketConnectOptions();

        Uri u = new Uri(uri);

        int port = 0;
        bool useSsl = false;

        if (u.Scheme._IsSamei("ws"))
        {
            port = Consts.Ports.Http;
        }
        else if (u.Scheme._IsSamei("wss"))
        {
            port = Consts.Ports.Https;
            useSsl = true;
        }
        else
        {
            throw new ArgumentException($"uri \"{uri}\" is not a WebSocket address.");
        }

        if (u.IsDefaultPort == false)
        {
            port = u.Port;
        }

        ConnSock tcpSock = await LocalNet.ConnectIPv4v6DualAsync(new TcpConnectParam(u.Host, port, connectTimeout: options.WebSocketOptions.TimeoutOpen, dnsTimeout: options.WebSocketOptions.TimeoutOpen), cancel);
        try
        {
            ConnSock targetSock = tcpSock;

            try
            {
                if (useSsl)
                {
                    SslSock sslSock = new SslSock(tcpSock);
                    try
                    {
                        options.SslOptions.TargetHost = u.Host;

                        await sslSock.StartSslClientAsync(options.SslOptions, cancel);

                        targetSock = sslSock;
                    }
                    catch
                    {
                        sslSock._DisposeSafe();
                        throw;
                    }
                }

                WebSocket webSock = new WebSocket(targetSock, options.WebSocketOptions);

                await webSock.StartWebSocketClientAsync(uri, cancel);

                return webSock;
            }
            catch
            {
                targetSock.Dispose();
                throw;
            }
        }
        catch
        {
            tcpSock._DisposeSafe();
            throw;
        }
    }
}

