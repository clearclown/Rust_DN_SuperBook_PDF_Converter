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

using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.Security;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;
using System.Buffers;
using System.Net.NetworkInformation;
using Microsoft.Extensions.ObjectPool;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using System.Collections.Immutable;
using System.Security.Authentication;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Text;

namespace IPA.Cores.Basic
{
    public static partial class CoresConfig
    {
        public static partial class SslSettings
        {
            public static readonly Copenhagen<SslProtocols> DefaultSslProtocolVersionsAsServer = SslProtocols.Tls | SslProtocols.Tls11 | SslProtocols.Tls12 | SslProtocols.Tls13;
            public static readonly Copenhagen<SslProtocols> DefaultSslProtocolVersionsAsClient = SslProtocols.Tls | SslProtocols.Tls11 | SslProtocols.Tls12 | SslProtocols.Tls13;

            public static readonly Copenhagen<EncryptionPolicy> DefaultSslEncryptionPolicyServer = EncryptionPolicy.RequireEncryption;
            public static readonly Copenhagen<EncryptionPolicy> DefaultSslEncryptionPolicyClient = EncryptionPolicy.RequireEncryption;

            [Obsolete]
            public static SslProtocols DefaultSslProtocolVersions => DefaultSslProtocolVersionsAsServer;

            public static readonly Copenhagen<int> DefaultNegotiationRecvTimeout = 15 * 1000;

            public static readonly Copenhagen<CipherSuitesPolicy?> DefaultCipherSuitesPolicyServer = new Copenhagen<CipherSuitesPolicy?>(null);
            public static readonly Copenhagen<CipherSuitesPolicy?> DefaultCipherSuitesPolicyClient = new Copenhagen<CipherSuitesPolicy?>(null);
        }
    }

    public static class PalUnixOpenSslSpecialUtil
    {
        // このクラスは、UNIX における OpenSSL ライブラリを使用する場合の設定を初期化するためのものである。
        // 
        // UNIX 版 .NET 5 / 6 で使用されている OpenSSL コードでは
        // 標準で /etc/ssl/openssl.cnf ファイルが読み込まれる。
        // そこでは、大抵の環境において SECLEVEL の値が設定されていないので、
        // OpenSSL のバージョンによって厳しいセキュリティレベル (デフォルト値) が設定されてしまっており、
        // 互換性の高い暗号アルゴリズムや 1024bit RSA 証明書等に関する SSL ネゴシエーションに失敗してしまう
        // 問題がある。
        // 
        // [ default_conf ]
        // ssl_conf = ssl_sect
        // [ssl_sect]
        // system_default = system_default_sect
        // [system_default_sect]
        // MinProtocol = TLSv1
        // CipherString = DEFAULT:@SECLEVEL=1
        // 
        // そこで、SECLEVEL=0 を設定する必要があるが、
        // https://github.com/dotnet/runtime/blob/292632fd04671aed0b302195f05834a01db474fa/src/libraries/Native/Unix/System.Security.Cryptography.Native/pal_ssl.c#L485
        // にあるように、SECLEVEL=0 が SSL_CTX に設定されるのは
        // EncryptionPolicy が AllowNoEncryption の場合のみである。
        // 
        // EncryptionPolicy を AllowNoEncryption に設定した場合、デフォルトで危険なアルゴリズム (eNULL など) が有効になってしまう。
        // そこで、EncryptionPolicy を AllowNoEncryption に設定した場合、危険なアルゴリズム (eNULL など) を手動で除外し
        // それ以外の暗号アルゴリズムを手動で追加する処理が必要である。
        // 
        // このような初期化処理を行なうコードが PalUnixOpenSslSpecialUtil クラスである。

        static Once OnceFlag = new Once();

        public static void TryInit()
        {
            if (EnvFastOsInfo.IsWindows) return;

            try
            {
                Init();
            }
            catch
            {
            }
        }

        public static void Init()
        {
            if (OnceFlag.IsFirstCall())
            {
                var pol = CreateUnixOpenSslCipherSuitesPolicyInternal();

                CoresConfig.SslSettings.DefaultCipherSuitesPolicyServer.Set(pol);
                CoresConfig.SslSettings.DefaultCipherSuitesPolicyClient.Set(pol);

                CoresConfig.SslSettings.DefaultSslEncryptionPolicyServer.Set(EncryptionPolicy.AllowNoEncryption);
                CoresConfig.SslSettings.DefaultSslEncryptionPolicyClient.Set(EncryptionPolicy.AllowNoEncryption);
            }
        }

        // NULL 以外のまともな SSL 暗号化アルゴリズムを追加する
        static CipherSuitesPolicy CreateUnixOpenSslCipherSuitesPolicyInternal()
        {
            List<TlsCipherSuite> list = new List<TlsCipherSuite>();
            var definedValues = TlsCipherSuite.TLS_NULL_WITH_NULL_NULL.GetEnumValuesList();
            foreach (var value in definedValues)
            {
                var name = value.EnumToStrExact();
                if (name._IsFilled())
                {
                    string[] tokens = name._Split(StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries, "_");
                    if (tokens.Contains("NULL", StrCmpi) == false)
                    {
                        // NULL 暗号であると思われるアルゴリズムを禁止する
                        list.Add(value);
                    }
                }
            }
            // 暗号強度の強いと思われる順に並び替える
            list.Reverse();
            return new CipherSuitesPolicy(list);
        }
    }

    public partial class PalX509Certificate
    {
        public X509Certificate NativeCertificate { get; }
        public X509Certificate2 NativeCertificate2 => (X509Certificate2)NativeCertificate;

        public override string ToString() => this.ToString(false);

        public string ToString(bool details) => this.NativeCertificate.ToString(details);

        public string HashSHA1 => this.NativeCertificate.GetCertHashString();
        public string HashSHA256 => this.NativeCertificate.GetCertHashString(System.Security.Cryptography.HashAlgorithmName.SHA256);
        public string HashSHA384 => this.NativeCertificate.GetCertHashString(System.Security.Cryptography.HashAlgorithmName.SHA384);
        public string HashSHA512 => this.NativeCertificate.GetCertHashString(System.Security.Cryptography.HashAlgorithmName.SHA512);

