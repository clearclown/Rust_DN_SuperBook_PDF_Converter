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
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Collections.Generic;
using System.Linq;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using System.Net.NetworkInformation;
using System.ComponentModel;

namespace IPA.Cores.Basic;

public static partial class CoresConfig
{
    public static partial class TcpIpSystemSettings
    {
        public static readonly Copenhagen<int> LocalHostPossibleGlobalIpAddressListCacheLifetime = 5 * 60 * 1000;
        public static readonly Copenhagen<int> LocalHostHostInfoCacheLifetime = 60 * 1000; // UNIX のみ
        public static readonly Copenhagen<int> HostHostInfoCacheLifetime = 25 * 1000;
        public static readonly Copenhagen<int> EasyHostNameToIpCacheLifetime = 30 * 1000;
    }

    public static partial class TcpIpStackDefaultSettings
    {
        public static readonly Copenhagen<int> ConnectTimeout = 15 * 1000;
        public static readonly Copenhagen<int> DnsTimeout = 15 * 1000;
        public static readonly Copenhagen<int> DnsTimeoutForSendPing = 8 * 1000;
    }
}

public abstract class NetworkConnectParam
{
    public IPAddress DestIp { get; private set; }
    public int DestPort { get; private set; }
    public IPAddress? SrcIp { get; }
    public int SrcPort { get; }

    public bool NeedToSolveDestHostname { get; private set; }
    public string? DestHostname { get; }
    public int DnsTimeout { get; }

    public AddressFamily AddressFamily => DestIp.AddressFamily;

    public NetworkConnectParam(string destHostname, int destPort, AddressFamily addressFamily = AddressFamily.InterNetwork, int srcPort = 0, int dnsTimeout = -1)
        : this(destHostname, destPort, GetAnyIPAddress(addressFamily), srcPort, dnsTimeout) { }

    public NetworkConnectParam(string destHostname, int destPort, IPAddress srcIp, int srcPort = 0, int dnsTimeout = -1)
        : this(GetAnyIPAddress(srcIp.AddressFamily), destPort, srcIp, srcPort)
    {
        if (dnsTimeout <= 0)
            dnsTimeout = CoresConfig.TcpIpStackDefaultSettings.DnsTimeout;

        this.NeedToSolveDestHostname = true;
        this.DestHostname = destHostname;
        this.DnsTimeout = dnsTimeout;
    }

    public NetworkConnectParam(IPAddress destIp, int destPort, IPAddress? srcIp = null, int srcPort = 0)
    {
        if (destIp == null) throw new ArgumentNullException("destIp");
        if (CheckAddressFamily(destIp) == false) throw new ArgumentOutOfRangeException("destIp.AddressFamily");
        if (CheckPortRange(destPort) == false) throw new ArgumentOutOfRangeException("destPort");

        if (srcIp != null)
        {
            if (srcIp.AddressFamily != destIp.AddressFamily) throw new ArgumentException("srcIp.AddressFamily != destIp.AddressFamily");
        }
        else
        {
            if (destIp.AddressFamily == AddressFamily.InterNetwork)
                srcIp = IPAddress.Any;
            else if (destIp.AddressFamily == AddressFamily.InterNetworkV6)
                srcIp = IPAddress.IPv6Any;
            else
                Debug.Assert(false, "Unsupported destIp.AddressFamily");
        }

        if (srcPort != 0 && CheckPortRange(srcPort) == false) throw new ArgumentOutOfRangeException("srcPort");

        this.DestIp = destIp;
        this.DestPort = destPort;
        this.SrcIp = srcIp;
        this.SrcPort = srcPort;
    }

    public static bool CheckPortRange(int port)
    {
        if (port < 1 || port > 65535) return false;
        return true;
    }

    public static bool CheckAddressFamily(IPAddress addr) => CheckAddressFamily(addr.AddressFamily);
    public static bool CheckAddressFamily(AddressFamily af)
    {
        if (af == AddressFamily.InterNetwork || af == AddressFamily.InterNetworkV6)
            return true;

        return false;
    }

    public static IPAddress GetAnyIPAddress(AddressFamily family)
    {
        if (family == AddressFamily.InterNetwork)
            return IPAddress.Any;
        else if (family == AddressFamily.InterNetworkV6)
            return IPAddress.IPv6Any;
        else
            throw new ArgumentOutOfRangeException("family");
    }

