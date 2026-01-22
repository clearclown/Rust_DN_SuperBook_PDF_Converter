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
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

namespace IPA.Cores.Basic.Legacy;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct TarHeader
{
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 100)]
    public byte[] Name;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
    public byte[] Mode;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
    public byte[] UID;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
    public byte[] GID;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 12)]
    public byte[] Size;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 12)]
    public byte[] MTime;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
    public byte[] CheckSum;

    public byte TypeFlag;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 100)]
    public byte[] LinkName;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
    public byte[] Magic;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
    public byte[] Version;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
    public byte[] UName;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
    public byte[] GName;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
    public byte[] DevMajor;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
    public byte[] DevMinor;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 155)]
    public byte[] Prefix;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 12)]
    public byte[] Padding;

    public TarHeader(bool dummy)
    {
        this.Name = new byte[100];
        this.Mode = new byte[8];
        this.UID = new byte[8];
        this.GID = new byte[8];
        this.Size = new byte[12];
        this.MTime = new byte[12];
        this.CheckSum = new byte[8];
        this.LinkName = new byte[100];
        this.Magic = new byte[6];
        this.Version = new byte[2];
        this.UName = new byte[32];
        this.GName = new byte[32];
        this.DevMajor = new byte[8];
        this.DevMinor = new byte[8];
        this.Prefix = new byte[155];
        this.Padding = new byte[12];
        this.TypeFlag = 0;

        this.Version[0] = 0x20;
        this.Version[1] = 0x00;

        byte[] data = Str.ShiftJisEncoding.GetBytes("ustar ");
        Util.CopyByte(this.Magic, 0, data, 0, 6);
    }

    public void SetName(string name, Encoding encoding)
    {
        byte[] data = encoding.GetBytes(name);
        if (data.Length <= 100)
        {
            Util.CopyByte(this.Name, 0, data, 0, data.Length);
        }
        else
        {
            Util.CopyByte(this.Name, 0, data, 0, 100);
            Util.CopyByte(this.Prefix, 0, data, 100, data.Length - 100);
        }
    }

    public void SetMode(string str)
    {
        StrToByteArray(this.Mode, str);
    }

    public void SetUID(string str)
    {
        StrToByteArray(this.UID, str);
    }

    public void SetGID(string str)
    {
        StrToByteArray(this.GID, str);
    }

    public void SetSize(long size)
    {
        if (size >= 0x1FFFFFFFF || size < 0)
        {
            throw new InvalidDataException("size");
        }
        StrToByteArray(this.Size, Str.AppendZeroToNumString(Convert.ToString(size, 8), 11));
    }

    public void SetMTime(DateTime dt)
    {
        uint t = (uint)Util.DateTimeToUnixTime(dt.ToUniversalTime());

        StrToByteArray(this.MTime, Str.AppendZeroToNumString(Convert.ToString(t, 8), 11));
    }

    public void CalcChecksum()
    {
        TarHeader h2 = this;
        Array.Clear(h2.CheckSum, 0, h2.CheckSum.Length);
        byte[] data = Util.Legacy_StructToByte(h2);
        SetChecksum(data);
    }

    public void SetChecksum(byte[] data)
    {
        ulong sum = 0;
        int i;
        for (i = 0; i < data.Length; i++)
        {
            sum += (ulong)data[i];
        }

        sum += 0x100;

        StrToByteArray(this.CheckSum, Str.AppendZeroToNumString(Convert.ToString((long)sum, 8), 6));
        this.CheckSum[7] = 0x20;
    }

    public void SetTypeFlag(int flag)
    {
        this.TypeFlag = (byte)flag.ToString()[0];
    }

    public void SetUName(string str)
    {
        StrToByteArray(this.UName, str);
    }

    public void SetGName(string str)
    {
        StrToByteArray(this.GName, str);
    }

    public static void StrToByteArray(byte[] dst, string str)
    {
        Encoding e = Str.ShiftJisEncoding;

        byte[] d = e.GetBytes(str);

        Array.Clear(dst, 0, dst.Length);
        Util.CopyByte(dst, 0, d, 0, Math.Min(d.Length, dst.Length - 1));
    }
}

