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
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Linq;
using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using System.Threading.Tasks;

namespace IPA.Cores.Basic;

public enum CacheType
{
    UpdateExpiresWhenAccess = 0,
    DoNotUpdateExpiresWhenAccess = 1,
}

public class FastCache<TValue> : FastCache<string, TValue>
{
    public FastCache(int expireMsecs = Consts.Numbers.DefaultCacheExpiresMsecs, int gcIntervalMsecs = 0,
        CacheType cahceType = CacheType.DoNotUpdateExpiresWhenAccess, bool ignoreCase = true)
        : base(expireMsecs, gcIntervalMsecs, cahceType, ignoreCase ? StrComparer.SensitiveCaseComparer : StrComparer.IgnoreCaseComparer)
    {
    }
}

public class ValueWithExpires<TValue>
{
    public ValueWithExpires(long expires, TValue? value)
    {
        Expires = expires;
        Value = value;
    }

    public long Expires { get; set; }
    public TValue? Value { get; }
}

public class FastCache<TKey, TValue>
    where TKey : notnull
{
    ImmutableDictionary<TKey, ValueWithExpires<TValue>> Dict;

    public CacheType CacheType { get; }
    public long ExpireMsecs { get; }
    public long GcIntervalMsecs { get; }

    public IEnumerable<KeyValuePair<TKey, ValueWithExpires<TValue>>> GetItems() => Dict;

    public IEnumerable<TValue?> GetValues()
    {
        if (IsCacheDisabled) return new TValue?[0];
        return Dict.Values.Select(x => x.Value);
    }

    public bool IsCacheDisabled { get; } = false;

    public FastCache(int expireMsecs = Consts.Numbers.DefaultCacheExpiresMsecs, int gcIntervalMsecs = 0, CacheType cahceType = CacheType.DoNotUpdateExpiresWhenAccess,
        IEqualityComparer<TKey>? comparer = null)
    {
        this.CacheType = cahceType;

        this.ExpireMsecs = expireMsecs;
        this.GcIntervalMsecs = gcIntervalMsecs;

        if (this.ExpireMsecs < 0) this.ExpireMsecs = long.MaxValue;
        if (this.ExpireMsecs >= int.MaxValue) this.ExpireMsecs = long.MaxValue;

        if (this.ExpireMsecs == long.MaxValue)
        {
            this.GcIntervalMsecs = long.MaxValue;
        }
        else
        {
            if (this.GcIntervalMsecs <= 0)
            {
                this.GcIntervalMsecs = Math.Max(this.ExpireMsecs / 4, 1);
            }
            else
            {
                this.GcIntervalMsecs = Math.Max(Math.Min(this.GcIntervalMsecs, this.ExpireMsecs), Consts.Numbers.MinCacheGcIntervalsMsecs);
            }
        }

        Dict = ImmutableDictionary<TKey, ValueWithExpires<TValue>>.Empty;

        if (comparer != null)
        {
            Dict = Dict.WithComparers(comparer);
        }

        if (this.ExpireMsecs <= 0)
        {
            IsCacheDisabled = true;
        }
    }

    long LastTimeGc = 0;

    [MethodImpl(Inline)]
    void GcIfNecessary(long now = 0)
    {
        if (IsCacheDisabled) return;
        if (this.GcIntervalMsecs == long.MaxValue) return;

        if (now == 0) now = TickNow;

        if (LastTimeGc == 0 || now > LastTimeGc)
        {
            LastTimeGc = now;
            GcCore(now);
        }
    }

    void GcCore(long now)
    {
        if (IsCacheDisabled) return;
        var currentDict = this.Dict;

        foreach (var item in currentDict)
        {
            if (now > item.Value.Expires)
            {
                ImmutableInterlocked.TryRemove(ref this.Dict, item.Key, out _);
            }
        }
    }

    public void Delete(TKey key)
    {
        if (IsCacheDisabled) return;
        ImmutableInterlocked.TryRemove(ref this.Dict, key, out _);
    }

    public void Add(TKey key, TValue? value)
    {
        if (IsCacheDisabled) return;
        GcIfNecessary();

        ImmutableInterlocked.AddOrUpdate(ref this.Dict, key,
            addValueFactory: (key) =>
            {
                return new ValueWithExpires<TValue>(this.ExpireMsecs == long.MaxValue ? long.MaxValue : Time.Tick64 + this.ExpireMsecs, value);
            },
            updateValueFactory: (key, current) =>
            {
                return new ValueWithExpires<TValue>(this.ExpireMsecs == long.MaxValue ? long.MaxValue : Time.Tick64 + this.ExpireMsecs, value);
            });
    }

    public TValue? GetOrCreate(TKey key, Func<TKey, TValue?> createProc)
        => GetOrCreate(key, createProc, out _);
    public TValue? GetOrCreate(TKey key, Func<TKey, TValue?> createProc, out bool found)
    {
        found = false;

        if (IsCacheDisabled)
        {
            return createProc(key);
        }

        TValue? value = Get(key, out found);

        if (found == false)
        {
            value = createProc(key);

            Add(key, value);
        }

        return value;
    }

    public async Task<TValue?> GetOrCreateAsync(TKey key, Func<TKey, Task<TValue?>> createProc, RefBool? found = null)
    {
        found?.Set(false);
        if (IsCacheDisabled)
        {
            return await createProc(key);
        }

        TValue? value = Get(key, out bool found2);

        if (found2 == false)
        {
            value = await createProc(key);

            Add(key, value);
        }

        found?.Set(found2);

        return value;
    }

    public void Clear()
    {
        if (IsCacheDisabled) return;
        this.Dict = this.Dict.Clear();
    }

    public bool ContainsKey(TKey key)
    {
        Get(key, out bool ret);

        return ret;
    }

    public TValue? Get(TKey key, TValue? defaultValue = default)
        => Get(key, out _, defaultValue);

    public TValue? Get(TKey key, out bool found, TValue? defaultValue = default)
    {
        found = false;

        if (IsCacheDisabled)
        {
            return defaultValue;
        }

        long now = Time.Tick64;

        GcIfNecessary(now);

        var currentDict = Dict;

        if (currentDict.TryGetValue(key, out ValueWithExpires<TValue>? entry) == false)
        {
            // 存在しない
            return defaultValue;
        }

        if (now > entry.Expires)
        {
            // 有効期限切れ
            ImmutableInterlocked.TryRemove(ref this.Dict, key, out _);
            return defaultValue;
        }

        if (this.CacheType == CacheType.UpdateExpiresWhenAccess)
        {
            // アクセスのあるたび有効期限を延長
            if (this.ExpireMsecs != long.MaxValue)
            {
                entry.Expires = now + this.ExpireMsecs;
            }
        }

        found = true;

        return entry.Value;
    }

    public TValue? this[TKey key]
    {
        get => Get(key);
        set => Add(key, value);
    }
}


