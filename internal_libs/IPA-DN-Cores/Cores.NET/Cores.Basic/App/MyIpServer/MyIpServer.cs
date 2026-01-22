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
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Serialization;
using System.Net;
using System.Net.Sockets;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

namespace IPA.Cores.Basic;

public class MyIpServerSettings : INormalizable
{
    public List<string> DnsServerList { get; set; } = new List<string>();
    public int HttpTimeoutMsecs { get; set; }

    public void Normalize()
    {
        if (this.DnsServerList.Count == 0)
        {
            this.DnsServerList.Add("8.8.8.8");
            this.DnsServerList.Add("1.1.1.1");
        }

        if (this.HttpTimeoutMsecs <= 0)
        {
            this.HttpTimeoutMsecs = 10 * 1000;
        }
    }
}

public class MyIpServerCgiHandler : CgiHandlerBase
{
    public readonly MyIpServerHost Host;

    public MyIpServerCgiHandler(MyIpServerHost host)
    {
        this.Host = host;
    }

    protected override void InitActionListImpl(CgiActionList noAuth, CgiActionList reqAuth)
    {
        try
        {
            noAuth.AddAction("{*path}", WebMethodBits.GET | WebMethodBits.HEAD, async (ctx) =>
            {
                return new HttpStringResult((await Host.GetResponseAsync(ctx, ctx.Cancel))._NormalizeCrlf(CrlfStyle.Lf), contentType: Consts.MimeTypes.Text);
            });
        }
        catch
        {
            this._DisposeSafe();
            throw;
        }
    }
}

public class MyIpServerHost : AsyncService
{
    readonly HiveData<MyIpServerSettings> SettingsHive;

    // 'Config\MyIpServer' のデータ
    public MyIpServerSettings Settings => SettingsHive.GetManagedDataSnapshot();

    readonly CriticalSection LockList = new CriticalSection<SnmpWorkHost>();

    readonly CgiHttpServer Cgi;

    readonly DnsResolver Dns;

    public MyIpServerHost()
    {
        try
        {
            // Settings を読み込む
            this.SettingsHive = new HiveData<MyIpServerSettings>(Hive.SharedLocalConfigHive, $"MyIpServer", null, HiveSyncPolicy.AutoReadFromFile);

            List<IPEndPoint> dnsServers = new List<IPEndPoint>();

            foreach (var host in this.Settings.DnsServerList)
            {
                var ep = host._ToIPEndPoint(53, allowed: AllowedIPVersions.IPv4, true);
                if (ep != null)
                {
                    dnsServers.Add(ep);
                }
            }

            if (dnsServers.Count == 0)
                throw new CoresLibException("dnsServers.Count == 0");

            this.Dns = new DnsClientLibBasedDnsResolver(
                new DnsResolverSettings(
                    flags: DnsResolverFlags.RoundRobinServers | DnsResolverFlags.UdpOnly,
                    dnsServersList: dnsServers
                    )
                );

            // HTTP サーバーを立ち上げる
            this.Cgi = new CgiHttpServer(new MyIpServerCgiHandler(this), new HttpServerOptions()
            {
                AutomaticRedirectToHttpsIfPossible = false,
                UseKestrelWithIPACoreStack = false,
                HttpPortsList = new int[] { 80, 992 }.ToList(),
                HttpsPortsList = new int[] { 443 }.ToList(),
                UseStaticFiles = false,
                MaxRequestBodySize = 32 * 1024,
                ReadTimeoutMsecs = this.Settings.HttpTimeoutMsecs,
            },
            true);
        }
        catch
        {
            this._DisposeSafe();
            throw;
        }
    }

    public async Task<string> GetResponseAsync(CgiContext ctx, CancellationToken cancel = default)
    {
        // Query string の解析
        bool port = ctx.QueryString._GetStrFirst("port")._ToBool();
        bool fqdn = ctx.QueryString._GetStrFirst("fqdn")._ToBool();
        bool verifyfqdn = ctx.QueryString._GetStrFirst("verifyfqdn")._ToBool();

        StringWriter w = new StringWriter();

        IPAddress clientIp = ctx.ClientIpAddress;

        string proxySrcIpStr = ctx.Request.Headers._GetStrFirst("x-proxy-srcip");
        var proxySrcIp = proxySrcIpStr._ToIPAddress(noExceptionAndReturnNull: true);
        if (proxySrcIp != null)
        {
            clientIp = proxySrcIp;
        }

        if (port == false && fqdn == false)
        {
            // 従来のサーバーとの互換性を維持するため改行を入れません !!
            return $"IP={clientIp.ToString()}";
        }

        w.WriteLine($"IP={clientIp.ToString()}");

        if (port)
        {
            w.WriteLine($"PORT={ctx.ClientPort}");
        }

        if (fqdn)
        {
            string hostname = clientIp.ToString();
            try
            {
                var ipType = clientIp._GetIPAddressType();
                if (ipType.BitAny(IPAddressType.IPv4_IspShared | IPAddressType.Loopback | IPAddressType.Zero | IPAddressType.Multicast | IPAddressType.LocalUnicast))
                {
                    // ナーシ
                }
                else
                {
                    hostname = await this.Dns.GetHostNameOrIpAsync(clientIp, cancel);

                    if (verifyfqdn)
                    {
                        try
                        {
                            var ipList = await this.Dns.GetIpAddressListSingleStackAsync(hostname,
                                clientIp.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 ? DnsResolverQueryType.AAAA : DnsResolverQueryType.A,
                                cancel: cancel);

                            if ((ipList?.Any(x => IpComparer.Comparer.Equals(x, clientIp)) ?? false) == false)
                            {
                                // NG
                                hostname = ctx.ClientIpAddress.ToString();
                            }
                        }
                        catch
                        {
                            // NG
                            hostname = ctx.ClientIpAddress.ToString();
                        }
                    }
                }
            }
            catch { }
            w.WriteLine($"FQDN={hostname}");
        }

        return w.ToString();
    }

    protected override async Task CleanupImplAsync(Exception? ex)
    {
        try
        {
            await this.Cgi._DisposeSafeAsync();

            await this.Dns._DisposeSafeAsync();

            this.SettingsHive._DisposeSafe();
        }
        finally
        {
            await base.CleanupImplAsync(ex);
        }
    }
}

#endif

