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
using System.IO;

using IPA.Cores.Basic;
using IPA.Cores.Basic.Legacy;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

namespace IPA.Cores.Basic;

// FIFO
public class Fifo
{


    byte[] InternalBuffer;
    int Position;
    public int Size { get; private set; }
    public byte[] Data => this.InternalBuffer;
    public int DataOffset => this.Position;
    public int PhysicalSize => InternalBuffer.Length;

    int ReallocMemSize;
    public const int FifoInitMemSize = 4096;
    public const int FifoReallocMemSize = 65536;
    public const int FifoReallocMemSizeSmall = 65536;

    public long TotalWriteSize { get; private set; } = 0;
    public long TotalReadSize { get; private set; } = 0;

    public Fifo(int reallocMemSize = 0)
    {
        if (reallocMemSize == 0)
        {
            reallocMemSize = FifoReallocMemSize;
        }

        this.Size = this.Position = 0;
        this.ReallocMemSize = reallocMemSize;

        this.InternalBuffer = new byte[FifoInitMemSize];
    }

    public void Write(Buf buf)
    {
        Write(buf.ByteData);
    }
    public void Write(byte[] src)
    {
        Write(src, src.Length);
    }
    public void SkipWrite(int size)
    {
        Write(null, size);
    }
    public void Write(byte[]? src, int size)
    {
        Write(src, 0, size);
    }
    public void Write(byte[]? src, int offset, int size)
    {
        checked
        {
            int i, needSize;
            bool reallocFlag;

            i = this.Size;
            this.Size += size;
            needSize = this.Position + this.Size;
            reallocFlag = false;

            int memSize = InternalBuffer.Length;
            while (needSize > memSize)
            {
                memSize = Math.Max(memSize, FifoInitMemSize) * 3;
                reallocFlag = true;
            }

            if (reallocFlag)
            {
                byte[] newPtr = new byte[memSize];
                Util.CopyByte(newPtr, 0, this.InternalBuffer, 0, this.InternalBuffer.Length);
                this.InternalBuffer = newPtr;
            }

            if (src != null)
            {
                Util.CopyByte(this.InternalBuffer, this.Position + i, src, offset, size);
            }

            TotalWriteSize += size;
        }
    }

    public byte[] Read()
    {
        return Read(this.Size);
    }
    public void ReadToBuf(Buf buf, int size)
    {
        byte[] data = Read(size);

        buf.Write(data);
    }
    public Buf ReadToBuf(int size)
    {
        byte[] data = Read(size);

        return new Buf(data);
    }
    public byte[] Read(int size)
    {
        byte[] ret = new byte[size];
        int read_size = Read(ret);
        Array.Resize<byte>(ref ret, read_size);

        return ret;
    }
    public int Read(byte[] dst)
    {
        return Read(dst, dst.Length);
    }
    public int SkipRead(int size)
    {
        return Read(null, size);
    }
    public int Read(byte[]? dst, int size)
    {
        return Read(dst, 0, size);
    }
    public int Read(byte[]? dst, int offset, int size)
    {
        checked
        {
            int readSize;

            readSize = Math.Min(size, this.Size);
            if (readSize == 0)
            {
                return 0;
            }
            if (dst != null)
            {
                Util.CopyByte(dst, offset, this.InternalBuffer, this.Position, size);
            }
            this.Position += readSize;
            this.Size -= readSize;

            if (this.Size == 0)
            {
                this.Position = 0;
            }

            // メモリの詰め直し
            if (this.Position >= FifoInitMemSize &&
                this.InternalBuffer.Length >= this.ReallocMemSize &&
                (this.InternalBuffer.Length / 2) > this.Size)
            {
                byte[] newPtr;
                int newSize;

                newSize = Math.Max(this.InternalBuffer.Length / 2, FifoInitMemSize);
                newPtr = new byte[newSize];
                Util.CopyByte(newPtr, 0, this.InternalBuffer, this.Position, this.Size);

                this.InternalBuffer = newPtr;

                this.Position = 0;
            }

            TotalReadSize += readSize;

            return readSize;
        }
    }

    public Span<byte> Span => this.Data.AsSpan(this.Position, this.Size);
}

public class Fifo<T>
{
    public T[] PhysicalData { get; private set; }
    public int Size { get; private set; }
    public int Position { get; private set; }
    public int PhysicalSize { get => PhysicalData.Length; }

    public const int FifoInitSize = 32;
    public const int FifoReAllocSize = 1024;
    public const int FifoReAllocSizeSmall = 1024;

    public int ReAllocMemSize { get; }

    public bool ClearUnused { get; }

    public Fifo(int reAllocMemSize = FifoReAllocSize, bool clearUnused = false)
    {
        ReAllocMemSize = reAllocMemSize;
        Size = Position = 0;
        PhysicalData = new T[FifoInitSize];
        ClearUnused = clearUnused;
    }

