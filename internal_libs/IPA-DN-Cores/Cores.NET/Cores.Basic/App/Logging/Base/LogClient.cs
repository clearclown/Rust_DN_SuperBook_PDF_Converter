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

#if CORES_BASIC_JSON

using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Linq;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace IPA.Cores.Basic;

public static partial class CoresConfig
{
    public static partial class LogProtocolSettings
    {
        public static readonly Copenhagen<int> DefaultDelay = 250;

        public static readonly Copenhagen<int> DefaultRetryIntervalMin = 1 * 1000;
        public static readonly Copenhagen<int> DefaultRetryIntervalMax = 60 * 60 * 1000;
    }
}


// SyslogClient を用いてログを送付するための LogRoute
public class SyslogClientLogRoute : LogRouteBase
{
    readonly SyslogClient Client;
    readonly string AppName;

    public SyslogClientLogRoute(SyslogClient client, string appName, string kind, LogPriority minimalPriority) : base(kind, minimalPriority)
    {
        this.AppName = appName._NullCheck();
        this.Client = client;
    }

    public override Task FlushAsync(bool halfFlush = false, CancellationToken cancel = default)
    {
        return this.Client.FlushAsync(3000, cancel);
    }

    public override void ReceiveLog(LogRecord record, string kind)
    {
        Client.SendLog(record);
    }
}

// SyslogClient の稼働を開始させた上で、ローカルのログのルーティングを SyslogClient に対して適切に設定するためのユーティリティクラス
public class SyslogClientInstaller : IDisposable
{
    public SyslogClient Client { get; }
    public SyslogClientLogRoute LogRoute { get; }

    public SyslogClientInstaller(SyslogClientOptions options, string appName, string kind, LogPriority minimalPriority)
    {
        try
        {
            this.Client = new SyslogClient(options, appName);

            this.LogRoute = new SyslogClientLogRoute(this.Client, appName, kind, minimalPriority);

            LocalLogRouter.Router?.InstallLogRoute(this.LogRoute);
        }
        catch
        {
            this._DisposeSafe();

            throw;
        }
    }

    public void Dispose() { this.Dispose(true); GC.SuppressFinalize(this); }
    Once DisposeFlag;
    protected virtual void Dispose(bool disposing)
    {
        if (!disposing || DisposeFlag.IsFirstCall() == false) return;

        try
        {
            // アンインストールの実施
            LocalLogRouter.Router?.UninstallLogRoute(this.LogRoute);
        }
        catch (Exception ex)
        {
            string str = ex.ToString();
            lock (Con.ConsoleWriteLock)
            {
                Console.WriteLine(ex);
            }
        }

        // クライアントの破棄
        this.Client._DisposeSafe();
    }
}


public class SyslogClientOptions
{
    public string SyslogServerHostname { get; }
    public int SyslogServerPort { get; }
    public TcpIpSystem TcpIp { get; }
    public bool PreferIPv6 { get; }

    public readonly Copenhagen<int> MaxQueueLength = 1024;
    public readonly Copenhagen<int> SendTimeoutMsecs = 10 * 1000;

    public SyslogClientOptions(TcpIpSystem? tcpIp, string syslogServerHostname, int syslogServerPort = Consts.Ports.SyslogServerPort, bool preferIPv6=false)
    {
        if (syslogServerPort <= 0) syslogServerPort = Consts.Ports.SyslogServerPort;
        this.TcpIp = tcpIp ?? LocalNet;
        this.SyslogServerHostname = syslogServerHostname._NonNullTrim();
        this.SyslogServerPort = syslogServerPort;
        this.PreferIPv6 = preferIPv6;
    }
}

public class SyslogClient : AsyncServiceWithMainLoop
{
    public SyslogClientOptions Options { get; }
    public TcpIpSystem TcpIp => Options.TcpIp;

    readonly NetUdpListener UdpListener_IPv4;
    readonly DatagramSock UdpSock_IPv4;

    readonly NetUdpListener UdpListener_IPv6;
    readonly DatagramSock UdpSock_IPv6;

    readonly Task MainProcTask;

    readonly AsyncAutoResetEvent EmptyEvent = new AsyncAutoResetEvent(true); // すべてのデータが送付されて空になるたびに発生する非同期イベント

