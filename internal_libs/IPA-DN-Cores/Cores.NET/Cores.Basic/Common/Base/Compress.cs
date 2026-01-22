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

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

using IPA.Cores.Basic.Internal;
using System.IO.Compression;

namespace IPA.Cores.Basic;

public static class DeflateUtil
{
    public static byte[] EasyCompress(ReadOnlySpan<byte> src, CompressionLevel level = CompressionLevel.Optimal)
    {
        using MemoryStream ms = new MemoryStream();
        using var d = new DeflateStream(ms, level);
        d.Write(src);
        d.Flush();
        return ms.ToArray();
    }

    public static Memory<byte> EasyCompressRetMemoryFast(ReadOnlySpan<byte> src, CompressionLevel level = CompressionLevel.Optimal)
    {
        using MemoryStream ms = new MemoryStream();
        using var d = new DeflateStream(ms, level);
        d.Write(src);
        d.Flush();

        return ms.GetBuffer().AsMemory(0, (int)ms.Length);
    }


    public static byte[] EasyDecompress(ReadOnlySpan<byte> src, int maxSize = 0)
        => EasyDecompress(src._CloneMemory(), maxSize);

    public static byte[] EasyDecompress(ReadOnlyMemory<byte> src, int maxSize = 0)
    {
        var srcSegment = src._AsSegment();

        using MemoryStream ms = new MemoryStream(srcSegment.Array!, srcSegment.Offset, srcSegment.Count);
        using var d = new DeflateStream(ms, CompressionMode.Decompress);
        return d._ReadToEnd(maxSize);
    }
}

public static class ZLib
{
    // データを圧縮する
    public static byte[] Compress(byte[] src)
    {
        return Compress(src, zlibConst.Z_DEFAULT_COMPRESSION);
    }
    public static byte[] Compress(byte[] src, int level)
    {
        return Compress(src, level, false);
    }
    public static byte[] Compress(byte[] src, int level, bool noHeader)
    {
        int dstSize = src.Length * 2 + 100;
        byte[] dst = new byte[dstSize];

        compress2(ref dst, src, level, noHeader);

        return dst;
    }

    // データを展開する
    public static byte[] Uncompress(byte[] src, int originalSize)
    {
        byte[] dst = new byte[originalSize];

        uncompress(ref dst, src);

        return dst;
    }

    static void compress2(ref byte[] dest, byte[] src, int level, bool noHeader)
    {
        ZStream stream = new ZStream();

        stream.next_in = src;
        stream.avail_in = src.Length;

        stream.next_out = dest;
        stream.avail_out = dest.Length;

        if (noHeader == false)
        {
            stream.deflateInit(level);
        }
        else
        {
            stream.deflateInit(level, -15);
        }

        stream.deflate(zlibConst.Z_FINISH);

        Array.Resize<byte>(ref dest, (int)stream.total_out);
    }

    static void uncompress(ref byte[] dest, byte[] src)
    {
        ZStream stream = new ZStream();

        stream.next_in = src;
        stream.avail_in = src.Length;

        stream.next_out = dest;
        stream.avail_out = dest.Length;

        stream.inflateInit();

        int err = stream.inflate(zlibConst.Z_FINISH);
        if (err != zlibConst.Z_STREAM_END)
        {
            stream.inflateEnd();
            throw new ApplicationException();
        }

        Array.Resize<byte>(ref dest, (int)stream.total_out);

        err = stream.inflateEnd();
        if (err != zlibConst.Z_OK)
        {
            throw new ApplicationException();
        }
    }
}
