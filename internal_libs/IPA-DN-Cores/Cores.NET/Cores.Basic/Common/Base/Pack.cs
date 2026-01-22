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
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

namespace IPA.Cores.Basic;

// 値
public class PackValue : IComparable
{
    uint index;             // インデックス
    public uint Index
    {
        get { return index; }
    }

    object data;            // データ

    // 比較
    int IComparable.CompareTo(object? obj)
    {
        PackValue v = (PackValue)obj!;

        return this.index.CompareTo(v.index);
    }

    // コンストラクタ
    public PackValue(uint index, uint intValue)
    {
        this.index = index;
        this.data = intValue;
    }
    public PackValue(uint index, ulong int64Value)
    {
        this.index = index;
        this.data = int64Value;
    }
    public PackValue(uint index, string str)
    {
        this.index = index;
        this.data = str;
    }
    public PackValue(uint index, byte[] data)
    {
        this.index = index;
        this.data = data;
    }

    // デフォルトの値
    public static PackValue? GetDefaultValue(uint index, PackValueType type)
    {
        switch (type)
        {
            case PackValueType.Int:
                return new PackValue(index, 0);

            case PackValueType.Int64:
                return new PackValue(index, 0);

            case PackValueType.Str:
                return new PackValue(index, "");

            case PackValueType.UniStr:
                return new PackValue(index, "");

            case PackValueType.Data:
                return new PackValue(index, new byte[0]);
        }

        return null;
    }

    // データの取得
    public uint IntValue
    {
        get { return (uint)data; }
    }
    public ulong Int64Value
    {
        get { return (ulong)data; }
    }
    public string StrValue
    {
        get { return (string)data; }
    }
    public byte[] Data
    {
        get { return (byte[])data; }
    }
    public bool BoolValue
    {
        get { return (IntValue == 0 ? false : true); }
    }

    // 読み込む
    public static PackValue? CreateFromBuf(Buf b, uint index, PackValueType type)
    {
        switch (type)
        {
            case PackValueType.Int:
                return new PackValue(index, b.ReadInt());

            case PackValueType.Int64:
                return new PackValue(index, b.ReadInt64());

            case PackValueType.Str:
                return new PackValue(index, b.ReadStr().Trim());

            case PackValueType.UniStr:
                uint len = b.ReadInt();
                if (len == 0)
                {
                    return new PackValue(index, "");
                }
                else
                {
                    byte[] data = b.Read(len - 1);
                    b.Read(1);
                    return new PackValue(index, Str.Utf8Encoding.GetString(data).Trim());

                }

            case PackValueType.Data:
                uint size = b.ReadInt();
                return new PackValue(index, b.Read(size));
        }

        return null;
    }

    // 書き出す
    public void WriteToBuf(Buf b, PackValueType type)
    {
        switch (type)
        {
            case PackValueType.Int:
                b.WriteInt(IntValue);
                break;

            case PackValueType.Int64:
                b.WriteInt64(Int64Value);
                break;

            case PackValueType.Data:
                b.WriteInt((uint)Data.Length);
                b.Write(Data);
                break;

            case PackValueType.Str:
                b.WriteStr(StrValue.Trim());
                break;

            case PackValueType.UniStr:
                byte[] data = Str.Utf8Encoding.GetBytes(StrValue.Trim());
                b.WriteInt((uint)data.Length + 1);
                b.Write(data);
                b.WriteByte(0);
                break;
        }
    }
}

// 要素の型
public enum PackValueType
{
    Int = 0,                // 整数型
    Data = 1,               // データ型
    Str = 2,                // ANSI 文字列型
    UniStr = 3,             // Unicode 文字列型
    Int64 = 4,              // 64 bit 整数型
}

// 要素
public class PackElement : IComparable
{
    string name;            // 要素名
    PackValueType type;         // 型
                                //List<Value> values;		// 値リスト
    Dictionary<uint, PackValue> values;
    uint maxIndex;

    public PackValueType Type => this.type;

    // コンストラクタ
    public PackElement(string name, PackValueType type)
    {
        this.name = name;
        this.type = type;
        maxIndex = 0;
        this.values = new Dictionary<uint, PackValue>();
    }

    // 値の取得
    public PackValue? GetValue(uint index)
    {
        bool tmp;
        return GetValue(index, out tmp);
    }
    public PackValue? GetValue(uint index, out bool exists)
    {
        if (values.ContainsKey(index) == false)
        {
            exists = false;
            return PackValue.GetDefaultValue(index, this.type);
        }
        else
        {
            exists = true;
            return values[index];
        }
    }

