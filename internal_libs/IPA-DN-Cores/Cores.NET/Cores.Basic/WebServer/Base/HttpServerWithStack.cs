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
using Microsoft.AspNetCore.Http.Extensions;
using Newtonsoft.Json;
using System.Linq;

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
using Microsoft.Extensions.Options;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Connections;

#if CORES_BASIC_HTTPSERVER
// ASP.NET Core 3.0 用の型名を無理やり ASP.NET Core 2.2 でコンパイルするための型エイリアスの設定
using IWebHostEnvironment = Microsoft.AspNetCore.Hosting.IHostingEnvironment;
using IHostApplicationLifetime = Microsoft.AspNetCore.Hosting.IApplicationLifetime;
using IConnectionListenerFactory = Microsoft.AspNetCore.Server.Kestrel.Transport.Abstractions.Internal.ITransportFactory;

#endif

namespace IPA.Cores.Basic;

public static partial class CoresConfig
{
    public static partial class KestrelWithStackSettings
    {
        public static readonly Copenhagen<int> MaxLogStringLen = 1024;
    }
}

// Pure copy from git\AspNetCore\src\Servers\Kestrel\Core\src\Internal\KestrelServerOptionsSetup.cs
public class KestrelServerOptionsSetup : IConfigureOptions<KestrelServerOptions>
{
    private IServiceProvider _services;

    public KestrelServerOptionsSetup(IServiceProvider services)
    {
        _services = services;
    }

    public void Configure(KestrelServerOptions options)
    {
        options.ApplicationServices = _services;
    }
}

public class KestrelServerWithStackOptions : KestrelServerOptions
{
    public TcpIpSystem TcpIp = LocalNet;
}

public class KestrelServerWithStack : KestrelServer
{
    public new KestrelServerWithStackOptions Options => (KestrelServerWithStackOptions)base.Options;

    public KestrelServerWithStack(IOptions<KestrelServerWithStackOptions> options, IConnectionListenerFactory transportFactory, ILoggerFactory loggerFactory)
        : base(options, transportFactory, loggerFactory)
    {
        KestrelStackTransportFactory factory = (KestrelStackTransportFactory)transportFactory;

        factory.SetServer(this);
    }
}

public static class HttpServerWithStackHelper
{
    public static IWebHostBuilder UseKestrelWithStack(this IWebHostBuilder hostBuilder, Action<KestrelServerWithStackOptions> options)
    {
        return hostBuilder.UseKestrelWithStack().ConfigureKestrelWithStack(options);
    }

    public static IWebHostBuilder UseKestrelWithStack(this IWebHostBuilder hostBuilder)
    {
        // From Microsoft.AspNetCore.Hosting.WebHostBuilderKestrelExtensions
        return hostBuilder.ConfigureServices(services =>
        {
                // Don't override an already-configured transport
                services.TryAddSingleton<IConnectionListenerFactory, KestrelStackTransportFactory>();

            services.AddTransient<IConfigureOptions<KestrelServerWithStackOptions>, KestrelServerOptionsSetup>();
            services.AddSingleton<IServer, KestrelServerWithStack>();
        });
    }

    public static IWebHostBuilder ConfigureKestrelWithStack(this IWebHostBuilder hostBuilder, Action<KestrelServerWithStackOptions> options)
    {
        return hostBuilder.ConfigureServices(services =>
        {
            services.Configure(options);
        });
    }