    public async Task ResolveDestHostnameIfNecessaryAsync(TcpIpSystem system, CancellationToken cancel = default)
    {
        if (this.NeedToSolveDestHostname == false) return;

        IPAddress ip = await system.GetIpAsync(this.DestHostname!, this.AddressFamily, this.DnsTimeout, cancel);
        this.DestIp = ip;
        this.NeedToSolveDestHostname = false;
    }
}

public class TcpConnectParam : NetworkConnectParam
{
    public int ConnectTimeout { get; }

    public TcpConnectParam(IPAddress destIp, int destPort, IPAddress? srcIp = null, int srcPort = 0, int connectTimeout = -1)
        : base(destIp, destPort, srcIp, srcPort)
    {
        if (connectTimeout <= 0) connectTimeout = CoresConfig.TcpIpStackDefaultSettings.ConnectTimeout;

        this.ConnectTimeout = connectTimeout;
    }

    public TcpConnectParam(string destHostname, int destPort, IPAddress srcIp, int srcPort = 0, int connectTimeout = -1, int dnsTimeout = -1)
        : base(destHostname, destPort, srcIp, srcPort, dnsTimeout)
    {
        if (connectTimeout <= 0) connectTimeout = CoresConfig.TcpIpStackDefaultSettings.ConnectTimeout;

        this.ConnectTimeout = connectTimeout;
    }

    public TcpConnectParam(string destHostname, int destPort, AddressFamily addressFamily = AddressFamily.InterNetwork, int srcPort = 0, int connectTimeout = -1, int dnsTimeout = -1)
        : base(destHostname, destPort, addressFamily, srcPort, dnsTimeout)
    {
        if (connectTimeout <= 0) connectTimeout = CoresConfig.TcpIpStackDefaultSettings.ConnectTimeout;

        this.ConnectTimeout = connectTimeout;
    }
}

public class TcpListenParam
{
    public NetTcpListenerAcceptedProcCallback? AcceptCallback { get; }
    public IReadOnlyList<IPEndPoint> EndPointsList { get; }
    public string? RateLimiterConfigName { get; }
    public bool IsRandomPortMode { get; }
    public AddressFamily RandomPortModeAddressFamily { get; }
    public IPAddress? RandomPortListenAddress { get; }

    static IPEndPoint[] PortsToEndPoints(int[] ports)
    {
        List<IPEndPoint> ret = new List<IPEndPoint>();

        foreach (int port in ports)
        {
            ret.Add(new IPEndPoint(IPAddress.Any, port));
            ret.Add(new IPEndPoint(IPAddress.IPv6Any, port));
        }

        return ret.ToArray();
    }

    public TcpListenParam(NetTcpListenerAcceptedProcCallback? acceptCallback, string? rateLimiterConfigName = null, params int[] ports)
        : this(acceptCallback, rateLimiterConfigName, PortsToEndPoints(ports)) { }

    public TcpListenParam(NetTcpListenerAcceptedProcCallback? acceptCallback, string? rateLimiterConfigName = null, params IPEndPoint[] endPoints)
    {
        this.EndPointsList = endPoints.ToList();
        this.AcceptCallback = acceptCallback;
        this.RateLimiterConfigName = rateLimiterConfigName;
    }

    public TcpListenParam(EnsureSpecial isRandomPortMode, NetTcpListenerAcceptedProcCallback? acceptCallback = null, AddressFamily family = AddressFamily.InterNetwork, IPAddress? address = null)
    {
        this.EndPointsList = new IPEndPoint[0];
        this.AcceptCallback = acceptCallback;
        this.IsRandomPortMode = true;
        this.RandomPortModeAddressFamily = family;
        if (address == null)
        {
            if (family == AddressFamily.InterNetwork)
            {
                address = IPAddress.Any;
            }
            else if (family == AddressFamily.InterNetworkV6)
            {
                address = IPAddress.IPv6Any;
            }
            else
            {
                throw new ArgumentOutOfRangeException(nameof(family));
            }
        }

        this.RandomPortListenAddress = address;
    }

    public TcpListenParam(EnsureSpecial compatibleWithKestrel, NetTcpListenerAcceptedProcCallback? acceptCallback, IPEndPoint endPoint, string? rateLimiterConfigName = null)
    {
        List<IPEndPoint> ret = new List<IPEndPoint>();

        if (endPoint.Address == IPAddress.IPv6Any)
        {
            ret.Add(new IPEndPoint(IPAddress.Any, endPoint.Port));
            ret.Add(new IPEndPoint(IPAddress.IPv6Any, endPoint.Port));
        }
        else
        {
            ret.Add(endPoint);
        }

        this.EndPointsList = ret;
        this.AcceptCallback = acceptCallback;
        this.RateLimiterConfigName = rateLimiterConfigName;
    }
}

