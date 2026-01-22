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

using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.IO.Pipes;

namespace IPA.Cores.Tools.DebugHost
{
    [Flags]
    public enum Mode
    {
        Unknown = 0,
        Run,
        Start,
        Stop,
        Restart,
    }

    [Flags]
    public enum Cmd
    {
        Stop,
        Start,
        Restart,
    }

    [Flags]
    public enum Status
    {
        Initializing = 0,
        Running,
        Stopped,
    }

    public class Host : IDisposable
    {
        public string CmdLine { get; }

        Thread CurrentThread = null;

        int CurrentRunningProcessId = 0;

        readonly AutoResetEvent StartEvent = new AutoResetEvent(false);

        bool Next_TestWeb = false;

        public Host(string cmdLine)
        {
            this.CmdLine = cmdLine;

            StatusManager.SetStatus(Status.Initializing);

            Thread thread = new Thread(RunThreadProc);
            thread.Start();

            CurrentThread = thread;
        }

        void RunThreadProc()
        {
            LABEL_RESTART:
            StartEvent.Reset();
            try
            {
                string cmdLine = this.CmdLine;

                if (this.Next_TestWeb)
                {
                    cmdLine = cmdLine.Replace(" Test", " TestWeb");
                }

                Console.WriteLine($"DebugHost: Starting the child process '{cmdLine}' ...");
                Console.WriteLine("DebugHost: --------------------------------------------------------------");

                using (Process p = Utils.ExecProcess(cmdLine))
                {
                    CurrentRunningProcessId = p.Id;

                    StatusManager.SetStatus(Status.Running);

                    try
                    {
                        p.WaitForExit();
                    }
                    finally
                    {
                        CurrentRunningProcessId = 0;
                    }

                    Console.WriteLine("DebugHost: --------------------------------------------------------------");
                    Console.WriteLine($"DebugHost: Exit code of the process: {p.ExitCode}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("DebugHost: --------------------------------------------------------------");
                Console.WriteLine($"DebugHost: {ex.ToString()}");
                Console.WriteLine("DebugHost: --------------------------------------------------------------");
            }

            StatusManager.SetStatus(Status.Stopped);

            Console.WriteLine("DebugHost: The process is stopped. Waiting for next start command ...");

            if (StartEvent.WaitOne(0) == false)
            {
                CancellationTokenSource cancelSource = new CancellationTokenSource();

                Thread waitKeyThread = new Thread(WaitKeyThreadProc);
                waitKeyThread.Start(cancelSource.Token);

                StartEvent.WaitOne();

                cancelSource.Cancel();
                waitKeyThread.Join();
            }

            goto LABEL_RESTART;
        }

        void WaitKeyThreadProc(object param)
        {
            CancellationToken cancel = (CancellationToken)param;

            ProcessStartInfo info = new ProcessStartInfo()
            {
                FileName = Process.GetCurrentProcess().MainModule.FileName,
                Arguments = "wait",
                UseShellExecute = false,
                CreateNoWindow = false,
            };

            using (Process p = Process.Start(info))
            {
                var reg = cancel.Register(() =>
                {
                    p.Kill();
                });

                using (reg)
                {
                    p.WaitForExit();

                    if (p.ExitCode == 0)
                    {
                        this.Next_TestWeb = false;
                        StartEvent.Set();
                    }
                    else if (p.ExitCode == 1)
                    {
                        this.Next_TestWeb = true;
                        StartEvent.Set();
                    }
                    else if (p.ExitCode == 4)
                    {
                        Console.WriteLine("Terminating...");
                        Process.GetCurrentProcess().Kill();
                    }
                }
            }
        }

        public void Stop()
        {
            StartEvent.Reset();

            int id = CurrentRunningProcessId;

            if (id != 0)
            {
                try
                {
                    using (Process p = Process.GetProcessById(id))
                    {
                        p.Kill();
                        p.WaitForExit();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.ToString());
                }
            }

            StartEvent.Reset();
        }

        public void Start()
        {
            if (CurrentRunningProcessId != 0)
            {
                return;
            }

            StartEvent.Set();

            while (true)
            {
                if (CurrentRunningProcessId != 0) break;
                Thread.Sleep(1);
            }
        }

        public void Dispose() => Dispose(true);
        Once DisposeFlag;
        protected virtual void Dispose(bool disposing)
        {
            if (!disposing || DisposeFlag.IsFirstCall() == false) return;

            if (CurrentThread != null)
            {
                CurrentThread.Join();
                CurrentThread = null;
            }
        }
    }

    public static class Client
    {
        public static void ExecuteCommand(string cmdLine, string pipeName, string appId, Cmd cmd)
        {
            NamedPipeClientStream cli = TryConnectPipe(pipeName);

            if (cli == null)
            {
                Console.WriteLine("DebugHost Client: The server is not running.");

                if (string.IsNullOrWhiteSpace(cmdLine) == false)
                {
                    if (cmd == Cmd.Stop)
                    {
                        return;
                    }

                    Console.WriteLine($"DebugHost Client: starting the DebugHost server with cmdline '{cmdLine}' ...");

                    // copy exe to temp path
                    string dir = Path.Combine(Path.GetTempPath(), "CoresAspNet_DebugHost");
                    string srcExe = Process.GetCurrentProcess().MainModule.FileName;
                    string dstExe = Path.Combine(dir, "DebugHost_" + appId.ToLower() + ".exe");

                    try
                    {
                        if (Directory.Exists(dir) == false)
                        {
                            Directory.CreateDirectory(dir);
                        }

                        File.Copy(srcExe, dstExe, true);
                    }
                    catch
                    {
                    }

                    ProcessStartInfo info = new ProcessStartInfo()
                    {
                        FileName = dstExe,
                        Arguments = $"run {appId} {cmdLine}",
                        UseShellExecute = true,
                        CreateNoWindow = false,
                    };

                    Process p = Process.Start(info);

                    Console.WriteLine($"DebugHost Client: The debug host server process is started.");
                    Console.WriteLine($"DebugHost Client: Waiting the server for ready ...");

                    while (true)
                    {
                        cli = TryConnectPipe(pipeName);
                        if (cli != null) break;

                        if (p.HasExited)
                        {
                            throw new ApplicationException("DebugHost Client: The server process is abnormally exited.");
                        }

                        Thread.Sleep(1);
                    }

                    Console.WriteLine($"DebugHost Client: Ready.");
                }
                else
                {
                    throw new ApplicationException("DebugHost Client: The server is not running.");
                }
            }

            using (cli)
            {
                Console.WriteLine($"DebugHost Client: Sending the command '{cmd.ToString()}'...");

                cli.WriteByte((byte)cmd);

                cli.ReadByte();

                Console.WriteLine($"DebugHost Client: Command '{cmd.ToString()}' completed.");
            }
        }

        static NamedPipeClientStream TryConnectPipe(string pipeName)
        {
            NamedPipeClientStream cli = null;
            try
            {
                cli = new NamedPipeClientStream(pipeName);
                cli.Connect(0);
                return cli;
            }
            catch
            {
                if (cli != null)
                {
                    try
                    {
                        cli.Dispose();
                    }
                    catch { }
                }
                return null;
            }
        }
    }

    public class Server
    {
        public string CmdLine { get; }
        public string PipeName { get; }

        readonly Host Host;

        public Server(string cmdLine, string pipeName)
        {
            this.CmdLine = cmdLine;
            this.PipeName = pipeName;

            this.Host = new Host(cmdLine);

            while (true)
            {
                using (NamedPipeServerStream svr = new NamedPipeServerStream(this.PipeName))
                {
                    try
                    {
                        svr.WaitForConnection();

                        Cmd cmd = (Cmd)svr.ReadByte();

                        Console.WriteLine($"DebugHost: Received a command: {cmd.ToString()}");

                        switch (cmd)
                        {
                            case Cmd.Stop:
                                this.Host.Stop();
                                break;

                            case Cmd.Start:
                                this.Host.Start();
                                break;

                            case Cmd.Restart:
                                this.Host.Stop();
                                this.Host.Start();
                                break;
                        }

                        svr.WriteByte(1);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex.ToString());
                    }
                }
            }
        }
    }

