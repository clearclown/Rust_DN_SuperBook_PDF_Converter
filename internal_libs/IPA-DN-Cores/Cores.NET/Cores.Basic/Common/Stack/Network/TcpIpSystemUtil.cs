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
using System.Text;
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
using System.Runtime.Serialization;

namespace IPA.Cores.Basic;

public abstract class SpeculativeConnectorBase
{
    protected class Attempt
    {
        public ITcpConnectableSystem System { get; }
        public TcpConnectParam Param { get; }
        public int PostWaitMsecs { get; }

        public Attempt(ITcpConnectableSystem system, TcpConnectParam param, int postWaitMsecs)
        {
            this.System = system;
            this.Param = param;
            this.PostWaitMsecs = postWaitMsecs;
        }

        public async Task<ConnSock> ConnectAsyncInternal(SpeculativeConnectorBase connector, CancellationToken cancel = default)
        {
            try
            {
                cancel.ThrowIfCancellationRequested();
                ConnSock tcp = await this.System.ConnectAsync(this.Param, cancel);

                if (PostWaitMsecs >= 1)
                {
                    await connector.PostWaitEvent.WaitAsync(this.PostWaitMsecs, cancel);
                }

                return tcp;
            }
            catch (Exception ex)
            {
                if (PostWaitMsecs == 0)
                {
                    connector.PostWaitEvent.Set();
                }

                connector.ExceptionList.Add(ex, ex is GetIpAddressFamilyMismatchException ? 1 : 100);
                throw;
            }
        }
    }

    readonly WeightedExceptionList ExceptionList = new WeightedExceptionList();

    AsyncManualResetEvent PostWaitEvent = new AsyncManualResetEvent();

    List<Attempt> AttemptList = new List<Attempt>();

    public TcpConnectParam BasicParam { get; }

    public SpeculativeConnectorBase(TcpConnectParam basicParam)
    {
        this.BasicParam = basicParam;
    }

    protected void AddAttempt(Attempt attempt)
    {
        AttemptList.Add(attempt);
    }

    Once Flag;

    public async Task<ConnSock> ConnectAsync(CancellationToken cancel = default)
    {
        if (Flag.IsFirstCall() == false) throw new ApplicationException("ConnectAsync() has been already called.");

        List<Task<ConnSock>> taskList = new List<Task<ConnSock>>();

        foreach (var attempt in AttemptList)
        {
            taskList.Add(attempt.ConnectAsyncInternal(this, cancel));
        }

        if (taskList.Count == 0)
        {
            throw new ApplicationException("SpeculativeConnectorBase: The attempt list is empty.");
        }

        while (true)
        {
            var okTask = taskList.Where(x => x.IsCompletedSuccessfully).FirstOrDefault();
            if (okTask != null)
            {
                // OK
                PostWaitEvent.Set();
                taskList.ForEach(x => x._TryWait(true));
                taskList.Where(x => x != okTask).Where(x => x.IsCompletedSuccessfully)._DoForEach(x => x.Result._DisposeSafe());
                return okTask._GetResult();
            }

            taskList.Where(x => x.IsCanceled || x.IsFaulted).ToArray()._DoForEach(x => taskList.Remove(x));

            if (taskList.Count == 0)
            {
                // Error
                throw this.ExceptionList.GetException()!;
            }

            await TaskUtil.WaitObjectsAsync(taskList.ToArray(), exceptions: ExceptionWhen.None);
        }
    }
}

public class IPv4V6DualStackSpeculativeConnector : SpeculativeConnectorBase
{
    public IPv4V6DualStackSpeculativeConnector(TcpConnectParam basicParam, TcpIpSystem system) : base(basicParam)
    {
        if (basicParam.DestHostname._IsEmpty())
        {
            // IP address is specified
            AddAttempt(new Attempt(system, basicParam, 0));
            return;
        }

        // Hostname is specified
        var hostInfo = system.GetHostInfo(true);

        if (hostInfo.IsIPv4Supported)
        {
            AddAttempt(new Attempt(system, new TcpConnectParam(basicParam.DestHostname, basicParam.DestPort, AddressFamily.InterNetwork,
                connectTimeout: basicParam.ConnectTimeout, dnsTimeout: basicParam.DnsTimeout), 0));
        }

        if (hostInfo.IsIPv6Supported)
        {
            AddAttempt(new Attempt(system, new TcpConnectParam(basicParam.DestHostname, basicParam.DestPort, AddressFamily.InterNetworkV6,
                connectTimeout: basicParam.ConnectTimeout, dnsTimeout: basicParam.DnsTimeout), 0));
        }
    }
}


