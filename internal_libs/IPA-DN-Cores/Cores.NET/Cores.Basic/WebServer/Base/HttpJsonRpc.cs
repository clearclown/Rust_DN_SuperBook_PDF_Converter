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
using System.Reflection;
using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Http.Extensions;
using Castle.DynamicProxy;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using Microsoft.Extensions.Hosting;
using System.IO;
using System.Web;
using System.Net;

#if CORES_BASIC_HTTPSERVER
// ASP.NET Core 3.0 用の型名を無理やり ASP.NET Core 2.2 でコンパイルするための型エイリアスの設定
using IWebHostEnvironment = Microsoft.AspNetCore.Hosting.IHostingEnvironment;
using IHostApplicationLifetime = Microsoft.AspNetCore.Hosting.IApplicationLifetime;
#endif

namespace IPA.Cores.Basic;

public class JsonRpcHttpServerGetMyIpServerSettings : INormalizable
{
    public List<string> DnsServerList { get; set; } = new List<string>();

    public void Normalize()
    {
        if (this.DnsServerList.Count == 0)
        {
            this.DnsServerList.Add("8.8.8.8");
            this.DnsServerList.Add("1.1.1.1");
        }
    }
}

public class JsonRpcHttpServerHook
{
    public JsonRpcHttpServer Svr = null!;

    public virtual string GetFooterMenuText(bool includeAdminPages = true)
    {
        string title = Svr.ServerFriendlyNameHtml;

        string note = $"<p><b>{title} Web &amp; API サーバー.</b> 本システムは、<a href='https://github.com/IPA-CyberLab' target='_blank'><b>GitHub 上でホストされているオープンソースソフトウェア</b></a> です。<a href='https://www.apache.org/licenses/LICENSE-2.0' target='_blank'>Apache License 2.0</a> ライセンスで無償・無保証で提供されています。<BR>Copyright &copy; 2018-{Env.BuildTimeStamp.Year} IPA サイバー技術研究室. All Rights Reserved.</p>";

        string menu = GetHeaderMenuText(includeAdminPages);

        return menu + "\r\n" + note;
    }

    public virtual string GetHeaderMenuText(bool includeAdminPages = false)
    {
        List<Tuple<string, string, string>> items = new List<Tuple<string, string, string>>();

        string title = Svr.ServerFriendlyNameHtml;

        string adminIcon = "<i class='fas fa-key'></i>";

        items.Add(new Tuple<string, string, string>($"<i class='fas fa-user'></i> {title} のコントロールパネル (Web フォーム)", Svr.ControlAbsoluteUrlPath, ""));
        items.Add(new Tuple<string, string, string>($"<i class='fas fa-dice-d6'></i> {title} JSON-RPC の API 一覧・リファレンス・直接呼び出し", Svr.RpcAbsoluteUrlPath, ""));

        if (includeAdminPages)
        {
            items.Add(new Tuple<string, string, string>($"{adminIcon} オブジェクトエディタ (管理者用)", Svr.AdminObjEditAbsoluteUrlPath, ""));
            items.Add(new Tuple<string, string, string>($"{adminIcon} フルテキスト検索 (管理者用)", Svr.AdminObjSearchAbsoluteUrlPath, ""));
            items.Add(new Tuple<string, string, string>($"{adminIcon} 設定エディタ (管理者用)", Svr.AdminConfigAbsoluteUrlPath, ""));
            items.Add(new Tuple<string, string, string>($"{adminIcon} ログブラウザ (管理者用)", Svr.AdminLogBrowserAbsoluteUrlPath, "_blank"));
        }

        List<string> o = new List<string>();

        foreach (var item in items)
        {
            string tmp = $"<a href='{item.Item2}' {(item.Item3._IsEmpty() ? "" : $"target='{item.Item3}'")}><b>{item.Item1}</b></a>";

            o.Add(tmp);
        }

        return "<p>" + o._Combine(" | ") + "</p>";
    }
}

public class JsonRpcHttpServer : JsonRpcServer
{
    public static bool HideErrorDetails = false; // 詳細エラーを隠すかどうかのフラグ。手抜きグローバル変数

    public string RpcAbsoluteUrlPath = ""; // "/rpc/" のような絶対パス。末尾に / を含む。
    public string ControlAbsoluteUrlPath = ""; // "/control/" のような絶対パス。末尾に / を含む。

    public bool HasAdminPages => this.Config.HadbBasedServicePoint != null;

    public JsonRpcHttpServerHook Hook { get; }

    public string AdminConfigAbsoluteUrlPath = ""; // "/admin_config/" のような絶対パス。末尾に / を含む。
    public string AdminObjEditAbsoluteUrlPath = ""; // "/admin_objedit/" のような絶対パス。末尾に / を含む。
    public string AdminObjSearchAbsoluteUrlPath = ""; // "/admin_search/" のような絶対パス。末尾に / を含む。
    public string AdminLogBrowserAbsoluteUrlPath = ""; // "/admin_logbrowser/" のような絶対パス。末尾に / を含む。

    DnsResolver? GetMyIpDnsResolver;
    HiveData<JsonRpcHttpServerGetMyIpServerSettings>? GetMyIpDnsServerSettingsHive;

    // 'Config\HttpJsonRpcGetMyIpServer' のデータ
    public JsonRpcHttpServerGetMyIpServerSettings? GetMyIpServerSettings => GetMyIpDnsServerSettingsHive?.GetManagedDataSnapshot() ?? null;

    string WebFormSecretKey => this.Config.HadbBasedServicePoint!.AdminForm_GetWebFormSecretKey();

    public JsonRpcHttpServer(JsonRpcServerApi api, JsonRpcServerConfig? cfg = null) : base(api, cfg)
    {
        this.Hook = this.Config.Hook;
        this.Hook.Svr = this;
    }

    enum AdminFormsOperation
    {
        ConfigForm = 0,
        ObjEdit,
        ObjSearch,
    }

    // health check の GET ハンドラ
    public virtual async Task HealthCheck_GetRequestHandler(HttpRequest request, HttpResponse response, RouteData routeData)
    {
        CancellationToken cancel = request._GetRequestCancellationToken();

        try
        {
            var result = await this.Config.HadbBasedServicePoint!.HealthCheck_GetCurrentHealthStatus(cancel);

            result.ThrowIfException();

            await response._SendStringContentsAsync("OK\n"._NormalizeCrlf(CrlfStyle.CrLf, true), cancel: cancel);
        }
        catch (Exception ex)
        {
            await response._SendStringContentsAsync("Request Error: " + ex.ToString(), cancel: cancel, statusCode: Consts.HttpStatusCodes.InternalServerError, normalizeCrlf: CrlfStyle.Lf);
        }
    }

    // /getmyip の GET ハンドラ
    public virtual async Task GetMyIp_GetRequestHandler(HttpRequest request, HttpResponse response, RouteData routeData)
    {
        CancellationToken cancel = request._GetRequestCancellationToken();

        try
        {
            StringWriter w = new StringWriter();

            // Query string の解析
            bool port = request._GetQueryStringFirst("port")._ToBool();
            bool fqdn = request._GetQueryStringFirst("fqdn")._ToBool();
            bool verifyfqdn = request._GetQueryStringFirst("verifyfqdn")._ToBool();

            IPAddress clientIp = request.HttpContext.Connection.RemoteIpAddress._UnmapIPv4()!;

            string proxySrcIpStr = request.Headers._GetStrFirst("x-proxy-srcip");
            var proxySrcIp = proxySrcIpStr._ToIPAddress(noExceptionAndReturnNull: true);
            if (proxySrcIp != null)
            {
                clientIp = proxySrcIp;
            }

            if (port == false && fqdn == false)
            {
                // 従来のサーバーとの互換性を維持するため改行を入れません !!
                w.WriteLine($"IP={clientIp.ToString()}");
            }
            else
            {
                w.WriteLine($"IP={clientIp.ToString()}");
            }

            if (port)
            {
                w.WriteLine($"PORT={request.HttpContext.Connection.RemotePort}");
            }

            if (fqdn)
            {
                string hostname = clientIp.ToString();
                try
                {
                    var ipType = clientIp._GetIPAddressType();
                    if (ipType.BitAny(IPAddressType.IPv4_IspShared | IPAddressType.Loopback | IPAddressType.Zero | IPAddressType.Multicast | IPAddressType.LocalUnicast))
                    {
                        // ナーシ
                    }
                    else
                    {
                        hostname = await this.GetMyIpDnsResolver!.GetHostNameOrIpAsync(clientIp, cancel);

                        if (verifyfqdn)
                        {
                            try
                            {
                                var ipList = await this.GetMyIpDnsResolver.GetIpAddressListSingleStackAsync(hostname,
                                    clientIp.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 ? DnsResolverQueryType.AAAA : DnsResolverQueryType.A,
                                    cancel: cancel);

                                if ((ipList?.Any(x => IpComparer.Comparer.Equals(x, clientIp)) ?? false) == false)
                                {
                                    // NG
                                    hostname = request.HttpContext.Connection.RemoteIpAddress._UnmapIPv4()!.ToString();
                                }
                            }
                            catch
                            {
                                // NG
                                hostname = request.HttpContext.Connection.RemoteIpAddress._UnmapIPv4()!.ToString();
                            }
                        }
                    }
                }
                catch { }
                w.WriteLine($"FQDN={hostname}");
            }

            await response._SendStringContentsAsync(w.ToString()._NormalizeCrlf(CrlfStyle.CrLf, true), cancel: cancel);
        }
        catch (Exception ex)
        {
            await response._SendStringContentsAsync("Request Error: " + ex.ToString(), cancel: cancel, statusCode: Consts.HttpStatusCodes.InternalServerError, normalizeCrlf: CrlfStyle.Lf);
        }
    }

    // /admin_config の GET ハンドラ
    public virtual async Task AdminConfig_GetRequestHandler(HttpRequest request, HttpResponse response, RouteData routeData)
    {
        await AdminConfig_CommonRequestHandler(request, response, routeData, WebMethods.GET);
    }

    // /admin_config の POST ハンドラ
    public virtual async Task AdminConfig_PostRequestHandler(HttpRequest request, HttpResponse response, RouteData routeData)
    {
        await this.CheckIsCrossSiteRefererAndDenyAsync(request, response);

        await AdminConfig_CommonRequestHandler(request, response, routeData, WebMethods.POST);
    }

    async Task CheckIsCrossSiteRefererAndDenyAsync(HttpRequest request, HttpResponse response)
    {
        if ((this.Config.HadbBasedServicePoint?.AdminForm_GetCurrentDynamicConfig()?.Service_Security_ProhibitCrossSiteRequest ?? true) == false)
        {
            return;
        }

        if (request._IsCrossSiteReferer() || request._IsCrossSiteFetch())
        {
            string err = "Cross site request is not allowed on this web server.";

            await response._SendStringContentsAsync(err, cancel: request._GetRequestCancellationToken(),
                statusCode: Consts.HttpStatusCodes.Forbidden);

            throw new CoresException(err);
        }
    }

    // /admin_config の共通ハンドラ
    public virtual async Task AdminConfig_CommonRequestHandler(HttpRequest request, HttpResponse response, RouteData routeData, WebMethods method)
    {
        await Admin_CommonAsync(request, response, routeData, "設定エディタ (管理者用)", AdminFormsOperation.ConfigForm, method,
            async (w, postData, c) =>
            {
                string configBody = "";

                string msg = "";
                string msgColor = "green";

                if (method == WebMethods.POST)
                {
                    string str = postData._GetString_UTF8();
                    var postCollection = HttpUtility.ParseQueryString(str);
                    string secret = postCollection._GetStr("_secret");
                    if (secret != this.WebFormSecretKey)
                    {
                        msg = "Error! Invalid web form update.";
                        msgColor = "red";
                    }
                    else
                    {
                        configBody = postCollection._GetStr("configbody");
                        await this.Config.HadbBasedServicePoint!.AdminForm_SetDynamicConfigTextAsync(configBody, c);

                        msg = "設定内容は、正常にデータベースに保存され、本システムに反映されました。";

                        this.FlushCache();
                    }
                }

                configBody = await this.Config.HadbBasedServicePoint!.AdminForm_GetDynamicConfigTextAsync(c);

                if (msg._IsFilled()) w.WriteLine($"<p><B><font color={msgColor}>{msg._EncodeHtml(true)}</font></B></p>");

                w.WriteLine($"<form action='{this.AdminConfigAbsoluteUrlPath}' method='post'>");

                w.WriteLine($"<input name='_secret' type='hidden' value='{this.WebFormSecretKey}'/>");

                w.WriteLine("<p>");
                w.WriteLine("<textarea name='configbody' spellcheck='false' rows='2' cols='20' id='configbody' style='color:#222222;font-family:Consolas;font-size:10pt;height:702px;width:95%;padding: 10px 10px 10px 10px;'>");
                w.WriteLine(configBody._EncodeHtmlCodeBlock());
                w.WriteLine("</textarea>");
                w.WriteLine("</p>");

                if (msg._IsFilled()) w.WriteLine($"<p><B><font color={msgColor}>{msg._EncodeHtml(true)}</font></B></p>");

                w.WriteLine("<input class='button is-link' type='submit' style='font-weight: bold' value='直ちに適用する (十分注意してクリックしてください)'>");

                w.WriteLine("</form>");
            });
    }

