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
using System.Text;
using System.Collections.Generic;

using IPA.Cores.Basic;
using IPA.Cores.Basic.Legacy;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

namespace IPA.Cores.Basic.Legacy;

// Ini ファイルの読み込み
public class ReadIni
{
    // Ini ファイルのキャッシュ
    class IniCache
    {
        static Dictionary<string, IniCacheEntry> caches = new Dictionary<string, IniCacheEntry>();

        class IniCacheEntry
        {
            public DateTime LastUpdate { get; }
            public Dictionary<string, string> Datas { get; }

            public IniCacheEntry(DateTime lastUpdate, Dictionary<string, string> datas)
            {
                this.LastUpdate = lastUpdate;
                this.Datas = datas;
            }
        }

        public static Dictionary<string, string>? GetCache(string filename, DateTime lastUpdate)
        {
            lock (caches)
            {
                try
                {
                    IniCacheEntry e = caches[filename];
                    if (e.LastUpdate == lastUpdate || lastUpdate.Ticks == 0)
                    {
                        return e.Datas;
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
        }

        public static void AddCache(string filename, DateTime lastUpdate, Dictionary<string, string> datas)
        {
            lock (caches)
            {
                if (caches.ContainsKey(filename))
                {
                    caches.Remove(filename);
                }

                caches.Add(filename, new IniCacheEntry(lastUpdate, datas));
            }
        }
    }

    Dictionary<string, string>? datas;

    public bool Updated { get; private set; }

    public StrData this[string key]
    {
        get
        {
            string? s;
            try
            {
                lock (typeof(ReadIni))
                {
                    s = datas![key.ToUpperInvariant()];
                }
            }
            catch
            {
                s = null;
            }

            return new StrData(s);
        }
    }

    public string[] GetKeys()
    {
        List<string> ret = new List<string>();

        lock (typeof(ReadIni))
        {
            foreach (string s in datas!.Keys)
            {
                ret.Add(s);
            }
        }

        return ret.ToArray();
    }

    public ReadIni(string filename, bool expandIncludes = false)
    {
        if (expandIncludes == false)
        {
            init(null, filename);
        }
        else
        {
            string body = Lfs.ReadStringFromFile(IPA.Cores.Basic.Legacy.IO.InnerFilePath(filename), flags: FileFlags.ReadStr_ExpandIncludes);

            init(body._GetBytes_UTF8(), null);
        }
    }

    void init(byte[] data)
    {
        init(data, null);
    }
    void init(byte[]? data, string? filename)
    {
        Updated = false;

        lock (typeof(ReadIni))
        {
            string[] lines;
            string srcstr;
            DateTime lastUpdate = new DateTime(0);

            if (filename._IsFilled())
            {
                lastUpdate = IO.GetLastWriteTimeUtc(filename);

                datas = IniCache.GetCache(filename, lastUpdate);
            }

            if (datas == null)
            {
                if (data == null)
                {
                    try
                    {
                        data = Buf.ReadFromFile(filename!).ByteData;
                    }
                    catch
                    {
                        data = new byte[0];
                        datas = IniCache.GetCache(filename!, new DateTime());
                    }
                }

                if (datas == null)
                {
                    datas = new Dictionary<string, string>();
                    Encoding currentEncoding = Str.Utf8Encoding;
                    srcstr = currentEncoding.GetString(data);

                    // 行に分解
                    lines = Str.GetLines(srcstr);

                    foreach (string s in lines)
                    {
                        string line = s.Trim();

                        if (Str.IsEmptyStr(line) == false)
                        {
                            if (line.StartsWith("#") == false &&
                                line.StartsWith("//") == false &&
                                line.StartsWith(";") == false)
                            {
                                string key, value;

                                if (Str.GetKeyAndValue(line, out key, out value))
                                {
                                    key = key.ToUpperInvariant();

                                    if (datas.ContainsKey(key) == false)
                                    {
                                        datas.Add(key, value);
                                    }
                                    else
                                    {
                                        int i;
                                        for (i = 1; ; i++)
                                        {
                                            string key2 = string.Format("{0}({1})", key, i).ToUpperInvariant();

                                            if (datas.ContainsKey(key2) == false)
                                            {
                                                datas.Add(key2, value);
                                                break;
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }

                    if (filename != null)
                    {
                        IniCache.AddCache(filename, lastUpdate, datas);
                    }

                    Updated = true;
                }
            }
        }
    }
}

