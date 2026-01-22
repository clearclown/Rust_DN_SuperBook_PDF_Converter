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

using System.Collections.Generic;
using System.IO;

using IPA.Cores.Basic;
using IPA.Cores.Basic.Legacy;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

namespace IPA.Cores.Basic.Legacy;

public class Stb
{
    Dictionary<string, StbEntry> entryList = null!;

    public string this[string name]
    {
        get
        {
            if (entryList.ContainsKey(name.ToUpperInvariant()))
            {
                return entryList[name.ToUpperInvariant()].String;
            }
            else
            {
                return "";
            }
        }
    }

    public Stb(string filename)
    {
        init(IO.ReadFile(filename));
    }

    public Stb(byte[] data)
    {
        init(data);
    }

    void init(byte[] data)
    {
        entryList = new Dictionary<string, StbEntry>();
        MemoryStream ms = new MemoryStream(data);
        StreamReader sr = new StreamReader(ms);
        string prefix = "";

        while (true)
        {
            string? tmp = sr.ReadLine();
            if (tmp == null)
            {
                break;
            }

            StbEntry? t = StbEntry.ParseTableLine(tmp, ref prefix);
            if (t != null)
            {
                if (entryList.ContainsKey(t.Name.ToUpperInvariant()) == false)
                {
                    entryList.Add(t.Name.ToUpperInvariant(), t);
                }
            }
        }
    }

    const string standardStbFileName = "|strtable.stb";
    static string defaultStbFileName = standardStbFileName;
    static object lockObj = new object();
    static Stb? defaultStb = null;
    public static string DefaultStbFileName
    {
        set
        {
            defaultStbFileName = value;
        }

        get
        {
            return defaultStbFileName;
        }
    }
    public static Stb DefaultStb
    {
        get
        {
            lock (lockObj)
            {
                if (defaultStb == null)
                {
                    defaultStb = new Stb(Stb.DefaultStbFileName);
                }

                return defaultStb;
            }
        }
    }
    public static string SS(string name)
    {
        return DefaultStb[name];
    }
    public static uint II(string name)
    {
        return Str.StrToUInt(SS(name));
    }
}

public class StbEntry
{
    string name;
    public string Name
    {
        get { return name; }
    }

    string str;
    public string String
    {
        get { return str; }
    }

    public StbEntry(string name, string str)
    {
        this.name = name;
        this.str = str;
    }
    public static StbEntry? ParseTableLine(string line, ref string prefix)
    {
        int i, len;
        int string_start;
        int len_name;
        string name, name2;

        // 行チェック
        line = line.TrimStart(' ', '\t');
        len = line.Length;
        if (len == 0)
        {
            return null;
        }

        // コメント
        if (line[0] == '#' || (line[0] == '/' && line[1] == '/'))
        {
            return null;
        }

        bool b = false;
        // 名前の終了位置まで検索
        len_name = 0;
        for (i = 0; i < line.Length; i++)
        {
            if (line[i] == ' ' || line[i] == '\t')
            {
                b = true;
                break;
            }
            len_name++;
        }

        if (b == false)
        {
            return null;
        }

        name = line.Substring(0, len_name);

        string_start = len_name;
        for (i = len_name; i < len; i++)
        {
            if (line[i] != ' ' && line[i] != '\t')
            {
                break;
            }
            string_start++;
        }
        if (i == len)
        {
            return null;
        }

        string str = line.Substring(string_start);

        // アンエスケープ
        str = UnescapeStr(str);

        if (Str.StrCmpi(name, "PREFIX"))
        {
            // プレフィックスが指定された
            prefix = str;
            prefix = prefix.TrimStart();

            if (Str.StrCmpi(prefix, "$") || Str.StrCmpi(prefix, "NULL"))
            {
                prefix = "";
            }

            return null;
        }

        name2 = "";

        if (prefix != "")
        {
            name2 += prefix + "@";
        }

        name2 += name;

        return new StbEntry(name2, str);
    }

    public static string UnescapeStr(string str)
    {
        int i, len;
        string tmp;

        len = str.Length;
        tmp = "";

        for (i = 0; i < len; i++)
        {
            if (str[i] == '\\')
            {
                i++;
                switch (str[i])
                {
                    case '\\':
                        tmp += '\\';
                        break;

                    case ' ':
                        tmp += ' ';
                        break;

                    case 'n':
                    case 'N':
                        tmp += '\n';
                        break;

                    case 'r':
                    case 'R':
                        tmp += '\r';
                        break;

                    case 't':
                    case 'T':
                        tmp += '\t';
                        break;
                }
            }
            else
            {
                tmp += str[i];
            }
        }

        return tmp;
    }
}
