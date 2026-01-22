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

using System;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using System.IO;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using System.Linq;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

namespace IPA.Cores.Basic;

public static partial class Consts
{
    public static partial class ExitCodes
    {
        // UNIX の制限のため、0 - 255 に限る。
        public const byte NoError = 0;
        public const byte GenericError = 1;
        public const byte DaemonCenterRebootRequestd_Normal = 81;
        public const byte DaemonCenterRebootRequestd_GitUpdated = 82;
    }

    public static partial class GoldenRatioPrime
    {
        // From https://github.com/torvalds/linux/blob/88c5083442454e5e8a505b11fa16f32d2879651e/include/linux/hash.h
        public const uint U32 = 0x61C88647;
        public const ulong U64 = 0x61C8864680B583EB;

        public const int S32 = unchecked((int)(U32));
        public const long S64 = unchecked((long)(U64));
    }

    public static partial class Numbers
    {
        public const long MaxMatchPoint = 1_0000_0000_0000_0000;
        public const long MaxMatchPoint2 = 1_0000_0000;
        public const long LogBrowserDefaultTailSize = 10_000;

        public const int DefaultUseStorageThreshold = 1_000_000;

        public const int DefaultSmallBufferSize = 8192;
        public const int DefaultLargeBufferSize = 65536;
        public const int DefaultVeryLargeBufferSize = 400000;

        public const int DefaultMaxLineSizeStreamRecv = 16 * 1024;

        public static readonly int MaxYear = (Util.MaxDateTimeValue.Year - 1);
        public static readonly int MinYear = Util.ZeroDateTimeValue.Year;

        public const int DefaultSendPingSize = 32;

        public const int DefaultBufferLines = 1024;

        public const int DefaultMaxBytesPerLine = 10 * 1024 * 1024;

        public const int DefaultMaxBytesTotalLine = 30 * 1024 * 1024;

        public const int DefaultMaxNetworkRecvSize = 30 * 1000 * 1000; // 30 MB (Kestrel default)

        public const int SignCodeServerMaxFileSize = 800 * 1024 * 1024; // 800 MB

        public const int GcTempFreq = 100;

        public const int DefaultMaxPartialFragments = 4096;

        public const int NormalJsonMaxSize = 1 * 1024 * 1024; // 1MB

        public const int MaxCookieDays = 365 + 366; // 2 Years

        public const int DefaultKestrelMaxConcurrentConnections = 10000;
        public const int DefaultKestrelMaxUpgradedConnections = 10000;

        public const int DefaultCacheExpiresMsecs = 60 * 1000;
        public const int MinCacheGcIntervalsMsecs = 100;

        public const int VlanMin = 1;
        public const int VlanMax = 4094;

        public const int PortMin = 1;
        public const int PortMax = 65535;

        public const int GenericMaxEntities_Small = 512;
        public const int GenericMaxSize_Middle = 1_000_000;

        public const int EasyHttpClient_DefaultTryCount = 5;
        public const int EasyHttpClient_DefaultRetryIntervalMsecs = 1 * 1000;

        public static readonly TimeSpan MaxCertExpireSpanTargetForUpdate = new TimeSpan(4 * 365, 0, 0, 0);

        public static readonly TimeSpan JapanStandardTimeOffset = new TimeSpan(9, 0, 0);

        public const uint MagicNumber_u32 = 0x7eadbeef;
        public const uint MagicNumberB_u32 = 0x7eadcafe;

        public const int MikakaDDns_MaxUnlockKeyCountOnce = 1000;

        public const int HadbFullTextSearchResultsInternalMemoryDefault = 200000;
        public const int HadbFullTextSearchResultsMaxDefault = 100000;
        public const int HadbFullTextSearchResultsStandardDefault = 100;

        public const int HadbHistoryMaxCount = 1000;

        public const double NearlyZero_Double = 0.0000000001;
        public const float NearlyZero_Float = 0.0000001f;

        // SQL Server その他の一般的なデータベースでインデックス可能な最大安全文字列長
        public const int MaxKeyOrLabelStrLength = 130;

        public const int HadbMaxLazyUpdateParallelQueueCount = 256;
        public const int HadbDefaultLazyUpdateParallelQueueCount = 32;

        public const int ReadOnlyCacheFileSystem_DefaultMaxDirs = 4096;
        public const int ReadOnlyCacheFileSystem_DefaultMaxFiles = 4096;
        public const long ReadOnlyCacheFileSystem_DefaultMaxSingleFileSize = 128 * 1024 * 1024;
        public const int ReadOnlyCacheFileSystem_DefaultPollIntervalMsecs = 60 * 1000;
    }

    public static partial class MaxLens
    {
        public const int MaxAutoCertGeneratingFqdnLen = 64;

        public const int GitCommitIdTruncateLen = 8;
        public const int StandardTruncateLen = 32;
        public const int NormalStringTruncateLen = 255;

        public const int ExceptionStrTruncateLen = 800;

        public const int MaxCookieSize = 4093;

        public const int DataVaultPathElementMaxLen = 64;

        public const int MaxCachedFileDownloadSizeDefault = 32 * 1024 * 1024;
    }

    public static partial class Ports
    {
        public const int TelnetLogWatcher = 8023;

        public const int LogServerDefaultHttpPort = 80;
        public const int LogServerDefaultHttpsPort = 443;

        public const int DataVaultServerDefaultHttpPort = 80;
        public const int DataVaultServerDefaultHttpsPort = 443;

        public const int Http = 80;
        public const int Https = 443;

        public const int Telnet = 23;
        public const int Ssh = 22;
        public const int Smtp = 25;
        public const int SmtpSubmission = 587;
        public const int Smtps = 465;
        public const int Pop3 = 110;
        public const int Pop3s = 995;
        public const int Imap4 = 143;
        public const int Imap4s = 993;
        public const int Dns = 53;
        public const int MsSqlServer = 1433;

        public const int VoiceVox = 50021;
        public const int FishAudio = 50022;

