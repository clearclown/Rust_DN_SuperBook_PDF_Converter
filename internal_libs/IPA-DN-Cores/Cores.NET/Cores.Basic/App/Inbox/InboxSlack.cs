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
    public static partial class InboxSlackAdapterSettings
    {
        public static readonly Copenhagen<int> RefreshAllInterval = 30 * 60 * 1000;
        public static readonly Copenhagen<int> MaxConcurrentTasks = 1;
    }
}

public class InboxSlackPerUserAdapter : InboxSlackPerAppAdapter
{
    public override string AdapterName => Consts.InboxProviderNames.Slack_User;

    public InboxSlackPerUserAdapter(string guid, Inbox inbox, InboxAdapterAppCredential appCredential, InboxOptions? adapterOptions = null) : base(guid, inbox, appCredential, adapterOptions)
    {
    }

    public override string AuthStartGetUrl(string redirectUrl, string state = "")
    {
        return "";
    }

    public override Task<InboxAdapterUserCredential> AuthGetCredentialAsync(string code, string redirectUrl, CancellationToken cancel = default)
    {
        throw new NotImplementedException();
    }

    protected override void StartImpl(InboxAdapterUserCredential credential)
    {
        if (credential == null) throw new ArgumentNullException("credential");

        if (Started.IsFirstCall())
        {
            this.UserCredential = credential;

            this.Api = new SlackApi("", "", this.AppCredential.ClientSecret, this.AdapterOptions.IgnoreSslCert);
        }
        else
        {
            throw new ApplicationException("Already started.");
        }
    }
}

public class InboxSlackPerAppAdapter : InboxAdapter
{
    public override string AdapterName => Consts.InboxProviderNames.Slack_App;

    string? currentAccountInfoStr = null;

    public override string? AccountInfoStr => currentAccountInfoStr;
    public override bool IsStarted => this.Started.IsSet;


    protected SlackApi? Api;

    public InboxSlackPerAppAdapter(string guid, Inbox inbox, InboxAdapterAppCredential appCredential, InboxOptions? adapterOptions = null)
        : base(guid, inbox, appCredential, adapterOptions)
    {
    }

    protected Once Started;

    protected override void StartImpl(InboxAdapterUserCredential credential)
    {
        if (credential == null) throw new ArgumentNullException("credential");

        if (Started.IsFirstCall())
        {
            this.UserCredential = credential;

            this.Api = new SlackApi(this.AppCredential.ClientId._NullCheck(), this.AppCredential.ClientSecret._NullCheck(), this.UserCredential.AccessToken, this.AdapterOptions.IgnoreSslCert);
        }
        else
        {
            throw new ApplicationException("Already started.");
        }
    }

    protected override Task CancelImplAsync(Exception? ex)
    {
        return base.CancelImplAsync(ex);
    }

    protected override Task CleanupImplAsync(Exception? ex)
    {
        return base.CleanupImplAsync(ex);
    }

    protected override void DisposeImpl(Exception? ex)
    {
        try
        {
            this.Api._DisposeSafe();
        }
        finally
        {
            base.DisposeImpl(ex);
        }
    }

    public override string AuthStartGetUrl(string redirectUrl, string state = "")
    {
        using (SlackApi tmpApi = new SlackApi(this.AppCredential.ClientId._NullCheck(), this.AppCredential.ClientSecret._NullCheck(), ignoreSslCert: this.AdapterOptions.IgnoreSslCert))
        {
            return tmpApi.AuthGenerateAuthorizeUrl(Consts.OAuthScopes.Slack_Client, redirectUrl, state);
        }
    }

    public override async Task<InboxAdapterUserCredential> AuthGetCredentialAsync(string code, string redirectUrl, CancellationToken cancel = default)
    {
        using (SlackApi tmpApi = new SlackApi(this.AppCredential.ClientId._NullCheck(), this.AppCredential.ClientSecret._NullCheck(), ignoreSslCert: this.AdapterOptions.IgnoreSslCert))
        {
            SlackApi.AccessToken token = await tmpApi.AuthGetAccessTokenAsync(code, redirectUrl, cancel);

            return new InboxAdapterUserCredential { AccessToken = token.access_token };
        }
    }

    SlackApi.User[]? UserList;
    SlackApi.Channel[]? ConversationList;
    SlackApi.Team? TeamInfo;
    string[]? MutedChannelList;
    Dictionary<string, SlackApi.Message[]> MessageListPerConversation = new Dictionary<string, SlackApi.Message[]>(StrComparer.IgnoreCaseComparer);

