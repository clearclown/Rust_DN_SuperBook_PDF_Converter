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

#if true

#pragma warning disable CA2235 // Mark all non-serializable fields

using System;
using System.Buffers;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Serialization;
using System.IO.Compression;

using IPA.Cores.Basic.Internal;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

namespace IPA.Cores.Basic;

public static partial class Consts
{
    public static partial class SecureCompress
    {
        // Header Str はいずれも長さが同一でなければならない
        public const string SecureCompressFirstHeader_Str = "\r\n\r\n!!__[MetaData:SecureCompressFirstHeader]__!!\r\n";
        public static readonly ReadOnlyMemory<byte> SecureCompressFirstHeader_Data = SecureCompressFirstHeader_Str._GetBytes_Ascii();

        public const string SecureCompressBlockHeader_Str = "\r\n\r\n!!__[MetaData:SecureCompressBlockHeader]__!!\r\n";
        public static readonly ReadOnlyMemory<byte> SecureCompressBlockHeader_Data = SecureCompressBlockHeader_Str._GetBytes_Ascii();

        public const string SecureCompressFinalHeader_Str = "\r\n\r\n!!__[MetaData:SecureCompressFinalHeader]__!!\r\n";
        public static readonly ReadOnlyMemory<byte> SecureCompressFinalHeader_Data = SecureCompressFinalHeader_Str._GetBytes_Ascii();

        public const int BlockSize = 4096;
        public const int DataBlockSrcSize = 1024 * 1024;

        public const int DefaultKeepAliveMsecs = 10 * 1000;
    }
}

public class SecureCompressUtilRet
{
    public int NumErrors;
    public int NumWarnings;
    public long TotalReadSize;
    public SecureCompressFinalHeader? FinalHeader;
}

public static class SecureCompressUtil
{
    public static async Task<SecureCompressUtilRet> BackupFileAsync(FilePath srcFilePath, FilePath destFilePath, SecureCompressOptions options, long truncate = -1, bool writeProgressToConsole = false, CancellationToken cancel = default)
    {
        await using var srcFile = await srcFilePath.OpenAsync(cancel: cancel);

        await using var srcStream = srcFile.GetStream();

        await using var dstFile = await destFilePath.CreateAsync(cancel: cancel);

        await using var dstStream = dstFile.GetStream();

        long sizeHint = srcFile.Size;

        if (truncate >= 0)
        {
            sizeHint = Math.Min(truncate, sizeHint);
        }

        await using var secureWriter = new SecureCompressEncoder(dstStream, options, sizeHint, true);

        ProgressReporter? reporter = null;

        if (writeProgressToConsole)
        {
            reporter = new ProgressReporter(new ProgressReporterSetting(ProgressReporterOutputs.Console, fileSizeStr: true, title: "SecureCompress Backup " + srcFilePath.GetFileName()._TruncStrEx(16), options: ProgressReporterOptions.EnableThroughput));
        }

        using IDisposable progDisp = reporter._EmptyDisposableIfNull();

        long sz = await srcStream.CopyBetweenStreamAsync(secureWriter, reporter: reporter, estimatedSize: srcFile.Size, truncateSize: truncate);

        var finalHeader = await secureWriter.FinalizeAsync();

        return new SecureCompressUtilRet
        {
            FinalHeader = finalHeader,
            NumErrors = 0,
            NumWarnings = 0,
            TotalReadSize = sz,
        };
    }

    public static async Task<SecureCompressUtilRet> RestoreFileAsync(FilePath srcFilePath, FilePath destFilePath, SecureCompressOptions options, bool writeProgressToConsole = false, CancellationToken cancel = default)
    {
        await using var srcFile = await srcFilePath.OpenAsync(cancel: cancel);

        await using var srcStream = srcFile.GetStream();

        await using var dstFile = await destFilePath.CreateAsync(cancel: cancel);

        await using var dstStream = dstFile.GetStream();

        ProgressReporter? reporter = null;

        if (writeProgressToConsole)
        {
            reporter = new ProgressReporter(new ProgressReporterSetting(ProgressReporterOutputs.Console, fileSizeStr: true, title: "SecureCompress Restore " + srcFilePath.GetFileName()._TruncStrEx(16), options: ProgressReporterOptions.EnableThroughput));
        }

        await using var secureWriter = new SecureCompressDecoder(dstStream, options, srcFile.Size, true, reporter);

        using IDisposable progDisp = reporter._EmptyDisposableIfNull();

        long sz = await srcStream.CopyBetweenStreamAsync(secureWriter, estimatedSize: srcFile.Size);

        var finalHeader = await secureWriter.FinalizeAsync();

        return new SecureCompressUtilRet
        {
            NumErrors = secureWriter.NumError,
            NumWarnings = secureWriter.NumWarning,
            FinalHeader = finalHeader,
            TotalReadSize = sz,
        };
    }

