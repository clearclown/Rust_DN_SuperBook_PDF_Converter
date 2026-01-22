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

#if CORES_BASIC_SECURITY

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
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Serialization;
using System.Collections;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Runtime.CompilerServices;

using Org.BouncyCastle.Crypto.Prng;
using Org.BouncyCastle.Security;

using IPA.Cores.Basic;
using IPA.Cores.Basic.DnsLib;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace IPA.Cores.Basic;

public static partial class CoresConfig
{
    public static partial class EasyDnsResponderSettings
    {
        public static Copenhagen<int> Default_RefreshIntervalSecs = 60;
        public static Copenhagen<int> Default_RetryIntervalSecs = 60;
        public static Copenhagen<int> Default_ExpireIntervalSecs = 88473600;
        public static Copenhagen<int> Default_NegativeCacheTtlSecs = 10;

        public static Copenhagen<ushort> Default_MxPreference = 100;

        public static Copenhagen<int> Default_ForwarderTimeoutMsecs = 10000;
    }
}

public static class DnsUtil
{
    [MethodImpl(Inline)]
    public static DnsMessageBase ParsePacket(ReadOnlySpan<byte> data)
    {
        return DnsMessageBase.CreateByFlag(data, null, null);
    }

    [MethodImpl(Inline)]
    public static Span<byte> BuildPacket(this DnsMessageBase message)
    {
        return message.Encode(false);
    }

    public static readonly DateTimeOffset DnsDtStartDay = (new DateTime(2023, 1, 1)).AddHours(-9)._AsDateTimeOffset(false, false);

    public static uint GenerateSoaSerialNumberFromDateTime(DateTimeOffset dt)
    {
        var diff = (dt - DnsDtStartDay);
        if (diff.Ticks < 0) diff = default;

        var a = (long)diff.TotalSeconds;
        if (a <= 0) a = 1;
        return (uint)a;
    }

    public static uint GenerateSoaSerialNumberFromDateTime_Legacy(DateTimeOffset dt)
    {
        var diff = (dt - DnsDtStartDay);
        int days = (int)diff.TotalDays;

        days = Math.Max(days, 1);
        days = (days % 32000) + 10000;

        string hhmmss = diff.Hours.ToString("D2") + diff.Minutes.ToString("D2") + diff.Seconds.ToString("D2");

        string str = days.ToString("D5") + hhmmss.Substring(0, 5);

        return str._ToUInt();
    }

    [MethodImpl(Inline)]
    public static bool IsEmptyDomain(this DomainName dn)
    {
        if (dn.LabelCount <= 0) return true;

        return false;
    }

    [MethodImpl(Inline)]
    public static EasyDnsResponderRecordType DnsLibRecordTypeToEasyDnsResponderRecordType(RecordType src)
    {
        switch (src)
        {
            case RecordType.Any: return EasyDnsResponderRecordType.Any;
            case RecordType.A: return EasyDnsResponderRecordType.A;
            case RecordType.Aaaa: return EasyDnsResponderRecordType.AAAA;
            case RecordType.Ns: return EasyDnsResponderRecordType.NS;
            case RecordType.CName: return EasyDnsResponderRecordType.CNAME;
            case RecordType.Soa: return EasyDnsResponderRecordType.SOA;
            case RecordType.Ptr: return EasyDnsResponderRecordType.PTR;
            case RecordType.Mx: return EasyDnsResponderRecordType.MX;
            case RecordType.Txt: return EasyDnsResponderRecordType.TXT;
            case RecordType.CAA: return EasyDnsResponderRecordType.CAA;
            case RecordType.Srv: return EasyDnsResponderRecordType.SRV;
        }

        return EasyDnsResponderRecordType.None;
    }

    // DNS ゾーン一覧から最長一致するゾーンを検索
    public static T? SearchLongestMatchDnsZone<T>(StrDictionary<T> zoneList, string normalizedFqdn, out string hostLabelStr, out ReadOnlyMemory<string> hostLabels)
    {
        // a.b.c.d のような FQDN を検索要求された場合、
        // 1. a.b.c.d
        // 2. b.c.d
        // 3. c.d
        // 4. d
        // の順で一致するゾーンがないかどうか検索する。
        // つまり、複数の一致する可能性があるゾーンがある場合、一致する文字長が最も長いゾーンを選択するのである。
        ReadOnlyMemory<string> labels = normalizedFqdn.Split(".").AsMemory();
        int numLabels = labels.Length;

        T? zone = default;

        hostLabelStr = "";

        hostLabels = default;

        for (int i = numLabels; i >= 1; i--)
        {
            var zoneLabels = labels.Slice(numLabels - i, i);

            string zoneLabelsStr = zoneLabels._Combine(".", estimatedLength: normalizedFqdn.Length);

            // 一致する Dict エントリがあるか？
            if (zoneList.TryGetValue(zoneLabelsStr, out T? zoneTmp))
            {
                // あった
                zone = zoneTmp;

                hostLabels = labels.Slice(0, numLabels - i);

                hostLabelStr = hostLabels._Combine(".", estimatedLength: normalizedFqdn.Length);

                break;
            }
        }

        return zone;
    }
}

[Flags]
public enum DnsUdpPacketFlags
{
    None = 0,
    ViaDnsProxy = 1,
    HasEDnsClientSubnetInfo = 2,
}

public class DnsUdpPacket
{
    [JsonConverter(typeof(StringEnumConverter))]
    public DnsUdpPacketFlags Flags { get; }

    public IPEndPoint RemoteEndPoint { get; } // UDP パケット送付元の DNS クライアントの情報 (dnsdist 等の DNS プロキシ経由の場合はプロキシの情報)
    public IPEndPoint? LocalEndPoint { get; }
    public IPEndPoint OriginalRemoteEndPoint { get; } // dnsdist 等の DNS プロキシを経由してきた場合の元の DNS クライアントの情報

    public DnsMessageBase Message { get; }

    public string? DnsResolver { get; }
    public string? EdnsClientSubnet { get; }

    public DnsUdpPacket(IPEndPoint remoteEndPoint, IPEndPoint? localEndPoint, DnsMessageBase message, IPEndPoint? originalRemoteEndPoint = null, DnsUdpPacketFlags flags = DnsUdpPacketFlags.None)
    {
        originalRemoteEndPoint ??= remoteEndPoint;

        if (localEndPoint != null && remoteEndPoint.AddressFamily != localEndPoint.AddressFamily)
        {
            throw new CoresException("remoteEndPoint.AddressFamily != localEndPoint.AddressFamily");
        }

        this.RemoteEndPoint = remoteEndPoint;
        this.LocalEndPoint = localEndPoint;
        this.OriginalRemoteEndPoint = originalRemoteEndPoint;
        this.Message = message;

        if (this.OriginalRemoteEndPoint.Equals(this.RemoteEndPoint) == false)
        {
            flags |= DnsUdpPacketFlags.ViaDnsProxy;
        }

        if (this.Message.IsQuery)
        {
            this.DnsResolver = originalRemoteEndPoint.ToString();

            if (this.Message.IsEDnsEnabled && this.Message.EDnsOptions != null)
            {
                var edns = this.Message.EDnsOptions;

                foreach (var item in edns.Options)
                {
                    if (item.Type == EDnsOptionType.ClientSubnet)
                    {
                        ClientSubnetOption? clientSubnet = item as ClientSubnetOption;
                        if (clientSubnet != null)
                        {
                            flags |= DnsUdpPacketFlags.HasEDnsClientSubnetInfo;

                            this.EdnsClientSubnet = $"{clientSubnet.Address.ToString()}/{clientSubnet.SourceNetmask}";

                            break;
                        }
                    }
                }
            }
        }

        this.Flags = flags;
    }
}




public class EasyDnsServerDynOptions : INormalizable
{
    public int UdpRecvLoopPollingIntervalMsecs { get; set; } = 0;
    public int UdpDelayedProcessTaskQueueLength { get; set; } = 0;
    public int UdpDelayedResponsePacketQueueLength { get; set; } = 0;
    public bool ParseUdpProxyProtocolV2 { get; set; } = false;
    public string DnsProxyProtocolAcceptSrcIpAcl { get; set; } = "";
    public int DnsTcpAxfrMaxRecordsPerMessage { get; set; } = 0;

    public EasyDnsServerDynOptions()
    {
        this.Normalize();
    }

    public void Normalize()
    {
        if (UdpRecvLoopPollingIntervalMsecs <= 0)
            UdpRecvLoopPollingIntervalMsecs = 10;

        if (UdpDelayedProcessTaskQueueLength <= 0)
            UdpDelayedProcessTaskQueueLength = 1024;

        if (UdpDelayedResponsePacketQueueLength <= 0)
            UdpDelayedResponsePacketQueueLength = 4096;

        if (this.DnsProxyProtocolAcceptSrcIpAcl._IsEmpty())
        {
            this.DnsProxyProtocolAcceptSrcIpAcl = Consts.Strings.EasyAclAllowAllRule;
        }

        if (this.DnsTcpAxfrMaxRecordsPerMessage <= 0)
        {
            this.DnsTcpAxfrMaxRecordsPerMessage = 32;
        }
    }
}

public class EasyDnsServerTcpAxfrCallbackParam
{
    public CancellationToken Cancel { init; get; }
    public DnsUdpPacket RequestPacket { init; get; } = null!;
    public DnsQuestion Question { init; get; } = null!;
    public Func<IEnumerable<DnsRecordBase>, CancellationToken, Task> SendRecordsBufferedCallbackAsync { init; get; } = null!;
}

public class EasyDnsServerSetting
{
    public int UdpPort { get; }
    public int TcpPort { get; }
    public Func<EasyDnsServer, List<DnsUdpPacket>, List<DnsUdpPacket>> StandardQueryCallback { get; }
    public Func<EasyDnsServer, EasyDnsServerTcpAxfrCallbackParam, Task>? TcpAxfrCallback { get; }
    public Func<EasyDnsServer, List<Tuple<string, DnsUdpPacket>>>? GetNotifyPacketsCallback { get; }

    public EasyDnsServerSetting(Func<EasyDnsServer, List<DnsUdpPacket>, List<DnsUdpPacket>> standardQueryCallback, Func<EasyDnsServer, EasyDnsServerTcpAxfrCallbackParam, Task>? tcpAxfrCallback, Func<EasyDnsServer, List<Tuple<string, DnsUdpPacket>>>? getNotifyPacketsCallback, int udpPort = Consts.Ports.Dns, int tcpPort = 0)
    {
        this.StandardQueryCallback = standardQueryCallback;
        this.TcpAxfrCallback = tcpAxfrCallback;
        this.GetNotifyPacketsCallback = getNotifyPacketsCallback;
        this.UdpPort = udpPort;
        this.TcpPort = tcpPort;
    }
}

public class EasyDnsServer : AsyncServiceWithMainLoop
{
    public EasyDnsServerSetting Setting { get; }

    public EasyDnsServerDynOptions GetCurrentDynOptions() => this._DynOptions._CloneDeepWithNormalize();
    public void SetCurrentDynOptions(EasyDnsServerDynOptions value) => this._DynOptions = value._CloneDeepWithNormalize();

    EasyDnsServerDynOptions _DynOptions;

    public EasyDnsServer(EasyDnsServerSetting setting, EasyDnsServerDynOptions? dynOptions = null)
    {
        dynOptions ??= new EasyDnsServerDynOptions();

        try
        {
            this.Setting = setting;
            this._DynOptions = dynOptions._CloneDeep();
            this._DynOptions.Normalize();

            this.StartMainLoop(this.MainLoopAsync);
        }
        catch
        {
            this._DisposeSafe();
            throw;
        }
    }

    readonly ConcurrentHashSet<Task> CurrentProcessingTasks = new ConcurrentHashSet<Task>();
    readonly List<Datagram> DelayedReplyUdpPacketsList = new List<Datagram>();
    readonly AsyncAutoResetEvent UdpRecvCancelEvent = new AsyncAutoResetEvent();

    long lastTcpProtocolErrorWriteTick = 0;

    async Task TcpListenerAcceptProcAsync(NetTcpListenerPort listener, ConnSock sock)
    {
        bool doNotWriteError = false;

        try
        {
            var cancel = this.GrandCancel;

            using var st = sock.GetStream();

            if (this.Setting.TcpAxfrCallback == null)
            {
                throw new CoresException($"TCP DNS connection request is not allowed because the callback '{nameof(this.Setting.TcpAxfrCallback)}' is not set");
            }

            st.ReadTimeout = 60 * 1000;

            while (true)
            {
                // DNS パケットの受信
                var requestPacket = await ReceiveDnsPacketAsync(sock, st, cancel);

                var requestMsg = requestPacket.Message;

                DnsQuestion? firstQuestion = null;

                if (requestMsg.IsQuery && requestMsg.Questions.Count >= 1)
                {
                    firstQuestion = requestMsg.Questions[0];
                }

                if ((firstQuestion?.RecordType ?? RecordType.Unspec).EqualsAny(RecordType.Axfr, RecordType.Ixfr))
                {
                    // AXFR リクエストの場合は特別処理を開始
                    await TcpProcessAxfrAsync(st, requestPacket, firstQuestion!, cancel);
                }
                else
                {
                    // AXFR リクエスト以外の普通の要求に対する応答処理を実施
                    if ((firstQuestion?.RecordType ?? RecordType.Unspec) != RecordType.Soa)
                    {
                        long now = TickNow;

                        if (lastTcpProtocolErrorWriteTick == 0 || (now >= (lastTcpProtocolErrorWriteTick + 600000)))
                        {
                            // 10 分に 1 回しかこのエラーをログに記録しないようにする
                            lastTcpProtocolErrorWriteTick = now;
                        }
                        else
                        {
                            doNotWriteError = true;
                        }

                        // SOA 以外の要求は拒否する
                        throw new CoresException($"TCP DNS request packet's type must be SOA or AXFR, but the client requested '{(firstQuestion?.RecordType ?? RecordType.Unspec).ToString()}'");
                    }

                    List<DnsMessageBase> sendMessagesList = new List<DnsMessageBase>();

                    // DNS パケットの処理
                    var responsePackets = Setting.StandardQueryCallback(this, requestPacket._SingleList());

                    // DNS 応答パケットの生成
                    foreach (var responsePacket in responsePackets)
                    {
                        // 途中で生成された DNS パケットのうち、フォワーダとして他の DNS サーバーに転送するパケットは無視する (TCP 経由では応答しない)
                        if (IpEndPointComparer.ComparerIgnoreScopeId.Equals(requestPacket.LocalEndPoint, responsePacket.LocalEndPoint) &&
                            IpEndPointComparer.ComparerIgnoreScopeId.Equals(requestPacket.RemoteEndPoint, responsePacket.RemoteEndPoint))
                        {
                            sendMessagesList.Add(responsePacket.Message);
                        }
                    }

                    if (sendMessagesList.Any() == false)
                    {
                        // 1 つも返すべきパケットがない場合は ServFail を返す
                        var servFailMessage = (DnsMessage)requestPacket.Message._CloneDeep();
                        servFailMessage.IsQuery = false;
                        servFailMessage.ReturnCode = ReturnCode.ServerFailure;
                        sendMessagesList.Add(servFailMessage);
                    }

                    // DNS 応答パケットの送信
                    await SendDnsPacketAsync(st, sendMessagesList, cancel);
                }
            }
        }
        catch (Exception ex)
        {
            if (!(ex is DisconnectedException))
            {
                if (doNotWriteError == false)
                {
                    ex._Error();
                }
            }
        }
    }

    // TCP DNS AXFR リクエストに対する応答を生成して送信する特別処理 (物理的な送信処理を実装する)
    async Task TcpProcessAxfrAsync(PipeStream st, DnsUdpPacket requestPacket, DnsQuestion question, CancellationToken cancel = default)
    {
        int maxBufferedItems = this._DynOptions.DnsTcpAxfrMaxRecordsPerMessage;

        FastStreamBuffer<DnsRecordBase> sendQueue = new FastStreamBuffer<DnsRecordBase>(true);

        EasyDnsServerTcpAxfrCallbackParam param = new EasyDnsServerTcpAxfrCallbackParam
        {
            Cancel = cancel,
            RequestPacket = requestPacket,
            Question = question,
            SendRecordsBufferedCallbackAsync = async (list, c) =>
            {
                // キューに追加
                sendQueue.Enqueue(list.ToArray());

                // キューに一定数以上溜まっていたら送信を試みる
                await SendQueuedAsync(c, false);
            },
        };

        // コールバック関数を呼んで、応答データを生成しながら送信をする
        await this.Setting.TcpAxfrCallback!(this, param);

        // 全部送信する
        await SendQueuedAsync(cancel, true);

        await st.FlushAsync(cancel);

        // 送信処理の実装関数
        async Task SendQueuedAsync(CancellationToken cancel = default, bool force = false)
        {
            while (true)
            {
                if (sendQueue.Length >= 1 && ((sendQueue.Length >= maxBufferedItems) || force))
                {
                    var sendItems = sendQueue.DequeueContiguousSlow(maxBufferedItems);

                    if (sendItems.Length >= 1)
                    {
                        // TCP DNS 応答パケットの生成
                        DnsMessage m = new DnsMessage();

                        m.IsQuery = false;
                        m.TransactionID = requestPacket.Message.TransactionID;
                        m.Questions = requestPacket.Message.Questions._CloneDeep();
                        m.AnswerRecords = sendItems.ToArray().ToList();

                        await this.SendDnsPacketAsync(st, m._SingleArray(), cancel);

                        continue;
                    }
                }

                break;
            }
        }
    }

    // DNS パケットを TCP で送信する
    async Task SendDnsPacketAsync(PipeStream st, IEnumerable<DnsMessageBase> messages, CancellationToken cancel = default)
    {
        MemoryBuffer<byte> sendBuffer = new MemoryBuffer<byte>();

        foreach (var message in messages)
        {
            var messageData = message.BuildPacket().ToArray();
            int messageSize = messageData.Length;

            if (messageSize > ushort.MaxValue)
            {
                throw new CoresException($"DNS reply message is too long: {messageSize}");
            }

            sendBuffer.WriteUInt16((ushort)messageSize);
            sendBuffer.Write(messageData);
        }

        await st.SendAsync(sendBuffer, cancel);
    }

