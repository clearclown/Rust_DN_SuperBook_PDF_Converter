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
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;

using IPA.Cores.Basic;
using IPA.Cores.Basic.Legacy;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using System.Diagnostics.CodeAnalysis;

#pragma warning disable 162

namespace IPA.Cores.Basic;

public class IPAddr : IComparable<IPAddr>, IEquatable<IPAddr>
{
    [Obsolete("Hotate InetDb のデシリアライズのために残しているが、使用してはなりません")]
    public IPAddr()
    {
    }

    public byte[] Bytes = null!;
    public int Size;
    public AddressFamily AddressFamily;
    public long ScopeId = 0;

    public virtual byte[] GetBytes()
    {
        return Bytes;
    }

    public virtual BigNumber GetBigNumber()
    {
        return FullRoute.ByteToBigNumber(this.Bytes, this.AddressFamily);
    }

    public virtual IPAddress GetIPAddress()
    {
        if (this.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return new IPAddress(this.Bytes, this.ScopeId);
        }
        else
        {
            return new IPAddress(this.Bytes);
        }
    }

    public override string ToString()
    {
        return this.GetIPAddress().ToString();
    }

    public virtual string GetZeroPaddingFullString()
    {
        throw new NotImplementedException();
    }

    public virtual string GetBinaryString()
    {
        byte[] tmp = this.GetBytes();

        StringBuilder sb = new StringBuilder();

        foreach (byte b in tmp)
        {
            string str = Convert.ToString((uint)b, 2);

            if (str.Length <= 7)
            {
                str = Str.MakeCharArray('0', 8 - str.Length) + str;
            }

            sb.Append(str);
        }

        return sb.ToString();
    }

    public virtual byte[] GetBinaryBytes()
    {
        byte[] tmp = this.GetBytes();
        byte[] ret = new byte[this.Size * 8];
        int c = 0;

        foreach (byte b in tmp)
        {
            int i;
            for (i = 7; i >= 0; i--)
            {
                ret[c++] = (byte)((((b >> i) & 0x01) == 0) ? 0 : 1);
            }
        }

        return ret;
    }

    public virtual IPAddr Add(int i)
    {
        throw new NotImplementedException();
    }

    public static byte[] PadBytes(byte[] b, int size)
    {
        if (size <= 0 || b.Length >= size)
        {
            return b;
        }

        int padsize = size - b.Length;
        byte[] ret = new byte[size];
        Util.CopyByte(ret, padsize, b, 0, b.Length);

        return ret;
    }

    public static IPAddr GetMinValue(AddressFamily f)
    {
        if (f == AddressFamily.InterNetwork)
        {
            return new IPv4Addr(new byte[] { 0x00, 0x00, 0x00, 0x00, });
        }
        else if (f == AddressFamily.InterNetworkV6)
        {
            return new IPv6Addr(new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, });
        }
        else
        {
            throw new ApplicationException("Invalid AddressFamily.");
        }
    }

    public static IPAddr GetMaxValue(AddressFamily f)
    {
        if (f == AddressFamily.InterNetwork)
        {
            return new IPv4Addr(new byte[] { 0xff, 0xff, 0xff, 0xff, });
        }
        else if (f == AddressFamily.InterNetworkV6)
        {
            return new IPv6Addr(new byte[] { 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, });
        }
        else
        {
            throw new ApplicationException("Invalid AddressFamily.");
        }
    }

    public static IPAddr FromBinaryString(string str)
    {
        if (str.Length != 32 && str.Length != 128)
        {
            throw new ApplicationException("Invalid binary string.");
        }

        byte[] a = new byte[str.Length / 8];

        int i;
        int n = 0;
        for (i = 0; i < str.Length; i += 8)
        {
            a[n++] = Convert.ToByte(str.Substring(i, 8), 2);
        }

        return FromBytes(a);
    }

    public static IPAddr FromString(string str)
    {
        IPAddress a = IPAddress.Parse(str);

        if (a.AddressFamily == AddressFamily.InterNetwork)
        {
            return new IPv4Addr(a);
        }
        else if (a.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return new IPv6Addr(a);
        }
        else
        {
            throw new ApplicationException("Not an IP address.");
        }
    }

    public static IPAddr FromAddress(IPAddress address)
    {
        return FromBytes(address.GetAddressBytes(), address._ScopeIdSafe());
    }

    public static IPAddr FromBytes(byte[] b, long scopeId = 0)
    {
        if (b.Length == 4)
        {
            return new IPv4Addr(b);
        }
        else if (b.Length == 16)
        {
            return new IPv6Addr(b, scopeId);
        }
        else
        {
            throw new ApplicationException("Not an IP address.");
        }
    }

    public int CompareTo(IPAddr? other)
    {
        return Util.MemCompare(this.Bytes, other!.Bytes);
    }

    public bool Equals(IPAddr? other)
    {
        return Util.MemEquals(this.Bytes, other!.Bytes);
    }

    public static int GetAddressSizeFromAddressFamily(AddressFamily family)
    {
        switch (family)
        {
            case AddressFamily.InterNetwork:
                return 4;
            case AddressFamily.InterNetworkV6:
                return 16;
        }

        throw new ApplicationException("invalid family");
    }

    int hashcode_cache = 0;
    bool hashcode_flag = false;
    public override int GetHashCode()
    {
        if (hashcode_flag == false)
        {
            int tmp = 0;
            if (this.Size == 4)
            {
                tmp = BitConverter.ToInt32(this.Bytes, 0);
            }
            else if (this.Size == 16)
            {
                tmp = 0;

                tmp ^= BitConverter.ToInt32(this.Bytes, 0);
                tmp ^= BitConverter.ToInt32(this.Bytes, 4);
                tmp ^= BitConverter.ToInt32(this.Bytes, 8);
                tmp ^= BitConverter.ToInt32(this.Bytes, 12);
            }

            hashcode_cache = tmp;

            hashcode_flag = true;
        }

        return hashcode_cache;
    }

    public static bool operator >(IPAddr a, IPAddr b)
    {
        if (object.ReferenceEquals(a, b)) return false;
        if ((object)a == null || (object)b == null) return false;
        return a.CompareTo(b) > 0;
    }

    public static bool operator <(IPAddr a, IPAddr b)
    {
        if (object.ReferenceEquals(a, b)) return false;
        if ((object)a == null || (object)b == null) return false;
        return a.CompareTo(b) < 0;
    }

    public static bool operator >=(IPAddr a, IPAddr b)
    {
        if (object.ReferenceEquals(a, b)) return false;
        if ((object)a == null || (object)b == null) return false;
        return a.CompareTo(b) <= 0;
    }

    public static bool operator <=(IPAddr a, IPAddr b)
    {
        if (object.ReferenceEquals(a, b)) return false;
        if (a == null || b == null) return false;
        return a.CompareTo(b) >= 0;
    }
}

public class IPv4Addr : IPAddr
{
#pragma warning disable CS0618 // 型またはメンバーが旧型式です
    public IPv4Addr(IPAddress a)
    {
        if (a.AddressFamily != AddressFamily.InterNetwork)
        {
            throw new ApplicationException("Not IPv4");
        }

        this.Bytes = a.GetAddressBytes();
        this.Size = 4;
        this.AddressFamily = AddressFamily.InterNetwork;
    }

    public IPv4Addr(byte[] b)
    {
        if (b.Length != 4)
        {
            throw new ApplicationException("b.Length != 4");
        }

        this.Bytes = b;
        this.Size = 4;
        this.AddressFamily = AddressFamily.InterNetwork;
    }

    public IPv4Addr(string str)
    {
        IPAddress a = IPUtil.StrToIP(str)!;

        if (a.AddressFamily != AddressFamily.InterNetwork)
        {
            throw new ApplicationException("Not IPv4");
        }

        this.Bytes = a.GetAddressBytes();
        this.Size = 4;
        this.AddressFamily = AddressFamily.InterNetwork;
    }
#pragma warning restore CS0618 // 型またはメンバーが旧型式です

    public override IPAddr Add(int i)
    {
        BigNumber b1 = this.GetBigNumber();

        b1 += i;

        if (b1 < 0)
        {
            b1 = 0;
        }
        if (b1 > 0xffffffffUL)
        {
            b1 = 0xffffffffUL;
        }

        byte[] tmp = FullRoute.BigNumberToByte(b1, AddressFamily.InterNetwork);
        return new IPv4Addr(IPAddr.PadBytes(tmp, 4));
    }

    public override string GetZeroPaddingFullString()
    {
        return $"{Bytes[0]:D3}.{Bytes[1]:D3}.{Bytes[2]:D3}.{Bytes[3]:D3}";
    }
}

public class IPv6Addr : IPAddr
{
    readonly static BigNumber max_ipv6;

    static IPv6Addr()
    {
        BigNumber a = new BigNumber(0x100000000UL);
        BigNumber b;

        b = a * a * a * a;

        b -= 1;

        max_ipv6 = b;
    }

#pragma warning disable CS0618 // 型またはメンバーが旧型式です
    public IPv6Addr(IPAddress a)
    {
        if (a.AddressFamily != AddressFamily.InterNetworkV6)
        {
            throw new ApplicationException("Not IPv6");
        }

        this.Bytes = a.GetAddressBytes();
        this.Size = 16;
        this.AddressFamily = AddressFamily.InterNetworkV6;
        this.ScopeId = a._ScopeIdSafe();
    }

    public IPv6Addr(byte[] b, long scopeId = 0)
    {
        if (b.Length != 16)
        {
            throw new ApplicationException("b.Length != 16");
        }

        this.Bytes = b;
        this.Size = 16;
        this.AddressFamily = AddressFamily.InterNetworkV6;
        this.ScopeId = scopeId;
    }

    public IPv6Addr(string str)
    {
        IPAddress a = IPUtil.StrToIP(str)!;

        if (a.AddressFamily != AddressFamily.InterNetworkV6)
        {
            throw new ApplicationException("Not IPv6");
        }

        this.Bytes = a.GetAddressBytes();
        this.Size = 16;
        this.AddressFamily = AddressFamily.InterNetworkV6;
        this.ScopeId = a._ScopeIdSafe();
    }
#pragma warning restore CS0618 // 型またはメンバーが旧型式です

    public override IPAddr Add(int i)
    {
        BigNumber b1 = this.GetBigNumber();

        //byte[] a1 = FullRoute.BigIntToByte(b1, AddressFamily.InterNetworkV6);

        b1 += i;

        //byte[] a2 = FullRoute.BigIntToByte(b1, AddressFamily.InterNetworkV6);

        if (b1 < 0)
        {
            b1 = 0;
        }
        if (b1 > max_ipv6)
        {
            b1 = max_ipv6;
        }

        byte[] tmp = FullRoute.BigNumberToByte(b1, AddressFamily.InterNetworkV6);
        return new IPv6Addr(tmp, this.ScopeId);
    }

