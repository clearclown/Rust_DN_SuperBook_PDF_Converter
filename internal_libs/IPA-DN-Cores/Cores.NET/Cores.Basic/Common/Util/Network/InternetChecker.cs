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

// Author: Daiyuu Nobori
// Internet Connectivity Checker

#if true

#pragma warning disable CA2235 // Mark all non-serializable fields

using System;
using System.Buffers;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Serialization;
using System.Net;
using System.Net.Sockets;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

namespace IPA.Cores.Basic;

[Serializable]
public abstract class InternetCheckerTestItemBase { }

[Serializable]
public class InternetCheckerHttpTestItem : InternetCheckerTestItemBase
{
    public string Fqdn = null!;
    public AddressFamily Family = AddressFamily.InterNetwork;
    public int Port;
    public string Path = null!;
    public string ExpectedResultString = null!;

    public override string ToString() => Fqdn;
}

[Serializable]
public class InternetCheckerPingTestItem : InternetCheckerTestItemBase
{
    public string IpAddress = null!;

    public override string ToString() => IpAddress;
}

[Serializable]
public class InternetCheckerOptions
{
    // HTTP タイムアウト
    public int HttpTimeout = 15 * 1000;

    // Ping タイムアウト
    public int PingTimeout = 1 * 1000;

    // 再試行間隔
    public int RetryIntervalMin = 1 * 1000;
    public int RetryIntervalMax = 180 * 1000;
    public int RetryIntervalMaxPing = 5 * 1000;
    public int RetryWhenNetworkChanged = 1 * 1000;

    // 一度インターネットとの接続が完了した後、接続試験を実施する間隔
    public int CheckIntervalAfterEstablished = 180 * 1000;

    // 切断されたと認識されるまでのタイムアウト
    public int TimeoutToDetectDisconnected = 180 * 1000;

#if true
    // HTTP テスト先
    public InternetCheckerHttpTestItem[] HttpTestItemList = new InternetCheckerHttpTestItem[]
        {
                // Microsoft #1
                new InternetCheckerHttpTestItem { Fqdn = "www.msftncsi.com", Family = AddressFamily.InterNetwork, Port = 80, Path = "/ncsi.txt", ExpectedResultString = "Microsoft NCSI" },

                // Microsoft #2
                new InternetCheckerHttpTestItem { Fqdn = "www.msftconnecttest.com", Family = AddressFamily.InterNetwork, Port = 80, Path = "/connecttest.txt", ExpectedResultString = "Microsoft Connect Test" },

                // SoftEther
                new InternetCheckerHttpTestItem { Fqdn = "www.softether.com", Family = AddressFamily.InterNetwork, Port = 80, Path = "/", ExpectedResultString = "softether." },
        };

    // Ping テスト先
    public InternetCheckerPingTestItem[] PingTestItemList = new InternetCheckerPingTestItem[]
        {
                new InternetCheckerPingTestItem {IpAddress = "8.8.8.8"},          // Google Public DNS #1
                new InternetCheckerPingTestItem {IpAddress = "8.8.4.4"},          // Google Public DNS #2
                new InternetCheckerPingTestItem {IpAddress = "9.9.9.9"},          // QUAD9-AS-1
                new InternetCheckerPingTestItem {IpAddress = "208.67.222.222"},   // Open DNS
                new InternetCheckerPingTestItem {IpAddress = "64.6.64.6"},        // Verisign Open DNS
        };
#else
        // HTTP テスト先
        public InternetCheckerHttpTestItem[] HttpTestItemList = new InternetCheckerHttpTestItem[]
            {
                // Microsoft #1
                new InternetCheckerHttpTestItem { Fqdn = "127.0.0.1", Family = AddressFamily.InterNetwork, Port = 3333, Path = "/ncsi.txt", ExpectedResultString = "Microsoft NCSI" },

                // Microsoft #2
                new InternetCheckerHttpTestItem { Fqdn = "192.168.99.99", Family = AddressFamily.InterNetwork, Port = 80, Path = "/connecttest.txt", ExpectedResultString = "Microsoft Connect Test" },
            };

        // Ping テスト先
        public InternetCheckerPingTestItem[] PingTestItemList = new InternetCheckerPingTestItem[]
            {
                new InternetCheckerPingTestItem {IpAddress = "192.168.99.1"},
                new InternetCheckerPingTestItem {IpAddress = "192.168.99.2"},
            };
#endif

    public InternetCheckerTestItemBase[] GetTestItemList()
    {
        List<InternetCheckerTestItemBase> ret = new List<InternetCheckerTestItemBase>();

        HttpTestItemList._DoForEach(x => ret.Add(x));

        PingTestItemList._DoForEach(x => ret.Add(x));

        return ret.ToArray();
    }
}

