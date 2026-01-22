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
// Telnet / SSH client

#if CORES_BASIC_MISC

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

using Renci.SshNet;
using Renci.SshNet.Common;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using System.IO.Ports;

namespace IPA.Cores.Basic;

public static partial class CoresConfig
{
    public static partial class ShellClientDefaultSettings
    {
        public static readonly Copenhagen<int> ConnectTimeout = 15 * 1000;
        public static readonly Copenhagen<int> CommmTimeout = 15 * 1000;
    }
}

public abstract class ShellClientSettingsBase
{
    public string TargetName { get; }
    public int TargetPort { get; }
    public int ConnectTimeoutMsecs { get; }
    public int CommTimeoutMsecs { get; }

    protected ShellClientSettingsBase(string targetName, int targetPort, int connectTimeoutMsecs = 0, int commTimeoutMsecs = 0)
    {
        if (connectTimeoutMsecs == 0) connectTimeoutMsecs = CoresConfig.ShellClientDefaultSettings.ConnectTimeout;
        if (commTimeoutMsecs == 0) commTimeoutMsecs = CoresConfig.ShellClientDefaultSettings.CommmTimeout;

        TargetName = targetName;
        TargetPort = targetPort;
        ConnectTimeoutMsecs = connectTimeoutMsecs;
        CommTimeoutMsecs = commTimeoutMsecs;
    }
}

public class ShellClientSock : AsyncService
{
    public ShellClientBase Client { get; }
    public PipePoint PipePoint { get; }
    public NetAppStub Stub { get; }
    public PipeStream Stream { get; }

    public ShellClientSock(ShellClientBase client, PipePoint pipePoint, NetAppStub stub, PipeStream stream)
    {
        this.Client = client;
        this.PipePoint = pipePoint;
        this.Stub = stub;
        this.Stream = stream;
    }

    protected override async Task CleanupImplAsync(Exception? ex)
    {
        try
        {
            await this.Stream._DisposeSafeAsync();
            await this.Stub._DisposeSafeAsync();
            await this.PipePoint._DisposeSafeAsync2();
            await this.Client._DisposeSafeAsync();
        }
        finally
        {
            await base.CleanupImplAsync(ex);
        }
    }

    public UnixShellProcessor CreateUnixShellProcessor(UnixShellProcessorSettings? settings = null, bool disposeObject = true)
    {
        if (settings == null)
        {
            settings = new UnixShellProcessorSettings();
        }

        settings.TargetHostName = Client.Settings.TargetName;

        var ret = new UnixShellProcessor(this, disposeObject, settings);

        return ret;
    }
}

public abstract class ShellClientBase : AsyncService
{
    public ShellClientSettingsBase Settings { get; }
    public bool Connected { get; private set; }

    public ShellClientBase(ShellClientSettingsBase settings)
    {
        try
        {
            this.Settings = settings;
        }
        catch
        {
            this._DisposeSafe();
            throw;
        }
    }

    protected abstract Task<PipePoint> ConnectImplAsync(CancellationToken cancel = default);
    protected abstract Task DisconnectImplAsync();

    readonly AsyncLock Lock = new AsyncLock();

    public async Task<ShellClientSock> ConnectAndGetSockAsync(CancellationToken cancel = default)
    {
        PipePoint? pipePoint = null;
        NetAppStub? stub = null;
        PipeStream? stream = null;
        try
        {
            pipePoint = await this.ConnectAsync(cancel);
            stub = pipePoint.GetNetAppProtocolStub();
            stream = stub.GetStream();

            return new ShellClientSock(this, pipePoint, stub, stream);
        }
        catch
        {
            await stream._DisposeSafeAsync();
            await stub._DisposeSafeAsync();
            await pipePoint._DisposeSafeAsync2();
            throw;
        }
    }

    public async Task<PipePoint> ConnectAsync(CancellationToken cancel = default)
    {
        await Task.Yield();

        using (await Lock.LockWithAwait(cancel))
        {
            if (this.Connected)
                throw new CoresException("Already connected.");

            PipePoint ret = await ConnectImplAsync(cancel);

            this.Connected = true;

            return ret;
        }
    }

    protected override async Task CleanupImplAsync(Exception? ex)
    {
        try
        {
            using (await Lock.LockWithAwait())
            {
                if (this.Connected)
                {
                    this.Connected = false;

                    await DisconnectImplAsync();
                }
            }
        }
        finally
        {
            await base.CleanupImplAsync(ex);
        }
    }
}

