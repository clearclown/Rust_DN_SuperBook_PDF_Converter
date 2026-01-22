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
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

using X509Certificate = System.Security.Cryptography.X509Certificates.X509Certificate;

namespace IPA.Cores.Basic.DnsLib;

/// <summary>
///   <para>IPv6 address</para>
///   <para>
///     Defined in
///     <see cref="!:http://tools.ietf.org/html/rfc3596">RFC 3596</see>
///   </para>
/// </summary>
public class AaaaRecord : AddressRecordBase
{
    internal AaaaRecord() { }

    /// <summary>
    ///   Creates a new instance of the AaaaRecord class
    /// </summary>
    /// <param name="name"> Domain name of the host </param>
    /// <param name="timeToLive"> Seconds the record should be cached at most </param>
    /// <param name="address"> IP address of the host </param>
    public AaaaRecord(DomainName name, int timeToLive, IPAddress address)
        : base(name, RecordType.Aaaa, timeToLive, address ?? IPAddress.IPv6None) { }

    protected internal override int MaximumRecordDataLength => 16;
}

/// <summary>
///   Base record class for storing host to ip allocation (ARecord and AaaaRecord)
/// </summary>
public abstract class AddressRecordBase : DnsRecordBase, IAddressRecord
{
    /// <summary>
    ///   IP address of the host
    /// </summary>
    public IPAddress Address { get; set; }

    protected AddressRecordBase() { }

    protected AddressRecordBase(DomainName name, RecordType recordType, int timeToLive, IPAddress address)
        : base(name, recordType, RecordClass.INet, timeToLive)
    {
        Address = address;
    }

    internal override void ParseRecordData(ReadOnlySpan<byte> resultData, int startPosition, int length)
    {
        Address = new IPAddress(DnsMessageBase.ParseByteData(resultData, ref startPosition, MaximumRecordDataLength));
    }

    internal override void ParseRecordData(DomainName origin, string[] stringRepresentation)
    {
        if (stringRepresentation.Length != 1)
            throw new FormatException();

        Address = IPAddress.Parse(stringRepresentation[0]);
    }

    internal override string RecordDataToString()
    {
        return Address.ToString();
    }

    protected internal override void EncodeRecordData(Span<byte> messageData, int offset, ref int currentPosition, Dictionary<DomainName, ushort> domainNames, bool useCanonical)
    {
        DnsMessageBase.EncodeByteArray(messageData, ref currentPosition, Address.GetAddressBytes());
    }
}

/// <summary>
///   <para>AFS data base location</para>
///   <para>
///     Defined in
///     <see cref="!:http://tools.ietf.org/html/rfc1183">RFC 1183</see>
///     and
///     <see cref="!:http://tools.ietf.org/html/rfc5864">RFC 5864</see>
///   </para>
/// </summary>
public class AfsdbRecord : DnsRecordBase
{
    /// <summary>
    ///   AFS database subtype
    /// </summary>
    public enum AfsSubType : ushort
    {
        /// <summary>
        ///   <para>Andrews File Service v3.0 Location service</para>
        ///   <para>
        ///     Defined in
        ///     <see cref="!:http://tools.ietf.org/html/rfc1183">RFC 1183</see>
        ///   </para>
        /// </summary>
        Afs = 1,

        /// <summary>
        ///   <para>DCE/NCA root cell directory node</para>
        ///   <para>
        ///     Defined in
        ///     <see cref="!:http://tools.ietf.org/html/rfc1183">RFC 1183</see>
        ///   </para>
        /// </summary>
        Dce = 2,
    }

    /// <summary>
    ///   Subtype of the record
    /// </summary>
    public AfsSubType SubType { get; private set; }

    /// <summary>
    ///   Hostname of the AFS database
    /// </summary>
    public DomainName Hostname { get; private set; }

    internal AfsdbRecord() { }

    /// <summary>
    ///   Creates a new instance of the AfsdbRecord class
    /// </summary>
    /// <param name="name"> Name of the record </param>
    /// <param name="timeToLive"> Seconds the record should be cached at most </param>
    /// <param name="subType"> Subtype of the record </param>
    /// <param name="hostname"> Hostname of the AFS database </param>
    public AfsdbRecord(DomainName name, int timeToLive, AfsSubType subType, DomainName hostname)
        : base(name, RecordType.Afsdb, RecordClass.INet, timeToLive)
    {
        SubType = subType;
        Hostname = hostname ?? DomainName.Root;
    }

    internal override void ParseRecordData(ReadOnlySpan<byte> resultData, int startPosition, int length)
    {
        SubType = (AfsSubType)DnsMessageBase.ParseUShort(resultData, ref startPosition);
        Hostname = DnsMessageBase.ParseDomainName(resultData, ref startPosition);
    }

    internal override void ParseRecordData(DomainName origin, string[] stringRepresentation)
    {
        if (stringRepresentation.Length != 2)
            throw new FormatException();

        SubType = (AfsSubType)Byte.Parse(stringRepresentation[0]);
        Hostname = ParseDomainName(origin, stringRepresentation[1]);
    }

    internal override string RecordDataToString()
    {
        return (byte)SubType
               + " " + Hostname;
    }

    protected internal override int MaximumRecordDataLength => Hostname.MaximumRecordDataLength + 4;

    protected internal override void EncodeRecordData(Span<byte> messageData, int offset, ref int currentPosition, Dictionary<DomainName, ushort> domainNames, bool useCanonical)
    {
        DnsMessageBase.EncodeUShort(messageData, ref currentPosition, (ushort)SubType);
        DnsMessageBase.EncodeDomainName(messageData, offset, ref currentPosition, Hostname, null, useCanonical);
    }
}

/// <summary>
///   <para>Address prefixes record</para>
///   <para>
///     Defined in
///     <see cref="!:http://tools.ietf.org/html/rfc3123">RFC 3123</see>
///   </para>
/// </summary>
public class AplRecord : DnsRecordBase
{
    internal enum Family : ushort
    {
        /// <summary>
        ///   <para>IPv4</para>
        ///   <para>
        ///     Defined in
        ///     <see cref="!:http://tools.ietf.org/html/rfc3123">RFC 3123</see>
        ///   </para>
        /// </summary>
        IpV4 = 1,

        /// <summary>
        ///   <para>IPv6</para>
        ///   <para>
        ///     Defined in
        ///     <see cref="!:http://tools.ietf.org/html/rfc3123">RFC 3123</see>
        ///   </para>
        /// </summary>
        IpV6 = 2,
    }

    /// <summary>
    ///   Represents an address prefix
    /// </summary>
    public class AddressPrefix
    {
        private static readonly Regex _parserRegex = new Regex(@"^(?<isneg>!?)(?<fam>(1|2)):(?<addr>[^/]+)/(?<pref>\d+)$", RegexOptions.ExplicitCapture | RegexOptions.Compiled);

        /// <summary>
        ///   Is negated prefix
        /// </summary>
        public bool IsNegated { get; }

        /// <summary>
        ///   Address familiy
        /// </summary>
        internal Family AddressFamily { get; }

        /// <summary>
        ///   Network address
        /// </summary>
        public IPAddress Address { get; }

        /// <summary>
        ///   Prefix of the network
        /// </summary>
        public byte Prefix { get; }

        /// <summary>
        ///   Creates a new instance of the AddressPrefix class
        /// </summary>
        /// <param name="isNegated"> Is negated prefix </param>
        /// <param name="address"> Network address </param>
        /// <param name="prefix"> Prefix of the network </param>
        public AddressPrefix(bool isNegated, IPAddress address, byte prefix)
        {
            IsNegated = isNegated;
            AddressFamily = (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork) ? Family.IpV4 : Family.IpV6;
            Address = address;
            Prefix = prefix;
        }

        /// <summary>
        ///   Returns the textual representation of an address prefix
        /// </summary>
        /// <returns> The textual representation </returns>
        public override string ToString()
        {
            return (IsNegated ? "!" : "")
                   + (ushort)AddressFamily
                   + ":" + Address
                   + "/" + Prefix;
        }

        internal static AddressPrefix Parse(string s)
        {
            var groups = _parserRegex.Match(s).Groups;

            IPAddress address = IPAddress.Parse(groups["addr"].Value);

            if ((address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork) && (groups["fam"].Value != "1"))
                throw new FormatException();

            return new AddressPrefix(groups["isneg"].Success, address, Byte.Parse(groups["pref"].Value));
        }
    }

    /// <summary>
    ///   List of address prefixes covered by this record
    /// </summary>
    public List<AddressPrefix> Prefixes { get; private set; }

    internal AplRecord() { }

    /// <summary>
    ///   Creates a new instance of the AplRecord class
    /// </summary>
    /// <param name="name"> Name of the record </param>
    /// <param name="timeToLive"> Seconds the record should be cached at most </param>
    /// <param name="prefixes"> List of address prefixes covered by this record </param>
    public AplRecord(DomainName name, int timeToLive, List<AddressPrefix> prefixes)
        : base(name, RecordType.Apl, RecordClass.INet, timeToLive)
    {
        Prefixes = prefixes ?? new List<AddressPrefix>();
    }

    internal override void ParseRecordData(ReadOnlySpan<byte> resultData, int currentPosition, int length)
    {
        int endPosition = currentPosition + length;

        Prefixes = new List<AddressPrefix>();
        while (currentPosition < endPosition)
        {
            Family family = (Family)DnsMessageBase.ParseUShort(resultData, ref currentPosition);
            byte prefix = resultData[currentPosition++];

            byte addressLength = resultData[currentPosition++];
            bool isNegated = false;
            if (addressLength > 127)
            {
                isNegated = true;
                addressLength -= 128;
            }

            byte[] addressData = new byte[(family == Family.IpV4) ? 4 : 16];
            Util.BlockCopy(resultData, currentPosition, addressData, 0, addressLength);
            currentPosition += addressLength;

            Prefixes.Add(new AddressPrefix(isNegated, new IPAddress(addressData), prefix));
        }
    }

    internal override void ParseRecordData(DomainName origin, string[] stringRepresentation)
    {
        if (stringRepresentation.Length == 0)
            throw new FormatException();

        Prefixes = stringRepresentation.Select(AddressPrefix.Parse).ToList();
    }

    internal override string RecordDataToString()
    {
        return String.Join(" ", Prefixes.Select(p => p.ToString()));
    }

    protected internal override int MaximumRecordDataLength => Prefixes.Count * 20;

    protected internal override void EncodeRecordData(Span<byte> messageData, int offset, ref int currentPosition, Dictionary<DomainName, ushort> domainNames, bool useCanonical)
    {
        foreach (AddressPrefix addressPrefix in Prefixes)
        {
            DnsMessageBase.EncodeUShort(messageData, ref currentPosition, (ushort)addressPrefix.AddressFamily);
            messageData[currentPosition++] = addressPrefix.Prefix;

            // no increment of position pointer, just set 1 bit
            if (addressPrefix.IsNegated)
                messageData[currentPosition] = 128;

            byte[] addressData = addressPrefix.Address.GetNetworkAddress(addressPrefix.Prefix).GetAddressBytes();
            int length = addressData.Length;
            for (; length > 0; length--)
            {
                if (addressData[length - 1] != 0)
                    break;
            }
            messageData[currentPosition++] |= (byte)length;
            DnsMessageBase.EncodeByteArray(messageData, ref currentPosition, addressData, length);
        }
    }
}

/// <summary>
///   <para>Host address record</para>
///   <para>
///     Defined in
///     <see cref="!:http://tools.ietf.org/html/rfc1035">RFC 1035</see>
///   </para>
/// </summary>
public class ARecord : AddressRecordBase
{
    internal ARecord() { }

    /// <summary>
    ///   Creates a new instance of the ARecord class
    /// </summary>
    /// <param name="name"> Domain name of the host </param>
    /// <param name="timeToLive"> Seconds the record should be cached at most </param>
    /// <param name="address"> IP address of the host </param>
    public ARecord(DomainName name, int timeToLive, IPAddress address)
        : base(name, RecordType.A, timeToLive, address ?? IPAddress.None) { }

    protected internal override int MaximumRecordDataLength => 4;
}

/// <summary>
///   <para>CAA</para>
///   <para>
///     Defined in
///     <see cref="!:http://tools.ietf.org/html/rfc6844">RFC 6844</see>
///   </para>
/// </summary>
// ReSharper disable once InconsistentNaming
public class CAARecord : DnsRecordBase
{
    /// <summary>
    ///   The flags
    /// </summary>
    public byte Flags { get; private set; }

    /// <summary>
    ///   The name of the tag
    /// </summary>
    public string Tag { get; private set; }

    /// <summary>
    ///   The value of the tag
    /// </summary>
    public string Value { get; private set; }

    internal CAARecord() { }

    /// <summary>
    ///   Creates a new instance of the CAARecord class
    /// </summary>
    /// <param name="name"> Name of the zone </param>
    /// <param name="timeToLive"> Seconds the record should be cached at most </param>
    /// <param name="flags">The flags</param>
    /// <param name="tag">The name of the tag</param>
    /// <param name="value">The value of the tag</param>
    public CAARecord(DomainName name, int timeToLive, byte flags, string tag, string value)
        : base(name, RecordType.CAA, RecordClass.INet, timeToLive)
    {
        Flags = flags;
        Tag = tag;
        Value = value;
    }

    internal override void ParseRecordData(ReadOnlySpan<byte> resultData, int startPosition, int length)
    {
        Flags = resultData[startPosition++];
        Tag = DnsMessageBase.ParseText(resultData, ref startPosition);
        Value = DnsMessageBase.ParseText(resultData, ref startPosition, length - (2 + Tag.Length));
    }

    internal override void ParseRecordData(DomainName origin, string[] stringRepresentation)
    {
        if (stringRepresentation.Length != 3)
            throw new FormatException();

        Flags = Byte.Parse(stringRepresentation[0]);
        Tag = stringRepresentation[1];
        Value = stringRepresentation[2];
    }

    internal override string RecordDataToString()
    {
        return Flags + " " + Tag.ToMasterfileLabelRepresentation() + " " + Value.ToMasterfileLabelRepresentation();
    }

    protected internal override int MaximumRecordDataLength => 2 + Tag.Length + Value.Length;

    protected internal override void EncodeRecordData(Span<byte> messageData, int offset, ref int currentPosition, Dictionary<DomainName, ushort> domainNames, bool useCanonical)
    {
        messageData[currentPosition++] = Flags;
        DnsMessageBase.EncodeTextBlock(messageData, ref currentPosition, Tag);
        DnsMessageBase.EncodeTextWithoutLength(messageData, ref currentPosition, Value);
    }
}

/// <summary>
///   <para>Child DNS Key record</para>
///   <para>
///     Defined in
///     <see cref="!:http://tools.ietf.org/html/rfc7344">RFC 7344</see>
///   </para>
/// </summary>
public class CDnsKeyRecord : DnsRecordBase
{
    /// <summary>
    ///   Flags of the key
    /// </summary>
    public DnsKeyFlags Flags { get; private set; }

    /// <summary>
    ///   Protocol field
    /// </summary>
    public byte Protocol { get; private set; }

    /// <summary>
    ///   Algorithm of the key
    /// </summary>
    public DnsSecAlgorithm Algorithm { get; private set; }

    /// <summary>
    ///   Binary data of the public key
    /// </summary>
    public byte[] PublicKey { get; private set; }

    /// <summary>
    ///   Binary data of the private key
    /// </summary>
    public byte[] PrivateKey { get; private set; }

    /// <summary>
    ///   <para>Record holds a DNS zone key</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc4034">RFC 4034</see>
    ///     and
    ///     <see cref="!:http://tools.ietf.org/html/rfc3757">RFC 3757</see>
    ///   </para>
    /// </summary>
    public bool IsZoneKey
    {
        get { return (Flags & DnsKeyFlags.Zone) == DnsKeyFlags.Zone; }
        set
        {
            if (value)
            {
                Flags |= DnsKeyFlags.Zone;
            }
            else
            {
                Flags &= ~DnsKeyFlags.Zone;
            }
        }
    }

    /// <summary>
    ///   <para>Key is intended for use as a secure entry point</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc4034">RFC 4034</see>
    ///     and
    ///     <see cref="!:http://tools.ietf.org/html/rfc3757">RFC 3757</see>
    ///   </para>
    /// </summary>
    public bool IsSecureEntryPoint
    {
        get { return (Flags & DnsKeyFlags.SecureEntryPoint) == DnsKeyFlags.SecureEntryPoint; }
        set
        {
            if (value)
            {
                Flags |= DnsKeyFlags.SecureEntryPoint;
            }
            else
            {
                Flags &= ~DnsKeyFlags.SecureEntryPoint;
            }
        }
    }

    /// <summary>
    ///   <para>Key is intended for use as a secure entry point</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc5011">RFC 5011</see>
    ///   </para>
    /// </summary>
    public bool IsRevoked
    {
        get { return (Flags & DnsKeyFlags.Revoke) == DnsKeyFlags.Revoke; }
        set
        {
            if (value)
            {
                Flags |= DnsKeyFlags.Revoke;
            }
            else
            {
                Flags &= ~DnsKeyFlags.Revoke;
            }
        }
    }

    /// <summary>
    ///   <para>Calculates the key tag</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc4034">RFC 4034</see>
    ///   </para>
    /// </summary>
    /// <returns></returns>
    public ushort CalculateKeyTag()
    {
        if (Algorithm == DnsSecAlgorithm.RsaMd5)
            return (ushort)(PublicKey[PublicKey.Length - 4] & PublicKey[PublicKey.Length - 3] << 8);

        byte[] buffer = new byte[MaximumRecordDataLength];
        int currentPosition = 0;
        EncodeRecordData(buffer, 0, ref currentPosition, null, false);

        ulong ac = 0;

        for (int i = 0; i < currentPosition; ++i)
        {
            ac += ((i & 1) == 1) ? buffer[i] : (ulong)buffer[i] << 8;
        }

        ac += (ac >> 16) & 0xFFFF;

        ushort res = (ushort)(ac & 0xffff);

        return res;
    }

    internal CDnsKeyRecord() { }

    /// <summary>
    ///   Creates a new instance of the DnsKeyRecord class
    /// </summary>
    /// <param name="name"> Name of the record </param>
    /// <param name="recordClass"> Class of the record </param>
    /// <param name="timeToLive"> Seconds the record should be cached at most </param>
    /// <param name="flags"> Flags of the key </param>
    /// <param name="protocol"> Protocol field </param>
    /// <param name="algorithm"> Algorithm of the key </param>
    /// <param name="publicKey"> Binary data of the public key </param>
    public CDnsKeyRecord(DomainName name, RecordClass recordClass, int timeToLive, DnsKeyFlags flags, byte protocol, DnsSecAlgorithm algorithm, byte[] publicKey)
        : this(name, recordClass, timeToLive, flags, protocol, algorithm, publicKey, null) { }

    /// <summary>
    ///   Creates a new instance of the DnsKeyRecord class
    /// </summary>
    /// <param name="name"> Name of the record </param>
    /// <param name="recordClass"> Class of the record </param>
    /// <param name="timeToLive"> Seconds the record should be cached at most </param>
    /// <param name="flags"> Flags of the key </param>
    /// <param name="protocol"> Protocol field </param>
    /// <param name="algorithm"> Algorithm of the key </param>
    /// <param name="publicKey"> Binary data of the public key </param>
    /// <param name="privateKey"> Binary data of the private key </param>
    public CDnsKeyRecord(DomainName name, RecordClass recordClass, int timeToLive, DnsKeyFlags flags, byte protocol, DnsSecAlgorithm algorithm, byte[] publicKey, byte[] privateKey)
        : base(name, RecordType.CDnsKey, recordClass, timeToLive)
    {
        Flags = flags;
        Protocol = protocol;
        Algorithm = algorithm;
        PublicKey = publicKey;
        PrivateKey = privateKey;
    }

    internal override void ParseRecordData(ReadOnlySpan<byte> resultData, int startPosition, int length)
    {
        Flags = (DnsKeyFlags)DnsMessageBase.ParseUShort(resultData, ref startPosition);
        Protocol = resultData[startPosition++];
        Algorithm = (DnsSecAlgorithm)resultData[startPosition++];
        PublicKey = DnsMessageBase.ParseByteData(resultData, ref startPosition, length - 4);
    }

    internal override void ParseRecordData(DomainName origin, string[] stringRepresentation)
    {
        if (stringRepresentation.Length < 4)
            throw new FormatException();

        Flags = (DnsKeyFlags)UInt16.Parse(stringRepresentation[0]);
        Protocol = Byte.Parse(stringRepresentation[1]);
        Algorithm = (DnsSecAlgorithm)Byte.Parse(stringRepresentation[2]);
        PublicKey = String.Join(String.Empty, stringRepresentation.Skip(3)).FromBase64String();
    }

    internal override string RecordDataToString()
    {
        return (ushort)Flags
               + " " + Protocol
               + " " + (byte)Algorithm
               + " " + PublicKey.ToBase64String();
    }

