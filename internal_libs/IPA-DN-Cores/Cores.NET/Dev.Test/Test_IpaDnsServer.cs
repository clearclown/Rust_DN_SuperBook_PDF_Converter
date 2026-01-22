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
using System.Net;

#pragma warning disable CS0162
#pragma warning disable CS0219

namespace IPA.TestDev;

class IpaDnsServerDaemon : Daemon
{
    IpaDnsService? SvcInstance = null;
    EasyJsonRpcServer<IpaDnsService.IRpc>? RpcInstance = null;

    public class MyHook : IpaDnsServiceHook
    {
        public class DN_Plugin_LB1_Responder : IpaDnsServiceDynamicResponderBase
        {
            List<IPAddress> IPv4List = new List<IPAddress>();
            List<IPAddress> IPv6List = new List<IPAddress>();

            public DN_Plugin_LB1_Responder(IpaDnsServiceDynamicResponderInitParam initParam) : base(initParam)
            {
                HashSet<IPAddress> ipv4Dict = new HashSet<IPAddress>(IpComparer.ComparerWithIgnoreScopeId);
                HashSet<IPAddress> ipv6Dict = new HashSet<IPAddress>(IpComparer.ComparerWithIgnoreScopeId);

                var vars = initParam.CustomRecordDef.DynamicRecordParamsList!._GetFirstValueOrDefault("iplist")._ParseQueryString(splitChar: ',', trimKeyAndValue: true);

                foreach (var kv in vars)
                {
                    var ip = kv.Key._ToIPAddress(AllowedIPVersions.IPv4, true);
                    if (ip != null)
                    {
                        ipv4Dict.Add(ip);
                    }
                }

                this.IPv4List = ipv4Dict.OrderBy(x => x, IpComparer.ComparerWithIgnoreScopeId).ToList();

                foreach (var kv in vars)
                {
                    var ip = kv.Key._ToIPAddress(AllowedIPVersions.IPv6, true);
                    if (ip != null)
                    {
                        ipv6Dict.Add(ip);
                    }
                }

                this.IPv6List = ipv6Dict.OrderBy(x => x, IpComparer.ComparerWithIgnoreScopeId).ToList();
            }

            public override bool ResolveImpl(IpaDnsServiceDynamicResponderCallParam param)
            {
                var e = param.EditMe;

                int ipv4Count = this.IPv4List.Count;

                if (ipv4Count >= 1)
                {
                    bool ok = false;
                    uint hash = 0;

                    string label = param.Request.RequestHostName;
                    string[] labels = label.Split(".", StringSplitOptions.RemoveEmptyEntries);

                    if (labels.Length >= 2 && labels[0].StartsWith("x") && labels[1].StartsWith("x"))
                    {
                        // x1.x2.aaa.bbb 形式
                        string hashSeed = labels[0] + "." + labels[1];
                        hash = (uint)Secure.HashSHA1AsSInt31(hashSeed._GetBytes_Ascii());
                        ok = true;
                    }
                    else if (labels.Length >= 1)
                    {
                        // num.aaa.bbb 形式
                        if (int.TryParse(labels[0], out int intValue))
                        {
                            if (intValue >= 0 && intValue < ipv4Count)
                            {
                                hash = (uint)intValue;
                                ok = true;
                            }
                        }
                    }

                    if (ok)
                    {
                        var ip = this.IPv4List[(int)(hash % ipv4Count)];

                        e.IPAddressList!.Add(ip);
                    }
                }


                int ipv6Count = this.IPv6List.Count;

                if (ipv6Count >= 1)
                {
                    bool ok = false;
                    uint hash = 0;

                    string label = param.Request.RequestHostName;
                    string[] labels = label.Split(".", StringSplitOptions.RemoveEmptyEntries);

                    if (labels.Length >= 2 && labels[0].StartsWith("x") && labels[1].StartsWith("x"))
                    {
                        // x1.x2.aaa.bbb 形式
                        string hashSeed = labels[0] + "." + labels[1];
                        hash = (uint)Secure.HashSHA1AsSInt31(hashSeed._GetBytes_Ascii());
                        ok = true;
                    }
                    else if (labels.Length >= 1)
                    {
                        // num.aaa.bbb 形式
                        if (int.TryParse(labels[0], out int intValue))
                        {
                            if (intValue >= 0 && intValue < ipv6Count)
                            {
                                hash = (uint)intValue;
                                ok = true;
                            }
                        }
                    }

                    if (ok)
                    {
                        var ip = this.IPv6List[(int)(hash % ipv6Count)];

                        e.IPAddressList!.Add(ip);
                    }
                }

                return e.IPAddressList!.Any();
            }
        }

        public override void InitDynamicResponderFactoryList(Dictionary<string, IpaDnsServiceDynamicResponderFactory> list)
        {
            list.Add("DN_Plugin_LB1", p => new DN_Plugin_LB1_Responder(p));
        }
    }

    public IpaDnsServerDaemon() : base(new DaemonOptions("IpaDnsServer", "IPA DNS Server Service", true))
    {
    }

    protected override async Task StartImplAsync(DaemonStartupMode startupMode, object? param)
    {
        Con.WriteLine("IpaDnsServerDaemon: Starting...");

        IpaDnsServiceGlobal.Init();

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

        IpaDnsServiceStartupParam startup = new IpaDnsServiceStartupParam
        {
            HadbSystemName = "IPA_DNS",
        };

        this.SvcInstance = new IpaDnsService(startup, new MyHook());

        JsonRpcServerConfig rpcConfig = new JsonRpcServerConfig
        {
            MaxRequestBodyLen = 1_000_000,
            EnableBuiltinRichWebPages = true,
            EnableGetMyIpServer = true,
            EnableHealthCheckServer = true,
            TopPageRedirectToControlPanel = true,
            HadbBasedServicePoint = SvcInstance,
        };

        this.RpcInstance = new EasyJsonRpcServer<IpaDnsService.IRpc>(httpOpt, rpcCfg: rpcConfig, targetObject: SvcInstance);

        SvcInstance.Start();

        await Task.CompletedTask;

        Con.WriteLine("IpaDnsServerDaemon: Started.");
    }

    protected override async Task StopImplAsync(object? param)
    {
        Con.WriteLine("IpaDnsServerDaemon: Stopping...");

        await RpcInstance._DisposeSafeAsync();

        await SvcInstance._DisposeSafeAsync();

        Con.WriteLine("IpaDnsServerDaemon: Stopped.");
    }
}

partial class TestDevCommands
{
    [ConsoleCommand(
        "Start or stop the IpaDnsServerDaemon daemon",
        "IpaDnsServerDaemon [command]",
        "Start or stop the IpaDnsServerDaemon daemon",
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
    static int IpaDnsServerDaemon(ConsoleService c, string cmdName, string str)
    {
        return DaemonCmdLineTool.EntryPoint(c, cmdName, str, new IpaDnsServerDaemon(), new DaemonSettings());
    }

}

