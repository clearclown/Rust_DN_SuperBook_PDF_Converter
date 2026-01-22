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

#if CORES_BASIC_WEBAPP && CORES_BASIC_HTTPSERVER
#error You cannot specify both CORES_BASIC_WEBAPP and CORES_BASIC_HTTPSERVER.
#endif

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
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Http.Extensions;
using Newtonsoft.Json;
using System.Linq;


using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Microsoft.AspNetCore.Routing;
using IPA.Cores.ClientApi.Acme;
using System.Security.Claims;
using System.Runtime.Serialization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Extensions.FileProviders;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.WebEncoders;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using Microsoft.AspNetCore.HttpOverrides;

using Microsoft.AspNetCore.Authentication.Certificate;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using System.Net.Security;

#if CORES_BASIC_HTTPSERVER
// ASP.NET Core 3.0 用の型名を無理やり ASP.NET Core 2.2 でコンパイルするための型エイリアスの設定
using IWebHostEnvironment = Microsoft.AspNetCore.Hosting.IHostingEnvironment;
using IHostApplicationLifetime = Microsoft.AspNetCore.Hosting.IApplicationLifetime;
#endif

namespace IPA.Cores.Basic;

public static partial class CoresConfig
{
    public static partial class HttpServerSimpleBasicAuthDatabaseConfig
    {
        public static readonly Copenhagen<Action<HttpServerSimpleBasicAuthDatabase>> InitAuthDatabaseUsernameAndPasswordCallback
            = new Copenhagen<Action<HttpServerSimpleBasicAuthDatabase>>((db) =>
            {
                db.UsernameAndPassword.Add("user1", Str.GenRandPassword());
                db.UsernameAndPassword.Add("user2", Str.GenRandPassword());
            });
    }
}

public class HttpServerStartupConfig
{
}

public class HttpServerSimpleBasicAuthDatabase
{
    public Dictionary<string, string> UsernameAndPassword = new Dictionary<string, string>(StrComparer.IgnoreCaseComparer);

    public HttpServerSimpleBasicAuthDatabase() { }

    public HttpServerSimpleBasicAuthDatabase(EnsureSpecial createInitialAdminUsers)
    {
        CoresConfig.HttpServerSimpleBasicAuthDatabaseConfig.InitAuthDatabaseUsernameAndPasswordCallback.Value(this);
    }

    public bool Authenticate(string username, string password)
    {
        if (UsernameAndPassword.TryGetValue(username, out string? pw))
        {
            if (password == pw)
            {
                return true;
            }
        }

        return false;
    }
}

public class HttpServerAnyAuthenticationRequired : AuthorizationHandler<HttpServerAnyAuthenticationRequired>, IAuthorizationRequirement
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        HttpServerAnyAuthenticationRequired requirement)
    {
        if (context.User.Identity!.IsAuthenticated)
        {
            context.Succeed(requirement);
        }
        else
        {
            context.Fail();
        }
        return Task.FromResult(0);
    }
}

public class HttpServerStartupHelper : IDisposable
{
    public IConfiguration Configuration { get; }
    public HttpServerOptions ServerOptions { get; }
    public HttpServerStartupConfig StartupConfig { get; }
    public object? Param { get; }
    public CancellationToken CancelToken { get; }

    public bool IsDevelopmentMode { get; private set; }

    readonly List<IFileProvider> StaticFileProviderList = new List<IFileProvider>();

    public Func<string, string, Task<bool>>? SimpleBasicAuthenticationPasswordValidator { get; set; } = null;

    public HttpServerStartupHelper(IConfiguration configuration)
    {
        this.Configuration = configuration;

        this.Param = GlobalObjectExchange.Withdraw(this.Configuration["coreutil_param_token"]);
        this.CancelToken = (CancellationToken)GlobalObjectExchange.Withdraw(this.Configuration["coreutil_cancel_token"])!;

        this.ServerOptions = this.Configuration["coreutil_ServerBuilderConfig"]._JsonToObject<HttpServerOptions>()!;
        this.ServerOptions.AfterJsonToObject();
        this.StartupConfig = new HttpServerStartupConfig();

        if (this.ServerOptions.DisableHiveBasedSetting == false)
        {
            Hive.LocalAppSettingsEx[this.ServerOptions.HiveName].AccessData(true,
                k =>
                {
                    this.IsDevelopmentMode = k.GetBool("IsDevelopmentMode", false);

                    if (this.ServerOptions.UseSimpleBasicAuthentication || this.ServerOptions.HoldSimpleBasicAuthenticationDatabase)
                    {
                        k.Get("SimpleBasicAuthDatabase", new HttpServerSimpleBasicAuthDatabase(EnsureSpecial.Yes));

                        SimpleBasicAuthenticationPasswordValidator = async (username, password) =>
                        {
                            bool ok = false;

                            Hive.LocalAppSettingsEx[this.ServerOptions.HiveName].AccessData(false, k2 =>
                            {
                                HttpServerSimpleBasicAuthDatabase? db = k2.Get<HttpServerSimpleBasicAuthDatabase>("SimpleBasicAuthDatabase");

                                ok = db?.Authenticate(username, password) ?? false;
                            });

                            await Task.CompletedTask;
                            return ok;
                        };
                    }
                });
        }
    }

