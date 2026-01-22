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
using System.Text;
using System.Collections.Specialized;
using System.IO;
using System.Net;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

namespace IPA.Cores.Basic.Legacy;

public class DnHttpPostData
{
    NameValueCollection nv = new NameValueCollection();

    public void Add(string name, string value)
    {
        nv.Add(name, value);
    }

    public string CurrentString
    {
        get
        {
            StringBuilder sb = new StringBuilder();
            bool b = false;

            foreach (string? name in nv.Keys)
            {
                string value = nv[name]._NonNull();

                string tmp = "";

                if (b)
                {
                    tmp += "&";
                }

                b = true;

                tmp += string.Format("{0}={1}", name, value);

                sb.Append(tmp);
            }

            return sb.ToString();
        }
    }

    public Buf GetData(Encoding enc)
    {
        return new Buf(enc.GetBytes(this.CurrentString));
    }
}

public class DnHttpClient
{
    CookieContainer cc = new CookieContainer();
    public CookieContainer CookieContainer
    {
        get { return cc; }
    }

    public bool NoCookie = false;
    public bool Http1_1 = false;
    public string? UserAgent = null;

    public string? ProxyHostname = null;
    public int ProxyPort = 0;

    TimeSpan timeout = new TimeSpan(0, 0, 15);
    public TimeSpan Timeout
    {
        get { return timeout; }
        set { timeout = value; }
    }

    int timeoutInt
    {
        get
        {
            if (this.timeout.Ticks == 0)
            {
                return System.Threading.Timeout.Infinite;
            }
            else
            {
                return (int)this.timeout.TotalMilliseconds;
            }
        }
    }

    static bool OnRemoteCertificateValidationCallback(Object sender, System.Security.Cryptography.X509Certificates.X509Certificate? certificate,
        System.Security.Cryptography.X509Certificates.X509Chain? chain, System.Net.Security.SslPolicyErrors sslPolicyErrors)
    {
        return true;
    }

    static DnHttpClient()
    {
        ServicePointManager.ServerCertificateValidationCallback = new System.Net.Security.RemoteCertificateValidationCallback(OnRemoteCertificateValidationCallback);
    }

    public Buf Get(Uri uri)
    {
#pragma warning disable SYSLIB0014 // 型またはメンバーが旧型式です
        HttpWebRequest r = (HttpWebRequest)HttpWebRequest.Create(uri);
#pragma warning restore SYSLIB0014 // 型またはメンバーが旧型式です

        if (this.NoCookie == false)
        {
            r.CookieContainer = this.cc;
        }
        else
        {
            r.CookieContainer = new CookieContainer();
        }

        if (Str.IsEmptyStr(this.ProxyHostname) == false && this.ProxyPort != 0)
        {
            r.Proxy = new WebProxy(this.ProxyHostname, this.ProxyPort);
        }

        r.UnsafeAuthenticatedConnectionSharing = true;
        r.AllowAutoRedirect = true;
        if (Http1_1 == false)
        {
            r.KeepAlive = false;
            r.ProtocolVersion = new Version(1, 0);
        }
        r.Timeout = this.timeoutInt;
        r.CachePolicy = new System.Net.Cache.RequestCachePolicy(System.Net.Cache.RequestCacheLevel.NoCacheNoStore);
        r.Method = "GET";

        if (this.UserAgent != null)
        {
            r.UserAgent = this.UserAgent;
        }

        WebResponse res = r.GetResponse();

        Stream st = res.GetResponseStream();

        return Buf.ReadFromStream(st);
    }

    public Buf Post(Uri uri, DnHttpPostData postData, Encoding postEncoding)
    {
        return Post(uri, postData, postEncoding, null);
    }
    public Buf Post(Uri uri, DnHttpPostData postData, Encoding postEncoding, string? referer)
    {
        return Post(uri, postData.GetData(postEncoding).ByteData, referer);
    }
    public Buf Post(Uri uri, byte[] postData, string? referer)
    {
        return Post(uri, postData, referer, Consts.MimeTypes.FormUrlEncoded);
    }
    public Buf Post(Uri uri, byte[] postData, string? referer, string content_type)
    {
#pragma warning disable SYSLIB0014 // 型またはメンバーが旧型式です
        HttpWebRequest r = (HttpWebRequest)HttpWebRequest.Create(uri);
#pragma warning restore SYSLIB0014 // 型またはメンバーが旧型式です

        if (this.NoCookie == false)
        {
            r.CookieContainer = this.cc;
        }
        else
        {
            r.CookieContainer = new CookieContainer();
        }

        if (Str.IsEmptyStr(this.ProxyHostname) == false && this.ProxyPort != 0)
        {
            r.Proxy = new WebProxy(this.ProxyHostname, this.ProxyPort);
        }

        r.UnsafeAuthenticatedConnectionSharing = true;
        r.AllowAutoRedirect = true;
        if (Http1_1 == false)
        {
            r.KeepAlive = false;
            r.ProtocolVersion = new Version(1, 0);
        }
        r.Timeout = this.timeoutInt;
        r.CachePolicy = new System.Net.Cache.RequestCachePolicy(System.Net.Cache.RequestCacheLevel.NoCacheNoStore);
        r.Method = "POST";

        if (this.UserAgent != null)
        {
            r.UserAgent = this.UserAgent;
        }

        if (Str.IsEmptyStr(referer) == false)
        {
            r.Referer = referer;
        }

        Buf data = new Buf(postData);
        r.ContentType = content_type;
        r.ContentLength = data.Size;

        Stream st2 = r.GetRequestStream();
        st2.Write(data.ByteData, 0, (int)data.Size);
        st2.Flush();
        st2.Close();

        WebResponse res = r.GetResponse();

        Stream st = res.GetResponseStream();

        return Buf.ReadFromStream(st);
    }
}
