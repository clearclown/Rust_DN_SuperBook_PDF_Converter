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
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using System.IO;
using System.Net;
using System.ComponentModel.DataAnnotations;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

//using System.Net.Http;
//using System.Net.Http.Headers;
using IPA.Cores.Basic.HttpClientCore;
using System.Security.Cryptography;
using System.Linq;
using Org.BouncyCastle.Ocsp;
using System.Net.Security;

#pragma warning disable CA2235 // Mark all non-serializable fields

namespace IPA.Cores.Basic;

public static partial class CoresConfig
{
    public static partial class DefaultHttpClientSettings
    {
        public static readonly Copenhagen<int> MaxConnectionPerServer = 512;
        public static readonly Copenhagen<int> PooledConnectionLifeTime = 5 * 60 * 1000;
        public static readonly Copenhagen<int> Timeout = 60 * 1000;
        public static readonly Copenhagen<int> MaxRecvSize = 100 * 1024 * 1024;
        public static readonly Copenhagen<bool> UseProxy = true;
    }
}

public class CoresWebApiResponseException : CoresException
{
    public static async Task<CoresWebApiResponseException> CreateExceptionAsync(HttpResponseMessage res)
    {
        string details = "";

        byte[] data = new byte[0];

        try
        {
            data = await res.Content.ReadAsByteArrayAsync();

            details = data._GetString_UTF8()._OneLine(" ");
        }
        catch { }

        string errStr = string.Format("Response status code does not indicate success: {0} ({1}).", (int)res.StatusCode, res.ReasonPhrase);

        if (details != null)
        {
            errStr += " Details: " + details._TruncStr(1024);
        }

        var ret = new CoresWebApiResponseException(errStr);

        ret.Version = res.Version;
        ret.StatusCode = res.StatusCode;
        ret.ReasonPhrase = res.ReasonPhrase;
        ret.ResponseHttpBodyData = data;
        try
        {
            ret.ResponseHttpBodyText = data._GetString_UTF8(true);
        }
        catch { }

        return ret;
    }

    public Version Version { get; private set; } = null!;
    public HttpStatusCode StatusCode { get; private set; }
    public string ReasonPhrase { get; private set; } = "";
    public ReadOnlyMemory<byte> ResponseHttpBodyData { get; private set; }
    public string ResponseHttpBodyText { get; private set; } = "";

    private CoresWebApiResponseException(string message) : base(message) { }
}

public class EasyHttpClientOptions
{
    public int TryCount { get; }
    public int RetryIntervalMsecs { get; }
    public bool RandomInterval { get; }
    public string BasicUsername { get; }
    public string BasicPassword { get; }

    public WebApiOptions WebApiOptions { get; }

    public RemoteCertificateValidationCallback? SslCallback { get; }

    public EasyHttpClientOptions(
        int retCount = Consts.Numbers.EasyHttpClient_DefaultTryCount, int retryIntervalMsecs = Consts.Numbers.EasyHttpClient_DefaultRetryIntervalMsecs,
        bool randomInterval = true,
        string basicUsername = "", string basicPassword = "",
        WebApiOptions? options = null, RemoteCertificateValidationCallback? sslCallback = null)
    {
        options ??= new WebApiOptions();

        this.TryCount = Math.Max(retCount, 1);

        this.RetryIntervalMsecs = Math.Max(retryIntervalMsecs, 1);

        this.RandomInterval = randomInterval;

        this.WebApiOptions = options;
        this.SslCallback = sslCallback;

        this.BasicUsername = basicUsername._NonNull();
        this.BasicPassword = basicPassword._NonNull();
    }
}

public partial class EasyHttpClient : AsyncService
{
    public EasyHttpClientOptions Options { get; }
    public WebApi Api { get; }

    public EasyHttpClient(EasyHttpClientOptions? options = null)
    {
        try
        {
            options ??= new EasyHttpClientOptions();
            this.Options = options;

            this.Api = new WebApi(this.Options.WebApiOptions, this.Options.SslCallback);

            if (this.Options.BasicUsername._IsFilled() && this.Options.BasicPassword._IsFilled())
            {
                this.Api.SetBasicAuthHeader(this.Options.BasicUsername, this.Options.BasicPassword);
            }
        }
        catch
        {
            this._DisposeSafe();
            throw;
        }
    }

    public async Task<WebRet> GetAsync(string url, CancellationToken cancel = default)
    {
        return await TaskUtil.RetryAsync(async () =>
        {
            return await Api.SimpleQueryAsync(WebMethods.GET, url, cancel);
        },
        this.Options.RetryIntervalMsecs,
        this.Options.TryCount,
        cancel,
        this.Options.RandomInterval);
    }

    protected override async Task CleanupImplAsync(Exception? ex)
    {
        try
        {
            await this.Api._DisposeSafeAsync();
        }
        finally
        {
            await base.CleanupImplAsync(ex);
        }
    }
}

public class WebResponseException : Exception
{
    public WebResponseException(string message) : base(message) { }
}

public interface IValidatable
{
    void Validate();
}

