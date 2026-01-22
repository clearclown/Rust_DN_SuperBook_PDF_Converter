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
using System.Text;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using System.IO;
using System.Linq;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using IPA.Cores.Basic.HttpClientCore;
using static IPA.Cores.Globals.Basic;

//using System.Net.Http;
//using System.Net.Http.Headers;

#pragma warning disable CS0649

namespace IPA.Cores.ClientApi.SlackApi;

public static class SlackApiHelper
{
    public static DateTimeOffset _ToDateTimeOfSlack(this decimal value) => Util.UnixTimeToDateTime(value)._AsDateTimeOffset(false).ToLocalTime();
    public static DateTimeOffset _ToDateTimeOfSlack(this long value) => Util.UnixTimeToDateTime((uint)value)._AsDateTimeOffset(false).ToLocalTime();

    public static long _ToLongDateTimeOfSlack(this DateTimeOffset dt) => Util.DateTimeToUnixTime(dt.UtcDateTime);
    public static decimal _ToDecimalDateTimeOfSlack(this DateTimeOffset dt) => Util.DateTimeToUnixTimeDecimal(dt.UtcDateTime);

    public static string _SlackExpandBodyUsername(this string src, SlackApi.User[]? users)
    {
        if (users == null) return src;

        ReadOnlySpan<char> span = src.AsSpan();

        StringBuilder sb = new StringBuilder();

        while (true)
        {
            int i = span.IndexOf("<@");
            if (i == -1)
            {
                sb.Append(span);
                break;
            }

            sb.Append(span.Slice(0, i));

            span = span.Slice(i);

            int j = span.IndexOf(">");
            if (j == -1)
            {
                sb.Append(span);
                break;
            }

            string tag = span.Slice(2, j - 2).ToString();

            string? username = users.Where(x => x.id._IsSamei(tag)).FirstOrDefault()?.profile?.real_name;

            if (username._IsFilled())
            {
                tag = " @" + username + " ";
            }
            else
            {
                tag = span.Slice(0, j + 1).ToString();
            }

            sb.Append(tag);

            span = span.Slice(j + 1);
        }

        return sb.ToString();
    }
}

public static class SlackApiSettings
{
    public static readonly Copenhagen<int> SlackApiLimitCounts = 200;
    public static readonly Copenhagen<int> SlackApiLimitCounts_Users = 1000;
}

public class SlackApi : WebApi
{
    public class ResponseMetadata
    {
        public string? next_cursor;
    }

    public abstract class SlackResponseBase : IValidatable
    {
        public bool ok;
        public string? error;
        public ResponseMetadata? response_metadata;

        public void Validate()
        {
            if (ok == false)
                throw new ApplicationException(error._FilledOrDefault("Slack response unknown error"));
        }
    }

    public string ClientId { get; }
    public string ClientSecret { get; }
    public string? AccessTokenStr { get; }

    public SlackApi(string clientId, string clientSecret, string? accessToken = "", bool ignoreSslCert = false) : base(new WebApiOptions(new WebApiSettings { SslAcceptAnyCerts = ignoreSslCert }))
    {
        this.ClientId = clientId;
        this.ClientSecret = clientSecret;
        this.AccessTokenStr = accessToken;
        this.Json_MaxDepth = 128;
    }

    protected override HttpRequestMessage CreateWebRequest(WebMethods method, string url, WebRequestOptions? options, params (string name, string? value)[]? queryList)
    {
        HttpRequestMessage r = base.CreateWebRequest(method, url, options, queryList);

        if (this.AccessTokenStr._IsFilled())
        {
            r.Headers.Add("Authorization", $"Bearer {this.AccessTokenStr._EncodeUrl(this.RequestEncoding)}");
        }

        return r;
    }

    public string AuthGenerateAuthorizeUrl(string scope, string redirectUrl, string state = "")
    {
        return "https://slack.com/oauth/authorize?" +
            BuildQueryString(
                ("client_id", this.ClientId),
                ("scope", scope),
                ("redirect_uri", redirectUrl),
                ("state", state));
    }

    public class AccessToken : SlackResponseBase
    {
        public string? access_token;
        public string? scope;
        public string? user_id;
        public string? team_name;
        public string? team_id;
    }

    public async Task<AccessToken> AuthGetAccessTokenAsync(string code, string redirectUrl, CancellationToken cancel = default)
    {
        WebRet ret = await this.SimpleQueryAsync(WebMethods.POST, "https://slack.com/api/oauth.access", cancel,
            null,
            ("client_id", this.ClientId),
            ("client_secret", this.ClientSecret),
            ("redirect_uri", redirectUrl),
            ("code", code));

        AccessToken a = ret.Deserialize<AccessToken>(true)!;

        return a;
    }

    public class ChannelsList : SlackResponseBase
    {
        public Channel?[]? channels;
    }

    public class Value
    {
        public string? value;
        public string? creator;
        public long? last_set;
    }

