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
using System.IO;
using System.Runtime.Serialization;

namespace IPA.Cores.Basic;

public class TcpServerOptions
{
    public IReadOnlyList<IPEndPoint> EndPoints { get; }
    public TcpIpSystem TcpIp { get; }
    public string? RateLimiterConfigName { get; }

    public TcpServerOptions(TcpIpSystem? tcpIp, string? rateLimiterConfigName = null, params IPEndPoint[] endPoints)
    {
        this.TcpIp = tcpIp ?? LocalNet;
        this.EndPoints = endPoints.ToList();
        this.RateLimiterConfigName = rateLimiterConfigName;
    }
}

public abstract class TcpServerBase : AsyncService
{
    protected TcpServerOptions Options { get; }

    NetTcpListener Listener;

    public TcpServerBase(TcpServerOptions options)
    {
        try
        {
            this.Options = options;

            Listener = this.Options.TcpIp.CreateTcpListener(new TcpListenParam(ListenerCallbackAsync, options.RateLimiterConfigName, this.Options.EndPoints.ToArray()));
        }
        catch
        {
            this._DisposeSafe();
            throw;
        }
    }

    protected abstract Task TcpAcceptedImplAsync(NetTcpListenerPort listener, ConnSock sock);

    async Task ListenerCallbackAsync(NetTcpListenerPort listener, ConnSock newSock)
    {
        await TcpAcceptedImplAsync(listener, newSock);
    }

    protected override async Task CleanupImplAsync(Exception? ex)
    {
        try
        {
            await this.Listener._DisposeSafeAsync();
        }
        finally
        {
            await base.CleanupImplAsync(ex);
        }
    }
}

public class SslServerOptions : TcpServerOptions
{
    public PalSslServerAuthenticationOptions SslServerAuthenticationOptions { get; }

    public SslServerOptions(TcpIpSystem? tcpIp, PalSslServerAuthenticationOptions sslAuthOptions, string? rateLimiterConfigName = null, params IPEndPoint[] endPoints) : base(tcpIp, rateLimiterConfigName, endPoints)
    {
        this.SslServerAuthenticationOptions = sslAuthOptions;
    }
}

public abstract class SslServerBase : TcpServerBase
{
    protected new SslServerOptions Options => (SslServerOptions)base.Options;

    public SslServerBase(SslServerOptions options) : base(options)
    {
    }

    protected abstract Task SslAcceptedImplAsync(NetTcpListenerPort listener, SslSock sock);

    protected sealed override async Task TcpAcceptedImplAsync(NetTcpListenerPort listener, ConnSock s)
    {
        await using (SslSock ssl = new SslSock(s))
        {
            await ssl.StartSslServerAsync(this.Options.SslServerAuthenticationOptions, s.GrandCancel);

            ssl.UpdateSslSessionInfo();

            await SslAcceptedImplAsync(listener, ssl);
        }
    }
}