    // 値の追加
    public void AddValue(PackValue value)
    {
        bool exists;
        PackValue? existValue = GetValue(value.Index, out exists);

        if (exists)
        {
            values.Remove(value.Index);
        }

        values.Add(value.Index, value);

        if (maxIndex < value.Index)
        {
            maxIndex = value.Index;
        }
    }

    // 型のチェック
    public bool CheckType(PackValueType type)
    {
        return this.type == type;
    }

    // 値の個数の取得
    public uint Count
    {
        get
        {
            if (values.Count >= 1)
            {

                return maxIndex + 1;
            }
            else
            {
                return 0;
            }
        }
    }

    int IComparable.CompareTo(object? obj)
    {
        PackElement e = (PackElement)obj!;

        return string.Compare(this.name, e.name, StringComparison.OrdinalIgnoreCase);
    }

    // 読み込む
    public static PackElement CreateFromBuf(Buf b)
    {
        // 名前
        string name = b.ReadStr(true);

        // 種類
        PackValueType type = (PackValueType)b.ReadInt();

        // 項目数
        uint num = b.ReadInt();

        PackElement e = new PackElement(name, type);

        // 値
        uint i;
        for (i = 0; i < num; i++)
        {
            PackValue v = PackValue.CreateFromBuf(b, i, type)!;

            e.AddValue(v);
        }

        return e;
    }

    // 書き出す
    public void WriteToBuf(Buf b)
    {
        // 名前
        b.WriteStr(this.name, true);

        // 種類
        b.WriteInt((uint)this.type);

        // 項目数
        b.WriteInt(this.Count);

        // 値
        uint i;
        if (this.Count > Pack.MaxValueNum)
        {
            throw new OverflowException();
        }
        for (i = 0; i < this.Count; i++)
        {
            PackValue v = this.GetValue(i)!;

            v.WriteToBuf(b, this.type);
        }
    }
}

// Pack の値
public class PackValueAccessor
{
    public string? StrValue = null;
    public string? UniStrValue = null;
    public uint IntValue = 0;
    public ulong Int64Value = 0;
    public byte[]? DataValue = null;
    public DateTime DateTimeValue = new DateTime(0);
    public bool BoolValue = false;

    public int SIntValue => (int)Math.Min(IntValue, int.MaxValue);
    public long SInt64Value => (long)Math.Min(Int64Value, long.MaxValue);
    public int SIntValueSafeNum => Math.Min(SIntValue, 65535);
    public string StrValueNonNull => StrValue._NonNull();
    public string UniStrValueNonNull => UniStrValue._NonNull();
    public string StrValueNonNullCheck => StrValue._NullCheck();
    public string UniStrValueNonNullCheck => UniStrValue._NullCheck();
    public ReadOnlyMemory<byte> DataValueNonNull => DataValue == null ? ReadOnlyMemory<byte>.Empty : DataValue;
    public string DataValueHexStr => DataValueNonNull._GetHexString();
}

// Pack
public partial class Pack
{
    List<PackElement> elements; // 要素リスト
    bool elementsSorted;
    public const uint MaxValueNum = 65536 * 4;      // 1 つの ELEMENT に格納できる最大 VALUE 数
    public const uint MaxElementNum = 131072 * 4;   // 1 つの PACK に格納できる最大 ELEMENT 数
    public const uint MaxPackSize = (64 * 1024 * 1024 * 4); // シリアル化された PACK の最大サイズ

    // インデクサ
    public PackValueAccessor this[string name]
    {
        get
        {
            return this[name, 0];
        }
    }
    public PackValueAccessor this[string name, uint index]
    {
        get
        {
            PackValueAccessor p = new PackValueAccessor();
            p.IntValue = GetInt(name, index);
            p.Int64Value = GetInt64(name, index);
            p.StrValue = GetStr(name, index);
            p.UniStrValue = GetUniStr(name, index);
            p.DataValue = GetData(name, index);
            p.BoolValue = GetBool(name, index);

            try
            {
                p.DateTimeValue = Util.ConvertDateTime(p.Int64Value);
            }
            catch
            {
            }

            return p;
        }
    }
    public PackValueAccessor this[string name, int index]
    {
        get => this[name, (uint)index];
    }

    // コンストラクタ
    public Pack()
    {
        elements = new List<PackElement>();
        elementsSorted = true;
    }

