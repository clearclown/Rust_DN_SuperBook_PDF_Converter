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
// 
// Original Source Code:
// ARSoft.Tools.Net by Alexander Reinert
// 
// From: https://github.com/alexreinert/ARSoft.Tools.Net/tree/18b427f9f3cfacd4464152090db0515d2675d899
//
// ARSoft.Tools.Net - C# DNS client/server and SPF Library, Copyright (c) 2010-2017 Alexander Reinert (https://github.com/alexreinert/ARSoft.Tools.Net)
// 
// Copyright 2010..2017 Alexander Reinert
// 
// This file is part of the ARSoft.Tools.Net - C# DNS client/server and SPF Library (https://github.com/alexreinert/ARSoft.Tools.Net)
// 
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// 
//   http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

#if CORES_BASIC_SECURITY

#nullable disable

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
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

namespace IPA.Cores.Basic.DnsLib;

/// <summary>
///   Event arguments of <see cref="DnsServer.ClientConnected" /> event.
/// </summary>
public class ClientConnectedEventArgs : EventArgs
{
    /// <summary>
    ///   Protocol used by the client
    /// </summary>
    public ProtocolType ProtocolType { get; private set; }

    /// <summary>
    ///   Remote endpoint of the client
    /// </summary>
    public IPEndPoint RemoteEndpoint { get; private set; }

    /// <summary>
    ///   If true, the client connection will be refused
    /// </summary>
    public bool RefuseConnect { get; set; }

    internal ClientConnectedEventArgs(ProtocolType protocolType, IPEndPoint remoteEndpoint)
    {
        ProtocolType = protocolType;
        RemoteEndpoint = remoteEndpoint;
    }
}

internal class DnsAsyncState : IAsyncResult
{
    internal List<IPAddress> Servers = null;
    internal int ServerIndex = 0;
    internal byte[] QueryData;
    internal int QueryLength = 0;

    internal DnsServer.SelectTsigKey TSigKeySelector = null;
    internal byte[] TSigOriginalMac = null;

    internal DnsMessage Response = null;

    internal Timer Timer;
    internal bool TimedOut = false;

    private long _timeOutUtcTicks;

    internal long TimeRemaining
    {
        get
        {
            long res = (_timeOutUtcTicks - DateTime.UtcNow.Ticks) / TimeSpan.TicksPerMillisecond;
            return res > 0 ? res : 0;
        }
        set { _timeOutUtcTicks = DateTime.UtcNow.Ticks + value * TimeSpan.TicksPerMillisecond; }
    }

    internal UdpClient UdpClient = null;
    internal IPEndPoint UdpEndpoint = null;

    internal TcpClient TcpClient = null;
    internal NetworkStream TcpStream = null;
    internal byte[] TcpBuffer = null;
    internal int TcpBytesToReceive = 0;

    internal AsyncCallback UserCallback = null;
    public object AsyncState { get; internal set; }
    public bool IsCompleted { get; private set; }

    public bool CompletedSynchronously
    {
        get { return false; }
    }

    private ManualResetEvent _waitHandle;

    public WaitHandle AsyncWaitHandle
    {
        get { return _waitHandle ?? (_waitHandle = new ManualResetEvent(IsCompleted)); }
    }

    internal void SetCompleted()
    {
        QueryData = null;

        if (Timer != null)
        {
            Timer.Dispose();
            Timer = null;
        }


        IsCompleted = true;
        if (_waitHandle != null)
            _waitHandle.Set();

        if (UserCallback != null)
            UserCallback(this);
    }
}

/// <summary>
///   Provides a client for querying dns records
/// </summary>
public class DnsClient : DnsClientBase
{
    /// <summary>
    ///   Returns a default instance of the DnsClient, which uses the configured dns servers of the executing computer and a
    ///   query timeout of 10 seconds.
    /// </summary>
    public static DnsClient Default { get; private set; }

    /// <summary>
    ///   Gets or sets a value indicationg whether queries can be sent using UDP.
    /// </summary>
    public new bool IsUdpEnabled
    {
        get { return base.IsUdpEnabled; }
        set { base.IsUdpEnabled = value; }
    }

    /// <summary>
    ///   Gets or sets a value indicationg whether queries can be sent using TCP.
    /// </summary>
    public new bool IsTcpEnabled
    {
        get { return base.IsTcpEnabled; }
        set { base.IsTcpEnabled = value; }
    }

    static DnsClient()
    {
        Default = new DnsClient(GetLocalConfiguredDnsServers(), 10000) { IsResponseValidationEnabled = true };
    }

    /// <summary>
    ///   Provides a new instance with custom dns server and query timeout
    /// </summary>
    /// <param name="dnsServer"> The IPAddress of the dns server to use </param>
    /// <param name="queryTimeout"> Query timeout in milliseconds </param>
    public DnsClient(IPAddress dnsServer, int queryTimeout)
        : this(new List<IPAddress> { dnsServer }, queryTimeout) { }

    /// <summary>
    ///   Provides a new instance with custom dns servers and query timeout
    /// </summary>
    /// <param name="dnsServers"> The IPAddresses of the dns servers to use </param>
    /// <param name="queryTimeout"> Query timeout in milliseconds </param>
    public DnsClient(IEnumerable<IPAddress> dnsServers, int queryTimeout)
        : base(dnsServers, queryTimeout, 53)
    {
        IsUdpEnabled = true;
        IsTcpEnabled = true;
    }

    protected override int MaximumQueryMessageSize => 512;

    /// <summary>
    ///   Queries a dns server for specified records.
    /// </summary>
    /// <param name="name"> Domain, that should be queried </param>
    /// <param name="recordType"> Type the should be queried </param>
    /// <param name="recordClass"> Class the should be queried </param>
    /// <param name="options"> Options for the query </param>
    /// <returns> The complete response of the dns server </returns>
    public DnsMessage Resolve(DomainName name, RecordType recordType = RecordType.A, RecordClass recordClass = RecordClass.INet, DnsQueryOptions options = null)
    {
        if (name == null)
            throw new ArgumentNullException(nameof(name), "Name must be provided");

        DnsMessage message = new DnsMessage() { IsQuery = true, OperationCode = OperationCode.Query, IsRecursionDesired = true, IsEDnsEnabled = true };

        if (options == null)
        {
            message.IsRecursionDesired = true;
            message.IsEDnsEnabled = true;
        }
        else
        {
            message.IsRecursionDesired = options.IsRecursionDesired;
            message.IsCheckingDisabled = options.IsCheckingDisabled;
            message.EDnsOptions = options.EDnsOptions;
        }

        message.Questions.Add(new DnsQuestion(name, recordType, recordClass));

        return SendMessage(message);
    }

    /// <summary>
    ///   Queries a dns server for specified records as an asynchronous operation.
    /// </summary>
    /// <param name="name"> Domain, that should be queried </param>
    /// <param name="recordType"> Type the should be queried </param>
    /// <param name="recordClass"> Class the should be queried </param>
    /// <param name="options"> Options for the query </param>
    /// <param name="token"> The token to monitor cancellation requests </param>
    /// <returns> The complete response of the dns server </returns>
    public Task<DnsMessage> ResolveAsync(DomainName name, RecordType recordType = RecordType.A, RecordClass recordClass = RecordClass.INet, DnsQueryOptions options = null, CancellationToken token = default(CancellationToken))
    {
        if (name == null)
            throw new ArgumentNullException(nameof(name), "Name must be provided");

        DnsMessage message = new DnsMessage() { IsQuery = true, OperationCode = OperationCode.Query, IsRecursionDesired = true, IsEDnsEnabled = true };

        if (options == null)
        {
            message.IsRecursionDesired = true;
            message.IsEDnsEnabled = true;
        }
        else
        {
            message.IsRecursionDesired = options.IsRecursionDesired;
            message.IsCheckingDisabled = options.IsCheckingDisabled;
            message.EDnsOptions = options.EDnsOptions;
        }

        message.Questions.Add(new DnsQuestion(name, recordType, recordClass));

        return SendMessageAsync(message, token);
    }

    /// <summary>
    ///   Send a custom message to the dns server and returns the answer.
    /// </summary>
    /// <param name="message"> Message, that should be send to the dns server </param>
    /// <returns> The complete response of the dns server </returns>
    public DnsMessage SendMessage(DnsMessage message)
    {
        if (message == null)
            throw new ArgumentNullException(nameof(message));

        if ((message.Questions == null) || (message.Questions.Count == 0))
            throw new ArgumentException("At least one question must be provided", nameof(message));

        return SendMessage<DnsMessage>(message);
    }

    /// <summary>
    ///   Send a custom message to the dns server and returns the answer as an asynchronous operation.
    /// </summary>
    /// <param name="message"> Message, that should be send to the dns server </param>
    /// <param name="token"> The token to monitor cancellation requests </param>
    /// <returns> The complete response of the dns server </returns>
    public Task<DnsMessage> SendMessageAsync(DnsMessage message, CancellationToken token = default(CancellationToken))
    {
        if (message == null)
            throw new ArgumentNullException(nameof(message));

        if ((message.Questions == null) || (message.Questions.Count == 0))
            throw new ArgumentException("At least one question must be provided", nameof(message));

        return SendMessageAsync<DnsMessage>(message, token);
    }

    /// <summary>
    ///   Send an dynamic update to the dns server and returns the answer.
    /// </summary>
    /// <param name="message"> Update, that should be send to the dns server </param>
    /// <returns> The complete response of the dns server </returns>
    public DnsUpdateMessage SendUpdate(DnsUpdateMessage message)
    {
        if (message == null)
            throw new ArgumentNullException(nameof(message));

        if (message.ZoneName == null)
            throw new ArgumentException("Zone name must be provided", nameof(message));

        return SendMessage(message);
    }

    /// <summary>
    ///   Send an dynamic update to the dns server and returns the answer as an asynchronous operation.
    /// </summary>
    /// <param name="message"> Update, that should be send to the dns server </param>
    /// <param name="token"> The token to monitor cancellation requests </param>
    /// <returns> The complete response of the dns server </returns>
    public Task<DnsUpdateMessage> SendUpdateAsync(DnsUpdateMessage message, CancellationToken token = default(CancellationToken))
    {
        if (message == null)
            throw new ArgumentNullException(nameof(message));

        if (message.ZoneName == null)
            throw new ArgumentException("Zone name must be provided", nameof(message));

        return SendMessageAsync(message, token);
    }

    /// <summary>
    ///   Returns a list of the local configured DNS servers.
    /// </summary>
    /// <returns></returns>
    public static List<IPAddress> GetLocalConfiguredDnsServers()
    {
        List<IPAddress> res = new List<IPAddress>();

        try
        {
            foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if ((nic.OperationalStatus == OperationalStatus.Up) && (nic.NetworkInterfaceType != NetworkInterfaceType.Loopback))
                {
                    foreach (IPAddress dns in nic.GetIPProperties().DnsAddresses)
                    {
                        // only use servers defined in draft-ietf-ipngwg-dns-discovery if they are in the same subnet
                        // fec0::/10 is marked deprecated in RFC 3879, so nobody should use these addresses
                        if (dns.AddressFamily == AddressFamily.InterNetworkV6)
                        {
                            IPAddress unscoped = new IPAddress(dns.GetAddressBytes());
                            if (unscoped.Equals(IPAddress.Parse("fec0:0:0:ffff::1"))
                                || unscoped.Equals(IPAddress.Parse("fec0:0:0:ffff::2"))
                                || unscoped.Equals(IPAddress.Parse("fec0:0:0:ffff::3")))
                            {
                                if (!nic.GetIPProperties().UnicastAddresses.Any(x => x.Address.GetNetworkAddress(10).Equals(IPAddress.Parse("fec0::"))))
                                    continue;
                            }
                        }

                        if (!res.Contains(dns))
                            res.Add(dns);
                    }
                }
            }
        }
        catch (Exception e)
        {
            Trace.TraceError("Configured nameserver couldn't be determined: " + e);
        }

        // try parsing resolv.conf since getting data by NetworkInterface is not supported on non-windows mono
        if ((res.Count == 0) && ((Environment.OSVersion.Platform == PlatformID.Unix) || (Environment.OSVersion.Platform == PlatformID.MacOSX)))
        {
            try
            {
                using (StreamReader reader = File.OpenText("/etc/resolv.conf"))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        int commentStart = line.IndexOf('#');
                        if (commentStart != -1)
                        {
                            line = line.Substring(0, commentStart);
                        }

                        string[] lineData = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                        IPAddress dns;
                        if ((lineData.Length == 2) && (lineData[0] == "nameserver") && (IPAddress.TryParse(lineData[1], out dns)))
                        {
                            res.Add(dns);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Trace.TraceError("/etc/resolv.conf could not be parsed: " + e);
            }
        }

        if (res.Count == 0)
        {
            // fallback: use the public dns-resolvers of google
            res.Add(IPAddress.Parse("2001:4860:4860::8844"));
            res.Add(IPAddress.Parse("2001:4860:4860::8888"));
            res.Add(IPAddress.Parse("8.8.4.4"));
            res.Add(IPAddress.Parse("8.8.8.8"));
        }

        return res.OrderBy(x => x.AddressFamily == AddressFamily.InterNetworkV6 ? 1 : 0).ToList();
    }
}

internal class DnsClientAsyncState<TMessage> : IAsyncResult
    where TMessage : DnsMessageBase
{
    internal List<DnsClientEndpointInfo> EndpointInfos;
    internal int EndpointInfoIndex;

    internal TMessage Query;
    internal byte[] QueryData;
    internal int QueryLength;

    internal DnsServer.SelectTsigKey TSigKeySelector;
    internal byte[] TSigOriginalMac;

    internal TMessage PartialMessage = null;
    internal List<TMessage> Responses;

    internal Timer Timer;
    internal bool TimedOut = false;

    private long _timeOutUtcTicks;

    internal long TimeRemaining
    {
        get
        {
            long res = (_timeOutUtcTicks - DateTime.UtcNow.Ticks) / TimeSpan.TicksPerMillisecond;
            return res > 0 ? res : 0;
        }
        set { _timeOutUtcTicks = DateTime.UtcNow.Ticks + value * TimeSpan.TicksPerMillisecond; }
    }

    internal System.Net.Sockets.Socket UdpClient = null;
    internal EndPoint UdpEndpoint = null;

    internal byte[] Buffer = null;

    internal TcpClient TcpClient = null;
    internal NetworkStream TcpStream = null;
    internal int TcpBytesToReceive = 0;

    internal AsyncCallback UserCallback = null;
    public object AsyncState { get; internal set; }
    public bool IsCompleted { get; private set; }

    public bool CompletedSynchronously
    {
        get { return false; }
    }

    private ManualResetEvent _waitHandle;

    public WaitHandle AsyncWaitHandle
    {
        get { return _waitHandle ?? (_waitHandle = new ManualResetEvent(IsCompleted)); }
    }

    internal void SetCompleted()
    {
        QueryData = null;

        if (Timer != null)
        {
            Timer.Dispose();
            Timer = null;
        }

        IsCompleted = true;
        if (_waitHandle != null)
            _waitHandle.Set();

        if (UserCallback != null)
            UserCallback(this);
    }

    public DnsClientAsyncState<TMessage> CreateTcpCloneWithoutCallback()
    {
        return
            new DnsClientAsyncState<TMessage>
            {
                EndpointInfos = EndpointInfos,
                EndpointInfoIndex = EndpointInfoIndex,
                Query = Query,
                QueryData = QueryData,
                QueryLength = QueryLength,
                TSigKeySelector = TSigKeySelector,
                TSigOriginalMac = TSigOriginalMac,
                Responses = Responses,
                _timeOutUtcTicks = _timeOutUtcTicks
            };
    }
}

public abstract class DnsClientBase
{
    private static readonly SecureRandom _secureRandom = new SecureRandom();

    private readonly List<IPAddress> _servers;
    private readonly bool _isAnyServerMulticast;
    private readonly int _port;

    internal DnsClientBase(IEnumerable<IPAddress> servers, int queryTimeout, int port)
    {
        _servers = servers.OrderBy(s => s.AddressFamily == AddressFamily.InterNetworkV6 ? 0 : 1).ToList();
        _isAnyServerMulticast = _servers.Any(s => s.IsMulticast());
        QueryTimeout = queryTimeout;
        _port = port;
    }

    /// <summary>
    ///   Milliseconds after which a query times out.
    /// </summary>
    public int QueryTimeout { get; }

    /// <summary>
    ///   Gets or set a value indicating whether the response is validated as described in
    ///   <see
    ///     cref="!:http://tools.ietf.org/id/draft-vixie-dnsext-dns0x20-00.txt">
    ///     draft-vixie-dnsext-dns0x20-00
    ///   </see>
    /// </summary>
    public bool IsResponseValidationEnabled { get; set; }

    /// <summary>
    ///   Gets or set a value indicating whether the query labels are used for additional validation as described in
    ///   <see
    ///     cref="!:http://tools.ietf.org/id/draft-vixie-dnsext-dns0x20-00.txt">
    ///     draft-vixie-dnsext-dns0x20-00
    ///   </see>
    /// </summary>
    // ReSharper disable once InconsistentNaming
    public bool Is0x20ValidationEnabled { get; set; }

    protected abstract int MaximumQueryMessageSize { get; }

    protected virtual bool IsUdpEnabled { get; set; }

    protected virtual bool IsTcpEnabled { get; set; }

    protected TMessage SendMessage<TMessage>(TMessage message)
        where TMessage : DnsMessageBase, new()
    {
        int messageLength;
        byte[] messageData;
        DnsServer.SelectTsigKey tsigKeySelector;
        byte[] tsigOriginalMac;

        PrepareMessage(message, out messageLength, out messageData, out tsigKeySelector, out tsigOriginalMac);

        bool sendByTcp = ((messageLength > MaximumQueryMessageSize) || message.IsTcpUsingRequested || !IsUdpEnabled);

        var endpointInfos = GetEndpointInfos();

        for (int i = 0; i < endpointInfos.Count; i++)
        {
            TcpClient tcpClient = null;
            NetworkStream tcpStream = null;

            try
            {
                var endpointInfo = endpointInfos[i];

                IPAddress responderAddress;
                byte[] resultData = sendByTcp ? QueryByTcp(endpointInfo.ServerAddress, messageData, messageLength, ref tcpClient, ref tcpStream, out responderAddress) : QueryByUdp(endpointInfo, messageData, messageLength, out responderAddress);

                if (resultData != null)
                {
                    TMessage result;

                    try
                    {
                        result = DnsMessageBase.Parse<TMessage>(resultData, tsigKeySelector, tsigOriginalMac);
                    }
                    catch (Exception e)
                    {
                        Trace.TraceError("Error on dns query: " + e);
                        continue;
                    }

                    if (!ValidateResponse(message, result))
                        continue;

                    if ((result.ReturnCode == ReturnCode.ServerFailure) && (i != endpointInfos.Count - 1))
                    {
                        continue;
                    }

                    if (result.IsTcpResendingRequested)
                    {
                        resultData = QueryByTcp(responderAddress, messageData, messageLength, ref tcpClient, ref tcpStream, out responderAddress);
                        if (resultData != null)
                        {
                            TMessage tcpResult;

                            try
                            {
                                tcpResult = DnsMessageBase.Parse<TMessage>(resultData, tsigKeySelector, tsigOriginalMac);
                            }
                            catch (Exception e)
                            {
                                Trace.TraceError("Error on dns query: " + e);
                                continue;
                            }

                            if (tcpResult.ReturnCode == ReturnCode.ServerFailure)
                            {
                                if (i != endpointInfos.Count - 1)
                                {
                                    continue;
                                }
                            }
                            else
                            {
                                result = tcpResult;
                            }
                        }
                    }

                    bool isTcpNextMessageWaiting = result.IsTcpNextMessageWaiting(false);
                    bool isSucessfullFinished = true;

                    while (isTcpNextMessageWaiting)
                    {
                        resultData = QueryByTcp(responderAddress, null, 0, ref tcpClient, ref tcpStream, out responderAddress);
                        if (resultData != null)
                        {
                            TMessage tcpResult;

                            try
                            {
                                tcpResult = DnsMessageBase.Parse<TMessage>(resultData, tsigKeySelector, tsigOriginalMac);
                            }
                            catch (Exception e)
                            {
                                Trace.TraceError("Error on dns query: " + e);
                                isSucessfullFinished = false;
                                break;
                            }

                            if (tcpResult.ReturnCode == ReturnCode.ServerFailure)
                            {
                                isSucessfullFinished = false;
                                break;
                            }
                            else
                            {
                                result.AnswerRecords.AddRange(tcpResult.AnswerRecords);
                                isTcpNextMessageWaiting = tcpResult.IsTcpNextMessageWaiting(true);
                            }
                        }
                        else
                        {
                            isSucessfullFinished = false;
                            break;
                        }
                    }

                    if (isSucessfullFinished)
                        return result;
                }
            }
            finally
            {
                try
                {
                    tcpStream?.Dispose();
                    tcpClient?.Close();
                }
                catch
                {
                    // ignored
                }
            }
        }

        return null;
    }

