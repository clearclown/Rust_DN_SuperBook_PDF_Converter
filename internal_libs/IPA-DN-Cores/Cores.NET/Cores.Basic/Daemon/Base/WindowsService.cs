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
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Diagnostics;
using System.ServiceProcess;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

using IPA.Cores.Basic.Internal.WinSvc;
using System.Net;

#pragma warning disable CA1416 // プラットフォームの互換性の検証

namespace IPA.Cores.Basic
{
    public interface IService
    {
        string Name { get; }

        void ExecMain();

        void StopService(int stopTimeout);

        void Show();
    }

    public sealed class WindowsService : IService
    {
        public string Name { get; }

        Action OnStartInternal { get; }
        Action OnStopInternal { get; }

        readonly WindowsServiceObject WinSvcObj;

        public int TelnetLogWatcherPort { get; }
        TelnetLocalLogWatcher? TelnetWatcher;

        public WindowsService(string name, Action onStart, Action onStop, int telnetLogWatcherPort)
        {
            if (Env.IsWindows == false) throw new PlatformNotSupportedException();

            this.Name = name;

            this.OnStartInternal = onStart;
            this.OnStopInternal = onStop;

            this.TelnetLogWatcherPort = telnetLogWatcherPort;

            this.WinSvcObj = new WindowsServiceObject(this);
        }

        Once Once;

        public void ExecMain()
        {
            if (Once.IsFirstCall() == false) throw new ApplicationException("StartMainLoop can be called only once.");

            ServiceBase.Run(this.WinSvcObj);
        }

        public void StopService(int stopTimeout)
        {
            throw new NotImplementedException();
        }

        public void OnStart()
        {
            // Start the TelnetLogWatcher
            List<IPEndPoint> telnetWatcherEpList = new List<IPEndPoint>();

            int localLogWatchPort = 50000 + Util.RandSInt31() % 10000;
            telnetWatcherEpList.Add(new IPEndPoint(IPAddress.Loopback, localLogWatchPort));
            telnetWatcherEpList.Add(new IPEndPoint(IPAddress.IPv6Loopback, localLogWatchPort));

            if (this.TelnetLogWatcherPort != 0)
            {
                telnetWatcherEpList.Add(new IPEndPoint(IPAddress.Any, this.TelnetLogWatcherPort));
                telnetWatcherEpList.Add(new IPEndPoint(IPAddress.IPv6Any, this.TelnetLogWatcherPort));
            }

            TelnetWatcher = new TelnetLocalLogWatcher(new TelnetStreamWatcherOptions((ip) => ip._GetIPAddressType().BitAny(IPAddressType.LocalUnicast | IPAddressType.Loopback), null,
                telnetWatcherEpList.ToArray()));

            // Start the service
            this.OnStartInternal();
        }

        public void OnStop()
        {
            // Stop the service
            this.OnStopInternal();

            // Stop the TelnetLogWatcher
            TelnetWatcher._DisposeSafe();
            TelnetWatcher = null;
        }

        public void Show() => throw new NotImplementedException();
    }

    namespace Internal.WinSvc
    {
        public sealed class WindowsServiceObject : ServiceBase
        {
            readonly WindowsService Svc;
            readonly CriticalSection LockObj = new CriticalSection<WindowsServiceObject>();

            public WindowsServiceObject(WindowsService service)
            {
                this.CanStop = true;
                this.CanShutdown = true;
                this.CanPauseAndContinue = false;

                this.AutoLog = true;
                this.ServiceName = service.Name;

                this.Svc = service;
            }

            Once StartOnce;

            void InternalStart()
            {
                lock (LockObj)
                {
                    if (StartOnce.IsFirstCall() == false) throw new ApplicationException($"WindowsServiceObject ({this.ServiceName}): You cannot try to start the same service twice.");

                    Svc.OnStart();
                }
            }

            Once StopOnce;

            void InternalStop()
            {
                lock (LockObj)
                {
                    if (StartOnce.IsSet == false) throw new ApplicationException($"WindowsServiceObject ({this.ServiceName}): The service is not started yet.");

                    if (StopOnce.IsFirstCall())
                    {
                        try
                        {
                            Svc.OnStop();
                        }
                        catch (Exception ex)
                        {
                            Kernel.SelfKill($"WindowsServiceObject ({this.ServiceName}): An error occured on the OnStop() routine. Terminating the process. Error: {ex.ToString()}");
                        }
                    }
                }
            }

            protected override void OnStart(string[] args)
            {
                InternalStart();
            }

            protected override void OnStop()
            {
                InternalStop();
            }

            protected override void OnShutdown()
            {
                InternalStop();
            }
        }
    }
}

#endif // CORES_BASIC_DAEMON

