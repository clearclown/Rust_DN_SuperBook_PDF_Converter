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

#if CORES_BASIC_WEBAPP || CORES_BASIC_HTTPSERVER

using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Security.Authentication;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;
using System.Linq;


using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using System.Text.Encodings.Web;
using System.Text;
using Microsoft.Net.Http.Headers;

namespace IPA.Cores.Basic;

public static class BasicAuthHelper
{
    public static IServiceCollection AddBasicAuth(this IServiceCollection services, Action<BasicAuthSettings> configureOptions)
    {
        if (services == null) throw new ArgumentNullException(nameof(services));
        if (configureOptions == null) throw new ArgumentNullException(nameof(configureOptions));

        services.Configure(configureOptions);

        return services;
    }

    public static IApplicationBuilder UseBasicAuth(this IApplicationBuilder app)
    {
        if (app == null) throw new ArgumentNullException(nameof(app));

        app.UseMiddleware<BasicAuthMiddleware>();

        return app;
    }
}

public class BasicAuthSettings
{
    public Func<string, string, Task<bool>>? PasswordValidatorAsync = (user, pass) => Task.FromResult(false);
    public string Realm = "Basic Authentication";
}

public class BasicAuthMiddleware
{
    public static readonly string BasicAuthResultItemName = Str.NewGuid();

    readonly RequestDelegate Next;
    public BasicAuthSettings Settings { get; }

    public BasicAuthMiddleware(RequestDelegate next, IOptions<BasicAuthSettings> options)
    {
        Next = next ?? throw new ArgumentNullException(nameof(next));

        this.Settings = options.Value;
    }

    public async Task Invoke(HttpContext context)
    {
        ResultAndError<string> result = await BasicAuthImpl.TryAuthenticateAsync(context.Request, Settings.PasswordValidatorAsync);

        context.Items[BasicAuthResultItemName] = result;

        if (result == false)
        {
            // 認証失敗
            string header = $"Basic realm=\"{Settings.Realm._FilledOrDefault("Auth")}\'";

            context.Response.StatusCode = Consts.HttpStatusCodes.Unauthorized;
            context.Response.Headers.Append(HeaderNames.WWWAuthenticate, header);
        }
        else
        {
            // 認証成功
            await Next(context);
        }
    }
}

// BASIC 認証の細かい実装 (static クラス)
public static class BasicAuthImpl
{
    public static Tuple<string, string>? ParseBasicAuthenticationHeader(string authorizationHeaderValue)
    {
        if (authorizationHeaderValue._IsFilled())
        {
            if (Str.GetKeyAndValue(authorizationHeaderValue, out string scheme, out string base64, " \t"))
            {
                if (scheme._IsSamei("Basic"))
                {
                    if (base64._IsFilled())
                    {
                        base64 = base64._NonNullTrim();

                        // Base64 デコード
                        string usernameAndPassword = base64._Base64Decode()._GetString_UTF8();

                        if (Str.GetKeyAndValue(usernameAndPassword, out string username, out string password, ":"))
                        {
                            return new Tuple<string, string>(username, password);
                        }
                    }
                }
            }
        }

        return null;
    }

    public static async Task SendAuthenticateHeaderAsync(HttpResponse response, string realm, CancellationToken cancel = default)
    {
        KeyValueList<string, string> basicAuthResponseHeaders = new KeyValueList<string, string>();
        basicAuthResponseHeaders.Add(Consts.HttpHeaders.WWWAuthenticate, $"Basic realm=\"{realm}\"");

        await using var basicAuthRequireResult = new HttpStringResult("Basic Auth Required", contentType: Consts.MimeTypes.TextUtf8, statusCode: Consts.HttpStatusCodes.Unauthorized, additionalHeaders: basicAuthResponseHeaders);

        await response._SendHttpResultAsync(basicAuthRequireResult, cancel: cancel);
    }

    public static async Task<ResultAndError<string>> TryAuthenticateAsync(HttpRequest request, Func<string, string, Task<bool>>? passwordAuthCallback, CancellationToken cancel = default)
    {
        cancel.ThrowIfCancellationRequested();

        string header = request.Headers._GetStrFirst("Authorization");

        var usernameAndPassword = ParseBasicAuthenticationHeader(header);

        if (usernameAndPassword == null)
        {
            return false;
        }

        // ユーザー認証を実施
        bool ok = false;

        if (passwordAuthCallback != null)
        {
            ok = await passwordAuthCallback(usernameAndPassword.Item1, usernameAndPassword.Item2);
        }

        var log = new
        {
            BasicAuthUserName = usernameAndPassword.Item1,
            BasicAuthPassword = usernameAndPassword.Item2._MaskPassword(),
            ClientIpAddress = request.HttpContext.Connection.RemoteIpAddress._UnmapIPv4(),
            ClientPort = request.HttpContext.Connection.RemotePort,
            ServerIpAddresss = request.HttpContext.Connection.LocalIpAddress._UnmapIPv4(),
            ServerPort = request.HttpContext.Connection.LocalPort,
            AuthResult = ok,
        };

        log._PostAccessLog("TryAuthenticateAsync");

        return new ResultAndError<string>(usernameAndPassword.Item1, ok);
    }
}

#endif  // CORES_BASIC_WEBAPP