public class FastSingleCache<TValue>
{
    ValueWithExpires<TValue>? CurrentValue;

    public CacheType CacheType { get; }
    public long ExpireMsecs { get; }
    public long GcIntervalMsecs { get; }
    public bool IsCacheDisabled { get; } = false;

    public FastSingleCache(int expireMsecs = Consts.Numbers.DefaultCacheExpiresMsecs, int gcIntervalMsecs = 0, CacheType cahceType = CacheType.DoNotUpdateExpiresWhenAccess)
    {
        this.CacheType = cahceType;

        this.ExpireMsecs = expireMsecs;
        this.GcIntervalMsecs = gcIntervalMsecs;

        if (this.ExpireMsecs < 0) this.ExpireMsecs = long.MaxValue;
        if (this.ExpireMsecs >= int.MaxValue) this.ExpireMsecs = long.MaxValue;

        if (this.ExpireMsecs == long.MaxValue)
        {
            this.GcIntervalMsecs = long.MaxValue;
        }
        else
        {
            if (this.GcIntervalMsecs <= 0)
            {
                this.GcIntervalMsecs = Math.Max(this.ExpireMsecs / 4, 1);
            }
            else
            {
                this.GcIntervalMsecs = Math.Max(Math.Min(this.GcIntervalMsecs, this.ExpireMsecs), Consts.Numbers.MinCacheGcIntervalsMsecs);
            }
        }

        if (this.ExpireMsecs <= 0)
        {
            IsCacheDisabled = true;
        }
    }