    protected internal override int MaximumRecordDataLength => 4 + PublicKey.Length;

    protected internal override void EncodeRecordData(Span<byte> messageData, int offset, ref int currentPosition, Dictionary<DomainName, ushort> domainNames, bool useCanonical)
    {
        DnsMessageBase.EncodeUShort(messageData, ref currentPosition, (ushort)Flags);
        messageData[currentPosition++] = Protocol;
        messageData[currentPosition++] = (byte)Algorithm;
        DnsMessageBase.EncodeByteArray(messageData, ref currentPosition, PublicKey);
    }
}

/// <summary>
///   <para>Child Delegation signer</para>
///   <para>
///     Defined in
///     <see cref="!:http://tools.ietf.org/html/rfc7344">RFC 7344</see>
///   </para>
/// </summary>
public class CDsRecord : DnsRecordBase
{
    /// <summary>
    ///   Key tag
    /// </summary>
    public ushort KeyTag { get; private set; }

    /// <summary>
    ///   Algorithm used
    /// </summary>
    public DnsSecAlgorithm Algorithm { get; private set; }

    /// <summary>
    ///   Type of the digest
    /// </summary>
    public DnsSecDigestType DigestType { get; private set; }

    /// <summary>
    ///   Binary data of the digest
    /// </summary>
    public byte[] Digest { get; private set; }

    internal CDsRecord() { }

    /// <summary>
    ///   Creates a new instance of the CDsRecord class
    /// </summary>
    /// <param name="name"> Name of the record </param>
    /// <param name="recordClass"> Class of the record </param>
    /// <param name="timeToLive"> Seconds the record should be cached at most </param>
    /// <param name="keyTag"> Key tag </param>
    /// <param name="algorithm"> Algorithm used </param>
    /// <param name="digestType"> Type of the digest </param>
    /// <param name="digest"> Binary data of the digest </param>
    public CDsRecord(DomainName name, RecordClass recordClass, int timeToLive, ushort keyTag, DnsSecAlgorithm algorithm, DnsSecDigestType digestType, byte[] digest)
        : base(name, RecordType.Ds, recordClass, timeToLive)
    {
        KeyTag = keyTag;
        Algorithm = algorithm;
        DigestType = digestType;
        Digest = digest ?? new byte[] { };
    }

    internal override void ParseRecordData(ReadOnlySpan<byte> resultData, int startPosition, int length)
    {
        KeyTag = DnsMessageBase.ParseUShort(resultData, ref startPosition);
        Algorithm = (DnsSecAlgorithm)resultData[startPosition++];
        DigestType = (DnsSecDigestType)resultData[startPosition++];
        Digest = DnsMessageBase.ParseByteData(resultData, ref startPosition, length - 4);
    }

    internal override void ParseRecordData(DomainName origin, string[] stringRepresentation)
    {
        if (stringRepresentation.Length < 4)
            throw new FormatException();

        KeyTag = UInt16.Parse(stringRepresentation[0]);
        Algorithm = (DnsSecAlgorithm)Byte.Parse(stringRepresentation[1]);
        DigestType = (DnsSecDigestType)Byte.Parse(stringRepresentation[2]);
        Digest = String.Join(String.Empty, stringRepresentation.Skip(3)).FromBase16String();
    }

    internal override string RecordDataToString()
    {
        return KeyTag
               + " " + (byte)Algorithm
               + " " + (byte)DigestType
               + " " + Digest.ToBase16String();
    }

    protected internal override int MaximumRecordDataLength => 4 + Digest.Length;

    protected internal override void EncodeRecordData(Span<byte> messageData, int offset, ref int currentPosition, Dictionary<DomainName, ushort> domainNames, bool useCanonical)
    {
        DnsMessageBase.EncodeUShort(messageData, ref currentPosition, KeyTag);
        messageData[currentPosition++] = (byte)Algorithm;
        messageData[currentPosition++] = (byte)DigestType;
        DnsMessageBase.EncodeByteArray(messageData, ref currentPosition, Digest);
    }
}

/// <summary>
///   <para>Certificate storage record</para>
///   <para>
///     Defined in
///     <see cref="!:http://tools.ietf.org/html/rfc4398">RFC 4398</see>
///   </para>
/// </summary>
public class CertRecord : DnsRecordBase
{
    /// <summary>
    ///   Type of cert
    /// </summary>
    public enum CertType : ushort
    {
        /// <summary>
        ///   None
        /// </summary>
        None = 0,

        /// <summary>
        ///   <para>X.509 as per PKIX</para>
        ///   <para>
        ///     Defined in
        ///     <see cref="!:http://tools.ietf.org/html/rfc4398">RFC 4398</see>
        ///   </para>
        /// </summary>
        Pkix = 1,

        /// <summary>
        ///   <para>SPKI certificate</para>
        ///   <para>
        ///     Defined in
        ///     <see cref="!:http://tools.ietf.org/html/rfc4398">RFC 4398</see>
        ///   </para>
        /// </summary>
        Spki = 2,

        /// <summary>
        ///   <para>OpenPGP packet</para>
        ///   <para>
        ///     Defined in
        ///     <see cref="!:http://tools.ietf.org/html/rfc4398">RFC 4398</see>
        ///   </para>
        /// </summary>
        Pgp = 3,

        /// <summary>
        ///   <para>The URL of an X.509 data object</para>
        ///   <para>
        ///     Defined in
        ///     <see cref="!:http://tools.ietf.org/html/rfc4398">RFC 4398</see>
        ///   </para>
        /// </summary>
        // ReSharper disable once InconsistentNaming
        IPkix = 4,

        /// <summary>
        ///   <para>The URL of an SPKI certificate</para>
        ///   <para>
        ///     Defined in
        ///     <see cref="!:http://tools.ietf.org/html/rfc4398">RFC 4398</see>
        ///   </para>
        /// </summary>
        // ReSharper disable once InconsistentNaming
        ISpki = 5,

        /// <summary>
        ///   <para>The fingerprint and URL of an OpenPGP packet</para>
        ///   <para>
        ///     Defined in
        ///     <see cref="!:http://tools.ietf.org/html/rfc4398">RFC 4398</see>
        ///   </para>
        /// </summary>
        // ReSharper disable once InconsistentNaming
        IPgp = 6,

        /// <summary>
        ///   <para>Attribute Certificate</para>
        ///   <para>
        ///     Defined in
        ///     <see cref="!:http://tools.ietf.org/html/rfc4398">RFC 4398</see>
        ///   </para>
        /// </summary>
        Acpkix = 7,

        /// <summary>
        ///   <para>The URL of an Attribute Certificate</para>
        ///   <para>
        ///     Defined in
        ///     <see cref="!:http://tools.ietf.org/html/rfc4398">RFC 4398</see>
        ///   </para>
        /// </summary>
        // ReSharper disable once InconsistentNaming
        IAcpkix = 8,

        /// <summary>
        ///   <para>URI private</para>
        ///   <para>
        ///     Defined in
        ///     <see cref="!:http://tools.ietf.org/html/rfc4398">RFC 4398</see>
        ///   </para>
        /// </summary>
        Uri = 253,

        /// <summary>
        ///   <para>OID private</para>
        ///   <para>
        ///     Defined in
        ///     <see cref="!:http://tools.ietf.org/html/rfc4398">RFC 4398</see>
        ///   </para>
        /// </summary>
        Oid = 254,
    }

    /// <summary>
    ///   Type of the certificate data
    /// </summary>
    public CertType Type { get; private set; }

    /// <summary>
    ///   Key tag
    /// </summary>
    public ushort KeyTag { get; private set; }

    /// <summary>
    ///   Algorithm of the certificate
    /// </summary>
    public DnsSecAlgorithm Algorithm { get; private set; }

    /// <summary>
    ///   Binary data of the certificate
    /// </summary>
    public byte[] Certificate { get; private set; }

    internal CertRecord() { }

    /// <summary>
    ///   Creates a new instace of the CertRecord class
    /// </summary>
    /// <param name="name"> Name of the record </param>
    /// <param name="timeToLive"> Seconds the record should be cached at most </param>
    /// <param name="type"> Type of the certificate data </param>
    /// <param name="keyTag"> Key tag </param>
    /// <param name="algorithm"> Algorithm of the certificate </param>
    /// <param name="certificate"> Binary data of the certificate </param>
    public CertRecord(DomainName name, int timeToLive, CertType type, ushort keyTag, DnsSecAlgorithm algorithm, byte[] certificate)
        : base(name, RecordType.Cert, RecordClass.INet, timeToLive)
    {
        Type = type;
        KeyTag = keyTag;
        Algorithm = algorithm;
        Certificate = certificate ?? new byte[] { };
    }

    internal override void ParseRecordData(ReadOnlySpan<byte> resultData, int startPosition, int length)
    {
        Type = (CertType)DnsMessageBase.ParseUShort(resultData, ref startPosition);
        KeyTag = DnsMessageBase.ParseUShort(resultData, ref startPosition);
        Algorithm = (DnsSecAlgorithm)resultData[startPosition++];
        Certificate = DnsMessageBase.ParseByteData(resultData, ref startPosition, length - 5);
    }

    internal override void ParseRecordData(DomainName origin, string[] stringRepresentation)
    {
        if (stringRepresentation.Length < 4)
            throw new FormatException();

        Type = (CertType)UInt16.Parse(stringRepresentation[0]);
        KeyTag = UInt16.Parse(stringRepresentation[1]);
        Algorithm = (DnsSecAlgorithm)Byte.Parse(stringRepresentation[2]);
        Certificate = String.Join(String.Empty, stringRepresentation.Skip(3)).FromBase64String();
    }

    internal override string RecordDataToString()
    {
        return (ushort)Type
               + " " + KeyTag
               + " " + (byte)Algorithm
               + " " + Certificate.ToBase64String();
    }

    protected internal override int MaximumRecordDataLength => 5 + Certificate.Length;

    protected internal override void EncodeRecordData(Span<byte> messageData, int offset, ref int currentPosition, Dictionary<DomainName, ushort> domainNames, bool useCanonical)
    {
        DnsMessageBase.EncodeUShort(messageData, ref currentPosition, (ushort)Type);
        DnsMessageBase.EncodeUShort(messageData, ref currentPosition, KeyTag);
        messageData[currentPosition++] = (byte)Algorithm;
        DnsMessageBase.EncodeByteArray(messageData, ref currentPosition, Certificate);
    }
}

/// <summary>
///   <para>Canonical name for an alias</para>
///   <para>
///     Defined in
///     <see cref="!:http://tools.ietf.org/html/rfc1035">RFC 1035</see>
///   </para>
/// </summary>
public class CNameRecord : DnsRecordBase
{
    /// <summary>
    ///   Canonical name
    /// </summary>
    public DomainName CanonicalName { get; private set; }

    internal CNameRecord() { }

    /// <summary>
    ///   Creates a new instance of the CNameRecord class
    /// </summary>
    /// <param name="name"> Domain name the host </param>
    /// <param name="timeToLive"> Seconds the record should be cached at most </param>
    /// <param name="canonicalName"> Canocical name for the alias of the host </param>
    public CNameRecord(DomainName name, int timeToLive, DomainName canonicalName)
        : base(name, RecordType.CName, RecordClass.INet, timeToLive)
    {
        CanonicalName = canonicalName ?? DomainName.Root;
    }

    internal override void ParseRecordData(ReadOnlySpan<byte> resultData, int startPosition, int length)
    {
        CanonicalName = DnsMessageBase.ParseDomainName(resultData, ref startPosition);
    }

    internal override void ParseRecordData(DomainName origin, string[] stringRepresentation)
    {
        if (stringRepresentation.Length != 1)
            throw new FormatException();

        CanonicalName = ParseDomainName(origin, stringRepresentation[0]);
    }

    internal override string RecordDataToString()
    {
        return CanonicalName.ToString();
    }

    protected internal override int MaximumRecordDataLength => CanonicalName.MaximumRecordDataLength + 2;

    protected internal override void EncodeRecordData(Span<byte> messageData, int offset, ref int currentPosition, Dictionary<DomainName, ushort> domainNames, bool useCanonical)
    {
        DnsMessageBase.EncodeDomainName(messageData, offset, ref currentPosition, CanonicalName, domainNames, useCanonical);
    }
}

/// <summary>
///   <para>Child-to-Parent Synchronization</para>
///   <para>
///     Defined in
///     <see cref="!:http://tools.ietf.org/html/rfc7477">RFC 7477</see>
///   </para>
/// </summary>
public class CSyncRecord : DnsRecordBase
{
    /// <summary>
    ///   CSync record flags
    /// </summary>
    public enum CSyncFlags : ushort
    {
        /// <summary>
        ///   <para>Immediate</para>
        ///   <para>
        ///     Defined in
        ///     <see cref="!:http://tools.ietf.org/html/rfc7477">RFC 7477</see>
        ///   </para>
        /// </summary>
        Immediate = 1,

        /// <summary>
        ///   <para>SOA minimum</para>
        ///   <para>
        ///     Defined in
        ///     <see cref="!:http://tools.ietf.org/html/rfc7477">RFC 7477</see>
        ///   </para>
        /// </summary>
        SoaMinimum = 2,
    }

    /// <summary>
    ///   SOA Serial Field
    /// </summary>
    public uint SerialNumber { get; internal set; }

    /// <summary>
    ///   Flags
    /// </summary>
    public CSyncFlags Flags { get; internal set; }

    /// <summary>
    ///   Record types
    /// </summary>
    public List<RecordType> Types { get; private set; }

    internal CSyncRecord() { }

    /// <summary>
    ///   Creates a new instance of the CSyncRecord class
    /// </summary>
    /// <param name="name"> Name of the record </param>
    /// <param name="recordClass"> Class of the record </param>
    /// <param name="timeToLive"> Seconds the record should be cached at most </param>
    /// <param name="serialNumber"> SOA Serial Field </param>
    /// <param name="flags"> Flags</param>
    /// <param name="types"> Record types of the next owner </param>
    public CSyncRecord(DomainName name, RecordClass recordClass, int timeToLive, uint serialNumber, CSyncFlags flags, List<RecordType> types)
        : base(name, RecordType.CSync, recordClass, timeToLive)
    {
        SerialNumber = serialNumber;
        Flags = flags;

        if ((types == null) || (types.Count == 0))
        {
            Types = new List<RecordType>();
        }
        else
        {
            Types = types.Distinct().OrderBy(x => x).ToList();
        }
    }

    internal override void ParseRecordData(ReadOnlySpan<byte> resultData, int currentPosition, int length)
    {
        int endPosition = currentPosition + length;

        SerialNumber = DnsMessageBase.ParseUInt(resultData, ref currentPosition);
        Flags = (CSyncFlags)DnsMessageBase.ParseUShort(resultData, ref currentPosition);
        Types = ParseTypeBitMap(resultData, ref currentPosition, endPosition);
    }

    internal static List<RecordType> ParseTypeBitMap(ReadOnlySpan<byte> resultData, ref int currentPosition, int endPosition)
    {
        List<RecordType> types = new List<RecordType>();
        while (currentPosition < endPosition)
        {
            byte windowNumber = resultData[currentPosition++];
            byte windowLength = resultData[currentPosition++];

            for (int i = 0; i < windowLength; i++)
            {
                byte bitmap = resultData[currentPosition++];

                for (int bit = 0; bit < 8; bit++)
                {
                    if ((bitmap & (1 << Math.Abs(bit - 7))) != 0)
                    {
                        types.Add((RecordType)(windowNumber * 256 + i * 8 + bit));
                    }
                }
            }
        }
        return types;
    }

    internal override void ParseRecordData(DomainName origin, string[] stringRepresentation)
    {
        if (stringRepresentation.Length < 3)
            throw new FormatException();

        SerialNumber = UInt32.Parse(stringRepresentation[0]);
        Flags = (CSyncFlags)UInt16.Parse(stringRepresentation[1]);
        Types = stringRepresentation.Skip(2).Select(RecordTypeHelper.ParseShortString).ToList();
    }

    internal override string RecordDataToString()
    {
        return SerialNumber
               + " " + (ushort)Flags + " " + String.Join(" ", Types.Select(RecordTypeHelper.ToShortString));
    }

    protected internal override int MaximumRecordDataLength => 7 + GetMaximumTypeBitmapLength(Types);

    internal static int GetMaximumTypeBitmapLength(List<RecordType> types)
    {
        int res = 0;

        int windowEnd = 255;
        ushort lastType = 0;

        foreach (ushort type in types.Select(t => (ushort)t))
        {
            if (type > windowEnd)
            {
                res += 3 + lastType % 256 / 8;
                windowEnd = (type / 256 + 1) * 256 - 1;
            }

            lastType = type;
        }

        return res + 3 + lastType % 256 / 8;
    }

    protected internal override void EncodeRecordData(Span<byte> messageData, int offset, ref int currentPosition, Dictionary<DomainName, ushort> domainNames, bool useCanonical)
    {
        DnsMessageBase.EncodeUInt(messageData, ref currentPosition, SerialNumber);
        DnsMessageBase.EncodeUShort(messageData, ref currentPosition, (ushort)Flags);
        EncodeTypeBitmap(messageData, ref currentPosition, Types);
    }

    internal static void EncodeTypeBitmap(Span<byte> messageData, ref int currentPosition, List<RecordType> types)
    {
        int windowEnd = 255;
        byte[] windowData = new byte[32];
        int windowLength = 0;

        foreach (ushort type in types.Select(t => (ushort)t))
        {
            if (type > windowEnd)
            {
                if (windowLength > 0)
                {
                    messageData[currentPosition++] = (byte)(windowEnd / 256);
                    messageData[currentPosition++] = (byte)windowLength;
                    DnsMessageBase.EncodeByteArray(messageData, ref currentPosition, windowData, windowLength);
                }

                windowEnd = (type / 256 + 1) * 256 - 1;
                windowLength = 0;
            }

            int typeLower = type % 256;

            int octetPos = typeLower / 8;
            int bitPos = typeLower % 8;

            while (windowLength <= octetPos)
            {
                windowData[windowLength] = 0;
                windowLength++;
            }

            byte octet = windowData[octetPos];
            octet |= (byte)(1 << Math.Abs(bitPos - 7));
            windowData[octetPos] = octet;
        }

        if (windowLength > 0)
        {
            messageData[currentPosition++] = (byte)(windowEnd / 256);
            messageData[currentPosition++] = (byte)windowLength;
            DnsMessageBase.EncodeByteArray(messageData, ref currentPosition, windowData, windowLength);
        }
    }
}

/// <summary>
///   <para>Dynamic Host Configuration Protocol (DHCP) Information record</para>
///   <para>
///     Defined in
///     <see cref="!:http://tools.ietf.org/html/rfc4701">RFC 4701</see>
///   </para>
/// </summary>
public class DhcidRecord : DnsRecordBase
{
    /// <summary>
    ///   Record data
    /// </summary>
    public byte[] RecordData { get; private set; }

    internal DhcidRecord() { }

    /// <summary>
    ///   Creates a new instance of the DhcidRecord class
    /// </summary>
    /// <param name="name"> Name of the record </param>
    /// <param name="timeToLive"> Seconds the record should be cached at most </param>
    /// <param name="recordData"> Record data </param>
    public DhcidRecord(DomainName name, int timeToLive, byte[] recordData)
        : base(name, RecordType.Dhcid, RecordClass.INet, timeToLive)
    {
        RecordData = recordData ?? new byte[] { };
    }

    internal override void ParseRecordData(ReadOnlySpan<byte> resultData, int startPosition, int length)
    {
        RecordData = DnsMessageBase.ParseByteData(resultData, ref startPosition, length);
    }

    internal override void ParseRecordData(DomainName origin, string[] stringRepresentation)
    {
        if (stringRepresentation.Length < 1)
            throw new FormatException();

        RecordData = String.Join(String.Empty, stringRepresentation).FromBase64String();
    }

    internal override string RecordDataToString()
    {
        return RecordData.ToBase64String();
    }

    protected internal override int MaximumRecordDataLength => RecordData.Length;

    protected internal override void EncodeRecordData(Span<byte> messageData, int offset, ref int currentPosition, Dictionary<DomainName, ushort> domainNames, bool useCanonical)
    {
        DnsMessageBase.EncodeByteArray(messageData, ref currentPosition, RecordData);
    }
}

/// <summary>
///   <para>DNS Name Redirection record</para>
///   <para>
///     Defined in
///     <see cref="!:http://tools.ietf.org/html/rfc6672">RFC 6672</see>
///   </para>
/// </summary>
public class DNameRecord : DnsRecordBase
{
    /// <summary>
    ///   Target of the redirection
    /// </summary>
    public DomainName Target { get; private set; }

    internal DNameRecord() { }

    /// <summary>
    ///   Creates a new instance of the DNameRecord class
    /// </summary>
    /// <param name="name"> Name of the record </param>
    /// <param name="timeToLive"> Seconds the record should be cached at most </param>
    /// <param name="target"> Target of the redirection </param>
    public DNameRecord(DomainName name, int timeToLive, DomainName target)
        : base(name, RecordType.DName, RecordClass.INet, timeToLive)
    {
        Target = target ?? DomainName.Root;
    }

