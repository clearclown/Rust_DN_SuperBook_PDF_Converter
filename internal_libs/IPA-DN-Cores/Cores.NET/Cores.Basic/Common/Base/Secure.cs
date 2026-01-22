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
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Runtime.InteropServices;

using IPA.Cores.Basic;
using IPA.Cores.Basic.Legacy;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Buffers;
using System.Net.Security;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace IPA.Cores.Basic
{
    // Secure クラス
    public class Secure
    {
        static readonly MD5 MD5Shared = MD5.Create();
        public const int SHA1Size = 20;
        public const int SHA256Size = 32;
        public const int SHA384Size = 48;
        public const int SHA512Size = 64;
        public const int MD5Size = 16;
        readonly static CriticalSection RandLock = new CriticalSection<Secure>();
        readonly static CriticalSection MD5Lock = new CriticalSection<Secure>();

        public static byte[] Rand(int size) { byte[] r = new byte[size]; Rand(r); return r; }

        public static void Rand(Span<byte> dest)
        {
            RandomNumberGenerator.Fill(dest);
        }

        // C# の RandomNumberGenerator クラスにバグがあり乱数強度が弱い場合に備えて、追加のインチキ・エントロピーを XOR で供給する。
        // (RandomNumberGenerator に万一欠陥があった場合のインチキ救済策)
        // インチキ・エントロピーの乱数強度は低いが、一応強度の高いと期待される RandomNumberGenerator の乱数にインチキを XOR しているだけなので、
        // 少なくとも、RandomNumberGenerator と比較して強度が低下する可能性は、暗号学的に、ないはずである。
        public static byte[] RandWithInchikiEntropySlow(int size) { byte[] r = new byte[size]; RandWithInchikiEntropySlow(r); return r; }
        public static void RandWithInchikiEntropySlow(Span<byte> dest)
        {
            dest.Fill(0);

            // 1. RandomNumberGenerator の結果
            Rand(dest);

            // 2. Util の Rand 関数を XOR で合成 (ああ、いんちきだなあ)
            Span<byte> inchiki = new byte[dest.Length];
            Util.Rand(inchiki);
            dest._Xor(dest, inchiki);

            // 3. インチキ・エントロピーのいくつかを XOR で合成 (ああ、いんちきだなあ)
            dest._Xor(dest, (new SeedBasedRandomGenerator(Secure.GenerateInchikiEntropy(), SHA512.Create())).GetBytes(dest.Length).Span);
            dest._Xor(dest, (new SeedBasedRandomGenerator(Secure.GenerateInchikiEntropy(), SHA256.Create())).GetBytes(dest.Length).Span);
            dest._Xor(dest, (new SeedBasedRandomGenerator(Secure.GenerateInchikiEntropy(), SHA384.Create())).GetBytes(dest.Length).Span);
            dest._Xor(dest, (new SeedBasedRandomGenerator(Secure.GenerateInchikiEntropy(), SHA1.Create())).GetBytes(dest.Length).Span);
        }

        [SkipLocalsInit]
        public static byte RandUInt8()
        {
            Span<byte> mem = stackalloc byte[1];
            Rand(mem);
            return mem._GetUInt8();
        }

        [SkipLocalsInit]
        public static ushort RandUInt16()
        {
            Span<byte> mem = stackalloc byte[2];
            Rand(mem);
            return mem._GetUInt16();
        }

        [SkipLocalsInit]
        public static uint RandUInt32()
        {
            Span<byte> mem = stackalloc byte[4];
            Rand(mem);
            return mem._GetUInt32();
        }

        [SkipLocalsInit]
        public static ulong RandUInt64()
        {
            Span<byte> mem = stackalloc byte[8];
            Rand(mem);
            return mem._GetUInt64();
        }

        [SkipLocalsInit]
        public static byte RandUInt7()
        {
            Span<byte> mem = stackalloc byte[1];
            Rand(mem);
            mem[0] &= 0x7F;
            return mem._GetUInt8();
        }

        [SkipLocalsInit]
        public static ushort RandUInt15()
        {
            Span<byte> mem = stackalloc byte[2];
            Rand(mem);
            mem[0] &= 0x7F;
            return mem._GetUInt16();
        }

        [SkipLocalsInit]
        public static uint RandUInt31()
        {
            Span<byte> mem = stackalloc byte[4];
            Rand(mem);
            mem[0] &= 0x7F;
            return mem._GetUInt32();
        }

        [SkipLocalsInit]
        public static ulong RandUInt63()
        {
            Span<byte> mem = stackalloc byte[8];
            Rand(mem);
            mem[0] &= 0x7F;
            return mem._GetUInt64();
        }

        [SkipLocalsInit]
        public static sbyte RandSInt8_Caution()
        {
            Span<byte> mem = stackalloc byte[1];
            Rand(mem);
            return mem._GetSInt8();
        }

        [SkipLocalsInit]
        public static short RandSInt16_Caution()
        {
            Span<byte> mem = stackalloc byte[2];
            Rand(mem);
            return mem._GetSInt16();
        }

        [SkipLocalsInit]
        public static int RandSInt32_Caution()
        {
            Span<byte> mem = stackalloc byte[4];
            Rand(mem);
            return mem._GetSInt32();
        }

        [SkipLocalsInit]
        public static long RandSInt64_Caution()
        {
            Span<byte> mem = stackalloc byte[8];
            Rand(mem);
            return mem._GetSInt64();
        }

        [SkipLocalsInit]
        public static sbyte RandSInt7()
        {
            Span<byte> mem = stackalloc byte[1];
            Rand(mem);
            mem[0] &= 0x7F;
            return mem._GetSInt8();
        }

        [SkipLocalsInit]
        public static short RandSInt15()
        {
            Span<byte> mem = stackalloc byte[2];
            Rand(mem);
            mem[0] &= 0x7F;
            return mem._GetSInt16();
        }

        [SkipLocalsInit]
        public static int RandSInt31()
        {
            Span<byte> mem = stackalloc byte[4];
            Rand(mem);
            mem[0] &= 0x7F;
            return mem._GetSInt32();
        }

        [SkipLocalsInit]
        public static long RandSInt63()
        {
            Span<byte> mem = stackalloc byte[8];
            Rand(mem);
            mem[0] &= 0x7F;
            return mem._GetSInt64();
        }

        public static bool RandBool()
        {
            return (RandUInt32() % 2) == 0;
        }

        // MD5
        public static byte[] HashMD5(ReadOnlySpan<byte> src)
        {
            Span<byte> dest = new byte[MD5Size];
            int r = HashMD5(src, dest);
            Debug.Assert(r == dest.Length);
            return dest.ToArray();
        }

        public static int HashMD5(ReadOnlySpan<byte> src, Span<byte> dest)
        {
            lock (MD5Lock)
            {
                if (MD5Shared.TryComputeHash(src, dest, out int ret))
                    return ret;
            }

            throw new ApplicationException("TryComputeHash error.");
        }

        // SHA1
        public static byte[] HashSHA1(ReadOnlySpan<byte> src)
        {
            Span<byte> dest = new byte[SHA1Size];
            int r = HashSHA1(src, dest);
            Debug.Assert(r == dest.Length);
            return dest.ToArray();
        }

        public static int HashSHA1(ReadOnlySpan<byte> src, Span<byte> dest)
        {
            using SHA1 sha = SHA1.Create();

            if (sha.TryComputeHash(src, dest, out int ret) == false)
                throw new ApplicationException("TryComputeHash error.");

            return ret;
        }

        public static byte[] HMacSHA1(byte[] key, ReadOnlySpan<byte> data)
        {
            Span<byte> dest = new byte[SHA1Size];
            int r = HMacSHA1(key, data, dest);
            Debug.Assert(r == dest.Length);
            return dest.ToArray();
        }

        public static int HMacSHA1(byte[] key, ReadOnlySpan<byte> data, Span<byte> dest)
        {
            using HMACSHA1 h = new HMACSHA1(key);

            if (h.TryComputeHash(data, dest, out int ret) == false)
                throw new CoresLibException("TryComputeHash error.");

            return ret;
        }


        // SHA0
        public static byte[] HashSHA0(ReadOnlySpan<byte> src)
        {
            return FromC.Internal_SHA0(src);
        }

        public static int HashSHA0(ReadOnlySpan<byte> src, Span<byte> dest)
        {
            byte[] tmp = HashSHA0(src);

            tmp.CopyTo(dest);

            return tmp.Length;
        }

        // SHA256
        public static byte[] HashSHA256(ReadOnlySpan<byte> src)
        {
            Span<byte> dest = new byte[SHA256Size];
            int r = HashSHA256(src, dest);
            Debug.Assert(r == dest.Length);
            return dest.ToArray();
        }

        public static int HashSHA256(ReadOnlySpan<byte> src, Span<byte> dest)
        {
            using SHA256 sha = SHA256.Create();

            if (sha.TryComputeHash(src, dest, out int ret) == false)
                throw new ApplicationException("TryComputeHash error.");

            return ret;
        }

        // SHA384
        public static byte[] HashSHA384(ReadOnlySpan<byte> src)
        {
            Span<byte> dest = new byte[SHA384Size];
            int r = HashSHA384(src, dest);
            Debug.Assert(r == dest.Length);
            return dest.ToArray();
        }

        public static int HashSHA384(ReadOnlySpan<byte> src, Span<byte> dest)
        {
            using SHA384 sha = SHA384.Create();

            if (sha.TryComputeHash(src, dest, out int ret) == false)
                throw new ApplicationException("TryComputeHash error.");

            return ret;
        }

        // SHA512
        public static byte[] HashSHA512(ReadOnlySpan<byte> src)
        {
            Span<byte> dest = new byte[SHA512Size];
            int r = HashSHA512(src, dest);
            Debug.Assert(r == dest.Length);
            return dest.ToArray();
        }

        public static int HashSHA512(ReadOnlySpan<byte> src, Span<byte> dest)
        {
            using SHA512 sha = SHA512.Create();

            if (sha.TryComputeHash(src, dest, out int ret) == false)
                throw new ApplicationException("TryComputeHash error.");

            return ret;
        }

        public static long HashSHA1AsLong(ReadOnlySpan<byte> src)
        {
            byte[] hash = Secure.HashSHA1(src);
            long ret = hash._GetSInt64();
            if (ret == 0) ret = 1;
            return ret;
        }

        public static long HashSHA1AsSInt63(ReadOnlySpan<byte> src)
        {
            byte[] hash = Secure.HashSHA1(src);
            long ret = hash._GetSInt64() & 0x7fffffffffffffffL;
            if (ret == 0) ret = 1;
            return ret;
        }

        public static int HashSHA1AsSInt31(ReadOnlySpan<byte> src)
        {
            byte[] hash = Secure.HashSHA1(src);
            int ret = (int)(hash._GetSInt64() & 0x7fffffffL);
            if (ret == 0) ret = 1;
            return ret;
        }

        public static string JavaScriptEasyStrEncrypt(string? srcString, string? password)
        {
            srcString = srcString._NonNull();
            password = password._NonNull();

            using var aes = Aes.Create();
            aes.Mode = CipherMode.CBC;
            aes.KeySize = 256;
            aes.BlockSize = 128;
            aes.Padding = PaddingMode.PKCS7;
            aes.IV = Secure.HashSHA256(("1" + password)._GetBytes_UTF8()).AsSpan(0, 128 / 8).ToArray();
            aes.Key = Secure.HashSHA256(("2" + password)._GetBytes_UTF8()).AsSpan(0, 256 / 8).ToArray();

            using var mem = new MemoryStream();
            using var enc = aes.CreateEncryptor();
            using var stream = new CryptoStream(mem, enc, CryptoStreamMode.Write);

            stream.Write(srcString._GetBytes_UTF8());
            stream.FlushFinalBlock();

            return mem.ToArray()._GetHexString()._JavaScriptSafeStrEncode();
        }

        public static Memory<byte> EasyEncrypt(ReadOnlyMemory<byte> src, string? password = null)
        {
            if (password._IsNullOrZeroLen()) password = Consts.Strings.EasyEncryptDefaultPassword;
            return ChaChaPoly.EasyEncryptWithPassword(src, password);
        }

        public static Memory<byte> EasyDecrypt(ReadOnlyMemory<byte> src, string? password = null)
        {
            if (password._IsNullOrZeroLen()) password = Consts.Strings.EasyEncryptDefaultPassword;
            return ChaChaPoly.EasyDecryptWithPassword(src, password);
        }

        public static async Task<byte[]> CalcStreamHashAsync(Stream stream, HashAlgorithm hash, long truncateSize = long.MaxValue,
            int bufferSize = Consts.Numbers.DefaultLargeBufferSize, RefLong? totalReadSize = null, CancellationToken cancel = default,
            ProgressReporterBase? progressReporter = null, string? progressReporterAdditionalInfo = null, long progressReporterCurrentSizeOffset = 0, long? progressReporterTotalSizeHint = null,
            bool progressReporterFinalize = true,
            ThroughputMeasuse? measure = null)
        {
            checked
            {
                progressReporterAdditionalInfo = progressReporterAdditionalInfo._NonNull();

                hash.Initialize();

                byte[] buffer = ArrayPool<byte>.Shared.Rent(bufferSize);

                long currentSize = 0;

                while (true)
                {
                    cancel.ThrowIfCancellationRequested();

                    int tryReadSize = buffer.Length;

                    if ((currentSize + tryReadSize) >= truncateSize)
                    {
                        tryReadSize = (int)(truncateSize - currentSize);
                    }

                    int readSize = await stream.ReadAsync(buffer, 0, tryReadSize);
                    if (readSize == 0)
                    {
                        break;
                    }

                    Debug.Assert(readSize <= tryReadSize);

                    hash.TransformBlock(buffer, 0, readSize, null, 0);
                    currentSize += readSize;

                    totalReadSize?.Set(currentSize);

                    if (progressReporter != null)
                    {
                        progressReporter.ReportProgress(new ProgressData(currentSize + progressReporterCurrentSizeOffset, progressReporterTotalSizeHint, false, progressReporterAdditionalInfo));
                    }

                    if (measure != null)
                    {
                        measure.Add(readSize);
                    }

                    Debug.Assert(currentSize <= truncateSize);

                    if (currentSize == truncateSize)
                    {
                        break;
                    }
                }

                hash.TransformFinalBlock(buffer, 0, 0);

                if (progressReporter != null)
                {
                    if (progressReporterFinalize)
                    {
                        progressReporter.ReportProgress(new ProgressData(currentSize, currentSize, true, progressReporterAdditionalInfo));
                    }
                    else
                    {
                        progressReporter.ReportProgress(new ProgressData(currentSize, progressReporterTotalSizeHint, false, progressReporterAdditionalInfo));
                    }
                }

                ArrayPool<byte>.Shared.Return(buffer);

                totalReadSize?.Set(currentSize);

                return hash.Hash!;
            }
        }

        // PKCS パディング
        public static byte[] PkcsPadding(byte[] srcData, int destSize)
        {
            int srcSize = srcData.Length;

            if ((srcSize + 11) > destSize)
            {
                throw new OverflowException();
            }

            int randSize = destSize - srcSize - 3;
            byte[] rand = Secure.Rand(randSize);

            Buf b = new Buf();
            b.WriteByte(0x00);
            b.WriteByte(0x02);
            b.Write(rand);
            b.WriteByte(0x00);
            b.Write(srcData);

            return b.ByteData;
        }

        // パスワードハッシュの生成
        public const int PasswordSaltSize = 16;
        public const int PasswordKeySize = 32;
        public const int PasswordIterations = 1234;
        public const string SaltPasswordPrefixV0 = "__pw_salted________v0_";
        public const string SaltPasswordPrefixCommon = "__pw_salted________";
        public static string SaltPassword(string password, byte[]? salt = null)
        {
            if (salt == null)
            {
                salt = Secure.RandWithInchikiEntropySlow(PasswordSaltSize);
            }

            byte[] pw = password._NonNull()._GetBytes_UTF8();
            byte[] src = pw;

            for (int i = 0; i < PasswordIterations; i++)
            {
                src = Secure.HashSHA256(src._CombineByte(salt));
            }

            return SaltPasswordPrefixV0 + src._CombineByte(salt)._GetHexString();
        }

        // パスワードハッシュの検証
        public static bool VeritySaltedPassword(string saltedPassword, string password)
        {
            if (password.StartsWith(SaltPasswordPrefixCommon, StringComparison.Ordinal))
            {
                return false;
            }

            if (saltedPassword.StartsWith(SaltPasswordPrefixV0, StringComparison.Ordinal) == false)
            {
                return saltedPassword._IsSame(password);
            }

            saltedPassword = saltedPassword.Substring(SaltPasswordPrefixV0.Length);

            byte[] data = saltedPassword._GetHexBytes();
            if (data.Length != (PasswordSaltSize + PasswordKeySize))
            {
                throw new ApplicationException("data.Length != (PasswordSaltSize + PasswordKeySize)");
            }

            byte[] pw = data._ExtractByte(0, PasswordKeySize);
            byte[] salt = data._ExtractByte(PasswordKeySize, PasswordSaltSize);

            string hash2 = SaltPassword(password, salt);

            hash2 = hash2.Substring(SaltPasswordPrefixV0.Length);

            return saltedPassword._GetHexBytes()._MemEquals(hash2._GetHexBytes());
        }

        // PKCS 証明書の読み込み
        public static X509Certificate2 LoadPkcs12(byte[] data, string? password = null, bool forWindowsCertStoreAdd = false)
        {
            password = password._NonNull();

            var flag = X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.Exportable;

            if (forWindowsCertStoreAdd)
            {
                flag |= X509KeyStorageFlags.PersistKeySet;
            }

            return new X509Certificate2(data, password, flag);
        }
        public static X509Certificate2 LoadPkcs12(string filename, string? password = null, FileSystem? fileSystem = null)
        {
            if (fileSystem == null) fileSystem = Lfs;

            return LoadPkcs12(fileSystem.ReadDataFromFile(filename).ToArray(), password);
        }
        public static X509Certificate2 LoadPkcs12(ResourceFileSystem resFs, string partOfFileName, string? password = null, bool exact = false)
        {
            return LoadPkcs12(resFs.EasyReadData(partOfFileName, exact: true).ToArray(), password);
        }

        public static CertSelectorCallback StaticServerCertSelector(X509Certificate2 cert) => (obj, sni) => cert;

        public static byte[] SoftEther_SecurePassword(ReadOnlyMemory<byte> passwordSha1Hash, ReadOnlyMemory<byte> random)
        {
            SpanBuffer<byte> b = new SpanBuffer<byte>();
            b.Write(passwordSha1Hash);
            b.Write(random);
            return Secure.HashSHA0(b);
        }

        // .NET 標準の SslStreamCertificateContext.Create() では、不必要な証明書は削除されてしまう。
        // この関数は、不必要な証明書もチェーンに入れた状態で SslStreamCertificateContext を作成するのである。
        // ただし、Windows では正しく動作しない。
        public static SslStreamCertificateContext CreateSslCreateCertificateContextWithFullChain(X509Certificate2 target, X509Certificate2Collection? additionalCertificates = null, bool offline = false, bool errorWhenFailed = false)
        {
            // まずオブジェクトを普通通りに作成する
            SslStreamCertificateContext original = SslStreamCertificateContext.Create(target, additionalCertificates, offline);

            if (additionalCertificates == null || additionalCertificates.Count == 0)
            {
                // 証明書チェーンが 0 個の場合はこれでよい
                return original;
            }

            List<X509Certificate2> chainList = new List<X509Certificate2>();

            foreach (var cert in additionalCertificates)
            {
                if (cert != null)
                {
                    chainList.Add(cert);
                }
            }

            X509Certificate2[] chainArray = chainList.ToArray();

            try
            {
                // このオブジェクトの internal の IntermediateCertificates フィールドを強制的に書き換える
                original._PrivateSet("IntermediateCertificates", chainArray);

                return original;
            }
            catch
            {
                if (errorWhenFailed) throw;

                // エラーが発生した場合 (.NET のライブラリのバージョンアップでフィールド名が変わった場合等) はオリジナルのものを作成して返す (failsafe)

                return SslStreamCertificateContext.Create(target, additionalCertificates, offline);
            }
        }

        // インチキ・エントロピーの生成
        static long inchikiSeed = Secure.RandSInt64_Caution() + Util.RandSInt64_Caution();
        public static unsafe string GenerateInchikiEntropy()
        {
            StringWriter w = new StringWriter();
            w.NewLine = Str.NewLine_Str_Unix;
            
            w.WriteLine(DtOffsetNow.Ticks.ToString());

            Span<byte> tmp32 = new byte[32];
            RandomNumberGenerator.Fill(tmp32);
            w.WriteLine(tmp32._GetHexString());

            var snap = new EnvInfoSnapshot();
            w.WriteLine(snap._ObjectToJson(compact: true));

            w.WriteLine(snap.TimeStamp.Ticks);
            w.WriteLine(snap.BootTime.Ticks);
            w.WriteLine(snap.BuildTimeStamp.Ticks);
            w.WriteLine(Time.NowHighResLong100Usecs);
            
            int x = 123;
            w.WriteLine((long)(&x));

            using (MemoryHelper.AllocUnmanagedMemoryWithUsing(3, out var ptr))
            {
                w.WriteLine(ptr.ToInt64());
            }

            TypedReference ptr2 = __makeref(w);
#pragma warning disable CS8500 // これは、マネージ型のアドレスの取得、サイズの取得、またはそのマネージ型へのポインターの宣言を行います
            IntPtr ptr3 = **(IntPtr**)(&ptr2);
#pragma warning restore CS8500 // これは、マネージ型のアドレスの取得、サイズの取得、またはそのマネージ型へのポインターの宣言を行います
            w.WriteLine(ptr3.ToInt64());
            
            w.WriteLine(LeakChecker.Count);

            Interlocked.Increment(ref inchikiSeed);
            w.WriteLine(inchikiSeed);

            w.WriteLine(Str.NewUid());

            w.WriteLine(Str.NewGuid());

            w.WriteLine(Str.GenRandStr());

            return w.ToString();
        }
    }

    public class HashCalcStream : StreamImplBase
    {
        public HashAlgorithm Algorithm { get; }
        public bool AutoDispose { get; }
        long _CurrentPosition = 0;

        public HashCalcStream(HashAlgorithm algorithm, bool autoDispose = true) : base(new StreamImplBaseOptions(false, true, false))
        {
            try
            {
                this.Algorithm = algorithm;
                this.AutoDispose = autoDispose;

                this.Algorithm.Initialize();
            }
            catch
            {
                this._DisposeSafe();
                throw;
            }
        }

        public override bool DataAvailable => throw new NotImplementedException();

        protected override Task FlushImplAsync(CancellationToken cancellationToken = default)
        {
            return TaskCompleted;
        }

        protected override long GetLengthImpl()
        {
            return this._CurrentPosition;
        }

        protected override long GetPositionImpl()
        {
            return this._CurrentPosition;
        }

        protected override ValueTask<int> ReadImplAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        protected override long SeekImpl(long offset, SeekOrigin origin)
        {
            if (origin == SeekOrigin.Begin && offset == this._CurrentPosition)
            {
                return this._CurrentPosition;
            }

            if (origin == SeekOrigin.Current && offset == 0)
            {
                return this._CurrentPosition;
            }

            throw new NotImplementedException();
        }

        protected override void SetLengthImpl(long length)
        {
            if (length == this._CurrentPosition)
            {
                return;
            }

            throw new NotImplementedException();
        }

        protected override void SetPositionImpl(long position)
        {
            if (position == this._CurrentPosition)
            {
                return;
            }

            throw new NotImplementedException();
        }

        protected override ValueTask WriteImplAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var seg = buffer._AsSegment();

            if (seg.Count >= 1)
            {
                seg.Array._NullCheck();

                this.Algorithm.TransformBlock(seg.Array, seg.Offset, seg.Count, null, 0);

                this._CurrentPosition += seg.Count;
            }

            return ValueTask.CompletedTask;
        }

        Once FinalFlag;
        byte[]? HashResult = null;
        Exception Error = new CoresException("Unknown error");

        public byte[] GetFinalHash()
        {
            if (FinalFlag.IsFirstCall())
            {
                try
                {
                    this.Algorithm.TransformFinalBlock(new byte[0], 0, 0);

                    this.HashResult = this.Algorithm.Hash;
                }
                catch (Exception ex)
                {
                    this.Error = ex;
                    throw;
                }
            }

            if (this.HashResult == null)
            {
                throw this.Error;
            }
            else
            {
                return this.HashResult;
            }
        }

        Once DisposeFlag;
        public override async ValueTask DisposeAsync()
        {
            try
            {
                if (DisposeFlag.IsFirstCall() == false) return;
                await DisposeInternalAsync();
            }
            finally
            {
                await base.DisposeAsync();
            }
        }
        protected override void Dispose(bool disposing)
        {
            try
            {
                if (!disposing || DisposeFlag.IsFirstCall() == false) return;
                DisposeInternalAsync()._GetResult();
            }
            finally { base.Dispose(disposing); }
        }
        Task DisposeInternalAsync()
        {
            if (this.AutoDispose)
            {
                this.Algorithm._DisposeSafe();
            }
            return Task.CompletedTask;
        }
    }


    public class HashCalc
    {
        public HashAlgorithm Algorithm { get; }

        public HashCalc(HashAlgorithm algorithm)
        {
            try
            {
                this.Algorithm = algorithm;

                this.Algorithm.Initialize();
            }
            catch
            {
                throw;
            }
        }

        public void Write(ReadOnlyMemory<byte> buffer)
        {
            var seg = buffer._AsSegment();

            if (seg.Count >= 1)
            {
                seg.Array._NullCheck();

                this.Algorithm.TransformBlock(seg.Array, seg.Offset, seg.Count, null, 0);
            }
        }

        Once FinalFlag;
        byte[]? HashResult = null;
        Exception Error = new CoresException("Unknown error");

        public byte[] GetFinalHash()
        {
            if (FinalFlag.IsFirstCall())
            {
                try
                {
                    this.Algorithm.TransformFinalBlock(new byte[0], 0, 0);

                    this.HashResult = this.Algorithm.Hash;
                }
                catch (Exception ex)
                {
                    this.Error = ex;
                    throw;
                }
            }

            if (this.HashResult == null)
            {
                throw this.Error;
            }
            else
            {
                return this.HashResult;
            }
        }
    }

    public class SeedBasedRandomGenerator
    {
        HashAlgorithm HashAlgo;
        MemoryBuffer<byte> seedPlusSeqNo;

        FastStreamBuffer<byte> fifo = new FastStreamBuffer<byte>();

        // hashAlgorithm が null の場合、標準的に、SHA1 を使用することになる。
        // それ以外のハッシュアルゴリズムを指定した場合、標準と異なる結果が出るので、注意すること。

        public SeedBasedRandomGenerator(string seed, HashAlgorithm? hashAlgorithm = null) : this(seed._GetBytes_UTF8(), hashAlgorithm) { }

        public SeedBasedRandomGenerator(ReadOnlySpan<byte> seed, HashAlgorithm? hashAlgorithm = null)
        {
            hashAlgorithm ??= SHA1.Create();

            this.HashAlgo = hashAlgorithm;

            seedPlusSeqNo = new MemoryBuffer<byte>();
            seedPlusSeqNo.WriteSInt64(0);
            seedPlusSeqNo.Write(Secure.HashSHA256(seed));
        }

        long SeqNo = 0;

        ReadOnlyMemory<byte> GenerateNextBlockInternal()
        {
            SeqNo++;

            var srcSpan = seedPlusSeqNo.Span;
            srcSpan._RawWriteValueSInt64(SeqNo);

            Memory<byte> tmp = new byte[HashAlgo.HashSize / 8];

            if (HashAlgo.TryComputeHash(srcSpan, tmp.Span, out int num) == false || num != tmp.Length)
            {
                throw new CoresLibException("Invalid status!");
            }

            return tmp;
        }

        public ReadOnlyMemory<byte> GetBytes(int wantSize)
        {
            if (wantSize < 0) throw new ArgumentOutOfRangeException(nameof(wantSize));
            if (wantSize == 0) return new byte[0];

            MemoryBuffer<byte> ret = new MemoryBuffer<byte>();

            while (true)
            {
                if (fifo.Length >= wantSize)
                {
                    return fifo.DequeueContiguousSlow(wantSize);
                }

                var tmp = GenerateNextBlockInternal();

                fifo.Enqueue(tmp);
            }
        }

        public byte GetUInt8()
        {
            var mem = GetBytes(1);
            return mem._GetUInt8();
        }

        public ushort GetUInt16()
        {
            var mem = GetBytes(2);
            return mem._GetUInt16();
        }

        public uint GetUInt32()
        {
            var mem = GetBytes(4);
            return mem._GetUInt32();
        }

        public ulong GetUInt64()
        {
            var mem = GetBytes(8);
            return mem._GetUInt64();
        }

        public byte GetUInt7()
        {
            var mem = GetBytes(1)._CloneSpan();
            mem[0] &= 0x7F;
            return mem._GetUInt8();
        }

        public ushort GetUInt15()
        {
            var mem = GetBytes(2)._CloneSpan();
            mem[0] &= 0x7F;
            return mem._GetUInt16();
        }

        public uint GetUInt31()
        {
            var mem = GetBytes(4)._CloneSpan();
            mem[0] &= 0x7F;
            return mem._GetUInt32();
        }

        public ulong GetUInt63()
        {
            var mem = GetBytes(8)._CloneSpan();
            mem[0] &= 0x7F;
            return mem._GetUInt64();
        }

        public sbyte GetSInt8_Caution()
        {
            var mem = GetBytes(1);
            return mem._GetSInt8();
        }

        public short GetSInt16_Caution()
        {
            var mem = GetBytes(2);
            return mem._GetSInt16();
        }

        public int GetSInt32_Caution()
        {
            var mem = GetBytes(4);
            return mem._GetSInt32();
        }

        public long GetSInt64_Caution()
        {
            var mem = GetBytes(8);
            return mem._GetSInt64();
        }

        public sbyte GetSInt7()
        {
            var mem = GetBytes(1)._CloneSpan();
            mem[0] &= 0x7F;
            return mem._GetSInt8();
        }

        public short GetSInt15()
        {
            var mem = GetBytes(2)._CloneSpan();
            mem[0] &= 0x7F;
            return mem._GetSInt16();
        }

        public int GetSInt31()
        {
            var mem = GetBytes(4)._CloneSpan();
            mem[0] &= 0x7F;
            return mem._GetSInt32();
        }

        public long GetSInt63()
        {
            var mem = GetBytes(8)._CloneSpan();
            mem[0] &= 0x7F;
            return mem._GetSInt64();
        }

        public bool GetBool()
        {
            return (GetUInt32() % 2) == 0;
        }
    }


    public static class ExeSignChecker
    {
        public static bool IsKernelModeSignedFile(string fileName)
        {
            return IsKernelModeSignedFile(IO.ReadFile(fileName));
        }

        public static bool IsKernelModeSignedFile(ReadOnlySpan<byte> data)
        {
            string str = Str.AsciiEncoding.GetString(data);

            if (str.IndexOf("Microsoft Code Verification Root") != -1 &&
                str.IndexOf("http://crl.microsoft.com/pki/crl/products/MicrosoftCodeVerifRoot.crl") != -1)
            {
                return true;
            }

            return false;
        }

        enum SignChecker_MemoryAllocator { HGlobal, CoTaskMem };
        enum SignChecker_UiChoice { All = 1, NoUI, NoBad, NoGood };
        enum SignChecker_StateAction { Ignore = 0, Verify, Close, AutoCache, AutoCacheFlush };
        enum SignChecker_UnionChoice { File = 1, Catalog, Blob, Signer, Cert };
        enum SignChecker_RevocationCheckFlags { None = 0, WholeChain };
        enum SignChecker_TrustProviderFlags
        {
            UseIE4Trust = 1,
            NoIE4Chain = 2,
            NoPolicyUsage = 4,
            RevocationCheckNone = 16,
            RevocationCheckEndCert = 32,
            RevocationCheckChain = 64,
            RecovationCheckChainExcludeRoot = 128,
            Safer = 256,
            HashOnly = 512,
            UseDefaultOSVerCheck = 1024,
            LifetimeSigning = 2048
        };
        enum SignChecker_UIContext { Execute = 0, Install };

        [DllImport("Wintrust.dll", PreserveSig = true, SetLastError = false)]
        static extern uint WinVerifyTrust(IntPtr hWnd, IntPtr pgActionID, IntPtr pWinTrustData);

        sealed class SignCheckerUnmanagedPointer : IDisposable
        {
            private IntPtr m_ptr;
            private SignChecker_MemoryAllocator m_meth;
            public SignCheckerUnmanagedPointer(IntPtr ptr, SignChecker_MemoryAllocator method)
            {
                m_meth = method;
                m_ptr = ptr;
            }

            ~SignCheckerUnmanagedPointer()
            {
                Dispose(false);
            }

            void Dispose(bool disposing)
            {
                if (m_ptr != IntPtr.Zero)
                {
                    if (m_meth == SignChecker_MemoryAllocator.HGlobal)
                    {
                        Marshal.FreeHGlobal(m_ptr);
                    }
                    else if (m_meth == SignChecker_MemoryAllocator.CoTaskMem)
                    {
                        Marshal.FreeCoTaskMem(m_ptr);
                    }
                    m_ptr = IntPtr.Zero;
                }

                if (disposing)
                {
                    GC.SuppressFinalize(this);
                }
            }

            public void Dispose()
            {
                Dispose(true);
            }

            public static implicit operator IntPtr(SignCheckerUnmanagedPointer ptr)
            {
                return ptr.m_ptr;
            }
        }

        struct WINTRUST_FILE_INFO : IDisposable
        {
            public WINTRUST_FILE_INFO(string fileName, Guid subject)
            {
                cbStruct = (uint)Marshal.SizeOf(typeof(WINTRUST_FILE_INFO));
                pcwszFilePath = fileName;

                if (subject != Guid.Empty)
                {
                    tmp = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(Guid)));
                    Marshal.StructureToPtr(subject, tmp, false);
                }
                else
                {
                    tmp = IntPtr.Zero;
                }
                hFile = IntPtr.Zero;
            }
            public uint cbStruct;
            [MarshalAs(UnmanagedType.LPTStr)]
            public string pcwszFilePath;
            public IntPtr hFile;
            public IntPtr tmp;

            public void Dispose()
            {
                Dispose(true);
            }

            private void Dispose(bool disposing)
            {
                if (tmp != IntPtr.Zero)
                {
                    Marshal.DestroyStructure(this.tmp, typeof(Guid));
                    Marshal.FreeHGlobal(this.tmp);
                }
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        struct WINTRUST_DATA : IDisposable
        {
            public WINTRUST_DATA(WINTRUST_FILE_INFO fileInfo)
            {
                this.cbStruct = (uint)Marshal.SizeOf(typeof(WINTRUST_DATA));
                pInfoStruct = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(WINTRUST_FILE_INFO)));
                Marshal.StructureToPtr(fileInfo, pInfoStruct, false);
                dwUnionChoice = SignChecker_UnionChoice.File;
                pPolicyCallbackData = IntPtr.Zero;
                pSIPCallbackData = IntPtr.Zero;
                dwUIChoice = SignChecker_UiChoice.NoUI;
                fdwRevocationChecks = SignChecker_RevocationCheckFlags.WholeChain;
                dwStateAction = SignChecker_StateAction.Ignore;
                hWVTStateData = IntPtr.Zero;
                pwszURLReference = IntPtr.Zero;
                dwProvFlags = SignChecker_TrustProviderFlags.RevocationCheckChain;

                dwUIContext = SignChecker_UIContext.Execute;
            }

            public uint cbStruct;
            public IntPtr pPolicyCallbackData;
            public IntPtr pSIPCallbackData;
            public SignChecker_UiChoice dwUIChoice;
            public SignChecker_RevocationCheckFlags fdwRevocationChecks;
            public SignChecker_UnionChoice dwUnionChoice;
            public IntPtr pInfoStruct;
            public SignChecker_StateAction dwStateAction;
            public IntPtr hWVTStateData;
            private IntPtr pwszURLReference;
            public SignChecker_TrustProviderFlags dwProvFlags;
            public SignChecker_UIContext dwUIContext;

            public void Dispose()
            {
                Dispose(true);
            }

            private void Dispose(bool disposing)
            {
                if (dwUnionChoice == SignChecker_UnionChoice.File)
                {
                    WINTRUST_FILE_INFO info = new WINTRUST_FILE_INFO();
                    Marshal.PtrToStructure(pInfoStruct, info);
                    info.Dispose();
                    Marshal.DestroyStructure(pInfoStruct, typeof(WINTRUST_FILE_INFO));
                }

                Marshal.FreeHGlobal(pInfoStruct);
            }
        }

        public static bool CheckFileDigitalSignature(string fileName)
        {
            Guid wintrust_action_generic_verify_v2 = new Guid("{00AAC56B-CD44-11d0-8CC2-00C04FC295EE}");
            WINTRUST_FILE_INFO fileInfo = new WINTRUST_FILE_INFO(fileName, Guid.Empty);
            WINTRUST_DATA data = new WINTRUST_DATA(fileInfo);

            uint ret = 0;

            using (SignCheckerUnmanagedPointer guidPtr = new SignCheckerUnmanagedPointer(Marshal.AllocHGlobal(Marshal.SizeOf(typeof(Guid))), SignChecker_MemoryAllocator.HGlobal))
            using (SignCheckerUnmanagedPointer wvtDataPtr = new SignCheckerUnmanagedPointer(Marshal.AllocHGlobal(Marshal.SizeOf(typeof(WINTRUST_DATA))), SignChecker_MemoryAllocator.HGlobal))
            {
                IntPtr pGuid = guidPtr;
                IntPtr pData = wvtDataPtr;

                Marshal.StructureToPtr(wintrust_action_generic_verify_v2, pGuid, false);
                Marshal.StructureToPtr(data, pData, false);

                ret = WinVerifyTrust(IntPtr.Zero, pGuid, pData);
            }

            if (ret != 0)
            {
                return false;
            }

            return true;
        }

        public static bool CheckFileDigitalSignature(ReadOnlyMemory<byte> data, bool checkDriverSignature = false)
        {
            string tmpPath = Lfs.SaveToTempFile("dat", data);

            bool b1 = CheckFileDigitalSignature(tmpPath);

            bool b2 = true;

            if (checkDriverSignature)
            {
                b2 = IsKernelModeSignedFile(data.Span);
            }

            return b1 && b2;
        }
    }

    namespace Legacy
    {
        public class RC4 : ICloneable
        {
            uint x, y;
            uint[] state;

            public RC4(byte[] key)
            {
                state = new uint[256];

                uint i, t, u, ki, si;

                x = 0;
                y = 0;

                for (i = 0; i < 256; i++)
                {
                    state[i] = i;
                }

                ki = si = 0;
                for (i = 0; i < 256; i++)
                {
                    t = state[i];

                    si = (si + key[ki] + t) & 0xff;
                    u = state[si];
                    state[si] = t;
                    state[i] = u;
                    if (++ki >= key.Length)
                    {
                        ki = 0;
                    }
                }
            }

            private RC4()
            {
                state = null!;
            }

            public object Clone()
            {
                RC4 rc4 = new RC4();

                rc4.x = this.x;
                rc4.y = this.y;
                rc4.state = (uint[])this.state.Clone();

                return rc4;
            }

            public byte[] Encrypt(byte[] src)
            {
                return Encrypt(src, src.Length);
            }
            public byte[] Encrypt(byte[] src, int len)
            {
                return Encrypt(src, 0, len);
            }
            public byte[] Encrypt(byte[] src, int offset, int len)
            {
                byte[] dst = new byte[len];

                uint x, y, sx, sy;
                x = this.x;
                y = this.y;

                int src_i = 0, dst_i = 0, end_src_i;

                for (end_src_i = src_i + len; src_i != end_src_i; src_i++, dst_i++)
                {
                    x = (x + 1) & 0xff;
                    sx = state[x];
                    y = (sx + y) & 0xff;
                    state[x] = sy = state[y];
                    state[y] = sx;
                    dst[dst_i] = (byte)(src[src_i + offset] ^ state[(sx + sy) & 0xff]);
                }

                this.x = x;
                this.y = y;

                return dst;
            }
            public void SkipDecrypt(int len)
            {
                SkipEncrypt(len);
            }
            public void SkipEncrypt(int len)
            {
                uint x, y, sx, sy;
                x = this.x;
                y = this.y;

                int src_i = 0, dst_i = 0, end_src_i;

                for (end_src_i = src_i + len; src_i != end_src_i; src_i++, dst_i++)
                {
                    x = (x + 1) & 0xff;
                    sx = state[x];
                    y = (sx + y) & 0xff;
                    state[x] = sy = state[y];
                    state[y] = sx;
                }

                this.x = x;
                this.y = y;
            }

            public byte[] Decrypt(byte[] src)
            {
                return Decrypt(src, src.Length);
            }
            public byte[] Decrypt(byte[] src, int len)
            {
                return Decrypt(src, 0, len);
            }
            public byte[] Decrypt(byte[] src, int offset, int len)
            {
                return Encrypt(src, offset, len);
            }
        }
    }

    // 証明書関係
    public delegate X509Certificate2 CertSelectorCallback(object? param, string sniHostname);
    public delegate Task<SslStreamCertificateContext> CertSelectorAsyncCallback2(object? param, string sniHostname);
}
