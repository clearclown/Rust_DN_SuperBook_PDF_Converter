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
// 開発中のクラスの一時置き場

#if true

#pragma warning disable CA2235 // Mark all non-serializable fields

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Runtime.CompilerServices;

using IPA.Cores.Basic;
using IPA.Cores.Basic.DnsLib;
using IPA.Cores.Basic.Internal;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using System.Net.Sockets;

using System.Xml;
using System.Xml.Linq;

using Newtonsoft.Json;
using System.Data;
using System.Reflection;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;

namespace IPA.Cores.Basic;

public enum S3FsClientFlags
{
    None = 0,
    AmazonS3 = 1,
}

public class S3FsClientConfig : INormalizable
{
    public S3FsClientFlags Flags = S3FsClientFlags.None;

    public string BaseUrl = "";

    public string AccessKey = "";
    public string SecretKey = "";
    public string BucketName = "";

    public void Normalize()
    {
    }
}

public class S3FsRequest
{
    public S3FsClientConfig Config = null!;
    public WebMethods Method = WebMethods.GET;
    public string ContentMD5 = "";
    public string ContentType = Consts.MimeTypes.OctetStream;
    public DateTimeOffset Date = Util.ZeroDateTimeOffsetValue;

    public string VirtualPath = "";
    public string SubResource = "";

    public long? RangeStart = null;
    public long? RangeLength = null;

    public StrDictionary<List<string>> Headers = new StrDictionary<List<string>>(StrCmpi);

    public string GenerateAuthorizationHeader()
    {
        return $"AWS {this.Config.AccessKey}:{GenerateSignature()}";
    }

    public string GenerateSignature()
    {
        // https://docs.aws.amazon.com/ja_jp/AmazonS3/latest/userguide/RESTAuthentication.html#ConstructingTheAuthenticationHeader
        string strToSign = GenerateStringToSign();

        byte[] signBytes = Secure.HMacSHA1(this.Config.SecretKey._GetBytes_UTF8(), strToSign._GetBytes_UTF8());

        return Str.Base64Encode(signBytes);
    }

    public string GenerateStringToSign()
    {
        // https://docs.aws.amazon.com/ja_jp/AmazonS3/latest/userguide/RESTAuthentication.html#ConstructingTheAuthenticationHeader
        StringWriter w = new StringWriter();
        w.NewLine = Str.NewLine_Str_Unix;

        w.WriteLine(this.Method.ToString());
        w.WriteLine(this.ContentMD5);

        if (this.Method == WebMethods.POST || this.Method == WebMethods.PUT)
        {
            w.WriteLine(this.ContentType);
        }
        else
        {
            w.WriteLine();
        }

        w.WriteLine(this.Date.ToUniversalTime().ToString("r"));

        if (this.Config.Flags.Bit(S3FsClientFlags.AmazonS3))
        {
            foreach (var kv in this.Headers.OrderBy(x => x.Key.ToLowerInvariant()))
            {
                string key = kv.Key;
                var valueList = kv.Value;

                if (key.StartsWith("x-amz-", StringComparison.OrdinalIgnoreCase))
                {
                    if (key._IsSamei("x-amz-date") == false)
                    {
                        string key2 = key.ToLowerInvariant();

                        string txt = key2 + ":" + valueList._Combine(",");

                        w.WriteLine(txt);
                    }
                }
            }
        }

        string canonicalizedResource = "";

        var baseUri = this.Config.BaseUrl._ParseUrl();
        if (baseUri.AbsolutePath == "" || baseUri.AbsolutePath == "/")
        {
            this.Config.BucketName._NotEmptyCheck(nameof(this.Config.BucketName));

            canonicalizedResource = "/" + this.Config.BucketName;
        }

        canonicalizedResource += this.VirtualPath;

        if (this.SubResource._IsFilled())
        {
            if (this.SubResource.StartsWith("?") == false)
            {
                canonicalizedResource += "?";
            }
            canonicalizedResource += this.SubResource;
        }

        w.Write(canonicalizedResource);

        return w.ToString();
    }
}

public class S3FsClient : WebFsClient
{
    public S3FsClientConfig Config;
    public WebApi Web;

