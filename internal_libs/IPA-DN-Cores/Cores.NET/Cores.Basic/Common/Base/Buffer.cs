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
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Buffers;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System.Text;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using System.Numerics;

namespace IPA.Cores.Basic;

public static partial class CoresConfig
{
    public static partial class LargeMemoryBuffer
    {
        public static readonly Copenhagen<int> DefaultSegmentSize = 10_000_000;
        //public static readonly Copenhagen<int> DefaultSegmentSize = 10;
    }

    public static partial class SpanBasedQueueSettings
    {
        public static readonly Copenhagen<int> DefaultInitialQueueLength = 8;
        public static readonly Copenhagen<int> DefaultMaxQueueLength = 16000;
    }
}

public static class BufferConsts
{
    public const int InitialBufferSize = 128;
}

public ref struct SpanBuffer<T>
{
    Span<T> InternalSpan;
    public int CurrentPosition { get; private set; }
    public int Length { get; private set; }
    public bool Empty => Length == 0;
    public int InternalBufferSize => InternalSpan.Length;

    public Span<T> Span { get => InternalSpan.Slice(0, Length); }
    public Span<T> SpanBefore { get => Span.Slice(0, CurrentPosition); }
    public Span<T> SpanAfter { get => Span.Slice(CurrentPosition); }

    public SpanBuffer(int initialBufferSize) : this(new T[initialBufferSize]) { }

    public SpanBuffer(EnsureSpecial withReserve, int reversedSized) : this(new T[reversedSized])
    {
        this.Length = 0;
    }

    public SpanBuffer(Span<T> baseSpan)
    {
        InternalSpan = baseSpan;
        CurrentPosition = 0;
        Length = baseSpan.Length;
    }

    public static unsafe SpanBuffer<byte> FromStruct<TStruct>(TStruct src) where TStruct : unmanaged
    {
        Memory<byte> baseMemory = new byte[sizeof(TStruct)];
        ref TStruct dst = ref baseMemory._AsStruct<TStruct>();
        dst = src;
        return new SpanBuffer<byte>(baseMemory.Span);
    }

    static T dummyRefValue = default!;
    public ref T GetRefForFixedPtr(int position = 0)
    {
        var span = this.Span.Slice(position);
        if (span.IsEmpty)
            return ref dummyRefValue;
        return ref span[0];
    }

    public static implicit operator SpanBuffer<T>(Span<T> span) => new SpanBuffer<T>(span);
    public static implicit operator SpanBuffer<T>(Memory<T> memory) => new SpanBuffer<T>(memory.Span);
    public static implicit operator SpanBuffer<T>(T[] array) => new SpanBuffer<T>(array.AsSpan());
    public static implicit operator Span<T>(SpanBuffer<T> buf) => buf.Span;
    public static implicit operator ReadOnlySpan<T>(SpanBuffer<T> buf) => buf.Span;
    public static implicit operator ReadOnlySpanBuffer<T>(SpanBuffer<T> buf) => buf.AsReadOnly();

    public SpanBuffer<T> SliceAfter() => Slice(CurrentPosition);
    public SpanBuffer<T> SliceBefore() => Slice(0, CurrentPosition);
    public SpanBuffer<T> Slice(int start) => Slice(start, this.Length - start);
    public SpanBuffer<T> Slice(int start, int length)
    {
        if (start < 0) throw new ArgumentOutOfRangeException("start < 0");
        if (length < 0) throw new ArgumentOutOfRangeException("length < 0");
        if (start > Length) throw new ArgumentOutOfRangeException("start > Size");
        if (checked(start + length) > Length) throw new ArgumentOutOfRangeException("length > Size");
        SpanBuffer<T> ret = new SpanBuffer<T>(this.InternalSpan.Slice(start, length));
        ret.Length = length;
        ret.CurrentPosition = Math.Max(checked(CurrentPosition - start), 0);
        return ret;
    }

    public SpanBuffer<T> Clone()
    {
        SpanBuffer<T> ret = new SpanBuffer<T>(InternalSpan.ToArray());
        ret.Length = Length;
        ret.CurrentPosition = CurrentPosition;
        return ret;
    }

    public ReadOnlySpanBuffer<T> AsReadOnly()
    {
        ReadOnlySpanBuffer<T> ret = new ReadOnlySpanBuffer<T>(Span);
        ret.Seek(CurrentPosition, SeekOrigin.Begin);
        return ret;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public Span<T> Walk(int size, bool noMove = false)
    {
        int newSize = checked(CurrentPosition + size);

        if (InternalSpan.Length < newSize)
        {
            EnsureInternalBufferReserved(newSize);
        }
        var ret = InternalSpan.Slice(CurrentPosition, size);
        Length = Math.Max(newSize, Length);
        if (noMove == false) CurrentPosition += size;
        return ret;
    }

    public void Write(Memory<T> data) => Write(data.Span);
    public void Write(ReadOnlyMemory<T> data) => Write(data.Span);
    public void Write(T[] data, int offset = 0, int length = DefaultSize) => Write(data.AsSpan(offset, length._DefaultSize(data.Length - offset)));

    public unsafe void Write(void* ptr, int size)
    {
        Span<T> span = Walk(size);
        ref T t = ref span[0];
        void* dst = Unsafe.AsPointer(ref t);
        Unsafe.CopyBlock(dst, ptr, (uint)size);
    }

    public void Write(Span<T> data)
    {
        var span = Walk(data.Length);
        data.CopyTo(span);
    }

    public void Write(ReadOnlySpan<T> data)
    {
        var span = Walk(data.Length);
        data.CopyTo(span);
    }

    public void WriteZero(int length)
    {
        var zeroSpan = ZeroedMemory<T>.Memory.Span;
        while (length >= 1)
        {
            int currentSize = Math.Min(length, zeroSpan.Length);
            this.Write(zeroSpan.Slice(0, currentSize));
            length -= currentSize;
        }
    }

    public ReadOnlySpan<T> Read(int size, bool allowPartial = false)
    {
        int sizeRead = size;
        if (checked(CurrentPosition + size) > Length)
        {
            if (allowPartial == false) throw new ArgumentOutOfRangeException("(CurrentPosition + size) > Size");
            sizeRead = Length - CurrentPosition;
        }

        Span<T> ret = InternalSpan.Slice(CurrentPosition, sizeRead);
        CurrentPosition += sizeRead;
        return ret;
    }

    public ReadOnlySpan<T> Peek(int size, bool allowPartial = false)
    {
        int sizeRead = size;
        if (checked(CurrentPosition + size) > Length)
        {
            if (allowPartial == false) throw new ArgumentOutOfRangeException("(CurrentPosition + size) > Size");
            sizeRead = Length - CurrentPosition;
        }

        Span<T> ret = InternalSpan.Slice(CurrentPosition, sizeRead);
        return ret;
    }

    public void Seek(int offset, SeekOrigin mode, bool allocate = false)
    {
        int newPosition = 0;
        if (mode == SeekOrigin.Current)
            newPosition = checked(CurrentPosition + offset);
        else if (mode == SeekOrigin.End)
            newPosition = checked(Length + offset);
        else
            newPosition = offset;

        if (newPosition < 0) throw new ArgumentOutOfRangeException("newPosition < 0");
        if (allocate == false)
        {
            if (newPosition > Length) throw new ArgumentOutOfRangeException("newPosition > Size");
        }
        else
        {
            EnsureInternalBufferReserved(newPosition);
            Length = Math.Max(newPosition, Length);
        }

        CurrentPosition = newPosition;
    }

    public SpanBuffer<T> SeekToBegin()
    {
        Seek(0, SeekOrigin.Begin);
        return this;
    }

    public SpanBuffer<T> SeekToEnd()
    {
        Seek(0, SeekOrigin.End);
        return this;
    }

    public int Read(Span<T> dest, int size = DefaultSize, bool allowPartial = false)
    {
        size = size._DefaultSize(dest.Length);
        if (dest.Length < size) throw new ArgumentException("dest.Length < size");
        var span = Read(size, allowPartial);
        span.CopyTo(dest);
        return span.Length;
    }

    public int Peek(Span<T> dest, int size = DefaultSize, bool allowPartial = false)
    {
        size = size._DefaultSize(dest.Length);
        if (dest.Length < size) throw new ArgumentException("dest.Length < size");
        var span = Peek(size, allowPartial);
        span.CopyTo(dest);
        return span.Length;
    }

    public int Read(T[] dest, int offset, int size, bool allowPartial = false) => Read(dest.AsSpan(offset, size), size, allowPartial);
    public int Peek(T[] dest, int offset, int size, bool allowPartial = false) => Peek(dest.AsSpan(offset, size), size, allowPartial);

    public void EnsureInternalBufferReserved(int newSize)
    {
        if (InternalSpan.Length >= newSize) return;

        int newInternalSize = Util.GetGreaterOrEqualOptimiedSizePowerOf2(Math.Max(newSize, BufferConsts.InitialBufferSize));

        InternalSpan = InternalSpan._ReAlloc(newInternalSize, this.Length);
    }

    public void OptimizeInternalBufferSize()
    {
        int newInternalSize = Util.GetGreaterOrEqualOptimiedSizePowerOf2(Math.Max(this.Length, BufferConsts.InitialBufferSize));

        if (this.InternalSpan.Length > newInternalSize)
            InternalSpan = InternalSpan._ReAlloc(newInternalSize, this.Length);
    }

    public void SetLength(int size)
    {
        if (size < 0) throw new ArgumentOutOfRangeException("size < 0");

        EnsureInternalBufferReserved(size);
        this.Length = size;
        if (this.CurrentPosition > this.Length)
            this.CurrentPosition = this.Length;
    }

    public void Clear(int initialBufferSize = 0)
    {
        initialBufferSize = Math.Max(0, initialBufferSize);
        InternalSpan = new T[initialBufferSize];
        CurrentPosition = 0;
        Length = 0;
    }

    public void WriteOne(T data)
    {
        var span = Walk(1);
        span[0] = data;
    }

    public T ReadOne()
    {
        var span = Read(1, false);
        return span[0];
    }

    public T PeekOne()
    {
        var span = Peek(1, false);
        return span[0];
    }
}

public ref struct ReadOnlySpanBuffer<T>
{
    ReadOnlySpan<T> InternalSpan;
    public int CurrentPosition { get; private set; }
    public int Length { get; private set; }
    public bool Empty => Length == 0;
    public int InternalBufferSize => InternalSpan.Length;

    public ReadOnlySpan<T> Span { get => InternalSpan.Slice(0, Length); }
    public ReadOnlySpan<T> SpanBefore { get => Span.Slice(0, CurrentPosition); }
    public ReadOnlySpan<T> SpanAfter { get => Span.Slice(CurrentPosition); }

    public static implicit operator ReadOnlySpanBuffer<T>(ReadOnlySpan<T> span) => new ReadOnlySpanBuffer<T>(span);
    public static implicit operator ReadOnlySpanBuffer<T>(ReadOnlyMemory<T> memory) => new ReadOnlySpanBuffer<T>(memory.Span);
    public static implicit operator ReadOnlySpanBuffer<T>(T[] array) => new ReadOnlySpanBuffer<T>(array.AsSpan());
    public static implicit operator ReadOnlySpan<T>(ReadOnlySpanBuffer<T> buf) => buf.Span;

    public ReadOnlySpanBuffer<T> SliceAfter() => Slice(CurrentPosition);
    public ReadOnlySpanBuffer<T> SliceBefore() => Slice(0, CurrentPosition);
    public ReadOnlySpanBuffer<T> Slice(int start) => Slice(start, this.Length - start);
    public ReadOnlySpanBuffer<T> Slice(int start, int length)
    {
        if (start < 0) throw new ArgumentOutOfRangeException("start < 0");
        if (length < 0) throw new ArgumentOutOfRangeException("length < 0");
        if (start > Length) throw new ArgumentOutOfRangeException("start > Size");
        if (checked(start + length) > Length) throw new ArgumentOutOfRangeException("length > Size");
        ReadOnlySpanBuffer<T> ret = new ReadOnlySpanBuffer<T>(this.InternalSpan.Slice(start, length));
        ret.Length = length;
        ret.CurrentPosition = Math.Max(checked(CurrentPosition - start), 0);
        return ret;
    }

    public ReadOnlySpanBuffer<T> Clone()
    {
        ReadOnlySpanBuffer<T> ret = new ReadOnlySpanBuffer<T>(InternalSpan.ToArray());
        ret.Length = Length;
        ret.CurrentPosition = CurrentPosition;
        return ret;
    }

    public SpanBuffer<T> CloneAsWritable()
    {
        SpanBuffer<T> ret = new SpanBuffer<T>(Span.ToArray());
        ret.Seek(CurrentPosition, SeekOrigin.Begin);
        return ret;
    }

    public ReadOnlySpanBuffer(int size = 0) : this(new T[size]) { }

    public ReadOnlySpanBuffer(ReadOnlySpan<T> baseSpan)
    {
        InternalSpan = baseSpan;
        CurrentPosition = 0;
        Length = baseSpan.Length;
    }

    public static unsafe ReadOnlySpanBuffer<byte> FromStruct<TStruct>(TStruct src) where TStruct : unmanaged
    {
        Memory<byte> baseMemory = new byte[sizeof(TStruct)];
        ref TStruct dst = ref baseMemory._AsStruct<TStruct>();
        dst = src;
        return new ReadOnlySpanBuffer<byte>(baseMemory.Span);
    }

    static T dummyRefValue = default!;
    public ref readonly T GetRefForFixedPtr(int position = 0)
    {
        var span = this.Span.Slice(position);
        if (span.IsEmpty)
            return ref dummyRefValue;
        return ref span[0];
    }

    public ReadOnlySpan<T> Read(int size, bool allowPartial = false)
    {
        int sizeRead = size;
        if (checked(CurrentPosition + size) > Length)
        {
            if (allowPartial == false) throw new ArgumentOutOfRangeException("(CurrentPosition + size) > Size");
            sizeRead = Length - CurrentPosition;
        }

        ReadOnlySpan<T> ret = InternalSpan.Slice(CurrentPosition, sizeRead);
        CurrentPosition += sizeRead;
        return ret;
    }

    public ReadOnlySpan<T> Peek(int size, bool allowPartial = false)
    {
        int sizeRead = size;
        if (checked(CurrentPosition + size) > Length)
        {
            if (allowPartial == false) throw new ArgumentOutOfRangeException("(CurrentPosition + size) > Size");
            sizeRead = Length - CurrentPosition;
        }

        ReadOnlySpan<T> ret = InternalSpan.Slice(CurrentPosition, sizeRead);
        return ret;
    }

    public void Seek(int offset, SeekOrigin mode, bool allocate = false)
    {
        int newPosition = 0;
        if (mode == SeekOrigin.Current)
            newPosition = checked(CurrentPosition + offset);
        else if (mode == SeekOrigin.End)
            newPosition = checked(Length + offset);
        else
            newPosition = offset;

        if (newPosition < 0) throw new ArgumentOutOfRangeException("newPosition < 0");
        if (newPosition > Length) throw new ArgumentOutOfRangeException("newPosition > Size");

        CurrentPosition = newPosition;
    }

    public ReadOnlySpanBuffer<T> SeekToBegin()
    {
        Seek(0, SeekOrigin.Begin);
        return this;
    }

    public ReadOnlySpanBuffer<T> SeekToEnd()
    {
        Seek(0, SeekOrigin.End);
        return this;
    }

    public int Read(Span<T> dest, int size = DefaultSize, bool allowPartial = false)
    {
        size = size._DefaultSize(dest.Length);
        if (dest.Length < size) throw new ArgumentException("dest.Length < size");
        var span = Read(size, allowPartial);
        span.CopyTo(dest);
        return span.Length;
    }

    public int Peek(Span<T> dest, int size = DefaultSize, bool allowPartial = false)
    {
        size = size._DefaultSize(dest.Length);
        if (dest.Length < size) throw new ArgumentException("dest.Length < size");
        var span = Peek(size, allowPartial);
        span.CopyTo(dest);
        return span.Length;
    }

    public int Read(T[] dest, int offset, int size, bool allowPartial = false) => Read(dest.AsSpan(offset, size), size, allowPartial);
    public int Peek(T[] dest, int offset, int size, bool allowPartial = false) => Peek(dest.AsSpan(offset, size), size, allowPartial);


    public void Clear()
    {
        InternalSpan = new ReadOnlySpan<T>();
        CurrentPosition = 0;
        Length = 0;
    }

    public T ReadOne()
    {
        var span = Read(1, false);
        return span[0];
    }

    public T PeekOne()
    {
        var span = Peek(1, false);
        return span[0];
    }
}

public ref struct FastMemoryBuffer<T>
{
    Memory<T> InternalBuffer;
    Span<T> InternalSpan;
    public int CurrentPosition { get; private set; }
    public int Length { get; private set; }
    public bool Empty => Length == 0;
    public int InternalBufferSize => InternalBuffer.Length;

    public Memory<T> Memory { get => InternalBuffer.Slice(0, Length); }
    public Memory<T> MemoryBefore { get => Memory.Slice(0, CurrentPosition); }
    public Memory<T> MemoryAfter { get => Memory.Slice(CurrentPosition); }

    public Span<T> Span { get => InternalBuffer.Slice(0, Length).Span; }
    public Span<T> SpanBefore { get => Memory.Slice(0, CurrentPosition).Span; }
    public Span<T> SpanAfter { get => Memory.Slice(CurrentPosition).Span; }

    public FastMemoryBuffer(EnsureSpecial withReserve, int reversedSized) : this(new T[reversedSized])
    {
        this.Length = 0;
    }

    public FastMemoryBuffer(int size = 0) : this(new T[size]) { }

    public FastMemoryBuffer(Memory<T> baseMemory)
    {
        InternalBuffer = baseMemory;
        CurrentPosition = 0;
        Length = baseMemory.Length;
        InternalSpan = InternalBuffer.Span;
    }

    public static unsafe FastMemoryBuffer<byte> FromStruct<TStruct>(TStruct src) where TStruct : unmanaged
    {
        Memory<byte> baseMemory = new byte[sizeof(TStruct)];
        ref TStruct dst = ref baseMemory._AsStruct<TStruct>();
        dst = src;
        return new FastMemoryBuffer<byte>(baseMemory);
    }

    static T dummyRefValue = default!;
    public ref T GetRefForFixedPtr(int position = 0)
    {
        var span = this.Span.Slice(position);
        if (span.IsEmpty)
            return ref dummyRefValue;
        return ref span[0];
    }

    public static implicit operator FastMemoryBuffer<T>(Memory<T> memory) => new FastMemoryBuffer<T>(memory);
    public static implicit operator FastMemoryBuffer<T>(T[] array) => new FastMemoryBuffer<T>(array.AsMemory());
    public static implicit operator Memory<T>(FastMemoryBuffer<T> buf) => buf.Memory;
    public static implicit operator Span<T>(FastMemoryBuffer<T> buf) => buf.Span;
    public static implicit operator ReadOnlyMemory<T>(FastMemoryBuffer<T> buf) => buf.Memory;
    public static implicit operator ReadOnlySpan<T>(FastMemoryBuffer<T> buf) => buf.Span;
    public static implicit operator FastReadOnlyMemoryBuffer<T>(FastMemoryBuffer<T> buf) => buf.AsReadOnly();
    public static implicit operator ReadOnlySpanBuffer<T>(FastMemoryBuffer<T> buf) => buf.AsReadOnlySpanBuffer();

    public FastMemoryBuffer<T> SliceAfter() => Slice(CurrentPosition);
    public FastMemoryBuffer<T> SliceBefore() => Slice(0, CurrentPosition);
    public FastMemoryBuffer<T> Slice(int start) => Slice(start, this.Length - start);
    public FastMemoryBuffer<T> Slice(int start, int length)
    {
        if (start < 0) throw new ArgumentOutOfRangeException("start < 0");
        if (length < 0) throw new ArgumentOutOfRangeException("length < 0");
        if (start > Length) throw new ArgumentOutOfRangeException("start > Size");
        if (checked(start + length) > Length) throw new ArgumentOutOfRangeException("length > Size");
        FastMemoryBuffer<T> ret = new FastMemoryBuffer<T>(this.InternalBuffer.Slice(start, length));
        ret.Length = length;
        ret.CurrentPosition = Math.Max(checked(CurrentPosition - start), 0);
        return ret;
    }

    public FastMemoryBuffer<T> Clone()
    {
        FastMemoryBuffer<T> ret = new FastMemoryBuffer<T>(InternalSpan.ToArray());
        ret.Length = Length;
        ret.CurrentPosition = CurrentPosition;
        return ret;
    }

    public FastReadOnlyMemoryBuffer<T> AsReadOnly()
    {
        FastReadOnlyMemoryBuffer<T> ret = new FastReadOnlyMemoryBuffer<T>(Memory);
        ret.Seek(CurrentPosition, SeekOrigin.Begin);
        return ret;
    }

    public SpanBuffer<T> AsSpanBuffer()
    {
        SpanBuffer<T> ret = new SpanBuffer<T>(Span);
        ret.Seek(CurrentPosition, SeekOrigin.Begin);
        return ret;
    }

    public ReadOnlySpanBuffer<T> AsReadOnlySpanBuffer()
    {
        ReadOnlySpanBuffer<T> ret = new ReadOnlySpanBuffer<T>(Span);
        ret.Seek(CurrentPosition, SeekOrigin.Begin);
        return ret;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public Span<T> Walk(int size, bool noMove = false)
    {
        int newSize = checked(CurrentPosition + size);
        if (InternalBuffer.Length < newSize)
        {
            EnsureInternalBufferReserved(newSize);
        }
        var ret = InternalSpan.Slice(CurrentPosition, size);
        Length = Math.Max(newSize, Length);
        if (noMove == false) CurrentPosition += size;
        return ret;
    }

    public void Write(T[] data, int offset = 0, int length = DefaultSize) => Write(data.AsSpan(offset, length._DefaultSize(data.Length - offset)));
    public void Write(Memory<T> data) => Write(data.Span);
    public void Write(ReadOnlyMemory<T> data) => Write(data.Span);

    public unsafe void Write(void* ptr, int size)
    {
        Span<T> span = Walk(size);
        ref T t = ref span[0];
        void* dst = Unsafe.AsPointer(ref t);
        Unsafe.CopyBlock(dst, ptr, (uint)size);
    }

    public void Write(Span<T> data)
    {
        var span = Walk(data.Length);
        data.CopyTo(span);
    }

    public void Write(ReadOnlySpan<T> data)
    {
        var span = Walk(data.Length);
        data.CopyTo(span);
    }

    public void WriteZero(int length)
    {
        var zeroSpan = ZeroedMemory<T>.Memory.Span;
        while (length >= 1)
        {
            int currentSize = Math.Min(length, zeroSpan.Length);
            this.Write(zeroSpan.Slice(0, currentSize));
            length -= currentSize;
        }
    }

    public ReadOnlySpan<T> Read(int size, bool allowPartial = false)
    {
        int sizeRead = size;
        if (checked(CurrentPosition + size) > Length)
        {
            if (allowPartial == false) throw new ArgumentOutOfRangeException("(CurrentPosition + size) > Size");
            sizeRead = Length - CurrentPosition;
        }

        ReadOnlySpan<T> ret = InternalSpan.Slice(CurrentPosition, sizeRead);
        CurrentPosition += sizeRead;
        return ret;
    }

    public ReadOnlySpan<T> Peek(int size, bool allowPartial = false)
    {
        int sizeRead = size;
        if (checked(CurrentPosition + size) > Length)
        {
            if (allowPartial == false) throw new ArgumentOutOfRangeException("(CurrentPosition + size) > Size");
            sizeRead = Length - CurrentPosition;
        }

        Span<T> ret = InternalSpan.Slice(CurrentPosition, sizeRead);
        return ret;
    }

    public ReadOnlyMemory<T> ReadAsMemory(int size, bool allowPartial = false)
    {
        int sizeRead = size;
        if (checked(CurrentPosition + size) > Length)
        {
            if (allowPartial == false) throw new ArgumentOutOfRangeException("(CurrentPosition + size) > Size");
            sizeRead = Length - CurrentPosition;
        }

        ReadOnlyMemory<T> ret = InternalBuffer.Slice(CurrentPosition, sizeRead);
        CurrentPosition += sizeRead;
        return ret;
    }

    public ReadOnlyMemory<T> PeekAsMemory(int size, bool allowPartial = false)
    {
        int sizeRead = size;
        if (checked(CurrentPosition + size) > Length)
        {
            if (allowPartial == false) throw new ArgumentOutOfRangeException("(CurrentPosition + size) > Size");
            sizeRead = Length - CurrentPosition;
        }

        ReadOnlyMemory<T> ret = InternalBuffer.Slice(CurrentPosition, sizeRead);
        return ret;
    }

    public void Seek(int offset, SeekOrigin mode, bool allocate = false)
    {
        int newPosition = 0;
        if (mode == SeekOrigin.Current)
            newPosition = checked(CurrentPosition + offset);
        else if (mode == SeekOrigin.End)
            newPosition = checked(Length + offset);
        else
            newPosition = offset;

        if (newPosition < 0) throw new ArgumentOutOfRangeException("newPosition < 0");

        if (allocate == false)
        {
            if (newPosition > Length) throw new ArgumentOutOfRangeException("newPosition > Size");
        }
        else
        {
            EnsureInternalBufferReserved(newPosition);
            Length = Math.Max(newPosition, Length);
        }

        CurrentPosition = newPosition;
    }

    public FastMemoryBuffer<T> SeekToBegin()
    {
        Seek(0, SeekOrigin.Begin);
        return this;
    }

    public FastMemoryBuffer<T> SeekToEnd()
    {
        Seek(0, SeekOrigin.End);
        return this;
    }

    public int Read(Span<T> dest, int size = DefaultSize, bool allowPartial = false)
    {
        size = size._DefaultSize(dest.Length);
        if (dest.Length < size) throw new ArgumentException("dest.Length < size");
        var span = Read(size, allowPartial);
        span.CopyTo(dest);
        return span.Length;
    }

    public int Peek(Span<T> dest, int size = DefaultSize, bool allowPartial = false)
    {
        size = size._DefaultSize(dest.Length);
        if (dest.Length < size) throw new ArgumentException("dest.Length < size");
        var span = Peek(size, allowPartial);
        span.CopyTo(dest);
        return span.Length;
    }

    public int Read(T[] dest, int offset, int size, bool allowPartial = false) => Read(dest.AsSpan(offset, size), size, allowPartial);
    public int Peek(T[] dest, int offset, int size, bool allowPartial = false) => Peek(dest.AsSpan(offset, size), size, allowPartial);

    public int Read(Memory<T> dest, int size = DefaultSize, bool allowPartial = false)
    {
        size = size._DefaultSize(dest.Length);
        if (dest.Length < size) throw new ArgumentException("dest.Length < size");
        var span = PeekAsMemory(size, allowPartial);
        span.CopyTo(dest);
        return span.Length;
    }

    public int Peek(Memory<T> dest, int size = DefaultSize, bool allowPartial = false)
    {
        size = size._DefaultSize(dest.Length);
        if (dest.Length < size) throw new ArgumentException("dest.Length < size");
        var span = PeekAsMemory(size, allowPartial);
        span.CopyTo(dest);
        return span.Length;
    }

    public void EnsureInternalBufferReserved(int newSize)
    {
        if (InternalBuffer.Length >= newSize) return;

        int newInternalSize = Util.GetGreaterOrEqualOptimiedSizePowerOf2(Math.Max(newSize, BufferConsts.InitialBufferSize));

        InternalBuffer = InternalBuffer._ReAlloc(newInternalSize, this.Length);
        InternalSpan = InternalBuffer.Span;
    }

    public void OptimizeInternalBufferSize()
    {
        int newInternalSize = Util.GetGreaterOrEqualOptimiedSizePowerOf2(Math.Max(this.Length, BufferConsts.InitialBufferSize));

        if (this.InternalBuffer.Length > newInternalSize)
        {
            InternalBuffer = InternalBuffer._ReAlloc(newInternalSize, this.Length);
            InternalSpan = InternalBuffer.Span;
        }
    }

    public void SetLength(int size)
    {
        if (size < 0) throw new ArgumentOutOfRangeException("size < 0");

        EnsureInternalBufferReserved(size);
        this.Length = size;
        if (this.CurrentPosition > this.Length)
            this.CurrentPosition = this.Length;
    }

    public void Clear()
    {
        InternalBuffer = new Memory<T>();
        CurrentPosition = 0;
        Length = 0;
        InternalSpan = new Span<T>();
    }

    public void WriteOne(T data)
    {
        var span = Walk(1);
        span[0] = data;
    }

    public T ReadOne()
    {
        var span = Read(1, false);
        return span[0];
    }

    public T PeekOne()
    {
        var span = Peek(1, false);
        return span[0];
    }
}

public ref struct FastReadOnlyMemoryBuffer<T>
{
    ReadOnlyMemory<T> InternalBuffer;
    ReadOnlySpan<T> InternalSpan;
    public int CurrentPosition { get; private set; }
    public int Length { get; private set; }
    public bool Empty => Length == 0;
    public int InternalBufferSize => InternalBuffer.Length;

    public ReadOnlyMemory<T> Memory { get => InternalBuffer.Slice(0, Length); }
    public ReadOnlyMemory<T> MemoryBefore { get => Memory.Slice(0, CurrentPosition); }
    public ReadOnlyMemory<T> MemoryAfter { get => Memory.Slice(CurrentPosition); }

    public ReadOnlySpan<T> Span { get => InternalBuffer.Slice(0, Length).Span; }
    public ReadOnlySpan<T> SpanBefore { get => Memory.Slice(0, CurrentPosition).Span; }
    public ReadOnlySpan<T> SpanAfter { get => Memory.Slice(CurrentPosition).Span; }

    public FastReadOnlyMemoryBuffer(int size = 0) : this(new T[size]) { }

    public FastReadOnlyMemoryBuffer(ReadOnlyMemory<T> baseMemory)
    {
        InternalBuffer = baseMemory;
        CurrentPosition = 0;
        Length = baseMemory.Length;
        InternalSpan = InternalBuffer.Span;
    }

    public static unsafe FastReadOnlyMemoryBuffer<byte> FromStruct<TStruct>(TStruct src) where TStruct : unmanaged
    {
        Memory<byte> baseMemory = new byte[sizeof(TStruct)];
        ref TStruct dst = ref baseMemory._AsStruct<TStruct>();
        dst = src;
        return new FastReadOnlyMemoryBuffer<byte>(baseMemory);
    }

    static T dummyRefValue = default!;
    public ref readonly T GetRefForFixedPtr(int position = 0)
    {
        var span = this.Span.Slice(position);
        if (span.IsEmpty)
            return ref dummyRefValue;
        return ref span[0];
    }

    public static implicit operator FastReadOnlyMemoryBuffer<T>(ReadOnlyMemory<T> memory) => new FastReadOnlyMemoryBuffer<T>(memory);
    public static implicit operator FastReadOnlyMemoryBuffer<T>(T[] array) => new FastReadOnlyMemoryBuffer<T>(array.AsMemory());
    public static implicit operator ReadOnlyMemory<T>(FastReadOnlyMemoryBuffer<T> buf) => buf.Memory;
    public static implicit operator ReadOnlySpan<T>(FastReadOnlyMemoryBuffer<T> buf) => buf.Span;
    public static implicit operator ReadOnlySpanBuffer<T>(FastReadOnlyMemoryBuffer<T> buf) => buf.AsReadOnlySpanBuffer();

    public FastReadOnlyMemoryBuffer<T> SliceAfter() => Slice(CurrentPosition);
    public FastReadOnlyMemoryBuffer<T> SliceBefore() => Slice(0, CurrentPosition);
    public FastReadOnlyMemoryBuffer<T> Slice(int start) => Slice(start, this.Length - start);
    public FastReadOnlyMemoryBuffer<T> Slice(int start, int length)
    {
        if (start < 0) throw new ArgumentOutOfRangeException("start < 0");
        if (length < 0) throw new ArgumentOutOfRangeException("length < 0");
        if (start > Length) throw new ArgumentOutOfRangeException("start > Size");
        if (checked(start + length) > Length) throw new ArgumentOutOfRangeException("length > Size");
        FastReadOnlyMemoryBuffer<T> ret = new FastReadOnlyMemoryBuffer<T>(this.InternalBuffer.Slice(start, length));
        ret.Length = length;
        ret.CurrentPosition = Math.Max(checked(CurrentPosition - start), 0);
        return ret;
    }

    public FastReadOnlyMemoryBuffer<T> Clone()
    {
        FastReadOnlyMemoryBuffer<T> ret = new FastReadOnlyMemoryBuffer<T>(InternalSpan.ToArray());
        ret.Length = Length;
        ret.CurrentPosition = CurrentPosition;
        return ret;
    }

    public FastMemoryBuffer<T> CloneAsWritable()
    {
        FastMemoryBuffer<T> ret = new FastMemoryBuffer<T>(Span.ToArray());
        ret.Seek(CurrentPosition, SeekOrigin.Begin);
        return ret;
    }

    public ReadOnlySpanBuffer<T> AsReadOnlySpanBuffer()
    {
        ReadOnlySpanBuffer<T> ret = new ReadOnlySpanBuffer<T>(Span);
        ret.Seek(CurrentPosition, SeekOrigin.Begin);
        return ret;
    }

    public ReadOnlySpan<T> Read(int size, bool allowPartial = false)
    {
        int sizeRead = size;
        if (checked(CurrentPosition + size) > Length)
        {
            if (allowPartial == false) throw new ArgumentOutOfRangeException("(CurrentPosition + size) > Size");
            sizeRead = Length - CurrentPosition;
        }

        ReadOnlySpan<T> ret = InternalSpan.Slice(CurrentPosition, sizeRead);
        CurrentPosition += sizeRead;
        return ret;
    }

    public ReadOnlySpan<T> Peek(int size, bool allowPartial = false)
    {
        int sizeRead = size;
        if (checked(CurrentPosition + size) > Length)
        {
            if (allowPartial == false) throw new ArgumentOutOfRangeException("(CurrentPosition + size) > Size");
            sizeRead = Length - CurrentPosition;
        }

        ReadOnlySpan<T> ret = InternalSpan.Slice(CurrentPosition, sizeRead);
        return ret;
    }

    public ReadOnlyMemory<T> ReadAsMemory(int size, bool allowPartial = false)
    {
        int sizeRead = size;
        if (checked(CurrentPosition + size) > Length)
        {
            if (allowPartial == false) throw new ArgumentOutOfRangeException("(CurrentPosition + size) > Size");
            sizeRead = Length - CurrentPosition;
        }

        ReadOnlyMemory<T> ret = InternalBuffer.Slice(CurrentPosition, sizeRead);
        CurrentPosition += sizeRead;
        return ret;
    }

    public ReadOnlyMemory<T> PeekAsMemory(int size, bool allowPartial = false)
    {
        int sizeRead = size;
        if (checked(CurrentPosition + size) > Length)
        {
            if (allowPartial == false) throw new ArgumentOutOfRangeException("(CurrentPosition + size) > Size");
            sizeRead = Length - CurrentPosition;
        }

        ReadOnlyMemory<T> ret = InternalBuffer.Slice(CurrentPosition, sizeRead);
        return ret;
    }

    public void Seek(int offset, SeekOrigin mode, bool allocate = false)
    {
        int newPosition = 0;
        if (mode == SeekOrigin.Current)
            newPosition = checked(CurrentPosition + offset);
        else if (mode == SeekOrigin.End)
            newPosition = checked(Length + offset);
        else
            newPosition = offset;

        if (newPosition < 0) throw new ArgumentOutOfRangeException("newPosition < 0");
        if (newPosition > Length) throw new ArgumentOutOfRangeException("newPosition > Size");

        CurrentPosition = newPosition;
    }

    public FastReadOnlyMemoryBuffer<T> SeekToBegin()
    {
        Seek(0, SeekOrigin.Begin);
        return this;
    }

    public FastReadOnlyMemoryBuffer<T> SeekToEnd()
    {
        Seek(0, SeekOrigin.End);
        return this;
    }

    public int Read(Span<T> dest, int size = DefaultSize, bool allowPartial = false)
    {
        size = size._DefaultSize(dest.Length);
        if (dest.Length < size) throw new ArgumentException("dest.Length < size");
        var span = Read(size, allowPartial);
        span.CopyTo(dest);
        return span.Length;
    }

    public int Peek(Span<T> dest, int size = DefaultSize, bool allowPartial = false)
    {
        size = size._DefaultSize(dest.Length);
        if (dest.Length < size) throw new ArgumentException("dest.Length < size");
        var span = Peek(size, allowPartial);
        span.CopyTo(dest);
        return span.Length;
    }

    public int Read(T[] dest, int offset, int size, bool allowPartial = false) => Read(dest.AsSpan(offset, size), size, allowPartial);
    public int Peek(T[] dest, int offset, int size, bool allowPartial = false) => Peek(dest.AsSpan(offset, size), size, allowPartial);

    public int Read(Memory<T> dest, int size = DefaultSize, bool allowPartial = false)
    {
        size = size._DefaultSize(dest.Length);
        if (dest.Length < size) throw new ArgumentException("dest.Length < size");
        var span = ReadAsMemory(size, allowPartial);
        span.CopyTo(dest);
        return span.Length;
    }

    public int Peek(Memory<T> dest, int size = DefaultSize, bool allowPartial = false)
    {
        size = size._DefaultSize(dest.Length);
        if (dest.Length < size) throw new ArgumentException("dest.Length < size");
        var span = PeekAsMemory(size, allowPartial);
        span.CopyTo(dest);
        return span.Length;
    }

    public void Clear()
    {
        InternalBuffer = new ReadOnlyMemory<T>();
        CurrentPosition = 0;
        Length = 0;
        InternalSpan = new Span<T>();
    }
}

public interface IBuffer<T> : IEmptyChecker
{
    long LongPosition { get; }
    long LongLength { get; }
    long LongInternalBufferSize { get; }
    long LongRemainLength { get; }

    void Write(ReadOnlySpan<T> data);
    void WriteOne(T data);
    ReadOnlySpan<T> Read(long size, bool allowPartial = false);
    ReadOnlyMemory<T> ReadAsMemory(long size, bool allowPartial = false);
    ReadOnlySpan<T> Peek(long size, bool allowPartial = false);
    public ReadOnlyMemory<T> PeekAsMemory(long size, bool allowPartial = false);
    T ReadOne();
    T PeekOne();
    void Seek(long offset, SeekOrigin mode, bool allocate = false);
    void SetLength(long size);
    void Clear();
    void Flush();
}

// 任意の Stream 型を IBuffer<byte> 型に変換するクラス
public class StreamBasedBuffer : IBuffer<byte>, IDisposable
{
    public Stream BaseStream { get; }
    public bool AutoDispose { get; }
    public bool IsMemoryStream { get; }

    IHolder LeakHolder;

    public StreamBasedBuffer(Stream baseStream, bool autoDispose = false, bool useBuffering = false)
    {
        try
        {
            LeakHolder = LeakChecker.Enter(LeakCounterKind.StreamBasedBuffer);

            if (baseStream.CanSeek == false) throw new ArgumentException("Cannot seek", nameof(baseStream));

            this.IsMemoryStream = baseStream is MemoryStream;

            if (useBuffering == false)
            {
                this.BaseStream = baseStream;
            }
            else
            {
                this.BaseStream = new BufferedStream(baseStream);
            }

            this.AutoDispose = autoDispose;
        }
        catch
        {
            this._DisposeSafe();
            throw;
        }
    }

    public void Dispose() { this.Dispose(true); GC.SuppressFinalize(this); }
    Once DisposeFlag;
    protected virtual void Dispose(bool disposing)
    {
        if (!disposing || DisposeFlag.IsFirstCall() == false) return;

        if (this.AutoDispose)
        {
            this.BaseStream._DisposeSafe();
        }

        LeakHolder._DisposeSafe();
    }

    public long LongPosition => BaseStream.Position;

    public long LongLength => BaseStream.Length;

    public long LongInternalBufferSize => BaseStream.Length;

    public long LongRemainLength
    {
        [MethodImpl(Inline)]
        get
        {
            long length = LongLength;
            long position = LongPosition;
            long ret = length - position;
            if (ret < 0) throw new CoresException($"Position ({length}) > Length ({position})");
            return ret;
        }
    }

    public int RemainLength
    {
        [MethodImpl(Inline)]
        get => checked((int)4);
    }

    public void Clear()
    {
        BaseStream.Seek(0, SeekOrigin.Begin);
        BaseStream.SetLength(0);
    }

    public bool IsThisEmpty()
    {
        return BaseStream.Length == 0;
    }

    public ReadOnlyMemory<byte> PeekAsMemory(long size, bool allowPartial = false)
    {
        checked
        {
            int sizeToRead = (int)size;

            long currentPosition = LongPosition;

            try
            {
                return ReadAsMemory(size, allowPartial);
            }
            finally
            {
                Seek(currentPosition, SeekOrigin.Begin, false);
            }
        }
    }
    public ReadOnlySpan<byte> Peek(long size, bool allowPartial = false)
        => PeekAsMemory(size, allowPartial).Span;

    public byte PeekOne()
    {
        var tmp = Peek(1, false);
        Debug.Assert(tmp.Length == 1);
        return tmp[0];
    }

    public ReadOnlyMemory<byte> ReadAsMemory(long size, bool allowPartial = false)
    {
        checked
        {
            int sizeToRead = (int)size;

            Memory<byte> buf = new byte[sizeToRead];

            int resultSize = BaseStream.Read(buf.Span);

            if (resultSize == 0)
            {
                // 最後に到達した
                if (allowPartial == false)
                    throw new CoresException("End of stream");
                else
                    return ReadOnlyMemory<byte>.Empty;
            }

            Debug.Assert(resultSize <= sizeToRead);

            if (allowPartial == false)
            {
                if (resultSize < sizeToRead)
                {
                    // 巻き戻す
                    if (resultSize >= 1)
                    {
                        BaseStream.Seek(-resultSize, SeekOrigin.Current);
                    }
                    throw new CoresException($"resultSize ({resultSize}) < sizeToRead ({sizeToRead})");
                }
            }

            if (resultSize != sizeToRead)
            {
                buf = buf.Slice(0, resultSize);
            }

            return buf;
        }
    }
    public ReadOnlySpan<byte> Read(long size, bool allowPartial = false)
        => ReadAsMemory(size, allowPartial).Span;

    public byte ReadOne()
    {
        var tmp = Read(1, false);
        Debug.Assert(tmp.Length == 1);
        return tmp[0];
    }

    public void Seek(long offset, SeekOrigin mode, bool allocate = false)
    {
        checked
        {
            long currentPosition = LongPosition;
            long newPosition;
            long currentLength = LongLength;
            long newLength = currentLength;

            if (mode == SeekOrigin.Current)
                newPosition = checked(currentPosition + offset);
            else if (mode == SeekOrigin.End)
                newPosition = checked(currentLength + offset);
            else
                newPosition = offset;

            if (newPosition < 0) throw new ArgumentOutOfRangeException("newPosition < 0");

            if (allocate == false)
            {
                if (newPosition > currentLength) throw new ArgumentOutOfRangeException("newPosition > Size");
            }
            else
            {
                newLength = Math.Max(newPosition, currentLength);
            }

            if (currentLength != newLength)
            {
                BaseStream.SetLength(newLength);
            }

            if (currentPosition != newPosition)
            {
                long ret = BaseStream.Seek(newPosition, SeekOrigin.Begin);

                if (ret != newPosition)
                {
                    throw new CoresException($"ret {ret} != newPosition {newPosition}");
                }
            }
        }
    }

    public void SetLength(long size)
    {
        BaseStream.SetLength(size);
    }

    public void Write(ReadOnlySpan<byte> data)
    {
        BaseStream.Write(data);
    }

    public void WriteOne(byte data)
    {
        Write(data._SingleArray());
    }

    public void Flush()
    {
        BaseStream.Flush();
    }
}


#pragma warning disable CS1998
public class BufferBasedStream : Stream
{
    public IBuffer<byte> BaseBuffer { get; }

    public BufferBasedStream(IBuffer<byte> baseBuffer)
    {
        BaseBuffer = baseBuffer;
    }

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => true;
    public override long Length => BaseBuffer.LongLength;

    public override long Position
    {
        get => BaseBuffer.LongPosition;
        set => Seek(value, SeekOrigin.Begin);
    }

    public override void Flush()
    {
        BaseBuffer.Flush();
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        BaseBuffer.Seek(offset, origin, true);
        return BaseBuffer.LongPosition;
    }

    public override void SetLength(long value)
        => BaseBuffer.SetLength(value);

    public override Task FlushAsync(CancellationToken cancellationToken)
    {
        this.Flush();
        return Task.CompletedTask;
    }

    override public int Read(Span<byte> buffer)
    {
        var readSpan = BaseBuffer.Read(buffer.Length, true);
        readSpan.CopyTo(buffer);
        return readSpan.Length;
    }


    override public void Write(ReadOnlySpan<byte> buffer)
    {
        BaseBuffer.Write(buffer);
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(this.Read(buffer, offset, count));
    }


    override public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return this.Read(buffer.Span);
    }

    public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        this.Write(buffer, offset, count);
    }

    override public async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        this.Write(buffer.Span);
    }
}
#pragma warning restore CS1998