        public const int DynamicPortMin = 10000;
        public const int DynamicPortMax = 19999;
        public const int DynamicPortCheckRetryMaxCount = 200;

        public static readonly IEnumerable<int> PotentialHttpsPorts = new int[] { Https, Smtps, Pop3s, Imap4s };

        // Unique server ports
        public const int MistPPPoEServerHttp = 7001;
        public const int MistPPPoEServerHttps = 7002;
        public const int LogServerDefaultServicePort = 7003;
        public const int DaemonCenterHttps = 7004;
        public const int CodeSignServer = 7006;
        public const int SnmpWorkHttp = 7007;
        public const int SnmpWorkTelnetStat = 7008;
        public const int DataVaultServerDefaultServicePort = 7009;
        public const int StressMonServerPort = 7010;
        public const int SslTestSuitePort = 7011;
        public const int SyslogServerPort = 514;
        public const int FntpMainteDaemonHealthPort = 7014;
        public const int FntpMainteDaemonHttpAdminPort = 7015;
        public const int FntpMainteDaemonHttpWhoAmIPort = 7016;
        public const int WebAutoChromeDebuggerPortDefault = 7022;
    }

    public static partial class DaemonArgKeys
    {
        public const string StartLogFileBrowser = "StartLogFileBrowser";
        public const string LogFileBrowserPort = "LogFileBrowserPort";
        public const string ForceGc = "ForceGc";
    }

    public static partial class DaemonMetaStatKeys
    {
        public const string CurrentLogFileBrowserUrl = "CurrentLogFileBrowserUrl";
        public const string CurrentDaemonClientLocalIp = "CurrentDaemonClientLocalIp";
    }

    public static partial class Strings
    {
        public const string DefaultCertCN = "DefaultCertificate";
        public const string DefaultSplitStr = " ,\t\r\n";

        public static readonly IEnumerable<char> DefaultEnumBitsSeparaters = new char[] { ',', '|', ' ', ';', '+', ':', '.', '/', '-', '　', };

        public const string DefaultKeyAndValueSplitStr = " \t";

        public const string LogBrowserDefaultSystemTitle = "HTTP Log Browser";

        public static readonly IEnumerable<string> CommentStartString = new string[] { "#", "//", ";" };

        public static readonly IEnumerable<string> CommentStartStringForMimeList = new string[] { "#", "//" };

        public static readonly IEnumerable<string> CommentStartStringForEasyIpAcl = new string[] { "#", "//" };

        public static readonly IEnumerable<string> AutoEnrollCertificateSubjectInStrList = new string[] { "Let's Encrypt", "Google Internet Authority", "Google Trust Services" };

        public const string EncodeEasyPrefix = "_E_";

        public const string HidePassword = "********";

        public const string RootUsername = "root";

        public const string DaemonDefFileMarker = "hTNdwaKmxL4MNPAyyes2qsgT";

        public const string DaemonExecModeLogFileSuffix = "daemon";

        public const string EasyCookieNamePrefix_Http = "Cores_Http_EasyCookie_";
        public const string EasyCookieNamePrefix_Https = "Cores_Https_EasyCookie_";
        public const string EasyCookieValuePrefix = "Ec_";

        public const string Sample = "__sample__";

        public const string StatManEncryptKey = "e6B8zNWgCEXuH44LNaNynyJS";

        public const string SmsDefaultCountryCode = "+81";

        public const string DefaultSessionIdPrefix = "SESSION";

        public const string EasyEncryptDefaultPassword = "pLkw4jkN8YxcD54AJ2rVvaD3sdnJEzMN";

        public const string HadbDefaultNameSpace = "DEFAULT_NS";

        public const string DefaultAdminUsername = "admin";
        public const string DefaultAdminPassword = "admin";

        public const string None = "none";

        public const string EasyAclAllowAllRule = "0.0.0.0/0; ::/0";
    }

    public static partial class HiveNames
    {
        public const string DefaultWebServer = "WebServer";
    }

    public static partial class MimeTypes
    {
        public const string Json = "application/json";
        public const string JoseJson = "application/jose+json";
        public const string FormUrlEncoded = "application/x-www-form-urlencoded";
        public const string OctetStream = "application/octet-stream";
        public const string Binary = OctetStream;
        public const string Directory = "text/directory";
        public const string DirectoryOpening = "text/directory-open";
        public const string Html = "text/html";
        public const string Zip = "application/zip";

        public const string Text = "text/plain";
        public const string TextUtf8 = "text/plain; charset=UTF-8";
        public const string HtmlUtf8 = "text/html; charset=UTF-8";
    }

    public static partial class FileNames
    {
        public const string CertVault_Settings = "settings.json";
        public const string CertVault_Password = "password.txt";

        public const string CertVault_AcmeAccountKey = "acme_account.key";
        public const string CertVault_AcmeCertKey = "acme_cert.key";

        public const string CertVault_DefaultCert = "default.pfx";
        public const string CertVault_AutoGeneratingCert = "ca.pfx";

        public const string DefaultStopRootSearchFileExtsForSafety = ".sln .git";

        public static readonly IEnumerable<string> AppRootMarkerFileNamesForBinary = new string[] { "approot", "appsettings.json", "appsettings.Development.json" };

        public static readonly IEnumerable<string> AppRootMarkerFileNames = new string[] { "approot", ".csproj" };

        public const string ResourceRootAbsoluteDirName = "/ResourceRoot";

        public const string SystemdConfigDirName = "/etc/systemd/system/";

        public const string RootMarker_Resource = "resource_root";
        public const string RootMarker_Library_CoresBasic = "cores_basic_root";
        public const string RootMarker_Library_CoresCodes = "cores_codes_root";
        public const string RootMarker_Library_CoresWeb = "cores_web_root";

        public const string MyDynamicTempSubDirName = "_dynamic";

        public const string AutoArchiveSubDirName = ".AutoBackup";

        public const string LogBrowserSecureJson = "_secure.json";
        public const string LogBrowserAccessLogDirName = "_accesslog";
        public const string LogBrowserHistoryDirName = "_history";
        public const string LogBrowserZipFileName = "_download_zip";