    readonly AsyncAutoResetEvent NewDataArrivedEvent = new AsyncAutoResetEvent(); // 新しいデータが届く度に叩かれるイベント

    readonly AsyncAutoResetEvent FlushNowEvent = new AsyncAutoResetEvent(); // 今すぐ Flush するべき指示のイベント

    readonly ConcurrentQueue<Datagram> SendQueue = new ConcurrentQueue<Datagram>();

    public string AppName { get; }

    public SyslogClient(SyslogClientOptions options, string appName)
    {
        try
        {
            this.AppName = appName._FilledOrDefault(Path.GetFileNameWithoutExtension(Env.AppExecutableExeOrDllFileName));

            this.Options = options;

            this.UdpListener_IPv4 = TcpIp.CreateUdpListener(new NetUdpListenerOptions(TcpDirectionType.Client, new IPEndPoint(IPAddress.Any, 0)));
            this.UdpSock_IPv4 = this.UdpListener_IPv4.GetSocket(true);

            this.UdpListener_IPv6 = TcpIp.CreateUdpListener(new NetUdpListenerOptions(TcpDirectionType.Client, new IPEndPoint(IPAddress.IPv6Any, 0)));
            this.UdpSock_IPv6 = this.UdpListener_IPv6.GetSocket(true);

            this.MainProcTask = this.StartMainLoop(MainLoopAsync);
        }
        catch
        {
            this._DisposeSafe();
            throw;
        }
    }

    protected override async Task CleanupImplAsync(Exception? ex)
    {
        try
        {
            await this.UdpSock_IPv4._DisposeSafeAsync(ex);
            await this.UdpListener_IPv4._DisposeSafeAsync(ex);

            await this.UdpSock_IPv6._DisposeSafeAsync(ex);
            await this.UdpListener_IPv6._DisposeSafeAsync(ex);
        }
        finally
        {
            await base.CleanupImplAsync(ex);
        }
    }

    async Task MainLoopAsync(CancellationToken cancel)
    {
        try
        {
            while (true)
            {
                if (cancel.IsCancellationRequested) return;

                while (true)
                {
                    if (this.SendQueue.TryDequeue(out Datagram? dg) == false)
                    {
                        break;
                    }

                    try
                    {
                        IPAddress ip = await this.TcpIp.GetIpAsync(this.Options.SyslogServerHostname, cancel: cancel, orderBy: ip => (long)ip.AddressFamily * (this.Options.PreferIPv6 ? -1 : 1));

                        DatagramSock? sock = null;

                        if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                        {
                            sock = this.UdpSock_IPv4;
                        }
                        else if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
                        {
                            sock = this.UdpSock_IPv6;
                        }
                        if (sock != null)
                        {
                            await sock.SendDatagramAsync(new Datagram(dg.Data, new IPEndPoint(ip, this.Options.SyslogServerPort)), cancel, this.Options.SendTimeoutMsecs, true);
                        }
                    }
                    catch
                    {
                        await cancel._WaitUntilCanceledAsync(256); // 念のため
                    }
                }

                if (cancel.IsCancellationRequested) return;

                EmptyEvent.Set(true);

                await TaskUtil.WaitObjectsAsync(cancels: cancel._SingleList(), events: this.NewDataArrivedEvent._SingleList());
            }
        }
        finally
        {
            EmptyEvent.Set(true);
        }
    }

    public void SendLog(LogRecord record)
    {
        int severity = LogPriorityToSyslogSeverity(record.Priority);

        string dtStr = GetSyslogDateTimeStr(record.TimeStamp.LocalDateTime);

        string[] lines = record.ConsolePrintableString._GetLines(true, trim: true);

        foreach (var line in lines)
        {
            string tmp = $"<{severity}>{dtStr} {Env.MachineName} {this.AppName}: {line}".Trim();

            SendRaw(tmp._GetBytes_UTF8());
        }
    }

    public void SendRaw(ReadOnlyMemory<byte> data)
    {
        if (this.SendQueue.Count >= this.Options.MaxQueueLength) return;

        this.SendQueue.Enqueue(new Datagram(data._CloneMemory(), new IPEndPoint(IPAddress.Any, 123)));

        this.NewDataArrivedEvent.Set(true);
    }

