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
using System.Diagnostics;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics.CodeAnalysis;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using Microsoft.AspNetCore.Server.IIS.Core;
using System.Runtime.CompilerServices;

namespace IPA.Cores.Basic;

public static partial class CoresConfig
{
    public static partial class ConfigHiveOptions
    {
        public static readonly Copenhagen<int> SyncIntervalMsec = 2 * 1000;
    }

    public static partial class DefaultHiveOptions
    {
        public static readonly Copenhagen<int> SyncIntervalMsec = 2 * 1000;
    }
}

[Flags]
public enum HiveSerializerSelection
{
    DefaultRuntimeJson = 0,
    RichJson = 1,
    Custom = int.MaxValue,
}

public abstract class HiveSerializerOptions { }

public abstract class HiveSerializer
{
    public HiveSerializerOptions Options { get; }

    public HiveSerializer(HiveSerializerOptions options)
    {
        this.Options = options;
    }

    abstract protected Memory<byte> SerializeImpl<T>(T obj);

    [return: MaybeNull]
    abstract protected T DeserializeImpl<T>(ReadOnlyMemory<byte> memory);

    public Memory<byte> Serialize<T>(T obj) => SerializeImpl(obj);
    public T? Deserialize<T>(ReadOnlyMemory<byte> memory) => DeserializeImpl<T>(memory);

    [return: NotNullIfNotNull("obj")]
    public T? CloneData<T>(T obj) => Deserialize<T>(Serialize(obj));
}

public class RuntimeJsonHiveSerializerOptions : HiveSerializerOptions
{
    public DataContractJsonSerializerSettings? JsonSettings { get; }

    public RuntimeJsonHiveSerializerOptions(DataContractJsonSerializerSettings? jsonSettings = null)
    {
        this.JsonSettings = jsonSettings;
    }
}

public class RuntimeJsonHiveSerializer : HiveSerializer
{
    public new RuntimeJsonHiveSerializerOptions Options => (RuntimeJsonHiveSerializerOptions)base.Options;

    public RuntimeJsonHiveSerializer(RuntimeJsonHiveSerializerOptions? options = null) : base(options ?? new RuntimeJsonHiveSerializerOptions()) { }

    protected override Memory<byte> SerializeImpl<T>(T obj)
    {
        if (obj == null)
            throw new ArgumentNullException("obj");

        MemoryBuffer<byte> ret = new MemoryBuffer<byte>();

        ret.Write(Str.NewLine_Bytes_Local);

        obj._ObjectToRuntimeJson(ret, Options.JsonSettings);

        ret.Write(Str.NewLine_Bytes_Local);
        ret.Write(Str.NewLine_Bytes_Local);

        // Linux 上でも改行コードが \r \n になってしまう (ライブラリの仕様) ため、強制的に \n に変換する
        if (Env.IsWindows == false)
        {
            return Str.NormalizeCrlf(ret.Memory.Span, style: CrlfStyle.LocalPlatform, false);
        }
        else
        {
            // Windows ではそのまま応答する
            return ret.Memory;
        }
    }

    [return: MaybeNull]
    protected override T DeserializeImpl<T>(ReadOnlyMemory<byte> memory)
    {
        return memory.ToArray()._RuntimeJsonToObject<T>(Options.JsonSettings);
    }
}

public abstract class HiveStorageOptionsBase
{
    public int MaxDataSize { get; }

    public HiveStorageOptionsBase(int maxDataSize = int.MaxValue)
    {
        this.MaxDataSize = maxDataSize;
    }
}

public class FileHiveStorageOptions : HiveStorageOptionsBase
{
    public bool SingleInstance { get; }
    public FileSystem FileSystem { get; }
    public bool PutGitIgnore { get; }
    public bool GlobalLock { get; }

    public Copenhagen<string> RootDirectoryPath { get; }
    public Copenhagen<FileFlags> Flags { get; }
    public Copenhagen<string> FileExtension { get; } = ".json";
    public Copenhagen<string> ErrorFileExtension { get; } = ".error.log";
    public Copenhagen<string> TmpFileExtension { get; } = ".tmp";
    public Copenhagen<string> DefaultDataName { get; } = "default";

    public FileHiveStorageOptions(FileSystem fileSystem, string rootDirectoryPath, FileFlags flags = FileFlags.WriteOnlyIfChanged,
        int maxDataSize = int.MaxValue, bool singleInstance = false, bool putGitIgnore = false, bool globalLock = false)
        : base(maxDataSize)
    {
        this.FileSystem = fileSystem;
        this.RootDirectoryPath = rootDirectoryPath;
        this.Flags = flags;
        this.SingleInstance = singleInstance;
        this.PutGitIgnore = putGitIgnore;
        this.GlobalLock = globalLock;
    }
}

public abstract class HiveStorageProvider : AsyncService
{
    public HiveStorageOptionsBase Options;

    public HiveStorageProvider(HiveStorageOptionsBase options)
    {
        this.Options = options;
    }

    protected abstract Task SaveImplAsync(string dataName, ReadOnlyMemory<byte> data, bool doNotOverwrite, CancellationToken cancel = default);
    protected abstract Task ReportErrorImplAsync(string dataName, string error, CancellationToken cancel = default);
    protected abstract Task<Memory<byte>> LoadImplAsync(string dataName, CancellationToken cancel = default);

