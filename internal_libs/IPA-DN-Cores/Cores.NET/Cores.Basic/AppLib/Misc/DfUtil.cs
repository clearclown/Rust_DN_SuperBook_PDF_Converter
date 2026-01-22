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

#if true

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
using System.Net;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Serialization;
using System.Net.Mail;
using System.Security.Cryptography;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using Castle.Core.Logging;
using Microsoft.Extensions.Options;
using System.Xml;

using IPA.Cores.Basic.Legacy;

namespace IPA.Cores.Basic.DfUtil;




#nullable disable

public static class DFDirScanner
{
    public static string Scan(string dir)
    {
        List<string> subdirs = new List<string>();

        string[] tmp1 = Directory.GetDirectories(dir);
        foreach (string tmp2 in tmp1)
        {
            string tmp4 = Path.GetFileName(tmp2);

            if (tmp4.Length != 6 || Str.StrToInt(tmp4) < 160000 || Str.StrToInt(tmp4) > 999999)
            {
                throw new ApplicationException(string.Format("ディレクトリ '{0}' に不正なサブディレクトリ名 '{1}' があります。", dir, tmp4));
            }

            subdirs.Add(tmp4);
        }

        subdirs.Sort();

        string lastHtmlFile = "";

        // あるディレクトリについて処理をする
        foreach (string tmp2 in subdirs)
        {
            string dir_name = Path.Combine(dir, tmp2);

            Con.WriteLine("ディレクトリ '{0}' の回線原簿を処理しています...", dir_name);

            string tag_dir_name = "";

            bool foundDfTagFileOnThisDir = false;

            // このディレクトリに最も近い DFTag.txt を取得する
            foreach (string tmp3 in subdirs)
            {
                int v1 = Str.StrToInt(tmp2);
                int v2 = Str.StrToInt(tmp3);

                if (v2 <= v1)
                {
                    string fn = Path.Combine(Path.Combine(dir, tmp3), "DFTag.txt");
                    if (File.Exists(fn))
                    {
                        tag_dir_name = Path.Combine(dir, tmp3);

                        if (v1 == v2)
                        {
                            foundDfTagFileOnThisDir = true;
                        }
                    }
                }
            }
            if (Str.IsEmptyStr(tag_dir_name))
            {
                //throw new ApplicationException(string.Format("ディレクトリ '{0}' に対応する適切な DFTag.txt が見つかりませんでした。", dir_name));
                continue;
            }

            if (foundDfTagFileOnThisDir == false)
            {
                Con.WriteLine($"ディレクトリ '{dir_name}' に DFTag.txt がありませんので、直近の '{tag_dir_name}' からパクリ (コピー) いたします。");

                try
                {
                    File.Copy(Path.Combine(tag_dir_name, "DFTag.txt"), Path.Combine(dir_name, "DFTag.txt"));
                }
                catch (Exception ex)
                {
                    Con.WriteLine($"   ★ エラー: txt ファイル '{Path.Combine(dir_name, "DFTag.txt")}' へのコピーに失敗しました。例外の内容: {ex.Message}");
                }
            }

            DFMap m = new DFMap();
            m.Load(dir_name, tag_dir_name);

            string html = m.GenerateHtml();

            string out_fn = Path.Combine(dir_name, "DFList_" + tmp2 + ".htm");

            try
            {
                Str.WriteTextFile(out_fn, html, Str.Utf8Encoding, true);

                Con.WriteLine("  HTML ファイル '{0}' を書き出しました。", out_fn);
            }
            catch (Exception ex)
            {
                Con.WriteLine($"   ★ エラー: HTML ファイル '{out_fn}' の書き出しに失敗しました。例外の内容: {ex.Message}");
            }

            lastHtmlFile = out_fn;
        }

        return lastHtmlFile;
    }
}

public class DFTag
{
    public string Project, Line, Usage, ID;
}


public static class DFConsts
{
    public const double LossPerKilloMeterGuess = 0.3;
    public const double LossPerBuilding = 3.6;
    public const double LossPerUser = 2.6;
    public const double LossPerKilloMeterSanko = 0.7;
    public const double LosBasicSanko = 3.0;
    public const int BasicUserLength = 10000;
}

public class DFCircuit : IComparable<DFCircuit>
{
    public string Tag_Project = "", Tag_Line = "", Tag_Usage = "";

    public DFObj First;
    public string ID = "";
    public DateTime StartDt = Util.ZeroDateTimeValue;

    public List<DFObj> ObjList = new List<DFObj>();

    public int TotalEstimatedDistance;
    public int TotalRealDistance;

    public double TotalLoss_Official, TotalLoss_Estimate;
    public int TotalCost;

    public int TotalIndoor, TotalAccess, TotalTransit;

    public DFCircuit(DFObj start)
    {
        this.First = start;
    }

    public void Normalize()
    {
        DFObj o = this.First;

        int idval = int.MaxValue;

        this.ObjList = new List<DFObj>();

        this.StartDt = DateTime.MaxValue;

        do
        {
            int i = Str.StrToInt(o.ID);
            if (i != 0)
            {
                idval = Math.Min(i, idval);
            }

            if (o.StartDt != Util.ZeroDateTimeValue)
            {
                this.StartDt = new DateTime(Math.Min(this.StartDt.Ticks, o.StartDt.Ticks));
            }

            this.ObjList.Add(o);

            o = o.Next;
        }
        while (o != null);

        if (this.StartDt == DateTime.MaxValue)
        {
            this.StartDt = Util.ZeroDateTimeValue;
        }

        if (idval == int.MaxValue)
        {
            idval = 0;
        }

        this.ID = idval.ToString();

        // 距離計算
        int distance_transit = 0;
        int distance_access = 0;
        this.TotalLoss_Official = 0;

        double estimate_se = 0;
        double estimate_sanko = 0;

        double cost = 0;

        this.TotalIndoor = this.TotalAccess = this.TotalTransit = 0;

        foreach (DFObj c in this.ObjList)
        {
            switch (c.Type)
            {
                case DFObjType.Transit:
                    this.TotalLoss_Official += c.Transit.Loss;

                    estimate_se += DFConsts.LossPerKilloMeterGuess * c.Transit.Distance / 1000;

                    estimate_sanko += (double)c.Transit.Distance * DFConsts.LossPerKilloMeterSanko / (double)1000;

                    if (c.Transit.IsKenkan == false)
                    {
                        cost += DFVars.Get("TransitCostBasic") + DFVars.Get("TransitCostKm") * (double)c.Transit.Distance / 1000.0;
                        distance_transit += c.Transit.Distance;
                        this.TotalTransit++;
                    }
                    else
                    {
                        distance_transit = 0;
                        cost += DFVars.Get("TransitCostBasic") + DFVars.Get("Kenkan_" + c.Transit.RouteCode);
                        if (DFVars.Get("Kenkan_" + c.Transit.RouteCode) == 0)
                        {
                            throw new ApplicationException("県間ルートコード " + c.Transit.RouteCode + " の料金が不明である。");
                        }
                    }

                    break;

                case DFObjType.Access:
                    distance_access += 5000;
                    this.TotalLoss_Official += c.Access.Loss;

                    estimate_se += DFConsts.LossPerUser;
                    estimate_se += DFConsts.LossPerKilloMeterGuess * (double)(DFConsts.BasicUserLength / 2.0) / (double)1000;

                    estimate_sanko += DFConsts.LosBasicSanko;

                    cost += DFVars.Get("AccessDfCost");

                    if (c.Access.HasKounai)
                    {
                        cost += DFVars.Get("AccessIndoorCost");
                    }

                    this.TotalAccess++;

                    break;

                case DFObjType.Indoor:
                    estimate_se += DFConsts.LossPerBuilding;

                    this.TotalIndoor++;

                    cost += DFVars.Get("IndoorCost");
                    break;
            }
        }

        this.TotalEstimatedDistance = distance_transit + distance_access;
        this.TotalRealDistance = distance_transit;
        this.TotalLoss_Estimate = Math.Max(estimate_se, estimate_sanko);
        this.TotalCost = (int)cost;
    }

