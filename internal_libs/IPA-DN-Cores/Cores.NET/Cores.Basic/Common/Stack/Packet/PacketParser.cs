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

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using System.Runtime.InteropServices;

namespace IPA.Cores.Basic;

[Flags]
public enum PacketL2Type
{
    Unknown = 0,
    Ethernet = 1,
}

[StructLayout(LayoutKind.Explicit, Pack = 8)]
public readonly struct L2
{
    [FieldOffset(0)]
    public readonly PacketL2Type Type;

    [FieldOffset(8)]
    public readonly PacketSpan<GenericHeader> Generic;
    [FieldOffset(8)]
    public readonly PacketSpan<EthernetHeader> Ethernet;

    [MethodImpl(Inline)]
    public L2(PacketSpan<EthernetHeader> etherSpan)
    {
        this.Ethernet = default;

        this.Type = PacketL2Type.Ethernet;
        this.Generic = etherSpan.ToGenericSpan();
    }
}

[StructLayout(LayoutKind.Explicit, Pack = 8)]
public readonly struct L2_VLan
{
    [FieldOffset(0)]
    public readonly EthernetProtocolId Type;

    [FieldOffset(8)]
    public readonly PacketSpan<GenericHeader> Generic;
    [FieldOffset(8)]
    public readonly PacketSpan<VLanHeader> TagVlan;

    public L2_VLan(PacketSpan<VLanHeader> tagVlanSpan, EthernetProtocolId tpid)
    {
        this.TagVlan = default;

        this.Type = tpid;
        this.Generic = tagVlanSpan.ToGenericSpan();
    }
}

[StructLayout(LayoutKind.Explicit, Pack = 8)]
public readonly struct L3
{
    [FieldOffset(0)]
    public readonly EthernetProtocolId Type;

    [FieldOffset(8)]
    public readonly PacketSpan<GenericHeader> Generic;
    [FieldOffset(8)]
    public readonly PacketSpan<IPv4Header> IPv4;
    [FieldOffset(8)]
    public readonly PacketSpan<PPPoESessionHeader> PPPoE;

    [MethodImpl(Inline)]
    public L3(PacketSpan<IPv4Header> ipv4Span)
    {
        this.IPv4 = default;
        this.PPPoE = default;

        this.Type = EthernetProtocolId.IPv4;
        this.Generic = ipv4Span.ToGenericSpan();
    }

    [MethodImpl(Inline)]
    public L3(PacketSpan<PPPoESessionHeader> pppoeSpan)
    {
        this.IPv4 = default;
        this.PPPoE = default;

        this.Type = EthernetProtocolId.PPPoE_Session;
        this.Generic = pppoeSpan.ToGenericSpan();
    }
}

[StructLayout(LayoutKind.Explicit, Pack = 8)]
public readonly struct L4
{
    [FieldOffset(0)]
    public readonly IPProtocolNumber Type;

    [FieldOffset(8)]
    public readonly PacketSpan<GenericHeader> Generic;
    [FieldOffset(8)]
    public readonly PacketSpan<TCPHeader> TCP;
    [FieldOffset(8)]
    public readonly PacketSpan<UDPHeader> UDP;

    [MethodImpl(Inline)]
    public L4(PacketSpan<TCPHeader> tcpSpan)
    {
        this.TCP = default;
        this.UDP = default;

        this.Type = IPProtocolNumber.TCP;
        this.Generic = tcpSpan.ToGenericSpan();
    }

    [MethodImpl(Inline)]
    public L4(PacketSpan<UDPHeader> udpSpan)
    {
        this.TCP = default;
        this.UDP = default;

        this.Type = IPProtocolNumber.UDP;
        this.Generic = udpSpan.ToGenericSpan();
    }
}

[Flags]
public enum L7Type
{
    Unknown = 0,
    GenericTCP,
    GenericUDP,
    L2TP,
}

