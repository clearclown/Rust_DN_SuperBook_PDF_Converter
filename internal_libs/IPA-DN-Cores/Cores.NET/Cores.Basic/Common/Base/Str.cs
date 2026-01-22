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
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Xml.Serialization;
using IPA.Cores.Basic.Legacy;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

namespace IPA.Cores.Basic
{
    public static partial class CoresConfig
    {
        public static partial class String
        {
            public static readonly Copenhagen<int> CachedWildcardObjectsExpires = 10 * 1000; // ワイルドカードオブジェクトキャッシュ有効期限
        }
    }

    [Flags]
    public enum FullTextSearchFlags : long
    {
        None = 0,
        WordMode = 1,
        FieldNameMode = 2,
    }

    public class FullTextSearchQuery
    {
        public FullTextSearchFlags Flags = FullTextSearchFlags.None;
        public bool AndMode = true;
        public List<ValueTuple<bool, string>> WordList = new List<ValueTuple<bool, string>>();
        public int MaxResults = int.MaxValue;
        public int MaxResultsBeforeSortInternal = int.MaxValue;
        public string SortFields = "";

        [MethodImpl(Inline)]
        public static bool IsWhiteSpace(char c)
        {
            if (c == ' ' || c == '\t' || c == '\n' || c == '\r') return true;
            return false;
        }

        public void GenerateSqlLikeConditions(string rowName, out string conditionStr, out KeyValueList<string, string> paramList)
        {
            var conditionList = new List<string>();
            paramList = new KeyValueList<string, string>();

            int index = 0;

            string paramPrefix = "WORD_" + Str.GenRandStr() + "_";

            foreach (var item in this.WordList)
            {
                string ss = item.Item2;

                if (this.Flags.Bit(FullTextSearchFlags.WordMode))
                {
                    ss = "| " + ss + " |";
                }

                ss = "%" + Str.SqlServerEscapeLikeToken(ss, '?') + "%";

                string paramName = $"{paramPrefix}{index}";

                string cond = rowName + " ";

                if (item.Item1 == false)
                {
                    cond += " not ";
                }

                cond += $" like @{paramName} escape '?' ";

                conditionList.Add(cond);
                paramList.Add(paramName, ss);

                index++;
            }

            if (conditionList.Any())
            {
                conditionStr = " ( " + conditionList._Combine(this.AndMode ? " and " : " or ") + " ) ";
            }
            else
            {
                conditionStr = " ( 1 = 1 ) ";
            }
        }

        public bool IsMatch(string targetStrNormalized, string? targetStrExactMatch = null)
        {
            if (this.WordList.Any() == false)
            {
                return true;
            }

            if (this.AndMode == false && this.WordList.Any(x => x.Item1 == false)) return false; // OR モードでかつ否定単語が付いている場合は、絶対に一致しないものとする

            foreach (var item in this.WordList)
            {
                string ss = item.Item2;

                if (this.Flags.Bit(FullTextSearchFlags.WordMode))
                {
                    ss = "| " + ss + " |";
                }

                bool inStr = Str.InStr(targetStrNormalized, ss, true);

                if (inStr == false && targetStrExactMatch._IsFilled())
                {
                    inStr = Str.StrCmpi(targetStrExactMatch, ss);
                }

                if (item.Item1 == false)
                {
                    inStr = !inStr;
                }

                if (this.AndMode)
                {
                    if (inStr == false)
                    {
                        return false;
                    }
                }
                else
                {
                    if (inStr)
                    {
                        return true;
                    }
                }
            }

            if (this.AndMode)
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        public static FullTextSearchQuery ParseText(string queryStr, FullTextSearchFlags flags = FullTextSearchFlags.None)
        {
            queryStr = queryStr._NonNullTrim();

            // aaa and "bbb ccc" and ddd  とかを入力したやつを、まず、"" を意識して単語分離します。
            List<string> wordList = new List<string>();

            int mode = 0;
            bool isInWord = false;
            char wordChar = (char)0;

            StringBuilder currentToken = new StringBuilder();

            foreach (char c in queryStr)
            {
                bool isWhiteSpace = IsWhiteSpace(c);

                LABEL1:
                if (mode == 0)
                {
                    if (isWhiteSpace == false || isInWord)
                    {
                        currentToken.Append(c);

                        if (isInWord == false && (c == '\"' || c == '\''))
                        {
                            isInWord = true;
                            wordChar = c;
                        }
                        else if (isInWord && c == wordChar)
                        {
                            wordChar = (char)0;
                            isInWord = false;
                        }
                    }
                    else
                    {
                        wordList.Add(currentToken.ToString());
                        currentToken.Clear();
                        mode = 1;
                    }
                }
                else if (mode == 1)
                {
                    wordChar = (char)0;
                    if (isWhiteSpace == false)
                    {
                        mode = 0;
                        goto LABEL1;
                    }
                }
            }

            wordList.Add(currentToken.ToString());

            int numAnd = 0;
            int numOr = 0;

            // 次に、各単語リストを分解します。
            foreach (string word in wordList)
            {
                if (word._IsFilled())
                {
                    if (word._IsSamei("and"))
                    {
                        numAnd++;
                    }
                    else if (word._IsSamei("or"))
                    {
                        numOr++;
                    }
                }
            }

            if (numAnd != 0 && numOr != 0)
            {
                throw new CoresException($"The specified search query string '{queryStr}' contains both AND and OR operators. You can use either AND or OR in a single search query string.");
            }

            FullTextSearchQuery ret = new FullTextSearchQuery();

            ret.AndMode = (numOr == 0); // true: AND モード、false: OR モード

            ret.Flags = flags;

            foreach (string word in wordList)
            {
                if (word._IsFilled() && word._IsDiffi("and") && word._IsDiffi("or"))
                {
                    string word2;
                    bool wordFlag;

                    if (word.StartsWith("-") || word.StartsWith("!"))
                    {
                        word2 = word.Substring(1);
                        wordFlag = false;
                    }
                    else
                    {
                        word2 = word;
                        wordFlag = true;
                    }

                    word2 = word2._RemoveQuotation();

                    word2 = Str.NormalizeStrForSearch(word2);

                    if (word2._IsFilled())
                    {
                        ret.WordList.Add(new ValueTuple<bool, string>(wordFlag, word2));
                    }
                }
            }

            return ret;
        }
    }

    [Flags]
    public enum CrlfStyle
    {
        LocalPlatform,
        Lf,
        CrLf,
        NoChange,
    }

    // DateTime をシンブル文字列に変換
    [Flags]
    public enum DtStrOption
    {
        All,
        DateOnly,
        TimeOnly,
    }

    namespace Legacy
    {
        // キーバリューリスト
        public class KeyValueList
        {
            SortedDictionary<string, string> data = new SortedDictionary<string, string>();

            public KeyValueList()
            {
            }

            public KeyValueList(string fromString)
            {
                KeyValueList v = this;
                StringReader r = new StringReader(fromString);
                while (true)
                {
                    string? line = r.ReadLine();
                    if (line == null)
                    {
                        break;
                    }

                    int i = Str.SearchStr(line, "=", 0);
                    if (i != -1)
                    {
                        string key = Str.DecodeCEscape(line.Substring(0, i));
                        string value = Str.DecodeCEscape(line.Substring(i + 1));

                        v.Add(key, value);
                    }
                }
            }

            void NormalizeName(ref string name)
            {
                Str.NormalizeString(ref name);
                name = name.ToUpperInvariant();
                if (Str.InStr(name, "="))
                {
                    throw new InvalidDataException(name);
                }
            }

            public string this[string name]
            {
                get
                {
                    return Get(name);
                }

                set
                {
                    Set(name, value);
                }
            }

            public void Add(string name, string value)
            {
                Set(name, value);
            }

            public void Set(string name, string value)
            {
                NormalizeName(ref name);

                if (value == null)
                {
                    value = "";
                }

                if (value == "")
                {
                    Delete(name);
                    return;
                }

                if (data.ContainsKey(name) == false)
                {
                    data.Add(name, value);
                }
                else
                {
                    data[name] = value;
                }
            }

            public string Get(string name)
            {
                NormalizeName(ref name);

                if (data.ContainsKey(name))
                {
                    return data[name];
                }
                else
                {
                    return "";
                }
            }

            public void Delete(string name)
            {
                NormalizeName(ref name);

                if (data.ContainsKey(name))
                {
                    data.Remove(name);
                }
            }

            public string[] EnumKeys()
            {
                List<string> ret = new List<string>();
                foreach (string key in this.data.Keys)
                {
                    ret.Add(key);
                }
                return ret.ToArray();
            }

            public override string ToString()
            {
                StringWriter w = new StringWriter();
                foreach (string name in data.Keys)
                {
                    string line = string.Format("{0}={1}", Str.EncodeCEscape(name), Str.EncodeCEscape(data[name]));

                    w.WriteLine(line);
                }

                return w.ToString();
            }

            public static KeyValueList FromString(string src)
            {
                return new KeyValueList(src);
            }
        }

        // シリアライズされたエラー文字列
        public class SerializedError
        {
            public string Code = null!;
            public string Language = null!;
            public string ErrorMsg = null!;
        }
    }

    // Printf 風フラグ
    [FlagsAttribute]
    public enum PrintFFLags
    {
        Minus = 1,
        Plus = 2,
        Zero = 4,
        Blank = 8,
        Sharp = 16,
    }

    // Printf 風パース結果
    public class PrintFParsedParam
    {
        public bool Ok = false;
        public readonly PrintFFLags Flags = 0;
        public readonly int Width = 0;
        public readonly int Precision = 0;
        public readonly bool NoPrecision = true;
        public readonly string Type = "";

        static PrintFFLags CharToFlag(char c)
        {
            switch (c)
            {
                case '-':
                    return PrintFFLags.Minus;

                case '+':
                    return PrintFFLags.Plus;

                case '0':
                    return PrintFFLags.Zero;

                case ' ':
                    return PrintFFLags.Blank;

                case '#':
                    return PrintFFLags.Sharp;
            }

            return 0;
        }

        public string GetString(object param)
        {
            int i;
            StringBuilder sb;
            string tmp = "(error)";
            double f;
            bool signed = false;

            switch (this.Type)
            {
                case "c":
                case "C":
                    if (param is char)
                    {
                        tmp += (char)param;
                    }
                    else if (param is string)
                    {
                        string s = (string)param;
                        if (s.Length >= 1)
                        {
                            tmp += s[0];
                        }
                    }
                    break;

                case "d":
                case "i":
                    sb = new StringBuilder();
                    int count = this.Width;
                    if (this.Precision != 0)
                    {
                        count = this.Precision;
                    }
                    for (i = 1; i < this.Precision; i++)
                    {
                        sb.Append("#");
                    }
                    sb.Append("0");

                    if (param is int)
                    {
                        tmp = ((int)param).ToString(sb.ToString());
                    }
                    else if (param is long)
                    {
                        tmp = ((long)param).ToString(sb.ToString());
                    }
                    else if (param is uint)
                    {
                        tmp = ((int)((uint)param)).ToString(sb.ToString());
                    }
                    else if (param is ulong)
                    {
                        tmp = ((long)((ulong)param)).ToString(sb.ToString());
                    }
                    else if (param is decimal)
                    {
                        tmp = ((decimal)param).ToString(sb.ToString());
                    }
                    signed = true;

                    break;

                case "u":
                    sb = new StringBuilder();
                    for (i = 1; i < this.Precision; i++)
                    {
                        sb.Append("#");
                    }
                    sb.Append("0");

                    if (param is int)
                    {
                        tmp = ((uint)((int)param)).ToString(sb.ToString());
                    }
                    else if (param is long)
                    {
                        tmp = ((ulong)((long)param)).ToString(sb.ToString());
                    }
                    else if (param is uint)
                    {
                        tmp = ((uint)param).ToString(sb.ToString());
                    }
                    else if (param is ulong)
                    {
                        tmp = ((ulong)param).ToString(sb.ToString());
                    }
                    else if (param is decimal)
                    {
                        tmp = ((decimal)param).ToString(sb.ToString());
                    }

                    break;

                case "x":
                case "X":
                    sb = new StringBuilder();
                    sb.Append(this.Type);
                    sb.Append(this.Precision.ToString());

                    if (param is int)
                    {
                        tmp = ((uint)((int)param)).ToString(sb.ToString());
                    }
                    else if (param is long)
                    {
                        tmp = ((ulong)((long)param)).ToString(sb.ToString());
                    }
                    else if (param is uint)
                    {
                        tmp = ((uint)param).ToString(sb.ToString());
                    }
                    else if (param is ulong)
                    {
                        tmp = ((ulong)param).ToString(sb.ToString());
                    }

                    break;

                case "e":
                case "E":
                case "f":
                    f = 0;

                    if (param is int)
                    {
                        f = (double)((int)param);
                    }
                    else if (param is long)
                    {
                        f = (double)((long)param);
                    }
                    else if (param is uint)
                    {
                        f = (double)((uint)param);
                    }
                    else if (param is ulong)
                    {
                        f = (double)((ulong)param);
                    }
                    else if (param is decimal)
                    {
                        f = (double)((long)param);
                    }
                    else if (param is float)
                    {
                        f = (double)((float)param);
                    }
                    else if (param is double)
                    {
                        f = (double)param;
                    }
                    else
                    {
                        break;
                    }

                    int prectmp = Precision;
                    if (prectmp == 0 && NoPrecision)
                    {
                        prectmp = 6;
                    }

                    tmp = f.ToString(string.Format("{0}{1}", Type, prectmp));

                    break;

                case "s":
                case "S":
                    if (param == null)
                    {
                        tmp = "(null)";
                    }
                    else
                    {
                        tmp = param.ToString()._NonNull();
                    }
                    break;
            }

            int normalWidth = Str.GetStrWidth(tmp);
            int targetWidth = Math.Max(this.Width, normalWidth);

            if ((this.Flags & PrintFFLags.Plus) != 0)
            {
                if (signed)
                {
                    if (tmp.StartsWith("-") == false)
                    {
                        tmp = "+" + tmp;
                    }
                }
            }
            else
            {
                if ((this.Flags & PrintFFLags.Blank) != 0)
                {
                    if (signed)
                    {
                        if (tmp.StartsWith("-") == false)
                        {
                            tmp = " " + tmp;
                        }
                    }
                }
            }

            if ((this.Flags & PrintFFLags.Minus) != 0)
            {
                int w = targetWidth - Str.GetStrWidth(tmp);
                if (w < 0)
                {
                    w = 0;
                }

                tmp += Str.MakeCharArray(' ', w);
            }
            else if ((this.Flags & PrintFFLags.Zero) != 0)
            {
                int w = targetWidth - Str.GetStrWidth(tmp);
                if (w < 0)
                {
                    w = 0;
                }

                tmp = Str.MakeCharArray('0', w) + tmp;
            }
            else
            {
                int w = targetWidth - Str.GetStrWidth(tmp);
                if (w < 0)
                {
                    w = 0;
                }

                tmp = Str.MakeCharArray(' ', w) + tmp;
            }

            if ((this.Flags & PrintFFLags.Sharp) != 0)
            {
                if (Type == "x" || Type == "X")
                {
                    tmp = "0x" + tmp;
                }
            }

            return tmp;
        }

        public PrintFParsedParam(string str)
        {
            Str.NormalizeString(ref str);

            if (str.StartsWith("%") == false)
            {
                return;
            }

            str = str.Substring(1);

            Queue<char> q = new Queue<char>();
            foreach (char c in str)
            {
                q.Enqueue(c);
            }

            while (q.Count >= 1)
            {
                char c = q.Peek();
                PrintFFLags f = CharToFlag(c);

                if (f == 0)
                {
                    break;
                }

                this.Flags |= f;
                q.Dequeue();
            }

            Queue<char> q2 = new Queue<char>();

            while (q.Count >= 1)
            {
                bool bf = false;
                char c = q.Peek();

                switch (c)
                {
                    case 'h':
                    case 'l':
                    case 'I':
                    case 'c':
                    case 'C':
                    case 'd':
                    case 'i':
                    case 'o':
                    case 'u':
                    case 'x':
                    case 'X':
                    case 'e':
                    case 'E':
                    case 'f':
                    case 'g':
                    case 'G':
                    case 'n':
                    case 'p':
                    case 's':
                    case 'S':
                        bf = true;
                        break;

                    default:
                        q2.Enqueue(c);
                        break;
                }

                if (bf)
                {
                    break;
                }

                q.Dequeue();
            }

            string[] widthAndPrec = (new string(q2.ToArray())).Split('.');

            if (widthAndPrec.Length == 1)
            {
                this.Width = Str.StrToInt(widthAndPrec[0]);
            }
            else if (widthAndPrec.Length == 2)
            {
                this.Width = Str.StrToInt(widthAndPrec[0]);
                this.Precision = Str.StrToInt(widthAndPrec[1]);
                this.NoPrecision = false;
            }

            this.Width = Math.Max(this.Width, 0);
            this.Precision = Math.Max(this.Precision, 0);


            while (q.Count >= 1)
            {
                char c = q.Peek();
                bool bf = false;

                switch (c)
                {
                    case 'c':
                    case 'C':
                    case 'd':
                    case 'i':
                    case 'o':
                    case 'u':
                    case 'x':
                    case 'X':
                    case 'e':
                    case 'E':
                    case 'f':
                    case 'g':
                    case 'G':
                    case 'a':
                    case 'A':
                    case 'n':
                    case 'p':
                    case 's':
                    case 'S':
                        bf = true;
                        break;

                    default:
                        break;
                }

                if (bf)
                {
                    break;
                }

                q.Dequeue();
            }

            this.Type = new string(q.ToArray());
            if (this.Type.Length >= 1)
            {
                this.Type = this.Type.Substring(0, 1);
            }

            this.Ok = (Str.IsEmptyStr(this.Type) == false);
        }
    }

    public class DevToolsCsUsingStrComparer : IEqualityComparer<string?>, IComparer<string?>
    {
        public int Compare(string? x, string? y)
        {
            x = x._NonNullTrim();
            y = y._NonNullTrim();

            if (x.StartsWith("using"))
            {
                Str.GetKeyAndValue(x, out _, out x);
            }

            if (x.EndsWith(";")) x = x.Substring(0, x.Length - 1);

            if (y.StartsWith("using"))
            {
                Str.GetKeyAndValue(y, out _, out y);
            }

            if (y.EndsWith(";")) y = y.Substring(0, y.Length - 1);

            bool b1 = (x.StartsWith("using System") || x.StartsWith("System"));
            bool b2 = (y.StartsWith("using System") || y.StartsWith("System"));

            if (b1 != b2)
            {
                return -b1.CompareTo(b2);
            }

            return x._CmpTrim(y);
        }

        public bool Equals(string? x, string? y)
        {
            return x._IsSameTrim(y);
        }

        public int GetHashCode(string? obj)
        {
            return obj._NonNullTrim().GetHashCode();
        }
    }

    public class ExtendedStrComparer : IEqualityComparer<string?>, IComparer<string?>
    {
        public StringComparison Comparison { get; }

        public ExtendedStrComparer(StringComparison comparison)
        {
            this.Comparison = comparison;
        }

        public int Compare(string? x, string? y)
        {
            return x._CmpTrim(y, this.Comparison);
        }

        public bool Equals(string? x, string? y)
        {
            return x._IsSameTrim(y, this.Comparison);
        }

        public int GetHashCode(string? obj)
        {
            return obj._NonNullTrim().GetHashCode(this.Comparison);
        }
    }

    public class IpAddressStrComparer : IEqualityComparer<string?>, IComparer<string?>
    {
        public int Compare(string? x, string? y)
        {
            x = x._NonNullTrim();
            y = y._NonNullTrim();

            if ((IgnoreCase)x == y) return 0;

            if (IPAddress.TryParse(x, out IPAddress? ip1))
            {
                if (IPAddress.TryParse(y, out IPAddress? ip2))
                {
                    int r = ip1.AddressFamily.CompareTo(ip2.AddressFamily);
                    if (r != 0) return r;

                    r = Util.MemCompare(ip1.GetAddressBytes(), ip2.GetAddressBytes());
                    if (r != 0) return r;

                    if (ip1.AddressFamily != System.Net.Sockets.AddressFamily.InterNetworkV6) return 0;

                    return ip1.ScopeId.CompareTo(ip2.ScopeId);
                }
            }

            return string.Compare(x, y, StringComparison.OrdinalIgnoreCase);
        }

        public bool Equals(string? x, string? y)
        {
            x = x._NonNullTrim();
            y = y._NonNullTrim();

            if ((IgnoreCase)x == y) return true;

            if (IPAddress.TryParse(x, out IPAddress? ip1))
            {
                if (IPAddress.TryParse(y, out IPAddress? ip2))
                {
                    return ip1.Equals(ip2);
                }
            }

            return false;
        }

        public int GetHashCode(string? obj)
        {
            obj = obj._NonNullTrim();

            if (IPAddress.TryParse(obj, out IPAddress? addr) == false)
            {
                return 0;
            }
            else
            {
                return addr.GetHashCode();
            }
        }
    }

    public class HttpFqdnReverseStrComparer : IEqualityComparer<string?>, IComparer<string?>
    {
        public int Compare(string? x, string? y)
        {
            x = x._NonNullTrim();
            y = y._NonNullTrim();

            if ((IgnoreCase)x == y) return 0;

            if (x._TryParseUrl(out Uri? url_x, out _, null) == false ||
                y._TryParseUrl(out Uri? url_y, out _, null) == false)
            {
                return string.Compare(x, y, StringComparison.OrdinalIgnoreCase);
            }

            url_x._MarkNotNull();
            url_y._MarkNotNull();

            string fqdn_x = Str.ReverseFqdnStr(url_x.DnsSafeHost);
            string fqdn_y = Str.ReverseFqdnStr(url_y.DnsSafeHost);

            int r = string.Compare(fqdn_x, fqdn_y, StringComparison.OrdinalIgnoreCase);
            if (r != 0) return r;

            r = string.Compare(url_x.PathAndQuery, url_y.PathAndQuery, StringComparison.OrdinalIgnoreCase);
            if (r != 0) return r;

            r = string.Compare(url_x.Scheme, url_y.Scheme, StringComparison.OrdinalIgnoreCase);
            if (r != 0) return r;

            r = url_x.Port.CompareTo(url_y.Port);

            return r;
        }

        public bool Equals(string? x, string? y)
        {
            x = x._NonNullTrim();
            y = y._NonNullTrim();

            if ((IgnoreCase)x == y) return true;

            if (x._TryParseUrl(out Uri? url_x, out _, null) == false ||
                y._TryParseUrl(out Uri? url_y, out _, null) == false)
            {
                return string.Equals(x, y, StringComparison.OrdinalIgnoreCase);
            }

            url_x._MarkNotNull();
            url_y._MarkNotNull();

            string fqdn_x = Str.ReverseFqdnStr(url_x.DnsSafeHost);
            string fqdn_y = Str.ReverseFqdnStr(url_y.DnsSafeHost);

            if (string.Equals(fqdn_x, fqdn_y, StringComparison.OrdinalIgnoreCase) == false)
                return false;

            if (string.Equals(url_x.PathAndQuery, url_y.PathAndQuery, StringComparison.OrdinalIgnoreCase) == false)
                return false;

            if (string.Equals(url_x.Scheme, url_y.Scheme, StringComparison.OrdinalIgnoreCase) == false)
                return false;

            return url_x.Port.Equals(url_y.Port);
        }

        public int GetHashCode(string? obj)
        {
            obj = obj._NonNullTrim();

            return obj.GetHashCode();
        }
    }

    [Flags]
    public enum FqdnReverseStrComparerFlags
    {
        None = 0,
        ConsiderDepth = 1,
    }

    public class FqdnReverseStrComparer : IEqualityComparer<string?>, IComparer<string?>
    {
        public static FqdnReverseStrComparer Comparer { get; } = new FqdnReverseStrComparer();
        public static FqdnReverseStrComparer ComparerConsiderDepth { get; } = new FqdnReverseStrComparer(FqdnReverseStrComparerFlags.ConsiderDepth);

        public FqdnReverseStrComparerFlags Flags { get; }

        public FqdnReverseStrComparer(FqdnReverseStrComparerFlags flags = FqdnReverseStrComparerFlags.None)
        {
            this.Flags = flags;
        }

        public int Compare(string? x, string? y)
        {
            x = x._NonNullTrim();
            y = y._NonNullTrim();

            if ((IgnoreCase)x == y) return 0;

            x = Str.ReverseFqdnStr(x, out int depth_x);
            y = Str.ReverseFqdnStr(y, out int depth_y);

            if (this.Flags.Bit(FqdnReverseStrComparerFlags.ConsiderDepth))
            {
                int r = depth_x.CompareTo(depth_y);
                if (r != 0)
                {
                    return r;
                }
            }

            return string.Compare(x, y, StringComparison.OrdinalIgnoreCase);
        }

        public bool Equals(string? x, string? y)
        {
            x = x._NonNullTrim();
            y = y._NonNullTrim();

            if ((IgnoreCase)x == y) return true;

            x = Str.ReverseFqdnStr(x);
            y = Str.ReverseFqdnStr(y);

            return string.Equals(x, y, StringComparison.OrdinalIgnoreCase);
        }

        public int GetHashCode(string? obj)
        {
            obj = obj._NonNullTrim();

            return obj.GetHashCode(StringComparison.OrdinalIgnoreCase);
        }
    }


    public class PrefixZenkakuNumberStrComparer : IEqualityComparer<string?>, IComparer<string?>
    {
        public static PrefixZenkakuNumberStrComparer Comparer { get; } = new PrefixZenkakuNumberStrComparer();

        public int Compare(string? x, string? y)
        {
            x = x._NonNullTrim();
            y = y._NonNullTrim();

            if ((IgnoreCase)x == y) return 0;

            x = Str.NormalizePrefixZenkakuNumberForSortFast(x);
            y = Str.NormalizePrefixZenkakuNumberForSortFast(y);

            return string.Compare(x, y, StringComparison.OrdinalIgnoreCase);
        }

        public bool Equals(string? x, string? y)
        {
            x = x._NonNullTrim();
            y = y._NonNullTrim();

            if ((IgnoreCase)x == y) return true;

            x = Str.NormalizePrefixZenkakuNumberForSortFast(x);
            y = Str.NormalizePrefixZenkakuNumberForSortFast(y);

            return string.Equals(x, y, StringComparison.OrdinalIgnoreCase);
        }

        public int GetHashCode(string? obj)
        {
            obj = obj._NonNullTrim();

            return obj.GetHashCode(StringComparison.OrdinalIgnoreCase);
        }

    }

    public class StrComparer : IEqualityComparer<string?>, IComparer<string?>
    {
        public static StrComparer IgnoreCaseComparer { get; } = new StrComparer(false);
        public static StrComparer SensitiveCaseComparer { get; } = new StrComparer(true);

        public static DevToolsCsUsingStrComparer DevToolsCsUsingComparer { get; } = new DevToolsCsUsingStrComparer();

        public static ExtendedStrComparer IgnoreCaseTrimComparer { get; } = new ExtendedStrComparer(StringComparison.OrdinalIgnoreCase);
        public static ExtendedStrComparer SensitiveCaseTrimComparer { get; } = new ExtendedStrComparer(StringComparison.Ordinal);

        public static IpAddressStrComparer IpAddressStrComparer { get; } = new IpAddressStrComparer();

        public static FqdnReverseStrComparer FqdnReverseStrComparer { get; } = new FqdnReverseStrComparer();
        public static HttpFqdnReverseStrComparer HttpFqdnReverseStrComparer { get; } = new HttpFqdnReverseStrComparer();

        public static PrefixZenkakuNumberStrComparer PrefixZenkakuNumberStrComparer { get; } = new PrefixZenkakuNumberStrComparer();

        readonly static Singleton<StringComparison, StrComparer> FromComparisonCache = new Singleton<StringComparison, StrComparer>(x => new StrComparer(x));

        public static StrComparer Get(StringComparison comparison)
        {
            if (comparison == StringComparison.Ordinal)
            {
                return SensitiveCaseComparer;
            }
            else if (comparison == StringComparison.OrdinalIgnoreCase)
            {
                return IgnoreCaseComparer;
            }
            else
            {
                return FromComparisonCache[comparison];
            }
        }

        public static StrComparer Get(bool caseSensitive = false)
        {
            if (caseSensitive == false)
            {
                return IgnoreCaseComparer;
            }
            else
            {
                return SensitiveCaseComparer;
            }
        }

        public StringComparison Comparison { get; }

        private StrComparer(bool caseSensitive = false)
        {
            this.Comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        }

        private StrComparer(StringComparison comparison)
        {
            this.Comparison = comparison;
        }

        public int Compare(string? x, string? y)
            => string.Compare(x, y, this.Comparison);

        public bool Equals(string? x, string? y)
            => string.Equals(x, y, this.Comparison);

        public int GetHashCode(string? obj)
            => obj?.GetHashCode(this.Comparison) ?? 0;

        public static implicit operator StringComparison(StrComparer cmp) => cmp.Comparison;
    }

    public delegate bool RemoveStringFunction(string str);

    [Flags]
    public enum MacAddressStyle
    {
        Windows,
        Linux,
    }

    [Flags]
    public enum FullDateTimeStrFlags : ulong
    {
        None = 0,
        NoSeconds = 1,
        SlashDate = 2,
        CommaTime = 4,
    }

    public class UrlEncodeParam
    {
        public string DoNotEncodeCharList { get; set; }

        public UrlEncodeParam(string dontEncodeCharList = "")
        {
            this.DoNotEncodeCharList = dontEncodeCharList;
        }
    }

    [Flags]
    public enum SearchableStrFlag
    {
        None = 0,
        IncludeBase64InBinary = 1,
        IncludePrintableAsciiStr = 2,
        PrependFieldName = 4,
        FastMode = 8,

        Default = IncludeBase64InBinary | IncludePrintableAsciiStr,
    }

    // 文字列操作
    public static class Str
    {
        public static Encoding AsciiEncoding { get; }
        public static Encoding ShiftJisEncoding { get; }
        public static Encoding ISO2022JPEncoding { get; }
        public static Encoding EucJpEncoding { get; }
        public static Encoding ISO88591Encoding { get; }
        public static Encoding GB2312Encoding { get; }
        public static Encoding EucKrEncoding { get; }
        public static Encoding Utf8Encoding { get; }
        public static Encoding UniEncoding { get; }

        public static byte[] BomUtf8 { get; }

        public static readonly ReadOnlyMemory<byte> NewLine_Bytes_Windows = new byte[] { 13, 10 };
        public static readonly ReadOnlyMemory<byte> NewLine_Bytes_Unix = new byte[] { 10 };
        public static readonly ReadOnlyMemory<byte> NewLine_Bytes_Local = (Environment.OSVersion.Platform == PlatformID.Win32NT) ? NewLine_Bytes_Windows : NewLine_Bytes_Unix;

        public static readonly ReadOnlyMemory<byte> CrLf_Bytes = new byte[] { 13, 10 };
        public static readonly ReadOnlyMemory<byte> Lf_Bytes = new byte[] { 10 };

        public static readonly string NewLine_Str_Windows = "\r\n";
        public static readonly string NewLine_Str_Unix = "\n";
        public static readonly string NewLine_Str_Local = (Environment.OSVersion.Platform == PlatformID.Win32NT) ? NewLine_Str_Windows : NewLine_Str_Unix;

        public static readonly string CrLf_Str = "\r\n";
        public static readonly string Lf_Str = "\n";

        public static ReadOnlyMemory<byte> GetCrlfBytes(CrlfStyle style = CrlfStyle.LocalPlatform) => GetNewLineBytes(style);
        public static ReadOnlyMemory<byte> GetNewLineBytes(CrlfStyle style = CrlfStyle.LocalPlatform)
        {
            switch (style)
            {
                case CrlfStyle.LocalPlatform:
                    return NewLine_Bytes_Local;

                case CrlfStyle.CrLf:
                    return NewLine_Bytes_Windows;

                case CrlfStyle.Lf:
                    return NewLine_Bytes_Unix;

                default:
                    throw new ArgumentOutOfRangeException("style");
            }
        }

        public static string GetCrlfStr(CrlfStyle style = CrlfStyle.LocalPlatform) => GetNewLineStr(style);
        public static string GetNewLineStr(CrlfStyle style = CrlfStyle.LocalPlatform)
        {
            switch (style)
            {
                case CrlfStyle.LocalPlatform:
                    return NewLine_Str_Local;

                case CrlfStyle.CrLf:
                    return NewLine_Str_Windows;

                case CrlfStyle.Lf:
                    return NewLine_Str_Unix;

                default:
                    throw new ArgumentOutOfRangeException("style");
            }
        }

        public static IReadOnlyList<Encoding> SuitableEncodingListForJapaneseWin32;

        // Encoding の初期化
        static Str()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            AsciiEncoding = GetNonBomEncoding(Encoding.ASCII);
            ShiftJisEncoding = GetNonBomEncoding(Encoding.GetEncoding("shift_jis"));
            ISO2022JPEncoding = GetNonBomEncoding(Encoding.GetEncoding("ISO-2022-JP"));
            EucJpEncoding = GetNonBomEncoding(Encoding.GetEncoding("euc-jp"));
            ISO88591Encoding = GetNonBomEncoding(Encoding.GetEncoding("iso-8859-1"));
            GB2312Encoding = GetNonBomEncoding(Encoding.GetEncoding("gb2312"));
            EucKrEncoding = GetNonBomEncoding(Encoding.GetEncoding("euc-kr"));
            Utf8Encoding = GetNonBomEncoding(Encoding.UTF8);
            UniEncoding = GetNonBomEncoding(Encoding.Unicode);
            BomUtf8 = Str.GetBOM(Str.Utf8Encoding)!; 

            var suitableEncodingListForJapaneseWin32 = new List<Encoding>();
            suitableEncodingListForJapaneseWin32.Add(ShiftJisEncoding);
            suitableEncodingListForJapaneseWin32.Add(AsciiEncoding);
            suitableEncodingListForJapaneseWin32.Add(Utf8Encoding);

            SuitableEncodingListForJapaneseWin32 = suitableEncodingListForJapaneseWin32;
        }

        public static Encoding GetNonBomEncoding(Encoding src)
        {
            try
            {
                if (_DefaultIfException(() => src.BodyName, "")._IsEmpty())
                {
                    // エンコーディングが System.Text.ConsoleEncoding の場合、BodyName の取得に失敗する。
                    // その原因は、.NET 6 のライブラリのバグにより、_codePage プライベート変数の値が 0 になっているためである。
                    // この場合、コードページ番号を元にして Encoding を明確に取得する。
                    src = Encoding.GetEncoding(src.CodePage);
                }
            }
            catch { }

            try
            {
                if (src.GetPreamble().Length >= 1)
                {
                    switch (src.CodePage)
                    {
                        case 65001:
                            return new UTF8Encoding(false);
                        case 1200:
                            return new UnicodeEncoding(false, false);
                        case 1201:
                            return new UnicodeEncoding(true, false);
                    }
                }
            }
            catch { }

            return src;
        }

        public static Encoding ConsoleInputEncoding => ConsoleInputEncodingInternal;
        public static Encoding ConsoleOutputEncoding => ConsoleOutputEncodingInternal;
        public static Encoding ConsoleErrorEncoding => ConsoleErrorEncodingInternal;

        static Singleton<Encoding> ConsoleInputEncodingInternal = new(() => GetAlternativeEncodingIfNullOrCodePage0(_DefaultIfException(() => Console.InputEncoding, null), () => GuessSystemConsoleEncodingByApi()));
        static Singleton<Encoding> ConsoleOutputEncodingInternal = new(() => GetAlternativeEncodingIfNullOrCodePage0(_DefaultIfException(() => Console.OutputEncoding, null), () => GuessSystemConsoleEncodingByApi()));
        static Singleton<Encoding> ConsoleErrorEncodingInternal = new(() => GetAlternativeEncodingIfNullOrCodePage0(_DefaultIfException(() => Console.OutputEncoding, null), () => GuessSystemConsoleEncodingByApi()));

        static Encoding GuessSystemConsoleEncodingByApi()
        {
            if (Env.IsWindows)
            {
                var ret = Win32ApiUtil.GetConsoleOutputEncoding();
                if (ret != null)
                {
                    return ret;
                }
            }

            return Str.Utf8Encoding;
        }

        static Encoding GetAlternativeEncodingIfNullOrCodePage0(Encoding? enc, Func<Encoding> getAlternativeEncodingProc)
        {
            bool isNull = false;
            //Console.WriteLine($"GetAlternativeEncodingIfNullOrCodePage0");
            //Console.WriteLine($"  {enc != null}");
            //try
            //{
            //    Console.WriteLine($"  {enc.ToString()}");
            //}
            //catch { }
            //try
            //{
            //    Console.WriteLine($"  CodePage = {enc.CodePage}");
            //}
            //catch { }
            //try
            //{
            //    Console.WriteLine($"  BodyName = {enc.BodyName}");
            //}
            //catch { }
            //try
            //{
            //    Console.WriteLine($"  WebName = {enc.WebName}");
            //}
            //catch { }
            //try
            //{
            //    string str1 = Str.GenRandStr();
            //    Console.WriteLine($"  str1 = {str1}");
            //    string str2 = enc.GetString(enc.GetBytes(str1));
            //    Console.WriteLine($"  str1 = {str2}");
            //}
            //catch { }
            if (enc == null || enc.CodePage == 0)
            {
                isNull = true;
            }
            else
            {
                try
                {
                    if (_DefaultIfException(() => enc.BodyName, "")._IsEmpty())
                    {
                        // エンコーディングが System.Text.ConsoleEncoding の場合、BodyName の取得に失敗する。
                        // その原因は、.NET 6 のライブラリのバグにより、_codePage プライベート変数の値が 0 になっているためである。
                        // この場合、コードページ番号を元にして Encoding を明確に取得する。
                        enc = Encoding.GetEncoding(enc.CodePage);
                    }

                    string str1 = Str.GenRandStr();
                    string str2 = enc.GetString(enc.GetBytes(str1));
                    if (str1 != str2)
                    {
                        isNull = true;
                    }
                }
                catch
                {
                    isNull = true;
                }
            }

            if (isNull == false) return enc!;

            return getAlternativeEncodingProc();
        }


        /// <summary>
        /// Processes the input template, replacing patterns of the form {option1|option2|...}
        /// with a randomly selected option, supporting optional weights and nesting.
        /// </summary>
        /// <param name="input">The template string containing patterns.</param>
        /// <returns>The processed string with all patterns evaluated.</returns>
        public static string ProcessTemplateStr(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            // Matches the innermost braces: { ... }
            var pattern = new Regex(@"\{([^{}]*)\}", RegexOptions.Compiled);
            string result = input;

            // Iteratively replace innermost patterns until none remain
            while (pattern.IsMatch(result))
            {
                result = pattern.Replace(result, match => EvaluateChoice(match.Groups[1].Value));
            }

            return result;
        }

        /// <summary>
        /// Evaluates a single brace-enclosed choice list, selecting one option based on weights.
        /// </summary>
        /// <param name="content">The inner text of the braces (no braces).</param>
        /// <returns>The selected option text.</returns>
        private static string EvaluateChoice(string content)
        {
            // Split options on top-level '|' (no nested braces here)
            var parts = content.Split('|');
            int count = parts.Length;
            var texts = new string[count];
            var weights = new double[count];

            double sumSpecified = 0;
            int unspecifiedCount = 0;

            // Parse each part for optional weight suffix
            for (int i = 0; i < count; i++)
            {
                string part = parts[i];
                string text = part;
                int weight = -1;

                int colonIndex = part.LastIndexOf(':');
                if (colonIndex >= 0)
                {
                    var weightStr = part.Substring(colonIndex + 1).Trim();
                    if (weightStr.Length > 0 && weightStr.All(char.IsDigit))
                    {
                        weight = int.Parse(weightStr);
                        text = part.Substring(0, colonIndex);
                    }
                    else if (weightStr.Length == 0)
                    {
                        // Trailing colon without number -> unspecified weight
                        text = part.Substring(0, colonIndex);
                    }
                }

                text = text.Trim();
                texts[i] = text;

                if (weight >= 0)
                {
                    weights[i] = weight;
                    sumSpecified += weight;
                }
                else
                {
                    weights[i] = -1;
                    unspecifiedCount++;
                }
            }

            // Distribute remaining weight among unspecified
            double defaultWeight = 0;
            if (unspecifiedCount > 0)
            {
                defaultWeight = Math.Max(0, 100 - sumSpecified) / unspecifiedCount;
            }

            // Finalize weights and compute total
            double totalWeight = 0;
            for (int i = 0; i < count; i++)
            {
                if (weights[i] < 0)
                    weights[i] = defaultWeight;
                totalWeight += weights[i];
            }

            // If all weights zero, fallback to uniform selection
            if (totalWeight <= 0)
            {
                int index = Util.RandSInt31() % count;
                return texts[index];
            }

            // Random selection based on weights
            double r = Util.RandDouble0To1() * totalWeight;
            double cumulative = 0;
            for (int i = 0; i < count; i++)
            {
                cumulative += weights[i];
                if (r < cumulative)
                {
                    return texts[i];
                }
            }

            // Fallback (shouldn't happen)
            return texts[count - 1];
        }


        static readonly FastCache<string, string> Cache_NormalizePrefixZenkakuNumberFast = new FastCache<string, string>(int.MaxValue, comparer: StrComparer.SensitiveCaseTrimComparer);

        public static string? NormalizePrefixZenkakuNumberFast(string number)
        {
            string? ret = Cache_NormalizePrefixZenkakuNumberFast.GetOrCreate(number, s => NormalizePrefixZenkakuNumberInternal(s));

            return ret;
        }

        static string? NormalizePrefixZenkakuNumberInternal(string str)
        {
            str = str._NonNullTrim();

            string[] tokens = str._Split(StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries, " ", "　", "\t");

            if (tokens.Length >= 1)
            {
                str = tokens[0];

                str = Str.HankakuToZenkaku(str);

                if (str.Length >= 3)
                {
                    if (str[1] == '－')
                    {
                        string tmp2 = Str.ZenkakuToHankaku(str).ToUpperInvariant();
                        if (tmp2[0] >= 'A' && tmp2[0] <= 'Z')
                        {
                            return str;
                        }
                    }
                }
            }
            return null;
        }

        static readonly FastCache<string, string> Cache_NormalizePrefixZenkakuNumberForSortFast = new FastCache<string, string>(int.MaxValue, comparer: StrComparer.SensitiveCaseTrimComparer);

        public static string? NormalizePrefixZenkakuNumberForSortFast(string str)
        {
            string? ret = Cache_NormalizePrefixZenkakuNumberForSortFast.GetOrCreate(str, s => NormalizePrefixZenkakuNumberForSortInternal(s));

            return ret;
        }

        static string? NormalizePrefixZenkakuNumberForSortInternal(string str)
        {
            string? normalized = NormalizePrefixZenkakuNumberFast(str);
            if (normalized._IsEmpty()) return null;

            string hankaku = Str.ZenkakuToHankaku(normalized).ToUpperInvariant();

            if (hankaku._GetKeyAndValue(out var a, out var b, "-"))
            {
                if (a._IsFilled() && b._IsFilled())
                {
                    if (a.Length == 1)
                    {
                        int i = b._DirtyStrToInt();

                        string tmp = a + "-" + i.ToString("D10");

                        return tmp.ToUpperInvariant();
                    }
                }
            }

            return null;
        }

        public static string? FileNameToPrefixZenkakuNumber(string filename)
        {
            string tmp1 = PP.GetFileNameWithoutExtension(filename);
            string[] tokens = tmp1._Split(StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries, " ", "\t", "　");
            if (tokens.Length >= 2)
            {
                string receiptNumber = tokens[0];
                if (receiptNumber[1] == '－')
                {
                    string tmp2 = Str.ZenkakuToHankaku(receiptNumber).ToUpperInvariant();

                    if (tmp2[0] >= 'A' && tmp2[0] <= 'Z')
                    {
                        return receiptNumber;
                    }
                }
            }

            return null;
        }

        public static string InsertStrIntoStr(string targetStr, string insertStr, int position, bool allowInsertAtEoL = false)
        {
            if (insertStr._IsNullOrZeroLen()) return targetStr;

            if (position < 0) position = 0;

            if (position > targetStr.Length) return targetStr;

            if (position == targetStr.Length)
            {
                if (allowInsertAtEoL == false)
                {
                    return targetStr;
                }
            }

            string s1 = targetStr.Substring(0, position);
            string s2 = targetStr.Substring(position);

            return s1 + insertStr + s2;
        }

        internal static readonly char[] standardSplitChars =
        {
            ' ', '　', '\t',
        };

        public static char[] StandardSplitChars
        {
            get
            {
                return (char[])standardSplitChars.Clone();
            }
        }

        public static KeyValueList<string, string> ConfigFileBodyToKeyValueList(string body)
        {
            var lines = body._GetLines(true, true);

            KeyValueList<string, string> configList = new KeyValueList<string, string>();

            foreach (var line in lines)
            {
                string line2 = line.TrimStart();

                if (line2._GetKeyAndValue(out string key, out string value))
                {
                    if (key._IsFilled())
                    {
                        configList.Add(key, value.Trim());
                    }
                }
            }

            return configList;
        }

        public static string KeyValueStrListToConfigFileBody(KeyValueList<string, string> config, StrDictionary<string>? commentsDict = null)
        {
            StringWriter w = new StringWriter();

            int keyStandardLength = 50;

            int keyStandardLength2 = config.Select(x => x.Key._NonNullTrim().Length).Max();

            keyStandardLength = Math.Max(keyStandardLength, keyStandardLength2);

            w.WriteLine("### Configuration Text");

            string lastKey = "";

            foreach (var kv in config)
            {
                int len1 = kv.Key.Length;
                string padding = "";

                if (len1 < keyStandardLength)
                {
                    padding = Str.MakeCharArray(' ', keyStandardLength - len1);
                }

                string line = $"{kv.Key}{padding} {kv.Value}";

                if (lastKey._IsDiffi(kv.Key))
                {
                    lastKey = kv.Key;
                    w.WriteLine();

                    string? commentStr = commentsDict?._GetOrDefault(kv.Key, "") ?? null;
                    if (commentStr._IsFilled())
                    {
                        foreach (string commentLine in commentStr._GetLines())
                        {
                            if (commentLine._IsFilled())
                            {
                                w.WriteLine("# " + commentLine.TrimEnd());
                            }
                        }
                    }
                }

                w.WriteLine(line);
            }

            w.WriteLine();
            w.WriteLine();

            return w.ToString();
        }

        // ユーティリティ関数: "IP/サブネットマスク" 形式の表記かどうか検索する
        public static bool IsIpSubnetStr(string str)
        {
            str = str._NonNull();
            int i = str._Search("/");
            if (i == -1)
            {
                if (IPAddress.TryParse(str, out _))
                {
                    return true;
                }
            }
            else
            {
                string ip = str.Substring(0, i).Trim();
                string mask = str.Substring(i + 1).Trim();
                if (IPAddress.TryParse(ip, out _))
                {
                    if (int.TryParse(mask, out _) || IPAddress.TryParse(mask, out _))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        // 指定されたエンコード一覧を順に試行し、最初に表現可能なエンコードを返す
        public static Encoding GetBestSuitableEncoding(string str, IEnumerable<Encoding?>? canditateList = null)
        {
            if (canditateList == null) canditateList = SuitableEncodingListForJapaneseWin32;

            foreach (var candidate in canditateList)
            {
                if (candidate != null)
                {
                    if (IsSuitableEncodingForString(str, candidate))
                    {
                        return candidate;
                    }
                }
            }

            return Str.Utf8Encoding;
        }

        public static string NormalizeStrForSearch(string s)
        {
            s = s._NonNullTrim().ToLowerInvariant();

            Str.NormalizeString(ref s, false, true, false, true);

            s = s._Split(StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries, ' ', '\t', '\r', '\n', '|', '\'', '\"')._Combine(" ");

            return s;
        }

        public static string SqlServerEscapeLikeToken(string src, char escapeChar = '!')
        {
            src = src._NonNull();

            if (escapeChar == '%' || escapeChar == '_' || escapeChar == '[' || escapeChar == ']' || escapeChar == '^' || escapeChar == '-')
            {
                throw new CoresLibException($"Invalid escape char: '{escapeChar}'");
            }

            src = src._ReplaceStr("" + escapeChar, "" + escapeChar + escapeChar);
            src = src._ReplaceStr("%", "" + escapeChar + "%");
            src = src._ReplaceStr("_", "" + escapeChar + "_");
            src = src._ReplaceStr("[", "" + escapeChar + "[");
            src = src._ReplaceStr("]", "" + escapeChar + "]");
            src = src._ReplaceStr("^", "" + escapeChar + "^");
            src = src._ReplaceStr("-", "" + escapeChar + "-");

            return src;
        }

        public static List<string> GetSearchableStrListFromPrimitiveData(object? o, SearchableStrFlag flag, string prefix = "")
        {
            HashSet<string> tmp = new HashSet<string>();

            void AddBytes(ReadOnlySpan<byte> byteArray)
            {
                tmp.Add(byteArray._GetHexString().ToLowerInvariant());

                if (flag.Bit(SearchableStrFlag.IncludeBase64InBinary))
                {
                    tmp.Add(byteArray._Base64Encode().ToLowerInvariant());
                }

                if (flag.Bit(SearchableStrFlag.IncludePrintableAsciiStr))
                {
                    tmp.Add(Str.MakeAsciiOneLinePrintableStr(byteArray).ToLowerInvariant());
                }
            }

            if (o != null && o._IsEmpty() == false)
            {
                if (o is byte[] byteArray)
                {
                    AddBytes(byteArray);
                }
                else if (o is Memory<byte> ms1)
                {
                    AddBytes(ms1.Span);
                }
                else if (o is ReadOnlyMemory<byte> ms2)
                {
                    AddBytes(ms2.Span);
                }
                else if (o is Enum e)
                {
                    tmp.Add(e.ToString().ToLowerInvariant()._ReplaceStr(", ", " ")._ReplaceStr(",", " "));

                    ulong value = Convert.ToUInt64(o);

                    tmp.Add(value.ToString());
                }
                else if (o._IsSignedIntegerObj())
                {
                    long s64 = Convert.ToInt64(o);
                    tmp.Add(s64.ToString());
                    tmp.Add(s64._ToString3());
                }
                else if (o._IsUnsignedIntegerObj())
                {
                    ulong u64 = Convert.ToUInt64(o);
                    tmp.Add(u64.ToString());
                    tmp.Add(u64._ToString3());
                }
                else if (o is decimal d)
                {
                    tmp.Add(d.ToString());
                }
                else if (o is float f1)
                {
                    tmp.Add(f1.ToString("F15"));
                    tmp.Add(f1.ToString("F3"));
                    tmp.Add(f1.ToString("F6"));
                }
                else if (o is double f2)
                {
                    tmp.Add(f2.ToString("F15"));
                    tmp.Add(f2.ToString("F3"));
                    tmp.Add(f2.ToString("F6"));
                }
                else if (o is bool b)
                {
                    tmp.Add(b ? "true" : "false");
                    tmp.Add(b ? "1" : "0");
                    tmp.Add(b ? "yes" : "no");
                }
                else if (o is DateTime dt)
                {
                    tmp.Add(dt._ToDtStr(true, withNanoSecs: true));
                    tmp.Add(dt._ToYymmddStr() + dt._ToHhmmssStr());
                    tmp.Add(dt._ToYymmddStr() + " " + dt._ToHhmmssStr());
                }
                else if (o is DateTimeOffset dto)
                {
                    tmp.Add(dto._ToDtStr(true, withNanoSecs: true));
                    tmp.Add(dto.UtcDateTime._AsDateTimeOffset(false, true)._ToDtStr(true));
                    tmp.Add(dto.UtcDateTime._AsDateTimeOffset(false, true)._ToDtStr(true, withNanoSecs: true));
                    tmp.Add(dto.UtcDateTime._AsDateTimeOffset(false, false)._ToDtStr(true));
                    tmp.Add(dto.UtcDateTime._AsDateTimeOffset(false, false)._ToDtStr(true, withNanoSecs: true));

                    tmp.Add(dto._ToYymmddStr() + dto._ToHhmmssStr());
                    tmp.Add(dto._ToYymmddStr() + " " + dto._ToHhmmssStr());
                    tmp.Add(dto.UtcDateTime._AsDateTimeOffset(false, true)._ToYymmddHhmmssLong().ToString());
                    tmp.Add(dto.UtcDateTime._AsDateTimeOffset(false, false)._ToYymmddHhmmssLong().ToString());
                }
                else if (o is TimeSpan ts)
                {
                    tmp.Add(ts._ToTsStr(true));
                    tmp.Add(ts.TotalMilliseconds.ToString());
                    tmp.Add(ts.TotalSeconds.ToString());
                }
                else
                {
                    string s;

                    if (o is string str)
                    {
                        s = str._NonNullTrim();
                    }
                    else
                    {
                        s = o.ToString()._NonNullTrim();
                    }

                    tmp.Add(NormalizeStrForSearch(s));
                }
            }

            List<string> ret = new List<string>();

            foreach (var s in tmp)
            {
                if (s._IsFilled())
                {
                    ret.Add((prefix + s).Trim());
                }
            }

            return ret;
        }

        public static List<string> GetSearchableStrListFromObject(object? obj, SearchableStrFlag flag = SearchableStrFlag.Default)
        {
            List<string> tmp = new List<string>();

            try
            {
                var walkList = Util.WalkObject(obj, flag.Bit(SearchableStrFlag.FastMode));

                foreach (var item in walkList)
                {
                    try
                    {
                        var set = GetSearchableStrListFromPrimitiveData(item.Data, flag, flag.Bit(SearchableStrFlag.PrependFieldName) ? item.Name.ToLowerInvariant() + "=" : "");

                        foreach (var s in set)
                        {
                            if (s._IsFilled())
                            {
                                tmp.Add(s.Trim());
                            }
                        }
                    }
                    catch { }
                }
            }
            catch { }

            return tmp;
        }

        public static string GenerateSearchableStrFromObject(object? obj, SearchableStrFlag flag = SearchableStrFlag.Default)
        {
            var list = GetSearchableStrListFromObject(obj, flag);

            if (list.Count >= 1)
            {
                list.Insert(0, "");
                list.Add("");
            }

            return list._Combine(" | ").Trim();
        }

        public static void Test_SearchableStr()
        {
            SSTest1 t1 = new SSTest1
            {
                str1 = "Banana",
                str2 = "Carot",
                u8 = 234,
                s8 = -124,
                u16 = 54321,
                s16 = -12345,
                u32 = 3123456789,
                s32 = -2012345678,
                s64 = -9223372036854775123,
                u64 = 18446744073709550616,
                dt1 = "2022/03/25 01:23:45"._ToDateTime(),
                ts1 = TimeSpan.FromMilliseconds(3600 * 24 * 1000).Add(TimeSpan.FromSeconds(-1)),
                b1 = true,
                decimal1 = 89454141,
                double1 = 3.1415926535897932D,
                float1 = 2.718281828459F,
                flag1 = SSTestFlag1.Microsoft | SSTestFlag1.Oracle,
                ip1 = "2001:af80:1:2:3::8931"._StrToIP()!,
                data1 = "ABCDEFG"._GetBytes_UTF8(),
                data2 = "abcdefg"._GetBytes_UTF8(),
            };

            SSTest1 t2 = new SSTest1
            {
                str1 = "Super",
                str2 = "Nekosan",
                u8 = 134,
                s8 = -104,
                u16 = 53321,
                s16 = -10345,
                u32 = 3113456789,
                s32 = -2019345678,
                s64 = -9223311036854775123,
                u64 = 10446744111119550616,
                dt1 = "2099/03/25 01:23:45"._ToDateTime(),
                ts1 = TimeSpan.FromMilliseconds(3600 * 24 * 1000).Add(TimeSpan.FromSeconds(-1)),
                b1 = true,
                decimal1 = 1689454141,
                double1 = 3.1415926535897932D,
                float1 = 2.718281828459F,
                flag1 = SSTestFlag1.Microsoft | SSTestFlag1.Oracle,
                ip1 = "2001:cafe:1:2:3::8931"._StrToIP()!,
                data1 = "xxxx"._GetBytes_UTF8(),
                data2 = "zzzz"._GetBytes_UTF8(),
            };

            t1.Child = t2;

            string ss = GenerateSearchableStrFromObject(t1, flag: SearchableStrFlag.Default | SearchableStrFlag.PrependFieldName);

            //ss._Print();

            string test = "| str2=carot | str1=banana | u8=234 | s8=-124 | u16=54321 | u16=54,321 | s16=-12345 | s16=-12,345 | u32=3123456789 | u32=3,123,456,789 | s32=-2012345678 | s32=-2,012,345,678 | u64=18446744073709550616 | u64=18,446,744,073,709,550,616 | s64=-9223372036854775123 | s64=-9,223,372,036,854,775,123 | dt1=2022/03/25 01:23:45.0000000 | dt1=20220325012345 | dt1=20220325 012345 | ts1=23:59:59.000 | ts1=86399000 | ts1=86399 | b1=true | b1=1 | b1=yes | double1=3.141592653589793 | double1=3.142 | double1=3.141593 | float1=2.718281745910645 | float1=2.718 | float1=2.718282 | flag1=microsoft oracle | flag1=3 | ip1=2001:af80:1:2:3::8931 | data1=41424344454647 | data1=qujdrevgrw== | data1=abcdefg | data2=61626364656667 | data2=ywjjzgvmzw== | data2=abcdefg | str2=nekosan | str1=super | u8=134 | s8=-104 | u16=53321 | u16=53,321 | s16=-10345 | s16=-10,345 | u32=3113456789 | u32=3,113,456,789 | s32=-2019345678 | s32=-2,019,345,678 | u64=10446744111119550616 | u64=10,446,744,111,119,550,616 | s64=-9223311036854775123 | s64=-9,223,311,036,854,775,123 | dt1=2099/03/25 01:23:45.0000000 | dt1=20990325012345 | dt1=20990325 012345 | ts1=23:59:59.000 | ts1=86399000 | ts1=86399 | b1=true | b1=1 | b1=yes | double1=3.141592653589793 | double1=3.142 | double1=3.141593 | float1=2.718281745910645 | float1=2.718 | float1=2.718282 | flag1=microsoft oracle | flag1=3 | ip1=2001:cafe:1:2:3::8931 | data1=78787878 | data1=ehh4ea== | data1=xxxx | data2=7a7a7a7a | data2=enp6eg== | data2=zzzz |";

            Dbg.TestTrue(test == ss);
        }

        [Flags]
        public enum SSTestFlag1
        {
            Apple = 0,
            Microsoft = 1,
            Oracle = 2,
        }

        public class SSTest1
        {
            public string str1 = "";
            public string str2 { get; set; } = "";
            public byte u8;
            public sbyte s8;
            public ushort u16;
            public short s16;
            public uint u32;
            public int s32;
            public ulong u64;
            public long s64;
            public DateTime dt1;
            public TimeSpan ts1;
            public bool b1;
            public decimal decimal1;
            public double double1;
            public float float1;
            public SSTestFlag1 flag1;
            public IPAddress ip1 = null!;
            public byte[] data1 = null!;
            public Memory<byte> data2;

            public SSTest1? Child;
        }


        public static void RunStartupTest()
        {
            string s0 = "こんにちは　独立行政法人 情報処理推進機構";
            string s1 = Res.Cores.EasyReadString("CoresInternal/220807_strtest_01_utf8.txt", encoding: Str.Utf8Encoding)._GetFirstFilledLineFromLines();
            string s2 = Res.Cores.EasyReadString("CoresInternal/220807_strtest_02_sjis.txt", encoding: Str.ShiftJisEncoding)._GetFirstFilledLineFromLines();
            string s3 = Res.Cores.EasyReadString("CoresInternal/220807_strtest_03_unicode.txt", encoding: Str.UniEncoding)._GetFirstFilledLineFromLines();

            Dbg.TestTrue(s0 == s1);
            Dbg.TestTrue(s0 == s2);
            Dbg.TestTrue(s0 == s3);
        }

        // Web Linking (RFC 8288) のパース
        public static Tuple<string, KeyValueList<string, string>> ParseWebLinkStr(string src)
        {
            // ; で区切る
            string[] tokens = src._Split(StringSplitOptions.TrimEntries, ';');

            string urlStr = tokens[0];

            if (urlStr.StartsWith("<") == false || urlStr.EndsWith(">") == false)
            {
                throw new CoresLibException("Invalid Web Linking URL String");
            }

            urlStr = urlStr.Substring(1, urlStr.Length - 2);

            if (urlStr._IsEmpty()) throw new CoresLibException("Invalid Web Linking URL String");

            KeyValueList<string, string> x = new KeyValueList<string, string>();

            for (int i = 1; i < tokens.Length; i++)
            {
                string kvstr = tokens[i];

                var tk = kvstr._Split(StringSplitOptions.RemoveEmptyEntries, '=');

                x.Add(tk[0]._RemoveQuotation(), tk[1]._RemoveQuotation());
            }

            return new Tuple<string, KeyValueList<string, string>>(urlStr, x);
        }

        public static string MakeStringUseOnlyChars(string src, string charList = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ-", StringComparison comparison = StringComparison.Ordinal)
        {
            src = src._NonNull();

            StringBuilder sb = new StringBuilder(src.Length);

            foreach (char c in src)
            {
                if (charList.IndexOf(c, StringComparison.Ordinal) != -1)
                    sb.Append(c);
            }

            return sb.ToString();
        }

        public static bool CheckUseOnlyChars(string src, Exception? exceptionToThrow = null, string charList = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ-", StringComparison comparison = StringComparison.Ordinal)
        {
            src = src._NonNull();

            foreach (char c in src)
            {
                if (charList.IndexOf(c, StringComparison.Ordinal) == -1)
                {
                    if (exceptionToThrow != null)
                    {
                        throw exceptionToThrow;
                    }
                    else
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        public static string PrependIndent(string src, int indentWidth = 4, char indentChar = ' ', CrlfStyle crlfStyle = CrlfStyle.LocalPlatform)
        {
            var lines = src._GetLines();

            StringWriter w = new StringWriter();

            string padding = indentChar._MakeCharArray(indentWidth);

            foreach (var line in lines)
            {
                w.WriteLine(padding + line);
            }

            return w.ToString()._NormalizeCrlf(crlfStyle, true);
        }

        static readonly CriticalSection LockNewId = new CriticalSection();
        static ulong LastNewIdMSecs = 0;

        public static Tuple<string, int> ParseHostnaneAndPort(string str, int defaultPort)
        {
            str = str._NonNull();

            string hoststr = "";
            string portstr = "";

            // hostname1:hostname2:port のようになっている可能性があるので、最後の ':' の出現場所を取得する
            for (int i = 0; i < str.Length - 1; i++)
            {
                char c = str[i];

                if (c == ':')
                {
                    hoststr = str.Substring(0, i);
                    portstr = str.Substring(i + 1);
                }
            }

            if (portstr._IsNumber())
            {
                return new Tuple<string, int>(hoststr.Trim(), portstr._ToInt());
            }
            else
            {
                return new Tuple<string, int>(str.Trim(), defaultPort);
            }
        }

        public static bool IsValidFqdn(string fqdn, bool allowWildcard = false, bool wildcardFirstTokenMustBeSimple = false)
            => Str.CheckFqdn(fqdn, allowWildcard, wildcardFirstTokenMustBeSimple);

        [return: NotNullIfNotNull("fqdn")]
        public static string? ReverseFqdnStr(string? fqdn, out int numTokens)
        {
            numTokens = 0;
            if (fqdn == null) return null;

            var tmpTokens = fqdn._Split(StringSplitOptions.None, '.');

            numTokens = tmpTokens.Length;

            return tmpTokens.Reverse()._Combine(".", estimatedLength: fqdn.Length);
        }
        [return: NotNullIfNotNull("fqdn")]
        public static string? ReverseFqdnStr(string? fqdn) => ReverseFqdnStr(fqdn, out _);

        public static Memory<byte> CHexArrayToBinary(string body)
        {
            SpanBuffer<byte> buf = new SpanBuffer<byte>();

            string[] lines = body._GetLines(true, true);

            foreach (string line in lines)
            {
                string[] tokens = line._Split(StringSplitOptions.RemoveEmptyEntries, ' ', '\t', '　', '{', '}', ',', '\r', '\n');

                foreach (string token in tokens)
                {
                    string tmp = token.Trim();

                    if (tmp.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                    {
                        string tmp2 = tmp._Slice(2);

                        buf.WriteOne(Convert.ToByte(tmp2, 16));
                    }
                }
            }

            return buf.Span.ToArray();
        }

        // 電話番号かどうか判定
        public static bool IsPhoneNumber(string? str)
        {
            if (str._IsEmpty()) return false;

            return str.All(x => (x >= '0' && x <= '9') || x == ' ' || x == '+' || x == '(' || x == ')');
        }

        // 指定されたディレクトリ名が "YYMMDD_なんとか" または "YYMMDD なんとか" または "YYYYMMDD_なんとか" または "YYYYMMDD なんとか" である場合は日付を返す
        static readonly char[] YymmddSplitChars = new char[] { '_', ' ', '　', '\t' };
        public static bool TryParseYYMMDDDirName(string name, out DateTime date)
        {
            date = default;

            name = name._TrimNonNull();

            try
            {
                int r = name.IndexOfAny(YymmddSplitChars);
                if ((r == 6 || r == 8) && name.Substring(0, r).All(x => x >= '0' && x <= '9'))
                {
                    name = name.Substring(0, r);
                }
                else
                {
                    if (name.Length >= 7 && name.Substring(0, 6).All(x => x >= '0' && x <= '9') && (!(name[6] >= '0' && name[6] <= '9')))
                    {
                        name = name.Substring(0, 6);
                    }
                    else if (name.Length >= 9 && name.Substring(0, 8).All(x => x >= '0' && x <= '9') && (!(name[8] >= '0' && name[8] <= '9')))
                    {
                        name = name.Substring(0, 8);
                    }
                }

                return TryParseYYMMDD(name, out date);
            }
            catch
            {
                return false;
            }
        }

        // HHMMSS をパースする
        public static bool TryParseHHMMSS(string str, out TimeSpan ts)
        {
            ts = TimeSpan.Zero;

            try
            {
                if (str.Length == 6 && str.All(x => x >= '0' && x <= '9'))
                {
                    if (str == "000000")
                    {
                        ts = TimeSpan.Zero;
                        return true;
                    }

                    if (str == "999999")
                    {
                        ts = TimeSpan.MaxValue;
                        return true;
                    }


                    int hour = str.Substring(0, 2)._ToInt();
                    int minute = str.Substring(2, 2)._ToInt();
                    int second = str.Substring(4, 2)._ToInt();

                    var dtThis = new DateTime(2000, 1, 1, hour, minute, second);
                    var dtBase = new DateTime(2000, 1, 1, 0, 0, 0);

                    ts = dtThis - dtBase;

                    return true;
                }
            }
            catch { }

            return false;
        }

        // YYMMDD または YYYYMMDD をパースする
        public static bool TryParseYYMMDD(string str, out DateTime date)
        {
            date = default;

            try
            {
                if (str.Length == 6 && str.All(x => x >= '0' && x <= '9'))
                {
                    if (str == "000000")
                    {
                        date = Util.ZeroDateTimeValue;
                        return true;
                    }

                    if (str == "999999")
                    {
                        date = Util.MaxDateTimeValue;
                        return true;
                    }


                    int year = str.Substring(0, 2)._ToInt();
                    int month = str.Substring(2, 2)._ToInt();
                    int day = str.Substring(4, 2)._ToInt();

                    date = new DateTime(year + 2000, month, day);
                    return true;
                }
                else if (str.Length == 8 && str.All(x => x >= '0' && x <= '9'))
                {
                    if (str == "00000000")
                    {
                        date = Util.ZeroDateTimeValue;
                        return true;
                    }

                    if (str == "99999999")
                    {
                        date = Util.MaxDateTimeValue;
                        return true;
                    }

                    int year = str.Substring(0, 4)._ToInt();
                    int month = str.Substring(4, 2)._ToInt();
                    int day = str.Substring(6, 2)._ToInt();

                    date = new DateTime(year, month, day);
                    return true;
                }
            }
            catch { }

            return false;
        }

        // 複数のワイルドカードパターンにある文字列が一致するかどうか検査
        public static bool MultipleWildcardMatch(string targetStr, string multipleWildcardList, string excludeMultipleWildcardList, bool ignoreCase = false, bool doNotUseCache = false)
        {
            targetStr = targetStr._NonNull();
            multipleWildcardList = multipleWildcardList._NonNull();
            excludeMultipleWildcardList = excludeMultipleWildcardList._NonNull();

            var wildcardList = multipleWildcardList._Split(StringSplitOptions.RemoveEmptyEntries, "|", ",", ";", " ").Where(x => x._IsFilled());

            if (wildcardList.Any() == false) return false;

            var excludeList = excludeMultipleWildcardList._Split(StringSplitOptions.RemoveEmptyEntries, "|", ",", ";", " ").Where(x => x._IsFilled());

            foreach (string exclude in excludeList)
            {
                if (WildcardMatch(targetStr, exclude, ignoreCase, doNotUseCache))
                {
                    return false;
                }
            }

            foreach (string wildcard in wildcardList)
            {
                if (WildcardMatch(targetStr, wildcard, ignoreCase, doNotUseCache))
                {
                    return true;
                }
            }

            return false;
        }
        public static bool MultipleWildcardMatch(string targetStr, string multipleWildcard, bool ignoreCase = false, bool doNotUseCache = false)
            => MultipleWildcardMatch(targetStr, multipleWildcard, "", ignoreCase, doNotUseCache);

        static FastCache<string, Regex> WildcardObjectCache = new FastCache<string, Regex>(CoresConfig.String.CachedWildcardObjectsExpires, 0, CacheType.UpdateExpiresWhenAccess);

        // ワイルドカード一致検査
        public static bool WildcardMatch(string targetStr, string wildcard, bool ignoreCase = false, bool doNotUseCache = false)
        {
            if (wildcard._IsEmpty()) return false;

            if (ignoreCase)
            {
                targetStr = targetStr.ToUpperInvariant();
                wildcard = wildcard.ToUpperInvariant();
            }

            try
            {
                if (doNotUseCache)
                {
                    string pattern = WildcardToRegex(wildcard);

                    if (new Regex(pattern).IsMatch(targetStr))
                    {
                        return true;
                    }
                }
                else
                {
                    Regex? r = WildcardObjectCache.GetOrCreate(wildcard, wc =>
                    {
                        string pattern = WildcardToRegex(wc);
                        return new Regex(pattern);
                    });

                    if (r != null)
                    {
                        return r.IsMatch(targetStr);
                    }
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        // ワイルドカード文字列を正規表現に変換
        public static string WildcardToRegex(string wildcard)
        {
            // https://qiita.com/kazuhirox/items/5e314d5e7732041a3fe7 を参考にいたしました

            var regexPattern = System.Text.RegularExpressions.Regex.Replace(wildcard, ".",
              m =>
              {
                  string s = m.Value;
                  if (s.Equals("?"))
                  {
                      return ".";
                  }
                  else if (s.Equals("*"))
                  {
                      return ".*";
                  }
                  else
                  {
                      return System.Text.RegularExpressions.Regex.Escape(s);
                  }
              }
              );

            return "^" + regexPattern + "$";
        }

        // ランダム MAC アドレスを生成
        public static string GenRandomMacStr(string seedStr, byte firstByte = 0xAE, string paddingStr = "-", bool lowerStr = false)
        {
            return MacToStr(IPUtil.GenRandomMac(seedStr, firstByte).Span, paddingStr, lowerStr);
        }
        public static string GenRandomMacStr(string seedStr, MacAddressStyle style, byte firstByte = 0xAE)
        {
            return MacToStr(IPUtil.GenRandomMac(seedStr, firstByte).Span, style);
        }

        // MAC アドレスをパース
        public static byte[] StrToMac(string src)
        {
            byte[] ret = Str.HexToByte(src);
            if (ret.Length != 6)
            {
                return new byte[6];
            }
            return ret;
        }

        // MAC アドレスを文字列に変換
        public static string MacToStr(ReadOnlySpan<byte> mac, string paddingStr = "-", bool lowerStr = false)
        {
            if (mac.Length > 6)
            {
                mac = mac.Slice(0, 6);
            }
            else if (mac.Length < 6)
            {
                Span<byte> mac2 = new byte[6];
                mac.CopyTo(mac2);
                mac = mac2;
            }

            string str = Str.ByteToHex(mac, paddingStr);

            if (lowerStr) str = str.ToLowerInvariant();

            return str;
        }
        public static string MacToStr(ReadOnlySpan<byte> mac, MacAddressStyle style)
        {
            switch (style)
            {
                case MacAddressStyle.Linux:
                    return MacToStr(mac, ":", true);

                default:
                    return MacToStr(mac, "-", false);
            }
        }

        // MAC アドレスを正規化
        public static string NormalizeMac(string src, string paddingStr = "-", bool lowerStr = false)
        {
            byte[] data = Str.StrToMac(src);
            return Str.MacToStr(data, paddingStr, lowerStr);
        }
        public static string NormalizeMac(string src, MacAddressStyle style)
        {
            byte[] data = Str.StrToMac(src);
            return Str.MacToStr(data, style);
        }

        // 16 進数文字列を正規化
        public static string NormalizeHexString(string? src, bool lowerCase = false, string padding = "")
        {
            src = src._NonNullTrimSe();
            byte[] data = src._GetHexBytes();
            string ret = data._GetHexString(padding);

            if (lowerCase)
                ret = ret.ToLowerInvariant();

            return ret;
        }

        // エラー文字列のシリアライズ
        public static string SerializeErrorStr(string code, string lang, string msg)
        {
            Str.NormalizeString(ref code);
            Str.NormalizeString(ref lang);
            Str.NormalizeString(ref msg);

            code = Str.ReplaceStr(code, ",", ".");
            code = Str.ReplaceStr(code, "{", "[");
            code = Str.ReplaceStr(code, "}", "]");

            lang = Str.ReplaceStr(lang, ",", ".");
            lang = Str.ReplaceStr(lang, "{", "[");
            lang = Str.ReplaceStr(lang, "}", "]");

            msg = Str.ReplaceStr(msg, "{", "[");
            msg = Str.ReplaceStr(msg, "}", "]");

            return "{ErrorCode=" + code + "," + lang + "," + msg + "}";
        }

        // シリアライズされたエラー文字列の展開
        public static SerializedError[] DeserializeErrorStr(string str)
        {
            List<SerializedError> ret = new List<SerializedError>();
            int pos = 0;
            while (true)
            {
                int i = Str.SearchStr(str, "{ErrorCode=", pos);
                if (i == -1)
                {
                    break;
                }

                pos = i + 11;

                // code の取得
                i = Str.SearchStr(str, ",", pos);
                if (i == -1)
                {
                    break;
                }

                string code = str.Substring(pos, (i - pos));
                pos = i + 1;

                // lang の取得
                i = Str.SearchStr(str, ",", pos);
                if (i == -1)
                {
                    break;
                }

                string lang = str.Substring(pos, (i - pos));
                pos = i + 1;

                // msg の取得
                i = Str.SearchStr(str, "}", pos);
                if (i == -1)
                {
                    break;
                }

                string msg = str.Substring(pos, (i - pos));
                pos = i + 1;

                Str.NormalizeString(ref code);
                Str.NormalizeString(ref lang);
                Str.NormalizeString(ref msg);

                SerializedError r = new SerializedError()
                {
                    Code = code,
                    ErrorMsg = msg,
                    Language = lang,
                };

                ret.Add(r);
            }

            return ret.ToArray();
        }

        // 新しい ID を生成する
        public static string NewFullId(string prefix)
        {
            // ID-AAAAAAAAAA-BBB-CCDDDDDEEEEEEE-<ID>-FFFFF-GGGGG

            // AAAAAAAAAA: 10 桁の整数 2000/01/01 からの秒数
            // BBB: 3 桁の整数 2000/01/01 からのミリ秒数
            // DDDDD および EEEEEEE: 乱数
            // FFFFF: 2015/08/10 からの秒数を 9600 で割った値
            // GGGGG: 乱数
            // CC: チェックサム (AAAAAAAAAABBBDDDDDEEEEEFFFFFGGGGG<ID> のハッシュ)
            prefix = prefix._NonNullTrim();

            DateTime start_dt = new DateTime(2000, 1, 1);
            DateTime now = DateTime.Now;

            ulong msecs = (ulong)Util.ConvertTimeSpan(now - start_dt);

            lock (LockNewId)
            {
                if (LastNewIdMSecs >= msecs)
                {
                    msecs = LastNewIdMSecs + 1;
                }

                LastNewIdMSecs = msecs;
            }

            string a = ((msecs / 1000UL) % 10000000000UL).ToString("D10");
            string b = (msecs % 1000UL).ToString("D3");
            string d = (Secure.RandUInt64() % 100000UL).ToString("D5");
            string e = (Secure.RandUInt64() % 100000UL).ToString("D5");
            string f = (((ulong)((now - new DateTime(2015, 8, 10)).TotalSeconds / 9600.0)) % 100000UL).ToString("D5");
            string g = (Secure.RandUInt64() % 100000UL).ToString("D5");
            string hash_str = a + b + d + e + f + g + prefix.ToUpperInvariant();
            byte[] hash = Secure.HashSHA1(Str.AsciiEncoding.GetBytes(hash_str));
            Buf buf = new Buf(hash);
            string c = (buf.ReadInt64() % 100000UL).ToString("D5");

            return "ID-" + a + "-" + b + "-" + c + d + e + "-" + prefix.ToUpperInvariant() + "-" + f + "-" + g;
        }

        // UID を正規化する
        public static string NormalizeUid(string? uid, int maxStrLength = Consts.Numbers.MaxKeyOrLabelStrLength)
        {
            string ret = uid._NonNullTrim().ToUpperInvariant();

            if (maxStrLength != int.MaxValue && maxStrLength < 0)
            {
                ret._CheckSqlMaxSafeStrLength(maxStrLength: maxStrLength);
            }

            ret._CheckAsciiPrintableOneLine();

            return ret;
        }

        // 任意のキーを正規化する
        public static string NormalizeKey(string? key, bool checkAsciiSafe = false, int maxStrLength = Consts.Numbers.MaxKeyOrLabelStrLength)
        {
            string ret = key._NonNullTrim().ToUpperInvariant();

            if (maxStrLength != int.MaxValue && maxStrLength < 0)
            {
                ret._CheckSqlMaxSafeStrLength(maxStrLength: maxStrLength);
            }

            if (checkAsciiSafe)
            {
                ret._CheckAsciiPrintableOneLine();
            }

            return ret;
        }

        // 新しい UID を生成する
        public static string NewUid(string prefix = "UID", char concat = '-', bool prependAtoZHashChar = false)
        {
            // <PREFIX>-AAAAAAAAAA-BBB-CCDDDDDEEEEEEE-FFFFF-GGGGG

            // AAAAAAAAAA: 10 桁の整数 2000/01/01 からの秒数
            // BBB: 3 桁の整数 2000/01/01 からのミリ秒数
            // DDDDD および EEEEEEE: 乱数
            // FFFFF: 2015/08/10 からの秒数を 9600 で割った値
            // GGGGG: 乱数
            // CC: チェックサム (AAAAAAAAAABBBDDDDDEEEEEFFFFFGGGGG<ID> のハッシュ)
            prefix = prefix._NonNullTrim();

            if (prefix._IsNullOrZeroLen()) prefix = "UID";

            DateTime start_dt = new DateTime(2000, 1, 1);
            DateTime now = DateTime.Now;

            ulong msecs = (ulong)Util.ConvertTimeSpan(now - start_dt);

            lock (LockNewId)
            {
                if (LastNewIdMSecs >= msecs)
                {
                    msecs = LastNewIdMSecs + 1;
                }

                LastNewIdMSecs = msecs;
            }

            string a = ((msecs / 1000UL) % 10000000000UL).ToString("D10");
            string b = (msecs % 1000UL).ToString("D3");
            string d = (Secure.RandUInt64() % 100000UL).ToString("D5");
            string e = (Secure.RandUInt64() % 100000UL).ToString("D5");
            string f = (((ulong)((now - new DateTime(2015, 8, 10)).TotalSeconds / 9600.0)) % 100000UL).ToString("D5");
            string g = (Secure.RandUInt64() % 100000UL).ToString("D5");
            string hash_str = a + b + d + e + f + g + prefix.ToUpperInvariant();
            byte[] hash = Secure.HashSHA1(Str.AsciiEncoding.GetBytes(hash_str));
            Buf buf = new Buf(hash);
            string c = (buf.ReadInt64() % 100000UL).ToString("D5");

            if (prependAtoZHashChar)
            {
                prefix = "" + (char)('A' + (Util.RandSInt31() % 26)) + concat + prefix;
            }

            return prefix.ToUpperInvariant() + concat + a + concat + b + concat + c + d + e + concat + f + concat + g;
        }
        // ID 文字列を短縮する
        public static string? GetShortId(string fullId)
        {
            Str.NormalizeString(ref fullId);
            fullId = fullId.ToUpperInvariant();

            if (fullId.StartsWith("ID-") == false)
            {
                return null;
            }

            string[] tokens = fullId.Split('-');
            if (tokens.Length != 7)
            {
                return null;
            }

            return tokens[4] + "-" + tokens[5] + "-" + tokens[6];
        }

        // バイト配列をバイナリ文字列に変換する
        public static string ByteToBinaryString(byte[] data)
        {
            StringBuilder sb = new StringBuilder();

            foreach (byte b in data)
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

        // バイナリ文字列をバイト配列に変換する
        //public static string BinaryString

        // 文字列をソフトイーサ表記規則に正規化する
        public static string NormalizeStrSoftEther(string? str, bool trim = false)
        {
            bool b = false;
            str = str._NonNull();

            str = UnicodeControlCodesNormalizeUtil.Normalize(str);

            StringReader sr = new StringReader(str);
            StringWriter sw = new StringWriter();
            while (true)
            {
                string? line = sr.ReadLine();
                if (line == null)
                {
                    break;
                }
                if (b)
                {
                    sw.WriteLine();
                }
                b = true;
                line = normalizeStrSoftEtherInternal(line);
                sw.Write(line);
            }

            int len = str.Length;

            try
            {
                if (str[len - 1] == '\n' || str[len - 1] == '\r')
                {
                    sw.WriteLine();
                }
            }
            catch
            {
            }

            str = sw.ToString();

            if (trim)
            {
                str = str.Trim();
            }

            return str;
        }
        static string normalizeStrSoftEtherInternal(string str)
        {
            if (str.Trim().Length == 0)
            {
                return "";
            }

            int i;
            StringBuilder sb1 = new StringBuilder();
            for (i = 0; i < str.Length; i++)
            {
                char c = str[i];

                if (c == ' ' || c == '　' || c == '\t')
                {
                    sb1.Append(c);
                }
                else
                {
                    break;
                }
            }
            string str2 = str.Substring(i).Trim();

            string str1 = sb1.ToString();

            str1 = ReplaceStr(str1, "　", "  ");
            str1 = ReplaceStr(str1, "\t", "    ");

            return str1 + normalizeStrSoftEtherInternal2(str2);
        }
        static string normalizeStrSoftEtherInternal2(string str)
        {
            NormalizeString(ref str, true, true, false, true);
            char[] chars = str.ToCharArray();
            StringBuilder sb = new StringBuilder();

            int i;
            for (i = 0; i < chars.Length; i++)
            {
                char c = chars[i];
                bool insert_space = false;
                bool insert_space2 = false;

                char c1 = (char)0;
                if (i >= 1)
                {
                    c1 = chars[i - 1];
                }

                char c2 = (char)0;
                if (i < (chars.Length - 1))
                {
                    c2 = chars[i + 1];
                }

                if (c == '\'' || c1 == '\'' || c2 == '\'' || c == '\"' || c1 == '\"' || c2 == '\"' || c == '>' || c1 == '>' || c2 == '>' || c == '<' || c1 == '<' || c2 == '<')
                {
                }
                else if (c == '(' || c == '[' || c == '{' || c == '<')
                {
                    // 括弧開始
                    if (c1 != '「' && c1 != '『' && c1 != '。' && c1 != '、' && c1 != '・')
                    {
                        insert_space = true;
                    }
                }
                else if (c == ')' || c == ']' || c == '}' || c == '>')
                {
                    // 括弧終了
                    if (c2 != '.' && c2 != ',' && c2 != '。' && c2 != '、')
                    {
                        insert_space2 = true;
                    }
                }
                else if (c == '～')
                {
                    if (c1 != '～')
                    {
                        insert_space = true;
                    }

                    if (c2 != '～')
                    {
                        insert_space2 = true;
                    }
                }
                else if (IsZenkaku(c) == false)
                {
                    // 半角
                    if (IsZenkaku(c1))
                    {
                        // 前の文字が全角
                        if (c != '.' && c != ',' && c != ';' && c != ':' && c1 != '※' && c1 != '〒' && c1 != '℡' && c1 != '「' && c1 != '『' && c1 != '。' && c1 != '、' && c1 != '・')
                        {
                            insert_space = true;
                        }
                    }
                }
                else
                {
                    // 全角
                    if (IsZenkaku(c1) == false)
                    {
                        // 前の文字が半角
                        if (c != '。' && c != '、' && c != '」' && c != '』' && c != '・' && c1 != '(' && c1 != '[' && c1 != '{' && c1 != '<' && c1 != ';' && c1 != ':')
                        {
                            insert_space = true;
                        }
                    }
                }

                if (insert_space)
                {
                    sb.Append(' ');
                }

                sb.Append(c);

                if (insert_space2)
                {
                    sb.Append(' ');
                }
            }

            str = sb.ToString();

            NormalizeString(ref str, true, true, false, true);

            return str;
        }

        // 指定した文字列を Git Commit ID として正規化する
        public static string NormalizeGitCommitId(string? src)
        {
            if (src._IsEmpty()) return "";

            if (TryNormalizeGitCommitId(src, out string dst))
            {
                return dst;
            }

            return "";
        }
        public static bool TryNormalizeGitCommitId(string? src, out string dst)
        {
            dst = "";

            if (src._IsEmpty()) return false;

            src = src._NonNullTrimSe();

            byte[] data = src._GetHexBytes();

            if (data.Length != 20)
                return false;

            dst = data._GetHexString().ToLowerInvariant();

            return true;
        }

        // 指定した文字が全角かどうか調べる
        public static bool IsZenkaku(char c)
        {
            return !((c >= (char)0) && (c <= (char)256));
        }

        // 文字列を可能性がある複数のトークンで分割する
        public static string[] DivideStringMulti(string str, bool caseSensitive, params string[] keywords)
        {
            List<string> ret = new List<string>();
            int next = 0;

            while (true)
            {
                int foundIndex;
                string foundKeyword;
                int r = Str.SearchStrMulti(str, next, caseSensitive, out foundIndex, out foundKeyword, keywords);
                if (r == -1)
                {
                    ret.Add(str.Substring(next));
                    break;
                }
                else
                {
                    ret.Add(str.Substring(next, r - next));
                    ret.Add(foundKeyword);
                    next = r + foundKeyword.Length;
                }
            }

            return ret.ToArray();
        }

        // 指定した文字が指定した複数のエンコードで表現できるかどうか検査
        public static bool IsSuitableCharForEncodings(char c, params Encoding[] encList)
        {
            foreach (var enc in encList)
            {
                if (IsSuitableCharForEncoding(c, enc) == false)
                {
                    return false;
                }
            }

            return true;
        }

        // 指定した文字が指定したエンコードで表現できるかどうか検査
        public static bool IsSuitableCharForEncoding(char c, Encoding enc)
        {
            return IsSuitableEncodingForString("" + c, enc);
        }

        // 指定した文字列が指定したエンコードで表現できるかどうか検査
        public static bool IsSuitableEncodingForString(string str, Encoding enc)
        {
            try
            {
                str = Str.NormalizeCrlf(str, CrlfStyle.CrLf);

                byte[] utf1 = Str.Utf8Encoding.GetBytes(str);

                byte[] b = enc.GetBytes(str);
                string str2 = enc.GetString(b);

                byte[] utf2 = Str.Utf8Encoding.GetBytes(str2);

                return Util.MemEquals(utf1, utf2);
            }
            catch
            {
                return false;
            }
        }

        // 文字が数字とアルファベットかどうか
        public static bool IsCharNumOrAlpha(char c)
        {
            if (c >= 'a' && c <= 'z')
            {
                return true;
            }
            if (c >= 'A' && c <= 'Z')
            {
                return true;
            }
            if (c >= '0' && c <= '9')
            {
                return true;
            }
            return false;
        }
        public static bool IsStringNumOrAlpha(string s)
        {
            foreach (char c in s)
            {
                if (IsCharNumOrAlpha(c) == false)
                {
                    return false;
                }
            }
            return true;
        }

        // 文字が数字のみかどうか
        public static bool IsCharNum(char c)
        {
            if (c >= '0' && c <= '9')
            {
                return true;
            }
            return false;
        }
        public static bool IsStringNum(string s)
        {
            foreach (char c in s)
            {
                if (IsCharNum(c) == false)
                {
                    return false;
                }
            }
            return true;
        }

        // 文字列を項目リストに分割する
        public static string[] StrToStrLineBySplitting(string str)
        {
            StringReader r = new StringReader(str);
            List<string> ret = new List<string>();

            while (true)
            {
                string? line = r.ReadLine();
                if (line == null)
                {
                    break;
                }

                if (IsEmptyStr(line) == false)
                {
                    ret.Add(line.Trim());
                }
            }

            return ret.ToArray();
        }

        // 前方から指定文字だけ取得する
        public static string GetLeft(string str, int len)
        {
            str = str._NonNull();

            if (str.Length > len)
            {
                return str.Substring(0, len);
            }
            else
            {
                return str;
            }
        }

        // 文字列を検索用にパースする
        public static string[] SplitStringForSearch(string str)
        {
            bool b = false;
            int i, len;
            len = str.Length;
            List<string> ret = new List<string>();
            string currentStr = "";

            for (i = 0; i < len; i++)
            {
                char c = str[i];

                if (c == '\"')
                {
                    b = !b;
                    if (b == false)
                    {
                        // " の終了
                        currentStr = currentStr.Trim();
                        if (Str.IsEmptyStr(currentStr) == false)
                        {
                            ret.Add(currentStr);
                            currentStr = "";
                        }
                    }
                }
                else
                {
                    if (b == false && (c == ' ' || c == '　' || c == '\t'))
                    {
                        // 区切り文字
                        currentStr = currentStr.Trim();
                        if (Str.IsEmptyStr(currentStr) == false)
                        {
                            ret.Add(currentStr);
                            currentStr = "";
                        }
                    }
                    else
                    {
                        currentStr += c;
                    }
                }
            }

            currentStr = currentStr.Trim();
            if (Str.IsEmptyStr(currentStr) == false)
            {
                ret.Add(currentStr);
            }

            return ret.ToArray();
        }

        // 数値の桁数が足りない場合にゼロを付ける
        public static string AppendZeroToNumString(string str, int numKeta)
        {
            int n = numKeta - str.Length;

            if (n >= 1)
            {
                return MakeCharArray('0', n) + str;
            }
            else
            {
                return str;
            }
        }

        // 指定したデータに BOM が付いているかどうか判別する
        [MethodImpl(Inline)]
        public static Encoding? CheckBOM(ReadOnlySpan<byte> data)
        {
            return CheckBOM(data, out _);
        }
        [MethodImpl(Inline)]
        public static Encoding? CheckBOM(ReadOnlySpan<byte> data, out int bomNumBytes)
        {
            bomNumBytes = 0;
            try
            {
                if (data.Length >= 3 && data[0] == 0x00 && data[1] == 0x00 && data[2] == 0xfe && data[3] == 0xff)
                {
                    bomNumBytes = 3;
                    return Encoding.GetEncoding("utf-32BE");
                }
                else if (data.Length >= 4 && data[0] == 0xff && data[1] == 0xfe && data[2] == 0x00 && data[3] == 0x00)
                {
                    bomNumBytes = 4;
                    return Encoding.GetEncoding("utf-32");
                }
                else if (data.Length >= 2 && data[0] == 0xff && data[1] == 0xfe)
                {
                    bomNumBytes = 2;
                    return Encoding.GetEncoding("utf-16");
                }
                else if (data.Length >= 2 && data[0] == 0xfe && data[1] == 0xff)
                {
                    bomNumBytes = 2;
                    return Encoding.GetEncoding("utf-16BE");
                }
                else if (data.Length >= 3 && data[0] == 0xef && data[1] == 0xbb && data[2] == 0xbf)
                {
                    bomNumBytes = 3;
                    return Encoding.GetEncoding("utf-8");
                }
                else
                {
                    return null;
                }
            }
            catch
            {
                return null;
            }
        }

        // Encoding の種類に応じた適切な BOM を取得する
        public static readonly ReadOnlyMemory<byte> BOM_UTF_32BE = new byte[] { 0x00, 0x00, 0xfe, 0xff };
        public static readonly ReadOnlyMemory<byte> BOM_UTF_32 = new byte[] { 0xff, 0xfe, 0x00, 0x00 };
        public static readonly ReadOnlyMemory<byte> BOM_UTF_16BE = new byte[] { 0xfe, 0xff };
        public static readonly ReadOnlyMemory<byte> BOM_UTF_16 = new byte[] { 0xff, 0xfe };
        public static readonly ReadOnlyMemory<byte> BOM_UTF_8 = new byte[] { 0xef, 0xbb, 0xbf };

        public static ReadOnlySpan<byte> GetBOMSpan(Encoding encoding)
        {
            string name = "";
            try
            {
                name = encoding.BodyName;
            }
            catch
            {
                name = encoding.WebName;
            }

            if (Str.StrCmpi(name, "utf-32BE"))
                return BOM_UTF_32BE.Span;
            else if (Str.StrCmpi(name, "utf-32"))
                return BOM_UTF_32.Span;
            else if (Str.StrCmpi(name, "utf-16"))
                return BOM_UTF_16.Span;
            else if (Str.StrCmpi(name, "utf-16BE") || Str.StrCmpi(name, "unicodeFFFE"))
                return BOM_UTF_16BE.Span;
            else if (Str.StrCmpi(name, "utf-8"))
                return BOM_UTF_8.Span;

            return null;
        }

        public static byte[]? GetBOM(Encoding encoding)
        {
            var span = GetBOMSpan(encoding);

            if (span.IsEmpty)
                return null;

            return span.ToArray();
        }

        // テキストファイルを強制的に指定したエンコーディングにエンコードする
        public static byte[] ConvertEncoding(byte[] srcData, Encoding destEncoding)
        {
            return ConvertEncoding(srcData, destEncoding, false);
        }
        public static byte[] ConvertEncoding(byte[] srcData, Encoding destEncoding, bool appendBom)
        {
            Encoding? srcEncoding = GetEncoding(srcData);
            if (srcEncoding == null)
            {
                srcEncoding = Str.ShiftJisEncoding;
            }

            int nb;
            if (CheckBOM(srcData, out nb) != null)
            {
                srcData = Util.RemoveStartByteArray(srcData, nb);
            }

            string str = srcEncoding.GetString(srcData);

            byte[]? b1 = null;
            if (appendBom)
            {
                b1 = GetBOM(destEncoding);
            }
            byte[] b2 = destEncoding.GetBytes(str);

            return Util.CombineByteArray(b1, b2);
        }

        // テキストファイルを読み込む
        public static string ReadTextFile(string filename)
        {
            byte[] data = IO.ReadFile(filename);
            int bomSize = 0;

            Encoding? enc = GetEncoding(data, out bomSize);
            if (enc == null)
            {
                enc = Str.Utf8Encoding;
            }
            if (bomSize >= 1)
            {
                data = Util.CopyByte(data, bomSize);
            }

            return enc.GetString(data);
        }
        public static string ReadTextFile(string filename, Encoding encoding)
        {
            byte[] data = IO.ReadFile(filename);

            Encoding enc = encoding;

            return enc.GetString(data);
        }


        // テキストファイルに書き込む
        public static void WriteTextFile(string filename, string contents, Encoding encoding, bool writeBom)
        {
            Buf buf = new Buf();
            byte[]? bom = GetBOM(encoding);
            if (writeBom && bom != null && bom.Length >= 1)
            {
                buf.Write(bom);
            }
            buf.Write(encoding.GetBytes(contents));

            buf.SeekToBegin();

            IO.SaveFile(filename, buf.Read());
        }

        // 受信した byte[] 配列を自動的にエンコーディング検出して string に変換する
        public static string DecodeStringAutoDetect(ReadOnlySpan<byte> data, out Encoding detectedEncoding, bool untilNullByte = false)
        {
            int bomSize;

            if (untilNullByte) data = data._UntilNullByte();

            detectedEncoding = Str.GetEncoding(data, out bomSize)!;
            if (detectedEncoding == null)
                detectedEncoding = Str.Utf8Encoding;

            data = data.Slice(bomSize);

            return detectedEncoding.GetString(data);
        }

        // 文字列をデコードする (BOM があれば BOM に従う)
        [MethodImpl(Inline)]
        public static string DecodeString(ReadOnlySpan<byte> data, Encoding defaultEncoding, out Encoding detectedEncoding, bool untilNullByte = false)
        {
            int bomSize;

            detectedEncoding = CheckBOM(data, out bomSize)!;
            if (detectedEncoding == null)
                detectedEncoding = defaultEncoding;

            bool isMultiByte = detectedEncoding.BodyName._IsSamei("utf-16") || detectedEncoding.BodyName._IsSamei("utf-16BE");

            data = data.Slice(bomSize);

            if (untilNullByte)
                if (isMultiByte == false)
                    data = data._UntilNullByte();
                else
                    data = data._AsUInt16Span()._UntilNullUShort()._AsUInt8Span();


            return detectedEncoding.GetString(data);
        }

        // テキストファイルのエンコーディングを取得する
        public static Encoding? GetEncoding(ReadOnlySpan<byte> data)
        {
            int i;
            return GetEncoding(data, out i);
        }
        public static Encoding? GetEncoding(ReadOnlySpan<byte> data, out int bomSize)
        {
            const byte bESC = 0x1B;
            const byte bAT = 0x40;
            const byte bDollar = 0x24;
            const byte bAnd = 0x26;
            const byte bOP = 0x28;
            const byte bB = 0x42;
            const byte bD = 0x44;
            const byte bJ = 0x4A;
            const byte bI = 0x49;
            bomSize = 0;

            int len = data.Length;
            int binary = 0;
            int ucs2 = 0;
            int sjis = 0;
            int euc = 0;
            int utf8 = 0;
            byte b1, b2;

            Encoding? bomEncoding = CheckBOM(data, out bomSize);
            if (bomEncoding != null)
            {
                return bomEncoding;
            }

            for (int i = 0; i < len; i++)
            {
                if (data[i] <= 0x06 || data[i] == 0x7F || data[i] == 0xFF)
                {
                    //'binary'
                    binary++;
                    if (len - 1 > i && data[i] == 0x00
                        && i > 0 && data[i - 1] <= 0x7F)
                    {
                        //smells like raw unicode
                        ucs2++;
                    }
                }
            }


            if (binary > 0)
            {
                if (ucs2 > 0)
                {
                    //JIS
                    //ucs2(Unicode)

                    int n1 = 0, n2 = 0;
                    for (int i = 0; i < (len / 2); i++)
                    {
                        byte e1 = data[i * 2];
                        byte e2 = data[i * 2 + 1];

                        if (e1 == 0 && e2 != 0)
                        {
                            n1++;
                        }
                        else if (e1 != 0 && e2 == 0)
                        {
                            n2++;
                        }
                    }

                    if (n1 > n2)
                    {
                        return Encoding.GetEncoding("utf-16BE");
                    }
                    else
                    {
                        return System.Text.Encoding.Unicode;
                    }
                }
                else
                {
                    //binary
                    return null;
                }
            }

            for (int i = 0; i < len - 1; i++)
            {
                b1 = data[i];
                b2 = data[i + 1];

                if (b1 == bESC)
                {
                    if (b2 >= 0x80)
                        //not Japanese
                        //ASCII
                        return Str.AsciiEncoding;
                    else if (len - 2 > i &&
                        b2 == bDollar && data[i + 2] == bAT)
                        //JIS_0208 1978
                        //JIS
                        return Str.ISO2022JPEncoding;
                    else if (len - 2 > i &&
                        b2 == bDollar && data[i + 2] == bB)
                        //JIS_0208 1983
                        //JIS
                        return Str.ISO2022JPEncoding;
                    else if (len - 5 > i &&
                        b2 == bAnd && data[i + 2] == bAT && data[i + 3] == bESC &&
                        data[i + 4] == bDollar && data[i + 5] == bB)
                        //JIS_0208 1990
                        //JIS
                        return Str.ISO2022JPEncoding;
                    else if (len - 3 > i &&
                        b2 == bDollar && data[i + 2] == bOP && data[i + 3] == bD)
                        //JIS_0212
                        //JIS
                        return Str.ISO2022JPEncoding;
                    else if (len - 2 > i &&
                        b2 == bOP && (data[i + 2] == bB || data[i + 2] == bJ))
                        //JIS_ASC
                        //JIS
                        return Str.ISO2022JPEncoding;
                    else if (len - 2 > i &&
                        b2 == bOP && data[i + 2] == bI)
                        //JIS_KANA
                        //JIS
                        return Str.ISO2022JPEncoding;
                }
            }

            for (int i = 0; i < len - 1; i++)
            {
                b1 = data[i];
                b2 = data[i + 1];
                if (((b1 >= 0x81 && b1 <= 0x9F) || (b1 >= 0xE0 && b1 <= 0xFC)) &&
                    ((b2 >= 0x40 && b2 <= 0x7E) || (b2 >= 0x80 && b2 <= 0xFC)))
                {
                    sjis += 2;
                    i++;
                }
            }
            for (int i = 0; i < len - 1; i++)
            {
                b1 = data[i];
                b2 = data[i + 1];
                if (((b1 >= 0xA1 && b1 <= 0xFE) && (b2 >= 0xA1 && b2 <= 0xFE)) ||
                    (b1 == 0x8E && (b2 >= 0xA1 && b2 <= 0xDF)))
                {
                    euc += 2;
                    i++;
                }
                else if (len - 2 > i &&
                    b1 == 0x8F && (b2 >= 0xA1 && b2 <= 0xFE) &&
                    (data[i + 2] >= 0xA1 && data[i + 2] <= 0xFE))
                {
                    euc += 3;
                    i += 2;
                }
            }
            for (int i = 0; i < len - 1; i++)
            {
                b1 = data[i];
                b2 = data[i + 1];
                if ((b1 >= 0xC0 && b1 <= 0xDF) && (b2 >= 0x80 && b2 <= 0xBF))
                {
                    utf8 += 2;
                    i++;
                }
                else if (len - 2 > i &&
                    (b1 >= 0xE0 && b1 <= 0xEF) && (b2 >= 0x80 && b2 <= 0xBF) &&
                    (data[i + 2] >= 0x80 && data[i + 2] <= 0xBF))
                {
                    utf8 += 3;
                    i += 2;
                }
            }

            if (euc > sjis && euc > utf8)
                //EUC
                return Str.EucJpEncoding;
            else if (sjis > euc && sjis > utf8)
                //SJIS
                return Str.ShiftJisEncoding;
            else if (utf8 > euc && utf8 > sjis)
                //UTF8
                return Str.Utf8Encoding;

            return null;
        }

        // いずれかの文字が最初に一致するかどうか
        public static bool StartsWithMulti(string str, StringComparison comparison, params string[] keys)
        {
            NormalizeString(ref str);

            foreach (string key in keys)
            {
                if (str.StartsWith(key, comparison))
                {
                    return true;
                }
            }

            return false;
        }

        // メールアドレスに使用可能な文字かどうか取得
        public static bool IsCharForMail(char c)
        {
            switch (c)
            {
                case '<':
                case '>':
                case ' ':
                case ';':
                case ':':
                case '/':
                case '(':
                case ')':
                case '&':
                case ',':
                case '%':
                case '$':
                case '#':
                case '\"':
                case '\'':
                case '!':
                case '=':
                case '\\':
                    return false;
            }

            if (c >= 0x80)
            {
                return false;
            }

            if (IsAscii(c) == false)
            {
                return false;
            }

            return true;
        }

        // メールのリンクになっているような部分をリンクする
        public static string LinkMailtoOnText(string text)
        {
            NormalizeString(ref text);

            StringBuilder sb = new StringBuilder();

            string tmp = "";

            int i;
            for (i = 0; i < text.Length; i++)
            {
                char c = text[i];

                if (IsCharForMail(c) == false)
                {
                    if (Str.CheckMailAddress(tmp) == false)
                    {
                        tmp += c;
                        sb.Append(tmp);
                        tmp = "";
                    }
                    else
                    {
                        sb.AppendFormat("<a href=\"mailto:{0}\">{0}</a>", tmp);
                        sb.Append(c);
                        tmp = "";
                    }
                }
                else
                {
                    tmp += c;
                }
            }
            if (Str.CheckMailAddress(tmp) == false)
            {
                sb.Append(tmp);
                tmp = "";
            }
            else
            {
                sb.AppendFormat("<a href=\"mailto:{0}\">{0}</a>", tmp);
                tmp = "";
            }

            return sb.ToString();
        }

        // URL のリンクになっているような部分をリンクする
        public static string LinkUrlOnText(string text, string target = "")
        {
            int findStart = 0;

            NormalizeString(ref text);
            NormalizeString(ref target);

            StringBuilder sb = new StringBuilder();

            while (true)
            {
                int foundStrIndex;
                int foundIndex = FindStrings(text, findStart, StringComparison.OrdinalIgnoreCase, out foundStrIndex,
                    "http://", "https://", "ftp://", "telnet://", "mailto://", "news://");

                // URL の末尾まで検索
                if (foundIndex != -1)
                {
                    int i;
                    int endOfUrl = -1;
                    for (i = foundIndex; i < text.Length; i++)
                    {
                        char c = text[i];

                        if (IsValidForUrl(c) == false)
                        {
                            endOfUrl = i;
                            break;
                        }

                        if (c == '<' || c == '&')
                        {
                            if (StartsWithMulti(text.Substring(i), StringComparison.OrdinalIgnoreCase,
                                HtmlSpacing, HtmlCrlf, HtmlBr, HtmlLt, HtmlGt))
                            {
                                endOfUrl = i;
                                break;
                            }
                        }
                    }

                    if (endOfUrl == -1)
                    {
                        endOfUrl = text.Length;
                    }

                    // URL を抽出
                    string url = text.Substring(foundIndex, endOfUrl - foundIndex);
                    string beforeUrl = text.Substring(findStart, foundIndex - findStart);

                    sb.Append(beforeUrl);

                    if (Str.IsEmptyStr(target) == false)
                    {
                        sb.AppendFormat("<a href=\"{0}\" target=\"{2}\">{1}</a>", url, url, target);
                    }
                    else
                    {
                        sb.AppendFormat("<a href=\"{0}\">{1}</a>", url, url);
                    }

                    findStart = endOfUrl;
                }
                else
                {
                    sb.Append(text.Substring(findStart));

                    break;
                }
            }

            return LinkMailtoOnText(sb.ToString());
        }

        // いずれかの最初に発見される文字列を検索
        public static int FindStrings(string str, int findStartIndex, StringComparison comparison, out int foundKeyIndex, params string[] keys)
        {
            int ret = -1;
            foundKeyIndex = -1;
            int n = 0;

            foreach (string key in keys)
            {
                int i = str.IndexOf(key, findStartIndex, comparison);

                if (i != -1)
                {
                    if (ret == -1)
                    {
                        ret = i;
                        foundKeyIndex = n;
                    }
                    else
                    {
                        if (ret > i)
                        {
                            ret = i;
                            foundKeyIndex = n;
                        }
                    }
                }

                n++;
            }

            return ret;
        }

        public static int FindStrings(string str, int findStartIndex, StringComparison comparison, out string foundString, params string[] keys)
        {
            int r = FindStrings(str, findStartIndex, comparison, out int foundIndex, keys: keys);
            if (r == -1)
            {
                foundString = "";
                return -1;
            }

            foundString = keys[foundIndex];

            return r;
        }

        // URL として使用可能な文字かどうか
        public static bool IsValidForUrl(char c)
        {
            if (c >= '0' && c <= '9')
            {
                return true;
            }
            if (c >= 'a' && c <= 'z')
            {
                return true;
            }
            if (c >= 'A' && c <= 'Z')
            {
                return true;
            }
            switch (c)
            {
                case '_':
                case '-':
                case '?':
                case '!':
                case '\"':
                case ',':
                case '\'':
                case '/':
                case '\\':
                case '&':
                case ';':
                case '%':
                case '#':
                case '@':
                case '~':
                case ':':
                case '=':
                case '+':
                case '*':
                case '$':
                case '.':
                    return true;
            }

            return false;
        }

        // 文字列リストから指定した文字列を削除する
        public static List<string> RemoteStringFromList(List<string> str, RemoveStringFunction func)
        {
            List<string> ret = new List<string>();

            foreach (string s in str)
            {
                if (func(s) == false)
                {
                    ret.Add(s);
                }
            }

            return ret;
        }

        public const string ConstZenkaku = "｀｛｝０１２３４５６７８９／＊－＋！”＃＄％＆’（）＝￣｜￥［］＠；：＜＞？＿＾　ａｂｃｄｅｆｇｈｉｊｋｌｍｎｏｐｑｒｓｔｕｖｗｘｙｚＡＢＣＤＥＦＧＨＩＪＫＬＭＮＯＰＱＲＳＴＵＶＷＸＹＺ‘";
        public const string ConstHankaku = "`{}0123456789/*-+!\"#$%&'()=~|\\[]@;:<>?_^ abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ'";
        public const string ConstKanaZenkaku = "ー「」アイウエオカキクケコサシスセソタチツテトナニヌネノハヒフヘホマミムメモヤユヨラリルレロワヲンァゥェォャュョッィ゛゜";
        public const string ConstKanaHankaku = "ｰ｢｣ｱｲｳｴｵｶｷｸｹｺｻｼｽｾｿﾀﾁﾂﾃﾄﾅﾆﾇﾈﾉﾊﾋﾌﾍﾎﾏﾐﾑﾒﾓﾔﾕﾖﾗﾘﾙﾚﾛﾜｦﾝｧｩｪｫｬｭｮｯｨﾞﾟ";
        public const string ConstKanaZenkakuDakuon = "ガギグゲゴザジズゼゾダヂヅデドバビブベボパピプペポ";

        // 空白の除去
        public static void RemoveSpace(ref string str)
        {
            NormalizeString(ref str);

            str = str.Replace(" ", "").Replace("　", "").Replace("\t", "");
        }

        // 先頭文字列の除去
        public static bool TrimStartWith(ref string str, string key, StringComparison comparison)
        {
            if (str.StartsWith(key, comparison))
            {
                str = str.Substring(key.Length);
                return true;
            }
            return false;
        }

        // 末尾文字列の除去
        public static bool TrimEndsWith(ref string str, string key, StringComparison comparison)
        {
            if (str.EndsWith(key, comparison))
            {
                str = str.Substring(0, str.Length - key.Length);
                return true;
            }
            return false;
        }

        // 空白の除去
        public static void RemoveSpaceChar([AllowNull] ref string str)
        {
            if (Str.IsEmptyStr(str))
            {
                return;
            }

            StringBuilder sb = new StringBuilder();

            foreach (char c in str)
            {
                if (c == ' ' || c == '\t' || c == '　')
                {
                }
                else
                {
                    sb.Append(c);
                }
            }

            str = sb.ToString();
        }

        // 文字列を正規化する
        public static void NormalizeStringStandard([AllowNull] ref string str)
        {
            NormalizeString(ref str, true, true, false, true);
        }
        public static void NormalizeString([AllowNull] ref string str, bool space, bool toHankaku, bool toZenkaku, bool toZenkakuKana)
        {
            NormalizeString(ref str);

            if (space)
            {
                str = NormalizeSpace(str);
            }

            if (toHankaku)
            {
                str = ZenkakuToHankaku(str);
            }

            if (toZenkaku)
            {
                str = HankakuToZenkaku(str);
            }

            if (toZenkakuKana)
            {
                str = KanaHankakuToZenkaku(str);
            }
        }
        public static string NormalizeString(string src, bool space, bool toHankaku, bool toZenkaku, bool toZenkakuKana)
        {
            NormalizeString(ref src, space, toHankaku, toZenkaku, toZenkakuKana);
            return src;
        }

        // 指定した文字幅に満ちるまでスペースを追加する
        public static string AddSpacePadding(string? srcStr, int totalWidth, bool addOnLeft = false, char spaceChar = ' ')
        {
            srcStr = srcStr._NonNull();

            int spaceCount = 0;

            int strWidth = Str.GetStrWidth(srcStr);
            if (strWidth < totalWidth)
            {
                spaceCount = totalWidth - strWidth;
            }

            if (addOnLeft == false)
            {
                return srcStr + Str.MakeCharArray(spaceChar, spaceCount);
            }
            else
            {
                return Str.MakeCharArray(spaceChar, spaceCount) + srcStr;
            }
        }

        // スペースを正規化する
        public static string NormalizeSpace(string? str)
        {
            NormalizeString(ref str);
            char[] sps =
            {
                ' ', '　', '\t',
            };

            string[] tokens = str.Split(sps, StringSplitOptions.RemoveEmptyEntries);

            return Str.CombineStringArray(tokens, " ");
        }

        // 指定された文字がカタカナかどうか判別する
        public static bool IsKatakanaChar(char c)
        {
            if (ConstKanaHankaku.IndexOf(c) != -1)
            {
                return true;
            }
            if (ConstKanaZenkaku.IndexOf(c) != -1)
            {
                return true;
            }
            if (ConstKanaZenkakuDakuon.IndexOf(c) != -1)
            {
                return true;
            }

            return false;
        }
        public static bool IsKatakanaStr(string str)
        {
            str = str.Trim();

            if (Str.IsEmptyStr(str))
            {
                return false;
            }

            foreach (char c in str)
            {
                if (IsKatakanaChar(c) == false)
                {
                    return false;
                }
            }
            return true;
        }

        // 半角全角文字を ⑴ ～ ⒇ の特殊文字に置換する
        public static string NormalStrToKakkoNumberSpecialStr(string str, bool zenkakuInRome = false)
        {
            str = str.Replace("(1)", "⑴")
                     .Replace("(2)", "⑵")
                     .Replace("(3)", "⑶")
                     .Replace("(4)", "⑷")
                     .Replace("(5)", "⑸")
                     .Replace("(6)", "⑹")
                     .Replace("(7)", "⑺")
                     .Replace("(8)", "⑻")
                     .Replace("(9)", "⑼")
                     .Replace("(10)", "⑽")
                     .Replace("(11)", "⑾")
                     .Replace("(12)", "⑿")
                     .Replace("(13)", "⒀")
                     .Replace("(14)", "⒁")
                     .Replace("(15)", "⒂")
                     .Replace("(16)", "⒃")
                     .Replace("(17)", "⒄")
                     .Replace("(18)", "⒅")
                     .Replace("(19)", "⒆")
                     .Replace("(20)", "⒇")
                     .Replace("(一)", "㈠")
                     .Replace("(二)", "㈡")
                     .Replace("(三)", "㈢")
                     .Replace("(四)", "㈣")
                     .Replace("(五)", "㈤")
                     .Replace("(六)", "㈥")
                     .Replace("(七)", "㈦")
                     .Replace("(八)", "㈧")
                     .Replace("(九)", "㈨")
                     .Replace("(十)", "㈩")
                     .Replace("(a)", "⒜")
                     .Replace("(b)", "⒝")
                     .Replace("(c)", "⒞")
                     .Replace("(d)", "⒟")
                     .Replace("(e)", "⒠")
                     .Replace("(f)", "⒡")
                     .Replace("(g)", "⒢")
                     .Replace("(h)", "⒣")
                     //.Replace("(i)", "⒤")
                     .Replace("(j)", "⒥")
                     .Replace("(k)", "⒦")
                     .Replace("(l)", "⒧")
                     .Replace("(m)", "⒨")
                     .Replace("(n)", "⒩")
                     .Replace("(o)", "⒪")
                     .Replace("(p)", "⒫")
                     .Replace("(q)", "⒬")
                     .Replace("(r)", "⒭")
                     .Replace("(s)", "⒮")
                     .Replace("(t)", "⒯")
                     .Replace("(u)", "⒰")
                     //.Replace("(v)", "⒱")
                     .Replace("(w)", "⒲")
                     //.Replace("(x)", "⒳")
                     .Replace("(y)", "⒴")
                     .Replace("(z)", "⒵")
                     .Replace("(１)", "⑴")
                     .Replace("(２)", "⑵")
                     .Replace("(３)", "⑶")
                     .Replace("(４)", "⑷")
                     .Replace("(５)", "⑸")
                     .Replace("(６)", "⑹")
                     .Replace("(７)", "⑺")
                     .Replace("(８)", "⑻")
                     .Replace("(９)", "⑼")
                     .Replace("(１０)", "⑽")
                     .Replace("(１１)", "⑾")
                     .Replace("(１２)", "⑿")
                     .Replace("(１３)", "⒀")
                     .Replace("(１４)", "⒁")
                     .Replace("(１５)", "⒂")
                     .Replace("(１６)", "⒃")
                     .Replace("(１７)", "⒄")
                     .Replace("(１８)", "⒅")
                     .Replace("(１９)", "⒆")
                     .Replace("(２０)", "⒇")
                     .Replace("(ａ)", "⒜")
                     .Replace("(ｂ)", "⒝")
                     .Replace("(ｃ)", "⒞")
                     .Replace("(ｄ)", "⒟")
                     .Replace("(ｅ)", "⒠")
                     .Replace("(ｆ)", "⒡")
                     .Replace("(ｇ)", "⒢")
                     .Replace("(ｈ)", "⒣")
                     //.Replace("(ｉ)", "⒤")
                     .Replace("(ｊ)", "⒥")
                     .Replace("(ｋ)", "⒦")
                     .Replace("(ｌ)", "⒧")
                     .Replace("(ｍ)", "⒨")
                     .Replace("(ｎ)", "⒩")
                     .Replace("(ｏ)", "⒪")
                     .Replace("(ｐ)", "⒫")
                     .Replace("(ｑ)", "⒬")
                     .Replace("(ｒ)", "⒭")
                     .Replace("(ｓ)", "⒮")
                     .Replace("(ｔ)", "⒯")
                     .Replace("(ｕ)", "⒰")
                     //.Replace("(ｖ)", "⒱")
                     .Replace("(ｗ)", "⒲")
                     //.Replace("(ｘ)", "⒳")
                     .Replace("(ｙ)", "⒴")
                     .Replace("(ｚ)", "⒵")
                     .Replace("（1）", "⑴")
                     .Replace("（2）", "⑵")
                     .Replace("（3）", "⑶")
                     .Replace("（4）", "⑷")
                     .Replace("（5）", "⑸")
                     .Replace("（6）", "⑹")
                     .Replace("（7）", "⑺")
                     .Replace("（8）", "⑻")
                     .Replace("（9）", "⑼")
                     .Replace("（10）", "⑽")
                     .Replace("（11）", "⑾")
                     .Replace("（12）", "⑿")
                     .Replace("（13）", "⒀")
                     .Replace("（14）", "⒁")
                     .Replace("（15）", "⒂")
                     .Replace("（16）", "⒃")
                     .Replace("（17）", "⒄")
                     .Replace("（18）", "⒅")
                     .Replace("（19）", "⒆")
                     .Replace("（20）", "⒇")
                     .Replace("（一）", "㈠")
                     .Replace("（二）", "㈡")
                     .Replace("（三）", "㈢")
                     .Replace("（四）", "㈣")
                     .Replace("（五）", "㈤")
                     .Replace("（六）", "㈥")
                     .Replace("（七）", "㈦")
                     .Replace("（八）", "㈧")
                     .Replace("（九）", "㈨")
                     .Replace("（十）", "㈩")
                     .Replace("（a）", "⒜")
                     .Replace("（b）", "⒝")
                     .Replace("（c）", "⒞")
                     .Replace("（d）", "⒟")
                     .Replace("（e）", "⒠")
                     .Replace("（f）", "⒡")
                     .Replace("（g）", "⒢")
                     .Replace("（h）", "⒣")
                     //.Replace("（i）", "⒤")
                     .Replace("（j）", "⒥")
                     .Replace("（k）", "⒦")
                     .Replace("（l）", "⒧")
                     .Replace("（m）", "⒨")
                     .Replace("（n）", "⒩")
                     .Replace("（o）", "⒪")
                     .Replace("（p）", "⒫")
                     .Replace("（q）", "⒬")
                     .Replace("（r）", "⒭")
                     .Replace("（s）", "⒮")
                     .Replace("（t）", "⒯")
                     .Replace("（u）", "⒰")
                     //.Replace("（v）", "⒱")
                     .Replace("（w）", "⒲")
                     //.Replace("（x）", "⒳")
                     .Replace("（y）", "⒴")
                     .Replace("（z）", "⒵")
                     .Replace("（１）", "⑴")
                     .Replace("（２）", "⑵")
                     .Replace("（３）", "⑶")
                     .Replace("（４）", "⑷")
                     .Replace("（５）", "⑸")
                     .Replace("（６）", "⑹")
                     .Replace("（７）", "⑺")
                     .Replace("（８）", "⑻")
                     .Replace("（９）", "⑼")
                     .Replace("（１０）", "⑽")
                     .Replace("（１１）", "⑾")
                     .Replace("（１２）", "⑿")
                     .Replace("（１３）", "⒀")
                     .Replace("（１４）", "⒁")
                     .Replace("（１５）", "⒂")
                     .Replace("（１６）", "⒃")
                     .Replace("（１７）", "⒄")
                     .Replace("（１８）", "⒅")
                     .Replace("（１９）", "⒆")
                     .Replace("（２０）", "⒇")
                     .Replace("（ａ）", "⒜")
                     .Replace("（ｂ）", "⒝")
                     .Replace("（ｃ）", "⒞")
                     .Replace("（ｄ）", "⒟")
                     .Replace("（ｅ）", "⒠")
                     .Replace("（ｆ）", "⒡")
                     .Replace("（ｇ）", "⒢")
                     .Replace("（ｈ）", "⒣")
                     //.Replace("（ｉ）", "⒤")
                     .Replace("（ｊ）", "⒥")
                     .Replace("（ｋ）", "⒦")
                     .Replace("（ｌ）", "⒧")
                     .Replace("（ｍ）", "⒨")
                     .Replace("（ｎ）", "⒩")
                     .Replace("（ｏ）", "⒪")
                     .Replace("（ｐ）", "⒫")
                     .Replace("（ｑ）", "⒬")
                     .Replace("（ｒ）", "⒭")
                     .Replace("（ｓ）", "⒮")
                     .Replace("（ｔ）", "⒯")
                     .Replace("（ｕ）", "⒰")
                     //.Replace("（ｖ）", "⒱")
                     .Replace("（ｗ）", "⒲")
                     //.Replace("（ｘ）", "⒳")
                     .Replace("（ｙ）", "⒴")
                     .Replace("（ｚ）", "⒵");

            if (zenkakuInRome == false)
            {
                str = str.Replace("(i)", "(ⅰ)")
                         .Replace("(ii)", "(ⅱ)")
                         .Replace("(iii)", "(ⅲ)")
                         .Replace("(iv)", "(ⅳ)")
                         .Replace("(v)", "(ⅴ)")
                         .Replace("(vi)", "(ⅵ)")
                         .Replace("(vii)", "(ⅶ)")
                         .Replace("(viii)", "(ⅷ)")
                         .Replace("(ix)", "(ⅸ)")
                         .Replace("(x)", "(ⅹ)")
                         .Replace("(xi)", "(ⅺ)")
                         .Replace("(xii)", "(ⅻ)")

                        .Replace("（i）", "(ⅰ)")
                        .Replace("（ii）", "(ⅱ)")
                        .Replace("（iii）", "(ⅲ)")
                        .Replace("（iv）", "(ⅳ)")
                        .Replace("（v）", "(ⅴ)")
                        .Replace("（vi）", "(ⅵ)")
                        .Replace("（vii）", "(ⅶ)")
                        .Replace("（viii）", "(ⅷ)")
                        .Replace("（ix）", "(ⅸ)")
                        .Replace("（x）", "(ⅹ)")
                        .Replace("（xi）", "(ⅺ)")
                        .Replace("（xii）", "(ⅻ)")

                        .Replace("(ｉ)", "(ⅰ)")
                        .Replace("(ｉｉ)", "(ⅱ)")
                        .Replace("(ｉｉｉ)", "(ⅲ)")
                        .Replace("(ｉｖ)", "(ⅳ)")
                        .Replace("(ｖ)", "(ⅴ)")
                        .Replace("(ｖｉ)", "(ⅵ)")
                        .Replace("(ｖｉｉ)", "(ⅶ)")
                        .Replace("(ｖｉｉｉ)", "(ⅷ)")
                        .Replace("(ｉｘ)", "(ⅸ)")
                        .Replace("(ｘ)", "(ⅹ)")
                        .Replace("(ｘｉ)", "(ⅺ)")
                        .Replace("(ｘｉｉ)", "(ⅻ)")

                        .Replace("（ｉ）", "(ⅰ)")
                        .Replace("（ｉｉ）", "(ⅱ)")
                        .Replace("（ｉｉｉ）", "(ⅲ)")
                        .Replace("（ｉｖ）", "(ⅳ)")
                        .Replace("（ｖ）", "(ⅴ)")
                        .Replace("（ｖｉ）", "(ⅵ)")
                        .Replace("（ｖｉｉ）", "(ⅶ)")
                        .Replace("（ｖｉｉｉ）", "(ⅷ)")
                        .Replace("（ｉｘ）", "(ⅸ)")
                        .Replace("（ｘ）", "(ⅹ)")
                        .Replace("（ｘｉ）", "(ⅺ)")
                        .Replace("（ｘｉｉ）", "(ⅻ)");
            }
            else
            {
                str = str.Replace("(i)", "（ⅰ）")
                         .Replace("(ii)", "（ⅱ）")
                         .Replace("(iii)", "（ⅲ）")
                         .Replace("(iv)", "（ⅳ）")
                         .Replace("(v)", "（ⅴ）")
                         .Replace("(vi)", "（ⅵ）")
                         .Replace("(vii)", "（ⅶ）")
                         .Replace("(viii)", "（ⅷ）")
                         .Replace("(ix)", "（ⅸ）")
                         .Replace("(x)", "（ⅹ）")
                         .Replace("(xi)", "（ⅺ）")
                         .Replace("(xii)", "（ⅻ）")

                        .Replace("（i）", "（ⅰ）")
                        .Replace("（ii）", "（ⅱ）")
                        .Replace("（iii）", "（ⅲ）")
                        .Replace("（iv）", "（ⅳ）")
                        .Replace("（v）", "（ⅴ）")
                        .Replace("（vi）", "（ⅵ）")
                        .Replace("（vii）", "（ⅶ）")
                        .Replace("（viii）", "（ⅷ）")
                        .Replace("（ix）", "（ⅸ）")
                        .Replace("（x）", "（ⅹ）")
                        .Replace("（xi）", "（ⅺ）")
                        .Replace("（xii）", "（ⅻ）")

                        .Replace("(ｉ)", "（ⅰ）")
                        .Replace("(ｉｉ)", "（ⅱ）")
                        .Replace("(ｉｉｉ)", "（ⅲ）")
                        .Replace("(ｉｖ)", "（ⅳ）")
                        .Replace("(ｖ)", "（ⅴ）")
                        .Replace("(ｖｉ)", "（ⅵ）")
                        .Replace("(ｖｉｉ)", "（ⅶ）")
                        .Replace("(ｖｉｉｉ)", "（ⅷ）")
                        .Replace("(ｉｘ)", "（ⅸ）")
                        .Replace("(ｘ)", "（ⅹ）")
                        .Replace("(ｘｉ)", "（ⅺ）")
                        .Replace("(ｘｉｉ)", "（ⅻ）")

                        .Replace("（ｉ）", "（ⅰ）")
                        .Replace("（ｉｉ）", "（ⅱ）")
                        .Replace("（ｉｉｉ）", "（ⅲ）")
                        .Replace("（ｉｖ）", "（ⅳ）")
                        .Replace("（ｖ）", "（ⅴ）")
                        .Replace("（ｖｉ）", "（ⅵ）")
                        .Replace("（ｖｉｉ）", "（ⅶ）")
                        .Replace("（ｖｉｉｉ）", "（ⅷ）")
                        .Replace("（ｉｘ）", "（ⅸ）")
                        .Replace("（ｘ）", "（ⅹ）")
                        .Replace("（ｘｉ）", "（ⅺ）")
                        .Replace("（ｘｉｉ）", "（ⅻ）");
            }

            return str;
        }

        // ⑴ ～ ⒇ の特殊文字を半角文字に置換する
        public static string KakkoNumberSpecialStrToNormalStr(string str)
        {
            return str.Replace("⑴", "(1)")
                     .Replace("⑵", "(2)")
                     .Replace("⑶", "(3)")
                     .Replace("⑷", "(4)")
                     .Replace("⑸", "(5)")
                     .Replace("⑹", "(6)")
                     .Replace("⑺", "(7)")
                     .Replace("⑻", "(8)")
                     .Replace("⑼", "(9)")
                     .Replace("⑽", "(10)")
                     .Replace("⑾", "(11)")
                     .Replace("⑿", "(12)")
                     .Replace("⒀", "(13)")
                     .Replace("⒁", "(14)")
                     .Replace("⒂", "(15)")
                     .Replace("⒃", "(16)")
                     .Replace("⒄", "(17)")
                     .Replace("⒅", "(18)")
                     .Replace("⒆", "(19)")
                     .Replace("⒇", "(20)")
                     .Replace("㈠", "(一)")
                     .Replace("㈡", "(二)")
                     .Replace("㈢", "(三)")
                     .Replace("㈣", "(四)")
                     .Replace("㈤", "(五)")
                     .Replace("㈥", "(六)")
                     .Replace("㈦", "(七)")
                     .Replace("㈧", "(八)")
                     .Replace("㈨", "(九)")
                     .Replace("㈩", "(十)")
                     .Replace("⒜", "(a)")
                     .Replace("⒝", "(b)")
                     .Replace("⒞", "(c)")
                     .Replace("⒟", "(d)")
                     .Replace("⒠", "(e)")
                     .Replace("⒡", "(f)")
                     .Replace("⒢", "(g)")
                     .Replace("⒣", "(h)")
                     .Replace("⒤", "(i)")
                     .Replace("⒥", "(j)")
                     .Replace("⒦", "(k)")
                     .Replace("⒧", "(l)")
                     .Replace("⒨", "(m)")
                     .Replace("⒩", "(n)")
                     .Replace("⒪", "(o)")
                     .Replace("⒫", "(p)")
                     .Replace("⒬", "(q)")
                     .Replace("⒭", "(r)")
                     .Replace("⒮", "(s)")
                     .Replace("⒯", "(t)")
                     .Replace("⒰", "(u)")
                     .Replace("⒱", "(v)")
                     .Replace("⒲", "(w)")
                     .Replace("⒳", "(x)")
                     .Replace("⒴", "(y)")
                     .Replace("⒵", "(z)")
                     .Replace("(ⅰ)", "(i)")
                     .Replace("(ⅱ)", "(ii)")
                     .Replace("(ⅲ)", "(iii)")
                     .Replace("(ⅳ)", "(iv)")
                     .Replace("(ⅴ)", "(v)")
                     .Replace("(ⅵ)", "(vi)")
                     .Replace("(ⅶ)", "(vii)")
                     .Replace("(ⅷ)", "(viii)")
                     .Replace("(ⅸ)", "(ix)")
                     .Replace("(ⅹ)", "(x)")
                     .Replace("(ⅺ)", "(xi)")
                     .Replace("(ⅻ)", "(xii)");
        }

        // 半角カナを全角カナに変換する
        public static string KanaHankakuToZenkaku(string str)
        {
            NormalizeString(ref str);

            str = str.Replace("ｶﾞ", "ガ");
            str = str.Replace("ｷﾞ", "ギ");
            str = str.Replace("ｸﾞ", "グ");
            str = str.Replace("ｹﾞ", "ゲ");
            str = str.Replace("ｺﾞ", "ゴ");
            str = str.Replace("ｻﾞ", "ザ");
            str = str.Replace("ｼﾞ", "ジ");
            str = str.Replace("ｽﾞ", "ズ");
            str = str.Replace("ｾﾞ", "ゼ");
            str = str.Replace("ｿﾞ", "ゾ");
            str = str.Replace("ﾀﾞ", "ダ");
            str = str.Replace("ﾁﾞ", "ヂ");
            str = str.Replace("ﾂﾞ", "ヅ");
            str = str.Replace("ﾃﾞ", "デ");
            str = str.Replace("ﾄﾞ", "ド");
            str = str.Replace("ﾊﾞ", "バ");
            str = str.Replace("ﾋﾞ", "ビ");
            str = str.Replace("ﾌﾞ", "ブ");
            str = str.Replace("ﾍﾞ", "ベ");
            str = str.Replace("ﾎﾞ", "ボ");
            str = str.Replace("ﾊﾟ", "パ");
            str = str.Replace("ﾋﾟ", "ピ");
            str = str.Replace("ﾌﾟ", "プ");
            str = str.Replace("ﾍﾟ", "ペ");
            str = str.Replace("ﾎﾟ", "ポ");

            char[] a = str.ToCharArray();
            int i;
            for (i = 0; i < a.Length; i++)
            {
                int j = ConstKanaHankaku.IndexOf(a[i]);

                if (j != -1)
                {
                    a[i] = ConstKanaZenkaku[j];
                }
            }

            return new string(a);
        }

        // 全角カナを半角カナに変換する
        public static string KanaZenkakuToHankaku(string str)
        {
            NormalizeString(ref str);

            str = str.Replace("ガ", "ｶﾞ");
            str = str.Replace("ギ", "ｷﾞ");
            str = str.Replace("グ", "ｸﾞ");
            str = str.Replace("ゲ", "ｹﾞ");
            str = str.Replace("ゴ", "ｺﾞ");
            str = str.Replace("ザ", "ｻﾞ");
            str = str.Replace("ジ", "ｼﾞ");
            str = str.Replace("ズ", "ｽﾞ");
            str = str.Replace("ゼ", "ｾﾞ");
            str = str.Replace("ゾ", "ｿﾞ");
            str = str.Replace("ダ", "ﾀﾞ");
            str = str.Replace("ヂ", "ﾁﾞ");
            str = str.Replace("ヅ", "ﾂﾞ");
            str = str.Replace("デ", "ﾃﾞ");
            str = str.Replace("ド", "ﾄﾞ");
            str = str.Replace("バ", "ﾊﾞ");
            str = str.Replace("ビ", "ﾋﾞ");
            str = str.Replace("ブ", "ﾌﾞ");
            str = str.Replace("ベ", "ﾍﾞ");
            str = str.Replace("ボ", "ﾎﾞ");
            str = str.Replace("パ", "ﾊﾟ");
            str = str.Replace("ピ", "ﾋﾟ");
            str = str.Replace("プ", "ﾌﾟ");
            str = str.Replace("ペ", "ﾍﾟ");
            str = str.Replace("ポ", "ﾎﾟ");

            char[] a = str.ToCharArray();
            int i;
            for (i = 0; i < a.Length; i++)
            {
                int j = ConstKanaZenkaku.IndexOf(a[i]);

                if (j != -1)
                {
                    a[i] = ConstKanaHankaku[j];
                }
            }

            return new string(a);
        }

        // 全角を半角に変換する
        public static string ZenkakuToHankaku(string str)
        {
            NormalizeString(ref str);

            str = ReplaceStr(str, "“", " \"");
            str = ReplaceStr(str, "”", "\" ");
            str = ReplaceStr(str, "‘", "'");
            str = ReplaceStr(str, "’", "'");

            char[] a = str.ToCharArray();
            int i;
            for (i = 0; i < a.Length; i++)
            {
                int j = ConstZenkaku.IndexOf(a[i]);

                if (j != -1)
                {
                    a[i] = ConstHankaku[j];
                }
            }

            return new string(a);
        }

        // 半角を全角に変換する
        public static string HankakuToZenkaku(string str)
        {
            NormalizeString(ref str);

            str = KanaHankakuToZenkaku(str);

            char[] a = str.ToCharArray();
            int i;
            for (i = 0; i < a.Length; i++)
            {
                int j = ConstHankaku.IndexOf(a[i]);

                if (j != -1)
                {
                    a[i] = ConstZenkaku[j];
                }
            }

            return new string(a);
        }

        public const string HtmlSpacing = "&nbsp;";
        public const string HtmlCrlf = "<BR>";
        public const string HtmlBr = HtmlCrlf;
        public const string HtmlLt = "&lt;";
        public const string HtmlGt = "&gt;";
        public const string HtmlAmp = "&amp;";
        public const string HtmlDoubleQuote = "&quot;";
        public const string HtmlSingleQuote = "&#039;";
        public const int HtmlNumTabChar = 8;

        public static string HtmlTab
        {
            get
            {
                int i;
                StringBuilder sb = new StringBuilder();
                for (i = 0; i < HtmlNumTabChar; i++)
                {
                    sb.Append(HtmlSpacing);
                }
                return sb.ToString();
            }
        }

        public static string GetHostNameFromFqdn(string fqdn)
        {
            if (fqdn._IsEmpty()) return "";
            int[] dots = fqdn._FindStringIndexes(".", true);
            if (dots.Length == 0)
                return fqdn;

            int i = dots.First();
            return fqdn.Substring(0, i);
        }

        public static string GetDomainFromFqdn(string fqdn)
        {
            if (fqdn._IsEmpty()) return "";
            int[] dots = fqdn._FindStringIndexes(".", true);
            if (dots.Length == 0)
                return "";

            int i = dots.First();
            return fqdn.Substring(i);
        }

        // URL パスエンコード
        public static string EncodeUrlPath(string? str, Encoding? encoding = null)
        {
            if (str == null) str = "";

            List<string> tokenList = new List<string>();

            StringBuilder currentToken = new StringBuilder();

            foreach (char c in str)
            {
                if (PathParser.Windows.PossibleDirectorySeparators.Where(x => x == c).Any())
                {
                    tokenList.Add(EncodeUrl(currentToken.ToString(), encoding));
                    currentToken.Clear();

                    tokenList.Add("/");
                }
                else
                {
                    currentToken.Append(c);
                }
            }

            tokenList.Add(EncodeUrl(currentToken.ToString(), encoding));
            currentToken.Clear();

            return tokenList._Combine();
        }

        // URL パスデコード
        public static string DecodeUrlPath(string? str, Encoding? encoding = null)
        {
            if (str == null) str = "";

            List<string> tokenList = new List<string>();

            StringBuilder currentToken = new StringBuilder();

            foreach (char c in str)
            {
                if (PathParser.Windows.PossibleDirectorySeparators.Where(x => x == c).Any())
                {
                    tokenList.Add(DecodeUrl(currentToken.ToString(), encoding));
                    currentToken.Clear();

                    tokenList.Add("/");
                }
                else
                {
                    currentToken.Append(c);
                }
            }

            tokenList.Add(DecodeUrl(currentToken.ToString(), encoding));
            currentToken.Clear();

            return tokenList._Combine();
        }

        // 任意の文字列を安全にエンコード (JavaScript 用)
        public static string JavaScriptSafeStrEncode(string? str)
        {
            str = str._NonNull();

            return str._EncodeUrl()._GetBytes_Ascii()._Base64Encode();
        }

        // 任意の文字列を安全にデコード (JavaScript 用)
        public static string JavaScriptSafeStrDecode(string? str)
        {
            str = str._NonNull();

            return str._Base64Decode()._GetString_Ascii()._DecodeUrl();
        }

        // URL エンコード
        public static string EncodeUrl(string? str, Encoding? encoding = null, UrlEncodeParam? param = null)
        {
            if (encoding == null) encoding = Str.Utf8Encoding;
            if (str == null) str = "";

            string ignoreCharList = param?.DoNotEncodeCharList._NonNull() ?? "";

            if (ignoreCharList._IsEmpty())
            {
                return Uri.EscapeDataString(str);
            }
            else
            {
                StringBuilder b = new StringBuilder();
                foreach (char c in str)
                {
                    if (ignoreCharList.IndexOf(c) == -1)
                    {
                        b.Append(Uri.EscapeDataString("" + c));
                    }
                    else
                    {
                        b.Append(c);
                    }
                }
                return b.ToString();
            }
        }

        // URL デコード
        public static string DecodeUrl(string? str, Encoding? encoding = null)
        {
            if (encoding == null) encoding = Str.Utf8Encoding;
            if (str == null) str = "";
            return Uri.UnescapeDataString(str);
        }

        // URL デコード (バイト配列に)
        public static byte[] DecodeUrlToBytes(string? str)
        {
            str = str._NonNull();
            return HttpUtility.UrlDecodeToBytes(str);
        }

        // URL デコードが可能であると考えられるエンコーディングの一覧を取得
        public static List<Encoding> GetSuitableUrlDecodeEncodings(string? str)
        {
            byte[] data = DecodeUrlToBytes(str);

            return GetSuitableEncodings(data);
        }

        // URL に含まれる任意のキーワードの抽出
        public static List<string> ExtractKeywordsFromUrl(string url, bool recursive = true)
        {
            if (url.StartsWith("/") == false && url.StartsWith("http://") == false && url.StartsWith("https://") == false && url.StartsWith("ftp://") == false)
            {
                url = "/" + url;
            }

            var encodingCandidates = GetSuitableUrlDecodeEncodings(url);

            HashSet<string> keywords = new HashSet<string>(StrComparer.IgnoreCaseComparer);

            foreach (var enc in encodingCandidates)
            {
                if (url._TryParseUrl(out Uri? uri, out QueryStringList qs, enc))
                {
                    uri._MarkNotNull();

                    // URL パスセグメント
                    uri.Segments._DoForEach(x => keywords.Add(x._DecodeUrl(enc)._NonNullTrim()));

                    // クエリ
                    foreach (var kv in qs)
                    {
                        keywords.Add(kv.Key._NonNullTrim());
                        keywords.Add(kv.Value._NonNullTrim());
                    }
                }
            }

            if (recursive)
            {
                foreach (var keyword in keywords._ToArrayList())
                {
                    // キーワードをもう一重デコードしてみる
                    var encodingCandidates2 = GetSuitableUrlDecodeEncodings(keyword);

                    string url2 = keyword;

                    if (url2.StartsWith("http://") || url2.StartsWith("https://"))
                    {
                        StringBuilder sb = new StringBuilder();
                        bool f = false;
                        for (int i = 0; i < url2.Length; i++)
                        {
                            char c = url2[i];

                            if (c == '?')
                            {
                                f = true;
                                c = '&';
                            }

                            if (f == false)
                            {
                                if (c == '/') c = '&';
                            }

                            sb.Append(c);
                        }

                        url2 = sb.ToString();
                    }

                    foreach (var enc in encodingCandidates2)
                    {
                        QueryStringList qs2 = new QueryStringList(url2);

                        foreach (var kv in qs2)
                        {
                            keywords.Add(kv.Key._NonNullTrim());
                            keywords.Add(kv.Value._NonNullTrim());
                        }
                    }
                }
            }

            return keywords
                .Where(x => x._IsFilled())
                .Where(x => x.Where(c => (c >= 128 || c == '\r' || c == '\n' || c == ' ' || c == '　' || c == '\t')).Any())
                .Where(x => IsPossiblyBase64(x) == false)
                .ToList();
        }

        // 文字列が Base64 っぽいかどうか検出
        public static bool IsPossiblyBase64(string str)
        {
            if (str.Where(c => c >= 0x80).Any()) return false;

            string[] tokens = str._Split(StringSplitOptions.RemoveEmptyEntries, ' ');

            foreach (string token in tokens)
            {
                if (token.Length >= 8)
                {
                    if (token._Split(StringSplitOptions.RemoveEmptyEntries, '0', '1', '2', '3', '4', '5', '6', '7', '8', '9', '/').Length >= 2)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        // HTML のダウンロード用 JavaScript の作成
        public static string GenerateHtmlDownloadJavaScript(ReadOnlySpan<byte> data, string mimeType, string filename, string buttonId = "download", string functionName = "handleDownload")
        {
            // 参考元: https://qiita.com/wadahiro/items/eb50ac6bbe2e18cf8813

            string src = @"
        <script type='text/javascript'>
            function ___FUNCTION___() {
                var data = new Uint8Array([__HEX__]);
                var blob = new Blob([data], { 'type' : '__MIME__' });

                if (window.navigator.msSaveBlob) { 
                    window.navigator.msSaveBlob(blob, '__FILENAME__'); 

                    window.navigator.msSaveOrOpenBlob(blob, '__FILENAME__'); 
                } else {
                    document.getElementById('___ID___').href = window.URL.createObjectURL(blob);
                }
            }
        </script>";


            return src._ReplaceStrWithReplaceClass(new
            {
                __MIME__ = mimeType,
                __FILENAME__ = filename,
                ___ID___ = buttonId,
                ___FUNCTION___ = functionName,
                __HEX__ = Str.ByteToJavaScriptHexString(data),
            });
        }

        // HTML デコード
        public static string DecodeHtml(string? str, bool normalizeMultiSpaces = false)
        {
            str = str._NonNull();

            if (normalizeMultiSpaces)
            {
                string[] strs = str.Split(new char[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                str = strs._Combine(" ").Trim();
            }

            str = Str.ReplaceStr(str, HtmlCrlf, "\r\n", false);

            str = str.Replace(HtmlSpacing, " ");

            str = str.Replace(HtmlLt, "<").Replace(HtmlGt, ">").Replace(HtmlAmp, "&");

            str = NormalizeCrlf(str, CrlfStyle.CrLf);

            return str;
        }

        // HTML エンコード
        public static string EncodeHtml(string? str, bool forceAllSpaceToTag = false, bool spaceIfEmpty = false)
        {
            str = str._NonNull();

            // 改行を正規化
            str = NormalizeCrlf(str, CrlfStyle.CrLf);

            // & を変換
            str = str.Replace("&", HtmlAmp);

            // " を変換
            str = str.Replace("\"", HtmlDoubleQuote);

            // ' を変換
            str = str.Replace("'", HtmlSingleQuote);

            // タグを変換
            str = str.Replace("<", HtmlLt).Replace(">", HtmlGt);

            // スペースを変換
            if (str.IndexOf(' ') != -1)
            {
                if (forceAllSpaceToTag)
                {
                    str = str.Replace(" ", HtmlSpacing);
                }
                else
                {
                    // 連続するスペースのみ変換
                    int i;
                    StringBuilder sb = new StringBuilder();
                    bool flag = false;

                    for (i = 0; i < str.Length; i++)
                    {
                        char c = str[i];

                        if (c == ' ')
                        {
                            if (flag == false)
                            {
                                flag = true;
                                sb.Append(' ');
                            }
                            else
                            {
                                sb.Append(HtmlSpacing);
                            }
                        }
                        else
                        {
                            flag = false;
                            sb.Append(c);
                        }
                    }

                    str = sb.ToString();
                }
            }

            // tab を変換
            str = str.Replace("\t", HtmlTab);

            // 改行コード
            str = str.Replace("\r\n", HtmlCrlf);

            if (spaceIfEmpty)
            {
                if (str._IsEmpty())
                {
                    str = Str.HtmlSpacing;
                }
            }

            return str;
        }

        // HTML Code ブロックエンコード
        public static string EncodeHtmlCodeBlock(string? str)
        {
            str = str._NonNull();

            // 改行を正規化
            str = NormalizeCrlf(str, CrlfStyle.CrLf);

            // & を変換
            str = str.Replace("&", HtmlAmp);

            // タグを変換
            str = str.Replace("<", HtmlLt).Replace(">", HtmlGt);

            return str;
        }

        // 指定した文字が表示可能で安全かどうか
        public static bool IsSafeAndPrintable(char c, bool crlfIsOk = false, bool htmlTagAreNotGood = false)
        {
            try
            {
                if (c == '\t' || c == '　')
                    return true;
                if (crlfIsOk)
                    if (c == '\r' || c == '\n')
                        return true;
                if (htmlTagAreNotGood)
                {
                    if (c == '<' || c == '>')
                        return false;
                    if (c == '＜' || c == '＞')
                        return false;
                }
                if (Char.IsControl(c))
                    return false;
                if (c >= 32 && c <= 126)
                    return true;
                if (Char.IsDigit(c) || Char.IsLetter(c) || Char.IsLetterOrDigit(c) || Char.IsNumber(c) ||
                    Char.IsPunctuation(c) || Char.IsSeparator(c) || Char.IsSymbol(c))
                {
                    return true;
                }
            }
            catch
            {
            }
            return false;
        }

        public static bool IsSafeAndPrintable(string s, bool crlfIsOk = false, bool htmlTagAreNotGood = false)
        {
            try
            {
                foreach (char c in s)
                {
                    if (IsSafeAndPrintable(c, crlfIsOk, htmlTagAreNotGood) == false)
                    {
                        return false;
                    }
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        // 指定した文字が表示可能かどうか
        public static bool IsPrintable(char c)
        {
            if (c >= 256)
            {
                return true;
            }

            if (c >= 32 && c <= 126)
            {
                return true;
            }

            return false;
        }
        public static bool IsPrintable(string str)
        {
            foreach (char c in str)
            {
                if (IsPrintable(c) == false)
                {
                    return false;
                }
            }

            return true;
        }

        // 指定した文字が Ascii 文字として 1 行で表示可能か
        public static bool IsAsciiOneLinePrintable(char c)
        {
            if (c >= 32 && c <= 126)
            {
                return true;
            }

            return false;
        }
        public static bool IsAsciiOneLinePrintable(string str)
        {
            foreach (char c in str)
            {
                if (!(c >= 32 && c <= 126))
                {
                    return false;
                }
            }

            return true;
        }

        // Ascii 文字として 1 行で表示可能な文字列に変換する
        public static string MakeAsciiOneLinePrintableStr(string? src, char alternativeChar = ' ')
        {
            src = src._NonNull();
            StringBuilder sb = new StringBuilder();
            foreach (char c in src)
            {
                if (IsAsciiOneLinePrintable(c) == false)
                {
                    sb.Append(alternativeChar);
                }
                else
                {
                    sb.Append(c);
                }
            }

            return sb.ToString();
        }

        // Ascii 文字として 1 行で表示可能な文字列に変換する
        public static string MakeAsciiOneLinePrintableStr(ReadOnlySpan<byte> src, char alternativeChar = ' ')
        {
            StringBuilder sb = new StringBuilder();
            foreach (byte c2 in src)
            {
                char c = (char)c2;
                if (IsAsciiOneLinePrintable(c) == false)
                {
                    sb.Append(alternativeChar);
                }
                else
                {
                    sb.Append(c);
                }
            }

            return sb.ToString();
        }

        public static int[] ParsePortsList(string str)
        {
            string[] tokens = str.Split(new char[] { ',', ' ', '　', '\t', '/', ';' }, StringSplitOptions.RemoveEmptyEntries);

            SortedSet<int> ret = new SortedSet<int>();

            foreach (string token in tokens)
            {
                int i = token._ToInt();

                if (i >= 1 && i <= 65535)
                {
                    ret.Add(i);
                }
            }

            return ret.ToArray();
        }

        public static string PortsListToStr(IEnumerable<int> ports)
        {
            SortedSet<int> tmp = new SortedSet<int>();
            ports.Where(x => x >= 1 && x <= 65535)._DoForEach(x => tmp.Add(x));
            return tmp.Select(x => x.ToString())._Combine(",");
        }

        // エスケープエンコード (本来のデータ => C 風の安全なデータ)
        public static string EncodeCEscape(string str)
        {
            StringBuilder sb = new StringBuilder();

            int i;
            for (i = 0; i < str.Length; i++)
            {
                char c = str[i];

                if (IsPrintable(c) && c != '\\')
                {
                    sb.Append(c);
                }
                else
                {
                    string s = "" + c;
                    switch (c)
                    {
                        case '\r':
                            s = "\\r";
                            break;

                        case '\n':
                            s = "\\n";
                            break;

                        case '\"':
                            s = "\\\"";
                            break;

                        case '\0':
                            s = "\\0";
                            break;

                        case '\t':
                            s = "\\t";
                            break;

                        case '\\':
                            s = "\\\\";
                            break;

                        default:
                            s = "0x" + Convert.ToString((int)c, 16);
                            break;
                    }
                    sb.Append(s);
                }
            }

            return sb.ToString();
        }

        // エスケープデコード (C 風の安全なデータ => 本来のデータ)
        public static string DecodeCEscape(string str)
        {
            StringBuilder sb = new StringBuilder();

            int i, j, hex;
            string padding = "00000000";
            str = str + padding;
            StringBuilder sb2;

            for (i = 0; i < str.Length - padding.Length; i++)
            {
                char c = str[i];
                char d = c;

                if (c == '\\')
                {
                    char c1 = str[i + 1];

                    switch (c1)
                    {
                        case '\'':
                            d = '\'';
                            i++;
                            break;

                        case '?':
                            d = '?';
                            i++;
                            break;

                        case '\\':
                            d = '\\';
                            i++;
                            break;

                        case 't':
                            d = '\t';
                            i++;
                            break;

                        case 'r':
                            d = '\r';
                            i++;
                            break;

                        case 'n':
                            d = '\n';
                            i++;
                            break;

                        case ' ':
                            d = ' ';
                            i++;
                            break;

                        case '　':
                            d = '　';
                            i++;
                            break;

                        case '\t':
                            d = '\t';
                            i++;
                            break;

                        case '0':
                            d = '\0';
                            i++;
                            break;

                        case '\"':
                            d = '\"';
                            break;

                        case 'x':
                            i++;
                            sb2 = new StringBuilder();
                            for (j = 0; j < 4; j++)
                            {
                                char c2 = str[++i];

                                if ((c2 >= '0' && c2 <= '9') || (c2 >= 'a' && c2 <= 'f') || (c2 >= 'A' && c2 <= 'F'))
                                {
                                    sb2.Append(c2);
                                }
                                else
                                {
                                    i--;
                                    break;
                                }
                            }
                            hex = Convert.ToInt32(sb2.ToString(), 16);
                            d = (char)hex;
                            break;

                        default:
                            if (c1 >= '0' && c1 <= '7')
                            {
                                sb2 = new StringBuilder();
                                for (j = 0; j < 3; j++)
                                {
                                    char c2 = str[++i];

                                    if (c2 >= '0' && c2 <= '7')
                                    {
                                        sb2.Append(c2);
                                    }
                                    else
                                    {
                                        i--;
                                        break;
                                    }
                                }
                                hex = Convert.ToInt32(sb2.ToString(), 8);
                                d = (char)hex;
                            }
                            else
                            {
                                d = '\\';
                                i++;
                            }
                            break;
                    }
                }

                if (d != '\0')
                {
                    sb.Append(d);
                }
                else
                {
                    break;
                }
            }

            return sb.ToString();
        }

        // 横幅を取得
        public static int GetStrWidth(string? str)
        {
            if (str._IsNullOrZeroLen()) return 0;

            int ret = 0;
            foreach (char c in str!)
            {
                if (c <= 255)
                {
                    ret++;
                }
                else
                {
                    ret += 2;
                }
            }
            return ret;
        }

        // 末尾の \r \n を削除
        public static string TrimCrlf(string? str)
        {
            return str._NonNull().TrimEnd('\r', '\n');
        }

        // 指定した文字列がすべて大文字かどうかチェックする
        public static bool IsAllUpperStr(string? str)
        {
            int i, len;
            // 引数チェック
            if (str == null)
            {
                return false;
            }

            len = str.Length;

            for (i = 0; i < len; i++)
            {
                char c = str[i];

                if ((c >= '0' && c <= '9') ||
                    (c >= 'A' && c <= 'Z'))
                {
                }
                else
                {
                    return false;
                }
            }

            return true;
        }

        // 文字列配列を文字列リストに変換
        public static List<string> StrArrayToList(IEnumerable<string> strArray, bool removeEmpty = false, bool distinct = false, bool distinctCaseSensitive = false)
        {
            List<string> ret = new List<string>();

            foreach (string s in strArray)
            {
                if (removeEmpty == false || s._IsEmpty() == false)
                {
                    ret.Add(s);
                }
            }

            if (distinct)
            {
                List<string> ret2 = new List<string>();
                HashSet<string> tmp = new HashSet<string>();
                foreach (string s in ret)
                {
                    string t = s;
                    if (distinctCaseSensitive == false) t = s.ToUpperInvariant();
                    if (tmp.Add(t))
                    {
                        ret2.Add(t);
                    }
                }
                ret = ret2;
            }

            return ret;
        }

        // コマンドライン文字列をパースする
        private static string[] __new_ParseCmdLine(string str)
        {
            List<string> o;
            int i, len, mode;
            char c;
            StringBuilder tmp;
            bool ignore_space = false;
            // 引数チェック
            if (str == null)
            {
                // トークン無し
                return new string[0];
            }

            o = new List<string>();
            tmp = new StringBuilder();

            mode = 0;
            len = str.Length;
            for (i = 0; i < len; i++)
            {
                c = str[i];

                switch (mode)
                {
                    case 0:
                        // 次のトークンを発見するモード
                        if (c == ' ' || c == '\t')
                        {
                            // 次の文字へ進める
                        }
                        else
                        {
                            // トークンの開始
                            if (c == '\"')
                            {
                                if (str[i + 1] == '\"')
                                {
                                    // 2 重の " は 1 個の " 文字として見なす
                                    tmp.Append("\"");
                                    i++;
                                }
                                else
                                {
                                    // 1 個の " はスペース無視フラグを有効にする
                                    ignore_space = true;
                                }
                            }
                            else
                            {
                                tmp.Append(c);
                            }
                        }

                        mode = 1;
                        break;

                    case 1:
                        if (ignore_space == false && (c == ' ' || c == '\t'))
                        {
                            // トークンの終了
                            o.Add(tmp.ToString());

                            tmp = new StringBuilder();
                            mode = 0;
                        }
                        else
                        {
                            if (c == '\"')
                            {
                                if (str[i + 1] == '\"')
                                {
                                    // 2 重の " は 1 個の " 文字として見なす
                                    tmp.Append("\"");
                                    i++;
                                }
                                else
                                {
                                    if (ignore_space == false)
                                    {
                                        // 1 個の " はスペース無視フラグを有効にする
                                        ignore_space = true;
                                    }
                                    else
                                    {
                                        // スペース無視フラグを無効にする
                                        ignore_space = false;
                                    }
                                }
                            }
                            else
                            {
                                tmp.Append(c);
                            }
                        }
                        break;

                }
            }

            if (tmp.Length >= 1)
            {
                o.Add(tmp.ToString());
            }

            List<string> ret = new List<string>();
            foreach (string s in o)
            {
                ret.Add(s);
            }

            return ret.ToArray();
        }

        // 文字列の比較関数
        public static int CompareString(string s1, string s2)
        {
            try
            {
                return string.Compare(s1, s2, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return 0;
            }
        }
        public static int CompareStringCaseSensitive(string s1, string s2)
        {
            try
            {
                return string.Compare(s1, s2, StringComparison.Ordinal);
            }
            catch
            {
                return 0;
            }
        }

        // 文字列の置換 (置換クラスを利用)
        public static string ReplaceStrWithReplaceClass(string str, object replaceClass, bool caseSensitive = false)
        {
            Type t = replaceClass.GetType();

            List<KeyValuePair<string, string>> list = new List<KeyValuePair<string, string>>();

            // 要素を抽出

            MemberInfo[] members = t.GetMembers(BindingFlags.Instance | BindingFlags.Public);
            foreach (MemberInfo member in members)
            {
                FieldInfo? fi = member as FieldInfo;
                PropertyInfo? pi = member as PropertyInfo;

                string? from = null;
                string? to = null;

                if (fi != null)
                {
                    to = (fi.GetValue(replaceClass))?.ToString();
                    from = fi.Name;
                }
                else if (pi != null)
                {
                    to = (pi.GetValue(replaceClass))?.ToString();
                    from = pi.Name;
                }

                if (from._IsFilled())
                {
                    to = to._NonNull();

                    list.Add(new KeyValuePair<string, string>(from, to));
                }
            }

            // 要素の文字列長順 (長い順) にソート (同じ場合は、文字列でソート)
            list.Sort((x, y) =>
            {
                int r = -x.Key.Length.CompareTo(y.Key.Length);
                if (r != 0) return r;
                return string.Compare(x.Key, y.Key, StringComparison.Ordinal);
            });

            foreach (var kv in list)
            {
                str = str._ReplaceStr(kv.Key, kv.Value, caseSensitive);
            }

            return str;
        }

        // 指定した文字列が出現する位置のリストを取得
        public static int[] FindStringIndexes(string str, string keyword, bool caseSensitive = false)
        {
            List<int> ret = new List<int>();

            str = str._NonNull();

            if (keyword._IsNullOrZeroLen()) return new int[0];

            int len_string, len_keyword;

            int i, j, num;

            len_string = str.Length;
            len_keyword = keyword.Length;

            i = j = num = 0;

            while (true)
            {
                i = SearchStr(str, keyword, i, caseSensitive);
                if (i == -1)
                {
                    break;
                }

                ret.Add(i);

                num++;

                i += len_keyword;
                j = i;
            }

            return ret.ToArray();
        }

        // 指定した文字列が出現する回数のカウント
        public static int GetCountSearchKeywordInStr(string str, string keyword, bool caseSensitive = false)
        {
            var r = FindStringIndexes(str, keyword, caseSensitive);
            if (r == null) return 0;
            return r.Length;
        }

        // 文字列の置換
        public static string ReplaceStr(string str, string oldKeyword, string newKeyword, bool caseSensitive = false)
        {
            if (str._IsNullOrZeroLen())
            {
                return "";
            }

            if (str.Length == 0)
            {
                return str;
            }

            if (oldKeyword._IsNullOrZeroLen())
            {
                return str;
            }

            newKeyword = newKeyword._NonNull();

            return str.Replace(oldKeyword, newKeyword, caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
        }

        // 複数の文字列を検索する
        public static int SearchStrMulti(string str, int start, bool caseSensitive, out int foundIndex, out string foundKeyword, params string[] keywords)
        {
            int i;
            foundIndex = -1;
            foundKeyword = "";
            int ret = -1;
            int min = int.MaxValue;
            for (i = 0; i < keywords.Length; i++)
            {
                string keyword = keywords[i];
                int r = Str.SearchStr(str, keyword, start, caseSensitive);
                if (r != -1)
                {
                    if (min > r)
                    {
                        min = r;
                        foundKeyword = str.Substring(r, keyword.Length);
                        foundIndex = i;
                    }
                }
            }

            if (foundIndex != -1)
            {
                ret = min;
            }

            return ret;
        }

        // 文字列 string から文字列 keyword を検索して最初に見つかった文字の場所を返す
        // (1文字目に見つかったら 0, 見つからなかったら -1)
        public static int SearchStr(string str, string keyword, int start)
        {
            return SearchStr(str, keyword, start, false);
        }
        public static int SearchStr(string str, string keyword, int start, bool caseSensitive)
        {
            if (str == null || keyword == null)
            {
                return -1;
            }

            try
            {
                return str.IndexOf(keyword, start, (caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return -1;
            }
        }

        // Printf
        public static void Printf(string fmt, params object[] args)
        {
            if (args.Length == 0)
            {
                Con.WriteLine(fmt);
            }
            else
            {
                Con.WriteLine(FormatC(fmt, args));
            }
        }

        // 文字列のフォーマット (内部関数)
        public static string FormatC(string fmt)
        {
            return FormatC(fmt, new object[0]);
        }
        public static string FormatC(string fmt, params object[] args)
        {
            int i, len;
            StringBuilder tmp;
            List<string> o;
            int mode = 0;
            int pos = 0;
            // 引数チェック
            if (fmt == null)
            {
                return "";
            }

            len = fmt.Length;
            tmp = new StringBuilder();
            o = new List<string>();

            mode = 0;

            for (i = 0; i < len; i++)
            {
                char c = fmt[i];

                if (mode == 0)
                {
                    // 通常の文字モード
                    switch (c)
                    {
                        case '%':
                            // 書式指定の開始
                            if (fmt[i + 1] == '%')
                            {
                                // 次の文字も % の場合は % を一文字出力するだけ
                                i++;
                                tmp.Append("%");
                            }
                            else
                            {
                                // 次の文字が % でない場合は状態遷移を行う
                                mode = 1;
                                o.Add(tmp.ToString());
                                tmp = new StringBuilder();

                                tmp.Append(c);
                            }
                            break;

                        default:
                            // 通常の文字
                            tmp.Append(c);
                            break;
                    }
                }
                else
                {
                    // 書式指定モード
                    switch (c)
                    {
                        case 'c':
                        case 'C':
                        case 'd':
                        case 'i':
                        case 'o':
                        case 'u':
                        case 'x':
                        case 'X':
                        case 'e':
                        case 'E':
                        case 'f':
                        case 'g':
                        case 'G':
                        case 'a':
                        case 'A':
                        case 'n':
                        case 'p':
                        case 's':
                        case 'S':
                            tmp.Append(c);

                            PrintFParsedParam pp = new PrintFParsedParam(tmp.ToString());
                            string s;
                            if (pp.Ok)
                            {
                                s = pp.GetString(args[pos++]);
                            }
                            else
                            {
                                s = "(parse_error)";
                            }

                            o.Add(s);

                            tmp = new StringBuilder();
                            mode = 0;
                            break;

                        default:
                            tmp.Append(c);
                            break;
                    }
                }
            }

            if (tmp.Length >= 1)
            {
                o.Add(tmp.ToString());
            }

            StringBuilder retstr = new StringBuilder();
            foreach (string stmp in o)
            {
                retstr.Append(stmp);
            }

            return retstr.ToString();
        }

        // 文字列を正規化する
        public static void NormalizeString([AllowNull] ref string str)
        {
            if (str == null)
            {
                str = "";
            }

            UnicodeStdKangxiMapUtil.StrangeToNormal(str);

            str = str.Trim();
        }

        public static string NormalizeString(string? str)
        {
            if (str == null)
            {
                return "";
            }

            return str.Trim();
        }

        // パスワードプロンプト
        public static string? PasswordPrompt()
        {
            Queue<char> ret = new Queue<char>();
            bool escape = false;

            while (true)
            {
                ConsoleKeyInfo ki = Console.ReadKey(true);
                char c = ki.KeyChar;

                if (c >= 0x20 && c <= 0x7e)
                {
                    // 1 文字
                    ret.Enqueue(c);
                    Console.Write("*");
                }
                else if (c == 0x04 || c == 0x1a || c == 0x0d || c == 0x0a)
                {
                    // 終了
                    if (c == 0x04 || c == 0x1a)
                    {
                        escape = true;
                    }
                    break;
                }
                else if (c == 0x08)
                {
                    // BS
                    Console.Write(c);
                    Console.Write(" ");
                    Console.Write(c);

                    if (ret.Count >= 1)
                    {
                        ret.Dequeue();
                    }
                }
            }

            lock (Con.ConsoleWriteLock)
            {
                Console.WriteLine();
            }

            if (escape)
            {
                return null;
            }

            return new string(ret.ToArray());
        }

        // 文字列の長さをチェックする
        public static bool CheckStrLen(string? str, int maxLen)
        {
            if (str == null)
            {
                return false;
            }

            if (str.Length > maxLen)
            {
                return false;
            }

            return true;
        }

        public static bool CheckStrSize(string? str, int maxSize, Encoding encoding)
        {
            if (str == null)
            {
                return false;
            }

            if (encoding.GetByteCount(str) > maxSize)
            {
                return false;
            }

            return true;
        }

        // 文字列が安全かどうか検査する
        public static bool IsSafe(string s, bool pathCharsAreNotGood = false)
        {
            foreach (char c in s)
            {
                if (IsSafe(c, pathCharsAreNotGood) == false)
                {
                    return false;
                }
            }

            return true;
        }

        // 文字が安全かどうか検査する
        public static bool IsSafe(char c, bool pathCharsAreNotGood = false)
        {
            foreach (char bb in InvalidFileNameChars)
            {
                if (bb == c)
                {
                    return false;
                }
            }

            if (pathCharsAreNotGood)
            {
                if (c == '\\' || c == '/')
                {
                    return false;
                }
            }

            return true;
        }

        // ユーザー名として安全か検査
        public static bool IsUsernameSafe(char c)
        {
            if (c >= '0' && c <= '9')
            {
                return true;
            }
            if (c >= 'A' && c <= 'Z')
            {
                return true;
            }
            if (c >= 'a' && c <= 'z')
            {
                return true;
            }
            if (c == '-' || c == '_')
            {
                return true;
            }
            return false;
        }
        public static bool IsUsernameSafe(string str)
        {
            if (str.Length < 3)
            {
                return false;
            }

            char firstChar = str.FirstOrDefault();
            char lastChar = str.LastOrDefault();

            if (firstChar == '-' || firstChar == '_')
            {
                return false;
            }

            if (lastChar == '-' || lastChar == '_')
            {
                return false;
            }

            if (firstChar >= '0' && firstChar <= '9')
            {
                return false;
            }

            int numSpecialChar = 0;

            foreach (char c in str)
            {
                if (IsUsernameSafe(c) == false)
                {
                    return false;
                }

                if (c == '-' || c == '_')
                {
                    numSpecialChar++;
                }
            }

            if (numSpecialChar >= 2)
            {
                return false;
            }

            return true;
        }


        // パスを安全にする (ShiftJIS の観点から)
        public static string MakeSafePathNameShiftJis(string name)
        {
            byte[] sjis = Str.ShiftJisEncoding.GetBytes(name);

            name = Str.ShiftJisEncoding.GetString(sjis);

            char[] a = name.ToCharArray();
            char[] b = Path.GetInvalidFileNameChars();
            StringBuilder sb = new StringBuilder();

            int i;
            for (i = 0; i < a.Length; i++)
            {
                int j;
                bool ok = true;

                for (j = 0; j < b.Length; j++)
                {
                    if (b[j] == a[i])
                    {
                        ok = false;
                        break;
                    }
                }

                if (a[i] == '\\' || a[i] == '/')
                {
                    ok = true;
                    a[i] = Env.PathSeparatorChar;
                }

                if (i == 1 && a[i] == ':')
                {
                    ok = true;
                }

                string s;

                if (ok == false)
                {
                    s = "_" + ((int)a[i]).ToString() + "_";
                }
                else
                {
                    s = "" + a[i];
                }

                sb.Append(s);
            }

            return sb.ToString();
        }

        // パスを安全にする
        static readonly char[] InvalidPathChars = Win32PathInternal.GetInvalidPathChars();
        public static string MakeSafePathName(string name, PathParser? pathParser = null)
        {
            char[] a = name.ToCharArray();
            StringBuilder sb = new StringBuilder();

            int i;
            for (i = 0; i < a.Length; i++)
            {
                int j;
                bool ok = true;

                for (j = 0; j < InvalidPathChars.Length; j++)
                {
                    if (InvalidPathChars[j] == a[i])
                    {
                        ok = false;
                        break;
                    }
                }

                if (a[i] == '\\' || a[i] == '/')
                {
                    ok = true;
                    if (pathParser == null)
                    {
                        a[i] = Env.PathSeparatorChar;
                    }
                    else
                    {
                        a[i] = pathParser.DirectorySeparator;
                    }
                }

                if (i == 1 && a[i] == ':')
                {
                    ok = true;
                }

                string s;

                if (ok == false)
                {
                    s = "_" + ((int)a[i]).ToString() + "_";
                }
                else
                {
                    s = "" + a[i];
                }

                sb.Append(s);
            }

            return sb.ToString();
        }

        // 任意文字列を安全な Ascii 文字のみを含む、かつ、空白文字を含まないファイル名に変換する
        public static string MakeVerySafeAsciiOnlyNonSpaceString(string? src, bool allowDot = false)
        {
            if (src._IsEmpty()) return "";
            src = src._NonNullTrim();

            Str.NormalizeString(ref src, true, true, false, false);

            string okChars = "0123456789-_abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";

            if (allowDot)
            {
                okChars += ".";
            }

            StringBuilder sb = new StringBuilder();

            foreach (char c in src)
            {
                if (okChars.IndexOf(c) != -1)
                {
                    sb.Append(c);
                }
                else
                {
                    sb.Append("_");
                }
            }

            string ret = sb.ToString();
            ret = ret.Trim();

            ret = ret._Split(StringSplitOptions.RemoveEmptyEntries, '_')._Combine("_", true);
            ret = ret._Split(StringSplitOptions.RemoveEmptyEntries, '.')._Combine(".", true);

            ret = ret.Trim();

            if (ret.All(x => x == '.'))
            {
                ret = "_";
            }

            return ret;
        }

        // 任意のファイルパスを安全な Ascii 文字のみを含む、かつ、空白文字を含まないファイル名に変換する
        public static string MakeVerySafeAsciiOnlyNonSpaceFileName(string? fullPath, bool allowDot = false)
        {
            if (fullPath._IsEmpty()) return "";
            fullPath = fullPath._NonNullTrim();

            string fn = PathParser.Windows.GetFileName(fullPath);

            Str.NormalizeString(ref fn, true, true, false, false);

            string okChars = "0123456789-_abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";

            if (allowDot)
            {
                okChars += ".";
            }

            StringBuilder sb = new StringBuilder();

            foreach (char c in fn)
            {
                if (okChars.IndexOf(c) != -1)
                {
                    sb.Append(c);
                }
                else
                {
                    sb.Append("_");
                }
            }

            string ret = sb.ToString();
            ret = ret.Trim();

            ret = ret._Split(StringSplitOptions.RemoveEmptyEntries, '_')._Combine("_", true);

            ret = ret.Trim();

            return ret;
        }

        // ファイル名を安全にする
        static readonly char[] InvalidFileNameChars = Win32PathInternal.GetInvalidFileNameChars();
        public static string MakeSafeFileName(string name)
        {
            char[] a = name.ToCharArray();
            StringBuilder sb = new StringBuilder();

            int i;
            for (i = 0; i < a.Length; i++)
            {
                int j;
                bool ok = true;

                for (j = 0; j < InvalidFileNameChars.Length; j++)
                {
                    if (InvalidFileNameChars[j] == a[i])
                    {
                        ok = false;
                        break;
                    }
                }

                string s;

                if (ok == false)
                {
                    s = "_" + ((int)a[i]).ToString() + "_";
                }
                else
                {
                    s = "" + a[i];
                }

                sb.Append(s);
            }

            return sb.ToString();
        }

        // オブジェクト配列を CSV に変換する
        public static string ObjectArrayToCsv<T>(IEnumerable<T> array, bool withHeader = false)
            where T : notnull
        {
            StringWriter w = new StringWriter();

            if (withHeader)
            {
                string header = ObjectHeaderToCsv<T>();

                w.WriteLine(header);
            }

            foreach (T item in array)
            {
                w.WriteLine(ObjectDataToCsv(item));
            }

            return w.ToString();
        }

        // オブジェクトのヘッダを CSV に変換する
        public static string ObjectHeaderToCsv<T>()
            => ObjectHeaderToCsv(typeof(T));
        public static string ObjectHeaderToCsv(Type objType)
        {
            FieldReaderWriter rw = objType._GetFieldReaderWriter(false);

            List<string> o = new List<string>();

            foreach (string name in rw.FieldOrPropertyNamesList)
            {
                o.Add(name);
            }

            return CombineStringArrayForCsv(o, "#");
        }

        // オブジェクトのデータを CSV に変換する
        public static string ObjectDataToCsv<T>(T obj, FieldReaderWriter? rw = null, IEnumerable<string>? additionalStrList = null) where T : notnull
        {
            if (rw == null) rw = obj._GetFieldReaderWriter(false);

            List<string> o = new List<string>();

            foreach (string name in rw.FieldOrPropertyNamesList)
            {
                object? value = rw.GetValue(obj, name);

                string str = "";

                if (value != null)
                {
                    str = value.ToString()._NonNull();
                }

                o.Add(str);
            }

            if (additionalStrList != null)
            {
                foreach (var s in additionalStrList)
                {
                    o.Add(s._NonNull());
                }
            }

            return CombineStringArrayForCsv(o);
        }

        // 1 行の CSV をオブジェクトデータに変換する
        public static T CsvToObjectData<T>(string csvLine, bool trimStr = false, FieldReaderWriter? rw = null) where T : notnull, new()
        {
            T obj = new T();

            if (rw == null) rw = obj._GetFieldReaderWriter(false);

            string[] tokens = SplitCsvToStringArray(csvLine);

            int i = 0;
            foreach (string name in rw.FieldOrPropertyNamesList)
            {
                string str;

                if (i < tokens.Length)
                    str = tokens[i];
                else
                    str = "";

                str = str._NonNull();

                if (trimStr) str = str.Trim();

                rw.SetValue(obj, name, str);

                i++;
            }

            return obj;
        }

        // 1 行の CSV を複数の文字列に分割する
        public static string[] SplitCsvToStringArray(string csvLine)
        {
            csvLine = csvLine._NonNull();

            // TODO: 後で CombineStringArrayForCsv と同じルールをちゃんと適用すること

            string[] tokens = csvLine._Split(StringSplitOptions.None, ",");

            return tokens;
        }

        // 複数の文字列を CSV 結合する
        public static string CombineStringArrayForCsv(params string?[]? strs)
        {
            return CombineStringArrayForCsv((IEnumerable<string?>?)strs);
        }
        public static string CombineStringArrayForCsv(IEnumerable<string?>? strs, string prefix = "")
        {
            if (strs == null) strs = new string[0];

            List<string> tmp = new List<string>();

            foreach (string? str in strs)
            {
                string str2 = str._NonNull();

                str2 = str2._GetLines()._Combine(" ");

                if (str2._InStr(",") || str2._InStr("\""))
                {
                    str2 = "\"" + str2._ReplaceStr("\"", "\"\"") + "\"";
                }

                tmp.Add(prefix + str2);
            }

            return tmp._Combine(",");
        }

        // 複数の文字列を結合する
        public static string CombineStringArray(string sepstr, params string?[]? strs)
        {
            if (strs == null) return "";

            List<string> tmp = new List<string>();

            foreach (string? str in strs)
            {
                if (str._IsFilled())
                {
                    tmp.Add(str);
                }
            }

            return CombineStringArray(tmp.ToArray(), sepstr);
        }

        public static string CombineStringArray(string sepstr, params object[] objs)
        {
            List<string> tmp = new List<string>();

            foreach (object obj in objs)
            {
                string? str = (obj == null ? "" : obj.ToString());
                if (Str.IsEmptyStr(str) == false)
                {
                    tmp.Add(str!);
                }
            }

            return CombineStringArray(tmp.ToArray(), sepstr);
        }


        public static string CombineStringArray(IEnumerable<string?> strList, string? sepstr = "", bool removeEmpty = false, int maxItems = int.MaxValue, string? ommitStr = "...", int estimatedLength = 0)
        {
            StringBuilder b;

            if (estimatedLength > 0)
            {
                b = new StringBuilder(estimatedLength);
            }
            else
            {
                b = new StringBuilder();
            }

            int num = 0;

            foreach (string? s in strList)
            {
                if (removeEmpty == false || s._IsFilled())
                {
                    if (num >= maxItems)
                    {
                        if (ommitStr._IsNotZeroLen())
                        {
                            b.Append(ommitStr._NonNull());
                        }
                        break;
                    }

                    if (num >= 1)
                    {
                        if (sepstr._IsNotZeroLen())
                        {
                            b.Append(sepstr);
                        }
                    }

                    if (s != null) b.Append(s);

                    num++;
                }
            }

            return b.ToString();
        }

        public static string CombineStringSpan(ReadOnlySpan<string> strList, string? sepstr = "", bool removeEmpty = false, int maxItems = int.MaxValue, string? ommitStr = "...", int estimatedLength = 0)
        {
            StringBuilder b;

            if (estimatedLength > 0)
            {
                b = new StringBuilder(estimatedLength);
            }
            else
            {
                b = new StringBuilder();
            }

            int num = 0;

            foreach (string? s in strList)
            {
                if (removeEmpty == false || s._IsFilled())
                {
                    if (num >= maxItems)
                    {
                        if (ommitStr._IsNotZeroLen())
                        {
                            b.Append(ommitStr._NonNull());
                        }
                        break;
                    }

                    if (num >= 1)
                    {
                        if (sepstr._IsNotZeroLen())
                        {
                            b.Append(sepstr);
                        }
                    }

                    if (s != null) b.Append(s);

                    num++;
                }
            }

            return b.ToString();
        }

        // ある文字を UTF-8 にした場合に何バイトになるか概算する
        [MethodImpl(Inline)]
        public static int GetCharUtf8DataSize(char c)
        {
            if (char.IsSurrogate(c)) return 4;

            return c switch
            {
                <= '\u007F' => 1,         // 0x0000–0x007F
                <= '\u07FF' => 2,         // 0x0080–0x07FF
                _ => 3         // 0x0800–0xFFFF
            };
        }

        // 文字列の最大 UTF-8 数を指定してそこまで切り取る
        public static string TruncStrUtf8DataSize(string? str, int maxSize, string? appendCode = "")
        {
            if (str == null) return "";
            int currentWidth = 0;
            StringBuilder b = new();

            for (int i = 0; i < str.Length; i++)
            {
                char c = str[i];
                int thisWidth = GetCharUtf8DataSize(c);

                currentWidth += thisWidth;
                if (currentWidth > maxSize)
                {
                    break;
                }
                b.Append(c);
            }

            if (appendCode._IsNullOrZeroLen())
            {
                b.Append(appendCode);
            }

            return b.ToString();
        }

        // 文字列の最大ワイド数を指定してそこまで切り取る
        public static string TruncStrWide(string? str, int maxWidth, string? appendCode = "")
        {
            if (str == null) return "";
            int currentWidth = 0;
            StringBuilder b = new();

            for (int i = 0; i < str.Length; i++)
            {
                char c = str[i];
                int thisWidth = 0;
                if (c >= 0 && c <= 255)
                {
                    thisWidth += 1;
                }
                else
                {
                    thisWidth += 2;
                }

                currentWidth += thisWidth;
                if (currentWidth > maxWidth)
                {
                    break;
                }
                b.Append(c);
            }

            if (appendCode._IsNullOrZeroLen())
            {
                b.Append(appendCode);
            }

            return b.ToString();
        }

        // 文字列の最大長を指定してそこまで切り取る
        public static string TruncStr(string? str, int len)
        {
            if (str == null)
            {
                return "";
            }
            if (str.Length <= len)
            {
                return str;
            }
            else
            {
                return str.Substring(0, len);
            }
        }

        // 文字列の最大長を指定してそこまで切り取る
        public static string TruncStrEx(string? str, int len, string? appendCode = "...")
        {
            if (str == null)
            {
                return "";
            }
            if (str.Length <= len)
            {
                return str;
            }
            else
            {
                if (appendCode != null)
                {
                    return str.Substring(0, len) + appendCode;
                }
                else
                {
                    return str.Substring(0, len);
                }
            }
        }

        // 文字列が最大長を超える時は中間を省略する
        public static string TruncStrMiddle(string? str, int maxLen, string appendCode = "..")
        {
            str = str._NonNullTrim();
            if (str._IsEmpty()) return str;

            appendCode._NotEmptyCheck();

            appendCode = appendCode.Trim();

            int appendCodeLen = appendCode.Length;
            maxLen = Math.Max(maxLen, appendCodeLen + 2);

            int strLen = str.Length;

            if (strLen <= maxLen) return str;

            int leftLen = maxLen / 2;
            int rightLen = maxLen - leftLen;

            return str.Substring(0, leftLen) + appendCode + str.Substring(strLen - rightLen, rightLen);
        }

        // おもしろ黒塗り
        public static string Kuronuri(string src, char replaceChar = '■')
        {
            StringBuilder sb = new StringBuilder();

            foreach (char c in src)
            {
                if (c == '\r' || c == '\n' || c == ' ' || c == '\t' || c == '　' || c == '.' || c == '(' || c == ')')
                {
                    sb.Append(c);
                }
                else
                {
                    sb.Append(replaceChar);
                }
            }

            return sb.ToString();
        }

        // 新しい GUID を生成
        public static string NewGuid() => System.Guid.NewGuid().ToString("N");

        // 新しい乱数文字列を生成
        public static string GenRandStr()
        {
            return ByteToStr(Secure.HashSHA1(Guid.NewGuid().ToByteArray()));
        }

        // 新しい数字だけのパスワードを生成
        public static string GenRandNumericPassword(int count = 16)
        {
            count._SetMax(1);

            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < count; i++)
            {
                int r = Secure.RandSInt31();
                char c = (char)('0' + (r % 10));
                sb.Append(c);
            }

            return sb.ToString();
        }
        public static string GenRandNumericPasswordWithBlocks(int lenPerBlock = 4, int numBlocks = 4, char sepstr = '_')
        {
            lenPerBlock._SetMax(1);
            numBlocks._SetMax(1);

            List<string> o = new List<string>();

            for (int i = 0; i < numBlocks; i++)
            {
                o.Add(GenRandNumericPassword(lenPerBlock));
            }

            return o._Combine(sepstr);
        }

        // 新しいパスワードを生成
        public static string GenRandPassword(int count = 16, bool mustHaveOneUnderBar = true)
        {
            count._SetMax(4);

            while (true)
            {
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < count; i++)
                {
                    char c;
                    int type = Secure.RandSInt31() % 5;
                    int r = Secure.RandSInt31();
                    if (type == 0)
                    {
                        c = (char)('0' + (r % 10));
                    }
                    else if (type == 1 || type == 2)
                    {
                        c = (char)('a' + (r % 26));
                    }
                    else
                    {
                        c = (char)('A' + (r % 26));
                    }
                    sb.Append(c);
                }

                if (mustHaveOneUnderBar)
                {
                    sb[Secure.RandSInt31() % (sb.Length - 2) + 1] = '_';
                }

                string ret = sb.ToString();

                bool b1 = false;
                bool b2 = false;
                bool b3 = false;

                foreach (char c in ret)
                {
                    if ('0' <= c && c <= '9') b1 = true;
                    if ('a' <= c && c <= 'z') b2 = true;
                    if ('A' <= c && c <= 'Z') b3 = true;
                }

                if (b1 && b2 && b3) return ret;
            }
        }

        // 任意のバイト列を Base62 ベースのダイジェストに変換
        // 8 文字: 約 47 bit (誕生日問題母数: 16777216)
        // 10 文字: 約 59 bit (誕生日問題母数: 536870912)
        // 12 文字: 約 71 bit (誕生日問題母数: 68719476736)
        // (小文字の場合)
        // 8 文字: 約 41 bit (誕生日問題母数: 1048576)
        // 10 文字: 約 57 bit (誕生日問題母数: 268435456)
        // 12 文字: 約 62 bit (誕生日問題母数: 2147483648)
        public static string GetBase62DigestStr(ReadOnlySpan<byte> data, int strLen = 12)
        {
            if (strLen <= 4 || strLen > 40) throw new ArgumentOutOfRangeException(nameof(strLen));

            byte[] hash = Secure.HashSHA256(data);

            string b64 = Str.Base64Encode(hash);

            string b62 = b64.Replace("+", "a").Replace("/", "B").Replace("=", "c");

            string tmp = b62.Substring(0, strLen);

            char c = tmp[0];
            string tmp2 = tmp.Substring(1);

            if (c >= '0' && c <= '9') c = (char)('D' + (c - '0'));

            return c + tmp2;
        }

        // 文字列をハッシュ
        public static byte[] HashStrSHA1(string? str)
            => HashStr(str);
        public static byte[] HashStr(string? str)
        {
            return Secure.HashSHA1(Str.Utf8Encoding.GetBytes(str._NonNull()));
        }
        public static ulong HashStrToLong(string str)
        {
            Buf b = new Buf();
            b.Write(HashStr(str));
            b.SeekToBegin();
            return b.ReadInt64();
        }
        public static byte[] HashStrSHA256(string? str)
        {
            str = str._NonNull();
            return Secure.HashSHA256(Str.Utf8Encoding.GetBytes(str));
        }

        // SHA-256 パスワードハッシュ
        public static string HashPasswordSHA256(string password)
        {
            return Str.ByteToHex(Secure.HashSHA256(Str.Utf8Encoding.GetBytes(password))).ToUpperInvariant();
        }

        // バイト列を文字列に変換
        public static string ByteToStr(byte[] data)
        {
            return ByteToStr(data, "");
        }
        public static string ByteToStr(byte[] data, string paddingStr)
        {
            StringBuilder sb = new StringBuilder();

            int i;
            for (i = 0; i < data.Length; i++)
            {
                byte b = data[i];
                sb.Append(b.ToString("X2"));

                if (i != (data.Length - 1))
                {
                    sb.Append(paddingStr);
                }
            }

            return sb.ToString();
        }

        // 乱数文字を 16 進数に変換する
        public static string RandToStr6(string rand)
        {
            byte[] hash = HashStr(rand + "coreutil");
            return ByteToStr(hash).Substring(0, 6);
        }

        // 指定された文字が ASCII 文字かどうかチェックする
        public static bool IsAscii(char c)
        {
            if (c >= '0' && c <= '9')
            {
                return true;
            }
            if (c >= 'A' && c <= 'Z')
            {
                return true;
            }
            if (c >= 'a' && c <= 'z')
            {
                return true;
            }
            if (c == '!' || c == '\"' || c == '#' || c == '$' || c == '%' || c == '&' || c == '\'' ||
                c == '(' || c == ')' || c == '-' || c == ' ' || c == '=' || c == '~' || c == '^' || c == '_' ||
                c == '\\' || c == '|' || c == '{' || c == '}' || c == '[' || c == ']' || c == '@' ||
                c == '*' || c == '+' || c == '.' || c == '<' || c == '>' ||
                c == ',' || c == '?' || c == '/' || c == ' ' || c == '^' || c == '\'' || c == ':' || c == ';' || c == '`')
            {
                return true;
            }
            return false;
        }
        public static bool IsAscii(string str)
        {
            foreach (char c in str)
            {
                if (IsAscii(c) == false)
                {
                    return false;
                }
            }
            return true;
        }


        // 指定された文字が ASCII 文字かどうかチェックする
        public static bool IsPasswordSafe(char c)
        {
            if (c >= '0' && c <= '9')
            {
                return true;
            }
            if (c >= 'A' && c <= 'Z')
            {
                return true;
            }
            if (c >= 'a' && c <= 'z')
            {
                return true;
            }
            if (c == '!' || c == '$' || c == '%' || c == '&' ||
                c == '(' || c == ')' || c == '-' || c == '=' || c == '~' || c == '^' || c == '_' ||
                c == '{' || c == '}' || c == '[' || c == ']' || c == '@' ||
                c == '+' || c == '.' || c == '<' || c == '>' ||
                c == ',' || c == '?' || c == '^')
            {
                return true;
            }
            return false;
        }
        public static bool IsPasswordSafe(string str)
        {
            foreach (char c in str)
            {
                if (IsPasswordSafe(c) == false)
                {
                    return false;
                }
            }
            return true;
        }


        // 通信速度文字列
        public static string GetBpsStr(long size)
        {
            if (size >= 1000000000000L)
            {
                return ((double)(size) / 1000.0f / 1000.0f / 1000.0f / 1000.0f).ToString(".00") + " Tbps";
            }
            if (size >= 1000 * 1000 * 1000)
            {
                return ((double)(size) / 1000.0f / 1000.0f / 1000.0f).ToString(".00") + " Gbps";
            }
            if (size >= 1000 * 1000)
            {
                return ((double)(size) / 1000.0f / 1000.0f).ToString(".00") + " Mbps";
            }
            if (size >= 1000)
            {
                return ((double)(size) / 1000.0f).ToString(".00") + " Kbps";
            }
            return ((double)(size)).ToString() + " bps";
        }

        // ファイルサイズ文字列
        public static string GetFileSizeStr(long size)
        {
            size = Math.Min(size, long.MaxValue);
            if (size >= 1099511627776L)
            {
                return ((double)(size) / 1024.0f / 1024.0f / 1024.0f / 1024.0f).ToString(".00") + " TB";
            }
            if (size >= 1024 * 1024 * 1024)
            {
                return ((double)(size) / 1024.0f / 1024.0f / 1024.0f).ToString(".00") + " GB";
            }
            if (size >= 1024 * 1024)
            {
                return ((double)(size) / 1024.0f / 1024.0f).ToString(".00") + " MB";
            }
            if (size >= 1024)
            {
                return ((double)(size) / 1024.0f).ToString(".00") + " KB";
            }
            return ((double)(size)).ToString() + " Bytes";
        }

        // 2 つの hex 文字列を比較する
        public static int CompareHex(string? hex1, string? hex2)
        {
            return Util.MemCompare(hex1._GetHexBytes(), hex2._GetHexBytes());
        }
        public static bool IsSameHex(string? hex1, string? hex2)
        {
            return Util.MemEquals(hex1._GetHexBytes(), hex2._GetHexBytes());
        }

        // int 型を文字列に変換する
        public static string IntToStr(int i)
        {
            return i.ToString();
        }
        public static string IntToStr(uint i)
        {
            return i.ToString();
        }

        // long 型を文字列に変換する
        public static string LongToStr(long i)
        {
            return i.ToString();
        }
        public static string LongToStr(ulong i)
        {
            return i.ToString();
        }

        // 文字列を Enum に変換する
        public static T ParseEnum<T>(string? str, T defaultValue, bool exactOnly = false, bool noMatchError = false) where T : unmanaged, Enum
        {
            return (T)StrToEnum(str, defaultValue, exactOnly, noMatchError);
        }
        public static object ParseEnum(object value, object defaultValue, bool exactOnly = false, bool noMatchError = false)
        {
            return ParseEnum(value.ToString()!, defaultValue, exactOnly, noMatchError);
        }
        public static object ParseEnum(string str, object defaultValue, bool exactOnly = false, bool noMatchError = false)
        {
            return StrToEnum(str, defaultValue, exactOnly, noMatchError);
        }

        static class EnumCacheRawValue<T> where T : unmanaged, Enum
        {
            public static readonly Dictionary<string, ulong> DictCastSensitive = new Dictionary<string, ulong>();
            public static readonly Dictionary<string, ulong> DictCastIgnore = new Dictionary<string, ulong>(StrComparer.IgnoreCaseComparer);
            public static readonly Dictionary<ulong, string> DictULongToStr = new Dictionary<ulong, string>();
            public static readonly IEnumerable<string> ElementNamesOrderByValue;
            public static readonly IEnumerable<string> ElementNamesOrderByName;

            static EnumCacheRawValue()
            {
                Type t = typeof(T);
                string[] names = Enum.GetNames(t);

                foreach (string name in names)
                {
                    T value = (T)Enum.Parse(t, name);
                    ulong u64 = value._RawReadValueUInt64();

                    DictCastSensitive.Add(name, u64);
                    DictCastIgnore.Add(name, u64);
                    DictULongToStr.Add(u64, name);
                }

                ElementNamesOrderByValue = DictCastSensitive.OrderBy(x => x.Value).Select(x => x.Key).Distinct();
                ElementNamesOrderByName = DictCastSensitive.OrderBy(x => x.Key, StrComparer.IgnoreCaseComparer).Select(x => x.Key).Distinct();
            }
        }

        public static IEnumerable<KeyValuePair<string, object>> GetEnumValuesList(Type type)
        {
            return EnumCacheCaseSensitive[type];
        }

        static Singleton<Type, Dictionary<string, object>> EnumCacheCaseSensitive = new Singleton<Type, Dictionary<string, object>>(t =>
        {
            string[] names = Enum.GetNames(t);
            Dictionary<string, object> d = new Dictionary<string, object>();
            foreach (string name in names)
            {
                d.Add(name, Enum.Parse(t, name));
            }
            return d;
        });

        static Singleton<Type, Dictionary<string, object>> EnumCacheCaseIgnore = new Singleton<Type, Dictionary<string, object>>(t =>
        {
            string[] names = Enum.GetNames(t);
            Dictionary<string, object> d = new Dictionary<string, object>(StrComparer.IgnoreCaseComparer);
            foreach (string name in names)
            {
                d.Add(name, Enum.Parse(t, name));
            }
            return d;
        });

        public static IEnumerable<string> GetDefinedEnumElementsStrList<T>(T anyOfEnumSampleElement)
             where T : unmanaged, Enum
        {
            return EnumCacheRawValue<T>.ElementNamesOrderByValue;
        }

        public static T StrToEnumBits<T>(string? str, T defaultValue, params char[] separators)
             where T : unmanaged, Enum
        {
            if (str._IsNullOrZeroLen()) return defaultValue;

            string[] strs = str._Split(StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries, separators);

            var dict1 = EnumCacheRawValue<T>.DictCastSensitive;
            var dict2 = EnumCacheRawValue<T>.DictCastIgnore;

            ulong ret = 0;

            foreach (string token in strs)
            {
                if (token[0] >= '0' && token[0] <= '9')
                {
                    ret |= token._ToULong();
                }
                else
                {
                    if (dict1.TryGetValue(token, out ulong value))
                    {
                        ret |= value;
                    }
                    else if (dict2.TryGetValue(token, out ulong value2))
                    {
                        ret |= value2;
                    }
                }
            }

            T ret2 = default;
            ret2._RawWriteValueUInt64(ret);

            return ret2;
        }

        public static object StrToEnum(string? str, object defaultValue, bool exactOnly = false, bool noMatchError = false)
        {
            if (str._IsNullOrZeroLen()) return defaultValue;

            Type type = defaultValue.GetType();
            if (EnumCacheCaseSensitive[type].TryGetValue(str, out object? ret))
            {
                return ret;
            }
            if (EnumCacheCaseIgnore[type].TryGetValue(str, out object? ret2))
            {
                return ret2;
            }
            if (exactOnly == false)
            {
                if (Enum.TryParse(type, str, out object? ret3))
                {
                    if (ret3 == null) return defaultValue;

                    return ret3;
                }
            }
            if (noMatchError)
                throw new ArgumentException($"The string \"{str}\' doesn't match to any items of the type \"{type.Name}\".");
            return defaultValue;
        }

        public static string EnumToStrExact<T>(T value, string? defaultStr = null) where T : unmanaged, Enum
        {
            ulong u64 = value._RawReadValueUInt64();

            if (EnumCacheRawValue<T>.DictULongToStr.TryGetValue(u64, out string? name))
            {
                return name;
            }

            if (defaultStr == null)
            {
                return u64.ToString();
            }
            else
            {
                return defaultStr;
            }
        }

        // 文字列を bool に変換する
        public static bool StrToBool(string? s, bool defaultValue = false)
        {
            if (s._IsNullOrZeroLen())
            {
                return defaultValue;
            }

            Str.NormalizeString(ref s, true, true, false, false);

            if (s.StartsWith("y", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (s.StartsWith("t", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (s.StartsWith("ok", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (s.StartsWith("on", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (s.StartsWith("enable", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (s.StartsWith("active", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (Str.StrToInt(s) != 0)
            {
                return true;
            }

            return false;
        }

        // 文字列を double に変換する
        public static double StrToDouble(string? str)
        {
            try
            {
                Str.RemoveSpaceChar(ref str);
                Str.NormalizeString(ref str, true, true, false, false);
                if (double.TryParse(str, out double ret))
                {
                    return ret;
                }

                return 0;
            }
            catch
            {
                return 0.0;
            }
        }

        // 文字列を decimal に変換する
        public static decimal StrToDecimal(string? str)
        {
            try
            {
                Str.RemoveSpaceChar(ref str);
                Str.NormalizeString(ref str, true, true, false, false);
                if (decimal.TryParse(str, out decimal ret))
                {
                    return ret;
                }

                return 0;
            }
            catch
            {
                return 0.0m;
            }
        }

        // 4bit 文字を byte 型に変換する
        [MethodImpl(Inline)]
        public static byte Char4BitToByte(char c)
        {
            if (c >= '0' && c <= '9')
            {
                return (byte)(c - '0');
            }
            else if (c >= 'A' && c <= 'F')
            {
                return (byte)(c - 'A' + 10);
            }
            else if (c >= 'a' && c <= 'f')
            {
                return (byte)(c - 'a' + 10);
            }
            else
            {
                throw new CoresException($"Character '{c}' is not hex");
            }
        }

        // 文字列を int 型に変換する
        public static int StrToInt(string? str)
        {
            try
            {
                Str.RemoveSpaceChar(ref str);
                Str.NormalizeString(ref str, true, true, false, false);
                str = str.Replace(",", "");
                if (int.TryParse(str, out int ret))
                {
                    return ret;
                }

                return 0;
            }
            catch
            {
                return 0;
            }
        }
        public static uint StrToUInt(string? str)
        {
            try
            {
                Str.RemoveSpaceChar(ref str);
                Str.NormalizeString(ref str, true, true, false, false);
                str = str.Replace(",", "");
                if (uint.TryParse(str, out uint ret))
                {
                    return ret;
                }

                return 0;
            }
            catch
            {
                return 0;
            }
        }

        // 指定した文字列を先頭部分からパースし、数字部分のみを抽出して無理矢理 long に変換する
        public static long DirtyStrToLong(string? str)
        {
            try
            {
                str = str._NonNullTrim();

                Str.RemoveSpaceChar(ref str);
                Str.NormalizeString(ref str, true, true, false, false);
                str = str.Replace(",", "");

                str = str._NonNullTrim();

                bool negative = false;

                if (str.Length >= 1)
                {
                    if (str[0] == '-')
                    {
                        negative = true;
                        str = str.Substring(1);
                    }

                    StringBuilder b = new();

                    foreach (char c in str)
                    {
                        if (c >= '0' && c <= '9')
                        {
                            b.Append(c);
                        }
                        else
                        {
                            break;
                        }
                    }

                    long i = StrToLong(b.ToString());

                    if (negative)
                    {
                        i = i * -1;
                    }

                    return i;
                }

                return 0;
            }
            catch
            {
                return 0;
            }
        }
        public static int DirtyStrToInt(string? str)
        {
            try
            {
                checked
                {
                    long v = DirtyStrToLong(str);

                    return (int)v;
                }
            }
            catch
            {
                return 0;
            }
        }

        // 文字列を long 型に変換する
        public static long StrToLong(string? str)
        {
            try
            {
                Str.RemoveSpaceChar(ref str);
                Str.NormalizeString(ref str, true, true, false, false);
                str = str.Replace(",", "");
                if (long.TryParse(str, out long ret))
                {
                    return ret;
                }

                return 0;
            }
            catch
            {
                return 0;
            }
        }
        public static ulong StrToULong(string? str)
        {
            try
            {
                Str.RemoveSpaceChar(ref str);
                Str.NormalizeString(ref str, true, true, false, false);
                str = str.Replace(",", "");
                if (ulong.TryParse(str, out ulong ret))
                {
                    return ret;
                }

                return 0;
            }
            catch
            {
                return 0;
            }
        }

        // 文字列を日時に変換する
        public static bool IsStrDateTime(string str)
        {
            try
            {
                Str.NormalizeString(ref str, true, true, false, false);
                StrToDateTime(str);
                return true;
            }
            catch
            {
                return false;
            }
        }
        public static DateTime StrToDateTime(string? str, bool toUtc = false, bool emptyToZeroDateTime = false)
        {
            if (str._IsEmpty())
            {
                if (emptyToZeroDateTime) return Util.ZeroDateTimeValue;
                return new DateTime(0);
            }
            DateTime ret = new DateTime(0);

            Str.NormalizeString(ref str, true, true, false, false);
            str = str.Trim();
            string[] sps =
                {
                    " ",
                    "_",
                    "　",
                    "\t",
                    "T",
                };

            string[] tokens = str.Split(sps, StringSplitOptions.RemoveEmptyEntries);

            if (tokens.Length != 2)
            {
                int r1 = str.IndexOf("年", StringComparison.OrdinalIgnoreCase);
                int r2 = str.IndexOf("月", StringComparison.OrdinalIgnoreCase);
                int r3 = str.IndexOf("日", StringComparison.OrdinalIgnoreCase);

                if (r1 != -1 && r2 != -1 && r3 != -1)
                {
                    tokens = new string[2];

                    tokens[0] = str.Substring(0, r3 + 1);
                    tokens[1] = str.Substring(r3 + 1);
                }
            }

            if (tokens.Length == 2)
            {
                DateTime dt1 = StrToDate(tokens[0]);
                DateTime dt2 = StrToTime(tokens[1]);

                ret = dt1.Date + dt2.TimeOfDay;
            }
            else if (tokens.Length == 1)
            {
                if (tokens[0].Length == 14)
                {
                    // yyyymmddhhmmss
                    DateTime dt1 = StrToDate(tokens[0].Substring(0, 8));
                    DateTime dt2 = StrToTime(tokens[0].Substring(8));

                    ret = dt1.Date + dt2.TimeOfDay;
                }
                else if (tokens[0].Length == 12)
                {
                    // yymmddhhmmss
                    DateTime dt1 = StrToDate(tokens[0].Substring(0, 6));
                    DateTime dt2 = StrToTime(tokens[0].Substring(6));

                    ret = dt1.Date + dt2.TimeOfDay;
                }
                else
                {
                    // 日付のみ
                    DateTime dt1 = StrToDate(tokens[0]);

                    ret = dt1.Date;
                }
            }
            else
            {
                throw new ArgumentException(str);
            }

            if (toUtc) ret = ret.ToUniversalTime();

            return ret;
        }

        // 文字列を時刻に変換する
        public static bool IsStrTime(string str)
        {
            try
            {
                Str.NormalizeString(ref str, true, true, false, false);
                StrToTime(str);
                return true;
            }
            catch
            {
                return false;
            }
        }
        public static DateTime StrToTime(string? str, bool toUtc = false, bool emptyToZeroDateTime = false)
        {
            if (emptyToZeroDateTime && str._IsEmpty()) return Util.ZeroDateTimeValue;

            DateTime ret = new DateTime(0);

            string[] sps =
                {
                    "/",
                    "-",
                    ":",
                    "時",
                    "分",
                    "秒",
                };
            Str.NormalizeString(ref str, true, true, false, false);
            str = str.Trim();

            string[] tokens;

            tokens = str.Split(sps, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 3)
            {
                // hh:mm:ss or hh:mm:ss.mmmmmm
                string hourStr = tokens[0];
                string minuteStr = tokens[1];
                string secondStr = tokens[2];
                string msecStr = "";
                int hour = -1;
                int minute = -1;
                int second = -1;
                int msecond = 0;
                long add_ticks = 0;

                int msec_index = secondStr._Search(".");
                if (msec_index != -1)
                {
                    msecStr = secondStr.Substring(msec_index + 1);
                    secondStr = secondStr.Substring(0, msec_index);

                    msecStr = "0." + msecStr;

                    decimal tmp = msecStr._ToDecimal();
                    msecond = (int)((tmp % 1.0m) * 1000.0m);
                    add_ticks = (int)((tmp % 0.001m) * 10000000.0m);
                }

                if ((hourStr.Length == 1 || hourStr.Length == 2) && IsNumber(hourStr))
                {
                    hour = StrToInt(hourStr);
                }
                if ((minuteStr.Length == 1 || minuteStr.Length == 2) && IsNumber(minuteStr))
                {
                    minute = StrToInt(minuteStr);
                }
                if ((secondStr.Length == 1 || secondStr.Length == 2) && IsNumber(secondStr))
                {
                    second = StrToInt(secondStr);
                }

                if (hour < 0 || hour >= 25 || minute < 0 || minute >= 60 || second < 0 || second >= 60 || msecond < 0 || msecond >= 1000)
                {
                    throw new ArgumentException(str);
                }

                ret = new DateTime(2000, 1, 1, hour, minute, second, msecond).AddTicks(add_ticks);
            }
            else if (tokens.Length == 2)
            {
                // hh:mm
                string hourStr = tokens[0];
                string minuteStr = tokens[1];
                int hour = -1;
                int minute = -1;
                int second = 0;

                if ((hourStr.Length == 1 || hourStr.Length == 2) && IsNumber(hourStr))
                {
                    hour = StrToInt(hourStr);
                }
                if ((minuteStr.Length == 1 || minuteStr.Length == 2) && IsNumber(minuteStr))
                {
                    minute = StrToInt(minuteStr);
                }

                if (hour < 0 || hour >= 25 || minute < 0 || minute >= 60 || second < 0 || second >= 60)
                {
                    throw new ArgumentException(str);
                }

                ret = new DateTime(2000, 1, 1, hour, minute, second);
            }
            else if (tokens.Length == 1)
            {
                string hourStr = tokens[0];
                int hour = -1;
                int minute = 0;
                int second = 0;
                int msec = 0;

                if ((hourStr.Length == 1 || hourStr.Length == 2) && IsNumber(hourStr))
                {
                    // hh
                    hour = StrToInt(hourStr);
                }
                else
                {
                    if ((hourStr.Length == 4) && IsNumber(hourStr))
                    {
                        // hhmm
                        int i = StrToInt(hourStr);
                        hour = i / 100;
                        minute = i % 100;
                    }
                    else if ((hourStr.Length == 6) && IsNumber(hourStr))
                    {
                        // hhmmss
                        int i = StrToInt(hourStr);
                        hour = i / 10000;
                        minute = ((i % 10000) / 100);
                        second = i % 100;
                    }
                    else if ((hourStr.Length == 10 && hourStr[6] == '.'))
                    {
                        // hhmmss.abc
                        int i = StrToInt(hourStr.Substring(0, 6));
                        hour = i / 10000;
                        minute = ((i % 10000) / 100);
                        second = i % 100;

                        msec = StrToInt(hourStr.Substring(7));
                    }
                }

                if (hour < 0 || hour >= 25 || minute < 0 || minute >= 60 || second < 0 || second >= 60 || msec < 0 || msec >= 1000)
                {
                    throw new ArgumentException(str);
                }

                ret = new DateTime(2000, 1, 1, hour, minute, second, msec);
            }
            else
            {
                throw new ArgumentException(str);
            }

            if (toUtc)
            {
                ret = ret.ToUniversalTime();
            }

            return ret;
        }

        // 文字列を日付に変換する
        public static bool IsStrDate(string str)
        {
            try
            {
                Str.NormalizeString(ref str, true, true, false, false);
                StrToDate(str);
                return true;
            }
            catch
            {
                return false;
            }
        }
        public static DateTime StrToDate(string? str, bool toUtc = false, bool emptyToZeroDateTime = false)
        {
            if (emptyToZeroDateTime && str._IsEmpty()) return Util.ZeroDateTimeValue;

            string[] sps =
                {
                    "/",
                    "/",
                    "-",
                    ":",
                    "年",
                    "月",
                    "日",
                };
            str = str._NonNullTrim();
            Str.NormalizeString(ref str, true, true, false, false);

            string[] youbi =
            {
                "月", "火", "水", "木", "金", "土", "日",
            };

            foreach (string ys in youbi)
            {
                string ys2 = string.Format("({0})", ys);

                str = str.Replace(ys2, "");
            }

            string[] tokens;

            DateTime ret = new DateTime(0);

            tokens = str.Split(sps, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 3)
            {
                // yyyy/mm/dd
                string yearStr = tokens[0];
                string monthStr = tokens[1];
                string dayStr = tokens[2];
                int year = 0;
                int month = 0;
                int day = 0;

                if ((yearStr.Length == 1 || yearStr.Length == 2) && IsNumber(yearStr))
                {
                    year = 2000 + StrToInt(yearStr);
                }
                else if (yearStr.Length == 4 && IsNumber(yearStr))
                {
                    year = StrToInt(yearStr);
                }

                if ((monthStr.Length == 1 || monthStr.Length == 2) && IsNumber(monthStr))
                {
                    month = StrToInt(monthStr);
                }
                if ((dayStr.Length == 1 || dayStr.Length == 2) && IsNumber(dayStr))
                {
                    day = StrToInt(dayStr);
                }

                if (year < Consts.Numbers.MinYear || year > Consts.Numbers.MaxYear || month <= 0 || month >= 13 || day <= 0 || day >= 32)
                {
                    throw new ArgumentException(str);
                }

                ret = new DateTime(year, month, day);
            }
            else if (tokens.Length == 1)
            {
                if (str.Length == 8)
                {
                    // yyyymmdd
                    string yearStr = str.Substring(0, 4);
                    string monthStr = str.Substring(4, 2);
                    string dayStr = str.Substring(6, 2);
                    int year = int.Parse(yearStr);
                    int month = int.Parse(monthStr);
                    int day = int.Parse(dayStr);

                    if (year < Consts.Numbers.MinYear || year > Consts.Numbers.MaxYear || month <= 0 || month >= 13 || day <= 0 || day >= 32)
                    {
                        throw new ArgumentException(str);
                    }

                    ret = new DateTime(year, month, day);
                }
                else if (str.Length == 6)
                {
                    // yymmdd
                    string yearStr = str.Substring(0, 2);
                    string monthStr = str.Substring(2, 2);
                    string dayStr = str.Substring(4, 2);
                    int year = int.Parse(yearStr) + 2000;
                    int month = int.Parse(monthStr);
                    int day = int.Parse(dayStr);

                    if (year < Consts.Numbers.MinYear || year > Consts.Numbers.MaxYear || month <= 0 || month >= 13 || day <= 0 || day >= 32)
                    {
                        throw new ArgumentException(str);
                    }

                    ret = new DateTime(year, month, day);
                }
            }
            else
            {
                throw new ArgumentException(str);
            }

            if (toUtc)
            {
                ret = ret.ToUniversalTime();
            }

            return ret;
        }

        // 時刻を文字列に変換する
        public static string TimeToStr(DateTime dt)
        {
            return TimeToStr(dt, false);
        }
        public static string TimeToStr(DateTime dt, CoreLanguage lang)
        {
            return TimeToStr(dt, false, lang);
        }
        public static string TimeToStr(DateTime dt, bool toLocalTime)
        {
            return TimeToStr(dt, toLocalTime, CoreLanguageClass.CurrentThreadLanguage);
        }
        public static string TimeToStr(DateTime dt, bool toLocalTime, CoreLanguage lang)
        {
            string s = DateTimeToStr(dt, toLocalTime, lang);

            string[] tokens = s.Split(' ');

            return tokens[1];
        }
        public static string TimeToStrShort(DateTime dt)
        {
            return TimeToStrShort(dt, false);
        }
        public static string TimeToStrShort(DateTime dt, bool toLocalTime)
        {
            string s = DateTimeToStrShort(dt, toLocalTime);

            string[] tokens = s.Split('_');

            return tokens[1];
        }

        // 日付を文字列に変換する
        public static string DateToStr(DateTime dt)
        {
            return DateToStr(dt, false);
        }
        public static string DateToStr(DateTime dt, CoreLanguage lang)
        {
            return DateToStr(dt, false, lang);
        }
        public static string DateToStr(DateTime dt, bool toLocalTime)
        {
            return DateToStr(dt, toLocalTime, false);
        }
        public static string DateToStr(DateTime dt, bool toLocalTime, CoreLanguage lang)
        {
            return DateToStr(dt, toLocalTime, false, lang);
        }
        public static string DateToStr(DateTime dt, bool toLocalTime, bool noDayOfWeek)
        {
            return DateToStr(dt, toLocalTime, noDayOfWeek, CoreLanguageClass.CurrentThreadLanguage);
        }
        public static string DateToStr(DateTime dt, bool toLocalTime, bool noDayOfWeek, CoreLanguage lang)
        {
            string s = DateTimeToStr(dt, toLocalTime, lang);

            string[] tokens = s.Split(' ');

            string ret = tokens[0];

            if (noDayOfWeek)
            {
                string[] tokens2 = s.Split('(');

                ret = tokens2[0];
            }

            return ret;
        }
        public static string DateToStrShort(DateTime dt)
        {
            return DateToStrShort(dt, false);
        }
        public static string DateToStrShort(DateTime dt, bool toLocalTime)
        {
            string s = DateTimeToStrShort(dt, toLocalTime);

            string[] tokens = s.Split('_');

            return tokens[0];
        }

        // 曜日を日付に変換する
        public static string DayOfWeekToStr(CoreLanguage lang, int d)
        {
            if (lang == CoreLanguage.Japanese)
            {
                string[] youbi =
                {
                    "日", "月", "火", "水", "木", "金", "土",
                };

                return youbi[d];
            }
            else
            {
                string[] youbi =
                {
                    "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday",
                };

                return youbi[d];
            }
        }

        public static string TimeSpanToTsStr(TimeSpan ts, bool withMSecs = false, bool withNanoSecs = false)
        {
            string tag = "";

            if (withNanoSecs)
                tag = @"\.fffffff";
            else if (withMSecs)
                tag = @"\.fff";

            if (ts.TotalDays >= 1)
                return ts.ToString(@"d\d\+hh\:mm\:ss" + tag);
            else
                return ts.ToString(@"hh\:mm\:ss" + tag);
        }

        // 日時を文字列に変換する
        public static string DateTimeToStr(DateTime dt, bool toLocalTime = false, CoreLanguage lang = CoreLanguage.Japanese, FullDateTimeStrFlags flags = FullDateTimeStrFlags.None)
        {
            if (toLocalTime)
            {
                dt = dt.ToLocalTime();
            }

            if (lang == CoreLanguage.Japanese)
            {
                string dateTag = "yyyy年M月d日";
                string timeTag = flags.Bit(FullDateTimeStrFlags.CommaTime) ? " HH:mm:ss" : " H時m分s秒";

                if (flags.Bit(FullDateTimeStrFlags.SlashDate))
                {
                    dateTag = "yyyy/MM/dd";
                }

                if (flags.Bit(FullDateTimeStrFlags.NoSeconds))
                {
                    timeTag = flags.Bit(FullDateTimeStrFlags.CommaTime) ? " HH:mm" : " H時m分";
                }

                return dt.ToString(dateTag) + "(" + DayOfWeekToStr(lang, (int)dt.DayOfWeek) + ")" + dt.ToString(timeTag);
            }
            else
            {
                string timeTag = ") H:mm:ss";

                if (flags.Bit(FullDateTimeStrFlags.NoSeconds))
                {
                    timeTag = ") H:mm";
                }

                return dt.ToString("yyyy-MM-dd(") + DayOfWeekToStr(lang, (int)dt.DayOfWeek) + dt.ToString(timeTag);
            }
        }
        public static string DateTimeToStrShort(DateTime dt, bool toLocalTime = false)
        {
            if (toLocalTime)
            {
                dt = dt.ToLocalTime();
            }

            return dt.ToString("yyyyMMdd_HHmmss");
        }
        public static string DateTimeToStrShortWithMilliSecs(DateTime dt, bool toLocalTime = false)
        {
            if (toLocalTime)
            {
                dt = dt.ToLocalTime();
            }

            long ticks = dt.Ticks % 10000000;
            if (ticks >= 9990000)
            {
                ticks = 9990000;
            }

            string msecStr = ((decimal)ticks / (decimal)10000000).ToString(".000");

            return dt.ToString("yyyyMMdd_HHmmss") + "." + msecStr.Split('.')[1];
        }

        public static int DateTimeToYymmddInt(DateTime dt, int zeroValue = 0, bool yearTwoDigits = false)
        {
            if (dt._IsZeroDateTime())
            {
                return zeroValue;
            }

            string ret = dt.ToString("yyyyMMdd");

            if (yearTwoDigits)
            {
                ret = ret.Substring(2);
            }

            return Str.StrToInt(ret);
        }

        public static string DateTimeToYymmddStr(DateTime dt, string zeroValue = "", bool yearTwoDigits = false)
        {
            zeroValue = zeroValue._NonNull();

            if (dt._IsZeroDateTime())
            {
                return zeroValue;
            }

            string ret = dt.ToString("yyyyMMdd");

            if (yearTwoDigits)
            {
                ret = ret.Substring(2);
            }

            return ret;
        }

        public static int DateTimeToHhmmssInt(DateTime dt, int zeroValue = 0)
        {
            if (dt._IsZeroDateTime())
            {
                return zeroValue;
            }

            string ret = dt.ToString("HHmmss");

            return Str.StrToInt(ret);
        }

        public static string DateTimeToHhmmssStr(DateTime dt, string zeroValue = "", bool millisecs = false)
        {
            zeroValue = zeroValue._NonNull();

            if (dt._IsZeroDateTime())
            {
                return zeroValue;
            }

            string ret = dt.ToString("HHmmss");

            if (millisecs)
            {
                long ticks = dt.Ticks % 10000000;
                if (ticks >= 9990000)
                {
                    ticks = 9990000;
                }

                string msecStr = ((decimal)ticks / (decimal)10000000).ToString(".000");

                ret += msecStr;
            }

            return ret;
        }

        public static long DateTimeToYymmddHHmmssLong(DateTime dt, long zeroValue = 0, bool yearTwoDigits = false)
        {
            if (dt._IsZeroDateTime())
            {
                return zeroValue;
            }

            string ret = dt.ToString("yyyyMMddHHmmss");

            if (yearTwoDigits)
            {
                ret = ret.Substring(2);
            }

            return Str.StrToLong(ret);
        }

        public static string GenerateRandomDigit(int numDigits)
        {
            StringBuilder sb = new StringBuilder();

            for (int i = 0; i < numDigits; i++)
            {
                char c = (char)('0' + Secure.RandSInt31() % 10);
                sb.Append(c);
            }

            return sb.ToString();
        }

        public static string DateTimeToDtstr(DateTime dt, bool withMSecs = false, DtStrOption option = DtStrOption.All, bool withNanoSecs = false, string zeroDateTimeStr = "")
        {
            long ticks = dt.Ticks % 10000000;
            if (ticks >= 9999999) ticks = 9999999;

            if (dt._IsZeroDateTime())
            {
                return zeroDateTimeStr;
            }

            string msecStr = "";
            if (withNanoSecs)
            {
                msecStr = dt.ToString("fffffff");
            }
            else if (withMSecs)
            {
                msecStr = dt.ToString("fff");
            }

            string ret = dt.ToString("yyyy/MM/dd HH:mm:ss") + ((withMSecs || withNanoSecs) ? "." + msecStr : "");

            if (option == DtStrOption.DateOnly)
            {
                ret = ret._ToToken(" ")[0];
            }
            else if (option == DtStrOption.TimeOnly)
            {
                ret = ret._ToToken(" ")[1];
            }

            return ret;
        }

        public static string DateTimeToDtstr(DateTimeOffset dt, bool withMSecs = false, DtStrOption option = DtStrOption.All, bool withNanoSecs = false, string zeroDateTimeStr = "")
        {
            long ticks = dt.Ticks % 10000000;
            if (ticks >= 9999999) ticks = 9999999;

            if (dt._IsZeroDateTime())
            {
                return zeroDateTimeStr;
            }

            string msecStr = "";
            if (withNanoSecs)
            {
                msecStr = dt.ToString("fffffff");
            }
            else if (withMSecs)
            {
                msecStr = dt.ToString("ffff").Substring(0, 3);
            }

            string ret = dt.ToString("yyyy/MM/dd HH:mm:ss") + ((withMSecs || withNanoSecs) ? "." + msecStr : "");

            if (option == DtStrOption.DateOnly)
            {
                ret = ret._ToToken(" ")[0];
            }
            else if (option == DtStrOption.TimeOnly)
            {
                ret = ret._ToToken(" ")[1];
            }

            ret += " " + dt.ToString("%K");

            return ret;
        }

        public static DateTimeOffset DtstrToDateTimeOffset(string str)
        {
            if (str._IsEmpty()) return ZeroDateTimeOffsetValue;

            string[] tokens = str._Split(StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries, " ");

            DateTime date = Str.StrToDate(tokens[0], false);
            DateTime time = Str.StrToTime(tokens[1], false);

            TimeSpan offset = default;
            string? offsetStr = tokens.ElementAtOrDefault(2);
            if (offsetStr._IsEmpty())
            {
            }
            else
            {
                if (offsetStr[0] == '+')
                {
                    offset = TimeSpan.Parse(offsetStr.Substring(1), CultureInfo.InvariantCulture);
                }
                else
                {
                    offset = -TimeSpan.Parse(offsetStr.Substring(1), CultureInfo.InvariantCulture);
                }
            }

            DateTime dt = date.Add(time.TimeOfDay);

            return new DateTimeOffset(dt._OverwriteKind(DateTimeKind.Unspecified), offset);
        }

        // "20190924-123456+0900" という形式のファイル名をパースする
        public static ResultOrError<DateTimeOffset> FileNameStrToDateTimeOffset(string str)
        {
            str = str._NonNullTrim();

            ReadOnlySpan<char> stra = str;

            if (stra.Length < 20) return false;

            if (stra.Length > 20)
                stra = stra.Slice(0, 20);

            if (stra[8] != '-') return false;

            bool positive;
            if (stra[15] == '+')
                positive = true;
            else if (stra[15] == '-')
                positive = false;
            else
                return false;

            if (int.TryParse(stra.Slice(0, 4), out int year) == false ||
                int.TryParse(stra.Slice(4, 2), out int month) == false ||
                int.TryParse(stra.Slice(6, 2), out int day) == false ||
                int.TryParse(stra.Slice(9, 2), out int hour) == false ||
                int.TryParse(stra.Slice(11, 2), out int minute) == false ||
                int.TryParse(stra.Slice(13, 2), out int second) == false ||
                int.TryParse(stra.Slice(16, 2), out int offsetHour) == false ||
                int.TryParse(stra.Slice(18, 2), out int offsetMinute) == false)
            {
                return false;
            }

            if (year < Consts.Numbers.MinYear || year > Consts.Numbers.MaxYear) return false;
            if (month < 1 || month > 12) return false;
            if (day < 1 || day > 31) return false;
            if (hour < 0 || hour >= 24) return false;
            if (minute < 0 || minute >= 60) return false;
            if (second < 0 || second >= 60) return false;
            if (offsetHour < 0 || offsetHour >= 24) return false;
            if (offsetMinute < 0 || offsetMinute >= 60) return false;

            TimeSpan offset = new TimeSpan(offsetHour, offsetMinute, 0);
            if (positive == false) offset = -offset;

            return new DateTimeOffset(year, month, day, hour, minute, second, 0, offset);
        }

        // "20190924-123456+0900" という形式のファイル名を出力する
        public static string DateTimeOffsetToFileNameStr(DateTimeOffset dt)
        {
            StringBuilder b = new StringBuilder();
            b.Append(dt.ToString("yyyyMMdd-HHmmss"));

            TimeSpan ts = dt.Offset;
            bool positive = true;

            if (ts.Ticks < 0)
            {
                ts = -ts;
                positive = false;
            }

            if (ts >= new TimeSpan(24, 0, 0)) throw new ArgumentOutOfRangeException(nameof(dt.Offset));
            if (ts.Seconds != 0 || ts.Milliseconds != 0) throw new ArgumentOutOfRangeException(nameof(dt.Offset));

            if (positive)
                b.Append("+");
            else
                b.Append("-");

            b.Append(ts.Hours.ToString("D2"));
            b.Append(ts.Minutes.ToString("D2"));

            return b.ToString();
        }

        // 2ch 方式のレスタグ生成
        public static string Make2chThreadTypeResHyperLink(string srcText, string baseThreadUrl)
        {
            baseThreadUrl = baseThreadUrl.TrimEnd('/');

            string[] lines = srcText._Split(StringSplitOptions.None, Str.HtmlCrlf);

            StringBuilder w = new StringBuilder();

            string resChars = "0123456789,-";

            string restag = ">>"._EncodeHtml();

            for (int j = 0; j < lines.Length; j++)
            {
                string line = lines[j];

                int startResTag = line.IndexOf(restag, StringComparison.OrdinalIgnoreCase);

                if (startResTag != -1)
                {
                    string beforeString = line.Substring(0, startResTag);
                    string tmp = line.Substring(startResTag + restag.Length);

                    StringBuilder resStringBuilder = new StringBuilder();
                    StringBuilder afterStringBuilder = new StringBuilder();
                    bool flag = false;

                    for (int i = 0; i < tmp.Length; i++)
                    {
                        char c = tmp[i];

                        if (flag == false)
                        {
                            if (resChars.IndexOf(c) != -1 || c == ' ' || c == '　')
                            {
                                resStringBuilder.Append(c);
                            }
                            else if (c == '&' && tmp.Substring(i).StartsWith(Str.HtmlSpacing, StringComparison.OrdinalIgnoreCase))
                            {
                                resStringBuilder.Append(Str.HtmlSpacing);
                                i += Str.HtmlSpacing.Length - 1;
                            }
                            else
                            {
                                afterStringBuilder.Append(c);
                                flag = true;
                            }
                        }
                        else
                        {
                            afterStringBuilder.Append(c);
                        }
                    }

                    string resString = resStringBuilder.ToString().Trim();
                    string resStringRemoveSpace = resString._ReplaceStr(Str.HtmlSpacing, "", true);
                    string afterString = afterStringBuilder.ToString();

                    string linkStart = $"<a href=\"{baseThreadUrl}/{resStringRemoveSpace}/\">";
                    string linkEnd = "</a>";

                    w.Append($"{linkStart}{restag}{resString}{linkEnd}{afterString}");
                }
                else
                {
                    w.Append($"{line}");
                }

                if (j != (lines.Length - 1))
                {
                    w.Append(Str.HtmlBr);
                }
            }

            return w.ToString();
        }

        // 2ch 方式のハッシュ文字列の生成
        public static string Easy2chTypeHashStr(string src, int len = 8)
        {
            len = Math.Max(len, 1);

            var rand = new SeedBasedRandomGenerator(src);

            StringBuilder b = new StringBuilder();

            for (int i = 0; i < len; i++)
            {
                char c = IntToChar(rand.GetSInt31());

                b.Append(c);
            }

            return b.ToString();

            char IntToChar(int i)
            {
                i = i % (10 + 26 * 2);

                if (i < 10)
                {
                    return (char)('0' + i);
                }
                else if (i < (10 + 26))
                {
                    return (char)('a' + i - 10);
                }
                else
                {
                    return (char)('A' + i - (10 + 26));
                }
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
        public static string Base64Encode(ReadOnlySpan<byte> data)
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
        public static string Base64Encode(ReadOnlyMemory<byte> data)
            => Base64Encode(data.Span);

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

        // Base 64 URL エンコード
        public static string Base64UrlEncode(byte[] data)
        {
            string tmp = Base64Encode(data);

            tmp = tmp.TrimEnd('=').Replace('+', '-').Replace('/', '_');

            return tmp;
        }

        // Base 64 URL デコード
        public static byte[] Base64UrlDecode(string str)
        {
            str = str._NonNull();

            string tmp = str.Replace('_', '/').Replace('-', '+');

            switch (str.Length % 4)
            {
                case 2: tmp += "=="; break;
                case 3: tmp += "="; break;
            }

            return Base64Decode(tmp);
        }

        // 文字列をバイトに変換
        public static byte[] StrToByte(string? str)
        {
            Str.NormalizeString(ref str, true, true, false, false);
            return Base64Decode(Safe64ToBase64(str));
        }

        // 文字列を Base 64 エンコードする
        public static string Encode64(string str)
        {
            return Convert.ToBase64String(Str.Utf8Encoding.GetBytes(str)).Replace("/", "(").Replace("+", ")");
        }

        // 文字列を Base 64 デコードする
        public static string Decode64(string str)
        {
            return Str.Utf8Encoding.GetString(Convert.FromBase64String(str.Replace(")", "+").Replace("(", "/")));
        }


        // メールアドレスをチェックする
        public static bool CheckMailAddress(string? str)
        {
            try
            {
                str = str._NonNullTrim();
                if (str.Length == 0)
                {
                    return false;
                }

                string[] tokens = str.Split('@');

                if (tokens.Length != 2)
                {
                    return false;
                }

                string a = tokens[0];
                string b = tokens[1];

                if (a.Length == 0 || b.Length == 0)
                {
                    return false;
                }

                if (b.IndexOf(".") == -1)
                {
                    return false;
                }

                return IsAscii(str);
            }
            catch
            {
                return false;
            }
        }

        // 文字列を大文字・小文字を区別せずに比較
        public static bool StrCmpi(string? s1, string? s2)
        {
            if (s1 == null && s2 == null) return true;
            if (s1 == null || s2 == null) return false;

            return string.Equals(s1, s2, StringComparison.OrdinalIgnoreCase);
        }

        // 文字列を大文字・小文字を区別して比較
        public static bool StrCmp(string? s1, string? s2)
        {
            if (s1 == null && s2 == null) return true;
            if (s1 == null || s2 == null) return false;

            return string.Equals(s1, s2, StringComparison.Ordinal);
        }

        // 簡易エンコードを実施
        public static string EncodeEasy(string? str)
        {
            try
            {
                str = str._NonNull();
                return Consts.Strings.EncodeEasyPrefix + str._GetBytes_UTF8()._GetHexString();
            }
            catch
            {
                return "";
            }
        }

        // 簡易デコードを実施
        public static string DecodeEasy(string? str)
        {
            try
            {
                str = str._NonNull();

                if (str._TryTrimStartWith(out string str2, StringComparison.OrdinalIgnoreCase, Consts.Strings.EncodeEasyPrefix) == false)
                {
                    return "";
                }

                return str2._GetHexBytes()._GetString_UTF8();
            }
            catch
            {
                return "";
            }
        }

        // バイト列を JavaScript 16 進数データ列に変換
        public static string ByteToJavaScriptHexString(ReadOnlySpan<byte> data)
        {
            StringBuilder ret = new StringBuilder();

            int i;
            for (i = 0; i < data.Length; i++)
            {
                byte b = data[i];

                string s = b.ToString("X");
                if (s.Length == 1)
                {
                    s = "0" + s;
                }

                ret.Append("0x" + s);

                if (i != (data.Length - 1))
                {
                    ret.Append(", ");
                }
            }

            return ret.ToString();
        }

        // バイト列を 16 進数文字列に変換
        public static string ByteToHex(ReadOnlySpan<byte> data, string? paddingStr = null)
        {
            checked
            {
                int padStrLen = (paddingStr == null ? 0 : paddingStr.Length);

                string tmp = Convert.ToHexString(data);
                if (padStrLen == 0) return tmp;

                int destLength = data.Length * 2;

                if (data.Length >= 2)
                {
                    destLength += padStrLen * (data.Length - 1);
                }

                Span<char> buf = new char[destLength];

                int pos1 = 0;
                int pos2 = 0;

                for (int i = 0; i < data.Length; i++)
                {
                    // 2 文字追記
                    buf[pos1++] = tmp[pos2++];
                    buf[pos1++] = tmp[pos2++];

                    // padding
                    if (i != (data.Length - 1))
                    {
                        for (int j = 0; j < padStrLen; j++)
                        {
                            buf[pos1++] = paddingStr![j];
                        }
                    }
                }

                return buf.ToString();
            }
        }

        // 16 進数文字列をバイト列に変換
        public static byte[] HexToByte(string? str)
        {
            try
            {
                if (str._IsNullOrZeroLen()) return new byte[0];

                List<byte> o = new List<byte>();
                string tmp = "";
                int i, len;

                str = str!.ToUpperInvariant().Trim();
                len = str.Length;

                for (i = 0; i < len; i++)
                {
                    char c = str[i];
                    if (('0' <= c && c <= '9') || ('A' <= c && c <= 'F'))
                    {
                        tmp += c;
                        if (tmp.Length == 2)
                        {
                            byte b = Convert.ToByte(tmp, 16);
                            o.Add(b);
                            tmp = "";
                        }
                    }
                    else if (c == ' ' || c == ',' || c == '-' || c == ';' || c == ':')
                    {
                        // 何もしない
                    }
                    else
                    {
                        break;
                    }
                }

                return o.ToArray();
            }
            catch
            {
                return new byte[0];
            }
        }

        // テキストから複数行を取り出す
        public static string[] GetLines(string str, bool removeEmpty = false, bool stripCommentsFromLine = false, IEnumerable<string>? commentStartStrList = null, bool singleLineAtLeast = false, bool trim = false, ICollection<string>? strippedStrList = null, bool commentMustBeWholeLine = false)
        {
            List<string> a = new List<string>();
            StringReader sr = new StringReader(str);
            while (true)
            {
                string? s = sr.ReadLine();
                if (s == null)
                {
                    break;
                }

                // BOM check (念のため)
                for (int i = 0; i < 2; i++)
                {
                    if (s.Length >= 1)
                    {
                        char c = s[0];
                        int cx = (int)c;
                        if (cx == 0xFEFF)
                        {
                            // BOM
                            s = s.Substring(1);
                        }
                    }
                }

                if (trim)
                {
                    s = s.Trim();
                }
                if (stripCommentsFromLine)
                {
                    Ref<string>? strippedStr = strippedStrList == null ? null : new Ref<string>("");

                    s = s._StripCommentFromLine(commentStartStrList, strippedStr, commentMustBeWholeLine);

                    if (strippedStrList != null && strippedStr != null && strippedStr.Value._IsFilled())
                    {
                        strippedStrList.Add(strippedStr.Value);
                    }
                }
                if (trim)
                {
                    s = s.Trim();
                }
                if (removeEmpty == false || s._IsFilled())
                {
                    a.Add(s);
                }
            }
            if (singleLineAtLeast)
            {
                if (a.Count == 0)
                {
                    a.Add("");
                }
            }
            return a.ToArray();
        }

        // 複数行をテキストに変換する
        public static string LinesToStr(IEnumerable<string> lines, string? newLineStr = null)
        {
            StringWriter sw = new StringWriter();
            if (newLineStr._IsNullOrZeroLen() == false)
            {
                sw.NewLine = newLineStr;
            }
            foreach (string s in lines)
            {
                sw.WriteLine(s);
            }
            return sw.ToString();
        }

        // 最初のトークンを取得
        public static string GetFirstToken(string str)
        {
            return GetFirstToken(str, new char[] { ' ', '\t', '　', });
        }
        public static string GetFirstToken(string str, params char[] sps)
        {
            try
            {
                string[] tokens = str.Split(sps, StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length >= 1)
                {
                    return tokens[0];
                }
            }
            catch
            {
            }
            return "";
        }

        // 最後のトークンを取得
        public static string GetLastToken(string str)
        {
            return GetLastToken(str, new char[] { ' ', '\t', '　', });
        }
        public static string GetLastToken(string str, params char[] sps)
        {
            try
            {
                string[] tokens = str.Split(sps, StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length >= 1)
                {
                    return tokens[tokens.Length - 1];
                }
            }
            catch
            {
            }
            return "";
        }
        // 空かどうか調べる
        [MethodImpl(Inline)]
        public static bool IsEmptyStr([NotNullWhen(false)] string? s)
        {
            return string.IsNullOrWhiteSpace(s);
        }
        [MethodImpl(Inline)]
        public static bool IsFilledStr([NotNullWhen(true)] string? str)
        {
            return !IsEmptyStr(str);
        }

        // 指定された文字が区切り文字に該当するかどうかチェックする
        public static bool IsSplitChar(char c, string splitStr = Consts.Strings.DefaultSplitStr)
        {
            splitStr = splitStr._NotEmptyOrDefault(Consts.Strings.DefaultSplitStr);

            return (splitStr.IndexOf(c, StringComparison.OrdinalIgnoreCase) != -1);
        }

        // QueryString をパースする
        public static QueryStringList ParseQueryString(string src, Encoding? encoding = null, char splitChar = '&', bool trimKeyAndValue = false)
        {
            return new QueryStringList(src, encoding, splitChar, trimKeyAndValue);
        }

        // 文字列から URL と QueryString を分離する
        public static void SplitUrlAndQueryString(string src, out string url, out string queryString)
        {
            src = src._NonNull();

            int i = src.IndexOf('?');
            if (i == -1)
            {
                url = src;
                queryString = "";
            }
            else
            {
                url = src.Substring(0, i);
                queryString = src.Substring(i + 1);
            }
        }

        // 文字列からキーと値を取得する (正確な分割)
        public static bool GetKeyAndValueExact(string str, out string key, out string value, string splitStrExact, StringComparison comparison = StringComparison.OrdinalIgnoreCase)
        {
            str = str._NonNull();
            splitStrExact = splitStrExact._NonNull();
            if (splitStrExact._IsNullOrZeroLen())
            {
                throw new CoresLibException(nameof(splitStrExact) + " is empty.");
            }

            int i = str.IndexOf(splitStrExact, comparison);
            if (i == -1)
            {
                key = "";
                value = "";
                return false;
            }

            key = str.Substring(0, i);
            value = str.Substring(i + splitStrExact.Length);
            return true;
        }

        // 文字列からキーと値を取得する
        public static bool GetKeyAndValue(string str, out string key, out string value, string splitStr = Consts.Strings.DefaultKeyAndValueSplitStr)
        {
            uint mode = 0;
            string keystr = "", valuestr = "";

            splitStr = splitStr._NotEmptyOrDefault(Consts.Strings.DefaultKeyAndValueSplitStr);

            foreach (char c in str)
            {
                switch (mode)
                {
                    case 0:
                        if (IsSplitChar(c, splitStr) == false)
                        {
                            mode = 1;
                            keystr += c;
                        }
                        break;

                    case 1:
                        if (IsSplitChar(c, splitStr) == false)
                        {
                            keystr += c;
                        }
                        else
                        {
                            mode = 2;
                        }
                        break;

                    case 2:
                        if (IsSplitChar(c, splitStr) == false)
                        {
                            mode = 3;
                            valuestr += c;
                        }
                        break;

                    case 3:
                        valuestr += c;
                        break;
                }
            }

            if (mode == 0)
            {
                value = "";
                key = "";
                return false;
            }
            else
            {
                value = valuestr;
                key = keystr;
                return true;
            }
        }

        // 文字列からキー (複数) と値を取得する
        public static bool GetKeysListAndValue(string str, int numKeys, out List<string> keys, out string value, string splitStr = Consts.Strings.DefaultKeyAndValueSplitStr)
        {
            uint mode = 0;
            string valuestr = "";
            List<string> keysList = new List<string>();

            string currentKeyStr = "";

            if (numKeys <= 0) throw new ArgumentOutOfRangeException(nameof(numKeys));

            splitStr = splitStr._NotEmptyOrDefault(Consts.Strings.DefaultKeyAndValueSplitStr);

            foreach (char c in str)
            {
                switch (mode)
                {
                    case 0:
                        if (IsSplitChar(c, splitStr) == false)
                        {
                            mode = 1;
                            currentKeyStr += c;
                        }
                        break;

                    case 1:
                        if (IsSplitChar(c, splitStr) == false)
                        {
                            currentKeyStr += c;
                        }
                        else
                        {
                            if (keysList.Count < (numKeys - 1))
                            {
                                keysList.Add(currentKeyStr);
                                currentKeyStr = "";
                                mode = 0;
                            }
                            else
                            {
                                mode = 2;
                            }
                        }
                        break;

                    case 2:
                        if (IsSplitChar(c, splitStr) == false)
                        {
                            mode = 3;
                            valuestr += c;
                        }
                        break;

                    case 3:
                        valuestr += c;
                        break;
                }
            }

            if (currentKeyStr.Length >= 1)
            {
                keysList.Add(currentKeyStr);
            }

            if (keysList.Count < numKeys)
            {
                value = "";
                keys = new List<string>();
                return false;
            }
            else
            {
                value = valuestr;
                keys = keysList;
                return true;
            }
        }
        // 文字列比較
        public static int StrCmpRetInt(string s1, string s2)
        {
            return string.Compare(s1, s2, StringComparison.Ordinal);
        }
        public static int StrCmpiRetInt(string s1, string s2)
        {
            return string.Compare(s1, s2, StringComparison.OrdinalIgnoreCase);
        }

        // 文字列がリストに含まれるかどうか比較
        public static bool IsStrInList(string str, params string[] args)
        {
            return IsStrInList(str, true, args);
        }
        public static bool IsStrInList(string str, bool ignoreCase, params string[] args)
        {
            foreach (string s in args)
            {
                if (ignoreCase)
                {
                    if (StrCmpi(str, s))
                    {
                        return true;
                    }
                }
                else
                {
                    if (StrCmp(str, s))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        // double かどうか取得
        public static bool IsDouble(string? str)
        {
            double v;
            Str.NormalizeString(ref str, true, true, false, false);
            str = str.Replace(",", "");
            return double.TryParse(str, out v);
        }

        // long かどうか取得
        public static bool IsLong(string? str)
        {
            long v;
            Str.RemoveSpaceChar(ref str);
            Str.NormalizeString(ref str, true, true, false, false);
            str = str.Replace(",", "");
            return long.TryParse(str, out v);
        }

        // int かどうか取得
        public static bool IsInt(string? str)
        {
            int v;
            Str.RemoveSpaceChar(ref str);
            Str.NormalizeString(ref str, true, true, false, false);
            str = str.Replace(",", "");
            return int.TryParse(str, out v);
        }

        // 数値かどうか取得
        public static bool IsNumber(string? str) // 挙動変更するな！！
        {
            str = str._NonNullTrim();
            Str.RemoveSpaceChar(ref str);
            Str.NormalizeString(ref str, true, true, false, false);
            str = str.Replace(",", "");

            if (str._IsEmpty()) return false;

            foreach (char c in str)
            {
                if (IsNumber(c) == false)
                {
                    return false;
                }
            }

            return true;
        }
        public static bool IsNumber(char c) // 挙動変更するな！！
        {
            if (c >= '0' && c <= '9')
            {
            }
            else if (c == '-')
            {
            }
            else
            {
                return false;
            }

            return true;
        }

        // 文字列の両端が指定された文字の場合は削除
        public static string TrimBothSideChar(string? src, char c1, char c2)
        {
            src = src._NonNull();

            int len = src.Length;

            if (len >= 2)
            {
                if (src[0] == c1 && src[len - 1] == c2)
                {
                    src = src.Substring(1, len - 2);
                }
            }

            return src;
        }

        // 指定した文字列が含まれているかどうかチェック
        public static bool InStr(string str, string keyword, bool caseSensitive = false)
        {
            if (str.IndexOf(keyword, (caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase)) == -1)
            {
                return false;
            }

            return true;
        }

        public static string MakeStrArray(string str, int count, string sepstr = "")
        {
            sepstr = sepstr._NonNull();
            StringWriter w = new StringWriter();

            for (int i = 0; i < count; i++)
            {
                w.Write(str);

                if (i != (count - 1))
                {
                    w.Write(sepstr);
                }
            }

            return w.ToString();
        }

        // 指定した文字の列を生成する
        public static string MakeCharArray(char c, int len)
        {
            if (len < 0) throw new ArgumentOutOfRangeException(nameof(len));
            if (len == 0) return "";
            return new string(c, len);
        }

        // 最後の改行を削除する
        public static string RemoveLastCrlf(string? str)
        {
            if (str == null) return "";
            if (str.Length >= 1 && str[str.Length - 1] == 10)
                str = str.Substring(0, str.Length - 1);
            if (str.Length >= 1 && str[str.Length - 1] == 13)
                str = str.Substring(0, str.Length - 1);
            return str;
        }

        public static string NormalizeComfortableUrl(string srcUrl)
        {
            if (srcUrl == null) return "";

            try
            {
                string[] amazonStoreHostNameList = new string[]
                    {
                    "www.amazon.co.jp",
                    "www.amazon.com",
                    "www.amazon.co.uk",
                    };

                string[] defaultFileNamesList = new string[]
                    {
                    "index.html",
                    "index.htm",
                    "index.cgi",
                    "default.html",
                    "default.htm",
                    "default.asp",
                    "default.aspx",
                    "index.php",
                    "index.pl",
                    "index.py",
                    "index.rb",
                    "index.xhtml",
                    "index.shtml",
                    "index.phtml",
                    "index.jsp",
                    "index.wml",
                    "welcome.html",
                    "index.nginx-debian.html",
                    };

                var originalLines = GetLinesWithExactCrlfNewLines(srcUrl);

                List<KeyValuePair<string, string>> lineDataDestination = new List<KeyValuePair<string, string>>();

                foreach (var lineDataOriginal in originalLines)
                {
                    var lineData = lineDataOriginal;
                    string line = lineData.Key;

                    var trimmedLine = AdvancedTrim(line);

                    string line2 = trimmedLine.Item1;

                    bool isUrlLineOrEmptyLine = false;

                    if (line2.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                        line2.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    {
                        if (TryParseUrl(line2, out Uri uri, out QueryStringList qs))
                        {
                            string absolutePath = uri.AbsolutePath;

                            // ディレクトリ名が省略されている場合、これを追加
                            if (string.IsNullOrEmpty(uri.Query)) // Query string がある場合は、ここはいじらない
                            {
                                if (uri.AbsolutePath.EndsWith("/") == false)
                                {
                                    string? tmp1 = uri.Segments.LastOrDefault();
                                    if (tmp1 == null) tmp1 = "";
                                    if (tmp1.Length >= 3 && tmp1.Substring(1, tmp1.Length - 2).Contains("."))
                                    {
                                        // /a/b/c/test.pdf のように、最後のパスに拡張子が含まれている場合: 何もしない
                                    }
                                    else
                                    {
                                        // それ以外の場合: "/" を追加
                                        absolutePath = uri.AbsolutePath + "/";
                                    }
                                }
                            }

                            // 末尾が index.html などの場合、これを削除
                            foreach (var defFileName in defaultFileNamesList)
                            {
                                if (absolutePath.EndsWith(defFileName, StringComparison.OrdinalIgnoreCase))
                                {
                                    absolutePath = absolutePath.Substring(absolutePath.Length - defFileName.Length);
                                }
                            }

                            if (string.IsNullOrEmpty(absolutePath))
                            {
                                absolutePath = "/";
                            }

                            string query = "";

                            if (string.IsNullOrEmpty(uri.Query) == false && uri.Query != "?")
                            {
                                query = uri.Query;
                            }

                            string fragment = "";

                            if (string.IsNullOrEmpty(uri.Fragment) == false && uri.Fragment != "#")
                            {
                                fragment = uri.Fragment;
                            }

                            foreach (var amazonHostNameCandidate in amazonStoreHostNameList)
                            {
                                if (amazonHostNameCandidate.Equals(uri.Host, StringComparison.OrdinalIgnoreCase))
                                {
                                    // Amazon 商品 URL を簡略化
                                    string[] tokens = uri.AbsolutePath.Split(new char[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                                    if (tokens.Length >= 3 && tokens[1].Equals("dp", StringComparison.OrdinalIgnoreCase) && tokens[2].Length == 10)
                                    {
                                        absolutePath = "/dp/" + tokens[2] + "/";
                                        query = "";
                                        fragment = "";
                                    }
                                    else if (tokens.Length >= 3 && tokens[0].Equals("gp", StringComparison.OrdinalIgnoreCase) && tokens[1].Equals("product", StringComparison.OrdinalIgnoreCase) && tokens[2].Length == 10)
                                    {
                                        absolutePath = "/dp/" + tokens[2] + "/";
                                        query = "";
                                        fragment = "";
                                    }
                                    break;
                                }
                            }

                            if (uri.Host.Equals("www.ebay.com", StringComparison.OrdinalIgnoreCase))
                            {
                                // ebay 商品 URL を簡略化
                                string[] tokens = uri.AbsolutePath.Split(new char[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                                if (tokens.Length >= 2 && tokens[0].Equals("itm", StringComparison.OrdinalIgnoreCase) && tokens[1].Length >= 10)
                                {
                                    absolutePath = "/itm/" + tokens[1] + "/";
                                    query = "";
                                    fragment = "";
                                }
                            }

                            string dstText = uri.Scheme + "://" + uri.Host + (uri.IsDefaultPort ? "" : ":" + uri.Port) + absolutePath + query + fragment;
                            trimmedLine = new Tuple<string, string, string>(dstText, trimmedLine.Item2, trimmedLine.Item3);

                            isUrlLineOrEmptyLine = true;
                        }
                    }

                    if (string.IsNullOrEmpty(line2))
                    {
                        isUrlLineOrEmptyLine = true;
                    }

                    if (isUrlLineOrEmptyLine == false)
                    {
                        // URL でも空白行でもない行が出現したら、処理を一切しない
                        return srcUrl;
                    }

                    lineData = new KeyValuePair<string, string>(trimmedLine.Item2 + trimmedLine.Item1 + trimmedLine.Item3, lineData.Value);

                    lineDataDestination.Add(lineData);
                }

                StringBuilder b = new StringBuilder();
                foreach (var lineData in lineDataDestination)
                {
                    b.Append(lineData.Key);
                    b.Append(lineData.Value);
                }

                return b.ToString();
            }
            catch
            {
                return srcUrl;
            }
        }

        public static Tuple<string, string, string> AdvancedTrim(
            string? srcText,
            bool trimStart = true,
            bool trimEnd = true,
            char[]? splitCharList = null)
        {
            if (srcText == null) srcText = "";
            if (splitCharList == null)
            {
                splitCharList = new char[] { ' ', '　', '\t', '\n', '\r', };
            }

            int len = srcText.Length;

            // トリムの開始位置と終了位置を決めるための変数を用意
            int startIndex = 0;
            int endIndex = len - 1;

            // 先頭のトリム
            if (trimStart)
            {
                while (startIndex < len)
                {
                    if (!splitCharList.Contains(srcText[startIndex]))
                    {
                        break;
                    }
                    startIndex++;
                }
            }

            // 末尾のトリム
            if (trimEnd)
            {
                while (endIndex >= startIndex)
                {
                    if (!splitCharList.Contains(srcText[endIndex]))
                    {
                        break;
                    }
                    endIndex--;
                }
            }

            // 先頭・末尾それぞれトリムされた文字列を取得
            // (trimStart = true のときのみ先頭が削られるので、その削られた部分を removedStart に入れる)
            string removedStart = (trimStart && startIndex > 0)
                ? srcText.Substring(0, startIndex)
                : "";

            // (trimEnd = true のときのみ末尾が削られるので、その削られた部分を removedEnd に入れる)
            string removedEnd = (trimEnd && endIndex < len - 1)
                ? srcText.Substring(endIndex + 1)
                : "";

            // トリム後の本体文字列
            string trimmedString;
            if (startIndex > endIndex)
            {
                // すべてがトリム対象になってしまった場合は空文字を返す
                trimmedString = "";
            }
            else
            {
                trimmedString = srcText.Substring(startIndex, endIndex - startIndex + 1);
            }

            return new Tuple<string, string, string>(trimmedString, removedStart, removedEnd);
        }

        public static List<KeyValuePair<string, string>> GetLinesWithExactCrlfNewLines(string srcText)
        {
            if (srcText == null) srcText = "";

            List<KeyValuePair<string, string>> ret = new List<KeyValuePair<string, string>>();

            int len = srcText.Length;

            StringBuilder b = new StringBuilder();

            for (int i = 0; i < len; i++)
            {
                char c = srcText[i];

                if (c == '\r')
                {
                    char c2 = (char)0;
                    if (i < (len - 1))
                    {
                        c2 = srcText[i + 1];
                    }
                    if (c2 == '\n')
                    {
                        ret.Add(new KeyValuePair<string, string>(b.ToString(), "\r\n"));
                        i++;
                    }
                    else
                    {
                        ret.Add(new KeyValuePair<string, string>(b.ToString(), "\r"));
                    }
                    b.Clear();
                }
                else if (c == '\n')
                {
                    ret.Add(new KeyValuePair<string, string>(b.ToString(), "\n"));
                    b.Clear();
                }
                else
                {
                    b.Append(c);
                }
            }

            if (b.Length >= 1)
            {
                ret.Add(new KeyValuePair<string, string>(b.ToString(), ""));
            }

            return ret;
        }

        // 改行コードを正規化する
        [return: NotNullIfNotNull("str")]
        public static string? NormalizeCrlf(string str, CrlfStyle style = CrlfStyle.LocalPlatform, bool ensureLastLineCrlf = false)
        {
            if (str == null) return null;
            if (style == CrlfStyle.NoChange) return str;
            return NormalizeCrlf(str, Str.GetNewLineBytes(style), ensureLastLineCrlf);
        }
        [return: NotNullIfNotNull("str")]
        public static string NormalizeCrlf(string str, ReadOnlyMemory<byte> crlfData, bool ensureLastLineCrlf = false)
        {
            if (str == null) return "";
            byte[] srcData = Str.Utf8Encoding.GetBytes(str);
            Memory<byte> destData = NormalizeCrlf(srcData, crlfData, ensureLastLineCrlf);
            return Str.Utf8Encoding.GetString(destData.Span);
        }
        public static Memory<byte> NormalizeCrlf(ReadOnlySpan<byte> srcData, CrlfStyle style = CrlfStyle.LocalPlatform, bool ensureLastLineCrlf = false)
        {
            if (style == CrlfStyle.NoChange) return srcData.ToArray();
            return NormalizeCrlf(srcData, Str.GetNewLineBytes(style), ensureLastLineCrlf);
        }
        public static Memory<byte> NormalizeCrlf(ReadOnlySpan<byte> srcData, ReadOnlyMemory<byte> crlfData, bool ensureLastLineCrlf = false)
        {
            FastMemoryBuffer<byte> ret = new FastMemoryBuffer<byte>();
            FastMemoryBuffer<byte> tmp = new FastMemoryBuffer<byte>();

            int i;
            for (i = 0; i < srcData.Length; i++)
            {
                bool isNewLine = false;
                if (srcData[i] == 13)
                {
                    if (i < (srcData.Length - 1) && srcData[i + 1] == 10)
                    {
                        i++;
                    }
                    isNewLine = true;
                }
                else if (srcData[i] == 10)
                {
                    isNewLine = true;
                }

                if (isNewLine)
                {
                    ret.Write(tmp.Span);
                    ret.Write(crlfData);

                    tmp.Clear();
                }
                else
                {
                    tmp.WriteUInt8(srcData[i]);
                }
            }

            if (tmp.Span.Length >= 1)
            {
                ret.Write(tmp.Span);

                if (ensureLastLineCrlf)
                {
                    ret.Write(crlfData);
                }
            }

            return ret.Memory;
        }

        public static string RemoveDoubleNewLines(string src, CrlfStyle style = CrlfStyle.LocalPlatform, bool removeEmpty = false, bool stripCommentsFromLine = false, IEnumerable<string>? commentStartStrList = null, bool singleLineAtLeast = false, bool trim = false, ICollection<string>? strippedStrList = null, bool commentMustBeWholeLine = false)
        {
            string[] lines = src._GetLines(removeEmpty, stripCommentsFromLine, commentStartStrList, singleLineAtLeast, trim, strippedStrList, commentMustBeWholeLine);

            List<string> tmp = new List<string>();

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];

                if (line._IsFilled())
                {
                    tmp.Add(line);
                }
                else
                {
                    if (tmp.Any())
                    {
                        if (tmp.Last()._IsFilled())
                        {
                            tmp.Add("");
                        }
                    }
                }
            }

            StringWriter w = new StringWriter();
            w.NewLine = Str.GetNewLineStr(style);
            foreach (var line in tmp)
            {
                w.WriteLine(line);
            }

            return w.ToString();
        }

        // トークンリストを重複の無いものに変換する
        public static string[] UniqueToken(IEnumerable<string> t)
        {
            Dictionary<string, object> o = new Dictionary<string, object>();
            List<string> ret = new List<string>();

            foreach (string s in t)
            {
                string key = s.ToUpperInvariant();

                if (o.ContainsKey(key) == false)
                {
                    o.Add(key, new Object());

                    ret.Add(s);
                }
            }

            return ret.ToArray();
        }

        // 数字を文字列に変換して 3 桁ずつカンマで区切る
        public static string ToStr3(long v)
            => ToString3(v);
        public static string ToString3(long v)
        {
            bool neg = false;

            if (v < 0)
            {
                neg = true;
                v = v * (long)-1;
            }

            string tmp, tmp2;
            int i;

            tmp = Str.LongToStr(v);

            tmp2 = "";
            for (i = tmp.Length - 1; i >= 0; i--)
            {
                tmp2 += tmp[i];
            }

            int len = tmp.Length;

            tmp = "";
            for (i = 0; i < len; i++)
            {
                if (i != 0 && (i % 3) == 0)
                {
                    tmp += ",";
                }

                tmp += tmp2[i];
            }

            char[] array = tmp.ToCharArray();
            Array.Reverse(array);

            string str = new string(array);

            if (neg)
            {
                str = "-" + str;
            }

            return str;
        }
        public static string ToStr3(BigNumber v)
             => ToString3(v);
        public static string ToString3(BigNumber v)
        {
            bool neg = false;

            if (v < 0)
            {
                neg = true;
                v = v * (long)-1;
            }

            string tmp, tmp2;
            int i;

            tmp = v.ToString();

            tmp2 = "";
            for (i = tmp.Length - 1; i >= 0; i--)
            {
                tmp2 += tmp[i];
            }

            int len = tmp.Length;

            tmp = "";
            for (i = 0; i < len; i++)
            {
                if (i != 0 && (i % 3) == 0)
                {
                    tmp += ",";
                }

                tmp += tmp2[i];
            }

            char[] array = tmp.ToCharArray();
            Array.Reverse(array);

            string str = new string(array);

            if (neg)
            {
                str = "-" + str;
            }

            return str;
        }

        // コマンドラインをビルドする
        public static string BuildCmdLine(IEnumerable<string> args)
        {
            List<string> o = new List<string>();

            foreach (string arg in args)
            {
                if (arg != null)
                {
                    // 1 つの " を 2 つの "" に置換する
                    string tmp = arg._ReplaceStr("\"", "\"\"");

                    if (tmp._InStr(" "))
                    {
                        // 文字列にスペースが含まれる場合は前後を "" で囲む
                        tmp = " " + tmp + " ";
                    }

                    o.Add(tmp);
                }
            }

            return o._Combine(" ");
        }

        // コマンドラインをパースする
        public static string[] ParseCmdLine(string str)
        {
            List<string> o;
            int i, len, mode;
            string tmp;
            bool ignoreSpace = false;
            bool ignoreSpace2 = false;

            o = new List<string>();
            mode = 0;
            len = str.Length;

            tmp = "";

            for (i = 0; i < len; i++)
            {
                char c = str[i];

                switch (mode)
                {
                    case 0:
                        // 次のトークンを発見するモード
                        if (c == ' ' || c == '\t')
                        {
                            // 次の文字へ進める
                        }
                        else
                        {
                            // トークンの開始
                            if (c == '\"')
                            {
                                if ((i != (len - 1)) && str[i + 1] == '\"')
                                {
                                    // 2 重の " は 1 個の " 文字として見なす
                                    tmp += '\"';
                                    i++;
                                }
                                else
                                {
                                    // 1 個の " はスペース無視フラグを有効にする
                                    ignoreSpace = true;
                                }
                            }
                            else if (c == '\'')
                            {
                                if ((i != (len - 1)) && str[i + 1] == '\'')
                                {
                                    // 2 重の ' は 1 個の ' 文字として見なす
                                    tmp += '\'';
                                    i++;
                                }
                                else
                                {
                                    // 1 個の ' はスペース無視フラグを有効にする
                                    ignoreSpace2 = true;
                                }
                            }
                            else
                            {
                                tmp += c;
                            }

                            mode = 1;
                        }
                        break;

                    case 1:
                        if (ignoreSpace == false && ignoreSpace2 == false && (c == ' ' || c == '\t'))
                        {
                            // トークンの終了
                            o.Add(tmp);
                            tmp = "";
                            mode = 0;
                        }
                        else
                        {
                            if (c == '\"')
                            {
                                if ((i != (len - 1)) && str[i + 1] == '\"')
                                {
                                    // 2 重の " は 1 個の " 文字として見なす
                                    tmp += '\"';
                                    i++;
                                }
                                else
                                {
                                    if (ignoreSpace == false)
                                    {
                                        // 1 個の " はスペース無視フラグを有効にする
                                        ignoreSpace = true;
                                    }
                                    else
                                    {
                                        // スペース無視フラグを無効にする
                                        ignoreSpace = false;
                                    }
                                }
                            }
                            else if (c == '\'')
                            {
                                if ((i != (len - 1)) && str[i + 1] == '\'')
                                {
                                    // 2 重の ' は 1 個の ' 文字として見なす
                                    tmp += '\'';
                                    i++;
                                }
                                else
                                {
                                    if (ignoreSpace2 == false)
                                    {
                                        // 1 個の ' はスペース無視フラグを有効にする
                                        ignoreSpace2 = true;
                                    }
                                    else
                                    {
                                        // スペース無視フラグを無効にする
                                        ignoreSpace2 = false;
                                    }
                                }
                            }
                            else
                            {
                                tmp += c;
                            }
                        }
                        break;
                }
            }

            if (tmp.Length != 0)
            {
                o.Add(tmp);
                tmp = "";
            }

            return o.ToArray();
        }

        public static string GetFirstFilledLineFromLines(string src)
        {
            string[] lines = src._GetLines(true);
            return (lines.FirstOrDefault())._NonNullTrim();
        }

        public static string OneLine(string? s, string splitter = " / ")
        {
            if (s._IsNullOrZeroLen()) return "";

            StringWriter w = new StringWriter();
            string[] lines = Str.GetLines(s);
            int num = 0;
            foreach (string line in lines)
            {
                string ss = line.Trim();

                if (Str.IsEmptyStr(ss) == false)
                {
                    if (num != 0)
                    {
                        if (splitter._IsNullOrZeroLen() == false)
                        {
                            w.Write(splitter);
                        }
                    }
                    w.Write(ss);
                    num++;
                }
            }
            return w.ToString();
        }

        public static string ObjectToXMLSimple_PublicLegacy(object o)
        {
            return ObjectToXMLSimple_PublicLegacy(o, o.GetType());
        }
        public static string ObjectToXMLSimple_PublicLegacy(object o, Type t)
        {
            XmlSerializer xs = new XmlSerializer(t);

            MemoryStream ms = new MemoryStream();
            xs.Serialize(ms, o);

            return Str.Utf8Encoding.GetString(ms.ToArray());
        }

        public static object? XMLToObjectSimple_PublicLegacy(string str, Type t)
        {
            XmlSerializer xs = new XmlSerializer(t);

            MemoryStream ms = new MemoryStream();
            byte[] data = Str.Utf8Encoding.GetBytes(str);
            ms.Write(data, 0, data.Length);
            ms.Position = 0;

            return xs.Deserialize(ms);
        }

        public static string ObjectToXmlStr(object obj, DataContractSerializerSettings? settings = null) => Util.ObjectToXml(obj, settings)._GetString_UTF8();
        public static object? XmlStrToObject(string src, Type type, DataContractSerializerSettings? settings = null) => Util.XmlToObject(src._GetBytes_UTF8(), type, settings);
        public static T? XmlStrToObject<T>(string src, DataContractSerializerSettings? settings = null) => Util.XmlToObject<T>(src._GetBytes_UTF8(), settings);

        public static string ObjectToRuntimeJsonStr(object obj, DataContractJsonSerializerSettings? settings = null) => Util.ObjectToRuntimeJson(obj, settings)._GetString_UTF8();
        public static object? RuntimeJsonStrToObject(string src, Type type, DataContractJsonSerializerSettings? settings = null) => Util.RuntimeJsonToObject(src._GetBytes_UTF8(), type, settings);
        public static T? RuntimeJsonStrToObject<T>(string src, DataContractJsonSerializerSettings? settings = null) => Util.RuntimeJsonToObject<T>(src._GetBytes_UTF8(), settings);

        public static string BuildHttpUrl(string protocol, string host, int port, string localPath)
        {
            protocol = Util.NormalizeHttpProtocolString(protocol);
            int defaultPort = Util.GetHttpProtocolDefaultPort(protocol);

            localPath = localPath._NonNull();

            if (host._IsEmpty()) throw new ArgumentNullException(nameof(host));

            if (port == 0) port = defaultPort;

            StringBuilder sb = new StringBuilder();
            sb.Append(protocol);
            sb.Append("://");
            sb.Append(host);

            if (defaultPort != port)
            {
                sb.Append(":");
                sb.Append(port.ToString());
            }

            sb.Append("/");

            if (localPath.StartsWith("/")) localPath = localPath.Substring(1);

            sb.Append(localPath);

            return sb.ToString();
        }

        public static void ParseUrl(string urlString, out Uri uri, out QueryStringList queryString, Encoding? encoding = null)
        {
            if (encoding == null) encoding = Str.Utf8Encoding;
            if (urlString._IsEmpty()) throw new ApplicationException("url_string is empty.");
            if (urlString.StartsWith("/")) urlString = "http://null" + urlString;
            uri = new Uri(urlString);
            queryString = uri.Query._ParseQueryString(encoding);
        }

        public static bool TryParseUrl(string urlString, out Uri uri, out QueryStringList queryString, Encoding? encoding = null)
        {
            if (encoding == null) encoding = Str.Utf8Encoding;
            if (urlString._IsEmpty()) throw new ApplicationException("url_string is empty.");
            if (urlString.StartsWith("/")) urlString = "http://null" + urlString;
            if (Uri.TryCreate(urlString, UriKind.Absolute, out uri!))
            {
                queryString = uri.Query._ParseQueryString(encoding);
                return true;
            }
            else
            {
                uri = null!;
                queryString = null!;
                return false;
            }
        }

        public static List<string> ParseUrlQueryKeywords(string urlString)
        {
            return null!;
        }

        // abc.def.example.org -> abc.def を抽出
        public static string GetSubdomainLabelFromParentAndSubFqdnNormalized(string parentFqdnNormalized, string subFqdnNormalized)
        {
            if (parentFqdnNormalized == subFqdnNormalized)
            {
                return "";
            }

            if (subFqdnNormalized.EndsWith(subFqdnNormalized))
            {
                string tmp = subFqdnNormalized.Substring(0, subFqdnNormalized.Length - parentFqdnNormalized.Length);
                if (tmp.EndsWith(".") == false)
                {
                    throw new Exception($"Subdomain string '{subFqdnNormalized}' is not a subdomain of the parent domain '{parentFqdnNormalized}'");
                }
                return tmp.Substring(0, tmp.Length - 1);
            }

            throw new Exception($"Subdomain string '{subFqdnNormalized}' is not a subdomain of the parent domain '{parentFqdnNormalized}'");
        }
        public static string GetSubdomainLabelFromParentAndSubFqdn(string parentFqdn, string subFqdn)
        {
            return GetSubdomainLabelFromParentAndSubFqdnNormalized(parentFqdn._NormalizeFqdn(), subFqdn._NormalizeFqdn());
        }


        public static bool TryParseFirstWildcardFqdnSandwitched(string fqdn, out (string beforeOfFirst, string afterOfFirst, string suffix) ret)
        {
            ret = new("", "", "");

            string[] tokens = fqdn._NonNullTrim().ToLowerInvariant().Split(".", StringSplitOptions.RemoveEmptyEntries);

            if (tokens.Length <= 0) return false;

            string firstToken = tokens[0];

            // 単一の "*" を見つける
            int i = firstToken.IndexOf("*");
            if (i == -1)
            {
                return false;
            }

            int j = firstToken.IndexOf("*", i + 1);
            if (j != -1)
            {
                return false;
            }

            string beforeOfFirst = firstToken.Substring(0, i);
            string afterOfFirst = firstToken.Substring(i + 1);

            StringBuilder sb = new StringBuilder(fqdn.Length);
            if (tokens.Length >= 2)
            {
                for (int k = 1; k < tokens.Length; k++)
                {
                    sb.Append(".");
                    sb.Append(tokens[k]);
                }
            }

            string suffix = sb.ToString();

            ret = new(beforeOfFirst, afterOfFirst, suffix);

            return true;
        }

        public static string CombineFqdn(params string[] labelsOrFqdns)
        {
            List<string> tokens = new List<string>();
            int estimatedLength = 0;
            foreach (var a in labelsOrFqdns)
            {
                string b = a._NonNullTrim().ToLowerInvariant();
                var t = b.Split(".", StringSplitOptions.RemoveEmptyEntries);
                tokens.AddRange(t);

                estimatedLength += a.Length + 2;
            }
            return tokens._Combine(".", estimatedLength: estimatedLength);
        }

        public static bool IsSubdomainOf(string subDomain, string parentDomain, out string normalizedHostLabel)
        {
            return IsSubdomainOf(EnsureSpecial.Yes, subDomain._NormalizeFqdn(), parentDomain._NormalizeFqdn(), out normalizedHostLabel);
        }

        public static bool IsSubdomainOf(EnsureSpecial normalized, string normalizedSubDomain, string normalizedParentDomain, out string normalizedHostLabel)
        {
            if (normalizedParentDomain._IsEmpty() || normalizedParentDomain == ".")
            {
                normalizedHostLabel = normalizedSubDomain;
                return true;
            }
            if (normalizedSubDomain.EndsWith("." + normalizedParentDomain, StringComparison.Ordinal))
            {
                normalizedHostLabel = normalizedSubDomain.Substring(0, normalizedSubDomain.Length - (normalizedParentDomain.Length + 1));
                return true;
            }

            normalizedHostLabel = "";
            return false;
        }

        public static bool IsEqualToOrSubdomainOf(string subDomain, string parentDomain, out string normalizedHostLabel)
        {
            return IsEqualToOrSubdomainOf(EnsureSpecial.Yes, subDomain._NormalizeFqdn(), parentDomain._NormalizeFqdn(), out normalizedHostLabel);
        }

        public static bool IsEqualToOrSubdomainOf(EnsureSpecial normalized, string normalizedSubDomain, string normalizedParentDomain, out string normalizedHostLabel)
        {
            if (normalizedSubDomain._IsSamei(normalizedParentDomain))
            {
                normalizedHostLabel = "";
                return true;
            }

            return IsSubdomainOf(EnsureSpecial.Yes, normalizedSubDomain, normalizedParentDomain, out normalizedHostLabel);
        }

        public static string NormalizeFqdn(string fqdn)
        {
            fqdn = fqdn._NonNullTrim().ToLowerInvariant();
            return fqdn.Split(".", StringSplitOptions.RemoveEmptyEntries)._Combine(".", estimatedLength: fqdn.Length);
        }

        public static bool CheckFqdn(string fqdn, bool allowWildcard = false, bool wildcardFirstTokenMustBeSimple = false)
        {
            try
            {
                fqdn = fqdn._NonNull().ToLowerInvariant();
                if (fqdn.EndsWith(".")) fqdn = fqdn.Substring(0, fqdn.Length - 1);

                if (fqdn.Length > 255) return false;
                string[] tokens = fqdn.Split(".", StringSplitOptions.None);

                if (tokens.Length <= 0) return false;

                for (int i = 0; i < tokens.Length; i++)
                {
                    string token = tokens[i];
                    if (token.Length > 63) return false;
                    foreach (char c in token)
                    {
                        if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || (c == '-') || (c == '_')) { }
                        else if (allowWildcard && c == '*' && i == 0) { }
                        else
                        {
                            return false;
                        }
                    }
                    if (i == 0 && wildcardFirstTokenMustBeSimple && token.Any(x => x == '*'))
                    {
                        // wildcardFirstTokenMustBeSimple が true の場合は、1 トークン目は '*' でなければならない
                        if (token != "*")
                        {
                            return false;
                        }
                    }
                }

                if (IPAddress.TryParse(fqdn, out _))
                {
                    return false;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        public static string GetSimpleHostnameFromFqdn(string fqdn)
        {
            fqdn = fqdn._NonNullTrim();
            if (fqdn._IsEmpty()) return "";
            return fqdn.Split(".", StringSplitOptions.RemoveEmptyEntries)[0];
        }

        public static bool TryTrimStartWith(string srcStr, out string outStr, StringComparison comparison, params string[] keys)
        {
            outStr = srcStr;
            if (keys.Length == 0 || keys == null) return false;

            foreach (string key in keys)
            {
                if (key._IsEmpty() == false)
                {
                    if (srcStr.StartsWith(key, comparison))
                    {
                        outStr = srcStr.Substring(key.Length);
                        return true;
                    }
                }
            }

            return false;
        }

        public static long CalcKeywordMatchPoint(string targetStr, string keyword, StringComparison comparison = StringComparison.OrdinalIgnoreCase)
        {
            targetStr = targetStr._NonNullTrim();
            keyword = keyword._NonNullTrim();

            if (targetStr._IsSame(keyword, comparison))
            {
                return Consts.Numbers.MaxMatchPoint;
            }

            if (keyword._IsEmpty())
            {
                return 0;
            }

            int originalLen = targetStr.Length;

            if (originalLen >= 1)
            {
                if (targetStr.IndexOf(keyword, comparison) != -1)
                {
                    int replacedLen = targetStr.Replace(keyword, "", comparison).Length;
                    int matchLen = originalLen - replacedLen;

                    matchLen = Math.Min(matchLen, originalLen);

                    long v = (long)matchLen * Consts.Numbers.MaxMatchPoint / (long)originalLen;

                    return v;
                }
                else if (keyword.IndexOf(targetStr, comparison) != -1)
                {
                    int replacedLen = keyword.Replace(targetStr, "", comparison).Length;
                    int matchLen = keyword.Length - replacedLen;

                    matchLen = Math.Min(matchLen, keyword.Length);

                    long v = (long)matchLen * Consts.Numbers.MaxMatchPoint2 / (long)keyword.Length;

                    return v;
                }
            }

            return 0;
        }

        public static string StripCommentFromLine(string srcLine, IEnumerable<string>? commentStartStrList = null, Ref<string>? strippedStr = null, bool commentMustBeWholeLine = false)
        {
            strippedStr?.Set("");
            if (srcLine == null) return "";
            if (commentStartStrList == null) commentStartStrList = Consts.Strings.CommentStartString;

            foreach (string keyword in commentStartStrList)
            {
                if (srcLine.Trim().StartsWith(keyword, StringComparison.OrdinalIgnoreCase))
                {
                    strippedStr?.Set(srcLine);
                    return "";
                }
            }

            if (commentMustBeWholeLine)
            {
                return srcLine;
            }

            int minStart = int.MaxValue;

            foreach (string keyword in commentStartStrList)
            {
                if (keyword != ";") // ; は頻繁に本文中で使われるので特別扱いする
                {
                    int startIndex = 0;

                    LOOP_START:
                    int i = srcLine.IndexOf(keyword, startIndex, StringComparison.OrdinalIgnoreCase);

                    // 「//」 は 「https://」 等にも使用される。そこで、 :// は無視する。。
                    if (keyword == "//" && i >= 1 && srcLine.Substring(i - 1, 3) == "://")
                    {
                        startIndex = i + 2;
                        goto LOOP_START;
                    }

                    if (i != -1)
                    {
                        minStart = Math.Min(i, minStart);
                    }
                }
            }

            if (minStart != int.MaxValue)
            {
                string ret = srcLine.Substring(0, minStart);

                ret = ret.TrimEnd();

                strippedStr?.Set(srcLine.Substring(minStart));

                return ret;
            }
            else
            {
                return srcLine;
            }
        }

        public static string GetTimeSpanStr(TimeSpan ts)
        {
            if (ts.Ticks < 0) ts = new TimeSpan(0);

            if (ts.TotalDays >= 1)
                return $"{ts.Days}d {ts.Hours}h {ts.Minutes}m {ts.Seconds}s";
            else if (ts.TotalHours >= 1)
                return $"{ts.Hours}h {ts.Minutes}m {ts.Seconds}s";
            else if (ts.Minutes >= 1)
                return $"{ts.Minutes}m {ts.Seconds}s";
            else
                return $"{ts.Seconds}s";
        }

        public static string GetTimeSpanStr(long tick)
            => GetTimeSpanStr(new TimeSpan(tick));

        // 文字列を特殊文字で区切る ただし特殊文字が 2 つ続いた場合は元の 1 つの特殊文字であるとみなす
        // 例: abc;def => [abc], [def]
        // 例: abc;;def;ghi => [abc;def], [ghi]
        public static IReadOnlyList<string> SplitBySpecialChar(string str, char sp = ';')
        {
            str = str._NonNullTrim();

            int mode = 0;

            StringBuilder sb = new StringBuilder();

            List<string> ret = new List<string>();

            foreach (char c in str)
            {
                if (mode == 0)
                {
                    if (c != sp)
                    {
                        sb.Append(c);
                    }
                    else
                    {
                        mode = 1;
                    }
                }
                else if (mode == 1)
                {
                    if (c == sp)
                    {
                        // 連続 2 文字 = 1 文字
                        sb.Append(c);
                    }
                    else
                    {
                        // 区切り検出
                        string s = sb.ToString();
                        if (s._IsFilled())
                        {
                            ret.Add(sb.ToString());
                        }
                        sb.Clear();
                        sb.Append(c);
                    }

                    mode = 0;
                }
            }

            string t = sb.ToString();
            if (t._IsFilled())
            {
                ret.Add(t);
            }

            return ret;
        }

        // 文字コード自動判別機
        private static class StrEncodingAutoDetectorInternal
        {
            public readonly static string UnknownStr = "{" + Str.NewGuid().ToLowerInvariant() + "}";

            public readonly static List<Encoding> EncodingsWithUnknownStrReplacement = new List<Encoding>();
            public readonly static List<Encoding> EncodingsNormal = new List<Encoding>();

            static StrEncodingAutoDetectorInternal()
            {
                string[] names = Consts.StrEncodingAutoDetector.Candidates._Split(StringSplitOptions.RemoveEmptyEntries, " ", "　", ",", "/", "\t", "\r", "\n");

                foreach (string name in names)
                {
                    Encoding encWithError = Encoding.GetEncoding(name, new EncoderReplacementFallback(UnknownStr), new DecoderReplacementFallback(UnknownStr));
                    Encoding enc = Encoding.GetEncoding(name);

                    EncodingsWithUnknownStrReplacement.Add(encWithError);
                    EncodingsNormal.Add(enc);
                }
            }
        }

        public static List<Encoding> GetSuitableEncodings(ReadOnlySpan<byte> data)
        {
            List<Encoding> ret = new List<Encoding>();

            for (int i = 0; i < StrEncodingAutoDetectorInternal.EncodingsWithUnknownStrReplacement.Count; i++)
            {
                try
                {
                    var encWithError = StrEncodingAutoDetectorInternal.EncodingsWithUnknownStrReplacement[i];
                    var encNormal = StrEncodingAutoDetectorInternal.EncodingsNormal[i];

                    bool isShiftJis = encWithError.WebName._IsSamei("shift_jis");

                    string tmp = encWithError.GetString(data);

                    if (tmp.IndexOf(StrEncodingAutoDetectorInternal.UnknownStr) == -1)
                    {
                        char[] charArray = tmp.ToCharArray();
                        bool ok = true;

                        foreach (var c in charArray)
                        {
                            if (c <= 127)
                            {
                                continue;
                            }

                            if (Char.IsControl(c))
                            {
                                ok = false;
                            }

                            var category = Char.GetUnicodeCategory(c);
                            if (category == UnicodeCategory.Control || category == UnicodeCategory.OtherNotAssigned || category == UnicodeCategory.Surrogate)
                            {
                                ok = false;
                            }

                            if (isShiftJis)
                            {
                                if (c > 0x9FCF && c < 0xff00)
                                {
                                    ok = false;
                                }
                            }

                            if (ok == false) break;
                        }

                        var data2 = NormalizeCrlf(data, CrlfStyle.Lf);

                        string test = encWithError.GetString(data2.Span);
                        byte[] data3 = encNormal.GetBytes(test);

                        if (data2._MemCompare(data3) != 0)
                        {
                            ok = false;
                        }

                        if (ok)
                        {
                            ret.Add(StrEncodingAutoDetectorInternal.EncodingsNormal[i]);
                        }
                    }
                }
                catch { }
            }

            return ret;
        }
    }

    public class AmbiguousSearchResult<T>
    {
        public AmbiguousSearchResult(string key, T value, long matchPoint, int index)
        {
            this.Key = key;
            this.Value = value;
            this.MatchPoint = matchPoint;
            this.Index = index;
        }

        public string Key { get; }
        public T Value { get; }
        public long MatchPoint { get; }
        public int Index { get; }
    }

    public class AmbiguousSearch<T> where T : class
    {
        readonly List<KeyValuePair<string, T>> List = new List<KeyValuePair<string, T>>();
        readonly Singleton<string, T> SearchTopWithCacheSingleton;

        public bool AllowWildcard { get; }

        public AmbiguousSearch(bool allowWildcard = false)
        {
            this.AllowWildcard = allowWildcard;

            this.SearchTopWithCacheSingleton = new Singleton<string, T>(
                x => this.SearchTop(x)!,
                StrComparer.IgnoreCaseComparer);
        }

        public void Add(string key, T value)
        {
            key = key._NonNullTrim();

            this.List.Add(new KeyValuePair<string, T>(key, value));
        }

        public IEnumerable<AmbiguousSearchResult<T>> Search(string key, StringComparison comparison = StringComparison.OrdinalIgnoreCase)
        {
            List<AmbiguousSearchResult<T>> ret = new List<AmbiguousSearchResult<T>>();

            for (int i = 0; i < List.Count; i++)
            {
                KeyValuePair<string, T> t = List[i];

                long point = t.Key._CalcKeywordMatchPoint(key, comparison);
                if (point >= 1)
                {
                    AmbiguousSearchResult<T> r = new AmbiguousSearchResult<T>(t.Key, t.Value, point, i);

                    ret.Add(r);
                }
                else
                {
                    if (this.AllowWildcard && t.Key == "*")
                    {
                        AmbiguousSearchResult<T> r = new AmbiguousSearchResult<T>(t.Key, t.Value, 0, i);

                        ret.Add(r);
                    }
                }
            }

            return ret.OrderByDescending(x => x.MatchPoint).ThenBy(x => x.Key, StrComparer.Get(comparison)).ThenBy(x => x.Index);
        }

        public T? SearchTop(string key, StringComparison comparison = StringComparison.OrdinalIgnoreCase)
        {
            AmbiguousSearchResult<T>? x = this.Search(key, comparison).FirstOrDefault();

            if (x == null)
            {
                return default!;
            }

            return x.Value;
        }

        public T? SearchTopWithCache(string key)
        {
            return SearchTopWithCacheSingleton[key];
        }
    }

    namespace Legacy
    {
        public class XmlCheckObjectInternal
        {
            public string? Str = null;
        }

        // 文字列トークン操作
        public class StrToken
        {
            string[] tokens;

            public string[] Tokens
            {
                get { return tokens; }
            }

            public string this[uint index]
            {
                get { return tokens[index]; }
            }

            public uint NumTokens
            {
                get
                {
                    return (uint)Tokens.Length;
                }
            }

            public StrToken(string[] tokens)
            {
                List<string> a = new List<string>();
                foreach (string s in tokens)
                {
                    a.Add(s);
                }

                this.tokens = a.ToArray();
            }

            public StrToken(string str, string splitStr = Consts.Strings.DefaultSplitStr)
            {
                // トークンの切り出し
                splitStr = splitStr._NotEmptyOrDefault(Consts.Strings.DefaultSplitStr);

                int i, len;
                len = splitStr.Length;
                char[] chars = new char[len];
                for (i = 0; i < len; i++)
                {
                    chars[i] = splitStr[i];
                }
                tokens = str.Split(chars, StringSplitOptions.RemoveEmptyEntries);
            }
        }

        // 文字列を各種のデータ型に変換
        public class StrData
        {
            public string StrValue { get; }

            public uint IntValue => Str.StrToUInt(StrValue);

            public ulong Int64Value => Str.StrToULong(StrValue);

            public bool BoolValue
            {
                get
                {
                    string s = StrValue.Trim();

                    if (Str.IsEmptyStr(s))
                    {
                        return false;
                    }
                    if (s.StartsWith("true", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                    if ("true".StartsWith(s, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                    if (s.StartsWith("yes", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                    if ("yes".StartsWith(s, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }

                    if (Str.StrToUInt(s) != 0)
                    {
                        return true;
                    }

                    return false;
                }
            }

            public StrData(string? str)
            {
                str = str._NonNull();

                StrValue = str;
            }
        }
    }

    public class StrClass
    {
        public string Value { get; } = "";

        public StrClass() { }

        public StrClass(string str)
        {
            this.Value = str;
        }

        public override string ToString()
        {
            return this.Value;
        }

        public static implicit operator string(StrClass s)
        {
            return s.Value;
        }

        public static implicit operator StrClass(string s)
        {
            return new StrClass(s);
        }
    }

    public class OneLineParams : KeyValueList<string, string>
    {
        public char Delimiter { get; }

        public OneLineParams(string oneLine = "", char delimiter = ';', bool treatDoubleDelimiterAsEscape = true)
        {
            oneLine = oneLine._NonNullTrim();

            // Parse
            this.Delimiter = delimiter;

            oneLine = Str.DecodeCEscape(oneLine);

            IReadOnlyList<string> list;

            if (treatDoubleDelimiterAsEscape)
                list = Str.SplitBySpecialChar(oneLine, this.Delimiter);
            else
                list = oneLine._Split(StringSplitOptions.RemoveEmptyEntries, delimiter);

            foreach (string token in list)
            {
                Str.GetKeyAndValue(token, out string key, out string value, "=:");

                if (key._IsFilled())
                {
                    key = key._NonNullTrim();
                    value = value._NonNullTrim();

                    this.Add(key, value);
                }
            }
        }

        public override string ToString()
        {
            // Combine
            List<string> o = new List<string>();

            foreach (var kv in this)
            {
                if (kv.Key._IsFilled())
                {
                    string tmp;

                    if (kv.Value._IsEmpty())
                    {
                        tmp = kv.Key;
                    }
                    else
                    {
                        tmp = kv.Key + "=" + kv.Value;
                    }

                    tmp = tmp._ReplaceStr("" + this.Delimiter, "" + this.Delimiter + this.Delimiter, true);

                    o.Add(tmp);
                }
            }

            string oneLine = o._Combine("" + this.Delimiter + " ");

            oneLine = Str.EncodeCEscape(oneLine);

            return oneLine;
        }
    }

    public readonly struct IgnoreCase
    {
        // Thanks to the great idea: https://stackoverflow.com/questions/631233/is-there-a-c-sharp-case-insensitive-equals-operator
        readonly string? Value;

        public IgnoreCase(string? value)
        {
            this.Value = value;
        }

        public override bool Equals(object? obj)
        {
            if (obj == null && this.Value == null) return true;
            if (obj == null) return false;
            if (ReferenceEquals(this.Value, obj)) return true;
            if (obj is IgnoreCase target)
            {
                return this == target;
            }
            else if (obj is string s)
            {
                return this == (IgnoreCase)s;
            }
            else
            {
                string? s2 = obj.ToString();
                return this == s2;
            }
        }

        public override int GetHashCode()
        {
            return Value?.GetHashCode(StringComparison.OrdinalIgnoreCase) ?? 0;
        }

        public static bool operator ==(IgnoreCase a, IgnoreCase b)
        {
            if ((object)a == null && (object)b == null) return true;
            if ((object)a == null || (object)b == null) return false;

            return a.Value._IsSamei(b.Value);
        }

        public static bool operator !=(IgnoreCase a, IgnoreCase b)
        {
            return !(a == b);
        }

        public static implicit operator string?(IgnoreCase s)
        {
            return s.Value;
        }

        public static implicit operator IgnoreCase(string? s)
        {
            return new IgnoreCase(s);
        }

        public override string? ToString() => this.Value;
    }

    public readonly struct Trim
    {
        // Thanks to the great idea: https://stackoverflow.com/questions/631233/is-there-a-c-sharp-case-insensitive-equals-operator
        readonly string? Value;

        public Trim(string? value)
        {
            this.Value = value;
        }

        public override bool Equals(object? obj)
        {
            if (obj == null && this.Value == null) return true;
            if (obj == null) return false;
            if (ReferenceEquals(this.Value, obj)) return true;
            if (obj is Trim target)
            {
                return this == target;
            }
            else if (obj is string s)
            {
                return this == (Trim)s;
            }
            else
            {
                string? s2 = obj.ToString();
                return this == s2;
            }
        }

        public override int GetHashCode()
        {
            return Value?._NonNullTrim().GetHashCode() ?? 0;
        }

        public static bool operator ==(Trim a, Trim b)
        {
            if ((object)a == null && (object)b == null) return true;
            if ((object)a == null || (object)b == null) return false;

            return a.Value._IsSameTrim(b.Value);
        }

        public static bool operator !=(Trim a, Trim b)
        {
            return !(a == b);
        }

        public static implicit operator string?(Trim s)
        {
            return s.Value;
        }

        public static implicit operator Trim(string? s)
        {
            return new Trim(s);
        }

        public override string? ToString() => this.Value;
    }

    public readonly struct IgnoreCaseTrim
    {
        // Thanks to the great idea: https://stackoverflow.com/questions/631233/is-there-a-c-sharp-case-insensitive-equals-operator
        readonly string? Value;

        public IgnoreCaseTrim(string? value)
        {
            this.Value = value;
        }

        public override bool Equals(object? obj)
        {
            if (obj == null && this.Value == null) return true;
            if (obj == null) return false;
            if (ReferenceEquals(this.Value, obj)) return true;
            if (obj is IgnoreCaseTrim target)
            {
                return this == target;
            }
            else if (obj is string s)
            {
                return this == (IgnoreCaseTrim)s;
            }
            else
            {
                string? s2 = obj.ToString();
                return this == s2;
            }
        }

        public override int GetHashCode()
        {
            return Value?._NonNullTrim().GetHashCode(StringComparison.OrdinalIgnoreCase) ?? 0;
        }

        public static bool operator ==(IgnoreCaseTrim a, IgnoreCaseTrim b)
        {
            if ((object)a == null && (object)b == null) return true;
            if ((object)a == null || (object)b == null) return false;

            return a.Value._IsSameTrimi(b.Value);
        }

        public static bool operator !=(IgnoreCaseTrim a, IgnoreCaseTrim b)
        {
            return !(a == b);
        }

        public static implicit operator string?(IgnoreCaseTrim s)
        {
            return s.Value;
        }

        public static implicit operator IgnoreCaseTrim(string? s)
        {
            return new IgnoreCaseTrim(s);
        }

        public override string? ToString() => this.Value;
    }

    public readonly struct IsEmpty
    {
        readonly string? Target;

        public IsEmpty(string? target)
        {
            this.Target = target;
        }

        public static implicit operator bool(IsEmpty t)
        {
            return t.Target._IsEmpty();
        }

        public static implicit operator IsEmpty(string? target)
        {
            return new IsEmpty(target);
        }

        public override string ToString() => ((bool)this).ToString();
    }

    public readonly struct IsFilled
    {
        readonly string? Target;

        public IsFilled(string? target)
        {
            this.Target = target;
        }

        public static implicit operator bool(IsFilled t)
        {
            return t.Target._IsFilled();
        }

        public static implicit operator IsFilled(string? target)
        {
            return new IsFilled(target);
        }

        public override string ToString() => ((bool)this).ToString();
    }


    public class QueryStringList : KeyValueList<string, string>
    {
        public char SplitChar = '&';

        public static QueryStringList Parse(string queryString, Encoding? encoding = null, char splitChar = '&', bool trimKeyAndValue = false)
        {
            try
            {
                return new QueryStringList(queryString, encoding, splitChar, trimKeyAndValue);
            }
            catch
            {
                return new();
            }
        }

        public QueryStringList() { }

        public QueryStringList(IEnumerable<KeyValuePair<string, string>> srcData)
        {
            foreach (var kv in srcData)
            {
                this.Add(kv.Key, kv.Value);
            }
        }

        public QueryStringList(string queryString, Encoding? encoding = null, char splitChar = '&', bool trimKeyAndValue = false)
        {
            this.SplitChar = splitChar;

            if (encoding == null) encoding = Str.Utf8Encoding;

            queryString = queryString._NonNull();

            // 先頭に ? があれば無視する
            if (queryString.StartsWith("?")) queryString = queryString.Substring(1);

            // ハッシュ文字 # があればそれ以降は無視する
            int i = queryString.IndexOf('#');
            if (i != -1) queryString = queryString.Substring(0, i);

            // & で分離する
            string[] tokens = queryString.Split(this.SplitChar, StringSplitOptions.RemoveEmptyEntries);

            foreach (string token in tokens)
            {
                // key と value を取得する
                string key, value;

                i = token.IndexOf('=');

                if (i == -1)
                {
                    key = token;
                    value = "";
                }
                else
                {
                    key = token.Substring(0, i);
                    value = token.Substring(i + 1);
                }

                // key と value を URL デコードする
                key = Str.DecodeUrl(key, encoding);
                value = Str.DecodeUrl(value, encoding);

                if (trimKeyAndValue)
                {
                    key = key.Trim();
                    value = value.Trim();
                }

                this.Add(key, value);
            }
        }

        public override string ToString()
            => ToString(null);

        public string ToString(Encoding? encoding, UrlEncodeParam? urlEncodeParam = null)
        {
            if (encoding == null) encoding = Str.Utf8Encoding;

            StringBuilder sb = new StringBuilder();

            for (int i = 0; i < this.Count; i++)
            {
                var kv = this[i];
                bool isLast = (i == (this.Count - 1));

                if (kv.Key._IsFilled() || kv.Value._IsFilled())
                {
                    string key = kv.Key._NonNull();
                    string value = kv.Value._NonNull();

                    // key と value を URL エンコードする
                    key = key._EncodeUrl(encoding, urlEncodeParam);
                    value = value._EncodeUrl(encoding, urlEncodeParam);

                    if (value._IsEmpty())
                    {
                        sb.Append(key);
                    }
                    else
                    {
                        sb.Append(key);
                        sb.Append('=');
                        sb.Append(value);
                    }

                    if (isLast == false)
                    {
                        sb.Append(this.SplitChar);
                    }
                }
            }

            return sb.ToString();
        }

        public void RemoveItem(string key)
        {
            this.RemoveWhenKey(key, StrComparer.IgnoreCaseTrimComparer);
        }

        public void AddOrUpdateKeyItemSingle(string key, string value)
        {
            this.AddOrUpdateKeyValueSingle(key, value, StrComparer.IgnoreCaseTrimComparer);
        }
    }


    // ユニークな文字列のリスト。StartWith による包含関係なしが保証されている
    public class LongestDistinctStrList
    {
        readonly StringComparison Comparison;

        readonly List<string> List = new List<string>();

        public LongestDistinctStrList(StringComparison comparison = StringComparison.OrdinalIgnoreCase)
        {
            Comparison = comparison;
        }

        public void Add(string str)
        {
            str = str._NonNullTrim();

            // 全く同一の文字列がリストに含まれるか?
            if (List.Where(t => string.Equals(t, str, Comparison)).Any()) return;

            // 追加しようとする文字列よりも長く、追加しようとする文字列を包含する文字列がリストに含まれるか?
            foreach (var s in List)
            {
                if (s.Length > str.Length)
                {
                    if (s.StartsWith(str, Comparison)) return;
                }
            }

            // 追加しようとする文字列よりも短く、追加しようとする文字列によって包含される文字列がリストに含まれるか?
            for (int i = 0; i < List.Count; i++)
            {
                if (List[i].Length < str.Length)
                {
                    if (str.StartsWith(List[i], Comparison))
                    {
                        // 置換
                        List[i] = str;
                        return;
                    }
                }
            }

            // いずれにも該当しない
            List.Add(str);
        }

        public IReadOnlyList<string> GetList() => this.List;

        public static IReadOnlyList<string> Normalize(IEnumerable<string> srcList, StringComparison comparison = StringComparison.OrdinalIgnoreCase)
        {
            LongestDistinctStrList o = new LongestDistinctStrList(comparison);

            srcList._DoForEach(x => o.Add(x));

            return o.GetList();
        }
    }

    // StrTable 言語
    public class StrTableLanguage
    {
        public string Key { get; }
        public string Name_English { get; }
        public string Name_Local { get; }
        public int WindowsLocaleId { get; }
        public HashSet<string> UnixLocalesList { get; } = new HashSet<string>();
        public HashSet<string> HttpAcceptLanguageList { get; } = new HashSet<string>();
        public StrTable Table { get; }

        public StrTableLanguage(string key, string name_English, string name_Local, int windowsLocaleId, string unixLocalesStrList, string httpAcceptLanguageStartWithList, StrTable table)
        {
            Key = key._NonNullTrim().ToLowerInvariant();
            Name_English = name_English;
            Name_Local = name_Local;
            WindowsLocaleId = windowsLocaleId;

            unixLocalesStrList._Split(StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries, ",")._DoForEach(x => this.UnixLocalesList.Add(x.ToLowerInvariant()));
            httpAcceptLanguageStartWithList._Split(StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries, ",")._DoForEach(x => this.HttpAcceptLanguageList.Add(x.ToLowerInvariant()));

            this.Table = table;
        }
    }

    // StrTable 言語リスト
    public class StrTableLanguageList
    {
        readonly CriticalSection<StrTableLanguageList> Lock = new CriticalSection<StrTableLanguageList>();
        readonly List<StrTableLanguage> LaugnageList = new List<StrTableLanguage>();
        readonly Singleton<string, StrTableLanguage> ByKeyCache;
        readonly Singleton<string, StrTableLanguage> ByHttpAcceptLanguageCache;

        public static StrTableLanguageList DefaultEmptyStrTableLanguageList { get; }

        static StrTableLanguageList()
        {
            DefaultEmptyStrTableLanguageList = new StrTableLanguageList();
            DefaultEmptyStrTableLanguageList.Add(new StrTableLanguage("zz", "Neutral", "Neutral", 0, "", "", new StrTable()));
        }

        public StrTableLanguageList()
        {
            ByKeyCache = new Singleton<string, StrTableLanguage>(key =>
            {
                key = key._NonNullTrim().ToLowerInvariant()._TruncStr(2);

                lock (this.Lock)
                {
                    if (key._IsEmpty()) return this.LaugnageList[0];

                    foreach (var item in this.LaugnageList)
                    {
                        if (item.Key == key)
                        {
                            return item;
                        }
                    }
                }

                return this.LaugnageList[0];
            });

            ByHttpAcceptLanguageCache = new Singleton<string, StrTableLanguage>(language =>
            {
                language = language._NonNullTrim().ToLowerInvariant()._TruncStr(2);

                lock (this.Lock)
                {
                    if (language._IsEmpty()) return this.LaugnageList[0];

                    foreach (var item in this.LaugnageList)
                    {
                        if (item.HttpAcceptLanguageList.Contains(language))
                        {
                            return item;
                        }
                    }
                }

                return this.LaugnageList[0];
            });
        }

        public IEnumerable<StrTableLanguage> GetLaugnageList()
        {
            lock (this.Lock)
            {
                return this.LaugnageList.ToArray();
            }
        }

        public void Add(StrTableLanguage language)
        {
            lock (this.Lock)
            {
                this.LaugnageList.Add(language);
            }
        }

        public StrTableLanguage DefaultLangage => FindDefaultLanguage();

        public StrTableLanguage FindDefaultLanguage()
            => FindLanguageByKey("");

        public StrTableLanguage FindLanguageByKey(string key)
        {
            key = key._NonNullTrim().ToLowerInvariant()._TruncStr(2);
            return this.ByKeyCache[key];
        }

        public StrTableLanguage FindLanguageByHttpAcceptLanguage(string language)
        {
            language = language._NonNullTrim().ToLowerInvariant()._TruncStr(2);
            return this.ByHttpAcceptLanguageCache[language];
        }
    }

    // StrTable
    public class StrTable
    {
        readonly Dictionary<string, string> EntryList = new Dictionary<string, string>();
        readonly KeyValueList<string, string> ReplaceList = new KeyValueList<string, string>();

        string CurrentPrefix = "";

        public StrTable(params string[] bodyList)
        {
            if (bodyList != null)
            {
                foreach (string body in bodyList)
                {
                    this.ImportLines(body._GetLines());
                }
            }
        }

        public string this[string key]
        {
            get => GetStr(key);
        }

        public KeyValueList<string, string> ToList()
        {
            KeyValueList<string, string> ret = new KeyValueList<string, string>();

            foreach (var item in this.EntryList)
            {
                ret.Add(item);
            }

            ret.Sort((x, y) => x.Key._Cmpi(y.Key));

            return ret;
        }

        public string GetStr(string key, string notFoundValue = "")
        {
            key = key._NonNullTrim().ToUpperInvariant();

            return this.EntryList._GetOrDefault(key, notFoundValue)._NonNull();
        }

        public async Task ImportFileAsync(FilePath file, CancellationToken cancel = default)
        {
            string body = await file.ReadStringFromFileAsync(cancel: cancel);

            ImportLines(body._GetLines());
        }
        public void ImportFile(FilePath file, CancellationToken cancel = default)
            => ImportFileAsync(file, cancel)._GetResult();

        public void ImportLines(IEnumerable<string> lines)
        {
            this.CurrentPrefix = "";

            lines._DoForEach(line => ImportLine(line));
        }

        public void ImportLine(string line)
        {
            line = line._NonNull().TrimStart(' ', '\t');
            if (line._IsEmpty()) return;
            if (line[0] == '#' || (line[0] == '/' && line[1] == '/')) return;

            Str.GetKeyAndValue(line, out string key, out string value);
            if (key._IsEmpty()) return;

            key = key.ToUpperInvariant();

            value = UnescapeStr(value);

            if (key == "PREFIX")
            {
                value = value._NonNullTrim();

                if (value == "$" || value._IsSamei("NULL"))
                {
                    value = "";
                }

                this.CurrentPrefix = value;
                return;
            }

            if (this.CurrentPrefix._IsFilled())
            {
                key = this.CurrentPrefix + "@" + key;
            }

            if (key.StartsWith("$") && key.EndsWith("$") && key.Length >= 3)
            {
                if (this.ReplaceList.Where(x => x.Key._IsSamei(key)).Any() == false)
                {
                    this.ReplaceList.Add(key, value);
                }
            }
            else
            {
                if (value._InStr("$"))
                {
                    foreach (var replaceItem in this.ReplaceList)
                    {
                        value = value._ReplaceStr(replaceItem.Key, replaceItem.Value, false);
                    }
                }
            }

            this.EntryList._GetOrNew(key, value);
        }

        public static string UnescapeStr(string str)
        {
            int i, len;
            StringBuilder tmp = new StringBuilder();

            len = str.Length;

            for (i = 0; i < len; i++)
            {
                char c = str[i];
                if (c == '\\')
                {
                    i++;
                    char c2 = ' ';
                    try
                    {
                        c2 = str[i];
                    }
                    catch { }
                    switch (c2)
                    {
                        case '\\':
                            tmp.Append('\\');
                            break;

                        case ' ':
                            tmp.Append(' ');
                            break;

                        case 'n':
                        case 'N':
                            tmp.Append('\n');
                            break;

                        case 'r':
                        case 'R':
                            tmp.Append('\r');
                            break;

                        case 't':
                        case 'T':
                            tmp.Append('\t');
                            break;
                    }
                }
                else
                {
                    tmp.Append(c);
                }
            }

            return tmp.ToString();
        }
    }




    [Flags]
    public enum PortRangeStyle
    {
        Normal = 0,
    }

    public class PortRange
    {
        readonly Memory<bool> PortArray = new bool[Consts.Numbers.PortMax + 1];

        public PortRange()
        {
        }

        public PortRange(string rangeString)
        {
            Add(rangeString);
        }

        public void Add(string rangeString)
        {
            var span = PortArray.Span;

            string[] tokens = rangeString._Split(StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries, ',', ';', ' ', '　', '\t');

            foreach (var token in tokens)
            {
                string[] tokens2 = token._Split(StringSplitOptions.TrimEntries, '-');
                if (tokens2.Length == 1)
                {
                    int number = tokens2[0]._ToInt();

                    if (number._IsValidPortNumber())
                    {
                        span[number] = true;
                    }
                }
                else if (tokens2.Length == 2)
                {
                    int number1 = tokens2[0]._ToInt();
                    int number2 = tokens2[1]._ToInt();
                    int start = Math.Min(number1, number2);
                    int end = Math.Max(number1, number2);
                    if (start._IsValidPortNumber() && end._IsValidPortNumber())
                    {
                        for (int i = start; i <= end; i++)
                        {
                            span[i] = true;
                        }
                    }
                }
            }
        }

        public List<int> ToArray()
        {
            List<int> ret = new List<int>();
            var span = this.PortArray.Span;
            for (int i = Consts.Numbers.PortMin; i <= Consts.Numbers.PortMax; i++)
            {
                if (span[i])
                {
                    ret.Add(i);
                }
            }
            return ret;
        }

        public override string ToString() => ToString(PortRangeStyle.Normal);

        public string ToString(PortRangeStyle style)
        {
            var span = PortArray.Span;

            List<WPair2<int, int>> segments = new List<WPair2<int, int>>();

            WPair2<int, int>? current = null;

            for (int i = Consts.Numbers.PortMin; i <= Consts.Numbers.PortMax; i++)
            {
                if (span[i])
                {
                    if (current == null)
                    {
                        current = new WPair2<int, int>(i, i);
                        segments.Add(current);
                    }
                    else
                    {
                        current.B = i;
                    }
                }
                else
                {
                    if (current != null)
                    {
                        current = null;
                    }
                }
            }

            switch (style)
            {
                case PortRangeStyle.Normal:
                    {
                        StringBuilder sb = new StringBuilder();
                        foreach (var segment in segments)
                        {
                            string str;

                            if (segment.A == segment.B)
                            {
                                str = segment.A.ToString();
                            }
                            else
                            {
                                str = $"{segment.A}-{segment.B}";
                            }

                            sb.Append(str);

                            sb.Append(",");
                        }

                        return sb.ToString().TrimEnd(',');
                    }

                default:
                    throw new ArgumentOutOfRangeException(nameof(style));
            }
        }
    }

}

// Unicode の康熙部首 (The Unicode Standard Kangxi Radicals) の変換ユーティティ
public static class UnicodeStdKangxiMapUtil
{
    // Strange: ⺃⺅⺉⺊⺋⺏⺐⺑⺒⺓⺔⺖⺘⺙⺛⺞⺟⺠⺡⺢⺣⺤⺥⺦⺨⺩⺪⺫⺬⺭⺯⺰⺱⺲⺸⺹⺺⺽⺾⺿⻀⻁⻂⻃⻄⻅⻈⻉⻋⻍⻎⻐⻑⻒⻓⻔⻖⻘⻙⻚⻛⻜⻝⻟⻠⻢⻣⻤⻥⻦⻧⻨⻩⻪⻫⻬⻭⻮⻯⻰⻱⻲⻳⼀⼁⼂⼃⼄⼅⼆⼇⼈⼉⼊⼋⼌⼍⼎⼏⼐⼑⼒⼓⼔⼕⼖⼗⼘⼙⼚⼛⼜⼝⼞⼟⼠⼡⼢⼣⼤⼥⼦⼧⼨⼩⼪⼫⼬⼭⼮⼯⼰⼱⼲⼳⼴⼵⼶⼷⼸⼹⼺⼻⼼⼽⼾⼿⽀⽁⽂⽃⽄⽅⽆⽇⽈⽉⽊⽋⽌⽍⽎⽏⽐⽑⽒⽓⽔⽕⽖⽗⽘⽙⽚⽛⽜⽝⽞⽟⽠⽡⽢⽣⽤⽥⽦⽧⽨⽩⽪⽫⽬⽭⽮⽯⽰⽱⽲⽳⽴⽵⽶⽷⽸⽹⽺⽻⽼⽽⽾⽿⾀⾁⾂⾃⾄⾅⾆⾇⾈⾉⾊⾋⾌⾍⾎⾏⾐⾑⾒⾓⾔⾕⾖⾗⾘⾙⾚⾛⾜⾝⾞⾟⾠⾡⾢⾣⾤⾥⾦⾧⾨⾩⾪⾫⾬⾭⾮⾯⾰⾱⾲⾳⾴⾵⾶⾷⾸⾹⾺⾻⾼⾽⾾⾿⿀⿁⿂⿃⿄⿅⿆⿇⿈⿉⿊⿋⿌⿍⿎⿏⿐⿑⿒⿓⿔⿕

    // Normal: 乚亻刂卜㔾尣尢尣巳幺彑忄扌攵旡歺母民氵氺灬爫爫丬犭王疋目示礻糹纟罓罒羋耂肀臼艹艹艹虎衤覀西见讠贝车辶辶钅長镸长门阝青韦页风飞食飠饣马骨鬼鱼鸟卤麦黄黾斉齐歯齿竜龙龜亀龟一丨丶丿乙亅二亠人儿入八冂冖冫几凵刀力勹匕匚匸十卜卩厂厶又口囗土士夂夊夕大女子宀寸小尢尸屮山巛工己巾干幺广廴廾弋弓彐彡彳心戈戶手支攴文斗斤方无日曰月木欠止歹殳毋比毛氏气水火爪父爻爿片牙牛犬玄玉瓜瓦甘生用田疋疒癶白皮皿目矛矢石示禸禾穴立竹米糸缶网羊羽老而耒耳聿肉臣自至臼舌舛舟艮色艸虍虫血行衣襾見角言谷豆豕豸貝赤走足身車辛辰辵邑酉釆里金長門阜隶隹雨靑非面革韋韭音頁風飛食首香馬骨高髟鬥鬯鬲鬼魚鳥鹵鹿麥麻黃黍黑黹黽鼎鼓鼠鼻齊齒龍龜龠

    // '⺃' (0x2e83) <--> '乚' (0x4e5a)
    // '⺅' (0x2e85) <--> '亻' (0x4ebb)
    // '⺉' (0x2e89) <--> '刂' (0x5202)
    // '⺊' (0x2e8a) <--> '卜' (0x535c)
    // '⺋' (0x2e8b) <--> '㔾' (0x353e)
    // '⺏' (0x2e8f) <--> '尣' (0x5c23)
    // '⺐' (0x2e90) <--> '尢' (0x5c22)
    // '⺑' (0x2e91) <--> '尣' (0x5c23)
    // '⺒' (0x2e92) <--> '巳' (0x5df3)
    // '⺓' (0x2e93) <--> '幺' (0x5e7a)
    // '⺔' (0x2e94) <--> '彑' (0x5f51)
    // '⺖' (0x2e96) <--> '忄' (0x5fc4)
    // '⺘' (0x2e98) <--> '扌' (0x624c)
    // '⺙' (0x2e99) <--> '攵' (0x6535)
    // '⺛' (0x2e9b) <--> '旡' (0x65e1)
    // '⺞' (0x2e9e) <--> '歺' (0x6b7a)
    // '⺟' (0x2e9f) <--> '母' (0x6bcd)
    // '⺠' (0x2ea0) <--> '民' (0x6c11)
    // '⺡' (0x2ea1) <--> '氵' (0x6c35)
    // '⺢' (0x2ea2) <--> '氺' (0x6c3a)
    // '⺣' (0x2ea3) <--> '灬' (0x706c)
    // '⺤' (0x2ea4) <--> '爫' (0x722b)
    // '⺥' (0x2ea5) <--> '爫' (0x722b)
    // '⺦' (0x2ea6) <--> '丬' (0x4e2c)
    // '⺨' (0x2ea8) <--> '犭' (0x72ad)
    // '⺩' (0x2ea9) <--> '王' (0x738b)
    // '⺪' (0x2eaa) <--> '疋' (0x758b)
    // '⺫' (0x2eab) <--> '目' (0x76ee)
    // '⺬' (0x2eac) <--> '示' (0x793a)
    // '⺭' (0x2ead) <--> '礻' (0x793b)
    // '⺯' (0x2eaf) <--> '糹' (0x7cf9)
    // '⺰' (0x2eb0) <--> '纟' (0x7e9f)
    // '⺱' (0x2eb1) <--> '罓' (0x7f53)
    // '⺲' (0x2eb2) <--> '罒' (0x7f52)
    // '⺸' (0x2eb8) <--> '羋' (0x7f8b)
    // '⺹' (0x2eb9) <--> '耂' (0x8002)
    // '⺺' (0x2eba) <--> '肀' (0x8080)
    // '⺽' (0x2ebd) <--> '臼' (0x81fc)
    // '⺾' (0x2ebe) <--> '艹' (0x8279)
    // '⺿' (0x2ebf) <--> '艹' (0x8279)
    // '⻀' (0x2ec0) <--> '艹' (0x8279)
    // '⻁' (0x2ec1) <--> '虎' (0x864e)
    // '⻂' (0x2ec2) <--> '衤' (0x8864)
    // '⻃' (0x2ec3) <--> '覀' (0x8980)
    // '⻄' (0x2ec4) <--> '西' (0x897f)
    // '⻅' (0x2ec5) <--> '见' (0x89c1)
    // '⻈' (0x2ec8) <--> '讠' (0x8ba0)
    // '⻉' (0x2ec9) <--> '贝' (0x8d1d)
    // '⻋' (0x2ecb) <--> '车' (0x8f66)
    // '⻍' (0x2ecd) <--> '辶' (0x8fb6)
    // '⻎' (0x2ece) <--> '辶' (0x8fb6)
    // '⻐' (0x2ed0) <--> '钅' (0x9485)
    // '⻑' (0x2ed1) <--> '長' (0x9577)
    // '⻒' (0x2ed2) <--> '镸' (0x9578)
    // '⻓' (0x2ed3) <--> '长' (0x957f)
    // '⻔' (0x2ed4) <--> '门' (0x95e8)
    // '⻖' (0x2ed6) <--> '阝' (0x961d)
    // '⻘' (0x2ed8) <--> '青' (0x9752)
    // '⻙' (0x2ed9) <--> '韦' (0x97e6)
    // '⻚' (0x2eda) <--> '页' (0x9875)
    // '⻛' (0x2edb) <--> '风' (0x98ce)
    // '⻜' (0x2edc) <--> '飞' (0x98de)
    // '⻝' (0x2edd) <--> '食' (0x98df)
    // '⻟' (0x2edf) <--> '飠' (0x98e0)
    // '⻠' (0x2ee0) <--> '饣' (0x9963)
    // '⻢' (0x2ee2) <--> '马' (0x9a6c)
    // '⻣' (0x2ee3) <--> '骨' (0x9aa8)
    // '⻤' (0x2ee4) <--> '鬼' (0x9b3c)
    // '⻥' (0x2ee5) <--> '鱼' (0x9c7c)
    // '⻦' (0x2ee6) <--> '鸟' (0x9e1f)
    // '⻧' (0x2ee7) <--> '卤' (0x5364)
    // '⻨' (0x2ee8) <--> '麦' (0x9ea6)
    // '⻩' (0x2ee9) <--> '黄' (0x9ec4)
    // '⻪' (0x2eea) <--> '黾' (0x9efe)
    // '⻫' (0x2eeb) <--> '斉' (0x6589)
    // '⻬' (0x2eec) <--> '齐' (0x9f50)
    // '⻭' (0x2eed) <--> '歯' (0x6b6f)
    // '⻮' (0x2eee) <--> '齿' (0x9f7f)
    // '⻯' (0x2eef) <--> '竜' (0x7adc)
    // '⻰' (0x2ef0) <--> '龙' (0x9f99)
    // '⻱' (0x2ef1) <--> '龜' (0x9f9c)
    // '⻲' (0x2ef2) <--> '亀' (0x4e80)
    // '⻳' (0x2ef3) <--> '龟' (0x9f9f)
    // '⼀' (0x2f00) <--> '一' (0x4e00)
    // '⼁' (0x2f01) <--> '丨' (0x4e28)
    // '⼂' (0x2f02) <--> '丶' (0x4e36)
    // '⼃' (0x2f03) <--> '丿' (0x4e3f)
    // '⼄' (0x2f04) <--> '乙' (0x4e59)
    // '⼅' (0x2f05) <--> '亅' (0x4e85)
    // '⼆' (0x2f06) <--> '二' (0x4e8c)
    // '⼇' (0x2f07) <--> '亠' (0x4ea0)
    // '⼈' (0x2f08) <--> '人' (0x4eba)
    // '⼉' (0x2f09) <--> '儿' (0x513f)
    // '⼊' (0x2f0a) <--> '入' (0x5165)
    // '⼋' (0x2f0b) <--> '八' (0x516b)
    // '⼌' (0x2f0c) <--> '冂' (0x5182)
    // '⼍' (0x2f0d) <--> '冖' (0x5196)
    // '⼎' (0x2f0e) <--> '冫' (0x51ab)
    // '⼏' (0x2f0f) <--> '几' (0x51e0)
    // '⼐' (0x2f10) <--> '凵' (0x51f5)
    // '⼑' (0x2f11) <--> '刀' (0x5200)
    // '⼒' (0x2f12) <--> '力' (0x529b)
    // '⼓' (0x2f13) <--> '勹' (0x52f9)
    // '⼔' (0x2f14) <--> '匕' (0x5315)
    // '⼕' (0x2f15) <--> '匚' (0x531a)
    // '⼖' (0x2f16) <--> '匸' (0x5338)
    // '⼗' (0x2f17) <--> '十' (0x5341)
    // '⼘' (0x2f18) <--> '卜' (0x535c)
    // '⼙' (0x2f19) <--> '卩' (0x5369)
    // '⼚' (0x2f1a) <--> '厂' (0x5382)
    // '⼛' (0x2f1b) <--> '厶' (0x53b6)
    // '⼜' (0x2f1c) <--> '又' (0x53c8)
    // '⼝' (0x2f1d) <--> '口' (0x53e3)
    // '⼞' (0x2f1e) <--> '囗' (0x56d7)
    // '⼟' (0x2f1f) <--> '土' (0x571f)
    // '⼠' (0x2f20) <--> '士' (0x58eb)
    // '⼡' (0x2f21) <--> '夂' (0x5902)
    // '⼢' (0x2f22) <--> '夊' (0x590a)
    // '⼣' (0x2f23) <--> '夕' (0x5915)
    // '⼤' (0x2f24) <--> '大' (0x5927)
    // '⼥' (0x2f25) <--> '女' (0x5973)
    // '⼦' (0x2f26) <--> '子' (0x5b50)
    // '⼧' (0x2f27) <--> '宀' (0x5b80)
    // '⼨' (0x2f28) <--> '寸' (0x5bf8)
    // '⼩' (0x2f29) <--> '小' (0x5c0f)
    // '⼪' (0x2f2a) <--> '尢' (0x5c22)
    // '⼫' (0x2f2b) <--> '尸' (0x5c38)
    // '⼬' (0x2f2c) <--> '屮' (0x5c6e)
    // '⼭' (0x2f2d) <--> '山' (0x5c71)
    // '⼮' (0x2f2e) <--> '巛' (0x5ddb)
    // '⼯' (0x2f2f) <--> '工' (0x5de5)
    // '⼰' (0x2f30) <--> '己' (0x5df1)
    // '⼱' (0x2f31) <--> '巾' (0x5dfe)
    // '⼲' (0x2f32) <--> '干' (0x5e72)
    // '⼳' (0x2f33) <--> '幺' (0x5e7a)
    // '⼴' (0x2f34) <--> '广' (0x5e7f)
    // '⼵' (0x2f35) <--> '廴' (0x5ef4)
    // '⼶' (0x2f36) <--> '廾' (0x5efe)
    // '⼷' (0x2f37) <--> '弋' (0x5f0b)
    // '⼸' (0x2f38) <--> '弓' (0x5f13)
    // '⼹' (0x2f39) <--> '彐' (0x5f50)
    // '⼺' (0x2f3a) <--> '彡' (0x5f61)
    // '⼻' (0x2f3b) <--> '彳' (0x5f73)
    // '⼼' (0x2f3c) <--> '心' (0x5fc3)
    // '⼽' (0x2f3d) <--> '戈' (0x6208)
    // '⼾' (0x2f3e) <--> '戶' (0x6236)
    // '⼿' (0x2f3f) <--> '手' (0x624b)
    // '⽀' (0x2f40) <--> '支' (0x652f)
    // '⽁' (0x2f41) <--> '攴' (0x6534)
    // '⽂' (0x2f42) <--> '文' (0x6587)
    // '⽃' (0x2f43) <--> '斗' (0x6597)
    // '⽄' (0x2f44) <--> '斤' (0x65a4)
    // '⽅' (0x2f45) <--> '方' (0x65b9)
    // '⽆' (0x2f46) <--> '无' (0x65e0)
    // '⽇' (0x2f47) <--> '日' (0x65e5)
    // '⽈' (0x2f48) <--> '曰' (0x66f0)
    // '⽉' (0x2f49) <--> '月' (0x6708)
    // '⽊' (0x2f4a) <--> '木' (0x6728)
    // '⽋' (0x2f4b) <--> '欠' (0x6b20)
    // '⽌' (0x2f4c) <--> '止' (0x6b62)
    // '⽍' (0x2f4d) <--> '歹' (0x6b79)
    // '⽎' (0x2f4e) <--> '殳' (0x6bb3)
    // '⽏' (0x2f4f) <--> '毋' (0x6bcb)
    // '⽐' (0x2f50) <--> '比' (0x6bd4)
    // '⽑' (0x2f51) <--> '毛' (0x6bdb)
    // '⽒' (0x2f52) <--> '氏' (0x6c0f)
    // '⽓' (0x2f53) <--> '气' (0x6c14)
    // '⽔' (0x2f54) <--> '水' (0x6c34)
    // '⽕' (0x2f55) <--> '火' (0x706b)
    // '⽖' (0x2f56) <--> '爪' (0x722a)
    // '⽗' (0x2f57) <--> '父' (0x7236)
    // '⽘' (0x2f58) <--> '爻' (0x723b)
    // '⽙' (0x2f59) <--> '爿' (0x723f)
    // '⽚' (0x2f5a) <--> '片' (0x7247)
    // '⽛' (0x2f5b) <--> '牙' (0x7259)
    // '⽜' (0x2f5c) <--> '牛' (0x725b)
    // '⽝' (0x2f5d) <--> '犬' (0x72ac)
    // '⽞' (0x2f5e) <--> '玄' (0x7384)
    // '⽟' (0x2f5f) <--> '玉' (0x7389)
    // '⽠' (0x2f60) <--> '瓜' (0x74dc)
    // '⽡' (0x2f61) <--> '瓦' (0x74e6)
    // '⽢' (0x2f62) <--> '甘' (0x7518)
    // '⽣' (0x2f63) <--> '生' (0x751f)
    // '⽤' (0x2f64) <--> '用' (0x7528)
    // '⽥' (0x2f65) <--> '田' (0x7530)
    // '⽦' (0x2f66) <--> '疋' (0x758b)
    // '⽧' (0x2f67) <--> '疒' (0x7592)
    // '⽨' (0x2f68) <--> '癶' (0x7676)
    // '⽩' (0x2f69) <--> '白' (0x767d)
    // '⽪' (0x2f6a) <--> '皮' (0x76ae)
    // '⽫' (0x2f6b) <--> '皿' (0x76bf)
    // '⽬' (0x2f6c) <--> '目' (0x76ee)
    // '⽭' (0x2f6d) <--> '矛' (0x77db)
    // '⽮' (0x2f6e) <--> '矢' (0x77e2)
    // '⽯' (0x2f6f) <--> '石' (0x77f3)
    // '⽰' (0x2f70) <--> '示' (0x793a)
    // '⽱' (0x2f71) <--> '禸' (0x79b8)
    // '⽲' (0x2f72) <--> '禾' (0x79be)
    // '⽳' (0x2f73) <--> '穴' (0x7a74)
    // '⽴' (0x2f74) <--> '立' (0x7acb)
    // '⽵' (0x2f75) <--> '竹' (0x7af9)
    // '⽶' (0x2f76) <--> '米' (0x7c73)
    // '⽷' (0x2f77) <--> '糸' (0x7cf8)
    // '⽸' (0x2f78) <--> '缶' (0x7f36)
    // '⽹' (0x2f79) <--> '网' (0x7f51)
    // '⽺' (0x2f7a) <--> '羊' (0x7f8a)
    // '⽻' (0x2f7b) <--> '羽' (0x7fbd)
    // '⽼' (0x2f7c) <--> '老' (0x8001)
    // '⽽' (0x2f7d) <--> '而' (0x800c)
    // '⽾' (0x2f7e) <--> '耒' (0x8012)
    // '⽿' (0x2f7f) <--> '耳' (0x8033)
    // '⾀' (0x2f80) <--> '聿' (0x807f)
    // '⾁' (0x2f81) <--> '肉' (0x8089)
    // '⾂' (0x2f82) <--> '臣' (0x81e3)
    // '⾃' (0x2f83) <--> '自' (0x81ea)
    // '⾄' (0x2f84) <--> '至' (0x81f3)
    // '⾅' (0x2f85) <--> '臼' (0x81fc)
    // '⾆' (0x2f86) <--> '舌' (0x820c)
    // '⾇' (0x2f87) <--> '舛' (0x821b)
    // '⾈' (0x2f88) <--> '舟' (0x821f)
    // '⾉' (0x2f89) <--> '艮' (0x826e)
    // '⾊' (0x2f8a) <--> '色' (0x8272)
    // '⾋' (0x2f8b) <--> '艸' (0x8278)
    // '⾌' (0x2f8c) <--> '虍' (0x864d)
    // '⾍' (0x2f8d) <--> '虫' (0x866b)
    // '⾎' (0x2f8e) <--> '血' (0x8840)
    // '⾏' (0x2f8f) <--> '行' (0x884c)
    // '⾐' (0x2f90) <--> '衣' (0x8863)
    // '⾑' (0x2f91) <--> '襾' (0x897e)
    // '⾒' (0x2f92) <--> '見' (0x898b)
    // '⾓' (0x2f93) <--> '角' (0x89d2)
    // '⾔' (0x2f94) <--> '言' (0x8a00)
    // '⾕' (0x2f95) <--> '谷' (0x8c37)
    // '⾖' (0x2f96) <--> '豆' (0x8c46)
    // '⾗' (0x2f97) <--> '豕' (0x8c55)
    // '⾘' (0x2f98) <--> '豸' (0x8c78)
    // '⾙' (0x2f99) <--> '貝' (0x8c9d)
    // '⾚' (0x2f9a) <--> '赤' (0x8d64)
    // '⾛' (0x2f9b) <--> '走' (0x8d70)
    // '⾜' (0x2f9c) <--> '足' (0x8db3)
    // '⾝' (0x2f9d) <--> '身' (0x8eab)
    // '⾞' (0x2f9e) <--> '車' (0x8eca)
    // '⾟' (0x2f9f) <--> '辛' (0x8f9b)
    // '⾠' (0x2fa0) <--> '辰' (0x8fb0)
    // '⾡' (0x2fa1) <--> '辵' (0x8fb5)
    // '⾢' (0x2fa2) <--> '邑' (0x9091)
    // '⾣' (0x2fa3) <--> '酉' (0x9149)
    // '⾤' (0x2fa4) <--> '釆' (0x91c6)
    // '⾥' (0x2fa5) <--> '里' (0x91cc)
    // '⾦' (0x2fa6) <--> '金' (0x91d1)
    // '⾧' (0x2fa7) <--> '長' (0x9577)
    // '⾨' (0x2fa8) <--> '門' (0x9580)
    // '⾩' (0x2fa9) <--> '阜' (0x961c)
    // '⾪' (0x2faa) <--> '隶' (0x96b6)
    // '⾫' (0x2fab) <--> '隹' (0x96b9)
    // '⾬' (0x2fac) <--> '雨' (0x96e8)
    // '⾭' (0x2fad) <--> '靑' (0x9751)
    // '⾮' (0x2fae) <--> '非' (0x975e)
    // '⾯' (0x2faf) <--> '面' (0x9762)
    // '⾰' (0x2fb0) <--> '革' (0x9769)
    // '⾱' (0x2fb1) <--> '韋' (0x97cb)
    // '⾲' (0x2fb2) <--> '韭' (0x97ed)
    // '⾳' (0x2fb3) <--> '音' (0x97f3)
    // '⾴' (0x2fb4) <--> '頁' (0x9801)
    // '⾵' (0x2fb5) <--> '風' (0x98a8)
    // '⾶' (0x2fb6) <--> '飛' (0x98db)
    // '⾷' (0x2fb7) <--> '食' (0x98df)
    // '⾸' (0x2fb8) <--> '首' (0x9996)
    // '⾹' (0x2fb9) <--> '香' (0x9999)
    // '⾺' (0x2fba) <--> '馬' (0x99ac)
    // '⾻' (0x2fbb) <--> '骨' (0x9aa8)
    // '⾼' (0x2fbc) <--> '高' (0x9ad8)
    // '⾽' (0x2fbd) <--> '髟' (0x9adf)
    // '⾾' (0x2fbe) <--> '鬥' (0x9b25)
    // '⾿' (0x2fbf) <--> '鬯' (0x9b2f)
    // '⿀' (0x2fc0) <--> '鬲' (0x9b32)
    // '⿁' (0x2fc1) <--> '鬼' (0x9b3c)
    // '⿂' (0x2fc2) <--> '魚' (0x9b5a)
    // '⿃' (0x2fc3) <--> '鳥' (0x9ce5)
    // '⿄' (0x2fc4) <--> '鹵' (0x9e75)
    // '⿅' (0x2fc5) <--> '鹿' (0x9e7f)
    // '⿆' (0x2fc6) <--> '麥' (0x9ea5)
    // '⿇' (0x2fc7) <--> '麻' (0x9ebb)
    // '⿈' (0x2fc8) <--> '黃' (0x9ec3)
    // '⿉' (0x2fc9) <--> '黍' (0x9ecd)
    // '⿊' (0x2fca) <--> '黑' (0x9ed1)
    // '⿋' (0x2fcb) <--> '黹' (0x9ef9)
    // '⿌' (0x2fcc) <--> '黽' (0x9efd)
    // '⿍' (0x2fcd) <--> '鼎' (0x9f0e)
    // '⿎' (0x2fce) <--> '鼓' (0x9f13)
    // '⿏' (0x2fcf) <--> '鼠' (0x9f20)
    // '⿐' (0x2fd0) <--> '鼻' (0x9f3b)
    // '⿑' (0x2fd1) <--> '齊' (0x9f4a)
    // '⿒' (0x2fd2) <--> '齒' (0x9f52)
    // '⿓' (0x2fd3) <--> '龍' (0x9f8d)
    // '⿔' (0x2fd4) <--> '龜' (0x9f9c)
    // '⿕' (0x2fd5) <--> '龠' (0x9fa0)

    public static readonly IEnumerable<char> StrangeCharList = new char[] {
        (char)0x2e83 /* '⺃' */,
        (char)0x2e85 /* '⺅' */,
        (char)0x2e89 /* '⺉' */,
        (char)0x2e8a /* '⺊' */,
        (char)0x2e8b /* '⺋' */,
        (char)0x2e8f /* '⺏' */,
        (char)0x2e90 /* '⺐' */,
        (char)0x2e91 /* '⺑' */,
        (char)0x2e92 /* '⺒' */,
        (char)0x2e93 /* '⺓' */,
        (char)0x2e94 /* '⺔' */,
        (char)0x2e96 /* '⺖' */,
        (char)0x2e98 /* '⺘' */,
        (char)0x2e99 /* '⺙' */,
        (char)0x2e9b /* '⺛' */,
        (char)0x2e9e /* '⺞' */,
        (char)0x2e9f /* '⺟' */,
        (char)0x2ea0 /* '⺠' */,
        (char)0x2ea1 /* '⺡' */,
        (char)0x2ea2 /* '⺢' */,
        (char)0x2ea3 /* '⺣' */,
        (char)0x2ea4 /* '⺤' */,
        (char)0x2ea5 /* '⺥' */,
        (char)0x2ea6 /* '⺦' */,
        (char)0x2ea8 /* '⺨' */,
        (char)0x2ea9 /* '⺩' */,
        (char)0x2eaa /* '⺪' */,
        (char)0x2eab /* '⺫' */,
        (char)0x2eac /* '⺬' */,
        (char)0x2ead /* '⺭' */,
        (char)0x2eaf /* '⺯' */,
        (char)0x2eb0 /* '⺰' */,
        (char)0x2eb1 /* '⺱' */,
        (char)0x2eb2 /* '⺲' */,
        (char)0x2eb8 /* '⺸' */,
        (char)0x2eb9 /* '⺹' */,
        (char)0x2eba /* '⺺' */,
        (char)0x2ebd /* '⺽' */,
        (char)0x2ebe /* '⺾' */,
        (char)0x2ebf /* '⺿' */,
        (char)0x2ec0 /* '⻀' */,
        (char)0x2ec1 /* '⻁' */,
        (char)0x2ec2 /* '⻂' */,
        (char)0x2ec3 /* '⻃' */,
        (char)0x2ec4 /* '⻄' */,
        (char)0x2ec5 /* '⻅' */,
        (char)0x2ec8 /* '⻈' */,
        (char)0x2ec9 /* '⻉' */,
        (char)0x2ecb /* '⻋' */,
        (char)0x2ecd /* '⻍' */,
        (char)0x2ece /* '⻎' */,
        (char)0x2ed0 /* '⻐' */,
        (char)0x2ed1 /* '⻑' */,
        (char)0x2ed2 /* '⻒' */,
        (char)0x2ed3 /* '⻓' */,
        (char)0x2ed4 /* '⻔' */,
        (char)0x2ed6 /* '⻖' */,
        (char)0x2ed8 /* '⻘' */,
        (char)0x2ed9 /* '⻙' */,
        (char)0x2eda /* '⻚' */,
        (char)0x2edb /* '⻛' */,
        (char)0x2edc /* '⻜' */,
        (char)0x2edd /* '⻝' */,
        (char)0x2edf /* '⻟' */,
        (char)0x2ee0 /* '⻠' */,
        (char)0x2ee2 /* '⻢' */,
        (char)0x2ee3 /* '⻣' */,
        (char)0x2ee4 /* '⻤' */,
        (char)0x2ee5 /* '⻥' */,
        (char)0x2ee6 /* '⻦' */,
        (char)0x2ee7 /* '⻧' */,
        (char)0x2ee8 /* '⻨' */,
        (char)0x2ee9 /* '⻩' */,
        (char)0x2eea /* '⻪' */,
        (char)0x2eeb /* '⻫' */,
        (char)0x2eec /* '⻬' */,
        (char)0x2eed /* '⻭' */,
        (char)0x2eee /* '⻮' */,
        (char)0x2eef /* '⻯' */,
        (char)0x2ef0 /* '⻰' */,
        (char)0x2ef1 /* '⻱' */,
        (char)0x2ef2 /* '⻲' */,
        (char)0x2ef3 /* '⻳' */,
        (char)0x2f00 /* '⼀' */,
        (char)0x2f01 /* '⼁' */,
        (char)0x2f02 /* '⼂' */,
        (char)0x2f03 /* '⼃' */,
        (char)0x2f04 /* '⼄' */,
        (char)0x2f05 /* '⼅' */,
        (char)0x2f06 /* '⼆' */,
        (char)0x2f07 /* '⼇' */,
        (char)0x2f08 /* '⼈' */,
        (char)0x2f09 /* '⼉' */,
        (char)0x2f0a /* '⼊' */,
        (char)0x2f0b /* '⼋' */,
        (char)0x2f0c /* '⼌' */,
        (char)0x2f0d /* '⼍' */,
        (char)0x2f0e /* '⼎' */,
        (char)0x2f0f /* '⼏' */,
        (char)0x2f10 /* '⼐' */,
        (char)0x2f11 /* '⼑' */,
        (char)0x2f12 /* '⼒' */,
        (char)0x2f13 /* '⼓' */,
        (char)0x2f14 /* '⼔' */,
        (char)0x2f15 /* '⼕' */,
        (char)0x2f16 /* '⼖' */,
        (char)0x2f17 /* '⼗' */,
        (char)0x2f18 /* '⼘' */,
        (char)0x2f19 /* '⼙' */,
        (char)0x2f1a /* '⼚' */,
        (char)0x2f1b /* '⼛' */,
        (char)0x2f1c /* '⼜' */,
        (char)0x2f1d /* '⼝' */,
        (char)0x2f1e /* '⼞' */,
        (char)0x2f1f /* '⼟' */,
        (char)0x2f20 /* '⼠' */,
        (char)0x2f21 /* '⼡' */,
        (char)0x2f22 /* '⼢' */,
        (char)0x2f23 /* '⼣' */,
        (char)0x2f24 /* '⼤' */,
        (char)0x2f25 /* '⼥' */,
        (char)0x2f26 /* '⼦' */,
        (char)0x2f27 /* '⼧' */,
        (char)0x2f28 /* '⼨' */,
        (char)0x2f29 /* '⼩' */,
        (char)0x2f2a /* '⼪' */,
        (char)0x2f2b /* '⼫' */,
        (char)0x2f2c /* '⼬' */,
        (char)0x2f2d /* '⼭' */,
        (char)0x2f2e /* '⼮' */,
        (char)0x2f2f /* '⼯' */,
        (char)0x2f30 /* '⼰' */,
        (char)0x2f31 /* '⼱' */,
        (char)0x2f32 /* '⼲' */,
        (char)0x2f33 /* '⼳' */,
        (char)0x2f34 /* '⼴' */,
        (char)0x2f35 /* '⼵' */,
        (char)0x2f36 /* '⼶' */,
        (char)0x2f37 /* '⼷' */,
        (char)0x2f38 /* '⼸' */,
        (char)0x2f39 /* '⼹' */,
        (char)0x2f3a /* '⼺' */,
        (char)0x2f3b /* '⼻' */,
        (char)0x2f3c /* '⼼' */,
        (char)0x2f3d /* '⼽' */,
        (char)0x2f3e /* '⼾' */,
        (char)0x2f3f /* '⼿' */,
        (char)0x2f40 /* '⽀' */,
        (char)0x2f41 /* '⽁' */,
        (char)0x2f42 /* '⽂' */,
        (char)0x2f43 /* '⽃' */,
        (char)0x2f44 /* '⽄' */,
        (char)0x2f45 /* '⽅' */,
        (char)0x2f46 /* '⽆' */,
        (char)0x2f47 /* '⽇' */,
        (char)0x2f48 /* '⽈' */,
        (char)0x2f49 /* '⽉' */,
        (char)0x2f4a /* '⽊' */,
        (char)0x2f4b /* '⽋' */,
        (char)0x2f4c /* '⽌' */,
        (char)0x2f4d /* '⽍' */,
        (char)0x2f4e /* '⽎' */,
        (char)0x2f4f /* '⽏' */,
        (char)0x2f50 /* '⽐' */,
        (char)0x2f51 /* '⽑' */,
        (char)0x2f52 /* '⽒' */,
        (char)0x2f53 /* '⽓' */,
        (char)0x2f54 /* '⽔' */,
        (char)0x2f55 /* '⽕' */,
        (char)0x2f56 /* '⽖' */,
        (char)0x2f57 /* '⽗' */,
        (char)0x2f58 /* '⽘' */,
        (char)0x2f59 /* '⽙' */,
        (char)0x2f5a /* '⽚' */,
        (char)0x2f5b /* '⽛' */,
        (char)0x2f5c /* '⽜' */,
        (char)0x2f5d /* '⽝' */,
        (char)0x2f5e /* '⽞' */,
        (char)0x2f5f /* '⽟' */,
        (char)0x2f60 /* '⽠' */,
        (char)0x2f61 /* '⽡' */,
        (char)0x2f62 /* '⽢' */,
        (char)0x2f63 /* '⽣' */,
        (char)0x2f64 /* '⽤' */,
        (char)0x2f65 /* '⽥' */,
        (char)0x2f66 /* '⽦' */,
        (char)0x2f67 /* '⽧' */,
        (char)0x2f68 /* '⽨' */,
        (char)0x2f69 /* '⽩' */,
        (char)0x2f6a /* '⽪' */,
        (char)0x2f6b /* '⽫' */,
        (char)0x2f6c /* '⽬' */,
        (char)0x2f6d /* '⽭' */,
        (char)0x2f6e /* '⽮' */,
        (char)0x2f6f /* '⽯' */,
        (char)0x2f70 /* '⽰' */,
        (char)0x2f71 /* '⽱' */,
        (char)0x2f72 /* '⽲' */,
        (char)0x2f73 /* '⽳' */,
        (char)0x2f74 /* '⽴' */,
        (char)0x2f75 /* '⽵' */,
        (char)0x2f76 /* '⽶' */,
        (char)0x2f77 /* '⽷' */,
        (char)0x2f78 /* '⽸' */,
        (char)0x2f79 /* '⽹' */,
        (char)0x2f7a /* '⽺' */,
        (char)0x2f7b /* '⽻' */,
        (char)0x2f7c /* '⽼' */,
        (char)0x2f7d /* '⽽' */,
        (char)0x2f7e /* '⽾' */,
        (char)0x2f7f /* '⽿' */,
        (char)0x2f80 /* '⾀' */,
        (char)0x2f81 /* '⾁' */,
        (char)0x2f82 /* '⾂' */,
        (char)0x2f83 /* '⾃' */,
        (char)0x2f84 /* '⾄' */,
        (char)0x2f85 /* '⾅' */,
        (char)0x2f86 /* '⾆' */,
        (char)0x2f87 /* '⾇' */,
        (char)0x2f88 /* '⾈' */,
        (char)0x2f89 /* '⾉' */,
        (char)0x2f8a /* '⾊' */,
        (char)0x2f8b /* '⾋' */,
        (char)0x2f8c /* '⾌' */,
        (char)0x2f8d /* '⾍' */,
        (char)0x2f8e /* '⾎' */,
        (char)0x2f8f /* '⾏' */,
        (char)0x2f90 /* '⾐' */,
        (char)0x2f91 /* '⾑' */,
        (char)0x2f92 /* '⾒' */,
        (char)0x2f93 /* '⾓' */,
        (char)0x2f94 /* '⾔' */,
        (char)0x2f95 /* '⾕' */,
        (char)0x2f96 /* '⾖' */,
        (char)0x2f97 /* '⾗' */,
        (char)0x2f98 /* '⾘' */,
        (char)0x2f99 /* '⾙' */,
        (char)0x2f9a /* '⾚' */,
        (char)0x2f9b /* '⾛' */,
        (char)0x2f9c /* '⾜' */,
        (char)0x2f9d /* '⾝' */,
        (char)0x2f9e /* '⾞' */,
        (char)0x2f9f /* '⾟' */,
        (char)0x2fa0 /* '⾠' */,
        (char)0x2fa1 /* '⾡' */,
        (char)0x2fa2 /* '⾢' */,
        (char)0x2fa3 /* '⾣' */,
        (char)0x2fa4 /* '⾤' */,
        (char)0x2fa5 /* '⾥' */,
        (char)0x2fa6 /* '⾦' */,
        (char)0x2fa7 /* '⾧' */,
        (char)0x2fa8 /* '⾨' */,
        (char)0x2fa9 /* '⾩' */,
        (char)0x2faa /* '⾪' */,
        (char)0x2fab /* '⾫' */,
        (char)0x2fac /* '⾬' */,
        (char)0x2fad /* '⾭' */,
        (char)0x2fae /* '⾮' */,
        (char)0x2faf /* '⾯' */,
        (char)0x2fb0 /* '⾰' */,
        (char)0x2fb1 /* '⾱' */,
        (char)0x2fb2 /* '⾲' */,
        (char)0x2fb3 /* '⾳' */,
        (char)0x2fb4 /* '⾴' */,
        (char)0x2fb5 /* '⾵' */,
        (char)0x2fb6 /* '⾶' */,
        (char)0x2fb7 /* '⾷' */,
        (char)0x2fb8 /* '⾸' */,
        (char)0x2fb9 /* '⾹' */,
        (char)0x2fba /* '⾺' */,
        (char)0x2fbb /* '⾻' */,
        (char)0x2fbc /* '⾼' */,
        (char)0x2fbd /* '⾽' */,
        (char)0x2fbe /* '⾾' */,
        (char)0x2fbf /* '⾿' */,
        (char)0x2fc0 /* '⿀' */,
        (char)0x2fc1 /* '⿁' */,
        (char)0x2fc2 /* '⿂' */,
        (char)0x2fc3 /* '⿃' */,
        (char)0x2fc4 /* '⿄' */,
        (char)0x2fc5 /* '⿅' */,
        (char)0x2fc6 /* '⿆' */,
        (char)0x2fc7 /* '⿇' */,
        (char)0x2fc8 /* '⿈' */,
        (char)0x2fc9 /* '⿉' */,
        (char)0x2fca /* '⿊' */,
        (char)0x2fcb /* '⿋' */,
        (char)0x2fcc /* '⿌' */,
        (char)0x2fcd /* '⿍' */,
        (char)0x2fce /* '⿎' */,
        (char)0x2fcf /* '⿏' */,
        (char)0x2fd0 /* '⿐' */,
        (char)0x2fd1 /* '⿑' */,
        (char)0x2fd2 /* '⿒' */,
        (char)0x2fd3 /* '⿓' */,
        (char)0x2fd4 /* '⿔' */,
        (char)0x2fd5 /* '⿕' */,
        };

    public static readonly IEnumerable<char> StrangeCharList2 = new char[] {
        (char)0xF06C /* 箇条書きの中黒点 (大) */,
        (char)0xF09F /* 箇条書きの中黒点 (小) */,
        (char)0x2022, // 中黒 (大)
        (char)0x00B7, // 中黒 (小)
        (char)0x0387, // 中黒 (小)
        (char)0x2219, // 中黒 (小)
        (char)0x22C5, // 中黒 (小)
        (char)0x30FB, // 中黒 (小)
        (char)0xFF65, // 中黒 (小)
        (char)0xF06E, // ■
        (char)0xF0B2, // □
        (char)0xF0FC, // ✓
        (char)0xF0D8, // ➢
        (char)0xf075, // ◆
        (char)0x00A5, // 円記号 (¥)
        };


    public static readonly IEnumerable<char> NormalCharList = new char[] {
        (char)0x4e5a /* '乚' */,
        (char)0x4ebb /* '亻' */,
        (char)0x5202 /* '刂' */,
        (char)0x535c /* '卜' */,
        (char)0x353e /* '㔾' */,
        (char)0x5c23 /* '尣' */,
        (char)0x5c22 /* '尢' */,
        (char)0x5c23 /* '尣' */,
        (char)0x5df3 /* '巳' */,
        (char)0x5e7a /* '幺' */,
        (char)0x5f51 /* '彑' */,
        (char)0x5fc4 /* '忄' */,
        (char)0x624c /* '扌' */,
        (char)0x6535 /* '攵' */,
        (char)0x65e1 /* '旡' */,
        (char)0x6b7a /* '歺' */,
        (char)0x6bcd /* '母' */,
        (char)0x6c11 /* '民' */,
        (char)0x6c35 /* '氵' */,
        (char)0x6c3a /* '氺' */,
        (char)0x706c /* '灬' */,
        (char)0x722b /* '爫' */,
        (char)0x722b /* '爫' */,
        (char)0x4e2c /* '丬' */,
        (char)0x72ad /* '犭' */,
        (char)0x738b /* '王' */,
        (char)0x758b /* '疋' */,
        (char)0x76ee /* '目' */,
        (char)0x793a /* '示' */,
        (char)0x793b /* '礻' */,
        (char)0x7cf9 /* '糹' */,
        (char)0x7e9f /* '纟' */,
        (char)0x7f53 /* '罓' */,
        (char)0x7f52 /* '罒' */,
        (char)0x7f8b /* '羋' */,
        (char)0x8002 /* '耂' */,
        (char)0x8080 /* '肀' */,
        (char)0x81fc /* '臼' */,
        (char)0x8279 /* '艹' */,
        (char)0x8279 /* '艹' */,
        (char)0x8279 /* '艹' */,
        (char)0x864e /* '虎' */,
        (char)0x8864 /* '衤' */,
        (char)0x8980 /* '覀' */,
        (char)0x897f /* '西' */,
        (char)0x89c1 /* '见' */,
        (char)0x8ba0 /* '讠' */,
        (char)0x8d1d /* '贝' */,
        (char)0x8f66 /* '车' */,
        (char)0x8fb6 /* '辶' */,
        (char)0x8fb6 /* '辶' */,
        (char)0x9485 /* '钅' */,
        (char)0x9577 /* '長' */,
        (char)0x9578 /* '镸' */,
        (char)0x957f /* '长' */,
        (char)0x95e8 /* '门' */,
        (char)0x961d /* '阝' */,
        (char)0x9752 /* '青' */,
        (char)0x97e6 /* '韦' */,
        (char)0x9875 /* '页' */,
        (char)0x98ce /* '风' */,
        (char)0x98de /* '飞' */,
        (char)0x98df /* '食' */,
        (char)0x98e0 /* '飠' */,
        (char)0x9963 /* '饣' */,
        (char)0x9a6c /* '马' */,
        (char)0x9aa8 /* '骨' */,
        (char)0x9b3c /* '鬼' */,
        (char)0x9c7c /* '鱼' */,
        (char)0x9e1f /* '鸟' */,
        (char)0x5364 /* '卤' */,
        (char)0x9ea6 /* '麦' */,
        (char)0x9ec4 /* '黄' */,
        (char)0x9efe /* '黾' */,
        (char)0x6589 /* '斉' */,
        (char)0x9f50 /* '齐' */,
        (char)0x6b6f /* '歯' */,
        (char)0x9f7f /* '齿' */,
        (char)0x7adc /* '竜' */,
        (char)0x9f99 /* '龙' */,
        (char)0x9f9c /* '龜' */,
        (char)0x4e80 /* '亀' */,
        (char)0x9f9f /* '龟' */,
        (char)0x4e00 /* '一' */,
        (char)0x4e28 /* '丨' */,
        (char)0x4e36 /* '丶' */,
        (char)0x4e3f /* '丿' */,
        (char)0x4e59 /* '乙' */,
        (char)0x4e85 /* '亅' */,
        (char)0x4e8c /* '二' */,
        (char)0x4ea0 /* '亠' */,
        (char)0x4eba /* '人' */,
        (char)0x513f /* '儿' */,
        (char)0x5165 /* '入' */,
        (char)0x516b /* '八' */,
        (char)0x5182 /* '冂' */,
        (char)0x5196 /* '冖' */,
        (char)0x51ab /* '冫' */,
        (char)0x51e0 /* '几' */,
        (char)0x51f5 /* '凵' */,
        (char)0x5200 /* '刀' */,
        (char)0x529b /* '力' */,
        (char)0x52f9 /* '勹' */,
        (char)0x5315 /* '匕' */,
        (char)0x531a /* '匚' */,
        (char)0x5338 /* '匸' */,
        (char)0x5341 /* '十' */,
        (char)0x535c /* '卜' */,
        (char)0x5369 /* '卩' */,
        (char)0x5382 /* '厂' */,
        (char)0x53b6 /* '厶' */,
        (char)0x53c8 /* '又' */,
        (char)0x53e3 /* '口' */,
        (char)0x56d7 /* '囗' */,
        (char)0x571f /* '土' */,
        (char)0x58eb /* '士' */,
        (char)0x5902 /* '夂' */,
        (char)0x590a /* '夊' */,
        (char)0x5915 /* '夕' */,
        (char)0x5927 /* '大' */,
        (char)0x5973 /* '女' */,
        (char)0x5b50 /* '子' */,
        (char)0x5b80 /* '宀' */,
        (char)0x5bf8 /* '寸' */,
        (char)0x5c0f /* '小' */,
        (char)0x5c22 /* '尢' */,
        (char)0x5c38 /* '尸' */,
        (char)0x5c6e /* '屮' */,
        (char)0x5c71 /* '山' */,
        (char)0x5ddb /* '巛' */,
        (char)0x5de5 /* '工' */,
        (char)0x5df1 /* '己' */,
        (char)0x5dfe /* '巾' */,
        (char)0x5e72 /* '干' */,
        (char)0x5e7a /* '幺' */,
        (char)0x5e7f /* '广' */,
        (char)0x5ef4 /* '廴' */,
        (char)0x5efe /* '廾' */,
        (char)0x5f0b /* '弋' */,
        (char)0x5f13 /* '弓' */,
        (char)0x5f50 /* '彐' */,
        (char)0x5f61 /* '彡' */,
        (char)0x5f73 /* '彳' */,
        (char)0x5fc3 /* '心' */,
        (char)0x6208 /* '戈' */,
        (char)0x6236 /* '戶' */,
        (char)0x624b /* '手' */,
        (char)0x652f /* '支' */,
        (char)0x6534 /* '攴' */,
        (char)0x6587 /* '文' */,
        (char)0x6597 /* '斗' */,
        (char)0x65a4 /* '斤' */,
        (char)0x65b9 /* '方' */,
        (char)0x65e0 /* '无' */,
        (char)0x65e5 /* '日' */,
        (char)0x66f0 /* '曰' */,
        (char)0x6708 /* '月' */,
        (char)0x6728 /* '木' */,
        (char)0x6b20 /* '欠' */,
        (char)0x6b62 /* '止' */,
        (char)0x6b79 /* '歹' */,
        (char)0x6bb3 /* '殳' */,
        (char)0x6bcb /* '毋' */,
        (char)0x6bd4 /* '比' */,
        (char)0x6bdb /* '毛' */,
        (char)0x6c0f /* '氏' */,
        (char)0x6c14 /* '气' */,
        (char)0x6c34 /* '水' */,
        (char)0x706b /* '火' */,
        (char)0x722a /* '爪' */,
        (char)0x7236 /* '父' */,
        (char)0x723b /* '爻' */,
        (char)0x723f /* '爿' */,
        (char)0x7247 /* '片' */,
        (char)0x7259 /* '牙' */,
        (char)0x725b /* '牛' */,
        (char)0x72ac /* '犬' */,
        (char)0x7384 /* '玄' */,
        (char)0x7389 /* '玉' */,
        (char)0x74dc /* '瓜' */,
        (char)0x74e6 /* '瓦' */,
        (char)0x7518 /* '甘' */,
        (char)0x751f /* '生' */,
        (char)0x7528 /* '用' */,
        (char)0x7530 /* '田' */,
        (char)0x758b /* '疋' */,
        (char)0x7592 /* '疒' */,
        (char)0x7676 /* '癶' */,
        (char)0x767d /* '白' */,
        (char)0x76ae /* '皮' */,
        (char)0x76bf /* '皿' */,
        (char)0x76ee /* '目' */,
        (char)0x77db /* '矛' */,
        (char)0x77e2 /* '矢' */,
        (char)0x77f3 /* '石' */,
        (char)0x793a /* '示' */,
        (char)0x79b8 /* '禸' */,
        (char)0x79be /* '禾' */,
        (char)0x7a74 /* '穴' */,
        (char)0x7acb /* '立' */,
        (char)0x7af9 /* '竹' */,
        (char)0x7c73 /* '米' */,
        (char)0x7cf8 /* '糸' */,
        (char)0x7f36 /* '缶' */,
        (char)0x7f51 /* '网' */,
        (char)0x7f8a /* '羊' */,
        (char)0x7fbd /* '羽' */,
        (char)0x8001 /* '老' */,
        (char)0x800c /* '而' */,
        (char)0x8012 /* '耒' */,
        (char)0x8033 /* '耳' */,
        (char)0x807f /* '聿' */,
        (char)0x8089 /* '肉' */,
        (char)0x81e3 /* '臣' */,
        (char)0x81ea /* '自' */,
        (char)0x81f3 /* '至' */,
        (char)0x81fc /* '臼' */,
        (char)0x820c /* '舌' */,
        (char)0x821b /* '舛' */,
        (char)0x821f /* '舟' */,
        (char)0x826e /* '艮' */,
        (char)0x8272 /* '色' */,
        (char)0x8278 /* '艸' */,
        (char)0x864d /* '虍' */,
        (char)0x866b /* '虫' */,
        (char)0x8840 /* '血' */,
        (char)0x884c /* '行' */,
        (char)0x8863 /* '衣' */,
        (char)0x897e /* '襾' */,
        (char)0x898b /* '見' */,
        (char)0x89d2 /* '角' */,
        (char)0x8a00 /* '言' */,
        (char)0x8c37 /* '谷' */,
        (char)0x8c46 /* '豆' */,
        (char)0x8c55 /* '豕' */,
        (char)0x8c78 /* '豸' */,
        (char)0x8c9d /* '貝' */,
        (char)0x8d64 /* '赤' */,
        (char)0x8d70 /* '走' */,
        (char)0x8db3 /* '足' */,
        (char)0x8eab /* '身' */,
        (char)0x8eca /* '車' */,
        (char)0x8f9b /* '辛' */,
        (char)0x8fb0 /* '辰' */,
        (char)0x8fb5 /* '辵' */,
        (char)0x9091 /* '邑' */,
        (char)0x9149 /* '酉' */,
        (char)0x91c6 /* '釆' */,
        (char)0x91cc /* '里' */,
        (char)0x91d1 /* '金' */,
        (char)0x9577 /* '長' */,
        (char)0x9580 /* '門' */,
        (char)0x961c /* '阜' */,
        (char)0x96b6 /* '隶' */,
        (char)0x96b9 /* '隹' */,
        (char)0x96e8 /* '雨' */,
        (char)0x9751 /* '靑' */,
        (char)0x975e /* '非' */,
        (char)0x9762 /* '面' */,
        (char)0x9769 /* '革' */,
        (char)0x97cb /* '韋' */,
        (char)0x97ed /* '韭' */,
        (char)0x97f3 /* '音' */,
        (char)0x9801 /* '頁' */,
        (char)0x98a8 /* '風' */,
        (char)0x98db /* '飛' */,
        (char)0x98df /* '食' */,
        (char)0x9996 /* '首' */,
        (char)0x9999 /* '香' */,
        (char)0x99ac /* '馬' */,
        (char)0x9aa8 /* '骨' */,
        (char)0x9ad8 /* '高' */,
        (char)0x9adf /* '髟' */,
        (char)0x9b25 /* '鬥' */,
        (char)0x9b2f /* '鬯' */,
        (char)0x9b32 /* '鬲' */,
        (char)0x9b3c /* '鬼' */,
        (char)0x9b5a /* '魚' */,
        (char)0x9ce5 /* '鳥' */,
        (char)0x9e75 /* '鹵' */,
        (char)0x9e7f /* '鹿' */,
        (char)0x9ea5 /* '麥' */,
        (char)0x9ebb /* '麻' */,
        (char)0x9ec3 /* '黃' */,
        (char)0x9ecd /* '黍' */,
        (char)0x9ed1 /* '黑' */,
        (char)0x9ef9 /* '黹' */,
        (char)0x9efd /* '黽' */,
        (char)0x9f0e /* '鼎' */,
        (char)0x9f13 /* '鼓' */,
        (char)0x9f20 /* '鼠' */,
        (char)0x9f3b /* '鼻' */,
        (char)0x9f4a /* '齊' */,
        (char)0x9f52 /* '齒' */,
        (char)0x9f8d /* '龍' */,
        (char)0x9f9c /* '龜' */,
        (char)0x9fa0 /* '龠' */,
        };

    public static readonly IEnumerable<char> NormalCharList2 = new char[] {
        '●' /* 箇条書きの中黒点 (大) */,
        '・' /* 箇条書きの中黒点 (小) */,
        '・', // 中黒 (小)
        '・', // 中黒 (小)
        '・', // 中黒 (小)
        '・', // 中黒 (小)
        '・', // 中黒 (小)
        '・', // 中黒 (小)
        '・', // 中黒 (小)
        '■', // ■
        '□', // □
        '✓', // ✓
        '➢', // ➢
        '◆', // ◆
        '\\', // バックスラッシュ (円記号は必ずバックスラッシュに)
    };

    public static readonly string StrangeCharArrayStr;
    public static readonly string NormalCharArrayStr;

    public static readonly string StrangeCharArrayStr2;
    public static readonly string NormalCharArrayStr2;

    static UnicodeStdKangxiMapUtil()
    {
        StrangeCharArrayStr = new string(StrangeCharList.ToArray());
        NormalCharArrayStr = new string(NormalCharList.ToArray());

        StrangeCharArrayStr2 = new string(StrangeCharList2.ToArray());
        NormalCharArrayStr2 = new string(NormalCharList2.ToArray());
    }

    public static char StrangeToNormal(char c)
    {
        int i = StrangeCharArrayStr.IndexOf(c);
        if (i != -1)
        {
            return NormalCharArrayStr[i];
        }
        i = StrangeCharArrayStr2.IndexOf(c);
        if (i != -1)
        {
            return NormalCharArrayStr2[i];
        }
        return c;
    }

    public static string StrangeToNormal(string str)
    {
        char[] a = str.ToCharArray();
        int i, len;
        len = a.Length;
        for (i = 0; i < len; i++)
        {
            a[i] = StrangeToNormal(a[i]);
        }
        return new string(a);
    }

    public static char NormalToStrange(char c)
    {
        int i = NormalCharArrayStr.IndexOf(c);
        if (i == -1) return StrangeCharArrayStr[i];
        return c;
    }

    public static string NormalToStrange(string str)
    {
        char[] a = str.ToCharArray();
        int i, len;
        len = a.Length;
        for (i = 0; i < len; i++)
        {
            a[i] = NormalToStrange(a[i]);
        }
        return new string(a);
    }
}

// Unicode の制御文字置換ユーティティ
public static class UnicodeControlCodesNormalizeUtil
{
    // 普通のスペース ' ' と見た目は同じだが、文字コードが異なる異字の配列
    public static readonly IEnumerable<char> Strange_Space_CharList = new char[]
    {
            (char)0x00A0 /* NO-BREAK SPACE (改行を許さない空白) */,
            (char)0x1680 /* OGHAM SPACE MARK (オガム文字用の固定幅空白) */,
            (char)0x180E /* MONGOLIAN VOWEL SEPARATOR (モンゴル語の母音区切り、幅ゼロ空白) */,
            (char)0x2000 /* EN QUAD (活字の 1/2 em 幅空白) */,
            (char)0x2001 /* EM QUAD (活字の 1 em 幅空白) */,
            (char)0x2002 /* EN SPACE (en 幅空白) */,
            (char)0x2003 /* EM SPACE (em 幅空白) */,
            (char)0x2004 /* THREE-PER-EM SPACE (全角の 1/3 幅空白) */,
            (char)0x2005 /* FOUR-PER-EM SPACE (全角の 1/4 幅空白) */,
            (char)0x2006 /* SIX-PER-EM SPACE (全角の 1/6 幅空白) */,
            (char)0x2007 /* FIGURE SPACE (等幅数字用空白) */,
            (char)0x2008 /* PUNCTUATION SPACE (句読点幅空白) */,
            (char)0x2009 /* THIN SPACE (細い空白) */,
            (char)0x200A /* HAIR SPACE (極細の空白) */,
            (char)0x202F /* NARROW NO-BREAK SPACE (狭い改行禁止空白) */,
            (char)0x205F /* MEDIUM MATHEMATICAL SPACE (数式用中幅空白) */,
            (char)0x3164 /* HANGUL FILLER (ハングル用空白記号) */
    };

    // 普通の改行 '\n' と見た目は同じだが、文字コードが異なる異字の配列
    public static readonly IEnumerable<char> Strange_NewLine_CharList = new char[]
    {
            (char)0x000B /* LINE TABULATION (VT) (垂直タブ — 縦方向改行) */,
            (char)0x000C /* FORM FEED (FF) (改ページ制御コード) */,
            (char)0x0085 /* NEXT LINE (NEL) (Unicode の次行制御) */,
            (char)0x2028 /* LINE SEPARATOR (行区切り用改行) */,
            (char)0x2029 /* PARAGRAPH SEPARATOR (段落区切り用改行) */
    };

    // 見た目は全く何も表示されないが、文字コードとして 1 文字を消費する制御コード
    public static readonly IEnumerable<char> Strange_HiddenControl_CharList = new char[]
    {
            (char)0x00AD /* SOFT HYPHEN (SHY) (改行時のみ表示されるソフトハイフン) */,
            (char)0x200B /* ZERO WIDTH SPACE (幅ゼロの空白) */,
            (char)0x200C /* ZERO WIDTH NON-JOINER (ZWNJ) (合字を阻止する幅ゼロ制御) */,
            (char)0x200D /* ZERO WIDTH JOINER (ZWJ) (合字を強制する幅ゼロ制御) */,
            (char)0x200E /* LEFT-TO-RIGHT MARK (LRM) (左→右方向指定の幅ゼロ) */,
            (char)0x200F /* RIGHT-TO-LEFT MARK (RLM) (右→左方向指定の幅ゼロ) */,
            (char)0x202A /* LEFT-TO-RIGHT EMBEDDING (LRE) (左→右埋め込み開始制御) */,
            (char)0x202B /* RIGHT-TO-LEFT EMBEDDING (RLE) (右→左埋め込み開始制御) */,
            (char)0x202C /* POP DIRECTIONAL FORMATTING (PDF) (埋め込み／上書き終了制御) */,
            (char)0x202D /* LEFT-TO-RIGHT OVERRIDE (LRO) (左→右上書き開始制御) */,
            (char)0x202E /* RIGHT-TO-LEFT OVERRIDE (RLO) (右→左上書き開始制御) */,
            (char)0x2060 /* WORD JOINER (単語分割禁止の幅ゼロ制御) */,
            (char)0x2061 /* FUNCTION APPLICATION (数学関数適用 — 不可視) */,
            (char)0x2062 /* INVISIBLE TIMES (不可視の掛け算記号) */,
            (char)0x2063 /* INVISIBLE SEPARATOR (不可視の区切り記号) */,
            (char)0x2064 /* INVISIBLE PLUS (不可視の足し算記号) */,
            (char)0x2066 /* LEFT-TO-RIGHT ISOLATE (LRI) (左→右アイソレート開始) */,
            (char)0x2067 /* RIGHT-TO-LEFT ISOLATE (RLI) (右→左アイソレート開始) */,
            (char)0x2068 /* FIRST STRONG ISOLATE (FSI) (最初に強い方向のアイソレート開始) */,
            (char)0x2069 /* POP DIRECTIONAL ISOLATE (PDI) (アイソレート終了制御) */,
            (char)0xFEFF /* ZERO WIDTH NO-BREAK SPACE (BOM) (BOM としても用いられる幅ゼロ改行禁止空白) */,
            (char)0xFFF9 /* INTERLINEAR ANNOTATION ANCHOR (行間注記用アンカー) */,
            (char)0xFFFA /* INTERLINEAR ANNOTATION SEPARATOR (行間注記用セパレータ) */,
            (char)0xFFFB /* INTERLINEAR ANNOTATION TERMINATOR (行間注記用終端) */
    };

    // 見た目はハイフンに見えるが、ASCII コード '-' (U+002D) とは異なる異字の配列
    public static readonly IEnumerable<char> Strange_Hyphen_CharList = new char[]
    {
        (char)0x2010 /* HYPHEN (改行可能なハイフン) */,
        (char)0x2011 /* NON‑BREAKING HYPHEN (改行を許さないハイフン) */,
        (char)0x058A /* ARMENIAN HYPHEN (アルメニア語用ハイフン) */,
        (char)0x1400 /* CANADIAN SYLLABICS HYPHEN (カナダ先住民音節文字用ハイフン) */,
        (char)0x1806 /* MONGOLIAN TODO SOFT HYPHEN (モンゴル語トド文字のソフトハイフン) */,
        (char)0x00AD /* SOFT HYPHEN (表示されない可能性のあるソフトハイフン) */,
        (char)0x2012 /* FIGURE DASH (数字列用ダッシュ) */,
        (char)0x2013 /* EN DASH (範囲を示すエンダッシュ) */,
        (char)0x2014 /* EM DASH (文章挿入用エムダッシュ) */,
        (char)0x2015 /* HORIZONTAL BAR (水平バー、日本語組版で使用) */,
        (char)0x2E3A /* TWO‑EM DASH (二倍エムダッシュ、省略線) */,
        (char)0x2E3B /* THREE‑EM DASH (三倍エムダッシュ、省略線) */,
        (char)0x2212 /* MINUS SIGN (数式用マイナス記号) */,
        (char)0x2796 /* HEAVY MINUS SIGN (太字マイナス、装飾用) */,
        (char)0xFF0D /* FULLWIDTH HYPHEN‑MINUS (全角ハイフンマイナス) */,
        (char)0xFE63 /* SMALL HYPHEN‑MINUS (小型ハイフンマイナス) */,
        (char)0xFE58 /* SMALL EM DASH (小型エムダッシュ) */,
        (char)0xFE31 /* PRESENTATION FORM FOR VERTICAL EM DASH (縦書き用エムダッシュ) */,
        (char)0xFE32 /* PRESENTATION FORM FOR VERTICAL EN DASH (縦書き用エンダッシュ) */,
        (char)0x2043 /* HYPHEN BULLET (箇条書き用ハイフン) */,
        (char)0x2053 /* SWUNG DASH (波状ダッシュ、文章の省略に使用) */,
        (char)0x30A0 /* KATAKANA‑HIRAGANA DOUBLE HYPHEN (カタカナ・ひらがなダブルハイフン) */
    };

    // 見た目はパイプ '|' に見えるが、ASCII コード '|' (U+007C) とは異なる異字の配列
    public static readonly IEnumerable<char> Strange_Pipe_CharList = new char[]
    {
        (char)0x01C0 /* LATIN LETTER DENTAL CLICK (縦線に似たクリック音文字) */,
        (char)0x05C0 /* HEBREW PUNCTUATION PASEQ (ヘブライ語の区切り記号) */,
        (char)0x2223 /* DIVIDES (数学の整除記号) */,
        (char)0x23D0 /* VERTICAL LINE EXTENSION (縦線延長記号) */,
        (char)0x2758 /* LIGHT VERTICAL BAR (細い縦線) */,
        (char)0x2759 /* MEDIUM VERTICAL BAR (中太縦線) */,
        (char)0x275A /* HEAVY VERTICAL BAR (太い縦線) */,
        (char)0xFF5C /* FULLWIDTH VERTICAL LINE (全角縦線) */,
        (char)0xFE31 /* PRESENTATION FORM FOR VERTICAL EM DASH (縦書き用エムダッシュ) */,
        (char)0xFE32 /* PRESENTATION FORM FOR VERTICAL EN DASH (縦書き用エンダッシュ) */
    };

    // 見た目はプラス '+' に見えるが、ASCII コード '+' (U+002B) とは異なる異字の配列
    public static readonly IEnumerable<char> Strange_Plus_CharList = new char[]
    {
        (char)0xFE62 /* SMALL PLUS SIGN (小型プラス) */,
        (char)0xFF0B /* FULLWIDTH PLUS SIGN (全角プラス) */,
        (char)0x2795 /* HEAVY PLUS SIGN (太線プラス) */,
        (char)0x2295 /* CIRCLED PLUS (丸囲みプラス) */,
        (char)0x229E /* SQUARED PLUS (四角囲みプラス) */
    };

    // 見た目はスラッシュ '/' に見えるが、ASCII コード '/' (U+002F) とは異なる異字の配列
    public static readonly IEnumerable<char> Strange_Slash_CharList = new char[]
    {
        (char)0x2044 /* FRACTION SLASH (分数用スラッシュ) */,
        (char)0x2215 /* DIVISION SLASH (除算用スラッシュ) */,
        (char)0x2571 /* BOX DRAWINGS LIGHT DIAGONAL UPPER RIGHT TO LOWER LEFT (罫線用細斜線) */,
        (char)0x29F8 /* BIG SOLIDUS (大型スラッシュ) */,
        (char)0xFE68 /* SMALL SOLIDUS (小型スラッシュ) */,
        (char)0xFF0F /* FULLWIDTH SOLIDUS (全角スラッシュ) */
    };

    // 見た目はアスタリスク '*' に見えるが、ASCII コード '*' (U+002A) とは異なる異字の配列
    public static readonly IEnumerable<char> Strange_Asterisk_CharList = new char[]
    {
        (char)0x204E /* LOW ASTERISK (低位置アスタリスク) */,
        (char)0x2217 /* ASTERISK OPERATOR (数学用アスタリスク演算子) */,
        (char)0x2731 /* HEAVY ASTERISK (太線アスタリスク) */,
        (char)0xFE61 /* SMALL ASTERISK (小型アスタリスク) */,
        (char)0xFF0A /* FULLWIDTH ASTERISK (全角アスタリスク) */
    };

    public static readonly string Strange_Space_CharsStr;
    public static readonly string Strange_NewLine_CharsStr;
    public static readonly string Strange_HiddenControl_CharsStr;
    public static readonly string Strange_Hyphon_CharsStr;
    public static readonly string Strange_Pipe_CharsStr;
    public static readonly string Strange_Plus_CharsStr;
    public static readonly string Strange_Slash_CharsStr;
    public static readonly string Strange_Asterisk_CharsStr;

    static UnicodeControlCodesNormalizeUtil()
    {
        Strange_Space_CharsStr = new string(Strange_Space_CharList.ToArray());
        Strange_NewLine_CharsStr = new string(Strange_NewLine_CharList.ToArray());
        Strange_HiddenControl_CharsStr = new string(Strange_HiddenControl_CharList.ToArray());
        Strange_Hyphon_CharsStr = new string(Strange_Hyphen_CharList.ToArray());
        Strange_Pipe_CharsStr = new string(Strange_Pipe_CharList.ToArray());
        Strange_Plus_CharsStr = new string(Strange_Plus_CharList.ToArray());
        Strange_Slash_CharsStr = new string(Strange_Slash_CharList.ToArray());
        Strange_Asterisk_CharsStr = new string(Strange_Slash_CharList.ToArray());
    }

    public static string Normalize(string str)
    {
        StringBuilder sb = new StringBuilder(str.Length);

        foreach (char src in str)
        {
            char dst;
            if (src == '\t' || src == ' ' || src == '　' || src == '\r' || src == '\n')
            {
                dst = src;
            }
            else if (src == '\b' || src == (char)0x007f)
            {
                dst = ' ';
            }
            else if (Strange_Space_CharsStr.Contains(src))
            {
                dst = ' ';
            }
            else if (Strange_NewLine_CharsStr.Contains(src))
            {
                dst = '\n';
            }
            else if (Strange_HiddenControl_CharList.Contains(src))
            {
                dst = (char)0;
            }
            else if (Strange_Hyphon_CharsStr.Contains(src))
            {
                dst = '-';
            }
            else if (Strange_Pipe_CharsStr.Contains(src))
            {
                dst = '|';
            }
            else if (Strange_Plus_CharsStr.Contains(src))
            {
                dst = '+';
            }
            else if (Strange_Slash_CharsStr.Contains(src))
            {
                dst = '/';
            }
            else if (Strange_Asterisk_CharsStr.Contains(src))
            {
                dst = '*';
            }
            else if (char.IsControl(src))
            {
                dst = (char)0;
            }
            else if (char.IsWhiteSpace(src))
            {
                dst = ' ';
            }
            else
            {
                dst = src;
            }

            if (dst != (char)0)
            {
                sb.Append(dst);
            }
        }

        return sb.ToString();
    }
}