    public override string GetZeroPaddingFullString()
    {
        return $"{Bytes[0]:X2}{Bytes[1]:X2}:{Bytes[2]:X2}{Bytes[3]:X2}:" +
            $"{Bytes[4]:X2}{Bytes[5]:X2}:{Bytes[6]:X2}{Bytes[7]:X2}:" +
            $"{Bytes[8]:X2}{Bytes[9]:X2}:{Bytes[10]:X2}{Bytes[11]:X2}:" +
            $"{Bytes[12]:X2}{Bytes[13]:X2}:{Bytes[14]:X2}{Bytes[15]:X2}";
    }
}

public class FullRouteEntry : IComparable<FullRouteEntry>, IEquatable<FullRouteEntry>, IComparable
{
    public readonly IPAddr Address;
    public readonly int SubnetLength;
    public int[] AsPath;
    public int OriginAs;
    public string TagString = "";
    public object? TmpObject;
    int hash_code;

    public FullRouteEntry(Buf buf, int addressSize)
    {
        this.Address = IPAddr.FromBytes(buf.Read((uint)addressSize));
        this.SubnetLength = (int)buf.RawReadInt();
        int num_aspath = (int)buf.RawReadInt();
        int i;
        this.AsPath = new int[num_aspath];
        for (i = 0; i < num_aspath; i++)
        {
            this.AsPath[i] = (int)buf.RawReadInt();
        }
        this.OriginAs = (int)buf.RawReadInt();
        this.hash_code = (int)buf.RawReadInt();
    }

    public FullRouteEntry(IPAddr addr, int subnetLen, string asPathStr, object? tmpObject = null)
    {
        this.Address = addr;
        this.SubnetLength = subnetLen;

        if (this.SubnetLength > FullRoute.GetMaxSubnetSize(addr.AddressFamily))
        {
            throw new ApplicationException("subnet_len is too large.");
        }

        this.AsPath = FullRoute.ParseAsPath(asPathStr);

        if (this.AsPath.Length >= 1)
        {
            this.OriginAs = this.AsPath[this.AsPath.Length - 1];
        }

        byte[] addr_bytes = this.Address.GetBytes();

        if (addr_bytes.Length == 4)
        {
            hash_code = BitConverter.ToInt32(addr_bytes, 0);
            hash_code ^= SubnetLength;
        }
        else if (addr_bytes.Length == 16)
        {
            hash_code = 0;

            hash_code ^= BitConverter.ToInt32(addr_bytes, 0);
            hash_code ^= BitConverter.ToInt32(addr_bytes, 4);
            hash_code ^= BitConverter.ToInt32(addr_bytes, 8);
            hash_code ^= BitConverter.ToInt32(addr_bytes, 12);
            hash_code ^= SubnetLength;
        }
        else
        {
            throw new ApplicationException("addr_bytes.Length");
        }

        this.TmpObject = tmpObject;
    }

    public ulong NumIP
    {
        get
        {
            if (Address.AddressFamily == AddressFamily.InterNetwork)
            {
                return (ulong)(1UL << (32 - this.SubnetLength));
            }
            else
            {
                int v = 64 - this.SubnetLength;
                if (v < 0)
                {
                    v = 0;
                }
                return (ulong)(1UL << v);
            }
        }
    }

    public static FullRouteEntry Parse(string ipAndMask, string asPathStr, AddressFamily addressFamily)
    {
        int max_size = FullRoute.GetMaxSubnetSize(addressFamily);

        int i = ipAndMask.IndexOf('/');
        if (i == -1)
        {
            throw new ApplicationException("ip_and_mask format error.");
        }

        string str1 = ipAndMask.Substring(0, i);
        string str2 = ipAndMask.Substring(i + 1);

        int subnet_len = Str.StrToInt(str2);

        if (subnet_len < 0 || subnet_len > max_size)
        {
            throw new ApplicationException("invalid subnet len.");
        }

        IPAddress tmpa = IPAddress.Parse(str1);

        if (tmpa.AddressFamily != addressFamily)
        {
            throw new ApplicationException("ip address wrong format.");
        }

        tmpa = IPUtil.GetPrefixAddress(tmpa, (tmpa.AddressFamily == AddressFamily.InterNetwork ? IPUtil.IntToSubnetMask4(subnet_len) : IPUtil.IntToSubnetMask6(subnet_len)));

        IPAddr a = IPAddr.FromBytes(tmpa.GetAddressBytes());

        if (a.AddressFamily != addressFamily)
        {
            throw new ApplicationException("ip address wrong format.");
        }

        return new FullRouteEntry(a, subnet_len, asPathStr);
    }

    public override string ToString()
    {
        string ret = this.Address.ToString() + "/" + this.SubnetLength.ToString();

        foreach (int i in this.AsPath)
        {
            ret += " " + i.ToString();
        }

        return ret;
    }

    public override int GetHashCode()
    {
        return hash_code;
    }

    public int CompareTo(object? obj)
    {
        return Util.MemCompare(this.Address.GetBytes(), ((FullRouteEntry)obj!).Address.GetBytes());
    }

    int IComparable<FullRouteEntry>.CompareTo(FullRouteEntry? other)
    {
        return Util.MemCompare(this.Address.GetBytes(), other!.Address.GetBytes());
    }

    bool IEquatable<FullRouteEntry>.Equals(FullRouteEntry? other)
    {
        return Util.MemEquals(this.Address.GetBytes(), other!.Address.GetBytes());
    }

    public string GetBinaryString()
    {
        string str = this.Address.GetBinaryString();

        return str.Substring(0, this.SubnetLength);
    }

    public byte[] GetBinaryBytes()
    {
        return Util.CopyByte(this.Address.GetBinaryBytes(), 0, this.SubnetLength);
    }

    public bool Contains(IPAddr target)
    {
        string target_str = target.GetBinaryString();
        string subnet_str = this.GetBinaryString();

        return target_str.StartsWith(subnet_str);
    }

    public static int CompareBySubnetLength(FullRouteEntry x, FullRouteEntry y)
    {
        return x.SubnetLength.CompareTo(y.SubnetLength);
    }

    public void Dump(Buf buf)
    {
        buf.Write(this.Address.Bytes);
        buf.RawWriteInt((uint)this.SubnetLength);
        buf.RawWriteInt((uint)this.AsPath.Length);
        foreach (int a in this.AsPath)
        {
            buf.RawWriteInt((uint)a);
        }
        buf.RawWriteInt((uint)this.OriginAs);
        buf.RawWriteInt((uint)this.hash_code);
    }

    public string ToCsvLine()
    {
        StringBuilder sb = new StringBuilder();

        int i;
        for (i = 0; i < this.AsPath.Length; i++)
        {
            int asn = this.AsPath[i];

            sb.Append(asn.ToString());

            if (i != (this.AsPath.Length - 1))
            {
                sb.Append(" ");
            }
        }

        return this.Address.ToString() + "," + this.SubnetLength.ToString() + "," +
            "AS" + this.OriginAs.ToString() + "," + sb.ToString();
    }
}

public class FullRouteAsNumber : IComparable<FullRouteAsNumber>, IEquatable<FullRouteAsNumber>
{
    public readonly int Number;
    public readonly string Name;
    public readonly string Country2;

    public List<FullRouteEntry>? Stat_IPv4List = null;
    public List<FullRouteEntry>? Stat_IPv6List = null;

    public ulong NumIPv4;
    public ulong NumIPv6;

    public FullRouteAsNumber(int num, string name, string country2)
    {
        this.Number = num;
        this.Name = name;
        this.Country2 = country2;
    }

    public FullRouteAsNumber(Buf buf)
    {
        this.Number = (int)buf.ReadInt();
        this.Name = buf.ReadAsciiStr();
        this.Country2 = buf.ReadAsciiStr();
    }

    public void Dump(Buf buf)
    {
        buf.WriteInt((uint)this.Number);
        buf.WriteAsciiStr(this.Name);
        buf.WriteAsciiStr(this.Country2);
    }

    public override bool Equals(object? obj)
    {
        if (obj is FullRouteAsNumber)
        {
            return this.Number.Equals(((FullRouteAsNumber)obj).Number);
        }

        return false;
    }

    public override string ToString()
    {
        return this.Number.ToString();
    }

    public override int GetHashCode()
    {
        return this.Number;
    }

    public bool Equals(FullRouteAsNumber? other)
    {
        return this.Number.Equals(other!.Number);
    }

    public int CompareTo(FullRouteAsNumber? other)
    {
        return this.Number.CompareTo(other!.Number);
    }

    public static FullRouteAsNumber NewDummyAs(int num)
    {
        return new FullRouteAsNumber(num, "AS" + num.ToString(), "ZZ");
    }

    public string VirtualIp
    {
        get
        {
            byte[] bytes = BitConverter.GetBytes(Util.Endian(((uint)this.Number) | 0x7F000000));

            return new IPAddress(bytes).ToString();
        }
    }
}

public class FullRouteAsList
{
    public Dictionary<int, FullRouteAsNumber> List = new Dictionary<int, FullRouteAsNumber>();

    public FullRouteAsList()
    {
    }

    public FullRouteAsList(Buf buf)
    {
        // version
        int ver = (int)buf.ReadInt();
        // num
        int num = (int)buf.ReadInt();
        // entries
        int i;
        for (i = 0; i < num; i++)
        {
            this.Insert(new FullRouteAsNumber(buf));
        }
    }

    public void Dump(Buf buf)
    {
        // version
        buf.WriteInt(1);
        // count
        buf.WriteInt((uint)this.List.Count);
        // entries
        foreach (FullRouteAsNumber a in List.Values)
        {
            a.Dump(buf);
        }
    }

    public string ToCsv()
    {
        StringWriter w = new StringWriter();
        List<FullRouteAsNumber> o = new List<FullRouteAsNumber>();

        foreach (FullRouteAsNumber a in List.Values)
        {
            o.Add(a);
        }

        o.Sort();

        FullRoute.WriteCreditHeaderString(w, "Internet AS Number List", string.Format("{0} AS numbers", o.Count));

        w.WriteLine("#ASNumber,Country,VirtualIP,TotalIPv4Addr,TotalIPv6Prefix(/64),ISPName");
        w.WriteLine("#VirtualIP = 127.A.B.C (A, B and C is the 3-byte integer of the AS-number)");

        foreach (FullRouteAsNumber a in o)
        {
            string name_str = a.Name;
            if (Str.InStr(name_str, ","))
            {
                name_str = Str.ReplaceStr(name_str, "\"", "\"\"");
                name_str = "\"" + name_str + "\"";
            }

            w.WriteLine("AS{0},{1},{2},{3},{4},{5}", a.Number, a.Country2, a.VirtualIp, a.NumIPv4, a.NumIPv6, name_str);
        }

        return w.ToString();
    }

    public void Insert(FullRouteAsNumber n)
    {
        if (List.ContainsKey(n.Number) == false)
        {
            List.Add(n.Number, n);
        }
    }

    public FullRouteAsNumber? Lookup(int n)
    {
        if (List.ContainsKey(n))
        {
            return List[n];
        }

        return null;
    }