[StructLayout(LayoutKind.Explicit, Pack = 8)]
public readonly struct L7
{
    [FieldOffset(0)]
    public readonly L7Type Type;

    [FieldOffset(8)]
    public readonly L2TPPacketParsed? L2TPPacketParsed;

    [FieldOffset(16)]
    public readonly PacketSpan<GenericHeader> Generic;

    [MethodImpl(Inline)]
    public L7(PacketSpan<GenericHeader> payloadSpan, L7Type type)
    {
        this.L2TPPacketParsed = null;

        this.Generic = payloadSpan;
        this.Type = type;
    }

    [MethodImpl(Inline)]
    public L7(PacketSpan<GenericHeader> l2tpPayloadSpan, L2TPPacketParsed l2tpPacketParsed)
    {
        this.L2TPPacketParsed = l2tpPacketParsed;

        this.Generic = l2tpPayloadSpan;
        this.Type = L7Type.L2TP;
    }
}

public class PacketParseOption
{
}

[Flags]
public enum PacketParseMode
{
    Layer2 = 0,
    Layer3,
}

public struct PacketInfo
{
    public uint L3_SrcIPv4;
    public uint L3_DestIPv4;

    public ushort L4_SrcPort;
    public ushort L4_DestPort;
}

public unsafe class PacketParsed
{
    static readonly PacketParseOption DefaultOption = new PacketParseOption();

    public PacketParseOption ParseOption { get; private set; }

    public string? ErrorStr { get; private set; }
    public bool IsOk => ErrorStr == null;
    public bool IsError => !IsOk;

    public L2 L2 { get; private set; }
    public L2_VLan L2_VLan1 { get; private set; }
    public L2_VLan L2_VLan2 { get; private set; }
    public L2_VLan L2_VLan3 { get; private set; }

    public L3 L3 { get; private set; }

    public L4 L4 { get; private set; }

    public L7 L7 { get; private set; }

    public PacketInfo Info;

    public PacketParsed? InnerPacket { get; private set; }

