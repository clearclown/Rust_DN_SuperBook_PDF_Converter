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
using System.Security.Cryptography;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

namespace IPA.Cores.Basic.DnsLib;

/// <summary>
///   Type of algorithm
/// </summary>
// ReSharper disable once InconsistentNaming
public enum TSigAlgorithm
{
    /// <summary>
    ///   Unknown
    /// </summary>
    Unknown,

    /// <summary>
    ///   <para>MD5</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc2845">RFC 2845</see>
    ///   </para>
    /// </summary>
    Md5,

    /// <summary>
    ///   <para>SHA-1</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc4635">RFC 4635</see>
    ///   </para>
    /// </summary>
    Sha1, // RFC4635

    /// <summary>
    ///   <para>SHA-256</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc4635">RFC 4635</see>
    ///   </para>
    /// </summary>
    Sha256,

    /// <summary>
    ///   <para>SHA-384</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc4635">RFC 4635</see>
    ///   </para>
    /// </summary>
    Sha384,

    /// <summary>
    ///   <para>SHA-512</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc4635">RFC 4635</see>
    ///   </para>
    /// </summary>
    Sha512,
}

// ReSharper disable once InconsistentNaming
internal class TSigAlgorithmHelper
{
    public static DomainName GetDomainName(TSigAlgorithm algorithm)
    {
        switch (algorithm)
        {
            case TSigAlgorithm.Md5:
                return DomainName.Parse("hmac-md5.sig-alg.reg.int");
            case TSigAlgorithm.Sha1:
                return DomainName.Parse("hmac-sha1");
            case TSigAlgorithm.Sha256:
                return DomainName.Parse("hmac-sha256");
            case TSigAlgorithm.Sha384:
                return DomainName.Parse("hmac-sha384");
            case TSigAlgorithm.Sha512:
                return DomainName.Parse("hmac-sha512");

            default:
                return null;
        }
    }

    public static TSigAlgorithm GetAlgorithmByName(DomainName name)
    {
        switch (name.ToString().ToLowerInvariant())
        {
            case "hmac-md5.sig-alg.reg.int":
                return TSigAlgorithm.Md5;
            case "hmac-sha1":
                return TSigAlgorithm.Sha1;
            case "hmac-sha256":
                return TSigAlgorithm.Sha256;
            case "hmac-sha384":
                return TSigAlgorithm.Sha384;
            case "hmac-sha512":
                return TSigAlgorithm.Sha512;

            default:
                return TSigAlgorithm.Unknown;
        }
    }

    public static KeyedHashAlgorithm GetHashAlgorithm(TSigAlgorithm algorithm)
    {
        switch (algorithm)
        {
            case TSigAlgorithm.Md5:
                return new HMACMD5();
            case TSigAlgorithm.Sha1:
                return new HMACSHA1();
            case TSigAlgorithm.Sha256:
                return new HMACSHA256();
            case TSigAlgorithm.Sha384:
                return new HMACSHA384();
            case TSigAlgorithm.Sha512:
                return new HMACSHA512();

            default:
                return null;
        }
    }

    internal static int GetHashSize(TSigAlgorithm algorithm)
    {
        switch (algorithm)
        {
            case TSigAlgorithm.Md5:
                return 16;
            case TSigAlgorithm.Sha1:
                return 20;
            case TSigAlgorithm.Sha256:
                return 32;
            case TSigAlgorithm.Sha384:
                return 48;
            case TSigAlgorithm.Sha512:
                return 64;

            default:
                return 0;
        }
    }
}

/// <summary>
///   <para>Transaction signature record</para>
///   <para>
///     Defined in
///     <see cref="!:http://tools.ietf.org/html/rfc2845">RFC 2845</see>
///   </para>
/// </summary>
// ReSharper disable once InconsistentNaming
public class TSigRecord : DnsRecordBase
{
    /// <summary>
    ///   Algorithm of the key
    /// </summary>
    public TSigAlgorithm Algorithm { get; private set; }