    public static async Task<SecureCompressUtilRet> VerifyFileAsync(FilePath originalFilePath, FilePath archiveFilePath, SecureCompressOptions options, bool writeProgressToConsole = false, CancellationToken cancel = default)
    {
        string archiveHashSha1 = "";
        string originalHashSha1 = "";

        long archiveHashSize = 0;
        long originalHashSize = 0;

        SecureCompressUtilRet ret;

        {
            await using var archiveFile = await archiveFilePath.OpenAsync(cancel: cancel);

            await using var archiveStream = archiveFile.GetStream();

            ProgressReporter? reporterReadArchive = null;

            if (writeProgressToConsole)
            {
                reporterReadArchive = new ProgressReporter(new ProgressReporterSetting(ProgressReporterOutputs.Console, fileSizeStr: true, title: "SecureCompress Verify Read (Archive) " + archiveFilePath.GetFileName()._TruncStrEx(16), options: ProgressReporterOptions.EnableThroughput));
            }

            using IDisposable reporterProgDisp = reporterReadArchive._EmptyDisposableIfNull();

            await using var archiveHashStream = new HashCalcStream(SHA1.Create());

            await using var secureWriter = new SecureCompressDecoder(archiveHashStream, options, archiveFile.Size, true, reporterReadArchive);

            long sz = await archiveStream.CopyBetweenStreamAsync(secureWriter, estimatedSize: archiveFile.Size);

            await secureWriter.FinalizeAsync();

            archiveHashSha1 = archiveHashStream.GetFinalHash()._GetHexString();

            archiveHashSize = archiveHashStream.Length;

            $"----------"._Print();
            $"Archive File Name: {archiveFilePath}"._Print();
            $"Archive File Size: {archiveHashSize._ToString3()} bytes"._Print();
            $"Archive File SHA-1 Hash: {archiveHashSha1}"._Print();
            $"----------"._Print();

            ret = new SecureCompressUtilRet
            {
                NumErrors = secureWriter.NumError,
                NumWarnings = secureWriter.NumWarning,
            };
        }

        {
            await using var originalFile = await originalFilePath.OpenAsync(cancel: cancel);

            await using var originalStream = originalFile.GetStream();

            await using var originalHashStream = new HashCalcStream(SHA1.Create());

            ProgressReporter? reporterReadOriginal = null;

            if (writeProgressToConsole)
            {
                reporterReadOriginal = new ProgressReporter(new ProgressReporterSetting(ProgressReporterOutputs.Console, fileSizeStr: true, title: "SecureCompress Verify Read (Original) " + originalFilePath.GetFileName()._TruncStrEx(16), options: ProgressReporterOptions.EnableThroughput));
            }

            using IDisposable reporterProgDisp = reporterReadOriginal._EmptyDisposableIfNull();

            await originalStream.CopyBetweenStreamAsync(originalHashStream, estimatedSize: originalFile.Size, reporter: reporterReadOriginal);

            originalHashSha1 = originalHashStream.GetFinalHash()._GetHexString();

            originalHashSize = originalHashStream.Length;

            $"----------"._Print();
            $"Original File Name: {originalFilePath}"._Print();
            $"Original File Size: {originalHashSize._ToString3()} bytes"._Print();
            $"Original File SHA-1 Hash: {originalHashSha1}"._Print();
            $"----------"._Print();

            ""._Print();
        }

        if (archiveHashSha1 == originalHashSha1)
        {
            $"Ok. Both files are exactly same. SHA1 = {archiveHashSha1}, Size = {originalHashSize._ToString3()}"._Print();
        }
        else
        {
            throw new CoresException($"File hash is different. Archive file size = {archiveHashSize._ToString3()} bytes, Archive file hash = {archiveHashSha1}, Original file size = {originalHashSize._ToString3()} bytes, Original file hash = {originalHashSha1}");
        }

        return ret;
    }
}

public class SecureCompressFinalHeader
{
    public long ChunkCount;
    public long SrcSize;
    public long DestContentSize;
    public long DestPhysicalSize;

    public string SrcSha1 = "";
    public string DestSha1 = "";

    public DateTimeOffset TimeStamp = Util.ZeroDateTimeOffsetValue;

    public SecureCompressFirstHeader? CopyOfFirstHeader;
}

public class SecureCompressBlockHeader : IValidatable
{
    public long ChunkIndex;
    public int DestDataSize;
    public int DestDataSizeWithoutPadding;
    public long SrcDataPosition;
    public int SrcDataLength;
    public string SrcSha1 = "";
    public string DestSha1 = "";
    public bool IsAllZero;
    public bool IsEoF;
    public DateTimeOffset TimeStamp = Util.ZeroDateTimeOffsetValue;

    public void Validate()
    {
        if (DestDataSize < 0) throw new CoresLibException($"DestDataSize ({DestDataSize}) < 0");
        if (DestDataSizeWithoutPadding < 0) throw new CoresLibException($"DestDataSizeWithoutPadding ({DestDataSizeWithoutPadding}) < 0");
        if (SrcDataPosition < 0) throw new CoresLibException($"SrcDataPosition ({SrcDataPosition}) < 0");
        if (SrcDataLength < 0) throw new CoresLibException($"SrcDataLength ({SrcDataLength}) < 0");
    }
}

public class SecureCompressFirstHeader
{
    public int Version;
    public bool Encrypted;
    public bool Compressed;
    public string SaltedPassword = "";
    public string MasterKeyEncryptedByPassword = "";
    public long SrcDataSizeHint;
    public DateTimeOffset TimeStamp = Util.ZeroDateTimeOffsetValue;
    public string Hostname = "";
}

[Flags]
public enum SecureCompressFlags
{
    None = 0,
    CalcZipCrc32,
}

public class SecureCompressOptions
{
    public string FileNameHint { get; }
    public bool Encrypt { get; }
    public bool Compress { get; }
    public string Password { get; }
    public CompressionLevel CompressionLevel { get; }
    public int NumCpu { get; }
    public SecureCompressFlags Flags { get; }
    public int KeepAliveMsecs { get; }

    public SecureCompressOptions(string fileNameHint, bool encrypt = false, string password = "", bool compress = true, CompressionLevel compressionLevel = CompressionLevel.SmallestSize, int numCpu = -1, SecureCompressFlags flags = SecureCompressFlags.None, int keepAliveMsecs = -1)
    {
        this.FileNameHint = fileNameHint;

        this.Encrypt = encrypt;
        this.Password = password._NonNull();

        this.Compress = compress;

        if (numCpu <= 0)
        {
            numCpu = Env.NumCpus;
        }

        if (numCpu >= 32)
        {
            numCpu = 32;
        }

        this.NumCpu = numCpu;

        this.CompressionLevel = compressionLevel;

        this.Flags = flags;

        if (keepAliveMsecs < 0)
        {
            keepAliveMsecs = Consts.SecureCompress.DefaultKeepAliveMsecs;
        }

        this.KeepAliveMsecs = keepAliveMsecs;
    }
}

public class SecureCompressDecoder : StreamImplBase
{
    public bool AutoDispose { get; }
    public Stream DestStream { get; }

