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

#if CORES_CODES_THINCONTROLLER

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
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Serialization;
using System.Net;
using System.Net.Sockets;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;


using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

using IPA.Cores.Codes;
using IPA.Cores.Helper.Codes;
using static IPA.Cores.Globals.Codes;

using Newtonsoft.Json;

namespace IPA.Cores.Codes;

public class ThinSession : IValidatable, INormalizable
{
    [SimpleTableOrder(2)]
    public string Msid = "";
    [SimpleTableOrder(1)]
    public string SessionId = "";
    [SimpleTableOrder(-100)]
    public DateTime EstablishedDateTime = Util.ZeroDateTimeValue;
    [SimpleTableOrder(3)]
    public string IpAddress = "";
    [SimpleTableOrder(4)]
    public string HostName = "";
    [SimpleTableOrder(6)]
    public int NumClients;
    [SimpleTableOrder(8)]
    public ThinServerMask64 ServerMask64 = ThinServerMask64.None;
    [SimpleTableOrder(7)]
    public int NumClientsUnique;
    [SimpleTableOrder(7.5)]
    public int NumClientsWebSocket;
    [SimpleTableOrder(20)]
    public string LocalHostname = "";
    [SimpleTableOrder(21)]
    public string LocalIp = "";
    [SimpleTableOrder(22)]
    public string LocalVersion = "";

    public void Normalize()
    {
        Msid = Msid._NonNull().ToUpperInvariant();
        SessionId = SessionId._NonNull();
        IpAddress = IpAddress._NonNull();
        HostName = HostName._NonNull();
        LocalHostname = LocalHostname._NonNull();
        LocalIp = LocalIp._NonNull();
        LocalVersion = LocalVersion._NonNull();
    }

    public void Validate()
    {
        if (Msid._IsNullOrZeroLen()) throw new CoresLibException("Msid._IsNullOrZeroLen()");
        if (SessionId.Length != 40) throw new CoresLibException("SessionId.Length != 40");
    }
}

public enum ThinGateCaps : ulong
{
    None = 0,
    WebSocket = 1,
}

public class ThinGate : IValidatable, INormalizable
{
    [SimpleTableOrder(1)]
    public string GateId = "";
    [SimpleTableOrder(6)]
    public string IpAddress = "";
    [SimpleTableOrder(7.5)]
    public int Port;
    [SimpleTableOrder(7)]
    public string HostName = "";
    [SimpleTableOrder(11)]
    public int Performance;
    [SimpleTableOrder(12)]
    public int NumSessions;
    [SimpleTableOrder(8)]
    public int Build;
    [SimpleTableOrder(9)]
    public string MacAddress = "";
    [SimpleTableOrder(10)]
    public string OsInfo = "";
    [SimpleTableOrder(2)]
    public DateTime EstablishedDateTime = Util.ZeroDateTimeValue;
    [SimpleTableOrder(3)]
    public DateTime LastCommDateTime = Util.ZeroDateTimeValue;
    [SimpleTableOrder(4)]
    public DateTime Expires = Util.ZeroDateTimeValue;
    [SimpleTableOrder(5)]
    public long NumComm;
    [SimpleTableOrder(20)]
    public string ThinLibCommitId = "";
    [SimpleTableOrder(19)]
    public DateTime CurrentTime = Util.ZeroDateTimeValue;
    [SimpleTableOrder(19.5)]
    public DateTime NextRebootTime = Util.ZeroDateTimeValue;
    [SimpleTableOrder(18)]
    public TimeSpan BootTick;
    [SimpleTableOrder(18.5)]
    public DateTime BootTime => (BootTick.Ticks != 0 ? LastCommDateTime - BootTick : ZeroDateTimeValue);

    [SimpleTableOrder(17.9)]
    public string StatusMessage = "";

    [SimpleTableOrder(12.1)]
    public int NumClients => this.SessionTable.Values.Sum(x => x.NumClients);
    [SimpleTableOrder(12.2)]
    public int NumClientsUnique => this.SessionTable.Values.Sum(x => x.NumClientsUnique);
    [SimpleTableOrder(12.3)]
    public int NumClientsWebSocket => this.SessionTable.Values.Sum(x => x.NumClientsWebSocket);

    [SimpleTableOrder(12.5)]
    public ThinGateCaps Caps = ThinGateCaps.None;

    [NoDebugDump]
    [SimpleTableIgnore]
    [JsonIgnore]
    public ImmutableDictionary<string, ThinSession> SessionTable = ImmutableDictionary<string, ThinSession>.Empty;

    public void Normalize()
    {
        GateId = GateId._NonNull().ToUpperInvariant();
        IpAddress = IpAddress._NonNull();
        if (Port == 0) Port = Consts.Ports.Https;
        HostName = HostName._NonNull();
        MacAddress = MacAddress._NonNull();
        OsInfo = OsInfo._NonNull();
        ThinLibCommitId = ThinLibCommitId._NonNull();
        StatusMessage = StatusMessage._NonNull();
    }

