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

// 多言語対応
using System;
using System.Text;
using System.Web;
using System.IO;

using IPA.Cores.Basic;
using IPA.Cores.Basic.Legacy;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

namespace IPA.Cores.Basic.Legacy;

// ASP.NET ユーティリティ
public static class AspUtil
{
    // 文字列を表示できる形式に整形する
    public static string NormalizeStringToHtml(string s)
    {
        return CrLfToBR(TabAndSpaceToTag(HttpUtility.HtmlEncode(s)));
    }

    // タブやスペースを対応する文字に変換する
    public static string TabAndSpaceToTag(string s)
    {
        return s.Replace("\t", "    ").Replace(" ", "&nbsp;");
    }

    // 改行を <BR> に変換する
    public static string CrLfToBR(string s)
    {
        char[] splitters = { '\r', '\n' };
        string[] lines = s.Split(splitters, StringSplitOptions.RemoveEmptyEntries);

        StringBuilder b = new StringBuilder();
        foreach (string line in lines)
        {
            b.AppendLine(line + "<BR>");
        }

        return b.ToString();
    }

    // 指定された HTML のタイトルを取得する
    public static string GetTitleFromHtml(string src)
    {
        return GetTitleFromHtml(src, false);
    }
    public static string GetTitleFromHtml(string src, bool no_alternative)
    {
        string tmp;
        string upper;
        int i;

        if (no_alternative == false)
        {
            string? at = GetAlternativeTitleFromHtml(src);
            if (Str.IsEmptyStr(at) == false)
            {
                return at!;
            }
        }

        upper = src.ToLowerInvariant();
        i = upper.IndexOf("</title>");
        if (i == -1)
        {
            return "";
        }

        tmp = src.Substring(0, i);

        i = tmp.IndexOf("<title>");
        if (i == -1)
        {
            return "";
        }

        return tmp.Substring(i + 7);
    }
    public static string GetTitleFromHtmlFile(string filename)
    {
        string body = IO.ReadAllTextWithAutoGetEncoding(filename);

        return GetTitleFromHtml(body);
    }

    // 指定された HTML のタイトルを取得する
    public static string? GetAlternativeTitleFromHtml(string src)
    {
        string tmp;
        string upper;
        int i;

        upper = src.ToLowerInvariant();
        i = upper.IndexOf("</at>");
        if (i == -1)
        {
            return null;
        }

        tmp = src.Substring(0, i);

        i = tmp.IndexOf("<at>");
        if (i == -1)
        {
            return null;
        }

        string ret = tmp.Substring(i + 4);

        if (ret.Length == 0)
        {
            return null;
        }
        else
        {
            return ret;
        }
    }

    // URL が Default.aspx を指す場合は Default.aspx を抜き取る
    public static string RemoveDefaultHtml(string url)
    {
        string tmp = url.ToLowerInvariant();
        if (tmp.EndsWith("/default.asp") || tmp.EndsWith("/default.aspx") || tmp.EndsWith("/default.htm") || tmp.EndsWith("/default.html"))
        {
            return GetUrlDirNameFromPath(url);
        }
        else
        {
            return url;
        }
    }

    // URL からフォルダ名だけを抜き出す
    public static string GetUrlDirNameFromPath(string url)
    {
        string ret = "";
        string[] strs = url.Split('/');
        int i;
        if (strs.Length >= 1)
        {
            for (i = 0; i < strs.Length - 1; i++)
            {
                ret += strs[i] + "/";
            }
        }
        return ret;
    }


    // ホストヘッダにホスト名とポート番号がある場合はホスト名だけ抽出する
    public static string RemovePortFromHostHeader(string str)
    {
        try
        {
            string[] ret = str.Split(':');

            return ret[0];
        }
        catch
        {
            return str;
        }
    }

    // ディレクトリ名から Default.aspx などを取得する
    public static string? GetDefaultDocumentIfExists(string dir)
    {
        string[] targets =
            {
                "default.aspx",
                "default.asp",
                "default.html",
                "default.htm",
                "index.html",
                "index.htm",
            };

        foreach (string s in targets)
        {
            string name = dir + s;

            if (IsFileExists(name))
            {
                return name;
            }
        }

        return null;
    }

    // 指定されたファイルが存在するかどうか確認する
    public static bool IsFileExists(string name)
    {
        return File.Exists(name);
    }
}