    public async Task FlushAsync(int timeout = -1, CancellationToken cancel = default)
    {
        if (timeout <= 0) timeout = int.MaxValue;
        long giveupTick = TickNow + timeout;

        while (true)
        {
            if (this.SendQueue.Count == 0) return;
            if (cancel.IsCancellationRequested) return;
            if (TickNow >= giveupTick) return;

            this.NewDataArrivedEvent.Set(true);

            await TaskUtil.WaitObjectsAsync(cancels: cancel._SingleList(), events: EmptyEvent._SingleList(), timeout: 256);
        }
    }

    public static int LogPriorityToSyslogSeverity(LogPriority p)
    {
        switch (p)
        {
            case LogPriority.Trace:
            case LogPriority.Debug:
                return 7;

            case LogPriority.Info:
                return 6;

            case LogPriority.Error:
                return 3;
        }

        return 6;
    }

    public static string GetSyslogDateTimeStr(DateTime dt)
    {
        string month = "Jan";
        switch (dt.Month)
        {
            case 1: month = "Jan"; break;
            case 2: month = "Feb"; break;
            case 3: month = "Mar"; break;
            case 4: month = "Apr"; break;
            case 5: month = "May"; break;
            case 6: month = "Jun"; break;
            case 7: month = "Jul"; break;
            case 8: month = "Aug"; break;
            case 9: month = "Sep"; break;
            case 10: month = "Oct"; break;
            case 11: month = "Nov"; break;
            case 12: month = "Dec"; break;
        }

        return $"{month} {dt.Day:D2} {dt.Hour:D2}:{dt.Minute:D2}:{dt.Second:D2}";
    }
}

public class LogClientOptions
{
    public string ServerHostname { get; }
    public int ServerPort { get; }
    public TcpIpSystem TcpIp { get; }
    public PalSslClientAuthenticationOptions SslAuthOptions { get; }

    public readonly Copenhagen<int> SendKeepAliveInterval = CoresConfig.LogProtocolSettings.DefaultSendKeepAliveInterval.Value;
    public readonly Copenhagen<int> ClientMaxBufferSize = CoresConfig.LogProtocolSettings.BufferingSizeThresholdPerServer.Value;
    public readonly Copenhagen<int> Delay = CoresConfig.LogProtocolSettings.DefaultDelay.Value;
    public readonly Copenhagen<int> RecvTimeout = CoresConfig.LogProtocolSettings.DefaultRecvTimeout.Value;
    public readonly Copenhagen<int> RetryIntervalMin = CoresConfig.LogProtocolSettings.DefaultRetryIntervalMin.Value;
    public readonly Copenhagen<int> RetryIntervalMax = CoresConfig.LogProtocolSettings.DefaultRetryIntervalMax.Value;

    public LogClientOptions(TcpIpSystem? tcpIp, PalSslClientAuthenticationOptions sslAuthOptions, string serverHostname, int serverPort = Consts.Ports.LogServerDefaultServicePort)
    {
        this.ServerHostname = serverHostname._NonNullTrim();
        this.ServerPort = serverPort;
        this.TcpIp = tcpIp ?? LocalNet;
        this.SslAuthOptions = sslAuthOptions;

        if (this.SslAuthOptions.TargetHost._IsEmpty())
        {
            this.SslAuthOptions.TargetHost = this.ServerHostname;
        }
    }
}

public class LogClient : AsyncServiceWithMainLoop
{
    public LogClientOptions Options { get; }

    readonly PipePoint Reader;
    readonly PipePoint Writer;

    readonly Task MainProcTask;

    readonly AsyncAutoResetEvent EmptyEvent = new AsyncAutoResetEvent(true); // すべてのデータが送付されて空になるたびに発生する非同期イベント

    readonly AsyncAutoResetEvent FlushNowEvent = new AsyncAutoResetEvent(); // 今すぐ Flush するべき指示のイベント

    public const int ClientVersion = 1;

    public bool UnderConnectionError { get; private set; } = false;

