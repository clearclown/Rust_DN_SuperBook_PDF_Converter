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

#if CORES_CODES_IISUTIL

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
using System.Runtime.CompilerServices;
using System.Security.Cryptography.X509Certificates;

using Microsoft.Web.Administration;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

using IPA.Cores.Codes;
using IPA.Cores.Helper.Codes;
using static IPA.Cores.Globals.Codes;

namespace IPA.Cores.Codes;

public class IisAdmin : AsyncService
{
    ServerManager Svr;
    X509Store CertStore;

    public IisAdmin()
    {
        try
        {
            this.Svr = new ServerManager();

            this.CertStore = new X509Store(StoreName.My, StoreLocation.LocalMachine);

            this.CertStore.Open(OpenFlags.ReadWrite);
        }
        catch
        {
            this._DisposeSafe();
            throw;
        }
    }

    public Dictionary<string, Tuple<Certificate, bool>> GetCurrentMachineCertificateList()
    {
        Dictionary<string, Tuple<Certificate, bool>> ret = new Dictionary<string, Tuple<Certificate, bool>>(StrComparer.IgnoreCaseComparer);

        foreach (var item in this.CertStore.Certificates)
        {
            var cert = item.AsPkiCertificate();

            // Is this certificate having a private key?
            bool hasPrivateKey = false;

            try
            {
                if (item.HasPrivateKey)
                {
                    if (item.GetRSAPrivateKey() != null)
                    {
                        hasPrivateKey = true;
                    }
                }
            }
            catch { }

            ret.Add(cert.DigestSHA1Str, new Tuple<Certificate, bool>(cert, hasPrivateKey));
        }

        return ret;
    }

    public class BindItem
    {
        public string SiteName = "";
        public string BindingInfo = "";
        public string HostName = "";
        public Certificate Cert = null!;
        public bool HasPrivateKey;
        public CertificateStore? NewCert;
    }

