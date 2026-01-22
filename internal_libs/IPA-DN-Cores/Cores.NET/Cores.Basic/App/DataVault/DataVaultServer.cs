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
    public static partial class DataVaultProtocolSettings
    {
        public static readonly Copenhagen<int> DefaultRecvTimeout = 30 * 1000; // 30 secs
        public static readonly Copenhagen<int> DefaultSendKeepAliveInterval = 1 * 1000; // 1 secs

        public static readonly Copenhagen<int> BufferingSizeThresholdPerServer = (16 * 1024 * 1024); // 16MB

        public static readonly Copenhagen<int> MaxDataSize = (64 * 1024 * 1024); // 64MB
    }
}


public class DataVaultData : ICloneable
{
    public DateTimeOffset TimeStamp;

    // Stat 系
    public string? StatUid;
    public string? StatAppVer;
    public string? StatGitCommitId;
    public string? StatGlobalIp;
    public string? StatGlobalFqdn;
    public int? StatGlobalPort;
    public string? StatLocalIp;
    public string? StatLocalFqdn;

    // 標準データ
    public string? SystemName;
    public string? LogName;
    public string? KeyType;
    public string? KeyShortValue;
    public string? KeyFullValue;
    public bool WithTime;
    public bool? WriteCompleteFlag;
    public object? Data;

    static readonly PathParser WinParser = PathParser.GetInstance(FileSystemStyle.Windows);

    public void NormalizeReceivedData(string? unused = null)
    {
        if (this.KeyType._IsEmpty()) this.KeyType = "all";
        if (this.KeyShortValue._IsEmpty()) this.KeyShortValue = "all";
        if (this.KeyFullValue._IsEmpty()) this.KeyFullValue = "all";

        this.SystemName = WinParser.MakeSafeFileName(this.SystemName._NonNullTrim()).ToLowerInvariant()._TruncStr(Consts.MaxLens.DataVaultPathElementMaxLen);
        this.LogName = WinParser.MakeSafeFileName(this.LogName._NonNullTrim()).ToLowerInvariant()._TruncStr(Consts.MaxLens.DataVaultPathElementMaxLen);
        this.KeyType = WinParser.MakeSafeFileName(this.KeyType._NonNullTrim()).ToLowerInvariant()._TruncStr(Consts.MaxLens.DataVaultPathElementMaxLen);
        this.KeyShortValue = WinParser.MakeSafeFileName(this.KeyShortValue._NonNullTrim()).ToLowerInvariant()._TruncStr(Consts.MaxLens.DataVaultPathElementMaxLen);
        this.KeyFullValue = WinParser.MakeSafeFileName(this.KeyFullValue._NonNullTrim()).ToLowerInvariant()._TruncStr(Consts.MaxLens.DataVaultPathElementMaxLen);

        if (this.TimeStamp == default) this.TimeStamp = Util.ZeroDateTimeOffsetValue;
    }

    public object Clone() => this.MemberwiseClone();
}

[Flags]
public enum DataVaultProtocolDataType
{
    StandardData = 0,
    KeepAlive = 1,
}

public abstract class DataVaultServerOptionsBase : SslServerOptions
{
    public readonly Copenhagen<int> RecvTimeout = CoresConfig.DataVaultProtocolSettings.DefaultRecvTimeout.Value;

    public string AccessKey { get; }

    public DataVaultServerOptionsBase(TcpIpSystem? tcpIp, PalSslServerAuthenticationOptions sslAuthOptions, int[] ports, string accessKey, string? rateLimiterConfigName = null)
        : base(tcpIp, sslAuthOptions, rateLimiterConfigName, IPUtil.GenerateListeningEndPointsList(false, ports))
    {
        this.AccessKey = accessKey;
    }
}

public class DataVaultServerReceivedData
{
    public ReadOnlyMemory<byte> BinaryData;
    public DataVaultData? JsonData;

    static readonly StrComparer LocalStrComparer = Lfs.PathParser.PathStringComparer;

    HashSet<string> DestFileNamesHashInternal = new HashSet<string>(LocalStrComparer);

    public IEnumerable<string> DestFileNames => DestFileNamesHashInternal;

    public void AddDestinationFileName(string fileName)
    {
        DestFileNamesHashInternal.Add(fileName);
    }
}

