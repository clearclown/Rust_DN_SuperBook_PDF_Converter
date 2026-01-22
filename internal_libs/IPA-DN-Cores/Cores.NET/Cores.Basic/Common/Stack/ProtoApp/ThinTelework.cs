// IPA Cores.NET
// 
// Copyright (c) 2019- IPA CyberLab.
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

// Author: Daiyuu Nobori
// Thin Telework System Protocol Stack

#if true

#pragma warning disable CA2235 // Mark all non-serializable fields

using System;
using System.Buffers;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Serialization;
using System.Net;
using System.Net.Sockets;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using Newtonsoft.Json;

namespace IPA.Cores.Basic;

public static partial class Consts
{
    public static partial class ThinClient
    {
        public const int ProtocolCommTimeoutMsecs = 3 * 60 * 1000;
        public const int DummyClientVer = 18;
        public const int DummyClientBuild = 9876;
        public const int ConnectionQueueLength = 2;
        public const int ReconnectRetrySpanMsecs = 2 * 1000;
        public const int ReconnectRetrySpanMaxMsecs = 3 * 60 * 1000;

        public const int RequestSwitchToWebSocketTimeoutMsecs = 10 * 1000;
        public const int RequestHardTimeoutMsecs = 150 * 1000;
        public const int RequestSoftTimeoutMsecs = 15 * 1000;
        public const int ConnectionQueueWaitTimeout = 10 * 1000;
        public const int ConnectionZeroTimeout = 20 * 1000; // ここで指定された秒数、コネクション数が 0 の場合は、セッションを解除いたします
    }
}

[Flags]
public enum ThinAuthType
{
    None = 0,
    Password = 1,
    UserPassword = 2,
    Cert = 3,
    SmartCard = 4,
    Advanced = 99,
}

[Flags]
public enum ThinSvcType
{
    Rdp = 0,
    Vnc = 1,
}

[Flags]
public enum ThinServerCaps
{
    None = 0,
    Urdp2 = 2,
    UrdpVeryLimited = 4,
    WinRdpEnabled = 8,
    GuacdSupported = 16,
    AudioInSupported = 32,
}

[Flags]
public enum ThinServerMask64 : ulong
{
    None = 0,
    UrdpClient = 1,
    WinRdpNormal = 2,
    WinRdpTs = 4,
    UserMode = 8,
    ServiceMode = 16,
    PolicyEnforced = 32,
    OtpEnabled = 64,
    SupportWolTrigger = 128,
    IsLimitedMode = 256,
}

public class ThinClientMiscParams
{
    public long LifeTime { get; }
    public string LifeTimeMsg { get; } = "";
    public int IdleTimeout { get; }
    public bool IsShareDisabled { get; }
    public bool IsAudioInSupported { get; }

    public ThinClientMiscParams()
    {
    }

    public ThinClientMiscParams(long lifeTime, string lifeTimeMsg, int idleTimeout, bool isShareDisabled, bool isAudioInSupported)
    {
        LifeTime = lifeTime;
        LifeTimeMsg = lifeTimeMsg._NonNull();
        IdleTimeout = idleTimeout;
        IsShareDisabled = isShareDisabled;
        IsAudioInSupported = isAudioInSupported;
    }
}

public class ThinClientAcceptReadyNotification : IDialogRequestData
{
    public string? WebSocketFullUrl;

    public IPEndPoint? ListenEndPoint;
    public ThinClientConnection? FirstConnection;
    public ThinServerCaps Caps;
    public ThinSvcType? SvcType;
    public string ConnectPacketData = "";
    public string WatermarkStr1 = "";
    public string WatermarkStr2 = "";
    public ThinClientMiscParams Misc = new ThinClientMiscParams();
    public bool IsStandaloneMode;
}

public class ThinClientInspectRequest : IDialogRequestData
{
}

public class ThinClientInspectResponse : IDialogResponseData
{
    public bool AntiVirusOk;
    public bool WindowsUpdateOk;
    public string MacAddressList = "";
    public string Ticket = "";
}

public class ThinClientOtpRequest : IDialogRequestData
{
}

public class ThinClientOtpResponse : IDialogResponseData
{
    public string Otp = "";
}

