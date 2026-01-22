// IPA Cores.NET
// 
// Copyright (c) 2018-2019 IPA CyberLab.
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
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

using IPA.Cores.Web;
using IPA.Cores.Helper.Web;
using static IPA.Cores.Globals.Web;
using Castle.DynamicProxy.Generators;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Http.Extensions;
using System.Net;
using System.Security.Cryptography.X509Certificates;

namespace IPA.Cores.Basic
{
    public static partial class CoresConfig
    {
        public static partial class AspNetHelperOptions
        {
            public static readonly Copenhagen<string> CookieEasyEncryptPassword = Consts.Strings.EasyEncryptDefaultPassword;
        }
    }
}

namespace IPA.Cores.Helper.Web
{
    public class AspNetCookieOptions : INormalizable
    {
        public string Domain { get; set; } = "";
        public bool NoJavaScript { get; set; } = false;
        public int Days { get; set; } = Consts.Numbers.MaxCookieDays;
        public string Path { get; set; } = "/";

        public void Normalize()
        {
            if (Days < 0) Days = 0;
            if (Days > Consts.Numbers.MaxCookieDays) Days = Consts.Numbers.MaxCookieDays;

            if (Path._IsEmpty()) Path = "/";
            Domain = Domain._NonNullTrim();
        }

        public AspNetCookieOptions(int days = Consts.Numbers.MaxCookieDays, bool noJs = false, string path = "/", string domain = "")
        {
            this.Days = days;
            this.NoJavaScript = noJs;
            this.Path = path;
            this.Domain = domain;

            this.Normalize();
        }

        public CookieOptions ToCookieOptions(bool httpsOnly)
        {
            this.Normalize();

            CookieOptions ret = new CookieOptions
            {
                Domain = this.Domain._IsEmpty() ? null : this.Domain,
                Expires = this.Days <= 0 ? (DateTimeOffset?)null : DateTimeOffset.Now.AddDays(this.Days),
                HttpOnly = this.NoJavaScript,
                Secure = httpsOnly,
                Path = this.Path,
                SameSite = SameSiteMode.Lax,
            };

            return ret;
        }
    }

    public static partial class AspNetExtensions
    {
        public static string GenerateAbsoluteUrl(this ControllerBase c, string actionName)
        {
            string url = Str.BuildHttpUrl(c.Request.Scheme, c.Request.Host.Host, c.Request.Host.Port ?? 0, c.Url.Action(actionName)._NonNull());

            return url;
        }

        // JSON.NET を用いた JSON 応答の生成
        public static IActionResult _AspNetJsonResult(this object obj, bool includeNull = false, bool escapeHtml = false, int? maxDepth = Json.DefaultMaxDepth, bool compact = false, bool referenceHandling = false, bool base64url = false, Type? type = null)
        {
            string jsonStr = obj._ObjectToJson(includeNull, escapeHtml, maxDepth, compact, referenceHandling, base64url, type);

            return new ContentResult()
            {
                Content = jsonStr,
                ContentType = Consts.MimeTypes.Json,
                StatusCode = 200,
            };
        }

        public static IMvcBuilder ConfigureMvcWithAspNetLib(this IMvcBuilder mvc, AspNetLib lib)
            => lib.ConfigureAspNetLibMvc(mvc);

        public static HttpActionResult GetHttpActionResult(this HttpResult h)
            => new HttpActionResult(h);

        public static TextActionResult _AspNetTextActionResult(this string str, string contentType = Consts.MimeTypes.TextUtf8, int statusCode = Consts.HttpStatusCodes.Ok, Encoding? encoding = null, IReadOnlyList<KeyValuePair<string, string>>? additionalHeaders = null)
            => new TextActionResult(str, contentType, statusCode, encoding, additionalHeaders);

        public static string GetUrl(this HttpRequest req) => req.GetEncodedUrl();
        public static string GetUrl(this HttpContext c) => c.Request.GetUrl();

        // エラーハンドラページで利用できる、最後のエラーの取得
        public static Exception _GetLastError(this Controller controller)
        {
            try
            {
                var errorContext = controller.HttpContext.Features.Get<IExceptionHandlerPathFeature>();

                return errorContext!.Error;
            }
            catch
            {
                throw new CoresLibException("Unknown exception: Failed to Get IExceptionHandlerPathFeature.");
            }
        }