public static class TarUtil
{
    public static TarHeader CreateTarHeader(string name, Encoding encoding, int type, long size, DateTime dt)
    {
        return CreateTarHeader(name, encoding, type, size, dt, "0000777");
    }

    public static TarHeader CreateTarHeader(string name, Encoding encoding, int type, long size, DateTime dt, string mode)
    {
        TarHeader h = new TarHeader(false);

        h.SetName(name, encoding);

        h.SetMode(mode);
        h.SetMTime(dt);
        h.SetName(name, encoding);
        h.SetSize(size);
        h.SetTypeFlag(type);
        h.SetGID("0000000");
        h.SetUID("0000000");

        h.CalcChecksum();

        return h;
    }
}

public class TarPacker
{
    Fifo fifo;
    Dictionary<string, int> dirList;
    Encoding encoding;

    public TarPacker()
        : this(Str.ShiftJisEncoding)
    {
    }
    public TarPacker(Encoding encoding)
    {
        fifo = new Fifo();
        dirList = new Dictionary<string, int>(StrComparer.SensitiveCaseComparer);
        this.encoding = encoding;
    }

    public void AddDirectory(string name, DateTime dt, string mode)
    {
        name = name.Replace('\\', '/');
        if (name.EndsWith("/") == false)
        {
            name = name + "/";
        }

        if (dirList.ContainsKey(name) == false)
        {
            TarHeader h = TarUtil.CreateTarHeader(name, encoding, 5, 0, dt, mode);
            fifo.Write(Util.Legacy_StructToByte(h));

            dirList.Add(name, 0);
        }
    }

    public void AddDirectory(string name, DateTime dt)
    {
        AddDirectory(name, dt, "0000777");
    }

    long currentFileSize = 0;
    long currentPos = 0;

    public void AddFileSimple(string name, byte[] data, int pos, int len, DateTime dt)
    {
        AddFileSimple(name, data, pos, len, dt, "0000777", "0000777");
    }

    public void AddFileSimple(string name, byte[] data, int pos, int len, DateTime dt, string directoryMode, string mode)
    {
        AddFileStart(name, len, dt, directoryMode, mode);
        AddFileData(data, pos, len);
    }

    public void AddFileStart(string name, long size, DateTime dt)
    {
        AddFileStart(name, size, dt, "0000777", "0000777");
    }

    public void AddFileStart(string name, long size, DateTime dt, string directoryMode, string mode)
    {
        if (currentFileSize != 0 || currentPos != 0)
        {
            throw new ApplicationException("last file not completed.");
        }

        name = name.Replace('\\', '/');
        if (Str.InStr(name, "/", true))
        {
            AddDirectory(Path.GetDirectoryName(name)!, dt, directoryMode);
        }

        TarHeader h = TarUtil.CreateTarHeader(name, encoding, 0, size, dt, mode);
        fifo.Write(Util.Legacy_StructToByte(h));

        currentFileSize = size;
        currentPos = 0;
    }

    public void AddFileData(byte[] data, int pos, int len)
    {
        long totalSize = currentPos + len;

        if (totalSize > currentFileSize)
        {
            throw new ApplicationException("totalSize > currentFileSize");
        }

        fifo.Write(data, pos, len);

        currentPos += len;
        if (currentPos >= currentFileSize)
        {
            long padding = ((currentFileSize + 511) / 512) * 512 - currentFileSize;

            byte[] pad = new byte[padding];
            Array.Clear(pad, 0, pad.Length);
            fifo.Write(pad, 0, pad.Length);

            currentFileSize = 0;
            currentPos = 0;
        }
    }

    public Fifo GeneratedData
    {
        get
        {
            return this.fifo;
        }
    }

    public void Finish()
    {
        byte[] data = new byte[1024];
        Array.Clear(data, 0, data.Length);

        fifo.Write(data);
    }

    public byte[] CompressToGZip()
    {
        GZipPacker g = new GZipPacker();
        byte[] data = this.fifo.Read();

        g.Write(data, 0, data.Length, true);

        return g.GeneratedData.Read();
    }
}