    /// <summary>
    ///   Time when the data was signed
    /// </summary>
    public DateTime TimeSigned { get; internal set; }

    /// <summary>
    ///   Timespan errors permitted
    /// </summary>
    public TimeSpan Fudge { get; private set; }

    /// <summary>
    ///   MAC defined by algorithm
    /// </summary>
    public byte[] Mac { get; internal set; }

    /// <summary>
    ///   Original ID of message
    /// </summary>
    public ushort OriginalID { get; private set; }

    /// <summary>
    ///   Error field
    /// </summary>
    public ReturnCode Error { get; internal set; }

    /// <summary>
    ///   Binary other data
    /// </summary>
    public byte[] OtherData { get; internal set; }

    /// <summary>
    ///   Binary data of the key
    /// </summary>
    public byte[] KeyData { get; internal set; }

    /// <summary>
    ///   Result of validation of record
    /// </summary>
    public ReturnCode ValidationResult { get; internal set; }

    internal TSigRecord() { }

    /// <summary>
    ///   Creates a new instance of the TSigRecord class
    /// </summary>
    /// <param name="name"> Name of the record </param>
    /// <param name="algorithm"> Algorithm of the key </param>
    /// <param name="timeSigned"> Time when the data was signed </param>
    /// <param name="fudge"> Timespan errors permitted </param>
    /// <param name="originalID"> Original ID of message </param>
    /// <param name="error"> Error field </param>
    /// <param name="otherData"> Binary other data </param>
    /// <param name="keyData"> Binary data of the key </param>
    public TSigRecord(DomainName name, TSigAlgorithm algorithm, DateTime timeSigned, TimeSpan fudge, ushort originalID, ReturnCode error, byte[] otherData, byte[] keyData)
        : base(name, RecordType.TSig, RecordClass.Any, 0)
    {
        Algorithm = algorithm;
        TimeSigned = timeSigned;
        Fudge = fudge;
        Mac = new byte[] { };
        OriginalID = originalID;
        Error = error;
        OtherData = otherData ?? new byte[] { };
        KeyData = keyData;
    }

    internal override void ParseRecordData(ReadOnlySpan<byte> resultData, int startPosition, int length)
    {
        Algorithm = TSigAlgorithmHelper.GetAlgorithmByName(DnsMessageBase.ParseDomainName(resultData, ref startPosition));
        TimeSigned = ParseDateTime(resultData, ref startPosition);
        Fudge = TimeSpan.FromSeconds(DnsMessageBase.ParseUShort(resultData, ref startPosition));
        int macSize = DnsMessageBase.ParseUShort(resultData, ref startPosition);
        Mac = DnsMessageBase.ParseByteData(resultData, ref startPosition, macSize);
        OriginalID = DnsMessageBase.ParseUShort(resultData, ref startPosition);
        Error = (ReturnCode)DnsMessageBase.ParseUShort(resultData, ref startPosition);
        int otherDataSize = DnsMessageBase.ParseUShort(resultData, ref startPosition);
        OtherData = DnsMessageBase.ParseByteData(resultData, ref startPosition, otherDataSize);
    }

    internal override void ParseRecordData(DomainName origin, string[] stringRepresentation)
    {
        throw new NotSupportedException();
    }

