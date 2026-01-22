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
using System.IO;

using IPA.Cores.Basic;
using IPA.Cores.Basic.Legacy;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

namespace IPA.Cores.Basic.Legacy;

// Ini ファイルの読み込み
public class ReadIni2
{
    // Ini ファイルのキャッシュ
    class IniCache
    {
        static Dictionary<string, IniCacheEntry> caches = new Dictionary<string, IniCacheEntry>();

        class IniCacheEntry
        {
            DateTime lastUpdate;
            public DateTime LastUpdate
            {
                get { return lastUpdate; }
            }

            Dictionary<string, List<string>> datas;
            public Dictionary<string, List<string>> Datas
            {
                get { return datas; }
            }

            public IniCacheEntry(DateTime lastUpdate, Dictionary<string, List<string>> datas)
            {
                this.lastUpdate = lastUpdate;
                this.datas = datas;
            }
        }

        public static Dictionary<string, List<string>>? GetCache(string filename, DateTime lastUpdate)
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

        public static void AddCache(string filename, DateTime lastUpdate, Dictionary<string, List<string>> datas)
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

    Dictionary<string, List<string>>? datas;
    bool updated;
    ulong hash_value;

    public bool Updated
    {
        get
        {
            return updated;
        }
    }

    public string[] GetStrList(string key)
    {
        try
        {
            List<string> list = datas![key.ToUpperInvariant()];

            return list.ToArray();
        }
        catch
        {
            return new string[0];
        }
    }

    public ulong HashValue
    {
        get
        {
            return this.hash_value;
        }
    }

    public StrData this[string key]
    {
        get
        {
            return this[key, 0];
        }
    }

    public StrData this[string key, int index]
    {
        get
        {
            string? s;
            try
            {
                List<string> list = datas![key.ToUpperInvariant()];

                if (index >= 0 && index < list.Count)
                {
                    s = list[index];
                }
                else
                {
                    s = null;
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

        foreach (string s in datas!.Keys)
        {
            ret.Add(s);
        }

        return ret.ToArray();
    }

    public ReadIni2(string filename)
    {
        init(null, filename);
    }

    void init(byte[] data)
    {
        init(data, null);
    }
    void init(byte[]? data, string? filename)
    {
        updated = false;

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
                    datas = new Dictionary<string, List<string>>();
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
                                        List<string> list = new List<string>();
                                        list.Add(value);

                                        datas.Add(key, list);
                                    }
                                    else
                                    {
                                        List<string> list = datas[key];

                                        list.Add(value);
                                    }
                                }
                            }
                        }
                    }

                    if (filename != null)
                    {
                        IniCache.AddCache(filename, lastUpdate, datas);
                    }

                    updated = true;
                }
            }
        }

        // calc hash
        StringWriter w = new StringWriter();
        SortedList<string, List<string>> tmp = new SortedList<string, List<string>>();
        foreach (string key in datas.Keys)
        {
            tmp.Add(key, datas[key]);
        }
        foreach (string key in tmp.Keys)
        {
            w.WriteLine("{0}={1}", key, Str.CombineStringArray(",", tmp[key].ToArray()));
        }

        this.hash_value = Str.HashStrToLong(w.ToString());
    }
}

