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

// Author: Daiyuu Nobori
// Description

#if true

#pragma warning disable CA2235 // Mark all non-serializable fields

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
using System.Runtime.CompilerServices;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Serialization;
using System.Net;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using System.Collections.Immutable;

namespace IPA.Cores.Basic;

public static partial class CoresConfig
{
    public static partial class DnsResolverDefaults
    {
        public static readonly Copenhagen<int> TimeoutOneQueryMsecs = 250;
        public static readonly Copenhagen<int> NumTry = 6;
        public static readonly Copenhagen<int> MinCacheTimeoutMsecs = 3600 * 1000;
        public static readonly Copenhagen<int> MaxCacheTimeoutMsecs = 6 * 3600 * 1000;
        public static readonly Copenhagen<int> ReverseLookupInternalCacheTimeoutMsecs = 3600 * 1000;
        public static readonly Copenhagen<int> ForwardLookupInternalCacheTimeoutMsecs = 3600 * 1000;
    }
}

[Flags]
public enum DnsResolverFlags : ulong
{
    None = 0,
    UseSystemDnsClientSettings = 1,
    UdpOnly = 2,
    TcpOnly = 4,
    RoundRobinServers = 8,
    DisableCache = 16,
    ThrowDnsError = 32,
    DisableRecursion = 64,

    Default = UseSystemDnsClientSettings | RoundRobinServers,
}

[Flags]
public enum DnsResolverQueryType
{
    A = 0,
    AAAA = 1,
}

public class DnsResolverSettings
{
    public TcpIpSystem TcpIp { get; }
    public DnsResolverFlags Flags { get; }
    public int TimeoutOneQuery { get; }
    public int NumTry { get; }
    public int MinCacheTimeout { get; }
    public int MaxCacheTimeout { get; }
    public IReadOnlyList<IPEndPoint> DnsServersList { get; }
    public int ReverseLookupIntervalCacheTimeoutMsecs { get; }
    public int ForwardLookupIntervalCacheTimeoutMsecs { get; }

    public DnsResolverSettings(TcpIpSystem? tcpIp = null, DnsResolverFlags flags = DnsResolverFlags.Default,
        int timeoutOneQuery = -1, int numTry = -1, int minCacheTimeout = -1, int maxCacheTimeout = -1, IEnumerable<IPEndPoint>? dnsServersList = null,
        int reverseLookupInternalCacheTimeoutMsecs = -1, int forwardLookupInternalCacheTimeoutMsecs = -1)
    {
        if (tcpIp == null) tcpIp = LocalNet;
        if (timeoutOneQuery <= 0) timeoutOneQuery = CoresConfig.DnsResolverDefaults.TimeoutOneQueryMsecs;
        if (numTry <= 0) numTry = CoresConfig.DnsResolverDefaults.NumTry;
        if (minCacheTimeout <= 0) minCacheTimeout = CoresConfig.DnsResolverDefaults.MinCacheTimeoutMsecs;
        if (maxCacheTimeout <= 0) maxCacheTimeout = CoresConfig.DnsResolverDefaults.MaxCacheTimeoutMsecs;
        if (reverseLookupInternalCacheTimeoutMsecs < 0) reverseLookupInternalCacheTimeoutMsecs = CoresConfig.DnsResolverDefaults.ReverseLookupInternalCacheTimeoutMsecs;
        if (forwardLookupInternalCacheTimeoutMsecs < 0) forwardLookupInternalCacheTimeoutMsecs = CoresConfig.DnsResolverDefaults.ForwardLookupInternalCacheTimeoutMsecs;

        this.TcpIp = tcpIp;
        this.Flags = flags;
        this.TimeoutOneQuery = timeoutOneQuery;
        this.NumTry = numTry;
        this.MinCacheTimeout = minCacheTimeout;
        this.MaxCacheTimeout = maxCacheTimeout;
        this.ReverseLookupIntervalCacheTimeoutMsecs = reverseLookupInternalCacheTimeoutMsecs;
        this.ForwardLookupIntervalCacheTimeoutMsecs = forwardLookupInternalCacheTimeoutMsecs;

        if (dnsServersList != null)
        {
            this.DnsServersList = dnsServersList.ToList();
        }
        else
        {
            this.DnsServersList = new List<IPEndPoint>();
        }
    }
}

public class DnsAdditionalResults
{
    public DnsAdditionalResults(bool isError, bool isNotFound, bool isCacheResult = false)
    {
        IsError = isError;
        IsNotFound = isNotFound;
        IsCacheResult = isCacheResult;
    }

