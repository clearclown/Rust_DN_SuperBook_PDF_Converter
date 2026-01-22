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
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;
using System.Text;
using System.Diagnostics;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

namespace IPA.Cores.Basic;

public static partial class DevTools
{
    public static void WriteToFile(string path, string bodyString, Encoding? encoding = null, bool writeBom = false, bool noDebug = false)
    {
        bodyString = bodyString._NonNull();
        bodyString = Str.NormalizeCrlf(bodyString, CrlfStyle.LocalPlatform);

        Lfs.WriteStringToFile(path, bodyString, FileFlags.AutoCreateDirectory | FileFlags.WriteOnlyIfChanged,
            encoding: encoding, writeBom: writeBom);

        if (noDebug == false)
        {
            Con.WriteDebug($"--- WriteToFile \"{path}\" ---");
            Con.WriteDebug(bodyString);
            Con.WriteDebug($"--- EOF ---");
            Con.WriteDebug();
        }
    }

    // テスト証明書
    // SHA256: 8A18D75E4702CC5138F54DAC4C8C88B49C9D1A9E2B556C8B10A6C779658E0026
    static readonly Singleton<PalX509Certificate> TestSampleCert_Singleton = new Singleton<PalX509Certificate>(() => new PalX509Certificate(new FilePath(Res.Cores, "SampleDefaultCert/SampleDefaultCert.p12")));
    public static PalX509Certificate TestSampleCert => TestSampleCert_Singleton;

    // デバッグ用 CA (旧)
    // SHA256: D9413D2F2E278BCDB277CD9321E5B9F0CB5EC468AF645C3EFD4942F9238FCE8F
    static readonly Singleton<PalX509Certificate> CoresDebugCACert_Singleton = new Singleton<PalX509Certificate>(() => new PalX509Certificate(new FilePath(Res.Cores, "SampleDefaultCert/190804CoresDebugCA.p12")));
    [Obsolete("Please use " + nameof(CoresDebugCACert_20221125))]
    public static PalX509Certificate CoresDebugCACert => CoresDebugCACert_Singleton;
    
    // デバッグ用 CA (新)
    // SHA256: 61C9901598E38228EC02B3DCC7A4BF9FE2BE610EC7FC0DA2E87898E69C6E797D
    static readonly Singleton<PalX509Certificate> CoresDebugCACert_20221125_Singleton = new Singleton<PalX509Certificate>(() => new PalX509Certificate(new FilePath(Res.Cores, "SampleDefaultCert/221125CoresDebugCA_20221125.p12")));
    public static PalX509Certificate CoresDebugCACert_20221125 => CoresDebugCACert_20221125_Singleton;

    // .h ファイルの定数一覧を読み込む
    public static Dictionary<string, int> ParseHeaderConstants(string body)
    {
        string[] lines = body._GetLines();

        Dictionary<string, int> ret = new Dictionary<string, int>();

        foreach (string line in lines)
        {
            string[] tokens = line._Split(StringSplitOptions.RemoveEmptyEntries, ' ', '\t');

            if (tokens.Length >= 3)
            {
                if (tokens[0] == "#define")
                {
                    if (tokens[2]._IsNumber())
                    {
                        ret.TryAdd(tokens[1], tokens[2]._ToInt());
                    }
                }
            }
        }

        return ret;
    }

    static readonly Singleton<PrivKey> AutoGeneratingDebugCertKey = new Singleton<PrivKey>(() =>
    {
        PkiUtil.GenerateRsaKeyPair(2048, out PrivKey newKey, out _);
        return newKey;
    });

    static readonly Singleton<string, PalX509Certificate> AutoGeneratingDebugCertSingleton = new Singleton<string, PalX509Certificate>(hostname =>
    {
        var ca = CoresDebugCACert_20221125;

        var newKey = AutoGeneratingDebugCertKey.CreateOrGet();

        Certificate newCert = new Certificate(newKey, ca, new CertificateOptions(PkiAlgorithm.RSA, cn: hostname.Trim(), c: "US", type: CertificateOptionsType.ServerCertificate));
        CertificateStore newCertStore = new CertificateStore(newCert, newKey);

        return newCertStore.X509Certificate;
    }, StrCmpi);

    public static PalX509Certificate GetAutoGeneratingDebugCert(string hostname) => AutoGeneratingDebugCertSingleton[hostname];
}

