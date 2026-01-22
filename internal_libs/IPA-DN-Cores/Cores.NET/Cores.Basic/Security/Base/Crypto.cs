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

namespace IPA.Cores.Basic;

public static class ChaChaPoly
{
    public const int AeadChaCha20Poly1305MacSize = 16;
    public const int AeadChaCha20Poly1305NonceSize = 12;
    public const int AeadChaCha20Poly1305KeySize = 32;
    public const int PasswordIterations = 1234;

    static readonly byte[] zero15 = new byte[15];

    static bool crypto_aead_chacha20poly1305_ietf_decrypt_detached(Memory<byte> m, ReadOnlyMemory<byte> c, ReadOnlyMemory<byte> mac, ReadOnlyMemory<byte> ad, ReadOnlyMemory<byte> npub, ReadOnlyMemory<byte> k)
    {
        var kk = k._AsSegment();
        var nn = npub._AsSegment();
        var cc = c._AsSegment();
        var aa = ad._AsSegment();
        var mm = m._AsSegment();

        byte[] block0 = new byte[64];

        ChaCha7539Engine ctx = new ChaCha7539Engine();
        ctx.Init(true, new ParametersWithIV(new KeyParameter(kk.Array, kk.Offset, kk.Count), nn.Array, nn.Offset, nn.Count));
        ctx.ProcessBytes(block0, 0, block0.Length, block0, 0);

        Poly1305 state = new Poly1305();
        state.Init(new KeyParameter(block0, 0, AeadChaCha20Poly1305KeySize));

        state.BlockUpdate(aa.Array, aa.Offset, aa.Count);
        if ((aa.Count % 16) != 0)
            state.BlockUpdate(zero15, 0, 16 - (aa.Count % 16));

        state.BlockUpdate(cc.Array, cc.Offset, cc.Count);
        if ((cc.Count % 16) != 0)
            state.BlockUpdate(zero15, 0, 16 - (cc.Count % 16));

        byte[] slen = BitConverter.GetBytes((ulong)aa.Count);
        if (Env.IsBigEndian) Array.Reverse(slen);
        state.BlockUpdate(slen, 0, slen.Length);

        byte[] mlen = BitConverter.GetBytes((ulong)cc.Count);
        if (Env.IsBigEndian) Array.Reverse(mlen);
        state.BlockUpdate(mlen, 0, mlen.Length);

        byte[] computed_mac = new byte[AeadChaCha20Poly1305MacSize];
        state.DoFinal(computed_mac, 0);

        if (computed_mac.AsSpan().SequenceEqual(mac.Span) == false)
        {
            return false;
        }

        ctx.ProcessBytes(cc.Array, cc.Offset, cc.Count, mm.Array, mm.Offset);

        return true;
    }

    static void crypto_aead_chacha20poly1305_ietf_encrypt_detached(Memory<byte> c, ReadOnlyMemory<byte> mac, ReadOnlyMemory<byte> m, ReadOnlyMemory<byte> ad, ReadOnlyMemory<byte> npub, ReadOnlyMemory<byte> k)
    {
        var kk = k._AsSegment();
        var nn = npub._AsSegment();
        var cc = c._AsSegment();
        var aa = ad._AsSegment();
        var mm = m._AsSegment();

        byte[] block0 = new byte[64];

        ChaCha7539Engine ctx = new ChaCha7539Engine();
        ctx.Init(true, new ParametersWithIV(new KeyParameter(kk.Array, kk.Offset, kk.Count), nn.Array, nn.Offset, nn.Count));
        ctx.ProcessBytes(block0, 0, block0.Length, block0, 0);

        Poly1305 state = new Poly1305();
        state.Init(new KeyParameter(block0, 0, AeadChaCha20Poly1305KeySize));

        state.BlockUpdate(aa.Array, aa.Offset, aa.Count);
        if ((aa.Count % 16) != 0)
            state.BlockUpdate(zero15, 0, 16 - (aa.Count % 16));

        ctx.ProcessBytes(mm.Array, mm.Offset, mm.Count, cc.Array, cc.Offset);

        state.BlockUpdate(cc.Array, cc.Offset, cc.Count);
        if ((cc.Count % 16) != 0)
            state.BlockUpdate(zero15, 0, 16 - (cc.Count % 16));

        byte[] slen = BitConverter.GetBytes((ulong)aa.Count);
        if (Env.IsBigEndian) Array.Reverse(slen);
        state.BlockUpdate(slen, 0, slen.Length);

        byte[] mlen = BitConverter.GetBytes((ulong)mm.Count);
        if (Env.IsBigEndian) Array.Reverse(mlen);
        state.BlockUpdate(mlen, 0, mlen.Length);

        var macmac = mac._AsSegment();
        state.DoFinal(macmac.Array, macmac.Offset);
    }

