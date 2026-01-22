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
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Buffers;
using System.Buffers.Binary;
using System.IO.Compression;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using System.IO;

namespace IPA.Cores.Helper.Basic;

public static class SpanMemoryBufferHelper
{
    public static SpanBuffer<T> _AsSpanBuffer<T>(this Span<T> span) => new SpanBuffer<T>(span);
    public static SpanBuffer<T> _AsSpanBuffer<T>(this Memory<T> memory) => new SpanBuffer<T>(memory.Span);
    public static SpanBuffer<T> _AsSpanBuffer<T>(this T[] data) => new SpanBuffer<T>(data.AsSpan());
    public static SpanBuffer<T> _AsSpanBuffer<T>(this T[] data, int offset) => new SpanBuffer<T>(data.AsSpan(offset));
    public static SpanBuffer<T> _AsSpanBuffer<T>(this T[] data, int offset, int size) => new SpanBuffer<T>(data.AsSpan(offset, size));

    public static ReadOnlySpanBuffer<T> _AsReadOnlySpanBuffer<T>(this ReadOnlySpan<T> span) => new ReadOnlySpanBuffer<T>(span);
    public static ReadOnlySpanBuffer<T> _AsReadOnlySpanBuffer<T>(this ReadOnlyMemory<T> memory) => new ReadOnlySpanBuffer<T>(memory.Span);
    public static ReadOnlySpanBuffer<T> _AsReadOnlySpanBuffer<T>(this T[] data) => new ReadOnlySpanBuffer<T>(data._AsReadOnlySpan());
    public static ReadOnlySpanBuffer<T> _AsReadOnlySpanBuffer<T>(this T[] data, int offset) => new ReadOnlySpanBuffer<T>(data._AsReadOnlySpan(offset));
    public static ReadOnlySpanBuffer<T> _AsReadOnlySpanBuffer<T>(this T[] data, int offset, int size) => new ReadOnlySpanBuffer<T>(data._AsReadOnlySpan(offset, size));

    public static FastMemoryBuffer<T> _AsFastMemoryBuffer<T>(this Memory<T> memory) => new FastMemoryBuffer<T>(memory);
    public static FastMemoryBuffer<T> _AsFastMemoryBuffer<T>(this T[] data) => new FastMemoryBuffer<T>(data.AsMemory());
    public static FastMemoryBuffer<T> _AsFastMemoryBuffer<T>(this T[] data, int offset) => new FastMemoryBuffer<T>(data.AsMemory(offset));
    public static FastMemoryBuffer<T> _AsFastMemoryBuffer<T>(this T[] data, int offset, int size) => new FastMemoryBuffer<T>(data.AsMemory(offset, size));

    public static FastReadOnlyMemoryBuffer<T> _AsFastReadOnlyMemoryBuffer<T>(this ReadOnlyMemory<T> memory) => new FastReadOnlyMemoryBuffer<T>(memory);
    public static FastReadOnlyMemoryBuffer<T> _AsFastReadOnlyMemoryBuffer<T>(this T[] data) => new FastReadOnlyMemoryBuffer<T>(data._AsReadOnlyMemory());
    public static FastReadOnlyMemoryBuffer<T> _AsFastReadOnlyMemoryBuffer<T>(this T[] data, int offset) => new FastReadOnlyMemoryBuffer<T>(data._AsReadOnlyMemory(offset));
    public static FastReadOnlyMemoryBuffer<T> _AsFastReadOnlyMemoryBuffer<T>(this T[] data, int offset, int size) => new FastReadOnlyMemoryBuffer<T>(data._AsReadOnlyMemory(offset, size));

    public static MemoryBuffer<T> _AsMemoryBuffer<T>(this Memory<T> memory) => new MemoryBuffer<T>(memory);
    public static MemoryBuffer<T> _AsMemoryBuffer<T>(this T[] data) => new MemoryBuffer<T>(data.AsMemory());
    public static MemoryBuffer<T> _AsMemoryBuffer<T>(this T[] data, int offset) => new MemoryBuffer<T>(data.AsMemory(offset));
    public static MemoryBuffer<T> _AsMemoryBuffer<T>(this T[] data, int offset, int size) => new MemoryBuffer<T>(data.AsMemory(offset, size));

    public static ReadOnlyMemoryBuffer<T> _AsReadOnlyMemoryBuffer<T>(this ReadOnlyMemory<T> memory) => new ReadOnlyMemoryBuffer<T>(memory);
    public static ReadOnlyMemoryBuffer<T> _AsReadOnlyMemoryBuffer<T>(this T[] data) => new ReadOnlyMemoryBuffer<T>(data._AsReadOnlyMemory());
    public static ReadOnlyMemoryBuffer<T> _AsReadOnlyMemoryBuffer<T>(this T[] data, int offset) => new ReadOnlyMemoryBuffer<T>(data._AsReadOnlyMemory(offset));
    public static ReadOnlyMemoryBuffer<T> _AsReadOnlyMemoryBuffer<T>(this T[] data, int offset, int size) => new ReadOnlyMemoryBuffer<T>(data._AsReadOnlyMemory(offset, size));

    public static BufferBasedStream _AsDirectStream(this MemoryBuffer<byte> buffer) => new BufferBasedStream(buffer);
    public static BufferBasedStream _AsDirectStream(this ReadOnlyMemoryBuffer<byte> buffer) => new BufferBasedStream(buffer);
    public static BufferBasedStream _AsDirectStream(this HugeMemoryBuffer<byte> buffer) => new BufferBasedStream(buffer);
}

public static class MemoryExtHelper
{
    public static Memory<T> _CloneMemory<T>(this ReadOnlyMemory<T> memory) => memory.Span.ToArray();
    public static Memory<T> _CloneMemory<T>(this Memory<T> memory) => memory.Span.ToArray();

    public static Memory<T> _CloneMemory<T>(this ReadOnlySpan<T> span) => span.ToArray();
    public static Memory<T> _CloneMemory<T>(this Span<T> span) => span.ToArray();

    public static Span<T> _CloneSpan<T>(this ReadOnlySpan<T> span) => span.ToArray();
    public static Span<T> _CloneSpan<T>(this Span<T> span) => span.ToArray();

    public static Span<T> _CloneSpan<T>(this ReadOnlyMemory<T> memory) => memory.Span.ToArray();
    public static Span<T> _CloneSpan<T>(this Memory<T> memory) => memory.Span.ToArray();

    [MethodImpl(Inline)]
    public static ReadOnlyMemory<T> _AsReadOnlyMemory<T>(this Memory<T> memory) => memory;
    [MethodImpl(Inline)]
    public static ReadOnlySpan<T> _AsReadOnlySpan<T>(this Span<T> span) => span;

    [MethodImpl(Inline)]
    public static ReadOnlyMemory<T> _AsReadOnlyMemory<T>(this ArraySegment<T> segment) => segment.AsMemory();
    [MethodImpl(Inline)]
    public static ReadOnlyMemory<T> _AsReadOnlyMemory<T>(this ArraySegment<T> segment, int start) => segment.AsMemory(start);
    [MethodImpl(Inline)]
    public static ReadOnlyMemory<T> _AsReadOnlyMemory<T>(this ArraySegment<T> segment, int start, int length) => segment.AsMemory(start, length);
    [MethodImpl(Inline)]
    public static ReadOnlyMemory<T> _AsReadOnlyMemory<T>(this T[] array) => array.AsMemory();
    [MethodImpl(Inline)]
    public static ReadOnlyMemory<T> _AsReadOnlyMemory<T>(this T[] array, int start) => array.AsMemory(start);
    [MethodImpl(Inline)]
    public static ReadOnlyMemory<T> _AsReadOnlyMemory<T>(this T[] array, int start, int length) => array.AsMemory(start, length);

    [MethodImpl(Inline)]
    public static ReadOnlySpan<T> _AsReadOnlySpan<T>(this T[] array, int start) => array.AsSpan(start);
    [MethodImpl(Inline)]
    public static ReadOnlySpan<T> _AsReadOnlySpan<T>(this T[] array) => array.AsSpan();
    [MethodImpl(Inline)]
    public static ReadOnlySpan<T> _AsReadOnlySpan<T>(this ArraySegment<T> segment, int start, int length) => segment.AsSpan(start, length);
    [MethodImpl(Inline)]
    public static ReadOnlySpan<T> _AsReadOnlySpan<T>(this ArraySegment<T> segment, int start) => segment.AsSpan(start);
    [MethodImpl(Inline)]
    public static ReadOnlySpan<T> _AsReadOnlySpan<T>(this T[] array, int start, int length) => array.AsSpan(start, length);

    [MethodImpl(Inline)]
    public static Memory<T> _SliceHead<T>(this Memory<T> target, int length) => target.Slice(0, length);
    [MethodImpl(Inline)]
    public static ReadOnlyMemory<T> _SliceHead<T>(this ReadOnlyMemory<T> target, int length) => target.Slice(0, length);
    [MethodImpl(Inline)]
    public static Span<T> _SliceHead<T>(this Span<T> target, int length) => target.Slice(0, length);
    [MethodImpl(Inline)]
    public static ReadOnlySpan<T> _SliceHead<T>(this ReadOnlySpan<T> target, int length) => target.Slice(0, length);
    [MethodImpl(Inline)]
    public static MemoryBuffer<T> _SliceHead<T>(this MemoryBuffer<T> target, int length) => target.Slice(0, length);
    [MethodImpl(Inline)]
    public static ReadOnlyMemoryBuffer<T> _SliceHead<T>(this ReadOnlyMemoryBuffer<T> target, int length) => target.Slice(0, length);
    [MethodImpl(Inline)]
    public static SpanBuffer<T> _SliceHead<T>(this SpanBuffer<T> target, int length) => target.Slice(0, length);
    [MethodImpl(Inline)]
    public static ReadOnlySpanBuffer<T> _SliceHead<T>(this ReadOnlySpanBuffer<T> target, int length) => target.Slice(0, length);

    [MethodImpl(Inline)]
    public static Memory<T> _SliceTail<T>(this Memory<T> target, int length) => target.Slice(target.Length - length);
    [MethodImpl(Inline)]
    public static ReadOnlyMemory<T> _SliceTail<T>(this ReadOnlyMemory<T> target, int length) => target.Slice(target.Length - length);
    [MethodImpl(Inline)]
    public static Span<T> _SliceTail<T>(this Span<T> target, int length) => target.Slice(target.Length - length);
    [MethodImpl(Inline)]
    public static ReadOnlySpan<T> _SliceTail<T>(this ReadOnlySpan<T> target, int length) => target.Slice(target.Length - length);
    [MethodImpl(Inline)]
    public static MemoryBuffer<T> _SliceTail<T>(this MemoryBuffer<T> target, int length) => target.Slice(target.Length - length);
    [MethodImpl(Inline)]
    public static ReadOnlyMemoryBuffer<T> _SliceTail<T>(this ReadOnlyMemoryBuffer<T> target, int length) => target.Slice(target.Length - length);
    [MethodImpl(Inline)]
    public static SpanBuffer<T> _SliceTail<T>(this SpanBuffer<T> target, int length) => target.Slice(target.Length - length);
    [MethodImpl(Inline)]
    public static ReadOnlySpanBuffer<T> _SliceTail<T>(this ReadOnlySpanBuffer<T> target, int length) => target.Slice(target.Length - length);

    [MethodImpl(Inline)]
    public static bool _IsAllZero(this Memory<byte> target) => Util.IsSpanAllZero(target.Span);
    [MethodImpl(Inline)]
    public static bool _IsAllZero(this ReadOnlyMemory<byte> target) => Util.IsSpanAllZero(target.Span);
    [MethodImpl(Inline)]
    public static bool _IsAllZero(this Span<byte> target) => Util.IsSpanAllZero(target);
    [MethodImpl(Inline)]
    public static bool _IsAllZero(this ReadOnlySpan<byte> target) => Util.IsSpanAllZero(target);
    [MethodImpl(Inline)]
    public static bool _IsAllZero(this MemoryBuffer<byte> target) => Util.IsSpanAllZero(target.Span);
    [MethodImpl(Inline)]
    public static bool _IsAllZero(this ReadOnlyMemoryBuffer<byte> target) => Util.IsSpanAllZero(target.Span);
    [MethodImpl(Inline)]
    public static bool _IsAllZero(this SpanBuffer<byte> target) => Util.IsSpanAllZero(target);
    [MethodImpl(Inline)]
    public static bool _IsAllZero(this ReadOnlySpanBuffer<byte> target) => Util.IsSpanAllZero(target);

    public static byte[] _EasyCompress(this Span<byte> src, CompressionLevel level = CompressionLevel.Optimal) => DeflateUtil.EasyCompress(src, level);
    public static byte[] _EasyCompress(this ReadOnlySpan<byte> src, CompressionLevel level = CompressionLevel.Optimal) => DeflateUtil.EasyCompress(src, level);
    public static byte[] _EasyCompress(this Memory<byte> src, CompressionLevel level = CompressionLevel.Optimal) => DeflateUtil.EasyCompress(src.Span, level);
    public static byte[] _EasyCompress(this ReadOnlyMemory<byte> src, CompressionLevel level = CompressionLevel.Optimal) => DeflateUtil.EasyCompress(src.Span, level);
    public static byte[] _EasyCompress(this byte[] src, CompressionLevel level = CompressionLevel.Optimal) => DeflateUtil.EasyCompress(src, level);

    public static byte[] _EasyDecompress(this Span<byte> src) => DeflateUtil.EasyDecompress(src);
    public static byte[] _EasyDecompress(this ReadOnlySpan<byte> src) => DeflateUtil.EasyDecompress(src);
    public static byte[] _EasyDecompress(this Memory<byte> src) => DeflateUtil.EasyDecompress(src);
    public static byte[] _EasyDecompress(this ReadOnlyMemory<byte> src) => DeflateUtil.EasyDecompress(src);
    public static byte[] _EasyDecompress(this byte[] src) => DeflateUtil.EasyDecompress(src._AsReadOnlyMemory());

    public static Memory<byte> _EasyEncrypt(this Span<byte> src, string? password = null) => Secure.EasyEncrypt(src._CloneMemory(), password);
    public static Memory<byte> _EasyEncrypt(this ReadOnlySpan<byte> src, string? password = null) => Secure.EasyEncrypt(src._CloneMemory(), password);
    public static Memory<byte> _EasyEncrypt(this Memory<byte> src, string? password = null) => Secure.EasyEncrypt(src, password);
    public static Memory<byte> _EasyEncrypt(this ReadOnlyMemory<byte> src, string? password = null) => Secure.EasyEncrypt(src, password);
    public static Memory<byte> _EasyEncrypt(this byte[] src, string? password = null) => Secure.EasyEncrypt(src, password);

    public static Memory<byte> _EasyDecrypt(this Span<byte> src, string? password = null) => Secure.EasyDecrypt(src._CloneMemory(), password);
    public static Memory<byte> _EasyDecrypt(this ReadOnlySpan<byte> src, string? password = null) => Secure.EasyDecrypt(src._CloneMemory(), password);
    public static Memory<byte> _EasyDecrypt(this Memory<byte> src, string? password = null) => Secure.EasyDecrypt(src, password);
    public static Memory<byte> _EasyDecrypt(this ReadOnlyMemory<byte> src, string? password = null) => Secure.EasyDecrypt(src, password);
    public static Memory<byte> _EasyDecrypt(this byte[] src, string? password = null) => Secure.EasyDecrypt(src._AsReadOnlyMemory(), password);

    // For BigEndian-standard world
    [MethodImpl(Inline)]
    public static ushort _Endian16_U(this ushort v) => BitConverter.IsLittleEndian ? BinaryPrimitives.ReverseEndianness(v) : v;
    [MethodImpl(Inline)]
    public static ushort _Endian16_U(this uint v) => BitConverter.IsLittleEndian ? BinaryPrimitives.ReverseEndianness((ushort)v) : (ushort)v;
    [MethodImpl(Inline)]
    public static ushort _Endian16_U(this ulong v) => BitConverter.IsLittleEndian ? BinaryPrimitives.ReverseEndianness((ushort)v) : (ushort)v;

    [MethodImpl(Inline)]
    public static short _Endian16_S(this short v) => BitConverter.IsLittleEndian ? BinaryPrimitives.ReverseEndianness(v) : v;
    [MethodImpl(Inline)]
    public static short _Endian16_S(this int v) => BitConverter.IsLittleEndian ? BinaryPrimitives.ReverseEndianness((short)v) : (short)v;
    [MethodImpl(Inline)]
    public static short _Endian16_S(this long v) => BitConverter.IsLittleEndian ? BinaryPrimitives.ReverseEndianness((short)v) : (short)v;


