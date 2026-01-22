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
using System.Collections.Immutable;
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
using IPA.Cores.Basic.DnsLib;

namespace IPA.Cores.Basic;


public class EasyDnsResponderBasedDnsServerSettings
{
    public int UdpPort { init; get; }
    public int TcpPort { init; get; }
}

public class EasyDnsResponderBasedDnsServer : AsyncService
{
    public EasyDnsServer DnsServer { get; }
    public EasyDnsResponder DnsResponder { get; }

    public bool SaveAccessLogForDebug { get; private set; }
    public bool CopyQueryAdditionalRecordsToResponse { get; set; } = false;

    public EasyDnsResponderBasedDnsServer(EasyDnsResponderBasedDnsServerSettings settings)
    {
        try
        {
            this.DnsResponder = new EasyDnsResponder();
            this.DnsServer = new EasyDnsServer(new EasyDnsServerSetting(this.DnsQueryResponseCallback, this.DnsTcpAxfrCallbackAsync, this.GetNotifyPacketsCallback, settings.UdpPort, settings.TcpPort));
        }
        catch
        {
            this._DisposeSafe();
            throw;
        }
    }

    protected override async Task CleanupImplAsync(Exception? ex)
    {
        try
        {
            await this.DnsServer._DisposeSafeAsync();
        }
        finally
        {
            await base.CleanupImplAsync(ex);
        }
    }

    public void ApplySetting(EasyDnsResponderSettings setting)
    {
        this.DnsResponder.ApplySetting(setting);

        this.SaveAccessLogForDebug = setting.SaveAccessLogForDebug;
        this.CopyQueryAdditionalRecordsToResponse = setting.CopyQueryAdditionalRecordsToResponse;
    }

    public class DnsAccessLog
    {
        public DnsUdpPacket? ReceivePacket;
        public DnsUdpPacket? SubmitPacket;
        public DnsUdpPacket[]? ForwardedSubmitPackets;
        public string TookSeconds = "";
    }

    // 前回のゾーンのバージョン
    StrDictionary<int> ZoneVersionsForNotify = new StrDictionary<int>();

    // ゾーン更新通知パケット生成コールバック関数 (リエントラントではないので注意！)
    List<Tuple<string, DnsUdpPacket>> GetNotifyPacketsCallback(EasyDnsServer svr)
    {
        List<Tuple<string, DnsUdpPacket>> ret = new List<Tuple<string, DnsUdpPacket>>();

        var zones = this.DnsResponder.GetZonesList();

        foreach (var zone in zones)
        {
            var soa = zone.SOARecord.ToDnsLibRecordBase(zone.DomainName, this.DnsResponder.BootDateTime, zone.Version);

            if (this.ZoneVersionsForNotify._GetOrDefault(zone.DomainFqdn, -1) != zone.Version)
            {
                this.ZoneVersionsForNotify[zone.DomainFqdn] = zone.Version;

                DnsMessage msg = new DnsMessage();
                msg.Flags = 0x2400; // Zone change notificateion

                var query = new DnsQuestion(zone.DomainName, RecordType.Soa, RecordClass.INet);

                msg.Questions.Add(query);
                msg.AnswerRecords.Add(soa);

                ret.Add(new Tuple<string, DnsUdpPacket>(zone.NotifyServers, new DnsUdpPacket(IPUtil.LocalHostIPv4HttpEndPoint, IPUtil.LocalHostIPv4HttpEndPoint, msg)));
            }
        }

        return ret;
    }

