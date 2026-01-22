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
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

namespace IPA.Cores.Basic.DnsLib;

/// <summary>
///   <para>EDNS0 Client Subnet Option</para>
///   <para>
///     Defined in
///     <see cref="!:http://tools.ietf.org/html/draft-vandergaast-edns-client-subnet-02">draft-vandergaast-edns-client-subnet</see>
///   </para>
/// </summary>
public class ClientSubnetOption : EDnsOptionBase
{
    /// <summary>
    ///   The address family
    /// </summary>
    public AddressFamily Family => Address.AddressFamily;

    /// <summary>
    ///   The source subnet mask
    /// </summary>
    public byte SourceNetmask { get; private set; }

    /// <summary>
    ///   The scope subnet mask
    /// </summary>
    public byte ScopeNetmask { get; private set; }

    /// <summary>
    ///   The address
    /// </summary>
    public IPAddress Address { get; private set; }

    internal ClientSubnetOption()
        : base(EDnsOptionType.ClientSubnet) { }

    /// <summary>
    ///   Creates a new instance of the OwnerOption class
    /// </summary>
    /// <param name="sourceNetmask"> The source subnet mask </param>
    /// <param name="address"> The address </param>
    public ClientSubnetOption(byte sourceNetmask, IPAddress address)
        : this(sourceNetmask, 0, address) { }

    /// <summary>
    ///   Creates a new instance of the OwnerOption class
    /// </summary>
    /// <param name="sourceNetmask"> The source subnet mask </param>
    /// <param name="scopeNetmask"> The scope subnet mask </param>
    /// <param name="address"> The address </param>
    public ClientSubnetOption(byte sourceNetmask, byte scopeNetmask, IPAddress address)
        : this()
    {
        SourceNetmask = sourceNetmask;
        ScopeNetmask = scopeNetmask;
        Address = address;
    }

    internal override void ParseData(ReadOnlySpan<byte> resultData, int startPosition, int length)
    {
        ushort family = DnsMessageBase.ParseUShort(resultData, ref startPosition);
        SourceNetmask = resultData[startPosition++];
        ScopeNetmask = resultData[startPosition++];

        byte[] addressData = new byte[family == 1 ? 4 : 16];
        Util.BlockCopy(resultData, startPosition, addressData, 0, GetAddressLength());

        Address = new IPAddress(addressData);
    }

    internal override ushort DataLength => (ushort)(4 + GetAddressLength());

    internal override void EncodeData(Span<byte> messageData, ref int currentPosition)
    {
        DnsMessageBase.EncodeUShort(messageData, ref currentPosition, (ushort)(Family == AddressFamily.InterNetwork ? 1 : 2));
        messageData[currentPosition++] = SourceNetmask;
        messageData[currentPosition++] = ScopeNetmask;

        byte[] data = Address.GetAddressBytes();
        DnsMessageBase.EncodeByteArray(messageData, ref currentPosition, data, GetAddressLength());
    }

    private int GetAddressLength()
    {
        return (int)Math.Ceiling(SourceNetmask / 8d);
    }
}

/// <summary>
///   <para>Cookie Option</para>
///   <para>
///     Defined in
///     <see cref="!:http://tools.ietf.org/html/draft-ietf-dnsop-cookies">draft-ietf-dnsop-cookies</see>
///   </para>
/// </summary>
public class CookieOption : EDnsOptionBase
{
    private byte[] _clientCookie;

    /// <summary>
    ///   Client cookie
    /// </summary>
    public byte[] ClientCookie
    {
        get { return _clientCookie; }
        private set
        {
            if ((value == null) || (value.Length != 8))
                throw new ArgumentException("Client cookie must contain 8 bytes");
            _clientCookie = value;
        }
    }

    /// <summary>
    ///   Server cookie
    /// </summary>
    public byte[] ServerCookie { get; private set; }

    /// <summary>
    ///   Creates a new instance of the ClientCookie class
    /// </summary>
    public CookieOption()
        : base(EDnsOptionType.Cookie) { }

