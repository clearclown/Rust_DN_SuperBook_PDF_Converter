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
// 
// Original Source Code:
// ARSoft.Tools.Net by Alexander Reinert
// 
// From: https://github.com/alexreinert/ARSoft.Tools.Net/tree/18b427f9f3cfacd4464152090db0515d2675d899
//
// ARSoft.Tools.Net - C# DNS client/server and SPF Library, Copyright (c) 2010-2017 Alexander Reinert (https://github.com/alexreinert/ARSoft.Tools.Net)
// 
// Copyright 2010..2017 Alexander Reinert
// 
// This file is part of the ARSoft.Tools.Net - C# DNS client/server and SPF Library (https://github.com/alexreinert/ARSoft.Tools.Net)
// 
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// 
//   http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

#if CORES_BASIC_SECURITY

#nullable disable

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
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Serialization;
using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;

using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Digests;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

namespace IPA.Cores.Basic.DnsLib;

public delegate Task AsyncEventHandler<T>(object sender, T eventArgs) where T : EventArgs;

internal static class AsyncEventHandlerExtensions
{
    public static Task RaiseAsync<T>(this AsyncEventHandler<T> eventHandler, object sender, T eventArgs)
        where T : EventArgs
    {
        if (eventHandler == null)
            return Task.FromResult(false);

        return Task.WhenAll(eventHandler.GetInvocationList().Cast<AsyncEventHandler<T>>().Select(x => x.Invoke(sender, eventArgs)));
    }
}

/// <summary>
///   <para>Extension class for encoding and decoding Base16, Base32 and Base64</para>
///   <para>
///     Defined in
///     <see cref="!:http://tools.ietf.org/html/rfc4648">RFC 4648</see>
///   </para>
/// </summary>
public static class BaseEncoding
{
    #region Helper
    private static Dictionary<char, byte> GetAlphabet(string alphabet, bool isCaseIgnored)
    {
        Dictionary<char, byte> res = new Dictionary<char, byte>(isCaseIgnored ? 2 * alphabet.Length : alphabet.Length);

        for (byte i = 0; i < alphabet.Length; i++)
        {
            res[alphabet[i]] = i;
        }

        if (isCaseIgnored)
        {
            alphabet = alphabet.ToLowerInvariant();
            for (byte i = 0; i < alphabet.Length; i++)
            {
                res[alphabet[i]] = i;
            }
        }

        return res;
    }
    #endregion

    #region Base16
    private const string _BASE16_ALPHABET = "0123456789ABCDEF";
    private static readonly char[] _base16Alphabet = _BASE16_ALPHABET.ToCharArray();
    private static readonly Dictionary<char, byte> _base16ReverseAlphabet = GetAlphabet(_BASE16_ALPHABET, true);

    /// <summary>
    ///   Decodes a Base16 string as described in <see cref="!:http://tools.ietf.org/html/rfc4648">RFC 4648</see> .
    /// </summary>
    /// <param name="inData"> An Base16 encoded string. </param>
    /// <returns> Decoded data </returns>
    public static byte[] FromBase16String(this string inData)
    {
        return inData.ToCharArray().FromBase16CharArray(0, inData.Length);
    }

    /// <summary>
    ///   Decodes a Base16 char array as described in <see cref="!:http://tools.ietf.org/html/rfc4648">RFC 4648</see> .
    /// </summary>
    /// <param name="inData"> An Base16 encoded char array. </param>
    /// <param name="offset"> An offset in inData. </param>
    /// <param name="length"> The number of elements of inData to decode. </param>
    /// <returns> Decoded data </returns>
    public static byte[] FromBase16CharArray(this char[] inData, int offset, int length)
    {
        byte[] res = new byte[length / 2];

        int inPos = offset;
        int outPos = 0;

        while (inPos < offset + length)
        {
            res[outPos++] = (byte)((_base16ReverseAlphabet[inData[inPos++]] << 4) + _base16ReverseAlphabet[inData[inPos++]]);
        }

        return res;
    }

    /// <summary>
    ///   Converts a byte array to its corresponding Base16 encoding described in
    ///   <see
    ///     cref="!:http://tools.ietf.org/html/rfc4648">
    ///     RFC 4648
    ///   </see>
    ///   .
    /// </summary>
    /// <param name="inArray"> An array of 8-bit unsigned integers. </param>
    /// <returns> Encoded string </returns>
    public static string ToBase16String(this byte[] inArray)
    {
        return inArray.ToBase16String(0, inArray.Length);
    }

    /// <summary>
    ///   Converts a byte array to its corresponding Base16 encoding described in
    ///   <see
    ///     cref="!:http://tools.ietf.org/html/rfc4648">
    ///     RFC 4648
    ///   </see>
    ///   .
    /// </summary>
    /// <param name="inArray"> An array of 8-bit unsigned integers. </param>
    /// <param name="offset"> An offset in inArray. </param>
    /// <param name="length"> The number of elements of inArray to convert. </param>
    /// <returns> Encoded string </returns>
    public static string ToBase16String(this byte[] inArray, int offset, int length)
    {
        char[] outData = new char[length * 2];

        int inPos = offset;
        int inEnd = offset + length;
        int outPos = 0;

        while (inPos < inEnd)
        {
            outData[outPos++] = _base16Alphabet[(inArray[inPos] >> 4) & 0x0f];
            outData[outPos++] = _base16Alphabet[inArray[inPos++] & 0x0f];
        }

        return new string(outData);
    }
    #endregion

    #region Base32
    private const string _BASE32_ALPHABET = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567=";
    private static readonly char[] _base32Alphabet = _BASE32_ALPHABET.ToCharArray();
    private static readonly Dictionary<char, byte> _base32ReverseAlphabet = GetAlphabet(_BASE32_ALPHABET, true);

    /// <summary>
    ///   Decodes a Base32 string as described in <see cref="!:http://tools.ietf.org/html/rfc4648">RFC 4648</see> .
    /// </summary>
    /// <param name="inData"> An Base32 encoded string. </param>
    /// <returns> Decoded data </returns>
    public static byte[] FromBase32String(this string inData)
    {
        return inData.ToCharArray().FromBase32CharArray(0, inData.Length);
    }

    /// <summary>
    ///   Decodes a Base32 char array as described in <see cref="!:http://tools.ietf.org/html/rfc4648">RFC 4648</see> .
    /// </summary>
    /// <param name="inData"> An Base32 encoded char array. </param>
    /// <param name="offset"> An offset in inData. </param>
    /// <param name="length"> The number of elements of inData to decode. </param>
    /// <returns> Decoded data </returns>
    public static byte[] FromBase32CharArray(this char[] inData, int offset, int length)
    {
        return inData.FromBase32CharArray(offset, length, _base32ReverseAlphabet);
    }

