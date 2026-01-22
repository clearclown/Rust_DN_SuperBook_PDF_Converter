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
using System.IO;
using System.Net;
using System.Linq;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Hosting;
using System.Globalization;
using Microsoft.AspNetCore.Http.Extensions;
using System.Security.Policy;
using System.Diagnostics.Eventing.Reader;
using Org.BouncyCastle.Ocsp;
using IPA.Cores.Basic.HttpClientCore;
using Org.BouncyCastle.Asn1;

#if CORES_BASIC_HTTPSERVER
// ASP.NET Core 3.0 用の型名を無理やり ASP.NET Core 2.2 でコンパイルするための型エイリアスの設定
using IWebHostEnvironment = Microsoft.AspNetCore.Hosting.IHostingEnvironment;
using IHostApplicationLifetime = Microsoft.AspNetCore.Hosting.IApplicationLifetime;
#endif

namespace IPA.Cores.Basic;

// _secure.json ファイルの定義
public class LogBrowserSecureJson : INormalizable
{
    public bool AuthRequired;
    public KeyValueList<string, string> AuthDatabase = null!;
    public string AuthSubject = null!;
    public DateTimeOffset Expires = Util.MaxDateTimeOffsetValue;
    public string AuthSubDirName = "";
    public bool DisableAccessLog = false;
    public bool AllowAccessToAccessLog = false;
    public bool AllowOnlyOnce = false;
    public int NumFiles = 0;
    public long TotalFileSize = 0;

    public bool IsInbox = false;
    public string InboxUploadPassword = "";
    public bool InboxForcePrefixYymmdd = false;
    public string InboxEmail = "";
    public string InboxIpAcl = "";

    public DateTimeOffset UploadTimeStamp = Util.ZeroDateTimeOffsetValue;

    public string UploadIp = "";
    public string? OnlyOnceDownloadIp = null;
    public DateTimeOffset? OnlyOnceDownloadExpires = null;

    public bool AllowZipDownload = false;

    public string DeleteCode = "";
    public bool IsDeleted = false;
    public DateTimeOffset DeletedTimeStamp = Util.ZeroDateTimeOffsetValue;
    public string DeleteIp = "";

    public void Normalize()
    {
        if (this.AuthDatabase == null) this.AuthDatabase = new KeyValueList<string, string>();
        this.AuthSubject = this.AuthSubject._NonNullTrimSe();
        if (this.Expires._IsZeroDateTime()) this.Expires = Util.MaxDateTimeOffsetValue;
        if (this.AuthSubDirName._IsEmpty()) this.AuthSubDirName = "auth";
        this.AuthSubDirName = this.AuthSubDirName._MakeSafeFileName();
        this.InboxIpAcl = EasyIpAcl.NormalizeRules(this.InboxIpAcl);
    }
}

// アクセスログ
public class LogBrowserAccessLog
{
    public DateTimeOffset Timestamp;
    public string? IP;
    public int Port;
    public string? Url;
    public string? PhysicalPath;
    public string? UserAgent;
    public string? Referer;
    public string? Username;
    public string? Password;
    public int StatusCode;
    public bool? PasswordMatched;
}

// LogBrowser を含んだ HttpServer のオプション
public class LogBrowserHttpServerOptions
{
    public LogBrowserOptions LogBrowserOptions { get; }
    public string AbsolutePrefixPath { get; }

    public LogBrowserHttpServerOptions(LogBrowserOptions options, string absolutePrefixPath)
    {
        if (options == null) throw new ArgumentNullException(nameof(options));

        this.LogBrowserOptions = options;
        this.AbsolutePrefixPath = absolutePrefixPath;
    }
}

// LogBrowser の動作フラグ
[Flags]
public enum LogBrowserFlags
{
    None = 0,
    NoPreview = 1, // プレビューボタンなし
    NoRootDirectory = 2, // 最上位ディレクトリへのアクセス不可
    SecureJson = 4, // _secure.json ファイルを必要とし、色々なセキュリティ処理を実施する
    NoZipDownload = 8, // ZIP ダウンロードを禁止
    SecureJson_FlatDir = 16, // SecureJson が ON の場合、通常は日付ごとの 2 階層ディレクトリ構造になるが、この構造をなくしてフラットにする
}

// LogBrowser のオプション
public class LogBrowserOptions
{
    public DirectoryPath RootDir { get; }
    public string SystemTitle { get; private set; }
    public long TailSize { get; }
    public Func<IPAddress, bool> ClientIpAcl { get; }
    public LogBrowserFlags Flags { get; }
    public string ZipEncryptPassword { get; }
    public StatMan? Stat { get; }
    public bool RegardAllLogFilesUtf8 { get; }
    public IEnumerable<string> ExtsAsMimeTypeUtf8 { get; }
    public long LogFileMaxSizePerDir { get; }

    public LogBrowserOptions(DirectoryPath rootDir,
        string systemTitle = Consts.Strings.LogBrowserDefaultSystemTitle,
        long tailSize = Consts.Numbers.LogBrowserDefaultTailSize,
        Func<IPAddress, bool>? clientIpAcl = null,
        LogBrowserFlags flags = LogBrowserFlags.None,
        string? zipEncryptPassword = null,
        StatMan? stat = null,
        bool regardAllLogFilesUtfs = false,
        IEnumerable<string>? extsAsMimeTypeUtf8 = null,
        long logFileMaxSizePerDir = 0
        )
    {
        this.SystemTitle = systemTitle._FilledOrDefault(Consts.Strings.LogBrowserDefaultSystemTitle);
        this.RootDir = rootDir;
        this.TailSize = tailSize._Max(1);
        this.RegardAllLogFilesUtf8 = regardAllLogFilesUtfs;

        // デフォルト ACL はすべて通す
        if (clientIpAcl == null) clientIpAcl = (ip) => true;

        this.ClientIpAcl = clientIpAcl;
        this.Flags = flags;

        this.ZipEncryptPassword = zipEncryptPassword._NonNull();

        this.Stat = stat;

        if (extsAsMimeTypeUtf8 == null)
        {
            this.ExtsAsMimeTypeUtf8 = new List<string>();
        }
        else
        {
            this.ExtsAsMimeTypeUtf8 = extsAsMimeTypeUtf8;
        }

        this.LogFileMaxSizePerDir = logFileMaxSizePerDir;
    }

    public void SetSystemTitle(string title)
    {
        this.SystemTitle = title;
    }
}

