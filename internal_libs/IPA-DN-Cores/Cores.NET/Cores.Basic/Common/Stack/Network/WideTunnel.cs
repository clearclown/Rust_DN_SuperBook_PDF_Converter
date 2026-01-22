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
// WideTunnel Protocol Stack

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
using System.Security.Cryptography.X509Certificates;
using System.Net.Security;

namespace IPA.Cores.Basic;

public static partial class Consts
{
    public static partial class WideTunnelConsts
    {
        public const int TunnelTimeout = 30 * 1000;
        public const int TunnelKeepAlive = 10 * 1000;

        public const int TunnelTimeoutHardMax = 5 * 60 * 1000;
        public const int TunnelKeepAliveHardMax = 2 * 60 * 1000;

        public const int MaxBlockSize = 65536;

        public const int SpecialOpCode_Min = 16777000;
        public const int SpecialOpCode_Max = 16777215;

        public const int SpecialOpCode_C2S_SwitchToWebSocket_Request_Guacd = 16777001;
        public const int SpecialOpCode_S2C_SwitchToWebSocket_Ack = 16777002;
    }
}

public static partial class CoresConfig
{
    public static partial class WtcConfig
    {
        public static readonly Copenhagen<int> PseudoVer = 9999;
        public static readonly Copenhagen<int> PseudoBuild = 9999;

        public static readonly Copenhagen<int> WpcTimeoutMsec = 15 * 1000;
        public static readonly Copenhagen<int> ConnectingTimeoutMsec = 15 * 1000;

        public static readonly Copenhagen<ReadOnlyMemory<byte>> DefaultWaterMark = (ReadOnlyMemory<byte>)Str.CHexArrayToBinary(CoresRes["210101_DefaultWaterMark.txt"].String);
    }
}

public class HttpHeader
{
    public string Method = "";
    public string Target = "";
    public string Version = "";
    public KeyValueList<string, string> ValueList = new KeyValueList<string, string>();

    public HttpHeader() { }

    public HttpHeader(string method, string target, string version)
    {
        this.Method = method;
        this.Target = target;
        this.Version = version;
    }

    public string WriteHttpHeader(KeyValueList<string, string>? headerValues = null)
    {
        StringWriter w = new StringWriter();
        w.NewLine = Str.NewLine_Str_Windows;

        if (headerValues == null) headerValues = this.ValueList;

        w.WriteLine($"{this.Method} {this.Target} {this.Version}");

        foreach (var kv in headerValues)
        {
            w.WriteLine($"{kv.Key}: {kv.Value}");
        }

        w.WriteLine();

        return w.ToString();
    }

    public Memory<byte> GeneratePostBinary(ReadOnlyMemory<byte> postData)
    {
        var list = this.ValueList.Clone();

        list._SetSingle("Content-Length", postData.Length.ToString());

        string headerBody = WriteHttpHeader(list);

        MemoryBuffer<byte> buf = new MemoryBuffer<byte>();
        buf.Write(headerBody._GetBytes());
        buf.Write(postData);

        return buf.Memory;
    }

    public async Task PostHttpAsync(Stream stream, ReadOnlyMemory<byte> postData, CancellationToken cancel = default)
    {
        await stream.WriteAsync(GeneratePostBinary(postData), cancel);
    }

    public static async Task<HttpHeader> RecvHttpHeaderAsync(Stream stream, CancellationToken cancel = default)
    {
        // Get the first line
        string firstLine = await stream._ReadLineAsync(cancel: cancel);
        firstLine = firstLine.Trim();

        // Split into tokens
        string[] tokens = firstLine._Split(StringSplitOptions.None, ' ');
        if (tokens.Length < 3)
        {
            throw new CoresLibException($"tokens.Length < 3 ('{firstLine}')");
        }

        var header = new HttpHeader(tokens[0], tokens[1], tokens[2]);

        if (header.Version._IsSamei("HTTP/0.9"))
        {
            // The header ends with this line
            return header;
        }

        // Get the subsequent lines
        while (true)
        {
            string line = await stream._ReadLineAsync(cancel: cancel);

            if (line.Length == 0)
            {
                // End of the header
                break;
            }

            line = line.Trim();

            if (line._GetKeyAndValue(out string key, out string value, ":"))
            {
                header.ValueList.Add(key, value);
            }
        }

        return header;
    }
}