    static void crypto_aead_chacha20poly1305_ietf_encrypt(Memory<byte> c, ReadOnlyMemory<byte> m, ReadOnlyMemory<byte> ad, ReadOnlyMemory<byte> npub, ReadOnlyMemory<byte> k)
    {
        crypto_aead_chacha20poly1305_ietf_encrypt_detached(c._SliceHead(m.Length),
            c.Slice(m.Length, AeadChaCha20Poly1305MacSize),
            m, ad, npub, k);
    }

    static bool crypto_aead_chacha20poly1305_ietf_decrypt(Memory<byte> m, ReadOnlyMemory<byte> c, ReadOnlyMemory<byte> ad, ReadOnlyMemory<byte> npub, ReadOnlyMemory<byte> k)
    {
        return crypto_aead_chacha20poly1305_ietf_decrypt_detached(m, c.Slice(0, c.Length - AeadChaCha20Poly1305MacSize),
            c.Slice(c.Length - AeadChaCha20Poly1305MacSize, AeadChaCha20Poly1305MacSize),
            ad, npub, k);
    }

    public static void Aead_ChaCha20Poly1305_Ietf_Encrypt(Memory<byte> dest, ReadOnlyMemory<byte> src, ReadOnlyMemory<byte> key, ReadOnlyMemory<byte> nonce, ReadOnlyMemory<byte> aad)
    {
        crypto_aead_chacha20poly1305_ietf_encrypt(dest, src, aad, nonce, key);
    }

    public static bool Aead_ChaCha20Poly1305_Ietf_Decrypt(Memory<byte> dest, ReadOnlyMemory<byte> src, ReadOnlyMemory<byte> key, ReadOnlyMemory<byte> nonce, ReadOnlyMemory<byte> aad)
    {
        return crypto_aead_chacha20poly1305_ietf_decrypt(dest, src, aad, nonce, key);
    }

    public static Memory<byte> PasswordToKey(string password)
    {
        byte[] pw = password._NonNull()._GetBytes_UTF8();

        Span<byte> hashSrc = new byte[32 + pw.Length + 4];
        Span<byte> hashDst = new byte[32];

        Secure.HashSHA256(pw, hashDst);

        for (int i = 0; i < PasswordIterations; i++)
        {
            hashDst.CopyTo(hashSrc);

            pw.CopyTo(hashSrc.Slice(32, pw.Length));

            hashSrc.Slice(32 + pw.Length)._SetSInt32(i);

            Secure.HashSHA256(hashSrc, hashDst);
        }

        return hashDst._CloneMemory();
    }

    public static Memory<byte> EasyEncryptWithPassword(ReadOnlyMemory<byte> src, string password, ReadOnlyMemory<byte> nonce12bytes = default, ReadOnlyMemory<byte> aadAnyBytes = default)
        => EasyEncrypt(src, PasswordToKey(password), nonce12bytes, aadAnyBytes);

    public static ResultOrExeption<Memory<byte>> EasyDecryptWithPassword(ReadOnlyMemory<byte> easyEncrypted, string password, ReadOnlyMemory<byte> aadAnyBytes = default)
        => EasyDecrypt(easyEncrypted, PasswordToKey(password), aadAnyBytes);

    public static Memory<byte> EasyEncrypt(ReadOnlyMemory<byte> src, ReadOnlyMemory<byte> key32bytes, ReadOnlyMemory<byte> nonce12bytes = default, ReadOnlyMemory<byte> aadAnyBytes = default)
    {
        if (key32bytes.Length != AeadChaCha20Poly1305KeySize) throw new ArgumentOutOfRangeException(nameof(key32bytes));

        if (nonce12bytes.IsEmpty) nonce12bytes = Secure.RandWithInchikiEntropySlow(AeadChaCha20Poly1305NonceSize);
        if (nonce12bytes.Length != AeadChaCha20Poly1305NonceSize) throw new ArgumentOutOfRangeException(nameof(nonce12bytes));

        Memory<byte> dest = new byte[src.Length + AeadChaCha20Poly1305MacSize + AeadChaCha20Poly1305NonceSize];

        Aead_ChaCha20Poly1305_Ietf_Encrypt(dest, src, key32bytes, nonce12bytes, aadAnyBytes);

        nonce12bytes.CopyTo(dest._SliceTail(AeadChaCha20Poly1305NonceSize));

        return dest;
    }