    public int CompareTo(DFCircuit other)
    {
        return this.StartDt.CompareTo(other.StartDt);
    }
}

public class DFUsage : IComparable<DFUsage>
{
    public string UsageName;

    public List<DFCircuit> CircuitList = new List<DFCircuit>();

    int IComparable<DFUsage>.CompareTo(DFUsage other)
    {
        return string.Compare(this.UsageName, other.UsageName, true);
    }

    public int TotalCost
    {
        get
        {
            int ret = 0;
            foreach (DFCircuit c in this.CircuitList)
                ret += c.TotalCost;
            return ret;
        }
    }

    public int TotalEstimatedDistance
    {
        get
        {
            int ret = 0;
            foreach (DFCircuit c in this.CircuitList)
                ret += c.TotalEstimatedDistance;
            return ret;
        }
    }

    public int TotalRealDistance
    {
        get
        {
            int ret = 0;
            foreach (DFCircuit c in this.CircuitList)
                ret += c.TotalRealDistance;
            return ret;
        }
    }

    public int TotalIndoor
    {
        get
        {
            int ret = 0;
            foreach (DFCircuit c in this.CircuitList)
                ret += c.TotalIndoor;
            return ret;
        }
    }

    public int TotalAccess
    {
        get
        {
            int ret = 0;
            foreach (DFCircuit c in this.CircuitList)
                ret += c.TotalAccess;
            return ret;
        }
    }

    public int TotalTransit
    {
        get
        {
            int ret = 0;
            foreach (DFCircuit c in this.CircuitList)
                ret += c.TotalTransit;
            return ret;
        }
    }
}

public class DFLine : IComparable<DFLine>
{
    public string LineName;

    public List<DFUsage> UsageList = new List<DFUsage>();

    public DFUsage add_or_get(string name)
    {
        foreach (DFUsage p in this.UsageList)
            if (Str.StrCmpi(p.UsageName, name))
                return p;
        DFUsage a = new DFUsage();
        a.UsageName = name;
        UsageList.Add(a);
        return a;
    }

    int IComparable<DFLine>.CompareTo(DFLine other)
    {
        return string.Compare(this.LineName, other.LineName, true);
    }

    public int TotalCost
    {
        get
        {
            int ret = 0;
            foreach (DFUsage u in UsageList)
                ret += u.TotalCost;
            return ret;
        }
    }

    public int TotalEstimatedDistance
    {
        get
        {
            int ret = 0;
            foreach (DFUsage u in UsageList)
                ret += u.TotalEstimatedDistance;
            return ret;
        }
    }

    public int TotalRealDistance
    {
        get
        {
            int ret = 0;
            foreach (DFUsage u in UsageList)
                ret += u.TotalRealDistance;
            return ret;
        }
    }

    public int TotalIndoor
    {
        get
        {
            int ret = 0;
            foreach (DFUsage u in UsageList)
                ret += u.TotalIndoor;
            return ret;
        }
    }

    public int TotalAccess
    {
        get
        {
            int ret = 0;
            foreach (DFUsage u in UsageList)
                ret += u.TotalAccess;
            return ret;
        }
    }

    public int TotalTransit
    {
        get
        {
            int ret = 0;
            foreach (DFUsage u in UsageList)
                ret += u.TotalTransit;
            return ret;
        }
    }
}

public class DFProject : IComparable<DFProject>
{
    public string ProjectName;

    public List<DFLine> LineList = new List<DFLine>();

    public DFLine add_or_get(string name)
    {
        foreach (DFLine p in this.LineList)
            if (Str.StrCmpi(p.LineName, name))
                return p;
        DFLine a = new DFLine();
        a.LineName = name;
        LineList.Add(a);
        return a;
    }

    int IComparable<DFProject>.CompareTo(DFProject other)
    {
        return string.Compare(this.ProjectName, other.ProjectName, true);
    }

    public int TotalCost
    {
        get
        {
            int ret = 0;
            foreach (DFLine e in this.LineList)
                ret += e.TotalCost;
            return ret;
        }
    }

    public int TotalEstimatedDistance
    {
        get
        {
            int ret = 0;
            foreach (DFLine e in this.LineList)
                ret += e.TotalEstimatedDistance;
            return ret;
        }
    }

    public int TotalRealDistance
    {
        get
        {
            int ret = 0;
            foreach (DFLine e in this.LineList)
                ret += e.TotalRealDistance;
            return ret;
        }
    }

    public int TotalIndoor
    {
        get
        {
            int ret = 0;
            foreach (DFLine e in this.LineList)
                ret += e.TotalIndoor;
            return ret;
        }
    }

    public int TotalAccess
    {
        get
        {
            int ret = 0;
            foreach (DFLine e in this.LineList)
                ret += e.TotalAccess;
            return ret;
        }
    }

    public int TotalTransit
    {
        get
        {
            int ret = 0;
            foreach (DFLine e in this.LineList)
                ret += e.TotalTransit;
            return ret;
        }
    }
}