public class WtcOptions : NetMiddleProtocolOptionsBase
{
    public WideTunnel WideTunnel { get; }
    public WtConnectParam ConnectParam { get; }

    public WtcOptions(WideTunnel wt, WtConnectParam param)
    {
        this.WideTunnel = wt;
        this.ConnectParam = param;
    }
}

public class NetWtcProtocolStack : NetMiddleProtocolStackBase
{
    public new WtcOptions Options => (WtcOptions)base.Options;

    public WideTunnel WideTunnel => Options.WideTunnel;

    // 特殊リクエスト処理系
    readonly AsyncAutoResetEvent C2S_SpecialOp_Event = new AsyncAutoResetEvent();
    volatile bool C2S_SwitchToWebSocket_Requested = false;
    volatile bool S2C_SwitchToWebSocket_Completed = false;
    readonly AsyncAutoResetEvent S2C_SoecialOp_Completed_Event = new AsyncAutoResetEvent();
    string WebSocketUrl = "";

    PipeStream LowerStream;
    PipeStream UpperStream;

    public NetWtcProtocolStack(PipePoint lower, PipePoint? upper, NetMiddleProtocolOptionsBase options, CancellationToken cancel = default) : base(lower, upper, options, cancel)
    {
        this.LowerStream = this.LowerAttach.GetStream();
        this.UpperStream = this.UpperAttach.GetStream();
    }

    Once Started;

    Task? SendTask;
    Task? RecvTask;