    // エラーコードの設定
    public void SetError(uint code)
    {
        AddInt("Error", code);
    }

    // エラーコードの取得
    public uint GetError(uint code)
    {
        return GetInt("Error");
    }

    // 指定した要素があるかどうかチェック
    public bool HasMember(string name)
    {
        if (getElement(name) != null)
        {
            return true;
        }
        else
        {
            return false;
        }
    }

    // 要素の追加
    void addElement(PackElement e)
    {
        elements.Add(e);
        elementsSorted = false;
    }
    public void AddStr(string name, string strValue, uint index = 0)
    {
        PackElement e = getElementAndCreateIfNotExists(name, PackValueType.Str);
        e.AddValue(new PackValue(index, strValue));
    }

    public void AddUniStr(string name, string strValue, uint index = 0)
    {
        PackElement e = getElementAndCreateIfNotExists(name, PackValueType.UniStr);
        e.AddValue(new PackValue(index, strValue));
    }

    public void AddInt(string name, uint intValue, uint index = 0)
    {
        PackElement e = getElementAndCreateIfNotExists(name, PackValueType.Int);
        e.AddValue(new PackValue(index, intValue));
    }

    public void AddSInt(string name, int intValue, uint index = 0)
        => AddInt(name, (uint)intValue);

    public void AddBool(string name, bool boolValue, uint index = 0)
    {
        AddInt(name, (uint)(boolValue ? 1 : 0), index);
    }

    public void AddIp(string name, IPAddress ip, uint index = 0)
    {
        if (ip.AddressFamily != AddressFamily.InterNetwork && ip.AddressFamily != AddressFamily.InterNetworkV6)
        {
            throw new ArgumentException(nameof(ip));
        }

        bool isIPv6 = ip.AddressFamily == AddressFamily.InterNetworkV6;

        this.AddBool($"{name}@ipv6_bool", isIPv6, index);

        if (isIPv6)
        {
            this.AddData($"{name}@ipv6_array", ip.GetAddressBytes(), index);
            this.AddInt($"{name}@ipv6_scope_id", (uint)ip.ScopeId, index);
        }
        else
        {
            this.AddData($"{name}@ipv6_array", new byte[16], index);
            this.AddInt($"{name}@ipv6_scope_id", 0, index);
        }

        uint ip_uint = ip._IPToUINT();

        if (Env.IsBigEndian)
        {
            ip_uint = ip_uint._ReverseEndian32_U();
        }

        this.AddInt(name, ip_uint, index);
    }

    public void AddDateTime(string name, DateTime dt, uint index = 0)
    {
        AddInt64(name, (ulong)Util.ConvertDateTime(dt), index);
    }

    public void AddInt64(string name, ulong int64Value, uint index = 0)
    {
        PackElement e = getElementAndCreateIfNotExists(name, PackValueType.Int64);
        e.AddValue(new PackValue(index, int64Value));
    }
    public void AddSInt64(string name, long int64Value, uint index = 0)
        => AddInt64(name, (ulong)int64Value, index);

    public void AddData(string name, byte[] data, uint index = 0)
    {
        PackElement e = getElementAndCreateIfNotExists(name, PackValueType.Data);
        e.AddValue(new PackValue(index, data));
    }
    public void AddData(string name, ReadOnlySpan<byte> data, uint index = 0)
        => AddData(name, data.ToArray(), index);

    public void AddData(string name, ReadOnlyMemory<byte> data, uint index = 0)
        => AddData(name, data.Span, index);

    // 要素の取得
    PackElement? getElement(string name)
    {
        PackElement t = new PackElement(name, PackValueType.Int);
        if (elementsSorted == false)
        {
            elements.Sort();
            elementsSorted = true;
        }
        int i = elements.BinarySearch(t);
        if (i < 0)
        {
            return null;
        }
        else
        {
            return elements[i];
        }
    }
    PackElement getElementAndCreateIfNotExists(string name, PackValueType type)
    {
        PackElement? e = getElement(name);
        if (e != null)
        {
            return e;
        }

        e = new PackElement(name, type);
        addElement(e);

        return e;
    }

    public uint GetCount(string name)
    {
        PackElement? e = getElement(name);
        if (e == null)
        {
            return 0;
        }
        return e.Count;
    }

    public string? GetStr(string name, uint index = 0)
    {
        PackElement? e = getElement(name);
        if (e == null)
        {
            return null;
        }
        if (e.CheckType(PackValueType.Str) == false)
        {
            return null;
        }
        PackValue? v = e.GetValue(index);
        if (v == null)
        {
            return null;
        }
        return v.StrValue;
    }

