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

#if CORES_CODES_THINCONTROLLER

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

using System.Runtime.CompilerServices;

namespace IPA.Cores.Codes;

[Flags]
public enum ThinControllerFqdnUsage
{
    ClientConnect = 1,
    ServerConnect = 2,
    ViaProxy = 4,
}

// ThinController 設定 (JSON 設定ファイルで動的に設定変更可能な設定項目)
[Serializable]
public sealed class ThinControllerSettings : INormalizable
{
    public List<string> WpcPathList = new List<string>();

    public string DbConnectionString_Read = "";
    public string DbConnectionString_Write = "";

    public ThinControllerSettings()
    {
    }

    public void Normalize()
    {
        if (this.WpcPathList._IsEmpty())
        {
            this.WpcPathList = new List<string>();

            this.WpcPathList.Add("/thincontrol/");
            this.WpcPathList.Add("/widecontrol/");
        }

        if (this.DbConnectionString_Read._IsEmpty())
        {
            // デフォルトダミー文字列 (安全)
            this.DbConnectionString_Read = ThinControllerConsts.Default_DbConnectionString_Read;
        }

        if (this.DbConnectionString_Write._IsEmpty())
        {
            // デフォルトダミー文字列 (安全)
            this.DbConnectionString_Write = ThinControllerConsts.Default_DbConnectionString_Write;
        }
    }
}

#pragma warning disable CS1998 // 非同期メソッドは、'await' 演算子がないため、同期的に実行されます
// ThinController の動作をカスタマイズ可能なフック抽象クラス
public abstract class ThinControllerHookBase
{
    public virtual async Task<string?> DetermineMachineGroupNameAsync(ThinControllerSession session, CancellationToken cancel = default) => null;

    public virtual async Task<string> GenerateFqdnForGateAsync(ThinGate gate, ThinControllerSession session, ThinControllerFqdnUsage usage, string prefix = "", string suffix = "", CancellationToken cancel = default)
    {
        if (IPAddress.TryParse(gate.IpAddress, out IPAddress? ip))
        {
            return IPUtil.GenerateWildCardDnsFqdn(ip,
                session.Controller.Db.MemDb?.WildCardDnsDomainName ?? ThinControllerConsts.Default_WildCardDnsDomainName,
                prefix,
                suffix);
        }

        return gate.IpAddress;
    }

    public virtual async Task<string?> DetermineConnectionProhibitedAsync(ThinController controller, bool isClientConnectMode, string serverIp, string? clientIp, ThinDbMachine serverMachine, CancellationToken cancel = default) => null;

    public virtual async Task<bool> SendOtpEmailAsync(ThinController controller, string otp, string emailTo, string emailFrom, string clientIp, string clientFqdn, string pcidMasked, string pcid,
        ThinControllerOtpServerSettings serverSettings, CancellationToken cancel = default) => throw new NotImplementedException();
}
#pragma warning restore CS1998 // 非同期メソッドは、'await' 演算子がないため、同期的に実行されます


// ThinController 設定 (Vars.cs で固定的に設定変更可能な設定項目)
public static class ThinControllerGlobalSettings
{
    // シン・テレワークシステム プライベート版を用いて商用サービスを実装したいユーザー (システム開発者) 向けの機能
    public static readonly Copenhagen<bool> PaidService_Enabled = false;    // 商用サービス化機能の有効化フラグ (デフォルトで無効)
    public static readonly Copenhagen<TimeSpan> PaidService_TrialSpan = new TimeSpan(30, 0, 0, 0);  // 体験版として利用を開始してから無償で体験利用ができる日数
    // 体験版の利用期限が切れたか、製品版のアクティベーションが切れた場合に表示される Web ページの URL。
    // <PCID>: コンピュータ名
    // <STATUS>: TrialExpired または Deactivated のいずれか
    // <EXPIRED>: STATUS が TrialExpired の場合、体験版の有効期限が切れた日時。Status が Deactivated の場合、解約された日。YYYYMMDDHHMMSS 形式
    // <TAG>: アクティベーションまたはアクティベーション解除時に指定されたタグ文字列
    // 注意: 展開後の URL は 256 文字以内である必要がある。これを超過した場合は、URL は切り捨てられる。
    public static readonly Copenhagen<string> PaidService_RedirectUrl = "https://example.org/expired/?status=<STATUS>&expired=<EXPIRED>&pcid=<PCID>&tag=<TAG>"; 
    public static readonly Copenhagen<string> PaidService_RpcAuthUsername = Consts.Strings.DefaultAdminUsername;
    public static readonly Copenhagen<string> PaidService_RpcAuthPassword = Consts.Strings.DefaultAdminPassword;
}


// ThinController 統計データ
public class ThinControllerStat
{
    public int Stat_CurrentRelayGates;
    public int Stat_CurrentUserSessionsServer;
    public int Stat_CurrentUserSessionsClient1;
    public int Stat_CurrentUserSessionsClient2;
    public int Stat_CurrentUserSessionsClient3_WebSocket;
    public int Stat_TotalServers;
    public int Stat_ActiveServers_Day01;
    public int Stat_ActiveServers_Day03;
    public int Stat_ActiveServers_Day07;
    public int Stat_ActiveServers_Day30;
    public int Stat_TodaysNewServers;
    public int Stat_YestardaysNewServers;
    public double Stat_TotalServerConnectRequestsKilo;
    public double Stat_TotalClientConnectRequestsKilo;

    public double Throughput_ClientGetWolMacList;
    public double Throughput_ClientConnect;
    public double Throughput_RenameMachine;
    public double Throughput_RegistMachine;
    public double Throughput_SendOtpEmail;
    public double Throughput_ServerConnect;
    public double Throughput_ReportSessionList;
    public double Throughput_ReportSessionAdd;
    public double Throughput_ReportSessionDel;
    public double Throughput_DatabaseRead;
    public double Throughput_DatabaseWrite;
    public double Throughput_Request_NonProxy;
    public double Throughput_Request_Proxy;

    public int Sys_DotNet_NumRunningTasks;
    public int Sys_DotNet_NumDelayedTasks;
    public int Sys_DotNet_NumTimerTasks;
    public int Sys_DotNet_NumObjects;
    public int Sys_DotNet_CpuUsage;
    public double Sys_DotNet_ManagedMemory_MBytes;
    public double Sys_DotNet_ProcessMemory_MBytes;
    public int Sys_DotNet_NumNativeThreads;
    public int Sys_DotNet_NumNativeHandles;
    public int Sys_DotNet_GcTotal, Sys_DotNet_Gc0, Sys_DotNet_Gc1, Sys_DotNet_Gc2;

    public double Sys_Thin_BootDays;
    public int Sys_Thin_DbLazyUpdateQueueLength;
    public int Sys_Thin_ConcurrentRequests;
    public int Sys_Thin_LastDbReadTookMsecs;
    public int Sys_Thin_IsDatabaseConnected;
    public int Sys_Thin_WebSocketCertTimestampDateYymmdd;
    public int Sys_Thin_WebSocketCertTimestampTimeHHmmss;
}

public class ThinControllerSessionClientInfo
{
    public ThinControllerServiceType ServiceType { get; }

    public HttpEasyContextBox? Box { get; }

    public string ClientPhysicalIp { get; }
    public string ClientIp { get; }
    public string ClientIpFqdn { get; private set; }
    public QueryStringList HttpQueryStringList { get; }
    public bool IsProxyMode { get; }

    public bool IsAuthed => AuthedMachine != null;
    public ThinDbMachine? AuthedMachine { get; private set; }
    public string MachineGroupName { get; private set; } = "";

    public int ConsumingConcurrentProcessCount { get; }

    public string ClientIpForRateLimiter { get; } = "";

    public ThinControllerSessionClientInfo(HttpEasyContextBox box, ThinControllerServiceType serviceType, int consumingConcurrentProcessCount)
    {
        this.ServiceType = serviceType;

        this.Box = box;

        this.ClientPhysicalIp = box.RemoteEndpoint.Address.ToString();

        this.ConsumingConcurrentProcessCount = consumingConcurrentProcessCount;

        string proxySrcIp = box.Request.Headers._GetStrFirst("X-WG-Proxy-SrcIP");

        if (proxySrcIp._IsFilled() && proxySrcIp != this.ClientIp)
        {
            this.ClientIp = proxySrcIp;
            this.IsProxyMode = true;
        }
        else
        {
            this.ClientIp = this.ClientPhysicalIp;
        }

        this.HttpQueryStringList = box.QueryStringList;

        this.ClientIpFqdn = this.ClientIp;

        // Rate limiter 用 IP アドレス
        var clientIpAddress = ClientIp._ToIPAddress(noExceptionAndReturnNull: true);
        if (clientIpAddress != null)
        {
            // サブネットマスクの AND をする
            if (clientIpAddress.AddressFamily == AddressFamily.InterNetwork)
            {
                // IPv4
                ClientIpForRateLimiter = (IPUtil.IPAnd(clientIpAddress, IPUtil.IntToSubnetMask4(24))).ToString();
            }
            else
            {
                // IPv6
                ClientIpForRateLimiter = (IPUtil.IPAnd(clientIpAddress, IPUtil.IntToSubnetMask6(56))).ToString();
            }
        }
    }

    public async Task ResolveClientIpFqdnIfPossibleAsync(DnsResolver resolver, CancellationToken cancel = default)
    {
        if (this.ClientIpFqdn != this.ClientIp) return;

        this.ClientIpFqdn = await resolver.GetHostNameOrIpAsync(this.ClientIp, cancel);
    }

    public async Task SetAuthedAsync(ThinControllerSession session, ThinDbMachine machine, CancellationToken cancel)
    {
        machine._NullCheck(nameof(machine));

        this.AuthedMachine = machine;
        this.MachineGroupName = (await session.Controller.Hook.DetermineMachineGroupNameAsync(session, cancel))._NonNullTrim()._FilledOrDefault("Default");
    }
}

public class ThinControllerOtpServerSettings
{
    public string SmtpServerHostname { get; }
    public int SmtpServerPort { get; }
    public string SmtpServerUsername { get; }
    public string SmtpServerPassword { get; }

    public string AwsSnsRegionEndPointName { get; }
    public string AwsSnsAccessKeyId { get; }
    public string AwsSnsSecretAccessKey { get; }
    public string AwsSnsDefaultCountryCode { get; }

    public ThinControllerOtpServerSettings(string smtpServerHostname, int smtpServerPort, string smtpServerUsername, string smtpServerPassword, string awsSnsRegionEndPointName, string awsSnsAccessKeyId, string awsSnsSecretAccessKey, string awsSnsDefaultCountryCode)
    {
        this.SmtpServerHostname = smtpServerHostname;
        this.SmtpServerPort = smtpServerPort._ZeroToDefault(Consts.Ports.Smtp);
        this.SmtpServerUsername = smtpServerUsername;
        this.SmtpServerPassword = smtpServerPassword;
        this.AwsSnsRegionEndPointName = awsSnsRegionEndPointName;
        this.AwsSnsAccessKeyId = awsSnsAccessKeyId;
        this.AwsSnsSecretAccessKey = awsSnsSecretAccessKey;
        this.AwsSnsDefaultCountryCode = awsSnsDefaultCountryCode._FilledOrDefault("+81");
    }
}

public class ThinControllerSession : IDisposable, IAsyncDisposable
{
    static RefLong IdSeed = new RefLong();

    public string Uid { get; }
    public ThinController Controller { get; }
    public ThinControllerSessionClientInfo ClientInfo { get; }

    public string FunctionName { get; private set; } = "Unknown";

    public ThinControllerSession(ThinController controller, ThinControllerSessionClientInfo clientInfo)
    {
        try
        {
            this.Uid = Str.NewUid("REQ", '_') + "_" + IdSeed.Increment().ToString("D12");
            this.Controller = controller;
            this.ClientInfo = clientInfo;
        }
        catch
        {
            this._DisposeSafe();
            throw;
        }
    }

    // 重い処理の Rate Limiter を開始
    public WpcResult? TryEnterHeavyRateLimiter()
    {
        if (this.ClientInfo.ClientIpForRateLimiter._IsFilled())
        {
            if (this.Controller.HeavyRequestRateLimiter.TryInput(this.ClientInfo.ClientIpForRateLimiter, out RateLimiterEntry? e) == false)
            {
                var ret = new WpcResult(VpnError.ERR_TEMP_ERROR);
                if (e != null)
                {
                    ret.AdditionalInfo.Add("HeavyRequestRateLimiter.CurrentAmount", e.CurrentAmount.ToString());
                    ret.AdditionalInfo.Add("HeavyRequestRateLimiter.Burst", e.Options.Burst.ToString());
                    ret.AdditionalInfo.Add("HeavyRequestRateLimiter.LimitPerSecond", e.Options.LimitPerSecond.ToString());
                }
                return ret;
            }
        }

        return null;
    }

    // 設定文字列の取得
    public WpcResult ProcGetEnvStr(WpcPack req, CancellationToken cancel)
    {
        string name = "Env_" + req.Pack["Name"].StrValueNonNull;
        string s = Controller.Db.GetVarString(name)._NonNullTrim();

        var ret = NewWpcResult();
        var p = ret.Pack;
        p.AddStr("Ret", s);

        ret.AdditionalInfo.Add("Name", name);
        ret.AdditionalInfo.Add("Ret", s);

        return ret;
    }

    // テスト
    public WpcResult ProcTest(WpcPack req, CancellationToken cancel)
    {
        Pack p = new Pack();

        p.AddStr("test", req.Pack["1"].Int64Value.ToString());

        return new WpcResult(VpnError.ERR_SECURITY_ERROR);
    }

    // 通信テスト
    public WpcResult ProcCommCheck(WpcPack req, CancellationToken cancel)
    {
        Pack p = new Pack();

        p.AddStr("retstr", req.Pack["str"].StrValue._NonNull());

        return NewWpcResult(p);
    }

