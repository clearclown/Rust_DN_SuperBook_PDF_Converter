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
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Buffers;
using System.Diagnostics;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using System.Text;

namespace IPA.Cores.Basic;

public static partial class CoresConfig
{
    public static partial class LazyWriteBufferSettings
    {
        public static readonly Copenhagen<int> DefaultBufferSize = 4 * 1024 * 1024;
        public static readonly Copenhagen<int> DefaultDefragmentWriteBlockSize = 1 * 1024 * 1024;
        public static readonly Copenhagen<int> DefaultDelay = 250;

        public static readonly Copenhagen<int> ErrorRetryIntervalStd = 1 * 1000;
        public static readonly Copenhagen<int> ErrorRetryIntervalMax = 10 * 1000;
    }
}

public class LazyBufferFileEmitterOptions : LazyBufferEmitterOptionsBase
{
    public FilePath FilePath { get; }
    public bool AppendMode { get; }
    public ReadOnlyMemory<byte> FileHeaderData { get; }

    public LazyBufferFileEmitterOptions(FilePath filePath, bool appendMode = true, int delay = 0, int defragmentWriteBlockSize = 0, ReadOnlyMemory<byte> fileHeaderData = default) : base(delay, defragmentWriteBlockSize)
    {
        this.FilePath = filePath;
        this.AppendMode = appendMode;
        this.FileHeaderData = fileHeaderData;
    }
}

public abstract class LazyBufferEmitterOptionsBase
{
    public int DefragmentWriteBlockSize { get; }
    public int Delay { get; }

    public LazyBufferEmitterOptionsBase(int delay = 0, int defragmentWriteBlockSize = 0)
    {
        if (defragmentWriteBlockSize <= 0) defragmentWriteBlockSize = CoresConfig.LazyWriteBufferSettings.DefaultDefragmentWriteBlockSize;
        if (delay <= 0) delay = CoresConfig.LazyWriteBufferSettings.DefaultDelay;

        this.DefragmentWriteBlockSize = defragmentWriteBlockSize;
        this.Delay = delay;
    }
}

public class LazyBufferFileEmitter : LazyBufferEmitterBase
{
    public new LazyBufferFileEmitterOptions Options => (LazyBufferFileEmitterOptions)base.Options;

    FileObject? file = null;

    public LazyBufferFileEmitter(LazyBufferFileEmitterOptions options) : base(options) { }

    public override async Task EmitAsync(IReadOnlyList<ReadOnlyMemory<byte>> dataToWrite, CancellationToken cancel = default)
    {
        if (IsClosed) throw new ObjectDisposedException("LazyBufferFileEmitter");

        bool firstOnFile = false;

        if (file == null)
        {
            if (this.Options.AppendMode)
            {
                file = await this.Options.FilePath.OpenOrCreateAppendAsync(additionalFlags: FileFlags.AutoCreateDirectory, cancel: cancel);
                if (file.Position == 0)
                {
                    firstOnFile = true;
                }
            }
            else
            {
                file = await this.Options.FilePath.CreateAsync(additionalFlags: FileFlags.AutoCreateDirectory, cancel: cancel);
                firstOnFile = true;
            }
        }

        if (firstOnFile)
        {
            if (this.Options.FileHeaderData.IsEmpty == false)
            {
                await file.WriteAsync(this.Options.FileHeaderData);
            }
        }

        foreach (ReadOnlyMemory<byte> data in dataToWrite)
        {
            try
            {
                await file!.WriteAsync(data, cancel);
            }
            catch (Exception ex)
            {
                Con.WriteDebug($"LazyBufferFileEmitter -> EmitAsync: WriteAsync('{this.Options.FilePath.ToString()}' failed. error: {ex.ToString()}");
                await cancel._WaitUntilCanceledAsync(1000);
                file = null;
            }
        }
    }

    public override Task FlushAsync(CancellationToken cancel = default)
    {
        if (IsClosed) return Task.CompletedTask;
        return file!.FlushAsync(cancel);
    }

    bool IsClosed = false;

    public override async Task CloseAsync()
    {
        IsClosed = true;

        if (file != null)
        {
            await file.CloseAsync();
            file._DisposeSafe();
            file = null;
        }
    }
}

public abstract class LazyBufferEmitterBase
{
    public LazyBufferEmitterOptionsBase Options { get; }

    public LazyBufferEmitterBase(LazyBufferEmitterOptionsBase options)
    {
        this.Options = options;
    }

    public abstract Task EmitAsync(IReadOnlyList<ReadOnlyMemory<byte>> dataToWrite, CancellationToken cancel = default);
    public abstract Task FlushAsync(CancellationToken cancel = default);
    public abstract Task CloseAsync();
}

