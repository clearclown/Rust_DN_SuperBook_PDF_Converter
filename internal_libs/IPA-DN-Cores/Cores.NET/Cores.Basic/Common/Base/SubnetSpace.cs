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
using System.Linq;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

namespace IPA.Cores.Basic;

public class SubnetSpaceSubnet<T> where T : class
{
    public IPAddr? Address = null!;
    public int SubnetLength;
    public List<T>? DataList = null!;

    public T? GetDataFirst() => this.DataList?._GetFirstOrNull();

    internal List<(int sortKey, T data)> TmpSortList = new List<(int sortKey, T data)>();

    int hash_code;

    public SubnetSpaceSubnet() { }

    public SubnetSpaceSubnet(IPAddr address, int subnetLen, T data) : this(address, subnetLen, new T[] { data }.ToList()) { }

    public SubnetSpaceSubnet(IPAddr address, int subnetLen, List<T>? dataList = null)
    {
        this.Address = address;
        this.SubnetLength = subnetLen;
        this.DataList = dataList;

        if (this.DataList == null)
        {
            this.DataList = new List<T>();
        }

        if (this.SubnetLength > FullRoute.GetMaxSubnetSize(Address.AddressFamily))
        {
            throw new ApplicationException("subnet_len is too large.");
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
    }

    public ulong CalcNumIPs()
    {
        if (Address == null) return 0;

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

    public override string ToString() => this.Address!.ToString() + "/" + this.SubnetLength.ToString();

    public override int GetHashCode() => hash_code;

    public override bool Equals(object? obj)
    {
        SubnetSpaceSubnet<T>? other = obj as SubnetSpaceSubnet<T>;
        if (other == null) return false;
        if (this.Address == null) return false;
        if (other.Address == null) return false;
        return this.Address.Equals(other.Address) && (this.SubnetLength == other.SubnetLength);
    }

    public string GetBinaryString()
        => this.Address!.GetBinaryString().Substring(0, this.SubnetLength);

    public byte[] GetBinaryBytes()
        => Util.CopyByte(this.Address!.GetBinaryBytes(), 0, this.SubnetLength);

    public bool Contains(IPAddr target)
    {
        if (this.Address == null) return false;
        if (this.Address.AddressFamily != target.AddressFamily) return false;

        string target_str = target.GetBinaryString();
        string subnet_str = this.GetBinaryString();

        return target_str.StartsWith(subnet_str);
    }

    public static int CompareBySubnetLength(SubnetSpaceSubnet<T> x, SubnetSpaceSubnet<T> y)
    {
        return x.SubnetLength.CompareTo(y.SubnetLength);
    }
}

public class SubnetSpace<T> where T : class
{
    public AddressFamily AddressFamily;
    public RadixTrie<SubnetSpaceSubnet<T>>? Trie;
    bool IsReadOnly = false;

    public SubnetSpace()
    {
        IsReadOnly = true;
    }

    public SubnetSpace(AddressFamily addressFamily)
    {
        IsReadOnly = false;
        this.AddressFamily = addressFamily;
    }

    // バッチ処理のためサブネット情報を追加する
    List<(IPAddress address, int subnetLength, T data, int dataSortKey)> BatchAddList = new List<(IPAddress address, int subnetLength, T data, int dataSortKey)>();
    public void BatchAddOne(IPAddress address, int subnetLength, T data, int dataSortKey)
    {
        if (IsReadOnly) throw new ApplicationException("The SubnetSpace object is readonly.");

        BatchAddList.Add((address, subnetLength, data, dataSortKey));
    }

    public void BatchAddFinish()
    {
        if (IsReadOnly) throw new ApplicationException("The SubnetSpace object is readonly.");

        SetData(BatchAddList.ToArray());
        BatchAddList.Clear();
    }

    // サブネット情報を投入する
    public void SetData((IPAddress address, int subnetLength, T data, int dataSortKey)[] items)
    {
        if (IsReadOnly) throw new ApplicationException("The SubnetSpace object is readonly.");

        // 重複するものを 1 つにまとめる
        Distinct<SubnetSpaceSubnet<T>> distinct = new Distinct<SubnetSpaceSubnet<T>>();

        foreach (var item in items)
        {
            SubnetSpaceSubnet<T> s = new SubnetSpaceSubnet<T>(IPAddr.FromAddress(item.address), item.subnetLength);

            s = distinct.AddOrGet(s);

            s.TmpSortList.Add((item.subnetLength, item.data));
        }

        SubnetSpaceSubnet<T>[] subnets = distinct.Values;

        foreach (SubnetSpaceSubnet<T> subnet in subnets)
        {
            // tmp_sort_list の内容を sort_key に基づき逆ソートする
            subnet.TmpSortList.Sort((a, b) =>
            {
                return -a.sortKey.CompareTo(b.sortKey);
            });

            // ソート済みオブジェクトを順に保存する
            subnet.DataList = new List<T>();
            foreach (var a in subnet.TmpSortList)
            {
                subnet.DataList.Add(a.data);
            }
        }

        List<SubnetSpaceSubnet<T>> subnetsList = subnets.ToList();

        subnetsList.Sort(SubnetSpaceSubnet<T>.CompareBySubnetLength);

        var trie = new RadixTrie<SubnetSpaceSubnet<T>>();

        foreach (var subnet in subnetsList)
        {
            RadixNode<SubnetSpaceSubnet<T>>? node = trie.Insert(subnet.GetBinaryBytes());

            if (node != null)
            {
                node.Object = subnet;
            }
        }

        this.Trie = trie;

        IsReadOnly = true;
    }

    // 検索をする
    public SubnetSpaceSubnet<T>? Lookup(IPAddress address)
    {
        if (address.AddressFamily != this.AddressFamily)
        {
            throw new ApplicationException("addr.AddressFamily != this.AddressFamily");
        }

        RadixTrie<SubnetSpaceSubnet<T>>? trie = this.Trie;

        if (trie == null)
        {
            return null;
        }

        byte[] key = IPAddr.FromAddress(address).GetBinaryBytes();
        RadixNode<SubnetSpaceSubnet<T>>? n = trie.Lookup(key);
        if (n == null)
        {
            return null;
        }

        n = n.TraverseParentNonNull();
        if (n == null)
        {
            return null;
        }

        return n.Object;
    }
}

