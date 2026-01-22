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

#if CORES_BASIC_SECURITY

using System;
using System.Collections.Generic;
using System.Net;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

namespace IPA.Cores.Basic.Legacy;

// WPC クライアント情報
public class WpcClient
{
    public string IpAddress { get; private set; }
    public string HostName { get; private set; }
    public string Guid { get; private set; }
    public int SeLang { get; private set; } = 0;
    public bool IsUser { get; private set; } = false;
    public string SvcName { get; private set; } = "";
    public string Msid { get; private set; } = "";
    public string? Pcid { get; private set; }
    public int MachineId { get; private set; }
    public DateTime CreateDate { get; private set; }
    public DateTime UpdateDate { get; private set; }
    public DateTime LastServerDate { get; private set; }
    public DateTime LastClientDate { get; private set; }
    public int NumServer { get; private set; }
    public int NumClient { get; private set; }

    public void SetUserInfo(int machineId, string svcName, string msid, string pcid, DateTime createDate, DateTime updateDate, DateTime lastServerDate,
        DateTime lastClientDate, int numServer, int numClient, int seLang)
    {
        this.IsUser = true;
        this.MachineId = machineId;
        this.SvcName = svcName;
        this.Msid = msid;
        this.Pcid = pcid;
        this.CreateDate = createDate;
        this.UpdateDate = updateDate;
        this.LastServerDate = lastServerDate;
        this.LastClientDate = lastClientDate;
        this.NumServer = numServer;
        this.NumClient = numClient;
        this.SeLang = seLang;
    }

    bool isGate = false;
    public bool IsGate
    {
        get
        {
            return isGate;
        }
    }
    public void GateConnection()
    {
        isGate = true;
    }

    Pack? requestPack;
    Cert? requestCert;

    Pack? responsePack;
    Cert? responseCert;
    Rsa? responseKey;

    public Pack? RequestPack
    {
        get
        {
            return requestPack;
        }
    }

    public Cert? RequestCert
    {
        get
        {
            return requestCert;
        }
    }

    public WpcClient(HttpListenerRequest httpListenerRequest) : this(httpListenerRequest.RemoteEndPoint.Address.ToString()) { }

    public WpcClient(string ip)
    {
        IpAddress = ip;
        HostName = "";
        try
        {
            HostName = Domain.GetHostName(IPAddress.Parse(ip), 250)[0];
        }
        catch
        {
        }
        if (Str.IsEmptyStr(HostName))
        {
            HostName = ip;
        }
        requestPack = null;
        requestCert = null;
        responsePack = null;
        responseCert = null;
        responseKey = null;
        Guid = System.Guid.NewGuid().ToString();
    }

    public void ParseRequest(string requestStr)
    {
        WpcPacket? packet = WpcPacket.ParsePacket(requestStr);
        if (packet == null)
        {
            // パケットが不正である
            throw new ApplicationException("Protocol Error: Not WPC Client.");
        }
        requestPack = packet.Pack;
        requestCert = packet.Cert;
    }

    public void SetResponse(Pack pack, Cert cert, Rsa key)
    {
        responsePack = pack;
        responseCert = cert;
        responseKey = key;
    }

    public string ResponseStr
    {
        get
        {
            return WpcPacket.GeneratePacket(responsePack._NullCheck(), responseCert._NullCheck(), responseKey._NullCheck());
        }
    }
}

// WpcEntry
public class WpcEntry
{
    public byte[] EntryName { get; }
    public byte[] Data { get; }

    public WpcEntry(string entryName, byte[] data)
    {
        this.EntryName = GenerateEntryName(entryName);
        this.Data = data;
    }

    public WpcEntry(byte[] entryNameByte, byte[] data)
    {
        this.EntryName = entryNameByte;
        this.Data = data;
    }

    public static List<WpcEntry> ParseDataEntry(string str)
    {
        Buf src = new Buf(Str.AsciiEncoding.GetBytes(str));
        List<WpcEntry> o = new List<WpcEntry>();

        while (true)
        {
            byte[] entryNameByte = src.Read(4);
            if (entryNameByte.Length != 4)
            {
                break;
            }
            byte[] sizeStrByte = src.Read(10);
            if (sizeStrByte.Length != 10)
            {
                break;
            }
            string sizeStr = Str.AsciiEncoding.GetString(sizeStrByte);
            uint size = Str.StrToUInt(sizeStr);

            byte[] strData = src.Read(size);
            if ((uint)strData.Length != size)
            {
                break;
            }

            string payload = Str.AsciiEncoding.GetString(strData);
            byte[] data = Wpc.StrToByte(payload);

            WpcEntry e = new WpcEntry(entryNameByte, data);

            o.Add(e);
        }

        return o;
    }

    public static WpcEntry? FindEntry(List<WpcEntry> entryList, string entryName)
    {
        byte[] entryNameByte = GenerateEntryName(entryName);
        foreach (WpcEntry e in entryList)
        {
            if (Util.MemEquals(e.EntryName, entryNameByte))
            {
                return e;
            }
        }

        return null;
    }

    public static byte[] GenerateEntryName(string entryName)
    {
        byte[] ret = new byte[4];
        uint len;
        entryName = entryName.ToUpperInvariant();
        len = (uint)entryName.Length;
        byte[] entryNameByte = Str.AsciiEncoding.GetBytes(entryName);
        uint i;
        for (i = 0; i < (uint)ret.Length; i++)
        {
            ret[i] = (byte)' ';
        }

        len = (uint)entryNameByte.Length;

        if (len < ret.Length)
        {
            Array.Copy(entryNameByte, 0, ret, 0, (int)len);
        }
        else
        {
            Array.Copy(entryNameByte, 0, ret, 0, ret.Length);
        }

        return ret;
    }