    public virtual void ConfigureServices(IServiceCollection services)
    {
        services.AddHttpExceptionLogger(opt => { });

        if (ServerOptions.ReverseProxyOverride_Enable)
        {
            services.Configure<ForwardedHeadersOptions>(options =>
            {
                options.ForwardLimit = ServerOptions.ReverseProxyOverride_ForwardLimit;
                options.ForwardedHeaders = ForwardedHeaders.All;

                if (ServerOptions.ReverseProxyOverride_HeaderName_X_Forwarded_For._IsFilled())
                    options.ForwardedForHeaderName = ServerOptions.ReverseProxyOverride_HeaderName_X_Forwarded_For._NonNullTrim();

                if (ServerOptions.ReverseProxyOverride_HeaderName_X_Forwarded_Host._IsFilled())
                    options.ForwardedHostHeaderName = ServerOptions.ReverseProxyOverride_HeaderName_X_Forwarded_Host._NonNullTrim();

                if (ServerOptions.ReverseProxyOverride_HeaderName_X_Forwarded_Proto._IsFilled())
                    options.ForwardedProtoHeaderName = ServerOptions.ReverseProxyOverride_HeaderName_X_Forwarded_Proto._NonNullTrim();

                options.KnownNetworks.Clear();
                options.KnownProxies.Clear();

                if (ServerOptions.ReverseProxyOverride_TrustedSubnets._IsFilled())
                {
                    var acl = EasyIpAcl.GetOrCreateCachedIpAcl(ServerOptions.ReverseProxyOverride_TrustedSubnets, defaultAction: EasyIpAclAction.Deny, defaultActionForEmpty: EasyIpAclAction.Deny);

                    foreach (var subnet in acl.RuleList.Where(x => x.Action == EasyIpAclAction.Permit))
                    {
                        if (subnet.SubnetLength.HasValue)
                        {
                            IPNetwork net = new IPNetwork(subnet.Network, subnet.SubnetLength.Value);

                            options.KnownNetworks.Add(net);
                        }
                    }
                }

                DoNothing();
            });
        }

        if (this.ServerOptions.AutomaticRedirectToHttpsIfPossible)
        {
            services.AddEnforceHttps(_ => { });
        }

        services.AddRouting();

        if (ServerOptions.UseSimpleBasicAuthentication)
        {
            // Basic 認証の追加
            services.AddBasicAuth(opt => opt.PasswordValidatorAsync = this.SimpleBasicAuthenticationPasswordValidator);
        }

        if (ServerOptions.DisableExcessiveHtmlEncoding)
        {
            services.Configure<WebEncoderOptions>(options =>
            {
                options.TextEncoderSettings = new TextEncoderSettings(UnicodeRanges.All);
            });
        }

        //    if (ServerOptions.RequireBasicAuthenticationToAllRequests)
        //    {
        //        services.AddAuthorization(options =>
        //        {
        //            options.AddPolicy(nameof(HttpServerAnyAuthenticationRequired), policy => policy.Requirements.Add(new HttpServerAnyAuthenticationRequired()));
        //        });
        //    }

        //    if (ServerOptions.UseSimpleBasicAuthentication)
        //    {
        //        // Simple BASIC authentication
        //        services.AddAuthentication(BasicAuthDefaults.AuthenticationScheme)
        //            .AddBasic(options =>
        //            {
        //                options.AllowInsecureProtocol = true;
        //                options.Realm = this.ServerOptions.SimpleBasicAuthenticationRealm._FilledOrDefault("Auth");

        //                options.Events = new BasicAuthEvents
        //                {
        //                    OnValidateCredentials = async (context) =>
        //                    {
        //                        if (await this.SimpleBasicAuthenticationPasswordValidator(context.Username, context.Password))
        //                        {
        //                            var claims = new[]
        //                            {
        //                            new Claim(
        //                                ClaimTypes.NameIdentifier,
        //                                context.Username,
        //                                ClaimValueTypes.String,
        //                                context.Options.ClaimsIssuer),

        //                            new Claim(
        //                                ClaimTypes.Name,
        //                                context.Username,
        //                                ClaimValueTypes.String,
        //                                context.Options.ClaimsIssuer)
        //                            };

        //                            context.Principal = new ClaimsPrincipal(
        //                                new ClaimsIdentity(claims, context.Scheme.Name));

        //                            context.Success();
        //                        }
        //                    }
        //                };
        //            });
        //    }
    }