    [MethodImpl(Inline)]
    public static ushort _Endian16_U(this short v) => BitConverter.IsLittleEndian ? BinaryPrimitives.ReverseEndianness((ushort)v) : (ushort)v;
    [MethodImpl(Inline)]
    public static ushort _Endian16_U(this int v) => BitConverter.IsLittleEndian ? BinaryPrimitives.ReverseEndianness((ushort)v) : (ushort)v;
    [MethodImpl(Inline)]
    public static ushort _Endian16_U(this long v) => BitConverter.IsLittleEndian ? BinaryPrimitives.ReverseEndianness((ushort)v) : (ushort)v;

    [MethodImpl(Inline)]
    public static uint _Endian32_U(this uint v) => BitConverter.IsLittleEndian ? BinaryPrimitives.ReverseEndianness(v) : v;
    [MethodImpl(Inline)]
    public static uint _Endian32_U(this ulong v) => BitConverter.IsLittleEndian ? BinaryPrimitives.ReverseEndianness((uint)v) : (uint)v;

    [MethodImpl(Inline)]
    public static int _Endian32_S(this int v) => BitConverter.IsLittleEndian ? BinaryPrimitives.ReverseEndianness(v) : v;
    [MethodImpl(Inline)]
    public static int _Endian32_S(this long v) => BitConverter.IsLittleEndian ? BinaryPrimitives.ReverseEndianness((int)v) : (int)v;

    [MethodImpl(Inline)]
    public static uint _Endian32_U(this int v) => BitConverter.IsLittleEndian ? BinaryPrimitives.ReverseEndianness((uint)v) : (uint)v;
    [MethodImpl(Inline)]
    public static uint _Endian32_U(this long v) => BitConverter.IsLittleEndian ? BinaryPrimitives.ReverseEndianness((uint)v) : (uint)v;

    [MethodImpl(Inline)]
    public static ulong _Endian64_U(this ulong v) => BitConverter.IsLittleEndian ? BinaryPrimitives.ReverseEndianness(v) : v;

    [MethodImpl(Inline)]
    public static long _Endian64_S(this long v) => BitConverter.IsLittleEndian ? BinaryPrimitives.ReverseEndianness(v) : v;

    [MethodImpl(Inline)]
    public static ulong _Endian64_U(this long v) => BitConverter.IsLittleEndian ? BinaryPrimitives.ReverseEndianness((ulong)v) : (ulong)v;

    [MethodImpl(Inline)]
    public static T _Endian16<T>(this T v) where T : unmanaged, Enum => BitConverter.IsLittleEndian ? _ReverseEndian16(v) : v;

    [MethodImpl(Inline)]
    public static T _Endian32<T>(this T v) where T : unmanaged, Enum => BitConverter.IsLittleEndian ? _ReverseEndian32(v) : v;

    [MethodImpl(Inline)]
    public static T _Endian64<T>(this T v) where T : unmanaged, Enum => BitConverter.IsLittleEndian ? _ReverseEndian64(v) : v;


    // For Little-endian standard world
    [MethodImpl(Inline)]
    public static ushort _LE_Endian16_U(this ushort v) => (!BitConverter.IsLittleEndian) ? BinaryPrimitives.ReverseEndianness(v) : v;
    [MethodImpl(Inline)]
    public static ushort _LE_Endian16_U(this uint v) => (!BitConverter.IsLittleEndian) ? BinaryPrimitives.ReverseEndianness((ushort)v) : (ushort)v;
    [MethodImpl(Inline)]
    public static ushort _LE_Endian16_U(this ulong v) => (!BitConverter.IsLittleEndian) ? BinaryPrimitives.ReverseEndianness((ushort)v) : (ushort)v;

    [MethodImpl(Inline)]
    public static short _LE_Endian16_S(this short v) => (!BitConverter.IsLittleEndian) ? BinaryPrimitives.ReverseEndianness(v) : v;
    [MethodImpl(Inline)]
    public static short _LE_Endian16_S(this int v) => (!BitConverter.IsLittleEndian) ? BinaryPrimitives.ReverseEndianness((short)v) : (short)v;
    [MethodImpl(Inline)]
    public static short _LE_Endian16_S(this long v) => (!BitConverter.IsLittleEndian) ? BinaryPrimitives.ReverseEndianness((short)v) : (short)v;


    [MethodImpl(Inline)]
    public static ushort _LE_Endian16_U(this short v) => (!BitConverter.IsLittleEndian) ? BinaryPrimitives.ReverseEndianness((ushort)v) : (ushort)v;
    [MethodImpl(Inline)]
    public static ushort _LE_Endian16_U(this int v) => (!BitConverter.IsLittleEndian) ? BinaryPrimitives.ReverseEndianness((ushort)v) : (ushort)v;
    [MethodImpl(Inline)]
    public static ushort _LE_Endian16_U(this long v) => (!BitConverter.IsLittleEndian) ? BinaryPrimitives.ReverseEndianness((ushort)v) : (ushort)v;

    [MethodImpl(Inline)]
    public static uint _LE_Endian32_U(this uint v) => (!BitConverter.IsLittleEndian) ? BinaryPrimitives.ReverseEndianness(v) : v;
    [MethodImpl(Inline)]
    public static uint _LE_Endian32_U(this ulong v) => (!BitConverter.IsLittleEndian) ? BinaryPrimitives.ReverseEndianness((uint)v) : (uint)v;

    [MethodImpl(Inline)]
    public static int _LE_Endian32_S(this int v) => (!BitConverter.IsLittleEndian) ? BinaryPrimitives.ReverseEndianness(v) : v;
    [MethodImpl(Inline)]
    public static int _LE_Endian32_S(this long v) => (!BitConverter.IsLittleEndian) ? BinaryPrimitives.ReverseEndianness((int)v) : (int)v;

    [MethodImpl(Inline)]
    public static uint _LE_Endian32_U(this int v) => (!BitConverter.IsLittleEndian) ? BinaryPrimitives.ReverseEndianness((uint)v) : (uint)v;
    [MethodImpl(Inline)]
    public static uint _LE_Endian32_U(this long v) => (!BitConverter.IsLittleEndian) ? BinaryPrimitives.ReverseEndianness((uint)v) : (uint)v;

    [MethodImpl(Inline)]
    public static ulong _LE_Endian64_U(this ulong v) => (!BitConverter.IsLittleEndian) ? BinaryPrimitives.ReverseEndianness(v) : v;

    [MethodImpl(Inline)]
    public static long _LE_Endian64_S(this long v) => (!BitConverter.IsLittleEndian) ? BinaryPrimitives.ReverseEndianness(v) : v;

    [MethodImpl(Inline)]
    public static ulong _LE_Endian64_U(this long v) => (!BitConverter.IsLittleEndian) ? BinaryPrimitives.ReverseEndianness((ulong)v) : (ulong)v;

    [MethodImpl(Inline)]
    public static T _LE_Endian16<T>(this T v) where T : unmanaged, Enum => (!BitConverter.IsLittleEndian) ? _ReverseEndian16(v) : v;

    [MethodImpl(Inline)]
    public static T _LE_Endian32<T>(this T v) where T : unmanaged, Enum => (!BitConverter.IsLittleEndian) ? _ReverseEndian32(v) : v;

    [MethodImpl(Inline)]
    public static T _LE_Endian64<T>(this T v) where T : unmanaged, Enum => (!BitConverter.IsLittleEndian) ? _ReverseEndian64(v) : v;





    [MethodImpl(Inline)]
    public static ushort _ReverseEndian16_U(this ushort v) => BinaryPrimitives.ReverseEndianness(v);

    [MethodImpl(Inline)]
    public static short _ReverseEndian16_S(this short v) => BinaryPrimitives.ReverseEndianness(v);

    [MethodImpl(Inline)]
    public static uint _ReverseEndian32_U(this uint v) => BinaryPrimitives.ReverseEndianness(v);

    [MethodImpl(Inline)]
    public static int _ReverseEndian32_S(this int v) => BinaryPrimitives.ReverseEndianness(v);

    [MethodImpl(Inline)]
    public static ulong _ReverseEndian64_U(this ulong v) => BinaryPrimitives.ReverseEndianness(v);

    [MethodImpl(Inline)]
    public static long _ReverseEndian64_S(this long v) => BinaryPrimitives.ReverseEndianness(v);

    [MethodImpl(Inline)]
    public static unsafe T _ReverseEndian16<T>(this T v) where T : unmanaged, Enum
    {
        byte* ptr = (byte*)(Unsafe.AsPointer(ref v));
        *((ushort*)ptr) = BinaryPrimitives.ReverseEndianness(*((ushort*)ptr));
        return v;
    }

    [MethodImpl(Inline)]
    public static unsafe T _ReverseEndian32<T>(this T v) where T : unmanaged, Enum
    {
        byte* ptr = (byte*)(Unsafe.AsPointer(ref v));
        *((uint*)ptr) = BinaryPrimitives.ReverseEndianness(*((uint*)ptr));
        return v;
    }

    [MethodImpl(Inline)]
    public static unsafe T _ReverseEndian64<T>(this T v) where T : unmanaged, Enum
    {
        byte* ptr = (byte*)(Unsafe.AsPointer(ref v));
        *((ulong*)ptr) = BinaryPrimitives.ReverseEndianness(*((ulong*)ptr));
        return v;
    }


    [MethodImpl(Inline)]
    public static ReadOnlySpan<T> _AsOtherSpan<T>(this ReadOnlySpan<byte> source)
        where T : struct
    {
        if (source.Length % Unsafe.SizeOf<T>() != 0)
        {
            throw new ArgumentOutOfRangeException("source.Length");
        }
        return MemoryMarshal.Cast<byte, T>(source);
    }

    [MethodImpl(Inline)]
    public static ReadOnlySpan<ushort> _AsUInt16Span(this ReadOnlySpan<byte> source)
        => _AsOtherSpan<ushort>(source);

    [MethodImpl(Inline)]
    public static ReadOnlySpan<uint> _AsUInt32Span(this ReadOnlySpan<byte> source)
        => _AsOtherSpan<uint>(source);

    [MethodImpl(Inline)]
    public static ReadOnlySpan<ulong> _AsUInt64Span(this ReadOnlySpan<byte> source)
        => _AsOtherSpan<ulong>(source);

    [MethodImpl(Inline)]
    public static ReadOnlySpan<short> _AsSInt16Span(this ReadOnlySpan<byte> source)
        => _AsOtherSpan<short>(source);

    [MethodImpl(Inline)]
    public static ReadOnlySpan<int> _AsSInt32Span(this ReadOnlySpan<byte> source)
        => _AsOtherSpan<int>(source);

    [MethodImpl(Inline)]
    public static ReadOnlySpan<long> _AsSInt64Span(this ReadOnlySpan<byte> source)
        => _AsOtherSpan<long>(source);


    [MethodImpl(Inline)]
    public static ReadOnlySpan<byte> _AsUInt8Span<T>(this ReadOnlySpan<T> source)
        where T : struct
    {
        return MemoryMarshal.Cast<T, byte>(source);
    }




    [MethodImpl(Inline)]
    public static Span<T> _AsOtherSpan<T>(this Span<byte> source)
        where T : struct
    {
        if (source.Length % Unsafe.SizeOf<T>() != 0)
        {
            throw new ArgumentOutOfRangeException("source.Length");
        }
        return MemoryMarshal.Cast<byte, T>(source);
    }

    [MethodImpl(Inline)]
    public static Span<ushort> _AsUInt16Span(this Span<byte> source)
        => _AsOtherSpan<ushort>(source);

    [MethodImpl(Inline)]
    public static Span<uint> _AsUInt32Span(this Span<byte> source)
        => _AsOtherSpan<uint>(source);

    [MethodImpl(Inline)]
    public static Span<ulong> _AsUInt64Span(this Span<byte> source)
        => _AsOtherSpan<ulong>(source);

    [MethodImpl(Inline)]
    public static Span<short> _AsSInt16Span(this Span<byte> source)
        => _AsOtherSpan<short>(source);

    [MethodImpl(Inline)]
    public static Span<int> _AsSInt32Span(this Span<byte> source)
        => _AsOtherSpan<int>(source);

    [MethodImpl(Inline)]
    public static Span<long> _AsSInt64Span(this Span<byte> source)
        => _AsOtherSpan<long>(source);


    [MethodImpl(Inline)]
    public static Span<byte> _AsUInt8Span<T>(this Span<T> source)
        where T : struct
    {
        return MemoryMarshal.Cast<T, byte>(source);
    }




    #region AutoGenerated

    public static unsafe bool _GetBool8(this byte[] data, int offset = 0)
    {
        return (data[offset] == 0) ? false : true;
    }

    public static unsafe byte _GetUInt8(this byte[] data, int offset = 0)
    {
        return (byte)data[offset];
    }

    public static unsafe sbyte _GetSInt8(this byte[] data, int offset = 0)
    {
        return (sbyte)data[offset];
    }

    public static unsafe ushort _GetUInt16(this byte[] data, int offset = 0, bool littleEndian = false)
    {
        if (offset < 0) throw new ArgumentOutOfRangeException("offset < 0");
        if (checked(offset + sizeof(ushort)) > data.Length) throw new ArgumentOutOfRangeException("data.Length is too small");
        fixed (byte* ptr = data)
            return BitConverter.IsLittleEndian != littleEndian ? BinaryPrimitives.ReverseEndianness(*((ushort*)(ptr + offset))) : *((ushort*)(ptr + offset));
    }

    public static unsafe short _GetSInt16(this byte[] data, int offset = 0, bool littleEndian = false)
    {
        if (offset < 0) throw new ArgumentOutOfRangeException("offset < 0");
        if (checked(offset + sizeof(short)) > data.Length) throw new ArgumentOutOfRangeException("data.Length is too small");
        fixed (byte* ptr = data)
            return BitConverter.IsLittleEndian != littleEndian ? BinaryPrimitives.ReverseEndianness(*((short*)(ptr + offset))) : *((short*)(ptr + offset));
    }

    public static unsafe uint _GetUInt32(this byte[] data, int offset = 0, bool littleEndian = false)
    {
        if (offset < 0) throw new ArgumentOutOfRangeException("offset < 0");
        if (checked(offset + sizeof(uint)) > data.Length) throw new ArgumentOutOfRangeException("data.Length is too small");
        fixed (byte* ptr = data)
            return BitConverter.IsLittleEndian != littleEndian ? BinaryPrimitives.ReverseEndianness(*((uint*)(ptr + offset))) : *((uint*)(ptr + offset));
    }

    public static unsafe int _GetSInt32(this byte[] data, int offset = 0, bool littleEndian = false)
    {
        if (offset < 0) throw new ArgumentOutOfRangeException("offset < 0");
        if (checked(offset + sizeof(int)) > data.Length) throw new ArgumentOutOfRangeException("data.Length is too small");
        fixed (byte* ptr = data)
            return BitConverter.IsLittleEndian != littleEndian ? BinaryPrimitives.ReverseEndianness(*((int*)(ptr + offset))) : *((int*)(ptr + offset));
    }

    public static unsafe ulong _GetUInt64(this byte[] data, int offset = 0, bool littleEndian = false)
    {
        if (offset < 0) throw new ArgumentOutOfRangeException("offset < 0");
        if (checked(offset + sizeof(ulong)) > data.Length) throw new ArgumentOutOfRangeException("data.Length is too small");
        fixed (byte* ptr = data)
            return BitConverter.IsLittleEndian != littleEndian ? BinaryPrimitives.ReverseEndianness(*((ulong*)(ptr + offset))) : *((ulong*)(ptr + offset));
    }

    public static unsafe long _GetSInt64(this byte[] data, int offset = 0, bool littleEndian = false)
    {
        if (offset < 0) throw new ArgumentOutOfRangeException("offset < 0");
        if (checked(offset + sizeof(long)) > data.Length) throw new ArgumentOutOfRangeException("data.Length is too small");
        fixed (byte* ptr = data)
            return BitConverter.IsLittleEndian != littleEndian ? BinaryPrimitives.ReverseEndianness(*((long*)(ptr + offset))) : *((long*)(ptr + offset));
    }

    public static unsafe bool _GetBool8(this Span<byte> span)
    {
        return (span[0] == 0) ? false : true;
    }

    public static unsafe byte _GetUInt8(this Span<byte> span)
    {
        return (byte)span[0];
    }

    public static unsafe sbyte _GetSInt8(this Span<byte> span)
    {
        return (sbyte)span[0];
    }