    /// <summary>
    ///   Converts a byte array to its corresponding Base32 encoding described in
    ///   <see
    ///     cref="!:http://tools.ietf.org/html/rfc4648">
    ///     RFC 4648
    ///   </see>
    ///   .
    /// </summary>
    /// <param name="inArray"> An array of 8-bit unsigned integers. </param>
    /// <returns> Encoded string </returns>
    public static string ToBase32String(this byte[] inArray)
    {
        return inArray.ToBase32String(0, inArray.Length);
    }

    /// <summary>
    ///   Converts a byte array to its corresponding Base32 encoding described in
    ///   <see
    ///     cref="!:http://tools.ietf.org/html/rfc4648">
    ///     RFC 4648
    ///   </see>
    ///   .
    /// </summary>
    /// <param name="inArray"> An array of 8-bit unsigned integers. </param>
    /// <param name="offset"> An offset in inArray. </param>
    /// <param name="length"> The number of elements of inArray to convert. </param>
    /// <returns> Encoded string </returns>
    public static string ToBase32String(this byte[] inArray, int offset, int length)
    {
        return inArray.ToBase32String(offset, length, _base32Alphabet);
    }

    private const string _BASE32_HEX_ALPHABET = "0123456789ABCDEFGHIJKLMNOPQRSTUV=";
    private static readonly char[] _base32HexAlphabet = _BASE32_HEX_ALPHABET.ToCharArray();
    private static readonly Dictionary<char, byte> _base32HexReverseAlphabet = GetAlphabet(_BASE32_HEX_ALPHABET, true);

    /// <summary>
    ///   Decodes a Base32Hex string as described in <see cref="!:http://tools.ietf.org/html/rfc4648">RFC 4648</see> .
    /// </summary>
    /// <param name="inData"> An Base32Hex encoded string. </param>
    /// <returns> Decoded data </returns>
    public static byte[] FromBase32HexString(this string inData)
    {
        return inData.ToCharArray().FromBase32HexCharArray(0, inData.Length);
    }

    /// <summary>
    ///   Decodes a Base32Hex char array as described in <see cref="!:http://tools.ietf.org/html/rfc4648">RFC 4648</see> .
    /// </summary>
    /// <param name="inData"> An Base32Hex encoded char array. </param>
    /// <param name="offset"> An offset in inData. </param>
    /// <param name="length"> The number of elements of inData to decode. </param>
    /// <returns> Decoded data </returns>
    public static byte[] FromBase32HexCharArray(this char[] inData, int offset, int length)
    {
        return inData.FromBase32CharArray(offset, length, _base32HexReverseAlphabet);
    }

    /// <summary>
    ///   Converts a byte array to its corresponding Base32Hex encoding described in
    ///   <see
    ///     cref="!:http://tools.ietf.org/html/rfc4648">
    ///     RFC 4648
    ///   </see>
    ///   .
    /// </summary>
    /// <param name="inArray"> An array of 8-bit unsigned integers. </param>
    /// <returns> Encoded string </returns>
    public static string ToBase32HexString(this byte[] inArray)
    {
        return inArray.ToBase32HexString(0, inArray.Length);
    }

    /// <summary>
    ///   Converts a byte array to its corresponding Base32Hex encoding described in
    ///   <see
    ///     cref="!:http://tools.ietf.org/html/rfc4648">
    ///     RFC 4648
    ///   </see>
    ///   .
    /// </summary>
    /// <param name="inArray"> An array of 8-bit unsigned integers. </param>
    /// <param name="offset"> An offset in inArray. </param>
    /// <param name="length"> The number of elements of inArray to convert. </param>
    /// <returns> Encoded string </returns>
    public static string ToBase32HexString(this byte[] inArray, int offset, int length)
    {
        return inArray.ToBase32String(offset, length, _base32HexAlphabet);
    }

    private static byte[] FromBase32CharArray(this char[] inData, int offset, int length, Dictionary<char, byte> alphabet)
    {
        int paddingCount = 0;
        while (paddingCount < 6)
        {
            if (alphabet[inData[offset + length - paddingCount - 1]] != 32)
                break;

            paddingCount++;
        }

        int remain;
        switch (paddingCount)
        {
            case 6:
                remain = 1;
                break;
            case 4:
                remain = 2;
                break;
            case 3:
                remain = 3;
                break;
            case 1:
                remain = 4;
                break;
            default:
                remain = 0;
                break;
        }

        int outSafeLength = (length - paddingCount) / 8 * 5;

        byte[] res = new byte[outSafeLength + remain];

        int inPos = offset;
        int outPos = 0;

        byte[] buffer = new byte[8];

        while (outPos < outSafeLength)
        {
            for (int i = 0; i < 8; i++)
            {
                buffer[i] = alphabet[inData[inPos++]];
            }

            res[outPos++] = (byte)((buffer[0] << 3) | ((buffer[1] >> 2) & 0x07));
            res[outPos++] = (byte)(((buffer[1] >> 6) & 0xc0) | (buffer[2] << 1) | ((buffer[3] >> 4) & 0x01));
            res[outPos++] = (byte)(((buffer[3] << 4) & 0xf0) | ((buffer[4] >> 1) & 0x0f));
            res[outPos++] = (byte)(((buffer[4] << 7) & 0x80) | (buffer[5] << 2) | ((buffer[6] >> 3) & 0x03));
            res[outPos++] = (byte)(((buffer[6] << 5) & 0xe0) | buffer[7]);
        }

        if (remain > 0)
        {
            for (int i = 0; i < 8 - paddingCount; i++)
            {
                buffer[i] = alphabet[inData[inPos++]];
            }

            switch (remain)
            {
                case 1:
                    res[outPos] = (byte)((buffer[0] << 3) | ((buffer[1] >> 2) & 0x07));
                    break;
                case 2:
                    res[outPos++] = (byte)((buffer[0] << 3) | ((buffer[1] >> 2) & 0x07));
                    res[outPos] = (byte)(((buffer[1] >> 6) & 0xc0) | (buffer[2] << 1) | ((buffer[3] >> 4) & 0x01));
                    break;
                case 3:
                    res[outPos++] = (byte)((buffer[0] << 3) | ((buffer[1] >> 2) & 0x07));
                    res[outPos++] = (byte)(((buffer[1] >> 6) & 0xc0) | (buffer[2] << 1) | ((buffer[3] >> 4) & 0x01));
                    res[outPos] = (byte)(((buffer[3] << 4) & 0xf0) | ((buffer[4] >> 1) & 0x0f));
                    break;
                case 4:
                    res[outPos++] = (byte)((buffer[0] << 3) | ((buffer[1] >> 2) & 0x07));
                    res[outPos++] = (byte)(((buffer[1] >> 6) & 0xc0) | (buffer[2] << 1) | ((buffer[3] >> 4) & 0x01));
                    res[outPos++] = (byte)(((buffer[3] << 4) & 0xf0) | ((buffer[4] >> 1) & 0x0f));
                    res[outPos] = (byte)(((buffer[4] << 7) & 0x80) | (buffer[5] << 2) | ((buffer[6] >> 3) & 0x03));
                    break;
            }
        }

        return res;
    }