    internal override void ParseRecordData(ReadOnlySpan<byte> resultData, int startPosition, int length)
    {
        Target = DnsMessageBase.ParseDomainName(resultData, ref startPosition);
    }

    internal override void ParseRecordData(DomainName origin, string[] stringRepresentation)
    {
        if (stringRepresentation.Length != 1)
            throw new FormatException();

        Target = ParseDomainName(origin, stringRepresentation[0]);
    }

    internal override string RecordDataToString()
    {
        return Target.ToString();
    }

    protected internal override int MaximumRecordDataLength => Target.MaximumRecordDataLength + 2;

    protected internal override void EncodeRecordData(Span<byte> messageData, int offset, ref int currentPosition, Dictionary<DomainName, ushort> domainNames, bool useCanonical)
    {
        DnsMessageBase.EncodeDomainName(messageData, offset, ref currentPosition, Target, null, useCanonical);
    }
}

/// <summary>
///   Base class representing a dns record
/// </summary>
public abstract class DnsRecordBase : DnsMessageEntryBase, IComparable<DnsRecordBase>, IEquatable<DnsRecordBase>
{
    internal int StartPosition { get; set; }
    internal ushort RecordDataLength { get; set; }

    /// <summary>
    ///   Seconds which a record should be cached at most
    /// </summary>
    public int TimeToLive { get; internal set; }

    protected DnsRecordBase() { }

    protected DnsRecordBase(DomainName name, RecordType recordType, RecordClass recordClass, int timeToLive)
    {
        if (name == null)
            throw new ArgumentNullException(nameof(name));

        Name = name;
        RecordType = recordType;
        RecordClass = recordClass;
        TimeToLive = timeToLive;
    }

    internal static DnsRecordBase Create(RecordType type, ReadOnlySpan<byte> resultData, int recordDataPosition)
    {
        if ((type == RecordType.Key) && (resultData[recordDataPosition + 3] == (byte)DnsSecAlgorithm.DiffieHellman))
        {
            return new DiffieHellmanKeyRecord();
        }
        else
        {
            return Create(type);
        }
    }

    internal static DnsRecordBase Create(RecordType type)
    {
        switch (type)
        {
            case RecordType.A:
                return new ARecord();
            case RecordType.Ns:
                return new NsRecord();
            case RecordType.CName:
                return new CNameRecord();
            case RecordType.Soa:
                return new SoaRecord();
            case RecordType.Wks:
                return new WksRecord();
            case RecordType.Ptr:
                return new PtrRecord();
            case RecordType.HInfo:
                return new HInfoRecord();
            case RecordType.Mx:
                return new MxRecord();
            case RecordType.Txt:
                return new TxtRecord();
            case RecordType.Rp:
                return new RpRecord();
            case RecordType.Afsdb:
                return new AfsdbRecord();
            case RecordType.X25:
                return new X25Record();
            case RecordType.Isdn:
                return new IsdnRecord();
            case RecordType.Rt:
                return new RtRecord();
            case RecordType.Nsap:
                return new NsapRecord();
            case RecordType.Sig:
                return new SigRecord();
            case RecordType.Key:
                return new KeyRecord();
            case RecordType.Px:
                return new PxRecord();
            case RecordType.GPos:
                return new GPosRecord();
            case RecordType.Aaaa:
                return new AaaaRecord();
            case RecordType.Loc:
                return new LocRecord();
            case RecordType.Srv:
                return new SrvRecord();
            case RecordType.Naptr:
                return new NaptrRecord();
            case RecordType.Kx:
                return new KxRecord();
            case RecordType.Cert:
                return new CertRecord();
            case RecordType.DName:
                return new DNameRecord();
            case RecordType.Opt:
                return new OptRecord();
            case RecordType.Apl:
                return new AplRecord();
            case RecordType.Ds:
                return new DsRecord();
            case RecordType.SshFp:
                return new SshFpRecord();
            case RecordType.IpSecKey:
                return new IpSecKeyRecord();
            case RecordType.RrSig:
                return new RrSigRecord();
            case RecordType.NSec:
                return new NSecRecord();
            case RecordType.DnsKey:
                return new DnsKeyRecord();
            case RecordType.Dhcid:
                return new DhcidRecord();
            case RecordType.NSec3:
                return new NSec3Record();
            case RecordType.NSec3Param:
                return new NSec3ParamRecord();
            case RecordType.Tlsa:
                return new TlsaRecord();
            case RecordType.Hip:
                return new HipRecord();
            case RecordType.CDs:
                return new CDsRecord();
            case RecordType.CDnsKey:
                return new CDnsKeyRecord();
            case RecordType.OpenPGPKey:
                return new OpenPGPKeyRecord();
            case RecordType.CSync:
                return new CSyncRecord();
#pragma warning disable 0612
            case RecordType.Spf:
                return new SpfRecord2();
#pragma warning restore 0612
            case RecordType.NId:
                return new NIdRecord();
            case RecordType.L32:
                return new L32Record();
            case RecordType.L64:
                return new L64Record();
            case RecordType.LP:
                return new LPRecord();
            case RecordType.Eui48:
                return new Eui48Record();
            case RecordType.Eui64:
                return new Eui64Record();
            case RecordType.TKey:
                return new TKeyRecord();
            case RecordType.TSig:
                return new TSigRecord();
            case RecordType.Uri:
                return new UriRecord();
            case RecordType.CAA:
                return new CAARecord();
            case RecordType.Dlv:
                return new DlvRecord();

            default:
                return new UnknownRecord();
        }
    }

    #region ToString
    internal abstract string RecordDataToString();

    /// <summary>
    ///   Returns the textual representation of a record
    /// </summary>
    /// <returns> Textual representation </returns>
    public override string ToString()
    {
        string recordData = RecordDataToString();
        return Name + " " + TimeToLive + " " + RecordClass.ToShortString() + " " + RecordType.ToShortString() + (String.IsNullOrEmpty(recordData) ? "" : " " + recordData);
    }
    #endregion

    #region Parsing
    internal abstract void ParseRecordData(ReadOnlySpan<byte> resultData, int startPosition, int length);

    internal abstract void ParseRecordData(DomainName origin, string[] stringRepresentation);

    internal void ParseUnknownRecordData(string[] stringRepresentation)
    {
        if (stringRepresentation.Length < 2)
            throw new FormatException();

        if (stringRepresentation[0] != @"\#")
            throw new FormatException();

        int length = Int32.Parse(stringRepresentation[1]);

        byte[] byteData = String.Join("", stringRepresentation.Skip(2)).FromBase16String();

        if (length != byteData.Length)
            throw new FormatException();

        ParseRecordData(byteData, 0, length);
    }

    protected DomainName ParseDomainName(DomainName origin, string name)
    {
        if (String.IsNullOrEmpty(name))
            throw new ArgumentException("Name must be provided", nameof(name));

        if (name.EndsWith("."))
            return DomainName.ParseFromMasterfile(name);

        return DomainName.ParseFromMasterfile(name) + origin;
    }
    #endregion

    #region Encoding
    internal override sealed int MaximumLength => Name.MaximumRecordDataLength + 12 + MaximumRecordDataLength;

    internal void Encode(Span<byte> messageData, int offset, ref int currentPosition, Dictionary<DomainName, ushort> domainNames, bool useCanonical = false)
    {
        EncodeRecordHeader(messageData, offset, ref currentPosition, domainNames, useCanonical);
        EncodeRecordBody(messageData, offset, ref currentPosition, domainNames, useCanonical);
    }

    internal void EncodeRecordHeader(Span<byte> messageData, int offset, ref int currentPosition, Dictionary<DomainName, ushort> domainNames, bool useCanonical)
    {
        DnsMessageBase.EncodeDomainName(messageData, offset, ref currentPosition, Name, domainNames, useCanonical);
        DnsMessageBase.EncodeUShort(messageData, ref currentPosition, (ushort)RecordType);
        DnsMessageBase.EncodeUShort(messageData, ref currentPosition, (ushort)RecordClass);
        DnsMessageBase.EncodeInt(messageData, ref currentPosition, TimeToLive);
    }

    internal void EncodeRecordBody(Span<byte> messageData, int offset, ref int currentPosition, Dictionary<DomainName, ushort> domainNames, bool useCanonical)
    {
        int recordDataOffset = currentPosition + 2;
        EncodeRecordData(messageData, offset, ref recordDataOffset, domainNames, useCanonical);
        EncodeRecordLength(messageData, offset, ref currentPosition, domainNames, recordDataOffset);
    }

    internal void EncodeRecordLength(Span<byte> messageData, int offset, ref int recordDataOffset, Dictionary<DomainName, ushort> domainNames, int recordPosition)
    {
        DnsMessageBase.EncodeUShort(messageData, ref recordDataOffset, (ushort)(recordPosition - recordDataOffset - 2));
        recordDataOffset = recordPosition;
    }


    protected internal abstract int MaximumRecordDataLength { get; }

    protected internal abstract void EncodeRecordData(Span<byte> messageData, int offset, ref int currentPosition, Dictionary<DomainName, ushort> domainNames, bool useCanonical);
    #endregion

    internal T Clone<T>()
        where T : DnsRecordBase
    {
        return (T)MemberwiseClone();
    }

    public int CompareTo(DnsRecordBase other)
    {
        int compare = Name.CompareTo(other.Name);
        if (compare != 0)
            return compare;

        compare = RecordType.CompareTo(other.RecordType);
        if (compare != 0)
            return compare;

        compare = RecordClass.CompareTo(other.RecordClass);
        if (compare != 0)
            return compare;

        compare = TimeToLive.CompareTo(other.TimeToLive);
        if (compare != 0)
            return compare;

        byte[] thisBuffer = new byte[MaximumRecordDataLength];
        int thisLength = 0;
        EncodeRecordData(thisBuffer, 0, ref thisLength, null, false);

        byte[] otherBuffer = new byte[other.MaximumRecordDataLength];
        int otherLength = 0;
        other.EncodeRecordData(otherBuffer, 0, ref otherLength, null, false);

        for (int i = 0; i < Math.Min(thisLength, otherLength); i++)
        {
            compare = thisBuffer[i].CompareTo(otherBuffer[i]);
            if (compare != 0)
                return compare;
        }

        return thisLength.CompareTo(otherLength);
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
        return Equals(obj as DnsRecordBase);
    }

    public bool Equals(DnsRecordBase other)
    {
        if (other == null)
            return false;

        return base.Equals(other)
               && RecordDataToString().Equals(other.RecordDataToString());
    }
}

/// <summary>
///   <para>EUI48</para>
///   <para>
///     Defined in
///     <see cref="!:http://tools.ietf.org/html/rfc7043">RFC 7043</see>
///   </para>
/// </summary>
public class Eui48Record : DnsRecordBase
{
    /// <summary>
    ///   IP address of the host
    /// </summary>
    public byte[] Address { get; private set; }

    internal Eui48Record() { }

    /// <summary>
    ///   Creates a new instance of the Eui48Record class
    /// </summary>
    /// <param name="name"> Domain name of the host </param>
    /// <param name="timeToLive"> Seconds the record should be cached at most </param>
    /// <param name="address"> The EUI48 address</param>
    public Eui48Record(DomainName name, int timeToLive, byte[] address)
        : base(name, RecordType.Eui48, RecordClass.INet, timeToLive)
    {
        Address = address ?? new byte[6];
    }

    internal override void ParseRecordData(ReadOnlySpan<byte> resultData, int startPosition, int length)
    {
        Address = DnsMessageBase.ParseByteData(resultData, ref startPosition, 6);
    }

    internal override void ParseRecordData(DomainName origin, string[] stringRepresentation)
    {
        if (stringRepresentation.Length != 1)
            throw new NotSupportedException();

        Address = stringRepresentation[0].Split('-').Select(x => Convert.ToByte(x, 16)).ToArray();

        if (Address.Length != 6)
            throw new NotSupportedException();
    }

    internal override string RecordDataToString()
    {
        return String.Join("-", Address.Select(x => x.ToString("x2")).ToArray());
    }

    protected internal override int MaximumRecordDataLength => 6;

    protected internal override void EncodeRecordData(Span<byte> messageData, int offset, ref int currentPosition, Dictionary<DomainName, ushort> domainNames, bool useCanonical)
    {
        DnsMessageBase.EncodeByteArray(messageData, ref currentPosition, Address);
    }
}

/// <summary>
///   <para>EUI64</para>
///   <para>
///     Defined in
///     <see cref="!:http://tools.ietf.org/html/rfc7043">RFC 7043</see>
///   </para>
/// </summary>
public class Eui64Record : DnsRecordBase
{
    /// <summary>
    ///   IP address of the host
    /// </summary>
    public byte[] Address { get; private set; }

    internal Eui64Record() { }

    /// <summary>
    ///   Creates a new instance of the Eui48Record class
    /// </summary>
    /// <param name="name"> Domain name of the host </param>
    /// <param name="timeToLive"> Seconds the record should be cached at most </param>
    /// <param name="address"> The EUI48 address</param>
    public Eui64Record(DomainName name, int timeToLive, byte[] address)
        : base(name, RecordType.Eui64, RecordClass.INet, timeToLive)
    {
        Address = address ?? new byte[8];
    }

    internal override void ParseRecordData(ReadOnlySpan<byte> resultData, int startPosition, int length)
    {
        Address = DnsMessageBase.ParseByteData(resultData, ref startPosition, 8);
    }

    internal override string RecordDataToString()
    {
        return String.Join("-", Address.Select(x => x.ToString("x2")).ToArray());
    }

    internal override void ParseRecordData(DomainName origin, string[] stringRepresentation)
    {
        if (stringRepresentation.Length != 1)
            throw new NotSupportedException();

        Address = stringRepresentation[0].Split('-').Select(x => Convert.ToByte(x, 16)).ToArray();

        if (Address.Length != 8)
            throw new NotSupportedException();
    }

    protected internal override int MaximumRecordDataLength => 8;

    protected internal override void EncodeRecordData(Span<byte> messageData, int offset, ref int currentPosition, Dictionary<DomainName, ushort> domainNames, bool useCanonical)
    {
        DnsMessageBase.EncodeByteArray(messageData, ref currentPosition, Address);
    }
}

/// <summary>
///   <para>Geographical position</para>
///   <para>
///     Defined in
///     <see cref="!:http://tools.ietf.org/html/rfc1712">RFC 1712</see>
///   </para>
/// </summary>
public class GPosRecord : DnsRecordBase
{
    /// <summary>
    ///   Longitude of the geographical position
    /// </summary>
    public double Longitude { get; private set; }

    /// <summary>
    ///   Latitude of the geographical position
    /// </summary>
    public double Latitude { get; private set; }

    /// <summary>
    ///   Altitude of the geographical position
    /// </summary>
    public double Altitude { get; private set; }

    internal GPosRecord() { }

    /// <summary>
    ///   Creates a new instance of the GPosRecord class
    /// </summary>
    /// <param name="name"> Name of the record </param>
    /// <param name="timeToLive"> Seconds the record should be cached at most </param>
    /// <param name="longitude"> Longitude of the geographical position </param>
    /// <param name="latitude"> Latitude of the geographical position </param>
    /// <param name="altitude"> Altitude of the geographical position </param>
    public GPosRecord(DomainName name, int timeToLive, double longitude, double latitude, double altitude)
        : base(name, RecordType.GPos, RecordClass.INet, timeToLive)
    {
        Longitude = longitude;
        Latitude = latitude;
        Altitude = altitude;
    }

    internal override void ParseRecordData(ReadOnlySpan<byte> resultData, int currentPosition, int length)
    {
        Longitude = Double.Parse(DnsMessageBase.ParseText(resultData, ref currentPosition), CultureInfo.InvariantCulture);
        Latitude = Double.Parse(DnsMessageBase.ParseText(resultData, ref currentPosition), CultureInfo.InvariantCulture);
        Altitude = Double.Parse(DnsMessageBase.ParseText(resultData, ref currentPosition), CultureInfo.InvariantCulture);
    }

    internal override void ParseRecordData(DomainName origin, string[] stringRepresentation)
    {
        if (stringRepresentation.Length != 3)
            throw new FormatException();

        Longitude = Double.Parse(stringRepresentation[0], CultureInfo.InvariantCulture);
        Latitude = Double.Parse(stringRepresentation[1], CultureInfo.InvariantCulture);
        Altitude = Double.Parse(stringRepresentation[2], CultureInfo.InvariantCulture);
    }

    internal override string RecordDataToString()
    {
        return Longitude.ToString(CultureInfo.InvariantCulture)
               + " " + Latitude.ToString(CultureInfo.InvariantCulture)
               + " " + Altitude.ToString(CultureInfo.InvariantCulture);
    }

    protected internal override int MaximumRecordDataLength => 3 + Longitude.ToString(CultureInfo.InvariantCulture).Length + Latitude.ToString(CultureInfo.InvariantCulture).Length + Altitude.ToString(CultureInfo.InvariantCulture).Length;

    protected internal override void EncodeRecordData(Span<byte> messageData, int offset, ref int currentPosition, Dictionary<DomainName, ushort> domainNames, bool useCanonical)
    {
        DnsMessageBase.EncodeTextBlock(messageData, ref currentPosition, Longitude.ToString(CultureInfo.InvariantCulture));
        DnsMessageBase.EncodeTextBlock(messageData, ref currentPosition, Latitude.ToString(CultureInfo.InvariantCulture));
        DnsMessageBase.EncodeTextBlock(messageData, ref currentPosition, Altitude.ToString(CultureInfo.InvariantCulture));
    }
}

/// <summary>
///   <para>Host information</para>
///   <para>
///     Defined in
///     <see cref="!:http://tools.ietf.org/html/rfc1035">RFC 1035</see>
///   </para>
/// </summary>
public class HInfoRecord : DnsRecordBase
{
    /// <summary>
    ///   Type of the CPU of the host
    /// </summary>
    public string Cpu { get; private set; }

    /// <summary>
    ///   Name of the operating system of the host
    /// </summary>
    public string OperatingSystem { get; private set; }

    internal HInfoRecord() { }

    /// <summary>
    ///   Creates a new instance of the HInfoRecord class
    /// </summary>
    /// <param name="name"> Name of the host </param>
    /// <param name="timeToLive"> Seconds the record should be cached at most </param>
    /// <param name="cpu"> Type of the CPU of the host </param>
    /// <param name="operatingSystem"> Name of the operating system of the host </param>
    public HInfoRecord(DomainName name, int timeToLive, string cpu, string operatingSystem)
        : base(name, RecordType.HInfo, RecordClass.INet, timeToLive)
    {
        Cpu = cpu ?? String.Empty;
        OperatingSystem = operatingSystem ?? String.Empty;
    }

    internal override void ParseRecordData(ReadOnlySpan<byte> resultData, int startPosition, int length)
    {
        Cpu = DnsMessageBase.ParseText(resultData, ref startPosition);
        OperatingSystem = DnsMessageBase.ParseText(resultData, ref startPosition);
    }

    internal override void ParseRecordData(DomainName origin, string[] stringRepresentation)
    {
        if (stringRepresentation.Length != 2)
            throw new FormatException();

        Cpu = stringRepresentation[0];
        OperatingSystem = stringRepresentation[1];
    }

    internal override string RecordDataToString()
    {
        return "\"" + Cpu.ToMasterfileLabelRepresentation() + "\""
               + " \"" + OperatingSystem.ToMasterfileLabelRepresentation() + "\"";
    }

    protected internal override int MaximumRecordDataLength => 2 + Cpu.Length + OperatingSystem.Length;

    protected internal override void EncodeRecordData(Span<byte> messageData, int offset, ref int currentPosition, Dictionary<DomainName, ushort> domainNames, bool useCanonical)
    {
        DnsMessageBase.EncodeTextBlock(messageData, ref currentPosition, Cpu);
        DnsMessageBase.EncodeTextBlock(messageData, ref currentPosition, OperatingSystem);
    }
}

/// <summary>
///   <para>Host identity protocol</para>
///   <para>
///     Defined in
///     <see cref="!:http://tools.ietf.org/html/rfc5205">RFC 5205</see>
///   </para>
/// </summary>
public class HipRecord : DnsRecordBase
{
    /// <summary>
    ///   Algorithm of the key
    /// </summary>
    public IpSecKeyRecord.IpSecAlgorithm Algorithm { get; private set; }

    /// <summary>
    ///   Host identity tag
    /// </summary>
    public byte[] Hit { get; private set; }

    /// <summary>
    ///   Binary data of the public key
    /// </summary>
    public byte[] PublicKey { get; private set; }

    /// <summary>
    ///   Possible rendezvous servers
    /// </summary>
    public List<DomainName> RendezvousServers { get; private set; }

    internal HipRecord() { }