public static partial class IErrorCheckableHelper
{
    // IErrorCheckable から IErrorCheckableHelper の実装に必要な Validate() 関数の応答を返す
    public static IEnumerable<ValidationResult> _Validate(this IValidatable targetObject, ValidationContext validationContext)
    {
        try
        {
            if (targetObject is INormalizable norm) norm.Normalize();

            targetObject.Validate();
        }
        catch (Exception ex)
        {
            ValidationResult err = new ValidationResult(ex.Message);

            return err._SingleArray();
        }

        return EmptyEnumerable<ValidationResult>.Empty;
    }
}

public class WebSendRecvRequest : IDisposable
{
    public WebMethods Method { get; }
    public string Url { get; }
    public CancellationToken Cancel { get; }
    public string UploadContentType { get; }
    public Stream? UploadStream { get; }
    public long? RangeStart { get; }
    public long? RangeLength { get; }
    public WebRequestOptions? Options { get; }

    public WebSendRecvRequest(WebMethods method, string url, CancellationToken cancel = default,
        string uploadContentType = Consts.MimeTypes.OctetStream, Stream? uploadStream = null,
        long? rangeStart = null, long? rangeLength = null, WebRequestOptions? options = null)
    {
        if (rangeStart == null && rangeLength != null) throw new ArgumentOutOfRangeException("rangeStart == null && rangeLength != null");
        if ((rangeStart ?? 0) < 0) throw new ArgumentOutOfRangeException(nameof(rangeStart));
        if ((rangeLength ?? 1) <= 0) throw new ArgumentOutOfRangeException(nameof(rangeLength));

        this.RangeStart = rangeStart;
        this.RangeLength = rangeLength;

        checked
        {
            Limbo.SInt64 = this.RangeStart ?? 0 + this.RangeLength ?? 0;
        }

        this.Method = method;
        this.Url = url;
        this.Cancel = cancel;
        this.UploadContentType = uploadContentType._FilledOrDefault(Consts.MimeTypes.OctetStream);
        this.UploadStream = uploadStream;
        this.Options = options;
    }

    public void Dispose() { this.Dispose(true); GC.SuppressFinalize(this); }
    Once DisposeFlag;
    protected virtual void Dispose(bool disposing)
    {
        if (!disposing || DisposeFlag.IsFirstCall() == false) return;
        this.UploadStream._DisposeSafe();
    }
}

public class WebSendRecvResponse : IDisposable, IAsyncDisposable
{
    public HttpContent DownloadContent { get; }
    public HttpResponseMessage HttpResponseMessage { get; }
    public string DownloadContentType { get; }
    public long? DownloadContentLength { get; }
    public Stream DownloadStream { get; }

    public WebSendRecvResponse(HttpResponseMessage response, Stream downloadStream)
    {
        try
        {
            this.HttpResponseMessage = response;
            this.DownloadContent = response.Content;

            this.DownloadContentType = (this.DownloadContent.Headers.ContentType?.MediaType)._NonNullTrim();

            if (this.DownloadContent.TryComputeLength(out long length))
                this.DownloadContentLength = length;
            else
                this.DownloadContentLength = response.Content.Headers.ContentLength;

            DownloadStream = downloadStream;
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
        this.HttpResponseMessage._DisposeSafe();
        this.DownloadContent._DisposeSafe();
        await this.DownloadStream._DisposeSafeAsync();
    }
}

public class WebUserRet<TUser> : SystemAndUser<WebRet, TUser>
{
    public WebUserRet(WebRet web, TUser user) : base(web, user)
    {
    }
}

public partial class WebRet
{
    public const string MediaTypeJson = Consts.MimeTypes.Json;

    public string Url { get; }
    public string ContentType { get; }
    public byte[] Data { get; }
    public string MediaType { get; }
    public string CharSet { get; }
    public Encoding DefaultEncoding { get; } = null!;
    public WebApi Api { get; }
    public HttpResponseHeaders Headers { get; }
    public HttpStatusCode StatusCode { get; }
    public bool IsSuccessStatusCode { get; }
    public string StatusReason { get; }
    public string StatusCodeAndReasonString { get; }
    public string String => this.ToString();

    public WebRet(WebApi api, string url, string contentType, byte[] data, HttpResponseHeaders headers, bool isSuccessStatusCode, HttpStatusCode statusCode, string statusReason)
    {
        this.Api = api;
        this.Url = url._NonNull();
        this.ContentType = contentType._NonNull();
        this.Headers = headers;

        this.StatusCode = statusCode;
        this.IsSuccessStatusCode = isSuccessStatusCode;
        this.StatusReason = statusReason._NonNull();

        this.StatusCodeAndReasonString = string.Format("Response status code does not indicate success: {0} ({1}).", (int)statusCode, statusReason);

        try
        {
            var ct = new System.Net.Mime.ContentType(this.ContentType);
            this.MediaType = ct.MediaType._NonNull();
            this.CharSet = ct.CharSet._NonNull();
        }
        catch
        {
            this.MediaType = this.ContentType;
            this.CharSet = "";
        }

        try
        {
            if (this.CharSet._IsFilled())
            {
                this.DefaultEncoding = Encoding.GetEncoding(this.CharSet);
            }
        }
        catch
        {
        }

        if (this.DefaultEncoding == null)
        {
            this.DefaultEncoding = api.RequestEncoding;
        }

        this.Data = data._NonNull();

        if (this.Api.DebugPrintResponse)
        {
            this._DebugObject();
        }
    }

