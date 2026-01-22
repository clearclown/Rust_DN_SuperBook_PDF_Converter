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
using System.IO.Enumeration;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

using IPA.Cores.ClientApi.SlackApi;
using IPA.Cores.ClientApi.GoogleApi;

#pragma warning disable CS0162
#pragma warning disable CS0219

namespace IPA.TestDev;

partial class TestDevCommands
{
    [ConsoleCommand(
    "Inbox command",
    "Inbox",
    "Inbox test")]
    static int Inbox(ConsoleService c, string cmdName, string str)
    {
        ConsoleParam[] args = { };
        ConsoleParamValueList vl = c.ParseCommandList(cmdName, str, args);

        //Inbox_SlackTestAsync()._GetResult();

        //Inbox_SlackAdapterPerAppTestAsync()._GetResult();

        Inbox_SlackAdapterPerUserTestAsync()._GetResult();

        //Inbox_GoogleApiSimpleTestAsync()._GetResult();

        //Inbox_GeneticAdapterTestAsync()._GetResult();

        //Inbox_GeneticAdapterTestAsync()._GetResult();

        return 0;
    }

    static async Task Inbox_GoogleApiSimpleTestAsync()
    {
        string appSecret = "___________";
        if (false)
        {
            using (GoogleApi api = new GoogleApi("651284401399-d2bth85kk6rks1no1dllb3k0d6mrornt.apps.googleusercontent.com", appSecret))
            {
                if (false)
                {
                    string url = api.AuthGenerateAuthorizeUrl(Consts.OAuthScopes.Google_Gmail, "https://www.google.com/", "123");

                    url._Print();
                }
                else
                {
                    string code = "___________";

                    var x = await api.AuthGetAccessTokenAsync(code, "https://www.google.com/");

                    x._DebugAsJson();
                }
            }
        }
        else
        {
            string token = "___________";

            using (GoogleApi api = new GoogleApi("651284401399-d2bth85kk6rks1no1dllb3k0d6mrornt.apps.googleusercontent.com", appSecret, token))
            {
                GoogleApi.MessageList[] list = await api.GmailListMessagesAsync("is:unread label:inbox", maxCount: 100);

                foreach (var msg in list)
                {
                    if (msg.id._IsFilled())
                    {
                        var m = await api.GmailGetMessageAsync(msg.id);

                        //m._DebugAsJson();

                        Con.WriteLine("----------------------------");

                        Con.WriteLine($"date: " + m.internalDate._ToDateTimeOfGoogle());
                        Con.WriteLine($"subject: " + m.GetSubject());
                        Con.WriteLine($"from: " + m.GetFrom());
                        Con.WriteLine($"body: " + m.snippet);
                    }
                }
            }
        }
    }

    static async Task Inbox_GeneticAdapterTestAsync()
    {
        using (Inbox inbox = new Inbox())
        {
            InboxAdapter gmailAdapter = inbox.AddAdapter(Str.NewGuid(), Consts.InboxProviderNames.Gmail, new InboxAdapterAppCredential
            {
                ClientId = "651284401399-d2bth85kk6rks1no1dllb3k0d6mrornt.apps.googleusercontent.com",
                ClientSecret = "_________________"
            });

            inbox.StartAdapter(gmailAdapter.Guid, new InboxAdapterUserCredential { AccessToken = "_________________" });

            InboxAdapter slackAdapter = inbox.AddAdapter(Str.NewGuid(), Consts.InboxProviderNames.Slack_App, new InboxAdapterAppCredential
            {
                ClientId = "687264585408.675851234162",
                ClientSecret = "_________________"
            });

            inbox.StartAdapter(slackAdapter.Guid, new InboxAdapterUserCredential { AccessToken = "_________________" });

            inbox.StateChangeEventListener.RegisterCallback((caller, type, state, eventArg) =>
            {
                var box = inbox.GetMessageBox();

                box.MessageList = box.MessageList.OrderBy(x => x.Timestamp).ToArray();

                box._PrintAsJson();
            });

            Con.ReadLine();
        }

        await Task.CompletedTask;
    }

