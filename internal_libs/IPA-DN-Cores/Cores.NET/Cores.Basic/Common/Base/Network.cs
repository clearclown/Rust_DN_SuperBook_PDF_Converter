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
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics.CodeAnalysis;

using IPA.Cores.Basic;
using IPA.Cores.Basic.Legacy;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using System.Linq;
using System.Text;
using System.Runtime.CompilerServices;

#pragma warning disable CA1416 // プラットフォームの互換性の検証

namespace IPA.Cores.Basic
{
    namespace Legacy
    {
        // ソケットの種類
        public enum SockType
        {
            Unknown = 0,
            Tcp = 1,
            Udp = 2,
        }

        // ソケットイベント
        public sealed class SockEvent : IDisposable
        {
            Event Win32Event = null!;

            internal List<Sock> UnixSockList = null!;
            IntPtr UnixPipeRead, UnixPipeWrite;
            int UnixCurrentPipeData;

            bool IsReleased = false;
            readonly CriticalSection ReleaseLock = new CriticalSection<SockEvent>();

            public SockEvent()
            {
                if (Env.IsWindows)
                {
                    Win32Event = new Event();
                }
                else
                {
                    UnixSockList = new List<Sock>();
                    UnixApi.NewPipe(out this.UnixPipeRead, out this.UnixPipeWrite);
                }
            }

            public void Dispose()
            {
                Release();
            }

            void Release()
            {
                lock (ReleaseLock)
                {
                    if (IsReleased == false)
                    {
                        IsReleased = true;
                        if (Env.IsUnix)
                        {
                            UnixApi.Close(this.UnixPipeRead);
                            UnixApi.Close(this.UnixPipeWrite);
                        }
                    }
                }
            }

            // ソケットをソケットイベントに関連付けして非同期に設定する
            public void JoinSock(Sock sock)
            {
                if (sock.asyncMode)
                {
                    return;
                }

                if (Env.IsWindows)
                {
                    // Windows
                    try
                    {
                        if (sock.ListenMode != false || (sock.Type == SockType.Tcp && sock.Connected == false))
                        {
                            return;
                        }

                        Sock.WSAEventSelect(sock.Socket!.Handle, Win32Event.Handle, 35);
                        sock.Socket.Blocking = false;

                        sock.SockEvent = this;

                        sock.asyncMode = true;
                    }
                    catch
                    {
                    }
                }
                else
                {
                    // UNIX
                    if (sock.ListenMode != false || (sock.Type == SockType.Tcp && sock.Connected == false))
                    {
                        return;
                    }

                    sock.asyncMode = true;

                    lock (UnixSockList)
                    {
                        UnixSockList.Add(sock);
                    }

                    sock.Socket!.Blocking = false;

                    sock.SockEvent = this;

                    this.Set();
                }
            }

            // イベントを叩く
            public void Set()
            {
                if (Env.IsWindows)
                {
                    this.Win32Event.Set();
                }
                else
                {
                    if (this.UnixCurrentPipeData <= 100)
                    {
                        UnixApi.Write(this.UnixPipeWrite, new byte[] { 0 }, 0, 1);
                        this.UnixCurrentPipeData++;
                    }
                }
            }

            // イベントを待つ
            public bool Wait(int timeout)
            {
                if (Env.IsWindows)
                {
                    if (timeout == 0)
                    {
                        return false;
                    }

                    return this.Win32Event.Wait(timeout);
                }
                else
                {
                    List<IntPtr> reads = new List<IntPtr>();
                    List<IntPtr> writes = new List<IntPtr>();

                    lock (this.UnixSockList)
                    {
                        foreach (Sock s in this.UnixSockList)
                        {
                            reads.Add(s.Fd);
                            if (s.writeBlocked)
                            {
                                writes.Add(s.Fd);
                            }
                        }
                    }

                    reads.Add(this.UnixPipeRead);

                    if (this.UnixCurrentPipeData == 0)
                    {
                        UnixApi.Poll(reads.ToArray(), writes.ToArray(), timeout);
                    }

                    int readret;
                    byte[] tmp = new byte[1024];
                    this.UnixCurrentPipeData = 0;
                    do
                    {
                        readret = UnixApi.Read(this.UnixPipeRead, tmp, 0, tmp.Length);
                    }
                    while (readret >= 1);

                    return true;
                }
            }
        }

        // ソケットセット
        public class SockSet
        {
            List<Sock> List = null!;

            public const int MaxSocketNum = 60;

            public SockSet()
            {
                Clear();
            }

            public void Add(Sock sock)
            {
                if (sock.Type == SockType.Tcp && sock.Connected == false)
                {
                    return;
                }

                if (List.Count >= MaxSocketNum)
                {
                    return;
                }

                List.Add(sock);
            }

            public void Clear()
            {
                List = new List<Sock>();
            }

            public void Poll(int timeout = Timeout.Infinite, Event? e1 = null)
            {
                Poll(timeout, e1, null);
            }
            public void Poll(Event? e1, Event? e2)
            {
                Poll(Timeout.Infinite, e1, e2);
            }
            public void Poll(int timeout, Event? e1, Event? e2)
            {
                try
                {
                    List<Event> array = new List<Event>();

                    // イベント配列の設定
                    foreach (Sock s in List)
                    {
                        s.initAsyncSocket();
                        if (s.hEvent != null)
                        {
                            array.Add(s.hEvent);
                        }
                    }

                    if (e1 != null)
                    {
                        array.Add(e1);
                    }

                    if (e2 != null)
                    {
                        array.Add(e2);
                    }

                    if (array.Count == 0)
                    {
                        ThreadObj.Sleep(timeout);
                    }
                    else
                    {
                        Event.WaitAny(array.ToArray(), timeout);
                    }
                }
                catch
                {
                }
            }
        }

        // Ping 送信
        public static class SendPing
        {
            public static async Task<SendPingReply> SendAsync(IPAddress target, byte[]? data = null, int timeout = Consts.Timeouts.DefaultSendPingTimeout)
            {
                try
                {
                    if (data == null)
                    {
                        data = Util.Rand(Consts.Numbers.DefaultSendPingSize);
                    }
                    if (timeout == 0)
                    {
                        timeout = Consts.Timeouts.DefaultSendPingTimeout;
                    }

                    using (Ping p = new Ping())
                    {
                        DateTime startDateTime = Time.NowHighResDateTimeUtc;

                        PingReply ret = await p.SendPingAsync(target, timeout, data);

                        DateTime endDateTime = Time.NowHighResDateTimeUtc;

                        TimeSpan span = endDateTime - startDateTime;

                        SendPingReply r = new SendPingReply(ret.Status, span, null, ret.Options?.Ttl ?? 0);

                        return r;
                    }
                }
                catch (Exception ex)
                {
                    return new SendPingReply(IPStatus.Unknown, default, ex, 0);
                }
            }
            public static SendPingReply Send(IPAddress target, byte[]? data = null, int timeout = Consts.Timeouts.DefaultSendPingTimeout)
                => SendAsync(target, data, timeout)._GetResult();
        }

    }


    public class IpEndPointComparer : IEqualityComparer<IPEndPoint?>, IComparer<IPEndPoint?>
    {
        public bool IgnoreScopeId { get; }
        IpComparer IpCmp { get; }

        public IpEndPointComparer(bool ignoreSopeId)
        {
            this.IgnoreScopeId = ignoreSopeId;

            if (this.IgnoreScopeId == false)
            {
                this.IpCmp = IpComparer.Comparer;
            }
            else
            {
                this.IpCmp = IpComparer.ComparerWithIgnoreScopeId;
            }
        }

        public static IpEndPointComparer Comparer { get; } = new IpEndPointComparer(false);
        public static IpEndPointComparer ComparerIgnoreScopeId { get; } = new IpEndPointComparer(true);

        public int Compare(IPEndPoint? x, IPEndPoint? y)
        {
            if (x == null && y != null) return 1;
            if (x != null && y == null) return -1;
            if (x == null && y == null) return 0;

            x._MarkNotNull();
            y._MarkNotNull();

            int r = this.IpCmp.Compare(x.Address, y.Address);
            if (r != 0) return r;

            r = x.Port.CompareTo(y.Port);
            return r;
        }

        public bool Equals(IPEndPoint? x, IPEndPoint? y)
            => (Compare(x, y) == 0);

        public int GetHashCode(IPEndPoint? obj)
        {
            if (obj == null) return 0;

            if (this.IgnoreScopeId == false)
            {
                return obj.GetHashCode();
            }
            else
            {
                return obj._RemoveScopeId().GetHashCode();
            }
        }
    }

    public class IpComparer : IEqualityComparer<IPAddress?>, IComparer<IPAddress?>
    {
        public bool IgnoreScopeId { get; } = false;

        public IpComparer(bool ignoreScopeId = false)
        {
            this.IgnoreScopeId = ignoreScopeId;
        }


        public static IpComparer Comparer { get; } = new IpComparer(false);
        public static IpComparer ComparerWithIgnoreScopeId { get; } = new IpComparer(true);

        public int Compare(IPAddress? x, IPAddress? y)
        {
            if (x == null && y != null) return 1;
            if (x != null && y == null) return -1;
            if (x == null && y == null) return 0;

            x._MarkNotNull();
            y._MarkNotNull();

            int r = x.AddressFamily.CompareTo(y.AddressFamily);
            if (r != 0) return r;

            r = Util.MemCompare(x.GetAddressBytes(), y.GetAddressBytes());
            if (r != 0) return r;

            if (x.AddressFamily != System.Net.Sockets.AddressFamily.InterNetworkV6) return 0;

            if (IgnoreScopeId == false)
            {
                return x.ScopeId.CompareTo(y.ScopeId);
            }
            else
            {
                return 0;
            }
        }

        [MethodImpl(Inline)]
        public bool Equals(IPAddress? x, IPAddress? y)
            => (Compare(x, y) == 0);

        [MethodImpl(Inline)]
        public int GetHashCode(IPAddress? obj)
        {
            if (obj == null) return 0;

            if (IgnoreScopeId == false)
            {
                return obj.GetHashCode();
            }
            else
            {
                return obj._RemoveScopeId().GetHashCode();
            }
        }
    }

    public static partial class CoresConfig
    {
        public static partial class EasyIpAclConfig
        {
            public static readonly Copenhagen<int> CachedAclObjectsExpires = 10 * 1000; // ACL オブジェクトキャッシュ有効期限
            public static readonly Copenhagen<int> CachedAclObjectResultsExpires = 60 * 1000; // ACL オブジェクトあたりの結果キャッシュ有効期限
            public static readonly Copenhagen<int> CachedFqdnResolvedAclObjectExpires = 30 * 1000; // FQDN 名前解決の結果キャッシュ有効期限
        }
    }

    // 簡易 IP ACL
    // 
    // 表記方法
    // 1. 改行または ; または , または | または空白文字でルール間を区切る。
    // 2. 1.2.3.0/24 のように表記すると、この範囲が permit となる。
    // 3. !1.2.3.4/24 のように先頭に ! を付けると、この範囲が deny となる。
    // 4. 上から順に評価される。
    // 
    // 表記例
    // 192.168.0.0/16,172.16.0.0/12,10.0.0.0/8
    public class EasyIpAcl
    {
        public EasyIpAclAction DefaultAction { get; }
        public EasyIpAclAction DefaultActionForEmpty { get; }

        public IEnumerable<EasyIpAclRule> RuleList => RuleListInternal;

        List<EasyIpAclRule> RuleListInternal = new List<EasyIpAclRule>();

        readonly FastCache<IPAddress, EasyIpAclAction>? CacheByIp = null;
        readonly FastCache<string, EasyIpAclAction>? CacheByStr = null;

        public bool IsEmpty => this.RuleListInternal.Count == 0;

        static readonly FastCache<string, EasyIpAcl> GlobalAclObjectCache = new FastCache<string, EasyIpAcl>(CoresConfig.EasyIpAclConfig.CachedAclObjectsExpires, 0, CacheType.UpdateExpiresWhenAccess);

        static readonly FastCache<string, string> GlobalAclResolvedFqdnCache = new FastCache<string, string>(CoresConfig.EasyIpAclConfig.CachedFqdnResolvedAclObjectExpires, 0, CacheType.DoNotUpdateExpiresWhenAccess);

        public EasyIpAcl(string? body, EasyIpAclAction defaultAction = EasyIpAclAction.Deny, EasyIpAclAction defaultActionForEmpty = EasyIpAclAction.Permit, bool enableCache = false)
        {
            List<string> rulesStrList = new List<string>();

            if (enableCache)
            {
                this.CacheByIp = new FastCache<IPAddress, EasyIpAclAction>(CoresConfig.EasyIpAclConfig.CachedAclObjectResultsExpires, 0, CacheType.UpdateExpiresWhenAccess, IpComparer.Comparer);
                this.CacheByStr = new FastCache<string, EasyIpAclAction>(CoresConfig.EasyIpAclConfig.CachedAclObjectResultsExpires, 0, CacheType.UpdateExpiresWhenAccess);
            }

            // ルールの列挙
            string[] lines = body._NonNull()._GetLines(true, true, Consts.Strings.CommentStartStringForEasyIpAcl);
            foreach (string line in lines)
            {
                string[] tokens = line._Split(StringSplitOptions.RemoveEmptyEntries, ';', '|', ',', ' ', '　', '\t');

                tokens.Where(x => x._IsFilled())._DoForEach(x => rulesStrList.Add(x));
            }

            foreach (string str in rulesStrList)
            {
                if (EasyIpAclRule.TryParse(str, out EasyIpAclRule? rule))
                {
                    RuleListInternal.Add(rule);
                }
            }

            this.DefaultAction = defaultAction;
            this.DefaultActionForEmpty = defaultActionForEmpty;
        }

        public static string NormalizeRules(string? rules, bool multiLines = false, bool allowAllIfEmptyOrError = false)
        {
            rules = rules._NonNullTrim();

            try
            {
                EasyIpAcl a = new EasyIpAcl(rules);

                if (a.IsEmpty && allowAllIfEmptyOrError)
                {
                    return Consts.Strings.EasyAclAllowAllRule;
                }

                return a.ToString(multiLines);
            }
            catch
            {
                if (allowAllIfEmptyOrError == false)
                {
                    return "";
                }
                else
                {
                    return Consts.Strings.EasyAclAllowAllRule;
                }
            }
        }

        public static async Task<EasyIpAclAction> EvaluateWithFqdnIncludedAsync(string? rulesFqdnAllow, string ipStr, EasyIpAclAction defaultAction = EasyIpAclAction.Deny, EasyIpAclAction defaultActionForEmpty = EasyIpAclAction.Permit, bool enableEvaluateCache = false, bool permitLocalHost = false, bool enableResolveCache=true, AddressFamily? resolveAddressFamily = null, int resolveTimeout = -1, int numParallelTasks = 8, CancellationToken cancel = default, Func<IPAddress, long>? orderBy = null, bool allowAllIfEmptyOrError = false)
        {
            string normalizedRule;

            if (enableResolveCache)
            {
                normalizedRule = await ResolveFqdnIncludedAclRuleWithCacheAsync(rulesFqdnAllow, resolveAddressFamily, resolveTimeout, numParallelTasks, cancel, orderBy, false, allowAllIfEmptyOrError);
            }
            else
            {
                normalizedRule = await ResolveFqdnIncludedAclRuleAsync(rulesFqdnAllow, resolveAddressFamily, resolveTimeout, numParallelTasks, cancel, orderBy, false, allowAllIfEmptyOrError);
            }

            return EasyIpAcl.Evaluate(normalizedRule, ipStr, defaultAction, defaultActionForEmpty, enableEvaluateCache, permitLocalHost);
        }

        public static async Task<EasyIpAclAction> EvaluateWithFqdnIncludedAsync(string? rulesFqdnAllow, IPAddress ip, EasyIpAclAction defaultAction = EasyIpAclAction.Deny, EasyIpAclAction defaultActionForEmpty = EasyIpAclAction.Permit, bool enableEvaluateCache = false, bool permitLocalHost = false, bool enableResolveCache = true, AddressFamily? resolveAddressFamily = null, int resolveTimeout = -1, int numParallelTasks = 8, CancellationToken cancel = default, Func<IPAddress, long>? orderBy = null, bool allowAllIfEmptyOrError = false)
        {
            string normalizedRule;

            if (enableResolveCache)
            {
                normalizedRule = await ResolveFqdnIncludedAclRuleWithCacheAsync(rulesFqdnAllow, resolveAddressFamily, resolveTimeout, numParallelTasks, cancel, orderBy, false, allowAllIfEmptyOrError);
            }
            else
            {
                normalizedRule = await ResolveFqdnIncludedAclRuleAsync(rulesFqdnAllow, resolveAddressFamily, resolveTimeout, numParallelTasks, cancel, orderBy, false, allowAllIfEmptyOrError);
            }

            return EasyIpAcl.Evaluate(normalizedRule, ip, defaultAction, defaultActionForEmpty, enableEvaluateCache, permitLocalHost);
        }

        public static async Task<string> ResolveFqdnIncludedAclRuleWithCacheAsync(string? rulesFqdnAllow, AddressFamily? addressFamily = null, int timeout = -1, int numParallelTasks = 8, CancellationToken cancel = default, Func<IPAddress, long>? orderBy = null, bool multiLines = false, bool allowAllIfEmptyOrError = false)
        {
            string cacheKey = $"{rulesFqdnAllow._NonNull()}@{addressFamily}@{timeout}@{multiLines}@{allowAllIfEmptyOrError}";

            string? ret = await GlobalAclResolvedFqdnCache.GetOrCreateAsync(cacheKey, async _ =>
            {
                return await ResolveFqdnIncludedAclRuleAsync(rulesFqdnAllow, addressFamily, timeout, numParallelTasks, cancel, orderBy, multiLines, allowAllIfEmptyOrError);
            });

            return ret._NonNull();
        }