public class GetIpAddressFamilyMismatchException : ApplicationException
{
    public GetIpAddressFamilyMismatchException(string message) : base(message) { }
}

public abstract partial class TcpIpSystem
{
    public async Task<string> ResolveHostAndPortListStrToIpAndPortListStrAsync(string srcStr, int? defaultPort = null, AddressFamily? addressFamily = null, int timeout = -1, int numParallelTasks = 8, Func<IPAddress, long>? orderBy = null, CancellationToken cancel = default, bool multiple = false)
    {
        string[] targetTokenList = srcStr._Split(StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries, " ", "\t", ";", ",", "/");

        List<Tuple<string, Ref<string>>> tmp = new List<Tuple<string, Ref<string>>>();

        foreach (var targetToken in targetTokenList)
        {
            tmp.Add(new (targetToken, ""));
        }

        Exception? firstError = null;

        await tmp._DoForEachParallelAsync(async (targetElement, taskIndex) =>
        {
            try
            {
                targetElement.Item2.Set(await ResolveHostAndPortStrToIpAndPortStrAsync(targetElement.Item1, defaultPort, addressFamily, timeout, orderBy, cancel, multiple));
            }
            catch (Exception ex)
            {
                // 例外があった場合は例外は握りつぶし、エラーを表示する
                firstError ??= ex;
                ex._Error();
            }
        },
        numParallelTasks,
        MultitaskDivideOperation.RoundRobin,
        cancel);

        if (tmp.Any() == false || tmp.Any(x => x.Item2._IsFilled()))
        {
            // 1 つでも結果がある場合は、例外を発生させない
            string ret = tmp.Select(x => x.Item2.Value)._Combine(" ");

            return ret;
        }
        else
        {
            // 結果が 0 の場合は、例外を発生させる
            firstError ??= new CoresException($"No resolve results found from the string '{srcStr}'");

            throw firstError;
        }
    }

    public async Task<string> ResolveHostAndPortStrToIpAndPortStrAsync(string srcStr, int? defaultPort = null, AddressFamily? addressFamily = null, int timeout = -1, Func<IPAddress, long>? orderBy = null, CancellationToken cancel = default, bool multiple = false)
    {
        if (IPUtil.TryParseHostPort(srcStr, out string host, out int port, defaultPort) == false)
        {
            throw new CoresException($"String '{srcStr}' is not like host:port or [ip]:port");
        }

        List<IPAddress> ipList = new List<IPAddress>();

        if (IPAddress.TryParse(host, out IPAddress? ip))
        {
            ipList.Add(ip);
        }
        else
        {
            if (multiple == false)
            {
                ip = await this.GetIpAsync(host, addressFamily, timeout, cancel, orderBy);
                ipList.Add(ip);
            }
            else
            {
                ipList.AddRange(await this.GetIpMultipleAsync(host, addressFamily, timeout, cancel, orderBy));
            }
        }

        return ipList.Select(ip =>
        {
            if (ip.AddressFamily == AddressFamily.InterNetwork)
            {
                return $"{ip.ToString()}:{port}";
            }
            else
            {
                return $"[{ip.ToString()}]:{port}";
            }
        })._Combine(" ");
    }

    public async Task<IEnumerable<IPAddress>> GetIpMultipleAsync(string hostname, AddressFamily? addressFamily = null, int timeout = -1, CancellationToken cancel = default, Func<IPAddress, long>? orderBy = null)
    {
        DnsResponse res = await this.QueryDnsAsync(new DnsGetIpQueryParam(hostname, timeout: timeout));

        var ipList = res.IPAddressList.Where(x => x.AddressFamily == AddressFamily.InterNetwork || x.AddressFamily == AddressFamily.InterNetworkV6)
            .Where(x => addressFamily == null || x.AddressFamily == addressFamily);

        if (orderBy != null)
        {
            ipList = ipList.OrderBy(ip => orderBy(ip));
        }

        ipList = ipList.DistinctBy(x => x, IpComparer.Comparer);

        if (ipList.Any() == false)
        {
            throw new GetIpAddressFamilyMismatchException($"The hostname \"{hostname}\" has no {addressFamily._ToIPv4v6String()} address.");
        }

        return ipList;
    }
    public IEnumerable<IPAddress> GetIpMultiple(string hostname, AddressFamily? addressFamily = null, int timeout = -1, CancellationToken cancel = default, Func<IPAddress, long>? orderBy = null)
        => GetIpMultipleAsync(hostname, addressFamily, timeout, cancel, orderBy)._GetResult();