public class DFProjectDb
{
    public List<DFProject> ProjectList = new List<DFProject>();

    public DFProject add_or_get(string name)
    {
        foreach (DFProject p in this.ProjectList)
            if (Str.StrCmpi(p.ProjectName, name))
                return p;
        DFProject a = new DFProject();
        a.ProjectName = name;
        ProjectList.Add(a);
        return a;
    }

    public void AddCircuit(DFCircuit c)
    {
        DFUsage u;
        if (c.Tag_Project == "")
        {
            u = this.add_or_get("不明なプロジェクト").add_or_get("不明な回線").add_or_get("不明な用途");
        }
        else
        {
            u = this.add_or_get(c.Tag_Project).add_or_get(c.Tag_Line).add_or_get(c.Tag_Usage);
        }

        u.CircuitList.Add(c);

        u.CircuitList.Sort();
    }

    public void Sort()
    {
        foreach (DFProject p in this.ProjectList)
        {
            foreach (DFLine e in p.LineList)
            {
                e.UsageList.Sort();
            }

            p.LineList.Sort();
        }

        this.ProjectList.Sort();
    }

    public int TotalCost
    {
        get
        {
            int ret = 0;
            foreach (DFProject e in this.ProjectList)
                ret += e.TotalCost;
            return ret;
        }
    }

    public int TotalEstimatedDistance
    {
        get
        {
            int ret = 0;
            foreach (DFProject e in this.ProjectList)
                ret += e.TotalEstimatedDistance;
            return ret;
        }
    }

    public int TotalRealDistance
    {
        get
        {
            int ret = 0;
            foreach (DFProject e in this.ProjectList)
                ret += e.TotalRealDistance;
            return ret;
        }
    }

    public int TotalIndoor
    {
        get
        {
            int ret = 0;
            foreach (DFProject e in this.ProjectList)
                ret += e.TotalIndoor;
            return ret;
        }
    }

    public int TotalAccess
    {
        get
        {
            int ret = 0;
            foreach (DFProject e in this.ProjectList)
                ret += e.TotalAccess;
            return ret;
        }
    }

    public int TotalTransit
    {
        get
        {
            int ret = 0;
            foreach (DFProject e in this.ProjectList)
                ret += e.TotalTransit;
            return ret;
        }
    }
}

public static class DFVars
{
    static ReadIni ini = null;

    public static void Load(string fn)
    {
        ini = new ReadIni(fn);
    }

    public static int Get(string name)
    {
        return (int)ini[name].IntValue;
    }
}

public class DFMap
{
    public string GenboDir, TagDir;

    public DFGenbo Genbo = new DFGenbo();

    public DFProjectDb ProjectDb = new DFProjectDb();

    public List<DFCircuit> CircuitList = new List<DFCircuit>();

    DFCircuit find_circuit_by_id(string id)
    {
        foreach (DFCircuit c in this.CircuitList)
        {
            foreach (DFObj o in c.ObjList)
            {
                if (o.ID == id)
                {
                    return c;
                }
            }
        }

        return null;
    }

    public int NumAccess, NumTransit, NumIndoor;

    public string GenerateHtml()
    {
        string template = CoresRes["Misc/240501_DF.htm"].String._NormalizeCrlf(CrlfStyle.Lf);

        StringWriter w = new StringWriter();

        w.WriteLine(string.Format("原簿ディレクトリ: {0}<BR>タグディレクトリ: {1}<BR>生成日時: {2}<BR>",
            this.GenboDir + PP.DirectorySeparator, this.TagDir + PP.DirectorySeparator, Str.DateTimeToStrShort(DateTime.Now)));

        string tmp3 = "";

        tmp3 += string.Format("<b>全プロジェクト合計: 月額費用: {0} 円 (中継: {6} 円, 加入 {7} 本: {8} 円, 局内 {9} 本: {10} 円)<BR>中継合計: {5:F1} km, 推定合計: {1:F1} km, 加入光本数: {2}, 中継光本数: {3}, 局内光本数: {4}</b>",
            Str.ToStr3(ProjectDb.TotalCost), (double)ProjectDb.TotalEstimatedDistance / 1000.0,
            this.NumAccess, this.NumTransit, this.NumIndoor, (double)ProjectDb.TotalRealDistance / 1000.0,
            Str.ToStr3((int)((double)ProjectDb.TotalRealDistance * (double)DFVars.Get("TransitCostKm") / 1000.0 + (double)DFVars.Get("TransitCostBasic") * (double)ProjectDb.TotalTransit)),
            ProjectDb.TotalAccess, Str.ToStr3(DFVars.Get("AccessDfCost") * ProjectDb.TotalAccess),
            ProjectDb.TotalIndoor, Str.ToStr3(DFVars.Get("IndoorCost") * ProjectDb.TotalIndoor));

        tmp3 += "<BR><BR>";

        tmp3 += "■ プロジェクトごと月額費用<BR>";
        foreach (DFProject proj in this.ProjectDb.ProjectList)
        {
            tmp3 += string.Format("・ <a href='#{2}'>{0}</a>:<BR>　　{1} 円<BR>", proj.ProjectName, Str.ToStr3(proj.TotalCost), Str.HashStrToLong(proj.ProjectName));
        }

        w.WriteLine("<p><span class='highlight'>{0}</span></p>", tmp3);

        foreach (DFProject proj in this.ProjectDb.ProjectList)
        {
            w.WriteLine("<p>　</p>");
            w.WriteLine("<HR>");
            w.WriteLine("<h2 id='{1}'>{0}</h2>", proj.ProjectName, Str.HashStrToLong(proj.ProjectName));

            string tmp2 = string.Format("<b>月額費用: {0} 円 (中継: {3} 円, 加入 {4} 本: {5} 円, 局内 {6} 本: {7} 円), 中継合計: {2:F1} km, 推定合計: {1:F1} km</b>",
                Str.ToStr3(proj.TotalCost), (double)proj.TotalEstimatedDistance / 1000.0, (double)proj.TotalRealDistance / 1000.0,
                    Str.ToStr3((int)((double)proj.TotalRealDistance * (double)DFVars.Get("TransitCostKm") / 1000.0 + (double)DFVars.Get("TransitCostBasic") * (double)proj.TotalTransit)),
                    proj.TotalAccess, Str.ToStr3(DFVars.Get("AccessDfCost") * proj.TotalAccess),
                    proj.TotalIndoor, Str.ToStr3(DFVars.Get("IndoorCost") * proj.TotalIndoor));
            w.WriteLine("<p><span class='highlight'>{0}</span></p>", tmp2);

            foreach (DFLine line in proj.LineList)
            {
                w.WriteLine("<h3>{0}</h3>", line.LineName);

                string tmp = string.Format("<b>月額費用: {0} 円 (中継: {3} 円, 加入 {4} 本: {5} 円, 局内 {6} 本: {7} 円), 中継合計: {2:F1} km, 推定合計: {1:F1} km</b>",
                    Str.ToStr3(line.TotalCost), (double)line.TotalEstimatedDistance / 1000.0, (double)line.TotalRealDistance / 1000.0,
                    Str.ToStr3((int)((double)line.TotalRealDistance * (double)DFVars.Get("TransitCostKm") / 1000.0 + (double)DFVars.Get("TransitCostBasic") * (double)line.TotalTransit)),
                    line.TotalAccess, Str.ToStr3(DFVars.Get("AccessDfCost") * line.TotalAccess),
                    line.TotalIndoor, Str.ToStr3(DFVars.Get("IndoorCost") * line.TotalIndoor)
                    );
                w.WriteLine("<p><span class='highlight'>{0}</span></p>", tmp);

                foreach (DFUsage usage in line.UsageList)
                {
                    w.WriteLine("<h4>{0}</h4>", usage.UsageName);
                    //w.WriteLine("<p>　</p>");

                    foreach (DFCircuit c in usage.CircuitList)
                    {
                        w.WriteLine("{0}", generate_circuit_html(c));
                        //w.WriteLine("<p>　</p>");
                    }
                }
            }
        }

        string ret = Str.ReplaceStr(template, "$DATA$", w.ToString());

        return ret;
    }