public class MemoryBuffer<T> : IBuffer<T>
{
    Memory<T> InternalBuffer;
    public int CurrentPosition { get; private set; }
    public int Length { get; private set; }
    public bool IsThisEmpty() => Length == 0;
    public int InternalBufferSize => InternalBuffer.Length;

    public Memory<T> Memory { get => InternalBuffer.Slice(0, Length); }
    public Memory<T> MemoryBefore { get => Memory.Slice(0, CurrentPosition); }
    public Memory<T> MemoryAfter { get => Memory.Slice(CurrentPosition); }

    public Span<T> Span { get => InternalBuffer.Slice(0, Length).Span; }
    public Span<T> SpanBefore { get => Memory.Slice(0, CurrentPosition).Span; }
    public Span<T> SpanAfter { get => Memory.Slice(CurrentPosition).Span; }

    public long LongPosition => this.CurrentPosition;

    public long LongLength => this.Length;

    public long LongInternalBufferSize => this.InternalBufferSize;

    public long LongRemainLength
    {
        [MethodImpl(Inline)]
        get
        {
            long length = LongLength;
            long position = LongPosition;
            long ret = length - position;
            if (ret < 0) throw new CoresException($"Position ({length}) > Length ({position})");
            return ret;
        }
    }

    public int RemainLength
    {
        [MethodImpl(Inline)]
        get => checked((int)LongRemainLength);
    }