    /// <summary>
    ///   Creates a new instace of the HipRecord class
    /// </summary>
    /// <param name="name"> Name of the record </param>
    /// <param name="timeToLive"> Seconds the record should be cached at most </param>
    /// <param name="algorithm"> Algorithm of the key </param>
    /// <param name="hit"> Host identity tag </param>
    /// <param name="publicKey"> Binary data of the public key </param>
    /// <param name="rendezvousServers"> Possible rendezvous servers </param>
    public HipRecord(DomainName name, int timeToLive, IpSecKeyRecord.IpSecAlgorithm algorithm, byte[] hit, byte[] publicKey, List<DomainName> rendezvousServers)
        : base(name, RecordType.Hip, RecordClass.INet, timeToLive)
    {
        Algorithm = algorithm;
        Hit = hit ?? new byte[] { };
        PublicKey = publicKey ?? new byte[] { };
        RendezvousServers = rendezvousServers ?? new List<DomainName>();
    }

    internal override void ParseRecordData(ReadOnlySpan<byte> resultData, int currentPosition, int length)
    {
        int endPosition = currentPosition + length;

        int hitLength = resultData[currentPosition++];
        Algorithm = (IpSecKeyRecord.IpSecAlgorithm)resultData[currentPosition++];
        int publicKeyLength = DnsMessageBase.ParseUShort(resultData, ref currentPosition);
        Hit = DnsMessageBase.ParseByteData(resultData, ref currentPosition, hitLength);
        PublicKey = DnsMessageBase.ParseByteData(resultData, ref currentPosition, publicKeyLength);
        RendezvousServers = new List<DomainName>();
        while (currentPosition < endPosition)
        {
            RendezvousServers.Add(DnsMessageBase.ParseDomainName(resultData, ref currentPosition));
        }
    }

    internal override void ParseRecordData(DomainName origin, string[] stringRepresentation)
    {
        if (stringRepresentation.Length < 3)
            throw new FormatException();

        Algorithm = (IpSecKeyRecord.IpSecAlgorithm)Byte.Parse(stringRepresentation[0]);
        Hit = stringRepresentation[1].FromBase16String();
        PublicKey = stringRepresentation[2].FromBase64String();
        RendezvousServers = stringRepresentation.Skip(3).Select(x => ParseDomainName(origin, x)).ToList();
    }

    internal override string RecordDataToString()
    {
        return (byte)Algorithm
               + " " + Hit.ToBase16String()
               + " " + PublicKey.ToBase64String()
               + " " + String.Join(" ", RendezvousServers.Select(s => s.ToString()));
    }

    protected internal override int MaximumRecordDataLength
    {
        get
        {
            int res = 4;
            res += Hit.Length;
            res += PublicKey.Length;
            res += RendezvousServers.Sum(s => s.MaximumRecordDataLength + 2);
            return res;
        }
    }

    protected internal override void EncodeRecordData(Span<byte> messageData, int offset, ref int currentPosition, Dictionary<DomainName, ushort> domainNames, bool useCanonical)
    {
        messageData[currentPosition++] = (byte)Hit.Length;
        messageData[currentPosition++] = (byte)Algorithm;
        DnsMessageBase.EncodeUShort(messageData, ref currentPosition, (ushort)PublicKey.Length);
        DnsMessageBase.EncodeByteArray(messageData, ref currentPosition, Hit);
        DnsMessageBase.EncodeByteArray(messageData, ref currentPosition, PublicKey);
        foreach (DomainName server in RendezvousServers)
        {
            DnsMessageBase.EncodeDomainName(messageData, offset, ref currentPosition, server, null, false);
        }
    }
}

/// <summary>
///   Interface for host address providing <see cref="DnsRecordBase">records</see>
/// </summary>
public interface IAddressRecord
{
    /// <summary>
    ///   IP address of the host
    /// </summary>
    IPAddress Address { get; }
}

/// <summary>
///   <para>IPsec key storage</para>
///   <para>
///     Defined in
///     <see cref="!:http://tools.ietf.org/html/rfc4025">RFC 4025</see>
///   </para>
/// </summary>
public class IpSecKeyRecord : DnsRecordBase
{
    /// <summary>
    ///   Algorithm of key
    /// </summary>
    public enum IpSecAlgorithm : byte
    {
        /// <summary>
        ///   None
        /// </summary>
        None = 0,

        /// <summary>
        ///   <para>RSA</para>
        ///   <para>
        ///     Defined in
        ///     <see cref="!:http://tools.ietf.org/html/rfc4025">RFC 4025</see>
        ///   </para>
        /// </summary>
        Rsa = 1,

        /// <summary>
        ///   <para>DSA</para>
        ///   <para>
        ///     Defined in
        ///     <see cref="!:http://tools.ietf.org/html/rfc4025">RFC 4025</see>
        ///   </para>
        /// </summary>
        Dsa = 2,
    }

    /// <summary>
    ///   Type of gateway
    /// </summary>
    public enum IpSecGatewayType : byte
    {
        /// <summary>
        ///   None
        /// </summary>
        None = 0,

        /// <summary>
        ///   <para>Gateway is a IPv4 address</para>
        ///   <para>
        ///     Defined in
        ///     <see cref="!:http://tools.ietf.org/html/rfc4025">RFC 4025</see>
        ///   </para>
        /// </summary>
        IpV4 = 1,

        /// <summary>
        ///   <para>Gateway is a IPv6 address</para>
        ///   <para>
        ///     Defined in
        ///     <see cref="!:http://tools.ietf.org/html/rfc4025">RFC 4025</see>
        ///   </para>
        /// </summary>
        IpV6 = 2,

        /// <summary>
        ///   <para>Gateway is a domain name</para>
        ///   <para>
        ///     Defined in
        ///     <see cref="!:http://tools.ietf.org/html/rfc4025">RFC 4025</see>
        ///   </para>
        /// </summary>
        Domain = 3,
    }

    /// <summary>
    ///   Precedence of the record
    /// </summary>
    public byte Precedence { get; private set; }

    /// <summary>
    ///   Type of gateway
    /// </summary>
    public IpSecGatewayType GatewayType { get; private set; }

    /// <summary>
    ///   Algorithm of the key
    /// </summary>
    public IpSecAlgorithm Algorithm { get; private set; }

    /// <summary>
    ///   Address of the gateway
    /// </summary>
    public string Gateway { get; private set; }

    /// <summary>
    ///   Binary data of the public key
    /// </summary>
    public byte[] PublicKey { get; private set; }

    internal IpSecKeyRecord() { }

    /// <summary>
    ///   Creates a new instance of the IpSecKeyRecord class
    /// </summary>
    /// <param name="name"> Name of the record </param>
    /// <param name="timeToLive"> Seconds the record should be cached at most </param>
    /// <param name="precedence"> Precedence of the record </param>
    /// <param name="algorithm"> Algorithm of the key </param>
    /// <param name="gateway"> Address of the gateway </param>
    /// <param name="publicKey"> Binary data of the public key </param>
    public IpSecKeyRecord(DomainName name, int timeToLive, byte precedence, IpSecAlgorithm algorithm, DomainName gateway, byte[] publicKey)
        : base(name, RecordType.IpSecKey, RecordClass.INet, timeToLive)
    {
        Precedence = precedence;
        GatewayType = IpSecGatewayType.Domain;
        Algorithm = algorithm;
        Gateway = (gateway ?? DomainName.Root).ToString();
        PublicKey = publicKey ?? new byte[] { };
    }

    /// <summary>
    ///   Creates a new instance of the IpSecKeyRecord class
    /// </summary>
    /// <param name="name"> Name of the record </param>
    /// <param name="timeToLive"> Seconds the record should be cached at most </param>
    /// <param name="precedence"> Precedence of the record </param>
    /// <param name="algorithm"> Algorithm of the key </param>
    /// <param name="gateway"> Address of the gateway </param>
    /// <param name="publicKey"> Binary data of the public key </param>
    public IpSecKeyRecord(DomainName name, int timeToLive, byte precedence, IpSecAlgorithm algorithm, IPAddress gateway, byte[] publicKey)
        : base(name, RecordType.IpSecKey, RecordClass.INet, timeToLive)
    {
        Precedence = precedence;
        GatewayType = (gateway.AddressFamily == AddressFamily.InterNetwork) ? IpSecGatewayType.IpV4 : IpSecGatewayType.IpV6;
        Algorithm = algorithm;
        Gateway = gateway.ToString();
        PublicKey = publicKey ?? new byte[] { };
    }

    internal override void ParseRecordData(ReadOnlySpan<byte> resultData, int currentPosition, int length)
    {
        int startPosition = currentPosition;

        Precedence = resultData[currentPosition++];
        GatewayType = (IpSecGatewayType)resultData[currentPosition++];
        Algorithm = (IpSecAlgorithm)resultData[currentPosition++];
        switch (GatewayType)
        {
            case IpSecGatewayType.None:
                Gateway = String.Empty;
                break;
            case IpSecGatewayType.IpV4:
                Gateway = new IPAddress(DnsMessageBase.ParseByteData(resultData, ref currentPosition, 4)).ToString();
                break;
            case IpSecGatewayType.IpV6:
                Gateway = new IPAddress(DnsMessageBase.ParseByteData(resultData, ref currentPosition, 16)).ToString();
                break;
            case IpSecGatewayType.Domain:
                Gateway = DnsMessageBase.ParseDomainName(resultData, ref currentPosition).ToString();
                break;
        }
        PublicKey = DnsMessageBase.ParseByteData(resultData, ref currentPosition, length + startPosition - currentPosition);
    }

    internal override void ParseRecordData(DomainName origin, string[] stringRepresentation)
    {
        if (stringRepresentation.Length < 5)
            throw new FormatException();

        Precedence = Byte.Parse(stringRepresentation[0]);
        GatewayType = (IpSecGatewayType)Byte.Parse(stringRepresentation[1]);
        Algorithm = (IpSecAlgorithm)Byte.Parse(stringRepresentation[2]);
        Gateway = stringRepresentation[3];
        PublicKey = String.Join(String.Empty, stringRepresentation.Skip(4)).FromBase64String();
    }

    internal override string RecordDataToString()
    {
        return Precedence
               + " " + (byte)GatewayType
               + " " + (byte)Algorithm
               + " " + GatewayToString()
               + " " + PublicKey.ToBase64String();
    }

    private string GatewayToString()
    {
        switch (GatewayType)
        {
            case IpSecGatewayType.Domain:
                return Gateway.ToMasterfileLabelRepresentation() + ".";

            case IpSecGatewayType.IpV4:
            case IpSecGatewayType.IpV6:
                return Gateway;

            default:
                return ".";
        }
    }

    protected internal override int MaximumRecordDataLength
    {
        get
        {
            int res = 3;
            switch (GatewayType)
            {
                case IpSecGatewayType.IpV4:
                    res += 4;
                    break;
                case IpSecGatewayType.IpV6:
                    res += 16;
                    break;
                case IpSecGatewayType.Domain:
                    res += 2 + Gateway.Length;
                    break;
            }
            res += PublicKey.Length;
            return res;
        }
    }

    protected internal override void EncodeRecordData(Span<byte> messageData, int offset, ref int currentPosition, Dictionary<DomainName, ushort> domainNames, bool useCanonical)
    {
        messageData[currentPosition++] = Precedence;
        messageData[currentPosition++] = (byte)GatewayType;
        messageData[currentPosition++] = (byte)Algorithm;
        switch (GatewayType)
        {
            case IpSecGatewayType.IpV4:
            case IpSecGatewayType.IpV6:
                byte[] addressBuffer = IPAddress.Parse(Gateway).GetAddressBytes();
                DnsMessageBase.EncodeByteArray(messageData, ref currentPosition, addressBuffer);
                break;
            case IpSecGatewayType.Domain:
                DnsMessageBase.EncodeDomainName(messageData, offset, ref currentPosition, ParseDomainName(DomainName.Root, Gateway), null, false);
                break;
        }
        DnsMessageBase.EncodeByteArray(messageData, ref currentPosition, PublicKey);
    }
}

/// <summary>
///   <para>ISDN address</para>
///   <para>
///     Defined in
///     <see cref="!:http://tools.ietf.org/html/rfc1183">RFC 1183</see>
///   </para>
/// </summary>
public class IsdnRecord : DnsRecordBase
{
    /// <summary>
    ///   ISDN number
    /// </summary>
    public string IsdnAddress { get; private set; }

    /// <summary>
    ///   Sub address
    /// </summary>
    public string SubAddress { get; private set; }

    internal IsdnRecord() { }

    /// <summary>
    ///   Creates a new instance of the IsdnRecord class
    /// </summary>
    /// <param name="name"> Name of the record </param>
    /// <param name="timeToLive"> Seconds the record should be cached at most </param>
    /// <param name="isdnAddress"> ISDN number </param>
    public IsdnRecord(DomainName name, int timeToLive, string isdnAddress)
        : this(name, timeToLive, isdnAddress, String.Empty) { }

    /// <summary>
    ///   Creates a new instance of the IsdnRecord class
    /// </summary>
    /// <param name="name"> Name of the record </param>
    /// <param name="timeToLive"> Seconds the record should be cached at most </param>
    /// <param name="isdnAddress"> ISDN number </param>
    /// <param name="subAddress"> Sub address </param>
    public IsdnRecord(DomainName name, int timeToLive, string isdnAddress, string subAddress)
        : base(name, RecordType.Isdn, RecordClass.INet, timeToLive)
    {
        IsdnAddress = isdnAddress ?? String.Empty;
        SubAddress = subAddress ?? String.Empty;
    }

    internal override void ParseRecordData(ReadOnlySpan<byte> resultData, int currentPosition, int length)
    {
        int endPosition = currentPosition + length;

        IsdnAddress = DnsMessageBase.ParseText(resultData, ref currentPosition);
        SubAddress = (currentPosition < endPosition) ? DnsMessageBase.ParseText(resultData, ref currentPosition) : String.Empty;
    }

    internal override void ParseRecordData(DomainName origin, string[] stringRepresentation)
    {
        if (stringRepresentation.Length > 2)
            throw new FormatException();

        IsdnAddress = stringRepresentation[0];

        if (stringRepresentation.Length > 1)
            SubAddress = stringRepresentation[1];
    }

    internal override string RecordDataToString()
    {
        return IsdnAddress.ToMasterfileLabelRepresentation()
               + (String.IsNullOrEmpty(SubAddress) ? String.Empty : " " + SubAddress.ToMasterfileLabelRepresentation());
    }

    protected internal override int MaximumRecordDataLength => 2 + IsdnAddress.Length + SubAddress.Length;

    protected internal override void EncodeRecordData(Span<byte> messageData, int offset, ref int currentPosition, Dictionary<DomainName, ushort> domainNames, bool useCanonical)
    {
        DnsMessageBase.EncodeTextBlock(messageData, ref currentPosition, IsdnAddress);
        DnsMessageBase.EncodeTextBlock(messageData, ref currentPosition, SubAddress);
    }
}

internal interface ITextRecord
{
    string TextData { get; }
}

/// <summary>
///   <para>Key exchanger record</para>
///   <para>
///     Defined in
///     <see cref="!:http://tools.ietf.org/html/rfc2230">RFC 2230</see>
///   </para>
/// </summary>
public class KxRecord : DnsRecordBase
{
    /// <summary>
    ///   Preference of the record
    /// </summary>
    public ushort Preference { get; private set; }

    /// <summary>
    ///   Domain name of the exchange host
    /// </summary>
    public DomainName Exchanger { get; private set; }

    internal KxRecord() { }

    /// <summary>
    ///   Creates a new instance of the KxRecord class
    /// </summary>
    /// <param name="name"> Name of the record </param>
    /// <param name="timeToLive"> Seconds the record should be cached at most </param>
    /// <param name="preference"> Preference of the record </param>
    /// <param name="exchanger"> Domain name of the exchange host </param>
    public KxRecord(DomainName name, int timeToLive, ushort preference, DomainName exchanger)
        : base(name, RecordType.Kx, RecordClass.INet, timeToLive)
    {
        Preference = preference;
        Exchanger = exchanger ?? DomainName.Root;
    }

    internal override void ParseRecordData(DomainName origin, string[] stringRepresentation)
    {
        if (stringRepresentation.Length != 2)
            throw new FormatException();

        Preference = UInt16.Parse(stringRepresentation[0]);
        Exchanger = ParseDomainName(origin, stringRepresentation[1]);
    }

    internal override void ParseRecordData(ReadOnlySpan<byte> resultData, int startPosition, int length)
    {
        Preference = DnsMessageBase.ParseUShort(resultData, ref startPosition);
        Exchanger = DnsMessageBase.ParseDomainName(resultData, ref startPosition);
    }

    internal override string RecordDataToString()
    {
        return Preference
               + " " + Exchanger;
    }

    protected internal override int MaximumRecordDataLength => Exchanger.MaximumRecordDataLength + 4;

    protected internal override void EncodeRecordData(Span<byte> messageData, int offset, ref int currentPosition, Dictionary<DomainName, ushort> domainNames, bool useCanonical)
    {
        DnsMessageBase.EncodeUShort(messageData, ref currentPosition, Preference);
        DnsMessageBase.EncodeDomainName(messageData, offset, ref currentPosition, Exchanger, domainNames, useCanonical);
    }
}

/// <summary>
///   <para>L32</para>
///   <para>
///     Defined in
///     <see cref="!:http://tools.ietf.org/html/rfc6742">RFC 6742</see>
///   </para>
/// </summary>
public class L32Record : DnsRecordBase
{
    /// <summary>
    ///   The preference
    /// </summary>
    public ushort Preference { get; private set; }

    /// <summary>
    ///   The Locator
    /// </summary>
    public uint Locator32 { get; private set; }

    internal L32Record() { }

    /// <summary>
    ///   Creates a new instance of the L32Record class
    /// </summary>
    /// <param name="name"> Domain name of the host </param>
    /// <param name="timeToLive"> Seconds the record should be cached at most </param>
    /// <param name="preference"> The preference </param>
    /// <param name="locator32"> The Locator </param>
    public L32Record(DomainName name, int timeToLive, ushort preference, uint locator32)
        : base(name, RecordType.L32, RecordClass.INet, timeToLive)
    {
        Preference = preference;
        Locator32 = locator32;
    }

    internal override void ParseRecordData(ReadOnlySpan<byte> resultData, int startPosition, int length)
    {
        Preference = DnsMessageBase.ParseUShort(resultData, ref startPosition);
        Locator32 = DnsMessageBase.ParseUInt(resultData, ref startPosition);
    }

    internal override void ParseRecordData(DomainName origin, string[] stringRepresentation)
    {
        if (stringRepresentation.Length != 2)
            throw new FormatException();

        Preference = UInt16.Parse(stringRepresentation[0]);
        Locator32 = UInt32.Parse(stringRepresentation[1]);
    }

    internal override string RecordDataToString()
    {
        return Preference + " " + new IPAddress(Locator32);
    }

    protected internal override int MaximumRecordDataLength => 6;

    protected internal override void EncodeRecordData(Span<byte> messageData, int offset, ref int currentPosition, Dictionary<DomainName, ushort> domainNames, bool useCanonical)
    {
        DnsMessageBase.EncodeUShort(messageData, ref currentPosition, Preference);
        DnsMessageBase.EncodeUInt(messageData, ref currentPosition, Locator32);
    }
}

/// <summary>
///   <para>L64</para>
///   <para>
///     Defined in
///     <see cref="!:http://tools.ietf.org/html/rfc6742">RFC 6742</see>
///   </para>
/// </summary>
public class L64Record : DnsRecordBase
{
    /// <summary>
    ///   The preference
    /// </summary>
    public ushort Preference { get; private set; }

    /// <summary>
    ///   The Locator
    /// </summary>
    public ulong Locator64 { get; private set; }

    internal L64Record() { }

    /// <summary>
    ///   Creates a new instance of the L64Record class
    /// </summary>
    /// <param name="name"> Domain name of the host </param>
    /// <param name="timeToLive"> Seconds the record should be cached at most </param>
    /// <param name="preference"> The preference </param>
    /// <param name="locator64"> The Locator </param>
    public L64Record(DomainName name, int timeToLive, ushort preference, ulong locator64)
        : base(name, RecordType.L64, RecordClass.INet, timeToLive)
    {
        Preference = preference;
        Locator64 = locator64;
    }

    internal override void ParseRecordData(ReadOnlySpan<byte> resultData, int startPosition, int length)
    {
        Preference = DnsMessageBase.ParseUShort(resultData, ref startPosition);
        Locator64 = DnsMessageBase.ParseULong(resultData, ref startPosition);
    }

    internal override void ParseRecordData(DomainName origin, string[] stringRepresentation)
    {
        if (stringRepresentation.Length != 2)
            throw new FormatException();

        Preference = UInt16.Parse(stringRepresentation[0]);
        Locator64 = UInt64.Parse(stringRepresentation[1]);
    }

