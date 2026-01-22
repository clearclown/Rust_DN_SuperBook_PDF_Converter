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
// 
// Original Source Code:
// ARSoft.Tools.Net by Alexander Reinert
// 
// From: https://github.com/alexreinert/ARSoft.Tools.Net/tree/18b427f9f3cfacd4464152090db0515d2675d899
//
// ARSoft.Tools.Net - C# DNS client/server and SPF Library, Copyright (c) 2010-2017 Alexander Reinert (https://github.com/alexreinert/ARSoft.Tools.Net)
// 
// Copyright 2010..2017 Alexander Reinert
// 
// This file is part of the ARSoft.Tools.Net - C# DNS client/server and SPF Library (https://github.com/alexreinert/ARSoft.Tools.Net)
// 
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// 
//   http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

#if CORES_BASIC_SECURITY

#nullable disable

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
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Serialization;
using System.Net;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

namespace IPA.Cores.Basic.DnsLib;

internal class DnsCacheRecordList<T> : List<T>
{
    public DnsSecValidationResult ValidationResult { get; set; }
}

internal class DnsCache
{
    private class CacheKey
    {
        private readonly DomainName _name;
        private readonly RecordClass _recordClass;
        private readonly int _hashCode;
        private readonly RecordType _recordType;

        public CacheKey(DomainName name, RecordType recordType, RecordClass recordClass)
        {
            _name = name;
            _recordClass = recordClass;
            _recordType = recordType;

            _hashCode = name.GetHashCode() ^ (7 * (int)recordType) ^ (11 * (int)recordClass);
        }


        public override int GetHashCode()
        {
            return _hashCode;
        }

        public override bool Equals(object obj)
        {
            CacheKey other = obj as CacheKey;

            if (other == null)
                return false;

            return (_recordType == other._recordType) && (_recordClass == other._recordClass) && (_name.Equals(other._name));
        }

        public override string ToString()
        {
            return _name + " " + _recordClass.ToShortString() + " " + _recordType.ToShortString();
        }
    }

    private class CacheValue
    {
        public DateTime ExpireDateUtc { get; }
        public DnsCacheRecordList<DnsRecordBase> Records { get; }

        public CacheValue(DnsCacheRecordList<DnsRecordBase> records, int timeToLive)
        {
            Records = records;
            ExpireDateUtc = DateTime.UtcNow.AddSeconds(timeToLive);
        }
    }

    private readonly ConcurrentDictionary<CacheKey, CacheValue> _cache = new ConcurrentDictionary<CacheKey, CacheValue>();

    public void Add<TRecord>(DomainName name, RecordType recordType, RecordClass recordClass, IEnumerable<TRecord> records, DnsSecValidationResult validationResult, int timeToLive)
        where TRecord : DnsRecordBase
    {
        DnsCacheRecordList<DnsRecordBase> cacheValues = new DnsCacheRecordList<DnsRecordBase>();
        cacheValues.AddRange(records);
        cacheValues.ValidationResult = validationResult;

        Add(name, recordType, recordClass, cacheValues, timeToLive);
    }

    public void Add(DomainName name, RecordType recordType, RecordClass recordClass, DnsCacheRecordList<DnsRecordBase> records, int timeToLive)
    {
        CacheKey key = new CacheKey(name, recordType, recordClass);
        _cache.TryAdd(key, new CacheValue(records, timeToLive));
    }

    public bool TryGetRecords<TRecord>(DomainName name, RecordType recordType, RecordClass recordClass, out List<TRecord> records)
        where TRecord : DnsRecordBase
    {
        CacheKey key = new CacheKey(name, recordType, recordClass);
        DateTime utcNow = DateTime.UtcNow;

        CacheValue cacheValue;
        if (_cache.TryGetValue(key, out cacheValue))
        {
            if (cacheValue.ExpireDateUtc < utcNow)
            {
                _cache.TryRemove(key, out cacheValue);
                records = null;
                return false;
            }

            int ttl = (int)(cacheValue.ExpireDateUtc - utcNow).TotalSeconds;

            records = new List<TRecord>();

            records.AddRange(cacheValue
                .Records
                .OfType<TRecord>()
                .Select(x =>
                {
                    TRecord record = x.Clone<TRecord>();
                    record.TimeToLive = ttl;
                    return record;
                }));

            return true;
        }

        records = null;
        return false;
    }