    // /admin_objedit の GET ハンドラ
    public virtual async Task AdminObjEdit_GetRequestHandler(HttpRequest request, HttpResponse response, RouteData routeData)
    {
        await AdminObjEdit_CommonRequestHandler(request, response, routeData, WebMethods.GET);
    }

    // /admin_objedit の POST ハンドラ
    public virtual async Task AdminObjEdit_PostRequestHandler(HttpRequest request, HttpResponse response, RouteData routeData)
    {
        await this.CheckIsCrossSiteRefererAndDenyAsync(request, response);

        await AdminObjEdit_CommonRequestHandler(request, response, routeData, WebMethods.POST);
    }

    // /admin_objedit の共通ハンドラ
    public virtual async Task AdminObjEdit_CommonRequestHandler(HttpRequest request, HttpResponse response, RouteData routeData, WebMethods method)
    {
        await Admin_CommonAsync(request, response, routeData, "低レベルデータベースオブジェクト ビューア兼エディタ", AdminFormsOperation.ObjEdit, method,
            async (w, postData, c) =>
            {
                string objBody = "";

                bool showUpdateButton = false;

                // Query String において指定された UID
                string qsUid = request.Query._GetStrFirst("uid");
                qsUid = qsUid._NormalizeUid()._MakeVerySafeAsciiOnlyNonSpaceFileName();

                bool qsMetadata = request.Query._GetStrFirst("metadata")._ToBool();
                bool qsArchive = request.Query._GetStrFirst("archive")._ToBool();


                HadbObject? currentObj = null;

                string postMsg = "";
                bool isPostError = false;

                if (method == WebMethods.GET)
                {
                    if (qsUid._IsFilled())
                    {
                        // qsUid が指定されている場合はオブジェクトの内容を取得する
                        if (qsMetadata || qsArchive)
                        {
                            try
                            {
                                objBody = await this.Config.HadbBasedServicePoint!.AdminForm_DirectGetObjectExAsync(qsUid, 100, qsArchive ? HadbObjectGetExFlag.WithArchive : HadbObjectGetExFlag.None);
                            }
                            catch (Exception ex)
                            {
                                objBody = "*** エラー: " + ex.ToString();
                            }
                        }
                        else
                        {
                            currentObj = await this.Config.HadbBasedServicePoint!.AdminForm_DirectGetObjectAsync(qsUid, c);
                            if (currentObj == null)
                            {
                                objBody = $"エラー: 指定された UID '{qsUid}' のオブジェクトが存在しません。";
                            }
                            else
                            {
                                objBody = currentObj.UserData._ObjectToJson(includeNull: true);

                                showUpdateButton = true;
                            }
                        }
                    }
                }
                else if (method == WebMethods.POST)
                {
                    // オブジェクトの内容を書き換える
                    string str = postData._GetString_UTF8();
                    var postCollection = HttpUtility.ParseQueryString(str);
                    string secret = postCollection._GetStr("_secret");
                    if (secret != this.WebFormSecretKey)
                    {
                        postMsg = "Error! Invalid web form update.";
                        isPostError = true;
                    }
                    else
                    {
                        string objectBody = postCollection._GetStr("objectbody");
                        string uid = postCollection._GetStr("uid");
                        string type = postCollection._GetStr("type");
                        string nameSpace = postCollection._GetStr("namespace");
                        bool delete = postCollection._GetStr("delete")._ToBool();

                        try
                        {
                            if (delete == false)
                            {
                                currentObj = await this.Config.HadbBasedServicePoint!.AdminForm_DirectSetObjectAsync(uid, objectBody, HadbObjectSetFlag.Update, type, nameSpace, c);

                                postMsg = "OK! 指定されたデータ項目は、正常にデータベースに書き込みされました。";

                                showUpdateButton = true;

                                objBody = currentObj.UserData._ObjectToJson(includeNull: true);

                                qsUid = currentObj.Uid;
                            }
                            else
                            {
                                currentObj = await this.Config.HadbBasedServicePoint!.AdminForm_DirectSetObjectAsync(uid, "", HadbObjectSetFlag.Delete, type, nameSpace, c);
                                postMsg = "OK! 指定されたデータ項目は、正常にデータベースから削除されました。";

                                showUpdateButton = false;


                                qsUid = currentObj.Uid;
                                qsArchive = true;
                                objBody = "";
                                currentObj = null;
                            }
                        }
                        catch (Exception ex)
                        {
                            postMsg = ex.ToString();
                            isPostError = true;
                            qsUid = uid;
                        }
                    }

                }


                // UID 入力フォーム
                string uidEditBoxStr = $@"
                    <div class='field'>
                        <p class='control'>
                            <input class='input is-info text-box single-line' name='uid' type='text' value='{qsUid}' />
                            入力例: DATA_0123456789_123_012345678901234_01234_01234
                        </p>
                    </div>
";

                w.WriteLine($"<form action='{this.AdminObjEditAbsoluteUrlPath}' method='get'>");

                w.WriteLine($@"
                    <div class='field is-horizontal'>
                        <div class='field-label is-normal'>
                            <label class='label'><i class='fas fa-cubes'></i> 対象のオブジェクト ID:<BR>(ユニーク ID)</label>
                        </div>
                        <div class='field-body'>
                            {uidEditBoxStr}
                        </div>
                    </div>");


                w.WriteLine($@"
                    <div class='field is-horizontal'>
                        <div class='field-label'>
                            <!-- Left empty for spacing -->
                        </div>
                        <div class='field-body'>
                            <div class='field'>
                                <div class='control'>
                                    <input class='button is-link' type='submit' style='font-weight: bold' value='UID を用いてオブジェクトを検索'>
                                    &nbsp;&nbsp;<input {qsMetadata._HtmlCheckedIfTrue()} id='metadata' name='metadata' type='checkbox' value='1' /><label for='metadata'>&nbsp;オブジェクトのメタデータを表示</label>
                                    &nbsp;&nbsp;<input {qsArchive._HtmlCheckedIfTrue()} id='archive' name='archive' type='checkbox' value='1' /><label for='archive'>&nbsp;オブジェクトの変更履歴データを表示</label>
                                    <p>　</p>
");

                w.WriteLine(@"
                                </div>
                            </div>
                        </div>
                    </div>");


                w.WriteLine("</form>");


                if (postMsg._IsFilled())
                {
                    w.WriteLine($"<p><font color={(isPostError ? "red" : "green")}><b>{postMsg._EncodeHtml(true)}</b></font></p>");
                }

                w.WriteLine($"<form action='{this.AdminObjEditAbsoluteUrlPath}' method='post'>");

                w.WriteLine($"<input name='_secret' type='hidden' value='{this.WebFormSecretKey}'/>");
                w.WriteLine($"<input name='uid' type='hidden' value='{currentObj?.Uid ?? ""}'/>");
                w.WriteLine($"<input name='type' type='hidden' value='{currentObj?.UserDataTypeName ?? ""}'/>");
                w.WriteLine($"<input name='namespace' type='hidden' value='{currentObj?.NameSpace ?? ""}'/>");

                if (currentObj != null)
                {
                    w.WriteLine("<p>");
                    w.WriteLine($"オブジェクトの UID: <code><b>{currentObj.Uid}</b></code>, ");
                    w.WriteLine($"オブジェクトの型名: <code><b>{currentObj.UserDataTypeName}</b></code>, ");
                    w.WriteLine($"オブジェクトの名前空間: <code><b>{currentObj.NameSpace}</b></code>");
                    w.WriteLine("</p>");
                }

                if (objBody._IsFilled())
                {
                    w.WriteLine("<p>");
                    w.WriteLine("<textarea name='objectbody' spellcheck='false' rows='2' cols='20' id='configbody' style='color:#222222;font-family:Consolas;font-size:10pt;height:702px;width:95%;padding: 10px 10px 10px 10px;'>");
                    w.WriteLine(objBody._EncodeHtmlCodeBlock());
                    w.WriteLine("</textarea>");
                    w.WriteLine("</p>");
                }

                //if (msg._IsFilled()) w.WriteLine($"<p><B><font color=green>{msg}</font></B></p>");

                if (showUpdateButton)
                {
                    w.WriteLine("<input class='button is-link' type='submit' style='font-weight: bold' value='このオブジェクトの内容を更新する (注意してクリックしてください)'>");
                    w.WriteLine($"&nbsp;&nbsp;<input id='delete' name='delete' type='checkbox' value='1' /><label for='delete'>&nbsp;このオブジェクトを削除する (慎重にクリックしてください)</label>");
                }

                w.WriteLine("</form>");
            });
    }

    // /admin_search の GET ハンドラ
    public virtual async Task AdminObjSearch_GetRequestHandler(HttpRequest request, HttpResponse response, RouteData routeData)
    {
        await AdminObjSearch_CommonRequestHandler(request, response, routeData, WebMethods.GET);
    }

    // /admin_search の POST ハンドラ
    public virtual async Task AdminObjSearch_PostRequestHandler(HttpRequest request, HttpResponse response, RouteData routeData)
    {
        await AdminObjSearch_CommonRequestHandler(request, response, routeData, WebMethods.POST);
    }

    // /admin_search の共通ハンドラ
    public virtual async Task AdminObjSearch_CommonRequestHandler(HttpRequest request, HttpResponse response, RouteData routeData, WebMethods method)
    {
        await Admin_CommonAsync(request, response, routeData, "データベース上の全オブジェクトに対するフルテキスト検索エンジン", AdminFormsOperation.ObjSearch, method,
            async (w, postData, c) =>
            {
                var queryList = request.Query;

                string q = queryList._GetStrFirst("q");
                string sort = queryList._GetStrFirst("sort");
                string type = queryList._GetStrFirst("type");
                string ns = queryList._GetStrFirst("ns");
                int max = queryList._GetIntFirst("max", this.Config.HadbBasedServicePoint!.AdminForm_GetCurrentDynamicConfig()!.Service_FullTextSearchResultsCountStandard);
                bool wordmode = queryList._GetBoolFirst("wordmode");
                bool fieldnamemode = queryList._GetBoolFirst("fieldnamemode");
                bool download = queryList._GetBoolFirst("download");

                bool isQuery = request.QueryString.ToString()._IsFilled();

                HadbFullTextSearchResult? result = null;

                int tookTime = 0;

                var timeStamp = DtOffsetNow;
                var now = timeStamp;

                // 検索の実施
                if (isQuery)
                {
                    await this.CheckIsCrossSiteRefererAndDenyAsync(request, response);

                    long startTick = Time.HighResTick64;
                    result = await this.Config.HadbBasedServicePoint!.ServiceAdmin_FullTextSearch(q, sort, wordmode, fieldnamemode, type, ns, max);
                    long endTick = Time.HighResTick64;

                    tookTime = (int)(endTick - startTick);
                }

                // 検索フォーム

                w.WriteLine($"<form action='{this.AdminObjSearchAbsoluteUrlPath}' method='get'>");

                w.WriteLine($@"
                    <div class='field is-horizontal'>
                        <div class='field-label is-normal'>
                            <label class='label'><i class='fas fa-search'></i> 検索文字列:</label>
                        </div>
                        <div class='field-body'>
                            <div class='field'>
                                <p class='control'>
                                    <input class='input is-info text-box single-line' name='q' type='text' value='{q._EncodeHtml()}' />
                                    入力例: hello<BR><i>'AND' または 'OR' キーワードを複数の単語間に指定できます。また、単語の前に '!' または '-' 文字を付加すると、その単語が含まれている項目を明示的に除外できます。</i>
                                </p>
                            </div>
                        </div>
                    </div>");


                w.WriteLine($@"
                    <div class='field is-horizontal'>
                        <div class='field-label'>
                            <!-- Left empty for spacing -->
                        </div>
                        <div class='field-body'>
                            <div class='field'>
                                <div class='control'>
                                    <input class='button is-link' type='submit' style='font-weight: bold' value='オブジェクトの検索'>
                                    &nbsp;&nbsp;<input {wordmode._HtmlCheckedIfTrue()} id='wordmode' name='wordmode' type='checkbox' value='1' /><label for='wordmode'>&nbsp;<b>単語単位で検索する</b></label>
                                    &nbsp;&nbsp;<input {fieldnamemode._HtmlCheckedIfTrue()} id='fieldnamemode' name='fieldnamemode' type='checkbox' value='1' /><label for='fieldnamemode'>&nbsp;<b>変数名付き検索モード</b> (例: 'Age=123')</label>
                                    &nbsp;&nbsp;<input {download._HtmlCheckedIfTrue()} id='download' name='download' type='checkbox' value='1' /><label for='download'>&nbsp;<b>ダウンロードボタンを表示 (注意: 応答速度が低下します)</b></label>
                                    <p>　</p>
");

                w.WriteLine(@"
                                </div>
                            </div>
                        </div>
                    </div>");



                w.WriteLine($@"
                    <div class='field is-horizontal'>
                        <div class='field-label'>
                            <!-- Left empty for spacing -->
                        </div>
                        <div class='field-label is-normal'>
                            <label class='label'><i class='fas fa-sort-amount-down'></i> 変数名でソートする:<BR>(オプション)</label>
                        </div>
                        <div class='field-body'>
                            <div class='field'>
                                <p class='control'>
                                    <input class='input is-info text-box single-line' name='sort' type='text' value='{sort._EncodeHtml()}' />
                                    入力例: Age <i>(JSON 上の変数名を指定してください。変数名に '!' を前置すると、逆順ソートされます。)</i>
                                </p>
                            </div>
                        </div>
                    </div>");




                w.WriteLine($@"
                    <div class='field is-horizontal'>
                        <div class='field-label'>
                            <!-- Left empty for spacing -->
                        </div>
                        <div class='field-label is-normal'>
                            <label class='label'><i class='fas fa-dice-d6'></i> 型名:<BR>(Optional)</label>
                        </div>
                        <div class='field-body'>
                            <div class='field'>
                                <p class='control'>
                                    <input class='input is-info text-box single-line' name='type' type='text' value='{type._EncodeHtml()}' />
                                    入力例: User <i>(JSON 上のオブジェクトの型名を指定してください。)</i>
                                </p>
                            </div>
                        </div>
                    </div>");




                w.WriteLine($@"
                    <div class='field is-horizontal'>
                        <div class='field-label'>
                            <!-- Left empty for spacing -->
                        </div>
                        <div class='field-label is-normal'>
                            <label class='label'><i class='fas fa-stream'></i> 名前空間文字列:<BR>(Optional)</label>
                        </div>
                        <div class='field-body'>
                            <div class='field'>
                                <p class='control'>
                                    <input class='input is-info text-box single-line' name='ns' type='text' value='{ns._EncodeHtml()}' />
                                    入力例: NS_SYSTEM1 <i>(Specify namespace here.)</i>
                                </p>
                            </div>
                        </div>
                    </div>");


                w.WriteLine($@"
                    <div class='field is-horizontal'>
                        <div class='field-label'>
                            <!-- Left empty for spacing -->
                        </div>
                        <div class='field-label is-normal'>
                            <label class='label'><i class='fas fa-list'></i> 検索結果の最大数:<BR>(Optional)</label>
                        </div>
                        <div class='field-body'>
                            <div class='field'>
                                <p class='control'>
                                    <input class='input is-info text-box single-line' name='max' type='text' value='{(max == 0 ? "" : max.ToString())}' />
                                </p>
                            </div>
                        </div>
                    </div>");


                w.WriteLine("</form>");

                if (result != null)
                {
                    string jsonString = result._ObjectToJson(includeNull: true);

                    string bomUtfDownloadFileName = $"JSON_{request.Host.Host}_{now._ToYymmddStr(yearTwoDigits: true)}_{now._ToHhmmssStr()}"._MakeVerySafeAsciiOnlyNonSpaceFileName() + ".json";

                    w.WriteLine($"<h3 id='results' class='title is-5'>" + $"検索結果: {result.NumResultObjects} 個のオブジェクト" + "</h3>");

                    if (download)
                    {
                        var bomUtfData = jsonString._GetBytes_UTF8(true);

                        w.WriteLine(Str.GenerateHtmlDownloadJavaScript(bomUtfData, Consts.MimeTypes.Binary, bomUtfDownloadFileName, "download2"));

                        w.WriteLine($"<a class='button is-primary' id='download2' style='font-weight: bold' href='#' download='{bomUtfDownloadFileName}' onclick='handleDownload()'><i class='far fa-folder-open'></i>&nbsp;検索結果を JSON データとしてダウンロードする ({bomUtfData.Length._ToString3()} バイト)</a>");
                    }

                    w.Write("<pre><code class='language-json'>");

                    w.Write(jsonString._EncodeHtmlCodeBlock().ToString());

                    w.WriteLine("</code></pre>");
                }
            });
    }


    // /admin_*** の共通ハンドラ
    async Task Admin_CommonAsync(HttpRequest request, HttpResponse response, RouteData routeData, string title, AdminFormsOperation operation, WebMethods method,
        Func<StringWriter, ReadOnlyMemory<byte>, CancellationToken, Task> bodyWriter)
    {
        try
        {
            if (this.HasAdminPages == false)
            {
                throw new NotSupportedException("Admin Pages are not supported.");
            }

            ReadOnlyMemory<byte> postData = default;
            CancellationToken cancel = request._GetRequestCancellationToken();

            if (method == WebMethods.POST)
            {
                postData = await request.Body._ReadToEndAsync(10_000_000, cancel);
            }

            // Basic 認証または Query String における認証クレデンシャルが提供されているかどうか調べる
            string suppliedUsername = "";
            string suppliedPassword = "";
            var basicAuthHeader = BasicAuthImpl.ParseBasicAuthenticationHeader(request.Headers._GetStrFirst("Authorization"));
            if (basicAuthHeader != null)
            {
                suppliedUsername = basicAuthHeader.Item1;
                suppliedPassword = basicAuthHeader.Item2;
            }
            var conn = request.HttpContext.Connection;

            SortedDictionary<string, string> requestHeaders = new SortedDictionary<string, string>();
            foreach (string headerName in request.Headers.Keys)
            {
                if (request.Headers.TryGetValue(headerName, out var val))
                {
                    requestHeaders.Add(headerName, val.ToString());
                }
            }

            JsonRpcClientInfo clientInfo = new JsonRpcClientInfo(
                request.Scheme,
                request.Host.ToString(),
                request.GetDisplayUrl(),
                conn.LocalIpAddress!._UnmapIPv4().ToString(), conn.LocalPort,
                conn.RemoteIpAddress!._UnmapIPv4().ToString(), conn.RemotePort,
                requestHeaders,
                suppliedUsername,
                suppliedPassword);

            string basicAuthRealm = "Auth for HADB Admin Page";

            TaskVar.Set<JsonRpcClientInfo>(clientInfo);
            try
            {
                if (this.Config.HadbBasedServicePoint == null)
                {
                    throw new CoresException("this.Config.HadbBasedServicePoint is not set.");
                }
                else
                {
                    await this.Config.HadbBasedServicePoint.Basic_Require_AdminBasicAuthAsync(basicAuthRealm);
                }

                StringWriter w = new StringWriter();

                Control_WriteHtmlHeader(w, $"{this.ServerFriendlyNameHtml} - {title}");


                w.WriteLine(@"
    <div class='box'>
        <div class='content'>
");

                w.WriteLine(this.Hook.GetHeaderMenuText());

                w.WriteLine($"<h2 class='title is-4'>" + $"{this.ServerFriendlyNameHtml} - {title}" + "</h2>");

                await bodyWriter(w, postData, cancel);

                w.WriteLine("<p>　</p><HR>");

                w.WriteLine(this.Hook.GetFooterMenuText());

                w.WriteLine(@"
        </div>
    </div>
");

                Control_WriteHtmlFooter(w);

                await response._SendStringContentsAsync(w.ToString(), contentsType: Consts.MimeTypes.HtmlUtf8, cancel: cancel, normalizeCrlf: CrlfStyle.CrLf);

            }
            catch (JsonRpcAuthErrorException)
            {
                // Basic 認証の要求
                KeyValueList<string, string> basicAuthResponseHeaders = new KeyValueList<string, string>();
                basicAuthResponseHeaders.Add(Consts.HttpHeaders.WWWAuthenticate, $"Basic realm=\"{basicAuthRealm}\"");

                await using var basicAuthRequireResult = new HttpStringResult("Basic Auth Required", contentType: Consts.MimeTypes.TextUtf8, statusCode: Consts.HttpStatusCodes.Unauthorized, additionalHeaders: basicAuthResponseHeaders);

                await response._SendHttpResultAsync(basicAuthRequireResult, cancel: request._GetRequestCancellationToken());

                return;
            }
            finally
            {
                TaskVar.Set<JsonRpcClientInfo>(null);
            }
        }
        catch (Exception ex)
        {
            await response._SendStringContentsAsync(ex.ToString(), cancel: request._GetRequestCancellationToken(), normalizeCrlf: CrlfStyle.Lf);
        }

    }

    // /control の GET ハンドラ
    public virtual async Task Control_GetRequestHandler(HttpRequest request, HttpResponse response, RouteData routeData)
    {
        CancellationToken cancel = request._GetRequestCancellationToken();
        try
        {
            string rpcMethod = routeData.Values._GetStr("rpc_method");
            if (rpcMethod._IsEmpty())
            {
                // 目次ページの表示
                var baseUri = request.GetEncodedUrl()._ParseUrl()._CombineUrl(this.RpcAbsoluteUrlPath);

                await response._SendStringContentsAsync(Control_GenerateHtmlHelpString(baseUri), contentsType: Consts.MimeTypes.HtmlUtf8, cancel: cancel, normalizeCrlf: CrlfStyle.CrLf);
            }
            else
            {
                var mi = this.Api.GetMethodInfo(rpcMethod);

                // API ごとのページの表示
                string suppliedUsername = "";
                string suppliedPassword = "";

                // Basic 認証または Query String における認証クレデンシャルが提供されているかどうか調べる
                var basicAuthHeader = BasicAuthImpl.ParseBasicAuthenticationHeader(request.Headers._GetStrFirst("Authorization"));
                if (basicAuthHeader != null)
                {
                    suppliedUsername = basicAuthHeader.Item1;
                    suppliedPassword = basicAuthHeader.Item2;
                }

                JObject jObj = new JObject();

                bool isCall = false;

                SortedDictionary<string, string> requestHeaders = new SortedDictionary<string, string>();
                foreach (string headerName in request.Headers.Keys)
                {
                    if (request.Headers.TryGetValue(headerName, out var val))
                    {
                        requestHeaders.Add(headerName, val.ToString());
                    }
                }

                foreach (string key in request.Query.Keys)
                {
                    string value = request.Query[key];

                    if (key._IsSamei("_call"))
                    {
                        if (value._ToBool())
                        {
                            isCall = true;
                        }
                    }

                    string valueTrim = value.Trim();
                    if (valueTrim.StartsWith("{") && valueTrim.EndsWith("}"))
                    {
                        // JSON Data
                        jObj.Add(key, value._JsonToObject<JObject>());
                    }
                    else
                    {
                        // Primitive Date
                        jObj.Add(key, JToken.FromObject(value));
                    }
                }

                if (isCall == false)
                {
                    if (mi.ParametersByIndex.Length == 0)
                    {
                        // 1 つもパラメータ指定がない場合は Web フォームを作成せず直接呼び出す
                        isCall = true;
                    }
                }

                if (isCall)
                {
                    await this.CheckIsCrossSiteRefererAndDenyAsync(request, response);
                }

                string args = jObj._ObjectToJson(compact: true);

                //string id = "GET-" + Str.NewGuid().ToUpperInvariant();
                string in_str = "{'jsonrpc':'2.0','method':'" + rpcMethod + "','params':" + args + "}";

                string body;

                var conn = request.HttpContext.Connection;

                JsonRpcClientInfo client_info = new JsonRpcClientInfo(
                    request.Scheme,
                    request.Host.ToString(),
                    request.GetDisplayUrl(),
                    conn.LocalIpAddress!._UnmapIPv4().ToString(), conn.LocalPort,
                    conn.RemoteIpAddress!._UnmapIPv4().ToString(), conn.RemotePort,
                    requestHeaders,
                    suppliedUsername,
                    suppliedPassword);

                if (isCall == false)
                {
                    // Web フォームの画面表示
                    body = Control_GenerateApiInputFormPage(rpcMethod);
                }
                else
                {
                    var timeStamp = DtOffsetNow;

                    // JSON-RPC の実際の実行と結果の取得
                    double tickStart = Time.NowHighResDouble;
                    var callResults = await this.CallMethods(in_str, client_info, true, StringComparison.OrdinalIgnoreCase);

                    double tickEnd = Time.NowHighResDouble;

                    if (HideErrorDetails) callResults.RemoveErrorDetailsFromResultString();


                    double tookTick = tickEnd - tickStart;

                    if (callResults.Error_AuthRequired)
                    {
                        // Basic 認証の要求
                        KeyValueList<string, string> basicAuthResponseHeaders = new KeyValueList<string, string>();
                        string realm = $"User Authentication for {this.RpcAbsoluteUrlPath}";
                        if (callResults.Error_AuthRequiredRealmName._IsFilled())
                        {
                            realm += $" ({realm._MakeVerySafeAsciiOnlyNonSpaceFileName()})";
                        }
                        basicAuthResponseHeaders.Add(Consts.HttpHeaders.WWWAuthenticate, $"Basic realm=\"{realm}\"");

                        await using var basicAuthRequireResult = new HttpStringResult(callResults.ResultString, contentType: Consts.MimeTypes.TextUtf8, statusCode: Consts.HttpStatusCodes.Unauthorized, additionalHeaders: basicAuthResponseHeaders);

                        await response._SendHttpResultAsync(basicAuthRequireResult, cancel: request._GetRequestCancellationToken());

                        return;
                    }

                    body = Control_GenerateApiResultPage(rpcMethod, callResults, request.Host.Host, timeStamp, tookTick);
                }

                await response._SendStringContentsAsync(body, contentsType: Consts.MimeTypes.HtmlUtf8, cancel: cancel, normalizeCrlf: CrlfStyle.CrLf);

                //await ProcessHttpRequestMain(request, response, in_str, Consts.MimeTypes.TextUtf8, Consts.HttpStatusCodes.InternalServerError, true, paramsStrComparison: StringComparison.OrdinalIgnoreCase);
            }
        }
        catch (Exception ex)
        {
            await response._SendStringContentsAsync("Request Error: " + ex.ToString(), cancel: cancel, statusCode: Consts.HttpStatusCodes.InternalServerError, normalizeCrlf: CrlfStyle.Lf);
        }
    }

    string Control_GenerateApiResultPage(string methodName, JsonRpcCallResult callResults, string httpHostHeader, DateTimeOffset timeStamp, double tookTick)
    {
        StringWriter w = new StringWriter();
        var mi = this.Api.GetMethodInfo(methodName);

        var bomUtfData = callResults.ResultString._GetBytes_UTF8(bom: true);

        string title = $"{mi.Name} API は、正常に応答データを返しました。応答データは、{bomUtfData.Length._ToString3()} バイトの JSON データです。";

        if (callResults.AllError && callResults.SingleErrorMessage._IsFilled())
        {
            title = $"{mi.Name} API の実行中にエラーが発生しました。";
        }

        Control_WriteHtmlHeader(w, title);


        var now = DtOffsetNow;

        string bomUtfDownloadFileName = $"JSON_{httpHostHeader}_{now._ToYymmddStr(yearTwoDigits: true)}_{now._ToHhmmssStr()}_{mi.Name}_API_Result"._MakeVerySafeAsciiOnlyNonSpaceFileName() + ".json";

        w.WriteLine(Str.GenerateHtmlDownloadJavaScript(bomUtfData, Consts.MimeTypes.Binary, bomUtfDownloadFileName));

        w.WriteLine(@"
    <div class='box'>
        <div class='content'>
");

        w.WriteLine(this.Hook.GetHeaderMenuText());

        w.WriteLine($"<h2 class='title is-4'>" + title._EncodeHtml() + "</h2>");

        if (callResults.AllError)
        {
            w.WriteLine($@"
<article class='message is-danger'>
  <div class='message-body'>
    <strong>エラーメッセージ:</strong><BR>{callResults.SingleErrorMessage._EncodeHtml()}
  </div>
</article>");

            w.WriteLine($"<a class='button is-danger is-rounded' style='font-weight: bold' href='javascript:history.go(-1)'><i class='fas fa-arrow-circle-left'></i>&nbsp;コントロールパネルの Web フォームに戻って再試行する</a>");
            w.WriteLine("<p>　</p>");
        }

        w.WriteLine($"<p><b>返却された JSON データは以下のとおりです。</b> 詳しい解説が、<a href='{this.RpcAbsoluteUrlPath}#{mi.Name}' target='_blank'><b>{mi.Name} API のリファレンスマニュアル</a></b> に記載されている場合があります。</p>");

        w.WriteLine($"<a class='button is-primary' id='download' style='font-weight: bold' href='#' download='{bomUtfDownloadFileName}' onclick='handleDownload()'><i class='far fa-folder-open'></i>&nbsp;JSON 応答データ ({bomUtfData.Length._ToString3()} バイト)</a>");

        if (mi.ParametersByIndex.Length == 0)
        {
            w.WriteLine($"<a class='button is-info' style='font-weight: bold' href='{this.ControlAbsoluteUrlPath}{mi.Name}/'><i class='fas fa-sync'></i>&nbsp;{mi.Name} API を再度呼び出す</a>");
        }

        w.WriteLine($"<a class='button is-success' style='font-weight: bold' href='{this.ControlAbsoluteUrlPath}'><i class='fas fa-user'></i>&nbsp;コントロールパネルの Web フォームの API 一覧に戻る</a>");


        w.WriteLine($"<p><i>実行日時: {timeStamp._ToDtStr(true)}, 実行に要した時間: {TimeSpan.FromSeconds(tookTick)._ToTsStr(true, true)}.</i></p>");

        w.Write("<pre><code class='language-json'>");

        w.Write(callResults.ResultString._EncodeHtmlCodeBlock().ToString());

        w.WriteLine("</code></pre>");

        if (mi.ParametersByIndex.Length >= 1)
        {
            w.WriteLine($"<a class='button is-info' style='font-weight: bold' href='{this.ControlAbsoluteUrlPath}{mi.Name}/'><i class='fas fa-user'></i>&nbsp;新規の {mi.Name} API のコントロールパネルの Web フォームを表示</a>");
        }
        else
        {
            w.WriteLine($"<a class='button is-info' style='font-weight: bold' href='{this.ControlAbsoluteUrlPath}{mi.Name}/'><i class='fas fa-sync'></i>&nbsp;{mi.Name} API を再度呼び出す</a>");
        }

        w.WriteLine($"<a class='button is-success' style='font-weight: bold' href='{this.ControlAbsoluteUrlPath}'><i class='fas fa-user'></i>&nbsp;コントロールパネルの Web フォームの API 一覧に戻る</a>");

        w.WriteLine("<p>　</p>");


        w.WriteLine("<hr />");

        w.WriteLine($"<p><b><a href='{this.ControlAbsoluteUrlPath}'><i class='fas fa-user'></i> API コントロールパネルの API 一覧</a></b> > <b><a href='{this.ControlAbsoluteUrlPath}{mi.Name}/'><i class='fas fa-user'></i> {mi.Name} API コントロールパネルの Web フォーム</a></b></p>");

        if (this.Config.EnableBuiltinRichWebPages)
        {
            w.WriteLine($"<p><b><a href='{this.RpcAbsoluteUrlPath}'><i class='fas fa-book-open'></i> すべての API のリファレンスドキュメント一覧</a></b> > <b><a href='{this.RpcAbsoluteUrlPath}#{mi.Name}'><i class='fas fa-book-open'></i> {mi.Name} API のドキュメント</a></b></p>");
        }

        w.WriteLine("<p>　</p><HR>");

        w.WriteLine(this.Hook.GetFooterMenuText());

        w.WriteLine(@"
        </div>
    </div>
");

        Control_WriteHtmlFooter(w);

        return w.ToString();
    }

    string Control_GenerateApiInputFormPage(string methodName)
    {
        StringWriter w = new StringWriter();
        var mi = this.Api.GetMethodInfo(methodName);

        Control_WriteHtmlHeader(w, $"{mi.Name} - {this.ServerFriendlyNameHtml} Web API フォーム");

        w.WriteLine($"<form action='{this.ControlAbsoluteUrlPath}{mi.Name}/' method='get'>");
        w.WriteLine($"<input name='_call' type='hidden' value='1'/>");

        w.WriteLine(@"
    <div class='box'>
        <div class='content'>
");

        w.WriteLine(this.Hook.GetHeaderMenuText());

        w.WriteLine($"<h2 class='title is-4'>" + $"<i class='fas fa-user'></i> {this.ServerFriendlyNameHtml} Web API 呼び出しフォーム - {mi.Name} API" + "</h2>");

        w.WriteLine($"<h3 class='title is-5'>" + $"{mi.Name} API: {mi.Description._EncodeHtml()}" + "</h3>");

        string requireAuthStr = "";

        if (mi.RequireAuth)
        {
            requireAuthStr = "<i class='fas fa-key'></i> ";
        }

        w.WriteLine($"<p><b><a href='{this.ControlAbsoluteUrlPath}'><i class='fas fa-user'></i> API 呼び出し Web フォーム一覧</a></b> > <b><a href='{this.ControlAbsoluteUrlPath}{mi.Name}/'><i class='fas fa-user'></i> {requireAuthStr} {mi.Name} API 呼び出し用 Web フォーム</a></b></p>");

        int index = 0;

        foreach (var p in mi.ParametersHelpList)
        {
            index++;

            string sampleStr = p.SampleValueOneLineStr._IsEmpty() ? "" : $"<p>入力例: <code>{p.SampleValueOneLineStr._RemoveQuotation()._EncodeHtmlCodeBlock()}</code></p>";

            string defaultValueStr = p.DefaultValue._ObjectToJson(compact: true);
            if (p.DefaultValue == null) defaultValueStr = "";

            string controlStr = $@"
                    <div class='field'>
                        <p class='control'>
                            <input class='input is-info text-box single-line' name='{p.Name}' type='text' value='{defaultValueStr._RemoveQuotation()._EncodeHtml()}' />
                            {sampleStr}
<p>{p.Description._EncodeHtml()}</p>
                        </p>
                    </div>
";

            if (p.IsEnumType)
            {
                StringWriter optionsStr = new StringWriter();

                optionsStr.WriteLine($"<option value='__Not_Selected__' selected>▼ 項目の選択</option>");

                foreach (var kv in p.EnumValuesList!)
                {
                    optionsStr.WriteLine($"<option value='{kv.Key}'>{kv.Value}</option>");
                }

                controlStr = $@"
                <div class='field-body'>
                    <div class='field is-narrow'>
                        <p class='control'>
                                <select name='{p.Name}' class='input is-info'>
{optionsStr}
                                </select>
<p>{p.Description._EncodeHtml()}</p>
{sampleStr}
                        </p>
                    </div>
                </div>
";
            }

            w.WriteLine($@"
            <div class='field is-horizontal'>
                <div class='field-label is-normal'>
                    <label class='label'><i class='fas fa-user'></i> #{index} {p.Name}:<BR>({p.TypeName})</label>
                </div>
                <div class='field-body'>
                    {controlStr}
                </div>
            </div>");
        }

        w.WriteLine($@"
            <div class='field is-horizontal'>
                <div class='field-label'>
                    <!-- Left empty for spacing -->
                </div>
                <div class='field-body'>
                    <div class='field'>
                        <div class='control'>
                            <input class='button is-link' type='submit' style='font-weight: bold' value='これらのパラメータを指定してこの API を呼び出す'>
                            <p>　</p>
");


        if (this.Config.EnableBuiltinRichWebPages)
        {
            w.WriteLine("<p><b>API の呼び出し方法や効果などの解説に関するドキュメントが参考になる場合があります。</b></p>");
            w.WriteLine($"<p><b><a href='{this.RpcAbsoluteUrlPath}'><i class='fas fa-book-open'></i> すべての API のリファレンスドキュメント一覧</a></b> > <b><a href='{this.RpcAbsoluteUrlPath}#{mi.Name}'><i class='fas fa-book-open'></i> {mi.Name} API のドキュメント</a></b></p>");
        }

        w.WriteLine(@"
                        </div>
                    </div>");


        w.WriteLine(@"
                </div>
            </div>");


        w.WriteLine("<p>　</p><HR>");
        w.WriteLine(this.Hook.GetFooterMenuText());

        w.WriteLine(@"
        </div>
    </div>
");

        w.WriteLine("</form>");

        Control_WriteHtmlFooter(w);

        return w.ToString();
    }

    protected virtual void Control_WriteHtmlHeader(StringWriter w, string title)
    {
        string additionalStyle = @"
<style type='text/css'>
/* Please see documentation at https://docs.microsoft.com/aspnet/core/client-side/bundling-and-minification\ 
for details on configuring this project to bundle and minify static web assets. */
body {
    padding-top: 0px;
    padding-bottom: 0px;
}

/* Wrapping element */
/* Set some basic padding to keep content from hitting the edges */
.body-content {
    padding-left: 15px;
    padding-right: 15px;
}

/* Carousel */
.carousel-caption p {
    font-size: 20px;
    line-height: 1.4;
}

/* Make .svg files in the carousel display properly in older browsers */
.carousel-inner .item img[src$='.svg'] {
    width: 100%;
}

/* QR code generator */
#qrCode {
    margin: 15px;
}

/* Hide/rearrange for smaller screens */
@media screen and (max-width: 767px) {
    /* Hide captions */
    .carousel-caption {
        display: none;
    }
}

.footer {
    padding: 1rem 1rem 1rem;
}

pre[class*='language-'] {
    background: #f6f8fa;
}

/* Solve the conflict between Bulma and Prism */
code[class*='language-'] .tag,
code[class*='language-'] .number {
    align-items: stretch;
    background-color: transparent;
    border-radius: 0;
    display: inline;
    font-size: 1em;
    height: auto;
    justify-content: flex-start;
    line-height: normal;
    padding: 0;
    white-space: pre;
    margin-right: 0;
    min-width: auto;
    text-align: left;
    vertical-align: baseline;
}

code[class*=""language-""], pre[class*=""language-""] {
    white-space: pre-wrap !important;
    word-break: break-word !important;
}

</style>
";

        w.WriteLine($@"<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8' />
    <meta name='viewport' content='width=device-width, initial-scale=1.0' />
    <title>{title._EncodeHtml()}</title>

    <link rel='stylesheet' href='https://cdnjs.cloudflare.com/ajax/libs/bulma/0.7.5/css/bulma.css' integrity='sha256-ujE/ZUB6CMZmyJSgQjXGCF4sRRneOimQplBVLu8OU5w=' crossorigin='anonymous' />
    <link rel='stylesheet' href='https://cdn.jsdelivr.net/npm/bulma-extensions@6.2.7/dist/css/bulma-extensions.min.css' integrity='sha256-RuPsE2zPsNWVhhvpOcFlMaZ1JrOYp2uxbFmOLBYtidc=' crossorigin='anonymous'>
    <link rel='stylesheet' href='https://cdnjs.cloudflare.com/ajax/libs/font-awesome/5.14.0/css/all.min.css' integrity='sha512-1PKOgIY59xJ8Co8+NE6FZ+LOAZKjy+KY8iq0G4B3CyeY6wYHN3yt9PW0XpSriVlkMXe40PTKnXrLnZ9+fkDaog==' crossorigin='anonymous' />
    <link rel='stylesheet' href='https://cdnjs.cloudflare.com/ajax/libs/prism/1.16.0/themes/prism.css' integrity='sha256-Vl2/8UdUJhoDlkCr9CEJmv77kiuh4yxMF7gP1OYe6EA=' crossorigin='anonymous' />
{additionalStyle}
</head>
<body>");
    }

    protected virtual void Control_WriteHtmlFooter(StringWriter w)
    {
        w.WriteLine(@"

    <script src='https://cdn.jsdelivr.net/npm/bulma-extensions@6.2.7/dist/js/bulma-extensions.js' integrity='sha256-02UMNoxzmxWzx36g1Y4tr93G0oHuz+khCNMBbilTBAg=' crossorigin='anonymous'></script>
    <script src='https://cdnjs.cloudflare.com/ajax/libs/vue/2.6.10/vue.js' integrity='sha256-ufGElb3TnOtzl5E4c/qQnZFGP+FYEZj5kbSEdJNrw0A=' crossorigin='anonymous'></script>
    <script src='https://cdn.jsdelivr.net/npm/buefy@0.7.10/dist/buefy.js' integrity='sha256-jzFK32XQpxIXsHY1dSlIXAerpfvTaKKBLrOGX76W9AU=' crossorigin='anonymous'></script>
    <script src='https://cdnjs.cloudflare.com/ajax/libs/axios/0.19.0/axios.js' integrity='sha256-XmdRbTre/3RulhYk/cOBUMpYlaAp2Rpo/s556u0OIKk=' crossorigin='anonymous'></script>
    <script src='https://cdnjs.cloudflare.com/ajax/libs/lodash.js/4.17.15/lodash.js' integrity='sha256-kzv+r6dLqmz7iYuR2OdwUgl4X5RVsoENBzigdF5cxtU=' crossorigin='anonymous'></script>
    <script src='https://cdnjs.cloudflare.com/ajax/libs/moment.js/2.24.0/moment.js' integrity='sha256-H9jAz//QLkDOy/nzE9G4aYijQtkLt9FvGmdUTwBk6gs=' crossorigin='anonymous'></script>
    <script src='https://cdnjs.cloudflare.com/ajax/libs/prism/1.16.0/prism.js' integrity='sha256-DIDyRYnz+KgK09kOQq3WVsIv1dcMpTZyuWimu3JMCj8=' crossorigin='anonymous'></script>
    <script src='https://cdnjs.cloudflare.com/ajax/libs/prism/1.16.0/components/prism-json.js' integrity='sha256-xRwOqwAdoQU7w2xCV4YQpPH5TaJMEMtzHExglTQTUZ4=' crossorigin='anonymous'></script>
    <script src='https://cdnjs.cloudflare.com/ajax/libs/prism/1.16.0/components/prism-bash.js' integrity='sha256-R8B0GSG+S1gSO1tPkcl2Y+s3qryWdPQDHOy4JxZtuJI=' crossorigin='anonymous'></script>
</body>
</html>
");
    }

    // /rpc の GET ハンドラ
    public virtual async Task Rpc_GetRequestHandler(HttpRequest request, HttpResponse response, RouteData routeData)
    {
        CancellationToken cancel = request._GetRequestCancellationToken();

        try
        {
            string rpcMethod = routeData.Values._GetStr("rpc_method");
            if (rpcMethod._IsEmpty())
            {
                var baseUri = request.GetEncodedUrl()._ParseUrl()._CombineUrl(this.RpcAbsoluteUrlPath);

                if (this.Config.EnableBuiltinRichWebPages)
                {
                    await response._SendStringContentsAsync(Rpc_GenerateHtmlHelpString(baseUri), contentsType: Consts.MimeTypes.HtmlUtf8, cancel: cancel, normalizeCrlf: CrlfStyle.CrLf);
                }
                else
                {
                    await response._SendStringContentsAsync($"This is a JSON-RPC server.\r\nAPI: {Api.GetType().AssemblyQualifiedName}\r\nNow: {DateTimeOffset.Now._ToDtStr(withNanoSecs: true)}\r\n", cancel: cancel, normalizeCrlf: CrlfStyle.Lf);
                }
            }
            else
            {
                await this.CheckIsCrossSiteRefererAndDenyAsync(request, response);

                string args = routeData.Values._GetStr("rpc_param");

                if (args._IsEmpty())
                {
                    JObject jObj = new JObject();

                    foreach (string key in request.Query.Keys)
                    {
                        string value = request.Query[key];

                        string valueTrim = value.Trim();
                        if (valueTrim.StartsWith("{") && valueTrim.EndsWith("}"))
                        {
                            // JSON Data
                            jObj.Add(key, value._JsonToObject<JObject>());
                        }
                        else
                        {
                            // Primitive Date
                            jObj.Add(key, JToken.FromObject(value));
                        }
                    }

                    args = jObj._ObjectToJson(compact: true);
                }

                //string id = "GET-" + Str.NewGuid().ToUpperInvariant();
                string in_str = "{'jsonrpc':'2.0','method':'" + rpcMethod + "','params':" + args + "}";

                await ProcessHttpRequestMain(request, response, in_str, Consts.MimeTypes.TextUtf8, Consts.HttpStatusCodes.InternalServerError, true, paramsStrComparison: StringComparison.OrdinalIgnoreCase);
            }
        }
        catch (Exception ex)
        {
            await response._SendStringContentsAsync("Request Error: " + ex.ToString(), cancel: cancel, statusCode: Consts.HttpStatusCodes.InternalServerError, normalizeCrlf: CrlfStyle.Lf);
        }
    }

    // /rpc の POST ハンドラ
    public virtual async Task Rpc_PostRequestHandler(HttpRequest request, HttpResponse response, RouteData routeData)
    {
        await this.CheckIsCrossSiteRefererAndDenyAsync(request, response);

        try
        {
            string in_str = await request._RecvStringContentsAsync(this.Config.MaxRequestBodyLen, cancel: request._GetRequestCancellationToken());

            await ProcessHttpRequestMain(request, response, in_str);
        }
        catch (Exception ex)
        {
            await response._SendStringContentsAsync(ex.ToString(), cancel: request._GetRequestCancellationToken(), normalizeCrlf: CrlfStyle.Lf);
        }
    }

    protected virtual async Task ProcessHttpRequestMain(HttpRequest request, HttpResponse response, string inStr, string responseContentsType = Consts.MimeTypes.Json, int httpStatusWhenError = Consts.HttpStatusCodes.Ok, bool simpleResultWhenOk = false, StringComparison paramsStrComparison = StringComparison.Ordinal)
    {
        int statusCode = Consts.HttpStatusCodes.Ok;

        string retStr = "";
        try
        {
            SortedDictionary<string, string> requestHeaders = new SortedDictionary<string, string>();
            foreach (string headerName in request.Headers.Keys)
            {
                if (request.Headers.TryGetValue(headerName, out var val))
                {
                    requestHeaders.Add(headerName, val.ToString());
                }
            }

            string suppliedUsername = "";
            string suppliedPassword = "";

            // ヘッダにおける認証クレデンシャルが提供されているかどうか調べる
            suppliedUsername = request.Headers._GetStrFirst("X-RPC-Auth-Username");
            suppliedPassword = request.Headers._GetStrFirst("X-RPC-Auth-Password");

            // Query String における認証クレデンシャルが提供されているかどうか調べる
            if (suppliedUsername._IsEmpty() || suppliedPassword._IsEmpty())
            {
                suppliedUsername = request._GetQueryStringFirst("rpc_auth_username");
                suppliedPassword = request._GetQueryStringFirst("rpc_auth_password");
            }

            if (suppliedUsername._IsEmpty() || suppliedPassword._IsEmpty())
            {
                // Basic 認証または Query String における認証クレデンシャルが提供されているかどうか調べる
                var basicAuthHeader = BasicAuthImpl.ParseBasicAuthenticationHeader(request.Headers._GetStrFirst("Authorization"));
                if (basicAuthHeader != null)
                {
                    suppliedUsername = basicAuthHeader.Item1;
                    suppliedPassword = basicAuthHeader.Item2;
                }
            }

            var conn = request.HttpContext.Connection;

            JsonRpcClientInfo client_info = new JsonRpcClientInfo(
                request.Scheme,
                request.Host.ToString(),
                request.GetDisplayUrl(),
                conn.LocalIpAddress!._UnmapIPv4().ToString(), conn.LocalPort,
                conn.RemoteIpAddress!._UnmapIPv4().ToString(), conn.RemotePort,
                requestHeaders,
                suppliedUsername,
                suppliedPassword);

            var callResults = await this.CallMethods(inStr, client_info, simpleResultWhenOk, paramsStrComparison);

            if (HideErrorDetails) callResults.RemoveErrorDetailsFromResultString();

            retStr = callResults.ResultString;

            if (callResults.Error_AuthRequired)
            {
                // Basic 認証の要求
                KeyValueList<string, string> basicAuthResponseHeaders = new KeyValueList<string, string>();
                string realm = $"User Authentication for {this.RpcAbsoluteUrlPath}";
                if (callResults.Error_AuthRequiredRealmName._IsFilled())
                {
                    realm += $" ({realm._MakeVerySafeAsciiOnlyNonSpaceFileName()})";
                }
                basicAuthResponseHeaders.Add(Consts.HttpHeaders.WWWAuthenticate, $"Basic realm=\"{realm}\"");

                await using var basicAuthRequireResult = new HttpStringResult(retStr, contentType: Consts.MimeTypes.TextUtf8, statusCode: Consts.HttpStatusCodes.Unauthorized, additionalHeaders: basicAuthResponseHeaders);

                await response._SendHttpResultAsync(basicAuthRequireResult, cancel: request._GetRequestCancellationToken());

                return;
            }

            if (callResults.AllError)
            {
                statusCode = httpStatusWhenError;
            }
        }
        catch (Exception ex)
        {
            JsonRpcException json_ex;
            if (ex is JsonRpcException) json_ex = (JsonRpcException)ex;
            else json_ex = new JsonRpcException(new JsonRpcError(1234, ex._GetSingleException().Message, ex.ToString()));

            retStr = new JsonRpcResponseError()
            {
                Error = json_ex.RpcError,
                Id = null,
                Result = null,
            }._ObjectToJson();

            statusCode = httpStatusWhenError;
        }

        await response._SendStringContentsAsync(retStr, responseContentsType, cancel: request._GetRequestCancellationToken(), statusCode: statusCode, normalizeCrlf: CrlfStyle.Lf);
    }


    Once stopOnce;
    public void FreeRegisteredResources()
    {
        if (stopOnce.IsFirstCall())
        {
            this.LogBrowser._DisposeSafe();

            this.GetMyIpDnsResolver._DisposeSafe();

            this.GetMyIpDnsServerSettingsHive._DisposeSafe();
        }
    }

    // Web Form ヘルプ文字列を生成する
    readonly FastCache<Uri, string> WebFormHelpStringCache = new FastCache<Uri, string>(10000);
    string Control_GenerateHtmlHelpString(Uri webFormBaseUri)
    {
        return WebFormHelpStringCache.GetOrCreate(webFormBaseUri, x => Control_GenerateHtmlHelpStringCore(x))!;
    }
    string Control_GenerateHtmlHelpStringCore(Uri webFormBaseUri)
    {
        var methodList = this.Api.EnumMethodsForHelp();

        StringWriter w = new StringWriter();

        int methodIndex = 0;


        Control_WriteHtmlHeader(w, $"{this.ServerFriendlyNameHtml} - JSON-RPC サーバー API コントロール パネル - API 一覧");

        w.WriteLine($@"
    <div class='container is-fluid'>

<div class='box'>
    <div class='content'>

        {this.Hook.GetHeaderMenuText()}

<h2 class='title is-4'><i class='fas fa-user'></i> {this.ServerFriendlyNameHtml} - JSON-RPC サーバー API コントロール パネル API 一覧</h2>
        
<h4 class='title is-5'>このサーバーには、{methodList.Count} 個の API が存在します。これらは、以下のコントロールパネルの Web フォームから簡単に呼び出せます。</h4>

<p><a href='{this.RpcAbsoluteUrlPath}'><i class='fas fa-hand-point-right'></i> <b>コマンドラインの curl/wget ツールや任意の開発アプリケーションから、JSON-RPC API を直接呼び出すことも可能です。</a></b></p>
");

        w.WriteLine("<ul>");

        foreach (var m in methodList)
        {
            methodIndex++;

            string requireAuthStr = "";

            if (m.RequireAuth)
            {
                requireAuthStr = "<i class='fas fa-key'></i> ";
            }

            string titleStr = $"<a href='{this.ControlAbsoluteUrlPath}{m.Name}/'><b><i class='fas fa-user'></i> {methodIndex}. {requireAuthStr}{m.Name} API を Web フォームから呼び出す</b></a>{(m.Description._IsFilled() ? " <BR>" : "")} <b>{m.Description._EncodeHtml()}</b>".Trim();

            //w.WriteLine();
            //w.WriteLine($"- {helpStr}");
            w.WriteLine($"<li>{titleStr}<BR><BR></li>");
        }

        w.WriteLine("</ul>");

        w.WriteLine();
        w.WriteLine();
        w.WriteLine();

        w.WriteLine(@$"
        <p>　</p><HR>
        {this.Hook.GetFooterMenuText()}
    </div>
    </div>
");

        this.Control_WriteHtmlFooter(w);

        return w.ToString();
    }


    // Flush cache
    void FlushCache()
    {
        this.WebFormHelpStringCache.Clear();

        this.RpcHelpStringCache.Clear();
    }

    // RPC API ヘルプ文字列を生成する
    readonly FastCache<Uri, string> RpcHelpStringCache = new FastCache<Uri, string>(10000);
    string Rpc_GenerateHtmlHelpString(Uri rpcBaseUri)
    {
        return RpcHelpStringCache.GetOrCreate(rpcBaseUri, x => Rpc_GenerateHtmlHelpStringCore(x))!;
    }
    string Rpc_GenerateHtmlHelpStringCore(Uri rpcBaseUri)
    {
        var methodList = this.Api.EnumMethodsForHelp();

        StringWriter w = new StringWriter();

        int methodIndex = 0;


        Control_WriteHtmlHeader(w, $"{this.ServerFriendlyNameHtml} - JSON-RPC サーバー API リファレンスドキュメント一覧");

        w.WriteLine($@"
    <div class='container is-fluid'>

<div class='box'>
    <div class='content'>

        {this.Hook.GetHeaderMenuText()}

<h2 class='title is-4'><i class='fas fa-dice-d6'></i> {this.ServerFriendlyNameHtml} - JSON-RPC サーバー API リファレンスドキュメント一覧および直接呼び出し方法</h2>
        
<h4 class='title is-5'>{methodList.Count} 個の RPC-API メソッドが存在します。各 API のリファレンスと呼び出し方の解説は、以下のとおりです:</h4>

<p><a href='{this.ControlAbsoluteUrlPath}'><i class='fas fa-home'></i> <b>Web フォームからも、各 JSON-RPC API を簡単に呼び出すことができます。</a></b></p>

");

        w.WriteLine("<ul>");

        foreach (var m in methodList)
        {
            methodIndex++;

            string titleStr = $"<a href='#{m.Name}'><b><i class='fas fa-dice-d6'></i> {methodIndex}. {m.Name} API</b></a>{(m.Description._IsFilled() ? "<BR>" : "")} <b>{m.Description._EncodeHtml()}</b>".Trim();

            //w.WriteLine();
            //w.WriteLine($"- {helpStr}");
            w.WriteLine($"<li>{titleStr}<BR><BR></li>");
        }

        w.WriteLine("</ul>");

        w.WriteLine();
        w.WriteLine();
        w.WriteLine();

        methodIndex = 0;

        foreach (var m in methodList)
        {
            var mi = m;

            methodIndex++;

            w.WriteLine($"<hr id={m.Name}>");

            string titleStr = $"<i class='fas fa-dice-d6'></i> RPC メソッド #{methodIndex}: {m.Name} API{(m.Description._IsFilled() ? ":" : "")} {m.Description._EncodeHtml()}".Trim();

            w.WriteLine($"<h4 class='title is-5'>{titleStr}</h4>");

            string requireAuthStr = "";

            if (mi.RequireAuth)
            {
                requireAuthStr = "<i class='fas fa-key'></i> ";
            }

            w.WriteLine($"<a class='button is-info' style='font-weight: bold' href='{this.ControlAbsoluteUrlPath}{mi.Name}/'><i class='fas fa-user'></i>&nbsp;{requireAuthStr}&nbsp;この {mi.Name} API を コントロールパネルの Web フォームから呼び出す</a>");

            w.WriteLine();
            w.WriteLine("<p>　</p>");
            w.WriteLine($"<p>RPC メソッド名: <b><code class='language-shell'>{m.Name}()</code></b></p>");
            w.WriteLine($"<p>RPC の定義: <b><code class='language-shell'>{m.Name}({(m.ParametersHelpList.Select(x => x.Name + ": " + x.TypeName + (x.Mandatory ? "" : " = " + x.DefaultValue._ObjectToJson(compact: true)))._Combine(", "))}): {m.RetValueTypeName};</code></b></p>");
            w.WriteLine();

            if (m.Description._IsFilled())
            {
                w.WriteLine($"<p>RPC の説明: <b>{m.Description._EncodeHtml()}</b></p>");
                w.WriteLine();
            }

            w.WriteLine($"<p>RPC の呼び出し時の認証要求: {m.RequireAuth._ToBoolYesNoStr()}</p>");
            w.WriteLine();

            var pl = m.ParametersHelpList;

            var sampleRequestJsonData = Json.NewJsonObject();

            QueryStringList qsList = new QueryStringList();

            if (pl.Count == 0)
            {
                w.WriteLine($"<p>この RPC には、入力パラメータは不要です。</p>");
                w.WriteLine();
            }
            else
            {
                w.WriteLine($"<p>この RPC を呼び出すため際は、下記の {pl.Count} 個の入力パラメータを指定します。</p>");

                for (int i = 0; i < pl.Count; i++)
                {
                    var pp = pl[i];
                    string? qsSampleOrDefaultValue = null;

                    w.WriteLine("<p>" + $"パラメータ #{i + 1}: <b><code class='language-shell'>{pp.Name}</code></b>".TrimEnd() + "</p>");
                    w.WriteLine("<ul>");
                    if (pp.Description._IsFilled())
                    {
                        w.WriteLine($"<li>説明: {pp.Description._EncodeHtml()}</li>");
                    }
                    if (pp.IsPrimitiveType)
                    {
                        if (pp.IsEnumType == false)
                        {
                            w.WriteLine($"<li>入力データ型: <code class='language-shell'>Primitive Value - {pp.TypeName}</code></li>");
                        }
                        else
                        {
                            StringWriter enumWriter = new StringWriter();
                            enumWriter.WriteLine($"enum {pp.Type.Name} {{");
                            foreach (var enumItem in Str.GetEnumValuesList(pp.Type).OrderBy(x => x.Value))
                            {
                                enumWriter.WriteLine($"    {enumItem.Key}: {Convert.ToUInt64(enumItem.Value)},");
                            }
                            enumWriter.WriteLine("}");
                            w.WriteLine($"<li>入力データ型: 列挙型 - {pp.TypeName}<BR>");
                            w.Write($"<pre><code class='language-json'>{enumWriter.ToString()._EncodeHtmlCodeBlock()}</code></pre>");
                            w.WriteLine($"</li>");
                        }
                    }
                    else
                    {
                        w.WriteLine($"<li>入力データ型: JSON データ - <code class='language-shell'>{pp.TypeName}</code></li>");
                    }

                    if (pp.SampleValueOneLineStr._IsFilled())
                    {
                        if (pp.IsPrimitiveType)
                        {
                            w.WriteLine($"<li>入力データのサンプル: <code>{pp.SampleValueOneLineStr._EncodeHtmlCodeBlock()}</code></li>");
                        }
                        else
                        {
                            w.WriteLine($"<li>入力データのサンプル (JSON):<BR>");
                            w.Write($"<pre><code class='language-json'>{pp.SampleValueMultiLineStr._EncodeHtmlCodeBlock()}</code></pre>");
                            w.WriteLine($"</li>");
                        }
                        qsSampleOrDefaultValue = pp.SampleValueOneLineStr;
                    }
                    w.WriteLine($"<li>必須パラメータ: {pp.Mandatory._ToBoolYesNoStr()}</li>");
                    if (pp.Mandatory == false)
                    {
                        w.WriteLine($"<li>省略時のデフォルト値: <code>{pp.DefaultValue._ObjectToJson(includeNull: true, compact: true)._EncodeHtml()}</code></li>");
                        if (qsSampleOrDefaultValue == null)
                        {
                            qsSampleOrDefaultValue = pp.DefaultValue._ObjectToJson(includeNull: true, compact: true);
                        }
                    }

                    if (qsSampleOrDefaultValue == null)
                    {
                        if (pp.IsPrimitiveType)
                        {
                            qsSampleOrDefaultValue = $"value_{i + 1}";
                        }
                        else
                        {
                            qsSampleOrDefaultValue = "{JSON_Input_Value_for_No_{i + 1}_Here}";
                        }
                    }

                    // Query String の値は文字列であっても "" で囲む必要がない
                    if (qsSampleOrDefaultValue.Length >= 2 && qsSampleOrDefaultValue.StartsWith("\"") && qsSampleOrDefaultValue.EndsWith("\""))
                    {
                        qsSampleOrDefaultValue = qsSampleOrDefaultValue.Substring(1, qsSampleOrDefaultValue.Length - 2);
                    }

                    qsList.Add(pp.Name, qsSampleOrDefaultValue);

                    JsonSerializerSettings settings = new JsonSerializerSettings();
                    Json.AddStandardSettingsToJsonConverter(settings, JsonFlags.AllEnumToStr, null);
                    JsonSerializer serializer = JsonSerializer.Create(settings);

                    sampleRequestJsonData.TryAdd(pp.Name, JToken.FromObject(pp.SampleValueObject!, serializer));

                    w.WriteLine("</ul>");
                    w.WriteLine();
                }

            }

            if (m.HasRetValue == false)
            {
                w.WriteLine("<p>この RPC には、応答データはありません。</p>");
                w.WriteLine();
            }
            else
            {
                w.WriteLine("<p><b>この RPC は、以下のような応答データを返却します:</b></p>");
                if (m.IsRetValuePrimitiveType)
                {
                    w.WriteLine($"<p>応答データの型: プリミティブ型 - {m.RetValueTypeName}</p>");
                }
                else
                {
                    w.WriteLine($"<p>応答データの型: JSON データ - {m.RetValueTypeName}</p>");
                }

                if (m.RetValueSampleValueJsonMultilineStr._IsFilled())
                {
                    w.WriteLine("<p>応答データのサンプル:</p>");

                    w.Write($"<pre><code class='language-json'>{m.RetValueSampleValueJsonMultilineStr._EncodeHtmlCodeBlock()}</code></pre>");
                }

                w.WriteLine("");
            }

            w.WriteLine();

            var urlEncodeParam = new UrlEncodeParam("\"'{}:=,");

            if (m.RequireAuth == false)
            {
                string urlDir = rpcBaseUri._CombineUrlDir(m.Name).ToString();
                if (qsList.Any())
                {
                    urlDir += "?" + qsList.ToString(null, urlEncodeParam);
                }

                w.WriteLine("<p>　</p>");
                w.WriteLine($"<h5 class='title is-6'>RPC 呼び出し方法のサンプル: HTTP GET (Query String 付き) の例</h5>");

                StringWriter tmp = new StringWriter();
                tmp.WriteLine($"# HTTP GET ターゲット URL (Web ブラウザ上で簡単に試すことができます):");
                tmp.WriteLine($"{urlDir}");
                tmp.WriteLine();
                tmp.WriteLine($"# wget コマンドライン サンプル - Linux bash 上を想定 (成功した場合は、応答コード = 0 が返ります):");
                tmp.WriteLine($"wget --content-on-error --no-verbose -O - --no-check-certificate {urlDir._EscapeBashArg()}");
                tmp.WriteLine();
                tmp.WriteLine($"# curl コマンドライン サンプル - Linux bash 上を想定 (成功した場合は、応答コード = 0 が返ります):");
                tmp.WriteLine($"curl --get --globoff --fail -k --raw --verbose {urlDir._EscapeBashArg()}");
                tmp.WriteLine();

                w.WriteLine($"<pre><code class='language-shell'>{tmp.ToString()._EncodeHtmlCodeBlock()}</code></pre>");

                JsonRpcRequestForHelp reqSample = new JsonRpcRequestForHelp
                {
                    Version = "2.0",
                    Method = m.Name,
                    Params = sampleRequestJsonData,
                };

                w.WriteLine($"<h5 class='title is-6'>RPC 呼び出し方法のサンプル: HTTP POST (JSON-RPC 規格準拠) の例:</h5>");

                tmp = new StringWriter();
                tmp.WriteLine($"# HTTP POST ターゲット URL:");
                tmp.WriteLine($"{rpcBaseUri}");
                tmp.WriteLine();
                tmp.WriteLine($"# curl JSON-RPC 呼び出しコマンドライン サンプル - Linux bash 上の想定:");
                tmp.WriteLine($"cat <<\\EOF | curl --request POST --globoff --fail -k --raw --verbose --data @- '{rpcBaseUri}'");
                tmp.Write(reqSample._ObjectToJson(includeNull: true, compact: false)._NormalizeCrlf(ensureLastLineCrlf: true));
                tmp.WriteLine("EOF");
                tmp.WriteLine();

                w.WriteLine($"<pre><code class='language-shell'>{tmp.ToString()._EncodeHtmlCodeBlock()}</code></pre>");

                w.WriteLine($"<h5 class='title is-6'>JSON-RPC の実行に成功した場合は、以下のような応答データが返ります:</h5>");
                var okSample = new JsonRpcResponseForHelp_Ok
                {
                    Version = "2.0",
                    Result = m.RetValueSampleValueObject,
                };

                w.Write($"<pre><code class='language-json'>{okSample._ObjectToJson(includeNull: true, compact: false)._NormalizeCrlf(ensureLastLineCrlf: true)._EncodeHtmlCodeBlock()}</code></pre>");
                w.WriteLine();

                w.WriteLine($"<h5 class='title is-6'>JSON-RPC の実行中でエラーが発生した場合は、以下のようなエラーメッセージが返ります:</h5>");

                var errorSample = new JsonRpcResponseForHelp_Error
                {
                    Version = "2.0",
                    Error = new JsonRpcError
                    {
                        Code = -32603,
                        Message = "Sample Error",
                        Data = "Sample Error Detail Data\nThis is a sample error data.",
                    },
                };
                w.Write($"<pre><code class='language-json'>{errorSample._ObjectToJson(includeNull: true, compact: false)._NormalizeCrlf(ensureLastLineCrlf: true)._EncodeHtmlCodeBlock()}</code></pre>");
                w.WriteLine();
            }
            else
            {
                string urlSimple = rpcBaseUri._CombineUrlDir(m.Name).ToString();
                if (qsList.Any())
                {
                    urlSimple += "?" + qsList.ToString(null, urlEncodeParam);
                }

                string urlWithBasicAuth = rpcBaseUri._CombineUrlDir(m.Name).ToString();
                if (qsList.Any())
                {
                    urlWithBasicAuth += "?" + qsList.ToString(null, urlEncodeParam);
                }
                urlWithBasicAuth = urlWithBasicAuth._AddCredentialOnUrl(Consts.Strings.DefaultAdminUsername, Consts.Strings.DefaultAdminPassword);

                string urlWithQsAuth = rpcBaseUri._CombineUrlDir(m.Name).ToString();
                urlWithQsAuth += $"?rpc_auth_username={Consts.Strings.DefaultAdminUsername}&rpc_auth_password={Consts.Strings.DefaultAdminPassword}";
                if (qsList.Any())
                {
                    urlWithQsAuth += "&" + qsList.ToString(null, urlEncodeParam);
                }

                w.WriteLine("<p>　</p>");
                w.WriteLine($"<h5 class='title is-6'>RPC 呼び出し方法のサンプル: HTTP GET (Query String 付き) の例</h5>");

                StringWriter tmp = new StringWriter();
                tmp.WriteLine($"# HTTP GET ターゲット URL (Web ブラウザ上で簡単に試すことができます):");
                tmp.WriteLine("# (HTTP Basic 認証を用いたユーザー認証を行なう場合)");
                tmp.WriteLine($"{urlWithBasicAuth}");
                tmp.WriteLine("# (HTTP Query String を用いた認証データ付加によるユーザー認証を行なう場合)");
                tmp.WriteLine($"{urlWithQsAuth}");
                tmp.WriteLine();
                tmp.WriteLine($"# wget コマンドライン サンプル - Linux bash 上を想定 (成功した場合は、応答コード = 0 が返ります):");
                tmp.WriteLine("# (HTTP Basic 認証を用いたユーザー認証を行なう場合)");
                tmp.WriteLine($"wget --content-on-error --no-verbose -O - --no-check-certificate {urlWithBasicAuth._EscapeBashArg()}");
                tmp.WriteLine("# (HTTP Query String を用いた認証データ付加によるユーザー認証を行なう場合)");
                tmp.WriteLine($"wget --content-on-error --no-verbose -O - --no-check-certificate {urlWithQsAuth._EscapeBashArg()}");
                tmp.WriteLine("# (HTTP ヘッダ認証データ付加によるユーザー認証を行なう場合)");
                tmp.WriteLine($"wget --content-on-error --no-verbose -O - --no-check-certificate --header 'X-RPC-Auth-Username: {Consts.Strings.DefaultAdminUsername}' --header 'X-RPC-Auth-Password: {Consts.Strings.DefaultAdminPassword}' {urlSimple._EscapeBashArg()}");
                tmp.WriteLine();
                tmp.WriteLine($"# curl コマンドライン サンプル - Linux bash 上を想定 (成功した場合は、応答コード = 0 が返ります):");
                tmp.WriteLine("# (HTTP Basic 認証を用いたユーザー認証を行なう場合)");
                tmp.WriteLine($"curl --get --globoff --fail -k --raw --verbose {urlWithBasicAuth._EscapeBashArg()}");
                tmp.WriteLine("# (HTTP Query String を用いた認証データ付加によるユーザー認証を行なう場合)");
                tmp.WriteLine($"curl --get --globoff --fail -k --raw --verbose {urlWithQsAuth._EscapeBashArg()}");
                tmp.WriteLine("# (HTTP ヘッダ認証データ付加によるユーザー認証を行なう場合)");
                tmp.WriteLine($"curl --get --globoff --fail -k --raw --verbose --header 'X-RPC-Auth-Username: {Consts.Strings.DefaultAdminUsername}' --header 'X-RPC-Auth-Password: {Consts.Strings.DefaultAdminPassword}' {urlSimple._EscapeBashArg()}");
                tmp.WriteLine();

                w.WriteLine($"<pre><code class='language-shell'>{tmp.ToString()._EncodeHtmlCodeBlock()}</code></pre>");

                JsonRpcRequestForHelp reqSample = new JsonRpcRequestForHelp
                {
                    Version = "2.0",
                    Method = m.Name,
                    Params = sampleRequestJsonData,
                };

                w.WriteLine($"<h5 class='title is-6'>RPC 呼び出し方法のサンプル: HTTP POST (JSON-RPC 規格準拠) の例:</h5>");

                tmp = new StringWriter();
                tmp.WriteLine($"# HTTP POST ターゲット URL:");
                tmp.WriteLine($"{urlWithBasicAuth}");
                tmp.WriteLine();
                tmp.WriteLine($"#  curl コマンドライン サンプル - Linux bash 上を想定:");
                tmp.WriteLine($"cat <<\\EOF | curl --request POST --globoff --fail -k --raw --verbose --data @- '{rpcBaseUri}'");
                tmp.Write(reqSample._ObjectToJson(includeNull: true, compact: false)._NormalizeCrlf(ensureLastLineCrlf: true));
                tmp.WriteLine("EOF");
                tmp.WriteLine();

                w.WriteLine($"<pre><code class='language-shell'>{tmp.ToString()._EncodeHtmlCodeBlock()}</code></pre>");

                w.WriteLine($"<h5 class='title is-6'>JSON-RPC の実行に成功した場合は、以下のような応答データが返ります:</h5>");
                var okSample = new JsonRpcResponseForHelp_Ok
                {
                    Version = "2.0",
                    Result = m.RetValueSampleValueObject,
                };

                w.Write($"<pre><code class='language-json'>{okSample._ObjectToJson(includeNull: true, compact: false)._NormalizeCrlf(ensureLastLineCrlf: true)._EncodeHtmlCodeBlock()}</code></pre>");
                w.WriteLine();

                w.WriteLine($"<h5 class='title is-6'>JSON-RPC の実行中でエラーが発生した場合は、以下のようなエラーメッセージが返ります:</h5>");
                var errorSample = new JsonRpcResponseForHelp_Error
                {
                    Version = "2.0",
                    Error = new JsonRpcError
                    {
                        Code = -32603,
                        Message = "Sample Error",
                        Data = "Sample Error Detail Data\nThis is a sample error data.",
                    },
                };
                //w.WriteLine($"--------------------");
                //w.Write(errorSample._ObjectToJson(includeNull: true, compact: false)._NormalizeCrlf(ensureLastLineCrlf: true));
                //w.WriteLine($"--------------------");
                w.Write($"<pre><code class='language-json'>{errorSample._ObjectToJson(includeNull: true, compact: false)._NormalizeCrlf(ensureLastLineCrlf: true)._EncodeHtmlCodeBlock()}</code></pre>");
                w.WriteLine();
            }

            w.WriteLine($"<a href='#'><b><i class='fas fa-arrow-up'></i> API 一覧に戻る</b></a>");

            w.WriteLine();
            w.WriteLine();
            w.WriteLine();
        }

        w.WriteLine(@$"
        <p>　</p><HR>
            {this.Hook.GetFooterMenuText()}
    </div>
   </div>
");

        this.Control_WriteHtmlFooter(w);

        return w.ToString();
    }

    LogBrowser? LogBrowser = null;

    public void RegisterRoutesToHttpServer(IApplicationBuilder appBuilder,
        string rpcPath = "/rpc", string controlPath = "/control", string configPath = "/admin_config", string objEditPath = "/admin_objedit",
        string objSearchPath = "/admin_search", string logBrowserPath = "/admin_logbrowser",
        string getMyIpPath = "/getmyip",
        string healthCheckPath = "/health_check",
        LogBrowserOptions? logBrowserOptions = null)
    {
        rpcPath = rpcPath._NonNullTrim();
        if (rpcPath.StartsWith("/") == false) throw new CoresLibException($"Invalid absolute path: '{rpcPath}'");
        if (rpcPath.Length >= 2 && rpcPath.EndsWith("/")) throw new CoresLibException($"Path must not end with '/'.");

        controlPath = controlPath._NonNullTrim();
        if (controlPath.StartsWith("/") == false) throw new CoresLibException($"Invalid absolute path: '{controlPath}'");
        if (controlPath.Length >= 2 && controlPath.EndsWith("/")) throw new CoresLibException($"Path must not end with '/'.");

        configPath = configPath._NonNullTrim();
        if (configPath.StartsWith("/") == false) throw new CoresLibException($"Invalid absolute path: '{configPath}'");
        if (configPath.Length >= 2 && configPath.EndsWith("/")) throw new CoresLibException($"Path must not end with '/'.");

        objEditPath = objEditPath._NonNullTrim();
        if (objEditPath.StartsWith("/") == false) throw new CoresLibException($"Invalid absolute path: '{objEditPath}'");
        if (objEditPath.Length >= 2 && objEditPath.EndsWith("/")) throw new CoresLibException($"Path must not end with '/'.");

        objSearchPath = objSearchPath._NonNullTrim();
        if (objSearchPath.StartsWith("/") == false) throw new CoresLibException($"Invalid absolute path: '{objSearchPath}'");
        if (objSearchPath.Length >= 2 && objSearchPath.EndsWith("/")) throw new CoresLibException($"Path must not end with '/'.");

        logBrowserPath = logBrowserPath._NonNullTrim();
        if (logBrowserPath.StartsWith("/") == false) throw new CoresLibException($"Invalid absolute path: '{logBrowserPath}'");
        if (logBrowserPath.Length >= 2 && logBrowserPath.EndsWith("/")) throw new CoresLibException($"Path must not end with '/'.");


        RouteBuilder rb = new RouteBuilder(appBuilder);

        if (this.Config.TopPageRedirectToControlPanel && this.Config.EnableBuiltinRichWebPages)
        {
            rb.MapGet("/", async (req, res, route) =>
            {
                await res._SendRedirectAsync(controlPath + "/", cancel: req._GetRequestCancellationToken());
            });
        }

        rb.MapGet(rpcPath, Rpc_GetRequestHandler);
        rb.MapGet(rpcPath + "/{rpc_method}", Rpc_GetRequestHandler);
        rb.MapGet(rpcPath + "/{rpc_method}/{rpc_param}", Rpc_GetRequestHandler);
        rb.MapPost(rpcPath, Rpc_PostRequestHandler);

        if (this.Config.EnableGetMyIpServer)
        {
            // Settings を読み込む
            this.GetMyIpDnsServerSettingsHive = new HiveData<JsonRpcHttpServerGetMyIpServerSettings>(Hive.SharedLocalConfigHive, $"HttpJsonRpcGetMyIpServer", null, HiveSyncPolicy.AutoReadFromFile);

            List<IPEndPoint> dnsServers = new List<IPEndPoint>();

            foreach (var host in this.GetMyIpServerSettings!.DnsServerList)
            {
                var ep = host._ToIPEndPoint(53, allowed: AllowedIPVersions.IPv4, true);
                if (ep != null)
                {
                    dnsServers.Add(ep);
                }
            }

            if (dnsServers.Count == 0)
                throw new CoresLibException("dnsServers.Count == 0");

            this.GetMyIpDnsResolver = new DnsClientLibBasedDnsResolver(
                new DnsResolverSettings(
                    flags: DnsResolverFlags.RoundRobinServers | DnsResolverFlags.UdpOnly,
                    dnsServersList: dnsServers
                    )
                );

            rb.MapGet(getMyIpPath, GetMyIp_GetRequestHandler);
        }

        if (this.Config.EnableHealthCheckServer)
        {
            rb.MapGet(healthCheckPath, HealthCheck_GetRequestHandler);
        }

        if (this.Config.EnableBuiltinRichWebPages)
        {
            rb.MapGet(controlPath, Control_GetRequestHandler);
            rb.MapGet(controlPath + "/{rpc_method}", Control_GetRequestHandler);

            rb.MapGet(configPath, AdminConfig_GetRequestHandler);
            rb.MapPost(configPath, AdminConfig_PostRequestHandler);

            rb.MapGet(objEditPath, AdminObjEdit_GetRequestHandler);
            rb.MapPost(objEditPath, AdminObjEdit_PostRequestHandler);

            rb.MapGet(objSearchPath, AdminObjSearch_GetRequestHandler);
            rb.MapPost(objSearchPath, AdminObjSearch_PostRequestHandler);


            if (logBrowserOptions == null)
            {
                logBrowserOptions = new LogBrowserOptions(PP.Combine(Env.AppRootDir, "Log"), $"ログブラウザ (管理者用)");
            }

            if (LogBrowser == null && this.HasAdminPages)
            {
                LogBrowser = new LogBrowser(logBrowserOptions, logBrowserPath);
            }

            if (LogBrowser != null)
            {
                rb.MapGet(logBrowserPath + "/{*path}", async (req, res, route) =>
                {
                    await this.CheckIsCrossSiteRefererAndDenyAsync(req, res);

                    var config = this.Config.HadbBasedServicePoint!.AdminForm_GetCurrentDynamicConfig();
                    var remoteIp = req.HttpContext.Connection.RemoteIpAddress._UnmapIPv4()!;

                    if (config!.Service_AdminPageAcl._IsFilled())
                    {
                        if (EasyIpAcl.Evaluate(config.Service_AdminPageAcl, remoteIp, enableCache: true, permitLocalHost: true) != EasyIpAclAction.Permit)
                        {
                            string err = $"このクライアント IP アドレス '{remoteIp.ToString()}' は、サーバー側の ACL 設定により、管理者向けページへのアクセスを許可されていません。";
                            await res._SendStringContentsAsync(err, statusCode: Consts.HttpStatusCodes.Forbidden);
                        }
                    }

                    var authResult = await BasicAuthImpl.TryAuthenticateAsync(req, (username, password) => this.Config.HadbBasedServicePoint!.AdminForm_AdminPasswordAuthAsync(username, password));
                    if (authResult.IsOk)
                    {
                        await LogBrowser.GetRequestHandlerAsync(req, res, route);
                    }
                    else
                    {
                        await BasicAuthImpl.SendAuthenticateHeaderAsync(res, "Admin Log Browser" /* Must be in English */, req._GetRequestCancellationToken());
                    }
                });
            }
        }

        IRouter router = rb.Build();
        appBuilder.UseRouter(router);

        this.RpcAbsoluteUrlPath = rpcPath;
        if (this.RpcAbsoluteUrlPath.EndsWith("/") == false) this.RpcAbsoluteUrlPath += "/";

        this.ControlAbsoluteUrlPath = controlPath;
        if (this.ControlAbsoluteUrlPath.EndsWith("/") == false) this.ControlAbsoluteUrlPath += "/";

        this.AdminConfigAbsoluteUrlPath = configPath;
        if (this.AdminConfigAbsoluteUrlPath.EndsWith("/") == false) this.AdminConfigAbsoluteUrlPath += "/";

        this.AdminObjEditAbsoluteUrlPath = objEditPath;
        if (this.AdminObjEditAbsoluteUrlPath.EndsWith("/") == false) this.AdminObjEditAbsoluteUrlPath += "/";

        this.AdminObjSearchAbsoluteUrlPath = objSearchPath;
        if (this.AdminObjSearchAbsoluteUrlPath.EndsWith("/") == false) this.AdminObjSearchAbsoluteUrlPath += "/";

        this.AdminLogBrowserAbsoluteUrlPath = logBrowserPath;
        if (this.AdminLogBrowserAbsoluteUrlPath.EndsWith("/") == false) this.AdminLogBrowserAbsoluteUrlPath += "/";
    }
}

public class JsonRpcHttpServerBuilder : HttpServerStartupBase
{
    public JsonRpcHttpServer JsonServer { get; }

    public JsonRpcHttpServerBuilder(IConfiguration configuration) : base(configuration)
    {
        (JsonRpcServerConfig rpcCfg, JsonRpcServerApi api) p = ((JsonRpcServerConfig rpcCfg, JsonRpcServerApi api))this.Param!;

        JsonServer = new JsonRpcHttpServer(p.api, p.rpcCfg);
    }

    public static HttpServer<JsonRpcHttpServerBuilder> StartServer(HttpServerOptions httpCfg, JsonRpcServerConfig rpcServerCfg, JsonRpcServerApi rpcApi, CancellationToken cancel = default)
        => new HttpServer<JsonRpcHttpServerBuilder>(httpCfg, (rpcServerCfg, rpcApi), cancel);

    protected override void ConfigureImpl_BeforeHelper(HttpServerStartupConfig cfg, IApplicationBuilder app, IWebHostEnvironment env, IHostApplicationLifetime lifetime)
    {
    }

    protected override void ConfigureImpl_AfterHelper(HttpServerStartupConfig cfg, IApplicationBuilder app, IWebHostEnvironment env, IHostApplicationLifetime lifetime)
    {
        this.JsonServer.RegisterRoutesToHttpServer(app);

        lifetime.ApplicationStopping.Register(() =>
        {
            this.JsonServer.FreeRegisteredResources();
        });
    }
}

#endif // CORES_BASIC_WEBAPP