    // DNS サーバーから呼ばれる TCP AXFR リクエストに対するコールバック関数。ここで TCP レコード応答リストを作る。
    async Task DnsTcpAxfrCallbackAsync(EasyDnsServer svr, EasyDnsServerTcpAxfrCallbackParam p)
    {
        if (this.DnsResponder.TcpAxfrCallback == null)
        {
            throw new CoresException("this.DnsResponder.TcpAxfrCallback is not implemented");
        }

        string zoneFqdn = p.Question.Name.ToNormalizedFqdnFast();
        var zone = this.DnsResponder.GetExactlyMatchZone(zoneFqdn);

        if (zone == null)
        {
            throw new CoresException($"Specified zone '{zoneFqdn}' is not defined in the database. TCP AXFR requested client: {p.RequestPacket.RemoteEndPoint.ToString()}");
        }

        // ACL の検査
        var aclResult = await EasyIpAcl.EvaluateWithFqdnIncludedAsync(zone.TcpAxfrAllowedAcl, p.RequestPacket.RemoteEndPoint.Address,
            EasyIpAclAction.Deny, EasyIpAclAction.Deny, true);

        if (aclResult != EasyIpAclAction.Permit)
        {
            throw new CoresException($"The DNS zone '{zoneFqdn}''s TcpAxfrAllowedAcl did not allow the TCP AXFR requested client '{p.RequestPacket.RemoteEndPoint.ToString()}' to execute the TCP AXFR command. Please add this host to the TcpAxfrAllowedAcl ACL rule of this DNS zone.");
        }

        EasyDnsResponderTcpAxfrCallbackRequest req = new EasyDnsResponderTcpAxfrCallbackRequest
        {
            CallbackParam = p,
            RequestPacket = p.RequestPacket,
            Zone = zone.SrcZone,
            ZoneInternal = zone,
        };

        var cancel = req.Cancel;

        // SOA レコード (開始を意味する)
        await req.SendBufferedAsync(zone.SOARecord, null, cancel, this.DnsResponder.BootDateTime, zone.Version);

        // 本体
        await this.DnsResponder.TcpAxfrCallback(req);

        // SOA レコード (終了を意味する)
        await req.SendBufferedAsync(zone.SOARecord, null, cancel, this.DnsResponder.BootDateTime, zone.Version);
    }

    // DNS サーバーから呼ばれる通常クエリに対するコールバック関数。ここでクエリに対する応答を作る。
    List<DnsUdpPacket> DnsQueryResponseCallback(EasyDnsServer svr, List<DnsUdpPacket> requestPackets)
    {
        List<DnsUdpPacket> responsePackets = new List<DnsUdpPacket>(requestPackets.Count);

        bool debug = this.SaveAccessLogForDebug;

        foreach (var request in requestPackets)
        {
            try
            {
                long startTick = 0;
                long endTick = 0;
                DnsUdpPacket? requestCopy = null;

                if (debug)
                {
                    startTick = Time.NowHighResLong100Usecs;
                    requestCopy = request._CloneDeep();
                }

                var response = RequestPacketToResponsePacket(request, out var alternativeSendPacketsList);

                if (debug)
                {
                    endTick = Time.NowHighResLong100Usecs;

                    long timespan = endTick - startTick;

                    DnsAccessLog log = new DnsAccessLog
                    {
                        TookSeconds = ((double)timespan / 10000000.0).ToString("F9"),
                        ReceivePacket = requestCopy,
                        SubmitPacket = response,
                    };

                    if (alternativeSendPacketsList != null && alternativeSendPacketsList.Any())
                    {
                        log.ForwardedSubmitPackets = alternativeSendPacketsList.ToArray();
                    }

                    log._PostAccessLog("DDnsAccessLogForDebug");
                }

                if (response != null)
                {
                    responsePackets.Add(response);
                }

                if (alternativeSendPacketsList != null)
                {
                    responsePackets.AddRange(alternativeSendPacketsList);
                }
            }
            catch (Exception ex)
            {
                ex._Debug();
            }
        }

        return responsePackets;
    }

    // ある 1 つの DNS リクエストパケットに対して DNS レスポンスパケットを作る関数
    DnsUdpPacket? RequestPacketToResponsePacket(DnsUdpPacket request, out List<DnsUdpPacket>? alternativeSendPacketsList)
    {
        alternativeSendPacketsList = null;
        DnsMessage? q = request.Message as DnsMessage;

        if (q == null) return null;

        DnsMessage? r = null;

        try
        {
            r = QueryToResponse(q, request, out alternativeSendPacketsList);
        }
        catch (Exception ex)
        {
            r = q;
            r.IsQuery = false;
            r.ReturnCode = ReturnCode.ServerFailure;
            ex._Debug();
        }

        if (r == null) return null;

        DnsUdpPacket response = new DnsUdpPacket(request.RemoteEndPoint, request.LocalEndPoint, r);

        return response;
    }