    public void Clear()
    {
        Size = Position = 0;
    }

    public void Write(ReadOnlySpan<T> data) => WriteInternal(data, data.Length);

    public void Write(T data) => WriteInternal(data);

    public void WriteSkip(int length) => WriteInternal(null, length);

    void WriteInternal(ReadOnlySpan<T> src, int size)
    {
        checked
        {
            int oldSize, newSize, needSize;

            oldSize = Size;
            newSize = oldSize + size;
            needSize = Position + newSize;

            bool reallocFlag = false;
            int newPhysicalSize = PhysicalData.Length;
            while (needSize > newPhysicalSize)
            {
                newPhysicalSize = Math.Max(newPhysicalSize, FifoInitSize) * 3;
                reallocFlag = true;
            }

            if (reallocFlag)
                PhysicalData = PhysicalData._ReAlloc(newPhysicalSize);

            if (src != null)
                src.CopyTo(PhysicalData.AsSpan().Slice(Position + oldSize));

            Size = newSize;
        }
    }

    void WriteInternal(T src)
    {
        checked
        {
            int oldSize, newSize, needSize;

            oldSize = Size;
            newSize = oldSize + 1;
            needSize = Position + newSize;

            bool reallocFlag = false;
            int newPhysicalSize = PhysicalData.Length;
            while (needSize > newPhysicalSize)
            {
                newPhysicalSize = Math.Max(newPhysicalSize, FifoInitSize) * 3;
                reallocFlag = true;
            }

            if (reallocFlag)
                PhysicalData = PhysicalData._ReAlloc(newPhysicalSize);

            if (src != null)
                PhysicalData[Position + oldSize] = src;

            Size = newSize;
        }
    }


    public int Read(Span<T> dest) => ReadInternal(dest, dest.Length);

    public T[] Read(int size)
    {
        int readSize = Math.Min(this.Size, size);
        T[] ret = new T[readSize];
        Read(ret);
        return ret;
    }

    public T[] Read() => Read(this.Size);

    int ReadInternal(Span<T> dest, int size)
    {
        checked
        {
            int readSize;

            readSize = Math.Min(size, Size);
            if (readSize == 0)
            {
                return 0;
            }
            if (dest != null)
            {
                PhysicalData.AsSpan(this.Position, size).CopyTo(dest);
            }
            Position += readSize;
            Size -= readSize;

            if (Size == 0)
            {
                Position = 0;
            }
            else
            {
                if (this.Position >= FifoInitSize &&
                    this.PhysicalData.Length >= this.ReAllocMemSize &&
                    (this.PhysicalData.Length / 2) > this.Size)
                {
                    int newPhysicalSize;

                    newPhysicalSize = Math.Max(this.PhysicalData.Length / 2, FifoInitSize);

                    T[] newArray = new T[newPhysicalSize];
                    this.PhysicalData.AsSpan(this.Position, this.Size).CopyTo(newArray);
                    this.PhysicalData = newArray;

                    this.Position = 0;
                }
                else
                {
                    if (this.ClearUnused)
                    {
                        Array.Clear(this.PhysicalData, Position - readSize, readSize);
                    }
                }
            }

            return readSize;
        }
    }

    public Span<T> Span { get => this.PhysicalData.AsSpan(this.Position, this.Size); }
}

// バッファ
public class Buf
{
    MemoryStream InternalBuffer;

    // コンストラクタ
    public Buf() : this(new byte[0]) { }
    public Buf(byte[] data)
    {
        InternalBuffer = new MemoryStream();
        Write(data);
        SeekToBegin();
    }

    // クリア
    public void Clear()
    {
        InternalBuffer.SetLength(0);
    }

    // 書き込み
    public void WriteByte(byte data)
    {
        InternalBuffer.WriteByte(data);
    }
    public void Write(byte[] data)
    {
        InternalBuffer.Write(data, 0, data.Length);
    }
    public void Write(byte[] data, int pos, int len)
    {
        InternalBuffer.Write(data, pos, len);
    }

    // サイズの取得
    public uint Size => (uint)InternalBuffer.Length;

    // 現在位置の取得
    public uint Pos => (uint)InternalBuffer.Position;

    // バイト列の取得
    public byte[] ByteData => InternalBuffer.ToArray();

    // インデクサによる編集
    public byte this[uint i]
    {
        get
        {
            return InternalBuffer.GetBuffer()[i];
        }

        set
        {
            InternalBuffer.GetBuffer()[i] = value;
        }
    }