    protected List<TMessage> SendMessageParallel<TMessage>(TMessage message)
        where TMessage : DnsMessageBase, new()
    {
        Task<List<TMessage>> result = SendMessageParallelAsync(message, default(CancellationToken));

        result.Wait();

        return result.Result;
    }

    private bool ValidateResponse<TMessage>(TMessage message, TMessage result)
        where TMessage : DnsMessageBase
    {
        if (IsResponseValidationEnabled)
        {
            if ((result.ReturnCode == ReturnCode.NoError) || (result.ReturnCode == ReturnCode.NxDomain))
            {
                if (message.TransactionID != result.TransactionID)
                    return false;

                if ((message.Questions == null) || (result.Questions == null))
                    return false;

                if ((message.Questions.Count != result.Questions.Count))
                    return false;

                for (int j = 0; j < message.Questions.Count; j++)
                {
                    DnsQuestion queryQuestion = message.Questions[j];
                    DnsQuestion responseQuestion = result.Questions[j];

                    if ((queryQuestion.RecordClass != responseQuestion.RecordClass)
                        || (queryQuestion.RecordType != responseQuestion.RecordType)
                        || (!queryQuestion.Name.Equals(responseQuestion.Name, false)))
                    {
                        return false;
                    }
                }
            }
        }

        return true;
    }

    private void PrepareMessage<TMessage>(TMessage message, out int messageLength, out byte[] messageData, out DnsServer.SelectTsigKey tsigKeySelector, out byte[] tsigOriginalMac)
        where TMessage : DnsMessageBase, new()
    {
        if (message.TransactionID == 0)
        {
            message.TransactionID = (ushort)_secureRandom.Next(1, 0xffff);
        }

        if (Is0x20ValidationEnabled)
        {
            message.Questions.ForEach(q => q.Name = q.Name.Add0x20Bits());
        }

        messageLength = message.Encode(false, out messageData);

        if (message.TSigOptions != null)
        {
            tsigKeySelector = (n, a) => message.TSigOptions.KeyData;
            tsigOriginalMac = message.TSigOptions.Mac;
        }
        else
        {
            tsigKeySelector = null;
            tsigOriginalMac = null;
        }
    }

    private byte[] QueryByUdp(DnsClientEndpointInfo endpointInfo, byte[] messageData, int messageLength, out IPAddress responderAddress)
    {
        using (var udpClient = new Socket(endpointInfo.LocalAddress.AddressFamily, SocketType.Dgram, ProtocolType.Udp))
        {
            try
            {
                udpClient.ReceiveTimeout = QueryTimeout;

                PrepareAndBindUdpSocket(endpointInfo, udpClient);

                EndPoint serverEndpoint = new IPEndPoint(endpointInfo.ServerAddress, _port);

                udpClient.SendTo(messageData, messageLength, SocketFlags.None, serverEndpoint);

                if (endpointInfo.IsMulticast)
                    serverEndpoint = new IPEndPoint(udpClient.AddressFamily == AddressFamily.InterNetwork ? IPAddress.Any : IPAddress.IPv6Any, _port);

                byte[] buffer = new byte[65535];
                int length = udpClient.ReceiveFrom(buffer, 0, buffer.Length, SocketFlags.None, ref serverEndpoint);

                responderAddress = ((IPEndPoint)serverEndpoint).Address;

                byte[] res = new byte[length];
                Buffer.BlockCopy(buffer, 0, res, 0, length);
                return res;
            }
            catch (Exception e)
            {
                Trace.TraceError("Error on dns query: " + e);
                responderAddress = default(IPAddress);
                return null;
            }
        }
    }

    private void PrepareAndBindUdpSocket(DnsClientEndpointInfo endpointInfo, Socket udpClient)
    {
        if (endpointInfo.IsMulticast)
        {
            udpClient.Bind(new IPEndPoint(endpointInfo.LocalAddress, 0));
        }
        else
        {
            udpClient.Connect(endpointInfo.ServerAddress, _port);
        }
    }

    private byte[] QueryByTcp(IPAddress nameServer, byte[] messageData, int messageLength, ref TcpClient tcpClient, ref NetworkStream tcpStream, out IPAddress responderAddress)
    {
        responderAddress = nameServer;

        if (!IsTcpEnabled)
            return null;

        IPEndPoint endPoint = new IPEndPoint(nameServer, _port);

        try
        {
            if (tcpClient == null)
            {
                tcpClient = new TcpClient(nameServer.AddressFamily)
                {
                    ReceiveTimeout = QueryTimeout,
                    SendTimeout = QueryTimeout
                };

                if (!tcpClient.TryConnect(endPoint, QueryTimeout))
                    return null;

                tcpStream = tcpClient.GetStream();
            }

            int tmp = 0;
            byte[] lengthBuffer = new byte[2];

            if (messageLength > 0)
            {
                DnsMessageBase.EncodeUShort(lengthBuffer, ref tmp, (ushort)messageLength);

                tcpStream.Write(lengthBuffer, 0, 2);
                tcpStream.Write(messageData, 0, messageLength);
            }

            if (!TryRead(tcpClient, tcpStream, lengthBuffer, 2))
                return null;

            tmp = 0;
            int length = DnsMessageBase.ParseUShort(lengthBuffer, ref tmp);

            byte[] resultData = new byte[length];

            return TryRead(tcpClient, tcpStream, resultData, length) ? resultData : null;
        }
        catch (Exception e)
        {
            Trace.TraceError("Error on dns query: " + e);
            return null;
        }
    }

    private bool TryRead(TcpClient client, NetworkStream stream, byte[] buffer, int length)
    {
        int readBytes = 0;

        while (readBytes < length)
        {
            if (!client.IsConnected())
                return false;

            readBytes += stream.Read(buffer, readBytes, length - readBytes);
        }

        return true;
    }

    protected async Task<TMessage> SendMessageAsync<TMessage>(TMessage message, CancellationToken token)
        where TMessage : DnsMessageBase, new()
    {
        int messageLength;
        byte[] messageData;
        DnsServer.SelectTsigKey tsigKeySelector;
        byte[] tsigOriginalMac;

        PrepareMessage(message, out messageLength, out messageData, out tsigKeySelector, out tsigOriginalMac);

        bool sendByTcp = ((messageLength > MaximumQueryMessageSize) || message.IsTcpUsingRequested || !IsUdpEnabled);

        var endpointInfos = GetEndpointInfos();

        for (int i = 0; i < endpointInfos.Count; i++)
        {
            token.ThrowIfCancellationRequested();

            var endpointInfo = endpointInfos[i];
            QueryResponse resultData = null;

            try
            {
                resultData = await (sendByTcp ? QueryByTcpAsync(endpointInfo.ServerAddress, messageData, messageLength, null, null, token) : QuerySingleResponseByUdpAsync(endpointInfo, messageData, messageLength, token));

                if (resultData == null)
                    return null;

                TMessage result;

                try
                {
                    result = DnsMessageBase.Parse<TMessage>(resultData.Buffer, tsigKeySelector, tsigOriginalMac);
                }
                catch (Exception e)
                {
                    Trace.TraceError("Error on dns query: " + e);
                    continue;
                }

                if (!ValidateResponse(message, result))
                    continue;

                if ((result.ReturnCode != ReturnCode.NoError) && (result.ReturnCode != ReturnCode.NxDomain) && (i != endpointInfos.Count - 1))
                    continue;

                if (result.IsTcpResendingRequested)
                {
                    resultData = await QueryByTcpAsync(resultData.ResponderAddress, messageData, messageLength, resultData.TcpClient, resultData.TcpStream, token);
                    if (resultData != null)
                    {
                        TMessage tcpResult;

                        try
                        {
                            tcpResult = DnsMessageBase.Parse<TMessage>(resultData.Buffer, tsigKeySelector, tsigOriginalMac);
                        }
                        catch (Exception e)
                        {
                            Trace.TraceError("Error on dns query: " + e);
                            return null;
                        }

                        if (tcpResult.ReturnCode == ReturnCode.ServerFailure)
                        {
                            return result;
                        }
                        else
                        {
                            result = tcpResult;
                        }
                    }
                }

                bool isTcpNextMessageWaiting = result.IsTcpNextMessageWaiting(false);
                bool isSucessfullFinished = true;

                while (isTcpNextMessageWaiting)
                {
                    // ReSharper disable once PossibleNullReferenceException
                    resultData = await QueryByTcpAsync(resultData.ResponderAddress, null, 0, resultData.TcpClient, resultData.TcpStream, token);
                    if (resultData != null)
                    {
                        TMessage tcpResult;

                        try
                        {
                            tcpResult = DnsMessageBase.Parse<TMessage>(resultData.Buffer, tsigKeySelector, tsigOriginalMac);
                        }
                        catch (Exception e)
                        {
                            Trace.TraceError("Error on dns query: " + e);
                            isSucessfullFinished = false;
                            break;
                        }

                        if (tcpResult.ReturnCode == ReturnCode.ServerFailure)
                        {
                            isSucessfullFinished = false;
                            break;
                        }
                        else
                        {
                            result.AnswerRecords.AddRange(tcpResult.AnswerRecords);
                            isTcpNextMessageWaiting = tcpResult.IsTcpNextMessageWaiting(true);
                        }
                    }
                    else
                    {
                        isSucessfullFinished = false;
                        break;
                    }
                }

                if (isSucessfullFinished)
                    return result;
            }
            finally
            {
                if (resultData != null)
                {
                    try
                    {
                        resultData.TcpStream?.Dispose();
                        resultData.TcpClient?.Close();
                    }
                    catch
                    {
                        // ignored
                    }
                }
            }
        }

        return null;
    }

    private async Task<QueryResponse> QuerySingleResponseByUdpAsync(DnsClientEndpointInfo endpointInfo, byte[] messageData, int messageLength, CancellationToken token)
    {
        try
        {
            if (endpointInfo.IsMulticast)
            {
                using (UdpClient udpClient = new UdpClient(new IPEndPoint(endpointInfo.LocalAddress, 0)))
                {
                    IPEndPoint serverEndpoint = new IPEndPoint(endpointInfo.ServerAddress, _port);
                    await udpClient.SendAsync(messageData, messageLength, serverEndpoint);

                    udpClient.Client.SendTimeout = QueryTimeout;
                    udpClient.Client.ReceiveTimeout = QueryTimeout;

                    UdpReceiveResult response = await udpClient.ReceiveAsync(QueryTimeout, token);
                    return new QueryResponse(response.Buffer, response.RemoteEndPoint.Address);
                }
            }
            else
            {
                using (UdpClient udpClient = new UdpClient(endpointInfo.LocalAddress.AddressFamily))
                {
                    udpClient.Connect(endpointInfo.ServerAddress, _port);

                    udpClient.Client.SendTimeout = QueryTimeout;
                    udpClient.Client.ReceiveTimeout = QueryTimeout;

                    await udpClient.SendAsync(messageData, messageLength);

                    UdpReceiveResult response = await udpClient.ReceiveAsync(QueryTimeout, token);
                    return new QueryResponse(response.Buffer, response.RemoteEndPoint.Address);
                }
            }
        }
        catch (Exception e)
        {
            Trace.TraceError("Error on dns query: " + e);
            return null;
        }
    }

    private class QueryResponse
    {
        public byte[] Buffer { get; }
        public IPAddress ResponderAddress { get; }

        public TcpClient TcpClient { get; }
        public NetworkStream TcpStream { get; }

        public QueryResponse(byte[] buffer, IPAddress responderAddress)
        {
            Buffer = buffer;
            ResponderAddress = responderAddress;
        }

        public QueryResponse(byte[] buffer, IPAddress responderAddress, TcpClient tcpClient, NetworkStream tcpStream)
        {
            Buffer = buffer;
            ResponderAddress = responderAddress;
            TcpClient = tcpClient;
            TcpStream = tcpStream;
        }
    }

    private async Task<QueryResponse> QueryByTcpAsync(IPAddress nameServer, byte[] messageData, int messageLength, TcpClient tcpClient, NetworkStream tcpStream, CancellationToken token)
    {
        if (!IsTcpEnabled)
            return null;

        try
        {
            if (tcpClient == null)
            {
                tcpClient = new TcpClient(nameServer.AddressFamily)
                {
                    ReceiveTimeout = QueryTimeout,
                    SendTimeout = QueryTimeout
                };

                if (!await tcpClient.TryConnectAsync(nameServer, _port, QueryTimeout, token))
                {
                    return null;
                }

                tcpStream = tcpClient.GetStream();
            }

            int tmp = 0;
            byte[] lengthBuffer = new byte[2];

            if (messageLength > 0)
            {
                DnsMessageBase.EncodeUShort(lengthBuffer, ref tmp, (ushort)messageLength);

                await tcpStream.WriteAsync(lengthBuffer, 0, 2, token);
                await tcpStream.WriteAsync(messageData, 0, messageLength, token);
            }

            if (!await TryReadAsync(tcpClient, tcpStream, lengthBuffer, 2, token))
                return null;

            tmp = 0;
            int length = DnsMessageBase.ParseUShort(lengthBuffer, ref tmp);

            byte[] resultData = new byte[length];

            return await TryReadAsync(tcpClient, tcpStream, resultData, length, token) ? new QueryResponse(resultData, nameServer, tcpClient, tcpStream) : null;
        }
        catch (Exception e)
        {
            Trace.TraceError("Error on dns query: " + e);
            return null;
        }
    }

    private async Task<bool> TryReadAsync(TcpClient client, NetworkStream stream, byte[] buffer, int length, CancellationToken token)
    {
        int readBytes = 0;

        while (readBytes < length)
        {
            if (token.IsCancellationRequested || !client.IsConnected())
                return false;

            readBytes += await stream.ReadAsync(buffer, readBytes, length - readBytes, token);
        }

        return true;
    }

    protected async Task<List<TMessage>> SendMessageParallelAsync<TMessage>(TMessage message, CancellationToken token)
        where TMessage : DnsMessageBase, new()
    {
        int messageLength;
        byte[] messageData;
        DnsServer.SelectTsigKey tsigKeySelector;
        byte[] tsigOriginalMac;

        PrepareMessage(message, out messageLength, out messageData, out tsigKeySelector, out tsigOriginalMac);

        if (messageLength > MaximumQueryMessageSize)
            throw new ArgumentException("Message exceeds maximum size");

        if (message.IsTcpUsingRequested)
            throw new NotSupportedException("Using tcp is not supported in parallel mode");

        BlockingCollection<TMessage> results = new BlockingCollection<TMessage>();
        CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();

        // ReSharper disable once ReturnValueOfPureMethodIsNotUsed
        GetEndpointInfos().Select(x => SendMessageParallelAsync(x, message, messageData, messageLength, tsigKeySelector, tsigOriginalMac, results, CancellationTokenSource.CreateLinkedTokenSource(token, cancellationTokenSource.Token).Token)).ToArray();

        await Task.Delay(QueryTimeout, token);

        cancellationTokenSource.Cancel();

        return results.ToList();
    }

    private async Task SendMessageParallelAsync<TMessage>(DnsClientEndpointInfo endpointInfo, TMessage message, byte[] messageData, int messageLength, DnsServer.SelectTsigKey tsigKeySelector, byte[] tsigOriginalMac, BlockingCollection<TMessage> results, CancellationToken token)
        where TMessage : DnsMessageBase, new()
    {
        using (UdpClient udpClient = new UdpClient(new IPEndPoint(endpointInfo.LocalAddress, 0)))
        {
            IPEndPoint serverEndpoint = new IPEndPoint(endpointInfo.ServerAddress, _port);
            await udpClient.SendAsync(messageData, messageLength, serverEndpoint);

            udpClient.Client.SendTimeout = QueryTimeout;
            udpClient.Client.ReceiveTimeout = QueryTimeout;

            while (true)
            {
                TMessage result;
                UdpReceiveResult response = await udpClient.ReceiveAsync(Int32.MaxValue, token);

                try
                {
                    result = DnsMessageBase.Parse<TMessage>(response.Buffer, tsigKeySelector, tsigOriginalMac);
                }
                catch (Exception e)
                {
                    Trace.TraceError("Error on dns query: " + e);
                    continue;
                }

                if (!ValidateResponse(message, result))
                    continue;

                if (result.ReturnCode == ReturnCode.ServerFailure)
                    continue;

                results.Add(result, token);

                if (token.IsCancellationRequested)
                    break;
            }
        }
    }

    private List<DnsClientEndpointInfo> GetEndpointInfos()
    {
        List<DnsClientEndpointInfo> endpointInfos;
        if (_isAnyServerMulticast)
        {
            var localIPs = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.SupportsMulticast && (n.OperationalStatus == OperationalStatus.Up) && (n.NetworkInterfaceType != NetworkInterfaceType.Loopback))
                .SelectMany(n => n.GetIPProperties().UnicastAddresses.Select(a => a.Address))
                .Where(a => !IPAddress.IsLoopback(a) && ((a.AddressFamily == AddressFamily.InterNetwork) || a.IsIPv6LinkLocal))
                .ToList();

            endpointInfos = _servers
                .SelectMany(
                    s =>
                    {
                        if (s.IsMulticast())
                        {
                            return localIPs
                                .Where(l => l.AddressFamily == s.AddressFamily)
                                .Select(
                                    l => new DnsClientEndpointInfo
                                    {
                                        IsMulticast = true,
                                        ServerAddress = s,
                                        LocalAddress = l
                                    });
                        }
                        else
                        {
                            return new[]
                            {
                                    new DnsClientEndpointInfo
                                    {
                                        IsMulticast = false,
                                        ServerAddress = s,
                                        LocalAddress = s.AddressFamily == AddressFamily.InterNetwork ? IPAddress.Any : IPAddress.IPv6Any
                                    }
                            };
                        }
                    }).ToList();
        }
        else
        {
            endpointInfos = _servers
                .Where(x => IsIPv6Enabled || (x.AddressFamily == AddressFamily.InterNetwork))
                .Select(
                    s => new DnsClientEndpointInfo
                    {
                        IsMulticast = false,
                        ServerAddress = s,
                        LocalAddress = s.AddressFamily == AddressFamily.InterNetwork ? IPAddress.Any : IPAddress.IPv6Any
                    }
                ).ToList();
        }
        return endpointInfos;
    }

    private static bool IsIPv6Enabled { get; } = IsAnyIPv6Configured();

    private static readonly IPAddress _ipvMappedNetworkAddress = IPAddress.Parse("0:0:0:0:0:FFFF::");

    private static bool IsAnyIPv6Configured()
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => (n.OperationalStatus == OperationalStatus.Up) && (n.NetworkInterfaceType != NetworkInterfaceType.Loopback))
            .SelectMany(n => n.GetIPProperties().UnicastAddresses.Select(a => a.Address))
            .Any(a => !IPAddress.IsLoopback(a) && (a.AddressFamily == AddressFamily.InterNetworkV6) && !a.IsIPv6LinkLocal && !a.IsIPv6Teredo && !a.GetNetworkAddress(96).Equals(_ipvMappedNetworkAddress));
    }
}

internal class DnsClientEndpointInfo
{
    public bool IsMulticast;
    public IPAddress LocalAddress;
    public IPAddress ServerAddress;
}

internal class DnsClientParallelAsyncState<TMessage> : IAsyncResult
    where TMessage : DnsMessageBase
{
    internal int ResponsesToReceive = 0;
    internal List<TMessage> Responses = null;

    internal AsyncCallback UserCallback = null;
    public object AsyncState { get; internal set; }
    public bool IsCompleted { get; private set; }

    public bool CompletedSynchronously
    {
        get { return false; }
    }

    private ManualResetEvent _waitHandle;

    public WaitHandle AsyncWaitHandle
    {
        get { return _waitHandle ?? (_waitHandle = new ManualResetEvent(IsCompleted)); }
    }

    internal void SetCompleted()
    {
        IsCompleted = true;

        if (_waitHandle != null)
            _waitHandle.Set();

        if (UserCallback != null)
            UserCallback(this);
    }
}

