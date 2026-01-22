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
using System.Globalization;

using Org.BouncyCastle.Asn1.CryptoPro;
using Org.BouncyCastle.Asn1.Nist;
using Org.BouncyCastle.Asn1.Sec;
using Org.BouncyCastle.Asn1.X9;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Prng;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Math.EC;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.Security;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

namespace IPA.Cores.Basic.DnsLib;

/// <summary>
///   <para>Security Key record using Diffie Hellman algorithm</para>
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
public class DiffieHellmanKeyRecord : KeyRecordBase
{
    /// <summary>
    ///   Binary data of the prime of the key
    /// </summary>
    public byte[] Prime { get; private set; }

    /// <summary>
    ///   Binary data of the generator of the key
    /// </summary>
    public byte[] Generator { get; private set; }

    /// <summary>
    ///   Binary data of the public value
    /// </summary>
    public byte[] PublicValue { get; private set; }

    internal DiffieHellmanKeyRecord() { }

    /// <summary>
    ///   Creates a new instance of the DiffieHellmanKeyRecord class
    /// </summary>
    /// <param name="name"> Name of the record </param>
    /// <param name="recordClass"> Class of the record </param>
    /// <param name="timeToLive"> Seconds the record should be cached at most </param>
    /// <param name="flags"> Flags of the key </param>
    /// <param name="protocol"> Protocol for which the key is used </param>
    /// <param name="prime"> Binary data of the prime of the key </param>
    /// <param name="generator"> Binary data of the generator of the key </param>
    /// <param name="publicValue"> Binary data of the public value </param>
    public DiffieHellmanKeyRecord(DomainName name, RecordClass recordClass, int timeToLive, ushort flags, ProtocolType protocol, byte[] prime, byte[] generator, byte[] publicValue)
        : base(name, recordClass, timeToLive, flags, protocol, DnsSecAlgorithm.DiffieHellman)
    {
        Prime = prime ?? new byte[] { };
        Generator = generator ?? new byte[] { };
        PublicValue = publicValue ?? new byte[] { };
    }

    protected override void ParsePublicKey(ReadOnlySpan<byte> resultData, int startPosition, int length)
    {
        int primeLength = DnsMessageBase.ParseUShort(resultData, ref startPosition);
        Prime = DnsMessageBase.ParseByteData(resultData, ref startPosition, primeLength);
        int generatorLength = DnsMessageBase.ParseUShort(resultData, ref startPosition);
        Generator = DnsMessageBase.ParseByteData(resultData, ref startPosition, generatorLength);
        int publicValueLength = DnsMessageBase.ParseUShort(resultData, ref startPosition);
        PublicValue = DnsMessageBase.ParseByteData(resultData, ref startPosition, publicValueLength);
    }

    internal override void ParseRecordData(DomainName origin, string[] stringRepresentation)
    {
        throw new NotSupportedException();
    }

    protected override string PublicKeyToString()
    {
        byte[] publicKey = new byte[MaximumPublicKeyLength];
        int currentPosition = 0;

        EncodePublicKey(publicKey, 0, ref currentPosition, null);

        return publicKey.ToBase64String();
    }

    protected override int MaximumPublicKeyLength => 3 + Prime.Length + Generator.Length + PublicValue.Length;

    protected override void EncodePublicKey(Span<byte> messageData, int offset, ref int currentPosition, Dictionary<DomainName, ushort> domainNames)
    {
        DnsMessageBase.EncodeUShort(messageData, ref currentPosition, (ushort)Prime.Length);
        DnsMessageBase.EncodeByteArray(messageData, ref currentPosition, Prime);
        DnsMessageBase.EncodeUShort(messageData, ref currentPosition, (ushort)Generator.Length);
        DnsMessageBase.EncodeByteArray(messageData, ref currentPosition, Generator);
        DnsMessageBase.EncodeUShort(messageData, ref currentPosition, (ushort)PublicValue.Length);
        DnsMessageBase.EncodeByteArray(messageData, ref currentPosition, PublicValue);
    }
}

/// <summary>
///   <para>DNSSEC lookaside validation</para>
///   <para>
///     Defined in
///     <see cref="!:http://tools.ietf.org/html/rfc4431">RFC 4431</see>
///   </para>
/// </summary>
public class DlvRecord : DnsRecordBase
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

    internal DlvRecord() { }

    /// <summary>
    ///   Creates a new instance of the DlvRecord class
    /// </summary>
    /// <param name="name"> Name of the record </param>
    /// <param name="recordClass"> Class of the record </param>
    /// <param name="timeToLive"> Seconds the record should be cached at most </param>
    /// <param name="keyTag"> Key tag </param>
    /// <param name="algorithm"> Algorithm used </param>
    /// <param name="digestType"> Type of the digest </param>
    /// <param name="digest"> Binary data of the digest </param>
    public DlvRecord(DomainName name, RecordClass recordClass, int timeToLive, ushort keyTag, DnsSecAlgorithm algorithm, DnsSecDigestType digestType, byte[] digest)
        : base(name, RecordType.Dlv, recordClass, timeToLive)
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

[Flags]
public enum DnsKeyFlags : ushort
{
    /// <summary>
    ///   <para>ZONE</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc3755">RFC 3755</see>
    ///     and
    ///     <see cref="!:http://tools.ietf.org/html/rfc4034">RFC 4034</see>
    ///   </para>
    /// </summary>
    Zone = 256,

    /// <summary>
    ///   <para>REVOKE</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc5011">RFC 5011</see>
    ///   </para>
    /// </summary>
    Revoke = 128,

    /// <summary>
    ///   <para>Secure Entry Point (SEP)</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc3755">RFC 3755</see>
    ///     and
    ///     <see cref="!:http://tools.ietf.org/html/rfc4034">RFC 4034</see>
    ///   </para>
    /// </summary>
    SecureEntryPoint = 1
}

/// <summary>
///   <para>DNS Key record</para>
///   <para>
///     Defined in
///     <see cref="!:http://tools.ietf.org/html/rfc4034">RFC 4034</see>
///     and
///     <see cref="!:http://tools.ietf.org/html/rfc3755">RFC 3755</see>
///   </para>
/// </summary>
public class DnsKeyRecord : DnsRecordBase
{
    private static readonly SecureRandom _secureRandom = new SecureRandom();

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
    public byte[] PrivateKey { get; }

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

    internal DnsKeyRecord() { }

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
    public DnsKeyRecord(DomainName name, RecordClass recordClass, int timeToLive, DnsKeyFlags flags, byte protocol, DnsSecAlgorithm algorithm, byte[] publicKey)
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
    public DnsKeyRecord(DomainName name, RecordClass recordClass, int timeToLive, DnsKeyFlags flags, byte protocol, DnsSecAlgorithm algorithm, byte[] publicKey, byte[] privateKey)
        : base(name, RecordType.DnsKey, recordClass, timeToLive)
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

    internal byte[] Sign(byte[] buffer, int length)
    {
        switch (Algorithm)
        {
            case DnsSecAlgorithm.RsaSha1:
            case DnsSecAlgorithm.RsaSha1Nsec3Sha1:
                return SignRsa(new Sha1Digest(), buffer, length);

            case DnsSecAlgorithm.RsaSha256:
                return SignRsa(new Sha256Digest(), buffer, length);

            case DnsSecAlgorithm.RsaSha512:
                return SignRsa(new Sha512Digest(), buffer, length);

            case DnsSecAlgorithm.Dsa:
            case DnsSecAlgorithm.DsaNsec3Sha1:
                return SignDsa(buffer, length);

            case DnsSecAlgorithm.EccGost:
                return SignGost(buffer, length);

            case DnsSecAlgorithm.EcDsaP256Sha256:
                return SignEcDsa(new Sha256Digest(), buffer, length);

            case DnsSecAlgorithm.EcDsaP384Sha384:
                return SignEcDsa(new Sha384Digest(), buffer, length);

            default:
                throw new NotSupportedException();
        }
    }

    private byte[] SignRsa(IDigest digest, byte[] buffer, int length)
    {
        RsaDigestSigner signer = new RsaDigestSigner(digest);

        signer.Init(true, new ParametersWithRandom(PrivateKeyFactory.CreateKey(PrivateKey), _secureRandom));

        signer.BlockUpdate(buffer, 0, length);
        return signer.GenerateSignature();
    }

    private byte[] SignDsa(byte[] buffer, int length)
    {
        var signer = new DsaSigner();
        signer.Init(true, new ParametersWithRandom(PrivateKeyFactory.CreateKey(PrivateKey), _secureRandom));

        var sha1 = new Sha1Digest();

        sha1.BlockUpdate(buffer, 0, length);
        byte[] hash = new byte[sha1.GetDigestSize()];
        sha1.DoFinal(hash, 0);

        var signature = signer.GenerateSignature(hash);

        byte[] res = new byte[41];

        res[0] = PublicKey[0];

        signature[0].ToByteArrayUnsigned().CopyTo(res, 1);
        signature[1].ToByteArrayUnsigned().CopyTo(res, 21);

        return res;
    }

    private byte[] SignGost(byte[] buffer, int length)
    {
        ECGost3410Signer signer = new ECGost3410Signer();
        signer.Init(true, new ParametersWithRandom(PrivateKeyFactory.CreateKey(PrivateKey), _secureRandom));

        var digest = new Gost3411Digest();

        digest.BlockUpdate(buffer, 0, length);
        byte[] hash = new byte[digest.GetDigestSize()];
        digest.DoFinal(hash, 0);

        var signature = signer.GenerateSignature(hash);

        byte[] res = new byte[64];

        signature[0].ToByteArrayUnsigned().CopyTo(res, 32);
        signature[1].ToByteArrayUnsigned().CopyTo(res, 0);

        return res;
    }

    private byte[] SignEcDsa(IDigest digest, byte[] buffer, int length)
    {
        int digestSize = digest.GetDigestSize();

        ECDsaSigner signer = new ECDsaSigner();

        signer.Init(true, new ParametersWithRandom(PrivateKeyFactory.CreateKey(PrivateKey), _secureRandom));

        digest.BlockUpdate(buffer, 0, length);
        byte[] hash = new byte[digest.GetDigestSize()];
        digest.DoFinal(hash, 0);

        var signature = signer.GenerateSignature(hash);

        byte[] res = new byte[digestSize * 2];

        signature[0].ToByteArrayUnsigned().CopyTo(res, 0);
        signature[1].ToByteArrayUnsigned().CopyTo(res, digestSize);

        return res;
    }