[Flags]
public enum DnsQueryOptions
{
    Default = 0,
    IPv4Only = 1,
    IPv6Only = 2,
}

public abstract class DnsQueryParamBase
{
    public DnsQueryOptions Options { get; }
    public int Timeout { get; }

    public DnsQueryParamBase(DnsQueryOptions options = DnsQueryOptions.Default, int timeout = -1)
    {
        if (timeout <= 0)
            timeout = CoresConfig.TcpIpStackDefaultSettings.DnsTimeout;

        this.Timeout = timeout;
    }
}

public class DnsGetIpQueryParam : DnsQueryParamBase
{
    public string Hostname { get; }

    public DnsGetIpQueryParam(string hostname, DnsQueryOptions options = DnsQueryOptions.Default, int timeout = -1) : base(options, timeout)
    {
        if (hostname._IsEmpty()) throw new ArgumentException("hostname is empty.");
        this.Hostname = hostname._NonNullTrim();
    }
}

public class DnsGetFqdnQueryParam : DnsQueryParamBase
{
    public IPAddress Ip { get; }

    public DnsGetFqdnQueryParam(IPAddress ip, int timeout = -1) : base(DnsQueryOptions.Default, timeout)
    {
        this.Ip = ip;
    }
}

public class DnsResponse
{
    public DnsQueryParamBase Query { get; }
    public IReadOnlyList<IPAddress> IPAddressList { get; }
    public IReadOnlyList<string> FqdnList { get; }

    public DnsResponse(DnsQueryParamBase query, IEnumerable<IPAddress> addressList)
    {
        this.Query = query;

        IEnumerable<IPAddress> filtered = addressList;

        if (this.Query.Options.Bit(DnsQueryOptions.IPv4Only))
            filtered = filtered.Where(x => x.AddressFamily == AddressFamily.InterNetwork);

        if (this.Query.Options.Bit(DnsQueryOptions.IPv6Only))
            filtered = filtered.Where(x => x.AddressFamily == AddressFamily.InterNetworkV6);

        this.IPAddressList = new List<IPAddress>(filtered);

        this.FqdnList = new List<string>();
    }

    public DnsResponse(DnsQueryParamBase query, IEnumerable<string> fqdnList)
    {
        this.Query = query;

        this.IPAddressList = new List<IPAddress>();

        this.FqdnList = fqdnList.ToList();
    }
}

// 注: DaemonCenter で利用しているためいじらないこと
public class TcpIpHostDataJsonSafe
{
    public string? HostName;
    public string? DomainName;
    public string? FqdnHostName;
    public bool IsIPv4Supported;
    public bool IsIPv6Supported;
    public string[]? IPAddressList;

    public TcpIpHostDataJsonSafe() { }

    public TcpIpHostDataJsonSafe(EnsureSpecial getThisHostInfo, bool once)
    {
        TcpIpSystemHostInfo info = LocalNet.GetHostInfo(once);

        this.HostName = info.HostName;
        this.DomainName = info.DomainName;
        this.FqdnHostName = info.FqdnHostName;
        this.IsIPv4Supported = info.IsIPv4Supported;
        this.IsIPv6Supported = info.IsIPv6Supported;
        this.IPAddressList = info.IPAddressList.Select(x => x.ToString()).ToArray();
    }
}

public abstract class TcpIpSystemHostInfo
{
    public abstract int InfoVersion { get; protected set; }
    public abstract string HostName { get; protected set; }
    public abstract string DomainName { get; protected set; }
    public string FqdnHostName => HostName + (string.IsNullOrEmpty(DomainName) ? "" : "." + DomainName);
    public abstract bool IsIPv4Supported { get; protected set; }
    public abstract bool IsIPv6Supported { get; protected set; }
    public abstract IReadOnlyList<IPAddress> IPAddressList { get; protected set; }

    public bool IsLocalIpAddress(IPAddress ip)
    {
        var type = ip._GetIPAddressType();
        if (type.Bit(IPAddressType.Loopback))
        {
            return true;
        }

        foreach (var a in this.IPAddressList)
        {
            if (IpComparer.ComparerWithIgnoreScopeId.Equals(a, ip))
            {
                return true;
            }
        }

        return false;
    }
}