    internal override string RecordDataToString()
    {
        string locator = Locator64.ToString("x16");
        return Preference + " " + locator.Substring(0, 4) + ":" + locator.Substring(4, 4) + ":" + locator.Substring(8, 4) + ":" + locator.Substring(12);
    }

    protected internal override int MaximumRecordDataLength => 10;

    protected internal override void EncodeRecordData(Span<byte> messageData, int offset, ref int currentPosition, Dictionary<DomainName, ushort> domainNames, bool useCanonical)
    {
        DnsMessageBase.EncodeUShort(messageData, ref currentPosition, Preference);
        DnsMessageBase.EncodeULong(messageData, ref currentPosition, Locator64);
    }
}

/// <summary>
///   <para>Location information</para>
///   <para>
///     Defined in
///     <see cref="!:http://tools.ietf.org/html/rfc1876">RFC 1876</see>
///   </para>
/// </summary>
public class LocRecord : DnsRecordBase
{
    private static readonly Regex _parserRegex = new Regex(@"^(?<latd>\d{1,2})( (?<latm>\d{1,2})( (?<lats>\d{1,2})(\.(?<latms>\d{1,3}))?)?)? (?<lat>(N|S)) (?<longd>\d{1,2})( (?<longm>\d{1,2})( (?<longs>\d{1,2})(\.(?<longms>\d{1,3}))?)?)? (?<long>(W|E)) (?<alt>-?\d{1,2}(\.\d+)?)m?( (?<size>\d+(\.\d+)?)m?( (?<hp>\d+(\.\d+)?)m?( (?<vp>\d+(\.\d+)?)m?)?)?)?$", RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture | RegexOptions.Compiled);

    /// <summary>
    ///   Represents a geopgraphical degree
    /// </summary>
    public class Degree
    {
        /// <summary>
        ///   Is negative value
        /// </summary>
        public bool IsNegative { get; }

        /// <summary>
        ///   Number of full degrees
        /// </summary>
        public int Degrees { get; }

        /// <summary>
        ///   Number of minutes
        /// </summary>
        public int Minutes { get; }

        /// <summary>
        ///   Number of seconds
        /// </summary>
        public int Seconds { get; }

        /// <summary>
        ///   Number of Milliseconds
        /// </summary>
        public int Milliseconds { get; }

        /// <summary>
        ///   Returns the decimal representation of the Degree instance
        /// </summary>
        public double DecimalDegrees => (IsNegative ? -1d : 1d) * (Degrees + (double)Minutes / 6000 * 100 + (Seconds + (double)Milliseconds / 1000) / 360000 * 100);

        /// <summary>
        ///   Creates a new instance of the Degree class
        /// </summary>
        /// <param name="isNegative"> Is negative value </param>
        /// <param name="degrees"> Number of full degrees </param>
        /// <param name="minutes"> Number of minutes </param>
        /// <param name="seconds"> Number of seconds </param>
        /// <param name="milliseconds"> Number of Milliseconds </param>
        public Degree(bool isNegative, int degrees, int minutes, int seconds, int milliseconds)
        {
            IsNegative = isNegative;
            Degrees = degrees;
            Minutes = minutes;
            Seconds = seconds;
            Milliseconds = milliseconds;
        }

        internal Degree(bool isNegative, string degrees, string minutes, string seconds, string milliseconds)
        {
            IsNegative = isNegative;

            Degrees = Int32.Parse(degrees);
            Minutes = Int32.Parse(minutes.PadLeft(1, '0'));
            Seconds = Int32.Parse(seconds.PadLeft(1, '0'));
            Milliseconds = Int32.Parse(milliseconds.PadRight(3, '0'));
        }

        /// <summary>
        ///   Creates a new instance of the Degree class
        /// </summary>
        /// <param name="decimalDegrees"> Decimal representation of the Degree </param>
        public Degree(double decimalDegrees)
        {
            if (decimalDegrees < 0)
            {
                IsNegative = true;
                decimalDegrees = -decimalDegrees;
            }

            Degrees = (int)decimalDegrees;
            decimalDegrees -= Degrees;
            decimalDegrees *= 60;
            Minutes = (int)decimalDegrees;
            decimalDegrees -= Minutes;
            decimalDegrees *= 60;
            Seconds = (int)decimalDegrees;
            decimalDegrees -= Seconds;
            decimalDegrees *= 1000;
            Milliseconds = (int)decimalDegrees;
        }

        private string ToDegreeString()
        {
            string res = String.Empty;

            if (Milliseconds != 0)
                res = "." + Milliseconds.ToString().PadLeft(3, '0').TrimEnd('0');

            if ((res.Length > 0) || (Seconds != 0))
                res = " " + Seconds + res;

            if ((res.Length > 0) || (Minutes != 0))
                res = " " + Minutes + res;

            res = Degrees + res;

            return res;
        }

        internal string ToLatitudeString()
        {
            return ToDegreeString() + " " + (IsNegative ? "S" : "N");
        }

        internal string ToLongitudeString()
        {
            return ToDegreeString() + " " + (IsNegative ? "W" : "E");
        }
    }

    /// <summary>
    ///   Version number of representation
    /// </summary>
    public byte Version { get; private set; }

    /// <summary>
    ///   Size of location in centimeters
    /// </summary>
    public double Size { get; private set; }

    /// <summary>
    ///   Horizontal precision in centimeters
    /// </summary>
    public double HorizontalPrecision { get; private set; }

    /// <summary>
    ///   Vertical precision in centimeters
    /// </summary>
    public double VerticalPrecision { get; private set; }

    /// <summary>
    ///   Latitude of the geographical position
    /// </summary>
    public Degree Latitude { get; private set; }

    /// <summary>
    ///   Longitude of the geographical position
    /// </summary>
    public Degree Longitude { get; private set; }

    /// <summary>
    ///   Altitude of the geographical position
    /// </summary>
    public double Altitude { get; private set; }

    internal LocRecord() { }

    /// <summary>
    ///   Creates a new instance of the LocRecord class
    /// </summary>
    /// <param name="name"> Name of the record </param>
    /// <param name="timeToLive"> Seconds the record should be cached at most </param>
    /// <param name="version"> Version number of representation </param>
    /// <param name="size"> Size of location in centimeters </param>
    /// <param name="horizontalPrecision"> Horizontal precision in centimeters </param>
    /// <param name="verticalPrecision"> Vertical precision in centimeters </param>
    /// <param name="latitude"> Latitude of the geographical position </param>
    /// <param name="longitude"> Longitude of the geographical position </param>
    /// <param name="altitude"> Altitude of the geographical position </param>
    public LocRecord(DomainName name, int timeToLive, byte version, double size, double horizontalPrecision, double verticalPrecision, Degree latitude, Degree longitude, double altitude)
        : base(name, RecordType.Loc, RecordClass.INet, timeToLive)
    {
        Version = version;
        Size = size;
        HorizontalPrecision = horizontalPrecision;
        VerticalPrecision = verticalPrecision;
        Latitude = latitude;
        Longitude = longitude;
        Altitude = altitude;
    }

    internal override void ParseRecordData(ReadOnlySpan<byte> resultData, int currentPosition, int length)
    {
        Version = resultData[currentPosition++];
        Size = ConvertPrecision(resultData[currentPosition++]);
        HorizontalPrecision = ConvertPrecision(resultData[currentPosition++]);
        VerticalPrecision = ConvertPrecision(resultData[currentPosition++]);
        Latitude = ConvertDegree(DnsMessageBase.ParseInt(resultData, ref currentPosition));
        Longitude = ConvertDegree(DnsMessageBase.ParseInt(resultData, ref currentPosition));
        Altitude = ConvertAltitude(DnsMessageBase.ParseInt(resultData, ref currentPosition));
    }

    internal override void ParseRecordData(DomainName origin, string[] stringRepresentation)
    {
        var groups = _parserRegex
            .Match(String.Join(" ", stringRepresentation))
            .Groups;

        bool latNegative = groups["lat"].Value.Equals("S", StringComparison.InvariantCultureIgnoreCase);
        Latitude = new Degree(latNegative, groups["latd"].Value, groups["latm"].Value, groups["lats"].Value, groups["latms"].Value);

        bool longNegative = groups["long"].Value.Equals("W", StringComparison.InvariantCultureIgnoreCase);
        Longitude = new Degree(longNegative, groups["longd"].Value, groups["longm"].Value, groups["longs"].Value, groups["longms"].Value);

        Altitude = Double.Parse(groups["alt"].Value, CultureInfo.InvariantCulture);
        Size = String.IsNullOrEmpty(groups["size"].Value) ? 1 : Double.Parse(groups["size"].Value, CultureInfo.InvariantCulture);
        HorizontalPrecision = String.IsNullOrEmpty(groups["hp"].Value) ? 10000 : Double.Parse(groups["hp"].Value, CultureInfo.InvariantCulture);
        VerticalPrecision = String.IsNullOrEmpty(groups["vp"].Value) ? 10 : Double.Parse(groups["vp"].Value, CultureInfo.InvariantCulture);
    }

    [SuppressMessage("ReSharper", "CompareOfFloatsByEqualityOperator")]
    internal override string RecordDataToString()
    {
        return Latitude.ToLatitudeString()
               + " "
               + Longitude.ToLongitudeString()
               + " " + Altitude.ToString(CultureInfo.InvariantCulture) + "m"
               + (((Size != 1) || (HorizontalPrecision != 10000) || (VerticalPrecision != 10)) ? " " + Size + "m" : "")
               + (((HorizontalPrecision != 10000) || (VerticalPrecision != 10)) ? " " + HorizontalPrecision + "m" : "")
               + ((VerticalPrecision != 10) ? " " + VerticalPrecision + "m" : "");
    }

    protected internal override int MaximumRecordDataLength => 16;

    protected internal override void EncodeRecordData(Span<byte> messageData, int offset, ref int currentPosition, Dictionary<DomainName, ushort> domainNames, bool useCanonical)
    {
        messageData[currentPosition++] = Version;
        messageData[currentPosition++] = ConvertPrecision(Size);
        messageData[currentPosition++] = ConvertPrecision(HorizontalPrecision);
        messageData[currentPosition++] = ConvertPrecision(VerticalPrecision);
        DnsMessageBase.EncodeInt(messageData, ref currentPosition, ConvertDegree(Latitude));
        DnsMessageBase.EncodeInt(messageData, ref currentPosition, ConvertDegree(Longitude));
        DnsMessageBase.EncodeInt(messageData, ref currentPosition, ConvertAltitude(Altitude));
    }

    #region Convert Precision
    private static readonly int[] _powerOften = new int[] { 1, 10, 100, 1000, 10000, 100000, 1000000, 10000000, 100000000, 1000000000 };

    private static double ConvertPrecision(byte precision)
    {
        int mantissa = ((precision >> 4) & 0x0f) % 10;
        int exponent = (precision & 0x0f) % 10;
        return mantissa * (double)_powerOften[exponent] / 100;
    }

    private static byte ConvertPrecision(double precision)
    {
        double centimeters = (precision * 100);

        int exponent;
        for (exponent = 0; exponent < 9; exponent++)
        {
            if (centimeters < _powerOften[exponent + 1])
                break;
        }

        int mantissa = (int)(centimeters / _powerOften[exponent]);
        if (mantissa > 9)
            mantissa = 9;

        return (byte)((mantissa << 4) | exponent);
    }
    #endregion

    #region Convert Degree
    private static Degree ConvertDegree(int degrees)
    {
        degrees -= (1 << 31);

        bool isNegative;
        if (degrees < 0)
        {
            isNegative = true;
            degrees = -degrees;
        }
        else
        {
            isNegative = false;
        }

        int milliseconds = degrees % 1000;
        degrees /= 1000;
        int seconds = degrees % 60;
        degrees /= 60;
        int minutes = degrees % 60;
        degrees /= 60;

        return new Degree(isNegative, degrees, minutes, seconds, milliseconds);
    }

    private static int ConvertDegree(Degree degrees)
    {
        int res = degrees.Degrees * 3600000 + degrees.Minutes * 60000 + degrees.Seconds * 1000 + degrees.Milliseconds;

        if (degrees.IsNegative)
            res = -res;

        return res + (1 << 31);
    }
    #endregion

    #region Convert Altitude
    private const int _ALTITUDE_REFERENCE = 10000000;

    private static double ConvertAltitude(int altitude)
    {
        return ((altitude < _ALTITUDE_REFERENCE) ? ((_ALTITUDE_REFERENCE - altitude) * -1) : (altitude - _ALTITUDE_REFERENCE)) / 100d;
    }

    private static int ConvertAltitude(double altitude)
    {
        int centimeter = (int)(altitude * 100);
        return ((centimeter > 0) ? (_ALTITUDE_REFERENCE + centimeter) : (centimeter + _ALTITUDE_REFERENCE));
    }
    #endregion
}

/// <summary>
///   <para>LP</para>
///   <para>
///     Defined in
///     <see cref="!:http://tools.ietf.org/html/rfc6742">RFC 6742</see>
///   </para>
/// </summary>
// ReSharper disable once InconsistentNaming
public class LPRecord : DnsRecordBase
{
    /// <summary>
    ///   The preference
    /// </summary>
    public ushort Preference { get; private set; }

    /// <summary>
    ///   The FQDN
    /// </summary>
    // ReSharper disable once InconsistentNaming
    public DomainName FQDN { get; private set; }

    internal LPRecord() { }

    /// <summary>
    ///   Creates a new instance of the LpRecord class
    /// </summary>
    /// <param name="name"> Domain name of the host </param>
    /// <param name="timeToLive"> Seconds the record should be cached at most </param>
    /// <param name="preference"> The preference </param>
    /// <param name="fqdn"> The FQDN </param>
    public LPRecord(DomainName name, int timeToLive, ushort preference, DomainName fqdn)
        : base(name, RecordType.LP, RecordClass.INet, timeToLive)
    {
        Preference = preference;
        FQDN = fqdn;
    }

    internal override void ParseRecordData(ReadOnlySpan<byte> resultData, int startPosition, int length)
    {
        Preference = DnsMessageBase.ParseUShort(resultData, ref startPosition);
        FQDN = DnsMessageBase.ParseDomainName(resultData, ref startPosition);
    }

    internal override void ParseRecordData(DomainName origin, string[] stringRepresentation)
    {
        if (stringRepresentation.Length != 2)
            throw new FormatException();

        Preference = UInt16.Parse(stringRepresentation[0]);
        FQDN = ParseDomainName(origin, stringRepresentation[1]);
    }

    internal override string RecordDataToString()
    {
        return Preference + " " + FQDN;
    }

    protected internal override int MaximumRecordDataLength => 4 + FQDN.MaximumRecordDataLength;

    protected internal override void EncodeRecordData(Span<byte> messageData, int offset, ref int currentPosition, Dictionary<DomainName, ushort> domainNames, bool useCanonical)
    {
        DnsMessageBase.EncodeUShort(messageData, ref currentPosition, Preference);
        DnsMessageBase.EncodeDomainName(messageData, offset, ref currentPosition, FQDN, null, false);
    }
}

/// <summary>
///   <para>Mail exchange</para>
///   <para>
///     Defined in
///     <see cref="!:http://tools.ietf.org/html/rfc1035">RFC 1035</see>
///   </para>
/// </summary>
public class MxRecord : DnsRecordBase
{
    /// <summary>
    ///   Preference of the record
    /// </summary>
    public ushort Preference { get; private set; }

    /// <summary>
    ///   Host name of the mail exchanger
    /// </summary>
    public DomainName ExchangeDomainName { get; private set; }

    internal MxRecord() { }

    /// <summary>
    ///   Creates a new instance of the MxRecord class
    /// </summary>
    /// <param name="name"> Name of the zone </param>
    /// <param name="timeToLive"> Seconds the record should be cached at most </param>
    /// <param name="preference"> Preference of the record </param>
    /// <param name="exchangeDomainName"> Host name of the mail exchanger </param>
    public MxRecord(DomainName name, int timeToLive, ushort preference, DomainName exchangeDomainName)
        : base(name, RecordType.Mx, RecordClass.INet, timeToLive)
    {
        Preference = preference;
        ExchangeDomainName = exchangeDomainName ?? DomainName.Root;
    }

    internal override void ParseRecordData(ReadOnlySpan<byte> resultData, int startPosition, int length)
    {
        Preference = DnsMessageBase.ParseUShort(resultData, ref startPosition);
        ExchangeDomainName = DnsMessageBase.ParseDomainName(resultData, ref startPosition);
    }

    internal override void ParseRecordData(DomainName origin, string[] stringRepresentation)
    {
        if (stringRepresentation.Length != 2)
            throw new FormatException();

        Preference = UInt16.Parse(stringRepresentation[0]);
        ExchangeDomainName = ParseDomainName(origin, stringRepresentation[1]);
    }

    internal override string RecordDataToString()
    {
        return Preference
               + " " + ExchangeDomainName;
    }

    protected internal override int MaximumRecordDataLength => ExchangeDomainName.MaximumRecordDataLength + 4;

    protected internal override void EncodeRecordData(Span<byte> messageData, int offset, ref int currentPosition, Dictionary<DomainName, ushort> domainNames, bool useCanonical)
    {
        DnsMessageBase.EncodeUShort(messageData, ref currentPosition, Preference);
        DnsMessageBase.EncodeDomainName(messageData, offset, ref currentPosition, ExchangeDomainName, domainNames, useCanonical);
    }
}

/// <summary>
///   <para>Naming authority pointer record</para>
///   <para>
///     Defined in
///     <see cref="!:http://tools.ietf.org/html/rfc2915">RFC 2915</see>
///     ,
///     <see cref="!:http://tools.ietf.org/html/rfc2168">RFC 2168</see>
///     and
///     <see cref="!:http://tools.ietf.org/html/rfc3403">RFC 3403</see>
///   </para>
/// </summary>
public class NaptrRecord : DnsRecordBase
{
    /// <summary>
    ///   Order of the record
    /// </summary>
    public ushort Order { get; private set; }

    /// <summary>
    ///   Preference of record with same order
    /// </summary>
    public ushort Preference { get; private set; }

    /// <summary>
    ///   Flags of the record
    /// </summary>
    public string Flags { get; private set; }

    /// <summary>
    ///   Available services
    /// </summary>
    public string Services { get; private set; }

    /// <summary>
    ///   Substitution expression that is applied to the original string
    /// </summary>
    public string RegExp { get; private set; }

    /// <summary>
    ///   The next name to query
    /// </summary>
    public DomainName Replacement { get; private set; }

    internal NaptrRecord() { }

    /// <summary>
    ///   Creates a new instance of the NaptrRecord class
    /// </summary>
    /// <param name="name"> Name of the record </param>
    /// <param name="timeToLive"> Seconds the record should be cached at most </param>
    /// <param name="order"> Order of the record </param>
    /// <param name="preference"> Preference of record with same order </param>
    /// <param name="flags"> Flags of the record </param>
    /// <param name="services"> Available services </param>
    /// <param name="regExp"> Substitution expression that is applied to the original string </param>
    /// <param name="replacement"> The next name to query </param>
    public NaptrRecord(DomainName name, int timeToLive, ushort order, ushort preference, string flags, string services, string regExp, DomainName replacement)
        : base(name, RecordType.Naptr, RecordClass.INet, timeToLive)
    {
        Order = order;
        Preference = preference;
        Flags = flags ?? String.Empty;
        Services = services ?? String.Empty;
        RegExp = regExp ?? String.Empty;
        Replacement = replacement ?? DomainName.Root;
    }

    internal override void ParseRecordData(ReadOnlySpan<byte> resultData, int startPosition, int length)
    {
        Order = DnsMessageBase.ParseUShort(resultData, ref startPosition);
        Preference = DnsMessageBase.ParseUShort(resultData, ref startPosition);
        Flags = DnsMessageBase.ParseText(resultData, ref startPosition);
        Services = DnsMessageBase.ParseText(resultData, ref startPosition);
        RegExp = DnsMessageBase.ParseText(resultData, ref startPosition);
        Replacement = DnsMessageBase.ParseDomainName(resultData, ref startPosition);
    }

    internal override void ParseRecordData(DomainName origin, string[] stringRepresentation)
    {
        if (stringRepresentation.Length != 6)
            throw new NotSupportedException();

        Order = UInt16.Parse(stringRepresentation[0]);
        Preference = UInt16.Parse(stringRepresentation[1]);
        Flags = stringRepresentation[2];
        Services = stringRepresentation[3];
        RegExp = stringRepresentation[4];
        Replacement = ParseDomainName(origin, stringRepresentation[5]);
    }

    internal override string RecordDataToString()
    {
        return Order
               + " " + Preference
               + " \"" + Flags.ToMasterfileLabelRepresentation() + "\""
               + " \"" + Services.ToMasterfileLabelRepresentation() + "\""
               + " \"" + RegExp.ToMasterfileLabelRepresentation() + "\""
               + " " + Replacement;
    }

    protected internal override int MaximumRecordDataLength => Flags.Length + Services.Length + RegExp.Length + Replacement.MaximumRecordDataLength + 13;

