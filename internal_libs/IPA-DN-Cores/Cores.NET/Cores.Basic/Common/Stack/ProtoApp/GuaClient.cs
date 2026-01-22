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
// Apache Guacamole Protocol Client

#if true

#pragma warning disable CA2235 // Mark all non-serializable fields

using System;
using System.Buffers;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Serialization;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

using IPA.Cores.Helper.GuaHelper;
using Newtonsoft.Json;

namespace IPA.Cores.Helper.GuaHelper
{
    public static class GuaHelper
    {
        public static string KeyboardLayoutToStr(this GuaKeyboardLayout k, bool unknownAsDefault = false)
        {
            switch (k)
            {
                case GuaKeyboardLayout.EnglishUs: return "en-us-qwerty";
                case GuaKeyboardLayout.Japanese: return "ja-jp-qwerty";
                default:
                    if (unknownAsDefault == false)
                        throw new CoresLibException($"Unknown keyboard layout value: {k}");
                    else
                        return "en-us-qwerty";
            }
        }

        public static GuaKeyboardLayout StrToKeyboardLayout(this string str, bool unknownAsDefault = false)
        {
            switch (str.ToLowerInvariant())
            {
                case "en-us-qwerty": return GuaKeyboardLayout.EnglishUs;
                case "ja-jp-qwerty": return GuaKeyboardLayout.Japanese;
                default:
                    if (unknownAsDefault == false)
                        throw new CoresLibException($"Unknown keyboard layout str: '{str}'");
                    else
                        return GuaKeyboardLayout.Japanese;
            }
        }

        public static List<Tuple<string, string>> GetKeyboardLayoutList()
        {
            List<Tuple<string, string>> ret = new List<Tuple<string, string>>();
            ret.Add(new Tuple<string, string>("ja-jp-qwerty", "Japanese Keyboard"));
            ret.Add(new Tuple<string, string>("en-us-qwerty", "English Keyboard"));
            return ret;
        }

        public static List<Tuple<string, string>> GetResizeMethodList()
        {
            List<Tuple<string, string>> ret = new List<Tuple<string, string>>();
            ret.Add(new Tuple<string, string>("display-update", "Allow Dynamic Resize"));
            ret.Add(new Tuple<string, string>("reconnect", "Always Reconnect"));
            return ret;
        }

        public static string ResizeMethodToStr(this GuaResizeMethods m, bool unknownAsDefault = false)
        {
            switch (m)
            {
                case GuaResizeMethods.DisplayUpdate: return "display-update";
                case GuaResizeMethods.Reconnect: return "reconnect";
                default:
                    if (unknownAsDefault == false)
                        throw new CoresLibException($"Unknown resize value: {m}");
                    else
                        return "display-update";
            }
        }

        public static GuaResizeMethods StrToResizeMethod(this string str, bool unknownAsDefault = false)
        {
            switch (str.ToLowerInvariant())
            {
                case "display-update": return GuaResizeMethods.DisplayUpdate;
                case "reconnect": return GuaResizeMethods.Reconnect;
                default:
                    if (unknownAsDefault == false)
                        throw new CoresLibException($"Unknown resize method str: '{str}'");
                    else
                        return GuaResizeMethods.DisplayUpdate;
            }
        }

        public static string GuaProtocolToStr(this GuaProtocol p, bool unknownAsDefault = false)
        {
            switch (p)
            {
                case GuaProtocol.Rdp: return "rdp";
                case GuaProtocol.Vnc: return "vnc";
                default:
                    if (unknownAsDefault == false)
                        throw new CoresLibException($"Unknown protocol value: {p}");
                    else
                        return "rdp";
            }
        }

        public static GuaProtocol StrToGuaProtocol(this string str, bool unknownAsDefault = false)
        {
            switch (str.ToLowerInvariant())
            {
                case "rdp": return GuaProtocol.Rdp;
                case "vnc": return GuaProtocol.Vnc;
                default:
                    if (unknownAsDefault == false)
                        throw new CoresLibException($"Unknown protocol str: '{str}'");
                    else
                        return GuaProtocol.Rdp;
            }
        }
    }
}