    internal bool Verify(byte[] buffer, int length, byte[] signature)
    {
        switch (Algorithm)
        {
            case DnsSecAlgorithm.RsaSha1:
            case DnsSecAlgorithm.RsaSha1Nsec3Sha1:
                return VerifyRsa(new Sha1Digest(), buffer, length, signature);

            case DnsSecAlgorithm.RsaSha256:
                return VerifyRsa(new Sha256Digest(), buffer, length, signature);

            case DnsSecAlgorithm.RsaSha512:
                return VerifyRsa(new Sha512Digest(), buffer, length, signature);

            case DnsSecAlgorithm.Dsa:
            case DnsSecAlgorithm.DsaNsec3Sha1:
                return VerifyDsa(buffer, length, signature);

            case DnsSecAlgorithm.EccGost:
                return VerifyGost(buffer, length, signature);

            case DnsSecAlgorithm.EcDsaP256Sha256:
                return VerifyEcDsa(new Sha256Digest(), NistNamedCurves.GetByOid(SecObjectIdentifiers.SecP256r1), buffer, length, signature);

            case DnsSecAlgorithm.EcDsaP384Sha384:
                return VerifyEcDsa(new Sha384Digest(), NistNamedCurves.GetByOid(SecObjectIdentifiers.SecP384r1), buffer, length, signature);

            default:
                throw new NotSupportedException();
        }
    }

    private bool VerifyRsa(IDigest digest, byte[] buffer, int length, byte[] signature)
    {
        RsaDigestSigner signer = new RsaDigestSigner(digest);

        int exponentOffset = 1;
        int exponentLength = PublicKey[0] == 0 ? DnsMessageBase.ParseUShort(PublicKey, ref exponentOffset) : PublicKey[0];
        int moduloOffset = exponentOffset + exponentLength;
        int moduloLength = PublicKey.Length - moduloOffset;

        RsaKeyParameters parameters = new RsaKeyParameters(false, new BigInteger(1, PublicKey, moduloOffset, moduloLength), new BigInteger(1, PublicKey, exponentOffset, exponentLength));

        signer.Init(false, new ParametersWithRandom(parameters, _secureRandom));

        signer.BlockUpdate(buffer, 0, length);
        return signer.VerifySignature(signature);
    }

    private bool VerifyDsa(byte[] buffer, int length, byte[] signature)
    {
        int numberSize = 64 + PublicKey[0] * 8;

        DsaPublicKeyParameters parameters = new DsaPublicKeyParameters(
            new BigInteger(1, PublicKey, 21 + 2 * numberSize, numberSize),
            new DsaParameters(
                new BigInteger(1, PublicKey, 21, numberSize),
                new BigInteger(1, PublicKey, 1, 20),
                new BigInteger(1, PublicKey, 21 + numberSize, numberSize))
            );

        var dsa = new DsaSigner();
        dsa.Init(false, parameters);

        var sha1 = new Sha1Digest();

        sha1.BlockUpdate(buffer, 0, length);
        byte[] hash = new byte[sha1.GetDigestSize()];
        sha1.DoFinal(hash, 0);

        return dsa.VerifySignature(hash, new BigInteger(1, signature, 1, 20), new BigInteger(1, signature, 21, 20));
    }

    private bool VerifyGost(byte[] buffer, int length, byte[] signature)
    {
        ECDomainParameters dParams = ECGost3410NamedCurves.GetByOid(CryptoProObjectIdentifiers.GostR3410x2001CryptoProA);
        byte[] reversedPublicKey = PublicKey.Reverse().ToArray();
        ECPoint q = dParams.Curve.CreatePoint(new BigInteger(1, reversedPublicKey, 32, 32), new BigInteger(1, reversedPublicKey, 0, 32));
        ECPublicKeyParameters parameters = new ECPublicKeyParameters(q, dParams);

        var signer = new ECGost3410Signer();
        signer.Init(false, parameters);

        var digest = new Gost3411Digest();

        digest.BlockUpdate(buffer, 0, length);
        byte[] hash = new byte[digest.GetDigestSize()];
        digest.DoFinal(hash, 0);

        return signer.VerifySignature(hash, new BigInteger(1, signature, 32, 32), new BigInteger(1, signature, 0, 32));
    }

    private bool VerifyEcDsa(IDigest digest, X9ECParameters curveParameter, byte[] buffer, int length, byte[] signature)
    {
        int digestSize = digest.GetDigestSize();

        ECDomainParameters dParams = new ECDomainParameters(
            curveParameter.Curve,
            curveParameter.G,
            curveParameter.N,
            curveParameter.H,
            curveParameter.GetSeed());

        ECPoint q = dParams.Curve.CreatePoint(new BigInteger(1, PublicKey, 0, digestSize), new BigInteger(1, PublicKey, digestSize, digestSize));

        ECPublicKeyParameters parameters = new ECPublicKeyParameters(q, dParams);

        var signer = new ECDsaSigner();
        signer.Init(false, parameters);

        digest.BlockUpdate(buffer, 0, length);
        byte[] hash = new byte[digest.GetDigestSize()];
        digest.DoFinal(hash, 0);

        return signer.VerifySignature(hash, new BigInteger(1, signature, 0, digestSize), new BigInteger(1, signature, digestSize, digestSize));
    }

    /// <summary>
    ///   Creates a new signing key pair
    /// </summary>
    /// <param name="name">The name of the key or zone</param>
    /// <param name="recordClass">The record class of the DnsKeyRecord</param>
    /// <param name="timeToLive">The TTL in seconds to the DnsKeyRecord</param>
    /// <param name="flags">The Flags of the DnsKeyRecord</param>
    /// <param name="protocol">The protocol version</param>
    /// <param name="algorithm">The key algorithm</param>
    /// <param name="keyStrength">The key strength or 0 for default strength</param>
    /// <returns></returns>
    public static DnsKeyRecord CreateSigningKey(DomainName name, RecordClass recordClass, int timeToLive, DnsKeyFlags flags, byte protocol, DnsSecAlgorithm algorithm, int keyStrength = 0)
    {
        byte[] privateKey;
        byte[] publicKey;

        switch (algorithm)
        {
            case DnsSecAlgorithm.RsaSha1:
            case DnsSecAlgorithm.RsaSha1Nsec3Sha1:
            case DnsSecAlgorithm.RsaSha256:
            case DnsSecAlgorithm.RsaSha512:
                if (keyStrength == 0)
                    keyStrength = (flags == (DnsKeyFlags.Zone | DnsKeyFlags.SecureEntryPoint)) ? 2048 : 1024;

                RsaKeyPairGenerator rsaKeyGen = new RsaKeyPairGenerator();
                rsaKeyGen.Init(new KeyGenerationParameters(_secureRandom, keyStrength));
                var rsaKey = rsaKeyGen.GenerateKeyPair();
                privateKey = PrivateKeyInfoFactory.CreatePrivateKeyInfo(rsaKey.Private).GetDerEncoded();
                var rsaPublicKey = (RsaKeyParameters)rsaKey.Public;
                var rsaExponent = rsaPublicKey.Exponent.ToByteArrayUnsigned();
                var rsaModulus = rsaPublicKey.Modulus.ToByteArrayUnsigned();

                int offset = 1;
                if (rsaExponent.Length > 255)
                {
                    publicKey = new byte[3 + rsaExponent.Length + rsaModulus.Length];
                    DnsMessageBase.EncodeUShort(publicKey, ref offset, (ushort)publicKey.Length);
                }
                else
                {
                    publicKey = new byte[1 + rsaExponent.Length + rsaModulus.Length];
                    publicKey[0] = (byte)rsaExponent.Length;
                }
                DnsMessageBase.EncodeByteArray(publicKey, ref offset, rsaExponent);
                DnsMessageBase.EncodeByteArray(publicKey, ref offset, rsaModulus);
                break;

            case DnsSecAlgorithm.Dsa:
            case DnsSecAlgorithm.DsaNsec3Sha1:
                if (keyStrength == 0)
                    keyStrength = 1024;

                DsaParametersGenerator dsaParamsGen = new DsaParametersGenerator();
                dsaParamsGen.Init(keyStrength, 12, _secureRandom);
                DsaKeyPairGenerator dsaKeyGen = new DsaKeyPairGenerator();
                dsaKeyGen.Init(new DsaKeyGenerationParameters(_secureRandom, dsaParamsGen.GenerateParameters()));
                var dsaKey = dsaKeyGen.GenerateKeyPair();
                privateKey = PrivateKeyInfoFactory.CreatePrivateKeyInfo(dsaKey.Private).GetDerEncoded();
                var dsaPublicKey = (DsaPublicKeyParameters)dsaKey.Public;

                var dsaY = dsaPublicKey.Y.ToByteArrayUnsigned();
                var dsaP = dsaPublicKey.Parameters.P.ToByteArrayUnsigned();
                var dsaQ = dsaPublicKey.Parameters.Q.ToByteArrayUnsigned();
                var dsaG = dsaPublicKey.Parameters.G.ToByteArrayUnsigned();
                var dsaT = (byte)((dsaY.Length - 64) / 8);

                publicKey = new byte[21 + 3 * dsaY.Length];
                publicKey[0] = dsaT;
                dsaQ.CopyTo(publicKey, 1);
                dsaP.CopyTo(publicKey, 21);
                dsaG.CopyTo(publicKey, 21 + dsaY.Length);
                dsaY.CopyTo(publicKey, 21 + 2 * dsaY.Length);
                break;

            case DnsSecAlgorithm.EccGost:
                ECDomainParameters gostEcDomainParameters = ECGost3410NamedCurves.GetByOid(CryptoProObjectIdentifiers.GostR3410x2001CryptoProA);

                var gostKeyGen = new ECKeyPairGenerator();
                gostKeyGen.Init(new ECKeyGenerationParameters(gostEcDomainParameters, _secureRandom));

                var gostKey = gostKeyGen.GenerateKeyPair();
                privateKey = PrivateKeyInfoFactory.CreatePrivateKeyInfo(gostKey.Private).GetDerEncoded();
                var gostPublicKey = (ECPublicKeyParameters)gostKey.Public;

                publicKey = new byte[64];

                gostPublicKey.Q.AffineXCoord.ToBigInteger().ToByteArrayUnsigned().CopyTo(publicKey, 32);
                gostPublicKey.Q.AffineYCoord.ToBigInteger().ToByteArrayUnsigned().CopyTo(publicKey, 0);

                publicKey = publicKey.Reverse().ToArray();
                break;

            case DnsSecAlgorithm.EcDsaP256Sha256:
            case DnsSecAlgorithm.EcDsaP384Sha384:
                int ecDsaDigestSize;
                X9ECParameters ecDsaCurveParameter;

                if (algorithm == DnsSecAlgorithm.EcDsaP256Sha256)
                {
                    ecDsaDigestSize = new Sha256Digest().GetDigestSize();
                    ecDsaCurveParameter = NistNamedCurves.GetByOid(SecObjectIdentifiers.SecP256r1);
                }
                else
                {
                    ecDsaDigestSize = new Sha384Digest().GetDigestSize();
                    ecDsaCurveParameter = NistNamedCurves.GetByOid(SecObjectIdentifiers.SecP384r1);
                }

                ECDomainParameters ecDsaP384EcDomainParameters = new ECDomainParameters(
                    ecDsaCurveParameter.Curve,
                    ecDsaCurveParameter.G,
                    ecDsaCurveParameter.N,
                    ecDsaCurveParameter.H,
                    ecDsaCurveParameter.GetSeed());

                var ecDsaKeyGen = new ECKeyPairGenerator();
                ecDsaKeyGen.Init(new ECKeyGenerationParameters(ecDsaP384EcDomainParameters, _secureRandom));

                var ecDsaKey = ecDsaKeyGen.GenerateKeyPair();
                privateKey = PrivateKeyInfoFactory.CreatePrivateKeyInfo(ecDsaKey.Private).GetDerEncoded();
                var ecDsaPublicKey = (ECPublicKeyParameters)ecDsaKey.Public;

                publicKey = new byte[ecDsaDigestSize * 2];

                ecDsaPublicKey.Q.AffineXCoord.ToBigInteger().ToByteArrayUnsigned().CopyTo(publicKey, 0);
                ecDsaPublicKey.Q.AffineYCoord.ToBigInteger().ToByteArrayUnsigned().CopyTo(publicKey, ecDsaDigestSize);
                break;

            default:
                throw new NotSupportedException();
        }

        return new DnsKeyRecord(name, recordClass, timeToLive, flags, protocol, algorithm, publicKey, privateKey);
    }
}