    public static unsafe ushort _GetUInt16(this Span<byte> span, bool littleEndian = false)
    {
        if (span.Length < sizeof(ushort)) throw new ArgumentOutOfRangeException("span.Length is too small");
        fixed (byte* ptr = span)
            return BitConverter.IsLittleEndian != littleEndian ? BinaryPrimitives.ReverseEndianness(*((ushort*)(ptr))) : *((ushort*)(ptr));
    }

    public static unsafe short _GetSInt16(this Span<byte> span, bool littleEndian = false)
    {
        if (span.Length < sizeof(short)) throw new ArgumentOutOfRangeException("span.Length is too small");
        fixed (byte* ptr = span)
            return BitConverter.IsLittleEndian != littleEndian ? BinaryPrimitives.ReverseEndianness(*((short*)(ptr))) : *((short*)(ptr));
    }

    public static unsafe uint _GetUInt32(this Span<byte> span, bool littleEndian = false)
    {
        if (span.Length < sizeof(uint)) throw new ArgumentOutOfRangeException("span.Length is too small");
        fixed (byte* ptr = span)
            return BitConverter.IsLittleEndian != littleEndian ? BinaryPrimitives.ReverseEndianness(*((uint*)(ptr))) : *((uint*)(ptr));
    }

    public static unsafe int _GetSInt32(this Span<byte> span, bool littleEndian = false)
    {
        if (span.Length < sizeof(int)) throw new ArgumentOutOfRangeException("span.Length is too small");
        fixed (byte* ptr = span)
            return BitConverter.IsLittleEndian != littleEndian ? BinaryPrimitives.ReverseEndianness(*((int*)(ptr))) : *((int*)(ptr));
    }

    public static unsafe ulong _GetUInt64(this Span<byte> span, bool littleEndian = false)
    {
        if (span.Length < sizeof(ulong)) throw new ArgumentOutOfRangeException("span.Length is too small");
        fixed (byte* ptr = span)
            return BitConverter.IsLittleEndian != littleEndian ? BinaryPrimitives.ReverseEndianness(*((ulong*)(ptr))) : *((ulong*)(ptr));
    }

    public static unsafe long _GetSInt64(this Span<byte> span, bool littleEndian = false)
    {
        if (span.Length < sizeof(long)) throw new ArgumentOutOfRangeException("span.Length is too small");
        fixed (byte* ptr = span)
            return BitConverter.IsLittleEndian != littleEndian ? BinaryPrimitives.ReverseEndianness(*((long*)(ptr))) : *((long*)(ptr));
    }

    public static unsafe bool _GetBool8(this ReadOnlySpan<byte> span)
    {
        return (span[0] == 0) ? false : true;
    }

    public static unsafe byte _GetUInt8(this ReadOnlySpan<byte> span)
    {
        return (byte)span[0];
    }

    public static unsafe sbyte _GetSInt8(this ReadOnlySpan<byte> span)
    {
        return (sbyte)span[0];
    }

    public static unsafe ushort _GetUInt16(this ReadOnlySpan<byte> span, bool littleEndian = false)
    {
        if (span.Length < sizeof(ushort)) throw new ArgumentOutOfRangeException("span.Length is too small");
        fixed (byte* ptr = span)
            return BitConverter.IsLittleEndian != littleEndian ? BinaryPrimitives.ReverseEndianness(*((ushort*)(ptr))) : *((ushort*)(ptr));
    }

    public static unsafe short _GetSInt16(this ReadOnlySpan<byte> span, bool littleEndian = false)
    {
        if (span.Length < sizeof(short)) throw new ArgumentOutOfRangeException("span.Length is too small");
        fixed (byte* ptr = span)
            return BitConverter.IsLittleEndian != littleEndian ? BinaryPrimitives.ReverseEndianness(*((short*)(ptr))) : *((short*)(ptr));
    }

    public static unsafe uint _GetUInt32(this ReadOnlySpan<byte> span, bool littleEndian = false)
    {
        if (span.Length < sizeof(uint)) throw new ArgumentOutOfRangeException("span.Length is too small");
        fixed (byte* ptr = span)
            return BitConverter.IsLittleEndian != littleEndian ? BinaryPrimitives.ReverseEndianness(*((uint*)(ptr))) : *((uint*)(ptr));
    }

    public static unsafe int _GetSInt32(this ReadOnlySpan<byte> span, bool littleEndian = false)
    {
        if (span.Length < sizeof(int)) throw new ArgumentOutOfRangeException("span.Length is too small");
        fixed (byte* ptr = span)
            return BitConverter.IsLittleEndian != littleEndian ? BinaryPrimitives.ReverseEndianness(*((int*)(ptr))) : *((int*)(ptr));
    }

    public static unsafe ulong _GetUInt64(this ReadOnlySpan<byte> span, bool littleEndian = false)
    {
        if (span.Length < sizeof(ulong)) throw new ArgumentOutOfRangeException("span.Length is too small");
        fixed (byte* ptr = span)
            return BitConverter.IsLittleEndian != littleEndian ? BinaryPrimitives.ReverseEndianness(*((ulong*)(ptr))) : *((ulong*)(ptr));
    }

    public static unsafe long _GetSInt64(this ReadOnlySpan<byte> span, bool littleEndian = false)
    {
        if (span.Length < sizeof(long)) throw new ArgumentOutOfRangeException("span.Length is too small");
        fixed (byte* ptr = span)
            return BitConverter.IsLittleEndian != littleEndian ? BinaryPrimitives.ReverseEndianness(*((long*)(ptr))) : *((long*)(ptr));
    }

    public static unsafe bool _GetBool8(this Memory<byte> memory)
    {
        return (memory.Span[0] == 0) ? false : true;
    }

    public static unsafe byte _GetUInt8(this Memory<byte> memory)
    {
        return (byte)memory.Span[0];
    }

    public static unsafe sbyte _GetSInt8(this Memory<byte> memory)
    {
        return (sbyte)memory.Span[0];
    }

    public static unsafe ushort _GetUInt16(this Memory<byte> memory, bool littleEndian = false)
    {
        if (memory.Length < sizeof(ushort)) throw new ArgumentOutOfRangeException("memory.Length is too small");
        fixed (byte* ptr = memory.Span)
            return BitConverter.IsLittleEndian != littleEndian ? BinaryPrimitives.ReverseEndianness(*((ushort*)(ptr))) : *((ushort*)(ptr));
    }

    public static unsafe short _GetSInt16(this Memory<byte> memory, bool littleEndian = false)
    {
        if (memory.Length < sizeof(short)) throw new ArgumentOutOfRangeException("memory.Length is too small");
        fixed (byte* ptr = memory.Span)
            return BitConverter.IsLittleEndian != littleEndian ? BinaryPrimitives.ReverseEndianness(*((short*)(ptr))) : *((short*)(ptr));
    }

    public static unsafe uint _GetUInt32(this Memory<byte> memory, bool littleEndian = false)
    {
        if (memory.Length < sizeof(uint)) throw new ArgumentOutOfRangeException("memory.Length is too small");
        fixed (byte* ptr = memory.Span)
            return BitConverter.IsLittleEndian != littleEndian ? BinaryPrimitives.ReverseEndianness(*((uint*)(ptr))) : *((uint*)(ptr));
    }

    public static unsafe int _GetSInt32(this Memory<byte> memory, bool littleEndian = false)
    {
        if (memory.Length < sizeof(int)) throw new ArgumentOutOfRangeException("memory.Length is too small");
        fixed (byte* ptr = memory.Span)
            return BitConverter.IsLittleEndian != littleEndian ? BinaryPrimitives.ReverseEndianness(*((int*)(ptr))) : *((int*)(ptr));
    }

    public static unsafe ulong _GetUInt64(this Memory<byte> memory, bool littleEndian = false)
    {
        if (memory.Length < sizeof(ulong)) throw new ArgumentOutOfRangeException("memory.Length is too small");
        fixed (byte* ptr = memory.Span)
            return BitConverter.IsLittleEndian != littleEndian ? BinaryPrimitives.ReverseEndianness(*((ulong*)(ptr))) : *((ulong*)(ptr));
    }

    public static unsafe long _GetSInt64(this Memory<byte> memory, bool littleEndian = false)
    {
        if (memory.Length < sizeof(long)) throw new ArgumentOutOfRangeException("memory.Length is too small");
        fixed (byte* ptr = memory.Span)
            return BitConverter.IsLittleEndian != littleEndian ? BinaryPrimitives.ReverseEndianness(*((long*)(ptr))) : *((long*)(ptr));
    }

    public static unsafe bool _GetBool8(this ReadOnlyMemory<byte> memory)
    {
        return (memory.Span[0] == 0) ? false : true;
    }

    public static unsafe byte _GetUInt8(this ReadOnlyMemory<byte> memory)
    {
        return (byte)memory.Span[0];
    }

    public static unsafe sbyte _GetSInt8(this ReadOnlyMemory<byte> memory)
    {
        return (sbyte)memory.Span[0];
    }

    public static unsafe ushort _GetUInt16(this ReadOnlyMemory<byte> memory, bool littleEndian = false)
    {
        if (memory.Length < sizeof(ushort)) throw new ArgumentOutOfRangeException("memory.Length is too small");
        fixed (byte* ptr = memory.Span)
            return BitConverter.IsLittleEndian != littleEndian ? BinaryPrimitives.ReverseEndianness(*((ushort*)(ptr))) : *((ushort*)(ptr));
    }

    public static unsafe short _GetSInt16(this ReadOnlyMemory<byte> memory, bool littleEndian = false)
    {
        if (memory.Length < sizeof(short)) throw new ArgumentOutOfRangeException("memory.Length is too small");
        fixed (byte* ptr = memory.Span)
            return BitConverter.IsLittleEndian != littleEndian ? BinaryPrimitives.ReverseEndianness(*((short*)(ptr))) : *((short*)(ptr));
    }

    public static unsafe uint _GetUInt32(this ReadOnlyMemory<byte> memory, bool littleEndian = false)
    {
        if (memory.Length < sizeof(uint)) throw new ArgumentOutOfRangeException("memory.Length is too small");
        fixed (byte* ptr = memory.Span)
            return BitConverter.IsLittleEndian != littleEndian ? BinaryPrimitives.ReverseEndianness(*((uint*)(ptr))) : *((uint*)(ptr));
    }

    public static unsafe int _GetSInt32(this ReadOnlyMemory<byte> memory, bool littleEndian = false)
    {
        if (memory.Length < sizeof(int)) throw new ArgumentOutOfRangeException("memory.Length is too small");
        fixed (byte* ptr = memory.Span)
            return BitConverter.IsLittleEndian != littleEndian ? BinaryPrimitives.ReverseEndianness(*((int*)(ptr))) : *((int*)(ptr));
    }

    public static unsafe ulong _GetUInt64(this ReadOnlyMemory<byte> memory, bool littleEndian = false)
    {
        if (memory.Length < sizeof(ulong)) throw new ArgumentOutOfRangeException("memory.Length is too small");
        fixed (byte* ptr = memory.Span)
            return BitConverter.IsLittleEndian != littleEndian ? BinaryPrimitives.ReverseEndianness(*((ulong*)(ptr))) : *((ulong*)(ptr));
    }

    public static unsafe long _GetSInt64(this ReadOnlyMemory<byte> memory, bool littleEndian = false)
    {
        if (memory.Length < sizeof(long)) throw new ArgumentOutOfRangeException("memory.Length is too small");
        fixed (byte* ptr = memory.Span)
            return BitConverter.IsLittleEndian != littleEndian ? BinaryPrimitives.ReverseEndianness(*((long*)(ptr))) : *((long*)(ptr));
    }


    public static unsafe void _SetBool8(this bool value, byte[] data, int offset = 0)
    {
        data[offset] = (byte)(value ? 1 : 0);
    }

    public static unsafe void _SetBool8(this byte[] data, bool value, int offset = 0)
    {
        data[offset] = (byte)(value ? 1 : 0);
    }

    public static unsafe void _SetUInt8(this byte value, byte[] data, int offset = 0)
    {
        data[offset] = (byte)value;
    }

    public static unsafe void _SetUInt8(this byte[] data, byte value, int offset = 0)
    {
        data[offset] = (byte)value;
    }

    public static unsafe void _SetSInt8(this sbyte value, byte[] data, int offset = 0)
    {
        data[offset] = (byte)value;
    }

    public static unsafe void _SetSInt8(this byte[] data, sbyte value, int offset = 0)
    {
        data[offset] = (byte)value;
    }

    public static unsafe void _SetUInt16(this ushort value, byte[] data, int offset = 0, bool littleEndian = false)
    {
        if (offset < 0) throw new ArgumentOutOfRangeException("offset < 0");
        if (checked(offset + sizeof(ushort)) > data.Length) throw new ArgumentOutOfRangeException("data.Length is too small");
        fixed (byte* ptr = data)
            *((ushort*)(ptr + offset)) = BitConverter.IsLittleEndian != littleEndian ? BinaryPrimitives.ReverseEndianness(value) : value;
    }

    public static unsafe void _SetUInt16(this byte[] data, ushort value, int offset = 0, bool littleEndian = false)
    {
        if (offset < 0) throw new ArgumentOutOfRangeException("offset < 0");
        if (checked(offset + sizeof(ushort)) > data.Length) throw new ArgumentOutOfRangeException("data.Length is too small");
        fixed (byte* ptr = data)
            *((ushort*)(ptr + offset)) = BitConverter.IsLittleEndian != littleEndian ? BinaryPrimitives.ReverseEndianness(value) : value;
    }

    public static unsafe void _SetSInt16(this short value, byte[] data, int offset = 0, bool littleEndian = false)
    {
        if (offset < 0) throw new ArgumentOutOfRangeException("offset < 0");
        if (checked(offset + sizeof(short)) > data.Length) throw new ArgumentOutOfRangeException("data.Length is too small");
        fixed (byte* ptr = data)
            *((short*)(ptr + offset)) = BitConverter.IsLittleEndian != littleEndian ? BinaryPrimitives.ReverseEndianness(value) : value;
    }

    public static unsafe void _SetSInt16(this byte[] data, short value, int offset = 0, bool littleEndian = false)
    {
        if (offset < 0) throw new ArgumentOutOfRangeException("offset < 0");
        if (checked(offset + sizeof(short)) > data.Length) throw new ArgumentOutOfRangeException("data.Length is too small");
        fixed (byte* ptr = data)
            *((short*)(ptr + offset)) = BitConverter.IsLittleEndian != littleEndian ? BinaryPrimitives.ReverseEndianness(value) : value;
    }

    public static unsafe void _SetUInt32(this uint value, byte[] data, int offset = 0, bool littleEndian = false)
    {
        if (offset < 0) throw new ArgumentOutOfRangeException("offset < 0");
        if (checked(offset + sizeof(uint)) > data.Length) throw new ArgumentOutOfRangeException("data.Length is too small");
        fixed (byte* ptr = data)
            *((uint*)(ptr + offset)) = BitConverter.IsLittleEndian != littleEndian ? BinaryPrimitives.ReverseEndianness(value) : value;
    }

    public static unsafe void _SetUInt32(this byte[] data, uint value, int offset = 0, bool littleEndian = false)
    {
        if (offset < 0) throw new ArgumentOutOfRangeException("offset < 0");
        if (checked(offset + sizeof(uint)) > data.Length) throw new ArgumentOutOfRangeException("data.Length is too small");
        fixed (byte* ptr = data)
            *((uint*)(ptr + offset)) = BitConverter.IsLittleEndian != littleEndian ? BinaryPrimitives.ReverseEndianness(value) : value;
    }

    public static unsafe void _SetSInt32(this int value, byte[] data, int offset = 0, bool littleEndian = false)
    {
        if (offset < 0) throw new ArgumentOutOfRangeException("offset < 0");
        if (checked(offset + sizeof(int)) > data.Length) throw new ArgumentOutOfRangeException("data.Length is too small");
        fixed (byte* ptr = data)
            *((int*)(ptr + offset)) = BitConverter.IsLittleEndian != littleEndian ? BinaryPrimitives.ReverseEndianness(value) : value;
    }

    public static unsafe void _SetSInt32(this byte[] data, int value, int offset = 0, bool littleEndian = false)
    {
        if (offset < 0) throw new ArgumentOutOfRangeException("offset < 0");
        if (checked(offset + sizeof(int)) > data.Length) throw new ArgumentOutOfRangeException("data.Length is too small");
        fixed (byte* ptr = data)
            *((int*)(ptr + offset)) = BitConverter.IsLittleEndian != littleEndian ? BinaryPrimitives.ReverseEndianness(value) : value;
    }

