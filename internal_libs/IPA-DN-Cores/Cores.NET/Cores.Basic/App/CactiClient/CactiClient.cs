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

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

namespace IPA.Cores.Basic;

public static class CactiConsts
{
    public static readonly Copenhagen<int> TimeoutMsecs = 5 * 60 * 1000;
    public static readonly Copenhagen<int> PingRetry = 5;
    public static readonly Copenhagen<int> PingTimeout = 200;
    public static readonly Copenhagen<int> DnsTimeout = 3 * 1000;
    public static readonly Copenhagen<string> GraphSmallSizeAdditionalParams = "&graph_height=100&graph_width=300&title_font_size=10&graph_nolegend=true";
}

public class CactiTask
{
    public string BaseUrl = "";
    public string Username = "";
    public string Password = "";

    public List<CactiHost> Hosts = new List<CactiHost>();
    public List<CactiEnableItem> Items = new List<CactiEnableItem>();

    public async Task ExecuteRegisterTasksAsync(CancellationToken cancel = default)
    {
        using (var session = new CactiSession(this.BaseUrl, this.Username, this.Password))
        {
            Con.WriteLine($"Logging in for {this.BaseUrl} ...");

            await session.EnsureLoginAsync(cancel);

            Con.WriteLine("Login Ok.");

            foreach (var h in Hosts)
            {
                Con.WriteLine($"Registering or finding the host {h.Description} - {h.Hostname} ...");

                try
                {
                    int hostId = await session.RegisterOrGetHostIdAsync(h, cancel);

                    Con.WriteLine($"Enabling items...");

                    await session.EnableGraphItemsAsync(hostId, Items, cancel);

                    Con.WriteLine("Ok.");
                }
                catch (Exception ex)
                {
                    ex._Print();
                }

                Con.WriteLine();
            }
        }
    }

    public static CactiTask LoadFromFile(string filename) => LoadFromData(Lfs.ReadDataFromFile(filename).Span);

    public static CactiTask LoadFromData(ReadOnlySpan<byte> data)
    {
        string body = data._GetString();
        string[] lines = body._GetLines(true);

        int mode = 0;

        CactiTask ret = new CactiTask();

        string graphName = "";

        foreach (string line in lines)
        {
            if (line.StartsWith("#") == false && line.StartsWith("//") == false && line.StartsWith(";") == false)
            {
                if (mode == 0)
                {
                    // URL 等
                    if (line._GetKeyAndValue(out string key, out string value))
                    {
                        switch (key.ToUpperInvariant())
                        {
                            case "URL":
                                ret.BaseUrl = value;
                                break;

                            case "USERNAME":
                                ret.Username = value;
                                break;

                            case "PASSWORD":
                                ret.Password = value;
                                break;
                        }
                    }
                }
                else if (mode == 1)
                {
                    string line2 = line.Trim();
                    // ホスト一覧  (形式は description hostname)
                    if (line2.StartsWith("[") == false)
                    {
                        if (line2._GetKeyAndValue(out string description, out string hostname))
                        {
                            CactiHost h = new CactiHost
                            {
                                Description = description,
                                Hostname = hostname,
                            };

                            ret.Hosts.Add(h);
                        }
                        else if (line2._IsFilled())
                        {
                            CactiHost h = new CactiHost
                            {
                                Description = line2,
                                Hostname = line2,
                            };

                            ret.Hosts.Add(h);
                        }
                    }
                }
                else if (mode == 2)
                {
                    // 値一覧
                    if (line.StartsWith("[") == false)
                    {
                        if (line.StartsWith(" ") || line.StartsWith("\t"))
                        {
                            // 値名ワイルドカード
                            if (graphName._IsFilled())
                            {
                                CactiEnableItem item = new CactiEnableItem(graphName, line.Trim());

                                ret.Items.Add(item);
                            }
                        }
                        else
                        {
                            // グラフ名
                            graphName = line.Trim();
                        }
                    }
                }

                switch (line.ToUpperInvariant())
                {
                    case "[HOSTS]":
                        mode = 1;
                        break;

                    case "[VALUES]":
                        mode = 2;
                        break;
                }
            }
        }

        return ret;
    }
}

public class CactiEnableItem
{
    public string GraphNameInStr = "";
    public string ValueNameWildcard = "";

    public CactiEnableItem() { }