public class LazyBufferOptions
{
    public int BufferSize { get; }
    public FastStreamNonStopWriteMode OverflowBehavior { get; }

    public LazyBufferOptions(FastStreamNonStopWriteMode overflowBehavior = FastStreamNonStopWriteMode.DiscardWritingData, int bufferSize = 0)
    {
        if (bufferSize <= 0) bufferSize = CoresConfig.LazyWriteBufferSettings.DefaultBufferSize;

        this.OverflowBehavior = overflowBehavior;
        this.BufferSize = bufferSize;
    }
}

public class LazyBuffer : AsyncServiceWithMainLoop
{
    public LazyBufferOptions Options { get; }

    LazyBufferEmitterBase? Emitter;

    PipePoint Reader;
    PipePoint Writer;

    public LazyBuffer(LazyBufferEmitterBase? initialEmitter = null, LazyBufferOptions? options = null, CancellationToken cancel = default) : base(cancel)
    {
        if (options == null) options = new LazyBufferOptions();

        this.Options = options;

        this.Reader = PipePoint.NewDuplexPipeAndGetOneSide(PipePointSide.A_LowerSide, this.GrandCancel, Options.BufferSize);

        this.Writer = this.Reader.CounterPart._NullCheck();

        if (initialEmitter != null)
        {
            RegisterEmitter(initialEmitter);
        }
    }

    Once RegisterOnce;

    public void RegisterEmitter(LazyBufferEmitterBase emitter)
    {
        if (emitter == null) throw new ArgumentNullException("emitter");
        if (this.IsCanceled) throw new ApplicationException("The object is canceled.");
        if (RegisterOnce.IsFirstCall() == false) throw new ApplicationException("RegisterReader is already called.");

        this.Emitter = emitter;

        this.StartMainLoop(ReadMainLoop);
    }

    public void Write(ReadOnlyMemory<byte> data)
    {
        if (this.IsCanceled) return;

        try
        {
            this.Writer.StreamWriter.NonStopWriteWithLock(data, false, this.Options.OverflowBehavior, true);
        }
        catch { }
    }

    public IReadOnlyList<ReadOnlyMemory<byte>> DequeueAll(out long totalSize, int defragmentWriteBlockSize = 0)
    {
        IReadOnlyList<ReadOnlyMemory<byte>> dataToWrite = this.Reader.StreamReader.DequeueAllWithLock(out totalSize);

        if (totalSize >= 1)
        {
            dataToWrite = Util.DefragmentMemoryArrays(dataToWrite, defragmentWriteBlockSize);
        }

        return dataToWrite;
    }

    async Task ReadMainLoop(CancellationToken cancel)
    {
        try
        {
            var st = this.Reader.StreamReader;
            int numFailed = 0;

            while (true)
            {
                await TaskUtil.AwaitWithPollAsync(Timeout.Infinite, this.Emitter!.Options.Delay, () => this.Reader.StreamReader.IsReadyToRead(), cancel);

                IReadOnlyList<ReadOnlyMemory<byte>> dataToWrite = DequeueAll(out long totalSize, this.Emitter.Options.DefragmentWriteBlockSize);
                if (totalSize == 0)
                {
                    if (cancel.IsCancellationRequested)
                    {
                        break;
                    }
                    else
                    {
                        continue;
                    }
                }

                L_RETRY:

                try
                {
                    await Emitter.EmitAsync(dataToWrite);

                    numFailed = 0;

                    if (this.Reader.StreamReader.IsReadyToRead() == false)
                    {
                        await Emitter.FlushAsync();
                    }
                }
                catch (Exception ex)
                {
                    ex._Debug();
                    numFailed++;

                    if (cancel.IsCancellationRequested == false)
                    {
                        await cancel._WaitUntilCanceledAsync(Util.GenRandIntervalWithRetry(CoresConfig.LazyWriteBufferSettings.ErrorRetryIntervalStd, numFailed, CoresConfig.LazyWriteBufferSettings.ErrorRetryIntervalMax));
                        goto L_RETRY;
                    }
                    else
                    {
                        break;
                    }
                }
            }
        }
        finally
        {
            try
            {
                await Emitter!.CloseAsync();
            }
            catch { }
        }
    }

    protected override async Task CleanupImplAsync(Exception? ex)
    {
        try
        {
            await this.Reader._DisposeSafeAsync(ex);
            await this.Writer._DisposeSafeAsync(ex);
        }
        finally
        {
            await base.CleanupImplAsync(ex);
        }
    }

    public void StartLazyFileEmitter(LazyBufferFileEmitterOptions options)
    {
        LazyBufferFileEmitter emit = new LazyBufferFileEmitter(options);

        try
        {
            this.RegisterEmitter(emit);
        }
        catch
        {
            emit.CloseAsync()._TryGetResult(true);
            throw;
        }
    }
}