    string generate_circuit_html(DFCircuit c)
    {
        StringWriter w = new StringWriter();
        int i;
        string notes = "";
        notes += string.Format("<b>月額費用: {4} 円 (中継: {5} 円, 加入 {6} 本: {7} 円, 局内 {8} 本: {9} 円)</b>, 開通日: {3}, 中継距離: {10:F1} km, 推定距離: {0:F1} km, 推定損失: {1:F1} dB, 電話会社保証損失: {2:F1} dB", (double)c.TotalEstimatedDistance / 1000.0, c.TotalLoss_Estimate, c.TotalLoss_Official,
            c.StartDt.ToShortDateString(), Str.ToStr3(c.TotalCost),
            Str.ToStr3((int)((double)c.TotalRealDistance * (double)DFVars.Get("TransitCostKm") / 1000.0 + (double)DFVars.Get("TransitCostBasic") * (double)c.TotalTransit)),
            c.TotalAccess, Str.ToStr3(DFVars.Get("AccessDfCost") * c.TotalAccess),
            c.TotalIndoor, Str.ToStr3(DFVars.Get("IndoorCost") * c.TotalIndoor),
            c.TotalRealDistance / 1000.0
            );

        w.WriteLine("<p><span class='highlight2'>{0}</span></p>", notes);

        w.WriteLine("<table cellspacing='1'>");
        w.WriteLine("	<tr>");

        for (i = 0; i < c.ObjList.Count; i++)
        {
            DFObj o = c.ObjList[i];
            w.WriteLine("		<td class='cell_center' valign='bottom' style='width: 18px'>{0}</td>", o.Place);

            if (i != (c.ObjList.Count - 1))
            {
                w.WriteLine("		<td class='cell_center' valign='bottom' style='width: 18px'>&nbsp;</td>", o.Type.ToString());
            }
        }

        w.WriteLine("	</tr>");

        w.WriteLine("	<tr>");
        for (i = 0; i < c.ObjList.Count; i++)
        {
            DFObj o = c.ObjList[i];
            w.WriteLine("		<td class='cell_center' valign='bottom' style='white-space:nowrap'><span class='{0}'>{1}</span></td>",
                o.Type.ToString(), o.Title);

            if (i != (c.ObjList.Count - 1))
            {
                w.WriteLine("		<td class='cell_center' valign='bottom' style='white-space:nowrap'><hr /></td>");
            }
        }
        w.WriteLine("	</tr>");

        w.WriteLine("	<tr>");
        for (i = 0; i < c.ObjList.Count; i++)
        {
            DFObj o = c.ObjList[i];
            w.WriteLine("		<td class='cell_center' valign='top' style='font-size: 70%'>{0}</td>",
                Str.EncodeHtml(o.Details));

            if (i != (c.ObjList.Count - 1))
            {
                w.WriteLine("		<td class='cell_center' valign='top'>&nbsp;</td>");
            }
        }
        w.WriteLine("	</tr>");
        w.WriteLine("</table>");


        return w.ToString();
    }

    public List<DFTag> LoadTagFile(string tag_fn)
    {
        string data = Str.ReadTextFile(tag_fn);
        string[] lines = Str.GetLines(data);

        List<DFTag> ret = new List<DFTag>();

        string proj = "";
        string line = "";
        string usage = "";

        List<string> unref_lines = new List<string>();

        foreach (string tmp in lines)
        {
            if (Str.IsEmptyStr(tmp) == false)
            {
                string this_line = tmp;
                int i = Str.SearchStr(tmp, "//", 0);
                if (i != -1)
                {
                    this_line = this_line.Substring(0, i);
                }

                this_line = Str.ReplaceStr(this_line, "    ", "\t");

                int depth = 0;
                if (this_line.StartsWith("\t")) depth = 1;
                if (this_line.StartsWith("\t\t")) depth = 2;
                if (this_line.StartsWith("\t\t\t")) depth = 3;
                if (this_line.StartsWith("\t\t\t\t")) throw new ApplicationException("不正なタグ行: '" + this_line + "'");

                for (i = 0; i < this_line.Length; i++)
                {
                    char ch = this_line[i];

                    if (ch == '\t')
                    {
                    }
                    else if (ch == ' ')
                    {
                        throw new ApplicationException("空白文字が入っている: '" + this_line + "'");
                    }
                    else
                    {
                        break;
                    }
                }

                string str = this_line.Substring(depth);
                str = str.Trim();

                str = Str.NormalizeStrSoftEther(str);

                if (Str.IsEmptyStr(str) == false)
                {
                    switch (depth)
                    {
                        case 0:
                            proj = str;
                            line = "";
                            usage = "";
                            break;

                        case 1:
                            line = str;
                            usage = "";
                            break;

                        case 2:
                            usage = str;
                            break;

                        case 3:
                            if (proj == "" || line == "" || usage == "")
                            {
                                throw new ApplicationException("不正な状態行: '" + line + "'");
                            }

                            DFCircuit c = find_circuit_by_id(str);
                            if (c == null)
                            {
                                unref_lines.Add(str);
                            }
                            else
                            {
                                if (c.Tag_Project == "")
                                {
                                    c.Tag_Project = proj;
                                    c.Tag_Line = line;
                                    c.Tag_Usage = usage;
                                }
                                else
                                {
                                    if (Str.StrCmpi(c.Tag_Project, proj) &&
                                        Str.StrCmpi(c.Tag_Line, line) &&
                                        Str.StrCmpi(c.Tag_Usage, usage))
                                    {
                                    }
                                    else
                                    {
                                        throw new ApplicationException(string.Format("回線 ID '{0}' に属する回線はすでにプロジェクト '{1}' に関連付けられており、新たにプロジェクト '{2}' に関連付けることはできません。",
                                            str, c.Tag_Project, proj));
                                    }
                                }
                            }

                            break;
                    }
                }

            }
        }

        unref_lines.Sort();
        foreach (string undef_line in unref_lines)
        {
            Con.WriteLine("見つからない回線 ID : " + undef_line);
        }

        return ret;
    }

