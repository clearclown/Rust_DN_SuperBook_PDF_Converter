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
// Description

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

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

using IPA.Cores.Web;
using IPA.Cores.Helper.Web;
using static IPA.Cores.Globals.Web;

using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.DependencyInjection;

namespace IPA.Cores.Web
{
    public class AspPageJavaScriptInitContext
    {
        public string LanguageKey { get; }

        public AspPageJavaScriptInitContext(string languageKey)
        {
            this.LanguageKey = languageKey;
        }
    }

    public class AspPageContext
    {
        // タイトルのベース文字列 (共通的な Web サイト名など)
        public virtual string SiteName { get; set; } = "WebSite";

        // タイトル文字列
        string? TitleInternal;

        // タイトル文字列の取得と設定
        public virtual string Title
        {
            get => TitleInternal._NonNullTrim();
            set => TitleInternal = value;
        }

        // フルタイトル文字列の取得
        public string FullTitle => GenerateFullTitleImpl();

        // フルタイトル文字列の取得 (派生クラスで挙動変更可能)
        protected virtual string GenerateFullTitleImpl()
        {
            if (this.Title._IsFilled())
            {
                return $"{this.Title._NonNullTrim()} - {SiteName._NonNullTrim()}";
            }
            else
            {
                return SiteName._NonNullTrim();
            }
        }

        // 現在の言語と文字列テーブル
        public StrTableLanguageList LanguageList { get; private set; } = StrTableLanguageList.DefaultEmptyStrTableLanguageList;
        readonly Copenhagen<StrTableLanguage> CurrentLanguageInternal = StrTableLanguageList.DefaultEmptyStrTableLanguageList.FindDefaultLanguage();
        public StrTableLanguage CurrentLanguage => CurrentLanguageInternal;
        public StrTable Stb => this.CurrentLanguage.Table;

        public void SetLanguageList(StrTableLanguageList list)
        {
            this.LanguageList = list;
        }

        public void SetLanguageByHttpString(string str)
        {
            SetCurrentLanguage(this.LanguageList.FindLanguageByHttpAcceptLanguage(str));
        }

        public void SetCurrentLanguage(StrTableLanguage language)
        {
            this.CurrentLanguageInternal.TrySet(language);
        }

        // 現在の Language List を元に文字列テーブルファイルを吐き出す 
        public void DumpStrTableJson(FilePath destFileName)
        {
            var objRoot = Json.NewJsonObject();

            foreach (var lang in this.LanguageList.GetLaugnageList().OrderBy(x => x.Key, StrComparer.IgnoreCaseComparer))
            {
                var obj2 = Json.NewJsonObject();

                var table = lang.Table.ToList();

                foreach (var kv in table)
                {
                    obj2.Add(kv.Key.ToUpper(), new Newtonsoft.Json.Linq.JValue(kv.Value));
                }

                objRoot.Add(lang.Key.ToLower(), obj2);
            }

            string jsonBody = objRoot._ObjectToJson(compact: true);

            StringWriter w = new StringWriter();
            w.WriteLine("// String table");
            w.WriteLine("// Automatically generated file");
            w.WriteLine();
            w.WriteLine("// --- BEGIN OF STRTABLE ---");
            w.WriteLine();
            w.WriteLine("var g_ipa_dn_cores_strtable =");
            w.WriteLine(jsonBody);
            w.WriteLine();
            w.WriteLine("// --- END OF STRTABLE ---");
            w.WriteLine();

            destFileName.WriteStringToFile(w.ToString()._NormalizeCrlf(CrlfStyle.Lf), FileFlags.AutoCreateDirectory | FileFlags.WriteOnlyIfChanged, writeBom: true);
        }

        // JavaScript の初期化に使うコンテキストの取得
        public virtual AspPageJavaScriptInitContext GetJavaScriptInitContext()
        {
            return new AspPageJavaScriptInitContext(this.CurrentLanguage.Key);
        }