    readonly AsyncAutoResetEvent UpdateChannelsEvent = new AsyncAutoResetEvent();
    readonly CriticalSection UpdateChannelsListLock = new CriticalSection<InboxSlackPerAppAdapter>();
    readonly HashSet<string> UpdateChannelsList = new HashSet<string>(StrComparer.IgnoreCaseComparer);

    async Task RealtimeRecvLoopAsync(CancellationToken cancel)
    {
        while (true)
        {
            cancel.ThrowIfCancellationRequested();

            try
            {
                using (WebSocket ws = await Api!.RealtimeConnectAsync(cancel))
                {
                    using (PipeStream st = ws.GetStream())
                    {
                        while (true)
                        {
                            ReadOnlyMemory<byte> recvData = await st.ReceiveAsync(cancel: cancel);

                            if (recvData.IsEmpty) throw new DisconnectedException();

                            try
                            {
                                dynamic json = recvData._GetString_UTF8()._JsonToDynamic()!;

                                string realtimeStr = Json.SerializeDynamic(json);

                                new { Workspace = this.AccountInfoStr, DataJson = realtimeStr }._PostData("slack_realtime_log");

                                string channel = json!.channel;
                                string type = json.type;

                                if (type._IsSamei("message") || type._IsSamei("channel_marked") || type._IsSamei("im_marked") || type._IsSamei("group_marked") || type._IsSamei("mpim_marked"))
                                {
                                    if (channel._IsFilled())
                                    {
                                        lock (UpdateChannelsListLock)
                                        {
                                            UpdateChannelsList.Add(channel);
                                        }

                                        UpdateChannelsEvent.Set(true);
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                Con.WriteDebug($"RealtimeRecvLoopAsync: Ignoring JSON Parse Error ({ex.Message}): '{recvData._GetString_UTF8()}'");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ex._Debug();
            }

            cancel.ThrowIfCancellationRequested();

            await cancel._WaitUntilCanceledAsync(15 * 1000);
        }
    }

    protected override async Task MainLoopImplAsync(CancellationToken cancel)
    {
        MessageListPerConversation.Clear();

        cancel.ThrowIfCancellationRequested();

        CancellationTokenSource realTimeTaskCancel = new CancellationTokenSource();

        Task realTimeTask = RealtimeRecvLoopAsync(realTimeTaskCancel.Token);

        try
        {
            bool all = true;
            string[]? targetChannelIdList = null;

            while (true)
            {
                cancel.ThrowIfCancellationRequested();

                ExceptionWhen reason;

                try
                {
                    InboxMessageBox box = await ReloadInternalAsync(all, targetChannelIdList, cancel);

                    ClearLastError();

                    this.InitialLoading = false;

                    MessageBoxUpdatedCallback(box);

                    if (this.UpdateChannelsList.Count == 0)
                    {
                        reason = await TaskUtil.WaitObjectsAsync(
                            cancels: cancel._SingleArray(),
                            events: this.UpdateChannelsEvent._SingleArray(),
                            timeout: Util.GenRandInterval(CoresConfig.InboxSlackAdapterSettings.RefreshAllInterval));
                    }
                    else
                    {
                        reason = ExceptionWhen.None;
                    }
                }
                catch (Exception ex)
                {
                    SetLastError(ex._GetSingleException());

                    ex._Debug();

                    reason = ExceptionWhen.TimeoutException;

                    await cancel._WaitUntilCanceledAsync(15000);
                }

                if (reason == ExceptionWhen.TimeoutException)
                {
                    all = true;
                    targetChannelIdList = null;
                    lock (this.UpdateChannelsListLock)
                    {
                        this.UpdateChannelsList.Clear();
                    }
                }
                else
                {
                    all = false;
                    lock (this.UpdateChannelsListLock)
                    {
                        targetChannelIdList = this.UpdateChannelsList.ToArray();
                        this.UpdateChannelsList.Clear();
                    }
                }
            }
        }
        finally
        {
            realTimeTaskCancel._TryCancel();

            await realTimeTask._TryWaitAsync(true);
        }
    }

    int numReload = 0;

    public class SlackSnapshot
    {
        public SlackApi.Team? TeamInfo;
        public SlackApi.User[]? UserList;
        public SlackApi.Channel[]? ConversationList;
        public string[]? MutedChannelList;
    }

    public class SlackStatus
    {
        public SlackApi.Team? TeamInfo;
        public int NumUnreadMessages;
    }

    int LastUnread = 0;
    int LastSnapshotDay = -1;

    async Task<InboxMessageBox> ReloadInternalAsync(bool all, IEnumerable<string>? targetChannelIDs, CancellationToken cancel)
    {
        List<InboxMessage> msgList = new List<InboxMessage>();

        // Team info
        this.TeamInfo = await Api!.GetTeamInfoAsync(cancel);

        currentAccountInfoStr = this.TeamInfo.name;

        if (all)
        {
            // Enum users
            this.UserList = await Api.GetUsersListAsync(cancel);

            // Clear cache
            this.MessageListPerConversation.Clear();

            // Muted channels list
            this.MutedChannelList = await Api.GetMutedChannels(cancel);
        }

        // Enum conversations
        this.ConversationList = await Api.GetConversationsListAsync(cancel);

        if (LastSnapshotDay != DateTime.Now.Day)
        {
            LastSnapshotDay = DateTime.Now.Day;

            SlackSnapshot snapshot = new SlackSnapshot
            {
                TeamInfo = this.TeamInfo,
                UserList = this.UserList,
                ConversationList = this.ConversationList,
                MutedChannelList = this.MutedChannelList,
            };

            snapshot._PostData("slack_snapshot");
        }

        // Enum messages
        foreach (var conv in ConversationList)
        {
            if (conv.id._IsFilled())
            {
                bool reload = false;

                if (conv.IsTarget())
                {
                    if (all)
                    {
                        reload = true;
                    }
                    else
                    {
                        if (targetChannelIDs!.Contains(conv.id, StrComparer.IgnoreCaseComparer))
                        {
                            reload = true;
                        }
                    }

                    if (MutedChannelList != null && MutedChannelList.Where(x => x._IsSamei(conv.id)).Any())
                    {
                        reload = false;
                    }
                }

                if (reload)
                {
                    // Get the conversation info
                    SlackApi.Channel convInfo = await Api.GetConversationInfoAsync(conv.id, cancel);

                    // Get unread messages
                    SlackApi.Message[] messages = await Api.GetConversationHistoryAsync(conv.id, convInfo.last_read, this.Inbox.Options.MaxMessagesPerAdapter, cancel: cancel);

                    MessageListPerConversation[conv.id] = messages;
                }

                if (conv.IsTarget())
                {
                    if (MessageListPerConversation.TryGetValue(conv.id, out SlackApi.Message[]? messages))
                    {
                        foreach (SlackApi.Message message in messages)
                        {
                            var user = GetUser(message.user);

                            string group_name = "";

                            if (conv.is_channel)
                            {
                                group_name = "#" + conv.name_normalized;
                            }
                            else if (conv.is_im)
                            {
                                group_name = "@" + GetUser(conv.user)?.profile?.real_name ?? "unknown";
                            }
                            else
                            {
                                group_name = "@" + conv.name_normalized;
                            }

                            InboxMessage m = new InboxMessage
                            {
                                Id = this.Guid + "_" + message.ts.ToString(),

                                Service = TeamInfo.name._DecodeHtml(),
                                FromImage = TeamInfo.icon?.image_132 ?? "",

                                From = (user?.profile?.real_name ?? "Unknown User")._DecodeHtml(),
                                ServiceImage = user?.profile?.image_512 ?? "",

                                Group = group_name._DecodeHtml(),

                                Body = message.text._DecodeHtml(),
                                Timestamp = message.ts._ToDateTimeOfSlack(),
                            };

                            m.Subject = this.TeamInfo.name._DecodeHtml();

                            m.Body = m.Body._SlackExpandBodyUsername(this.UserList);

                            if (message.upload)
                            {
                                m.Body += $"Filename: '{message.files!.FirstOrDefault()?.name ?? "Unknown Filename"}'";
                            }

                            msgList.Add(m);
                        }
                    }
                }
            }
        }

        InboxMessageBox ret = new InboxMessageBox(false)
        {
            MessageList = msgList.OrderByDescending(x => x.Timestamp).Take(this.Inbox.Options.MaxMessagesPerAdapter).ToArray(),
        };

        int numUnread = ret.MessageList.Length;

        if (this.LastUnread != numUnread)
        {
            this.LastUnread = numUnread;

            SlackStatus st = new SlackStatus
            {
                NumUnreadMessages = numUnread,
                TeamInfo = this.TeamInfo,
            };

            st._PostData("slack_status");
        }

        ClearLastError();

        if (numReload == 0)
        {
            ret.IsFirst = true;
        }

        numReload++;

        return ret;
    }

    SlackApi.User? GetUser(string? userId)
    {
        if (userId._IsEmpty()) return null;
        SlackApi.User? user = this.UserList!.Where(x => x.id._IsSamei(userId)).SingleOrDefault();
        return user;
    }
}

#endif  // CORES_BASIC_JSON
