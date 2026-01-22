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
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Serialization;

using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

namespace IPA.Cores.Basic;

public class WpcPack
{
    public Pack Pack { get; private set; }
    public string HostKey { get; private set; } = "";
    public string HostSecret2 { get; private set; } = "";

    public WpcPack(Pack pack, string? hostKey = null, string? hostSecret2 = null)
    {
        this.Pack = pack;
        this.HostKey = hostKey._NonNull();
        this.HostSecret2 = hostSecret2._NonNull();
    }

    public string ToPacketString()
        => ToPacketBinary().Span._GetString_Ascii();

    public SpanBuffer<byte> ToPacketBinary()
    {
        WpcItemList list = new WpcItemList();

        var packData = this.Pack.WriteToBuf().ByteData;
        list.Add("PACK", packData);

        var hashData = Secure.HashSHA1(packData);
        list.Add("HASH", hashData);

        if (this.HostKey._IsFilled() && this.HostSecret2._IsFilled())
        {
            var hostKeyData = this.HostKey._GetHexBytes();
            var hostSecret2Data = this.HostSecret2._GetHexBytes();

            if (hostKeyData.Length != 20)
            {
                throw new CoresLibException("hostKeyData.Length != 20");
            }

            if (hostSecret2Data.Length != 20)
            {
                throw new CoresLibException("hostSecret2Data.Length != 20");
            }

            list.Add("HOST", hostKeyData);
            list.Add("HOST", hostSecret2Data);
        }

        return list.ToPacketBinary();
    }

    public static WpcPack Parse(string recvStr, bool requireKeyAndSecret)
    {
        WpcItemList items = WpcItemList.Parse(recvStr);
        var packItem = items.Find("PACK");
        var hashItem = items.Find("HASH");
        var hostKeyItem = items.Find("HOST", 0);
        var hostSecret2Item = items.Find("HOST", 1);

        if (packItem == null || hashItem == null)
        {
            throw new CoresLibException("packItem == null || hashItem == null");
        }

        var hash = Secure.HashSHA1(packItem.Data.Span).AsSpan();

        if (hash._MemEquals(hashItem.Data.Span) == false)
        {
            throw new CoresLibException("Different hash");
        }

        Buf buf = new Buf(packItem.Data.ToArray());

        var pack = Pack.CreateFromBuf(buf);

        string hostKey = "";
        string hostSecret2 = "";

        if (requireKeyAndSecret)
        {
            if (hostKeyItem == null || hostKeyItem.Data.Length != 20)
            {
                throw new CoresLibException("hostKeyItem == null || hostKeyItem.Data.Length != 20");
            }

            if (hostSecret2Item == null || hostSecret2Item.Data.Length != 20)
            {
                throw new CoresLibException("hostSecret2Item == null || hostSecret2Item.Data.Length != 20");
            }
        }

        hostKey = hostKeyItem?.Data._GetHexString() ?? "";
        hostSecret2 = hostSecret2Item?.Data._GetHexString() ?? "";

        return new WpcPack(pack, hostKey, hostSecret2);
    }
}

public class WpcItemList : List<WpcItem>
{
    public static WpcItemList Parse(string str)
    {
        ReadOnlySpanBuffer<byte> buf = str._GetBytes_Ascii();

        return Parse(ref buf);
    }

    public static WpcItemList Parse(ref ReadOnlySpanBuffer<byte> buf)
    {
        WpcItemList ret = new WpcItemList();

        while (true)
        {
            if (WpcItem.Parse(ref buf, out string name, out ReadOnlyMemory<byte> data) == false)
            {
                break;
            }

            ret.Add(name, data);
        }

        return ret;
    }

    public WpcItem? Find(string name, int index = 0)
    {
        return this.Where(x => x.Name._IsSamei(name) && x.Index == index).SingleOrDefault();
    }

    public void Add(string name, ReadOnlyMemory<byte> data)
    {
        name = name.ToUpperInvariant();

        int index = this.Where(x => x.Name._IsSamei(name)).Count();

        this.Add(new WpcItem(name, index, data));
    }

    public void Emit(ref SpanBuffer<byte> buf)
    {
        foreach (var item in this)
        {
            item.Emit(ref buf);
        }
    }

    public SpanBuffer<byte> ToPacketBinary()
    {
        SpanBuffer<byte> ret = new SpanBuffer<byte>();

        this.Emit(ref ret);

        return ret;
    }

    public string ToPacketString()
        => this.ToPacketBinary().Span._GetString_Ascii();
}