    public void InsertFromHtml(string body)
    {
        if (Str.InStr(body, "</html>") == false)
        {
            throw new ApplicationException("no </html>");
        }

        body = Str.NormalizeCrlf(body, CrlfStyle.CrLf);

        int pos = 0;
        while (true)
        {
            int i = body.IndexOf("\">AS", pos, StringComparison.OrdinalIgnoreCase);
            if (i == -1)
            {
                break;
            }
            pos = i + 4;

            int j = body.IndexOf("</a>", pos, StringComparison.OrdinalIgnoreCase);
            if (j == -1)
            {
                break;
            }
            pos = j + 4;

            string as_num_str = body.Substring(i + 4, j - i - 4).Trim();

            int k = body.IndexOf("<", pos, StringComparison.OrdinalIgnoreCase);
            if (k == -1)
            {
                k = body.IndexOf("\r", pos, StringComparison.OrdinalIgnoreCase);
                if (k == -1)
                {
                    break;
                }
            }
            pos = k + 1;

            string as_name_str = body.Substring(j + 4, k - j - 4);

            as_name_str = Str.ReplaceStr(as_name_str, "\r", "");
            as_name_str = Str.ReplaceStr(as_name_str, "\n", "");
            as_name_str = as_name_str.Trim();


            int as_num = Str.StrToInt(as_num_str.Trim());

            //Con.WriteLine("{0} - {1}", as_num_str, as_name_str);

            int m = 0;
            int o = -1;
            while (true)
            {
                m = as_name_str.IndexOf(",", m + 1);
                if (m == -1)
                {
                    break;
                }
                o = m;
            }

            string as_name = "";
            string as_country_2 = "ZZ";
            if (o == -1)
            {
                as_name = as_name_str;
            }
            else
            {
                as_name = as_name_str.Substring(0, o);
                as_country_2 = as_name_str.Substring(o + 1);
            }

            as_name = as_name.Trim();
            as_country_2 = as_country_2.Trim();

            if (Str.IsEmptyStr(as_name))
            {
                as_name = "AS" + as_num.ToString();
            }

            if (as_country_2.Length != 2)
            {
                as_country_2 = "ZZ";
            }

            if (as_num >= 1)
            {
                as_country_2 = as_country_2.ToUpperInvariant();

                Insert(new FullRouteAsNumber(as_num, as_name, as_country_2));
            }
        }
    }
}

public class FullRouteCountryEntry : IComparable<FullRouteCountryEntry>, IEquatable<FullRouteCountryEntry>
{
    public readonly string Country2;
    public readonly string CountryFull;
    public List<FullRouteAsNumber>? Stat_AsList = null;
    public List<FullRouteEntry>? Stat_IPv4List = null;
    public List<FullRouteEntry>? Stat_IPv6List = null;

    public ulong NumIPv4;
    public ulong NumIPv6;

    public FullRouteCountryEntry(string country2, string full)
    {
        this.Country2 = country2;
        this.CountryFull = full;
    }

    public FullRouteCountryEntry(Buf buf)
    {
        this.Country2 = buf.ReadAsciiStr();
        this.CountryFull = buf.ReadAsciiStr();
    }

    public void Dump(Buf buf)
    {
        buf.WriteAsciiStr(this.Country2);
        buf.WriteAsciiStr(this.CountryFull);
    }

    public int CompareTo(FullRouteCountryEntry? other)
    {
        return this.Country2.CompareTo(other!.Country2);
    }

    public bool Equals(FullRouteCountryEntry? other)
    {
        return this.Country2.Equals(other!.Country2, StringComparison.OrdinalIgnoreCase);
    }