    // 読み込み
    public byte[] Read()
    {
        return Read(this.Size);
    }
    public byte[] Read(uint size)
    {
        byte[] tmp = new byte[size];
        int i = InternalBuffer.Read(tmp, 0, (int)size);

        byte[] ret = new byte[i];
        Array.Copy(tmp, 0, ret, 0, i);

        return ret;
    }
    public byte ReadByte()
    {
        byte[] a = Read(1);

        return a[0];
    }

    // シーク
    public void SeekToBegin()
    {
        Seek(0);
    }
    public void SeekToEnd()
    {
        Seek(0, SeekOrigin.End);
    }
    public void Seek(uint offset)
    {
        Seek(offset, SeekOrigin.Begin);
    }
    public void Seek(uint offset, SeekOrigin mode)
    {
        InternalBuffer.Seek(offset, mode);
    }

    // short 型の読み出し
    public ushort ReadShort()
    {
        byte[] data = Read(2);
        if (data.Length != 2)
        {
            return 0;
        }
        if (Env.IsLittleEndian)
        {
            Array.Reverse(data);
        }
        return BitConverter.ToUInt16(data, 0);
    }
    public ushort RawReadShort()
    {
        byte[] data = Read(2);
        if (data.Length != 2)
        {
            return 0;
        }
        return BitConverter.ToUInt16(data, 0);
    }

    // int 型の読み出し
    public uint ReadInt()
    {
        byte[] data = Read(4);
        if (data.Length != 4)
        {
            return 0;
        }
        if (Env.IsLittleEndian)
        {
            Array.Reverse(data);
        }
        return BitConverter.ToUInt32(data, 0);
    }
    public uint RawReadInt()
    {
        byte[] data = Read(4);
        if (data.Length != 4)
        {
            return 0;
        }
        return BitConverter.ToUInt32(data, 0);
    }

    // int64 型の読み出し
    public ulong ReadInt64()
    {
        byte[] data = Read(8);
        if (data.Length != 8)
        {
            return 0;
        }
        if (Env.IsLittleEndian)
        {
            Array.Reverse(data);
        }
        return BitConverter.ToUInt64(data, 0);
    }
    public ulong RawReadInt64()
    {
        byte[] data = Read(8);
        if (data.Length != 8)
        {
            return 0;
        }
        return BitConverter.ToUInt64(data, 0);
    }

    // ANSI 文字列の読み出し
    public string ReadStr()
    {
        return ReadStr(false);
    }
    public string ReadStr(bool includeNull)
    {
        uint len = ReadInt();
        byte[] data = Read(len - (uint)(includeNull ? 1 : 0));
        return Str.ShiftJisEncoding.GetString(data);
    }

    public string ReadAsciiStr()
    {
        return ReadAsciiStr(false);
    }
    public string ReadAsciiStr(bool includeNull)
    {
        uint len = ReadInt();
        byte[] data = Read(len - (uint)(includeNull ? 1 : 0));
        return Str.AsciiEncoding.GetString(data);
    }

    // Unicode 文字列の読み出し
    public string? ReadUniStr()
    {
        return ReadUniStr(false);
    }
    public string? ReadUniStr(bool includeNull)
    {
        uint len = ReadInt();
        if (len == 0)
        {
            return null;
        }
        byte[] data = Read(len - (uint)(includeNull ? 2 : 0));
        return Str.Utf8Encoding.GetString(data);
    }

    // short 型の書き込み
    public void WriteShort(ushort shortValue)
    {
        byte[] data = BitConverter.GetBytes(shortValue);
        if (Env.IsLittleEndian)
        {
            Array.Reverse(data);
        }
        Write(data);
    }
    public void RawWriteShort(ushort shortValue)
    {
        byte[] data = BitConverter.GetBytes(shortValue);
        Write(data);
    }

    // int 型の書き込み
    public void WriteInt(uint intValue)
    {
        byte[] data = BitConverter.GetBytes(intValue);
        if (Env.IsLittleEndian)
        {
            Array.Reverse(data);
        }
        Write(data);
    }
    public void RawWriteInt(uint intValue)
    {
        byte[] data = BitConverter.GetBytes(intValue);
        Write(data);
    }

    // int64 型の書き込み
    public void WriteInt64(ulong int64Value)
    {
        byte[] data = BitConverter.GetBytes(int64Value);
        if (Env.IsLittleEndian)
        {
            Array.Reverse(data);
        }
        Write(data);
    }
    public void RawWriteInt64(ulong int64Value)
    {
        byte[] data = BitConverter.GetBytes(int64Value);
        Write(data);
    }

    // 1 行を文字列として読み込み
    public string? ReadNextLineAsString()
    {
        return ReadNextLineAsString(Str.Utf8Encoding);
    }
    public string? ReadNextLineAsString(Encoding encoding)
    {
        byte[]? ret = ReadNextLineAsData();
        if (ret == null)
        {
            return null;
        }

        return encoding.GetString(ret);
    }