    public bool IsError { get; }
    public bool IsNotFound { get; }
    public bool IsCacheResult { get; }

    public DnsAdditionalResults Clone(bool? isCacheResult = null)
    {
        return new DnsAdditionalResults(this.IsError, this.IsNotFound, isCacheResult == null ? this.IsCacheResult : isCacheResult.Value);
    }
}

public abstract class DnsResolver : AsyncService
{
    public abstract bool IsAvailable { get; }

    public DnsResolverSettings Settings { get; }

    protected abstract Task<IEnumerable<string>?> GetHostNameListImplAsync(IPAddress ip, Ref<DnsAdditionalResults>? additional = null, CancellationToken cancel = default);

    protected abstract Task<IEnumerable<IPAddress>?> GetIpAddressListImplAsync(string hostname, DnsResolverQueryType queryType, Ref<DnsAdditionalResults>? additional = null, CancellationToken cancel = default);

    class ForwardLookupCacheItem
    {
        public List<IPAddress>? Value { get; }
        public DnsAdditionalResults AdditionalResults { get; }

        public ForwardLookupCacheItem(List<IPAddress>? value, DnsAdditionalResults additionalResults)
        {
            Value = value;
            AdditionalResults = additionalResults;
        }
    }

    class ReverseLookupCacheItem
    {
        public List<string>? Value { get; }
        public DnsAdditionalResults AdditionalResults { get; }

        public ReverseLookupCacheItem(List<string>? value, DnsAdditionalResults additionalResults)
        {
            Value = value;
            AdditionalResults = additionalResults;
        }
    }

    readonly FastCache<string, ForwardLookupCacheItem> ForwardLookupCache;
    readonly FastCache<string, ReverseLookupCacheItem> ReverseLookupCache;

    public DnsResolver(DnsResolverSettings? setting = null)
    {
        try
        {
            if (setting == null) setting = new DnsResolverSettings();

            this.Settings = setting;

            this.ReverseLookupCache = new FastCache<string, ReverseLookupCacheItem>(this.Settings.ReverseLookupIntervalCacheTimeoutMsecs, comparer: StrComparer.IpAddressStrComparer);
            this.ForwardLookupCache = new FastCache<string, ForwardLookupCacheItem>(this.Settings.ForwardLookupIntervalCacheTimeoutMsecs, comparer: StrComparer.IgnoreCaseComparer);
        }
        catch
        {
            this._DisposeSafe();
            throw;
        }
    }

    // 逆引き
    public async Task<List<string>?> GetHostNameListAsync(string ip, Ref<DnsAdditionalResults>? additional = null, CancellationToken cancel = default, bool noCache = false)
    {
        try
        {
            return await GetHostNameListAsync(ip._ToIPAddress(), additional, cancel, noCache);
        }
        catch
        {
            return null;
        }
    }

    public async Task<List<string>?> GetHostNameListAsync(IPAddress? ip, Ref<DnsAdditionalResults>? additional = null, CancellationToken cancel = default, bool noCache = false)
    {
        if (noCache) return await GetHostNameListCoreAsync(ip, additional, cancel);

        if (ip == null) return null;

        var ipType = ip._GetIPAddressType();

        if (ipType.Bit(IPAddressType.Loopback))
        {
            return "localhost"._SingleList();
        }

        string ipStr = ip.ToString();

        RefBool found = new RefBool();

        ReverseLookupCacheItem? item = await this.ReverseLookupCache.GetOrCreateAsync(ipStr, async ipStr =>
        {
            Ref<DnsAdditionalResults> additionals = new Ref<DnsAdditionalResults>();

            List<string>? value = await GetHostNameListCoreAsync(ip, additionals, cancel);

            return new ReverseLookupCacheItem(value, additionals.Value ?? new DnsAdditionalResults(true, false));
        }, found);

        if (item == null)
        {
            additional?.Set(new DnsAdditionalResults(true, false, false));
            return null;
        }

        additional?.Set(item.AdditionalResults.Clone(true));

        return item.Value;
    }