    public async Task<IPAddress> GetIpAsync(string hostname, AddressFamily? addressFamily = null, int timeout = -1, CancellationToken cancel = default, Func<IPAddress, long>? orderBy = null)
    {
        var ipList = await GetIpMultipleAsync(hostname, addressFamily, timeout, cancel, orderBy);

        IPAddress? ret = ipList.FirstOrDefault();

        if (ret == null)
        {
            throw new GetIpAddressFamilyMismatchException($"The hostname \"{hostname}\" has no {addressFamily._ToIPv4v6String()} address.");
        }

        return ret;
    }

    public IPAddress GetIp(string hostname, AddressFamily? addressFamily = null, int timeout = -1, CancellationToken cancel = default, Func<IPAddress, long>? orderBy = null)
        => GetIpAsync(hostname, addressFamily, timeout, cancel, orderBy)._GetResult();

    public async Task<ConnSock> ConnectIPv4v6DualAsync(TcpConnectParam param, CancellationToken cancel = default)
    {
        IPv4V6DualStackSpeculativeConnector connector = new IPv4V6DualStackSpeculativeConnector(param, this);

        return await connector.ConnectAsync(cancel);
    }
    public ConnSock ConnectIPv4v6Dual(TcpConnectParam param, CancellationToken cancel = default)
        => ConnectIPv4v6DualAsync(param, cancel)._GetResult();

    // FQDN を入力し、サフィックスをパースする
    public void ParseDomainBySuffixList(string fqdn, out string suffix, out string suffixPlusOneToken, out string hostnames)
    {
        MasterData.DomainSuffixList.ParseDomainBySuffixList(fqdn, out suffix, out suffixPlusOneToken, out hostnames);
    }

    async Task<IPAddress?> GetMyPrivateIpInternalAsync(string hostname, int port, CancellationToken cancel = default)
    {
        try
        {
            await using (var sock = await this.ConnectAsync(new TcpConnectParam(hostname, port, connectTimeout: 5000, dnsTimeout: 5000)))
            {
                return sock.EndPointInfo.LocalIP._ToIPAddress(AllowedIPVersions.IPv4);
            }
        }
        catch
        {
            return null;
        }
    }

    public async Task<IPAddress> GetMyPrivateIpAsync(CancellationToken cancel = default)
    {
        string[] hosts = new string[]
            {
                    "www.msftncsi.com",
                    "www.msftconnecttest.com",
                    "www.microsoft.com",
                    "www.yahoo.com",
                    "www.bing.com",
            };

        foreach (string host in hosts)
        {
            var ip = await GetMyPrivateIpInternalAsync(host, Consts.Ports.Http, cancel);

            if (ip != null)
            {
                return ip;
            }
        }

        return IPAddress.Loopback;
    }
}

public class MiddleSock : NetSock
{
    public MiddleSock(NetProtocolBase protocolStack) : base(protocolStack) { }
}

public class MiddleConnSock : ConnSock
{
    public MiddleConnSock(NetProtocolBase protocolStack) : base(protocolStack) { }
}

public class SslSock : MiddleConnSock
{
    public LogDefSslSession? SslSessionInfo { get; private set; }

    protected new NetSslProtocolStack Stack => (NetSslProtocolStack)base.Stack;

    public SslSock(ConnSock lowerSock) : base(new NetSslProtocolStack(lowerSock.UpperPoint, null, null))
    {
    }