    public async Task StartWtcAsync(CancellationToken cancel = default)
    {
        if (Started.IsFirstCall() == false)
        {
            throw new ApplicationException("Already started.");
        }

        LowerStream.ReadTimeout = CoresConfig.WtcConfig.ConnectingTimeoutMsec;
        LowerStream.WriteTimeout = CoresConfig.WtcConfig.ConnectingTimeoutMsec;

        // シグネチャをアップロードする
        HttpHeader h = new HttpHeader("POST", "/widetunnel/connect.cgi", "HTTP/1.1");
        h.ValueList.Add("Content-Type", "image/jpeg");
        h.ValueList.Add("Connection", "Keep-Alive");

        // 透かしデータのアップロード
        int randSize = Util.RandSInt31() % 2000;
        MemoryBuffer<byte> water = new MemoryBuffer<byte>();
        water.Write(this.WideTunnel.Options.WaterMark);
        water.Write(Util.Rand(randSize));
        await h.PostHttpAsync(LowerStream, water, cancel);

        // Hello パケットのダウンロード
        var hello = await LowerStream._HttpClientRecvPackAsync(cancel);
        if (hello["hello"].BoolValue == false)
        {
            throw new CoresLibException("Hello packet recv error.");
        }

        // 接続パラメータの送信
        var p = new Pack();
        p.AddData("client_id", WideTunnel.Options.ClientId);
        p.AddStr("method", "connect_session");
        p.AddBool("use_compress", false);
        p.AddData("session_id", this.Options.ConnectParam.SessionId);
        p.AddSInt("ver", CoresConfig.WtcConfig.PseudoVer);
        p.AddSInt("build", CoresConfig.WtcConfig.PseudoBuild);
        p.AddStr("name_suite", this.WideTunnel.Options.NameSuite);
        p.AddBool("support_timeout_param", true);

        var clientOptions = Options.ConnectParam.ClientOptions;
        if (clientOptions.RealClientIp._IsFilled())
        {
            p.AddBool("is_trusted", true);
            p.AddStr("trusted_real_client_ip", clientOptions.RealClientIp._NonNullTrim());
            p.AddStr("trusted_real_client_fqdn", clientOptions.RealClientFqdn._NonNullTrim());
            p.AddInt("trusted_real_client_port", (uint)clientOptions.RealClientPort);

            string auth_str = $"{clientOptions.RealClientIp._NonNullTrim()}/{clientOptions.RealClientFqdn._NonNullTrim()}/{(uint)clientOptions.RealClientPort}/{this.WideTunnel.Options.ControllerGateSecretKey}";
            p.AddData("trusted_auth_sha1", auth_str._HashSHA1());
        }

        await LowerStream._HttpClientSendPackAsync(p, cancel);

        // 結果の受信
        p = await LowerStream._HttpClientRecvPackAsync(cancel);
        int code = p["code"].SIntValue;
        VpnError err = (VpnError)code;
        err.ThrowIfError(p);

        int tunnelTimeout = Consts.WideTunnelConsts.TunnelTimeout;
        int tunnelKeepalive = Consts.WideTunnelConsts.TunnelKeepAlive;
        bool tunnelUseAggressiveTimeout = false;

        int tunnelTimeout2 = p["tunnel_timeout"].SIntValue;
        int tunnelKeepAlive2 = p["tunnel_keepalive"].SIntValue;
        bool tunnelUseAggressiveTimeout2 = p["tunnel_use_aggressive_timeout"].BoolValue;

        if (tunnelTimeout2 >= 1 && tunnelKeepAlive2 >= 1)
        {
            tunnelTimeout = tunnelTimeout2;
            tunnelKeepalive = tunnelKeepAlive2;
            tunnelUseAggressiveTimeout = tunnelUseAggressiveTimeout2;
        }

        this.WebSocketUrl = p["websocket_url"].StrValue!;
        if (this.WebSocketUrl._IsEmpty()) throw new CoresLibException("This version of the destination ThinGate has no WebSocket capability.");

        LowerStream.ReadTimeout = tunnelTimeout;
        LowerStream.WriteTimeout = Timeout.Infinite;

        this.SendTask = SendLoopAsync(tunnelKeepalive)._LeakCheck();
        this.RecvTask = RecvLoopAsync()._LeakCheck();
    }

    public async Task<string> RequestSwitchToWebSocketAsync(CancellationToken cancel = default, int timeout = -1)
    {
        if (this.C2S_SwitchToWebSocket_Requested) throw new CoresLibException("Already requested.");

        cancel.ThrowIfCancellationRequested();
        this.GrandCancel.ThrowIfCancellationRequested();

        this.C2S_SwitchToWebSocket_Requested = true;
        this.C2S_SpecialOp_Event.Set(true);

        await using (TaskUtil.CreateCombinedCancellationToken(out CancellationToken c2, cancel, this.GrandCancel))
        {
            await S2C_SoecialOp_Completed_Event.WaitOneAsync(timeout: timeout, cancel: c2);
        }

        if (this.S2C_SwitchToWebSocket_Completed == false)
        {
            throw new CoresException("RequestSwitchToWebSocketAsync timed out.");
        }

        return this.WebSocketUrl;
    }