    public class ConversationInfo : SlackResponseBase
    {
        public Channel? channel;
    }

    public class Channel
    {
        public string? id;
        public string? name;
        public bool is_channel;
        public bool is_group;
        public bool is_im;
        public decimal created;
        public string? creator;
        public bool is_archived;
        public bool is_member;
        public bool is_private;
        public bool is_mpim;
        public string? name_normalized;
        public Value? purpose;
        public decimal last_read;
        public string? user;

        public bool IsTarget()
        {
            if (this.is_archived) return false;
            if (this.is_channel && this.is_member == false) return false;

            return true;
        }
    }

    public class PostMessageData
    {
        public string? channel;
        public string? text;
        public bool as_user;
    }

    public class RealtimeResponse : SlackResponseBase
    {
        public string? url;
    }

    public class Profile
    {
        public string? image_512;
        public string? real_name;
        public string? status_text;
        public string? status_emoji;
        public string? display_name;
        public string? real_name_normalized;
        public string? display_name_normalized;
        public string? email;
    }

    public class User
    {
        public string? id;
        public string? name;
        public string? real_name;
        public bool is_admin;
        public bool is_owner;
        public bool is_primary_owner;
        public bool is_restricted;
        public bool is_ultra_restricted;
        public bool is_bot;
        public bool is_app_user;
        public long updated;
        public Profile? profile;
    }

    public class UserListResponse : SlackResponseBase
    {
        public User?[]? members;
    }

    public class TeamIcon
    {
        public string? image_132;
    }

    public class Team
    {
        public string? id;
        public string? name;
        public TeamIcon? icon;
    }

    public class TeamResponse : SlackResponseBase
    {
        public Team? team;
    }

    public class File
    {
        public string? name;
    }

    public class Message
    {
        public string? type;
        public string? subtype;
        public string? user;
        public string? text;
        public bool upload;
        public decimal ts;

        public File?[]? files;
    }

    public class HistoryResponse : SlackResponseBase
    {
        public Message?[]? messages;
    }

    public class UserePrefs
    {
        public string? muted_channels;
    }

    public class UserePrefsResponse : SlackResponseBase
    {
        public UserePrefs? prefs;
    }

    public override async Task<WebRet> SimpleQueryAsync(WebMethods method, bool exactMimeType, string url, WebRequestOptions? options, CancellationToken cancel = default, string? postContentType = Consts.MimeTypes.FormUrlEncoded, params (string name, string? value)[]? queryList)
    {
        int num_retry = 0;

        LABEL_RETRY:
        {
            if (postContentType._IsEmpty()) postContentType = Consts.MimeTypes.FormUrlEncoded;
            using HttpRequestMessage r = CreateWebRequest(method, url, options, queryList);

            if (method == WebMethods.POST || method == WebMethods.PUT)
            {
                string qs = BuildQueryString(queryList);

                r.Content = new StringContent(qs, this.RequestEncoding, postContentType);
            }

            using (HttpResponseMessage res = await this.Client.SendAsync(r, HttpCompletionOption.ResponseContentRead, cancel))
            {
                if (res.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    string? retryAfter = res.Headers.GetValues("Retry-After").FirstOrDefault();
                    if (retryAfter._IsFilled())
                    {
                        num_retry++;

                        if (num_retry <= 5)
                        {
                            int interval = retryAfter._ToInt();

                            interval = Math.Min(Math.Max(interval, 1), 300);

                            int intervalMsecs = Util.GenRandInterval(interval * 1500);

                            Con.WriteDebug($"Get TooManyRequests for '{url}'. Waiting for {intervalMsecs._ToString3()} msecs...");

                            await cancel._WaitUntilCanceledAsync(intervalMsecs);

                            await Task.Yield();

                            goto LABEL_RETRY;
                        }
                    }
                }

                await ThrowIfErrorAsync(res);
                byte[] data = await res.Content.ReadAsByteArrayAsync();
                return new WebRet(this, url, res.Content.Headers._TryGetContentType(), data, res.Headers, res.IsSuccessStatusCode, res.StatusCode, res.ReasonPhrase);
            }
        }
    }

    public async Task<string[]> GetMutedChannels(CancellationToken cancel = default)
    {
        WebRet ret = await SimpleQueryAsync(WebMethods.GET, "https://slack.com/api/users.prefs.get", cancel, null);

        var res = ret.Deserialize<UserePrefsResponse>(true);

        return res!.prefs!.muted_channels?.Split(',', StringSplitOptions.RemoveEmptyEntries) ?? new string[0];
    }

    public async Task<WebSocket> RealtimeConnectAsync(CancellationToken cancel = default)
    {
        WebRet ret = await SimpleQueryAsync(WebMethods.POST, "https://slack.com/api/rtm.connect", cancel, null);

        string url = ret.Deserialize<RealtimeResponse>(true)!.url!;

        return await WebSocket.ConnectAsync(url, cancel: cancel, options: new WebSocketConnectOptions(new WebSocketOptions { RespectMessageDelimiter = true }));
    }