class LogBrowserSecureJsonRewriteFileSystem : RewriteFileSystem
{
    public LogBrowserFlags Flags { get; }

    public LogBrowserSecureJsonRewriteFileSystem(RewriteFileSystemParam param, LogBrowserFlags flags) : base(param)
    {
        this.Flags = flags;
    }

    protected override string MapPathVirtualToPhysicalImpl(string relativeSafeUnderlayFsStyleVirtualPath)
    {
        if (relativeSafeUnderlayFsStyleVirtualPath == "") return "/";

        if (this.Flags.Bit(LogBrowserFlags.SecureJson_FlatDir) == false)
        {
            // yymmdd_aaa を /yymmdd/yymmdd_aaa に変換する
            if (relativeSafeUnderlayFsStyleVirtualPath.Length >= 7 && relativeSafeUnderlayFsStyleVirtualPath[6] == '_')
            {
                var yymmdd = relativeSafeUnderlayFsStyleVirtualPath._SliceHead(6);

                if (yymmdd.All(x => (x >= '0' && x <= '9')))
                {
                    return "/" + yymmdd + "/" + relativeSafeUnderlayFsStyleVirtualPath;
                }
            }

            throw new CoresLibException($"relativeSafeUnderlayFsStyleVirtualPath is incorrect: '{relativeSafeUnderlayFsStyleVirtualPath}'");
        }
        else
        {
            // フラット構造
            return "/" + relativeSafeUnderlayFsStyleVirtualPath;
        }
    }

    protected override string MapPathPhysicalToVirtualImpl(string underlayFsStylePhysicalPath)
    {
        if (underlayFsStylePhysicalPath == "/") return "";

        if (this.Flags.Bit(LogBrowserFlags.SecureJson_FlatDir) == false)
        {
            // /yymmdd/yymmdd_aaa を yymmdd_aaa に変換する
            if (underlayFsStylePhysicalPath[0] == '/')
            {
                underlayFsStylePhysicalPath = underlayFsStylePhysicalPath._Slice(1);

                if (underlayFsStylePhysicalPath.Length >= 7 && underlayFsStylePhysicalPath[6] == '/')
                {
                    var yymmdd = underlayFsStylePhysicalPath._SliceHead(6);

                    if (yymmdd.All(x => (x >= '0' && x <= '9')))
                    {
                        return underlayFsStylePhysicalPath._Slice(7);
                    }
                }
            }

            throw new CoresLibException($"underlayFsStylePhysicalPath is incorrect: '{underlayFsStylePhysicalPath}'");
        }
        else
        {
            // フラット構造
            if (underlayFsStylePhysicalPath[0] == '/')
            {
                underlayFsStylePhysicalPath = underlayFsStylePhysicalPath._Slice(1);

                return underlayFsStylePhysicalPath;
            }
            throw new CoresLibException($"underlayFsStylePhysicalPath is incorrect: '{underlayFsStylePhysicalPath}'");
        }
    }
}

// 汎用的に任意の Kestrel App から利用できる LogBrowser
public class LogBrowser : AsyncService
{
    public LogBrowserOptions Options { get; }

    public FileSystem RootFs;

    public string AbsolutePathPrefix { get; }

    readonly NamedAsyncLocks AccessLogLock = new NamedAsyncLocks(StrComparer.IgnoreCaseTrimComparer);

    public LogBrowser(LogBrowserOptions options, string absolutePathPrefix = "")
    {
        if (absolutePathPrefix._IsFilled())
        {
            if (absolutePathPrefix.StartsWith("/") == false)
            {
                throw new ArgumentException("Must be an absolute URL.", nameof(absolutePathPrefix));
            }
        }
        this.AbsolutePathPrefix = absolutePathPrefix._NonNull();

        this.Options = options;

        this.RootFs = new ChrootFileSystem(new ChrootFileSystemParam(Options.RootDir.FileSystem, Options.RootDir.PathString, FileSystemMode.Writeable));

        if (this.Options.Flags.Bit(LogBrowserFlags.SecureJson))
        {
            this.RootFs = new LogBrowserSecureJsonRewriteFileSystem(new RewriteFileSystemParam(this.RootFs, disposeUnderlay: true), this.Options.Flags);
        }
    }

    public async Task GetRequestHandlerAsync(HttpRequest request, HttpResponse response, RouteData routeData)
    {
        CancellationToken cancel = request._GetRequestCancellationToken();

        await using (HttpResult result = await ProcessRequestAsync(request.HttpContext.Connection.RemoteIpAddress!._UnmapIPv4(),
            request.HttpContext.Connection.RemotePort,
            request._GetRequestPathAndQueryString(),
            request,
            response,
            cancel
            ))
        {
            await response._SendHttpResultAsync(result, cancel);
        }
    }

    // アクセスログ記録指令
    class InternalAccessLogResults
    {
        public LogBrowserAccessLog AccessLogData = new LogBrowserAccessLog() { Timestamp = DateTimeOffset.Now };

        public string? PhysicalAccessLogDirPath; // アクセスログを書き込む際には指定すること
    }

    public async Task<HttpResult> ProcessRequestAsync(IPAddress clientIpAddress, int clientPort, string requestPathAndQueryString, HttpRequest request, HttpResponse response, CancellationToken cancel = default)
    {
        InternalAccessLogResults alog = new InternalAccessLogResults();

        try
        {
            HttpResult result = await ProcessRequestInternalAsync(clientIpAddress, clientPort, requestPathAndQueryString, request, response, cancel, alog);

            alog.AccessLogData.StatusCode = result.StatusCode;

            return result;
        }
        finally
        {
            try
            {
                await WriteAccessLogAsync(alog.PhysicalAccessLogDirPath, alog.AccessLogData, alog.AccessLogData.Timestamp, cancel);
            }
            catch (Exception ex)
            {
                ex._Error();
            }
        }
    }