internal class DnsClientParallelState<TMessage>
    where TMessage : DnsMessageBase
{
    internal object Lock = new object();
    internal IAsyncResult SingleMessageAsyncResult = null;
    internal DnsClientParallelAsyncState<TMessage> ParallelMessageAsyncState = null;
}

/// <summary>
///   Message returned as result to a dns query
/// </summary>
public class DnsMessage : DnsMessageBase
{
    /// <summary>
    ///   Parses a the contents of a byte array as DnsMessage
    /// </summary>
    /// <param name="data">Buffer, that contains the message data</param>
    /// <returns>A new instance of the DnsMessage class</returns>
    public static DnsMessage Parse(byte[] data)
    {
        return Parse<DnsMessage>(data);
    }

    #region Header
    /// <summary>
    ///   <para>Gets or sets the autoritive answer (AA) flag</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc1035">RFC 1035</see>
    ///   </para>
    /// </summary>
    public bool IsAuthoritiveAnswer
    {
        get { return (Flags & 0x0400) != 0; }
        set
        {
            if (value)
            {
                Flags |= 0x0400;
            }
            else
            {
                Flags &= 0xfbff;
            }
        }
    }

    /// <summary>
    ///   <para>Gets or sets the truncated response (TC) flag</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc1035">RFC 1035</see>
    ///   </para>
    /// </summary>
    public bool IsTruncated
    {
        get { return (Flags & 0x0200) != 0; }
        set
        {
            if (value)
            {
                Flags |= 0x0200;
            }
            else
            {
                Flags &= 0xfdff;
            }
        }
    }

    /// <summary>
    ///   <para>Gets or sets the recursion desired (RD) flag</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc1035">RFC 1035</see>
    ///   </para>
    /// </summary>
    public bool IsRecursionDesired
    {
        get { return (Flags & 0x0100) != 0; }
        set
        {
            if (value)
            {
                Flags |= 0x0100;
            }
            else
            {
                Flags &= 0xfeff;
            }
        }
    }

    /// <summary>
    ///   <para>Gets or sets the recursion allowed (RA) flag</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc1035">RFC 1035</see>
    ///   </para>
    /// </summary>
    public bool IsRecursionAllowed
    {
        get { return (Flags & 0x0080) != 0; }
        set
        {
            if (value)
            {
                Flags |= 0x0080;
            }
            else
            {
                Flags &= 0xff7f;
            }
        }
    }

    /// <summary>
    ///   <para>Gets or sets the authentic data (AD) flag</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc4035">RFC 4035</see>
    ///   </para>
    /// </summary>
    public bool IsAuthenticData
    {
        get { return (Flags & 0x0020) != 0; }
        set
        {
            if (value)
            {
                Flags |= 0x0020;
            }
            else
            {
                Flags &= 0xffdf;
            }
        }
    }

    /// <summary>
    ///   <para>Gets or sets the checking disabled (CD) flag</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc4035">RFC 4035</see>
    ///   </para>
    /// </summary>
    public bool IsCheckingDisabled
    {
        get { return (Flags & 0x0010) != 0; }
        set
        {
            if (value)
            {
                Flags |= 0x0010;
            }
            else
            {
                Flags &= 0xffef;
            }
        }
    }
    #endregion

    /// <summary>
    ///   Gets or sets the entries in the question section
    /// </summary>
    public new List<DnsQuestion> Questions
    {
        get { return base.Questions; }
        set { base.Questions = (value ?? new List<DnsQuestion>()); }
    }

    /// <summary>
    ///   Gets or sets the entries in the answer records section
    /// </summary>
    public new List<DnsRecordBase> AnswerRecords
    {
        get { return base.AnswerRecords; }
        set { base.AnswerRecords = (value ?? new List<DnsRecordBase>()); }
    }

    /// <summary>
    ///   Gets or sets the entries in the authority records section
    /// </summary>
    public new List<DnsRecordBase> AuthorityRecords
    {
        get { return base.AuthorityRecords; }
        set { base.AuthorityRecords = (value ?? new List<DnsRecordBase>()); }
    }

    /// <summary>
    ///   <para>Gets or sets the DNSSEC answer OK (DO) flag</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc4035">RFC 4035</see>
    ///     and
    ///     <see cref="!:http://tools.ietf.org/html/rfc3225">RFC 3225</see>
    ///   </para>
    /// </summary>
    public bool IsDnsSecOk
    {
        get
        {
            OptRecord ednsOptions = EDnsOptions;
            return (ednsOptions != null) && ednsOptions.IsDnsSecOk;
        }
        set
        {
            OptRecord ednsOptions = EDnsOptions;
            if (ednsOptions == null)
            {
                if (value)
                {
                    throw new ArgumentOutOfRangeException(nameof(value), "Setting DO flag is allowed in edns messages only");
                }
            }
            else
            {
                ednsOptions.IsDnsSecOk = value;
            }
        }
    }

    /// <summary>
    ///   Creates a new instance of the DnsMessage as response to the current instance
    /// </summary>
    /// <returns>A new instance of the DnsMessage as response to the current instance</returns>
    public DnsMessage CreateResponseInstance()
    {
        DnsMessage result = new DnsMessage()
        {
            TransactionID = TransactionID,
            IsEDnsEnabled = IsEDnsEnabled,
            IsQuery = false,
            OperationCode = OperationCode,
            IsRecursionDesired = IsRecursionDesired,
            IsCheckingDisabled = IsCheckingDisabled,
            IsDnsSecOk = IsDnsSecOk,
            Questions = new List<DnsQuestion>(Questions),
        };

        if (IsEDnsEnabled)
        {
            result.EDnsOptions.Version = EDnsOptions.Version;
            result.EDnsOptions.UdpPayloadSize = EDnsOptions.UdpPayloadSize;
        }

        return result;
    }

    internal override bool IsTcpUsingRequested => (Questions.Count > 0) && ((Questions[0].RecordType == RecordType.Axfr) || (Questions[0].RecordType == RecordType.Ixfr));

    internal override bool IsTcpResendingRequested => IsTruncated;

    internal override bool IsTcpNextMessageWaiting(bool isSubsequentResponseMessage)
    {
        if (isSubsequentResponseMessage)
        {
            return (AnswerRecords.Count > 0) && (AnswerRecords[AnswerRecords.Count - 1].RecordType != RecordType.Soa);
        }

        if (Questions.Count == 0)
            return false;

        if ((Questions[0].RecordType != RecordType.Axfr) && (Questions[0].RecordType != RecordType.Ixfr))
            return false;

        return (AnswerRecords.Count > 0)
               && (AnswerRecords[0].RecordType == RecordType.Soa)
               && ((AnswerRecords.Count == 1) || (AnswerRecords[AnswerRecords.Count - 1].RecordType != RecordType.Soa));
    }
}

/// <summary>
///   Base class for a dns answer
/// </summary>
public abstract class DnsMessageBase
{
    public ushort Flags;

    protected internal List<DnsQuestion> Questions = new List<DnsQuestion>();
    protected internal List<DnsRecordBase> AnswerRecords = new List<DnsRecordBase>();
    protected internal List<DnsRecordBase> AuthorityRecords = new List<DnsRecordBase>();

    private List<DnsRecordBase> _additionalRecords = new List<DnsRecordBase>();

    /// <summary>
    ///   Gets or sets the entries in the additional records section
    /// </summary>
    public List<DnsRecordBase> AdditionalRecords
    {
        get { return _additionalRecords; }
        set { _additionalRecords = (value ?? new List<DnsRecordBase>()); }
    }

    internal abstract bool IsTcpUsingRequested { get; }
    internal abstract bool IsTcpResendingRequested { get; }
    internal abstract bool IsTcpNextMessageWaiting(bool isSubsequentResponseMessage);

    #region Header
    /// <summary>
    ///   Gets or sets the transaction identifier (ID) of the message
    /// </summary>
    public ushort TransactionID { get; set; }

    /// <summary>
    ///   Gets or sets the query (QR) flag
    /// </summary>
    public bool IsQuery
    {
        get { return (Flags & 0x8000) == 0; }
        set
        {
            if (value)
            {
                Flags &= 0x7fff;
            }
            else
            {
                Flags |= 0x8000;
            }
        }
    }

    /// <summary>
    ///   Gets or sets the Operation Code (OPCODE)
    /// </summary>
    public OperationCode OperationCode
    {
        get { return (OperationCode)((Flags & 0x7800) >> 11); }
        set
        {
            ushort clearedOp = (ushort)(Flags & 0x8700);
            Flags = (ushort)(clearedOp | (ushort)value << 11);
        }
    }

    /// <summary>
    ///   Gets or sets the return code (RCODE)
    /// </summary>
    public ReturnCode ReturnCode
    {
        get
        {
            ReturnCode rcode = (ReturnCode)(Flags & 0x000f);

            OptRecord ednsOptions = EDnsOptions;
            if (ednsOptions == null)
            {
                return rcode;
            }
            else
            {
                // ReSharper disable once BitwiseOperatorOnEnumWithoutFlags
                return (rcode | ednsOptions.ExtendedReturnCode);
            }
        }
        set
        {
            OptRecord ednsOptions = EDnsOptions;

            if ((ushort)value > 15)
            {
                if (ednsOptions == null)
                {
                    throw new ArgumentOutOfRangeException(nameof(value), "ReturnCodes greater than 15 only allowed in edns messages");
                }
                else
                {
                    ednsOptions.ExtendedReturnCode = value;
                }
            }
            else
            {
                if (ednsOptions != null)
                {
                    ednsOptions.ExtendedReturnCode = 0;
                }
            }

            ushort clearedOp = (ushort)(Flags & 0xfff0);
            Flags = (ushort)(clearedOp | ((ushort)value & 0x0f));
        }
    }
    #endregion

    #region EDNS
    /// <summary>
    ///   Enables or disables EDNS
    /// </summary>
    public bool IsEDnsEnabled
    {
        get
        {
            if (_additionalRecords != null)
            {
                foreach (var item in _additionalRecords)
                    if (item.RecordType == RecordType.Opt)
                        return true;

                return false;
            }
            else
            {
                return false;
            }
        }
        set
        {
            if (value && !IsEDnsEnabled)
            {
                if (_additionalRecords == null)
                {
                    _additionalRecords = new List<DnsRecordBase>();
                }
                _additionalRecords.Add(new OptRecord());
            }
            else if (!value && IsEDnsEnabled)
            {
                _additionalRecords.RemoveAll(record => (record.RecordType == RecordType.Opt));
            }
        }
    }

    /// <summary>
    ///   Gets or set the OptRecord for the EDNS options
    /// </summary>
    public OptRecord EDnsOptions
    {
        get { return (OptRecord)_additionalRecords?.Find(record => (record.RecordType == RecordType.Opt)); }
        set
        {
            if (value == null)
            {
                IsEDnsEnabled = false;
            }
            else if (IsEDnsEnabled)
            {
                int pos = _additionalRecords.FindIndex(record => (record.RecordType == RecordType.Opt));
                _additionalRecords[pos] = value;
            }
            else
            {
                if (_additionalRecords == null)
                {
                    _additionalRecords = new List<DnsRecordBase>();
                }
                _additionalRecords.Add(value);
            }
        }
    }
    #endregion

    #region TSig
    /// <summary>
    ///   Gets or set the TSigRecord for the tsig signed messages
    /// </summary>
    // ReSharper disable once InconsistentNaming
    public TSigRecord TSigOptions { get; set; }

    internal static DnsMessageBase CreateByFlag(ReadOnlySpan<byte> data, DnsServer.SelectTsigKey tsigKeySelector, byte[] originalMac)
    {
        int flagPosition = 2;
        ushort flags = ParseUShort(data, ref flagPosition);

        DnsMessageBase res;

        switch ((OperationCode)((flags & 0x7800) >> 11))
        {
            case OperationCode.Update:
                res = new DnsUpdateMessage();
                break;

            default:
                res = new DnsMessage();
                break;
        }

        res.ParseInternal(data, tsigKeySelector, originalMac);

        return res;
    }

    internal static TMessage Parse<TMessage>(byte[] data)
        where TMessage : DnsMessageBase, new()
    {
        return Parse<TMessage>(data, null, null);
    }

    internal static TMessage Parse<TMessage>(byte[] data, DnsServer.SelectTsigKey tsigKeySelector, byte[] originalMac)
        where TMessage : DnsMessageBase, new()
    {
        TMessage result = new TMessage();
        result.ParseInternal(data, tsigKeySelector, originalMac);
        return result;
    }

    private void ParseInternal(ReadOnlySpan<byte> data, DnsServer.SelectTsigKey tsigKeySelector, byte[] originalMac)
    {
        int currentPosition = 0;

        TransactionID = ParseUShort(data, ref currentPosition);
        Flags = ParseUShort(data, ref currentPosition);

        int questionCount = ParseUShort(data, ref currentPosition);
        int answerRecordCount = ParseUShort(data, ref currentPosition);
        int authorityRecordCount = ParseUShort(data, ref currentPosition);
        int additionalRecordCount = ParseUShort(data, ref currentPosition);

        ParseQuestions(data, ref currentPosition, questionCount);
        ParseSection(data, ref currentPosition, AnswerRecords, answerRecordCount);
        ParseSection(data, ref currentPosition, AuthorityRecords, authorityRecordCount);
        ParseSection(data, ref currentPosition, _additionalRecords, additionalRecordCount);

        if (_additionalRecords.Count > 0)
        {
            int tSigPos = _additionalRecords.FindIndex(record => (record.RecordType == RecordType.TSig));
            if (tSigPos == (_additionalRecords.Count - 1))
            {
                TSigOptions = (TSigRecord)_additionalRecords[tSigPos];

                _additionalRecords.RemoveAt(tSigPos);

                TSigOptions.ValidationResult = ValidateTSig(data, tsigKeySelector, originalMac);
            }
        }

        FinishParsing();
    }

    private ReturnCode ValidateTSig(ReadOnlySpan<byte> resultData, DnsServer.SelectTsigKey tsigKeySelector, byte[] originalMac)
    {
        byte[] keyData;
        if ((TSigOptions.Algorithm == TSigAlgorithm.Unknown) || (tsigKeySelector == null) || ((keyData = tsigKeySelector(TSigOptions.Algorithm, TSigOptions.Name)) == null))
        {
            return ReturnCode.BadKey;
        }
        else if (((TSigOptions.TimeSigned - TSigOptions.Fudge) > DateTime.Now) || ((TSigOptions.TimeSigned + TSigOptions.Fudge) < DateTime.Now))
        {
            return ReturnCode.BadTime;
        }
        else if ((TSigOptions.Mac == null) || (TSigOptions.Mac.Length == 0))
        {
            return ReturnCode.BadSig;
        }
        else
        {
            TSigOptions.KeyData = keyData;

            // maxLength for the buffer to validate: Original (unsigned) dns message and encoded TSigOptions
            // because of compression of keyname, the size of the signed message can not be used
            int maxLength = TSigOptions.StartPosition + TSigOptions.MaximumLength;
            if (originalMac != null)
            {
                // add length of mac on responses. MacSize not neccessary, this field is allready included in the size of the tsig options
                maxLength += originalMac.Length;
            }

            byte[] validationBuffer = new byte[maxLength];

            int currentPosition = 0;

            // original mac if neccessary
            if ((originalMac != null) && (originalMac.Length > 0))
            {
                EncodeUShort(validationBuffer, ref currentPosition, (ushort)originalMac.Length);
                EncodeByteArray(validationBuffer, ref currentPosition, originalMac);
            }

            // original unsiged buffer
            resultData.CopyTo(validationBuffer.AsSpan(currentPosition, TSigOptions.StartPosition));
            //Buffer.BlockCopy(resultData, 0, validationBuffer, currentPosition, TSigOptions.StartPosition);

            // update original transaction id and ar count in message
            EncodeUShort(validationBuffer, currentPosition, TSigOptions.OriginalID);
            EncodeUShort(validationBuffer, currentPosition + 10, (ushort)_additionalRecords.Count);
            currentPosition += TSigOptions.StartPosition;

            // TSig Variables
            EncodeDomainName(validationBuffer, 0, ref currentPosition, TSigOptions.Name, null, false);
            EncodeUShort(validationBuffer, ref currentPosition, (ushort)TSigOptions.RecordClass);
            EncodeInt(validationBuffer, ref currentPosition, (ushort)TSigOptions.TimeToLive);
            EncodeDomainName(validationBuffer, 0, ref currentPosition, TSigAlgorithmHelper.GetDomainName(TSigOptions.Algorithm), null, false);
            TSigRecord.EncodeDateTime(validationBuffer, ref currentPosition, TSigOptions.TimeSigned);
            EncodeUShort(validationBuffer, ref currentPosition, (ushort)TSigOptions.Fudge.TotalSeconds);
            EncodeUShort(validationBuffer, ref currentPosition, (ushort)TSigOptions.Error);
            EncodeUShort(validationBuffer, ref currentPosition, (ushort)TSigOptions.OtherData.Length);
            EncodeByteArray(validationBuffer, ref currentPosition, TSigOptions.OtherData);

            // Validate MAC
            KeyedHashAlgorithm hashAlgorithm = TSigAlgorithmHelper.GetHashAlgorithm(TSigOptions.Algorithm);
            hashAlgorithm.Key = keyData;
            return (hashAlgorithm.ComputeHash(validationBuffer, 0, currentPosition).SequenceEqual(TSigOptions.Mac)) ? ReturnCode.NoError : ReturnCode.BadSig;
        }
    }
    #endregion

    #region Parsing
    protected virtual void FinishParsing() { }

    #region Methods for parsing answer
    private static void ParseSection(ReadOnlySpan<byte> resultData, ref int currentPosition, List<DnsRecordBase> sectionList, int recordCount)
    {
        for (int i = 0; i < recordCount; i++)
        {
            sectionList.Add(ParseRecord(resultData, ref currentPosition));
        }
    }

    private static DnsRecordBase ParseRecord(ReadOnlySpan<byte> resultData, ref int currentPosition)
    {
        int startPosition = currentPosition;

        DomainName name = ParseDomainName(resultData, ref currentPosition);
        RecordType recordType = (RecordType)ParseUShort(resultData, ref currentPosition);
        DnsRecordBase record = DnsRecordBase.Create(recordType, resultData, currentPosition + 6);
        record.StartPosition = startPosition;
        record.Name = name;
        record.RecordType = recordType;
        record.RecordClass = (RecordClass)ParseUShort(resultData, ref currentPosition);
        record.TimeToLive = ParseInt(resultData, ref currentPosition);
        record.RecordDataLength = ParseUShort(resultData, ref currentPosition);

        if (record.RecordDataLength > 0)
        {
            record.ParseRecordData(resultData, currentPosition, record.RecordDataLength);
            currentPosition += record.RecordDataLength;
        }

        return record;
    }

    private void ParseQuestions(ReadOnlySpan<byte> resultData, ref int currentPosition, int recordCount)
    {
        for (int i = 0; i < recordCount; i++)
        {
            DnsQuestion question = new DnsQuestion { Name = ParseDomainName(resultData, ref currentPosition), RecordType = (RecordType)ParseUShort(resultData, ref currentPosition), RecordClass = (RecordClass)ParseUShort(resultData, ref currentPosition) };

            Questions.Add(question);
        }
    }
    #endregion

    #region Helper methods for parsing records
    internal static string ParseText(ReadOnlySpan<byte> resultData, ref int currentPosition)
    {
        int length = resultData[currentPosition++];
        return ParseText(resultData, ref currentPosition, length);
    }

    internal static string ParseText(ReadOnlySpan<byte> resultData, ref int currentPosition, int length)
    {
        string res = Encoding.ASCII.GetString(resultData.Slice(currentPosition, length));
        currentPosition += length;

        return res;
    }

    [MethodImpl(Inline)]
    internal static DomainName ParseDomainName(ReadOnlySpan<byte> resultData, ref int currentPosition)
    {
        int firstLabelLength;
        DomainName res = ParseDomainName(resultData, currentPosition, out firstLabelLength);
        currentPosition += firstLabelLength;
        return res;
    }