    public async Task<Message[]> GetConversationHistoryAsync(string channelId, decimal oldest = 0, int maxCount = int.MaxValue, CancellationToken cancel = default)
    {
        string? nextCursor = null;

        List<Message> o = new List<Message>();

        do
        {
            WebRet ret = await SimpleQueryAsync(WebMethods.POST, "https://slack.com/api/conversations.history", cancel, null, ("limit", SlackApiSettings.SlackApiLimitCounts.ToString()), ("cursor", nextCursor), ("channel", channelId), ("oldest", oldest == 0 ? null : oldest.ToString()));
            //WebRet ret = await SimpleQueryAsync(WebMethods.POST, "https://slack.com/api/conversations.history", cancel, null, ("limit", SlackApiSettings.SlackApiLimitCounts.ToString()), ("cursor", nextCursor), ("channel", channelId));

            HistoryResponse data = ret.Deserialize<HistoryResponse>(true)!;

            //ret.Data._GetString_UTF8()._JsonNormalizeAndDebug();

            foreach (Message? m in data.messages!)
            {
                if (m != null)
                {
                    if ((m.type?._IsSamei("message") ?? false) && ((m.subtype?.StartsWith("channel_", StringComparison.OrdinalIgnoreCase) ?? false) == false))
                    {
                        // Add only message but except channel_join
                        o.Add(m);
                    }
                }
            }

            nextCursor = data.response_metadata?.next_cursor;

            if (o.Count >= maxCount) break;
        }
        while (nextCursor._IsFilled());

        return o.Take(maxCount).ToArray();
    }

    public async Task<Channel> GetConversationInfoAsync(string id, CancellationToken cancel = default)
    {
        WebRet ret = await SimpleQueryAsync(WebMethods.POST, "https://slack.com/api/conversations.info", cancel, null, ("channel", id));

        return ret.Deserialize<ConversationInfo>(true)!.channel!;
    }

    public async Task<Channel[]> GetConversationsListAsync(CancellationToken cancel = default)
    {
        string? nextCursor = null;

        List<Channel> o = new List<Channel>();

        do
        {
            WebRet ret = await SimpleQueryAsync(WebMethods.POST, "https://slack.com/api/conversations.list", cancel, null, ("limit", SlackApiSettings.SlackApiLimitCounts.ToString()), ("cursor", nextCursor), ("types", "public_channel,private_channel,mpim,im"));
            //ret.Data._GetString_UTF8()._JsonNormalizeAndPrint();
            ChannelsList data = ret.Deserialize<ChannelsList>(true)!;

            foreach (Channel? c in data.channels!)
            {
                if (c != null)
                {
                    o.Add(c);
                }
            }

            nextCursor = data.response_metadata?.next_cursor;
        }
        while (nextCursor._IsFilled());

        return o.ToArray();
    }

    public async Task<ChannelsList> GetChannelsListAsync(CancellationToken cancel = default)
    {
        WebRet ret = await SimpleQueryAsync(WebMethods.POST, "https://slack.com/api/channels.list", cancel, queryList: ("unreads", "true"));

        //ret.Data._GetString_UTF8()._JsonNormalizeAndDebug();

        return ret.Deserialize<ChannelsList>(true)!;
    }

    public async Task PostMessageAsync(string channelId, string text, bool asUser, CancellationToken cancel = default)
    {
        PostMessageData m = new PostMessageData()
        {
            channel = channelId,
            text = text,
            as_user = asUser,
        };

        await PostMessageAsync(m, cancel);
    }

    public async Task<Team> GetTeamInfoAsync(CancellationToken cancel = default)
    {
        WebRet ret = await SimpleQueryAsync(WebMethods.POST, "https://slack.com/api/team.info", cancel);

        return ret.Deserialize<TeamResponse>(true)!.team!;
    }

    public async Task<User[]> GetUsersListAsync(CancellationToken cancel = default)
    {
        string? nextCursor = null;

        List<User> o = new List<User>();

        do
        {
            WebRet ret = await SimpleQueryAsync(WebMethods.POST, "https://slack.com/api/users.list", cancel, null, ("limit", SlackApiSettings.SlackApiLimitCounts_Users.ToString()), ("cursor", nextCursor));

            UserListResponse data = ret.Deserialize<UserListResponse>(true)!;

            foreach (User? u in data.members!)
            {
                if (u != null)
                {
                    o.Add(u);
                }
            }

            nextCursor = data.response_metadata?.next_cursor;
        }
        while (nextCursor._IsFilled());

        return o.ToArray();
    }

    public async Task PostMessageAsync(PostMessageData m, CancellationToken cancel = default)
    {
        (await RequestWithJsonObjectAsync(WebMethods.POST, "https://slack.com/api/chat.postMessage", m, cancel)).Deserialize<SlackResponseBase>(true);
    }
}

#endif  // CORES_BASIC_JSON