        // エラーハンドラページで利用できる、最後のエラーパスの取得
        public static string _GetLastErrorPath(this Controller controller)
        {
            try
            {
                var errorContext = controller.HttpContext.Features.Get<IExceptionHandlerPathFeature>();

                return errorContext!.Path;
            }
            catch
            {
                throw new CoresLibException("Unknown exception: Failed to Get IExceptionHandlerPathFeature.");
            }
        }

        // HTML 関係
        public static IHtmlContent Spacing<TModel>(this IHtmlHelper<TModel> helper, int num = 1)
            => helper.Raw(Str.HtmlSpacing._MakeStrArray(num));

        // タグ関係
        public static string _BoolToChecked(this bool b) => b ? " checked" : "";
        public static string _BoolToDisabled(this bool b) => b ? " disabled" : "";

        // IP アドレス関係
        public static IPAddress _GetLocalIp(this HttpContext ctx) => ctx.Connection.LocalIpAddress!._UnmapIPv4();
        public static Task<string> _GetLocalHostnameAsync(this HttpContext ctx, CancellationToken cancel = default) => ctx._GetLocalIp()._GetHostnameFromIpAsync(cancel);
        public static string _GetLocalHostname(this HttpContext ctx, CancellationToken cancel = default) => _GetLocalHostnameAsync(ctx)._GetResult();
        public static int _GetLocalPort(this HttpContext ctx) => ctx.Connection.LocalPort;
        public static bool _IsHttps(this HttpContext ctx) => ctx.Request.IsHttps;
        public static Task<X509Certificate2?> _GetClientCertificatAsync(this HttpContext ctx, CancellationToken cancel = default) => ctx.Connection.GetClientCertificateAsync(cancel);

        public static IPAddress _GetRemoteIp(this HttpContext ctx) => ctx.Connection.RemoteIpAddress!._UnmapIPv4();
        public static Task<string> _GetRemoteHostnameAsync(this HttpContext ctx, CancellationToken cancel = default) => ctx._GetRemoteIp()._GetHostnameFromIpAsync(cancel);
        public static string _GetRemoteHostname(this HttpContext ctx, CancellationToken cancel = default) => _GetRemoteHostnameAsync(ctx)._GetResult();
        public static int _GetRemotePort(this HttpContext ctx) => ctx.Connection.RemotePort;


        public static IPAddress _GetLocalIp(this Controller controller) => controller.HttpContext._GetLocalIp();
        public static Task<string> _GetLocalHostnameAsync(this Controller controller, CancellationToken cancel = default) => controller._GetLocalIp()._GetHostnameFromIpAsync(cancel);
        public static string _GetLocalHostname(this Controller controller, CancellationToken cancel = default) => _GetLocalHostnameAsync(controller)._GetResult();
        public static int _GetLocalPort(this Controller controller) => controller.HttpContext._GetLocalPort();
        public static bool _IsHttps(this Controller controller) => controller.HttpContext.Request.IsHttps;
        public static Task<X509Certificate2?> _GetClientCertificatAsync(this Controller controller, CancellationToken cancel = default) => controller.HttpContext.Connection.GetClientCertificateAsync(cancel);

        public static IPAddress _GetRemoteIp(this Controller controller) => controller.HttpContext._GetRemoteIp();
        public static Task<string> _GetRemoteHostnameAsync(this Controller controller, CancellationToken cancel = default) => controller._GetRemoteIp()._GetHostnameFromIpAsync(cancel);
        public static string _GetRemoteHostname(this Controller controller, CancellationToken cancel = default) => _GetRemoteHostnameAsync(controller)._GetResult();
        public static int _GetRemotePort(this Controller controller) => controller.HttpContext._GetRemotePort();


        // --- Cookie 関係 ---

        public static void _EasySaveCookie<T>(this HttpResponse response, string cookieName, T value, AspNetCookieOptions? options = null, bool easyEncrypt = false, string? easyEncryptPassword = null)
        {
            bool isHttps = response.HttpContext._IsHttps();

            if (options == null) options = new AspNetCookieOptions();

            cookieName = (isHttps ? Consts.Strings.EasyCookieNamePrefix_Https : Consts.Strings.EasyCookieNamePrefix_Http) + cookieName;

            if (easyEncryptPassword._IsNullOrZeroLen())
            {
                easyEncryptPassword = CoresConfig.AspNetHelperOptions.CookieEasyEncryptPassword;
            }

            string valueStr = EasyCookieUtil.SerializeObject(value, easyEncrypt, easyEncryptPassword);

            if (valueStr._IsEmpty())
            {
                response.Cookies.Delete(cookieName);
            }
            else
            {
                response.Cookies.Append(cookieName, valueStr, options.ToCookieOptions(isHttps));
            }
        }

