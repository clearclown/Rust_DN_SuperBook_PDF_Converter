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

#if CORES_BASIC_SECURITY

using System;
using System.IO;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Encodings;
using Org.BouncyCastle.OpenSsl;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;
using Org.BouncyCastle.Crypto.Macs;
using Org.BouncyCastle.Crypto.Parameters;

using IPA.Cores.Basic;
using IPA.Cores.Basic.Legacy;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

namespace IPA.Cores.Basic
{
    namespace Legacy
    {
        // Rsa アルゴリズム
        public class Rsa
        {
            byte[]? data;
            Cert? cert;
            static object lockObj = new object();

            public Rsa(byte[] data)
            {
                init(data);
            }
            public Rsa(string filename)
            {
                Buf b = Buf.ReadFromFile(filename);
                init(b.ByteData);
            }
            public Rsa(Buf buf)
            {
                init(buf.ByteData);
            }
            void init(byte[] data)
            {
                this.data = (byte[])data.Clone();
                this.cert = null;
            }

            public Rsa(Cert cert)
            {
                init(cert);
            }
            void init(Cert cert)
            {
                this.cert = (Cert)cert.Clone();
                this.data = null;
            }

            public byte[] SignData(byte[] data)
            {
                lock (lockObj)
                {
                    byte[] ret;
                    using (RsaInner rsa = new RsaInner(this.data!, this.cert!))
                    {
                        ret = rsa.SignData(data);
                    }
                    return ret;
                }
            }

            public byte[] SignHash(byte[] hash)
            {
                lock (lockObj)
                {
                    byte[] ret;
                    using (RsaInner rsa = new RsaInner(this.data!, this.cert!))
                    {
                        ret = rsa.SignHash(hash);
                    }
                    return ret;
                }
            }

            public bool VerifyData(byte[] data, byte[] sign)
            {
                lock (lockObj)
                {
                    bool ret;
                    using (RsaInner rsa = new RsaInner(this.data!, this.cert!))
                    {
                        ret = rsa.VerifyData(data, sign);
                    }
                    return ret;
                }
            }

            public bool VerifyHash(byte[] hash, byte[] sign)
            {
                lock (lockObj)
                {
                    bool ret;
                    using (RsaInner rsa = new RsaInner(this.data!, this.cert!))
                    {
                        ret = rsa.VerifyHash(hash, sign);
                    }
                    return ret;
                }
            }

            public byte[] Encrypt(byte[] data)
            {
                lock (lockObj)
                {
                    using (RsaInner rsa = new RsaInner(this.data!, this.cert!))
                    {
                        return rsa.Encrypt(data);
                    }
                }
            }

            public byte[] Decrypt(byte[] data)
            {
                lock (lockObj)
                {
                    using (RsaInner rsa = new RsaInner(this.data!, this.cert!))
                    {
                        return rsa.Decrypt(data);
                    }
                }
            }
        }

        // Rsa アルゴリズム (内部)
        public sealed class RsaInner : IDisposable
        {
            AsymmetricKeyParameter key = null!;

            public RsaInner(byte[] data, Cert cert)
            {
                if (data != null)
                {
                    init(data);
                }
                else
                {
                    init(cert);
                }
            }
            public RsaInner(byte[] data)
            {
                init(data);
            }
            public RsaInner(string filename)
            {
                Buf b = Buf.ReadFromFile(filename);
                init(b.ByteData);
            }
            public RsaInner(Buf b)
            {
                init(b.ByteData);
            }
            void init(byte[] data)
            {
                PemReader pem = new PemReader(new StringReader(data._GetString_Ascii()));
                object o = pem.ReadObject();
                if (o is AsymmetricCipherKeyPair)
                {
                    AsymmetricCipherKeyPair pair = (AsymmetricCipherKeyPair)o;

                    o = pair.Private;
                }
                key = (AsymmetricKeyParameter)o;
            }

