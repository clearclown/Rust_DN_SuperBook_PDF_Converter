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

#pragma warning disable CA2235 // Mark all non-serializable fields

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Linq;

using IPA.Cores.Basic;
using IPA.Cores.Basic.Legacy;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

namespace IPA.Cores.Basic;

public static partial class CoresConfig
{
    public static partial class Logger
    {
        public static readonly Copenhagen<long> DefaultAutoDeleteTotalMinSize = 4000000000L; // 4GB
        public static readonly Copenhagen<long> DefaultAutoDeleteTotalMinSize_ForStat = 100000000L; // 100MB
        public static readonly Copenhagen<long> DefaultMaxLogSize = 1073741823L; // 1GB
        public static readonly Copenhagen<int> DefaultMaxPendingRecords = 10000;
        public static readonly Copenhagen<int> EraserIntervalMsecs = 10 * 60 * 1000; // 10 mins
    }
}

[Flags]
public enum LogSwitchType
{
    None,
    Second,
    Minute,
    Hour,
    Day,
    Month,
}

[Flags]
public enum LogPendingTreatment
{
    Discard,
    Wait,
    WriteForcefully,
}

[Flags]
public enum LogPriority
{
    Trace = 0,
    Debug = 1,
    Info = 2,
    Error = 4,
    None = 0x7FFFFFFF,
}

[Serializable]
public class LogInfoOptions
{
    public bool WithTimeStamp = true;
    public bool WithGuid = false;
    public bool WithMachineName = false;
    public bool WithAppName = false;
    public bool WithKind = false;
    public bool WithPriority = false;
    public bool WithTag = false;
    public bool WithTypeName = false;

    public string? NewLineForString = "\r\n";
    public string? ObjectPrintSeparator = ", ";

    public string? MachineName = Str.GetSimpleHostnameFromFqdn(Env.MachineName);
    public string? AppName = Env.ExeAssemblySimpleName;
    public string? Kind = "";

    public bool WriteAsJsonFormat = false;

    public void Normalize(string logKind)
    {
        this.MachineName = this.MachineName._NonNullTrim()._NoSpace();
        this.AppName = this.AppName._NonNullTrim()._NoSpace();
        this.Kind = this.Kind._NonNullTrim()._FilledOrDefault(logKind)._NoSpace();
    }
}

[Flags]
public enum LogFlags
{
    None = 0,
    NoOutputToConsole = 1,
    Heading = 2,
}

public interface ILogRecordTimeStamp
{
    DateTimeOffset TimeStamp { get; }
}

public class LogJsonData
{
    public DateTimeOffset? TimeStamp;
    public object? Data;
    public string? TypeName;
    public string? Kind;
    public string? Priority;
    public string? Tag;
    public string? AppName;
    public string? MachineName;
    public string? Guid;

    static readonly PathParser WinParser = PathParser.GetInstance(FileSystemStyle.Windows);

    public void NormalizeReceivedLog(string defaultSrcMachineName)
    {
        this.Guid = WinParser.MakeSafeFileName(this.Guid);
        this.MachineName = WinParser.MakeSafeFileName(this.MachineName);
        this.AppName = WinParser.MakeSafeFileName(this.AppName);
        this.Kind = WinParser.MakeSafeFileName(this.Kind);
        this.Priority = WinParser.MakeSafeFileName(this.Priority);
        this.Tag = WinParser.MakeSafeFileName(this.Tag);
        this.TypeName = WinParser.MakeSafeFileName(this.TypeName);

        if (this.TimeStamp == null) this.TimeStamp = DateTimeOffset.Now;

        if (this.Guid._IsEmpty()) this.Guid = Str.NewGuid();

        this.MachineName = this.MachineName._NonNullTrim().ToLowerInvariant();
        if (this.MachineName._IsEmpty()) this.MachineName = defaultSrcMachineName;

        if (this.AppName._IsEmpty()) this.AppName = "unknown";

        if (this.Kind._IsEmpty()) this.Kind = LogKind.Default;

        if (this.Priority._IsEmpty()) this.Priority = LogPriority.None.ToString();

        if (this.Tag._IsEmpty()) this.Tag = LogTag.None;

        if (this.TypeName._IsEmpty()) this.TypeName = "unknown";
    }
}