    [MethodImpl(Inline)]
    internal static ushort ParseUShort(ReadOnlySpan<byte> resultData, ref int currentPosition)
    {
        ushort res;

        if (BitConverter.IsLittleEndian)
        {
            res = (ushort)((resultData[currentPosition++] << 8) | resultData[currentPosition++]);
        }
        else
        {
            res = (ushort)(resultData[currentPosition++] | (resultData[currentPosition++] << 8));
        }

        return res;
    }

    [MethodImpl(Inline)]
    internal static int ParseInt(ReadOnlySpan<byte> resultData, ref int currentPosition)
    {
        int res;

        if (BitConverter.IsLittleEndian)
        {
            res = ((resultData[currentPosition++] << 24) | (resultData[currentPosition++] << 16) | (resultData[currentPosition++] << 8) | resultData[currentPosition++]);
        }
        else
        {
            res = (resultData[currentPosition++] | (resultData[currentPosition++] << 8) | (resultData[currentPosition++] << 16) | (resultData[currentPosition++] << 24));
        }

        return res;
    }

    [MethodImpl(Inline)]
    internal static uint ParseUInt(ReadOnlySpan<byte> resultData, ref int currentPosition)
    {
        uint res;

        if (BitConverter.IsLittleEndian)
        {
            res = (((uint)resultData[currentPosition++] << 24) | ((uint)resultData[currentPosition++] << 16) | ((uint)resultData[currentPosition++] << 8) | resultData[currentPosition++]);
        }
        else
        {
            res = (resultData[currentPosition++] | ((uint)resultData[currentPosition++] << 8) | ((uint)resultData[currentPosition++] << 16) | ((uint)resultData[currentPosition++] << 24));
        }

        return res;
    }

    [MethodImpl(Inline)]
    internal static ulong ParseULong(ReadOnlySpan<byte> resultData, ref int currentPosition)
    {
        ulong res;

        if (BitConverter.IsLittleEndian)
        {
            res = ((ulong)ParseUInt(resultData, ref currentPosition) << 32) | ParseUInt(resultData, ref currentPosition);
        }
        else
        {
            res = ParseUInt(resultData, ref currentPosition) | ((ulong)ParseUInt(resultData, ref currentPosition) << 32);
        }

        return res;
    }

    private static DomainName ParseDomainName(ReadOnlySpan<byte> resultData, int currentPosition, out int uncompressedLabelBytes)
    {
        List<string> labels = new List<string>();

        bool isInUncompressedSpace = true;
        uncompressedLabelBytes = 0;

        for (int i = 0; i < 127; i++) // max is 127 labels (see RFC 2065)
        {
            byte currentByte = resultData[currentPosition++];
            if (currentByte == 0)
            {
                // end of domain, RFC1035
                if (isInUncompressedSpace)
                    uncompressedLabelBytes += 1;

                return new DomainName(labels.ToArray());
            }
            else if (currentByte >= 192)
            {
                // Pointer, RFC1035

                if (isInUncompressedSpace)
                {
                    uncompressedLabelBytes += 2;
                    isInUncompressedSpace = false;
                }

                int pointer;
                if (BitConverter.IsLittleEndian)
                {
                    pointer = (ushort)(((currentByte - 192) << 8) | resultData[currentPosition]);
                }
                else
                {
                    pointer = (ushort)((currentByte - 192) | (resultData[currentPosition] << 8));
                }

                currentPosition = pointer;
            }
            else if (currentByte == 65)
            {
                // binary EDNS label, RFC2673, RFC3363, RFC3364
                int length = resultData[currentPosition++];
                if (isInUncompressedSpace)
                    uncompressedLabelBytes += 1;
                if (length == 0)
                    length = 256;

                StringBuilder sb = new StringBuilder();

                sb.Append(@"\[x");
                string suffix = "/" + length + "]";

                do
                {
                    currentByte = resultData[currentPosition++];
                    if (isInUncompressedSpace)
                        uncompressedLabelBytes += 1;

                    if (length < 8)
                    {
                        currentByte &= (byte)(0xff >> (8 - length));
                    }

                    sb.Append(currentByte.ToString("x2"));

                    length = length - 8;
                } while (length > 0);

                sb.Append(suffix);

                labels.Add(sb.ToString());
            }
            else if (currentByte >= 64)
            {
                // extended dns label RFC 2671
                throw new NotSupportedException("Unsupported extended dns label");
            }
            else
            {
                // append additional text part
                if (isInUncompressedSpace)
                    uncompressedLabelBytes += 1 + currentByte;

                labels.Add(Encoding.ASCII.GetString(resultData.Slice(currentPosition, currentByte)));
                currentPosition += currentByte;
            }
        }

        throw new FormatException("Domain name could not be parsed. Invalid message?");
    }

    internal static byte[] ParseByteData(ReadOnlySpan<byte> resultData, ref int currentPosition, int length)
    {
        if (length == 0)
        {
            return new byte[] { };
        }
        else
        {
            byte[] res = new byte[length];
            resultData.Slice(currentPosition).CopyTo(res);
            //Buffer.BlockCopy(resultData, currentPosition, res, 0, length);
            currentPosition += length;
            return res;
        }
    }
    #endregion

    #endregion

    #region Serializing
    [MethodImpl(Inline)]
    protected virtual void PrepareEncoding() { }

    [MethodImpl(Inline)]
    internal int Encode(bool addLengthPrefix, out byte[] messageData)
    {
        byte[] newTSigMac;

        return Encode(addLengthPrefix, null, false, out messageData, out newTSigMac);
    }

    [MethodImpl(Inline)]
    internal int Encode(bool addLengthPrefix, byte[] originalTsigMac, out byte[] messageData)
    {
        byte[] newTSigMac;

        return Encode(addLengthPrefix, originalTsigMac, false, out messageData, out newTSigMac);
    }

    [MethodImpl(Inline)]
    internal int Encode(bool addLengthPrefix, byte[] originalTsigMac, bool isSubSequentResponse, out byte[] messageData, out byte[] newTSigMac)
    {
        var span = Encode(addLengthPrefix, originalTsigMac, isSubSequentResponse, out newTSigMac);
        messageData = span.ToArray();
        return span.Length;
    }

    [MethodImpl(Inline)]
    internal Span<byte> Encode(bool addLengthPrefix)
    {
        byte[] newTSigMac;

        return Encode(addLengthPrefix, null, false, out newTSigMac);
    }

    [MethodImpl(Inline)]
    internal Span<byte> Encode(bool addLengthPrefix, byte[] originalTsigMac)
    {
        byte[] newTSigMac;

        return Encode(addLengthPrefix, originalTsigMac, false, out newTSigMac);
    }

    [MethodImpl(Inline)]
    internal Span<byte> Encode(bool addLengthPrefix, byte[] originalTsigMac, bool isSubSequentResponse, out byte[] newTSigMac)
    {
        PrepareEncoding();

        int offset = 0;
        int messageOffset = offset;
        int maxLength = addLengthPrefix ? 2 : 0;

        if (TSigOptions != null)
        {
            originalTsigMac = originalTsigMac ?? new byte[] { };

            if (!IsQuery)
            {
                offset += 2 + originalTsigMac.Length;
                maxLength += 2 + originalTsigMac.Length;
            }

            maxLength += TSigOptions.MaximumLength;
        }

        #region Get Message Length
        maxLength += 12;

        if (Questions.Count >= 1)
            foreach (var item in Questions)
                maxLength += item.MaximumLength;

        if (AnswerRecords.Count >= 1)
            foreach (var item in AnswerRecords)
                maxLength += item.MaximumLength;

        if (AuthorityRecords.Count >= 1)
            foreach (var item in AuthorityRecords)
                maxLength += item.MaximumLength;

        if (_additionalRecords.Count >= 1)
            foreach (var item in _additionalRecords)
                maxLength += item.MaximumLength;
        #endregion

        Span<byte> messageData = new byte[maxLength];
        int currentPosition = offset;

        Dictionary<DomainName, ushort> domainNames = new Dictionary<DomainName, ushort>();

        EncodeUShort(messageData, ref currentPosition, TransactionID);
        EncodeUShort(messageData, ref currentPosition, Flags);
        EncodeUShort(messageData, ref currentPosition, (ushort)Questions.Count);
        EncodeUShort(messageData, ref currentPosition, (ushort)AnswerRecords.Count);
        EncodeUShort(messageData, ref currentPosition, (ushort)AuthorityRecords.Count);
        EncodeUShort(messageData, ref currentPosition, (ushort)_additionalRecords.Count);

        foreach (DnsQuestion question in Questions)
        {
            question.Encode(messageData, offset, ref currentPosition, domainNames);
        }
        foreach (DnsRecordBase record in AnswerRecords)
        {
            record.Encode(messageData, offset, ref currentPosition, domainNames);
        }
        foreach (DnsRecordBase record in AuthorityRecords)
        {
            record.Encode(messageData, offset, ref currentPosition, domainNames);
        }
        foreach (DnsRecordBase record in _additionalRecords)
        {
            record.Encode(messageData, offset, ref currentPosition, domainNames);
        }

        if (TSigOptions == null)
        {
            newTSigMac = null;
        }
        else
        {
            if (!IsQuery)
            {
                EncodeUShort(messageData, messageOffset, (ushort)originalTsigMac.Length);
                Util.BlockCopy(originalTsigMac, 0, messageData, messageOffset + 2, originalTsigMac.Length);
            }

            EncodeUShort(messageData, offset, TSigOptions.OriginalID);

            int tsigVariablesPosition = currentPosition;

            if (isSubSequentResponse)
            {
                TSigRecord.EncodeDateTime(messageData, ref tsigVariablesPosition, TSigOptions.TimeSigned);
                EncodeUShort(messageData, ref tsigVariablesPosition, (ushort)TSigOptions.Fudge.TotalSeconds);
            }
            else
            {
                EncodeDomainName(messageData, offset, ref tsigVariablesPosition, TSigOptions.Name, null, false);
                EncodeUShort(messageData, ref tsigVariablesPosition, (ushort)TSigOptions.RecordClass);
                EncodeInt(messageData, ref tsigVariablesPosition, (ushort)TSigOptions.TimeToLive);
                EncodeDomainName(messageData, offset, ref tsigVariablesPosition, TSigAlgorithmHelper.GetDomainName(TSigOptions.Algorithm), null, false);
                TSigRecord.EncodeDateTime(messageData, ref tsigVariablesPosition, TSigOptions.TimeSigned);
                EncodeUShort(messageData, ref tsigVariablesPosition, (ushort)TSigOptions.Fudge.TotalSeconds);
                EncodeUShort(messageData, ref tsigVariablesPosition, (ushort)TSigOptions.Error);
                EncodeUShort(messageData, ref tsigVariablesPosition, (ushort)TSigOptions.OtherData.Length);
                EncodeByteArray(messageData, ref tsigVariablesPosition, TSigOptions.OtherData);
            }

            KeyedHashAlgorithm hashAlgorithm = TSigAlgorithmHelper.GetHashAlgorithm(TSigOptions.Algorithm);
            if ((hashAlgorithm != null) && (TSigOptions.KeyData != null) && (TSigOptions.KeyData.Length > 0))
            {
                hashAlgorithm.Key = TSigOptions.KeyData;
                newTSigMac = hashAlgorithm.ComputeHash(messageData.ToArray(), messageOffset, tsigVariablesPosition);
            }
            else
            {
                newTSigMac = new byte[] { };
            }

            EncodeUShort(messageData, offset, TransactionID);
            EncodeUShort(messageData, offset + 10, (ushort)(_additionalRecords.Count + 1));

            TSigOptions.Encode(messageData, offset, ref currentPosition, domainNames, newTSigMac);

            if (!IsQuery)
            {
                Util.BlockCopy(messageData, offset, messageData, messageOffset, (currentPosition - offset));
                currentPosition -= (2 + originalTsigMac.Length);
            }
        }

        if (addLengthPrefix)
        {
            Util.BlockCopy(messageData, 0, messageData, 2, currentPosition);
            EncodeUShort(messageData, 0, (ushort)(currentPosition));
            currentPosition += 2;
        }

        return messageData.Slice(0, currentPosition);
    }

    [MethodImpl(Inline)]
    internal static void EncodeUShort(Span<byte> buffer, int currentPosition, ushort value)
    {
        EncodeUShort(buffer, ref currentPosition, value);
    }

    [MethodImpl(Inline)]
    internal static void EncodeUShort(Span<byte> buffer, ref int currentPosition, ushort value)
    {
        if (BitConverter.IsLittleEndian)
        {
            buffer[currentPosition++] = (byte)((value >> 8) & 0xff);
            buffer[currentPosition++] = (byte)(value & 0xff);
        }
        else
        {
            buffer[currentPosition++] = (byte)(value & 0xff);
            buffer[currentPosition++] = (byte)((value >> 8) & 0xff);
        }
    }

    [MethodImpl(Inline)]
    internal static void EncodeInt(Span<byte> buffer, ref int currentPosition, int value)
    {
        if (BitConverter.IsLittleEndian)
        {
            buffer[currentPosition++] = (byte)((value >> 24) & 0xff);
            buffer[currentPosition++] = (byte)((value >> 16) & 0xff);
            buffer[currentPosition++] = (byte)((value >> 8) & 0xff);
            buffer[currentPosition++] = (byte)(value & 0xff);
        }
        else
        {
            buffer[currentPosition++] = (byte)(value & 0xff);
            buffer[currentPosition++] = (byte)((value >> 8) & 0xff);
            buffer[currentPosition++] = (byte)((value >> 16) & 0xff);
            buffer[currentPosition++] = (byte)((value >> 24) & 0xff);
        }
    }

    [MethodImpl(Inline)]
    internal static void EncodeUInt(Span<byte> buffer, ref int currentPosition, uint value)
    {
        if (BitConverter.IsLittleEndian)
        {
            buffer[currentPosition++] = (byte)((value >> 24) & 0xff);
            buffer[currentPosition++] = (byte)((value >> 16) & 0xff);
            buffer[currentPosition++] = (byte)((value >> 8) & 0xff);
            buffer[currentPosition++] = (byte)(value & 0xff);
        }
        else
        {
            buffer[currentPosition++] = (byte)(value & 0xff);
            buffer[currentPosition++] = (byte)((value >> 8) & 0xff);
            buffer[currentPosition++] = (byte)((value >> 16) & 0xff);
            buffer[currentPosition++] = (byte)((value >> 24) & 0xff);
        }
    }

    [MethodImpl(Inline)]
    internal static void EncodeULong(Span<byte> buffer, ref int currentPosition, ulong value)
    {
        if (BitConverter.IsLittleEndian)
        {
            EncodeUInt(buffer, ref currentPosition, (uint)((value >> 32) & 0xffffffff));
            EncodeUInt(buffer, ref currentPosition, (uint)(value & 0xffffffff));
        }
        else
        {
            EncodeUInt(buffer, ref currentPosition, (uint)(value & 0xffffffff));
            EncodeUInt(buffer, ref currentPosition, (uint)((value >> 32) & 0xffffffff));
        }
    }

    internal static void EncodeDomainName(Span<byte> messageData, int offset, ref int currentPosition, DomainName name, Dictionary<DomainName, ushort> domainNames, bool useCanonical)
    {
        if (name.LabelCount == 0)
        {
            messageData[currentPosition++] = 0;
            return;
        }

        bool isCompressionAllowed = !useCanonical & (domainNames != null);

        ushort pointer;
        if (isCompressionAllowed && domainNames.TryGetValue(name, out pointer))
        {
            EncodeUShort(messageData, ref currentPosition, pointer);
            return;
        }

        string label = name.Labels.Span[0];

        if (isCompressionAllowed)
            domainNames[name] = (ushort)((currentPosition | 0xc000) - offset);

        messageData[currentPosition++] = (byte)label.Length;

        if (useCanonical)
            label = label.ToLowerInvariant();

        EncodeByteArray(messageData, ref currentPosition, Encoding.ASCII.GetBytes(label));

        EncodeDomainName(messageData, offset, ref currentPosition, name.GetParentName(), domainNames, useCanonical);
    }

    internal static void EncodeTextBlock(Span<byte> messageData, ref int currentPosition, string text)
    {
        byte[] textData = Encoding.ASCII.GetBytes(text);

        for (int i = 0; i < textData.Length; i += 255)
        {
            int blockLength = Math.Min(255, (textData.Length - i));
            messageData[currentPosition++] = (byte)blockLength;

            Util.BlockCopy(textData, i, messageData, currentPosition, blockLength);
            currentPosition += blockLength;
        }
    }

    internal static void EncodeTextWithoutLength(Span<byte> messageData, ref int currentPosition, string text)
    {
        byte[] textData = Encoding.ASCII.GetBytes(text);
        Util.BlockCopy(textData, 0, messageData, currentPosition, textData.Length);
        currentPosition += textData.Length;
    }

    internal static void EncodeByteArray(Span<byte> messageData, ref int currentPosition, byte[] data)
    {
        if (data != null)
        {
            EncodeByteArray(messageData, ref currentPosition, data, data.Length);
        }
    }

    internal static void EncodeByteArray(Span<byte> messageData, ref int currentPosition, byte[] data, int length)
    {
        if ((data != null) && (length > 0))
        {
            Util.BlockCopy(data, 0, messageData, currentPosition, length);
            currentPosition += length;
        }
    }
    #endregion
}

/// <summary>
///   Base class for a dns name identity
/// </summary>
public abstract class DnsMessageEntryBase : IEquatable<DnsMessageEntryBase>
{
    /// <summary>
    ///   Domain name
    /// </summary>
    public DomainName Name { get; set; }

    /// <summary>
    ///   Type of the record
    /// </summary>
    public RecordType RecordType { get; set; }

    /// <summary>
    ///   Class of the record
    /// </summary>
    public RecordClass RecordClass { get; set; }

    internal abstract int MaximumLength { get; }

    /// <summary>
    ///   Returns the textual representation
    /// </summary>
    /// <returns> Textual representation </returns>
    public override string ToString()
    {
        return Name + " " + RecordType + " " + RecordClass;
    }

    private int? _hashCode;

    [SuppressMessage("ReSharper", "NonReadonlyMemberInGetHashCode")]
    public override int GetHashCode()
    {
        if (!_hashCode.HasValue)
        {
            _hashCode = ToString().GetHashCode();
        }

        return _hashCode.Value;
    }

    public override bool Equals(object obj)
    {
        return Equals(obj as DnsMessageEntryBase);
    }

    public bool Equals(DnsMessageEntryBase other)
    {
        if (other == null)
            return false;

        return Name.Equals(other.Name)
               && RecordType.Equals(other.RecordType)
               && RecordClass.Equals(other.RecordClass);
    }
}

/// <summary>
///   Provides options to be used in <see cref="DnsClient">DNS client</see> for resolving queries
/// </summary>
public class DnsQueryOptions
{
    /// <summary>
    ///   <para>Gets or sets the recursion desired (RD) flag</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc1035">RFC 1035</see>
    ///   </para>
    /// </summary>
    public bool IsRecursionDesired { get; set; }

    /// <summary>
    ///   <para>Gets or sets the checking disabled (CD) flag</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc4035">RFC 4035</see>
    ///   </para>
    /// </summary>
    public bool IsCheckingDisabled { get; set; }

    /// <summary>
    ///   Enables or disables EDNS
    /// </summary>
    public bool IsEDnsEnabled
    {
        get { return (EDnsOptions != null); }
        set
        {
            if (value && (EDnsOptions == null))
            {
                EDnsOptions = new OptRecord();
            }
            else if (!value)
            {
                EDnsOptions = null;
            }
        }
    }

    /// <summary>
    ///   Gets or set the OptRecord for the EDNS options
    /// </summary>
    public OptRecord EDnsOptions { get; set; }

    /// <summary>
    ///   <para>Gets or sets the DNSSEC answer OK (DO) flag</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc4035">RFC 4035</see>
    ///     and
    ///     <see cref="!:http://tools.ietf.org/html/rfc3225">RFC 3225</see>
    ///   </para>
    /// </summary>
    public bool IsDnsSecOk
    {
        get
        {
            OptRecord ednsOptions = EDnsOptions;
            return (ednsOptions != null) && ednsOptions.IsDnsSecOk;
        }
        set
        {
            OptRecord ednsOptions = EDnsOptions;
            if (ednsOptions == null)
            {
                if (value)
                {
                    throw new ArgumentOutOfRangeException(nameof(value), "Setting DO flag is allowed in edns messages only");
                }
            }
            else
            {
                ednsOptions.IsDnsSecOk = value;
            }
        }
    }
}