    /// <summary>
    ///   Creates a new instance of the ClientCookie class
    /// </summary>
    /// <param name="clientCookie">The client cookie</param>
    /// <param name="serverCookie">The server cookie</param>
    public CookieOption(byte[] clientCookie, byte[] serverCookie = null)
        : this()
    {
        ClientCookie = clientCookie;
        ServerCookie = serverCookie ?? new byte[] { };
    }

    internal override void ParseData(ReadOnlySpan<byte> resultData, int startPosition, int length)
    {
        ClientCookie = DnsMessageBase.ParseByteData(resultData, ref startPosition, 8);
        ServerCookie = DnsMessageBase.ParseByteData(resultData, ref startPosition, length - 8);
    }

    internal override ushort DataLength => (ushort)(8 + ServerCookie.Length);

    internal override void EncodeData(Span<byte> messageData, ref int currentPosition)
    {
        DnsMessageBase.EncodeByteArray(messageData, ref currentPosition, ClientCookie);
        DnsMessageBase.EncodeByteArray(messageData, ref currentPosition, ServerCookie);
    }
}

/// <summary>
///   <para>DNSSEC Algorithm Understood option</para>
///   <para>
///     Defined in
///     <see cref="!:http://tools.ietf.org/html/rfc6975">RFC 6975</see>
///   </para>
/// </summary>
public class DnssecAlgorithmUnderstoodOption : EDnsOptionBase
{
    /// <summary>
    ///   List of Algorithms
    /// </summary>
    public List<DnsSecAlgorithm> Algorithms { get; private set; }

    internal DnssecAlgorithmUnderstoodOption()
        : base(EDnsOptionType.DnssecAlgorithmUnderstood) { }

    /// <summary>
    ///   Creates a new instance of the DnssecAlgorithmUnderstoodOption class
    /// </summary>
    /// <param name="algorithms">The list of algorithms</param>
    public DnssecAlgorithmUnderstoodOption(List<DnsSecAlgorithm> algorithms)
        : this()
    {
        Algorithms = algorithms;
    }

    internal override void ParseData(ReadOnlySpan<byte> resultData, int startPosition, int length)
    {
        Algorithms = new List<DnsSecAlgorithm>(length);
        for (int i = 0; i < length; i++)
        {
            Algorithms.Add((DnsSecAlgorithm)resultData[startPosition++]);
        }
    }

    internal override ushort DataLength => (ushort)(Algorithms?.Count ?? 0);

    internal override void EncodeData(Span<byte> messageData, ref int currentPosition)
    {
        foreach (var algorithm in Algorithms)
        {
            messageData[currentPosition++] = (byte)algorithm;
        }
    }
}

/// <summary>
///   <para>DS Hash Understood option</para>
///   <para>
///     Defined in
///     <see cref="!:http://tools.ietf.org/html/rfc6975">RFC 6975</see>
///   </para>
/// </summary>
public class DsHashUnderstoodOption : EDnsOptionBase
{
    /// <summary>
    ///   List of Algorithms
    /// </summary>
    public List<DnsSecAlgorithm> Algorithms { get; private set; }

    internal DsHashUnderstoodOption()
        : base(EDnsOptionType.DsHashUnderstood) { }

    /// <summary>
    ///   Creates a new instance of the DsHashUnderstoodOption class
    /// </summary>
    /// <param name="algorithms">The list of algorithms</param>
    public DsHashUnderstoodOption(List<DnsSecAlgorithm> algorithms)
        : this()
    {
        Algorithms = algorithms;
    }

    internal override void ParseData(ReadOnlySpan<byte> resultData, int startPosition, int length)
    {
        Algorithms = new List<DnsSecAlgorithm>(length);
        for (int i = 0; i < length; i++)
        {
            Algorithms.Add((DnsSecAlgorithm)resultData[startPosition++]);
        }
    }

    internal override ushort DataLength => (ushort)(Algorithms?.Count ?? 0);

    internal override void EncodeData(Span<byte> messageData, ref int currentPosition)
    {
        foreach (var algorithm in Algorithms)
        {
            messageData[currentPosition++] = (byte)algorithm;
        }
    }
}

/// <summary>
///   Base class of EDNS options
/// </summary>
public abstract class EDnsOptionBase
{
    /// <summary>
    ///   Type of the option
    /// </summary>
    public EDnsOptionType Type { get; internal set; }