/// <summary>
///   DNSSEC algorithm type
/// </summary>
public enum DnsSecAlgorithm : byte
{
    /// <summary>
    ///   <para>RSA MD5</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc3110">RFC 3110</see>
    ///     and
    ///     <see cref="!:http://tools.ietf.org/html/rfc4034">RFC 4034</see>
    ///   </para>
    /// </summary>
    RsaMd5 = 1,

    /// <summary>
    ///   <para>Diffie Hellman</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc2539">RFC 2539</see>
    ///   </para>
    /// </summary>
    DiffieHellman = 2,

    /// <summary>
    ///   <para>DSA/SHA-1</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc3755">RFC 3755</see>
    ///     and
    ///     <see cref="!:http://tools.ietf.org/html/rfc2536">RFC 4034</see>
    ///   </para>
    /// </summary>
    Dsa = 3,

    /// <summary>
    ///   <para>RSA/SHA-1</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc3110">RFC 3110</see>
    ///     and
    ///     <see cref="!:http://tools.ietf.org/html/rfc4034">RFC 4034</see>
    ///   </para>
    /// </summary>
    RsaSha1 = 5,

    /// <summary>
    ///   <para>DSA/SHA-1 using NSEC3 hashs</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc5155">RFC 5155</see>
    ///   </para>
    /// </summary>
    DsaNsec3Sha1 = 6,

    /// <summary>
    ///   <para>RSA/SHA-1 using NSEC3 hashs</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc5155">RFC 5155</see>
    ///   </para>
    /// </summary>
    RsaSha1Nsec3Sha1 = 7,

    /// <summary>
    ///   <para>RSA/SHA-256</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc5702">RFC 5702</see>
    ///   </para>
    /// </summary>
    RsaSha256 = 8,

    /// <summary>
    ///   <para>RSA/SHA-512</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc5702">RFC 5702</see>
    ///   </para>
    /// </summary>
    RsaSha512 = 10,

    /// <summary>
    ///   <para>GOST R 34.10-2001</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc5933">RFC 5933</see>
    ///   </para>
    /// </summary>
    EccGost = 12,

    /// <summary>
    ///   <para>ECDSA Curve P-256 with SHA-256</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc6605">RFC 6605</see>
    ///   </para>
    /// </summary>
    EcDsaP256Sha256 = 13,

    /// <summary>
    ///   <para>ECDSA Curve P-384 with SHA-384</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc6605">RFC 6605</see>
    ///   </para>
    /// </summary>
    EcDsaP384Sha384 = 14,

    /// <summary>
    ///   <para>Indirect</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc4034">RFC 4034</see>
    ///   </para>
    /// </summary>
    Indirect = 252,

    /// <summary>
    ///   <para>Private key using named algorithm</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc4034">RFC 4034</see>
    ///   </para>
    /// </summary>
    PrivateDns = 253,

    /// <summary>
    ///   <para>Private key using algorithm object identifier</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc4034">RFC 4034</see>
    ///   </para>
    /// </summary>
    PrivateOid = 254,
}

internal static class DnsSecAlgorithmHelper
{
    public static bool IsSupported(this DnsSecAlgorithm algorithm)
    {
        switch (algorithm)
        {
            case DnsSecAlgorithm.RsaSha1:
            case DnsSecAlgorithm.Dsa:
            case DnsSecAlgorithm.RsaSha1Nsec3Sha1:
            case DnsSecAlgorithm.DsaNsec3Sha1:
            case DnsSecAlgorithm.RsaSha256:
            case DnsSecAlgorithm.RsaSha512:
            case DnsSecAlgorithm.EccGost:
            case DnsSecAlgorithm.EcDsaP256Sha256:
            case DnsSecAlgorithm.EcDsaP384Sha384:
                return true;

            default:
                return false;
        }
    }

    public static int GetPriority(this DnsSecAlgorithm algorithm)
    {
        switch (algorithm)
        {
            case DnsSecAlgorithm.RsaSha1:
                return 100;
            case DnsSecAlgorithm.Dsa:
                return 90;
            case DnsSecAlgorithm.RsaSha1Nsec3Sha1:
                return 100;
            case DnsSecAlgorithm.DsaNsec3Sha1:
                return 90;
            case DnsSecAlgorithm.RsaSha256:
                return 80;
            case DnsSecAlgorithm.RsaSha512:
                return 70;
            case DnsSecAlgorithm.EccGost:
                return 110;
            case DnsSecAlgorithm.EcDsaP256Sha256:
                return 60;
            case DnsSecAlgorithm.EcDsaP384Sha384:
                return 50;

            default:
                throw new NotSupportedException();
        }
    }

    public static bool IsCompatibleWithNSec(this DnsSecAlgorithm algorithm)
    {
        switch (algorithm)
        {
            case DnsSecAlgorithm.RsaSha1:
            case DnsSecAlgorithm.Dsa:
            case DnsSecAlgorithm.RsaSha256:
            case DnsSecAlgorithm.RsaSha512:
            case DnsSecAlgorithm.EccGost:
            case DnsSecAlgorithm.EcDsaP256Sha256:
            case DnsSecAlgorithm.EcDsaP384Sha384:
                return true;

            default:
                return false;
        }
    }

    public static bool IsCompatibleWithNSec3(this DnsSecAlgorithm algorithm)
    {
        switch (algorithm)
        {
            case DnsSecAlgorithm.RsaSha1Nsec3Sha1:
            case DnsSecAlgorithm.DsaNsec3Sha1:
            case DnsSecAlgorithm.RsaSha256:
            case DnsSecAlgorithm.RsaSha512:
            case DnsSecAlgorithm.EccGost:
            case DnsSecAlgorithm.EcDsaP256Sha256:
            case DnsSecAlgorithm.EcDsaP384Sha384:
                return true;

            default:
                return false;
        }
    }
}

/// <summary>
///   Type of DNSSEC digest
/// </summary>
public enum DnsSecDigestType : byte
{
    /// <summary>
    ///   <para>SHA-1</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc3658">RFC 3658</see>
    ///   </para>
    /// </summary>
    Sha1 = 1,

    /// <summary>
    ///   <para>SHA-256</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc4509">RFC 4509</see>
    ///   </para>
    /// </summary>
    Sha256 = 2,

    /// <summary>
    ///   <para>GOST R 34.11-94</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc5933">RFC 5933</see>
    ///   </para>
    /// </summary>
    EccGost = 3,

    /// <summary>
    ///   <para>SHA-384</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc6605">RFC 6605</see>
    ///   </para>
    /// </summary>
    Sha384 = 4,
}

/// <summary>
///   The exception that is thrown when a DNSSEC validation fails
/// </summary>
public class DnsSecValidationException : Exception
{
    internal DnsSecValidationException(string message)
        : base(message) { }
}

internal class DnsSecValidator<TState>
{
    private readonly IInternalDnsSecResolver<TState> _resolver;
    private readonly IResolverHintStore _resolverHintStore;

    public DnsSecValidator(IInternalDnsSecResolver<TState> resolver, IResolverHintStore resolverHintStore)
    {
        _resolver = resolver;
        _resolverHintStore = resolverHintStore;
    }

    public async Task<DnsSecValidationResult> ValidateAsync<TRecord>(DomainName name, RecordType recordType, RecordClass recordClass, DnsMessage msg, List<TRecord> resultRecords, TState state, CancellationToken token)
        where TRecord : DnsRecordBase
    {
        List<RrSigRecord> rrSigRecords = msg
            .AnswerRecords.OfType<RrSigRecord>()
            .Union(msg.AuthorityRecords.OfType<RrSigRecord>())
            .Where(x => name.IsEqualOrSubDomainOf(x.SignersName) && (x.SignatureInception <= DateTime.Now) && (x.SignatureExpiration >= DateTime.Now)).ToList();

        if (rrSigRecords.Count == 0)
        {
            return await ValidateOptOut(name, recordClass, state, token) ? DnsSecValidationResult.Unsigned : DnsSecValidationResult.Bogus;
        }

        DomainName zoneApex = rrSigRecords.OrderByDescending(x => x.Labels).First().SignersName;

        if (resultRecords.Count != 0)
            return await ValidateRrSigAsync(name, recordType, recordClass, resultRecords, rrSigRecords, zoneApex, msg, state, token);

        return await ValidateNonExistenceAsync(name, recordType, recordClass, rrSigRecords, DomainName.Asterisk + zoneApex, zoneApex, msg, state, token);
    }