    public void AddStaticFileProvider(string physicalDirectoryPath)
    {
        FileSystemBasedProvider provider = Lfs.CreateFileProvider(physicalDirectoryPath);

        this.AddStaticFileProvider(provider);
    }

    public void AddStaticFileProvider(IFileProvider provider)
    {
        if (provider != null)
            StaticFileProviderList.Add(provider);
    }

    public void AddStaticFileProvider(IEnumerable<IFileProvider> provider)
    {
        if (provider != null)
            provider._DoForEach(x => AddStaticFileProvider(x));
    }

    [Obsolete("Use ConfigurePre and ConfigureAuth instead.")]
    public virtual void Configure(IApplicationBuilder app, IWebHostEnvironment env)
    {
        this.ConfigurePre(app, env);
        this.ConfigureAuth(app, env);
    }

    public virtual void ConfigurePre(IApplicationBuilder app, IWebHostEnvironment env)
    {
        if (ServerOptions.ReverseProxyOverride_Enable)
        {
            app.UseForwardedHeaders();
        }

        if (this.ServerOptions.DenyRobots)
        {
            app.Use((context, next) =>
            {
                if (context.Request.Path.Value._IsSameTrimi(Consts.UrlPaths.Robots))
                {
                    string replyBody = "User-agent: *\r\nDisallow: /\r\n";
                    context.Response.StatusCode = 200;
                    return context.Response.WriteAsync(replyBody, context.RequestAborted);
                }
                else
                {
                    return next();
                }
            });
        }

        if (this.ServerOptions.AutomaticRedirectToHttpsIfPossible)
        {
            app.UseEnforceHttps();
        }

        app.UseStatusCodePages();

        app.UseWebServerLogger();

#if CORES_BASIC_JSON
        if (this.ServerOptions.UseGlobalCertVault && this.ServerOptions.HasHttpPort80)
        {
            // Add the ACME HTTP-based challenge responder
            RouteBuilder rb = new RouteBuilder(app);

            rb.MapGet("/.well-known/acme-challenge/{token}", AcmeGetChallengeFileRequestHandler);

            IRouter router = rb.Build();
            app.UseRouter(router);
        }
#endif  // CORES_BASIC_JSON

        if (this.ServerOptions.MustIncludeHostnameStrList.Count >= 1)
        {
            app.Use(async (context, next) =>
            {
                if (this.ServerOptions.MustIncludeHostnameStrList.Select(x => x._NonNullTrim()).Where(x => x._IsFilled() && (context?.Request?.Host.Host?._InStr(x, true) ?? false)).Any())
                {
                    await next();
                }
                else
                {
                    context.Response.StatusCode = 404;
                    await context.Response._SendStringContentsAsync("Server not found");
                }
            });
        }
    }