    protected internal override void EncodeRecordData(Span<byte> messageData, int offset, ref int currentPosition, Dictionary<DomainName, ushort> domainNames, bool useCanonical)
    {
        DnsMessageBase.EncodeUShort(messageData, ref currentPosition, Order);
        DnsMessageBase.EncodeUShort(messageData, ref currentPosition, Preference);
        DnsMessageBase.EncodeTextBlock(messageData, ref currentPosition, Flags);
        DnsMessageBase.EncodeTextBlock(messageData, ref currentPosition, Services);
        DnsMessageBase.EncodeTextBlock(messageData, ref currentPosition, RegExp);
        DnsMessageBase.EncodeDomainName(messageData, offset, ref currentPosition, Replacement, null, useCanonical);
    }
}

/// <summary>
///   <para>NID</para>
///   <para>
///     Defined in
///     <see cref="!:http://tools.ietf.org/html/rfc6742">RFC 6742</see>
///   </para>
/// </summary>
public class NIdRecord : DnsRecordBase
{
    /// <summary>
    ///   The preference
    /// </summary>
    public ushort Preference { get; private set; }

    /// <summary>
    ///   The Node ID
    /// </summary>
    public ulong NodeID { get; private set; }

    internal NIdRecord() { }

    /// <summary>
    ///   Creates a new instance of the NIdRecord class
    /// </summary>
    /// <param name="name"> Domain name of the host </param>
    /// <param name="timeToLive"> Seconds the record should be cached at most </param>
    /// <param name="preference"> The preference </param>
    /// <param name="nodeID"> The Node ID </param>
    public NIdRecord(DomainName name, int timeToLive, ushort preference, ulong nodeID)
        : base(name, RecordType.NId, RecordClass.INet, timeToLive)
    {
        Preference = preference;
        NodeID = nodeID;
    }

    internal override void ParseRecordData(ReadOnlySpan<byte> resultData, int startPosition, int length)
    {
        Preference = DnsMessageBase.ParseUShort(resultData, ref startPosition);
        NodeID = DnsMessageBase.ParseULong(resultData, ref startPosition);
    }

    internal override void ParseRecordData(DomainName origin, string[] stringRepresentation)
    {
        if (stringRepresentation.Length != 2)
            throw new FormatException();

        Preference = UInt16.Parse(stringRepresentation[0]);

        string[] nodeIDParts = stringRepresentation[1].Split(':');

        if (nodeIDParts.Length != 4)
            throw new FormatException();

        for (int i = 0; i < 4; i++)
        {
            if (nodeIDParts[i].Length != 4)
                throw new FormatException();

            NodeID = NodeID << 16;
            NodeID |= Convert.ToUInt16(nodeIDParts[i], 16);
        }
    }

    internal override string RecordDataToString()
    {
        string nodeID = NodeID.ToString("x16");
        return Preference + " " + nodeID.Substring(0, 4) + ":" + nodeID.Substring(4, 4) + ":" + nodeID.Substring(8, 4) + ":" + nodeID.Substring(12);
    }

    protected internal override int MaximumRecordDataLength => 10;

    protected internal override void EncodeRecordData(Span<byte> messageData, int offset, ref int currentPosition, Dictionary<DomainName, ushort> domainNames, bool useCanonical)
    {
        DnsMessageBase.EncodeUShort(messageData, ref currentPosition, Preference);
        DnsMessageBase.EncodeULong(messageData, ref currentPosition, NodeID);
    }
}

/// <summary>
///   <para>NSAP address, NSAP style A record</para>
///   <para>
///     Defined in
///     <see cref="!:http://tools.ietf.org/html/rfc1706">RFC 1706</see>
///     and
///     <see cref="!:http://tools.ietf.org/html/rfc1348">RFC 1348</see>
///   </para>
/// </summary>
public class NsapRecord : DnsRecordBase
{
    /// <summary>
    ///   Binary encoded NSAP data
    /// </summary>
    public byte[] RecordData { get; private set; }

    internal NsapRecord() { }

    /// <summary>
    ///   Creates a new instance of the NsapRecord class
    /// </summary>
    /// <param name="name"> Name of the record </param>
    /// <param name="timeToLive"> Seconds the record should be cached at most </param>
    /// <param name="recordData"> Binary encoded NSAP data </param>
    public NsapRecord(DomainName name, int timeToLive, byte[] recordData)
        : base(name, RecordType.Nsap, RecordClass.INet, timeToLive)
    {
        RecordData = recordData ?? new byte[] { };
    }

    internal override void ParseRecordData(ReadOnlySpan<byte> resultData, int startPosition, int length)
    {
        RecordData = DnsMessageBase.ParseByteData(resultData, ref startPosition, length);
    }

    internal override void ParseRecordData(DomainName origin, string[] stringRepresentation)
    {
        if (stringRepresentation.Length != 1)
            throw new FormatException();

        if (!stringRepresentation[0].StartsWith("0x", StringComparison.InvariantCultureIgnoreCase))
            throw new FormatException();

        RecordData = stringRepresentation[0].Substring(2).Replace(".", String.Empty).FromBase16String();
    }

    internal override string RecordDataToString()
    {
        return "0x" + RecordData.ToBase16String();
    }

    protected internal override int MaximumRecordDataLength => RecordData.Length;

    protected internal override void EncodeRecordData(Span<byte> messageData, int offset, ref int currentPosition, Dictionary<DomainName, ushort> domainNames, bool useCanonical)
    {
        DnsMessageBase.EncodeByteArray(messageData, ref currentPosition, RecordData);
    }
}

/// <summary>
///   <para>Authoritatitve name server record</para>
///   <para>
///     Defined in
///     <see cref="!:http://tools.ietf.org/html/rfc1035">RFC 1035</see>
///   </para>
/// </summary>
public class NsRecord : DnsRecordBase
{
    /// <summary>
    ///   Name of the authoritatitve nameserver for the zone
    /// </summary>
    public DomainName NameServer { get; private set; }

    internal NsRecord() { }

    /// <summary>
    ///   Creates a new instance of the NsRecord class
    /// </summary>
    /// <param name="name"> Domain name of the zone </param>
    /// <param name="timeToLive"> Seconds the record should be cached at most </param>
    /// <param name="nameServer"> Name of the authoritative name server </param>
    public NsRecord(DomainName name, int timeToLive, DomainName nameServer)
        : base(name, RecordType.Ns, RecordClass.INet, timeToLive)
    {
        NameServer = nameServer ?? DomainName.Root;
    }

    internal override void ParseRecordData(ReadOnlySpan<byte> resultData, int startPosition, int length)
    {
        NameServer = DnsMessageBase.ParseDomainName(resultData, ref startPosition);
    }

    internal override void ParseRecordData(DomainName origin, string[] stringRepresentation)
    {
        if (stringRepresentation.Length != 1)
            throw new FormatException();

        NameServer = ParseDomainName(origin, stringRepresentation[0]);
    }

    internal override string RecordDataToString()
    {
        return NameServer.ToString();
    }

    protected internal override int MaximumRecordDataLength => NameServer.MaximumRecordDataLength + 2;

    protected internal override void EncodeRecordData(Span<byte> messageData, int offset, ref int currentPosition, Dictionary<DomainName, ushort> domainNames, bool useCanonical)
    {
        DnsMessageBase.EncodeDomainName(messageData, offset, ref currentPosition, NameServer, domainNames, useCanonical);
    }
}

/// <summary>
///   <para>OpenPGP Key</para>
///   <para>
///     Defined in
///     <see cref="!:http://tools.ietf.org/html/draft-ietf-dane-openpgpkey">draft-ietf-dane-openpgpkey</see>
///   </para>
/// </summary>
// ReSharper disable once InconsistentNaming
public class OpenPGPKeyRecord : DnsRecordBase
{
    /// <summary>
    ///   The Public Key
    /// </summary>
    public byte[] PublicKey { get; private set; }

    internal OpenPGPKeyRecord() { }

    /// <summary>
    ///   Creates a new instance of the OpenPGPKeyRecord class
    /// </summary>
    /// <param name="name"> Domain name of the host </param>
    /// <param name="timeToLive"> Seconds the record should be cached at most </param>
    /// <param name="publicKey"> The Public Key</param>
    public OpenPGPKeyRecord(DomainName name, int timeToLive, byte[] publicKey)
        : base(name, RecordType.OpenPGPKey, RecordClass.INet, timeToLive)
    {
        PublicKey = publicKey ?? new byte[] { };
    }

    internal override void ParseRecordData(ReadOnlySpan<byte> resultData, int startPosition, int length)
    {
        PublicKey = DnsMessageBase.ParseByteData(resultData, ref startPosition, length);
    }

    internal override void ParseRecordData(DomainName origin, string[] stringRepresentation)
    {
        if (stringRepresentation.Length == 0)
            throw new NotSupportedException();

        PublicKey = String.Join(String.Empty, stringRepresentation).FromBase64String();
    }

    internal override string RecordDataToString()
    {
        return PublicKey.ToBase64String();
    }

    protected internal override int MaximumRecordDataLength => PublicKey.Length;

    protected internal override void EncodeRecordData(Span<byte> messageData, int offset, ref int currentPosition, Dictionary<DomainName, ushort> domainNames, bool useCanonical)
    {
        DnsMessageBase.EncodeByteArray(messageData, ref currentPosition, PublicKey);
    }
}

/// <summary>
///   <para>Domain name pointer</para>
///   <para>
///     Defined in
///     <see cref="!:http://tools.ietf.org/html/rfc1035">RFC 1035</see>
///   </para>
/// </summary>
public class PtrRecord : DnsRecordBase
{
    /// <summary>
    ///   Domain name the address points to
    /// </summary>
    public DomainName PointerDomainName { get; private set; }

    internal PtrRecord() { }

    /// <summary>
    ///   Creates a new instance of the PtrRecord class
    /// </summary>
    /// <param name="name"> Reverse name of the address </param>
    /// <param name="timeToLive"> Seconds the record should be cached at most </param>
    /// <param name="pointerDomainName"> Domain name the address points to </param>
    public PtrRecord(DomainName name, int timeToLive, DomainName pointerDomainName)
        : base(name, RecordType.Ptr, RecordClass.INet, timeToLive)
    {
        PointerDomainName = pointerDomainName ?? DomainName.Root;
    }

    internal override void ParseRecordData(ReadOnlySpan<byte> resultData, int startPosition, int length)
    {
        PointerDomainName = DnsMessageBase.ParseDomainName(resultData, ref startPosition);
    }

    internal override void ParseRecordData(DomainName origin, string[] stringRepresentation)
    {
        if (stringRepresentation.Length != 1)
            throw new FormatException();

        PointerDomainName = ParseDomainName(origin, stringRepresentation[0]);
    }

    internal override string RecordDataToString()
    {
        return PointerDomainName.ToString();
    }

    protected internal override int MaximumRecordDataLength => PointerDomainName.MaximumRecordDataLength + 2;

    protected internal override void EncodeRecordData(Span<byte> messageData, int offset, ref int currentPosition, Dictionary<DomainName, ushort> domainNames, bool useCanonical)
    {
        DnsMessageBase.EncodeDomainName(messageData, offset, ref currentPosition, PointerDomainName, domainNames, useCanonical);
    }
}

/// <summary>
///   <para>X.400 mail mapping information record</para>
///   <para>
///     Defined in
///     <see cref="!:http://tools.ietf.org/html/rfc2163">RFC 2163</see>
///   </para>
/// </summary>
public class PxRecord : DnsRecordBase
{
    /// <summary>
    ///   Preference of the record
    /// </summary>
    public ushort Preference { get; private set; }

    /// <summary>
    ///   Domain name containing the RFC822 domain
    /// </summary>
    public DomainName Map822 { get; private set; }

    /// <summary>
    ///   Domain name containing the X.400 part
    /// </summary>
    public DomainName MapX400 { get; private set; }

    internal PxRecord() { }

    /// <summary>
    ///   Creates a new instance of the PxRecord class
    /// </summary>
    /// <param name="name"> Name of the record </param>
    /// <param name="timeToLive"> Seconds the record should be cached at most </param>
    /// <param name="preference"> Preference of the record </param>
    /// <param name="map822"> Domain name containing the RFC822 domain </param>
    /// <param name="mapX400"> Domain name containing the X.400 part </param>
    public PxRecord(DomainName name, int timeToLive, ushort preference, DomainName map822, DomainName mapX400)
        : base(name, RecordType.Px, RecordClass.INet, timeToLive)
    {
        Preference = preference;
        Map822 = map822 ?? DomainName.Root;
        MapX400 = mapX400 ?? DomainName.Root;
    }

    internal override void ParseRecordData(ReadOnlySpan<byte> resultData, int startPosition, int length)
    {
        Preference = DnsMessageBase.ParseUShort(resultData, ref startPosition);
        Map822 = DnsMessageBase.ParseDomainName(resultData, ref startPosition);
        MapX400 = DnsMessageBase.ParseDomainName(resultData, ref startPosition);
    }

    internal override void ParseRecordData(DomainName origin, string[] stringRepresentation)
    {
        if (stringRepresentation.Length != 3)
            throw new FormatException();

        Preference = UInt16.Parse(stringRepresentation[0]);
        Map822 = ParseDomainName(origin, stringRepresentation[1]);
        MapX400 = ParseDomainName(origin, stringRepresentation[2]);
    }

    internal override string RecordDataToString()
    {
        return Preference
               + " " + Map822
               + " " + MapX400;
    }

    protected internal override int MaximumRecordDataLength => 6 + Map822.MaximumRecordDataLength + MapX400.MaximumRecordDataLength;

    protected internal override void EncodeRecordData(Span<byte> messageData, int offset, ref int currentPosition, Dictionary<DomainName, ushort> domainNames, bool useCanonical)
    {
        DnsMessageBase.EncodeUShort(messageData, ref currentPosition, Preference);
        DnsMessageBase.EncodeDomainName(messageData, offset, ref currentPosition, Map822, null, useCanonical);
        DnsMessageBase.EncodeDomainName(messageData, offset, ref currentPosition, MapX400, null, useCanonical);
    }
}

/// <summary>
///   <para>Responsible person record</para>
///   <para>
///     Defined in
///     <see cref="!:http://tools.ietf.org/html/rfc1183">RFC 1183</see>
///   </para>
/// </summary>
public class RpRecord : DnsRecordBase
{
    /// <summary>
    ///   Mail address of responsable person, the @ should be replaced by a dot
    /// </summary>
    public DomainName MailBox { get; protected set; }

    /// <summary>
    ///   Domain name of a <see cref="TxtRecord" /> with additional information
    /// </summary>
    public DomainName TxtDomainName { get; protected set; }

    internal RpRecord() { }

    /// <summary>
    ///   Creates a new instance of the RpRecord class
    /// </summary>
    /// <param name="name"> Name of the record </param>
    /// <param name="timeToLive"> Seconds the record should be cached at most </param>
    /// <param name="mailBox"> Mail address of responsable person, the @ should be replaced by a dot </param>
    /// <param name="txtDomainName">
    ///   Domain name of a <see cref="TxtRecord" /> with additional information
    /// </param>
    public RpRecord(DomainName name, int timeToLive, DomainName mailBox, DomainName txtDomainName)
        : base(name, RecordType.Rp, RecordClass.INet, timeToLive)
    {
        MailBox = mailBox ?? DomainName.Root;
        TxtDomainName = txtDomainName ?? DomainName.Root;
    }

    internal override void ParseRecordData(ReadOnlySpan<byte> resultData, int startPosition, int length)
    {
        MailBox = DnsMessageBase.ParseDomainName(resultData, ref startPosition);
        TxtDomainName = DnsMessageBase.ParseDomainName(resultData, ref startPosition);
    }

    internal override void ParseRecordData(DomainName origin, string[] stringRepresentation)
    {
        if (stringRepresentation.Length != 2)
            throw new FormatException();

        MailBox = ParseDomainName(origin, stringRepresentation[0]);
        TxtDomainName = ParseDomainName(origin, stringRepresentation[1]);
    }

    internal override string RecordDataToString()
    {
        return MailBox
               + " " + TxtDomainName;
    }

    protected internal override int MaximumRecordDataLength => 4 + MailBox.MaximumRecordDataLength + TxtDomainName.MaximumRecordDataLength;

    protected internal override void EncodeRecordData(Span<byte> messageData, int offset, ref int currentPosition, Dictionary<DomainName, ushort> domainNames, bool useCanonical)
    {
        DnsMessageBase.EncodeDomainName(messageData, offset, ref currentPosition, MailBox, null, useCanonical);
        DnsMessageBase.EncodeDomainName(messageData, offset, ref currentPosition, TxtDomainName, null, useCanonical);
    }
}

/// <summary>
///   <para>Route through record</para>
///   <para>
///     Defined in
///     <see cref="!:http://tools.ietf.org/html/rfc1183">RFC 1183</see>
///   </para>
/// </summary>
public class RtRecord : DnsRecordBase
{
    /// <summary>
    ///   Preference of the record
    /// </summary>
    public ushort Preference { get; private set; }

    /// <summary>
    ///   Name of the intermediate host
    /// </summary>
    public DomainName IntermediateHost { get; private set; }

    internal RtRecord() { }

    /// <summary>
    ///   Creates a new instance of the RtRecord class
    /// </summary>
    /// <param name="name"> Name of the record </param>
    /// <param name="timeToLive"> Seconds the record should be cached at most </param>
    /// <param name="preference"> Preference of the record </param>
    /// <param name="intermediateHost"> Name of the intermediate host </param>
    public RtRecord(DomainName name, int timeToLive, ushort preference, DomainName intermediateHost)
        : base(name, RecordType.Rt, RecordClass.INet, timeToLive)
    {
        Preference = preference;
        IntermediateHost = intermediateHost ?? DomainName.Root;
    }

    internal override void ParseRecordData(DomainName origin, string[] stringRepresentation)
    {
        if (stringRepresentation.Length != 2)
            throw new FormatException();

        Preference = UInt16.Parse(stringRepresentation[0]);
        IntermediateHost = ParseDomainName(origin, stringRepresentation[1]);
    }

    internal override void ParseRecordData(ReadOnlySpan<byte> resultData, int startPosition, int length)
    {
        Preference = DnsMessageBase.ParseUShort(resultData, ref startPosition);
        IntermediateHost = DnsMessageBase.ParseDomainName(resultData, ref startPosition);
    }

    internal override string RecordDataToString()
    {
        return Preference
               + " " + IntermediateHost;
    }

    protected internal override int MaximumRecordDataLength => IntermediateHost.MaximumRecordDataLength + 4;

    protected internal override void EncodeRecordData(Span<byte> messageData, int offset, ref int currentPosition, Dictionary<DomainName, ushort> domainNames, bool useCanonical)
    {
        DnsMessageBase.EncodeUShort(messageData, ref currentPosition, Preference);
        DnsMessageBase.EncodeDomainName(messageData, offset, ref currentPosition, IntermediateHost, null, useCanonical);
    }
}

/// <summary>
///   <para>Start of zone of authority record</para>
///   <para>
///     Defined in
///     <see cref="!:http://tools.ietf.org/html/rfc1035">RFC 1035</see>
///   </para>
/// </summary>
public class SoaRecord : DnsRecordBase
{
    /// <summary>
    ///   Hostname of the primary name server
    /// </summary>
    public DomainName MasterName { get; private set; }

    /// <summary>
    ///   Mail address of the responsable person. The @ should be replaced by a dot.
    /// </summary>
    public DomainName ResponsibleName { get; private set; }

    /// <summary>
    ///   Serial number of the zone
    /// </summary>
    public uint SerialNumber { get; private set; }

    /// <summary>
    ///   Seconds before the zone should be refreshed
    /// </summary>
    public int RefreshInterval { get; private set; }

    /// <summary>
    ///   Seconds that should be elapsed before retry of failed transfer
    /// </summary>
    public int RetryInterval { get; private set; }

    /// <summary>
    ///   Seconds that can elapse before the zone is no longer authorative
    /// </summary>
    public int ExpireInterval { get; private set; }

    /// <summary>
    ///   <para>Seconds a negative answer could be cached</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc2308">RFC 2308</see>
    ///   </para>
    /// </summary>
    // ReSharper disable once InconsistentNaming
    public int NegativeCachingTTL { get; private set; }

    internal SoaRecord() { }