    private static string ToBase32String(this byte[] inArray, int offset, int length, char[] alphabet)
    {
        int inRemain = length % 5;
        int inSafeEnd = offset + length - inRemain;

        int outLength = length / 5 * 8 + ((inRemain == 0) ? 0 : 8);

        char[] outData = new char[outLength];
        int outPos = 0;

        int inPos = offset;
        while (inPos < inSafeEnd)
        {
            outData[outPos++] = alphabet[(inArray[inPos] & 0xf8) >> 3];
            outData[outPos++] = alphabet[((inArray[inPos] & 0x07) << 2) | ((inArray[++inPos] & 0xc0) >> 6)];
            outData[outPos++] = alphabet[((inArray[inPos] & 0x3e) >> 1)];
            outData[outPos++] = alphabet[((inArray[inPos] & 0x01) << 4) | ((inArray[++inPos] & 0xf0) >> 4)];
            outData[outPos++] = alphabet[((inArray[inPos] & 0x0f) << 1) | ((inArray[++inPos] & 0x80) >> 7)];
            outData[outPos++] = alphabet[((inArray[inPos] & 0x7c) >> 2)];
            outData[outPos++] = alphabet[((inArray[inPos] & 0x03) << 3) | ((inArray[++inPos] & 0xe0) >> 5)];
            outData[outPos++] = alphabet[inArray[inPos++] & 0x1f];
        }

        switch (inRemain)
        {
            case 1:
                outData[outPos++] = alphabet[(inArray[inPos] & 0xf8) >> 3];
                outData[outPos++] = alphabet[((inArray[inPos] & 0x07) << 2)];
                outData[outPos++] = alphabet[32];
                outData[outPos++] = alphabet[32];
                outData[outPos++] = alphabet[32];
                outData[outPos++] = alphabet[32];
                outData[outPos++] = alphabet[32];
                outData[outPos] = alphabet[32];
                break;
            case 2:
                outData[outPos++] = alphabet[(inArray[inPos] & 0xf8) >> 3];
                outData[outPos++] = alphabet[((inArray[inPos] & 0x07) << 2) | ((inArray[++inPos] & 0xc0) >> 6)];
                outData[outPos++] = alphabet[((inArray[inPos] & 0x3e) >> 1)];
                outData[outPos++] = alphabet[((inArray[inPos] & 0x01) << 4)];
                outData[outPos++] = alphabet[32];
                outData[outPos++] = alphabet[32];
                outData[outPos++] = alphabet[32];
                outData[outPos] = alphabet[32];
                break;
            case 3:
                outData[outPos++] = alphabet[(inArray[inPos] & 0xf8) >> 3];
                outData[outPos++] = alphabet[((inArray[inPos] & 0x07) << 2) | ((inArray[++inPos] & 0xc0) >> 6)];
                outData[outPos++] = alphabet[((inArray[inPos] & 0x3e) >> 1)];
                outData[outPos++] = alphabet[((inArray[inPos] & 0x01) << 4) | ((inArray[++inPos] & 0xf0) >> 4)];
                outData[outPos++] = alphabet[((inArray[inPos] & 0x0f) << 1)];
                outData[outPos++] = alphabet[32];
                outData[outPos++] = alphabet[32];
                outData[outPos] = alphabet[32];
                break;
            case 4:
                outData[outPos++] = alphabet[(inArray[inPos] & 0xf8) >> 3];
                outData[outPos++] = alphabet[((inArray[inPos] & 0x07) << 2) | ((inArray[++inPos] & 0xc0) >> 6)];
                outData[outPos++] = alphabet[((inArray[inPos] & 0x3e) >> 1)];
                outData[outPos++] = alphabet[((inArray[inPos] & 0x01) << 4) | ((inArray[++inPos] & 0xf0) >> 4)];
                outData[outPos++] = alphabet[((inArray[inPos] & 0x0f) << 1) | ((inArray[++inPos] & 0x80) >> 7)];
                outData[outPos++] = alphabet[((inArray[inPos] & 0x7c) >> 2)];
                outData[outPos++] = alphabet[((inArray[inPos] & 0x03) << 3)];
                outData[outPos] = alphabet[32];
                break;
        }

        return new string(outData);
    }
    #endregion

    #region Base64
    private const string _BASE64_ALPHABET = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/=";
    private static readonly char[] _base64Alphabet = _BASE64_ALPHABET.ToCharArray();
    private static readonly Dictionary<char, byte> _base64ReverseAlphabet = GetAlphabet(_BASE64_ALPHABET, false);

    /// <summary>
    ///   Decodes a Base64 string as described in <see cref="!:http://tools.ietf.org/html/rfc4648">RFC 4648</see> .
    /// </summary>
    /// <param name="inData"> An Base64 encoded string. </param>
    /// <returns> Decoded data </returns>
    public static byte[] FromBase64String(this string inData)
    {
        return inData.ToCharArray().FromBase64CharArray(0, inData.Length);
    }

    /// <summary>
    ///   Decodes a Base64 char array as described in <see cref="!:http://tools.ietf.org/html/rfc4648">RFC 4648</see> .
    /// </summary>
    /// <param name="inData"> An Base64 encoded char array. </param>
    /// <param name="offset"> An offset in inData. </param>
    /// <param name="length"> The number of elements of inData to decode. </param>
    /// <returns> Decoded data </returns>
    public static byte[] FromBase64CharArray(this char[] inData, int offset, int length)
    {
        return inData.FromBase64CharArray(offset, length, _base64ReverseAlphabet);
    }

    /// <summary>
    ///   Converts a byte array to its corresponding Base64 encoding described in
    ///   <see
    ///     cref="!:http://tools.ietf.org/html/rfc4648">
    ///     RFC 4648
    ///   </see>
    ///   .
    /// </summary>
    /// <param name="inArray"> An array of 8-bit unsigned integers. </param>
    /// <returns> Encoded string </returns>
    public static string ToBase64String(this byte[] inArray)
    {
        return inArray.ToBase64String(0, inArray.Length);
    }

    /// <summary>
    ///   Converts a byte array to its corresponding Base64 encoding described in
    ///   <see
    ///     cref="!:http://tools.ietf.org/html/rfc4648">
    ///     RFC 4648
    ///   </see>
    ///   .
    /// </summary>
    /// <param name="inArray"> An array of 8-bit unsigned integers. </param>
    /// <param name="offset"> An offset in inArray. </param>
    /// <param name="length"> The number of elements of inArray to convert. </param>
    /// <returns> Encoded string </returns>
    public static string ToBase64String(this byte[] inArray, int offset, int length)
    {
        return inArray.ToBase64String(offset, length, _base64Alphabet);
    }