namespace IPA.Cores.Basic
{
    public static partial class CoresConfig
    {
        public static partial class GuaClient
        {
            public static readonly Copenhagen<int> TimeoutMsecs = 30 * 1000;

            public static readonly Copenhagen<int> MinScreenWidth = 800;
            public static readonly Copenhagen<int> MinScreenHeight = 600;

            public static readonly Copenhagen<int> DefaultScreenWidth = 1024;
            public static readonly Copenhagen<int> DefaultScreenHeight = 768;

            public static readonly Copenhagen<int> MaxScreenWidth = 5760;
            public static readonly Copenhagen<int> MaxScreenHeight = 2400;
        }
    }

    [Flags]
    public enum GuaDnFlags
    {
        None = 0,
        AlwaysWebp = 1,
    }

    [Flags]
    public enum GuaProtocol
    {
        Rdp = 0,
        Vnc,
    }

    public class GuaPacket
    {
        public string Opcode { get; set; } = "";
        public List<string> Args { get; set; } = new List<string>();

        public static GuaPacket CreateConnectPacket(KeyValueList<string, string> options, IReadOnlyList<string> supportedOptions)
        {
            List<string> tmp = new List<string>();

            for (int i = 0; i < supportedOptions.Count; i++)
            {
                string opt = supportedOptions[i];

                string value = options.Where(x => x.Key._IsSameiTrim(opt)).Select(x => x.Value).FirstOrDefault()._NonNull();

                tmp.Add(value);
            }

            return new GuaPacket { Opcode = "connect", Args = tmp, };
        }

        public override string ToString()
        {
            List<string> tmp = new List<string>();
            tmp.Add(this.Opcode);
            tmp.AddRange(this.Args);

            var data = BuildTokens(tmp);

            return data.Span._GetString_UTF8();
        }

        public async Task SendPacketAsync(Stream st, CancellationToken cancel = default)
        {
            List<string> tmp = new List<string>();
            tmp.Add(this.Opcode);
            tmp.AddRange(this.Args);

            var data = BuildTokens(tmp);

            await st.WriteAsync(data, cancel);
        }

        public static async Task<GuaPacket> RecvPacketAsync(Stream st, CancellationToken cancel = default)
        {
            List<string> tokens = await RecvAndParseTokensAsync(st, cancel);

            if (tokens.Count == 0)
            {
                throw new CoresLibException("Protocol Error: tokens.Count == 0");
            }

            GuaPacket ret = new GuaPacket
            {
                Opcode = tokens[0],
                Args = tokens.Skip(1).ToList(),
            };

            return ret;
        }

        public static MemoryBuffer<byte> BuildTokens(IEnumerable<string> tokens)
        {
            MemoryBuffer<byte> sb = new MemoryBuffer<byte>();

            int num = 0;

            foreach (string token in tokens)
            {
                if (num >= 1)
                {
                    sb.WriteStr(",");
                }
                num++;

                var data = token._GetBytes_UTF8();

                int len = data.Length;
                sb.WriteStr(len.ToString());
                sb.WriteStr(".");
                sb.Write(data);
            }
            sb.WriteStr(";");

            return sb;
        }