        public IReadOnlyList<string> GetSHAHashStrList() => this.NativeCertificate._GetCertSHAHashStrList();

        public PalX509Certificate(X509Certificate nativeCertificate)
        {
            if (nativeCertificate == null) throw new ArgumentNullException("nativeCertificate");

            NativeCertificate = nativeCertificate;

            InitFields();
        }

        public PalX509Certificate(ReadOnlySpan<byte> pkcs12Data, string? password = null)
        {
            NativeCertificate = Secure.LoadPkcs12(pkcs12Data.ToArray(), password);

            InitFields();
        }

        public PalX509Certificate(FilePath filePath, string? password = null)
            : this(filePath.EasyAccess.Binary.Span, password) { }

        public void InitFields()
        {
            InitPkiFields();
        }

        partial void InitPkiFields();

        public ReadOnlyMemory<byte> ExportCertificate()
        {
            var ret = this.NativeCertificate.GetRawCertData();
            return ret;
        }

        public ReadOnlyMemory<byte> ExportCertificateAndKeyAsP12(string? password = null)
        {
            password = password._NonNull();

            ReadOnlyMemory<byte> ret = this.NativeCertificate.Export(X509ContentType.Pfx, password);

            return ret;
        }

        public bool VerifySignedByCertificate(PalX509Certificate issuerCertificate)
        {
            return this.PkiCertificate.VerifySignedByCertificate(issuerCertificate.PkiCertificate);
        }

        public static implicit operator X509Certificate2(PalX509Certificate cert) => cert.NativeCertificate2;
        public static implicit operator X509Certificate(PalX509Certificate cert) => cert.NativeCertificate;
    }

    public struct PalSocketReceiveFromResult
    {
        public int ReceivedBytes;
        public EndPoint RemoteEndPoint;
        public EndPoint? LocalEndPoint;
    }

    public class PalSocket : IDisposable
    {
        public static bool OSSupportsIPv4 { get => Socket.OSSupportsIPv4; }
        public static bool OSSupportsIPv6 { get => Socket.OSSupportsIPv6; }

        readonly Socket _Socket;

        public AddressFamily AddressFamily { get; }
        public SocketType SocketType { get; }
        public ProtocolType ProtocolType { get; }

        readonly CriticalSection LockObj = new CriticalSection<PalSocket>();

        public CachedProperty<bool> NoDelay { get; }
        public CachedProperty<int> LingerTime { get; }
        public CachedProperty<int> SendBufferSize { get; }
        public CachedProperty<int> ReceiveBufferSize { get; }
        public long NativeHandle { get; }
        public Socket NativeSocket => _Socket;

        public CachedProperty<EndPoint> LocalEndPoint { get; }
        public CachedProperty<EndPoint> RemoteEndPoint { get; }

        public IPEndPoint LocalIPEndPoint => (IPEndPoint)LocalEndPoint;
        public IPEndPoint RemoteIPEndPoint => (IPEndPoint)RemoteEndPoint;

        IPEndPoint? UdpLocalEndPointCache = null;

        public TcpDirectionType Direction { get; }

        IHolder Leak;

        public PalSocket(Socket s, TcpDirectionType direction)
        {
            _Socket = s;

            AddressFamily = _Socket.AddressFamily;
            SocketType = _Socket.SocketType;
            ProtocolType = _Socket.ProtocolType;

            Direction = direction;

            NoDelay = new CachedProperty<bool>(value => _Socket.NoDelay = value, () => _Socket.NoDelay);
            LingerTime = new CachedProperty<int>(value =>
            {
                if (value <= 0) value = 0;
                if (value == 0)
                    _Socket.LingerState = new LingerOption(false, 0);
                else
                    _Socket.LingerState = new LingerOption(true, value);

                try
                {
                    if (value == 0)
                        _Socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.DontLinger, true);
                    else
                        _Socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.DontLinger, false);
                }
                catch { }

                return value;
            }, () =>
            {
                var lingerOption = _Socket.LingerState;
                if (lingerOption == null || lingerOption.Enabled == false)
                    return 0;
                else
                    return lingerOption.LingerTime;
            });
            SendBufferSize = new CachedProperty<int>(value => _Socket.SendBufferSize = value, () => _Socket.SendBufferSize);
            ReceiveBufferSize = new CachedProperty<int>(value => _Socket.ReceiveBufferSize = value, () => _Socket.ReceiveBufferSize);
            LocalEndPoint = new CachedProperty<EndPoint>(null, () => _Socket.LocalEndPoint!);
            RemoteEndPoint = new CachedProperty<EndPoint>(null, () => _Socket.RemoteEndPoint!);

            NativeHandle = _Socket.Handle.ToInt64();

