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

#if CORES_BASIC_JSON

using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Linq;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System.ComponentModel;
using System.Buffers;

namespace IPA.Cores.Basic;

public static partial class CoresConfig
{
    public static partial class DataVaultProtocolSettings
    {
        public static readonly Copenhagen<int> DefaultDelay = 250;

        public static readonly Copenhagen<int> DefaultRetryIntervalMin = 1 * 1000;
        public static readonly Copenhagen<int> DefaultRetryIntervalMax = 60 * 60 * 1000;
    }

    public static partial class DataVaultClientSettings
    {
        public static readonly Copenhagen<int> TimeoutForFirstConnectEstablishedMsecs = 20 * 1000;
        public static readonly Copenhagen<int> TimeoutForRetrySending = 60 * 1000;
    }
}

public class DataVaultClientOptions
{
    public string ServerHostname { get; }
    public int ServerPort { get; }
    public TcpIpSystem TcpIp { get; }
    public PalSslClientAuthenticationOptions SslAuthOptions { get; }
    public string AccessKey { get; }

    public readonly Copenhagen<int> SendKeepAliveInterval = CoresConfig.DataVaultProtocolSettings.DefaultSendKeepAliveInterval.Value;
    public readonly Copenhagen<int> ClientMaxBufferSize = CoresConfig.DataVaultProtocolSettings.BufferingSizeThresholdPerServer.Value;
    public readonly Copenhagen<int> Delay = CoresConfig.DataVaultProtocolSettings.DefaultDelay.Value;
    public readonly Copenhagen<int> RecvTimeout = CoresConfig.DataVaultProtocolSettings.DefaultRecvTimeout.Value;
    public readonly Copenhagen<int> RetryIntervalMin = CoresConfig.DataVaultProtocolSettings.DefaultRetryIntervalMin.Value;
    public readonly Copenhagen<int> RetryIntervalMax = CoresConfig.DataVaultProtocolSettings.DefaultRetryIntervalMax.Value;

    public DataVaultClientOptions(string serverHostname, string accessKey, PalSslClientAuthenticationOptions sslAuthOptions, int serverPort = Consts.Ports.DataVaultServerDefaultServicePort, TcpIpSystem? tcpIp = null)
    {
        this.ServerHostname = serverHostname._NonNullTrim();
        this.ServerPort = serverPort;
        this.TcpIp = tcpIp ?? LocalNet;
        this.SslAuthOptions = sslAuthOptions;
        this.AccessKey = accessKey;

        if (this.SslAuthOptions.TargetHost._IsEmpty())
        {
            this.SslAuthOptions.TargetHost = this.ServerHostname;
        }
    }
}

public class DataVaultClient : AsyncServiceWithMainLoop
{
    public DataVaultClientOptions Options { get; }

    readonly PipePoint Reader;
    readonly PipePoint Writer;

    readonly Task MainProcTask;

    readonly AsyncAutoResetEvent EmptyEvent = new AsyncAutoResetEvent(true); // すべてのデータが送付されて空になるたびに発生する非同期イベント

    readonly AsyncManualResetEvent FirstConnectionEstablishedEvent = new AsyncManualResetEvent(); // 最初に接続が確立されたときに発生するイベント

    readonly AsyncAutoResetEvent FlushNowEvent = new AsyncAutoResetEvent(); // 今すぐ Flush するべき指示のイベント

    public const int ClientVersion = 1;

    public bool UnderConnectionError { get; private set; } = false;

    public DataVaultClient(DataVaultClientOptions options)
    {
        this.Options = options;

        this.Reader = PipePoint.NewDuplexPipeAndGetOneSide(PipePointSide.A_LowerSide, default, this.Options.ClientMaxBufferSize);
        this.Writer = this.Reader.CounterPart!;

        this.MainProcTask = this.StartMainLoop(MainLoopAsync);
    }

    public async Task WriteCompleteAsync(DataVaultData data, CancellationToken cancel = default)
    {
        data.WriteCompleteFlag = true;
        data.Data = null;

        await WriteDataAsync(data, cancel);
    }

    public async Task WriteDataAsync(DataVaultData data, CancellationToken cancel = default)
    {
        // 最初の接続確立まで待機する
        if (await this.FirstConnectionEstablishedEvent.WaitAsync(timeout: CoresConfig.DataVaultClientSettings.TimeoutForFirstConnectEstablishedMsecs, cancel: cancel) == false)
        {
            throw new CoresLibException("TimeoutForFirstConnectEstablishedMsecs occured.");
        }

        string str = data._ObjectToJson(compact: true);

        byte[] jsonBuf = str._GetBytes_UTF8();
        if (jsonBuf.Length > CoresConfig.DataVaultProtocolSettings.MaxDataSize)
        {
            return;
        }

        MemoryBuffer<byte> buf = new MemoryBuffer<byte>(jsonBuf.Length + sizeof(int) * 2);
        buf.WriteSInt32((int)DataVaultProtocolDataType.StandardData);
        buf.WriteSInt32(jsonBuf.Length);
        buf.Write(jsonBuf);

        long firstErrorTick = 0;

        while (this.Writer.StreamWriter.IsReadyToWrite() == false)
        {
            if (this.UnderConnectionError)
            {
                long now = Time.Tick64;
                if (firstErrorTick == 0)
                {
                    firstErrorTick = now;
                    "DataVaultClient: Connection to the server is temporary disconnected. Retrying."._Error();
                }
                else
                {
                    if (now > (firstErrorTick + CoresConfig.DataVaultClientSettings.TimeoutForRetrySending))
                    {
                        throw new CoresLibException("Connection to the server is disconnected.");
                    }
                }
            }

            await this.Writer.StreamWriter.WaitForReadyToWriteAsync(cancel, 100, noTimeoutException: true);
        }

        this.Writer.StreamWriter.NonStopWriteWithLock(buf, true, FastStreamNonStopWriteMode.ForceWrite, true);
    }