    public Task SaveAsync(string dataName, ReadOnlyMemory<byte> data, bool doNotOverwrite, CancellationToken cancel = default)
        => this.RunCriticalProcessAsync(true, cancel, c => SaveImplAsync(dataName, data, doNotOverwrite, c));
    public Task ReportErrorAsync(string dataName, string error, CancellationToken cancel = default)
        => this.RunCriticalProcessAsync(true, cancel, c => ReportErrorImplAsync(dataName, error, c));
    public Task<Memory<byte>> LoadAsync(string dataName, CancellationToken cancel = default)
        => this.RunCriticalProcessAsync(true, cancel, c => LoadImplAsync(dataName, c));
}

public class FileHiveStorageProvider : HiveStorageProvider
{
    public new FileHiveStorageOptions Options => (FileHiveStorageOptions)base.Options;

    FileSystem FileSystem => Options.FileSystem;
    PathParser PathParser => FileSystem.PathParser;
    PathParser SafePathParser = PathParser.GetInstance(FileSystemStyle.Windows);

    AsyncLock LockObj = new AsyncLock();

    SingleInstance? SingleInstance = null;

    public FileHiveStorageProvider(FileHiveStorageOptions options) : base(options)
    {
        try
        {
            if (options.SingleInstance)
            {
                this.SingleInstance = new SingleInstance(options.RootDirectoryPath);
            }
        }
        catch (Exception ex)
        {
            this._DisposeSafe(ex);
            throw;
        }
    }

    string MakeFileName(string dataName, string extension)
    {
        dataName = Hive.NormalizeDataName(dataName);

        if (dataName._IsEmpty())
            dataName = Options.DefaultDataName;

        string ret = PathParser.Combine(Options.RootDirectoryPath, SafePathParser.MakeSafePathName(dataName), true);

        ret = PathParser.RemoveDangerousDirectoryTraversal(ret);

        ret = PathParser.NormalizeDirectorySeparatorIncludeWindowsBackslash(ret);

        ret += extension;

        return ret;
    }

    protected override async Task SaveImplAsync(string dataName, ReadOnlyMemory<byte> data, bool doNotOverwrite, CancellationToken cancel = default)
    {
        string filename = MakeFileName(dataName, Options.FileExtension);
        string newFilename = filename + Options.TmpFileExtension;
        string directoryName = PathParser.GetDirectoryName(filename);

        if (dataName._InStri("timestampdocs"))
        {
            DoNothing();
        }

        Mutant? mutant = null;

        if (this.Options.GlobalLock)
        {
            mutant = new Mutant("Hive_GlobalLock_" + filename, false, false);
        }

        try
        {
            if (mutant != null)
            {
                await mutant.LockAsync();
            }

            if (data.IsEmpty == false)
            {
                if (doNotOverwrite)
                {
                    if (await FileSystem.IsFileExistsAsync(filename, cancel))
                    {
                        throw new ApplicationException($"The file \"{filename}\" exists while doNotOverwrite flag is set.");
                    }
                }

                bool sameContentsAlreadyInFile = false;

                try
                {
                    if (Options.Flags.Value.Bit(FileFlags.WriteOnlyIfChanged))
                    {
                        if (await FileSystem.IsFileExistsAsync(filename, cancel))
                        {
                            var currentData = await FileSystem.ReadDataFromFileAsync(filename, flags: Options.Flags, cancel: cancel);

                            if (currentData._MemEquals(data))
                            {
                                sameContentsAlreadyInFile = true;
                            }
                        }
                    }
                }
                catch { }

                try
                {
                    await FileSystem.CreateDirectoryAsync(directoryName, Options.Flags, cancel);

                    if (Options.PutGitIgnore)
                        Util.PutGitIgnoreFileOnDirectory(Options.RootDirectoryPath.Value);

                    if (sameContentsAlreadyInFile == false)
                    {
                        await FileSystem.WriteDataToFileAsync(newFilename, data, Options.Flags | FileFlags.ForceClearReadOnlyOrHiddenBitsOnNeed, false, cancel);

                        try
                        {
                            await FileSystem.DeleteFileAsync(filename, Options.Flags | FileFlags.ForceClearReadOnlyOrHiddenBitsOnNeed, cancel);
                        }
                        catch { }

                        await FileSystem.MoveFileAsync(newFilename, filename, cancel);
                    }
                }
                finally
                {
                    await FileSystem.DeleteFileAsync(newFilename, Options.Flags | FileFlags.ForceClearReadOnlyOrHiddenBitsOnNeed, cancel);
                }
            }
            else
            {
                if (doNotOverwrite)
                    throw new ApplicationException($"The file {filename} exists while doNotOverwrite flag is set.");

                try
                {
                    await FileSystem.DeleteFileAsync(filename, FileFlags.ForceClearReadOnlyOrHiddenBitsOnNeed, cancel);
                }
                catch { }

                try
                {
                    await FileSystem.DeleteFileAsync(newFilename, FileFlags.ForceClearReadOnlyOrHiddenBitsOnNeed, cancel);
                }
                catch { }
            }
        }
        finally
        {
            if (mutant != null)
            {
                await mutant.UnlockAsync();
                mutant._DisposeSafe();
            }
        }
    }