public class ThinClientAuthRequest : IDialogRequestData
{
    public ThinAuthType AuthType;
    public ReadOnlyMemory<byte> Rand;
}

public class ThinClientAuthResponse : IDialogResponseData
{
    public string Password = "";
    public string Username = "";
}

public class ThinClientConnectOptions
{
    public string Pcid { get; }
    public WideTunnelClientOptions ClientOptions { get; }
    public IPAddress ClientIpAddress { get; }
    public string ClientFqdn { get; }
    public object? AppParams { get; }
    public ThinSvcType? ConnectedSvcType { get; private set; } = null;
    public bool DebugGuacMode { get; }
    public GuaPreference? GuaPreference { get; }
    public string? WebSocketUrl { get; private set; } = "/ws/";
    public string ConnectPacketData { get; private set; } = "";
    public string WatermarkStr1 { get; private set; } = "";
    public string WatermarkStr2 { get; private set; } = "";
    public bool IsWebpSupported { get; }
    public ThinServerCaps Caps { get; private set; }
    public ThinClientMiscParams MiscParams { get; private set; } = new ThinClientMiscParams();

    public ThinClientConnectOptions(string pcid, IPAddress clientIp, string clientFqdn, bool debugGuacMode, WideTunnelClientOptions clientOptions, bool isWebpSupported, GuaPreference? guaPreference = null, object? appParams = null)
    {
        this.Pcid = pcid;
        this.ClientOptions = clientOptions;
        this.ClientIpAddress = clientIp;
        this.ClientFqdn = clientFqdn;
        this.AppParams = appParams;
        this.DebugGuacMode = debugGuacMode;
        this.GuaPreference = guaPreference;
        this.IsWebpSupported = isWebpSupported;
    }

    public void UpdateConnectPacketData(string data)
    {
        this.ConnectPacketData = data._NonNull();
    }

    public void UpdateWebSocketUrl(string url)
    {
        this.WebSocketUrl = url;
    }

    public void UpdateConnectedSvcType(ThinSvcType type)
    {
        if (this.ConnectedSvcType == null)
        {
            this.ConnectedSvcType = type;
        }
        else if (this.ConnectedSvcType != type)
        {
            throw new CoresLibException("this.ConnectedSvcType is already set.");
        }
    }

    public void UpdateWatermarkStr(string str1, string str2)
    {
        this.WatermarkStr1 = str1._NonNull();
        this.WatermarkStr2 = str2._NonNull();
    }

    public void UpdateMiscParams(ThinClientMiscParams misc)
    {
        this.MiscParams = misc;
    }

    public void UpdateCaps(ThinServerCaps caps)
    {
        this.Caps = caps;
    }
}

public class ThinClientOptions
{
    public WideTunnelOptions WideTunnelOptions { get; }
    public DialogSessionManager SessionManager { get; }
    public AddressFamily ListenAddressFamily { get; }
    public IPAddress ListenAddress { get; }

    public ThinClientOptions(WideTunnelOptions wideTunnelOptions, DialogSessionManager sessionManager, AddressFamily listenAddressFamily = AddressFamily.InterNetwork, IPAddress? listenAddress = null)
    {
        this.WideTunnelOptions = wideTunnelOptions;
        this.SessionManager = sessionManager;

        this.ListenAddressFamily = listenAddressFamily;
        if (listenAddress == null)
        {
            listenAddress = listenAddressFamily == AddressFamily.InterNetworkV6 ? IPAddress.IPv6Loopback : IPAddress.Loopback;
        }
        this.ListenAddress = listenAddress;
    }
}

public class ThinClientConnection : AsyncService
{
    public WtcSocket Socket { get; }
    public PipeStream Stream { get; }
    public ThinSvcType SvcType { get; }
    public int SvcPort { get; }
    public bool IsShareDisabled { get; }
    public ThinServerCaps Caps { get; }
    public ThinServerMask64 ServerMask64 => (ThinServerMask64)Socket.Options.ConnectParam.ServerMask64;
    public bool RunInspect { get; }
    public string OtpTicket { get; }
    public string InspectTicket { get; }
    public string WatermarkStr1 { get; }
    public string WatermarkStr2 { get; }
    public ThinClientMiscParams Misc { get; }