        public static async Task<List<string>> RecvAndParseTokensAsync(Stream st, CancellationToken cancel = default)
        {
            List<string> ret = new List<string>();

            int totalLen = 0;

            while (true)
            {
                int len = await RecvLengthAsync(st, cancel);

                totalLen += len;

                if (totalLen > Consts.Numbers.GenericMaxSize_Middle)
                {
                    throw new CoresLibException($"Protocol Error: totalLen == {totalLen}");
                }

                var data = await st._ReadAllAsync(len, cancel);
                string dataStr = data._GetString_UTF8();

                ret.Add(dataStr);

                if (ret.Count > Consts.Numbers.GenericMaxEntities_Small)
                {
                    throw new CoresLibException($"Protocol Error: ret.Count == {ret.Count}");
                }

                char c = (char)(await st.ReceiveByteAsync(cancel));
                if (c == ',')
                {
                    continue;
                }
                else if (c == ';')
                {
                    break;
                }
                else
                {
                    throw new CoresLibException($"Protocol Error: Unexpected character: '{c}' ({(int)c})");
                }
            }

            return ret;
        }

        // 0 ～ 999999 までの範囲内の数字を受信する
        public static async Task<int> RecvLengthAsync(Stream st, CancellationToken cancel = default)
        {
            StringBuilder sb = new StringBuilder();
            while (true)
            {
                char c = (char)(await st.ReceiveByteAsync(cancel));
                if (c == '.')
                {
                    break;
                }
                sb.Append(c);
                if (sb.Length >= 7)
                {
                    throw new CoresLibException("Protocol Error: sb.Length >= 7");
                }
            }
            int ret = sb.ToString()._ToInt();
            if (ret < 0)
            {
                throw new CoresLibException($"Protocol Error: ret == {ret}");
            }

            return ret;
        }
    }

    [Flags]
    public enum GuaResizeMethods
    {
        DisplayUpdate = 0,
        Reconnect,
    }

    [Flags]
    public enum GuaKeyboardLayout
    {
        EnglishUs = 0x00000409,
        Japanese = 0x00000411,
    }

    public class GuaPreference : INormalizable
    {
        public bool EnableAudio { get; set; } = true;
        public bool EnableMic { get; set; } = false;
        public bool EnableWallPaper { get; set; } = true;
        public bool EnableTheming { get; set; } = true;
        public bool EnableFontSmoothing { get; set; } = true;
        public bool EnableFullWindowDrag { get; set; } = false;
        public bool EnableDesktopComposition { get; set; } = true;
        public bool EnableMenuAnimations { get; set; } = false;
        public bool EnableAlwaysWebp { get; set; } = false;

        public bool EnableClipboardC2S { get; set; } = false;
        public bool EnableClipboardS2C { get; set; } = false;

        public bool Win_ShiftWin { get; set; } = true;
        public bool Win_Ctrl2Alt2 { get; set; } = true;
        public bool Cad_CtrlAltEnd { get; set; } = true;
        public bool Cad_CtrlAltHome { get; set; } = true;
        public bool Cad_CtrlAltBackspace { get; set; } = true;
        public bool Cad_CtrlShiftBackspace { get; set; } = true;
        public bool Tab_AltShift { get; set; } = true;
        public bool Ime_LeftCtrlSpace { get; set; } = true;
        public bool Ime_LeftShiftSpace { get; set; } = true;
        public bool Ime_OptionSpace { get; set; } = true;
        public bool Ime_ZenHan { get; set; } = true;

        [JsonIgnore]
        public GuaResizeMethods ResizeMethod { get; set; } = GuaResizeMethods.DisplayUpdate;

        [JsonIgnore]
        public GuaKeyboardLayout KeyboardLayout { get; set; } = GuaKeyboardLayout.Japanese;

        public string ResizeMethodStr { get => this.ResizeMethod.ResizeMethodToStr(); set => this.ResizeMethod = value.StrToResizeMethod(true); }
        public string KeyboardLayoutStr { get => this.KeyboardLayout.KeyboardLayoutToStr(); set => this.KeyboardLayout = value.StrToKeyboardLayout(true); }

        public bool ScreenAutoFullScreen { get; set; } = true;

        public bool ScreenAutoResize { get; set; } = true;
        public bool ScreenAutoResizeRemoteFit { get; set; } = true;