    protected override async Task ReportErrorImplAsync(string dataName, string error, CancellationToken cancel = default)
    {
        string errFilename = MakeFileName(dataName, Options.ErrorFileExtension);
        string realFilename = MakeFileName(dataName, Options.FileExtension);

        StringWriter w = new StringWriter();
        w.WriteLine($"--- The hive file \"{realFilename}\" load error log ---");
        w.WriteLine($"Process ID: {Env.ProcessId}");
        w.WriteLine($"Process Name: {Env.AppRealProcessExeFileName}");
        w.WriteLine($"Timestamp: {DateTimeOffset.Now._ToDtStr(true)}");
        w.WriteLine($"Path: {realFilename}");
        w.WriteLine($"DataName: {dataName}");
        w.WriteLine($"Error:");
        w.WriteLine($"{error}");
        w.WriteLine();
        w.WriteLine($"Note: This log file \"{errFilename}\" is for only your reference. You may delete this file anytime.");
        w.WriteLine();
        w.WriteLine();

        await FileSystem.AppendDataToFileAsync(errFilename, w.ToString()._GetBytes_UTF8(),
            Options.Flags | FileFlags.ForceClearReadOnlyOrHiddenBitsOnNeed | FileFlags.AutoCreateDirectory | FileFlags.OnCreateSetCompressionFlag,
            cancel);
    }

    protected override async Task<Memory<byte>> LoadImplAsync(string dataName, CancellationToken cancel = default)
    {
        string filename = MakeFileName(dataName, Options.FileExtension);
        string newFilename = filename + Options.TmpFileExtension;

        Mutant? mutant = null;

        if (this.Options.GlobalLock)
        {
            mutant = new Mutant("Hive_GlobalLock_" + filename, false, false);
        }

        try
        {
            if (mutant != null)
            {
                await mutant.LockAsync();
            }

            try
            {
                try
                {
                    if (await FileSystem.IsFileExistsAsync(newFilename, cancel))
                    {
                        await FileSystem.MoveFileAsync(newFilename, filename, cancel);
                    }
                }
                catch { }

                return await FileSystem.ReadDataFromFileAsync(filename, Options.MaxDataSize, Options.Flags, cancel);
            }
            catch
            {
                throw;
            }
        }
        finally
        {
            if (mutant != null)
            {
                await mutant.UnlockAsync();
                mutant._DisposeSafe();
            }
        }
    }

    protected override void DisposeImpl(Exception? ex)
    {
        try
        {
            if (this.SingleInstance != null)
                this.SingleInstance._DisposeSafe();
        }
        finally
        {
            base.DisposeImpl(ex);
        }
    }
}

public class HiveOptions
{
    public const int MinSyncIntervalMsec = 2 * 1000;

    public HiveStorageProvider StorageProvider { get; }
    public bool IsPollingEnabled { get; }

    public bool IsAutoArchiverEnabled { get; }
    public string? PhysicalRootPath { get; }

    public Copenhagen<int> SyncIntervalMsec { get; } = new Copenhagen<int>(CoresConfig.DefaultHiveOptions.SyncIntervalMsec);

    public HiveOptions(string rootDirectoryPath, bool enableManagedSync = false, int? syncInterval = null, bool singleInstance = false, bool putGitIgnore = false, bool globalLock = false, bool enableAutoArchiver = false)
        : this(new FileHiveStorageProvider(new FileHiveStorageOptions(LfsUtf8, rootDirectoryPath, singleInstance: singleInstance, putGitIgnore: putGitIgnore, globalLock: globalLock)), enableManagedSync, syncInterval, enableAutoArchiver) { }

    public HiveOptions(HiveStorageProvider provider, bool enablePolling = false, int? syncInterval = null, bool enableAutoArchiver = false)
    {
        if (provider == null) throw new ArgumentNullException(nameof(provider));
        this.StorageProvider = provider;

        if (this.StorageProvider is FileHiveStorageProvider fsProvider)
        {
            this.IsAutoArchiverEnabled = true;
            PhysicalRootPath = fsProvider.Options.RootDirectoryPath;
        }

        if (enablePolling == false)
        {
            if (syncInterval.HasValue)
            {
                throw new ApplicationException("enablePolling is false while syncInterval is specified.");
            }
        }

        if (syncInterval != null)
            this.SyncIntervalMsec.SetValue(syncInterval.Value);

        this.IsPollingEnabled = enablePolling;

        if (enableAutoArchiver)
        {
            if (this.PhysicalRootPath._IsEmpty())
            {
                throw new ApplicationException("enableAutoArchiver is true while PhysicalRootPath is null.");
            }
        }

        this.IsAutoArchiverEnabled = enableAutoArchiver;
    }
}

public class Hive : AsyncServiceWithMainLoop
{
    // Static states and methods
    public static readonly StaticModule Module = new StaticModule(InitModule, FreeModule);

    static readonly HashSet<Hive> RunningHivesList = new HashSet<Hive>();
    static readonly CriticalSection RunningHivesListLockObj = new CriticalSection<Hive>();

    static readonly string ConfigHiveDirName = Path.Combine(Env.AppRootDir, "Config", (CoresLib.Mode == CoresMode.Library ? "Lib_" : "App_") + CoresLib.AppNameFnSafe);
    static readonly string LocalConfigHiveDirName = Path.Combine(Env.AppLocalDir, "Config");
    static readonly string UserConfigHiveDirName = Path.Combine(Env.HomeDir, ".Cores.NET/Config");

    public static Hive SharedLocalConfigHive { get; private set; } = null!;
    public static Hive SharedConfigHive { get; private set; } = null!;
    public static Hive SharedUserConfigHive { get; private set; } = null!;


    // Normal runtime hive data
    public static readonly Singleton<string, HiveData<HiveKeyValue>> LocalAppSettings =
        new Singleton<string, HiveData<HiveKeyValue>>(appName => new HiveData<HiveKeyValue>(SharedLocalConfigHive, "AppSettings/" + appName,
            serializer: HiveSerializerSelection.DefaultRuntimeJson));