public class InternetChecker : AsyncServiceWithMainLoop
{
    public readonly TcpIpSystem TcpIp;
    public readonly InternetCheckerOptions Options;

    public readonly FastEventListenerList<InternetChecker, NonsenseEventType> EventListener = new FastEventListenerList<InternetChecker, NonsenseEventType>();

    public readonly AsyncManualResetEvent FirstConnectedEvent = new AsyncManualResetEvent();
    public readonly AsyncManualResetEvent FirstDisconnectedEvent = new AsyncManualResetEvent();

    public bool IsInternetConnected => _IsInternetConnected;

    volatile bool _IsInternetConnected = false;

    public InternetChecker(InternetCheckerOptions? options = null, TcpIpSystem? tcpIp = null)
    {
        this.Options = options?._CloneIfClonable() ?? new InternetCheckerOptions();
        this.TcpIp = tcpIp ?? LocalNet;

        this.StartMainLoop(MainLoopAsync);
    }

    // メインループ
    async Task MainLoopAsync(CancellationToken cancel)
    {
        while (cancel.IsCancellationRequested == false)
        {
            // インターネットが接続されるまで待機する
            if (await WaitForInternetAsync(cancel) == false)
            {
                // キャンセルされた
                return;
            }

            // 接続された
            _IsInternetConnected = true;
            FirstConnectedEvent.Set(true);
            EventListener.FireSoftly(this, NonsenseEventType.Nonsense, true);

            int lastNetworkVersion = TcpIp.GetHostInfo(false).InfoVersion;

            AsyncAutoResetEvent networkChangedEvent = new AsyncAutoResetEvent();
            int eventRegisterId = TcpIp.RegisterHostInfoChangedEvent(networkChangedEvent);
            try
            {
                // 定期的に、現在もインターネットに接続されているかどうか確認する
                while (cancel.IsCancellationRequested == false)
                {
                    // 一定時間待つ ただしネットワーク状態の変化が発生したときは待ちを解除する
                    await TaskUtil.WaitObjectsAsync(cancels: cancel._SingleArray(), events: networkChangedEvent._SingleArray(),
                        timeout: Options.CheckIntervalAfterEstablished);

                    if (cancel.IsCancellationRequested) break;

                    // 接続検査を再実行する
                    // ただし、今度はタイムアウトを設定する
                    CancellationTokenSource timeoutCts = new CancellationTokenSource();
                    timeoutCts.CancelAfter(Options.TimeoutToDetectDisconnected);

                    bool ret = await StartEveryTestAsync(timeoutCts.Token, null);

                    if (cancel.IsCancellationRequested) break;

                    if (ret == false)
                    {
                        // 接続試験に失敗 (タイムアウト発生)
                        // 切断された
                        _IsInternetConnected = false;
                        FirstDisconnectedEvent.Set(true);
                        EventListener.FireSoftly(this, NonsenseEventType.Nonsense, false);

                        break;
                    }
                }
            }
            finally
            {
                TcpIp.UnregisterHostInfoChangedEvent(eventRegisterId);
            }
        }
    }

    // インターネットが接続されるまで待機する
    // true: 接続された
    // false: キャンセルされた
    async Task<bool> WaitForInternetAsync(CancellationToken cancel = default)
    {
        await using (this.CreatePerTaskCancellationToken(out CancellationToken opCancel, cancel))
        {
            AsyncAutoResetEvent networkChangedEvent = new AsyncAutoResetEvent();
            int eventRegisterId = TcpIp.RegisterHostInfoChangedEvent(networkChangedEvent);

            try
            {
                while (opCancel.IsCancellationRequested == false)
                {
                    if (await StartEveryTestAsync(opCancel, networkChangedEvent))
                    {
                        return true;
                    }

                    if (opCancel.IsCancellationRequested) return false;

                    // ネットワーク状態が変化した
                    // 一定時間待って再試行する
                    await opCancel._WaitUntilCanceledAsync(Options.RetryWhenNetworkChanged);
                }
            }
            finally
            {
                TcpIp.UnregisterHostInfoChangedEvent(eventRegisterId);
            }

            return false;
        }
    }

