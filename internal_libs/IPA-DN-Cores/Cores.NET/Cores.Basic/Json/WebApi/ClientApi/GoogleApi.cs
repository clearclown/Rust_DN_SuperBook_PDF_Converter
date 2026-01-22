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


#pragma warning disable CS0649

namespace IPA.Cores.Basic
{
    public static partial class CoresConfig
    {
        public static partial class GoogleApiSettings
        {
            public static readonly Copenhagen<int> TokenRefreshInterval = 300 * 1000;
        }
    }
}

namespace IPA.Cores.ClientApi.GoogleApi
{
    public static class GoogleApiHelper
    {
        public static DateTimeOffset _ToDateTimeOfGoogle(this decimal value) => Util.UnixTimeToDateTime(value)._AsDateTimeOffset(false).ToLocalTime();
        public static DateTimeOffset _ToDateTimeOfGoogle(this long value) => Util.UnixTimeToDateTime(value / (decimal)1000.0)._AsDateTimeOffset(false).ToLocalTime();

        public static long _ToLongDateTimeOfGoogle(this DateTimeOffset dt) => (long)(Util.DateTimeToUnixTimeDecimal(dt.UtcDateTime) * (decimal)1000.0);
        public static decimal _ToDecimalDateTimeOfGoogle(this DateTimeOffset dt) => Util.DateTimeToUnixTimeDecimal(dt.UtcDateTime);
    }

    public class GoogleApi : WebApi
    {
        public string ClientId { get; }
        public string ClientSecret { get; }
        public string? RefreshTokenStr { get; }
        string? AccessTokenStr;

        public GoogleApi(string clientId, string clientSecret, string? refreshToken = "") : base()
        {
            this.ClientId = clientId;
            this.ClientSecret = clientSecret;
            this.RefreshTokenStr = refreshToken;
            this.Json_MaxDepth = 128;
        }

        protected override HttpRequestMessage CreateWebRequest(WebMethods method, string url, WebRequestOptions? options, params (string name, string? value)[]? queryList)
        {
            HttpRequestMessage r = base.CreateWebRequest(method, url, options, queryList);

            if (this.AccessTokenStr._IsFilled())
            {
                r.Headers.Add("Authorization", $"Bearer {this.AccessTokenStr._EncodeUrl(this.RequestEncoding)}");
            }

            //Con.WriteLine($"token = {this.AccessTokenStr}");

            return r;
        }

        public string AuthGenerateAuthorizeUrl(string scope, string redirectUrl, string state = "")
        {
            return "https://accounts.google.com/o/oauth2/auth?" +
                BuildQueryString(
                    ("client_id", this.ClientId),
                    ("scope", scope),
                    ("redirect_uri", redirectUrl),
                    ("state", state),
                    ("access_type", "offline"),
                    ("response_type", "code"));
        }

        public class AccessToken
        {
            public string? access_token;
            public string? token_type;
            public string? refresh_token;
            public int expires_in;
        }

        public class MessageList
        {
            public string? id;
            public string? threadId;
        }

        public class MessageListResponse
        {
            public MessageList?[]? messages;

            public string? nextPageToken;
            public long resultSizeEstimate;
        }

        public class Header
        {
            public string? name;
            public string? value;
        }

        public class Payload
        {
            public Header?[]? headers;
        }

        public class Message
        {
            public string? id;
            public string? threadId;
            public Payload? payload;
            public string? snippet;
            public long internalDate;

            public string GetFrom() => this.payload?.headers!.Where(x => x != null && x.name._IsSamei("from")).Select(x => x!.value).FirstOrDefault() ?? "";
            public string GetSubject() => this.payload?.headers!.Where(x => x != null && x.name._IsSamei("subject")).Select(x => x!.value).FirstOrDefault() ?? "";
        }

        public class GmailProfile
        {
            public string? emailAddress;
            public int messagesTotal;
            public int threadsTotal;
            public ulong historyId;
        }

        public async Task<AccessToken> AuthGetAccessTokenAsync(string code, string redirectUrl, CancellationToken cancel = default)
        {
            WebRet ret = await this.SimpleQueryAsync(WebMethods.POST, "https://accounts.google.com/o/oauth2/token", cancel,
                null,
                ("client_id", this.ClientId),
                ("client_secret", this.ClientSecret),
                ("code", code),
                ("redirect_uri", redirectUrl),
                ("grant_type", "authorization_code"));

            //ret.NormalizedJsonStr._Debug();

            AccessToken a = ret.Deserialize<AccessToken>(true)!;

            if (a.access_token._IsEmpty()) throw new ApplicationException("access_token is empty.");
            if (a.refresh_token._IsEmpty()) throw new ApplicationException("refresh_token is empty.");

            return a;
        }

        async Task<AccessToken> RefreshAccessTokenAsync(CancellationToken cancel = default)
        {
            WebRet ret = await this.SimpleQueryAsync(WebMethods.POST, "https://accounts.google.com/o/oauth2/token", cancel,
                null,
                ("client_id", this.ClientId),
                ("client_secret", this.ClientSecret),
                ("refresh_token", this.RefreshTokenStr),
                ("grant_type", "refresh_token"));

            AccessToken a = ret.Deserialize<AccessToken>(true)!;

            return a;
        }

        long nextRefrestTokenTick = 0;

