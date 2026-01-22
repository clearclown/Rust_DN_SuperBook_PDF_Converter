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

#if CORES_BASIC_JSON

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using System;
using System.Diagnostics.CodeAnalysis;
using static IPA.Cores.Globals.Basic;

namespace IPA.Cores.Basic;

public static partial class Util
{
    // オブジェクトのハッシュ値を計算
    public static ulong CalcObjectHashByJson(object o)
    {
        if (o == null) return 0;
        try
        {
            return Str.HashStrToLong(Json.Serialize(o, true, false, null));
        }
        catch
        {
            return 0;
        }
    }
}

// EasyCookie ユーティリティ
public static class EasyCookieUtil
{
    public static string SerializeObject<T>(T obj, bool easyEncrypt = false, string? easyEncryptPassword = null)
    {
        if (obj == null) return "";

        if (obj is INormalizable n) n.Normalize();

        MemoryBuffer<byte> buf = new MemoryBuffer<byte>();

        string json = obj._ObjectToJson<T>(EnsurePresentInterface.Yes, compact: true);
        byte[] jsonData = json._GetBytes_UTF8();

        buf.Write(jsonData);
        buf.WriteSInt64(Secure.HashSHA1AsLong(jsonData));

        Memory<byte> data = buf.Span._EasyCompress();

        if (easyEncrypt)
        {
            data = Secure.EasyEncrypt(data, easyEncryptPassword);
        }

        string cookieStr = data._Base64UrlEncode();

        cookieStr = Consts.Strings.EasyCookieValuePrefix + cookieStr;

        if (cookieStr.Length > Consts.MaxLens.MaxCookieSize)
        {
            throw new CoresLibException($"The serializing base64 length exceeds max cookie length ({cookieStr.Length} > {Consts.MaxLens.MaxCookieSize}");
        }

        return cookieStr;
    }

    [return: MaybeNull]
    public static T DeserializeObject<T>(string? cookieStr = null, bool easyDecrypt = false, string? easyDecryptPassword = null)
    {
        try
        {
            if (cookieStr._IsEmpty()) return default;

            if (cookieStr.StartsWith(Consts.Strings.EasyCookieValuePrefix) == false) return default;

            cookieStr = cookieStr._Slice(Consts.Strings.EasyCookieValuePrefix.Length);

            Memory<byte> data = cookieStr._Base64UrlDecode();

            if (easyDecrypt) data = Secure.EasyDecrypt(data, easyDecryptPassword);

            data = data._EasyDecompress();

            var jsonData = data._SliceHead(data.Length - sizeof(long));
            var hash = data._SliceTail(sizeof(long))._GetSInt64();

            if (Secure.HashSHA1AsLong(jsonData.Span) != hash)
            {
                return default;
            }

            string json = jsonData._GetString_UTF8();

            T? ret = json._JsonToObject<T>();

            if (ret is INormalizable n) n.Normalize();

            return ret;
        }
        catch
        {
            return default;
        }
    }
}

#endif // CORES_BASIC_JSON

