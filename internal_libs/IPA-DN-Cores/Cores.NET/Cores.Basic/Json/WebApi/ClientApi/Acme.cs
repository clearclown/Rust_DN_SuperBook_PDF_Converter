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

// RFC 8555: Automatic Certificate Management Environment (ACME)
// https://tools.ietf.org/html/rfc8555

#if CORES_BASIC_JSON
#if CORES_BASIC_SECURITY

using System;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using System.IO;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using System.Linq;

//using System.Net.Http;
//using System.Net.Http.Headers;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

using IPA.Cores.Basic.HttpClientCore;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System.Security.Cryptography;

#pragma warning disable CS0649

namespace IPA.Cores.Basic
{
    public static partial class CoresConfig
    {
        public static partial class AcmeClientSettings
        {
            public static readonly Copenhagen<TimeSpan> AcmeDirectoryCacheLifeTime = new TimeSpan(1, 0, 0);

            public static readonly Copenhagen<int> RetryInterval = 100;
            public static readonly Copenhagen<int> RetryIntervalMax = 10 * 1000;

            public static readonly Copenhagen<int> ShortTimeout = 10 * 1000;
            public static readonly Copenhagen<int> GiveupTime = 10 * 1000;
        }
    }
}

namespace IPA.Cores.ClientApi.Acme
{
    public class AcmeEntryPoints : IValidatable
    {
        public string? keyChange;
        public string? newAccount;
        public string? newNonce;
        public string? newOrder;
        public string? revokeCert;

        public void Validate()
        {
            if (this.keyChange._IsEmpty() ||
                this.newAccount._IsEmpty() ||
                this.newNonce._IsEmpty() ||
                this.newOrder._IsEmpty() ||
                this.revokeCert._IsEmpty())
            {
                throw new ApplicationException("ACME Directory: parameter is missing.");
            }
        }
    }

    public class AcmeCreateAccountPayload
    {
        public bool termsOfServiceAgreed;
        public string?[]? contact;
    }

    [Flags]
    public enum AcmeOrderIdType
    {
        dns = 0,
    }

    public class AcmeOrderIdEntity
    {
        [JsonConverter(typeof(StringEnumConverter))]
        public AcmeOrderIdType type;

        public string? value;
    }

    [Flags]
    public enum AcmeOrderStatus
    {
        invalid = 0,
        pending,
        ready,
        processing,
        valid,
    }

    [Flags]
    public enum AcmeAuthzStatus
    {
        invalid = 0,
        pending,
        valid,
        deactivated,
        expired,
        revoked,
    }

    [Flags]
    public enum AcmeChallengeStatus
    {
        invalid,
        pending,
        processing,
        valid,
    }

    public class AcmeOrderPayload
    {
        [JsonConverter(typeof(StringEnumConverter))]
        public AcmeOrderStatus? status;
        public AcmeOrderIdEntity?[]? identifiers;
        public string[]? authorizations;
        public string? finalize;
        public string? certificate;
    }

    public class AcmeChallengeElement
    {
        public string? type;
        public string? url;
        public string? token;
        public AcmeChallengeStatus status;
        public object? error;
        public object? validationRecord;
    }

    public class AcmeAuthzPayload
    {
        [JsonConverter(typeof(StringEnumConverter))]
        public AcmeAuthzStatus? status;
        public DateTime? expires;
        public AcmeOrderIdEntity?[]? identifiers;
        public AcmeChallengeElement?[]? challenges;
    }

    public class AcmeFinalizePayload
    {
        public string? csr;
    }

    public static class AcmeWellKnownServiceUrls
    {
        public const string LetsEncryptProduction = "https://acme-v02.api.letsencrypt.org/directory";
        public const string LetsEncryptStaging = "https://acme-staging-v02.api.letsencrypt.org/directory";
    }

    public class AcmeClientOptions
    {
        public const string DefaultEntryPointUrl = AcmeWellKnownServiceUrls.LetsEncryptProduction;

        public string DirectoryUrl { get; }
        public TcpIpSystem TcpIp { get; }

        PersistentLocalCache<AcmeEntryPoints> DirectoryWebContentsCache;