    async Task<List<string>?> GetHostNameListCoreAsync(IPAddress? ip, Ref<DnsAdditionalResults>? additional = null, CancellationToken cancel = default)
    {
        try
        {
            if (ip == null) return null;

            var ipType = ip._GetIPAddressType();

            if (ipType.Bit(IPAddressType.Loopback))
            {
                return "localhost"._SingleList();
            }

            List<string>? ret = new List<string>();

            IEnumerable<string>? tmp = null;

            tmp = await GetHostNameListImplAsync(ip, additional, cancel);

            if (tmp != null)
            {
                foreach (string fqdn in tmp)
                {
                    string a = fqdn.Trim().TrimEnd('.');

                    if (a._IsFilled())
                    {
                        if (ret.Contains(a, StrComparer.IgnoreCaseComparer) == false)
                        {
                            ret.Add(a);
                        }
                    }
                }
            }

            if (ret.Any() == false)
            {
                ret = null;
            }

            return ret;
        }
        catch
        {
            return null;
        }
    }

    [Obsolete("Use GetHostNameOrIpAsync instead.")]
    public Task<string> GetHostNameSingleOrIpAsync(IPAddress? ip, CancellationToken cancel = default, bool noCache = false)
        => GetHostNameOrIpAsync(ip, cancel, noCache);

    [Obsolete("Use GetHostNameOrIpAsync instead.")]
    public Task<string> GetHostNameSingleOrIpAsync(string? ip, CancellationToken cancel = default, bool noCache = false)
        => GetHostNameOrIpAsync(ip, cancel, noCache);

