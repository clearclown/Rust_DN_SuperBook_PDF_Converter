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

namespace IPA.SnmpWork
{
    class SnmpWorkDaemon : Daemon
    {
        SnmpWorkHost? host = null;

        public SnmpWorkDaemon() : base(new DaemonOptions("SnmpWork", "SnmpWork Service", true))
        {
        }

        protected override async Task StartImplAsync(DaemonStartupMode startupMode, object? param)
        {
            Con.WriteLine("SnmpWorkDaemon: Starting...");

            host = new SnmpWorkHost();

            await Task.CompletedTask;

            try
            {
                host.Register("Temperature", 101_00000, new SnmpWorkFetcherTemperature(host));
                host.Register("Ram", 102_00000, new SnmpWorkFetcherMemory(host));
                host.Register("Disk", 103_00000, new SnmpWorkFetcherDisk(host));
                host.Register("Net", 104_00000, new SnmpWorkFetcherNetwork(host));

                host.Register("Ping", 105_00000, new SnmpWorkFetcherPing(host));
                host.Register("Speed", 106_00000, new SnmpWorkFetcherSpeed(host));
                host.Register("Quality", 107_00000, new SnmpWorkFetcherPktQuality(host));
                host.Register("Bird", 108_00000, new SnmpWorkFetcherBird(host));

                host.RegisterSensors(109_00000);

                Con.WriteLine("SnmpWorkDaemon: Started.");
            }
            catch
            {
                await host._DisposeSafeAsync();
                host = null;
                throw;
            }
        }

        protected override async Task StopImplAsync(object? param)
        {
            Con.WriteLine("SnmpWorkDaemon: Stopping...");

            if (host != null)
            {
                await host.DisposeWithCleanupAsync();

                host = null;
            }

            Con.WriteLine("SnmpWorkDaemon: Stopped.");
        }
    }

    partial class SnmpWorkCommands
    {
        [ConsoleCommand(
            "Start or stop the SnmpWorkDaemon daemon",
            "SnmpWorkDaemon [command]",
            "Start or stop the SnmpWorkDaemon daemon",
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
        static int SnmpWorkDaemon(ConsoleService c, string cmdName, string str)
        {
            return DaemonCmdLineTool.EntryPoint(c, cmdName, str, new SnmpWorkDaemon(), new DaemonSettings());
        }

    }
}

