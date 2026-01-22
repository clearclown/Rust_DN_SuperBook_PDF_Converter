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
using System.IO.Enumeration;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

#pragma warning disable CS0162
#pragma warning disable CS0219

namespace IPA.TestDev;

class MikakaDDnsServerDaemon : Daemon
{
    MikakaDDnsService? SvcInstance = null;
    EasyJsonRpcServer<MikakaDDnsService.IRpc>? RpcInstance = null;

    public MikakaDDnsServerDaemon() : base(new DaemonOptions("MikakaDDnsServer", "Mikaka DDNS Server Service", true))
    {
    }

    protected override async Task StartImplAsync(DaemonStartupMode startupMode, object? param)
    {
        Con.WriteLine("MikakaDDnsServerDaemon: Starting...");

        MikakaDDnsServiceGlobal.Init();

        HttpServerOptions httpOpt = new HttpServerOptions
        {
            AutomaticRedirectToHttpsIfPossible = false,
            DenyRobots = true,
            DebugKestrelToConsole = true,
            DebugKestrelToLog = true,
            HttpPortsList = new int[] { 80, 88 }.ToList(),
            HttpsPortsList = new int[] { 443 }.ToList(),
            UseKestrelWithIPACoreStack = false,
        };

        MikakaDDnsServiceStartupParam startup = new MikakaDDnsServiceStartupParam
        {
        };

        this.SvcInstance = new MikakaDDnsService(startup, new MikakaDDnsServiceHook());

        JsonRpcServerConfig rpcConfig = new JsonRpcServerConfig
        {
            MaxRequestBodyLen = 1_000_000,
            EnableBuiltinRichWebPages = true,
            EnableGetMyIpServer = true,
            EnableHealthCheckServer = true,
            TopPageRedirectToControlPanel = true,
            HadbBasedServicePoint = SvcInstance,
        };

        this.RpcInstance = new EasyJsonRpcServer<MikakaDDnsService.IRpc>(httpOpt, rpcCfg: rpcConfig, targetObject: SvcInstance);

        SvcInstance.Start();

        await Task.CompletedTask;

        Con.WriteLine("MikakaDDnsServerDaemon: Started.");
    }

    protected override async Task StopImplAsync(object? param)
    {
        Con.WriteLine("MikakaDDnsServerDaemon: Stopping...");

        await RpcInstance._DisposeSafeAsync();

        await SvcInstance._DisposeSafeAsync();

        Con.WriteLine("MikakaDDnsServerDaemon: Stopped.");
    }
}

partial class TestDevCommands
{
    [ConsoleCommand(
        "Start or stop the MikakaDDnsServerDaemon daemon",
        "MikakaDDnsServerDaemon [command]",
        "Start or stop the MikakaDDnsServerDaemon daemon",
        @"[command]:The control command.

[UNIX / Windows common commands]
start        - Start the daemon in the background mode.
stop         - Stop the running daemon in the background mode.
show         - Show the real-time log by the background daemon.
test         - Start the daemon in the foreground testing mode.

[Windows specific commands]
winstart     - Start the daemon as a Windows service.
winstop      - Stop the running daemon as a Windows service.
wininstall   - Install the daemon as a Windows service.
winuninstall - Uninstall the daemon as a Windows service.")]
    static int MikakaDDnsServerDaemon(ConsoleService c, string cmdName, string str)
    {
        return DaemonCmdLineTool.EntryPoint(c, cmdName, str, new MikakaDDnsServerDaemon(), new DaemonSettings());
    }

}