    public CactiEnableItem(string graphNameInStr, string valueNameWildcard)
    {
        GraphNameInStr = graphNameInStr;
        ValueNameWildcard = valueNameWildcard;
    }
}

public class CactiGraphList
{
    public List<CactiGraph> GraphList = new List<CactiGraph>();
    public KeyValueList<string, string> HiddenValues = new KeyValueList<string, string>();
}

public class CactiGraph
{
    public string GraphTitle = "";
    public int GraphId;
    public string RefreshUrl = "";
    public KeyValueList<string, List<string>> Items = new KeyValueList<string, List<string>>();
}

public class CactiHost
{
    public int Id;
    public string Description = "";
    public string Hostname = "";
}

public class CactiDownloadedGraph
{
    public int GraphId;
    public int RraId;
    public int Size;
    public Memory<byte> Data;
}

public class CactiSession : AsyncService
{
    public string BaseUrl { get; }

    readonly string Username;
    readonly string Password;
    public readonly WebApi Http;

    string Magic = "";

    public CactiSession(string baseUrl, string username, string password, WebApiSettings? settings = null)
    {
        try
        {
            this.BaseUrl = baseUrl;
            this.Username = username;
            this.Password = password;

            if (this.BaseUrl.EndsWith("/") == false)
            {
                this.BaseUrl += "/";
            }

            if (settings == null) settings = new WebApiSettings() { DebugPrintResponse = false, SslAcceptAnyCerts = true, Timeout = CactiConsts.TimeoutMsecs, };

            this.Http = new WebApi(new WebApiOptions(settings));
        }
        catch
        {
            this._DisposeSafe();
            throw;
        }
    }

    public string GenerateUrl(string subUrl)
    {
        if (subUrl.StartsWith("/"))
        {
            subUrl = subUrl.Substring(1);
        }

        return this.BaseUrl + subUrl;
    }

    // まだログインできていなければログインする
    public async Task EnsureLoginAsync(CancellationToken cancel = default)
    {
        string url = GenerateUrl("/");

        var ret1 = await this.Http.SimpleQueryAsync(WebMethods.GET, url, cancel, null);

        string body = ret1.ToString();

        string tag = "name='__csrf_magic' value=\"";

        if (body._InStr("Logged in as"))
        {
            goto L_FINISH;
        }

        int i = body.IndexOf(tag);

        if (i == -1)
        {
            throw new CoresException("No __csrf_magic");
        }

        string tmp = body.Substring(i + tag.Length);

        i = tmp.IndexOf("\"");

        string magic = tmp.Substring(0, i);

        var ret2 = await this.Http.SimpleQueryAsync(WebMethods.POST, url, cancel, null,
            ("action", "login"),
            ("__csrf_magic", magic),
            ("login_username", this.Username),
            ("login_password", this.Password),
            ("Login", "Login"));

        if (ret2.ToString()._InStr("Logged in as") == false)
        {
            throw new CoresException("Login failed.");
        }

        body = ret2.ToString();

        L_FINISH:

        tag = "var csrfMagicToken = \"";

        i = body.IndexOf(tag);

        if (i == -1)
        {
            throw new CoresException("No csrfMagicToken");
        }

        tmp = body.Substring(i + tag.Length);

        i = tmp.IndexOf("\"");

        magic = tmp.Substring(0, i);

        this.Magic = magic;
    }

    // 現在 Cacti に登録されているホストリストの取得
    public async Task<List<CactiHost>> GetCurrentRegisteredHostListAsync(CancellationToken cancel = default)
    {
        string url = GenerateUrl("host.php");
        var ret1 = await this.Http.SimpleQueryAsync(WebMethods.GET, url, cancel, null);
        string body = ret1.ToString();

        var html = body._ParseHtml();

        var table = html.ParseTable("/html[1]/body[1]/table[1]/tr[3]/td[2]/div[1]/form[1]/table[1]/tr[1]/td[1]/table[1]",
            new HtmlTableParseOption(skipHeaderRowCount: 1));

        List<CactiHost> ret = new List<CactiHost>();

        foreach (var data in table.DataList)
        {
            CactiHost h = new CactiHost
            {
                Description = data["Description**"].SimpleText,
                Id = data["ID"].SimpleText._ToInt(),
                Hostname = data["Hostname"].SimpleText,
            };

            ret.Add(h);
        }

        return ret;
    }