    /// <summary>
    ///   Creates a new instance of the SoaRecord class
    /// </summary>
    /// <param name="name"> Name of the zone </param>
    /// <param name="timeToLive"> Seconds the record should be cached at most </param>
    /// <param name="masterName"> Hostname of the primary name server </param>
    /// <param name="responsibleName"> Mail address of the responsable person. The @ should be replaced by a dot. </param>
    /// <param name="serialNumber"> Serial number of the zone </param>
    /// <param name="refreshInterval"> Seconds before the zone should be refreshed </param>
    /// <param name="retryInterval"> Seconds that should be elapsed before retry of failed transfer </param>
    /// <param name="expireInterval"> Seconds that can elapse before the zone is no longer authorative </param>
    /// <param name="negativeCachingTTL">
    ///   <para>Seconds a negative answer could be cached</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc2308">RFC 2308</see>
    ///   </para>
    /// </param>
    // ReSharper disable once InconsistentNaming
    public SoaRecord(DomainName name, int timeToLive, DomainName masterName, DomainName responsibleName, uint serialNumber, int refreshInterval, int retryInterval, int expireInterval, int negativeCachingTTL)
        : base(name, RecordType.Soa, RecordClass.INet, timeToLive)
    {
        MasterName = masterName ?? DomainName.Root;
        ResponsibleName = responsibleName ?? DomainName.Root;
        SerialNumber = serialNumber;
        RefreshInterval = refreshInterval;
        RetryInterval = retryInterval;
        ExpireInterval = expireInterval;
        NegativeCachingTTL = negativeCachingTTL;
    }

    internal override void ParseRecordData(ReadOnlySpan<byte> resultData, int startPosition, int length)
    {
        MasterName = DnsMessageBase.ParseDomainName(resultData, ref startPosition);
        ResponsibleName = DnsMessageBase.ParseDomainName(resultData, ref startPosition);

        SerialNumber = DnsMessageBase.ParseUInt(resultData, ref startPosition);
        RefreshInterval = DnsMessageBase.ParseInt(resultData, ref startPosition);
        RetryInterval = DnsMessageBase.ParseInt(resultData, ref startPosition);
        ExpireInterval = DnsMessageBase.ParseInt(resultData, ref startPosition);
        NegativeCachingTTL = DnsMessageBase.ParseInt(resultData, ref startPosition);
    }

    internal override void ParseRecordData(DomainName origin, string[] stringRepresentation)
    {
        MasterName = ParseDomainName(origin, stringRepresentation[0]);
        ResponsibleName = ParseDomainName(origin, stringRepresentation[1]);

        SerialNumber = UInt32.Parse(stringRepresentation[2]);
        RefreshInterval = Int32.Parse(stringRepresentation[3]);
        RetryInterval = Int32.Parse(stringRepresentation[4]);
        ExpireInterval = Int32.Parse(stringRepresentation[5]);
        NegativeCachingTTL = Int32.Parse(stringRepresentation[6]);
    }

    internal override string RecordDataToString()
    {
        return MasterName
               + " " + ResponsibleName
               + " " + SerialNumber
               + " " + RefreshInterval
               + " " + RetryInterval
               + " " + ExpireInterval
               + " " + NegativeCachingTTL;
    }

    protected internal override int MaximumRecordDataLength => MasterName.MaximumRecordDataLength + ResponsibleName.MaximumRecordDataLength + 24;

    protected internal override void EncodeRecordData(Span<byte> messageData, int offset, ref int currentPosition, Dictionary<DomainName, ushort> domainNames, bool useCanonical)
    {
        DnsMessageBase.EncodeDomainName(messageData, offset, ref currentPosition, MasterName, domainNames, useCanonical);
        DnsMessageBase.EncodeDomainName(messageData, offset, ref currentPosition, ResponsibleName, domainNames, useCanonical);
        DnsMessageBase.EncodeUInt(messageData, ref currentPosition, SerialNumber);
        DnsMessageBase.EncodeInt(messageData, ref currentPosition, RefreshInterval);
        DnsMessageBase.EncodeInt(messageData, ref currentPosition, RetryInterval);
        DnsMessageBase.EncodeInt(messageData, ref currentPosition, ExpireInterval);
        DnsMessageBase.EncodeInt(messageData, ref currentPosition, NegativeCachingTTL);
    }
}

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
public class SpfRecord2 : TextRecordBase
{
    internal SpfRecord2() { }

    /// <summary>
    ///   Creates a new instance of the SpfRecord class
    /// </summary>
    /// <param name="name"> Name of the record </param>
    /// <param name="timeToLive"> Seconds the record should be cached at most </param>
    /// <param name="textData"> Text data of the record </param>
    public SpfRecord2(DomainName name, int timeToLive, string textData)
        : base(name, RecordType.Spf, timeToLive, textData) { }

    /// <summary>
    ///   Creates a new instance of the SpfRecord class
    /// </summary>
    /// <param name="name"> Name of the record </param>
    /// <param name="timeToLive"> Seconds the record should be cached at most </param>
    /// <param name="textParts"> All parts of the text data </param>
    public SpfRecord2(DomainName name, int timeToLive, IEnumerable<string> textParts)
        : base(name, RecordType.Spf, timeToLive, textParts) { }
}

/// <summary>
///   <para>Server selector</para>
///   <para>
///     Defined in
///     <see cref="!:http://tools.ietf.org/html/rfc2782">RFC 2782</see>
///   </para>
/// </summary>
public class SrvRecord : DnsRecordBase
{
    /// <summary>
    ///   Priority of the record
    /// </summary>
    public ushort Priority { get; private set; }

    /// <summary>
    ///   Relative weight for records with the same priority
    /// </summary>
    public ushort Weight { get; private set; }

    /// <summary>
    ///   The port of the service on the target
    /// </summary>
    public ushort Port { get; private set; }

    /// <summary>
    ///   Domain name of the target host
    /// </summary>
    public DomainName Target { get; private set; }

    internal SrvRecord() { }

    /// <summary>
    ///   Creates a new instance of the SrvRecord class
    /// </summary>
    /// <param name="name"> Name of the record </param>
    /// <param name="timeToLive"> Seconds the record should be cached at most </param>
    /// <param name="priority"> Priority of the record </param>
    /// <param name="weight"> Relative weight for records with the same priority </param>
    /// <param name="port"> The port of the service on the target </param>
    /// <param name="target"> Domain name of the target host </param>
    public SrvRecord(DomainName name, int timeToLive, ushort priority, ushort weight, ushort port, DomainName target)
        : base(name, RecordType.Srv, RecordClass.INet, timeToLive)
    {
        Priority = priority;
        Weight = weight;
        Port = port;
        Target = target ?? DomainName.Root;
    }

    internal override void ParseRecordData(ReadOnlySpan<byte> resultData, int startPosition, int length)
    {
        Priority = DnsMessageBase.ParseUShort(resultData, ref startPosition);
        Weight = DnsMessageBase.ParseUShort(resultData, ref startPosition);
        Port = DnsMessageBase.ParseUShort(resultData, ref startPosition);
        Target = DnsMessageBase.ParseDomainName(resultData, ref startPosition);
    }

    internal override void ParseRecordData(DomainName origin, string[] stringRepresentation)
    {
        if (stringRepresentation.Length != 4)
            throw new FormatException();

        Priority = UInt16.Parse(stringRepresentation[0]);
        Weight = UInt16.Parse(stringRepresentation[1]);
        Port = UInt16.Parse(stringRepresentation[2]);
        Target = ParseDomainName(origin, stringRepresentation[3]);
    }

    internal override string RecordDataToString()
    {
        return Priority
               + " " + Weight
               + " " + Port
               + " " + Target;
    }

    protected internal override int MaximumRecordDataLength => Target.MaximumRecordDataLength + 8;

    protected internal override void EncodeRecordData(Span<byte> messageData, int offset, ref int currentPosition, Dictionary<DomainName, ushort> domainNames, bool useCanonical)
    {
        DnsMessageBase.EncodeUShort(messageData, ref currentPosition, Priority);
        DnsMessageBase.EncodeUShort(messageData, ref currentPosition, Weight);
        DnsMessageBase.EncodeUShort(messageData, ref currentPosition, Port);
        DnsMessageBase.EncodeDomainName(messageData, offset, ref currentPosition, Target, null, useCanonical);
    }
}

/// <summary>
///   <para>SSH key fingerprint record</para>
///   <para>
///     Defined in
///     <see cref="!:http://tools.ietf.org/html/rfc4255">RFC 4255</see>
///   </para>
/// </summary>
public class SshFpRecord : DnsRecordBase
{
    /// <summary>
    ///   Algorithm of the fingerprint
    /// </summary>
    public enum SshFpAlgorithm : byte
    {
        /// <summary>
        ///   None
        /// </summary>
        None = 0,

        /// <summary>
        ///   <para>RSA</para>
        ///   <para>
        ///     Defined in
        ///     <see cref="!:http://tools.ietf.org/html/rfc4255">RFC 4255</see>
        ///   </para>
        /// </summary>
        Rsa = 1,

        /// <summary>
        ///   <para>DSA</para>
        ///   <para>
        ///     Defined in
        ///     <see cref="!:http://tools.ietf.org/html/rfc4255">RFC 4255</see>
        ///   </para>
        /// </summary>
        Dsa = 2,

        /// <summary>
        ///   <para>ECDSA</para>
        ///   <para>
        ///     Defined in
        ///     <see cref="!:http://tools.ietf.org/html/rfc6594">RFC 6594</see>
        ///   </para>
        /// </summary>
        EcDsa = 3,

        /// <summary>
        ///   <para>Ed25519</para>
        ///   <para>
        ///     Defined in
        ///     <see cref="!:http://tools.ietf.org/html/rfc7479">RFC 7479</see>
        ///   </para>
        /// </summary>
        Ed25519 = 4,
    }

    /// <summary>
    ///   Type of the fingerprint
    /// </summary>
    public enum SshFpFingerPrintType : byte
    {
        /// <summary>
        ///   None
        /// </summary>
        None = 0,

        /// <summary>
        ///   <para>SHA-1</para>
        ///   <para>
        ///     Defined in
        ///     <see cref="!:http://tools.ietf.org/html/rfc4255">RFC 4255</see>
        ///   </para>
        /// </summary>
        Sha1 = 1,

        /// <summary>
        ///   <para>SHA-1</para>
        ///   <para>
        ///     Defined in
        ///     <see cref="!:http://tools.ietf.org/html/rfc6594">RFC 6594</see>
        ///   </para>
        /// </summary>
        Sha256 = 2,
    }

    /// <summary>
    ///   Algorithm of fingerprint
    /// </summary>
    public SshFpAlgorithm Algorithm { get; private set; }

    /// <summary>
    ///   Type of fingerprint
    /// </summary>
    public SshFpFingerPrintType FingerPrintType { get; private set; }

    /// <summary>
    ///   Binary data of the fingerprint
    /// </summary>
    public byte[] FingerPrint { get; private set; }

    internal SshFpRecord() { }

    /// <summary>
    ///   Creates a new instance of the SshFpRecord class
    /// </summary>
    /// <param name="name"> Name of the record </param>
    /// <param name="timeToLive"> Seconds the record should be cached at most </param>
    /// <param name="algorithm"> Algorithm of fingerprint </param>
    /// <param name="fingerPrintType"> Type of fingerprint </param>
    /// <param name="fingerPrint"> Binary data of the fingerprint </param>
    public SshFpRecord(DomainName name, int timeToLive, SshFpAlgorithm algorithm, SshFpFingerPrintType fingerPrintType, byte[] fingerPrint)
        : base(name, RecordType.SshFp, RecordClass.INet, timeToLive)
    {
        Algorithm = algorithm;
        FingerPrintType = fingerPrintType;
        FingerPrint = fingerPrint ?? new byte[] { };
    }

    internal override void ParseRecordData(ReadOnlySpan<byte> resultData, int currentPosition, int length)
    {
        Algorithm = (SshFpAlgorithm)resultData[currentPosition++];
        FingerPrintType = (SshFpFingerPrintType)resultData[currentPosition++];
        FingerPrint = DnsMessageBase.ParseByteData(resultData, ref currentPosition, length - 2);
    }

    internal override void ParseRecordData(DomainName origin, string[] stringRepresentation)
    {
        if (stringRepresentation.Length < 3)
            throw new FormatException();

        Algorithm = (SshFpAlgorithm)Byte.Parse(stringRepresentation[0]);
        FingerPrintType = (SshFpFingerPrintType)Byte.Parse(stringRepresentation[1]);
        FingerPrint = String.Join("", stringRepresentation.Skip(2)).FromBase16String();
    }

    internal override string RecordDataToString()
    {
        return (byte)Algorithm
               + " " + (byte)FingerPrintType
               + " " + FingerPrint.ToBase16String();
    }

    protected internal override int MaximumRecordDataLength => 2 + FingerPrint.Length;

    protected internal override void EncodeRecordData(Span<byte> messageData, int offset, ref int currentPosition, Dictionary<DomainName, ushort> domainNames, bool useCanonical)
    {
        messageData[currentPosition++] = (byte)Algorithm;
        messageData[currentPosition++] = (byte)FingerPrintType;
        DnsMessageBase.EncodeByteArray(messageData, ref currentPosition, FingerPrint);
    }
}

/// <summary>
///   Base record class for storing text labels (TxtRecord and SpfRecord)
/// </summary>
public abstract class TextRecordBase : DnsRecordBase, ITextRecord
{
    protected TextRecordBase() { }

    protected TextRecordBase(DomainName name, RecordType recordType, int timeToLive, string textData)
        : this(name, recordType, timeToLive, new List<string> { textData ?? String.Empty }) { }

    protected TextRecordBase(DomainName name, RecordType recordType, int timeToLive, IEnumerable<string> textParts)
        : base(name, recordType, RecordClass.INet, timeToLive)
    {
        TextParts = new List<string>(textParts);
    }

    /// <summary>
    ///   Text data
    /// </summary>
    public string TextData => String.Join(String.Empty, TextParts);

    /// <summary>
    ///   The single parts of the text data
    /// </summary>
    public IEnumerable<string> TextParts { get; protected set; }

    protected internal override int MaximumRecordDataLength
    {
        get { return TextParts.Sum(p => p.Length + (p.Length / 255) + (p.Length % 255 == 0 ? 0 : 1)); }
    }

    internal override void ParseRecordData(ReadOnlySpan<byte> resultData, int startPosition, int length)
    {
        int endPosition = startPosition + length;

        List<string> textParts = new List<string>();
        while (startPosition < endPosition)
        {
            textParts.Add(DnsMessageBase.ParseText(resultData, ref startPosition));
        }

        TextParts = textParts;
    }

    internal override void ParseRecordData(DomainName origin, string[] stringRepresentation)
    {
        if (stringRepresentation.Length == 0)
            throw new FormatException();

        TextParts = stringRepresentation;
    }

    internal override string RecordDataToString()
    {
        return String.Join(" ", TextParts.Select(x => "\"" + x.ToMasterfileLabelRepresentation() + "\""));
    }

    protected internal override void EncodeRecordData(Span<byte> messageData, int offset, ref int currentPosition, Dictionary<DomainName, ushort> domainNames, bool useCanonical)
    {
        foreach (var part in TextParts)
        {
            DnsMessageBase.EncodeTextBlock(messageData, ref currentPosition, part);
        }
    }
}

/// <summary>
///   <para>Transaction key</para>
///   <para>
///     Defined in
///     <see cref="!:http://tools.ietf.org/html/rfc2930">RFC 2930</see>
///   </para>
/// </summary>
// ReSharper disable once InconsistentNaming
public class TKeyRecord : DnsRecordBase
{
    /// <summary>
    ///   Mode of transaction
    /// </summary>
    // ReSharper disable once InconsistentNaming
    public enum TKeyMode : ushort
    {
        /// <summary>
        ///   <para>Server assignment</para>
        ///   <para>
        ///     Defined in
        ///     <see cref="!:http://tools.ietf.org/html/rfc2930">RFC 2930</see>
        ///   </para>
        /// </summary>
        ServerAssignment = 1, // RFC2930

        /// <summary>
        ///   <para>Diffie-Hellman exchange</para>
        ///   <para>
        ///     Defined in
        ///     <see cref="!:http://tools.ietf.org/html/rfc2930">RFC 2930</see>
        ///   </para>
        /// </summary>
        DiffieHellmanExchange = 2, // RFC2930

        /// <summary>
        ///   <para>GSS-API negotiation</para>
        ///   <para>
        ///     Defined in
        ///     <see cref="!:http://tools.ietf.org/html/rfc2930">RFC 2930</see>
        ///   </para>
        /// </summary>
        GssNegotiation = 3, // RFC2930

        /// <summary>
        ///   <para>Resolver assignment</para>
        ///   <para>
        ///     Defined in
        ///     <see cref="!:http://tools.ietf.org/html/rfc2930">RFC 2930</see>
        ///   </para>
        /// </summary>
        ResolverAssignment = 4, // RFC2930

        /// <summary>
        ///   <para>Key deletion</para>
        ///   <para>
        ///     Defined in
        ///     <see cref="!:http://tools.ietf.org/html/rfc2930">RFC 2930</see>
        ///   </para>
        /// </summary>
        KeyDeletion = 5, // RFC2930
    }

    /// <summary>
    ///   Algorithm of the key
    /// </summary>
    public TSigAlgorithm Algorithm { get; private set; }

    /// <summary>
    ///   Date from which the key is valid
    /// </summary>
    public DateTime Inception { get; private set; }

    /// <summary>
    ///   Date to which the key is valid
    /// </summary>
    public DateTime Expiration { get; private set; }

    /// <summary>
    ///   Mode of transaction
    /// </summary>
    public TKeyMode Mode { get; private set; }

    /// <summary>
    ///   Error field
    /// </summary>
    public ReturnCode Error { get; private set; }

    /// <summary>
    ///   Binary data of the key
    /// </summary>
    public byte[] Key { get; private set; }

    /// <summary>
    ///   Binary other data
    /// </summary>
    public byte[] OtherData { get; private set; }

    internal TKeyRecord() { }

    /// <summary>
    ///   Creates a new instance of the TKeyRecord class
    /// </summary>
    /// <param name="name"> Name of the record </param>
    /// <param name="algorithm"> Algorithm of the key </param>
    /// <param name="inception"> Date from which the key is valid </param>
    /// <param name="expiration"> Date to which the key is valid </param>
    /// <param name="mode"> Mode of transaction </param>
    /// <param name="error"> Error field </param>
    /// <param name="key"> Binary data of the key </param>
    /// <param name="otherData"> Binary other data </param>
    public TKeyRecord(DomainName name, TSigAlgorithm algorithm, DateTime inception, DateTime expiration, TKeyMode mode, ReturnCode error, byte[] key, byte[] otherData)
        : base(name, RecordType.TKey, RecordClass.Any, 0)
    {
        Algorithm = algorithm;
        Inception = inception;
        Expiration = expiration;
        Mode = mode;
        Error = error;
        Key = key ?? new byte[] { };
        OtherData = otherData ?? new byte[] { };
    }

    internal override void ParseRecordData(ReadOnlySpan<byte> resultData, int startPosition, int length)
    {
        Algorithm = TSigAlgorithmHelper.GetAlgorithmByName(DnsMessageBase.ParseDomainName(resultData, ref startPosition));
        Inception = ParseDateTime(resultData, ref startPosition);
        Expiration = ParseDateTime(resultData, ref startPosition);
        Mode = (TKeyMode)DnsMessageBase.ParseUShort(resultData, ref startPosition);
        Error = (ReturnCode)DnsMessageBase.ParseUShort(resultData, ref startPosition);
        int keyLength = DnsMessageBase.ParseUShort(resultData, ref startPosition);
        Key = DnsMessageBase.ParseByteData(resultData, ref startPosition, keyLength);
        int otherDataLength = DnsMessageBase.ParseUShort(resultData, ref startPosition);
        OtherData = DnsMessageBase.ParseByteData(resultData, ref startPosition, otherDataLength);
    }

    internal override void ParseRecordData(DomainName origin, string[] stringRepresentation)
    {
        throw new NotSupportedException();
    }