    private const string _BASE64_URL_ALPHABET = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_=";
    private static readonly char[] _base64UrlAlphabet = _BASE64_URL_ALPHABET.ToCharArray();
    private static readonly Dictionary<char, byte> _base64UrlReverseAlphabet = GetAlphabet(_BASE64_URL_ALPHABET, false);

    /// <summary>
    ///   Decodes a Base64Url string as described in <see cref="!:http://tools.ietf.org/html/rfc4648">RFC 4648</see> .
    /// </summary>
    /// <param name="inData"> An Base64Url encoded string. </param>
    /// <returns> Decoded data </returns>
    public static byte[] FromBase64UrlString(this string inData)
    {
        return inData.ToCharArray().FromBase64UrlCharArray(0, inData.Length);
    }

    /// <summary>
    ///   Decodes a Base64Url char array as described in <see cref="!:http://tools.ietf.org/html/rfc4648">RFC 4648</see> .
    /// </summary>
    /// <param name="inData"> An Base64Url encoded char array. </param>
    /// <param name="offset"> An offset in inData. </param>
    /// <param name="length"> The number of elements of inData to decode. </param>
    /// <returns> Decoded data </returns>
    public static byte[] FromBase64UrlCharArray(this char[] inData, int offset, int length)
    {
        return inData.FromBase64CharArray(offset, length, _base64UrlReverseAlphabet);
    }

    /// <summary>
    ///   Converts a byte array to its corresponding Base64Url encoding described in
    ///   <see
    ///     cref="!:http://tools.ietf.org/html/rfc4648">
    ///     RFC 4648
    ///   </see>
    ///   .
    /// </summary>
    /// <param name="inArray"> An array of 8-bit unsigned integers. </param>
    /// <returns> Encoded string </returns>
    public static string ToBase64UrlString(this byte[] inArray)
    {
        return inArray.ToBase64UrlString(0, inArray.Length);
    }

    /// <summary>
    ///   Converts a byte array to its corresponding Base64Url encoding described in
    ///   <see
    ///     cref="!:http://tools.ietf.org/html/rfc4648">
    ///     RFC 4648
    ///   </see>
    ///   .
    /// </summary>
    /// <param name="inArray"> An array of 8-bit unsigned integers. </param>
    /// <param name="offset"> An offset in inArray. </param>
    /// <param name="length"> The number of elements of inArray to convert. </param>
    /// <returns> Encoded string </returns>
    public static string ToBase64UrlString(this byte[] inArray, int offset, int length)
    {
        return inArray.ToBase64String(offset, length, _base64UrlAlphabet);
    }

    private static byte[] FromBase64CharArray(this char[] inData, int offset, int length, Dictionary<char, byte> alphabet)
    {
        int paddingCount;
        int remain;

        if (alphabet[inData[offset + length - 2]] == 64)
        {
            paddingCount = 2;
            remain = 1;
        }
        else if (alphabet[inData[offset + length - 1]] == 64)
        {
            paddingCount = 1;
            remain = 2;
        }
        else
        {
            paddingCount = 0;
            remain = 0;
        }

        int outSafeLength = (length - paddingCount) / 4 * 3;

        byte[] res = new byte[outSafeLength + remain];

        int inPos = offset;
        int outPos = 0;

        byte[] buffer = new byte[4];

        while (outPos < outSafeLength)
        {
            for (int i = 0; i < 4; i++)
            {
                buffer[i] = alphabet[inData[inPos++]];
            }

            res[outPos++] = (byte)((buffer[0] << 2) | ((buffer[1] >> 4) & 0x03));
            res[outPos++] = (byte)(((buffer[1] << 4) & 0xf0) | ((buffer[2] >> 2) & 0x0f));
            res[outPos++] = (byte)(((buffer[2] << 6) & 0xc0) | (buffer[3] & 0x3f));
        }

        if (remain > 0)
        {
            for (int i = 0; i < 4 - paddingCount; i++)
            {
                buffer[i] = alphabet[inData[inPos++]];
            }

            switch (remain)
            {
                case 1:
                    res[outPos] = (byte)((buffer[0] << 2) | ((buffer[1] >> 4) & 0x03));
                    break;
                case 2:
                    res[outPos++] = (byte)((buffer[0] << 2) | ((buffer[1] >> 4) & 0x03));
                    res[outPos] = (byte)(((buffer[1] << 4) & 0xf0) | ((buffer[2] >> 2) & 0x0f));
                    break;
            }
        }

        return res;
    }

    private static string ToBase64String(this byte[] inArray, int offset, int length, char[] alphabet)
    {
        int inRemain = length % 3;
        int inSafeEnd = offset + length - inRemain;

        int outLength = length / 3 * 4 + ((inRemain == 0) ? 0 : 4);

        char[] outData = new char[outLength];
        int outPos = 0;

        int inPos = offset;
        while (inPos < inSafeEnd)
        {
            outData[outPos++] = alphabet[(inArray[inPos] & 0xfc) >> 2];
            outData[outPos++] = alphabet[((inArray[inPos] & 0x03) << 4) | ((inArray[++inPos] & 0xf0) >> 4)];
            outData[outPos++] = alphabet[((inArray[inPos] & 0x0f) << 2) | ((inArray[++inPos] & 0xc0) >> 6)];
            outData[outPos++] = alphabet[inArray[inPos++] & 0x3f];
        }

        switch (inRemain)
        {
            case 1:
                outData[outPos++] = alphabet[(inArray[inPos] & 0xfc) >> 2];
                outData[outPos++] = alphabet[(inArray[inPos] & 0x03) << 4];
                outData[outPos++] = alphabet[64];
                outData[outPos] = alphabet[64];
                break;
            case 2:
                outData[outPos++] = alphabet[(inArray[inPos] & 0xfc) >> 2];
                outData[outPos++] = alphabet[((inArray[inPos] & 0x03) << 4) | ((inArray[++inPos] & 0xf0) >> 4)];
                outData[outPos++] = alphabet[(inArray[inPos] & 0x0f) << 2];
                outData[outPos] = alphabet[64];
                break;
        }

        return new string(outData);
    }
    #endregion
}

/// <summary>
///   Represents a domain name
/// </summary>
public class DomainName : IEquatable<DomainName>, IComparable<DomainName>
{
    private readonly ReadOnlyMemory<string> _labels;

    /// <summary>
    ///   The DNS root name (.)
    /// </summary>
    public static DomainName Root { get; } = new DomainName(new string[] { });

    internal static DomainName Asterisk { get; } = new DomainName(new[] { "*" });