    public static IApplicationBuilder UseWebServerLogger(this IApplicationBuilder app)
    {
        return app.Use(async (context, next) =>
        {
            int httpResponseCode = 0;

            long tickStart = 0;
            long tickEnd = 0;

            string? contentsType = null;
            long? contentsLength = 0;

            try
            {
                tickStart = Time.Tick64;
                await next.Invoke();
                tickEnd = Time.Tick64;

                HttpResponse response = context.Response;

                httpResponseCode = response.StatusCode;

                contentsType = response.ContentType;
                contentsLength = response.ContentLength ?? 0;
            }
            catch
            {
                httpResponseCode = -1;
                tickEnd = Time.Tick64;
            }

            Exception? exception = HttpExceptionLoggerMiddleware.GetSavedExceptionFromContext(context);

            int processTime = (int)(tickEnd - tickStart);

            HttpRequest req = context.Request;
            ConnectionInfo conn = context.Connection;
            string? username = null;
            string? authtype = null;

            if (context.User?.Identity?.IsAuthenticated ?? false)
            {
                username = context.User?.Identity?.Name;
                authtype = context.User?.Identity?.AuthenticationType;
            }

            int maxLen = CoresConfig.KestrelWithStackSettings.MaxLogStringLen;

            WebServerLogData log = new WebServerLogData()
            {
                ConnectionId = conn.Id,
                LocalIP = conn.LocalIpAddress!._UnmapIPv4().ToString(),
                RemoteIP = conn.RemoteIpAddress!._UnmapIPv4().ToString(),
                LocalPort = conn.LocalPort,
                RemotePort = conn.RemotePort,

                Protocol = req.Protocol,
                Method = req.Method,
                Host = req.Host.ToString()._TruncStrEx(maxLen),
                Path = req.Path.ToString()._TruncStrEx(maxLen),
                QueryString = req.QueryString.ToString()._TruncStrEx(maxLen),
                Url = req.GetEncodedUrl()._TruncStrEx(maxLen),

                AuthUserName = username._TruncStrEx(maxLen),
                AuthType = authtype,

                ProcessTimeMsecs = processTime,
                ResponseCode = httpResponseCode,

                ContentsType = contentsType,
                ContentsLength = contentsLength,
            };

            var clientCert = await conn.GetClientCertificateAsync();
            if (clientCert != null)
            {
                log.ClientCert = clientCert.AsPkiCertificate().ToString();
            }

            log.AuthUserName = log.AuthUserName._NullIfEmpty();
            log.QueryString = log.QueryString._NullIfEmpty();

            if (context.Items.TryGetValue(BasicAuthMiddleware.BasicAuthResultItemName, out object? basicAuthResultObj))
            {
                if (basicAuthResultObj is ResultAndError<string> basicAuthRet)
                {
                    log.BasicAuthResult = basicAuthRet.IsOk;
                    log.BasicAuthUserName = basicAuthRet.Value;
                }
            }

            if (req.Headers.TryGetValue("User-Agent", out StringValues userAgentValue))
                log.UserAgent = userAgentValue.ToString()._TruncStrEx(maxLen);

            if (exception != null)
            {
                try
                {
                    string msg = $"Web Server Exception on {log.Method} {log.Url}\r\n{exception.ToString()}\r\n{log._ObjectToJson(compact: true)}\r\n------------------\r\n";

                        //msg._Debug();
                        msg._Error();
                }
                catch { }
            }

            log.Exception = exception?.ToString();

            log._PostAccessLog(LogTag.WebServer);
        });
    }
}

public class HttpEasyContextBox
{
    public HttpContext Context { get; }
    public RouteData RouteData { get; }
    public HttpRequest Request { get; }
    public HttpResponse Response { get; }
    public ConnectionInfo ConnInfo { get; }
    public WebMethods Method { get; }
    public CancellationToken CancelPerRequest { get; }     // HTTP コネクション単位のキャンセル指令が含まれる可能性がある Cancel
    public CancellationToken CancelService { get; }     // Web サーバーサービスが停止されようとする際のみ叩かれる Cancel
    public IPEndPoint RemoteEndpoint { get; }
    public IPEndPoint LocalEndpoint { get; }

    public string RemoteHostnameOrIpAddress { get; private set; }
    public bool IsRemoteHostnameResolved { get; private set; }