        public const string HadbBackupDatabaseFileName = "Database.json";
        public const string HadbBackupDynamicConfigFileName = "DynamicConfig.json";

        public const string GitLabMainte_CommitIdFileName = "_git_current_commit_id.txt";
        public const string GitLabMainte_PublishFileName = "publish.config";

        public const string OkFileDirName = ".ok";
        public const string OkFileExt = ".ok.txt";

        public static readonly IEnumerable<string> StandardExcludeDirNames = new string[] { ".svn", "_vti_cnf", "_vti_pvt", "_private", ".git", ".vs" };

        public static bool IsSpecialFileNameForLogBrowser(string? fn)
        {
            fn = fn._NonNullTrim().ToLowerInvariant();

            switch (fn)
            {
                case AutoArchiveSubDirName:
                case LogBrowserSecureJson:
                case LogBrowserAccessLogDirName:
                case LogBrowserZipFileName:
                    return true;
            }

            return false;
        }
    }

    public static partial class HadbDynamicConfigDefaultValues
    {
        public const int HadbReloadIntervalMsecsLastOk = 5 * 1000;
        public const int HadbReloadIntervalMsecsLastError = 1 * 1000;
        public const int HadbReloadTimeShiftMarginMsecs = 60 * 1000;
        public const int HadbFullReloadIntervalMsecs = 30 * 60 * 1000;
        public const int HadbLazyUpdateIntervalMsecs = 1 * 1000;

        public const int HadbAutomaticSnapshotIntervalMsecs = 12 * 60 * 60 * 1000;
        public const int HadbAutomaticStatIntervalMsecs = 60 * 60 * 1000;

        public const int HadbMaxDbConcurrentWriteTransactionsTotal = 50;
        public const int HadbMaxDbConcurrentReadTransactionsTotal = 500;

        public const int HadbMaxDbConcurrentWriteTransactionsPerClient = 5;
        public const int HadbMaxDbConcurrentReadTransactionsPerClient = 25;
    }

    public static partial class HadbDynamicConfigMaxValues
    {
        public const int HadbReloadIntervalMsecsLastOk = 30 * 60 * 1000;
        public const int HadbReloadIntervalMsecsLastError = 30 * 60 * 1000;
        public const int HadbReloadTimeShiftMarginMsecs = 4 * 60 * 60 * 1000;
        public const int HadbFullReloadIntervalMsecs = 4 * 60 * 60 * 1000;
        public const int HadbLazyUpdateIntervalMsecs = 5 * 60 * 1000;
    }

    public static partial class BlazorApp
    {
        public const string DummyImageFileName = "/tmp/dummy/webasm.exe";
        public const string DummyImageDirName = "/tmp/dummy";
        public const string DummyBuildConfigurationName = "Debug";

        public const string DummyFqdn = "webasm.example.org";
        public const int DummyProcessId = 12345;
    }

    public static partial class Extensions
    {
        public const string Certificate = ".cer";
        public const string Certificate_Acme = ".crt";
        public const string Pkcs12 = ".pfx";
        public const string GenericKey = ".key";
        public const string Text = ".txt";

        public const string Filter_Pkcs12s = "*.p12;*.pfx";
        public const string Filter_Certificates = "*.cer;*.crt";
        public const string Filter_Keys = "*.key;*.pem";

        public const string Filter_MusicFiles = "*.wav;*.mp3;*.m4a;*.aac;*.flac;*.wma";

        public const string Zip = ".zip";

        public const string Data = ".dat";
        public const string Backup = ".bak";

        public const string Win32Executable = ".exe";

        public const string Filter_SourceCodes = "*.c;*.cpp;*.h;*.rc;*.stb;*.cs;*.fx;*.hlsl;*.cxx;*.cc;*.hh;*.hpp;*.hxx;*.hh;*.txt;*.cshtml;*.scss;*.ts;*.js;*.resx;*.htm;*.html;*.aspx;*.ascx;*.asmx;*.asp;*.vbhtml;*.razor;*.css;*.xml;*.json;*.sln;*.vcxproj;*.csproj;*.md;*.yml;";

        public const string Wildcard_SourceCode_NormalizeBomUtf8_Include = "*.c;*.cpp;*.h;*.stb;*.cs;*.fx;*.hlsl;*.cxx;*.cc;*.hh;*.hpp;*.hxx;*.hh;*.txt;*.cshtml;*.scss;*.ts;*.js;*.resx;*.htm;*.html;*.aspx;*.ascx;*.asmx;*.asp;*.vbhtml;*.razor;*.css;*.xml;*.json;*.sln;*.vcxproj;*.csproj;*.md;*.yml;";
        public const string Wildcard_SourceCode_NormalizeBomUtf8_Exclude = "resource.h;CurrentBuild.txt;package-lock.json;package.json;tsconfig.json;bundle.js.LICENSE.txt;bundle.js;stats.json;";

        public const string EncryptedXtsAes256 = "._encrypted_xtsaes256";
        public const string CompressedXtsAes256 = "._compressed_xtsaes256";

        public const string EncryptedSecureCompress = "._encrypted_securecompress";

        public const string DirSuperBackupHistory = "._backup_history";
    }

    public static partial class Urls
    {
        public const string GetMyIpUrl_IPv4 = "http://getmyip-v4.arpanet.jp/";
        public const string GetMyIpUrl_IPv6 = "http://getmyip-v6.arpanet.jp/";
    }

    public static partial class CdnUrls
    {
        public const string GmailIcon = "https://upload.wikimedia.org/wikipedia/commons/4/4e/Gmail_Icon.png";
    }

    public static partial class UrlPaths
    {
        public const string Robots = "/robots.txt";

        public const string LogBrowserMvcPath = "/LogBrowser";
    }

    public static partial class OAuthScopes
    {
        public const string Slack_Client = "client";
        public const string Google_Gmail = "https://www.googleapis.com/auth/gmail.readonly";
    }

