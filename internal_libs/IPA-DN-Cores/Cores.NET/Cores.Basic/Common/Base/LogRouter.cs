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
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.IO;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using System.Runtime.InteropServices;

namespace IPA.Cores.Basic;

public static partial class CoresConfig
{
    public static partial class LocalBufferedLogRouteSettings
    {
        public static readonly Copenhagen<int> BufferSize = 1 * 128 * 1024;
    }
}

public class BufferedLogRoute : LogRouteBase
{
    public static readonly string DefaultFilter = Str.CombineStringArray(",", LogKind.Default, LogKind.Access, LogKind.Data, LogKind.Socket, LogKind.Stat);
    public static readonly string DefaultFilterForSyslog = Str.CombineStringArray(",", LogKind.Default);

    readonly LogInfoOptions LogInfoOptions;
    readonly int BufferSize;

    readonly FastStreamBuffer MasterBuffer;

    readonly HashSet<PipePoint> SubscribersList = new HashSet<PipePoint>();

    readonly CriticalSection LockObj = new CriticalSection<BufferedLogRoute>();

    public BufferedLogRoute(string kind, LogPriority minimalPriority, LogInfoOptions infoOptions, int bufferSize) : base(kind, minimalPriority)
    {
        this.LogInfoOptions = infoOptions;
        this.BufferSize = Math.Max(bufferSize, 1);

        MasterBuffer = new FastStreamBuffer(false, this.BufferSize);
    }

    public override Task FlushAsync(bool halfFlush = false, CancellationToken cancel = default) => Task.CompletedTask;

    public override void ReceiveLog(LogRecord record, string kind)
    {
        MemoryBuffer<byte> buf = new MemoryBuffer<byte>();
        record.WriteRecordToBuffer(this.LogInfoOptions, buf);

        MasterBuffer.NonStopWriteWithLock(buf.Memory, true, FastStreamNonStopWriteMode.DiscardExistingData);

        lock (LockObj)
        {
            foreach (var pipe in this.SubscribersList)
            {
                pipe.CounterPart._NullCheck();

                lock (pipe.CounterPart.StreamWriter.LockObj)
                {
                    if (pipe.CounterPart.StreamWriter.NonStopWriteWithLock(buf.Memory, false, FastStreamNonStopWriteMode.DiscardExistingData) != 0)
                    {
                        // To avoid deadlock, CompleteWrite() must be called from other thread.
                        // (CompleteWrite() ==> Disconnect ==> Socket Log will recorded ==> ReceiveLog() ==> this function will be called!)
                        TaskUtil.StartSyncTaskAsync(() => pipe.CounterPart.StreamWriter.CompleteWrite(false), false, false)._LaissezFaire(true);
                    }
                }
            }
        }
    }

    public PipePoint Subscribe(int? bufferSize = null, CancellationToken cancelForNewPipe = default)
    {
        bufferSize = Math.Max(bufferSize ?? this.BufferSize, 1);

        PipePoint mySide = PipePoint.NewDuplexPipeAndGetOneSide(PipePointSide.A_LowerSide, cancelForNewPipe, bufferSize.Value);

        mySide.CounterPart._MarkNotNull();

        mySide.AddOnDisconnected(() => UnsubscribeAsync(mySide.CounterPart));

        lock (this.MasterBuffer.LockObj)
        {
            ReadOnlySpan<ReadOnlyMemory<byte>> currentAllData = this.MasterBuffer.GetAllFast();

            lock (mySide.StreamWriter.LockObj)
            {
                mySide.StreamWriter.NonStopWriteWithLock(currentAllData, true, FastStreamNonStopWriteMode.DiscardExistingData);
            }
        }

        lock (LockObj)
        {
            this.SubscribersList.Add(mySide.CounterPart);
        }

        return mySide.CounterPart;
    }

    public async Task UnsubscribeAsync(PipePoint pipePoint)
    {
        lock (LockObj)
        {
            this.SubscribersList.Remove(pipePoint);
        }

        await Task.CompletedTask;
    }
}