    public override string ToString() => this.Data._GetString(this.DefaultEncoding);
    public string ToString(Encoding encoding) => this.Data._GetString(encoding);

    public WebUserRet<TUser> CreateUserRet<TUser>(TUser userData) => new WebUserRet<TUser>(this, userData);

    public List<Tuple<string, KeyValueList<string, string>>> ParseWebLinks()
    {
        List<Tuple<string, KeyValueList<string, string>>> ret = new List<Tuple<string, KeyValueList<string, string>>>();

        foreach (var str in this.Headers.Where(x => x.Key._IsSamei("Link")).Select(x => x.Value).SelectMany(x => x))
        {
            try
            {
                var parsed = Str.ParseWebLinkStr(str);

                ret.Add(parsed);
            }
            catch
            {
            }
        }

        return ret;
    }
}

[Flags]
public enum WebMethods
{
    GET,
    DELETE,
    POST,
    PUT,
    HEAD,
}

[Flags]
public enum WebMethodBits
{
    GET = 1,
    DELETE = 2,
    POST = 4,
    PUT = 8,
    HEAD = 16,
}

[Serializable]
public class WebApiSettings
{
    public int Timeout = CoresConfig.DefaultHttpClientSettings.Timeout;
    public int MaxRecvSize = CoresConfig.DefaultHttpClientSettings.MaxRecvSize;
    public int MaxConnectionPerServer = CoresConfig.DefaultHttpClientSettings.MaxConnectionPerServer;
    public int PooledConnectionLifeTime = CoresConfig.DefaultHttpClientSettings.PooledConnectionLifeTime;
    public bool UseProxy = CoresConfig.DefaultHttpClientSettings.UseProxy;
    public bool DisableKeepAlive = false;
    public bool DoNotThrowHttpResultError = false;
    public string ManualProxyUri = "";

    public bool AllowAutoRedirect = true;
    public int MaxAutomaticRedirections = 10;

    public bool SslAcceptAnyCerts = false;
    public List<string> SslAcceptCertSHAHashList = new List<string>();

    public bool DebugPrintResponse = false;
}

public class WebApiOptions
{
    public WebApiSettings Settings { get; }
    public TcpIpSystem? TcpIp { get; }

    public WebApiOptions(WebApiSettings? settings = null, TcpIpSystem? tcpIp = null, bool doNotUseTcpStack = false)
    {
        if (settings == null) settings = new WebApiSettings();
        if (tcpIp == null) tcpIp = (doNotUseTcpStack ? null : LocalNet);

        this.Settings = settings;
        this.TcpIp = tcpIp;
    }
}

public class WebRequestOptions
{
    public StrDictionary<List<string>> RequestHeaders = new StrDictionary<List<string>>(StrCmpi);
}

public partial class WebApi : IDisposable, IAsyncDisposable
{
    WebApiSettings Settings;

    public int TimeoutMsecs { get => (int)Client.Timeout.TotalMilliseconds; set => Client.Timeout = new TimeSpan(0, 0, 0, 0, value); }
    public long MaxRecvSize { get => this.Client.MaxResponseContentBufferSize; set => this.Client.MaxResponseContentBufferSize = value; }

    public Encoding RequestEncoding { get; set; } = Encoding.UTF8;

    public StrDictionary<List<string>> RequestHeaders = new StrDictionary<List<string>>(StrCmpi);

    SocketsHttpHandler ClientHandler;

    public X509CertificateCollection? ClientCerts { get => this.ClientHandler.SslOptions.ClientCertificates; }

    public HttpClient Client { get; private set; }

    public bool DebugPrintResponse => this.Settings.DebugPrintResponse;

    public RemoteCertificateValidationCallback? SslServerCertValicationCallback { get; }

    public WebApi(WebApiOptions? options = null, RemoteCertificateValidationCallback? sslServerCertValicationCallback = null)
    {
        if (options == null) options = new WebApiOptions();

        this.Settings = options.Settings._CloneDeep();

        this.ClientHandler = new SocketsHttpHandler(options.TcpIp);

        this.ClientHandler.AllowAutoRedirect = this.Settings.AllowAutoRedirect;
        this.ClientHandler.MaxAutomaticRedirections = this.Settings.MaxAutomaticRedirections;
        this.ClientHandler.MaxConnectionsPerServer = this.Settings.MaxConnectionPerServer;
        this.ClientHandler.PooledConnectionLifetime = Util.ConvertTimeSpan(this.Settings.PooledConnectionLifeTime);

        this.Client = new HttpClient(this.ClientHandler, true);
        this.MaxRecvSize = this.Settings.MaxRecvSize;
        this.TimeoutMsecs = this.Settings.Timeout;
        this.SslServerCertValicationCallback = sslServerCertValicationCallback;
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
        this.Client._DisposeSafe();
        this.Client = null!;

        await Task.CompletedTask;
    }

