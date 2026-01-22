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

#pragma warning disable CA2235 // Mark all non-serializable fields
#pragma warning disable CA1416 // プラットフォームの互換性の検証

using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Diagnostics;
using System.ServiceProcess;
using System.Runtime.Serialization;
using System.Net;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

using IPA.Cores.Basic.Internal.UserModeService;
using System.IO.Pipes;

namespace IPA.Cores.Basic
{
    public static partial class CoresConfig
    {
        public static partial class UserModeServiceSettings
        {
            public static readonly Copenhagen<Func<string>> GetLocalHiveDirProc = new Func<string>(() => Env.LocalPathParser.Combine(Env.AppRootDir, "Local", "DaemonPid"));
        }
    }

    namespace Internal.UserModeService
    {
        [Serializable]
        [DataContract]
        public class UserModeServicePidData : INormalizable
        {
            [DataMember]
            public long Pid;

            [DataMember]
            public string? EventName;

            [DataMember]
            public int LocalLogWatchPort;

            public void Normalize()
            {
                if (LocalLogWatchPort == 0)
                    LocalLogWatchPort = 50000 + Util.RandSInt31() % 10000;
            }
        }
    }

    public sealed class UserModeService : IService
    {
        public const string ExecMainSignature = "--- User mode service started ---";

        public string Name { get; }

        public Action OnStart { get; }
        public Action OnStop { get; }

        readonly CriticalSection LockObj = new CriticalSection<UserModeService>();

        readonly Hive Hive;
        readonly HiveData<UserModeServicePidData> HiveData;
        readonly Event StoppedEvent = new Event(true);

        SingleInstance? SingleInstance;

        public int TelnetLogWatcherPort { get; }
        TelnetLocalLogWatcher? TelnetWatcher;

        public UserModeService(string name, Action onStart, Action onStop, int telnetLogWatcherPort)
        {
            this.Name = name;

            this.OnStart = onStart;
            this.OnStop = onStop;

            this.TelnetLogWatcherPort = telnetLogWatcherPort;

            this.Hive = new Hive(new HiveOptions(CoresConfig.UserModeServiceSettings.GetLocalHiveDirProc.Value(), globalLock: true));
            this.HiveData = new HiveData<UserModeServicePidData>(this.Hive, this.Name, () => new UserModeServicePidData());
        }

        Once Once;

        public void ExecMain()
        {
            if (Once.IsFirstCall() == false) throw new ApplicationException("StartMainLoop can be called only once.");

            this.SingleInstance = new SingleInstance($"UserModeService_{this.Name}");

            try
            {
                string? eventName = null;
                EventWaitHandle? eventHandle = null;

                if (Env.IsUnix)
                {
                    System.Runtime.Loader.AssemblyLoadContext.Default.Unloading += UnixSigTermHandler;
                }
                else
                {
                    eventName = @"Global\usermodesvc_" + Util.Rand(16)._GetHexString().ToLowerInvariant();

                    eventHandle = new EventWaitHandle(false, EventResetMode.ManualReset, eventName);

                    TaskUtil.StartSyncTaskAsync(() =>
                    {
                        eventHandle.WaitOne();

                        Win32PipeRecvHandler();
                    }, leakCheck: false)._LaissezFaire(true);
                }

                // Start the TelnetLogWatcher
                List<IPEndPoint> telnetWatcherEpList = new List<IPEndPoint>();

                telnetWatcherEpList.Add(new IPEndPoint(IPAddress.Loopback, HiveData.ManagedData.LocalLogWatchPort));
                telnetWatcherEpList.Add(new IPEndPoint(IPAddress.IPv6Loopback, HiveData.ManagedData.LocalLogWatchPort));

                if (this.TelnetLogWatcherPort != 0)
                {
                    telnetWatcherEpList.Add(new IPEndPoint(IPAddress.Any, this.TelnetLogWatcherPort));
                    telnetWatcherEpList.Add(new IPEndPoint(IPAddress.IPv6Any, this.TelnetLogWatcherPort));
                }

                TelnetWatcher = new TelnetLocalLogWatcher(new TelnetStreamWatcherOptions((ip) => ip._GetIPAddressType().BitAny(IPAddressType.LocalUnicast | IPAddressType.Loopback), null,
                    telnetWatcherEpList.ToArray()));

                InternalStart();

                lock (HiveData.DataLock)
                {
                    HiveData.ManagedData.Pid = Env.ProcessId;
                    HiveData.ManagedData.EventName = eventName;
                }

                HiveData.SyncWithStorage(HiveSyncFlags.SaveToFile | HiveSyncFlags.ForceUpdate, true);

                // Save pid
                string pidFileName = Lfs.PathParser.Combine(CoresConfig.UserModeServiceSettings.GetLocalHiveDirProc.Value(), this.Name + ".pid");
                string pidBody = Env.ProcessId.ToString() + Env.NewLine;
                Lfs.WriteStringToFile(pidFileName, pidBody, FileFlags.AutoCreateDirectory);

                lock (Con.ConsoleWriteLock)
                {
                    Console.WriteLine(ExecMainSignature);
                }

                // The daemon routine is now started. Wait here until InternalStop() is called.
                StoppedEvent.Wait();
            }
            finally
            {
                this.TelnetWatcher._DisposeSafe();
                this.TelnetWatcher = null;

                this.SingleInstance._DisposeSafe();
                this.SingleInstance = null;
            }
        }