    public static unsafe void _SetUInt64(this ulong value, byte[] data, int offset = 0, bool littleEndian = false)
    {
        if (offset < 0) throw new ArgumentOutOfRangeException("offset < 0");
        if (checked(offset + sizeof(ulong)) > data.Length) throw new ArgumentOutOfRangeException("data.Length is too small");
        fixed (byte* ptr = data)
            *((ulong*)(ptr + offset)) = BitConverter.IsLittleEndian != littleEndian ? BinaryPrimitives.ReverseEndianness(value) : value;
    }

    public static unsafe void _SetUInt64(this byte[] data, ulong value, int offset = 0, bool littleEndian = false)
    {
        if (offset < 0) throw new ArgumentOutOfRangeException("offset < 0");
        if (checked(offset + sizeof(ulong)) > data.Length) throw new ArgumentOutOfRangeException("data.Length is too small");
        fixed (byte* ptr = data)
            *((ulong*)(ptr + offset)) = BitConverter.IsLittleEndian != littleEndian ? BinaryPrimitives.ReverseEndianness(value) : value;
    }

    public static unsafe void _SetSInt64(this long value, byte[] data, int offset = 0, bool littleEndian = false)
    {
        if (offset < 0) throw new ArgumentOutOfRangeException("offset < 0");
        if (checked(offset + sizeof(long)) > data.Length) throw new ArgumentOutOfRangeException("data.Length is too small");
        fixed (byte* ptr = data)
            *((long*)(ptr + offset)) = BitConverter.IsLittleEndian != littleEndian ? BinaryPrimitives.ReverseEndianness(value) : value;
    }

    public static unsafe void _SetSInt64(this byte[] data, long value, int offset = 0, bool littleEndian = false)
    {
        if (offset < 0) throw new ArgumentOutOfRangeException("offset < 0");
        if (checked(offset + sizeof(long)) > data.Length) throw new ArgumentOutOfRangeException("data.Length is too small");
        fixed (byte* ptr = data)
            *((long*)(ptr + offset)) = BitConverter.IsLittleEndian != littleEndian ? BinaryPrimitives.ReverseEndianness(value) : value;
    }

    public static unsafe void _SetBool8(this bool value, Span<byte> span)
    {
        span[0] = (byte)(value ? 1 : 0);
    }

    public static unsafe void _SetBool8(this Span<byte> span, bool value)
    {
        span[0] = (byte)(value ? 1 : 0);
    }

    public static unsafe void _SetUInt8(this byte value, Span<byte> span)
    {
        span[0] = (byte)value;
    }

    public static unsafe void _SetUInt8(this Span<byte> span, byte value)
    {
        span[0] = (byte)value;
    }

    public static unsafe void _SetSInt8(this sbyte value, Span<byte> span)
    {
        span[0] = (byte)value;
    }

    public static unsafe void _SetSInt8(this Span<byte> span, sbyte value)
    {
        span[0] = (byte)value;
    }

    public static unsafe void _SetUInt16(this ushort value, Span<byte> span, bool littleEndian = false)
    {
        if (span.Length < sizeof(ushort)) throw new ArgumentOutOfRangeException("span.Length is too small");
        fixed (byte* ptr = span)
            *((ushort*)(ptr)) = BitConverter.IsLittleEndian != littleEndian ? BinaryPrimitives.ReverseEndianness(value) : value;
    }

    public static unsafe void _SetUInt16(this Span<byte> span, ushort value, bool littleEndian = false)
    {
        if (span.Length < sizeof(ushort)) throw new ArgumentOutOfRangeException("span.Length is too small");
        fixed (byte* ptr = span)
            *((ushort*)(ptr)) = BitConverter.IsLittleEndian != littleEndian ? BinaryPrimitives.ReverseEndianness(value) : value;
    }

    public static unsafe void _SetSInt16(this short value, Span<byte> span, bool littleEndian = false)
    {
        if (span.Length < sizeof(short)) throw new ArgumentOutOfRangeException("span.Length is too small");
        fixed (byte* ptr = span)
            *((short*)(ptr)) = BitConverter.IsLittleEndian != littleEndian ? BinaryPrimitives.ReverseEndianness(value) : value;
    }

    public static unsafe void _SetSInt16(this Span<byte> span, short value, bool littleEndian = false)
    {
        if (span.Length < sizeof(short)) throw new ArgumentOutOfRangeException("span.Length is too small");
        fixed (byte* ptr = span)
            *((short*)(ptr)) = BitConverter.IsLittleEndian != littleEndian ? BinaryPrimitives.ReverseEndianness(value) : value;
    }

    public static unsafe void _SetUInt32(this uint value, Span<byte> span, bool littleEndian = false)
    {
        if (span.Length < sizeof(uint)) throw new ArgumentOutOfRangeException("span.Length is too small");
        fixed (byte* ptr = span)
            *((uint*)(ptr)) = BitConverter.IsLittleEndian != littleEndian ? BinaryPrimitives.ReverseEndianness(value) : value;
    }

    public static unsafe void _SetUInt32(this Span<byte> span, uint value, bool littleEndian = false)
    {
        if (span.Length < sizeof(uint)) throw new ArgumentOutOfRangeException("span.Length is too small");
        fixed (byte* ptr = span)
            *((uint*)(ptr)) = BitConverter.IsLittleEndian != littleEndian ? BinaryPrimitives.ReverseEndianness(value) : value;
    }

    public static unsafe void _SetSInt32(this int value, Span<byte> span, bool littleEndian = false)
    {
        if (span.Length < sizeof(int)) throw new ArgumentOutOfRangeException("span.Length is too small");
        fixed (byte* ptr = span)
            *((int*)(ptr)) = BitConverter.IsLittleEndian != littleEndian ? BinaryPrimitives.ReverseEndianness(value) : value;
    }

    public static unsafe void _SetSInt32(this Span<byte> span, int value, bool littleEndian = false)
    {
        if (span.Length < sizeof(int)) throw new ArgumentOutOfRangeException("span.Length is too small");
        fixed (byte* ptr = span)
            *((int*)(ptr)) = BitConverter.IsLittleEndian != littleEndian ? BinaryPrimitives.ReverseEndianness(value) : value;
    }

    public static unsafe void _SetUInt64(this ulong value, Span<byte> span, bool littleEndian = false)
    {
        if (span.Length < sizeof(ulong)) throw new ArgumentOutOfRangeException("span.Length is too small");
        fixed (byte* ptr = span)
            *((ulong*)(ptr)) = BitConverter.IsLittleEndian != littleEndian ? BinaryPrimitives.ReverseEndianness(value) : value;
    }

    public static unsafe void _SetUInt64(this Span<byte> span, ulong value, bool littleEndian = false)
    {
        if (span.Length < sizeof(ulong)) throw new ArgumentOutOfRangeException("span.Length is too small");
        fixed (byte* ptr = span)
            *((ulong*)(ptr)) = BitConverter.IsLittleEndian != littleEndian ? BinaryPrimitives.ReverseEndianness(value) : value;
    }

    public static unsafe void _SetSInt64(this long value, Span<byte> span, bool littleEndian = false)
    {
        if (span.Length < sizeof(long)) throw new ArgumentOutOfRangeException("span.Length is too small");
        fixed (byte* ptr = span)
            *((long*)(ptr)) = BitConverter.IsLittleEndian != littleEndian ? BinaryPrimitives.ReverseEndianness(value) : value;
    }

    public static unsafe void _SetSInt64(this Span<byte> span, long value, bool littleEndian = false)
    {
        if (span.Length < sizeof(long)) throw new ArgumentOutOfRangeException("span.Length is too small");
        fixed (byte* ptr = span)
            *((long*)(ptr)) = BitConverter.IsLittleEndian != littleEndian ? BinaryPrimitives.ReverseEndianness(value) : value;
    }

    public static unsafe void _SetBool8(this bool value, Memory<byte> memory)
    {
        memory.Span[0] = (byte)(value ? 1 : 0);
    }

    public static unsafe void _SetBool8(this Memory<byte> memory, bool value)
    {
        memory.Span[0] = (byte)(value ? 1 : 0);
    }

    public static unsafe void _SetUInt8(this byte value, Memory<byte> memory)
    {
        memory.Span[0] = (byte)value;
    }

    public static unsafe void _SetUInt8(this Memory<byte> memory, byte value)
    {
        memory.Span[0] = (byte)value;
    }

    public static unsafe void _SetSInt8(this sbyte value, Memory<byte> memory)
    {
        memory.Span[0] = (byte)value;
    }

    public static unsafe void _SetSInt8(this Memory<byte> memory, sbyte value)
    {
        memory.Span[0] = (byte)value;
    }

    public static unsafe void _SetUInt16(this ushort value, Memory<byte> memory, bool littleEndian = false)
    {
        if (memory.Length < sizeof(ushort)) throw new ArgumentOutOfRangeException("memory.Length is too small");
        fixed (byte* ptr = memory.Span)
            *((ushort*)(ptr)) = BitConverter.IsLittleEndian != littleEndian ? BinaryPrimitives.ReverseEndianness(value) : value;
    }

    public static unsafe void _SetUInt16(this Memory<byte> memory, ushort value, bool littleEndian = false)
    {
        if (memory.Length < sizeof(ushort)) throw new ArgumentOutOfRangeException("memory.Length is too small");
        fixed (byte* ptr = memory.Span)
            *((ushort*)(ptr)) = BitConverter.IsLittleEndian != littleEndian ? BinaryPrimitives.ReverseEndianness(value) : value;
    }

    public static unsafe void _SetSInt16(this short value, Memory<byte> memory, bool littleEndian = false)
    {
        if (memory.Length < sizeof(short)) throw new ArgumentOutOfRangeException("memory.Length is too small");
        fixed (byte* ptr = memory.Span)
            *((short*)(ptr)) = BitConverter.IsLittleEndian != littleEndian ? BinaryPrimitives.ReverseEndianness(value) : value;
    }

    public static unsafe void _SetSInt16(this Memory<byte> memory, short value, bool littleEndian = false)
    {
        if (memory.Length < sizeof(short)) throw new ArgumentOutOfRangeException("memory.Length is too small");
        fixed (byte* ptr = memory.Span)
            *((short*)(ptr)) = BitConverter.IsLittleEndian != littleEndian ? BinaryPrimitives.ReverseEndianness(value) : value;
    }

    public static unsafe void _SetUInt32(this uint value, Memory<byte> memory, bool littleEndian = false)
    {
        if (memory.Length < sizeof(uint)) throw new ArgumentOutOfRangeException("memory.Length is too small");
        fixed (byte* ptr = memory.Span)
            *((uint*)(ptr)) = BitConverter.IsLittleEndian != littleEndian ? BinaryPrimitives.ReverseEndianness(value) : value;
    }

    public static unsafe void _SetUInt32(this Memory<byte> memory, uint value, bool littleEndian = false)
    {
        if (memory.Length < sizeof(uint)) throw new ArgumentOutOfRangeException("memory.Length is too small");
        fixed (byte* ptr = memory.Span)
            *((uint*)(ptr)) = BitConverter.IsLittleEndian != littleEndian ? BinaryPrimitives.ReverseEndianness(value) : value;
    }

    public static unsafe void _SetSInt32(this int value, Memory<byte> memory, bool littleEndian = false)
    {
        if (memory.Length < sizeof(int)) throw new ArgumentOutOfRangeException("memory.Length is too small");
        fixed (byte* ptr = memory.Span)
            *((int*)(ptr)) = BitConverter.IsLittleEndian != littleEndian ? BinaryPrimitives.ReverseEndianness(value) : value;
    }

    public static unsafe void _SetSInt32(this Memory<byte> memory, int value, bool littleEndian = false)
    {
        if (memory.Length < sizeof(int)) throw new ArgumentOutOfRangeException("memory.Length is too small");
        fixed (byte* ptr = memory.Span)
            *((int*)(ptr)) = BitConverter.IsLittleEndian != littleEndian ? BinaryPrimitives.ReverseEndianness(value) : value;
    }

    public static unsafe void _SetUInt64(this ulong value, Memory<byte> memory, bool littleEndian = false)
    {
        if (memory.Length < sizeof(ulong)) throw new ArgumentOutOfRangeException("memory.Length is too small");
        fixed (byte* ptr = memory.Span)
            *((ulong*)(ptr)) = BitConverter.IsLittleEndian != littleEndian ? BinaryPrimitives.ReverseEndianness(value) : value;
    }

    public static unsafe void _SetUInt64(this Memory<byte> memory, ulong value, bool littleEndian = false)
    {
        if (memory.Length < sizeof(ulong)) throw new ArgumentOutOfRangeException("memory.Length is too small");
        fixed (byte* ptr = memory.Span)
            *((ulong*)(ptr)) = BitConverter.IsLittleEndian != littleEndian ? BinaryPrimitives.ReverseEndianness(value) : value;
    }

    public static unsafe void _SetSInt64(this long value, Memory<byte> memory, bool littleEndian = false)
    {
        if (memory.Length < sizeof(long)) throw new ArgumentOutOfRangeException("memory.Length is too small");
        fixed (byte* ptr = memory.Span)
            *((long*)(ptr)) = BitConverter.IsLittleEndian != littleEndian ? BinaryPrimitives.ReverseEndianness(value) : value;
    }

    public static unsafe void _SetSInt64(this Memory<byte> memory, long value, bool littleEndian = false)
    {
        if (memory.Length < sizeof(long)) throw new ArgumentOutOfRangeException("memory.Length is too small");
        fixed (byte* ptr = memory.Span)
            *((long*)(ptr)) = BitConverter.IsLittleEndian != littleEndian ? BinaryPrimitives.ReverseEndianness(value) : value;
    }


    public static unsafe byte[] _GetBool8(this bool value)
    {
        byte[] data = new byte[1];
        data[0] = (byte)(value ? 1 : 0);
        return data;
    }

    public static unsafe byte[] _GetUInt8(this byte value)
    {
        byte[] data = new byte[1];
        data[0] = (byte)value;
        return data;
    }

    public static unsafe byte[] _GetSInt8(this sbyte value)
    {
        byte[] data = new byte[1];
        data[0] = (byte)value;
        return data;
    }

    public static unsafe byte[] _GetUInt16(this ushort value, bool littleEndian = false)
    {
        byte[] data = new byte[2];
        fixed (byte* ptr = data)
            *((ushort*)(ptr)) = BitConverter.IsLittleEndian != littleEndian ? BinaryPrimitives.ReverseEndianness(value) : value;
        return data;
    }

    public static unsafe byte[] _GetSInt16(this short value, bool littleEndian = false)
    {
        byte[] data = new byte[2];
        fixed (byte* ptr = data)
            *((short*)(ptr)) = BitConverter.IsLittleEndian != littleEndian ? BinaryPrimitives.ReverseEndianness(value) : value;
        return data;
    }

    public static unsafe byte[] _GetUInt32(this uint value, bool littleEndian = false)
    {
        byte[] data = new byte[4];
        fixed (byte* ptr = data)
            *((uint*)(ptr)) = BitConverter.IsLittleEndian != littleEndian ? BinaryPrimitives.ReverseEndianness(value) : value;
        return data;
    }

    public static unsafe byte[] _GetSInt32(this int value, bool littleEndian = false)
    {
        byte[] data = new byte[4];
        fixed (byte* ptr = data)
            *((int*)(ptr)) = BitConverter.IsLittleEndian != littleEndian ? BinaryPrimitives.ReverseEndianness(value) : value;
        return data;
    }

    public static unsafe byte[] _GetUInt64(this ulong value, bool littleEndian = false)
    {
        byte[] data = new byte[8];
        fixed (byte* ptr = data)
            *((ulong*)(ptr)) = BitConverter.IsLittleEndian != littleEndian ? BinaryPrimitives.ReverseEndianness(value) : value;
        return data;
    }

    public static unsafe byte[] _GetSInt64(this long value, bool littleEndian = false)
    {
        byte[] data = new byte[8];
        fixed (byte* ptr = data)
            *((long*)(ptr)) = BitConverter.IsLittleEndian != littleEndian ? BinaryPrimitives.ReverseEndianness(value) : value;
        return data;
    }
    #endregion

    public static void _WalkWrite<T>(ref this Span<T> span, ReadOnlySpan<T> data) => data.CopyTo(span._Walk(data.Length));