/// <summary>
///   A single entry of the Question section of a dns query
/// </summary>
public class DnsQuestion : DnsMessageEntryBase, IEquatable<DnsQuestion>
{
    /// <summary>
    ///   Creates a new instance of the DnsQuestion class
    /// </summary>
    /// <param name="name"> Domain name </param>
    /// <param name="recordType"> Record type </param>
    /// <param name="recordClass"> Record class </param>
    public DnsQuestion(DomainName name, RecordType recordType, RecordClass recordClass)
    {
        if (name == null)
            throw new ArgumentNullException(nameof(name));

        Name = name;
        RecordType = recordType;
        RecordClass = recordClass;
    }

    internal DnsQuestion() { }

    internal override int MaximumLength => Name.MaximumRecordDataLength + 6;

    internal void Encode(Span<byte> messageData, int offset, ref int currentPosition, Dictionary<DomainName, ushort> domainNames)
    {
        DnsMessageBase.EncodeDomainName(messageData, offset, ref currentPosition, Name, domainNames, false);
        DnsMessageBase.EncodeUShort(messageData, ref currentPosition, (ushort)RecordType);
        DnsMessageBase.EncodeUShort(messageData, ref currentPosition, (ushort)RecordClass);
    }

    private int? _hashCode;

    [SuppressMessage("ReSharper", "NonReadonlyMemberInGetHashCode")]
    public override int GetHashCode()
    {
        if (!_hashCode.HasValue)
        {
            _hashCode = ToString().GetHashCode();
        }

        return _hashCode.Value;
    }

    public override bool Equals(object obj)
    {
        return Equals(obj as DnsQuestion);
    }

    public bool Equals(DnsQuestion other)
    {
        if (other == null)
            return false;

        return base.Equals(other);
    }
}

/// <summary>
///   Provides a base dns server interface
/// </summary>
public class DnsServer : IDisposable
{
    /// <summary>
    ///   Represents the method, that will be called to get the keydata for processing a tsig signed message
    /// </summary>
    /// <param name="algorithm"> The algorithm which is used in the message </param>
    /// <param name="keyName"> The keyname which is used in the message </param>
    /// <returns> Binary representation of the key </returns>
    public delegate byte[] SelectTsigKey(TSigAlgorithm algorithm, DomainName keyName);

    private const int _DNS_PORT = 53;

    private readonly object _listenerLock = new object();
    private TcpListener _tcpListener;
    private UdpClient _udpListener;
    private readonly IPEndPoint _bindEndPoint;

    private readonly int _udpListenerCount;
    private readonly int _tcpListenerCount;

    private int _availableUdpListener;
    private bool _hasActiveUdpListener;

    private int _availableTcpListener;
    private bool _hasActiveTcpListener;

    /// <summary>
    ///   Method that will be called to get the keydata for processing a tsig signed message
    /// </summary>
    public SelectTsigKey TsigKeySelector;

    /// <summary>
    ///   Gets or sets the timeout for sending and receiving data
    /// </summary>
    public int Timeout { get; set; }

    /// <summary>
    ///   Creates a new dns server instance which will listen on all available interfaces
    /// </summary>
    /// <param name="udpListenerCount"> The count of threads listings on udp, 0 to deactivate udp </param>
    /// <param name="tcpListenerCount"> The count of threads listings on tcp, 0 to deactivate tcp </param>
    public DnsServer(int udpListenerCount, int tcpListenerCount)
        : this(IPAddress.Any, udpListenerCount, tcpListenerCount) { }

    /// <summary>
    ///   Creates a new dns server instance
    /// </summary>
    /// <param name="bindAddress"> The address, on which should be listend </param>
    /// <param name="udpListenerCount"> The count of threads listings on udp, 0 to deactivate udp </param>
    /// <param name="tcpListenerCount"> The count of threads listings on tcp, 0 to deactivate tcp </param>
    public DnsServer(IPAddress bindAddress, int udpListenerCount, int tcpListenerCount)
        : this(new IPEndPoint(bindAddress, _DNS_PORT), udpListenerCount, tcpListenerCount) { }

    /// <summary>
    ///   Creates a new dns server instance
    /// </summary>
    /// <param name="bindEndPoint"> The endpoint, on which should be listend </param>
    /// <param name="udpListenerCount"> The count of threads listings on udp, 0 to deactivate udp </param>
    /// <param name="tcpListenerCount"> The count of threads listings on tcp, 0 to deactivate tcp </param>
    public DnsServer(IPEndPoint bindEndPoint, int udpListenerCount, int tcpListenerCount)
    {
        _bindEndPoint = bindEndPoint;

        _udpListenerCount = udpListenerCount;
        _tcpListenerCount = tcpListenerCount;

        Timeout = 120000;
    }

    /// <summary>
    ///   Starts the server
    /// </summary>
    public void Start()
    {
        if (_udpListenerCount > 0)
        {
            lock (_listenerLock)
            {
                _availableUdpListener = _udpListenerCount;
            }
            _udpListener = new UdpClient(_bindEndPoint);
            StartUdpListenerTask();
        }

        if (_tcpListenerCount > 0)
        {
            lock (_listenerLock)
            {
                _availableTcpListener = _tcpListenerCount;
            }
            _tcpListener = new TcpListener(_bindEndPoint);
            _tcpListener.Start();
            StartTcpListenerTask();
        }
    }

    /// <summary>
    ///   Stops the server
    /// </summary>
    public void Stop()
    {
        if (_udpListenerCount > 0)
        {
            _udpListener.Close();
        }
        if (_tcpListenerCount > 0)
        {
            _tcpListener.Stop();
        }
    }

    private async Task<DnsMessageBase> ProcessMessageAsync(DnsMessageBase query, ProtocolType protocolType, IPEndPoint remoteEndpoint)
    {
        if (query.TSigOptions != null)
        {
            switch (query.TSigOptions.ValidationResult)
            {
                case ReturnCode.BadKey:
                case ReturnCode.BadSig:
                    query.IsQuery = false;
                    query.ReturnCode = ReturnCode.NotAuthoritive;
                    query.TSigOptions.Error = query.TSigOptions.ValidationResult;
                    query.TSigOptions.KeyData = null;

#pragma warning disable 4014
                    InvalidSignedMessageReceived.RaiseAsync(this, new InvalidSignedMessageEventArgs(query, protocolType, remoteEndpoint));
#pragma warning restore 4014

                    return query;

                case ReturnCode.BadTime:
                    query.IsQuery = false;
                    query.ReturnCode = ReturnCode.NotAuthoritive;
                    query.TSigOptions.Error = query.TSigOptions.ValidationResult;
                    query.TSigOptions.OtherData = new byte[6];
                    int tmp = 0;
                    TSigRecord.EncodeDateTime(query.TSigOptions.OtherData, ref tmp, DateTime.Now);

#pragma warning disable 4014
                    InvalidSignedMessageReceived.RaiseAsync(this, new InvalidSignedMessageEventArgs(query, protocolType, remoteEndpoint));
#pragma warning restore 4014

                    return query;
            }
        }

        QueryReceivedEventArgs eventArgs = new QueryReceivedEventArgs(query, protocolType, remoteEndpoint);
        await QueryReceived.RaiseAsync(this, eventArgs);
        return eventArgs.Response;
    }

    private void StartUdpListenerTask()
    {
        lock (_listenerLock)
        {
            if ((_udpListener.Client == null) || !_udpListener.Client.IsBound) // server is stopped
                return;

            if ((_availableUdpListener > 0) && !_hasActiveUdpListener)
            {
                _availableUdpListener--;
                _hasActiveUdpListener = true;
                HandleUdpListenerAsync();
            }
        }
    }

    private async void HandleUdpListenerAsync()
    {
        try
        {
            UdpReceiveResult receiveResult;
            try
            {
                receiveResult = await _udpListener.ReceiveAsync();
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            finally
            {
                lock (_listenerLock)
                {
                    _hasActiveUdpListener = false;
                }
            }

            ClientConnectedEventArgs clientConnectedEventArgs = new ClientConnectedEventArgs(ProtocolType.Udp, receiveResult.RemoteEndPoint);
            await ClientConnected.RaiseAsync(this, clientConnectedEventArgs);

            if (clientConnectedEventArgs.RefuseConnect)
                return;

            StartUdpListenerTask();

            byte[] buffer = receiveResult.Buffer;

            DnsMessageBase query;
            byte[] originalMac;
            try
            {
                query = DnsMessageBase.CreateByFlag(buffer, TsigKeySelector, null);
                originalMac = query.TSigOptions?.Mac;
            }
            catch (Exception e)
            {
                throw new Exception("Error parsing dns query", e);
            }

            DnsMessageBase response;
            try
            {
                response = await ProcessMessageAsync(query, ProtocolType.Udp, receiveResult.RemoteEndPoint);
            }
            catch (Exception ex)
            {
                OnExceptionThrownAsync(ex);
                response = null;
            }

            if (response == null)
            {
                response = query;
                query.IsQuery = false;
                query.ReturnCode = ReturnCode.ServerFailure;
            }

            int length = response.Encode(false, originalMac, out buffer);

            #region Truncating
            DnsMessage message = response as DnsMessage;

            if (message != null)
            {
                int maxLength = 512;
                if (query.IsEDnsEnabled && message.IsEDnsEnabled)
                {
                    maxLength = Math.Max(512, (int)message.EDnsOptions.UdpPayloadSize);
                }

                while (length > maxLength)
                {
                    // First step: remove data from additional records except the opt record
                    if ((message.IsEDnsEnabled && (message.AdditionalRecords.Count > 1)) || (!message.IsEDnsEnabled && (message.AdditionalRecords.Count > 0)))
                    {
                        for (int i = message.AdditionalRecords.Count - 1; i >= 0; i--)
                        {
                            if (message.AdditionalRecords[i].RecordType != RecordType.Opt)
                            {
                                message.AdditionalRecords.RemoveAt(i);
                            }
                        }

                        length = message.Encode(false, originalMac, out buffer);
                        continue;
                    }

                    int savedLength = 0;
                    if (message.AuthorityRecords.Count > 0)
                    {
                        for (int i = message.AuthorityRecords.Count - 1; i >= 0; i--)
                        {
                            savedLength += message.AuthorityRecords[i].MaximumLength;
                            message.AuthorityRecords.RemoveAt(i);

                            if ((length - savedLength) < maxLength)
                            {
                                break;
                            }
                        }

                        message.IsTruncated = true;

                        length = message.Encode(false, originalMac, out buffer);
                        continue;
                    }

                    if (message.AnswerRecords.Count > 0)
                    {
                        for (int i = message.AnswerRecords.Count - 1; i >= 0; i--)
                        {
                            savedLength += message.AnswerRecords[i].MaximumLength;
                            message.AnswerRecords.RemoveAt(i);

                            if ((length - savedLength) < maxLength)
                            {
                                break;
                            }
                        }

                        message.IsTruncated = true;

                        length = message.Encode(false, originalMac, out buffer);
                        continue;
                    }

                    if (message.Questions.Count > 0)
                    {
                        for (int i = message.Questions.Count - 1; i >= 0; i--)
                        {
                            savedLength += message.Questions[i].MaximumLength;
                            message.Questions.RemoveAt(i);

                            if ((length - savedLength) < maxLength)
                            {
                                break;
                            }
                        }

                        message.IsTruncated = true;

                        length = message.Encode(false, originalMac, out buffer);
                    }
                }
            }
            #endregion

            await _udpListener.SendAsync(buffer, length, receiveResult.RemoteEndPoint);
        }
        catch (Exception ex)
        {
            OnExceptionThrownAsync(ex);
        }
        finally
        {
            lock (_listenerLock)
            {
                _availableUdpListener++;
            }
            StartUdpListenerTask();
        }
    }

    private void StartTcpListenerTask()
    {
        lock (_listenerLock)
        {
            if ((_tcpListener.Server == null) || !_tcpListener.Server.IsBound) // server is stopped
                return;

            if ((_availableTcpListener > 0) && !_hasActiveTcpListener)
            {
                _availableTcpListener--;
                _hasActiveTcpListener = true;
                HandleTcpListenerAsync();
            }
        }
    }

    private async void HandleTcpListenerAsync()
    {
        TcpClient client = null;

        try
        {
            try
            {
                client = await _tcpListener.AcceptTcpClientAsync();

                ClientConnectedEventArgs clientConnectedEventArgs = new ClientConnectedEventArgs(ProtocolType.Tcp, (IPEndPoint)client.Client.RemoteEndPoint);
                await ClientConnected.RaiseAsync(this, clientConnectedEventArgs);

                if (clientConnectedEventArgs.RefuseConnect)
                    return;
            }
            finally
            {
                lock (_listenerLock)
                {
                    _hasActiveTcpListener = false;
                }
            }

            StartTcpListenerTask();

            using (NetworkStream stream = client.GetStream())
            {
                while (true)
                {
                    byte[] buffer = await ReadIntoBufferAsync(client, stream, 2);
                    if (buffer == null) // client disconneted while reading or timeout
                        break;

                    int offset = 0;
                    int length = DnsMessageBase.ParseUShort(buffer, ref offset);

                    buffer = await ReadIntoBufferAsync(client, stream, length);
                    if (buffer == null) // client disconneted while reading or timeout
                    {
                        throw new Exception("Client disconnted or timed out while sending data");
                    }

                    DnsMessageBase query;
                    byte[] tsigMac;
                    try
                    {
                        query = DnsMessageBase.CreateByFlag(buffer, TsigKeySelector, null);
                        tsigMac = query.TSigOptions?.Mac;
                    }
                    catch (Exception e)
                    {
                        throw new Exception("Error parsing dns query", e);
                    }

                    DnsMessageBase response;
                    try
                    {
                        response = await ProcessMessageAsync(query, ProtocolType.Tcp, (IPEndPoint)client.Client.RemoteEndPoint);
                    }
                    catch (Exception ex)
                    {
                        OnExceptionThrownAsync(ex);

                        response = DnsMessageBase.CreateByFlag(buffer, TsigKeySelector, null);
                        response.IsQuery = false;
                        response.AdditionalRecords.Clear();
                        response.AuthorityRecords.Clear();
                        response.ReturnCode = ReturnCode.ServerFailure;
                    }

                    byte[] newTsigMac;

                    length = response.Encode(true, tsigMac, false, out buffer, out newTsigMac);

                    if (length <= 65535)
                    {
                        await stream.WriteAsync(buffer, 0, length);
                    }
                    else
                    {
                        if ((response.Questions.Count == 0) || (response.Questions[0].RecordType != RecordType.Axfr))
                        {
                            OnExceptionThrownAsync(new ArgumentException("The length of the serialized response is greater than 65,535 bytes"));

                            response = DnsMessageBase.CreateByFlag(buffer, TsigKeySelector, null);
                            response.IsQuery = false;
                            response.AdditionalRecords.Clear();
                            response.AuthorityRecords.Clear();
                            response.ReturnCode = ReturnCode.ServerFailure;

                            length = response.Encode(true, tsigMac, false, out buffer, out newTsigMac);
                            await stream.WriteAsync(buffer, 0, length);
                        }
                        else
                        {
                            bool isSubSequentResponse = false;

                            while (true)
                            {
                                List<DnsRecordBase> nextPacketRecords = new List<DnsRecordBase>();

                                while (length > 65535)
                                {
                                    int lastIndex = Math.Min(500, response.AnswerRecords.Count / 2);
                                    int removeCount = response.AnswerRecords.Count - lastIndex;

                                    nextPacketRecords.InsertRange(0, response.AnswerRecords.GetRange(lastIndex, removeCount));
                                    response.AnswerRecords.RemoveRange(lastIndex, removeCount);

                                    length = response.Encode(true, tsigMac, isSubSequentResponse, out buffer, out newTsigMac);
                                }

                                await stream.WriteAsync(buffer, 0, length);

                                if (nextPacketRecords.Count == 0)
                                    break;

                                isSubSequentResponse = true;
                                tsigMac = newTsigMac;
                                response.AnswerRecords = nextPacketRecords;
                                length = response.Encode(true, tsigMac, true, out buffer, out newTsigMac);
                            }
                        }
                    }

                    // Since support for multiple tsig signed messages is not finished, just close connection after response to first signed query
                    if (newTsigMac != null)
                        break;
                }
            }
        }
        catch (Exception ex)
        {
            OnExceptionThrownAsync(ex);
        }
        finally
        {
            try
            {
                // ReSharper disable once ConstantConditionalAccessQualifier
                client?.Close();
            }
            catch
            {
                // ignored
            }

            lock (_listenerLock)
            {
                _availableTcpListener++;
            }
            StartTcpListenerTask();
        }
    }

    private async Task<byte[]> ReadIntoBufferAsync(TcpClient client, NetworkStream stream, int count)
    {
        CancellationToken token = new CancellationTokenSource(Timeout).Token;

        byte[] buffer = new byte[count];

        if (await TryReadAsync(client, stream, buffer, count, token))
            return buffer;

        return null;
    }

    private async Task<bool> TryReadAsync(TcpClient client, NetworkStream stream, byte[] buffer, int length, CancellationToken token)
    {
        int readBytes = 0;

        while (readBytes < length)
        {
            if (token.IsCancellationRequested || !client.IsConnected())
                return false;

            readBytes += await stream.ReadAsync(buffer, readBytes, length - readBytes, token);
        }

        return true;
    }

    private void OnExceptionThrownAsync(Exception e)
    {
        if (e is ObjectDisposedException)
            return;

        Trace.TraceError("Exception in DnsServer: " + e);
        ExceptionThrown.RaiseAsync(this, new ExceptionEventArgs(e));
    }

    /// <summary>
    ///   This event is fired on exceptions of the listeners. You can use it for custom logging.
    /// </summary>
    public event AsyncEventHandler<ExceptionEventArgs> ExceptionThrown;

    /// <summary>
    ///   This event is fired whenever a message is received, that is not correct signed
    /// </summary>
    public event AsyncEventHandler<InvalidSignedMessageEventArgs> InvalidSignedMessageReceived;

    /// <summary>
    ///   This event is fired whenever a client connects to the server
    /// </summary>
    public event AsyncEventHandler<ClientConnectedEventArgs> ClientConnected;

    /// <summary>
    ///   This event is fired whenever a query is received by the server
    /// </summary>
    public event AsyncEventHandler<QueryReceivedEventArgs> QueryReceived;

    void IDisposable.Dispose()
    {
        Stop();
    }
}

/// <summary>
///   Event arguments of <see cref="DnsServer.ExceptionThrown" /> event.
/// </summary>
public class ExceptionEventArgs : EventArgs
{
    /// <summary>
    ///   Exception which was thrown originally
    /// </summary>
    public Exception Exception { get; set; }

    internal ExceptionEventArgs(Exception exception)
    {
        Exception = exception;
    }
}

/// <summary>
///   Event arguments of <see cref="DnsServer.InvalidSignedMessageReceived" /> event.
/// </summary>
public class InvalidSignedMessageEventArgs : EventArgs
{
    /// <summary>
    ///   Original message, which the client provided
    /// </summary>
    public DnsMessageBase Query { get; private set; }

    /// <summary>
    ///   Protocol used by the client
    /// </summary>
    public ProtocolType ProtocolType { get; private set; }

    /// <summary>
    ///   Remote endpoint of the client
    /// </summary>
    public IPEndPoint RemoteEndpoint { get; private set; }

    internal InvalidSignedMessageEventArgs(DnsMessageBase query, ProtocolType protocolType, IPEndPoint remoteEndpoint)
    {
        Query = query;
        ProtocolType = protocolType;
        RemoteEndpoint = remoteEndpoint;
    }
}

/// <summary>
///   Provides a client for querying LLMNR (link-local multicast name resolution) as defined in
///   <see
///     cref="!:http://tools.ietf.org/html/rfc4795">
///     RFC 4795
///   </see>
///   .
/// </summary>
public sealed class LlmnrClient : DnsClientBase
{
    private static readonly List<IPAddress> _addresses = new List<IPAddress> { IPAddress.Parse("FF02::1:3"), IPAddress.Parse("224.0.0.252") };

    /// <summary>
    ///   Provides a new instance with a timeout of 1 second
    /// </summary>
    public LlmnrClient()
        : this(1000) { }