    /// <summary>
    ///   Creates a new instance of the DomainName class
    /// </summary>
    /// <param name="labels">The labels of the DomainName</param>
    public DomainName(ReadOnlyMemory<string> labels)
    {
        _labels = labels;
    }

    internal DomainName(string label, DomainName parent)
    {
        var labels = new string[1 + parent.LabelCount];

        labels[0] = label;
        Array.Copy(parent._labels.ToArray(), 0, labels, 1, parent.LabelCount);

        this._labels = labels;
    }

    /// <summary>
    ///   Gets the labels of the domain name
    /// </summary>
    [JsonIgnore]
    public ReadOnlyMemory<string> Labels => _labels;

    public string[] LabelStrings => Labels.ToArray();

    /// <summary>
    ///   Gets the count of labels this domain name contains
    /// </summary>
    public int LabelCount => _labels.Length;

    internal int MaximumRecordDataLength
    {
        get
        {
            var span = _labels.Span;
            int ret = span.Length;
            foreach (var item in span)
            {
                ret += item.Length;
            }
            return ret;
        }
    }

    // ReSharper disable once InconsistentNaming
    internal DomainName Add0x20Bits()
    {
        string[] newLabels = new string[LabelCount];

        var span = Labels.Span;
        int count = span.Length;

        for (int i = 0; i < count; i++)
        {
            newLabels[i] = span[i].Add0x20Bits();
        }

        return new DomainName(newLabels) { _hashCode = _hashCode };
    }

    /// <summary>
    ///   Gets a parent zone of the domain name
    /// </summary>
    /// <param name="removeLabels">The number of labels to be removed</param>
    /// <returns>The DomainName of the parent zone</returns>
    public DomainName GetParentName(int removeLabels = 1)
    {
        if (removeLabels < 0)
            throw new ArgumentOutOfRangeException(nameof(removeLabels));

        if (removeLabels > LabelCount)
            throw new ArgumentOutOfRangeException(nameof(removeLabels));

        if (removeLabels == 0)
            return this;

        var newLabels = _labels.Slice(removeLabels, LabelCount - removeLabels);

        //string[] newLabels = new string[LabelCount - removeLabels];
        //Array.Copy(_labels, removeLabels, newLabels, 0, newLabels.Length);

        return new DomainName(newLabels);
    }

    /// <summary>
    ///   Returns if with domain name equals an other domain name or is a child of it
    /// </summary>
    /// <param name="domainName">The possible equal or parent domain name</param>
    /// <returns>true, if the domain name equals the other domain name or is a child of it; otherwise, false</returns>
    public bool IsEqualOrSubDomainOf(DomainName domainName)
    {
        if (Equals(domainName))
            return true;

        if (domainName.LabelCount >= LabelCount)
            return false;

        return GetParentName(LabelCount - domainName.LabelCount).Equals(domainName);
    }

    /// <summary>
    ///   Returns if with domain name is a child of an other domain name
    /// </summary>
    /// <param name="domainName">The possible parent domain name</param>
    /// <returns>true, if the domain name is a child of the other domain; otherwise, false</returns>
    public bool IsSubDomainOf(DomainName domainName)
    {
        if (domainName.LabelCount >= LabelCount)
            return false;

        return GetParentName(LabelCount - domainName.LabelCount).Equals(domainName);
    }

    internal byte[] GetNSec3Hash(NSec3HashAlgorithm algorithm, int iterations, byte[] salt)
    {
        IDigest digest;

        switch (algorithm)
        {
            case NSec3HashAlgorithm.Sha1:
                digest = new Sha1Digest();
                break;

            default:
                throw new NotSupportedException();
        }

        byte[] buffer = new byte[Math.Max(MaximumRecordDataLength + 1, digest.GetDigestSize()) + salt.Length];

        int length = 0;

        DnsMessageBase.EncodeDomainName(buffer, 0, ref length, this, null, true);

        for (int i = 0; i <= iterations; i++)
        {
            DnsMessageBase.EncodeByteArray(buffer, ref length, salt);

            digest.BlockUpdate(buffer, 0, length);

            digest.DoFinal(buffer, 0);
            length = digest.GetDigestSize();
        }

        byte[] res = new byte[length];
        Buffer.BlockCopy(buffer, 0, res, 0, length);

        return res;
    }

    internal DomainName GetNsec3HashName(NSec3HashAlgorithm algorithm, int iterations, byte[] salt, DomainName zoneApex)
    {
        return new DomainName(GetNSec3Hash(algorithm, iterations, salt).ToBase32HexString(), zoneApex);
    }

    internal static DomainName ParseFromMasterfile(string s)
    {
        if (s == ".")
            return Root;

        List<string> labels = new List<string>();

        int lastOffset = 0;

        for (int i = 0; i < s.Length; ++i)
        {
            if (s[i] == '.' && (i == 0 || s[i - 1] != '\\'))
            {
                labels.Add(s.Substring(lastOffset, i - lastOffset).FromMasterfileLabelRepresentation());
                lastOffset = i + 1;
            }
        }
        labels.Add(s.Substring(lastOffset, s.Length - lastOffset).FromMasterfileLabelRepresentation());

        if (labels[labels.Count - 1] == String.Empty)
            labels.RemoveAt(labels.Count - 1);

        return new DomainName(labels.ToArray());
    }

    /// <summary>
    ///   Parses the string representation of a domain name
    /// </summary>
    /// <param name="s">The string representation of the domain name to parse</param>
    /// <returns>A new instance of the DomainName class</returns>
    public static DomainName Parse(string s)
    {
        DomainName res;

        if (TryParse(s, out res))
            return res;

        throw new ArgumentException("Domain name could not be parsed", nameof(s));
    }

    public static DomainName ParseWildcardAllow(string fqdn)
    {
        fqdn = fqdn._NonNullTrim().ToLowerInvariant();
        string[] tokens = fqdn.Split(".", StringSplitOptions.RemoveEmptyEntries);

        return new DomainName(tokens);
    }

    /// <summary>
    ///   Parses the string representation of a domain name
    /// </summary>
    /// <param name="s">The string representation of the domain name to parse</param>
    /// <param name="name">
    ///   When this method returns, contains a DomainName instance representing s or null, if s could not be
    ///   parsed
    /// </param>
    /// <returns>true, if s was parsed successfully; otherwise, false</returns>
    public static bool TryParse(string s, out DomainName name)
    {
        if (s == ".")
        {
            name = Root;
            return true;
        }

        List<string> labels = new List<string>();

        int lastOffset = 0;

        string label;

        for (int i = 0; i < s.Length; ++i)
        {
            if (s[i] == '.' && (i == 0 || s[i - 1] != '\\'))
            {
                if (TryParseLabel(s.Substring(lastOffset, i - lastOffset), out label))
                {
                    labels.Add(label);
                    lastOffset = i + 1;
                }
                else
                {
                    name = null;
                    return false;
                }
            }
        }

        if (s.Length == lastOffset)
        {
            // empty label --> name ends with dot
        }
        else if (TryParseLabel(s.Substring(lastOffset, s.Length - lastOffset), out label))
        {
            labels.Add(label);
        }
        else
        {
            name = null;
            return false;
        }

        name = new DomainName(labels.ToArray());
        return true;
    }