    public string BuildQueryString(params (string name, string? value)[]? queryList)
    {
        StringWriter w = new StringWriter();
        int count = 0;
        if (queryList != null)
        {
            foreach (var t in queryList)
            {
                if (t.value != null)
                {
                    if (count != 0)
                    {
                        w.Write("&");
                    }
                    w.Write($"{t.name._EncodeUrl(this.RequestEncoding)}={t.value._EncodeUrl(this.RequestEncoding)}");
                    count++;
                }
            }
        }
        return w.ToString();
    }

    public void SetBasicAuthHeader(string username, string password)
    {
        username = username._NonNull();
        password = password._NonNull();
        string tmp = $"{username}:{password}";
        string tmp2 = "Basic " + Str.Base64Encode(tmp._GetBytes_UTF8());

        this.AddHeader("Authorization", tmp2);
    }

    public void AddHeader(string name, string value, bool allowMultiple = false)
    {
        if (name._IsEmpty()) return;
        lock (this.RequestHeaders)
        {
            if (value._IsEmpty() == false)
            {
                if (allowMultiple == false && this.RequestHeaders.ContainsKey(name))
                    this.RequestHeaders[name] = value._SingleList();
                else
                    this.RequestHeaders._GetOrNew(name, () => new List<string>()).Add(value);
            }
            else
            {
                if (allowMultiple == false)
                {
                    if (this.RequestHeaders.ContainsKey(name))
                        this.RequestHeaders.Remove(name);
                }
            }
        }
    }

    public class SimpleProxyCredentialDef : ICredentials
    {
        public NetworkCredential? GetCredential(Uri uri, string authType)
        {
            return null;
        }
    }

    public class SimpleProxyDef : IWebProxy
    {
        readonly Uri proxyUri;

        public SimpleProxyDef(string uri)
        {
            proxyUri = new Uri(uri);
        }

        readonly SimpleProxyCredentialDef credentials = new SimpleProxyCredentialDef();

        public ICredentials? Credentials { get => credentials; set => throw new NotImplementedException(); }

        public Uri? GetProxy(Uri destination)
        {
            return proxyUri;
        }

        public bool IsBypassed(Uri host)
        {
            return false;
        }
    }

    static readonly FastCache<string, SimpleProxyDef> ProxyDefCacheList = new FastCache<string, SimpleProxyDef>(-1, 0, CacheType.DoNotUpdateExpiresWhenAccess);
    static SimpleProxyDef GetSimpleProxyDef(string proxyUri)
    {
        return ProxyDefCacheList.GetOrCreate(proxyUri, a => new SimpleProxyDef(a))!;
    }

    protected HttpRequestMessage CreateWebRequest(WebMethods method, string url, params (string name, string? value)[]? queryList)
        => CreateWebRequest(method, url, null, queryList);