    internal EDnsOptionBase(EDnsOptionType optionType)
    {
        Type = optionType;
    }

    internal abstract void ParseData(ReadOnlySpan<byte> resultData, int startPosition, int length);
    internal abstract ushort DataLength { get; }
    internal abstract void EncodeData(Span<byte> messageData, ref int currentPosition);
}

/// <summary>
///   ENDS Option types
/// </summary>
public enum EDnsOptionType : ushort
{
    /// <summary>
    ///   <para>Update Lease</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://files.dns-sd.org/draft-sekar-dns-llq.txt">draft-sekar-dns-llq</see>
    ///   </para>
    /// </summary>
    LongLivedQuery = 1,

    /// <summary>
    ///   <para>Update Lease</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://files.dns-sd.org/draft-sekar-dns-ul.txt">draft-sekar-dns-ul</see>
    ///   </para>
    /// </summary>
    UpdateLease = 2,

    /// <summary>
    ///   <para>Name server ID</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc5001">RFC 5001</see>
    ///   </para>
    /// </summary>
    NsId = 3,

    /// <summary>
    ///   <para>Owner</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/draft-cheshire-edns0-owner-option">draft-cheshire-edns0-owner-option</see>
    ///   </para>
    /// </summary>
    Owner = 4,

    /// <summary>
    ///   <para>DNSSEC Algorithm Understood</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc6975">RFC 6975</see>
    ///   </para>
    /// </summary>
    DnssecAlgorithmUnderstood = 5,

    /// <summary>
    ///   <para>DS Hash Understood</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc6975">RFC 6975</see>
    ///   </para>
    /// </summary>
    DsHashUnderstood = 6,

    /// <summary>
    ///   <para>NSEC3 Hash Understood</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc6975">RFC 6975</see>
    ///   </para>
    /// </summary>
    Nsec3HashUnderstood = 7,

    /// <summary>
    ///   <para>ClientSubnet</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/draft-vandergaast-edns-client-subnet">draft-vandergaast-edns-client-subnet</see>
    ///   </para>
    /// </summary>
    ClientSubnet = 8,

    /// <summary>
    ///   <para>Expire EDNS Option</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc7314">RFC 7314</see>
    ///   </para>
    /// </summary>
    Expire = 9,

    /// <summary>
    ///   <para>Cookie Option</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/draft-ietf-dnsop-cookies">draft-ietf-dnsop-cookies</see>
    ///   </para>
    /// </summary>
    Cookie = 10,
}

/// <summary>
///   <para>Expire EDNS Option</para>
///   <para>
///     Defined in
///     <see cref="!:http://tools.ietf.org/html/rfc7314">RFC 7314</see>
///   </para>
/// </summary>
public class ExpireOption : EDnsOptionBase
{
    /// <summary>
    ///   The expiration of the SOA record in seconds. Should be null on queries.
    /// </summary>
    public int? SoaExpire { get; private set; }

    /// <summary>
    ///   Creates a new instance of the ExpireOption class
    /// </summary>
    public ExpireOption()
        : base(EDnsOptionType.Expire) { }

    /// <summary>
    ///   Creates a new instance of the ExpireOption class
    /// </summary>
    /// <param name="soaExpire">The expiration of the SOA record in seconds</param>
    public ExpireOption(int soaExpire)
        : this()
    {
        SoaExpire = soaExpire;
    }

    internal override void ParseData(ReadOnlySpan<byte> resultData, int startPosition, int length)
    {
        if (length == 4)
            SoaExpire = DnsMessageBase.ParseInt(resultData, ref startPosition);
    }

    internal override ushort DataLength => (ushort)(SoaExpire.HasValue ? 4 : 0);

    internal override void EncodeData(Span<byte> messageData, ref int currentPosition)
    {
        if (SoaExpire.HasValue)
            DnsMessageBase.EncodeInt(messageData, ref currentPosition, SoaExpire.Value);
    }
}

/// <summary>
///   <para>Long lived query option</para>
///   <para>
///     Defined in
///     <see cref="!:http://files.dns-sd.org/draft-sekar-dns-llq.txt">draft-sekar-dns-llq</see>
///   </para>
/// </summary>
public class LongLivedQueryOption : EDnsOptionBase
{
    /// <summary>
    ///   Long lived query operation codes
    /// </summary>
    public enum LlqOperationCode : ushort
    {
        /// <summary>
        ///   Setup a LLQ
        /// </summary>
        Setup = 1,