    private static readonly IdnMapping _idnParser = new IdnMapping() { UseStd3AsciiRules = true };
    private static readonly Regex _asciiNameRegex = new Regex("^[a-zA-Z0-9_-]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static bool TryParseLabel(string s, out string label)
    {
        try
        {
            if (_asciiNameRegex.IsMatch(s))
            {
                label = s;
                return true;
            }
            else
            {
                label = _idnParser.GetAscii(s);
                return true;
            }
        }
        catch
        {
            label = null;
            return false;
        }
    }

    private string _toString;

    /// <summary>
    ///   Returns the string representation of the domain name
    /// </summary>
    /// <returns>The string representation of the domain name</returns>
    public override string ToString()
    {
        if (_toString != null)
            return _toString;

        return (_toString = String.Join(".", _labels.Span.ToArray().Select(x => x.ToMasterfileLabelRepresentation(true))) + ".");
    }

    private string _toNormalizedFqdnFast;

    public string ToNormalizedFqdnFast()
    {
        if (_toNormalizedFqdnFast != null)
            return _toNormalizedFqdnFast;

        var span = _labels.Span;
        int lenEstimated = 0;
        foreach (var token in span)
        {
            lenEstimated += token.Length;
            lenEstimated += 1;
        }

        return (_toNormalizedFqdnFast = _labels.Span._Combine(".", true, estimatedLength: lenEstimated).ToLowerInvariant());
    }

    private int? _hashCode;

    /// <summary>
    ///   Returns the hash code for this domain name
    /// </summary>
    /// <returns>The hash code for this domain name</returns>
    [SuppressMessage("ReSharper", "NonReadonlyMemberInGetHashCode")]
    [MethodImpl(Inline)]
    public override int GetHashCode()
    {
        if (_hashCode.HasValue)
            return _hashCode.Value;

        var span = _labels.Span;

        int count = span.Length;
        int hash = count;

        for (int i = 0; i < count; i++)
        {
            unchecked
            {
                hash = hash * 17 + span[i].GetHashCode(StringComparison.OrdinalIgnoreCase);
            }
        }

        return (_hashCode = hash).Value;
    }

    /// <summary>
    ///   Concatinates two names
    /// </summary>
    /// <param name="name1">The left name</param>
    /// <param name="name2">The right name</param>
    /// <returns>A new domain name</returns>
    public static DomainName operator +(DomainName name1, DomainName name2)
    {
        string[] newLabels = new string[name1.LabelCount + name2.LabelCount];

        Array.Copy(name1._labels.ToArray(), newLabels, name1.LabelCount);
        Array.Copy(name2._labels.ToArray(), 0, newLabels, name1.LabelCount, name2.LabelCount);

        return new DomainName(newLabels);
    }

    /// <summary>
    ///   Checks, whether two names are identical (case sensitive)
    /// </summary>
    /// <param name="name1">The first name</param>
    /// <param name="name2">The second name</param>
    /// <returns>true, if the names are identical</returns>
    public static bool operator ==(DomainName name1, DomainName name2)
    {
        if (ReferenceEquals(name1, name2))
            return true;

        if (ReferenceEquals(name1, null))
            return false;

        return name1.Equals(name2, false);
    }

    /// <summary>
    ///   Checks, whether two names are not identical (case sensitive)
    /// </summary>
    /// <param name="name1">The first name</param>
    /// <param name="name2">The second name</param>
    /// <returns>true, if the names are not identical</returns>
    public static bool operator !=(DomainName name1, DomainName name2)
    {
        return !(name1 == name2);
    }

    /// <summary>
    ///   Checks, whether this name is equal to an other object (case insensitive)
    /// </summary>
    /// <param name="obj">The other object</param>
    /// <returns>true, if the names are equal</returns>
    [MethodImpl(Inline)]
    public override bool Equals(object obj)
    {
        return Equals(obj as DomainName);
    }

    /// <summary>
    ///   Checks, whether this name is equal to an other name (case insensitive)
    /// </summary>
    /// <param name="other">The other name</param>
    /// <returns>true, if the names are equal</returns>
    [MethodImpl(Inline)]
    public bool Equals(DomainName other)
    {
        return Equals(other, true);
    }

    /// <summary>
    ///   Checks, whether this name is equal to an other name
    /// </summary>
    /// <param name="other">The other name</param>
    /// <param name="ignoreCase">true, if the case should ignored on checking</param>
    /// <returns>true, if the names are equal</returns>
    [MethodImpl(Inline)]
    public bool Equals(DomainName other, bool ignoreCase)
    {
        if (ReferenceEquals(other, null))
            return false;

        var span1 = _labels.Span;
        int count1 = span1.Length;

        var span2 = other._labels.Span;
        int count2 = span2.Length;

        if (count1 != count2)
            return false;

        if (_hashCode.HasValue && other._hashCode.HasValue && (_hashCode != other._hashCode))
            return false;

        StringComparison comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        for (int i = 0; i < count1; i++)
        {
            if (!String.Equals(span1[i], span2[i], comparison))
                return false;
        }

        return true;
    }

    /// <summary>
    ///   Compares the current instance with another name and returns an integer that indicates whether the current instance
    ///   precedes, follows, or occurs in the same position in the sort order as the other name.
    /// </summary>
    /// <param name="other">A name to compare with this instance.</param>
    /// <returns>A value that indicates the relative order of the objects being compared.</returns>
    public int CompareTo(DomainName other)
    {
        var span1 = this._labels.Span;
        int count1 = span1.Length;

        var span2 = other._labels.Span;
        int count2 = span2.Length;

        int minCount = Math.Min(count1, count2);

        for (int i = 1; i <= minCount; i++)
        {
            int labelCompare = String.Compare(span1[count1 - i], span2[count2 - i], StringComparison.OrdinalIgnoreCase);

            if (labelCompare != 0)
                return labelCompare;
        }

        return count1.CompareTo(count2);
    }
}

internal static class EnumHelper<T>
    where T : struct
{
    private static readonly Dictionary<T, string> _names;
    private static readonly Dictionary<string, T> _values;

    static EnumHelper()
    {
        string[] names = Enum.GetNames(typeof(T));
        T[] values = (T[])Enum.GetValues(typeof(T));

        _names = new Dictionary<T, string>(names.Length);
        _values = new Dictionary<string, T>(names.Length * 2);

        for (int i = 0; i < names.Length; i++)
        {
            _names[values[i]] = names[i];
            _values[names[i]] = values[i];
            _values[names[i].ToLowerInvariant()] = values[i];
        }
    }

    public static bool TryParse(string s, bool ignoreCase, out T value)
    {
        if (String.IsNullOrEmpty(s))
        {
            value = default(T);
            return false;
        }

        return _values.TryGetValue((ignoreCase ? s.ToLowerInvariant() : s), out value);
    }

    public static string ToString(T value)
    {
        string res;
        return _names.TryGetValue(value, out res) ? res : Convert.ToInt64(value).ToString();
    }

    public static Dictionary<T, string> Names => _names;

    internal static T Parse(string s, bool ignoreCase, T defaultValue)
    {
        T res;
        return TryParse(s, ignoreCase, out res) ? res : defaultValue;
    }

    internal static T Parse(string s, bool ignoreCase)
    {
        T res;

        if (TryParse(s, ignoreCase, out res))
            return res;

        throw new ArgumentOutOfRangeException(nameof(s));
    }
}

internal static class EventHandlerExtensions
{
    public static void Raise<T>(this EventHandler<T> eventHandler, object sender, T eventArgs)
        where T : EventArgs
    {
        eventHandler?.Invoke(sender, eventArgs);
    }
}

/// <summary>
///   Extension class for the <see cref="IPAddress" /> class
/// </summary>
public static class IPAddressExtensions
{
    /// <summary>
    ///   Reverses the order of the bytes of an IPAddress
    /// </summary>
    /// <param name="ipAddress"> Instance of the IPAddress, that should be reversed </param>
    /// <returns> New instance of IPAddress with reversed address </returns>
    public static IPAddress Reverse(this IPAddress ipAddress)
    {
        if (ipAddress == null)
            throw new ArgumentNullException(nameof(ipAddress));

        byte[] addressBytes = ipAddress.GetAddressBytes();
        byte[] res = new byte[addressBytes.Length];

        for (int i = 0; i < res.Length; i++)
        {
            res[i] = addressBytes[addressBytes.Length - i - 1];
        }

        return new IPAddress(res);
    }

