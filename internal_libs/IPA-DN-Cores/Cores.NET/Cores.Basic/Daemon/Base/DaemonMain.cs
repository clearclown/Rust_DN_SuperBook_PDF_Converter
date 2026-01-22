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

#if CORES_BASIC_DAEMON

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Diagnostics;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

namespace IPA.Cores.Basic;

public enum DaemonStartupMode
{
    ForegroundTestMode = 0,
    BackgroundServiceMode,
}

public static partial class StandardMainFunctions
{
    public static class DaemonMain
    {
        static Func<Daemon> GetDaemonProc = null!;
        static DaemonSettings DefaultDaemonSettings = null!;

        public static int DoMain(CoresLibOptions coresOptions, string[] args, Func<Daemon> getDaemonProc, DaemonSettings? defaultDaemonSettings = null)
        {
            DaemonMain.DefaultDaemonSettings = defaultDaemonSettings ?? new DaemonSettings();

            DaemonMain.GetDaemonProc = getDaemonProc ?? throw new ArgumentNullException("getDaemonProc");

            CoresLib.Init(coresOptions, args);

            try
            {
                return ConsoleService.EntryPoint(Env.CommandLine, "Daemon", typeof(DaemonMain));
            }
            finally
            {
                if (CoresLib.Free()?.LeakCheckerResult.HasLeak ?? false && Dbg.IsConsoleDebugMode)
                {
                    Console.ReadKey();
                }
            }
        }

        [ConsoleCommand(
            "IPA.Cores for .NET Core: Daemon Host Process",
            "[/IN:infile] [/OUT:outfile] [/CSV] [/CMD command_line...]",
            "IPA.Cores for .NET Core: Daemon Host Process",
            "IN:This will specify the text file 'infile' that contains the list of commands that are automatically executed after the connection is completed. If the /IN parameter is specified, this program will terminate automatically after the execution of all commands in the file are finished. If the file contains multiple-byte characters, the encoding must be Unicode (UTF-8). This cannot be specified together with /CMD (if /CMD is specified, /IN will be ignored).",
            "OUT:If the optional command 'commands...' is included after /CMD, that command will be executed after the connection is complete and this program will terminate after that. This cannot be specified together with /IN (if specified together with /IN, /IN will be ignored). Specify the /CMD parameter after all other parameters.",
            "CMD:If the optional command 'commands...' is included after /CMD, that command will be executed after the connection is complete and this program will terminate after that. This cannot be specified together with /IN (if specified together with /IN, /IN will be ignored). Specify the /CMD parameter after all other parameters.",
            "CSV:You can specify this option to enable CSV outputs. Results of each command will be printed in the CSV format. It is useful for processing the results by other programs."
            )]
        static int Daemon(ConsoleService c, string cmdName, string str)
        {
            ConsoleParam[] args =
            {
                    new ConsoleParam("IN", null, null, null, null),
                    new ConsoleParam("OUT", null, null, null, null),
                    new ConsoleParam("CMD", null, null, null, null),
                    new ConsoleParam("CSV", null, null, null, null),
                };

            ConsoleParamValueList vl = c.ParseCommandList(cmdName, str, args, noErrorOnUnknownArg: true);

            string cmdline = vl["CMD"].StrValue;

            ConsoleService cs = c;

            while (cs.DispatchCommand(cmdline, CoresLib.AppName + ">", typeof(DaemonCommands), null))
            {
                if (Str.IsEmptyStr(cmdline) == false)
                {
                    break;
                }
            }

            return cs.RetCode;
        }

        public static class DaemonCommands
        {
            [ConsoleCommand(
                "Start or stop the daemon",
                "Daemon [command]",
                "Start or stop the daemon",
                @"[command]:The control command.

[UNIX / Windows common commands]
start        - Start the daemon in the background mode.
stop         - Stop the running daemon in the background mode.
show         - Show the real-time log by the background daemon.
test         - Start the daemon in the foreground testing mode.
testdebug    - Similar to test, but for Visual Studio IDE debug.
testweb      - Similar to test, but Enable RazorRuntimeCompilation.
install      - Install the daemon as a Linux systemd service.
uninstall    - Uninstall the daemon as a Linux systemd service.

[Windows specific commands]
winstart     - Start the daemon as a Windows service.
winstop      - Stop the running daemon as a Windows service.
wininstall   - Install the daemon as a Windows service.
winuninstall - Uninstall the daemon as a Windows service.")]
            static int Daemon(ConsoleService c, string cmdName, string str)
            {
                return DaemonCmdLineTool.EntryPoint(c, cmdName, str, GetDaemonProc(), DaemonMain.DefaultDaemonSettings);
            }
        }
    }
}

#endif  // CORES_BASIC_DAEMON