        /// <summary>
        ///   Refresh a LLQ
        /// </summary>
        Refresh = 2,

        /// <summary>
        ///   LLQ event
        /// </summary>
        Event = 3,
    }

    /// <summary>
    ///   Long lived query error codes
    /// </summary>
    public enum LlqErrorCode : ushort
    {
        /// <summary>
        ///   The LLQ Setup Request was successful.
        /// </summary>
        NoError = 0,

        /// <summary>
        ///   The server cannot grant the LLQ request because it is overloaded, or the request exceeds the server's rate limit.
        /// </summary>
        ServerFull = 1,

        /// <summary>
        ///   The data for this name and type is not expected to change frequently, and the server therefore does not support the
        ///   requested LLQ.
        /// </summary>
        Static = 2,

        /// <summary>
        ///   The LLQ was improperly formatted
        /// </summary>
        FormatError = 3,

        /// <summary>
        ///   The requested LLQ is expired or non-existent
        /// </summary>
        NoSuchLlq = 4,

        /// <summary>
        ///   The protocol version specified in the client's request is not supported by the server.
        /// </summary>
        BadVersion = 5,

        /// <summary>
        ///   The LLQ was not granted for an unknown reason.
        /// </summary>
        UnknownError = 6,
    }

    /// <summary>
    ///   Version of LLQ protocol implemented
    /// </summary>
    public ushort Version { get; private set; }

    /// <summary>
    ///   Identifies LLQ operation
    /// </summary>
    public LlqOperationCode OperationCode { get; private set; }

    /// <summary>
    ///   Identifies LLQ errors
    /// </summary>
    public LlqErrorCode ErrorCode { get; private set; }

    /// <summary>
    ///   Identifier for an LLQ
    /// </summary>
    public ulong Id { get; private set; }

    /// <summary>
    ///   Requested or granted life of LLQ
    /// </summary>
    public TimeSpan LeaseTime { get; private set; }

    internal LongLivedQueryOption()
        : base(EDnsOptionType.LongLivedQuery) { }

    /// <summary>
    ///   Creates a new instance of the LongLivedQueryOption class
    /// </summary>
    /// <param name="operationCode"> Identifies LLQ operation </param>
    /// <param name="errorCode"> Identifies LLQ errors </param>
    /// <param name="id"> Identifier for an LLQ </param>
    /// <param name="leaseTime"> Requested or granted life of LLQ </param>
    public LongLivedQueryOption(LlqOperationCode operationCode, LlqErrorCode errorCode, ulong id, TimeSpan leaseTime)
        : this(0, operationCode, errorCode, id, leaseTime) { }

    /// <summary>
    ///   Creates a new instance of the LongLivedQueryOption class
    /// </summary>
    /// <param name="version"> Version of LLQ protocol implemented </param>
    /// <param name="operationCode"> Identifies LLQ operation </param>
    /// <param name="errorCode"> Identifies LLQ errors </param>
    /// <param name="id"> Identifier for an LLQ </param>
    /// <param name="leaseTime"> Requested or granted life of LLQ </param>
    public LongLivedQueryOption(ushort version, LlqOperationCode operationCode, LlqErrorCode errorCode, ulong id, TimeSpan leaseTime)
        : this()
    {
        Version = version;
        OperationCode = operationCode;
        ErrorCode = errorCode;
        Id = id;
        LeaseTime = leaseTime;
    }

    internal override void ParseData(ReadOnlySpan<byte> resultData, int startPosition, int length)
    {
        Version = DnsMessageBase.ParseUShort(resultData, ref startPosition);
        OperationCode = (LlqOperationCode)DnsMessageBase.ParseUShort(resultData, ref startPosition);
        ErrorCode = (LlqErrorCode)DnsMessageBase.ParseUShort(resultData, ref startPosition);
        Id = DnsMessageBase.ParseULong(resultData, ref startPosition);
        LeaseTime = TimeSpan.FromSeconds(DnsMessageBase.ParseUInt(resultData, ref startPosition));
    }

