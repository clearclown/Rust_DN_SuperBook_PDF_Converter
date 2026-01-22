// IPA Cores.NET
// 
// Copyright (c) 2019- IPA CyberLab.
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

// Author: Daiyuu Nobori
// CGI HTTP Server

#if CORES_BASIC_WEBAPP || CORES_BASIC_HTTPSERVER

#pragma warning disable CA2235 // Mark all non-serializable fields

using System;
using System.Buffers;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Serialization;
using System.Net;

using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Hosting;

#if CORES_BASIC_HTTPSERVER
// ASP.NET Core 3.0 用の型名を無理やり ASP.NET Core 2.2 でコンパイルするための型エイリアスの設定
using IWebHostEnvironment = Microsoft.AspNetCore.Hosting.IHostingEnvironment;
using IHostApplicationLifetime = Microsoft.AspNetCore.Hosting.IApplicationLifetime;
#endif

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

namespace IPA.Cores.Basic;

// CGI コンテキスト
public class CgiContext
{
    public readonly HttpRequest Request;
    public readonly RouteData RouteData;
    public readonly CancellationToken Cancel;

    public readonly string RequestPathAndQueryString;

    public readonly Uri Uri;
    public readonly QueryStringList QueryString;

    public readonly IPAddress ClientIpAddress;
    public readonly int ClientPort;

    public readonly IPAddress ServerIpAddress;
    public readonly int ServerPort;

    public CgiContext(HttpRequest request, RouteData routeData, CancellationToken cancel = default)
    {
        Request = request;
        RouteData = routeData;
        Cancel = cancel;

        var connection = Request.HttpContext.Connection;

        this.ClientIpAddress = connection.RemoteIpAddress!._UnmapIPv4();
        this.ClientPort = connection.RemotePort;

        this.ServerIpAddress = connection.LocalIpAddress!._UnmapIPv4();
        this.ServerPort = connection.LocalPort;

        this.RequestPathAndQueryString = request._GetRequestPathAndQueryString();

        this.RequestPathAndQueryString._ParseUrl(out this.Uri, out this.QueryString);
    }
}

public delegate Task<HttpResult> CgiActionAsync(CgiContext ctx);

// CGI アクション要素
public class CgiAction
{
    public readonly string Template;
    public readonly WebMethodBits Methods;
    public readonly CgiActionAsync ActionAsync;

    public CgiAction(string template, WebMethodBits methods, CgiActionAsync actionAsync)
    {
        Template = template;
        Methods = methods;
        ActionAsync = actionAsync;
    }
}

// CGI アクションリスト
public class CgiActionList
{
    public bool IsSealed { get; private set; } = false;

    readonly List<CgiAction> List = new List<CgiAction>();

    public void AddAction(string template, WebMethodBits method, CgiActionAsync actionAsync)
    {
        if (IsSealed) throw new CoresException("Sealed.");

        List.Add(new CgiAction(template, method, actionAsync));
    }

    public void AddToRouteBuilder(RouteBuilder rb, bool showDetailError)
    {
        this.IsSealed = true;

        foreach (var act in List)
        {
            foreach (var method in act.Methods._GetWebMethodListFromBits())
            {
                rb.MapVerb(method.ToString(), act.Template,
                    async (request, response, routeData) =>
                    {
                        CgiContext ctx = new CgiContext(request, routeData, request._GetRequestCancellationToken());

                        try
                        {
                            await using HttpResult result = await act.ActionAsync(ctx);

                            await response._SendHttpResultAsync(result, ctx.Cancel);
                        }
                        catch (Exception ex)
                        {
                            ex = ex._GetSingleException();

                            if (ex._IsCancelException() == false)
                            {
                                ex._Error();
                            }

                            string errorStr;

                            if (showDetailError == false)
                            {
                                errorStr = ex.Message;
                            }
                            else
                            {
                                errorStr = ex.ToString();
                            }

                            await using var errorResult = new HttpStringResult($"HTTP Status Code: 500\r\n" + errorStr, statusCode: 500);

                            try
                            {
                                await response._SendHttpResultAsync(errorResult, ctx.Cancel);
                            }
                            catch { }
                        }
                    });
            }
        }
    }
}