    // DNS パケットを 1 つ TCP で受信する
    async Task<DnsUdpPacket> ReceiveDnsPacketAsync(ConnSock sock, PipeStream st, CancellationToken cancel = default)
    {
        int packetSize = (int)(await st.ReceiveUInt16Async(cancel));

        var recvData = await st.ReceiveAllAsync(packetSize, cancel);

        var message = DnsUtil.ParsePacket(recvData.Span);

        DnsUdpPacket packet = new DnsUdpPacket(sock.EndPointInfo.GetRemoteEndPoint(), sock.EndPointInfo.GetLocalEndPoint(), message);

        return packet;
    }

    // DNS ゾーン更新通知のメインループ
    async Task NotifyMainLoopAsync()
    {
        var cancel = this.GrandCancel;

        while (cancel.IsCancellationRequested == false)
        {
            try
            {
                if (this.Setting.GetNotifyPacketsCallback != null)
                {
                    List<Tuple<string, DnsUdpPacket>> packetsList = this.Setting.GetNotifyPacketsCallback(this);

                    // 送付先ホスト名の名前解決
                    Dictionary<string, string> resolvedEpDict = new Dictionary<string, string>(StrCmpi);

                    foreach (var packet in packetsList)
                    {
                        var serversList = packet.Item1;

                        if (resolvedEpDict.ContainsKey(serversList) == false)
                        {
                            string resolved = "";

                            try
                            {
                                resolved = await LocalNet.ResolveHostAndPortListStrToIpAndPortListStrAsync(serversList, 53, cancel: cancel, multiple: true);
                            }
                            catch (Exception ex)
                            {
                                ex._Error();
                            }

                            resolvedEpDict.Add(serversList, resolved);
                        }
                    }

                    // 送付すべきパケットリストを DNS サーバーごとに整理する
                    Dictionary<IPEndPoint, List<DnsUdpPacket>> perDnsServerList = new Dictionary<IPEndPoint, List<DnsUdpPacket>>(IpEndPointComparer.ComparerIgnoreScopeId);

                    foreach (var packet in packetsList)
                    {
                        var serversList = packet.Item1;
                        var udpPacket = packet.Item2;

                        string resolved = resolvedEpDict._GetOrDefault(serversList, "");
                        if (resolved._IsFilled())
                        {
                            var epList = IPUtil.ParseIpAndPortList(resolved);

                            foreach (var ep in epList)
                            {
                                perDnsServerList._GetOrNew(ep).Add(udpPacket);
                            }
                        }
                    }

                    // 並列して更新通知パケットを送付
                    await perDnsServerList._DoForEachParallelAsync(async (target, taskIndex) =>
                    {
                        try
                        {
                            var remoteEndPoint = target.Key;
                            var packetList = target.Value;

                            using UdpClient udp = new UdpClient(remoteEndPoint.AddressFamily);

                            int numSend = 5;
                            int interval = 333;

                            for (int i = 0; i < numSend; i++)
                            {
                                if (cancel.IsCancellationRequested)
                                {
                                    break;
                                }

                                foreach (var packet in packetList)
                                {
                                    if (cancel.IsCancellationRequested)
                                    {
                                        break;
                                    }
                                    try
                                    {
                                        await udp.SendAsync(DnsUtil.BuildPacket(packet.Message).ToArray(), remoteEndPoint, cancel);
                                    }
                                    catch { }
                                }

                                await this.GrandCancel._WaitUntilCanceledAsync(Util.GenRandInterval(interval));

                                if (cancel.IsCancellationRequested)
                                {
                                    break;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            // 例外は握りつぶす
                            ex._Error();
                        }
                    },
                    32,
                    MultitaskDivideOperation.RoundRobin,
                    cancel);
                }
            }
            catch (Exception ex)
            {
                ex._Error();
            }

            await cancel._WaitUntilCanceledAsync(Util.GenRandInterval(1000));
        }
    }

    // DNS サーバー機能のメインループ
    async Task MainLoopAsync(CancellationToken cancel)
    {
        await using var udpListener = LocalNet.CreateUdpListener(new NetUdpListenerOptions(TcpDirectionType.Server));
        await using var tcpListener = LocalNet.CreateTcpListener(new TcpListenParam(TcpListenerAcceptProcAsync, null, new[] { this.Setting.TcpPort }));

        // UDP: リスナ用
        udpListener.AddEndPoint(new IPEndPoint(IPAddress.Any, this.Setting.UdpPort));
        udpListener.AddEndPoint(new IPEndPoint(IPAddress.IPv6Any, this.Setting.UdpPort));

        // UDP: フォワーダ用 (DNS クライアントポートとして動作)
        udpListener.AddEndPoint(new IPEndPoint(IPAddress.Any, 0));
        udpListener.AddEndPoint(new IPEndPoint(IPAddress.IPv6Any, 0));

        // Zone 更新通知発信スレッド
        var notifyLoopTask = TaskUtil.StartAsyncTaskAsync(NotifyMainLoopAsync);
        try
        {
            await using var udpSock = udpListener.GetSocket(true);

            while (cancel.IsCancellationRequested == false)
            {
                try
                {
                    // UDP パケットを受信するまで待機して受信を実行 (タイムアウトが発生した場合も抜ける)
                    var allRecvList = await udpSock.ReceiveDatagramsListAsync(cancel: cancel, timeout: this._DynOptions.UdpRecvLoopPollingIntervalMsecs, noTimeoutException: true, cancelEvent: UdpRecvCancelEvent);

                    var proxyAcl = new EasyIpAcl(this._DynOptions.DnsProxyProtocolAcceptSrcIpAcl, EasyIpAclAction.Deny, EasyIpAclAction.Deny, false);

                    // 受信した UDP パケットを複数の CPU で処理
                    var allSendList = await allRecvList._ProcessParallelAndAggregateAsync((perTaskRecvList, taskIndex) =>
                    {
                        // 以下の処理は各 CPU で分散
                        List<DnsUdpPacket> perTaskRequestPacketsList = new List<DnsUdpPacket>(perTaskRecvList.Count);

                        // 受信した UDP パケットをすべてパースし DNS パケットをパースする
                        foreach (var item in perTaskRecvList)
                        {
                            if (item != null)
                            {
                                try
                                {
                                    DnsUdpPacketFlags flags = DnsUdpPacketFlags.None;

                                    IPEndPoint? originalRemoteEndPoint = null;

                                    var dnsPacketData = item.Data.Span;

                                    if (this._DynOptions.ParseUdpProxyProtocolV2)
                                    {
                                        // Proxy Protocol のパースを試行
                                        if (ProxyProtocolV2Parsed.TryParse(ref dnsPacketData, out var proxyProtocolParsed))
                                        {
                                            // パースに成功した場合 ACL を確認
                                            if (proxyAcl.Evaluate(item.RemoteIPEndPoint!.Address) == EasyIpAclAction.Permit)
                                            {
                                                // ACL が一致した場合のみ Proxy Protocol の結果を採用。それ以外の場合は無視
                                                originalRemoteEndPoint = proxyProtocolParsed.SrcEndPoint;
                                                if (originalRemoteEndPoint != null)
                                                {
                                                    flags |= DnsUdpPacketFlags.ViaDnsProxy;
                                                }
                                            }
                                        }
                                    }

                                    var request = DnsUtil.ParsePacket(dnsPacketData);

                                    DnsUdpPacket pkt = new DnsUdpPacket(item.RemoteIPEndPoint!, item.LocalIPEndPoint!, request, originalRemoteEndPoint, flags);

                                    perTaskRequestPacketsList.Add(pkt);
                                }
                                catch { }
                            }
                        }

                        // 指定されたコールバック関数を呼び出して DNS クエリを解決し、回答すべき DNS レスポンス一覧を生成する
                        List<DnsUdpPacket> perTaskReaponsePacketsList = Setting.StandardQueryCallback(this, perTaskRequestPacketsList);

                        List<Datagram> perTaskSendList = new List<Datagram>(perTaskReaponsePacketsList.Count);

                        // 回答すべき DNS レスポンス一覧をバイナリとしてビルドし UDP パケットを生成
                        foreach (var responsePkt in perTaskReaponsePacketsList)
                        {
                            try
                            {
                                Memory<byte> packetData = DnsUtil.BuildPacket(responsePkt.Message).ToArray();

                                //responsePkt.RemoteEndPoint.ToString()._Print();

                                var datagram = new Datagram(packetData, responsePkt.RemoteEndPoint, responsePkt.LocalEndPoint);

                                //responsePkt.RemoteEndPoint.ToString()._Print();
                                perTaskSendList.Add(datagram);
                                //responsePkt.RemoteEndPoint.ToString()._Print();
                                //perTaskSendList[0].RemoteEndPoint.ToString()._Print();
                            }
                            catch { }
                        }

                        return TR(perTaskSendList);
                    },
                    operation: MultitaskDivideOperation.RoundRobin,
                    cancel: cancel);

                    // UDP パケットを応答 (通常分)
                    await udpSock.SendDatagramsListAsync(allSendList.ToArray(), cancel: cancel);

                    // UDP パケットを応答 (Delay 回答分)
                    Datagram[] delayedUdpPacketsArray;
                    lock (this.DelayedReplyUdpPacketsList)
                    {
                        delayedUdpPacketsArray = DelayedReplyUdpPacketsList.ToArray();
                        DelayedReplyUdpPacketsList.Clear();
                    }

                    await udpSock.SendDatagramsListAsync(delayedUdpPacketsArray, cancel: cancel);
                }
                catch (Exception ex)
                {
                    ex._Debug();
                }
            }
        }
        finally
        {
            await notifyLoopTask._TryWaitAsync();
        }
    }

    public bool BeginDelayDnsPacketProcessing(DnsUdpPacket requestDnsPacket, Func<DnsUdpPacket, Task<DnsUdpPacket?>> proc)
    {
        Dbg.RunDebugProcIntervalOnce(() =>
        {
            $"{this.CurrentProcessingTasks.Count}  {this.DelayedReplyUdpPacketsList.Count}"._Print();
        });

        if (this.CurrentProcessingTasks.Count >= this._DynOptions.UdpDelayedProcessTaskQueueLength)
        {
            // これ以上の個数の遅延タスクを開始できない
            return false;
        }

        if (this.DelayedReplyUdpPacketsList.Count >= this._DynOptions.UdpDelayedResponsePacketQueueLength)
        {
            // これ以上の応答パケットのキューメモリがない
            return false;
        }

        try
        {
            Task task = TaskUtil.StartAsyncTaskSlowAsync(async currentTask =>
            {
                try
                {
                    DnsUdpPacket? responsePkt = await proc(requestDnsPacket);
                    if (responsePkt != null)
                    {
                        Memory<byte> packetData = DnsUtil.BuildPacket(responsePkt.Message).ToArray();

                        var datagram = new Datagram(packetData, responsePkt.RemoteEndPoint, responsePkt.LocalEndPoint);

                        if (this.DelayedReplyUdpPacketsList.Count < this._DynOptions.UdpDelayedResponsePacketQueueLength)
                        {
                            lock (DelayedReplyUdpPacketsList)
                            {
                                this.DelayedReplyUdpPacketsList.Add(datagram);
                            }

                            UdpRecvCancelEvent.Set(softly: true);
                        }
                    }
                }
                catch (Exception ex)
                {
                    ex._Debug();
                }
                finally
                {
                    this.CurrentProcessingTasks.Remove(currentTask);
                }
            },
            task =>
            {
                this.CurrentProcessingTasks.Add(task);
            });

            return true;
        }
        catch (Exception ex)
        {
            // 普通ここには来ないはず
            ex._Debug();

            return false;
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

















// 以下はユーザー設定用構造体 (内部データ構造ではない)。ユーザー開発者が組み立てしやすいように、単なるクラスの列になっているのである。
public class EasyDnsResponderRecordSettings
{
    public int TtlSecs { get; set; } = 60;
}

[Flags]
public enum EasyDnsResponderRecordType
{
    None = 0,
    Any,
    A,
    AAAA,
    NS,
    CNAME,
    SOA,
    PTR,
    MX,
    TXT,
    CAA,
    SRV,
}

[Flags]
public enum EasyDnsResponderRecordAttribute
{
    None = 0,
    DynamicRecord = 1,
}

public class EasyDnsResponderRecord
{
    [JsonConverter(typeof(StringEnumConverter))]
    public EasyDnsResponderRecordAttribute Attribute { get; set; } = EasyDnsResponderRecordAttribute.None;

    public string Name { get; set; } = ""; // アスタリスク文字を用いたワイルドカード指定可能。

    [JsonConverter(typeof(StringEnumConverter))]
    public EasyDnsResponderRecordType Type { get; set; } = EasyDnsResponderRecordType.None;

    public string Contents { get; set; } = ""; // DynamicRecord の場合はコールバック ID (任意の文字列) を指定

    public EasyDnsResponderRecordSettings? Settings { get; set; } = null;

    [JsonIgnore]
    public object? Param = null; // 注意！ 複雑なオブジェクトを設定すると動作がおかしくなる。内部的 Clone やシリアライズが原因。

    public static EasyDnsResponderRecordType StrToRecordType(string typeStr)
    {
        var type = EasyDnsResponderRecordType.None.ParseAsDefault(typeStr);

        return type;
    }


    static readonly EasyDnsResponder.Zone dummyZoneForTryParse = new EasyDnsResponder.Zone(new EasyDnsResponder.DataSet(new EasyDnsResponderSettings()), new EasyDnsResponderZone { DomainName = "_dummy_domain.example.org" });

    public static EasyDnsResponderRecord TryParseFromString(string str, string? parentDomainFqdn = null, EasyDnsResponderRecordSettings? settings = null)
    {
        var r = FromString(str, parentDomainFqdn, settings: settings);

        EasyDnsResponder.Record.CreateFrom(dummyZoneForTryParse, r);

        return r;
    }

    public static EasyDnsResponderRecord FromString(string str, string? parentDomainFqdn = null, object? param = null, EasyDnsResponderRecordSettings? settings = null)
    {
        if (str._GetKeysListAndValue(2, out var keys, out string value) == false)
        {
            throw new CoresLibException($"DNS Record String: Invalid Format. Str = '{str}'");
        }

        string typeStr = keys[0];
        var type = StrToRecordType(typeStr);
        if (type == EasyDnsResponderRecordType.None || type == EasyDnsResponderRecordType.SOA)
        {
            throw new CoresLibException($"DNS Record String: Invalid Record Type. Str = '{str}'");
        }

        string name = keys[1];

        if (name == "@")
        {
            name = "";
        }

        if (parentDomainFqdn._IsFilled())
        {
            value = value._ReplaceStr("@", parentDomainFqdn);
        }
        else
        {
            if (value._InStr("@"))
            {
                throw new CoresLibException($"FQDN has an invalid character '@'");
            }
        }

        return new EasyDnsResponderRecord
        {
            Attribute = EasyDnsResponderRecordAttribute.None,
            Contents = value,
            Type = type,
            Name = name,
            Settings = settings,
            Param = param,
        };
    }

    public override string ToString()
    {
        return $"{Attribute} {Type} {Name._FilledOrDefault("@")} {Contents}";
    }
}

public class EasyDnsResponderForwarder
{
    public string Selector { get; set; } = "";        // ドメイン名、ワイルドカード付き FQDN、またはサブネット表記

    public string TargetServers { get; set; } = "";     // ターゲットの DNS サーバー一覧

    public string CallbackId { get; set; } = "";

    public int TimeoutMsecs { get; set; } = CoresConfig.EasyDnsResponderSettings.Default_ForwarderTimeoutMsecs;

    public QueryStringList ArgsList { get; set; } = new QueryStringList();
}

public class EasyDnsResponderZone
{
    public string DomainName { get; set; } = "";

    public List<EasyDnsResponderRecord> RecordList { get; set; } = new List<EasyDnsResponderRecord>();

    public EasyDnsResponderRecordSettings? DefaultSettings { get; set; } = null;

    public string TcpAxfrAllowedAcl { get; set; } = "";
    public string NotifyServers { get; set; } = "";
    public string AdditionalDigestSeedStr { get; set; } = "";
}

public class EasyDnsResponderSettings
{
    public List<EasyDnsResponderZone> ZoneList { get; set; } = new List<EasyDnsResponderZone>();

    public EasyDnsResponderRecordSettings? DefaultSettings { get; set; } = null;

    public List<EasyDnsResponderForwarder> ForwarderList { get; set; } = new List<EasyDnsResponderForwarder>();

    public bool SaveAccessLogForDebug { get; set; } = false;

    public bool CopyQueryAdditionalRecordsToResponse { get; set; } = false;
}

// TCP AXFR コールバック関数に渡されるリクエストデータ
public class EasyDnsResponderTcpAxfrCallbackRequest
{
    public EasyDnsResponderZone Zone { init; get; } = null!;
    public EasyDnsResponder.Zone ZoneInternal { init; get; } = null!;
    public DnsUdpPacket RequestPacket { init; get; } = null!;
    public EasyDnsServerTcpAxfrCallbackParam CallbackParam { get; set; } = null!;

    public CancellationToken Cancel => this.CallbackParam.Cancel;


    // 現在の Zone に静的に定義されている静的レコードリストを送信する (SOA を除く)
    // 件数の少ない静的レコードは戻り値として返し、件数の多い自動生成静的レコードは直ちに送信試行をする
    public async Task<List<Tuple<EasyDnsResponder.Record, string?>>> GenerateStandardStaticRecordsListFromZoneDataAsync(CancellationToken cancel = default)
    {
        List<Tuple<EasyDnsResponder.Record, string?>> list = new List<Tuple<EasyDnsResponder.Record, string?>>();

        var rootVirtualZone = new EasyDnsResponder.Zone(isVirtualRootZone: EnsureSpecial.Yes, ZoneInternal);

        // この Zone そのものの NS レコード
        foreach (var ns in ZoneInternal.NSRecordList)
        {
            list.Add(new Tuple<EasyDnsResponder.Record, string?>(ns, null));

            // NS レコードに付随する Glue レコード (明示的指定)
            foreach (var glue in ns.GlueRecordList)
            {
                switch (glue)
                {
                    case EasyDnsResponder.Record_A glueA:
                        list.Add(new Tuple<EasyDnsResponder.Record, string?>(glueA, null));
                        break;

                    case EasyDnsResponder.Record_AAAA glueAAAA:
                        list.Add(new Tuple<EasyDnsResponder.Record, string?>(glueAAAA, null));
                        break;
                }
            }

            // NS レコードに付随する Glue レコード (暗黙的な指定、A または AAAA が存在すれば Glue レコードとみなす)
            string nsFqdn = ns.ServerName.ToNormalizedFqdnFast();
            var found2 = ZoneInternal.ParentDataSet.Search(nsFqdn);
            if (found2 != null && found2.RecordList != null)
            {
                foreach (var record in found2.RecordList.Where(x => x.Type == EasyDnsResponderRecordType.A || x.Type == EasyDnsResponderRecordType.AAAA))
                {
                    switch (record)
                    {
                        case EasyDnsResponder.Record_A a:
                            if (a.IsSubnet == false)
                            {
                                list.Add(new Tuple<EasyDnsResponder.Record, string?>(new EasyDnsResponder.Record_A(rootVirtualZone, a.Settings, nsFqdn, a.IPv4Address), null));
                            }
                            break;

                        case EasyDnsResponder.Record_AAAA aaaa:
                            if (aaaa.IsSubnet == false)
                            {
                                list.Add(new Tuple<EasyDnsResponder.Record, string?>(new EasyDnsResponder.Record_AAAA(rootVirtualZone, aaaa.Settings, nsFqdn, aaaa.IPv6Address), null));
                            }
                            break;
                    }
                }
            }
        }

        // 権限移譲 NS レコード
        foreach (var dele in ZoneInternal.NSDelegationRecordList)
        {
            string fqdn = dele.Key;
            foreach (var ns in dele.Value)
            {
                list.Add(new Tuple<EasyDnsResponder.Record, string?>(ns, null));

                // NS レコードに付随する Glue レコード (明示的指定)
                foreach (var glue in ns.GlueRecordList)
                {
                    switch (glue)
                    {
                        case EasyDnsResponder.Record_A glueA:
                            list.Add(new Tuple<EasyDnsResponder.Record, string?>(glueA, null));
                            break;

                        case EasyDnsResponder.Record_AAAA glueAAAA:
                            list.Add(new Tuple<EasyDnsResponder.Record, string?>(glueAAAA, null));
                            break;
                    }
                }

                // NS レコードに付随する Glue レコード (暗黙的な指定、A または AAAA が存在すれば Glue レコードとみなす)
                string nsFqdn = ns.ServerName.ToNormalizedFqdnFast();
                var found2 = ZoneInternal.ParentDataSet.Search(nsFqdn);
                if (found2 != null && found2.RecordList != null)
                {
                    foreach (var record in found2.RecordList.Where(x => x.Type == EasyDnsResponderRecordType.A || x.Type == EasyDnsResponderRecordType.AAAA))
                    {
                        switch (record)
                        {
                            case EasyDnsResponder.Record_A a:
                                if (a.IsSubnet == false)
                                {
                                    list.Add(new Tuple<EasyDnsResponder.Record, string?>(new EasyDnsResponder.Record_A(rootVirtualZone, a.Settings, nsFqdn, a.IPv4Address), null));
                                }
                                break;

                            case EasyDnsResponder.Record_AAAA aaaa:
                                if (aaaa.IsSubnet == false)
                                {
                                    list.Add(new Tuple<EasyDnsResponder.Record, string?>(new EasyDnsResponder.Record_AAAA(rootVirtualZone, aaaa.Settings, nsFqdn, aaaa.IPv6Address), null));
                                }
                                break;
                        }
                    }
                }
            }
        }

        // 様々な静的レコード
        foreach (var record in ZoneInternal.RecordList)
        {
            string label = record.Name;
            string fqdn = Str.CombineFqdn(label, ZoneInternal.DomainFqdn);

            switch (record)
            {
                case EasyDnsResponder.Record_A a:
                    if (a.IsSubnet == false)
                    {
                        // 通常の A レコード
                        if (fqdn._IsValidFqdn(true, true))
                        {
                            // ワイルドカード無し、またはシンプルなワイルドカードのみ許容
                            list.Add(new Tuple<EasyDnsResponder.Record, string?>(a, null));
                        }
                    }
                    else
                    {
                        var attributes = a.Param as EasyJsonStrAttributes;
                        string wildcard_before_str = "";
                        string wildcard_after_str = "";
                        if (attributes != null)
                        {
                            wildcard_before_str = attributes["wildcard_before_str"];
                            wildcard_after_str = attributes["wildcard_after_str"];
                        }

                        // サブネット形式の A レコード
                        if (fqdn._IsValidFqdn(true, true))
                        {
                            if (fqdn._InStri("*"))
                            {
                                // ワイルドカードの場合は、展開をする
                                if (Str.TryParseFirstWildcardFqdnSandwitched(fqdn, out var wildcardInfo))
                                {
                                    if (a.IPv4SubnetMaskLength >= 8) // /8 すなわち 16777216 個まで自動生成する
                                    {
                                        var ipStart = IPUtil.GetPrefixAddress(a.IPv4Address, a.IPv4SubnetMaskLength);
                                        var ipStart2 = IPv4Addr.FromAddress(ipStart);
                                        int num = (int)IPUtil.CalcNumIPFromSubnetLen(AddressFamily.InterNetwork, a.IPv4SubnetMaskLength);

                                        for (int i = 0; i < num; i++)
                                        {
                                            var ip = ipStart2.Add(i).GetIPAddress();

                                            List<(string, string)> tmp = new List<(string, string)>();

                                            tmp.Add((IPUtil.GenerateWildCardDnsFqdn(ip, wildcardInfo.suffix, "", "", false), IPUtil.GenerateWildCardDnsFqdn(ip, wildcardInfo.suffix, "", "", true)));

                                            if (wildcard_before_str._IsFilled())
                                            {
                                                tmp.Add((IPUtil.GenerateWildCardDnsFqdn(ip, wildcardInfo.suffix, wildcard_before_str, "", false), IPUtil.GenerateWildCardDnsFqdn(ip, wildcardInfo.suffix, wildcard_before_str, "", true)));
                                            }

                                            if (wildcard_after_str._IsFilled())
                                            {
                                                tmp.Add((IPUtil.GenerateWildCardDnsFqdn(ip, wildcardInfo.suffix, "", wildcard_after_str, false), IPUtil.GenerateWildCardDnsFqdn(ip, wildcardInfo.suffix, "", wildcard_after_str, true)));
                                            }

                                            List<Tuple<EasyDnsResponder.Record, string?>> tmpList = new List<Tuple<EasyDnsResponder.Record, string?>>();

                                            foreach (var new_fqdn in tmp)
                                            {
                                                tmpList.Add(new Tuple<EasyDnsResponder.Record, string?>(new EasyDnsResponder.Record_A(rootVirtualZone, a.Settings, new_fqdn.Item1, ip), new_fqdn.Item2));
                                            }

                                            // 件数が膨大であるので、戻り値リストに追加せず、ここで直ちに送信をする
                                            await this.SendBufferedAsync(tmpList, cancel);
                                        }
                                    }
                                }
                            }
                            else
                            {
                                // 非ワイルドカードの場合は、最初の 1 個を応答するレコードを生成する
                                var ipStart = IPUtil.GetPrefixAddress(a.IPv4Address, a.IPv4SubnetMaskLength);
                                list.Add(new Tuple<EasyDnsResponder.Record, string?>(new EasyDnsResponder.Record_A(rootVirtualZone, a.Settings, fqdn, ipStart), null));
                            }
                        }
                    }
                    break;

                case EasyDnsResponder.Record_AAAA aaaa:
                    if (aaaa.IsSubnet == false)
                    {
                        // 通常の A レコード
                        if (fqdn._IsValidFqdn(true, true))
                        {
                            // ワイルドカード無し、またはシンプルなワイルドカードのみ許容
                            list.Add(new Tuple<EasyDnsResponder.Record, string?>(aaaa, null));
                        }
                    }
                    else
                    {
                        var attributes = aaaa.Param as EasyJsonStrAttributes;
                        string wildcard_before_str = "";
                        string wildcard_after_str = "";
                        if (attributes != null)
                        {
                            wildcard_before_str = attributes["wildcard_before_str"];
                            wildcard_after_str = attributes["wildcard_after_str"];
                        }

                        // サブネット形式の AAAA レコード
                        if (fqdn._IsValidFqdn(true, true))
                        {
                            if (fqdn._InStri("*"))
                            {
                                // ワイルドカードの場合は、展開をする
                                if (Str.TryParseFirstWildcardFqdnSandwitched(fqdn, out var wildcardInfo))
                                {
                                    if (aaaa.IPv6SubnetMaskLength >= 104) // /104 すなわち 16777216 個まで自動生成する
                                    {
                                        var ipStart = IPUtil.GetPrefixAddress(aaaa.IPv6Address, aaaa.IPv6SubnetMaskLength);
                                        var ipStart2 = IPv6Addr.FromAddress(ipStart);
                                        int num = (int)IPUtil.CalcNumIPFromSubnetLen(AddressFamily.InterNetworkV6, aaaa.IPv6SubnetMaskLength);

                                        for (int i = 0; i < num; i++)
                                        {
                                            var ip = ipStart2.Add(i).GetIPAddress();

                                            List<(string, string)> tmp = new List<(string, string)>();

                                            tmp.Add((IPUtil.GenerateWildCardDnsFqdn(ip, wildcardInfo.suffix, "", "", false), IPUtil.GenerateWildCardDnsFqdn(ip, wildcardInfo.suffix, "", "", true)));

                                            if (wildcard_before_str._IsFilled())
                                            {
                                                tmp.Add((IPUtil.GenerateWildCardDnsFqdn(ip, wildcardInfo.suffix, wildcard_before_str, "", false), IPUtil.GenerateWildCardDnsFqdn(ip, wildcardInfo.suffix, wildcard_before_str, "", true)));
                                            }

                                            if (wildcard_after_str._IsFilled())
                                            {
                                                tmp.Add((IPUtil.GenerateWildCardDnsFqdn(ip, wildcardInfo.suffix, "", wildcard_after_str, false), IPUtil.GenerateWildCardDnsFqdn(ip, wildcardInfo.suffix, "", wildcard_after_str, true)));
                                            }

                                            List<Tuple<EasyDnsResponder.Record, string?>> tmpList = new List<Tuple<EasyDnsResponder.Record, string?>>();

                                            foreach (var new_fqdn in tmp)
                                            {
                                                tmpList.Add(new Tuple<EasyDnsResponder.Record, string?>(new EasyDnsResponder.Record_AAAA(rootVirtualZone, aaaa.Settings, new_fqdn.Item1, ip), new_fqdn.Item2));
                                            }

                                            // 件数が膨大であるので、戻り値リストに追加せず、ここで直ちに送信をする
                                            await this.SendBufferedAsync(tmpList, cancel);
                                        }
                                    }
                                }
                            }
                            else
                            {
                                // 非ワイルドカードの場合は、最初の 1 個を応答するレコードを生成する
                                var ipStart = IPUtil.GetPrefixAddress(aaaa.IPv6Address, aaaa.IPv6SubnetMask);
                                list.Add(new Tuple<EasyDnsResponder.Record, string?>(new EasyDnsResponder.Record_A(rootVirtualZone, aaaa.Settings, fqdn, ipStart), null));
                            }
                        }
                    }
                    break;

                case EasyDnsResponder.Record_SOA:
                case EasyDnsResponder.Record_NS:
                    // 何もしない
                    break;

                default:
                    // その他のレコード
                    if (fqdn._IsValidFqdn(true, true))
                    {
                        // ワイルドカード無し、またはシンプルなワイルドカードのみ許容
                        list.Add(new Tuple<EasyDnsResponder.Record, string?>(record, null));
                    }
                    break;
            }
        }

        return list;
    }

    // 複数個の DNS レコードを送信する
    public async Task SendBufferedAsync(IEnumerable<Tuple<EasyDnsResponder.Record, string?>> recordList, CancellationToken cancel = default, DateTimeOffset? bootTimeForSoa = null, int soaVersion = 0, bool distinct = false, bool sort = false)
    {
        HashSet<string> distinctHash = new HashSet<string>();
        List<(DnsRecordBase, string)> answerList = new List<(DnsRecordBase, string)>();

        foreach (var record in recordList)
        {
            DateTimeOffset bootTimeForSoa2 = DtOffsetZero;

            string fqdn = Str.CombineFqdn(record.Item1.Name, record.Item1.ParentZone.DomainFqdn);

            if (record.Item1.Type == EasyDnsResponderRecordType.SOA)
            {
                bootTimeForSoa2 = bootTimeForSoa ?? DtOffsetNow;
            }

            DomainName domainName;

            if (fqdn._InStri("*"))
            {
                domainName = DomainName.ParseWildcardAllow(fqdn);
            }
            else
            {
                domainName = DomainName.Parse(fqdn);
            }

            var answer = record.Item1.ToDnsLibRecordBase(domainName, bootTimeForSoa2, soaVersion);

            if (answer != null)
            {
                if (distinct == false || distinctHash.Add(answer.ToString()))
                {
                    string? fqdnForSort = record.Item2;

                    if (fqdnForSort == null)
                    {
                        fqdnForSort = answer.Name.ToString();
                    }

                    answerList.Add((answer, fqdnForSort));
                }
            }
        }

        var comparer = new FqdnReverseStrComparer(FqdnReverseStrComparerFlags.ConsiderDepth);

        answerList._DoSortBy(a => a.OrderBy(x => x.Item2, comparer));

        if (answerList.Any())
        {
            await this.CallbackParam.SendRecordsBufferedCallbackAsync(answerList.Select(x => x.Item1), cancel);
        }
    }

    // 1 個の DNS レコードを送信する
    public Task SendBufferedAsync(EasyDnsResponder.Record record, string? fqdnForSort = null, CancellationToken cancel = default, DateTimeOffset? bootTimeFotSoa = null, int soaVersion = 0)
        => SendBufferedAsync(new Tuple<EasyDnsResponder.Record, string?>(record, fqdnForSort)._SingleArray(), cancel, bootTimeFotSoa, soaVersion);
}

// ダイナミックレコードのコールバック関数に渡されるリクエストデータ
public class EasyDnsResponderDynamicRecordCallbackRequest
{
    public EasyDnsResponderZone Zone { init; get; } = null!;
    public EasyDnsResponderRecord Record { init; get; } = null!;

    public EasyDnsResponderRecordType ExpectedRecordType { init; get; }
    public string RequestFqdn { init; get; } = null!;
    public string RequestHostName { init; get; } = null!;
    public string CallbackId { init; get; } = null!;
    public DnsUdpPacket RequestPacket { init; get; } = null!;
}

// ダイナミックレコードのコールバック関数で返却すべきデータ
public class EasyDnsResponderDynamicRecordCallbackResult
{
    public List<IPAddress>? IPAddressList { get; set; } // A, AAAA の場合
    public List<DomainName>? MxFqdnList { get; set; } // MX の場合
    public List<ushort>? MxPreferenceList { get; set; } // MX の場合の Preference 値のリスト
    public List<DomainName>? CNameFqdnList { get; set; } // CNAME の場合
    public List<DomainName>? PtrFqdnList { get; set; } // PTR の場合
    public List<DomainName>? NsFqdnList { get; set; } // NS の場合
    public List<string>? TextList { get; set; } // TXT の場合
    public List<Tuple<byte, string, string>>? CaaList { get; set; } // CAA の場合
    public List<ushort>? SrvPriorityList { get; set; } // SRV の場合
    public List<ushort>? SrvWeightList { get; set; } // SRV の場合
    public List<ushort>? SrvPortList { get; set; } // SRV の場合
    public List<DomainName>? SrvTargetList { get; set; } // SRV の場合

    public EasyDnsResponderRecordSettings? Settings { get; set; } // TTL 等

    public bool NotFound { get; set; } // 発見されず
}

// フォワーダのリクエストメッセージ変形コールバック関数に渡されるデータ
public class EasyDnsResponderForwarderRequestTransformerCallbackRequest
{
    public string RequestFqdn { init; get; } = null!;
    public string CallbackId { init; get; } = null!;
    public QueryStringList ArgsList => this.ForwarderInternal.ArgsList;
    public DnsUdpPacket OriginalRequestPacket { init; get; } = null!;
    public EasyDnsResponderForwarder ForwarderDef { init; get; } = null!;
    public EasyDnsResponder.Forwarder ForwarderInternal { init; get; } = null!;
}

// フォワーダのリクエストメッセージ変形コールバック関数から返却されるべきデータ
public class EasyDnsResponderForwarderRequestTransformerCallbackResult
{
    public bool SkipForwarder { get; set; } = false;
    public ReturnCode ErrorCode { get; set; } = ReturnCode.NoError;
    public DnsMessageBase? ModifiedDnsRequestMessage { get; set; } = null;
    public Func<EasyDnsResponderForwarderResponseTransformerCallbackRequest, EasyDnsResponderForwarderResponseTransformerCallbackResult>? ForwarderResponseTransformerCallback { get; set; }
}

// フォワーダのレスポンスメッセージ変形コールバック関数に渡されるデータ
public class EasyDnsResponderForwarderResponseTransformerCallbackRequest
{
    public string CallbackId { init; get; } = null!;
    public DnsUdpPacket OriginalResponsePacket { init; get; } = null!;
    public EasyDnsResponderForwarder ForwarderDef { init; get; } = null!;
    public EasyDnsResponder.Forwarder ForwarderInternal { init; get; } = null!;
}

// フォワーダのレスポンスメッセージ変形コールバック関数から返却されるべきデータ
public class EasyDnsResponderForwarderResponseTransformerCallbackResult
{
    public bool Ignore { get; set; } = false;
    public DnsMessageBase? ModifiedDnsResponseMessage { get; set; } = null;
}


public class EasyDnsResponder
{
    // フォワーダ変換テーブル
    public class ForwarderTranslateTable
    {
        public class Entry
        {
            public HashSet<IPEndPoint> TargetForwarderEndPointHashSet = null!;
            public long ExpiresTick;
            public ushort NewTransactionId;
            public DnsUdpPacket OriginalRequestPacket = null!;
            public DnsMessageBase ModifiedRequestMessage = null!;
            public Func<Entry, DnsUdpPacket, DnsUdpPacket?> GenerateResponsePacketCallback = null!;
        }

        Entry?[] Table = new Entry[65536];
        ushort[] IdSeed = new ushort[65536];
        int IdSeedPos = 0;

        public ForwarderTranslateTable()
        {
            RandomizeTranslateIdSeedCritical();
        }

        public Entry? TryLookup(IPEndPoint targetForwarderEndPoint, ushort newTranscationId)
        {
            Entry? ret = null;

            long now = TickNow;

            GcCritical();

            int id = (int)newTranscationId;

            lock (Table)
            {
                var e = this.Table[id];

                if (e != null)
                {
                    if (e.TargetForwarderEndPointHashSet.Contains(targetForwarderEndPoint))
                    {
                        if (now < e.ExpiresTick)
                        {
                            ret = e;
                        }
                        this.Table[id] = null;
                    }
                }
            }
            return ret;
        }

        public Entry? TryCreateNew(HashSet<IPEndPoint> targetForwarderEndPointHashSet, long expiresTick, DnsUdpPacket originalRequestPacket, DnsMessageBase modifiedRequestMessage, Func<Entry, DnsUdpPacket, DnsUdpPacket?> generateResponsePacketCallback)
        {
            Entry e = new Entry
            {
                TargetForwarderEndPointHashSet = targetForwarderEndPointHashSet,
                ExpiresTick = expiresTick,
                GenerateResponsePacketCallback = generateResponsePacketCallback,
                OriginalRequestPacket = originalRequestPacket,
                ModifiedRequestMessage = modifiedRequestMessage,
            };

            GcCritical();

            lock (Table)
            {
                int newId = GetNextTranslateIdCritical();
                if (newId < 0)
                {
                    return null;
                }

                e.NewTransactionId = (ushort)newId;

                Table[newId] = e;
            }

            return e;
        }

        void RandomizeTranslateIdSeedCritical()
        {
            List<ushort> tmp = new List<ushort>();
            for (int i = 0; i < 65536; i++)
            {
                ushort us = (ushort)i;
                tmp.Add(us);
            }
            tmp = tmp._Shuffle().ToList();

            tmp.CopyTo(IdSeed);
        }

        int GcCounter = 0;

        [MethodImpl(Inline)]
        void GcCritical()
        {
            int c = ++GcCounter;
            if (c >= 1000)
            {
                c = 0;
                GcCoreCritical(Time.Tick64);
            }
        }

        void GcCoreCritical(long now)
        {
            for (int i = 0; i < 65536; i++)
            {
                Entry? e = Table[i];
                if (e != null)
                {
                    if (now >= e.ExpiresTick)
                    {
                        Table[i] = null;
                    }
                }
            }
        }

        int GetNextTranslateIdCritical()
        {
            for (int i = 0; i < 65536 * 2; i++)
            {
                ushort candidate = GetNextTranslateIdCandidateCritical();

                if (Table[candidate] == null)
                {
                    return candidate;
                }
            }
            return -1;
        }

        ushort GetNextTranslateIdCandidateCritical()
        {
            ushort ret = 0;
            ret = IdSeed[IdSeedPos];
            IdSeedPos++;
            if (IdSeedPos >= 65536)
            {
                RandomizeTranslateIdSeedCritical();
                IdSeedPos = 0;
            }
            return ret;
        }
    }

    public ForwarderTranslateTable FwdTranslateTable = new ForwarderTranslateTable();

    // ダイナミックレコードのコールバック関数
    public Func<EasyDnsResponderDynamicRecordCallbackRequest, EasyDnsResponderDynamicRecordCallbackResult?>? DynamicRecordCallback { get; set; }

    // TCP AXFR のコールバック関数
    public Func<EasyDnsResponderTcpAxfrCallbackRequest, Task>? TcpAxfrCallback { get; set; }

    // フォワーダのコールバック関数
    public Func<EasyDnsResponderForwarderRequestTransformerCallbackRequest, EasyDnsResponderForwarderRequestTransformerCallbackResult>? ForwarderRequestTransformerCallback { get; set; }

    // 内部データセット
    public class Record_A : Record
    {
        public IPAddress IPv4Address;
        public IPAddress IPv4SubnetMask;
        public int IPv4SubnetMaskLength;
        public bool IsSubnet;

        public Record_A(Zone parent, EasyDnsResponderRecord src) : base(parent, src)
        {
            string[] tokens = src.Contents._Split(StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries, ';', ',', ' ', '\t');
            string? tmp = tokens.ElementAtOrDefault(0);
            if (tmp._IsEmpty()) throw new CoresLibException("Contents is empty.");

            if (tmp._InStr("/") == false)
            {
                // 単体の IPv4 アドレス
                this.IPv4Address = IPAddress.Parse(tmp);
                this.IPv4SubnetMask = IPAddress.Broadcast;
                this.IPv4SubnetMaskLength = 32;
                this.IsSubnet = false;
            }
            else
            {
                // サブネットマスク付き IPv4 アドレス
                IPUtil.ParseIPAndSubnetMask(tmp, out this.IPv4Address, out this.IPv4SubnetMask);
                this.IPv4SubnetMaskLength = IPUtil.SubnetMaskToInt(this.IPv4SubnetMask);
                this.IsSubnet = !IPUtil.IsSubnetLenHostAddress(this.IPv4Address.AddressFamily, this.IPv4SubnetMaskLength);
            }

            if (this.IPv4Address.AddressFamily != AddressFamily.InterNetwork)
                throw new CoresLibException($"AddressFamily of '{tmp}' is not IPv4.");
        }

        public Record_A(Zone parent, EasyDnsResponderRecordSettings settings, string nameNormalized, IPAddress ipv4address, object? param = null) : base(parent, EasyDnsResponderRecordType.A, settings, nameNormalized, param)
        {
            this.IPv4Address = ipv4address;
            this.IPv4SubnetMask = IPAddress.Broadcast;
            this.IPv4SubnetMaskLength = 32;
            this.IsSubnet = false;

            if (this.IPv4Address.AddressFamily != AddressFamily.InterNetwork)
                throw new CoresLibException($"AddressFamily of '{this.IPv4Address}' is not IPv4.");
        }

        public Record_A Clone()
        {
            return (Record_A)this.MemberwiseClone();
        }

        protected override string ToStringForCompareImpl()
        {
            return this.IPv4Address.ToString();
        }
    }

    public class Record_AAAA : Record
    {
        public IPAddress IPv6Address;
        public IPAddress IPv6SubnetMask;
        public int IPv6SubnetMaskLength;
        public bool IsSubnet;

        public Record_AAAA(Zone parent, EasyDnsResponderRecord src) : base(parent, src)
        {
            string[] tokens = src.Contents._Split(StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries, ';', ',', ' ', '\t');
            string? tmp = tokens.ElementAtOrDefault(0);
            if (tmp._IsEmpty()) throw new CoresLibException("Contents is empty.");

            if (tmp._InStr("/") == false)
            {
                // 単体の IPv6 アドレス
                this.IPv6Address = IPAddress.Parse(tmp);
                this.IPv6SubnetMask = IPUtil.IPv6AllFilledAddressCache;
                this.IPv6SubnetMaskLength = 128;
                this.IsSubnet = false;
            }
            else
            {
                // サブネットマスク付き IPv6 アドレス
                IPUtil.ParseIPAndSubnetMask(tmp, out this.IPv6Address, out this.IPv6SubnetMask);
                this.IPv6SubnetMaskLength = IPUtil.SubnetMaskToInt(this.IPv6SubnetMask);
                this.IsSubnet = !IPUtil.IsSubnetLenHostAddress(this.IPv6Address.AddressFamily, this.IPv6SubnetMaskLength);
            }

            if (this.IPv6Address.AddressFamily != AddressFamily.InterNetworkV6)
                throw new CoresLibException($"AddressFamily of '{tmp}' is not IPv6.");

            this.IPv6Address.ScopeId = 0;
        }

        public Record_AAAA(Zone parent, EasyDnsResponderRecordSettings settings, string nameNormalized, IPAddress ipv6address, object? param = null) : base(parent, EasyDnsResponderRecordType.AAAA, settings, nameNormalized, param)
        {
            this.IPv6Address = ipv6address;
            this.IPv6SubnetMask = IPUtil.IPv6AllFilledAddressCache;
            this.IPv6SubnetMaskLength = 128;
            this.IsSubnet = false;

            if (this.IPv6Address.AddressFamily != AddressFamily.InterNetworkV6)
                throw new CoresLibException($"AddressFamily of '{this.IPv6Address}' is not IPv6.");
        }

        public Record_AAAA Clone()
        {
            return (Record_AAAA)this.MemberwiseClone();
        }

        protected override string ToStringForCompareImpl()
        {
            return this.IPv6Address.ToString();
        }
    }

    public class Record_NS : Record
    {
        public DomainName ServerName;
        public List<Record> GlueRecordList = new List<Record>();

        public Record_NS(Zone parent, EasyDnsResponderRecord src) : base(parent, src)
        {
            if (this.Name._InStr("*") || this.Name._InStr("?"))
            {
                throw new CoresLibException($"NS record doesn't allow wildcard names. Specified name: '{this.Name}'");
            }

            string[] tokens = src.Contents._Split(StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries, ';', ',', ' ', '\t', ';', ',', '=');
            string? nameServerName = tokens.ElementAtOrDefault(0);
            if (nameServerName._IsEmpty()) throw new CoresLibException("Contents is empty.");

            // ns1.example.org=1.2.3.4,5.6.7.8,2001::cafe のような書式で Glue レコードも記載されている場合がある。
            // この場合は、当該記載をパースする。

            List<IPAddress> glueIpList = new List<IPAddress>();
            if (tokens.Length >= 2)
            {
                for (int i = 1; i < tokens.Length; i++)
                {
                    string s = tokens[i];

                    if (s._IsFilled())
                    {
                        if (IPAddress.TryParse(s, out IPAddress? ip))
                        {
                            if (ip._IsIPv4OrIPv6AddressFaimly())
                            {
                                ip = ip._RemoveScopeId();
                                if (glueIpList.Contains(ip, IpComparer.Comparer) == false)
                                {
                                    glueIpList.Add(ip);
                                }
                            }
                        }
                    }
                }
            }

            this.ServerName = DomainName.Parse(nameServerName);

            var rootVirtualZone = new Zone(isVirtualRootZone: EnsureSpecial.Yes, this.ParentZone);

            foreach (var glueIp in glueIpList)
            {
                if (glueIp.AddressFamily == AddressFamily.InterNetwork)
                {
                    this.GlueRecordList.Add(new Record_A(rootVirtualZone, src.Settings ?? rootVirtualZone.Settings, this.ServerName.ToNormalizedFqdnFast(), glueIp));
                }
                else
                {
                    this.GlueRecordList.Add(new Record_AAAA(rootVirtualZone, src.Settings ?? rootVirtualZone.Settings, this.ServerName.ToNormalizedFqdnFast(), glueIp));
                }
            }

            if (this.ServerName.IsEmptyDomain()) throw new CoresLibException("NS server field is empty.");
        }

        public Record_NS(Zone parent, EasyDnsResponderRecordSettings settings, string nameNormalized, DomainName serverName, object? param = null) : base(parent, EasyDnsResponderRecordType.NS, settings, nameNormalized, param)
        {
            this.ServerName = serverName;
        }

        protected override string ToStringForCompareImpl()
        {
            return this.ServerName.ToString();
        }
    }

    public class Record_CNAME : Record
    {
        public DomainName CName;

        public Record_CNAME(Zone parent, EasyDnsResponderRecord src) : base(parent, src)
        {
            string[] tokens = src.Contents._Split(StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries, ';', ',', ' ', '\t');
            string? tmp = tokens.ElementAtOrDefault(0);
            if (tmp._IsEmpty()) throw new CoresLibException("Contents is empty.");

            this.CName = DomainName.Parse(tmp);

            if (this.CName.IsEmptyDomain()) throw new CoresLibException("CNAME field is empty.");
        }

        public Record_CNAME(Zone parent, EasyDnsResponderRecordSettings settings, string nameNormalized, DomainName cname, object? param = null) : base(parent, EasyDnsResponderRecordType.CNAME, settings, nameNormalized, param)
        {
            this.CName = cname;
        }

        protected override string ToStringForCompareImpl()
        {
            return this.CName.ToString();
        }
    }

    public class Record_SOA : Record
    {
        public DomainName MasterName;
        public DomainName ResponsibleName;
        public uint SerialNumber;
        public int RefreshIntervalSecs;
        public int RetryIntervalSecs;
        public int ExpireIntervalSecs;
        public int NegativeCacheTtlSecs;

        public Record_SOA(Zone parent, EasyDnsResponderRecord src) : base(parent, src)
        {
            if (this.Name._IsFilled()) throw new CoresLibException($"SOA record doesn't allow Name field. Name is not empty: '{this.Name}'");

            string[] tokens = src.Contents._Split(StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries, ';', ',', ' ', '\t');

            if (tokens.Length == 0) throw new CoresLibException("Contents is empty.");

            this.MasterName = DomainName.Parse(tokens.ElementAt(0));
            this.ResponsibleName = DomainName.Parse(tokens._ElementAtOrDefaultStr(1, "somebody.example.org."));

            this.SerialNumber = tokens.ElementAtOrDefault(2)._ToUInt();
            if (this.SerialNumber <= 0) this.SerialNumber = 1;

            this.RefreshIntervalSecs = tokens.ElementAtOrDefault(3)._ToInt();
            if (this.RefreshIntervalSecs <= 0) this.RefreshIntervalSecs = CoresConfig.EasyDnsResponderSettings.Default_RefreshIntervalSecs;

            this.RetryIntervalSecs = tokens.ElementAtOrDefault(4)._ToInt();
            if (this.RetryIntervalSecs <= 0) this.RetryIntervalSecs = CoresConfig.EasyDnsResponderSettings.Default_RetryIntervalSecs;

            this.ExpireIntervalSecs = tokens.ElementAtOrDefault(5)._ToInt();
            if (this.ExpireIntervalSecs <= 0) this.ExpireIntervalSecs = CoresConfig.EasyDnsResponderSettings.Default_ExpireIntervalSecs;

            this.NegativeCacheTtlSecs = tokens.ElementAtOrDefault(6)._ToInt();
            if (this.NegativeCacheTtlSecs <= 0) this.NegativeCacheTtlSecs = CoresConfig.EasyDnsResponderSettings.Default_NegativeCacheTtlSecs;
        }

        protected override string ToStringForCompareImpl()
        {
            return $"{MasterName} {ResponsibleName} {SerialNumber} {RefreshIntervalSecs} {RetryIntervalSecs} {ExpireIntervalSecs} {NegativeCacheTtlSecs}";
        }
    }

    public class Record_PTR : Record
    {
        public DomainName Ptr;

        public Record_PTR(Zone parent, EasyDnsResponderRecord src) : base(parent, src)
        {
            string[] tokens = src.Contents._Split(StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries, ';', ',', ' ', '\t');
            string? tmp = tokens.ElementAtOrDefault(0);
            if (tmp._IsEmpty()) throw new CoresLibException("Contents is empty.");

            this.Ptr = DomainName.Parse(tmp);

            if (this.Ptr.IsEmptyDomain()) throw new CoresLibException("PTR field is empty.");
        }

        public Record_PTR(Zone parent, EasyDnsResponderRecordSettings settings, string nameNormalized, DomainName ptr, object? param = null) : base(parent, EasyDnsResponderRecordType.PTR, settings, nameNormalized, param)
        {
            this.Ptr = ptr;
        }

        protected override string ToStringForCompareImpl()
        {
            return this.Ptr.ToString();
        }
    }

    public class Record_SRV : Record
    {
        public ushort Priority;
        public ushort Weight;
        public ushort Port;
        public DomainName Target;

        public Record_SRV(Zone parent, EasyDnsResponderRecord src) : base(parent, src)
        {
            string[] tokens = src.Contents._Split(StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries, ';', ',', '\t', ' ');

            if (tokens.Length == 0) throw new CoresLibException("Contents is empty.");
            if (tokens.Length < 4) throw new CoresLibException("SRV record contents must have priority, weight, port and target.");

            this.Priority = (ushort)tokens[0]._ToUInt();
            this.Weight = (ushort)tokens[1]._ToUInt();
            this.Port = (ushort)tokens[2]._ToUInt();
            this.Target = DomainName.Parse(tokens[3]._NonNullTrim());
        }

        public Record_SRV(Zone parent, EasyDnsResponderRecordSettings settings, string nameNormalized, ushort priority, ushort weight, ushort port, DomainName target, object? param = null) : base(parent, EasyDnsResponderRecordType.SRV, settings, nameNormalized, param)
        {
            this.Priority = priority;
            this.Weight = weight;
            this.Port = port;
            this.Target = target;
        }

        protected override string ToStringForCompareImpl()
        {
            return $"{Priority} {Weight} {Port} {Target.ToString()}";
        }
    }

    public class Record_MX : Record
    {
        public DomainName MailServer;
        public ushort Preference;

        public Record_MX(Zone parent, EasyDnsResponderRecord src) : base(parent, src)
        {
            string[] tokens = src.Contents._Split(StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries, ';', ',', '\t', ' ');

            if (tokens.Length == 0) throw new CoresLibException("Contents is empty.");

            this.MailServer = DomainName.Parse(tokens.ElementAt(0));

            this.Preference = (ushort)tokens.ElementAtOrDefault(1)._ToUInt();
            if (this.Preference <= 0) this.Preference = CoresConfig.EasyDnsResponderSettings.Default_MxPreference;
        }

        public Record_MX(Zone parent, EasyDnsResponderRecordSettings settings, string nameNormalized, DomainName mailServer, ushort preference, object? param = null) : base(parent, EasyDnsResponderRecordType.MX, settings, nameNormalized, param)
        {
            this.MailServer = mailServer;
            this.Preference = preference;
        }

        protected override string ToStringForCompareImpl()
        {
            return this.MailServer.ToString() + " " + this.Preference;
        }
    }

    public class Record_CAA : Record
    {
        public byte Flags;
        public string Tag;
        public string Value;

        public Record_CAA(Zone parent, EasyDnsResponderRecord src) : base(parent, src)
        {
            string[] tokens = src.Contents._Split(StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries, '\t', ' ');

            if (tokens.Length < 3) throw new CoresLibException("Contents must have <flag> <tag> <value>.");

            this.Flags = (byte)tokens.ElementAt(0)._ToInt();
            this.Tag = tokens.ElementAt(1);
            this.Value = tokens.ElementAt(2);
        }

        public Record_CAA(Zone parent, EasyDnsResponderRecordSettings settings, string nameNormalized, byte flags, string tag, string value, object? param = null) : base(parent, EasyDnsResponderRecordType.CAA, settings, nameNormalized, param)
        {
            this.Flags = flags;
            this.Tag = tag;
            this.Value = value;
        }

        protected override string ToStringForCompareImpl()
        {
            return $"{this.Flags} {this.Tag} {this.Value}";
        }
    }

    public class Record_TXT : Record
    {
        public string TextData;

        public Record_TXT(Zone parent, EasyDnsResponderRecord src) : base(parent, src)
        {
            this.TextData = src.Contents._NonNull();
        }

        public Record_TXT(Zone parent, EasyDnsResponderRecordSettings settings, string nameNormalized, string textData, object? param = null) : base(parent, EasyDnsResponderRecordType.TXT, settings, nameNormalized, param)
        {
            this.TextData = textData._NonNull();
        }

        protected override string ToStringForCompareImpl()
        {
            return this.TextData;
        }
    }

    public class Record_Dynamic : Record
    {
        public string CallbackId;

        public Record_Dynamic(Zone parent, EasyDnsResponderRecord src) : base(parent, src)
        {
            switch (src.Type)
            {
                case EasyDnsResponderRecordType.Any:
                case EasyDnsResponderRecordType.A:
                case EasyDnsResponderRecordType.AAAA:
                case EasyDnsResponderRecordType.CNAME:
                case EasyDnsResponderRecordType.MX:
                case EasyDnsResponderRecordType.NS:
                case EasyDnsResponderRecordType.PTR:
                case EasyDnsResponderRecordType.TXT:
                case EasyDnsResponderRecordType.CAA:
                case EasyDnsResponderRecordType.SRV:
                    this.CallbackId = src.Contents._NonNull();

                    if (this.CallbackId._IsEmpty()) throw new CoresLibException("Callback ID is empty.");
                    return;
            }

            throw new CoresLibException($"Invalid Dynamic Record Type '{this.Name}': {src.Type}");
        }

        protected override string ToStringForCompareImpl()
        {
            return this.CallbackId;
        }
    }

    public abstract class Record
    {
        [JsonIgnore]
        public Zone ParentZone;
        public string Name;
        [JsonConverter(typeof(StringEnumConverter))]
        public EasyDnsResponderRecordType Type;
        public EasyDnsResponderRecordSettings Settings;

        [JsonIgnore]
        public EasyDnsResponderRecord? SrcRecord;

        [JsonIgnore]
        public object? Param;

        protected abstract string ToStringForCompareImpl();

        readonly CachedProperty<string>? _StringForCompareCache;
        public string ToStringForCompare() => _StringForCompareCache ?? "";

        public Record(Zone parent, EasyDnsResponderRecordType type, EasyDnsResponderRecordSettings settings, string nameNormalized, object? param = null)
        {
            this.ParentZone = parent;
            this.Type = type;
            this.Settings = settings;
            this.Name = nameNormalized;
            this.Param = param;

            this._StringForCompareCache = new CachedProperty<string>(getter: () =>
            {
                return $"{Name} {Type} {this.ToStringForCompareImpl()}";
            });
        }

        public Record(Zone parent, EasyDnsResponderRecord src)
        {
            this.ParentZone = parent;

            this.Param = src.Param;

            this.Settings = (src.Settings ?? parent.Settings)._CloneDeep();

            this.Name = src.Name._NormalizeFqdn();

            this.Type = src.Type;

            this.SrcRecord = src._CloneDeep();

            this._StringForCompareCache = new CachedProperty<string>(getter: () =>
            {
                return $"{Name} {Type} {this.ToStringForCompareImpl()}";
            });
        }

        public static Record CreateFrom(Zone parent, EasyDnsResponderRecord src)
        {
            if (src.Attribute.Bit(EasyDnsResponderRecordAttribute.DynamicRecord))
            {
                // ダイナミックレコード
                return new Record_Dynamic(parent, src);
            }

            switch (src.Type)
            {
                case EasyDnsResponderRecordType.A:
                    return new Record_A(parent, src);

                case EasyDnsResponderRecordType.AAAA:
                    return new Record_AAAA(parent, src);

                case EasyDnsResponderRecordType.NS:
                    return new Record_NS(parent, src);

                case EasyDnsResponderRecordType.CNAME:
                    return new Record_CNAME(parent, src);

                case EasyDnsResponderRecordType.SOA:
                    return new Record_SOA(parent, src);

                case EasyDnsResponderRecordType.PTR:
                    return new Record_PTR(parent, src);

                case EasyDnsResponderRecordType.MX:
                    return new Record_MX(parent, src);

                case EasyDnsResponderRecordType.TXT:
                    return new Record_TXT(parent, src);

                case EasyDnsResponderRecordType.CAA:
                    return new Record_CAA(parent, src);

                case EasyDnsResponderRecordType.SRV:
                    return new Record_SRV(parent, src);
            }

            throw new CoresLibException($"Unknown record type: {src.Type}");
        }

        public DnsRecordBase? ToDnsLibRecordBase(DomainName domainName, DateTimeOffset soaBootTime, int soaVersion)
        {
            int ttl = this.Settings.TtlSecs;

            switch (this)
            {
                case Record_A a:
                    return new ARecord(domainName, ttl, a.IPv4Address);

                case Record_AAAA aaaa:
                    return new AaaaRecord(domainName, ttl, aaaa.IPv6Address);

                case Record_NS ns:
                    return new NsRecord(domainName, ttl, ns.ServerName);

                case Record_CNAME cname:
                    return new CNameRecord(domainName, ttl, cname.CName);

                case Record_SOA soa:
                    uint serialNumber = soa.SerialNumber;
                    if (serialNumber == Consts.Numbers.MagicNumber_u32)
                    {
                        serialNumber = DnsUtil.GenerateSoaSerialNumberFromDateTime(soaBootTime.AddSeconds(soaVersion));
                    }
                    else if (serialNumber == Consts.Numbers.MagicNumberB_u32)
                    {
                        serialNumber = DnsUtil.GenerateSoaSerialNumberFromDateTime_Legacy(DtOffsetNow);
                    }
                    return new SoaRecord(domainName, ttl, soa.MasterName, soa.ResponsibleName, serialNumber, soa.RefreshIntervalSecs, soa.RetryIntervalSecs, soa.ExpireIntervalSecs, soa.NegativeCacheTtlSecs);

                case Record_PTR ptr:
                    return new PtrRecord(domainName, ttl, ptr.Ptr);

                case Record_MX mx:
                    return new MxRecord(domainName, ttl, mx.Preference, mx.MailServer);

                case Record_TXT txt:
                    return new TxtRecord(domainName, ttl, txt.TextData);

                case Record_CAA caa:
                    return new CAARecord(domainName, ttl, caa.Flags, caa.Tag, caa.Value);

                case Record_SRV srv:
                    return new SrvRecord(domainName, ttl, srv.Priority, srv.Weight, srv.Port, srv.Target);
            }

            return null;
        }

        public DnsRecordBase? ToDnsLibRecordBase(DnsQuestion q, DateTimeOffset soaBootTime, int soaVersion)
            => ToDnsLibRecordBase(q.Name, soaBootTime, soaVersion);
    }

    // フォワーダの種類
    [Flags]
    public enum ForwarderType
    {
        DomainName = 0,
        WildcardFqdn,
        Subnet,
    }

    // 内部フォワーダデータ
    public class Forwarder
    {
        public DataSet ParentDataSet;
        public ForwarderType Type;
        public string DomainFqdn = "";
        public string WildcardFqdn = "";
        public IPAddress Subnet_IpNetwork = IPAddress.Any;
        public IPAddress Subnet_SubnetMask = IPAddress.Broadcast;
        public int Subnet_SubnetLength = 32;
        public int TimeoutMsecs;
        public string CallbackId = "";
        public QueryStringList ArgsList;
        public List<IPEndPoint> TargetServersList = new List<IPEndPoint>();
        public EasyDnsResponderForwarder Src;

        public Forwarder(DataSet parent, EasyDnsResponderForwarder src)
        {
            this.ParentDataSet = parent;

            string selector = src.Selector;

            if (selector._InStri("/"))
            {
                // IP アドレス / サブネットマスク形式
                this.Type = ForwarderType.Subnet;
                IPUtil.ParseIPAndSubnetMask(selector, out Subnet_IpNetwork, out Subnet_SubnetMask);
                this.Subnet_SubnetLength = IPUtil.SubnetMaskToInt(Subnet_SubnetMask);
            }
            else if (selector._InStri("*") || selector._InStri("?"))
            {
                // ワイルドカード形式
                this.Type = ForwarderType.WildcardFqdn;
                this.WildcardFqdn = selector.ToLowerInvariant().Trim();
            }
            else
            {
                // ドメイン名形式
                this.Type = ForwarderType.DomainName;
                this.DomainFqdn = selector._NormalizeFqdn();

                if (this.DomainFqdn._IsEmpty() || this.DomainFqdn._IsValidFqdn() == false)
                {
                    throw new CoresException($"Domain name '{src.Selector}' is an invalid FQDN.");
                }
            }

            this.TimeoutMsecs = src.TimeoutMsecs;
            if (this.TimeoutMsecs <= 0) this.TimeoutMsecs = CoresConfig.EasyDnsResponderSettings.Default_ForwarderTimeoutMsecs;

            this.ArgsList = src.ArgsList;

            string[] targetToken = src.TargetServers._Split(StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries, " ", "\t", ";", ",", "/");

            HashSet<string> distinctCheck = new HashSet<string>(StrCmpi);

            foreach (var target in targetToken)
            {
                if (IPUtil.TryParseHostPort(target, out string host, out int port, Consts.Ports.Dns))
                {
                    IPEndPoint ep = new IPEndPoint(IPUtil.StrToIP(host)!, port);

                    string test = ep.ToString();

                    if (distinctCheck.Add(test))
                    {
                        this.TargetServersList.Add(ep);
                    }
                }
            }

            this.CallbackId = src.CallbackId._NonNull();
            this.Src = src._CloneDeep();
        }
    }

    // 内部ゾーンデータ
    public class Zone
    {
        public DataSet ParentDataSet;
        public string DomainFqdn;
        public DomainName DomainName;
        public EasyDnsResponderRecordSettings Settings;
        public EasyDnsResponderZone SrcZone;
        public string TcpAxfrAllowedAcl = "";
        public string NotifyServers = "";

        public string SettingDigest = "";

        public List<Record> RecordList = new List<Record>();
        public StrDictionary<List<Record>> RecordDictByName = new StrDictionary<List<Record>>();

        public HashSet<string> SubDomainList = new HashSet<string>(); // レコードが 1 つ以上存在するサブドメインのリスト

        public Record_SOA SOARecord;

        public List<Record> WildcardAnyRecordList = new List<Record>(); // "*" という名前のワイルドカード
        public KeyValueList<string, List<Record>> WildcardEndWithRecordList = new KeyValueList<string, List<Record>>(); // "*abc" または "*.abc" という先頭ワイルドカード
        public KeyValueList<string, List<Record>> WildcardInStrRecordList = new KeyValueList<string, List<Record>>(); // "*abc*" とか "abc*def" とか "abc?def" という複雑なワイルドカード

        public List<Record_NS> NSRecordList = new List<Record_NS>(); // このゾーンそのものの NS レコード
        public StrDictionary<List<Record_NS>> NSDelegationRecordList = new StrDictionary<List<Record_NS>>(); // サブドメイン権限委譲レコード

        public bool Has_WildcardAnyRecordList = false;
        public bool Has_WildcardEndWithRecordList = false;
        public bool Has_WildcardInStrRecordList = false;
        public bool Has_NSDelegationRecordList = false;

        public bool IsVirtualRootZone = false;

        public int Version = 0;

        // Glue レコード等の内部的生成に便宜上必要となるルートゾーン (.) の定義
        public Zone(EnsureSpecial isVirtualRootZone, Zone templateZone)
            : this(templateZone.ParentDataSet,
                  new EasyDnsResponderZone
                  {
                      DefaultSettings = templateZone.Settings,
                      DomainName = ".",
                  })
        {
            this.IsVirtualRootZone = true;
        }

        // ゾーンの定義
        public Zone(DataSet parent, EasyDnsResponderZone src)
        {
            this.ParentDataSet = parent;

            this.Settings = (src.DefaultSettings ?? parent.Settings)._CloneDeep();

            this.DomainFqdn = src.DomainName._NormalizeFqdn();

            this.DomainName = new DomainName(this.DomainFqdn._Split(StringSplitOptions.None, '.').AsMemory());

            this.TcpAxfrAllowedAcl = src.TcpAxfrAllowedAcl;

            this.NotifyServers = src.NotifyServers;

            Record_SOA? soa = null;

            // レコード情報のコンパイル
            foreach (var srcRecord in src.RecordList)
            {
                try
                {
                    var record = Record.CreateFrom(this, srcRecord);

                    if (record.Type != EasyDnsResponderRecordType.SOA)
                    {
                        string tmp1 = record.ToStringForCompare();
                        if (this.RecordList.Where(x => x.Type == record.Type && x.ToStringForCompare() == tmp1).Any() == false)
                        {
                            // 全く同じ内容のレコードが 2 つ追加されることは禁止する。最初の 1 つ目のみをリストに追加するのである。
                            this.RecordList.Add(record);
                        }
                    }
                    else
                    {
                        // SOA レコード
                        if (soa != null)
                        {
                            // SOA レコードは 2 つ以上指定できない
                            $"Domain '{this.DomainFqdn}': SOA record is duplicating."._Error();
                        }
                        else
                        {
                            soa = (Record_SOA)record;
                        }
                    }
                }
                catch (Exception ex)
                {
                    // 1 つのレコード情報のコンパイル失敗
                    $"Domain '{this.DomainFqdn}': {ex._GetOneLineExceptionString()}"._Error();
                }
            }

            // SOA レコードが無い場合は、適当にでっち上げる
            if (soa == null)
            {
                soa = new Record_SOA(this,
                    new EasyDnsResponderRecord
                    {
                        Type = EasyDnsResponderRecordType.SOA,
                        Attribute = EasyDnsResponderRecordAttribute.None,
                        Contents = this.DomainFqdn._FilledOrDefault("."),
                    });
            }

            this.SOARecord = soa;

            this.SubDomainList.Add(""); // サブドメインリストにまずこのゾーン自体を追加する

            // レコード情報を検索を高速化するためにハッシュテーブル等として並べて保持する
            foreach (var r in this.RecordList)
            {
                if (r.Type == EasyDnsResponderRecordType.SOA) { } // SOA レコードは追加しない
                else if (r.Type == EasyDnsResponderRecordType.NS) { } // NS レコードは後で特殊な処理を行なう
                else
                {
                    // 普通の種類のレコード (A など)
                    if (r.Name._InStr("*") || r.Name._InStr("?"))
                    {
                        // ワイルドカードレコード
                        if (r.Name == "*")
                        {
                            // any ワイルドカード
                            this.WildcardAnyRecordList.Add(r);
                        }
                        else if (r.Name.StartsWith("*") && r.Name.Substring(1)._InStr("*") == false && r.Name.Substring(1)._InStr("?") == false && r.Name.Substring(1).Length >= 1)
                        {
                            // 先頭ワイルドカード (*abc)
                            this.WildcardEndWithRecordList.GetSingleOrNew(r.Name.Substring(1), () => new List<Record>(), StrComparer.IgnoreCaseComparer).Add(r);
                        }
                        else
                        {
                            // 複雑なワイルドカード (abc*def とか abc*def といったもの)
                            this.WildcardInStrRecordList.GetSingleOrNew(r.Name, () => new List<Record>(), StrComparer.IgnoreCaseComparer).Add(r);
                        }
                    }
                    else
                    {
                        // 非ワイルドカードレコード
                        this.RecordDictByName._GetOrNew(r.Name).Add(r);

                        // レコード名が a.b.c の場合、 a.b.c, b.c, c をサブドメイン存在リストに追加する
                        var labels = r.Name.Split(".").AsSpan();
                        int numLabels = labels.Length;
                        for (int i = 0; i < numLabels; i++)
                        {
                            this.SubDomainList.Add(labels.Slice(i)._Combine(".", estimatedLength: r.Name.Length));
                        }
                    }
                }
            }

            foreach (var r in this.RecordList.Where(x => x.Type == EasyDnsResponderRecordType.NS))
            {
                // NS レコードに関する処理
                if (r.Name._IsEmpty())
                {
                    // これは、このゾーンそのものに関する NS 情報である。Name は空文字である。
                    this.NSRecordList.Add((Record_NS)r);
                }
                else
                {
                    // これは、サブドメインに関する NS 情報である。つまり、Name にはサブドメイン名が入っている。
                    // これは、DNS における権限委譲 (delegate) と呼ばれる。
                    // たとえば abc.def である。
                    // そこで、まずこの NS サブドメインが定義済みサブドメイン (普通のサブドメイン) の一覧と重複しないかどうか検査する。
                    if (this.SubDomainList.Contains(r.Name))
                    {
                        // 一致するのでエラーとする。つまり、普通のサブドメインが存在する場合、同じ名前の NS サブドメインの登録は禁止するのである。
                        $"NS record Name: {r.Name} is duplicating with the existing sub domain record."._Error();

                        // エラーを記録するものの、この関数は失敗させない。
                        continue;
                    }

                    // 問題なければ、NS 権限委譲レコードとして追加する。
                    this.NSDelegationRecordList._GetOrNew(r.Name).Add((Record_NS)r);
                }
            }

            // 先頭ワイルドカードリストと複雑なワイルドカードリストは、文字列長で逆ソートする。
            // つまり、できるだけ文字列長が長い候補が優先してマッチするようにするのである。
            this.WildcardEndWithRecordList._DoSortBy(x => x.OrderByDescending(y => y.Key.Length).ThenByDescending(y => y.Key));
            this.WildcardInStrRecordList._DoSortBy(x => x.OrderByDescending(y => y.Key.Length).ThenByDescending(y => y.Key));

            this.Has_WildcardAnyRecordList = this.WildcardAnyRecordList.Any();
            this.Has_WildcardEndWithRecordList = this.WildcardEndWithRecordList.Any();
            this.Has_WildcardInStrRecordList = this.WildcardInStrRecordList.Any();
            this.Has_NSDelegationRecordList = this.NSDelegationRecordList.Any();

            this.SrcZone = src._CloneDeep();

            // ダイジェスト値の保存
            this.SettingDigest = this.SrcZone._CalcObjectDigestAsJson()._GetHexString();
        }

        public SearchResult Search(string hostLabelNormalized, ReadOnlyMemory<string> hostLabelSpan)
        {
            List<Record>? answers = null;
            List<Record> glueRecords = new List<Record>();

            bool isWildcardMatch = false;

            // まず完全一致するものがないか確かめる
            if (this.RecordDictByName.TryGetValue(hostLabelNormalized, out List<Record>? found))
            {
                // 完全一致あり
                answers = found._CloneListFast();
            }

            if (answers == null)
            {
                if (hostLabelSpan.Length >= 1)
                {
                    // 完全一致がなければ、次に NS レコードによって権限委譲されているサブドメインがあるかどうか確認する。
                    // この場合、クエリサブドメイン名が a.b.c の場合、
                    // a.b.c、b.c、c の順で検索し、最初に発見されたものを NS 委譲されているサブドメインとして扱う。
                    for (int i = 0; i < hostLabelSpan.Length; i++)
                    {
                        if (this.NSDelegationRecordList.TryGetValue(hostLabelSpan.Slice(i)._Combine(".", estimatedLength: hostLabelNormalized.Length), out List<Record_NS>? found2))
                        {
                            // 権限委譲ドメイン情報が見つかった
                            SearchResult ret2 = new SearchResult
                            {
                                SOARecord = this.SOARecord,
                                Zone = this,
                                RecordList = found2.Cast<Record>().ToList(),
                                ResultFlags = SearchResultFlags.SubDomainIsDelegated,
                                RequestHostName = hostLabelNormalized,
                            };

                            return ret2;
                        }
                    }
                }
            }

            if (answers == null)
            {
                if (this.Has_WildcardEndWithRecordList) // 高速化 (効果があるかどうかは不明)
                {
                    // もし完全一致するものが 1 つも無ければ、
                    // 先頭ワイルドカード一致を検索し、一致するものがないかどうか調べる
                    foreach (var r in this.WildcardEndWithRecordList)
                    {
                        if (hostLabelNormalized.EndsWith(r.Key))
                        {
                            // 後方一致あり
                            answers = r.Value._CloneListFast();
                            isWildcardMatch = true;
                            break;
                        }
                    }
                }
            }

            if (answers == null)
            {
                if (this.Has_WildcardInStrRecordList) // 高速化 (効果があるかどうかは不明)
                {
                    // もし完全一致または後方一致するものが 1 つも無ければ、
                    // 複雑なワイルドカード一致を検索し、一致するものがないかどうか調べる
                    foreach (var r in this.WildcardInStrRecordList)
                    {
                        if (hostLabelNormalized._WildcardMatch(r.Key))
                        {
                            // 一致あり
                            answers = r.Value._CloneListFast();
                            isWildcardMatch = true;
                            break;
                        }
                    }
                }
            }

            if (answers == null)
            {
                if (this.Has_WildcardAnyRecordList) // 高速化 (効果があるかどうかは不明)
                {
                    // これまででまだ一致するものが無ければ、
                    // any アスタリスクレコードがあればそれを返す
                    answers = this.WildcardAnyRecordList._CloneListFast();

                    isWildcardMatch = true;
                }
            }

            if (answers != null && isWildcardMatch)
            {
                // この時点でワイルドカード一致による結果が整っている場合で、
                // サブネットを示す A/AAAA レコードがある場合は、適切なフィルタを実施する
                List<Record> newList = new List<Record>(answers.Count);

                for (int i = 0; i < answers.Count; i++)
                {
                    var answer = answers[i];
                    bool ok = true;
                    if (answer is Record_A a && a.IsSubnet)
                    {
                        ok = false;
                        if (IPUtil.TryParseWildCardDnsFqdn(hostLabelNormalized, out IPAddress? embedIp)) // ホスト名部分に埋め込まれている IP アドレスをパースする
                        {
                            // このパースされた IP アドレスがサブネット範囲に属するかどうか検査する
                            if (IPUtil.IsInSameNetwork(a.IPv4Address, embedIp, a.IPv4SubnetMask, true))
                            {
                                // 宜しい
                                ok = true;

                                var new_a = a.Clone();
                                new_a.IPv4Address = embedIp;
                                new_a.IPv4SubnetMask = IPAddress.Broadcast;
                                new_a.IsSubnet = false;
                                answer = new_a;
                            }
                        }
                    }
                    else if (answer is Record_AAAA aaaa && aaaa.IsSubnet)
                    {
                        ok = false;
                        if (IPUtil.TryParseWildCardDnsFqdn(hostLabelNormalized, out IPAddress? embedIp)) // ホスト名部分に埋め込まれている IP アドレスをパースする
                        {
                            // このパースされた IP アドレスがサブネット範囲に属するかどうか検査する
                            if (IPUtil.IsInSameNetwork(aaaa.IPv6Address, embedIp, aaaa.IPv6SubnetMask, true))
                            {
                                // 宜しい
                                ok = true;

                                var new_aaaa = aaaa.Clone();
                                new_aaaa.IPv6Address = embedIp;
                                new_aaaa.IPv6SubnetMask = IPUtil.IPv6AllFilledAddressCache;
                                new_aaaa.IsSubnet = false;
                                answer = new_aaaa;
                            }
                        }
                    }

                    if (ok)
                    {
                        newList.Add(answer);
                    }
                }

                answers = newList;

                // フィルタを通り抜けた結果が 1 つも無い場合は、この時点ではまだ not found 状態とする
                if (answers.Any() == false)
                {
                    answers = null;
                }
            }

            // この状態でまだ一致するものがなければ、サブドメイン一覧に一致する場合は空リストを返し、
            // いずれのサブドメインにも一致しない場合は null を返す。(null の場合、DNS 的には NXDOMAIN を意味することとする。)
            if (answers == null)
            {
                if (hostLabelSpan.Length == 0)
                {
                    // サブドメイン名がない (つまり、このドメインと全く一緒) の場合は、空リストを返す。
                    answers = new List<Record>();
                }
                else
                {
                    for (int i = 0; i < hostLabelSpan.Length; i++)
                    {
                        if (this.SubDomainList.Contains(hostLabelSpan.Slice(i)._Combine(".", estimatedLength: hostLabelNormalized.Length)))
                        {
                            // いずれかの階層でサブドメインリストが見つかった
                            answers = new List<Record>();
                            break;
                        }
                    }

                    // いずれの階層でもサブドメインリストが見つからなかった場合は、answers は null のままとなる。
                }
            }

            if (answers != null)
            {
                // このゾーン名を完全一致でクエリをしてきている場合、このゾーンに関する NS と SOA レコードも追加する
                if (hostLabelSpan.Length == 0)
                {
                    // SOA
                    answers.Add(this.SOARecord);

                    // Glue
                    List<Record> glueRecordsListTmp = new List<Record>();

                    var rootVirtualZone = new Zone(isVirtualRootZone: EnsureSpecial.Yes, this);

                    foreach (var ns in this.NSRecordList)
                    {
                        answers.Add(ns);

                        // Glue レコードの追記 (明示的な指定)
                        foreach (var glue in ns.GlueRecordList)
                        {
                            glueRecordsListTmp.Add(glue);
                        }

                        // Glue レコードの追記 (暗黙的な指定、A または AAAA が存在すれば Glue レコードとみなす)
                        string nsFqdn = ns.ServerName.ToNormalizedFqdnFast();
                        var found2 = this.ParentDataSet.Search(nsFqdn);
                        if (found2 != null && found2.RecordList != null)
                        {
                            foreach (var record in found2.RecordList.Where(x => x.Type == EasyDnsResponderRecordType.A || x.Type == EasyDnsResponderRecordType.AAAA))
                            {
                                switch (record)
                                {
                                    case Record_A a:
                                        if (a.IsSubnet == false)
                                        {
                                            glueRecordsListTmp.Add(new Record_A(rootVirtualZone, a.Settings, nsFqdn, a.IPv4Address));
                                        }
                                        break;

                                    case Record_AAAA aaaa:
                                        if (aaaa.IsSubnet == false)
                                        {
                                            glueRecordsListTmp.Add(new Record_AAAA(rootVirtualZone, aaaa.Settings, nsFqdn, aaaa.IPv6Address));
                                        }
                                        break;
                                }
                            }
                        }
                    }

                    // Glue 重複排除
                    HashSet<string> glueRecordDistinctHash = new HashSet<string>();

                    foreach (var glue in glueRecordsListTmp)
                    {
                        string test = glue.Name + "." + glue.ParentZone.DomainFqdn + " = " + glue.ToStringForCompare();

                        if (glueRecordDistinctHash.Contains(test) == false)
                        {
                            glueRecordDistinctHash.Add(test);

                            glueRecords.Add(glue);
                        }
                    }
                }
            }

            SearchResult ret = new SearchResult
            {
                RecordList = answers,
                GlueRecordListForNs = glueRecords,
                SOARecord = this.SOARecord,
                Zone = this,
                ResultFlags = (answers == null ? SearchResultFlags.NotFound : SearchResultFlags.NormalAnswer),
                RequestHostName = hostLabelNormalized,
            };

            return ret;
        }
    }

    public class DataSet
    {
        // 内部データの実体
        public StrDictionary<Zone> ZoneDict = new StrDictionary<Zone>();
        public EasyDnsResponderRecordSettings Settings;

        public StrDictionary<Forwarder> Forwarder_DomainBasedDict = new StrDictionary<Forwarder>();
        public FullRoute46<Forwarder> Forwarder_SubnetBasedRadixTrie = new FullRoute46<Forwarder>();
        public List<Forwarder> Forwarder_WildcardBasedList = new List<Forwarder>();
        public bool HasForwarder_DomainBased = false;
        public bool HasForwarder_SubnetBased = false;
        public bool HasForwarder_WildcardBased = false;

        // Settings からコンパイルする
        public DataSet(EasyDnsResponderSettings src)
        {
            this.Settings = (src.DefaultSettings ?? new EasyDnsResponderRecordSettings())._CloneDeep();

            // ゾーン情報のコンパイル
            foreach (var srcZone in src.ZoneList)
            {
                var zone = new Zone(this, srcZone);

                this.ZoneDict.Add(zone.DomainFqdn, zone);
            }

            // フォワーダ情報のコンパイル
            foreach (var srcForwarder in src.ForwarderList)
            {
                var forwarder = new Forwarder(this, srcForwarder);

                switch (forwarder.Type)
                {
                    case ForwarderType.DomainName:
                        // ドメイン名に基づくフォワーダ
                        this.Forwarder_DomainBasedDict.Add(forwarder.DomainFqdn, forwarder);
                        this.HasForwarder_DomainBased = true;
                        break;

                    case ForwarderType.Subnet:
                        // サブネットに基づくフォワーダ
                        this.Forwarder_SubnetBasedRadixTrie.Insert(forwarder.Subnet_IpNetwork, forwarder.Subnet_SubnetLength, forwarder);
                        this.HasForwarder_SubnetBased = true;
                        break;

                    case ForwarderType.WildcardFqdn:
                        // ワイルドカード文字列に基づくフォワーダ
                        this.Forwarder_WildcardBasedList.Add(forwarder);
                        this.HasForwarder_WildcardBased = true;
                        break;
                }
            }

            // ワイルドカード文字列に基づくフォワーダは文字列長が長い順にソートする
            this.Forwarder_WildcardBasedList._DoSortBy(x => x.OrderByDescending(y => y.WildcardFqdn.Length).ThenByDescending(y => y.WildcardFqdn));
        }

        public Zone? SearchExactMatchDnsZone(string fqdnNormalized)
        {
            if (this.ZoneDict.TryGetValue(fqdnNormalized, out Zone? ret))
            {
                return ret;
            }

            return null;
        }

        // ゾーン検索
        public Zone? SearchLongestMatchDnsZone(string fqdnNormalized, out string hostLabelStr, out ReadOnlyMemory<string> hostLabels)
        {
            Zone? zone = DnsUtil.SearchLongestMatchDnsZone(this.ZoneDict, fqdnNormalized, out hostLabelStr, out hostLabels);

            return zone;
        }

        // クエリ検索
        public SearchResult? Search(string fqdnNormalized)
        {
            Zone? zone = SearchLongestMatchDnsZone(fqdnNormalized, out string hostLabelStr, out ReadOnlyMemory<string> hostLabels);

            if (zone == null)
            {
                // 一致するゾーンが 1 つもありません！
                // DNS 的には Refused を意味することとする。
                return null;
            }

            return zone.Search(hostLabelStr, hostLabels);
        }
    }

    // 検索要求
    public class SearchRequest
    {
        public string FqdnNormalized { init; get; } = null!;
        public DnsUdpPacket RequestPacket { init; get; } = null!;
    }

    [Flags]
    public enum SearchResultFlags
    {
        NormalAnswer = 0,
        SubDomainIsDelegated = 1,
        NotFound = 2,
    }

    // 検索結果
    public class SearchResult
    {
        public List<Record>? RecordList { get; set; } = null; // null: サブドメインが全く存在しない 空リスト: サブドメインは存在するものの、レコードは存在しない
        public List<Record>? GlueRecordListForNs { get; set; } = null;

        [JsonIgnore]
        public Zone Zone { get; set; } = null!;
        public string ZoneDomainFqdn => Zone.DomainFqdn;
        public DomainName ZoneDomainName => Zone.DomainName;
        public string RequestHostName { get; set; } = null!;
        public Record_SOA SOARecord { get; set; } = null!;
        [JsonConverter(typeof(StringEnumConverter))]
        public SearchResultFlags ResultFlags { get; set; } = SearchResultFlags.NormalAnswer;
        public ReturnCode RaiseCustomError { get; set; } = ReturnCode.NoError;

        public List<DnsUdpPacket>? AlternativeSendPackets { get; set; } = null; // 追加送信パケット
    }

    DataSet? CurrentDataSet = null;

    string OldWholeDigest = "";

    readonly CriticalSection<EasyDnsResponder> ApplySettingsLock = new CriticalSection<EasyDnsResponder>();

    public readonly DateTimeOffset BootDateTime = DtOffsetNow;

    StrDictionary<int> ZoneVersionsDict = new StrDictionary<int>(StrCmpi);

    public void ApplySetting(EasyDnsResponderSettings setting)
    {
        string newWholeDigest = setting._CalcObjectDigestAsJson()._GetHexString();

        lock (ApplySettingsLock)
        {
            if (newWholeDigest != OldWholeDigest) // setting 全体のハッシュを確認し、変化があった場合のみ読み込む
            {
                OldWholeDigest = newWholeDigest;

                var dataSet = new DataSet(setting);

                // ゾーン情報の元データに変化があったかどうか確認する
                foreach (var zone in dataSet.ZoneDict.Values)
                {
                    string newZoneDigest = zone.SettingDigest;

                    string oldZoneDigest = "";
                    if (this.CurrentDataSet != null)
                    {
                        var oldZone = this.CurrentDataSet.ZoneDict._GetOrDefault(zone.DomainFqdn);
                        if (oldZone != null)
                        {
                            oldZoneDigest = oldZone.SettingDigest;
                        }
                    }

                    if (newZoneDigest != oldZoneDigest)
                    {
                        int zoneVer = ZoneVersionsDict._GetOrDefault(zone.DomainFqdn, -1);
                        zoneVer += 1;
                        ZoneVersionsDict[zone.DomainFqdn] = zoneVer;
                    }

                    zone.Version = ZoneVersionsDict._GetOrDefault(zone.DomainFqdn, 0);
                }

                this.CurrentDataSet = dataSet;

                ZoneVersionsDict._PrintAsJson();
            }
        }
    }

    public List<Zone> GetZonesList()
    {
        List<Zone> ret = new List<Zone>();
        var dataSet = this.CurrentDataSet;
        if (dataSet != null)
        {
            dataSet.ZoneDict.Values.OrderBy(x => x.DomainFqdn, FqdnReverseStrComparer.ComparerConsiderDepth)._DoForEach(x => ret.Add(x));
        }
        return ret;
    }

    public DnsUdpPacket? TryProcessForwarderResponse(DnsUdpPacket recvPacket)
    {
        var dataSet = this.CurrentDataSet;
        if (dataSet == null)
        {
            return null;
        }

        if (recvPacket.Message.IsQuery)
        {
            return null;
        }

        if (dataSet.HasForwarder_DomainBased || dataSet.HasForwarder_SubnetBased || dataSet.HasForwarder_WildcardBased)
        {
            // フォワーダセッションテーブルの検索
            var e = this.FwdTranslateTable.TryLookup(recvPacket.RemoteEndPoint, recvPacket.Message.TransactionID);
            if (e != null)
            {
                // 戻りパケットの生成
                var responsePacket = e.GenerateResponsePacketCallback(e, recvPacket._CloneDeep());

                if (responsePacket != null)
                {
                    // トランザクション ID の書き戻し
                    responsePacket.Message.TransactionID = e.OriginalRequestPacket.Message.TransactionID;

                    return responsePacket;
                }
                else
                {
                    // 無視
                    return null;
                }
            }
        }

        return null;
    }

    public Zone? GetExactlyMatchZone(string zoneFqdn)
    {
        var dataSet = this.CurrentDataSet;
        if (dataSet == null)
        {
            throw new CoresException("Current DNS Server Data Set is not loaded");
        }

        zoneFqdn = zoneFqdn._NormalizeFqdn();

        return dataSet.SearchExactMatchDnsZone(zoneFqdn);
    }

    public SearchResult? Search(string fqdnNormalized)
    {
        var dataSet = this.CurrentDataSet;
        if (dataSet == null)
        {
            throw new CoresException("Current DNS Server Data Set is not loaded");
        }

        return dataSet.Search(fqdnNormalized);
    }

    public SearchResult? Query(SearchRequest request, EasyDnsResponderRecordType type)
    {
        var dataSet = this.CurrentDataSet;
        if (dataSet == null)
        {
            throw new CoresException("Current DNS Server Data Set is not loaded");
        }

        // ユーザーからのクエリがフォワード対象の場合は、フォワーダに飛ばす。
        // 最初に一致するフォワーダがあるかどうか調べる。
        // フォワーダは最優先で使用される。
        if (dataSet.HasForwarder_DomainBased || dataSet.HasForwarder_SubnetBased || dataSet.HasForwarder_WildcardBased)
        {
            Forwarder? forwarder = null;

            // ワイルドカードベース -> サブネットベース -> ドメイン名ベースの順に走査する。

            // ワイルドカードベース
            if (forwarder == null)
            {
                if (dataSet.HasForwarder_WildcardBased)
                {
                    foreach (var w in dataSet.Forwarder_WildcardBasedList)
                    {
                        if (Str.WildcardMatch(request.FqdnNormalized, w.WildcardFqdn))
                        {
                            forwarder = w;
                            break;
                        }
                    }
                }
            }

            // サブネットベース
            if (forwarder == null)
            {
                if (dataSet.HasForwarder_SubnetBased)
                {
                    if (Str.IsEqualToOrSubdomainOf(EnsureSpecial.Yes, request.FqdnNormalized, "in-addr.arpa", out _) ||
                        Str.IsEqualToOrSubdomainOf(EnsureSpecial.Yes, request.FqdnNormalized, "ip6.arpa", out _))
                    {
                        var tmp = IPUtil.PtrZoneOrFqdnToIpAddressAndSubnet(request.FqdnNormalized, true);
                        if (IPUtil.IsSubnetLenHostAddress(tmp.Item1.AddressFamily, tmp.Item2))
                        {
                            forwarder = dataSet.Forwarder_SubnetBasedRadixTrie.Lookup(tmp.Item1, out _, out _);
                        }
                    }
                }
            }

            // ドメイン名ベース
            if (forwarder == null)
            {
                if (dataSet.HasForwarder_DomainBased)
                {
                    forwarder = DnsUtil.SearchLongestMatchDnsZone<Forwarder>(dataSet.Forwarder_DomainBasedDict, request.FqdnNormalized, out _, out _);
                }
            }

            if (forwarder != null)
            {
                // 使用すべきフォワーダが見つかった。コールバックを呼ぶ。
                var callback = this.ForwarderRequestTransformerCallback;

                EasyDnsResponderForwarderRequestTransformerCallbackRequest fwdReq = new EasyDnsResponderForwarderRequestTransformerCallbackRequest
                {
                    CallbackId = forwarder.CallbackId,
                    ForwarderInternal = forwarder,
                    ForwarderDef = forwarder.Src,
                    OriginalRequestPacket = request.RequestPacket._CloneDeep(),
                    RequestFqdn = request.FqdnNormalized,
                };

                EasyDnsResponderForwarderRequestTransformerCallbackResult fwdResult;

                Func<EasyDnsResponderForwarderResponseTransformerCallbackRequest, EasyDnsResponderForwarderResponseTransformerCallbackResult>? callback2 = null;

                if (callback != null)
                {
                    fwdResult = callback(fwdReq);
                    callback2 = fwdResult.ForwarderResponseTransformerCallback;
                }
                else
                {
                    fwdResult = new EasyDnsResponderForwarderRequestTransformerCallbackResult { };
                }

                if (fwdResult.SkipForwarder)
                {
                    // Skip
                }
                else if (fwdResult.ErrorCode != ReturnCode.NoError)
                {
                    // エラー
                    SearchResult ret2 = new SearchResult
                    {
                        RaiseCustomError = fwdResult.ErrorCode,
                    };
                    return ret2;
                }
                else
                {
                    DnsMessageBase modifiedRequestMessage;

                    if (fwdResult.ModifiedDnsRequestMessage != null)
                    {
                        modifiedRequestMessage = fwdResult.ModifiedDnsRequestMessage._CloneDeep();
                    }
                    else
                    {
                        modifiedRequestMessage = request.RequestPacket.Message._CloneDeep();
                    }

                    // フォワーダ処理の実施
                    long expires = Time.Tick64 + forwarder.TimeoutMsecs;
                    bool ok = false;

                    SearchResult ret3 = new SearchResult { };
                    ret3.AlternativeSendPackets = new List<DnsUdpPacket>();

                    HashSet<IPEndPoint> targetEndPointHashSet = new HashSet<IPEndPoint>(IpEndPointComparer.ComparerIgnoreScopeId);

                    foreach (var targetEndPoint in forwarder.TargetServersList)
                    {
                        targetEndPointHashSet.Add(targetEndPoint);
                    }

                    var e = this.FwdTranslateTable.TryCreateNew(targetEndPointHashSet, expires, request.RequestPacket, modifiedRequestMessage, (e, src) =>
                    {
                        DnsMessageBase? newDnsMessage = null;

                        if (callback2 != null)
                        {
                            EasyDnsResponderForwarderResponseTransformerCallbackRequest callbackIn = new EasyDnsResponderForwarderResponseTransformerCallbackRequest
                            {
                                CallbackId = forwarder.CallbackId,
                                ForwarderInternal = forwarder,
                                ForwarderDef = forwarder.Src,
                                OriginalResponsePacket = src,
                            };

                            var callbackOut = callback2(callbackIn);

                            if (callbackOut.Ignore)
                            {
                                return null;
                            }

                            if (callbackOut.ModifiedDnsResponseMessage != null)
                            {
                                newDnsMessage = callbackOut.ModifiedDnsResponseMessage;
                            }
                        }

                        if (newDnsMessage == null)
                        {
                            newDnsMessage = src.Message;
                        }

                        var reqPacket = request.RequestPacket;

                        return new DnsUdpPacket(reqPacket.RemoteEndPoint, reqPacket.LocalEndPoint, newDnsMessage);
                    });

                    if (e != null)
                    {
                        modifiedRequestMessage.TransactionID = e.NewTransactionId;

                        foreach (var targetEndPoint in targetEndPointHashSet)
                        {
                            ok = true;

                            var local = new IPEndPoint(targetEndPoint.AddressFamily == AddressFamily.InterNetworkV6 ? IPAddress.IPv6Any : IPAddress.Any, 0);
                            DnsUdpPacket newPacket = new DnsUdpPacket(targetEndPoint, local, modifiedRequestMessage);

                            ret3.AlternativeSendPackets.Add(newPacket);
                        }
                    }

                    if (ok == false)
                    {
                        SearchResult ret2 = new SearchResult
                        {
                            // Transcation ID 空き無し
                            RaiseCustomError = ReturnCode.ServerFailure,
                        };
                        return ret2;
                    }

                    // フォワーダ宛パケットの送信
                    return ret3;
                }
            }
        }

        // -- 以下は、フォワーダで処理がなされなかった場合のメイン処理の継続 --

        // 純粋な Zone の検索処理を実施する。クエリにおける要求レコードタイプは見ない。
        SearchResult? ret = this.Search(request.FqdnNormalized);

        // 次にクエリにおける要求レコードタイプに従って特別処理を行なう。
        if (ret != null)
        {
            var zone = ret.Zone;

            if (ret.ResultFlags.Bit(SearchResultFlags.SubDomainIsDelegated))
            {
                // 特別処理: クエリ種類が NS の場合で、権限委譲されているドメインの場合、結果には権限委譲のための NS レコードを埋め込むのである。
                // (dataSet.Search() によって、すでに埋め込みされているはずである。したがって、ここでは何もしない。)
            }
            else
            {
                // 応答リストを指定されたクエリ種類によってフィルタする。
                if (ret.RecordList != null)
                {
                    if (type != EasyDnsResponderRecordType.Any)
                    {
                        List<Record> tmpList = new List<Record>(ret.RecordList.Count);

                        foreach (var r in ret.RecordList)
                        {
                            if (r.Type == type || (r.Type == EasyDnsResponderRecordType.Any && r is Record_Dynamic))
                            {
                                tmpList.Add(r);
                            }
                        }

                        if (tmpList.Count == 0)
                        {
                            // フィルタ結果が 0 件の場合で、一致する CNAME があれば、それを追加する
                            foreach (var r in ret.RecordList)
                            {
                                if (r.Type == EasyDnsResponderRecordType.CNAME)
                                {
                                    tmpList.Add(r);
                                }
                            }
                        }

                        ret.RecordList = tmpList;
                    }
                }
            }

            // ダイナミックレコードが含まれている場合はコールバックを呼んで解決をする
            if (ret.RecordList != null)
            {
                int count = ret.RecordList.Count;
                List<Record> solvedDynamicRecordResults = new List<Record>();
                List<Record_Dynamic> originalDynamicRecords = new List<Record_Dynamic>();

                bool anyDynamicRecordExists = false;

                bool allDynamicRecords = true;

                bool notFound = false;

                for (int i = 0; i < count; i++)
                {
                    if (ret.RecordList[i] is Record_Dynamic dynRecord)
                    {
                        if (ResolveDynamicRecord(solvedDynamicRecordResults, dynRecord, ret, request, type) == false)
                        {
                            notFound = true;
                        }

                        anyDynamicRecordExists = true;

                        originalDynamicRecords.Add(dynRecord);
                    }
                    else
                    {
                        allDynamicRecords = false;
                    }
                }

                if (anyDynamicRecordExists == false)
                {
                    allDynamicRecords = false;
                }

                if (anyDynamicRecordExists)
                {
                    // 結果リストから DynamicRecord をすべて除去し、Callback の結果得られたレコードを挿入
                    foreach (var dynRecord in originalDynamicRecords)
                    {
                        ret.RecordList.Remove(dynRecord);
                    }

                    foreach (var resultRecord in solvedDynamicRecordResults)
                    {
                        ret.RecordList.Add(resultRecord);
                    }
                }

                if (allDynamicRecords)
                {
                    if (notFound)
                    {
                        // ダイナミックレコードリストを走査したが、1 つも見つからない場合は、NotFound ビットを返す
                        ret.ResultFlags |= SearchResultFlags.NotFound;
                    }
                }
            }
        }

        return ret;
    }

    // ダイナミックレコードをコールバックを用いて実際に解決する
    bool ResolveDynamicRecord(List<Record> listToAdd, Record_Dynamic dynRecord, SearchResult result, SearchRequest request, EasyDnsResponderRecordType expectedRecordType)
    {
        EasyDnsResponderDynamicRecordCallbackRequest req = new EasyDnsResponderDynamicRecordCallbackRequest
        {
            Zone = result.Zone.SrcZone,
            Record = dynRecord.SrcRecord!,
            ExpectedRecordType = expectedRecordType,
            RequestFqdn = request.FqdnNormalized,
            RequestHostName = result.RequestHostName,
            CallbackId = dynRecord.CallbackId,
            RequestPacket = request.RequestPacket,
        };

        EasyDnsResponderDynamicRecordCallbackResult? callbackResult = null;

        if (this.DynamicRecordCallback == null) throw new CoresLibException("Callback delegate is not set.");

        callbackResult = this.DynamicRecordCallback(req);

        if (callbackResult == null)
        {
            throw new CoresLibException($"Callback delegate returns null for callback ID '{dynRecord.CallbackId}'.");
        }

        if (callbackResult.NotFound)
        {
            return false;
        }

        EasyDnsResponderRecordSettings? settings = callbackResult.Settings;
        if (settings == null)
        {
            settings = dynRecord.Settings;
        }

        if (expectedRecordType == EasyDnsResponderRecordType.A || expectedRecordType == EasyDnsResponderRecordType.Any)
        {
            if (callbackResult.IPAddressList != null)
            {
                foreach (var ip in callbackResult.IPAddressList)
                {
                    if (ip.AddressFamily == AddressFamily.InterNetwork)
                    {
                        listToAdd.Add(new Record_A(result.Zone, settings, result.RequestHostName, ip));
                    }
                }
            }
        }

        if (expectedRecordType == EasyDnsResponderRecordType.AAAA || expectedRecordType == EasyDnsResponderRecordType.Any)
        {
            if (callbackResult.IPAddressList != null)
            {
                foreach (var ip in callbackResult.IPAddressList)
                {
                    if (ip.AddressFamily == AddressFamily.InterNetworkV6)
                    {
                        listToAdd.Add(new Record_AAAA(result.Zone, settings, result.RequestHostName, ip));
                    }
                }
            }
        }

        if (expectedRecordType == EasyDnsResponderRecordType.MX || expectedRecordType == EasyDnsResponderRecordType.Any)
        {
            if (callbackResult.MxFqdnList != null)
            {
                if (callbackResult.MxPreferenceList != null)
                {
                    if (callbackResult.MxFqdnList.Count == callbackResult.MxPreferenceList.Count)
                    {
                        for (int i = 0; i < callbackResult.MxFqdnList.Count; i++)
                        {
                            listToAdd.Add(new Record_MX(result.Zone, settings, result.RequestHostName, callbackResult.MxFqdnList[i], callbackResult.MxPreferenceList[i]));
                        }
                    }
                }
            }
        }

        if (expectedRecordType == EasyDnsResponderRecordType.SRV || expectedRecordType == EasyDnsResponderRecordType.Any)
        {
            if (callbackResult.SrvPortList != null && callbackResult.SrvPriorityList != null && callbackResult.SrvWeightList != null && callbackResult.SrvTargetList != null)
            {
                int count = callbackResult.SrvPortList.Count;
                if (callbackResult.SrvPriorityList.Count == count && callbackResult.SrvWeightList.Count == count && callbackResult.SrvTargetList.Count == count)
                {
                    for (int i = 0; i < count; i++)
                    {
                        listToAdd.Add(new Record_SRV(result.Zone, settings, result.RequestHostName,
                            callbackResult.SrvPriorityList[i], callbackResult.SrvWeightList[i], callbackResult.SrvPortList[i], callbackResult.SrvTargetList[i]));
                    }
                }
            }
        }

        if (expectedRecordType == EasyDnsResponderRecordType.NS || expectedRecordType == EasyDnsResponderRecordType.Any)
        {
            if (callbackResult.NsFqdnList != null)
            {
                foreach (var domain in callbackResult.NsFqdnList)
                {
                    listToAdd.Add(new Record_NS(result.Zone, settings, result.RequestHostName, domain));
                }
            }
        }

        if (expectedRecordType == EasyDnsResponderRecordType.PTR || expectedRecordType == EasyDnsResponderRecordType.Any)
        {
            if (callbackResult.PtrFqdnList != null)
            {
                foreach (var domain in callbackResult.PtrFqdnList)
                {
                    listToAdd.Add(new Record_PTR(result.Zone, settings, result.RequestHostName, domain));
                }
            }
        }

        if (expectedRecordType == EasyDnsResponderRecordType.TXT || expectedRecordType == EasyDnsResponderRecordType.Any)
        {
            if (callbackResult.TextList != null)
            {
                foreach (var text in callbackResult.TextList)
                {
                    listToAdd.Add(new Record_TXT(result.Zone, settings, result.RequestHostName, text));
                }
            }
        }

        if (expectedRecordType == EasyDnsResponderRecordType.CAA || expectedRecordType == EasyDnsResponderRecordType.Any)
        {
            if (callbackResult.CaaList != null)
            {
                foreach (var caa in callbackResult.CaaList)
                {
                    listToAdd.Add(new Record_CAA(result.Zone, settings, result.RequestHostName, caa.Item1, caa.Item2, caa.Item3));
                }
            }
        }

        //if (expectedRecordType == EasyDnsResponderRecordType.CNAME || expectedRecordType == EasyDnsResponderRecordType.Any)
        // CNAME はすべての場合に応答する
        {
            if (callbackResult.CNameFqdnList != null)
            {
                foreach (var domain in callbackResult.CNameFqdnList)
                {
                    listToAdd.Add(new Record_CNAME(result.Zone, settings, result.RequestHostName, domain));
                }
            }
        }

        return true;
    }
}


public static class EasyDnsTest
{
    public static void Test1()
    {
        EasyDnsResponderSettings st = new EasyDnsResponderSettings();

        {
            var zone = new EasyDnsResponderZone { DomainName = "test1.com" };

            zone.RecordList.Add(new EasyDnsResponderRecord { Type = EasyDnsResponderRecordType.A, Name = "", Contents = "9.3.1.7" });
            zone.RecordList.Add(new EasyDnsResponderRecord { Type = EasyDnsResponderRecordType.AAAA, Name = "", Contents = "2001::8181" });

            zone.RecordList.Add(new EasyDnsResponderRecord { Type = EasyDnsResponderRecordType.A, Name = "www", Contents = "1.2.3.4" });
            zone.RecordList.Add(new EasyDnsResponderRecord { Type = EasyDnsResponderRecordType.A, Name = "www", Contents = "1.9.8.4" });
            zone.RecordList.Add(new EasyDnsResponderRecord { Type = EasyDnsResponderRecordType.AAAA, Name = "www", Contents = "2001::1234" });
            zone.RecordList.Add(new EasyDnsResponderRecord { Type = EasyDnsResponderRecordType.A, Name = "ftp", Contents = "5.6.7.8" });
            zone.RecordList.Add(new EasyDnsResponderRecord { Type = EasyDnsResponderRecordType.SOA, Contents = "ns1.test1.com nobori.softether.com 123 50 100 200 25" });

            zone.RecordList.Add(new EasyDnsResponderRecord { Type = EasyDnsResponderRecordType.NS, Name = "", Contents = "ns1.ipa.go.jp" });
            zone.RecordList.Add(new EasyDnsResponderRecord { Type = EasyDnsResponderRecordType.NS, Name = "", Contents = "ns2.ipa.go.jp" });

            zone.RecordList.Add(new EasyDnsResponderRecord { Type = EasyDnsResponderRecordType.NS, Name = "subdomain1", Contents = "ns3.ipa.go.jp" });
            zone.RecordList.Add(new EasyDnsResponderRecord { Type = EasyDnsResponderRecordType.NS, Name = "subdomain1", Contents = "ns4.ipa.go.jp" });

            zone.RecordList.Add(new EasyDnsResponderRecord { Type = EasyDnsResponderRecordType.CNAME, Name = "cnametest1", Contents = "www.google.com" });

            zone.RecordList.Add(new EasyDnsResponderRecord { Type = EasyDnsResponderRecordType.NS, Name = "subdomain2.subdomain1", Contents = "ns5.ipa.go.jp" });
            zone.RecordList.Add(new EasyDnsResponderRecord { Type = EasyDnsResponderRecordType.NS, Name = "subdomain2.subdomain1", Contents = "ns6.ipa.go.jp" });

            zone.RecordList.Add(new EasyDnsResponderRecord { Type = EasyDnsResponderRecordType.A, Name = "www.subdomain3", Contents = "8.9.4.5" });

            zone.RecordList.Add(new EasyDnsResponderRecord { Type = EasyDnsResponderRecordType.A, Name = "*.kgb.abc123.subdomain5", Contents = "4.9.8.9" });
            zone.RecordList.Add(new EasyDnsResponderRecord { Type = EasyDnsResponderRecordType.A, Name = "*.subdomain5", Contents = "5.9.6.3" });

            zone.RecordList.Add(new EasyDnsResponderRecord { Type = EasyDnsResponderRecordType.A, Name = "*subdomain6*", Contents = "6.7.8.9" });

            zone.RecordList.Add(new EasyDnsResponderRecord { Type = EasyDnsResponderRecordType.A, Name = "*.subdomain7", Contents = "proc0001", Attribute = EasyDnsResponderRecordAttribute.DynamicRecord });
            zone.RecordList.Add(new EasyDnsResponderRecord { Type = EasyDnsResponderRecordType.AAAA, Name = "*.subdomain7", Contents = "proc0002", Attribute = EasyDnsResponderRecordAttribute.DynamicRecord });

            st.ZoneList.Add(zone);
        }

        {
            var zone = new EasyDnsResponderZone { DomainName = "abc.test1.com" };

            zone.RecordList.Add(new EasyDnsResponderRecord { Type = EasyDnsResponderRecordType.A, Name = "", Contents = "1.8.1.8" });
            zone.RecordList.Add(new EasyDnsResponderRecord { Type = EasyDnsResponderRecordType.A, Name = "www", Contents = "8.9.3.1" });
            zone.RecordList.Add(new EasyDnsResponderRecord { Type = EasyDnsResponderRecordType.AAAA, Name = "ftp", Contents = "2001::abcd" });
            zone.RecordList.Add(new EasyDnsResponderRecord { Type = EasyDnsResponderRecordType.AAAA, Name = "aho.baka.manuke", Contents = "2001::abcd" });
            zone.RecordList.Add(new EasyDnsResponderRecord { Type = EasyDnsResponderRecordType.SOA, Contents = "ns2.test1.com daiyuu.softether.com 894 895 896 897 898" });

            st.ZoneList.Add(zone);
        }

        EasyDnsResponder r = new EasyDnsResponder();

        r.ApplySetting(st);

        r.DynamicRecordCallback = (req) =>
        {
            switch (req.CallbackId)
            {
                case "proc0001":
                    var res1 = new EasyDnsResponderDynamicRecordCallbackResult();
                    res1.IPAddressList = new List<IPAddress>();
                    res1.IPAddressList.Add(IPAddress.Parse("1.0.0.1"));
                    res1.IPAddressList.Add(IPAddress.Parse("2.0.0.2"));
                    return res1;

                case "proc0002":
                    var res2 = new EasyDnsResponderDynamicRecordCallbackResult();
                    res2.IPAddressList = new List<IPAddress>();
                    res2.IPAddressList.Add(IPAddress.Parse("2001::1"));
                    res2.IPAddressList.Add(IPAddress.Parse("2001::2"));
                    return res2;
            }

            return null;
        };

        {
            var res = r.Query(new EasyDnsResponder.SearchRequest { FqdnNormalized = "test1.com" }, EasyDnsResponderRecordType.Any);

            Dbg.TestTrue(res!.RecordList!.Where(x => x.Name != "").Count() == 0);

            Dbg.TestTrue(res!.RecordList!.Where(x => x.Name == "").Where(x => x.Type == EasyDnsResponderRecordType.A).Count() == 1);
            Dbg.TestTrue(res.RecordList!.Where(x => x.Name == "").Where(x => x.Type == EasyDnsResponderRecordType.A)
                .Cast<EasyDnsResponder.Record_A>().OrderBy(x => x.IPv4Address, IpComparer.Comparer).ElementAt(0).IPv4Address.ToString() == "9.3.1.7");

            Dbg.TestTrue(res.RecordList!.Where(x => x.Name == "").Where(x => x.Type == EasyDnsResponderRecordType.AAAA).Count() == 1);
            Dbg.TestTrue(res.RecordList!.Where(x => x.Name == "").Where(x => x.Type == EasyDnsResponderRecordType.AAAA)
                .Cast<EasyDnsResponder.Record_AAAA>().OrderBy(x => x.IPv6Address, IpComparer.Comparer).ElementAt(0).IPv6Address.ToString() == "2001::8181");

            Dbg.TestTrue(res.RecordList!.Where(x => x.Name == "").Where(x => x.Type == EasyDnsResponderRecordType.NS).Count() == 2);
            Dbg.TestTrue(res.RecordList!.Where(x => x.Name == "").Where(x => x.Type == EasyDnsResponderRecordType.NS)
                .Cast<EasyDnsResponder.Record_NS>().OrderBy(x => x.ServerName.ToString(), StrCmpi).ElementAt(0).ServerName.ToString() == "ns1.ipa.go.jp.");
            Dbg.TestTrue(res.RecordList!.Where(x => x.Name == "").Where(x => x.Type == EasyDnsResponderRecordType.NS)
                .Cast<EasyDnsResponder.Record_NS>().OrderBy(x => x.ServerName.ToString(), StrCmpi).ElementAt(1).ServerName.ToString() == "ns2.ipa.go.jp.");
        }

        {
            var res = r.Query(new EasyDnsResponder.SearchRequest { FqdnNormalized = "cnametest1.test1.com" }, EasyDnsResponderRecordType.A);
            var record = res!.RecordList!.Single();
            Dbg.TestTrue(record.Type == EasyDnsResponderRecordType.CNAME);
        }

        {
            var res = r.Query(new EasyDnsResponder.SearchRequest { FqdnNormalized = "subdomain3.test1.com" }, EasyDnsResponderRecordType.Any);
            Dbg.TestTrue(res!.RecordList != null);
            Dbg.TestTrue(res.RecordList!.Count == 0);
        }

        {
            var res = r.Query(new EasyDnsResponder.SearchRequest { FqdnNormalized = "subdomain4.test1.com" }, EasyDnsResponderRecordType.Any);
            Dbg.TestTrue(res!.RecordList == null);
        }

        {
            var res = r.Query(new EasyDnsResponder.SearchRequest { FqdnNormalized = "abc123.subdomain5.test1.com" }, EasyDnsResponderRecordType.Any);
            Dbg.TestTrue(res!.RecordList!.Where(x => x.Type == EasyDnsResponderRecordType.A)
                .Cast<EasyDnsResponder.Record_A>().Single().IPv4Address.ToString() == "5.9.6.3");
        }

        {
            var res = r.Query(new EasyDnsResponder.SearchRequest { FqdnNormalized = "def456.abc123.subdomain5.test1.com" }, EasyDnsResponderRecordType.Any);
            Dbg.TestTrue(res!.RecordList!.Where(x => x.Type == EasyDnsResponderRecordType.A)
                .Cast<EasyDnsResponder.Record_A>().Single().IPv4Address.ToString() == "5.9.6.3");
        }

        {
            var res = r.Query(new EasyDnsResponder.SearchRequest { FqdnNormalized = "def456.kgb.abc123.subdomain5.test1.com" }, EasyDnsResponderRecordType.Any);
            Dbg.TestTrue(res!.RecordList!.Where(x => x.Type == EasyDnsResponderRecordType.A)
                .Cast<EasyDnsResponder.Record_A>().Single().IPv4Address.ToString() == "4.9.8.9");
        }

        {
            var res = r.Query(new EasyDnsResponder.SearchRequest { FqdnNormalized = "subdomain6.test1.com" }, EasyDnsResponderRecordType.Any);
            Dbg.TestTrue(res!.RecordList!.Where(x => x.Type == EasyDnsResponderRecordType.A)
                .Cast<EasyDnsResponder.Record_A>().Single().IPv4Address.ToString() == "6.7.8.9");
        }

        {
            var res = r.Query(new EasyDnsResponder.SearchRequest { FqdnNormalized = "1.2.3.4.subdomain7.test1.com" }, EasyDnsResponderRecordType.Any);
            res._PrintAsJson();

            Dbg.TestTrue(res!.RecordList!.Where(x => x.Type == EasyDnsResponderRecordType.A).Count() == 2);
            Dbg.TestTrue(res.RecordList!.Where(x => x.Type == EasyDnsResponderRecordType.A)
                .Cast<EasyDnsResponder.Record_A>().OrderBy(x => x.IPv4Address, IpComparer.Comparer).ElementAt(0).IPv4Address.ToString() == "1.0.0.1");
            Dbg.TestTrue(res.RecordList!.Where(x => x.Type == EasyDnsResponderRecordType.A)
                .Cast<EasyDnsResponder.Record_A>().OrderBy(x => x.IPv4Address, IpComparer.Comparer).ElementAt(1).IPv4Address.ToString() == "2.0.0.2");

            Dbg.TestTrue(res!.RecordList!.Where(x => x.Type == EasyDnsResponderRecordType.AAAA).Count() == 2);
            Dbg.TestTrue(res.RecordList!.Where(x => x.Type == EasyDnsResponderRecordType.AAAA)
                .Cast<EasyDnsResponder.Record_AAAA>().OrderBy(x => x.IPv6Address, IpComparer.Comparer).ElementAt(0).IPv6Address.ToString() == "2001::1");
            Dbg.TestTrue(res.RecordList!.Where(x => x.Type == EasyDnsResponderRecordType.AAAA)
                .Cast<EasyDnsResponder.Record_AAAA>().OrderBy(x => x.IPv6Address, IpComparer.Comparer).ElementAt(1).IPv6Address.ToString() == "2001::2");
        }

        {
            var res = r.Query(new EasyDnsResponder.SearchRequest { FqdnNormalized = "subdomain1.test1.com" }, EasyDnsResponderRecordType.Any);

            Dbg.TestTrue(res!.RecordList!.Where(x => x.Name != "subdomain1").Count() == 0);

            Dbg.TestTrue(res!.RecordList!.Where(x => x.Name == "subdomain1").Where(x => x.Type == EasyDnsResponderRecordType.A).Count() == 0);
            Dbg.TestTrue(res.RecordList!.Where(x => x.Name == "subdomain1").Where(x => x.Type == EasyDnsResponderRecordType.AAAA).Count() == 0);

            Dbg.TestTrue(res.RecordList!.Where(x => x.Name == "subdomain1").Where(x => x.Type == EasyDnsResponderRecordType.NS).Count() == 2);
            Dbg.TestTrue(res.RecordList!.Where(x => x.Name == "subdomain1").Where(x => x.Type == EasyDnsResponderRecordType.NS)
                .Cast<EasyDnsResponder.Record_NS>().OrderBy(x => x.ServerName.ToString(), StrCmpi).ElementAt(0).ServerName.ToString() == "ns3.ipa.go.jp.");
            Dbg.TestTrue(res.RecordList!.Where(x => x.Name == "subdomain1").Where(x => x.Type == EasyDnsResponderRecordType.NS)
                .Cast<EasyDnsResponder.Record_NS>().OrderBy(x => x.ServerName.ToString(), StrCmpi).ElementAt(1).ServerName.ToString() == "ns4.ipa.go.jp.");
        }

        {
            var res = r.Query(new EasyDnsResponder.SearchRequest { FqdnNormalized = "subdomain2.subdomain1.test1.com" }, EasyDnsResponderRecordType.Any);

            Dbg.TestTrue(res!.RecordList!.Where(x => x.Name != "subdomain2.subdomain1").Count() == 0);

            Dbg.TestTrue(res!.RecordList!.Where(x => x.Name == "subdomain2.subdomain1").Where(x => x.Type != EasyDnsResponderRecordType.NS).Count() == 0);

            Dbg.TestTrue(res.RecordList!.Where(x => x.Name == "subdomain2.subdomain1").Where(x => x.Type == EasyDnsResponderRecordType.NS).Count() == 2);
            Dbg.TestTrue(res.RecordList!.Where(x => x.Name == "subdomain2.subdomain1").Where(x => x.Type == EasyDnsResponderRecordType.NS)
                .Cast<EasyDnsResponder.Record_NS>().OrderBy(x => x.ServerName.ToString(), StrCmpi).ElementAt(0).ServerName.ToString() == "ns5.ipa.go.jp.");
            Dbg.TestTrue(res.RecordList!.Where(x => x.Name == "subdomain2.subdomain1").Where(x => x.Type == EasyDnsResponderRecordType.NS)
                .Cast<EasyDnsResponder.Record_NS>().OrderBy(x => x.ServerName.ToString(), StrCmpi).ElementAt(1).ServerName.ToString() == "ns6.ipa.go.jp.");
        }

        {
            var res = r.Query(new EasyDnsResponder.SearchRequest { FqdnNormalized = "xyz.abc.subdomain2.subdomain1.test1.com" }, EasyDnsResponderRecordType.Any);

            Dbg.TestTrue(res!.RecordList!.Where(x => x.Name != "subdomain2.subdomain1").Count() == 0);

            Dbg.TestTrue(res!.RecordList!.Where(x => x.Name == "subdomain2.subdomain1").Where(x => x.Type != EasyDnsResponderRecordType.NS).Count() == 0);

            Dbg.TestTrue(res.RecordList!.Where(x => x.Name == "subdomain2.subdomain1").Where(x => x.Type == EasyDnsResponderRecordType.NS).Count() == 2);
            Dbg.TestTrue(res.RecordList!.Where(x => x.Name == "subdomain2.subdomain1").Where(x => x.Type == EasyDnsResponderRecordType.NS)
                .Cast<EasyDnsResponder.Record_NS>().OrderBy(x => x.ServerName.ToString(), StrCmpi).ElementAt(0).ServerName.ToString() == "ns5.ipa.go.jp.");
            Dbg.TestTrue(res.RecordList!.Where(x => x.Name == "subdomain2.subdomain1").Where(x => x.Type == EasyDnsResponderRecordType.NS)
                .Cast<EasyDnsResponder.Record_NS>().OrderBy(x => x.ServerName.ToString(), StrCmpi).ElementAt(1).ServerName.ToString() == "ns6.ipa.go.jp.");
        }

        {
            var res = r.Query(new EasyDnsResponder.SearchRequest { FqdnNormalized = "test123.subdomain1.test1.com" }, EasyDnsResponderRecordType.Any);

            Dbg.TestTrue(res!.RecordList!.Where(x => x.Name != "subdomain1").Count() == 0);

            Dbg.TestTrue(res!.RecordList!.Where(x => x.Name == "subdomain1").Where(x => x.Type != EasyDnsResponderRecordType.NS).Count() == 0);

            Dbg.TestTrue(res.RecordList!.Where(x => x.Name == "subdomain1").Where(x => x.Type == EasyDnsResponderRecordType.NS).Count() == 2);
            Dbg.TestTrue(res.RecordList!.Where(x => x.Name == "subdomain1").Where(x => x.Type == EasyDnsResponderRecordType.NS)
                .Cast<EasyDnsResponder.Record_NS>().OrderBy(x => x.ServerName.ToString(), StrCmpi).ElementAt(0).ServerName.ToString() == "ns3.ipa.go.jp.");
            Dbg.TestTrue(res.RecordList!.Where(x => x.Name == "subdomain1").Where(x => x.Type == EasyDnsResponderRecordType.NS)
                .Cast<EasyDnsResponder.Record_NS>().OrderBy(x => x.ServerName.ToString(), StrCmpi).ElementAt(1).ServerName.ToString() == "ns4.ipa.go.jp.");
        }

        {
            var res = r.Query(new EasyDnsResponder.SearchRequest { FqdnNormalized = "www.subdomain3.test1.com" }, EasyDnsResponderRecordType.Any);

            Dbg.TestTrue(res!.RecordList!.Where(x => x.Name != "www.subdomain3").Count() == 0);

            Dbg.TestTrue(res!.RecordList!.Where(x => x.Name == "www.subdomain3").Where(x => x.Type == EasyDnsResponderRecordType.A).Count() == 1);
            Dbg.TestTrue(res.RecordList!.Where(x => x.Name == "www.subdomain3").Where(x => x.Type == EasyDnsResponderRecordType.A)
                .Cast<EasyDnsResponder.Record_A>().OrderBy(x => x.IPv4Address, IpComparer.Comparer).ElementAt(0).IPv4Address.ToString() == "8.9.4.5");
        }

        {
            var res = r.Query(new EasyDnsResponder.SearchRequest { FqdnNormalized = "www.test1.com" }, EasyDnsResponderRecordType.Any);

            Dbg.TestTrue(res!.SOARecord.MasterName.ToString() == "ns1.test1.com.");
            Dbg.TestTrue(res.SOARecord.ResponsibleName.ToString() == "nobori.softether.com.");
            Dbg.TestTrue(res.SOARecord.SerialNumber == 123);
            Dbg.TestTrue(res.SOARecord.RefreshIntervalSecs == 50);
            Dbg.TestTrue(res.SOARecord.RetryIntervalSecs == 100);
            Dbg.TestTrue(res.SOARecord.ExpireIntervalSecs == 200);
            Dbg.TestTrue(res.SOARecord.NegativeCacheTtlSecs == 25);

            Dbg.TestTrue(res.RecordList!.Where(x => x.Name != "www").Count() == 0);

            Dbg.TestTrue(res.RecordList!.Where(x => x.Name == "www").Where(x => x.Type == EasyDnsResponderRecordType.A).Count() == 2);

            Dbg.TestTrue(res.RecordList!.Where(x => x.Name == "www").Where(x => x.Type == EasyDnsResponderRecordType.A)
                .Cast<EasyDnsResponder.Record_A>().OrderBy(x => x.IPv4Address, IpComparer.Comparer).ElementAt(0).IPv4Address.ToString() == "1.2.3.4");

            Dbg.TestTrue(res.RecordList!.Where(x => x.Name == "www").Where(x => x.Type == EasyDnsResponderRecordType.A)
                .Cast<EasyDnsResponder.Record_A>().OrderBy(x => x.IPv4Address, IpComparer.Comparer).ElementAt(1).IPv4Address.ToString() == "1.9.8.4");

            Dbg.TestTrue(res.RecordList!.Where(x => x.Name == "www").Where(x => x.Type == EasyDnsResponderRecordType.AAAA).Count() == 1);

            Dbg.TestTrue(res.RecordList!.Where(x => x.Name == "www").Where(x => x.Type == EasyDnsResponderRecordType.AAAA)
                .Cast<EasyDnsResponder.Record_AAAA>().OrderBy(x => x.IPv6Address, IpComparer.Comparer).ElementAt(0).IPv6Address.ToString() == "2001::1234");
        }

        {
            var res = r.Query(new EasyDnsResponder.SearchRequest { FqdnNormalized = "abc.test1.com" }, EasyDnsResponderRecordType.Any);

            Dbg.TestTrue(res!.SOARecord.MasterName.ToString() == "ns2.test1.com.");
            Dbg.TestTrue(res.SOARecord.ResponsibleName.ToString() == "daiyuu.softether.com.");
            Dbg.TestTrue(res.SOARecord.SerialNumber == 894);
            Dbg.TestTrue(res.SOARecord.RefreshIntervalSecs == 895);
            Dbg.TestTrue(res.SOARecord.RetryIntervalSecs == 896);
            Dbg.TestTrue(res.SOARecord.ExpireIntervalSecs == 897);
            Dbg.TestTrue(res.SOARecord.NegativeCacheTtlSecs == 898);

            Dbg.TestTrue(((EasyDnsResponder.Record_A)res.RecordList!.Single(x => x.Type == EasyDnsResponderRecordType.A)).IPv4Address.ToString() == "1.8.1.8");
        }

    }
}


#endif