        public static async Task<string> ResolveFqdnIncludedAclRuleAsync(string? rulesFqdnAllow, AddressFamily? addressFamily = null, int timeout = -1, int numParallelTasks = 8, CancellationToken cancel = default, Func<IPAddress, long>? orderBy = null, bool multiLines = false, bool allowAllIfEmptyOrError = false)
        {
            rulesFqdnAllow = rulesFqdnAllow._NonNullTrim();

            List<string> rulesStrList = new List<string>();

            // ルールの列挙
            string[] lines = rulesFqdnAllow._NonNull()._GetLines(true, true, Consts.Strings.CommentStartStringForEasyIpAcl);
            foreach (string line in lines)
            {
                string[] tokens = line._Split(StringSplitOptions.RemoveEmptyEntries, ';', '|', ',', ' ', '　', '\t');

                tokens.Where(x => x._IsFilled())._DoForEach(x => rulesStrList.Add(x));
            }

            List<(bool negative, string ipOrFqdn, string subnet, List<IPAddress> result)> jobs = new();

            foreach (string str in rulesStrList)
            {
                string str2 = str;
                bool negative = false;

                if (str.StartsWith("-", StringComparison.Ordinal) || str.StartsWith("!", StringComparison.Ordinal))
                {
                    negative = true;
                    str2 = str.Substring(1);
                }

                string[] tokens = str2.Split('/');
                string ipOrFqdn = "";
                string subnet = "";
                if (tokens.Length >= 2)
                {
                    ipOrFqdn = tokens[0];
                    subnet = tokens[1];
                }
                else if (tokens.Length >= 1)
                {
                    ipOrFqdn = tokens[0];
                }

                if (ipOrFqdn._IsFilled())
                {
                    var ipList = new List<IPAddress>();

                    if (IPAddress.TryParse(ipOrFqdn, out IPAddress? ip))
                    {
                        ipList.Add(ip);
                    }

                    jobs.Add((negative, ipOrFqdn, subnet, ipList));
                }
            }

            Exception? firstError = null;

            await jobs._DoForEachParallelAsync(async (targetElement, taskIndex) =>
            {
                try
                {
                    if (targetElement.result.Any() == false)
                    {
                        var ipList = await LocalNet.GetIpMultipleAsync(targetElement.ipOrFqdn, addressFamily, timeout, cancel, orderBy);
                        targetElement.result.AddRange(ipList);
                    }
                }
                catch (Exception ex)
                {
                    // 例外があった場合は例外は握りつぶし、エラーを表示する
                    firstError ??= ex;
                    ex._Error();
                }
            },
            numParallelTasks,
            MultitaskDivideOperation.RoundRobin,
            cancel);

            List<string> tmp = new List<string>();

            foreach (var job in jobs)
            {
                if (job.result.Any())
                {
                    string negativeStr = job.negative ? "!" : "";
                    foreach (var ip in job.result)
                    {
                        if (job.subnet._IsFilled())
                        {
                            tmp.Add($"{negativeStr}{ip.ToString()}/{job.subnet}");
                        }
                        else
                        {
                            tmp.Add($"{negativeStr}{ip.ToString()}");
                        }
                    }
                }
            }

            return EasyIpAcl.NormalizeRules(tmp._Combine(" "), multiLines, allowAllIfEmptyOrError);
        }

        public static EasyIpAclAction Evaluate(string? rules, string ipStr, EasyIpAclAction defaultAction = EasyIpAclAction.Deny, EasyIpAclAction defaultActionForEmpty = EasyIpAclAction.Permit, bool enableCache = false, bool permitLocalHost = false)
        {
            if (rules._IsEmpty()) return defaultActionForEmpty;

            if (permitLocalHost)
            {
                var ip = ipStr._ToIPAddress()!;
                var type = ip._GetIPAddressType();
                if (type.Bit(IPAddressType.Loopback))
                {
                    return EasyIpAclAction.Permit;
                }
            }

            if (enableCache == false)
            {
                return EvaluateCore(rules, ipStr, defaultAction, defaultActionForEmpty);
            }
            else
            {
                return GetOrCreateCachedIpAcl(rules, defaultAction, default).EvaluateCore(ipStr);
            }
        }

        public static EasyIpAclAction Evaluate(string? rules, IPAddress ip, EasyIpAclAction defaultAction = EasyIpAclAction.Deny, EasyIpAclAction defaultActionForEmpty = EasyIpAclAction.Permit, bool enableCache = false, bool permitLocalHost = false)
        {
            if (rules._IsEmpty()) return defaultActionForEmpty;

            if (permitLocalHost)
            {
                var type = ip._GetIPAddressType();
                if (type.Bit(IPAddressType.Loopback))
                {
                    return EasyIpAclAction.Permit;
                }
            }

            if (enableCache == false)
            {
                return EvaluateCore(rules, ip, defaultAction, defaultActionForEmpty);
            }
            else
            {
                return GetOrCreateCachedIpAcl(rules, defaultAction, default).EvaluateCore(ip);
            }
        }

        public static EasyIpAcl GetOrCreateCachedIpAcl(string? rules, EasyIpAclAction defaultAction = EasyIpAclAction.Deny, EasyIpAclAction defaultActionForEmpty = EasyIpAclAction.Permit)
        {
            string key = $"{rules},{(int)defaultAction},{(int)defaultActionForEmpty}";

            return GlobalAclObjectCache.GetOrCreate(key, key => new EasyIpAcl(rules, defaultAction, defaultActionForEmpty, true))!;
        }

        static EasyIpAclAction EvaluateCore(string? rules, IPAddress ip, EasyIpAclAction defaultAction = EasyIpAclAction.Deny, EasyIpAclAction defaultActionForEmpty = EasyIpAclAction.Permit)
        {
            try
            {
                EasyIpAcl acl = new EasyIpAcl(rules, defaultAction, defaultActionForEmpty);

                return acl.Evaluate(ip);
            }
            catch
            {
                return EasyIpAclAction.Deny;
            }
        }

        static EasyIpAclAction EvaluateCore(string? rules, string ipStr, EasyIpAclAction defaultAction = EasyIpAclAction.Deny, EasyIpAclAction defaultActionForEmpty = EasyIpAclAction.Permit)
        {
            try
            {
                EasyIpAcl acl = new EasyIpAcl(rules, defaultAction, defaultActionForEmpty);

                return acl.Evaluate(ipStr);
            }
            catch
            {
                return EasyIpAclAction.Deny;
            }
        }

        public EasyIpAclAction Evaluate(string ipStr)
        {
            if (RuleListInternal.Count == 0)
            {
                return this.DefaultActionForEmpty;
            }

            if (this.CacheByStr != null)
            {
                return this.CacheByStr.GetOrCreate(ipStr, ipStr =>
                {
                    return EvaluateCore(ipStr);
                });
            }
            else
            {
                return EvaluateCore(ipStr);
            }
        }

        EasyIpAclAction EvaluateCore(string ipStr)
        {
            if (RuleListInternal.Count == 0)
            {
                return this.DefaultActionForEmpty;
            }

            IPAddress? ipa = ipStr._ToIPAddress(noExceptionAndReturnNull: true);
            if (ipa == null)
            {
                return EasyIpAclAction.Deny;
            }

            return EvaluateCore(ipa);
        }

        public EasyIpAclAction Evaluate(IPAddress ip)
        {
            if (this.CacheByIp != null)
            {
                return this.CacheByIp.GetOrCreate(ip, ip =>
                {
                    return EvaluateCore(ip);
                });
            }
            else
            {
                return EvaluateCore(ip);
            }
        }

        EasyIpAclAction EvaluateCore(IPAddress ip)
        {
            try
            {
                if (RuleListInternal.Count == 0)
                {
                    return this.DefaultActionForEmpty;
                }

                foreach (var rule in RuleListInternal)
                {
                    if (rule.IsMatch(ip))
                    {
                        return rule.Action;
                    }
                }

                return this.DefaultAction;
            }
            catch
            {
                return EasyIpAclAction.Deny;
            }
        }

        public override string ToString() => ToString(false);
        public string ToString(bool multiLines)
        {
            List<string> tmp = new List<string>();

            foreach (var item in this.RuleList)
            {
                tmp.Add(item.ToString());
            }

            return tmp._Combine(multiLines ? "\r\n" : "; ");
        }
    }

    [Flags]
    public enum EasyIpAclAction
    {
        Deny,
        Permit,
    }

    public class EasyIpAclRule
    {
        public IPAddress Network { get; }
        public IPAddress Mask { get; }
        public int? SubnetLength { get; }
        public AddressFamily AddressFamily { get; }
        public EasyIpAclAction Action { get; }

        public EasyIpAclRule(string ruleStr)
        {
            ruleStr = ruleStr._NonNullTrim();

            this.Action = EasyIpAclAction.Permit;

            if (ruleStr.StartsWith("!") || ruleStr.StartsWith("-"))
            {
                ruleStr = ruleStr.Substring(1);
                this.Action = EasyIpAclAction.Deny;
            }

            IPUtil.ParseIPAndMask(ruleStr, out IPAddress network, out IPAddress subnetMask);

            network = network._RemoveScopeId();
            subnetMask = subnetMask._RemoveScopeId();

            this.AddressFamily = network.AddressFamily;
            this.Mask = subnetMask;
            this.Network = IPUtil.IPAnd(network, subnetMask);
            if (IPUtil.IsSubnetMask(subnetMask))
            {
                this.SubnetLength = IPUtil.SubnetMaskToInt(subnetMask);
            }
            else
            {
                this.SubnetLength = null;
            }
        }

        public bool IsMatch(IPAddress ip)
        {
            if (ip.AddressFamily != this.AddressFamily)
            {
                return false;
            }

            return IPUtil.IsInSameNetwork(ip, this.Network, this.Mask, true);
        }

        public static bool TryParse([NotNullWhen(true)] string? ruleStr, [NotNullWhen(true)] out EasyIpAclRule? rule)
        {
            rule = null;

            if (ruleStr._IsEmpty()) return false;

            try
            {
                EasyIpAclRule ret = new EasyIpAclRule(ruleStr);

                rule = ret;

                return true;
            }
            catch
            {
                return false;
            }
        }

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();

            if (this.Action == EasyIpAclAction.Deny)
            {
                sb.Append("-");
            }

            sb.Append(this.Network);

            sb.Append("/");

            if (this.SubnetLength.HasValue)
            {
                sb.Append(this.SubnetLength.Value);
            }
            else
            {
                sb.Append(this.Mask.ToString());
            }