    public virtual void ConfigureAuth(IApplicationBuilder app, IWebHostEnvironment env)
    {
        if (this.ServerOptions.ClientCertficateMode != ClientCertificateMode.NoCertificate)
        {
            // ここで証明書の検証を行なう
            app.Use(async (context, next) =>
            {
                var cancel = context._GetRequestCancellationToken();
                var clientCertificate = await context.Connection.GetClientCertificateAsync(cancel);

                bool ok = false;

                string errStrJa = "";
                string errStrEn = "";

                try
                {
                    if (clientCertificate == null)
                    {
                        if (this.ServerOptions.ClientCertficateMode == ClientCertificateMode.AllowCertificate)
                        {
                            // 証明書が提示されなかった。これは差し支え無い。
                            ok = true;
                        }
                        else
                        {
                            // 証明書が提示されなかった。証明書が要求されているので、これではエラーになる。
                            ok = false;
                            if (context.Request.IsHttps)
                            {
                                errStrJa = "クライアント証明書が Web ブラウザによって提示されませんでした。";
                                errStrEn = "The client certificate was not presented by the web browser.";
                            }
                            else
                            {
                                errStrJa = "この Web サイトは SSL クライアント証明書を必要とするため、HTTPS プロトコルでアクセスする必要があります。";
                                errStrEn = "This web site requires an SSL client certificate and must be accessed with the HTTPS protocol.";
                            }
                        }
                    }
                    else if (this.ServerOptions.ClientCertificateValidatorAsync == null)
                    {
                        // 証明書が提示されたが、プログラムエラー (検証用コールバックが設定されていない) により検証できなかった。
                        var ex = new CoresLibException("this.ClientCertificateValidatorAsync == null");
                        ex._Error();
                        ok = false;
                        errStrJa = "プログラム内部エラー: コールバック関数 ClientCertificateValidatorAsync() が実装されていません。";
                        errStrEn = "Program Internal Error: The callback function ClientCertificateValidatorAsync() is not implemented.";
                    }
                    else
                    {
                        // 提示された証明書の検証を実施
                        ok = await this.ServerOptions.ClientCertificateValidatorAsync(clientCertificate, null, SslPolicyErrors.None);
                        if (ok == false)
                        {
                            errStrJa = "指定されたクライアント証明書の認証に失敗しました。検証関数が false (検証失敗) を返しました。";
                            errStrEn = "Failed to authenticate the specified client certificate. The verification function returned false (= verification failure).";
                        }
                    }
                }
                catch (Exception ex)
                {
                    // 何らかの検証エラーが発生 (大抵は証明書検証中のエラー)
                    ex._Error();
                    ok = false;
                    errStrJa = "指定されたクライアント証明書の認証に失敗しました。\nエラー内容: " + ex.Message;
                    errStrEn = "Failed to authenticate the specified client certificate.\nError: " + ex.Message;
                }

                if (ok == false)
                {
                    // 証明書検証エラーを HTTP 文字列として表示する
                    string template = CoresRes["Text/WebServer/CertSoftFailError.txt"].String;

                    DateTimeOffset now = DtOffsetNow;

                    string errorBody = template._ReplaceStrWithReplaceClass(
                        new
                        {
                            __ERR_TEXT_JA__ = errStrJa,
                            __ERR_TEXT_EN__ = errStrEn,

                            __TIMESTAMP__ = now._ToDtStr(true),
                        });

                    await context.Response._SendStringContentsAsync(errorBody._NormalizeCrlf(CrlfStyle.CrLf, true), cancel: cancel);
                }
                else
                {
                    // 検証に成功したので次を呼ぶ
                    await next();
                }
            });
        }

        //app.UseAuthentication();

        //if (ServerOptions.UseSimpleBasicAuthentication)
        //{
        //    // Simple Basic Authentication
        //    app.UseAuthentication();

        //    if (ServerOptions.RequireBasicAuthenticationToAllRequests)
        //    {
        //        app.Use(async (context, next) =>
        //        {
        //            AuthorizationResult allowed = await context.RequestServices.GetRequiredService<IAuthorizationService>().AuthorizeAsync(context.User, null, nameof(HttpServerAnyAuthenticationRequired));
        //            if (allowed.Succeeded)
        //            {
        //                await next();
        //            }
        //            else
        //            {
        //                await context.RequestServices.GetRequiredService<IAuthenticationService>().ChallengeAsync(context, BasicAuthDefaults.AuthenticationScheme, new AuthenticationProperties());
        //            }
        //        });
        //    }
        //}


        if (ServerOptions.UseSimpleBasicAuthentication)
        {
            // Basic 認証の実施
            app.UseBasicAuth();
        }

        if (ServerOptions.UseStaticFiles)
        {
            StaticFileOptions sfo = new StaticFileOptions
            {
                FileProvider = new CompositeFileProvider(this.StaticFileProviderList),
            };

            app.UseStaticFiles(sfo);
        }
    }

#if CORES_BASIC_JSON && CORES_BASIC_JSON

    public virtual async Task AcmeGetChallengeFileRequestHandler(HttpRequest request, HttpResponse response, RouteData routeData)
    {
        try
        {
            AcmeAccount? currentAccount = GlobalCertVault.GetAcmeAccountForChallengeResponse();
            string retStr;

            if (currentAccount == null)
            {
                retStr = "Error: GlobalCertVault.GetAcmeAccountForChallengeResponse() == null";
            }
            else
            {
                string token = routeData.Values._GetStr("token");

                retStr = currentAccount.ProcessChallengeRequest(token);
            }

            await response._SendStringContentsAsync(retStr, Consts.MimeTypes.OctetStream);
        }
        catch (Exception ex)
        {
            ex._Debug();
            throw;
        }
    }

    public void Dispose() { this.Dispose(true); GC.SuppressFinalize(this); }
    Once DisposeFlag;
    protected virtual void Dispose(bool disposing)
    {
        if (!disposing || DisposeFlag.IsFirstCall() == false) return;

        foreach (IFileProvider provider in StaticFileProviderList)
        {
            if (provider is IDisposable target) target._DisposeSafe();
        }
    }

#endif  // CORES_BASIC_JSON && CORES_BASIC_JSON

}