public class LogRecord
{
    public static readonly byte[] CrLfByte = "\r\n"._GetBytes_Ascii();

    public DateTimeOffset TimeStamp { get; }
    public object? Data { get; }
    public LogPriority Priority { get; }
    public LogFlags Flags { get; }
    public string? Tag { get; }
    public string Guid { get; }

    public LogRecord(object? data, LogPriority priority = LogPriority.Debug, LogFlags flags = LogFlags.None, string? tag = null) : this(0, data, priority, flags, tag) { }

    public LogRecord(long tick, object? data, LogPriority priority = LogPriority.Debug, LogFlags flags = LogFlags.None, string? tag = null)
        : this(tick == 0 ? ((data as ILogRecordTimeStamp)?.TimeStamp ?? DateTimeOffset.Now) : Time.Tick64ToDateTimeOffsetLocal(tick), data, priority, flags, tag) { }

    public LogRecord(DateTimeOffset dateTime, object? data, LogPriority priority = LogPriority.Debug, LogFlags flags = LogFlags.None, string? tag = null)
    {
        this.TimeStamp = dateTime;
        this.Data = data;
        this.Priority = priority;
        this.Flags = flags;
        this.Tag = tag;
        this.Guid = System.Guid.NewGuid().ToString("N");
    }

    LogInfoOptions ConsolePrintOptions = new LogInfoOptions() { };

    public string ConsolePrintableString => GetTextFromData(this.Data, ConsolePrintOptions);

    public static string GetTextFromData(object? data, LogInfoOptions opt)
    {
        if (data == null) return "null";
        if (data is string str) return str;

        return data._GetObjectDump("", opt.ObjectPrintSeparator, true);
    }

    public static string GetMultilineText(string src, LogInfoOptions opt, int paddingLenForNextLines = 1)
    {
        if ((opt.NewLineForString?.IndexOf("\n") ?? -1) == -1)
            paddingLenForNextLines = 0;

        string pad = Str.MakeCharArray(' ', paddingLenForNextLines);
        string[] lines = src._GetLines();
        StringBuilder sb = new StringBuilder();

        int num = 0;
        foreach (string line in lines)
        {
            if (line._IsFilled())
            {
                string line2 = line.TrimEnd();
                if (num >= 1)
                {
                    sb.Append(opt.NewLineForString);
                    sb.Append(pad);
                }
                sb.Append(line2);
                num++;
            }
        }

        return sb.ToString();
    }

    public void WriteRecordToBuffer(LogInfoOptions opt, MemoryBuffer<byte> b)
    {
        if (opt.WriteAsJsonFormat && Dbg.IsJsonSupported)
        {
            // JSON text
            LogJsonData jc = new LogJsonData();

            if (opt.WithTimeStamp)
                jc.TimeStamp = this.TimeStamp;

            if (opt.WithGuid)
                jc.Guid = this.Guid;

            if (opt.WithMachineName)
                jc.MachineName = opt.MachineName;

            if (opt.WithAppName)
                jc.AppName = opt.AppName;

            if (opt.WithKind)
                jc.Kind = opt.Kind;

            if (opt.WithPriority)
                jc.Priority = this.Priority.ToString();

            if (opt.WithTag)
                jc.Tag = this.Tag._FilledOrDefault(LogTag.None);

            if (opt.WithTypeName)
                jc.TypeName = this.Data?.GetType().Name ?? "null";

            jc.Data = this.Data;

            string jsonText = jc._GetObjectDump(jsonIfPossible: true);

            b.Write(jsonText._GetBytes_UTF8());
            b.Write(CrLfByte);
        }
        else
        {
            // Normal text
            StringBuilder sb = new StringBuilder();

            // Timestamp
            if (opt.WithTimeStamp)
            {
                sb.Append(this.TimeStamp._ToDtStr(true, DtStrOption.All, false));
                sb.Append(" ");
            }

            // Additional strings
            List<string?> additionalList = new List<string?>();

            if (opt.WithGuid)
                additionalList.Add(this.Guid);

            if (opt.WithMachineName)
                additionalList.Add(opt.MachineName);

            if (opt.WithAppName)
                additionalList.Add(opt.AppName);

            if (opt.WithKind)
                additionalList.Add(opt.Kind);

            if (opt.WithPriority)
                additionalList.Add(this.Priority.ToString());

            if (opt.WithTag)
                additionalList.Add(this.Tag._FilledOrDefault(LogTag.None));

            if (opt.WithTypeName)
                additionalList.Add(this.Data?.GetType().Name ?? "null");

            string additionalStr = Str.CombineStringArray(" ", additionalList.ToArray());
            if (additionalStr._IsFilled())
            {
                sb.Append("[");
                sb.Append(additionalStr);
                sb.Append("] ");
            }

            // Log text
            string logText = GetMultilineText(GetTextFromData(this.Data, opt), opt);
            sb.Append(logText);
            sb.Append("\r\n");

            b.Write(sb.ToString()._GetBytes_UTF8());
        }
    }
}