    readonly StreamBasedSequentialWritable DestWritable;
    readonly SequentialWritableBasedRandomAccess<byte> DestRandomWriter;

    public override bool DataAvailable => throw new NotImplementedException();

    public SecureCompressOptions Options { get; }

    int CurrentSrcDataBufferMaxSize => Consts.SecureCompress.DataBlockSrcSize * Options.NumCpu * 2;
    readonly FastStreamBuffer CurrentSrcDataBuffer;

    ReadOnlyMemory<byte> MasterSecret;

    HashCalc DestHash_Sha1 = new HashCalc(SHA1.Create());

    Exception? LastException = null;

    ProgressReporterBase? Reporter = null;

    public int NumError { get; private set; }
    public int NumWarning { get; private set; }

    public uint Crc32Value => this.DestWritable.Crc32Value;

    public SecureCompressDecoder(Stream destStream, SecureCompressOptions options, long srcDataSizeHint = -1, bool autoDispose = false, ProgressReporterBase? reporter = null, HashCalcStream? hashCalcStream = null)
        : base(new StreamImplBaseOptions(false, true, false))
    {
        try
        {
            this.AutoDispose = autoDispose;
            this.DestStream = destStream;
            this.Options = options;
            this.Reporter = reporter;

            this.DestWritable = new StreamBasedSequentialWritable(this.DestStream, autoDispose, this.Options.Flags.Bit(SecureCompressFlags.CalcZipCrc32), hashCalcStream);
            this.DestRandomWriter = new SequentialWritableBasedRandomAccess<byte>(this.DestWritable, allowForwardSeek: true);

            this.CurrentSrcDataBuffer = new FastStreamBuffer();
        }
        catch
        {
            this._DisposeSafeSync();
            throw;
        }
    }

    SecureCompressFirstHeader? FirstHeader = null;
    SecureCompressFinalHeader? FinalHeader = null;

    public class Chunk
    {
        public SecureCompressBlockHeader Header = null!;
        public Memory<byte> SrcData;
        public long SrcOffset;
        public ReadOnlyMemory<byte> DstData;

        public string? Warning;
        public string? Error;
    }