    public ThinClientConnection(WtcSocket socket, PipeStream stream, ThinSvcType svcType, int svcPort, bool isShareDisabled, ThinServerCaps caps, bool runInspect, string otpTicket, string inspectTicket,
        string watermarkStr1, string watermarkStr2, ThinClientMiscParams misc)
    {
        Socket = socket;
        Stream = stream;
        SvcType = svcType;
        SvcPort = svcPort;
        IsShareDisabled = isShareDisabled;
        Caps = caps;
        RunInspect = runInspect;
        OtpTicket = otpTicket;
        InspectTicket = inspectTicket;
        WatermarkStr1 = watermarkStr1._NonNull();
        WatermarkStr2 = watermarkStr2._NonNull();
        this.Misc = misc;
    }

    protected override async Task CleanupImplAsync(Exception? ex)
    {
        try
        {
            await this.Stream._DisposeSafeAsync();
            await this.Socket._DisposeSafeAsync();
        }
        finally
        {
            await base.CleanupImplAsync(ex);
        }
    }
}

public class ThinClient
{
    public ThinClientOptions Options { get; }
    public DialogSessionManager SessionManager => Options.SessionManager;

    public ThinClient(ThinClientOptions options)
    {
        this.Options = options;
    }

    public DialogSession StartConnect(ThinClientConnectOptions connectOptions, int maxSessionsPerQuotaKey)
    {
        if (connectOptions.ClientOptions.Flags.Bit(WideTunnelClientFlags.WoL))
        {
            throw new CoresLibException("connectOptions.ClientOptions.Flags.Bit(WideTunnelClientFlags.WoL) == true");
        }

        return this.SessionManager.StartNewSession(new DialogSessionOptions(DialogSessionMainProcAsync, maxSessionsPerQuotaKey, connectOptions), connectOptions.ClientIpAddress.ToString());
    }

    public async Task ExecuteWoLAsync(ThinClientConnectOptions connectOptions, string targetPcid, CancellationToken cancel = default)
    {
        if (connectOptions.ClientOptions.Flags.Bit(WideTunnelClientFlags.WoL) == false)
        {
            throw new CoresLibException("connectOptions.ClientOptions.Flags.Bit(WideTunnelClientFlags.WoL) == false");
        }

        await using WideTunnel wt = new WideTunnel(this.Options.WideTunnelOptions);

        // Mac アドレスリストの取得
        string macList = await wt.WideClientGetWoLMacList(targetPcid, cancel);

        // ターゲット PC に叩き起こし依頼
        await using WtcSocket? sock = await wt.WideClientConnectAsync(connectOptions.Pcid, connectOptions.ClientOptions, true, cancel);
        await using PipeStream? st = sock.GetStream(true);

        st.ReadTimeout = st.WriteTimeout = Consts.ThinClient.ProtocolCommTimeoutMsecs;

        Pack p = new Pack();
        p.AddInt("ClientVer", Consts.ThinClient.DummyClientVer);
        p.AddInt("ClientBuild", Consts.ThinClient.DummyClientBuild);
        p.AddBool("WoLMode", true);
        p.AddStr("mac_list", macList);

        await st._SendPackAsync(p, cancel);

        p = await st._RecvPackAsync(cancel);

        var err = p.GetErrorFromPack();

        err.ThrowIfError(p);
    }