    // Cacti に登録されているホストの検索
    public async Task<int> FindHostByHostnameAsync(string description, CancellationToken cancel = default)
    {
        var list = await GetCurrentRegisteredHostListAsync(cancel);

        return list.Where(x => x.Description._IsSamei(description)).Select(x => x.Id).FirstOrDefault();
    }

    // ホストの登録
    public async Task RegisterHostAsync(CactiHost h, CancellationToken cancel = default)
    {
        // 接続チェック
        bool pingOk = false;
        for (int i = 0; i < CactiConsts.PingRetry; i++)
        {
            var r = await LocalNet.SendPingAsync(h.Hostname, pingCancel: cancel, dnsCancel: cancel, pingTimeout: CactiConsts.PingTimeout, dnsTimeout: CactiConsts.DnsTimeout);
            if (r.Ok)
            {
                pingOk = true;
                break;
            }
        }
        if (pingOk == false)
        {
            throw new CoresException($"Ping to {h.Hostname} error.");
        }

        string url = GenerateUrl("host.php");

        var ret2 = await this.Http.SimpleQueryAsync(WebMethods.POST, url, cancel, null,
            ("action", "save"),
            ("__csrf_magic", this.Magic),
            ("description", h.Description),
            ("hostname", h.Hostname),
            ("host_template_id", "9"),
            ("device_threads", "1"),
            ("availability_method", "2"),
            ("ping_method", "2"),
            ("ping_port", "23"),
            ("ping_timeout", "400"),
            ("ping_retries", "1"),
            ("snmp_version", "2"),
            ("snmp_community", "public"),
            ("snmp_username", "admin"),
            ("snmp_password", "pass"),
            ("snmp_auth_protocol", "MD5"),
            ("snmp_priv_protocol", "DES"),
            ("snmp_port", "161"),
            ("snmp_timeout", "500"),
            ("max_oids", "0"),
            ("notes", ""),
            ("id", "0"),
            ("_host_template_id", "0"),
            ("Create", "Create"),
            ("save_component_host", "1"));

        string body = ret2.ToString();

        if (body._InStr("Save Successful") == false)
        {
            throw new CoresException($"Register host {h._ObjectToJson(compact: true)} failed.");
        }
    }

    // ホストがまだ登録されていなければ登録する
    public async Task<int> RegisterOrGetHostIdAsync(CactiHost host, CancellationToken cancel = default)
    {
        int id = await FindHostByHostnameAsync(host.Description, cancel);

        if (id == 0)
        {
            await RegisterHostAsync(host, cancel);

            id = await FindHostByHostnameAsync(host.Description, cancel);

            if (id == 0)
            {
                throw new CoresException($"Failed to register host {host._ObjectToJson(compact: true)}.");
            }
        }

        return id;
    }

    // ホストの SNMP 動的値をすべて再取得する
    public async Task RefreshHostAllSnmpDynamicValuesAsync(int hostId, CancellationToken cancel = default)
    {
        CactiGraphList g = await GetHostGraphListAsync(hostId, cancel);

        foreach (var t in g.GraphList)
        {
            if (t.RefreshUrl._IsFilled())
            {
                // Refresh URL を叩く
                try
                {
                    await Http.SimpleQueryAsync(WebMethods.GET, t.RefreshUrl, cancel);
                }
                catch
                {
                }
            }
        }
    }