    // WOL MAC アドレス取得
    public async Task<WpcResult> ProcClientGetWolMacList(WpcPack req, CancellationToken cancel)
    {
        var q = req.Pack;

        // svcName を取得 (正規化)
        string svcName = NormalizeSvcName(q["SvcName"].StrValueNonNull);
        if (svcName._IsEmpty()) return NewWpcResult(VpnError.ERR_SVCNAME_NOT_FOUND);

        string pcid = q["Pcid"].StrValueNonNull;

        // svcName と PCID から Machine を取得
        var machine = await Controller.Db.SearchMachineByPcidAsync(svcName, pcid, cancel);

        if (machine == null)
        {
            // PCID が見つからない
            var ret2 = NewWpcResult(VpnError.ERR_PCID_NOT_FOUND);
            ret2.AdditionalInfo.Add("SvcName", svcName);
            ret2.AdditionalInfo.Add("Pcid", pcid);
            return ret2;
        }

        if (machine.WOL_MACLIST._IsEmpty())
        {
            // オンメモリ上のデータベース上で WOL_MACLIST が空の場合はデータベースから読み込む
            var machine2 = await Controller.Db.SearchMachineByMsidFromDbForceAsync(machine.MSID, cancel);

            if (machine2 != null)
            {
                // データベースから読み込んだものを利用
                machine = machine2;
            }
        }

        var ret = NewWpcResult();
        var p = ret.Pack;
        p.AddStr("wol_maclist", machine.WOL_MACLIST._NonNullTrim());

        ret.AdditionalInfo.Add("SvcName", machine.SVC_NAME);
        ret.AdditionalInfo.Add("Pcid", machine.PCID);
        ret.AdditionalInfo.Add("Msid", machine.MSID);
        ret.AdditionalInfo.Add("WoL_MacList", machine.WOL_MACLIST._NonNullTrim());

        Controller.StatMan?.AddReport("ProcClientGetWolMacList_Total", 1);
        Controller.Throughput_ClientGetWolMacList.Add(1);

        return ret;
    }

    // クライアントからの接続要求を処理
    public async Task<WpcResult> ProcClientConnectAsync(WpcPack req, CancellationToken cancel)
    {
        var q = req.Pack;

        // svcName を取得 (正規化)
        string svcName = NormalizeSvcName(q["SvcName"].StrValueNonNull);
        if (svcName._IsEmpty()) return NewWpcResult(VpnError.ERR_SVCNAME_NOT_FOUND);

        string pcid = q["Pcid"].StrValueNonNull;
        int ver = q["Ver"].SIntValue;
        int build = q["Build"].SIntValue;
        ulong clientFlags = q["ClientFlags"].Int64Value;
        ulong clientOptions = q["ClientOptions"].Int64Value;

        // svcName と PCID から Machine を取得
        var machine = await Controller.Db.SearchMachineByPcidAsync(svcName, pcid, cancel);

        if (machine == null)
        {
            // PCID が見つからない
            var ret2 = NewWpcResult(VpnError.ERR_PCID_NOT_FOUND);
            ret2.AdditionalInfo.Add("SvcName", svcName);
            ret2.AdditionalInfo.Add("Pcid", pcid);
            return ret2;
        }

        // 行政情報システム適合モードかどうか
        bool isLimitedMode = machine.LAST_FLAG._InStr("limited", true);

        // 接続先の Gate と Session を取得
        var gateAndSession = Controller.SessionManager.SearchServerSessionByMsid(machine.MSID);

        if (gateAndSession == null)
        {
            // PCID はデータベースに存在するがセッションが存在しない
            var ret2 = NewWpcResult(VpnError.ERR_DEST_MACHINE_NOT_EXISTS);
            ret2.AdditionalInfo.Add("SvcName", svcName);
            ret2.AdditionalInfo.Add("Pcid", pcid);
            return ret2;
        }

        // IP アドレス等を元にした接続禁止を行なう場合、禁止の旨のメッセージを応答する
        string? prohibitedMessage = await Controller.Hook.DetermineConnectionProhibitedAsync(this.Controller, true, gateAndSession.B.IpAddress, this.ClientInfo.ClientIp, machine);

        if (prohibitedMessage._IsFilled())
        {
            // 接続禁止メッセージを応答
            var ret2 = NewWpcResult(VpnError.ERR_RECV_MSG);
            ret2.Pack.AddUniStr("Msg", prohibitedMessage);
            ret2.AdditionalInfo.Add("SvcName", svcName);
            ret2.AdditionalInfo.Add("Pcid", pcid);
            ret2.AdditionalInfo.Add("MsgForClient", prohibitedMessage);
            return ret2;
        }

        if ((clientOptions & 1) != 0)
        {
            // WoL クライアントである
            if (gateAndSession.B.ServerMask64.Bit(ThinServerMask64.SupportWolTrigger) == false)
            {
                // トリガー PC のバージョンが WoL トリガー機能がない古いバージョンである
                var ret2 = NewWpcResult(VpnError.ERR_WOL_TRIGGER_NOT_SUPPORTED);
                ret2.AdditionalInfo.Add("SvcName", svcName);
                ret2.AdditionalInfo.Add("Pcid", pcid);
                return ret2;
            }
        }

        if (ThinControllerGlobalSettings.PaidService_Enabled && this.Controller.Db.IsDatabaseConnected)
        {
            // 商用サービス開発者向け: 体験版の有効期限切れの場合や製品版の解約済みの場合は指定された URL を表示して接続をキャンセル
            // 注意: 物理データベースとの接続が切れてしまっている場合は、無条件に接続を許可する。

            ThinControllerRpcServerObjectInfo? currentStatus = null;

            // まずオンメモリデータベースを検索する。
            var licenseStatusOnMemDb = this.Controller.Paid_CalcServerLicenseStatus(machine.PCID, machine.JSON_ATTRIBUTES, machine.FIRST_CLIENT_DATE._AsDateTimeOffset(true, true));

            if (licenseStatusOnMemDb.Status == ThinControllerPaidServiceRedirectUrlStatusFlag.Deactivated || licenseStatusOnMemDb.Status == ThinControllerPaidServiceRedirectUrlStatusFlag.TrialExpired)
            {
                // オンメモリデータベース上はまだ有効化されていないが、物理データベース上はすでに有効化されている可能性もあるのでオンメモリデータベースを検索する
                var machineOnDb = await Controller.Db.SearchMachineByMsidFromDbForceAsync(machine.MSID, cancel);

                if (machineOnDb != null)
                {
                    var licenseStatusOnPhysicalDb = this.Controller.Paid_CalcServerLicenseStatus(machineOnDb.PCID, machineOnDb.JSON_ATTRIBUTES, machineOnDb.FIRST_CLIENT_DATE._AsDateTimeOffset(true, true));

                    currentStatus = licenseStatusOnPhysicalDb;
                }
                else
                {
                    // MACHINE 情報が物理 DB でも見つからなかった。本来はここに来ないはず。
                }
            }
            else
            {
                currentStatus = licenseStatusOnMemDb;
            }

            if (currentStatus != null)
            {
                if (currentStatus.Status == ThinControllerPaidServiceRedirectUrlStatusFlag.Deactivated || currentStatus.Status == ThinControllerPaidServiceRedirectUrlStatusFlag.TrialExpired)
                {
                    // 体験版の有効期限切れの場合や製品版の解約済みの場合は指定された URL を表示して接続をキャンセル
                    var ret2 = NewWpcResult(VpnError.ERR_RECV_URL);
                    string url = ThinControllerGlobalSettings.PaidService_RedirectUrl.Value._ReplaceStr("<STATUS>", currentStatus.Status.ToString())
                        ._ReplaceStr("<EXPIRED>", (currentStatus.Status == ThinControllerPaidServiceRedirectUrlStatusFlag.TrialExpired) ? currentStatus.TrialExpireDateTime._ToYymmddHhmmssLong().ToString() : currentStatus.DeactivatedDateTime._ToYymmddHhmmssLong().ToString())
                        ._ReplaceStr("<PCID>", currentStatus.Pcid)
                        ._ReplaceStr("<TAG>", currentStatus.Tag);
                    ret2.Pack.AddStr("Url", url);
                    ret2.AdditionalInfo.Add("CurrentStatus", currentStatus._ObjectToJson(compact: true));
                    ret2.AdditionalInfo.Add("RedirectUrl", url);
                    return ret2;
                }
            }
        }

        var ret = NewWpcResult();
        var p = ret.Pack;

        string hostname = (await Controller.Hook.GenerateFqdnForGateAsync(gateAndSession.A, this, ThinControllerFqdnUsage.ClientConnect, "thinclient-", "", cancel))._FilledOrDefault(gateAndSession.A.IpAddress);
        string hostnameForProxy = (await Controller.Hook.GenerateFqdnForGateAsync(gateAndSession.A, this, ThinControllerFqdnUsage.ClientConnect | ThinControllerFqdnUsage.ViaProxy, "thinclient-", "", cancel))._FilledOrDefault(gateAndSession.A.IpAddress);

        p.AddStr("Hostname", hostname);
        p.AddStr("HostnameForProxy", hostnameForProxy);
        p.AddInt("Port", (uint)gateAndSession.A.Port);
        p.AddData("SessionId", gateAndSession.B.SessionId._GetHexBytes());

        ThinServerMask64 serverMask64 = gateAndSession.B.ServerMask64;
        if (isLimitedMode)
            serverMask64 |= ThinServerMask64.IsLimitedMode; // 行政情報システム適合モード (ThinController が勝手に付ける)
        p.AddInt64("ServerMask64", (ulong)serverMask64);
        p.AddStr("WebSocketWildCardDomainName", this.Controller.WebSocketCertMaintainer.GetCertData()?.DomainName ?? "");

        ret.AdditionalInfo.Add("SvcName", machine.SVC_NAME);
        ret.AdditionalInfo.Add("Pcid", machine.PCID);
        ret.AdditionalInfo.Add("Msid", machine.MSID);
        ret.AdditionalInfo.Add("GateHostname", hostname);
        ret.AdditionalInfo.Add("GateHostnameForProxy", hostnameForProxy);
        ret.AdditionalInfo.Add("GatePort", gateAndSession.A.Port.ToString());

        // データベース更新
        Controller.Db.UpdateDbForClientConnect(machine.MSID, DtNow, ClientInfo.ClientIp);

        Controller.StatMan?.AddReport("ProcClientConnectAsync_Total", 1);
        Controller.Throughput_ClientConnect.Add(1);

        return ret;
    }

    string NormalizeSvcName(string svcName)
    {
        if (svcName._IsEmpty()) return "";
        svcName = Controller.Db.MemDb?.SvcBySvcName._GetOrDefault(svcName)?.SVC_NAME ?? "";
        if (svcName._IsEmpty()) return "";
        return svcName;
    }

    // PCID の変更
    public async Task<WpcResult> ProcRenameMachine(WpcPack req, CancellationToken cancel)
    {
        var notAuthedErr = RequireMachineAuth(); if (notAuthedErr != null) return notAuthedErr; // 認証を要求
        var heavyErr = TryEnterHeavyRateLimiter(); if (heavyErr != null) return heavyErr; // 重い処理なので Rate Limit を設定

        var q = req.Pack;
        string newPcid = q["NewName"].StrValueNonNull;

        // 変更の実行
        var now = DtNow;
        var err = await Controller.Db.RenamePcidAsync(this.ClientInfo.AuthedMachine!.MSID, newPcid, now, cancel);
        if (err != VpnError.ERR_NO_ERROR)
        {
            // 登録エラー
            var ret2 = NewWpcResult(err);
            ret2.AdditionalInfo.Add("OldPcid", this.ClientInfo.AuthedMachine!.PCID);
            ret2.AdditionalInfo.Add("NewPcid", newPcid);
            return ret2;
        }

        var ret = NewWpcResult();
        var p = ret.Pack;

        ret.AdditionalInfo.Add("OldPcid", this.ClientInfo.AuthedMachine!.PCID);
        ret.AdditionalInfo.Add("NewPcid", newPcid);

        Controller.StatMan?.AddReport("ProcRenameMachine_Total", 1);
        Controller.Throughput_RenameMachine.Add(1);

        return ret;
    }

    // サーバーの登録
    public async Task<WpcResult> ProcRegistMachine(WpcPack req, CancellationToken cancel)
    {
        if (req.HostKey._IsEmpty() || req.HostSecret2._IsEmpty())
        {
            return NewWpcResult(VpnError.ERR_PROTOCOL_ERROR);
        }

        var heavyErr = TryEnterHeavyRateLimiter(); if (heavyErr != null) return heavyErr; // 重い処理なので Rate Limit を設定

        var q = req.Pack;

        // svcName を取得 (正規化)
        string svcName = NormalizeSvcName(q["SvcName"].StrValueNonNull);
        if (svcName._IsEmpty()) return NewWpcResult(VpnError.ERR_SVCNAME_NOT_FOUND);

        string pcid = q["Pcid"].StrValueNonNull;

        // データベースエラー時は処理禁止
        if (Controller.Db.IsDatabaseConnected == false)
        {
            return NewWpcResult(VpnError.ERR_TEMP_ERROR);
        }

        // 登録キーのチェック (DB に RegistrationKey がある場合のみ)
        string registrationPassword = q["RegistrationPassword"].StrValueNonNull;
        string registrationEmail = q["RegistrationEmail"].StrValueNonNull;
        bool registrationKeyOk = false;
        var regKeyVars = Controller.Db.GetVars("RegistrationKey");
        if (regKeyVars?.Where(x => x.VAR_VALUE1._IsFilled()).Any() ?? false)
        {
            if (regKeyVars.Where(x => x.VAR_VALUE1 == registrationPassword && x.VAR_VALUE1._IsFilled()).Any())
            {
                registrationKeyOk = true;
            }

            if (registrationKeyOk == false)
            {
                // 登録キー不正
                var ret2 = NewWpcResult(registrationPassword._IsEmpty() ? VpnError.ERR_REG_PASSWORD_EMPTY : VpnError.ERR_REG_PASSWORD_INCORRECT);
                ret2.AdditionalInfo.Add("RegistrationPassword", registrationPassword);
                ret2.AdditionalInfo.Add("RegistrationEmail", registrationEmail);
                return ret2;
            }
        }

        // PCID チェック
        var err = ThinController.CheckPCID(pcid);
        if (err != VpnError.ERR_NO_ERROR)
        {
            // PCID 不正
            var ret2 = NewWpcResult(err);
            ret2.AdditionalInfo.Add("SvcName", svcName);
            ret2.AdditionalInfo.Add("Pcid", pcid);
            return ret2;
        }

        // PCID の簡易存在検査 (メモリ上とデータベース上のいずれにも存在しないことを確認)
        // データベースにトランザクションはかけていないが、一意インデックスで一意性は保証されるので
        // この軽いチェックのみで問題はない
        if (await Controller.Db.SearchMachineByPcidAsync(svcName, pcid, cancel) != null)
        {
            // 既に存在する
            var ret2 = NewWpcResult(VpnError.ERR_PCID_ALREADY_EXISTS);
            ret2.AdditionalInfo.Add("SvcName", svcName);
            ret2.AdditionalInfo.Add("Pcid", pcid);
            return ret2;
        }

        // MSID の生成
        string msid = ThinController.GenerateMsid(req.HostKey, svcName);

        // 登録の実行
        EasyJsonStrAttributes attributes = new EasyJsonStrAttributes();

        if (registrationKeyOk)
        {
            attributes["RegistrationPassword"] = registrationPassword;
            attributes["RegistrationEmail"] = registrationEmail;
        }

        var now = DtNow;
        err = await Controller.Db.RegisterMachineAsync(svcName, msid, q["Pcid"].StrValueNonNull, req.HostKey, req.HostSecret2, now, this.ClientInfo.ClientIp, this.ClientInfo.ClientIpFqdn,
            attributes, cancel);
        if (err != VpnError.ERR_NO_ERROR)
        {
            // 登録エラー
            var ret2 = NewWpcResult(err);
            ret2.AdditionalInfo.Add("SvcName", svcName);
            ret2.AdditionalInfo.Add("Pcid", pcid);
            ret2.AdditionalInfo.Add("HostKey", req.HostKey);
            ret2.AdditionalInfo.Add("Msid", msid);
            return ret2;
        }

        var ret = NewWpcResult();
        var p = ret.Pack;

        ret.AdditionalInfo.Add("SvcName", svcName);
        ret.AdditionalInfo.Add("Pcid", pcid);
        ret.AdditionalInfo.Add("HostKey", req.HostKey);
        ret.AdditionalInfo.Add("Msid", msid);

        if (registrationKeyOk)
        {
            ret.AdditionalInfo.Add("RegistrationPassword", registrationPassword);
            ret.AdditionalInfo.Add("RegistrationEmail", registrationEmail);
        }

        Controller.StatMan?.AddReport("ProcRegistMachine_Total", 1);
        Controller.Throughput_RegistMachine.Add(1);

        return ret;
    }