            Leak = LeakChecker.Enter(LeakCounterKind.PalSocket);
        }

        public PalSocket(AddressFamily addressFamily, SocketType socketType, ProtocolType protocolType, TcpDirectionType direction)
            : this(new Socket(addressFamily, socketType, protocolType), direction) { }

        public void TrySetRecommendedSettings()
        {
            this.LingerTime.TrySet(0);
            this.NoDelay.TrySet(true);
        }

        public Task ConnectAsync(IPAddress address, int port) => ConnectAsync(new IPEndPoint(address, port));

        public async Task ConnectAsync(EndPoint remoteEP)
        {
            this.TrySetRecommendedSettings();

            await _Socket.ConnectAsync(remoteEP);

            this.LocalEndPoint.Flush();
            this.RemoteEndPoint.Flush();

            // 接続直後にここで Socket から EndPoint を取得しておく。そうしないと Linux 系で .NET 5 系でおかしくなる (null になる)
            this.LocalEndPoint.Get();
            this.RemoteEndPoint.Get();
        }

        public void Connect(EndPoint remoteEP) => _Socket.Connect(remoteEP);

        public void Connect(IPAddress address, int port) => _Socket.Connect(address, port);

        public void Bind(EndPoint localEP, bool udpReuse = false)
        {
            this.TrySetRecommendedSettings();
            if (this.SocketType == SocketType.Stream)
            {
                // TCP
                _Socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ExclusiveAddressUse, true);
            }
            else
            {
                // UDP
                bool isRandomPortMode = false;

                if (localEP is IPEndPoint ipEp)
                {
                    if (ipEp.Port == 0)
                    {
                        isRandomPortMode = true;
                    }
                }

                if (isRandomPortMode == false)
                {
                    _Socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ExclusiveAddressUse, false);
                    _Socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, udpReuse);
                }
            }
            _Socket.Bind(localEP);
            this.LocalEndPoint.Flush();
            this.RemoteEndPoint.Flush();

            this.UdpLocalEndPointCache = (IPEndPoint)this.LocalEndPoint;
        }

        public static bool IsUdpReuseSupported()
        {
            // 2021/6/14 時点で Linux でのみサポート。Windows では Reuse Bind() 自体は成功するが、実際には 1 つのソケットでしか受信されない。
            return Env.IsLinux;
        }

        public void Listen(int backlog = int.MaxValue)
        {
            _Socket.Listen(backlog);
            this.LocalEndPoint.Flush();
            this.RemoteEndPoint.Flush();
        }

        public async Task<PalSocket> AcceptAsync()
        {
            Socket newSocket = await _Socket.AcceptAsync();
            PalSocket s = new PalSocket(newSocket, TcpDirectionType.Server);

            // 接続直後にここで Socket から EndPoint を取得しておく。そうしないと Linux 系で .NET 5 系でおかしくなる (null になる)
            s.LocalEndPoint.Get();
            s.RemoteEndPoint.Get();

            s.TrySetRecommendedSettings();

            return s;
        }

        public Task<int> SendAsync(IEnumerable<ReadOnlyMemory<byte>> buffers)
        {
            List<ArraySegment<byte>> sendArraySegmentsList = new List<ArraySegment<byte>>();
            foreach (ReadOnlyMemory<byte> mem in buffers)
                sendArraySegmentsList.Add(mem._AsSegment());

            return _Socket.SendAsync(sendArraySegmentsList, SocketFlags.None);
        }

        public async Task<int> SendAsync(ReadOnlyMemory<byte> buffer)
        {
            return await _Socket.SendAsync(buffer, SocketFlags.None);
        }

        public async Task<int> ReceiveAsync(Memory<byte> buffer)
        {
            return await _Socket.ReceiveAsync(buffer, SocketFlags.None);
        }

        public async Task<int> SendToAsync(Memory<byte> buffer, EndPoint remoteEP)
        {
            try
            {
                //Task<int> t = _Socket.SendToAsync(buffer._AsSegment(), SocketFlags.None, remoteEP);
                int ret = await _Socket.SendToAsync(remoteEP, buffer);
                if (ret <= 0) throw new SocketDisconnectedException();
                return ret;
            }
            catch (SocketException e) when (CanUdpSocketErrorBeIgnored(e))
            {
                return buffer.Length;
            }
        }

        static readonly IPEndPoint StaticUdpEndPointIPv4 = new IPEndPoint(IPAddress.Any, 0);
        static readonly IPEndPoint StaticUdpEndPointIPv6 = new IPEndPoint(IPAddress.IPv6Any, 0);
        const int UdpMaxRetryOnIgnoreError = 1000;

        public async Task<PalSocketReceiveFromResult> ReceiveFromAsync(Memory<byte> buffer)
        {
            int numRetry = 0;

            //var bufferSegment = buffer._AsSegment();

            LABEL_RETRY:

            try
            {
                //Task<SocketReceiveFromResult> t = _Socket.ReceiveFromAsync(bufferSegment, SocketFlags.None,
                //    this.AddressFamily == AddressFamily.InterNetworkV6 ? StaticUdpEndPointIPv6 : StaticUdpEndPointIPv4);
                ValueTask<SocketReceiveFromResult> t = _Socket.ReceiveFromAsync(buffer, this.AddressFamily);
                if (t.IsCompleted == false)
                {
                    numRetry = 0;
                    await t;
                }
                SocketReceiveFromResult ret = t._GetResult();
                if (ret.ReceivedBytes <= 0) throw new SocketDisconnectedException();
                return new PalSocketReceiveFromResult()
                {
                    ReceivedBytes = ret.ReceivedBytes,
                    RemoteEndPoint = ret.RemoteEndPoint,
                    LocalEndPoint = this.UdpLocalEndPointCache,
                };
            }
            catch (SocketException e) when (CanUdpSocketErrorBeIgnored(e) || _Socket.Available >= 1)
            {
                numRetry++;
                if (numRetry >= UdpMaxRetryOnIgnoreError)
                {
                    throw;
                }
                if (numRetry >= 1 && (numRetry % 100) == 0)
                {
                    await Task.Yield();
                }
                goto LABEL_RETRY;
            }
        }

        Once DisposeFlag;
        public void Dispose() { this.Dispose(true); GC.SuppressFinalize(this); }
        protected virtual void Dispose(bool disposing)
        {
            if (disposing && DisposeFlag.IsFirstCall())
            {
                IDisposable[] disposeList;
                lock (DisposeOnDisposeList)
                {
                    disposeList = DisposeOnDisposeList.ToArray();
                    DisposeOnDisposeList.Clear();
                }
                disposeList._DoForEach(x => x._DisposeSafe());

                try
                {
                    _Socket.Shutdown(SocketShutdown.Both);
                }
                catch { }
                _Socket._DisposeSafe();

                Leak._DisposeSafe();
            }
        }

        readonly List<IDisposable> DisposeOnDisposeList = new List<IDisposable>();

        public void AddDisposeOnDispose(IDisposable? dispose)
        {
            if (dispose == null) return;

            bool disposeNow = false;

            lock (DisposeOnDisposeList)
            {
                if (DisposeFlag.IsSet)
                {
                    disposeNow = true;
                }
                else
                {
                    DisposeOnDisposeList.Add(dispose);
                }
            }

            if (disposeNow)
            {
                dispose._DisposeSafe();
            }
        }

        public static bool CanUdpSocketErrorBeIgnored(SocketException e)
        {
            switch (e.SocketErrorCode)
            {
                case SocketError.ConnectionReset:
                case SocketError.NetworkReset:
                case SocketError.MessageSize:
                case SocketError.HostUnreachable:
                case SocketError.NetworkUnreachable:
                case SocketError.NoBufferSpaceAvailable:
                case SocketError.AddressNotAvailable:
                case SocketError.ConnectionRefused:
                case SocketError.Interrupted:
                case SocketError.WouldBlock:
                case SocketError.TryAgain:
                case SocketError.InProgress:
                case SocketError.InvalidArgument:
                case (SocketError)12: // ENOMEM
                case (SocketError)13: // EACCES: brodcast packet etc
                case (SocketError)10068: // WSAEUSERS
                    return true;
            }
            return false;
        }

        public static bool IsSocketErrorDisconnected(SocketException e)
        {
            try
            {
                switch (e.SocketErrorCode)
                {
                    case SocketError.ConnectionReset:
                    case SocketError.Disconnecting:
                        return true;
                }
            }
            catch { }

            return false;
        }

        public static bool CheckIsTcpPortListenable(int port)
        {
            try
            {
                using (PalSocket sock = new PalSocket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp, TcpDirectionType.Server))
                {
                    sock.Bind(new IPEndPoint(IPAddress.Any, port));

                    sock.Listen();

                    return true;
                }
            }
            catch
            {
                return false;
            }
        }
    }

    public class PalStream : StreamImplBase
    {
        protected Stream NativeStream;
        protected NetworkStream? NativeNetworkStream;

        public bool IsNetworkStream => (NativeNetworkStream != null);

        public PalStream(Stream nativeStream)
        {
            NativeStream = nativeStream;

            NativeNetworkStream = NativeStream as NetworkStream;
        }

        protected override ValueTask<int> ReadImplAsync(Memory<byte> buffer, CancellationToken cancel = default)
            => NativeStream.ReadAsync(buffer, cancel);

        protected override ValueTask WriteImplAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancel = default)
            => NativeStream.WriteAsync(buffer, cancel);

        Once DisposeFlag;

        public override int ReadTimeout { get => NativeStream.ReadTimeout; set => NativeStream.ReadTimeout = value; }
        public override int WriteTimeout { get => NativeStream.WriteTimeout; set => NativeStream.WriteTimeout = value; }
        public override bool DataAvailable => NativeNetworkStream?.DataAvailable ?? true;

        protected override void Dispose(bool disposing)
        {
            try
            {
                if (!disposing || DisposeFlag.IsFirstCall() == false) return;
                NativeStream._DisposeSafe();
            }
            finally { base.Dispose(disposing); }
        }

        protected override Task FlushImplAsync(CancellationToken cancel = default) => NativeStream.FlushAsync(cancel);

        protected override long GetLengthImpl() => throw new NotImplementedException();
        protected override void SetLengthImpl(long length) => throw new NotImplementedException();
        protected override long GetPositionImpl() => throw new NotImplementedException();
        protected override void SetPositionImpl(long position) => throw new NotImplementedException();
        protected override long SeekImpl(long offset, SeekOrigin origin) => throw new NotImplementedException();
    }

    public delegate bool PalSslValidateRemoteCertificateCallback(PalX509Certificate cert);

    public delegate PalX509Certificate PalSslCertificateSelectionCallback(object param, string sniHostName);

    public class PalSslClientAuthenticationOptions : ICloneable
    {
        public PalSslClientAuthenticationOptions() { }

        public PalSslClientAuthenticationOptions(bool allowAnyServerCert, PalSslValidateRemoteCertificateCallback? validateRemoteCertificateProc = null, params string[] serverCertSHA1List)
            : this(null, allowAnyServerCert, validateRemoteCertificateProc, serverCertSHA1List) { }

        public PalSslClientAuthenticationOptions(string? targetHost, bool allowAnyServerCert, PalSslValidateRemoteCertificateCallback? validateRemoteCertificateProc = null, params string[] serverCertSHAList)
        {
            this.TargetHost = targetHost;
            this.AllowAnyServerCert = allowAnyServerCert;
            this.ValidateRemoteCertificateProc = validateRemoteCertificateProc;
            this.ServerCertSHAList = serverCertSHAList;
        }

        public string? TargetHost { get; set; }
        public PalSslValidateRemoteCertificateCallback? ValidateRemoteCertificateProc { get; set; }
        public string[] ServerCertSHAList { get; set; } = new string[0];
        public bool AllowAnyServerCert { get; set; } = false;
        public SslProtocols SslProtocols { get; set; } = default;

        public readonly Copenhagen<int> NegotiationRecvTimeout = CoresConfig.SslSettings.DefaultNegotiationRecvTimeout.Value;

        public PalX509Certificate? ClientCertificate { get; set; }

        public SslClientAuthenticationOptions GetNativeOptions()
        {
            SslClientAuthenticationOptions ret = new SslClientAuthenticationOptions()
            {
                TargetHost = TargetHost._NonNull(),
                AllowRenegotiation = true,
                RemoteCertificateValidationCallback = new RemoteCertificateValidationCallback((sender, cert, chain, err) =>
                {
                    IReadOnlyList<string> certHashList = cert!._GetCertSHAHashStrList();

                    bool b1 = (ValidateRemoteCertificateProc != null ? ValidateRemoteCertificateProc(new PalX509Certificate(cert!)) : false);
                    bool b2 = ServerCertSHAList?.Select(sha => sha._ReplaceStr(":", "")).Where(sha => certHashList.Where(certSha => (IgnoreCaseTrim)certSha == sha).Any()).Any() ?? false;
                    bool b3 = this.AllowAnyServerCert;

                    return b1 || b2 || b3;
                }),
                EncryptionPolicy = CoresConfig.SslSettings.DefaultSslEncryptionPolicyClient,
                CipherSuitesPolicy = CoresConfig.SslSettings.DefaultCipherSuitesPolicyClient,
                CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
                EnabledSslProtocols = (SslProtocols == default ? CoresConfig.SslSettings.DefaultSslProtocolVersionsAsClient : SslProtocols),
            };

            if (this.ClientCertificate != null)
                ret.ClientCertificates!.Add(this.ClientCertificate.NativeCertificate);

            return ret;
        }

        public object Clone() => this.MemberwiseClone();
    }

    public class PalSslServerAuthenticationOptions
    {
        public PalSslServerAuthenticationOptions() { }

        public PalSslServerAuthenticationOptions(PalX509Certificate serverCertificate, bool allowAnyClientCert, PalSslValidateRemoteCertificateCallback? validateRemoteCertificateProc, params string[] clientCertSHAList)
        {
            this.AllowAnyClientCert = allowAnyClientCert;
            this.ValidateRemoteCertificateProc = validateRemoteCertificateProc;
            this.ClientCertSHAList = clientCertSHAList;
            this.ServerCertificate = serverCertificate;
        }

        public PalSslValidateRemoteCertificateCallback? ValidateRemoteCertificateProc { get; set; }
        public string[] ClientCertSHAList { get; set; } = new string[0];
        public bool AllowAnyClientCert { get; set; } = true;

        public PalSslCertificateSelectionCallback? ServerCertificateSelectionProc { get; set; }
        public object? ServerCertificateSelectionProcParam { get; set; }

        public readonly Copenhagen<int> NegotiationRecvTimeout = CoresConfig.SslSettings.DefaultNegotiationRecvTimeout.Value;

        public PalX509Certificate? ServerCertificate { get; set; }

        public SslServerAuthenticationOptions GetNativeOptions()
        {
            SslServerAuthenticationOptions ret = new SslServerAuthenticationOptions()
            {
                AllowRenegotiation = true,
                RemoteCertificateValidationCallback = new RemoteCertificateValidationCallback((sender, cert, chain, err) =>
                {
                    if (cert == null) return this.AllowAnyClientCert;

                    IReadOnlyList<string> certHashList = cert._GetCertSHAHashStrList();

                    bool b1 = (ValidateRemoteCertificateProc != null ? ValidateRemoteCertificateProc(new PalX509Certificate(cert)) : false);
                    bool b2 = ClientCertSHAList?.Select(sha => sha._ReplaceStr(":", "")).Where(sha => certHashList.Where(certSha => (IgnoreCaseTrim)certSha == sha).Any()).Any() ?? false;
                    bool b3 = this.AllowAnyClientCert;

                    return b1 || b2 || b3;
                }),
                ClientCertificateRequired = !AllowAnyClientCert,
                EncryptionPolicy = CoresConfig.SslSettings.DefaultSslEncryptionPolicyServer,
                CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
                CipherSuitesPolicy = CoresConfig.SslSettings.DefaultCipherSuitesPolicyServer,
            };

            bool certExists = false;

            if (this.ServerCertificateSelectionProc != null)
            {
                object param = this.ServerCertificateSelectionProcParam._MarkNotNull();
                ret.ServerCertificateSelectionCallback = (obj, sniHostName) =>
                {
                    PalX509Certificate cert = this.ServerCertificateSelectionProc(param, sniHostName._NonNull());
                    return cert.NativeCertificate;
                };

                certExists = true;
            }

            if (this.ServerCertificate != null)
            {
                ret.ServerCertificateSelectionCallback = (obj, sniHostName) =>
                {
                    return this.ServerCertificate.NativeCertificate;
                };

                certExists = true;
            }

            if (certExists == false)
                throw new ApplicationException("CertificateSelectionProc or Certificate must be specified.");

            return ret;
        }
    }

    public class PalSslStream : PalStream
    {
        SslStream Ssl;
        public PalSslStream(Stream innerStream) : base(new SslStream(innerStream, true))
        {
            this.Ssl = (SslStream)NativeStream;
        }

        public Task AuthenticateAsClientAsync(PalSslClientAuthenticationOptions sslClientAuthenticationOptions, CancellationToken cancellationToken)
            => Ssl.AuthenticateAsClientAsync(sslClientAuthenticationOptions.GetNativeOptions(), cancellationToken);

        public Task AuthenticateAsServerAsync(PalSslServerAuthenticationOptions sslServerAuthenticationOptions, CancellationToken cancellationToken)
            => Ssl.AuthenticateAsServerAsync(sslServerAuthenticationOptions.GetNativeOptions(), cancellationToken);

        public string SslProtocol => Ssl.SslProtocol.ToString();
        public string CipherAlgorithm => Ssl.CipherAlgorithm.ToString();
        public int CipherStrength => Ssl.CipherStrength;
        public string HashAlgorithm => Ssl.HashAlgorithm.ToString();
        public int HashStrength => Ssl.HashStrength;
        public string KeyExchangeAlgorithm => Ssl.KeyExchangeAlgorithm.ToString();
        public int KeyExchangeStrength => Ssl.KeyExchangeStrength;
        public PalX509Certificate? LocalCertificate => Ssl.LocalCertificate == null ? null : new PalX509Certificate(Ssl.LocalCertificate);
        public PalX509Certificate? RemoteCertificate => Ssl.RemoteCertificate == null ? null : new PalX509Certificate(Ssl.RemoteCertificate);

        Once DisposeFlag;
        protected override void Dispose(bool disposing)
        {
            try
            {
                if (!disposing || DisposeFlag.IsFirstCall() == false) return;
                this.Ssl._DisposeSafe();
            }
            finally { base.Dispose(disposing); }
        }
    }

    public static class PalDns
    {
        public static Task<IPAddress[]> GetHostAddressesAsync(string hostNameOrAddress, int timeout = Timeout.Infinite, CancellationToken cancel = default)
            => TaskUtil.DoAsyncWithTimeout(c => Dns.GetHostAddressesAsync(hostNameOrAddress),
                timeout: timeout, cancel: cancel);

        public static Task<IPHostEntry> GetHostEntryAsync(string hostNameOrAddress, int timeout = Timeout.Infinite, CancellationToken cancel = default)
            => TaskUtil.DoAsyncWithTimeout(c => Dns.GetHostEntryAsync(hostNameOrAddress),
                timeout: timeout, cancel: cancel);

        public static Task<IPHostEntry> GetHostEntryAsync(IPAddress ip, int timeout = Timeout.Infinite, CancellationToken cancel = default)
            => TaskUtil.DoAsyncWithTimeout(c => Dns.GetHostEntryAsync(ip),
                timeout: timeout, cancel: cancel);
    }

    public class PalHostNetInfo : BackgroundStateDataBase
    {
        public override BackgroundStateDataUpdatePolicy DataUpdatePolicy =>
            new BackgroundStateDataUpdatePolicy(300, 6000, 2000);

        public string HostName;
        public string DomainName;
        public string FqdnHostName => HostName + (string.IsNullOrEmpty(DomainName) ? "" : "." + DomainName);
        public bool IsIPv4Supported;
        public bool IsIPv6Supported;
        public IReadOnlyList<IPAddress>? IPAddressList = null;

        public static bool IsUnix { get; } = (Environment.OSVersion.Platform != PlatformID.Win32NT);

        static IPAddress[] GetLocalIPAddressBySocketApi() => PalDns.GetHostAddressesAsync(Dns.GetHostName())._GetResult();

        class ByteComparer : IComparer<byte[]>
        {
            public int Compare(byte[]? x, byte[]? y) => x!.AsSpan().SequenceCompareTo(y!.AsSpan());
        }

        public static IPGlobalProperties GetHostNameAndDomainNameInfo(out string hostName, out string domainName)
        {
            IPGlobalProperties prop;

            if (CoresLib.Caps.Bit(CoresCaps.BlazorApp) == false)
            {
                prop = IPGlobalProperties.GetIPGlobalProperties();

                if (prop.DomainName._IsSamei("(none)") || prop.DomainName._IsEmpty())
                {
                    string fqdn = prop.HostName;

                    hostName = Str.GetHostNameFromFqdn(fqdn);
                    domainName = Str.GetDomainFromFqdn(fqdn);
                }
                else
                {
                    hostName = prop.HostName;
                    domainName = prop.DomainName;
                }
            }
            else
            {
                string fqdn = Consts.BlazorApp.DummyFqdn;
                hostName = Str.GetHostNameFromFqdn(fqdn);
                domainName = Str.GetDomainFromFqdn(fqdn);

                prop = null!;
            }

            hostName = hostName._NonNullTrim();
            domainName = domainName._NonNullTrim();

            hostName = hostName._TrimStartWith(".");
            hostName = hostName._TrimEndsWith(".");

            domainName = domainName._TrimStartWith(".");
            domainName = domainName._TrimEndsWith(".");

            if (hostName._IsEmpty()) hostName = "unknown-host";

            hostName = hostName.ToLowerInvariant();
            domainName = domainName.ToLowerInvariant();

            return prop;
        }


        public PalHostNetInfo()
        {
            IPGlobalProperties prop = GetHostNameAndDomainNameInfo(out this.HostName, out this.DomainName);

            HashSet<IPAddress> hash = new HashSet<IPAddress>();

            if (IsUnix)
            {
                UnicastIPAddressInformationCollection info = prop.GetUnicastAddresses();
                foreach (UnicastIPAddressInformation ip in info)
                {
                    if (ip.Address.AddressFamily == AddressFamily.InterNetwork || ip.Address.AddressFamily == AddressFamily.InterNetworkV6)
                        hash.Add(ip.Address);
                }
            }
            else
            {
                try
                {
                    IPAddress[] info = GetLocalIPAddressBySocketApi();
                    if (info.Length >= 1)
                    {
                        foreach (IPAddress ip in info)
                        {
                            if (ip.AddressFamily == AddressFamily.InterNetwork || ip.AddressFamily == AddressFamily.InterNetworkV6)
                                hash.Add(ip);
                        }
                    }
                }
                catch { }
            }

            if (PalSocket.OSSupportsIPv4)
            {
                this.IsIPv4Supported = true;
                hash.Add(IPAddress.Any);
                hash.Add(IPAddress.Loopback);
            }
            if (PalSocket.OSSupportsIPv6)
            {
                this.IsIPv6Supported = true;
                hash.Add(IPAddress.IPv6Any);
                hash.Add(IPAddress.IPv6Loopback);
            }

            try
            {
                var cmp = new ByteComparer();
                this.IPAddressList = hash.OrderBy(x => x.AddressFamily)
                    .ThenBy(x => x.GetAddressBytes(), cmp)
                    .ThenBy(x => (x.AddressFamily == AddressFamily.InterNetworkV6 ? x.ScopeId : 0))
                    .ToList();
            }
            catch { }
        }

        public Memory<byte> IPAddressListBinary
        {
            get
            {
                FastMemoryBuffer<byte> ret = new FastMemoryBuffer<byte>();
                if (IPAddressList != null)
                {
                    foreach (IPAddress addr in IPAddressList)
                    {
                        ret.WriteSInt32((int)addr.AddressFamily);
                        ret.Write(addr.GetAddressBytes());
                        if (addr.AddressFamily == AddressFamily.InterNetworkV6)
                            ret.WriteSInt64(addr.ScopeId);
                    }
                }
                return ret;
            }
        }

        public override bool Equals(BackgroundStateDataBase? otherArg)
        {
            PalHostNetInfo other = (PalHostNetInfo)otherArg!;
            if (string.Equals(this.HostName, other.HostName, StringComparison.Ordinal) == false) return false;
            if (string.Equals(this.DomainName, other.DomainName, StringComparison.Ordinal) == false) return false;
            if (this.IsIPv4Supported != other.IsIPv4Supported) return false;
            if (this.IsIPv6Supported != other.IsIPv6Supported) return false;
            if (this.IPAddressListBinary.Span.SequenceEqual(other.IPAddressListBinary.Span) == false) return false;
            return true;
        }

        Action? callMeCache = null;

        public override void RegisterSystemStateChangeNotificationCallbackOnlyOnceImpl(Action callMe)
        {
            callMeCache = callMe;

            NetworkChange.NetworkAddressChanged += NetworkChange_NetworkAddressChanged;
            NetworkChange.NetworkAvailabilityChanged += NetworkChange_NetworkAvailabilityChanged;
        }

        private void NetworkChange_NetworkAddressChanged(object? sender, EventArgs e)
        {
            if (callMeCache != null) callMeCache();

            NetworkChange.NetworkAddressChanged += NetworkChange_NetworkAddressChanged;
        }

        private void NetworkChange_NetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e)
        {
            if (callMeCache != null) callMeCache();

            NetworkChange.NetworkAvailabilityChanged += NetworkChange_NetworkAvailabilityChanged;
        }

        public static IPAddress GetLocalIPForDestinationHost(IPAddress dest)
        {
            try
            {
                using (PalSocket sock = new PalSocket(dest.AddressFamily, SocketType.Dgram, ProtocolType.IP, TcpDirectionType.Client))
                {
                    sock.Connect(dest, 65530);
                    IPEndPoint ep = (IPEndPoint)sock.LocalEndPoint.Value;
                    return ep.Address;
                }
            }
            catch { }

            using (PalSocket sock = new PalSocket(dest.AddressFamily, SocketType.Dgram, ProtocolType.Udp, TcpDirectionType.Unknown))
            {
                sock.Connect(dest, 65531);
                IPEndPoint ep = (IPEndPoint)sock.LocalEndPoint.Value;
                return ep.Address;
            }
        }

        public static async Task<IPAddress> GetLocalIPv4ForInternetAsync()
        {
            try
            {
                return GetLocalIPForDestinationHost(IPAddress.Parse("8.8.8.8"));
            }
            catch { }

            try
            {
                using (PalSocket sock = new PalSocket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp, TcpDirectionType.Client))
                {
                    var hostent = await PalDns.GetHostEntryAsync("www.msftncsi.com");
                    var addr = hostent.AddressList.Where(x => x.AddressFamily == AddressFamily.InterNetwork).First();
                    await sock.ConnectAsync(addr, 443);
                    IPEndPoint ep = (IPEndPoint)sock.LocalEndPoint.Value;
                    return ep.Address;
                }
            }
            catch { }

            try
            {
                using (PalSocket sock = new PalSocket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp, TcpDirectionType.Client))
                {
                    var hostent = await PalDns.GetHostEntryAsync("www.msftncsi.com");
                    var addr = hostent.AddressList.Where(x => x.AddressFamily == AddressFamily.InterNetwork).First();
                    await sock.ConnectAsync(addr, 80);
                    IPEndPoint ep = (IPEndPoint)sock.LocalEndPoint.Value;
                    return ep.Address;
                }
            }
            catch { }

            try
            {
                return BackgroundState<PalHostNetInfo>.Current.Data!.IPAddressList!.Where(x => x.AddressFamily == AddressFamily.InterNetwork)
                    .Where(x => IPAddress.IsLoopback(x) == false).Where(x => x != IPAddress.Any).First();
            }
            catch { }

            return IPAddress.Any;
        }

    }

    // From: https://github.com/enclave-networks/research.udp-perf/tree/e5e5afd5107cd6d64dff951e0deaa5dca26d549a
    // Copyright (c) 2021 Enclave Networks
    // MIT License

    /// <summary>
    /// Provides a derived implementation of <see cref="SocketAsyncEventArgs"/> that allows fast UDP send/receive activity.
    /// </summary>
    /// <remarks>
    /// Note that this implementation isn't perfect if you are using async context. There is more work to be done if you need
    /// to make sure async context is fully preserved, but at the expense of performance.
    /// Heavily inspired by the AwaitableSocketAsyncEventArgs class inside the dotnet runtime: 
    /// https://github.com/dotnet/runtime/blob/9bb4bfe7b84ce0e05e55059fd3ab99448176d5ac/src/libraries/System.Net.Sockets/src/System/Net/Sockets/Socket.Tasks.cs#L746
    /// </remarks>
    internal sealed class UdpAwaitableSocketAsyncEventArgs : SocketAsyncEventArgs, IValueTaskSource<int>
    {
        private static readonly Action<object?> _completedSentinel = new Action<object?>(state => throw new InvalidOperationException("Task misuse"));

        // This _token is a basic attempt to protect against misuse of the value tasks we return from this source.
        // Not perfect, intended to be 'best effort'.
        private short _token;
        private Action<object?>? _continuation;

        // Note the use of 'unsafeSuppressExecutionContextFlow'; this is an optimisation new to .NET5. We are not concerned with execution context preservation
        // in our example, so we can disable it for a slight perf boost.
        public UdpAwaitableSocketAsyncEventArgs()
            : base(unsafeSuppressExecutionContextFlow: true)
        {
        }

        public ValueTask<int> DoReceiveFromAsync(Socket socket)
        {
            // Call our socket method to do the receive.
            if (socket.ReceiveFromAsync(this))
            {
                // ReceiveFromAsync will return true if we are going to complete later.
                // So we return a ValueTask, passing 'this' (our IValueTaskSource) 
                // to the constructor. We will then tell the ValueTask when we are 'done'.
                return new ValueTask<int>(this, _token);
            }

            // If our socket method returns false, it means the call already complete synchronously; for example
            // if there is data in the socket buffer all ready to go, we'll usually complete this way.            
            return CompleteSynchronously();
        }

        public ValueTask<int> DoSendToAsync(Socket socket)
        {
            // Send looks very similar to send, just calling a different method on the socket.
            if (socket.SendToAsync(this))
            {
                return new ValueTask<int>(this, _token);
            }

            return CompleteSynchronously();
        }

        private ValueTask<int> CompleteSynchronously()
        {
            // Completing synchronously, so we don't need to preserve the 
            // async bits.
            Reset();

            var error = SocketError;
            if (error == SocketError.Success)
            {
                // Return a ValueTask directly, in a no-alloc operation.
                return new ValueTask<int>(BytesTransferred);
            }

            // Fail synchronously.
            return ValueTask.FromException<int>(new SocketException((int)error));
        }

        // This method is called by the base class when our async socket operation completes. 
        // The goal here is to wake up our 
        protected override void OnCompleted(SocketAsyncEventArgs e)
        {
            Action<object?>? c = _continuation;

            // This Interlocked.Exchange is intended to ensure that only one path can end up invoking the
            // continuation that completes the ValueTask. We swap for our _completedSentinel, and only proceed if
            // there was a continuation action present in that field.
            if (c != null || (c = Interlocked.CompareExchange(ref _continuation, _completedSentinel, null)) != null)
            {
                object? continuationState = UserToken;
                UserToken = null;

                // Mark us as done.
                _continuation = _completedSentinel;

                // Invoke the continuation. Because this completion is, by its nature, happening asynchronously,
                // we don't need to force an async invoke.
                InvokeContinuation(c, continuationState, forceAsync: false);
            }
        }

        // This method is invoked if someone calls ValueTask.IsCompleted (for example) and when the operation completes, and needs to indicate the
        // state of the current operation.
        public ValueTaskSourceStatus GetStatus(short token)
        {
            if (token != _token)
            {
                ThrowMisuseException();
            }

            // If _continuation isn't _completedSentinel, we're still going.
            return !ReferenceEquals(_continuation, _completedSentinel) ? ValueTaskSourceStatus.Pending :
                    SocketError == SocketError.Success ? ValueTaskSourceStatus.Succeeded :
                    ValueTaskSourceStatus.Faulted;
        }

        // This method is only called once per ValueTask, once GetStatus returns something other than
        // ValueTaskSourceStatus.Pending.
        public int GetResult(short token)
        {
            // Detect multiple awaits on a single ValueTask.
            if (token != _token)
            {
                ThrowMisuseException();
            }

            // We're done, reset.
            Reset();

            // Now we just return the result (or throw if there was an error).
            var error = SocketError;
            if (error == SocketError.Success)
            {
                return BytesTransferred;
            }

            throw new SocketException((int)error);
        }

        // This is called when someone awaits on the ValueTask, and tells us what method to call to complete.
        public void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
        {
            if (token != _token)
            {
                ThrowMisuseException();
            }

            UserToken = state;

            // Do the exchange so we know we're the only ones that could invoke the continuation.
            Action<object>? prevContinuation = Interlocked.CompareExchange(ref _continuation, continuation, null);

            // Check whether we've already finished.
            if (ReferenceEquals(prevContinuation, _completedSentinel))
            {
                // This means the operation has already completed; most likely because we completed before
                // we could attach the continuation.
                // Don't need to store the user token.
                UserToken = null;

                // We need to set forceAsync here and dispatch on the ThreadPool, otherwise
                // we can hit a stackoverflow!
                InvokeContinuation(continuation, state, forceAsync: true);
            }
            else if (prevContinuation != null)
            {
                throw new InvalidOperationException("Continuation being attached more than once.");
            }
        }

        private void InvokeContinuation(Action<object?> continuation, object? state, bool forceAsync)
        {
            if (forceAsync)
            {
                // Dispatch the operation on the thread pool.
                ThreadPool.UnsafeQueueUserWorkItem(continuation, state, preferLocal: true);
            }
            else
            {
                // Just complete the continuation inline (on the IO thread that completed the socket operation).
                continuation(state);
            }
        }

        private void Reset()
        {
            // Increment our token for the next operation.
            _token++;
            _continuation = null;
        }

        private static void ThrowMisuseException()
        {
            throw new InvalidOperationException("ValueTask mis-use; multiple await?");
        }
    }

}