    public S3FsClient(S3FsClientConfig config, WebFsClientSettings? settings = null) : base(settings)
    {
        try
        {
            this.Config = config;

            this.Config.Normalize();

            this.Web = new WebApi(this.Settings.WebOptions);
        }
        catch (Exception ex)
        {
            this._DisposeSafe(ex);
            throw;
        }
    }

    // 任意の絶対パスを、S3 内で通用する仮想パスに変換する (主にパスのチェックをしてディレクトリ区切り記号を正規化するだけ)
    public static string NormalizeS3VirtualPath(string path, bool isDirectory)
    {
        path = PPLinux.NormalizeDirectorySeparatorAndCheckIfAbsolutePath(path);

        if (path.StartsWith("/") == false)
        {
            throw new CoresLibException($"targetPath '{path}' doesn't start with '/'.");
        }

        if (isDirectory == false)
        {
            if (path.Length <= 1 || path.EndsWith("/"))
            {
                throw new CoresLibException($"targetPath '{path}' is invalid as filename.");
            }
        }
        else
        {
            if (path.EndsWith("/") == false)
            {
                path += "/";
            }
        }

        string[] elements = PPLinux.SplitAbsolutePathToElementsUnixStyle(path, noProcessStacking: true);

        foreach (var element in elements)
        {
            PPLinux.ValidateFileOrDirectoryName(element);
            PPWin.ValidateFileOrDirectoryName(element);
        }

        return path;
    }

    // 任意の絶対パスを、S3 用の URL に変換する
    public string GenerateS3UrlFromVirtualPath(string path, bool isDirectory)
    {
        path = NormalizeS3VirtualPath(path, isDirectory);

        if (path.StartsWith("/") == false)
        {
            throw new CoresLibException($"Invalid path: '{path}'");
        }

        path = path.Substring(1);

        string baseUrlTmp = this.Config.BaseUrl;

        if (baseUrlTmp.EndsWith("/") == false)
        {
            baseUrlTmp += "/";
        }

        var baseUri = baseUrlTmp._ParseUrl();

        var ret = baseUri._CombineUrl(path);

        return ret.ToString();
    }

    // リクエストの送付
    public async Task<WebSendRecvResponse> RequestS3Async(S3FsRequest req, string url, Stream? uploadStream = null, CancellationToken cancel = default)
    {
        WebRequestOptions options = new WebRequestOptions();

        options.RequestHeaders.Add("Date", req.Date.ToUniversalTime().ToString("r")._SingleList());
        options.RequestHeaders.Add("Authorization", req.GenerateAuthorizationHeader()._SingleList());

        foreach (var kv in req.Headers)
        {
            options.RequestHeaders.Add(kv.Key, kv.Value.ToList());
        }

        WebSendRecvRequest webReq = new WebSendRecvRequest(req.Method, url, cancel, req.ContentType, uploadStream, options: options, rangeStart: req.RangeStart, rangeLength: req.RangeLength);

        var webRes = await this.Web.HttpSendRecvDataAsync(webReq);

        return webRes;
    }