    private async Task<bool> ValidateOptOut(DomainName name, RecordClass recordClass, TState state, CancellationToken token)
    {
        while (name != DomainName.Root)
        {
            DnsMessage msg = await _resolver.ResolveMessageAsync(name, RecordType.Ds, recordClass, state, token);

            if ((msg == null) || ((msg.ReturnCode != ReturnCode.NoError) && (msg.ReturnCode != ReturnCode.NxDomain)))
            {
                throw new Exception("DNS request failed");
            }

            List<RrSigRecord> rrSigRecords = msg
                .AnswerRecords.OfType<RrSigRecord>()
                .Union(msg.AuthorityRecords.OfType<RrSigRecord>())
                .Where(x => name.IsEqualOrSubDomainOf(x.SignersName) && (x.SignatureInception <= DateTime.Now) && (x.SignatureExpiration >= DateTime.Now)).ToList();

            if (rrSigRecords.Count != 0)
            {
                DomainName zoneApex = rrSigRecords.OrderByDescending(x => x.Labels).First().SignersName;

                var nonExistenceValidation = await ValidateNonExistenceAsync(name, RecordType.Ds, recordClass, rrSigRecords, name, zoneApex, msg, state, token);
                if ((nonExistenceValidation != DnsSecValidationResult.Bogus) && (nonExistenceValidation != DnsSecValidationResult.Indeterminate))
                    return true;
            }

            name = name.GetParentName();
        }
        return false;
    }

    private async Task<DnsSecValidationResult> ValidateNonExistenceAsync(DomainName name, RecordType recordType, RecordClass recordClass, List<RrSigRecord> rrSigRecords, DomainName stop, DomainName zoneApex, DnsMessageBase msg, TState state, CancellationToken token)
    {
        var nsecRes = await ValidateNSecAsync(name, recordType, recordClass, rrSigRecords, stop, zoneApex, msg, state, token);
        if (nsecRes == DnsSecValidationResult.Signed)
            return nsecRes;

        var nsec3Res = await ValidateNSec3Async(name, recordType, recordClass, rrSigRecords, stop == DomainName.Asterisk + zoneApex, zoneApex, msg, state, token);
        if (nsec3Res == DnsSecValidationResult.Signed)
            return nsec3Res;

        if ((nsecRes == DnsSecValidationResult.Unsigned) || (nsec3Res == DnsSecValidationResult.Unsigned))
            return DnsSecValidationResult.Unsigned;

        if ((nsecRes == DnsSecValidationResult.Bogus) || (nsec3Res == DnsSecValidationResult.Bogus))
            return DnsSecValidationResult.Bogus;

        return DnsSecValidationResult.Indeterminate;
    }

    private async Task<DnsSecValidationResult> ValidateNSecAsync(DomainName name, RecordType recordType, RecordClass recordClass, List<RrSigRecord> rrSigRecords, DomainName stop, DomainName zoneApex, DnsMessageBase msg, TState state, CancellationToken token)
    {
        List<NSecRecord> nsecRecords = msg.AuthorityRecords.OfType<NSecRecord>().ToList();

        if (nsecRecords.Count == 0)
            return DnsSecValidationResult.Indeterminate;

        foreach (var nsecGroup in nsecRecords.GroupBy(x => x.Name))
        {
            DnsSecValidationResult validationResult = await ValidateRrSigAsync(nsecGroup.Key, RecordType.NSec, recordClass, nsecGroup.ToList(), rrSigRecords, zoneApex, msg, state, token);

            if (validationResult != DnsSecValidationResult.Signed)
                return validationResult;
        }

        DomainName current = name;

        while (true)
        {
            if (current.Equals(stop))
            {
                return DnsSecValidationResult.Signed;
            }

            NSecRecord nsecRecord = nsecRecords.FirstOrDefault(x => x.Name.Equals(current));
            if (nsecRecord != null)
            {
                return nsecRecord.Types.Contains(recordType) ? DnsSecValidationResult.Bogus : DnsSecValidationResult.Signed;
            }
            else
            {
                nsecRecord = nsecRecords.FirstOrDefault(x => x.IsCovering(current, zoneApex));
                if (nsecRecord == null)
                    return DnsSecValidationResult.Bogus;
            }

            current = DomainName.Asterisk + current.GetParentName(current.Labels.Span[0] == "*" ? 2 : 1);
        }
    }

    private async Task<DnsSecValidationResult> ValidateNSec3Async(DomainName name, RecordType recordType, RecordClass recordClass, List<RrSigRecord> rrSigRecords, bool checkWildcard, DomainName zoneApex, DnsMessageBase msg, TState state, CancellationToken token)
    {
        List<NSec3Record> nsecRecords = msg.AuthorityRecords.OfType<NSec3Record>().ToList();

        if (nsecRecords.Count == 0)
            return DnsSecValidationResult.Indeterminate;

        foreach (var nsecGroup in nsecRecords.GroupBy(x => x.Name))
        {
            DnsSecValidationResult validationResult = await ValidateRrSigAsync(nsecGroup.Key, RecordType.NSec3, recordClass, nsecGroup.ToList(), rrSigRecords, zoneApex, msg, state, token);

            if (validationResult != DnsSecValidationResult.Signed)
                return validationResult;
        }

        var nsec3Parameter = nsecRecords.Where(x => x.Name.GetParentName().Equals(zoneApex)).Where(x => x.HashAlgorithm.IsSupported()).Select(x => new { x.HashAlgorithm, x.Iterations, x.Salt }).OrderBy(x => x.HashAlgorithm.GetPriority()).First();

        DomainName hashedName = name.GetNsec3HashName(nsec3Parameter.HashAlgorithm, nsec3Parameter.Iterations, nsec3Parameter.Salt, zoneApex);

        if (recordType == RecordType.Ds && nsecRecords.Any(x => (x.Flags == 1) && (x.IsCovering(hashedName))))
            return DnsSecValidationResult.Unsigned;

        var directMatch = nsecRecords.FirstOrDefault(x => x.Name.Equals(hashedName));
        if (directMatch != null)
        {
            return directMatch.Types.Contains(recordType) ? DnsSecValidationResult.Bogus : DnsSecValidationResult.Signed;
        }

        // find closest encloser
        DomainName current = name;
        DomainName previousHashedName = hashedName;

        while (true)
        {
            if (nsecRecords.Any(x => x.Name == hashedName))
                break;

            if (current == zoneApex)
                return DnsSecValidationResult.Bogus; // closest encloser could not be found, but at least the zone apex must be found as

            current = current.GetParentName();
            previousHashedName = hashedName;
            hashedName = current.GetNsec3HashName(nsec3Parameter.HashAlgorithm, nsec3Parameter.Iterations, nsec3Parameter.Salt, zoneApex);
        }

        if (!nsecRecords.Any(x => x.IsCovering(previousHashedName)))
            return DnsSecValidationResult.Bogus;

        if (checkWildcard)
        {
            DomainName wildcardHashName = (DomainName.Asterisk + current).GetNsec3HashName(nsec3Parameter.HashAlgorithm, nsec3Parameter.Iterations, nsec3Parameter.Salt, zoneApex);

            var wildcardDirectMatch = nsecRecords.FirstOrDefault(x => x.Name.Equals(wildcardHashName));
            if ((wildcardDirectMatch != null) && (!wildcardDirectMatch.Types.Contains(recordType)))
                return wildcardDirectMatch.Types.Contains(recordType) ? DnsSecValidationResult.Bogus : DnsSecValidationResult.Signed;

            var wildcardCoveringMatch = nsecRecords.FirstOrDefault(x => x.IsCovering(wildcardHashName));
            return (wildcardCoveringMatch != null) ? DnsSecValidationResult.Signed : DnsSecValidationResult.Bogus;
        }
        else
        {
            return DnsSecValidationResult.Signed;
        }
    }

    private async Task<DnsSecValidationResult> ValidateRrSigAsync<TRecord>(DomainName name, RecordType recordType, RecordClass recordClass, List<TRecord> resultRecords, List<RrSigRecord> rrSigRecords, DomainName zoneApex, DnsMessageBase msg, TState state, CancellationToken token)
        where TRecord : DnsRecordBase
    {
        DnsSecValidationResult res = DnsSecValidationResult.Bogus;

        foreach (var record in rrSigRecords.Where(x => x.Name.Equals(name) && (x.TypeCovered == recordType)))
        {
            res = await VerifyAsync(record, resultRecords, recordClass, state, token);
            if (res == DnsSecValidationResult.Signed)
            {
                if ((record.Labels == name.LabelCount)
                    || ((name.Labels.Span[0] == "*") && (record.Labels == name.LabelCount - 1)))
                    return DnsSecValidationResult.Signed;

                if (await ValidateNonExistenceAsync(name, recordType, recordClass, rrSigRecords, DomainName.Asterisk + record.Name.GetParentName(record.Name.LabelCount - record.Labels), zoneApex, msg, state, token) == DnsSecValidationResult.Signed)
                    return DnsSecValidationResult.Signed;
            }
        }

        return res;
    }

    private async Task<DnsSecValidationResult> VerifyAsync<TRecord>(RrSigRecord rrSigRecord, List<TRecord> coveredRecords, RecordClass recordClass, TState state, CancellationToken token)
        where TRecord : DnsRecordBase
    {
        if (rrSigRecord.TypeCovered == RecordType.DnsKey)
        {
            List<DsRecord> dsRecords;

            if (rrSigRecord.SignersName.Equals(DomainName.Root))
            {
                dsRecords = _resolverHintStore.RootKeys;
            }
            else
            {
                var dsRecordResults = await _resolver.ResolveSecureAsync<DsRecord>(rrSigRecord.SignersName, RecordType.Ds, recordClass, state, token);

                if ((dsRecordResults.ValidationResult == DnsSecValidationResult.Bogus) || (dsRecordResults.ValidationResult == DnsSecValidationResult.Indeterminate))
                    throw new DnsSecValidationException("DS records could not be retrieved");

                if (dsRecordResults.ValidationResult == DnsSecValidationResult.Unsigned)
                    return DnsSecValidationResult.Unsigned;

                dsRecords = dsRecordResults.Records;
            }

            return dsRecords.Any(dsRecord => rrSigRecord.Verify(coveredRecords, coveredRecords.Cast<DnsKeyRecord>().Where(dsRecord.IsCovering).ToList())) ? DnsSecValidationResult.Signed : DnsSecValidationResult.Bogus;
        }
        else
        {
            var dnsKeyRecordResults = await _resolver.ResolveSecureAsync<DnsKeyRecord>(rrSigRecord.SignersName, RecordType.DnsKey, recordClass, state, token);

            if ((dnsKeyRecordResults.ValidationResult == DnsSecValidationResult.Bogus) || (dnsKeyRecordResults.ValidationResult == DnsSecValidationResult.Indeterminate))
                throw new DnsSecValidationException("DNSKEY records could not be retrieved");

            if (dnsKeyRecordResults.ValidationResult == DnsSecValidationResult.Unsigned)
                return DnsSecValidationResult.Unsigned;

            return rrSigRecord.Verify(coveredRecords, dnsKeyRecordResults.Records) ? DnsSecValidationResult.Signed : DnsSecValidationResult.Bogus;
        }
    }
}

