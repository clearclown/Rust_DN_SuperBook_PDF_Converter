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
// StatMan: 統計マネージャ

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
using Newtonsoft.Json.Linq;

namespace IPA.Cores.Basic;

public static partial class CoresConfig
{
    public static partial class StatMan
    {
        public static readonly Copenhagen<int> DefaultPostIntervalMsecs = (30 * 60 * 1000);
        public static readonly Copenhagen<int> DefaultSaveIntervalMsecs = (1 * 60 * 1000);
        public static readonly Copenhagen<string> DefaultStatFileName = "Statistics.db";
        public static readonly Copenhagen<string> DefaultSystemName = "default_system";
        public static readonly Copenhagen<string> DefaultLogName = "default_stat";
        public static readonly Copenhagen<string> DefaultUrl = "https://dn-stat1.ep.ipantt.net/stat/";
    }
}

public class StatManDatabase : INormalizable
{
    public string Uid = "";
    public SortedDictionary<string, long> LongValues = new SortedDictionary<string, long>(StrComparer.IgnoreCaseTrimComparer);
    public SortedDictionary<string, string> StrValues = new SortedDictionary<string, string>(StrComparer.IgnoreCaseTrimComparer);

    public void Normalize()
    {
        if (this.Uid._IsEmpty()) this.Uid = Str.GenRandStr();
        if (LongValues == null) LongValues = new SortedDictionary<string, long>(StrComparer.IgnoreCaseTrimComparer);
        if (StrValues == null) StrValues = new SortedDictionary<string, string>(StrComparer.IgnoreCaseTrimComparer);
    }
}

public delegate Task StatManPollCallbackAsync(object? Param, SortedDictionary<string, long> LongValues, SortedDictionary<string, string> StrValues);

[Serializable]
public class StatManConfig : INormalizable
{
    public int PostIntervalMsecs { get; set; }
    public int SaveIntervalMsecs { get; set; }
    public string PostUrl { get; set; } = "";
    public string StatFileName { get; set; } = "";
    public string SystemName { get; set; } = "";
    public string LogName { get; set; } = "";

    public object? Param { get; set; }

    [NonSerialized]
    public StatManPollCallbackAsync? Callback;

    public void Normalize()
    {
        if (PostIntervalMsecs <= 0) PostIntervalMsecs = CoresConfig.StatMan.DefaultPostIntervalMsecs;
        if (SaveIntervalMsecs <= 0) SaveIntervalMsecs = CoresConfig.StatMan.DefaultSaveIntervalMsecs;
        StatFileName = StatFileName._FilledOrDefault(CoresConfig.StatMan.DefaultStatFileName);
        SystemName = SystemName._FilledOrDefault(CoresConfig.StatMan.DefaultSystemName);
        LogName = LogName._FilledOrDefault(CoresConfig.StatMan.DefaultLogName);
        PostUrl = PostUrl._FilledOrDefault(CoresConfig.StatMan.DefaultUrl);
    }
}

public class StatMan : AsyncService
{
    readonly StatManConfig Config;

    readonly Task SaveTask;
    readonly Task PostTask;

    readonly AsyncLock FileLock = new AsyncLock();
    readonly CriticalSection DataLock = new CriticalSection<StatMan>();

    IPAddress CurrentLocalIp = IPAddress.Loopback;

    StatManDatabase Database;
    
    public string FileNameFullPath;

    public StatMan(StatManConfig config)
    {
        try
        {
            // this.Config = config._CloneDeep(); DO NOT CLONE THIE OBJECT!! delegate をクローンすると不具合が発生します。
            this.Config = config;

            this.Config.Callback = config.Callback;

            this.Config.Normalize();

            this.FileNameFullPath = this.Config.StatFileName;
            if (PP.IsAbsolutePath(this.Config.StatFileName) == false)
            {
                this.FileNameFullPath = Env.AppLocalDir._CombinePath("Config", "Statistics", this.Config.StatFileName);
            }

            this.FileNameFullPath = PP.NormalizeDirectorySeparator(this.FileNameFullPath, true);

            this.Database = Lfs.ReadJsonFromFileEncrypted<StatManDatabase>(this.FileNameFullPath, Consts.Strings.StatManEncryptKey, nullIfError: true);
            if (this.Database == null)
            {
                this.Database = new StatManDatabase();
            }

            this.Database.Normalize();

            NormalizeAndPollAsync(noPoll: true)._GetResult();

            this.SaveTask = SaveTaskProcAsync(this.GrandCancel)._LeakCheck();
            this.PostTask = PostTaskProcAsync(this.GrandCancel)._LeakCheck();
        }
        catch (Exception ex)
        {
            this._DisposeSafe(ex);
            throw;
        }
    }

    async Task SaveTaskProcAsync(CancellationToken cancel = default)
    {
        while (true)
        {
            if (cancel.IsCancellationRequested)
            {
                break;
            }

            int interval = Util.GenRandInterval(Config.SaveIntervalMsecs);

            await NormalizeAndPollAsync(cancel);

            await cancel._WaitUntilCanceledAsync(interval);
        }

        await NormalizeAndPollAsync();
    }