// SSH 認証方法
[Flags]
public enum SecureShellClientAuthType
{
    Password = 0,
}

// SSH クライアント設定
public class SecureShellClientSettings : ShellClientSettingsBase
{
    public SecureShellClientAuthType AuthType { get; }
    public string Username { get; }
    public string Password { get; }

    public SecureShellClientSettings(string hostAddress, int hostPort, string username, string password, int connectTimeoutMsecs = 0, int commTimeoutMsecs = 0)
        : base(hostAddress, hostPort, connectTimeoutMsecs, commTimeoutMsecs)
    {
        this.AuthType = SecureShellClientAuthType.Password;
        this.Username = username;
        this.Password = password;
    }
}

public class PipePointSshShellStreamWrapper : PipePointAsyncObjectWrapperBase
{
    public ShellStream Stream { get; }
    public override PipeSupportedDataTypes SupportedDataTypes => PipeSupportedDataTypes.Stream;
    public static readonly int SendTmpBufferSize = CoresConfig.BufferSizes.MaxNetworkStreamSendRecvBufferSize;

    public PipePointSshShellStreamWrapper(PipePoint pipePoint, ShellStream stream, CancellationToken cancel = default) : base(pipePoint, cancel)
    {
        try
        {
            this.Stream = stream;

            this.Stream.DataReceived += Stream_DataReceived;

            // この時点で届いているデータを吸い上げる
            int size = 4096;
            while (true)
            {
                Memory<byte> tmp = new byte[size];
                int r = this.Stream.Read(tmp.Span);

                if (r == 0) break;

                var fifo = this.PipePoint.StreamWriter;

                ReadOnlyMemory<byte>[]? recvList = new ReadOnlyMemory<byte>[] { tmp.Slice(0, r) };

                fifo.EnqueueAllWithLock(recvList);

                fifo.CompleteWrite();
            }

            this.StartBaseAsyncLoops();
        }
        catch
        {
            this._DisposeSafe();
            throw;
        }
    }

    private void Stream_DataReceived(object? sender, ShellDataEventArgs e)
    {
        if (e.Data.Length >= 1)
        {
            var fifo = this.PipePoint.StreamWriter;

            ReadOnlyMemory<byte>[]? recvList = new ReadOnlyMemory<byte>[] { e.Data._CloneByte() };

            fifo.EnqueueAllWithLock(recvList);

            fifo.CompleteWrite();
        }
    }

    protected override async Task StreamWriteToObjectImplAsync(FastStreamBuffer fifo, CancellationToken cancel)
    {
        await TaskUtil.DoAsyncWithTimeout(
            async c =>
            {
                bool flush = false;

                using (MemoryHelper.FastAllocMemoryWithUsing(SendTmpBufferSize, out Memory<byte> buffer))
                {
                    while (true)
                    {
                        int size = fifo.DequeueContiguousSlowWithLock(buffer);
                        if (size == 0)
                            break;

                        var dataToSend = buffer.Slice(0, size);

                        await Stream.WriteAsync(dataToSend, cancel);
                        flush = true;
                    }
                }

                if (flush)
                    await Stream.FlushAsync(cancel);

                return 0;
            },
            cancel: cancel);
    }

    protected override async Task<bool> StreamReadFromObjectImplAsync(FastStreamBuffer fifo, CancellationToken cancel)
    {
        await cancel._WaitUntilCanceledAsync();

        return true;
    }

    protected override Task DatagramWriteToObjectImplAsync(FastDatagramBuffer fifo, CancellationToken cancel)
        => throw new NotImplementedException();
    protected override Task DatagramReadFromObjectImplAsync(FastDatagramBuffer fifo, CancellationToken cancel)
        => throw new NotImplementedException();
}

// SSH クライアント
public class SecureShellClient : ShellClientBase
{
    public new SecureShellClientSettings Settings => (SecureShellClientSettings)base.Settings;

    SshClient? Ssh = null;
    PipePoint? PipePointMySide = null;
    PipePoint? PipePointUserSide = null;
    PipePointSshShellStreamWrapper? PipePointWrapper = null;
    ShellStream? Stream = null;

    public SecureShellClient(SecureShellClientSettings settings) : base(settings)
    {
    }