        async Task RefreshAccessTokenIfNecessaryAsync(CancellationToken cancel = default)
        {
            long now = Tick64.Now;

            if (this.AccessTokenStr._IsFilled() && now < nextRefrestTokenTick)
            {
                return;
            }

            AccessToken newToken = await RefreshAccessTokenAsync(cancel);

            if (newToken.access_token._IsFilled())
            {
                this.AccessTokenStr = newToken.access_token;

                nextRefrestTokenTick = now + CoresConfig.GoogleApiSettings.TokenRefreshInterval;
            }
            else
            {
                throw new ApplicationException("newToken.access_token is empty.");
            }
        }

        public async Task<MessageList[]> GmailListMessagesAsync(string? query = null, int maxCount = int.MaxValue, CancellationToken cancel = default)
        {
            List<MessageList> o = new List<MessageList>();

            string? nextPage = null;

            do
            {
                await RefreshAccessTokenIfNecessaryAsync(cancel);

                WebRet ret = await SimpleQueryAsync(WebMethods.GET, "https://www.googleapis.com/gmail/v1/users/me/messages/", cancel, null, ("q", query), ("pageToken", nextPage));

                //ret.Data._GetString_UTF8()._JsonNormalizeAndPrint();

                MessageListResponse response = ret.Deserialize<MessageListResponse>()!;

                if (response.messages != null)
                {
                    foreach (var msg in response.messages)
                    {
                        if (msg != null)
                        {
                            o.Add(msg);
                        }
                    }
                }

                if (o.Count >= maxCount) break;

                if (response.nextPageToken._IsFilled())
                {
                    nextPage = response.nextPageToken;
                }
            }
            while (nextPage._IsFilled());

            return o.Take(maxCount).ToArray();
        }

        public async Task<Message> GmailGetMessageAsync(string id, CancellationToken cancel = default)
        {
            await RefreshAccessTokenIfNecessaryAsync(cancel);

            WebRet ret = await SimpleQueryAsync(WebMethods.GET, $"https://www.googleapis.com/gmail/v1/users/me/messages/{id._EncodeUrl()}", cancel);

            Message msg = ret.Deserialize<Message>()!;

            return msg;
        }

        public async Task<GmailProfile> GmailGetProfileAsync(CancellationToken cancel = default)
        {
            await RefreshAccessTokenIfNecessaryAsync(cancel);

            WebRet ret = await SimpleQueryAsync(WebMethods.GET, "https://www.googleapis.com/gmail/v1/users/me/profile", cancel);

            GmailProfile profile = ret.Deserialize<GmailProfile>()!;

            return profile;
        }
    }

    public class GoogleMapsApiSettings
    {
        public string Language { get; }
        public string ApiKey { get; }

        public GoogleMapsApiSettings(string apiKey, string language = "ja")
        {
            ApiKey = apiKey;
            Language = language;
        }
    }

    public enum GoogleMapsMethod
    {
        driving,
        walking,
        transit,
    }

    public enum GoogleMapsTrafficModel
    {
        best_guess,
        optimistic,
        pessimistic,
    }

    public abstract class GoogleMapsResultBase
    {
        public bool IsOk { get; set; } = true;
        public bool IsError => !IsOk;
        public string ErrorString { get; set; } = "";

        public void SetError(string errorString)
        {
            this.IsOk = false;
            this.ErrorString = errorString;
        }

        public void ClearError()
        {
            this.IsOk = true;
            this.ErrorString = "";
        }
    }

    public class GoogleMapsDurationResult : GoogleMapsResultBase
    {
        public string? RouteSummary;
        public double DistanceKm;
        public TimeSpan Duration;
        public string? EndAddress;
        public string? StartAddress;
    }

    public class GoogleMapsApi : WebApi
    {
        public GoogleMapsApiSettings Settings { get; }

        public GoogleMapsApi(GoogleMapsApiSettings settings)
        {
            this.Settings = settings;
        }

        public async Task<GoogleMapsDurationResult> CalcDurationAsync(string start, string end,
            DateTimeOffset departureTime = default,
            GoogleMapsMethod method = GoogleMapsMethod.driving,
            GoogleMapsTrafficModel trafficModel = GoogleMapsTrafficModel.best_guess, CancellationToken cancel = default)
        {
            if (departureTime == default) departureTime = DateTimeOffset.Now;

            WebRet ret = await this.SimpleQueryAsync(WebMethods.GET, "https://maps.googleapis.com/maps/api/directions/json", cancel, null,
                ("key", this.Settings.ApiKey),
                ("language", this.Settings.Language),
                ("origin", start),
                ("destination", end),
                ("mode", method.ToString()),
                ("departure_time", departureTime.ToUnixTimeSeconds().ToString()),
                ("traffic_model", trafficModel.ToString()));

            dynamic d = ret.JsonDynamic!;

            //ret.Data._GetString_UTF8()._Print();

            GoogleMapsDurationResult result = new GoogleMapsDurationResult();

            if (d.status != "OK")
            {
                result.SetError((string)d.status);
            }
            else
            {
                try
                {
                    result.DistanceKm = (double)d.routes[0].legs[0].distance.value / 1000.0;

                    if (d.routes[0].legs[0].duration_in_traffic != null)
                    {
                        result.Duration = new TimeSpan(0, 0, (int)d.routes[0].legs[0].duration_in_traffic.value);
                    }
                    else
                    {
                        result.Duration = new TimeSpan(0, 0, (int)d.routes[0].legs[0].duration.value);
                    }

                    result.RouteSummary = d.routes[0].summary;

                    result.StartAddress = d.routes[0].legs[0].start_address;
                    result.EndAddress = d.routes[0].legs[0].end_address;
                }
                catch
                {
                    ret.Data._GetString_UTF8()._Debug();
                    throw;
                }
            }

            return result!;
        }
    }
}

#endif  // CORES_BASIC_JSON