public class TcpIpSystemParam : NetworkSystemParam
{
    public TcpIpSystemParam(string name) : base(name) { }
}

public interface ITcpConnectableSystem
{
    Task<ConnSock> ConnectAsync(TcpConnectParam param, CancellationToken cancel = default);
}

public abstract partial class TcpIpSystem : NetworkSystemBase, ITcpConnectableSystem
{
    protected new TcpIpSystemParam Param => (TcpIpSystemParam)base.Param;

    protected abstract TcpIpSystemHostInfo GetHostInfoImpl(bool doNotStartBackground);
    protected abstract int RegisterHostInfoChangedEventImpl(AsyncAutoResetEvent ev);
    protected abstract void UnregisterHostInfoChangedEventImpl(int registerId);
    protected abstract NetTcpProtocolStubBase CreateTcpProtocolStubImpl(TcpConnectParam param, CancellationToken cancel);
    protected abstract Task<DnsResponse> QueryDnsImplAsync(DnsQueryParamBase param, CancellationToken cancel);
    protected abstract NetTcpListener CreateTcpListenerImpl(NetTcpListenerAcceptedProcCallback acceptedProc, string? rateLimiterConfigName = null);
    protected abstract DnsResolver CreateDnsResolverImpl();
    protected abstract NetUdpListener CreateUdpListenerImpl(NetUdpListenerOptions options);

    readonly Singleton<DnsResolver> DnsResolverSingleton;
    public DnsResolver DnsResolver => DnsResolverSingleton;

    protected abstract Task<SendPingReply> SendPingImplAsync(IPAddress target, byte[] data, int timeout, CancellationToken cancel);

    AsyncCache<HashSet<IPAddress>> LocalHostPossibleGlobalIpAddressListCache;

    readonly CachedProperty<TcpIpSystemHostInfo> HostInfoCache;

    readonly AsyncCache<string, IPAddress> EasyHostNameToIpCache;

    public TcpIpSystem(TcpIpSystemParam param) : base(param)
    {
        LocalHostPossibleGlobalIpAddressListCache =
            new AsyncCache<HashSet<IPAddress>>(CoresConfig.TcpIpSystemSettings.LocalHostPossibleGlobalIpAddressListCacheLifetime, CacheFlags.IgnoreUpdateError | CacheFlags.NoGc,
            GetLocalHostPossibleGlobalIpAddressListMainAsync);

        DnsResolverSingleton = new Singleton<DnsResolver>(() => CreateDnsResolverImpl());

        this.HostInfoCache = new CachedProperty<TcpIpSystemHostInfo>(getter: () => this.GetHostInfo(true), expiresLifeTimeMsecs: CoresConfig.TcpIpSystemSettings.HostHostInfoCacheLifetime);

        this.EasyHostNameToIpCache = new AsyncCache<string, IPAddress>(CoresConfig.TcpIpSystemSettings.EasyHostNameToIpCacheLifetime, CacheFlags.IgnoreUpdateError | CacheFlags.NoGc,
            async (hostname, cancel) => await this.GetIpAsync(hostname, cancel: cancel, orderBy: ip => (long)ip.AddressFamily));
    }

    public async Task< IPAddress> EasyHostnameToIpAsync(string hostname, CancellationToken cancel = default) => (await this.EasyHostNameToIpCache.GetAsync(hostname, cancel))!;

    protected override async Task CleanupImplAsync(Exception? ex)
    {
        try
        {
            this.DnsResolverSingleton._DisposeSafe();
        }
        finally
        {
            await base.CleanupImplAsync(ex);
        }
    }

    public TcpIpSystemHostInfo GetHostInfoCached() => this.HostInfoCache;

    public TcpIpSystemHostInfo GetHostInfo(bool doNotStartBackground) => GetHostInfoImpl(doNotStartBackground);

    public int RegisterHostInfoChangedEvent(AsyncAutoResetEvent ev) => RegisterHostInfoChangedEventImpl(ev);
    public void UnregisterHostInfoChangedEvent(int registerId) => UnregisterHostInfoChangedEventImpl(registerId);