public class WpcItem
{
    public string Name { get; }
    public int Index { get; }

    public ReadOnlyMemory<byte> Data { get; }

    public WpcItem(string name, int index, ReadOnlyMemory<byte> data)
    {
        if (name.Length > 4) throw new ArgumentException("name.Length > 4");
        this.Name = name.ToUpperInvariant();
        this.Index = index;
        this.Data = data;
    }

    public static bool Parse(ref ReadOnlySpanBuffer<byte> buf, out string name, out ReadOnlyMemory<byte> data)
    {
        name = "";
        data = default;

        var nameBuf = buf.Read(4, allowPartial: true);
        if (nameBuf.Length != 4) return false;
        name = nameBuf._GetString_Ascii().ToUpperInvariant();

        int i = name.IndexOf(' ');
        if (i != -1) name = name.Substring(0, i);

        var sizeStrBuf = buf.Read(10, allowPartial: true);
        if (sizeStrBuf.Length != 10) return false;

        int size = sizeStrBuf._GetString_Ascii()._ToInt();
        size = Math.Max(size, 0);
        if (size > Pack.MaxPackSize) throw new CoresException($"size ({size}) > Pack.MaxPackSize ({Pack.MaxPackSize})");

        var dataBuf = buf.Read(size, allowPartial: true);
        if (dataBuf.Length != size) return false;
        string dataStr = dataBuf._GetString_Ascii();
        data = Str.Base64Decode(Str.Safe64ToBase64(dataStr));

        return true;
    }

    public void Emit(ref SpanBuffer<byte> buf)
    {
        string name = this.Name.ToUpperInvariant();
        if (name.Length < 4)
        {
            int pad = 4 - name.Length;
            name = name + ' '._MakeCharArray(pad);
        }

        var nameBuf = name.ToUpperInvariant()._GetBytes_Ascii();
        if (nameBuf.Length != 4) throw new CoresLibException("nameBuf.Length != 4");

        string dataStr = Str.Base64ToSafe64(Str.Base64Encode(this.Data));
        var dataBuf = dataStr._GetBytes_Ascii();

        int dataSize = dataBuf.Length;

        string dataSizeStr = dataSize.ToString("0000000000");
        var dataSizeStrData = dataSizeStr._GetBytes_Ascii();

        buf.Write(nameBuf);
        buf.Write(dataSizeStrData);
        buf.Write(dataBuf);
    }
}

public class WpcResult
{
    public bool IsOk => ErrorCode == VpnError.ERR_NO_ERROR;
    public bool IsError => !IsOk;

    [JsonIgnore]
    [NoDebugDump]
    public Pack Pack { get; }

    public VpnError ErrorCode { get; }
    public string ErrorCodeString => ErrorCode.ToString();

    public KeyValueList<string, string> AdditionalInfo { get; } = new KeyValueList<string, string>();

    public KeyValueList<string, string> ClientInfo { get; } = new KeyValueList<string, string>();

    public string? ErrorLocation { get; }
    public string? AdditionalErrorStr { get; }

    public WpcResult(Pack? pack = null) : this(VpnError.ERR_NO_ERROR, pack) { }

    public WpcResult(VpnError errorCode, Pack? pack = null, string? additionalErrorStr = null, [CallerFilePath] string filename = "", [CallerLineNumber] int line = 0, [CallerMemberName] string? caller = null)
    {
        if (pack == null) pack = new Pack();

        this.Pack = pack;

        this.ErrorCode = errorCode;
        this.Pack.AddInt("Error", (uint)this.ErrorCode);

        if (this.IsError)
        {
            this.AdditionalErrorStr = additionalErrorStr._NonNullTrim();
            if (this.AdditionalErrorStr._IsEmpty()) this.AdditionalErrorStr = null;

            this.ErrorLocation = $"{filename}:{line} by {caller}";
        }
    }

    public WpcResult(Exception ex, Pack? pack = null)
    {
        if (pack == null) pack = new Pack();

        this.Pack = pack;
        this.ErrorCode = VpnError.ERR_TEMP_ERROR;
        this.Pack.AddInt("Error", (uint)this.ErrorCode);
        this.ErrorLocation = ex.StackTrace?.ToString() ?? "Unknown";
        this.AdditionalErrorStr = ex.Message;
    }

    public WpcPack ToWpcPack()
    {
        WpcPack wp = new WpcPack(this.Pack);

        return wp;
    }
}

#endif

