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
using System.Text;
using System.IO;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Http.Extensions;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using System.Collections.Generic;
using Microsoft.Extensions.Primitives;
using System.Linq;
using System.Net;

namespace IPA.Cores.Helper.Basic;

public static class WebServerHelper
{
    public static CancellationToken _GetRequestCancellationToken(this HttpResponse h) => h.HttpContext.RequestAborted;
    public static CancellationToken _GetRequestCancellationToken(this HttpRequest h) => h.HttpContext.RequestAborted;
    public static CancellationToken _GetRequestCancellationToken(this HttpContext h) => h.RequestAborted;

    public static bool _IsCrossSiteFetch(this HttpRequest r)
    {
        return r.Headers._GetStrFirst("sec-fetch-site")._IsSamei("cross-site");
    }

    public static bool _IsCrossSiteReferer(this HttpRequest r)
    {
        string host1 = r.Host.Host;
        string referer = r.Headers.Referer.ToString();
        if (host1._IsFilled() && referer._IsFilled())
        {
            if (r.Headers.Referer.ToString()._TryParseUrl(out Uri? uri, out _))
            {
                string host2 = uri!.Host;

                if (host2._IsFilled())
                {
                    if (host1._IsSameiTrim(host2) == false)
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

#if CORES_BASIC_WEBAPP
    public static CancellationToken _GetRequestCancellationToken(this Microsoft.AspNetCore.Mvc.Controller c) => c.HttpContext._GetRequestCancellationToken();
#endif

    public static async Task _SendHttpResultAsync(this HttpResponse h, HttpResult result, CancellationToken cancel = default)
    {
        if (result.Stream.CanSeek)
        {
            result.Stream.Seek(result.Offset, SeekOrigin.Begin);
        }

        await h._SendStreamContentsAsync(result.Stream, result.Length, result.ContentType, cancel, result.PreData, result.PostData, result.StatusCode, result.AdditionalHeaders);
    }

    public static Task _SendRedirectAsync(this HttpResponse h, string targetUrl, int statusCode = Consts.HttpStatusCodes.Found, CancellationToken cancel = default)
    {
        string htmlBody = $@"<html><head><title>Object moved</title></head><body>
<h2>Object moved to <a href=""{targetUrl}"">here</a>.</h2>
</body></html>
";

        h.Headers.Location = targetUrl;
        h.Headers.CacheControl = "private";

        return _SendStringContentsAsync(h, htmlBody, Consts.MimeTypes.HtmlUtf8, cancel: cancel, statusCode: statusCode);
    }

    public static Task _SendStringContentsAsync(this HttpResponse h, string body, string contentsType = Consts.MimeTypes.TextUtf8, Encoding? encoding = null, CancellationToken cancel = default(CancellationToken), int statusCode = Consts.HttpStatusCodes.Ok, CrlfStyle normalizeCrlf = CrlfStyle.NoChange)
    {
        body = body._NormalizeCrlf(normalizeCrlf);

        if (encoding == null) encoding = Str.Utf8Encoding;
        byte[] ret_data = encoding.GetBytes(body);

        h.ContentType = contentsType;
        h.ContentLength = ret_data.Length;
        h.StatusCode = statusCode;

        return h.Body.WriteAsync(ret_data, 0, ret_data.Length, cancel);
    }

    public static async Task<string> _RecvStringContentsAsync(this HttpRequest h, int maxRequestBodyLen = int.MaxValue, Encoding? encoding = null, CancellationToken cancel = default(CancellationToken))
    {
        if (encoding == null) encoding = Str.Utf8Encoding;

        return (await h.Body._ReadToEndAsync(maxRequestBodyLen, cancel))._GetString_UTF8();
    }

    public static async Task _SendFileContentsAsync(this HttpResponse h, FileBase file, long offset, long? count, string contentsType = Consts.MimeTypes.OctetStream, CancellationToken cancel = default)
    {
        CheckStreamRange(offset, count, file.Size);

        await using (Stream srcStream = file.GetStream(false))
        {
            if (offset > 0)
            {
                srcStream.Seek(offset, SeekOrigin.Begin);
            }

            await h._SendStreamContentsAsync(srcStream, count, contentsType, cancel);
        }
    }

    public static async Task _SendStreamContentsAsync(this HttpResponse h, Stream sourceStream, long? count, string? contentsType = Consts.MimeTypes.OctetStream, CancellationToken cancel = default,
        ReadOnlyMemory<byte> preData = default, ReadOnlyMemory<byte> postData = default, int statusCode = Consts.HttpStatusCodes.Ok, IReadOnlyList<KeyValuePair<string, string>>? additionalHeaders = null)
    {
        h.ContentType = contentsType._FilledOrDefault(Consts.MimeTypes.OctetStream);
        h.ContentLength = count + preData.Length + postData.Length;
        h.StatusCode = statusCode;

        if (additionalHeaders != null)
        {
            StrDictionary<List<string>> tmp = new StrDictionary<List<string>>(StrCmpi);
            additionalHeaders._DoForEach(x =>
            {
                tmp._GetOrNew(x.Key, () => new List<string>()).Add(x.Value);
            });

            foreach (var kv in tmp)
            {
                h.Headers.Add(kv.Key, new StringValues(kv.Value.ToArray()));
            }
        }

        if (preData.IsEmpty == false) await h.Body.WriteAsync(preData, cancel);

        await StreamCopyOperation.CopyToAsync(sourceStream, h.Body, count, cancel);

        if (postData.IsEmpty == false) await h.Body.WriteAsync(postData, cancel);
    }

    // From: https://github.com/aspnet/HttpAbstractions/blob/31a836c9f35987c736161bf6e3f763517da8d504/src/Microsoft.AspNetCore.Http.Extensions/SendFileResponseExtensions.cs
    // Copyright (c) .NET Foundation. All rights reserved.
    // Licensed under the Apache License, Version 2.0.
    // License: https://github.com/aspnet/HttpAbstractions/blob/31a836c9f35987c736161bf6e3f763517da8d504/LICENSE.txt
    static void CheckStreamRange(long offset, long? count, long fileLength)
    {
        if (offset < 0 || offset > fileLength)
        {
            throw new ArgumentOutOfRangeException(nameof(offset), offset, string.Empty);
        }
        if (count.HasValue &&
            (count.GetValueOrDefault() < 0 || count.GetValueOrDefault() > fileLength - offset))
        {
            throw new ArgumentOutOfRangeException(nameof(count), count, string.Empty);
        }
    }

    public static string _GetRequestPathAndQueryString(this HttpRequest r)
    {
        string path = r.Path.ToString()._NonNull();
        string qs = r.QueryString.ToString()._NonNull();

        if (qs._IsNullOrZeroLen() == false)
        {
            return path + qs;
        }
        else
        {
            return path;
        }
    }

    public static void _GetRequestPathAndQueryString(this HttpRequest r, out string requestPath, out string queryString)
    {
        string path = r.Path.ToString()._NonNull();
        string qs = r.QueryString.ToString()._NonNull();

        if (qs._IsNullOrZeroLen() == false)
        {
            requestPath = path;
            queryString = qs;
        }
        else
        {
            requestPath = path;
            queryString = "";
        }
    }

    public static string _GetQueryStringFirst(this HttpRequest r, string key, string defaultStr = "", StringComparison comparison = StringComparison.OrdinalIgnoreCase, bool autoTrim = true)
        => _GetQueryStringFirst(r.Query, key, defaultStr, comparison, autoTrim);

    public static string _GetQueryStringFirst(this IQueryCollection d, string key, string defaultStr = "", StringComparison comparison = StringComparison.OrdinalIgnoreCase, bool autoTrim = true)
    {
        if (key._IsEmpty()) throw new ArgumentNullException(nameof(key));

        if (d == null) return defaultStr;

        var matchList = d.Where(x => x.Key._IsSameTrim(key, comparison));
        if (matchList.Any() == false) return defaultStr;

        StringValues values = matchList.First().Value;

        string? ret = values.Where(x => (autoTrim ? x._IsFilled() : x != null)).FirstOrDefault();

        if (ret._IsEmpty())
        {
            return defaultStr;
        }

        if (autoTrim)
        {
            ret = ret._NonNullTrim();
        }

        return ret;
    }

    public static void _MapGetStandardHandler(this RouteBuilder routeBuilder, string template, HttpResultStandardRequestAsyncCallback handler, CancellationToken cancelService = default)
    {
        routeBuilder.MapGet(template, HttpResult.GetStandardRequestHandler(handler, cancelService));
    }

    public static void _MapPostStandardHandler(this RouteBuilder routeBuilder, string template, HttpResultStandardRequestAsyncCallback handler, CancellationToken cancelService = default)
    {
        routeBuilder.MapPost(template, HttpResult.GetStandardRequestHandler(handler, cancelService));
    }

    public static HttpEasyContextBox _GetHttpEasyContextBox(this HttpContext context, CancellationToken cancelService = default)
        => new HttpEasyContextBox(context, cancelService);
}

#endif // CORES_BASIC_WEBAPP