        public static void _EasyDeleteCookie(this HttpResponse response, string cookieName)
        {
            bool isHttps = response.HttpContext._IsHttps();

            cookieName = (isHttps ? Consts.Strings.EasyCookieNamePrefix_Https : Consts.Strings.EasyCookieNamePrefix_Http) + cookieName;

            response.Cookies.Delete(cookieName);
        }

        [return: MaybeNull]
        public static T _EasyLoadCookie<T>(this HttpRequest request, string cookieName, bool easyDecrypt = false, string? easyDecryptPassword = null)
        {
            bool isHttps = request.HttpContext._IsHttps();

            cookieName = (isHttps ? Consts.Strings.EasyCookieNamePrefix_Https : Consts.Strings.EasyCookieNamePrefix_Http) + cookieName;

            string? valueStr = request.Cookies[cookieName];

            if (easyDecryptPassword._IsNullOrZeroLen())
            {
                easyDecryptPassword = CoresConfig.AspNetHelperOptions.CookieEasyEncryptPassword;
            }

            return EasyCookieUtil.DeserializeObject<T>(valueStr, easyDecrypt, easyDecryptPassword);
        }

        public static void _EasySaveCookie<T>(this HttpContext context, string cookieName, T value, AspNetCookieOptions? options = null, bool easyEncrypt = false, string? easyEncryptPassword = null)
            => context.Response._EasySaveCookie(cookieName, value, options, easyEncrypt, easyEncryptPassword);

        public static void _EasyDeleteCookie(this HttpContext context, string cookieName)
            => context.Response._EasyDeleteCookie(cookieName);

        [return: MaybeNull]
        public static T _EasyLoadCookie<T>(this HttpContext context, string cookieName, bool easyDecrypt = false, string? easyDecryptPassword = null)
            => context.Request._EasyLoadCookie<T>(cookieName, easyDecrypt, easyDecryptPassword);

        public static void _EasySaveCookie<T>(this Controller controller, string cookieName, T value, AspNetCookieOptions? options = null, bool easyDecrypt = false, string? easyDecryptPassword = null)
            => controller.HttpContext._EasySaveCookie(cookieName, value, options, easyDecrypt, easyDecryptPassword);

        public static void _EasyDeleteCookie(this Controller controller, string cookieName)
            => controller.HttpContext._EasyDeleteCookie(cookieName);

        [return: MaybeNull]
        public static T _EasyLoadCookie<T>(this Controller controller, string cookieName, bool easyDecrypt = false, string? easyDecryptPassword = null)
            => controller.HttpContext._EasyLoadCookie<T>(cookieName, easyDecrypt, easyDecryptPassword);

        public static bool _IsPostBack(this Controller controller)
        {
            var method = controller.Request.Method._ParseEnum<WebMethods>(WebMethods.GET, false, false);

            return (method == WebMethods.POST);
        }
    }
}

namespace IPA.Cores.Web
{
    // 単純なテキストを返すクラス
    public class TextActionResult : HttpActionResult
    {
        public TextActionResult(string str, string contentType = Consts.MimeTypes.TextUtf8, int statusCode = Consts.HttpStatusCodes.Ok, Encoding? encoding = null, IReadOnlyList<KeyValuePair<string, string>>? additionalHeaders = null)
            : base(new HttpStringResult(str, contentType, statusCode, encoding, additionalHeaders))
        {
        }
    }

    // HttpResult を元にして ASP.NET MVC の IActionResult インスタンスを生成するクラス
    public class HttpActionResult : IActionResult
    {
        public HttpResult HttpResult { get; }

        public HttpActionResult(HttpResult httpResult)
        {
            if (httpResult == null) throw new ArgumentNullException(nameof(httpResult));

            this.HttpResult = httpResult;
        }

        public async Task ExecuteResultAsync(ActionContext context)
        {
            await using (this.HttpResult)
            {
                await context.HttpContext.Response._SendHttpResultAsync(this.HttpResult, context.HttpContext._GetRequestCancellationToken());
            }
        }
    }
}

namespace IPA.Cores.Globals
{
    public static partial class Web
    {
    }
}