    // ホスト用のグラフリストを取得する
    public async Task<CactiGraphList> GetHostGraphListAsync(int hostId, CancellationToken cancel = default)
    {
        string url = GenerateUrl($"graphs_new.php?host_id={hostId}");

        var response = await Http.SimpleQueryAsync(WebMethods.GET, url, cancel);

        string body = response.ToString();

        List<string> alreadyCreatedIDs = new List<string>();

        // "var created_graphs = new Array()" 以降のスクリプトを Parse し、作成済みグラフ ID 一覧を取得
        string tag2 = "var created_graphs = new Array()";
        int j = body.IndexOf(tag2);
        if (j != -1)
        {
            string script = body.Substring(j + tag2.Length);

            int endOfScript = script.IndexOf("</script>");
            if (endOfScript != -1)
            {
                script = script.Substring(0, endOfScript);
            }

            while (true)
            {
                j = script.IndexOf('\'');
                if (j == -1)
                {
                    break;
                }

                script = script.Substring(j + 1);

                int k = script.IndexOf('\'');
                if (k != -1)
                {
                    string candidate = script.Substring(0, k);
                    if (candidate.Length == 32)
                    {
                        try
                        {
                            byte[] data = candidate._GetHexBytes();
                            if (data.Length == 16)
                            {
                                alreadyCreatedIDs.Add(candidate);
                            }
                        }
                        catch
                        {
                        }
                    }
                    else if (candidate.Length <= 10)
                    {
                        if (Str.IsNumber(candidate))
                        {
                            alreadyCreatedIDs.Add(candidate);
                        }
                    }
                }
            }
        }

        var html = body._ParseHtml();

        List<HtmlAgilityPack.HtmlNode> tables = new List<HtmlAgilityPack.HtmlNode>();

        var children = html.DocumentNode.GetAllChildren();

        CactiGraphList ret = new CactiGraphList();

        // hidden 値を追加
        var hiddenValues = children.Where(x => x.Name == "input" && x.Attributes["type"].Value == "hidden");

        hiddenValues._DoForEach(x => ret.HiddenValues.Add(x.Attributes["name"].Value, x.Attributes["value"].Value));

        // HTML 中からテーブル列挙
        for (int i = 0; i < 20; i++)
        {
            HtmlAgilityPack.HtmlNode node = html.DocumentNode.SelectSingleNode($"/html[1]/body[1]/table[1]/tr[3]/td[2]/div[1]/form[1]/table[{i}]");

            if (node != null)
            {
                tables.Add(node);
            }
        }

        // 列挙したテーブル (グラフごとに 1 テーブル) をパースして処理
        foreach (var node2 in tables)
        {
            var node = node2;

            HtmlParsedTableWithHeader? table = null;
            try
            {
                table = node.ParseTable(new HtmlTableParseOption(skipHeaderRowCount: 2));
            }
            catch
            {
                // "Graph Templates" のみ Table in Table である
                node = node.SelectSingleNode("tr").SelectSingleNode("td").SelectSingleNode("table");

                try
                {
                    table = node.ParseTable(new HtmlTableParseOption(skipHeaderRowCount: 1));
                }
                catch { }
            }

            if (table != null)
            {
                string title = table.TableNode.SelectNodes("tr").First().SelectNodes("td").First().GetSimpleText()._DecodeHtml();

                string refreshPath = "";
                try
                {
                    refreshPath = table.TableNode.SelectSingleNode("tr").SelectSingleNode("td").SelectSingleNode("table")
                        .SelectSingleNode("tr").SelectNodes("td").ElementAt(1).SelectSingleNode("a").Attributes["href"].Value._DecodeHtml();
                }
                catch
                {
                }

                string refreshUrl = refreshPath._IsFilled() ? GenerateUrl(refreshPath) : "";

                int id = 0;

                if (refreshUrl._IsFilled())
                {
                    refreshUrl._ParseUrl(out _, out QueryStringList qs);

                    id = qs._GetStrFirst("id")._ToInt();
                }

                CactiGraph g = new CactiGraph
                {
                    GraphTitle = title,
                    GraphId = id,
                    RefreshUrl = refreshUrl,
                };

                foreach (var row in table.DataList)
                {
                    List<string> strList = new List<string>();

                    foreach (var kv in row)
                    {
                        if (kv.Key._IsSamei("index") == false)
                        {
                            if (kv.Value.SimpleText._IsFilled())
                            {
                                string text = kv.Value.SimpleText;

                                string tag = "Create:";
                                if (text.StartsWith(tag))
                                {
                                    text = text.Substring(tag.Length);
                                }
                                strList.Add(text.Trim());
                            }
                        }
                    }

                    string checkBoxId = row.Last().Value.TdNode.SelectNodes("input").Where(x => x.Attributes["type"].Value == "checkbox").Single().Attributes["name"].Value;

                    if (checkBoxId._IsFilled())
                    {
                        if (alreadyCreatedIDs.Where(id => checkBoxId._InStr("_" + id, true)).Any() == false)
                        {
                            g.Items.Add(checkBoxId, strList);
                        }
                    }
                }

                ret.GraphList.Add(g);
            }
        }

        return ret;
    }