    public async Task<string> GetHostNameOrIpAsync(IPAddress? ip, CancellationToken cancel = default, bool noCache = false)
    {
        if (ip == null) return "";

        try
        {
            var res = await GetHostNameListAsync(ip, null, cancel, noCache);

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
    public Task<string> GetHostNameOrIpAsync(string? ip, CancellationToken cancel = default, bool noCache = false)
        => GetHostNameOrIpAsync(ip._ToIPAddress(noExceptionAndReturnNull: true), cancel, noCache);


    // 正引き v4/v6 Dual Stack 対応
    public async Task<List<IPAddress>> GetIpAddressListDualStackAsync(string hostname, bool preferV6 = false, CancellationToken cancel = default, bool noCache = false)
    {
        try
        {
            ConcurrentBag<Tuple<IPAddress, int>> list = new();

            var queryTypeList = new DnsResolverQueryType[] { DnsResolverQueryType.A, DnsResolverQueryType.AAAA };

            await TaskUtil.ForEachAsync(int.MaxValue, queryTypeList, async (item, index, cancel) =>
            {
                var resultsList = await GetIpAddressListSingleStackAsync(hostname, item, null, cancel, noCache);

                if (resultsList != null)
                {
                    int i = 0;
                    foreach (var ip in resultsList.Distinct(IpComparer.Comparer))
                    {
                        list.Add(new Tuple<IPAddress, int>(ip, i));

                        i++;
                    }
                }
            }, cancel, 0);

            return list.OrderBy(x => (int)x.Item1.AddressFamily * (preferV6 ? -1 : 1)).Select(x => x.Item1).Distinct().ToList();
        }
        catch
        {
            return new List<IPAddress>(0);
        }
    }

    public async Task<IPAddress> GetIpAddressDualStackAsync(string hostname, bool preferV6 = false, CancellationToken cancel = default, bool noCache = false)
    {
        var res = await GetIpAddressListDualStackAsync(hostname, preferV6, cancel, noCache);

        if ((res?.Count ?? 0) >= 1)
        {
            return res![0];
        }
        else
        {
            throw new CoresException($"Hostname '{hostname}': DNS record not found.");
        }
    }

    // 正引き
    public async Task<List<IPAddress>?> GetIpAddressListSingleStackAsync(string hostname, DnsResolverQueryType queryType, Ref<DnsAdditionalResults>? additional = null, CancellationToken cancel = default, bool noCache = false)
    {
        hostname = Str.NormalizeFqdn(hostname);

        if (hostname._IsSamei("localhost") || hostname._IsSamei("localhost.localdomain"))
        {
            if (queryType == DnsResolverQueryType.AAAA)
                return IPAddress.IPv6Loopback._SingleList();
            else
                return null;
        }

        if (hostname._IsSamei("localhost6") || hostname._IsSamei("localhost6.localdomain6"))
        {
            if (queryType == DnsResolverQueryType.AAAA)
                return IPAddress.IPv6Loopback._SingleList();
            else
                return IPAddress.Loopback._SingleList();
        }

        if (noCache) return await GetIpAddressListSingleStackCoreAsync(hostname, queryType, additional, cancel);

        if (hostname._IsEmpty()) return null;

        AllowedIPVersions ipver;
        if (queryType == DnsResolverQueryType.A)
            ipver = AllowedIPVersions.IPv4;
        else
            ipver = AllowedIPVersions.IPv6;
        var ip = hostname._ToIPAddress(ipver, true);
        if (ip != null)
        {
            return ip._SingleList();
        }

        RefBool found = new RefBool();

        ForwardLookupCacheItem? item = await this.ForwardLookupCache.GetOrCreateAsync(hostname + "@" + queryType.ToString(), async ipStr =>
        {
            Ref<DnsAdditionalResults> additionals = new Ref<DnsAdditionalResults>();

            List<IPAddress>? value = await GetIpAddressListSingleStackCoreAsync(hostname, queryType, additionals, cancel);

            return new ForwardLookupCacheItem(value, additionals.Value ?? new DnsAdditionalResults(true, false));
        }, found);

        if (item == null)
        {
            additional?.Set(new DnsAdditionalResults(true, false, false));
            return null;
        }

        additional?.Set(item.AdditionalResults.Clone(true));

        return item.Value;
    }

    async Task<List<IPAddress>?> GetIpAddressListSingleStackCoreAsync(string hostname, DnsResolverQueryType queryType, Ref<DnsAdditionalResults>? additional = null, CancellationToken cancel = default)
    {
        try
        {
            List<IPAddress>? ret = new List<IPAddress>();

            IEnumerable<IPAddress>? tmp = null;

            HashSet<string> ipDuplicateChecker = new HashSet<string>(StrComparer.IpAddressStrComparer);

            tmp = await GetIpAddressListImplAsync(hostname, queryType, additional, cancel);

            if (tmp != null)
            {
                foreach (IPAddress ip in tmp)
                {
                    if (ipDuplicateChecker.Add(ip.ToString()))
                    {
                        ret.Add(ip);
                    }
                }
            }

            if (ret.Any() == false)
            {
                ret = null;
            }

            return ret;
        }
        catch
        {
            return null;
        }
    }

    public async Task<IPAddress> GetIpAddressSingleStackAsync(string hostname, DnsResolverQueryType queryType, CancellationToken cancel = default, bool noCache = false)
    {
        var res = await GetIpAddressListSingleStackAsync(hostname, queryType, null, cancel, noCache);

        if ((res?.Count ?? 0) >= 1)
        {
            return res![0];
        }
        else
        {
            throw new CoresException($"Hostname '{hostname}': DNS record for type '{queryType.ToString()}' not found.");
        }
    }

    public async Task<IPAddress> GetIpAddressAsync(string hostname, AllowedIPVersions ver = AllowedIPVersions.All, bool preferV6 = false, CancellationToken cancel = default, bool noCache = false)
    {
        if (!ver.Bit(AllowedIPVersions.IPv4) && !ver.Bit(AllowedIPVersions.IPv6))
        {
            throw new CoresException($"{nameof(ver)} must have either IPv4 or IPv6.");
        }

        if (ver.Bit(AllowedIPVersions.IPv4) && ver.Bit(AllowedIPVersions.IPv6))
        {
            return await this.GetIpAddressDualStackAsync(hostname, preferV6, cancel, noCache);
        }
        else
        {
            DnsResolverQueryType qtype = DnsResolverQueryType.A;
            if (ver.Bit(AllowedIPVersions.IPv6))
            {
                qtype = DnsResolverQueryType.AAAA;
            }

            return await this.GetIpAddressSingleStackAsync(hostname, qtype, cancel, noCache);
        }
    }

    // ヘルパーさん
    public static DnsResolver CreateDnsResolverIfSupported(DnsResolverSettings? settings)
    {
#if CORES_BASIC_MISC
        return new DnsClientLibBasedDnsResolver(settings);
#else // CORES_BASIC_MISC
            return new UnimplementedDnsResolver(settings);
#endif // CORES_BASIC_MISC
    }
}

public class UnimplementedDnsResolver : DnsResolver
{
    public override bool IsAvailable => false;

    protected override Task<IEnumerable<string>?> GetHostNameListImplAsync(IPAddress ip, Ref<DnsAdditionalResults>? additional = null, CancellationToken cancel = default)
    {
        throw new NotImplementedException();
    }

    protected override Task<IEnumerable<IPAddress>?> GetIpAddressListImplAsync(string hostname, DnsResolverQueryType queryType, Ref<DnsAdditionalResults>? additional = null, CancellationToken cancel = default)
    {
        throw new NotImplementedException();
    }
}

#endif

