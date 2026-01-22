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
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

using IPA.Cores.Web;
using IPA.Cores.Helper.Web;
using static IPA.Cores.Globals.Web;

namespace IPA.Cores.Web;

public static class EasyCookieAuth
{
    public static readonly Copenhagen<string> LoginPath = "/EasyCookieAuth/";
    public static readonly Copenhagen<string> CookieNameBase = "authcookie1";
    public static readonly Copenhagen<CookieSecurePolicy> CookiePolicy = CookieSecurePolicy.SameAsRequest;
    public static readonly Copenhagen<TimeSpan> CookieLifetime = TimeSpan.FromDays(365 * 2);

    public static readonly Copenhagen<string> LoginFormMessage = "Please log in.";

    public static Func<string, string, Task<bool>>? AuthenticationPasswordValidator = null;

    public static void ConfigureServices(IServiceCollection services, bool allowHttp)
    {
        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(opt =>
            {
                opt.Cookie.Name = CookieNameBase + (allowHttp ? "_http_ok" : "");
                opt.ExpireTimeSpan = CookieLifetime.Value;
                opt.Cookie.SecurePolicy = allowHttp ? CookieSecurePolicy.None : CookiePolicy.Value;
                opt.LoginPath = LoginPath.Value;
                opt.SlidingExpiration = true;
            });
    }
}

public class EasyCookieAuthModel
{
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string? ReturnUrl { get; set; }

    public string? ErrorStr { get; set; }
}

[AspNetLibFeature(AspNetLibFeatures.EasyCookieAuth)]
public class EasyCookieAuthController : Controller
{
    [HttpGet]
    [AllowAnonymous]
    public IActionResult Index(string ReturnUrl)
    {
        return View(new EasyCookieAuthModel { ReturnUrl = ReturnUrl });
    }

    [HttpPost]
    [AllowAnonymous]
    public async Task<IActionResult> Index(EasyCookieAuthModel model)
    {
        model.Username = model.Username._NonNullTrim();
        model.Password = model.Password._NonNullTrim();

        if (model.Username._IsEmpty() || model.Password._IsEmpty())
        {
            model.ErrorStr = "ユーザー名とパスワードを入力してください。";
            return View(model);
        }

        if (EasyCookieAuth.AuthenticationPasswordValidator == null || (await EasyCookieAuth.AuthenticationPasswordValidator(model.Username, model.Password)) == false)
        {
            model.ErrorStr = "ユーザー名またはパスワードが間違っています。確認をして再度入力をしてください。";
            Con.WriteError($"EasyCookieAuthController: Login failed. Username = {model.Username}");
            return View(model);
        }

        Claim[] claims = new[]
        {
                new Claim(ClaimTypes.Name, model.Username),
            };

        ClaimsIdentity identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        ClaimsPrincipal principal = new ClaimsPrincipal(identity);

        AuthenticationProperties authProperties = new AuthenticationProperties
        {
            AllowRefresh = true,
            ExpiresUtc = DateTimeOffset.UtcNow.Add(EasyCookieAuth.CookieLifetime),
            IsPersistent = true,
        };

        await HttpContext.SignInAsync(principal, authProperties);

        Con.WriteError($"EasyCookieAuthController: Logged in. Username = {model.Username}");

        if (model.ReturnUrl._IsEmpty())
        {
            return Redirect("/");
        }
        else
        {
            return Redirect(model.ReturnUrl);
        }
    }

    [AllowAnonymous]
    public async Task<IActionResult> Logout(string ReturnUrl)
    {
        await HttpContext.SignOutAsync();

        if (ReturnUrl._IsFilled())
        {
            return Redirect(ReturnUrl);
        }
        else
        {
            return Redirect("/");
        }
    }
}
