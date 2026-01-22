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
// 開発中のクラスの一時置き場

#if true

#pragma warning disable CA2235 // Mark all non-serializable fields

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Runtime.CompilerServices;

using IPA.Cores.Basic;
using IPA.Cores.Basic.DnsLib;
using IPA.Cores.Basic.Internal;
using IPA.Cores.Basic.HttpClientCore;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using Microsoft.AspNetCore.Server.IIS.Core;
//using Microsoft.EntityFrameworkCore.Query.Internal;
using System.Net.Sockets;

using Newtonsoft.Json;
using System.Data;
using System.Reflection;
using Newtonsoft.Json.Converters;

using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Hosting;

#if CORES_BASIC_HTTPSERVER
// ASP.NET Core 3.0 用の型名を無理やり ASP.NET Core 2.2 でコンパイルするための型エイリアスの設定
using IWebHostEnvironment = Microsoft.AspNetCore.Hosting.IHostingEnvironment;
using IHostApplicationLifetime = Microsoft.AspNetCore.Hosting.IApplicationLifetime;
#endif

namespace IPA.Cores.Basic;



public class MultiPartItem
{
    public string ContentType = "";
    public string ContentDisposition = "";
    public StrDictionary<List<string>> Headers = new StrDictionary<List<string>>(StrCmpi);
    public Memory<byte> Data;
}

public class MultiPartBody
{
    public string BoundaryString = Str.NewUid("MP", '_');
    public List<MultiPartItem> ItemList = new List<MultiPartItem>();

    public Memory<byte> Build()
    {
        MultipartContent content = new MultipartContent("test", this.BoundaryString);

        foreach (var item in this.ItemList)
        {
            ByteArrayContent c = new ByteArrayContent(item.Data.ToArray());

            if (item.ContentDisposition._IsFilled())
            {
                c.Headers.Add("Content-Disposition", item.ContentDisposition);
            }
            if (item.ContentType._IsFilled())
            {
                c.Headers.Add("Content-Type", item.ContentType);
            }
            foreach (var kv in item.Headers)
            {
                c.Headers.Add(kv.Key, kv.Value);
            }

            content.Add(c);
        }

        MemoryStream ms = new MemoryStream();

        content.CopyToAsync(ms);

        return ms.ToArray();
    }

    public static MultiPartBody TryParse(ReadOnlySpan<byte> data, string boundary)
    {
        return TryParse(data._ToMemoryStream(), boundary)._GetResult();
    }

    public static async Task<MultiPartBody> TryParse(Stream st, string boundary, CancellationToken cancel = default)
    {
        if (boundary._IsEmpty()) throw new CoresException("boundary is empty");

        MultiPartBody ret = new MultiPartBody();
        ret.BoundaryString = boundary;

        MultipartReader reader = new MultipartReader(boundary, st);

        reader.HeadersCountLimit = int.MaxValue;
        reader.HeadersLengthLimit = int.MaxValue;
        reader.BodyLengthLimit = int.MaxValue;

        // hack: bug fix
        var rw = reader._GetFieldReaderWriter(true);
        var currentStream = rw.GetValue(reader, "_currentStream");
        currentStream!._PrivateSet("LengthLimit", (long?)int.MaxValue);

        while (true)
        {
            var section = await reader.ReadNextSectionAsync(cancel);

            if (section == null)
            {
                break;
            }

            MultiPartItem item = new MultiPartItem
            {
                Data = await section.Body._ReadToEndAsync(cancel: cancel),
                ContentDisposition = section.ContentDisposition._NonNull(),
                ContentType = section.ContentType._NonNull(),
            };

            if (section.Headers != null)
            {
                foreach (var kv in section.Headers)
                {
                    if (kv.Key._IsSamei("Content-Disposition") || kv.Key._IsSamei("Content-Type")) { }
                    else
                    {
                        foreach (var value in kv.Value)
                        {
                            item.Headers._GetOrNew(kv.Key, () => new List<string>()).Add(value);
                        }
                    }
                }
            }

            ret.ItemList.Add(item);
        }

        return ret;
    }
}

public class EasyReverseProxyFilterContext
{
    public HttpRequest Request = null!;
    public HttpResponse Response = null!;
    public string Url = "";
    public WebMethods Method;
    public Memory<byte> PostData;
    public string PostDataContentType = "";
    public MediaTypeHeaderValue ParsedPostDataContentType = null!;
}

// リバースプロキシハンドラベースクラス (このクラスを派生して自前のハンドラを実装します)
public class EasyReverseProxyHandler : AsyncService
{
    public virtual Task FilterAsync(EasyReverseProxyFilterContext context, CancellationToken cancel = default) => Task.CompletedTask;
}

// リバースプロキシ用 HTTP サーバービルダ
public sealed class EasyReverseProxyHttpServerBuilder : HttpServerStartupBase
{
    EasyReverseProxyHttpServer HttpServer => (EasyReverseProxyHttpServer)this.Param!;
    EasyReverseProxyHandler Handler => HttpServer.Handler;
    EasyReverseProxyHttpServerSettings Settings => HttpServer.Settings;

    public static HttpServer<EasyReverseProxyHttpServerBuilder> StartServer(HttpServerOptions httpCfg, EasyReverseProxyHttpServer httpServer, CancellationToken cancel = default)
        => new HttpServer<EasyReverseProxyHttpServerBuilder>(httpCfg, httpServer, cancel);