/// <summary>
///   <para>Delegation signer</para>
///   <para>
///     Defined in
///     <see cref="!:http://tools.ietf.org/html/rfc4034">RFC 4034</see>
///     and
///     <see cref="!:http://tools.ietf.org/html/rfc3658">RFC 3658</see>
///   </para>
/// </summary>
public class DsRecord : DnsRecordBase
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

    internal DsRecord() { }

    /// <summary>
    ///   Creates a new instance of the DsRecord class
    /// </summary>
    /// <param name="name"> Name of the record </param>
    /// <param name="recordClass"> Class of the record </param>
    /// <param name="timeToLive"> Seconds the record should be cached at most </param>
    /// <param name="keyTag"> Key tag </param>
    /// <param name="algorithm"> Algorithm used </param>
    /// <param name="digestType"> Type of the digest </param>
    /// <param name="digest"> Binary data of the digest </param>
    public DsRecord(DomainName name, RecordClass recordClass, int timeToLive, ushort keyTag, DnsSecAlgorithm algorithm, DnsSecDigestType digestType, byte[] digest)
        : base(name, RecordType.Ds, recordClass, timeToLive)
    {
        KeyTag = keyTag;
        Algorithm = algorithm;
        DigestType = digestType;
        Digest = digest ?? new byte[] { };
    }

    /// <summary>
    ///   Creates a new instance of the DsRecord class
    /// </summary>
    /// <param name="key"> The key, that should be covered </param>
    /// <param name="timeToLive"> Seconds the record should be cached at most </param>
    /// <param name="digestType"> Type of the digest </param>
    public DsRecord(DnsKeyRecord key, int timeToLive, DnsSecDigestType digestType)
        : base(key.Name, RecordType.Ds, key.RecordClass, timeToLive)
    {
        KeyTag = key.CalculateKeyTag();
        Algorithm = key.Algorithm;
        DigestType = digestType;
        Digest = CalculateKeyHash(key);
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

    internal bool IsCovering(DnsKeyRecord dnsKeyRecord)
    {
        if (dnsKeyRecord.Algorithm != Algorithm)
            return false;

        if (dnsKeyRecord.CalculateKeyTag() != KeyTag)
            return false;

        byte[] hash = CalculateKeyHash(dnsKeyRecord);

        return StructuralComparisons.StructuralEqualityComparer.Equals(hash, Digest);
    }

    private byte[] CalculateKeyHash(DnsKeyRecord dnsKeyRecord)
    {
        byte[] buffer = new byte[dnsKeyRecord.Name.MaximumRecordDataLength + 2 + dnsKeyRecord.MaximumRecordDataLength];

        int currentPosition = 0;

        DnsMessageBase.EncodeDomainName(buffer, 0, ref currentPosition, dnsKeyRecord.Name, null, true);
        dnsKeyRecord.EncodeRecordData(buffer, 0, ref currentPosition, null, true);

        var hashAlgorithm = GetHashAlgorithm();

        hashAlgorithm.BlockUpdate(buffer, 0, currentPosition);

        byte[] hash = new byte[hashAlgorithm.GetDigestSize()];

        hashAlgorithm.DoFinal(hash, 0);
        return hash;
    }

    private IDigest GetHashAlgorithm()
    {
        switch (DigestType)
        {
            case DnsSecDigestType.Sha1:
                return new Sha1Digest();

            case DnsSecDigestType.Sha256:
                return new Sha256Digest();

            case DnsSecDigestType.EccGost:
                return new Gost3411Digest();

            case DnsSecDigestType.Sha384:
                return new Sha384Digest();

            default:
                throw new NotSupportedException();
        }
    }
}

internal interface IInternalDnsSecResolver<in TState>
{
    Task<DnsMessage> ResolveMessageAsync(DomainName name, RecordType recordType, RecordClass recordClass, TState state, CancellationToken token);

    Task<DnsSecResult<TRecord>> ResolveSecureAsync<TRecord>(DomainName name, RecordType recordType, RecordClass recordClass, TState state, CancellationToken token)
        where TRecord : DnsRecordBase;
}

/// <summary>
///   <para>Security Key record</para>
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
public class KeyRecord : KeyRecordBase
{
    /// <summary>
    ///   Binary data of the public key
    /// </summary>
    public byte[] PublicKey { get; private set; }

    internal KeyRecord() { }

    /// <summary>
    ///   Creates of new instance of the KeyRecord class
    /// </summary>
    /// <param name="name"> Name of the record </param>
    /// <param name="recordClass"> Class of the record </param>
    /// <param name="timeToLive"> Seconds the record should be cached at most </param>
    /// <param name="flags"> Flags of the key </param>
    /// <param name="protocol"> Protocol for which the key is used </param>
    /// <param name="algorithm"> Algorithm of the key </param>
    /// <param name="publicKey"> Binary data of the public key </param>
    public KeyRecord(DomainName name, RecordClass recordClass, int timeToLive, ushort flags, ProtocolType protocol, DnsSecAlgorithm algorithm, byte[] publicKey)
        : base(name, recordClass, timeToLive, flags, protocol, algorithm)
    {
        PublicKey = publicKey ?? new byte[] { };
    }

    protected override void ParsePublicKey(ReadOnlySpan<byte> resultData, int startPosition, int length)
    {
        PublicKey = DnsMessageBase.ParseByteData(resultData, ref startPosition, length);
    }

    internal override void ParseRecordData(DomainName origin, string[] stringRepresentation)
    {
        if (stringRepresentation.Length < 1)
            throw new FormatException();

        PublicKey = String.Join(String.Empty, stringRepresentation).FromBase64String();
    }

    protected override string PublicKeyToString()
    {
        return PublicKey.ToBase64String();
    }

    protected override int MaximumPublicKeyLength => PublicKey.Length;

    protected override void EncodePublicKey(Span<byte> messageData, int offset, ref int currentPosition, Dictionary<DomainName, ushort> domainNames)
    {
        DnsMessageBase.EncodeByteArray(messageData, ref currentPosition, PublicKey);
    }
}

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
public abstract class KeyRecordBase : DnsRecordBase
{
    /// <summary>
    ///   Type of key
    /// </summary>
    public enum KeyTypeFlag : ushort
    {
        /// <summary>
        ///   <para>Use of the key is prohibited for authentication</para>
        ///   <para>
        ///     Defined in
        ///     <see cref="!:http://tools.ietf.org/html/rfc2535">RFC 2535</see>
        ///   </para>
        /// </summary>
        AuthenticationProhibited = 0x8000,

        /// <summary>
        ///   <para>Use of the key is prohibited for confidentiality</para>
        ///   <para>
        ///     Defined in
        ///     <see cref="!:http://tools.ietf.org/html/rfc2535">RFC 2535</see>
        ///   </para>
        /// </summary>
        ConfidentialityProhibited = 0x4000,

        /// <summary>
        ///   <para>Use of the key for authentication and/or confidentiality is permitted</para>
        ///   <para>
        ///     Defined in
        ///     <see cref="!:http://tools.ietf.org/html/rfc2535">RFC 2535</see>
        ///   </para>
        /// </summary>
        BothProhibited = 0x0000,

        /// <summary>
        ///   <para>There is no key information</para>
        ///   <para>
        ///     Defined in
        ///     <see cref="!:http://tools.ietf.org/html/rfc2535">RFC 2535</see>
        ///   </para>
        /// </summary>
        NoKey = 0xc000,
    }

    /// <summary>
    ///   Type of name
    /// </summary>
    public enum NameTypeFlag : ushort
    {
        /// <summary>
        ///   <para>Key is associated with a user or account</para>
        ///   <para>
        ///     Defined in
        ///     <see cref="!:http://tools.ietf.org/html/rfc2535">RFC 2535</see>
        ///   </para>
        /// </summary>
        User = 0x0000,

        /// <summary>
        ///   <para>Key is associated with a zone</para>
        ///   <para>
        ///     Defined in
        ///     <see cref="!:http://tools.ietf.org/html/rfc2535">RFC 2535</see>
        ///   </para>
        /// </summary>
        Zone = 0x0100,

        /// <summary>
        ///   <para>Key is associated with a host</para>
        ///   <para>
        ///     Defined in
        ///     <see cref="!:http://tools.ietf.org/html/rfc2535">RFC 2535</see>
        ///   </para>
        /// </summary>
        Host = 0x0200,

        /// <summary>
        ///   <para>Reserved</para>
        ///   <para>
        ///     Defined in
        ///     <see cref="!:http://tools.ietf.org/html/rfc2535">RFC 2535</see>
        ///   </para>
        /// </summary>
        Reserved = 0x0300,
    }

    /// <summary>
    ///   Protocol for which the key is used
    /// </summary>
    public enum ProtocolType : byte
    {
        /// <summary>
        ///   <para>Use in connection with TLS</para>
        ///   <para>
        ///     Defined in
        ///     <see cref="!:http://tools.ietf.org/html/rfc2535">RFC 2535</see>
        ///   </para>
        /// </summary>
        Tls = 1,

        /// <summary>
        ///   <para>Use in connection with email</para>
        ///   <para>
        ///     Defined in
        ///     <see cref="!:http://tools.ietf.org/html/rfc2535">RFC 2535</see>
        ///   </para>
        /// </summary>
        Email = 2,

        /// <summary>
        ///   <para>Used for DNS security</para>
        ///   <para>
        ///     Defined in
        ///     <see cref="!:http://tools.ietf.org/html/rfc2535">RFC 2535</see>
        ///   </para>
        /// </summary>
        DnsSec = 3,

        /// <summary>
        ///   <para>Refer to the Oakley/IPSEC  protocol</para>
        ///   <para>
        ///     Defined in
        ///     <see cref="!:http://tools.ietf.org/html/rfc2535">RFC 2535</see>
        ///   </para>
        /// </summary>
        IpSec = 4,

        /// <summary>
        ///   <para>Used in connection with any protocol</para>
        ///   <para>
        ///     Defined in
        ///     <see cref="!:http://tools.ietf.org/html/rfc2535">RFC 2535</see>
        ///   </para>
        /// </summary>
        Any = 255,
    }

    /// <summary>
    ///   Flags of the key
    /// </summary>
    public ushort Flags { get; private set; }

    /// <summary>
    ///   Protocol for which the key is used
    /// </summary>
    public ProtocolType Protocol { get; private set; }

    /// <summary>
    ///   Algorithm of the key
    /// </summary>
    public DnsSecAlgorithm Algorithm { get; private set; }

    #region Flags
    /// <summary>
    ///   Type of key
    /// </summary>
    public KeyTypeFlag Type
    {
        get { return (KeyTypeFlag)(Flags & 0xc000); }
        set
        {
            ushort clearedOp = (ushort)(Flags & 0x3fff);
            Flags = (ushort)(clearedOp | (ushort)value);
        }
    }