public class ConsoleLogRoute : LogRouteBase
{
    public ConsoleLogRoute(string kind, LogPriority minimalPriority) : base(kind, minimalPriority) { }

    public override Task FlushAsync(bool halfFlush = false, CancellationToken cancel = default)
    {
        Console.Out.Flush();
        return Task.CompletedTask;
    }

    public override void ReceiveLog(LogRecord record, string kind)
    {
        if (record.Flags.Bit(LogFlags.NoOutputToConsole) == false)
        {
            lock (Con.ConsoleWriteLock)
            {
                Console.WriteLine(record.ConsolePrintableString);
            }
        }
    }
}

public class LoggerLogRoute : LogRouteBase
{
    Logger? Log = null;

    public LoggerLogRoute(string kind, LogPriority minimalPriority, string prefix, string dir, LogSwitchType switchType, LogInfoOptions infoOptions,
        long? autoDeleteTotalMaxSize = null) : base(kind, minimalPriority)
    {
        if (minimalPriority == LogPriority.None)
            return;

        Log = new Logger(dir, kind, prefix, LocalLogRouter.UniqueLogProcessId,
            switchType: switchType,
            infoOptions: infoOptions,
            maxLogSize: CoresConfig.Logger.DefaultMaxLogSize,
            autoDeleteTotalMinSize: autoDeleteTotalMaxSize ?? CoresConfig.Logger.DefaultAutoDeleteTotalMinSize);

        AddDirectDisposeLink(Log);
    }

    public override Task FlushAsync(bool halfFlush = false, CancellationToken cancel = default)
    {
        return Log?.FlushAsync(halfFlush, cancel) ?? Task.CompletedTask;
    }

    public override void ReceiveLog(LogRecord record, string kind)
    {
        Log?.Add(record);
    }
}

public abstract class LogRouteBase : AsyncService
{
    public ImmutableHashSet<string> KindHash { get; set; } = ImmutableHashSet<string>.Empty;
    public LogPriority MinimalPriority { get; }

    public LogRouteBase(string kind, LogPriority minimalPriority)
    {
        this.MinimalPriority = minimalPriority;

        SetKind(kind);
    }

    public void SetKind(string? kind)
    {
        kind = kind._NonNullTrim();

        ImmutableHashSet<string> hash = ImmutableHashSet<string>.Empty;

        foreach (string token in kind.Split(new char[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries))
        {
            hash = hash.Add(token);
        }

        this.KindHash = hash;
    }

    public abstract void ReceiveLog(LogRecord record, string kind);

    public abstract Task FlushAsync(bool halfFlush = false, CancellationToken cancel = default);
}

public class LogRouter : AsyncService
{
    readonly CriticalSection LockObj = new CriticalSection<LogRouter>();

    ImmutableList<LogRouteBase> RouteList = ImmutableList<LogRouteBase>.Empty;

    protected override async Task CancelImplAsync(Exception? ex)
    {
        try
        {
            var routeList = this.RouteList;
            foreach (LogRouteBase route in routeList)
            {
                await route._CancelSafeAsync();
            }
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
            var routeList = this.RouteList;
            foreach (LogRouteBase route in routeList)
            {
                await UninstallLogRouteAsync(route);
            }
        }
        finally
        {
            await base.CleanupImplAsync(ex);
        }
    }

    protected override void DisposeImpl(Exception? ex) { }

    public T InstallLogRoute<T>(T route) where T : LogRouteBase
    {
        lock (LockObj)
        {
            CheckNotCanceled();

            this.RouteList = this.RouteList.Add(route);
        }

        return route;
    }

    public async Task UninstallLogRouteAsync(LogRouteBase route)
    {
        lock (LockObj)
        {
            this.RouteList = this.RouteList.Remove(route);
        }

        await route._CancelSafeAsync();
        await route._CleanupSafeAsync();
        await route._DisposeSafeAsync();
    }
    public void UninstallLogRoute(LogRouteBase route)
        => UninstallLogRouteAsync(route)._GetResult();

