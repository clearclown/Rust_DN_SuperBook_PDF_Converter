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
// ZIP ファイルフォーマットヘッダ定義

#if true

#pragma warning disable CA2235 // Mark all non-serializable fields

using System;
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

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using System.Runtime.CompilerServices;

namespace IPA.Cores.Basic;

// ZIP ファイル構造の参考ドキュメント類
// https://pkware.cachefly.net/webdocs/casestudies/APPNOTE.TXT
// https://ja.wikipedia.org/wiki/ZIP_(%E3%83%95%E3%82%A1%E3%82%A4%E3%83%AB%E3%83%95%E3%82%A9%E3%83%BC%E3%83%9E%E3%83%83%E3%83%88)
// https://atrsas.exblog.jp/9661531/
// https://atrsas.exblog.jp/9671353/
// https://atrsas.exblog.jp/9675346/
// https://atrsas.exblog.jp/9677162/
// https://atrsas.exblog.jp/9683786/
// https://www.tnksoft.com/reading/zipfile/index.php?p=cryptzip
//
// 2019-08-26 メモ
// 現状は ZIP Version 4.5 に対応している。
// それ以降は対応していない。

public static class ZipConsts
{
    public const uint LocalFileHeaderSignature = 0x04034b50;
    public const uint DataDescriptorSignature = 0x08074b50;
    public const uint CentralFileHeaderSignature = 0x02014B50;
    public const uint EndOfCentralDirectorySignature = 0x06054b50;
    public const uint Zip64EndOfCentralDirectorySignature = 0x06064b50;
    public const uint Zip64EndOfCentralDirectoryLocatorSignature = 0x07064b50;

    public const int MaxFileNameSize = 65535;
}

// 4.4.4 general purpose bit flag: (2 bytes)
[Flags]
public enum ZipGeneralPurposeFlags : ushort
{
    None = 0,
    Encrypted = 1,                  // bit 0
    Bit1 = 2,                       // bit 1
    Bit2 = 4,                       // bit 2
    UseDataDescriptor = 8,          // bit 3
    Bit4 = 16,                      // bit 4
    CompressedPatchedData = 32,     // bit 5
    StrongEncryption = 64,          // bit 6
    Reserved_0 = 128,               // bit 7
    Reserved_1 = 256,               // bit 8
    Reserved_2 = 512,               // bit 9
    Reserved_3 = 1024,              // bit 10
    Utf8 = 2048,                    // bit 11
    Reserved_4 = 4096,              // bit 12
    EncryptCentralDirectory = 8192, // bit 13
}

// 4.4.5 compression method: (2 bytes)
[Flags]
public enum ZipCompressionMethod : ushort
{
    Raw = 0,
    Deflated = 8,
}

// 4.4.3 version needed to extract (2 bytes)
[Flags]
public enum ZipFileSystemType : byte
{
    MsDos = 0,
    UNIX = 1,
    Ntfs = 10,
    Darwin = 19,
}

// 4.4.2 version made by (2 bytes)
[Flags]
public enum ZipFileVersion : byte
{
    Ver2_0 = 20,
    Ver4_5 = 45,
}

// ローカルファイルヘッダ
// 4.3.7  Local file header
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct ZipLocalFileHeader
{
    public uint Signature;
    public ZipFileVersion NeedVersion;
    public byte Reserved;
    public ZipGeneralPurposeFlags GeneralPurposeFlag;
    public ZipCompressionMethod CompressionMethod;
    public ushort LastModFileTime;
    public ushort LastModFileDate;
    public uint Crc32;
    public uint CompressedSize;
    public uint UncompressedSize;
    public ushort FileNameSize;
    public ushort ExtraFieldSize;

    // Followed by:
    // [file name (variable size)]
    // [extra field (variable size)]
}

// 4.5.2 The current Header ID mappings defined by PKWARE
[Flags]
public enum ZipExtHeaderIDs : ushort
{
    None = 0,
    Zip64 = 0x0001,
}

// データディスクリプタ
// 4.3.9  Data descriptor
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct ZipDataDescriptor
{
    public uint Signature;
    public uint Crc32;
    public uint CompressedSize;
    public uint UncompressedSize;
}

// セントラルディレクトリファイルヘッダ
// 4.3.12  Central directory structure
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct ZipCentralFileHeader
{
    public uint Signature;
    public ZipFileVersion MadeVersion;
    public ZipFileSystemType MadeFileSystemType;
    public ZipFileVersion NeedVersion;
    public byte Reserved;
    public ZipGeneralPurposeFlags GeneralPurposeFlag;
    public ZipCompressionMethod CompressionMethod;
    public ushort LastModFileTime;
    public ushort LastModFileDate;
    public uint Crc32;
    public uint CompressedSize;
    public uint UncompressedSize;
    public ushort FileNameSize;
    public ushort ExtraFieldSize;
    public ushort FileCommentSize;
    public ushort DiskNumberStart;
    public ushort InternalFileAttributes;
    public uint ExternalFileAttributes;
    public uint RelativeOffsetOfLocalHeader;

    // Followed by:
    // [file name(variable size)]
    // [extra field(variable size)]
    // [file comment(variable size)]
}