    List<BindItem> GetIisCertBindings(Dictionary<string, Tuple<Certificate, bool>> certs)
    {
        List<BindItem> bindItems = new List<BindItem>();

        // サーバーに存在するすべての証明書バインディングを取得
        foreach (var site in Svr.Sites.Where(x => x.Name._IsFilled()).OrderBy(x => x.Name))
        {
            foreach (var bind in site.Bindings.Where(x => x.BindingInformation._IsFilled()).OrderBy(x => x.BindingInformation))
            {
                if (bind.Protocol._IsSamei("https"))
                {
                    if (bind.CertificateHash != null && bind.CertificateHash.Length >= 1)
                    {
                        string hash = bind.CertificateHash._GetHexString();

                        if (certs.TryGetValue(hash, out Tuple<Certificate, bool>? certTuple))
                        {
                            var cert = certTuple.Item1;

                            BindItem item = new BindItem
                            {
                                BindingInfo = bind.BindingInformation,
                                Cert = cert,
                                SiteName = site.Name,
                                HostName = bind.Host._NormalizeFqdn()._NonNullTrim(),
                                HasPrivateKey = certTuple.Item2,
                            };

                            bindItems.Add(item);
                        }
                    }
                }
                else if (bind.Protocol._IsSamei("ftp"))
                {
                    var ftpServer = site.GetChildElement("ftpServer");
                    if (ftpServer != null)
                    {
                        var security = ftpServer.GetChildElement("security");
                        if (security != null)
                        {
                            var ssl = security.GetChildElement("ssl");
                            if (ssl != null)
                            {
                                string hash = (string)ssl.Attributes["serverCertHash"].Value;
                                if (hash._IsFilled())
                                {
                                    hash = hash._NormalizeHexString();

                                    if (certs.TryGetValue(hash, out Tuple<Certificate, bool>? certTuple))
                                    {
                                        var cert = certTuple.Item1;

                                        BindItem item = new BindItem
                                        {
                                            BindingInfo = "ftp",
                                            Cert = cert,
                                            SiteName = site.Name,
                                            HasPrivateKey = certTuple.Item2,
                                        };

                                        bindItems.Add(item);
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        return bindItems;
    }

    public void UpdateCerts(IEnumerable<CertificateStore> certsList, bool updateSameCert, string emptySiteAs)
    {
        int numWarningTotal = 0;
        int warningCerts = 0;

        // サーバーに存在するすべての証明書リストを取得
        var currentCertDict = GetCurrentMachineCertificateList();

        // 取得した証明書が古くなっていれば警告を出す
        DateTimeOffset threshold2 = DtOffsetNow.AddDays(50);
        foreach (var cert in certsList.Select(x => x.PrimaryCertificate).OrderBy(x => x.CommonNameOrFirstDnsName, StrComparer.FqdnReverseStrComparer))
        {
            if (cert.ExpireSpan < Consts.Numbers.MaxCertExpireSpanTargetForUpdate)
            {
                if (cert.NotAfter < threshold2)
                {
                    Con.WriteLine($"Warning: A supplied certificate is expiring or expired. Please check! Cert: '{cert.ToString()}'", flags: LogFlags.Heading);
                    warningCerts++;
                    numWarningTotal++;
                }
            }
        }

        // IIS のバインディング情報を取得
        List<BindItem> bindItems = GetIisCertBindings(currentCertDict);

        Con.WriteLine();

        Con.WriteLine($"The IIS server has {bindItems.Count} SSL bindings.");
        int index = 0;
        foreach (var bind in bindItems.OrderBy(x => x.SiteName, StrComparer.IgnoreCaseComparer).ThenBy(x => x.BindingInfo, StrComparer.IgnoreCaseComparer))
        {
            index++;
            Con.WriteLine($"Binding #{index}/{bindItems.Count}: '{bind.SiteName}' - '{bind.BindingInfo}' (Hostname: '{bind.HostName._NonNull()}'): '{bind.Cert}' (hasPrivateKey: {bind.HasPrivateKey})");
        }

        Con.WriteLine();

        // サーバーに存在するすべての証明書バインディングについて検討し、更新すべき証明書をマーク
        // ただしもともと有効期限が 約 3 年間よりも長い証明書が登録されている場合は、意図的に登録されているオレオレ証明書であるので、更新対象としてマークしない
        foreach (var bind in bindItems.Where(x => x.Cert.ExpireSpan < Consts.Numbers.MaxCertExpireSpanTargetForUpdate))
        {
            var cert = bind.Cert;

            bool forceUpdate = false;

            if (bind.HasPrivateKey == false)
            {
                // Current binded certificate has no private key. It should be error. Therefore update this certificate forcefully.
                forceUpdate = true;

                numWarningTotal++;
                Con.WriteLine($"Warning: the current binding '{bind.SiteName}' - '{bind.BindingInfo}': '{bind.Cert}' has no private key. Thus we update this binding forcefully.", flags: LogFlags.Heading);
            }

            List<CertificateStore> newCandidateCerts = new List<CertificateStore>();

            if (cert.IsMultipleSanCertificate() == false && cert.HostNameList.Any(x => x.HostName._InStri("iis-default-short-ssl-cert")) == false)
            {
                // すでに登録されている証明書がシングル証明書の場合、その証明書の DNS 名から、この binding が現在どのホスト名での使用を意図しているものであるのか判別する
                foreach (var certDns in cert.HostNameList)
                {
                    if (certDns.Type == CertificateHostnameType.Wildcard)
                    {
                        // *.example.org のようなワイルドカードホスト名について、完全一致するものがある場合
                        var candidates = certsList.Where(x => x.PrimaryCertificate.HostNameList.Any(x => x.Type == CertificateHostnameType.Wildcard && x.HostName._IsSamei(certDns.HostName)));

                        candidates._DoForEach(x => newCandidateCerts.Add(x));
                    }
                }

                if (newCandidateCerts.Any() == false)
                {
                    // 既存の証明書のワイルドカードと完全一致するものがない場合
                    foreach (var certDns in cert.HostNameList)
                    {
                        if (certDns.Type == CertificateHostnameType.SingleHost)
                        {
                            // aaa.example.org のような普通のホスト名
                            var candidates = certsList.GetHostnameMatchedCertificatesList(certDns.HostName);

                            candidates._DoForEach(x => newCandidateCerts.Add(x.Item1));
                        }
                    }
                }
            }
            else
            {
                if (bind.BindingInfo._IsSamei("ftp"))
                {
                    // FTP の場合で、すでに登録されている証明書が SAN 複数 DNS 名保持証明書の場合、判断ができないので更新はしない
                    if (emptySiteAs._IsFilled())
                    {
                        // EMPTYSITEAS が指定されているので、その指定されている文字列がホスト名であると仮定して処理をしてみます。
                        var candidates = certsList.GetHostnameMatchedCertificatesList(emptySiteAs);

                        candidates._DoForEach(x => newCandidateCerts.Add(x.Item1));
                    }
                }
                else
                {
                    // すでに登録されている証明書が SAN 複数 DNS 名保持証明書の場合、証明書から意図している DNS ホスト名は分からない。
                    // そこで、binding のホスト名を参考にして判断する。
                    if (bind.HostName._IsFilled())
                    {
                        var candidates = certsList.GetHostnameMatchedCertificatesList(bind.HostName);

                        candidates._DoForEach(x => newCandidateCerts.Add(x.Item1));
                    }
                    else
                    {
                        // ホスト名が空の場合はどうすれば良いかわからないので 警告を 出します！！
                        if (emptySiteAs._IsFilled())
                        {
                            // ホスト名は空ですが、EMPTYSITEAS が指定されているので、その指定されている文字列がホスト名であると仮定して処理をしてみます。
                            var candidates = certsList.GetHostnameMatchedCertificatesList(emptySiteAs);

                            candidates._DoForEach(x => newCandidateCerts.Add(x.Item1));
                        }
                    }
                }
            }

            if (newCandidateCerts.Any() == false)
            {
                // 警告さん
                numWarningTotal++;
                Con.WriteLine($"Warning: We could not determine the best certificate for the binding '{bind.SiteName}' - '{bind.BindingInfo}': '{bind.Cert}'", flags: LogFlags.Heading);
            }

            // 更新候補に挙げられた証明書リストの中で最も有効期限が長いものを選択
            var bestCert = newCandidateCerts.OrderByDescending(x => x.PrimaryCertificate.NotAfter).ThenBy(x => x.DigestSHA1Str).FirstOrDefault();
            if (bestCert != null)
            {
                // この最も有効期限が長い候補の証明書と、現在登録されている証明書との有効期限を比較し、候補証明書のほうが発行日が新しいか、または期限が長ければ、更新する
                if (forceUpdate || bestCert.NotBefore > cert.NotBefore || bestCert.NotAfter > cert.NotAfter || (updateSameCert && bestCert.DigestSHA1Str._IsSameHex(cert.DigestSHA1Str)))
                {
                    // 更新対象としてマーク
                    bind.NewCert = bestCert;
                }
            }
        }

        // 更新対象としてマークされた証明書を証明書ストアに書き込む
        HashSet<string> newCertsHashList = new HashSet<string>(StrComparer.IgnoreCaseComparer);
        bindItems.Where(x => x.NewCert != null).Select(x => x.NewCert!.DigestSHA1Str!)._DoForEach(x => newCertsHashList.Add(x));

        foreach (var hash in newCertsHashList.OrderBy(x => x))
        {
            if (currentCertDict.ContainsKey(hash) == false || currentCertDict[hash].Item2 == false)
            {
                var certToWrite = certsList.Where(x => x.DigestSHA1Str._IsSamei(hash)).First();

                X509Certificate2 certObj = certToWrite.GetX509Certificate2ForAddToWindowsCertStore();

#pragma warning disable CA1416 // プラットフォームの互換性を検証
                certObj.FriendlyName = certToWrite.GenerateFriendlyName();
#pragma warning restore CA1416 // プラットフォームの互換性を検証

                this.CertStore.Add(certObj);
            }
        }

        // 念のため証明書ストアにすべての証明書の書き込みが成功したかどうか検査する
        var certDict2 = GetCurrentMachineCertificateList();

        foreach (var hash in newCertsHashList.OrderBy(x => x))
        {
            var certTuple = certDict2[hash];

            var cert = certTuple.Item1;

            if (cert.DigestSHA1Str._IsSameHex(hash) == false)
            {
                throw new CoresLibException("Invalid certificate status! hash: " + hash);
            }

            if (certTuple.Item2 == false)
            {
                throw new CoresLibException("Added certificate has no private key! hash: " + hash);
            }
        }

        int numUpdates = 0;

        // IIS のバインディング設定を更新する
        foreach (var site in Svr.Sites.Where(x => x.Name._IsFilled()).OrderBy(x => x.Name))
        {
            foreach (var bind in site.Bindings.Where(x => x.BindingInformation._IsFilled()).OrderBy(x => x.BindingInformation))
            {
                if (bind.Protocol._IsSamei("https"))
                {
                    var item = bindItems.Where(x => x.NewCert != null && x.SiteName == site.Name && x.BindingInfo == bind.BindingInformation).SingleOrDefault();

                    if (item != null)
                    {
                        bind.CertificateHash = item.NewCert!.DigestSHA1Str._GetHexBytes();

                        Con.WriteLine($"UpdatedHttpsCert: Site = '{item.SiteName}', Binding = '{item.BindingInfo}', newCert = '{item.NewCert}', oldCert = '{item.Cert}'");

                        numUpdates++;
                    }
                }
                else if (bind.Protocol._IsSamei("ftp"))
                {
                    var ftpServer = site.GetChildElement("ftpServer");
                    if (ftpServer != null)
                    {
                        var security = ftpServer.GetChildElement("security");
                        if (security != null)
                        {
                            var ssl = security.GetChildElement("ssl");
                            if (ssl != null)
                            {
                                string hash = (string)ssl.Attributes["serverCertHash"].Value;
                                if (hash._IsFilled())
                                {
                                    hash = hash._NormalizeHexString();

                                    var item = bindItems.Where(x => x.NewCert != null && x.SiteName == site.Name && x.BindingInfo == "ftp").SingleOrDefault();

                                    if (item != null)
                                    {
                                        ssl["serverCertHash"] = item.NewCert!.DigestSHA1Str;

                                        Con.WriteLine($"UpdatedFtpCert: Site = '{item.SiteName}', Binding = '{item.BindingInfo}', newCert = '{item.NewCert}', oldCert = '{item.Cert}'");

                                        numUpdates++;
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        if (numUpdates >= 1)
        {
            Con.WriteLine($"Total {numUpdates} certs updated.", flags: LogFlags.Heading);

            CoresLib.Report_SimpleResult = $"Updated {numUpdates} certs";

            Svr.CommitChanges();
        }
        else
        {
            Con.WriteLine($"No certs updated.", flags: LogFlags.Heading);

            CoresLib.Report_SimpleResult = $"Updated No certs";
        }

        // 再度 IIS のバインディング情報を取得して証明書の有効期限が怪しいものがあれば表示する
        // 28 日以内に有効期限が切れる証明書を検出して警告を出す
        bindItems = GetIisCertBindings(currentCertDict);
        DateTimeOffset threshold = DtOffsetNow.AddDays(28);
        foreach (var item in bindItems)
        {
            if (item.Cert.ExpireSpan < Consts.Numbers.MaxCertExpireSpanTargetForUpdate)
            {
                if (item.Cert.NotAfter < threshold)
                {
                    Con.WriteLine($"Warning: IIS bound certificate is expiring or expired. Site: '{item.SiteName}', Binding: '{item.BindingInfo}', Cert: '{item.Cert.ToString()}'", flags: LogFlags.Heading);
                    warningCerts++;
                    numWarningTotal++;
                }
            }
        }
        if (warningCerts >= 1)
        {
            Con.WriteLine($"Warning! There are {warningCerts} certificates with warning! Please consider to check if it is Ok!!", flags: LogFlags.Heading);
        }

        if (numWarningTotal >= 1)
        {
            CoresLib.Report_SimpleResult += $" (Warnings: {numWarningTotal})";
        }
    }

    public void Test()
    {
        foreach (var site in Svr.Sites)
        {
            site.Name._Print();
            foreach (var b in site.Bindings)
            {
                $"  - {b.Protocol}"._Print();
            }
        }
    }

    protected override async Task CleanupImplAsync(Exception? ex)
    {
        try
        {
            try
            {
                this.CertStore.Close();
            }
            catch (Exception ex2)
            {
                ex2._Debug();
            }
            this.CertStore._DisposeSafe();
        }
        finally
        {
            await base.CleanupImplAsync(ex);
        }
    }
}

#endif

