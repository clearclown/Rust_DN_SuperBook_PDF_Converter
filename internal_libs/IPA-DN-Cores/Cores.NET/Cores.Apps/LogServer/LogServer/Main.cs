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
using System.Runtime.CompilerServices;
using System.Diagnostics;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

namespace IPA.LogServer
{
    class LogServerAppMain
    {
        static int Main(string[] args)
        {
            int ret = -1;

            //Dbg.SetDebugMode(DebugMode.Debug, printStatToConsole: false, leakFullStack: false);

            CoresLib.Init(new CoresLibOptions(CoresMode.Daemon, "LogServer", DebugMode.Debug, defaultPrintStatToConsole: false, defaultRecordLeakFullStack: false), args);

            try
            {
                ret = ConsoleService.EntryPoint(Env.CommandLine, "LogServer", typeof(LogServerAppMain));
            }
            finally
            {
                CoresLib.Free();
            }

            return ret;
        }

        [ConsoleCommand(
            "IPA.Cores LogServer Program",
            "[/IN:infile] [/OUT:outfile] [/CSV] [/CMD command_line...]",
            "This is the LogServer Program.",
            "IN:This will specify the text file 'infile' that contains the list of commands that are automatically executed after the connection is completed. If the /IN parameter is specified, this program will terminate automatically after the execution of all commands in the file are finished. If the file contains multiple-byte characters, the encoding must be Unicode (UTF-8). This cannot be specified together with /CMD (if /CMD is specified, /IN will be ignored).",
            "OUT:If the optional command 'commands...' is included after /CMD, that command will be executed after the connection is complete and this program will terminate after that. This cannot be specified together with /IN (if specified together with /IN, /IN will be ignored). Specify the /CMD parameter after all other parameters.",
            "CMD:If the optional command 'commands...' is included after /CMD, that command will be executed after the connection is complete and this program will terminate after that. This cannot be specified together with /IN (if specified together with /IN, /IN will be ignored). Specify the /CMD parameter after all other parameters.",
            "CSV:You can specify this option to enable CSV outputs. Results of each command will be printed in the CSV format. It is useful for processing the results by other programs."
            )]
        static int LogServer(ConsoleService c, string cmdName, string str)
        {
            c.WriteLine($"Copyright (c) 2018-{DateTimeOffset.Now.Year} IPA CyberLab. All Rights Reserved.");
            c.WriteLine("");

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

            while (cs.DispatchCommand(cmdline, "LogServer>", typeof(LogServerCommands), null))
            {
                if (Str.IsEmptyStr(cmdline) == false)
                {
                    break;
                }
            }

            return cs.RetCode;
        }
    }
}