    public static readonly Singleton<string, HiveData<HiveKeyValue>> AppSettings =
        new Singleton<string, HiveData<HiveKeyValue>>(appName => new HiveData<HiveKeyValue>(SharedConfigHive, "AppSettings/" + appName,
            serializer: HiveSerializerSelection.DefaultRuntimeJson));

    public static readonly Singleton<string, HiveData<HiveKeyValue>> UserSettings =
        new Singleton<string, HiveData<HiveKeyValue>>(appName => new HiveData<HiveKeyValue>(SharedUserConfigHive, "AppUserSettings/" + appName,
            serializer: HiveSerializerSelection.DefaultRuntimeJson));


    // Rich hive data
    public static readonly Singleton<string, HiveData<HiveKeyValue>> LocalAppSettingsEx =
        new Singleton<string, HiveData<HiveKeyValue>>(appName => new HiveData<HiveKeyValue>(SharedLocalConfigHive, "AppSettings/" + appName,
            serializer: HiveSerializerSelection.RichJson));

    public static readonly Singleton<string, HiveData<HiveKeyValue>> AppSettingsEx =
        new Singleton<string, HiveData<HiveKeyValue>>(appName => new HiveData<HiveKeyValue>(SharedConfigHive, "AppSettings/" + appName,
            serializer: HiveSerializerSelection.RichJson));

    public static readonly Singleton<string, HiveData<HiveKeyValue>> UserSettingsEx =
        new Singleton<string, HiveData<HiveKeyValue>>(appName => new HiveData<HiveKeyValue>(SharedUserConfigHive, "AppUserSettings/" + appName,
            serializer: HiveSerializerSelection.RichJson));


    static void InitModule()
    {
        Module.AddAfterInitAction(() =>
        {
            // Create shared config hive
            SharedConfigHive = new Hive(new HiveOptions(ConfigHiveDirName, enableManagedSync: true, syncInterval: CoresConfig.ConfigHiveOptions.SyncIntervalMsec, globalLock: true, enableAutoArchiver: true));

            SharedLocalConfigHive = new Hive(new HiveOptions(LocalConfigHiveDirName, enableManagedSync: true, syncInterval: CoresConfig.ConfigHiveOptions.SyncIntervalMsec, putGitIgnore: true, globalLock: true, enableAutoArchiver: true));

            SharedUserConfigHive = new Hive(new HiveOptions(UserConfigHiveDirName, enableManagedSync: true, syncInterval: CoresConfig.ConfigHiveOptions.SyncIntervalMsec, globalLock: true, enableAutoArchiver: true));
        });
    }

    static void FreeModule()
    {
        LocalAppSettings.Clear();

        Hive[] runningHives;
        lock (RunningHivesListLockObj)
        {
            runningHives = RunningHivesList._ToArrayList();
        }
        foreach (Hive hive in runningHives)
        {
            hive._DisposeSafe(new CoresLibraryShutdowningException());
        }
        lock (RunningHivesListLockObj)
        {
            RunningHivesList.Clear();
        }
    }

    public static string NormalizeDataName(string name) => name._NonNullTrim();

    // Instance states and methods
    public HiveOptions Options { get; }

    public HiveStorageProvider StorageProvider => Options.StorageProvider;

    readonly Dictionary<string, IHiveData> RegisteredHiveData = new Dictionary<string, IHiveData>();
    readonly CriticalSection LockObj = new CriticalSection<Hive>();

    readonly AsyncManualResetEvent EventWhenFirstHiveDataRegistered = new AsyncManualResetEvent();

    readonly AutoArchiver? Archiver = null;

    public Hive(HiveOptions options)
    {
        try
        {
            this.Options = options;

            lock (RunningHivesListLockObj)
            {
                Module.CheckInitalized();
                RunningHivesList.Add(this);
            }

            if (this.Options.IsPollingEnabled)
            {
                this.StartMainLoop(MainLoopProcAsync);
            }

            if (this.Options.IsAutoArchiverEnabled)
            {
                this.Archiver = new AutoArchiver(new AutoArchiverOptions(options.PhysicalRootPath._NullCheck(), new FileHistoryManagerPolicy(EnsureSpecial.Yes)));
            }
        }
        catch
        {
            this._DisposeSafe();
            throw;
        }
    }

    async Task MainLoopProcAsync(CancellationToken cancel)
    {
        int syncInterval = Math.Max(HiveOptions.MinSyncIntervalMsec, Options.SyncIntervalMsec);

        AsyncLocalTimer timer = new AsyncLocalTimer();

        long nextReadTick = 0;

        await EventWhenFirstHiveDataRegistered.WaitAsync(Timeout.Infinite, cancel);

        while (cancel.IsCancellationRequested == false)
        {
            if (timer.RepeatIntervalTimer(syncInterval, ref nextReadTick))
            {
                try
                {
                    await SyncAllHiveDataAsync(cancel);
                }
                catch { }
            }

            await timer.WaitUntilNextTickAsync(cancel);
        }
    }

    async Task SyncAllHiveDataAsync(CancellationToken cancel)
    {
        IHiveData[] hiveArray;

        lock (LockObj)
        {
            hiveArray = RegisteredHiveData.Values._ToArrayList();
        }

        foreach (var hive in hiveArray)
        {
            cancel.ThrowIfCancellationRequested();

            HiveSyncFlags flags = HiveSyncFlags.None;

            if (hive.Policy.Bit(HiveSyncPolicy.AutoReadFromFile))
                flags |= HiveSyncFlags.LoadFromFile;

            if (hive.Policy.Bit(HiveSyncPolicy.AutoWriteToFile))
                flags |= HiveSyncFlags.SaveToFile;

            if (flags != HiveSyncFlags.None)
            {
                // no worry for error
                await hive.SyncWithStorageAsync(flags, true, cancel);
            }
        }
    }