    public LogClient(LogClientOptions options)
    {
        this.Options = options;

        this.Reader = PipePoint.NewDuplexPipeAndGetOneSide(PipePointSide.A_LowerSide, default, this.Options.ClientMaxBufferSize);
        this.Writer = this.Reader.CounterPart!;

        this.MainProcTask = this.StartMainLoop(MainLoopAsync);
    }

    public void WriteLog(LogJsonData data)
    {
        string str = data._ObjectToJson(compact: true);

        byte[] jsonBuf = str._GetBytes_UTF8();
        if (jsonBuf.Length > CoresConfig.LogProtocolSettings.MaxDataSize)
        {
            return;
        }

        MemoryBuffer<byte> buf = new MemoryBuffer<byte>(jsonBuf.Length + sizeof(int) * 2);
        buf.WriteSInt32((int)LogProtocolDataType.StandardLog);
        buf.WriteSInt32(jsonBuf.Length);
        buf.Write(jsonBuf);

        this.Writer.StreamWriter.NonStopWriteWithLock(buf, true, FastStreamNonStopWriteMode.DiscardWritingData, true);
    }

    public async Task FlushAsync(bool halfFlush = false, CancellationToken cancel = default)
    {
        long threshold = 0;

        if (UnderConnectionError) return;

        if (halfFlush)
        {
            threshold = this.Reader.StreamReader.Threshold / 2;
        }

        while (this.Reader.StreamReader.Length > threshold)
        {
            if (UnderConnectionError) return;

            if (cancel.IsCancellationRequested) return;

            FlushNowEvent.Set(true);

            await EmptyEvent.WaitOneAsync(cancel: cancel);
        }
    }

    async Task MainLoopAsync(CancellationToken cancel)
    {
        try
        {
            int numRetry = 0;
            await using (PipeStream reader = new PipeStream(this.Reader))
            {
                while (true)
                {
                    if (cancel.IsCancellationRequested) return;

                    try
                    {
                        UnderConnectionError = false;

                        await ConnectAndSendAsync(reader, cancel);
                    }
                    catch (Exception ex)
                    {
                        if (cancel.IsCancellationRequested) return;

                        UnderConnectionError = true;
                        EmptyEvent.Set(true);

                        numRetry++;

                        int nextRetryInterval = Util.GenRandIntervalWithRetry(Options.RetryIntervalMin, numRetry, Options.RetryIntervalMax);

                        Con.WriteError($"LogClient: Error (numRetry = {numRetry}, nextWait = {nextRetryInterval}): {ex.ToString()}");

                        await cancel._WaitUntilCanceledAsync(nextRetryInterval);
                    }
                }
            }
        }
        finally
        {
            EmptyEvent.Set(true);
            UnderConnectionError = true;
        }
    }

    async Task ConnectAndSendAsync(PipeStream reader, CancellationToken cancel)
    {
        await using (ConnSock sock = await Options.TcpIp.ConnectIPv4v6DualAsync(new TcpConnectParam(Options.ServerHostname, Options.ServerPort), cancel))
        {
            await using (SslSock ssl = new SslSock(sock))
            {
                await ssl.StartSslClientAsync(Options.SslAuthOptions);

                await using (PipeStream writer = ssl.GetStream())
                {
                    MemoryBuffer<byte> initSendBuf = new MemoryBuffer<byte>();
                    initSendBuf.WriteSInt32(LogServerBase.MagicNumber);
                    initSendBuf.WriteSInt32(ClientVersion);

                    await writer.SendAsync(initSendBuf, cancel);

                    writer.ReadTimeout = this.Options.RecvTimeout;
                    MemoryBuffer<byte> initRecvBuf = await writer.ReceiveAllAsync(sizeof(int) * 2, cancel);
                    writer.ReadTimeout = Timeout.Infinite;

                    int magicNumber = initRecvBuf.ReadSInt32();
                    int serverVersion = initRecvBuf.ReadSInt32();

                    if (magicNumber != LogServerBase.MagicNumber)
                    {
                        throw new ApplicationException($"Invalid magicNumber = 0x{magicNumber:X}");
                    }

                    LocalTimer keepAliveTimer = new LocalTimer();

                    long nextKeepAlive = keepAliveTimer.AddTimeout(Options.SendKeepAliveInterval);

                    while (true)
                    {
                        if (reader.IsReadyToReceive() == false)
                        {
                            EmptyEvent.Set(true);

                            await TaskUtil.WaitObjectsAsync(cancels: cancel._SingleArray(),
                                events: this.FlushNowEvent._SingleArray(),
                                timeout: this.Options.Delay);

                            cancel.ThrowIfCancellationRequested();

                            if (Time.Tick64 >= nextKeepAlive)
                            {
                                nextKeepAlive = keepAliveTimer.AddTimeout(Options.SendKeepAliveInterval);
                                MemoryBuffer<byte> buf = new MemoryBuffer<byte>();
                                buf.WriteSInt32((int)LogProtocolDataType.KeepAlive);
                                await writer.SendAsync(buf, cancel);
                            }
                        }
                        else
                        {
                            RefInt totalRecvSize = new RefInt();
                            IReadOnlyList<ReadOnlyMemory<byte>> data = await reader.FastPeekAsync(totalRecvSize: totalRecvSize);

                            await writer.FastSendAsync(data.ToArray(), cancel, true);

                            await reader.FastReceiveAsync(cancel, maxSize: totalRecvSize);
                        }
                    }
                }
            }
        }
    }