    // PCID 候補を取得
    public async Task<WpcResult> ProcGetPcidCandidate(WpcPack req, CancellationToken cancel)
    {
        var q = req.Pack;

        // svcName を取得 (正規化)
        string svcName = NormalizeSvcName(q["SvcName"].StrValueNonNull);
        if (svcName._IsEmpty()) return NewWpcResult(VpnError.ERR_SVCNAME_NOT_FOUND);

        // データベースエラー時は処理禁止
        if (Controller.Db.IsDatabaseConnected == false)
        {
            return NewWpcResult(VpnError.ERR_TEMP_ERROR);
        }

        string fqdn = await Controller.DnsResolver.GetHostNameOrIpAsync(this.ClientInfo.ClientIp, cancel);
        string machineName = q["MachineName"].StrValueNonNull;
        string computerName = q["ComputerName"].StrValueNonNull;
        string userName = q["UserName"].StrValueNonNull;

        string candidate = await Controller.GeneratePcidCandidateAsync(svcName, fqdn, machineName, userName, computerName, cancel);

        var ret = NewWpcResult();
        var p = ret.Pack;
        p.AddStr("Ret", candidate);

        ret.AdditionalInfo.Add("Candidate", candidate);
        ret.AdditionalInfo.Add("svcName", svcName);
        ret.AdditionalInfo.Add("fqdn", fqdn);
        ret.AdditionalInfo.Add("machineName", machineName);
        ret.AdditionalInfo.Add("userName", userName);
        ret.AdditionalInfo.Add("computerName", computerName);

        Controller.StatMan?.AddReport("ProcGetPcidCandidate_Total", 1);

        return ret;
    }

    // OTP メールを送信
    public async Task<WpcResult> ProcSendOtpEmailAsync(WpcPack req, CancellationToken cancel)
    {
        var notAuthedErr = RequireMachineAuth(); if (notAuthedErr != null) return notAuthedErr; // 認証を要求

        Pack q = req.Pack;

        string otp = q["Otp"].StrValueNonNull;
        string email = q["Email"].StrValueNonNull;
        string clientIp = q["Ip"].StrValueNonNull;
        string clientFqdn = q["Fqdn"].StrValueNonNull;
        string pcid = ClientInfo.AuthedMachine!.PCID;

        int pcidMaskLen = Math.Min(pcid.Length / 2, 4);
        if (pcidMaskLen == pcid.Length && pcidMaskLen >= 1)
        {
            pcidMaskLen--;
        }

        string pcidMasked = pcid.Substring(0, pcid.Length - pcidMaskLen);
        for (int i = 0; i < pcidMaskLen; i++)
        {
            pcidMasked += '*';
        }

        string otpFrom = Controller.Db.GetVarString("SmtpOtpFrom")._NonNullTrim();

        ThinControllerOtpServerSettings serverSettings = new ThinControllerOtpServerSettings(
            Controller.Db.GetVarString("SmtpServerHostname")._NonNullTrim(),
            Controller.Db.GetVarString("SmtpServerPort")._NonNullTrim()._ToInt(),
            Controller.Db.GetVarString("SmtpUsername")._NonNullTrim(),
            Controller.Db.GetVarString("SmtpPassword")._NonNullTrim(),
            Controller.Db.GetVarString("AwsSnsRegionEndPointName")._NonNullTrim(),
            Controller.Db.GetVarString("AwsSnsAccessKeyId")._NonNullTrim(),
            Controller.Db.GetVarString("AwsSnsSecretAccessKey")._NonNullTrim(),
            Controller.Db.GetVarString("AwsSnsDefaultCountryCode")._NonNullTrim()
            );

        bool ok = await Controller.Hook.SendOtpEmailAsync(Controller, otp, email, otpFrom, clientIp, clientFqdn, pcidMasked, pcid, serverSettings, cancel);
        var ret = NewWpcResult();
        var p = ret.Pack;

        ret.AdditionalInfo.Add("otp", otp);
        ret.AdditionalInfo.Add("email", email);
        ret.AdditionalInfo.Add("clientIp", clientIp);
        ret.AdditionalInfo.Add("clientFqdn", clientFqdn);
        ret.AdditionalInfo.Add("pcid", pcid);
        ret.AdditionalInfo.Add("pcidMasked", pcidMasked);

        Controller.StatMan?.AddReport("ProcSendOtpEmailAsync_Total", 1);
        Controller.Throughput_SendOtpEmail.Add(1);

        return ret;
    }


    // サーバーからの接続要求を処理
    public async Task<WpcResult> ProcServerConnectAsync(WpcPack req, CancellationToken cancel)
    {
        var notAuthedErr = RequireMachineAuth(); if (notAuthedErr != null) return notAuthedErr; // 認証を要求

        // 設定変数の "PreferredGateIpRange_Group_<グループ名>" に書かれている ACL を取得する
        string preferredIpAcl = Controller.Db.GetVarString($"PreferredGateIpRange_Group_{this.ClientInfo.MachineGroupName}")._NonNullTrim();
        bool forceAcl = false;
        if (preferredIpAcl._TryTrimStartWith(out preferredIpAcl, StringComparison.OrdinalIgnoreCase, "[force]"))
        {
            // 先頭が [force] で始まっていれば、強制適用
            forceAcl = true;
        }
        var acl = EasyIpAcl.GetOrCreateCachedIpAcl(preferredIpAcl);

        string preferGate = this.ClientInfo.HttpQueryStringList._GetStrFirst("prefer_gate");

        // 最良の Gate を選択
        var bestGate = Controller.SessionManager.SelectBestGateForServer(acl, Controller.Db.MemDb?.MaxSessionsPerGate ?? 0, !forceAcl, preferGate);

        if (bestGate == null)
        {
            // 適合 Gate なし (混雑?)
            return NewWpcResult(VpnError.ERR_NO_GATE_CAN_ACCEPT);
        }

        // 選択された Gate のセッション数を仮に 1 つ増加させる
        Interlocked.Increment(ref bestGate.NumSessions);

        ulong expires = (ulong)Util.ConvertDateTime(DtUtcNow.AddMinutes(20));

        var gateIdData = bestGate.GateId._GetHexBytes();

        MemoryBuffer<byte> b = new MemoryBuffer<byte>();
        b.Write(ClientInfo.AuthedMachine!.MSID._GetBytes_Ascii());
        b.WriteUInt64(expires);
        b.Write(gateIdData);

        var ret = NewWpcResult();
        var p = ret.Pack;
        p.AddStr("Msid", ClientInfo.AuthedMachine!.MSID);
        p.AddData("GateId", gateIdData);
        p.AddInt64("Expires", expires);

        // 署名
        MemoryBuffer<byte> signSrc = new MemoryBuffer<byte>();
        signSrc.Write((Controller.Db.MemDb?.ControllerGateSecretKey ?? "")._GetBytes_Ascii());
        signSrc.Write(b.Span);
        byte[] sign = Secure.HashSHA1(signSrc.Span);
        p.AddData("Signature2", sign);
        p.AddStr("Pcid", this.ClientInfo.AuthedMachine!.PCID);

        string hostname = (await Controller.Hook.GenerateFqdnForGateAsync(bestGate, this, ThinControllerFqdnUsage.ServerConnect, "thinserver-", "", cancel))._FilledOrDefault(bestGate.IpAddress);
        string hostnameForProxy = (await Controller.Hook.GenerateFqdnForGateAsync(bestGate, this, ThinControllerFqdnUsage.ServerConnect | ThinControllerFqdnUsage.ViaProxy, "thinserver-", "", cancel))._FilledOrDefault(bestGate.IpAddress);

        p.AddStr("Hostname", hostname);
        p.AddStr("HostnameForProxy", hostnameForProxy);

        p.AddInt("Port", (uint)bestGate.Port);

        // IP アドレス等を元にした接続禁止を行なう場合、禁止の旨のメッセージを応答する (接続処理自体は成功させる)
        string? prohibitedMessage = await Controller.Hook.DetermineConnectionProhibitedAsync(this.Controller, false, this.ClientInfo.ClientIp, null, this.ClientInfo.AuthedMachine!);

        if (prohibitedMessage._IsFilled())
        {
            p.AddUniStr("MsgForServer", prohibitedMessage);
            p.AddInt("MsgForServerOnce", 0);
            ret.AdditionalInfo.Add("MsgForServer", prohibitedMessage);
        }

        ret.AdditionalInfo.Add("SvcName", this.ClientInfo.AuthedMachine!.SVC_NAME);
        ret.AdditionalInfo.Add("Pcid", this.ClientInfo.AuthedMachine!.PCID);
        ret.AdditionalInfo.Add("Msid", this.ClientInfo.AuthedMachine!.MSID);
        ret.AdditionalInfo.Add("GateHostname", hostname);
        ret.AdditionalInfo.Add("GateHostnameForProxy", hostnameForProxy);
        ret.AdditionalInfo.Add("GatePort", bestGate.Port.ToString());

        string wolMacList = req.Pack["wol_maclist"].StrValueNonNull;
        long serverMask64 = req.Pack["ServerMask64"].SInt64Value;

        var machine = this.ClientInfo.AuthedMachine!;

        if (Controller.Db.IsDatabaseConnected) // MAC アドレス関係の DB 書き込みは DB 接続が成功している場合のみ
        {
            if (wolMacList._IsFilled() && machine.WOL_MACLIST._IsSamei(wolMacList) == false)
            {
                // WoL MAC アドレスリストが登録されようとしており、かつ、現在の DB の内容と異なる場合は、
                // 直ちに DB 強制更新する

                await Controller.Db.UpdateDbForWolMacAsync(ClientInfo.AuthedMachine!.MSID, wolMacList, serverMask64, DtNow, cancel);

                machine.WOL_MACLIST = wolMacList;
                machine.SERVERMASK64 = serverMask64;
            }
        }

        // DB 更新 (遅延)
        string realProxyIp = "";
        if (ClientInfo.ClientIp._IsSameIPAddress(ClientInfo.ClientPhysicalIp) == false)
        {
            realProxyIp = ClientInfo.ClientPhysicalIp;
        }

        Controller.Db.UpdateDbForServerConnect(ClientInfo.AuthedMachine!.MSID, DtNow, ClientInfo.ClientIp, realProxyIp,
            ClientInfo.HttpQueryStringList._GetStrFirst("flag"),
            wolMacList,
            serverMask64);

        Controller.StatMan?.AddReport("ProcServerConnectAsync_Total", 1);
        Controller.Throughput_ServerConnect.Add(1);

        return ret;
    }


    // サーバーが未登録の場合は登録を要求する
    WpcResult? RequireMachineAuth()
    {
        if (this.ClientInfo.IsAuthed == false)
        {
            return NewWpcResult(VpnError.ERR_NO_INIT_CONFIG);
        }
        return null;
    }

    // Gate のセキュリティ検査
    async Task GateSecurityCheckAsync(WpcPack req, CancellationToken cancel)
    {
        if (this.ClientInfo.ServiceType != ThinControllerServiceType.ApiServiceForGateway)
        {
            throw new CoresException($"ClientInfo.ServiceType != ThinControllerServiceType.ApiServiceForGateway. IP: {ClientInfo.ClientIp}, Physical IP: {ClientInfo.ClientPhysicalIp}");
        }

        string gateKey = req.Pack["GateKey"].StrValueNonNull;

        bool ok = false;

        if (gateKey._IsFilled())
        {
            ok = Controller.Db.GetVars("GateKeyList")?.Where(x => x.VAR_VALUE1 == gateKey).Any() ?? false;
        }

        if (ok == false)
        {
            throw new CoresException($"The specified gateKey '{gateKey}' is invalid. IP: {ClientInfo.ClientIp}, Physical IP: {ClientInfo.ClientPhysicalIp}");
        }

        // Gate の場合 IP 逆引きの実行
        await this.ClientInfo.ResolveClientIpFqdnIfPossibleAsync(Controller.DnsResolver, cancel);
    }