    public async Task FlushAsync(bool halfFlush = false, CancellationToken cancel = default)
    {
        try
        {
            List<Task> flushTaskList = new List<Task>();
            var routeList = this.RouteList;
            foreach (LogRouteBase route in routeList)
            {
                try
                {
                    flushTaskList.Add(route.FlushAsync(halfFlush, cancel));
                }
                catch { }
            }
            foreach (Task t in flushTaskList)
            {
                await t._TryWaitAsync();
            }
        }
        catch { }
    }

    public void PostLog(LogRecord record, string kind)
    {
        if (record.Priority == LogPriority.None)
            return;

        var routeList = this.RouteList;
        foreach (LogRouteBase route in routeList)
        {
            if (route.MinimalPriority != LogPriority.None)
            {
                if (route.KindHash.Count == 0 || route.KindHash.Contains(kind))
                {
                    if (route.MinimalPriority <= record.Priority)
                    {
                        try
                        {
                            route.ReceiveLog(record, kind);
                        }
                        catch { }
                    }
                }
            }
        }
    }
}

public static class LocalLogRouter
{
    public static int UniqueLogProcessId { get; private set; } = -1;

    public static LogRouter Router { get; private set; } = null!;

    public static BufferedLogRoute BufferedLogRoute { get; private set; } = null!;

    public static readonly Copenhagen<CoresLibOptions?> OptionsForSmtpLogRouterInit = new Copenhagen<CoresLibOptions?>(null);

    public static StaticModule Module { get; } = new StaticModule(ModuleInit, ModuleFree);

    static int PostDataCounter = 0;

    static int PostDataFlushCountPer = CoresConfig.LocalLogRouterSettings.PostDataFlushCountPer;

    static int DetermineUniqueLogProcessId()
    {
        int ret = 0;

        bool ok = false;

        for (int uid = 0; uid < CoresConfig.BasicConfig.MaxPossibleConcurrentProcessCounts; uid++)
        {
            string libName = CoresLib.Mode == CoresMode.Library ? CoresLib.AppName : "";
            string uniqueName = $"UlogName_{Env.AppRootDir}_{CoresLib.Mode}_{libName}_{uid}";

            SingleInstance? instance = SingleInstance.TryGet(uniqueName, true);
            if (instance != null)
            {
                // Suppress GC
                Util.AddToBlackhole(instance);

                ret = uid;

                ok = true;
                break;
            }
        }

        if (ok == false)
        {
            throw new ApplicationException("Failed to initialize the Unique log process id.");
        }

        return ret;
    }