    public void Load(string genbo_dir, string tag_dir)
    {
        this.GenboDir = genbo_dir;
        this.TagDir = tag_dir;

        DFVars.Load(Path.Combine(tag_dir, "DFTag.txt"));

        // 原簿の読み込み
        Genbo.Load(genbo_dir);

        // 加入を端緒として検索
        foreach (DFAccess a in Genbo.AccessList)
        {
            if (a.InUse)
            {
                if (a.Flag1 == false)
                {
                    DFObj start_obj = new DFObj(a);
                    build_line(start_obj);

                    CircuitList.Add(new DFCircuit(start_obj));
                }
            }
        }

        // 中継を端緒として検索
        foreach (DFTransit a in Genbo.TransitList)
        {
            if (a.InUse)
            {
                if (a.Flag1 == false)
                {
                    // この中継の両端のいずれか一方が、局内と接続されていないものであるかどうかチェック
                    int num = 0;
                    foreach (DFIndoor indoor in Genbo.IndoorList)
                    {
                        if (indoor.InUse)
                        {
                            if (indoor.A_ID == a.ID)
                            {
                                num++;
                            }
                            if (indoor.B_ID == a.ID)
                            {
                                num++;
                            }
                        }
                    }

                    if (num <= 1)
                    {
                        // 未発見の端緒中継を発見
                        DFObj start_obj;
                        start_obj = new DFObj();
                        DFObj transit_obj = new DFObj(a);
                        start_obj.Next = transit_obj;
                        build_line(transit_obj);

                        CircuitList.Add(new DFCircuit(start_obj));
                    }
                }
            }
        }

        // 局内を端緒として検索
        foreach (DFIndoor a in Genbo.IndoorList)
        {
            if (a.InUse)
            {
                if (a.Flag1 == false)
                {
                    bool next_left = false;
                    string next_id = "";
                    string coloc_str = "";
                    if (a.A_ID == "" || a.A_ID == "-")
                    {
                        next_left = false;
                        next_id = a.B_ID;
                        coloc_str = a.A_Rack;
                    }
                    else if (a.B_ID == "" || a.A_ID == "-")
                    {
                        next_left = true;
                        next_id = a.A_ID;
                        coloc_str = a.B_Rack;
                    }

                    if (Str.IsEmptyStr(next_id) == false)
                    {
                        DFObj start_obj;
                        if (Str.IsEmptyStr(coloc_str))
                        {
                            start_obj = new DFObj();
                        }
                        else
                        {
                            start_obj = new DFObj(coloc_str, a.TelcoBldg);
                        }
                        DFObj indoor_obj = new DFObj(a);
                        start_obj.Next = indoor_obj;
                        build_line(indoor_obj, !next_left, null);

                        CircuitList.Add(new DFCircuit(start_obj));
                    }
                }
            }
        }

        // 未到達回線を表示
        int num_unused = 0;
        foreach (DFIndoor a in Genbo.IndoorList)
        {
            if (a.InUse && a.Flag1 == false)
            {
                Con.WriteLine(string.Format("局内 {0} が未到達", a.ID));
                num_unused++;
            }
        }
        foreach (DFAccess a in Genbo.AccessList)
        {
            if (a.InUse && a.Flag1 == false)
            {
                Con.WriteLine(string.Format("加入 {0} が未到達", a.ID));
                num_unused++;
            }
        }
        foreach (DFTransit a in Genbo.TransitList)
        {
            if (a.InUse && a.Flag1 == false)
            {
                Con.WriteLine(string.Format("中継 {0} が未到達", a.ID));
                num_unused++;
            }
        }
        if (num_unused != 0)
        {
            throw new ApplicationException(string.Format("未到達回線が {0} 個もあります!", num_unused));
        }

        Con.WriteLine("合計回線数: {0}", this.CircuitList.Count);

        this.NumAccess = this.NumIndoor = this.NumTransit = 0;

        foreach (DFAccess a in this.Genbo.AccessList) if (a.InUse) this.NumAccess++;
        foreach (DFIndoor a in this.Genbo.IndoorList) if (a.InUse) this.NumIndoor++;
        foreach (DFTransit a in this.Genbo.TransitList) if (a.InUse) this.NumTransit++;

        Con.WriteLine("  加入: {0}", NumAccess);
        Con.WriteLine("  中継: {0}", NumTransit);
        Con.WriteLine("  局内: {0}", NumIndoor);

        foreach (DFCircuit circuit in this.CircuitList)
        {
            circuit.Normalize();
        }

        // タグファイルの読み込み
        LoadTagFile(Path.Combine(tag_dir, "DFTag.txt"));

        // 木構造に変換
        foreach (DFCircuit circuit in this.CircuitList)
        {
            this.ProjectDb.AddCircuit(circuit);
        }
        this.ProjectDb.Sort();

        Util.RetZero();

    }