        public AcmeClientOptions(string directoryUrl = DefaultEntryPointUrl, TcpIpSystem? tcpIp = null)
        {
            DirectoryUrl = directoryUrl;
            this.TcpIp = tcpIp ?? LocalNet;

            DirectoryWebContentsCache = new PersistentLocalCache<AcmeEntryPoints>($"acme/directory_{PathParser.Windows.MakeSafeFileName(this.DirectoryUrl)}",
                CoresConfig.AcmeClientSettings.AcmeDirectoryCacheLifeTime,
                true,
                async (cancel) =>
                {
                    using (WebApi api = new WebApi(new WebApiOptions(new WebApiSettings() { SslAcceptAnyCerts = true, Timeout = CoresConfig.AcmeClientSettings.ShortTimeout })))
                    {
                        WebRet ret = await api.SimpleQueryAsync(WebMethods.GET, this.DirectoryUrl, cancel);

                        return ret.Deserialize<AcmeEntryPoints>(true)._NullCheck();
                    }
                });
        }

        public async Task<AcmeEntryPoints> GetEntryPointsAsync(CancellationToken cancel = default)
        {
            return await this.DirectoryWebContentsCache.GetAsync(cancel);
        }
    }

    public class AcmeAuthz
    {
        public AcmeOrder Order { get; }
        public AcmeAccount Account => Order.Account;

        public string Url;
        public AcmeAuthzPayload? Info { get; private set; }

        public AcmeAuthz(AcmeOrder order, string url)
        {
            this.Order = order;
            this.Url = url;
        }

        public async Task UpdateInfoAsync(CancellationToken cancel = default)
        {
            WebUserRet<AcmeAuthzPayload> ret = await Account.RequestAsync<AcmeAuthzPayload>(WebMethods.POST, this.Url, null, cancel);

            this.Info = ret.User;
        }

        public async Task ProcessAuthAsync(CancellationToken cancel = default)
        {
            AcmeChallengeElement httpChallenge = this.Info!.challenges!.Where(x => x?.type == "http-01").First()!;

            if (httpChallenge.status == AcmeChallengeStatus.valid) return;

            if (httpChallenge.status == AcmeChallengeStatus.invalid) throw new ApplicationException("httpChallenge.status == AcmeChallengeStatus.invalid");

            try
            {
                var ret = await this.Account.RequestAsync<None>(WebMethods.POST, httpChallenge.url._NullCheck(), new object(), cancel);
            }
            catch { }
        }

        public string GetChallengeErrors()
        {
            StringWriter w = new StringWriter();

            foreach (AcmeChallengeElement? c in this.Info!.challenges!)
            {
                if (c != null)
                {
                    if (c.error != null)
                    {
                        w.WriteLine(c.error._ObjectToJson());
                    }

                    if (c.validationRecord != null)
                    {
                        w.WriteLine(c.validationRecord._ObjectToJson());
                    }
                }
            }

            return w.ToString()._OneLine(" ");
        }
    }

    public class AcmeOrder
    {
        public AcmeAccount Account { get; }

        public string Url { get; }
        public AcmeOrderPayload Info { get; private set; }

        public IReadOnlyList<AcmeAuthz>? AuthzList { get; private set; }

        public AcmeOrder(AcmeAccount account, string url, AcmeOrderPayload info)
        {
            this.Account = account;
            this.Url = url;
            this.Info = info;
        }

        public async Task UpdateInfoAsync(CancellationToken cancel = default)
        {
            WebUserRet<AcmeOrderPayload> ret = await Account.RequestAsync<AcmeOrderPayload>(WebMethods.POST, this.Url, null, cancel);

            this.Info = ret.User;

            List<AcmeAuthz> authzList = new List<AcmeAuthz>();

            foreach (string authUrl in Info!.authorizations!)
            {
                AcmeAuthz a = new AcmeAuthz(this, authUrl);

                await a.UpdateInfoAsync(cancel);

                authzList.Add(a);
            }

            this.AuthzList = authzList;
        }