public abstract class HttpServerStartupBase
{
    public HttpServerStartupHelper Helper { get; }
    public IConfiguration Configuration { get; }

    public HttpServerOptions ServerOptions => Helper.ServerOptions;
    public HttpServerStartupConfig StartupConfig => Helper.StartupConfig;
    public object? Param => Helper.Param;
    public CancellationToken CancelToken => Helper.CancelToken;

    protected abstract void ConfigureImpl_BeforeHelper(HttpServerStartupConfig cfg, IApplicationBuilder app, IWebHostEnvironment env, IHostApplicationLifetime lifetime);

    protected abstract void ConfigureImpl_AfterHelper(HttpServerStartupConfig cfg, IApplicationBuilder app, IWebHostEnvironment env, IHostApplicationLifetime lifetime);

    public HttpServerStartupBase(IConfiguration configuration)
    {
        this.Configuration = configuration;

        this.Helper = new HttpServerStartupHelper(configuration);
    }

    public virtual void ConfigureServices(IServiceCollection services)
    {
        Helper.ConfigureServices(services);
    }

    public virtual void Configure(IApplicationBuilder app, IWebHostEnvironment env, IHostApplicationLifetime lifetime)
    {
        Helper.ConfigurePre(app, env);

        ConfigureImpl_BeforeHelper(Helper.StartupConfig, app, env, lifetime);

        Helper.ConfigureAuth(app, env);

        ConfigureImpl_AfterHelper(Helper.StartupConfig, app, env, lifetime);
    }
}

public class StaticOptionsMonitor<T> : IOptionsMonitor<T>
{
    public T CurrentValue { get; }

    public StaticOptionsMonitor(T value)
    {
        this.CurrentValue = value;
    }

    public T Get(string name)
    {
        throw new NotImplementedException();
    }

    public IDisposable OnChange(Action<T, string> listener)
    {
        return new EmptyDisposable();
    }
}

public class HttpServerOptions
{
    // Settings which can be saved on the Hive
    public List<int> HttpPortsList { get; set; } = new List<int>(new int[] { 88, 8080 });
    public List<int> HttpsPortsList { get; set; } = new List<int>(new int[] { 8081 });
    public bool LocalHostOnly { get; set; } = false;
    public bool IPv4Only { get; set; } = false;
    public List<string> MustIncludeHostnameStrList { get; set; } = new List<string>();

    // Other settings
    public string ContentsRoot { get; set; } = Env.AppRootDir;
    public string WwwRoot { get; set; } = Env.AppRootDir._CombinePath("wwwroot");
    public bool DebugKestrelToConsole { get; set; } = false;
    public bool DebugKestrelToLog { get; set; } = false;
    public bool UseStaticFiles { get; set; } = true;
    public bool ShowDetailError { get; set; } = true;
    public bool UseKestrelWithIPACoreStack { get; set; } = true;

    public int? ReadTimeoutMsecs { get; set; } = null;

    public bool UseSimpleBasicAuthentication { get; set; } = false;
    public bool HoldSimpleBasicAuthenticationDatabase { get; set; } = false;
    public string SimpleBasicAuthenticationRealm { get; set; } = "Basic Authentication";
    public bool AutomaticRedirectToHttpsIfPossible { get; set; } = true;

    public bool ReverseProxyOverride_Enable { get; set; } = true;
    public int ReverseProxyOverride_ForwardLimit { get; set; } = 4;
    public string ReverseProxyOverride_TrustedSubnets { get; set; } = "127.0.0.0/8 192.168.0.0/16 172.16.0.0/12 10.0.0.0/8 ::1/128 fe80::/64";
    public string ReverseProxyOverride_HeaderName_X_Forwarded_For { get; set; } = "X-Forwarded-For";
    public string ReverseProxyOverride_HeaderName_X_Forwarded_Host { get; set; } = "X-Forwarded-Host";
    public string ReverseProxyOverride_HeaderName_X_Forwarded_Proto { get; set; } = "X-Forwarded-Proto";

    public bool DisableExcessiveHtmlEncoding { get; set; } = true;

    public bool DisableHttpKeepAlive { get; set; } = false;

    public bool HideKestrelServerHeader { get; set; } = true;

    public int MaxRequestBodySize { get; set; } = Consts.Numbers.DefaultMaxNetworkRecvSize; // Kestrel default

    public int KestrelMaxConcurrentConnections { get; set; } = Consts.Numbers.DefaultKestrelMaxConcurrentConnections;
    public int KestrelMaxUpgradedConnections { get; set; } = Consts.Numbers.DefaultKestrelMaxUpgradedConnections;

