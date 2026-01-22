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

#if CORES_CODES_THINWEBCLIENT

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
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Serialization;
using System.Net;
using System.Net.Sockets;
using System.Net.Security;
using System.Net.WebSockets;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;


using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

using IPA.Cores.Codes;
using IPA.Cores.Helper.Codes;
using static IPA.Cores.Globals.Codes;

using IPA.Cores.Web;
using IPA.Cores.Helper.Web;
using static IPA.Cores.Globals.Web;

using IPA.Cores.Helper.GuaHelper;

using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Mvc.Rendering;
using IPA.App.ThinWebClientApp;
using Microsoft.AspNetCore.Mvc.Filters;

namespace IPA.Cores.Codes;

// ThinWebClient 設定 (JSON 設定ファイルで動的に設定変更可能な設定項目)
[Serializable]
public sealed class ThinWebClientSettings : INormalizable
{
    public bool Debug_EnableGuacdMode = false;

    public bool Debug_GuacdMode_ProxyPortListenAllowAny = false;
    public string Debug_GuacdMode_GuacdHostname = "";
    public int Debug_GuacdPort;

    public List<string> ThinControllerUrlList = new List<string>();

    public string ControllerGateSecretKey = ThinControllerConsts.DefaultControllerGateSecretKey;

    public string CookieDomainName = "";
    public string CookieEncryptPassword = Consts.Strings.EasyEncryptDefaultPassword;

    public int MaxConcurrentSessionsPerClientIp;

    public bool IsConfigured() => this.ThinControllerUrlList.Where(x => !x._InStr("specify_address_here.example.org", true)).Any();

    public ThinWebClientSettings()
    {
    }

    public void Normalize()
    {
        if (this.ThinControllerUrlList.Count == 0)
        {
            this.ThinControllerUrlList.Add("https://specify_address_here.example.org/thincontrol/");
        }

        if (this.Debug_GuacdMode_GuacdHostname._IsEmpty())
        {
            this.Debug_GuacdMode_GuacdHostname = "none";
        }

        if (this.Debug_GuacdPort == 0)
        {
            this.Debug_GuacdPort = 4822;
        }

        if (this.MaxConcurrentSessionsPerClientIp <= 0)
        {
            this.MaxConcurrentSessionsPerClientIp = 5;
        }

        this.CookieDomainName = this.CookieDomainName._NormalizeFqdn();
        if (this.CookieEncryptPassword._IsNullOrZeroLen()) this.CookieEncryptPassword = Consts.Strings.EasyEncryptDefaultPassword;

        if (this.ControllerGateSecretKey._IsEmpty()) this.ControllerGateSecretKey = ThinControllerConsts.DefaultControllerGateSecretKey;
    }
}

#pragma warning disable CS1998 // 非同期メソッドは、'await' 演算子がないため、同期的に実行されます
// ThinWebClient の動作をカスタマイズ可能なフック抽象クラス
public abstract class ThinWebClientHookBase
{
    public abstract RateLimiter<string> GetRateLimiterForNewSession();

    public abstract ReadOnlyMemory<byte> GetProtocolWatermarkBinary();
}
#pragma warning restore CS1998 // 非同期メソッドは、'await' 演算子がないため、同期的に実行されます

// サーバー接続プロファイル
public class ThinWebClientProfile : INormalizable
{
    public string Pcid { get; set; } = ""; // 接続先コンピュータ ID
    public GuaPreference Preference { get; set; } = new GuaPreference(); // 接続設定

    public ThinWebClientProfile(GuaKeyboardLayout keyboardLayout = GuaKeyboardLayout.Japanese)
    {
        this.Preference.KeyboardLayout = keyboardLayout;
    }

    public void Normalize()
    {
        this.Pcid = Str.NormalizeString(this.Pcid, false, true, false, true);
        this.Preference.Normalize();
    }

    public ThinWebClientProfile CloneAsDefault()
    {
        ThinWebClientProfile ret = this._CloneWithJson();

        ret.Pcid = "";

        ret.Preference = ret.Preference.CloneAsDefault();

        ret.Normalize();

        return ret;
    }
}