    public static partial class HtmlTarget
    {
        public const string Blank = "_blank";
    }

    public static partial class InboxProviderNames
    {
        public const string Gmail = "Gmail";
        public const string Slack_App = "Slack_as_Registered_App";
        public const string Slack_User = "Slack_as_Per_User_Token";

        public const string Slack_Old = "Slack";
    }

    public static partial class HttpHeaders
    {
        public const string WWWAuthenticate = "WWW-Authenticate";
        public const string UserAgent = "User-Agent";
        public const string Referer = "Referer";
        public const string ContentDisposition = "Content-Disposition";
    }

    public static partial class HttpStatusCodes
    {
        public const int Continue = 100;
        public const int Ok = 200;
        public const int MovedPermanently = 301;
        public const int Found = 302;
        public const int NotModified = 304;
        public const int TemporaryRedirect = 307;
        //public const int BadRequest = 100; 注意して利用すること！ 危険！ コメントアウトした
        public const int Unauthorized = 401;
        public const int Forbidden = 403;
        public const int NotFound = 404;
        public const int MethodNotAllowed = 405;
        public const int InternalServerError = 500;
        public const int NotImplemented = 501;
        public const int ServiceUnavailable = 503;
        public const int TooManyRequests = 429;
    }

    public static partial class HttpProtocolSchemes
    {
        public const string Http = "http";
        public const string Https = "https";
    }

    public static partial class RateLimiter
    {
        public const int DefaultSrcIPv4SubnetLength = 24;
        public const int DefaultSrcIPv6SubnetLength = 56;

        // RateLimiter
        public const double DefaultBurst = 200;
        public const double DefaultLimitPerSecond = 10;
        public const int DefaultExpiresMsec = 30_000;
        public const int DefaultMaxEntries = 1_000_000; // 100 万セッションまで対応!?
        public const int DefaultGcIntervalMsec = 15_000;

        // ConcurrentLimiter
        public const int DefaultMaxConcurrentRequestsPerSrcSubnet = 40;
    }

    public static partial class Intervals
    {
        public const int MinKeepAliveIntervalsMsec = 1 * 1000;
        public const int MaxKeepAliveIntervalsMsec = 24 * 60 * 60 * 1000;

        public const int JsonRpcClientEndPointInfoUpdateInterval = 60 * 1000;

        public const int AutoArchivePollingInterval = 12 * 60 * 1000;

        public const int UiAutomationDefaultInterval = 50;

        public const int DefaultThroughtputMeasutementUnitMsecs = 60 * 1000;
        public const int DefaultThroughtputInitialMinMeasutementUnitMsecs = 1 * 1000;
        public const int DefaultThroughtputMeasutementPrintMsecs = 100;

        public const int WtEntranceUrlTimeUpdateMsecs = 5 * 60 * 1000;
    }

    public static partial class Timeouts
    {
        public const int Rapid = 5 * 1000;

        public const int DefaultSendPingTimeout = 1 * 1000;

        public const int GcTempDefaultFileLifeTime = 5 * 60 * 1000;

        public const int DefaultShellPromptRecvTimeout = 30 * 1000;
        public const int DefaultShellPromptSendTimeout = 30 * 1000;

        public const int DefaultDialogSessionExpiresAfterFinishedMsecs = 300 * 1000;
        public const int DefaultDialogSessionGcIntervals = 10 * 1000;
    }

    public static partial class LinuxCommands
    {
        // 一応絶対パスでここに書くことを推奨するが、コマンド名のみでもよい。
        // また、実行時に絶対パスが見つからない場合は、 LinuxPaths.BasicBinDirList のディレクトリの探索が自動的になされる
        // ので、さほど心配することなく色々な Linux ディスフリビューションでそのまま動作させることができる。
        public const string Bash = "/bin/bash";
        public const string Ip = "/sbin/ip";
        public const string Ifconfig = "/sbin/ifconfig";
        public const string ConnTrack = "/usr/sbin/conntrack";
        public const string PppoeDiscovery = "/usr/sbin/pppoe-discovery";
        public const string PppoeStart = "/usr/sbin/pppoe-start";
        public const string KillAll = "/usr/bin/killall";
        public const string Reboot = "/sbin/reboot";
        public const string Sync = "/bin/sync";
        public const string Ping = "/bin/ping";
        public const string Ping6 = "/bin/ping6";
        public const string IpTables = "/sbin/iptables";
        public const string Sensors = "/usr/bin/sensors";
        public const string Free = "/usr/bin/free";
        public const string Df = "/bin/df";
        public const string Birdc = "/usr/local/sbin/birdc";
        public const string Birdc6 = "/usr/local/sbin/birdc6";
        public const string Temper = "/usr/bin/temper";
        public const string BlockDev = "/sbin/blockdev";
        public const string FindMnt = "/bin/findmnt";
    }

    public static partial class LinuxPaths
    {
        public static readonly IEnumerable<string> BasicBinDirList = new string[] {
                "/bin/",
                "/sbin/",
                "/usr/bin/",
                "/usr/sbin/",
                "/usr/local/bin/",
                "/usr/local/sbin/",
            };

        public const string SysThermal = "/sys/class/thermal/";
        public const string SockStat = "/proc/net/sockstat";
        public const string FileNr = "/proc/sys/fs/file-nr";
        public const string Aliases = "/etc/aliases";
        public const string Passwd = "/etc/passwd";
        public const string Shadow = "/etc/shadow";
    }

    public static partial class StrEncodingAutoDetector
    {
        public const string Candidates = "utf-8 euc-jp shift_jis gb2312 euc-kr iso-8859-1 big5 iso-2022-jp";
    }

    public static partial class SnmpOids
    {
        public const string SnmpWorkNames = ".1.3.6.1.4.1.9801.5.29.1.1";
        public const string SnmpWorkValues = ".1.3.6.1.4.1.9801.5.29.1.2";
    }
}

public static partial class CoresConfig
{
    public static partial class Timeouts
    {
        public static readonly Copenhagen<int> DaemonCenterRebootRequestTimeout = 60 * 1000;