    internal override string RecordDataToString()
    {
        return TSigAlgorithmHelper.GetDomainName(Algorithm)
               + " " + (int)(Inception - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds
               + " " + (int)(Expiration - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds
               + " " + (ushort)Mode
               + " " + (ushort)Error
               + " " + Key.ToBase64String()
               + " " + OtherData.ToBase64String();
    }

    protected internal override int MaximumRecordDataLength => 18 + TSigAlgorithmHelper.GetDomainName(Algorithm).MaximumRecordDataLength + Key.Length + OtherData.Length;

    protected internal override void EncodeRecordData(Span<byte> messageData, int offset, ref int currentPosition, Dictionary<DomainName, ushort> domainNames, bool useCanonical)
    {
        DnsMessageBase.EncodeDomainName(messageData, offset, ref currentPosition, TSigAlgorithmHelper.GetDomainName(Algorithm), null, false);
        EncodeDateTime(messageData, ref currentPosition, Inception);
        EncodeDateTime(messageData, ref currentPosition, Expiration);
        DnsMessageBase.EncodeUShort(messageData, ref currentPosition, (ushort)Mode);
        DnsMessageBase.EncodeUShort(messageData, ref currentPosition, (ushort)Error);
        DnsMessageBase.EncodeUShort(messageData, ref currentPosition, (ushort)Key.Length);
        DnsMessageBase.EncodeByteArray(messageData, ref currentPosition, Key);
        DnsMessageBase.EncodeUShort(messageData, ref currentPosition, (ushort)OtherData.Length);
        DnsMessageBase.EncodeByteArray(messageData, ref currentPosition, OtherData);
    }

    internal static void EncodeDateTime(Span<byte> buffer, ref int currentPosition, DateTime value)
    {
        int timeStamp = (int)(value.ToUniversalTime() - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
        DnsMessageBase.EncodeInt(buffer, ref currentPosition, timeStamp);
    }

    private static DateTime ParseDateTime(ReadOnlySpan<byte> buffer, ref int currentPosition)
    {
        int timeStamp = DnsMessageBase.ParseInt(buffer, ref currentPosition);
        return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(timeStamp).ToLocalTime();
    }
}

/// <summary>
///   <para>TLSA</para>
///   <para>
///     Defined in
///     <see cref="!:http://tools.ietf.org/html/rfc6698">RFC 6698</see>
///   </para>
/// </summary>
public class TlsaRecord : DnsRecordBase
{
    /// <summary>
    ///   Certificate Usage
    /// </summary>
    public enum TlsaCertificateUsage : byte
    {
        /// <summary>
        ///   <para>CA constraint</para>
        ///   <para>
        ///     Defined in
        ///     <see cref="!:http://tools.ietf.org/html/rfc6698">RFC 6698</see>
        ///   </para>
        /// </summary>
        // ReSharper disable once InconsistentNaming
        PkixTA = 0,

        /// <summary>
        ///   <para>Service certificate constraint</para>
        ///   <para>
        ///     Defined in
        ///     <see cref="!:http://tools.ietf.org/html/rfc6698">RFC 6698</see>
        ///   </para>
        /// </summary>
        // ReSharper disable once InconsistentNaming
        PkixEE = 1,

        /// <summary>
        ///   <para> Trust anchor assertion</para>
        ///   <para>
        ///     Defined in
        ///     <see cref="!:http://tools.ietf.org/html/rfc6698">RFC 6698</see>
        ///   </para>
        /// </summary>
        // ReSharper disable once InconsistentNaming
        DaneTA = 2,

        /// <summary>
        ///   <para>
        ///     Domain-issued certificate
        ///   </para>
        ///   <para>
        ///     Defined in
        ///     <see cref="!:http://tools.ietf.org/html/rfc6698">RFC 6698</see>
        ///   </para>
        /// </summary>
        // ReSharper disable once InconsistentNaming
        DaneEE = 3,

        /// <summary>
        ///   <para>
        ///     Reserved for Private Use
        ///   </para>
        ///   <para>
        ///     Defined in
        ///     <see cref="!:http://tools.ietf.org/html/rfc6698">RFC 6698</see>
        ///   </para>
        /// </summary>
        PrivCert = 255,
    }

    /// <summary>
    ///   Selector
    /// </summary>
    public enum TlsaSelector : byte
    {
        /// <summary>
        ///   <para>Full certificate</para>
        ///   <para>
        ///     Defined in
        ///     <see cref="!:http://tools.ietf.org/html/rfc6698">RFC 6698</see>
        ///   </para>
        /// </summary>
        FullCertificate = 0,

        /// <summary>
        ///   <para>DER-encoded binary structure</para>
        ///   <para>
        ///     Defined in
        ///     <see cref="!:http://tools.ietf.org/html/rfc6698">RFC 6698</see>
        ///   </para>
        /// </summary>
        SubjectPublicKeyInfo = 1,
    }

    /// <summary>
    ///   Matching Type
    /// </summary>
    public enum TlsaMatchingType : byte
    {
        /// <summary>
        ///   <para>No hash used</para>
        ///   <para>
        ///     Defined in
        ///     <see cref="!:http://tools.ietf.org/html/rfc6698">RFC 6698</see>
        ///   </para>
        /// </summary>
        Full = 0,

        /// <summary>
        ///   <para>SHA-256 hash</para>
        ///   <para>
        ///     Defined in
        ///     <see cref="!:http://tools.ietf.org/html/rfc6698">RFC 6698</see>
        ///   </para>
        /// </summary>
        Sha256Hash = 1,

        /// <summary>
        ///   <para>SHA-512 hash</para>
        ///   <para>
        ///     Defined in
        ///     <see cref="!:http://tools.ietf.org/html/rfc6698">RFC 6698</see>
        ///   </para>
        /// </summary>
        Sha512Hash = 2,

        /// <summary>
        ///   <para>Reserved for Private Use</para>
        ///   <para>
        ///     Defined in
        ///     <see cref="!:http://tools.ietf.org/html/rfc6698">RFC 6698</see>
        ///   </para>
        /// </summary>
        PrivMatch = 255,
    }

    /// <summary>
    ///   The certificate usage
    /// </summary>
    public TlsaCertificateUsage CertificateUsage { get; private set; }

    /// <summary>
    ///   The selector
    /// </summary>
    public TlsaSelector Selector { get; private set; }

    /// <summary>
    ///   The matching type
    /// </summary>
    public TlsaMatchingType MatchingType { get; private set; }

    /// <summary>
    ///   The certificate association data
    /// </summary>
    public byte[] CertificateAssociationData { get; private set; }

    internal TlsaRecord() { }

    /// <summary>
    ///   Creates a new instance of the TlsaRecord class
    /// </summary>
    /// <param name="name"> Domain name of the host </param>
    /// <param name="timeToLive"> Seconds the record should be cached at most </param>
    /// <param name="certificateUsage">The certificate usage</param>
    /// <param name="selector">The selector</param>
    /// <param name="matchingType">The matching type</param>
    /// <param name="certificateAssociationData">The certificate association data</param>
    public TlsaRecord(DomainName name, int timeToLive, TlsaCertificateUsage certificateUsage, TlsaSelector selector, TlsaMatchingType matchingType, byte[] certificateAssociationData)
        : base(name, RecordType.Tlsa, RecordClass.INet, timeToLive)
    {
        CertificateUsage = certificateUsage;
        Selector = selector;
        MatchingType = matchingType;
        CertificateAssociationData = certificateAssociationData ?? new byte[] { };
    }

    /// <summary>
    ///   Creates a new instance of the TlsaRecord class
    /// </summary>
    /// <param name="name"> Domain name of the host </param>
    /// <param name="timeToLive"> Seconds the record should be cached at most </param>
    /// <param name="certificateUsage">The certificate usage</param>
    /// <param name="selector">The selector</param>
    /// <param name="matchingType">The matching type</param>
    /// <param name="certificate">The certificate to get the association data from</param>
    public TlsaRecord(DomainName name, int timeToLive, TlsaCertificateUsage certificateUsage, TlsaSelector selector, TlsaMatchingType matchingType, X509Certificate certificate)
        : base(name, RecordType.Tlsa, RecordClass.INet, timeToLive)
    {
        CertificateUsage = certificateUsage;
        Selector = selector;
        MatchingType = matchingType;
        CertificateAssociationData = GetCertificateAssocicationData(selector, matchingType, certificate);
    }

    // from bc-csharp-master\crypto\src\security\DotNetUtilities.cs
    public static Org.BouncyCastle.X509.X509Certificate DotNetUtilities_FromX509Certificate(
        X509Certificate x509Cert)
    {
        return new X509CertificateParser().ReadCertificate(x509Cert.GetRawCertData());
    }

    internal static byte[] GetCertificateAssocicationData(TlsaSelector selector, TlsaMatchingType matchingType, X509Certificate certificate)
    {
        byte[] selectedBytes;
        switch (selector)
        {
            case TlsaSelector.FullCertificate:
                selectedBytes = certificate.GetRawCertData();
                break;

            case TlsaSelector.SubjectPublicKeyInfo:
                selectedBytes = SubjectPublicKeyInfoFactory.CreateSubjectPublicKeyInfo(DotNetUtilities_FromX509Certificate(certificate).GetPublicKey()).GetDerEncoded();
                break;

            default:
                throw new NotSupportedException();
        }

        byte[] matchingBytes;
        switch (matchingType)
        {
            case TlsaMatchingType.Full:
                matchingBytes = selectedBytes;
                break;

            case TlsaMatchingType.Sha256Hash:
                Sha256Digest sha256Digest = new Sha256Digest();
                sha256Digest.BlockUpdate(selectedBytes, 0, selectedBytes.Length);
                matchingBytes = new byte[sha256Digest.GetDigestSize()];
                sha256Digest.DoFinal(matchingBytes, 0);
                break;

            case TlsaMatchingType.Sha512Hash:
                Sha512Digest sha512Digest = new Sha512Digest();
                sha512Digest.BlockUpdate(selectedBytes, 0, selectedBytes.Length);
                matchingBytes = new byte[sha512Digest.GetDigestSize()];
                sha512Digest.DoFinal(matchingBytes, 0);
                break;

            default:
                throw new NotSupportedException();
        }

        return matchingBytes;
    }

    internal override void ParseRecordData(ReadOnlySpan<byte> resultData, int startPosition, int length)
    {
        CertificateUsage = (TlsaCertificateUsage)resultData[startPosition++];
        Selector = (TlsaSelector)resultData[startPosition++];
        MatchingType = (TlsaMatchingType)resultData[startPosition++];
        CertificateAssociationData = DnsMessageBase.ParseByteData(resultData, ref startPosition, length - 3);
    }

    internal override void ParseRecordData(DomainName origin, string[] stringRepresentation)
    {
        if (stringRepresentation.Length < 4)
            throw new FormatException();

        CertificateUsage = (TlsaCertificateUsage)Byte.Parse(stringRepresentation[0]);
        Selector = (TlsaSelector)Byte.Parse(stringRepresentation[1]);
        MatchingType = (TlsaMatchingType)Byte.Parse(stringRepresentation[2]);
        CertificateAssociationData = String.Join(String.Empty, stringRepresentation.Skip(3)).FromBase16String();
    }

    internal override string RecordDataToString()
    {
        return (byte)CertificateUsage
               + " " + (byte)Selector
               + " " + (byte)MatchingType
               + " " + String.Join(String.Empty, CertificateAssociationData.ToBase16String());
    }

    protected internal override int MaximumRecordDataLength => 3 + CertificateAssociationData.Length;

    protected internal override void EncodeRecordData(Span<byte> messageData, int offset, ref int currentPosition, Dictionary<DomainName, ushort> domainNames, bool useCanonical)
    {
        messageData[currentPosition++] = (byte)CertificateUsage;
        messageData[currentPosition++] = (byte)Selector;
        messageData[currentPosition++] = (byte)MatchingType;
        DnsMessageBase.EncodeByteArray(messageData, ref currentPosition, CertificateAssociationData);
    }
}

/// <summary>
///   <para>Text strings</para>
///   <para>
///     Defined in
///     <see cref="!:http://tools.ietf.org/html/rfc1035">RFC 1035</see>
///   </para>
/// </summary>
public class TxtRecord : TextRecordBase
{
    internal TxtRecord() { }

    /// <summary>
    ///   Creates a new instance of the TxtRecord class
    /// </summary>
    /// <param name="name"> Name of the record </param>
    /// <param name="timeToLive"> Seconds the record should be cached at most </param>
    /// <param name="textData"> Text data </param>
    public TxtRecord(DomainName name, int timeToLive, string textData)
        : base(name, RecordType.Txt, timeToLive, textData) { }

    /// <summary>
    ///   Creates a new instance of the TxtRecord class
    /// </summary>
    /// <param name="name"> Name of the record </param>
    /// <param name="timeToLive"> Seconds the record should be cached at most </param>
    /// <param name="textParts"> All parts of the text data </param>
    public TxtRecord(DomainName name, int timeToLive, IEnumerable<string> textParts)
        : base(name, RecordType.Txt, timeToLive, textParts) { }
}

/// <summary>
///   Represent a dns record, which is not directly supported by this library
/// </summary>
public class UnknownRecord : DnsRecordBase
{
    /// <summary>
    ///   Binary data of the RDATA section of the record
    /// </summary>
    public byte[] RecordData { get; private set; }

    internal UnknownRecord() { }

    /// <summary>
    ///   Creates a new instance of the UnknownRecord class
    /// </summary>
    /// <param name="name"> Domain name of the record </param>
    /// <param name="recordType"> Record type </param>
    /// <param name="recordClass"> Record class </param>
    /// <param name="timeToLive"> Seconds the record should be cached at most </param>
    /// <param name="recordData"> Binary data of the RDATA section of the record </param>
    public UnknownRecord(DomainName name, RecordType recordType, RecordClass recordClass, int timeToLive, byte[] recordData)
        : base(name, recordType, recordClass, timeToLive)
    {
        RecordData = recordData ?? new byte[] { };
    }

    internal override void ParseRecordData(ReadOnlySpan<byte> resultData, int startPosition, int length)
    {
        RecordData = DnsMessageBase.ParseByteData(resultData, ref startPosition, length);
    }

    internal override void ParseRecordData(DomainName origin, string[] stringRepresentation)
    {
        ParseUnknownRecordData(stringRepresentation);
    }

    internal override string RecordDataToString()
    {
        return @"\# " + ((RecordData == null) ? "0" : RecordData.Length + " " + RecordData.ToBase16String());
    }

    protected internal override int MaximumRecordDataLength => RecordData.Length;

    protected internal override void EncodeRecordData(Span<byte> messageData, int offset, ref int currentPosition, Dictionary<DomainName, ushort> domainNames, bool useCanonical)
    {
        DnsMessageBase.EncodeByteArray(messageData, ref currentPosition, RecordData);
    }
}

/// <summary>
///   <para>Uniform Resource Identifier</para>
///   <para>
///     Defined in
///     <see cref="!:http://tools.ietf.org/html/rfc7553">RFC 7553</see>
///   </para>
/// </summary>
public class UriRecord : DnsRecordBase
{
    /// <summary>
    ///   Priority
    /// </summary>
    public ushort Priority { get; private set; }

    /// <summary>
    ///   Weight
    /// </summary>
    public ushort Weight { get; private set; }

    /// <summary>
    ///   Target
    /// </summary>
    public string Target { get; private set; }

    internal UriRecord() { }

    /// <summary>
    ///   Creates a new instance of the MxRecord class
    /// </summary>
    /// <param name="name"> Name of the zone </param>
    /// <param name="timeToLive"> Seconds the record should be cached at most </param>
    /// <param name="priority"> Priority of the record </param>
    /// <param name="weight"> Weight of the record </param>
    /// <param name="target"> Target of the record </param>
    public UriRecord(DomainName name, int timeToLive, ushort priority, ushort weight, string target)
        : base(name, RecordType.Uri, RecordClass.INet, timeToLive)
    {
        Priority = priority;
        Weight = weight;
        Target = target ?? String.Empty;
    }

    internal override void ParseRecordData(ReadOnlySpan<byte> resultData, int startPosition, int length)
    {
        Priority = DnsMessageBase.ParseUShort(resultData, ref startPosition);
        Weight = DnsMessageBase.ParseUShort(resultData, ref startPosition);
        Target = DnsMessageBase.ParseText(resultData, ref startPosition, length - 4);
    }

    internal override void ParseRecordData(DomainName origin, string[] stringRepresentation)
    {
        if (stringRepresentation.Length != 3)
            throw new FormatException();

        Priority = UInt16.Parse(stringRepresentation[0]);
        Weight = UInt16.Parse(stringRepresentation[1]);
        Target = stringRepresentation[2];
    }

    internal override string RecordDataToString()
    {
        return Priority + " " + Weight
               + " \"" + Target + "\"";
    }

    protected internal override int MaximumRecordDataLength => Target.Length + 4;

    protected internal override void EncodeRecordData(Span<byte> messageData, int offset, ref int currentPosition, Dictionary<DomainName, ushort> domainNames, bool useCanonical)
    {
        DnsMessageBase.EncodeUShort(messageData, ref currentPosition, Priority);
        DnsMessageBase.EncodeUShort(messageData, ref currentPosition, Weight);
        DnsMessageBase.EncodeTextWithoutLength(messageData, ref currentPosition, Target);
    }
}

/// <summary>
///   <para>Well known services record</para>
///   <para>
///     Defined in
///     <see cref="!:http://tools.ietf.org/html/rfc1035">RFC 1035</see>
///   </para>
/// </summary>
public class WksRecord : DnsRecordBase
{
    /// <summary>
    ///   IP address of the host
    /// </summary>
    public IPAddress Address { get; private set; }

    /// <summary>
    ///   Type of the protocol
    /// </summary>
    public ProtocolType Protocol { get; private set; }

    /// <summary>
    ///   List of ports which are supported by the host
    /// </summary>
    public List<ushort> Ports { get; private set; }

    internal WksRecord() { }

    /// <summary>
    ///   Creates a new instance of the WksRecord class
    /// </summary>
    /// <param name="name"> Name of the host </param>
    /// <param name="timeToLive"> Seconds the record should be cached at most </param>
    /// <param name="address"> IP address of the host </param>
    /// <param name="protocol"> Type of the protocol </param>
    /// <param name="ports"> List of ports which are supported by the host </param>
    public WksRecord(DomainName name, int timeToLive, IPAddress address, ProtocolType protocol, List<ushort> ports)
        : base(name, RecordType.Wks, RecordClass.INet, timeToLive)
    {
        Address = address ?? IPAddress.None;
        Protocol = protocol;
        Ports = ports ?? new List<ushort>();
    }

    internal override void ParseRecordData(ReadOnlySpan<byte> resultData, int currentPosition, int length)
    {
        int endPosition = currentPosition + length;

        Address = new IPAddress(DnsMessageBase.ParseByteData(resultData, ref currentPosition, 4));
        Protocol = (ProtocolType)resultData[currentPosition++];
        Ports = new List<ushort>();

        int octetNumber = 0;
        while (currentPosition < endPosition)
        {
            byte octet = resultData[currentPosition++];

            for (int bit = 0; bit < 8; bit++)
            {
                if ((octet & (1 << Math.Abs(bit - 7))) != 0)
                {
                    Ports.Add((ushort)(octetNumber * 8 + bit));
                }
            }

            octetNumber++;
        }
    }

    internal override void ParseRecordData(DomainName origin, string[] stringRepresentation)
    {
        if (stringRepresentation.Length < 2)
            throw new FormatException();

        Address = IPAddress.Parse(stringRepresentation[0]);
        Ports = stringRepresentation.Skip(1).Select(UInt16.Parse).ToList();
    }

    internal override string RecordDataToString()
    {
        return Address
               + " " + (byte)Protocol
               + " " + String.Join(" ", Ports.Select(port => port.ToString()));
    }

    protected internal override int MaximumRecordDataLength => 5 + Ports.Max() / 8 + 1;

    protected internal override void EncodeRecordData(Span<byte> messageData, int offset, ref int currentPosition, Dictionary<DomainName, ushort> domainNames, bool useCanonical)
    {
        DnsMessageBase.EncodeByteArray(messageData, ref currentPosition, Address.GetAddressBytes());
        messageData[currentPosition++] = (byte)Protocol;

        foreach (ushort port in Ports)
        {
            int octetPosition = port / 8 + currentPosition;
            int bitPos = port % 8;
            byte octet = messageData[octetPosition];
            octet |= (byte)(1 << Math.Abs(bitPos - 7));
            messageData[octetPosition] = octet;
        }
        currentPosition += Ports.Max() / 8 + 1;
    }
}

/// <summary>
///   <para>X.25 PSDN address record</para>
///   <para>
///     Defined in
///     <see cref="!:http://tools.ietf.org/html/rfc1183">RFC 1183</see>
///   </para>
/// </summary>
public class X25Record : DnsRecordBase
{
    /// <summary>
    ///   PSDN (Public Switched Data Network) address
    /// </summary>
    public string X25Address { get; protected set; }

    internal X25Record() { }

    /// <summary>
    ///   Creates a new instance of the X25Record class
    /// </summary>
    /// <param name="name"> Name of the record </param>
    /// <param name="timeToLive"> Seconds the record should be cached at most </param>
    /// <param name="x25Address"> PSDN (Public Switched Data Network) address </param>
    public X25Record(DomainName name, int timeToLive, string x25Address)
        : base(name, RecordType.X25, RecordClass.INet, timeToLive)
    {
        X25Address = x25Address ?? String.Empty;
    }

    internal override void ParseRecordData(ReadOnlySpan<byte> resultData, int startPosition, int length)
    {
        X25Address += DnsMessageBase.ParseText(resultData, ref startPosition);
    }

    internal override void ParseRecordData(DomainName origin, string[] stringRepresentation)
    {
        if (stringRepresentation.Length != 1)
            throw new FormatException();

        X25Address = stringRepresentation[0];
    }

    internal override string RecordDataToString()
    {
        return X25Address.ToMasterfileLabelRepresentation();
    }

    protected internal override int MaximumRecordDataLength => 1 + X25Address.Length;

    protected internal override void EncodeRecordData(Span<byte> messageData, int offset, ref int currentPosition, Dictionary<DomainName, ushort> domainNames, bool useCanonical)
    {
        DnsMessageBase.EncodeTextBlock(messageData, ref currentPosition, X25Address);
    }
}


#endif