    public bool TryGetRecords<TRecord>(DomainName name, RecordType recordType, RecordClass recordClass, out DnsCacheRecordList<TRecord> records)
        where TRecord : DnsRecordBase
    {
        CacheKey key = new CacheKey(name, recordType, recordClass);
        DateTime utcNow = DateTime.UtcNow;

        CacheValue cacheValue;
        if (_cache.TryGetValue(key, out cacheValue))
        {
            if (cacheValue.ExpireDateUtc < utcNow)
            {
                _cache.TryRemove(key, out cacheValue);
                records = null;
                return false;
            }

            int ttl = (int)(cacheValue.ExpireDateUtc - utcNow).TotalSeconds;

            records = new DnsCacheRecordList<TRecord>();

            records.AddRange(cacheValue
                .Records
                .OfType<TRecord>()
                .Select(x =>
                {
                    TRecord record = x.Clone<TRecord>();
                    record.TimeToLive = ttl;
                    return record;
                }));

            records.ValidationResult = cacheValue.Records.ValidationResult;

            return true;
        }

        records = null;
        return false;
    }

    public void RemoveExpiredItems()
    {
        DateTime utcNow = DateTime.UtcNow;

        foreach (var kvp in _cache)
        {
            CacheValue tmp;
            if (kvp.Value.ExpireDateUtc < utcNow)
                _cache.TryRemove(kvp.Key, out tmp);
        }
    }
}

internal class NameserverCache
{
    private class CacheValue
    {
        public DateTime ExpireDateUtc { get; }
        public IPAddress Address { get; }

        public CacheValue(int timeToLive, IPAddress address)
        {
            ExpireDateUtc = DateTime.UtcNow.AddSeconds(timeToLive);
            Address = address;
        }

        public override int GetHashCode()
        {
            return Address.GetHashCode();
        }

        public override bool Equals(object obj)
        {
            CacheValue second = obj as CacheValue;

            if (second == null)
                return false;

            return Address.Equals(second.Address);
        }
    }

    private readonly ConcurrentDictionary<DomainName, HashSet<CacheValue>> _cache = new ConcurrentDictionary<DomainName, HashSet<CacheValue>>();

    public void Add(DomainName zoneName, IPAddress address, int timeToLive)
    {
        HashSet<CacheValue> addresses;

        if (_cache.TryGetValue(zoneName, out addresses))
        {
            lock (addresses)
            {
                addresses.Add(new CacheValue(timeToLive, address));
            }
        }
        else
        {
            _cache.TryAdd(zoneName, new HashSet<CacheValue>() { new CacheValue(timeToLive, address) });
        }
    }

    public bool TryGetAddresses(DomainName zoneName, out List<IPAddress> addresses)
    {
        DateTime utcNow = DateTime.UtcNow;

        HashSet<CacheValue> cacheValues;
        if (_cache.TryGetValue(zoneName, out cacheValues))
        {
            addresses = new List<IPAddress>();
            bool needsCleanup = false;

            lock (cacheValues)
            {
                foreach (CacheValue cacheValue in cacheValues)
                {
                    if (cacheValue.ExpireDateUtc < utcNow)
                    {
                        needsCleanup = true;
                    }
                    else
                    {
                        addresses.Add(cacheValue.Address);
                    }
                }

                if (needsCleanup)
                {
                    cacheValues.RemoveWhere(x => x.ExpireDateUtc < utcNow);
                    if (cacheValues.Count == 0)
#pragma warning disable 0728
                        _cache.TryRemove(zoneName, out cacheValues);
#pragma warning restore 0728
                }
            }

            if (addresses.Count > 0)
                return true;
        }

        addresses = null;
        return false;
    }

    public void RemoveExpiredItems()
    {
        DateTime utcNow = DateTime.UtcNow;

        foreach (var kvp in _cache)
        {
            lock (kvp.Value)
            {
                HashSet<CacheValue> tmp;

                kvp.Value.RemoveWhere(x => x.ExpireDateUtc < utcNow);
                if (kvp.Value.Count == 0)
                    _cache.TryRemove(kvp.Key, out tmp);
            }
        }
    }
}

#endif