    long LastTimeGc = 0;

    [MethodImpl(Inline)]
    void GcIfNecessary(long now = 0)
    {
        if (IsCacheDisabled) return;
        if (this.GcIntervalMsecs == long.MaxValue) return;

        if (now == 0)
        {
            now = TickNow;
        }

        if (LastTimeGc == 0 || now > LastTimeGc)
        {
            LastTimeGc = now;
            GcCore(now);
        }
    }

    void GcCore(long now)
    {
        if (IsCacheDisabled) return;
        var current = CurrentValue;
        if (current != null && now > current.Expires)
        {
            CurrentValue = null;
        }
    }

    public void Clear() => Delete();

    public void Delete()
    {
        if (IsCacheDisabled) return;
        this.CurrentValue = null;
    }

    public void Add(TValue? value)
    {
        if (IsCacheDisabled) return;
        this.CurrentValue = new ValueWithExpires<TValue>(this.ExpireMsecs == long.MaxValue ? long.MaxValue : Time.Tick64 + this.ExpireMsecs, value);
    }

    public TValue? GetOrCreate(Func<TValue?> createProc)
        => GetOrCreate(createProc, out _);
    public TValue? GetOrCreate(Func<TValue?> createProc, out bool found)
    {
        found = false;

        if (IsCacheDisabled)
        {
            return createProc();
        }

        TValue? value = Get(out found);

        if (found)
        {
            return value;
        }

        value = createProc();

        Add(value);

        return value;
    }

    public async Task<TValue?> GetOrCreateAsync(Func<Task<TValue?>> createProc, RefBool? found = null)
    {
        found?.Set(false);

        if (IsCacheDisabled)
        {
            return await createProc();
        }

        TValue? value = Get(out bool found2);

        if (found2)
        {
            found?.Set(true);
            return value;
        }

        value = await createProc();

        Add(value);

        return value;
    }

    public bool Contains()
    {
        Get(out bool ret);

        return ret;
    }

    public TValue? Get(TValue? defaultValue = default)
        => Get(out _, defaultValue);
    public TValue? Get(out bool found, TValue? defaultValue = default)
    {
        found = false;

        if (IsCacheDisabled)
        {
            return defaultValue;
        }

        var current = this.CurrentValue;

        long now = Time.Tick64;

        if (current == null)
        {
            // 存在しない
            return defaultValue;
        }

        if (now > current.Expires)
        {
            // 有効期限切れ
            this.CurrentValue = null;
            return defaultValue;
        }

        if (this.CacheType == CacheType.UpdateExpiresWhenAccess)
        {
            // アクセスのあるたび有効期限を延長
            if (this.ExpireMsecs != long.MaxValue)
            {
                current.Expires = now + this.ExpireMsecs;
            }
        }

        found = true;

        return current.Value;
    }

    public static implicit operator TValue?(FastSingleCache<TValue> cache) => cache.Get();
}



