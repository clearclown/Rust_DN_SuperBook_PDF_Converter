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
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;
using System.IO;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using System.Net;

namespace IPA.Cores.Basic;

public class AmbiguousMasterData<T> : AmbiguousSearch<T> where T : class
{
    public AmbiguousMasterData(string body, Func<string, T?> parser, bool allowWildcard = false) : base(allowWildcard)
    {
        string[] lines = body._GetLines();

        foreach (string line in lines)
        {
            string line2 = line._StripCommentFromLine()._NonNullTrim();

            if (line2._IsFilled())
            {
                if (line2._GetKeyAndValue(out string key, out string value))
                {
                    try
                    {
                        T? t = parser(value);

                        if (t != null)
                        {
                            this.Add(key, t);
                        }
                    }
                    catch (Exception ex)
                    {
                        ex._Debug();
                    }
                }
            }
        }
    }
}

public static class MasterData
{
    public static AmbiguousMasterData<Tuple<string, string>> MimeToFasIcon => MimeToFasIconSingleton;
    static readonly Singleton<AmbiguousMasterData<Tuple<string, string>>> MimeToFasIconSingleton =
        new Singleton<AmbiguousMasterData<Tuple<string, string>>>(() => new AmbiguousMasterData<Tuple<string, string>>(
            allowWildcard: true,
            body: CoresRes["MasterData/MimeToFasIcon/MimeToFasIconList.txt"].String,
            parser: line =>
            {
                string[] tokens = line._Split(StringSplitOptions.RemoveEmptyEntries, " ", "　", "\t");
                if (tokens.Length == 2)
                {
                    return new Tuple<string, string>(tokens[0], tokens[1]);
                }
                return null;
            }));

    public static MimeList ExtensionToMime => ExtensionToMimeSingleton;
    static readonly Singleton<MimeList> ExtensionToMimeSingleton =
        new Singleton<MimeList>(() => new MimeList(
            body: CoresRes["MasterData/MimeLookup/MimeList.txt"].String
            ));

    public static PublicSuffixList DomainSuffixList => PublicSuffixListSingleton;
    static readonly Singleton<PublicSuffixList> PublicSuffixListSingleton =
        new Singleton<PublicSuffixList>(() => new PublicSuffixList(
            body: CoresRes["MasterData/DomainPublicSuffixList/public_suffix_list.txt"].String
            ));

    public static PrefectureList Prefectures => PrefectureListSingleton;
    static readonly Singleton<PrefectureList> PrefectureListSingleton =
        new Singleton<PrefectureList>(() => new PrefectureList(
            body: CoresRes["MasterData/PrefectureList/PrefectureList.txt"].String
            ));

    public static NgnRouteInfoList NgnRouteInfoData => NgnRouteInfoListSingleton;
    static readonly Singleton<NgnRouteInfoList> NgnRouteInfoListSingleton =
        new Singleton<NgnRouteInfoList>(() => new NgnRouteInfoList(
            body: CoresRes["MasterData/NgnRouteInfo/NgnRouteInfo.txt"].String
            ));

    public static Tuple<string, string> GetFasIconFromExtension(string extensionOrMimeType)
    {
        if (extensionOrMimeType._InStr("/"))
        {
            // Mime type search
            var ret = MimeToFasIcon.SearchTopWithCache(extensionOrMimeType);
            if (ret != null) return ret;

            // Last resort
            return new Tuple<string, string>("fas", "fa-file-download");
        }
        else
        {
            // Extension search
            var mime = ExtensionToMime.Get(extensionOrMimeType);

            var ret = MimeToFasIcon.SearchTopWithCache(mime);
            if (ret != null) return ret;

            // Last resort
            return new Tuple<string, string>("fas", "fa-file-download");
        }
    }

    public class Prefecture
    {
        public int Number { get; }
        public string Kanji { get; }
        public string Kana { get; }
        public string English { get; }

        public Prefecture(int number, string kanji, string kana, string english)
        {
            Number = number;
            Kanji = kanji;
            Kana = kana;
            English = english;
        }
    }

    public class PrefectureList
    {
        public IEnumerable<Prefecture> List => _List;
        public IReadOnlyDictionary<string, Prefecture> ByKanji => _ByKanji;

        readonly List<Prefecture> _List = new List<Prefecture>();
        readonly Dictionary<string, Prefecture> _ByKanji = new Dictionary<string, Prefecture>(StrComparer.IgnoreCaseTrimComparer);

        public PrefectureList(string body)
        {
            string[] lines = body._GetLines(true, true);

            int number = 0;

            foreach (string line in lines)
            {
                string[] tokens = line._Split(StringSplitOptions.None, ',');
                if (tokens.Length == 3)
                {
                    Prefecture p = new Prefecture(++number, tokens[0], tokens[1], tokens[2]);

                    _List.Add(p);
                }
            }

            foreach (var p in _List)
            {
                this._ByKanji.TryAdd(p.Kanji, p);
            }
        }
    }

    public class NgnRouteInfo
    {
        public int Id { get; }
        public EasyIpAclRule Prefix { get; }
        public string Comment { get; }

        public NgnRouteInfo(int id, string prefixStr, string comment)
        {
            this.Id = id;
            this.Prefix = new EasyIpAclRule(prefixStr);
            this.Comment = comment._NonNullTrim().ToUpperInvariant();
        }
    }

    public class NgnRouteInfoList
    {
        public IReadOnlyList<NgnRouteInfo> RouteInfoList;

