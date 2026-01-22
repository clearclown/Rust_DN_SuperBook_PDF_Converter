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

#if CORES_BASIC_WEBAPP || CORES_BASIC_HTTPSERVER
#if CORES_BASIC_DAEMON

using System;
using System.Threading;
using System.Threading.Tasks;
using System.Text;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

namespace IPA.Cores.Basic;

public class HttpServerDaemon<TStartup> : Daemon where TStartup : class
{
    public HttpServerOptions HttpOptions { get; }

    AsyncService? HttpServerInstance = null;

    public HttpServerDaemon(string name, string friendlyName, HttpServerOptions httpOptions)
        : base(new DaemonOptions(name, friendlyName, true))
    {
        this.HttpOptions = httpOptions;
    }

    protected override async Task StartImplAsync(DaemonStartupMode startupMode, object? param)
    {
        Con.WriteLine($"{this.Options.FriendlyName}: Starting...");

        this.HttpServerInstance = new HttpServer<TStartup>(this.HttpOptions);

        await Task.CompletedTask;

        Con.WriteLine($"{this.Options.FriendlyName}: Started.");
    }

    protected override async Task StopImplAsync(object? param)
    {
        Con.WriteLine($"{this.Options.FriendlyName}: Stopping...");

        await HttpServerInstance!.DisposeWithCleanupAsync(new OperationCanceledException("The daemon is shutting down."));

        Con.WriteLine($"{this.Options.FriendlyName}: Stopped.");

        HttpServerInstance = null;
    }
}

public class HttpServerDualDaemon<TStartup> : Daemon where TStartup : class
{
    public HttpServerOptions HttpOptions1 { get; }
    public HttpServerOptions HttpOptions2 { get; }

    AsyncService? HttpServerInstance1 = null;
    AsyncService? HttpServerInstance2 = null;

    public HttpServerDualDaemon(string name, string friendlyName, HttpServerOptions httpOptions1, HttpServerOptions httpOptions2)
        : base(new DaemonOptions(name, friendlyName, true))
    {
        this.HttpOptions1 = httpOptions1;
        this.HttpOptions2 = httpOptions2;
    }

    protected override async Task StartImplAsync(DaemonStartupMode startupMode, object? param)
    {
        Con.WriteLine($"{this.Options.FriendlyName}: Starting...");

        this.HttpServerInstance1 = new HttpServer<TStartup>(this.HttpOptions1);

        this.HttpServerInstance2 = new HttpServer<TStartup>(this.HttpOptions2);

        await Task.CompletedTask;

        Con.WriteLine($"{this.Options.FriendlyName}: Started.");
    }

    protected override async Task StopImplAsync(object? param)
    {
        Con.WriteLine($"{this.Options.FriendlyName}: Stopping...");

        await HttpServerInstance1!.DisposeWithCleanupAsync(new OperationCanceledException("The daemon1 is shutting down."));

        await HttpServerInstance2!.DisposeWithCleanupAsync(new OperationCanceledException("The daemon2 is shutting down."));

        Con.WriteLine($"{this.Options.FriendlyName}: Stopped.");

        HttpServerInstance1 = null;
        HttpServerInstance2 = null;
    }
}

#endif  // CORES_BASIC_DAEMON
#endif  // CORES_BASIC_WEBAPP