namespace IPA.Cores.Helper.Basic
{
    public static class UdpSocketExtensions
    {
        // This pool of socket events means that we don't need to keep allocating the SocketEventArgs.
        // The main reason we want to pool these (apart from just reducing allocations), is that, on windows at least, within the depths 
        // of the underlying SocketAsyncEventArgs implementation, each one holds an instance of PreAllocatedNativeOverlapped,
        // an IOCP-specific object which is VERY expensive to allocate each time.        
        private static readonly ObjectPool<UdpAwaitableSocketAsyncEventArgs> _socketEventPool = ObjectPool.Create<UdpAwaitableSocketAsyncEventArgs>();
        private static readonly IPEndPoint _blankEndpoint_IPv4 = new IPEndPoint(IPAddress.Any, 0);
        private static readonly IPEndPoint _blankEndpoint_IPv6 = new IPEndPoint(IPAddress.IPv6Any, 0);

        /// <summary>
        /// Send a block of data to a specified destination, and complete asynchronously.
        /// </summary>
        /// <param name="socket">The socket to send on.</param>
        /// <param name="destination">The destination of the data.</param>
        /// <param name="data">The data buffer itself.</param>
        /// <returns>The number of bytes transferred.</returns>
        public static async ValueTask<int> SendToAsync(this Socket socket, EndPoint destination, ReadOnlyMemory<byte> data)
        {
            // Get an async argument from the socket event pool.
            var asyncArgs = _socketEventPool.Get();

            asyncArgs.RemoteEndPoint = destination;
            asyncArgs.SetBuffer(MemoryMarshal.AsMemory(data));

            try
            {
                return await asyncArgs.DoSendToAsync(socket);
            }
            finally
            {
                _socketEventPool.Return(asyncArgs);
            }
        }