// ヒストリ
public class ThinWebClientHistory
{
    public List<ThinWebClientProfile> Items = new List<ThinWebClientProfile>();

    public void Add(ThinWebClientProfile profile)
    {
        var clone = profile._CloneWithJson();
        clone.Normalize();
        if (clone.Pcid._IsEmpty()) return;

        var deleteList = this.Items.Where(x => x.Pcid._IsSamei(clone.Pcid)).ToList();
        deleteList.ForEach(x => this.Items.Remove(x));

        this.Items.Add(clone);

        while (this.Items.Count >= 1 && this.Items.Count > ThinWebClientConsts.MaxHistory)
        {
            this.Items.RemoveAt(0);
        }
    }

    public void Clear()
    {
        this.Items.Clear();
    }

    public void SaveToCookie(Controller c, AspNetCookieOptions options, string easyEncryptPassword)
    {
        for (int i = 0; i < ThinWebClientConsts.MaxHistory; i++)
        {
            var item = this.Items.ElementAtOrDefault(i);
            string tagName = $"thin_history_{i:D4}";
            if (item != null)
            {
                c._EasySaveCookie(tagName, item, options, true, easyEncryptPassword);
            }
            else
            {
                c._EasyDeleteCookie(tagName);
            }
        }
    }

    public static ThinWebClientHistory LoadFromCookie(Controller c, string easyEncryptPassword)
    {
        ThinWebClientHistory ret = new ThinWebClientHistory();

        for (int i = 0; i < ThinWebClientConsts.MaxHistory; i++)
        {
            string tagName = $"thin_history_{i:D4}";
            var data = c._EasyLoadCookie<ThinWebClientProfile>(tagName, true, easyEncryptPassword);
            if (data != null)
            {
                ret.Items.Add(data);
            }
        }

        return ret;
    }
}

public class ThinWebClientModelIndex
{
    public ThinWebClientProfile CurrentProfile { get; set; } = new ThinWebClientProfile();

    // リサイズ方式の選択肢
    public List<SelectListItem> ResizeMethodItems { get; }

    // キーボードの選択肢
    public List<SelectListItem> KeyboardLayoutItems { get; }

    // History 選択肢
    public List<SelectListItem> HistoryItems { get; private set; } = new List<SelectListItem>();

    // 選択されている History
    public string? SelectedHistory { get; set; }

    public bool FocusToPcid { get; set; } = false;

    public string WolErrorMessage { get; set; } = "";
    public string WolOkMessage { get; set; } = "";
    public bool JumpToWol { get; set; } = false;

    public bool IsWebpSupported { get; set; }

    public ThinWebClientModelIndex()
    {
        this.ResizeMethodItems = new List<SelectListItem>();
        foreach (var item in GuaHelper.GetResizeMethodList())
        {
            this.ResizeMethodItems.Add(new SelectListItem(item.Item2, item.Item1));
        }

        this.KeyboardLayoutItems = new List<SelectListItem>();
        foreach (var item in GuaHelper.GetKeyboardLayoutList())
        {
            this.KeyboardLayoutItems.Add(new SelectListItem(item.Item2, item.Item1));
        }
    }

    public void FillHistory(ThinWebClientHistory history)
    {
        this.HistoryItems = new List<SelectListItem>();

        this.HistoryItems.Add(new SelectListItem("", ""));

        foreach (var item in history.Items.AsEnumerable().Reverse())
        {
            this.HistoryItems.Add(new SelectListItem(item.Pcid, $"/?pcid={item.Pcid._MakeVerySafeAsciiOnlyNonSpaceString()}"));
        }
    }
}

public abstract class ThinWebClientModelSessionBase
{
    public string? SessionId { get; set; }
    public string? RequestId { get; set; }
    public ThinClientConnectOptions? ConnectOptions { get; set; }
    public ThinWebClientProfile? Profile { get; set; }
}

public class ThinWebClientModelOtp : ThinWebClientModelSessionBase
{
}

public class ThinWebClientModelSessionAuthPassword : ThinWebClientModelSessionBase
{
    public ThinClientAuthRequest? Request { get; set; }
    public ThinClientAuthResponse? Response { get; set; }
}