    internal override ushort DataLength => 18;

    internal override void EncodeData(Span<byte> messageData, ref int currentPosition)
    {
        DnsMessageBase.EncodeUShort(messageData, ref currentPosition, Version);
        DnsMessageBase.EncodeUShort(messageData, ref currentPosition, (ushort)OperationCode);
        DnsMessageBase.EncodeUShort(messageData, ref currentPosition, (ushort)ErrorCode);
        DnsMessageBase.EncodeULong(messageData, ref currentPosition, Id);
        DnsMessageBase.EncodeUInt(messageData, ref currentPosition, (uint)LeaseTime.TotalSeconds);
    }
}

/// <summary>
///   <para>NSEC3 Hash Unterstood option</para>
///   <para>
///     Defined in
///     <see cref="!:http://tools.ietf.org/html/rfc6975">RFC 6975</see>
///   </para>
/// </summary>
public class Nsec3HashUnderstoodOption : EDnsOptionBase
{
    /// <summary>
    ///   List of Algorithms
    /// </summary>
    public List<DnsSecAlgorithm> Algorithms { get; private set; }

    internal Nsec3HashUnderstoodOption()
        : base(EDnsOptionType.Nsec3HashUnderstood) { }

    /// <summary>
    ///   Creates a new instance of the Nsec3HashUnderstoodOption class
    /// </summary>
    /// <param name="algorithms">The list of algorithms</param>
    public Nsec3HashUnderstoodOption(List<DnsSecAlgorithm> algorithms)
        : this()
    {
        Algorithms = algorithms;
    }

    internal override void ParseData(ReadOnlySpan<byte> resultData, int startPosition, int length)
    {
        Algorithms = new List<DnsSecAlgorithm>(length);
        for (int i = 0; i < length; i++)
        {
            Algorithms.Add((DnsSecAlgorithm)resultData[startPosition++]);
        }
    }

    internal override ushort DataLength => (ushort)(Algorithms?.Count ?? 0);

    internal override void EncodeData(Span<byte> messageData, ref int currentPosition)
    {
        foreach (var algorithm in Algorithms)
        {
            messageData[currentPosition++] = (byte)algorithm;
        }
    }
}

/// <summary>
///   <para>Name server ID option</para>
///   <para>
///     Defined in
///     <see cref="!:http://tools.ietf.org/html/rfc5001">RFC 5001</see>
///   </para>
/// </summary>
public class NsIdOption : EDnsOptionBase
{
    /// <summary>
    ///   Binary data of the payload
    /// </summary>
    public byte[] Payload { get; private set; }

    internal NsIdOption()
        : base(EDnsOptionType.NsId) { }

    /// <summary>
    ///   Creates a new instance of the NsIdOption class
    /// </summary>
    public NsIdOption(byte[] payload)
        : this()
    {
        Payload = payload;
    }

    internal override void ParseData(ReadOnlySpan<byte> resultData, int startPosition, int length)
    {
        Payload = DnsMessageBase.ParseByteData(resultData, ref startPosition, length);
    }

    internal override ushort DataLength => (ushort)(Payload?.Length ?? 0);

    internal override void EncodeData(Span<byte> messageData, ref int currentPosition)
    {
        DnsMessageBase.EncodeByteArray(messageData, ref currentPosition, Payload);
    }
}

/// <summary>
///   <para>OPT record</para>
///   <para>
///     Defined in
///     <see cref="!:http://tools.ietf.org/html/rfc2671">RFC 2671</see>
///   </para>
/// </summary>
public class OptRecord : DnsRecordBase
{
    /// <summary>
    ///   Gets or set the sender's UDP payload size
    /// </summary>
    public ushort UdpPayloadSize
    {
        get { return (ushort)RecordClass; }
        set { RecordClass = (RecordClass)value; }
    }

    /// <summary>
    ///   Gets or sets the high bits of return code (EXTENDED-RCODE)
    /// </summary>
    public ReturnCode ExtendedReturnCode
    {
        get { return (ReturnCode)((TimeToLive & 0xff000000) >> 20); }
        set
        {
            int clearedTtl = (TimeToLive & 0x00ffffff);
            TimeToLive = (clearedTtl | ((int)value << 20));
        }
    }