    /// <summary>
    ///   Provides a new instance with a custom timeout
    /// </summary>
    /// <param name="queryTimeout"> Query timeout in milliseconds </param>
    public LlmnrClient(int queryTimeout)
        : base(_addresses, queryTimeout, 5355)
    {
        int maximumMessageSize = 0;

        try
        {
            maximumMessageSize = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.SupportsMulticast && (n.NetworkInterfaceType != NetworkInterfaceType.Loopback) && (n.OperationalStatus == OperationalStatus.Up) && (n.Supports(NetworkInterfaceComponent.IPv4)))
                .Select(n => n.GetIPProperties())
                .Min(p => Math.Min(p.GetIPv4Properties().Mtu, p.GetIPv6Properties().Mtu));
        }
        catch
        {
            // ignored
        }

        MaximumQueryMessageSize = Math.Max(512, maximumMessageSize);

        IsUdpEnabled = true;
        IsTcpEnabled = false;
    }

    protected override int MaximumQueryMessageSize { get; }

    /// <summary>
    ///   Queries for specified records.
    /// </summary>
    /// <param name="name"> Name, that should be queried </param>
    /// <param name="recordType"> Type the should be queried </param>
    /// <returns> All available responses on the local network </returns>
    public List<LlmnrMessage> Resolve(DomainName name, RecordType recordType = RecordType.A)
    {
        if (name == null)
            throw new ArgumentNullException(nameof(name), "Name must be provided");

        LlmnrMessage message = new LlmnrMessage { IsQuery = true, OperationCode = OperationCode.Query };
        message.Questions.Add(new DnsQuestion(name, recordType, RecordClass.INet));

        return SendMessageParallel(message);
    }

    /// <summary>
    ///   Queries for specified records as an asynchronous operation.
    /// </summary>
    /// <param name="name"> Name, that should be queried </param>
    /// <param name="recordType"> Type the should be queried </param>
    /// <param name="token"> The token to monitor cancellation requests </param>
    /// <returns> All available responses on the local network </returns>
    public Task<List<LlmnrMessage>> ResolveAsync(DomainName name, RecordType recordType = RecordType.A, CancellationToken token = default(CancellationToken))
    {
        if (name == null)
            throw new ArgumentNullException(nameof(name), "Name must be provided");

        LlmnrMessage message = new LlmnrMessage { IsQuery = true, OperationCode = OperationCode.Query };
        message.Questions.Add(new DnsQuestion(name, recordType, RecordClass.INet));

        return SendMessageParallelAsync(message, token);
    }
}

/// <summary>
///   Message returned as result to a LLMNR query
/// </summary>
public class LlmnrMessage : DnsMessageBase
{
    /// <summary>
    ///   Parses a the contents of a byte array as LlmnrMessage
    /// </summary>
    /// <param name="data">Buffer, that contains the message data</param>
    /// <returns>A new instance of the LlmnrMessage class</returns>
    public static LlmnrMessage Parse(byte[] data)
    {
        return Parse<LlmnrMessage>(data);
    }

    #region Header
    /// <summary>
    ///   <para>Gets or sets the conflict (C) flag</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc4795">RFC 4795</see>
    ///   </para>
    /// </summary>
    public bool IsConflict
    {
        get { return (Flags & 0x0400) != 0; }
        set
        {
            if (value)
            {
                Flags |= 0x0400;
            }
            else
            {
                Flags &= 0xfbff;
            }
        }
    }

    /// <summary>
    ///   <para>Gets or sets the truncated response (TC) flag</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc4795">RFC 4795</see>
    ///   </para>
    /// </summary>
    public bool IsTruncated
    {
        get { return (Flags & 0x0200) != 0; }
        set
        {
            if (value)
            {
                Flags |= 0x0200;
            }
            else
            {
                Flags &= 0xfdff;
            }
        }
    }

    /// <summary>
    ///   <para>Gets or sets the tentive (T) flag</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc4795">RFC 4795</see>
    ///   </para>
    /// </summary>
    public bool IsTentive
    {
        get { return (Flags & 0x0100) != 0; }
        set
        {
            if (value)
            {
                Flags |= 0x0100;
            }
            else
            {
                Flags &= 0xfeff;
            }
        }
    }
    #endregion

    /// <summary>
    ///   Gets or sets the entries in the question section
    /// </summary>
    public new List<DnsQuestion> Questions
    {
        get { return base.Questions; }
        set { base.Questions = (value ?? new List<DnsQuestion>()); }
    }

    /// <summary>
    ///   Gets or sets the entries in the answer records section
    /// </summary>
    public new List<DnsRecordBase> AnswerRecords
    {
        get { return base.AnswerRecords; }
        set { base.AnswerRecords = (value ?? new List<DnsRecordBase>()); }
    }

    /// <summary>
    ///   Gets or sets the entries in the authority records section
    /// </summary>
    public new List<DnsRecordBase> AuthorityRecords
    {
        get { return base.AuthorityRecords; }
        set { base.AuthorityRecords = (value ?? new List<DnsRecordBase>()); }
    }

    internal override bool IsTcpUsingRequested => (Questions.Count > 0) && ((Questions[0].RecordType == RecordType.Axfr) || (Questions[0].RecordType == RecordType.Ixfr));

    internal override bool IsTcpResendingRequested => IsTruncated;

    internal override bool IsTcpNextMessageWaiting(bool isSubsequentResponseMessage)
    {
        return false;
    }
}

/// <summary>
///   Message returned as result to a dns query
/// </summary>
public class MulticastDnsMessage : DnsMessageBase
{
    /// <summary>
    ///   Parses a the contents of a byte array as MulticastDnsMessage
    /// </summary>
    /// <param name="data">Buffer, that contains the message data</param>
    /// <returns>A new instance of the MulticastDnsMessage class</returns>
    public static MulticastDnsMessage Parse(byte[] data)
    {
        return Parse<MulticastDnsMessage>(data);
    }

    #region Header
    /// <summary>
    ///   <para>Gets or sets the autoritive answer (AA) flag</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc1035">RFC 1035</see>
    ///   </para>
    /// </summary>
    public bool IsAuthoritiveAnswer
    {
        get { return (Flags & 0x0400) != 0; }
        set
        {
            if (value)
            {
                Flags |= 0x0400;
            }
            else
            {
                Flags &= 0xfbff;
            }
        }
    }

    /// <summary>
    ///   <para>Gets or sets the truncated response (TC) flag</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc1035">RFC 1035</see>
    ///   </para>
    /// </summary>
    public bool IsTruncated
    {
        get { return (Flags & 0x0200) != 0; }
        set
        {
            if (value)
            {
                Flags |= 0x0200;
            }
            else
            {
                Flags &= 0xfdff;
            }
        }
    }

    /// <summary>
    ///   <para>Gets or sets the recursion desired (RD) flag</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc1035">RFC 1035</see>
    ///   </para>
    /// </summary>
    public bool IsRecursionDesired
    {
        get { return (Flags & 0x0100) != 0; }
        set
        {
            if (value)
            {
                Flags |= 0x0100;
            }
            else
            {
                Flags &= 0xfeff;
            }
        }
    }

    /// <summary>
    ///   <para>Gets or sets the recursion allowed (RA) flag</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc1035">RFC 1035</see>
    ///   </para>
    /// </summary>
    public bool IsRecursionAllowed
    {
        get { return (Flags & 0x0080) != 0; }
        set
        {
            if (value)
            {
                Flags |= 0x0080;
            }
            else
            {
                Flags &= 0xff7f;
            }
        }
    }

    /// <summary>
    ///   <para>Gets or sets the authentic data (AD) flag</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc4035">RFC 4035</see>
    ///   </para>
    /// </summary>
    public bool IsAuthenticData
    {
        get { return (Flags & 0x0020) != 0; }
        set
        {
            if (value)
            {
                Flags |= 0x0020;
            }
            else
            {
                Flags &= 0xffdf;
            }
        }
    }

    /// <summary>
    ///   <para>Gets or sets the checking disabled (CD) flag</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc4035">RFC 4035</see>
    ///   </para>
    /// </summary>
    public bool IsCheckingDisabled
    {
        get { return (Flags & 0x0010) != 0; }
        set
        {
            if (value)
            {
                Flags |= 0x0010;
            }
            else
            {
                Flags &= 0xffef;
            }
        }
    }
    #endregion

    /// <summary>
    ///   Gets or sets the entries in the question section
    /// </summary>
    public new List<DnsQuestion> Questions
    {
        get { return base.Questions; }
        set { base.Questions = (value ?? new List<DnsQuestion>()); }
    }

    /// <summary>
    ///   Gets or sets the entries in the answer records section
    /// </summary>
    public new List<DnsRecordBase> AnswerRecords
    {
        get { return base.AnswerRecords; }
        set { base.AnswerRecords = (value ?? new List<DnsRecordBase>()); }
    }

    /// <summary>
    ///   Gets or sets the entries in the authority records section
    /// </summary>
    public new List<DnsRecordBase> AuthorityRecords
    {
        get { return base.AuthorityRecords; }
        set { base.AuthorityRecords = (value ?? new List<DnsRecordBase>()); }
    }

    internal override bool IsTcpUsingRequested => (Questions.Count > 0) && ((Questions[0].RecordType == RecordType.Axfr) || (Questions[0].RecordType == RecordType.Ixfr));

    internal override bool IsTcpResendingRequested => IsTruncated;

    internal override bool IsTcpNextMessageWaiting(bool isSubsequentResponseMessage)
    {
        return false;
    }
}

/// <summary>
///   Provides a one/shot client for querying Multicast DNS as defined in
///   <see
///     cref="!:http://tools.ietf.org/html/rfc6762">
///     RFC 6762
///   </see>
///   .
/// </summary>
public sealed class MulticastDnsOneShotClient : DnsClientBase
{
    private static readonly List<IPAddress> _addresses = new List<IPAddress> { IPAddress.Parse("FF02::FB"), IPAddress.Parse("224.0.0.251") };

    /// <summary>
    ///   Provides a new instance with a timeout of 2.5 seconds
    /// </summary>
    public MulticastDnsOneShotClient()
        : this(2500) { }

    /// <summary>
    ///   Provides a new instance with a custom timeout
    /// </summary>
    /// <param name="queryTimeout"> Query timeout in milliseconds </param>
    public MulticastDnsOneShotClient(int queryTimeout)
        : base(_addresses, queryTimeout, 5353)
    {
        int maximumMessageSize = 0;

        try
        {
            maximumMessageSize = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.SupportsMulticast && (n.NetworkInterfaceType != NetworkInterfaceType.Loopback) && (n.OperationalStatus == OperationalStatus.Up) && (n.Supports(NetworkInterfaceComponent.IPv4)))
                .Select(n => n.GetIPProperties())
                .Min(p => Math.Min(p.GetIPv4Properties().Mtu, p.GetIPv6Properties().Mtu));
        }
        catch
        {
            // ignored
        }

        MaximumQueryMessageSize = Math.Max(512, maximumMessageSize);

        IsUdpEnabled = true;
        IsTcpEnabled = false;
    }

    protected override int MaximumQueryMessageSize { get; }

    /// <summary>
    ///   Queries for specified records.
    /// </summary>
    /// <param name="name"> Name, that should be queried </param>
    /// <param name="recordType"> Type the should be queried </param>
    /// <returns> All available responses on the local network </returns>
    public List<MulticastDnsMessage> Resolve(DomainName name, RecordType recordType = RecordType.Any)
    {
        if (name == null)
            throw new ArgumentNullException(nameof(name), "Name must be provided");

        MulticastDnsMessage message = new MulticastDnsMessage { IsQuery = true, OperationCode = OperationCode.Query };
        message.Questions.Add(new DnsQuestion(name, recordType, RecordClass.INet));

        return SendMessageParallel(message);
    }

    /// <summary>
    ///   Queries for specified records as an asynchronous operation.
    /// </summary>
    /// <param name="name"> Name, that should be queried </param>
    /// <param name="recordType"> Type the should be queried </param>
    /// <param name="token"> The token to monitor cancellation requests </param>
    /// <returns> All available responses on the local network </returns>
    public Task<List<MulticastDnsMessage>> ResolveAsync(DomainName name, RecordType recordType = RecordType.Any, CancellationToken token = default(CancellationToken))
    {
        if (name == null)
            throw new ArgumentNullException(nameof(name), "Name must be provided");

        MulticastDnsMessage message = new MulticastDnsMessage { IsQuery = true, OperationCode = OperationCode.Query };
        message.Questions.Add(new DnsQuestion(name, recordType, RecordClass.INet));

        return SendMessageParallelAsync(message, token);
    }
}

/// <summary>
///   Operation code of a dns query
/// </summary>
public enum OperationCode : ushort
{
    /// <summary>
    ///   <para>Normal query</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc1035">RFC 1035</see>
    ///   </para>
    /// </summary>
    Query = 0,

    /// <summary>
    ///   <para>Inverse query</para>
    ///   <para>
    ///     Obsoleted by
    ///     <see cref="!:http://tools.ietf.org/html/rfc3425">RFC 3425</see>
    ///   </para>
    /// </summary>
    [Obsolete]
    InverseQuery = 1,

    /// <summary>
    ///   <para>Server status request</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc1035">RFC 1035</see>
    ///   </para>
    /// </summary>
    Status = 2,

    /// <summary>
    ///   <para>Notify of zone change</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc1996">RFC 1996</see>
    ///   </para>
    /// </summary>
    Notify = 4,

    /// <summary>
    ///   <para>Dynamic update</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc2136">RFC 2136</see>
    ///   </para>
    /// </summary>
    Update = 5,
}

/// <summary>
///   Event arguments of <see cref="DnsServer.QueryReceived" /> event.
/// </summary>
public class QueryReceivedEventArgs : EventArgs
{
    /// <summary>
    ///   Original query, which the client provided
    /// </summary>
    public DnsMessageBase Query { get; private set; }

    /// <summary>
    ///   Protocol used by the client
    /// </summary>
    public ProtocolType ProtocolType { get; private set; }

    /// <summary>
    ///   Remote endpoint of the client
    /// </summary>
    public IPEndPoint RemoteEndpoint { get; private set; }

    /// <summary>
    ///   The response, which should be sent to the client
    /// </summary>
    public DnsMessageBase Response { get; set; }

    internal QueryReceivedEventArgs(DnsMessageBase query, ProtocolType protocolType, IPEndPoint remoteEndpoint)
    {
        Query = query;
        ProtocolType = protocolType;
        RemoteEndpoint = remoteEndpoint;
    }
}

/// <summary>
///   DNS record class
/// </summary>
public enum RecordClass : ushort
{
    /// <summary>
    ///   Invalid record class
    /// </summary>
    Invalid = 0,

    /// <summary>
    ///   <para>Record class Internet (IN)</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc1035">RFC 1035</see>
    ///   </para>
    /// </summary>
    // ReSharper disable once InconsistentNaming
    INet = 1,

    /// <summary>
    ///   <para>Record class Chaois (CH)</para>
    ///   <para>
    ///     Defined: D. Moon, "Chaosnet", A.I. Memo 628, Massachusetts Institute of Technology Artificial Intelligence
    ///     Laboratory, June 1981.
    ///   </para>
    /// </summary>
    Chaos = 3,

    /// <summary>
    ///   <para>Record class Hesiod (HS)</para>
    ///   <para>Defined: Dyer, S., and F. Hsu, "Hesiod", Project Athena Technical Plan - Name Service, April 1987.</para>
    /// </summary>
    Hesiod = 4,

    /// <summary>
    ///   <para>Record class NONE</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc2136">RFC 2136</see>
    ///   </para>
    /// </summary>
    None = 254,

    /// <summary>
    ///   <para>Record class * (ANY)</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc1035">RFC 1035</see>
    ///   </para>
    /// </summary>
    Any = 255
}

internal static class RecordClassHelper
{
    public static string ToShortString(this RecordClass recordClass)
    {
        switch (recordClass)
        {
            case RecordClass.INet:
                return "IN";
            case RecordClass.Chaos:
                return "CH";
            case RecordClass.Hesiod:
                return "HS";
            case RecordClass.None:
                return "NONE";
            case RecordClass.Any:
                return "*";
            default:
                return "CLASS" + (int)recordClass;
        }
    }

    public static bool TryParseShortString(string s, out RecordClass recordClass, bool allowAny = true)
    {
        if (String.IsNullOrEmpty(s))
        {
            recordClass = RecordClass.Invalid;
            return false;
        }

        switch (s.ToUpperInvariant())
        {
            case "IN":
                recordClass = RecordClass.INet;
                return true;

            case "CH":
                recordClass = RecordClass.Chaos;
                return true;

            case "HS":
                recordClass = RecordClass.Hesiod;
                return true;

            case "NONE":
                recordClass = RecordClass.None;
                return true;

            case "*":
                if (allowAny)
                {
                    recordClass = RecordClass.Any;
                    return true;
                }
                else
                {
                    recordClass = RecordClass.Invalid;
                    return false;
                }

            default:
                if (s.StartsWith("CLASS", StringComparison.InvariantCultureIgnoreCase))
                {
                    ushort classValue;
                    if (UInt16.TryParse(s.Substring(5), out classValue))
                    {
                        recordClass = (RecordClass)classValue;
                        return true;
                    }
                }
                recordClass = RecordClass.Invalid;
                return false;
        }
    }
}

/// <summary>
///   Type of record
/// </summary>
public enum RecordType : ushort
{
    /// <summary>
    ///   Invalid record type
    /// </summary>
    Invalid = 0,

    /// <summary>
    ///   <para>Host address</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc1035">RFC 1035</see>
    ///   </para>
    /// </summary>
    A = 1,

    /// <summary>
    ///   <para>Authoritatitve name server</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc1035">RFC 1035</see>
    ///   </para>
    /// </summary>
    Ns = 2,

    /// <summary>
    ///   <para>Mail destination</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc1035">RFC 1035</see>
    ///   </para>
    /// </summary>
    [Obsolete]
    Md = 3,

    /// <summary>
    ///   <para>Mail forwarder</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc1035">RFC 1035</see>
    ///   </para>
    /// </summary>
    [Obsolete]
    Mf = 4,

    /// <summary>
    ///   <para>Canonical name for an alias</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc1035">RFC 1035</see>
    ///   </para>
    /// </summary>
    CName = 5,

    /// <summary>
    ///   <para>Start of zone of authority</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc1035">RFC 1035</see>
    ///   </para>
    /// </summary>
    Soa = 6,

    /// <summary>
    ///   <para>Mailbox domain name</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc1035">RFC 1035</see>
    ///     - Experimental
    ///   </para>
    /// </summary>
    Mb = 7, // not supported yet

    /// <summary>
    ///   <para>Mail group member</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc1035">RFC 1035</see>
    ///     - Experimental
    ///   </para>
    /// </summary>
    Mg = 8, // not supported yet

    /// <summary>
    ///   <para>Mail rename domain name</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc1035">RFC 1035</see>
    ///     - Experimental
    ///   </para>
    /// </summary>
    Mr = 9, // not supported yet

    /// <summary>
    ///   <para>Null record</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc1035">RFC 1035</see>
    ///     - Experimental
    ///   </para>
    /// </summary>
    Null = 10, // not supported yet

    /// <summary>
    ///   <para>Well known services</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc1035">RFC 1035</see>
    ///   </para>
    /// </summary>
    Wks = 11,

    /// <summary>
    ///   <para>Domain name pointer</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc1035">RFC 1035</see>
    ///   </para>
    /// </summary>
    Ptr = 12,

    /// <summary>
    ///   <para>Host information</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc1035">RFC 1035</see>
    ///   </para>
    /// </summary>
    HInfo = 13,

    /// <summary>
    ///   <para>Mailbox or mail list information</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc1035">RFC 1035</see>
    ///   </para>
    /// </summary>
    MInfo = 14, // not supported yet

    /// <summary>
    ///   <para>Mail exchange</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc1035">RFC 1035</see>
    ///   </para>
    /// </summary>
    Mx = 15,

    /// <summary>
    ///   <para>Text strings</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc1035">RFC 1035</see>
    ///   </para>
    /// </summary>
    Txt = 16,

    /// <summary>
    ///   <para>Responsible person</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc1183">RFC 1183</see>
    ///   </para>
    /// </summary>
    Rp = 17,

    /// <summary>
    ///   <para>AFS data base location</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc1183">RFC 1183</see>
    ///     and
    ///     <see cref="!:http://tools.ietf.org/html/rfc5864">RFC 5864</see>
    ///   </para>
    /// </summary>
    Afsdb = 18,