    protected override async Task CleanupImplAsync(Exception? ex)
    {
        try
        {
            await this.MainProcTask._TryAwait(true);

            await this.Reader._DisposeSafeAsync();
            await this.Writer._DisposeSafeAsync();
        }
        finally
        {
            await base.CleanupImplAsync(ex);
        }
    }
}

// LogClient を用いてログを送付するための LogRoute
public class LogClientLogRoute : LogRouteBase
{
    readonly LogClient Client;
    readonly string AppName;

    public LogClientLogRoute(LogClient client, string appName, string kind, LogPriority minimalPriority) : base(kind, minimalPriority)
    {
        this.AppName = appName._NullCheck();
        this.Client = client;
    }

    public override Task FlushAsync(bool halfFlush = false, CancellationToken cancel = default)
    {
        return this.Client.FlushAsync(halfFlush, cancel);
    }

    public override void ReceiveLog(LogRecord record, string kind)
    {
        //Console.WriteLine("Log!");
        //"Log!"._Debug();
        LogJsonData data = new LogJsonData
        {
            TimeStamp = record.TimeStamp,
            Guid = record.Guid._NonNull(),
            MachineName = Env.DnsFqdnHostName,
            AppName = this.AppName,
            Kind = kind._NonNull(),
            Priority = record.Priority.ToString(),
            Tag = record.Tag._NonNull(),
            TypeName = record.Data?.GetType().Name ?? "null",
            Data = record.Data,
        };

        Client.WriteLog(data);
    }
}

// LogClient の稼働を開始させた上で、ローカルのログのルーティングを LogClient に対して適切に設定するためのユーティリティクラス
public class LogClientInstaller : IDisposable
{
    public LogClient Client { get; }
    public LogClientLogRoute LogRoute { get; }

    public LogClientInstaller(LogClientOptions options, string appName, string kind, LogPriority minimalPriority)
    {
        try
        {
            this.Client = new LogClient(options);

            this.LogRoute = new LogClientLogRoute(this.Client, appName, kind, minimalPriority);

            LocalLogRouter.Router?.InstallLogRoute(this.LogRoute);
        }
        catch
        {
            this._DisposeSafe();

            throw;
        }
    }

    public void Dispose() { this.Dispose(true); GC.SuppressFinalize(this); }
    Once DisposeFlag;
    protected virtual void Dispose(bool disposing)
    {
        if (!disposing || DisposeFlag.IsFirstCall() == false) return;

        try
        {
            // アンインストールの実施
            LocalLogRouter.Router?.UninstallLogRoute(this.LogRoute);
        }
        catch (Exception ex)
        {
            string str = ex.ToString();
            lock (Con.ConsoleWriteLock)
            {
                Console.WriteLine(ex);
            }
        }

        // クライアントの破棄
        this.Client._DisposeSafe();
    }
}

#endif  // CORES_BASIC_JSON