    public string? GetUniStr(string name, uint index = 0)
    {
        PackElement? e = getElement(name);
        if (e == null)
        {
            return null;
        }
        if (e.CheckType(PackValueType.UniStr) == false)
        {
            return null;
        }
        PackValue? v = e.GetValue(index);
        if (v == null)
        {
            return null;
        }
        return v.StrValue;
    }

    public uint GetInt(string name, uint index = 0)
    {
        PackElement? e = getElement(name);
        if (e == null)
        {
            return 0;
        }
        if (e.CheckType(PackValueType.Int) == false)
        {
            return 0;
        }
        PackValue? v = e.GetValue(index);
        if (v == null)
        {
            return 0;
        }
        return v.IntValue;
    }

    public bool GetBool(string name, uint index = 0)
    {
        return (GetInt(name, index) == 0 ? false : true);
    }

    public ulong GetInt64(string name, uint index = 0)
    {
        PackElement? e = getElement(name);
        if (e == null)
        {
            return 0;
        }
        if (e.CheckType(PackValueType.Int64) == false)
        {
            return 0;
        }
        PackValue? v = e.GetValue(index);
        if (v == null)
        {
            return 0;
        }
        return v.Int64Value;
    }

    public IPAddress? GetIp(string name, uint index = 0)
    {
        bool isIPv6 = this.GetBool($"{name}@ipv6_bool", index);

        if (isIPv6)
        {
            byte[]? data = this.GetData($"{name}@ipv6_array", index);
            if (data == null || data.Length != 16) return null;
            uint scopeId = this.GetInt($"{name}@ipv6_scope_id", index);

            return new IPAddress(data, (long)scopeId);
        }
        else
        {
            var e = getElement(name);
            if (e == null) return null;
            if (e.Type != PackValueType.Int) return null;

            uint ip_uint = this.GetInt(name, index);

            if (Env.IsBigEndian)
            {
                ip_uint = ip_uint._ReverseEndian32_U();
            }

            return IPUtil.UINTToIP(ip_uint);
        }
    }

    public byte[]? GetData(string name, uint index = 0)
    {
        PackElement? e = getElement(name);
        if (e == null)
        {
            return null;
        }
        if (e.CheckType(PackValueType.Data) == false)
        {
            return null;
        }
        PackValue? v = e.GetValue(index);
        if (v == null)
        {
            return null;
        }
        return v.Data;
    }

    // 読み込む
    public static Pack CreateFromBuf(Buf b)
    {
        Pack p = new Pack();
        uint i, num;

        if (b.Size > Pack.MaxPackSize)
        {
            throw new OverflowException();
        }

        num = b.ReadInt();

        if (num > Pack.MaxElementNum)
        {
            throw new OverflowException();
        }

        for (i = 0; i < num; i++)
        {
            p.addElement(PackElement.CreateFromBuf(b));
        }

        return p;
    }

    public static Pack CreateFromMemory(ReadOnlySpan<byte> data)
    {
        return CreateFromBuf(new Buf(data.ToArray()));
    }
    public static Pack CreateFromMemory(ReadOnlyMemory<byte> data)
        => CreateFromMemory(data.Span);

    // バッファに変換
    public Buf WriteToBuf()
    {
        Buf b = new Buf();

        // 要素数
        b.WriteInt((uint)elements.Count);

        // 要素
        foreach (PackElement e in elements)
        {
            e.WriteToBuf(b);
        }

        b.SeekToBegin();

        return b;
    }

    // バイトデータ
    public byte[] ByteData
    {
        get
        {
            return WriteToBuf().ByteData;
        }
    }

    public Pack Clone()
    {
        Buf buf = this.WriteToBuf();
        buf.SeekToBegin();
        return Pack.CreateFromBuf(buf);
    }

    public VpnError GetErrorFromPack()
    {
        int err = this["error"].SIntValue;

        return (VpnError)err;
    }

    public VpnException? GetExceptionFromPack()
    {
        VpnError err = GetErrorFromPack();

        if (err == VpnError.ERR_NO_ERROR)
        {
            return null;
        }

        return new VpnException(err, this);
    }

    public void ThrowIfError()
    {
        VpnException? ex = GetExceptionFromPack();

        if (ex != null)
        {
            throw ex;
        }
    }
}