    static void ModuleInit()
    {
        PostDataCounter = 0;

        // Determine the Unique Log Process Id
        if (UniqueLogProcessId == -1)
        {
            if (CoresLib.Caps.Bit(CoresCaps.BlazorApp) == false)
            {
                UniqueLogProcessId = DetermineUniqueLogProcessId();
            }
            else
            {
                UniqueLogProcessId = Consts.BlazorApp.DummyProcessId;
            }
        }

        // Determine the destination directory
        if (CoresConfig.LocalLogRouterSettings.LogRootDir.IsDetermined == false)
        {
            string logDestDir = Path.Combine(Env.AppRootDir, "Log");

            if (CoresLib.Mode == CoresMode.Library)
            {
                logDestDir = Path.Combine(Env.AppRootDir, "Log", "_Lib", CoresLib.AppNameFnSafe);
            }

            CoresConfig.LocalLogRouterSettings.LogRootDir.Set(logDestDir);
        }

        Router = new LogRouter();

        // Console log
        Router.InstallLogRoute(new ConsoleLogRoute(LogKind.Default,
            CoresConfig.DebugSettings.ConsoleMinimalLevel));

        var smtpOptions = LocalLogRouter.OptionsForSmtpLogRouterInit.Value;

        if (smtpOptions != null)
        {
            if (smtpOptions.SmtpFrom._IsFilled() &&
                smtpOptions.SmtpTo._IsFilled() &&
                smtpOptions.SmtpServer._IsFilled())
            {
                // SMTP log
                Router.InstallLogRoute(new SmtpLogRoute(LogKind.Default,
                    smtpOptions.SmtpLogLevel,
                    new SmtpLogRouteSettings(
                        smtpOptions.SmtpServer,
                        smtpOptions.SmtpUseSsl,
                        smtpOptions.SmtpUsername,
                        smtpOptions.SmtpPassword,
                        smtpOptions.SmtpFrom,
                        smtpOptions.SmtpTo)
                    ));
            }
        }

        // Buffered debug log
        BufferedLogRoute = Router.InstallLogRoute(new BufferedLogRoute(BufferedLogRoute.DefaultFilter,
            CoresConfig.DebugSettings.BufferedLogMinimalLevel,
            new LogInfoOptions() { WithTimeStamp = true },
            CoresConfig.LocalBufferedLogRouteSettings.BufferSize
            ));

        // Debug log (file)
        Router.InstallLogRoute(new LoggerLogRoute(LogKind.Default,
            CoresConfig.DebugSettings.LogMinimalDebugLevel,
            "debug",
            CoresConfig.LocalLogRouterSettings.LogDebugDir.Value(),
            CoresConfig.LocalLogRouterSettings.SwitchTypeForDebug,
            CoresConfig.LocalLogRouterSettings.InfoOptionsForDebug));

        // Info log (file)
        Router.InstallLogRoute(new LoggerLogRoute(LogKind.Default,
            CoresConfig.DebugSettings.LogMinimalInfoLevel,
            "info",
            CoresConfig.LocalLogRouterSettings.LogInfoDir.Value(),
            CoresConfig.LocalLogRouterSettings.SwitchTypeForInfo,
            CoresConfig.LocalLogRouterSettings.InfoOptionsForInfo));

        // Error log (file)
        Router.InstallLogRoute(new LoggerLogRoute(LogKind.Default,
            CoresConfig.DebugSettings.LogMinimalErrorLevel,
            "error",
            CoresConfig.LocalLogRouterSettings.LogErrorDir.Value(),
            CoresConfig.LocalLogRouterSettings.SwitchTypeForError,
            CoresConfig.LocalLogRouterSettings.InfoOptionsForError));

        // Data log (file)
        Router.InstallLogRoute(new LoggerLogRoute(LogKind.Data,
            CoresConfig.DebugSettings.LogMinimalDataLevel,
            "data",
            CoresConfig.LocalLogRouterSettings.LogDataDir.Value(),
            CoresConfig.LocalLogRouterSettings.SwitchTypeForData,
            CoresConfig.LocalLogRouterSettings.InfoOptionsForData));

        // Statistics log (file)
        Router.InstallLogRoute(new LoggerLogRoute(LogKind.Stat,
            CoresConfig.DebugSettings.LogMinimalStatLevel,
            "stat",
            CoresConfig.LocalLogRouterSettings.LogStatDir.Value(),
            CoresConfig.LocalLogRouterSettings.SwitchTypeForStat,
            CoresConfig.LocalLogRouterSettings.InfoOptionsForStat,
            CoresConfig.Logger.DefaultAutoDeleteTotalMinSize_ForStat));

        // Access log (file)
        Router.InstallLogRoute(new LoggerLogRoute(LogKind.Access,
            CoresConfig.DebugSettings.LogMinimalAccessLevel,
            "access",
            CoresConfig.LocalLogRouterSettings.LogAccessDir.Value(),
            CoresConfig.LocalLogRouterSettings.SwitchTypeForAccess,
            CoresConfig.LocalLogRouterSettings.InfoOptionsForAccess));

        // Socket log (file)
        Router.InstallLogRoute(new LoggerLogRoute(LogKind.Socket,
            CoresConfig.DebugSettings.LogMinimalSocketLevel,
            "socket",
            CoresConfig.LocalLogRouterSettings.LogSocketDir.Value(),
            CoresConfig.LocalLogRouterSettings.SwitchTypeForSocket,
            CoresConfig.LocalLogRouterSettings.InfoOptionsForSocket));

        EnvInfoSnapshot snapshot = new EnvInfoSnapshot("--- Process boottime log ---");

        Router.PostLog(new LogRecord(snapshot, LogPriority.Info, LogFlags.NoOutputToConsole, "boottime"), LogKind.Default);
    }