    // Gate から: セッションリストの報告
    public async Task<WpcResult> ProcReportSessionListAsync(WpcPack req, CancellationToken cancel)
    {
        await GateSecurityCheckAsync(req, cancel);

        Pack p = req.Pack;

        int numSessions = p["NumSession"].SIntValueSafeNum;

        Dictionary<string, ThinSession> sessionList = new Dictionary<string, ThinSession>();

        for (int i = 0; i < numSessions; i++)
        {
            ThinSession sess = new ThinSession
            {
                Msid = p["Msid", i].StrValueNonNull,
                SessionId = p["SessionId", i].DataValueHexStr,
                EstablishedDateTime = Util.ConvertDateTime(p["EstablishedDateTime", i].Int64Value).ToLocalTime(),
                IpAddress = p["IpAddress", i].StrValueNonNull,
                HostName = p["Hostname", i].StrValueNonNull,
                NumClients = p["NumClients", i].SIntValue,
                ServerMask64 = (ThinServerMask64)p["ServerMask64", i].Int64Value,
                LocalVersion = p["LocalVersion", i].StrValueNonNull,
                LocalHostname = p["LocalHostname", i].UniStrValueNonNull,
                LocalIp = p.GetIp("LocalIp", (uint)i)?.ToString() ?? "",
            };

            sess.Normalize();
            sess.Validate();

            sessionList.TryAdd(sess.SessionId, sess);
        }

        ThinGate gate = new ThinGate
        {
            GateId = p["GateId"].DataValueHexStr,
            IpAddress = this.ClientInfo.ClientIp,
            Port = p["Port"].SIntValue,
            HostName = this.ClientInfo.ClientIpFqdn,
            Performance = p["Performance"].SIntValue,
            NumSessions = sessionList.Count,
            Build = p["Build"].SIntValue,
            MacAddress = p["MacAddress"].StrValueNonNull,
            OsInfo = p["OsInfo"].StrValueNonNull,
            ThinLibCommitId = p["UltraCommitId"].StrValueNonNull,
            CurrentTime = p["CurrentTime"].DateTimeValue.ToLocalTime(),
            BootTick = p["BootTick"].Int64Value._ToTimeSpanMSecs(),
            Caps = (ThinGateCaps)p["Caps"].Int64Value,
            StatusMessage = p["StatMsg"].StrValueNonNull,
        };

        if (gate.Performance == 0) gate.Performance = 100;

        // Performance の値を DB の Vars で上書き
        int v = this.Controller.Db.GetVarInt($"PerformanceOverride_{gate.IpAddress}");
        if (v != 0)
        {
            gate.Performance = v;
        }

        gate.Normalize();
        gate.Validate();

        int numSc = (int)Math.Min(p.GetCount("SC_SessionId"), 65536);

        Dictionary<string, HashSet<string>> sessionAndClientTable = new Dictionary<string, HashSet<string>>();
        for (int i = 0; i < numSc; i++)
        {
            string sessionId = p["SC_SessionId", i].DataValueHexStr;
            string clientId = p["SC_ClientID", i].DataValueHexStr;
            bool isWebSocket = p["SC_IsWebSocket", i].BoolValue;

            if (sessionId.Length == 40 && clientId.Length == 40)
            {
                sessionId = sessionId.ToUpperInvariant();
                clientId = clientId.ToUpperInvariant();

                sessionAndClientTable._GetOrNew(sessionId, () => new HashSet<string>()).Add(clientId);

                if (isWebSocket)
                {
                    var sess = sessionList._GetOrDefault(sessionId);
                    if (sess != null) sess.NumClientsWebSocket++;
                }
            }
        }

        foreach (var item in sessionAndClientTable)
        {
            var sess = sessionList._GetOrDefault(item.Key);
            if (sess != null) sess.NumClientsUnique++;
        }

        bool exists = false;
        if (Controller.SessionManager.GateTable.TryGetValue(gate.GateId, out ThinGate? currentGate))
        {
            if (DtNow <= currentGate.Expires)
            {
                exists = true;
            }
        }
        if (exists == false)
        {
            // この Gate がまだ GateTable に存在しない。初めての登録試行であるため、この Gate に本当に到達可能かどうかチェックする
            if (Controller.Db.GetVarBool("TestGateTcpReachability"))
            {
                using var tcp = new TcpClient();
                tcp.NoDelay = true;
                tcp.ReceiveTimeout = tcp.ReceiveTimeout = 5 * 1000;
                await tcp.ConnectAsync(gate.IpAddress, gate.Port, cancel);
                await using var stream = tcp.GetStream();
                await using var sslStream = new SslStream(stream, false, (sender, cert, chain, err) => true);
                stream.ReadTimeout = 5 * 1000;
                stream.WriteTimeout = 5 * 1000;
                sslStream.ReadTimeout = 5 * 1000;
                sslStream.WriteTimeout = 5 * 1000;
                await sslStream.AuthenticateAsClientAsync(
                    new SslClientAuthenticationOptions()
                    {
                        AllowRenegotiation = false,
                        CertificateRevocationCheckMode = System.Security.Cryptography.X509Certificates.X509RevocationMode.NoCheck,
                        EnabledSslProtocols = CoresConfig.SslSettings.DefaultSslProtocolVersionsAsClient,
                        TargetHost = gate.IpAddress.ToString(),
                    }, cancel
                    );
            }
        }

        var now = DtNow;

        Controller.SessionManager.UpdateGateAndReportSessions(now, now.AddMilliseconds(p["EntryExpires"].SIntValue), gate, sessionList.Values);

        var ret = NewWpcResult();

        ret.AdditionalInfo.Add("Gate", gate._GetObjectDumpForJsonFriendly());

        AddGateSettingsToPack(ret.Pack, gate);

        Controller.StatMan?.AddReport("ProcReportSessionListAsync_Total", 1);
        Controller.Throughput_ReportSessionList.Add(1);

        return ret;
    }

    // Gate から: 1 本のセッションの追加の報告
    public async Task<WpcResult> ProcReportSessionAddAsync(WpcPack req, CancellationToken cancel)
    {
        await GateSecurityCheckAsync(req, cancel);

        Pack p = req.Pack;

        int i = 0;
        ThinSession sess = new ThinSession
        {
            Msid = p["Msid", i].StrValueNonNull,
            SessionId = p["SessionId", i].DataValueHexStr,
            EstablishedDateTime = Util.ConvertDateTime(p["EstablishedDateTime", i].Int64Value).ToLocalTime(),
            IpAddress = p["IpAddress", i].StrValueNonNull,
            HostName = p["Hostname", i].StrValueNonNull,
            NumClients = p["NumClients", i].SIntValue,
            ServerMask64 = (ThinServerMask64)p["ServerMask64", i].Int64Value,
            LocalVersion = p["LocalVersion", i].StrValueNonNull,
            LocalHostname = p["LocalHostname", i].UniStrValueNonNull,
            LocalIp = p.GetIp("LocalIp", (uint)i)?.ToString() ?? "",
        };

        sess.Normalize();
        sess.Validate();

        // gate のフィールド内容は UpdateGateAndAddSession でそれほど参照されないので適当で OK
        ThinGate gate = new ThinGate
        {
            GateId = p["GateId"].DataValueHexStr,
            IpAddress = this.ClientInfo.ClientIp,
            Port = p["Port"].SIntValue,
            HostName = this.ClientInfo.ClientIpFqdn,
            Performance = p["Performance"].SIntValue,
            NumSessions = 0,
            Build = p["Build"].SIntValue,
            MacAddress = p["MacAddress"].StrValueNonNull,
            OsInfo = p["OsInfo"].StrValueNonNull,
            ThinLibCommitId = p["UltraCommitId"].StrValueNonNull,
            CurrentTime = p["CurrentTime"].DateTimeValue.ToLocalTime(),
            BootTick = p["BootTick"].Int64Value._ToTimeSpanMSecs(),
            StatusMessage = p["StatMsg"].StrValueNonNull,
        };

        if (gate.Performance == 0) gate.Performance = 100;

        gate.Normalize();
        gate.Validate();

        sess.NumClientsUnique = sess.NumClients == 0 ? 0 : 1;

        var now = DtNow;

        Controller.SessionManager.UpdateGateAndAddSession(now, now.AddMilliseconds(p["EntryExpires"].SIntValue), gate, sess);

        var ret = NewWpcResult();

        ret.AdditionalInfo.Add("Gate", gate._GetObjectDumpForJsonFriendly());
        ret.AdditionalInfo.Add("AddSession", sess._GetObjectDumpForJsonFriendly());

        AddGateSettingsToPack(ret.Pack, gate);

        Controller.StatMan?.AddReport("ProcReportSessionAddAsync_Total", 1);
        Controller.Throughput_ReportSessionAdd.Add(1);

        return ret;
    }

    // Gate から: 1 本のセッションの削除の報告
    public async Task<WpcResult> ProcReportSessionDelAsync(WpcPack req, CancellationToken cancel)
    {
        await GateSecurityCheckAsync(req, cancel);

        Pack p = req.Pack;

        string sessionId = p["SessionId"].DataValueHexStr;

        // gate のフィールド内容は UpdateGateAndAddSession でそれほど参照されないので適当で OK
        ThinGate gate = new ThinGate
        {
            GateId = p["GateId"].DataValueHexStr,
            IpAddress = this.ClientInfo.ClientIp,
            Port = p["Port"].SIntValue,
            HostName = this.ClientInfo.ClientIpFqdn,
            Performance = p["Performance"].SIntValue,
            NumSessions = 0,
            Build = p["Build"].SIntValue,
            MacAddress = p["MacAddress"].StrValueNonNull,
            OsInfo = p["OsInfo"].StrValueNonNull,
            ThinLibCommitId = p["UltraCommitId"].StrValueNonNull,
            CurrentTime = p["CurrentTime"].DateTimeValue.ToLocalTime(),
            BootTick = p["BootTick"].Int64Value._ToTimeSpanMSecs(),
            StatusMessage = p["StatMsg"].StrValueNonNull,
        };

        if (gate.Performance == 0) gate.Performance = 100;

        gate.Normalize();
        gate.Validate();

        var now = DtNow;

        bool ok = Controller.SessionManager.TryUpdateGateAndDeleteSession(now, now.AddMilliseconds(p["EntryExpires"].SIntValue), gate, sessionId, out ThinSession? session);

        var ret = NewWpcResult();

        ret.AdditionalInfo.Add("Gate", gate._GetObjectDumpForJsonFriendly());

        if (ok && session != null)
        {
            ret.AdditionalInfo.Add("DeleteSession", session._GetObjectDumpForJsonFriendly());
        }

        AddGateSettingsToPack(ret.Pack, gate);

        Controller.StatMan?.AddReport("ProcReportSessionDelAsync_Total", 1);
        Controller.Throughput_ReportSessionDel.Add(1);

        return ret;
    }

    // Gate にとって有用な設定を Gate に返送する
    void AddGateSettingsToPack(Pack p, ThinGate gate)
    {
        ulong nextRebootDt64 = 0;
        DateTime nextRebootDt = gate.NextRebootTime;
        if (nextRebootDt._IsZeroDateTime() == false)
        {
            nextRebootDt64 = (ulong)Util.ConvertDateTime(nextRebootDt.ToUniversalTime());
        }

        p.AddInt64("NextRebootTime64", nextRebootDt64);

        string? secretKey = Controller.Db.GetVarString("ControllerGateSecretKey");
        if (secretKey._IsFilled()) p.AddStr("ControllerGateSecretKey", secretKey);

        var db = Controller.Db.MemDb;
        if (db != null)
        {
            var varsList = db.VarList;
            var names = varsList.Where(x => x.VAR_NAME.StartsWith("GateSettings_Int_", StringComparison.OrdinalIgnoreCase)).Select(x => x.VAR_NAME).Distinct(StrComparer.IgnoreCaseComparer);

            names._DoForEach(x =>
            {
                var item = db.VarByName._GetOrDefault(x)?.FirstOrDefault();
                if (item != null)
                {
                    p.AddInt(item.VAR_NAME, (uint)item.VAR_VALUE1._ToInt());
                }
            });
        }

        ThinControllerWebSocketCertData? webSocketCertData = Controller.WebSocketCertMaintainer?.GetCertData();
        if (webSocketCertData != null)
        {
            // WebSocket 用証明書データ
            if (webSocketCertData.CertsList.Count >= 1 && (webSocketCertData.Key?.Length ?? 0) >= 1 && webSocketCertData.DomainName._IsFilled())
            {
                p.AddStr("WebSocketCertData_DomainName", webSocketCertData.DomainName);
                p.AddData("WebSocketCertData_Key", webSocketCertData.Key!);
                p.AddInt("WebSocketCertData_Cert_Count", (uint)webSocketCertData.CertsList.Count);
                for (int i = 0; i < webSocketCertData.CertsList.Count; i++)
                {
                    p.AddData("WebSocketCertData_Cert", webSocketCertData.CertsList[i], (uint)i);
                }
            }
        }
    }

    public async Task<WpcResult> ProcessWpcRequestCoreAsync(string wpcRequestString, CancellationToken cancel = default)
    {
        try
        {
            if (Controller.Db.IsLoaded == false)
            {
                throw new CoresException("Controller.DB is not loaded yet.");
            }

            // WPC リクエストをパースする
            WpcPack req = WpcPack.Parse(wpcRequestString, false);

            // 関数名を取得する
            this.FunctionName = req.Pack["Function"].StrValue._NonNull();
            if (this.FunctionName._IsEmpty()) this.FunctionName = "Unknown";

            if (FunctionName.StartsWith("report", StringComparison.OrdinalIgnoreCase) == false)
            {
                // Gate からの要求以外の場合は、認証を実施する
                if (req.HostKey._IsFilled() && req.HostSecret2._IsFilled())
                {
                    var authedMachine = await Controller.Db.AuthMachineAsync(req.HostKey, req.HostSecret2, cancel);

                    if (authedMachine != null)
                    {
                        // 認証成功
                        await this.ClientInfo.SetAuthedAsync(this, authedMachine, cancel);
                    }
                }
            }

            cancel.ThrowIfCancellationRequested();

            // 関数を実行する
            switch (this.FunctionName.ToLowerInvariant())
            {
                // テスト系
                case "test": return ProcTest(req, cancel);
                case "commcheck": return ProcCommCheck(req, cancel);

                // Gate 系
                case "reportsessionlist": return await ProcReportSessionListAsync(req, cancel);
                case "reportsessionadd": return await ProcReportSessionAddAsync(req, cancel);
                case "reportsessiondel": return await ProcReportSessionDelAsync(req, cancel);

                // サーバー系
                case "getpcidcandidate": return await ProcGetPcidCandidate(req, cancel);
                case "registmachine": return await ProcRegistMachine(req, cancel);
                case "renamemachine": return await ProcRenameMachine(req, cancel);
                case "serverconnect": return await ProcServerConnectAsync(req, cancel);
                case "sendotpemail": return await ProcSendOtpEmailAsync(req, cancel);

                // クライアント系
                case "clientconnect": return await ProcClientConnectAsync(req, cancel);
                case "clientgetwolmaclist": return await ProcClientGetWolMacList(req, cancel);

                // 共通系
                case "getenvstr": return ProcGetEnvStr(req, cancel);

                default:
                    // 適切な関数が見つからない
                    return NewWpcResult(VpnError.ERR_NOT_SUPPORTED);
            }
        }
        catch (Exception ex)
        {
            // エラー発生
            return NewWpcResult(ex);
        }
    }