    public void Validate()
    {
        if (GateId.Length != 40) throw new CoresLibException("GateId.Length != 40");
        IpAddress._NotEmptyCheck(nameof(IpAddress));
    }

    public double CalcLoad()
    {
        return (double)NumSessions * (double)100.0f / (double)Performance;
    }
}

public class ThinSessionManager
{
    public ImmutableDictionary<string, ThinGate> GateTable = ImmutableDictionary<string, ThinGate>.Empty;

    public Pair2<ThinGate, ThinSession>? SearchServerSessionByMsid(string msid)
    {
        var table = this.GateTable;

        // 全 Gate の Session を検索
        List<Pair2<ThinGate, ThinSession>> candidates = new List<Pair2<ThinGate, ThinSession>>();

        foreach (var gate in table.Values)
        {
            var sess = gate.SessionTable._GetOrDefault(msid);

            // Gate あたり MSID の検索結果セッションは必ず 1 つになるはずである
            if (sess != null)
            {
                candidates.Add(new Pair2<ThinGate, ThinSession>(gate, sess));
            }
        }

        // 万一複数の Gate で同じ MSID のセッションが複数発見された場合は、最後に接続されたものを選択する
        var candidates2 = candidates.OrderByDescending(x => x.B.EstablishedDateTime);

        return candidates2.FirstOrDefault();
    }

    public ThinGate? SelectBestGateForServer(EasyIpAcl preferAcl, int gateMaxSessions, bool allowCandidate2, string preferGate)
    {
        DateTime now = DtNow;
        var table = this.GateTable;

        // 現在アクティブな Gate で有効期限が切れていないもののリスト
        var candidates2 = table.Values.Where(x => now <= x.Expires);

        if (preferGate._IsFilled())
        {
            // 特定の Gate を特に希望
            var gg = candidates2.Where(x => x.IpAddress._IsSamei(preferGate) && x.NumSessions < (gateMaxSessions * 2)).FirstOrDefault();
            if (gg != null)
            {
                return gg;
            }
        }

        // これらの Gate の中でセッション数が MaxSessionsPerGate 以下のもののリスト
        if (gateMaxSessions != 0)
        {
            candidates2 = candidates2.Where(x => x.NumSessions < gateMaxSessions);
        }

        // これらの Gate の中で希望 IP アドレスリストの範囲内であるものを candidates1 とする
        // 希望 IP アドレスの範囲にかかわらずすべての適合 Gate を candidates2 とする
        var candidates1 = candidates2.Where(x => preferAcl.Evaluate(x.IpAddress) == EasyIpAclAction.Permit);

        List<IEnumerable<ThinGate>> candidatesList = new List<IEnumerable<ThinGate>>();
        candidatesList.Add(candidates1);

        if (allowCandidate2)
        {
            candidatesList.Add(candidates2);
        }

        // 1/2 の確率で、「force_random_mode」を有効にする。
        // 「force_random_mode」の場合は、最小セッション数は無関係にすべての適応ホストから無作為に 1 つ選択する。
        bool force_random_mode = Util.RandBool();

        foreach (var candidates in candidatesList)
        {
            IEnumerable<ThinGate> selectedGates;

            if (force_random_mode == false)
            {
                // 候補を load をもとにソートする
                List<Pair2<ThinGate, double>> sortList = new List<Pair2<ThinGate, double>>();

                foreach (var gate in candidates)
                {
                    double load = gate.CalcLoad();

                    sortList.Add(new Pair2<ThinGate, double>(gate, load));
                }

                // 最も低い load の値を取得
                double minLoad = sortList.OrderBy(x => x.B).Select(x => x.B).FirstOrDefault();
                var minLoadCandidates = sortList.Where(x => x.B == minLoad);
                selectedGates = minLoadCandidates.Select(x => x.A);
            }
            else
            {
                selectedGates = candidates;
            }

            var selectedGate = selectedGates._Shuffle().FirstOrDefault();
            if (selectedGate != null)
            {
                // 1 つ選定完了!
                return selectedGate;
            }
        }

        // 全部失敗
        return null;
    }

    public int UpdateNextRebootTime(EasyIpAcl acl, DateTime nextRebootTime)
    {
        int count = 0;

        var gateIdList = this.GateTable.Where(x => acl.Evaluate(x.Value.IpAddress) == EasyIpAclAction.Permit).Select(x => x.Key).ToArray();

        ImmutableInterlocked.Update(ref this.GateTable, table =>
        {
            foreach (var id in gateIdList)
            {
                if (table.TryGetValue(id, out ThinGate? gate))
                {
                    gate.NextRebootTime = nextRebootTime;
                    count++;
                }
            }
            return table;
        });

        return count;
    }