    public EasyReverseProxyHttpServerBuilder(IConfiguration configuration) : base(configuration)
    {
    }

    public async Task RequestHandlerAsync(HttpRequest request, HttpResponse response, RouteData routeData)
    {
        try
        {
            var cancel = request._GetRequestCancellationToken();

            if (IPAddress.TryParse(request.Host.Host, out _))
            {
                await response._SendStringContentsAsync("Invalid request", statusCode: Consts.HttpStatusCodes.InternalServerError);
                return;
            }

            WebMethods method = request.Method._ParseEnum(WebMethods.GET);

            string url = request.IsHttps ? "https://" : "http://";

            url += request.Host.Host;

            if (request.Host.Port == null || ((request.IsHttps && request.Host.Port == 443) || (request.IsHttps == false && request.Host.Port == 80))) { }
            else
            {
                url += ":" + request.Host.Port.ToString();
            }

            url += request._GetRequestPathAndQueryString();

            byte[] postData = new byte[0];

            if (method == WebMethods.POST || method == WebMethods.PUT)
            {
                postData = await request.Body._ReadToEndAsync(cancel: cancel);
            }

            await using WebApi api = new WebApi(Settings.WebOptions);

            foreach (var header in request.Headers)
            {
                if (header.Key._InStri(":") == false && header.Key._IsSamei("content-length") == false && header.Key._IsSamei("content-type") == false)
                {
                    foreach (var value in header.Value)
                    {
                        api.AddHeader(header.Key, value, true);
                    }
                }
            }

            WebRet ret;

            $"{method.ToString()} ({postData.Length._ToString3()} bytes) {url}"._Print();

            string contentTypeStr = request.ContentType._FilledOrDefault(Consts.MimeTypes.OctetStream);

            EasyReverseProxyFilterContext context = new EasyReverseProxyFilterContext
            {
                Method = method,
                Url = url,
                PostData = postData,
                PostDataContentType = contentTypeStr,
                ParsedPostDataContentType = WebApi.ParseContentTypeStr(contentTypeStr),
                Request = request,
                Response = response,
            };

            await Handler.FilterAsync(context, cancel);

            if (context.Method == WebMethods.POST || context.Method == WebMethods.PUT)
            {
                ret = await api.SimplePostDataAsync(context.Url, context.PostData.ToArray(), cancel, context.PostDataContentType);
            }
            else
            {
                ret = await api.SimpleQueryAsync(context.Method, context.Url, cancel);
            }

            List<KeyValuePair<string, string>> headers = new List<KeyValuePair<string, string>>();

            foreach (var header in ret.Headers)
            {
                foreach (var value in header.Value)
                {
                    headers.Add(new KeyValuePair<string, string>(header.Key, value));
                }
            }

            await using HttpMemoryResult result = new HttpMemoryResult(ret.Data, ret.ContentType, (int)ret.StatusCode, headers);

            await response._SendHttpResultAsync(result, cancel);
        }
        catch (Exception ex)
        {
            string err = ex.ToString();

            ex._Error();

            await response._SendStringContentsAsync(err, statusCode: Consts.HttpStatusCodes.InternalServerError);
        }
    }

    protected override void ConfigureImpl_BeforeHelper(HttpServerStartupConfig cfg, IApplicationBuilder app, IWebHostEnvironment env, IHostApplicationLifetime lifetime)
    {
        RouteBuilder rb = new RouteBuilder(app);

        IRouter router = rb.Build();
        app.UseRouter(router);
    }

    protected override void ConfigureImpl_AfterHelper(HttpServerStartupConfig cfg, IApplicationBuilder app, IWebHostEnvironment env, IHostApplicationLifetime lifetime)
    {
        RouteBuilder rb = new RouteBuilder(app);

        string template = "{*path}";

        rb.MapGet(template, RequestHandlerAsync);
        rb.MapPost(template, RequestHandlerAsync);
        rb.MapDelete(template, RequestHandlerAsync);

        IRouter router = rb.Build();
        app.UseRouter(router);
    }
}

public class EasyReverseProxyHttpServerSettings
{
    public WebApiOptions WebOptions = new WebApiOptions(new WebApiSettings
    {
        AllowAutoRedirect = false,
        MaxRecvSize = 1_000_000_000,
        DoNotThrowHttpResultError = true,
        SslAcceptAnyCerts = true,
    }, doNotUseTcpStack: true);
}

// リバースプロキシサーバー
public sealed class EasyReverseProxyHttpServer : AsyncService
{
    readonly HttpServer<EasyReverseProxyHttpServerBuilder> HttpSvr;
    public readonly EasyReverseProxyHandler Handler;
    public readonly EasyReverseProxyHttpServerSettings Settings;

    readonly bool AutoDisposeHandler;

    public EasyReverseProxyHttpServer(EasyReverseProxyHandler handler, HttpServerOptions options, EasyReverseProxyHttpServerSettings settings, bool autoDisposeHandler = false)
    {
        try
        {
            this.Handler = handler;
            this.AutoDisposeHandler = autoDisposeHandler;
            this.Settings = settings;

            HttpSvr = EasyReverseProxyHttpServerBuilder.StartServer(options, this);
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