    public async Task<SendPingReply> SendPingAsync(string hostName, AddressFamily? v4v6 = null, byte[]? data = null,
        int pingTimeout = Consts.Timeouts.DefaultSendPingTimeout, CancellationToken pingCancel = default, int dnsTimeout = 0, CancellationToken dnsCancel = default)
    {
        await using (CreatePerTaskCancellationToken(out CancellationToken opPingCancel, dnsCancel))
        {
            await using (CreatePerTaskCancellationToken(out CancellationToken opDnsCancel, dnsCancel))
            {
                using (EnterCriticalCounter())
                {
                    try
                    {
                        if (dnsTimeout <= 0) dnsTimeout = CoresConfig.TcpIpStackDefaultSettings.DnsTimeoutForSendPing;

                        var ip = await this.GetIpAsync(hostName, v4v6, dnsTimeout, opDnsCancel);

                        return await SendPingAsync(ip, data, pingTimeout, opPingCancel);
                    }
                    catch (Exception ex)
                    {
                        return new SendPingReply(IPStatus.Unknown, default, ex, 0);
                    }
                }
            }
        }
    }
    public SendPingReply SendPing(string hostName, AddressFamily? v4v6 = null, byte[]? data = null,
        int timeout = Consts.Timeouts.DefaultSendPingTimeout, CancellationToken pingCancel = default, int dnsTimeout = 0, CancellationToken dnsCancel = default)
        => SendPingAsync(hostName, v4v6, data, timeout, pingCancel, dnsTimeout, dnsCancel)._GetResult();

    public async Task<SendPingReply> SendPingAsync(IPAddress target, byte[]? data = null, int timeout = Consts.Timeouts.DefaultSendPingTimeout, CancellationToken pingCancel = default)
    {
        if (data == null) data = Util.Rand(Consts.Numbers.DefaultSendPingSize);
        if (timeout <= 0) timeout = Consts.Timeouts.DefaultSendPingTimeout;

        return await SendPingImplAsync(target, data, timeout, pingCancel);
    }
    public SendPingReply SendPing(IPAddress target, byte[]? data = null, int timeout = Consts.Timeouts.DefaultSendPingTimeout)
        => SendPingAsync(target, data, timeout)._GetResult();

    public async Task<SendPingReply> SendPingAndGetBestResultAsync(IPAddress target, byte[]? data = null, int timeout = Consts.Timeouts.DefaultSendPingTimeout, CancellationToken pingCancel = default, int numTry = 5)
    {
        numTry = Math.Max(numTry, 1);

        List<SendPingReply> o = new List<SendPingReply>();

        for (int i = 0; i < numTry; i++)
        {
            SendPingReply r = await SendPingAsync(target, data, timeout, pingCancel);

            o.Add(r);
        }

        SendPingReply? best = o.Where(x => x.Ok).OrderBy(x => x.RttDouble).FirstOrDefault();
        if (best != null) return best;

        return o.Last();
    }

    public async Task<ConnSock> ConnectAsync(TcpConnectParam param, CancellationToken cancel = default)
    {
        await using (CreatePerTaskCancellationToken(out CancellationToken opCancel, cancel))
        {
            await param.ResolveDestHostnameIfNecessaryAsync(this, opCancel);

            using (EnterCriticalCounter())
            {
                NetTcpProtocolStubBase tcp = CreateTcpProtocolStubImpl(param, this.GrandCancel);

                try
                {
                    await tcp.ConnectAsync(new IPEndPoint(param.DestIp, param.DestPort), param.ConnectTimeout, opCancel);

                    ConnSock sock = new ConnSock(tcp);
                    try
                    {
                        this.AddToOpenedSockList(sock, LogTag.SocketConnected);

                        return sock;
                    }
                    catch
                    {
                        await sock._DisposeWithCleanupSafeAsync();
                        throw;
                    }
                }
                catch
                {
                    await tcp._DisposeWithCleanupSafeAsync();
                    throw;
                }
            }
        }
    }
    public ConnSock Connect(TcpConnectParam param, CancellationToken cancel = default)
        => ConnectAsync(param, cancel)._GetResult();

    // UDP リスナーを作成する
    public NetUdpListener CreateUdpListener(NetUdpListenerOptions options)
    {
        NetUdpListener ret = CreateUdpListenerImpl(options);

        return ret;
    }