    internal void CheckPreRegisterInterval(IHiveData hiveData)
    {
        if (hiveData.IsManaged == false)
            throw new ArgumentException("hiveData.IsManaged == false");

        lock (LockObj)
        {
            CheckNotCanceled();

            if (RegisteredHiveData.ContainsKey(hiveData.DataName))
            {
                throw new ApplicationException($"The hive name \"{hiveData.DataName}\" is already registered on the same hive.");
            }
        }
    }

    internal void RegisterInternal(IHiveData hiveData)
    {
        if (hiveData.IsManaged == false)
            throw new ArgumentException("hiveData.IsManaged == false");

        lock (LockObj)
        {
            CheckNotCanceled();

            if (RegisteredHiveData.TryAdd(hiveData.DataName, hiveData) == false)
            {
                throw new ApplicationException($"The hive name \"{hiveData.DataName}\" is already registered on the same hive.");
            }
        }

        this.EventWhenFirstHiveDataRegistered.Set(true);
    }

    internal void UnregisterInternal(IHiveData hiveData)
    {
        // 最後に Sync を実行する
        HiveSyncFlags flags = HiveSyncFlags.LoadFromFile;

        if (hiveData.IsReadOnly == false)
            flags |= HiveSyncFlags.SaveToFile | HiveSyncFlags.ForceUpdate;

        // エラーを無視
        hiveData.SyncWithStorageAsync(flags, true)._GetResult();

        lock (LockObj)
        {
            RegisteredHiveData.Remove(hiveData.DataName);
        }
    }

    protected override async Task CleanupImplAsync(Exception? ex)
    {
        // Flush all managed hives
        try
        {
            await SyncAllHiveDataAsync(default)._TryAwait();

            lock (RunningHivesListLockObj)
            {
                RunningHivesList.Remove(this);
            }

            await this.Archiver._DisposeSafeAsync();

            await this.Options.StorageProvider._DisposeSafeAsync();
        }
        finally
        {
            await base.CleanupImplAsync(ex);
        }
    }

    public HiveData<T> CreateHive<T>(string dataName, Func<T> getDefaultDataFunc, HiveSyncPolicy policy = HiveSyncPolicy.None) where T : class, new()
        => new HiveData<T>(this, dataName, getDefaultDataFunc, policy);

    public HiveData<T> CreateReadOnlyHive<T>(string dataName, Func<T> getDefaultDataFunc) where T : class, new()
        => CreateHive(dataName, getDefaultDataFunc, HiveSyncPolicy.ReadOnly);

    public HiveData<T> CreateAutoReadHive<T>(string dataName, Func<T> getDefaultDataFunc) where T : class, new()
        => CreateHive(dataName, getDefaultDataFunc, HiveSyncPolicy.AutoReadFromFile | HiveSyncPolicy.ReadOnly);

    public HiveData<T> CreateAutoSyncHive<T>(string dataName, Func<T> getDefaultDataFunc) where T : class, new()
        => CreateHive(dataName, getDefaultDataFunc, HiveSyncPolicy.AutoReadFromFile | HiveSyncPolicy.AutoWriteToFile);
}

[Flags]
public enum HiveSyncFlags
{
    None = 0,
    LoadFromFile = 1,
    SaveToFile = 2,
    ForceUpdate = 4,
}

[Flags]
public enum HiveSyncPolicy
{
    // 以下は Sync を実行しない
    None = 0,                           // 初期化時に読み込むのみ。手動書き込みが可能。
    ReadOnly = 1,                       // 初期化時に読み込むのみ。手動書き込みも不可能。

    // 以下は数秒ごとに自動 Sync を実行する
    AutoReadFromFile = 2,               // 物理ファイルの内容が変更されたら、自動的にメモリに読み込む
    AutoWriteToFile = 4,                // メモリの内容が変更されたら、自動的に物理ファイルに書き込む

    AutoReadWriteFile = AutoReadFromFile | AutoWriteToFile, // ※ 2020/11/01 時点ではバグがあったが、2020/12/01 時点で改善されている模様。
}

public interface IHiveData
{
    HiveSyncPolicy Policy { get; }
    string DataName { get; }
    Hive Hive { get; }
    bool IsManaged { get; }
    bool IsReadOnly { get; }
    Task SyncWithStorageAsync(HiveSyncFlags flag, bool ignoreError, CancellationToken cancel = default);
}

public interface INormalizable
{
    void Normalize();
}

[Flags]
public enum HiveDataEventType
{
    DataChanged = 4,
}

public class HiveData<T> : IHiveData, IDisposable where T : class, new()
{
    public HiveSyncPolicy Policy { get; private set; }
    public string DataName { get; }
    public Hive Hive { get; }
    public HiveSerializer Serializer { get; }
    public bool IsManaged { get; } = false;
    public bool IsReadOnly => this.Policy.Bit(HiveSyncPolicy.ReadOnly);
    //public CriticalSection ReaderWriterLockObj { get; } = new CriticalSection(); 不要? 2020/8/15 登

    Exception? InitialLoadError = null;

    T? DataInternal = null;
    long StorageHash = 0;

    public CriticalSection DataLock { get; } = new CriticalSection<HiveData<T>>();

    readonly AsyncLock StorageAsyncLock = new AsyncLock();

    readonly Func<T> GetDefaultDataFunc;

    readonly IHolder? Leak = null;

    public AsyncEventListenerList<HiveData<T>, HiveDataEventType> EventListener { get; }