    static void ModuleFree()
    {
        Router._DisposeSafe(new CoresLibraryShutdowningException());
        Router = null!;

        BufferedLogRoute = null!;
    }

    public static Task FlushAsync(bool halfFlush = false, CancellationToken cancel = default)
        => Router?.FlushAsync(halfFlush, cancel) ?? Task.CompletedTask;

    public static void Post(LogRecord record, string kind = LogKind.Default) => Router?.PostLog(record, kind);

    public static void Post(object? obj, LogPriority priority = LogPriority.Debug, string kind = LogKind.Default, LogFlags flags = LogFlags.None, string? tag = null)
        => Router?.PostLog(new LogRecord(obj, priority, flags, tag), kind);

    public static void PrintConsole(object? obj, bool noConsole = false, LogPriority priority = LogPriority.Info, string? tag = null, LogFlags flags = LogFlags.None)
        => Post(obj, priority, flags: flags | (noConsole ? LogFlags.NoOutputToConsole : LogFlags.None), tag: tag);

    public static Task PostDataAsync(object? obj, string? tag = null, bool copyToDebug = false, LogPriority priority = LogPriority.Info, CancellationToken cancel = default, bool noWait = false)
    {
        Post(obj, priority, kind: LogKind.Data, tag: tag);

        if (copyToDebug)
        {
            Post(new PostedData() { Data = obj, Tag = tag }, priority: LogPriority.Debug, kind: LogKind.Default, tag: tag);
        }

        // データを post したときは一定個数ごとに Half Flush し、Half Flush 完了まで待機をする (消失防止のため)
        if (noWait == false && (PostDataFlushCountPer == 0 || (Interlocked.Increment(ref PostDataCounter) % PostDataFlushCountPer) == 0))
        {
            return FlushAsync(halfFlush: true, cancel: cancel);
        }
        else
        {
            return Task.CompletedTask;
        }
    }

    public static void PostData(object? obj, string? tag = null, bool copyToDebug = false, LogPriority priority = LogPriority.Info, CancellationToken cancel = default, bool noWait = false)
        => PostDataAsync(obj, tag, copyToDebug, priority, cancel, noWait)._GetResult();

    public static void PostStat(object? obj, string? tag = null, bool copyToDebug = false, LogPriority priority = LogPriority.Info)
    {
        Post(obj, priority, kind: LogKind.Stat, tag: tag);
        if (copyToDebug)
        {
            Post(new PostedStat() { Data = obj, Tag = tag }, priority: LogPriority.Debug, kind: LogKind.Default, tag: tag);
        }
    }

    public static void PostAccessLog(object? obj, string? tag = null, bool copyToDebug = false, LogPriority priority = LogPriority.Info)
    {
        Post(obj, priority, kind: LogKind.Access, tag: tag);
        if (copyToDebug)
        {
            Post(new PostedAccessLog() { Data = obj, Tag = tag }, priority: LogPriority.Debug, kind: LogKind.Default, tag: tag);
        }
    }

    public static void PostSocketLog(object? obj, string? tag = null, bool copyToDebug = false, LogPriority priority = LogPriority.Info)
    {
        Post(obj, priority, kind: LogKind.Socket, tag: tag);
        if (copyToDebug)
        {
            Post(new PostedSocketLog() { Data = obj, Tag = tag }, priority: LogPriority.Debug, kind: LogKind.Default, tag: tag);
        }
    }

    class PostedData
    {
        public string? Tag;
        public object? Data;
    }

    class PostedAccessLog
    {
        public string? Tag;
        public object? Data;
    }