    /// <summary>
    ///   Gets the network address for a specified IPAddress and netmask
    /// </summary>
    /// <param name="ipAddress"> IPAddress, for that the network address should be returned </param>
    /// <param name="netmask"> Netmask, that should be used </param>
    /// <returns> New instance of IPAddress with the network address assigend </returns>
    public static IPAddress GetNetworkAddress(this IPAddress ipAddress, IPAddress netmask)
    {
        if (ipAddress == null)
            throw new ArgumentNullException(nameof(ipAddress));

        if (netmask == null)
            throw new ArgumentNullException(nameof(netmask));

        if (ipAddress.AddressFamily != netmask.AddressFamily)
            throw new ArgumentOutOfRangeException(nameof(netmask), "Protocoll version of ipAddress and netmask do not match");

        byte[] resultBytes = ipAddress.GetAddressBytes();
        byte[] ipAddressBytes = ipAddress.GetAddressBytes();
        byte[] netmaskBytes = netmask.GetAddressBytes();

        for (int i = 0; i < netmaskBytes.Length; i++)
        {
            resultBytes[i] = (byte)(ipAddressBytes[i] & netmaskBytes[i]);
        }

        return new IPAddress(resultBytes);
    }

    /// <summary>
    ///   Gets the network address for a specified IPAddress and netmask
    /// </summary>
    /// <param name="ipAddress"> IPAddress, for that the network address should be returned </param>
    /// <param name="netmask"> Netmask in CIDR format </param>
    /// <returns> New instance of IPAddress with the network address assigend </returns>
    public static IPAddress GetNetworkAddress(this IPAddress ipAddress, int netmask)
    {
        if (ipAddress == null)
            throw new ArgumentNullException(nameof(ipAddress));

        if ((ipAddress.AddressFamily == AddressFamily.InterNetwork) && ((netmask < 0) || (netmask > 32)))
            throw new ArgumentException("Netmask have to be in range of 0 to 32 on IPv4 addresses", nameof(netmask));

        if ((ipAddress.AddressFamily == AddressFamily.InterNetworkV6) && ((netmask < 0) || (netmask > 128)))
            throw new ArgumentException("Netmask have to be in range of 0 to 128 on IPv6 addresses", nameof(netmask));

        byte[] ipAddressBytes = ipAddress.GetAddressBytes();

        for (int i = 0; i < ipAddressBytes.Length; i++)
        {
            if (netmask >= 8)
            {
                netmask -= 8;
            }
            else
            {
                if (BitConverter.IsLittleEndian)
                {
                    ipAddressBytes[i] &= ReverseBitOrder((byte)~(255 << netmask));
                }
                netmask = 0;
            }
        }

        return new IPAddress(ipAddressBytes);
    }

    /// <summary>
    ///   Returns the reverse lookup address of an IPAddress
    /// </summary>
    /// <param name="ipAddress"> Instance of the IPAddress, that should be used </param>
    /// <returns> A string with the reverse lookup address </returns>
    public static string GetReverseLookupAddress(this IPAddress ipAddress)
    {
        if (ipAddress == null)
            throw new ArgumentNullException(nameof(ipAddress));

        StringBuilder res = new StringBuilder();

        byte[] addressBytes = ipAddress.GetAddressBytes();

        if (ipAddress.AddressFamily == AddressFamily.InterNetwork)
        {
            for (int i = addressBytes.Length - 1; i >= 0; i--)
            {
                res.Append(addressBytes[i]);
                res.Append(".");
            }
            res.Append("in-addr.arpa");
        }
        else
        {
            for (int i = addressBytes.Length - 1; i >= 0; i--)
            {
                string hex = addressBytes[i].ToString("x2");
                res.Append(hex[1]);
                res.Append(".");
                res.Append(hex[0]);
                res.Append(".");
            }

            res.Append("ip6.arpa");
        }

        return res.ToString();
    }

    /// <summary>
    ///   Returns the reverse lookup DomainName of an IPAddress
    /// </summary>
    /// <param name="ipAddress"> Instance of the IPAddress, that should be used </param>
    /// <returns> A DomainName with the reverse lookup address </returns>
    public static DomainName GetReverseLookupDomain(this IPAddress ipAddress)
    {
        if (ipAddress == null)
            throw new ArgumentNullException(nameof(ipAddress));

        byte[] addressBytes = ipAddress.GetAddressBytes();

        if (ipAddress.AddressFamily == AddressFamily.InterNetwork)
        {
            string[] labels = new string[addressBytes.Length + 2];

            int labelPos = 0;

            for (int i = addressBytes.Length - 1; i >= 0; i--)
            {
                labels[labelPos++] = addressBytes[i].ToString();
            }

            labels[labelPos++] = "in-addr";
            labels[labelPos] = "arpa";

            return new DomainName(labels);
        }
        else
        {
            string[] labels = new string[addressBytes.Length * 2 + 2];

            int labelPos = 0;

            for (int i = addressBytes.Length - 1; i >= 0; i--)
            {
                string hex = addressBytes[i].ToString("x2");

                labels[labelPos++] = hex[1].ToString();
                labels[labelPos++] = hex[0].ToString();
            }

            labels[labelPos++] = "ip6";
            labels[labelPos] = "arpa";

            return new DomainName(labels);
        }
    }