    public void UpdateSslSessionInfo()
    {
        this.SslSessionInfo = new LogDefSslSession()
        {
            IsServerMode = this.Info?.Ssl?.IsServerMode ?? false,
            SslProtocol = this.Info?.Ssl?.SslProtocol,
            CipherAlgorithm = this.Info?.Ssl?.CipherAlgorithm,
            CipherStrength = this.Info?.Ssl?.CipherStrength ?? 0,
            HashAlgorithm = this.Info?.Ssl?.HashAlgorithm,
            HashStrength = this.Info?.Ssl?.HashStrength ?? 0,
            KeyExchangeAlgorithm = this.Info?.Ssl?.KeyExchangeAlgorithm,
            KeyExchangeStrength = this.Info?.Ssl?.KeyExchangeStrength ?? 0,
            LocalCertificateInfo = this.Info?.Ssl?.LocalCertificate?.ToString(),
            LocalCertificateHashSHA1 = this.Info?.Ssl?.LocalCertificate?.HashSHA1,
            RemoteCertificateInfo = this.Info?.Ssl?.RemoteCertificate?.ToString(),
            RemoteCertificateHashSHA1 = this.Info?.Ssl?.RemoteCertificate?.HashSHA1,
        };
    }

    public async Task StartSslClientAsync(PalSslClientAuthenticationOptions sslClientAuthenticationOptions, CancellationToken cancellationToken = default)
    {
        await Stack.SslStartClientAsync(sslClientAuthenticationOptions, cancellationToken);
    }
    public void StartSslClient(PalSslClientAuthenticationOptions sslClientAuthenticationOptions, CancellationToken cancellationToken = default)
        => StartSslClientAsync(sslClientAuthenticationOptions, cancellationToken)._GetResult();

    public async Task StartSslServerAsync(PalSslServerAuthenticationOptions sslServerAuthenticationOptions, CancellationToken cancellationToken = default)
    {
        await Stack.SslStartServerAsync(sslServerAuthenticationOptions, cancellationToken);
    }
    public void StartSslServer(PalSslServerAuthenticationOptions sslServerAuthenticationOptions, CancellationToken cancellationToken = default)
        => StartSslServerAsync(sslServerAuthenticationOptions, cancellationToken)._GetResult();
}

public static class SslSockHelper
{
    public static async Task<SslSock> SslStartClientAsync(this ConnSock baseSock, PalSslClientAuthenticationOptions sslOptions, CancellationToken cancel = default)
    {
        SslSock ret = new SslSock(baseSock);

        await ret.StartSslClientAsync(sslOptions, cancel);

        return ret;
    }
    public static SslSock SslStartClient(this ConnSock baseSock, PalSslClientAuthenticationOptions sslOptions, CancellationToken cancel = default)
        => SslStartClientAsync(baseSock, sslOptions, cancel)._GetResult();
}

public class GenericAcceptQueueUtil<TSocket> : AsyncService
    where TSocket : AsyncService
{
    readonly CriticalSection Lock = new CriticalSection<GenericAcceptQueueUtil<TSocket>>();
    readonly Queue<SocketEntry> AcceptedQueue = new Queue<SocketEntry>();
    readonly AsyncAutoResetEvent AcceptedEvent = new AsyncAutoResetEvent();

    class SocketEntry
    {
        public TSocket Socket { get; }
        public AsyncManualResetEvent DisconnectedEvent { get; }

        public SocketEntry(TSocket sock)
        {
            this.Socket = sock;
            this.DisconnectedEvent = new AsyncManualResetEvent();

            // この Socket がユーザーによって Cancel されたときにイベントを発生させる
            this.Socket.AddOnCancelAction(() =>
            {
                this.DisconnectedEvent.Set(true);
                return Task.CompletedTask;
            })._LaissezFaire();
        }
    }

    public int BackLog { get; }

    public GenericAcceptQueueUtil(int backLog = 512)
    {
        backLog._SetMax(1);
        this.BackLog = backLog;
    }

    // 新しいソケットをキューに入れてから、ソケットが切断されるまで待機する
    public async Task<bool> InjectAndWaitAsync(TSocket newSocket)
    {
        if (newSocket == null) return false;

        SocketEntry? sockEntry = null;

        lock (Lock)
        {
            if (AcceptedQueue.Count >= this.BackLog)
            {
                // バックログがいっぱいです
                Dbg.Where($"Backlog exceeded: {AcceptedQueue.Count} >= {this.BackLog}");
                return false;
            }

            if (this.IsCanceled)
            {
                // 終了されようとしている
                return false;
            }

            AcceptedQueue.Enqueue(sockEntry = new SocketEntry(newSocket));
        }

        AcceptedEvent.Set();

        if (sockEntry.Socket.IsDisposed)
        {
            // すでに Dispose されていた
            return false;
        }

        // このソケットがユーザーによって Dispose されるまでの間待機する
        await sockEntry.DisconnectedEvent.WaitAsync(timeout: Timeout.Infinite, cancel: this.GrandCancel);

        return true;
    }

    // 新しいソケットがキューに入るまで待機し、キューに入ったらこれを Accept する
    public async Task<TSocket> AcceptAsync(CancellationToken cancel = default)
    {
        LABEL_RETRY:

        SocketEntry? sockEntry = null;

        while (true)
        {
            cancel.ThrowIfCancellationRequested();
            this.GrandCancel.ThrowIfCancellationRequested();

            lock (Lock)
            {
                if (this.AcceptedQueue.TryDequeue(out sockEntry))
                {
                    break;
                }
            }

            cancel.ThrowIfCancellationRequested();
            this.GrandCancel.ThrowIfCancellationRequested();

            await using (this.CreatePerTaskCancellationToken(out CancellationToken cancel2, cancel))
            {
                await AcceptedEvent.WaitOneAsync(cancel: cancel2);
            }
        }

        if (sockEntry.Socket.IsDisposed)
        {
            // すでに Dispose されていた
            sockEntry.DisconnectedEvent.Set(true);
            goto LABEL_RETRY;
        }

        return sockEntry.Socket;
    }
}

