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

namespace IPA.Cores.Basic;

public static partial class CoresConfig
{
    public static partial class LogProtocolSettings
    {
        public static readonly Copenhagen<int> DefaultRecvTimeout = 30 * 1000; // 30 secs
        public static readonly Copenhagen<int> DefaultSendKeepAliveInterval = 1 * 1000; // 1 secs

        public static readonly Copenhagen<int> BufferingSizeThresholdPerServer = (16 * 1024 * 1024); // 16MB

        public static readonly Copenhagen<int> MaxDataSize = (64 * 1024 * 1024); // 64MB
    }
}

[Flags]
public enum LogProtocolDataType
{
    StandardLog = 0,
    KeepAlive = 1,
}

public abstract class LogServerOptionsBase : SslServerOptions
{
    public readonly Copenhagen<int> RecvTimeout = CoresConfig.LogProtocolSettings.DefaultRecvTimeout.Value;

    public LogServerOptionsBase(TcpIpSystem tcpIp, PalSslServerAuthenticationOptions sslAuthOptions, string? rateLimiterConfigName = null, params IPEndPoint[] endPoints)
        : base(tcpIp, sslAuthOptions, rateLimiterConfigName, endPoints.Any() ? endPoints : IPUtil.GenerateListeningEndPointsList(false, Consts.Ports.LogServerDefaultServicePort))
    {
    }

    public LogServerOptionsBase(TcpIpSystem? tcpIp, PalSslServerAuthenticationOptions sslAuthOptions, int[] ports, string? rateLimiterConfigName = null)
        : base(tcpIp, sslAuthOptions, rateLimiterConfigName, IPUtil.GenerateListeningEndPointsList(false, ports))
    {
    }
}

public class LogServerReceivedData
{
    public ReadOnlyMemory<byte> BinaryData;
    public LogJsonData? JsonData;

    static readonly StrComparer LocalStrComparer = Lfs.PathParser.PathStringComparer;

    HashSet<string> DestFileNamesHashInternal = new HashSet<string>(LocalStrComparer);

    public IEnumerable<string> DestFileNames => DestFileNamesHashInternal;

    public void AddDestinationFileName(string fileName)
    {
        DestFileNamesHashInternal.Add(fileName);
    }
}

public abstract class LogServerBase : SslServerBase
{
    public const int MagicNumber = 0x415554a4;
    public const int ServerVersion = 1;

    protected new LogServerOptionsBase Options => (LogServerOptionsBase)base.Options;

    protected abstract Task LogReceiveImplAsync(IReadOnlyList<LogServerReceivedData> dataList);

    public LogServerBase(LogServerOptionsBase options) : base(options)
    {
    }

    protected override async Task SslAcceptedImplAsync(NetTcpListenerPort listener, SslSock sock)
    {
        await using (PipeStream st = sock.GetStream())
        {
            await sock.AttachHandle.SetStreamReceiveTimeoutAsync(this.Options.RecvTimeout);

            int magicNumber = await st.ReceiveSInt32Async();
            if (magicNumber != MagicNumber) throw new ApplicationException($"Invalid magicNumber = 0x{magicNumber:X}");

            int clientVersion = await st.ReceiveSInt32Async();

            MemoryBuffer<byte> sendBuffer = new MemoryBuffer<byte>();
            sendBuffer.WriteSInt32(MagicNumber);
            sendBuffer.WriteSInt32(ServerVersion);
            await st.SendAsync(sendBuffer);

            SizedDataQueue<Memory<byte>> standardLogQueue = new SizedDataQueue<Memory<byte>>();
            try
            {
                while (true)
                {
                    if (standardLogQueue.CurrentTotalSize >= CoresConfig.LogProtocolSettings.BufferingSizeThresholdPerServer || st.IsReadyToReceive(sizeof(int)) == false)
                    {
                        var list = standardLogQueue.GetList();
                        standardLogQueue.Clear();
                        await LogDataReceivedInternalAsync(sock.EndPointInfo.RemoteIP._NonNullTrim(), list);
                    }

                    LogProtocolDataType type = (LogProtocolDataType)await st.ReceiveSInt32Async();

                    switch (type)
                    {
                        case LogProtocolDataType.StandardLog:
                            {
                                int size = await st.ReceiveSInt32Async();

                                if (size > CoresConfig.LogProtocolSettings.MaxDataSize)
                                    throw new ApplicationException($"size > MaxDataSize. size = {size}");

                                Memory<byte> data = new byte[size];

                                await st.ReceiveAllAsync(data);

                                standardLogQueue.Add(data, data.Length);

                                break;
                            }

                        case LogProtocolDataType.KeepAlive:
                            break;

                        default:
                            throw new ApplicationException("Invalid LogProtocolDataType");
                    }
                }
            }
            finally
            {
                await LogDataReceivedInternalAsync(sock.EndPointInfo.RemoteIP._NonNullTrim(), standardLogQueue.GetList());
            }
        }
    }

    async Task LogDataReceivedInternalAsync(string srcHostName, IReadOnlyList<Memory<byte>> dataList)
    {
        if (dataList.Count == 0) return;

        List<LogServerReceivedData> list = new List<LogServerReceivedData>();

        foreach (Memory<byte> data in dataList)
        {
            try
            {
                string str = data._GetString_UTF8();

                LogServerReceivedData d = new LogServerReceivedData()
                {
                    BinaryData = data,
                    JsonData = str._JsonToObject<LogJsonData>(),
                };

                d.JsonData!.NormalizeReceivedLog(srcHostName);

                list.Add(d);
            }
            catch (Exception ex)
            {
                Con.WriteError($"LogDataReceivedInternalAsync: {ex.ToString()}");
            }
        }

        if (list.Count >= 1)
        {
            await LogReceiveImplAsync(list);
        }
    }
}

