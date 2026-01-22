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

using IPA.Cores.ClientApi.SlackApi;

namespace IPA.Cores.Basic;

public static partial class CoresConfig
{
    public static partial class InboxSettings
    {
        public static readonly Copenhagen<int> DefaultMaxMessagesPerAdapter = 100;
        public static readonly Copenhagen<int> DefaultMaxMessagesTotal = 1000;

        public static readonly Copenhagen<int> MaxFromLen = 60;
        public static readonly Copenhagen<int> MaxSubjectLen = 100;
        public static readonly Copenhagen<int> MaxBodyLen = 400;
    }
}

public class InboxMessage
{
    public string? Id;
    public string? Service;
    public string? ServiceImage;
    public string? Group;
    public string? FromImage;

    private string? from;
    private string? subject;
    private string? body;

    public DateTimeOffset Timestamp;
    public DateTimeOffset? FirstSeen;

    public string From { get => Safe(from, CoresConfig.InboxSettings.MaxFromLen); set => from = value; }
    public string Subject { get => Safe(subject, CoresConfig.InboxSettings.MaxSubjectLen); set => subject = value; }
    public string Body { get => Safe(body, CoresConfig.InboxSettings.MaxBodyLen); set => body = value; }

    static string Safe(string? src, int maxLen)
    {
        src = src._NonNullTrim();

        src = src._OneLine(" ")._TruncStrEx(maxLen);

        src = src._NormalizeSoftEther(true);

        return src;
    }
}

public class InboxMessageBox
{
    public InboxMessageBox(bool isInitialLoading)
    {
        this.IsInitialLoading = isInitialLoading;
    }

    public InboxMessage[] MessageList = new InboxMessage[0];

    public ulong Version;

    public bool IsInitialLoading = true;

    public bool IsFirst;

    public ulong CalcVersion()
    {
        string hashSrcStr = this.MessageList._ObjectToJson(compact: true) + this.IsInitialLoading.ToString();
        ulong v = hashSrcStr._CalcObjectHashByJson();

        this.Version = v;

        return v;
    }
}

public class Inbox : AsyncService
{
    public InboxOptions Options { get; }

    public FastEventListenerList<Inbox, NonsenseEventType> StateChangeEventListener { get; }

    readonly List<InboxAdapter> AdapterList = new List<InboxAdapter>();
    readonly CriticalSection LockObj = new CriticalSection<Inbox>();

    readonly InboxAdapterFactory Factory;

    readonly CriticalSection FirstSeenTableLock = new CriticalSection<Inbox>();

    readonly Dictionary<string, DateTimeOffset> FirstSeenTable = new Dictionary<string, DateTimeOffset>();

    public Inbox(InboxOptions? options = null)
    {
        try
        {
            this.Options = options ?? new InboxOptions();

            this.StateChangeEventListener = new FastEventListenerList<Inbox, NonsenseEventType>();

            this.Factory = new InboxAdapterFactory(this, this.Options);
        }
        catch
        {
            this._DisposeSafe();
            throw;
        }
    }

    public InboxMessageBox GetMessageBox()
    {
        InboxAdapter[] adaptersList = this.EnumAdapters();

        List<InboxMessage> msgList = new List<InboxMessage>();

        DateTimeOffset now = DateTimeOffset.Now;

        bool isInitialLoading = false;

        foreach (InboxAdapter a in adaptersList)
        {
            InboxMessageBox? box = a.MessageBox;

            if (a.InitialLoading) isInitialLoading = true;

            if (box != null && box.MessageList != null)
            {
                lock (FirstSeenTableLock)
                {
                    foreach (InboxMessage m in box.MessageList)
                    {
                        if (m.Id._IsFilled())
                        {
                            if (m.FirstSeen == null)
                            {
                                m.FirstSeen = m.Timestamp;

                                if (FirstSeenTable.ContainsKey(m.Id) == false)
                                {
                                    if (box.IsFirst == false)
                                    {
                                        m.FirstSeen = now;
                                    }
                                    FirstSeenTable[m.Id] = m.FirstSeen.Value;
                                }
                                else
                                {
                                    m.FirstSeen = FirstSeenTable[m.Id];
                                }
                            }

                            msgList.Add(m);
                        }
                    }
                }
            }
        }

        InboxMessageBox ret = new InboxMessageBox(isInitialLoading);

        ret.MessageList = msgList.OrderByDescending(x => x.FirstSeen).ThenByDescending(x => x.Timestamp).Take(this.Options.MaxMessagesTotal).ToArray();

        ret.CalcVersion();

        return ret;
    }

    public string[] GetProviderNameList()
    {
        return this.Factory.ProviderNameList.ToArray();
    }

    public InboxAdapter AddAdapter(string guid, string providerName, InboxAdapterAppCredential appCredential)
    {
        lock (LockObj)
        {
            if (this.AdapterList.Where(x => x.Guid._IsSamei(guid)).Any())
                throw new ArgumentException("guid already exists");

            InboxAdapter adapter = this.Factory.Create(guid, providerName, appCredential);

            this.AdapterList.Add(adapter);

            return adapter;
        }
    }

    public void DeleteAdapter(string guid)
    {
        InboxAdapter adapter;

        lock (LockObj)
        {
            adapter = this.AdapterList.Where(x => x.Guid._IsSamei(guid)).Single();

            this.AdapterList.Remove(adapter);
        }

        adapter._DisposeSafe();
    }

    public void StartAdapter(string guid, InboxAdapterUserCredential userCredential)
    {
        lock (LockObj)
        {
            InboxAdapter adapter = this.AdapterList.Where(x => x.Guid._IsSamei(guid)).Single();

            adapter.Start(userCredential);
        }
    }

