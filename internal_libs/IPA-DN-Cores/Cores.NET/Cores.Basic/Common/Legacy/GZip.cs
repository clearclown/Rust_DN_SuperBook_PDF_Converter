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
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using IPA.Cores.Basic.Internal;

namespace IPA.Cores.Basic.Legacy;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct GZipHeader
{
    public byte ID1, ID2, CM, FLG;
    public uint MTIME;
    public byte XFL, OS;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct GZipFooter
{
    public uint CRC32;
    public uint ISIZE;
}

public static class GZipUtil
{
    public static byte[] Decompress(byte[] gzip)
    {
        using (GZipStream stream = new GZipStream(new MemoryStream(gzip), CompressionMode.Decompress))
        {
            const int size = 4096;
            byte[] buffer = new byte[size];
            using (MemoryStream memory = new MemoryStream())
            {
                int count = 0;
                do
                {
                    count = stream.Read(buffer, 0, size);
                    if (count > 0)
                    {
                        memory.Write(buffer, 0, count);
                    }
                }
                while (count > 0);
                return memory.ToArray();
            }
        }
    }

    public static byte[] Compress(byte[] data)
    {
        GZipPacker p = new GZipPacker();
        p.Write(data, 0, data.Length, true);
        return p.GeneratedData.Read();
    }

    public static bool IsGZipHeader(in GZipHeader header)
    {
        return (header.ID1 == 0x1f && header.ID2 == 0x8b && header.CM == 0x08);
    }

    public static async Task<bool> IsGZipStreamAsync(Stream st, CancellationToken cancel=default)
    {
        if (st.CanSeek == false)
        {
            throw new CoresLibException("Stream cannot seek");
        }

        long position = st.Position;

        try
        {
            int size = Util.SizeOfStruct<GZipHeader>();
            Memory<byte> buf = new byte[size];

            int readSize = await st.ReadAsync(buf, cancel);
            if (readSize < size)
            {
                return false;
            }

            var header = buf._AsStructSafe<GZipHeader>();

            return IsGZipHeader(in header);
        }
        finally
        {
            st.Seek(position, SeekOrigin.Begin);
        }
    }
}

public class GZipPacker
{
    Fifo fifo;
    ZStream zs;
    long currentSize;
    uint crc32;
    bool finished;

    public bool Finished
    {
        get { return finished; }
    }

    public Fifo GeneratedData
    {
        get
        {
            return this.fifo;
        }
    }

    public GZipPacker()
    {
        fifo = new Fifo();

        zs = new ZStream();
        zs.deflateInit(-1, -15);

        this.currentSize = 0;
        this.crc32 = 0xffffffff;
        this.finished = false;

        GZipHeader h = new GZipHeader();
        h.ID1 = 0x1f;
        h.ID2 = 0x8b;
        h.FLG = 0;
        h.MTIME = (uint)Util.DateTimeToUnixTime(DateTime.Now.ToUniversalTime());
        h.XFL = 0;
        h.OS = 3;
        h.CM = 8;

        fifo.Write(Util.Legacy_StructToByte(h));
    }

    public void Write(byte[] data, int pos, int len, bool finish)
    {
        byte[] srcData = Util.ExtractByteArray(data, pos, len);
        byte[] dstData = new byte[srcData.Length * 2 + 100];

        if (this.finished)
        {
            throw new ApplicationException("already finished");
        }

        zs.next_in = srcData;
        zs.avail_in = srcData.Length;
        zs.next_in_index = 0;

        zs.next_out = dstData;
        zs.avail_out = dstData.Length;
        zs.next_out_index = 0;

        if (finish)
        {
            zs.deflate(zlibConst.Z_FINISH);
        }
        else
        {
            zs.deflate(zlibConst.Z_SYNC_FLUSH);
        }

        fifo.Write(dstData, 0, dstData.Length - zs.avail_out);

        currentSize += len;

        this.crc32 = ZipUtil.Crc32Next(data, pos, len, this.crc32);

        if (finish)
        {
            this.finished = true;
            this.crc32 = ~this.crc32;

            GZipFooter f = new GZipFooter();
            f.CRC32 = this.crc32;
            f.ISIZE = (uint)(this.currentSize % 0x100000000);

            fifo.Write(Util.Legacy_StructToByte(f));
        }
    }
}