    public async Task WriteAccessLogAsync(string? alogDir, object? alogData, DateTimeOffset timeStamp, CancellationToken cancel = default)
    {
        // アクセスログ保存
        if (alogDir._IsFilled())
        {
            using (await AccessLogLock.LockWithAwait(alogDir, cancel))
            {
                // ファイル名を決定
                string filename = timeStamp.ToString("yyyyMMdd") + ".log";

                string filepath = RootFs.PathParser.Combine(alogDir, filename);

                // アクセスログを JSON 文字列に変更
                string json = alogData._ObjectToJson(compact: true) + "\r\n";

                // すでにファイルが存在するか?
                bool isFileExists = await RootFs.IsFileExistsAsync(filepath, cancel);

                // 書き込み
                await RootFs.AppendStringToFileAsync(filepath, json, FileFlags.AutoCreateDirectory, writeBom: true, cancel: cancel);

                if (isFileExists == false)
                {
                    // 新しいファイルを作成したところである。
                    // このディレクトリの古いログを自動消去する。
                    if (this.Options.LogFileMaxSizePerDir > 0)
                    {
                        await RootFs.DeleteOldFilesAsync(alogDir, ".log", this.Options.LogFileMaxSizePerDir, cancel);
                    }
                }
            }
        }
    }

    async Task<HttpResult> ProcessRequestInternalAsync(IPAddress clientIpAddress, int clientPort, string requestPathAndQueryString, HttpRequest request, HttpResponse response, CancellationToken cancel,
        InternalAccessLogResults accessLogResults)
    {
        Options.Stat?.AddReport("DownloadTryAccess_Total", 1);

        string footer = "";

        clientIpAddress = clientIpAddress._UnmapIPv4();

        var alog = accessLogResults.AccessLogData;

        alog.IP = clientIpAddress.ToString();
        alog.Port = clientPort;
        alog.Url = request.GetEncodedUrl();
        alog.UserAgent = request.Headers[Consts.HttpHeaders.UserAgent];
        alog.Referer = request.Headers[Consts.HttpHeaders.Referer];

        alog.Url._ParseUrl(out Uri fullUri, out _);

        Ref<string> suppliedPassword = "";

        try
        {
            // クライアント IP による ACL のチェック
            if (Options.ClientIpAcl(clientIpAddress) == false)
            {
                // ACL error
                return new HttpStringResult("403 Forbidden", statusCode: 403);
            }

            // URL のチェック
            requestPathAndQueryString._ParseUrl(out Uri uri, out QueryStringList qsList);

            string physicalPath;
            string? logicalPath = null;

            if (this.AbsolutePathPrefix._IsFilled())
            {
                // AbsolutePathPrefix の検査
                if (uri.AbsolutePath._TryTrimStartWith(out physicalPath, StringComparison.OrdinalIgnoreCase, this.AbsolutePathPrefix) == false)
                {
                    // Not found
                    return new HttpStringResult("404 Not Found", statusCode: 404);
                }
            }
            else
            {
                physicalPath = uri.AbsolutePath;
            }

            if (physicalPath.StartsWith("/") == false) physicalPath = "/" + physicalPath;

            if (physicalPath._IsDangerousDirectoryTraversal())
            {
                // ディレクトリトラバーサルの恐れあり
                return new HttpStringResult("403 Forbidden", statusCode: 403);
            }

            physicalPath = PathParser.Linux.NormalizeUnixStylePathWithRemovingRelativeDirectoryElements(physicalPath);

            physicalPath = physicalPath._DecodeUrlPath();

            if (physicalPath._IsDangerousDirectoryTraversal())
            {
                // ディレクトリトラバーサルの恐れあり
                return new HttpStringResult("403 Forbidden", statusCode: 403);
            }

            if (this.Options.Flags.Bit(LogBrowserFlags.NoRootDirectory) && PathParser.Linux.IsRootDirectory(physicalPath))
            {
                // トップディレクトリはアクセス不能
                return new HttpStringResult("403 Forbidden", statusCode: 403);
            }

            LogBrowserSecureJson? secureJson = null;
            bool isAccessToAccessLog = false;
            bool isZipDownloadRequest = false;
            string zipDownloadRoot = "";
            string? secureJsonPath = null;
            string zipFileName = "";

            if (this.Options.Flags.Bit(LogBrowserFlags.SecureJson))
            {
                // _secure.json ファイルの読み込みを試行する
                string[] dirNames = PathParser.Linux.SplitAbsolutePathToElementsUnixStyle(physicalPath);
                string firstDirName = "/" + dirNames[0];
                zipDownloadRoot = "/" + dirNames[0];

                secureJsonPath = PathParser.Linux.Combine(firstDirName, Consts.FileNames.LogBrowserSecureJson);

                // このファイルはあるかな?
                secureJson = secureJsonPath._FileToObject<LogBrowserSecureJson>(RootFs, cancel: cancel, nullIfError: true);

                if (secureJson == null)
                {
                    // _secure.json がありません！！
                    return new HttpStringResult("404 Not Found", statusCode: 404);
                }

                secureJson.Normalize();

                if (secureJson.DisableAccessLog == false)
                {
                    // アクセスログを書き込む指令をいたします
                    accessLogResults.PhysicalAccessLogDirPath = PathParser.Linux.Combine(firstDirName, Consts.FileNames.LogBrowserAccessLogDirName);
                }

                bool isEncrypted = false;

                if (secureJson.AuthRequired)
                {
                    // 認証が必要とされている場合、パスが  /任意の名前/<authsubdirname>/ である場合は Basic 認証を経由して実ファイルにアクセスさせる。
                    // それ以外の場合は、認証案内ページを出す。
                    var thisDirFullUrl = new Uri(fullUri, this.AbsolutePathPrefix + firstDirName + "/");
                    var authFullUrl = new Uri(thisDirFullUrl, secureJson.AuthSubDirName + "/");

                    isEncrypted = true;

                    if (dirNames.ElementAtOrDefault(1)._IsSamei(secureJson.AuthSubDirName))
                    {
                        // 認証を実施する
                        var authResult = await BasicAuthImpl.TryAuthenticateAsync(request, (username, password) =>
                        {
                            alog.Username = username;
                            alog.Password = Str.MakeCharArray('*', password.Length);

                            // ユーザー名とパスワードが一致するものが 1 つ以上あるかどうか
                            bool ok = secureJson.AuthDatabase
                            .Where(db => db.Key._IsFilled() && db.Value._IsFilled())
                            .Where(db => db.Key._IsSamei(username) && Secure.VeritySaltedPassword(db.Value, password))
                            .Any();

                            alog.PasswordMatched = ok;

                            suppliedPassword = password;

                            return TR(ok);
                        });

                        if (authResult.IsError)
                        {
                            // 認証失敗
                            KeyValueList<string, string> headers = new KeyValueList<string, string>();
                            headers.Add(Consts.HttpHeaders.WWWAuthenticate, $"Basic realm=\"User Authentication for {firstDirName._MakeSafePath(PathParser.Linux) + "/."}\"");

                            // 認証案内を出す
                            string htmlBody = BuildAuthRequiredHtml(new DirectoryPath(physicalPath, RootFs), fullUri.ToString(), secureJson);

                            return new HttpStringResult(htmlBody, contentType: Consts.MimeTypes.HtmlUtf8, statusCode: Consts.HttpStatusCodes.Unauthorized, additionalHeaders: headers);
                        }

                        // 認証成功

                        // パスをリライト (1 階層減らす) する
                        physicalPath = "/" + dirNames.Where((s, index) => index != 1)._Combine("/");

                        logicalPath = firstDirName + "/" + secureJson.AuthSubDirName + "/" + dirNames.Skip(2)._Combine("/");
                    }
                    else
                    {
                        // 認証案内を出す
                        string htmlBody = BuildAuthRequiredHtml(new DirectoryPath(physicalPath, RootFs), authFullUrl.ToString(), secureJson);

                        return new HttpStringResult(htmlBody, contentType: Consts.MimeTypes.HtmlUtf8);
                    }
                }

                // アクセスログディレクトリに対するアクセスかどうかを検査
                if (dirNames.ElementAtOrDefault(secureJson.AuthRequired ? 2 : 1)._IsSameTrimi(Consts.FileNames.LogBrowserAccessLogDirName))
                {
                    // アクセスログディレクトリに対するアクセスである
                    if (secureJson.AllowAccessToAccessLog == false)
                    {
                        // アクセスログディレクトリに対するアクセスは禁止されている
                        return new HttpStringResult("403 Forbidden", statusCode: 403);
                    }

                    // アクセスログディレクトリに対するアクセスはログを記録しない
                    accessLogResults.PhysicalAccessLogDirPath = "";

                    isAccessToAccessLog = true;
                }

                // ZIP ファイルとしてダウンロードすることを要求しているアクセスかどうかを検査
                if (dirNames.LastOrDefault()._IsSameTrimi(Consts.FileNames.LogBrowserZipFileName))
                {
                    // ZIP ファイルとしてのアクセスである
                    if (secureJson.AllowZipDownload == false)
                    {
                        // ZIP ファイルとしてのアクセスは禁止されている
                        return new HttpStringResult("403 Forbidden", statusCode: 403);
                    }

                    zipDownloadRoot = "/" + dirNames[0] + "/" + dirNames.Skip(secureJson.AuthRequired ? 2 : 1).SkipLast(1)._Combine("/");

                    isZipDownloadRequest = true;

                    string fn1 = dirNames.FirstOrDefault()._FilledOrDefault("unknown").Trim();
                    string fn2 = dirNames.SkipLast(1).LastOrDefault()._FilledOrDefault("unknown").Trim();

                    fn1 = PPWin.MakeSafeFileName(fn1);
                    fn2 = PPWin.MakeSafeFileName(fn2);

                    string fntmp = fn1 + "_" + fn2;

                    if (fn1._IsSamei(fn2))
                    {
                        fntmp = fn1 + "_root";
                    }

                    zipFileName = PPWin.MakeSafeFileName(fntmp + "_" + Str.DateTimeToStrShort(DtNow).Substring(2) + Consts.Extensions.Zip);
                }

                if ((secureJson.AllowAccessToAccessLog && secureJson.DisableAccessLog == false) || (secureJson.AllowZipDownload))
                {
                    // フッターにアクセスログ等へのリンクを付ける
                    footer += "<hr>";

                    if (secureJson.AllowZipDownload)
                    {
                        string s1 = dirNames.Take(secureJson.AuthRequired ? 2 : 1)._Combine("/");
                        string s2 = dirNames._Combine("/");

                        bool isRoot = s1._IsSamei(s2);

                        if (isRoot)
                        {
                            footer += $"<p><a href=\"{this.AbsolutePathPrefix}/{dirNames.Take(secureJson.AuthRequired ? 2 : 1)._Combine("/")._EncodeHtml()}/{Consts.FileNames.LogBrowserZipFileName}/\"><strong><i class=\"fas fa-download\"></i> ZIP ファイルとしてルートディレクトリからの全ファイルをまとめてダウンロード</strong></a></p>";
                        }
                        else
                        {
                            footer += $"<p><a href=\"{this.AbsolutePathPrefix}/{dirNames._Combine("/")._EncodeHtml()}/{Consts.FileNames.LogBrowserZipFileName}/\"><strong><i class=\"fas fa-download\"></i> ZIP ファイルとしてこのサブディレクトリ \"/{dirNames._Combine("/")._EncodeHtml()}/\" からの全ファイルをまとめてダウンロード</strong></a></p>";
                        }
                    }

                    if (secureJson.AllowAccessToAccessLog && secureJson.DisableAccessLog == false)
                    {
                        footer += $"<p><a href=\"{this.AbsolutePathPrefix}/{dirNames.Take(secureJson.AuthRequired ? 2 : 1)._Combine("/")._EncodeHtml()}/{Consts.FileNames.LogBrowserAccessLogDirName}/\"><strong><i class=\"far fa-eye\"></i> アクセスログの参照</strong></a></p>";
                    }
                }

                if (isEncrypted && secureJson.IsInbox == false && secureJson.AllowZipDownload == false)
                {
                    // 暗号化されている案内を出す
                    footer += $@"
        <hr>
        <p><i class=""fas fa-shield-alt""></i>&nbsp;このディレクトリのファイルは、Web サーバー上で XTS-AES256 で強力に透過的暗号化されています。<BR>
        (XTS-AES256 の暗号鍵は、認証時の非常に長いパスワード文字列を利用しています。)<BR>
        ファイルへのアクセスの都度、サーバー上でメモリ上で一時的・透過的に解読されます。<BR>
        したがって、たとえサーバーや VM、クラウドシステムの管理者 (root) であっても、アクセス用パスワードを知らない限り、ファイルの内容にアクセスすることはできません。</p>
";
                }

                footer += $"<HR><p>IPA FileCenter Uploader: <a href='https://github.com/IPA-CyberLab/IPA-DN-FileCenter'>オープンソース GitHub ソースコード</a> | <a href='https://github.com/IPA-CyberLab/IPA-DN-FileCenter/blob/master/LICENSE'>使用条件</a> | Copyright &copy; {DateTime.Now.Year.ToString()} IPA 産業サイバーセキュリティセンター サイバー技術研究室. All rights reserved.</p>";
            }
            else
            {
                if (this.Options.Flags.Bit(LogBrowserFlags.NoZipDownload) == false)
                {
                    // 通常モード ZIP ダウンロードを許可
                    footer += "<hr>";

                    footer += $"<p><a href=\"./?zip=1\"><strong><i class=\"fas fa-download\"></i> ZIP ファイルとしてこのディレクトリ以下をまとめてダウンロード</strong></a></p>";
                }
            }

            if (logicalPath._IsEmpty()) logicalPath = physicalPath;

            string? encryptedRawPhysicalPath = null;

            if (this.Options.Flags.Bit(LogBrowserFlags.SecureJson))
            {
                if (await RootFs.IsDirectoryExistsAsync(physicalPath, cancel) == false)
                {
                    if (await RootFs.IsFileExistsAsync(physicalPath, cancel) == false)
                    {
                        string tmp = physicalPath + Consts.Extensions.EncryptedXtsAes256;
                        if (await RootFs.IsFileExistsAsync(tmp, cancel))
                        {
                            encryptedRawPhysicalPath = tmp;
                        }
                    }
                }
            }


            if (await RootFs.IsDirectoryExistsAsync(physicalPath, cancel))
            {
                // Directory
                if (qsList._GetStrFirst("zip")._ToBool() && this.Options.Flags.Bit(LogBrowserFlags.SecureJson) == false && this.Options.Flags.Bit(LogBrowserFlags.NoZipDownload) == false)
                {
                    string dirtmp = physicalPath._NonNullTrim();
                    if (dirtmp.StartsWith("/"))
                    {
                        dirtmp = dirtmp.Substring(1);
                    }

                    if (dirtmp._IsEmpty())
                    {
                        dirtmp = "root";
                    }
                    else
                    {
                        dirtmp = PPWin.MakeSafeFileName(dirtmp);
                    }

                    string zip_fn = $"alldir_{Str.DateTimeToStrShort(DtNow).Substring(2)}_{fullUri.Host._MakeSafeFileName()._NonNullTrim()}_{dirtmp}{Consts.Extensions.Zip}".ToLowerInvariant();

                    // ZIP ファイルとしてまとめてダウンロード
                    PipeStreamPairWithSubTask streamPair = new PipeStreamPairWithSubTask(async (writeMe) =>
                    {
                        await using WriteOnlyStreamBasedRandomAccess r = new WriteOnlyStreamBasedRandomAccess(writeMe);

                        await using ZipWriter zipWriter = new ZipWriter(new ZipContainerOptions(r), cancel);

                        long totalSize = await zipWriter.ImportDirectoryAsync(new DirectoryPath(physicalPath, RootFs),
                            new FileContainerEntityParam(flags: FileContainerEntityFlags.FileNameUseShiftJisIfPossible | FileContainerEntityFlags.EnableCompression | FileContainerEntityFlags.CompressionMode_Fast,
                            encryptPassword: this.Options.ZipEncryptPassword),
                            exceptionHandler: (pathInfo, exception, cancel) =>
                            {
                                exception._Error();
                                return true._TaskResult();
                            },
                            cancel: cancel);

                        Options.Stat?.AddReport("DownloadDataSize_Total", totalSize);
                        Options.Stat?.AddReport("DownloadZipCount_Total", 1);

                        await zipWriter.FinishAsync(cancel);
                    });

                    List<KeyValuePair<string, string>> headers = new List<KeyValuePair<string, string>>();

                    headers.Add(new KeyValuePair<string, string>(Consts.HttpHeaders.ContentDisposition, $"attachment; filename=\"{zip_fn}\""));

                    return new HttpResult(streamPair.StreamA, 0, null, Consts.MimeTypes.Zip, additionalHeaders: headers, onDisposeAsync: () => streamPair._DisposeSafeAsync2());
                }

                string htmlBody = await BuildDirectoryHtmlAsync(new DirectoryPath(physicalPath, RootFs), logicalPath, footer, (secureJson?.IsDeleted ?? false) && isAccessToAccessLog == false, cancel);

                Options.Stat?.AddReport("DownloadBrowseDirectory_Total", 1);

                return new HttpStringResult(htmlBody, contentType: Consts.MimeTypes.HtmlUtf8);
            }
            else if (encryptedRawPhysicalPath._IsFilled() || await RootFs.IsFileExistsAsync(physicalPath, cancel) || isZipDownloadRequest)
            {
                // File
                if (this.Options.Flags.Bit(LogBrowserFlags.SecureJson) && RootFs.PathParser.GetFileName(physicalPath).StartsWith(Consts.FileNames.LogBrowserSecureJson, StringComparison.OrdinalIgnoreCase))
                {
                    // _secure.json そのものにはアクセスできません
                    return new HttpStringResult("403 Forbidden", statusCode: 403);
                }

                if (secureJson != null && secureJson.IsDeleted && isAccessToAccessLog == false)
                {
                    return new HttpStringResult("404 Files are deleted by the administrator", statusCode: 404);
                }

                if (secureJson != null && secureJson.AllowOnlyOnce && isAccessToAccessLog == false)
                {
                    bool downloadBySelf = false;

                    if (secureJson.UploadIp._IsSamei(clientIpAddress.ToString()))
                    {
                        // アップロードした本人
                        downloadBySelf = true;
                    }

                    // Only Once 処理
                    if (downloadBySelf == false)
                    {
                        if (secureJson.OnlyOnceDownloadIp._IsEmpty())
                        {
                            // 初回のダウンロードである。IP アドレスの書き込みをし、カウントダウンを開始する。
                            secureJson.OnlyOnceDownloadIp = clientIpAddress.ToString();
                            secureJson.OnlyOnceDownloadExpires = DateTimeOffset.Now.AddHours(1); // 1 時間のカウントダウン開始

                            // ファイル保存
                            secureJsonPath._NullCheck();

                            await RootFs.WriteJsonToFileAsync(secureJsonPath, secureJson, cancel: cancel);
                        }
                    }

                    if (downloadBySelf || secureJson.OnlyOnceDownloadIp._IsSamei(clientIpAddress.ToString()))
                    {
                        // 同じ IP アドレスから 2 回目以降のダウンロードである。
                        // 有効期限チェック
                        if (secureJson.OnlyOnceDownloadExpires.HasValue == false ||
                            secureJson.OnlyOnceDownloadExpires.Value >= DateTimeOffset.Now)
                        {
                            // OK
                        }
                        else
                        {
                            // 有効期限切れ
                            return new HttpStringResult(BuildErrorHtml(secureJson,
                                $"このファイルは、IP アドレス '{secureJson.OnlyOnceDownloadIp}' から最初にダウンロードされてから 60 分間のみダウンロード可能に設定されています。ファイルのダウンロード可能な有効期限が切れています。有効期限: {secureJson.OnlyOnceDownloadExpires?._ToDtStr() ?? "Unknown"}",
                                new DirectoryPath(physicalPath, RootFs)
                                ), contentType: Consts.MimeTypes.HtmlUtf8, statusCode: 403);
                        }
                    }
                    else
                    {
                        // IP 違い
                        return new HttpStringResult(BuildErrorHtml(secureJson,
                            $"このファイルは、IP アドレス '{secureJson.OnlyOnceDownloadIp}' からすでにダウンロードされています。あなたは、異なる IP アドレス '{clientIpAddress}' であるため、ダウンロードできません。",
                            new DirectoryPath(physicalPath, RootFs)
                            ), contentType: Consts.MimeTypes.HtmlUtf8, statusCode: 403);
                    }
                }

                if (secureJson != null && secureJson.Expires <= DateTimeOffset.Now)
                {
                    if (isAccessToAccessLog == false)
                    {
                        // 有効期限が切れました
                        return new HttpStringResult(BuildErrorHtml(secureJson,
                            $"ファイルのダウンロード可能な有効期限が切れています。有効期限: {secureJson.Expires._ToDtStr()}",
                            new DirectoryPath(physicalPath, RootFs)
                            ), contentType: Consts.MimeTypes.HtmlUtf8, statusCode: 403);
                    }
                }

                if (isZipDownloadRequest)
                {
                    // ZIP ファイルとしてまとめてダウンロード
                    PipeStreamPairWithSubTask streamPair = new PipeStreamPairWithSubTask(async (writeMe) =>
                    {
                        await using WriteOnlyStreamBasedRandomAccess r = new WriteOnlyStreamBasedRandomAccess(writeMe);

                        await using ZipWriter zipWriter = new ZipWriter(new ZipContainerOptions(r), cancel);

                        long totalSize = await zipWriter.ImportDirectoryAsync(new DirectoryPath(zipDownloadRoot, RootFs),
                            new FileContainerEntityParam(flags: FileContainerEntityFlags.FileNameUseShiftJisIfPossible),
                            fileFilter: e => !PPWin.SplitTokens(e.FullPath).Where(name => Consts.FileNames.IsSpecialFileNameForLogBrowser(name)).Any(),
                            exceptionHandler: (pathInfo, exception, cancel) =>
                            {
                                exception._Error();
                                return true._TaskResult();
                            },
                            cancel: cancel);

                        Options.Stat?.AddReport("DownloadDataSize_Total", totalSize);
                        Options.Stat?.AddReport("DownloadZipCount_Total", 1);

                        await zipWriter.FinishAsync(cancel);
                    });

                    List<KeyValuePair<string, string>> headers = new List<KeyValuePair<string, string>>();

                    headers.Add(new KeyValuePair<string, string>(Consts.HttpHeaders.ContentDisposition, $"attachment; filename=\"{zipFileName}\""));

                    return new HttpResult(streamPair.StreamA, 0, null, Consts.MimeTypes.Zip, additionalHeaders: headers, onDisposeAsync: () => streamPair._DisposeSafeAsync2());
                }

                // 1 ファイルをダウンロード
                string extension = RootFs.PathParser.GetExtension(physicalPath);
                string mimeType = MasterData.ExtensionToMime.Get(extension);

                if (this.Options.Flags.Bit(LogBrowserFlags.SecureJson))
                {
                    if (secureJson!.AuthRequired)
                    {
                        if (extension._IsSamei(".pdf"))
                        {
                            // 2022/3/23 Chrome の不具合により、Basic 認証中の PDF はバイナリファイルとしてダウンロードさせる
                            // (そうしないと、うまく PDF ビューアで表示されない)
                            mimeType = MasterData.ExtensionToMime.Get(".bin");
                        }
                    }
                }

                FileObject file;
                IRandomAccess<byte> randomAccess;
                Stream fileStream;

                if (encryptedRawPhysicalPath._IsEmpty())
                {
                    // 普通のファイル
                    file = await RootFs.OpenAsync(physicalPath, cancel: cancel);
                    try
                    {
                        fileStream = file.GetStream(disposeTarget: true);

                        randomAccess = file;
                    }
                    catch
                    {
                        file._DisposeSafe();

                        throw;
                    }
                }
                else
                {
                    // 暗号化済みファイル
                    file = await RootFs.OpenAsync(encryptedRawPhysicalPath, cancel: cancel);

                    try
                    {
                        randomAccess = new XtsAesRandomAccess(file, suppliedPassword.Value!, disposeObject: true);

                        fileStream = randomAccess.GetStream(disposeTarget: true);
                    }
                    catch
                    {
                        file._DisposeSafe();

                        throw;
                    }
                }

                try
                {
                    long fileSize = fileStream.Length;

                    long head = qsList._GetStrFirst("head")._ToInt()._NonNegative();
                    long tail = qsList._GetStrFirst("tail")._ToInt()._NonNegative();

                    if (this.Options.Flags.Bit(LogBrowserFlags.NoPreview))
                    {
                        // プレビュー機能は禁止です！！
                        head = tail = 0;
                    }

                    if (head != 0 && tail != 0) throw new ApplicationException("You can specify either head or tail.");

                    head = head._Min(fileSize);
                    tail = tail._Min(fileSize);

                    long readStart = 0;
                    long readSize = fileSize;

                    if (head != 0)
                    {
                        readStart = 0;
                        readSize = head;
                    }
                    else if (tail != 0)
                    {
                        readStart = fileSize - tail;
                        readSize = tail;
                    }

                    bool isUtf8 = MasterData.ExtensionToMime.Get(".json")._IsSamei(mimeType); // JSON は必ず UTF-8 である

                    if (this.Options.RegardAllLogFilesUtf8)
                    {
                        if (extension._IsSamei(".log"))
                        {
                            // すべての log ファイルを UTF8 として扱う
                            isUtf8 = true;
                        }
                    }

                    if (tail != 0)
                    {
                        mimeType = Consts.MimeTypes.Text;
                    }

                    if (extension._IsExtensionMatch(this.Options.ExtsAsMimeTypeUtf8))
                    {
                        if (mimeType._InStr(";") == false)
                        {
                            mimeType += "; charset=UTF-8";
                        }
                    }

                    ReadOnlyMemory<byte> preData = new byte[0];

                    if (readSize != 0 && fileSize >= 3)
                    {
                        try
                        {
                            byte[] bom = new byte[3];
                            if (await randomAccess.ReadRandomAsync(0, bom, cancel) == 3 && Str.BOM_UTF_8._MemEquals(bom))
                            {
                                // 元のファイルの先頭に BOM が付いていて、先頭をスキップする場合は、
                                // 応答データに先頭にも BOM を付ける。
                                if (readStart != 0)
                                {
                                    preData = Str.BOM_UTF_8;
                                }
                            }
                            else
                            {
                                // 2020.12.19 元のファイルに BOM がなくても、UTF-8 であることが確実であれば BOM を付ける。
                                if (isUtf8)
                                {
                                    preData = Str.BOM_UTF_8;
                                }
                            }
                        }
                        catch { }
                    }

                    Options.Stat?.AddReport("DownloadFileCount_Total", 1);
                    Options.Stat?.AddReport("DownloadDataSize_Total", readSize);

                    return new HttpResult(fileStream, readStart, readSize, mimeType, preData: preData);
                }
                catch
                {
                    randomAccess._DisposeSafe();
                    fileStream._DisposeSafe();
                    file._DisposeSafe();
                    throw;
                }
            }
            else
            {
                // Not found
                return new HttpStringResult("404 Not Found", statusCode: 404);
            }
        }
        catch (Exception ex)
        {
            return new HttpStringResult($"HTTP Status Code: 500\r\n" + ex.ToString(), statusCode: 500);
        }
    }