    class HiveDataState
    {
        public T? Data;
        public Memory<byte> SerializedData;
        public long Hash;
    }

    public HiveData(Hive hive, string dataName, Func<T>? getDefaultDataFunc = null, HiveSyncPolicy policy = HiveSyncPolicy.None, HiveSerializerSelection serializer = HiveSerializerSelection.DefaultRuntimeJson, HiveSerializer? customSerializer = null)
    {
        this.EventListener = new AsyncEventListenerList<HiveData<T>, HiveDataEventType>();

        if (getDefaultDataFunc == null)
            getDefaultDataFunc = () => new T();

        switch (serializer)
        {
            case HiveSerializerSelection.DefaultRuntimeJson:
                customSerializer = new RuntimeJsonHiveSerializer();
                break;

            case HiveSerializerSelection.RichJson:
#if CORES_BASIC_JSON
                customSerializer = new RichJsonHiveSerializer();
                break;
#else // CORES_BASIC_JSON
                    throw new NotImplementedException("CORES_BASIC_JSON is not implemented.");
#endif // CORES_BASIC_JSON

            default:
                if (customSerializer == null)
                    throw new ArgumentNullException("serializerInstance");
                break;
        }

        this.Serializer = customSerializer;
        this.Hive = hive;
        this.Policy = policy;
        this.DataName = Hive.NormalizeDataName(dataName);
        this.GetDefaultDataFunc = getDefaultDataFunc;

        if (policy.BitAny(HiveSyncPolicy.AutoReadFromFile | HiveSyncPolicy.AutoWriteToFile))
        {
            this.IsManaged = true;
        }

        if (policy.Bit(HiveSyncPolicy.ReadOnly) && policy.Bit(HiveSyncPolicy.AutoWriteToFile))
        {
            throw new ArgumentException("Invalid flags: ReadOnly is set while AutoWriteToFile is set.");
        }

        if (this.IsManaged)
        {
            if (hive.Options.IsPollingEnabled == false)
                throw new ArgumentException($"policy = {policy.ToString()} while the Hive object doesn't support polling.");

            this.Hive.CheckPreRegisterInterval(this);
        }

        // Ensure to load the initial data from the storage (or create empty one)
        GetManagedData();

        // Initializing the managed hive
        if (this.IsManaged)
        {
            this.Hive.RegisterInternal(this);

            // リークチェック
            this.Leak = LeakChecker.Enter(LeakCounterKind.ManagedHiveRunning);
        }
    }

    public void Dispose() { this.Dispose(true); GC.SuppressFinalize(this); }
    Once DisposeFlag;
    protected virtual void Dispose(bool disposing)
    {
        if (!disposing || DisposeFlag.IsFirstCall() == false) return;

        // IsManaged の場合のみ Unregister 処理を実施する
        if (this.IsManaged)
        {
            this.Hive.UnregisterInternal(this);

            this.Leak._DisposeSafe();
        }
    }

    public T ManagedData { get => GetManagedData(); }

    T GetManagedData()
    {
        lock (DataLock)
        {
            if (this.DataInternal == null)
            {
                HiveDataState result;
                try
                {
                    // If the data is empty, first try loading from the storage.
                    result = LoadDataCoreAsync()._GetResult();

                    if (this.Policy.Bit(HiveSyncPolicy.ReadOnly) == false)
                    {
                        // If loading is ok, then write to the storage except readonly mode.
                        try
                        {
                            SaveDataCoreAsync(result.SerializedData, false)._GetResult();
                        }
                        catch { }
                    }
                }
                catch (Exception initialLoadError)
                {
                    // If the loading failed, then try create an empty one.
                    result = GetDefaultDataState();

                    // Save the initial data to the storage, however prevent to overwrite if the file exists on the storage.
                    try
                    {
                        SaveDataCoreAsync(result.SerializedData, true)._GetResult();
                    }
                    catch
                    {
                        // Save to the storage. Perhaps there is a file on the storage, and must not be overwritten to prevent data loss.
                        this.Policy |= HiveSyncPolicy.ReadOnly;
                        this.InitialLoadError = initialLoadError;
                    }
                }

                this.DataInternal = result.Data;
                this.StorageHash = result.Hash;
            }

            return this.DataInternal!;
        }
    }

    T? ReadOnlySnapshotCacheInternal = null;

    public T CachedFastSnapshot
    {
        [MethodImpl(Inline)]
        get => GetCachedFastSnapshot();
    }

    [MethodImpl(Inline)]
    public T GetCachedFastSnapshot()
    {
        if (ReadOnlySnapshotCacheInternal == null)
        {
            lock (DataLock)
            {
                T currentData = GetManagedData();

                Memory<byte> mem = Serializer.Serialize<T>(currentData);

                ReadOnlySnapshotCacheInternal = Serializer.Deserialize<T>(mem)!;
            }
        }

        return ReadOnlySnapshotCacheInternal;
    }

    void UpdateReadOnlySnapshot(T data)
    {
        if (data != null)
        {
            this.ReadOnlySnapshotCacheInternal = CloneData(data);
        }
    }

    public T GetManagedDataSnapshot()
    {
        lock (DataLock)
        {
            T currentData = GetManagedData();

            Memory<byte> mem = Serializer.Serialize<T>(currentData);

            return Serializer.Deserialize<T>(mem)!;
        }
    }

    [return: NotNullIfNotNull("data")]
    [return: MaybeNull]
    T CloneData(T data)
    {
        if (data == null) return null;

        Memory<byte> mem = Serializer.Serialize<T>(data);

        return Serializer.Deserialize<T>(mem)!;
    }