    /// <summary>
    ///   True, if a second flag field should be added
    /// </summary>
    public bool IsExtendedFlag
    {
        get { return (Flags & 0x1000) != 0; }
        set
        {
            if (value)
            {
                Flags |= 0x1000;
            }
            else
            {
                Flags &= 0xefff;
            }
        }
    }

    /// <summary>
    ///   Type of name
    /// </summary>
    public NameTypeFlag NameType
    {
        get { return (NameTypeFlag)(Flags & 0x0300); }
        set
        {
            ushort clearedOp = (ushort)(Flags & 0xfcff);
            Flags = (ushort)(clearedOp | (ushort)value);
        }
    }

    /// <summary>
    ///   Is the key authorized for zone updates
    /// </summary>
    public bool IsZoneSignatory
    {
        get { return (Flags & 0x0008) != 0; }
        set
        {
            if (value)
            {
                Flags |= 0x0008;
            }
            else
            {
                Flags &= 0xfff7;
            }
        }
    }

    /// <summary>
    ///   Is the key authorized for updates of records signed with other key
    /// </summary>
    public bool IsStrongSignatory
    {
        get { return (Flags & 0x0004) != 0; }
        set
        {
            if (value)
            {
                Flags |= 0x0004;
            }
            else
            {
                Flags &= 0xfffb;
            }
        }
    }

    /// <summary>
    ///   Is the key only authorized for update of records with the same record name as the key
    /// </summary>
    public bool IsUniqueSignatory
    {
        get { return (Flags & 0x0002) != 0; }
        set
        {
            if (value)
            {
                Flags |= 0x0002;
            }
            else
            {
                Flags &= 0xfffd;
            }
        }
    }

    /// <summary>
    ///   Is the key an update key
    /// </summary>
    public bool IsGeneralSignatory
    {
        get { return (Flags & 0x0001) != 0; }
        set
        {
            if (value)
            {
                Flags |= 0x0001;
            }
            else
            {
                Flags &= 0xfffe;
            }
        }
    }
    #endregion

    protected KeyRecordBase() { }

    protected KeyRecordBase(DomainName name, RecordClass recordClass, int timeToLive, ushort flags, ProtocolType protocol, DnsSecAlgorithm algorithm)
        : base(name, RecordType.Key, recordClass, timeToLive)
    {
        Flags = flags;
        Protocol = protocol;
        Algorithm = algorithm;
    }

    internal override sealed void ParseRecordData(ReadOnlySpan<byte> resultData, int startPosition, int length)
    {
        Flags = DnsMessageBase.ParseUShort(resultData, ref startPosition);
        Protocol = (ProtocolType)resultData[startPosition++];
        Algorithm = (DnsSecAlgorithm)resultData[startPosition++];
        ParsePublicKey(resultData, startPosition, length - 4);
    }

    protected abstract void ParsePublicKey(ReadOnlySpan<byte> resultData, int startPosition, int length);

    internal override sealed string RecordDataToString()
    {
        return Flags
               + " " + (byte)Protocol
               + " " + (byte)Algorithm
               + " " + PublicKeyToString();
    }

    protected abstract string PublicKeyToString();

    protected internal override sealed int MaximumRecordDataLength => 4 + MaximumPublicKeyLength;

    protected abstract int MaximumPublicKeyLength { get; }

    protected internal override sealed void EncodeRecordData(Span<byte> messageData, int offset, ref int currentPosition, Dictionary<DomainName, ushort> domainNames, bool useCanonical)
    {
        DnsMessageBase.EncodeUShort(messageData, ref currentPosition, Flags);
        messageData[currentPosition++] = (byte)Protocol;
        messageData[currentPosition++] = (byte)Algorithm;
        EncodePublicKey(messageData, offset, ref currentPosition, domainNames);
    }

    protected abstract void EncodePublicKey(Span<byte> messageData, int offset, ref int currentPosition, Dictionary<DomainName, ushort> domainNames);
}

/// <summary>
///   DNSSEC algorithm type
/// </summary>
public enum NSec3HashAlgorithm : byte
{
    /// <summary>
    ///   <para>RSA MD5</para>
    ///   <para>
    ///     Defined in
    ///     <see cref="!:http://tools.ietf.org/html/rfc5155">RFC 5155</see>
    ///   </para>
    /// </summary>
    Sha1 = 1,
}

internal static class NSec3HashAlgorithmHelper
{
    public static bool IsSupported(this NSec3HashAlgorithm algorithm)
    {
        switch (algorithm)
        {
            case NSec3HashAlgorithm.Sha1:
                return true;

            default:
                return false;
        }
    }

    public static int GetPriority(this NSec3HashAlgorithm algorithm)
    {
        switch (algorithm)
        {
            case NSec3HashAlgorithm.Sha1:
                return 1;

            default:
                throw new NotSupportedException();
        }
    }
}

/// <summary>
///   <para>Hashed next owner parameter record</para>
///   <para>
///     Defined in
///     <see cref="!:http://tools.ietf.org/html/rfc5155">RFC 5155</see>
///   </para>
/// </summary>
public class NSec3ParamRecord : DnsRecordBase
{
    /// <summary>
    ///   Algorithm of the hash
    /// </summary>
    public NSec3HashAlgorithm HashAlgorithm { get; private set; }

    /// <summary>
    ///   Flags of the record
    /// </summary>
    public byte Flags { get; private set; }

    /// <summary>
    ///   Number of iterations
    /// </summary>
    public ushort Iterations { get; private set; }

    /// <summary>
    ///   Binary data of salt
    /// </summary>
    public byte[] Salt { get; private set; }

    internal NSec3ParamRecord() { }

    /// <summary>
    ///   Creates a new instance of the NSec3ParamRecord class
    /// </summary>
    /// <param name="name"> Name of the record </param>
    /// <param name="recordClass"> Class of the record </param>
    /// <param name="timeToLive"> Seconds the record should be cached at most </param>
    /// <param name="hashAlgorithm"> Algorithm of hash </param>
    /// <param name="flags"> Flags of the record </param>
    /// <param name="iterations"> Number of iterations </param>
    /// <param name="salt"> Binary data of salt </param>
    public NSec3ParamRecord(DomainName name, RecordClass recordClass, int timeToLive, NSec3HashAlgorithm hashAlgorithm, byte flags, ushort iterations, byte[] salt)
        : base(name, RecordType.NSec3Param, recordClass, timeToLive)
    {
        HashAlgorithm = hashAlgorithm;
        Flags = flags;
        Iterations = iterations;
        Salt = salt ?? new byte[] { };
    }

    internal override void ParseRecordData(ReadOnlySpan<byte> resultData, int currentPosition, int length)
    {
        HashAlgorithm = (NSec3HashAlgorithm)resultData[currentPosition++];
        Flags = resultData[currentPosition++];
        Iterations = DnsMessageBase.ParseUShort(resultData, ref currentPosition);
        int saltLength = resultData[currentPosition++];
        Salt = DnsMessageBase.ParseByteData(resultData, ref currentPosition, saltLength);
    }

    internal override void ParseRecordData(DomainName origin, string[] stringRepresentation)
    {
        if (stringRepresentation.Length != 4)
            throw new FormatException();

        HashAlgorithm = (NSec3HashAlgorithm)Byte.Parse(stringRepresentation[0]);
        Flags = Byte.Parse(stringRepresentation[1]);
        Iterations = UInt16.Parse(stringRepresentation[2]);
        Salt = (stringRepresentation[3] == "-") ? new byte[] { } : stringRepresentation[3].FromBase16String();
    }

    internal override string RecordDataToString()
    {
        return (byte)HashAlgorithm
               + " " + Flags
               + " " + Iterations
               + " " + ((Salt.Length == 0) ? "-" : Salt.ToBase16String());
    }

    protected internal override int MaximumRecordDataLength => 5 + Salt.Length;

    protected internal override void EncodeRecordData(Span<byte> messageData, int offset, ref int currentPosition, Dictionary<DomainName, ushort> domainNames, bool useCanonical)
    {
        messageData[currentPosition++] = (byte)HashAlgorithm;
        messageData[currentPosition++] = Flags;
        DnsMessageBase.EncodeUShort(messageData, ref currentPosition, Iterations);
        messageData[currentPosition++] = (byte)Salt.Length;
        DnsMessageBase.EncodeByteArray(messageData, ref currentPosition, Salt);
    }
}

/// <summary>
///   Hashed next owner
///   <para>
///     Defined in
///     <see cref="!:http://tools.ietf.org/html/rfc5155">RFC 5155</see>
///   </para>
/// </summary>
public class NSec3Record : DnsRecordBase
{
    /// <summary>
    ///   Algorithm of hash
    /// </summary>
    public NSec3HashAlgorithm HashAlgorithm { get; private set; }

    /// <summary>
    ///   Flags of the record
    /// </summary>
    public byte Flags { get; private set; }

    /// <summary>
    ///   Number of iterations
    /// </summary>
    public ushort Iterations { get; private set; }

    /// <summary>
    ///   Binary data of salt
    /// </summary>
    public byte[] Salt { get; private set; }

    /// <summary>
    ///   Binary data of hash of next owner
    /// </summary>
    public byte[] NextHashedOwnerName { get; internal set; }

    /// <summary>
    ///   Types of next owner
    /// </summary>
    public List<RecordType> Types { get; private set; }

    internal NSec3Record() { }

    /// <summary>
    ///   Creates of new instance of the NSec3Record class
    /// </summary>
    /// <param name="name"> Name of the record </param>
    /// <param name="recordClass"> Class of the record </param>
    /// <param name="timeToLive"> Seconds the record should be cached at most </param>
    /// <param name="hashAlgorithm"> Algorithm of hash </param>
    /// <param name="flags"> Flags of the record </param>
    /// <param name="iterations"> Number of iterations </param>
    /// <param name="salt"> Binary data of salt </param>
    /// <param name="nextHashedOwnerName"> Binary data of hash of next owner </param>
    /// <param name="types"> Types of next owner </param>
    public NSec3Record(DomainName name, RecordClass recordClass, int timeToLive, NSec3HashAlgorithm hashAlgorithm, byte flags, ushort iterations, byte[] salt, byte[] nextHashedOwnerName, List<RecordType> types)
        : base(name, RecordType.NSec3, recordClass, timeToLive)
    {
        HashAlgorithm = hashAlgorithm;
        Flags = flags;
        Iterations = iterations;
        Salt = salt ?? new byte[] { };
        NextHashedOwnerName = nextHashedOwnerName ?? new byte[] { };

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

        HashAlgorithm = (NSec3HashAlgorithm)resultData[currentPosition++];
        Flags = resultData[currentPosition++];
        Iterations = DnsMessageBase.ParseUShort(resultData, ref currentPosition);
        int saltLength = resultData[currentPosition++];
        Salt = DnsMessageBase.ParseByteData(resultData, ref currentPosition, saltLength);
        int hashLength = resultData[currentPosition++];
        NextHashedOwnerName = DnsMessageBase.ParseByteData(resultData, ref currentPosition, hashLength);
        Types = NSecRecord.ParseTypeBitMap(resultData, ref currentPosition, endPosition);
    }