public class Logger : AsyncService
{
    public const string DefaultExtension = ".log";
    public const long BufferCacheMaxSize = 5 * 1024 * 1024;

    public readonly byte[] NewFilePreamble = Str.BomUtf8;

    readonly CriticalSection Lock = new CriticalSection<Logger>();
    public string DirName { get; }
    public string Kind { get; }
    public string Prefix { get; }
    public LogSwitchType SwitchType { get; set; }
    public long MaxLogSize { get; }
    public int UniqueProcessId { get; }
    readonly Queue<LogRecord> RecordQueue = new Queue<LogRecord>();
    readonly AsyncAutoResetEvent Event = new AsyncAutoResetEvent();
    readonly AsyncAutoResetEvent WaitPendingEvent = new AsyncAutoResetEvent();
    public string Extension { get; }
    public bool DiscardPendingDataOnDispose { get; set; } = false;

    readonly static ConcurrentHashSet<string> DebugFileErrorPrintHashSet = new ConcurrentHashSet<string>(StrComparer.IgnoreCaseComparer);

    public int MaxPendingRecords { get; set; } = CoresConfig.Logger.DefaultMaxPendingRecords;

    LogInfoOptions InfoOptions { get; }

    long LastTick = 0;
    LogSwitchType LastSwitchType = LogSwitchType.None;
    long CurrentFilePointer = 0;
    int CurrentLogNumber = 0;
    bool LogNumberIncremented = false;
    string? LastCachedStr = null;

    public bool KeepFileHandleWhenIdle { get; set; }

    public bool NoFlush { get; set; } = false;

    Task? LogTask = null;
    OldFileEraser? Eraser = null;

    public Logger(string dir, string kind, string prefix, int uniqueProcessId, LogSwitchType switchType, LogInfoOptions infoOptions,
        long maxLogSize = 0, string extension = DefaultExtension,
        long? autoDeleteTotalMinSize = null,
        bool keepFileHandleWhenIdle = true)
        : base()
    {
        this.UniqueProcessId = uniqueProcessId;
        this.DirName = dir._NonNullTrim();
        this.Kind = kind._NonNullTrim()._FilledOrDefault(LogKind.Default);
        this.Prefix = prefix._NonNullTrim()._FilledOrDefault("log")._ReplaceStr("\\", "_").Replace("/", "_");
        this.SwitchType = switchType;
        this.Extension = extension._FilledOrDefault(DefaultExtension);
        this.KeepFileHandleWhenIdle = keepFileHandleWhenIdle;
        if (this.MaxLogSize <= 0)
            this.MaxLogSize = CoresConfig.Logger.DefaultMaxLogSize;
        this.MaxLogSize = Math.Max(maxLogSize, BufferCacheMaxSize * 10L);
        if (this.Extension.StartsWith(".") == false)
            this.Extension = "." + this.Extension;

        this.InfoOptions = infoOptions._CloneDeep();
        this.InfoOptions.Normalize(this.Kind);


        if (autoDeleteTotalMinSize != null && autoDeleteTotalMinSize.Value != long.MaxValue)
        {
            autoDeleteTotalMinSize = autoDeleteTotalMinSize._FilledOrDefault(CoresConfig.Logger.DefaultAutoDeleteTotalMinSize.Value);
            this.Eraser = new OldFileEraser(autoDeleteTotalMinSize ?? 0, dir._SingleArray(), extension, CoresConfig.Logger.EraserIntervalMsecs);
        }

        LogTask = LogThreadAsync()._LeakCheck();
    }