    public async Task<string> ProcessWpcRequestAsync(string wpcRequestString, CancellationToken cancel = default)
    {
        // メイン処理の実行
        WpcResult err = await ProcessWpcRequestCoreAsync(wpcRequestString, cancel);

        err._PostAccessLog(ThinControllerConsts.AccessLogTag);

        if (err.IsError)
        {
            err._Error();
        }

        // WPC 応答文字列の回答
        WpcPack wp = err.ToWpcPack();

        return wp.ToPacketString();
    }

    // WPC 応答にクライアント情報を付加
    protected void SetWpcResultAdditionalInfo(WpcResult result)
    {
        var clientInfo = this.ClientInfo;

        // 追加クライアント情報
        var c = result.ClientInfo;

        c.Add("SessionUid", this.Uid);


        c.Add("ServiceType", clientInfo.ServiceType.ToString());

        c.Add("ClientPhysicalIp", clientInfo.ClientPhysicalIp);
        c.Add("ClientIp", clientInfo.ClientIp);
        c.Add("ClientPort", clientInfo.Box?.RemoteEndpoint.Port.ToString() ?? "");
        c.Add("HttpQueryStringList", clientInfo.HttpQueryStringList.ToString());
        c.Add("IsProxyMode", clientInfo.IsProxyMode.ToString());

        c.Add("IsAuthed", clientInfo.IsAuthed.ToString());

        if (clientInfo.IsAuthed)
        {
            c.Add("AuthedMachineMsid", clientInfo.AuthedMachine!.MSID);
            c.Add("AuthedMachineGroupName", clientInfo.MachineGroupName);
        }

        // 追加情報
        var a = result.AdditionalInfo;

        a.Add("FunctionName", this.FunctionName);
    }

    // OK WPC 応答の生成
    public WpcResult NewWpcResult(Pack? pack = null)
    {
        WpcResult ret = new WpcResult(pack);

        SetWpcResultAdditionalInfo(ret);

        return ret;
    }

    // 通常エラー WPC 応答の生成
    public WpcResult NewWpcResult(VpnError errorCode, Pack? pack = null, string? additionalErrorStr = null, [CallerFilePath] string filename = "", [CallerLineNumber] int line = 0, [CallerMemberName] string? caller = null)
    {
        WpcResult ret = new WpcResult(errorCode, pack, additionalErrorStr, filename, line, caller);

        SetWpcResultAdditionalInfo(ret);

        return ret;
    }

    // 例外エラー WPC 応答の生成
    public WpcResult NewWpcResult(Exception ex, Pack? pack = null)
    {
        WpcResult ret = new WpcResult(ex, pack);

        SetWpcResultAdditionalInfo(ret);

        return ret;
    }

    // 解放系
    public void Dispose() { this.Dispose(true); GC.SuppressFinalize(this); }
    Once DisposeFlag;
    public async ValueTask DisposeAsync()
    {
        if (DisposeFlag.IsFirstCall() == false) return;
        await DisposeInternalAsync();
    }
    protected virtual void Dispose(bool disposing)
    {
        if (!disposing || DisposeFlag.IsFirstCall() == false) return;
        DisposeInternalAsync()._GetResult();
    }
    Task DisposeInternalAsync()
    {
        // Here
        return TR();
    }
}

public class ThinControllerWebSocketCertData
{
    public string DomainName = "";
    public List<byte[]> CertsList = new List<byte[]>();
    public byte[] Key = new byte[0];
    public DateTimeOffset TimeStamp;
}

public class ThinControllerWebSocketCertMaintainer : AsyncServiceWithMainLoop
{
    public DirectoryPath WebSocketCertCacheDir { get; }

    public ThinController Controller { get; }

    readonly CriticalSection<ThinControllerWebSocketCertMaintainer> FileLock = new CriticalSection<ThinControllerWebSocketCertMaintainer>();

    public ThinControllerWebSocketCertMaintainer(ThinController controller)
    {
        try
        {
            this.Controller = controller;
            this.WebSocketCertCacheDir = PP.Combine(Env.AppLocalDir, "Config", "WebSocketCertCache");

            try
            {
                this.WebSocketCertCacheDir.CreateDirectory();
            }
            catch { }

            this.StartMainLoop(this.MainLoopAsync);
        }
        catch (Exception ex)
        {
            this._DisposeSafe(ex);
            throw;
        }
    }

    readonly FastSingleCache<ThinControllerWebSocketCertData?> CertDataCache = new FastSingleCache<ThinControllerWebSocketCertData?>();

    public ThinControllerWebSocketCertData? GetCertData()
    {
        return this.CertDataCache.GetOrCreate(() => GetCertDataCore());
    }

    ThinControllerWebSocketCertData? GetCertDataCore()
    {
        ThinControllerWebSocketCertData ret = new ThinControllerWebSocketCertData();

        try
        {
            Memory<byte> certsData = default;
            Memory<byte> certsKey = default;

            lock (FileLock)
            {
                certsData = this.WebSocketCertCacheDir.Combine("cert.cer").ReadDataFromFile();
                certsKey = this.WebSocketCertCacheDir.Combine("cert.key").ReadDataFromFile();

                string localTimestampBody = "";
                try
                {
                    localTimestampBody = this.WebSocketCertCacheDir.Combine("timestamp.txt").ReadStringFromFile()._GetFirstFilledLineFromLines();

                    if (localTimestampBody._IsFilled())
                    {
                        ret.TimeStamp = Str.StrToDateTime(localTimestampBody, emptyToZeroDateTime: true);
                    }
                }
                catch
                {
                }
            }

            var cs = new CertificateStore(certsData.Span, certsKey.Span);

            cs.PrimaryContainer.CertificateList.ForEach(x => ret.CertsList.Add(x.Export().ToArray()));
            ret.Key = cs.PrimaryPrivateKey.Export().ToArray();

            ret.DomainName = Controller.Db.GetVarString("ThinWebClient_WebSocketWildCardDomainName", ThinControllerConsts.Default_ThinWebClient_WebSocketWildCardDomainName)._NormalizeFqdn();

            return ret;
        }
        catch (Exception ex)
        {
            ex._Debug();
            return null;
        }
    }

    async Task MainLoopAsync(CancellationToken cancel)
    {
        int numFailed = 0;

        while (cancel.IsCancellationRequested == false)
        {
            int nextWaitInterval;

            if (Controller.Db.IsLoaded == false)
            {
                nextWaitInterval = 100;
            }
            else
            {
                try
                {
                    await UpdateCoreAsync(cancel);
                    numFailed = 0;
                }
                catch (Exception ex)
                {
                    ex._Debug();
                    numFailed++;
                }

                nextWaitInterval = Util.GenRandInterval(ThinControllerConsts.ThinWebClient_WebSocketCertMaintainer_Interval_Normal_Msecs);
                if (numFailed >= 1)
                {
                    nextWaitInterval = Util.GenRandIntervalWithRetry(ThinControllerConsts.ThinWebClient_WebSocketCertMaintainer_Interval_Retry_Initial_Msecs,
                        numFailed,
                        ThinControllerConsts.ThinWebClient_WebSocketCertMaintainer_Interval_Retry_Max_Msecs);
                }
            }

            if (cancel.IsCancellationRequested) break;

            await cancel._WaitUntilCanceledAsync(nextWaitInterval);
        }
    }

    async Task UpdateCoreAsync(CancellationToken cancel)
    {
        string certUrl = Controller.Db.GetVarString("ThinWebClient_WebSocketWildCardCertServerLatestUrl", ThinControllerConsts.Default_ThinWebClient_WebSocketWildCardCertServerLatestUrl);
        string certUsername = Controller.Db.GetVarString("ThinWebClient_WebSocketWildCardCertServerLatestUrl_Username", ThinControllerConsts.Default_ThinWebClient_WebSocketWildCardCertServerLatestUrl_Username);
        string certPassword = Controller.Db.GetVarString("ThinWebClient_WebSocketWildCardCertServerLatestUrl_Password", ThinControllerConsts.Default_ThinWebClient_WebSocketWildCardCertServerLatestUrl_Password);

        if (certUrl._IsFilled())
        {
            await using var http = new WebApi(new WebApiOptions(new WebApiSettings { AllowAutoRedirect = true, SslAcceptAnyCerts = true, Timeout = 15 * 1000 }, doNotUseTcpStack: true));

            if (certUsername._IsFilled() && certPassword._IsFilled())
            {
                http.SetBasicAuthHeader(certUsername, certPassword);
            }

            string newTimestampBody = (await http.SimpleQueryAsync(WebMethods.GET, certUrl._CombineUrl("timestamp.txt").ToString())).ToString()._GetFirstFilledLineFromLines();

            if (newTimestampBody._IsEmpty()) throw new CoresLibException("timestampBody is empty.");

            var newCertBody = (await http.SimpleQueryAsync(WebMethods.GET, certUrl._CombineUrl("cert.cer").ToString())).Data;

            var newKeyBody = (await http.SimpleQueryAsync(WebMethods.GET, certUrl._CombineUrl("cert.key").ToString())).Data;

            // 証明書のパースを試行
            var newCertStore = new CertificateStore(newCertBody, newKeyBody);

            // 現在保存されているキャッシュのタイムスタンプと比較
            string localTimestampBody = "";
            try
            {
                localTimestampBody = this.WebSocketCertCacheDir.Combine("timestamp.txt").ReadStringFromFile()._GetFirstFilledLineFromLines();
            }
            catch
            {
            }

            DateTimeOffset localTimestampDt = Str.StrToDateTime(localTimestampBody, emptyToZeroDateTime: true);
            DateTimeOffset newTimestampDt = Str.StrToDateTime(newTimestampBody, emptyToZeroDateTime: true);

            if (newTimestampDt > localTimestampDt)
            {
                // 新しく更新されているので証明書をローカルに保存する
                lock (FileLock)
                {
                    this.WebSocketCertCacheDir.Combine("cert.cer").WriteDataToFile(newCertBody, FileFlags.AutoCreateDirectory);
                    this.WebSocketCertCacheDir.Combine("cert.key").WriteDataToFile(newKeyBody, FileFlags.AutoCreateDirectory);
                    this.WebSocketCertCacheDir.Combine("timestamp.txt").WriteStringToFile(newTimestampBody + Str.NewLine_Str_Local, FileFlags.AutoCreateDirectory);
                }

                this.CertDataCache.Clear();
            }
        }
    }

    protected override async Task CleanupImplAsync(Exception? ex)
    {
        try
        {
        }
        finally
        {
            await base.CleanupImplAsync(ex);
        }
    }
}


public class ThinController : AsyncService, IThinControllerRpcApi
{

    // Hive
    readonly HiveData<ThinControllerSettings> SettingsHive;

    // 設定へのアクセスを容易にするための自動プロパティ
    CriticalSection ManagedSettingsLock => SettingsHive.DataLock;
    ThinControllerSettings ManagedSettings => SettingsHive.ManagedData;

    public ThinControllerSettings SettingsFastSnapshot => SettingsHive.CachedFastSnapshot;

    public DnsResolver DnsResolver { get; }

    public DateTimeOffset BootDateTime { get; } = DtOffsetNow;

    public long BootTick { get; } = TickNow;

    public ThinControllerHookBase Hook { get; }

    // データベース
    public ThinDatabase Db { get; }
    public ThinSessionManager SessionManager { get; }
    public ThinControllerWebSocketCertMaintainer WebSocketCertMaintainer { get; }

    readonly Task RecordStatTask;

    public StatMan? StatMan { get; }

    public JsonRpcHttpServer PaidServiceRpcServer { get; }

    public readonly ThroughputMeasuse Throughput_ClientGetWolMacList = new ThroughputMeasuse();
    public readonly ThroughputMeasuse Throughput_ClientConnect = new ThroughputMeasuse();
    public readonly ThroughputMeasuse Throughput_RenameMachine = new ThroughputMeasuse();
    public readonly ThroughputMeasuse Throughput_RegistMachine = new ThroughputMeasuse();
    public readonly ThroughputMeasuse Throughput_SendOtpEmail = new ThroughputMeasuse();
    public readonly ThroughputMeasuse Throughput_ServerConnect = new ThroughputMeasuse();
    public readonly ThroughputMeasuse Throughput_ReportSessionList = new ThroughputMeasuse();
    public readonly ThroughputMeasuse Throughput_ReportSessionAdd = new ThroughputMeasuse();
    public readonly ThroughputMeasuse Throughput_ReportSessionDel = new ThroughputMeasuse();
    public readonly ThroughputMeasuse Throughput_DatabaseRead = new ThroughputMeasuse();
    public readonly ThroughputMeasuse Throughput_DatabaseWrite = new ThroughputMeasuse();
    public readonly ThroughputMeasuse Throughput_Request_NonProxy = new ThroughputMeasuse();
    public readonly ThroughputMeasuse Throughput_Request_Proxy = new ThroughputMeasuse();

    public RateLimiter<string> HeavyRequestRateLimiter { get; } = new RateLimiter<string>(new RateLimiterOptions(burst: 50, limitPerSecond: 5, mode: RateLimiterMode.NoPenalty));