    async Task SendLoopAsync(int keepaliveInterval)
    {
        try
        {
            CancellationToken cancel = this.GrandCancel;

            LocalTimer timer = new LocalTimer();

            long nextPingTick = timer.AddTimeout(0);

            bool initialFourZeroSent = false;

            bool localFlag_C2S_SwitchToWebSocket_Requested = false;

            while (true)
            {
                MemoryBuffer<byte> sendBuffer = new MemoryBuffer<byte>();

                if (initialFourZeroSent == false)
                {
                    // 最初の 0x00000000 (4 バイト) を送信
                    initialFourZeroSent = true;
                    sendBuffer.WriteSInt32(4);
                    sendBuffer.WriteSInt32(0x00000000);
                }

                // 上位ストリームからのデータを送信
                IReadOnlyList<ReadOnlyMemory<byte>> userDataList = UpperStream.FastReceiveNonBlock(out int totalSendSize, maxSize: Consts.WideTunnelConsts.MaxBlockSize);

                if (totalSendSize >= 1)
                {
                    // Send data
                    foreach (var mem in userDataList)
                    {
                        //$"Send: {mem.Length}"._Debug();
                        //$"SendData: {mem._GetHexString()}"._Debug();
                        sendBuffer.WriteSInt32(mem.Length);
                        sendBuffer.Write(mem);
                    }
                }

                if (timer.Now >= nextPingTick)
                {
                    // Send ping
                    sendBuffer.WriteSInt32(0);

                    nextPingTick = timer.AddTimeout(Util.GenRandInterval(keepaliveInterval));
                }

                if (localFlag_C2S_SwitchToWebSocket_Requested == false && this.C2S_SwitchToWebSocket_Requested)
                {
                    // Web socket switch request invoked (only once)
                    $"Web socket switch request invoked"._Debug();
                    localFlag_C2S_SwitchToWebSocket_Requested = true;
                    sendBuffer.WriteSInt32(Consts.WideTunnelConsts.SpecialOpCode_C2S_SwitchToWebSocket_Request_Guacd);
                }

                if (sendBuffer.IsThisEmpty() == false)
                {
                    //$"RawSendData: {sendBuffer.Span._GetHexString()}"._Debug();
                    LowerStream.FastSendNonBlock(sendBuffer);
                }

                await LowerStream.WaitReadyToSendAsync(cancel, Timeout.Infinite);

                await UpperStream.WaitReadyToReceiveAsync(cancel, timer.GetNextInterval(), noTimeoutException: true, cancelEvent: C2S_SpecialOp_Event);
            }
        }
        catch (Exception ex)
        {
            //                ex._Debug();
            this.UpperStream.Disconnect();
            await this.CleanupAsync(ex);
        }
    }

