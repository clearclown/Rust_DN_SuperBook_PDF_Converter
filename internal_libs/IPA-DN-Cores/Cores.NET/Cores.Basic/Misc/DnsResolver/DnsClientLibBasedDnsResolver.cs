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

#if CORES_BASIC_MISC

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
using System.Net.Sockets;

using DnsClient;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

namespace IPA.Cores.Basic;

public class DnsClientLibBasedDnsResolver : DnsResolver
{
    public override bool IsAvailable => true;

    LookupClient Client { get; }

    public DnsClientLibBasedDnsResolver(DnsResolverSettings? settings = null) : base(settings)
    {
        try
        {
            LookupClientOptions opt;

            if (Settings.DnsServersList.Any())
            {
                opt = new LookupClientOptions(Settings.DnsServersList.ToArray());
            }
            else
            {
                opt = new LookupClientOptions();
            }

            var flags = Settings.Flags;

            opt.AutoResolveNameServers = flags.Bit(DnsResolverFlags.UseSystemDnsClientSettings);
            opt.UseTcpOnly = flags.Bit(DnsResolverFlags.TcpOnly);
            opt.UseTcpFallback = !flags.Bit(DnsResolverFlags.UdpOnly);
            opt.UseRandomNameServer = flags.Bit(DnsResolverFlags.RoundRobinServers);
            opt.UseCache = !flags.Bit(DnsResolverFlags.DisableCache);
            opt.ThrowDnsErrors = flags.Bit(DnsResolverFlags.ThrowDnsError);
            opt.Recursion = !flags.Bit(DnsResolverFlags.DisableRecursion);

            opt.Timeout = Settings.TimeoutOneQuery._ToTimeSpanMSecs();
            opt.Retries = Math.Max(Settings.NumTry, 1) - 1;

            opt.MinimumCacheTimeout = Settings.MinCacheTimeout._ToTimeSpanMSecs();
            opt.MaximumCacheTimeout = Settings.MaxCacheTimeout._ToTimeSpanMSecs();

            this.Client = new LookupClient(opt);
        }
        catch
        {
            this._DisposeSafe();
            throw;
        }
    }

    protected override async Task CleanupImplAsync(Exception? ex)
    {
        try
        {
        }
        catch
        {
            await base.CleanupImplAsync(ex);
        }
    }

    protected override async Task<IEnumerable<string>?> GetHostNameListImplAsync(IPAddress ip, Ref<DnsAdditionalResults>? additional = null, CancellationToken cancel = default)
    {
        DnsAdditionalResults additionalData = new DnsAdditionalResults(true, false);

        try
        {
            var res = await Client.QueryReverseAsync(ip, cancel);

            if (res.Header.ResponseCode == DnsHeaderResponseCode.NotExistentDomain)
            {
                additionalData = new DnsAdditionalResults(false, true);
            }

            if (res.HasError)
            {
                additionalData = new DnsAdditionalResults(true, false);
                return null;
            }

            return res.Answers?.PtrRecords().Select(x => x.PtrDomainName.ToString()).ToArray() ?? null;
        }
        finally
        {
            additional?.Set(additionalData);
        }
    }

    protected override async Task<IEnumerable<IPAddress>?> GetIpAddressListImplAsync(string hostname, DnsResolverQueryType queryType, Ref<DnsAdditionalResults>? additional = null, CancellationToken cancel = default)
    {
        DnsAdditionalResults additionalData = new DnsAdditionalResults(true, false);

        QueryType qt;

        switch (queryType)
        {
            case DnsResolverQueryType.A:
                qt = QueryType.A;
                break;

            case DnsResolverQueryType.AAAA:
                qt = QueryType.AAAA;
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(queryType));
        }

        try
        {
            var res = await Client.QueryAsync(hostname, qt, queryClass: QueryClass.IN, cancel);

            if (res.Header.ResponseCode == DnsHeaderResponseCode.NotExistentDomain)
            {
                additionalData = new DnsAdditionalResults(false, true);
            }

            if (res.HasError)
            {
                additionalData = new DnsAdditionalResults(true, false);
                return null;
            }

            if (qt == QueryType.A)
            {
                return res.Answers?.ARecords().Where(x => x.Address.AddressFamily == AddressFamily.InterNetwork).Select(x => x.Address).ToArray() ?? null;
            }
            else
            {
                return res.Answers?.AaaaRecords().Where(x => x.Address.AddressFamily == AddressFamily.InterNetworkV6).Select(x => x.Address).ToArray() ?? null;
            }
        }
        finally
        {
            additional?.Set(additionalData);
        }
    }

    public static void RunTestStartup()
    {
        new LookupClientOptions();
    }
}

#endif