    public ThinController(ThinControllerSettings settings, ThinControllerHookBase hook, Func<ThinControllerSettings>? getDefaultSettings = null)
    {
        try
        {
            this.Hook = hook;

            this.SettingsHive = new HiveData<ThinControllerSettings>(
                Hive.SharedLocalConfigHive,
                "ThinController",
                getDefaultSettings,
                HiveSyncPolicy.AutoReadWriteFile,
                HiveSerializerSelection.RichJson);

            this.DnsResolver = new DnsClientLibBasedDnsResolver(new DnsResolverSettings());

            this.Db = new ThinDatabase(this);
            this.SessionManager = new ThinSessionManager();

            this.RecordStatTask = RecordStatAsync()._LeakCheck();

            this.StatMan = new StatMan(new StatManConfig
            {
                SystemName = "thincontroller",
                LogName = "thincontroller_stat",
                Callback = async (p, nums, strs) =>
                {
                    await TaskCompleted;

                    var stat = this.GenerateStat();
                    if (stat != null)
                    {
                        nums.Add("Stat_CurrentRelayGates", stat.Stat_CurrentRelayGates);
                        nums.Add("Stat_CurrentUserSessionsServer", stat.Stat_CurrentUserSessionsServer);
                        nums.Add("Stat_CurrentUserSessionsClient1", stat.Stat_CurrentUserSessionsClient1);
                        nums.Add("Stat_CurrentUserSessionsClient2", stat.Stat_CurrentUserSessionsClient2);
                        nums.Add("Stat_CurrentUserSessionsClient3_WebSocket", stat.Stat_CurrentUserSessionsClient3_WebSocket);
                        nums.Add("Stat_TotalServers", stat.Stat_TotalServers);
                        nums.Add("Stat_ActiveServers_Day01", stat.Stat_ActiveServers_Day01);
                        nums.Add("Stat_ActiveServers_Day03", stat.Stat_ActiveServers_Day03);
                        nums.Add("Stat_ActiveServers_Day07", stat.Stat_ActiveServers_Day07);
                        nums.Add("Stat_ActiveServers_Day30", stat.Stat_ActiveServers_Day30);
                        nums.Add("Stat_TodaysNewServers", stat.Stat_TodaysNewServers);
                        nums.Add("Stat_YestardaysNewServers", stat.Stat_YestardaysNewServers);
                        nums.Add("Stat_TotalServerConnectRequests", (long)(stat.Stat_TotalServerConnectRequestsKilo * 1000.0));
                        nums.Add("Stat_TotalClientConnectRequests", (long)(stat.Stat_TotalClientConnectRequestsKilo * 1000.0));

                        nums.Add("Sys_DotNet_NumRunningTasks", stat.Sys_DotNet_NumRunningTasks);
                        nums.Add("Sys_DotNet_NumDelayedTasks", stat.Sys_DotNet_NumDelayedTasks);
                        nums.Add("Sys_DotNet_NumTimerTasks", stat.Sys_DotNet_NumTimerTasks);
                        nums.Add("Sys_DotNet_NumObjects", stat.Sys_DotNet_NumObjects);
                        nums.Add("Sys_DotNet_CpuUsage", stat.Sys_DotNet_CpuUsage);
                        nums.Add("Sys_DotNet_ManagedMemory", (long)(stat.Sys_DotNet_ManagedMemory_MBytes * 1024.0 * 1024.0));
                        nums.Add("Sys_DotNet_ProcessMemory", (long)(stat.Sys_DotNet_ProcessMemory_MBytes * 1024.0 * 1024.0));
                        nums.Add("Sys_DotNet_NumNativeThreads", stat.Sys_DotNet_NumNativeThreads);
                        nums.Add("Sys_DotNet_NumNativeHandles", stat.Sys_DotNet_NumNativeHandles);
                        nums.Add("Sys_DotNet_GcTotal", stat.Sys_DotNet_GcTotal);
                        nums.Add("Sys_DotNet_Gc0", stat.Sys_DotNet_Gc0);
                        nums.Add("Sys_DotNet_Gc1", stat.Sys_DotNet_Gc1);
                        nums.Add("Sys_DotNet_Gc2", stat.Sys_DotNet_Gc2);

                        nums.Add("Sys_Thin_BootSeconds", (long)(stat.Sys_Thin_BootDays * 60.0 * 60.0 * 24.0));
                        nums.Add("Sys_Thin_ConcurrentRequests", stat.Sys_Thin_ConcurrentRequests);
                        nums.Add("Sys_Thin_LastDbReadTookMsecs", stat.Sys_Thin_LastDbReadTookMsecs);
                        nums.Add("Sys_Thin_IsDatabaseConnected", stat.Sys_Thin_IsDatabaseConnected);

                        nums.Add("Sys_Thin_WebSocketCertTimestampDateYymmdd", stat.Sys_Thin_WebSocketCertTimestampDateYymmdd);
                        nums.Add("Sys_Thin_WebSocketCertTimestampTimeHHmmss", stat.Sys_Thin_WebSocketCertTimestampTimeHHmmss);
                    }
                }
            });

            StatMan?.AddReport("BootCount_Total", 1);

            this.WebSocketCertMaintainer = new ThinControllerWebSocketCertMaintainer(this);

            // 商用サービス用 RPC エンドポイントを Web サーバーに登録
            this.PaidServiceRpcServer = new JsonRpcHttpServer(new JsonRpcServerApi(targetObject: this),
                new JsonRpcServerConfig
                {
                    EnableBuiltinRichWebPages = true,
                }
                );
        }
        catch (Exception ex)
        {
            this._DisposeSafe(ex);
            throw;
        }
    }

    public class HandleWpcParam
    {
        public ThinControllerServiceType ServiceType;
    }

    readonly RefInt CurrentConcurrentProcess = new RefInt();

    // 統計記録用タスク
    async Task RecordStatAsync()
    {
        await Task.Yield();

        CancellationToken cancel = this.GrandCancel;

        long lastTick = 0;

        int nextInterval = Util.GenRandInterval(this.CurrentValue_ControllerRecordStatIntervalMsecs);

        while (cancel.IsCancellationRequested == false)
        {
            long now = TickNow;

            if (lastTick == 0 || now >= (lastTick + nextInterval))
            {
                lastTick = now;
                nextInterval = Util.GenRandInterval(this.CurrentValue_ControllerRecordStatIntervalMsecs);

                try
                {
                    await RecordStatCoreAsync(cancel);
                }
                catch (Exception ex)
                {
                    ex._Error();
                }
            }

            await cancel._WaitUntilCanceledAsync(Util.GenRandInterval(1000));
        }
    }

    async Task RecordStatCoreAsync(CancellationToken cancel)
    {
        var stat = this.GenerateStat();
        if (stat == null)
        {
            return;
        }

        var now = DtNow;

        // ファイルに記録
        stat._PostData("ThinControllerStat", noWait: true);

        // DB にポスト
        this.Db.EnqueueUpdateJob(async (db, cancel) =>
        {
            await db.QueryWithNoReturnAsync("INSERT INTO STAT (STAT_DATE, STAT_NUM_GATES, STAT_NUM_SERVERS, STAT_NUM_CLIENTS, STAT_NUM_UNIQUE_CLIENTS, STAT_HOSTNAME, STAT_JSON) VALUES (@,@,@,@,@,@,@)",
                            now,
                            stat.Stat_CurrentRelayGates,
                            stat.Stat_CurrentUserSessionsServer,
                            stat.Stat_CurrentUserSessionsClient1,
                            stat.Stat_CurrentUserSessionsClient2,
                            Env.MachineName.ToLowerInvariant(),
                            stat._ObjectToJson(compact: true)
                            );
        });

        await TaskCompleted;
    }

    // 管理用ビューを取得
    public ThinAdminView GetAdminView()
    {
        SessionManager.DeleteOldGate(DtNow);

        ThinAdminView ret = new ThinAdminView();

        ret.BootDateTime = this.BootDateTime;

        ret.GatesList = this.SessionManager.GateTable.Values.OrderBy(x => x.IpAddress, StrComparer.IpAddressStrComparer).ThenBy(x => x.GateId);

        ret.VarsList = (this.Db.MemDb?.VarList.OrderBy(x => x.VAR_NAME).ThenBy(x => x.VAR_ID) ?? null)?.ToList()._CloneWithJson() ?? null;

        ret.VarsList?._DoForEach(x =>
        {
            if (x.VAR_NAME._InStr("password", true) || x.VAR_NAME._InStr("secret", true))
            {
                x.VAR_VALUE1 = x.VAR_VALUE1._MaskPassword();
            }
        });

        ret.MachinesList = this.Db.MemDb?.MachineList.OrderBy(x => x.CREATE_DATE) ?? null;

        return ret;
    }

    async Task<HttpResult?> SpecialHandlingAsync(HttpEasyContextBox box, ThinControllerSessionClientInfo clientInfo, CancellationToken cancel)
    {
        string urlPath = box.PathAndQueryStringUri.AbsolutePath._NonNullTrim();

        // ステータステキスト応答モード (例: /thincontrol/api/stat/)
        if (urlPath._InStr("api/", true) && urlPath._InStr("/stat", true))
        {
            if (Db.IsLoaded == false)
            {
                return new HttpErrorResult(Consts.HttpStatusCodes.InternalServerError, $"The database is not loaded yet.");
            }
            else
            {
                string password = box.QueryStringList._GetStrFirst("password");
                string password2 = Db.GetVarString("QueryPassword_Stat")._NonNullTrim();
                if (password2._IsEmpty() || password2 == password)
                {
                    var stat = this.GenerateStat(false, clientInfo.ConsumingConcurrentProcessCount)!;
                    string body;
                    string mime;

                    if (urlPath._InStr("json", true) == false)
                    {
                        // 通常モード (例: /thincontrol/api/stat/)
                        var rw = FieldReaderWriter.GetCached<ThinControllerStat>();

                        StringWriter w = new StringWriter();

                        w.WriteLine($"# {DtNow._ToDtStr()}");

                        foreach (string name in rw.OrderedPublicFieldOrPropertyNamesList)
                        {
                            object? obj = rw.GetValue(stat, name);
                            string objStr;
                            if (obj is double d)
                            {
                                objStr = d.ToString("F3");
                            }
                            else
                            {
                                objStr = obj?.ToString() ?? "";
                            }
                            if (objStr._IsEmpty())
                            {
                                objStr = "0";
                            }

                            w.WriteLine($"{name}: {objStr}");
                        }

                        body = w.ToString();
                        mime = Consts.MimeTypes.Text;
                    }
                    else
                    {
                        // JSON モード (例: /thincontrol/api/statjson/)
                        body = stat._ObjectToJson();
                        mime = Consts.MimeTypes.Json;
                    }

                    await TaskCompleted;

                    return new HttpStringResult(body, mime);
                }
                else
                {
                    return new HttpErrorResult(Consts.HttpStatusCodes.Forbidden, $"Invalid query password.");
                }
            }
        }

        // 中継ゲートウェイ列挙モード (例: /thincontrol/api/gates/)
        if (urlPath._InStr("api/", true) && urlPath._InStr("/gates", true))
        {
            string password = box.QueryStringList._GetStrFirst("password");
            string password2 = Db.GetVarString("QueryPassword_Gates")._NonNullTrim();
            if (password2._IsEmpty() || password2 == password)
            {
                var gates = SessionManager.GateTable.Values.OrderBy(x => x.IpAddress, StrComparer.IpAddressStrComparer).ThenBy(x => x.GateId, StrComparer.IgnoreCaseComparer);

                return new HttpStringResult(gates._ObjectToJson(), Consts.MimeTypes.Json);
            }
            else
            {
                return new HttpErrorResult(Consts.HttpStatusCodes.Forbidden, $"Invalid query password.");
            }
        }

        // セッション列挙モード (例: /thincontrol/api/sessions/)
        if (urlPath._InStr("api/", true) && urlPath._InStr("/sessions", true))
        {
            string password = box.QueryStringList._GetStrFirst("password");
            string password2 = Db.GetVarString("QueryPassword_Sessions")._NonNullTrim();
            if (password2._IsEmpty() || password2 == password)
            {
                var result = new Dictionary<string, List<ThinSession>>();

                var gateTable = SessionManager.GateTable.Values;

                foreach (var gate in gateTable)
                {
                    var list = result._GetOrNew(gate.GateId, () => new List<ThinSession>());

                    gate.SessionTable.Values.OrderBy(x => x.SessionId).ThenBy(x => x.EstablishedDateTime)._DoForEach(x => list.Add(x));
                }

                return new HttpStringResult(result._ObjectToJson(), Consts.MimeTypes.Json);
            }
            else
            {
                return new HttpErrorResult(Consts.HttpStatusCodes.Forbidden, $"Invalid query password.");
            }
        }

        // ホスト情報モード
        if (urlPath._InStr("api/", true) && urlPath._InStr("/info", true))
        {
            StringWriter w = new StringWriter();

            w.WriteLine($"Current time: {DtOffsetNow.ToString()}");
            w.WriteLine($"Hostname: {Env.MachineName}");
            w.WriteLine($"Server Endpoint: {box.LocalEndpoint.ToString()}");
            w.WriteLine($"Client Endpoint: {box.RemoteEndpoint.ToString()}");

            return new HttpStringResult(w.ToString());
        }

        return null;
    }

    public async Task<HttpResult> HandleWpcAsync(HttpEasyContextBox box, object? param2)
    {
        CancellationToken cancel = box.CancelService; // 2025.4.4 コネクション単位の Cancel 指令は、下位のサブルーチンに渡さないようにした。サービスそのものの停止指令のみ伝達するようにした。Sql 系の関数の Cancel の実装がおかしく、Kestrel で TCP レイヤで切断通知がなされた場合に何らかのフリーズが発生する可能性があるためである。
        HandleWpcParam param = (HandleWpcParam)param2!;

        // エンドユーザー用サービスポイントの場合、最大同時処理リクエスト数を制限する
        bool limitMaxConcurrentProcess = param.ServiceType == ThinControllerServiceType.ApiServiceForUsers;

        if (limitMaxConcurrentProcess)
        {
            // カウント加算
            int cur = this.CurrentConcurrentProcess.Increment();
            int maxValue = this.CurrentValue_ControllerMaxConcurrentWpcRequestProcessingForUsers;
            if (cur > maxValue)
            {
                // 最大数超過
                this.CurrentConcurrentProcess.Decrement();
                return new HttpErrorResult(Consts.HttpStatusCodes.TooManyRequests, $"Too many WPC concurrent requests ({cur} > {maxValue})");
            }
        }

        try
        {
            // クライアント情報
            ThinControllerSessionClientInfo clientInfo = new ThinControllerSessionClientInfo(box, param.ServiceType, limitMaxConcurrentProcess ? 1 : 0);

            if (clientInfo.IsProxyMode)
            {
                // "X-WG-Proxy-SrcIP" HTTP ヘッダの値は、"AllowedProxyIpAcl" 設定変数に記載されている IP アドレスからのみ受付ける
                var proxyAclStr = Db.GetVarString("AllowedProxyIpAcl")._NonNullTrim();
                if (EasyIpAcl.Evaluate(proxyAclStr, clientInfo.ClientPhysicalIp, enableCache: true) == EasyIpAclAction.Deny)
                {
                    // 不正プロキシ
                    string errStr = $"Error: The client IP address '{clientInfo.ClientPhysicalIp}' is not allowed as the proxy server. Check the 'AllowedProxyIpAcl' variable.";
                    errStr._Error();
                    return new HttpErrorResult(Consts.HttpStatusCodes.Forbidden, errStr);
                }
            }

            // 特別ハンドリングの試行
            var specialResult = await SpecialHandlingAsync(box, clientInfo, cancel);
            if (specialResult != null)
            {
                return specialResult;
            }

            if (clientInfo.IsProxyMode == false && param.ServiceType == ThinControllerServiceType.ApiServiceForUsers)
            {
                // 非プロキシのユーザーリクエスト
                this.Throughput_Request_NonProxy.Add(1);
            }
            else if (clientInfo.IsProxyMode)
            {
                // プロキシ経由のユーザーリクエスト
                this.Throughput_Request_Proxy.Add(1);
            }

            // WPC リクエスト文字列の受信
            string requestWpcString = "";
            if (box.Method == WebMethods.POST)
            {
                requestWpcString = await box.Request._RecvStringContentsAsync((int)Pack.MaxPackSize * 2, cancel: box.CancelPerRequest);
            }

            if (requestWpcString._IsFilled())
            {
                // WPC 処理のためのセッションの作成
                var session = new ThinControllerSession(this, clientInfo);

                // WPC 処理の実施
                string responseWpcString = await session.ProcessWpcRequestAsync(requestWpcString, cancel);

                // WPC 結果の応答
                return new HttpStringResult(responseWpcString);
            }
            else
            {
                // プロトコルエラー (リクエストがない)
                WpcResult result = new WpcResult(VpnError.ERR_PROTOCOL_ERROR);
                return new HttpStringResult(result.ToWpcPack().ToPacketString());
            }

        }
        catch (Exception ex)
        {
            // WPC 処理中にエラー発生
            return new HttpErrorResult(Consts.HttpStatusCodes.InternalServerError, $"Internal Server Error: {ex.Message}");
        }
        finally
        {
            // カウント減算
            if (limitMaxConcurrentProcess)
            {
                this.CurrentConcurrentProcess.Decrement();
            }
        }
    }

