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
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Diagnostics.CodeAnalysis;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

#pragma warning disable CS0649

namespace IPA.Cores.Basic;

public static partial class IPConsts
{
    public const int EtherMtuDefault = 1500;
    public const int EtherMtuMax = 9300;

    public const int MinIPv4Mtu = 576;
    public const int MinIPv6Mtu = 1280;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct GenericHeader
{
}

[Flags]
public enum TCPWellknownPorts : ushort
{
    L2TP = 1701,
}

[Flags]
public enum PPPProtocolId : ushort
{
    Unknown = 0,
    LCP = 0xc021,
    PAP = 0xc023,
    IPCP = 0x8021,
    CHAP = 0xc223,
    IPv4 = 0x0021,
    IPv6 = 0x0057,
}

[Flags]
public enum EthernetProtocolId : ushort
{
    Unknown = 0,
    ARPv4 = 0x0806,
    IPv4 = 0x0800,
    IPv6 = 0x86dd,
    VLan = 0x8100,
    PPPoE_Discovery = 0x8863,
    PPPoE_Session = 0x8864,
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct EthernetHeader
{
    public fixed byte DestAddress[6];
    public fixed byte SrcAddress[6];
    public EthernetProtocolId Protocol;

    [MethodImpl(Inline)]
    public void SetDestAddress(ReadOnlySpan<byte> addr)
    {
        fixed (byte* d = this.DestAddress)
        fixed (byte* s = addr)
            Unsafe.CopyBlock(d, s, 6);
    }

    [MethodImpl(Inline)]
    public void SetSrcAddress(ReadOnlySpan<byte> addr)
    {
        fixed (byte* d = this.SrcAddress)
        fixed (byte* s = addr)
            Unsafe.CopyBlock(d, s, 6);
    }

    [MethodImpl(Inline)]
    public void Set(ReadOnlySpan<byte> dest, ReadOnlySpan<byte> src, EthernetProtocolId protocol_EndianSafe)
    {
        SetDestAddress(dest);
        SetSrcAddress(src);
        this.Protocol = protocol_EndianSafe._Endian16();
    }
}

[Flags]
public enum PPPoECode : byte
{
    Data = 0x00,
    ActiveDiscoveryInitiation = 0x09,
    ActiveDiscoveryOffer = 0x07,
    ActiveDiscoveryRequest = 0x19,
    ActiveDiscoverySessionConfirmation = 0x65,
    ActiveDiscoveryTerminate = 0xa7,
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct PPPoESessionHeader
{
    public byte VersionAndType;
    public PPPoECode Code;
    public ushort SessionId;
    public ushort PayloadLength;
    public PPPProtocolId PPPProtocolId;

    public byte Version
    {
        get => (byte)(this.VersionAndType._GetBitsUInt8(0xf0) >> 4);
        set => this.VersionAndType._UpdateBitsUInt8(0xf0, (byte)(value << 4));
    }

    public byte Type
    {
        get => this.VersionAndType._GetBitsUInt8(0x0f);
        set => this.VersionAndType._UpdateBitsUInt8(0x0f, (byte)value);
    }
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct VLanHeader
{
    public ushort TagAndVLanId;
    public EthernetProtocolId Protocol;

    public ushort VLanId_EndianSafe
    {
        get => this.TagAndVLanId._GetBitsUInt16_EndianSafe(0xfff);
        set => this.TagAndVLanId._UpdateBitsUInt16_EndianSafe(0xfff, (ushort)value);
    }
}

[Flags]
public enum IPProtocolNumber : byte
{
    Unknown = 0,
    TCP = 0x06,
    UDP = 0x11,
    ESP = 50,
    EtherIP = 97,
    L2TPv3 = 115,
}

[Flags]
public enum IPv4Flags : byte
{
    MoreFragments = 1,
    DontFragment = 2,
    Reserved = 4,
}


// Proxy Protocol Ver 2.0: https://www.haproxy.org/download/2.2/doc/proxy-protocol.txt

[Flags]
public enum ProxyProtocolVersion : byte
{
    V2 = 2,
}

[Flags]
public enum ProxyProtocolCommand : byte
{
    Local = 0,
    Proxy = 1,
}

[Flags]
public enum ProxyProtocolAddressFamily : byte
{
    None = 0,
    IPv4 = 1,
    IPv6 = 2,
    Unix = 3,
}

[Flags]
public enum ProxyProtocolProtocol : byte
{
    None = 0,
    Stream = 0x11,
    Datagram = 0x22,
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct ProxyProtocolV2Header
{
    public static readonly ReadOnlyMemory<byte> ProxyProtocolSignatureConst12Bytes = "0D0A0D0A000D0A515549540A"._GetHexBytes();

    public fixed byte ProxyProcotolSignature[12]; // 0x0D,0x0A,0x0D,0x0A,0x00,0x0D,0x0A,0x51,0x55,0x49,0x54,0x0A
    public byte VersionAndCommand;
    public byte AddressFamilyAndProtocol;
    public ushort AdditionalDataLength;

    public ProxyProtocolVersion Version
    {
        get => (ProxyProtocolVersion)(this.VersionAndCommand >> 4 & 0x0f);
        set => VersionAndCommand |= (byte)((((byte)value) & 0x0f) << 4);
    }

    public ProxyProtocolCommand Command
    {
        get => (ProxyProtocolCommand)(VersionAndCommand & 0x0f);
        set => VersionAndCommand |= (byte)(((byte)value) & 0x0f);
    }

    public ProxyProtocolAddressFamily AddressFamily
    {
        get => (ProxyProtocolAddressFamily)(this.AddressFamilyAndProtocol >> 4 & 0x0f);
        set => AddressFamilyAndProtocol |= (byte)((((byte)value) & 0x0f) << 4);
    }

    public ProxyProtocolProtocol Protocol
    {
        get => (ProxyProtocolProtocol)(AddressFamilyAndProtocol & 0x0f);
        set => AddressFamilyAndProtocol |= (byte)(((byte)value) & 0x0f);
    }
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct ProxyProtocolAdditionalDataForIPv4
{
    public uint SrcIPv4Address;
    public uint DstIPv4Address;
    public ushort SrcPort;
    public ushort DstPort;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct ProxyProtocolAdditionalDataForIPv6
{
    public fixed byte SrcIPv6Address[16];
    public fixed byte DstIPv6Address[16];
    public ushort SrcPort;
    public ushort DstPort;
}

public class ProxyProtocolV2Parsed
{
    public ProxyProtocolVersion Version;
    public ProxyProtocolCommand Command;
    public ProxyProtocolAddressFamily AddressFamily;
    public ProxyProtocolProtocol Protocol;
    public IPEndPoint? SrcEndPoint;
    public IPEndPoint? DstEndPoint;

    public static unsafe bool TryParse(ref Span<byte> bufWalk, [NotNullWhen(true)] out ProxyProtocolV2Parsed? ret)
    {
        var current = bufWalk;
        ret = null;

        // ヘッダ
        if (current._TryWalkAsStruct(out ProxyProtocolV2Header header) == false)
        {
            return false;
        }

        if (header.Version != ProxyProtocolVersion.V2)
        {
            return false;
        }

        if (Util.MemEquals(new ReadOnlySpan<byte>(header.ProxyProcotolSignature, 12), ProxyProtocolV2Header.ProxyProtocolSignatureConst12Bytes.Span) == false)
        {
            return false;
        }

        // 付加データ
        int additionalDataSize = header.AdditionalDataLength._Endian16_U();

        ret = new ProxyProtocolV2Parsed();

        ret.Version = header.Version;
        ret.Command = header.Command;
        ret.AddressFamily = header.AddressFamily;
        ret.Protocol = header.Protocol;

        if (additionalDataSize != 0)
        {
            var additionalDataBuf = current._TryWalk(additionalDataSize);

            if (additionalDataBuf.IsEmpty)
            {
                return false;
            }

            switch (header.AddressFamily)
            {
                case ProxyProtocolAddressFamily.IPv4:
                    if (additionalDataBuf._TryWalkAsStruct(out ProxyProtocolAdditionalDataForIPv4 v4))
                    {
                        ret.SrcEndPoint = new IPEndPoint(new IPAddress(v4.SrcIPv4Address), v4.SrcPort);
                        ret.DstEndPoint = new IPEndPoint(new IPAddress(v4.DstIPv4Address), v4.DstPort);
                    }
                    break;

                case ProxyProtocolAddressFamily.IPv6:
                    if (additionalDataBuf._TryWalkAsStruct(out ProxyProtocolAdditionalDataForIPv6 v6))
                    {
                        ret.SrcEndPoint = new IPEndPoint(new IPAddress(Util.BytePtrToReadOnlySpan(v6.SrcIPv6Address, 16)), v6.SrcPort);
                        ret.DstEndPoint = new IPEndPoint(new IPAddress(Util.BytePtrToReadOnlySpan(v6.DstIPv6Address, 16)), v6.DstPort);
                    }
                    break;

                default:
                    break;
            }
        }

        bufWalk = current;
        return true;
    }
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct IPv4Header
{
    public byte VersionAndHeaderLength;
    public byte TypeOfService;
    public ushort TotalLength;
    public ushort Identification;
    public fixed byte FlagsAndFlagmentOffset[2];
    public byte TimeToLive;
    public IPProtocolNumber Protocol;
    public ushort Checksum;
    public uint SrcIP;
    public uint DstIP;

    public byte Version
    {
        get => (byte)(this.VersionAndHeaderLength >> 4 & 0x0f);
        set => VersionAndHeaderLength |= (byte)(((value) & 0x0f) << 4);
    }

    public byte HeaderLen
    {
        get => (byte)(VersionAndHeaderLength & 0x0f);
        set => VersionAndHeaderLength |= (byte)((value) & 0x0f);
    }

    public IPv4Flags Flags
    {
        get => (IPv4Flags)((FlagsAndFlagmentOffset[0] >> 5) & 0x07);
        set => FlagsAndFlagmentOffset[0] |= (byte)((((byte)(value)) & 0x07) << 5);
    }

    public ushort Offset_EndianSafe
    {
        get => (ushort)(((FlagsAndFlagmentOffset[0] & 0x1f) * 256 + (FlagsAndFlagmentOffset[1])));
        set { FlagsAndFlagmentOffset[0] |= (byte)((value) / 256); FlagsAndFlagmentOffset[1] = (byte)((value) % 256); }
    }
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct UDPHeader
{
    public ushort SrcPort;
    public ushort DstPort;
    public ushort PacketLength;
    public ushort Checksum;
}

[Flags]
public enum TCPFlags : byte
{
    None = 0,
    Fin = 1,
    Syn = 2,
    Rst = 4,
    Psh = 8,
    Ack = 16,
    Urg = 32,
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct TCPHeader
{
    public ushort SrcPort;
    public ushort DstPort;
    public uint SeqNumber;
    public uint AckNumber;
    public byte HeaderSizeAndReserved;
    public TCPFlags Flag;
    public ushort WindowSize;
    public ushort Checksum;
    public ushort UrgentPointer;

    public byte HeaderLen
    {
        get => (byte)((this.HeaderSizeAndReserved >> 4) & 0x0f);
        set => this.HeaderSizeAndReserved = (byte)((value & 0x0f) << 4);
    }
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct IPv4PseudoHeader
{
    public uint SrcIP;
    public uint DstIP;
    public byte Reserved;
    public byte Protocol;
    public ushort PacketLength;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct UDPv4PseudoHeader
{
    public uint SrcIP;
    public uint DstIP;
    public byte Reserved;
    public byte Protocol;
    public ushort PacketLength1;
    public ushort SrcPort;
    public ushort DstPort;
    public ushort PacketLength2;
    public ushort Checksum;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct PPPDataHeader
{
    public byte Address;
    public byte Control;
    public PPPProtocolId Protocol;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct L2TPHeaderForStdData
{
    public L2TPPacketFlag Flag;
    public byte ReservedAndVersion;
    public ushort Length;
    public ushort TunnelId;
    public ushort SessionId;
}

[Flags]
public enum L2TPPacketType
{
    Data = 0,
    Control,
}

[Flags]
public enum L2TPPacketFlag : byte
{
    None = 0,
    Priority = 0x01,
    Offset = 0x02,
    Sequence = 0x08,
    Length = 0x40,
    ControlMessage = 0x80,
}

public class L2TPPacketParsed
{
    public int Version;
    public L2TPPacketFlag Flag;
    public bool IsZLB;
    public bool IsYamahaV3;
    public int Length;
    public uint TunnelId;
    public uint SessionId;
    public ushort Ns, Nr;
    public int OffsetSize;
    public PacketSpan<GenericHeader> Data;

    public bool IsControlMessage => Flag.Bit(L2TPPacketFlag.ControlMessage);
    public bool IsDataMessage => !IsControlMessage;
}


public static partial class IPUtil
{
    [MethodImpl(Inline)]
    public static EthernetProtocolId ConvertPPPToEthernetProtocolId(this PPPProtocolId id)
    {
        switch (id)
        {
            case PPPProtocolId.IPv4:
                return EthernetProtocolId.IPv4;

            case PPPProtocolId.IPv6:
                return EthernetProtocolId.IPv6;

            default:
                return EthernetProtocolId.Unknown;
        }
    }

    [MethodImpl(Inline)]
    public static unsafe ushort CalcIPv4Checksum(this ref IPv4Header v4Header)
    {
        int headerLen = v4Header.HeaderLen * 4;

        if (v4Header.Checksum == 0)
        {
            return IpChecksum(Unsafe.AsPointer(ref v4Header), headerLen);
        }
        else
        {
            return CalcIPv4ChecksumInternalWithZeroBackupRestore(ref v4Header, headerLen);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static unsafe ushort CalcIPv4ChecksumInternalWithZeroBackupRestore(this ref IPv4Header v4Header, int headerLen)
    {
        ushort checksumBackup = v4Header.Checksum;
        v4Header.Checksum = 0;

        ushort ret = IpChecksum(Unsafe.AsPointer(ref v4Header), headerLen);

        v4Header.Checksum = checksumBackup;

        return ret;
    }

    [MethodImpl(Inline)]
    public static unsafe ushort CalcTcpUdpPseudoChecksum(void* ipHeaderPtr, void* ipPayloadPtr, int ipPayloadSize)
    {
        ushort pseudoChecksum = IPUtil.CalcPseudoTcpUdpHeaderChecksum(ipHeaderPtr, ipPayloadSize);

        return IpChecksum(ipPayloadPtr, ipPayloadSize, pseudoChecksum);
    }

    [MethodImpl(Inline)]
    public static unsafe ushort CalcTcpUdpPseudoChecksum(this ref IPv4Header v4Header, void* ipPayloadPtr, int ipPayloadSize)
    {
        return CalcTcpUdpPseudoChecksum(Unsafe.AsPointer(ref v4Header), ipPayloadPtr, ipPayloadSize);
    }

    [MethodImpl(Inline)]
    public static unsafe ushort CalcTcpUdpPseudoChecksum(this ref TCPHeader tcpHeader, ref IPv4Header v4Header, ReadOnlySpan<byte> tcpPayloadData)
    {
        ushort tcpPayloadChecksum;
        ushort pseudoHeaderChecksum;
        ushort tcpHeaderChecksum;

        int tcpHeaderLen = tcpHeader.HeaderLen * 4;
        int tcpPayloadDataLen = tcpPayloadData.Length;

        pseudoHeaderChecksum = CalcPseudoTcpUdpHeaderChecksum(Unsafe.AsPointer(ref v4Header), tcpHeaderLen + tcpPayloadDataLen);

        tcpHeaderChecksum = IpChecksumWithoutComplement(Unsafe.AsPointer(ref tcpHeader), tcpHeaderLen, pseudoHeaderChecksum);

        fixed (byte* tcpPtr = tcpPayloadData)
            tcpPayloadChecksum = IpChecksum(tcpPtr, tcpPayloadDataLen, tcpHeaderChecksum);

        return tcpPayloadChecksum;
    }

    // Use of this source code is governed by the Apache 2.0 license; see COPYING.
    // * Generic checksm routine originally taken from DPDK: 
    // *   BSD license; (C) Intel 2010-2015, 6WIND 2014. 
    // From https://github.com/snabbco/snabb/blob/771b55c829f42a1a788002c2924c6d7047cd1568/src/lib/checksum.c
    public static unsafe ushort IpChecksum(void* ptr, int size, ushort initialValue = 0)
    {
        uint sum = initialValue/*._Endian16()*/;
        ushort* u16 = (ushort*)ptr;

        while (size >= (sizeof(ushort) * 4))
        {
            sum += u16[0];
            sum += u16[1];
            sum += u16[2];
            sum += u16[3];
            size -= sizeof(ushort) * 4;
            u16 += 4;
        }
        while (size >= sizeof(ushort))
        {
            sum += *u16;
            size -= sizeof(ushort);
            u16 += 1;
        }

        /* if length is in odd bytes */
        if (size == 1)
            sum += *((byte*)u16);

        while ((sum >> 16) != 0)
            sum = (sum & 0xFFFF) + (sum >> 16);
        return ((ushort)~sum)/*._Endian16()*/;
    }

    public static unsafe ushort IpChecksumWithoutComplement(void* ptr, int size, ushort initialValue = 0)
    {
        uint sum = initialValue/*._Endian16()*/;
        ushort* u16 = (ushort*)ptr;

        while (size >= (sizeof(ushort) * 4))
        {
            sum += u16[0];
            sum += u16[1];
            sum += u16[2];
            sum += u16[3];
            size -= sizeof(ushort) * 4;
            u16 += 4;
        }
        while (size >= sizeof(ushort))
        {
            sum += *u16;
            size -= sizeof(ushort);
            u16 += 1;
        }

        /* if length is in odd bytes */
        if (size == 1)
            sum += *((byte*)u16);

        while ((sum >> 16) != 0)
            sum = (sum & 0xFFFF) + (sum >> 16);
        return ((ushort)sum)/*._Endian16()*/;
    }


    // calculates the initial checksum value resulting from
    // the pseudo header.
    // return values:
    // 0x0000 - 0xFFFF : initial checksum (in host byte order).
    // 0xFFFF0001 : unknown packet (non IPv4/6 or non TCP/UDP)
    // 0xFFFF0002 : bad header
    // Use of this source code is governed by the Apache 2.0 license; see COPYING.
    // * Generic checksm routine originally taken from DPDK: 
    // *   BSD license; (C) Intel 2010-2015, 6WIND 2014. 
    // From https://github.com/snabbco/snabb/blob/771b55c829f42a1a788002c2924c6d7047cd1568/src/lib/checksum.c
    public static unsafe ushort CalcPseudoTcpUdpHeaderChecksum(void* ipHeaderPtr, int ipPayloadTotalSizeWithoutIpHeader)
    {
        byte* buf = (byte*)ipHeaderPtr;
        ushort* hwbuf = (ushort*)buf;
        byte ipv = (byte)((buf[0] & 0xF0) >> 4);
        byte proto = 0;
        int headersize = 0;

        if (ipv == 4)
        {
            // IPv4
            proto = buf[9];
            headersize = (buf[0] & 0x0F) * 4;
        }
        else if (ipv == 6)
        {
            // IPv6
            proto = buf[6];
            headersize = 40;
        }
        else
        {
            return (ushort)0xBEEF._Endian16_S();
        }

        if (proto == 6 || proto == 17)
        {
            // TCP || UDP
            uint sum = 0;
            if (ipv == 4)
            {
                // IPv4
                //if (IpChecksum(buf, headersize, 0) != 0)
                //{
                //    return 0xFFFF0002;
                //}
                sum = ((ushort)(ipPayloadTotalSizeWithoutIpHeader))._Endian16_U() + (uint)(proto << 8) + hwbuf[6] + hwbuf[7] + hwbuf[8] + hwbuf[9];
            }
            else
            {
                // IPv6
                sum = ((uint)ipPayloadTotalSizeWithoutIpHeader)._Endian32_U() /*hwbuf[2]*/ + (uint)(proto << 8);
                int i;
                for (i = 4; i < 20; i += 4)
                {
                    sum += (uint)(hwbuf[i] + hwbuf[i + 1] + hwbuf[i + 2] + hwbuf[i + 3]);
                }
            }
            sum = ((sum & 0xffff0000) >> 16) + (sum & 0xffff);
            sum = ((sum & 0xffff0000) >> 16) + (sum & 0xffff);
            return (ushort)(sum);
        }
        return (ushort)0xDEAD._Endian16_S();
    }

    [MethodImpl(Inline)]
    public static unsafe ushort IpChecksum(ReadOnlySpan<byte> data, ushort initial = 0)
    {
        fixed (byte* ptr = data)
            return IpChecksum(ptr, data.Length, initial);
    }

    public static int CalcIpMtu(IPVersion ipVer, int etherMtu = IPConsts.EtherMtuDefault)
    {
        int ret;
        if (ipVer == IPVersion.IPv4)
        {
            ret = etherMtu - 20;
        }
        else
        {
            ret = etherMtu - 40;
        }
        return Math.Max(ret, (ipVer == IPVersion.IPv4 ? IPConsts.MinIPv4Mtu : IPConsts.MinIPv6Mtu));
    }

    public static int CalcTcpMss(IPVersion ipVer, int etherMtu = IPConsts.EtherMtuDefault)
    {
        int ret;
        if (ipVer == IPVersion.IPv4)
        {
            ret = etherMtu - 20 - 20;
        }
        else
        {
            ret = etherMtu - 40 - 20;
        }
        return Math.Max(ret, (ipVer == IPVersion.IPv4 ? IPConsts.MinIPv4Mtu : IPConsts.MinIPv6Mtu));
    }
}

public class TcpPseudoPacketGeneratorOptions
{
    public IPAddress LocalIP { get; }
    public ushort LocalPort { get; }
    public IPAddress RemoteIP { get; }
    public ushort RemotePort { get; }
    public TcpDirectionType TcpDirection { get; }
    public int EtherMtu { get; }
    public IPVersion IPVersion { get; }

    public TcpPseudoPacketGeneratorOptions(TcpDirectionType direction, IPAddress localIP, int localPort, IPAddress remoteIP, int remotePort, int etherMtu = IPConsts.EtherMtuDefault)
    {
        if (direction.EqualsAny(TcpDirectionType.Client, TcpDirectionType.Server) == false)
            throw new ArgumentException("direction");

        if (localIP.AddressFamily != remoteIP.AddressFamily)
            throw new ArgumentException("LocalIP.AddressFamily != RemoteIP.AddressFamily");

        this.LocalIP = localIP;
        this.LocalPort = (ushort)localPort;
        this.RemoteIP = remoteIP;
        this.RemotePort = (ushort)remotePort;
        this.EtherMtu = etherMtu;

        this.TcpDirection = direction;

        this.IPVersion = this.LocalIP.AddressFamily._GetIPVersion();
    }
}

public class TcpPseudoPacketGenerator : IDisposable, IAsyncDisposable
{
    public TcpPseudoPacketGeneratorOptions Options { get; }

    public ReadOnlyMemory<byte> LocalMacAddress { get; }
    public ReadOnlyMemory<byte> RemoteMacAddress { get; }

    public DatagramExchangePoint Output { get; }

    readonly PacketSizeSet DefaultPacketSizeSet;

    readonly uint LocalIP_IPv4;
    readonly uint RemoteIP_IPv4;

    readonly int TcpMss;

    readonly EthernetProtocolId ProtocolId;

    long TotalSendSize, TotalRecvSize;

    ulong PacketIdSeed;

    public TcpPseudoPacketGenerator(DatagramExchangePoint output, TcpPseudoPacketGeneratorOptions options)
    {
        this.Output = output;

        this.Options = options;

        this.LocalMacAddress = IPUtil.GenerateRandomLocalMacAddress();
        this.RemoteMacAddress = IPUtil.GenerateRandomLocalMacAddress();

        PacketSizeSet tcpPacketSizeSet;

        if (Options.IPVersion == IPVersion.IPv4)
        {
            this.LocalIP_IPv4 = Options.LocalIP._Get_IPv4_UInt32_BigEndian();
            this.RemoteIP_IPv4 = Options.RemoteIP._Get_IPv4_UInt32_BigEndian();
            ProtocolId = EthernetProtocolId.IPv4;

            tcpPacketSizeSet = PacketSizeSets.NormalTcpIpPacket_V4;
        }
        else
        {
            throw new ApplicationException("Unsupported AddressFamily");
        }

        DefaultPacketSizeSet = tcpPacketSizeSet + PacketSizeSets.PcapNgPacket;

        this.TcpMss = IPUtil.CalcTcpMss(Options.IPVersion, options.EtherMtu);
    }

    public void EmitData(ReadOnlySpan<byte> data, Direction direction)
    {
        SpanBasedQueue<Datagram> queue = new SpanBasedQueue<Datagram>(EnsureCtor.Yes);

        int spanLen = data.Length;

        for (int pos = 0; pos < spanLen; pos += this.TcpMss)
        {
            ReadOnlySpan<byte> segmentData = data.Slice(pos, Math.Min(this.TcpMss, spanLen - pos));

            EmitSegmentData(ref queue, segmentData, direction);
        }

        this.Output[0].DatagramWriter.EnqueueAllWithLock(queue.DequeueAll(), true);
    }

    unsafe void EmitSegmentData(ref SpanBasedQueue<Datagram> queue, ReadOnlySpan<byte> data, Direction direction)
    {
        Packet pkt = new Packet(DefaultPacketSizeSet + data.Length);

        pkt.PrependSpanWithData(data);

        ref TCPHeader tcp = ref pkt.PrependSpan<TCPHeader>();

        if (direction == Direction.Send)
        {
            tcp.SeqNumber = (1 + this.TotalSendSize)._Endian32_U();
            tcp.AckNumber = (1 + this.TotalRecvSize)._Endian32_U();

            tcp.SrcPort = Options.LocalPort._Endian16_U();
            tcp.DstPort = Options.RemotePort._Endian16_U();

            this.TotalSendSize += data.Length;
        }
        else
        {
            tcp.SeqNumber = (1 + this.TotalRecvSize)._Endian32_U();
            tcp.AckNumber = (1 + this.TotalSendSize)._Endian32_U();

            tcp.SrcPort = Options.RemotePort._Endian16_U();
            tcp.DstPort = Options.LocalPort._Endian16_U();

            this.TotalRecvSize += data.Length;
        }

        tcp.HeaderLen = (byte)(sizeof(TCPHeader) / 4);
        tcp.Flag = TCPFlags.Ack | TCPFlags.Psh;
        tcp.WindowSize = 0xffff;

        PrependIPHeader(ref pkt, ref tcp, data, direction);

        queue.Enqueue(pkt.ToDatagram());
    }

    public unsafe void EmitFinish(Direction direction)
    {
        Packet pkt = new Packet(DefaultPacketSizeSet);

        ref TCPHeader tcp = ref pkt.PrependSpan<TCPHeader>();

        if (direction == Direction.Send)
        {
            tcp.SeqNumber = (1 + this.TotalSendSize)._Endian32_U();
            tcp.AckNumber = (1 + this.TotalRecvSize)._Endian32_U();

            tcp.SrcPort = Options.LocalPort._Endian16_U();
            tcp.DstPort = Options.RemotePort._Endian16_U();
        }
        else
        {
            tcp.SeqNumber = (1 + this.TotalRecvSize)._Endian32_U();
            tcp.AckNumber = (1 + this.TotalSendSize)._Endian32_U();

            tcp.SrcPort = Options.RemotePort._Endian16_U();
            tcp.DstPort = Options.LocalPort._Endian16_U();
        }

        tcp.HeaderLen = (byte)(sizeof(TCPHeader) / 4);
        tcp.Flag = TCPFlags.Fin | TCPFlags.Ack;
        tcp.WindowSize = 0xffff;

        PrependIPHeader(ref pkt, ref tcp, default, direction);

        this.Output[0].DatagramWriter.EnqueueAllWithLock(pkt.ToDatagram()._SingleReadOnlySpan(), true);
    }

    public void EmitReset(Direction initiator)
    {
        SpanBasedQueue<Datagram> queue = new SpanBasedQueue<Datagram>(EnsureCtor.Yes);

        if (initiator == Direction.Send)
        {
            EmitResetOne(ref queue, Direction.Send);
            EmitResetOne(ref queue, Direction.Recv);
        }
        else
        {
            EmitResetOne(ref queue, Direction.Recv);
            EmitResetOne(ref queue, Direction.Send);
        }

        this.Output[0].DatagramWriter.EnqueueAllWithLock(queue.DequeueAll(), true);
    }

    unsafe void EmitResetOne(ref SpanBasedQueue<Datagram> queue, Direction direction)
    {
        Packet pkt = new Packet(DefaultPacketSizeSet);

        ref TCPHeader tcp = ref pkt.PrependSpan<TCPHeader>();

        if (direction == Direction.Send)
        {
            tcp.SeqNumber = (1 + this.TotalSendSize)._Endian32_U();

            tcp.SrcPort = Options.LocalPort._Endian16_U();
            tcp.DstPort = Options.RemotePort._Endian16_U();
        }
        else
        {
            tcp.SeqNumber = (1 + this.TotalRecvSize)._Endian32_U();

            tcp.SrcPort = Options.RemotePort._Endian16_U();
            tcp.DstPort = Options.LocalPort._Endian16_U();
        }

        tcp.HeaderLen = (byte)(sizeof(TCPHeader) / 4);
        tcp.Flag = TCPFlags.Rst;
        tcp.WindowSize = 0xffff;

        PrependIPHeader(ref pkt, ref tcp, default, direction);

        queue.Enqueue(pkt.ToDatagram());
    }

    public unsafe void EmitConnected()
    {
        SpanBasedQueue<Datagram> queue = new SpanBasedQueue<Datagram>(EnsureCtor.Yes);

        if (Options.TcpDirection == TcpDirectionType.Client)
        {
            // TCP Client
            // SYN
            {
                Packet pkt = new Packet(DefaultPacketSizeSet);

                ref TCPHeader tcp = ref pkt.PrependSpan<TCPHeader>();
                tcp.SrcPort = Options.LocalPort._Endian16_U();
                tcp.DstPort = Options.RemotePort._Endian16_U();
                tcp.SeqNumber = 0._Endian32_U();
                tcp.AckNumber = 0._Endian32_U();
                tcp.HeaderLen = (byte)(sizeof(TCPHeader) / 4);
                tcp.Flag = TCPFlags.Syn;
                tcp.WindowSize = 0xffff;

                PrependIPHeader(ref pkt, ref tcp, default, Direction.Send);

                queue.Enqueue(pkt.ToDatagram());
            }

            // SYN + ACK
            {
                Packet pkt = new Packet(DefaultPacketSizeSet);

                ref TCPHeader tcp = ref pkt.PrependSpan<TCPHeader>();
                tcp.SrcPort = Options.RemotePort._Endian16_U();
                tcp.DstPort = Options.LocalPort._Endian16_U();
                tcp.SeqNumber = 0._Endian32_U();
                tcp.AckNumber = 1._Endian32_U();
                tcp.HeaderLen = (byte)(sizeof(TCPHeader) / 4);
                tcp.Flag = TCPFlags.Syn | TCPFlags.Ack;
                tcp.WindowSize = 0xffff;

                PrependIPHeader(ref pkt, ref tcp, default, Direction.Recv);

                queue.Enqueue(pkt.ToDatagram());
            }

            // ACK
            {
                Packet pkt = new Packet(DefaultPacketSizeSet);

                ref TCPHeader tcp = ref pkt.PrependSpan<TCPHeader>();
                tcp.SrcPort = Options.LocalPort._Endian16_U();
                tcp.DstPort = Options.RemotePort._Endian16_U();
                tcp.SeqNumber = 1._Endian32_U();
                tcp.AckNumber = 1._Endian32_U();
                tcp.HeaderLen = (byte)(sizeof(TCPHeader) / 4);
                tcp.Flag = TCPFlags.Ack;
                tcp.WindowSize = 0xffff;

                PrependIPHeader(ref pkt, ref tcp, default, Direction.Send);

                queue.Enqueue(pkt.ToDatagram());
            }
        }
        else
        {
            // TCP Server
            // SYN
            {
                Packet pkt = new Packet(DefaultPacketSizeSet);

                ref TCPHeader tcp = ref pkt.PrependSpan<TCPHeader>();
                tcp.SrcPort = Options.RemotePort._Endian16_U();
                tcp.DstPort = Options.LocalPort._Endian16_U();
                tcp.SeqNumber = 0._Endian32_U();
                tcp.AckNumber = 0._Endian32_U();
                tcp.HeaderLen = (byte)(sizeof(TCPHeader) / 4);
                tcp.Flag = TCPFlags.Syn;
                tcp.WindowSize = 0xffff;

                PrependIPHeader(ref pkt, ref tcp, default, Direction.Recv);

                queue.Enqueue(pkt.ToDatagram());
            }

            // SYN + ACK
            {
                Packet pkt = new Packet(DefaultPacketSizeSet);

                ref TCPHeader tcp = ref pkt.PrependSpan<TCPHeader>();
                tcp.SrcPort = Options.LocalPort._Endian16_U();
                tcp.DstPort = Options.RemotePort._Endian16_U();
                tcp.SeqNumber = 0._Endian32_U();
                tcp.AckNumber = 1._Endian32_U();
                tcp.HeaderLen = (byte)(sizeof(TCPHeader) / 4);
                tcp.Flag = TCPFlags.Syn | TCPFlags.Ack;
                tcp.WindowSize = 0xffff;

                PrependIPHeader(ref pkt, ref tcp, default, Direction.Recv);

                queue.Enqueue(pkt.ToDatagram());
            }

            // ACK
            {
                Packet pkt = new Packet(DefaultPacketSizeSet);

                ref TCPHeader tcp = ref pkt.PrependSpan<TCPHeader>();
                tcp.SrcPort = Options.RemotePort._Endian16_U();
                tcp.DstPort = Options.LocalPort._Endian16_U();
                tcp.SeqNumber = 1._Endian32_U();
                tcp.AckNumber = 1._Endian32_U();
                tcp.HeaderLen = (byte)(sizeof(TCPHeader) / 4);
                tcp.Flag = TCPFlags.Ack;
                tcp.WindowSize = 0xffff;

                PrependIPHeader(ref pkt, ref tcp, default, Direction.Recv);

                queue.Enqueue(pkt.ToDatagram());
            }
        }

        this.Output[0].DatagramWriter.EnqueueAllWithLock(queue.DequeueAll(), true);
    }

    unsafe void PrependIPHeader(ref Packet p, ref TCPHeader tcp, ReadOnlySpan<byte> tcpPayloadForChecksum, Direction direction)
    {
        ref IPv4Header ip = ref p.PrependSpan<IPv4Header>();
        ip.Version = 4;
        ip.HeaderLen = (byte)(sizeof(IPv4Header) / 4);
        ip.TotalLength = p.Length._Endian16_U();
        ip.Identification = (++PacketIdSeed)._Endian16_U();
        ip.Flags = IPv4Flags.DontFragment;
        ip.Protocol = IPProtocolNumber.TCP;
        ip.TimeToLive = 63;

        if (direction == Direction.Send)
        {
            ip.SrcIP = LocalIP_IPv4;
            ip.DstIP = RemoteIP_IPv4;
        }
        else
        {
            ip.SrcIP = RemoteIP_IPv4;
            ip.DstIP = LocalIP_IPv4;
        }

        tcp.Checksum = tcp.CalcTcpUdpPseudoChecksum(ref ip, tcpPayloadForChecksum);
        ip.Checksum = ip.CalcIPv4Checksum();

        ref EthernetHeader ether = ref p.PrependSpan<EthernetHeader>();
        if (direction == Direction.Send)
        {
            ether.Set(this.RemoteMacAddress.Span, this.LocalMacAddress.Span, ProtocolId);
        }
        else
        {
            ether.Set(this.LocalMacAddress.Span, this.RemoteMacAddress.Span, ProtocolId);
        }
    }

    public void Dispose() { this.Dispose(true); GC.SuppressFinalize(this); }
    Once DisposeFlag;
    public virtual async ValueTask DisposeAsync()
    {
        if (DisposeFlag.IsFirstCall() == false) return;
        await DisposeInternalAsync();
    }
    protected virtual void Dispose(bool disposing)
    {
        if (!disposing || DisposeFlag.IsFirstCall() == false) return;
        DisposeInternalAsync()._GetResult();
    }
    async Task DisposeInternalAsync()
    {
        await this.Output._DisposeSafeAsync();
    }
}