    public override bool Equals(object? obj)
    {
        if (obj is FullRouteCountryEntry)
        {
            return this.Country2.Equals(((FullRouteCountryEntry)obj).Country2, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    public override int GetHashCode()
    {
        return this.Country2.GetHashCode();
    }

    public string VirtualIp
    {
        get
        {
            return "127.0." + ((int)((byte)this.Country2[0])).ToString() + "." + ((int)((byte)this.Country2[1])).ToString();
        }
    }

    public override string ToString()
    {
        return this.Country2;
    }
}

public class FullRouteCountryList
{
    public Dictionary<string, FullRouteCountryEntry> List = new Dictionary<string, FullRouteCountryEntry>();

    public FullRouteCountryList()
    {
    }

    public FullRouteCountryList(Buf buf)
    {
        // version
        int ver = (int)buf.ReadInt();
        // count
        int num = (int)buf.ReadInt();
        // entries
        int i;
        for (i = 0; i < num; i++)
        {
            FullRouteCountryEntry e = new FullRouteCountryEntry(buf);

            this.Insert(e);
        }
    }

    public FullRouteCountryEntry? Lookup(string country_code)
    {
        if (this.List.ContainsKey(country_code))
        {
            return this.List[country_code];
        }

        return null;
    }

    public void Insert(FullRouteCountryEntry e)
    {
        if (this.List.ContainsKey(e.Country2) == false)
        {
            this.List.Add(e.Country2, e);
        }
    }

    public void Dump(Buf buf)
    {
        // version
        buf.WriteInt(1);
        // count
        buf.WriteInt((uint)this.List.Count);
        // entries
        foreach (FullRouteCountryEntry e in this.List.Values)
        {
            e.Dump(buf);
        }
    }

    public string ToCsv()
    {
        StringWriter w = new StringWriter();

        List<FullRouteCountryEntry> tmp = new List<FullRouteCountryEntry>();

        foreach (FullRouteCountryEntry e in this.List.Values)
        {
            tmp.Add(e);
        }

        tmp.Sort();

        FullRoute.WriteCreditHeaderString(w, "ISO Country Code List", string.Format("{0} countires and regions", tmp.Count));

        w.WriteLine("#CountryCode,VirtualIP,TotalIPv4Addr,TotalIPv6Prefix(/64),CountryName");
        w.WriteLine("#VirtualIP = 127.0.A.B (A and B is the ASCII-code of the country code)");

        foreach (FullRouteCountryEntry e in tmp)
        {
            w.WriteLine("{0},{1},{2},{3},{4}", e.Country2, e.VirtualIp, e.NumIPv4, e.NumIPv6, e.CountryFull);
        }

        return w.ToString();
    }

    public static FullRouteCountryList FromCsv(string str)
    {
        StringReader r = new StringReader(str);

        FullRouteCountryList ret = new FullRouteCountryList();
        char[] sps =
        {
                ',',
            };

        while (true)
        {
            string? line = r.ReadLine();
            if (line == null)
            {
                break;
            }

            if (line.StartsWith("#") == false)
            {
                string[] tokens = line.Split(sps, StringSplitOptions.RemoveEmptyEntries);

                if (tokens.Length >= 3 && tokens[0].Length == 2 && Str.IsEmptyStr(tokens[2]) == false)
                {
                    ret.Insert(new FullRouteCountryEntry(tokens[0].ToUpperInvariant().Trim(), tokens[2].Trim()));
                }
            }
        }

        return ret;
    }

    public static FullRouteCountryList BuildFromLegacyIPInfo()
    {
        FullRouteCountryList ret = new FullRouteCountryList();

        string[] cclist = FullRouteIPInfo.GetCountryCodes()!;

        foreach (string cc in cclist)
        {
            string name = FullRouteIPInfo.SearchCountry(cc);

            if (Str.InStr(name, ","))
            {
                throw new ApplicationException("if (Str.InStr(name, \",\"))");
            }

            ret.Insert(new FullRouteCountryEntry(cc.ToUpperInvariant(), name));
        }

        return ret;
    }
}

public class FullRouteSetResult
{
    public string? IPAddress;
    public string? IPRouteNetwork;
    public int IPRouteSubnetLength;
    public int ASNumber;
    public int[]? AS_Path;
    public string? ASName;
    public string? CountryCode2;
    public string? CountryName;
    public AddressFamily AddressFamily;

    public string AS_PathString
    {
        get
        {
            string ret = "";
            foreach (int i in this.AS_Path!)
            {
                ret += i.ToString() + " ";
            }
            ret = ret.Trim();
            return ret;
        }
    }
}

public class FullRouteSet
{
    public readonly FullRouteCountryList CountryList;
    public readonly FullRouteAsList AsList;
    public readonly FullRoute IPv4;
    public readonly FullRoute IPv6;

    bool calc_stat_flag = false;

    public FullRouteSet(Buf buf)
    {
        CountryList = new FullRouteCountryList(buf);
        AsList = new FullRouteAsList(buf);
        IPv4 = new FullRoute(AddressFamily.InterNetwork, buf);
        IPv6 = new FullRoute(AddressFamily.InterNetworkV6, buf);
    }

    // 統計処理
    public void CalcStatistics()
    {
        if (calc_stat_flag == false)
        {
            calc_stat_flag = true;

            Con.WriteLine("CalcStatistics begin.");

            // 国ごとの AS
            foreach (FullRouteCountryEntry ce in this.CountryList.List.Values)
            {
                ce.Stat_AsList = new List<FullRouteAsNumber>();
            }
            foreach (FullRouteAsNumber asn in this.AsList.List.Values)
            {
                if (this.CountryList.List.ContainsKey(asn.Country2))
                {
                    this.CountryList.List[asn.Country2].Stat_AsList!.Add(asn);
                }
            }
            foreach (FullRouteCountryEntry ce in this.CountryList.List.Values)
            {
                ce.Stat_AsList!.Sort();
            }

            // 国ごとの IPv4 / IPv6 ルート
            foreach (FullRouteCountryEntry ce in this.CountryList.List.Values)
            {
                ce.Stat_IPv4List = new List<FullRouteEntry>();
                ce.Stat_IPv6List = new List<FullRouteEntry>();
            }
            Dictionary<string, int> unk = new Dictionary<string, int>();
            foreach (FullRouteEntry? fe in this.IPv4.Trie!.EnumAllObjects())
            {
                FullRouteAsNumber asn = this.AsList.Lookup(fe!.OriginAs)!;
                if (asn != null)
                {
                    if (this.CountryList.List.ContainsKey(asn.Country2))
                    {
                        this.CountryList.List[asn.Country2].Stat_IPv4List!.Add(fe);
                    }
                }
            }
            foreach (FullRouteEntry? fe in this.IPv6.Trie!.EnumAllObjects())
            {
                FullRouteAsNumber asn = this.AsList.Lookup(fe!.OriginAs)!;
                if (asn != null)
                {
                    if (this.CountryList.List.ContainsKey(asn.Country2))
                    {
                        this.CountryList.List[asn.Country2].Stat_IPv6List!.Add(fe);
                    }
                }
            }
            foreach (FullRouteCountryEntry ce in this.CountryList.List.Values)
            {
                ce.Stat_IPv4List!.Sort();
                ce.Stat_IPv6List!.Sort();
            }

            // ISP ごとの IPv4 / IPv6 ルート
            foreach (FullRouteAsNumber asn in this.AsList.List.Values)
            {
                asn.Stat_IPv4List = new List<FullRouteEntry>();
                asn.Stat_IPv6List = new List<FullRouteEntry>();
            }
            foreach (FullRouteEntry? fe in this.IPv4.Trie.EnumAllObjects())
            {
                FullRouteAsNumber? asn = this.AsList.Lookup(fe!.OriginAs);
                if (asn != null)
                {
                    asn.Stat_IPv4List!.Add(fe);
                }
            }
            foreach (FullRouteEntry? fe in this.IPv6.Trie.EnumAllObjects())
            {
                FullRouteAsNumber? asn = this.AsList.Lookup(fe!.OriginAs);
                if (asn != null)
                {
                    asn.Stat_IPv6List!.Add(fe);
                }
            }
            foreach (FullRouteAsNumber asn in this.AsList.List.Values)
            {
                asn.Stat_IPv4List!.Sort();
                asn.Stat_IPv6List!.Sort();
            }

            Con.WriteLine("CalcStatistics end.");
        }
    }

    public FullRouteSetResult? Lookup(string ip)
    {
        try
        {
            IPAddr addr = IPAddr.FromString(ip);

            FullRoute? r = null;

            if (addr.AddressFamily == AddressFamily.InterNetwork)
            {
                r = this.IPv4;
            }
            else if (addr.AddressFamily == AddressFamily.InterNetworkV6)
            {
                r = this.IPv6;
            }
            else
            {
                return null;
            }

            FullRouteEntry? fe = r.Lookup(addr);
            if (fe == null)
            {
                return null;
            }

            FullRouteSetResult ret = new FullRouteSetResult();

            ret.AddressFamily = addr.AddressFamily;

            ret.IPAddress = addr.ToString();
            ret.IPRouteNetwork = fe.Address.ToString();
            ret.IPRouteSubnetLength = fe.SubnetLength;
            ret.ASNumber = fe.OriginAs;
            ret.AS_Path = fe.AsPath;

            FullRouteAsNumber? asn = this.AsList.Lookup(fe.OriginAs);
            if (asn == null)
            {
                asn = FullRouteAsNumber.NewDummyAs(fe.OriginAs);
            }
            ret.ASName = asn.Name;

            ret.CountryCode2 = asn.Country2;

            FullRouteCountryEntry? ce = this.CountryList.Lookup(ret.CountryCode2);
            if (ce != null)
            {
                ret.CountryName = ce.CountryFull;
            }
            else
            {
                ret.CountryName = ret.CountryCode2;
            }

            return ret;
        }
        catch
        {
            return null;
        }
    }
}

public class FullRouteSetThread
{
    public static readonly string CacheFileName = Path.Combine(Env.TempDir, "fullrouteset_cache.dat");
    public static readonly string DefaultURL = "http://files.open.ad.jp/private/fullrouteset/fullrouteset.dat";
    const int interval_ok = 8 * 60 * 60 * 1000;
    const int interval_error = 10 * 1000;

    bool calc_statistics = false;

    string url = "";
    ThreadObj? main_thread = null;
    Event? halt_event = null;
    volatile bool halt_flag = false;
    object lock_obj = new object();
    Event? ready_event = null;

    FullRouteSet? current_fs = null;

    public FullRouteSetThread(bool calcStatistics)
    {
        init(DefaultURL, calcStatistics);
    }
    public FullRouteSetThread(string url, bool calcStatistics)
    {
        init(url, calcStatistics);
    }

    public FullRouteSet? FullRouteSet
    {
        get
        {
            return current_fs;
        }
    }

    public FullRouteSetResult? Lookup(string ip)
    {
        try
        {
            if (current_fs == null)
            {
                return null;
            }

            return current_fs.Lookup(ip);
        }
        catch
        {
            return null;
        }
    }

    void fetch_main()
    {
        Con.WriteLine("fetch_main");

        FullRouteSet? fs = null;

        try
        {
            // download
            DnHttpClient hc = new DnHttpClient();
            Buf buf = hc.Get(new Uri(url));

            fs = new FullRouteSet(Buf.ReadFromBufWithHash(buf));

            // OK の場合キャッシュを更新
            buf.WriteToFile(CacheFileName);

            Con.WriteLine("OK: read from URL");
        }
        catch (Exception ex)
        {
            Con.WriteLine(ex.ToString());

            // ダウンロード失敗の場合キャッシュを読み込む
            fs = new FullRouteSet(Buf.ReadFromFileWithHash(CacheFileName));

            Con.WriteLine("OK: read from cache");
        }

        if (this.calc_statistics)
        {
            fs.CalcStatistics();
        }

        this.current_fs = fs;

        ready_event!.Set();
    }

    void init(string url, bool calcStatistics)
    {
        this.calc_statistics = calcStatistics;

        this.url = url;

        IO.MakeDirIfNotExists(Path.GetDirectoryName(CacheFileName)!);

        halt_event = new Event();
        halt_flag = false;

        ready_event = new Event();

        main_thread = new ThreadObj(loop_thread);
    }

    public void Stop()
    {
        lock (lock_obj)
        {
            if (main_thread != null)
            {
                halt_flag = true;
                halt_event!.Set();

                main_thread.WaitForEnd();

                main_thread = null;
            }
        }
    }

    void loop_thread(object? param)
    {
        while (halt_flag == false)
        {
            int next_interval = interval_ok;
            try
            {
                fetch_main();
            }
            catch (Exception ex)
            {
                Con.WriteLine(ex.ToString());
                next_interval = interval_error;
            }

            if (halt_flag)
            {
                break;
            }

            halt_event!.Wait(next_interval);
        }

        Con.WriteLine("Halt.");
    }

    public bool WaitForReady(int timeout)
    {
        return ready_event!.Wait(timeout);
    }
}

public class FullRouteCompiler
{
    public readonly string BaseDir = @"C:\tmp\FullRouteCompiler";
    public readonly string MasterDir;
    public readonly string OutputDir;
    public readonly string LogDir;
    public readonly string TmpDir;
    public readonly string FullRouteURL_IPv4 = "http://fullroute1.v4.open.ad.jp/ipv4_fullroute.txt.gz";
    public readonly string FullRouteURL_IPv6 = "http://fullroute1.v4.open.ad.jp/ipv6_fullroute.txt.gz";
    public readonly string AsURL = "http://bgp.potaroo.net/cidr/autnums.html";
    public readonly string AsTmpData;

    public FullRouteCompiler(string baseDir)
    {
        if (Str.IsEmptyStr(baseDir) == false)
        {
            this.BaseDir = baseDir;
        }

        this.MasterDir = Path.Combine(BaseDir, "master");
        this.OutputDir = Path.Combine(BaseDir, "output");
        this.LogDir = Path.Combine(BaseDir, "log");
        this.TmpDir = Path.Combine(BaseDir, "tmp");
        this.AsTmpData = Path.Combine(TmpDir, "autnums_cache.dat");
    }

    void make_dirs()
    {
        IO.MakeDirIfNotExists(BaseDir);
        IO.MakeDirIfNotExists(MasterDir);
        IO.MakeDirIfNotExists(OutputDir);
        IO.MakeDirIfNotExists(TmpDir);
        IO.MakeDirIfNotExists(LogDir);
    }

    public void Generate()
    {
        make_dirs();

        FileLogger log = new FileLogger(LogDir);

        try
        {
            // 国データの取得
            Con.WriteLine("Country List");
            FullRouteCountryList country_list = FullRouteCountryList.FromCsv(Str.ReadTextFile(Path.Combine(MasterDir, "CountryList.csv")));

            // AS データのダウンロード
            Con.WriteLine("AS List");
            FullRouteAsList? as_list = null;

            try
            {
                as_list = LoadAsListFromUrl(AsURL);

                // 検査
                if (Str.StrCmpi(as_list.Lookup(59103)!.Country2, "jp") == false)
                {
                    throw new ApplicationException("Invalid AS numbers list HTML");
                }

                Buf buf = new Buf();
                as_list.Dump(buf);
                buf.WriteToFileWithHash(AsTmpData);
            }
            catch (Exception ex)
            {
                log.Write("AS Download error: " + ex.ToString());
                Buf buf = Buf.ReadFromFileWithHash(AsTmpData);
                as_list = new FullRouteAsList(buf);
            }

            // フルルートのダウンロード
            Con.WriteLine("IPv4");
            FullRoute ipv4 = LoadFullRouteFromUrl(FullRouteURL_IPv4, AddressFamily.InterNetwork);
            Con.WriteLine("IPv6");
            FullRoute ipv6 = LoadFullRouteFromUrl(FullRouteURL_IPv6, AddressFamily.InterNetworkV6);

            Con.WriteLine("country={0} ipv4={1} ipv6={2} as={3}", country_list.List.Count, ipv4.Trie!.Count, ipv6.Trie!.Count, as_list.List.Count);

            if (country_list.List.Count < 200)
            {
                throw new ApplicationException("country_list.List.Count < 200");
            }

            if (ipv4.Trie.Count < 500000)
            {
                throw new ApplicationException("ipv4.Trie.Count < 500000");
            }

            if (ipv6.Trie.Count < 15000)
            {
                throw new ApplicationException("ipv6.Trie.Count < 15000");
            }

            if (as_list.List.Count < 60000)
            {
                throw new ApplicationException("as_list.List.Count < 60000");
            }

            // スペースのコンパイル
            Con.WriteLine("Compiling the space...");
            FullRouteSpace fs_ipv4_as = new FullRouteSpace(ipv4, country_list, as_list, FullRouteSpaceType.ByAS);
            FullRouteSpace fs_ipv4_country = new FullRouteSpace(ipv4, country_list, as_list, FullRouteSpaceType.ByCountry);
            FullRouteSpace fs_ipv6_as = new FullRouteSpace(ipv6, country_list, as_list, FullRouteSpaceType.ByAS);
            FullRouteSpace fs_ipv6_country = new FullRouteSpace(ipv6, country_list, as_list, FullRouteSpaceType.ByCountry);

            // セットデータの書き込み
            Con.WriteLine("Writing the set data...");
            Buf b = new Buf();
            country_list.Dump(b);
            as_list.Dump(b);
            ipv4.Dump(b);
            ipv6.Dump(b);
            b.WriteToFileWithHash(Path.Combine(OutputDir, "fullrouteset.dat"));

            // フルルートの書き込み
            Con.WriteLine("Writing the IPv4 full route data...");
            byte[] ipv4_data;
            ipv4_data = Str.AsciiEncoding.GetBytes(ipv4.ToCsv());
            IO.SaveFile(Path.Combine(OutputDir, "fullroute_ipv4.csv"), ipv4_data);
            IO.SaveFile(Path.Combine(OutputDir, "fullroute_ipv4.csv.gz"), GZipUtil.Compress(ipv4_data));

            Con.WriteLine("Writing the IPv6 full route data...");
            byte[] ipv6_data;
            ipv6_data = Str.AsciiEncoding.GetBytes(ipv6.ToCsv());
            IO.SaveFile(Path.Combine(OutputDir, "fullroute_ipv6.csv"), ipv6_data);
            IO.SaveFile(Path.Combine(OutputDir, "fullroute_ipv6.csv.gz"), GZipUtil.Compress(ipv6_data));

            // マッピングファイルの書き込み
            Con.WriteLine("Writing the IPv4 mapping files...");
            byte[] fs_ipv4_as_data = Str.AsciiEncoding.GetBytes(fs_ipv4_as.ToCsv());
            IO.SaveFile(Path.Combine(OutputDir, "mapping_ipv4_to_isp.csv"), fs_ipv4_as_data);
            IO.SaveFile(Path.Combine(OutputDir, "mapping_ipv4_to_isp.csv.gz"), GZipUtil.Compress(fs_ipv4_as_data));
            byte[] fs_ipv4_country_data = Str.AsciiEncoding.GetBytes(fs_ipv4_country.ToCsv());
            IO.SaveFile(Path.Combine(OutputDir, "mapping_ipv4_to_country.csv"), fs_ipv4_country_data);
            IO.SaveFile(Path.Combine(OutputDir, "mapping_ipv4_to_country.csv.gz"), GZipUtil.Compress(fs_ipv4_country_data));

            Con.WriteLine("Writing the IPv6 mapping files...");
            byte[] fs_ipv6_as_data = Str.AsciiEncoding.GetBytes(fs_ipv6_as.ToCsv());
            IO.SaveFile(Path.Combine(OutputDir, "mapping_ipv6_to_isp.csv"), fs_ipv6_as_data);
            IO.SaveFile(Path.Combine(OutputDir, "mapping_ipv6_to_isp.csv.gz"), GZipUtil.Compress(fs_ipv6_as_data));
            byte[] fs_ipv6_country_data = Str.AsciiEncoding.GetBytes(fs_ipv6_country.ToCsv());
            IO.SaveFile(Path.Combine(OutputDir, "mapping_ipv6_to_country.csv"), fs_ipv6_country_data);
            IO.SaveFile(Path.Combine(OutputDir, "mapping_ipv6_to_country.csv.gz"), GZipUtil.Compress(fs_ipv6_country_data));

            // 国ごとの IP アドレスリストを生成
            Con.WriteLine("Writing the IP address filter list by country list...");
            fs_ipv4_country.SaveIPListCsv(Path.Combine(this.OutputDir, @"ip_prefix_list\ipv4_country"), Path.Combine(this.OutputDir, "ipv4_prefix_list_to_country.csv"));
            fs_ipv6_country.SaveIPListCsv(Path.Combine(this.OutputDir, @"ip_prefix_list\ipv6_country"), Path.Combine(this.OutputDir, "ipv6_prefix_list_to_country.csv"));

            // AS ごとの IP アドレスリストを生成
            Con.WriteLine("Writing the IP address filter list by AS list...");
            fs_ipv4_as.SaveIPListCsv(Path.Combine(this.OutputDir, @"ip_prefix_list\ipv4_isp"), Path.Combine(this.OutputDir, "ipv4_prefix_list_to_isp.csv"));
            fs_ipv6_as.SaveIPListCsv(Path.Combine(this.OutputDir, @"ip_prefix_list\ipv6_isp"), Path.Combine(this.OutputDir, "ipv6_prefix_list_to_isp.csv"));

            // 国データの書き込み
            Con.WriteLine("Writing the country data...");
            byte[] country_list_data;
            country_list_data = Str.AsciiEncoding.GetBytes(country_list.ToCsv());
            IO.SaveFile(Path.Combine(OutputDir, "country_list.csv"), country_list_data);
            IO.SaveFile(Path.Combine(OutputDir, "country_list.csv.gz"), GZipUtil.Compress(country_list_data));

            // AS データの書き込み
            Con.WriteLine("Writing the AS data...");
            byte[] as_list_data;
            as_list_data = Str.AsciiEncoding.GetBytes(as_list.ToCsv());
            IO.SaveFile(Path.Combine(OutputDir, "as_list.csv"), as_list_data);
            IO.SaveFile(Path.Combine(OutputDir, "as_list.csv.gz"), GZipUtil.Compress(as_list_data));

            log.Write("OK " + string.Format("country={0} ipv4={1} ipv6={2} as={3}", country_list.List.Count, ipv4.Trie.Count, ipv6.Trie.Count, as_list.List.Count));
        }
        catch (Exception ex)
        {
            log.Write(ex.ToString());
        }
        finally
        {
            log.Close();
        }
    }

    FullRouteAsList LoadAsListFromUrl(string url)
    {
        DnHttpClient hc = new DnHttpClient();
        Buf buf = hc.Get(new Uri(url));
        string body = Str.AsciiEncoding.GetString(buf.ByteData);

        FullRouteAsList as_list = new FullRouteAsList();
        as_list.InsertFromHtml(body);

        return as_list;
    }

    FullRoute LoadFullRouteFromUrl(string url, AddressFamily family)
    {
        DnHttpClient hc = new DnHttpClient();

        Buf buf = hc.Get(new Uri(url));
        byte[] data = GZipUtil.Decompress(buf.ByteData);
        string body = Str.AsciiEncoding.GetString(data);

        FullRoute f = new FullRoute(family);
        f.InsertFromBirdText(body);

        f.UpdateTrie();

        return f;
    }
}

public enum FullRouteSpaceType
{
    ByCountry,
    ByAS,
    ByTagString,
}

public class FullRouteSpaceEntry : IComparable<FullRouteSpaceEntry>
{
    public readonly IPAddr IPStart, IPEnd;
    public readonly object Value;

    public FullRouteSpaceEntry(IPAddr start, IPAddr end, object value)
    {
        this.IPStart = start;
        this.IPEnd = end;
        this.Value = value;
    }

    public int CompareTo(FullRouteSpaceEntry? other)
    {
        return this.IPStart.CompareTo(other!.IPStart);
    }
}

public class FullRouteIPFilterList
{
    public readonly FullRouteSpaceType Type;
    public object Key;
    public FullRouteEntry[] EntryList;

    public FullRouteIPFilterList(FullRouteSpaceType type, object key, FullRouteEntry[] entryList)
    {
        this.Type = type;
        this.Key = key;
        this.EntryList = entryList;
    }

    public string ToCsv()
    {
        StringWriter w = new StringWriter();

        if (Key is FullRouteAsNumber)
        {
            FullRouteAsNumber asn = (FullRouteAsNumber)Key;
            FullRoute.WriteCreditHeaderString(w, "IP Address Filter List for AS" + asn.Number.ToString() + " (" + asn.Name + ")", string.Format("{0} entries", this.EntryList.Length), true);
            w.WriteLine("# Plain list of IP addresses which are originated by AS" + asn.Number.ToString() + " (" + asn.Name + ")");
        }
        else if (Key is FullRouteCountryEntry)
        {
            FullRouteCountryEntry ce = (FullRouteCountryEntry)Key;
            FullRoute.WriteCreditHeaderString(w, "IP Address Filter List for IP addresses located in " + ce.Country2 + " (" + ce.CountryFull + ")", string.Format("{0} entries", this.EntryList.Length), true);
            w.WriteLine("# Plain list of IP addresses which are located in " + ce.Country2 + " (" + ce.CountryFull + ")");
        }

        foreach (FullRouteEntry fe in this.EntryList)
        {
            w.WriteLine(fe.Address.ToString() + "/" + fe.SubnetLength.ToString());
        }

        w.WriteLine();

        if (Key is FullRouteAsNumber)
        {
            FullRouteAsNumber asn = (FullRouteAsNumber)Key;
            w.WriteLine("# iptables rules list of IP addresses which are originated by " + asn.Number.ToString() + " (" + asn.Name + ")");
        }
        else if (Key is FullRouteCountryEntry)
        {
            FullRouteCountryEntry ce = (FullRouteCountryEntry)Key;
            w.WriteLine("# iptables rules list of IP addresses which are located in " + ce.Country2 + " (" + ce.CountryFull + ")");
        }

        foreach (FullRouteEntry fe in this.EntryList)
        {
            w.WriteLine("iptables -A INPUT -j DROP -s " + fe.Address.ToString() + "/" + fe.SubnetLength.ToString());
        }

        w.WriteLine();

        return w.ToString();
    }
}

public class FullRouteSpace
{
    public readonly FullRouteSpaceType Type;
    public readonly AddressFamily AddressFamily;
    public List<FullRouteSpaceEntry> List = new List<FullRouteSpaceEntry>();
    public Dictionary<string, List<FullRouteSpaceEntry>> HashList = new Dictionary<string, List<FullRouteSpaceEntry>>();
    public readonly FullRouteCountryList CountryList;
    public readonly FullRouteAsList AsList;

    public FullRouteSpace(FullRoute fullRoute, FullRouteCountryList countryList, FullRouteAsList asList, FullRouteSpaceType type)
    {
        this.Type = type;
        this.AddressFamily = fullRoute.AddressFamily;
        this.CountryList = countryList;
        this.AsList = asList;

        Dictionary<IPAddr, int> boundary_list = new Dictionary<IPAddr, int>();

        AddToBoundaryList(boundary_list, IPAddr.GetMinValue(fullRoute.AddressFamily));
        AddToBoundaryList(boundary_list, IPAddr.GetMaxValue(fullRoute.AddressFamily));

        ArrayList all_nodes = fullRoute.Trie!.EnumAllObjects();

        foreach (FullRouteEntry? e in all_nodes)
        {
            AddToBoundaryList(boundary_list, e!.Address);

            IPAddr a = e.Address;
            IPAddr? b = null;

            if (fullRoute.AddressFamily == AddressFamily.InterNetwork)
            {
                IPAddress mask = IPUtil.IntToSubnetMask4(e.SubnetLength);
                mask = IPUtil.IPNot(mask);

                BigNumber bi = a.GetBigNumber() + new IPv4Addr(mask).GetBigNumber() + 1;

                b = new IPv4Addr(IPAddr.PadBytes(FullRoute.BigNumberToByte(bi, AddressFamily.InterNetwork), 4));
            }
            else if (fullRoute.AddressFamily == AddressFamily.InterNetworkV6)
            {
                IPAddress mask = IPUtil.IntToSubnetMask6(e.SubnetLength);
                mask = IPUtil.IPNot(mask);

                BigNumber bi = a.GetBigNumber() + (new IPv6Addr(mask).GetBigNumber()) + 1;

                b = new IPv6Addr(IPAddr.PadBytes(FullRoute.BigNumberToByte(bi, AddressFamily.InterNetworkV6), 16));
            }
            else
            {
                throw new ApplicationException("invalid address family.");
            }

            AddToBoundaryList(boundary_list, b);
        }

        List<IPAddr> tmp2 = new List<IPAddr>();

        foreach (IPAddr a in boundary_list.Keys)
        {
            tmp2.Add(a);
        }

        tmp2.Sort();

        object? current_value = null;

        IPAddr? current_start = null;

        foreach (IPAddr a in tmp2)
        {
            FullRouteEntry? entry = fullRoute.Lookup(a);

            object? value = null;

            if (entry != null)
            {
                if (type == FullRouteSpaceType.ByAS)
                {
                    value = asList.Lookup(entry.OriginAs);
                    if (value == null)
                    {
                        value = FullRouteAsNumber.NewDummyAs(entry.OriginAs);
                    }
                }
                else if (type == FullRouteSpaceType.ByCountry)
                {
                    FullRouteAsNumber? asn = asList.Lookup(entry.OriginAs);
                    if (asn == null)
                    {
                        asn = FullRouteAsNumber.NewDummyAs(entry.OriginAs);
                    }
                    value = countryList.Lookup(asn.Country2);
                }
                else
                {
                    value = entry.TagString;
                }
            }

            if (CompareObj(current_value, value) == false)
            {
                if (current_value != null)
                {
                    IPAddr start = current_start!;
                    IPAddr end = a.Add(-1);

                    FullRouteSpaceEntry ent = new FullRouteSpaceEntry(start, end, current_value);

                    this.List.Add(ent);

                    string hash_key = ent.Value.ToString()._NonNull();
                    if (this.HashList.ContainsKey(hash_key) == false)
                    {
                        this.HashList.Add(hash_key, new List<FullRouteSpaceEntry>());
                    }
                    this.HashList[hash_key].Add(ent);
                }

                current_start = a;
                current_value = value;
            }
        }
    }

    public FullRouteIPFilterList[] GenerateIPFilterLists()
    {
        List<FullRouteIPFilterList> ret = new List<FullRouteIPFilterList>();
        List<object> keys = new List<object>();

        if (this.Type == FullRouteSpaceType.ByCountry)
        {
            foreach (FullRouteCountryEntry ce in this.CountryList.List.Values)
            {
                keys.Add(ce);
            }
        }
        else if (this.Type == FullRouteSpaceType.ByAS)
        {
            foreach (FullRouteAsNumber asn in this.AsList.List.Values)
            {
                keys.Add(asn);
            }
        }

        int n = 0;
        foreach (object key in keys)
        {
            string key_str = key.ToString()._NonNull();
            Con.WriteLine("GenerateIPFilterLists: {0}/{1} ({2})", n, keys.Count, key_str);
            FullRouteSpaceEntry[]? fse = null;
            if (this.HashList.ContainsKey(key_str))
            {
                fse = this.HashList[key_str].ToArray();
            }
            FullRouteIPFilterList o = new FullRouteIPFilterList(this.Type, key, SubnetGenerator.GenerateSubnets(fse!));
            ret.Add(o);
            n++;
        }

        return ret.ToArray();
    }

    public void SaveIPListCsv(string outputDir, string allTxtFile)
    {
        IO.MakeDirIfNotExists(outputDir);

        FullRouteIPFilterList[] ip_list = GenerateIPFilterLists();
        List<FullRouteEntry> all_list = new List<FullRouteEntry>();

        foreach (FullRouteIPFilterList p in ip_list)
        {
            string key2 = p.Key.ToString()._NonNull();
            if (p.Key is FullRouteAsNumber)
            {
                key2 = "AS" + key2;
            }
            string fn = Path.Combine(outputDir, key2 + ".txt");

            Con.WriteLine("Processing {0}, {1}, {2}", key2, this.AddressFamily.ToString(), this.Type.ToString());

            foreach (FullRouteEntry e in p.EntryList)
            {
                e.TmpObject = p.Key;
                all_list.Add(e);

                if (this.Type == FullRouteSpaceType.ByAS)
                {
                    FullRouteAsNumber asn = (FullRouteAsNumber)p.Key;
                    if (this.AddressFamily == AddressFamily.InterNetwork)
                    {
                        asn.NumIPv4 += e.NumIP;
                    }
                    else if (this.AddressFamily == AddressFamily.InterNetworkV6)
                    {
                        asn.NumIPv6 += e.NumIP;
                    }
                }
                else if (this.Type == FullRouteSpaceType.ByCountry)
                {
                    FullRouteCountryEntry ce = (FullRouteCountryEntry)p.Key;
                    if (this.AddressFamily == AddressFamily.InterNetwork)
                    {
                        ce.NumIPv4 += e.NumIP;
                    }
                    else if (this.AddressFamily == AddressFamily.InterNetworkV6)
                    {
                        ce.NumIPv6 += e.NumIP;
                    }
                }
            }

            byte[] data = Str.AsciiEncoding.GetBytes(p.ToCsv());
            IO.SaveFile(fn, data);
            IO.SaveFile(fn + ".gz", GZipUtil.Compress(data));
        }

        all_list.Sort();

        StringWriter w = new StringWriter();
        if (this.Type == FullRouteSpaceType.ByAS)
        {
            FullRoute.WriteCreditHeaderString(w, "IP Prefix to ISP Mapping Table", string.Format("{0} entries", all_list.Count));
            w.WriteLine("#Prefix/SubnetLength,ASNumber,ISPName");
        }
        else
        {
            FullRoute.WriteCreditHeaderString(w, "IP Prefix to Country Code Mapping Table", string.Format("{0} entries", all_list.Count));
            w.WriteLine("#Prefix/SubnetLength,CountryCode,CountryName");
        }

        foreach (FullRouteEntry e in all_list)
        {
            if (this.Type == FullRouteSpaceType.ByAS)
            {
                FullRouteAsNumber asn = (FullRouteAsNumber)e.TmpObject!;
                w.WriteLine("{0}/{1},AS{2},{3}", e.Address.ToString(), e.SubnetLength.ToString(), asn.Number.ToString(), asn.Name);
            }
            else
            {
                FullRouteCountryEntry ce = (FullRouteCountryEntry)e.TmpObject!;
                w.WriteLine("{0}/{1},{2},{3}", e.Address.ToString(), e.SubnetLength.ToString(), ce.Country2, ce.CountryFull);
            }
        }

        byte[] filedata = Str.AsciiEncoding.GetBytes(w.ToString());
        IO.SaveFile(allTxtFile, filedata);
        IO.SaveFile(allTxtFile + ".gz", GZipUtil.Compress(filedata));
    }

    public string ToCsv()
    {
        StringWriter w = new StringWriter();

        string ip_str = "IPv4";
        if (this.AddressFamily == AddressFamily.InterNetworkV6)
        {
            ip_str = "IPv6";
        }

        string v6str = "";

        if (this.AddressFamily == AddressFamily.InterNetworkV6)
        {
            v6str = "(/64prefix)";
        }

        if (this.Type == FullRouteSpaceType.ByAS)
        {
            FullRoute.WriteCreditHeaderString(w, ip_str + " Address to AS Number Mapping Table", this.List.Count + " entries");
            w.WriteLine("#Range_Start_IP,Range_End_IP,Range_Start_Decimal{0},Range_End_Decimal{0},NumberOfIPs{0},OriginAS,ISPName", v6str);
        }
        else
        {
            FullRoute.WriteCreditHeaderString(w, ip_str + " Address to Country Mapping Table", this.List.Count + " entries");
            w.WriteLine("#Range_Start_IP,Range_End_IP,Range_Start_Decimal{0},Range_End_Decimal{0},NumberOfIPs{0},CountryCode,CountryName", v6str);
        }

        BigNumber max64bit = new BigNumber(0xffffffffffffffffUL);
        max64bit += 1UL;

        foreach (FullRouteSpaceEntry e in this.List)
        {
            List<string> data = new List<string>();

            if (e.Value is FullRouteAsNumber)
            {
                FullRouteAsNumber asn = (FullRouteAsNumber)e.Value;

                data.Add("AS" + asn.Number.ToString());
                data.Add(asn.Country2);
                data.Add(asn.Name);
            }
            else if (e.Value is FullRouteCountryEntry)
            {
                FullRouteCountryEntry ce = (FullRouteCountryEntry)e.Value;

                data.Add(ce.Country2);
                data.Add(ce.CountryFull);
            }

            List<string> data2 = new List<string>();

            data2.Add(e.IPStart.ToString());
            data2.Add(e.IPEnd.ToString());

            if (this.AddressFamily == AddressFamily.InterNetworkV6)
            {
                data2.Add((e.IPStart.GetBigNumber() / max64bit).ToString());
                data2.Add((e.IPEnd.GetBigNumber() / max64bit).ToString());
                data2.Add(((e.IPEnd.GetBigNumber() - e.IPStart.GetBigNumber() + 1) / max64bit).ToString());
            }
            else
            {
                data2.Add(e.IPStart.GetBigNumber().ToString());
                data2.Add(e.IPEnd.GetBigNumber().ToString());
                data2.Add((e.IPEnd.GetBigNumber() - e.IPStart.GetBigNumber() + 1).ToString());
            }

            foreach (string s in data)
            {
                string s2 = s;

                if (Str.InStr(s2, ","))
                {
                    s2 = Str.ReplaceStr(s2, "\"", "\"\"");
                    s2 = "\"" + s2 + "\"";
                }

                data2.Add(s2);
            }

            string line = Str.CombineStringArray(data2.ToArray(), ",");

            w.WriteLine(line);
        }

        return w.ToString();
    }

    static bool CompareObj(object? obj1, object? obj2)
    {
        if (obj1 == null && obj2 == null)
        {
            return true;
        }

        if (obj1 == null || obj2 == null)
        {
            return false;
        }

        return obj1.Equals(obj2);
    }

    static void AddToBoundaryList(Dictionary<IPAddr, int> o, IPAddr a)
    {
        if (o.ContainsKey(a) == false)
        {
            o.Add(a, 0);
        }
    }
}

public static class SubnetGenerator
{
    private class Context
    {
        public int MaxDepth = 0;
        public List<string> MatchBitsList = new List<string>();

        public Context(int depth)
        {
            this.MaxDepth = depth;
        }
    }

    static string FillRemainBit(string currentBits, int maxDepth, int bit)
    {
        int len = currentBits.Length;

        if (len >= maxDepth)
        {
            return currentBits;
        }
        else
        {
            return currentBits + Str.MakeCharArray((bit == 0 ? '0' : '1'), (maxDepth - len));
        }
    }

    static void ProcBit(Context ctx, string currentBits, string bitsStart, string bitsEnd)
    {
        int bits_depth = currentBits.Length;

        if (currentBits == "0001110")
        {
            Util.NoOP();
        }

        if (bitsStart == bitsEnd)
        {
            Util.NoOP();
        }

        string f1 = currentBits + "0";
        string f2 = currentBits + "1";

        //			if (bits_start.StartsWith(f1) && bits_end.StartsWith(f2))
        if (FillRemainBit(currentBits, ctx.MaxDepth, 0) == bitsStart &&
            FillRemainBit(currentBits, ctx.MaxDepth, 1) == bitsEnd)
        {
            //Con.WriteLine(current_bits);
            ctx.MatchBitsList.Add(currentBits);
            return;
        }

        if (bitsStart[bits_depth] == '0')
        {
            string tmp = bitsEnd;
            string tmp2 = FillRemainBit(currentBits + "0", ctx.MaxDepth, 1);
            if (tmp.CompareTo(tmp2) > 0)
            {
                tmp = tmp2;
            }
            ProcBit(ctx, currentBits + "0", bitsStart, tmp);
        }
        if (bitsEnd[bits_depth] == '1')
        {
            string tmp = bitsStart;
            string tmp2 = FillRemainBit(currentBits + "1", ctx.MaxDepth, 0);
            if (tmp.CompareTo(tmp2) < 0)
            {
                tmp = tmp2;
            }
            ProcBit(ctx, currentBits + "1", tmp, bitsEnd);
        }
    }

    public static FullRouteEntry[] GenerateSubnets(FullRouteSpaceEntry[] fullSpaceEntries)
    {
        List<FullRouteEntry> ret = new List<FullRouteEntry>();

        if (fullSpaceEntries != null)
        {
            foreach (FullRouteSpaceEntry e in fullSpaceEntries)
            {
                FullRouteEntry[] gs = GenerateSubnets(e.IPStart, e.IPEnd);

                foreach (FullRouteEntry e2 in gs)
                {
                    ret.Add(e2);
                }
            }
        }

        return ret.ToArray();
    }

    public static FullRouteEntry[] GenerateSubnets(IPAddr ipStart, IPAddr ipEnd)
    {
        if (ipStart.AddressFamily != ipEnd.AddressFamily)
        {
            throw new ApplicationException("ip_start.AddressFamily != ip_end.AddressFamily");
        }

        Context ctx = new Context(ipStart.AddressFamily == AddressFamily.InterNetwork ? 32 : 128);

        string bits_start = ipStart.GetBinaryString();
        string bits_end = ipEnd.GetBinaryString();

        if (bits_start.CompareTo(bits_end) > 0)
        {
            bits_end = ipStart.GetBinaryString();
            bits_start = ipEnd.GetBinaryString();
        }

        ProcBit(ctx, "", bits_start, bits_end);

        List<FullRouteEntry> ret = new List<FullRouteEntry>();

        foreach (string str in ctx.MatchBitsList)
        {
            string str2 = FillRemainBit(str, ctx.MaxDepth, 0);

            IPAddr a = IPAddr.FromBinaryString(str2);

            ret.Add(new FullRouteEntry(a, str.Length, ""));
        }

        return ret.ToArray();
    }
}

public class FullRoute46<T>
{
    readonly FullRoute FullRoute4 = new FullRoute(AddressFamily.InterNetwork);
    readonly FullRoute FullRoute6 = new FullRoute(AddressFamily.InterNetworkV6);

    public int Count => FullRoute4.Count + FullRoute6.Count;

    public bool Insert(IPAddress network, int subnetLength, T data)
    {
        FullRoute table;

        switch (network.AddressFamily)
        {
            case AddressFamily.InterNetwork:
                table = this.FullRoute4;
                break;

            case AddressFamily.InterNetworkV6:
                table = this.FullRoute6;
                break;

            default:
                throw new CoresException("Invalid AddressFamily");
        }

        if (subnetLength < 0 || subnetLength > IPUtil.GetSubnetLenForAddressFamily(network.AddressFamily))
        {
            throw new CoresException($"Invalid subnetLength: {subnetLength}");
        }

        network = IPUtil.GetPrefixAddress(network, subnetLength);

        bool ret = table.Insert(new FullRouteEntry(IPAddr.FromAddress(network), subnetLength, "", data));

        return ret;
    }

    public T? Lookup(IPAddress target, [NotNullWhen(true)] out IPAddr? subnet, out int subnetLength)
    {
        FullRoute table;

        switch (target.AddressFamily)
        {
            case AddressFamily.InterNetwork:
                table = this.FullRoute4;
                break;

            case AddressFamily.InterNetworkV6:
                table = this.FullRoute6;
                break;

            default:
                throw new CoresException("Invalid AddressFamily");
        }

        var e = table.Lookup(IPAddr.FromAddress(target));

        if (e == null)
        {
            subnet = null;
            subnetLength = 0;
            return default;
        }

        subnet = e.Address;
        subnetLength = e.SubnetLength;

        return (T)e.TmpObject!;
    }
}

public class FullRoute
{
    public readonly AddressFamily AddressFamily;
    public Dictionary<FullRouteEntry, int> BulkList = new Dictionary<FullRouteEntry, int>();
    public FullRouteEntry? DefaultEntry = null;
    public RadixTrie? Trie = null;
    readonly bool is_readonly = false;
    public readonly int AddressSize = 0;

    public int Count => this.BulkList.Count;

    public FullRoute(AddressFamily addressFamily)
    {
        this.AddressFamily = addressFamily;
        this.AddressSize = IPAddr.GetAddressSizeFromAddressFamily(this.AddressFamily);
    }

    public FullRoute(AddressFamily addressFamily, Buf buf)
    {
        this.AddressFamily = addressFamily;
        this.AddressSize = IPAddr.GetAddressSizeFromAddressFamily(this.AddressFamily);

        this.is_readonly = true;

        // version
        int ver = (int)buf.ReadInt();
        if (ver != 1)
        {
            throw new ApplicationException("ver != 1");
        }

        // addr family
        int fam = (int)buf.ReadInt();
        if (fam != (int)addressFamily)
        {
            throw new ApplicationException("wrong address_family");
        }

        RadixNode root = new RadixNode(null);
        this.Trie = new RadixTrie(root);

        ReadDumpNode(buf, root);
    }

    public string ToCsv()
    {
        StringWriter w = new StringWriter();

        string ip_type = "IPv4";
        if (this.AddressFamily == AddressFamily.InterNetworkV6)
        {
            ip_type = "IPv6";
        }

        WriteCreditHeaderString(w, ip_type + " Full Route",
            this.Trie!.Count.ToString() + " routes");

        w.WriteLine("#NetworkAddress,SubnetLength,OriginAS,AS_PATH");

        ArrayList o = this.Trie.EnumAllObjects();

        o.Sort();

        foreach (FullRouteEntry? e in o)
        {
            w.WriteLine(e!.ToCsvLine());
        }

        return w.ToString();
    }

    void ReadDumpNode(Buf buf, RadixNode n)
    {
        int len = (int)buf.RawReadInt();
        n.Label = buf.Read((uint)len);
        int flag = (int)buf.RawReadInt();
        if (flag != 0)
        {
            FullRouteEntry e = new FullRouteEntry(buf, this.AddressSize);

            this.Trie!.Count++;

            n.Object = e;
        }

        int count = (int)buf.RawReadInt();
        int i;
        for (i = 0; i < count; i++)
        {
            RadixNode cn = new RadixNode(n);
            ReadDumpNode(buf, cn);

            n.SubNodes.Add(cn);
        }
    }

    void DumpNode(Buf buf, RadixNode n)
    {
        buf.RawWriteInt((uint)n.Label.Length);
        buf.Write(n.Label);
        FullRouteEntry? e = n.Object as FullRouteEntry;
        if (e == null)
        {
            buf.RawWriteInt(0);
        }
        else
        {
            buf.RawWriteInt(1);
            e.Dump(buf);
        }

        buf.RawWriteInt((uint)n.SubNodes.Count);
        foreach (RadixNode cn in n.SubNodes)
        {
            DumpNode(buf, cn);
        }
    }

    public void Dump(Buf buf)
    {
        UpdateTrie();

        // version
        buf.WriteInt(1);

        // addr family
        buf.WriteInt((uint)this.AddressFamily);

        DumpNode(buf, this.Trie!.Root);
    }

    public bool Insert(FullRouteEntry e)
    {
        if (is_readonly)
        {
            throw new ApplicationException("readonly");
        }

        if (this.BulkList.ContainsKey(e) == false)
        {
            this.BulkList.Add(e, 0);
        }
        else
        {
            return false;
        }

        if (e.SubnetLength == 0)
        {
            // サブネット長 0 はすべてを包含するため特別である
            this.DefaultEntry = e;
        }

        this.Trie = null;

        return true;
    }

    public void UpdateTrie()
    {
        if (is_readonly)
        {
            return;
        }

        if (this.Trie != null)
        {
            return;
        }

        List<FullRouteEntry> tmp = new List<FullRouteEntry>();

        foreach (FullRouteEntry e in this.BulkList.Keys)
        {
            if (e.SubnetLength >= 1)
            {
                tmp.Add(e);
            }
        }

        tmp.Sort(FullRouteEntry.CompareBySubnetLength);

        this.Trie = new RadixTrie();
        foreach (FullRouteEntry e in tmp)
        {
            RadixNode n = Trie.Insert(e.GetBinaryBytes())!;

            n.Object = e;
        }
    }

    public FullRouteEntry? Lookup(IPAddr addr)
    {
        if (addr.AddressFamily != this.AddressFamily)
        {
            throw new ApplicationException("addr.AddressFamily != this.AddressFamily");
        }

        UpdateTrie();

        byte[] key = addr.GetBinaryBytes();

        RadixNode? n = this.Trie!.Lookup(key);
        if (n == null)
        {
            if (this.DefaultEntry != null)
            {
                return this.DefaultEntry;
            }
            return null;
        }

        n = n.TraverseParentNonNull();
        if (n == null)
        {
            if (this.DefaultEntry != null)
            {
                return this.DefaultEntry;
            }
            return null;
        }

        FullRouteEntry? ret = (FullRouteEntry?)n.Object;
        if (ret == null)
        {
            return null;
        }
        /*
        if (addr.GetBinaryString().StartsWith(ret.GetBinaryString()) == false)
        {
            return null;
        }*/

        return ret;
    }

    public void InsertFromBirdText(string str)
    {
        StringReader r = new StringReader(str);
        string? current_ip_and_subnet = null;
        string tag = "BGP.as_path:";

        int num_insert = 0;

        while (true)
        {
            string? line = r.ReadLine();

            /*if (num_insert >= 10000)
            {
                break;
            }*/

            if (line == null)
            {
                break;
            }

            try
            {
                if (Str.IsEmptyStr(line) == false)
                {
                    if (line.StartsWith("#") == false)
                    {
                        if (line[0] == ' ' || line[0] == '\t')
                        {
                            // find as_path
                            if (current_ip_and_subnet != null)
                            {
                                int i = line.IndexOf(tag, StringComparison.OrdinalIgnoreCase);
                                if (i != -1)
                                {
                                    int j = i + tag.Length;

                                    string as_path_str = line.Substring(j).Trim();

                                    FullRouteEntry e = FullRouteEntry.Parse(current_ip_and_subnet, as_path_str, this.AddressFamily);
                                    current_ip_and_subnet = null;

                                    Insert(e);
                                    num_insert++;
                                }
                            }
                        }
                        else
                        {
                            // ip address
                            string[] tokens = line.Split(Str.standardSplitChars, StringSplitOptions.RemoveEmptyEntries);

                            if (tokens.Length >= 1)
                            {
                                current_ip_and_subnet = tokens[0];
                            }
                        }
                    }
                }
            }
            catch
            {
            }
        }
    }

    public static string GetSubnetMaskBinaryStr(int len, AddressFamily family)
    {
        int size = GetMaxSubnetSize(family);

        len = Math.Min(len, size);

        int padSize = size - len;

        return Str.MakeCharArray('1', len) + Str.MakeCharArray('0', padSize);
    }

    public static int GetMaxSubnetSize(AddressFamily family)
    {
        if (family == AddressFamily.InterNetwork)
        {
            return 32;
        }
        else if (family == AddressFamily.InterNetworkV6)
        {
            return 128;
        }
        else
        {
            throw new ApplicationException("family is not IP.");
        }
    }

    public static int[] ParseAsPath(string str)
    {
        string[] tokens = str.Split(Str.standardSplitChars, StringSplitOptions.RemoveEmptyEntries);

        List<int> ret = new List<int>();

        foreach (string s in tokens)
        {
            ret.Add(Str.StrToInt(s));
        }

        return ret.ToArray();
    }

    public static void WriteCreditHeaderString(StringWriter w, string title, string summary)
    {
        WriteCreditHeaderString(w, title, summary, false);
    }
    public static void WriteCreditHeaderString(StringWriter w, string title, string summary, bool noSoftEtherCredit)
    {
        w.WriteLine("# {0}" + (noSoftEtherCredit ? "" : " - by open.ad.jp AS59103"), title);
        w.WriteLine("# Generated on " + DateTime.Now.ToString("s").Replace("T", " "));
        w.WriteLine("# ");
        w.WriteLine("# {0}", summary);
        //			w.WriteLine("# ");
        //			w.WriteLine("# THIS DATABASE IS PROVIDED \"AS IS\" AND ANY EXPRESS OR IMPLIED WARRANTIES, \r\n# INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY \r\n# AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL \r\n# THE DISTRIBUTOR BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, \r\n# EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, \r\n# PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR \r\n# PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF \r\n# LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING \r\n# NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS \r\n# DATABASE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.");
        w.WriteLine();
    }

    public static BigNumber ByteToBigNumber(byte[] data, AddressFamily af)
    {
        if (af == AddressFamily.InterNetwork)
        {
            if (data.Length != 4)
            {
                throw new ApplicationException("data.Length != 4");
            }

            uint a1 = BitConverter.ToUInt32(Util.EndianRetByte(data), 0);
            long b1 = (long)((ulong)a1);

            return new BigNumber(b1);
        }
        else if (af == AddressFamily.InterNetworkV6)
        {
            if (data.Length != 16)
            {
                throw new ApplicationException("data.Length != 16");
            }

            uint a1 = BitConverter.ToUInt32(Util.EndianRetByte(data), 12);
            uint a2 = BitConverter.ToUInt32(Util.EndianRetByte(data), 8);
            uint a3 = BitConverter.ToUInt32(Util.EndianRetByte(data), 4);
            uint a4 = BitConverter.ToUInt32(Util.EndianRetByte(data), 0);

            long b1 = (long)((ulong)a1);
            long b2 = (long)((ulong)a2);
            long b3 = (long)((ulong)a3);
            long b4 = (long)((ulong)a4);

            BigNumber c1 = new BigNumber(b1);
            c1 *= 0x100000000UL;
            c1 *= 0x100000000UL;
            c1 *= 0x100000000UL;

            BigNumber c2 = new BigNumber(b2);
            c2 *= 0x100000000UL;
            c2 *= 0x100000000UL;

            BigNumber c3 = new BigNumber(b3);
            c3 *= 0x100000000UL;

            BigNumber c4 = new BigNumber(b4);

            return c1 + c2 + c3 + c4;
        }
        else
        {
            throw new ApplicationException("invalid AddressFamily");
        }
    }

    public static byte[] BigNumberToByte(BigNumber bi, AddressFamily af)
    {
        if (af == AddressFamily.InterNetwork)
        {
            long b1 = bi.LongValue();
            uint a1 = (uint)((ulong)b1);

            return Util.EndianRetByte(BitConverter.GetBytes(a1));
        }
        else if (af == AddressFamily.InterNetworkV6)
        {
            BigNumber c4 = bi % 0x100000000UL;
            BigNumber c3 = (bi / 0x100000000UL) % 0x100000000UL;
            BigNumber c2 = (bi / 0x100000000UL / 0x100000000UL) % 0x100000000UL;
            BigNumber c1 = (bi / 0x100000000UL / 0x100000000UL / 0x100000000UL) % 0x100000000UL;

            long b1 = c1.LongValue();
            uint a1 = (uint)((ulong)b1);

            long b2 = c2.LongValue();
            uint a2 = (uint)((ulong)b2);

            long b3 = c3.LongValue();
            uint a3 = (uint)((ulong)b3);

            long b4 = c4.LongValue();
            uint a4 = (uint)((ulong)b4);

            byte[] ret = new byte[16];
            Util.CopyByte(ret, 0, Util.EndianRetByte(BitConverter.GetBytes(a1)));
            Util.CopyByte(ret, 4, Util.EndianRetByte(BitConverter.GetBytes(a2)));
            Util.CopyByte(ret, 8, Util.EndianRetByte(BitConverter.GetBytes(a3)));
            Util.CopyByte(ret, 12, Util.EndianRetByte(BitConverter.GetBytes(a4)));
            return ret;
        }
        else
        {
            throw new ApplicationException("invalid AddressFamily");
        }
    }

    public static void Test()
    {
        /*CountryList clist = CountryList.BuildFromLegacyIPInfo();
        Buf cbuf = new Buf();
        clist.Dump(cbuf);
        cbuf.WriteToFileWithHash(@"C:\TMP\141207fullroute\dump_clist.dat");*/

        FullRouteCountryList clist = new FullRouteCountryList(Buf.ReadFromFileWithHash(@"C:\tmp\141207fullroute\dump_clist.dat"));
        //Str.WriteTextFile(@"C:\tmp\141207fullroute\country_list2.csv", clist2.ToCsv(), Str.AsciiEncoding, false);

        FullRouteAsList aslist = new FullRouteAsList();
        aslist.InsertFromHtml(Str.ReadTextFile(@"C:\TMP\141207fullroute\as.txt", Str.AsciiEncoding));
        Str.WriteTextFile(@"C:\tmp\141207fullroute\as_list.csv", aslist.ToCsv(), Str.AsciiEncoding, false);

        Buf asbuf = new Buf();
        aslist.Dump(asbuf);
        asbuf.WriteToFileWithHash(@"C:\tmp\141207fullroute\dump_aslist.dat");
        FullRouteAsList aslist2 = new FullRouteAsList(Buf.ReadFromFileWithHash(@"C:\tmp\141207fullroute\dump_aslist.dat"));
        Str.WriteTextFile(@"C:\tmp\141207fullroute\as_list2.csv", aslist2.ToCsv(), Str.AsciiEncoding, false);


        long t1 = Time.Tick64;
        FullRoute r = new FullRoute(AddressFamily.InterNetwork);

        if (false)
        {
            //			string body = Str.ReadTextFile(@"C:\TMP\141207fullroute\ipv6_fullroute.txt", Str.AsciiEncoding);
            string body = Str.ReadTextFile(@"C:\TMP\141207fullroute\ipv4_fullroute.txt", Str.AsciiEncoding);
            //			string body = Str.ReadTextFile(@"C:\TMP\141207fullroute\test.txt", Str.AsciiEncoding);

            r.InsertFromBirdText(body);

            r.UpdateTrie();

            long t2 = Time.Tick64;
            Con.WriteLine("Load from BIRD: Took time: {0}", Str.ToString3(t2 - t1));

            Buf rbuf = new Buf();
            r.Dump(rbuf);

            rbuf.WriteToFileWithHash(@"C:\tmp\141207fullroute\dump_fullroute_ipv4.dat");

            long t3 = Time.Tick64;
            Con.WriteLine("Save dump: Took time: {0}", Str.ToString3(t3 - t2));
            if (true)
            {
                Str.WriteTextFile(@"C:\tmp\141207fullroute\fullroute_ipv4_2.csv", r.ToCsv(),
                    Str.AsciiEncoding, false);
                long t4 = Time.Tick64;

                Con.WriteLine("Save CSV: Took time: {0}", Str.ToString3(t4 - t3));
            }
        }
        else
        {
            Buf rbuf = Buf.ReadFromFileWithHash(@"C:\tmp\141207fullroute\dump_fullroute_ipv4.dat");

            r = new FullRoute(AddressFamily.InterNetwork, rbuf);

            long t2 = Time.Tick64;
            Con.WriteLine("Load from dump: Took time: {0}", Str.ToString3(t2 - t1));

            if (false)
            {
                Str.WriteTextFile(@"C:\tmp\141207fullroute\fullroute_ipv4.csv", r.ToCsv(),
                    Str.AsciiEncoding, false);
                long t3 = Time.Tick64;

                Con.WriteLine("Save CSV: Took time: {0}", Str.ToString3(t3 - t2));
            }
        }

        if (false)
        {
            FullRouteSpace sp = new FullRouteSpace(r, clist, aslist, FullRouteSpaceType.ByCountry);

            Str.WriteTextFile(@"C:\tmp\141207fullroute\space_by_country.csv", sp.ToCsv(), Str.AsciiEncoding, false);

            FullRouteSpace sp2 = new FullRouteSpace(r, clist, aslist, FullRouteSpaceType.ByAS);

            Str.WriteTextFile(@"C:\tmp\141207fullroute\space_by_as.csv", sp2.ToCsv(), Str.AsciiEncoding, false);
        }

        while (true)
        {
            string? ip = Con.ReadLine("IP>");

            if (Str.IsNumber(ip))
            {
                int i;
                int num = Str.StrToInt(ip);

                List<IPAddr> o = new List<IPAddr>();

                for (i = 0; i < num; i++)
                {
                    o.Add(IPAddr.FromString(string.Format("{0}.{1}.{2}.{3}", Util.RandUInt32() % 256, Util.RandUInt32() % 256, Util.RandUInt32() % 256, Util.RandUInt32() % 256)));
                }

                long start = Time.Tick64;

                foreach (IPAddr a in o)
                {
                    r.Lookup(a);
                }

                long end = Time.Tick64;

                Con.WriteLine("New: {0} msecs", Str.ToString3(end - start));

                start = Time.Tick64;

                foreach (IPAddr a in o)
                {
                    FullRouteIPInfo.Search(a.GetIPAddress());
                }

                end = Time.Tick64;

                Con.WriteLine("Old: {0} msecs", Str.ToString3(end - start));

            }

            try
            {
                IPAddr target = IPAddr.FromString(ip!);
                FullRouteEntry? e = r.Lookup(target);
                Con.WriteLine(e!.ToString());

                Con.WriteLine("  " + target.GetBinaryString());
                Con.WriteLine("  " + e.GetBinaryString());
            }
            catch (Exception ex)
            {
                Con.WriteLine(ex.ToString());
            }
        }
    }
}