// 4.5.3 Zip64 Extended Information Extra Field (0x0001)
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct ZipExtZip64Field
{
    public ulong UncompressedSize;
    public ulong CompressedSize;
    public ulong RelativeOffsetOfLocalHeader;
    public uint DiskNumberStart;
}

// エンドオブセントラルディレクトリレコード
// 4.3.16  End of central directory record
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct ZipEndOfCentralDirectoryRecord
{
    public uint Signature;
    public ushort NumberOfThisDisk;
    public ushort DiskNumberStart;
    public ushort NumberOfCentralDirectoryOnThisDisk;
    public ushort TotalNumberOfCentralDirectory;
    public uint SizeOfCentralDirectory;
    public uint OffsetStartCentralDirectory;
    public ushort CommentLength;

    // Followed by:
    // [.ZIP file comment (variable size)]
}

// ZIP64 エンドオブセントラルディレクトリレコード
// 4.3.14  Zip64 end of central directory record
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct Zip64EndOfCentralDirectoryRecord
{
    public uint Signature;
    public ulong SizeOfZip64EndOfCentralDirectoryRecord;        // 4.3.14.1 Size = SizeOfFixedFields + SizeOfVariableData - 12.
    public ZipFileVersion MadeVersion;
    public ZipFileSystemType MadeFileSystemType;
    public ZipFileVersion NeedVersion;
    public byte Reserved;
    public uint NumberOfThisDisk;
    public uint DiskNumberStart;
    public ulong TotalNumberOfCentralDirectory;
    public ulong TotalNumberOfEntriesOnCentralDirectory;
    public ulong SizeOfCentralDirectory;
    public ulong OffsetStartCentralDirectory;

    // Followed by:
    // [zip64 extensible data sector    (variable size)]
}

// ZIP64 エンドオブセントラルディレクトリロケータ
// 4.3.15 Zip64 end of central directory locator
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct Zip64EndOfCentralDirectoryLocator
{
    public uint Signature;
    public uint NumberOfThisDisk;
    public ulong OffsetStartZip64CentralDirectoryRecord;
    public uint TotalNumberOfDisk;
}

public class ZipExtraFieldsList : KeyValueList<ZipExtHeaderIDs, ReadOnlyMemory<byte>>
{
    public void Add<T>(ZipExtHeaderIDs id, in T data, int dataSize = DefaultSize)
        where T : unmanaged
    {
        ReadOnlyMemory<byte> a = Unsafe.AsRef(in data)._CopyToMemory();

        base.Add(id, a);
    }

    public Memory<byte> ToMemory()
    {
        checked
        {
            MemoryBuffer<byte> buf = new MemoryBuffer<byte>();

            foreach (var item in this)
            {
                // Header
                buf.WriteUInt16((ushort)item.Key, littleEndian: true);
                buf.WriteUInt16((ushort)item.Value.Length, littleEndian: true);

                // Data
                buf.Write(item.Value);
            }

            return buf.Memory;
        }
    }
}

// ZIP ファイル用 CRC32 計算構造体
public unsafe struct ZipCrc32
{
    const int TableSize = 256;

    static readonly uint* Table;

    static ZipCrc32()
    {
        // Table のメモリは Unmanaged メモリを初期化時に動的に確保し、その後一生解放しない。
        // 厳密にはメモリリークであるが、少量かつ 1 回のみであるから、問題無いのである。
        Table = (uint*)MemoryHelper.AllocUnmanagedMemory(sizeof(int) * 256);

        uint poly = 0xEDB88320;
        uint u, i, j;

        for (i = 0; i < 256; i++)
        {
            u = i;

            for (j = 0; j < 8; j++)
            {
                if ((u & 0x1) != 0)
                {
                    u = (u >> 1) ^ poly;
                }
                else
                {
                    u >>= 1;
                }
            }

            Table[i] = u;
        }

    }

    uint CurrentInternal;
    bool IsNotFirst;

    public uint Value
    {
        [MethodImpl(Inline)]
        get => GetValue();
    }