    public InboxAdapter[] EnumAdapters()
    {
        lock (LockObj)
        {
            return this.AdapterList.ToArray();
        }
    }

    protected override void DisposeImpl(Exception? ex)
    {
        try
        {
            lock (LockObj)
            {
                foreach (InboxAdapter adapter in this.AdapterList)
                {
                    adapter._DisposeSafe();
                }

                this.AdapterList.Clear();
            }
        }
        finally
        {
            base.DisposeImpl(ex);
        }
    }

    public string GetProviderAddingAppHelpString(string providerName)
    {
        string fileName = $"Inbox/inbox_app_add_help_{providerName}.txt";

        return CoresRes[fileName].String;
    }
}

public class InboxAdapterFactory
{
    public Inbox Inbox { get; }
    public InboxOptions Options { get; }

    SortedDictionary<string, Func<string, InboxAdapterAppCredential, InboxAdapter>> ProviderList = new SortedDictionary<string, Func<string, InboxAdapterAppCredential, InboxAdapter>>(StrComparer.IgnoreCaseComparer);

    public IReadOnlyList<string> ProviderNameList => this.ProviderList.Keys.ToList();

    public InboxAdapterFactory(Inbox inbox, InboxOptions options)
    {
        this.Inbox = inbox;
        this.Options = options;

        AddProvider(Consts.InboxProviderNames.Slack_App, (guid, cred) => new InboxSlackPerAppAdapter(guid, this.Inbox, cred, this.Options));

        AddProvider(Consts.InboxProviderNames.Slack_User, (guid, cred) => new InboxSlackPerUserAdapter(guid, this.Inbox, cred, this.Options));

        AddProvider(Consts.InboxProviderNames.Gmail, (guid, cred) => new InboxGmailAdapter(guid, this.Inbox, cred, this.Options));
    }

    void AddProvider(string name, Func<string, InboxAdapterAppCredential, InboxAdapter> newFunction)
    {
        if (this.ProviderList.ContainsKey(name))
            throw new ArgumentException("Duplicated provider key");

        this.ProviderList.Add(name, newFunction);
    }

    public InboxAdapter Create(string guid, string providerName, InboxAdapterAppCredential appCredential)
    {
        return this.ProviderList[providerName](guid, appCredential);
    }
}

public class InboxAdapterUserCredential
{
    public string? AccessToken;
}

public class InboxAdapterAppCredential
{
    public string? ClientId;
    public string? ClientSecret;
}

public sealed class InboxOptions
{
    public TcpIpSystem TcpIp { get; }
    public int MaxMessagesPerAdapter { get; }
    public int MaxMessagesTotal { get; }
    public bool RecordRealtimeTextLog { get; }
    public bool IgnoreSslCert { get; }

    public InboxOptions(TcpIpSystem? tcpIp = null, int maxMessagesPerAdapter = DefaultSize, int maxMessagesTotal = DefaultSize, bool recordRealtimeTextLog = false, bool ignoreSslCert = false)
    {
        this.TcpIp = tcpIp ?? LocalNet;

        this.MaxMessagesPerAdapter = maxMessagesPerAdapter._DefaultSize(CoresConfig.InboxSettings.DefaultMaxMessagesPerAdapter);
        this.MaxMessagesTotal = maxMessagesTotal._DefaultSize(CoresConfig.InboxSettings.DefaultMaxMessagesTotal);
        this.RecordRealtimeTextLog = recordRealtimeTextLog;
        this.IgnoreSslCert = ignoreSslCert;
    }
}

public abstract class InboxAdapter : AsyncServiceWithMainLoop
{
    public Inbox Inbox { get; }

    public abstract string AdapterName { get; }

    public InboxOptions AdapterOptions { get; }
    public InboxAdapterAppCredential AppCredential { get; }
    public InboxAdapterUserCredential? UserCredential { get; protected set; }

    public abstract string? AccountInfoStr { get; }
    public abstract bool IsStarted { get; }

    public bool InitialLoading { get; protected set; } = false;

    public InboxMessageBox? MessageBox { get; private set; }

    public Exception? LastError { get; private set; }

    public string Guid { get; }

    public abstract string AuthStartGetUrl(string redirectUrl, string state = "");

    public InboxAdapter(string guid, Inbox inbox, InboxAdapterAppCredential appCredential, InboxOptions? adapterOptions)
    {
        this.Guid = guid;
        this.Inbox = inbox;

        this.AdapterOptions = adapterOptions ?? new InboxOptions();

        this.AppCredential = appCredential;
    }

    public abstract Task<InboxAdapterUserCredential> AuthGetCredentialAsync(string code, string redirectUrl, CancellationToken cancel = default);

    public void Start(InboxAdapterUserCredential credential)
    {
        StartImpl(credential);

        this.InitialLoading = true;

        this.StartMainLoop(MainLoopAsync);
    }

    protected abstract void StartImpl(InboxAdapterUserCredential credential);

    protected abstract Task MainLoopImplAsync(CancellationToken cancel);

    async Task MainLoopAsync(CancellationToken cancel)
    {
        while (true)
        {
            cancel.ThrowIfCancellationRequested();

            try
            {
                await this.MainLoopImplAsync(cancel);
            }
            catch (Exception ex)
            {
                ex._Debug();

                await cancel._WaitUntilCanceledAsync(5000);
            }
        }
    }

    protected void MessageBoxUpdatedCallback(InboxMessageBox box)
    {
        this.MessageBox = box;

        this.Inbox.StateChangeEventListener.FireSoftly(this.Inbox, NonsenseEventType.Nonsense);
    }

    protected void ClearLastError() => SetLastError(null);

    protected void SetLastError(Exception? ex)
    {
        this.LastError = ex;
    }
}

#endif  // CORES_BASIC_JSON