        // 言語切替え・選択処理
        public void StartLanguageSelection(HttpContext context)
        {
            StrTableLanguage? selectedLanguage = null;

            // setlang Query String が付いている場合はその言語を選択
            var req = context.Request;
            if (HttpMethods.IsGet(req.Method))
            {
                string setlang = req._GetQueryStringFirst("setlang");

                if (setlang._IsFilled())
                {
                    selectedLanguage = this.LanguageList.FindLanguageByKey(setlang);

                    // Cookie に言語名を書く
                    context._EasySaveCookie("asp_page_language_setting", selectedLanguage.Key);

                    // Query string から "setlang" を削除したものにそのままリダイレクトする
                    var originalUrl = context.GetUrl();
                    var newUrl = originalUrl._RemoveQueryStringItem("setlang");

                    context.Response.Redirect(newUrl.ToString());
                }
            }

            if (selectedLanguage == null)
            {
                // 無ければ Cookie を読んでみる
                string cookieLang = req._EasyLoadCookie<string>("asp_page_language_setting")._NonNullTrim();
                if (cookieLang._IsFilled())
                {
                    selectedLanguage = this.LanguageList.FindLanguageByKey(cookieLang);
                }
            }

            // それでも無ければブラウザの accept language を利用する
            if (selectedLanguage == null)
            {
                string acceptLanguage = req.Headers._GetStrFirst("Accept-Language");
                selectedLanguage = this.LanguageList.FindLanguageByHttpAcceptLanguage(acceptLanguage);
            }

            // 現在の言語を更新
            if (selectedLanguage != null)
            {
                this.SetCurrentLanguage(selectedLanguage);
            }
        }

        // 言語 Select Box のデータの取得
        public List<SelectListItem> GetLanguageSelectHtmlBox(Uri currentUri)
        {
            List<SelectListItem> ret = new List<SelectListItem>();

            foreach (var lang in this.LanguageList.GetLaugnageList().OrderBy(x => x.Key, StrComparer.IgnoreCaseTrimComparer))
            {
                string newUrl = currentUri._UpdateQueryStringItem("setlang", lang.Key).PathAndQuery;

                ret.Add(new SelectListItem(lang.Name_English, newUrl, lang.Key._IsSamei(this.CurrentLanguage.Key)));
            }

            return ret;
        }
    }
}

namespace IPA.Cores.Web.TagHelpers
{
    public static class GlobalSettings
    {
        public static Type? PageContextType { get; private set; }

        public static void SetPageContextType<T>() where T : AspPageContext
        {
            PageContextType = typeof(T);
        }
    }

    // タグヘルパーの基本クラス。これを継承してタグヘルパーを作成すること。
    public abstract class AspTagHelperBase : TagHelper
    {
        protected HttpRequest Request => ViewContext.HttpContext.Request;
        protected HttpResponse Response => ViewContext.HttpContext.Response;

        [ViewContext]
        public ViewContext ViewContext { get; set; } = null!;

        readonly Singleton<AspPageContext> PageSingleton;
        protected AspPageContext Page => this.PageSingleton;

        public AspTagHelperBase()
        {
            this.PageSingleton = new Singleton<AspPageContext>(() =>
            {
                if (GlobalSettings.PageContextType == null)
                {
                    throw new CoresLibException("IPA.Cores.Web.TagHelpers.GlobalSettings.PageContextType is not set. Please call IPA.Cores.Web.TagHelpers.GlobalSettings.SetPageContextType() in your ConfigureServices().");
                }

                AspPageContext? context = (AspPageContext?)this.ViewContext.HttpContext.RequestServices.GetService(GlobalSettings.PageContextType);
                if (context == null)
                {
                    throw new CoresLibException("AspPageContext instance is not found.");
                }

                return context;
            });
        }
    }

#pragma warning disable CS1998 // 非同期メソッドは、'await' 演算子がないため、同期的に実行されます
    // 指定された文字列に対応する HTML タグを HTML エンコードせずにそのまま出力するタグヘルパー
    public class StbTagHelper : AspTagHelperBase
    {
        public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
        {
            var content = await output.GetChildContentAsync();
            string innerText = content.GetContent()._NonNullTrim();

            string str = Page.Stb[innerText];

            if (str._IsNullOrZeroLen()) str = "@" + innerText._EncodeHtml() + "@";

            content.SetHtmlContent(str);

            output.Content = content;
            output.TagName = "";
        }
    }

    // 指定された文字列に対応する HTML タグを HTML エンコードして出力するタグヘルパー
    public class StbeTagHelper : AspTagHelperBase
    {
        public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
        {
            var content = await output.GetChildContentAsync();
            string innerText = content.GetContent()._NonNullTrim();

            string str = Page.Stb[innerText];

            if (str._IsNullOrZeroLen()) str = "@" + innerText._EncodeHtml() + "@";

            content.SetContent(str);

            output.Content = content;
            output.TagName = "";
        }
    }
#pragma warning restore CS1998 // 非同期メソッドは、'await' 演算子がないため、同期的に実行されます
}


