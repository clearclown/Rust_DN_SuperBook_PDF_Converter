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

public static partial class CoresConfig
{
    public static partial class GetMyIpClientSettings
    {
        public static readonly Copenhagen<int> NumRetry = 3;
        public static readonly Copenhagen<int> Timeout = 5 * 1000;
    }
}

public class GetMyIpInfoResult
{
    public IPAddress GlobalIpAddress { get; }
    public int GlobalPort { get; }
    public string GlobalFqdn { get; }
    public IPAddress PrivateIpAddress { get; }

    public GetMyIpInfoResult(IPAddress globalIp, int port, string fqdn, IPAddress privateIp)
    {
        this.GlobalIpAddress = globalIp;
        this.GlobalPort = port;
        this.GlobalFqdn = fqdn;
        this.PrivateIpAddress = privateIp;
    }
}

public static class GetMyPrivateIpNativeUtil
{
    public static async Task<IPAddress> GetMyPrivateIpAsync(IPVersion ver = IPVersion.IPv4, CancellationToken cancel = default)
    {
        string host;

        if (ver == IPVersion.IPv6)
        {
            host = "connect-v6.arpanet.jp.";
        }
        else
        {
            host = "connect-v4.arpanet.jp.";
        }

        try
        {
            return await RetryHelper.RunAsync(async () =>
            {
                using TcpClient tcp = new TcpClient();
                tcp.ReceiveTimeout = 5000;
                tcp.SendTimeout = 5000;
                await tcp.ConnectAsync(host, 80);
                var stream = tcp.GetStream();
                var socket = stream.Socket;
                IPEndPoint ep = (IPEndPoint)socket.LocalEndPoint!;
                var addr = ep.Address._UnmapIPv4();
                if ((ver == IPVersion.IPv4 && addr.AddressFamily == AddressFamily.InterNetwork) ||
                    (ver == IPVersion.IPv6 && addr.AddressFamily == AddressFamily.InterNetworkV6))
                {
                    return addr;
                }
                else
                {
                    throw new CoresLibException("IP address family invalid.");
                }
            }, 200, 3, cancel, true);
        }
        catch
        {
            return IPAddress.Loopback;
        }
    }
}

public class GetMyIpClient : AsyncService
{
    public TcpIpSystem TcpIp;
    WebApi Web;

    public GetMyIpClient(TcpIpSystem? tcpIp = null, bool useNativeLibrary = false)
    {
        try
        {
            this.TcpIp = tcpIp ?? LocalNet;
            this.Web = new WebApi(new WebApiOptions(new WebApiSettings { SslAcceptAnyCerts = true, UseProxy = false, Timeout = CoresConfig.GetMyIpClientSettings.Timeout }, this.TcpIp, useNativeLibrary));
        }
        catch
        {
            this._DisposeSafe();
            throw;
        }
    }

    public async Task<GetMyIpInfoResult> GetMyIpInfoAsync(IPVersion ver = IPVersion.IPv4, CancellationToken cancel = default)
    {
        string url;

        if (ver == IPVersion.IPv4)
            url = Consts.Urls.GetMyIpUrl_IPv4;
        else
            url = Consts.Urls.GetMyIpUrl_IPv6;

        url += "?port=true&fqdn=true";

        Exception error = new CoresLibException("Unknown Error");

        GetMyIpInfoResult? ret = null;

        for (int i = 0; i < CoresConfig.GetMyIpClientSettings.NumRetry; i++)
        {
            try
            {
                WebRet webRet = await Web.SimpleQueryAsync(WebMethods.GET, url, cancel);

                string str = webRet.Data._GetString_UTF8();

                IPAddress? ip = null;
                string fqdn = "";
                int port = 0;

                foreach (string line in str._GetLines(true))
                {
                    if (line._GetKeyAndValue(out string key, out string value, "="))
                    {
                        key = key.ToUpperInvariant();

                        switch (key)
                        {
                            case "IP":
                                ip = value._NonNullTrim()._ToIPAddress(ver == IPVersion.IPv4 ? AllowedIPVersions.IPv4 : AllowedIPVersions.IPv6);
                                break;

                            case "FQDN":
                                fqdn = value._NonNullTrim();
                                break;

                            case "PORT":
                                port = value._NonNullTrim()._ToInt();
                                break;
                        }
                    }
                }

                if (ip != null && port != 0)
                {
                    if (fqdn._IsEmpty()) fqdn = ip.ToString();
                    ret = new GetMyIpInfoResult(ip, port, fqdn, null!);
                    break;
                }
                else
                {
                    throw new ApplicationException($"Invalid GetMyIp Server reply str: \"{str}\"");
                }
            }
            catch (Exception ex)
            {
                error = ex;
            }
        }

        if (ret == null)
        {
            throw error!;
        }

        return ret;
    }

    public async Task<IPAddress> GetMyIpAsync(IPVersion ver = IPVersion.IPv4, CancellationToken cancel = default)
    {
        string url;

        if (ver == IPVersion.IPv4)
            url = Consts.Urls.GetMyIpUrl_IPv4;
        else
            url = Consts.Urls.GetMyIpUrl_IPv6;

        Exception error = new CoresLibException("Unknown Error");

        IPAddress? ret = null;

        for (int i = 0; i < CoresConfig.GetMyIpClientSettings.NumRetry; i++)
        {
            try
            {
                WebRet webRet = await Web.SimpleQueryAsync(WebMethods.GET, url, cancel);

                string str = webRet.Data._GetString_Ascii()._OneLine("");

                if (str.StartsWith("IP=", StringComparison.OrdinalIgnoreCase))
                {
                    ret = IPAddress.Parse(str.Substring(3));
                    break;
                }
                else
                {
                    throw new ApplicationException($"Invalid IP str: \"{str}\"");
                }
            }
            catch (Exception ex)
            {
                error = ex;
            }
        }

        if (ret == null)
        {
            throw error!;
        }

        return ret;
    }

    protected override async Task CleanupImplAsync(Exception? ex)
    {
        try
        {
            await this.Web._DisposeSafeAsync();
        }
        finally
        {
            await base.CleanupImplAsync(ex);
        }
    }

    public static async Task<IPAddress> GetMyIpSimpleAsync(IPVersion ver = IPVersion.IPv4, CancellationToken cancel = default)
    {
        await using GetMyIpClient client = new GetMyIpClient(null, true);

        return await client.GetMyIpAsync(ver, cancel);
    }
}