    /// <summary>
    ///   <para>X.25 PSDN address</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc1183">RFC 1183</see>
    ///   </para>
    /// </summary>
    X25 = 19,

    /// <summary>
    ///   <para>ISDN address</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc1183">RFC 1183</see>
    ///   </para>
    /// </summary>
    Isdn = 20,

    /// <summary>
    ///   <para>Route through</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc1183">RFC 1183</see>
    ///   </para>
    /// </summary>
    Rt = 21,

    /// <summary>
    ///   <para>NSAP address, NSAP style A record</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc1706">RFC 1706</see>
    ///   </para>
    /// </summary>
    Nsap = 22,

    /// <summary>
    ///   <para>Domain name pointer, NSAP style</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc1348">RFC 1348</see>
    ///     ,
    ///     <see cref="!:http://tools.ietf.org/html/rfc1637">RFC 1637</see>
    ///     and
    ///     <see cref="!:http://tools.ietf.org/html/rfc1706">RFC 1706</see>
    ///   </para>
    /// </summary>
    NsapPtr = 23, // not supported yet

    /// <summary>
    ///   <para>Security signature</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc4034">RFC 4034</see>
    ///     ,
    ///     <see cref="!:http://tools.ietf.org/html/rfc3755">RFC 3755</see>
    ///     ,
    ///     <see cref="!:http://tools.ietf.org/html/rfc2535">RFC 2535</see>
    ///     and
    ///     <see cref="!:http://tools.ietf.org/html/rfc2931">RFC 2931</see>
    ///   </para>
    /// </summary>
    Sig = 24,

    /// <summary>
    ///   <para>Security Key</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc4034">RFC 4034</see>
    ///     ,
    ///     <see cref="!:http://tools.ietf.org/html/rfc3755">RFC 3755</see>
    ///     ,
    ///     <see cref="!:http://tools.ietf.org/html/rfc2535">RFC 2535</see>
    ///     and
    ///     <see cref="!:http://tools.ietf.org/html/rfc2930">RFC 2930</see>
    ///   </para>
    /// </summary>
    Key = 25,

    /// <summary>
    ///   <para>X.400 mail mapping information</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc2163">RFC 2163</see>
    ///   </para>
    /// </summary>
    Px = 26,

    /// <summary>
    ///   <para>Geographical position</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc1712">RFC 1712</see>
    ///   </para>
    /// </summary>
    GPos = 27,

    /// <summary>
    ///   <para>IPv6 address</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc3596">RFC 3596</see>
    ///   </para>
    /// </summary>
    Aaaa = 28,

    /// <summary>
    ///   <para>Location information</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc1876">RFC 1876</see>
    ///   </para>
    /// </summary>
    Loc = 29,

    /// <summary>
    ///   <para>Next domain</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc3755">RFC 3755</see>
    ///     and
    ///     <see cref="!:http://tools.ietf.org/html/rfc2535">RFC 2535</see>
    ///   </para>
    /// </summary>
    [Obsolete]
    Nxt = 30,

    /// <summary>
    ///   <para>Endpoint identifier</para>
    ///   <para>Defined by Michael Patton, &lt;map@bbn.com&gt;, June 1995</para>
    /// </summary>
    Eid = 31, // not supported yet

    /// <summary>
    ///   <para>Nimrod locator</para>
    ///   <para>Defined by Michael Patton, &lt;map@bbn.com&gt;, June 1995</para>
    /// </summary>
    NimLoc = 32, // not supported yet

    /// <summary>
    ///   <para>Server selector</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc2782">RFC 2782</see>
    ///   </para>
    /// </summary>
    Srv = 33,

    /// <summary>
    ///   <para>ATM address</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://broadband-forum.org/ftp/pub/approved-specs/af-saa-0069.000.pdf">
    ///       ATM Forum Technical Committee,
    ///       "ATM Name System, V2.0"
    ///     </see>
    ///   </para>
    /// </summary>
    AtmA = 34, // not supported yet

    /// <summary>
    ///   <para>Naming authority pointer</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc2915">RFC 2915</see>
    ///     ,
    ///     <see cref="!:http://tools.ietf.org/html/rfc2168">RFC 2168</see>
    ///     and
    ///     <see cref="!:http://tools.ietf.org/html/rfc3403">RFC 3403</see>
    ///   </para>
    /// </summary>
    Naptr = 35,

    /// <summary>
    ///   <para>Key exchanger</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc2230">RFC 2230</see>
    ///   </para>
    /// </summary>
    Kx = 36,

    /// <summary>
    ///   <para>Certificate storage</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc4398">RFC 4398</see>
    ///   </para>
    /// </summary>
    Cert = 37,

    /// <summary>
    ///   <para>A6</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc3226">RFC 3226</see>
    ///     ,
    ///     <see cref="!:http://tools.ietf.org/html/rfc2874">RFC 2874</see>
    ///     and
    ///     <see cref="!:http://tools.ietf.org/html/rfc6563">RFC 2874</see>
    ///     - Experimental
    ///   </para>
    /// </summary>
    [Obsolete]
    A6 = 38,

    /// <summary>
    ///   <para>DNS Name Redirection</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc6672">RFC 6672</see>
    ///   </para>
    /// </summary>
    DName = 39,

    /// <summary>
    ///   <para>SINK</para>
    ///   <para>Defined by Donald E. Eastlake, III &lt;d3e3e3@gmail.com&gt;, January 1995, November 1997</para>
    /// </summary>
    Sink = 40, // not supported yet

    /// <summary>
    ///   <para>OPT</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc6891">RFC 6891</see>
    ///     and
    ///     <see cref="!:http://tools.ietf.org/html/rfc3225">RFC 3658</see>
    ///   </para>
    /// </summary>
    Opt = 41,

    /// <summary>
    ///   <para>Address prefixes</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc3123">RFC 3123</see>
    ///   </para>
    /// </summary>
    Apl = 42,

    /// <summary>
    ///   <para>Delegation signer</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc4034">RFC 4034</see>
    ///     and
    ///     <see cref="!:http://tools.ietf.org/html/rfc3658">RFC 3658</see>
    ///   </para>
    /// </summary>
    Ds = 43,

    /// <summary>
    ///   <para>SSH key fingerprint</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc4255">RFC 4255</see>
    ///   </para>
    /// </summary>
    SshFp = 44,

    /// <summary>
    ///   <para>IPsec key storage</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc4025">RFC 4025</see>
    ///   </para>
    /// </summary>
    IpSecKey = 45,

    /// <summary>
    ///   <para>Record signature</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc4034">RFC 4034</see>
    ///     and
    ///     <see cref="!:http://tools.ietf.org/html/rfc3755">RFC 3755</see>
    ///   </para>
    /// </summary>
    RrSig = 46,

    /// <summary>
    ///   <para>Next owner</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc4034">RFC 4034</see>
    ///     and
    ///     <see cref="!:http://tools.ietf.org/html/rfc3755">RFC 3755</see>
    ///   </para>
    /// </summary>
    NSec = 47,

    /// <summary>
    ///   <para>DNS Key</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc4034">RFC 4034</see>
    ///     and
    ///     <see cref="!:http://tools.ietf.org/html/rfc3755">RFC 3755</see>
    ///   </para>
    /// </summary>
    DnsKey = 48,

    /// <summary>
    ///   <para>Dynamic Host Configuration Protocol (DHCP) Information</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc4701">RFC 4701</see>
    ///   </para>
    /// </summary>
    Dhcid = 49,

    /// <summary>
    ///   <para>Hashed next owner</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc5155">RFC 5155</see>
    ///   </para>
    /// </summary>
    NSec3 = 50,

    /// <summary>
    ///   <para>Hashed next owner parameter</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc5155">RFC 5155</see>
    ///   </para>
    /// </summary>
    NSec3Param = 51,

    /// <summary>
    ///   <para>TLSA</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc6698">RFC 6698</see>
    ///   </para>
    /// </summary>
    Tlsa = 52,

    /// <summary>
    ///   <para>Host identity protocol</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc5205">RFC 5205</see>
    ///   </para>
    /// </summary>
    Hip = 55,

    /// <summary>
    ///   <para>NINFO</para>
    ///   <para>Defined by Jim Reid, &lt;jim@telnic.org&gt;, 21 January 2008</para>
    /// </summary>
    NInfo = 56, // not supported yet

    /// <summary>
    ///   <para>RKEY</para>
    ///   <para>Defined by Jim Reid, &lt;jim@telnic.org&gt;, 21 January 2008</para>
    /// </summary>
    RKey = 57, // not supported yet

    /// <summary>
    ///   <para>Trust anchor link</para>
    ///   <para>Defined by Wouter Wijngaards, &lt;wouter@nlnetlabs.nl&gt;, 2010-02-17</para>
    /// </summary>
    // ReSharper disable once InconsistentNaming
    TALink = 58, // not supported yet

    /// <summary>
    ///   <para>Child DS</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc7344">RFC 7344</see>
    ///   </para>
    /// </summary>
    CDs = 59,

    /// <summary>
    ///   <para>Child DnsKey</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc7344">RFC 7344</see>
    ///   </para>
    /// </summary>
    CDnsKey = 60,

    /// <summary>
    ///   <para>OpenPGP Key</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/draft-ietf-dane-openpgpkey">draft-ietf-dane-openpgpkey</see>
    ///   </para>
    /// </summary>
    // ReSharper disable once InconsistentNaming
    OpenPGPKey = 61,

    /// <summary>
    ///   <para>Child-to-Parent Synchronization</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc7477">RFC 7477</see>
    ///   </para>
    /// </summary>
    CSync = 62,

    /// <summary>
    ///   <para>Sender Policy Framework</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc4408">RFC 4408</see>
    ///     and
    ///     <see cref="!:http://tools.ietf.org/html/rfc7208">RFC 7208</see>
    ///   </para>
    /// </summary>
    [Obsolete]
    Spf = 99,

    /// <summary>
    ///   <para>UINFO</para>
    ///   <para>IANA-Reserved</para>
    /// </summary>
    UInfo = 100, // not supported yet

    /// <summary>
    ///   <para>UID</para>
    ///   <para>IANA-Reserved</para>
    /// </summary>
    UId = 101, // not supported yet

    /// <summary>
    ///   <para>GID</para>
    ///   <para>IANA-Reserved</para>
    /// </summary>
    Gid = 102, // not supported yet

    /// <summary>
    ///   <para>UNSPEC</para>
    ///   <para>IANA-Reserved</para>
    /// </summary>
    Unspec = 103, // not supported yet

    /// <summary>
    ///   <para>NID</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc6742">RFC 6742</see>
    ///   </para>
    /// </summary>
    NId = 104,

    /// <summary>
    ///   <para>L32</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc6742">RFC 6742</see>
    ///   </para>
    /// </summary>
    L32 = 105,

    /// <summary>
    ///   <para>L64</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc6742">RFC 6742</see>
    ///   </para>
    /// </summary>
    L64 = 106,

    /// <summary>
    ///   <para>LP</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc6742">RFC 6742</see>
    ///   </para>
    /// </summary>
    // ReSharper disable once InconsistentNaming
    LP = 107,

    /// <summary>
    ///   <para>EUI-48 address</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc7043">RFC 7043</see>
    ///   </para>
    /// </summary>
    Eui48 = 108,

    /// <summary>
    ///   <para>EUI-64 address</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc7043">RFC 7043</see>
    ///   </para>
    /// </summary>
    Eui64 = 109,

    /// <summary>
    ///   <para>Transaction key</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc2930">RFC 2930</see>
    ///   </para>
    /// </summary>
    // ReSharper disable once InconsistentNaming
    TKey = 249,

    /// <summary>
    ///   <para>Transaction signature</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc2845">RFC 2845</see>
    ///   </para>
    /// </summary>
    // ReSharper disable once InconsistentNaming
    TSig = 250,

    /// <summary>
    ///   <para>Incremental zone transfer</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc1995">RFC 1995</see>
    ///   </para>
    /// </summary>
    Ixfr = 251,

    /// <summary>
    ///   <para>Request transfer of entire zone</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc1035">RFC 1035</see>
    ///     and
    ///     <see cref="!:http://tools.ietf.org/html/rfc5936">RFC 5936</see>
    ///   </para>
    /// </summary>
    Axfr = 252,

    /// <summary>
    ///   <para>Request mailbox related recors</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc1035">RFC 1035</see>
    ///   </para>
    /// </summary>
    MailB = 253,

    /// <summary>
    ///   <para>Request of mail agent records</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc1035">RFC 1035</see>
    ///   </para>
    /// </summary>
    [Obsolete]
    MailA = 254,

    /// <summary>
    ///   <para>Request of all records</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc1035">RFC 1035</see>
    ///   </para>
    /// </summary>
    Any = 255,

    /// <summary>
    ///   <para>Uniform Resource Identifier</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc7553">RFC 7553</see>
    ///   </para>
    /// </summary>
    Uri = 256,

    /// <summary>
    ///   <para>Certification authority auhtorization</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc6844">RFC 6844</see>
    ///   </para>
    /// </summary>
    // ReSharper disable once InconsistentNaming
    CAA = 257,

    /// <summary>
    ///   <para>DNSSEC trust authorities</para>
    ///   <para>Defined by Sam Weiler, &lt;weiler+iana@tislabs.com&gt;</para>
    /// </summary>
    Ta = 32768, // not supported yet

    /// <summary>
    ///   <para>DNSSEC lookaside validation</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc4431">RFC 4431</see>
    ///   </para>
    /// </summary>
    Dlv = 32769,
}

internal static class RecordTypeHelper
{
    public static string ToShortString(this RecordType recordType)
    {
        string res;
        if (!EnumHelper<RecordType>.Names.TryGetValue(recordType, out res))
        {
            return "TYPE" + (int)recordType;
        }
        return res.ToUpperInvariant();
    }

    public static bool TryParseShortString(string s, out RecordType recordType)
    {
        if (String.IsNullOrEmpty(s))
        {
            recordType = RecordType.Invalid;
            return false;
        }

        if (EnumHelper<RecordType>.TryParse(s, true, out recordType))
            return true;

        if (s.StartsWith("TYPE", StringComparison.InvariantCultureIgnoreCase))
        {
            ushort classValue;
            if (UInt16.TryParse(s.Substring(4), out classValue))
            {
                recordType = (RecordType)classValue;
                return true;
            }
        }
        recordType = RecordType.Invalid;
        return false;
    }

    public static RecordType ParseShortString(string s)
    {
        if (String.IsNullOrEmpty(s))
            throw new ArgumentOutOfRangeException(nameof(s));

        RecordType recordType;
        if (EnumHelper<RecordType>.TryParse(s, true, out recordType))
            return recordType;

        if (s.StartsWith("TYPE", StringComparison.InvariantCultureIgnoreCase))
        {
            ushort classValue;
            if (UInt16.TryParse(s.Substring(4), out classValue))
                return (RecordType)classValue;
        }

        throw new ArgumentOutOfRangeException(nameof(s));
    }
}

/// <summary>
///   Result of a dns request
/// </summary>
public enum ReturnCode : ushort
{
    /// <summary>
    ///   <para>No error</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc1035">RFC 1035</see>
    ///   </para>
    /// </summary>
    NoError = 0,

    /// <summary>
    ///   <para>Format error</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc1035">RFC 1035</see>
    ///   </para>
    /// </summary>
    FormatError = 1,

    /// <summary>
    ///   <para>Server failure</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc1035">RFC 1035</see>
    ///   </para>
    /// </summary>
    ServerFailure = 2,

    /// <summary>
    ///   <para>Non-existent domain</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc1035">RFC 1035</see>
    ///   </para>
    /// </summary>
    NxDomain = 3,

    /// <summary>
    ///   <para>Not implemented</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc1035">RFC 1035</see>
    ///   </para>
    /// </summary>
    NotImplemented = 4,

    /// <summary>
    ///   <para>Query refused</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc1035">RFC 1035</see>
    ///   </para>
    /// </summary>
    Refused = 5,

    /// <summary>
    ///   <para>Name exists when it should not</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc2136">RFC 2136</see>
    ///   </para>
    /// </summary>
    // ReSharper disable once InconsistentNaming
    YXDomain = 6,

    /// <summary>
    ///   <para>Record exists when it should not</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc2136">RFC 2136</see>
    ///   </para>
    /// </summary>
    // ReSharper disable once InconsistentNaming
    YXRRSet = 7,

    /// <summary>
    ///   <para>Record that should exist does not</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc2136">RFC 2136</see>
    ///   </para>
    /// </summary>
    // ReSharper disable once InconsistentNaming
    NXRRSet = 8,

    /// <summary>
    ///   <para>Server is not authoritative for zone</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc2136">RFC 2136</see>
    ///   </para>
    /// </summary>
    NotAuthoritive = 9,

    /// <summary>
    ///   <para>Name not contained in zone</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc2136">RFC 2136</see>
    ///   </para>
    /// </summary>
    NotZone = 10,

    /// <summary>
    ///   <para>EDNS version is not supported by responder</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc2671">RFC 2671</see>
    ///   </para>
    /// </summary>
    BadVersion = 16,

    /// <summary>
    ///   <para>TSIG signature failure</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc2845">RFC 2845</see>
    ///   </para>
    /// </summary>
    BadSig = 16,

    /// <summary>
    ///   <para>Key not recognized</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc2845">RFC 2845</see>
    ///   </para>
    /// </summary>
    BadKey = 17,

    /// <summary>
    ///   <para>Signature out of time window</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc2845">RFC 2845</see>
    ///   </para>
    /// </summary>
    BadTime = 18,

    /// <summary>
    ///   <para>Bad TKEY mode</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc2930">RFC 2930</see>
    ///   </para>
    /// </summary>
    BadMode = 19,

    /// <summary>
    ///   <para>Duplicate key name</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc2930">RFC 2930</see>
    ///   </para>
    /// </summary>
    BadName = 20,

    /// <summary>
    ///   <para>Algorithm not supported</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc2930">RFC 2930</see>
    ///   </para>
    /// </summary>
    BadAlg = 21,

    /// <summary>
    ///   <para>Bad truncation of TSIG record</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc4635">RFC 4635</see>
    ///   </para>
    /// </summary>
    BadTrunc = 22,

    /// <summary>
    ///   <para>Bad/missing server cookie</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/draft-ietf-dnsop-cookies">draft-ietf-dnsop-cookies</see>
    ///   </para>
    /// </summary>
    BadCookie = 23,
}

/// <summary>
///   Class representing a DNS zone
/// </summary>
public class Zone : ICollection<DnsRecordBase>
{
    private static readonly SecureRandom _secureRandom = new SecureRandom();