    public void SyncWithStorage(HiveSyncFlags flag, bool ignoreError, CancellationToken cancel = default)
        => SyncWithStorageAsync(flag, ignoreError, cancel)._GetResult();

    public async Task SyncWithStorageAsync(HiveSyncFlags flag, bool ignoreError, CancellationToken cancel = default)
    {
        //if (flag.Bit(HiveSyncFlags.SaveToFile)) flag |= HiveSyncFlags.ForceUpdate;

        try
        {
            await SyncWithStorageAsyncInternal(flag, ignoreError, cancel);
        }
        catch (Exception ex)
        {
            if (ignoreError)
            {
                Con.WriteError($"SyncWithStorageAsync (DataName = '{DataName}') error.\n{ex.ToString()}");
            }
            else
            {
                throw;
            }
        }
    }

    async Task SyncWithStorageAsyncInternal(HiveSyncFlags flag, bool ignoreError, CancellationToken cancel = default)
    {
        if (this.IsReadOnly && flag.Bit(HiveSyncFlags.SaveToFile))
            throw new ApplicationException("IsReadOnly is set.");

        bool dataChanged = false;

        try
        {
            using (await StorageAsyncLock.LockWithAwait(cancel))
            {
                T dataSnapshot = GetManagedDataSnapshot();
                HiveDataState dataSnapshotState = GetDataState(dataSnapshot);

                bool skipLoadFromFile = false;

                if (flag.Bit(HiveSyncFlags.SaveToFile) && (this.Policy.Bit(HiveSyncPolicy.AutoWriteToFile) || flag.Bit(HiveSyncFlags.ForceUpdate)))
                {
                    if (this.StorageHash != dataSnapshotState.Hash || flag.Bit(HiveSyncFlags.ForceUpdate))
                    {
                        dataChanged = true;

                        await SaveDataCoreAsync(dataSnapshotState.SerializedData, false, cancel);

                        this.StorageHash = dataSnapshotState.Hash;

                        skipLoadFromFile = true;

                        dataChanged = true;

                        UpdateReadOnlySnapshot(dataSnapshot);
                    }
                }

                if (flag.Bit(HiveSyncFlags.LoadFromFile) && this.Policy.Bit(HiveSyncPolicy.AutoReadFromFile) && skipLoadFromFile == false)
                {
                    HiveDataState loadDataState = await LoadDataCoreAsync(cancel);

                    if (loadDataState.Hash != dataSnapshotState.Hash || flag.Bit(HiveSyncFlags.ForceUpdate))
                    {
                        dataChanged = true;

                        UpdateReadOnlySnapshot(loadDataState.Data!);

                        lock (DataLock)
                        {
                            this.DataInternal = loadDataState.Data;
                            this.StorageHash = loadDataState.Hash;
                        }
                    }
                }
            }
        }
        finally
        {
            if (dataChanged)
            {
                // データが変化したらイベントを叩く
                await this.EventListener.FireAsync(this, HiveDataEventType.DataChanged);
            }
        }
    }

    async Task ReplaceDataAsync(T data, CancellationToken cancel = default)
    {
        if (this.IsReadOnly)
            throw new ApplicationException("IsReadOnly is set.");

        data = CloneData(data);

        HiveDataState dataState = GetDataState(data);

        using (await StorageAsyncLock.LockWithAwait(cancel))
        {
            lock (DataLock)
            {
                this.DataInternal = dataState.Data;
            }
        }
    }

    HiveDataState GetDataState(T data)
    {
        HiveDataState ret = new HiveDataState();

        ret.SerializedData = this.Serializer.Serialize(data);
        ret.Hash = Secure.HashSHA1AsLong(ret.SerializedData.Span);
        ret.Data = data;

        return ret;
    }

    HiveDataState GetDefaultDataState()
    {
        HiveDataState ret = new HiveDataState();

        T data = this.GetDefaultDataFunc();

        try
        {
            if (data is INormalizable data2) data2.Normalize();
        }
        catch { }

        ret.SerializedData = this.Serializer.Serialize(data);
        ret.Hash = Secure.HashSHA1AsLong(ret.SerializedData.Span);
        ret.Data = data;

        return ret;
    }

    string? cacheForSupressSameError = null;

    async Task<HiveDataState> LoadDataCoreAsync(CancellationToken cancel = default)
    {
        HiveDataState ret = new HiveDataState();

        Memory<byte> loadBytes = await Hive.StorageProvider.LoadAsync(this.DataName, cancel);
        try
        {
            T? data = this.Serializer.Deserialize<T>(loadBytes)!;

            try
            {
                if (data is INormalizable data2) data2.Normalize();
            }
            catch { }

            ret.SerializedData = this.Serializer.Serialize(data);
            ret.Hash = Secure.HashSHA1AsLong(ret.SerializedData.Span);
            ret.Data = data;

            cacheForSupressSameError = null;

            return ret;
        }
        catch (Exception ex)
        {
            if (cacheForSupressSameError._IsSame(ex.Message) == false)
            {
                cacheForSupressSameError = ex.Message;
                await Hive.StorageProvider.ReportErrorAsync(this.DataName, ex.ToString(), cancel);
            }
            throw;
        }
    }

    async Task SaveDataCoreAsync(ReadOnlyMemory<byte> serializedData, bool doNotOverwrite, CancellationToken cancel = default)
    {
        await Hive.StorageProvider.SaveAsync(this.DataName, serializedData, doNotOverwrite, cancel);
    }

    AsyncLock LoadSaveLock = new AsyncLock();