public class Cache<TKey, TValue>
    where TKey : notnull
{
    class Entry
    {
        public DateTime CreatedDateTime { get; }
        public DateTime UpdatedDateTime { get; private set; }
        public DateTime LastAccessedDateTime { get; private set; }
        public TKey Key { get; }

        TValue _value;
        public TValue Value
        {
            get
            {
                LastAccessedDateTime = Time.NowHighResDateTimeUtc;
                return this._value;
            }
            set
            {
                this._value = value;
                UpdatedDateTime = Time.NowHighResDateTimeUtc;
                LastAccessedDateTime = Time.NowHighResDateTimeUtc;
            }
        }

        public Entry(TKey key, TValue value)
        {
            this.Key = key;
            this._value = value;
            LastAccessedDateTime = UpdatedDateTime = CreatedDateTime = Time.NowHighResDateTimeUtc;
        }

        public override int GetHashCode() => Key.GetHashCode();

        public override string ToString() => Key.ToString() + "," + _value?.ToString() ?? "";
    }

    public static readonly TimeSpan DefaultExpireSpan = new TimeSpan(0, 5, 0);
    public const CacheType DefaultCacheType = CacheType.UpdateExpiresWhenAccess;
    public TimeSpan ExpireSpan { get; private set; }
    public CacheType Type { get; private set; }
    Dictionary<TKey, Entry> list;
    CriticalSection LockObj;

    public Cache(TimeSpan expireSpan = default, CacheType type = DefaultCacheType, IEqualityComparer<TKey>? comparer = null)
    {
        if (expireSpan == default)
            expireSpan = DefaultExpireSpan;

        this.ExpireSpan = expireSpan;
        this.Type = type;

        if (comparer == null)
        {
            list = new Dictionary<TKey, Entry>();
        }
        else
        {
            list = new Dictionary<TKey, Entry>(comparer);
        }

        LockObj = new CriticalSection<Cache<TKey, TValue>>();
    }

    public void Add(TKey key, TValue value)
    {
        lock (LockObj)
        {
            Entry e;

            DeleteExpired();

            if (list.ContainsKey(key) == false)
            {
                e = new Entry(key, value);

                list.Add(e.Key, e);

                DeleteExpired();
            }
            else
            {
                e = list[key];
                e.Value = value;
            }
        }
    }

    public TValue Get(TKey key) => this[key];

    public void Delete(TKey key)
    {
        lock (LockObj)
        {
            if (list.ContainsKey(key))
            {
                list.Remove(key);
            }
        }
    }

    public TValue this[TKey key]
    {
        get
        {
            lock (LockObj)
            {
                DeleteExpired();

                if (list.ContainsKey(key) == false)
                {
                    return default(TValue)!;
                }

                return list[key].Value;
            }
        }

        set
        {
            Add(key, value);
        }
    }

    public TValue GetOrCreate(TKey key, Func<TKey, TValue> createProc)
    {
        lock (LockObj)
        {
            DeleteExpired();

            if (list.ContainsKey(key) == false)
            {
                TValue newValue = createProc(key);

                Add(key, newValue);

                return newValue;
            }

            return list[key].Value;
        }
    }

    long LastDeleted = 0;

    void DeleteExpired()
    {
        bool doDelete = false;
        long now = Tick64.Value;
        long deleteInterval = ExpireSpan.Milliseconds / 10;

        lock (LockObj)
        {
            if (LastDeleted == 0 || now > (LastDeleted + deleteInterval))
            {
                LastDeleted = now;
                doDelete = true;
            }
        }

        if (doDelete == false)
        {
            return;
        }

        lock (LockObj)
        {
            List<Entry> o = new List<Entry>();
            DateTime expire = Time.NowHighResDateTimeUtc - this.ExpireSpan;

            foreach (Entry e in list.Values)
            {
                if (this.Type == CacheType.UpdateExpiresWhenAccess)
                {
                    if (e.LastAccessedDateTime < expire)
                    {
                        o.Add(e);
                    }
                }
                else
                {
                    if (e.UpdatedDateTime < expire)
                    {
                        o.Add(e);
                    }
                }
            }

            foreach (Entry e in o)
            {
                list.Remove(e.Key);
            }
        }
    }
}