public class ThinWebClientRemoteMisc2
{
    public string OnceMsg { get; set; } = "";
    public string OnceMsgTitle { get; set; } = "";
}

public class ThinWebClientModelRemote : ThinWebClientModelSessionBase
{
    public string? WebSocketUrl { get; set; }
    public string? ConnectPacketData { get; set; }
    public ThinSvcType SvcType { get; set; }
    public string WatermarkStr1 { get; set; } = "";
    public string WatermarkStr2 { get; set; } = "";
    public ThinClientMiscParams Misc { get; set; } = new ThinClientMiscParams();
    public ThinWebClientRemoteMisc2 Misc2 { get; set; } = new ThinWebClientRemoteMisc2();
}

public static class ThinWebClientErrorUtil
{
    public static string GetFriendlyErrorMessage(Exception? ex, PageContext page)
        => GetFriendlyErrorMessage(ex, page.Stb);

    public static string GetFriendlyErrorMessage(Exception? ex, StrTable table)
    {
        string msg = "エラー: " + (ex?.Message ?? "不明なエラーが発生しました。");

        VpnException? vpnError = ex as VpnException;

        if (vpnError != null)
        {
            msg = vpnError.GetFriendlyVpnErrorMessage(table);
        }

        return msg;
    }
}

public class ThinWebClientController : Controller
{
    public ThinWebClient Client { get; }
    public PageContext Page { get; }

    public StrTableLanguage Language => Page.CurrentLanguage;
    public StrTable StrTable => Page.Stb;

    static Once DumpStrTableJsonOnceFlag;
    static CriticalSection<ThinWebClientController> DumpStrTableJsonOnceFlagLock = new CriticalSection<ThinWebClientController>();

    public ThinWebClientController(ThinWebClient client, PageContext page)
    {
        this.Client = client;
        this.Page = page;

        this.Page.SetLanguageList(client.LanguageList);

        // 文字列 JSON をダンプする
        lock (DumpStrTableJsonOnceFlagLock)
        {
            if (DumpStrTableJsonOnceFlag.IsFirstCall())
            {
                this.Page.DumpStrTableJson(Env.AppRootDir._CombinePath("wwwroot", "js", "strtable.js"));
            }
        }

        this.Page.SetLanguageByHttpString("ja");
    }

    public override void OnActionExecuting(ActionExecutingContext context)
    {
        try
        {
            this.Page.StartLanguageSelection(this.HttpContext);
        }
        finally
        {
            base.OnActionExecuting(context);
        }
    }