    private static readonly Regex _commentRemoverRegex = new Regex(@"^(?<data>(\\\""|[^\""]|(?<!\\)\"".*?(?<!\\)\"")*?)(;.*)?$", RegexOptions.Compiled | RegexOptions.ExplicitCapture);
    private static readonly Regex _lineSplitterRegex = new Regex("([^\\s\"]+)|\"(.*?(?<!\\\\))\"", RegexOptions.Compiled);

    private readonly List<DnsRecordBase> _records;

    /// <summary>
    ///   Gets the name of the Zone
    /// </summary>
    public DomainName Name { get; }

    /// <summary>
    ///   Creates a new instance of the Zone class with no records
    /// </summary>
    /// <param name="name">The name of the zone</param>
    public Zone(DomainName name)
    {
        Name = name;
        _records = new List<DnsRecordBase>();
    }

    /// <summary>
    ///   Creates a new instance of the Zone class that contains records copied from the specified collection
    /// </summary>
    /// <param name="name">The name of the zone</param>
    /// <param name="collection">Collection of records which are copied to the new Zone instance</param>
    public Zone(DomainName name, IEnumerable<DnsRecordBase> collection)
    {
        Name = name;
        _records = new List<DnsRecordBase>(collection);
    }

    /// <summary>
    ///   Create a new instance of the Zone class with the specified initial capacity
    /// </summary>
    /// <param name="name">The name of the zone</param>
    /// <param name="capacity">The initial capacity for the new Zone instance</param>
    public Zone(DomainName name, int capacity)
    {
        Name = name;
        _records = new List<DnsRecordBase>(capacity);
    }

    /// <summary>
    ///   Loads a Zone from a master file
    /// </summary>
    /// <param name="name">The name of the zone</param>
    /// <param name="zoneFile">Path to the Zone master file</param>
    /// <returns>A new instance of the Zone class</returns>
    public static Zone ParseMasterFile(DomainName name, string zoneFile)
    {
        using (StreamReader reader = new StreamReader(zoneFile))
        {
            return ParseMasterFile(name, reader);
        }
    }

    /// <summary>
    ///   Loads a Zone from a master data stream
    /// </summary>
    /// <param name="name">The name of the zone</param>
    /// <param name="zoneFile">Stream containing the zone master data</param>
    /// <returns>A new instance of the Zone class</returns>
    public static Zone ParseMasterFile(DomainName name, Stream zoneFile)
    {
        using (StreamReader reader = new StreamReader(zoneFile))
        {
            return ParseMasterFile(name, reader);
        }
    }

    private static Zone ParseMasterFile(DomainName name, StreamReader reader)
    {
        List<DnsRecordBase> records = ParseRecords(reader, name, 0, new UnknownRecord(name, RecordType.Invalid, RecordClass.INet, 0, new byte[] { }));

        SoaRecord soa = (SoaRecord)records.SingleOrDefault(x => x.RecordType == RecordType.Soa);

        if (soa != null)
        {
            records.ForEach(x =>
            {
                if (x.TimeToLive == 0)
                    x.TimeToLive = soa.NegativeCachingTTL;
            });
        }

        return new Zone(name, records);
    }

    private static List<DnsRecordBase> ParseRecords(StreamReader reader, DomainName origin, int ttl, DnsRecordBase lastRecord)
    {
        List<DnsRecordBase> records = new List<DnsRecordBase>();

        while (!reader.EndOfStream)
        {
            string line = ReadRecordLine(reader);

            if (!String.IsNullOrEmpty(line))
            {
                string[] parts = _lineSplitterRegex.Matches(line).Cast<Match>().Select(x => x.Groups.Cast<Group>().Last(g => g.Success).Value.FromMasterfileLabelRepresentation()).ToArray();

                if (parts[0].Equals("$origin", StringComparison.InvariantCultureIgnoreCase))
                {
                    origin = DomainName.ParseFromMasterfile(parts[1]);
                }
                if (parts[0].Equals("$ttl", StringComparison.InvariantCultureIgnoreCase))
                {
                    ttl = Int32.Parse(parts[1]);
                }
                if (parts[0].Equals("$include", StringComparison.InvariantCultureIgnoreCase))
                {
                    FileStream fileStream = reader.BaseStream as FileStream;

                    if (fileStream == null)
                        throw new NotSupportedException("Includes only supported when loading files");

                    // ReSharper disable once AssignNullToNotNullAttribute
                    string path = Path.Combine(new FileInfo(fileStream.Name).DirectoryName, parts[1]);

                    DomainName includeOrigin = (parts.Length > 2) ? DomainName.ParseFromMasterfile(parts[2]) : origin;

                    using (StreamReader includeReader = new StreamReader(path))
                    {
                        records.AddRange(ParseRecords(includeReader, includeOrigin, ttl, lastRecord));
                    }
                }
                else
                {
                    string domainString;
                    RecordType recordType;
                    RecordClass recordClass;
                    int recordTtl;
                    string[] rrData;

                    if (Int32.TryParse(parts[0], out recordTtl))
                    {
                        // no domain, starts with ttl
                        if (RecordClassHelper.TryParseShortString(parts[1], out recordClass, false))
                        {
                            // second is record class
                            domainString = null;
                            recordType = RecordTypeHelper.ParseShortString(parts[2]);
                            rrData = parts.Skip(3).ToArray();
                        }
                        else
                        {
                            // no record class
                            domainString = null;
                            recordClass = RecordClass.Invalid;
                            recordType = RecordTypeHelper.ParseShortString(parts[1]);
                            rrData = parts.Skip(2).ToArray();
                        }
                    }
                    else if (RecordClassHelper.TryParseShortString(parts[0], out recordClass, false))
                    {
                        // no domain, starts with record class
                        if (Int32.TryParse(parts[1], out recordTtl))
                        {
                            // second is ttl
                            domainString = null;
                            recordType = RecordTypeHelper.ParseShortString(parts[2]);
                            rrData = parts.Skip(3).ToArray();
                        }
                        else
                        {
                            // no ttl
                            recordTtl = 0;
                            domainString = null;
                            recordType = RecordTypeHelper.ParseShortString(parts[1]);
                            rrData = parts.Skip(2).ToArray();
                        }
                    }
                    else if (RecordTypeHelper.TryParseShortString(parts[0], out recordType))
                    {
                        // no domain, start with record type
                        recordTtl = 0;
                        recordClass = RecordClass.Invalid;
                        domainString = null;
                        rrData = parts.Skip(2).ToArray();
                    }
                    else
                    {
                        domainString = parts[0];

                        if (Int32.TryParse(parts[1], out recordTtl))
                        {
                            // domain, second is ttl
                            if (RecordClassHelper.TryParseShortString(parts[2], out recordClass, false))
                            {
                                // third is record class
                                recordType = RecordTypeHelper.ParseShortString(parts[3]);
                                rrData = parts.Skip(4).ToArray();
                            }
                            else
                            {
                                // no record class
                                recordClass = RecordClass.Invalid;
                                recordType = RecordTypeHelper.ParseShortString(parts[2]);
                                rrData = parts.Skip(3).ToArray();
                            }
                        }
                        else if (RecordClassHelper.TryParseShortString(parts[1], out recordClass, false))
                        {
                            // domain, second is record class
                            if (Int32.TryParse(parts[2], out recordTtl))
                            {
                                // third is ttl
                                recordType = RecordTypeHelper.ParseShortString(parts[3]);
                                rrData = parts.Skip(4).ToArray();
                            }
                            else
                            {
                                // no ttl
                                recordTtl = 0;
                                recordType = RecordTypeHelper.ParseShortString(parts[2]);
                                rrData = parts.Skip(3).ToArray();
                            }
                        }
                        else
                        {
                            // domain with record type
                            recordType = RecordTypeHelper.ParseShortString(parts[1]);
                            recordTtl = 0;
                            recordClass = RecordClass.Invalid;
                            rrData = parts.Skip(2).ToArray();
                        }
                    }

                    DomainName domain;
                    if (String.IsNullOrEmpty(domainString))
                    {
                        domain = lastRecord.Name;
                    }
                    else if (domainString == "@")
                    {
                        domain = origin;
                    }
                    else if (domainString.EndsWith("."))
                    {
                        domain = DomainName.ParseFromMasterfile(domainString);
                    }
                    else
                    {
                        domain = DomainName.ParseFromMasterfile(domainString) + origin;
                    }

                    if (recordClass == RecordClass.Invalid)
                    {
                        recordClass = lastRecord.RecordClass;
                    }

                    if (recordType == RecordType.Invalid)
                    {
                        recordType = lastRecord.RecordType;
                    }

                    if (recordTtl == 0)
                    {
                        recordTtl = ttl;
                    }
                    else
                    {
                        ttl = recordTtl;
                    }

                    lastRecord = DnsRecordBase.Create(recordType);
                    lastRecord.RecordType = recordType;
                    lastRecord.Name = domain;
                    lastRecord.RecordClass = recordClass;
                    lastRecord.TimeToLive = recordTtl;

                    if ((rrData.Length > 0) && (rrData[0] == @"\#"))
                    {
                        lastRecord.ParseUnknownRecordData(rrData);
                    }
                    else
                    {
                        lastRecord.ParseRecordData(origin, rrData);
                    }

                    records.Add(lastRecord);
                }
            }
        }

        return records;
    }

    private static string ReadRecordLine(StreamReader reader)
    {
        string line = ReadLineWithoutComment(reader);

        int bracketPos;
        if ((bracketPos = line.IndexOf('(')) != -1)
        {
            StringBuilder sb = new StringBuilder();

            sb.Append(line.Substring(0, bracketPos));
            sb.Append(" ");
            sb.Append(line.Substring(bracketPos + 1));

            while (true)
            {
                sb.Append(" ");

                line = ReadLineWithoutComment(reader);

                if ((bracketPos = line.IndexOf(')')) == -1)
                {
                    sb.Append(line);
                }
                else
                {
                    sb.Append(line.Substring(0, bracketPos));
                    sb.Append(" ");
                    sb.Append(line.Substring(bracketPos + 1));
                    line = sb.ToString();
                    break;
                }
            }
        }

        return line;
    }

    private static string ReadLineWithoutComment(StreamReader reader)
    {
        string line = reader.ReadLine();
        // ReSharper disable once AssignNullToNotNullAttribute
        return _commentRemoverRegex.Match(line).Groups["data"].Value;
    }

    /// <summary>
    ///   Signs a zone
    /// </summary>
    /// <param name="keys">A list of keys to sign the zone</param>
    /// <param name="inception">The inception date of the signatures</param>
    /// <param name="expiration">The expiration date of the signatures</param>
    /// <param name="nsec3Algorithm">The NSEC3 algorithm (or 0 when NSEC should be used)</param>
    /// <param name="nsec3Iterations">The number of iterations when NSEC3 is used</param>
    /// <param name="nsec3Salt">The salt when NSEC3 is used</param>
    /// <param name="nsec3OptOut">true, of NSEC3 OptOut should be used for delegations without DS record</param>
    /// <returns>A signed zone</returns>
    public Zone Sign(List<DnsKeyRecord> keys, DateTime inception, DateTime expiration, NSec3HashAlgorithm nsec3Algorithm = 0, int nsec3Iterations = 10, byte[] nsec3Salt = null, bool nsec3OptOut = false)
    {
        if ((keys == null) || (keys.Count == 0))
            throw new Exception("No DNS Keys were provided");

        if (!keys.All(x => x.IsZoneKey))
            throw new Exception("No DNS key with Zone Key Flag were provided");

        if (keys.Any(x => (x.PrivateKey == null) || (x.PrivateKey.Length == 0)))
            throw new Exception("For at least one DNS key no Private Key was provided");

        if (keys.Any(x => (x.Protocol != 3) || ((nsec3Algorithm != 0) ? !x.Algorithm.IsCompatibleWithNSec3() : !x.Algorithm.IsCompatibleWithNSec())))
            throw new Exception("At least one invalid DNS key was provided");

        List<DnsKeyRecord> keySigningKeys = keys.Where(x => x.IsSecureEntryPoint).ToList();
        List<DnsKeyRecord> zoneSigningKeys = keys.Where(x => !x.IsSecureEntryPoint).ToList();

        if (nsec3Algorithm == 0)
        {
            return SignWithNSec(inception, expiration, zoneSigningKeys, keySigningKeys);
        }
        else
        {
            return SignWithNSec3(inception, expiration, zoneSigningKeys, keySigningKeys, nsec3Algorithm, nsec3Iterations, nsec3Salt, nsec3OptOut);
        }
    }

    private Zone SignWithNSec(DateTime inception, DateTime expiration, List<DnsKeyRecord> zoneSigningKeys, List<DnsKeyRecord> keySigningKeys)
    {
        var soaRecord = _records.OfType<SoaRecord>().First();
        var subZones = _records.Where(x => (x.RecordType == RecordType.Ns) && (x.Name != Name)).Select(x => x.Name).Distinct().ToList();
        var glueRecords = _records.Where(x => subZones.Any(y => x.Name.IsSubDomainOf(y))).ToList();
        var recordsByName = _records.Except(glueRecords).Union(zoneSigningKeys).Union(keySigningKeys).GroupBy(x => x.Name).Select(x => new Tuple<DomainName, List<DnsRecordBase>>(x.Key, x.OrderBy(y => y.RecordType == RecordType.Soa ? -1 : (int)y.RecordType).ToList())).OrderBy(x => x.Item1).ToList();

        Zone res = new Zone(Name, Count * 3);

        for (int i = 0; i < recordsByName.Count; i++)
        {
            List<RecordType> recordTypes = new List<RecordType>();

            DomainName currentName = recordsByName[i].Item1;

            foreach (var recordsByType in recordsByName[i].Item2.GroupBy(x => x.RecordType))
            {
                List<DnsRecordBase> records = recordsByType.ToList();

                recordTypes.Add(recordsByType.Key);
                res.AddRange(records);

                // do not sign nameserver delegations for sub zones
                if ((records[0].RecordType == RecordType.Ns) && (currentName != Name))
                    continue;

                recordTypes.Add(RecordType.RrSig);

                foreach (var key in zoneSigningKeys)
                {
                    res.Add(new RrSigRecord(records, key, inception, expiration));
                }
                if (records[0].RecordType == RecordType.DnsKey)
                {
                    foreach (var key in keySigningKeys)
                    {
                        res.Add(new RrSigRecord(records, key, inception, expiration));
                    }
                }
            }

            recordTypes.Add(RecordType.NSec);

            NSecRecord nsecRecord = new NSecRecord(recordsByName[i].Item1, soaRecord.RecordClass, soaRecord.NegativeCachingTTL, recordsByName[(i + 1) % recordsByName.Count].Item1, recordTypes);
            res.Add(nsecRecord);

            foreach (var key in zoneSigningKeys)
            {
                res.Add(new RrSigRecord(new List<DnsRecordBase>() { nsecRecord }, key, inception, expiration));
            }
        }

        res.AddRange(glueRecords);

        return res;
    }

    private Zone SignWithNSec3(DateTime inception, DateTime expiration, List<DnsKeyRecord> zoneSigningKeys, List<DnsKeyRecord> keySigningKeys, NSec3HashAlgorithm nsec3Algorithm, int nsec3Iterations, byte[] nsec3Salt, bool nsec3OptOut)
    {
        var soaRecord = _records.OfType<SoaRecord>().First();
        var subZoneNameserver = _records.Where(x => (x.RecordType == RecordType.Ns) && (x.Name != Name)).ToList();
        var subZones = subZoneNameserver.Select(x => x.Name).Distinct().ToList();
        var unsignedRecords = _records.Where(x => subZones.Any(y => x.Name.IsSubDomainOf(y))).ToList(); // glue records
        if (nsec3OptOut)
            unsignedRecords = unsignedRecords.Union(subZoneNameserver.Where(x => !_records.Any(y => (y.RecordType == RecordType.Ds) && (y.Name == x.Name)))).ToList(); // delegations without DS record
        var recordsByName = _records.Except(unsignedRecords).Union(zoneSigningKeys).Union(keySigningKeys).GroupBy(x => x.Name).Select(x => new Tuple<DomainName, List<DnsRecordBase>>(x.Key, x.OrderBy(y => y.RecordType == RecordType.Soa ? -1 : (int)y.RecordType).ToList())).OrderBy(x => x.Item1).ToList();

        byte nsec3RecordFlags = (byte)(nsec3OptOut ? 1 : 0);

        Zone res = new Zone(Name, Count * 3);
        List<NSec3Record> nSec3Records = new List<NSec3Record>(Count);

        if (nsec3Salt == null)
            nsec3Salt = _secureRandom.GenerateSeed(8);

        recordsByName[0].Item2.Add(new NSec3ParamRecord(soaRecord.Name, soaRecord.RecordClass, 0, nsec3Algorithm, 0, (ushort)nsec3Iterations, nsec3Salt));

        HashSet<DomainName> allNames = new HashSet<DomainName>();

        for (int i = 0; i < recordsByName.Count; i++)
        {
            List<RecordType> recordTypes = new List<RecordType>();

            DomainName currentName = recordsByName[i].Item1;

            foreach (var recordsByType in recordsByName[i].Item2.GroupBy(x => x.RecordType))
            {
                List<DnsRecordBase> records = recordsByType.ToList();

                recordTypes.Add(recordsByType.Key);
                res.AddRange(records);

                // do not sign nameserver delegations for sub zones
                if ((records[0].RecordType == RecordType.Ns) && (currentName != Name))
                    continue;

                recordTypes.Add(RecordType.RrSig);

                foreach (var key in zoneSigningKeys)
                {
                    res.Add(new RrSigRecord(records, key, inception, expiration));
                }
                if (records[0].RecordType == RecordType.DnsKey)
                {
                    foreach (var key in keySigningKeys)
                    {
                        res.Add(new RrSigRecord(records, key, inception, expiration));
                    }
                }
            }

            byte[] hash = recordsByName[i].Item1.GetNSec3Hash(nsec3Algorithm, nsec3Iterations, nsec3Salt);
            nSec3Records.Add(new NSec3Record(DomainName.ParseFromMasterfile(hash.ToBase32HexString()) + Name, soaRecord.RecordClass, soaRecord.NegativeCachingTTL, nsec3Algorithm, nsec3RecordFlags, (ushort)nsec3Iterations, nsec3Salt, hash, recordTypes));

            allNames.Add(currentName);
            for (int j = currentName.LabelCount - Name.LabelCount; j > 0; j--)
            {
                DomainName possibleNonTerminal = currentName.GetParentName(j);

                if (!allNames.Contains(possibleNonTerminal))
                {
                    hash = possibleNonTerminal.GetNSec3Hash(nsec3Algorithm, nsec3Iterations, nsec3Salt);
                    nSec3Records.Add(new NSec3Record(DomainName.ParseFromMasterfile(hash.ToBase32HexString()) + Name, soaRecord.RecordClass, soaRecord.NegativeCachingTTL, nsec3Algorithm, nsec3RecordFlags, (ushort)nsec3Iterations, nsec3Salt, hash, new List<RecordType>()));

                    allNames.Add(possibleNonTerminal);
                }
            }
        }

        nSec3Records = nSec3Records.OrderBy(x => x.Name).ToList();

        byte[] firstNextHashedOwnerName = nSec3Records[0].NextHashedOwnerName;

        for (int i = 1; i < nSec3Records.Count; i++)
        {
            nSec3Records[i - 1].NextHashedOwnerName = nSec3Records[i].NextHashedOwnerName;
        }

        nSec3Records[nSec3Records.Count - 1].NextHashedOwnerName = firstNextHashedOwnerName;

        foreach (var nSec3Record in nSec3Records)
        {
            res.Add(nSec3Record);

            foreach (var key in zoneSigningKeys)
            {
                res.Add(new RrSigRecord(new List<DnsRecordBase>() { nSec3Record }, key, inception, expiration));
            }
        }

        res.AddRange(unsignedRecords);

        return res;
    }


    /// <summary>
    ///   Adds a record to the end of the Zone
    /// </summary>
    /// <param name="item">Record to be added</param>
    public void Add(DnsRecordBase item)
    {
        _records.Add(item);
    }

    /// <summary>
    ///   Adds an enumeration of records to the end of the Zone
    /// </summary>
    /// <param name="items">Records to be added</param>
    public void AddRange(IEnumerable<DnsRecordBase> items)
    {
        _records.AddRange(items);
    }

    /// <summary>
    ///   Removes all records from the zone
    /// </summary>
    public void Clear()
    {
        _records.Clear();
    }

    /// <summary>
    ///   Determines whether a record is in the Zone
    /// </summary>
    /// <param name="item">Item which should be searched</param>
    /// <returns>true, if the item is in the zone; otherwise, false</returns>
    public bool Contains(DnsRecordBase item)
    {
        return _records.Contains(item);
    }

    /// <summary>
    ///   Copies the entire Zone to a compatible array
    /// </summary>
    /// <param name="array">Array to which the records should be copied</param>
    /// <param name="arrayIndex">Starting index within the target array</param>
    public void CopyTo(DnsRecordBase[] array, int arrayIndex)
    {
        _records.CopyTo(array, arrayIndex);
    }

    /// <summary>
    ///   Gets the number of records actually contained in the Zone
    /// </summary>
    public int Count => _records.Count;

    /// <summary>
    ///   A value indicating whether the Zone is readonly
    /// </summary>
    /// <returns>false</returns>
    bool ICollection<DnsRecordBase>.IsReadOnly => false;

    /// <summary>
    ///   Removes a record from the Zone
    /// </summary>
    /// <param name="item">Item to be removed</param>
    /// <returns>true, if the record was removed from the Zone; otherwise, false</returns>
    public bool Remove(DnsRecordBase item)
    {
        return _records.Remove(item);
    }

    /// <summary>
    ///   Returns an enumerator that iterates through the records of the Zone
    /// </summary>
    /// <returns>An enumerator that iterates through the records of the Zone</returns>
    public IEnumerator<DnsRecordBase> GetEnumerator()
    {
        return _records.GetEnumerator();
    }

    /// <summary>
    ///   Returns an enumerator that iterates through the records of the Zone
    /// </summary>
    /// <returns>An enumerator that iterates through the records of the Zone</returns>
    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}


#endif