    async Task PostTaskProcAsync(CancellationToken cancel = default)
    {
        int num_try = 0;

        while (true)
        {
            int interval;

            IPAddress tmp = await LocalNet.GetMyPrivateIpAsync(cancel);

            if (tmp._GetIPAddressType().Bit(IPAddressType.Loopback) == false)
            {
                this.CurrentLocalIp = tmp;
            }

            num_try++;

            if (await PostHttpMainAsync(cancel)._TryAwaitAndRetBool(true))
            {
                num_try = 0;
            }

            if (cancel.IsCancellationRequested)
            {
                break;
            }

            interval = Util.GenRandIntervalWithRetry(Config.PostIntervalMsecs, num_try + 1, Config.PostIntervalMsecs * 30);

            await cancel._WaitUntilCanceledAsync(interval);
        }
    }

    async Task PostHttpMainAsync(CancellationToken cancel = default)
    {
        if (this.Config.PostUrl._IsEmpty()) return;

        StatManDatabase copy;

        lock (this.DataLock)
        {
            copy = this.Database._CloneWithJson();
        }

        KeyValueList<string, string> vers = new KeyValueList<string, string>();

        vers.Add("APPNAME", CoresLib.AppNameFnSafe);
        vers.Add("OS", Env.OsInfoString);
        vers.Add("CPU", Env.CpuInfo.ToString());
        vers.Add("NUMCPU", Env.NumCpus.ToString());
        vers.Add("DOTNET", Env.FrameworkInfoString);
        vers.Add("EXE", Env.AppExecutableExeOrDllFileName._GetFileName() ?? "");

        List<string> versStrs = new List<string>();
        vers.ForEach(x => versStrs.Add($"{x.Key}={x.Value}"));

        var data = Json.NewJsonObject();

        foreach (var item in copy.StrValues)
        {
            data.TryAdd(item.Key, new JValue(item.Value));
        }

        foreach (var item in copy.LongValues)
        {
            data.TryAdd(item.Key, new JValue(item.Value));
        }

        DataVaultData postData = new DataVaultData
        {
            TimeStamp = DtOffsetNow,
            StatUid = copy.Uid,
            StatAppVer = versStrs._Combine("|"),
            StatGitCommitId = Dbg.GetCurrentGitCommitId(),
            StatLocalIp = this.CurrentLocalIp.ToString(),
            StatLocalFqdn = LocalNet.GetHostInfo(true).HostName,
            SystemName = Config.SystemName,
            LogName = Config.LogName,
            Data = data,
        };

        string postStr = postData._ObjectToJson();

        using var http = new WebApi(new WebApiOptions(new WebApiSettings { SslAcceptAnyCerts = true }));

        var ret = await http.SimplePostJsonAsync(WebMethods.POST, Config.PostUrl, postStr, cancel);

        if (ret.ToString()._InStr("ok") == false)
        {
            throw new CoresException($"Http error: {ret.ToString()._TruncStrEx(1000)}");
        }
    }

    public void AddReport(string name, long value)
    {
        lock (DataLock)
        {
            if (name.EndsWith("_total", StringComparison.OrdinalIgnoreCase))
            {
                long current = 0;

                if (this.Database.LongValues.TryGetValue(name, out current) == false)
                {
                    current = 0;
                }

                current += value;

                this.Database.LongValues[name] = current;
            }
            else
            {
                this.Database.LongValues[name] = value;
            }
        }
    }

    public void AddReport(string name, string value)
    {
        lock (DataLock)
        {
            this.Database.StrValues[name] = value;
        }
    }

    async Task NormalizeAndPollAsync(CancellationToken cancel = default, bool noPoll = false)
    {
        using (await FileLock.LockWithAwait(cancel))
        {
            if (noPoll == false && this.IsCanceled == false && this.IsCleanuped == false)
            {
                if (this.Config.Callback != null)
                {
                    StatManDatabase tmp = new StatManDatabase();

                    await this.Config.Callback(Config.Param, tmp.LongValues, tmp.StrValues)._TryWaitAsync();

                    tmp.LongValues._DoForEach(x => this.AddReport(x.Key, x.Value));
                    tmp.StrValues._DoForEach(x => this.AddReport(x.Key, x.Value));
                }
            }

            StatManDatabase dataCopy;

            lock (DataLock)
            {
                dataCopy = this.Database._CloneWithJson();
            }

            await Lfs.WriteJsonToFileEncryptedAsync(this.FileNameFullPath, dataCopy, Consts.Strings.StatManEncryptKey, FileFlags.AutoCreateDirectory, cancel: cancel)._TryWaitAsync();
        }
    }

    protected override async Task CleanupImplAsync(Exception? ex)
    {
        try
        {
            await this.SaveTask._TryWaitAsync(true);
            await this.PostTask._TryWaitAsync(true);
        }
        finally
        {
            await base.CleanupImplAsync(ex);
        }
    }
}

#endif

