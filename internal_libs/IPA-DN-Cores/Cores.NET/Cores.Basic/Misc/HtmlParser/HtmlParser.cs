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
// Description

#if CORES_BASIC_MISC

#pragma warning disable CA2235 // Mark all non-serializable fields

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
using System.Runtime.CompilerServices;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Serialization;

using HtmlAgilityPack;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

namespace IPA.Cores.Basic;

// ヘッダ付きテーブルのデータ
public class HtmlParsedTableData
{
    public HtmlNode TdNode { get; }
    public string SimpleText { get; }

    public HtmlParsedTableData(HtmlNode node)
    {
        this.TdNode = node;
        this.SimpleText = node.GetSimpleText();
    }

    public string FirstHyperLinkTarget
    {
        get
        {
            var a_list = this.TdNode.Elements("a");
            foreach (var a in a_list)
            {
                return a.GetAttributeValue("href", "");
            }
            return "";
        }
    }

    public override string ToString() => this.SimpleText;
}

// テーブルのパースオプション
public class HtmlTableParseOption
{
    public readonly string[]? AlternativeHeaders;
    public int SkipHeaderRowCount;
    public bool FindTBody;

    public HtmlTableParseOption(string[]? alternativeHeaders = null, int skipHeaderRowCount = 0, bool findTBody = false)
    {
        this.AlternativeHeaders = alternativeHeaders;
        this.SkipHeaderRowCount = skipHeaderRowCount;
        this.FindTBody = findTBody;

        if (findTBody && skipHeaderRowCount != 0)
        {
            throw new CoresLibException($"findTBody == true && skipHeaderRowCount({skipHeaderRowCount}) != 0");
        }
    }
}

// HTML をパースして読み込まれたヘッダ付きテーブル
public class HtmlParsedTableWithHeader
{
    public HtmlTableParseOption ParseOption { get; }
    public HtmlNode TableNode { get; }
    public List<string> HeaderList { get; }
    public List<Dictionary<string, HtmlParsedTableData>> DataList { get; }

    public HtmlParsedTableWithHeader(HtmlNode table_node, HtmlTableParseOption? parseOption = null)
    {
        if (parseOption == null) parseOption = new HtmlTableParseOption();

        this.ParseOption = parseOption;

        this.TableNode = table_node;

        HtmlNodeCollection rows;

        int skipHeaderRowCount;

        if (parseOption.FindTBody == false)
        {
            rows = TableNode.SelectNodes("tr");

            // ヘッダリストの取得
            HeaderList = new List<string>();

            if (ParseOption.AlternativeHeaders == null)
            {
                var header_coulmns = rows[ParseOption.SkipHeaderRowCount].SelectNodes("td");

                if (header_coulmns == null)
                {
                    header_coulmns = rows[ParseOption.SkipHeaderRowCount].SelectNodes("th");
                }

                foreach (var column in header_coulmns)
                {
                    HeaderList.Add(column.GetSimpleText());
                }

                skipHeaderRowCount = 1 + ParseOption.SkipHeaderRowCount;
            }
            else
            {
                HeaderList = ParseOption.AlternativeHeaders.ToList();

                skipHeaderRowCount = ParseOption.SkipHeaderRowCount;
            }
        }
        else
        {
            rows = TableNode.SelectSingleNode("tbody").SelectNodes("tr");

            // ヘッダリストの取得
            HeaderList = new List<string>();

            if (ParseOption.AlternativeHeaders == null)
            {
                var header_tr = TableNode.SelectSingleNode("thead").SelectNodes("tr");

                var header_coulmns = header_tr[0].SelectNodes("td");

                if (header_coulmns == null)
                {
                    header_coulmns = header_tr[0].SelectNodes("th");
                }

                foreach (var column in header_coulmns)
                {
                    HeaderList.Add(column.GetSimpleText());
                }
            }
            else
            {
                HeaderList = ParseOption.AlternativeHeaders.ToList();
            }

            skipHeaderRowCount = 0;
        }

        if (rows.Count <= 0) throw new ApplicationException("Table: rows.Count <= 0");


        // データリストの取得
        this.DataList = new List<Dictionary<string, HtmlParsedTableData>>();

        for (int i = skipHeaderRowCount; i < rows.Count; i++)
        {
            var td_list = rows[i].SelectNodes("td");

            Dictionary<string, HtmlParsedTableData> data_list = new Dictionary<string, HtmlParsedTableData>();

            if (td_list.Count >= this.HeaderList.Count)
            {
                for (int j = 0; j < td_list.Count; j++)
                {
                    data_list[HeaderList[j]] = new HtmlParsedTableData(td_list[j]);
                }

                DataList.Add(data_list);
            }
        }
    }
}

public static class HtmlParser
{
    public static HtmlDocument ParseHtml(string body)
    {
        HtmlDocument doc = new HtmlAgilityPack.HtmlDocument();
        doc.LoadHtml(body);

        return doc;
    }

    public static void RunStartupTest()
    {
        new HtmlAgilityPack.HtmlDocument();
    }
}

public partial class EasyHttpClient
{
    public async Task<HtmlDocument> GetHtmlAsync(string url, CancellationToken cancel = default)
    {
        var ret = await this.GetAsync(url, cancel);

        return ret.ToString()._ParseHtml();
    }
}

#endif