            return sb.ToString();
        }
    }

    // IP アドレスの種類
    [Flags]
    public enum IPAddressType : long
    {
        IPv4 = 0,
        IPv6 = 1,
        Unicast = 2,
        GlobalUnicast = 4,
        LocalUnicast = 8,
        Multicast = 16,
        Zero = 32,
        Loopback = 64,
        IPv4_APIPA = 128,
        IPv4_IspShared = 256,
        IPv4_Broadcast = 512,
        IPv6_AllNodeMulticast = 1024,
        IPv6_AllRouterMulticast = 2048,
        IPv6_SoliciationMulticast = 4096,
        GlobalIp = 8192,
    }

    // 許容される IP アドレスの種類
    [Flags]
    public enum AllowedIPVersions
    {
        None = 0,
        IPv4 = 1,
        IPv6 = 2,
        All = IPv4 | IPv6,
    }

    // IPAddrsssType ヘルパー
    public static class IPAddressTypeHelper
    {
        public static bool _IsLocalNetwork(this IPAddressType type)
        {
            return type.BitAny(IPAddressType.Loopback | IPAddressType.LocalUnicast);
        }
    }

    // IP ユーティリティ
    public static partial class IPUtil
    {
        public static readonly IPEndPoint LocalHostIPv4HttpEndPoint = new IPEndPoint(IPAddress.Loopback, 80);

        // TCP ポートが開いているかどうかチェック
        public static async Task<bool> CheckTcpPortAsync(string hostname, int port, int connectTimeout = 500, CancellationToken cancel = default)
        {
            if (connectTimeout <= 0) connectTimeout = 500;

            try
            {
                return await TaskUtil.DoAsyncWithTimeout(async c =>
                {
                    try
                    {
                        using var tcp = new TcpClient();
                        tcp.ReceiveTimeout = connectTimeout;
                        tcp.SendTimeout = connectTimeout;
                        await tcp.ConnectAsync(hostname, port, c);
                        using var stream = tcp.GetStream();
                        return true;
                    }
                    catch
                    {
                        return false;
                    }
                },
                timeout: connectTimeout,
                cancel: cancel);
            }
            catch
            {
                return false;
            }
        }
        public static async Task<bool> CheckTcpPortWithRetryAsync(string hostname, int port, int numTry = 5, int connectTimeout = 500, CancellationToken cancel = default)
        {
            numTry = Math.Max(numTry, 1);

            for (int i = 0; i < numTry; i++)
            {
                cancel.ThrowIfCancellationRequested();

                if (i >= 1)
                {
                    await Task.Yield();
                }

                bool r = await CheckTcpPortAsync(hostname, port, connectTimeout, cancel);

                if (r) return true;
            }

            return false;
        }

        // IP アドレスからワイルドカード DNS 名を生成
        public static string GenerateWildCardDnsFqdn(IPAddress ip, string baseDomainName, string prefix = "", string suffix = "", bool allDigits = false)
        {
            prefix = prefix._NonNullTrim();
            suffix = suffix._NonNullTrim();

            baseDomainName = baseDomainName._NormalizeFqdn();
            if (baseDomainName._IsFilled())
            {
                baseDomainName = "." + baseDomainName;
            }

            if (ip.AddressFamily == AddressFamily.InterNetwork)
            {
                return prefix + ip._IPToStr(allDigits)._ReplaceStr(".", "-") + suffix + baseDomainName;
            }
            else if (ip.AddressFamily == AddressFamily.InterNetworkV6)
            {
                return prefix + ip._IPToStr(allDigits)._ReplaceStr(":", "-") + suffix + baseDomainName;
            }
            else
            {
                throw new CoresLibException("ip.AddressFamily: out of range");
            }
        }

        // ワイルドカード DNS FQDN から IP アドレスのパースを試行
        // prefix-1-2-3-4-suffix.example.org -> 1.2.3.4
        // prefix-1111-2222-3333-4444-5555-6666-7777-8888-suffix.example.org -> 1111:2222:3333:4444:5555:6666:7777:8888
        // prefix-1111-2222--7777-8888-suffix.example.org -> 1111:2222::7777:8888
        public static bool TryParseWildCardDnsFqdn(string fqdnOrLabelNormalized, [NotNullWhen(true)] out IPAddress? ip)
        {
            ip = null;

            string[] labels = fqdnOrLabelNormalized._Split(StringSplitOptions.None, ".");

            foreach (var label in labels)
            {
                if (label._IsFilled())
                {
                    if (TryParseWildCardDnsLabel(label, out IPAddress? ip2))
                    {
                        ip = ip2;
                        return true;
                    }
                }
            }

            if (labels.Length >= 4)
            {
                for (int i = 0; i < labels.Length - 3; i++)
                {
                    if (IsIPv4Token(labels[i]) || IsIPv4Token(labels[i + 1]) || IsIPv4Token(labels[i + 2]) || IsIPv4Token(labels[i + 3]))
                    {
                        string tmp = labels[i] + "-" + labels[i + 1] + "-" + labels[i + 2] + "-" + labels[i + 3];

                        if (TryParseWildCardDnsLabel(tmp, out IPAddress? ip2))
                        {
                            ip = ip2;
                            return true;
                        }
                    }
                }
            }

            return false;
        }
        public static bool TryParseWildCardDnsLabel(string labelNormalized, [NotNullWhen(true)] out IPAddress? ip)
        {
            ip = null;

            try
            {
                string? label = labelNormalized._Split(StringSplitOptions.None, ".").ElementAtOrDefault(0);
                if (label._IsEmpty()) return false;

                string[] tokens = label._Split(StringSplitOptions.None, "-");
                if (tokens.Length < 3) return false;

                // 最初のいくつかの文字列のみのトークンをスキップする

                {
                    int first = -1;
                    for (int i = 0; i < tokens.Length; i++)
                    {
                        if (IsIPv6Token(tokens[i]))
                        {
                            first = i;
                            break;
                        }
                    }
                    if (first == -1)
                    {
                        // 全部のトークンが文字列
                        return false;
                    }

                    int last = -1;
                    // 最後のいくつかの文字列のみのトークンをスキップする
                    for (int i = tokens.Length - 1; i >= first; i--)
                    {
                        if (IsIPv6Token(tokens[i]))
                        {
                            last = i;
                            break;
                        }
                    }
                    if (last == -1)
                    {
                        // 全部のトークンが文字列
                        return false;
                    }

                    int num = last - first + 1;

                    if (num >= 3)
                    {
                        // まず IPv6 としてパースを試みる
                        StringBuilder sb = new StringBuilder(39);
                        bool hasEmptyToken = false;
                        for (int i = first; i <= last; i++)
                        {
                            string token = tokens[i];
                            if (IsIPv6Token(token) == false)
                            {
                                break;
                            }
                            if (token.Length == 0)
                            {
                                hasEmptyToken = true;
                            }
                            if (i > first)
                            {
                                sb.Append(":");
                            }
                            sb.Append(token);
                        }
                        if (hasEmptyToken || num >= 8)
                        {
                            string ipstr = sb.ToString();
                            int i = ipstr.IndexOf("::");
                            if (i != -1)
                            {
                                i = ipstr.IndexOf("::", i + 1);
                                if (i != -1)
                                {
                                    ipstr = ipstr.Substring(0, i);
                                }
                            }
                            if (IPAddress.TryParse(ipstr, out IPAddress? ip2))
                            {
                                if (ip2.AddressFamily == AddressFamily.InterNetworkV6)
                                {
                                    ip = ip2;
                                    return true;
                                }
                            }
                        }
                    }
                }

                {
                    // 次に IPv4 としてパースを試みる
                    int first = -1;
                    for (int i = 0; i < tokens.Length; i++)
                    {
                        if (IsIPv4Token(tokens[i]))
                        {
                            first = i;
                            break;
                        }
                    }
                    if (first == -1)
                    {
                        // 全部のトークンが文字列
                        return false;
                    }

                    int last = -1;
                    // 最後のいくつかの文字列のみのトークンをスキップする
                    for (int i = tokens.Length - 1; i >= first; i--)
                    {
                        if (IsIPv4Token(tokens[i]))
                        {
                            last = i;
                            break;
                        }
                    }
                    if (last == -1)
                    {
                        // 全部のトークンが文字列
                        return false;
                    }

                    int num = last - first + 1;
                    if (num >= 4)
                    {
                        StringBuilder sb = new StringBuilder(15);
                        for (int i = first; i < (first + 4); i++)
                        {
                            if (IsIPv4Token(tokens[i]) == false)
                            {
                                return false;
                            }
                            if (i > first)
                            {
                                sb.Append(".");
                            }
                            sb.Append(tokens[i]);
                        }
                        string ipstr = sb.ToString();
                        if (IPAddress.TryParse(ipstr, out IPAddress? ip2) == false)
                        {
                            return false;
                        }
                        if (ip2.AddressFamily != AddressFamily.InterNetwork)
                        {
                            return false;
                        }
                        ip = ip2;
                        return true;
                    }
                }

                // IPv4 としても IPv6 としてもパースに失敗した
                return false;
            }
            catch
            {
                return false;
            }
        }

        [MethodImpl(Inline)]
        public static bool IsNumberOrHexToken(string str)
        {
            foreach (char c in str)
            {
                if (c >= '0' && c <= '9') { }
                else if (c >= 'a' && c <= 'f') { }
                else if (c >= 'A' && c <= 'F') { }
                else return false;
            }
            return true;
        }

        [MethodImpl(Inline)]
        public static bool IsIPv4OrIPv6Token(string str)
        {
            return IsIPv4Token(str) || IsIPv6Token(str);
        }

        [MethodImpl(Inline)]
        public static bool IsIPv6Token(string str)
        {
            if (IsNumberOrHexToken(str) == false) return false;
            if (str.Length >= 5) return false;
            return true;
        }

        [MethodImpl(Inline)]
        public static bool IsIPv4Token(string str)
        {
            if (IsNumberToken(str) == false) return false;
            if (int.TryParse(str, out int i) == false) return false;
            return i >= 0 && i <= 255;
        }

        [MethodImpl(Inline)]
        public static bool IsNumberToken(string str)
        {
            foreach (char c in str)
            {
                if (c >= '0' && c <= '9') { }
                else return false;
            }
            return true;
        }

        // MAC アドレスのランダム生成
        public static ReadOnlyMemory<byte> GenRandomMac(string seedStr, byte firstByte = 0xAE)
        {
            byte[] hash = Secure.HashSHA256($"random_mac_seed{seedStr}"._GetBytes());
            hash[0] = firstByte;
            return (hash.AsMemory()).Slice(0, 6);
        }

        public static IPEndPoint[] GenerateListeningEndPointsList(bool localHostOnly, params int[] ports)
        {
            List<IPEndPoint> ret = new List<IPEndPoint>();

            foreach (int port in ports)
            {
                if (localHostOnly == false)
                {
                    ret.Add(new IPEndPoint(IPAddress.Any, port));
                    ret.Add(new IPEndPoint(IPAddress.IPv6Any, port));
                }
                else
                {
                    ret.Add(new IPEndPoint(IPAddress.Loopback, port));
                    ret.Add(new IPEndPoint(IPAddress.IPv6Loopback, port));
                }
            }

            return ret.ToArray();
        }

        // ユーザーが利用できるホストアドレスかどうか確認
        public static bool IsIPv4UserHostAddress(IPAddress ip, IPAddress subnet)
        {
            IPAddress b = GetBroadcastAddress(ip, subnet);
            IPAddress r = GetRouterAddress(ip, subnet);
            IPAddress n = NormalizeIpNetworkAddress(ip, SubnetMaskToInt4(subnet));

            if (IPUtil.CompareIPAddress(ip, b) || IPUtil.CompareIPAddress(ip, r) || IPUtil.CompareIPAddress(ip, n))
            {
                return false;
            }

            return true;
        }

        // ブロードキャストアドレスの取得
        public static IPAddress GetBroadcastAddress(IPAddress ip, IPAddress subnet)
        {
            if (ip.AddressFamily == AddressFamily.InterNetwork)
            {
                // IPv4
                if (subnet.AddressFamily != AddressFamily.InterNetwork)
                {
                    throw new ArgumentException("invalid address family.");
                }

                if (IsSubnetMask4(subnet) == false)
                {
                    throw new ArgumentException("not a subnet mask.");
                }

                IPAddress network = NormalizeIpNetworkAddress(ip, SubnetMaskToInt4(subnet));

                IPAddress broadcast = IPOr(network, IPNot(subnet));

                return broadcast;
            }
            else if (ip.AddressFamily == AddressFamily.InterNetworkV6)
            {
                // IPv6
                if (subnet.AddressFamily != AddressFamily.InterNetworkV6)
                {
                    throw new ArgumentException("invalid address family.");
                }

                if (IsSubnetMask6(subnet) == false)
                {
                    throw new ArgumentException("not a subnet mask.");
                }

                IPAddress network = NormalizeIpNetworkAddress(ip, SubnetMaskToInt6(subnet));

                IPAddress broadcast = IPOr(network, IPNot(subnet));

                broadcast.ScopeId = ip.ScopeId;

                return broadcast;
            }
            else
            {
                throw new ArgumentException("invalid address family.");
            }
        }
        public static IPAddress GetBroadcastAddress(IPAddress ip, int subnetLength)
        {
            if (ip.AddressFamily == AddressFamily.InterNetwork)
            {
                return GetBroadcastAddress(ip, IPUtil.IntToSubnetMask4(subnetLength));
            }
            else if (ip.AddressFamily == AddressFamily.InterNetworkV6)
            {
                return GetBroadcastAddress(ip, IPUtil.IntToSubnetMask6(subnetLength));
            }
            else
            {
                throw new CoresLibException($"{nameof(ip)} is not IPv4 or IPv6");
            }
        }

        // ルータアドレスの取得
        public static IPAddress GetRouterAddress(IPAddress ip, IPAddress subnet)
        {
            if (ip.AddressFamily != AddressFamily.InterNetwork || subnet.AddressFamily != AddressFamily.InterNetwork)
            {
                throw new ArgumentException("invalid address family.");
            }

            if (IsSubnetMask4(subnet) == false)
            {
                throw new ArgumentException("not a subnet mask.");
            }

            if (SubnetMaskToInt4(subnet) >= 31)
            {
                throw new ArgumentException("subnet mask must be <= 30");
            }

            IPAddress network = NormalizeIpNetworkAddress(ip, SubnetMaskToInt4(subnet));

            IPAddress broadcast = IPOr(network, IPNot(subnet));

            IPAddress router = (new IPv4Addr(broadcast).Add(-1)).GetIPAddress();

            return router;
        }

        // 指定された 2 つの IP ネットワークが重なるかどうか検出
        public static bool IsOverlappedSubnets(IPAddress ip1, IPAddress subnet1, IPAddress ip2, IPAddress subnet2)
        {
            // 正規化
            if (IPUtil.IsSubnetMask(subnet1) == false || IPUtil.IsSubnetMask(subnet2) == false)
            {
                return false;
            }

            ip1 = IPUtil.GetPrefixAddress(ip1, subnet1);
            ip2 = IPUtil.GetPrefixAddress(ip2, subnet2);

            KeyValuePair<IPAddress, IPAddress> minmax1 = GetMinMaxIPFromSubnet(ip1, IPUtil.SubnetMaskToInt(subnet1));
            KeyValuePair<IPAddress, IPAddress> minmax2 = GetMinMaxIPFromSubnet(ip2, IPUtil.SubnetMaskToInt(subnet2));

            BigNumber min1 = IPAddr.FromBytes(minmax1.Key.GetAddressBytes()).GetBigNumber();
            BigNumber max1 = IPAddr.FromBytes(minmax1.Value.GetAddressBytes()).GetBigNumber();

            BigNumber min2 = IPAddr.FromBytes(minmax2.Key.GetAddressBytes()).GetBigNumber();
            BigNumber max2 = IPAddr.FromBytes(minmax2.Value.GetAddressBytes()).GetBigNumber();

            if (min2 >= min1 && min2 <= max1 && max1 >= min2 && max1 <= max2)
            {
                return true;
            }
            if (min1 >= min2 && max1 <= max2)
            {
                return true;
            }
            if (min1 >= min2 && min1 <= max2 && max2 >= min1 && max2 >= max1)
            {
                return true;
            }
            if (min2 >= min1 && max2 <= max1)
            {
                return true;
            }

            return false;
        }

        // IP 個数を計算
        public static long CalcNumIPFromSubnetLen(AddressFamily af, int subnetLen)
        {
            if (af == AddressFamily.InterNetwork)
            {
                return (long)(1UL << (32 - subnetLen));
            }
            else
            {
                int v = 128 - subnetLen;
                if (v < 0)
                {
                    v = 0;
                }
                return (long)(1UL << v);
            }
        }


        // IPv6 射影アドレスを IPv4 アドレスに変換
        [return: NotNullIfNotNull("addr")]
        public static IPAddress? UnmapIPv6AddressToIPv4Address(IPAddress? addr)
        {
            if (addr == null) return null;

            if (addr.IsIPv4MappedToIPv6)
            {
                return addr.MapToIPv4();
            }

            return addr;
        }

        // ホスト名とポート番号から文字列に結合
        public static string BuildHostPortStr(string host, int port, int? defaultPortToOmit = null)
        {
            host = host._NonNullTrim();

            if (host._InStr(":")) host = $"[{host}]";

            if (port == (defaultPortToOmit ?? int.MinValue))
            {
                return host;
            }
            else
            {
                return host + ":" + port;
            }
        }

        public static List<IPEndPoint> ParseIpAndPortList(string src, int? defaultPort = null)
        {
            List<IPEndPoint> ret = new List<IPEndPoint>();

            string[] targetToken = src._Split(StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries, " ", "\t", ";", ",", "/");

            HashSet<string> distinctCheck = new HashSet<string>(StrCmpi);

            foreach (var target in targetToken)
            {
                if (IPUtil.TryParseHostPort(target, out string host, out int port, defaultPort))
                {
                    IPEndPoint ep = new IPEndPoint(IPUtil.StrToIP(host)!, port);

                    string test = ep.ToString();

                    if (distinctCheck.Add(test))
                    {
                        ret.Add(ep);
                    }
                }
            }

            return ret;
        }

        // 文字列からホスト名とポート番号にパース
        public static bool TryParseHostPort(string? src, out string host, out int port, int? defaultPort = null)
        {
            if (TryParseHostPortCore(src, out host, out port, defaultPort))
            {
                host = host._TrimBothSideChar('[', ']');

                return true;
            }
            return false;
        }
        static bool TryParseHostPortCore(string? src, out string host, out int port, int? defaultPort = null)
        {
            host = "";
            port = 0;

            src = src._NonNullTrim();
            if (src._IsEmpty()) return false;

            if (src.StartsWith("["))
            {
                if (src._InStr("]"))
                {
                    // [target]:port
                    string tmp = src;
                    int n = tmp.IndexOf("]");
                    if (n != -1)
                    {
                        char[] array = tmp.ToCharArray();
                        for (int i = n; i < array.Length; i++)
                        {
                            if (array[i] == ':')
                            {
                                array[i] = '@';
                            }
                        }

                        tmp = new string(array);
                    }

                    return TryParseHostPortAtmarkInternal(tmp, out host, out port, defaultPort);
                }
            }

            if (src._InStr("@")) return TryParseHostPortAtmarkInternal(src, out host, out port, defaultPort);

            // target:port
            string[] t = src._Split(StringSplitOptions.None, ":");
            if (t.Length == 0)
            {
                return false;
            }

            if (t.Length >= 2)
            {
                // ipv6
                string lastToken = t[t.Length - 1];
                int p = lastToken._ToInt();
                if ((p >= 1 && p <= 65535) && lastToken.All(x => x >= '0' && x <= '9'))
                {
                    // ipv6:port
                    char[] tmp = src.Reverse().ToArray();
                    if ((defaultPort ?? 0) <= 0 || t[0]._InStri("."))
                    {
                        for (int i = 0; i < tmp.Length; i++)
                        {
                            if (tmp[i] == ':')
                            {
                                tmp[i] = '@';
                                break;
                            }
                        }
                    }
                    string tmp2 = new string(tmp.Reverse().ToArray());

                    return TryParseHostPortAtmarkInternal(tmp2, out host, out port, defaultPort);
                }

                if ((defaultPort ?? 0) <= 0)
                {
                    return false;
                }

                host = src;
                port = defaultPort ?? 0;

                return true;
            }

            if ((defaultPort ?? 0) <= 0)
            {
                if (t.Length < 2) return false;
                if (t[1]._ToInt() <= 0) return false;
            }

            if (t.Length >= 2 && t[1]._ToInt() <= 0) return false;

            if (t.Length >= 1 && t[0]._IsFilled())
            {
                host = t[0].Trim();
                if (t.Length >= 2)
                {
                    port = t[1]._ToInt();
                }
            }

            if (port == 0) port = defaultPort ?? 0;
            if (port == 0) return false;

            return true;
        }

        static bool TryParseHostPortAtmarkInternal(string? src, out string host, out int port, int? defaultPort = null)
        {
            host = "";
            port = 0;

            src = src._NonNullTrim();
            if (src._IsEmpty()) return false;

            var t = src._Split(StringSplitOptions.None, "@");

            if ((defaultPort ?? 0) <= 0)
            {
                if (t.Length < 2) return false;
                if (t[1]._ToInt() <= 0) return false;
            }

            if (t.Length >= 2 && t[1]._ToInt() <= 0) return false;

            if (t.Length >= 1 && t[0]._IsFilled())
            {
                host = t[0].Trim();
                if (t.Length >= 2)
                {
                    port = t[1]._ToInt();
                }
            }

            if (port == 0) port = defaultPort ?? 0;
            if (port == 0) return false;

            return true;
        }

        // 文字列を IP エンドポイントに変換
        public static IPEndPoint? StrToIPEndPoint(string? str, int defaultPort, AllowedIPVersions allowed = AllowedIPVersions.All, bool noExceptionAndReturnNull = false)
        {
            try
            {
                if (defaultPort <= 0 || defaultPort >= 65536) throw new ArgumentOutOfRangeException(nameof(defaultPort));

                if (str._IsEmpty()) return null;

                if (TryParseHostPort(str, out string host, out int port, defaultPort) == false) return null;

                IPAddress? ip = StrToIP(host, allowed, noExceptionAndReturnNull);
                if (ip == null) return null;

                IPEndPoint ep = new IPEndPoint(ip, port);

                return ep;
            }
            catch
            {
                if (noExceptionAndReturnNull == false) throw;

                return null;
            }
        }

        // 文字列を IP アドレスに変換
        public static IPAddress? StrToIP(string? str, AllowedIPVersions allowed = AllowedIPVersions.All, bool noExceptionAndReturnNull = false)
        {
            if (str._IsEmpty()) return null;

            if (noExceptionAndReturnNull == false)
            {
                if (Str.InStr(str, ":") == false && Str.InStr(str, ".") == false)
                {
                    throw new ArgumentException("str is not IPv4 nor IPv6 address.");
                }

                IPAddress ip = IPAddress.Parse(str);

                if (CheckIsIpAddressVersionAllowed(ip, allowed) == false)
                {
                    throw new ArgumentOutOfRangeException($"The specified IP address \"{str}\"'s version is not allowed.");
                }

                return ip;
            }
            else
            {
                if (Str.InStr(str, ":") == false && Str.InStr(str, ".") == false)
                {
                    return null;
                }

                if (IPAddress.TryParse(str, out IPAddress? ip) == false)
                    return null;

                if (CheckIsIpAddressVersionAllowed(ip, allowed) == false)
                    return null;

                return ip;
            }
        }

        // IP アドレスの種類を検査
        public static bool CheckIsIpAddressVersionAllowed(IPAddress ip, AllowedIPVersions allowed)
        {
            bool ok = false;

            if (allowed.BitAny(AllowedIPVersions.IPv4)) if (ip.AddressFamily == AddressFamily.InterNetwork) ok = true;

            if (allowed.BitAny(AllowedIPVersions.IPv6)) if (ip.AddressFamily == AddressFamily.InterNetworkV6) ok = true;

            return ok;
        }

        // 指定された IP ネットワークとサブネットマスクから、最初と最後の IP を取得
        public static KeyValuePair<IPAddress, IPAddress> GetMinMaxIPFromSubnet(IPAddress networkAddress, int subnetLen)
        {
            networkAddress = NormalizeIpNetworkAddress(networkAddress, subnetLen);

            if (networkAddress.AddressFamily == AddressFamily.InterNetwork)
            {
                IPAddress mask = IPUtil.IntToSubnetMask4(subnetLen);
                mask = IPUtil.IPNot(mask);

                BigNumber bi = new IPv4Addr(networkAddress).GetBigNumber() + (new IPv4Addr(mask).GetBigNumber());

                IPAddress end = new IPv4Addr(FullRoute.BigNumberToByte(bi, AddressFamily.InterNetwork)).GetIPAddress();

                return new KeyValuePair<IPAddress, IPAddress>(networkAddress, end);
            }
            else if (networkAddress.AddressFamily == AddressFamily.InterNetworkV6)
            {
                IPAddress mask = IPUtil.IntToSubnetMask6(subnetLen);
                mask = IPUtil.IPNot(mask);

                BigNumber bi = new IPv6Addr(networkAddress).GetBigNumber() + (new IPv6Addr(mask).GetBigNumber());

                IPAddress end = new IPv6Addr(FullRoute.BigNumberToByte(bi, AddressFamily.InterNetworkV6)).GetIPAddress();

                return new KeyValuePair<IPAddress, IPAddress>(networkAddress, end);
            }
            else
            {
                throw new ApplicationException("invalid AddressFamily");
            }
        }

        // 指定した IP アドレスに数値を足す
        public static IPAddress IPAdd(IPAddress a, int i)
        {
            IPAddr aa = IPAddr.FromBytes(a.GetAddressBytes());

            BigNumber bi = aa.GetBigNumber();
            bi += i;

            byte[] data = FullRoute.BigNumberToByte(bi, a.AddressFamily);

            return new IPAddress(data);
        }

        // 指定した IP アドレスから先頭 2 バイトを取得 (IPv4 の場合)
        public static string GetHead2BytesIPString(string ip)
        {
            return GetHead2BytesIPString(IPAddress.Parse(ip));
        }
        public static string GetHead2BytesIPString(IPAddress ip)
        {
            if (ip.AddressFamily != AddressFamily.InterNetwork)
            {
                throw new ArgumentException("ip.AddressFamily != AddressFamily.InterNetwork");
            }

            byte[] b = ip.GetAddressBytes();

            return $"{b[0]}.{b[1]}";
        }

        // 指定した IP アドレスから先頭 1 バイトを取得 (IPv4 の場合)
        public static string GetHead1BytesIPString(string ip)
        {
            return GetHead1BytesIPString(IPAddress.Parse(ip));
        }
        public static string GetHead1BytesIPString(IPAddress ip)
        {
            if (ip.AddressFamily != AddressFamily.InterNetwork)
            {
                throw new ArgumentException("ip.AddressFamily != AddressFamily.InterNetwork");
            }

            byte[] b = ip.GetAddressBytes();

            return $"{b[0]}";
        }

        // IP アドレスを文字列に変換
        public static string IPToStr(IPAddress ip, bool allDigits = false)
        {
            if (allDigits == false)
            {
                return ip.ToString();
            }
            else
            {
                ip._CheckIsIPv4OrIPv6AddressFamily();
                var data = ip.GetAddressBytes();
                if (ip.AddressFamily == AddressFamily.InterNetwork)
                {
                    return $"{(uint)data[0]:D3}.{(uint)data[1]:D3}.{(uint)data[2]:D3}.{(uint)data[3]:D3}";
                }
                else
                {
                    StringBuilder sb = new StringBuilder(40);
                    for (int i = 0; i < 8; i++)
                    {
                        uint x = data[i * 2];
                        uint y = data[i * 2 + 1];
                        if (i != 0)
                        {
                            sb.Append(":");
                        }
                        sb.Append($"{x:x2}{y:x2}");
                    }
                    if (ip.ScopeId != 0)
                    {
                        sb.Append("%" + ip.ScopeId);
                    }
                    return sb.ToString();
                }
            }
        }

        // 指定された IP アドレスが IPv4 かどうか検査
        public static bool IsIPv4([NotNullWhen(true)] IPAddress? ip)
        {
            if (ip == null) return false;
            return (ip.AddressFamily == AddressFamily.InterNetwork);
        }

        // 指定された IP アドレスが IPv6 かどうか検査
        public static bool IsIPv6([NotNullWhen(true)] IPAddress? ip)
        {
            if (ip == null) return false;
            return (ip.AddressFamily == AddressFamily.InterNetworkV6);
        }

        // 指定された文字列が IP アドレスかどうか検査
        public static bool IsStrIP(string? str)
        {
            return StrToIP(str, AllowedIPVersions.All, true) != null;
        }

        // 指定された文字列が IPv4 アドレスかどうか検査
        public static bool IsStrIPv4(string? str)
        {
            return StrToIP(str, AllowedIPVersions.IPv4, true) != null;
        }

        // 指定された文字列が IPv6 アドレスかどうか検査
        public static bool IsStrIPv6(string? str)
        {
            return StrToIP(str, AllowedIPVersions.IPv6, true) != null;
        }

        // IPv4 / IPv6 マスクを文字列に変換
        public static string MaskToStr(IPAddress mask, bool alwaysFullAddress)
        {
            if (alwaysFullAddress == false && IsSubnetMask(mask))
            {
                return Str.IntToStr(SubnetMaskToInt(mask));
            }
            else
            {
                return IPToStr(mask);
            }
        }

        // 文字列を IPv4 / IPv6 マスクに変換
        public static IPAddress StrToMask(string str, bool ipv6)
        {
            if (ipv6)
            {
                return StrToMask6(str);
            }
            else
            {
                return StrToMask4(str);
            }
        }

        // 文字列を IPv4 マスクに変換
        public static IPAddress StrToMask4(string str)
        {
            if (str.StartsWith("/"))
            {
                str = str.Substring(1);
            }

            if (Str.IsNumber(str))
            {
                int n = Str.StrToInt(str);
                if (n >= 0 && n <= 32)
                {
                    return IntToSubnetMask4(n);
                }
                else
                {
                    throw new ArgumentException("str is not subnet mask.");
                }
            }
            else
            {
                IPAddress? ip = StrToIP(str);
                if (IsIPv4(ip) == false)
                {
                    throw new ArgumentException("str is not IPv4 address.");
                }
                else
                {
                    return ip!;
                }
            }
        }

        // 文字列を IPv6 マスクに変換
        public static IPAddress StrToMask6(string str)
        {
            if (str.StartsWith("/"))
            {
                str = str.Substring(1);
            }

            if (Str.IsNumber(str))
            {
                int n = Str.StrToInt(str);

                if (n >= 0 && n <= 128)
                {
                    return IntToSubnetMask6(n);
                }
                else
                {
                    throw new ArgumentException("str is not subnet mask.");
                }
            }
            else
            {
                IPAddress? ip = StrToIP(str);
                if (IsIPv6(ip) == false)
                {
                    throw new ArgumentException("str is not IPv6 address.");
                }
                else
                {
                    return ip!;
                }
            }
        }

        // IP アドレスとサブネットマスクのパース
        public static void ParseIPAndSubnetMask(string str, out IPAddress ip, out IPAddress mask)
        {
            ParseIPAndMask(str, out ip, out mask);

            if (IsIPv4(ip))
            {
                if (IsSubnetMask4(mask) == false)
                {
                    throw new ArgumentException("mask is not a subnet.");
                }
            }
            else
            {
                if (IsSubnetMask6(mask) == false)
                {
                    throw new ArgumentException("mask is not a subnet.");
                }
            }
        }

        // IP アドレスとマスクのパース
        public static void ParseIPAndMask(string str, out IPAddress ip, out IPAddress mask)
        {
            string ipstr;
            string? subnetstr;
            IPAddress? ip2, mask2;

            string[] tokens = str.Split('/');

            if (tokens.Length == 2)
            {
                // ip/subnet
                ipstr = tokens[0].Trim();
                subnetstr = tokens[1].Trim();
            }
            else if (tokens.Length == 1)
            {
                // exach host?
                ipstr = tokens[0].Trim();
                subnetstr = null;
            }
            else
            {
                // invalid
                throw new ArgumentException("Invalid ip and subnet mask format.");
            }

            ip2 = StrToIP(ipstr);

            if (subnetstr == null)
            {
                // exact host?
                if (IsIPv6(ip2))
                {
                    // IPv6 exact host
                    ip = ip2;
                    mask = IntToSubnetMask6(128);
                }
                else if (IsIPv4(ip2))
                {
                    // IPv4 exact host
                    ip = ip2;
                    mask = IntToSubnetMask4(32);
                }
                else
                {
                    throw new ArgumentException("ip is invalid.");
                }

                return;
            }

            if (IsStrIP(subnetstr))
            {
                mask2 = StrToIP(subnetstr);
                if (IsIPv6(ip2) && IsIPv6(mask2))
                {
                    // 両方とも IPv6
                    ip = ip2;
                    mask = mask2;
                }
                else if (IsIPv4(ip2) && IsIPv4(mask2))
                {
                    // 両方とも IPv4
                    ip = ip2;
                    mask = mask2;
                }
                else
                {
                    throw new ArgumentException("ip or mask is invalid.");
                }
            }
            else
            {
                if (Str.IsNumber(subnetstr) == false)
                {
                    throw new ArgumentException("mask is invalid.");
                }
                else
                {
                    int i = Str.StrToInt(subnetstr);
                    // マスク部が数値
                    if (IsIPv6(ip2) && i >= 0 && i <= 128)
                    {
                        ip = ip2;
                        mask = IntToSubnetMask6(i);
                    }
                    else if (IsIPv4(ip2) && i <= 32)
                    {
                        ip = ip2;
                        mask = IntToSubnetMask4(i);
                    }
                    else
                    {
                        throw new ArgumentException("ip or mask is invalid.");
                    }
                }
            }
        }

        static readonly IPAddress DummyIPv4ForIPv6 = IPAddress.Parse("223.255.255.254");

        // IPv4 を uint に変換する
        public static uint IPToUINT(IPAddress? ip)
        {
            if (ip == null) return 0;

            if (ip.AddressFamily == AddressFamily.InterNetworkV6) ip = DummyIPv4ForIPv6;

            int i;
            Buf b = new Buf();
            if (IsIPv4(ip) == false)
            {
                throw new ArgumentException("ip is not IPv4.");
            }

            byte[] data = ip.GetAddressBytes();

            for (i = 0; i < 4; i++)
            {
                b.WriteByte(data[i]);
            }

            b.SeekToBegin();

            return b.RawReadInt();
        }

        // uint を IPv4 に変換する
        public static IPAddress UINTToIP(uint value)
        {
            Buf b = new Buf();
            b.RawWriteInt(value);

            b.SeekToBegin();

            return new IPAddress(b.ByteData);
        }

        // 指定された IP アドレスがサブネットマスクかどうか調べる
        public static bool IsSubnetMask(IPAddress ip)
        {
            if (IsIPv6(ip))
            {
                return IsSubnetMask6(ip);
            }
            else if (IsIPv4(ip))
            {
                return IsSubnetMask4(ip);
            }
            else
            {
                return false;
            }
        }
        public static bool IsSubnetMask4(IPAddress ip)
        {
            uint i;

            if (IsIPv4(ip) == false)
            {
                throw new ArgumentException("ip is not IPv4.");
            }

            i = IPToUINT(ip);
            i = Util.Endian(i);

            switch (i)
            {
                case 0x00000000:
                case 0x80000000:
                case 0xC0000000:
                case 0xE0000000:
                case 0xF0000000:
                case 0xF8000000:
                case 0xFC000000:
                case 0xFE000000:
                case 0xFF000000:
                case 0xFF800000:
                case 0xFFC00000:
                case 0xFFE00000:
                case 0xFFF00000:
                case 0xFFF80000:
                case 0xFFFC0000:
                case 0xFFFE0000:
                case 0xFFFF0000:
                case 0xFFFF8000:
                case 0xFFFFC000:
                case 0xFFFFE000:
                case 0xFFFFF000:
                case 0xFFFFF800:
                case 0xFFFFFC00:
                case 0xFFFFFE00:
                case 0xFFFFFF00:
                case 0xFFFFFF80:
                case 0xFFFFFFC0:
                case 0xFFFFFFE0:
                case 0xFFFFFFF0:
                case 0xFFFFFFF8:
                case 0xFFFFFFFC:
                case 0xFFFFFFFE:
                case 0xFFFFFFFF:
                    return true;
            }

            return false;
        }

        public static IPAddress IntToSubnetMask(AddressFamily family, int i)
        {
            if (family == AddressFamily.InterNetwork)
            {
                return IntToSubnetMask4(i);
            }
            else if (family == AddressFamily.InterNetworkV6)
            {
                return IntToSubnetMask6(i);
            }
            else
            {
                throw new CoresLibException("Invalid address family");
            }
        }

        public static IPAddress IntToSubnetMask4(int i)
        {
            uint ret = 0xffffffff;

            switch (i)
            {
                case 0: ret = 0x00000000; break;
                case 1: ret = 0x80000000; break;
                case 2: ret = 0xC0000000; break;
                case 3: ret = 0xE0000000; break;
                case 4: ret = 0xF0000000; break;
                case 5: ret = 0xF8000000; break;
                case 6: ret = 0xFC000000; break;
                case 7: ret = 0xFE000000; break;
                case 8: ret = 0xFF000000; break;
                case 9: ret = 0xFF800000; break;
                case 10: ret = 0xFFC00000; break;
                case 11: ret = 0xFFE00000; break;
                case 12: ret = 0xFFF00000; break;
                case 13: ret = 0xFFF80000; break;
                case 14: ret = 0xFFFC0000; break;
                case 15: ret = 0xFFFE0000; break;
                case 16: ret = 0xFFFF0000; break;
                case 17: ret = 0xFFFF8000; break;
                case 18: ret = 0xFFFFC000; break;
                case 19: ret = 0xFFFFE000; break;
                case 20: ret = 0xFFFFF000; break;
                case 21: ret = 0xFFFFF800; break;
                case 22: ret = 0xFFFFFC00; break;
                case 23: ret = 0xFFFFFE00; break;
                case 24: ret = 0xFFFFFF00; break;
                case 25: ret = 0xFFFFFF80; break;
                case 26: ret = 0xFFFFFFC0; break;
                case 27: ret = 0xFFFFFFE0; break;
                case 28: ret = 0xFFFFFFF0; break;
                case 29: ret = 0xFFFFFFF8; break;
                case 30: ret = 0xFFFFFFFC; break;
                case 31: ret = 0xFFFFFFFE; break;
                case 32: ret = 0xFFFFFFFF; break;
                default:
                    throw new ArgumentException($"{i} is not IPv4 subnet numeric.");
            }

            ret = Util.Endian(ret);

            return UINTToIP(ret);
        }

        // AddressFamily からホストを意味するサブネット長を取得する
        public static int GetSubnetLenForAddressFamily(AddressFamily af)
        {
            switch (af)
            {
                case AddressFamily.InterNetwork:
                    return 32;
                case AddressFamily.InterNetworkV6:
                    return 128;
                default:
                    throw new CoresException("Invalid AddressFamily");
            }
        }

        // 指定されたサブネット長がホストアドレスを指定するものかどうか検索する
        public static bool IsSubnetLenHostAddress(AddressFamily af, int len)
        {
            return (GetSubnetLenForAddressFamily(af) == len);
        }
        public static bool IsSubnetMaskHostAddress(AddressFamily af, IPAddress mask)
        {
            return IsSubnetLenHostAddress(af, IPUtil.SubnetMaskToInt(mask));
        }

        // サブネットマスクを数値に変換する
        public static int SubnetMaskToInt(IPAddress ip)
        {
            if (IsIPv6(ip))
            {
                return SubnetMaskToInt6(ip);
            }
            else if (IsIPv4(ip))
            {
                return SubnetMaskToInt4(ip);
            }
            else
            {
                throw new ArgumentException("ip is not IPv4 nor IPv6.");
            }
        }
        public static int SubnetMaskToInt4(IPAddress ip)
        {
            int i;
            if (IsIPv4(ip) == false)
            {
                throw new ArgumentException("ip is not IPv4.");
            }

            for (i = 0; i <= 32; i++)
            {
                IPAddress tmp = IntToSubnetMask4(i);

                if (Util.MemEquals(tmp.GetAddressBytes(), ip.GetAddressBytes()))
                {
                    return i;
                }
            }

            throw new ArgumentException("ip is not IPv4 Subnet Mask.");
        }
        public static int SubnetMaskToInt6(IPAddress ip)
        {
            int i;
            if (IsIPv6(ip) == false)
            {
                throw new ArgumentException("ip is not IPv6.");
            }

            for (i = 0; i <= 128; i++)
            {
                IPAddress tmp = IntToSubnetMask6(i);

                if (Util.MemEquals(tmp.GetAddressBytes(), ip.GetAddressBytes()))
                {
                    return i;
                }
            }

            throw new ArgumentException("ip is not IPv6 Subnet Mask.");
        }

        // サブネットマスクの作成
        public static IPAddress IntToSubnetMask6(int i)
        {
            if (!(i >= 0 && i <= 128))
            {
                throw new ArgumentException("i is not IPv6 Subnet Mask Numeric.");
            }
            int j = i / 8;
            int k = i % 8;
            int z;

            byte[] tmp = new byte[16];

            for (z = 0; z < 16; z++)
            {
                if (z < j)
                {
                    tmp[z] = 0xff;
                }
                else if (z == j)
                {
                    tmp[z] = (byte)((~(0xff >> k)) & 0xff);
                }
            }

            return new IPAddress(tmp);
        }

        // 指定された文字列が IPv6 サブネットマスクかどうか識別する
        public static bool IsSubnetMask6(IPAddress ip)
        {
            try
            {
                SubnetMaskToInt6(ip);
                return true;
            }
            catch
            {
                return false;
            }
        }

        // IP ネットワークアドレスの正規化
        public static IPAddress NormalizeIpNetworkAddress(IPAddress a, int subnetLength)
        {
            IPAddress mask;

            if (a.AddressFamily == AddressFamily.InterNetwork)
            {
                mask = IPUtil.IntToSubnetMask4(subnetLength);
            }
            else if (a.AddressFamily == AddressFamily.InterNetworkV6)
            {
                mask = IPUtil.IntToSubnetMask6(subnetLength);
            }
            else
            {
                throw new ApplicationException("invalid AddressFamily");
            }

            IPAddress ret = IPUtil.IPAnd(a, mask);

            return ret;
        }

        // IP ネットワークアドレスの正規化
        public static IPAddress NormalizeIpNetworkAddressIPv4v6(IPAddress a, int ipv4SubnetLength, int ipv6SubnetLength)
        {
            IPAddress mask;

            if (a.AddressFamily == AddressFamily.InterNetwork)
            {
                mask = IPUtil.IntToSubnetMask4(ipv4SubnetLength);
            }
            else if (a.AddressFamily == AddressFamily.InterNetworkV6)
            {
                mask = IPUtil.IntToSubnetMask6(ipv6SubnetLength);
            }
            else
            {
                throw new ApplicationException("invalid AddressFamily");
            }

            IPAddress ret = IPUtil.IPAnd(a, mask);

            return ret;
        }

        // すべてのビットが立っているアドレス
        public static IPAddress IPv6AllFilledAddress
        {
            get
            {
                byte[] data = new byte[16];
                int i;
                for (i = 0; i < 16; i++)
                {
                    data[i] = 0xff;
                }

                return new IPAddress(data);
            }
        }

        public static readonly IPAddress IPv6AllFilledAddressCache = IPv6AllFilledAddress;

        // ループバックアドレス
        public static IPAddress IPv6LoopbackAddress
        {
            get
            {
                byte[] data = new byte[16];
                data[15] = 0x01;
                return new IPAddress(data);
            }
        }

        // 全ノードマルチキャストアドレス
        public static IPAddress IPv6AllNodeMulticaseAddress
        {
            get
            {
                byte[] data = new byte[16];
                data[0] = 0xff;
                data[1] = 0x02;
                data[15] = 0x01;
                return new IPAddress(data);
            }
        }

        // 全ルータマルチキャストアドレス
        public static IPAddress IPv6AllRouterMulticastAddress
        {
            get
            {
                byte[] data = new byte[16];
                data[0] = 0xff;
                data[1] = 0x02;
                data[15] = 0x02;
                return new IPAddress(data);
            }
        }

        // IPv6 アドレスの論理演算
        public static IPAddress IPAnd(IPAddress a, IPAddress b)
        {
            if (a.AddressFamily != b.AddressFamily)
            {
                throw new ArgumentException("a.AddressFamily != b.AddressFamily");
            }
            byte[] a_data = a.GetAddressBytes();
            byte[] b_data = b.GetAddressBytes();
            byte[] data = new byte[a_data.Length];
            int i;
            for (i = 0; i < data.Length; i++)
            {
                data[i] = (byte)(a_data[i] & b_data[i]);
            }
            return new IPAddress(data);
        }
        public static IPAddress IPOr(IPAddress a, IPAddress b)
        {
            if (a.AddressFamily != b.AddressFamily)
            {
                throw new ArgumentException("a.AddressFamily != b.AddressFamily");
            }
            byte[] a_data = a.GetAddressBytes();
            byte[] b_data = b.GetAddressBytes();
            byte[] data = new byte[a_data.Length];
            int i;
            for (i = 0; i < data.Length; i++)
            {
                data[i] = (byte)(a_data[i] | b_data[i]);
            }
            return new IPAddress(data);
        }
        public static IPAddress IPNot(IPAddress a)
        {
            byte[] a_data = a.GetAddressBytes();
            byte[] data = new byte[a_data.Length];
            int i;
            for (i = 0; i < data.Length; i++)
            {
                data[i] = (byte)(~(a_data[i]));
            }
            return new IPAddress(data);
        }

        // IP アドレス同士を比較する
        public static bool CompareIPAddress(string? a, string? b, AllowedIPVersions allowed = AllowedIPVersions.All, bool noException = false)
        {
            try
            {
                return CompareIPAddress(StrToIP(a, allowed, noException), StrToIP(b, allowed, noException));
            }
            catch
            {
                if (noException) return false; // 念のため
                throw;
            }
        }
        public static bool CompareIPAddress(IPAddress? a, IPAddress? b)
        {
            if (a == null && b == null) return true;
            if (a == null || b == null) return false;

            if (a.AddressFamily != b.AddressFamily)
            {
                return false;
            }

            return Util.MemEquals(a.GetAddressBytes(), b.GetAddressBytes());
        }

        // IP アドレス同士を比較する
        public static int CompareIPAddressRetInt(string? a, string? b)
        {
            return CompareIPAddressRetInt(StrToIP(a), StrToIP(b));
        }
        public static int CompareIPAddressRetInt(IPAddress? a, IPAddress? b)
        {
            if (a == null && b == null) return 0;
            if (a == null) return 1;
            if (b == null) return -1;

            if (a.AddressFamily != b.AddressFamily)
            {
                return a.AddressFamily.CompareTo(b.AddressFamily);
            }

            return Util.MemCompare(a.GetAddressBytes(), b.GetAddressBytes());
        }

        // IP アドレスを正規化する
        public static string NormalizeIPAddress(string str)
        {
            Str.NormalizeString(ref str);

            if (str._IsEmpty()) return "";

            try
            {
                if (IPAddress.TryParse(str, out IPAddress? a) == false)
                {
                    return str.ToLowerInvariant();
                }

                if (a.AddressFamily == AddressFamily.InterNetworkV6)
                {
                    return a.ToString().ToLowerInvariant();
                }
                else if (a.AddressFamily == AddressFamily.InterNetwork)
                {
                    return a.ToString().ToLowerInvariant();
                }
                else
                {
                    return str.ToLowerInvariant();
                }
            }
            catch
            {
            }

            return str.ToLowerInvariant();
        }

        // MAC アドレスをバイト配列に変換する
        public static byte[] MacToBytes(string mac)
        {
            Str.NormalizeString(ref mac);

            mac = mac.Replace(":", "").Replace("-", "");

            byte[] ret = Str.HexToByte(mac);

            if (ret.Length != 6)
            {
                throw new ApplicationException("Invalid MAC address");
            }

            return ret;
        }

        // バイト配列を MAC アドレスに変換する
        public static string BytesToMac(byte[] b, bool linuxStyle)
        {
            if (b.Length != 6)
            {
                throw new ApplicationException("Invalid MAC address");
            }

            string ret = Str.ByteToHex(b, linuxStyle ? ":" : "-");

            if (linuxStyle == false)
            {
                ret = ret.ToUpperInvariant();
            }
            else
            {
                ret = ret.ToLowerInvariant();
            }

            return ret;
        }

        // MAC アドレスを正規化する
        public static string NormalizeMac(string mac, bool linuxStyle)
        {
            return BytesToMac(MacToBytes(mac), linuxStyle);
        }

        // IPv6 アドレスを正規化する
        public static string NormalizeIPv6Address(string str)
        {
            IPAddr a = IPAddr.FromString(str);

            if (a.AddressFamily == AddressFamily.InterNetworkV6)
            {
                return a.ToString().ToLowerInvariant();
            }

            throw new ApplicationException("a.AddressFamily != AddressFamily.InterNetworkV6");
        }

        // IPv4 アドレスを正規化する
        public static string NormalizeIPv4Address(string str)
        {
            IPAddr a = IPAddr.FromString(str);

            if (a.AddressFamily == AddressFamily.InterNetwork)
            {
                return a.ToString().ToLowerInvariant();
            }

            throw new ApplicationException("a.AddressFamily != AddressFamily.InterNetwork");
        }

        // IP アドレスの種類を取得する
        public static IPAddressType GetIPAddressType(string ipStr) => GetIPAddressType(IPUtil.StrToIP(ipStr));
        public static IPAddressType GetIPAddressType(IPAddress? ip)
        {
            if (IsIPv4(ip)) return GetIPv4AddressType(ip);
            if (IsIPv6(ip)) return GetIPv6AddressType(ip);
            throw new ApplicationException("ip is not IPv4/IPv6.");
        }

        // IPv4 アドレスの種類を取得する
        public static IPAddressType GetIPv4AddressType(IPAddress ip)
        {
            IPAddressType ret = IPAddressType.IPv4;
            if (IsIPv4(ip) == false)
            {
                throw new ArgumentException("ip is not IPv6.");
            }

            byte[] data = ip.GetAddressBytes();

            if (Util.IsZero(data)) ret |= IPAddressType.Zero;

            if (ret.Bit(IPAddressType.Zero) == false)
            {
                ret |= IPAddressType.Loopback.If(IPUtil.IsInSubnet(ip, "127.0.0.0/8"));

                if (data[0] == 0xff && data[1] == 0xff && data[2] == 0xff && data[3] == 0xff)
                {
                    ret |= IPAddressType.IPv4_Broadcast;
                }
                else
                {
                    if (data[0] >= 224 && data[0] <= 239)
                    {
                        ret |= IPAddressType.Multicast;
                    }
                    else
                    {
                        ret |= IPAddressType.Unicast;

                        if (IPUtil.IsInSubnet(ip, "100.64.0.0/16"))
                        {
                            ret |= IPAddressType.IPv4_IspShared;
                        }
                        else if (IPUtil.IsInSubnet(ip, "192.168.0.0/16") ||
                            IPUtil.IsInSubnet(ip, "172.16.0.0/12") ||
                            IPUtil.IsInSubnet(ip, "10.0.0.0/8"))
                        {
                            ret |= IPAddressType.LocalUnicast;
                        }
                        else if (IPUtil.IsInSubnet(ip, "169.254.0.0/16"))
                        {
                            ret |= IPAddressType.IPv4_APIPA;
                            ret |= IPAddressType.LocalUnicast;
                        }
                        else
                        {
                            ret |= IPAddressType.GlobalUnicast;

                            if (ret.Bit(IPAddressType.Loopback) == false)
                            {
                                ret |= IPAddressType.GlobalIp;
                            }
                        }
                    }
                }
            }
            else
            {
                ret |= IPAddressType.Unicast;
            }

            return ret;
        }

        // IPv6 アドレスの種類を取得する
        public static IPAddressType GetIPv6AddressType(IPAddress ip)
        {
            IPAddressType ret = IPAddressType.IPv6;
            byte[] data;
            if (IsIPv6(ip) == false)
            {
                throw new ArgumentException("ip is not IPv6.");
            }

            data = ip.GetAddressBytes();

            if (data[0] == 0xff)
            {
                IPAddress all_node = IPv6AllNodeMulticaseAddress;
                IPAddress all_router = IPv6AllRouterMulticastAddress;

                ret |= IPAddressType.Multicast;

                if (CompareIPAddress(ip, all_node))
                {
                    ret |= IPAddressType.IPv6_AllNodeMulticast;
                }
                else if (CompareIPAddress(ip, all_router))
                {
                    ret |= IPAddressType.IPv6_AllRouterMulticast;
                }
                else
                {
                    byte[] addr = ip.GetAddressBytes();
                    if (addr[1] == 0x02 && addr[2] == 0 && addr[3] == 0 &&
                        addr[4] == 0 && addr[5] == 0 && addr[6] == 0 &&
                        addr[7] == 0 && addr[8] == 0 && addr[9] == 0 &&
                        addr[10] == 0 && addr[11] == 0x01 && addr[12] == 0xff)
                    {
                        ret |= IPAddressType.IPv6_SoliciationMulticast;
                    }
                }
            }
            else
            {
                ret |= IPAddressType.Unicast;

                byte[] addr = ip.GetAddressBytes();

                if (addr[0] == 0xfe && (addr[1] & 0xc0) == 0x80)
                {
                    ret |= IPAddressType.LocalUnicast;
                }
                else
                {
                    if (Util.IsZero(addr))
                    {
                        ret |= IPAddressType.Zero;
                    }
                    else
                    {
                        ret |= IPAddressType.GlobalUnicast;

                        if (CompareIPAddress(ip, IPv6LoopbackAddress))
                        {
                            ret |= IPAddressType.Loopback;
                        }
                        else
                        {
                            ret |= IPAddressType.GlobalIp;
                        }
                    }
                }
            }

            return ret;
        }

        // ローカルホストの IPv4 アドレスリストを取得
        public static IPAddress[] GetLocalIPv4Addresses()
        {
            try
            {
                IPAddress[] localIPs = Dns.GetHostAddresses(Dns.GetHostName());

                List<IPAddress> ret = new List<IPAddress>();

                foreach (IPAddress a in localIPs)
                {
                    if (a.AddressFamily == AddressFamily.InterNetwork)
                    {
                        if (a.GetAddressBytes()[0] != 127)
                        {
                            ret.Add(a);
                        }
                    }
                }

                return ret.ToArray();
            }
            catch
            {
                return new IPAddress[0];
            }
        }

        public static byte[] GenerateRandomLocalMacAddress()
        {
            byte[] ret = Secure.Rand(6);

            byte a = ret[0];

            a |= 0x02;
            a &= 0xFE;

            ret[0] = a;

            return ret;
        }

        // マルチキャストアドレスに対応した MAC アドレスの生成
        public static byte[] GenerateMulticastMacAddress(IPAddress ip)
        {
            byte[] mac = new byte[6];
            byte[] addr = ip.GetAddressBytes();

            mac[0] = 0x33;
            mac[1] = 0x33;
            mac[2] = addr[12];
            mac[3] = addr[13];
            mac[4] = addr[14];
            mac[5] = addr[15];

            return mac;
        }

        // 要請ノードマルチキャストアドレスを取得
        public static IPAddress GetSoliciationMulticastAddr(IPAddress src)
        {
            IPAddress prefix, mask104, or1, or2;
            IPAddress dst;

            byte[] addr = new byte[16];
            addr[0] = 0xff;
            addr[1] = 0x02;
            addr[11] = 0x01;
            addr[12] = 0xff;

            prefix = new IPAddress(addr);

            mask104 = IntToSubnetMask6(104);

            or1 = IPAnd(prefix, mask104);
            or2 = IPAnd(src, mask104);
            dst = IPOr(or1, or2);

            dst.ScopeId = src.ScopeId;

            return dst;
        }

        // IP アドレスがオールゼロかどうか検査する
        public static bool IsZeroIP(IPAddress ip)
        {
            return Util.IsZero(ip.GetAddressBytes());
        }

        // プレフィックスアドレスの取得
        public static IPAddress GetPrefixAddress(IPAddress ip, IPAddress subnet)
        {
            IPAddress dst = IPAnd(ip, subnet);

            if (IsIPv6(ip))
            {
                dst.ScopeId = ip.ScopeId;
            }

            return dst;
        }
        public static IPAddress GetPrefixAddress(IPAddress ip, int subnetLength)
        {
            if (ip.AddressFamily == AddressFamily.InterNetwork)
            {
                return GetPrefixAddress(ip, IPUtil.IntToSubnetMask4(subnetLength));
            }
            else if (ip.AddressFamily == AddressFamily.InterNetworkV6)
            {
                return GetPrefixAddress(ip, IPUtil.IntToSubnetMask6(subnetLength));
            }
            else
            {
                throw new CoresLibException($"{nameof(ip)} is not IPv4 or IPv6");
            }
        }

        // ホストアドレスの取得
        public static IPAddress GetHostAddress(IPAddress ip, IPAddress subnet)
        {
            IPAddress dst = IPAnd(ip, IPNot(subnet));

            if (IsIPv6(ip))
            {
                dst.ScopeId = ip.ScopeId;
            }

            return dst;
        }

        // ネットワークプレフィックスアドレスかどうかチェックする
        public static bool IsNetworkPrefixAddress(IPAddress ip, IPAddress subnet)
        {
            IPAddress host;

            host = GetHostAddress(ip, subnet);

            return IsZeroIP(host);
        }

        // IP サブネットリスト文字列をパースする
        public static List<Pair2<IPAddress, IPAddress>> ParseIpSubnetListStr(string str)
        {
            List<Pair2<IPAddress, IPAddress>> ret = new List<Pair2<IPAddress, IPAddress>>();

            var tokens = str._Split(StringSplitOptions.RemoveEmptyEntries, " ", ",", "|", "　");

            foreach (string token in tokens)
            {
                try
                {
                    ParseIPAndSubnetMask(token.Trim(), out IPAddress ip, out IPAddress mask);

                    ret.Add(new Pair2<IPAddress, IPAddress>(ip, mask));
                }
                catch { }
            }

            return ret;
        }

        // IP サブネットリスト文字列をもとに IP アドレスの一覧を生成する
        public static List<IPAddress> GenerateIpAddressListFromIpSubnetList(IEnumerable<Pair2<IPAddress, IPAddress>> subnetList)
        {
            HashSet<IPAddress> hash = new HashSet<IPAddress>();

            subnetList._DoForEach(subnet => GenerateIpAddressListFromIpSubnet(subnet.A, subnet.B).ForEach(x => hash.Add(x)));

            return hash.OrderBy(x => x, IpComparer.Comparer).ToList();
        }
        public static List<IPAddress> GenerateIpAddressListFromIpSubnetList(string subnetListStr)
            => GenerateIpAddressListFromIpSubnetList(ParseIpSubnetListStr(subnetListStr));

        // IP サブネットをもとに IP アドレスの一覧を生成する
        public static List<IPAddress> GenerateIpAddressListFromIpSubnet(string subnetStr)
        {
            ParseIPAndSubnetMask(subnetStr.Trim(), out IPAddress ip, out IPAddress mask);

            return GenerateIpAddressListFromIpSubnet(ip, mask);
        }
        public static List<IPAddress> GenerateIpAddressListFromIpSubnet(IPAddress ip, IPAddress subnet)
        {
            checked
            {
                if (IsSubnetMask(subnet) == false) throw new CoresException("IsSubnetMask(subnet) == false");
                var prefixAddress = GetPrefixAddress(ip, subnet);

                int intMask = SubnetMaskToInt(subnet);

                int numAddresses;

                if (ip.AddressFamily == AddressFamily.InterNetwork)
                {
                    // IPv4
                    numAddresses = (int)Math.Pow(2, (32 - intMask));
                }
                else
                {
                    // IPv6
                    numAddresses = (int)Math.Pow(2, (128 - intMask));
                }

                IPAddr start = IPAddr.FromAddress(prefixAddress);

                List<IPAddress> ret = new List<IPAddress>();

                for (int i = 0; i < numAddresses; i++)
                {
                    var addr = start.Add(i);

                    ret.Add(addr.GetIPAddress());
                }

                return ret;
            }
        }

        // IP アドレスがサブネットに属しているかどうか調べる
        public static bool IsInSubnet(IPAddress ip, string subnetString, bool ignoreScopeId = false)
        {
            ParseIPAndSubnetMask(subnetString, out IPAddress ip2, out IPAddress subnet);
            return IsInSubnet(ip, ip2, subnet, ignoreScopeId);
        }
        public static bool IsInSubnet(IPAddress ip1, IPAddress ip2, IPAddress subnet, bool ignoreScopeId = false)
        {
            if (IsSubnetMask(subnet) == false)
                throw new ArgumentException("mask is not a subnet.");
            return IsInSameNetwork(ip1, ip2, subnet, ignoreScopeId);
        }

        // 同一のネットワークかどうか調べる
        public static bool IsInSameNetwork(IPAddress ip1, IPAddress ip2, IPAddress subnet, bool ignoreScopeId = false)
        {
            if (ip1.AddressFamily != subnet.AddressFamily) return false;
            if (ip2.AddressFamily != subnet.AddressFamily) return false;

            IPAddress prefix1, prefix2;

            if (ignoreScopeId == false)
            {
                if (IsIPv6(ip1))
                {
                    if (ip1.ScopeId != ip2.ScopeId)
                    {
                        return false;
                    }
                }
            }

            prefix1 = GetPrefixAddress(ip1, subnet);
            prefix2 = GetPrefixAddress(ip2, subnet);

            if (CompareIPAddress(prefix1, prefix2))
            {
                return true;
            }

            return false;
        }

        // MAC アドレスから EUI-64 アドレスを生成する
        public static IPAddress GenerateEui64LocalAddress(byte[] mac)
        {
            byte[] tmp = new byte[8];

            Util.CopyByte(tmp, 0, mac, 0, 3);
            Util.CopyByte(tmp, 5, mac, 3, 3);

            tmp[3] = 0xff;
            tmp[4] = 0xfe;
            tmp[0] = (byte)(((~(tmp[0] & 0x02)) & 0x02) | (tmp[0] & 0xfd));

            byte[] addr = new byte[16];
            addr[0] = 0xfe;
            addr[1] = 0x80;

            Util.CopyByte(addr, 8, tmp, 0, 8);

            return new IPAddress(addr);
        }

        // MAC アドレスからグローバルアドレスを生成する
        public static IPAddress GenerateEui64GlobalAddress(IPAddress prefix, IPAddress subnet, byte[] mac)
        {
            if (prefix.AddressFamily != AddressFamily.InterNetworkV6)
            {
                throw new ApplicationException("Not IPv6 address");
            }

            if (subnet.AddressFamily != AddressFamily.InterNetworkV6)
            {
                throw new ApplicationException("Not IPv6 address");
            }

            byte[] tmp = new byte[8];

            Util.CopyByte(tmp, 0, mac, 0, 3);
            Util.CopyByte(tmp, 5, mac, 3, 3);

            tmp[3] = 0xff;
            tmp[4] = 0xfe;
            tmp[0] = (byte)(((~(tmp[0] & 0x02)) & 0x02) | (tmp[0] & 0xfd));

            byte[] addr = new byte[16];

            Util.CopyByte(addr, 8, tmp, 0, 8);

            IPAddress subnet_not = IPNot(subnet);
            IPAddress or1 = IPAnd(new IPAddress(addr), subnet_not);
            IPAddress or2 = IPAnd(prefix, subnet);

            return IPOr(or1, or2);
        }

        // MAC アドレスを比較する
        public static int CompareMacAddressRetInt(string a, string b)
        {
            return CompareMacAddressRetInt(MacToBytes(a), MacToBytes(b));
        }
        public static int CompareMacAddressRetInt(byte[] a, byte[] b)
        {
            if (a.Length != 6 || b.Length != 6)
            {
                throw new ApplicationException("Invalid MAC address");
            }

            return Util.MemCompare(a, b);
        }
        public static bool CompareMacAddress(string a, string b)
        {
            return CompareMacAddress(MacToBytes(a), MacToBytes(b));
        }
        public static bool CompareMacAddress(byte[] a, byte[] b)
        {
            if (a.Length != 6 || b.Length != 6)
            {
                throw new ApplicationException("Invalid MAC address");
            }

            return Util.MemEquals(a, b);
        }

        // EUI-64 から MAC アドレスを取得する
        public static byte[] GetMacAddressFromEui64Address(string addr)
        {
            return GetMacAddressFromEui64Address(StrToIP(addr)!);
        }
        public static byte[] GetMacAddressFromEui64Address(IPAddress? addr)
        {
            if (addr == null || addr.AddressFamily != AddressFamily.InterNetworkV6)
            {
                throw new ApplicationException("Not IPv6 address");
            }

            byte[] tmp = addr.GetAddressBytes();

            tmp = Util.CopyByte(tmp, 8);

            if (tmp[3] != 0xff || tmp[4] != 0xfe)
            {
                throw new ApplicationException("Not an EUI-64 address");
            }

            tmp[0] = (byte)(((~(tmp[0] & 0x02)) & 0x02) | (tmp[0] & 0xfd));

            byte[] mac = new byte[6];
            mac[0] = tmp[0];
            mac[1] = tmp[1];
            mac[2] = tmp[2];
            mac[3] = tmp[5];
            mac[4] = tmp[6];
            mac[5] = tmp[7];

            return mac;
        }

        // 指定された IPv6 アドレスが指定された MAC アドレスから生成されたものかどうか判定する
        public static bool IsIPv6AddressEui64ForMac(string addr, string mac)
        {
            try
            {
                return IsIPv6AddressEui64ForMac(StrToIP(addr), MacToBytes(mac));
            }
            catch
            {
            }

            return false;
        }
        public static bool IsIPv6AddressEui64ForMac(IPAddress? addr, byte[] mac)
        {
            try
            {
                if (addr == null) return false;
                return CompareMacAddress(mac, GetMacAddressFromEui64Address(addr));
            }
            catch
            {
            }

            return false;
        }

        // 指定された IPv4 アドレスの逆引き PTR レコードを取得する
        public static string GetIPv4PtrRecord(IPAddress ip)
        {
            if (ip.AddressFamily != AddressFamily.InterNetwork) throw new ArgumentException(nameof(ip) + " is not IPv4");
            byte[] ip4 = ip.GetAddressBytes();
            return $"{ip4[3]}.{ip4[2]}.{ip4[1]}.{ip4[0]}.in-addr.arpa";
        }

        // in-addr.arpa または ip6.arpa 形式の逆引き FQDN から IP サブネットを生成
        public static (IPAddress, int) PtrZoneOrFqdnToIpAddressAndSubnet(string str, bool alreadyNormalized = false)
        {
            const string ipv4End = "in-addr.arpa";
            const string ipv6End = "ip6.arpa";

            if (alreadyNormalized == false)
            {
                str = Str.NormalizeFqdn(str);
            }

            if (str.EndsWith(ipv4End, StringComparison.Ordinal))
            {
                // IPv4
                string tmp = str.Substring(0, str.Length - ipv4End.Length);

                var tokens = tmp._Split(StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries, ".");

                if (tokens.Length > 4)
                {
                    throw new CoresException($"Specified FQDN '{str}' doesn't represent IPv4 PTR address");
                }

                Array.Reverse(tokens);

                Span<byte> data = new byte[4];

                for (int i = 0; i < tokens.Length; i++)
                {
                    data[i] = byte.Parse(tokens[i]);
                }

                return (new IPAddress(data), tokens.Length * 8);
            }
            else if (str.EndsWith(ipv6End, StringComparison.Ordinal))
            {
                // IPv6
                string tmp = str.Substring(0, str.Length - ipv6End.Length);

                var tokens = tmp._Split(StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries, ".");

                if (tokens.Length > 32)
                {
                    throw new CoresException($"Specified FQDN '{str}' doesn't represent IPv6 PTR address");
                }

                Array.Reverse(tokens);

                Span<byte> data = new byte[16];

                for (int i = 0; i < tokens.Length; i++)
                {
                    string token = tokens[i];

                    if (token.Length != 1)
                    {
                        throw new CoresException($"Specified FQDN '{str}' doesn't represent IPv6 PTR address");
                    }

                    char c = token[0];

                    if ((i % 2) == 0)
                    {
                        data[i / 2] = (byte)((Str.Char4BitToByte(c) << 4) & 0xF0);
                    }
                    else
                    {
                        data[i / 2] |= (byte)(Str.Char4BitToByte(c));
                    }
                }

                return (new IPAddress(data), tokens.Length * 4);
            }
            else
            {
                throw new CoresException($"Specified FQDN '{str}' is neither IPv4 or IPv6 PTR address");
            }
        }

        // IP アドレスまたは IP サブネットから in-addr.arpa または ip6.arpa 形式の逆引き FQDN を生成
        public static string IPAddressOrSubnetToPtrZoneOrFqdn(IPAddress ip, int subnetLength = -1, bool withSuffix = true, bool ipv4AllDigitsForSortKey = false)
        {
            if (ip.AddressFamily == AddressFamily.InterNetwork)
            {
                if (subnetLength < 0) subnetLength = 32;

                var prefix = GetPrefixAddress(ip, subnetLength);
                var d = prefix.GetAddressBytes();

                string tmp;

                if (ipv4AllDigitsForSortKey == false)
                {
                    switch (subnetLength)
                    {
                        case 0: tmp = ""; break;
                        case 8: tmp = $"{d[0]}"; break;
                        case 16: tmp = $"{d[1]}.{d[0]}"; break;
                        case 24: tmp = $"{d[2]}.{d[1]}.{d[0]}"; break;
                        case 32: tmp = $"{d[3]}.{d[2]}.{d[1]}.{d[0]}"; break;
                        default: throw new CoresException($"in-addr.arpa prefix length: '{subnetLength}' must be 0, 8, 16, 24 or 32");
                    }
                }
                else
                {
                    switch (subnetLength)
                    {
                        case 0: tmp = ""; break;
                        case 8: tmp = $"{d[0]:D3}"; break;
                        case 16: tmp = $"{d[1]:D3}.{d[0]:D3}"; break;
                        case 24: tmp = $"{d[2]:D3}.{d[1]:D3}.{d[0]:D3}"; break;
                        case 32: tmp = $"{d[3]:D3}.{d[2]:D3}.{d[1]:D3}.{d[0]:D3}"; break;
                        default: throw new CoresException($"in-addr.arpa prefix length: '{subnetLength}' must be 0, 8, 16, 24 or 32");
                    }
                }

                if (withSuffix)
                {
                    if (tmp.Length >= 1)
                    {
                        tmp += ".in-addr.arpa";
                    }
                    else
                    {
                        tmp += "in-addr.arpa";
                    }
                }

                return tmp;
            }
            else if (ip.AddressFamily == AddressFamily.InterNetworkV6)
            {
                if (subnetLength < 0) subnetLength = 128;
                if (subnetLength > 128) throw new CoresException($"invalid IPv6 subnet length: {subnetLength}");
                if ((subnetLength % 4) != 0) throw new CoresException($"in6.arpa prefix length: '{subnetLength}' must be a multiple of 4");

                var prefix = GetPrefixAddress(ip, subnetLength);
                var d = prefix.GetAddressBytes();

                string dstr = d._GetHexString().ToLowerInvariant();

                var dstrArray = dstr.ToCharArray();
                Array.Reverse(dstrArray);

                int bufLength = (subnetLength / 4) * 2 - 1;
                if (bufLength < 0) bufLength = 0;
                Span<char> buf = new char[bufLength];
                int pos = 0;

                for (int i = ((128 - subnetLength) / 4); i < dstrArray.Length; i++)
                {
                    if (pos >= 1)
                    {
                        buf[pos++] = '.';
                    }
                    buf[pos++] = dstrArray[i];
                }

                string tmp = buf.ToString();

                if (withSuffix)
                {
                    if (tmp.Length >= 1)
                    {
                        tmp += ".ip6.arpa";
                    }
                    else
                    {
                        tmp += "ip6.arpa";
                    }
                }

                return tmp;
            }
            else
            {
                throw new ArgumentException(nameof(ip) + " is not IPv4 or IPv6");
            }
        }
    }

    namespace Legacy
    {
        // ソケット
        public class Sock
        {
            static readonly SocketFlags DefaultSocketFlags;
            static Sock()
            {
                if (Env.IsWindows)
                {
                    Sock.DefaultSocketFlags = SocketFlags.Partial;
                }
                else
                {
                    Sock.DefaultSocketFlags = SocketFlags.None;
                }
            }

            public const int TimeoutInfinite = Timeout.Infinite;
            public const int TimeoutTcpPortCheck = 10 * 1000;
            public const int TimeoutSslConnect = 15 * 1000;
            public const int TimeoutGetHostname = 1500;
            public const int MaxSendBufMemSize = 10 * 1024 * 1024;
            public const int SockLater = -1;
            public const string SecureProtocolKey = "SecureProtocol";
            public bool IsIPv6 = false;

            internal object lockObj;
            internal object disconnectLockObj;
            internal object sslLockObj;
            public Socket? Socket { get; private set; }
            public SockType Type { get; private set; }
            public bool Connected { get; private set; }
            public bool ServerMode { get; private set; }
            internal bool asyncMode;
            public bool AsyncMode => asyncMode;
            public bool SecureMode { get; private set; }
            public bool ListenMode { get; private set; }
            bool cancelAccept;
            public IPAddress? RemoteIP { get; private set; }
            public IPAddress? LocalIP { get; private set; }
            public string? RemoteHostName { get; private set; }
            public int RemotePort { get; private set; }
            public int LocalPort { get; private set; }
            public long SendSize { get; private set; }
            public long RecvSize { get; private set; }
            public long SendNum { get; private set; }
            public long RecvNum { get; private set; }
            public bool IgnoreLastRecvError { get; private set; }
            public ulong LastRecvError = 0;
            public bool IgnoreLastSendError { get; private set; }

            int timeOut;
            internal bool writeBlocked;
            internal bool disconnecting;
            public bool UDPBroadcastMode { get; private set; }
            public object? Param { get; set; }

            internal Event? hEvent;

            public SockEvent? SockEvent = null;

            public IntPtr Fd = new IntPtr(-1);

            [DllImport("ws2_32.dll", SetLastError = true)]
            internal static extern int WSAEventSelect(IntPtr s, IntPtr hEventObject, int lNetworkEvents);

            // 初期化
            private Sock()
            {
                this.lockObj = new object();
                this.disconnectLockObj = new object();
                this.sslLockObj = new object();
                this.Socket = null;
                this.Type = SockType.Unknown;
                this.IgnoreLastRecvError = this.IgnoreLastSendError = false;
            }

            // UDP 受信
            public byte[]? RecvFrom(out IPEndPoint? src, int size)
            {
                byte[] data = new byte[size];
                int ret;

                ret = RecvFrom(out src, data, 0, data.Length);

                if (ret > 0)
                {
                    Array.Resize<byte>(ref data, ret);

                    return data;
                }
                else if (ret == SockLater)
                {
                    return new byte[0];
                }
                else
                {
                    return null;
                }
            }
            public int RecvFrom(out IPEndPoint? src, byte[] data)
            {
                return RecvFrom(out src, data, data.Length);
            }
            public int RecvFrom(out IPEndPoint? src, byte[] data, int size)
            {
                return RecvFrom(out src, data, 0, size);
            }
            public int RecvFrom(out IPEndPoint? src, byte[] data, int offset, int size)
            {
                Socket s;
                src = null;
                if (this.Type != SockType.Udp || this.Socket == null)
                {
                    return 0;
                }
                if (size == 0)
                {
                    return 0;
                }

                s = this.Socket;

                int ret = -1;
                SocketError err = SocketError.Success;

                try
                {
                    EndPoint ep = new IPEndPoint(this.IsIPv6 ? IPAddress.IPv6Any : IPAddress.Any, 0);
                    ret = s.ReceiveFrom(data, offset, size, 0, ref ep);
                    src = (IPEndPoint)ep;
                }
                catch (SocketException se)
                {
                    err = se.SocketErrorCode;
                }
                catch
                {
                }

                if (ret > 0)
                {
                    lock (this.lockObj)
                    {
                        this.RecvNum++;
                        this.RecvSize += (long)ret;
                    }

                    return ret;
                }
                else
                {
                    this.IgnoreLastRecvError = false;

                    if (err == SocketError.ConnectionReset || err == SocketError.MessageSize || err == SocketError.NetworkUnreachable ||
                        err == SocketError.NoBufferSpaceAvailable || (int)err == 10068 || err == SocketError.NetworkReset)
                    {
                        this.IgnoreLastRecvError = true;
                    }
                    else if (err == SocketError.WouldBlock)
                    {
                        return SockLater;
                    }
                    else
                    {
                        this.LastRecvError = (ulong)err;
                    }

                    return 0;
                }
            }

            // UDP 送信
            public int SendTo(IPAddress destAddr, int destPort, byte[] data)
            {
                return SendTo(destAddr, destPort, data, data.Length);
            }
            public int SendTo(IPAddress destAddr, int destPort, byte[] data, int size)
            {
                return SendTo(destAddr, destPort, data, 0, size);
            }
            public int SendTo(IPAddress destAddr, int destPort, byte[] data, int offset, int size)
            {
                Socket s;
                bool isBroadcast = false;
                if (this.Type != SockType.Udp || this.Socket == null)
                {
                    return 0;
                }
                if (size == 0)
                {
                    return 0;
                }

                s = this.Socket;

                byte[] destBytes = destAddr.GetAddressBytes();
                if (destAddr.AddressFamily == AddressFamily.InterNetwork)
                {
                    if (destBytes.Length == 4)
                    {
                        if (destBytes[0] == 255 &&
                            destBytes[1] == 255 &&
                            destBytes[2] == 255 &&
                            destBytes[3] == 255)
                        {
                            s.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Broadcast, true);

                            isBroadcast = true;

                            this.UDPBroadcastMode = true;
                        }
                    }
                }

                int ret = -1;
                SocketError err = 0;

                try
                {
                    ret = s.SendTo(data, offset, size, (isBroadcast ? SocketFlags.Broadcast : 0), new IPEndPoint(destAddr, destPort));
                }
                catch (SocketException se)
                {
                    err = se.SocketErrorCode;
                }
                catch
                {
                }

                if (ret != size)
                {
                    this.IgnoreLastSendError = false;

                    if (err == SocketError.ConnectionReset || err == SocketError.MessageSize || err == SocketError.NetworkUnreachable ||
                        err == SocketError.NoBufferSpaceAvailable || (int)err == 10068 || err == SocketError.NetworkReset)
                    {
                        this.IgnoreLastSendError = true;
                    }
                    else if (err == SocketError.WouldBlock)
                    {
                        return SockLater;
                    }

                    return 0;
                }

                lock (this.lockObj)
                {
                    this.SendSize += (long)ret;
                    this.SendNum++;
                }

                return ret;
            }

            // UDP ソケットの作成と初期化
            public static Sock NewUDP()
            {
                return NewUDP(0);
            }
            public static Sock NewUDP(int port)
            {
                return NewUDP(port, IPAddress.Any);
            }
            public static Sock NewUDP(int port, IPAddress endpoint)
            {
                return NewUDP(port, endpoint, false);
            }
            public static Sock NewUDP(int port, IPAddress endpoint, bool ipv6)
            {
                Sock sock;
                Socket s;

                if (ipv6 == false)
                {
                    s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                }
                else
                {
                    s = new Socket(AddressFamily.InterNetworkV6, SocketType.Dgram, ProtocolType.Udp);
                }

                IPEndPoint ep = new IPEndPoint(endpoint, port);
                s.Bind(ep);

                sock = new Sock();
                sock.Type = SockType.Udp;
                sock.Connected = false;
                sock.asyncMode = false;
                sock.ServerMode = false;
                sock.IsIPv6 = ipv6;
                if (port != 0)
                {
                    sock.ServerMode = true;
                }

                sock.Socket = s;
                sock.Fd = s.Handle;

                sock.querySocketInformation();

                return sock;
            }

            // Pack の送信
            public bool SendPack(Pack p)
            {
                Buf b = new Buf();
                byte[] data = p.ByteData;

                b.WriteInt((uint)data.Length);
                b.Write(data);

                return SendAll(b.ByteData);
            }

            // Pack の受信
            public Pack? RecvPack(int maxSize = 0)
            {
                byte[]? sizeData = RecvAll(Util.SizeOfInt32);
                if (sizeData == null)
                {
                    return null;
                }

                int size = Util.ByteToInt(sizeData);

                if (maxSize != 0 && size > maxSize)
                {
                    return null;
                }

                byte[]? data = RecvAll(size);
                if (data == null)
                {
                    return null;
                }

                Buf b = new Buf(data);

                try
                {
                    Pack p = Pack.CreateFromBuf(b);

                    return p;
                }
                catch
                {
                    return null;
                }
            }

            // ソケットを非同期に設定する
            internal void initAsyncSocket()
            {
                try
                {
                    if (this.asyncMode)
                    {
                        return;
                    }
                    if (this.ListenMode != false || (this.Type == SockType.Tcp && this.Connected == false))
                    {
                        return;
                    }

                    this.hEvent = new Event();

                    // 関連付け
                    WSAEventSelect((IntPtr)this.Socket!.Handle, this.hEvent.Handle, 35);
                    this.Socket.Blocking = false;

                    this.asyncMode = true;
                }
                catch
                {
                }
            }

            // TCP すべて受信
            public byte[]? RecvAll(int size)
            {
                byte[] data = new byte[size];
                bool ret = RecvAll(data);
                if (ret)
                {
                    return data;
                }
                else
                {
                    return null;
                }
            }
            public bool RecvAll(byte[] data)
            {
                return RecvAll(data, 0, data.Length);
            }
            public bool RecvAll(byte[] data, int size)
            {
                return RecvAll(data, 0, size);
            }
            public bool RecvAll(byte[] data, int offset, int size)
            {
                int recv_size, sz, ret;
                if (size == 0)
                {
                    return true;
                }
                if (this.asyncMode)
                {
                    return false;
                }

                recv_size = 0;

                while (true)
                {
                    sz = size - recv_size;

                    ret = Recv(data, offset + recv_size, sz);
                    if (ret <= 0)
                    {
                        return false;
                    }

                    recv_size += ret;
                    if (recv_size >= size)
                    {
                        return true;
                    }
                }
            }

            // TCP 受信
            public byte[]? Recv(int size)
            {
                byte[] data = new byte[size];
                int ret = Recv(data);
                if (ret >= 1)
                {
                    Array.Resize<byte>(ref data, ret);
                    return data;
                }
                else if (ret == SockLater)
                {
                    return new byte[0];
                }
                else
                {
                    return null;
                }
            }
            public int Recv(byte[] data)
            {
                return Recv(data, 0, data.Length);
            }
            public int Recv(byte[] data, int size)
            {
                return Recv(data, 0, size);
            }
            public int Recv(byte[] data, int offset, int size)
            {
                Socket s;

                if (this.Type != SockType.Tcp || this.Connected == false || this.ListenMode != false ||
                    this.Socket == null)
                {
                    return 0;
                }

                // 受信
                s = this.Socket;

                int ret = -1;
                SocketError err = 0;
                try
                {
                    ret = s.Receive(data, offset, size, DefaultSocketFlags);
                }
                catch (SocketException se)
                {
                    err = se.SocketErrorCode;
                }
                catch
                {
                }

                if (ret > 0)
                {
                    // 受信成功
                    lock (lockObj)
                    {
                        this.RecvSize += (long)ret;
                        this.RecvNum++;
                    }

                    return ret;
                }

                if (this.asyncMode)
                {
                    if (err == SocketError.WouldBlock)
                    {
                        // ブロッキングしている
                        return SockLater;
                    }
                }

                Disconnect();

                return 0;
            }

            // TCP 送信
            public int Send(byte[] data)
            {
                return Send(data, 0, data.Length);
            }
            public int Send(byte[] data, int size)
            {
                return Send(data, 0, size);
            }
            public int Send(byte[] data, int offset, int size)
            {
                Socket s;
                size = Math.Min(size, MaxSendBufMemSize);
                if (this.Type != SockType.Tcp || this.Connected == false || this.ListenMode != false ||
                    this.Socket == null)
                {
                    return 0;
                }

                // 送信
                s = this.Socket;
                int ret = -1;
                SocketError err = 0;
                try
                {
                    ret = s.Send(data, offset, size, DefaultSocketFlags);
                }
                catch (SocketException se)
                {
                    err = se.SocketErrorCode;
                }
                catch
                {
                }

                if (ret > 0)
                {
                    // 送信成功
                    lock (this.lockObj)
                    {
                        this.SendSize += (long)ret;
                        this.SendNum++;
                    }
                    this.writeBlocked = false;

                    return ret;
                }

                // 送信失敗
                if (this.asyncMode)
                {
                    // 非同期モードの場合、エラーを調べる
                    if (err == SocketError.WouldBlock)
                    {
                        // ブロッキングしている
                        this.writeBlocked = true;

                        return SockLater;
                    }
                }

                // 切断された
                Disconnect();

                return 0;
            }

            // TCP すべて送信
            public bool SendAll(byte[] data)
            {
                return SendAll(data, 0, data.Length);
            }
            public bool SendAll(byte[] data, int size)
            {
                return SendAll(data, 0, size);
            }
            public bool SendAll(byte[] data, int offset, int size)
            {
                if (this.asyncMode)
                {
                    return false;
                }
                if (size == 0)
                {
                    return true;
                }

                int sent_size = 0;

                while (true)
                {
                    int ret = Send(data, offset + sent_size, size - sent_size);
                    if (ret <= 0)
                    {
                        return false;
                    }
                    sent_size += ret;
                    if (sent_size >= size)
                    {
                        return true;
                    }
                }
            }

            // TCP 接続受諾
            public Sock? Accept(bool getHostName = false)
            {
                if (this.ListenMode == false || this.Type != SockType.Tcp || this.ServerMode == false)
                {
                    return null;
                }
                if (this.cancelAccept)
                {
                    return null;
                }

                Socket? s = this.Socket;
                if (s == null)
                {
                    return null;
                }

                try
                {
                    Socket newSocket = s.Accept();

                    if (newSocket == null)
                    {
                        return null;
                    }

                    if (this.cancelAccept)
                    {
                        newSocket.Close();
                        return null;
                    }

                    Sock ret = new Sock();
                    ret.Socket = newSocket;
                    ret.Connected = true;
                    ret.asyncMode = false;
                    ret.Type = SockType.Tcp;
                    ret.ServerMode = true;
                    ret.SecureMode = false;
                    newSocket.NoDelay = true;

                    ret.SetTimeout(TimeoutInfinite);

                    ret.Fd = (IntPtr)ret.Socket.Handle;

                    ret.querySocketInformation();

                    if (getHostName)
                    {
                        try
                        {
                            ret.RemoteHostName = Domain.GetHostName(ret.RemoteIP!, TimeoutGetHostname)[0];
                        }
                        catch
                        {
                            ret.RemoteHostName = ret.RemoteIP!.ToString();
                        }
                    }

                    return ret;
                }
                catch
                {
                    return null;
                }
            }

            // TCP 待ち受け
            public static Sock Listen(int port, bool localOnly = false, bool ipv6 = false)
            {
                Socket s;

                if (ipv6 == false)
                {
                    s = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                }
                else
                {
                    s = new Socket(AddressFamily.InterNetworkV6, SocketType.Stream, ProtocolType.Tcp);
                }

                IPEndPoint ep = new IPEndPoint(ipv6 ? IPAddress.IPv6Any : IPAddress.Any, port);
                if (localOnly)
                {
                    ep = new IPEndPoint(ipv6 ? IPAddress.IPv6Loopback : IPAddress.Loopback, port);
                }

                s.Bind(ep);

                s.Listen(0x7fffffff);

                Sock sock = new Sock();
                sock.Fd = s.Handle;
                sock.Connected = false;
                sock.asyncMode = false;
                sock.ServerMode = true;
                sock.Type = SockType.Tcp;
                sock.Socket = s;
                sock.ListenMode = true;
                sock.SecureMode = false;
                sock.LocalPort = port;
                sock.IsIPv6 = ipv6;

                return sock;
            }

            // TCP 接続
            public static Sock Connect(string hostName, int port, int timeout = 0, bool use46 = false, bool noDnsCache = false)
            {
                if (timeout == 0)
                {
                    timeout = TimeoutInfinite;
                }

                // 正引き
                IPAddress ip;

                if (use46 == false)
                {
                    ip = Domain.GetIP(hostName, noDnsCache)![0];
                }
                else
                {
                    ip = Domain.GetIP46(hostName, noDnsCache)![0];
                }

                IPEndPoint endPoint = new IPEndPoint(ip, port);

                // ソケット作成
                Sock sock = new Sock();
                sock.Socket = new Socket(ip.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                sock.Type = SockType.Tcp;
                sock.ServerMode = false;

                // 接続の実施
                connectTimeout(sock.Socket, endPoint, timeout);

                // ホスト名解決
                try
                {
                    string[] hostname = Domain.GetHostName(ip, TimeoutGetHostname);
                    sock.RemoteHostName = hostname[0];
                }
                catch
                {
                    sock.RemoteHostName = ip.ToString();
                }

                sock.Socket.LingerState = new LingerOption(false, 0);
                try
                {
                    sock.Socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.DontLinger, true);
                }
                catch
                {
                }
                sock.Socket.NoDelay = true;

                sock.querySocketInformation();

                sock.Connected = true;
                sock.asyncMode = false;
                sock.SecureMode = false;
                sock.Fd = sock.Socket.Handle;

                sock.IsIPv6 = (ip.AddressFamily == AddressFamily.InterNetworkV6) ? true : false;

                sock.SetTimeout(TimeoutInfinite);

                return sock;
            }
            // 接続の実施
            static void connectTimeoutCallback(IAsyncResult r)
            {
            }
            static void connectTimeout(Socket s, IPEndPoint endPoint, int timeout)
            {
                IAsyncResult r = s.BeginConnect(endPoint, new AsyncCallback(connectTimeoutCallback), null);
                r.AsyncWaitHandle.WaitOne(timeout, false);
                if (r.IsCompleted == false)
                {
                    throw new TimeoutException();
                }
                s.EndConnect(r);
            }

            // 切断
            object unix_asyncmode_lock = new object();
            public void Disconnect()
            {
                try
                {
                    if (Env.IsUnix)
                    {
                        lock (unix_asyncmode_lock)
                        {
                            if (this.asyncMode)
                            {
                                this.asyncMode = false;

                                if (this.SockEvent != null)
                                {
                                    lock (this.SockEvent.UnixSockList)
                                    {
                                        this.SockEvent.UnixSockList.Remove(this);
                                    }

                                    SockEvent se = this.SockEvent;
                                    this.SockEvent = null;

                                    se.Set();
                                }
                            }
                        }
                    }
                }
                catch
                {
                }

                try
                {
                    this.disconnecting = true;

                    if (this.Type == SockType.Tcp && this.ListenMode)
                    {
                        this.cancelAccept = true;

                        try
                        {
                            Sock s = Sock.Connect((this.IsIPv6 ? "::1" : "127.0.0.1"), this.LocalPort);

                            s.Disconnect();
                        }
                        catch
                        {
                        }
                    }

                    lock (disconnectLockObj)
                    {
                        if (this.Type == SockType.Tcp)
                        {
                            if (this.Socket != null)
                            {
                                try
                                {
                                    this.Socket.LingerState = new LingerOption(false, 0);
                                }
                                catch
                                {
                                }

                                try
                                {
                                    this.Socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.DontLinger, true);
                                }
                                catch
                                {
                                }
                            }

                            lock (lockObj)
                            {
                                if (this.Socket == null)
                                {
                                    return;
                                }

                                Socket s = this.Socket;

                                if (this.Connected)
                                {
                                    s.Shutdown(SocketShutdown.Both);
                                }

                                s.Close();
                            }

                            lock (sslLockObj)
                            {
                                if (this.SecureMode)
                                {

                                }
                            }

                            this.Socket = null;
                            this.Type = SockType.Unknown;
                            this.asyncMode = this.Connected = this.ListenMode = this.SecureMode = false;
                        }
                        else if (this.Type == SockType.Udp)
                        {
                            lock (lockObj)
                            {
                                if (this.Socket == null)
                                {
                                    return;
                                }

                                Socket s = this.Socket;

                                s.Close();

                                this.Type = SockType.Unknown;
                                this.asyncMode = this.Connected = this.ListenMode = this.SecureMode = false;
                            }
                        }
                    }
                }
                catch
                {
                }
            }

            // タイムアウト時間の設定
            public void SetTimeout(int timeout)
            {
                try
                {
                    if (this.Type == SockType.Tcp)
                    {
                        if (timeout < 0)
                        {
                            timeout = TimeoutInfinite;
                        }

                        this.Socket!.SendTimeout = this.Socket.ReceiveTimeout = timeout;
                        this.timeOut = timeout;
                    }
                }
                catch
                {
                }
            }

            // タイムアウト時間の取得
            public int GetTimeout()
            {
                try
                {
                    if (this.Type != SockType.Tcp)
                    {
                        return TimeoutInfinite;
                    }

                    return this.timeOut;
                }
                catch
                {
                    return Timeout.Infinite;
                }
            }

            // ソケット情報の取得
            void querySocketInformation()
            {
                try
                {
                    lock (this.lockObj)
                    {
                        if (this.Type == SockType.Tcp)
                        {
                            // リモートホストの情報を取得
                            IPEndPoint ep1 = (IPEndPoint)this.Socket!.RemoteEndPoint!;

                            this.RemotePort = ep1.Port;
                            this.RemoteIP = ep1.Address;
                        }

                        // ローカルホストの情報を取得
                        IPEndPoint ep2 = (IPEndPoint)this.Socket!.LocalEndPoint!;

                        this.LocalPort = ep2.Port;
                        this.LocalIP = ep2.Address;
                    }
                }
                catch
                {
                }
            }

            public static bool SetKeepAlive(Socket socket, ulong time, ulong interval)
            {
                const int BytesPerLong = 4;
                const int BitsPerByte = 8;

                try
                {
                    var input = new[]
                    {
                    (time == 0 || interval == 0) ? 0UL : 1UL,
                    time,
                    interval
                };

                    byte[] inValue = new byte[3 * BytesPerLong];
                    for (int i = 0; i < input.Length; i++)
                    {
                        inValue[i * BytesPerLong + 3] = (byte)(input[i] >> ((BytesPerLong - 1) * BitsPerByte) & 0xff);
                        inValue[i * BytesPerLong + 2] = (byte)(input[i] >> ((BytesPerLong - 2) * BitsPerByte) & 0xff);
                        inValue[i * BytesPerLong + 1] = (byte)(input[i] >> ((BytesPerLong - 3) * BitsPerByte) & 0xff);
                        inValue[i * BytesPerLong + 0] = (byte)(input[i] >> ((BytesPerLong - 4) * BitsPerByte) & 0xff);
                    }

                    byte[] outValue = BitConverter.GetBytes(0);
                    //socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.KeepAlive, true);
                    socket.IOControl(IOControlCode.KeepAliveValues, inValue, outValue);
                }
                catch// (Exception ex)
                {
                    //Con.WriteLine(ex.ToString());
                    return false;
                }

                return true;
            }
        }


        // From WideControl 2007
        public class LegacyDomainUtil
        {
            string domainStr;
            string[] tokens;

            public string DomainString
            {
                get { return domainStr; }
            }

            public LegacyDomainUtil(string domainStr)
            {
                string str = domainStr.Trim().ToLowerInvariant();
                string str2 = "";

                foreach (char c in str)
                {
                    if (IsSafeCharDomain(c))
                    {
                        str2 += c;
                    }
                }

                this.domainStr = str2;

                StrToken tok = new StrToken(domainStr, ".");
                tokens = tok.Tokens;
            }

            // ドメインに使用できる文字かどうかチェック
            public static bool IsSafeCharDomain(char c)
            {
                if ('a' <= c && c <= 'z')
                {
                    return true;
                }
                else if ('A' <= c && c <= 'Z')
                {
                    return true;
                }
                else if (c == '_' || c == '-')
                {
                    return true;
                }
                else if ('0' <= c && c <= '9')
                {
                    return true;
                }
                else if (c == '.')
                {
                    return true;
                }
                return false;
            }

            // FQDN の最後の項目を取得
            public string LastUnit
            {
                get
                {
                    if (tokens.Length == 0)
                    {
                        return "";
                    }
                    return tokens[tokens.Length - 1];
                }
            }

            // GTLD かどうか取得
            public bool IsGtld
            {
                get
                {
                    string last = LastUnit;

                    if (last == "com" || last == "net" || last == "org" || last == "edu" ||
                        last == "gov" || last == "mil" || last == "int" || last == "info" ||
                        last == "biz" || last == "name" || last == "pro" || last == "museum" ||
                        last == "aero" || last == "coop" || last == "jobs" || last == "travel" ||
                        last == "mobi" || last == "cat" || last == "asia" || last == "post" ||
                        last == "tel")
                    {
                        return true;
                    }

                    return false;
                }
            }

            // 汎用 JP かどうか取得
            public bool IsHanyouJP
            {
                get
                {
                    string last = LastUnit;

                    if (last == "jp")
                    {
                        if (tokens.Length >= 3)
                        {
                            string s = tokens[tokens.Length - 2];

                            if (s == "co" || s == "or" || s == "ne" || s == "ac" || s == "ad" ||
                                s == "ed" || s == "go" || s == "gr" || s == "lg")
                            {
                                return false;
                            }

                            if (Str.IsStrInList(s, "HOKKAIDO", "FUKUSHIMA", "TOKYO", "YAMANASHI", "SHIGA",
                                "TOTTORI", "KAGAWA", "KUMAMOTO", "SENDAI", "OSAKA", "AOMORI", "IBARAKI",
                                "KANAGAWA", "NAGANO", "KYOTO", "SHIMANE", "EHIME", "OITA", "CHIBA", "KOBE",
                                "IWATE", "TOCHIGI", "NIIGATA", "GIFU", "OSAKA", "OKAYAMA", "KOCHI", "MIYAZAKI",
                                "YOKOHAMA", "HIROSHIMA", "MIYAGI", "GUNMA", "TOYAMA", "SHIZUOKA", "HYOGO",
                                "HIROSHIMA", "FUKUOKA", "KAGOSHIMA", "KAWASAKI",
                                "FUKUOKA", "AKITA", "SAITAMA", "ISHIKAWA", "AICHI", "NARA", "YAMAGUCHI",
                                "SAGA", "OKINAWA", "NAGOYA", "KITAKYUSHU", "YAMAGATA",
                                "CHIBA", "FUKUI", "MIE", "WAKAYAMA",
                                "TOKUSHIMA", "NAGASAKI", "SAPPORO", "KYOTO"))
                            {
                                return false;
                            }

                            return true;
                        }
                        else
                        {
                            return true;
                        }
                    }
                    else
                    {
                        return false;
                    }
                }
            }

            // 固有文字列を取得
            public string ProperString
            {
                get
                {
                    bool b = IsHanyouJP || IsGtld;

                    if (tokens.Length == 0)
                    {
                        return "";
                    }
                    else if (tokens.Length == 1)
                    {
                        return tokens[0].Trim();
                    }

                    if (tokens.Length == 2 || b)
                    {
                        return tokens[tokens.Length - 2];
                    }
                    else
                    {
                        return tokens[tokens.Length - 3];
                    }
                }
            }
        }

        // DNS
        public class Domain
        {
            public static readonly TimeSpan DnsCacheLifeTime = new TimeSpan(0, 1, 0, 0);
            static Cache<string, IPAddress[]> dnsACache = new Cache<string, IPAddress[]>(DnsCacheLifeTime, CacheType.UpdateExpiresWhenAccess);
            static Cache<IPAddress, string[]> dnsPTRCache = new Cache<IPAddress, string[]>(DnsCacheLifeTime, CacheType.UpdateExpiresWhenAccess);


            class GetHostNameData
            {
                public string[]? HostName;
                public IPAddress? IP;
                public bool NoCache;
            }

            // タイムアウト付き逆引き
            public static string[] GetHostName(IPAddress ip, int timeout)
            {
                return GetHostName(ip, timeout, false);
            }
            public static string[] GetHostName(IPAddress ip, int timeout, bool noCache)
            {
                GetHostNameData d = new GetHostNameData();
                d.HostName = null;
                d.IP = ip;
                d.NoCache = noCache;

                ThreadObj t = new ThreadObj(new ThreadProc(getHostNameThreadProc), d);
                t.WaitForEnd(timeout);

                lock (d)
                {
                    if (d.HostName != null)
                    {
                        return d.HostName;
                    }
                    else
                    {
                        if (noCache == false)
                        {
                            string[] ret = dnsPTRCache[ip];

                            if (ret != null)
                            {
                                return ret;
                            }
                        }
                        throw new TimeoutException();
                    }
                }
            }
            static void getHostNameThreadProc(object? param)
            {
                GetHostNameData d = (GetHostNameData)param!;

                string[]? hostname = Domain.GetHostName(d.IP!, d.NoCache);

                lock (d)
                {
                    d.HostName = hostname;
                }
            }

            // 逆引き
            public static string[]? GetHostName(IPAddress ip, bool noCache = false)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork && ip.GetAddressBytes()[0] == 127)
                {
                    return new string[] { "localhost" };
                }

                try
                {
                    IPHostEntry e = Dns.GetHostEntry(ip);

                    string[] ret = IPHostEntryToStringArray(e);

                    if (ret.Length == 0)
                    {
                        return null;
                    }

                    if (noCache == false)
                    {
                        dnsPTRCache.Add(ip, ret);
                    }

                    return ret;
                }
                catch
                {
                    if (noCache)
                    {
                        return null;
                    }

                    string[] ret = dnsPTRCache[ip];

                    if (ret == null)
                    {
                        return null;
                    }

                    return ret;
                }
            }

            // IPHostEntry から文字列リストを取得する
            public static string[] IPHostEntryToStringArray(IPHostEntry e)
            {
                List<string> o = new List<string>();

                o.Add(e.HostName);

                foreach (string s in e.Aliases)
                {
                    o.Add(s);
                }

                return Str.UniqueToken(o.ToArray());
            }

            // 正引き (IPv6 も)
            public static IPAddress[]? GetIP46(string hostName, bool noCache = false)
            {
                hostName = NormalizeHostName(hostName);

                if (IsIPAddress(hostName))
                {
                    return new IPAddress[1] { StrToIP(hostName)! };
                }

                if (Str.StrCmpi(hostName, "localhost"))
                {
                    return new IPAddress[] { new IPAddress(new byte[] { 127, 0, 0, 1 }) };
                }

                try
                {
                    IPAddress[] ret = Dns.GetHostAddresses(hostName);

                    if (ret.Length == 0)
                    {
                        return null;
                    }

                    if (noCache == false)
                    {
                        dnsACache.Add(hostName, ret);
                    }

                    return ret;
                }
                catch
                {
                    if (noCache)
                    {
                        throw;
                    }
                    IPAddress[] ret = dnsACache[hostName];

                    if (ret == null)
                    {
                        throw;
                    }

                    return ret;
                }
            }

            // 正引き
            public static IPAddress[]? GetIP(string hostName, bool noCache = false)
            {
                hostName = NormalizeHostName(hostName);

                if (IsIPAddress(hostName))
                {
                    return new IPAddress[1] { StrToIP(hostName)! };
                }

                if (Str.StrCmpi(hostName, "localhost"))
                {
                    return new IPAddress[] { new IPAddress(new byte[] { 127, 0, 0, 1 }) };
                }

                try
                {
                    IPAddress[] ret = GetIPv4OnlyFromIPAddressList(Dns.GetHostAddresses(hostName));

                    if (ret.Length == 0)
                    {
                        return null;
                    }

                    if (noCache == false)
                    {
                        dnsACache.Add(hostName, ret);
                    }

                    return ret;
                }
                catch
                {
                    if (noCache)
                    {
                        throw;
                    }
                    IPAddress[] ret = dnsACache[hostName];

                    if (ret == null)
                    {
                        throw;
                    }

                    return ret;
                }
            }

            // IP アドレスリストから IPv4 のもののみを抽出する
            public static IPAddress[] GetIPv4OnlyFromIPAddressList(IPAddress[] list)
            {
                List<IPAddress> o = new List<IPAddress>();

                foreach (IPAddress p in list)
                {
                    if (p.AddressFamily == AddressFamily.InterNetwork)
                    {
                        o.Add(p);
                    }
                }

                return o.ToArray();
            }

            // 文字列が IP アドレスかどうか取得する
            public static bool IsIPAddress(string str)
            {
                str = NormalizeHostName(str);

                try
                {
                    IPAddress.Parse(str);
                    return true;
                }
                catch
                {
                    return false;
                }
            }

            // 文字列を IP アドレスに変換する
            public static IPAddress? StrToIP(string str)
            {
                str = NormalizeHostName(str);

                if (IsIPAddress(str) == false)
                {
                    return null;
                }

                return IPAddress.Parse(str);
            }

            // ホスト名の正規化
            public static string NormalizeHostName(string hostName)
            {
                return hostName.Trim().ToLowerInvariant();
            }
        }
    }

    public class NonBlockSocket : IDisposable
    {
        public PalSocket Sock { get; }
        public bool IsStream { get; }
        public bool IsDisconnected { get => CancelWatcher.Canceled; }
        public CancellationToken CancelToken { get => CancelWatcher.CancelToken; }

        public AsyncAutoResetEvent EventSendReady { get; } = new AsyncAutoResetEvent();
        public AsyncAutoResetEvent EventRecvReady { get; } = new AsyncAutoResetEvent();
        public AsyncAutoResetEvent EventSendNow { get; } = new AsyncAutoResetEvent();

        CancelWatcher CancelWatcher;
        byte[] TmpRecvBuffer;

        public Fifo RecvTcpFifo { get; } = new Fifo();
        public Fifo SendTcpFifo { get; } = new Fifo();

        public Queue<Datagram> RecvUdpQueue { get; } = new Queue<Datagram>();
        public Queue<Datagram> SendUdpQueue { get; } = new Queue<Datagram>();

        int MaxRecvFifoSize;
        public int MaxRecvUdpQueueSize { get; }

        Task RecvLoopTask;
        Task SendLoopTask;

        AsyncBulkReceiver<Datagram, int>? UdpBulkReader = null;

        public NonBlockSocket(PalSocket s, CancellationToken cancel = default, int tmpBufferSize = 65536, int maxRecvBufferSize = 65536, int maxRecvUdpQueueSize = 4096)
        {
            if (tmpBufferSize < 65536) tmpBufferSize = 65536;
            TmpRecvBuffer = new byte[tmpBufferSize];
            MaxRecvFifoSize = maxRecvBufferSize;
            MaxRecvUdpQueueSize = maxRecvUdpQueueSize;

            EventSendReady.Set();
            EventRecvReady.Set();

            Sock = s;
            IsStream = (s.SocketType == SocketType.Stream);
            CancelWatcher = new CancelWatcher(cancel);

            if (IsStream)
            {
                RecvLoopTask = TCP_RecvLoop();
                SendLoopTask = TCP_SendLoop();
            }
            else
            {
                UdpBulkReader = new AsyncBulkReceiver<Datagram, int>(async (x, cancel2) =>
                {
                    PalSocketReceiveFromResult ret = await Sock.ReceiveFromAsync(TmpRecvBuffer);
                    return new ValueOrClosed<Datagram>(new Datagram(TmpRecvBuffer.AsSpan().Slice(0, ret.ReceivedBytes).ToArray(), (IPEndPoint)ret.RemoteEndPoint, (IPEndPoint?)ret.LocalEndPoint));
                });

                RecvLoopTask = UDP_RecvLoop();
                SendLoopTask = UDP_SendLoop();
            }
        }

        async Task TCP_RecvLoop()
        {
            try
            {
                await TaskUtil.DoAsyncWithTimeout(async (cancel) =>
                {
                    while (cancel.IsCancellationRequested == false)
                    {
                        int r = await Sock.ReceiveAsync(TmpRecvBuffer);
                        if (r <= 0) break;

                        while (cancel.IsCancellationRequested == false)
                        {
                            lock (RecvTcpFifo)
                            {
                                if (RecvTcpFifo.Size <= MaxRecvFifoSize)
                                {
                                    RecvTcpFifo.Write(TmpRecvBuffer, r);
                                    break;
                                }
                            }

                            await TaskUtil.WaitObjectsAsync(cancels: new CancellationToken[] { cancel },
                                timeout: 10);
                        }

                        EventRecvReady.Set();
                    }

                    return 0;
                },
                cancel: CancelWatcher.CancelToken
                );
            }
            finally
            {
                this.CancelWatcher.Cancel();
                EventSendReady.Set();
                EventRecvReady.Set();
            }
        }

        async Task UDP_RecvLoop()
        {
            try
            {
                await TaskUtil.DoAsyncWithTimeout(async (cancel) =>
                {
                    while (cancel.IsCancellationRequested == false)
                    {
                        Datagram[]? recvPackets = await UdpBulkReader!.RecvAsync(cancel);

                        if (recvPackets == null)
                        {
                            // Disconnected
                            throw new DisconnectedException();
                        }

                        bool fullQueue = false;
                        bool pktReceived = false;

                        lock (RecvUdpQueue)
                        {
                            foreach (Datagram p in recvPackets)
                            {
                                if (RecvUdpQueue.Count <= MaxRecvUdpQueueSize)
                                {
                                    RecvUdpQueue.Enqueue(p);
                                    pktReceived = true;
                                }
                                else
                                {
                                    fullQueue = true;
                                    break;
                                }
                            }
                        }

                        if (fullQueue)
                        {
                            await TaskUtil.WaitObjectsAsync(cancels: new CancellationToken[] { cancel },
                                timeout: 10);
                        }

                        if (pktReceived)
                        {
                            EventRecvReady.Set();
                        }
                    }

                    return 0;
                },
                cancel: CancelWatcher.CancelToken
                );
            }
            finally
            {
                this.CancelWatcher.Cancel();
                EventSendReady.Set();
                EventRecvReady.Set();
            }
        }

        async Task TCP_SendLoop()
        {
            try
            {
                await TaskUtil.DoAsyncWithTimeout(async (cancel) =>
                {
                    while (cancel.IsCancellationRequested == false)
                    {
                        byte[]? sendData = null;

                        while (cancel.IsCancellationRequested == false)
                        {
                            lock (SendTcpFifo)
                            {
                                sendData = SendTcpFifo.Read();
                            }

                            if (sendData != null && sendData.Length >= 1)
                            {
                                break;
                            }

                            await TaskUtil.WaitObjectsAsync(cancels: new CancellationToken[] { cancel },
                                events: new AsyncAutoResetEvent[] { EventSendNow });
                        }

                        int r = await Sock.SendAsync(sendData);
                        if (r <= 0) break;

                        EventSendReady.Set();
                    }

                    return 0;
                },
                cancel: CancelWatcher.CancelToken
                );
            }
            finally
            {
                this.CancelWatcher.Cancel();
                EventSendReady.Set();
                EventRecvReady.Set();
            }
        }

        async Task UDP_SendLoop()
        {
            try
            {
                await TaskUtil.DoAsyncWithTimeout(async (cancel) =>
                {
                    while (cancel.IsCancellationRequested == false)
                    {
                        Datagram? pkt = null;

                        while (cancel.IsCancellationRequested == false)
                        {
                            lock (SendUdpQueue)
                            {
                                if (SendUdpQueue.Count >= 1)
                                {
                                    pkt = SendUdpQueue.Dequeue();
                                }
                            }

                            if (pkt != null)
                            {
                                break;
                            }

                            await TaskUtil.WaitObjectsAsync(cancels: new CancellationToken[] { cancel },
                                events: new AsyncAutoResetEvent[] { EventSendNow });
                        }

                        int r = await Sock.SendToAsync(pkt!.Data._AsSegment(), pkt.RemoteIPEndPoint!);
                        if (r <= 0) break;

                        EventSendReady.Set();
                    }

                    return 0;
                },
                cancel: CancelWatcher.CancelToken
                );
            }
            catch (Exception ex)
            {
                Dbg.Where(ex.ToString());
            }
            finally
            {
                this.CancelWatcher.Cancel();
                EventSendReady.Set();
                EventRecvReady.Set();
            }
        }

        public Datagram? RecvFromNonBlock()
        {
            if (IsDisconnected) return null;
            lock (RecvUdpQueue)
            {
                if (RecvUdpQueue.TryDequeue(out Datagram? ret))
                {
                    return ret;
                }
            }
            return null;
        }

        public void Dispose() { this.Dispose(true); GC.SuppressFinalize(this); }
        Once DisposeFlag;
        protected virtual void Dispose(bool disposing)
        {
            if (!disposing || DisposeFlag.IsFirstCall() == false) return;
            CancelWatcher._DisposeSafe();
            Sock._DisposeSafe();
        }
    }
}