    void build_line(DFObj c)
    {
        build_line(c, false, null);
    }
    void build_line(DFObj c, bool indoor_left, string last_indoor_id)
    {
        switch (c.Type)
        {
            case DFObjType.Access:
                {
                    // 加入
                    if (Str.IsEmptyStr(c.Access.Trunk) == false)
                    {
                        // 自前トランク接続
                        DFObj trunk = new DFObj("インチキ・ラック", "快適天空", c.Access.Trunk);
                        c.Next = trunk;
                    }
                    else
                    {
                        DFIndoor indoor = Genbo.SearchIndoorByABId(c.Access.ID, null);
                        if (indoor == null)
                        {
                            // 遠端
                            DFObj empty = new DFObj();
                            c.Next = empty;
                        }
                        else
                        {
                            if (indoor.Flag1)
                            {
                                throw new ApplicationException(string.Format("局内 {0} が 2 回線から参照されています", indoor.ID));
                            }

                            // 局内ファイバー
                            DFObj indoor_obj = new DFObj(indoor);
                            c.Next = indoor_obj;

                            bool next_left = false;
                            if (indoor.A_ID == c.Access.ID)
                            {
                                next_left = true;
                            }

                            build_line(indoor_obj, next_left, null);
                        }
                    }
                    break;
                }

            case DFObjType.Transit:
                {
                    // 中継
                    // 接続先の局内を探す
                    DFIndoor indoor = Genbo.SearchIndoorByABId(c.Transit.ID, last_indoor_id);

                    if (indoor == null)
                    {
                        // 遠端
                        DFObj empty = new DFObj();
                        c.Next = empty;

                        Util.DoNothing();
                    }
                    else
                    {
                        if (indoor.Flag1)
                        {
                            throw new ApplicationException(string.Format("局内 {0} が 2 回線から参照されています", indoor.ID));
                        }

                        DFObj indoor_obj = new DFObj(indoor);
                        c.Next = indoor_obj;

                        bool next_left = false;
                        if (indoor.A_ID == c.Transit.ID)
                        {
                            next_left = true;
                        }

                        build_line(indoor_obj, next_left, null);
                    }

                    break;
                }

            case DFObjType.Indoor:
                {
                    // 局内
                    string next_id = "";
                    if (indoor_left)
                    {
                        next_id = c.Indoor.B_ID;
                    }
                    else
                    {
                        next_id = c.Indoor.A_ID;
                    }

                    if (next_id == "" || next_id == "-")
                    {
                        // 接続先がコロケ
                        DFObj coloc = new DFObj((indoor_left ? c.Indoor.B_Rack : c.Indoor.A_Rack), c.Indoor.TelcoBldg);
                        c.Next = coloc;
                    }
                    else
                    {
                        // 接続先が他の回線 ID
                        DFAccess access = Genbo.SearchAccessById(next_id);
                        DFTransit transit = Genbo.SearchTransitById(next_id);

                        if (access != null)
                        {
                            // 接続先が加入
                            if (access.Flag1)
                            {
                                throw new ApplicationException(string.Format("加入 {0} が 2 回線から参照されています", access.ID));
                            }

                            DFObj access_obj = new DFObj(access);
                            c.Next = access_obj;
                        }
                        else if (transit != null)
                        {
                            // 接続先が中継
                            if (transit.Flag1)
                            {
                                throw new ApplicationException(string.Format("中継 {0} が 2 回線から参照されています", transit.ID));
                            }

                            DFObj transit_obj = new DFObj(transit);
                            c.Next = transit_obj;

                            build_line(transit_obj, false, c.Indoor.ID);
                        }
                        else
                        {
                            throw new ApplicationException(string.Format("局内 {0} の接続先である {0} が見つかりません。", next_id));
                        }
                    }

                    break;
                }

            default:
                throw new NotImplementedException();
        }
    }
}

public enum DFObjType
{
    Access,
    Transit,
    Indoor,
    Rack,
    Trunk,
    Empty,
}

public class DFObj
{
    public DFObj()
    {
        this.Type = DFObjType.Empty;
    }

    public DFObj(object o)
    {
        if (o is DFAccess)
        {
            this.Access = o as DFAccess;
            this.Type = DFObjType.Access;
            this.Access.Flag1 = true;
            this.ID = this.Access.ID;
            this.StartDt = this.Access.StartDt;
            this.InUse = this.Access.InUse;
        }
        else if (o is DFTransit)
        {
            this.Transit = o as DFTransit;
            this.Type = DFObjType.Transit;
            this.Transit.Flag1 = true;
            this.ID = this.Transit.ID;
            this.StartDt = this.Transit.StartDt;
            this.InUse = this.Transit.InUse;
        }
        else if (o is DFIndoor)
        {
            this.Indoor = o as DFIndoor;
            this.Type = DFObjType.Indoor;
            this.Indoor.Flag1 = true;
            this.ID = this.Indoor.ID;
            this.StartDt = this.Indoor.StartDt;
            this.InUse = this.Indoor.InUse;
        }
        else
        {
            throw new ArgumentException();
        }
    }

    public DFObj(string rack_name, string rack_place)
    {
        this.Type = DFObjType.Rack;
        this.RackName = rack_name;
        this.RackPlace = rack_place;
    }

    public DFObj(string rack_name, string rack_place, string trunk_name)
    {
        this.Type = DFObjType.Trunk;
        this.RackName = rack_name;
        this.RackPlace = rack_place;
        this.Trunk = trunk_name;
    }

    public DFObj Next;

    public DFObjType Type;
    public DFAccess Access;
    public DFTransit Transit;
    public DFIndoor Indoor;

    public bool InUse = false;

    public string RackName = "";
    public string RackPlace = "";
    public string Trunk = "";
    public string ID = "";
    public DateTime StartDt = Util.ZeroDateTimeValue;

    public string Details
    {
        get
        {
            StringWriter w = new StringWriter();

            switch (Type)
            {
                case DFObjType.Access:
                    w.WriteLine(Access.CableName + " " + Access.CableStrand + " / " + Access.Loss.ToString("F1") + " dB");
                    w.WriteLine("加入: {0} / 局内: {1}", Str.NormalizeStrSoftEther(Access.UserConnectorType), Str.NormalizeStrSoftEther(Access.TelcoConnectorType));
                    w.WriteLine("FTM: {0} {1}", Access.TelcoFloor, Access.TelcoBoard);
                    string user_str = Str.NormalizeStrSoftEther(Access.UserAddress);
                    if (Str.IsEmptyStr(Access.UserFloor) == false)
                    {
                        user_str += "-" + Str.NormalizeStrSoftEther(Access.UserFloor);
                    }
                    w.WriteLine(user_str);
                    if (Access.HasKounai)
                    {
                        w.WriteLine("【第 2 PD 利用】");
                    }
                    break;

                case DFObjType.Transit:
                    w.WriteLine(Transit.Loss.ToString("F1") + "db");
                    w.WriteLine(Transit.RouteCode);
                    w.WriteLine(Str.NormalizeStrSoftEther(Transit.CableName));
                    w.WriteLine("{0}: {1} {2} {3} #{4}", Transit.Telco_A_Bldg, Transit.Telco_A_Floor, Transit.Telco_A_Board, Str.NormalizeStrSoftEther(Transit.Telco_A_ConnectorType), Transit.CableStrand_A);
                    w.WriteLine("{0}: {1} {2} {3} #{4}", Transit.Telco_B_Bldg, Transit.Telco_B_Floor, Transit.Telco_B_Board, Str.NormalizeStrSoftEther(Transit.Telco_B_ConnectorType), Transit.CableStrand_B);
                    break;

                case DFObjType.Trunk:
                    w.WriteLine(Trunk);
                    break;

                case DFObjType.Rack:
                    w.WriteLine(Str.NormalizeStrSoftEther(RackName));
                    break;
            }

            return w.ToString();
        }
    }