    public static ReadOnlySpan<T> _Walk<T>(ref this ReadOnlySpan<T> span, int size)
    {
        if (size == 0) return Span<T>.Empty;
        if (size < 0) throw new ArgumentOutOfRangeException("size");
        var original = span;
        span = span.Slice(size);
        return original.Slice(0, size);
    }

    public static ReadOnlySpan<T> _TryWalk<T>(ref this ReadOnlySpan<T> span, int size)
    {
        if (span.Length < size) return default;
        return _Walk(ref span, size);
    }

    public static unsafe bool _TryWalkAsStruct<T>(ref this ReadOnlySpan<byte> span, out T dst) where T : unmanaged
    {
        int structSize = sizeof(T);
        if (structSize == 0)
        {
            dst = default;
            return default;
        }

        ReadOnlySpan<byte> structSpan = _TryWalk(ref span, structSize);
        if (structSpan.IsEmpty)
        {
            dst = default;
            return false;
        }

        dst = structSpan._AsStruct<T>(); // コピー発生

        return true;
    }


    public static Span<T> _TryWalk<T>(ref this Span<T> span, int size)
    {
        if (span.Length < size) return default;
        return _Walk(ref span, size);
    }

    public static unsafe bool _TryWalkAsStruct<T>(ref this Span<byte> span, out T dst) where T : unmanaged
    {
        int structSize = sizeof(T);
        if (structSize == 0)
        {
            dst = default;
            return default;
        }

        Span<byte> structSpan = _TryWalk(ref span, structSize);
        if (structSpan.IsEmpty)
        {
            dst = default;
            return false;
        }

        dst = structSpan._AsStruct<T>(); // コピー発生

        return true;
    }


    public static Span<T> _Walk<T>(ref this Span<T> span, int size)
    {
        if (size == 0) return Span<T>.Empty;
        if (size < 0) throw new ArgumentOutOfRangeException("size");
        var original = span;
        span = span.Slice(size);
        return original.Slice(0, size);
    }

    public static void _WalkWriteBool8(ref this Span<byte> span, bool value) => value._SetBool8(span._Walk(1));
    public static void _WalkWriteUInt8(ref this Span<byte> span, byte value) => value._SetUInt8(span._Walk(1));
    public static void _WalkWriteUInt16(ref this Span<byte> span, ushort value, bool littleEndian = false) => value._SetUInt16(span._Walk(2), littleEndian);
    public static void _WalkWriteUInt32(ref this Span<byte> span, uint value, bool littleEndian = false) => value._SetUInt32(span._Walk(4), littleEndian);
    public static void _WalkWriteUInt64(ref this Span<byte> span, ulong value, bool littleEndian = false) => value._SetUInt64(span._Walk(8), littleEndian);
    public static void _WalkWriteSInt8(ref this Span<byte> span, sbyte value) => value._SetSInt8(span._Walk(1));
    public static void _WalkWriteSInt16(ref this Span<byte> span, short value, bool littleEndian = false) => value._SetSInt16(span._Walk(2), littleEndian);
    public static void _WalkWriteSInt32(ref this Span<byte> span, int value, bool littleEndian = false) => value._SetSInt32(span._Walk(4), littleEndian);
    public static void _WalkWriteSInt64(ref this Span<byte> span, long value, bool littleEndian = false) => value._SetSInt64(span._Walk(8), littleEndian);

    public static Span<T> _WalkRead<T>(ref this Span<T> span, int size) => span._Walk(size);

    public static ReadOnlySpan<T> _WalkRead<T>(ref this ReadOnlySpan<T> span, int size) => span._Walk(size);

    public static bool _WalkReadBool8(ref this Span<byte> span) => span._WalkRead(1)._GetBool8();
    public static byte _WalkReadUInt8(ref this Span<byte> span) => span._WalkRead(1)._GetUInt8();
    public static ushort _WalkReadUInt16(ref this Span<byte> span, bool littleEndian = false) => span._WalkRead(2)._GetUInt16(littleEndian);
    public static uint _WalkReadUInt32(ref this Span<byte> span, bool littleEndian = false) => span._WalkRead(4)._GetUInt32(littleEndian);
    public static ulong _WalkReadUInt64(ref this Span<byte> span, bool littleEndian = false) => span._WalkRead(8)._GetUInt64(littleEndian);
    public static sbyte _WalkReadSInt8(ref this Span<byte> span) => span._WalkRead(1)._GetSInt8();
    public static short _WalkReadSInt16(ref this Span<byte> span, bool littleEndian = false) => span._WalkRead(2)._GetSInt16(littleEndian);
    public static int _WalkReadSInt32(ref this Span<byte> span, bool littleEndian = false) => span._WalkRead(4)._GetSInt32(littleEndian);
    public static long _WalkReadSInt64(ref this Span<byte> span, bool littleEndian = false) => span._WalkRead(8)._GetSInt64(littleEndian);

    public static bool _WalkReadBool8(ref this ReadOnlySpan<byte> span) => span._WalkRead(1)._GetBool8();
    public static byte _WalkReadUInt8(ref this ReadOnlySpan<byte> span) => span._WalkRead(1)._GetUInt8();
    public static ushort _WalkReadUInt16(ref this ReadOnlySpan<byte> span, bool littleEndian = false) => span._WalkRead(2)._GetUInt16(littleEndian);
    public static uint _WalkReadUInt32(ref this ReadOnlySpan<byte> span, bool littleEndian = false) => span._WalkRead(4)._GetUInt32(littleEndian);
    public static ulong _WalkReadUInt64(ref this ReadOnlySpan<byte> span, bool littleEndian = false) => span._WalkRead(8)._GetUInt64(littleEndian);
    public static sbyte _WalkReadSInt8(ref this ReadOnlySpan<byte> span) => span._WalkRead(1)._GetSInt8();
    public static short _WalkReadSInt16(ref this ReadOnlySpan<byte> span, bool littleEndian = false) => span._WalkRead(2)._GetSInt16(littleEndian);
    public static int _WalkReadSInt32(ref this ReadOnlySpan<byte> span, bool littleEndian = false) => span._WalkRead(4)._GetSInt32(littleEndian);
    public static long _WalkReadSInt64(ref this ReadOnlySpan<byte> span, bool littleEndian = false) => span._WalkRead(8)._GetSInt64(littleEndian);

    public static Memory<T> _Walk<T>(ref this Memory<T> memory, int size)
    {
        if (size == 0) return Memory<T>.Empty;
        if (size < 0) throw new ArgumentOutOfRangeException("size");
        var original = memory;
        memory = memory.Slice(size);
        return original.Slice(0, size);
    }

    public static ReadOnlyMemory<T> _Walk<T>(ref this ReadOnlyMemory<T> memory, int size)
    {
        if (size == 0) return ReadOnlyMemory<T>.Empty;
        if (size < 0) throw new ArgumentOutOfRangeException("size");
        var original = memory;
        memory = memory.Slice(size);
        return original.Slice(0, size);
    }

    public static int _WalkGetPin<T>(this Memory<T> memory) => _WalkGetPin(memory._AsReadOnlyMemory());
    public static int _WalkGetPin<T>(this ReadOnlyMemory<T> memory) => memory._AsSegment().Offset;

    public static int _WalkGetCurrentLength<T>(this Memory<T> memory, int compareTargetPin) => _WalkGetCurrentLength(memory._AsReadOnlyMemory(), compareTargetPin);

    public static int _WalkGetCurrentLength<T>(this ReadOnlyMemory<T> memory, int compareTargetPin)
    {
        int currentPin = memory._WalkGetPin();
        if (currentPin < compareTargetPin) throw new ArgumentOutOfRangeException("currentPin < compareTargetPin");
        return currentPin - compareTargetPin;
    }

    public static Memory<T> _SliceWithPin<T>(this Memory<T> memory, int pin, int size = DefaultSize)
    {
        if (size == 0) return Memory<T>.Empty;
        if (pin < 0) throw new ArgumentOutOfRangeException("pin");

        ArraySegment<T> a = memory._AsSegment();
        if (size == DefaultSize)
        {
            size = a.Offset + a.Count - pin;
        }
        if (size < 0) throw new ArgumentOutOfRangeException("size");
        if ((a.Offset + a.Count) < pin)
        {
            throw new ArgumentOutOfRangeException("(a.Offset + a.Count) < pin");
        }
        if ((a.Offset + a.Count) < (pin + size))
        {
            throw new ArgumentOutOfRangeException("(a.Offset + a.Count) < (pin + size)");
        }

        ArraySegment<T> b = new ArraySegment<T>(a.Array!, pin, size);
        return b.AsMemory();
    }

    public static ReadOnlyMemory<T> _SliceWithPin<T>(this ReadOnlyMemory<T> memory, int pin, int size = DefaultSize)
    {
        if (size == 0) return Memory<T>.Empty;
        if (pin < 0) throw new ArgumentOutOfRangeException("pin");

        ArraySegment<T> a = memory._AsSegment();
        if (size == DefaultSize)
        {
            size = a.Offset + a.Count - pin;
        }
        if (size < 0) throw new ArgumentOutOfRangeException("size");
        if ((a.Offset + a.Count) < pin)
        {
            throw new ArgumentOutOfRangeException("(a.Offset + a.Count) < pin");
        }
        if ((a.Offset + a.Count) < (pin + size))
        {
            throw new ArgumentOutOfRangeException("(a.Offset + a.Count) < (pin + size)");
        }

        ArraySegment<T> b = new ArraySegment<T>(a.Array!, pin, size);
        return b.AsMemory();
    }

    public static void _WalkAutoRynamicEnsureReserveBuffer<T>(ref this Memory<T> memory, int size) => memory._WalkAutoInternal(size, false, true);
    public static Memory<T> _WalkAutoDynamic<T>(ref this Memory<T> memory, int size) => memory._WalkAutoInternal(size, false, false);
    public static Memory<T> _WalkAutoStatic<T>(ref this Memory<T> memory, int size) => memory._WalkAutoInternal(size, true, false);

    static Memory<T> _WalkAutoInternal<T>(ref this Memory<T> memory, int size, bool noReAlloc, bool noStep)
    {
        if (size == 0) return Memory<T>.Empty;
        if (size < 0) throw new ArgumentOutOfRangeException("size");
        if (memory.Length >= size)
        {
            return memory._Walk(size);
        }

        if (((long)memory.Length + (long)size) > int.MaxValue) throw new OverflowException("size");

        ArraySegment<T> a = memory._AsSegment();
        long requiredLen = (long)a.Offset + (long)a.Count + (long)size;
        if (requiredLen > int.MaxValue) throw new OverflowException("size");

        int newLen = a.Array!.Length;
        while (newLen < requiredLen)
        {
            newLen = (int)Math.Min(Math.Max((long)newLen, 128) * 2, int.MaxValue);
        }

        T[] newArray = a.Array;
        if (newArray.Length < newLen)
        {
            if (noReAlloc)
            {
                throw new ArgumentOutOfRangeException("Internal byte array overflow: array.Length < newLen");
            }
            newArray = a.Array._ReAlloc(newLen);
        }

        if (noStep == false)
        {
            a = new ArraySegment<T>(newArray, a.Offset, Math.Max(a.Count, size));
        }
        else
        {
            a = new ArraySegment<T>(newArray, a.Offset, a.Count);
        }

        var m = a.AsMemory();

        if (noStep == false)
        {
            var ret = m._Walk(size);
            memory = m;
            return ret;
        }
        else
        {
            memory = m;
            return Memory<T>.Empty;
        }
    }

    public static void _WalkWriteBool8(ref this Memory<byte> memory, bool value) => value._SetBool8(memory._Walk(1));
    public static void _WalkWriteUInt8(ref this Memory<byte> memory, byte value) => value._SetUInt8(memory._Walk(1));
    public static void _WalkWriteUInt16(ref this Memory<byte> memory, ushort value, bool littleEndian = false) => value._SetUInt16(memory._Walk(2), littleEndian);
    public static void _WalkWriteUInt32(ref this Memory<byte> memory, uint value, bool littleEndian = false) => value._SetUInt32(memory._Walk(4), littleEndian);
    public static void _WalkWriteUInt64(ref this Memory<byte> memory, ulong value, bool littleEndian = false) => value._SetUInt64(memory._Walk(8), littleEndian);
    public static void _WalkWriteSInt8(ref this Memory<byte> memory, sbyte value) => value._SetSInt8(memory._Walk(1));
    public static void _WalkWriteSInt16(ref this Memory<byte> memory, short value, bool littleEndian = false) => value._SetSInt16(memory._Walk(2), littleEndian);
    public static void _WalkWriteSInt32(ref this Memory<byte> memory, int value, bool littleEndian = false) => value._SetSInt32(memory._Walk(4), littleEndian);
    public static void _WalkWriteSInt64(ref this Memory<byte> memory, long value, bool littleEndian = false) => value._SetSInt64(memory._Walk(8), littleEndian);
    public static void _WalkWrite<T>(ref this Memory<T> memory, ReadOnlyMemory<T> data) => data.CopyTo(memory._Walk(data.Length));
    public static void _WalkWrite<T>(ref this Memory<T> memory, ReadOnlySpan<T> data) => data.CopyTo(memory._Walk(data.Length).Span);
    public static void _WalkWrite<T>(ref this Memory<T> memory, T[] data) => data.CopyTo(memory._Walk(data.Length).Span);

    public static void _WalkAutoDynamicWriteBool8(ref this Memory<byte> memory, bool value) => value._SetBool8(memory._WalkAutoDynamic(1));
    public static void _WalkAutoDynamicWriteUInt8(ref this Memory<byte> memory, byte value) => value._SetUInt8(memory._WalkAutoDynamic(1));
    public static void _WalkAutoDynamicWriteUInt16(ref this Memory<byte> memory, ushort value, bool littleEndian = false) => value._SetUInt16(memory._WalkAutoDynamic(2), littleEndian);
    public static void _WalkAutoDynamicWriteUInt32(ref this Memory<byte> memory, uint value, bool littleEndian = false) => value._SetUInt32(memory._WalkAutoDynamic(4), littleEndian);
    public static void _WalkAutoDynamicWriteUInt64(ref this Memory<byte> memory, ulong value, bool littleEndian = false) => value._SetUInt64(memory._WalkAutoDynamic(8), littleEndian);
    public static void _WalkAutoDynamicWriteSInt8(ref this Memory<byte> memory, sbyte value) => value._SetSInt8(memory._WalkAutoDynamic(1));
    public static void _WalkAutoDynamicWriteSInt16(ref this Memory<byte> memory, short value, bool littleEndian = false) => value._SetSInt16(memory._WalkAutoDynamic(2), littleEndian);
    public static void _WalkAutoDynamicWriteSInt32(ref this Memory<byte> memory, int value, bool littleEndian = false) => value._SetSInt32(memory._WalkAutoDynamic(4), littleEndian);
    public static void _WalkAutoDynamicWriteSInt64(ref this Memory<byte> memory, long value, bool littleEndian = false) => value._SetSInt64(memory._WalkAutoDynamic(8), littleEndian);
    public static void _WalkAutoDynamicWrite<T>(ref this Memory<T> memory, Memory<T> data) => data.CopyTo(memory._WalkAutoDynamic(data.Length));
    public static void _WalkAutoDynamicWrite<T>(ref this Memory<T> memory, Span<T> data) => data.CopyTo(memory._WalkAutoDynamic(data.Length).Span);
    public static void _WalkAutoDynamicWrite<T>(ref this Memory<T> memory, T[] data) => data.CopyTo(memory._WalkAutoDynamic(data.Length).Span);

    public static void _WalkAutoStaticWriteBool8(ref this Memory<byte> memory, bool value) => value._SetBool8(memory._WalkAutoStatic(1));
    public static void _WalkAutoStaticWriteUInt8(ref this Memory<byte> memory, byte value) => value._SetUInt8(memory._WalkAutoStatic(1));
    public static void _WalkAutoStaticWriteUInt16(ref this Memory<byte> memory, ushort value, bool littleEndian = false) => value._SetUInt16(memory._WalkAutoStatic(2), littleEndian);
    public static void _WalkAutoStaticWriteUInt32(ref this Memory<byte> memory, uint value, bool littleEndian = false) => value._SetUInt32(memory._WalkAutoStatic(4), littleEndian);
    public static void _WalkAutoStaticWriteUInt64(ref this Memory<byte> memory, ulong value, bool littleEndian = false) => value._SetUInt64(memory._WalkAutoStatic(8), littleEndian);
    public static void _WalkAutoStaticWriteSInt8(ref this Memory<byte> memory, sbyte value) => value._SetSInt8(memory._WalkAutoStatic(1));
    public static void _WalkAutoStaticWriteSInt16(ref this Memory<byte> memory, short value, bool littleEndian = false) => value._SetSInt16(memory._WalkAutoStatic(2), littleEndian);
    public static void _WalkAutoStaticWriteSInt32(ref this Memory<byte> memory, int value, bool littleEndian = false) => value._SetSInt32(memory._WalkAutoStatic(4), littleEndian);
    public static void _WalkAutoStaticWriteSInt64(ref this Memory<byte> memory, long value, bool littleEndian = false) => value._SetSInt64(memory._WalkAutoStatic(8), littleEndian);
    public static void _WalkAutoStaticWrite<T>(ref this Memory<T> memory, Memory<T> data) => data.CopyTo(memory._WalkAutoStatic(data.Length));
    public static void _WalkAutoStaticWrite<T>(ref this Memory<T> memory, Span<T> data) => data.CopyTo(memory._WalkAutoStatic(data.Length).Span);
    public static void _WalkAutoStaticWrite<T>(ref this Memory<T> memory, T[] data) => data.CopyTo(memory._WalkAutoStatic(data.Length).Span);