    /// <summary>
    ///   Gets or set the EDNS version
    /// </summary>
    public byte Version
    {
        get { return (byte)((TimeToLive & 0x00ff0000) >> 16); }
        set
        {
            int clearedTtl = (int)((uint)TimeToLive & 0xff00ffff);
            TimeToLive = clearedTtl | (value << 16);
        }
    }

    /// <summary>
    ///   <para>Gets or sets the DNSSEC OK (DO) flag</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc4035">RFC 4035</see>
    ///     and
    ///     <see cref="!:http://tools.ietf.org/html/rfc3225">RFC 3225</see>
    ///   </para>
    /// </summary>
    public bool IsDnsSecOk
    {
        get { return (TimeToLive & 0x8000) != 0; }
        set
        {
            if (value)
            {
                TimeToLive |= 0x8000;
            }
            else
            {
                TimeToLive &= 0x7fff;
            }
        }
    }

    /// <summary>
    ///   Gets or set additional EDNS options
    /// </summary>
    public List<EDnsOptionBase> Options { get; private set; }

    /// <summary>
    ///   Creates a new instance of the OptRecord
    /// </summary>
    public OptRecord()
        : base(DomainName.Root, RecordType.Opt, unchecked((RecordClass)512), 0)
    {
        UdpPayloadSize = 4096;
        Options = new List<EDnsOptionBase>();
    }

    internal override void ParseRecordData(ReadOnlySpan<byte> resultData, int startPosition, int length)
    {
        int endPosition = startPosition + length;

        Options = new List<EDnsOptionBase>();
        while (startPosition < endPosition)
        {
            EDnsOptionType type = (EDnsOptionType)DnsMessageBase.ParseUShort(resultData, ref startPosition);
            ushort dataLength = DnsMessageBase.ParseUShort(resultData, ref startPosition);

            EDnsOptionBase option;

            switch (type)
            {
                case EDnsOptionType.LongLivedQuery:
                    option = new LongLivedQueryOption();
                    break;

                case EDnsOptionType.UpdateLease:
                    option = new UpdateLeaseOption();
                    break;

                case EDnsOptionType.NsId:
                    option = new NsIdOption();
                    break;

                case EDnsOptionType.Owner:
                    option = new OwnerOption();
                    break;

                case EDnsOptionType.DnssecAlgorithmUnderstood:
                    option = new DnssecAlgorithmUnderstoodOption();
                    break;

                case EDnsOptionType.DsHashUnderstood:
                    option = new DsHashUnderstoodOption();
                    break;

                case EDnsOptionType.Nsec3HashUnderstood:
                    option = new Nsec3HashUnderstoodOption();
                    break;

                case EDnsOptionType.ClientSubnet:
                    option = new ClientSubnetOption();
                    break;

                case EDnsOptionType.Expire:
                    option = new ExpireOption();
                    break;

                case EDnsOptionType.Cookie:
                    option = new CookieOption();
                    break;

                default:
                    option = new UnknownOption(type);
                    break;
            }

            option.ParseData(resultData, startPosition, dataLength);
            Options.Add(option);
            startPosition += dataLength;
        }
    }

    internal override void ParseRecordData(DomainName origin, string[] stringRepresentation)
    {
        throw new NotSupportedException();
    }

    /// <summary>
    ///   Returns the textual representation of the OptRecord
    /// </summary>
    /// <returns> The textual representation </returns>
    public override string ToString()
    {
        return RecordDataToString();
    }

    internal override string RecordDataToString()
    {
        string flags = IsDnsSecOk ? "DO" : "";
        return String.Format("; EDNS version: {0}; flags: {1}; udp: {2}", Version, flags, UdpPayloadSize);
    }

    protected internal override int MaximumRecordDataLength
    {
        get
        {
            if ((Options == null) || (Options.Count == 0))
            {
                return 0;
            }
            else
            {
                return Options.Sum(option => option.DataLength + 4);
            }
        }
    }