    // TCP リスナーを作成する
    // 注意: param.AcceptCallback == null の場合は NetTcpListener.AcceptNextSocketFromQueueUtilAsync() で通常のソケット Accept() 動作と同様の動作が可能となる
    [Obsolete]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public NetTcpListener CreateListener(TcpListenParam param)
        => CreateTcpListener(param);
    public NetTcpListener CreateTcpListener(TcpListenParam param)
    {
        var hostInfo = GetHostInfo(true);

        // ユーザーが Callback を指定していないので GenericAcceptQueueUtil キューユーティリティを初期化する
        GenericAcceptQueueUtil<ConnSock>? acceptQueueUtil = null;
        if (param.AcceptCallback == null)
        {
            acceptQueueUtil = new GenericAcceptQueueUtil<ConnSock>();
        }

        try
        {
            using (EnterCriticalCounter())
            {
                NetTcpListener ret = CreateTcpListenerImpl(async (listener, sock) =>
                {
                    this.AddToOpenedSockList(sock, LogTag.SocketAccepted);

                    // ソケットが Accept されたらここに飛ぶ。
                    // この非同期 Callback はユーザーがソケットを閉じてもはや不要となるまで await しなければならない。

                    if (param.AcceptCallback != null)
                    {
                        // ユーザー指定の Callback が設定されている
                        await param.AcceptCallback(listener, sock);
                    }
                    else
                    {
                        // GenericAcceptQueueUtil ユーティリティでキューに登録する
                        await acceptQueueUtil!.InjectAndWaitAsync(sock);
                    }
                }, param.RateLimiterConfigName);

                try
                {
                    if (acceptQueueUtil != null)
                    {
                        // Listener が廃棄される際は GenericAcceptQueueUtil キューをキャンセルするよう登録する
                        ret.AddOnCancelAction(() => TaskUtil.StartAsyncTaskAsync(() => acceptQueueUtil._DisposeSafeAsync(new OperationCanceledException())))._LaissezFaire(true);

                        // Listner クラスの AcceptNextSocketFromQueueUtilAsync を登録する
                        ret.AcceptNextSocketFromQueueUtilAsync = (cancel) => acceptQueueUtil.AcceptAsync(cancel);
                    }

                    if (param.IsRandomPortMode == false)
                    {
                        foreach (IPEndPoint ep in param.EndPointsList)
                        {
                            try
                            {
                                if (hostInfo.IsIPv4Supported && ep.AddressFamily == AddressFamily.InterNetwork) ret.Add(ep.Port, IPVersion.IPv4, ep.Address);
                            }
                            catch { }

                            try
                            {
                                if (hostInfo.IsIPv6Supported && ep.AddressFamily == AddressFamily.InterNetworkV6) ret.Add(ep.Port, IPVersion.IPv6, ep.Address);
                            }
                            catch { }
                        }
                    }
                    else
                    {
                        ret.AddRandom(param.RandomPortModeAddressFamily.GetIPVersion(), param.RandomPortListenAddress);
                    }

                    return ret;
                }
                catch
                {
                    ret._DisposeSafe();
                    throw;
                }
            }
        }
        catch (Exception ex)
        {
            // 途中でエラーが発生した場合は GenericAcceptQueueUtil キューをキャンセルする
            acceptQueueUtil._DisposeSafe(ex);
            throw;
        }
    }

    public async Task<string> GetHostNameSingleOrIpAsync(IPAddress? ip, CancellationToken cancel = default)
    {
        if (ip == null) return "";

        try
        {
            var res = await GetHostNameAsync(ip, null, cancel);

            if (res._IsEmpty())
            {
                return ip.ToString();
            }

            return res.First();
        }
        catch
        {
            return ip.ToString();
        }
    }
    public Task<string> GetHostNameSingleOrIpAsync(string? ip, CancellationToken cancel = default)
        => GetHostNameSingleOrIpAsync(ip._ToIPAddress(noExceptionAndReturnNull: true), cancel);

    // IP アドレスからホスト名を逆引き。できるだけ専用 DNS リゾルバライブラリを利用
    public virtual async Task<List<string>?> GetHostNameAsync(IPAddress? ip, Ref<DnsAdditionalResults>? additional = null, CancellationToken cancel = default)
    {
        if (ip == null) return null;

        if (this.DnsResolver.IsAvailable == false)
        {
            DnsAdditionalResults additionalResults = new DnsAdditionalResults(true, false);

            try
            {
                var p = new DnsGetFqdnQueryParam(ip);

                var res = await QueryDnsAsync(p, cancel);

                var ret = res.FqdnList.ToList();

                if (ret._IsEmpty())
                {
                    additionalResults = new DnsAdditionalResults(false, true);

                    ret = null;
                }
                else
                {
                    additionalResults = new DnsAdditionalResults(false, false);
                }

                return ret;
            }
            catch
            {
                return null;
            }
            finally
            {
                additional?.Set(additionalResults);
            }
        }
        else
        {
            return await this.DnsResolver.GetHostNameListAsync(ip, additional, cancel);
        }
    }