    public static class StatusManager
    {
        public static Status CurrentStatus { get; private set; }
        public static string TitlePrefix { get; private set; } = "DebugHost";

        public static void SetTitleBase(string titlePrefix)
        {
            TitlePrefix = titlePrefix;

            SetStatus(CurrentStatus);
        }

        public static void SetStatus(Status status)
        {
            CurrentStatus = status;

            string tmp = TitlePrefix + " - " + status.ToString();

            if (Console.Title != tmp)
            {
                Console.Title = tmp;
            }
        }
    }

    public static class MainClass
    {
        static int Main(string[] args)
        {
            if (args.Length == 1 && args[0].ToLower() == "wait")
            {
                while (true)
                {
                    Console.Write("'r' to restart / 'q' to terminate / 'w' for Auto Razor Recompile>");
                    string line = Console.ReadLine().Trim();
                    if (string.Equals(line, "r", StringComparison.OrdinalIgnoreCase))
                    {
                        return 0;
                    }
                    if (string.Equals(line, "w", StringComparison.OrdinalIgnoreCase))
                    {
                        return 1;
                    }
                    if (string.Equals(line, "q", StringComparison.OrdinalIgnoreCase))
                    {
                        return 4;
                    }
                }
            }

            Utils.DivideCommandLine(Environment.CommandLine, out _, out string argString);

            argString = argString.Trim();

            bool showHelp = false;

            if (string.IsNullOrWhiteSpace(argString) == false)
            {
                Utils.GetKeyAndValue(argString, out string action, out string appIdAndCmdLine);
                Utils.GetKeyAndValue(appIdAndCmdLine, out string appId, out string cmdLine);

                if (string.IsNullOrWhiteSpace(appId) == false)
                {
                    StatusManager.SetTitleBase(appId);
                }

                if (string.IsNullOrWhiteSpace(argString) || string.IsNullOrWhiteSpace(appId))
                {
                    showHelp = true;
                }
                else
                {
                    string appIdHash = Utils.ByteToHex(Utils.HashSHA1(Encoding.UTF8.GetBytes(appId.ToLower() + ":HashDebugHost")), "").ToLower();

                    string pipeName = "pipe_" + appIdHash;

                    string mutexName = "mutex_" + appIdHash;

                    Mode mode = Mode.Unknown;
                    Cmd cmd = default;

                    switch (action.ToLower())
                    {
                        case "run":
                            if (string.IsNullOrWhiteSpace(cmdLine) == false)
                            {
                                mode = Mode.Run;
                            }
                            break;

                        case "stop":
                            mode = Mode.Stop;
                            cmd = Cmd.Stop;
                            break;

                        case "start":
                            mode = Mode.Start;
                            cmd = Cmd.Start;
                            break;

                        case "restart":
                            mode = Mode.Restart;
                            cmd = Cmd.Restart;
                            break;
                    }

                    if (mode == Mode.Unknown)
                    {
                        showHelp = true;
                    }
                    else
                    {
                        if (mode == Mode.Run)
                        {
                            GlobalMutex mutex = new GlobalMutex(mutexName);

                            if (mutex.TryLock())
                            {
                                Server svr = new Server(cmdLine, pipeName);

                                mutex.Unlock();
                            }
                            else
                            {
                                Console.WriteLine("Error: Another instance is already running.");

                                return -2;
                            }
                        }
                        else
                        {
                            Client.ExecuteCommand(cmdLine, pipeName, appId, cmd);
                        }
                    }
                }
            }
            else
            {
                showHelp = true;
            }

            if (showHelp)
            {
                Console.WriteLine(@"Usage:
 DebugHost run <AppId> <CommandLine...>
 DebugHost start <AppId> [CommandLine...]
 DebugHost stop <AppId>
 DebugHost restart <AppId>
");

                return -1;
            }

            return 0;
        }
    }
}