        public async Task ProcessAllAuthAsync(CancellationToken cancel = default)
        {
            long giveup = Time.Tick64 + CoresConfig.AcmeClientSettings.GiveupTime;

            int numRetry = 0;

            Con.WriteDebug($"ACME: Trying to process the authorization for '{this.Info.identifiers!.Where(x => x != null).Select(x => x!.value)._Combine(", ")}' ...");

            while (true)
            {
                if (giveup < Time.Tick64)
                {
                    throw new ApplicationException("ProcessAllAuthAsync: Give up.");
                }

                if (this.Info.status == AcmeOrderStatus.invalid)
                {
                    throw new ApplicationException($"Order failed. Details: \"{this.AuthzList!.Select(x => x.GetChallengeErrors())._Combine(" ")._OneLine(" ")}\"");
                }

                if (this.Info.status != AcmeOrderStatus.pending)
                {
                    // Completed
                    Con.WriteDebug($"ACME: Completed authorization for '{this.Info.identifiers!.Where(x => x != null).Select(x => x!.value)._Combine(", ")}'.");
                    return;
                }

                cancel.ThrowIfCancellationRequested();

                foreach (AcmeAuthz? auth in this.AuthzList!)
                {
                    if (auth != null)
                    {
                        if (auth.Info!.status!.Value.EqualsAny(AcmeAuthzStatus.deactivated, AcmeAuthzStatus.expired, AcmeAuthzStatus.invalid, AcmeAuthzStatus.revoked))
                        {
                            if (auth.Info.status.Value == AcmeAuthzStatus.invalid)
                            {
                                throw new ApplicationException($"Auth failed. Details: \"{auth.GetChallengeErrors()._OneLine(" ")}\"");
                            }
                            else
                            {
                                throw new ApplicationException($"auth.Info.status is {auth.Info.status.Value}");
                            }
                        }
                    }
                }

                foreach (var auth in this.AuthzList)
                {
                    if (auth.Info!.status!.Value == AcmeAuthzStatus.pending)
                    {
                        await auth.ProcessAuthAsync(cancel);
                    }
                }

                if (numRetry >= 1)
                {
                    int interval = Util.GenRandIntervalWithRetry(CoresConfig.AcmeClientSettings.RetryInterval, numRetry, CoresConfig.AcmeClientSettings.RetryIntervalMax);

                    await cancel._WaitUntilCanceledAsync(interval);
                }

                numRetry++;

                await UpdateInfoAsync(cancel);
            }
        }

        public async Task<CertificateStore> FinalizeAsync(PrivKey certPrivateKey, CancellationToken cancel, string preferredChains)
        {
            long giveup = Time.Tick64 + CoresConfig.AcmeClientSettings.GiveupTime;

            int numRetry = 0;

            while (true)
            {
                if (giveup < Time.Tick64)
                {
                    throw new ApplicationException("ProcessAllAuthAsync: Give up.");
                }

                if (this.Info.status == AcmeOrderStatus.invalid)
                {
                    throw new ApplicationException($"Order failed. Details: \"{this.AuthzList!.Select(x => x.GetChallengeErrors())._Combine(" ")._OneLine(" ")}\"");
                }
                else if (this.Info.status == AcmeOrderStatus.pending)
                {
                    await ProcessAllAuthAsync(cancel);
                    await UpdateInfoAsync(cancel);
                    giveup = Time.Tick64 + CoresConfig.AcmeClientSettings.GiveupTime;

                    continue;
                }
                else if (this.Info.status == AcmeOrderStatus.ready)
                {
                    // Create a CSR
                    Csr csr = new Csr(certPrivateKey, new CertificateOptions(certPrivateKey.Algorithm, CertificateOptionsType.ServerCertificate, this.Info.identifiers![0]!.value));

                    AcmeFinalizePayload payload = new AcmeFinalizePayload
                    {
                        csr = csr.ExportDer()._Base64UrlEncode(),
                    };

                    Con.WriteDebug($"ACME: Trying to finalize the order for '{this.Info.identifiers.Where(x => x != null).Select(x => x!.value)._Combine(", ")}' ...");

                    // Send finalize request
                    try
                    {
                        var ret = await this.Account.RequestAsync<None>(WebMethods.POST, this.Info.finalize!, payload, cancel);
                    }
                    catch { }

                    if (numRetry >= 1)
                    {
                        int interval = Util.GenRandIntervalWithRetry(CoresConfig.AcmeClientSettings.RetryInterval, numRetry, CoresConfig.AcmeClientSettings.RetryIntervalMax);

                        await cancel._WaitUntilCanceledAsync(interval);
                    }

                    numRetry++;

                    await UpdateInfoAsync(cancel);

                    continue;
                }
                else if (this.Info.status == AcmeOrderStatus.processing)
                {
                    if (numRetry >= 1)
                    {
                        int interval = Util.GenRandIntervalWithRetry(CoresConfig.AcmeClientSettings.RetryInterval, numRetry, CoresConfig.AcmeClientSettings.RetryIntervalMax);

                        await cancel._WaitUntilCanceledAsync(interval);
                    }

                    numRetry++;

                    await UpdateInfoAsync(cancel);

                    continue;
                }
                else if (this.Info.status == AcmeOrderStatus.valid)
                {
                    Con.WriteDebug($"ACME: The certificate issuance for '{this.Info.identifiers!.Where(x => x != null).Select(x => x!.value)._Combine(", ")}' is completed. Downloading...");

                    // Completed. Download the certificate
                    var certChainList = await this.Account.DownloadAllCertificateChainsAsync(WebMethods.POST, this.Info.certificate!, null, cancel);

                    Con.WriteDebug($"ACME: Downloaded the certificate for '{this.Info.identifiers!.Where(x => x != null).Select(x => x!.value)._Combine(", ")}'.");
                    Con.WriteDebug($"ACME: Count of Downloaded Chains: {certChainList.Count}");
                    int index = 0;
                    foreach (var chain in certChainList)
                    {
                        try
                        {
                            Con.WriteDebug($"ACME: Cert Chain #{index}: Tail Cert Issuer: {chain.PrimaryContainer.CertificateList.LastOrDefault()?.ToString()}");
                        }
                        catch (Exception ex)
                        {
                            ex._Debug();
                        }

                        index++;
                    }

                    var ret = CertificateUtil.SelectPreferredCertificateChain(certChainList, preferredChains);

                    Con.WriteDebug($"Preferred Chains Str: \"{preferredChains}\"");
                    Con.WriteDebug($"ACME: Selected Chain Tail Cert Issuer: {ret.PrimaryContainer.CertificateList.LastOrDefault()?.ToString()}");

                    return ret;
                }
                else
                {
                    throw new ApplicationException($"Invalid status: {this.Info.status}");
                }
            }
        }
    }