    public string HiveName { get; set; } = Consts.HiveNames.DefaultWebServer;
    public bool DisableHiveBasedSetting = false;

    public List<string> StringOptions = new List<string>();

    public bool DenyRobots { get; set; } = false;

    public ClientCertificateMode ClientCertficateMode { get; set; } = ClientCertificateMode.NoCertificate;

    [JsonIgnore]
    public Func<X509Certificate2, X509Chain?, SslPolicyErrors, Task<bool>>? ClientCertificateValidatorAsync = null;

    public string ObjToken_ClientCertificateValidatorAsync = "";

    public void BeforeObjectToJson()
    {
        this.ObjToken_ClientCertificateValidatorAsync = GlobalObjectExchange.Deposit(this.ClientCertificateValidatorAsync);
    }

    public void AfterJsonToObject()
    {
        if (GlobalObjectExchange.TryWithdraw(this.ObjToken_ClientCertificateValidatorAsync, out object? tmp))
        {
            this.ClientCertificateValidatorAsync = (Func<X509Certificate2, X509Chain?, SslPolicyErrors, Task<bool>>?)tmp;
        }
    }

#if CORES_BASIC_JSON
#if CORES_BASIC_SECURITY
    public bool UseGlobalCertVault { get; set; } = true;

    public bool StartGlobalCertVaultOnHttpServerStartup { get; set; } = false;

    [JsonIgnore]
    public CertificateStore? GlobalCertVaultDefauleCert { get; set; } = null;
#endif  // CORES_BASIC_JSON
#endif  // CORES_BASIC_SECURITY;

    [JsonIgnore]
    public bool HasHttpPort80 => this.HttpPortsList.Where(x => x == 80).Any();

    [JsonIgnore]
    public CertSelectorCallback? ServerCertSelector { get; set; } = null;

    [JsonIgnore]
    public CertSelectorAsyncCallback2? ServerCertSelector2Async { get; set; } = null;

    [JsonIgnore]
    public TcpIpSystem? TcpIp { get; set; } = null;

    public IWebHostBuilder GetWebHostBuilder<TStartup>(object? sslCertSelectorParam = null) where TStartup : class
    {
        IWebHostBuilder? baseWebHost = null;

        if (this.UseKestrelWithIPACoreStack)
        {
            baseWebHost = new WebHostBuilder().UseKestrelWithStack(opt => ConfigureKestrelServerOptions(opt, sslCertSelectorParam));
        }
        else
        {
            baseWebHost = new WebHostBuilder().UseKestrel(opt => ConfigureKestrelServerOptions(opt, sslCertSelectorParam));
        }

        return baseWebHost
            .UseWebRoot(WwwRoot)
            .UseContentRoot(ContentsRoot)
            .ConfigureAppConfiguration((hostingContext, config) =>
            {

            })
            .ConfigureLogging((hostingContext, logging) =>
            {
                if (this.DebugKestrelToConsole)
                {
#pragma warning disable CS0618 // 型またはメンバーが旧型式です
                    ConsoleLoggerOptions opt = new ConsoleLoggerOptions { DisableColors = false, IncludeScopes = true };
#pragma warning restore CS0618 // 型またはメンバーが旧型式です

                    logging.AddProvider(new ConsoleLoggerProvider(new StaticOptionsMonitor<ConsoleLoggerOptions>(opt)));
                }

                if (this.DebugKestrelToLog)
                {
                    logging.AddProvider(new MsLoggerProvider());
                }
            })
            .UseStartup<TStartup>();
    }