    internal override void ParseRecordData(DomainName origin, string[] stringRepresentation)
    {
        if (stringRepresentation.Length < 5)
            throw new FormatException();

        HashAlgorithm = (NSec3HashAlgorithm)Byte.Parse(stringRepresentation[0]);
        Flags = Byte.Parse(stringRepresentation[1]);
        Iterations = UInt16.Parse(stringRepresentation[2]);
        Salt = (stringRepresentation[3] == "-") ? new byte[] { } : stringRepresentation[3].FromBase16String();
        NextHashedOwnerName = stringRepresentation[4].FromBase32HexString();
        Types = stringRepresentation.Skip(5).Select(RecordTypeHelper.ParseShortString).ToList();
    }

    internal override string RecordDataToString()
    {
        return (byte)HashAlgorithm
               + " " + Flags
               + " " + Iterations
               + " " + ((Salt.Length == 0) ? "-" : Salt.ToBase16String())
               + " " + NextHashedOwnerName.ToBase32HexString()
               + " " + String.Join(" ", Types.Select(RecordTypeHelper.ToShortString));
    }

    protected internal override int MaximumRecordDataLength => 6 + Salt.Length + NextHashedOwnerName.Length + NSecRecord.GetMaximumTypeBitmapLength(Types);

    protected internal override void EncodeRecordData(Span<byte> messageData, int offset, ref int currentPosition, Dictionary<DomainName, ushort> domainNames, bool useCanonical)
    {
        messageData[currentPosition++] = (byte)HashAlgorithm;
        messageData[currentPosition++] = Flags;
        DnsMessageBase.EncodeUShort(messageData, ref currentPosition, Iterations);
        messageData[currentPosition++] = (byte)Salt.Length;
        DnsMessageBase.EncodeByteArray(messageData, ref currentPosition, Salt);
        messageData[currentPosition++] = (byte)NextHashedOwnerName.Length;
        DnsMessageBase.EncodeByteArray(messageData, ref currentPosition, NextHashedOwnerName);

        if (Types.Count > 0)
            NSecRecord.EncodeTypeBitmap(messageData, ref currentPosition, Types);
    }

    internal bool IsCovering(DomainName name)
    {
        DomainName nextDomainName = new DomainName(NextHashedOwnerName.ToBase32HexString(), name.GetParentName());

        return ((name.CompareTo(Name) > 0) && (name.CompareTo(nextDomainName) < 0));
    }
}

/// <summary>
///   <para>Next owner</para>
///   <para>
///     Defined in
///     <see cref="!:http://tools.ietf.org/html/rfc4034">RFC 4034</see>
///     and
///     <see cref="!:http://tools.ietf.org/html/rfc3755">RFC 3755</see>
///   </para>
/// </summary>
public class NSecRecord : DnsRecordBase
{
    /// <summary>
    ///   Name of next owner
    /// </summary>
    public DomainName NextDomainName { get; internal set; }

    /// <summary>
    ///   Record types of the next owner
    /// </summary>
    public List<RecordType> Types { get; private set; }

    internal NSecRecord() { }

    /// <summary>
    ///   Creates a new instance of the NSecRecord class
    /// </summary>
    /// <param name="name"> Name of the record </param>
    /// <param name="recordClass"> Class of the record </param>
    /// <param name="timeToLive"> Seconds the record should be cached at most </param>
    /// <param name="nextDomainName"> Name of next owner </param>
    /// <param name="types"> Record types of the next owner </param>
    public NSecRecord(DomainName name, RecordClass recordClass, int timeToLive, DomainName nextDomainName, List<RecordType> types)
        : base(name, RecordType.NSec, recordClass, timeToLive)
    {
        NextDomainName = nextDomainName ?? DomainName.Root;

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

        NextDomainName = DnsMessageBase.ParseDomainName(resultData, ref currentPosition);

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
        if (stringRepresentation.Length < 2)
            throw new FormatException();

        NextDomainName = ParseDomainName(origin, stringRepresentation[0]);
        Types = stringRepresentation.Skip(1).Select(RecordTypeHelper.ParseShortString).ToList();
    }

    internal override string RecordDataToString()
    {
        return NextDomainName
               + " " + String.Join(" ", Types.Select(RecordTypeHelper.ToShortString));
    }

    protected internal override int MaximumRecordDataLength => 2 + NextDomainName.MaximumRecordDataLength + GetMaximumTypeBitmapLength(Types);

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
        DnsMessageBase.EncodeDomainName(messageData, offset, ref currentPosition, NextDomainName, null, useCanonical);
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

    internal bool IsCovering(DomainName name, DomainName zone)
    {
        return ((name.CompareTo(Name) > 0) && (name.CompareTo(NextDomainName) < 0)) // within zone
               || ((name.CompareTo(Name) > 0) && NextDomainName.Equals(zone)); // behind zone
    }
}

/// <summary>
///   <para>Record signature record</para>
///   <para>
///     Defined in
///     <see cref="!:http://tools.ietf.org/html/rfc4034">RFC 4034</see>
///     and
///     <see cref="!:http://tools.ietf.org/html/rfc3755">RFC 3755</see>
///   </para>
/// </summary>
public class RrSigRecord : DnsRecordBase
{
    /// <summary>
    ///   <see cref="RecordType">Record type</see> that is covered by this record
    /// </summary>
    public RecordType TypeCovered { get; private set; }

    /// <summary>
    ///   <see cref="DnsSecAlgorithm">Algorithm</see> that is used for signature
    /// </summary>
    public DnsSecAlgorithm Algorithm { get; private set; }

    /// <summary>
    ///   Label count of original record that is covered by this record
    /// </summary>
    public byte Labels { get; private set; }

    /// <summary>
    ///   Original time to live value of original record that is covered by this record
    /// </summary>
    public int OriginalTimeToLive { get; private set; }

    /// <summary>
    ///   Signature is valid until this date
    /// </summary>
    public DateTime SignatureExpiration { get; private set; }

    /// <summary>
    ///   Signature is valid from this date
    /// </summary>
    public DateTime SignatureInception { get; private set; }

    /// <summary>
    ///   Key tag
    /// </summary>
    public ushort KeyTag { get; private set; }

    /// <summary>
    ///   Domain name of generator of the signature
    /// </summary>
    public DomainName SignersName { get; private set; }

    /// <summary>
    ///   Binary data of the signature
    /// </summary>
    public byte[] Signature { get; internal set; }

    internal RrSigRecord() { }

    /// <summary>
    ///   Creates a new instance of the RrSigRecord class
    /// </summary>
    /// <param name="name"> Name of the record </param>
    /// <param name="recordClass"> Class of the record </param>
    /// <param name="timeToLive"> Seconds the record should be cached at most </param>
    /// <param name="typeCovered">
    ///   <see cref="RecordType">Record type</see> that is covered by this record
    /// </param>
    /// <param name="algorithm">
    ///   <see cref="DnsSecAlgorithm">Algorithm</see> that is used for signature
    /// </param>
    /// <param name="labels"> Label count of original record that is covered by this record </param>
    /// <param name="originalTimeToLive"> Original time to live value of original record that is covered by this record </param>
    /// <param name="signatureExpiration"> Signature is valid until this date </param>
    /// <param name="signatureInception"> Signature is valid from this date </param>
    /// <param name="keyTag"> Key tag </param>
    /// <param name="signersName"> Domain name of generator of the signature </param>
    /// <param name="signature"> Binary data of the signature </param>
    public RrSigRecord(DomainName name, RecordClass recordClass, int timeToLive, RecordType typeCovered, DnsSecAlgorithm algorithm, byte labels, int originalTimeToLive, DateTime signatureExpiration, DateTime signatureInception, ushort keyTag, DomainName signersName, byte[] signature)
        : base(name, RecordType.RrSig, recordClass, timeToLive)
    {
        TypeCovered = typeCovered;
        Algorithm = algorithm;
        Labels = labels;
        OriginalTimeToLive = originalTimeToLive;
        SignatureExpiration = signatureExpiration;
        SignatureInception = signatureInception;
        KeyTag = keyTag;
        SignersName = signersName ?? DomainName.Root;
        Signature = signature ?? new byte[] { };
    }

    internal RrSigRecord(List<DnsRecordBase> records, DnsKeyRecord key, DateTime inception, DateTime expiration)
        : base(records[0].Name, RecordType.RrSig, records[0].RecordClass, records[0].TimeToLive)
    {
        TypeCovered = records[0].RecordType;
        Algorithm = key.Algorithm;
        Labels = (byte)(records[0].Name.Labels.Span[0] == DomainName.Asterisk.Labels.Span[0] ? records[0].Name.LabelCount - 1 : records[0].Name.LabelCount);
        OriginalTimeToLive = records[0].TimeToLive;
        SignatureExpiration = expiration;
        SignatureInception = inception;
        KeyTag = key.CalculateKeyTag();
        SignersName = key.Name;
        Signature = new byte[] { };

        byte[] signBuffer;
        int signBufferLength;
        EncodeSigningBuffer(records, out signBuffer, out signBufferLength);

        Signature = key.Sign(signBuffer, signBufferLength);
    }

    internal override void ParseRecordData(ReadOnlySpan<byte> resultData, int startPosition, int length)
    {
        int currentPosition = startPosition;

        TypeCovered = (RecordType)DnsMessageBase.ParseUShort(resultData, ref currentPosition);
        Algorithm = (DnsSecAlgorithm)resultData[currentPosition++];
        Labels = resultData[currentPosition++];
        OriginalTimeToLive = DnsMessageBase.ParseInt(resultData, ref currentPosition);
        SignatureExpiration = ParseDateTime(resultData, ref currentPosition);
        SignatureInception = ParseDateTime(resultData, ref currentPosition);
        KeyTag = DnsMessageBase.ParseUShort(resultData, ref currentPosition);
        SignersName = DnsMessageBase.ParseDomainName(resultData, ref currentPosition);
        Signature = DnsMessageBase.ParseByteData(resultData, ref currentPosition, length + startPosition - currentPosition);
    }

