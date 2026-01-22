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

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

using IPA.Cores.Codes;
using IPA.Cores.Helper.Codes;
using static IPA.Cores.Globals.Codes;
using System.Data;

namespace IPA.Cores.Codes;

public static partial class ThinControllerConsts
{
    public const int MaxPcidLen = 31;

    public static readonly Copenhagen<string> AccessLogTag = "ThinControlerLog";

    public static readonly Copenhagen<int> ControllerMaxBodySizeForUsers = 64 * 1024;
    public static readonly Copenhagen<int> ControllerMaxBodySizeForGateway = (int)Pack.MaxPackSize;
    public static readonly Copenhagen<int> ControllerMaxConcurrentKestrelConnectionsForUsers = 1000 * Math.Max(Environment.ProcessorCount, 1);
    public static readonly Copenhagen<int> ControllerStatCacheExpiresMsecs = 30 * 1000;
    public static readonly Copenhagen<int> ControllerMaxDatabaseWriteQueueLength = 100000; // 1 レコードあたり 10KB として 1GB 分まで

    public static readonly Copenhagen<string> Default_DbConnectionString_Read = "Data Source=127.0.0.1;Initial Catalog=THINDB;Persist Security Info=True;Pooling=False;User ID=sql_thin_reader;Password=sql_password;";
    public static readonly Copenhagen<string> Default_DbConnectionString_Write = "Data Source=127.0.0.1;Initial Catalog=THINDB;Persist Security Info=True;Pooling=False;User ID=sql_thin_writer;Password=sql_password;";

    public static readonly Copenhagen<string> Default_WildCardDnsDomainName = "websocket.jp";
    public static readonly Copenhagen<string> Default_ThinWebClient_WebSocketWildCardDomainName = "websocket.jp";
    public static readonly Copenhagen<string> Default_ThinWebClient_WebSocketWildCardCertServerLatestUrl = "http://ssl-cert-server.websocket.jp/wildcard_cert_files/websocket.jp/latest/";
    public static readonly Copenhagen<string> Default_ThinWebClient_WebSocketWildCardCertServerLatestUrl_Username = "user123";
    public static readonly Copenhagen<string> Default_ThinWebClient_WebSocketWildCardCertServerLatestUrl_Password = "pass123";
    public static readonly Copenhagen<int> ThinWebClient_WebSocketCertMaintainer_Interval_Normal_Msecs = 1 * 60 * 60 * 1000;
    public static readonly Copenhagen<int> ThinWebClient_WebSocketCertMaintainer_Interval_Retry_Initial_Msecs = 15 * 1000;
    public static readonly Copenhagen<int> ThinWebClient_WebSocketCertMaintainer_Interval_Retry_Max_Msecs = 5 * 60 * 1000;

    // DB 接続時のトランザクション分離レベルのデフォルト値
    public static readonly Copenhagen<IsolationLevel> Controller_DatabaseIsolationLevel_Read = IsolationLevel.Snapshot;
    public static readonly Copenhagen<IsolationLevel> Controller_DatabaseIsolationLevel_Write = IsolationLevel.Serializable;

    // DB の Var で設定可能な変数のデフォルト値
    public static readonly Copenhagen<int> Default_ControllerMaxConcurrentWpcRequestProcessingForUsers = 500;
    public static readonly Copenhagen<int> Default_ControllerDbFullReloadIntervalMsecs = 10 * 1000;
    public static readonly Copenhagen<int> Default_ControllerDbWriteUpdateIntervalMsecs = 1 * 1000;
    public static readonly Copenhagen<int> Default_ControllerDbBackupFileWriteIntervalMsecs = 5 * 60 * 1000;
    public static readonly Copenhagen<int> Default_ControllerRecordStatIntervalMsecs = 5 * 60 * 1000;

    // DB の Var で設定可能な変数の最大値
    public const int Max_ControllerDbReadFullReloadIntervalMsecs = 30 * 60 * 1000;
    public const int Max_ControllerDbWriteUpdateIntervalMsecs = 5 * 60 * 1000;
    public const int Max_ControllerDbBackupFileWriteIntervalMsecs = 24 * 60 * 60 * 1000;
    public const int Max_ControllerRecordStatIntervalMsecs = 60 * 60 * 1000;

    public static readonly Copenhagen<string> ControllerDefaultAdminUsername = "admin";
    public static readonly Copenhagen<string> ControllerDefaultAdminPassword = "ipantt";

    public static readonly Copenhagen<string> DefaultControllerGateSecretKey = "JuP4611KJd1dFTqenNpVPU6r";

}

public static partial class ThinWebClientConsts
{
    public static readonly Copenhagen<int> ControllerMaxBodySizeForUsers = 1 * 1024 * 1024;
    public static readonly Copenhagen<int> ControllerMaxConcurrentKestrelConnectionsForUsers = 10000;

    public static readonly Copenhagen<int> MaxHistory = 12;
}

[Flags]
public enum ThinControllerServiceType
{
    ApiServiceForUsers,
    ApiServiceForGateway,
}

#endif