    protected override async Task CancelImplAsync(Exception? ex)
    {
        try
        {
            await this.Eraser._CancelSafeAsync(ex);
        }
        finally
        {
            await base.CancelImplAsync(ex);
        }
    }

    protected override async Task CleanupImplAsync(Exception? ex)
    {
        try
        {
            await this.Eraser._CleanupSafeAsync(ex);

            await LogTask._TryWaitAsync();
        }
        finally
        {
            await base.CleanupImplAsync(ex);
        }
    }

    protected override void DisposeImpl(Exception? ex)
    {
        this.Eraser._DisposeSafe(ex);
    }

    public async Task Stop(bool abandonUnwritenData)
    {
        this.DiscardPendingDataOnDispose = abandonUnwritenData;

        await this.CancelAsync();
    }

    async Task LogThreadAsync()
    {
        IO? io = null;
        MemoryBuffer<byte> b = new MemoryBuffer<byte>();
        string currentFileName = "";
        string currentLogFileDateName = "";
        bool logDateChanged = false;
        long lastIdleFlushTick = 0;

        while (true)
        {
            await Task.Yield();

            LogRecord? rec = null;
            long s = FastTick64.Now;

            while (true)
            {
                string fileName;
                int num;

                lock (this.RecordQueue)
                {
                    rec = RecordQueue._DequeueOrNull();
                    num = RecordQueue.Count;
                }

                if (MaxPendingRecords >= 1)
                {
                    if (num == (this.MaxPendingRecords - 1))
                    {
                        this.WaitPendingEvent.Set();
                    }
                }

                if (b.Length > Math.Max(this.MaxLogSize, BufferCacheMaxSize))
                {
                    // Erase if the size of the buffer is larger than the maximum log file size
                    b.Clear();
                }

                await Task.Yield();

                if (b.Length >= BufferCacheMaxSize)
                {
                    // Write the contents of the buffer to the file
                    if (io != null)
                    {
                        if ((CurrentFilePointer + b.Length) > MaxLogSize)
                        {
                            if (LogNumberIncremented == false)
                            {
                                CurrentLogNumber++;
                                LogNumberIncremented = true;
                            }
                        }
                        else
                        {
                            if (io.Write(b.Memory) == false)
                            {
                                io.Close(this.NoFlush);
                                io = null;
                                b.Clear();
                            }
                            else
                            {
                                CurrentFilePointer += b.Length;
                                b.Clear();
                            }
                        }
                    }
                }

                if (rec == null)
                {
                    if (b.Length != 0)
                    {
                        // Write the contents of the buffer to the file
                        if (io != null)
                        {
                            if ((CurrentFilePointer + b.Length) > MaxLogSize)
                            {
                                if (LogNumberIncremented == false)
                                {
                                    CurrentLogNumber++;
                                    LogNumberIncremented = true;
                                }
                            }
                            else
                            {
                                if (io.Write(b.Memory) == false)
                                {
                                    io.Close(this.NoFlush);
                                    io = null;
                                    b.Clear();
                                }
                                else
                                {
                                    CurrentFilePointer += b.Length;
                                    b.Clear();
                                }
                            }
                        }
                    }

                    break;
                }

                // Generate a log file name
                lock (this.Lock)
                {
                    logDateChanged = MakeLogFileName(out fileName, this.DirName, this.Prefix, this.UniqueProcessId,
                        rec.TimeStamp, this.SwitchType, this.CurrentLogNumber, ref currentLogFileDateName);

                    if (logDateChanged)
                    {
                        int existingMaxLogNumber = 0;

                        try
                        {
                            string[] existingFiles = Directory.GetFiles(IO.InnerFilePath(this.DirName), "*" + this.Extension, SearchOption.TopDirectoryOnly);

                            string? candidateFileNameStartStr = Path.GetFileNameWithoutExtension(fileName).Split("~").FirstOrDefault();

                            if (candidateFileNameStartStr._IsFilled())
                            {
                                string? maxFileName = existingFiles.Select(x => x._GetFileName()!).OrderByDescending(x => x).Where(x => x._InStr("~") && x.StartsWith(candidateFileNameStartStr, StringComparison.OrdinalIgnoreCase)).Select(x => Path.GetFileNameWithoutExtension(x)).FirstOrDefault();

                                if (maxFileName._IsFilled())
                                {
                                    string? existingFileNumberStr = maxFileName.Split("~").LastOrDefault();
                                    if (existingFileNumberStr._IsFilled())
                                    {
                                        existingMaxLogNumber = existingFileNumberStr._ToInt();
                                    }
                                }
                            }
                        }
                        catch { }

                        if (existingMaxLogNumber != 0)
                        {
                            this.CurrentLogNumber = existingMaxLogNumber;
                            MakeLogFileName(out fileName, this.DirName, this.Prefix, this.UniqueProcessId,
                                rec.TimeStamp, this.SwitchType, this.CurrentLogNumber, ref currentLogFileDateName);
                        }
                        else
                        {
                            this.CurrentLogNumber = 0;
                            MakeLogFileName(out fileName, this.DirName, this.Prefix, this.UniqueProcessId,
                                rec.TimeStamp, this.SwitchType, 0, ref currentLogFileDateName);
                            for (int i = 0; ; i++)
                            {
                                MakeLogFileName(out string tmp, this.DirName, this.Prefix, this.UniqueProcessId,
                                    rec.TimeStamp, this.SwitchType, i, ref currentLogFileDateName);

                                if (IO.IsFileExists(tmp) == false)
                                    break;

                                this.CurrentLogNumber = i;
                            }
                        }
                    }
                }

                if (io != null)
                {
                    if (currentFileName != fileName)
                    {
                        // If a log file is currently opened and writing to another log
                        // file is needed for this time, write the contents of the 
                        // buffer and close the log file. Write the contents of the buffer
                        if (io != null)
                        {
                            if (logDateChanged)
                            {
                                if ((this.CurrentFilePointer + b.Length) <= this.MaxLogSize)
                                {
                                    if (io.Write(b.Memory) == false)
                                    {
                                        io.Close(this.NoFlush);
                                        b.Clear();
                                        io = null;
                                    }
                                    else
                                    {
                                        this.CurrentFilePointer += b.Length;
                                        b.Clear();
                                    }
                                }
                            }
                            // Close the file
                            if (io != null)
                            {
                                io.Close(this.NoFlush);
                                io = null;
                            }
                        }

                        this.LogNumberIncremented = false;

                        // Open or create a new log file
                        currentFileName = fileName;
                        try
                        {
                            io = IO.FileOpen(fileName, writeMode: true, useAsync: false);
                            this.CurrentFilePointer = io.FileSize64;
                            io.Seek(SeekOrigin.End, 0);
                        }
                        catch
                        {
                            // Create a log file
                            lock (this.Lock)
                            {
                                try
                                {
                                    IO.MakeDirIfNotExists(this.DirName);
                                    Win32FolderCompression.SetFolderCompression(this.DirName, true);
                                }
                                catch { }
                            }
                            try
                            {
                                io = IO.FileCreate(fileName, useAsync: false);
                                await io.WriteAsync(NewFilePreamble);
                            }
                            catch (Exception ex)
                            {
                                if (Dbg.IsConsoleDebugMode)
                                {
                                    if (DebugFileErrorPrintHashSet.Add(fileName))
                                    {
                                        string str = $"IO.FileCreate('{fileName}') failed. {ex.Message}";
                                        lock (Con.ConsoleWriteLock)
                                        {
                                            Console.WriteLine(str);
                                        }
                                    }
                                }
                            }
                            this.CurrentFilePointer = 0;
                        }
                    }
                }
                else
                {
                    // Open or create a new log file
                    currentFileName = fileName;
                    try
                    {
                        io = IO.FileOpen(fileName, writeMode: true, useAsync: false);
                        this.CurrentFilePointer = io.FileSize64;
                        io.Seek(SeekOrigin.End, 0);
                    }
                    catch
                    {
                        // Create a log file
                        lock (this.Lock)
                        {
                            try
                            {
                                IO.MakeDirIfNotExists(this.DirName);
                                Win32FolderCompression.SetFolderCompression(this.DirName, true);
                            }
                            catch { }
                        }
                        this.CurrentFilePointer = 0;
                        try
                        {
                            io = IO.FileCreate(fileName, useAsync: false);
                            await io.WriteAsync(NewFilePreamble);
                        }
                        catch (Exception ex)
                        {
                            if (Dbg.IsConsoleDebugMode)
                            {
                                if (DebugFileErrorPrintHashSet.Add(fileName))
                                {
                                    string str = $"IO.FileCreate('{fileName}') failed. {ex.Message}";
                                    lock (Con.ConsoleWriteLock)
                                    {
                                        Console.WriteLine(str);
                                    }
                                }
                            }

                            await Task.Delay(30);
                        }
                    }

                    this.LogNumberIncremented = false;
                }

                // Write the contents of the log to the buffer
                rec.WriteRecordToBuffer(this.InfoOptions, b);

                if (io == null)
                    break;

                if (this.GrandCancel.IsCancellationRequested)
                    break;
            }

            // Break after finishing to save all records
            // when the stop flag stood
            int num2;
            lock (this.RecordQueue)
            {
                num2 = this.RecordQueue.Count;
            }

            if (this.GrandCancel.IsCancellationRequested)
            {
                if ((num2 == 0 && b.Length == 0) || io == null || DiscardPendingDataOnDispose)
                {
                    break;
                }
            }
            else
            {
                if (num2 == 0)
                {
                    int nextWaitInterval = Util.RandSInt31() % 10000;

                    if (this.KeepFileHandleWhenIdle == false)
                    {
                        if (io != null)
                        {
                            io.Close(this.NoFlush);
                            io = null;
                        }
                    }
                    else
                    {
                        long now = Tick64.Now;

                        if (lastIdleFlushTick == 0 || (lastIdleFlushTick + 1000) <= now)
                        {
                            lastIdleFlushTick = now;

                            if (io != null)
                            {
                                io.Flush();
                            }
                        }
                    }

                    await this.Event.WaitOneAsync(nextWaitInterval, this.GrandCancel);
                }
            }
        }

        if (io != null)
        {
            io.Close(true);
        }

        b.Clear();
    }