    internal override void ParseRecordData(DomainName origin, string[] stringRepresentation)
    {
        if (stringRepresentation.Length < 9)
            throw new FormatException();

        TypeCovered = RecordTypeHelper.ParseShortString(stringRepresentation[0]);
        Algorithm = (DnsSecAlgorithm)Byte.Parse(stringRepresentation[1]);
        Labels = Byte.Parse(stringRepresentation[2]);
        OriginalTimeToLive = Int32.Parse(stringRepresentation[3]);
        SignatureExpiration = DateTime.ParseExact(stringRepresentation[4], "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
        SignatureInception = DateTime.ParseExact(stringRepresentation[5], "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
        KeyTag = UInt16.Parse(stringRepresentation[6]);
        SignersName = ParseDomainName(origin, stringRepresentation[7]);
        Signature = String.Join(String.Empty, stringRepresentation.Skip(8)).FromBase64String();
    }

    internal override string RecordDataToString()
    {
        return TypeCovered.ToShortString()
               + " " + (byte)Algorithm
               + " " + Labels
               + " " + OriginalTimeToLive
               + " " + SignatureExpiration.ToUniversalTime().ToString("yyyyMMddHHmmss")
               + " " + SignatureInception.ToUniversalTime().ToString("yyyyMMddHHmmss")
               + " " + KeyTag
               + " " + SignersName
               + " " + Signature.ToBase64String();
    }

    protected internal override int MaximumRecordDataLength => 20 + SignersName.MaximumRecordDataLength + Signature.Length;

    protected internal override void EncodeRecordData(Span<byte> messageData, int offset, ref int currentPosition, Dictionary<DomainName, ushort> domainNames, bool useCanonical)
    {
        EncodeRecordData(messageData, offset, ref currentPosition, domainNames, useCanonical, true);
    }

    internal void EncodeRecordData(Span<byte> messageData, int offset, ref int currentPosition, Dictionary<DomainName, ushort> domainNames, bool useCanonical, bool encodeSignature)
    {
        DnsMessageBase.EncodeUShort(messageData, ref currentPosition, (ushort)TypeCovered);
        messageData[currentPosition++] = (byte)Algorithm;
        messageData[currentPosition++] = Labels;
        DnsMessageBase.EncodeInt(messageData, ref currentPosition, OriginalTimeToLive);
        EncodeDateTime(messageData, ref currentPosition, SignatureExpiration);
        EncodeDateTime(messageData, ref currentPosition, SignatureInception);
        DnsMessageBase.EncodeUShort(messageData, ref currentPosition, KeyTag);
        DnsMessageBase.EncodeDomainName(messageData, offset, ref currentPosition, SignersName, null, useCanonical);

        if (encodeSignature)
            DnsMessageBase.EncodeByteArray(messageData, ref currentPosition, Signature);
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

    internal bool Verify<T>(List<T> coveredRecords, IEnumerable<DnsKeyRecord> dnsKeys)
        where T : DnsRecordBase
    {
        byte[] messageData;
        int length;

        EncodeSigningBuffer(coveredRecords, out messageData, out length);

        return dnsKeys
            .Where(x => x.IsZoneKey && (x.Protocol == 3) && x.Algorithm.IsSupported() && (KeyTag == x.CalculateKeyTag()))
            .Any(x => x.Verify(messageData, length, Signature));
    }

    private void EncodeSigningBuffer<T>(List<T> records, out byte[] messageData, out int length)
        where T : DnsRecordBase
    {
        messageData = new byte[2 + MaximumRecordDataLength - Signature.Length + records.Sum(x => x.MaximumLength)];
        length = 0;
        EncodeRecordData(messageData, 0, ref length, null, true, false);
        foreach (var record in records.OrderBy(x => x))
        {
            if (record.Name.LabelCount == Labels)
            {
                DnsMessageBase.EncodeDomainName(messageData, 0, ref length, record.Name, null, true);
            }
            else if (record.Name.LabelCount > Labels)
            {
                DnsMessageBase.EncodeDomainName(messageData, 0, ref length, DomainName.Asterisk + record.Name.GetParentName(record.Name.LabelCount - Labels), null, true);
            }
            else
            {
                throw new Exception("Encoding of records with less labels than RrSigRecord is not allowed");
            }
            DnsMessageBase.EncodeUShort(messageData, ref length, (ushort)record.RecordType);
            DnsMessageBase.EncodeUShort(messageData, ref length, (ushort)record.RecordClass);
            DnsMessageBase.EncodeInt(messageData, ref length, OriginalTimeToLive);

            record.EncodeRecordBody(messageData, 0, ref length, null, true);
        }
    }
}

/// <summary>
///   <para>Security signature record</para>
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
public class SigRecord : DnsRecordBase
{
    /// <summary>
    ///   <see cref="RecordType">Record type</see> that is covered by this record
    /// </summary>
    public RecordType TypeCovered { get; private set; }

    /// <summary>
    ///   <see cref="DnsSecAlgorithm">Algorithm</see> that is used for signature
    /// </summary>
    public DnsSecAlgorithm Algorithm { get; private set; }

    /// <summary>
    ///   Label count of original record that is covered by this record
    /// </summary>
    public byte Labels { get; private set; }

    /// <summary>
    ///   Original time to live value of original record that is covered by this record
    /// </summary>
    public int OriginalTimeToLive { get; private set; }

    /// <summary>
    ///   Signature is valid until this date
    /// </summary>
    public DateTime SignatureExpiration { get; private set; }

    /// <summary>
    ///   Signature is valid from this date
    /// </summary>
    public DateTime SignatureInception { get; private set; }

    /// <summary>
    ///   Key tag
    /// </summary>
    public ushort KeyTag { get; private set; }

    /// <summary>
    ///   Domain name of generator of the signature
    /// </summary>
    public DomainName SignersName { get; private set; }

    /// <summary>
    ///   Binary data of the signature
    /// </summary>
    public byte[] Signature { get; private set; }

    internal SigRecord() { }

    /// <summary>
    ///   Creates a new instance of the SigRecord class
    /// </summary>
    /// <param name="name"> Name of the record </param>
    /// <param name="recordClass"> Class of the record </param>
    /// <param name="timeToLive"> Seconds the record should be cached at most </param>
    /// <param name="typeCovered">
    ///   <see cref="RecordType">Record type</see> that is covered by this record
    /// </param>
    /// <param name="algorithm">
    ///   <see cref="DnsSecAlgorithm">Algorithm</see> that is used for signature
    /// </param>
    /// <param name="labels"> Label count of original record that is covered by this record </param>
    /// <param name="originalTimeToLive"> Original time to live value of original record that is covered by this record </param>
    /// <param name="signatureExpiration"> Signature is valid until this date </param>
    /// <param name="signatureInception"> Signature is valid from this date </param>
    /// <param name="keyTag"> Key tag </param>
    /// <param name="signersName"> Domain name of generator of the signature </param>
    /// <param name="signature"> Binary data of the signature </param>
    public SigRecord(DomainName name, RecordClass recordClass, int timeToLive, RecordType typeCovered, DnsSecAlgorithm algorithm, byte labels, int originalTimeToLive, DateTime signatureExpiration, DateTime signatureInception, ushort keyTag, DomainName signersName, byte[] signature)
        : base(name, RecordType.Sig, recordClass, timeToLive)
    {
        TypeCovered = typeCovered;
        Algorithm = algorithm;
        Labels = labels;
        OriginalTimeToLive = originalTimeToLive;
        SignatureExpiration = signatureExpiration;
        SignatureInception = signatureInception;
        KeyTag = keyTag;
        SignersName = signersName ?? DomainName.Root;
        Signature = signature ?? new byte[] { };
    }

    internal override void ParseRecordData(ReadOnlySpan<byte> resultData, int startPosition, int length)
    {
        int currentPosition = startPosition;

        TypeCovered = (RecordType)DnsMessageBase.ParseUShort(resultData, ref currentPosition);
        Algorithm = (DnsSecAlgorithm)resultData[currentPosition++];
        Labels = resultData[currentPosition++];
        OriginalTimeToLive = DnsMessageBase.ParseInt(resultData, ref currentPosition);
        SignatureExpiration = ParseDateTime(resultData, ref currentPosition);
        SignatureInception = ParseDateTime(resultData, ref currentPosition);
        KeyTag = DnsMessageBase.ParseUShort(resultData, ref currentPosition);
        SignersName = DnsMessageBase.ParseDomainName(resultData, ref currentPosition);
        Signature = DnsMessageBase.ParseByteData(resultData, ref currentPosition, length + startPosition - currentPosition);
    }

    internal override void ParseRecordData(DomainName origin, string[] stringRepresentation)
    {
        if (stringRepresentation.Length < 9)
            throw new FormatException();

        TypeCovered = RecordTypeHelper.ParseShortString(stringRepresentation[0]);
        Algorithm = (DnsSecAlgorithm)Byte.Parse(stringRepresentation[1]);
        Labels = Byte.Parse(stringRepresentation[2]);
        OriginalTimeToLive = Int32.Parse(stringRepresentation[3]);
        SignatureExpiration = DateTime.ParseExact(stringRepresentation[4], "yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        SignatureInception = DateTime.ParseExact(stringRepresentation[5], "yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        KeyTag = UInt16.Parse(stringRepresentation[6]);
        SignersName = ParseDomainName(origin, stringRepresentation[7]);
        Signature = String.Join(String.Empty, stringRepresentation.Skip(8)).FromBase64String();
    }

    internal override string RecordDataToString()
    {
        return TypeCovered.ToShortString()
               + " " + (byte)Algorithm
               + " " + Labels
               + " " + OriginalTimeToLive
               + " " + SignatureExpiration.ToString("yyyyMMddHHmmss")
               + " " + SignatureInception.ToString("yyyyMMddHHmmss")
               + " " + KeyTag
               + " " + SignersName
               + " " + Signature.ToBase64String();
    }

    protected internal override int MaximumRecordDataLength => 20 + SignersName.MaximumRecordDataLength + Signature.Length;

    protected internal override void EncodeRecordData(Span<byte> messageData, int offset, ref int currentPosition, Dictionary<DomainName, ushort> domainNames, bool useCanonical)
    {
        DnsMessageBase.EncodeUShort(messageData, ref currentPosition, (ushort)TypeCovered);
        messageData[currentPosition++] = (byte)Algorithm;
        messageData[currentPosition++] = Labels;
        DnsMessageBase.EncodeInt(messageData, ref currentPosition, OriginalTimeToLive);
        EncodeDateTime(messageData, ref currentPosition, SignatureExpiration);
        EncodeDateTime(messageData, ref currentPosition, SignatureInception);
        DnsMessageBase.EncodeUShort(messageData, ref currentPosition, KeyTag);
        DnsMessageBase.EncodeDomainName(messageData, offset, ref currentPosition, SignersName, null, useCanonical);
        DnsMessageBase.EncodeByteArray(messageData, ref currentPosition, Signature);
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


#endif