    async Task DialogSessionMainProcAsync(DialogSession session, CancellationToken cancel)
    {
        session.Debug("Start");

        ThinClientConnectOptions connectOptions = (ThinClientConnectOptions)session.Param!;

        await using WideTunnel wt = new WideTunnel(this.Options.WideTunnelOptions);

        ThinClientAuthResponse authResponseCache = null!;

        session.Debug($"Begin DcConnectEx. connectOptions: {connectOptions._GetObjectDump()}");

        await using ThinClientConnection firstConnection = await DcConnectEx(wt, connectOptions, cancel, true, true, async (req, c) =>
        {
                // 認証コールバック
                var response = (ThinClientAuthResponse)(await session.RequestAndWaitResponseAsync(req, Consts.ThinClient.RequestHardTimeoutMsecs, Consts.ThinClient.RequestSoftTimeoutMsecs, cancel));

            authResponseCache = response;

            return response;
        },
        async (req, c) =>
        {
                // OTP コールバック
                var response = (ThinClientOtpResponse)(await session.RequestAndWaitResponseAsync(req, Consts.ThinClient.RequestHardTimeoutMsecs, Consts.ThinClient.RequestSoftTimeoutMsecs, cancel));

            return response;
        },
        async (req, c) =>
        {
                // 検疫・MAC アドレスコールバック
                var response = (ThinClientInspectResponse)(await session.RequestAndWaitResponseAsync(req, Consts.ThinClient.RequestHardTimeoutMsecs, Consts.ThinClient.RequestSoftTimeoutMsecs, cancel));

            return response;
        });

        //if (connectOptions.DebugGuacMode == false)
        //{
        //    session.Debug("Switching to WebSocket mode...");
        //    string webSocketUrl = await firstConnection.Socket.RequestSwitchToWebSocketAsync(cancel, Consts.ThinClient.RequestSwitchToWebSocketTimeoutMsecs);
        //    session.Debug($"Switching to WebSocket mode OK. webSocketUrl = {webSocketUrl}");
        //}

        session.Debug("First WideTunnel Connected.");

        if (connectOptions.DebugGuacMode == false)
        {
            // ThinGate ネイティブ WebSocket モード (ThinGate が WebSocket を直接話す)
            if (firstConnection.Caps.Bit(ThinServerCaps.GuacdSupported) == false)
            {
                // 古いバージョンであり、サポートされていない
                throw new VpnException(VpnError.ERR_DESK_GUACD_NOT_SUPPORTED_VER);
            }

            // 'A' を送信
            // この段階でサーバー機の Guacd にソケットが接続された状態となる
            byte[] a = new byte[] { (byte)'A' };
            await firstConnection.Stream.SendAsync(a, cancel);

            // 最初に少し Guacamole Protocol で ThinWebClient と guacd との間で対話を行なう。
            // これを行なうことにより、guacd が、local PC の RDP/VNC に接続し通信を初期化する。
            // その後、WebSocket への切替えを行ない、それ以降は guacd とクライアント Web ブラウザが直接対話する。


            await using var guaClient = new GuaClient(
                new GuaClientSettings(
                    "", 0,
                    firstConnection.SvcType == ThinSvcType.Rdp ? GuaProtocol.Rdp : GuaProtocol.Vnc,
                    "127.0.0.1",
                    firstConnection.SvcPort,
                    connectOptions.GuaPreference!,
                    connectOptions.IsWebpSupported,
                    firstConnection.Caps.Bit(ThinServerCaps.AudioInSupported)),
                firstConnection.Stream);

            string webSocketUrl = "";

            // この非同期 StartAsync は、Guacd Protocol で "select, rdp" のような最初の hello に相当するプロトコルを送付し、
            // その後、最初の応答を受信してから、いくつかの追加パラメータを送付し、最後に、Connect パケットを送付する直前に呼び出される。
            // Connect パケットの本体は文字列にして HTML5 クライアントに渡される。
            // その後、HTML5 クライアントが WebSocket を ThinGate との間で確立した後に、
            // HTML5 クライアントから最初のパケットとして Connect パケットの本体が渡されることになるのである。
            string connectPacketData = await guaClient.StartAsync(cancel);

            webSocketUrl = await firstConnection.Socket.RequestSwitchToWebSocketAsync(cancel, Consts.ThinClient.RequestSwitchToWebSocketTimeoutMsecs);
            $"webSocketUrl = {webSocketUrl}"._Debug();
            webSocketUrl._NotEmptyCheck(nameof(webSocketUrl));

            // フル WebSocket URL を生成する
            var gateEndPoint = firstConnection.Socket.GatePhysicalEndPoint;

            // WebSocket URL を HTML クライアントに通知する
            var ready = new ThinClientAcceptReadyNotification
            {
                WebSocketFullUrl = "wss://" + IPUtil.GenerateWildCardDnsFqdn(gateEndPoint.Address, firstConnection.Socket.Options.ConnectParam.WebSocketWildCardDomainName, "ws-", "") +
                    (gateEndPoint.Port == 443 ? "" : ":" + gateEndPoint.Port.ToString()) +
                    webSocketUrl,

                SvcType = firstConnection.SvcType,
                ConnectPacketData = connectPacketData,
                WatermarkStr1 = firstConnection.WatermarkStr1,
                WatermarkStr2 = firstConnection.WatermarkStr2,
                Misc = firstConnection.Misc,
                Caps = firstConnection.Caps,
                IsStandaloneMode = firstConnection.Socket.Options.ConnectParam.IsStandaloneMode,
            };

            if (firstConnection.Socket.Options.ConnectParam.WebSocketWildCardDomainName._IsSamei("<<!!samehost!!>>"))
            {
                ready.WebSocketFullUrl = webSocketUrl;
            }

            Dbg.Where();
            await session.RequestAndWaitResponseAsync(ready, Consts.ThinClient.RequestHardTimeoutMsecs, Consts.ThinClient.RequestSoftTimeoutMsecs, isFinalAnswer: true);

            // 確立済みの firstConnection は何か 1 バイトでもデータを受信するか切断されるまで待機する
            // (このコネクションは ThinGate からみると用済みのため、新たなデータが届くことはないはずである)
            Dbg.Where("Waiting for unnecessary connection (already switched to WebSocket) disconnects.");
            // タイムアウトは 30 秒であります。
            try
            {
                var data2 = await firstConnection.Stream._ReadAsyncWithTimeout(maxSize: 1024, timeout: 30 * 1000, cancel: cancel);
            }
            catch (Exception ex)
            {
                ex._Debug();
            }
            Dbg.Where("Unnecessary connection (already switched to WebSocket) is disconnected.");
        }
        else
        {
            // Guacd Proxy Mode (デバッグ・開発用)
            await using var abort = new CancelWatcher(cancel);

            AsyncAutoResetEvent connectionAddedEvent = new AsyncAutoResetEvent();
            AsyncAutoResetEvent connectedEvent = new AsyncAutoResetEvent();
            Task? connectionKeepTask = null;
            NetTcpListener? listener = null;

            // このコネクションをキューに追加する
            ConcurrentQueue<ThinClientConnection> connectionQueue = new ConcurrentQueue<ThinClientConnection>();
            try
            {
                connectionQueue.Enqueue(firstConnection);

                // コネクションのキューの長さが ConnectionQueueLength 未満になると自動的にコネクションを張る非同期タスクを開始する
                connectionKeepTask = TaskUtil.StartAsyncTaskAsync(async () =>
                {
                    int numRetry = 0;

                    while (abort.IsCancellationRequested == false)
                    {
                            // キューの長さが少なくなれば追加コネクションを接続する
                            while (abort.IsCancellationRequested == false)
                        {
                            if (connectionQueue.Count < Consts.ThinClient.ConnectionQueueLength)
                            {
                                session.Debug($"connectionQueue.Count ({connectionQueue.Count}) < Consts.ThinClient.ConnectionQueueLength ({Consts.ThinClient.ConnectionQueueLength})");

                                try
                                {
                                        // 追加接続
                                        ThinClientConnection additionalConnection = await DcConnectEx(wt, connectOptions, abort, false, false, async (req, c) =>
                                        {
                                                // 認証コールバック
                                                await Task.CompletedTask;
                                                // キャッシュされた認証情報をそのまま応答
                                                return authResponseCache;
                                        },
                                        async (req, c) =>
                                        {
                                                // OTP コールバック
                                                await Task.CompletedTask;
                                                // OTP チケットを応答
                                                return new ThinClientOtpResponse { Otp = firstConnection.OtpTicket };
                                        },
                                        async (req, c) =>
                                        {
                                                // 検疫・MAC コールバック
                                                await Task.CompletedTask;
                                                // チケットを応答
                                                return new ThinClientInspectResponse { Ticket = firstConnection.InspectTicket };
                                        });

                                        // 接続に成功したのでキューに追加
                                        connectionQueue.Enqueue(additionalConnection);
                                    session.Debug($"Additional WideTunnel Connected. connectionQueue.Count = ({connectionQueue.Count})");

                                    connectionAddedEvent.Set(true);

                                    numRetry = 0;
                                }
                                catch (Exception ex)
                                {
                                        // 接続に失敗したので一定時間待ってリトライする
                                        ex._Debug();

                                    session.Error(ex._GetObjectDump());

                                    numRetry++;

                                    int waitTime = Util.GenRandIntervalWithRetry(Consts.ThinClient.ReconnectRetrySpanMsecs, numRetry, Consts.ThinClient.ReconnectRetrySpanMaxMsecs);
                                    if (waitTime == 0) waitTime = 1;

                                    session.Debug($"Additional tunnel establish failed. numRetry = {numRetry}. Wait for {waitTime} msecs...");

                                    await connectedEvent.WaitOneAsync(waitTime, abort);
                                }
                            }
                            else
                            {
                                break;
                            }
                        }

                        if (abort.IsCancellationRequested) break;

                        await connectedEvent.WaitOneAsync(1000, abort);
                    }
                });

                // Listen ソケットの開始
                listener = LocalNet.CreateTcpListener(new TcpListenParam(isRandomPortMode: EnsureSpecial.Yes, async (listen, sock) =>
                {
                    session.Debug($"Listener ({sock.EndPointInfo._GetObjectDump()}) Accepted.");

                    ThinClientConnection? connection = null;
                    long giveupTick = TickNow + Consts.ThinClient.ConnectionQueueWaitTimeout;
                        // キューにコネクションが貯まるまで待機する
                        while (connectionQueue.TryDequeue(out connection) == false)
                    {
                        if (TickNow >= giveupTick)
                        {
                            session.Debug($"Listener ({sock.EndPointInfo._GetObjectDump()}): TickNow ({TickNow}) >= giveupTick ({giveupTick})");
                            return;
                        }

                        abort.ThrowIfCancellationRequested();
                        await connectionAddedEvent.WaitOneAsync(100, abort.CancelToken);
                    }
                    await using ThinClientConnection connection2 = connection;

                    session.Debug($"Listener ({sock.EndPointInfo._GetObjectDump()}) Starting Relay Operation.");

                        // 'A' を送信
                        byte[] a = new byte[] { (byte)'A' };
                    await connection.Stream.SendAsync(a, abort);

                    var sockStream = sock.GetStream(true);

                    RefLong totalBytes = new RefLong();

                    try
                    {
                        await Util.RelayDuplexStreamAsync(sockStream, connection.Stream, abort, totalBytes: totalBytes);
                    }
                    catch (Exception ex)
                    {
                        session.Debug($"Listener ({sock.EndPointInfo._GetObjectDump()}) RelayDuplexStreamAsync Exception = {ex._GetObjectDump()}");
                    }

                    session.Debug($"Listener ({sock.EndPointInfo._GetObjectDump()}) Finished Relay Operation. Total Bytes = {totalBytes.Value}");

                        // RDP の場合はダミーデータを最後に流す
                        if (connection.SvcType == ThinSvcType.Rdp)
                    {
                        byte[] dummySize = new byte[4096];
                        await sockStream.SendAsync(dummySize, abort);
                        await Task.Delay(50, abort);
                    }

                }, family: Options.ListenAddressFamily, address: Options.ListenAddress));

                session.Debug($"Create Listener. Assigned Random Port = {listener.AssignedRandomPort}");

                // 接続が完了いたしました
                var ready = new ThinClientAcceptReadyNotification
                {
                    ListenEndPoint = new IPEndPoint(listener.AssignedRandomPort!.IPAddress, listener.AssignedRandomPort.Port),
                    FirstConnection = firstConnection,
                    Caps = firstConnection.Caps,
                };

                Dbg.Where();
                await session.RequestAndWaitResponseAsync(ready, Consts.ThinClient.RequestHardTimeoutMsecs, Consts.ThinClient.RequestSoftTimeoutMsecs, abort, isFinalAnswer: true);

                // コネクション数が 0 の状態が 30 秒以上継続した場合は終了します
                long connectionZeroStartTick = 0;
                while (true)
                {
                    abort.ThrowIfCancellationRequested();

                    long now = TickNow;

                    //$"listener.CurrentConnections = {listener.CurrentConnections}"._Debug();
                    if (listener.CurrentConnections == 0)
                    {
                        if (connectionZeroStartTick == 0) connectionZeroStartTick = now;
                    }
                    else
                    {
                        connectionZeroStartTick = 0;
                    }

                    if (connectionZeroStartTick != 0 && now >= (connectionZeroStartTick + Consts.ThinClient.ConnectionZeroTimeout))
                    {
                        session.Debug($"All client connections disconnected. No more connection exists. connectionZeroStartTick = {connectionZeroStartTick}, now = {now}");
                        throw new CoresException("All client connections disconnected. No more connection exists.");
                    }

                    await abort.CancelToken._WaitUntilCanceledAsync(1000);
                }
            }
            catch (Exception ex)
            {
                session.Error(ex);
                throw;
            }
            finally
            {
                abort.Cancel();

                await connectionKeepTask._TryAwait(noDebugMessage: true);
                await listener._DisposeSafeAsync();

                foreach (var connection in connectionQueue)
                {
                    await connection._DisposeSafeAsync();
                }

                Dbg.Where();
            }
        }
    }

