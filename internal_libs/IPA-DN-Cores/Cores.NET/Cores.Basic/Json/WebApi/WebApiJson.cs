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

#if CORES_BASIC_JSON

using System;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using System.IO;
using System.Diagnostics.CodeAnalysis;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

namespace IPA.Cores.Basic;

public partial class WebRet
{
    dynamic? jsonDynamic = null;
    public dynamic? JsonDynamic
    {
        get
        {
            if (jsonDynamic == null)
                jsonDynamic = Json.DeserializeDynamic(this.ToString());
            return jsonDynamic;
        }
    }

    [return: MaybeNull]
    public T Deserialize<T>(bool checkError = false)
    {
        T? ret = Json.Deserialize<T>(this.ToString(), this.Api.Json_IncludeNull, this.Api.Json_MaxDepth);

        if (checkError)
        {
            if (ret is IValidatable b)
            {
                b.Validate();
            }
        }

        return ret;
    }

    public string NormalizedJsonStr => this.Data._GetString_UTF8()._JsonNormalize();
}

public partial class WebApi
{
    public int? Json_MaxDepth { get; set; } = Json.DefaultMaxDepth;

    public bool Json_IncludeNull { get; set; } = false;
    public bool Json_EscapeHtml { get; set; } = false;

    public string JsonSerialize(object? obj, Type? type = null)
        => Json.Serialize(obj, this.Json_IncludeNull, this.Json_EscapeHtml, this.Json_MaxDepth, type: type);

    public virtual async Task<WebRet> RequestWithJsonObjectAsync(WebMethods method, string url, object? jsonObject, CancellationToken cancel = default, string postContentType = Consts.MimeTypes.Json)
        => await SimplePostJsonAsync(method, url, this.JsonSerialize(jsonObject), cancel, postContentType);

    public virtual async Task<WebRet> RequestWithJsonDynamicAsync(WebMethods method, string url, dynamic? jsonDynamic, CancellationToken cancel = default, string postContentType = Consts.MimeTypes.Json)
        => await SimplePostJsonAsync(method, url, Json.SerializeDynamic(jsonDynamic), cancel, postContentType);
}

#endif  // CORES_BASIC_JSON

