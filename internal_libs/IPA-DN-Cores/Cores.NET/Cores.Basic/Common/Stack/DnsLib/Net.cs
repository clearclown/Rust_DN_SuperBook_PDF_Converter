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
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

namespace IPA.Cores.Basic.DnsLib;

/// <summary>
///   Provides a stream used for client-server communication that uses SSL/TLS and DANE/TLSA validation to authenticate
///   the server.
/// </summary>
public class DaneStream : AuthenticatedStream
{
    private readonly IDnsSecResolver _resolver;
    private readonly bool _enforceTlsaValidation;
    private readonly SslStream _sslStream;

    private DnsSecResult<TlsaRecord> _tlsaRecords;

    /// <summary>
    ///   Creates a new instance of the TlsaStream class
    /// </summary>
    /// <param name="innerStream">The underlying stream on which the encrypted stream should work</param>
    /// <param name="resolver">A DNSSEC resolver to get the TLSA records</param>
    /// <param name="enforceTlsaValidation">If true, the use of TLSA records is enforced</param>
    /// <param name="leaveInnerStreamOpen">If true, the underlying stream will not be closed when this instance is closed</param>
    /// <param name="userCertificateSelectionCallback">
    ///   A callback to select client certificates to authenticate the client to
    ///   the server
    /// </param>
    public DaneStream(Stream innerStream, IDnsSecResolver resolver, bool enforceTlsaValidation = false, bool leaveInnerStreamOpen = false, LocalCertificateSelectionCallback userCertificateSelectionCallback = null)
        : base(innerStream, leaveInnerStreamOpen)
    {
        _resolver = resolver;
        _enforceTlsaValidation = enforceTlsaValidation;
        _sslStream = new SslStream(innerStream, leaveInnerStreamOpen, ValidateRemoteCertificate, userCertificateSelectionCallback);
    }