    public string PathAndQueryString { get; }
    public QueryStringList QueryStringList { get; }
    public Uri PathAndQueryStringUri { get; }

    public HttpEasyContextBox(HttpContext context, CancellationToken cancelService = default)
    {
        Context = context;
        RouteData = context.GetRouteData();
        Request = context.Request;
        Response = context.Response;
        ConnInfo = context.Connection;
        Method = Request.Method._ParseEnum(WebMethods.GET);
        CancelPerRequest = Request._GetRequestCancellationToken();
        CancelService = cancelService;
        RemoteEndpoint = new IPEndPoint(ConnInfo.RemoteIpAddress!._UnmapIPv4(), ConnInfo.RemotePort);
        LocalEndpoint = new IPEndPoint(ConnInfo.LocalIpAddress!._UnmapIPv4(), ConnInfo.LocalPort);

        this.IsRemoteHostnameResolved = false;
        this.RemoteHostnameOrIpAddress = RemoteEndpoint.Address.ToString();

        PathAndQueryString = Request._GetRequestPathAndQueryString();

        PathAndQueryString._ParseUrl(out Uri uri, out QueryStringList qs);
        this.PathAndQueryStringUri = uri;
        this.QueryStringList = qs;
    }

    public async Task TryResolveRemoteHostnameAsync(DnsResolver? resolver = null, CancellationToken cancel = default)
    {
        if (this.IsRemoteHostnameResolved)
        {
            return;
        }

        if (resolver == null) resolver = LocalNet.DnsResolver;

        string hostname = await resolver.GetHostNameOrIpAsync(this.RemoteEndpoint.Address, cancel);

        if (hostname._IsEmpty() == false)
        {
            this.RemoteHostnameOrIpAddress = hostname;
            this.IsRemoteHostnameResolved = true;
        }
    }
}


public delegate Task<HttpResult> HttpResultStandardRequestAsyncCallback(WebMethods method, string path, QueryStringList queryString, HttpContext context, RouteData routeData, IPEndPoint local, IPEndPoint remote, CancellationToken cancel = default);


public partial class HttpResult
{
    public static async Task EasyRequestHandler(HttpContext context, object? param, Func<HttpEasyContextBox, object?, Task<HttpResult>> callback, CancellationToken cancelService = default)
    {
        var box = context._GetHttpEasyContextBox(cancelService);

        try
        {
            await using (HttpResult result = await callback(box, param))
            {
                await box.Response._SendHttpResultAsync(result, box.CancelPerRequest);
            }
        }
        catch (Exception ex)
        {
            ex._Error();

            await using HttpResult errResult = new HttpStringResult("Error: " + ex.Message, statusCode: Consts.HttpStatusCodes.InternalServerError);

            await box.Response._SendHttpResultAsync(errResult, box.CancelPerRequest);
        }
    }

    public static RequestDelegate GetStandardRequestHandler(HttpResultStandardRequestAsyncCallback handler, CancellationToken cancelService = default)
    {
        return (context) => StandardRequestHandlerAsync(context, handler, cancelService);
    }

    static async Task StandardRequestHandlerAsync(HttpContext context, HttpResultStandardRequestAsyncCallback callback, CancellationToken cancelService = default)
    {
        var box = new HttpEasyContextBox(context, cancelService);

        try
        {
            await using (HttpResult result = await callback(box.Method, box.PathAndQueryStringUri.LocalPath, box.QueryStringList, context, box.RouteData, box.LocalEndpoint, box.RemoteEndpoint, box.CancelService))
            {
                await box.Response._SendHttpResultAsync(result, box.CancelPerRequest);
            }
        }
        catch (Exception ex)
        {
            ex._Error();

            await using HttpResult errResult = new HttpStringResult("Error: " + ex.Message, statusCode: Consts.HttpStatusCodes.InternalServerError);

            await box.Response._SendHttpResultAsync(errResult, box.CancelPerRequest);
        }
    }
}

#endif

