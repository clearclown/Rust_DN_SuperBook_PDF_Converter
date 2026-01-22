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

using IPA.Cores.ClientApi.GoogleApi;

namespace IPA.Cores.Basic;

public static partial class CoresConfig
{
    public static partial class InboxGmailAdapterSettings
    {
        public static readonly Copenhagen<int> ReloadInterval = 1 * 1000;
    }
}

public class InboxGmailAdapter : InboxAdapter
{
    public override string AdapterName => Consts.InboxProviderNames.Gmail;

    string? currentAccountInfoStr = null;

    public override string? AccountInfoStr => currentAccountInfoStr;
    public override bool IsStarted => this.Started.IsSet;

    GoogleApi? Api;

    public InboxGmailAdapter(string guid, Inbox inbox, InboxAdapterAppCredential appCredential, InboxOptions? adapterOptions = null)
        : base(guid, inbox, appCredential, adapterOptions)
    {
    }

    Once Started;

    protected override void StartImpl(InboxAdapterUserCredential credential)
    {
        if (credential == null) throw new ArgumentNullException("credential");

        if (Started.IsFirstCall())
        {
            this.UserCredential = credential;

            this.Api = new GoogleApi(this.AppCredential.ClientId._NullCheck(), this.AppCredential.ClientSecret._NullCheck(), this.UserCredential.AccessToken);
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
        using (GoogleApi tmpApi = new GoogleApi(this.AppCredential.ClientId._NullCheck(), this.AppCredential.ClientSecret._NullCheck()))
        {
            return tmpApi.AuthGenerateAuthorizeUrl(Consts.OAuthScopes.Google_Gmail, redirectUrl, state);
        }
    }

    public override async Task<InboxAdapterUserCredential> AuthGetCredentialAsync(string code, string redirectUrl, CancellationToken cancel = default)
    {
        using (GoogleApi tmpApi = new GoogleApi(this.AppCredential.ClientId._NullCheck(), this.AppCredential.ClientSecret._NullCheck()))
        {
            GoogleApi.AccessToken token = await tmpApi.AuthGetAccessTokenAsync(code, redirectUrl, cancel);

            return new InboxAdapterUserCredential { AccessToken = token.refresh_token };
        }
    }

    GoogleApi.GmailProfile? currentProfile = null;

    protected override async Task MainLoopImplAsync(CancellationToken cancel)
    {
        cancel.ThrowIfCancellationRequested();

        GoogleApi.GmailProfile profile;

        try
        {
            profile = await Api!.GmailGetProfileAsync(cancel);

            ClearLastError();
        }
        catch (Exception ex)
        {
            SetLastError(ex);
            throw;
        }

        currentProfile = profile;

        currentAccountInfoStr = profile.emailAddress;

        while (true)
        {
            cancel.ThrowIfCancellationRequested();

            try
            {
                InboxMessageBox box = await ReloadInternalAsync(cancel);

                this.InitialLoading = false;

                ClearLastError();

                MessageBoxUpdatedCallback(box);
            }
            catch (Exception ex)
            {
                SetLastError(ex);
                throw;
            }

            await TaskUtil.WaitObjectsAsync(
                cancels: cancel._SingleArray(),
                timeout: Util.GenRandInterval(CoresConfig.InboxGmailAdapterSettings.ReloadInterval));
        }
    }

    readonly Dictionary<string, GoogleApi.Message> MessageCache = new Dictionary<string, GoogleApi.Message>(StrComparer.IgnoreCaseComparer);

    int numReload = 0;

    int LastUnread = 0;

    public class GmailStatus
    {
        public string? EmailAddress;
        public int NumUnreadMessages;
    }

    async Task<InboxMessageBox> ReloadInternalAsync(CancellationToken cancel)
    {
        GoogleApi.MessageList[] list = await Api!.GmailListMessagesAsync("is:unread label:inbox", this.Inbox.Options.MaxMessagesPerAdapter, cancel);

        List<GoogleApi.Message> msgList = new List<GoogleApi.Message>();

        foreach (GoogleApi.MessageList message in list)
        {
            if (message.id._IsFilled())
            {
                try
                {
                    if (MessageCache.TryGetValue(message.id, out GoogleApi.Message? m) == false)
                    {
                        m = await Api.GmailGetMessageAsync(message.id, cancel);

                        MessageCache[message.id] = m;
                    }

                    m._MarkNotNull();

                    msgList.Add(m);
                }
                catch (Exception ex)
                {
                    ex._Debug();
                }
            }
        }

        // delete old cache
        List<string> deleteList = new List<string>();
        foreach (string id in MessageCache.Keys)
        {
            if (list.Where(x => x.id._IsSamei(id)).Any() == false)
                deleteList.Add(id);
        }
        foreach (string id in deleteList)
        {
            MessageCache.Remove(id);
        }

        InboxMessageBox box = new InboxMessageBox(false);

        List<InboxMessage> msgList2 = new List<InboxMessage>();

        foreach (var msg in msgList.OrderByDescending(x => x.internalDate).Take(this.Inbox.Options.MaxMessagesPerAdapter))
        {
            var m = new InboxMessage
            {
                From = msg.GetFrom()._DecodeHtml(),
                FromImage = Consts.CdnUrls.GmailIcon,
                Group = "",
                Id = this.Guid + "_" + msg.id,
                Service = currentProfile!.emailAddress,
                ServiceImage = Consts.CdnUrls.GmailIcon,
                Subject = msg.GetSubject()._DecodeHtml(),
                Body = msg.snippet._DecodeHtml(),
                Timestamp = msg.internalDate._ToDateTimeOfGoogle(),
            };

            msgList2.Add(m);
        }

        int numUnread = msgList2.Count;
        if (this.LastUnread != numUnread)
        {
            this.LastUnread = numUnread;

            GmailStatus st = new GmailStatus
            {
                EmailAddress = this.currentProfile?.emailAddress,
                NumUnreadMessages = numUnread,
            };

            st._PostData("gmail_status");
        }

        box.MessageList = msgList2.ToArray();

        if (numReload == 0)
        {
            box.IsFirst = true;
        }

        numReload++;

        return box;
    }
}

#endif  // CORES_BASIC_JSON