    async Task ProcessAndSendBufferedDataAsync(CancellationToken cancel = default)
    {
        if (LastException != null)
        {
            throw LastException;
        }

        try
        {
            List<Chunk> currentChunkList = new List<Chunk>();

            bool escapeFlag = false;

            while (this.CurrentSrcDataBuffer.Length >= Consts.SecureCompress.BlockSize)
            {
                // 次の 1 ブロックのヘッダ部分の読み出しを実施する
                var headerMemory = this.CurrentSrcDataBuffer.GetContiguous(this.CurrentSrcDataBuffer.PinHead, Consts.SecureCompress.BlockSize, false);

                if (this.CurrentSrcDataBuffer.PinHead == 0)
                {
                    if (!(ParseHeader(headerMemory, this.CurrentSrcDataBuffer.PinHead) is SecureCompressFirstHeader firstHeader))
                    {
                        // ファイルの先頭にヘッダが付いていない
                        throw new CoresException($"This file has no SecureCompressFirstHeader at the beginning");
                    }

                    this.FirstHeader = firstHeader;

                    if (this.FirstHeader.Version > 1)
                    {
                        throw new CoresException($"Unsupported SecureCompress version: {this.FirstHeader.Version}. You have to upgrade the program version to support it.");
                    }

                    if (this.FirstHeader.Encrypted)
                    {
                        if (Secure.VeritySaltedPassword(FirstHeader.SaltedPassword, this.Options.Password) == false)
                        {
                            throw new CoresException("Invalid decrypt password");
                        }

                        this.MasterSecret = ChaChaPoly.EasyDecryptWithPassword(this.FirstHeader.MasterKeyEncryptedByPassword._GetHexBytes(), this.Options.Password).Value;

                        //$"Master = {masterKey.Value._GetHexString()}"._Debug();
                    }
                }

                try
                {
                    object? header = ParseHeader(headerMemory, this.CurrentSrcDataBuffer.PinHead);

                    int forwardSize = Consts.SecureCompress.BlockSize;

                    if (header != null)
                    {
                        if (header is SecureCompressFirstHeader firstHeader)
                        {
                            if (this.FirstHeader._ObjectToJson() != firstHeader._ObjectToJson())
                            {
                                throw new CoresException($"Duplicate different FirstHeader. Previous = {this.FirstHeader._ObjectToJson(compact: true)}, This = {firstHeader._ObjectToJson(compact: true)}");
                            }
                        }
                        else if (header is SecureCompressBlockHeader blockHeader)
                        {
                            blockHeader.Validate();

                            if (this.CurrentSrcDataBuffer.Length >= (Consts.SecureCompress.BlockSize + blockHeader.DestDataSize))
                            {
                                ReadOnlyMemory<byte> srcData = Memory<byte>.Empty;

                                srcData = this.CurrentSrcDataBuffer.GetContiguous(this.CurrentSrcDataBuffer.PinHead + Consts.SecureCompress.BlockSize, blockHeader.DestDataSize, false);

                                forwardSize += srcData.Length;

                                Chunk c = new Chunk
                                {
                                    Header = blockHeader,
                                    SrcData = srcData.ToArray(),
                                    SrcOffset = this.CurrentSrcDataBuffer.PinHead + Consts.SecureCompress.BlockSize,
                                };

                                currentChunkList.Add(c);

                                if (currentChunkList.Count >= this.Options.NumCpu)
                                {
                                    await DecodeAndProcessChunkListAsync(currentChunkList);
                                }
                            }
                            else
                            {
                                // 処理を予定しているブロックが尻切れトンボとなっているので、ここでの処理は諦めて次回に任せる
                                // (forwardSize を 0 にして、読み進めないようにする)
                                forwardSize = 0;
                                escapeFlag = true;
                            }
                        }
                        else if (header is SecureCompressFinalHeader finalHeader)
                        {
                            this.FinalHeader = finalHeader;
                        }
                        else
                        {
                            //Dbg.Where();
                        }
                    }
                    else
                    {
                        //Dbg.Where();
                    }

                    if (forwardSize >= 1)
                    {
                        this.CurrentSrcDataBuffer.Dequeue(forwardSize, out _, true);
                    }

                    if (escapeFlag)
                    {
                        break;
                    }
                }
                catch (Exception ex)
                {
                    $"{this.Options.FileNameHint}: Offset = {this.CurrentSrcDataBuffer.PinHead}. Error = {ex.ToString()}"._Error();
                    this.NumError++;

                    this.CurrentSrcDataBuffer.Dequeue(Consts.SecureCompress.BlockSize, out _, true);
                }
            }

            await DecodeAndProcessChunkListAsync(currentChunkList);

            // 溜まったチャンクデータのデコードと書き込み処理
            async Task DecodeAndProcessChunkListAsync(List<Chunk> chunkList)
            {
                var copyOfList = chunkList.ToArray();

                chunkList.Clear();

                if (copyOfList.Length == 0)
                {
                    return;
                }

                await TaskUtil.ForEachAsync(int.MaxValue, copyOfList, (chunk, index, cancel) =>
                {
                    try
                    {
                        var tmp1 = chunk.SrcData._AsReadOnlyMemory();

                        byte[] sha1;

                        if (chunk.Header.IsEoF)
                        {
                        }
                        else if (chunk.Header.IsAllZero)
                        {
                            tmp1 = Util.GetZeroedSharedBuffer<byte>(chunk.Header.SrcDataLength);
                        }
                        else
                        {
                            // ハッシュ比較
                            sha1 = Secure.HashSHA1(tmp1.Span);
                            if (sha1._GetHexString()._CompareHex(chunk.Header.DestSha1) != 0)
                            {
                                chunk.Warning = $"DestSha1 is different. Header: {chunk.Header.DestSha1}, Real: {sha1._GetHexString()}";
                            }

                            if (this.FirstHeader!.Encrypted)
                            {
                                // 解読の実施
                                Memory<byte> destMemory = new byte[tmp1.Length];

                                XtsAes256Util.Decrypt(destMemory, tmp1, this.MasterSecret, (ulong)chunk.Header.SrcDataPosition);

                                //chunk.Header.SrcDataPosition._Print();

                                tmp1 = destMemory;
                            }

                            if (chunk.Header.DestDataSizeWithoutPadding != tmp1.Length)
                            {
                                if (chunk.Header.DestDataSizeWithoutPadding > tmp1.Length)
                                {
                                    // おかしな値
                                    throw new CoresException($"chunk.Header.DestDataSizeWithoutPadding ({chunk.Header.DestDataSizeWithoutPadding}) > tmp1.Length ({tmp1.Length})");
                                }
                                else if (chunk.Header.DestDataSizeWithoutPadding < 0)
                                {
                                    // おかしな値
                                    throw new CoresException($"chunk.Header.DestDataSizeWithoutPadding ({chunk.Header.DestDataSizeWithoutPadding}) < 0");
                                }
                                else
                                {
                                    // パディング解除
                                    tmp1 = tmp1.Slice(0, chunk.Header.DestDataSizeWithoutPadding);
                                }
                            }

                            if (this.FirstHeader!.Compressed)
                            {
                                //tmp1.Span[Secure.RandSInt31() % tmp1.Length] = Secure.RandUInt8();
                                // 圧縮解除の実施
                                tmp1 = DeflateUtil.EasyDecompress(tmp1, Consts.SecureCompress.DataBlockSrcSize);
                            }
                        }

                        // ハッシュ比較
                        if (chunk.Header.IsEoF == false)
                        {
                            sha1 = Secure.HashSHA1(tmp1.Span);
                            if (sha1._GetHexString()._CompareHex(chunk.Header.SrcSha1) != 0)
                            {
                                chunk.Warning = $"SrcSha1 is different. Header: {chunk.Header.SrcSha1}, Real: {sha1._GetHexString()}";
                            }
                        }

                        chunk.DstData = tmp1;

                        // メモリ節約のためソースデータを解放
                        chunk.SrcData = Memory<byte>.Empty;
                    }
                    catch (Exception ex)
                    {
                        chunk.Error = ex.Message;
                    }

                    return TR();
                },
                cancel);

                // 結果を書き込み
                foreach (var chunk in copyOfList)
                {
                    if (chunk.Warning._IsFilled())
                    {
                        $"{this.Options.FileNameHint}: Offset: {chunk.SrcOffset} Warning: {chunk.Warning}"._Error();
                        this.NumWarning++;
                    }

                    this.DestRandomWriter.HashCalcForWrite = DestHash_Sha1;
                    this.DestRandomWriter.Reporter = this.Reporter;

                    if (this.FirstHeader!.SrcDataSizeHint >= 0)
                    {
                        this.DestRandomWriter.Reporter_EstimatedTotalSize = this.FirstHeader!.SrcDataSizeHint;
                    }
                    else
                    {
                        this.DestRandomWriter.Reporter_EstimatedTotalSize = null;
                    }

                    if (chunk.Error._IsFilled())
                    {
                        $"{this.Options.FileNameHint}: Offset: {chunk.SrcOffset} Error: {chunk.Error}"._Error();
                        this.NumError++;
                        await this.DestRandomWriter.WriteRandomAsync(chunk.Header.SrcDataPosition, Util.GetZeroedSharedBuffer<byte>(chunk.Header.SrcDataLength), cancel);
                    }
                    else
                    {
                        //long diff = chunk.Header.SrcDataPosition - lastPos;

                        //diff._Print();

                        //$"pos = {chunk.Header.SrcDataPosition}, diff = {diff}"._Print();

                        if (chunk.Header.IsEoF == false)
                        {
                            await this.DestRandomWriter.WriteRandomAsync(chunk.Header.SrcDataPosition, chunk.DstData, cancel);

                            //DestHash_Sha1.Write(chunk.DstData);
                            //lastPos = chunk.Header.SrcDataPosition + chunk.DstData.Length;
                        }
                        else
                        {
                            await this.DestRandomWriter.WriteRandomAsync(chunk.Header.SrcDataPosition, ReadOnlyMemory<byte>.Empty, cancel);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            this.LastException = ex;
            throw;
        }
    }

    object? ParseHeader(ReadOnlyMemory<byte> data, long offsetHint)
    {
        try
        {
            if (data.Length != Consts.SecureCompress.BlockSize)
            {
                throw new CoresLibException($"Offset {offsetHint}: data.Length ({data.Length}) != Consts.SecureCompress.BlockSize ({Consts.SecureCompress.BlockSize})");
            }

            int headerStrSize = Consts.SecureCompress.SecureCompressFirstHeader_Data.Length;

            var headOfData = data.Slice(0, headerStrSize);

            data._Walk(headerStrSize);

            if (headOfData._MemEquals(Consts.SecureCompress.SecureCompressFirstHeader_Data))
            {
                string jsonStr = data._GetString_UTF8(true);

                return jsonStr._JsonToObject<SecureCompressFirstHeader>();
            }
            else if (headOfData._MemEquals(Consts.SecureCompress.SecureCompressBlockHeader_Data))
            {
                string jsonStr = data._GetString_UTF8(true);

                return jsonStr._JsonToObject<SecureCompressBlockHeader>();
            }
            else if (headOfData._MemEquals(Consts.SecureCompress.SecureCompressFinalHeader_Data))
            {
                string jsonStr = data._GetString_UTF8(true);

                return jsonStr._JsonToObject<SecureCompressFinalHeader>();
            }
        }
        catch (Exception ex)
        {
            $"{this.Options.FileNameHint}: ParseHeader error: Offset {offsetHint}"._Error();
            ex._Error();
            this.NumError++;
        }

        return null;
    }

    protected override async ValueTask WriteImplAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancel = default)
    {
        if (buffer.IsEmpty)
        {
            return;
        }

        while (buffer.Length >= 1)
        {
            long remainBufSize = this.CurrentSrcDataBufferMaxSize - this.CurrentSrcDataBuffer.Length;

            if (remainBufSize >= 1)
            {
                int sz = Math.Min(buffer.Length, (int)remainBufSize);

                var part = buffer._WalkRead(sz);

                this.CurrentSrcDataBuffer.Enqueue(part.ToArray());
            }

            if (this.CurrentSrcDataBuffer.Length >= this.CurrentSrcDataBufferMaxSize)
            {
                await ProcessAndSendBufferedDataAsync(cancel);
            }
        }
    }

    Once FinalizedFlag;

    SecureCompressFinalHeader? FinalHeaderRet;

    public async Task<SecureCompressFinalHeader> FinalizeAsync(CancellationToken cancel = default)
    {
        if (LastException != null)
        {
            throw LastException;
        }

        try
        {
            if (FinalizedFlag.IsFirstCall())
            {
                // 必ず 1 回は呼び出す
                await ProcessAndSendBufferedDataAsync(cancel);

                if (this.FirstHeader == null)
                {
                    $"{this.Options.FileNameHint}: ParseHeader error: No first header found"._Error();

                    this.NumError++;

                    throw new CoresException($"{this.Options.FileNameHint}: ParseHeader error: No first header found");
                }

                if (this.FinalHeader == null)
                {
                    $"{this.Options.FileNameHint}: ParseHeader error: No final header found"._Error();
                    this.NumError++;
                }
                else
                {
                    if (this.FinalHeader.SrcSha1._CompareHex(this.DestHash_Sha1.GetFinalHash()._GetHexString()) != 0)
                    {
                        $"{this.Options.FileNameHint}: FinalHeader's SrcSha1 is different. Header: {this.FinalHeader.SrcSha1}, Real: {this.DestHash_Sha1.GetFinalHash()._GetHexString()}"._Error();
                        this.NumError++;
                    }

                    FinalHeaderRet = this.FinalHeader._CloneDeep();
                }
            }

            return FinalHeaderRet ?? new SecureCompressFinalHeader();
        }
        catch (Exception ex)
        {
            this.LastException = ex;
            throw;
        }
    }

    protected override Task FlushImplAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
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
    async Task DisposeInternalAsync()
    {
        try
        {
            await FinalizeAsync();
        }
        finally
        {
            await this.DestRandomWriter._DisposeSafeAsync();

            await this.DestWritable._DisposeSafeAsync();

            if (this.AutoDispose)
            {
                await this.DestStream._DisposeSafeAsync();
            }
        }
    }

    protected override long GetLengthImpl()
    {
        throw new NotImplementedException();
    }

    protected override void SetLengthImpl(long length)
    {
        throw new NotImplementedException();
    }

    protected override long GetPositionImpl()
    {
        throw new NotImplementedException();
    }

    protected override void SetPositionImpl(long position)
    {
        throw new NotImplementedException();
    }

    protected override long SeekImpl(long offset, SeekOrigin origin)
    {
        throw new NotImplementedException();
    }

    protected override ValueTask<int> ReadImplAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }
}

public class SecureCompressEncoder : StreamImplBase
{
    public bool AutoDispose { get; }
    public Stream DestStream { get; }

    public override bool DataAvailable => throw new NotImplementedException();

    public SecureCompressOptions Options { get; }

    readonly SecureCompressFirstHeader FirstHeader;

    readonly SecureCompressFinalHeader CurrentFinalHeader = new SecureCompressFinalHeader();

    readonly ReadOnlyMemory<byte> MasterSecret;

    int CurrentSrcDataBufferMaxSize => Consts.SecureCompress.DataBlockSrcSize * Options.NumCpu;
    readonly MemoryBuffer<byte> CurrentSrcDataBuffer;

    long CurrentSrcDataPosition = 0;

    long CurrentNumChunks = 0;

    bool FirstWriteFlag = false;

    HashCalc SrcHash_Sha1 = new HashCalc(SHA1.Create());

    HashCalc DestHash_Sha1 = new HashCalc(SHA1.Create());

    public SecureCompressEncoder(Stream destStream, SecureCompressOptions options, long srcDataSizeHint = -1, bool autoDispose = false)
        : base(new StreamImplBaseOptions(false, true, false))
    {
        try
        {
            this.AutoDispose = autoDispose;
            this.DestStream = destStream;
            this.Options = options;

            if (options.Encrypt == false && options.Compress == false)
            {
                throw new CoresLibException("encrypt == false && compress == false");
            }

            if (options.Compress && options.CompressionLevel == CompressionLevel.NoCompression)
            {
                throw new CoresLibException("compress == true && compressionLevel == CompressionLevel.NoCompression");
            }

            MasterSecret = Secure.RandWithInchikiEntropySlow(XtsAesRandomAccess.XtsAesKeySize);

            //$"Master = {this.CurrentMasterKey._GetHexString()}"._Debug();

            FirstHeader = new SecureCompressFirstHeader
            {
                Version = 1,
                Encrypted = this.Options.Encrypt,
                Compressed = this.Options.Compress,
                SrcDataSizeHint = srcDataSizeHint,
                SaltedPassword = Secure.SaltPassword(options.Password),

                MasterKeyEncryptedByPassword = ChaChaPoly.EasyEncryptWithPassword(this.MasterSecret, this.Options.Password)._GetHexString(),
            };

            if (FirstHeader.Encrypted == false)
            {
                FirstHeader.SaltedPassword = "";
                FirstHeader.MasterKeyEncryptedByPassword = "";
            }

            this.CurrentSrcDataBuffer = new MemoryBuffer<byte>(EnsureSpecial.Yes, CurrentSrcDataBufferMaxSize);
        }
        catch
        {
            this._DisposeSafeSync();
            throw;
        }
    }

    public static Memory<byte> HeaderToData(string signature, object header)
    {
        MemoryBuffer<byte> tmp = new MemoryBuffer<byte>(EnsureSpecial.Yes, Consts.SecureCompress.BlockSize);

        tmp.Write(signature._GetBytes_Ascii());

        string jsonBody = "\r\n" + header._ObjectToJson() + "\r\n";

        jsonBody = jsonBody._NormalizeCrlf(CrlfStyle.CrLf);

        tmp.Write(jsonBody._GetBytes_UTF8());

        int currentSize = tmp.Length;

        if (currentSize > (Consts.SecureCompress.BlockSize - 4))
        {
            throw new CoresLibException($"currentSize ({currentSize}) > (Consts.SecureCompress.BlockSize - 1) ({Consts.SecureCompress.BlockSize - 1})");
        }

        int padSize = Consts.SecureCompress.BlockSize - currentSize;

        var padMemory = Util.GetZeroedSharedBuffer<byte>(padSize);

        tmp.Write(padMemory);

        var ret = tmp.Memory;

        ret.Span[ret.Length - 2] = 13;
        ret.Span[ret.Length - 1] = 10;

        return ret;
    }

    class Chunk
    {
        public long SrcPosition;
        public int DestWithoutPaddingSize;
        public Memory<byte> SrcData;
        public Memory<byte> DestData;
        public string SrcSha1 = "";
        public string DestSha1 = "";
        public bool IsAllZero;
    }

    Exception? LastException = null;

    bool IsLastChunkAllZero = false;

    long LastChunkWrittenTick = 0;

    // メモリ上の CurrentSrcDataBuffer の内容 (これは、1MB * CPU 数のサイズが上限なはずである) を出力ストリームに書き出す
    async Task ProcessAndSendBufferedDataAsync(CancellationToken cancel = default)
    {
        if (LastException != null)
        {
            throw LastException;
        }

        try
        {
            if (this.CurrentSrcDataBuffer.Length > this.CurrentSrcDataBufferMaxSize)
            {
                // バッファサイズがおかしな状態になっている (ここには到達し得ないはず)
                throw new CoresLibException($"this.CurrentSrcDataBuffer.Length ({this.CurrentSrcDataBuffer.Length}) > (this.CurrentSrcDataBufferMaxSize - this.CurrentSrcDataBuffer.Length ({this.CurrentSrcDataBufferMaxSize - this.CurrentSrcDataBuffer.Length}))");
            }

            // バッファを CPU 数で分割
            int numCpu = (this.CurrentSrcDataBuffer.Length + (Consts.SecureCompress.DataBlockSrcSize - 1)) / Consts.SecureCompress.DataBlockSrcSize;
            int currentPos = 0;
            List<Chunk> chunkList = new List<Chunk>();

            for (int i = 0; i < numCpu; i++)
            {
                Chunk chunk = new Chunk
                {
                    SrcPosition = this.CurrentSrcDataPosition + currentPos,
                    SrcData = this.CurrentSrcDataBuffer.Slice(currentPos, Math.Min(this.CurrentSrcDataBuffer.Length - currentPos, Consts.SecureCompress.DataBlockSrcSize)),
                };

                chunkList.Add(chunk);

                currentPos += chunk.SrcData.Length;
            }

            CurrentSrcDataPosition += currentPos;

            // 分割タスクを並列実行
            await TaskUtil.ForEachAsync(int.MaxValue, chunkList, (chunk, index, cancel) =>
            {
                Memory<byte> tmp = chunk.SrcData;

                lock (CurrentFinalHeader)
                {
                    CurrentFinalHeader.SrcSize += tmp.Length;
                }

                chunk.SrcSha1 = Secure.HashSHA1(tmp.Span)._GetHexString();

                if (Util.IsZeroFast(tmp) == false)
                {
                    if (this.Options.Compress)
                    {
                        // 圧縮の実施
                        tmp = DeflateUtil.EasyCompressRetMemoryFast(tmp.Span, this.Options.CompressionLevel);
                    }

                    lock (CurrentFinalHeader)
                    {
                        CurrentFinalHeader.DestContentSize += tmp.Length;
                    }

                    chunk.DestWithoutPaddingSize = tmp.Length;

                    int paddingSize = 0;

                    // パディングの実施 (4096 の倍数でない場合)
                    if ((tmp.Length % Consts.SecureCompress.BlockSize) != 0)
                    {
                        paddingSize = Consts.SecureCompress.BlockSize - (tmp.Length % Consts.SecureCompress.BlockSize);
                    }

                    int paddedTotalSize = tmp.Length + paddingSize;

                    MemoryBuffer<byte> tmp2 = new MemoryBuffer<byte>(EnsureSpecial.Yes, paddedTotalSize);
                    tmp2.Write(tmp);

                    if (paddingSize >= 1)
                    {
                        if (this.Options.Encrypt == false)
                        {
                            tmp2.Write(Util.GetZeroedSharedBuffer<byte>(paddingSize));
                        }
                        else
                        {
                            tmp2.Write(Secure.Rand(paddingSize));
                        }
                    }

                    Memory<byte> tmp3 = tmp2.Memory;

                    if (this.Options.Encrypt)
                    {
                        // 暗号化の実施
                        //var srcSeg = tmp3._AsSegment();

                        //"Hello"._GetBytes_Ascii().CopyTo(tmp3);

                        Memory<byte> destMemory = new byte[tmp3.Length];

                        //var destSeg = destMemory._AsSegment();

                        XtsAes256Util.Encrypt(destMemory, tmp3, this.MasterSecret, (ulong)chunk.SrcPosition);
                        //this.CurrentEncrypter.TransformBlock(srcSeg.Array!, srcSeg.Offset, srcSeg.Count, destSeg.Array!, destSeg.Offset, (ulong)chunk.SrcPosition);

                        //chunk.SrcPosition._Print();

                        tmp3 = destMemory;
                    }

                    chunk.DestData = tmp3;

                    chunk.DestSha1 = Secure.HashSHA1(tmp3.Span)._GetHexString();
                }
                else
                {
                    chunk.IsAllZero = true;
                    chunk.DestData = Memory<byte>.Empty;
                    chunk.DestSha1 = "0000000000000000000000000000000000000000";
                }

                return TR();
            },
            cancel);

            // 一時メモリサイズの計算
            int tmpBufMemSize = 0;
            foreach (var chunk in chunkList)
            {
                tmpBufMemSize += chunk.DestData.Length;
                tmpBufMemSize += Consts.SecureCompress.BlockSize;
            }
            if (FirstWriteFlag == false)
            {
                tmpBufMemSize += Consts.SecureCompress.BlockSize * 2;
            }

            MemoryBuffer<byte> tmpBuf = new MemoryBuffer<byte>(EnsureSpecial.Yes, tmpBufMemSize);

            if (FirstWriteFlag == false)
            {
                // 1 個目なのでファイル全体のヘッダ (FirstHeader) を書き込み
                this.FirstHeader.Hostname = Env.DnsFqdnHostName;
                this.FirstHeader.TimeStamp = DtOffsetNow;

                var headerData = HeaderToData(Consts.SecureCompress.SecureCompressFirstHeader_Str, this.FirstHeader);

                // ヘッダは、万一の破損に備えて 2 回書く
                tmpBuf.Write(headerData);
                tmpBuf.Write(headerData);

                FirstWriteFlag = true;
            }

            // チャンクを書き込み
            foreach (var chunk in chunkList)
            {
                long now = Time.Tick64;

                // チャンクヘッダを書き込み
                SecureCompressBlockHeader h = new SecureCompressBlockHeader
                {
                    DestDataSize = chunk.DestData.Length,
                    DestDataSizeWithoutPadding = chunk.DestWithoutPaddingSize,
                    SrcDataPosition = chunk.SrcPosition,
                    SrcDataLength = chunk.SrcData.Length,
                    SrcSha1 = chunk.SrcSha1,
                    DestSha1 = chunk.DestSha1,
                    ChunkIndex = CurrentNumChunks,
                    IsAllZero = chunk.IsAllZero,
                    IsEoF = false,
                    TimeStamp = DtOffsetNow,
                };

                bool skip = this.IsLastChunkAllZero && chunk.IsAllZero; // 内容がゼロで、1 つ前の内容もゼロだった場合はスキップする

                this.IsLastChunkAllZero = chunk.IsAllZero;

                if (skip)
                {
                    if (this.LastChunkWrittenTick == 0 || (LastChunkWrittenTick + this.Options.KeepAliveMsecs) <= now)
                    {
                        this.LastChunkWrittenTick = now;

                        skip = false;
                    }
                }

                if (skip == false)
                {
                    var header = HeaderToData(Consts.SecureCompress.SecureCompressBlockHeader_Str, h);

                    tmpBuf.Write(header);

                    if (chunk.IsAllZero == false)
                    {
                        tmpBuf.Write(chunk.DestData);
                    }

                    this.LastChunkWrittenTick = now;
                }

                CurrentNumChunks++;

                CurrentFinalHeader.ChunkCount++;
            }

            await this.AppendToDestBufferAsync(tmpBuf, cancel);

            // バッファをクリア
            this.CurrentSrcDataBuffer.SetLength(0);
        }
        catch (Exception ex)
        {
            this.LastException = ex;
            throw;
        }
    }

    public async Task AppendToDestBufferAsync(ReadOnlyMemory<byte> data, CancellationToken cancel = default)
    {
        this.DestHash_Sha1.Write(data);

        CurrentFinalHeader.DestPhysicalSize += data.Length;

        await this.DestStream.WriteAsync(data, cancel);
    }

    // 本クラスの呼び出し元が書き込み要求をしたときに、このメソッドが呼ばれる
    // メモリ上の CurrentSrcDataBuffer が一杯になるまで、呼び出し元が書き込み要求をしたデータを追記していく
    // 一杯になったら、ProcessAndSendBufferedDataAsync を呼び出す
    protected override async ValueTask WriteImplAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancel = default)
    {
        if (buffer.IsEmpty)
        {
            return;
        }

        while (buffer.Length >= 1)
        {
            int remainBufSize = this.CurrentSrcDataBufferMaxSize - this.CurrentSrcDataBuffer.Length;

            if (remainBufSize >= 1)
            {
                int sz = Math.Min(buffer.Length, remainBufSize);

                var part = buffer._WalkRead(sz);

                this.CurrentSrcDataBuffer.Write(part);

                this.SrcHash_Sha1.Write(part);
            }

            if (this.CurrentSrcDataBuffer.Length >= this.CurrentSrcDataBufferMaxSize)
            {
                await ProcessAndSendBufferedDataAsync(cancel);
            }
        }
    }

    protected override Task FlushImplAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    Once FinalizedFlag;

    SecureCompressFinalHeader? RetHeaderCache = null;

    public async Task<SecureCompressFinalHeader> FinalizeAsync(CancellationToken cancel = default)
    {
        if (LastException != null)
        {
            LastException._ReThrow();
        }

        try
        {
            if (FinalizedFlag.IsFirstCall())
            {
                // 必ず 1 回は呼び出す
                await ProcessAndSendBufferedDataAsync(cancel);

                MemoryBuffer<byte> tmpBuf = new MemoryBuffer<byte>();

                // 最後のチャンクヘッダを必ず書き込む
                SecureCompressBlockHeader lastBlockHeader = new SecureCompressBlockHeader
                {
                    DestDataSize = 0,
                    DestDataSizeWithoutPadding = 0,
                    SrcDataPosition = this.CurrentSrcDataPosition,
                    SrcDataLength = 0,
                    SrcSha1 = "0000000000000000000000000000000000000000",
                    DestSha1 = "0000000000000000000000000000000000000000",
                    ChunkIndex = CurrentNumChunks,
                    IsAllZero = false,
                    IsEoF = true,
                    TimeStamp = DtOffsetNow,
                };

                var headerData1 = HeaderToData(Consts.SecureCompress.SecureCompressBlockHeader_Str, lastBlockHeader);

                tmpBuf.Write(headerData1);

                CurrentNumChunks++;

                CurrentFinalHeader.ChunkCount++;

                // 末尾ヘッダの作成
                SecureCompressFinalHeader finalHeader = this.CurrentFinalHeader;
                finalHeader.CopyOfFirstHeader = this.FirstHeader;

                finalHeader.SrcSha1 = this.SrcHash_Sha1.GetFinalHash()._GetHexString();

                finalHeader.DestSha1 = this.DestHash_Sha1.GetFinalHash()._GetHexString();

                finalHeader.TimeStamp = DtOffsetNow;

                var headerData2 = HeaderToData(Consts.SecureCompress.SecureCompressFinalHeader_Str, finalHeader);

                //h._PrintAsJson();

                // 末尾ヘッダも予備のため 2 回書く
                tmpBuf.Write(headerData2);
                tmpBuf.Write(headerData2);

                await this.AppendToDestBufferAsync(tmpBuf, cancel);

                RetHeaderCache =finalHeader._CloneDeep();
            }

            return RetHeaderCache ?? new SecureCompressFinalHeader();
        }
        catch (Exception ex)
        {
            this.LastException = ex;
            throw;
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
    async Task DisposeInternalAsync()
    {
        try
        {
            await this.FinalizeAsync();
        }
        finally
        {
            if (this.AutoDispose)
            {
                await this.DestStream._DisposeSafeAsync();
            }
        }
    }

    protected override long GetLengthImpl()
    {
        throw new NotImplementedException();
    }

    protected override void SetLengthImpl(long length)
    {
        throw new NotImplementedException();
    }

    protected override long GetPositionImpl()
    {
        throw new NotImplementedException();
    }

    protected override void SetPositionImpl(long position)
    {
        throw new NotImplementedException();
    }

    protected override long SeekImpl(long offset, SeekOrigin origin)
    {
        throw new NotImplementedException();
    }

    protected override ValueTask<int> ReadImplAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }
}

public static class SecureCompresTest
{
    public static async Task DoTestAsync()
    {
        // テストデータ生成元: Test_230927_generate_securecompress_test_data

        var normal = await SimpleHttpDownloader.DownloadAsync(@"https://lts.dn.ipantt.net/d/230928_001_51962/1.dat",
            options: new WebApiOptions(new WebApiSettings { AllowAutoRedirect = true, }));

        Dbg.TestTrue(Secure.HashSHA1(normal.Data)._GetHexString() == "D8F45E54F82858ECC1831E7C00DD96D54ED97CE9");

        var broken = await SimpleHttpDownloader.DownloadAsync(@"https://lts.dn.ipantt.net/d/230928_001_51962/1_broken.dat",
            options: new WebApiOptions(new WebApiSettings { AllowAutoRedirect = true, }));

        Dbg.TestTrue(Secure.HashSHA1(broken.Data)._GetHexString() == "9C63E340C42DE44D1C801AC728DA6F6DACE86DDE");

        MemoryStream ms1 = new MemoryStream();
        await using SecureCompressDecoder dec1 = new SecureCompressDecoder(ms1, new SecureCompressOptions("", true, "microsoft", true));
        await dec1.WriteAsync(normal.Data);
        await dec1.FlushAsync();
        await dec1.FinalizeAsync();

        MemoryStream ms2 = new MemoryStream();
        await using SecureCompressDecoder dec2 = new SecureCompressDecoder(ms2, new SecureCompressOptions("", true, "microsoft", true));
        await dec2.WriteAsync(broken.Data);
        await dec2.FlushAsync();
        await dec2.FinalizeAsync();

        Dbg.TestTrue(Secure.HashSHA1(ms1.ToArray())._GetHexString() == "C0F31F265AC69011A0545563476DF9AB3DD797D7");

        Dbg.TestTrue(Secure.HashSHA1(ms2.ToArray())._GetHexString() == "C79FD716B3720E582533524B0CC44943E416BF4B");

        Dbg.TestTrue(dec2.NumError == 8);
        Dbg.TestTrue(dec2.NumWarning == 9);
    }
}

#endif