    public bool TryUpdateGateAndDeleteSession(DateTime now, DateTime expires, ThinGate gate, string sessionId, out ThinSession? session)
    {
        session = null;
        bool sessionIsDeleted = false;

        gate.Normalize();
        gate.Validate();

        if (sessionId.Length != 40) throw new CoresLibException("sessionId.Length != 40");
        sessionId = sessionId.ToUpperInvariant();

        if (this.GateTable.TryGetValue(gate.GateId, out ThinGate? currentGate))
        {
            // Gate が存在するかどうかチェック。存在しない場合は、単発セッション削除指令は無視する
            Interlocked.Increment(ref currentGate.NumComm);

            if (currentGate.LastCommDateTime < now) currentGate.LastCommDateTime = now;
            if (currentGate.Expires < expires) currentGate.Expires = expires;
            if (currentGate.BootTick < gate.BootTick) currentGate.BootTick = gate.BootTick;

            // セッション ID でセッションを検索する
            var currentSessionTable = currentGate.SessionTable;

            foreach (var item in currentSessionTable)
            {
                var sess = item.Value;
                if (sess.SessionId == sessionId)
                {
                    if (ImmutableInterlocked.TryRemove(ref currentGate.SessionTable, item.Key, out session))
                    {
                        sessionIsDeleted = true;
                    }
                }
            }

            if (sessionIsDeleted)
            {
                // セッションが減ったことを記録
                currentGate.NumSessions = Math.Max(currentGate.NumSessions - 1, 0);
            }
        }

        // 古い Gate を削除
        DeleteOldGate(now, gate);

        return sessionIsDeleted;
    }

    public void UpdateGateAndAddSession(DateTime now, DateTime expires, ThinGate gate, ThinSession session)
    {
        gate.Normalize();
        gate.Validate();
        session.Normalize();
        session.Validate();

        if (this.GateTable.TryGetValue(gate.GateId, out ThinGate? currentGate))
        {
            // Gate が存在するかどうかチェック。存在しない場合は、単発セッション追加指令は無視する
            Interlocked.Increment(ref currentGate.NumComm);

            if (currentGate.LastCommDateTime < now) currentGate.LastCommDateTime = now;
            if (currentGate.Expires < expires) currentGate.Expires = expires;
            if (currentGate.BootTick < gate.BootTick) currentGate.BootTick = gate.BootTick;

            ImmutableInterlocked.AddOrUpdate(ref currentGate.SessionTable, session.Msid,
                addValueFactory: msid =>
                {
                    currentGate.NumSessions++;
                    return session;
                },
                updateValueFactory: (msid, existSession) =>
                {
                        // 同じ MSID のセッションがすでに存在する場合、AddSession で追加されようとしているセッション
                        // のほうが新しい場合は置き換える。そうでない場合は何もしない。
                        if (session.EstablishedDateTime >= existSession.EstablishedDateTime)
                    {
                        return session;
                    }
                    else
                    {
                        return existSession;
                    }
                });
        }

        // 古い Gate を削除
        DeleteOldGate(now, gate);
    }

    public void UpdateGateAndReportSessions(DateTime now, DateTime expires, ThinGate gate, IEnumerable<ThinSession> sessionList)
    {
        gate.Normalize();
        gate.Validate();
        sessionList._DoForEach(x => { x.Normalize(); x.Validate(); });

        Dictionary<string, ThinSession> newSessionDictionary = new Dictionary<string, ThinSession>();

        foreach (ThinSession sess in sessionList)
        {
            if (newSessionDictionary.TryGetValue(sess.Msid, out ThinSession? current) == false ||
                sess.EstablishedDateTime >= current.EstablishedDateTime)
            {
                // MSID が重複している場合は、EstablishedDateTime が最も最近のもの 1 つだけを入れる
                newSessionDictionary[sess.Msid] = sess;
            }
        }

        gate.SessionTable = gate.SessionTable.AddRange(newSessionDictionary);
        gate.NumSessions = gate.SessionTable.Count;
        gate.LastCommDateTime = now;
        gate.Expires = expires;

        ImmutableInterlocked.AddOrUpdate(ref this.GateTable, gate.GateId,
            addValueFactory: gateId =>
            {
                gate.NumComm = 1;
                gate.EstablishedDateTime = now;
                gate.NextRebootTime = ZeroDateTimeValue;
                return gate;
            },
            updateValueFactory: (gateId, current) =>
            {
                gate.NumComm = current.NumComm + 1;
                gate.EstablishedDateTime = current.EstablishedDateTime;
                gate.NextRebootTime = current.NextRebootTime;
                return gate;
            });

        // 古い Gate を削除
        DeleteOldGate(now, gate);
    }

    // 古い Gate を削除
    public void DeleteOldGate(DateTime now, ThinGate? latestGate = null)
    {
        var currentTable = this.GateTable;

        // 有効期限切れの Gate を削除する
        foreach (var item in currentTable)
        {
            if (now > item.Value.Expires)
            {
                ImmutableInterlocked.TryRemove(ref this.GateTable, item.Key, out _);
            }
        }

        // 同一の IP Address およびポート番号を持った古い Gate が存在している場合はこれを削除する
        if (latestGate != null)
        {
            foreach (var item in currentTable)
            {
                if (latestGate.IpAddress._IsSamei(item.Value.IpAddress) && latestGate.Port == item.Value.Port && item.Key != latestGate.GateId)
                {
                    ImmutableInterlocked.TryRemove(ref this.GateTable, item.Key, out _);
                }
            }
        }
    }
}

#endif