    protected AspNetCookieOptions GetCookieOption() => new AspNetCookieOptions(domain: this.Client.SettingsFastSnapshot.CookieDomainName);

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public async Task<IActionResult> IndexAsync(ThinWebClientModelIndex form, string? pcid, string? deleteAll, string? button_wol)
    {
        ThinWebClientProfile? historySelectedProfile = null;

        ThinWebClientProfile profile = form.CurrentProfile;
        profile.Normalize();

        ThinWebClientHistory history = ThinWebClientHistory.LoadFromCookie(this, this.Client.SettingsFastSnapshot.CookieEncryptPassword);

        if (this._IsPostBack())
        {
            if (profile.Pcid._IsFilled())
            {
                // 現在のプロファイルの保存
                this._EasySaveCookie("thin_current_profile", profile.CloneAsDefault(), GetCookieOption(), true, this.Client.SettingsFastSnapshot.CookieEncryptPassword);

                // ヒストリへの追加
                history.Add(profile);
                history.SaveToCookie(this, GetCookieOption(), this.Client.SettingsFastSnapshot.CookieEncryptPassword);

                // thin_last_pcid の保存
                this._EasySaveCookie("thin_last_pcid", profile.Pcid, GetCookieOption());

                var clientIp = Request.HttpContext.Connection.RemoteIpAddress._UnmapIPv4()!;
                var clientPort = Request.HttpContext.Connection.RemotePort;
                string clientFqdn = await Client.DnsResolver.GetHostNameOrIpAsync(clientIp);

                // Rate limit
                if (this.Client.RateLimit.TryInput(clientIp.ToString(), out _) == false)
                {
                    throw new CoresException(this.Page.Stb["THINWEB_RATELIMIT_EXCEEDED"]);
                }

                var tc = this.Client.CreateThinClient(this.StrTable);

                if (button_wol._ToBool() == false)
                {
                    // 普通の接続
                    WideTunnelClientOptions wideOptions = new WideTunnelClientOptions(WideTunnelClientFlags.None, clientIp.ToString(), clientFqdn, clientPort);

                    // セッションの開始
                    var session = tc.StartConnect(new ThinClientConnectOptions(profile.Pcid, clientIp, clientFqdn, this.Client.SettingsFastSnapshot.Debug_EnableGuacdMode, wideOptions, form.IsWebpSupported, profile.Preference, profile._CloneWithJson()),
                        this.Client.SettingsFastSnapshot.MaxConcurrentSessionsPerClientIp);
                    string sessionId = session.SessionId;

                    // セッション ID をもとにした URL にリダイレクト
                    return Redirect($"/ThinWebClient/Session/{sessionId}/?pcid={profile.Pcid._MakeVerySafeAsciiOnlyNonSpaceString()}");
                }
                else
                {
                    try
                    {
                        // WoL 信号の発射
                        WideTunnelClientOptions wideOptions = new WideTunnelClientOptions(WideTunnelClientFlags.WoL, clientIp.ToString(), clientFqdn, clientPort);

                        await tc.ExecuteWoLAsync(new ThinClientConnectOptions(profile.Preference.WoLTriggerPcid, clientIp, clientFqdn, false, wideOptions, form.IsWebpSupported, profile.Preference, profile._CloneWithJson()),
                            profile.Pcid, this._GetRequestCancellationToken());

                        // WoL OK メッセージ
                        form.WolOkMessage = this.Page.Stb["DU_WOL_MSG"]._FormatC(profile.Pcid, profile.Preference.WoLTriggerPcid);
                    }
                    catch (Exception ex)
                    {
                        // WoL エラーメッセージの文字列化
                        string msg = ThinWebClientErrorUtil.GetFriendlyErrorMessage(ex, this.Page);

                        form.WolErrorMessage = msg;

                        form.JumpToWol = true;
                    }
                }
            }
        }
        else
        {
            if (pcid._IsEmpty())
            {
                // Query String の pcid が空の場合、Cookie から読み出す
                pcid = this._EasyLoadCookie<string>("thin_last_pcid")._NonNullTrim();
            }

            if (deleteAll._ToBool())
            {
                // History をすべて消去するよう指示された
                // Cookie の History をすべて消去する
                history.Clear();
                history.SaveToCookie(this, GetCookieOption(), this.Client.SettingsFastSnapshot.CookieEncryptPassword);
                this._EasySaveCookie("thin_last_pcid", "", GetCookieOption());

                // トップページにリダイレクトする
                return Redirect("/");
            }
            else if (pcid._IsFilled())
            {
                // History から履歴を指定された。id を元に履歴からプロファイルを読み出す
                historySelectedProfile = history.Items.Where(h => h.Pcid._IsSamei(pcid)).FirstOrDefault();
            }

            if (historySelectedProfile == null)
            {
                // デフォルト値
                GuaKeyboardLayout defaultKeyboardLayout = GuaKeyboardLayout.Japanese;

                if (Page.CurrentLanguage.Key._IsSamei("ja") == false)
                {
                    // 日本語以外の環境では英語キーボードをデフォルトで選択する
                    defaultKeyboardLayout = GuaKeyboardLayout.EnglishUs;
                }

                profile = this._EasyLoadCookie<ThinWebClientProfile>("thin_current_profile", true, this.Client.SettingsFastSnapshot.CookieEncryptPassword) ?? new ThinWebClientProfile(defaultKeyboardLayout);
            }
            else
            {
                // History で選択された値
                profile = historySelectedProfile;
            }

            profile.Normalize();
            form.CurrentProfile = profile;

            // GET の場合は必ず PCID 入力ボックスをフォーカスする
            form.FocusToPcid = true;
        }

        form.FillHistory(history);

        if (historySelectedProfile != null)
        {
            form.SelectedHistory = form.HistoryItems.Where(x => x.Text._IsSamei(historySelectedProfile.Pcid)).FirstOrDefault()?.Value ?? "";
        }

        return View(form);
    }

