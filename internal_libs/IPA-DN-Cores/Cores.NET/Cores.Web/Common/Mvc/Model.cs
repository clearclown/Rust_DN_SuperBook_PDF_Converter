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

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

using IPA.Cores.Web;
using IPA.Cores.Helper.Web;
using static IPA.Cores.Globals.Web;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Razor;

namespace IPA.Cores.Web;

// ASP.NET Core のテンプレートの「ErrorViewModel.cs」からもらってきた。
// 共通クラスにするのである。
public class AspNetErrorModel
{
    public string RequestId { get; set; } = "";

    public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);

    public Exception ErrorInfo { get; set; } = null!;

    public string ErrorPath { get; set; } = "Unknown";

    public string WebServerName { get; set; } = "Unknown";

    public string WebClientName { get; set; } = "Unknown";

    public AspNetErrorModel() { }

    public AspNetErrorModel(Controller controller)
    {
        RequestId = controller.HttpContext.TraceIdentifier;
        ErrorInfo = controller._GetLastError();
        ErrorPath = controller._GetLastErrorPath();
        WebServerName = controller.Request.Host.ToString();
        WebClientName = controller.HttpContext.Connection.RemoteIpAddress!.MapToIPv4().ToString();
    }
}

[Flags]
public enum ModelMode
{
    Unknown = 0,
    Add,
    Edit,
    Delete,
}

public class DualData<TData1, TData2> : SingleData<TData1>
    where TData1 : new()
    where TData2 : new()
{
    public TData2 Data2 { get; set; }
    public string? Id2 { get; set; }

    public DualData() : base()
    {
        this.Data2 = new TData2();
    }

    public DualData(string id1, TData1 data1, string? id2, TData2 data2, ModelMode mode) : base(id1, data1, mode)
    {
        if (mode != ModelMode.Add)
        {
            if (id2._IsEmpty())
            {
                throw new ArgumentNullException(nameof(id2));
            }

            id2 = id2._NonNullTrim();
        }
        else
        {
            if (id2._IsFilled())
            {
                throw new ArgumentOutOfRangeException($"{nameof(id2)} is specified.");
            }
            id2 = null;
        }

        this.Id2 = id2;
        this.Data2 = data2;

        NormalizeImpl();
    }

    protected override void NormalizeImpl()
    {
        base.NormalizeImpl();
        if (this.Data2 is INormalizable normalize) normalize.Normalize();
    }
}

public class SingleData<TData> where TData : new()
{
    public TData Data { get; set; }
    public string? Id { get; set; }
    public ModelMode Mode { get; set; }

    public SingleData()
    {
        this.Data = new TData();
        this.Id = null;
        this.Mode = ModelMode.Unknown;

        NormalizeImpl();
    }

    public SingleData(string? id, TData data, ModelMode mode)
    {
        if (mode != ModelMode.Add)
        {
            if (id._IsEmpty())
            {
                throw new ArgumentNullException(nameof(id));
            }

            id = id._NonNullTrim();
        }
        else
        {
            if ((IsFilled)id)
            {
                throw new ArgumentOutOfRangeException($"{nameof(id)} is specified.");
            }
            id = null;
        }

        this.Id = id;
        this.Data = data;
        this.Mode = mode;

        NormalizeImpl();

        //            if (this.Data is IErrorCheckable check) check.CheckError();
    }

    // データの正規化
    protected virtual void NormalizeImpl()
    {
        if (this.Data is INormalizable normalize) normalize.Normalize();
    }

    // 操作を分かりやすく示すタイトル文字列の生成
    public virtual string GetOperationTitle()
    {
        switch (Mode)
        {
            case ModelMode.Add:
                return "追加";

            case ModelMode.Edit:
                if ((this.Data?.ToString())._IsFilled())
                    return $"「{this.Data.ToString()}」の編集";
                else
                    return $"編集";

            case ModelMode.Delete:
                return $"「{this.Data?.ToString()}」の削除";

            default:
                throw new ArgumentOutOfRangeException(nameof(Mode));
        }
    }

    // ボタン名の生成
    public virtual string GetButtonTitle()
    {
        switch (Mode)
        {
            case ModelMode.Add:
                return "追加";

            case ModelMode.Edit:
                return "保存";

            case ModelMode.Delete:
                return "削除";

            default:
                throw new ArgumentOutOfRangeException(nameof(Mode));
        }
    }
}