public class LogServerOptions : LogServerOptionsBase
{
    public Action<LogServerReceivedData, LogServerOptions> SetDestinationsProc { get; }

    public FileFlags FileFlags { get; }
    public FileSystem DestFileSystem { get; }
    public string DestRootDirName { get; }

    public LogServerOptions(FileSystem? destFileSystem, string destRootDirName, FileFlags fileFlags, Action<LogServerReceivedData, LogServerOptions>? setDestinationProc, TcpIpSystem? tcpIp, PalSslServerAuthenticationOptions sslAuthOptions, int[] ports, string? rateLimiterConfigName = null)
        : base(tcpIp, sslAuthOptions, ports, rateLimiterConfigName)
    {
        if (setDestinationProc == null) setDestinationProc = LogServer.DefaultSetDestinationsProc;

        this.DestRootDirName = destRootDirName;

        this.FileFlags = fileFlags;

        this.DestFileSystem = destFileSystem ?? Lfs;

        this.DestRootDirName = this.DestFileSystem.PathParser.RemoveLastSeparatorChar(this.DestFileSystem.PathParser.NormalizeDirectorySeparatorAndCheckIfAbsolutePath(this.DestRootDirName));

        this.SetDestinationsProc = setDestinationProc;
    }
}

public class LogServer : LogServerBase
{
    protected new LogServerOptions Options => (LogServerOptions)base.Options;

    public LogServer(LogServerOptions options) : base(options)
    {
    }

    public static void DefaultSetDestinationsProc(LogServerReceivedData data, LogServerOptions options)
    {
        FileSystem fs = options.DestFileSystem;
        PathParser parser = fs.PathParser;
        string root = options.DestRootDirName;
        LogPriority priority = data.JsonData!.Priority._ParseEnum(LogPriority.None);
        LogJsonData d = data.JsonData;
        DateTime date;
        if (d.TimeStamp.HasValue == false)
        {
            date = Util.ZeroDateTimeValue;
        }
        else
        {
            date = d.TimeStamp.Value.LocalDateTime.Date;
        }

        if (d.Kind._IsSamei(LogKind.Default))
        {
            if (priority >= LogPriority.Debug)
                Add($"{d.AppName}/{d.MachineName}/Debug", d.AppName!, "Debug", date);

            if (priority >= LogPriority.Info)
                Add($"{d.AppName}/{d.MachineName}/Info", d.AppName!, "Info", date);

            if (priority >= LogPriority.Error)
                Add($"{d.AppName}/{d.MachineName}/Error", d.AppName!, "Error", date);
        }
        else
        {
            Add($"{d.AppName}/{d.MachineName}/{d.Kind}", d.AppName!, d.Kind!, date);
        }

        void Add(string subDirName, string token0, string token1, DateTime date)
        {
            string yyyymmdd = Str.DateToStrShort(date);

            string tmp = parser.Combine(root, subDirName, $"{yyyymmdd}-{token0}-{token1}.log");

            data.AddDestinationFileName(parser.NormalizeDirectorySeparator(tmp));
        }
    }

    protected override async Task LogReceiveImplAsync(IReadOnlyList<LogServerReceivedData> dataList)
    {
        SingletonSlim<string, MemoryBuffer<byte>> writeBufferList =
            new SingletonSlim<string, MemoryBuffer<byte>>((filename) => new MemoryBuffer<byte>(), this.Options.DestFileSystem.PathParser.PathStringComparer);

        foreach (var data in dataList)
        {
            Options.SetDestinationsProc(data, this.Options);

            foreach (string fileName in data.DestFileNames)
            {
                var buffer = writeBufferList[fileName];

                buffer.Write(data.BinaryData);
                buffer.Write(Str.NewLine_Bytes_Windows);
            }
        }

        bool firstFlag = false;

        // 2020/8/17 パスで並び替えるようにしてみた (そのほうがファイルシステム上高速だという仮定)
        foreach (string fileName in writeBufferList.Keys.OrderBy(x => x, this.Options.DestFileSystem.PathParser.PathStringComparer))
        {
            try
            {
                MemoryBuffer<byte>? buffer = writeBufferList[fileName];

                if (buffer != null)
                {
                    if (firstFlag == false)
                    {
                        // ファイルシステム API が同期モードになっておりディスク I/O に長時間かかる場合を想定し、
                        // 呼び出し元の非同期ソケットタイムアウト検出がおかしくなる問題がありえるため
                        // 1 回は必ず Yield する
                        firstFlag = true;
                        await Task.Yield();
                    }

                    await Options.DestFileSystem.ConcurrentSafeAppendDataToFileAsync(fileName, buffer.Memory, this.Options.FileFlags | FileFlags.AutoCreateDirectory);
                }
            }
            catch (Exception ex)
            {
                Con.WriteError($"LogReceiveImplAsync: Filename '{fileName}' write error: {ex.ToString()}");
            }
        }
    }
}

#endif  // CORES_BASIC_JSON