    protected override async Task<WebFsFileResponse> ReadFileImplAsync(WebFsArgs args, string path, CancellationToken cancel = default)
    {
        string url = GenerateS3UrlFromVirtualPath(path, false);

        S3FsRequest req = new S3FsRequest
        {
            Config = this.Config,
            Method = WebMethods.GET,
            Date = DtOffsetNow,
            VirtualPath = NormalizeS3VirtualPath(path, false),
            RangeStart = args.DownloadStartPosition,
            RangeLength = args.DownloadPartialLength,
        };

        var res = await this.RequestS3Async(req, url, null, cancel);

        try
        {
            if (res.DownloadContentLength.HasValue == false)
            {
                throw new CoresLibException("No ContentLength header in the S3 web response");
            }

            long fullContentLength;
            long partialContentLength = res.DownloadContentLength.Value;

            var range = res.HttpResponseMessage.Content.Headers.ContentRange;

            long startPos;

            if (range != null)
            {
                if (range.Length.HasValue == false)
                {
                    throw new CoresLibException("range.Length is missing in the S3 response");
                }

                fullContentLength = range.Length.Value;

                if (range.Unit._IsDiffi("bytes"))
                {
                    throw new CoresLibException($"range unit is '{range.Unit}'");
                }

                if (range.From.HasValue)
                {
                    startPos = range.From.Value;
                }
                else
                {
                    startPos = 0;
                }

                if (range.To.HasValue)
                {
                    partialContentLength = range.To.Value + 1 - startPos;
                    if (partialContentLength < 0)
                    {
                        throw new CoresLibException($"Invalid range. startPos = {startPos}, To = {range.To}, Calculated partialContentLength = {partialContentLength}");
                    }
                }
                else
                {
                    throw new CoresLibException("range.To is missing in the S3 response");
                }

                if ((partialContentLength + startPos) > fullContentLength)
                {
                    throw new CoresLibException($"(partialContentLength ({partialContentLength}) + startPos ({startPos})) > fullContentLength ({fullContentLength})");
                }
            }
            else
            {
                startPos = 0;
                fullContentLength = partialContentLength;
            }

            if (startPos != (args.DownloadStartPosition ?? 0))
            {
                throw new CoresLibException($"Returned DownloadStartPosition ({startPos}) != Requested DownloadStartPosition ({args.DownloadStartPosition})");
            }

            var ret = new WebFsFileResponse(res, res.DownloadStream, fullContentLength, startPos, partialContentLength);

            return ret;
        }
        catch
        {
            await res._DisposeSafeAsync();
            throw;
        }
    }

    protected override async Task<WebFsEnumDirResponse> EnumDirectoryImplAsync(WebFsArgs args, string path, string wildcard = "", string continueToken = "", int maxItems = 1000, CancellationToken cancel = default)
    {
        string dirPrefix = NormalizeS3VirtualPath(path, true);

        // 先頭の "/" を抜く
        if (dirPrefix[0] != '/')
        {
            throw new CoresLibException($"dirPrefix[0] != '/'. dirPrefix = '{dirPrefix}'");
        }
        dirPrefix = dirPrefix.Substring(1);

        S3FsRequest req = new S3FsRequest
        {
            Config = this.Config,
            Method = WebMethods.GET,
            Date = DtOffsetNow,
            VirtualPath = "/",
        };

        QueryStringList qs = new QueryStringList();
        qs.Add("list-type", "2");
        qs.Add("delimiter", "/");
        qs.Add("max-keys", maxItems.ToString());

        if (dirPrefix._IsFilled())
        {
            qs.Add("prefix", dirPrefix);
        }

        if (continueToken._IsFilled())
        {
            qs.Add("continuation-token", continueToken);
        }

        string qsString = qs.ToString();

        string url = GenerateS3UrlFromVirtualPath("/", true) + "?" + qsString;

        await using var res = await this.RequestS3Async(req, url, null, cancel);

        string xmlBody = (await res.DownloadStream._ReadToEndAsync())._GetString_UTF8();

        var ret = ParseListObjectsV2(xmlBody, dirPrefix);

        if (ret.MaxKeys > maxItems)
        {
            throw new CoresLibException($"MaxKeys ({ret.MaxKeys}) > maxItems ({maxItems})");
        }

        if (ret.SpecifiedPrefix != dirPrefix)
        {
            throw new CoresLibException($"Response refix '{ret.SpecifiedPrefix}' != Request prefix '{dirPrefix}'");
        }

        if (ret.Delimiter != "/")
        {
            throw new CoresLibException($"Response Delimiter '{ret.Delimiter}' != '/'");
        }

        if (ret.BucketName != this.Config.BucketName)
        {
            throw new CoresLibException($"Response BucketName '{ret.BucketName}' != Request BucketName '{this.Config.BucketName}'");
        }

        return ret;
    }