    virtual protected HttpRequestMessage CreateWebRequest(WebMethods method, string url, WebRequestOptions? options, params (string name, string? value)[]? queryList)
    {
        if (options == null)
        {
            options = new WebRequestOptions();
        }

        string[]? embeddedSslCertHashList = null;

        int sslHashStrIndex = url._Search("!ssl=");
        if (sslHashStrIndex != -1)
        {
            string urlBody = url.Substring(0, sslHashStrIndex);
            string sslCertStr = url.Substring(sslHashStrIndex + 5);

            url = urlBody;

            int i = sslCertStr._Search("!");
            if (i != -1)
            {
                sslCertStr = sslCertStr.Substring(0, i);
            }

            embeddedSslCertHashList = sslCertStr._Split(StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries, ',');
        }

        string qs = "";

        string embeddedUsername = "";
        string embeddedPassword = "";

        if (method == WebMethods.GET || method == WebMethods.DELETE || method == WebMethods.HEAD)
        {
            qs = BuildQueryString(queryList);
            if (qs._IsEmpty() == false)
            {
                url = url + "?" + qs;
            }
        }

        if (url._TryParseUrl(out Uri uri, out QueryStringList qs2))
        {
            if (uri.UserInfo._IsFilled())
            {
                var tokens = uri.UserInfo._Split(StringSplitOptions.None, ':');

                if (tokens.Length >= 2)
                {
                    embeddedUsername = tokens[0];
                    embeddedPassword = tokens[1];
                }
            }
        }

        HttpRequestMessage requestMessage = new HttpRequestMessage(new HttpMethod(method.ToString()), url);

        CacheControlHeaderValue cacheControl = new CacheControlHeaderValue();
        cacheControl.NoStore = true;
        cacheControl.NoCache = true;
        requestMessage.Headers.CacheControl = cacheControl;

        try
        {
            if (embeddedSslCertHashList != null)
            {
                this.ClientHandler.SslOptions.RemoteCertificateValidationCallback = (message, cert, chain, errors) =>
                {
                    foreach (var s in embeddedSslCertHashList.Select(x => x._NormalizeHexString()))
                    {
                        if (cert!.GetCertHashString(HashAlgorithmName.SHA1)._IsSamei(s)) return true;
                        if (cert!.GetCertHashString(HashAlgorithmName.SHA256)._IsSamei(s)) return true;
                        if (cert!.GetCertHashString(HashAlgorithmName.SHA512)._IsSamei(s)) return true;
                    }
                    return false;
                };
            }
            else if (this.SslServerCertValicationCallback != null)
            {
                this.ClientHandler.SslOptions.RemoteCertificateValidationCallback = this.SslServerCertValicationCallback;
            }
            else if (this.Settings.SslAcceptAnyCerts)
            {
                this.ClientHandler.SslOptions.RemoteCertificateValidationCallback = (message, cert, chain, errors) => true;
            }
            else if (this.Settings.SslAcceptCertSHAHashList != null && this.Settings.SslAcceptCertSHAHashList.Count >= 1)
            {
                this.ClientHandler.SslOptions.RemoteCertificateValidationCallback = (message, cert, chain, errors) =>
                {
                    foreach (var s in this.Settings.SslAcceptCertSHAHashList.Select(x => x._NormalizeHexString()))
                    {
                        if (cert!.GetCertHashString(HashAlgorithmName.SHA1)._IsSamei(s)) return true;
                        if (cert!.GetCertHashString(HashAlgorithmName.SHA256)._IsSamei(s)) return true;
                        if (cert!.GetCertHashString(HashAlgorithmName.SHA512)._IsSamei(s)) return true;
                    }
                    return false;
                };
            }
        }
        catch { }

        try
        {
            this.ClientHandler.SslOptions.EncryptionPolicy = CoresConfig.SslSettings.DefaultSslEncryptionPolicyClient;
        }
        catch { }

        try
        {
            this.ClientHandler.SslOptions.CipherSuitesPolicy = CoresConfig.SslSettings.DefaultCipherSuitesPolicyClient;
        }
        catch { }

        try
        {
            if (this.ClientHandler.UseProxy != this.Settings.UseProxy)
            {
                this.ClientHandler.UseProxy = this.Settings.UseProxy;
            }
        }
        catch { }

        try
        {
            if (this.Settings.UseProxy && this.Settings.ManualProxyUri._IsFilled())
            {
                var proxy = GetSimpleProxyDef(this.Settings.ManualProxyUri);

                if (this.ClientHandler.Proxy != proxy)
                {
                    this.ClientHandler.Proxy = proxy;
                }
            }
        }
        catch { }

        string embeddedAuthHeader = "";
        if (embeddedUsername._IsFilled() || embeddedPassword._IsFilled())
        {
            string tmp = $"{embeddedUsername}:{embeddedPassword}";
            embeddedAuthHeader = "Basic " + Str.Base64Encode(tmp._GetBytes_UTF8());
        }

        lock (this.RequestHeaders)
        {
            StrDictionary<List<string>> tmp = new StrDictionary<List<string>>(StrCmpi);

            foreach (string name in this.RequestHeaders.Keys)
            {
                bool ok = true;

                if (name._IsSamei("Authorization") && embeddedAuthHeader._IsFilled())
                {
                    ok = false;
                }

                if (ok)
                {
                    foreach (var value in this.RequestHeaders[name])
                    {
                        tmp._GetOrNew(name, () => new List<string>()).Add(value);
                    }
                }
            }

            foreach (string name in options.RequestHeaders.Keys)
            {
                bool ok = true;

                if (name._IsSamei("Authorization") && embeddedAuthHeader._IsFilled())
                {
                    ok = false;
                }

                if (ok)
                {
                    foreach (var value in options.RequestHeaders[name])
                    {
                        tmp._GetOrNew(name, () => new List<string>()).Add(value);
                    }
                }
            }

            foreach (var kv in tmp.OrderBy(x => x.Key, StrCmpi))
            {
                if (kv.Key._IsSamei("cookie") == false)
                {
                    // cookie 以外はカンマ区切り
                    requestMessage.Headers.Add(kv.Key, kv.Value);
                }
                else
                {
                    // cookie は ; 区切り
                    requestMessage.Headers.Add(kv.Key, kv.Value._Combine("; "));
                }
            }
        }

        if (embeddedAuthHeader._IsFilled())
        {
            requestMessage.Headers.Add("Authorization", embeddedAuthHeader);
        }

        if (this.Settings.DisableKeepAlive)
        {
            try
            {
                requestMessage.Headers.Add("Connection", "close");
            }
            catch { }
        }

        return requestMessage;
    }

    public virtual async Task ThrowIfErrorAsync(HttpResponseMessage res)
    {
        if (res.IsSuccessStatusCode) return;

        var ex = await CoresWebApiResponseException.CreateExceptionAsync(res);

        throw ex;
    }