    // 1 行をバイト列として読み込み
    public byte[]? ReadNextLineAsData()
    {
        if (this.Size <= this.Pos)
        {
            // 最後まで読んだ
            return null;
        }

        // 現在位置を記憶
        long pos = InternalBuffer.Position;

        // 13 または 10 を探索
        long i;
        byte[] tmp = new byte[1];
        for (i = pos; i < InternalBuffer.Length; i++)
        {
            InternalBuffer.Read(tmp, 0, 1);

            if (tmp[0] == 13 || tmp[0] == 10)
            {
                if (tmp[0] == 13)
                {
                    if ((i + 2) < InternalBuffer.Length)
                    {
                        i++;
                    }
                }

                break;
            }
        }

        long len = i - pos;

        InternalBuffer.Position = pos;

        // len だけ読み込む
        byte[] ret = Read((uint)((int)len));

        // シークする
        try
        {
            Seek(1, SeekOrigin.Current);
        }
        catch
        {
        }

        if (ret.Length >= 1 && ret[ret.Length - 1] == 13)
        {
            Array.Resize<byte>(ref ret, ret.Length - 1);
        }

        return ret;
    }

    // ANSI 文字列の書き込み
    public void WriteStr(string strValue)
    {
        WriteStr(strValue, false);
    }
    public void WriteStr(string strValue, bool includeNull)
    {
        byte[] data = Str.ShiftJisEncoding.GetBytes(strValue);
        WriteInt((uint)data.Length + (uint)(includeNull ? 1 : 0));
        Write(data);
    }
    public void WriteAsciiStr(string strValue)
    {
        WriteAsciiStr(strValue, false);
    }
    public void WriteAsciiStr(string strValue, bool includeNull)
    {
        byte[] data = Str.AsciiEncoding.GetBytes(strValue);
        WriteInt((uint)data.Length + (uint)(includeNull ? 1 : 0));
        Write(data);
    }

    // Unicode 文字列の書き込み
    public void WriteUniStr(string strValue)
    {
        WriteUniStr(strValue, false);
    }
    public void WriteUniStr(string strValue, bool includeNull)
    {
        byte[] data = Str.Utf8Encoding.GetBytes(strValue);
        WriteInt((uint)data.Length + (uint)(includeNull ? 2 : 0));
        Write(data);
    }

    // ファイルから読み込み
    public static Buf ReadFromFile(string filename)
    {
        return new Buf(IO.ReadFile(filename));
    }

    // バッファから読み込み (ハッシュを調べる)
    public static Buf ReadFromBufWithHash(Buf buf)
    {
        byte[] filedata = buf.ByteData;
        if (filedata.Length < 20)
        {
            throw new ApplicationException("filedata.Length < 20");
        }
        byte[] hash = Util.CopyByte(filedata, 0, 20);
        byte[] data = Util.CopyByte(filedata, 20);
        byte[] hash2 = Secure.HashSHA1(data);

        if (Util.MemEquals(hash, hash2) == false)
        {
            throw new ApplicationException("hash mismatch");
        }

        return new Buf(data);
    }

    // ファイルから読み込み (ハッシュ調べる)
    public static Buf ReadFromFileWithHash(string filename)
    {
        byte[] filedata = IO.ReadFile(filename);
        if (filedata.Length < 20)
        {
            throw new ApplicationException("filedata.Length < 20");
        }
        byte[] hash = Util.CopyByte(filedata, 0, 20);
        byte[] data = Util.CopyByte(filedata, 20);
        byte[] hash2 = Secure.HashSHA1(data);

        if (Util.MemEquals(hash, hash2) == false)
        {
            throw new ApplicationException("hash mismatch");
        }

        return new Buf(data);
    }

    // ファイルに書き込み
    public void WriteToFile(string filename)
    {
        IO.SaveFile(filename, this.ByteData);
    }

    // ファイルに書き込み (ハッシュ付ける)
    public void WriteToFileWithHash(string filename)
    {
        byte[] data = this.ByteData;
        byte[] hash = Secure.HashSHA1(data);

        Buf b = new Buf();
        b.Write(hash);
        b.Write(data);
        b.WriteToFile(filename);
    }

    // ストリームから読み込み
    public static Buf ReadFromStream(Stream st)
    {
        Buf ret = new Buf();
        int size = 32767;

        while (true)
        {
            byte[] tmp = new byte[size];
            int i = st.Read(tmp, 0, tmp.Length);

            if (i <= 0)
            {
                break;
            }

            Array.Resize<byte>(ref tmp, i);

            ret.Write(tmp);
        }

        ret.SeekToBegin();

        return ret;
    }
}