            public RsaInner(Cert cert)
            {
                init(cert);
            }
            void init(Cert cert)
            {
                PemReader pem = new PemReader(new StringReader(cert.PublicKey._GetString_Ascii()));
                key = (AsymmetricKeyParameter)pem.ReadObject();
            }

            public byte[] SignData(byte[] data)
            {
                byte[] hash = Secure.HashSHA1(data);
                return SignHash(hash);
            }

            public byte[] SignHash(byte[] hash)
            {
                hash = hash_for_sign(hash);
                ISigner signer = SignerUtilities.GetSigner("RSA");
                signer.Init(true, key);
                signer.BlockUpdate(hash, 0, hash.Length);
                return signer.GenerateSignature();
            }

            byte[] hash_for_sign(byte[] data)
            {
                byte[] padding_data = {
                    0x30, 0x21, 0x30, 0x09, 0x06, 0x05, 0x2B, 0x0E,
                    0x03, 0x02, 0x1A, 0x05, 0x00, 0x04, 0x14,
            };

                return Util.CombineByteArray(padding_data, data);
            }

            public bool VerifyData(byte[] data, byte[] sign)
            {
                byte[] hash = Secure.HashSHA1(data);
                return VerifyHash(hash, sign);
            }

            public bool VerifyHash(byte[] hash, byte[] sign)
            {
                hash = hash_for_sign(hash);
                ISigner signer = SignerUtilities.GetSigner("RSA");
                signer.Init(false, key);
                signer.BlockUpdate(hash, 0, hash.Length);
                return signer.VerifySignature(sign);
            }

            public byte[] Encrypt(byte[] data)
            {
                IAsymmetricBlockCipher rsa = new Pkcs1Encoding(new RsaEngine());
                rsa.Init(true, key);
                return rsa.ProcessBlock(data, 0, data.Length);
            }

            public byte[] Decrypt(byte[] data)
            {
                IAsymmetricBlockCipher rsa = new Pkcs1Encoding(new RsaEngine());
                rsa.Init(false, key);
                return rsa.ProcessBlock(data, 0, data.Length);
            }

            public void Dispose()
            {
            }
        }

        // 証明書
        public class Cert
        {
            X509Certificate x509 = null!;
            static TimeSpan deleteOldCertSpan = new TimeSpan(0, 0, 30);
            static object lockObj = new Object();

            public X509Certificate X509Cert
            {
                get { return x509; }
            }

            public Rsa RsaPublicKey
            {
                get
                {
                    return new Rsa(this);
                }
            }

            public Cert(byte[] data)
            {
                init(data);
            }
            public Cert(string filename)
            {
                init(IO.ReadFile(filename));
            }
            public Cert(Buf buf)
            {
                init(buf.ByteData);
            }
            void init(byte[] data)
            {
                PemReader cert_pem = new PemReader(new StringReader(data._GetString_Ascii()));
                x509 = (X509Certificate)cert_pem.ReadObject();
            }

            public byte[] Hash
            {
                get
                {
                    return Secure.HashSHA1(x509.GetEncoded());
                }
            }

            public byte[] PublicKey
            {
                get
                {
                    StringWriter w = new StringWriter();
                    PemWriter pw = new PemWriter(w);
                    pw.WriteObject(x509.GetPublicKey());
                    return w.ToString()._GetBytes_Ascii();
                }
            }

            public byte[] ByteData
            {
                get
                {
                    StringWriter w = new StringWriter();
                    PemWriter pw = new PemWriter(w);
                    pw.WriteObject(x509);
                    return w.ToString()._GetBytes_Ascii();
                }
            }
            public Buf ToBuf()
            {
                return new Buf(ByteData);
            }
            public void ToFile(string filename)
            {
                ToBuf().WriteToFile(filename);
            }

            public Cert Clone()
            {
                return new Cert(this.ByteData);
            }
        }
    }

}

#endif // CORES_BASIC_SECURITY