    class PostedSocketLog
    {
        public string? Tag;
        public object? Data;
    }

    class PostedStat
    {
        public string? Tag;
        public object? Data;
    }

    public static void PutGitIgnoreFileOnLogDirectory() => Util.PutGitIgnoreFileOnDirectory(CoresConfig.LocalLogRouterSettings.LogRootDir.Value);
}

public static partial class CoresConfig
{
    public static partial class LocalLogRouterSettings
    {
        public static readonly Copenhagen<int> PostDataFlushCountPer = 30; // Post データ何個ごとに Half Flush するか

        public static readonly Copenhagen<string> LogRootDir = Path.Combine(Env.AppRootDir, "Log");

        // Debug
        public static readonly Copenhagen<Func<string>> LogDebugDir = new Func<string>(() => Path.Combine(LogRootDir, "Debug"));
        public static readonly Copenhagen<LogSwitchType> SwitchTypeForDebug = LogSwitchType.Hour;
        public static readonly Copenhagen<LogInfoOptions> InfoOptionsForDebug = new LogInfoOptions() { WithPriority = true };

        // Info
        public static readonly Copenhagen<Func<string>> LogInfoDir = new Func<string>(() => Path.Combine(LogRootDir, "Info"));
        public static readonly Copenhagen<LogSwitchType> SwitchTypeForInfo = LogSwitchType.Day;
        public static readonly Copenhagen<LogInfoOptions> InfoOptionsForInfo = new LogInfoOptions() { };

        // Error
        public static readonly Copenhagen<Func<string>> LogErrorDir = new Func<string>(() => Path.Combine(LogRootDir, "Error"));
        public static readonly Copenhagen<LogSwitchType> SwitchTypeForError = LogSwitchType.Day;
        public static readonly Copenhagen<LogInfoOptions> InfoOptionsForError = new LogInfoOptions() { };

        // Data
        public static readonly Copenhagen<Func<string>> LogDataDir = new Func<string>(() => Path.Combine(LogRootDir, "Data"));
        public static readonly Copenhagen<LogSwitchType> SwitchTypeForData = LogSwitchType.Day;
        public static readonly Copenhagen<LogInfoOptions> InfoOptionsForData = new LogInfoOptions() { WithTypeName = true, WriteAsJsonFormat = true, WithTag = true, WithGuid = true };

        // Stat
        public static readonly Copenhagen<Func<string>> LogStatDir = new Func<string>(() => Path.Combine(LogRootDir, "Stat"));
        public static readonly Copenhagen<LogSwitchType> SwitchTypeForStat = LogSwitchType.Day;
        public static readonly Copenhagen<LogInfoOptions> InfoOptionsForStat = new LogInfoOptions() { WithTypeName = true, WriteAsJsonFormat = true, WithTag = true, WithGuid = true };

        // Access log
        public static readonly Copenhagen<Func<string>> LogAccessDir = new Func<string>(() => Path.Combine(LogRootDir, "Access"));
        public static readonly Copenhagen<LogSwitchType> SwitchTypeForAccess = LogSwitchType.Hour;
        public static readonly Copenhagen<LogInfoOptions> InfoOptionsForAccess = new LogInfoOptions() { WithTypeName = true, WriteAsJsonFormat = true, WithTag = true, WithGuid = true };

        // Socket log
        public static readonly Copenhagen<Func<string>> LogSocketDir = new Func<string>(() => Path.Combine(LogRootDir, "Socket"));
        public static readonly Copenhagen<LogSwitchType> SwitchTypeForSocket = LogSwitchType.Day;
        public static readonly Copenhagen<LogInfoOptions> InfoOptionsForSocket = new LogInfoOptions() { WithTypeName = true, WriteAsJsonFormat = true, WithTag = true, WithGuid = true };

        // DaemonUpdate log
        public static readonly Copenhagen<Func<string>> LogDaemonUpdateDir = new Func<string>(() => Path.Combine(LogRootDir, "DaemonUpdate"));
    }
}

