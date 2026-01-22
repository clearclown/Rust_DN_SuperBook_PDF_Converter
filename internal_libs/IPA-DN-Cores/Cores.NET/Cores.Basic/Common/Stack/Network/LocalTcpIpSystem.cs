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

namespace IPA.Cores.Basic;

public class LocalTcpIpSystemParam : TcpIpSystemParam
{
    public LocalTcpIpSystemParam(string name) : base(name) { }
}

public class LocalTcpIpSystem : TcpIpSystem
{
    class HostInfo : TcpIpSystemHostInfo
    {
        public override int InfoVersion { get; protected set; }
        public override string HostName { get; protected set; }
        public override string DomainName { get; protected set; }
        public override bool IsIPv4Supported { get; protected set; }
        public override bool IsIPv6Supported { get; protected set; }
        public override IReadOnlyList<IPAddress> IPAddressList { get; protected set; }

        public HostInfo(bool doNotStartBackground)
        {
            PalHostNetInfo data;

            if (doNotStartBackground == false)
            {
                // Background 定期的チェックを開始してそこから取得する
                // 本来軽量であるが、フルルート BGP ルータなどで動作すると大変重くなる
                var current = BackgroundState<PalHostNetInfo>.Current;
                this.InfoVersion = current.Version;
                data = current.Data._NullCheck();
            }
            else
            {
                // 単発で取得する
                this.InfoVersion = -1;
                data = BackgroundState<PalHostNetInfo>.GetOnce();
            }

            this.HostName = data.HostName;
            this.DomainName = data.DomainName;
            this.IsIPv4Supported = data.IsIPv4Supported;
            this.IsIPv6Supported = data.IsIPv6Supported;
            this.IPAddressList = data.IPAddressList ?? new List<IPAddress>();
        }
    }

    public static LocalTcpIpSystem Local { get; private set; } = null!;

    public static StaticModule Module { get; } = new StaticModule(ModuleInit, ModuleFree);

    static void ModuleInit()
    {
        Local = new LocalTcpIpSystem(new LocalTcpIpSystemParam("LocalTcpIp"));
    }

    static void ModuleFree()
    {
        Local._DisposeSafe();
        Local = null!;
    }


    protected new LocalTcpIpSystemParam Param => (LocalTcpIpSystemParam)base.Param;

    private LocalTcpIpSystem(LocalTcpIpSystemParam param) : base(param)
    {
    }

    static readonly CachedProperty<TcpIpSystemHostInfo> CachedSystemHostInfo = new CachedProperty<TcpIpSystemHostInfo>(getter: () => new HostInfo(true), expiresLifeTimeMsecs: CoresConfig.TcpIpSystemSettings.LocalHostHostInfoCacheLifetime);

    protected override TcpIpSystemHostInfo GetHostInfoImpl(bool doNotStartBackground)
    {
        if (doNotStartBackground == false || Env.IsWindows)
        {
            // Windows の場合は、doNotStartBackground が true でもバックグラウンドを起動する。
            // これは、UNIX (Linux) と異なり、パフォーマンス上の問題がないためである。
            return new HostInfo(false);
        }
        else
        {
            return CachedSystemHostInfo;
        }
    }

    protected override int RegisterHostInfoChangedEventImpl(AsyncAutoResetEvent ev)
    {
        return BackgroundState<PalHostNetInfo>.EventListener.RegisterAsyncEvent(ev);
    }

    protected override void UnregisterHostInfoChangedEventImpl(int registerId)
    {
        BackgroundState<PalHostNetInfo>.EventListener.UnregisterAsyncEvent(registerId);
    }

    protected override NetTcpProtocolStubBase CreateTcpProtocolStubImpl(TcpConnectParam param, CancellationToken cancel)
    {
        NetPalTcpProtocolStub tcp = new NetPalTcpProtocolStub(cancel: cancel);

        return tcp;
    }

    protected override NetUdpListener CreateUdpListenerImpl(NetUdpListenerOptions options)
    {
        return new NetPalUdpListener(options);
    }

    protected override NetTcpListener CreateTcpListenerImpl(NetTcpListenerAcceptedProcCallback acceptedProc, string? rateLimiterConfigName = null)
    {
        NetPalTcpListener ret = new NetPalTcpListener(acceptedProc, rateLimiterConfigName);

        return ret;
    }

    protected override DnsResolver CreateDnsResolverImpl()
    {
#if CORES_BASIC_MISC
        return new DnsClientLibBasedDnsResolver();
#else // CORES_BASIC_MISC
            return new UnimplementedDnsResolver();
#endif // CORES_BASIC_MISC
    }

    protected override async Task<DnsResponse> QueryDnsImplAsync(DnsQueryParamBase param, CancellationToken cancel)
    {
        switch (param)
        {
            case DnsGetIpQueryParam getIpQuery:
                if (IPAddress.TryParse(getIpQuery.Hostname, out IPAddress? ip))
                    return new DnsResponse(param, ip._SingleArray());
                else
                    return new DnsResponse(param, await PalDns.GetHostAddressesAsync(getIpQuery.Hostname, getIpQuery.Timeout, cancel));

            case DnsGetFqdnQueryParam getFqdnQuery:
                var res = await PalDns.GetHostEntryAsync(getFqdnQuery.Ip._UnmapIPv4(), getFqdnQuery.Timeout, cancel);

                List<string> hostNameList = new List<string>();

                hostNameList.Add(res.HostName);
                res.Aliases._DoForEach(x => hostNameList.Add(x));

                hostNameList.Distinct();

                return new DnsResponse(param, hostNameList);
        }

        throw new NotImplementedException();
    }

    public TcpIpHostDataJsonSafe GetTcpIpHostDataJsonSafe(bool once) => new TcpIpHostDataJsonSafe(EnsureSpecial.Yes, once);

    protected override Task<SendPingReply> SendPingImplAsync(IPAddress target, byte[] data, int timeout, CancellationToken cancel)
    {
        // 現時点で SendAsync メソッドはキャンセルや厳密なタイムアウトを実現していないので DoAsyncWithTimeout() を用いて無理矢理実現する
        return TaskUtil.DoAsyncWithTimeout((c) =>
        {
            return Legacy.SendPing.SendAsync(target, data, timeout);
        }, timeout: timeout + 1000, cancel: cancel);
    }
}