[Serializable]
[DataContract]
public class IpConnectionRateLimiterOptions : INormalizable
{
    [DataMember]
    public bool Common_Enabled { get; set; } = true;
    [DataMember]
    public bool Common_ReportDebugLog { get; set; } = true;

    [DataMember]
    public int Common_SrcIPv4SubnetLength { get; set; } = Consts.RateLimiter.DefaultSrcIPv4SubnetLength;
    [DataMember]
    public int Common_SrcIPv6SubnetLength { get; set; } = Consts.RateLimiter.DefaultSrcIPv6SubnetLength;
    [DataMember]
    public bool Common_SrcIPExcludeLocalNetwork { get; set; } = true;

    [DataMember]
    public double RateLimiter_Burst { get; set; } = Consts.RateLimiter.DefaultBurst;
    [DataMember]
    public double RateLimiter_LimitPerSecond { get; set; } = Consts.RateLimiter.DefaultLimitPerSecond;
    [DataMember]
    public int RateLimiter_ExpiresMsec { get; set; } = Consts.RateLimiter.DefaultExpiresMsec;
    [DataMember]
    public int RateLimiter_MaxEntries { get; set; } = Consts.RateLimiter.DefaultMaxEntries;
    [DataMember]
    public bool RateLimiter_EnablePenalty { get; set; } = true;
    [DataMember]
    public int RateLimiter_GcIntervalMsec { get; set; } = Consts.RateLimiter.DefaultGcIntervalMsec;

    [DataMember]
    public int ConcurrentLimiter_MaxConcurrentRequestsPerSrcSubnet { get; set; } = Consts.RateLimiter.DefaultMaxConcurrentRequestsPerSrcSubnet;

    public void Normalize()
    {
        if (Common_SrcIPv4SubnetLength <= 0 || Common_SrcIPv4SubnetLength > 32) Common_SrcIPv4SubnetLength = 24;
        if (Common_SrcIPv6SubnetLength <= 0 || Common_SrcIPv6SubnetLength > 128) Common_SrcIPv6SubnetLength = 56;

        if (RateLimiter_Burst <= 0.0) RateLimiter_Burst = Consts.RateLimiter.DefaultBurst;
        if (RateLimiter_LimitPerSecond <= 0.0) RateLimiter_LimitPerSecond = Consts.RateLimiter.DefaultLimitPerSecond;
        if (RateLimiter_ExpiresMsec <= 0) RateLimiter_ExpiresMsec = Consts.RateLimiter.DefaultExpiresMsec;
        if (RateLimiter_MaxEntries <= 0) RateLimiter_MaxEntries = Consts.RateLimiter.DefaultMaxEntries;
        if (RateLimiter_GcIntervalMsec <= 0) RateLimiter_GcIntervalMsec = Consts.RateLimiter.DefaultGcIntervalMsec;

        if (ConcurrentLimiter_MaxConcurrentRequestsPerSrcSubnet <= 0) ConcurrentLimiter_MaxConcurrentRequestsPerSrcSubnet = Consts.RateLimiter.DefaultMaxConcurrentRequestsPerSrcSubnet;
    }
}