    public async Task<bool> AddAsync(LogRecord r, LogPendingTreatment pendingTreatment = LogPendingTreatment.Discard, CancellationToken pendingWaitCancel = default)
    {
        if (r.Priority == LogPriority.None)
        {
            return true;
        }

        if (this.GrandCancel.IsCancellationRequested)
        {
            return false;
        }

        while (MaxPendingRecords >= 1 && this.RecordQueue.Count >= this.MaxPendingRecords)
        {
            if (pendingTreatment == LogPendingTreatment.WriteForcefully)
            {
                break;
            }
            else if (pendingTreatment == LogPendingTreatment.Wait)
            {
                await TaskUtil.WaitObjectsAsync(cancels: new CancellationToken[] { pendingWaitCancel, this.GrandCancel }, events: this.WaitPendingEvent._SingleArray());
            }
            else
            {
                return false;
            }
        }

        lock (this.RecordQueue)
        {
            this.RecordQueue.Enqueue(r);
        }

        this.Event.Set();

        return true;
    }

    public bool Add(LogRecord r)
    {
        if (r.Priority == LogPriority.None)
        {
            return true;
        }

        if (this.GrandCancel.IsCancellationRequested)
        {
            return false;
        }

        if (MaxPendingRecords >= 1 && this.RecordQueue.Count >= this.MaxPendingRecords)
        {
            return false;
        }

        lock (this.RecordQueue)
        {
            this.RecordQueue.Enqueue(r);
        }

        this.Event.Set();

        return true;
    }