        /// <summary>
        /// Asynchronously receive a block of data, getting the amount of data received, and the remote endpoint that
        /// sent it.
        /// </summary>
        /// <param name="socket">The socket to send on.</param>
        /// <param name="buffer">The buffer to place data in.</param>
        /// <returns>The number of bytes transferred.</returns>
        public static async ValueTask<SocketReceiveFromResult> ReceiveFromAsync(this Socket socket, Memory<byte> buffer, AddressFamily addressFamily)
        {
            // Get an async argument from the socket event pool.
            var asyncArgs = _socketEventPool.Get();

            if (addressFamily == AddressFamily.InterNetwork)
            {
                asyncArgs.RemoteEndPoint = _blankEndpoint_IPv4;
            }
            else if (addressFamily == AddressFamily.InterNetworkV6)
            {
                asyncArgs.RemoteEndPoint = _blankEndpoint_IPv6;
            }
            else
            {
                throw new ArgumentOutOfRangeException(nameof(addressFamily));
            }

            asyncArgs.SetBuffer(buffer);

            try
            {
                var recvdBytes = await asyncArgs.DoReceiveFromAsync(socket);

                return new SocketReceiveFromResult { ReceivedBytes = recvdBytes, RemoteEndPoint = asyncArgs.RemoteEndPoint };

            }
            finally
            {
                _socketEventPool.Return(asyncArgs);
            }
        }
    }
}