    async Task RecvLoopAsync()
    {
        try
        {
            CancellationToken cancel = this.GrandCancel;

            while (true)
            {
                // データサイズを受信
                int dataSize = await LowerStream.ReceiveSInt32Async(cancel).FlushOtherStreamIfPending(UpperStream);

                if (dataSize >= Consts.WideTunnelConsts.SpecialOpCode_Min && dataSize < Consts.WideTunnelConsts.SpecialOpCode_Max)
                {
                    // 特殊コード受信
                    int code = dataSize;

                    if (code == Consts.WideTunnelConsts.SpecialOpCode_S2C_SwitchToWebSocket_Ack)
                    {
                        // WebSocket 切替え完了応答
                        this.S2C_SwitchToWebSocket_Completed = true;
                        this.S2C_SoecialOp_Completed_Event.Set(true);
                    }

                    // ループの最初に戻る
                    continue;
                }
                else if (dataSize < 0 || dataSize > Consts.WideTunnelConsts.MaxBlockSize)
                {
                    // 不正なデータサイズを受信。通信エラーか
                    throw new CoresLibException($"dataSize < 0 || dataSize > Consts.WideTunnelConsts.MaxBlockSize ({dataSize})");
                }

                // データ本体を受信
                var data = await LowerStream.ReceiveAllAsync(dataSize, cancel);

                // データが 1 バイト以上あるか (0 バイトの場合は Keep Alive であるので無視する)
                if (data.IsEmpty == false)
                {
                    // 上位レイヤに渡す
                    await UpperStream.WaitReadyToSendAsync(cancel, Timeout.Infinite);

                    UpperStream.FastSendNonBlock(data, false);
                }
            }

        }
        catch (Exception ex)
        {
            //                ex._Debug();
            this.UpperStream.Disconnect();
            await this.CleanupAsync(ex);
        }
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

public class WtcSocket : MiddleConnSock
{
    protected new NetWtcProtocolStack Stack => (NetWtcProtocolStack)base.Stack;
    public WtcOptions Options => Stack.Options;
    public IPEndPoint GatePhysicalEndPoint { get; }

    public WtcSocket(ConnSock lowerSock, WtcOptions options) : base(new NetWtcProtocolStack(lowerSock.UpperPoint, null, options))
    {
        try
        {
            this.GatePhysicalEndPoint = new IPEndPoint(IPAddress.Parse(lowerSock.EndPointInfo.RemoteIP!), lowerSock.EndPointInfo.RemotePort);
        }
        catch (Exception ex)
        {
            this._DisposeSafe(ex);
            throw;
        }
    }

    public async Task StartWtcAsync(CancellationToken cancel = default)
        => await Stack.StartWtcAsync(cancel);

    public async Task<string> RequestSwitchToWebSocketAsync(CancellationToken cancel = default, int timeout = -1)
    {
        return await this.Stack.RequestSwitchToWebSocketAsync(cancel, timeout);
    }
}

[Serializable]
public class WideTunnelClientOptions
{
    public WideTunnelClientFlags Flags { get; }
    public string RealClientIp { get; }
    public string RealClientFqdn { get; }
    public int RealClientPort { get; }

    public WideTunnelClientOptions(WideTunnelClientFlags flags, string realClientIp, string realClientFqdn, int realClientPort)
    {
        Flags = flags;
        RealClientIp = realClientIp;
        RealClientFqdn = realClientFqdn;
        RealClientPort = realClientPort;
    }
}

[Flags]
public enum WideTunnelClientFlags : uint
{
    None = 0,
    WoL = 1,
}

[Flags]
public enum ProxyType
{
    Direct = 0,
    Http,
    Socks,
}

[Serializable]
public class WtConnectParam
{
    public string HostName = "";
    public string HostNameForProxy = "";
    public int Port;
    public ProxyType ProxyType;
    public string ProxyHostname = "";
    public int ProxyPort;
    public string ProxyUsername = "";
    public string ProxyPassword = "";
    public string ProxyUserAgent = "";
    public string MsgForServer = "";
    public bool MsgForServerOnce;
    public long SessionLifeTime;
    public string SessionLifeTimeMsg = "";
    public string WebSocketWildCardDomainName = "";
    public bool IsStandaloneMode;

    public WideTunnelClientOptions ClientOptions = null!;

    // Client 用
    public byte[] SessionId = new byte[0];
    public ulong ServerMask64;
    public bool CacheUsed;
}

public class WideTunnelOptions
{
    public string SvcName { get; }
    public ReadOnlyMemory<byte> ClientId { get; }
    public IEnumerable<string> EntryUrlList { get; }
    public TcpIpSystem TcpIp { get; }
    public WebApiOptions? WebApiOptions { get; }
    public ReadOnlyMemory<byte> WaterMark { get; }
    public string NameSuite { get; }
    public IEnumerable<Certificate> MasterCertificates { get; }
    public string ControllerGateSecretKey { get; }

    public ReadOnlyMemory<byte> GenNewClientId() => Secure.Rand(20);

    public WideTunnelOptions(string svcName, string nameSuite, IEnumerable<string> entryUrlList, IEnumerable<Certificate> masterCertificates, ReadOnlyMemory<byte> clientId = default, ReadOnlyMemory<byte> waterMark = default, WebApiOptions? webApiOptions = null, string? controllerGateSecretKey = null, TcpIpSystem? tcpIp = null)
    {
        if (clientId.IsEmpty) clientId = GenNewClientId();
        if (waterMark.IsEmpty) waterMark = CoresConfig.WtcConfig.DefaultWaterMark;
        if (webApiOptions == null) webApiOptions = new WebApiOptions(
            new WebApiSettings
            {
                AllowAutoRedirect = false,
                Timeout = CoresConfig.WtcConfig.WpcTimeoutMsec,
            },
            tcpIp, false);

        if (clientId.Length != 20) throw new ArgumentException($"{nameof(clientId)} must be 20 bytes.");
        if (tcpIp == null) tcpIp = LocalNet;

        this.NameSuite = nameSuite;

        SvcName = svcName._NonNullTrim();
        ClientId = clientId;
        this.EntryUrlList = entryUrlList.Distinct(StrComparer.IgnoreCaseTrimComparer).OrderBy(x => x, StrComparer.IgnoreCaseTrimComparer).ToList();
        this.TcpIp = tcpIp;
        this.WebApiOptions = webApiOptions;
        this.WaterMark = waterMark;
        this.MasterCertificates = masterCertificates;
        this.ControllerGateSecretKey = controllerGateSecretKey._NonNullTrim();
    }
}

public class VpnException : CoresException
{
    public VpnError Error { get; }

    public string RecvMsg { get; } = "";
    public string RecvUrl { get; } = "";

    public bool HasRecvMsg => this.RecvMsg._IsFilled();
    public bool HasRecvUrl => this.RecvUrl._IsFilled();

    public Pack? Pack { get; }

    static string ErrorToStr(VpnError err)
    {
        return $"Error code {(int)err}: {err.EnumToStrExact("Unknown Error")}";
    }

    public VpnException(VpnError error, Pack? pack = null) : base($"{ErrorToStr(error)}")
    {
        this.Error = error;
        this.Pack = pack?.Clone() ?? null;

        if (this.Pack != null)
        {
            if (error == VpnError.ERR_RECV_MSG)
            {
                // メッセージ付きエラー
                this.RecvMsg = this.Pack["Msg"].UniStrValueNonNull;
            }

            if (error == VpnError.ERR_RECV_URL)
            {
                // URL 付きエラー
                this.RecvUrl = this.Pack["Url"].StrValueNonNull;
            }
        }
    }

    public string GetFriendlyVpnErrorMessage(StrTable table)
    {
        string str = table.GetStr($"ERR_{(int)this.Error}");
        if (str._IsEmpty())
        {
            str = this.Message;
        }

        if (this.HasRecvMsg)
        {
            str = this.RecvMsg;
        }

        return str;
    }

    public bool IsCommuncationError => this.Error.IsCommunicationError();
}

public class WideTunnel : AsyncService
{
    public WideTunnelOptions Options { get; }

    public TcpIpSystem TcpIp => Options.TcpIp;

    public WideTunnel(WideTunnelOptions options)
    {
        this.Options = options;
    }

    public void CheckCertificate(X509Certificate? cert)
    {
        if (cert == null) throw new CoresLibException("cert == null");

        var pkiCert = cert.AsPkiCertificate();

        if (pkiCert.CheckIfSignedByAnyOfParentCertificatesListOrExactlyMatch(Options.MasterCertificates, out bool exactlyMatch) == false)
        {
            throw new CoresLibException($"The SSL certificate '{pkiCert}' is not trusted by any of master certificates.");
        }

        if (exactlyMatch == false)
        {
            if (pkiCert.IsExpired())
            {
                throw new CoresLibException($"The SSL certificate '{pkiCert}' is expired or not valid. NotBefore = '{pkiCert.NotBefore._ToDtStr()}', NotAfter = '{pkiCert.NotAfter._ToDtStr()}'");
            }
        }
    }

    public bool CheckValidationCallback(object sender, X509Certificate? certificate, X509Chain? chain, SslPolicyErrors sslPolicyErrors)
    {
        try
        {
            this.CheckCertificate(certificate);
        }
        catch (Exception ex)
        {
            ex._Error();

            return false;
        }

        return true;
    }

    public async Task<WtcSocket> WideClientConnectAsync(string pcid, WideTunnelClientOptions clientOptions, bool noCache, CancellationToken cancel = default)
    {
        bool retryFlag = false;
        L_RETRY:
        WtConnectParam connectParam = await WideClientConnectInnerAsync(pcid, clientOptions, noCache, cancel);

        $"WideClientConnect: pcid {pcid}: Redirecting to {connectParam.HostName}:{connectParam.Port} (CacheUsed = {connectParam.CacheUsed}) ..."._Debug();

        try
        {
            ConnSock tcpSock = await this.TcpIp.ConnectAsync(new TcpConnectParam(connectParam.HostName, connectParam.Port, AddressFamily.InterNetwork, connectTimeout: CoresConfig.WtcConfig.WpcTimeoutMsec, dnsTimeout: CoresConfig.WtcConfig.WpcTimeoutMsec), cancel);

            try
            {
                ConnSock targetSock = tcpSock;

                try
                {
                    PalSslClientAuthenticationOptions sslOptions = new PalSslClientAuthenticationOptions(connectParam.HostName, false, (cert) => this.CheckValidationCallback(this, cert.NativeCertificate, null, SslPolicyErrors.None));

                    SslSock sslSock = new SslSock(tcpSock);
                    try
                    {
                        await sslSock.StartSslClientAsync(sslOptions, cancel);

                        targetSock = sslSock;
                    }
                    catch
                    {
                        await sslSock._DisposeSafeAsync();
                        throw;
                    }

                    WtcSocket wtcSocket = new WtcSocket(targetSock, new WtcOptions(this, connectParam));

                    await wtcSocket.StartWtcAsync(cancel);

                    return wtcSocket;
                }
                catch
                {
                    await targetSock._DisposeSafeAsync();
                    throw;
                }
            }
            catch
            {
                await tcpSock._DisposeSafeAsync();
                throw;
            }
        }
        catch
        {
            if (connectParam.CacheUsed && retryFlag == false)
            {
                retryFlag = true;

                // 接続キャッシュを使用して接続することに失敗した
                // 場合はキャッシュを消去して再試行する
                WideTunnel.ConnectParamCache.Delete(pcid);

                $"WideClientConnect: pcid {pcid}: Connect with Session Cache Failed. Retrying..."._Debug();
                goto L_RETRY;
            }

            throw;
        }
    }

    public async Task<string> WideClientGetWoLMacList(string pcid, CancellationToken cancel = default)
    {
        Pack r = new Pack();
        r.AddStr("SvcName", Options.SvcName);
        r.AddStr("Pcid", pcid);
        r.AddSInt("Ver", CoresConfig.WtcConfig.PseudoVer);
        r.AddSInt("Build", CoresConfig.WtcConfig.PseudoBuild);
        r.AddData("ClientId", Options.ClientId);

        var p = await WtWpcCall("ClientGetWoLMacList", r, cancel);
        p.ThrowIfError();

        string list = p["wol_maclist"].StrValueNonNull;

        if (list._IsEmpty()) throw new VpnException(VpnError.ERR_WOL_TARGET_NOT_ENABLED);

        return list;
    }

    static readonly Cache<string, WtConnectParam> ConnectParamCache = new Cache<string, WtConnectParam>(TimeSpan.FromSeconds(60), CacheType.DoNotUpdateExpiresWhenAccess, StrComparer.IgnoreCaseTrimComparer);

    async Task<WtConnectParam> WideClientConnectInnerAsync(string pcid, WideTunnelClientOptions clientOptions, bool noCache, CancellationToken cancel = default)
    {
        if (noCache == false)
        {
            WtConnectParam? cached = ConnectParamCache[pcid];
            if (cached != null)
            {
                var ret = cached._CloneDeep();
                ret.CacheUsed = true;
                return ret;
            }
        }

        Pack r = new Pack();
        r.AddStr("SvcName", Options.SvcName);
        r.AddStr("Pcid", pcid);
        r.AddSInt("Ver", CoresConfig.WtcConfig.PseudoVer);
        r.AddSInt("Build", CoresConfig.WtcConfig.PseudoBuild);
        r.AddInt("ClientOptions", (uint)clientOptions.Flags);
        r.AddData("ClientId", Options.ClientId);

        var p = await WtWpcCall("ClientConnect", r, cancel);
        p.ThrowIfError();

        WtConnectParam c = new WtConnectParam
        {
            HostName = p["Hostname"].StrValueNonNullCheck,
            HostNameForProxy = p["HostNameForProxy"].StrValueNonNullCheck,
            Port = p["Port"].SIntValue,
            SessionId = p["SessionId"].DataValueNonNull.ToArray(),
            ServerMask64 = p["ServerMask64"].Int64Value,
            WebSocketWildCardDomainName = p["WebSocketWildCardDomainName"].StrValueNonNull,
            ClientOptions = clientOptions._CloneDeep(),
            IsStandaloneMode = p["IsStandaloneMode"].BoolValue,
        };

        if (c.WebSocketWildCardDomainName._IsEmpty())
        {
            throw new CoresLibException("c.WebSocketWildCardDomainName is empty.");
        }

        if (c.HostName._IsSamei("<<!!samehost!!>>"))
        {
            string hostTmp = p["__remote_hostname"].StrValueNonNull;
            int portTmp = p["__remote_port"].SIntValue;
            if (hostTmp._IsFilled() && portTmp != 0)
            {
                c.HostName = hostTmp;
                c.Port = portTmp;
            }
        }

        c.CacheUsed = false;

        if (noCache == false)
        {
            WideTunnel.ConnectParamCache[pcid] = c._CloneDeep();
        }

        return c;
    }

    public async Task<Pack> WtWpcCall(string funciontName, Pack requestPack, CancellationToken cancel = default)
    {
        long secs = Util.ConvertDateTime(DtUtcNow) / (long)Consts.Intervals.WtEntranceUrlTimeUpdateMsecs;

        Exception? firstCommError = null;

        // EntryList 一覧にある URL をシャッフルして順序を決める
        List<string> thisTimeEntryList = this.Options.EntryUrlList._Shuffle().ToList();

        // 順に試行
        foreach (string url in thisTimeEntryList)
        {
            cancel.ThrowIfCancellationRequested();

            Pack requestPackCopy = requestPack.Clone();

            string url2 = url._ReplaceStr("__TIME__", secs.ToString(), false);

            $"Trying for the URL: {url2}"._Debug();

            try
            {
                Pack pRet = await WtWpcCallInner(funciontName, requestPackCopy, url2, cancel);

                // エラーの場合は例外を発生させる
                pRet.ThrowIfError();

                // OK。コレを返す
                return pRet;
            }
            catch (Exception ex)
            {
                if (ex._IsVpnCommuncationError() == false)
                {
                    // 通信エラー以外のエラー (すなわちサーバー側のエラー) が返ってきた
                    throw;
                }

                // 最初のエラーは保存しておく
                if (firstCommError == null)
                {
                    firstCommError = ex;
                }
            }
        }

        // 最初の通信エラーを throw する
        if (firstCommError != null)
        {
            throw new CoresLibException($"Fatal error: All Thin Telework controllers unreachable. Please wait for minutes and try again. Last error: '{firstCommError.Message}'");
        }

        // エラーがない。おかしいな
        throw new VpnException(VpnError.ERR_INTERNAL_ERROR);
    }

    async Task<Pack> WtWpcCallInner(string functionName, Pack requestPack, string url, CancellationToken cancel = default)
    {
        requestPack.AddStr("function", functionName);

        WpcPack wpcPostPack = new WpcPack(requestPack);

        var wpcPostBuffer = wpcPostPack.ToPacketBinary().Span.ToArray();

        using var http = new WebApi(this.Options.WebApiOptions, this.CheckValidationCallback);

        var webRet = await http.SimplePostDataAsync(url, wpcPostBuffer, cancel, Consts.MimeTypes.FormUrlEncoded);

        WpcPack wpcResponsePack = WpcPack.Parse(webRet.ToString(), false);

        var retPack = wpcResponsePack.Pack;

        Uri uri = url._ParseUrl();

        retPack.AddStr("__remote_hostname", uri.Host);
        retPack.AddSInt("__remote_port", uri.Port);

        return retPack;
    }
}

#endif