    private static readonly IPAddress _ipv4MulticastNetworkAddress = IPAddress.Parse("224.0.0.0");
    private static readonly IPAddress _ipv6MulticastNetworkAddress = IPAddress.Parse("FF00::");

    /// <summary>
    ///   Returns a value indicating whether a ip address is a multicast address
    /// </summary>
    /// <param name="ipAddress"> Instance of the IPAddress, that should be used </param>
    /// <returns> true, if the given address is a multicast address; otherwise, false </returns>
    public static bool IsMulticast(this IPAddress ipAddress)
    {
        if (ipAddress == null)
            throw new ArgumentNullException(nameof(ipAddress));

        if (ipAddress.AddressFamily == AddressFamily.InterNetwork)
        {
            return ipAddress.GetNetworkAddress(4).Equals(_ipv4MulticastNetworkAddress);
        }
        else
        {
            return ipAddress.GetNetworkAddress(8).Equals(_ipv6MulticastNetworkAddress);
        }
    }

    /// <summary>
    ///   Returns the index for the interface which has the ip address assigned
    /// </summary>
    /// <param name="ipAddress"> The ip address to look for </param>
    /// <returns> The index for the interface which has the ip address assigned </returns>
    public static int GetInterfaceIndex(this IPAddress ipAddress)
    {
        if (ipAddress == null)
            throw new ArgumentNullException(nameof(ipAddress));

        var interfaceProperty = NetworkInterface.GetAllNetworkInterfaces().Select(n => n.GetIPProperties()).FirstOrDefault(p => p.UnicastAddresses.Any(a => a.Address.Equals(ipAddress)));

        if (interfaceProperty != null)
        {
            if (ipAddress.AddressFamily == AddressFamily.InterNetwork)
            {
                var property = interfaceProperty.GetIPv4Properties();
                if (property != null)
                    return property.Index;
            }
            else
            {
                var property = interfaceProperty.GetIPv6Properties();
                if (property != null)
                    return property.Index;
            }
        }

        throw new ArgumentOutOfRangeException(nameof(ipAddress), "The given ip address is not configured on the local system");
    }

    private static byte ReverseBitOrder(byte value)
    {
        byte result = 0;

        for (int i = 0; i < 8; i++)
        {
            result |= (byte)((((1 << i) & value) >> i) << (7 - i));
        }

        return result;
    }
}

internal static class StringExtensions
{
    private static readonly Regex _fromStringRepresentationRegex = new Regex(@"\\(?<key>([^0-9]|\d\d\d))", RegexOptions.Compiled);

    internal static string FromMasterfileLabelRepresentation(this string s)
    {
        if (s == null)
            return null;

        return _fromStringRepresentationRegex.Replace(s, k =>
        {
            string key = k.Groups["key"].Value;

            if (key == "#")
            {
                return @"\#";
            }
            else if (key.Length == 3)
            {
                return new String((char)Byte.Parse(key), 1);
            }
            else
            {
                return key;
            }
        });
    }

    internal static string ToMasterfileLabelRepresentation(this string s, bool encodeDots = false)
    {
        if (s == null)
            return null;

        StringBuilder sb = new StringBuilder();

        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];

            if ((c < 32) || (c > 126))
            {
                sb.Append(@"\" + ((int)c).ToString("000"));
            }
            else if (c == '"')
            {
                sb.Append(@"\""");
            }
            else if (c == '\\')
            {
                sb.Append(@"\\");
            }
            else if ((c == '.') && encodeDots)
            {
                sb.Append(@"\.");
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }

    private static readonly Random _random = new Random();
    // ReSharper disable once InconsistentNaming
    internal static string Add0x20Bits(this string s)
    {
        char[] res = new char[s.Length];

        for (int i = 0; i < s.Length; i++)
        {
            bool isLower = _random.Next() > 0x3ffffff;

            char current = s[i];

            if (!isLower && current >= 'A' && current <= 'Z')
            {
                current = (char)(current + 0x20);
            }
            else if (isLower && current >= 'a' && current <= 'z')
            {
                current = (char)(current - 0x20);
            }

            res[i] = current;
        }

        return new string(res);
    }
}

internal static class TcpClientExtensions
{
    public static bool TryConnect(this TcpClient tcpClient, IPEndPoint endPoint, int timeout)
    {
        IAsyncResult ar = tcpClient.BeginConnect(endPoint.Address, endPoint.Port, null, null);
        var wh = ar.AsyncWaitHandle;
        try
        {
            if (!ar.AsyncWaitHandle.WaitOne(TimeSpan.FromMilliseconds(timeout), false))
            {
                tcpClient.Close();
                return false;
            }

            tcpClient.EndConnect(ar);
            return true;
        }
        finally
        {
            wh.Close();
        }
    }

    public static async Task<bool> TryConnectAsync(this TcpClient tcpClient, IPAddress address, int port, int timeout, CancellationToken token)
    {
        var connectTask = tcpClient.ConnectAsync(address, port);
        var timeoutTask = Task.Delay(timeout, token);

        await Task.WhenAny(connectTask, timeoutTask);

        if (connectTask.IsCompleted)
            return true;

        tcpClient.Close();
        return false;
    }

    public static bool IsConnected(this TcpClient client)
    {
        if (!client.Connected)
            return false;

        if (client.Client.Poll(0, SelectMode.SelectRead))
        {
            if (client.Connected)
            {
                byte[] b = new byte[1];
                try
                {
                    if (client.Client.Receive(b, SocketFlags.Peek) == 0)
                    {
                        return false;
                    }
                }
                catch
                {
                    return false;
                }
            }
        }

        return true;
    }
}

internal static class UdpClientExtensions
{
    public static async Task<UdpReceiveResult> ReceiveAsync(this UdpClient udpClient, int timeout, CancellationToken token)
    {
        var connectTask = udpClient.ReceiveAsync();
        var timeoutTask = Task.Delay(timeout, token);

        await Task.WhenAny(connectTask, timeoutTask);

        if (connectTask.IsCompleted)
            return connectTask.Result;

        return new UdpReceiveResult();
    }
}


#endif