    string BuildErrorHtml(LogBrowserSecureJson secureJson, string errorString, DirectoryPath dir)
    {
        string body = CoresRes["LogBrowser/Html/Error.html"].String;

        body = body._ReplaceStrWithReplaceClass(new
        {
            __TITLE__ = $"{this.Options.SystemTitle} - { (dir.IsDirectoryExists() ? RootFs.PathParser.AppendDirectorySeparatorTail(dir.PathString) : dir.PathString)}"._EncodeHtml(true),
            __TITLE2__ = $"{this.Options.SystemTitle} - { (dir.IsDirectoryExists() ? RootFs.PathParser.AppendDirectorySeparatorTail(dir.PathString) : dir.PathString)}"._EncodeHtml(true),
            __ERROR__ = errorString._EncodeHtml(true),
        });

        return body;
    }

    string BuildAuthRequiredHtml(DirectoryPath dir, string targetFullUrl, LogBrowserSecureJson secureJson)
    {
        string body = CoresRes["LogBrowser/Html/Auth.html"].String;

        body = body._ReplaceStrWithReplaceClass(new
        {
            __FULLPATH__ = targetFullUrl._EncodeHtml(true),
            __TITLE__ = $"{this.Options.SystemTitle} - {RootFs.PathParser.AppendDirectorySeparatorTail(dir.PathString)}"._EncodeHtml(true),
            __TITLE2__ = $"{this.Options.SystemTitle} - {RootFs.PathParser.AppendDirectorySeparatorTail(dir.PathString)}"._EncodeHtml(true),
        });

        return body;
    }