    public async Task<IActionResult> HelpAsync()
    {
        //string url = this.Request.GetDisplayUrl();
        //$"url = {url}"._Debug();
        var h = this.Request.Headers;
        await Task.CompletedTask;
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public async Task<IActionResult> RemoteAsync(string? id, string? pcid)
    {
        var cancel = Request._GetRequestCancellationToken();
        id = id._NonNullTrim();

        // 指定されたセッション ID を元に検索
        var session = this.Client.SessionManager.GetSessionById(id);

        if (session != null)
        {
            ThinClientConnectOptions connectOptions = (ThinClientConnectOptions)session.Param!;
            ThinWebClientProfile profile = (ThinWebClientProfile)connectOptions.AppParams!;

            var req = session.GetFinalAnswerRequest();

            if (req != null)
            {
                var misc2 = new ThinWebClientRemoteMisc2();

                var caps = connectOptions.Caps;

                // VNC の場合にメッセージを表示する
                if (connectOptions.ConnectedSvcType!.Value == ThinSvcType.Vnc)
                {
                    if (caps.Bit(ThinServerCaps.UrdpVeryLimited))
                    {
                        misc2.OnceMsg = Page.Stb["DU_ONCEMSG_1"];
                    }
                    else
                    {
                        if (caps.Bit(ThinServerCaps.WinRdpEnabled))
                        {
                            misc2.OnceMsg = Page.Stb["DU_ONCEMSG_3"];
                        }
                        else
                        {
                            misc2.OnceMsg = Page.Stb["DU_ONCEMSG_2"];
                        }
                    }
                    misc2.OnceMsg = misc2.OnceMsg._FormatC(profile.Pcid);
                    misc2.OnceMsgTitle = "操作のヒント";
                }

                ThinWebClientModelRemote main = new ThinWebClientModelRemote()
                {
                    ConnectOptions = connectOptions,
                    WebSocketUrl = connectOptions.WebSocketUrl,
                    SessionId = session.SessionId,
                    Profile = profile,
                    SvcType = connectOptions.ConnectedSvcType!.Value,
                    ConnectPacketData = connectOptions.ConnectPacketData,
                    WatermarkStr1 = connectOptions.WatermarkStr1,
                    WatermarkStr2 = connectOptions.WatermarkStr2,
                    Misc = connectOptions.MiscParams,
                    Misc2 = misc2,
                };

                return View(main);
            }
        }

        await TaskCompleted;
        return Redirect("/" + (pcid._IsFilled() ? "?pcid=" + pcid._MakeVerySafeAsciiOnlyNonSpaceString() : ""));
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public async Task<IActionResult> SessionAsync(string? id, string? requestid, string? formtype, string? password, string? username, string? otp, string? pcid)
    {
        var cancel = Request._GetRequestCancellationToken();
        id = id._NonNullTrim();

        // 指定されたセッション ID を元に検索
        var session = this.Client.SessionManager.GetSessionById(id);

        if (session == null)
        {
            // セッション ID が見つからない。トップページに戻る
            return Redirect("/" + (pcid._IsFilled() ? "?pcid=" + pcid._MakeVerySafeAsciiOnlyNonSpaceString() : ""));
        }
        else
        {
            ThinClientConnectOptions connectOptions = (ThinClientConnectOptions)session.Param!;
            ThinWebClientProfile profile = (ThinWebClientProfile)connectOptions.AppParams!;

            if (requestid._IsFilled() && formtype._IsFilled())
            {
                IDialogResponseData? responseData = null;
                switch (formtype)
                {
                    case "SessionAuthPassword":
                        responseData = new ThinClientAuthResponse { Username = "", Password = password._NonNull() };
                        break;

                    case "SessionAuthAdvanced":
                        responseData = new ThinClientAuthResponse { Username = username._NonNull(), Password = password._NonNull() };
                        break;

                    case "SessionOtp":
                        responseData = new ThinClientOtpResponse { Otp = otp._NonNull() };
                        break;
                }
                if (responseData != null)
                {
                    session.SetResponseData(requestid, responseData);
                }
            }

            L_GET_NEXT:
            var req = await session.GetNextRequestAsync(cancel: cancel);

            if (req != null)
            {
                session.SendHeartBeat(req.RequestId);

                switch (req.RequestData)
                {
                    case ThinClientAuthRequest authReq:
                        switch (authReq.AuthType)
                        {
                            case ThinAuthType.None:
                                session.SetResponseData(req.RequestId, new ThinClientAuthResponse());
                                goto L_GET_NEXT;

                            case ThinAuthType.Password:
                                ThinWebClientModelSessionAuthPassword page = new ThinWebClientModelSessionAuthPassword
                                {
                                    SessionId = session.SessionId,
                                    RequestId = req.RequestId,
                                    ConnectOptions = connectOptions,
                                    Request = authReq,
                                    Profile = profile._CloneWithJson(),
                                };

                                return View("SessionAuthPassword", page);

                            case ThinAuthType.Advanced:
                                ThinWebClientModelSessionAuthPassword page2 = new ThinWebClientModelSessionAuthPassword
                                {
                                    SessionId = session.SessionId,
                                    RequestId = req.RequestId,
                                    ConnectOptions = connectOptions,
                                    Request = authReq,
                                    Profile = profile._CloneWithJson(),
                                };

                                return View("SessionAuthAdvanced", page2);

                            default:
                                throw new CoresException($"authReq.AuthType = {authReq.AuthType}: Unsupported auth type.");
                        }

                    case ThinClientOtpRequest otpReq:
                        ThinWebClientModelOtp page3 = new ThinWebClientModelOtp
                        {
                            SessionId = session.SessionId,
                            RequestId = req.RequestId,
                            ConnectOptions = connectOptions,
                            Profile = profile._CloneWithJson(),
                        };

                        return View("SessionOtp", page3);

                    case ThinClientAcceptReadyNotification ready:
                        if (ready.IsStandaloneMode)
                        {
                            if (Request.Headers._GetStrFirst("X-Original-For")._IsEmpty())
                            {
                                var ss = Page.Stb["THINWEB_STANDALONE_MUST_VIA_PROXY"];
                                throw new CoresException(ss);
                            }
                        }

                        if (connectOptions.DebugGuacMode)
                        {
                            connectOptions.UpdateConnectedSvcType(ready.FirstConnection!.SvcType);
                            connectOptions.UpdateWatermarkStr(ready.FirstConnection!.WatermarkStr1, ready.FirstConnection!.WatermarkStr2);
                            connectOptions.UpdateMiscParams(ready.FirstConnection.Misc);
                            connectOptions.UpdateCaps(ready.FirstConnection.Caps);
                        }
                        else
                        {
                            connectOptions.UpdateConnectedSvcType(ready.SvcType!.Value);
                            ready.WebSocketFullUrl._NotEmptyCheck();
                            connectOptions.UpdateWebSocketUrl(ready.WebSocketFullUrl);
                            connectOptions.UpdateConnectPacketData(ready.ConnectPacketData);
                            connectOptions.UpdateWatermarkStr(ready.WatermarkStr1, ready.WatermarkStr2);
                            connectOptions.UpdateMiscParams(ready.Misc);
                            connectOptions.UpdateCaps(ready.Caps);
                        }

                        if (ready.WebSocketFullUrl._IsFilled())
                        {
                            $"ready.WebSocketFullUrl = {ready.WebSocketFullUrl?.ToString()}"._Debug();
                        }
                        else
                        {
                            $"ready.ListenEndPoint = {ready.ListenEndPoint?.ToString()}"._Debug();
                        }
                        req.SetResponseDataEmpty();

                        return Redirect($"/ThinWebClient/Remote/{session.SessionId}/" + (pcid._IsFilled() ? "?pcid=" + pcid._MakeVerySafeAsciiOnlyNonSpaceString() : ""));

                    case ThinClientInspectRequest inspect:
                        ThinClientInspectResponse insRes = new ThinClientInspectResponse
                        {
                            AntiVirusOk = true,
                            Ticket = "",
                            WindowsUpdateOk = true,
                            MacAddressList = Str.NormalizeMac(profile.Preference.MacAddress, style: MacAddressStyle.Windows),
                        };

                        session.SetResponseData(req.RequestId, insRes);
                        goto L_GET_NEXT;

                    default:
                        throw new CoresException($"Unknown request data: {req.RequestData.GetType().ToString()}");
                }
            }
            else
            {
                Dbg.Where();
            }
        }

        return Redirect("/" + (pcid._IsFilled() ? "?pcid=" + pcid._MakeVerySafeAsciiOnlyNonSpaceString() : ""));
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public async Task<IActionResult> SendHeartBeatAsync(string sessionId, string requestId)
    {
        sessionId = sessionId._NonNullTrim();
        requestId = requestId._NonNullTrim();

        string ret = "error";

        if (sessionId._IsFilled() && requestId._IsFilled())
        {
            if (this.Client.SessionManager.SendHeartBeat(sessionId, requestId))
            {
                ret = "ok";
            }
        }

        await TaskCompleted;

        return new TextActionResult(ret);
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public async Task<IActionResult> SessionHealthCheckAsync(string sessionId)
    {
        sessionId = sessionId._NonNullTrim();

        string ret = "error";

        if (sessionId._IsFilled())
        {
            if (this.Client.SessionManager.CheckSessionHealth(sessionId))
            {
                ret = "ok";
            }
        }

        await TaskCompleted;

        return new TextActionResult(ret);
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    [HttpGet("/ws")]
    [HttpPost("/ws")]
    public async Task AcceptWebSocketAsync(string? id, string? width, string? height)
    {
        await using (TaskUtil.CreateCombinedCancellationToken(out CancellationToken cancel, this._GetRequestCancellationToken(), this.Client.GrandCancel))
        {
            if (HttpContext.WebSockets.IsWebSocketRequest)
            {
                // 指定されたセッション ID を元に検索
                var session = this.Client.SessionManager.GetSessionById(id._NonNull());

                if (session != null)
                {
                    ThinClientConnectOptions connectOptions = (ThinClientConnectOptions)session.Param!;
                    ThinWebClientProfile profile = (ThinWebClientProfile)connectOptions.AppParams!;

                    if (connectOptions.DebugGuacMode == false)
                    {
                        throw new CoresLibException("connectOptions.DebugGuacMode == false");
                    }

                    var req = session.GetFinalAnswerRequest();

                    if (req?.RequestData is ThinClientAcceptReadyNotification ready)
                    {
                        var pref = profile.Preference._CloneWithJson();

                        // ws 接続時に width と height がパラメータとして指定されていた場合は、preference の内容を更新する
                        int widthInt = width._ToInt();
                        int heightInt = height._ToInt();

                        if (widthInt >= 1 && heightInt >= 1)
                        {
                            pref.ScreenWidth = widthInt;
                            pref.ScreenHeight = heightInt;
                        }

                        await using var guaClient = new GuaClient(
                            new GuaClientSettings(
                                Client.SettingsFastSnapshot.Debug_GuacdMode_GuacdHostname,
                                Client.SettingsFastSnapshot.Debug_GuacdPort,
                                ready.FirstConnection!.SvcType.ToString().StrToGuaProtocol(),
                                "", ready.ListenEndPoint!.Port,
                                //"pc37.sec.softether.co.jp", 3333, // testtest
                                pref,
                                connectOptions.IsWebpSupported,
                                ready.FirstConnection.Caps.Bit(ThinServerCaps.AudioInSupported)));

                        var readyPacket = await guaClient.StartAsync(cancel);

                        await using var gcStream = guaClient.Stream._NullCheck();

                        using var webSocket = await HttpContext.WebSockets.AcceptWebSocketAsync("guacamole");

                        //if (true)
                        //{
                        //    MemoryStream ms = new MemoryStream();
                        //    await readyPacket.SendPacketAsync(ms);
                        //    //var tmp = ms.ToArray();
                        //    var tmp = "6.hello;"._GetBytes_Ascii();
                        //    await webSocket.SendAsync(tmp, WebSocketMessageType.Text, true, cancel);
                        //}

                        await GuaWebSocketUtil.RelayBetweenWebSocketAndStreamDuplex(gcStream, webSocket, cancel: cancel);

                        return;
                    }
                }
            }

            HttpContext.Response.StatusCode = 400;
        }
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new AspNetErrorModel(this));
    }
}

public class ThinWebClientOptions
{
    public IEnumerable<Certificate> MasterCertificates { get; }

    public ThinWebClientOptions(IEnumerable<Certificate> masterCertificates)
    {
        this.MasterCertificates = masterCertificates;
    }
}

public class ThinWebClient : AsyncService
{
    // Hive
    readonly HiveData<ThinWebClientSettings> SettingsHive;

    // 設定へのアクセスを容易にするための自動プロパティ
    CriticalSection ManagedSettingsLock => SettingsHive.DataLock;
    ThinWebClientSettings ManagedSettings => SettingsHive.ManagedData;

    public ThinWebClientSettings SettingsFastSnapshot => SettingsHive.CachedFastSnapshot;

    public DnsResolver DnsResolver { get; }

    public DateTimeOffset BootDateTime { get; } = DtOffsetNow;

    public long BootTick { get; } = TickNow;

    public ThinWebClientHookBase Hook { get; }

    public DialogSessionManager SessionManager { get; }

    public StrTableLanguageList LanguageList { get; private set; } = null!;

    public ThinWebClientOptions Options { get; }

    public RateLimiter<string> RateLimit { get; }

    public ThinWebClient(ThinWebClientOptions options, ThinWebClientHookBase hook, Func<ThinWebClientSettings>? getDefaultSettings = null)
    {
        try
        {
            this.Options = options;

            this.Hook = hook;

            this.SettingsHive = new HiveData<ThinWebClientSettings>(
                Hive.SharedLocalConfigHive,
                "ThinWebClient",
                getDefaultSettings,
                HiveSyncPolicy.AutoReadWriteFile,
                HiveSerializerSelection.RichJson);

            this.DnsResolver = new DnsClientLibBasedDnsResolver(new DnsResolverSettings());

            this.SessionManager = new DialogSessionManager(new DialogSessionManagerOptions(sessionIdPrefix: "thin"), cancel: this.GrandCancel);

            this.RateLimit = this.Hook.GetRateLimiterForNewSession();
        }
        catch (Exception ex)
        {
            this._DisposeSafe(ex);
            throw;
        }
    }

    public void SetLanguageList(StrTableLanguageList list)
    {
        this.LanguageList = list;
    }

    public ThinClient CreateThinClient(StrTable? stb)
    {
        AddressFamily listenFamily = AddressFamily.InterNetwork;
        IPAddress listenAddress = IPAddress.Loopback;

        if (this.SettingsFastSnapshot.Debug_GuacdMode_ProxyPortListenAllowAny)
        {
            listenAddress = IPAddress.Any;
        }

        if (this.SettingsFastSnapshot.IsConfigured() == false)
        {
            throw new CoresException(stb != null ? stb["THINWEB_NOT_CONFIGURED"] : "ThinWebClient.json is not configured.");
        }

        ThinClient tc = new ThinClient(new ThinClientOptions(new WideTunnelOptions("DESK", nameof(ThinWebClient), this.SettingsFastSnapshot.ThinControllerUrlList, this.Options.MasterCertificates,
            waterMark: this.Hook.GetProtocolWatermarkBinary(), controllerGateSecretKey: this.SettingsFastSnapshot.ControllerGateSecretKey), this.SessionManager,
            listenFamily, listenAddress));

        return tc;
    }

    protected override async Task CleanupImplAsync(Exception? ex)
    {
        try
        {
            await this.DnsResolver._DisposeSafeAsync();

            await this.SessionManager._DisposeSafeAsync();

            this.SettingsHive._DisposeSafe();
        }
        finally
        {
            await base.CleanupImplAsync(ex);
        }
    }
}

#endif