    AsyncLock AccessLock = new AsyncLock();

    public async Task<T> LoadDataAsync(CancellationToken cancel = default)
    {
        if (this.IsManaged) throw new ApplicationException($"The HiveData \"{this.DataName}\" is managed. LoadDataAsync() is not supported.");

        using (await LoadSaveLock.LockWithAwait(cancel))
        {
            await SyncWithStorageAsync(HiveSyncFlags.LoadFromFile | HiveSyncFlags.ForceUpdate, false, cancel);

            return this.ManagedData;
        }
    }

    public T LoadData(CancellationToken cancel = default)
        => LoadDataAsync(cancel)._GetResult();

    public async Task SaveDataAsync(T data, CancellationToken cancel = default)
    {
        if (this.IsManaged) throw new ApplicationException($"The HiveData \"{this.DataName}\" is managed. SaveDataAsync() is not supported.");

        using (await LoadSaveLock.LockWithAwait(cancel))
        {
            await ReplaceDataAsync(data, cancel);

            await SyncWithStorageAsync(HiveSyncFlags.SaveToFile | HiveSyncFlags.ForceUpdate, false, cancel);
        }
    }
    public void SaveData(T data, CancellationToken cancel = default)
        => SaveDataAsync(data, cancel)._GetResult();

    public async Task AccessDataAsync(bool writeMode, Func<T, Task> proc, CancellationToken cancel = default)
    {
        using (await AccessLock.LockWithAwait(cancel))
        {
            T data = await LoadDataAsync(cancel);

            if (this.InitialLoadError != null)
            {
                throw this.InitialLoadError;
            }

            await proc(data);

            if (writeMode)
            {
                await SaveDataAsync(data, cancel);
            }
        }
    }

    public void AccessData(bool writeMode, Action<T> proc, CancellationToken cancel = default)
        => AccessDataAsync(writeMode, (data) => { proc(data); return Task.CompletedTask; }, cancel)._GetResult();
}

[Serializable]
[DataContract]
public class HiveKeyValue : INormalizable
{
    public HiveKeyValue() => Normalize();

    LazyCriticalSection LockObj;

    [DataMember(IsRequired = true)]
    public SortedDictionary<string, object> Root = new SortedDictionary<string, object>(StrComparer.IgnoreCaseComparer);

    public void Normalize()
    {
        this.LockObj.EnsureCreated();

        if (this.Root == null) this.Root = new SortedDictionary<string, object>();
    }

    static string NormalizeKeyName(string keyName)
    {
        keyName = keyName._NonNullTrim();
        return keyName;
    }

    public bool SetObject(string keyName, object? value)
    {
        keyName = NormalizeKeyName(keyName);

        if (value == null)
        {
            return DeleteObject(keyName);
        }

        lock (LockObj.LockObj)
        {
            if (Root.ContainsKey(keyName))
            {
                Root[keyName] = value;
                return true;
            }
            else
            {
                Root.Add(keyName, value);
                return false;
            }
        }
    }

    public object? GetObject(string keyName, object? defaultValue = null)
    {
        keyName = NormalizeKeyName(keyName);
        lock (LockObj.LockObj)
        {
            if (Root.ContainsKey(keyName) == false)
            {
                if (defaultValue != null)
                {
                    SetObject(keyName, defaultValue); // Create the key with the default value
                }

                return defaultValue;
            }
            else
            {
                return Root[keyName];
            }
        }
    }

    public bool DeleteObject(string keyName)
    {
        keyName = NormalizeKeyName(keyName);
        lock (LockObj.LockObj)
        {
            if (Root.ContainsKey(keyName))
            {
                Root.Remove(keyName);
                return true;
            }

            return false;
        }
    }

    public bool IsObjectExists(string keyName)
    {
        keyName = NormalizeKeyName(keyName);
        lock (LockObj.LockObj)
        {
            return Root.ContainsKey(keyName);
        }
    }

    public bool Set<T>(string keyName, [AllowNull] T value)
        => SetObject(keyName, value);

    [return: MaybeNull]
    public T Get<T>(string keyName, [AllowNull] T defaultValue = default)
    {
        object? obj = GetObject(keyName, defaultValue);
        if (obj == null)
        {
            obj = defaultValue;
        }

#if CORES_BASIC_JSON
        if (obj is Newtonsoft.Json.Linq.JObject jobj)
        {
            return jobj.ToObject<T>();
        }
#endif  // CORES_BASIC_JSON

        return (T)obj!;
    }

    public bool Delete<T>(string keyName)
    {
        Get<T>(keyName);
        return DeleteObject(keyName);
    }

    public bool SetStr(string key, string value) => Set(key, value._NonNull());
    public string GetStr(string key, string? defaultValue = null) => Get(key, defaultValue)._NonNull();

    public bool SetSInt32(string key, int value) => Set(key, value);
    public int GetSInt32(string key, int defaultValue = 0) => Get(key, defaultValue);

    public bool SetSInt64(string key, long value) => Set(key, value);
    public long GetSInt64(string key, long defaultValue = 0) => Get(key, defaultValue);

    public bool SetBool(string key, bool value) => Set(key, value);
    public bool GetBool(string key, bool defaultValue = false) => Get(key, defaultValue);

    public bool SetEnum<T>(string key, T value) where T : unmanaged, Enum => SetStr(key, value.ToString());
    public T GetEnum<T>(string key, T defaultValue = default) where T : unmanaged, Enum
    {
        string str = GetStr(key);
        if (str._IsEmpty()) return defaultValue;
        return str._ParseEnum<T>(defaultValue);
    }
}