    public static ReadOnlyMemory<T> _WalkRead<T>(ref this ReadOnlyMemory<T> memory, int size) => memory._Walk(size);
    public static Memory<T> _WalkRead<T>(ref this Memory<T> memory, int size) => memory._Walk(size);

    public static bool _WalkReadBool8(ref this Memory<byte> memory) => memory._WalkRead(1)._GetBool8();
    public static byte _WalkReadUInt8(ref this Memory<byte> memory) => memory._WalkRead(1)._GetUInt8();
    public static ushort _WalkReadUInt16(ref this Memory<byte> memory, bool littleEndian = false) => memory._WalkRead(2)._GetUInt16(littleEndian);
    public static uint _WalkReadUInt32(ref this Memory<byte> memory, bool littleEndian = false) => memory._WalkRead(4)._GetUInt32(littleEndian);
    public static ulong _WalkReadUInt64(ref this Memory<byte> memory, bool littleEndian = false) => memory._WalkRead(8)._GetUInt64(littleEndian);
    public static sbyte _WalkReadSInt8(ref this Memory<byte> memory) => memory._WalkRead(1)._GetSInt8();
    public static short _WalkReadSInt16(ref this Memory<byte> memory, bool littleEndian = false) => memory._WalkRead(2)._GetSInt16(littleEndian);
    public static int _WalkReadSInt32(ref this Memory<byte> memory, bool littleEndian = false) => memory._WalkRead(4)._GetSInt32(littleEndian);
    public static long _WalkReadSInt64(ref this Memory<byte> memory, bool littleEndian = false) => memory._WalkRead(8)._GetSInt64(littleEndian);

    public static bool _WalkReadBool8(ref this ReadOnlyMemory<byte> memory) => memory._WalkRead(1)._GetBool8();
    public static byte _WalkReadUInt8(ref this ReadOnlyMemory<byte> memory) => memory._WalkRead(1)._GetUInt8();
    public static ushort _WalkReadUInt16(ref this ReadOnlyMemory<byte> memory, bool littleEndian = false) => memory._WalkRead(2)._GetUInt16(littleEndian);
    public static uint _WalkReadUInt32(ref this ReadOnlyMemory<byte> memory, bool littleEndian = false) => memory._WalkRead(4)._GetUInt32(littleEndian);
    public static ulong _WalkReadUInt64(ref this ReadOnlyMemory<byte> memory, bool littleEndian = false) => memory._WalkRead(8)._GetUInt64(littleEndian);
    public static sbyte _WalkReadSInt8(ref this ReadOnlyMemory<byte> memory) => memory._WalkRead(1)._GetSInt8();
    public static short _WalkReadSInt16(ref this ReadOnlyMemory<byte> memory, bool littleEndian = false) => memory._WalkRead(2)._GetSInt16(littleEndian);
    public static int _WalkReadSInt32(ref this ReadOnlyMemory<byte> memory, bool littleEndian = false) => memory._WalkRead(4)._GetSInt32(littleEndian);
    public static long _WalkReadSInt64(ref this ReadOnlyMemory<byte> memory, bool littleEndian = false) => memory._WalkRead(8)._GetSInt64(littleEndian);

    static Action InternalFastThrowVitalException = new Action(() => { throw new ApplicationException("Vital Error"); });
    public static void _FastThrowVitalError()
    {
        InternalFastThrowVitalException();
    }

    public static ArraySegment<T> _AsSegmentSlow<T>(this Memory<T> memory)
    {
        if (MemoryMarshal.TryGetArray(memory, out ArraySegment<T> seg) == false)
        {
            _FastThrowVitalError();
        }

        return seg;
    }

    public static ArraySegment<T> _AsSegmentSlow<T>(this ReadOnlyMemory<T> memory)
    {
        if (MemoryMarshal.TryGetArray(memory, out ArraySegment<T> seg) == false)
        {
            _FastThrowVitalError();
        }

        return seg;
    }

    public static T[] _ReAlloc<T>(this T[] src, int newSize)
    {
        if (newSize < 0) throw new ArgumentOutOfRangeException("newSize");
        if (newSize == src.Length)
        {
            return src;
        }

        T[] ret = src;
        Array.Resize(ref ret, newSize);
        return ret;
    }

    public static Span<T> _ReAlloc<T>(this Span<T> src, int newSize, int maxCopySize = int.MaxValue)
    {
        if (newSize < 0) throw new ArgumentOutOfRangeException("newSize");
        if (maxCopySize < 0) throw new ArgumentOutOfRangeException("maxCopySize");
        if (newSize == src.Length)
        {
            return src;
        }
        else
        {
            T[] ret = new T[newSize];
            int copySize = Math.Min(Math.Min(src.Length, ret.Length), maxCopySize);
            if (copySize >= 1)
                src.Slice(0, copySize).CopyTo(ret);
            return ret.AsSpan();
        }
    }

    public static Memory<T> _ReAlloc<T>(this Memory<T> src, int newSize, int maxCopySize = int.MaxValue)
    {
        if (newSize < 0) throw new ArgumentOutOfRangeException("newSize");
        if (maxCopySize < 0) throw new ArgumentOutOfRangeException("maxCopySize");
        if (newSize == src.Length)
        {
            return src;
        }
        else
        {
            T[] ret = new T[newSize];
            int copySize = Math.Min(Math.Min(src.Length, ret.Length), maxCopySize);
            if (copySize >= 1)
                src.Slice(0, copySize).CopyTo(ret);
            return ret.AsMemory();
        }
    }

    public static void _FastFree<T>(this T[] a)
    {
        if (a.Length >= Cores.Basic.MemoryHelper.MemoryUsePoolThreshold)
            ArrayPool<T>.Shared.Return(a);
    }

    public static void _FastFree<T>(this Memory<T> memory) => memory._GetInternalArray()._FastFree();

    public static T[] _GetInternalArray<T>(this Memory<T> memory)
    {
        unsafe
        {
            byte* ptr = (byte*)Unsafe.AsPointer(ref memory);
            ptr += Cores.Basic.MemoryHelper._MemoryObjectOffset;
            T[] o = Unsafe.Read<T[]>(ptr);
            return o;
        }
    }
    public static int _GetInternalArrayLength<T>(this Memory<T> memory) => _GetInternalArray(memory).Length;

    public static void _FastFree<T>(this ReadOnlyMemory<T> memory) => memory._GetInternalArray()._FastFree();

    public static T[] _GetInternalArray<T>(this ReadOnlyMemory<T> memory)
    {
        unsafe
        {
            byte* ptr = (byte*)Unsafe.AsPointer(ref memory);
            ptr += Cores.Basic.MemoryHelper._MemoryObjectOffset;
            T[] o = Unsafe.Read<T[]>(ptr);
            return o;
        }
    }
    public static int _GetInternalArrayLength<T>(this ReadOnlyMemory<T> memory) => _GetInternalArray(memory).Length;

    public static ArraySegment<T> _AsSegment<T>(this Memory<T> memory)
    {
        if (Cores.Basic.MemoryHelper._UseFast == false) return _AsSegmentSlow(memory);

        if (memory.IsEmpty) return new ArraySegment<T>();

        unsafe
        {
            byte* ptr = (byte*)Unsafe.AsPointer(ref memory);
            return new ArraySegment<T>(
                Unsafe.Read<T[]>(ptr + Cores.Basic.MemoryHelper._MemoryObjectOffset),
                *((int*)(ptr + Cores.Basic.MemoryHelper._MemoryIndexOffset)),
                *((int*)(ptr + Cores.Basic.MemoryHelper._MemoryLengthOffset))
                );
        }
    }

    public static ReadOnlyMemory<byte> _AsByteMemoryUnsafe<T>(this ReadOnlyMemory<T> memory)
    {
        var seg = memory._AsSegment();

        return new ReadOnlyMemory<byte>(Unsafe.As<byte[]>(seg.Array), seg.Offset, seg.Count);
    }

    public static Memory<byte> _AsByteMemoryUnsafe<T>(this Memory<T> memory)
    {
        var seg = memory._AsSegment();

        return new Memory<byte>(Unsafe.As<byte[]>(seg.Array), seg.Offset, seg.Count);
    }

    public static ArraySegment<T> _AsSegment<T>(this ReadOnlyMemory<T> memory)
    {
        if (Cores.Basic.MemoryHelper._UseFast == false) return _AsSegmentSlow(memory);

        if (memory.IsEmpty) return new ArraySegment<T>();

        unsafe
        {
            byte* ptr = (byte*)Unsafe.AsPointer(ref memory);
            return new ArraySegment<T>(
                Unsafe.Read<T[]>(ptr + Cores.Basic.MemoryHelper._MemoryObjectOffset),
                *((int*)(ptr + Cores.Basic.MemoryHelper._MemoryIndexOffset)),
                *((int*)(ptr + Cores.Basic.MemoryHelper._MemoryLengthOffset))
                );
        }
    }

    static MemoryStream ToMemoryStreamInternal(byte[] data)
    {
        if (data == null) data = new byte[0];

        var ms = new MemoryStream(data);

        ms._SeekToBegin();

        return ms;
    }

    public static MemoryStream _ToMemoryStream(this ReadOnlySpan<byte> span)
        => ToMemoryStreamInternal(span.ToArray());
    public static MemoryStream _ToMemoryStream(this Span<byte> span)
        => ToMemoryStreamInternal(span.ToArray());
    public static MemoryStream _ToMemoryStream(this ReadOnlyMemory<byte> span)
        => ToMemoryStreamInternal(span.ToArray());
    public static MemoryStream _ToMemoryStream(this Memory<byte> span)
        => ToMemoryStreamInternal(span.ToArray());

    [MethodImpl(Inline)]
    public static ref T _AsStruct<T>(this ref byte data) where T : unmanaged
        => ref Unsafe.As<byte, T>(ref data);

    [MethodImpl(Inline)]
    public static ref T _AsStruct<T>(this Span<byte> data) where T : unmanaged
        => ref Unsafe.As<byte, T>(ref data[0]);

    [MethodImpl(Inline)]
    public static unsafe ref readonly T _AsStruct<T>(this ReadOnlySpan<byte> data) where T : unmanaged
    {
        fixed (void* ptr = &data[0])
            return ref Unsafe.AsRef<T>(ptr);
    }

    [MethodImpl(Inline)]
    public static ref T _AsStruct<T>(this Memory<byte> data) where T : unmanaged
        => ref Unsafe.As<byte, T>(ref data.Span[0]);

    [MethodImpl(Inline)]
    public static unsafe ref readonly T _AsStruct<T>(this ReadOnlyMemory<byte> data) where T : unmanaged
    {
        fixed (void* ptr = &data.Span[0])
            return ref Unsafe.AsRef<T>(ptr);
    }

    [MethodImpl(Inline)]
    public static unsafe ref T _AsStructSafe<T>(this Span<byte> data, int minSize = 0) where T : unmanaged
    {
        if (minSize <= 0) minSize = sizeof(T);
        return ref Unsafe.As<byte, T>(ref data.Slice(0, minSize)[0]);
    }

    [MethodImpl(Inline)]
    public static unsafe ref readonly T _AsStructSafe<T>(this ReadOnlySpan<byte> data, int minSize = 0) where T : unmanaged
    {
        if (minSize <= 0) minSize = sizeof(T);
        fixed (void* ptr = &data.Slice(0, minSize)[0])
            return ref Unsafe.AsRef<T>(ptr);
    }

    [MethodImpl(Inline)]
    public static unsafe ref T _AsStructSafe<T>(this Memory<byte> data, int minSize = 0) where T : unmanaged
    {
        if (minSize <= 0) minSize = sizeof(T);
        return ref Unsafe.As<byte, T>(ref data.Span.Slice(0, minSize)[0]);
    }

    [MethodImpl(Inline)]
    public static unsafe ref readonly T _AsStructSafe<T>(this ReadOnlyMemory<byte> data, int minSize = 0) where T : unmanaged
    {
        if (minSize <= 0) minSize = sizeof(T);
        fixed (void* ptr = &data.Span.Slice(0, minSize)[0])
            return ref Unsafe.AsRef<T>(ptr);
    }






    [MethodImpl(Inline)]
    public static unsafe void _RawWriteValueSInt8<T>(this ref T target, sbyte value, long pointerOffset = 0) where T : unmanaged
    {
        int size = sizeof(T);
        byte* ptr = (byte*)(Unsafe.AsPointer(ref target)) + pointerOffset;
        if (size >= 1) *((sbyte*)ptr) = value;
    }

    [MethodImpl(Inline)]
    public static unsafe void _RawWriteValueSInt16<T>(this ref T target, short value, long pointerOffset = 0) where T : unmanaged
    {
        int size = sizeof(T);
        byte* ptr = (byte*)(Unsafe.AsPointer(ref target)) + pointerOffset;
        if (size >= 2) *((short*)ptr) = value;
        else if (size >= 1) *((byte*)ptr) = (byte)value;
    }

    [MethodImpl(Inline)]
    public static unsafe void _RawWriteValueSInt32<T>(this ref T target, int value, long pointerOffset = 0) where T : unmanaged
    {
        int size = sizeof(T);
        byte* ptr = (byte*)(Unsafe.AsPointer(ref target)) + pointerOffset;
        if (size >= 4) *((int*)ptr) = value;
        else if (size >= 2) *((ushort*)ptr) = (ushort)value;
        else if (size >= 1) *((byte*)ptr) = (byte)value;
    }

    [MethodImpl(Inline)]
    public static unsafe void _RawWriteValueSInt64<T>(this ref T target, long value, long pointerOffset = 0) where T : unmanaged
    {
        int size = sizeof(T);
        byte* ptr = (byte*)(Unsafe.AsPointer(ref target)) + pointerOffset;
        if (size >= 8) *((long*)ptr) = value;
        else if (size >= 4) *((uint*)ptr) = (uint)value;
        else if (size >= 2) *((ushort*)ptr) = (ushort)value;
        else if (size >= 1) *((byte*)ptr) = (byte)value;
    }

    [MethodImpl(Inline)]
    public static unsafe void _RawWriteValueUInt8<T>(this ref T target, byte value, long pointerOffset = 0) where T : unmanaged
    {
        int size = sizeof(T);
        byte* ptr = (byte*)(Unsafe.AsPointer(ref target)) + pointerOffset;
        if (size >= 1) *((byte*)ptr) = value;
    }

    [MethodImpl(Inline)]
    public static unsafe void _RawWriteValueUInt16<T>(this ref T target, ushort value, long pointerOffset = 0) where T : unmanaged
    {
        int size = sizeof(T);
        byte* ptr = (byte*)(Unsafe.AsPointer(ref target)) + pointerOffset;
        if (size >= 2) *((ushort*)ptr) = value;
        else if (size >= 1) *((byte*)ptr) = (byte)value;
    }

    [MethodImpl(Inline)]
    public static unsafe void _RawWriteValueUInt32<T>(this ref T target, uint value, long pointerOffset = 0) where T : unmanaged
    {
        int size = sizeof(T);
        byte* ptr = (byte*)(Unsafe.AsPointer(ref target)) + pointerOffset;
        if (size >= 4) *((uint*)ptr) = value;
        else if (size >= 2) *((ushort*)ptr) = (ushort)value;
        else if (size >= 1) *((byte*)ptr) = (byte)value;
    }

