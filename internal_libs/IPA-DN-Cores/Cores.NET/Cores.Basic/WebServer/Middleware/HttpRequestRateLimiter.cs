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

// Description

#if CORES_BASIC_WEBAPP || CORES_BASIC_HTTPSERVER

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;
using System.Net;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Hosting.Server.Features;
//using Microsoft.AspNetCore.Http.Internal;
using Microsoft.Net.Http.Headers;
using System.Net.Sockets;

namespace IPA.Cores.Basic;

// サポートされているハッシュ型を示す Interface
public interface IHttpRequestRateLimiterHashKey
{ }

// Factory メソッドを兼ねた、サポートされているハッシュ型のリスト
public static partial class HttpRequestRateLimiterHashKeys
{
    public class SrcIPAddress : HashKeys.SingleIPAddress, IHttpRequestRateLimiterHashKey
    {
        public SrcIPAddress(IPAddress address) : base(address) { }
    }

    // Factory メソッド
    public static IHttpRequestRateLimiterHashKey? CreateFromHttpContext<TKey>(HttpContext context, HttpRequestRateLimiterOptions<TKey> options) where TKey : IHttpRequestRateLimiterHashKey
    {
        // Src IP の処理
        IPAddress srcIp = context.Connection.RemoteIpAddress!._UnmapIPv4();

        if (options.SrcIPExcludeLocalNetwork)
        {
            // ローカルネットワークを除外する
            if (srcIp._GetIPAddressType()._IsLocalNetwork())
            {
                return null;
            }
        }

        // サブネットマスクの AND をする
        if (srcIp.AddressFamily == AddressFamily.InterNetwork)
        {
            // IPv4
            srcIp = IPUtil.IPAnd(srcIp, IPUtil.IntToSubnetMask4(options.SrcIPv4SubnetLength));
        }
        else
        {
            // IPv6
            srcIp = IPUtil.IPAnd(srcIp, IPUtil.IntToSubnetMask6(options.SrcIPv6SubnetLength));
        }

        if (typeof(TKey) == typeof(SrcIPAddress))
        {
            if (srcIp != null)
                return new SrcIPAddress(srcIp);
        }

        return null;
    }
}

// 拡張メソッド
public static class HttpRequestRateLimiterHelper
{
    public static IServiceCollection AddHttpRequestRateLimiter<TKey>(this IServiceCollection services, Action<HttpRequestRateLimiterOptions<TKey>> configureOptions)
         where TKey : IHttpRequestRateLimiterHashKey
    {
        if (services == null) throw new ArgumentNullException(nameof(services));
        if (configureOptions == null) throw new ArgumentNullException(nameof(configureOptions));
        services.Configure(configureOptions);
        return services;
    }

    public static IApplicationBuilder UseHttpRequestRateLimiter<TKey>(this IApplicationBuilder app) where TKey : IHttpRequestRateLimiterHashKey
    {
        if (app == null) throw new ArgumentNullException(nameof(app));

        app.UseMiddleware<HttpRequestRateLimiterMiddleware<TKey>>();

        return app;
    }
}

public class HttpRequestRateLimiterOptions<TKey> where TKey : IHttpRequestRateLimiterHashKey
{
    public int SrcIPv4SubnetLength { get; set; } = Consts.RateLimiter.DefaultSrcIPv4SubnetLength;
    public int SrcIPv6SubnetLength { get; set; } = Consts.RateLimiter.DefaultSrcIPv6SubnetLength;
    public bool SrcIPExcludeLocalNetwork { get; set; } = true;

    public bool EnableRateLimiter { get; set; } = true;
    public RateLimiterOptions RateLimiterOptions { get; set; }
        = new RateLimiterOptions(
            burst: Consts.RateLimiter.DefaultBurst,
            limitPerSecond: Consts.RateLimiter.DefaultLimitPerSecond,
            expiresMsec: Consts.RateLimiter.DefaultExpiresMsec,
            mode: RateLimiterMode.Penalty,
            maxEntries: Consts.RateLimiter.DefaultMaxEntries,
            gcIntervalMsec: Consts.RateLimiter.DefaultGcIntervalMsec);

    public int MaxConcurrentRequestsPerSrcSubnet { get; set; } = Consts.RateLimiter.DefaultMaxConcurrentRequestsPerSrcSubnet;
}

public class HttpRequestRateLimiterMiddleware<TKey> where TKey : IHttpRequestRateLimiterHashKey
{
    readonly RequestDelegate Next;
    readonly IConfiguration Config;
    readonly HttpRequestRateLimiterOptions<TKey> Options;

    public RateLimiterOptions RateLimiterOptions => Options.RateLimiterOptions;

    readonly RateLimiter<TKey> RateLimiter;
    readonly ConcurrentLimiter<TKey> ConcurrentLimiter;

    public HttpRequestRateLimiterMiddleware(RequestDelegate next, IOptions<HttpRequestRateLimiterOptions<TKey>> options, IConfiguration config, ILoggerFactory loggerFactory)
    {
        Next = next ?? throw new ArgumentNullException(nameof(next));
        Config = config ?? throw new ArgumentNullException(nameof(config));
        Options = options?.Value ?? throw new ArgumentNullException(nameof(options));

        RateLimiter = new RateLimiter<TKey>(RateLimiterOptions);
        ConcurrentLimiter = new ConcurrentLimiter<TKey>(Options.MaxConcurrentRequestsPerSrcSubnet);
    }

    public async Task Invoke(HttpContext context)
    {
        // context をもとに hashkey を生成
        IHttpRequestRateLimiterHashKey? hashKey = HttpRequestRateLimiterHashKeys.CreateFromHttpContext<TKey>(context, Options);

        if (hashKey == null)
        {
            // Hash key の作成に失敗した場合は無条件で流入を許可する
            await Next(context);
            return;
        }

        // 流入量検査
        bool ok = (Options.EnableRateLimiter == false) || this.RateLimiter.TryInput(hashKey, out _);

        if (ok) // 流量 OK
        {
            // 同時接続数検査
            if (ConcurrentLimiter.TryEnter(hashKey, out _))
            {
                try
                {
                    await Next(context);
                }
                finally
                {
                    // 同時接続数減算
                    ConcurrentLimiter.Exit(hashKey, out _);
                }
            }
            else
            {
                // 同時接続数制限されたのでエラーを返す
                context.Response.StatusCode = Consts.HttpStatusCodes.TooManyRequests;
                await context.Response.WriteAsync($"{Consts.HttpStatusCodes.TooManyRequests} Too many concurrent requests", context.RequestAborted);
            }
        }
        else
        {
            // 流量制限されたのでエラーを返す
            context.Response.StatusCode = Consts.HttpStatusCodes.TooManyRequests;
            await context.Response.WriteAsync($"{Consts.HttpStatusCodes.TooManyRequests} Too many requests", context.RequestAborted);
        }
    }
}

#endif

