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

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

namespace IPA.Cores.Basic.Legacy;

public enum ListenerStatus
{
    Trying = 0,
    Listening = 1,
}

public delegate void AcceptProc(Listener listener, Sock sock, object param);

public class Listener
{
    public const int ListenRetryTimeDefault = 2 * 1000;

    public int ListenRetryTime { get; set; }
    object lockObj;

    public int Port { get; }
    ThreadObj? thread;

    public Sock? Sock { get; private set; }
    Event eventObj;
    bool halt;

    public ListenerStatus Status { get; private set; }

    public AcceptProc AcceptProc { get; }
    public object AcceptParam { get; }
    public bool LocalOnly { get; }
    public bool GetHostName { get; set; }

    public Listener(int port, AcceptProc acceptProc, object acceptParam, bool localOnly = false, bool getHostName = false)
    {
        this.lockObj = new object();
        this.Port = port;
        this.AcceptProc = acceptProc;
        this.AcceptParam = acceptParam;
        this.Status = ListenerStatus.Trying;
        this.eventObj = new Event();
        this.halt = false;
        this.LocalOnly = localOnly;
        this.GetHostName = getHostName;
        this.ListenRetryTime = ListenRetryTimeDefault;

        // スレッドの作成
        ThreadObj thread = new ThreadObj(new ThreadProc(ListenerThread));

        thread.WaitForInit();
    }

    // 停止
    public void Stop()
    {
        Sock? s;

        lock (this.lockObj)
        {
            if (this.halt)
            {
                return;
            }

            this.halt = true;

            s = this.Sock;
        }

        if (s != null)
        {
            s.Disconnect();
        }

        this.eventObj.Set();

        this.thread?.WaitForEnd();
    }

    // TCP 受付完了
    void tcpAccepted(Sock s)
    {
        ThreadObj t = new ThreadObj(new ThreadProc(tcpAcceptedThread), s);
        t.WaitForInit();
    }

    void tcpAcceptedThread(object? param)
    {
        Sock s = (Sock)param!;

        ThreadObj.NoticeInited();

        this.AcceptProc(this, s, this.AcceptParam);
    }

    // スレッド
    public void ListenerThread(object? param)
    {
        Sock? new_sock;
        Sock s;
        int num_failed;

        this.thread = ThreadObj.GetCurrentThreadObj()!;

        this.Status = ListenerStatus.Trying;

        ThreadObj.NoticeInited();

        while (true)
        {
            bool firstFailed = true;
            this.Status = ListenerStatus.Trying;

            // Listen を試みる
            while (true)
            {
                if (this.halt)
                {
                    return;
                }

                try
                {
                    s = Sock.Listen(this.Port, this.LocalOnly);

                    this.Sock = s;

                    break;
                }
                catch
                {
                    if (firstFailed)
                    {
                        firstFailed = false;
                    }

                    this.eventObj.Wait(this.ListenRetryTime);

                    if (this.halt)
                    {
                        return;
                    }
                }
            }

            this.Status = ListenerStatus.Listening;

            if (this.halt)
            {
                this.Sock.Disconnect();
                break;
            }

            num_failed = 0;

            // Accept ループ
            while (true)
            {
                // Accept する
                new_sock = this.Sock.Accept(this.GetHostName);
                if (new_sock != null)
                {
                    // 成功
                    tcpAccepted(new_sock);
                }
                else
                {
                    // 失敗
                    if (this.halt == false)
                    {
                        if ((++num_failed) <= 5)
                        {
                            continue;
                        }
                    }

                    this.Sock.Disconnect();
                    break;
                }
            }

            if (this.halt)
            {
                return;
            }
        }
    }
}