    // ランダムに配列した各テストを 1 秒間隔で順に実行していき、1 つでも成功したら抜ける
    // 1 つでも成功した場合は true、成功するまでにネットワークの状態が変化した場合は false を返す
    async Task<bool> StartEveryTestAsync(CancellationToken cancel, AsyncAutoResetEvent? networkChangedEvent)
    {
        int startNetworkVersion = (networkChangedEvent == null) ? 0 : TcpIp.GetHostInfo(false).InfoVersion;

        CancellationTokenSource cts = new CancellationTokenSource();

        await using (TaskUtil.CreateCombinedCancellationToken(out CancellationToken opCancel, cancel, cts.Token))
        {
            // テストをシャッフルする
            var shuffledTests = Options.GetTestItemList()._Shuffle();

            List<Task<bool>> runningTaskList = new List<Task<bool>>();

            RefBool retBool = new RefBool();

            // シャッフルしたテストを実行する
            int num = 0;
            foreach (var test in shuffledTests)
            {
                Task<bool> t = AsyncAwait(async () =>
                {
                        //Con.WriteDebug($"{test} - Waiting {num} secs...");

                        if (await opCancel._WaitUntilCanceledAsync(1000 * num))
                    {
                            // キャンセルされた
                            return false;
                    }

                    int numRetry = 0;

                    while (true)
                    {
                        if (opCancel.IsCancellationRequested) return false;

                        bool ret = await PerformSingleTestAsync(test, opCancel);

                            //Con.WriteDebug($"{test} - {ret}");

                            if (ret)
                        {
                                // 成功
                                retBool.Set(true);

                                // 自分自信のほか、他のタスクもすべてキャンセルする
                                cts._TryCancelNoBlock();
                            return true;
                        }

                            // 再試行まで待機
                            numRetry++;

                        int retryInterval = Util.GenRandIntervalWithRetry(Options.RetryIntervalMin, numRetry, Options.RetryIntervalMax);

                        if (test is InternetCheckerPingTestItem)
                        {
                            retryInterval = Util.GenRandIntervalWithRetry(Options.RetryIntervalMin, numRetry, Options.RetryIntervalMaxPing);
                        }

                        await TaskUtil.WaitObjectsAsync(cancels: opCancel._SingleArray(), events: networkChangedEvent._SingleArray(),
                            timeout: retryInterval);

                        if (opCancel.IsCancellationRequested)
                        {
                                // キャンセルされた
                                return false;
                        }

                        if (startNetworkVersion != 0)
                        {
                            int currentNetworkVersion = BackgroundState<PalHostNetInfo>.Current.Version;

                            if (startNetworkVersion != currentNetworkVersion)
                            {
                                    // ネットワーク状態が変化した
                                    // 自分自身のほか、他のタスクもすべてキャンセルする
                                    cts._TryCancelNoBlock();
                                return false;
                            }
                        }
                    }
                });

                runningTaskList.Add(t);

                num++;
            }

            // 実行中のテストすべてを待機する
            await Task.WhenAll(runningTaskList);

            return retBool;
        }
    }

    // 1 個のテストを実行する
    public async Task<bool> PerformSingleTestAsync(InternetCheckerTestItemBase item, CancellationToken cancel = default)
    {
        try
        {
            switch (item)
            {
                case InternetCheckerHttpTestItem http:
                    return await PerformSingleHttpTestAsync(http, cancel);

                case InternetCheckerPingTestItem ping:
                    return await PerformSinglePingTestAsync(ping, cancel);

                default:
                    return false;
            }
        }
        catch
        {
            return false;
        }
    }

    // 1 個の Ping テストを実行する
    async Task<bool> PerformSinglePingTestAsync(InternetCheckerPingTestItem ping, CancellationToken cancel = default)
    {
        SendPingReply reply = await this.TcpIp.SendPingAsync(ping.IpAddress, pingTimeout: Options.PingTimeout, pingCancel: cancel, dnsCancel: cancel);

        return reply.Ok;
    }

    // 1 個の HTTP テストを実行する
    async Task<bool> PerformSingleHttpTestAsync(InternetCheckerHttpTestItem http, CancellationToken cancel = default)
    {
        await using ConnSock sock = await this.TcpIp.ConnectAsync(new TcpConnectParam(http.Fqdn, http.Port, http.Family, connectTimeout: Options.HttpTimeout, dnsTimeout: Options.HttpTimeout), cancel);
        await using PipeStream stream = sock.GetStream();
        stream.ReadTimeout = stream.WriteTimeout = Options.HttpTimeout;
        await using StringWriter w = new StringWriter();

        // リクエスト
        w.WriteLine($"GET {http.Path} HTTP/1.1");
        w.WriteLine($"HOST: {http.Fqdn}");
        w.WriteLine();
        byte[] sendData = w.ToString()._GetBytes_UTF8();

        await stream.SendAsync(sendData, cancel);

        // レスポンス
        MemoryBuffer<byte> recvBuffer = new MemoryBuffer<byte>();

        bool ret = false;

        while (recvBuffer.Length <= 65536)
        {
            ReadOnlyMemory<byte> recvData = await stream.ReceiveAsync(1024, cancel);

            if (recvData.IsEmpty)
            {
                break;
            }

            recvBuffer.Write(recvData);

            // 現時点で指定された文字列が受信されているかどうか確認する
            string utf8 = recvBuffer.Memory._GetString_UTF8();
            if (utf8._InStr(http.ExpectedResultString, ignoreCase: true))
            {
                // 受信されていた
                ret = true;
                break;
            }
        }

        return ret;
    }
}

#endif

