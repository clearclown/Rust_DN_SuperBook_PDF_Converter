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
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Serialization;
using System.Collections.Immutable;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

namespace IPA.Cores.Basic;

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public class SimpleTableOrderAttribute : Attribute
{
    public SimpleTableOrderAttribute(double order = 0)
    {
        this.Order = order;
    }
    public double Order { get; set; } = 0;
}

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public class SimpleTableIgnoreAttribute : Attribute { }

public class SimpleTableDynamicColumn<TRow>
{
    public string Name { get; }
    public double Order { get; }
    public Func<TRow, string?> GetStrProc;

    public SimpleTableDynamicColumn(string name, double order, Func<TRow, string?> getStrProc)
    {
        this.Name = name;
        this.Order = order;
        this.GetStrProc = getStrProc;
    }
}

public class SimpleTableView<TRow>
{
    readonly FieldReaderWriter Rw = FieldReaderWriter.GetCached<TRow>();

    public IReadOnlyList<string> OrderedColumnNamesList;

    readonly Dictionary<string, SimpleTableDynamicColumn<TRow>> DynamicColumnTable = new Dictionary<string, SimpleTableDynamicColumn<TRow>>();

    public IEnumerable<TRow>? Data { get; }

    public SimpleTableView(IEnumerable<TRow>? data, params SimpleTableDynamicColumn<TRow>[] dynamicColumns)
    {
        this.Data = data;

        var names = this.Rw.OrderedPublicFieldOrPropertyNamesList;

        List<Pair3<string, double, int>> cols = new List<Pair3<string, double, int>>();

        int i = 0;
        foreach (string name in names)
        {
            int naturalOrder = ++i;

            var metadata = Rw.MetadataTable[name];
            var orderAttribute = metadata.GetCustomAttributes<SimpleTableOrderAttribute>().FirstOrDefault();
            var ignoreAttribute = metadata.GetCustomAttributes<SimpleTableIgnoreAttribute>().FirstOrDefault();

            if (ignoreAttribute != null) continue;

            double customOrder = orderAttribute?.Order ?? double.MaxValue;

            cols.Add(new Pair3<string, double, int>(name, customOrder, naturalOrder));
        }

        foreach (var dynamicColumn in dynamicColumns)
        {
            cols.Add(new Pair3<string, double, int>(dynamicColumn.Name, dynamicColumn.Order, ++i));

            DynamicColumnTable.TryAdd(dynamicColumn.Name, dynamicColumn);
        }

        this.OrderedColumnNamesList = cols.OrderBy(x => x.B).ThenBy(x => x.C).Select(x => x.A).ToList();
    }

    public string GenerateHtml(string id = "")
    {
        StringWriter w = new StringWriter();
        if (id._IsEmpty()) id = Str.NewUid("ID", '_');

        w.WriteLine($"<table cellspacing=\"0\" cellpadding=\"4\" border=\"0\" id=\"{id}\" style=\"color:#333333;border-collapse:collapse;\">");
        w.WriteLine($"  <tr style=\"color: White; background-color:#507CD1;font-weight:bold;\">");

        foreach (var name in this.OrderedColumnNamesList)
        {
            w.WriteLine($"    <th scope=\"col\" style=\"white-space: nowrap;\">{name._EncodeHtml()}</th>");
        }

        w.WriteLine($"  </tr>");

        int i = 0;
        if (this.Data != null)
        {
            foreach (var row in this.Data)
            {
                if (row != null)
                {
                    string color = ((i % 2) == 0) ? "#EFF3FB" : "White";

                    w.WriteLine($"  <tr style=\"background-color:{color};\">");

                    foreach (var name in this.OrderedColumnNamesList)
                    {
                        string dataStr;

                        var dynamicColumn = this.DynamicColumnTable._GetOrDefault(name);
                        if (dynamicColumn != null)
                        {
                            dataStr = dynamicColumn.GetStrProc(row)._NonNull();
                        }
                        else
                        {
                            object? obj = Rw.GetValue(row, name);

                            dataStr = obj?.ToString()._NonNull() ?? "";

                            switch (obj)
                            {
                                case DateTime dt:
                                    dataStr = dt._ToDtStr();
                                    break;

                                case DateTimeOffset dt:
                                    dataStr = dt._ToDtStr();
                                    break;

                                case TimeSpan ts:
                                    dataStr = ts._ToTsStr();
                                    break;
                            }
                        }

                        string dataHtml = dataStr._EncodeHtml(true, true);

                        w.WriteLine($"    <td style=\"white-space: nowrap;\">{dataHtml}</td>");
                    }

                    w.WriteLine($"  </tr>");

                    i++;
                }
            }
        }

        w.WriteLine($"</table>");

        return w.ToString();
    }
}

#endif