        public bool ScreenGetAutoSize { get; set; } = true;
        public int ScreenWidth { get; set; } = CoresConfig.GuaClient.DefaultScreenWidth;
        public int ScreenHeight { get; set; } = CoresConfig.GuaClient.DefaultScreenHeight;

        public bool ShowLocalMouseCursor { get; set; } = true;
        public bool ShowRemoteMouseCursor { get; set; } = true;
        public bool ShowHelpOnFullScreenUnset { get; set; } = true;
        public bool ShowOnceMsg { get; set; } = true;

        public string Username { get; set; } = "";
        [NoDebugDumpAttribute]
        public string Password { get; set; } = "";
        public string Domain { get; set; } = "";
        public string MacAddress { get; set; } = "";

        public string WoLTriggerPcid { get; set; } = "";

        public bool EnableDebug { get; set; } = false;

        public void AddToKeyValueList(KeyValueList<string, string> list)
        {
            this.Normalize();

            list.Add("domain", this.Domain);
            list.Add("username", this.Username);
            list.Add("password", this.Password);
            list.Add("disable-audio", (!this.EnableAudio)._ToBoolStrLower());
            list.Add("ignore-cert", true._ToBoolStrLower());
            list.Add("enable-wallpaper", this.EnableWallPaper._ToBoolStrLower());
            list.Add("enable-theming", this.EnableTheming._ToBoolStrLower());
            list.Add("enable-font-smoothing", this.EnableFontSmoothing._ToBoolStrLower());
            list.Add("enable-full-window-drag", this.EnableFullWindowDrag._ToBoolStrLower());
            list.Add("enable-desktop-composition", this.EnableDesktopComposition._ToBoolStrLower());
            list.Add("enable-menu-animations", this.EnableMenuAnimations._ToBoolStrLower());
            list.Add("disable-bitmap-caching", true._ToBoolStrLower());
            list.Add("disable-offscreen-caching", true._ToBoolStrLower());
            list.Add("disable-glyph-caching", true._ToBoolStrLower());
            list.Add("resize-method", ResizeMethod.ResizeMethodToStr(true));
            list.Add("client-name", "Thin Telework");
            list.Add("server-layout", this.KeyboardLayout.KeyboardLayoutToStr(true));
            list.Add("enable-audio-input", this.EnableMic._ToBoolStrLower());
        }

        public void Normalize()
        {
            this.Username = this.Username._NonNullTrim();
            this.Password = this.Password._NonNull();
            this.Domain = this.Domain._NonNullTrim();

            this.WoLTriggerPcid = this.WoLTriggerPcid._NonNullTrim();

            if (this.ScreenWidth <= 0) this.ScreenWidth = CoresConfig.GuaClient.DefaultScreenWidth;
            if (this.ScreenHeight <= 0) this.ScreenWidth = CoresConfig.GuaClient.DefaultScreenHeight;

            if (this.ScreenWidth < CoresConfig.GuaClient.MinScreenWidth) this.ScreenWidth = CoresConfig.GuaClient.MinScreenWidth;
            if (this.ScreenWidth > CoresConfig.GuaClient.MaxScreenWidth) this.ScreenWidth = CoresConfig.GuaClient.MaxScreenWidth;

            if (this.ScreenHeight < CoresConfig.GuaClient.MinScreenHeight) this.ScreenHeight = CoresConfig.GuaClient.MinScreenHeight;
            if (this.ScreenHeight > CoresConfig.GuaClient.MaxScreenHeight) this.ScreenHeight = CoresConfig.GuaClient.MaxScreenHeight;

            if (this.ScreenAutoResizeRemoteFit)
            {
                this.ResizeMethod = GuaResizeMethods.DisplayUpdate;
            }
            else
            {
                this.ResizeMethod = GuaResizeMethods.Reconnect;
            }

            if (this.Username._IsEmpty() || this.Password._IsNullOrZeroLen())
            {
                // ユーザー名またはパスワードのいずれかが空の場合、NLA 認証パラメータは一切送信しない
                this.Username = "";
                this.Password = "";
                this.Domain = "";
            }

            if (this.MacAddress._IsFilled())
            {
                this.MacAddress = Str.NormalizeMac(this.MacAddress, style: MacAddressStyle.Windows);
            }

            //if (this.ScreenAutoResizeRemoteFit)
            //{
            //    this.ScreenAutoResize = true;
            //}
        }