    static async Task Inbox_GmailAdapterTestAsync()
    {
        using (Inbox inbox = new Inbox())
        {
            InboxAdapter gmailAdapter = inbox.AddAdapter(Str.NewGuid(), Consts.InboxProviderNames.Gmail, new InboxAdapterAppCredential
            {
                ClientId = "651284401399-d2bth85kk6rks1no1dllb3k0d6mrornt.apps.googleusercontent.com",
                ClientSecret = "_________________"
            });

            inbox.StartAdapter(gmailAdapter.Guid, new InboxAdapterUserCredential { AccessToken = "_________________" });

            inbox.StateChangeEventListener.RegisterCallback((caller, type, state, eventArg) =>
            {
                var box = inbox.GetMessageBox();

                box._PrintAsJson();
            });

            Con.ReadLine();
        }

        await Task.CompletedTask;
    }

    static async Task Inbox_SlackAdapterPerUserTestAsync()
    {
        using (Inbox inbox = new Inbox())
        {
            InboxAdapter slackAdapter = inbox.AddAdapter(Str.NewGuid(), Consts.InboxProviderNames.Slack_User, new InboxAdapterAppCredential
            {
                ClientId = "",
                ClientSecret = "____"
            });

            inbox.StartAdapter(slackAdapter.Guid, new InboxAdapterUserCredential { AccessToken = "" });

            inbox.StateChangeEventListener.RegisterCallback((caller, type, state, eventArg) =>
            {
                var box = inbox.GetMessageBox();

                box._PrintAsJson();
            });

            Con.ReadLine();
        }

        await Task.CompletedTask;
    }


    static async Task Inbox_SlackAdapterPerAppTestAsync()
    {
        using (Inbox inbox = new Inbox())
        {
            InboxAdapter slackAdapter = inbox.AddAdapter(Str.NewGuid(), Consts.InboxProviderNames.Slack_App, new InboxAdapterAppCredential
            {
                ClientId = "687264585408.675851234162",
                ClientSecret = "_________________"
            });

            inbox.StartAdapter(slackAdapter.Guid, new InboxAdapterUserCredential { AccessToken = "_________________" });

            inbox.StateChangeEventListener.RegisterCallback((caller, type, state, eventArg) =>
            {
                var box = inbox.GetMessageBox();

                box._PrintAsJson();
            });

            Con.ReadLine();
        }

        await Task.CompletedTask;
    }

    static async Task Inbox_SlackTestAsync()
    {
        string accessToken = "_________________";

        if (false)
        {
            using (SlackApi slack = new SlackApi("687264585408.675851234162", "_________________"))
            {
                if (false)
                {
                    //string scope = "channels:read groups:read im:read mpim:read channels:history groups:history im:history mpim:history users:read users.profile:read";
                    string scope = Consts.OAuthScopes.Slack_Client;
                    string url = slack.AuthGenerateAuthorizeUrl(Consts.OAuthScopes.Slack_Client, "https://www.google.com/");

                    url._Print();

                }
                else
                {
                    var token = await slack.AuthGetAccessTokenAsync("_________________", null);

                    token._PrintAsJson();
                }
            }
        }
        else
        {
            using (SlackApi slack = new SlackApi("687264585408.675851234162", "_________________", accessToken))
            {
                //var channels = await slack.GetChannelsListAsync();

                //await slack.GetConversationsListAsync();

                using (WebSocket ws = await slack.RealtimeConnectAsync())
                {
                    using (var st = ws.GetStream())
                    {
                        while (true)
                        {
                            IReadOnlyList<ReadOnlyMemory<byte>> segments = await st.FastReceiveAsync();

                            foreach (ReadOnlyMemory<byte> mem in segments)
                            {
                                string str = mem._GetString_Ascii();

                                str._Print();
                            }
                        }
                    }
                }
            }
        }
    }
}