    protected internal override void EncodeRecordData(Span<byte> messageData, int offset, ref int currentPosition, Dictionary<DomainName, ushort> domainNames, bool useCanonical)
    {
        if ((Options != null) && (Options.Count != 0))
        {
            foreach (EDnsOptionBase option in Options)
            {
                DnsMessageBase.EncodeUShort(messageData, ref currentPosition, (ushort)option.Type);
                DnsMessageBase.EncodeUShort(messageData, ref currentPosition, option.DataLength);
                option.EncodeData(messageData, ref currentPosition);
            }
        }
    }
}

/// <summary>
///   <para>EDNS0 Owner Option</para>
///   <para>
///     Defined in
///     <see cref="!:http://files.dns-sd.org/draft-sekar-dns-llq.txt">draft-cheshire-edns0-owner-option</see>
///   </para>
/// </summary>
public class OwnerOption : EDnsOptionBase
{
    /// <summary>
    ///   The version
    /// </summary>
    public byte Version { get; private set; }

    /// <summary>
    ///   The sequence number
    /// </summary>
    public byte Sequence { get; private set; }

    /// <summary>
    ///   The primary MAC address
    /// </summary>
    public PhysicalAddress PrimaryMacAddress { get; private set; }

    /// <summary>
    ///   The Wakeup MAC address
    /// </summary>
    public PhysicalAddress WakeupMacAddress { get; private set; }

    /// <summary>
    ///   The password, should be empty, 4 bytes long or 6 bytes long
    /// </summary>
    public byte[] Password { get; private set; }

    internal OwnerOption()
        : base(EDnsOptionType.Owner) { }

    /// <summary>
    ///   Creates a new instance of the OwnerOption class
    /// </summary>
    /// <param name="sequence"> The sequence number </param>
    /// <param name="primaryMacAddress"> The primary MAC address </param>
    public OwnerOption(byte sequence, PhysicalAddress primaryMacAddress)
        : this(0, sequence, primaryMacAddress, null) { }

    /// <summary>
    ///   Creates a new instance of the OwnerOption class
    /// </summary>
    /// <param name="version"> The version </param>
    /// <param name="sequence"> The sequence number </param>
    /// <param name="primaryMacAddress"> The primary MAC address </param>
    public OwnerOption(byte version, byte sequence, PhysicalAddress primaryMacAddress)
        : this(version, sequence, primaryMacAddress, null) { }

    /// <summary>
    ///   Creates a new instance of the OwnerOption class
    /// </summary>
    /// <param name="sequence"> The sequence number </param>
    /// <param name="primaryMacAddress"> The primary MAC address </param>
    /// <param name="wakeupMacAddress"> The wakeup MAC address </param>
    public OwnerOption(byte sequence, PhysicalAddress primaryMacAddress, PhysicalAddress wakeupMacAddress)
        : this(0, sequence, primaryMacAddress, wakeupMacAddress) { }

    /// <summary>
    ///   Creates a new instance of the OwnerOption class
    /// </summary>
    /// <param name="version"> The version </param>
    /// <param name="sequence"> The sequence number </param>
    /// <param name="primaryMacAddress"> The primary MAC address </param>
    /// <param name="wakeupMacAddress"> The wakeup MAC address </param>
    public OwnerOption(byte version, byte sequence, PhysicalAddress primaryMacAddress, PhysicalAddress wakeupMacAddress)
        : this(version, sequence, primaryMacAddress, wakeupMacAddress, null) { }

    /// <summary>
    ///   Creates a new instance of the OwnerOption class
    /// </summary>
    /// <param name="sequence"> The sequence number </param>
    /// <param name="primaryMacAddress"> The primary MAC address </param>
    /// <param name="wakeupMacAddress"> The wakeup MAC address </param>
    /// <param name="password"> The password, should be empty, 4 bytes long or 6 bytes long </param>
    public OwnerOption(byte sequence, PhysicalAddress primaryMacAddress, PhysicalAddress wakeupMacAddress, byte[] password)
        : this(0, sequence, primaryMacAddress, wakeupMacAddress, password) { }