        public GuaPreference CloneAsDefault()
        {
            var ret = this._CloneWithJson();

            ret.Username = "";
            ret.Password = "";
            ret.Domain = "";

            return ret;
        }
    }

    public class GuaClientSettings
    {
        public GuaProtocol Protocol { get; }
        public string ServerHostname { get; }
        public int ServerPort { get; }
        public GuaPreference Preference { get; }
        public TcpIpSystem TcpIp { get; }
        public string GuacdHostname { get; }
        public int GuacdPort { get; }
        public bool EnableWebp { get; }

        public GuaClientSettings(string guacdHostname, int guacdPort, GuaProtocol protocol, string serverHostname, int serverPort, GuaPreference preference, bool enableWebp, bool audioInSupported, TcpIpSystem? tcpIp = null)
        {
            GuacdHostname = guacdHostname;
            GuacdPort = guacdPort;
            TcpIp = tcpIp ?? LocalNet;
            Protocol = protocol;
            ServerHostname = serverHostname;
            ServerPort = serverPort;
            Preference = preference._CloneWithJson();
            if (audioInSupported == false)
            {
                Preference.EnableMic = false;
            }
            EnableWebp = enableWebp;
            if (this.EnableWebp == false)
            {
                Preference.EnableAlwaysWebp = false;
            }
        }

        public void AddToKeyValueList(KeyValueList<string, string> list, string serverHostnameIfEmpty)
        {
            string hostname = this.ServerHostname;

            if (hostname._IsEmpty())
            {
                hostname = serverHostnameIfEmpty;
            }

            list.Add("hostname", hostname);

            list.Add("port", this.ServerPort.ToString());

            this.Preference.AddToKeyValueList(list);
        }
    }

    public class GuaClient : AsyncService
    {
        public GuaClientSettings Settings { get; }

        public string ConnectionId { get; private set; } = "";

        public bool ConnectedStreamMode { get; } = false;

        public GuaClient(GuaClientSettings settings)
        {
            this.Settings = settings;
        }

        public GuaClient(GuaClientSettings settings, PipeStream connectedStream)
        {
            this.Settings = settings;
            this.Stream = connectedStream;
            this.ConnectedStreamMode = true;
        }

        Once Started;

        public ConnSock? Sock { get; private set; }
        public PipeStream? Stream { get; private set; }