    static WebFsEnumDirResponse ParseListObjectsV2(string xmlBody, string dirPrefix)
    {
        //xmlBody._XDocumentReformat()._Print();

        var doc = xmlBody._StrToXDocument();

        var listNode = doc.Root!;

        if (listNode.Name.LocalName != "ListBucketResult")
        {
            throw new CoresLibException($"Invalid XML result: {xmlBody._OneLine()._TruncStrEx(512)}");
        }

        var childNodes = listNode.Elements();

        WebFsEnumDirResponse ret = new WebFsEnumDirResponse();

        ret.IsTruncated = childNodes._XmlGetSimpleElementStr("IsTruncated")._ToBool();
        ret.BucketName = childNodes._XmlGetSimpleElementStr("Name")._NonNull();
        ret.SpecifiedPrefix = childNodes._XmlGetSimpleElementStr("Prefix")._NonNull();
        ret.KeyCount = childNodes._XmlGetSimpleElementStr("KeyCount")._ToInt();
        ret.MaxKeys = childNodes._XmlGetSimpleElementStr("MaxKeys")._ToInt();
        ret.Delimiter = childNodes._XmlGetSimpleElementStr("Delimiter")._NonNull();
        ret.NextContinuationToken = childNodes._XmlGetSimpleElementStr("NextContinuationToken")._NonNull();

        if (ret.IsTruncated && ret.NextContinuationToken._IsEmpty())
        {
            throw new CoresLibException("ret.IsTruncated && ret.NextContinuationToken._IsEmpty()");
        }

        if (ret.IsTruncated == false && ret.NextContinuationToken._IsFilled())
        {
            throw new CoresLibException("ret.IsTruncated == false && ret.NextContinuationToken._IsFilled()");
        }

        if (ret.KeyCount > ret.MaxKeys)
        {
            throw new CoresLibException($"KeyCount ({ret.KeyCount}) > MaxKeys ({ret.MaxKeys})");
        }

        var contents = childNodes._XmlEnumNodesByLocalName("Contents").ToArray();

        foreach (var content in contents)
        {
            var nodes = content.Elements();

            string? keyName = nodes._XmlGetSimpleElementStr("Key");
            DateTimeOffset dt = DtOffsetZero;

            if (DateTimeOffset.TryParse(nodes._XmlGetSimpleElementStr("LastModified"), out DateTimeOffset time))
            {
                dt = time;
            }

            dt = dt.LocalDateTime._AsDateTimeOffset(true);

            string etag = nodes._XmlGetSimpleElementStr("ETag")._NonNullTrim()._RemoveQuotation();

            long size = nodes._XmlGetSimpleElementStr("Size")._ToLong();

            string storageClass = nodes._XmlGetSimpleElementStr("StorageClass")._NonNullTrim();

            if (keyName._IsNullOrZeroLen()) throw new CoresLibException($"Invalid keyname: '{keyName}'");
            if (size < 0) throw new CoresLibException($"Invalid size: {size}");

            if (keyName.StartsWith("/")) throw new CoresLibException($"Invalid keyname: '{keyName}'");

            if (keyName != dirPrefix)
            {
                // Normal file
                string keyNameNormalized = NormalizeS3VirtualPath("/" + keyName, false);

                WebFsEnumDirEntity entity = new WebFsEnumDirEntity
                {
                    IsDirectory = false,
                    Name = keyNameNormalized,
                    LastModified = time._NormalizeDateTimeOffsetForFileSystem(),
                    ETag = etag,
                    StorageClass = storageClass,
                    Size = size,
                };

                ret.EntityList.Add(entity.Name, entity);
            }
            else
            {
                // Current directory
                string prefixNormalized = NormalizeS3VirtualPath("/" + keyName, true);

                WebFsEnumDirEntity entity = new WebFsEnumDirEntity
                {
                    IsDirectory = true,
                    IsCurrentDirectory = true,
                    Name = prefixNormalized,
                    LastModified = time._NormalizeDateTimeOffsetForFileSystem(),
                    ETag = etag,
                    StorageClass = storageClass,
                    Size = size,
                };

                ret.EntityList.Add(entity.Name, entity);
            }
        }

        var commonPrefixes = childNodes._XmlEnumNodesByLocalName("CommonPrefixes").ToArray();

        foreach (var commonPrefix in commonPrefixes)
        {
            var nodes = commonPrefix.Elements();

            string? prefix = nodes._XmlGetSimpleElementStr("Prefix");

            if (prefix._IsNullOrZeroLen()) throw new CoresLibException($"Invalid prefix: '{prefix}'");
            if (prefix.StartsWith("/")) throw new CoresLibException($"Invalid prefix: '{prefix}'");
            if (prefix.EndsWith("/") == false) throw new CoresLibException($"Invalid prefix: '{prefix}'");

            string prefixNormalized = NormalizeS3VirtualPath("/" + prefix, true);

            WebFsEnumDirEntity entity = new WebFsEnumDirEntity
            {
                IsDirectory = true,
                Name = prefixNormalized,
                LastModified = DtOffsetZero._NormalizeDateTimeOffsetForFileSystem(),
                ETag = "",
                StorageClass = "",
            };

            ret.EntityList.Add(entity.Name, entity);
        }

        //ret._PrintAsJson();

        return ret;
    }