    public MemoryBuffer(EnsureSpecial withReserve, int reversedSized) : this(new T[reversedSized])
    {
        this.Length = 0;
    }

    public MemoryBuffer(int size = 0) : this(new T[size]) { }

    public MemoryBuffer(Memory<T> baseMemory)
    {
        InternalBuffer = baseMemory;
        CurrentPosition = 0;
        Length = baseMemory.Length;
    }

    public static unsafe MemoryBuffer<byte> FromStruct<TStruct>(TStruct src) where TStruct : unmanaged
    {
        Memory<byte> baseMemory = new byte[sizeof(TStruct)];
        ref TStruct dst = ref baseMemory._AsStruct<TStruct>();
        dst = src;
        return new MemoryBuffer<byte>(baseMemory);
    }

    LazyCriticalSection PinLockObj;
    int PinLockedCounter = 0;
    MemoryHandle PinHandle;
    public bool IsPinLocked() => (PinLockedCounter != 0);
    public ValueHolder PinLock()
    {
        lock (PinLockObj.LockObj)
        {
            if (PinLockedCounter == 0)
            {
                PinHandle = InternalBuffer.Pin();
            }
            PinLockedCounter++;
        }

        return new ValueHolder(() =>
        {
            lock (PinLockObj.LockObj)
            {
                Debug.Assert(PinLockedCounter >= 1);
                PinLockedCounter--;
                if (PinLockedCounter == 0)
                {
                    PinHandle.Dispose();
                    PinHandle = default;
                }
            }
        },
        LeakCounterKind.PinnedMemory);
    }

    static T dummyRefValue = default!;
    public ref T GetRefForFixedPtr(int position = 0)
    {
        var span = this.Span.Slice(position);
        if (span.IsEmpty)
            return ref dummyRefValue;
        return ref span[0];
    }

    public static implicit operator MemoryBuffer<T>(Memory<T> memory) => new MemoryBuffer<T>(memory);
    public static implicit operator MemoryBuffer<T>(T[] array) => new MemoryBuffer<T>(array.AsMemory());
    public static implicit operator Memory<T>(MemoryBuffer<T> buf) => buf.Memory;
    public static implicit operator Span<T>(MemoryBuffer<T> buf) => buf.Span;
    public static implicit operator ReadOnlyMemory<T>(MemoryBuffer<T> buf) => buf.Memory;
    public static implicit operator ReadOnlySpan<T>(MemoryBuffer<T> buf) => buf.Span;
    public static implicit operator ReadOnlyMemoryBuffer<T>(MemoryBuffer<T> buf) => buf.AsReadOnly();
    public static implicit operator ReadOnlySpanBuffer<T>(MemoryBuffer<T> buf) => buf.AsReadOnlySpanBuffer();

    public MemoryBuffer<T> SliceAfter() => Slice(CurrentPosition);
    public MemoryBuffer<T> SliceBefore() => Slice(0, CurrentPosition);
    public MemoryBuffer<T> Slice(int start) => Slice(start, this.Length - start);
    public MemoryBuffer<T> Slice(int start, int length)
    {
        if (start < 0) throw new ArgumentOutOfRangeException("start < 0");
        if (length < 0) throw new ArgumentOutOfRangeException("length < 0");
        if (start > Length) throw new ArgumentOutOfRangeException("start > Size");
        if (checked(start + length) > Length) throw new ArgumentOutOfRangeException("length > Size");
        MemoryBuffer<T> ret = new MemoryBuffer<T>(this.InternalBuffer.Slice(start, length));
        ret.Length = length;
        ret.CurrentPosition = Math.Max(checked(CurrentPosition - start), 0);
        return ret;
    }

    public MemoryBuffer<T> Clone()
    {
        MemoryBuffer<T> ret = new MemoryBuffer<T>(InternalBuffer.ToArray());
        ret.Length = Length;
        ret.CurrentPosition = CurrentPosition;
        return ret;
    }

    public ReadOnlyMemoryBuffer<T> AsReadOnly()
    {
        ReadOnlyMemoryBuffer<T> ret = new ReadOnlyMemoryBuffer<T>(Memory);
        ret.Seek(CurrentPosition, SeekOrigin.Begin);
        return ret;
    }

    public SpanBuffer<T> AsSpanBuffer()
    {
        SpanBuffer<T> ret = new SpanBuffer<T>(Span);
        ret.Seek(CurrentPosition, SeekOrigin.Begin);
        return ret;
    }

    public ReadOnlySpanBuffer<T> AsReadOnlySpanBuffer()
    {
        ReadOnlySpanBuffer<T> ret = new ReadOnlySpanBuffer<T>(Span);
        ret.Seek(CurrentPosition, SeekOrigin.Begin);
        return ret;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public Span<T> Walk(int size, bool noMove = false)
    {
        int newSize = checked(CurrentPosition + size);
        if (InternalBuffer.Length < newSize)
        {
            EnsureInternalBufferReserved(newSize);
        }
        var ret = InternalBuffer.Span.Slice(CurrentPosition, size);
        Length = Math.Max(newSize, Length);
        if (noMove == false) CurrentPosition += size;
        return ret;
    }

    public void Write(T[] data, int offset = 0, int length = DefaultSize) => Write(data.AsSpan(offset, length._DefaultSize(data.Length - offset)));
    public void Write(Memory<T> data) => Write(data.Span);
    public void Write(ReadOnlyMemory<T> data) => Write(data.Span);

    public unsafe void Write(void* ptr, int size)
    {
        Span<T> span = Walk(size);
        ref T t = ref span[0];
        void* dst = Unsafe.AsPointer(ref t);
        Unsafe.CopyBlock(dst, ptr, (uint)size);
    }

    public void Write(Span<T> data)
    {
        var span = Walk(data.Length);
        data.CopyTo(span);
    }

    public void Write(ReadOnlySpan<T> data)
    {
        var span = Walk(data.Length);
        data.CopyTo(span);
    }

    public void WriteOne(T data)
    {
        var span = Walk(1);
        span[0] = data;
    }

    public T ReadOne()
    {
        var span = Read(1, false);
        return span[0];
    }

    public T PeekOne()
    {
        var span = Peek(1, false);
        return span[0];
    }

    public void WriteZero(int length)
    {
        var zeroSpan = ZeroedMemory<T>.Memory.Span;
        while (length >= 1)
        {
            int currentSize = Math.Min(length, zeroSpan.Length);
            this.Write(zeroSpan.Slice(0, currentSize));
            length -= currentSize;
        }
    }

    public ReadOnlySpan<T> Read(int size, bool allowPartial = false)
    {
        int sizeRead = size;
        if (checked(CurrentPosition + size) > Length)
        {
            if (allowPartial == false) throw new ArgumentOutOfRangeException("(CurrentPosition + size) > Size");
            sizeRead = Length - CurrentPosition;
        }

        ReadOnlySpan<T> ret = InternalBuffer.Span.Slice(CurrentPosition, sizeRead);
        CurrentPosition += sizeRead;
        return ret;
    }

    public ReadOnlySpan<T> Peek(int size, bool allowPartial = false)
    {
        int sizeRead = size;
        if (checked(CurrentPosition + size) > Length)
        {
            if (allowPartial == false) throw new ArgumentOutOfRangeException("(CurrentPosition + size) > Size");
            sizeRead = Length - CurrentPosition;
        }

        Span<T> ret = InternalBuffer.Span.Slice(CurrentPosition, sizeRead);
        return ret;
    }

    public ReadOnlyMemory<T> ReadAsMemory(int size, bool allowPartial = false)
    {
        int sizeRead = size;
        if (checked(CurrentPosition + size) > Length)
        {
            if (allowPartial == false) throw new ArgumentOutOfRangeException("(CurrentPosition + size) > Size");
            sizeRead = Length - CurrentPosition;
        }

        ReadOnlyMemory<T> ret = InternalBuffer.Slice(CurrentPosition, sizeRead);
        CurrentPosition += sizeRead;
        return ret;
    }

    public ReadOnlyMemory<T> PeekAsMemory(int size, bool allowPartial = false)
    {
        int sizeRead = size;
        if (checked(CurrentPosition + size) > Length)
        {
            if (allowPartial == false) throw new ArgumentOutOfRangeException("(CurrentPosition + size) > Size");
            sizeRead = Length - CurrentPosition;
        }

        ReadOnlyMemory<T> ret = InternalBuffer.Slice(CurrentPosition, sizeRead);
        return ret;
    }

    public void Seek(int offset, SeekOrigin mode, bool allocate = false)
    {
        int newPosition = 0;
        if (mode == SeekOrigin.Current)
            newPosition = checked(CurrentPosition + offset);
        else if (mode == SeekOrigin.End)
            newPosition = checked(Length + offset);
        else
            newPosition = offset;

        if (newPosition < 0) throw new ArgumentOutOfRangeException("newPosition < 0");

        if (allocate == false)
        {
            if (newPosition > Length) throw new ArgumentOutOfRangeException("newPosition > Size");
        }
        else
        {
            EnsureInternalBufferReserved(newPosition);
            Length = Math.Max(newPosition, Length);
        }

        CurrentPosition = newPosition;
    }

    public void SetLength(int size)
    {
        if (size < 0) throw new ArgumentOutOfRangeException("size < 0");

        EnsureInternalBufferReserved(size);
        this.Length = size;
        if (this.CurrentPosition > this.Length)
            this.CurrentPosition = this.Length;
    }

    public MemoryBuffer<T> SeekToBegin()
    {
        Seek(0, SeekOrigin.Begin);
        return this;
    }

    public MemoryBuffer<T> SeekToEnd()
    {
        Seek(0, SeekOrigin.End);
        return this;
    }

    public int Read(Span<T> dest, int size = DefaultSize, bool allowPartial = false)
    {
        size = size._DefaultSize(dest.Length);
        if (dest.Length < size) throw new ArgumentException("dest.Length < size");
        var span = Read(size, allowPartial);
        span.CopyTo(dest);
        return span.Length;
    }

    public int Peek(Span<T> dest, int size = DefaultSize, bool allowPartial = false)
    {
        size = size._DefaultSize(dest.Length);
        if (dest.Length < size) throw new ArgumentException("dest.Length < size");
        var span = Peek(size, allowPartial);
        span.CopyTo(dest);
        return span.Length;
    }

    public int Read(T[] dest, int offset, int size, bool allowPartial = false) => Read(dest.AsSpan(offset, size), size, allowPartial);
    public int Peek(T[] dest, int offset, int size, bool allowPartial = false) => Peek(dest.AsSpan(offset, size), size, allowPartial);

    public int Read(Memory<T> dest, int size = DefaultSize, bool allowPartial = false)
    {
        size = size._DefaultSize(dest.Length);
        if (dest.Length < size) throw new ArgumentException("dest.Length < size");
        var span = PeekAsMemory(size, allowPartial);
        span.CopyTo(dest);
        return span.Length;
    }

    public int Peek(Memory<T> dest, int size = DefaultSize, bool allowPartial = false)
    {
        size = size._DefaultSize(dest.Length);
        if (dest.Length < size) throw new ArgumentException("dest.Length < size");
        var span = PeekAsMemory(size, allowPartial);
        span.CopyTo(dest);
        return span.Length;
    }

    public void EnsureInternalBufferReserved(int newSize)
    {
        if (InternalBuffer.Length >= newSize) return;

        int newInternalSize = Util.GetGreaterOrEqualOptimiedSizePowerOf2(Math.Max(newSize, BufferConsts.InitialBufferSize));

        if (IsPinLocked()) throw new ApplicationException("Memory pin is locked.");

        InternalBuffer = InternalBuffer._ReAlloc(newInternalSize, this.Length);
    }

    public void OptimizeInternalBufferSize()
    {
        if (IsPinLocked()) throw new ApplicationException("Memory pin is locked.");

        int newInternalSize = Util.GetGreaterOrEqualOptimiedSizePowerOf2(Math.Max(this.Length, BufferConsts.InitialBufferSize));

        if (this.InternalBuffer.Length > newInternalSize)
            InternalBuffer = InternalBuffer._ReAlloc(newInternalSize, this.Length);
    }

    public void Clear()
    {
        if (IsPinLocked()) throw new ApplicationException("Memory pin is locked.");

        InternalBuffer = new Memory<T>();
        CurrentPosition = 0;
        Length = 0;
    }

    public ReadOnlySpan<T> Read(long size, bool allowPartial = false)
        => Read(checked((int)size), allowPartial);

    public ReadOnlySpan<T> Peek(long size, bool allowPartial = false)
        => Peek(checked((int)size), allowPartial);

    public ReadOnlyMemory<T> ReadAsMemory(long size, bool allowPartial = false)
        => ReadAsMemory(checked((int)size), allowPartial);

    public ReadOnlyMemory<T> PeekAsMemory(long size, bool allowPartial = false)
        => PeekAsMemory(checked((int)size), allowPartial);

    public void Seek(long offset, SeekOrigin mode, bool allocate = false)
        => Seek(checked((int)offset), mode, allocate);

    public void SetLength(long size)
        => SetLength(checked((int)size));

    public void Flush() { }
}

public class ReadOnlyMemoryBuffer<T> : IBuffer<T>
{
    ReadOnlyMemory<T> InternalBuffer;
    public int CurrentPosition { get; private set; }
    public int Length { get; private set; }
    public bool IsThisEmpty() => Length == 0;
    public int InternalBufferSize => InternalBuffer.Length;