    public async Task<bool> FlushAsync(bool halfFlush = false, CancellationToken cancel = default)
    {
        this.Event.Set();

        while (this.GrandCancel.IsCancellationRequested == false && cancel.IsCancellationRequested == false)
        {
            int num;
            lock (this.RecordQueue)
                num = this.RecordQueue.Count;

            if (halfFlush == false)
            {
                if (num == 0) return true;
            }
            else
            {
                if (num <= (this.MaxPendingRecords / 2)) return true;
            }

            if (this.GrandCancel.IsCancellationRequested) return false;
            if (cancel.IsCancellationRequested) return false;

            var ret = await TaskUtil.WaitObjectsAsync(timeout: 100, cancels: new CancellationToken[] { cancel, this.GrandCancel });
            if (ret == ExceptionWhen.CancelException)
                return false;
        }
        return false;
    }

    bool MakeLogFileName(out string name, string dir, string prefix, int uniqueProcessId, DateTimeOffset dateTime, LogSwitchType switchType, int num, ref string oldDateStr)
    {
        prefix = prefix._TrimNonNull();
        string dateTimePart = MakeLogFileNameStringFromTick(dateTime, switchType);
        string numberStr = "";
        string logSuffixStr = "";
        string uniqueProcessIdStr = ("@" + uniqueProcessId.ToString("D3"));
        bool ret = false;

        if (CoresLib.LogFileSuffix._IsFilled())
        {
            logSuffixStr = "@" + CoresLib.LogFileSuffix;
        }

        if (num != 0)
        {
            long maxLogSize = this.MaxLogSize;
            int digits = 9;
            if (maxLogSize >= 1000000000L)
                digits = 3;
            else if (maxLogSize >= 100000000L)
                digits = 4;
            else if (maxLogSize >= 10000000L)
                digits = 5;
            else if (maxLogSize >= 1000000L)
                digits = 6;
            else if (maxLogSize >= 100000L)
                digits = 7;
            else if (maxLogSize >= 10000L)
                digits = 8;
            numberStr = "~" + num.ToString($"D" + digits);
        }

        if (oldDateStr != dateTimePart)
        {
            ret = true;
            oldDateStr = dateTimePart;
        }

        name = Path.Combine(dir, prefix + dateTimePart + logSuffixStr + uniqueProcessIdStr + numberStr + this.Extension);

        return ret;
    }

    string MakeLogFileNameStringFromTick(DateTimeOffset dateTime, LogSwitchType switchType)
    {
        if (this.LastCachedStr != null)
            if (this.LastTick == dateTime.Ticks && this.LastSwitchType == switchType)
                return this.LastCachedStr;

        string str = "";

        switch (switchType)
        {
            case LogSwitchType.Second:
                str = dateTime.ToString("_yyyyMMdd_HHmmss");
                break;

            case LogSwitchType.Minute:
                str = dateTime.ToString("_yyyyMMdd_HHmm");
                break;

            case LogSwitchType.Hour:
                str = dateTime.ToString("_yyyyMMdd_HH");
                break;

            case LogSwitchType.Day:
                str = dateTime.ToString("_yyyyMMdd");
                break;

            case LogSwitchType.Month:
                str = dateTime.ToString("_yyyyMM");
                break;
        }

        this.LastCachedStr = str;
        this.LastTick = dateTime.Ticks;
        this.LastSwitchType = SwitchType;

        return str;
    }
}