    [MethodImpl(Inline)]
    public static unsafe void _RawWriteValueUInt64<T>(this ref T target, ulong value, long pointerOffset = 0) where T : unmanaged
    {
        int size = sizeof(T);
        byte* ptr = (byte*)(Unsafe.AsPointer(ref target)) + pointerOffset;
        if (size >= 8) *((ulong*)ptr) = value;
        else if (size >= 4) *((uint*)ptr) = (uint)value;
        else if (size >= 2) *((ushort*)ptr) = (ushort)value;
        else if (size >= 1) *((byte*)ptr) = (byte)value;
    }






    [MethodImpl(Inline)]
    public static unsafe void _RawWriteValueSInt8<T>(this ReadOnlySpan<T> target, sbyte value) where T : unmanaged
    {
        int size = sizeof(T) * target.Length;
        ref T r = ref MemoryMarshal.GetReference(target);
        byte* ptr = (byte*)(Unsafe.AsPointer(ref r));
        if (size >= 1) *((sbyte*)ptr) = (sbyte)value;
    }
    [MethodImpl(Inline)]
    public static unsafe void _RawWriteValueSInt8<T>(this ReadOnlyMemory<T> target, sbyte value) where T : unmanaged
        => _RawWriteValueSInt8(target.Span, value);

    [MethodImpl(Inline)]
    public static unsafe void _RawWriteValueSInt16<T>(this ReadOnlySpan<T> target, short value) where T : unmanaged
    {
        int size = sizeof(T) * target.Length;
        ref T r = ref MemoryMarshal.GetReference(target);
        byte* ptr = (byte*)(Unsafe.AsPointer(ref r));
        if (size >= 2) *((short*)ptr) = (short)value;
        else if (size >= 1) *((sbyte*)ptr) = (sbyte)value;
    }
    [MethodImpl(Inline)]
    public static unsafe void _RawWriteValueSInt16<T>(this ReadOnlyMemory<T> target, short value) where T : unmanaged
        => _RawWriteValueSInt16(target.Span, value);

    [MethodImpl(Inline)]
    public static unsafe void _RawWriteValueSInt32<T>(this ReadOnlySpan<T> target, int value) where T : unmanaged
    {
        int size = sizeof(T) * target.Length;
        ref T r = ref MemoryMarshal.GetReference(target);
        byte* ptr = (byte*)(Unsafe.AsPointer(ref r));
        if (size >= 4) *((int*)ptr) = (int)value;
        else if (size >= 2) *((short*)ptr) = (short)value;
        else if (size >= 1) *((sbyte*)ptr) = (sbyte)value;
    }
    [MethodImpl(Inline)]
    public static unsafe void _RawWriteValueSInt32<T>(this ReadOnlyMemory<T> target, int value) where T : unmanaged
        => _RawWriteValueSInt32(target.Span, value);

    [MethodImpl(Inline)]
    public static unsafe void _RawWriteValueSInt64<T>(this ReadOnlySpan<T> target, long value) where T : unmanaged
    {
        int size = sizeof(T) * target.Length;
        ref T r = ref MemoryMarshal.GetReference(target);
        byte* ptr = (byte*)(Unsafe.AsPointer(ref r));
        if (size >= 8) *((long*)ptr) = value;
        else if (size >= 4) *((int*)ptr) = (int)value;
        else if (size >= 2) *((short*)ptr) = (short)value;
        else if (size >= 1) *((sbyte*)ptr) = (sbyte)value;
    }
    [MethodImpl(Inline)]
    public static unsafe void _RawWriteValueSInt64<T>(this ReadOnlyMemory<T> target, long value) where T : unmanaged
        => _RawWriteValueSInt64(target.Span, value);

    [MethodImpl(Inline)]
    public static unsafe void _RawWriteValueUInt8<T>(this ReadOnlySpan<T> target, byte value) where T : unmanaged
    {
        int size = sizeof(T) * target.Length;
        ref T r = ref MemoryMarshal.GetReference(target);
        byte* ptr = (byte*)(Unsafe.AsPointer(ref r));
        if (size >= 1) *((byte*)ptr) = (byte)value;
    }
    [MethodImpl(Inline)]
    public static unsafe void _RawWriteValueUInt8<T>(this ReadOnlyMemory<T> target, byte value) where T : unmanaged
        => _RawWriteValueUInt8(target.Span, value);

    [MethodImpl(Inline)]
    public static unsafe void _RawWriteValueUInt16<T>(this ReadOnlySpan<T> target, ushort value) where T : unmanaged
    {
        int size = sizeof(T) * target.Length;
        ref T r = ref MemoryMarshal.GetReference(target);
        byte* ptr = (byte*)(Unsafe.AsPointer(ref r));
        if (size >= 2) *((ushort*)ptr) = (ushort)value;
        else if (size >= 1) *((byte*)ptr) = (byte)value;
    }
    [MethodImpl(Inline)]
    public static unsafe void _RawWriteValueUInt16<T>(this ReadOnlyMemory<T> target, ushort value) where T : unmanaged
        => _RawWriteValueUInt16(target.Span, value);

    [MethodImpl(Inline)]
    public static unsafe void _RawWriteValueUInt32<T>(this ReadOnlySpan<T> target, uint value) where T : unmanaged
    {
        int size = sizeof(T) * target.Length;
        ref T r = ref MemoryMarshal.GetReference(target);
        byte* ptr = (byte*)(Unsafe.AsPointer(ref r));
        if (size >= 4) *((uint*)ptr) = (uint)value;
        else if (size >= 2) *((ushort*)ptr) = (ushort)value;
        else if (size >= 1) *((byte*)ptr) = (byte)value;
    }
    [MethodImpl(Inline)]
    public static unsafe void _RawWriteValueUInt32<T>(this ReadOnlyMemory<T> target, uint value) where T : unmanaged
        => _RawWriteValueUInt32(target.Span, value);

    [MethodImpl(Inline)]
    public static unsafe void _RawWriteValueUInt64<T>(this ReadOnlySpan<T> target, ulong value) where T : unmanaged
    {
        int size = sizeof(T) * target.Length;
        ref T r = ref MemoryMarshal.GetReference(target);
        byte* ptr = (byte*)(Unsafe.AsPointer(ref r));
        if (size >= 8) *((ulong*)ptr) = value;
        else if (size >= 4) *((uint*)ptr) = (uint)value;
        else if (size >= 2) *((ushort*)ptr) = (ushort)value;
        else if (size >= 1) *((byte*)ptr) = (byte)value;
    }
    [MethodImpl(Inline)]
    public static unsafe void _RawWriteValueUInt64<T>(this ReadOnlyMemory<T> target, ulong value) where T : unmanaged
        => _RawWriteValueUInt64(target.Span, value);



    [MethodImpl(Inline)]
    public static unsafe void _RawWriteValueSInt8<T>(this Span<T> target, sbyte value) where T : unmanaged
    {
        int size = sizeof(T) * target.Length;
        ref T r = ref MemoryMarshal.GetReference(target);
        byte* ptr = (byte*)(Unsafe.AsPointer(ref r));
        if (size >= 1) *((sbyte*)ptr) = (sbyte)value;
    }
    [MethodImpl(Inline)]
    public static unsafe void _RawWriteValueSInt8<T>(this Memory<T> target, sbyte value) where T : unmanaged
        => _RawWriteValueSInt8(target.Span, value);

    [MethodImpl(Inline)]
    public static unsafe void _RawWriteValueSInt16<T>(this Span<T> target, short value) where T : unmanaged
    {
        int size = sizeof(T) * target.Length;
        ref T r = ref MemoryMarshal.GetReference(target);
        byte* ptr = (byte*)(Unsafe.AsPointer(ref r));
        if (size >= 2) *((short*)ptr) = (short)value;
        else if (size >= 1) *((sbyte*)ptr) = (sbyte)value;
    }
    [MethodImpl(Inline)]
    public static unsafe void _RawWriteValueSInt16<T>(this Memory<T> target, short value) where T : unmanaged
        => _RawWriteValueSInt16(target.Span, value);

    [MethodImpl(Inline)]
    public static unsafe void _RawWriteValueSInt32<T>(this Span<T> target, int value) where T : unmanaged
    {
        int size = sizeof(T) * target.Length;
        ref T r = ref MemoryMarshal.GetReference(target);
        byte* ptr = (byte*)(Unsafe.AsPointer(ref r));
        if (size >= 4) *((int*)ptr) = (int)value;
        else if (size >= 2) *((short*)ptr) = (short)value;
        else if (size >= 1) *((sbyte*)ptr) = (sbyte)value;
    }
    [MethodImpl(Inline)]
    public static unsafe void _RawWriteValueSInt32<T>(this Memory<T> target, int value) where T : unmanaged
        => _RawWriteValueSInt32(target.Span, value);

    [MethodImpl(Inline)]
    public static unsafe void _RawWriteValueSInt64<T>(this Span<T> target, long value) where T : unmanaged
    {
        int size = sizeof(T) * target.Length;
        ref T r = ref MemoryMarshal.GetReference(target);
        byte* ptr = (byte*)(Unsafe.AsPointer(ref r));
        if (size >= 8) *((long*)ptr) = value;
        else if (size >= 4) *((int*)ptr) = (int)value;
        else if (size >= 2) *((short*)ptr) = (short)value;
        else if (size >= 1) *((sbyte*)ptr) = (sbyte)value;
    }
    [MethodImpl(Inline)]
    public static unsafe void _RawWriteValueSInt64<T>(this Memory<T> target, long value) where T : unmanaged
        => _RawWriteValueSInt64(target.Span, value);

    [MethodImpl(Inline)]
    public static unsafe void _RawWriteValueUInt8<T>(this Span<T> target, byte value) where T : unmanaged
    {
        int size = sizeof(T) * target.Length;
        ref T r = ref MemoryMarshal.GetReference(target);
        byte* ptr = (byte*)(Unsafe.AsPointer(ref r));
        if (size >= 1) *((byte*)ptr) = (byte)value;
    }
    [MethodImpl(Inline)]
    public static unsafe void _RawWriteValueUInt8<T>(this Memory<T> target, byte value) where T : unmanaged
        => _RawWriteValueUInt8(target.Span, value);

    [MethodImpl(Inline)]
    public static unsafe void _RawWriteValueUInt16<T>(this Span<T> target, ushort value) where T : unmanaged
    {
        int size = sizeof(T) * target.Length;
        ref T r = ref MemoryMarshal.GetReference(target);
        byte* ptr = (byte*)(Unsafe.AsPointer(ref r));
        if (size >= 2) *((ushort*)ptr) = (ushort)value;
        else if (size >= 1) *((byte*)ptr) = (byte)value;
    }
    [MethodImpl(Inline)]
    public static unsafe void _RawWriteValueUInt16<T>(this Memory<T> target, ushort value) where T : unmanaged
        => _RawWriteValueUInt16(target.Span, value);

    [MethodImpl(Inline)]
    public static unsafe void _RawWriteValueUInt32<T>(this Span<T> target, uint value) where T : unmanaged
    {
        int size = sizeof(T) * target.Length;
        ref T r = ref MemoryMarshal.GetReference(target);
        byte* ptr = (byte*)(Unsafe.AsPointer(ref r));
        if (size >= 4) *((uint*)ptr) = (uint)value;
        else if (size >= 2) *((ushort*)ptr) = (ushort)value;
        else if (size >= 1) *((byte*)ptr) = (byte)value;
    }
    [MethodImpl(Inline)]
    public static unsafe void _RawWriteValueUInt32<T>(this Memory<T> target, uint value) where T : unmanaged
        => _RawWriteValueUInt32(target.Span, value);

    [MethodImpl(Inline)]
    public static unsafe void _RawWriteValueUInt64<T>(this Span<T> target, ulong value) where T : unmanaged
    {
        int size = sizeof(T) * target.Length;
        ref T r = ref MemoryMarshal.GetReference(target);
        byte* ptr = (byte*)(Unsafe.AsPointer(ref r));
        if (size >= 8) *((ulong*)ptr) = value;
        else if (size >= 4) *((uint*)ptr) = (uint)value;
        else if (size >= 2) *((ushort*)ptr) = (ushort)value;
        else if (size >= 1) *((byte*)ptr) = (byte)value;
    }
    [MethodImpl(Inline)]
    public static unsafe void _RawWriteValueUInt64<T>(this Memory<T> target, ulong value) where T : unmanaged
        => _RawWriteValueUInt64(target.Span, value);




    [MethodImpl(Inline)]
    public static unsafe sbyte _RawReadValueSInt8<T>(this T target, long pointerOffset = 0) where T : unmanaged
    {
        int size = sizeof(T);
        byte* ptr = (byte*)(Unsafe.AsPointer(ref target)) + pointerOffset;
        if (size >= 1) return *((sbyte*)ptr);
        return 0;
    }

    [MethodImpl(Inline)]
    public static unsafe short _RawReadValueSInt16<T>(this T target, long pointerOffset = 0) where T : unmanaged
    {
        int size = sizeof(T);
        byte* ptr = (byte*)(Unsafe.AsPointer(ref target)) + pointerOffset;
        if (size >= 2) return *((short*)ptr);
        if (size >= 1) return *((byte*)ptr);
        return 0;
    }

    [MethodImpl(Inline)]
    public static unsafe int _RawReadValueSInt32<T>(this T target, long pointerOffset = 0) where T : unmanaged
    {
        int size = sizeof(T);
        byte* ptr = (byte*)(Unsafe.AsPointer(ref target)) + pointerOffset;
        if (size >= 4) return *((int*)ptr);
        if (size >= 2) return *((ushort*)ptr);
        if (size >= 1) return *((byte*)ptr);
        return 0;
    }

    [MethodImpl(Inline)]
    public static unsafe long _RawReadValueSInt64<T>(this T target, long pointerOffset = 0) where T : unmanaged
    {
        int size = sizeof(T);
        byte* ptr = (byte*)(Unsafe.AsPointer(ref target)) + pointerOffset;
        if (size >= 8) return *((long*)ptr);
        if (size >= 4) return *((uint*)ptr);
        if (size >= 2) return *((ushort*)ptr);
        if (size >= 1) return *((byte*)ptr);
        return 0;
    }

    [MethodImpl(Inline)]
    public static unsafe byte _RawReadValueUInt8<T>(this T target, long pointerOffset = 0) where T : unmanaged
    {
        int size = sizeof(T);
        byte* ptr = (byte*)(Unsafe.AsPointer(ref target)) + pointerOffset;
        if (size >= 1) return *((byte*)ptr);
        return 0;
    }

    [MethodImpl(Inline)]
    public static unsafe ushort _RawReadValueUInt16<T>(this T target, long pointerOffset = 0) where T : unmanaged
    {
        int size = sizeof(T);
        byte* ptr = (byte*)(Unsafe.AsPointer(ref target)) + pointerOffset;
        if (size >= 2) return *((ushort*)ptr);
        if (size >= 1) return *((byte*)ptr);
        return 0;
    }

    [MethodImpl(Inline)]
    public static unsafe uint _RawReadValueUInt32<T>(this T target, long pointerOffset = 0) where T : unmanaged
    {
        int size = sizeof(T);
        byte* ptr = (byte*)(Unsafe.AsPointer(ref target)) + pointerOffset;
        if (size >= 4) return *((uint*)ptr);
        if (size >= 2) return *((ushort*)ptr);
        if (size >= 1) return *((byte*)ptr);
        return 0;
    }

    [MethodImpl(Inline)]
    public static unsafe ulong _RawReadValueUInt64<T>(this T target, long pointerOffset = 0) where T : unmanaged
    {
        int size = sizeof(T);
        byte* ptr = (byte*)(Unsafe.AsPointer(ref target)) + pointerOffset;
        if (size >= 8) return *((ulong*)ptr);
        if (size >= 4) return *((uint*)ptr);
        if (size >= 2) return *((ushort*)ptr);
        if (size >= 1) return *((byte*)ptr);
        return 0;
    }





    [MethodImpl(Inline)]
    public static unsafe sbyte _RawReadValueSInt8<T>(this Span<T> target) where T : unmanaged
    {
        int size = sizeof(T) * target.Length;
        ref T r = ref MemoryMarshal.GetReference(target);
        byte* ptr = (byte*)(Unsafe.AsPointer(ref r));
        if (size >= 1) return *((sbyte*)ptr);
        return 0;
    }

    [MethodImpl(Inline)]
    public static unsafe short _RawReadValueSInt16<T>(this Span<T> target) where T : unmanaged
    {
        int size = sizeof(T) * target.Length;
        ref T r = ref MemoryMarshal.GetReference(target);
        byte* ptr = (byte*)(Unsafe.AsPointer(ref r));
        if (size >= 2) return *((short*)ptr);
        if (size >= 1) return *((sbyte*)ptr);
        return 0;
    }