    public string Place
    {
        get
        {
            switch (Type)
            {
                case DFObjType.Access:
                    return Str.NormalizeStrSoftEther(Access.UserBldg);
                case DFObjType.Indoor:
                    return "NTT " + Indoor.TelcoBldg;
                case DFObjType.Transit:
                    return "(" + ((double)Transit.Distance / 1000.0).ToString("F1") + " km)";
                case DFObjType.Rack:
                    return "NTT " + RackPlace;
                case DFObjType.Trunk:
                    return "NTT " + RackPlace;
                case DFObjType.Empty:
                    return "未接続";
                default:
                    throw new NotImplementedException();
            }
        }
    }

    public string Title
    {
        get
        {
            switch (Type)
            {
                case DFObjType.Access:
                    return "加入 " + Access.ID;
                case DFObjType.Indoor:
                    return "局内 " + Indoor.ID;
                case DFObjType.Transit:
                    if (this.Transit.IsKenkan == false)
                    {
                        return "中継 " + Transit.ID;
                    }
                    else
                    {
                        return "県間 " + Transit.ID;
                    }
                case DFObjType.Rack:
                    return string.Format("義務的コロケーションラック設備", RackPlace);
                case DFObjType.Trunk:
                    return "義務コロ自前ケーブル";
                case DFObjType.Empty:
                    return "未接続";
                default:
                    throw new NotImplementedException();
            }
        }
    }
}

public class DFAccess
{
    public bool InUse = false;
    public string ID = "";
    public string TelcoBldg = "";
    public string TelcoFloor = "";
    public string TelcoBoard = "";
    public string TelcoConnectorType = "";
    public string UserBldg = "";
    public string UserAddress = "";
    public string UserFloor = "";
    public string UserConnectorType = "";
    public string CableName = "";
    public string Trunk = "";
    public int CableStrand = 0;
    public double Loss = 0.0;
    public DateTime StartDt = Util.ZeroDateTimeValue;
    public DateTime EndDt = Util.ZeroDateTimeValue;
    public bool HasKounai;

    public bool Flag1 = false;
}

public class DFTransit
{
    public bool InUse = false;
    public string ID = "";
    public string RouteCode = "";
    public string Telco_A_Bldg = "";
    public string Telco_A_Floor = "";
    public string Telco_A_Board = "";
    public string Telco_A_ConnectorType = "";
    public string Telco_B_Bldg = "";
    public string Telco_B_Floor = "";
    public string Telco_B_Board = "";
    public string Telco_B_ConnectorType = "";
    public string CableName = "";
    public int CableStrand_A = 0;
    public int CableStrand_B = 0;
    public int Distance;
    public double Loss = 0.0;
    public DateTime StartDt = Util.ZeroDateTimeValue;
    public DateTime EndDt = Util.ZeroDateTimeValue;
    public bool IsKenkan = false;

    public bool Flag1 = false;
}

public class DFIndoor
{
    public bool InUse = false;
    public string ID = "";
    public string TelcoBldg = "";
    public string A_ID = "";
    public string B_ID = "";
    public string A_Rack = "";
    public string B_Rack = "";
    public DateTime StartDt = Util.ZeroDateTimeValue;
    public DateTime EndDt = Util.ZeroDateTimeValue;

    public bool Flag1 = false;
}

public class DFGenbo
{
    public List<DFAccess> AccessList = new List<DFAccess>();
    public List<DFTransit> TransitList = new List<DFTransit>();
    public List<DFIndoor> IndoorList = new List<DFIndoor>();

    public void Load(string genbo_dir)
    {
        // 原簿を読み込み
        var files = Lfs.EnumDirectory(genbo_dir);

        string tanmatu = files.Where(x => x.IsFile && PP.GetExtension(x.Name)._IsSamei(".csv") && PP.GetFileNameWithoutExtension(x.Name).EndsWith("_tanmatu")).Select(x => x.FullPath).SingleOrDefault();
        string tyuukei = files.Where(x => x.IsFile && PP.GetExtension(x.Name)._IsSamei(".csv") && PP.GetFileNameWithoutExtension(x.Name).EndsWith("_tyuukei")).Select(x => x.FullPath).SingleOrDefault();
        string kyokunai = files.Where(x => x.IsFile && PP.GetExtension(x.Name)._IsSamei(".csv") && PP.GetFileNameWithoutExtension(x.Name).EndsWith("_kyokunai")).Select(x => x.FullPath).SingleOrDefault();

        if (tanmatu._IsEmpty())
        {
            throw new CoresException($"ディレクトリ '{genbo_dir}' には、tanmatu の CSV ファイルが見つかりませんでした。");
        }

        if (tyuukei._IsEmpty())
        {
            throw new CoresException($"ディレクトリ '{genbo_dir}' には、tyuukei の CSV ファイルが見つかりませんでした。");
        }

        if (kyokunai._IsEmpty())
        {
            throw new CoresException($"ディレクトリ '{genbo_dir}' には、kyokunai の CSV ファイルが見つかりませんでした。");
        }

        load_access_csv(tanmatu);
        load_transit_csv(tyuukei);
        load_indoor_csv(kyokunai);
    }

    public DFTransit SearchTransitById(string id)
    {
        foreach (DFTransit a in this.TransitList)
        {
            if (a.InUse)
            {
                if (a.ID == id)
                {
                    return a;
                }
            }
        }

        return null;
    }