    // ある 1 つの DNS クエリメッセージに対して応答メッセージを作る関数
    // CPU 時間の節約のため、届いたクエリメッセージ構造体をそのまま応答メッセージ構造体として書き換えて応答する
    DnsMessage? QueryToResponse(DnsMessage? q, DnsUdpPacket requestPacket, out List<DnsUdpPacket>? alternativeSendPacketsList)
    {
        alternativeSendPacketsList = null;

        if (q == null) return null;
        if (q.IsQuery == false)
        {
            // フォワーダからの戻りパケットの処理
            var responsePacket = this.DnsResponder.TryProcessForwarderResponse(requestPacket);
            if (responsePacket != null)
            {
                alternativeSendPacketsList = responsePacket._SingleList();
            }
            return null;
        }

        q.ReturnCode = ReturnCode.NoError;

        if (this.CopyQueryAdditionalRecordsToResponse == false)
        {
            // クエリに付いてきた Additional Records を削除 (dnsdist キャッシュ対策)
            q.AdditionalRecords.Clear();
        }

        // クエリに付いてきた Authority Records と Answer Records を削除
        q.AuthorityRecords.Clear();
        q.AnswerRecords.Clear();

        if (q.Questions.Count == 0)
        {
            // 質問が付いていない
            q.IsQuery = false;
            q.ReturnCode = ReturnCode.FormatError;
            return q;
        }

        if (q.Questions.Count >= 2)
        {
            // DNS クエリが複数付いている場合でも、最初の 1 個目だけに応答する。
            q.Questions.RemoveRange(1, q.Questions.Count - 1);
        }

        var question = q.Questions[0];
        string questionFqdn = question.Name.ToNormalizedFqdnFast();
        if (questionFqdn._IsEmpty())
        {
            // 質問の FQDN 名が不正である
            q.IsQuery = false;
            q.ReturnCode = ReturnCode.FormatError;
            return q;
        }

        var questionType = DnsUtil.DnsLibRecordTypeToEasyDnsResponderRecordType(question.RecordType);

        if (question.RecordType == RecordType.Axfr || question.RecordType == RecordType.Ixfr)
        {
            // UDP 経由で AXFR または IXFR リクエストが届いた場合は SOA 要求が届いたものとみなす
            questionType = EasyDnsResponderRecordType.SOA;
        }

        try
        {
            var searchRequest = new EasyDnsResponder.SearchRequest
            {
                FqdnNormalized = questionFqdn,
                RequestPacket = requestPacket,
            };

            var searchResponse = this.DnsResponder.Query(searchRequest, questionType);

            if (searchResponse == null)
            {
                // Zone 不存在。Refuse する。
                q.ReturnCode = ReturnCode.Refused;
                q.IsQuery = false;
                return q;
            }

            if (searchResponse.AlternativeSendPackets != null)
            {
                // 代替パケットを応答する (フォワーダ等が生成したパケットである)
                alternativeSendPacketsList = searchResponse.AlternativeSendPackets;
                return null;
            }

            q.IsQuery = false;

            if (searchResponse.RaiseCustomError != ReturnCode.NoError)
            {
                // カスタムエラー
                q.ReturnCode = searchResponse.RaiseCustomError;
                return q;
            }

            if (searchResponse.ResultFlags.Bit(EasyDnsResponder.SearchResultFlags.NotFound) || searchResponse.RecordList == null)
            {
                // レコード不存在
                q.ReturnCode = ReturnCode.NxDomain;
                return q;
            }

            List<DnsRecordBase> answersList = new List<DnsRecordBase>(searchResponse.RecordList.Count + (searchResponse.GlueRecordListForNs?.Count ?? 0));

            foreach (var ans in searchResponse.RecordList._Shuffle())
            {
                var a = ans.ToDnsLibRecordBase(question, this.DnsResponder.BootDateTime, searchResponse.Zone?.Version ?? 0);
                if (a != null)
                {
                    answersList.Add(a);
                }
            }

            if (searchResponse.GlueRecordListForNs != null)
            {
                if (answersList.Where(x => x.RecordType == RecordType.Ns).Any()) // Glue レコードは NS 応答が 1 つ以上存在する場合のみ挿入する
                {
                    foreach (var ans in searchResponse.GlueRecordListForNs._Shuffle())
                    {
                        var a = ans.ToDnsLibRecordBase(DomainName.Parse(Str.CombineFqdn(ans.Name, ans.ParentZone.DomainFqdn)), this.DnsResponder.BootDateTime, searchResponse.Zone?.Version ?? 0);
                        if (a != null)
                        {
                            answersList.Add(a);
                        }
                    }
                }
            }

            if (searchResponse.ResultFlags.Bit(EasyDnsResponder.SearchResultFlags.SubDomainIsDelegated))
            {
                string targetSubDomainFqdn = "";
                if (searchResponse.RecordList[0].Name._IsFilled())
                {
                    targetSubDomainFqdn = searchResponse.RecordList[0].Name + ".";
                }
                targetSubDomainFqdn += searchResponse.RecordList[0].ParentZone.DomainFqdn;

                var targetSubDomainFqdnParsed = DomainName.Parse(targetSubDomainFqdn);

                // 他サブドメインへの委譲
                foreach (var ans in answersList)
                {
                    ans.Name = targetSubDomainFqdnParsed;
                }

                // Glue レコードの追記 (明示的な指定)
                List<EasyDnsResponder.Record> glueRecordsList = new List<EasyDnsResponder.Record>();

                foreach (var rec in searchResponse.RecordList)
                {
                    if (rec is EasyDnsResponder.Record_NS ns)
                    {
                        foreach (var glue in ns.GlueRecordList)
                        {
                            glueRecordsList.Add(glue);
                        }
                    }
                }

                // Glue レコードの追記 (暗黙的な指定、A または AAAA が存在すれば Glue レコードとみなす)
                foreach (var ans in answersList)
                {
                    if (ans is NsRecord ns)
                    {
                        string nsFqdn = ns.NameServer.ToNormalizedFqdnFast();
                        var found2 = this.DnsResponder.Search(nsFqdn);
                        if (found2 != null && found2.RecordList != null)
                        {
                            foreach (var record in found2.RecordList.Where(x => x.Type == EasyDnsResponderRecordType.A || x.Type == EasyDnsResponderRecordType.AAAA))
                            {
                                var rootVirtualZone = new EasyDnsResponder.Zone(isVirtualRootZone: EnsureSpecial.Yes, record.ParentZone);

                                switch (record)
                                {
                                    case EasyDnsResponder.Record_A a:
                                        if (a.IsSubnet == false)
                                        {
                                            glueRecordsList.Add(new EasyDnsResponder.Record_A(rootVirtualZone, a.Settings, nsFqdn, a.IPv4Address));
                                        }
                                        break;

                                    case EasyDnsResponder.Record_AAAA aaaa:
                                        if (aaaa.IsSubnet == false)
                                        {
                                            glueRecordsList.Add(new EasyDnsResponder.Record_AAAA(rootVirtualZone, aaaa.Settings, nsFqdn, aaaa.IPv6Address));
                                        }
                                        break;
                                }
                            }
                        }
                    }
                }

                // 重複排除
                HashSet<string> glueRecordDistinctHash = new HashSet<string>();

                foreach (var glue in glueRecordsList)
                {
                    string test = glue.Name + "." + glue.ParentZone.DomainFqdn + " = " + glue.ToStringForCompare();

                    if (glueRecordDistinctHash.Contains(test) == false)
                    {
                        glueRecordDistinctHash.Add(test);

                        var r = glue.ToDnsLibRecordBase(DomainName.Parse(glue.Name + "." + glue.ParentZone.DomainFqdn), this.DnsResponder.BootDateTime, searchResponse.Zone?.Version ?? 0);

                        q.AdditionalRecords.Add(r);
                    }
                }

                // シャッフル
                if (q.AdditionalRecords.Count >= 1)
                {
                    q.AdditionalRecords = q.AdditionalRecords._Shuffle().ToList();
                }

                q.AuthorityRecords = answersList;
                q.IsAuthoritiveAnswer = false;
            }
            else
            {
                // 権威ある回答
                q.AnswerRecords = answersList;
                q.IsAuthoritiveAnswer = true;

                // 回答権威者の SOA レコード
                q.AuthorityRecords.Add(searchResponse.SOARecord.ToDnsLibRecordBase(searchResponse.ZoneDomainName, this.DnsResponder.BootDateTime, searchResponse.Zone?.Version ?? 0));
            }
        }
        catch
        {
            q.ReturnCode = ReturnCode.ServerFailure;
        }

        return q;
    }
}


#endif