    public ReadOnlyMemory<T> Memory { get => InternalBuffer.Slice(0, Length); }
    public ReadOnlyMemory<T> MemoryBefore { get => Memory.Slice(0, CurrentPosition); }
    public ReadOnlyMemory<T> MemoryAfter { get => Memory.Slice(CurrentPosition); }

    public ReadOnlySpan<T> Span { get => InternalBuffer.Slice(0, Length).Span; }
    public ReadOnlySpan<T> SpanBefore { get => Memory.Slice(0, CurrentPosition).Span; }
    public ReadOnlySpan<T> SpanAfter { get => Memory.Slice(CurrentPosition).Span; }

    public long LongPosition => this.CurrentPosition;

    public long LongLength => this.Length;

    public long LongInternalBufferSize => this.InternalBufferSize;

    public long LongRemainLength
    {
        [MethodImpl(Inline)]
        get
        {
            long length = LongLength;
            long position = LongPosition;
            long ret = length - position;
            if (ret < 0) throw new CoresException($"Position ({length}) > Length ({position})");
            return ret;
        }
    }

    public int RemainLength
    {
        [MethodImpl(Inline)]
        get => checked((int)LongRemainLength);
    }

    public ReadOnlyMemoryBuffer(int size = 0) : this(new T[size]) { }

    public ReadOnlyMemoryBuffer(ReadOnlyMemory<T> baseMemory)
    {
        InternalBuffer = baseMemory;
        CurrentPosition = 0;
        Length = baseMemory.Length;
    }

    public static unsafe ReadOnlyMemoryBuffer<byte> FromStruct<TStruct>(TStruct src) where TStruct : unmanaged
    {
        Memory<byte> baseMemory = new byte[sizeof(TStruct)];
        ref TStruct dst = ref baseMemory._AsStruct<TStruct>();
        dst = src;
        return new ReadOnlyMemoryBuffer<byte>(baseMemory);
    }

    readonly CriticalSection PinLockObj = new CriticalSection<ReadOnlyMemoryBuffer<T>>();
    int PinLockedCounter = 0;
    MemoryHandle PinHandle;
    public bool IsPinLocked() => (PinLockedCounter != 0);
    public ValueHolder PinLock()
    {
        lock (PinLockObj)
        {
            if (PinLockedCounter == 0)
                PinHandle = InternalBuffer.Pin();
            PinLockedCounter++;
        }

        return new ValueHolder(() =>
        {
            lock (PinLockObj)
            {
                Debug.Assert(PinLockedCounter >= 1);
                PinLockedCounter--;
                if (PinLockedCounter == 0)
                {
                    PinHandle.Dispose();
                    PinHandle = default;
                }
            }
        },
        LeakCounterKind.PinnedMemory);
    }

    static T dummyRefValue = default!;
    public ref readonly T GetRefForFixedPtr(int position = 0)
    {
        var span = this.Span.Slice(position);
        if (span.IsEmpty)
            return ref dummyRefValue;
        return ref span[0];
    }

    public static implicit operator ReadOnlyMemoryBuffer<T>(ReadOnlyMemory<T> memory) => new ReadOnlyMemoryBuffer<T>(memory);
    public static implicit operator ReadOnlyMemoryBuffer<T>(T[] array) => new ReadOnlyMemoryBuffer<T>(array.AsMemory());
    public static implicit operator ReadOnlyMemory<T>(ReadOnlyMemoryBuffer<T> buf) => buf.Memory;
    public static implicit operator ReadOnlySpan<T>(ReadOnlyMemoryBuffer<T> buf) => buf.Span;
    public static implicit operator ReadOnlySpanBuffer<T>(ReadOnlyMemoryBuffer<T> buf) => buf.AsReadOnlySpanBuffer();

    public ReadOnlyMemoryBuffer<T> SliceAfter() => Slice(CurrentPosition);
    public ReadOnlyMemoryBuffer<T> SliceBefore() => Slice(0, CurrentPosition);
    public ReadOnlyMemoryBuffer<T> Slice(int start) => Slice(start, this.Length - start);
    public ReadOnlyMemoryBuffer<T> Slice(int start, int length)
    {
        if (start < 0) throw new ArgumentOutOfRangeException("start < 0");
        if (length < 0) throw new ArgumentOutOfRangeException("length < 0");
        if (start > Length) throw new ArgumentOutOfRangeException("start > Size");
        if (checked(start + length) > Length) throw new ArgumentOutOfRangeException("length > Size");
        ReadOnlyMemoryBuffer<T> ret = new ReadOnlyMemoryBuffer<T>(this.InternalBuffer.Slice(start, length));
        ret.Length = length;
        ret.CurrentPosition = Math.Max(checked(CurrentPosition - start), 0);
        return ret;
    }

    public ReadOnlyMemoryBuffer<T> Clone()
    {
        ReadOnlyMemoryBuffer<T> ret = new ReadOnlyMemoryBuffer<T>(InternalBuffer.ToArray());
        ret.Length = Length;
        ret.CurrentPosition = CurrentPosition;
        return ret;
    }

    public MemoryBuffer<T> CloneAsWritable()
    {
        MemoryBuffer<T> ret = new MemoryBuffer<T>(Span.ToArray());
        ret.Seek(CurrentPosition, SeekOrigin.Begin);
        return ret;
    }

    public ReadOnlySpanBuffer<T> AsReadOnlySpanBuffer()
    {
        ReadOnlySpanBuffer<T> ret = new ReadOnlySpanBuffer<T>(Span);
        ret.Seek(CurrentPosition, SeekOrigin.Begin);
        return ret;
    }

    public ReadOnlySpan<T> Read(int size, bool allowPartial = false)
    {
        int sizeRead = size;
        if (checked(CurrentPosition + size) > Length)
        {
            if (allowPartial == false) throw new ArgumentOutOfRangeException("(CurrentPosition + size) > Size");
            sizeRead = Length - CurrentPosition;
        }

        ReadOnlySpan<T> ret = InternalBuffer.Span.Slice(CurrentPosition, sizeRead);
        CurrentPosition += sizeRead;
        return ret;
    }

    public ReadOnlySpan<T> Peek(int size, bool allowPartial = false)
    {
        int sizeRead = size;
        if (checked(CurrentPosition + size) > Length)
        {
            if (allowPartial == false) throw new ArgumentOutOfRangeException("(CurrentPosition + size) > Size");
            sizeRead = Length - CurrentPosition;
        }

        ReadOnlySpan<T> ret = InternalBuffer.Span.Slice(CurrentPosition, sizeRead);
        return ret;
    }

    public ReadOnlyMemory<T> ReadAsMemory(int size, bool allowPartial = false)
    {
        int sizeRead = size;
        if (checked(CurrentPosition + size) > Length)
        {
            if (allowPartial == false) throw new ArgumentOutOfRangeException("(CurrentPosition + size) > Size");
            sizeRead = Length - CurrentPosition;
        }

        ReadOnlyMemory<T> ret = InternalBuffer.Slice(CurrentPosition, sizeRead);
        CurrentPosition += sizeRead;
        return ret;
    }

    public ReadOnlyMemory<T> PeekAsMemory(int size, bool allowPartial = false)
    {
        int sizeRead = size;
        if (checked(CurrentPosition + size) > Length)
        {
            if (allowPartial == false) throw new ArgumentOutOfRangeException("(CurrentPosition + size) > Size");
            sizeRead = Length - CurrentPosition;
        }

        ReadOnlyMemory<T> ret = InternalBuffer.Slice(CurrentPosition, sizeRead);
        return ret;
    }

    public void Seek(int offset, SeekOrigin mode, bool allocate = false)
    {
        int newPosition = 0;
        if (mode == SeekOrigin.Current)
            newPosition = checked(CurrentPosition + offset);
        else if (mode == SeekOrigin.End)
            newPosition = checked(Length + offset);
        else
            newPosition = offset;

        if (newPosition < 0) throw new ArgumentOutOfRangeException("newPosition < 0");
        if (newPosition > Length) throw new ArgumentOutOfRangeException("newPosition > Size");

        CurrentPosition = newPosition;
    }

    public ReadOnlySpanBuffer<T> SeekToBegin()
    {
        Seek(0, SeekOrigin.Begin);
        return this;
    }

    public ReadOnlySpanBuffer<T> SeekToEnd()
    {
        Seek(0, SeekOrigin.End);
        return this;
    }

    public int Read(Span<T> dest, int size = DefaultSize, bool allowPartial = false)
    {
        size = size._DefaultSize(dest.Length);
        if (dest.Length < size) throw new ArgumentException("dest.Length < size");
        var span = Read(size, allowPartial);
        span.CopyTo(dest);
        return span.Length;
    }

    public int Peek(Span<T> dest, int size = DefaultSize, bool allowPartial = false)
    {
        size = size._DefaultSize(dest.Length);
        if (dest.Length < size) throw new ArgumentException("dest.Length < size");
        var span = Peek(size, allowPartial);
        span.CopyTo(dest);
        return span.Length;
    }

    public int Read(T[] dest, int offset, int size, bool allowPartial = false) => Read(dest.AsSpan(offset, size), size, allowPartial);
    public int Peek(T[] dest, int offset, int size, bool allowPartial = false) => Peek(dest.AsSpan(offset, size), size, allowPartial);

    public int Read(Memory<T> dest, int size = DefaultSize, bool allowPartial = false)
    {
        size = size._DefaultSize(dest.Length);
        if (dest.Length < size) throw new ArgumentException("dest.Length < size");
        var span = ReadAsMemory(size, allowPartial);
        span.CopyTo(dest);
        return span.Length;
    }

    public int Peek(Memory<T> dest, int size = DefaultSize, bool allowPartial = false)
    {
        size = size._DefaultSize(dest.Length);
        if (dest.Length < size) throw new ArgumentException("dest.Length < size");
        var span = PeekAsMemory(size, allowPartial);
        span.CopyTo(dest);
        return span.Length;
    }

    void IBuffer<T>.Clear() => throw new NotSupportedException();

    void IBuffer<T>.Write(ReadOnlySpan<T> data) => throw new NotSupportedException();

    public ReadOnlySpan<T> Read(long size, bool allowPartial = false)
        => Read(checked((int)size), allowPartial);

    public ReadOnlySpan<T> Peek(long size, bool allowPartial = false)
        => Peek(checked((int)size), allowPartial);

    public ReadOnlyMemory<T> ReadAsMemory(long size, bool allowPartial = false)
        => ReadAsMemory(checked((int)size), allowPartial);

    public ReadOnlyMemory<T> PeekAsMemory(long size, bool allowPartial = false)
        => PeekAsMemory(checked((int)size), allowPartial);

    public void Seek(long offset, SeekOrigin mode, bool allocate = false)
        => Seek(checked((int)offset), mode, allocate);

    public void SetLength(long size)
        => SetLength(checked((int)size));

    public T ReadOne()
    {
        var span = Read(1, false);
        return span[0];
    }

    public T PeekOne()
    {
        var span = Peek(1, false);
        return span[0];
    }

    void IBuffer<T>.WriteOne(T data) => throw new NotSupportedException();

    public void Flush() => throw new NotSupportedException();
}

public class HugeMemoryBufferOptions
{
    public readonly long SegmentSize;

    public HugeMemoryBufferOptions(long? segmentSize = null)
    {
        this.SegmentSize = Math.Max(1, segmentSize ?? CoresConfig.LargeMemoryBuffer.DefaultSegmentSize.Value);
    }
}

public sealed class HugeMemoryBuffer<T> : IEmptyChecker, IBuffer<T>, IRandomAccess<T>
{
    public readonly HugeMemoryBufferOptions Options;

    Dictionary<long, MemoryBuffer<T>> Segments = new Dictionary<long, MemoryBuffer<T>>();
    public long CurrentPosition { get; private set; } = 0;
    public long Length { get; private set; } = 0;
    public bool IsThisEmpty() => Length == 0;

    public long PhysicalSize => this.Segments.Values.Select(x => (long)x.InternalBufferSize).Sum();

    public long LongPosition => this.CurrentPosition;
    public long LongLength => this.Length;
    public long LongInternalBufferSize => PhysicalSize;

    public long LongRemainLength
    {
        [MethodImpl(Inline)]
        get
        {
            long length = LongLength;
            long position = LongPosition;
            long ret = length - position;
            if (ret < 0) throw new CoresException($"Position ({length}) > Length ({position})");
            return ret;
        }
    }

    public int RemainLength
    {
        [MethodImpl(Inline)]
        get => checked((int)LongRemainLength);
    }

    public AsyncLock SharedAsyncLock { get; } = new AsyncLock();

    public HugeMemoryBuffer(HugeMemoryBufferOptions? options = null, Memory<T> initialContents = default)
    {
        checked
        {
            this.Options = options ?? new HugeMemoryBufferOptions();

            if (this.Options.SegmentSize <= 0) throw new ArgumentOutOfRangeException("Options.SegmentSize <= 0");

            if (initialContents.IsEmpty == false)
            {
                Debug.Assert(Segments.Count == 0);

                long totalSize = 0;
                DividedSegmentList segList = new DividedSegmentList(0, initialContents.Length, this.Options.SegmentSize);
                foreach (var seg in segList.SegmentList)
                {
                    totalSize += seg.Size;
                    Segments.Add(seg.SegmentIndex, new MemoryBuffer<T>(initialContents.Slice((int)seg.RelativePosition, (int)seg.Size)));
                }
                Debug.Assert(totalSize == initialContents.Length);
                Length = initialContents.Length;
            }
        }
    }

    public static implicit operator HugeMemoryBuffer<T>(ReadOnlyMemory<T> readOnlyMemory) => new HugeMemoryBuffer<T>(initialContents: readOnlyMemory.ToArray());
    public static implicit operator HugeMemoryBuffer<T>(Memory<T> memory) => new HugeMemoryBuffer<T>(initialContents: memory);
    public static implicit operator HugeMemoryBuffer<T>(T[] array) => new HugeMemoryBuffer<T>(initialContents: array);

    public void Write(T[] data, int offset = 0, int length = DefaultSize) => Write(data.AsMemory(offset, length._DefaultSize(data.Length - offset)));
    public void Write(ReadOnlyMemory<T> data) => Write(data.Span);

    public void WriteRandom(long position, ReadOnlySpan<T> data)
    {
        if (position < 0) throw new ArgumentOutOfRangeException("position < 0");
        checked
        {
            long start = position;

            DividedSegmentList segList = new DividedSegmentList(start, data.Length, this.Options.SegmentSize);
            foreach (DividedSegment seg in segList.SegmentList)
            {
                var srcSpan = data.Slice((int)seg.RelativePosition, (int)seg.Size);
                var destSpan = PrepareSegment(in seg);

                Debug.Assert(srcSpan.Length == destSpan.Length);
                Debug.Assert(srcSpan.Length >= 1);

                srcSpan.CopyTo(destSpan);
            }

            this.Length = Math.Max(this.Length, start + data.Length);
        }
    }

    public void Write(ReadOnlySpan<T> data)
    {
        checked
        {
            WriteRandom(this.CurrentPosition, data);

            this.CurrentPosition += data.Length;
        }
    }

    Span<T> PrepareSegment(in DividedSegment seg)
    {
        checked
        {
            long segIndex = seg.AbsolutePosition / this.Options.SegmentSize;
            if (this.Segments.TryGetValue(segIndex, out MemoryBuffer<T>? segment))
            {
                if (segment.Length < (seg.InSegmentOffset + seg.Size))
                    segment.SetLength((int)(seg.InSegmentOffset + seg.Size));
            }
            else
            {
                segment = new MemoryBuffer<T>((int)(seg.InSegmentOffset + seg.Size));
                this.Segments.Add(segIndex, segment);
            }
            return segment.Span.Slice((int)seg.InSegmentOffset, (int)seg.Size);
        }
    }

    public void WriteZeroRandom(long position, long length)
    {
        checked
        {
            if (position < 0) throw new ArgumentOutOfRangeException("position < 0");
            if (length < 0) throw new ArgumentOutOfRangeException("length");
            if (length == 0) return;

            long start = position;

            DividedSegmentList segList = new DividedSegmentList(start, length, this.Options.SegmentSize);
            foreach (DividedSegment seg in segList.SegmentList)
            {
                var destSpan = PrepareSegment(in seg);

                Debug.Assert(destSpan.Length >= 1);

                destSpan.Fill(default!);
            }

            this.Length = Math.Max(this.Length, start + length);
        }
    }

    public void WriteZero(long length)
    {
        checked
        {
            if (length < 0) throw new ArgumentOutOfRangeException("length");
            if (length == 0) return;

            WriteZeroRandom(this.CurrentPosition, length);

            this.CurrentPosition += length;
        }
    }

    public T[] ToArray()
    {
        T[] tmp = new T[this.Length];

        this.ReadRandom(0, tmp);

        return tmp;
    }

    public IReadOnlyList<SparseChunk<T>> ReadRandomFast(long start, long size, out long readSize, bool allowPartial = false)
    {
        checked
        {
            if (start < 0) throw new ArgumentOutOfRangeException("start");
            if (size < 0) throw new ArgumentOutOfRangeException("size");
            if (start > this.Length) throw new ArgumentException("start > length");

            long end = start + size;

            if (end > this.Length)
            {
                if (allowPartial == false)
                    throw new ArgumentException("start + size > length");
                size = this.Length - start;
                end = this.Length;
            }

            List<SparseChunk<T>> ret = new List<SparseChunk<T>>();

            DividedSegmentList segList = new DividedSegmentList(start, size, this.Options.SegmentSize);
            foreach (DividedSegment seg in segList.SegmentList)
            {
                long segIndex = seg.AbsolutePosition / this.Options.SegmentSize;
                if (this.Segments.TryGetValue(segIndex, out MemoryBuffer<T>? segment))
                {
                    if (seg.InSegmentOffset >= segment.Length)
                    {
                        ret.Add(new SparseChunk<T>(seg.RelativePosition, (int)seg.Size));
                    }
                    else
                    {
                        if (segment.Length >= (seg.InSegmentOffset + seg.Size))
                        {
                            ret.Add(new SparseChunk<T>(seg.RelativePosition, segment.Memory.Slice((int)seg.InSegmentOffset, (int)seg.Size)));
                        }
                        else
                        {
                            ret.Add(new SparseChunk<T>(seg.RelativePosition, segment.Memory.Slice((int)seg.InSegmentOffset, (int)(segment.Length - seg.InSegmentOffset))));
                            ret.Add(new SparseChunk<T>(seg.RelativePosition, (int)(seg.Size - (segment.Length - seg.InSegmentOffset))));
                        }
                    }
                }
                else
                {
                    ret.Add(new SparseChunk<T>(seg.RelativePosition, (int)seg.Size));
                }
            }

            Debug.Assert(ret.Select(x => (long)x.Size).Sum() == size);

            readSize = size;

            return ret;
        }
    }

    public IReadOnlyList<SparseChunk<T>> ReadFast(long size, out long readSize, bool allowPartial = false)
    {
        long sizeToRead = size;
        if (checked(CurrentPosition + size) > Length)
        {
            if (allowPartial == false) throw new ArgumentOutOfRangeException("(CurrentPosition + size) > Size");
            sizeToRead = Length - CurrentPosition;
        }

        var ret = this.ReadRandomFast(CurrentPosition, size, out readSize, allowPartial);

        Debug.Assert(readSize == sizeToRead);

        CurrentPosition += sizeToRead;

        return ret;
    }

