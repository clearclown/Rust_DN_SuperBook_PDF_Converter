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
// Authenticode Sign Client

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
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Serialization;
using System.Net;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

namespace IPA.Cores.Basic;

public class AuthenticodeSignClient : AsyncService
{
    const string SeInternalPasswordFilePathDefault = @"\\192.168.3.2\share\tmp\signserver\password.txt";

    readonly string Url;

    readonly WebApi Web;

    string? SeInternalPasswordCache = null;

    public AuthenticodeSignClient(string url, string sslSha, TcpIpSystem? tcpIp = null)
    {
        try
        {
            this.Url = url;

            this.Web = new WebApi(new WebApiOptions(new WebApiSettings { MaxRecvSize = Consts.Numbers.SignCodeServerMaxFileSize, SslAcceptCertSHAHashList = sslSha._SingleList() }, tcpIp));
        }
        catch (Exception ex)
        {
            this._DisposeSafe(ex);
            throw;
        }
    }

    public async Task<byte[]> SignSeInternalAsync(ReadOnlyMemory<byte> srcData, string certName, string flags, string comment, int numRetry = 5, CancellationToken cancel = default,
        string passwordFilePath = SeInternalPasswordFilePathDefault)
    {
        if (SeInternalPasswordCache._IsEmpty())
        {
            SeInternalPasswordCache = await Lfs.ReadStringFromFileAsync(passwordFilePath, oneLine: true);
        }

        return await SignAsync(SeInternalPasswordCache, srcData, certName, flags, comment, numRetry, cancel);
    }

    public async Task<byte[]> SignAsync(string password, ReadOnlyMemory<byte> srcData, string certName, string flags, string comment, int numRetry = 5, CancellationToken cancel = default)
    {
        QueryStringList qs = new QueryStringList();

        qs.Add("password", password);
        qs.Add("cert", certName);
        qs.Add("flags", flags);
        qs.Add("comment", comment);
        qs.Add("numretry", numRetry.ToString());

        WebRet ret = await this.Web.SimplePostDataAsync(this.Url + "?" + qs, srcData.ToArray(), cancel, Consts.MimeTypes.OctetStream);

        if (ret.Data.Length <= (srcData.Length * 9L / 10L))
        {
            throw new CoresException("ret.Data.Length <= (srcData.Length * 9L / 10L)");
        }

        if (ExeSignChecker.CheckFileDigitalSignature(ret.Data, flags._InStr("driver", true)) == false)
        {
            throw new CoresException("CheckFileDigitalSignature failed.");
        }

        return ret.Data;
    }

    protected override void DisposeImpl(Exception? ex)
    {
        try
        {
            this.Web._DisposeSafe();
        }
        finally
        {
            base.DisposeImpl(ex);
        }
    }
}

#endif