    protected override async Task CleanupImplAsync(Exception? ex)
    {
        try
        {
            await this.Web._DisposeSafeAsync();
        }
        finally
        {
            await base.CleanupImplAsync(ex);
        }
    }
}

public class WebFsClientSettings
{
    public WebApiOptions WebOptions { get; }

    public WebFsClientSettings(WebApiOptions? webOptions = null)
    {
        if (webOptions == null)
        {
            webOptions = new WebApiOptions(doNotUseTcpStack: true);
        }

        this.WebOptions = webOptions;
    }
}

public class WebFsFileResponse : IDisposable, IAsyncDisposable
{
    public Stream? DownloadStream { get; }
    public long ContentFullLength { get; }
    public long ContentStartPosition { get; }
    public long ContentPartialLength { get; }

    readonly IAsyncDisposable? DisposeMe;

    public WebFsFileResponse(IAsyncDisposable? disposeMe, Stream? downloadStream, long contentFullLength, long contentStartPosition, long contentPartialLength)
    {
        try
        {
            this.DisposeMe = disposeMe;
            this.DownloadStream = downloadStream;

            this.ContentFullLength = contentFullLength;
            this.ContentStartPosition = contentStartPosition;
            this.ContentPartialLength = contentPartialLength;

            if (this.ContentFullLength < 0) throw new CoresLibException($"ContentFullLength ({this.ContentFullLength}) < 0");
            if (this.ContentStartPosition < 0) throw new CoresLibException($"ContentStartPosition ({this.ContentStartPosition}) < 0");
            if (this.ContentPartialLength < 0) throw new CoresLibException($"ContentPartialLength ({this.ContentPartialLength}) < 0");

            if ((this.ContentStartPosition + this.ContentPartialLength) > this.ContentFullLength)
            {
                throw new CoresLibException($"(ContentStartPosition ({this.ContentStartPosition}) + ContentPartialLength ({this.ContentPartialLength})) > ContentFullLength ({this.ContentFullLength})");
            }
        }
        catch
        {
            this._DisposeSafe();
            throw;
        }
    }

    public void Dispose() { this.Dispose(true); GC.SuppressFinalize(this); }
    Once DisposeFlag;
    public virtual async ValueTask DisposeAsync()
    {
        if (DisposeFlag.IsFirstCall() == false) return;
        await DisposeInternalAsync();
    }
    protected virtual void Dispose(bool disposing)
    {
        if (!disposing || DisposeFlag.IsFirstCall() == false) return;
        DisposeInternalAsync()._GetResult();
    }
    async Task DisposeInternalAsync()
    {
        await this.DownloadStream._DisposeSafeAsync();

        await this.DisposeMe._DisposeSafeAsync();
    }
}

public class WebFsEnumDirEntity
{
    public bool IsDirectory;
    public bool IsCurrentDirectory;
    public string Name = "";
    public DateTimeOffset LastModified = ZeroDateTimeOffsetValue;
    public string ETag = "";
    public long Size;
    public string StorageClass = "";
}

public class WebFsEnumDirResponse
{
    public StrDictionary<WebFsEnumDirEntity> EntityList = new StrDictionary<WebFsEnumDirEntity>();
    public string SpecifiedPrefix = "";
    public string Delimiter = "";
    public int KeyCount;
    public int MaxKeys;
    public string BucketName = "";
    public bool IsTruncated = false;
    public string NextContinuationToken = "";
}