    public IReadOnlyList<SparseChunk<T>> PeekFast(long size, out long readSize, bool allowPartial = false)
    {
        long sizeToRead = size;
        if (checked(CurrentPosition + size) > Length)
        {
            if (allowPartial == false) throw new ArgumentOutOfRangeException("(CurrentPosition + size) > Size");
            sizeToRead = Length - CurrentPosition;
        }

        var ret = this.ReadRandomFast(CurrentPosition, size, out readSize, allowPartial);

        Debug.Assert(readSize == sizeToRead);

        return ret;
    }

    public int Read(Span<T> dest, bool allowPartial = false)
    {
        checked
        {
            long readSize2 = Util.CopySparseChunkListToSpan(ReadFast(dest.Length, out long readSize1, allowPartial), dest);
            Debug.Assert(readSize1 == readSize2);
            return (int)readSize1;
        }
    }

    public int Peek(Span<T> dest, bool allowPartial = false)
    {
        checked
        {
            long readSize2 = Util.CopySparseChunkListToSpan(PeekFast(dest.Length, out long readSize1, allowPartial), dest);
            Debug.Assert(readSize1 == readSize2);
            return (int)readSize1;
        }
    }

    public ReadOnlyMemory<T> ReadAsMemory(long size, bool allowPartial = false)
    {
        checked
        {
            long remainSize = this.Length - this.CurrentPosition;
            if (allowPartial == false && remainSize < size) throw new ArgumentOutOfRangeException("(CurrentPosition + size) > Size");
            int sizeToRead = (int)Math.Min(remainSize, size);

            var chunkList = ReadFast(sizeToRead, out long readSize1, allowPartial);
            Debug.Assert(readSize1 == sizeToRead);

            if (chunkList.Count == 1)
            {
                // No need to copy
                return chunkList[0].GetMemoryOrGenerateSparse();
            }

            Memory<T> ret = new T[sizeToRead];
            long retSize = Util.CopySparseChunkListToSpan(chunkList, ret.Span);
            Debug.Assert(retSize == sizeToRead);
            return ret;
        }
    }
    public ReadOnlySpan<T> Read(int size, bool allowPartial = false)
        => ReadAsMemory(size, allowPartial).Span;

    public ReadOnlyMemory<T> PeekAsMemory(long size, bool allowPartial = false)
    {
        checked
        {
            long remainSize = this.Length - this.CurrentPosition;
            if (allowPartial == false && remainSize < size) throw new ArgumentOutOfRangeException("(CurrentPosition + size) > Size");
            int sizeToRead = (int)Math.Min(remainSize, size);

            var chunkList = PeekFast(sizeToRead, out long readSize1, allowPartial);
            Debug.Assert(readSize1 == sizeToRead);

            if (chunkList.Count == 1)
            {
                // No need to copy
                return chunkList[0].GetMemoryOrGenerateSparse();
            }

            Memory<T> ret = new T[sizeToRead];
            long retSize = Util.CopySparseChunkListToSpan(chunkList, ret.Span);
            Debug.Assert(retSize == sizeToRead);
            return ret;
        }
    }
    public ReadOnlySpan<T> Peek(int size, bool allowPartial = false)
        => PeekAsMemory(size, allowPartial).Span;

    public ReadOnlyMemory<T> ReadAsMemory(int size, bool allowPartial = false)
    {
        checked
        {
            long remainSize = this.Length - this.CurrentPosition;
            if (allowPartial == false && remainSize < size) throw new ArgumentOutOfRangeException("(CurrentPosition + size) > Size");
            int sizeToRead = (int)Math.Min(remainSize, size);

            var chunkList = ReadFast(sizeToRead, out long readSize1, allowPartial);
            Debug.Assert(readSize1 == sizeToRead);

            if (chunkList.Count == 1)
            {
                // No need to copy
                return chunkList[0].GetMemoryOrGenerateSparse();
            }

            Memory<T> ret = new T[sizeToRead];
            long retSize = Util.CopySparseChunkListToSpan(chunkList, ret.Span);
            Debug.Assert(retSize == sizeToRead);
            return ret;
        }
    }

    public ReadOnlyMemory<T> PeekAsMemory(int size, bool allowPartial = false)
    {
        checked
        {
            long remainSize = this.Length - this.CurrentPosition;
            if (allowPartial == false && remainSize < size) throw new ArgumentOutOfRangeException("(CurrentPosition + size) > Size");
            int sizeToRead = (int)Math.Min(remainSize, size);

            var chunkList = PeekFast(sizeToRead, out long readSize1, allowPartial);
            Debug.Assert(readSize1 == sizeToRead);

            if (chunkList.Count == 1)
            {
                // No need to copy
                return chunkList[0].GetMemoryOrGenerateSparse();
            }

            Memory<T> ret = new T[sizeToRead];
            long retSize = Util.CopySparseChunkListToSpan(chunkList, ret.Span);
            Debug.Assert(retSize == sizeToRead);
            return ret;
        }
    }

    public void Seek(long offset, SeekOrigin mode, bool allocate = false)
    {
        checked
        {
            long newPosition = 0;
            if (mode == SeekOrigin.Current)
                newPosition = checked(CurrentPosition + offset);
            else if (mode == SeekOrigin.End)
                newPosition = checked(Length + offset);
            else
                newPosition = offset;

            if (newPosition < 0) throw new ArgumentOutOfRangeException("newPosition < 0");

            if (allocate == false)
            {
                if (newPosition > Length) throw new ArgumentOutOfRangeException("newPosition > Size");
            }
            else
            {
                Length = Math.Max(newPosition, Length);
            }

            CurrentPosition = newPosition;
        }
    }
    public HugeMemoryBuffer<T> SeekToBegin()
    {
        Seek(0, SeekOrigin.Begin);
        return this;
    }
    public HugeMemoryBuffer<T> SeekToEnd()
    {
        Seek(0, SeekOrigin.End);
        return this;
    }

    public void Clear() => SetLength(0);

    public void SetLength(long size)
    {
        checked
        {
            if (size < 0) throw new ArgumentOutOfRangeException("size");

            if (this.Length > size)
            {
                // Shrinking
                if (size == 0)
                {
                    this.Segments.Clear();
                }
                else
                {
                    // Get the last segment information
                    DividedSegmentList segList = new DividedSegmentList(size - 1, 1, this.Options.SegmentSize);
                    Debug.Assert(segList.SegmentList.Length == 1);
                    DividedSegment lastSegInfo = segList.SegmentList[0];

                    // delete all unnecessary segments following the last segment
                    var deleteList = this.Segments.Keys.Where(x => x > lastSegInfo.SegmentIndex).ToArray();
                    foreach (var index in deleteList)
                        this.Segments.Remove(index);

                    // shrink the last segment if necessary
                    if (this.Segments.TryGetValue(lastSegInfo.SegmentIndex, out MemoryBuffer<T>? lastSegment))
                    {
                        if (lastSegment.Length > lastSegInfo.Size)
                        {
                            lastSegment.SetLength((int)lastSegInfo.Size);
                            lastSegment.OptimizeInternalBufferSize();
                        }
                    }
                }
            }

            this.Length = size;
            if (this.CurrentPosition > this.Length)
                this.CurrentPosition = this.Length;
        }
    }

    public ReadOnlySpan<T> Read(long size, bool allowPartial = false)
        => Read(checked((int)size), allowPartial);

    public ReadOnlySpan<T> Peek(long size, bool allowPartial = false)
        => Peek(checked((int)size), allowPartial);

    public Task<int> ReadRandomAsync(long position, Memory<T> data, CancellationToken cancel = default)
        => Task.FromResult(ReadRandom(position, data, cancel));

    public int ReadRandom(long position, Memory<T> data, CancellationToken cancel = default)
    {
        cancel.ThrowIfCancellationRequested();
        long readSize2 = Util.CopySparseChunkListToSpan(ReadRandomFast(position, data.Length, out long readSize1, true), data.Span);
        Debug.Assert(readSize1 == readSize2);
        return (int)readSize1;
    }

    public Task WriteRandomAsync(long position, ReadOnlyMemory<T> data, CancellationToken cancel = default)
    {
        WriteRandom(position, data, cancel);
        return Task.CompletedTask;
    }

    public void WriteRandom(long position, ReadOnlyMemory<T> data, CancellationToken cancel = default)
    {
        cancel.ThrowIfCancellationRequested();
        WriteRandom(position, data.Span);
    }

    public Task AppendAsync(ReadOnlyMemory<T> data, CancellationToken cancel = default)
    {
        Append(data, cancel);
        return Task.CompletedTask;
    }

    public void Append(ReadOnlyMemory<T> data, CancellationToken cancel = default)
    {
        cancel.ThrowIfCancellationRequested();
        WriteRandom(this.CurrentPosition, data.Span);
    }

    public Task<long> GetFileSizeAsync(bool refresh = false, CancellationToken cancel = default)
        => Task.FromResult(GetFileSize(refresh, cancel));

    public long GetFileSize(bool refresh = false, CancellationToken cancel = default)
    {
        cancel.ThrowIfCancellationRequested();
        return this.Length;
    }

    public Task SetFileSizeAsync(long size, CancellationToken cancel = default)
    {
        SetFileSize(size, cancel);
        return Task.CompletedTask;
    }

    public void SetFileSize(long size, CancellationToken cancel = default)
    {
        cancel.ThrowIfCancellationRequested();
        this.SetLength(size);
    }

    public Task FlushAsync(CancellationToken cancel = default)
    {
        Flush(cancel);
        return Task.CompletedTask;
    }

    public void Flush(CancellationToken cancel = default)
    {
        cancel.ThrowIfCancellationRequested();
    }

    public void Dispose() { this.Dispose(true); GC.SuppressFinalize(this); }
    Once DisposeFlag;
    public async ValueTask DisposeAsync()
    {
        if (DisposeFlag.IsFirstCall() == false) return;
        await DisposeInternalAsync();
    }
    void Dispose(bool disposing)
    {
        if (!disposing || DisposeFlag.IsFirstCall() == false) return;
        DisposeInternalAsync()._GetResult();
    }
    Task DisposeInternalAsync() => TaskCompleted;

    public Task<long> GetPhysicalSizeAsync(CancellationToken cancel = default)
    {
        return Task.FromResult(GetPhysicalSize(cancel));
    }

    public long GetPhysicalSize(CancellationToken cancel = default)
    {
        cancel.ThrowIfCancellationRequested();
        return this.PhysicalSize;
    }

    Memory<T> tmpArray = new T[1];

    public void WriteOne(T data)
    {
        tmpArray.Span[0] = data;

        Write(tmpArray);
    }

    public T ReadOne()
    {
        var span = Read(1, false);
        return span[0];
    }

    public T PeekOne()
    {
        var span = Peek(1, false);
        return span[0];
    }

    public void Flush() { }
}

public static class SpanMemoryBufferHelper
{
    public static unsafe void WriteAny<T>(this ref SpanBuffer<byte> buf, in T value) where T : unmanaged
        => buf.Write(Unsafe.AsPointer(ref Unsafe.AsRef(in value)), sizeof(T));
    public static unsafe void WriteAny<T>(this MemoryBuffer<byte> buf, in T value) where T : unmanaged
        => buf.Write(Unsafe.AsPointer(ref Unsafe.AsRef(in value)), sizeof(T));

    public static void WriteBool8(this ref SpanBuffer<byte> buf, bool value) => value._SetBool8(buf.Walk(1, false));
    public static void WriteUInt8(this ref SpanBuffer<byte> buf, byte value) => value._SetUInt8(buf.Walk(1, false));
    public static void WriteByte(this ref SpanBuffer<byte> buf, byte value) => value._SetUInt8(buf.Walk(1, false));
    public static void WriteUInt16(this ref SpanBuffer<byte> buf, ushort value, bool littleEndian = false) => value._SetUInt16(buf.Walk(2, false), littleEndian);
    public static void WriteUInt32(this ref SpanBuffer<byte> buf, uint value, bool littleEndian = false) => value._SetUInt32(buf.Walk(4, false), littleEndian);
    public static void WriteUInt64(this ref SpanBuffer<byte> buf, ulong value, bool littleEndian = false) => value._SetUInt64(buf.Walk(8, false), littleEndian);
    public static void WriteSInt8(this ref SpanBuffer<byte> buf, sbyte value) => value._SetSInt8(buf.Walk(1, false));
    public static void WriteSInt16(this ref SpanBuffer<byte> buf, short value, bool littleEndian = false) => value._SetSInt16(buf.Walk(2, false), littleEndian);
    public static void WriteSInt32(this ref SpanBuffer<byte> buf, int value, bool littleEndian = false) => value._SetSInt32(buf.Walk(4, false), littleEndian);
    public static void WriteSInt64(this ref SpanBuffer<byte> buf, long value, bool littleEndian = false) => value._SetSInt64(buf.Walk(8, false), littleEndian);
    public static void WriteStr(this ref SpanBuffer<byte> buf, string str, Encoding? encoding = null) => buf.Write(str._GetBytes(encoding));

    public static void SetBool8(this ref SpanBuffer<byte> buf, bool value) => value._SetBool8(buf.Walk(1, true));
    public static void SetUInt8(this ref SpanBuffer<byte> buf, byte value) => value._SetUInt8(buf.Walk(1, true));
    public static void SetUInt16(this ref SpanBuffer<byte> buf, ushort value, bool littleEndian = false) => value._SetUInt16(buf.Walk(2, true), littleEndian);
    public static void SetUInt32(this ref SpanBuffer<byte> buf, uint value, bool littleEndian = false) => value._SetUInt32(buf.Walk(4, true), littleEndian);
    public static void SetUInt64(this ref SpanBuffer<byte> buf, ulong value, bool littleEndian = false) => value._SetUInt64(buf.Walk(8, true), littleEndian);
    public static void SetSInt8(this ref SpanBuffer<byte> buf, sbyte value) => value._SetSInt8(buf.Walk(1, true));
    public static void SetSInt16(this ref SpanBuffer<byte> buf, short value, bool littleEndian = false) => value._SetSInt16(buf.Walk(2, true), littleEndian);
    public static void SetSInt32(this ref SpanBuffer<byte> buf, int value, bool littleEndian = false) => value._SetSInt32(buf.Walk(4, true), littleEndian);
    public static void SetSInt64(this ref SpanBuffer<byte> buf, long value, bool littleEndian = false) => value._SetSInt64(buf.Walk(8, true), littleEndian);

    public static bool ReadBool8(ref this SpanBuffer<byte> buf) => buf.Read(1)._GetBool8();
    public static byte ReadUInt8(ref this SpanBuffer<byte> buf) => buf.Read(1)._GetUInt8();
    public static byte ReadByte(ref this SpanBuffer<byte> buf) => buf.Read(1)._GetUInt8();
    public static ushort ReadUInt16(ref this SpanBuffer<byte> buf, bool littleEndian = false) => buf.Read(2)._GetUInt16(littleEndian);
    public static uint ReadUInt32(ref this SpanBuffer<byte> buf, bool littleEndian = false) => buf.Read(4)._GetUInt32(littleEndian);
    public static ulong ReadUInt64(ref this SpanBuffer<byte> buf, bool littleEndian = false) => buf.Read(8)._GetUInt64(littleEndian);
    public static sbyte ReadSInt8(ref this SpanBuffer<byte> buf) => buf.Read(1)._GetSInt8();
    public static short ReadSInt16(ref this SpanBuffer<byte> buf, bool littleEndian = false) => buf.Read(2)._GetSInt16(littleEndian);
    public static int ReadSInt32(ref this SpanBuffer<byte> buf, bool littleEndian = false) => buf.Read(4)._GetSInt32(littleEndian);
    public static long ReadSInt64(ref this SpanBuffer<byte> buf, bool littleEndian = false) => buf.Read(8)._GetSInt64(littleEndian);

    public static bool PeekBool8(ref this SpanBuffer<byte> buf) => buf.Peek(1)._GetBool8();
    public static byte PeekUInt8(ref this SpanBuffer<byte> buf) => buf.Peek(1)._GetUInt8();
    public static ushort PeekUInt16(ref this SpanBuffer<byte> buf, bool littleEndian = false) => buf.Peek(2)._GetUInt16(littleEndian);
    public static uint PeekUInt32(ref this SpanBuffer<byte> buf, bool littleEndian = false) => buf.Peek(4)._GetUInt32(littleEndian);
    public static ulong PeekUInt64(ref this SpanBuffer<byte> buf, bool littleEndian = false) => buf.Peek(8)._GetUInt64(littleEndian);
    public static sbyte PeekSInt8(ref this SpanBuffer<byte> buf) => buf.Peek(1)._GetSInt8();
    public static short PeekSInt16(ref this SpanBuffer<byte> buf, bool littleEndian = false) => buf.Peek(2)._GetSInt16(littleEndian);
    public static int PeekSInt32(ref this SpanBuffer<byte> buf, bool littleEndian = false) => buf.Peek(4)._GetSInt32(littleEndian);
    public static long PeekSInt64(ref this SpanBuffer<byte> buf, bool littleEndian = false) => buf.Peek(8)._GetSInt64(littleEndian);


    public static bool ReadBool8(ref this ReadOnlySpanBuffer<byte> buf) => buf.Read(1)._GetBool8();
    public static byte ReadUInt8(ref this ReadOnlySpanBuffer<byte> buf) => buf.Read(1)._GetUInt8();
    public static byte ReadByte(ref this ReadOnlySpanBuffer<byte> buf) => buf.Read(1)._GetUInt8();
    public static ushort ReadUInt16(ref this ReadOnlySpanBuffer<byte> buf, bool littleEndian = false) => buf.Read(2)._GetUInt16(littleEndian);
    public static uint ReadUInt32(ref this ReadOnlySpanBuffer<byte> buf, bool littleEndian = false) => buf.Read(4)._GetUInt32(littleEndian);
    public static ulong ReadUInt64(ref this ReadOnlySpanBuffer<byte> buf, bool littleEndian = false) => buf.Read(8)._GetUInt64(littleEndian);
    public static sbyte ReadSInt8(ref this ReadOnlySpanBuffer<byte> buf) => buf.Read(1)._GetSInt8();
    public static short ReadSInt16(ref this ReadOnlySpanBuffer<byte> buf, bool littleEndian = false) => buf.Read(2)._GetSInt16(littleEndian);
    public static int ReadSInt32(ref this ReadOnlySpanBuffer<byte> buf, bool littleEndian = false) => buf.Read(4)._GetSInt32(littleEndian);
    public static long ReadSInt64(ref this ReadOnlySpanBuffer<byte> buf, bool littleEndian = false) => buf.Read(8)._GetSInt64(littleEndian);

    public static bool PeekBool8(ref this ReadOnlySpanBuffer<byte> buf) => buf.Peek(1)._GetBool8();
    public static byte PeekUInt8(ref this ReadOnlySpanBuffer<byte> buf) => buf.Peek(1)._GetUInt8();
    public static ushort PeekUInt16(ref this ReadOnlySpanBuffer<byte> buf, bool littleEndian = false) => buf.Peek(2)._GetUInt16(littleEndian);
    public static uint PeekUInt32(ref this ReadOnlySpanBuffer<byte> buf, bool littleEndian = false) => buf.Peek(4)._GetUInt32(littleEndian);
    public static ulong PeekUInt64(ref this ReadOnlySpanBuffer<byte> buf, bool littleEndian = false) => buf.Peek(8)._GetUInt64(littleEndian);
    public static sbyte PeekSInt8(ref this ReadOnlySpanBuffer<byte> buf) => buf.Peek(1)._GetSInt8();
    public static short PeekSInt16(ref this ReadOnlySpanBuffer<byte> buf, bool littleEndian = false) => buf.Peek(2)._GetSInt16(littleEndian);
    public static int PeekSInt32(ref this ReadOnlySpanBuffer<byte> buf, bool littleEndian = false) => buf.Peek(4)._GetSInt32(littleEndian);
    public static long PeekSInt64(ref this ReadOnlySpanBuffer<byte> buf, bool littleEndian = false) => buf.Peek(8)._GetSInt64(littleEndian);