    public Task<WebRet> SimpleQueryAsync(WebMethods method, string url, CancellationToken cancel = default, string? postContentType = Consts.MimeTypes.FormUrlEncoded, params (string name, string? value)[] queryList)
        => SimpleQueryAsync(method, false, url, cancel, postContentType, queryList);

    public Task<WebRet> SimpleQueryAsync(WebMethods method, bool exactMimeType, string url, CancellationToken cancel = default, string? postContentType = Consts.MimeTypes.FormUrlEncoded, params (string name, string? value)[] queryList)
        => SimpleQueryAsync(method, exactMimeType, url, null, cancel, postContentType, queryList);

    public virtual async Task<WebRet> SimpleQueryAsync(WebMethods method, bool exactMimeType, string url, WebRequestOptions? options, CancellationToken cancel = default, string? postContentType = Consts.MimeTypes.FormUrlEncoded, params (string name, string? value)[] queryList)
    {
        if (postContentType._IsEmpty()) postContentType = Consts.MimeTypes.FormUrlEncoded;
        using HttpRequestMessage r = CreateWebRequest(method, url, options, queryList);

        if (method == WebMethods.POST || method == WebMethods.PUT)
        {
            string qs = BuildQueryString(queryList);

            r.Content = new StringContent(qs, this.RequestEncoding, postContentType);

            if (exactMimeType)
            {
                // 勝手に charset を付けさせない
                MediaTypeHeaderValue mimeTypeHeader = new MediaTypeHeaderValue(postContentType);
                mimeTypeHeader.CharSet = null;
                r.Content.Headers.ContentType = mimeTypeHeader;
            }
        }

        using (HttpResponseMessage res = await this.Client.SendAsync(r, HttpCompletionOption.ResponseContentRead, cancel))
        {
            if (this.Settings.DoNotThrowHttpResultError == false)
                await ThrowIfErrorAsync(res);

            byte[] data = await res.Content.ReadAsByteArrayAsync();
            return new WebRet(this, url, res.Content.Headers._TryGetContentType(), data, res.Headers, res.IsSuccessStatusCode, res.StatusCode, res.ReasonPhrase);
        }
    }

    public static MediaTypeHeaderValue ParseContentTypeStr(string str)
    {
        if (MediaTypeHeaderValue.TryParse(str, out var ret))
        {
            return ret;
        }

        return new MediaTypeHeaderValue(str);
    }

    public virtual async Task<WebRet> SimplePostDataAsync(string url, byte[] postData, CancellationToken cancel = default, string postContentType = Consts.MimeTypes.Json, WebRequestOptions? options = null)
    {
        if (postContentType._IsEmpty()) postContentType = Consts.MimeTypes.Json;
        using HttpRequestMessage r = CreateWebRequest(WebMethods.POST, url, options);

        r.Content = new ByteArrayContent(postData);
        r.Content.Headers.ContentType = ParseContentTypeStr(postContentType);

        using (HttpResponseMessage res = await this.Client.SendAsync(r, HttpCompletionOption.ResponseContentRead, cancel))
        {
            if (this.Settings.DoNotThrowHttpResultError == false)
                await ThrowIfErrorAsync(res);

            byte[] data = await res.Content.ReadAsByteArrayAsync();
            string type = res.Content.Headers._TryGetContentType();
            return new WebRet(this, url, res.Content.Headers._TryGetContentType(), data, res.Headers, res.IsSuccessStatusCode, res.StatusCode, res.ReasonPhrase);
        }
    }


    public virtual async Task<WebRet> SimplePostJsonAsync(WebMethods method, string url, string jsonString, CancellationToken cancel = default, string postContentType = Consts.MimeTypes.Json, WebRequestOptions? options = null)
    {
        if (postContentType._IsEmpty()) postContentType = Consts.MimeTypes.Json;

        if (!(method == WebMethods.POST || method == WebMethods.PUT)) throw new ArgumentException($"Invalid method: {method.ToString()}");

        using HttpRequestMessage r = CreateWebRequest(method, url, options);

        byte[] upload_data = jsonString._GetBytes(this.RequestEncoding);

        r.Content = new ByteArrayContent(upload_data);
        r.Content.Headers.ContentType = ParseContentTypeStr(postContentType);

        using (HttpResponseMessage res = await this.Client.SendAsync(r, HttpCompletionOption.ResponseContentRead, cancel))
        {
            if (this.Settings.DoNotThrowHttpResultError == false)
                await ThrowIfErrorAsync(res);

            byte[] data = await res.Content.ReadAsByteArrayAsync();
            return new WebRet(this, url, res.Content.Headers._TryGetContentType(), data, res.Headers, res.IsSuccessStatusCode, res.StatusCode, res.ReasonPhrase);
        }
    }