public class WebFsArgs : IValidatable
{
    public long? DownloadStartPosition;
    public long? DownloadPartialLength;
    public int? MaxEnumDirItemsPerRequest;

    public void Validate()
    {
        if ((this.DownloadStartPosition ?? 0) < 0) throw new CoresLibException($"DownloadStartPosition ({this.DownloadStartPosition}) < 0");
        if ((this.DownloadPartialLength ?? 0) < 0) throw new CoresLibException($"DownloadPartialLength ({this.DownloadPartialLength}) < 0");
        if ((this.MaxEnumDirItemsPerRequest ?? 1000) <= 0) throw new CoresLibException($"MaxEnumDirItemsPerRequest ({this.MaxEnumDirItemsPerRequest}) <= 0");
    }
}


public abstract class WebFsClient : AsyncService
{
    protected abstract Task<WebFsFileResponse> ReadFileImplAsync(WebFsArgs args, string path, CancellationToken cancel = default);
    protected abstract Task<WebFsEnumDirResponse> EnumDirectoryImplAsync(WebFsArgs args, string path, string wildcard = "", string continueToken = "", int maxItems = 1000, CancellationToken cancel = default);

    public WebFsClientSettings Settings { get; }

    public WebFsClient(WebFsClientSettings? settings = null)
    {
        try
        {
            if (settings == null) settings = new WebFsClientSettings();

            this.Settings = settings;
        }
        catch (Exception ex)
        {
            this._DisposeSafe(ex);
            throw;
        }
    }

    public async Task<WebFsFileResponse> ReadFileAsync(string path, WebFsArgs? args = null, CancellationToken cancel = default)
    {
        args ??= new WebFsArgs();

        args.Validate();

        var res = await this.ReadFileImplAsync(args, path, cancel);

        return res;
    }

    public async Task<WebFsEnumDirResponse> EnumDirecroryAsync(string path, string wildcard = "", WebFsArgs? args = null, CancellationToken cancel = default)
    {
        args ??= new WebFsArgs();

        args.Validate();

        HashSet<string> tokenHistory = new HashSet<string>();

        WebFsEnumDirResponse ret = await this.EnumDirectoryImplAsync(args, path, wildcard, "", args.MaxEnumDirItemsPerRequest ?? 1000, cancel);
        
        string nextToken = ret.NextContinuationToken;

        tokenHistory.Add(nextToken);

        int counterForAbort = 0;

        while (nextToken._IsFilled())
        {
            cancel.ThrowIfCancellationRequested();

            WebFsEnumDirResponse contResponse = await this.EnumDirectoryImplAsync(args, path, wildcard, nextToken, args.MaxEnumDirItemsPerRequest ?? 1000, cancel);

            bool newNameItem = false;

            foreach (var item in contResponse.EntityList)
            {
                if (ret.EntityList.TryAdd(item.Key, item.Value))
                {
                    newNameItem = true;
                }
            }

            if (newNameItem == false)
            {
                // 継続列挙結果で何ら新しいアイテムが追加されないことが 2 回以上発生したら、S3 側の不具合で無限ループしていることになるので、中断する
                counterForAbort++;

                if (counterForAbort >= 3)
                {
                    throw new CoresLibException("Invalid state: counterForAbort >= 3");
                }
            }
            else
            {
                counterForAbort = 0;
            }

            nextToken = contResponse.NextContinuationToken;

            if (nextToken._IsFilled())
            {
                if (tokenHistory.Add(nextToken) == false)
                {
                    // 継続トークンループ
                    throw new CoresLibException("Invalid state: existing token");
                }
            }
        }

        ret.NextContinuationToken = "";
        ret.IsTruncated = false;

        if (ret.EntityList.Any() == false && ret.SpecifiedPrefix._IsNotZeroLen())
        {
            throw new VfsNotFoundException(path, $"Directory '{path}' not found");
        }

        return ret;
    }

    public async Task TestAsync(CancellationToken cancel = default)
    {
        await this.EnumDirectoryImplAsync(new WebFsArgs(), "/", maxItems: 1);
    }

    protected override Task CleanupImplAsync(Exception? ex)
    {
        return base.CleanupImplAsync(ex);
    }
}




#endif

