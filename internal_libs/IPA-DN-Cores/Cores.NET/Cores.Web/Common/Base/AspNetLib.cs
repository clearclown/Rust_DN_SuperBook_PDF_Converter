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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.Extensions.FileProviders;
using Microsoft.AspNetCore.Mvc.Razor.RuntimeCompilation;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

using IPA.Cores.Web;
using IPA.Cores.Helper.Web;
using static IPA.Cores.Globals.Web;
using System.Reflection;

namespace IPA.Cores.Web;

// AspNetLib の機能を識別する bit スイッチ。各 Controller クラスの [AspNetLibFeature] 属性としてメンバを記述すること
[Flags]
public enum AspNetLibFeatures : long
{
    None = 0,
    Any = 1,
    EasyCookieAuth = 2,
    LogBrowser = 4,
}

// [AspNetLibFeature] 属性の実装
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
public class AspNetLibFeatureAttribute : Attribute
{
    public AspNetLibFeatures FeatureSwitch { get; }

    public AspNetLibFeatureAttribute(AspNetLibFeatures featureSwitch = AspNetLibFeatures.None)
    {
        this.FeatureSwitch = featureSwitch;
    }
}

// AspNetLib から必要なクラスだけを ASP.NET MVC に登録する処理の実装
public class AspNetLibControllersProvider : IApplicationFeatureProvider<ControllerFeature>
{
    public AspNetLib Lib { get; }

    public AspNetLibControllersProvider(AspNetLib lib)
    {
        this.Lib = lib;
    }

    public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
    {
        Assembly asm = Lib.ThisAssembly;

        // AspNetLib に含まれているすべてのクラスに関して、AspNetLibFeatureAttribute が付いているものでかつビットが一致するもののみを追加する
        asm.GetTypes()
            .Where(x => Lib.EnabledFeatures.Bit(x.GetCustomAttribute<AspNetLibFeatureAttribute>()?.FeatureSwitch ?? (AspNetLibFeatures)long.MaxValue))
            ._DoForEach(x => feature.Controllers.Add(x.GetTypeInfo()));
    }
}

public class AspNetLib : IDisposable
{
    public readonly Assembly ThisAssembly = typeof(AspNetLib).Assembly;

    static readonly string LibSourceCodeSampleFileName = Dbg.GetCallerSourceCodeFilePath();
    public static readonly string LibRootFullPath = Lfs.DetermineRootPathWithMarkerFile(LibSourceCodeSampleFileName, Consts.FileNames.RootMarker_Library_CoresWeb)._NullCheck();

    public AspNetLibFeatures EnabledFeatures { get; }

    LogBrowser? LogBrowser = null;

    public AspNetLib(IConfiguration configuration, AspNetLibFeatures features)
    {
        this.EnabledFeatures = features | AspNetLibFeatures.Any;
    }

    public readonly ResourceFileSystem AspNetResFs = ResourceFileSystem.CreateOrGet(
        new AssemblyWithSourceInfo(typeof(AspNetLib), new SourceCodePathAndMarkerFileName(LibSourceCodeSampleFileName, Consts.FileNames.RootMarker_Library_CoresWeb)));

    public void ConfigureServices(HttpServerStartupHelper helper, IServiceCollection services)
    {
    }

    public void SetupLogBrowser(IServiceCollection services, LogBrowserOptions options)
    {
        if (this.EnabledFeatures.Bit(AspNetLibFeatures.LogBrowser) == false)
            throw new ApplicationException(nameof(AspNetLibFeatures.LogBrowser) + " is not enabled.");

        if (LogBrowser != null) throw new ApplicationException("SetupLogBrowser is already called.");

        LogBrowser = new LogBrowser(options, Consts.UrlPaths.LogBrowserMvcPath);

        services.AddSingleton(this.LogBrowser);
    }

    public void Configure(HttpServerStartupHelper helper, IApplicationBuilder app, IWebHostEnvironment env)
    {
        // Embedded resource of the assembly
        helper.AddStaticFileProvider(AspNetResFs.CreateEmbeddedAndPhysicalFileProviders("/"));
    }

    public IMvcBuilder ConfigureAspNetLibMvc(IMvcBuilder mvc)
    {
        // MVC の設定
        mvc = mvc.AddViewOptions(opt => opt.HtmlHelperOptions.ClientValidationEnabled = false);

        if (GlobalDaemonStateManager.EnableAspNetEnableRazorRuntimeCompilation)
        {
            // Razor 再コンパイルを有効化
            mvc = mvc.AddRazorRuntimeCompilation(opt =>
            {
                    //以下を追加すると重くなるので追加しないようにした。その代わり Cores.Web の View をいじる場合は再コンパイルが必要
                    //if (AspNetLib.LibRootFullPath._IsFilled())
                    //    opt.FileProviders.Add(new PhysicalFileProvider(AspNetLib.LibRootFullPath));
                });
        }

        // この AspNetLib アセンブリのすべてのコントローラをデフォルト読み込み対象から除外する
        mvc = mvc.ConfigureApplicationPartManager(apm =>
        {
            List<ApplicationPart> removesList = new List<ApplicationPart>();

            apm.ApplicationParts.OfType<AssemblyPart>().Where(x => x.Assembly.Equals(ThisAssembly))._DoForEach(x => removesList.Add(x));

            removesList.ForEach(x => apm.ApplicationParts.Remove(x));

            apm.FeatureProviders.Add(new AspNetLibControllersProvider(this));
        });

        return mvc;
    }

    public void Dispose() { this.Dispose(true); GC.SuppressFinalize(this); }
    Once DisposeFlag;
    protected virtual void Dispose(bool disposing)
    {
        if (!disposing || DisposeFlag.IsFirstCall() == false) return;

        LogBrowser._DisposeSafe();
    }
}