// CGI ハンドラベースクラス (このクラスを派生して自前のハンドラを実装します)
public abstract class CgiHandlerBase : AsyncService
{
    readonly CgiActionList ActionListNoAuth = new CgiActionList();
    readonly CgiActionList ActionListRequireAuth = new CgiActionList();

    public readonly Copenhagen<bool> ShowDetailError = new Copenhagen<bool>(false);

    public CgiHandlerBase()
    {
    }

    Once inited;

    void InitActionListInternal()
    {
        if (inited.IsFirstCall())
        {
            InitActionListImpl(this.ActionListNoAuth, this.ActionListRequireAuth);
        }
    }

    public void InsertActionListNoAuthToRouteBuilder(RouteBuilder rb)
    {
        InitActionListInternal();

        this.ActionListNoAuth.AddToRouteBuilder(rb, ShowDetailError);
    }

    public void InsertActionListReqAuthToRouteBuilder(RouteBuilder rb)
    {
        InitActionListInternal();

        this.ActionListRequireAuth.AddToRouteBuilder(rb, ShowDetailError);
    }

    protected abstract void InitActionListImpl(CgiActionList noAuth, CgiActionList reqAuth);

    public virtual void ConfigureImpl_BeforeHelper(HttpServerStartupConfig cfg, IApplicationBuilder app, IWebHostEnvironment env, IHostApplicationLifetime lifetime, RouteBuilder rb)
    {
    }

    public virtual void ConfigureImpl_AfterHelper(HttpServerStartupConfig cfg, IApplicationBuilder app, IWebHostEnvironment env, IHostApplicationLifetime lifetime, RouteBuilder rb)
    {
    }
}

// CGI 用簡易 HTTP サーバービルダー
public sealed class CgiHttpServerBuilder : HttpServerStartupBase
{
    CgiHttpServer CgiHttpServer => (CgiHttpServer)this.Param!;
    CgiHandlerBase Handler => CgiHttpServer.Handler;

    public static HttpServer<CgiHttpServerBuilder> StartServer(HttpServerOptions httpCfg, CgiHttpServer cgiHttpServer, CancellationToken cancel = default)
        => new HttpServer<CgiHttpServerBuilder>(httpCfg, cgiHttpServer, cancel);

    public CgiHttpServerBuilder(IConfiguration configuration) : base(configuration)
    {
    }

    protected override void ConfigureImpl_BeforeHelper(HttpServerStartupConfig cfg, IApplicationBuilder app, IWebHostEnvironment env, IHostApplicationLifetime lifetime)
    {
        RouteBuilder rb = new RouteBuilder(app);

        Handler.InsertActionListNoAuthToRouteBuilder(rb);

        Handler.ConfigureImpl_BeforeHelper(cfg, app, env, lifetime, rb);

        IRouter router = rb.Build();
        app.UseRouter(router);
    }

    protected override void ConfigureImpl_AfterHelper(HttpServerStartupConfig cfg, IApplicationBuilder app, IWebHostEnvironment env, IHostApplicationLifetime lifetime)
    {
        RouteBuilder rb = new RouteBuilder(app);

        Handler.InsertActionListReqAuthToRouteBuilder(rb);

        Handler.ConfigureImpl_AfterHelper(cfg, app, env, lifetime, rb);

        IRouter router = rb.Build();
        app.UseRouter(router);
    }
}

// CGI 用簡易 HTTP サーバー
public sealed class CgiHttpServer : AsyncService
{
    readonly HttpServer<CgiHttpServerBuilder> HttpSvr;
    public readonly CgiHandlerBase Handler;

    readonly bool AutoDisposeHandler;

    public CgiHttpServer(CgiHandlerBase handler, HttpServerOptions options, bool autoDisposeHandler = false)
    {
        try
        {
            this.Handler = handler;
            this.AutoDisposeHandler = autoDisposeHandler;

            this.Handler.ShowDetailError.TrySet(options.ShowDetailError);

            HttpSvr = CgiHttpServerBuilder.StartServer(options, this);
        }
        catch (Exception ex)
        {
            this._DisposeSafe(ex);
            throw;
        }
    }

    protected override async Task CleanupImplAsync(Exception? ex)
    {
        try
        {
            await HttpSvr._DisposeSafeAsync(ex);

            if (this.AutoDisposeHandler)
            {
                await Handler._DisposeSafeAsync(ex);
            }
        }
        finally
        {
            await base.CleanupImplAsync(ex);
        }
    }
}

#endif