    public async Task<DnsResponse> QueryDnsAsync(DnsQueryParamBase param, CancellationToken cancel = default)
    {
        try
        {
            await using (CreatePerTaskCancellationToken(out CancellationToken opCancel, cancel))
            {
                using (EnterCriticalCounter())
                {
                    return await QueryDnsImplAsync(param, opCancel);
                }
            }
        }
        catch
        {
            // OS によって "localhost" の解決がなぜかできなかった場合は、自力で解決を行なう
            if (param is DnsGetIpQueryParam ipQuery)
            {
                string hostname = ipQuery.Hostname;

                if (hostname._IsSamei("localhost") || hostname._IsSamei("localhost.localdomain"))
                {
                    if (ipQuery.Options.Bit(DnsQueryOptions.IPv6Only))
                    {
                        return new DnsResponse(param, IPAddress.IPv6Loopback._SingleList());
                    }
                    else if (ipQuery.Options.Bit(DnsQueryOptions.IPv4Only))
                    {
                        return new DnsResponse(param, IPAddress.Loopback._SingleList());
                    }
                    else
                    {
                        return new DnsResponse(param, new[] { IPAddress.Loopback, IPAddress.IPv6Loopback });
                    }
                }

                if (hostname._IsSamei("localhost6") || hostname._IsSamei("localhost6.localdomain6"))
                {
                    if (ipQuery.Options.Bit(DnsQueryOptions.IPv4Only) == false)
                    {
                        return new DnsResponse(param, new[] { IPAddress.Loopback, IPAddress.IPv6Loopback });
                    }
                }
            }

            throw;
        }
    }
    public DnsResponse QueryDns(DnsQueryParamBase param, CancellationToken cancel = default)
        => QueryDnsAsync(param, cancel)._GetResult();

    async Task<HashSet<IPAddress>?> GetLocalHostPossibleGlobalIpAddressListMainAsync(CancellationToken cancel)
    {
        HashSet<IPAddress> ret = new HashSet<IPAddress>();

        TcpIpSystemHostInfo info = this.GetHostInfo(true);

        foreach (IPAddress addr in info.IPAddressList)
        {
            IPAddressType ipType = addr._GetIPAddressType();
            if (ipType.Bit(IPAddressType.GlobalIp))
            {
                ret.Add(addr);
            }
        }

        using (GetMyIpClient myip = new GetMyIpClient(this))
        {
            try
            {
                IPAddress ipv4 = await myip.GetMyIpAsync(IPVersion.IPv4, cancel);

                if (ipv4._GetIPAddressType().Bit(IPAddressType.GlobalIp))
                {
                    ret.Add(ipv4);
                }
            }
            catch { }

            try
            {
                IPAddress ipv6 = await myip.GetMyIpAsync(IPVersion.IPv6, cancel);

                if (ipv6._GetIPAddressType().Bit(IPAddressType.GlobalIp))
                {
                    ret.Add(ipv6);
                }
            }
            catch { }
        }

        return ret;
    }

    public async Task<HashSet<IPAddress>> GetLocalHostPossibleIpAddressListAsync(CancellationToken cancel = default)
    {
        HashSet<IPAddress>? ret = await LocalHostPossibleGlobalIpAddressListCache.GetAsync(cancel);

        return ret ?? new HashSet<IPAddress>();
    }
}

// Ping 応答
public class SendPingReply
{
    public TimeSpan RttTimeSpan { get; }

    public double RttDouble { get; }

    public IPStatus Status { get; }

    public bool Ok { get; }

    public int Ttl { get; }

    public Exception? OptionalException { get; }

    public SendPingReply(IPStatus status, TimeSpan span, Exception? optionalException, int ttl)
    {
        this.Status = status;

        if (this.Status == IPStatus.Success)
        {
            this.RttTimeSpan = span;
            this.RttDouble = span.Ticks / 10000000.0;
            this.Ttl = ttl;
            Ok = true;
        }
        else
        {
            Ok = false;

            this.OptionalException = optionalException;
        }
    }
}