    private bool ValidateRemoteCertificate(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
    {
        IsAuthenticatedByDane = false;

        switch (_tlsaRecords.ValidationResult)
        {
            case DnsSecValidationResult.Signed:
                if (_tlsaRecords.Records.Count == 0)
                    return !_enforceTlsaValidation && (sslPolicyErrors == SslPolicyErrors.None);

                foreach (var tlsaRecord in _tlsaRecords.Records)
                {
                    if (ValidateCertificateByTlsa(tlsaRecord, certificate, chain, sslPolicyErrors))
                    {
                        IsAuthenticatedByDane = true;
                        return true;
                    }
                }

                return false;

            case DnsSecValidationResult.Bogus:
                return false;

            default:
                return !_enforceTlsaValidation && (sslPolicyErrors == SslPolicyErrors.None);
        }
    }

    private bool ValidateCertificateByTlsa(TlsaRecord tlsaRecord, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
    {
        switch (tlsaRecord.CertificateUsage)
        {
            case TlsaRecord.TlsaCertificateUsage.PkixTA:
                return chain.ChainElements.Cast<X509ChainElement>().Any(x => ValidateCertificateByTlsa(tlsaRecord, x.Certificate)) && (sslPolicyErrors == SslPolicyErrors.None);

            case TlsaRecord.TlsaCertificateUsage.PkixEE:
                return ValidateCertificateByTlsa(tlsaRecord, certificate) && (sslPolicyErrors == SslPolicyErrors.None);

            case TlsaRecord.TlsaCertificateUsage.DaneTA:
                return chain.ChainElements.Cast<X509ChainElement>().Any(x => ValidateCertificateByTlsa(tlsaRecord, x.Certificate)) && ((sslPolicyErrors | SslPolicyErrors.RemoteCertificateChainErrors) == SslPolicyErrors.RemoteCertificateChainErrors);

            case TlsaRecord.TlsaCertificateUsage.DaneEE:
                return ValidateCertificateByTlsa(tlsaRecord, certificate) && ((sslPolicyErrors | SslPolicyErrors.RemoteCertificateChainErrors) == SslPolicyErrors.RemoteCertificateChainErrors);

            default:
                throw new NotSupportedException();
        }
    }

    private bool ValidateCertificateByTlsa(TlsaRecord tlsaRecord, X509Certificate certificate)
    {
        return TlsaRecord.GetCertificateAssocicationData(tlsaRecord.Selector, tlsaRecord.MatchingType, certificate).SequenceEqual(tlsaRecord.CertificateAssociationData);
    }

    /// <summary>
    ///   Closes the current stream and releases any resources.
    /// </summary>
    public override void Close()
    {
        _sslStream.Close();
    }

    /// <summary>
    ///   Called by clients to authenticate the server and optionally the client in a client-server connection.
    /// </summary>
    /// <param name="targetHost">The name of the server</param>
    /// <param name="port">The port of the server</param>
    /// <param name="protocol">The protocol used to communicate with the server</param>
    /// <param name="clientCertificates">The X509CertificateCollection that contains client certificates.</param>
    /// <param name="enabledSslProtocols">The SslProtocols value that represents the protocol used for authentication.</param>
    /// <param name="checkCertificateRevocation">
    ///   A Boolean value that specifies whether the certificate revocation list is
    ///   checked during authentication.
    /// </param>
    public void AuthenticateAsClient(string targetHost, int port, ProtocolType protocol = ProtocolType.Tcp, X509CertificateCollection clientCertificates = null, SslProtocols enabledSslProtocols = SslProtocols.None, bool checkCertificateRevocation = false)
    {
        if (enabledSslProtocols == SslProtocols.None) enabledSslProtocols = IPA.Cores.Basic.CoresConfig.SslSettings.DefaultSslProtocolVersionsAsClient;
        _tlsaRecords = _resolver.ResolveSecure<TlsaRecord>(DomainName.Parse("_" + port + "._" + EnumHelper<ProtocolType>.ToString(protocol).ToLowerInvariant() + "." + targetHost), RecordType.Tlsa);
        _sslStream.AuthenticateAsClient(targetHost, clientCertificates ?? new X509CertificateCollection(), enabledSslProtocols, checkCertificateRevocation);
    }

    /// <summary>
    ///   Called by clients to authenticate the server and optionally the client in a client-server connection.
    /// </summary>
    /// <param name="targetHost">The name of the server</param>
    /// <param name="port">The port of the server</param>
    /// <param name="protocol">The protocol used to communicate with the server</param>
    /// <param name="clientCertificates">The X509CertificateCollection that contains client certificates.</param>
    /// <param name="enabledSslProtocols">The SslProtocols value that represents the protocol used for authentication.</param>
    /// <param name="checkCertificateRevocation">
    ///   A Boolean value that specifies whether the certificate revocation list is
    ///   checked during authentication.
    /// </param>
    public async Task AuthenticateAsClientAsync(string targetHost, int port, ProtocolType protocol = ProtocolType.Tcp, X509CertificateCollection clientCertificates = null, SslProtocols enabledSslProtocols = SslProtocols.None, bool checkCertificateRevocation = false)
    {
        if (enabledSslProtocols == SslProtocols.None) enabledSslProtocols = IPA.Cores.Basic.CoresConfig.SslSettings.DefaultSslProtocolVersionsAsClient;
        _tlsaRecords = await _resolver.ResolveSecureAsync<TlsaRecord>(DomainName.Parse("_" + port + "._" + EnumHelper<ProtocolType>.ToString(protocol).ToLowerInvariant() + "." + targetHost), RecordType.Tlsa);
        await _sslStream.AuthenticateAsClientAsync(targetHost, clientCertificates ?? new X509CertificateCollection(), enabledSslProtocols, checkCertificateRevocation);
    }

    /// <summary>
    ///   Sets the length of the underlying stream.
    /// </summary>
    /// <param name="value">The new length</param>
    public override void SetLength(long value)
    {
        _sslStream.SetLength(value);
    }

    /// <summary>
    ///   Infrastructure. Throws a NotSupportedException.
    /// </summary>
    /// <param name="offset"></param>
    /// <param name="origin"></param>
    /// <returns></returns>
    public override long Seek(long offset, SeekOrigin origin)
    {
        return _sslStream.Seek(offset, origin);
    }

    /// <summary>
    ///   Causes any buffered data to be written to the underlying device.
    /// </summary>
    public override void Flush()
    {
        _sslStream.Flush();
    }

    /// <summary>
    ///   Reads data from this stream and stores it in the specified array.
    /// </summary>
    /// <param name="buffer">A Byte array that receives the bytes read from this stream.</param>
    /// <param name="offset">
    ///   A Int32 that contains the zero-based location in buffer at which to begin storing the data read
    ///   from this stream.
    /// </param>
    /// <param name="count">A Int32 that contains the maximum number of bytes to read from this stream.</param>
    /// <returns>A Int32 value that specifies the number of bytes read. When there is no more data to be read, returns 0.</returns>
    public override int Read(byte[] buffer, int offset, int count)
    {
        return _sslStream.Read(buffer, offset, count);
    }

    /// <summary>
    ///   Writes data to this stream.
    /// </summary>
    /// <param name="buffer">A Byte array that supplies the bytes written to the stream.</param>
    /// <param name="offset">
    ///   A Int32 that contains the zero-based location in buffer at which to begin reading bytes to be
    ///   written to the stream.
    /// </param>
    /// <param name="count">A Int32 that contains the number of bytes to read from buffer.</param>
    public override void Write(byte[] buffer, int offset, int count)
    {
        _sslStream.Write(buffer, offset, count);
    }

    /// <summary>
    ///   Begins an asynchronous read operation that reads data from the stream and stores it in the specified array.
    /// </summary>
    /// <param name="buffer">A Byte array that receives the bytes read from the stream.</param>
    /// <param name="offset">The zero-based location in buffer at which to begin storing the data read from this stream.</param>
    /// <param name="count">The maximum number of bytes to read from the stream.</param>
    /// <param name="asyncCallback">
    ///   An AsyncCallback delegate that references the method to invoke when the read operation is
    ///   complete.
    /// </param>
    /// <param name="asyncState">
    ///   A user-defined object that contains information about the read operation. This object is
    ///   passed to the asyncCallback delegate when the operation completes.
    /// </param>
    /// <returns>An IAsyncResult object that indicates the status of the asynchronous operation.</returns>
    public override IAsyncResult BeginRead(byte[] buffer, int offset, int count, AsyncCallback asyncCallback, object asyncState)
    {
        return _sslStream.BeginRead(buffer, offset, count, asyncCallback, asyncState);
    }

    /// <summary>
    ///   Ends an asynchronous read operation started with a previous call to BeginRead.
    /// </summary>
    /// <param name="asyncResult">An IAsyncResult instance returned by a call to BeginRead</param>
    /// <returns>A Int32 value that specifies the number of bytes read from the underlying stream.</returns>
    public override int EndRead(IAsyncResult asyncResult)
    {
        return _sslStream.EndRead(asyncResult);
    }

    /// <summary>
    ///   Begins an asynchronous write operation that writes Bytes from the specified buffer to the stream.
    /// </summary>
    /// <param name="buffer">A Byte array that supplies the bytes to be written to the stream.</param>
    /// <param name="offset">The zero-based location in buffer at which to begin reading bytes to be written to the stream.</param>
    /// <param name="count">An Int32 value that specifies the number of bytes to read from buffer.</param>
    /// <param name="asyncCallback">
    ///   An AsyncCallback delegate that references the method to invoke when the write operation is
    ///   complete.
    /// </param>
    /// <param name="asyncState">
    ///   A user-defined object that contains information about the write operation. This object is
    ///   passed to the asyncCallback delegate when the operation completes.
    /// </param>
    /// <returns>An IAsyncResult object indicating the status of the asynchronous operation.</returns>
    public override IAsyncResult BeginWrite(byte[] buffer, int offset, int count, AsyncCallback asyncCallback, object asyncState)
    {
        return _sslStream.BeginWrite(buffer, offset, count, asyncCallback, asyncState);
    }

    /// <summary>
    ///   Ends an asynchronous write operation started with a previous call to BeginWrite.
    /// </summary>
    /// <param name="asyncResult">An IAsyncResult instance returned by a call to BeginWrite</param>
    public override void EndWrite(IAsyncResult asyncResult)
    {
        _sslStream.EndWrite(asyncResult);
    }

    /// <summary>
    ///   Gets the TransportContext used for authentication using extended protection.
    /// </summary>
    public TransportContext TransportContext => _sslStream.TransportContext;

    /// <summary>
    ///   Gets a Boolean value that indicates whether authentication was successful.
    /// </summary>
    public override bool IsAuthenticated => _sslStream.IsAuthenticated;

    /// <summary>
    ///   Gets a Boolean value that indicates whether authentication by TLSA/DANE was successful.
    /// </summary>
    public bool IsAuthenticatedByDane { get; private set; }

    /// <summary>
    ///   Gets a Boolean value that indicates whether both server and client have been authenticated.
    /// </summary>
    public override bool IsMutuallyAuthenticated => _sslStream.IsMutuallyAuthenticated;

    /// <summary>
    ///   Gets a Boolean value that indicates whether this SslStream uses data encryption.
    /// </summary>
    public override bool IsEncrypted => _sslStream.IsEncrypted;

    /// <summary>
    ///   Gets a Boolean value that indicates whether the data sent using this stream is signed.
    /// </summary>
    public override bool IsSigned => _sslStream.IsSigned;

    /// <summary>
    ///   Gets a Boolean value that indicates whether the local side of the connection used by this SslStream was
    ///   authenticated as the server.
    /// </summary>
    public override bool IsServer => _sslStream.IsServer;

    /// <summary>
    ///   Gets a value that indicates the security protocol used to authenticate this connection.
    /// </summary>
    public SslProtocols SslProtocol => _sslStream.SslProtocol;

    /// <summary>
    ///   Gets a Boolean value that indicates whether the certificate revocation list is checked during the certificate
    ///   validation process.
    /// </summary>
    public bool CheckCertRevocationStatus => _sslStream.CheckCertRevocationStatus;

    /// <summary>
    ///   Gets the certificate used to authenticate the local endpoint.
    /// </summary>
    public X509Certificate LocalCertificate => _sslStream.LocalCertificate;

    /// <summary>
    ///   Gets the certificate used to authenticate the remote endpoint.
    /// </summary>
    public X509Certificate RemoteCertificate => _sslStream.RemoteCertificate;

    /// <summary>
    ///   Gets a value that identifies the bulk encryption algorithm used by this SslStream.
    /// </summary>
    public CipherAlgorithmType CipherAlgorithm => _sslStream.CipherAlgorithm;

    /// <summary>
    ///   Gets a value that identifies the strength of the cipher algorithm used by this SslStream.
    /// </summary>
    public int CipherStrength => _sslStream.CipherStrength;

    /// <summary>
    ///   Gets the algorithm used for generating message authentication codes (MACs).
    /// </summary>
    public HashAlgorithmType HashAlgorithm => _sslStream.HashAlgorithm;

    /// <summary>
    ///   Gets a value that identifies the strength of the hash algorithm used by this instance.
    /// </summary>
    public int HashStrength => _sslStream.HashStrength;

    /// <summary>
    ///   Gets the key exchange algorithm used by this SslStream.
    /// </summary>
    public ExchangeAlgorithmType KeyExchangeAlgorithm => _sslStream.KeyExchangeAlgorithm;

    /// <summary>
    ///   Gets a value that identifies the strength of the key exchange algorithm used by this instance.
    /// </summary>
    public int KeyExchangeStrength => _sslStream.KeyExchangeStrength;

    /// <summary>
    ///   Gets a Boolean value that indicates whether the underlying stream is seekable.
    /// </summary>
    public override bool CanSeek => _sslStream.CanSeek;

    /// <summary>
    ///   Gets a Boolean value that indicates whether the underlying stream is readable.
    /// </summary>
    public override bool CanRead => _sslStream.CanRead;

    /// <summary>
    ///   Gets a Boolean value that indicates whether the underlying stream supports time-outs.
    /// </summary>
    public override bool CanTimeout => _sslStream.CanTimeout;

    /// <summary>
    ///   Gets a Boolean value that indicates whether the underlying stream is writable.
    /// </summary>
    public override bool CanWrite => _sslStream.CanWrite;

    /// <summary>
    ///   Gets or sets the amount of time a read operation blocks waiting for data.
    /// </summary>
    public override int ReadTimeout
    {
        get { return _sslStream.ReadTimeout; }
        set { _sslStream.ReadTimeout = value; }
    }

    /// <summary>
    ///   Gets or sets the amount of time a write operation blocks waiting for data.
    /// </summary>
    public override int WriteTimeout
    {
        get { return _sslStream.WriteTimeout; }
        set { _sslStream.WriteTimeout = value; }
    }

    /// <summary>
    ///   Gets the length of the underlying stream.
    /// </summary>
    public override long Length => _sslStream.Length;

    /// <summary>
    ///   Gets or sets the current position in the underlying stream.
    /// </summary>
    public override long Position
    {
        get { return _sslStream.Position; }
        set { _sslStream.Position = value; }
    }

    /// <summary>
    ///   Releases the unmanaged resources used by this and optionally releases the managed resources.
    /// </summary>
    /// <param name="disposing">
    ///   true to release both managed and unmanaged resources; false to release only unmanaged
    ///   resources.
    /// </param>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _sslStream.Dispose();

        base.Dispose(disposing);
    }
}


#endif