        Once StartOnce;

        void InternalStart()
        {
            lock (LockObj)
            {
                if (StartOnce.IsFirstCall() == false) throw new ApplicationException($"UserModeService ({this.Name}): You cannot try to start the same service twice.");

                this.OnStart();
            }
        }

        Once StopOnce;

        internal void InternalStop()
        {
            this.SingleInstance._DisposeSafe();
            this.SingleInstance = null;

            lock (LockObj)
            {
                if (StartOnce.IsSet == false) throw new ApplicationException($"UserModeService ({this.Name}): The service is not started yet.");

                if (StopOnce.IsFirstCall())
                {
                    try
                    {
                        this.OnStop();
                    }
                    catch (Exception ex)
                    {
                        Kernel.SelfKill($"UserModeService ({this.Name}): An error occured on the OnStop() routine. Terminating the process. Error: {ex.ToString()}");
                    }

                    if (TelnetWatcher != null)
                    {
                        TelnetWatcher._DisposeSafe();
                    }

                    lock (HiveData.DataLock)
                    {
                        HiveData.ManagedData.Pid = 0;
                    }

                    // Delete pid
                    string pidFileName = Lfs.PathParser.Combine(CoresConfig.UserModeServiceSettings.GetLocalHiveDirProc.Value(), this.Name + ".pid");
                    try
                    {
                        // 2019/11/06 .pid ファイルを削除しないようにした
                        //Lfs.DeleteFile(pidFileName);
                    }
                    catch { }

                    HiveData.SyncWithStorage(HiveSyncFlags.SaveToFile | HiveSyncFlags.ForceUpdate, true);

                    // Break the freeze state of the ExecMain() function
                    StoppedEvent.Set();
                }
            }
        }

        // Win32 Pipe handler
        Once PipeRecvOnce;
        void Win32PipeRecvHandler()
        {
            if (PipeRecvOnce.IsFirstCall())
            {
                // Stop the service
                Con.WriteLine($"The daemon \"{Name}\" received the shutdown command. Shutting down the daemon...");

                InternalStop();

                Con.WriteLine($"The daemon \"{Name}\" completed the shutdown command handler.");
            }
        }

        // SIGTERM handler
        Once SigTermOnce;
        void UnixSigTermHandler(System.Runtime.Loader.AssemblyLoadContext obj)
        {
            if (SigTermOnce.IsFirstCall())
            {
                // Stop the service
                Con.WriteLine($"The daemon \"{Name}\" received the SIGTERM signal. Shutting down the daemon...");

                InternalStop();

                Con.WriteLine($"The daemon \"{Name}\" completed the SIGTERM handler.");
            }
        }

