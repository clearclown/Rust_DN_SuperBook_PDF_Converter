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

#if CORES_BASIC_JSON
#if CORES_BASIC_SECURITY

using System;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using System.IO;
using System.Diagnostics.CodeAnalysis;

using Newtonsoft.Json;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using Org.BouncyCastle.Asn1.Pkcs;

namespace IPA.Cores.Basic;

public class JwsPacket
{
    [JsonProperty("protected")]
    public string? Protected;

    public string? payload;

    public string? signature;
}

public class JwsKey
{
    // Members must be lexicographic order (https://tools.ietf.org/html/rfc7638#section-3)
    public string? crv;
    public string? e;
    public string? kty;
    public string? n;
    public string? x;
    public string? y;

    public byte[] CalcThumbprint()
    {
        string str = this._ObjectToJson(includeNull: false, compact: true);

        return Secure.HashSHA256(str._GetBytes_UTF8());
    }
}

public class JwsProtected
{
    public string? alg;
    public JwsKey? jwk;
    public string? nonce;
    public string? url;
    public string? kid;
}

public static class JwsUtil
{
    public static JwsKey CreateJwsKey(PubKey key, out string algName, out string signerName)
    {
        JwsKey jwk;

        switch (key.Algorithm)
        {
            case PkiAlgorithm.ECDSA:
                jwk = new JwsKey()
                {
                    kty = "EC",
                    crv = "P-" + key.BitsSize,
                    x = key.EcdsaParameters.Q.AffineXCoord.GetEncoded()._Base64UrlEncode(),
                    y = key.EcdsaParameters.Q.AffineYCoord.GetEncoded()._Base64UrlEncode(),
                };

                switch (key.BitsSize)
                {
                    case 256:
                        algName = "ES256";
                        signerName = "SHA-256withPLAIN-ECDSA";
                        break;

                    case 384:
                        algName = "ES384";
                        signerName = "SHA-384withPLAIN-ECDSA";
                        break;

                    default:
                        throw new ArgumentException("Unsupported key length.");
                }

                break;

            case PkiAlgorithm.RSA:
                jwk = new JwsKey()
                {
                    kty = "RSA",
                    n = key.RsaParameters.Modulus.ToByteArray()._Base64UrlEncode(),
                    e = key.RsaParameters.Exponent.ToByteArray()._Base64UrlEncode(),
                };

                algName = "RS256";
                signerName = PkcsObjectIdentifiers.Sha256WithRsaEncryption.Id;
                break;

            default:
                throw new ArgumentException("Unsupported key.Algorithm.");
        }

        return jwk;
    }

    public static JwsPacket Encapsulate(PrivKey key, string? kid, string nonce, string url, object? payload)
    {
        JwsKey jwk = CreateJwsKey(key.PublicKey, out string algName, out string signerName);

        JwsProtected protect = new JwsProtected()
        {
            alg = algName,
            jwk = kid._IsEmpty() ? jwk : null,
            kid = kid._IsEmpty() ? null : kid,
            nonce = nonce,
            url = url,
        };

        JwsPacket ret = new JwsPacket()
        {
            Protected = protect._ObjectToJson(base64url: true, includeNull: true),
            payload = (payload == null ? "" : payload._ObjectToJson(base64url: true)),
        };

        var signer = key.GetSigner(signerName);

        byte[] signature = signer.Sign((ret.Protected + "." + ret.payload)._GetBytes_Ascii());

        ret.signature = signature._Base64UrlEncode();

        return ret;
    }
}

public partial class WebApi
{
    public virtual async Task<WebRet> RequestWithJwsObjectAsync(WebMethods method, PrivKey privKey, string? kid, string nonce, string url, object? payload, CancellationToken cancel = default, string postContentType = Consts.MimeTypes.Json)
    {
        JwsPacket reqPacket = JwsUtil.Encapsulate(privKey, kid, nonce, url, payload);

        return await this.RequestWithJsonObjectAsync(method, url, reqPacket, cancel, postContentType);
    }
}

#endif  // CORES_BASIC_SECURITY
#endif  // CORES_BASIC_JSON