    public static ResultOrExeption<Memory<byte>> EasyDecrypt(ReadOnlyMemory<byte> easyEncrypted, ReadOnlyMemory<byte> key32bytes, ReadOnlyMemory<byte> aadAnyBytes = default)
    {
        if (key32bytes.Length != AeadChaCha20Poly1305KeySize) throw new ArgumentOutOfRangeException(nameof(key32bytes));

        if (easyEncrypted.Length < (AeadChaCha20Poly1305MacSize + AeadChaCha20Poly1305NonceSize)) throw new CoresException("easyEncrypted.Length < (AeadChaCha20Poly1305MacSize + AeadChaCha20Poly1305NonceSize)");

        Memory<byte> dest = new byte[easyEncrypted.Length - AeadChaCha20Poly1305MacSize - AeadChaCha20Poly1305NonceSize];

        var src = easyEncrypted._SliceHead(easyEncrypted.Length - AeadChaCha20Poly1305NonceSize);
        var nonce = easyEncrypted._SliceTail(AeadChaCha20Poly1305NonceSize);

        if (Aead_ChaCha20Poly1305_Ietf_Decrypt(dest, src, key32bytes, nonce, aadAnyBytes) == false)
        {
            return new ResultOrExeption<Memory<byte>>(new CoresException("Different key or authentication data"));
        }

        return new ResultOrExeption<Memory<byte>>(dest);
    }

    public static void Aead_ChaCha20Poly1305_Ietf_Test()
    {
        string nonce_hex = "07 00 00 00 40 41 42 43 44 45 46 47";
        string plaintext_hex =
            "4c 61 64 69 65 73 20 61 6e 64 20 47 65 6e 74 6c " +
            "65 6d 65 6e 20 6f 66 20 74 68 65 20 63 6c 61 73 " +
            "73 20 6f 66 20 27 39 39 3a 20 49 66 20 49 20 63 " +
            "6f 75 6c 64 20 6f 66 66 65 72 20 79 6f 75 20 6f " +
            "6e 6c 79 20 6f 6e 65 20 74 69 70 20 66 6f 72 20 " +
            "74 68 65 20 66 75 74 75 72 65 2c 20 73 75 6e 73 " +
            "63 72 65 65 6e 20 77 6f 75 6c 64 20 62 65 20 69 " +
            "74 2e";
        string aad_hex = "50 51 52 53 c0 c1 c2 c3 c4 c5 c6 c7";
        string key_hex = "80 81 82 83 84 85 86 87 88 89 8a 8b 8c 8d 8e 8f " +
            "90 91 92 93 94 95 96 97 98 99 9a 9b 9c 9d 9e 9f";

        string rfc_mac = "1a:e1:0b:59:4f:09:e2:6a:7e:90:2e:cb:d0:60:06:91".Replace(':', ' ');
        string rfc_enc = "d3 1a 8d 34 64 8e 60 db 7b 86 af bc 53 ef 7e c2 " +
            "a4 ad ed 51 29 6e 08 fe a9 e2 b5 a7 36 ee 62 d6 " +
            "3d be a4 5e 8c a9 67 12 82 fa fb 69 da 92 72 8b " +
            "1a 71 de 0a 9e 06 0b 29 05 d6 a5 b6 7e cd 3b 36 " +
            "92 dd bd 7f 2d 77 8b 8c 98 03 ae e3 28 09 1b 58 " +
            "fa b3 24 e4 fa d6 75 94 55 85 80 8b 48 31 d7 bc " +
            "3f f4 de f0 8e 4b 7a 9d e5 76 d2 65 86 ce c6 4b " +
            "61 16";

        var nonce = nonce_hex._GetHexBytes().AsMemory();
        var plaintext = plaintext_hex._GetHexBytes().AsMemory();
        var aad = aad_hex._GetHexBytes().AsMemory();
        var key = key_hex._GetHexBytes().AsMemory();
        var encrypted = new byte[plaintext.Length + AeadChaCha20Poly1305MacSize].AsMemory();
        var decrypted = new byte[plaintext.Length].AsMemory();

        Con.WriteLine("Aead_ChaCha20Poly1305_Ietf_Test()");

        Aead_ChaCha20Poly1305_Ietf_Encrypt(encrypted, plaintext, key, nonce, aad);

        string encrypted_hex = encrypted.Slice(0, plaintext.Length).ToArray()._GetHexString(" ");
        string mac_hex = encrypted.Slice(plaintext.Length, AeadChaCha20Poly1305MacSize).ToArray()._GetHexString(" ");

        Con.WriteLine($"Encrypted:\n{encrypted_hex}\n");

        Con.WriteLine($"MAC:\n{mac_hex}\n");

        var a = rfc_enc._GetHexBytes();
        if (encrypted.Slice(0, plaintext.Length).Span.SequenceEqual(a) == false)
        {
            throw new ApplicationException("encrypted != rfc_enc");
        }

        Con.WriteLine("Check OK.");

        if (Aead_ChaCha20Poly1305_Ietf_Decrypt(decrypted, encrypted, key, nonce, aad) == false)
        {
            throw new ApplicationException("Decrypt failed.");
        }
        else
        {
            Con.WriteLine("Decrypt OK.");

            if (plaintext.Span.SequenceEqual(decrypted.Span))
            {
                Con.WriteLine("Same OK.");
            }
            else
            {
                throw new ApplicationException("Different !!!");
            }
        }
    }
}

#endif // CORES_BASIC_SECURITY