    public virtual async Task<WebSendRecvResponse> HttpSendRecvDataAsync(WebSendRecvRequest request)
    {
        HttpRequestMessage r = CreateWebRequest(request.Method, request.Url, request.Options);

        if (request.RangeStart != null)
        {
            // 一部分のみ指定してダウンロード
            r.Headers.Range = new RangeHeaderValue(request.RangeStart, request.RangeLength.HasValue ? request.RangeStart + request.RangeLength - 1 : null);
        }

        if (request.Method.EqualsAny(WebMethods.POST, WebMethods.PUT))
        {
            if (request.UploadStream == null)
                throw new ArgumentException("request.UploadStream == null");

            r.Content = new StreamContent(request.UploadStream);
            r.Content.Headers.ContentType = ParseContentTypeStr(request.UploadContentType);
        }
        else
        {
            if (request.UploadStream != null)
                throw new ArgumentException($"request.UploadStream != null, but the specified method is {request.Method}.");
        }

        HttpResponseMessage res = await this.Client.SendAsync(r, HttpCompletionOption.ResponseHeadersRead, request.Cancel);
        try
        {
            if (this.Settings.DoNotThrowHttpResultError == false)
                await ThrowIfErrorAsync(res);

            return new WebSendRecvResponse(res, await res.Content.ReadAsStreamAsync());
        }
        catch
        {
            res._DisposeSafe();
            throw;
        }
    }
}

// 大変シンプルな HTTP ダウンローダ
public static class SimpleHttpDownloader
{
    public static async Task<SimpleHttpDownloaderResult> DownloadAsync(string url, WebMethods method = WebMethods.GET, bool printStatus = false, WebApiOptions? options = null, RemoteCertificateValidationCallback? sslServerCertValicationCallback = null, CancellationToken cancel = default, string? postContentType = Consts.MimeTypes.FormUrlEncoded, params (string name, string? value)[] queryList)
    {
        try
        {
            using var http = new WebApi(options, sslServerCertValicationCallback);

            if (printStatus) $"HTTP Accessing to '{url}' ..."._Print();

            var webret = await http.SimpleQueryAsync(method, url, cancel, postContentType, queryList);

            SimpleHttpDownloaderResult ret = new SimpleHttpDownloaderResult
            {
                Url = url,
                Method = method,
                ContentType = webret.ContentType,
                MediaType = webret.MediaType,
                Data = webret.Data,
                DataSize = webret.Data.Length,
                StatusCode = (int)webret.StatusCode,
            };

            if (printStatus) $"HTTP Result: code = {ret.StatusCode}, size = {ret.DataSize}"._Print();

            return ret;
        }
        catch (Exception ex)
        {
            if (printStatus) $"HTTP Result: error = {ex.Message}"._Print();

            throw;
        }
    }

    public static async Task<SimpleHttpDownloaderStreamResult> DownloadGetStreamAsync(string url, WebMethods method = WebMethods.GET, WebApiOptions? options = null, RemoteCertificateValidationCallback? sslServerCertValicationCallback = null, CancellationToken cancel = default)
    {
        var http = new WebApi(options, sslServerCertValicationCallback);

        try
        {
            using WebSendRecvRequest req = new WebSendRecvRequest(method, url, cancel);

            var res = await http.HttpSendRecvDataAsync(req);

            SimpleHttpDownloaderStreamResult ret = new SimpleHttpDownloaderStreamResult
            {
                Url = url,
                Method = method,
                ContentType = res.DownloadContentType,
                DataSize = res.DownloadContentLength,
                StatusCode = (int)res.HttpResponseMessage.StatusCode,
                Stream = res.DownloadStream,
                WebApi = http,
            };

            return ret;
        }
        catch
        {
            await http._DisposeSafeAsync();
            throw;
        }
    }

    public static async Task<SimpleHttpDownloaderStreamResult> DownloadToFileAsync(FilePath destFilePath, string url, WebMethods method = WebMethods.GET, WebApiOptions? options = null, RemoteCertificateValidationCallback? sslServerCertValicationCallback = null, ProgressReporterBase? reporter = null, CancellationToken cancel = default)
    {
        await using var result = await DownloadGetStreamAsync(url, method, options, sslServerCertValicationCallback, cancel);

        await using var file = await destFilePath.CreateAsync(additionalFlags: FileFlags.AutoCreateDirectory, cancel: cancel);

        await using var fileStream = file.GetStream(true);

        var totalSize = await result.Stream.CopyBetweenStreamAsync(fileStream, reporter: reporter, estimatedSize: result.DataSize ?? -1, cancel: cancel);

        result.DataSize = totalSize;

        return result;
    }
}

public class SimpleHttpDownloaderResult
{
    public string Url { get; set; } = "";
    public WebMethods Method { get; set; } = WebMethods.GET;
    public string ContentType { get; set; } = "";
    public byte[] Data { get; set; } = new byte[0];
    public int DataSize { get; set; }
    public int StatusCode { get; set; }
    public string MediaType { get; set; } = "";
}

public class SimpleHttpDownloaderStreamResult : IAsyncDisposable
{
    public string Url { get; set; } = "";
    public WebMethods Method { get; set; } = WebMethods.GET;
    public string ContentType { get; set; } = "";
    public long? DataSize { get; set; } = null;
    public int StatusCode { get; set; }
    public Stream Stream { get; set; } = null!;
    public WebApi WebApi { get; set; } = null!;

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
        await this.Stream._DisposeSafeAsync();