public class IpConnectionRateLimiter
{
    readonly IpConnectionRateLimiterOptions Options;

    readonly RateLimiter<HashKeys.SingleIPAddress> RateLimiter;
    readonly ConcurrentLimiter<HashKeys.SingleIPAddress> ConcurrentLimiter;

    public string HiveName;

    public IpConnectionRateLimiter(string hiveName)
    {
        hiveName._NotEmptyCheck(nameof(hiveName));

        this.HiveName = hiveName;

        using var config = new HiveData<IpConnectionRateLimiterOptions>(Hive.SharedLocalConfigHive, $"NetworkSettings/IpConnectionRateLimiter/{hiveName}",
            () => new IpConnectionRateLimiterOptions(),
            policy: HiveSyncPolicy.None);

        lock (config.DataLock)
        {
            this.Options = config.ManagedData;
        }

        this.RateLimiter = new RateLimiter<HashKeys.SingleIPAddress>(new RateLimiterOptions(this.Options.RateLimiter_Burst, this.Options.RateLimiter_LimitPerSecond, this.Options.RateLimiter_ExpiresMsec,
            this.Options.RateLimiter_EnablePenalty ? RateLimiterMode.Penalty : RateLimiterMode.NoPenalty,
            this.Options.RateLimiter_MaxEntries,
            this.Options.RateLimiter_GcIntervalMsec));

        this.ConcurrentLimiter = new ConcurrentLimiter<HashKeys.SingleIPAddress>(this.Options.ConcurrentLimiter_MaxConcurrentRequestsPerSrcSubnet);

        if (this.Options.Common_ReportDebugLog)
        {
            this.RateLimiter.EventListener.RegisterCallback((a, b, c, d) =>
            {
                string? str = d as string;
                if (str._IsFilled()) Dbg.WriteLine($"IpConnectionRateLimiter[{this.HiveName}]: {str}");
            });

            this.ConcurrentLimiter.EventListener.RegisterCallback((a, b, c, d) =>
            {
                string? str = d as string;
                if (str._IsFilled()) Dbg.WriteLine($"IpConnectionRateLimiter[{this.HiveName}]: {str}");
            });
        }
    }

    public ResultOrError<IDisposable> TryEnter(IPAddress srcIp)
    {
        try
        {
            if (Options.Common_Enabled == false) return new EmptyDisposable();

            // Src IP の処理
            srcIp = srcIp._UnmapIPv4();

            if (this.Options.Common_SrcIPExcludeLocalNetwork)
            {
                // ローカルネットワークを除外する
                if (srcIp._GetIPAddressType()._IsLocalNetwork())
                {
                    return new EmptyDisposable();
                }
            }

            // サブネットマスクの AND をする
            if (srcIp.AddressFamily == AddressFamily.InterNetwork)
            {
                // IPv4
                srcIp = IPUtil.IPAnd(srcIp, IPUtil.IntToSubnetMask4(this.Options.Common_SrcIPv4SubnetLength));
            }
            else
            {
                // IPv6
                srcIp = IPUtil.IPAnd(srcIp, IPUtil.IntToSubnetMask6(this.Options.Common_SrcIPv6SubnetLength));
            }

            HashKeys.SingleIPAddress key = new HashKeys.SingleIPAddress(srcIp);

            // RateLimiter でチェック
            if (this.RateLimiter.TryInput(key, out _) == false)
            {
                // 失敗
                return false;
            }

            // 同時接続数検査
            if (this.ConcurrentLimiter.TryEnter(key, out _))
            {
                // 同時接続数 OK
                return new Holder<HashKeys.SingleIPAddress>(key2 =>
                {
                    // Dispose 時に同時接続数デクメリントを実施
                    this.ConcurrentLimiter.Exit(key, out _);
                },
                key,
                LeakCounterKind.IpConnectionRateLimiterTryEnterHolder);
            }
            else
            {
                // 失敗
                return false;
            }
        }
        catch (Exception ex)
        {
            ex._Debug();
            return false;
        }
    }
}