    public void ConfigureKestrelServerOptions(KestrelServerOptions opt, object? sslCertSelectorParam)
    {
        opt.AddServerHeader = !this.HideKestrelServerHeader;

        KestrelServerWithStackOptions? withStackOpt = opt as KestrelServerWithStackOptions;

        if (this.LocalHostOnly)
        {
            foreach (int port in this.HttpPortsList) opt.ListenLocalhost(port);
            foreach (int port in this.HttpsPortsList) opt.ListenLocalhost(port, lo => EnableHttps(lo));
        }
        else if (this.IPv4Only)
        {
            foreach (int port in this.HttpPortsList) opt.Listen(IPAddress.Any, port);
            foreach (int port in this.HttpsPortsList) opt.Listen(IPAddress.Any, port, lo => EnableHttps(lo));
        }
        else
        {
            foreach (int port in this.HttpPortsList) opt.ListenAnyIP(port);
            foreach (int port in this.HttpsPortsList) opt.ListenAnyIP(port, lo => EnableHttps(lo));
        }

        opt.Limits.MaxRequestBodySize = this.MaxRequestBodySize;

        if (withStackOpt != null)
        {
            // Kestrel with Stack
            withStackOpt.TcpIp = this.TcpIp ?? LocalNet;
        }

        opt.Limits.MaxConcurrentConnections = this.KestrelMaxConcurrentConnections;
        opt.Limits.MaxConcurrentUpgradedConnections = this.KestrelMaxUpgradedConnections;

        if (this.ReadTimeoutMsecs != null)
        {
            opt.Limits.RequestHeadersTimeout = TimeSpan.FromMilliseconds(this.ReadTimeoutMsecs.Value);
            opt.Limits.KeepAliveTimeout = TimeSpan.FromMilliseconds(this.ReadTimeoutMsecs.Value);
        }

        if (this.DisableHttpKeepAlive)
        {
            opt.Limits.KeepAliveTimeout = TimeSpan.FromTicks(1);
        }

        opt.ConfigureHttpsDefaults(s =>
        {
            s.SslProtocols = CoresConfig.SslSettings.DefaultSslProtocolVersionsAsServer;

            s.ClientCertificateMode = this.ClientCertficateMode;
            if (this.ClientCertficateMode == ClientCertificateMode.RequireCertificate)
            {
                s.ClientCertificateMode = ClientCertificateMode.AllowCertificate;
            }

            if (this.ReadTimeoutMsecs != null)
            {
                s.HandshakeTimeout = TimeSpan.FromMilliseconds(this.ReadTimeoutMsecs.Value);
            }
        });

        void EnableHttps(ListenOptions listenOptions)
        {
            int sslTimeoutMsecs = int.MaxValue;

            if (this.ReadTimeoutMsecs != null) sslTimeoutMsecs = Math.Min(sslTimeoutMsecs, this.ReadTimeoutMsecs.Value);
            sslTimeoutMsecs = Math.Min(sslTimeoutMsecs, CoresConfig.SslSettings.DefaultNegotiationRecvTimeout.Value);

#pragma warning disable CS1998 // 非同期メソッドは、'await' 演算子がないため、同期的に実行されます
            listenOptions.UseHttps(async (sslStream, sslHelloInfo, state, cancel) =>
#pragma warning restore CS1998 // 非同期メソッドは、'await' 演算子がないため、同期的に実行されます
            {
                try
                {
                    SslServerAuthenticationOptions ret = new SslServerAuthenticationOptions();

                    ret.AllowRenegotiation = false;
                    ret.CertificateRevocationCheckMode = X509RevocationMode.NoCheck;
                    ret.EncryptionPolicy = CoresConfig.SslSettings.DefaultSslEncryptionPolicyServer;
                    ret.CipherSuitesPolicy = CoresConfig.SslSettings.DefaultCipherSuitesPolicyServer;
                    ret.ServerCertificate = null;
                    ret.ServerCertificateSelectionCallback = null;
                    ret.ServerCertificateContext = null;

                    ret.EnabledSslProtocols = CoresConfig.SslSettings.DefaultSslProtocolVersionsAsServer;

                    ret.ClientCertificateRequired = (this.ClientCertficateMode != ClientCertificateMode.NoCertificate);

                    bool useGlobalCertVault = false;

#if CORES_BASIC_JSON
#if CORES_BASIC_SECURITY
                    useGlobalCertVault = this.UseGlobalCertVault;
#endif  // CORES_BASIC_JSON
#endif  // CORES_BASIC_SECURITY;

                    if (useGlobalCertVault == false)
                    {
                        if (this.ServerCertSelector2Async != null)
                        {
                            try
                            {
                                ret.ServerCertificateContext = await this.ServerCertSelector2Async(sslCertSelectorParam, sslHelloInfo.ServerName._NonNull());
                            }
                            catch (Exception ex)
                            {
                                ex._Error();
                                throw;
                            }
                        }
                        else if (this.ServerCertSelector != null)
                        {
                            try
                            {
                                var cert = this.ServerCertSelector(sslCertSelectorParam, sslHelloInfo.ServerName._NonNull());

                                cert._NullCheck();

                                if (cert.HasPrivateKey == false) throw new CoresLibException("cert.HasPrivateKey == false");

                                ret.ServerCertificate = cert;
                            }
                            catch (Exception ex)
                            {
                                ex._Error();
                                throw;
                            }
                        }
                    }

#if CORES_BASIC_JSON
#if CORES_BASIC_SECURITY
                    if (useGlobalCertVault)
                    {
                        if (this.GlobalCertVaultDefauleCert != null)
                        {
                            GlobalCertVault.SetDefaultCertificate(this.GlobalCertVaultDefauleCert);
                        }

                        if (this.StartGlobalCertVaultOnHttpServerStartup)
                        {
                            // GlobalCertVault をすぐに起動する
                            GlobalCertVault.GetGlobalCertVault();
                        }

                        var selectedCert = GlobalCertVault.GetGlobalCertVault().CertificateStoreSelector(sslHelloInfo.ServerName._NonNull(), !this.HasHttpPort80);

                        ret.ServerCertificateContext = selectedCert.SslStreamCertificateContext;
                    }
#endif  // CORES_BASIC_JSON
#endif  // CORES_BASIC_SECURITY;

                    if (this.ClientCertficateMode != ClientCertificateMode.NoCertificate)
                    {
                        ret.RemoteCertificateValidationCallback = (sender, cert, chain, err) =>
                        {
                            // ここでは証明書を検証せずに true を返す
                            return true;
                        };
                    }

                    return ret;
                }
                catch (Exception ex)
                {
                    ex._Error();
                    throw;
                }
            }, Limbo.EmptyObject, TimeSpan.FromMilliseconds(sslTimeoutMsecs));
        }
    }
}