    protected override async Task<PipePoint> ConnectImplAsync(CancellationToken cancel = default)
    {
        await Task.CompletedTask;

        cancel.ThrowIfCancellationRequested();

        SshClient ssh;

        switch (Settings.AuthType)
        {
            case SecureShellClientAuthType.Password:
                ssh = new SshClient(Settings.TargetName, Settings.TargetPort, Settings.Username, Settings.Password);
                break;

            default:
                throw new ArgumentException(nameof(Settings.AuthType));
        }

        ShellStream? stream = null;
        PipePoint? pipePointMySide = null;
        PipePoint? pipePointUserSide = null;
        PipePointSshShellStreamWrapper? pipePointWrapper = null;

        try
        {
            ssh.ConnectionInfo.Timeout = new TimeSpan(0, 0, 0, 0, Settings.ConnectTimeoutMsecs);

            ssh.Connect();

            stream = ssh.CreateShellStream("test", uint.MaxValue, uint.MaxValue, uint.MaxValue, uint.MaxValue, 65536);

            //while (true)
            //{
            //    byte[] data = new byte[256];

            //    int x = stream.Read(data, 0, data.Length);
            //    x._Print();
            //}

            // Stream に標準入出力を接続する
            pipePointMySide = PipePoint.NewDuplexPipeAndGetOneSide(PipePointSide.A_LowerSide);
            pipePointUserSide = pipePointMySide.CounterPart._NullCheck();
            pipePointWrapper = new PipePointSshShellStreamWrapper(pipePointMySide, stream);

            this.PipePointMySide = pipePointMySide;
            this.PipePointUserSide = pipePointUserSide;
            this.PipePointWrapper = pipePointWrapper;

            this.Ssh = ssh;
            this.Stream = stream;

            return this.PipePointUserSide;
        }
        catch
        {
            pipePointMySide._DisposeSafe();
            pipePointUserSide._DisposeSafe();
            pipePointWrapper._DisposeSafe();
            stream._DisposeSafe();
            ssh._DisposeSafe();

            throw;
        }
    }

    protected override async Task DisconnectImplAsync()
    {
        await this.PipePointMySide._DisposeSafeAsync2();
        await this.PipePointWrapper._DisposeSafeAsync2();

        TaskUtil.StartAsyncTaskAsync(async () =>
        {
                // To prevent deadlock: SSH.net bug
                await this.Stream._DisposeSafeAsync2();
            await this.Ssh._DisposeSafeAsync2();
        })._LaissezFaire();

        this.PipePointMySide = null;
        this.PipePointWrapper = null;

        // To prevent deadlock: SSH.net bug
        //this.Stream = null;
        //this.Ssh = null;
    }

    public static void RunStartupTest()
    {
        using var c = new SshClient("127.0.0.1", 22, "root", "test");
    }
}


// COM ポート設定
public class ComPortSettings : ShellClientSettingsBase
{
    public int BaudRate { get; }
    public Parity Parity { get; }
    public int DataBits { get; }
    public StopBits StopBits { get; }
    public Handshake Handshake { get; }

    public ComPortSettings(string targetName, int connectTimeoutMsecs = 0, int commTimeoutMsecs = 0, int baudRate = 9600, Parity parity = Parity.None, int dataBits = 8, StopBits stopBits = StopBits.One, Handshake handshake = Handshake.None)
        : base(targetName, 0, connectTimeoutMsecs, commTimeoutMsecs)
    {
        BaudRate = baudRate;
        Parity = parity;
        DataBits = dataBits;
        StopBits = stopBits;
        Handshake = handshake;
    }
}

// COM ポート用 Stream ラッパー
public class ComPortStreamWrapper : PipePointAsyncObjectWrapperBase
{
    public SerialPort SerialPort { get; }
    public Stream Stream { get; }
    public override PipeSupportedDataTypes SupportedDataTypes => PipeSupportedDataTypes.Stream;
    public static readonly int RecvTmpBufferSize = CoresConfig.BufferSizes.MaxNetworkStreamSendRecvBufferSize;
    public static readonly int SendTmpBufferSize = CoresConfig.BufferSizes.MaxNetworkStreamSendRecvBufferSize;

    public ComPortStreamWrapper(PipePoint pipePoint, SerialPort serialPort, CancellationToken cancel = default)
        : base(pipePoint, cancel)
    {
        try
        {
            this.SerialPort = serialPort;
            this.Stream = this.SerialPort.BaseStream;

            this.StartBaseAsyncLoops();
        }
        catch
        {
            this._DisposeSafe();
            throw;
        }
    }