        public static readonly Copenhagen<int> DaemonCenterGitUpdateTimeout = 3 * 60 * 1000;

        public static readonly Copenhagen<int> DaemonStopLogFinish = 60 * 1000;

        public static readonly Copenhagen<int> DefaultEasyExecTimeout = 60 * 1000;

        public static readonly Copenhagen<int> RebootDangerous_Sync_Timeout = 20 * 1000;

        public static readonly Copenhagen<int> RebootDangerous_Reboot_Timeout = 60 * 1000;

        public static readonly Copenhagen<int> GitCommandTimeout = 60 * 1000;

        public static readonly Copenhagen<int> DaemonDefaultStopTimeout = 60 * 1000;

        public static readonly Copenhagen<int> DaemonStartExecTimeout = 5 * 60 * 1000;

        public static readonly Copenhagen<int> DaemonSystemdStartTimeoutSecs = 10 * 60;

        public static readonly Copenhagen<int> DaemonSystemdStopTimeoutSecs = 2 * 60;

        // 重いサーバー (大量のインスタンスや大量のコンテナが稼働、または大量のコネクションを処理) における定数変更
        public static void ApplyHeavyLoadServerConfig()
        {
            DaemonStartExecTimeout.TrySet(3 * 60 * 60 * 1000);
            DaemonCenterRebootRequestTimeout.TrySet(15 * 60 * 1000);
            DaemonCenterGitUpdateTimeout.TrySet(3 * 60 * 60 * 1000);
            DaemonStopLogFinish.TrySet(3 * 60 * 1000);
            DefaultEasyExecTimeout.TrySet(1 * 60 * 60 * 1000);
            RebootDangerous_Reboot_Timeout.TrySet(5 * 60 * 1000);
            GitCommandTimeout.TrySet(60 * 60 * 1000);
            DaemonDefaultStopTimeout.TrySet(3 * 60 * 60 * 1000);
            DaemonSystemdStartTimeoutSecs.TrySet(3 * 60 * 60);
            DaemonSystemdStopTimeoutSecs.TrySet(5 * 60);
        }
    }

    public static partial class BufferSizes
    {
        public static readonly Copenhagen<int> FileCopyBufferSize = 81920;  // .NET の Stream クラスの実装からもらってきた定数

        public static readonly Copenhagen<int> MaxNetworkStreamSendRecvBufferSize = 65536;  // ストリームソケットの送受信バッファの最大サイズ
    }
}

public static partial class CoresConfig
{
    // 重いサーバー (大量のインスタンスや大量のコンテナが稼働、または大量のコネクションを処理) における定数変更
    public static void ApplyHeavyLoadServerConfigAll()
    {
        Timeouts.ApplyHeavyLoadServerConfig();
        PipeConfig.ApplyHeavyLoadServerConfig();
        FileDownloader.ApplyHeavyLoadServerConfig();
        FastBufferConfig.ApplyHeavyLoadServerConfig();
        FastMemoryPoolConfig.ApplyHeavyLoadServerConfig();
        ThreadPoolConfig.ApplyHeavyLoadServerConfig();
    }
}