public abstract class DataVaultServerBase : SslServerBase
{
    public const int MagicNumber = 0x415554a4;
    public const int ServerVersion = 1;

    protected new DataVaultServerOptionsBase Options => (DataVaultServerOptionsBase)base.Options;

    protected abstract Task DataVaultReceiveImplAsync(IReadOnlyList<DataVaultServerReceivedData> dataList);

    public DataVaultServerBase(DataVaultServerOptionsBase options) : base(options)
    {
    }

    protected override async Task SslAcceptedImplAsync(NetTcpListenerPort listener, SslSock sock)
    {
        using (PipeStream st = sock.GetStream())
        {
            await sock.AttachHandle.SetStreamReceiveTimeoutAsync(this.Options.RecvTimeout);

            int magicNumber = await st.ReceiveSInt32Async();
            if (magicNumber != MagicNumber) throw new ApplicationException($"Invalid magicNumber = 0x{magicNumber:X}");

            int clientVersion = await st.ReceiveSInt32Async();

            var accessKeyData = await st.ReceiveAllAsync(256);
            if (accessKeyData._GetString_UTF8(untilNullByte: true)._IsSame(this.Options.AccessKey) == false)
            {
                throw new ApplicationException($"Invalid access key");
            }

            MemoryBuffer<byte> sendBuffer = new MemoryBuffer<byte>();
            sendBuffer.WriteSInt32(MagicNumber);
            sendBuffer.WriteSInt32(ServerVersion);
            await st.SendAsync(sendBuffer);

            SizedDataQueue<Memory<byte>> standardDataQueue = new SizedDataQueue<Memory<byte>>();
            try
            {
                while (true)
                {
                    if (standardDataQueue.CurrentTotalSize >= CoresConfig.DataVaultProtocolSettings.BufferingSizeThresholdPerServer || st.IsReadyToReceive(sizeof(int)) == false)
                    {
                        var list = standardDataQueue.GetList();
                        standardDataQueue.Clear();
                        await DataVaultDataReceivedInternalAsync(list);
                    }

                    DataVaultProtocolDataType type = (DataVaultProtocolDataType)await st.ReceiveSInt32Async();

                    switch (type)
                    {
                        case DataVaultProtocolDataType.StandardData:
                            {
                                int size = await st.ReceiveSInt32Async();

                                if (size > CoresConfig.DataVaultProtocolSettings.MaxDataSize)
                                    throw new ApplicationException($"size > MaxDataSize. size = {size}");

                                Memory<byte> data = new byte[size];

                                await st.ReceiveAllAsync(data);

                                standardDataQueue.Add(data, data.Length);

                                break;
                            }

                        case DataVaultProtocolDataType.KeepAlive:
                            break;

                        default:
                            throw new ApplicationException("Invalid DataVaultProtocolDataType");
                    }
                }
            }
            finally
            {
                await DataVaultDataReceivedInternalAsync(standardDataQueue.GetList());
            }
        }
    }

    async Task DataVaultDataReceivedInternalAsync(IReadOnlyList<Memory<byte>> dataList)
    {
        if (dataList.Count == 0) return;

        List<DataVaultServerReceivedData> list = new List<DataVaultServerReceivedData>();

        foreach (Memory<byte> data in dataList)
        {
            try
            {
                string str = data._GetString_UTF8();

                DataVaultServerReceivedData d = new DataVaultServerReceivedData()
                {
                    BinaryData = data,
                    JsonData = str._JsonToObject<DataVaultData>(),
                };

                if (d.JsonData == null) continue;

                d.JsonData!.NormalizeReceivedData();

                list.Add(d);
            }
            catch (Exception ex)
            {
                Con.WriteError($"DataVaultDataReceivedInternalAsync: {ex.ToString()}");
            }
        }

        if (list.Count >= 1)
        {
            await DataVaultReceiveAsync(list);
        }
    }

    public Task DataVaultReceiveAsync(IReadOnlyList<DataVaultServerReceivedData> dataList)
    {
        if (dataList.Any() == false)
            return Task.CompletedTask;

        return DataVaultReceiveImplAsync(dataList);
    }
}

[Flags]
public enum DataVaultServerFlags : long
{
    None = 0,
    UseConcurrentSafeAppendDataToFileAsync = 1,