    // 抽象 Stream --> シリアルポート
    protected override async Task StreamWriteToObjectImplAsync(FastStreamBuffer fifo, CancellationToken cancel)
    {
        if (this.SerialPort.IsOpen == false) throw new FastBufferDisconnectedException();

        await TaskUtil.DoAsyncWithTimeout(
            async c =>
            {
                bool flush = false;

                using (MemoryHelper.FastAllocMemoryWithUsing(SendTmpBufferSize, out Memory<byte> buffer))
                {
                    while (true)
                    {
                        int size = fifo.DequeueContiguousSlowWithLock(buffer);
                        if (size == 0)
                            break;

                        await Stream.WriteAsync(buffer.Slice(0, size), cancel);
                        flush = true;
                    }
                }

                if (flush)
                    await Stream.FlushAsync(cancel);

                return 0;
            },
            cancelProc: () =>
            {
                this.SerialPort.Close();
            },
            cancel: cancel);
    }

    // シリアルポート --> 抽象 Stream
    protected override async Task<bool> StreamReadFromObjectImplAsync(FastStreamBuffer fifo, CancellationToken cancel)
    {
        if (this.SerialPort.IsOpen == false) throw new FastBufferDisconnectedException();

        await TaskUtil.DoAsyncWithTimeout(
            async c =>
            {
                using (MemoryHelper.FastAllocMemoryWithUsing(RecvTmpBufferSize, out Memory<byte> buffer))
                {
                    int recvSize = await this.Stream.ReadAsync(buffer, cancel);

                    if (recvSize <= 0)
                    {
                        fifo.Disconnect();
                        return 0;
                    }

                    fifo.EnqueueWithLock(buffer._SliceHead(recvSize)._CloneMemory(), true);
                }

                return 0;
            },
            cancelProc: () =>
            {
                this.SerialPort.Close();
            },
            cancel: cancel);

        return true;
    }

    protected override Task DatagramWriteToObjectImplAsync(FastDatagramBuffer fifo, CancellationToken cancel)
        => throw new NotImplementedException();

    protected override Task DatagramReadFromObjectImplAsync(FastDatagramBuffer fifo, CancellationToken cancel)
        => throw new NotImplementedException();
}

// COM ポートクライアント
public class ComPortClient : ShellClientBase
{
    public new ComPortSettings Settings => (ComPortSettings)base.Settings;

    PipePoint? PipePointMySide = null;
    PipePoint? PipePointUserSide = null;
    ComPortStreamWrapper? PipePointWrapper = null;

    SerialPort? SerialPort = null;

    public ComPortClient(ComPortSettings settings) : base(settings)
    {
    }

    public static string[] GetPortTargetNames()
    {
        return SerialPort.GetPortNames().Distinct().OrderBy(x => x, StrComparer.IgnoreCaseComparer).ToArray();
    }

    protected override async Task<PipePoint> ConnectImplAsync(CancellationToken cancel = default)
    {
        await Task.CompletedTask;

        cancel.ThrowIfCancellationRequested();

        PipePoint? pipePointMySide = null;
        PipePoint? pipePointUserSide = null;
        ComPortStreamWrapper? pipePointWrapper = null;

        SerialPort port = new SerialPort(this.Settings.TargetName, this.Settings.BaudRate, this.Settings.Parity, this.Settings.DataBits, this.Settings.StopBits);
        try
        {
            port.Handshake = this.Settings.Handshake;

            if (Settings.CommTimeoutMsecs >= 1)
            {
                port.ReadTimeout = Settings.CommTimeoutMsecs;
            }

            port.Open();

            this.SerialPort = port;

            pipePointMySide = PipePoint.NewDuplexPipeAndGetOneSide(PipePointSide.A_LowerSide);
            pipePointUserSide = pipePointMySide.CounterPart._NullCheck();
            pipePointWrapper = new ComPortStreamWrapper(pipePointMySide, this.SerialPort);

            this.PipePointMySide = pipePointMySide;
            this.PipePointUserSide = pipePointUserSide;
            this.PipePointWrapper = pipePointWrapper;

            return this.PipePointUserSide;
        }
        catch
        {
            pipePointMySide._DisposeSafe();
            pipePointUserSide._DisposeSafe();
            pipePointWrapper._DisposeSafe();

            port._DisposeSafe();
            throw;
        }
    }

    protected override async Task DisconnectImplAsync()
    {
        await this.PipePointMySide._DisposeSafeAsync2();
        await this.PipePointWrapper._DisposeSafeAsync2();
        await this.SerialPort._DisposeSafeAsync2();

        this.PipePointMySide = null;
        this.PipePointWrapper = null;
        this.SerialPort = null;
    }

    public static void RunStartupTest()
    {
        using var x = new SerialPort("test", 9600);
    }
}

#endif  // CORES_BASIC_MISC