    public static void WriteBool8(this ref FastMemoryBuffer<byte> buf, bool value) => value._SetBool8(buf.Walk(1, false));
    public static void WriteUInt8(this ref FastMemoryBuffer<byte> buf, byte value) => value._SetUInt8(buf.Walk(1, false));
    public static void WriteByte(this ref FastMemoryBuffer<byte> buf, byte value) => value._SetUInt8(buf.Walk(1, false));
    public static void WriteUInt16(this ref FastMemoryBuffer<byte> buf, ushort value, bool littleEndian = false) => value._SetUInt16(buf.Walk(2, false), littleEndian);
    public static void WriteUInt32(this ref FastMemoryBuffer<byte> buf, uint value, bool littleEndian = false) => value._SetUInt32(buf.Walk(4, false), littleEndian);
    public static void WriteUInt64(this ref FastMemoryBuffer<byte> buf, ulong value, bool littleEndian = false) => value._SetUInt64(buf.Walk(8, false), littleEndian);
    public static void WriteSInt8(this ref FastMemoryBuffer<byte> buf, sbyte value) => value._SetSInt8(buf.Walk(1, false));
    public static void WriteSInt16(this ref FastMemoryBuffer<byte> buf, short value, bool littleEndian = false) => value._SetSInt16(buf.Walk(2, false), littleEndian);
    public static void WriteSInt32(this ref FastMemoryBuffer<byte> buf, int value, bool littleEndian = false) => value._SetSInt32(buf.Walk(4, false), littleEndian);
    public static void WriteSInt64(this ref FastMemoryBuffer<byte> buf, long value, bool littleEndian = false) => value._SetSInt64(buf.Walk(8, false), littleEndian);
    public static void WriteStr(this ref FastMemoryBuffer<byte> buf, string str, Encoding? encoding = null) => buf.Write(str._GetBytes(encoding));

    public static void SetBool8(this ref FastMemoryBuffer<byte> buf, bool value) => value._SetBool8(buf.Walk(1, true));
    public static void SetUInt8(this ref FastMemoryBuffer<byte> buf, byte value) => value._SetUInt8(buf.Walk(1, true));
    public static void SetUInt16(this ref FastMemoryBuffer<byte> buf, ushort value, bool littleEndian = false) => value._SetUInt16(buf.Walk(2, true), littleEndian);
    public static void SetUInt32(this ref FastMemoryBuffer<byte> buf, uint value, bool littleEndian = false) => value._SetUInt32(buf.Walk(4, true), littleEndian);
    public static void SetUInt64(this ref FastMemoryBuffer<byte> buf, ulong value, bool littleEndian = false) => value._SetUInt64(buf.Walk(8, true), littleEndian);
    public static void SetSInt8(this ref FastMemoryBuffer<byte> buf, sbyte value) => value._SetSInt8(buf.Walk(1, true));
    public static void SetSInt16(this ref FastMemoryBuffer<byte> buf, short value, bool littleEndian = false) => value._SetSInt16(buf.Walk(2, true), littleEndian);
    public static void SetSInt32(this ref FastMemoryBuffer<byte> buf, int value, bool littleEndian = false) => value._SetSInt32(buf.Walk(4, true), littleEndian);
    public static void SetSInt64(this ref FastMemoryBuffer<byte> buf, long value, bool littleEndian = false) => value._SetSInt64(buf.Walk(8, true), littleEndian);

    public static bool ReadBool8(ref this FastMemoryBuffer<byte> buf) => buf.Read(1)._GetBool8();
    public static byte ReadUInt8(ref this FastMemoryBuffer<byte> buf) => buf.Read(1)._GetUInt8();
    public static byte ReadByte(ref this FastMemoryBuffer<byte> buf) => buf.Read(1)._GetUInt8();
    public static ushort ReadUInt16(ref this FastMemoryBuffer<byte> buf, bool littleEndian = false) => buf.Read(2)._GetUInt16(littleEndian);
    public static uint ReadUInt32(ref this FastMemoryBuffer<byte> buf, bool littleEndian = false) => buf.Read(4)._GetUInt32(littleEndian);
    public static ulong ReadUInt64(ref this FastMemoryBuffer<byte> buf, bool littleEndian = false) => buf.Read(8)._GetUInt64(littleEndian);
    public static sbyte ReadSInt8(ref this FastMemoryBuffer<byte> buf) => buf.Read(1)._GetSInt8();
    public static short ReadSInt16(ref this FastMemoryBuffer<byte> buf, bool littleEndian = false) => buf.Read(2)._GetSInt16(littleEndian);
    public static int ReadSInt32(ref this FastMemoryBuffer<byte> buf, bool littleEndian = false) => buf.Read(4)._GetSInt32(littleEndian);
    public static long ReadSInt64(ref this FastMemoryBuffer<byte> buf, bool littleEndian = false) => buf.Read(8)._GetSInt64(littleEndian);

    public static bool PeekBool8(ref this FastMemoryBuffer<byte> buf) => buf.Peek(1)._GetBool8();
    public static byte PeekUInt8(ref this FastMemoryBuffer<byte> buf) => buf.Peek(1)._GetUInt8();
    public static ushort PeekUInt16(ref this FastMemoryBuffer<byte> buf, bool littleEndian = false) => buf.Peek(2)._GetUInt16(littleEndian);
    public static uint PeekUInt32(ref this FastMemoryBuffer<byte> buf, bool littleEndian = false) => buf.Peek(4)._GetUInt32(littleEndian);
    public static ulong PeekUInt64(ref this FastMemoryBuffer<byte> buf, bool littleEndian = false) => buf.Peek(8)._GetUInt64(littleEndian);
    public static sbyte PeekSInt8(ref this FastMemoryBuffer<byte> buf) => buf.Peek(1)._GetSInt8();
    public static short PeekSInt16(ref this FastMemoryBuffer<byte> buf, bool littleEndian = false) => buf.Peek(2)._GetSInt16(littleEndian);
    public static int PeekSInt32(ref this FastMemoryBuffer<byte> buf, bool littleEndian = false) => buf.Peek(4)._GetSInt32(littleEndian);
    public static long PeekSInt64(ref this FastMemoryBuffer<byte> buf, bool littleEndian = false) => buf.Peek(8)._GetSInt64(littleEndian);


    public static bool ReadBool8(ref this FastReadOnlyMemoryBuffer<byte> buf) => buf.Read(1)._GetBool8();
    public static byte ReadUInt8(ref this FastReadOnlyMemoryBuffer<byte> buf) => buf.Read(1)._GetUInt8();
    public static byte ReadByte(ref this FastReadOnlyMemoryBuffer<byte> buf) => buf.Read(1)._GetUInt8();
    public static ushort ReadUInt16(ref this FastReadOnlyMemoryBuffer<byte> buf, bool littleEndian = false) => buf.Read(2)._GetUInt16(littleEndian);
    public static uint ReadUInt32(ref this FastReadOnlyMemoryBuffer<byte> buf, bool littleEndian = false) => buf.Read(4)._GetUInt32(littleEndian);
    public static ulong ReadUInt64(ref this FastReadOnlyMemoryBuffer<byte> buf, bool littleEndian = false) => buf.Read(8)._GetUInt64(littleEndian);
    public static sbyte ReadSInt8(ref this FastReadOnlyMemoryBuffer<byte> buf) => buf.Read(1)._GetSInt8();
    public static short ReadSInt16(ref this FastReadOnlyMemoryBuffer<byte> buf, bool littleEndian = false) => buf.Read(2)._GetSInt16(littleEndian);
    public static int ReadSInt32(ref this FastReadOnlyMemoryBuffer<byte> buf, bool littleEndian = false) => buf.Read(4)._GetSInt32(littleEndian);
    public static long ReadSInt64(ref this FastReadOnlyMemoryBuffer<byte> buf, bool littleEndian = false) => buf.Read(8)._GetSInt64(littleEndian);

    public static bool PeekBool8(ref this FastReadOnlyMemoryBuffer<byte> buf) => buf.Peek(1)._GetBool8();
    public static byte PeekUInt8(ref this FastReadOnlyMemoryBuffer<byte> buf) => buf.Peek(1)._GetUInt8();
    public static ushort PeekUInt16(ref this FastReadOnlyMemoryBuffer<byte> buf, bool littleEndian = false) => buf.Peek(2)._GetUInt16(littleEndian);
    public static uint PeekUInt32(ref this FastReadOnlyMemoryBuffer<byte> buf, bool littleEndian = false) => buf.Peek(4)._GetUInt32(littleEndian);
    public static ulong PeekUInt64(ref this FastReadOnlyMemoryBuffer<byte> buf, bool littleEndian = false) => buf.Peek(8)._GetUInt64(littleEndian);
    public static sbyte PeekSInt8(ref this FastReadOnlyMemoryBuffer<byte> buf) => buf.Peek(1)._GetSInt8();
    public static short PeekSInt16(ref this FastReadOnlyMemoryBuffer<byte> buf, bool littleEndian = false) => buf.Peek(2)._GetSInt16(littleEndian);
    public static int PeekSInt32(ref this FastReadOnlyMemoryBuffer<byte> buf, bool littleEndian = false) => buf.Peek(4)._GetSInt32(littleEndian);
    public static long PeekSInt64(ref this FastReadOnlyMemoryBuffer<byte> buf, bool littleEndian = false) => buf.Peek(8)._GetSInt64(littleEndian);


    public static void WriteBool8(this MemoryBuffer<byte> buf, bool value) => value._SetBool8(buf.Walk(1, false));
    public static void WriteUInt8(this MemoryBuffer<byte> buf, byte value) => value._SetUInt8(buf.Walk(1, false));
    public static void WriteByte(this MemoryBuffer<byte> buf, byte value) => value._SetUInt8(buf.Walk(1, false));
    public static void WriteUInt16(this MemoryBuffer<byte> buf, ushort value, bool littleEndian = false) => value._SetUInt16(buf.Walk(2, false), littleEndian);
    public static void WriteUInt32(this MemoryBuffer<byte> buf, uint value, bool littleEndian = false) => value._SetUInt32(buf.Walk(4, false), littleEndian);
    public static void WriteUInt64(this MemoryBuffer<byte> buf, ulong value, bool littleEndian = false) => value._SetUInt64(buf.Walk(8, false), littleEndian);
    public static void WriteSInt8(this MemoryBuffer<byte> buf, sbyte value) => value._SetSInt8(buf.Walk(1, false));
    public static void WriteSInt16(this MemoryBuffer<byte> buf, short value, bool littleEndian = false) => value._SetSInt16(buf.Walk(2, false), littleEndian);
    public static void WriteSInt32(this MemoryBuffer<byte> buf, int value, bool littleEndian = false) => value._SetSInt32(buf.Walk(4, false), littleEndian);
    public static void WriteSInt64(this MemoryBuffer<byte> buf, long value, bool littleEndian = false) => value._SetSInt64(buf.Walk(8, false), littleEndian);
    public static void WriteStr(this MemoryBuffer<byte> buf, string str, Encoding? encoding = null) => buf.Write(str._GetBytes(encoding));

    public static void SetBool8(this MemoryBuffer<byte> buf, bool value) => value._SetBool8(buf.Walk(1, true));
    public static void SetUInt8(this MemoryBuffer<byte> buf, byte value) => value._SetUInt8(buf.Walk(1, true));
    public static void SetUInt16(this MemoryBuffer<byte> buf, ushort value, bool littleEndian = false) => value._SetUInt16(buf.Walk(2, true), littleEndian);
    public static void SetUInt32(this MemoryBuffer<byte> buf, uint value, bool littleEndian = false) => value._SetUInt32(buf.Walk(4, true), littleEndian);
    public static void SetUInt64(this MemoryBuffer<byte> buf, ulong value, bool littleEndian = false) => value._SetUInt64(buf.Walk(8, true), littleEndian);
    public static void SetSInt8(this MemoryBuffer<byte> buf, sbyte value) => value._SetSInt8(buf.Walk(1, true));
    public static void SetSInt16(this MemoryBuffer<byte> buf, short value, bool littleEndian = false) => value._SetSInt16(buf.Walk(2, true), littleEndian);
    public static void SetSInt32(this MemoryBuffer<byte> buf, int value, bool littleEndian = false) => value._SetSInt32(buf.Walk(4, true), littleEndian);
    public static void SetSInt64(this MemoryBuffer<byte> buf, long value, bool littleEndian = false) => value._SetSInt64(buf.Walk(8, true), littleEndian);

    public static bool ReadBool8(this MemoryBuffer<byte> buf) => buf.Read(1)._GetBool8();
    public static byte ReadUInt8(this MemoryBuffer<byte> buf) => buf.Read(1)._GetUInt8();
    public static byte ReadByte(this MemoryBuffer<byte> buf) => buf.Read(1)._GetUInt8();
    public static ushort ReadUInt16(this MemoryBuffer<byte> buf, bool littleEndian = false) => buf.Read(2)._GetUInt16(littleEndian);
    public static uint ReadUInt32(this MemoryBuffer<byte> buf, bool littleEndian = false) => buf.Read(4)._GetUInt32(littleEndian);
    public static ulong ReadUInt64(this MemoryBuffer<byte> buf, bool littleEndian = false) => buf.Read(8)._GetUInt64(littleEndian);
    public static sbyte ReadSInt8(this MemoryBuffer<byte> buf) => buf.Read(1)._GetSInt8();
    public static short ReadSInt16(this MemoryBuffer<byte> buf, bool littleEndian = false) => buf.Read(2)._GetSInt16(littleEndian);
    public static int ReadSInt32(this MemoryBuffer<byte> buf, bool littleEndian = false) => buf.Read(4)._GetSInt32(littleEndian);
    public static long ReadSInt64(this MemoryBuffer<byte> buf, bool littleEndian = false) => buf.Read(8)._GetSInt64(littleEndian);

    public static bool PeekBool8(this MemoryBuffer<byte> buf) => buf.Peek(1)._GetBool8();
    public static byte PeekUInt8(this MemoryBuffer<byte> buf) => buf.Peek(1)._GetUInt8();
    public static ushort PeekUInt16(this MemoryBuffer<byte> buf, bool littleEndian = false) => buf.Peek(2)._GetUInt16(littleEndian);
    public static uint PeekUInt32(this MemoryBuffer<byte> buf, bool littleEndian = false) => buf.Peek(4)._GetUInt32(littleEndian);
    public static ulong PeekUInt64(this MemoryBuffer<byte> buf, bool littleEndian = false) => buf.Peek(8)._GetUInt64(littleEndian);
    public static sbyte PeekSInt8(this MemoryBuffer<byte> buf) => buf.Peek(1)._GetSInt8();
    public static short PeekSInt16(this MemoryBuffer<byte> buf, bool littleEndian = false) => buf.Peek(2)._GetSInt16(littleEndian);
    public static int PeekSInt32(this MemoryBuffer<byte> buf, bool littleEndian = false) => buf.Peek(4)._GetSInt32(littleEndian);
    public static long PeekSInt64(this MemoryBuffer<byte> buf, bool littleEndian = false) => buf.Peek(8)._GetSInt64(littleEndian);

    public static bool ReadBool8(this ReadOnlyMemoryBuffer<byte> buf) => buf.Read(1)._GetBool8();
    public static byte ReadUInt8(this ReadOnlyMemoryBuffer<byte> buf) => buf.Read(1)._GetUInt8();
    public static byte ReadByte(this ReadOnlyMemoryBuffer<byte> buf) => buf.Read(1)._GetUInt8();
    public static ushort ReadUInt16(this ReadOnlyMemoryBuffer<byte> buf, bool littleEndian = false) => buf.Read(2)._GetUInt16(littleEndian);
    public static uint ReadUInt32(this ReadOnlyMemoryBuffer<byte> buf, bool littleEndian = false) => buf.Read(4)._GetUInt32(littleEndian);
    public static ulong ReadUInt64(this ReadOnlyMemoryBuffer<byte> buf, bool littleEndian = false) => buf.Read(8)._GetUInt64(littleEndian);
    public static sbyte ReadSInt8(this ReadOnlyMemoryBuffer<byte> buf) => buf.Read(1)._GetSInt8();
    public static short ReadSInt16(this ReadOnlyMemoryBuffer<byte> buf, bool littleEndian = false) => buf.Read(2)._GetSInt16(littleEndian);
    public static int ReadSInt32(this ReadOnlyMemoryBuffer<byte> buf, bool littleEndian = false) => buf.Read(4)._GetSInt32(littleEndian);
    public static long ReadSInt64(this ReadOnlyMemoryBuffer<byte> buf, bool littleEndian = false) => buf.Read(8)._GetSInt64(littleEndian);

    public static bool PeekBool8(this ReadOnlyMemoryBuffer<byte> buf) => buf.Peek(1)._GetBool8();
    public static byte PeekUInt8(this ReadOnlyMemoryBuffer<byte> buf) => buf.Peek(1)._GetUInt8();
    public static ushort PeekUInt16(this ReadOnlyMemoryBuffer<byte> buf, bool littleEndian = false) => buf.Peek(2)._GetUInt16(littleEndian);
    public static uint PeekUInt32(this ReadOnlyMemoryBuffer<byte> buf, bool littleEndian = false) => buf.Peek(4)._GetUInt32(littleEndian);
    public static ulong PeekUInt64(this ReadOnlyMemoryBuffer<byte> buf, bool littleEndian = false) => buf.Peek(8)._GetUInt64(littleEndian);
    public static sbyte PeekSInt8(this ReadOnlyMemoryBuffer<byte> buf) => buf.Peek(1)._GetSInt8();
    public static short PeekSInt16(this ReadOnlyMemoryBuffer<byte> buf, bool littleEndian = false) => buf.Peek(2)._GetSInt16(littleEndian);
    public static int PeekSInt32(this ReadOnlyMemoryBuffer<byte> buf, bool littleEndian = false) => buf.Peek(4)._GetSInt32(littleEndian);
    public static long PeekSInt64(this ReadOnlyMemoryBuffer<byte> buf, bool littleEndian = false) => buf.Peek(8)._GetSInt64(littleEndian);
}

public static class MemoryHelper
{
    public static readonly long _MemoryObjectOffset;
    public static readonly long _MemoryIndexOffset;
    public static readonly long _MemoryLengthOffset;
    public static readonly bool _UseFast = false;

    static unsafe MemoryHelper()
    {
        try
        {
            _MemoryObjectOffset = Marshal.OffsetOf<Memory<byte>>("_object").ToInt64();
            _MemoryIndexOffset = Marshal.OffsetOf<Memory<byte>>("_index").ToInt64();
            _MemoryLengthOffset = Marshal.OffsetOf<Memory<byte>>("_length").ToInt64();

            if (_MemoryObjectOffset != Marshal.OffsetOf<ReadOnlyMemory<DummyValueType>>("_object").ToInt64() ||
                _MemoryIndexOffset != Marshal.OffsetOf<ReadOnlyMemory<DummyValueType>>("_index").ToInt64() ||
                _MemoryLengthOffset != Marshal.OffsetOf<ReadOnlyMemory<DummyValueType>>("_length").ToInt64())
            {
                throw new Exception();
            }

            _UseFast = true;
        }
        catch
        {
            Random r = new Random();
            bool ok = true;

            for (int i = 0; i < 32; i++)
            {
                int a = r.Next(96) + 32;
                int b = r.Next(a / 2);
                int c = r.Next(a / 2);
                if (ValidateMemoryStructureLayoutForSecurityInternal(EnsureInternal.Yes, a, b, c) == false ||
                    ValidateReadOnlyMemoryStructureLayoutForSecurityInternal(EnsureInternal.Yes, a, b, c) == false)
                {
                    ok = false;
                    break;
                }
            }

            if (ok)
            {
                _MemoryObjectOffset = 0;
                _MemoryIndexOffset = sizeof(void*);
                _MemoryLengthOffset = _MemoryIndexOffset + sizeof(int);

                _UseFast = true;
            }
        }

        if (_UseFast == false)
        {
            Console.WriteLine("MemoryHelper Warning: Fail to set _UseFast to true. Performance impact.");
        }
    }