    async Task<string> BuildDirectoryHtmlAsync(DirectoryPath dir, string logicalPath, string footer = "", bool empty = false, CancellationToken cancel = default)
    {
        string body = CoresRes["LogBrowser/Html/Directory.html"].String;

        // Enum dir list
        List<FileSystemEntity> list = (await dir.EnumDirectoryAsync(flags: EnumDirectoryFlags.NoGetPhysicalSize | EnumDirectoryFlags.IncludeParentDirectory)).ToList();

        // Bread crumbs
        var breadCrumbList = new DirectoryPath(logicalPath, dir.FileSystem).GetBreadCrumbList();
        StringWriter breadCrumbsHtml = new StringWriter();

        if (this.Options.Flags.Bit(LogBrowserFlags.NoRootDirectory))
        {
            // ルートディレクトリをパン屑リストから消します
            breadCrumbList.RemoveAt(0);
        }

        foreach (var crumb in breadCrumbList)
        {
            var iconInfo = MasterData.GetFasIconFromExtension(Consts.MimeTypes.DirectoryOpening);
            string printName = crumb.GetThisDirectoryName() + "/";
            string classStr = "";
            if (breadCrumbList.LastOrDefault() == crumb)
            {
                classStr = " class=\"is-active\"";
            }

            breadCrumbsHtml.WriteLine($"<li{classStr}><a href=\"{AbsolutePathPrefix}{crumb.PathString._EncodeUrlPath()}\"><span class=\"icon\"><i class=\"{iconInfo.Item1} {iconInfo.Item2}\" aria-hidden=\"true\"></i></span><b>{printName._EncodeHtml(true)}</b></a></li>");
        }

        // File contents
        StringWriter dirHtml = new StringWriter();

        var fileListSorted = list
            .Where(x => empty == false)
            .Where(x => x.Name.IndexOf('\"') == -1)
            .OrderByDescending(x => x.IsCurrentDirectory)
            .ThenByDescending(x => x.IsParentDirectory)
            .ThenByDescending(x => x.IsDirectory)
            .ThenBy(x => x.Name, StrComparer.IgnoreCaseComparer);

        long totalSize = fileListSorted.Where(x => x.IsFile).Sum(x => x.Size);
        int totalFileCount = fileListSorted.Where(x => x.IsFile).Count();

        foreach (FileSystemEntity e in fileListSorted)
        {
            // ルートディレクトリへのリンクは消します
            if (this.Options.Flags.Bit(LogBrowserFlags.NoRootDirectory))
                if (e.IsParentDirectory && RootFs.PathParser.IsRootDirectory(e.FullPath))
                    continue;

            // _secure.json ファイルは非表示とする
            if (this.Options.Flags.Bit(LogBrowserFlags.SecureJson))
                if (e.IsFile && e.Name.StartsWith(Consts.FileNames.LogBrowserSecureJson, StringComparison.OrdinalIgnoreCase))
                    continue;

            // アクセスログフォルダは非表示とする
            if (this.Options.Flags.Bit(LogBrowserFlags.SecureJson))
                if (e.IsDirectory && e.Name._IsSamei(Consts.FileNames.LogBrowserAccessLogDirName))
                    continue;

            string absolutePath = e.FullPath;

            string printFileName = e.Name;
            long printSize = e.Size;
            if (e.IsDirectory)
            {
                printFileName = $"{e.Name}/";
            }

            if (e.IsDirectory == false)
            {
                if (this.Options.Flags.Bit(LogBrowserFlags.SecureJson))
                {
                    if (e.Name.EndsWith(Consts.Extensions.EncryptedXtsAes256, StringComparison.Ordinal))
                    {
                        // 暗号化ファイル
                        printFileName = e.Name.Substring(0, e.Name.Length - Consts.Extensions.EncryptedXtsAes256.Length);

                        absolutePath = RootFs.PathParser.Combine(RootFs.PathParser.GetDirectoryName(e.FullPath), printFileName);

                        // 暗号化ファイルの場合は実際にヘッダを読み取ってみる
                        try
                        {
                            var encryptedParseResult = await XtsAesRandomAccess.TryReadMetaDataAsync(new FilePath(e.FullPath, dir.FileSystem), cancel);
                            if (encryptedParseResult.IsOk)
                            {
                                printSize = encryptedParseResult.Value!.VirtualSize;
                            }
                        }
                        catch { }
                    }
                }
            }

            if (e.IsParentDirectory == false)
            {
                string relativePath = RootFs.PathParser.GetRelativeFileName(absolutePath, dir);
                absolutePath = RootFs.PathParser.Combine(logicalPath, relativePath);
            }
            else
            {
                absolutePath = RootFs.PathParser.NormalizeUnixStylePathWithRemovingRelativeDirectoryElements(RootFs.PathParser.Combine(logicalPath, "../"));
            }

            if (e.IsDirectory && absolutePath.Last() != '/')
            {
                absolutePath += "/";
            }

            string extension = RootFs.PathParser.GetExtension(absolutePath);
            if (e.IsDirectory)
            {
                extension = Consts.MimeTypes.Directory;
                if (e.IsCurrentOrParentDirectory)
                {
                    extension = Consts.MimeTypes.DirectoryOpening;
                }
            }

            var iconInfo = MasterData.GetFasIconFromExtension(extension);

            dirHtml.WriteLine("<tr>");
            dirHtml.WriteLine("<th>");

            if (e.IsDirectory == false)
            {
                if (this.Options.Flags.Bit(LogBrowserFlags.NoPreview) == false)
                {
                    dirHtml.WriteLine($@"<a href=""{AbsolutePathPrefix}{absolutePath._EncodeUrlPath()}?tail={Options.TailSize}""><i class=""far fa-eye""></i></a>&nbsp;");
                }
            }

            dirHtml.WriteLine($@"<a href=""{AbsolutePathPrefix}{absolutePath._EncodeUrlPath()}""><span class=""icon\""><i class=""{iconInfo.Item1} {iconInfo.Item2}""></i></span> {printFileName._EncodeHtml(true)}</a>");

            dirHtml.WriteLine("</th>");

            string sizeStr = Str.GetFileSizeStr(printSize);
            if (e.IsDirectory)
            {
                if (e.IsParentDirectory)
                {
                    sizeStr = $"Total {totalFileCount._ToString3()} files: {Str.GetFileSizeStr(totalSize)}";
                }
                else
                {
                    sizeStr = "<Dir>";
                }
            }

            dirHtml.WriteLine($"<td align=\"right\">{sizeStr._EncodeHtml()}</td>");
            dirHtml.WriteLine($"<td>{e.LastWriteTime.LocalDateTime._ToDtStr()}</td>");

            dirHtml.WriteLine("</tr>");
        }

        body = body._ReplaceStrWithReplaceClass(new
        {
            __FULLPATH__ = RootFs.PathParser.AppendDirectorySeparatorTail(logicalPath)._EncodeHtml(true),
            __TITLE__ = $"{this.Options.SystemTitle} - {RootFs.PathParser.AppendDirectorySeparatorTail(dir.PathString)}"._EncodeHtml(true),
            __TITLE2__ = $"{this.Options.SystemTitle} - {RootFs.PathParser.AppendDirectorySeparatorTail(dir.PathString)}"._EncodeHtml(true),
            __BREADCRUMB__ = breadCrumbsHtml,
            __FILENAMES__ = dirHtml,
            __FOOTER__ = footer,
        });

        return body;
    }

    protected override async Task CleanupImplAsync(Exception? ex)
    {
        try
        {
            await this.RootFs._DisposeSafeAsync();
        }
        finally
        {
            await base.CleanupImplAsync(ex);
        }
    }
}

// LogBrowser 用の新しい Kestrel 専用サーバーを立てるためのスタートアップクラス
public class LogBrowserHttpServerBuilder : HttpServerStartupBase
{
    public LogBrowserHttpServerOptions Options => (LogBrowserHttpServerOptions)this.Param!;