        await this.WebApi._DisposeSafeAsync();
    }
}

// 任意の Stream の一部を HTTP 応答するクラス
public partial class HttpResult : IDisposable, IAsyncDisposable
{
    public int StatusCode { get; }
    public string? ContentType { get; }

    public ReadOnlyMemory<byte> PreData { get; }
    public ReadOnlyMemory<byte> PostData { get; }

    public Stream Stream { get; }
    public long Offset { get; }
    public long? Length { get; }

    public bool DisposeStream { get; }

    public IReadOnlyList<KeyValuePair<string, string>>? AdditionalHeaders { get; }

    readonly Func<Task>? OnDisposeAsync;

    public HttpResult(Stream stream, long offset, long? length, string? contentType = Consts.MimeTypes.TextUtf8, int statusCode = Consts.HttpStatusCodes.Ok, bool disposeStream = true,
        ReadOnlyMemory<byte> preData = default, ReadOnlyMemory<byte> postData = default, IReadOnlyList<KeyValuePair<string, string>>? additionalHeaders = null, Func<Task>? onDisposeAsync = null)
    {
        try
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
            if ((length ?? long.MaxValue) < 0) throw new ArgumentOutOfRangeException(nameof(length));
            if (statusCode == 0) throw new ArgumentOutOfRangeException(nameof(statusCode));

            contentType = contentType._NullIfEmpty();

            this.Stream = stream;
            this.Offset = offset;
            this.Length = length;
            this.ContentType = contentType;
            this.StatusCode = statusCode;
            this.PreData = preData;
            this.PostData = postData;

            this.DisposeStream = disposeStream;
            this.AdditionalHeaders = additionalHeaders;
            this.OnDisposeAsync = onDisposeAsync;
        }
        catch
        {
            this._DisposeSafe();
            throw;
        }
    }

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
    async Task DisposeInternalAsync()
    {
        if (this.DisposeStream)
        {
            await this.Stream._DisposeSafeAsync();
        }

        if (this.OnDisposeAsync != null)
        {
            await this.OnDisposeAsync()._TryAwait();
        }
    }

}

// すでに用意されたメモリ配列を HTTP 応答するクラス
public class HttpMemoryResult : HttpResult
{
    public HttpMemoryResult(ReadOnlySpan<byte> data, string contentType = Consts.MimeTypes.OctetStream, int statusCode = Consts.HttpStatusCodes.Ok, IReadOnlyList<KeyValuePair<string, string>>? additionalHeadersList = null)
        : base(data._ToMemoryStream(), 0, data.Length, contentType, statusCode, additionalHeaders: additionalHeadersList) { }
}

// すでに用意された文字列データを HTTP 応答するクラス
public class HttpStringResult : HttpMemoryResult
{
    public HttpStringResult(string str, string contentType = Consts.MimeTypes.TextUtf8, int statusCode = Consts.HttpStatusCodes.Ok, Encoding? encoding = null, IReadOnlyList<KeyValuePair<string, string>>? additionalHeaders = null)
        : base(str._NonNull()._GetBytes(encoding ?? Str.Utf8Encoding), contentType, statusCode, additionalHeaders) { }
}

public class HttpErrorResult : HttpStringResult
{
    public HttpErrorResult(int statusCode, string friendlyString, string contentType = Consts.MimeTypes.TextUtf8, Encoding? encoding = null, IReadOnlyList<KeyValuePair<string, string>>? additionalHeaders = null)
        : base($"{statusCode} {(friendlyString._IsEmpty() ? "HTTP Error" : friendlyString)}", contentType, statusCode, encoding, additionalHeaders) { }
}

// 指定されたファイルを HTTP 応答するクラス
public class HttpFileResult : HttpResult
{
    static IReadOnlyList<KeyValuePair<string, string>>? Util_AddFilenameToHeadersList(IReadOnlyList<KeyValuePair<string, string>>? original, string? filename = null)
    {
        if (filename._IsEmpty()) return original;
        if (original == null) original = new List<KeyValuePair<string, string>>();

        var newList = original._CloneListFast();

        if (newList._HasKey(Consts.HttpHeaders.ContentDisposition, StrCmpi) == false)
        {
            newList.Add(new KeyValuePair<string, string>(Consts.HttpHeaders.ContentDisposition, $"attachment; filename=\"{filename}\""));
        }

        return newList;
    }

    public HttpFileResult(FileBase file, long offset, long? length, string contentType = Consts.MimeTypes.OctetStream, int statusCode = Consts.HttpStatusCodes.Ok, bool disposeFile = true,
        ReadOnlyMemory<byte> preData = default, ReadOnlyMemory<byte> postData = default, IReadOnlyList<KeyValuePair<string, string>>? additionalHeaders = null, string? filename = null)
        : base(file.GetStream(disposeFile), offset, length, contentType, statusCode, true, preData, postData, Util_AddFilenameToHeadersList(additionalHeaders, filename)) { }
}