    public class AcmeAccount
    {
        public AcmeClient Client { get; }
        public PrivKey PrivKey { get; }
        public string AccountUrl { get; }
        public AcmeClientOptions Options => Client.Options;

        internal AcmeAccount(EnsureInternal yes, AcmeClient client, PrivKey privKey, string accountUrl)
        {
            this.Client = client;
            this.PrivKey = privKey;
            this.AccountUrl = accountUrl;
        }

        public async Task<AcmeOrder> NewOrderAsync(IEnumerable<string> dnsNames, CancellationToken cancel = default)
        {
            Con.WriteDebug($"ACME: Putting the new certificate order for '{dnsNames._Combine(", ")}' ...");

            List<AcmeOrderIdEntity> o = new List<AcmeOrderIdEntity>();

            foreach (string dnsName in dnsNames)
            {
                o.Add(new AcmeOrderIdEntity { type = AcmeOrderIdType.dns, value = dnsName, });
            }

            AcmeOrderPayload request = new AcmeOrderPayload
            {
                identifiers = o.ToArray(),
            };

            WebUserRet<AcmeOrderPayload> ret = await RequestAsync<AcmeOrderPayload>(WebMethods.POST, (await Options.GetEntryPointsAsync(cancel)).newOrder!, request, cancel);

            string accountUrl = ret.System.Headers.GetValues("Location").Single();

            AcmeOrder order = new AcmeOrder(this, accountUrl, ret.User);

            await order.UpdateInfoAsync(cancel);

            Con.WriteDebug($"ACME: The order is put.");

            return order;
        }
        public async Task<AcmeOrder> NewOrderAsync(string dnsName, CancellationToken cancel = default)
            => await NewOrderAsync(dnsName._SingleArray(), cancel);

        public Task<WebUserRet<TResponse>> RequestAsync<TResponse>(WebMethods method, string url, object? request, CancellationToken cancel = default)
        {
            return this.Client.RequestAsync<TResponse>(method, this.PrivKey, this.AccountUrl, url, request, cancel);
        }

        public async Task<List<CertificateStore>> DownloadAllCertificateChainsAsync(WebMethods method, string url, object? request, CancellationToken cancel = default)
        {
            List<CertificateStore> ret = new List<CertificateStore>();

            List<WebRet> webRetList = new List<WebRet>();

            // メインの証明書チェーンをダウンロードする
            var mainRet = await DownloadAsync(method, url, request, cancel);

            webRetList.Add(mainRet);

            // ダウンロード時にサーバーから提示されたヘッダに WebLinking で "alternate" URL が提示されていればそれを順にダウンロードする
            // https://datatracker.ietf.org/doc/html/rfc8555#section-7.4.2
            foreach (var alternateUrl in mainRet.ParseWebLinks().Where(x => x.Item2.Where(x => x.Key._IsSamei("rel") && x.Value._IsSamei("alternate")).Any()).Select(x => x.Item1))
            {
                for (int i = 0; i < 3; i++)
                {
                    // 追加チェーンのダウンロードは、それぞれ 3 回トライする。ただし、全部失敗したら諦める。
                    try
                    {
                        var additionalWebRet = await DownloadAsync(method, alternateUrl, request, cancel);

                        webRetList.Add(additionalWebRet);

                        break;
                    }
                    catch (Exception ex)
                    {
                        ex._Debug();
                    }
                }
            }

            foreach (var dat in webRetList.Select(x => x.Data))
            {
                CertificateStore cs = new CertificateStore(dat, this.PrivKey);

                ret.Add(cs);
            }

            return ret;
        }