    async Task<ThinClientConnection> DcConnectEx(WideTunnel wt, ThinClientConnectOptions connectOptions, CancellationToken cancel, bool checkPort, bool firstConnection,
        Func<ThinClientAuthRequest, CancellationToken, Task<ThinClientAuthResponse>> authCallback,
        Func<ThinClientOtpRequest, CancellationToken, Task<ThinClientOtpResponse>> otpCallback,
        Func<ThinClientInspectRequest, CancellationToken, Task<ThinClientInspectResponse>> inspectCallback)
    {
        string otpTicket = "";
        string inspectTicket = "";
        WtcSocket? sock = null;
        PipeStream? st = null;

        if (checkPort == false && connectOptions.DebugGuacMode == false)
        {
            throw new CoresLibException("checkPort == false && connectOptions.DebugGuacMode == false");
        }

        try
        {
            sock = await wt.WideClientConnectAsync(connectOptions.Pcid, connectOptions.ClientOptions, false, cancel);
            st = sock.GetStream(true);

            st.ReadTimeout = st.WriteTimeout = Consts.ThinClient.ProtocolCommTimeoutMsecs;

            // バージョンを送信
            Pack p = new Pack();
            p.AddInt("ClientVer", Consts.ThinClient.DummyClientVer);
            p.AddInt("ClientBuild", Consts.ThinClient.DummyClientBuild);
            p.AddBool("CheckPort", checkPort);
            p.AddBool("FirstConnection", firstConnection);
            p.AddBool("HasURDP2Client", true);
            p.AddBool("SupportOtp", true);
            p.AddBool("SupportOtpEnforcement", true);
            p.AddBool("SupportInspect", true);
            p.AddBool("SupportServerAllowedMacListErr", true);
            p.AddIp("ClientLocalIP", connectOptions.ClientIpAddress);
            p.AddUniStr("UserName", "HTML5 WebClient");
            p.AddUniStr("ComputerName", "HTML5 WebClient");
            p.AddBool("SupportWatermark", true);
            p.AddBool("GuacdMode", connectOptions.DebugGuacMode == false);

            GuaDnFlags flags = GuaDnFlags.None;
            if (connectOptions.GuaPreference?.EnableAlwaysWebp ?? false) flags |= GuaDnFlags.AlwaysWebp;
            p.AddInt("GuacdFlags", (uint)flags);

            await st._SendPackAsync(p, cancel);

            // 認証パラメータを受信
            p = await st._RecvPackAsync(cancel);

            var err = p.GetErrorFromPack();
            err.ThrowIfError(p);

            var authType = (ThinAuthType)p["AuthType"].SIntValueSafeNum;
            var svcType = (ThinSvcType)p["ServiceType"].SIntValueSafeNum;
            var svcPort = (int)p["ServicePort"].SIntValueSafeNum;
            var caps = (ThinServerCaps)p["DsCaps"].SIntValueSafeNum;
            var rand = p["Rand"].DataValueNonNull;
            var machineKey = p["MachineKey"].DataValueNonNull;
            var isShareDisabled = p["IsShareDisabled"].BoolValue;
            var useAdvancedSecurity = p["UseAdvancedSecurity"].BoolValue;
            var isOtpEnabled = p["IsOtpEnabled"].BoolValue;
            var runInspect = p["RunInspect"].BoolValue;
            var lifeTime = p["Lifetime"].SInt64Value;
            var lifeTimeMsg = p["LifeTimeMsg"].UniStrValueNonNull;
            var waterMarkStr1 = p["WatermarkStr1"].UniStrValueNonNull;
            var waterMarkStr2 = p["WatermarkStr2"].UniStrValueNonNull;
            var idleTimeout = p["IdleTimeout"].SIntValueSafeNum;
            var isLimitedFirewallMandated = p["IsLimitedFirewallMandated"].BoolValue;

            if (isLimitedFirewallMandated)
            {
                // 完全閉域化 FW が強制されているが HTML5 版ではサポートされていない
                throw new VpnException(VpnError.ERR_DESK_GOVFW_HTML5_NO_SUPPORT);
            }

            if (isOtpEnabled)
            {
                // OTP 認証
                ThinClientOtpRequest otpReq = new ThinClientOtpRequest();

                var otpRes = await otpCallback(otpReq, cancel);

                p = new Pack();
                p.AddStr("Otp", otpRes.Otp._NonNull());
                await st._SendPackAsync(p, cancel);

                // 結果を受信
                p = await st._RecvPackAsync(cancel);
                p.ThrowIfError();

                // OTP チケットを保存
                otpTicket = p["OtpTicket"].StrValueNonNull;
            }

            if (runInspect)
            {
                // 検疫および MAC アドレス認証
                ThinClientInspectRequest inspectReq = new ThinClientInspectRequest();

                var inspectRes = await inspectCallback(inspectReq, cancel);

                p = new Pack();
                p.AddBool("AntiVirusOk", inspectRes.AntiVirusOk);
                p.AddBool("WindowsUpdateOk", inspectRes.WindowsUpdateOk);
                p.AddStr("MacAddressList", inspectRes.MacAddressList._NonNull());
                p.AddStr("Ticket", inspectRes.Ticket._NonNull());
                await st._SendPackAsync(p, cancel);

                // 結果を受信
                p = await st._RecvPackAsync(cancel);
                p.ThrowIfError();

                // OTP チケットを保存
                inspectTicket = p["InspectionTicket"].StrValueNonNull;
            }

            // ユーザー認証
            p = new Pack();

            if (useAdvancedSecurity == false)
            {
                // 古いユーザー認証
                if (authType == ThinAuthType.None)
                {
                    // 匿名認証
                    ThinClientAuthRequest authReq = new ThinClientAuthRequest
                    {
                        AuthType = ThinAuthType.None,
                    };

                    var authRes = await authCallback(authReq, cancel);
                }
                else if (authType == ThinAuthType.Password)
                {
                    // パスワード認証
                    ThinClientAuthRequest authReq = new ThinClientAuthRequest
                    {
                        AuthType = ThinAuthType.Password,
                    };

                    var authRes = await authCallback(authReq, cancel);

                    var passwordHash = Secure.HashSHA1(authRes.Password._GetBytes_UTF8());

                    p.AddData("SecurePassword", Secure.SoftEther_SecurePassword(passwordHash, rand));
                }
                else
                {
                    // そのような認証方法はサポートしていない
                    throw new VpnException(VpnError.ERR_AUTHTYPE_NOT_SUPPORTED);
                }
            }
            else
            {
                // 高度なユーザー認証
                ThinClientAuthRequest authReq = new ThinClientAuthRequest
                {
                    AuthType = ThinAuthType.Advanced,
                };

                var authRes = await authCallback(authReq, cancel);

                p.AddInt("authtype", 2); // CLIENT_AUTHTYPE_PLAIN_PASSWORD
                p.AddStr("username", authRes.Username);
                p.AddStr("plain_password", authRes.Password);
            }

            await st._SendPackAsync(p, cancel);

            // 結果を受信
            p = await st._RecvPackAsync(cancel);
            err = p.GetErrorFromPack();
            err.ThrowIfError(p);

            st.ReadTimeout = st.WriteTimeout = Timeout.Infinite;

            var misc = new ThinClientMiscParams(lifeTime, lifeTimeMsg, idleTimeout, isShareDisabled, caps.Bit(ThinServerCaps.AudioInSupported));

            return new ThinClientConnection(sock, st, svcType, svcPort, isShareDisabled, caps, runInspect, otpTicket, inspectTicket, waterMarkStr1, waterMarkStr2, misc);
        }
        catch
        {
            await st._DisposeSafeAsync();
            await sock._DisposeSafeAsync();
            throw;
        }
    }
}

#endif