    // 指定されたホスト用グラフを有効化する
    public async Task EnableGraphItemsAsync(int hostId, IEnumerable<CactiEnableItem> items, CancellationToken cancel = default)
    {
        // 動的更新
        try
        {
            await RefreshHostAllSnmpDynamicValuesAsync(hostId, cancel);
        }
        catch
        {
        }

        // グラフリストの取得
        CactiGraphList graphList = await GetHostGraphListAsync(hostId, cancel);

        List<string> enableItemIdList = new List<string>();

        foreach (var item in items)
        {
            if (item.GraphNameInStr._IsFilled())
            {
                var graphs = graphList.GraphList.Where(x => x.GraphTitle._InStr(item.GraphNameInStr));

                foreach (var graph in graphs)
                {
                    // あるグラフに関する処理
                    foreach (var graphItem in graph.Items)
                    {
                        if (graphItem.Value.Where(str => str._WildcardMatch(item.ValueNameWildcard, true)).Any())
                        {
                            enableItemIdList.Add(graphItem.Key);
                        }
                    }
                }
            }
        }

        string url = GenerateUrl($"graphs_new.php");

        List<(string name, string? value)> queryParams = new List<(string name, string? value)>();

        //queryParams.Add(("action", "save"));
        //queryParams.Add(("__csrf_magic", this.Magic));
        //queryParams.Add(("host_id", hostId.ToString()));
        //queryParams.Add(("save_component_graph", "1"));
        queryParams.Add(("sgg_1", "14"));
        queryParams.Add(("cg_g", "0"));

        graphList.HiddenValues._DoForEach(x => queryParams.Add((x.Key, x.Value)));

        enableItemIdList.Distinct()._DoForEach(x => queryParams.Add((x, "on")));

        var ret2 = await this.Http.SimpleQueryAsync(WebMethods.POST, url, cancel, null, queryParams.ToArray());

        string body = ret2.ToString();
    }

    public async Task<List<CactiDownloadedGraph>> DownloadGraphAsync(int graphId, int minRraId, int maxRraId, CancellationToken cancel = default)
    {
        List<CactiDownloadedGraph> ret = new List<CactiDownloadedGraph>();

        for (int size = 0; size < 2; size++)
        {
            for (int rraId = minRraId; rraId <= maxRraId; rraId++)
            {
                try
                {
                    string url = GenerateUrl($"graph_image.php?action=view&local_graph_id={graphId}&rra_id={rraId}");

                    if (size == 1)
                    {
                        url += CactiConsts.GraphSmallSizeAdditionalParams;
                    }

                    var res = await this.Http.SimpleQueryAsync(WebMethods.GET, url, cancel);

                    CactiDownloadedGraph g = new CactiDownloadedGraph
                    {
                        GraphId = graphId,
                        RraId = rraId,
                        Data = res.Data,
                        Size = size,
                    };

                    ret.Add(g);
                }
                catch { }
            }
        }

        return ret;
    }

    protected override void DisposeImpl(Exception? ex)
    {
        try
        {
            this.Http._DisposeSafe();
        }
        finally
        {
            base.DisposeImpl(ex);
        }
    }
}

// App
public static class CactiClientApp
{
    public static async Task DownloadGraphsAsync(string destDir, string baseUrl, string username, string password, IEnumerable<int> targetGraphIdList, CancellationToken cancel = default)
    {
        using var r = new CactiSession(baseUrl, username, password);

        List<CactiDownloadedGraph> list = new List<CactiDownloadedGraph>();

        foreach (int id in targetGraphIdList.Distinct())
        {
            await r.EnsureLoginAsync(cancel);

            Con.WriteLine($"Downloading id = {id} ...");

            try
            {
                var tmp = await r.DownloadGraphAsync(id, 1, 6, cancel);

                tmp._DoForEach(x => list.Add(x));
            }
            catch
            {
            }
        }

        if (list.Count == 0)
        {
            throw new CoresException($"Download cacti graphs failed.");
        }

        Con.WriteLine("Done.");

        foreach (var g in list)
        {
            string fn = destDir._CombinePath("graph_size_" + g.Size + "_id_" + g.GraphId.ToString("D5") + "_rra_" + g.RraId.ToString("D5") + ".png");

            Lfs.WriteDataToFile(fn, g.Data, FileFlags.AutoCreateDirectory);
        }
    }

    public static async Task ExecuteRegisterTasksAsync(string taskFileName, CancellationToken cancel = default)
    {
        CactiTask task = CactiTask.LoadFromFile(taskFileName);

        await task.ExecuteRegisterTasksAsync(cancel);
    }
}

#endif