    public const int MemoryUsePoolThreshold = 1024;

    public static T[] FastAllocMoreThan<T>(int length)
    {
        if (length < MemoryUsePoolThreshold)
            return new T[length];
        else
            return ArrayPool<T>.Shared.Rent(length);
    }

    public static Memory<T> FastAllocMemory<T>(int length)
    {
        if (length < MemoryUsePoolThreshold)
            return new T[length];
        else
            return new Memory<T>(FastAllocMoreThan<T>(length)).Slice(0, length);
    }

    public static void FastFree<T>(T[] array)
    {
        if (array.Length >= MemoryUsePoolThreshold)
            ArrayPool<T>.Shared.Return(array);
    }

    public static void FastFree<T>(Memory<T> memory) => memory._GetInternalArray()._FastFree();

    public static ValueHolder FastAllocMemoryWithUsing<T>(int length, out Memory<T> memory)
    {
        T[] allocatedArray = FastAllocMoreThan<T>(length);

        if (allocatedArray.Length == length)
            memory = allocatedArray;
        else
            memory = allocatedArray.AsMemory(0, length);

        return new ValueHolder(() =>
        {
            FastFree(allocatedArray);
        },
        LeakCounterKind.FastAllocMemoryWithUsing);
    }

    public static ValueHolder FastAllocArrayMoreThanWithUsing<T>(int length, out T[] array)
    {
        T[] allocatedArray = FastAllocMoreThan<T>(length);

        array = allocatedArray;

        return new ValueHolder(() =>
        {
            FastFree(allocatedArray);
        },
        LeakCounterKind.FastAllocMemoryWithUsing);
    }

    public static unsafe ValueHolder AllocUnmanagedMemoryWithUsing(long byteLength, out IntPtr ptr)
    {
        IntPtr p;

        if (byteLength < 0) throw new ArgumentOutOfRangeException(nameof(byteLength));

        if (byteLength == 0) byteLength = 1;

        if (byteLength >= 1_000_000)
        {
            p = Marshal.AllocHGlobal((IntPtr)byteLength);
            ptr = p;

            return new ValueHolder(() =>
            {
                Marshal.FreeHGlobal(p);
            },
            LeakCounterKind.AllocUnmanagedMemory);
        }
        else
        {
            p = Marshal.AllocCoTaskMem((int)byteLength);
            ptr = p;

            return new ValueHolder(() =>
            {
                Marshal.FreeCoTaskMem(p);
            },
            LeakCounterKind.AllocUnmanagedMemory);
        }
    }

    public static unsafe void* AllocUnmanagedMemory(int byteLength)
    {
        if (byteLength < 0) throw new ArgumentOutOfRangeException(nameof(byteLength));

        if (byteLength == 0) byteLength = 1;

        IntPtr p;

        p = Marshal.AllocCoTaskMem((int)byteLength);

        return (void*)p;
    }

    public static unsafe void FreeUnmanagedMemory(void* p)
    {
        if (p == null) return;

        Marshal.FreeCoTaskMem((IntPtr)p);
    }

    unsafe struct DummyValueType
    {
        public fixed char fixedBuffer[96];
    }