    public void Configure(IApplicationBuilder app, IWebHostEnvironment env, ThinControllerServiceType serviceType)
    {
        app.Use(async (context, next) =>
        {
            if (serviceType == ThinControllerServiceType.ApiServiceForGateway)
            {
                // Gateway 宛のポートに通信が来たら IP ACL を確認する
                var addr = context.Connection.RemoteIpAddress!._UnmapIPv4();
                if (EasyIpAcl.Evaluate(this.Db.GetVarString("GatewayServicePortIpAcl"), addr) == EasyIpAclAction.Deny)
                {
                    await using var errResult = new HttpErrorResult(Consts.HttpStatusCodes.Forbidden, $"Your IP address '{addr}' is not allowed to access to this endpoint.");

                    await context.Response._SendHttpResultAsync(errResult, context._GetRequestCancellationToken());

                    return;
                }
            }

            string path = context.Request.Path.Value._NonNullTrim();

            if (this.SettingsFastSnapshot.WpcPathList.Where(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase)).Any())
            {
                HandleWpcParam param = new HandleWpcParam
                {
                    ServiceType = serviceType,
                };

                await HttpResult.EasyRequestHandler(context, param, HandleWpcAsync, this.GrandCancel);

                return;
            }

            await next();
        });

        // 商用サービス用 RPC エンドポイントを Web サーバーに登録
        this.PaidServiceRpcServer.RegisterRoutesToHttpServer(app);
    }

    protected override async Task CleanupImplAsync(Exception? ex)
    {
        try
        {
            await this.WebSocketCertMaintainer._DisposeSafeAsync();

            await this.StatMan._DisposeSafeAsync();

            await this.Db._DisposeSafeAsync();

            await this.DnsResolver._DisposeSafeAsync();

            await this.RecordStatTask._TryAwait(true);

            await this.PaidServiceRpcServer.Api._DisposeSafeAsync();

            this.SettingsHive._DisposeSafe();
        }
        finally
        {
            await base.CleanupImplAsync(ex);
        }
    }

    // 設定値プロパティ集
    // ここで、this.Db はまだ null である可能性があるため、? を付けること
    public int CurrentValue_ControllerMaxConcurrentWpcRequestProcessingForUsers
        => (this.Db?.MemDb?.ControllerMaxConcurrentWpcRequestProcessingForUsers)._ZeroToDefault(ThinControllerConsts.Default_ControllerMaxConcurrentWpcRequestProcessingForUsers);

    public int CurrentValue_ControllerDbFullReloadIntervalMsecs
        => (this.Db?.MemDb?.ControllerDbFullReloadIntervalMsecs)._ZeroToDefault(ThinControllerConsts.Default_ControllerDbFullReloadIntervalMsecs, max: ThinControllerConsts.Max_ControllerDbReadFullReloadIntervalMsecs);

    public int CurrentValue_ControllerDbWriteUpdateIntervalMsecs
        => (this.Db?.MemDb?.ControllerDbWriteUpdateIntervalMsecs)._ZeroToDefault(ThinControllerConsts.Default_ControllerDbWriteUpdateIntervalMsecs, max: ThinControllerConsts.Max_ControllerDbWriteUpdateIntervalMsecs);

    public int CurrentValue_ControllerDbBackupFileWriteIntervalMsecs
        => (this.Db?.MemDb?.ControllerDbBackupFileWriteIntervalMsecs)._ZeroToDefault(ThinControllerConsts.Default_ControllerDbBackupFileWriteIntervalMsecs, max: ThinControllerConsts.Max_ControllerDbBackupFileWriteIntervalMsecs);

    public int CurrentValue_ControllerRecordStatIntervalMsecs
        => (this.Db?.MemDb?.ControllerRecordStatIntervalMsecs)._ZeroToDefault(ThinControllerConsts.Default_ControllerRecordStatIntervalMsecs, max: ThinControllerConsts.Max_ControllerRecordStatIntervalMsecs);

    // ユーティリティ関数系

    // Web に表示可能なステータス情報の取得
    public IEnumerable<Pair2<string, IEnumerable<StrKeyValueItem>>> GetAdminStatus(bool refresh = false)
    {
        List<Pair2<string, IEnumerable<StrKeyValueItem>>> ret = new List<Pair2<string, IEnumerable<StrKeyValueItem>>>();

        var stat = GenerateStat(refresh);
        if (stat != null)
        {
            // 統計データの生成
            List<StrKeyValueItem> list = new List<StrKeyValueItem>();
            var rw = FieldReaderWriter.GetCached<ThinControllerStat>();
            foreach (string name in rw.OrderedPublicFieldOrPropertyNamesList)
            {
                object? obj = rw.GetValue(stat, name);
                string str = (rw.GetValue(stat, name)?.ToString())._NonNullTrim();
                if (obj is double d) str = d.ToString("F3");
                list.Add(new StrKeyValueItem(name, str));
            }

            ret.Add(new Pair2<string, IEnumerable<StrKeyValueItem>>("ThinController System Status", list));
        }

        var env = new EnvInfoSnapshot();
        if (env != null)
        {
            // 環境データの生成
            List<StrKeyValueItem> list = new List<StrKeyValueItem>();
            var rw = FieldReaderWriter.GetCached<EnvInfoSnapshot>();
            foreach (string name in rw.OrderedPublicFieldOrPropertyNamesList)
            {
                string str = (rw.GetValue(env, name)?.ToString())._NonNullTrim();
                list.Add(new StrKeyValueItem(name, str));
            }

            ret.Add(new Pair2<string, IEnumerable<StrKeyValueItem>>(".NET Runtime Environment Information", list));
        }

        return ret;
    }

    // 統計データの作成
    readonly FastSingleCache<ThinControllerStat> StatCache = new FastSingleCache<ThinControllerStat>(ThinControllerConsts.ControllerStatCacheExpiresMsecs, 0, CacheType.DoNotUpdateExpiresWhenAccess);
    public ThinControllerStat? GenerateStat(bool refresh = false, int subtractConcurrentProcessCount = 0)
    {
        if (Db.MemDb == null) return null;
        if (refresh) StatCache.Clear();
        return StatCache.GetOrCreate(() => GenerateStatCore(subtractConcurrentProcessCount));
    }


    ThinControllerStat? GenerateStatCore(int subtractConcurrentProcessCount = 0)
    {
        var now = DtNow;
        var days1 = now.AddDays(-1);
        var days3 = now.AddDays(-3);
        var days7 = now.AddDays(-7);
        var days30 = now.AddDays(-30);

        var today = now.Date;
        var yesterday = now.Date.AddDays(-1);

        var mem = Db.MemDb;

        if (mem == null)
        {
            return null;
        }

        CoresRuntimeStat sys = new CoresRuntimeStat();

        bool forceGc = false;

        var startupParams = new OneLineParams(Consts.DaemonArgKeys.ForceGc);

        if (startupParams._HasKey("ForceGc"))
        {
            forceGc = true;
        }

        sys.Refresh(forceGc: forceGc);

        long processMemory = 0;
        int numThreads = 0;
        int numHandles = 0;
        try
        {
            var proc = Process.GetCurrentProcess();
            processMemory = proc.PrivateMemorySize64;
            numThreads = proc.Threads.Count;
            numHandles = proc.HandleCount;
        }
        catch
        {
        }

        ThinControllerWebSocketCertData? certData = this.WebSocketCertMaintainer.GetCertData();

        ThinControllerStat ret = new ThinControllerStat
        {
            Stat_CurrentRelayGates = SessionManager.GateTable.Count,
            Stat_CurrentUserSessionsServer = SessionManager.GateTable.Values.Sum(x => x.SessionTable.Count),
            Stat_CurrentUserSessionsClient1 = SessionManager.GateTable.Values.Sum(x => x.SessionTable.Values.Sum(s => s.NumClients)),
            Stat_CurrentUserSessionsClient2 = SessionManager.GateTable.Values.Sum(x => x.SessionTable.Values.Sum(s => s.NumClientsUnique)),
            Stat_CurrentUserSessionsClient3_WebSocket = SessionManager.GateTable.Values.Sum(x => x.SessionTable.Values.Sum(s => s.NumClientsWebSocket)),
            Stat_TotalServers = mem.MachineList.Count,
            Stat_ActiveServers_Day01 = mem.MachineList.Where(x => x.LAST_CLIENT_DATE >= days1).Count(),
            Stat_ActiveServers_Day03 = mem.MachineList.Where(x => x.LAST_CLIENT_DATE >= days3).Count(),
            Stat_ActiveServers_Day07 = mem.MachineList.Where(x => x.LAST_CLIENT_DATE >= days7).Count(),
            Stat_ActiveServers_Day30 = mem.MachineList.Where(x => x.LAST_CLIENT_DATE >= days30).Count(),
            Stat_TodaysNewServers = mem.MachineList.Where(x => x.CREATE_DATE >= today).Count(),
            Stat_YestardaysNewServers = mem.MachineList.Where(x => x.CREATE_DATE >= yesterday && x.CREATE_DATE < today).Count(),
            Stat_TotalServerConnectRequestsKilo = (double)mem.MachineList.Sum(x => (long)x.NUM_SERVER) / 1000.0,
            Stat_TotalClientConnectRequestsKilo = (double)mem.MachineList.Sum(x => (long)x.NUM_CLIENT) / 1000.0,

            Throughput_ClientGetWolMacList = this.Throughput_ClientGetWolMacList,
            Throughput_ClientConnect = this.Throughput_ClientConnect,
            Throughput_RenameMachine = this.Throughput_RenameMachine,
            Throughput_RegistMachine = this.Throughput_RegistMachine,
            Throughput_SendOtpEmail = this.Throughput_SendOtpEmail,
            Throughput_ServerConnect = this.Throughput_ServerConnect,
            Throughput_ReportSessionList = this.Throughput_ReportSessionList,
            Throughput_ReportSessionAdd = this.Throughput_ReportSessionAdd,
            Throughput_ReportSessionDel = this.Throughput_ReportSessionDel,
            Throughput_DatabaseRead = this.Throughput_DatabaseRead,
            Throughput_DatabaseWrite = this.Throughput_DatabaseWrite,
            Throughput_Request_NonProxy = this.Throughput_Request_NonProxy,
            Throughput_Request_Proxy = this.Throughput_Request_Proxy,

            Sys_DotNet_NumRunningTasks = sys.Task,
            Sys_DotNet_NumDelayedTasks = sys.D,
            Sys_DotNet_NumTimerTasks = sys.Q,
            Sys_DotNet_NumObjects = sys.Obj,
            Sys_DotNet_CpuUsage = sys.Cpu,
            Sys_DotNet_ManagedMemory_MBytes = (double)sys.Mem / 1024.0,
            Sys_DotNet_ProcessMemory_MBytes = (double)processMemory / 1024.0 / 1024.0,
            Sys_DotNet_NumNativeThreads = numThreads,
            Sys_DotNet_NumNativeHandles = numHandles,
            Sys_DotNet_GcTotal = sys.Gc,
            Sys_DotNet_Gc0 = sys.Gc0,
            Sys_DotNet_Gc1 = sys.Gc1,
            Sys_DotNet_Gc2 = sys.Gc2,

            Sys_Thin_BootDays = (double)(TickNow - this.BootTick) / (double)(24 * 60 * 60 * 1000),
            Sys_Thin_ConcurrentRequests = Math.Max(this.CurrentConcurrentProcess - subtractConcurrentProcessCount, 0),
            Sys_Thin_LastDbReadTookMsecs = this.Db.LastDbReadTookMsecs,
            Sys_Thin_IsDatabaseConnected = this.Db.IsDatabaseConnected ? 1 : 0,
            Sys_Thin_DbLazyUpdateQueueLength = this.Db.LazyUpdateJobQueueLength,

            Sys_Thin_WebSocketCertTimestampDateYymmdd = certData?.TimeStamp._ToYymmddInt() ?? 0,
            Sys_Thin_WebSocketCertTimestampTimeHHmmss = certData?.TimeStamp._ToHhmmssInt() ?? 0,
        };

        return ret;
    }

    // Gate の IP を指定することで、データベースに規程されている IP グループ名の適合一覧を取得
    public IEnumerable<string> GetGatePreferredIpRangeGroup(string gateIpAddress)
    {
        List<string> ret = new List<string>();

        ret.Add("Default");

        string tag = "PreferredGateIpRange_Group_";
        var varNames = Db.MemDb?.VarByName.Keys.Where(x => x.StartsWith(tag, StringComparison.OrdinalIgnoreCase)).OrderBy(x => x, StrComparer.IgnoreCaseComparer);

        if (varNames != null)
        {
            foreach (var name in varNames)
            {
                var aclString = Db.GetVarString(name)._NonNullTrim();

                var acl = EasyIpAcl.GetOrCreateCachedIpAcl(aclString);

                if (acl.Evaluate(gateIpAddress) == EasyIpAclAction.Permit)
                {
                    ret.Add(name._Slice(tag.Length));
                }
            }
        }

        return ret.Distinct(StrComparer.IgnoreCaseComparer);
    }
    // 最近発行した PCID 候補のキャッシュ
    readonly FastCache<string, int> RecentPcidCandidateCache = new FastCache<string, int>(10 * 60 * 1000, comparer: StrComparer.IgnoreCaseComparer);

    public void AddPcidToRecentPcidCandidateCache(string pcid)
    {
        RecentPcidCandidateCache.Add(pcid, 1);
    }

    // PCID の候補の決定
    public async Task<string> GeneratePcidCandidateAsync(string svcName, string dns1, string dns2, string username, string machineName, CancellationToken cancel)
    {
        string s = await GeneratePcidCandidateCoreAsync(svcName, dns1, dns2, username, machineName, cancel);

        RecentPcidCandidateCache.Add(s, 1);

        return s;
    }

    async Task<string> GeneratePcidCandidateCoreAsync(string svcName, string dns1, string dns2, string username, string machineName, CancellationToken cancel)
    {
        int i;
        if (dns1._IsSamei("localhost")) dns1 = "";
        if (dns2._IsSamei("localhost")) dns2 = "";
        if (machineName._IsSamei("localhost")) machineName = "";
        var domain1 = new Basic.Legacy.LegacyDomainUtil(dns1);
        var domain2 = new Basic.Legacy.LegacyDomainUtil(dns2);
        string d1 = ConvertStrToSafeForPcid(domain1.ProperString);
        string d2 = ConvertStrToSafeForPcid(domain2.ProperString);
        if (dns1._IsStrIP())
        {
            d1 = "";
        }
        if (dns2._IsStrIP())
        {
            d2 = "";
        }
        username = ConvertStrToSafeForPcid(username).Trim();
        i = machineName.IndexOf(".");
        if (i != -1)
        {
            machineName = machineName.Substring(0, i);
        }
        machineName = ConvertStrToSafeForPcid(machineName).Trim();
        if (machineName.Length > 8)
        {
            machineName = machineName.Substring(0, 8);
        }

        if (d1 != "")
        {
            return await GeneratePcidCandidateStringCoreAsync(svcName, d1, cancel);
        }

        if (Str.IsStrInList(username, "", "administrator", "system") == false)
        {
            return await GeneratePcidCandidateStringCoreAsync(svcName, username, cancel);
        }

        if (d2 != "")
        {
            return await GeneratePcidCandidateStringCoreAsync(svcName, d2, cancel);
        }

        if (machineName != "")
        {
            return await GeneratePcidCandidateStringCoreAsync(svcName, machineName, cancel);
        }

        string key = Str.ByteToHex(Secure.Rand(8));
        return await GeneratePcidCandidateStringCoreAsync(svcName, key, cancel);
    }

    // 候補名の生成
    async Task<string> GeneratePcidCandidateStringCoreAsync(string svcName, string key, CancellationToken cancel)
    {
        uint n = 0;
        while (true)
        {
            n++;

            string rand;

            if (n <= 100)
            {
                rand = Str.GenRandNumericPasswordWithBlocks(3, 2, '-');
            }
            else if (n <= 200)
            {
                rand = Str.GenRandNumericPasswordWithBlocks(4, 2, '-');
            }
            else if (n <= 1000)
            {
                rand = Str.GenRandNumericPasswordWithBlocks(5, 3, '-');
            }
            else
            {
                throw new CoresLibException("(n >= 1000)");
            }

            string pcidCandidate = GeneratePcidCandidateStringFromKeyAndNumber(key, rand);

            if (RecentPcidCandidateCache.ContainsKey(pcidCandidate) == false)
            {
                if (await this.Db.SearchMachineByPcidAsync(svcName, pcidCandidate, cancel) == null)
                {
                    return pcidCandidate;
                }
            }
        }
    }

    // PCID 候補生成
    public static string GeneratePcidCandidateStringFromKeyAndNumber(string key, string add)
    {
        key = key.ToLowerInvariant().Trim();
        string ret = key + "-" + add;

        ret._SliceHead(ThinControllerConsts.MaxPcidLen);

        return ret;
    }

    // PCID に使用できる文字列にコンバート
    public static string ConvertStrToSafeForPcid(string str)
    {
        string ret = "";

        foreach (char c in str)
        {
            if (IsSafeCharForPcid(c))
            {
                ret += c;
            }
        }

        if (ret.Length > 20)
        {
            ret = ret.Substring(0, 20);
        }

        return ret;
    }

    // PCID に使用できる文字かどうかチェック
    public static bool IsSafeCharForPcid(char c)
    {
        if ('a' <= c && c <= 'z')
        {
            return true;
        }
        else if ('A' <= c && c <= 'Z')
        {
            return true;
        }
        else if (c == '_' || c == '-')
        {
            return true;
        }
        else if ('0' <= c && c <= '9')
        {
            return true;
        }
        return false;
    }

    // PCID のチェック
    public static VpnError CheckPCID(string pcid)
    {
        int i, len;
        len = pcid.Length;
        if (len == 0)
        {
            return VpnError.ERR_PCID_NOT_SPECIFIED;
        }
        if (len > ThinControllerConsts.MaxPcidLen)
        {
            return VpnError.ERR_PCID_TOO_LONG;
        }
        for (i = 0; i < len; i++)
        {
            if (IsSafeCharForPcid(pcid[i]) == false)
            {
                return VpnError.ERR_PCID_INVALID;
            }
        }

        return VpnError.ERR_NO_ERROR;
    }

    // MSID の生成
    public static string GenerateMsid(string certhash, string svcName)
    {
        byte[] hex = certhash._GetHexBytes();
        if (hex.Length != 20)
        {
            throw new ArgumentException("certHash");
        }
        return "MSID-" + svcName + "-" + Str.ByteToHex(hex);
    }

    void RPC_PaidService_Auth()
    {
        JsonRpcServerApi.TryAuth((user, pass) =>
        {
            return user == ThinControllerGlobalSettings.PaidService_RpcAuthUsername && pass == ThinControllerGlobalSettings.PaidService_RpcAuthPassword;
        }, "PaidServiceApi");
    }

    public Task<string> Test(int i)
    {
        return TR($"Hello {i}");
    }

    public async Task<ThinControllerRpcServerObjectInfo?> ServerObject_Get(string uniqueId)
    {
        if (ThinControllerGlobalSettings.PaidService_Enabled == false) throw new NotImplementedException();
        RPC_PaidService_Auth();

        var ret = await this.Db.Paid_GetOrSetServerObjectInfoAsync(uniqueId);

        return ret;
    }

    public async Task<ThinControllerRpcServerObjectInfo?> ServerObject_Activate(string uniqueId, string tag)
    {
        if (ThinControllerGlobalSettings.PaidService_Enabled == false) throw new NotImplementedException();
        RPC_PaidService_Auth();

        tag = tag._NonNullTrim();

        var ret = await this.Db.Paid_GetOrSetServerObjectInfoAsync(uniqueId, true, tag);

        return ret;
    }

    public async Task<ThinControllerRpcServerObjectInfo?> ServerObject_Deactivate(string uniqueId, string tag)
    {
        if (ThinControllerGlobalSettings.PaidService_Enabled == false) throw new NotImplementedException();
        RPC_PaidService_Auth();

        tag = tag._NonNullTrim();

        var ret = await this.Db.Paid_GetOrSetServerObjectInfoAsync(uniqueId, false, tag);

        return ret;
    }

    public ThinControllerRpcServerObjectInfo Paid_CalcServerLicenseStatus(string pcid, string jsonAttributes, DateTimeOffset firstClientUseDate)
    {
        EasyJsonStrAttributes json = new EasyJsonStrAttributes(jsonAttributes);

        ThinControllerRpcServerObjectInfo ret = new ThinControllerRpcServerObjectInfo
        {
            Pcid = pcid.Trim().ToLowerInvariant(),
            IsCurrentActivated = json["Paid_IsCurrentActivated"]._ToBool(),
            FirstClientUseDateTime = firstClientUseDate,
            Tag = json["Paid_Tag"],
    };

        // 状態の判定
        if (ret.IsCurrentActivated)
        {
            // アクティベート済み
            ret.ActivatedOnceInPast = true;
            ret.ActivatedDateTime = Str.DtstrToDateTimeOffset(json["Paid_ActivatedDateTime"]);

            ret.Status = ThinControllerPaidServiceRedirectUrlStatusFlag.Activated;
        }
        else
        {
            // アクティベートまだ
            ret.ActivatedOnceInPast = json["Paid_ActivatedOnceInPast"]._ToBool();

            if (ret.ActivatedOnceInPast)
            {
                ret.Status = ThinControllerPaidServiceRedirectUrlStatusFlag.Deactivated;
                ret.DeactivatedDateTime = Str.DtstrToDateTimeOffset(json["Paid_DeactivatedDateTime"]);
            }
            else
            {
                ret.TrialExpireDateTime = ZeroDateTimeOffsetValue;

                if (firstClientUseDate._IsFilled())
                {
                    ret.TrialExpireDateTime = firstClientUseDate + ThinControllerGlobalSettings.PaidService_TrialSpan;

                    if (DtOffsetNow > ret.TrialExpireDateTime)
                    {
                        ret.Status = ThinControllerPaidServiceRedirectUrlStatusFlag.TrialExpired;
                    }
                    else
                    {
                        ret.Status = ThinControllerPaidServiceRedirectUrlStatusFlag.InTrial;
                    }
                }
                else
                {
                    ret.TrialExpireDateTime = DtOffsetNow + ThinControllerGlobalSettings.PaidService_TrialSpan;
                    ret.Status = ThinControllerPaidServiceRedirectUrlStatusFlag.InTrial;
                }
            }
        }

        return ret;
    }
}