    async Task MainLoopAsync(CancellationToken cancel)
    {
        try
        {
            int numRetry = 0;
            using (PipeStream reader = new PipeStream(this.Reader))
            {
                while (true)
                {
                    if (cancel.IsCancellationRequested) return;

                    try
                    {
                        UnderConnectionError = false;

                        await ConnectAndSendAsync(reader, cancel);
                    }
                    catch (Exception ex)
                    {
                        if (cancel.IsCancellationRequested) return;

                        UnderConnectionError = true;
                        EmptyEvent.Set(true);

                        numRetry++;

                        int nextRetryInterval = Util.GenRandIntervalWithRetry(Options.RetryIntervalMin, numRetry, Options.RetryIntervalMax);

                        Con.WriteError($"DataVaultClient: Error (numRetry = {numRetry}, nextWait = {nextRetryInterval}): {ex.ToString()}");

                        await cancel._WaitUntilCanceledAsync(nextRetryInterval);
                    }
                }
            }
        }
        finally
        {
            EmptyEvent.Set(true);
            UnderConnectionError = true;
        }
    }

    async Task ConnectAndSendAsync(PipeStream reader, CancellationToken cancel)
    {
        using (ConnSock sock = await Options.TcpIp.ConnectIPv4v6DualAsync(new TcpConnectParam(Options.ServerHostname, Options.ServerPort), cancel))
        {
            using (SslSock ssl = new SslSock(sock))
            {
                await ssl.StartSslClientAsync(Options.SslAuthOptions);

                using (PipeStream writer = ssl.GetStream())
                {
                    MemoryBuffer<byte> initSendBuf = new MemoryBuffer<byte>();
                    initSendBuf.WriteSInt32(DataVaultServerBase.MagicNumber);
                    initSendBuf.WriteSInt32(ClientVersion);

                    Memory<byte> accessKeyData = new byte[256];
                    var accessKeyBytes = this.Options.AccessKey._GetBytes_UTF8();
                    accessKeyBytes.CopyTo(accessKeyData);

                    initSendBuf.Write(accessKeyData);

                    await writer.SendAsync(initSendBuf, cancel);

                    writer.ReadTimeout = this.Options.RecvTimeout;
                    MemoryBuffer<byte> initRecvBuf = await writer.ReceiveAllAsync(sizeof(int) * 2, cancel);
                    writer.ReadTimeout = Timeout.Infinite;

                    int magicNumber = initRecvBuf.ReadSInt32();
                    int serverVersion = initRecvBuf.ReadSInt32();

                    if (magicNumber != DataVaultServerBase.MagicNumber)
                    {
                        throw new ApplicationException($"Invalid magicNumber = 0x{magicNumber:X}");
                    }

                    Con.WriteInfo($"DataVaultClient: Connection established.");

                    // 最初に接続が確立されたことを通知
                    FirstConnectionEstablishedEvent.Set(true);

                    LocalTimer keepAliveTimer = new LocalTimer();

                    long nextKeepAlive = keepAliveTimer.AddTimeout(Options.SendKeepAliveInterval);

                    while (true)
                    {
                        if (reader.IsReadyToReceive() == false)
                        {
                            EmptyEvent.Set(true);

                            await TaskUtil.WaitObjectsAsync(cancels: cancel._SingleArray(),
                                events: this.FlushNowEvent._SingleArray(),
                                timeout: this.Options.Delay);

                            cancel.ThrowIfCancellationRequested();

                            if (Time.Tick64 >= nextKeepAlive)
                            {
                                nextKeepAlive = keepAliveTimer.AddTimeout(Options.SendKeepAliveInterval);
                                MemoryBuffer<byte> buf = new MemoryBuffer<byte>();
                                buf.WriteSInt32((int)DataVaultProtocolDataType.KeepAlive);
                                await writer.SendAsync(buf, cancel);
                            }
                        }
                        else
                        {
                            RefInt totalRecvSize = new RefInt();
                            IReadOnlyList<ReadOnlyMemory<byte>> data = await reader.FastPeekAsync(totalRecvSize: totalRecvSize);

                            await writer.FastSendAsync(data.ToArray(), cancel, true);

                            await reader.FastReceiveAsync(cancel, maxSize: totalRecvSize);
                        }
                    }
                }
            }
        }
    }


    protected override async Task CleanupImplAsync(Exception? ex)
    {
        try
        {
            await this.MainProcTask._TryAwait(true);

            await this.Reader._DisposeSafeAsync();
            await this.Writer._DisposeSafeAsync();
        }
        finally
        {
            await base.CleanupImplAsync(ex);
        }
    }
}

#endif  // CORES_BASIC_JSON