    public static unsafe bool ValidateMemoryStructureLayoutForSecurityInternal(EnsureInternal yes, int a, int b, int c)
    {
        try
        {
            DummyValueType[] obj = new DummyValueType[a];
            Memory<DummyValueType> mem = new Memory<DummyValueType>(obj, b, c);

            void* memPtr = Unsafe.AsPointer(ref mem);

            byte* p = (byte*)memPtr;
            DummyValueType[] array = Unsafe.Read<DummyValueType[]>(p);
            if (array == obj)
            {
                p += sizeof(void*);
                if (Unsafe.Read<int>(p) == b)
                {
                    p += sizeof(int);
                    if (Unsafe.Read<int>(p) == c)
                    {
                        return true;
                    }
                }
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    public static unsafe bool ValidateReadOnlyMemoryStructureLayoutForSecurityInternal(EnsureInternal yes, int a, int b, int c)
    {
        try
        {
            DummyValueType[] obj = new DummyValueType[a];
            ReadOnlyMemory<DummyValueType> mem = new ReadOnlyMemory<DummyValueType>(obj, b, c);

            void* memPtr = Unsafe.AsPointer(ref mem);

            byte* p = (byte*)memPtr;
            DummyValueType[] array = Unsafe.Read<DummyValueType[]>(p);
            if (array == obj)
            {
                p += sizeof(void*);
                if (Unsafe.Read<int>(p) == b)
                {
                    p += sizeof(int);
                    if (Unsafe.Read<int>(p) == c)
                    {
                        return true;
                    }
                }
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    public static IReadOnlyList<Memory<T>> SplitMemory<T>(Memory<T> src, int elementMaxSize)
    {
        elementMaxSize = Math.Max(1, elementMaxSize);

        List<Memory<T>> ret = new List<Memory<T>>();

        int srcLen = src.Length;
        for (int i = 0; i < srcLen; i += elementMaxSize)
        {
            Memory<T> segment = src.Slice(i, Math.Min(elementMaxSize, srcLen - i));
            ret.Add(segment);
        }

        return ret;
    }

    public static IReadOnlyList<IReadOnlyList<Memory<T>>> SplitMemoryArray<T>(IEnumerable<Memory<T>> src, int elementMaxSize)
    {
        elementMaxSize = Math.Max(1, elementMaxSize);

        int currentSize = 0;
        List<List<Memory<T>>> ret = new List<List<Memory<T>>>();
        List<Memory<T>> currentList = new List<Memory<T>>();

        foreach (Memory<T> mSrc in src)
        {
            Memory<T> m = mSrc;

            LABEL_START:

            if (m.Length >= 1)
            {
                int overSize = (currentSize + m.Length) - elementMaxSize;
                if (overSize >= 0)
                {
                    Memory<T> mAdd = m.Slice(0, m.Length - overSize);

                    currentList.Add(mAdd);
                    ret.Add(currentList);
                    currentList = new List<Memory<T>>();
                    currentSize = 0;

                    m = m.Slice(mAdd.Length);

                    goto LABEL_START;
                }
                else
                {
                    currentList.Add(m);
                    currentSize += m.Length;
                }
            }
        }

        if (currentList.Count >= 1)
            ret.Add(currentList);

        return ret;
    }

    public static List<List<ArraySegment<T>>> SplitMemoryArrayToArraySegment<T>(IEnumerable<Memory<T>> src, int elementMaxSize)
    {
        elementMaxSize = Math.Max(1, elementMaxSize);

        int currentSize = 0;
        List<List<ArraySegment<T>>> ret = new List<List<ArraySegment<T>>>();
        List<ArraySegment<T>> currentList = new List<ArraySegment<T>>();

        foreach (Memory<T> mSrc in src)
        {
            Memory<T> m = mSrc;

            LABEL_START:

            if (m.Length >= 1)
            {
                int overSize = (currentSize + m.Length) - elementMaxSize;
                if (overSize >= 0)
                {
                    Memory<T> mAdd = m.Slice(0, m.Length - overSize);

                    currentList.Add(mAdd._AsSegment());
                    ret.Add(currentList);
                    currentList = new List<ArraySegment<T>>();
                    currentSize = 0;

                    m = m.Slice(mAdd.Length);

                    goto LABEL_START;
                }
                else
                {
                    currentList.Add(m._AsSegment());
                    currentSize += m.Length;
                }
            }
        }

        if (currentList.Count >= 1)
            ret.Add(currentList);

        return ret;
    }
}

public static partial class CoresConfig
{
    public static partial class FastMemoryPoolConfig
    {
        public static readonly Copenhagen<int> ExpandSizeMultipleBy = 5;

        // 重いサーバー (大量のインスタンスや大量のコンテナが稼働、または大量のコネクションを処理) における定数変更
        public static void ApplyHeavyLoadServerConfig()
        {
            //ExpandSizeMultipleBy.TrySet(1);
        }
    }
}

public class FastMemoryPool<T>
{
    Memory<T> Pool;
    int CurrentPos;
    int MinReserveSize;

    public FastMemoryPool(int initialSize = 0)
    {
        initialSize = Math.Max(initialSize, 1);
        Pool = new T[initialSize];
        MinReserveSize = initialSize;
    }

    [MethodImpl(Inline)]
    public Memory<T> Reserve(int maxSize)
    {
        checked
        {
            if (maxSize < 0) throw new ArgumentOutOfRangeException("size");
            if (maxSize == 0) return Memory<T>.Empty;

            Debug.Assert((Pool.Length - CurrentPos) >= 0);

            if ((Pool.Length - CurrentPos) < maxSize)
            {
                MinReserveSize = Math.Max(MinReserveSize, maxSize * Math.Max(CoresConfig.FastMemoryPoolConfig.ExpandSizeMultipleBy, 2));
                Pool = new T[MinReserveSize];
                CurrentPos = 0;
            }

            var ret = Pool.Slice(CurrentPos, maxSize);
            CurrentPos += maxSize;
            return ret;
        }
    }

    [MethodImpl(Inline)]
    public void Commit(ref Memory<T> reservedMemory, int commitSize)
    {
        reservedMemory = Commit(reservedMemory, commitSize);
    }

    [MethodImpl(Inline)]
    public Memory<T> Commit(Memory<T> reservedMemory, int commitSize)
    {
        checked
        {
            int returnSize = reservedMemory.Length - commitSize;
            Debug.Assert(returnSize >= 0);
            if (returnSize == 0) return reservedMemory;

            CurrentPos -= returnSize;
            Debug.Assert(CurrentPos >= 0);

            if (commitSize >= 1)
                return reservedMemory.Slice(0, commitSize);
            else
                return Memory<T>.Empty;
        }
    }
}

public ref struct ElasticSpan<T> where T : unmanaged
{
    public const int PreAllocationUnit = 40;
    public const int PostAllocationUnit = 40;

    public Span<T> InternalBuffer { get; private set; }
    public int Length { get; private set; }
    public int PreAllocSize { get; private set; }
    public int PostAllocSize { get; private set; }
    public int NumRealloc { get; private set; }

    public ElasticSpan(EnsureSpecial yes, Span<T> internalBuffer, int internalStart, int internalSize)
    {
        this.InternalBuffer = internalBuffer;
        this.Length = internalSize;
        this.PreAllocSize = internalStart;
        this.PostAllocSize = internalBuffer.Length - this.Length - this.PreAllocSize;
        this.NumRealloc = 0;
    }

    public ElasticSpan(ReadOnlySpan<T> initialContentsToCopy, PacketSizeSet sizeSet)
    {
        int internalLength = initialContentsToCopy.Length + sizeSet.PreSize + sizeSet.PostSize;

        this.InternalBuffer = new T[internalLength];
        this.Length = initialContentsToCopy.Length;
        this.PreAllocSize = sizeSet.PreSize;
        this.PostAllocSize = sizeSet.PostSize;
        this.NumRealloc = 0;

        initialContentsToCopy.CopyTo(this.InternalBuffer.Slice(sizeSet.PreSize));

    }

    public Span<T> Span
    {
        [MethodImpl(Inline)]
        get => InternalBuffer.Slice(PreAllocSize, Length);
    }

    public void Clear()
    {
        InternalBuffer = Span<T>.Empty;
        Length = 0;
        PreAllocSize = 0;
        PostAllocSize = 0;
    }

    [MethodImpl(Inline)]
    public unsafe void PrependWithData(ReadOnlySpan<T> data, int size = DefaultSize)
    {
        fixed (T* src = data)
            PrependWithData(src, data.Length, size);
    }

    [MethodImpl(Inline)]
    public unsafe void PrependWithData(T* data, int dataLength, int size = DefaultSize)
    {
        size = size._DefaultSize(dataLength);

        if (size == 0) return;
        if (size < 0) throw new ArgumentOutOfRangeException("size");

        if (PreAllocSize < size)
            EnsurePreSize(size);

        fixed (T* dst = &InternalBuffer[PreAllocSize - size])
        {
            Unsafe.CopyBlock((void*)dst, (void*)data, (uint)(dataLength * sizeof(T)));
        }

        PreAllocSize -= size;
        Length += size;
    }

    [MethodImpl(Inline)]
    public ref T Prepend(int size)
    {
        if (size == 0) return ref this.InternalBuffer[PreAllocSize];
        if (size < 0) throw new ArgumentOutOfRangeException("size");

        if (PreAllocSize < size)
            EnsurePreSize(size);

        PreAllocSize -= size;
        Length += size;

        return ref this.InternalBuffer[PreAllocSize];
    }

    [MethodImpl(Inline)]
    public unsafe void AppendWithData(ReadOnlySpan<T> data, int size = DefaultSize)
    {
        fixed (T* src = data)
            AppendWithData(src, data.Length, size);
    }

    [MethodImpl(Inline)]
    public unsafe void AppendWithData(T* data, int dataLength, int size = DefaultSize)
    {
        size = size._DefaultSize(dataLength);

        if (size == 0) return;
        if (size < 0) throw new ArgumentOutOfRangeException("size");

        if (PostAllocSize < size)
            EnsurePostSize(size);

        fixed (T* dst = &InternalBuffer[PreAllocSize + Length])
        {
            Unsafe.CopyBlock((void*)dst, (void*)data, (uint)(dataLength * sizeof(T)));
        }

        PostAllocSize -= size;
        Length += size;
    }

    [MethodImpl(Inline)]
    public ref T Append(int size)
    {
        if (size == 0) return ref this.InternalBuffer[PreAllocSize + Length];
        if (size < 0) throw new ArgumentOutOfRangeException("size");

        if (PostAllocSize < size)
            EnsurePostSize(size);

        PostAllocSize -= size;

        ref T ret = ref this.InternalBuffer[PreAllocSize + Length];

        Length += size;

        return ref ret;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public unsafe void InsertWithData(ReadOnlySpan<T> data, int pos, int size = DefaultSize)
    {
        size = size._DefaultSize(data.Length);
        if (size == 0) return;
        if (size < 0) throw new ArgumentOutOfRangeException("size");

        if (pos < 0 || pos > Length) throw new ArgumentException("pos");

        if (pos == 0)
        {
            PrependWithData(data, size);
            return;
        }
        else if (pos == Length)
        {
            AppendWithData(data, size);
            return;
        }

        if (Length >= 1) this.NumRealloc++;

        int newDataLength = Length + size;
        int newBufferLength = PreAllocSize + newDataLength + PostAllocSize;
        Span<T> newBuffer = new T[newBufferLength];
        InternalBuffer.Slice(PreAllocSize, pos).CopyTo(newBuffer.Slice(PreAllocSize, pos));
        InternalBuffer.Slice(PreAllocSize + pos, Length - pos).CopyTo(newBuffer.Slice(PreAllocSize + pos + size, Length - pos));
        data.CopyTo(newBuffer.Slice(PreAllocSize + pos, size));
        InternalBuffer = newBuffer;
        Length = newDataLength;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public unsafe void InsertWithData(T* data, int dataLength, int pos, int size = DefaultSize)
    {
        size = size._DefaultSize(dataLength);
        if (size == 0) return;
        if (size < 0) throw new ArgumentOutOfRangeException("size");

        if (pos < 0 || pos > Length) throw new ArgumentException("pos");

        if (pos == 0)
        {
            PrependWithData(data, dataLength, size);
            return;
        }
        else if (pos == Length)
        {
            AppendWithData(data, dataLength, size);
            return;
        }

        if (Length >= 1) this.NumRealloc++;

        int newDataLength = Length + size;
        int newBufferLength = PreAllocSize + newDataLength + PostAllocSize;
        Span<T> newBuffer = new T[newBufferLength];
        InternalBuffer.Slice(PreAllocSize, pos).CopyTo(newBuffer.Slice(PreAllocSize, pos));
        InternalBuffer.Slice(PreAllocSize + pos, Length - pos).CopyTo(newBuffer.Slice(PreAllocSize + pos + size, Length - pos));

        fixed (T* dst = &newBuffer[PreAllocSize + pos])
            Unsafe.CopyBlock((void*)dst, (void*)data, (uint)(dataLength * sizeof(T)));

        InternalBuffer = newBuffer;
        Length = newDataLength;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public ref T Insert(int size, int pos)
    {
        if (size == 0) return ref this.InternalBuffer[pos];
        if (size < 0) throw new ArgumentOutOfRangeException("size");

        if (pos < 0 || pos >= Length) throw new ArgumentException("pos");

        if (pos == 0)
        {
            return ref Prepend(size);
        }
        else if (pos == Length)
        {
            return ref Append(size);
        }

        if (Length >= 1) this.NumRealloc++;

        int newDataLength = Length + size;
        int newBufferLength = PreAllocSize + newDataLength + PostAllocSize;
        Span<T> newBuffer = new T[newBufferLength];
        InternalBuffer.Slice(PreAllocSize, pos).CopyTo(newBuffer.Slice(PreAllocSize, pos));
        InternalBuffer.Slice(PreAllocSize + pos, Length - pos).CopyTo(newBuffer.Slice(PreAllocSize + pos + size, Length - pos));
        InternalBuffer = newBuffer;
        Length = newDataLength;

        return ref this.InternalBuffer[pos];
    }

    [MethodImpl(Inline)]
    void EnsurePreSize(int newPreSize)
    {
        if (PreAllocSize >= newPreSize) return;
        newPreSize = newPreSize + PreAllocationUnit;

        // Expand the post size by this chance
        int newPostSize = Math.Max(PostAllocSize, PostAllocationUnit);

        if (Length >= 1) this.NumRealloc++;

        int newBufferLength = newPreSize + Length + newPostSize;
        Span<T> newBuffer = new T[newBufferLength];
        InternalBuffer.Slice(PreAllocSize, Length).CopyTo(newBuffer.Slice(newPreSize, Length));
        PreAllocSize = newPreSize;
        PostAllocSize = newPostSize;
        InternalBuffer = newBuffer;
    }

    [MethodImpl(Inline)]
    void EnsurePostSize(int newPostSize)
    {
        if (PostAllocSize >= newPostSize) return;
        newPostSize = newPostSize + PostAllocationUnit;

        // Expand the pre size by this change
        int newPreSize = Math.Max(PreAllocSize, PreAllocationUnit);

        if (Length >= 1) this.NumRealloc++;

        int newBufferLength = newPreSize + Length + newPostSize;
        Span<T> newBuffer = new T[newBufferLength];
        InternalBuffer.Slice(PreAllocSize, Length).CopyTo(newBuffer.Slice(newPreSize, Length));
        PostAllocSize = newPostSize;
        PreAllocSize = newPreSize;
        InternalBuffer = newBuffer;
    }
}

public ref struct SpanBasedQueue<T>
{
    public int MaxQueueLength { get; }
    public int InitialBufferLength { get; }

    public int Count { get; private set; }

    int BufferSize;
    Span<T> Buffer;

    [MethodImpl(Inline)]
    public SpanBasedQueue(EnsureCtor yes, int initialBufferLength = DefaultSize, int maxQueueLength = DefaultSize)
    {
        this.MaxQueueLength = maxQueueLength._DefaultSize(CoresConfig.SpanBasedQueueSettings.DefaultMaxQueueLength);
        this.MaxQueueLength = Math.Max(0, this.MaxQueueLength);

        this.InitialBufferLength = initialBufferLength._DefaultSize(CoresConfig.SpanBasedQueueSettings.DefaultInitialQueueLength);

        this.BufferSize = this.InitialBufferLength;
        this.Buffer = new T[this.BufferSize];
        this.Count = 0;
    }

    [MethodImpl(Inline)]
    public unsafe void Enqueue(T item) /* 5 ns */
    {
        if (this.MaxQueueLength >= 1 && this.Count >= this.MaxQueueLength) return;

        if (BufferSize <= this.Count)
        {
            BufferSize = Util.GetGreaterOrEqualOptimiedSizePowerOf2(Math.Max(this.Count + 1, this.InitialBufferLength));
            this.Buffer = this.Buffer._ReAlloc(BufferSize);
        }

        this.Buffer[this.Count] = item;
        this.Count++;
    }

    [MethodImpl(Inline)]
    public Span<T> DequeueAll()
    {
        var ret = this.Buffer.Slice(0, this.Count);

        this.BufferSize = 0;
        this.Count = 0;

        return ret;
    }
}

public static class FastMemOperations
{
    // Licensed to the .NET Foundation under one or more agreements.
    // The .NET Foundation licenses this file to you under the MIT license.
    // See the LICENSE file in the project root for more information.
    // From: https://github.com/dotnet/corefx/tree/e2b4dc93c9f9482b67f962106937d4fdd46f5673/src/Common/src/CoreLib/System

    public static unsafe int SequenceCompareTo(ref byte first, int firstLength, ref byte second, int secondLength)
    {
        Debug.Assert(firstLength >= 0);
        Debug.Assert(secondLength >= 0);

        if (Unsafe.AreSame(ref first, ref second))
            goto Equal;

        IntPtr minLength = (IntPtr)((firstLength < secondLength) ? firstLength : secondLength);

        IntPtr i = (IntPtr)0; // Use IntPtr for arithmetic to avoid unnecessary 64->32->64 truncations
        IntPtr n = (IntPtr)(void*)minLength;

        if (Vector.IsHardwareAccelerated && (byte*)n > (byte*)Vector<byte>.Count)
        {
            n -= Vector<byte>.Count;
            while ((byte*)n > (byte*)i)
            {
                if (Unsafe.ReadUnaligned<Vector<byte>>(ref Unsafe.AddByteOffset(ref first, i)) !=
                    Unsafe.ReadUnaligned<Vector<byte>>(ref Unsafe.AddByteOffset(ref second, i)))
                {
                    goto NotEqual;
                }
                i += Vector<byte>.Count;
            }
            goto NotEqual;
        }

        if ((byte*)n > (byte*)sizeof(UIntPtr))
        {
            n -= sizeof(UIntPtr);
            while ((byte*)n > (byte*)i)
            {
                if (Unsafe.ReadUnaligned<UIntPtr>(ref Unsafe.AddByteOffset(ref first, i)) !=
                    Unsafe.ReadUnaligned<UIntPtr>(ref Unsafe.AddByteOffset(ref second, i)))
                {
                    goto NotEqual;
                }
                i += sizeof(UIntPtr);
            }
        }

        NotEqual:  // Workaround for https://github.com/dotnet/coreclr/issues/13549
        while ((byte*)minLength > (byte*)i)
        {
            int result = Unsafe.AddByteOffset(ref first, i).CompareTo(Unsafe.AddByteOffset(ref second, i));
            if (result != 0)
                return result;
            i += 1;
        }

        Equal:
        return firstLength - secondLength;
    }

    public static unsafe bool SequenceEqual(ref byte first, ref byte second, long length)
    {
        Debug.Assert(length >= 0);

        if (Unsafe.AreSame(ref first, ref second))
            goto Equal;

        IntPtr i = (IntPtr)0; // Use IntPtr for arithmetic to avoid unnecessary 64->32->64 truncations
        IntPtr n = (IntPtr)(void*)length;

        if (Vector.IsHardwareAccelerated && (byte*)n >= (byte*)Vector<byte>.Count)
        {
            n -= Vector<byte>.Count;
            while ((byte*)n > (byte*)i)
            {
                if (Unsafe.ReadUnaligned<Vector<byte>>(ref Unsafe.AddByteOffset(ref first, i)) !=
                    Unsafe.ReadUnaligned<Vector<byte>>(ref Unsafe.AddByteOffset(ref second, i)))
                {
                    goto NotEqual;
                }
                i += Vector<byte>.Count;
            }
            return Unsafe.ReadUnaligned<Vector<byte>>(ref Unsafe.AddByteOffset(ref first, n)) ==
                   Unsafe.ReadUnaligned<Vector<byte>>(ref Unsafe.AddByteOffset(ref second, n));
        }

        if ((byte*)n >= (byte*)sizeof(UIntPtr))
        {
            n -= sizeof(UIntPtr);
            while ((byte*)n > (byte*)i)
            {
                if (Unsafe.ReadUnaligned<UIntPtr>(ref Unsafe.AddByteOffset(ref first, i)) !=
                    Unsafe.ReadUnaligned<UIntPtr>(ref Unsafe.AddByteOffset(ref second, i)))
                {
                    goto NotEqual;
                }
                i += sizeof(UIntPtr);
            }
            return Unsafe.ReadUnaligned<UIntPtr>(ref Unsafe.AddByteOffset(ref first, n)) ==
                   Unsafe.ReadUnaligned<UIntPtr>(ref Unsafe.AddByteOffset(ref second, n));
        }

        while ((byte*)n > (byte*)i)
        {
            if (Unsafe.AddByteOffset(ref first, i) != Unsafe.AddByteOffset(ref second, i))
                goto NotEqual;
            i += 1;
        }

        Equal:
        return true;

        NotEqual: // Workaround for https://github.com/dotnet/coreclr/issues/13549
        return false;
    }

    public static bool SequenceEqual<T>(ref T first, ref T second, int length)
        where T : IEquatable<T>
    {
        Debug.Assert(length >= 0);

        if (Unsafe.AreSame(ref first, ref second))
            goto Equal;

        IntPtr index = (IntPtr)0; // Use IntPtr for arithmetic to avoid unnecessary 64->32->64 truncations
        T lookUp0;
        T lookUp1;
        while (length >= 8)
        {
            length -= 8;

            lookUp0 = Unsafe.Add(ref first, index);
            lookUp1 = Unsafe.Add(ref second, index);
            if (!(lookUp0?.Equals(lookUp1) ?? (object)lookUp1! is null))
                goto NotEqual;
            lookUp0 = Unsafe.Add(ref first, index + 1);
            lookUp1 = Unsafe.Add(ref second, index + 1);
            if (!(lookUp0?.Equals(lookUp1) ?? (object)lookUp1! is null))
                goto NotEqual;
            lookUp0 = Unsafe.Add(ref first, index + 2);
            lookUp1 = Unsafe.Add(ref second, index + 2);
            if (!(lookUp0?.Equals(lookUp1) ?? (object)lookUp1! is null))
                goto NotEqual;
            lookUp0 = Unsafe.Add(ref first, index + 3);
            lookUp1 = Unsafe.Add(ref second, index + 3);
            if (!(lookUp0?.Equals(lookUp1) ?? (object)lookUp1! is null))
                goto NotEqual;
            lookUp0 = Unsafe.Add(ref first, index + 4);
            lookUp1 = Unsafe.Add(ref second, index + 4);
            if (!(lookUp0?.Equals(lookUp1) ?? (object)lookUp1! is null))
                goto NotEqual;
            lookUp0 = Unsafe.Add(ref first, index + 5);
            lookUp1 = Unsafe.Add(ref second, index + 5);
            if (!(lookUp0?.Equals(lookUp1) ?? (object)lookUp1! is null))
                goto NotEqual;
            lookUp0 = Unsafe.Add(ref first, index + 6);
            lookUp1 = Unsafe.Add(ref second, index + 6);
            if (!(lookUp0?.Equals(lookUp1) ?? (object)lookUp1! is null))
                goto NotEqual;
            lookUp0 = Unsafe.Add(ref first, index + 7);
            lookUp1 = Unsafe.Add(ref second, index + 7);
            if (!(lookUp0?.Equals(lookUp1) ?? (object)lookUp1! is null))
                goto NotEqual;

            index += 8;
        }

        if (length >= 4)
        {
            length -= 4;

            lookUp0 = Unsafe.Add(ref first, index);
            lookUp1 = Unsafe.Add(ref second, index);
            if (!(lookUp0?.Equals(lookUp1) ?? (object)lookUp1! is null))
                goto NotEqual;
            lookUp0 = Unsafe.Add(ref first, index + 1);
            lookUp1 = Unsafe.Add(ref second, index + 1);
            if (!(lookUp0?.Equals(lookUp1) ?? (object)lookUp1! is null))
                goto NotEqual;
            lookUp0 = Unsafe.Add(ref first, index + 2);
            lookUp1 = Unsafe.Add(ref second, index + 2);
            if (!(lookUp0?.Equals(lookUp1) ?? (object)lookUp1! is null))
                goto NotEqual;
            lookUp0 = Unsafe.Add(ref first, index + 3);
            lookUp1 = Unsafe.Add(ref second, index + 3);
            if (!(lookUp0?.Equals(lookUp1) ?? (object)lookUp1! is null))
                goto NotEqual;

            index += 4;
        }

        while (length > 0)
        {
            lookUp0 = Unsafe.Add(ref first, index);
            lookUp1 = Unsafe.Add(ref second, index);
            if (!(lookUp0?.Equals(lookUp1) ?? (object)lookUp1! is null))
                goto NotEqual;
            index += 1;
            length--;
        }

        Equal:
        return true;

        NotEqual: // Workaround for https://github.com/dotnet/coreclr/issues/13549
        return false;
    }

    public static int SequenceCompareTo<T>(ref T first, int firstLength, ref T second, int secondLength)
        where T : IComparable<T>
    {
        Debug.Assert(firstLength >= 0);
        Debug.Assert(secondLength >= 0);

        var minLength = firstLength;
        if (minLength > secondLength)
            minLength = secondLength;
        for (int i = 0; i < minLength; i++)
        {
            T lookUp = Unsafe.Add(ref second, i);
            int result = (Unsafe.Add(ref first, i)?.CompareTo(lookUp) ?? (((object)lookUp! is null) ? 0 : -1));
            if (result != 0)
                return result;
        }

        return firstLength.CompareTo(secondLength);
    }
}

public class BitStructKey<TStruct> : IEquatable<BitStructKey<TStruct>>, IComparable<BitStructKey<TStruct>>
    where TStruct : unmanaged
{
    public readonly TStruct Value;
    public readonly int HashCode;

    [MethodImpl(Inline)]
    public BitStructKey(TStruct value)
    {
        this.Value = value;
        this.HashCode = this.Value._HashMarvin();
    }

    [MethodImpl(Inline)]
    public int CompareTo(BitStructKey<TStruct>? other)
    {
        return Util.StructBitCompare(in this.Value, in other!.Value);
    }

    [MethodImpl(Inline)]
    public bool Equals(BitStructKey<TStruct>? other)
    {
        if (this.HashCode != other!.HashCode) return false;
        return Util.StructBitEquals(in this.Value, in other!.Value);
    }

    [MethodImpl(Inline)]
    public override bool Equals(object? obj)
    {
        return Equals((BitStructKey<TStruct>)obj!);
    }

    [MethodImpl(Inline)]
    public override int GetHashCode()
    {
        return this.HashCode;
    }

    [MethodImpl(Inline)]
    public override string? ToString()
    {
        return this.Value.ToString();
    }

    [MethodImpl(Inline)]
    public static implicit operator TStruct(BitStructKey<TStruct> key) => key.Value;

    [MethodImpl(Inline)]
    public static implicit operator BitStructKey<TStruct>(TStruct key) => new BitStructKey<TStruct>(key);
}

public static partial class StructComparers<T> where T : unmanaged
{
    public static StructBitComparerImpl StructBitComparer { get; } = new StructBitComparerImpl(EnsureInternal.Yes);

    public class StructBitComparerImpl : IEqualityComparer<T>, IComparer<T>
    {
        internal StructBitComparerImpl(EnsureInternal yes) { }

        public int Compare(T x, T y)
        {
            return x._StructBitCompare(y);
        }

        public bool Equals(T x, T y)
        {
            return x._StructBitEquals(y);
        }

        public int GetHashCode(T obj)
        {
            return obj._HashMarvin();
        }
    }
}

public static partial class MemoryComparers
{
    public static MemoryComparers<byte>.ReadOnlyMemoryComparerImpl ReadOnlyMemoryComparer { get; } = new MemoryComparers<byte>.ReadOnlyMemoryComparerImpl(EnsureInternal.Yes);
    public static MemoryComparers<byte>.MemoryComparerImpl MemoryComparer { get; } = new MemoryComparers<byte>.MemoryComparerImpl(EnsureInternal.Yes);
    public static MemoryComparers<byte>.ArrayComparerImpl ArrayComparer { get; } = new MemoryComparers<byte>.ArrayComparerImpl(EnsureInternal.Yes);
}

public static partial class MemoryComparers<T> where T : unmanaged, IEquatable<T>, IComparable<T>
{
    public static ReadOnlyMemoryComparerImpl ReadOnlyMemoryComparer { get; } = new ReadOnlyMemoryComparerImpl(EnsureInternal.Yes);
    public static MemoryComparerImpl MemoryComparer { get; } = new MemoryComparerImpl(EnsureInternal.Yes);
    public static ArrayComparerImpl ArrayComparer { get; } = new ArrayComparerImpl(EnsureInternal.Yes);

    public class ReadOnlyMemoryComparerImpl : IEqualityComparer<ReadOnlyMemory<T>>, IComparer<ReadOnlyMemory<T>>
    {
        internal ReadOnlyMemoryComparerImpl(EnsureInternal yes) { }

        public int Compare(ReadOnlyMemory<T> x, ReadOnlyMemory<T> y)
        {
            return x._MemCompare(y);
        }

        public bool Equals(ReadOnlyMemory<T> x, ReadOnlyMemory<T> y)
        {
            return x._MemEquals(y);
        }

        public int GetHashCode(ReadOnlyMemory<T> obj)
        {
            return obj._HashMarvin();
        }
    }

    public class MemoryComparerImpl : IEqualityComparer<Memory<T>>, IComparer<Memory<T>>
    {
        internal MemoryComparerImpl(EnsureInternal yes) { }

        public int Compare(Memory<T> x, Memory<T> y)
        {
            return x._MemCompare(y);
        }

        public bool Equals(Memory<T> x, Memory<T> y)
        {
            return x._MemEquals(y);
        }

        public int GetHashCode(Memory<T> obj)
        {
            return obj._HashMarvin();
        }
    }

    public class ArrayComparerImpl : IEqualityComparer<T[]>, IComparer<T[]>
    {
        internal ArrayComparerImpl(EnsureInternal yes) { }

        public int Compare(T[]? x, T[]? y)
        {
            return x!._MemCompare(y!);
        }

        public bool Equals(T[]? x, T[]? y)
        {
            return x!._MemEquals(y!);
        }

        public int GetHashCode(T[] obj)
        {
            return obj._HashMarvin();
        }
    }
}

public class MemoryOrDiskBufferOptions
{
    public int UseStorageThreshold { get; }

    public MemoryOrDiskBufferOptions(int useStorageThreshold = Consts.Numbers.DefaultUseStorageThreshold)
    {
        UseStorageThreshold = useStorageThreshold._NonNegative();
    }
}

// 一定サイズまではメモリ上、それを超えた場合は自動的にストレージ上に移行して保存されるバッファ (非同期アクセスはサポートしていない。将来遠隔ストレージに置くなどして非同期が必要になった場合は実装を追加すること)
public class MemoryOrDiskBuffer : IBuffer<byte>, IDisposable
{
    public MemoryOrDiskBufferOptions Options { get; }

    StreamBasedBuffer CurrentBuffer;

    IHolder Leak;

    public MemoryOrDiskBuffer(MemoryOrDiskBufferOptions? options = null)
    {
        try
        {
            Leak = LeakChecker.Enter(LeakCounterKind.MemoryOrStorageBuffer);

            this.Options = options ?? new MemoryOrDiskBufferOptions();

            // 最初はまず MemoryStream を作る
            CurrentBuffer = new StreamBasedBuffer(new MemoryStream(), true, false);

            SwitchToFileBasedStreamIfNecessary();
        }
        catch
        {
            this._DisposeSafe();
            throw;
        }
    }

    // 必要な場合はファイルベースのストリームに切替える
    void SwitchToFileBasedStreamIfNecessary()
    {
        if (CurrentBuffer.IsMemoryStream)
        {
            if (CurrentBuffer.LongLength >= Options.UseStorageThreshold)
            {
                // 現在のサイズがスレッショルドを超過しているのでファイルベースのストリームに切替える
                long currentMemoryStreamPosition = CurrentBuffer.BaseStream.Position;

                FileObject file = Lfs.CreateDynamicTempFile(prefix: "MemoryOrDiskBuffer");
                try
                {
                    Stream fileStream = file.GetStream(true);

                    // 現在のメモリストリームの内容をファイルストリームに書き出す
                    // (現在の CurrentBuffer の内容は直ちに破棄するので中の MemoryStream から直接吸い出して問題無い)
                    CurrentBuffer.BaseStream.Seek(0, SeekOrigin.Begin);
                    CurrentBuffer.BaseStream.CopyTo(fileStream);

                    fileStream.Seek(currentMemoryStreamPosition, SeekOrigin.Begin);

                    fileStream.Flush();

                    // コピーが完了したら CurrentBuffer を交換する
                    // この際はファイルベースのバッファであるので、オーバーヘッドを少なくするためバッファリングを有効にする
                    StreamBasedBuffer newCurrentBuffer = new StreamBasedBuffer(fileStream, true, true);

                    CurrentBuffer._DisposeSafe(); // 古い MemoryStream は念のため Dispose する

                    CurrentBuffer = newCurrentBuffer; // 交換完了
                }
                catch
                {
                    file._DisposeSafe();

                    CurrentBuffer.BaseStream.Seek(currentMemoryStreamPosition, SeekOrigin.Begin);
                    throw;
                }
            }
        }
    }

    public void Dispose() { this.Dispose(true); GC.SuppressFinalize(this); }
    Once DisposeFlag;
    protected virtual void Dispose(bool disposing)
    {
        if (!disposing || DisposeFlag.IsFirstCall() == false) return;

        CurrentBuffer._DisposeSafe();

        Leak._DisposeSafe();
    }

    public long LongPosition
        => CurrentBuffer.LongPosition;

    public long LongLength
        => CurrentBuffer.LongLength;

    public long LongInternalBufferSize
        => CurrentBuffer.LongLength;

    public long LongRemainLength
    {
        [MethodImpl(Inline)]
        get
        {
            long length = LongLength;
            long position = LongPosition;
            long ret = length - position;
            if (ret < 0) throw new CoresException($"Position ({length}) > Length ({position})");
            return ret;
        }
    }

    public int RemainLength
    {
        [MethodImpl(Inline)]
        get => checked((int)LongRemainLength);
    }
    public void Clear()
        => CurrentBuffer.Clear();

    public bool IsThisEmpty()
        => CurrentBuffer.IsThisEmpty();

    public ReadOnlySpan<byte> Peek(long size, bool allowPartial = false)
        => CurrentBuffer.Peek(size, allowPartial);

    public ReadOnlyMemory<byte> PeekAsMemory(long size, bool allowPartial = false)
        => CurrentBuffer.PeekAsMemory(size, allowPartial);

    public byte PeekOne()
        => CurrentBuffer.PeekOne();

    public ReadOnlySpan<byte> Read(long size, bool allowPartial = false)
        => CurrentBuffer.Read(size, allowPartial);

    public ReadOnlyMemory<byte> ReadAsMemory(long size, bool allowPartial = false)
        => CurrentBuffer.ReadAsMemory(size, allowPartial);

    public byte ReadOne()
        => CurrentBuffer.ReadOne();

    public void Seek(long offset, SeekOrigin mode, bool allocate = false)
    {
        CurrentBuffer.Seek(offset, mode, allocate);
        if (allocate)
            SwitchToFileBasedStreamIfNecessary();
    }

    public void SetLength(long size)
    {
        CurrentBuffer.SetLength(size);
        SwitchToFileBasedStreamIfNecessary();
    }

    public void Write(ReadOnlySpan<byte> data)
    {
        CurrentBuffer.Write(data);
        SwitchToFileBasedStreamIfNecessary();
    }

    public void WriteOne(byte data)
        => CurrentBuffer.WriteOne(data);

    public void Flush()
        => CurrentBuffer.Flush();
}