        public NgnRouteInfoList(string body)
        {
            List<NgnRouteInfo> list = new List<NgnRouteInfo>();
            this.RouteInfoList = list;

            string[] lines = body._GetLines();

            foreach (string line in lines)
            {
                if (Str.GetKeyAndValue(line, out string s1, out string s2, "#"))
                {
                    s1 = s1._NonNullTrim();
                    s2 = s2._NonNullTrim();
                    if (s1._IsFilled() && s2._IsFilled())
                    {
                        string[] tokens = s1._Split(StringSplitOptions.None, ",");
                        if (tokens.Length == 2)
                        {
                            try
                            {
                                NgnRouteInfo info = new NgnRouteInfo(tokens[0]._ToInt(), tokens[1], s2);

                                if (info.Prefix.SubnetLength >= 1)
                                {
                                    list.Add(info);
                                }
                            }
                            catch { }
                        }
                    }
                }
            }
        }

        public NgnRouteInfo? Lookup(IPAddress ipv6)
        {
            foreach (var item in this.RouteInfoList)
            {
                if (item.Prefix.IsMatch(ipv6))
                {
                    return item;
                }
            }

            return null;
        }

        public NgnRouteInfo? Lookup(string ipv6)
        {
            if (IPAddress.TryParse(ipv6, out IPAddress? ip))
            {
                return Lookup(ip);
            }
            return null;
        }
    }

    public class PublicSuffixList
    {
        readonly HashSet<string> SuffixList = new HashSet<string>(StrComparer.IgnoreCaseComparer);

        public PublicSuffixList(string body)
        {
            string[] lines = body._GetLines();

            foreach (string line in lines)
            {
                string line2 = line._StripCommentFromLine()._NonNullTrim();
                if (line2._IsFilled())
                {
                    string[] tokens = line2._Split(StringSplitOptions.RemoveEmptyEntries, ' ', '　', '\t');
                    if (tokens.Length == 1)
                    {
                        string suffix = tokens[0].ToLowerInvariant();

                        suffix = suffix._Split(StringSplitOptions.RemoveEmptyEntries, '.')._Combine(".");

                        if (suffix._IsFilled())
                        {
                            this.SuffixList.Add(suffix);
                        }
                    }
                }
            }
        }

        public bool ParseDomainBySuffixList(string fqdn, out string suffixTld, out string suffixTldPlusOneDomainLabel, out string hostnames)
        {
            if (fqdn._IsEmpty()) fqdn = "";

            string[] tokens = fqdn._Split(StringSplitOptions.RemoveEmptyEntries, '.').Reverse().ToArray();

            if (tokens.Length == 0)
            {
                suffixTld = "";
                suffixTldPlusOneDomainLabel = "";
                hostnames = "";
                return false;
            }

            for (int i = tokens.Length; i >= 0; i--)
            {
                string suffixTmp = tokens.Take(i).Reverse()._Combine(".");

                if (SuffixList.Contains(suffixTmp))
                {
                    suffixTld = suffixTmp;
                    suffixTldPlusOneDomainLabel = tokens.Take(Math.Min(i + 1, tokens.Length)).Reverse()._Combine(".");
                    hostnames = tokens.Skip(Math.Min(i + 1, tokens.Length)).Reverse()._Combine(".");
                    return true;
                }
            }

            suffixTld = tokens.Take(1).Reverse()._Combine(".");
            suffixTldPlusOneDomainLabel = tokens.Take(Math.Min(2, tokens.Length)).Reverse()._Combine(".");
            hostnames = tokens.Skip(Math.Min(2, tokens.Length)).Reverse()._Combine(".");

            return false;
        }
    }

    public class MimeList
    {
        Dictionary<string, string> ExtToMimeDictionary = new Dictionary<string, string>(StrComparer.IgnoreCaseComparer);

        public MimeList(string body)
        {
            string[] lines = body._GetLines();

            HashSetDictionary<string, string> extToMime = new HashSetDictionary<string, string>(StrComparer.IgnoreCaseComparer, StrComparer.IgnoreCaseComparer);
            HashSetDictionary<string, string> mimeToExt = new HashSetDictionary<string, string>(StrComparer.IgnoreCaseComparer, StrComparer.IgnoreCaseComparer);

            foreach (string line in lines)
            {
                string line2 = line._StripCommentFromLine(Consts.Strings.CommentStartStringForMimeList)._NonNullTrim();

                if (line2._IsFilled())
                {
                    if (line2._GetKeyAndValue(out string key, out string value))
                    {
                        key = key.ToLowerInvariant();
                        value = value.ToLowerInvariant();

                        if (key.StartsWith(".")) key = key.Substring(1);

                        if (key._IsFilled() && value._IsFilled())
                        {
                            extToMime.Add(key, value);
                            mimeToExt.Add(value, key);

                            if (line._InStr("#overwrite") || line._InStr("# overwrite"))
                            {
                                this.ExtToMimeDictionary.TryAdd(key, value);
                            }
                        }
                    }
                }
            }

            foreach (var extInfo in extToMime)
            {
                string? mime = extInfo.Value.OrderBy(x => mimeToExt[x].Count).FirstOrDefault();
                if (mime._IsFilled())
                {
                    this.ExtToMimeDictionary.TryAdd(extInfo.Key, mime);
                }
            }
        }

        public string Get(string ext, string defaultMimeType = Consts.MimeTypes.OctetStream)
        {
            if (ext.StartsWith("."))
            {
                ext = ext.Substring(1);
            }

            if (ExtToMimeDictionary.TryGetValue(ext, out string? ret))
            {
                return ret;
            }
            else
            {
                return defaultMimeType;
            }
        }
    }
}