    Default = UseConcurrentSafeAppendDataToFileAsync,
}

public class DataVaultServerOptions : DataVaultServerOptionsBase
{
    public Action<DataVaultServerReceivedData, DataVaultServerOptions> SetDestinationsProc { get; }

    public FileFlags FileFlags { get; }
    public FileSystem DestFileSystem { get; }
    public string DestRootDirName { get; }
    public DataVaultServerFlags ServerFlags { get; }

    public DataVaultServerOptions(FileSystem? destFileSystem, string destRootDirName, FileFlags fileFlags, Action<DataVaultServerReceivedData, DataVaultServerOptions>? setDestinationProc,
        TcpIpSystem? tcpIp, PalSslServerAuthenticationOptions sslAuthOptions, int[] ports, string accessKey, DataVaultServerFlags serverFlags = DataVaultServerFlags.Default, string? rateLimiterConfigName = null)
        : base(tcpIp, sslAuthOptions, ports, accessKey, rateLimiterConfigName)
    {
        if (setDestinationProc == null) setDestinationProc = DataVaultServer.DefaultSetDestinationsProc;

        this.DestRootDirName = destRootDirName;

        this.ServerFlags = serverFlags;

        this.FileFlags = fileFlags;

        this.DestFileSystem = destFileSystem ?? Lfs;

        this.DestRootDirName = this.DestFileSystem.PathParser.RemoveLastSeparatorChar(this.DestFileSystem.PathParser.NormalizeDirectorySeparatorAndCheckIfAbsolutePath(this.DestRootDirName));

        this.SetDestinationsProc = setDestinationProc;
    }
}

public class DataVaultServer : DataVaultServerBase
{
    protected new DataVaultServerOptions Options => (DataVaultServerOptions)base.Options;

    public DataVaultServer(DataVaultServerOptions options) : base(options)
    {
    }

    public static void DefaultSetDestinationsProc(DataVaultServerReceivedData data, DataVaultServerOptions options)
    {
        FileSystem fs = options.DestFileSystem;
        PathParser parser = fs.PathParser;
        string root = options.DestRootDirName;
        DataVaultData d = data.JsonData!;

        string timeStampStr;

        if (d.WithTime == false)
        {
            timeStampStr = Str.DateToStrShort(d.TimeStamp.LocalDateTime.Date);
        }
        else
        {
            timeStampStr = Str.DateTimeToStrShortWithMilliSecs(d.TimeStamp.LocalDateTime);
        }

        string relativePath = Str.CombineStringArray("/", d.SystemName, d.LogName, d.KeyType, d.KeyShortValue, d.KeyFullValue,
            $"{timeStampStr}-{d.SystemName}-{d.LogName}-{d.KeyType}-{d.KeyFullValue}.json");

        if (d.WriteCompleteFlag ?? false)
        {
            relativePath += ".completed";
        }

        data.AddDestinationFileName(parser.NormalizeDirectorySeparator(parser.Combine(root, relativePath)));
    }

    protected override async Task DataVaultReceiveImplAsync(IReadOnlyList<DataVaultServerReceivedData> dataList)
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

                    if (Options.ServerFlags.Bit(DataVaultServerFlags.UseConcurrentSafeAppendDataToFileAsync) == false)
                    {
                        // 従来のモード。動作重い？ btrfs がバグる
                        var handle = await Options.DestFileSystem.GetRandomAccessHandleAsync(fileName, true, this.Options.FileFlags | FileFlags.AutoCreateDirectory);
                        var concurrentHandle = handle.GetConcurrentRandomAccess();

                        await concurrentHandle.AppendWithLargeFsAutoPaddingAsync(buffer.Memory);

                        await concurrentHandle.FlushAsync();
                    }
                    else
                    {
                        // 新しいモード。軽いことを期待 2020/8/16
                        await Options.DestFileSystem.ConcurrentSafeAppendDataToFileAsync(fileName, buffer.Memory, this.Options.FileFlags | FileFlags.AutoCreateDirectory);
                    }
                }
            }
            catch (Exception ex)
            {
                Con.WriteError($"DataVaultReceiveImplAsync: Filename '{fileName}' write error: {ex.ToString()}");
            }
        }
    }
}

#endif  // CORES_BASIC_JSON