// RPC-API インターフェイス
// 主にシン・テレワークシステム プライベート版を用いて商用サービスを実装したいユーザー (システム開発者) 向けの機能
[RpcInterface]
public interface IThinControllerRpcApi
{
    [RpcMethodHelp("テスト関数。パラメータで int 型で指定された値を文字列に変換し、Hello という文字列を前置して返却します。RPC を呼び出すためのテストコードを実際に記述する際のテストとして便利です。", "Hello 123")]
    public Task<string> Test([RpcParamHelp("テスト入力整数値", 123)] int i);

    [RpcRequireAuth]
    [RpcMethodHelp("1 台のサーバーオブジェクトの固有 ID を指定して、サーバーオブジェクトの情報を取得します。")]
    public Task<ThinControllerRpcServerObjectInfo?> ServerObject_Get(
        [RpcParamHelp("サーバーオブジェクトの固有 ID。空白文字列は省略可能。半角英数字。大文字・小文字を個別しない。", "00112233445566778899AABBCCDDEEFF00112233")] string uniqueId
        );

    [RpcRequireAuth]
    [RpcMethodHelp("1 台のサーバーオブジェクトの固有 ID を指定して、サーバーオブジェクトをアクティベーション (有効化) します。また、有効化された後のサーバーオブジェクトの情報を取得します。")]
    public Task<ThinControllerRpcServerObjectInfo?> ServerObject_Activate(
        [RpcParamHelp("サーバーオブジェクトの固有 ID。空白文字列は省略可能。半角英数字。大文字・小文字を個別しない。", "00112233445566778899AABBCCDDEEFF00112233")] string uniqueId,
        [RpcParamHelp("任意のタグ文字列。このタグ文字列は、サーバーオブジェクトと一緒にデータベースに保存され、GetServerObjectInfoByUniqueId API で返却される情報に含まれます。", "Hello East Telecom")] string tag
        );

    [RpcRequireAuth]
    [RpcMethodHelp("1 台のサーバーオブジェクトの固有 ID を指定して、サーバーオブジェクトをアクティベーション解除 (無効化) します。また、無効化された後のサーバーオブジェクトの情報を取得します。")]
    public Task<ThinControllerRpcServerObjectInfo?> ServerObject_Deactivate(
        [RpcParamHelp("サーバーオブジェクトの固有 ID。空白文字列は省略可能。半角英数字。大文字・小文字を個別しない。", "00112233445566778899AABBCCDDEEFF00112233")] string uniqueId,
        [RpcParamHelp("任意のタグ文字列。このタグ文字列は、サーバーオブジェクトと一緒にデータベースに保存され、GetServerObjectInfoByUniqueId API で返却される情報に含まれます。", "Hello East Telecom")] string tag
        );
}

// シン・テレワークシステム プライベート版を用いて商用サービスを実装したいユーザー (システム開発者) 向けの機能
[Flags]
public enum ThinControllerPaidServiceRedirectUrlStatusFlag
{
    InTrial = 0,    // 体験期間中
    TrialExpired,   // 体験版の有効期限が切れた
    Activated,      // 製品版アクティベーション済み
    Deactivated,    // 製品版として利用していたが、何らかの理由でアクティベーション解除がされた
}

public class ThinControllerRpcServerObjectInfo
{
    public string Pcid = "";
    public bool IsCurrentActivated;
    public bool ActivatedOnceInPast;
    public string Tag = "";
    public DateTimeOffset FirstClientUseDateTime = ZeroDateTimeOffsetValue;
    public DateTimeOffset ActivatedDateTime = ZeroDateTimeOffsetValue;
    public DateTimeOffset DeactivatedDateTime = ZeroDateTimeOffsetValue;
    public DateTimeOffset TrialExpireDateTime = ZeroDateTimeOffsetValue;

    [JsonConverter(typeof(StringEnumConverter))]
    public ThinControllerPaidServiceRedirectUrlStatusFlag Status;

    public static ThinControllerRpcServerObjectInfo _Sample
        => new ThinControllerRpcServerObjectInfo
        {
            Pcid = "abc123",
            IsCurrentActivated = true,
            ActivatedOnceInPast = true,
            Tag = "Hello Neko",
            FirstClientUseDateTime = new DateTime(2020, 4, 30).AddSeconds(29321)._AsDateTimeOffset(true, true),
            ActivatedDateTime = new DateTime(2020, 5, 3).AddSeconds(54321)._AsDateTimeOffset(true, true),
            DeactivatedDateTime = ZeroDateTimeOffsetValue,
            TrialExpireDateTime = new DateTime(2019, 12, 11).AddSeconds(59321)._AsDateTimeOffset(true, true)
        };

    public static ThinControllerRpcServerObjectInfo _Sample_DeactivateServerObject
        => new ThinControllerRpcServerObjectInfo
        {
            Pcid = "def456",
            IsCurrentActivated = false,
            ActivatedOnceInPast = true,
            Tag = "Hello East Telecom",
            FirstClientUseDateTime = new DateTime(2020, 7, 6).AddSeconds(9321)._AsDateTimeOffset(true, true),
            DeactivatedDateTime = new DateTime(2021, 12, 26).AddSeconds(65432)._AsDateTimeOffset(true, true),
            TrialExpireDateTime = new DateTime(2020, 8, 8).AddSeconds(45432)._AsDateTimeOffset(true, true),
        };
}


#endif