        public async Task<WebRet> DownloadAsync(WebMethods method, string url, object? request, CancellationToken cancel = default)
        {
            var webret = await this.Client.DownloadAsync(method, this.PrivKey, this.AccountUrl, url, request, cancel);

            return webret;
        }

        public string ProcessChallengeRequest(string token)
        {
            string keyThumbprintBase64 = JwsUtil.CreateJwsKey(this.PrivKey.PublicKey, out _, out _).CalcThumbprint()._Base64UrlEncode();

            Con.WriteLine($"ACME: Processing the challenge token '{token}'.");

            return token + "." + keyThumbprintBase64;
        }
    }

    public class AcmeClient : IDisposable
    {
        public AcmeClientOptions Options { get; }

        WebApi Web;

        public AcmeClient(AcmeClientOptions options)
        {
            this.Options = options;

            this.Web = new WebApi(new WebApiOptions(new WebApiSettings() { SslAcceptAnyCerts = true, Timeout = CoresConfig.AcmeClientSettings.ShortTimeout, AllowAutoRedirect = false, }, Options.TcpIp));
            this.Web.AddHeader("User-Agent", "AcmeClient/1.0");
        }

        public async Task<string> GetNonceAsync(CancellationToken cancel = default)
        {
            AcmeEntryPoints url = await Options.GetEntryPointsAsync(cancel);

            WebRet response = await Web.SimpleQueryAsync(WebMethods.HEAD, url.newNonce!, cancel, Consts.MimeTypes.JoseJson);

            string ret = response.Headers.GetValues("Replay-Nonce").Single();

            if (ret._IsEmpty()) throw new ApplicationException("Replay-Nonce is empty.");

            return ret;
        }

        //public async Task<byte[]> DownloadAsync(WebMethods method, string url, CancellationToken cancel = default)
        //{
        //    WebRet ret = await Web.SimpleQueryAsync(method, true, url, cancel, Consts.MimeTypes.JoseJson);

        //    return ret.Data;
        //}

        public async Task<WebUserRet<TResponse>> RequestAsync<TResponse>(WebMethods method, PrivKey key, string? kid, string url, object? request, CancellationToken cancel = default)
        {
            string nonce = await GetNonceAsync(cancel);

            //("*** " + url)._Debug();

            WebRet webret = await Web.RequestWithJwsObjectAsync(method, key, kid, nonce, url, request, cancel, Consts.MimeTypes.JoseJson);

            TResponse? ret = webret.Deserialize<TResponse>(true);

            //webret.Headers._DebugHeaders();
            //webret.ToString()._Debug();

            return webret.CreateUserRet(ret!);
        }

        public async Task<WebRet> DownloadAsync(WebMethods method, PrivKey key, string? kid, string url, object? request, CancellationToken cancel = default)
        {
            string nonce = await GetNonceAsync(cancel);

            //("*** " + url)._Debug();

            WebRet webret = await Web.RequestWithJwsObjectAsync(method, key, kid, nonce, url, request, cancel, Consts.MimeTypes.JoseJson);

            return webret;
        }

        public async Task<AcmeAccount> LoginAccountAsync(PrivKey key, string[] contacts, CancellationToken cancel = default)
        {
            AcmeEntryPoints url = await Options.GetEntryPointsAsync(cancel);

            AcmeCreateAccountPayload req = new AcmeCreateAccountPayload()
            {
                contact = contacts,
                termsOfServiceAgreed = true,
            };

            WebUserRet<object> ret = await this.RequestAsync<object>(WebMethods.POST, key, null, url.newAccount!, req, cancel);

            string accountUrl = ret.System.Headers.GetValues("Location").Single();

            if (accountUrl._IsEmpty()) throw new ApplicationException("Account Location is empty.");

            return new AcmeAccount(EnsureInternal.Yes, this, key, accountUrl);
        }

        public void Dispose() { this.Dispose(true); GC.SuppressFinalize(this); }
        Once DisposeFlag;
        protected virtual void Dispose(bool disposing)
        {
            if (!disposing || DisposeFlag.IsFirstCall() == false) return;

            this.Web._DisposeSafe();
        }
    }
}

#endif  // CORES_BASIC_JSON
#endif  // CORES_BASIC_SECURITY;