    [MethodImpl(Inline)]
    public static unsafe int _RawReadValueSInt32<T>(this Span<T> target) where T : unmanaged
    {
        int size = sizeof(T) * target.Length;
        ref T r = ref MemoryMarshal.GetReference(target);
        byte* ptr = (byte*)(Unsafe.AsPointer(ref r));
        if (size >= 4) return *((int*)ptr);
        if (size >= 2) return *((short*)ptr);
        if (size >= 1) return *((sbyte*)ptr);
        return 0;
    }

    [MethodImpl(Inline)]
    public static unsafe long _RawReadValueSInt64<T>(this Span<T> target) where T : unmanaged
    {
        int size = sizeof(T) * target.Length;
        ref T r = ref MemoryMarshal.GetReference(target);
        byte* ptr = (byte*)(Unsafe.AsPointer(ref r));
        if (size >= 8) return *((long*)ptr);
        if (size >= 4) return *((int*)ptr);
        if (size >= 2) return *((short*)ptr);
        if (size >= 1) return *((sbyte*)ptr);
        return 0;
    }

    [MethodImpl(Inline)]
    public static unsafe byte _RawReadValueUInt8<T>(this Span<T> target) where T : unmanaged
    {
        int size = sizeof(T) * target.Length;
        ref T r = ref MemoryMarshal.GetReference(target);
        byte* ptr = (byte*)(Unsafe.AsPointer(ref r));
        if (size >= 1) return *((byte*)ptr);
        return 0;
    }

    [MethodImpl(Inline)]
    public static unsafe ushort _RawReadValueUInt16<T>(this Span<T> target) where T : unmanaged
    {
        int size = sizeof(T) * target.Length;
        ref T r = ref MemoryMarshal.GetReference(target);
        byte* ptr = (byte*)(Unsafe.AsPointer(ref r));
        if (size >= 2) return *((ushort*)ptr);
        if (size >= 1) return *((byte*)ptr);
        return 0;
    }

    [MethodImpl(Inline)]
    public static unsafe uint _RawReadValueUInt32<T>(this Span<T> target) where T : unmanaged
    {
        int size = sizeof(T) * target.Length;
        ref T r = ref MemoryMarshal.GetReference(target);
        byte* ptr = (byte*)(Unsafe.AsPointer(ref r));
        if (size >= 4) return *((uint*)ptr);
        if (size >= 2) return *((ushort*)ptr);
        if (size >= 1) return *((byte*)ptr);
        return 0;
    }

    [MethodImpl(Inline)]
    public static unsafe ulong _RawReadValueUInt64<T>(this Span<T> target) where T : unmanaged
    {
        int size = sizeof(T) * target.Length;
        ref T r = ref MemoryMarshal.GetReference(target);
        byte* ptr = (byte*)(Unsafe.AsPointer(ref r));
        if (size >= 8) return *((ulong*)ptr);
        if (size >= 4) return *((uint*)ptr);
        if (size >= 2) return *((ushort*)ptr);
        if (size >= 1) return *((byte*)ptr);
        return 0;
    }


    [MethodImpl(Inline)]
    public static unsafe sbyte _RawReadValueSInt8<T>(this ReadOnlySpan<T> target) where T : unmanaged
    {
        int size = sizeof(T) * target.Length;
        ref T r = ref MemoryMarshal.GetReference(target);
        byte* ptr = (byte*)(Unsafe.AsPointer(ref r));
        if (size >= 1) return *((sbyte*)ptr);
        return 0;
    }
    [MethodImpl(Inline)]
    public static unsafe sbyte _RawReadValueSInt8<T>(this ReadOnlyMemory<T> target) where T : unmanaged
        => _RawReadValueSInt8(target.Span);

    [MethodImpl(Inline)]
    public static unsafe short _RawReadValueSInt16<T>(this ReadOnlySpan<T> target) where T : unmanaged
    {
        int size = sizeof(T) * target.Length;
        ref T r = ref MemoryMarshal.GetReference(target);
        byte* ptr = (byte*)(Unsafe.AsPointer(ref r));
        if (size >= 2) return *((short*)ptr);
        if (size >= 1) return *((sbyte*)ptr);
        return 0;
    }
    [MethodImpl(Inline)]
    public static unsafe short _RawReadValueSInt16<T>(this ReadOnlyMemory<T> target) where T : unmanaged
        => _RawReadValueSInt16(target.Span);

    [MethodImpl(Inline)]
    public static unsafe int _RawReadValueSInt32<T>(this ReadOnlySpan<T> target) where T : unmanaged
    {
        int size = sizeof(T) * target.Length;
        ref T r = ref MemoryMarshal.GetReference(target);
        byte* ptr = (byte*)(Unsafe.AsPointer(ref r));
        if (size >= 4) return *((int*)ptr);
        if (size >= 2) return *((short*)ptr);
        if (size >= 1) return *((sbyte*)ptr);
        return 0;
    }
    [MethodImpl(Inline)]
    public static unsafe int _RawReadValueSInt32<T>(this ReadOnlyMemory<T> target) where T : unmanaged
        => _RawReadValueSInt32(target.Span);

    [MethodImpl(Inline)]
    public static unsafe long _RawReadValueSInt64<T>(this ReadOnlySpan<T> target) where T : unmanaged
    {
        int size = sizeof(T) * target.Length;
        ref T r = ref MemoryMarshal.GetReference(target);
        byte* ptr = (byte*)(Unsafe.AsPointer(ref r));
        if (size >= 8) return *((long*)ptr);
        if (size >= 4) return *((int*)ptr);
        if (size >= 2) return *((short*)ptr);
        if (size >= 1) return *((sbyte*)ptr);
        return 0;
    }
    [MethodImpl(Inline)]
    public static unsafe long _RawReadValueSInt64<T>(this ReadOnlyMemory<T> target) where T : unmanaged
        => _RawReadValueSInt64(target.Span);

    [MethodImpl(Inline)]
    public static unsafe byte _RawReadValueUInt8<T>(this ReadOnlySpan<T> target) where T : unmanaged
    {
        int size = sizeof(T) * target.Length;
        ref T r = ref MemoryMarshal.GetReference(target);
        byte* ptr = (byte*)(Unsafe.AsPointer(ref r));
        if (size >= 1) return *((byte*)ptr);
        return 0;
    }
    [MethodImpl(Inline)]
    public static unsafe byte _RawReadValueUInt8<T>(this ReadOnlyMemory<T> target) where T : unmanaged
        => _RawReadValueUInt8(target.Span);

    [MethodImpl(Inline)]
    public static unsafe ushort _RawReadValueUInt16<T>(this ReadOnlySpan<T> target) where T : unmanaged
    {
        int size = sizeof(T) * target.Length;
        ref T r = ref MemoryMarshal.GetReference(target);
        byte* ptr = (byte*)(Unsafe.AsPointer(ref r));
        if (size >= 2) return *((ushort*)ptr);
        if (size >= 1) return *((byte*)ptr);
        return 0;
    }
    [MethodImpl(Inline)]
    public static unsafe ushort _RawReadValueUInt16<T>(this ReadOnlyMemory<T> target) where T : unmanaged
        => _RawReadValueUInt16(target.Span);

    [MethodImpl(Inline)]
    public static unsafe uint _RawReadValueUInt32<T>(this ReadOnlySpan<T> target) where T : unmanaged
    {
        int size = sizeof(T) * target.Length;
        ref T r = ref MemoryMarshal.GetReference(target);
        byte* ptr = (byte*)(Unsafe.AsPointer(ref r));
        if (size >= 4) return *((uint*)ptr);
        if (size >= 2) return *((ushort*)ptr);
        if (size >= 1) return *((byte*)ptr);
        return 0;
    }
    [MethodImpl(Inline)]
    public static unsafe uint _RawReadValueUInt32<T>(this ReadOnlyMemory<T> target) where T : unmanaged
        => _RawReadValueUInt32(target.Span);

    [MethodImpl(Inline)]
    public static unsafe ulong _RawReadValueUInt64<T>(this ReadOnlySpan<T> target) where T : unmanaged
    {
        int size = sizeof(T) * target.Length;
        ref T r = ref MemoryMarshal.GetReference(target);
        byte* ptr = (byte*)(Unsafe.AsPointer(ref r));
        if (size >= 8) return *((ulong*)ptr);
        if (size >= 4) return *((uint*)ptr);
        if (size >= 2) return *((ushort*)ptr);
        if (size >= 1) return *((byte*)ptr);
        return 0;
    }
    [MethodImpl(Inline)]
    public static unsafe ulong _RawReadValueUInt64<T>(this ReadOnlyMemory<T> target) where T : unmanaged
        => _RawReadValueUInt64(target.Span);



    [MethodImpl(Inline)]
    public static unsafe void* _AsPointer<T>(this ref T target) where T : unmanaged
        => Unsafe.AsPointer(ref target);

    [MethodImpl(Inline)]
    public static unsafe Span<byte> _AsByteSpan<T>(this ref T target) where T : unmanaged
    {
        void* ptr = Unsafe.AsPointer(ref target);
        return new Span<byte>(ptr, sizeof(T));
    }

    [MethodImpl(Inline)]
    public static unsafe ReadOnlySpan<byte> _AsReadOnlyByteSpan<T>(this ref T target) where T : unmanaged
    {
        void* ptr = Unsafe.AsPointer(ref target);
        return new ReadOnlySpan<byte>(ptr, sizeof(T));
    }

    [MethodImpl(Inline)]
    public static unsafe byte _GetBitsUInt8(this byte src, byte bitMask)
        => (byte)(src & bitMask);

    [MethodImpl(Inline)]
    public static unsafe ushort _GetBitsUInt16(this ushort src, ushort bitMask)
        => (ushort)(src & bitMask);

    [MethodImpl(Inline)]
    public static unsafe uint _GetBitsUInt32(this uint src, uint bitMask)
        => (uint)(src & bitMask);

    [MethodImpl(Inline)]
    public static unsafe ulong _GetBitsUInt64(this ulong src, ulong bitMask)
        => (ulong)(src & bitMask);

    [MethodImpl(Inline)]
    public static unsafe sbyte _GetBitsSInt8(this sbyte src, sbyte bitMask)
        => (sbyte)(src & bitMask);

    [MethodImpl(Inline)]
    public static unsafe short _GetBitsSInt16(this short src, short bitMask)
        => (short)(src & bitMask);

    [MethodImpl(Inline)]
    public static unsafe int _GetBitsSInt32(this int src, int bitMask)
        => (int)(src & bitMask);

    [MethodImpl(Inline)]
    public static unsafe long _GetBitsSInt64(this long src, long bitMask)
        => (long)(src & bitMask);


    [MethodImpl(Inline)]
    public static unsafe ushort _GetBitsUInt16_EndianSafe(this ushort src, ushort bitMask)
        => (ushort)(src._Endian16_U() & bitMask);

    [MethodImpl(Inline)]
    public static unsafe uint _GetBitsUInt32_EndianSafe(this uint src, uint bitMask)
        => (uint)(src._Endian32_U() & bitMask);

    [MethodImpl(Inline)]
    public static unsafe ulong _GetBitsUInt64_EndianSafe(this ulong src, ulong bitMask)
        => (ulong)(src._Endian64_U() & bitMask);

    [MethodImpl(Inline)]
    public static unsafe short _GetBitsSInt16_EndianSafe(this short src, short bitMask)
        => (short)(src._Endian16_S() & bitMask);

    [MethodImpl(Inline)]
    public static unsafe int _GetBitsSInt32_EndianSafe(this int src, int bitMask)
        => (int)(src._Endian32_S() & bitMask);

    [MethodImpl(Inline)]
    public static unsafe long _GetBitsSInt64_EndianSafe(this long src, long bitMask)
        => (long)(src._Endian64_S() & bitMask);



    [MethodImpl(Inline)]
    public static unsafe void _UpdateBitsUInt8(this ref byte src, byte bitMask, byte value)
        => src = (byte)((src & ~bitMask) | (value & bitMask));

    [MethodImpl(Inline)]
    public static unsafe void _UpdateBitsUInt16(this ref ushort src, ushort bitMask, ushort value)
        => src = (ushort)((src & ~bitMask) | (value & bitMask));

    [MethodImpl(Inline)]
    public static unsafe void _UpdateBitsUInt32(this ref uint src, uint bitMask, uint value)
        => src = (uint)((src & ~bitMask) | (value & bitMask));

    [MethodImpl(Inline)]
    public static unsafe void _UpdateBitsUInt64(this ref ulong src, ulong bitMask, ulong value)
        => src = (ulong)((src & ~bitMask) | (value & bitMask));

    [MethodImpl(Inline)]
    public static unsafe void _UpdateBitsSInt8(this ref sbyte src, sbyte bitMask, sbyte value)
        => src = (sbyte)((src & ~bitMask) | (value & bitMask));

    [MethodImpl(Inline)]
    public static unsafe void _UpdateBitsSInt16(this ref short src, short bitMask, short value)
        => src = (short)((src & ~bitMask) | (value & bitMask));

    [MethodImpl(Inline)]
    public static unsafe void _UpdateBitsSInt32(this ref int src, int bitMask, int value)
        => src = (int)((src & ~bitMask) | (value & bitMask));

    [MethodImpl(Inline)]
    public static unsafe void _UpdateBitsSInt64(this ref long src, long bitMask, long value)
        => src = (long)((src & ~bitMask) | (value & bitMask));

    [MethodImpl(Inline)]
    public static unsafe void _UpdateBitsUInt16_EndianSafe(this ref ushort src, ushort bitMask, ushort value)
    {
        src = src._Endian16_U();
        _UpdateBitsUInt16(ref src, bitMask, value);
        src = src._Endian16_U();
    }

    [MethodImpl(Inline)]
    public static unsafe void _UpdateBitsUInt32_EndianSafe(this ref uint src, uint bitMask, uint value)
    {
        src = src._Endian32_U();
        _UpdateBitsUInt32(ref src, bitMask, value);
        src = src._Endian32_U();
    }

    [MethodImpl(Inline)]
    public static unsafe void _UpdateBitsUInt64_EndianSafe(this ref ulong src, ulong bitMask, ulong value)
    {
        src = src._Endian64_U();
        _UpdateBitsUInt64(ref src, bitMask, value);
        src = src._Endian64_U();
    }

    [MethodImpl(Inline)]
    public static unsafe void _UpdateBitsSInt16_EndianSafe(this ref short src, short bitMask, short value)
    {
        src = src._Endian16_S();
        _UpdateBitsSInt16(ref src, bitMask, value);
        src = src._Endian16_S();
    }

    [MethodImpl(Inline)]
    public static unsafe void _UpdateBitsSInt32_EndianSafe(this ref int src, int bitMask, int value)
    {
        src = src._Endian32_S();
        _UpdateBitsSInt32(ref src, bitMask, value);
        src = src._Endian32_S();
    }

    [MethodImpl(Inline)]
    public static unsafe void _UpdateBitsSInt64_EndianSafe(this ref long src, long bitMask, long value)
    {
        src = src._Endian64_S();
        _UpdateBitsSInt64(ref src, bitMask, value);
        src = src._Endian64_S();
    }

    [MethodImpl(Inline)]
    public static unsafe bool _IsZeroStruct<T>(this ref T value, int size = DefaultSize) where T : unmanaged
    {
        if (size == DefaultSize)
        {
            if (sizeof(T) <= 16)
                return Util.IsZeroStruct(in value, DefaultSize);
            else
                return Util.IsZeroFastStruct(in value, DefaultSize);
        }
        else
        {
            if (size <= 16)
                return Util.IsZeroStruct(in value, size);
            else
                return Util.IsZeroFastStruct(in value, size);
        }
    }

    [MethodImpl(Inline)]
    public static unsafe Memory<byte> _CopyToMemory<T>(this ref T data, int size = DefaultSize) where T : unmanaged
    {
        size = size._DefaultSize(sizeof(T));

        byte[] ret = new byte[size];

        Util.CopyByte(ref ret[0], in Unsafe.As<T, byte>(ref data), size);

        return ret;
    }

    [MethodImpl(Inline)]
    public static unsafe void _Xor(this Span<byte> dest, ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
    {
        if (dest.Length < a.Length) throw new CoresLibException("dest.Length < a.Length");
        if (a.Length != b.Length) throw new CoresLibException("a.Length != b.Length");
        if (a.Length == 0) return;
        int len = a.Length;
        for (int i = 0; i < len; i++)
        {
            dest[i] = (byte)(a[i] ^ b[i]);
        }
    }
}