        public async Task<string> StartAsync(CancellationToken cancel = default)
        {
            if (Started.IsFirstCall() == false) throw new CoresLibException("StartAsync has already been called.");

            try
            {
                if (this.ConnectedStreamMode == false)
                {
                    // TCP 接続モード
                    this.Sock = await Settings.TcpIp.ConnectIPv4v6DualAsync(new TcpConnectParam(this.Settings.GuacdHostname, this.Settings.GuacdPort), cancel);
                    this.Stream = Sock.GetStream(true);
                }
                else
                {
                    // すでに接続されている Stream を用いるモード
                    this.Stream._MarkNotNull();
                }

                this.Stream.ReadTimeout = CoresConfig.GuaClient.TimeoutMsecs;
                this.Stream.WriteTimeout = CoresConfig.GuaClient.TimeoutMsecs;

                GuaPacket hello = new GuaPacket
                {
                    Opcode = "select",
                    Args = Settings.Protocol.GuaProtocolToStr()._SingleList(),
                };

                // こんにちは！
                await hello.SendPacketAsync(this.Stream, cancel);

                var args = await GuaPacket.RecvPacketAsync(this.Stream, cancel);

                if (args.Opcode._IsSamei("args") == false)
                {
                    throw new CoresLibException($"Protocol error: Received unexpected opcode: '{args.Opcode}'");
                }

                // サポートされているオプション文字列一覧を取得
                var supportedOptions = args.Args;

                // size, audio, video, image, timezone の 5 項目 (固定) を送付
                var opSize = new GuaPacket { Opcode = "size", Args = StrList(Settings.Preference.ScreenWidth, Settings.Preference.ScreenHeight, 96), };
                var opAudio = new GuaPacket { Opcode = "audio", Args = StrList("audio/L8", "audio/L16"), };
                var opVideo = new GuaPacket { Opcode = "video", Args = StrList(), };

                List<string> imgList = StrList("image/jpeg", "image/png");

                if (this.Settings.EnableWebp)
                {
                    imgList.Add("image/webp");
                }

                var opImage = new GuaPacket { Opcode = "image", Args = imgList, };
                var opTimezone = new GuaPacket { Opcode = "timezone", Args = StrList("Asia/Tokyo"), };

                await opSize.SendPacketAsync(this.Stream, cancel);
                await opAudio.SendPacketAsync(this.Stream, cancel);
                await opVideo.SendPacketAsync(this.Stream, cancel);
                await opImage.SendPacketAsync(this.Stream, cancel);
                await opTimezone.SendPacketAsync(this.Stream, cancel);

                // Connect パケットを送付
                KeyValueList<string, string> connectOptions = new KeyValueList<string, string>();
                if (this.ConnectedStreamMode == false)
                {
                    this.Settings.AddToKeyValueList(connectOptions, this.Sock!.EndPointInfo.LocalIP._NullCheck());
                }
                else
                {
                    this.Settings.AddToKeyValueList(connectOptions, "127.0.0.1");
                }
                var opConnect = GuaPacket.CreateConnectPacket(connectOptions, supportedOptions);

                if (this.ConnectedStreamMode)
                {
                    // Connect パケットは 送りません！
                    // Ready パケットは 届きません！
                    // connect パケットを文字列化して返す。
                    // これは HTML5 クライアント経由で送付
                    return opConnect.ToString();
                }

                await opConnect.SendPacketAsync(this.Stream, cancel);

                // Ready パケットを受信
                GuaPacket ready = await GuaPacket.RecvPacketAsync(this.Stream, cancel);

                if (ready.Opcode._IsSamei("ready") == false)
                {
                    throw new CoresLibException($"Protocol error: Received unexpected opcode: '{ready.Opcode}'");
                }

                this.ConnectionId = ready.Args.FirstOrDefault()._NonNullTrim();
                if (this.ConnectionId._IsEmpty())
                {
                    throw new CoresLibException($"Protocol error: Connection ID not returned.");
                }

                return "";
            }
            catch
            {
                if (this.ConnectedStreamMode == false)
                {
                    await Sock._DisposeSafeAsync();
                    await Stream._DisposeSafeAsync();
                }

                throw;
            }
        }

        protected override async Task CleanupImplAsync(Exception? ex)
        {
            try
            {
                if (this.ConnectedStreamMode == false)
                {
                    await Sock._DisposeSafeAsync();
                    await Stream._DisposeSafeAsync();
                }
            }
            finally
            {
                await base.CleanupImplAsync(ex);
            }
        }
    }