    /// <summary>
    ///   Creates a new instance of the OwnerOption class
    /// </summary>
    /// <param name="version"> The version </param>
    /// <param name="sequence"> The sequence number </param>
    /// <param name="primaryMacAddress"> The primary MAC address </param>
    /// <param name="wakeupMacAddress"> The wakeup MAC address </param>
    /// <param name="password"> The password, should be empty, 4 bytes long or 6 bytes long </param>
    public OwnerOption(byte version, byte sequence, PhysicalAddress primaryMacAddress, PhysicalAddress wakeupMacAddress, byte[] password)
        : this()
    {
        Version = version;
        Sequence = sequence;
        PrimaryMacAddress = primaryMacAddress;
        WakeupMacAddress = wakeupMacAddress;
        Password = password;
    }

    internal override void ParseData(ReadOnlySpan<byte> resultData, int startPosition, int length)
    {
        Version = resultData[startPosition++];
        Sequence = resultData[startPosition++];
        PrimaryMacAddress = new PhysicalAddress(DnsMessageBase.ParseByteData(resultData, ref startPosition, 6));
        if (length > 8)
            WakeupMacAddress = new PhysicalAddress(DnsMessageBase.ParseByteData(resultData, ref startPosition, 6));
        if (length > 14)
            Password = DnsMessageBase.ParseByteData(resultData, ref startPosition, length - 14);
    }

    internal override ushort DataLength => (ushort)(8 + (WakeupMacAddress != null ? 6 : 0) + (Password?.Length ?? 0));

    internal override void EncodeData(Span<byte> messageData, ref int currentPosition)
    {
        messageData[currentPosition++] = Version;
        messageData[currentPosition++] = Sequence;
        DnsMessageBase.EncodeByteArray(messageData, ref currentPosition, PrimaryMacAddress.GetAddressBytes());
        if (WakeupMacAddress != null)
            DnsMessageBase.EncodeByteArray(messageData, ref currentPosition, WakeupMacAddress.GetAddressBytes());
        DnsMessageBase.EncodeByteArray(messageData, ref currentPosition, Password);
    }
}

/// <summary>
///   Unknown EDNS option
/// </summary>
public class UnknownOption : EDnsOptionBase
{
    /// <summary>
    ///   Binary data of the option
    /// </summary>
    public byte[] Data { get; private set; }

    internal UnknownOption(EDnsOptionType type)
        : base(type) { }

    /// <summary>
    ///   Creates a new instance of the UnknownOption class
    /// </summary>
    /// <param name="type">Type of the option</param>
    /// <param name="data">The data of the option</param>
    public UnknownOption(EDnsOptionType type, byte[] data)
        : this(type)
    {
        Data = data;
    }

    internal override void ParseData(ReadOnlySpan<byte> resultData, int startPosition, int length)
    {
        Data = DnsMessageBase.ParseByteData(resultData, ref startPosition, length);
    }

    internal override ushort DataLength => (ushort)(Data?.Length ?? 0);

    internal override void EncodeData(Span<byte> messageData, ref int currentPosition)
    {
        DnsMessageBase.EncodeByteArray(messageData, ref currentPosition, Data);
    }
}

/// <summary>
///   <para>Update lease option</para>
///   <para>
///     Defined in
///     <see cref="!:http://files.dns-sd.org/draft-sekar-dns-ul.txt">draft-sekar-dns-ul</see>
///   </para>
/// </summary>
public class UpdateLeaseOption : EDnsOptionBase
{
    /// <summary>
    ///   Desired lease (request) or granted lease (response)
    /// </summary>
    public TimeSpan LeaseTime { get; private set; }

    internal UpdateLeaseOption()
        : base(EDnsOptionType.UpdateLease) { }

    /// <summary>
    ///   Creates a new instance of the UpdateLeaseOption class
    /// </summary>
    public UpdateLeaseOption(TimeSpan leaseTime)
        : this()
    {
        LeaseTime = leaseTime;
    }

    internal override void ParseData(ReadOnlySpan<byte> resultData, int startPosition, int length)
    {
        LeaseTime = TimeSpan.FromSeconds(DnsMessageBase.ParseInt(resultData, ref startPosition));
    }

    internal override ushort DataLength => 4;

    internal override void EncodeData(Span<byte> messageData, ref int currentPosition)
    {
        DnsMessageBase.EncodeInt(messageData, ref currentPosition, (int)LeaseTime.TotalSeconds);
    }
}


#endif