    // CRC32 計算対象データの追加
    [MethodImpl(Inline)]
    public void Append(ReadOnlySpan<byte> data)
    {
        if (IsNotFirst == false)
        {
            // 初回初期化
            IsNotFirst = true;
            CurrentInternal = 0xffffffff;
        }

        if (data.Length == 0) return;

        uint ret = CurrentInternal;
        int len = data.Length;
        for (int i = 0; i < len; i++)
        {
            ret = (ret >> 8) ^ Table[(int)(data[i] ^ (ret & 0xff))];
        }
        CurrentInternal = ret;
    }

    [MethodImpl(Inline)]
    public uint GetValue()
    {
        if (IsNotFirst == false)
        {
            // 初回初期化
            IsNotFirst = true;
            CurrentInternal = 0xffffffff;
        }

        return ~this.CurrentInternal;
    }

    // 一発計算
    [MethodImpl(Inline)]
    public static uint Calc(ReadOnlySpan<byte> data)
    {
        ZipCrc32 c = new ZipCrc32();
        c.Append(data);
        return c.Value;
    }

    // ZIP 暗号化に使用する CRC
    [MethodImpl(Inline)]
    public static uint CalcCrc32ForZipEncryption(uint n1, byte n2)
    {
        return Table[(int)((n1 ^ n2) & 0xFF)] ^ (n1 >> 8);
    }
}

// ZIP 暗号化ストリーム
public class ZipEncryptionStream : WrapperStreamImplBase
{
    readonly ZipEncryption Enc;

    public Exception? Error { get; private set; } = null;

    readonly Memory<byte> InitialHeader;

    public ZipEncryptionStream(Stream baseStream, bool leaveStreamOpen, string password, byte byte11th) : base(baseStream, leaveStreamOpen, new StreamImplBaseOptions(false, true, false))
    {
        this.Enc = new ZipEncryption(password);

        // 最初の 12 バイトのダミーデータ (PKZIP のドキュメントではヘッダと呼ばれている) データを覚える
        InitialHeader = Secure.RandWithInchikiEntropySlow(12);

        InitialHeader.Span[11] = byte11th;
    }

    // 最初の 12 バイトのダミーデータ (PKZIP のドキュメントではヘッダと呼ばれている) を書き込む
    protected override async Task InitImplAsync(CancellationToken cancel = default)
    {
        await this.WriteAsync(this.InitialHeader, cancel);
    }

    public override bool DataAvailable => false;

    protected override async ValueTask WriteImplAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ThrowIfError();

        try
        {
            // データを暗号化いたします
            Memory<byte> tmp = new byte[buffer.Length];

            Enc.Encrypt(tmp.Span, buffer.Span);

            // 暗号化したデータを書き込みいたします
            await this.BaseStream.WriteAsync(tmp, cancellationToken);
        }
        catch (Exception ex)
        {
            this.Error = ex;
            throw;
        }
    }

    void ThrowIfError()
    {
        if (this.Error != null)
            throw this.Error;
    }

    protected override Task FlushImplAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfError();

        return BaseStream.FlushAsync(cancellationToken);
    }

    protected override long GetLengthImpl() => throw new NotImplementedException();
    protected override long GetPositionImpl() => throw new NotImplementedException();
    protected override ValueTask<int> ReadImplAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();
    protected override long SeekImpl(long offset, SeekOrigin origin) => throw new NotImplementedException();
    protected override void SetLengthImpl(long length) => throw new NotImplementedException();
    protected override void SetPositionImpl(long position) => throw new NotImplementedException();
}

// ZIP 暗号化ルーチン
public class ZipEncryption
{
    uint Key0 = 305419896;
    uint Key1 = 591751049;
    uint Key2 = 878082192;

    public ZipEncryption(string password)
    {
        password = password._NonNull();

        byte[] passwordData = password._GetBytes_UTF8();

        foreach (byte b in passwordData)
        {
            UpdateKeys(b);
        }
    }

    [MethodImpl(Inline)]
    void UpdateKeys(byte c)
    {
        Key0 = ZipCrc32.CalcCrc32ForZipEncryption(Key0, c);
        Key1 += (byte)Key0;
        Key1 = Key1 * 134775813 + 1;
        Key2 = ZipCrc32.CalcCrc32ForZipEncryption(Key2, (byte)(Key1 >> 24));
    }

    [MethodImpl(Inline)]
    byte GetNextXorByte()
    {
        uint temp = (ushort)((Key2 & 0xFFFF) | 2);
        return (byte)((temp * (temp ^ 1)) >> 8);
    }

    [MethodImpl(Inline)]
    public void Encrypt(Span<byte> dst, ReadOnlySpan<byte> src)
    {
        if (dst.Length != src.Length) throw new CoresException("dst.Length != src.Length");
        int len = src.Length;
        for (int i = 0; i < len; i++)
        {
            byte n = src[i];

            dst[i] = (byte)(n ^ GetNextXorByte());

            UpdateKeys(n);
        }
    }
}

#endif