    internal override string RecordDataToString()
    {
        return TSigAlgorithmHelper.GetDomainName(Algorithm)
               + " " + (int)(TimeSigned - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds
               + " " + (ushort)Fudge.TotalSeconds
               + " " + Mac.Length
               + " " + Mac.ToBase64String()
               + " " + OriginalID
               + " " + (ushort)Error
               + " " + OtherData.Length
               + " " + OtherData.ToBase64String();
    }

    protected internal override int MaximumRecordDataLength => TSigAlgorithmHelper.GetDomainName(Algorithm).MaximumRecordDataLength + 18 + TSigAlgorithmHelper.GetHashSize(Algorithm) + OtherData.Length;

    internal void Encode(Span<byte> messageData, int offset, ref int currentPosition, Dictionary<DomainName, ushort> domainNames, byte[] mac)
    {
        EncodeRecordHeader(messageData, offset, ref currentPosition, domainNames, false);
        int recordDataOffset = currentPosition + 2;
        EncodeRecordData(messageData, offset, ref recordDataOffset, mac);
        EncodeRecordLength(messageData, offset, ref currentPosition, domainNames, recordDataOffset);
    }

    private void EncodeRecordData(Span<byte> messageData, int offset, ref int currentPosition, byte[] mac)
    {
        DnsMessageBase.EncodeDomainName(messageData, offset, ref currentPosition, TSigAlgorithmHelper.GetDomainName(Algorithm), null, false);
        EncodeDateTime(messageData, ref currentPosition, TimeSigned);
        DnsMessageBase.EncodeUShort(messageData, ref currentPosition, (ushort)Fudge.TotalSeconds);
        DnsMessageBase.EncodeUShort(messageData, ref currentPosition, (ushort)mac.Length);
        DnsMessageBase.EncodeByteArray(messageData, ref currentPosition, mac);
        DnsMessageBase.EncodeUShort(messageData, ref currentPosition, OriginalID);
        DnsMessageBase.EncodeUShort(messageData, ref currentPosition, (ushort)Error);
        DnsMessageBase.EncodeUShort(messageData, ref currentPosition, (ushort)OtherData.Length);
        DnsMessageBase.EncodeByteArray(messageData, ref currentPosition, OtherData);
    }

    protected internal override void EncodeRecordData(Span<byte> messageData, int offset, ref int currentPosition, Dictionary<DomainName, ushort> domainNames, bool useCanonical)
    {
        EncodeRecordData(messageData, offset, ref currentPosition, Mac);
    }

    internal static void EncodeDateTime(Span<byte> buffer, ref int currentPosition, DateTime value)
    {
        long timeStamp = (long)(value.ToUniversalTime() - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;

        if (BitConverter.IsLittleEndian)
        {
            buffer[currentPosition++] = (byte)((timeStamp >> 40) & 0xff);
            buffer[currentPosition++] = (byte)((timeStamp >> 32) & 0xff);
            buffer[currentPosition++] = (byte)(timeStamp >> 24 & 0xff);
            buffer[currentPosition++] = (byte)((timeStamp >> 16) & 0xff);
            buffer[currentPosition++] = (byte)((timeStamp >> 8) & 0xff);
            buffer[currentPosition++] = (byte)(timeStamp & 0xff);
        }
        else
        {
            buffer[currentPosition++] = (byte)(timeStamp & 0xff);
            buffer[currentPosition++] = (byte)((timeStamp >> 8) & 0xff);
            buffer[currentPosition++] = (byte)((timeStamp >> 16) & 0xff);
            buffer[currentPosition++] = (byte)((timeStamp >> 24) & 0xff);
            buffer[currentPosition++] = (byte)((timeStamp >> 32) & 0xff);
            buffer[currentPosition++] = (byte)((timeStamp >> 40) & 0xff);
        }
    }

    private static DateTime ParseDateTime(ReadOnlySpan<byte> buffer, ref int currentPosition)
    {
        long timeStamp;

        if (BitConverter.IsLittleEndian)
        {
            timeStamp = ((buffer[currentPosition++] << 40) | (buffer[currentPosition++] << 32) | buffer[currentPosition++] << 24 | (buffer[currentPosition++] << 16) | (buffer[currentPosition++] << 8) | buffer[currentPosition++]);
        }
        else
        {
            timeStamp = (buffer[currentPosition++] | (buffer[currentPosition++] << 8) | (buffer[currentPosition++] << 16) | (buffer[currentPosition++] << 24) | (buffer[currentPosition++] << 32) | (buffer[currentPosition++] << 40));
        }

        return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(timeStamp).ToLocalTime();
    }
}


#endif
