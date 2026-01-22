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

#pragma warning disable CA2235 // Mark all non-serializable fields

using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http.Extensions;
using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Net.Http.Headers;


using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

#if CORES_BASIC_HTTPSERVER
// ASP.NET Core 2.2 専用
using Microsoft.AspNetCore.Http.Internal;
#endif

namespace IPA.Cores.Basic;

// From: https://github.com/aspnet/BasicMiddleware/tree/2.1.1/src/Microsoft.AspNetCore.HttpsPolicy
// License: https://github.com/aspnet/BasicMiddleware/blob/2.1.1/LICENSE.txt
// 
// Copyright(c) .NET Foundation and Contributors
// All rights reserved.
// Licensed under the Apache License, Version 2.0 (the "License"); you may not use
// this file except in compliance with the License. You may obtain a copy of the
// License at
//    http://www.apache.org/licenses/LICENSE-2.0
// Unless required by applicable law or agreed to in writing, software distributed
// under the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR
// CONDITIONS OF ANY KIND, either express or implied. See the License for the
// specific language governing permissions and limitations under the License.

/// <summary>
/// Extension methods for the HttpsRedirection middleware.
/// </summary>
public static class EnforceHttpsHelper
{
    /// <summary>
    /// Adds HTTPS redirection services.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> for adding services.</param>
    /// <param name="configureOptions">A delegate to configure the <see cref="HttpsRedirectionOptions"/>.</param>
    /// <returns></returns>
    public static IServiceCollection AddEnforceHttps(this IServiceCollection services, Action<EnforceHttpsOptions> configureOptions)
    {
        if (services == null)
        {
            throw new ArgumentNullException(nameof(services));
        }
        if (configureOptions == null)
        {
            throw new ArgumentNullException(nameof(configureOptions));
        }
        services.Configure(configureOptions);
        return services;
    }

    /// <summary>
    /// Adds middleware for redirecting HTTP Requests to HTTPS.
    /// </summary>
    /// <param name="app">The <see cref="IApplicationBuilder"/> instance this method extends.</param>
    /// <returns>The <see cref="IApplicationBuilder"/> for HttpsRedirection.</returns>
    public static IApplicationBuilder UseEnforceHttps(this IApplicationBuilder app)
    {
        if (app == null)
        {
            throw new ArgumentNullException(nameof(app));
        }

        var serverAddressFeature = app.ServerFeatures.Get<IServerAddressesFeature>();
        if (serverAddressFeature != null)
        {
            app.UseMiddleware<EnforceHttpsMiddleware>(serverAddressFeature);
        }
        else
        {
            app.UseMiddleware<EnforceHttpsMiddleware>();
        }
        return app;
    }
}

[Serializable]
public class EnforceHttpsOptions
{
    public int RedirectStatusCode { get; set; } = 302;

    public List<string> ExcludePathPrefixList { get; set; } = new List<string>();

    public EnforceHttpsOptions()
    {
        this.ExcludePathPrefixList.Add("/.well-known/");
        this.ExcludePathPrefixList.Add("/Account");
    }
}

public class EnforceHttpsMiddleware
{
    readonly RequestDelegate Next;
    readonly IConfiguration Config;
    private bool PortEvaluated = false;
    private int? HttpsPort;
    private readonly int StatusCode;
    private readonly IServerAddressesFeature? _serverAddressesFeature;
    readonly EnforceHttpsOptions Options;

    public EnforceHttpsMiddleware(RequestDelegate next, IOptions<EnforceHttpsOptions> options, IConfiguration config, ILoggerFactory loggerFactory)
    {
        Next = next ?? throw new ArgumentNullException(nameof(next));
        Config = config ?? throw new ArgumentNullException(nameof(config));
        if (options == null)
        {
            throw new ArgumentNullException(nameof(options));
        }
        this.StatusCode = options.Value.RedirectStatusCode;
        this.Options = options.Value._CloneDeep();
    }

    public EnforceHttpsMiddleware(RequestDelegate next, IOptions<EnforceHttpsOptions> options, IConfiguration config, ILoggerFactory loggerFactory,
      IServerAddressesFeature serverAddressesFeature)
      : this(next, options, config, loggerFactory)
    {
        _serverAddressesFeature = serverAddressesFeature ?? throw new ArgumentNullException(nameof(serverAddressesFeature));
    }

    public Task Invoke(HttpContext context)
    {
        bool skip = false;
        int port = 0;
        if (context.Request.IsHttps || !TryGetHttpsPort(out port))
        {
            skip = true;
        }
        else
        {
            string path = context.Request.Path;
            if (path._IsFilled())
            {
                if (this.Options.ExcludePathPrefixList.Where(x => x._IsFilled() && path.StartsWith(x, StringComparison.OrdinalIgnoreCase)).Any())
                {
                    skip = true;
                }
            }
        }

        if (skip)
        {
            return Next(context);
        }
        var host = context.Request.Host;
        if (port != 443)
        {
            host = new HostString(host.Host, port);
        }
        else
        {
            host = new HostString(host.Host);
        }

        var request = context.Request;
        var redirectUrl = UriHelper.BuildAbsolute(
            "https",
            host,
            request.PathBase,
            request.Path,
            request.QueryString);

        context.Response.StatusCode = StatusCode;
        context.Response.Headers[HeaderNames.Location] = redirectUrl;

        return Task.CompletedTask;
    }

    private bool TryGetHttpsPort(out int port)
    {
        // The IServerAddressesFeature will not be ready until the middleware is Invoked,
        // Order for finding the HTTPS port:
        // 1. Set in the HttpsRedirectionOptions
        // 2. HTTPS_PORT environment variable
        // 3. IServerAddressesFeature
        // 4. Fail if not set

        port = -1;

        if (PortEvaluated)
        {
            port = HttpsPort ?? port;
            return HttpsPort.HasValue;
        }
        PortEvaluated = true;

        HttpsPort = Config.GetValue<int?>("HTTPS_PORT");
        //if (HttpsPort.HasValue)
        //{
        //    port = HttpsPort.Value;
        //    return true;
        //}

        if (_serverAddressesFeature == null)
        {
            return false;
        }

        int? httpsPort = null;
        foreach (var address in _serverAddressesFeature.Addresses)
        {
            var bindingAddress = BindingAddress.Parse(address);
            if (bindingAddress.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
            {
                // If we find multiple different https ports specified, throw
                if (httpsPort.HasValue && httpsPort != bindingAddress.Port)
                {
                    return false;
                }
                else
                {
                    httpsPort = bindingAddress.Port;
                    break;
                }
            }
        }

        if (httpsPort.HasValue)
        {
            HttpsPort = httpsPort;
            port = HttpsPort.Value;
            return true;
        }

        return false;
    }
}

#endif  // CORES_BASIC_WEBAPP