[Flags]
public enum VpnError
{
    // By ConvertCErrorsToCsErrors
    ERR_NO_ERROR = 0, // No error
    ERR_CONNECT_FAILED = 1, // Connection to the server has failed
    ERR_SERVER_IS_NOT_VPN = 2, // The destination server is not a VPN server
    ERR_DISCONNECTED = 3, // The connection has been interrupted
    ERR_PROTOCOL_ERROR = 4, // Protocol error
    ERR_CLIENT_IS_NOT_VPN = 5, // Connecting client is not a VPN client
    ERR_USER_CANCEL = 6, // User cancel
    ERR_AUTHTYPE_NOT_SUPPORTED = 7, // Specified authentication method is not supported
    ERR_HUB_NOT_FOUND = 8, // The HUB does not exist
    ERR_AUTH_FAILED = 9, // Authentication failure
    ERR_HUB_STOPPING = 10, // HUB is stopped
    ERR_SESSION_REMOVED = 11, // Session has been deleted
    ERR_ACCESS_DENIED = 12, // Access denied
    ERR_SESSION_TIMEOUT = 13, // Session times out
    ERR_INVALID_PROTOCOL = 14, // Protocol is invalid
    ERR_TOO_MANY_CONNECTION = 15, // Too many connections
    ERR_HUB_IS_BUSY = 16, // Too many sessions of the HUB
    ERR_PROXY_CONNECT_FAILED = 17, // Connection to the proxy server fails
    ERR_PROXY_ERROR = 18, // Proxy Error
    ERR_PROXY_AUTH_FAILED = 19, // Failed to authenticate on the proxy server
    ERR_TOO_MANY_USER_SESSION = 20, // Too many sessions of the same user
    ERR_LICENSE_ERROR = 21, // License error
    ERR_DEVICE_DRIVER_ERROR = 22, // Device driver error
    ERR_INTERNAL_ERROR = 23, // Internal error
    ERR_SECURE_DEVICE_OPEN_FAILED = 24, // The secure device cannot be opened
    ERR_SECURE_PIN_LOGIN_FAILED = 25, // PIN code is incorrect
    ERR_SECURE_NO_CERT = 26, // Specified certificate is not stored
    ERR_SECURE_NO_PRIVATE_KEY = 27, // Specified private key is not stored
    ERR_SECURE_CANT_WRITE = 28, // Write failure
    ERR_OBJECT_NOT_FOUND = 29, // Specified object can not be found
    ERR_VLAN_ALREADY_EXISTS = 30, // Virtual LAN card with the specified name already exists
    ERR_VLAN_INSTALL_ERROR = 31, // Specified virtual LAN card cannot be created
    ERR_VLAN_INVALID_NAME = 32, // Specified name of the virtual LAN card is invalid
    ERR_NOT_SUPPORTED = 33, // Unsupported
    ERR_ACCOUNT_ALREADY_EXISTS = 34, // Account already exists
    ERR_ACCOUNT_ACTIVE = 35, // Account is operating
    ERR_ACCOUNT_NOT_FOUND = 36, // Specified account doesn't exist
    ERR_ACCOUNT_INACTIVE = 37, // Account is offline
    ERR_INVALID_PARAMETER = 38, // Parameter is invalid
    ERR_SECURE_DEVICE_ERROR = 39, // Error has occurred in the operation of the secure device
    ERR_NO_SECURE_DEVICE_SPECIFIED = 40, // Secure device is not specified
    ERR_VLAN_IS_USED = 41, // Virtual LAN card in use by account
    ERR_VLAN_FOR_ACCOUNT_NOT_FOUND = 42, // Virtual LAN card of the account can not be found
    ERR_VLAN_FOR_ACCOUNT_USED = 43, // Virtual LAN card of the account is already in use
    ERR_VLAN_FOR_ACCOUNT_DISABLED = 44, // Virtual LAN card of the account is disabled
    ERR_INVALID_VALUE = 45, // Value is invalid
    ERR_NOT_FARM_CONTROLLER = 46, // Not a farm controller
    ERR_TRYING_TO_CONNECT = 47, // Attempting to connect
    ERR_CONNECT_TO_FARM_CONTROLLER = 48, // Failed to connect to the farm controller
    ERR_COULD_NOT_HOST_HUB_ON_FARM = 49, // A virtual HUB on farm could not be created
    ERR_FARM_MEMBER_HUB_ADMIN = 50, // HUB cannot be managed on a farm member
    ERR_NULL_PASSWORD_LOCAL_ONLY = 51, // Accepting only local connections for an empty password
    ERR_NOT_ENOUGH_RIGHT = 52, // Right is insufficient
    ERR_LISTENER_NOT_FOUND = 53, // Listener can not be found
    ERR_LISTENER_ALREADY_EXISTS = 54, // Listener already exists
    ERR_NOT_FARM_MEMBER = 55, // Not a farm member
    ERR_CIPHER_NOT_SUPPORTED = 56, // Encryption algorithm is not supported
    ERR_HUB_ALREADY_EXISTS = 57, // HUB already exists
    ERR_TOO_MANY_HUBS = 58, // Too many HUBs
    ERR_LINK_ALREADY_EXISTS = 59, // Link already exists
    ERR_LINK_CANT_CREATE_ON_FARM = 60, // The link can not be created on the server farm
    ERR_LINK_IS_OFFLINE = 61, // Link is off-line
    ERR_TOO_MANY_ACCESS_LIST = 62, // Too many access list
    ERR_TOO_MANY_USER = 63, // Too many users
    ERR_TOO_MANY_GROUP = 64, // Too many Groups
    ERR_GROUP_NOT_FOUND = 65, // Group can not be found
    ERR_USER_ALREADY_EXISTS = 66, // User already exists
    ERR_GROUP_ALREADY_EXISTS = 67, // Group already exists
    ERR_USER_AUTHTYPE_NOT_PASSWORD = 68, // Authentication method of the user is not a password authentication
    ERR_OLD_PASSWORD_WRONG = 69, // The user does not exist or the old password is wrong
    ERR_LINK_CANT_DISCONNECT = 73, // Cascade session cannot be disconnected
    ERR_ACCOUNT_NOT_PRESENT = 74, // Not completed configure the connection to the VPN server
    ERR_ALREADY_ONLINE = 75, // It is already online
    ERR_OFFLINE = 76, // It is offline
    ERR_NOT_RSA_1024 = 77, // The certificate is not RSA 1024bit
    ERR_SNAT_CANT_DISCONNECT = 78, // SecureNAT session cannot be disconnected
    ERR_SNAT_NEED_STANDALONE = 79, // SecureNAT works only in stand-alone HUB
    ERR_SNAT_NOT_RUNNING = 80, // SecureNAT function is not working
    ERR_SE_VPN_BLOCK = 81, // Stopped by PacketiX VPN Block
    ERR_BRIDGE_CANT_DISCONNECT = 82, // Bridge session can not be disconnected
    ERR_LOCAL_BRIDGE_STOPPING = 83, // Bridge function is stopped
    ERR_LOCAL_BRIDGE_UNSUPPORTED = 84, // Bridge feature is not supported
    ERR_CERT_NOT_TRUSTED = 85, // Certificate of the destination server can not be trusted
    ERR_PRODUCT_CODE_INVALID = 86, // Product code is different
    ERR_VERSION_INVALID = 87, // Version is different
    ERR_CAPTURE_DEVICE_ADD_ERROR = 88, // Adding capture device failure
    ERR_VPN_CODE_INVALID = 89, // VPN code is different
    ERR_CAPTURE_NOT_FOUND = 90, // Capture device can not be found
    ERR_LAYER3_CANT_DISCONNECT = 91, // Layer-3 session cannot be disconnected
    ERR_LAYER3_SW_EXISTS = 92, // L3 switch of the same already exists
    ERR_LAYER3_SW_NOT_FOUND = 93, // Layer-3 switch can not be found
    ERR_INVALID_NAME = 94, // Name is invalid
    ERR_LAYER3_IF_ADD_FAILED = 95, // Failed to add interface
    ERR_LAYER3_IF_DEL_FAILED = 96, // Failed to delete the interface
    ERR_LAYER3_IF_EXISTS = 97, // Interface that you specified already exists
    ERR_LAYER3_TABLE_ADD_FAILED = 98, // Failed to add routing table
    ERR_LAYER3_TABLE_DEL_FAILED = 99, // Failed to delete the routing table
    ERR_LAYER3_TABLE_EXISTS = 100, // Routing table entry that you specified already exists
    ERR_BAD_CLOCK = 101, // Time is queer
    ERR_LAYER3_CANT_START_SWITCH = 102, // The Virtual Layer 3 Switch can not be started
    ERR_CLIENT_LICENSE_NOT_ENOUGH = 103, // Client connection licenses shortage
    ERR_BRIDGE_LICENSE_NOT_ENOUGH = 104, // Bridge connection licenses shortage
    ERR_SERVER_CANT_ACCEPT = 105, // Not Accept on the technical issues
    ERR_SERVER_CERT_EXPIRES = 106, // Destination VPN server has expired
    ERR_MONITOR_MODE_DENIED = 107, // Monitor port mode was rejected
    ERR_BRIDGE_MODE_DENIED = 108, // Bridge-mode or Routing-mode was rejected
    ERR_IP_ADDRESS_DENIED = 109, // Client IP address is denied
    ERR_TOO_MANT_ITEMS = 110, // Too many items
    ERR_MEMORY_NOT_ENOUGH = 111, // Out of memory
    ERR_OBJECT_EXISTS = 112, // Object already exists
    ERR_FATAL = 113, // A fatal error occurred
    ERR_SERVER_LICENSE_FAILED = 114, // License violation has occurred on the server side
    ERR_SERVER_INTERNET_FAILED = 115, // Server side is not connected to the Internet
    ERR_CLIENT_LICENSE_FAILED = 116, // License violation occurs on the client side
    ERR_BAD_COMMAND_OR_PARAM = 117, // Command or parameter is invalid
    ERR_INVALID_LICENSE_KEY = 118, // License key is invalid
    ERR_NO_VPN_SERVER_LICENSE = 119, // There is no valid license for the VPN Server
    ERR_NO_VPN_CLUSTER_LICENSE = 120, // There is no cluster license
    ERR_NOT_ADMINPACK_SERVER = 121, // Not trying to connect to a server with the Administrator Pack license
    ERR_NOT_ADMINPACK_SERVER_NET = 122, // Not trying to connect to a server with the Administrator Pack license (for .NET)
    ERR_BETA_EXPIRES = 123, // Destination Beta VPN Server has expired
    ERR_BRANDED_C_TO_S = 124, // Branding string of connection limit is different (Authentication on the server side)
    ERR_BRANDED_C_FROM_S = 125, // Branding string of connection limit is different (Authentication for client-side)
    ERR_AUTO_DISCONNECTED = 126, // VPN session is disconnected for a certain period of time has elapsed
    ERR_CLIENT_ID_REQUIRED = 127, // Client ID does not match
    ERR_TOO_MANY_USERS_CREATED = 128, // Too many created users
    ERR_SUBSCRIPTION_IS_OLDER = 129, // Subscription expiration date Is earlier than the build date of the VPN Server
    ERR_ILLEGAL_TRIAL_VERSION = 130, // Many trial license is used continuously
    ERR_NAT_T_TWO_OR_MORE = 131, // There are multiple servers in the back of a global IP address in the NAT-T connection
    ERR_DUPLICATE_DDNS_KEY = 132, // DDNS host key duplicate
    ERR_DDNS_HOSTNAME_EXISTS = 133, // Specified DDNS host name already exists
    ERR_DDNS_HOSTNAME_INVALID_CHAR = 134, // Characters that can not be used for the host name is included
    ERR_DDNS_HOSTNAME_TOO_LONG = 135, // Host name is too long
    ERR_DDNS_HOSTNAME_IS_EMPTY = 136, // Host name is not specified
    ERR_DDNS_HOSTNAME_TOO_SHORT = 137, // Host name is too short
    ERR_MSCHAP2_PASSWORD_NEED_RESET = 138, // Necessary that password is changed
    ERR_DDNS_DISCONNECTED = 139, // Communication to the dynamic DNS server is disconnected
    ERR_SPECIAL_LISTENER_ICMP_ERROR = 140, // The ICMP socket can not be opened
    ERR_SPECIAL_LISTENER_DNS_ERROR = 141, // Socket for DNS port can not be opened
    ERR_OPENVPN_IS_NOT_ENABLED = 142, // OpenVPN server feature is not enabled
    ERR_NOT_SUPPORTED_AUTH_ON_OPENSOURCE = 143, // It is the type of user authentication that are not supported in the open source version
    ERR_VPNGATE = 144, // Operation on VPN Gate Server is not available
    ERR_VPNGATE_CLIENT = 145, // Operation on VPN Gate Client is not available
    ERR_VPNGATE_INCLIENT_CANT_STOP = 146, // Can not be stopped if operating within VPN Client mode
    ERR_NOT_SUPPORTED_FUNCTION_ON_OPENSOURCE = 147, // It is a feature that is not supported in the open source version
    ERR_SUSPENDING = 148, // System is suspending
    ERR_DHCP_SERVER_NOT_RUNNING = 149, // DHCP server is not running
    ERR_MACHINE_ALREADY_CONNECTED = 201, // すでに接続されている
    ERR_DEST_MACHINE_NOT_EXISTS = 202, // 接続先マシンがインターネット上に存在しない
    ERR_SSL_X509_UNTRUSTED = 203, // 接続先 X509 証明書が信頼できない
    ERR_SSL_X509_EXPIRED = 204, // 接続先 X509 証明書の有効期限切れ
    ERR_TEMP_ERROR = 205, // 一時的なエラー
    ERR_FUNCTION_NOT_FOUND = 206, // 関数が見つからない
    ERR_PCID_ALREADY_EXISTS = 207, // すでに同一の PCID が使用されている
    ERR_TIMEOUTED = 208, // タイムアウト発生
    ERR_PCID_NOT_FOUND = 209, // PCID が見つからない
    ERR_PCID_RENAME_ERROR = 210, // PCID のリネームエラー
    ERR_SECURITY_ERROR = 211, // セキュリティエラー
    ERR_PCID_INVALID = 212, // PCID に使用できない文字が含まれている
    ERR_PCID_NOT_SPECIFIED = 213, // PCID が指定されていない
    ERR_PCID_TOO_LONG = 214, // PCID が長すぎる
    ERR_SVCNAME_NOT_FOUND = 215, // 指定されたサービス名が見つからない
    ERR_INTERNET_COMM_FAILED = 216, // インターネットとの間の通信に失敗した
    ERR_NO_INIT_CONFIG = 217, // 初期設定が完了していない
    ERR_NO_GATE_CAN_ACCEPT = 218, // 接続を受け付けることができる Gate が存在しない
    ERR_GATE_CERT_ERROR = 219, // Gate 証明書エラー
    ERR_RECV_URL = 220, // URL を受信
    ERR_PLEASE_WAIT = 221, // しばらくお待ちください
    ERR_RESET_CERT = 222, // 証明書をリセットせよ
    ERR_TOO_MANY_CLIENTS = 223, // 接続クライアント数が多すぎる
    ERR_RETRY_AFTER_15_MINS = 240, // 15 分後に再試行してください
    ERR_RETRY_AFTER_1_HOURS = 241, // 1 時間後に再試行してください
    ERR_RETRY_AFTER_8_HOURS = 242, // 8 時間後に再試行してください
    ERR_RETRY_AFTER_24_HOURS = 243, // 24 時間後に再試行してください
    ERR_RECV_MSG = 251, // メッセージを受信
    ERR_WOL_TARGET_NOT_ENABLED = 252, // WoL ターゲット機能無効
    ERR_WOL_TRIGGER_NOT_ENABLED = 253, // WoL トリガー機能無効
    ERR_WOL_TRIGGER_NOT_SUPPORTED = 254, // WoL トリガー機能がサポートされていない
    ERR_REG_PASSWORD_EMPTY = 255, // 登録用パスワードが未設定である
    ERR_REG_PASSWORD_INCORRECT = 256, // 登録用パスワードが誤っている
    ERR_GATE_SYSTEM_INTERNAL_PROXY = 257, // ゲートウェイ <--> 中間プロキシサーバー <--> コントローラ 間の通信が不良である
    ERR_NOT_LGWAN = 258, // LGWAN 上の PC でない
    ERR_WG_TOO_MANY_SESSIONS = 259, // スタンドアロンモード Gate のセッション数が多すぎる
    ERR_WG_NO_SMTP_SERVER_CONFIG = 260, // SMTP サーバーの設定がされていません
    ERR_WG_SMTP_ERROR = 261, // SMTP エラーが発生
    ERR_DESK_VERSION_DIFF = 300, // サービスと設定ツールのバージョンが違う
    ERR_DESK_RPC_CONNECT_FAILED = 301, // サービスに接続できない
    ERR_DESK_RPC_PROTOCOL_ERROR = 302, // RPC プロトコルエラー
    ERR_DESK_URDP_DESKTOP_LOCKED = 303, // デスクトップがロックされている
    ERR_DESK_NOT_ACTIVE = 304, // 接続を受け付けていない
    ERR_DESK_URDP_START_FAILED = 306, // URDP の起動に失敗した
    ERR_DESK_FAILED_TO_CONNECT_PORT = 307, // ポートへの接続に失敗した
    ERR_DESK_LOCALHOST = 308, // localhost に対して接続しようとした
    ERR_DESK_UNKNOWN_AUTH_TYPE = 309, // 不明な認証方法
    ERR_DESK_LISTENER_OPEN_FAILED = 310, // Listen ポートを開けない
    ERR_DESK_RDP_NOT_ENABLED_XP = 311, // RDP が無効である (Windows XP)
    ERR_DESK_RDP_NOT_ENABLED_VISTA = 312, // RDP が無効である (Windows Vista)
    ERR_DESK_MSTSC_DOWNLOAD_FAILED = 313, // mstsc ダウンロード失敗
    ERR_DESK_MSTSC_INSTALL_FAILED = 314, // mstsc インストール失敗
    ERR_DESK_FILE_IS_NOT_MSTSC = 315, // ファイルは mstsc でない
    ERR_DESK_RDP_NOT_ENABLED_2000 = 316, // RDP が無効である (Windows 2000)
    ERR_DESK_BAD_PASSWORD = 317, // 入力されたパスワードが間違っている
    ERR_DESK_PROCESS_EXEC_FAILED = 318, // 子プロセス起動失敗
    ERR_DESK_DIFF_ADMIN = 319, // 管理者ユーザー名が違う
    ERR_DESK_DONT_USE_RDP_FILE = 320, // .rdp ファイルを指定しないでください
    ERR_DESK_RDP_FILE_WRITE_ERROR = 321, // .rdp ファイルに書き込めない
    ERR_DESK_NEED_WINXP = 322, // Windows XP 以降が必要
    ERR_DESK_PASSWORD_NOT_SET = 323, // パスワード未設定
    ERR_DESK_OTP_INVALID = 324, // OTP 間違い
    ERR_DESK_OTP_ENFORCED_BUT_NO = 325, // OTP がポリシー強制なのに設定されていてない
    ERR_DESK_INSPECTION_AVS_ERROR = 326, // 検疫 AVS エラー
    ERR_DESK_INSPECTION_WU_ERROR = 327, // 検疫 Windows Update エラー
    ERR_DESK_INSPECTION_MAC_ERROR = 328, // MAC エラー
    ERR_DESK_SERVER_ALLOWED_MAC_LIST = 329, // SERVER_ALLOWED_MAC_LIST に該当するものがない
    ERR_DESK_AUTH_LOCKOUT = 330, // 認証ロックアウト
    ERR_DESK_GUACD_START_ERROR = 331, // Guacd 起動失敗
    ERR_DESK_GUACD_NOT_SUPPORTED_OS = 332, // Guacd がサポートされていない OS
    ERR_DESK_GUACD_PROHIBITED = 333,        // Guacd が禁止されている
    ERR_DESK_GUACD_NOT_SUPPORTED_VER = 334, // Guacd のバージョンが古い
    ERR_DESK_GUACD_CLIENT_REQUIRED = 335, // Guacd クライアントが必要である
    ERR_DESK_GOVFW_HTML5_NO_SUPPORT = 336, // 完全閉域化 FW が強制されているが HTML5 版でサポートされていない
}


