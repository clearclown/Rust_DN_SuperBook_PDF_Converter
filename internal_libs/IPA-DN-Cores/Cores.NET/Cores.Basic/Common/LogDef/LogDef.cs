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
using System.Collections.Generic;
using System.Reflection;
using System.Net;
using System.Net.Sockets;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

#pragma warning disable CA2235 // Mark all non-serializable fields

namespace IPA.Cores.Basic;

public partial class LogTag
{
    public const string None = "None";
    public const string SocketConnected = "SocketConnected";
    public const string SocketAccepted = "SocketAccepted";
    public const string SocketDisconnected = "SocketDisconnected";

    public partial class Data
    {
        public const string FletsBRASInfo = "FletsBRASInfo";
    }
}

public partial class LogKind
{
    public const string Default = "Default";
    public const string Data = "Data";
    public const string Access = "Access";
    public const string Socket = "Socket";
    public const string Stat = "Stat";
}

public class LogDefIPEndPoints
{
    public TcpDirectionType Direction = TcpDirectionType.Client;
    public string? LocalIP = null;
    public int LocalPort = 0;
    public string? RemoteIP = null;
    public int RemotePort = 0;

    public IPEndPoint GetLocalEndPoint() => new IPEndPoint(IPAddress.Parse(this.LocalIP!), this.LocalPort);
    public IPEndPoint GetRemoteEndPoint() => new IPEndPoint(IPAddress.Parse(this.RemoteIP!), this.RemotePort);
}

public class LogDefSslSession
{
    public bool IsServerMode;
    public string? SslProtocol;
    public string? CipherAlgorithm;
    public int CipherStrength;
    public string? HashAlgorithm;
    public int HashStrength;
    public string? KeyExchangeAlgorithm;
    public int KeyExchangeStrength;
    public string? LocalCertificateInfo;
    public string? LocalCertificateHashSHA1;
    public string? RemoteCertificateInfo;
    public string? RemoteCertificateHashSHA1;
}

[Flags]
public enum LogDefSocketAction
{
    Connected = 0,
    Disconnected,
}

[Serializable]
public class LogDefSocket
{
    public LogDefSocketAction Action;

    public string? NetworkSystem;
    public string? SockGuid;
    public string? SockType;
    public string? Direction;
    public long NativeHandle;

    public string? LocalIP;
    public string? RemoteIP;
    public int? LocalPort;
    public int? RemotePort;

    public DateTimeOffset ConnectedTime;
    public DateTimeOffset? DisconnectedTime;

    public long StreamSend;
    public long StreamRecv;
    public long DatagramSend;
    public long DatagramRecv;
}