public sealed class HttpServer<THttpServerBuilder> : AsyncService
    where THttpServerBuilder : class
{
    readonly HttpServerOptions Options;
    readonly Task HostTask;

    readonly string? ParamToken;
    readonly string? CancelToken;

    public HttpServer(HttpServerOptions options, object? param = null, CancellationToken cancel = default) : base(cancel)
    {
        try
        {
            this.Options = options;

            bool isDevelopmentMode = false;

            if (this.Options.DisableHiveBasedSetting == false)
            {
                Hive.LocalAppSettingsEx[this.Options.HiveName].AccessData(true,
                    k =>
                    {
                        isDevelopmentMode = k.GetBool("IsDevelopmentMode", false);

                        // Update options with the config file
                        string httpPortsList = k.GetStr("HttpPorts", Str.PortsListToStr(Options.HttpPortsList));
                        Options.HttpPortsList = Str.ParsePortsList(httpPortsList).ToList();

                        string httpsPortsList = k.GetStr("HttpsPorts", Str.PortsListToStr(Options.HttpsPortsList));
                        Options.HttpsPortsList = Str.ParsePortsList(httpsPortsList).ToList();

                        Options.LocalHostOnly = k.GetBool("LocalHostOnly", Options.LocalHostOnly);
                        Options.IPv4Only = k.GetBool("IPv4Only", Options.IPv4Only);

                        string mustIncludeHostnameStr = k.GetStr("MustIncludeHostnameList", "");
                        string[] tokens = mustIncludeHostnameStr.Split(new char[] { ' ', '　', ';', '/', ',' }, StringSplitOptions.RemoveEmptyEntries);
                        tokens._DoForEach(x => Options.MustIncludeHostnameStrList.Add(x));
                    });
            }

            Lfs.CreateDirectory(Options.WwwRoot);
            Lfs.CreateDirectory(Options.ContentsRoot);

            ParamToken = GlobalObjectExchange.Deposit(param);
            CancelToken = GlobalObjectExchange.Deposit(this.GrandCancel);

            try
            {
                this.Options.BeforeObjectToJson();
                var dict = new Dictionary<string, string>
                    {
                        {"coreutil_ServerBuilderConfig", this.Options._ObjectToJson() },
                        {"coreutil_param_token", ParamToken },
                        {"coreutil_cancel_token", CancelToken },
                    };

                IConfiguration iconf = new ConfigurationBuilder()
                    .AddInMemoryCollection(dict)
                    .Build();

                var host = Options.GetWebHostBuilder<THttpServerBuilder>(param)
                    .UseConfiguration(iconf)
                    .CaptureStartupErrors(true)
                    .UseSetting("detailedErrors", isDevelopmentMode.ToString())
                    .Build();

                HostTask = host.RunAsync(this.CancelWatcher.CancelToken);
            }
            catch
            {
                GlobalObjectExchange.TryWithdraw(ParamToken, out _);
                ParamToken = null;

                GlobalObjectExchange.TryWithdraw(CancelToken, out _);
                CancelToken = null;
                throw;
            }
        }
        catch
        {
            this._DisposeSafe();
            throw;
        }
    }

    protected override async Task CleanupImplAsync(Exception? ex)
    {
        try
        {
            if (HostTask != null)
            {
                await HostTask;
            }
        }
        finally
        {
            GlobalObjectExchange.TryWithdraw(ParamToken, out _);
            GlobalObjectExchange.TryWithdraw(CancelToken, out _);

            await base.CleanupImplAsync(ex);
        }
    }
}

#endif // CORES_BASIC_WEBAPP