    public DFAccess SearchAccessById(string id)
    {
        foreach (DFAccess a in this.AccessList)
        {
            if (a.InUse)
            {
                if (a.ID == id)
                {
                    return a;
                }
            }
        }
        return null;
    }

    public DFIndoor SearchIndoorByABId(string id, string exclude_id)
    {
        foreach (DFIndoor a in this.IndoorList)
        {
            if (a.InUse)
            {
                if (a.A_ID == id || a.B_ID == id)
                {
                    bool ok = true;
                    if (Str.IsEmptyStr(exclude_id) == false)
                    {
                        if (a.ID == exclude_id)
                        {
                            ok = false;
                        }
                    }

                    if (ok)
                    {
                        return a;
                    }
                }
            }
        }
        return null;
    }

    string kaisen_id_to_str(string id)
    {
        try
        {
            if (id.StartsWith("回線ＩＤ："))
            {
                id = id.Substring(5);
                Str.NormalizeString(ref id, false, true, false, false);
                return id;
            }
        }
        catch
        {
        }
        return "";
    }

    void load_indoor_csv(string csv_fn)
    {
        Con.WriteLine("局内回線原簿 '{0}' 読み込み", csv_fn);

        Csv csv = new Csv(csv_fn);

        foreach (CsvEntry c in csv.Items)
        {
            if (c.Count != 0 && c[0] != "通番")
            {
                if (c.Count != 40 && c.Count != 41)
                {
                    throw new ApplicationException(string.Format("{0}: CSV 列の数が不正: {1}", csv_fn, c.ToString()));
                }

                DFIndoor a = new DFIndoor();
                a.ID = c[6];
                if (c[7] != "局内伝送路") throw new ApplicationException(string.Format("加入回線でない"));
                a.InUse = (c[8] == "使用中");
                a.TelcoBldg = c[10];
                a.A_ID = c[13];
                a.B_ID = c[23];

                if (c[14] == "")
                {
                    string kari_id = kaisen_id_to_str(c[11]);

                    if (kari_id == "")
                    {
                        a.A_ID = "";
                        a.A_Rack = c[11];
                    }
                    else
                    {
                        a.A_ID = kari_id;
                    }
                }

                if (c[24] == "")
                {
                    string kari_id = kaisen_id_to_str(c[21]);

                    if (kari_id == "")
                    {
                        a.B_ID = "";
                        a.B_Rack = c[21];
                    }
                    else
                    {
                        a.B_ID = kari_id;
                    }
                }

                a.StartDt = Str.StrToDate(c[38]);
                if (Str.IsEmptyStr(c[39]) == false)
                    a.EndDt = Str.StrToDate(c[39]);
                else
                    a.EndDt = new DateTime(2038, 1, 1);

                this.IndoorList.Add(a);
            }
        }
    }

    void load_transit_csv(string csv_fn)
    {
        Con.WriteLine("中継回線原簿 '{0}' 読み込み", csv_fn);

        Csv csv = new Csv(csv_fn);

        foreach (CsvEntry c in csv.Items)
        {
            if (c.Count != 0 && c[0] != "通番")
            {
                if (c.Count != 34 && c.Count != 35)
                {
                    throw new ApplicationException(string.Format("{0}: CSV 列の数が不正: {1}", csv_fn, c.ToString()));
                }

                DFTransit a = new DFTransit();
                a.ID = c[6];
                if (c[7] != "中継回線") throw new ApplicationException(string.Format("中継回線でない"));
                a.InUse = (c[8] == "使用中");
                a.RouteCode = c[9];
                a.Telco_A_Bldg = c[10];
                a.Telco_A_Floor = c[11];
                a.Telco_A_Board = c[12];
                a.Telco_A_ConnectorType = c[14];
                a.CableName = c[17];
                a.CableStrand_A = Str.StrToInt(c[18]);
                a.Telco_B_Bldg = c[19];
                a.Telco_B_Floor = c[20];
                a.Telco_B_Board = c[21];
                a.Telco_B_ConnectorType = c[23];
                a.CableStrand_B = Str.StrToInt(c[27]);
                a.Distance = Str.StrToInt(c[28]);
                a.Loss = double.Parse(c[29]);
                a.StartDt = Str.StrToDate(c[32]);
                if (Str.IsEmptyStr(c[33]) == false)
                    a.EndDt = Str.StrToDate(c[33]);
                else
                    a.EndDt = new DateTime(2038, 1, 1);

                if (a.RouteCode.StartsWith("EKK", StringComparison.InvariantCultureIgnoreCase))
                {
                    a.IsKenkan = true;
                }

                this.TransitList.Add(a);
            }
        }
    }

    void load_access_csv(string csv_fn)
    {
        Con.WriteLine("加入回線原簿 '{0}' 読み込み", csv_fn);

        Csv csv = new Csv(csv_fn);

        foreach (CsvEntry c in csv.Items)
        {
            if (c.Count != 0 && c[0] != "通番")
            {
                if (c.Count != 40)
                {
                    throw new ApplicationException(string.Format("{0}: CSV 列の数が不正: {1}", csv_fn, c.ToString()));
                }

                DFAccess a = new DFAccess();
                a.ID = c[6];
                if (c[7] != "端末回線") throw new ApplicationException(string.Format("加入回線でない"));
                a.InUse = (c[8] == "使用中");
                a.TelcoBldg = c[9];
                a.TelcoFloor = c[10];
                a.TelcoBoard = c[11];
                a.TelcoConnectorType = c[13];
                a.UserBldg = c[23];
                a.UserAddress = c[24];
                a.UserFloor = c[26];
                a.UserConnectorType = c[29];
                a.CableName = c[30];
                a.CableStrand = Str.StrToInt(c[31]);
                a.Loss = double.Parse(c[32]);
                a.StartDt = Str.StrToDate(c[37]);
                if (Str.IsEmptyStr(c[38]) == false)
                    a.EndDt = Str.StrToDate(c[38]);
                else
                    a.EndDt = new DateTime(2038, 1, 1);

                if (c[9] == "快適天空" && c[17] == "3F" && (c[22] == "49" || c[22] == "50"))
                {
                    a.Trunk = string.Format("3F IDM-{2}/段: {3}/列: {0}/端子: {1}", c[22], c[20], c[19], c[21]);
                }

                /*
                if (this.KounaiIdList.Contains(a.ID))
                {
                    a.HasKounai = true;
                }*/

                if (Str.InStr(c[35], "有"))
                {
                    a.HasKounai = true;
                }

                this.AccessList.Add(a);
            }
        }
    }
}

#nullable restore


#endif