    public static class GuaWebSocketUtil
    {
        public static async Task RelayBetweenWebSocketAndStreamDuplex(Stream st, System.Net.WebSockets.WebSocket ws, int bufferSize = Consts.Numbers.DefaultLargeBufferSize, RefLong? totalBytes = null, CancellationToken cancel = default)
        {
            await using CancelWatcher w = new CancelWatcher(cancel);

            Task relayStreamToWebSocket = RelayStreamToWebSocketAsync(st, ws, bufferSize, totalBytes, w.CancelToken);
            Task relayWebSocketToStream = RelayWebSocketToStreamAsync(ws, st, bufferSize, totalBytes, w.CancelToken);

            await TaskUtil.WaitObjectsAsync(new Task[] { relayStreamToWebSocket, relayWebSocketToStream }, cancel._SingleArray());

            w.Cancel();

            await relayStreamToWebSocket._TryAwait();
            await relayWebSocketToStream._TryAwait();

            if (relayStreamToWebSocket.Exception != null) throw relayStreamToWebSocket.Exception;
            if (relayWebSocketToStream.Exception != null) throw relayWebSocketToStream.Exception;
        }

        public static async Task RelayStreamToWebSocketAsync(Stream src, System.Net.WebSockets.WebSocket dst, int bufferSize = Consts.Numbers.DefaultLargeBufferSize, RefLong? totalBytes = null, CancellationToken cancel = default)
        {
            bool isDisconnected = false;

            while (isDisconnected == false)
            {
                // 1 個のパケットを TCP で受信
                MemoryBuffer<byte> packet = new MemoryBuffer<byte>();

                while (isDisconnected == false)
                {
                    StringBuilder sb = new StringBuilder();
                    while (isDisconnected == false)
                    {
                        byte a;
                        try
                        {
                            a = (await src.ReceiveByteAsync(cancel));
                        }
                        catch (DisconnectedException)
                        {
                            isDisconnected = true;
                            break;
                        }
                        packet.WriteByte(a);
                        if (a == '.')
                        {
                            break;
                        }
                        sb.Append((char)a);
                        if (sb.Length >= 7)
                        {
                            throw new CoresLibException("Protocol Error: sb.Length >= 7");
                        }
                    }
                    if (isDisconnected)
                    {
                        break;
                    }

                    int dataSize = sb.ToString()._ToInt();
                    if (dataSize < 0)
                    {
                        throw new CoresLibException($"Protocol Error: dataSize == {dataSize}");
                    }

                    if (packet.Length > Consts.Numbers.GenericMaxSize_Middle)
                    {
                        throw new CoresLibException($"Protocol Error: packet.Length == {packet.Length}");
                    }

                    var data = await src._ReadAllAsync(dataSize, cancel);
                    packet.Write(data);

                    //$"Stream -> WS: {data._GetString_Ascii()}"._Debug();

                    byte c = (await src.ReceiveByteAsync(cancel));
                    packet.WriteByte(c);
                    if (c == ',')
                    {
                        continue;
                    }
                    else if (c == ';')
                    {
                        break;
                    }
                    else
                    {
                        throw new CoresLibException($"Protocol Error: Unexpected character: '{(char)c}' ({(int)c})");
                    }
                }

                totalBytes?.Add(packet.Length);
                await dst.SendAsync(packet, WebSocketMessageType.Text, true, cancel);
            }

            await dst.SendAsync(ReadOnlyMemory<byte>.Empty, WebSocketMessageType.Close, true, cancel);
        }

        public static async Task RelayWebSocketToStreamAsync(System.Net.WebSockets.WebSocket src, Stream dst, int bufferSize = Consts.Numbers.DefaultLargeBufferSize, RefLong? totalBytes = null, CancellationToken cancel = default)
        {
            using (MemoryHelper.FastAllocMemoryWithUsing(bufferSize, out Memory<byte> buffer))
            {
                while (true)
                {
                    var result = await src.ReceiveAsync(buffer, cancel);

                    if (result.MessageType == WebSocketMessageType.Close || result.Count <= 0)
                    {
                        break;
                    }

                    var data = buffer.Slice(0, result.Count);

                    totalBytes?.Add(data.Length);
                    await dst.WriteAsync(data, cancel);

                    //$"WS -> Stream: {data._GetString_Ascii()}"._Debug();

                    if (result.EndOfMessage)
                    {
                        await dst.FlushAsync(cancel);
                    }
                }
            }
        }
    }
}

#endif