    public LogBrowser? Impl = null;

    public static HttpServer<LogBrowserHttpServerBuilder> StartServer(HttpServerOptions httpCfg, LogBrowserHttpServerOptions options, CancellationToken cancel = default)
        => new HttpServer<LogBrowserHttpServerBuilder>(httpCfg, options, cancel);

    public LogBrowserHttpServerBuilder(IConfiguration configuration) : base(configuration)
    {
    }

    protected override void ConfigureImpl_BeforeHelper(HttpServerStartupConfig cfg, IApplicationBuilder app, IWebHostEnvironment env, IHostApplicationLifetime lifetime)
    {
    }

    protected override void ConfigureImpl_AfterHelper(HttpServerStartupConfig cfg, IApplicationBuilder app, IWebHostEnvironment env, IHostApplicationLifetime lifetime)
    {
        this.Impl = new LogBrowser(this.Options.LogBrowserOptions, this.Options.AbsolutePrefixPath);

        RouteBuilder rb = new RouteBuilder(app);

        if (this.Impl.AbsolutePathPrefix._IsEmpty())
        {
            // 通常
            rb.MapGet("{*path}", Impl.GetRequestHandlerAsync);
        }
        else
        {
            // URL Secret を設定
            rb.MapGet(this.Impl.AbsolutePathPrefix + "/{*path}", Impl.GetRequestHandlerAsync);
        }

        IRouter router = rb.Build();
        app.UseRouter(router);

        lifetime.ApplicationStopping.Register(() =>
        {
            this.Impl._DisposeSafe();
        });
    }
}

#endif // CORES_BASIC_WEBAPP