    public PacketParsed(ref Packet pkt, int startPin = DefaultSize, PacketParseOption? options = null, int maxPacketSize = DefaultSize, PacketParseMode mode = PacketParseMode.Layer2, EthernetProtocolId layer3ProtocolId = EthernetProtocolId.Unknown)
    {
        this.ParseOption = options ?? DefaultOption;
        maxPacketSize = maxPacketSize._DefaultSize(pkt.Length);

        switch (mode)
        {
            case PacketParseMode.Layer2:
                PacketSpan<EthernetHeader> etherSpan = pkt.GetSpan<EthernetHeader>(startPin._DefaultSize(pkt.PinHead), maxPacketSize: maxPacketSize);
                ParseL2_Ethernet(ref pkt, etherSpan);
                break;

            case PacketParseMode.Layer3:
                PacketSpan<GenericHeader> l3Span = pkt.GetSpan<GenericHeader>(startPin._DefaultSize(pkt.PinHead), size: 0, maxPacketSize: maxPacketSize);
                ParseL3(ref pkt, l3Span, layer3ProtocolId);
                break;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    void SetError(string err, [CallerMemberName] string? caller = null)
    {
        caller = caller._NonNullTrim();
        err = err._NonNullTrim();
        this.ErrorStr = $"{caller}: {err}";
    }

    bool ParseL2_Ethernet(ref Packet pkt, PacketSpan<EthernetHeader> etherSpan)
    {
        if (etherSpan.IsEmpty(ref pkt))
        {
            SetError("Insufficient header data");
            return false;
        }

        this.L2 = new L2(etherSpan);

        ref EthernetHeader ether = ref etherSpan.GetRefValue(ref pkt);

        EthernetProtocolId tpid = ether.Protocol._Endian16();

        if (tpid == EthernetProtocolId.VLan)
            return ParseL2_VLan1(ref pkt, this.L2.Ethernet, tpid);
        else
            return ParseL3(ref pkt, this.L2.Generic, tpid);
    }

    bool ParseL2_VLan1(ref Packet pkt, PacketSpan<EthernetHeader> prevSpan, EthernetProtocolId thisTpid)
    {
        PacketSpan<VLanHeader> vlanSpan = prevSpan.GetNextSpan<VLanHeader>(ref pkt);
        if (vlanSpan.IsEmpty(ref pkt))
        {
            SetError("Insufficient header data");
            return false;
        }

        this.L2_VLan1 = new L2_VLan(vlanSpan, thisTpid);

        ref VLanHeader vlan = ref vlanSpan.GetRefValue(ref pkt);

        EthernetProtocolId tpid = vlan.Protocol._Endian16();

        if (tpid == EthernetProtocolId.VLan)
            return ParseL2_VLan2(ref pkt, this.L2.Ethernet, tpid);
        else
            return ParseL3(ref pkt, this.L2_VLan1.Generic, tpid);
    }

    bool ParseL2_VLan2(ref Packet pkt, PacketSpan<EthernetHeader> prevSpan, EthernetProtocolId thisTpid)
    {
        PacketSpan<VLanHeader> vlanSpan = prevSpan.GetNextSpan<VLanHeader>(ref pkt);
        if (vlanSpan.IsEmpty(ref pkt))
        {
            SetError("Insufficient header data");
            return false;
        }

        ref VLanHeader vlan = ref vlanSpan.GetRefValue(ref pkt);

        EthernetProtocolId tpid = vlan.Protocol._Endian16();

        if (tpid == EthernetProtocolId.VLan)
            return ParseL2_VLan3(ref pkt, this.L2.Ethernet, tpid);
        else
            return ParseL3(ref pkt, this.L2_VLan2.Generic, tpid);
    }

    bool ParseL2_VLan3(ref Packet pkt, PacketSpan<EthernetHeader> prevSpan, EthernetProtocolId thisTpid)
    {
        PacketSpan<VLanHeader> vlanSpan = prevSpan.GetNextSpan<VLanHeader>(ref pkt);
        if (vlanSpan.IsEmpty(ref pkt))
        {
            SetError("Insufficient header data");
            return false;
        }

        ref VLanHeader vlan = ref vlanSpan.GetRefValue(ref pkt);

        EthernetProtocolId tpid = vlan.Protocol._Endian16();

        if (tpid == EthernetProtocolId.VLan)
        {
            SetError("Too many tagged VLAN headers stacking");
            return false;
        }
        else
        {
            return ParseL3(ref pkt, this.L2_VLan3.Generic, tpid);
        }
    }

    [MethodImpl(Inline)]
    bool ParseL3(ref Packet pkt, PacketSpan<GenericHeader> prevSpan, EthernetProtocolId tpid)
    {
        switch (tpid)
        {
            case EthernetProtocolId.IPv4:
                return ParseL3_IPv4(ref pkt, prevSpan);

            case EthernetProtocolId.PPPoE_Session:
                return ParseL3_PPPoESession(ref pkt, prevSpan);
        }

        return true;
    }

    bool ParseL3_PPPoESession(ref Packet pkt, PacketSpan<GenericHeader> prevSpan)
    {
        PacketSpan<PPPoESessionHeader> pppoeSpan = prevSpan.GetNextSpan<PPPoESessionHeader>(ref pkt);
        if (pppoeSpan.IsEmpty(ref pkt))
        {
            SetError("Insufficient header data");
            return false;
        }

        ref PPPoESessionHeader pppoe = ref pppoeSpan.GetRefValue(ref pkt);

        if (pppoe.Version != 1)
        {
            SetError($"Invalid version: {pppoe.Version}");
            return false;
        }

        if (pppoe.Type != 1)
        {
            SetError($"Invalid type: {pppoe.Type}");
            return false;
        }

        if (pppoe.Code != PPPoECode.Data)
        {
            SetError($"Invalid code: {pppoe.Code}");
            return false;
        }

        int payloadSize = pppoe.PayloadLength._Endian16_U();
        if (payloadSize < sizeof(PPPProtocolId))
        {
            SetError($"PayloadLength < {sizeof(PPPProtocolId)}");
            return false;
        }
        payloadSize -= sizeof(PPPProtocolId);

        this.L3 = new L3(pppoeSpan);

        ParsePPP_AsOverlay(ref pkt, this.L3.Generic, pppoe.PPPProtocolId._Endian16(), payloadSize);

        return true;
    }

    void ParsePPP_AsOverlay(ref Packet pkt, PacketSpan<GenericHeader> prevSpan, PPPProtocolId pppProtocolId, int size)
    {
        EthernetProtocolId etherProtocolId = pppProtocolId.ConvertPPPToEthernetProtocolId();

        PacketSpan<GenericHeader> innerSpan = prevSpan.GetNextSpan<GenericHeader>(ref pkt, size);

        PacketParsed innerPacketParsed = new PacketParsed(ref pkt, innerSpan.Pin, this.ParseOption, innerSpan.HeaderSize, PacketParseMode.Layer3, etherProtocolId);

        this.InnerPacket = innerPacketParsed;
    }

    bool ParseL3_IPv4(ref Packet pkt, PacketSpan<GenericHeader> prevSpan)
    {
        ref IPv4Header ipv4 = ref prevSpan.GetNextHeaderRefValue<IPv4Header>(ref pkt);

        if (ipv4.Version != 4)
        {
            SetError($"Invalid version: {ipv4.Version}");
            return false;
        }

        int headerLen = ipv4.HeaderLen * 4;
        if (headerLen < sizeof(IPv4Header))
        {
            SetError($"Invalid HeaderLen: {headerLen}");
            return false;
        }

        int totalLen = ipv4.TotalLength._Endian16_U();
        if (totalLen < headerLen)
        {
            SetError($"Invalid TotalLength: {totalLen}");
            return false;
        }

        PacketSpan<IPv4Header> ipv4Span = prevSpan.GetNextSpan<IPv4Header>(ref pkt, headerLen, totalLen);
        if (ipv4Span.IsEmpty(ref pkt))
        {
            SetError($"Insufficient header data. HeaderLen: {headerLen}");
            return false;
        }

        this.L3 = new L3(ipv4Span);
        this.Info.L3_SrcIPv4 = ipv4.SrcIP;
        this.Info.L3_DestIPv4 = ipv4.DstIP;

        switch (ipv4.Protocol)
        {
            case IPProtocolNumber.TCP:
                return ParseL4_TCP(ref pkt, this.L3.Generic);

            case IPProtocolNumber.UDP:
                return ParseL4_UDP(ref pkt, this.L3.Generic);
        }

        return true;
    }

    bool ParseL4_UDP(ref Packet pkt, PacketSpan<GenericHeader> prevSpan)
    {
        PacketSpan<UDPHeader> udpSpan = prevSpan.GetNextSpan<UDPHeader>(ref pkt);
        if (udpSpan.IsEmpty(ref pkt))
        {
            SetError("Insufficient header data");
            return false;
        }

        ref UDPHeader udp = ref udpSpan.GetRefValue(ref pkt);

        this.L4 = new L4(udpSpan);

        this.Info.L4_SrcPort = udp.SrcPort._Endian16_U();
        this.Info.L4_DestPort = udp.DstPort._Endian16_U();

        PacketSpan<GenericHeader> payloadSpan = udpSpan.GetNextSpan<GenericHeader>(ref pkt, size: udpSpan.GetPayloadSize(ref pkt));

        return ParseL7_UDP(ref pkt, payloadSpan, in udp);
    }

    bool ParseL4_TCP(ref Packet pkt, PacketSpan<GenericHeader> prevSpan)
    {
        ref TCPHeader tcp = ref prevSpan.GetNextHeaderRefValue<TCPHeader>(ref pkt);

        int headerLen = tcp.HeaderLen * 4;
        if (headerLen < sizeof(TCPHeader))
        {
            SetError($"Invalid HeaderLen: {headerLen}");
            return false;
        }

        PacketSpan<TCPHeader> tcpSpanFull = prevSpan.GetNextSpan<TCPHeader>(ref pkt, headerLen);
        if (tcpSpanFull.IsEmpty(ref pkt))
        {
            SetError($"Insufficient header data. HeaderLen: {headerLen}");
            return false;
        }

        this.L4 = new L4(tcpSpanFull);

        this.Info.L4_SrcPort = tcp.SrcPort._Endian16_U();
        this.Info.L4_DestPort = tcp.DstPort._Endian16_U();

        PacketSpan<GenericHeader> payloadSpan = tcpSpanFull.GetNextSpan<GenericHeader>(ref pkt, size: tcpSpanFull.GetPayloadSize(ref pkt));

        this.L7 = new L7(payloadSpan, L7Type.GenericTCP);

        return true;
    }

    bool ParseL7_UDP(ref Packet pkt, PacketSpan<GenericHeader> payloadSpan, in UDPHeader udpHeader)
    {
        if (this.Info.L4_SrcPort == (ushort)TCPWellknownPorts.L2TP || this.Info.L4_DestPort == (ushort)TCPWellknownPorts.L2TP)
        {
            // L2TP
            if (ParseL7_L2TP(ref pkt, payloadSpan, udpHeader))
            {
                return true;
            }
        }

        // Generic
        this.L7 = new L7(payloadSpan, L7Type.GenericUDP);
        return true;
    }

    bool ParseL7_L2TP(ref Packet pkt, PacketSpan<GenericHeader> payloadSpan, in UDPHeader udpHeader)
    {
        Span<byte> span = payloadSpan.GetSpan(ref pkt);

        if (span.Length < 6) return false;

        ref L2TPHeaderForStdData l2tpStd = ref span._AsStruct<L2TPHeaderForStdData>();

        L2TPPacketFlag flags = l2tpStd.Flag;
        byte version = (byte)(l2tpStd.ReservedAndVersion & 0x0F);
        if (version != 2)
        {
            return false;
        }

        L2TPPacketParsed parsed = new L2TPPacketParsed();

        parsed.Version = version;
        parsed.Flag = flags;

        if (flags.Bit(L2TPPacketFlag.Length) && flags.Bit(L2TPPacketFlag.ControlMessage) == false && flags.Bit(L2TPPacketFlag.Offset) == false &&
            flags.Bit(L2TPPacketFlag.Sequence) == false)
        {
            // Suitable for standard data packet: parse with the structure for faster processing
            parsed.Length = l2tpStd.Length._Endian16_U();

            if (parsed.Length > span.Length || parsed.Length < sizeof(L2TPHeaderForStdData))
            {
                return false;
            }

            parsed.TunnelId = l2tpStd.TunnelId._Endian16_U();

            parsed.SessionId = l2tpStd.SessionId._Endian16_U();

            parsed.Data = payloadSpan.GetInnerSpan<GenericHeader>(ref pkt, sizeof(L2TPHeaderForStdData), parsed.Length - sizeof(L2TPHeaderForStdData));
        }
        else
        {
            // Other packets: parse normally
            SpanBuffer<byte> buf = span;

            buf.Walk(2);

            if (flags.Bit(L2TPPacketFlag.Length))
            {
                parsed.Length = buf.ReadUInt16();

                if (parsed.Length > buf.Length || parsed.Length < buf.CurrentPosition)
                {
                    return false;
                }

                buf = buf.Slice(0, parsed.Length);
            }

            parsed.TunnelId = buf.ReadUInt16();

            parsed.SessionId = buf.ReadUInt16();

            if (flags.Bit(L2TPPacketFlag.Sequence))
            {
                parsed.Ns = buf.ReadUInt16();
                parsed.Nr = buf.ReadUInt16();
            }

            if (flags.Bit(L2TPPacketFlag.Offset))
            {
                parsed.OffsetSize = buf.ReadUInt16();

                buf.Read(parsed.OffsetSize);
            }

            parsed.Data = payloadSpan.GetInnerSpan<GenericHeader>(ref pkt, buf.CurrentPosition, buf.Length - buf.CurrentPosition);
        }

        this.L7 = new L7(payloadSpan, parsed);

        if (parsed.IsControlMessage)
        {
            return true;
        }
        else
        {
            return ParseL7_L2TP_PPPData(ref pkt, parsed);
        }
    }

    bool ParseL7_L2TP_PPPData(ref Packet pkt, L2TPPacketParsed l2tp)
    {
        PacketSpan<PPPDataHeader> pppHeader = l2tp.Data.GetInnerSpan<PPPDataHeader>(ref pkt, 0);
        ref PPPDataHeader h = ref pppHeader.GetRefValue(ref pkt);

        if (h.Address != 0xff) return false;
        if (h.Control != 0x03) return false;

        ParsePPP_AsOverlay(ref pkt, pppHeader.ToGenericSpan(), h.Protocol._Endian16(), pppHeader.GetPayloadSize(ref pkt));

        return true;
    }
}