    public static void AddDataEntry(Buf buf, string entryName, byte[] data)
    {
        AddDataEntry(buf, entryName, Wpc.ByteToStr(data));
    }
    public static void AddDataEntry(Buf buf, string entryName, string str)
    {
        byte[] entryNameByte = GenerateEntryName(entryName);
        buf.Write(entryNameByte);

        string numStr = str.Length.ToString("0000000000");
        buf.Write(Str.AsciiEncoding.GetBytes(numStr));

        buf.Write(Str.AsciiEncoding.GetBytes(str));
    }
}

// Packet
public class WpcPacket
{
    public Pack Pack { get; }
    public byte[] Hash { get; }
    public Cert? Cert { get; }
    public byte[]? Sign { get; }

    public bool IsSigned
    {
        get
        {
            return (Cert != null);
        }
    }

    private WpcPacket(Pack pack, byte[] hash, Cert? cert = null, byte[]? sign = null)
    {
        this.Pack = pack;
        this.Hash = hash;
        this.Cert = cert;
        this.Sign = sign;
    }

    public static string GeneratePacket(Pack pack, Cert? cert = null, Rsa? key = null)
    {
        Buf b = new Buf();

        byte[] pack_data = pack.WriteToBuf().ByteData;
        WpcEntry.AddDataEntry(b, "PACK", pack_data);

        byte[] hash = Secure.HashSHA1(pack_data);
        WpcEntry.AddDataEntry(b, "HASH", hash);

        if (cert != null && key != null)
        {
            WpcEntry.AddDataEntry(b, "CERT", cert.ByteData);
            WpcEntry.AddDataEntry(b, "SIGN", key.SignData(hash));
        }

        return Str.AsciiEncoding.GetString(b.ByteData);
    }

    public static WpcPacket? ParsePacket(string recvStr)
    {
        List<WpcEntry> o = WpcEntry.ParseDataEntry(recvStr);

        WpcEntry? e;

        try
        {
            e = WpcEntry.FindEntry(o, "PACK");
            if (e != null)
            {
                byte[] hash = Secure.HashSHA1(e.Data);
                Pack? pack = null;

                pack = Pack.CreateFromBuf(new Buf(e.Data));

                e = WpcEntry.FindEntry(o, "HASH");

                if (e != null)
                {
                    byte[] hash2 = e.Data;

                    if (Util.MemEquals(hash, hash2))
                    {
                        e = WpcEntry.FindEntry(o, "CERT");
                        if (e != null)
                        {
                            Cert cert;

                            try
                            {
                                cert = new Cert(e.Data);
                            }
                            catch
                            {
                                return null;
                            }

                            e = WpcEntry.FindEntry(o, "SIGN");
                            if (e != null)
                            {
                                byte[] sign = e.Data;

                                if (cert.RsaPublicKey.VerifyData(hash2, sign))
                                {
                                    return new WpcPacket(pack, hash2, cert, sign);
                                }
                            }
                        }
                        else
                        {
                            return new WpcPacket(pack, hash2);
                        }
                    }
                }
            }
        }
        catch (OverflowException)
        {
            return null;
        }

        return null;
    }
}

// Web Procedure Call
public static class Wpc
{
    // Base 64 エンコード
    public static string Base64Encode(byte[] data)
    {
        try
        {
            return Convert.ToBase64String(data);
        }
        catch
        {
            return "";
        }
    }

    // Base 64 デコード
    public static byte[] Base64Decode(string str)
    {
        try
        {
            return Convert.FromBase64String(str);
        }
        catch
        {
            return new byte[0];
        }
    }

    // 文字列置換
    public static string Base64ToSafe64(string str)
    {
        return str.Replace('=', '(').Replace('+', ')').Replace('/', '_');
    }
    public static string Safe64ToBase64(string str)
    {
        return str.Replace('(', '=').Replace(')', '+').Replace('_', '/');
    }

    // バイトを文字列に変換
    public static string ByteToStr(byte[] data)
    {
        return Base64ToSafe64(Base64Encode(data));
    }

    // 文字列をバイトに変換
    public static byte[] StrToByte(string str)
    {
        return Base64Decode(Safe64ToBase64(str));
    }

    // ulong を DateTime に変換
    public static DateTime ConvertDateTime(ulong time64)
    {
        if (time64 == 0)
        {
            return new DateTime(0);
        }
        return new DateTime(((long)time64 + 62135629200000) * 10000);
    }

    // DateTime を ulong に変換
    public static ulong ConvertDateTime(DateTime dt)
    {
        if (dt.Ticks == 0)
        {
            return 0;
        }
        return (ulong)dt.Ticks / 10000 - 62135629200000;
    }

    // ulong を TimeSpan に変換
    public static TimeSpan ConvertTimeSpan(ulong tick)
    {
        return new TimeSpan((long)tick * 10000);
    }

    // TimeSpan を ulong に変換
    public static ulong ConvertTimeSpan(TimeSpan span)
    {
        return (ulong)span.Ticks / 10000;
    }
}

#endif // CORES_BASIC_SECURITY