        public void Show()
        {
            HiveData.SyncWithStorage(HiveSyncFlags.LoadFromFile, true);

            long pid = HiveData.ManagedData.Pid;
            int port = HiveData.ManagedData.LocalLogWatchPort;

            if (pid != 0 && port != 0)
            {
                Con.WriteLine("Starting the real-time log session.");
                Con.WriteLine("Pressing Ctrl + D or Ctrl + Q to disconnect the session.");
                Con.WriteLine();

                Con.WriteLine($"Connecting to localhost:{port} ...");

                CancellationTokenSource cancelSource = new CancellationTokenSource();
                CancellationToken cancel = cancelSource.Token;

                Task task = TaskUtil.StartAsyncTaskAsync(async () =>
                {
                    try
                    {
                        await using (var sock = await LocalNet.ConnectAsync(new TcpConnectParam(IPAddress.Loopback, port), cancel))
                        await using (var stream = sock.GetStream())
                        using (MemoryHelper.FastAllocMemoryWithUsing(65536, out Memory<byte> tmp))
                        {
                            Con.WriteLine("The real-time log session is connected.");
                            Con.WriteLine();
                            try
                            {
                                while (true)
                                {
                                    int r = await stream.ReadAsync(tmp, cancel);
                                    if (r <= 0) break;
                                    ReadOnlyMemory<byte> data = tmp.Slice(0, r);
                                    string s = Str.Utf8Encoding.GetString(data.Span);
                                    Console.Write(s);
                                }
                            }
                            catch { }

                            Con.WriteLine();
                            Con.WriteLine("The real-time log session is disconnected.");
                        }
                    }
                    catch (Exception ex)
                    {
                        Con.WriteError(ex.Message);
                    }
                });

                try
                {
                    while (true)
                    {
                        var key = Console.ReadKey();
                        if ((key.Key == ConsoleKey.D || key.Key == ConsoleKey.Q) && key.Modifiers == ConsoleModifiers.Control)
                        {
                            break;
                        }
                    }
                }
                catch { }

                cancelSource._TryCancelNoBlock();

                task._TryWait(true);
            }
            else
            {
                Con.WriteLine($"The daemon \"{Name}\" is not running.");
            }
        }

        public void StopService(int stopTimeout)
        {
            HiveData.SyncWithStorage(HiveSyncFlags.LoadFromFile, true);

            long pid = HiveData.ManagedData.Pid;

            if (pid != 0)
            {
                if (Env.IsWindows == false)
                {
                    if (UnixApi.Kill((int)pid, UnixApi.Signals.SIGTERM) == 0)
                    {
                        Con.WriteLine($"Shutting down the daemon \"{Name}\" (pid = {pid}) ...");

                        if (UnixApi.WaitProcessExit((int)pid, stopTimeout) == false)
                        {
                            Con.WriteLine($"Shutting down the daemon \"{Name}\" (pid = {pid}) timed out.");

                            throw new ApplicationException($"Shutting down the daemon \"{Name}\" (pid = {pid}) timed out.");
                        }
                    }
                    else
                    {
                        Con.WriteLine($"The daemon \"{Name}\" is not running.");
                    }
                }
                else
                {
                    if (ManualResetEvent.TryOpenExisting(HiveData.ManagedData.EventName!, out EventWaitHandle? eventHandle))
                    {
                        try
                        {
                            Con.WriteLine($"Stopping the daemon \"{Name}\" (pid = {pid}) ...");

                            eventHandle.Set();

                            if (Win32ApiUtil.WaitProcessExit((int)pid, stopTimeout) == false)
                            {
                                Con.WriteLine($"Stopping the daemon \"{Name}\" (pid = {pid}) timed out.");

                                throw new ApplicationException($"Stopping the daemon \"{Name}\" (pid = {pid}) timed out.");
                            }
                        }
                        finally
                        {
                            eventHandle._DisposeSafe();
                        }
                    }
                    else
                    {
                        Con.WriteLine($"The daemon \"{Name}\" is not running.");
                    }
                }
            }
            else
            {
                Con.WriteLine($"The daemon \"{Name}\" is not running.");
            }
        }
    }
}

#endif // CORES_BASIC_DAEMON
