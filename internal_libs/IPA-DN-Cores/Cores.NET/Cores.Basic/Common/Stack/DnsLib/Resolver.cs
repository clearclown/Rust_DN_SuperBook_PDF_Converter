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
using System.Net.Sockets;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

namespace IPA.Cores.Basic.DnsLib;

/// <summary>
///   Extension methods for DNS resolvers
/// </summary>
public static class DnsResolverExtensions
{
    /// <summary>
    ///   Queries a dns resolver for IP addresses of a host.
    /// </summary>
    /// <param name="resolver"> The resolver instance, that should be used for queries </param>
    /// <param name="name"> Host name, that should be queried </param>
    /// <returns> A list of matching host addresses </returns>
    public static List<IPAddress> ResolveHost(this IDnsResolver resolver, DomainName name)
    {
        List<IPAddress> result = new List<IPAddress>();

        List<AaaaRecord> aaaaRecords = resolver.Resolve<AaaaRecord>(name, RecordType.Aaaa);
        if (aaaaRecords != null)
            result.AddRange(aaaaRecords.Select(x => x.Address));

        List<ARecord> aRecords = resolver.Resolve<ARecord>(name);
        if (aRecords != null)
            result.AddRange(aRecords.Select(x => x.Address));

        return result;
    }

    /// <summary>
    ///   Queries a dns resolver for IP addresses of a host.
    /// </summary>
    /// <param name="resolver"> The resolver instance, that should be used for queries </param>
    /// <param name="name"> Host name, that should be queried </param>
    /// <returns> A list of matching host addresses </returns>
    public static List<IPAddress> ResolveHost(this IDnsResolver resolver, string name)
    {
        return resolver.ResolveHost(DomainName.Parse(name));
    }

    /// <summary>
    ///   Queries a dns resolver for IP addresses of a host as an asynchronous operation.
    /// </summary>
    /// <param name="resolver"> The resolver instance, that should be used for queries </param>
    /// <param name="name"> Host name, that should be queried </param>
    /// <param name="token"> The token to monitor cancellation requests </param>
    /// <returns> A list of matching host addresses </returns>
    public static async Task<List<IPAddress>> ResolveHostAsync(this IDnsResolver resolver, DomainName name, CancellationToken token = default(CancellationToken))
    {
        List<IPAddress> result = new List<IPAddress>();

        List<AaaaRecord> aaaaRecords = await resolver.ResolveAsync<AaaaRecord>(name, RecordType.Aaaa, token: token);
        if (aaaaRecords != null)
            result.AddRange(aaaaRecords.Select(x => x.Address));

        List<ARecord> aRecords = await resolver.ResolveAsync<ARecord>(name, token: token);
        if (aRecords != null)
            result.AddRange(aRecords.Select(x => x.Address));

        return result;
    }

    /// <summary>
    ///   Queries a dns resolver for IP addresses of a host as an asynchronous operation.
    /// </summary>
    /// <param name="resolver"> The resolver instance, that should be used for queries </param>
    /// <param name="name"> Host name, that should be queried </param>
    /// <param name="token"> The token to monitor cancellation requests </param>
    /// <returns> A list of matching host addresses </returns>
    public static Task<List<IPAddress>> ResolveHostAsync(this IDnsResolver resolver, string name, CancellationToken token = default(CancellationToken))
    {
        return resolver.ResolveHostAsync(DomainName.Parse(name), token);
    }

    /// <summary>
    ///   Queries a dns resolver for reverse name of an IP address.
    /// </summary>
    /// <param name="resolver"> The resolver instance, that should be used for queries </param>
    /// <param name="address"> The address, that should be queried </param>
    /// <returns> The reverse name of the IP address </returns>
    public static DomainName ResolvePtr(this IDnsResolver resolver, IPAddress address)
    {
        List<PtrRecord> ptrRecords = resolver.Resolve<PtrRecord>(address.GetReverseLookupDomain(), RecordType.Ptr);
        return ptrRecords.Select(x => x.PointerDomainName).FirstOrDefault();
    }

    /// <summary>
    ///   Queries a dns resolver for reverse name of an IP address as an asynchronous operation.
    /// </summary>
    /// <param name="resolver"> The resolver instance, that should be used for queries </param>
    /// <param name="address"> The address, that should be queried </param>
    /// <param name="token"> The token to monitor cancellation requests </param>
    /// <returns> The reverse name of the IP address </returns>
    public static async Task<DomainName> ResolvePtrAsync(this IDnsResolver resolver, IPAddress address, CancellationToken token = default(CancellationToken))
    {
        List<PtrRecord> ptrRecords = await resolver.ResolveAsync<PtrRecord>(address.GetReverseLookupDomain(), RecordType.Ptr, token: token);
        return ptrRecords.Select(x => x.PointerDomainName).FirstOrDefault();
    }

    /// <summary>
    ///   Queries a dns resolver for specified records.
    /// </summary>
    /// <typeparam name="T"> Type of records, that should be returned </typeparam>
    /// <param name="resolver"> The resolver instance, that should be used for queries </param>
    /// <param name="name"> Domain, that should be queried </param>
    /// <param name="recordType"> Type the should be queried </param>
    /// <param name="recordClass"> Class the should be queried </param>
    /// <returns> A list of matching <see cref="DnsRecordBase">records</see> </returns>
    public static List<T> Resolve<T>(this IDnsResolver resolver, string name, RecordType recordType = RecordType.A, RecordClass recordClass = RecordClass.INet)
        where T : DnsRecordBase
    {
        return resolver.Resolve<T>(DomainName.Parse(name), recordType, recordClass);
    }

    /// <summary>
    ///   Queries a dns resolver for specified records as an asynchronous operation.
    /// </summary>
    /// <typeparam name="T"> Type of records, that should be returned </typeparam>
    /// <param name="resolver"> The resolver instance, that should be used for queries </param>
    /// <param name="name"> Domain, that should be queried </param>
    /// <param name="recordType"> Type the should be queried </param>
    /// <param name="recordClass"> Class the should be queried </param>
    /// <param name="token"> The token to monitor cancellation requests </param>
    /// <returns> A list of matching <see cref="DnsRecordBase">records</see> </returns>
    public static Task<List<T>> ResolveAsync<T>(this IDnsResolver resolver, string name, RecordType recordType = RecordType.A, RecordClass recordClass = RecordClass.INet, CancellationToken token = default(CancellationToken))
        where T : DnsRecordBase
    {
        return resolver.ResolveAsync<T>(DomainName.Parse(name), recordType, recordClass, token);
    }
}

/// <summary>
///   <para>Recursive resolver</para>
///   <para>
///     Defined in
///     <see cref="!:http://tools.ietf.org/html/rfc1035">RFC 1035</see>
///   </para>
/// </summary>
public class DnsSecRecursiveDnsResolver : IDnsSecResolver, IInternalDnsSecResolver<DnsSecRecursiveDnsResolver.State>
{
    private class State
    {
        public int QueryCount;
    }

    private DnsCache _cache = new DnsCache();
    private readonly DnsSecValidator<State> _validator;
    private NameserverCache _nameserverCache = new NameserverCache();

    private readonly IResolverHintStore _resolverHintStore;

    /// <summary>
    ///   Provides a new instance with custom root server hints
    /// </summary>
    /// <param name="resolverHintStore"> The resolver hint store with the IP addresses of the root server and root DnsKey hints</param>
    public DnsSecRecursiveDnsResolver(IResolverHintStore resolverHintStore = null)
    {
        _resolverHintStore = resolverHintStore ?? new StaticResolverHintStore();
        _validator = new DnsSecValidator<State>(this, _resolverHintStore);
        IsResponseValidationEnabled = true;
        QueryTimeout = 2000;
        MaximumReferalCount = 20;
    }

    /// <summary>
    ///   Gets or sets a value indicating how much referals for a single query could be performed
    /// </summary>
    public int MaximumReferalCount { get; set; }

    /// <summary>
    ///   Milliseconds after which a query times out.
    /// </summary>
    public int QueryTimeout { get; set; }

    /// <summary>
    ///   Gets or set a value indicating whether the response is validated as described in
    ///   <see
    ///     cref="!:http://tools.ietf.org/id/draft-vixie-dnsext-dns0x20-00.txt">
    ///     draft-vixie-dnsext-dns0x20-00
    ///   </see>
    /// </summary>
    public bool IsResponseValidationEnabled { get; set; }

    /// <summary>
    ///   Gets or set a value indicating whether the query labels are used for additional validation as described in
    ///   <see
    ///     cref="!:http://tools.ietf.org/id/draft-vixie-dnsext-dns0x20-00.txt">
    ///     draft-vixie-dnsext-dns0x20-00
    ///   </see>
    /// </summary>
    // ReSharper disable once InconsistentNaming
    public bool Is0x20ValidationEnabled { get; set; }

    /// <summary>
    ///   Clears the record cache
    /// </summary>
    public void ClearCache()
    {
        _cache = new DnsCache();
        _nameserverCache = new NameserverCache();
    }

    /// <summary>
    ///   Resolves specified records.
    /// </summary>
    /// <typeparam name="T"> Type of records, that should be returned </typeparam>
    /// <param name="name"> Domain, that should be queried </param>
    /// <param name="recordType"> Type the should be queried </param>
    /// <param name="recordClass"> Class the should be queried </param>
    /// <returns> A list of matching <see cref="DnsRecordBase">records</see> </returns>
    public List<T> Resolve<T>(DomainName name, RecordType recordType = RecordType.A, RecordClass recordClass = RecordClass.INet)
        where T : DnsRecordBase
    {
        var res = ResolveAsync<T>(name, recordType, recordClass);
        res.Wait();
        return res.Result;
    }

    /// <summary>
    ///   Resolves specified records as an asynchronous operation.
    /// </summary>
    /// <typeparam name="T"> Type of records, that should be returned </typeparam>
    /// <param name="name"> Domain, that should be queried </param>
    /// <param name="recordType"> Type the should be queried </param>
    /// <param name="recordClass"> Class the should be queried </param>
    /// <param name="token"> The token to monitor cancellation requests </param>
    /// <returns> A list of matching <see cref="DnsRecordBase">records</see> </returns>
    public async Task<List<T>> ResolveAsync<T>(DomainName name, RecordType recordType = RecordType.A, RecordClass recordClass = RecordClass.INet, CancellationToken token = default(CancellationToken))
        where T : DnsRecordBase
    {
        var res = await ResolveSecureAsync<T>(name, recordType, recordClass, token);
        return res.Records;
    }

    /// <summary>
    ///   Resolves specified records.
    /// </summary>
    /// <typeparam name="T"> Type of records, that should be returned </typeparam>
    /// <param name="name"> Domain, that should be queried </param>
    /// <param name="recordType"> Type the should be queried </param>
    /// <param name="recordClass"> Class the should be queried </param>
    /// <returns> A list of matching <see cref="DnsRecordBase">records</see> </returns>
    public DnsSecResult<T> ResolveSecure<T>(DomainName name, RecordType recordType = RecordType.A, RecordClass recordClass = RecordClass.INet)
        where T : DnsRecordBase
    {
        var res = ResolveSecureAsync<T>(name, recordType, recordClass);
        res.Wait();
        return res.Result;
    }

    /// <summary>
    ///   Resolves specified records as an asynchronous operation.
    /// </summary>
    /// <typeparam name="T"> Type of records, that should be returned </typeparam>
    /// <param name="name"> Domain, that should be queried </param>
    /// <param name="recordType"> Type the should be queried </param>
    /// <param name="recordClass"> Class the should be queried </param>
    /// <param name="token"> The token to monitor cancellation requests </param>
    /// <returns> A list of matching <see cref="DnsRecordBase">records</see> </returns>
    public Task<DnsSecResult<T>> ResolveSecureAsync<T>(DomainName name, RecordType recordType = RecordType.A, RecordClass recordClass = RecordClass.INet, CancellationToken token = default(CancellationToken))
        where T : DnsRecordBase
    {
        if (name == null)
            throw new ArgumentNullException(nameof(name), "Name must be provided");

        return ResolveAsyncInternal<T>(name, recordType, recordClass, new State(), token);
    }

    private async Task<DnsMessage> ResolveMessageAsync(DomainName name, RecordType recordType, RecordClass recordClass, State state, CancellationToken token)
    {
        for (; state.QueryCount <= MaximumReferalCount; state.QueryCount++)
        {
            DnsMessage msg = await new DnsClient(GetBestNameservers(recordType == RecordType.Ds ? name.GetParentName() : name), QueryTimeout)
            {
                IsResponseValidationEnabled = IsResponseValidationEnabled,
                Is0x20ValidationEnabled = Is0x20ValidationEnabled
            }.ResolveAsync(name, recordType, recordClass, new DnsQueryOptions()
            {
                IsRecursionDesired = false,
                IsEDnsEnabled = true,
                IsDnsSecOk = true,
                IsCheckingDisabled = true
            }, token);

            if ((msg != null) && ((msg.ReturnCode == ReturnCode.NoError) || (msg.ReturnCode == ReturnCode.NxDomain)))
            {
                if (msg.IsAuthoritiveAnswer)
                    return msg;

                List<NsRecord> referalRecords = msg.AuthorityRecords
                    .Where(x =>
                        (x.RecordType == RecordType.Ns)
                        && (name.Equals(x.Name) || name.IsSubDomainOf(x.Name)))
                    .OfType<NsRecord>()
                    .ToList();

                if (referalRecords.Count > 0)
                {
                    if (referalRecords.GroupBy(x => x.Name).Count() == 1)
                    {
                        var newServers = referalRecords.Join(msg.AdditionalRecords.OfType<AddressRecordBase>(), x => x.NameServer, x => x.Name, (x, y) => new { y.Address, TimeToLive = Math.Min(x.TimeToLive, y.TimeToLive) }).ToList();

                        if (newServers.Count > 0)
                        {
                            DomainName zone = referalRecords.First().Name;

                            foreach (var newServer in newServers)
                            {
                                _nameserverCache.Add(zone, newServer.Address, newServer.TimeToLive);
                            }

                            continue;
                        }
                        else
                        {
                            NsRecord firstReferal = referalRecords.First();

                            var newLookedUpServers = await ResolveHostWithTtlAsync(firstReferal.NameServer, state, token);

                            foreach (var newServer in newLookedUpServers)
                            {
                                _nameserverCache.Add(firstReferal.Name, newServer.Item1, Math.Min(firstReferal.TimeToLive, newServer.Item2));
                            }

                            if (newLookedUpServers.Count > 0)
                                continue;
                        }
                    }
                }

                // Response of best known server is not authoritive and has no referrals --> No chance to get a result
                throw new Exception("Could not resolve " + name);
            }
        }

        // query limit reached without authoritive answer
        throw new Exception("Could not resolve " + name);
    }

    private async Task<DnsSecResult<T>> ResolveAsyncInternal<T>(DomainName name, RecordType recordType, RecordClass recordClass, State state, CancellationToken token)
        where T : DnsRecordBase
    {
        DnsCacheRecordList<T> cachedResults;
        if (_cache.TryGetRecords(name, recordType, recordClass, out cachedResults))
        {
            return new DnsSecResult<T>(cachedResults, cachedResults.ValidationResult);
        }

        DnsCacheRecordList<CNameRecord> cachedCNames;
        if (_cache.TryGetRecords(name, RecordType.CName, recordClass, out cachedCNames))
        {
            var cNameResult = await ResolveAsyncInternal<T>(cachedCNames.First().CanonicalName, recordType, recordClass, state, token);
            return new DnsSecResult<T>(cNameResult.Records, cachedCNames.ValidationResult == cNameResult.ValidationResult ? cachedCNames.ValidationResult : DnsSecValidationResult.Unsigned);
        }

        DnsMessage msg = await ResolveMessageAsync(name, recordType, recordClass, state, token);

        // check for cname
        List<DnsRecordBase> cNameRecords = msg.AnswerRecords.Where(x => (x.RecordType == RecordType.CName) && (x.RecordClass == recordClass) && x.Name.Equals(name)).ToList();
        if (cNameRecords.Count > 0)
        {
            DnsSecValidationResult cNameValidationResult = await _validator.ValidateAsync(name, RecordType.CName, recordClass, msg, cNameRecords, state, token);
            if ((cNameValidationResult == DnsSecValidationResult.Bogus) || (cNameValidationResult == DnsSecValidationResult.Indeterminate))
                throw new DnsSecValidationException("CNAME record could not be validated");

            _cache.Add(name, RecordType.CName, recordClass, cNameRecords, cNameValidationResult, cNameRecords.Min(x => x.TimeToLive));

            DomainName canonicalName = ((CNameRecord)cNameRecords.First()).CanonicalName;

            List<DnsRecordBase> matchingAdditionalRecords = msg.AnswerRecords.Where(x => (x.RecordType == recordType) && (x.RecordClass == recordClass) && x.Name.Equals(canonicalName)).ToList();
            if (matchingAdditionalRecords.Count > 0)
            {
                DnsSecValidationResult matchingValidationResult = await _validator.ValidateAsync(canonicalName, recordType, recordClass, msg, matchingAdditionalRecords, state, token);
                if ((matchingValidationResult == DnsSecValidationResult.Bogus) || (matchingValidationResult == DnsSecValidationResult.Indeterminate))
                    throw new DnsSecValidationException("CNAME matching records could not be validated");

                DnsSecValidationResult validationResult = cNameValidationResult == matchingValidationResult ? cNameValidationResult : DnsSecValidationResult.Unsigned;
                _cache.Add(canonicalName, recordType, recordClass, matchingAdditionalRecords, validationResult, matchingAdditionalRecords.Min(x => x.TimeToLive));

                return new DnsSecResult<T>(matchingAdditionalRecords.OfType<T>().ToList(), validationResult);
            }

            var cNameResults = await ResolveAsyncInternal<T>(canonicalName, recordType, recordClass, state, token);
            return new DnsSecResult<T>(cNameResults.Records, cNameValidationResult == cNameResults.ValidationResult ? cNameValidationResult : DnsSecValidationResult.Unsigned);
        }

        // check for "normal" answer
        List<DnsRecordBase> answerRecords = msg.AnswerRecords.Where(x => (x.RecordType == recordType) && (x.RecordClass == recordClass) && x.Name.Equals(name)).ToList();
        if (answerRecords.Count > 0)
        {
            DnsSecValidationResult validationResult = await _validator.ValidateAsync(name, recordType, recordClass, msg, answerRecords, state, token);
            if ((validationResult == DnsSecValidationResult.Bogus) || (validationResult == DnsSecValidationResult.Indeterminate))
                throw new DnsSecValidationException("Response records could not be validated");

            _cache.Add(name, recordType, recordClass, answerRecords, validationResult, answerRecords.Min(x => x.TimeToLive));
            return new DnsSecResult<T>(answerRecords.OfType<T>().ToList(), validationResult);
        }

        // check for negative answer
        SoaRecord soaRecord = msg.AuthorityRecords
            .Where(x =>
                (x.RecordType == RecordType.Soa)
                && (name.Equals(x.Name) || name.IsSubDomainOf(x.Name)))
            .OfType<SoaRecord>()
            .FirstOrDefault();

        if (soaRecord != null)
        {
            DnsSecValidationResult validationResult = await _validator.ValidateAsync(name, recordType, recordClass, msg, answerRecords, state, token);
            if ((validationResult == DnsSecValidationResult.Bogus) || (validationResult == DnsSecValidationResult.Indeterminate))
                throw new DnsSecValidationException("Negative answer could not be validated");

            _cache.Add(name, recordType, recordClass, new List<DnsRecordBase>(), validationResult, soaRecord.NegativeCachingTTL);
            return new DnsSecResult<T>(new List<T>(), validationResult);
        }

        // authoritive response does not contain answer
        throw new Exception("Could not resolve " + name);
    }


    private async Task<List<Tuple<IPAddress, int>>> ResolveHostWithTtlAsync(DomainName name, State state, CancellationToken token)
    {
        List<Tuple<IPAddress, int>> result = new List<Tuple<IPAddress, int>>();

        var aaaaRecords = await ResolveAsyncInternal<AaaaRecord>(name, RecordType.Aaaa, RecordClass.INet, state, token);
        result.AddRange(aaaaRecords.Records.Select(x => new Tuple<IPAddress, int>(x.Address, x.TimeToLive)));

        var aRecords = await ResolveAsyncInternal<ARecord>(name, RecordType.A, RecordClass.INet, state, token);
        result.AddRange(aRecords.Records.Select(x => new Tuple<IPAddress, int>(x.Address, x.TimeToLive)));

        return result;
    }

    private IEnumerable<IPAddress> GetBestNameservers(DomainName name)
    {
        Random rnd = new Random();

        while (name.LabelCount > 0)
        {
            List<IPAddress> cachedAddresses;
            if (_nameserverCache.TryGetAddresses(name, out cachedAddresses))
            {
                return cachedAddresses.OrderBy(x => x.AddressFamily == AddressFamily.InterNetworkV6 ? 0 : 1).ThenBy(x => rnd.Next());
            }

            name = name.GetParentName();
        }

        return _resolverHintStore.RootServers.OrderBy(x => x.AddressFamily == AddressFamily.InterNetworkV6 ? 0 : 1).ThenBy(x => rnd.Next());
    }

    Task<DnsMessage> IInternalDnsSecResolver<State>.ResolveMessageAsync(DomainName name, RecordType recordType, RecordClass recordClass, State state, CancellationToken token)
    {
        return ResolveMessageAsync(name, recordType, recordClass, state, token);
    }

    Task<DnsSecResult<TRecord>> IInternalDnsSecResolver<State>.ResolveSecureAsync<TRecord>(DomainName name, RecordType recordType, RecordClass recordClass, State state, CancellationToken token)
    {
        return ResolveAsyncInternal<TRecord>(name, recordType, recordClass, state, token);
    }
}

/// <summary>
///   Extension methods for DNSSEC resolvers
/// </summary>
public static class DnsSecResolverExtensions
{
    /// <summary>
    ///   Queries a dns resolver for specified records.
    /// </summary>
    /// <typeparam name="T"> Type of records, that should be returned </typeparam>
    /// <param name="resolver"> The resolver instance, that should be used for queries </param>
    /// <param name="name"> Domain, that should be queried </param>
    /// <param name="recordType"> Type the should be queried </param>
    /// <param name="recordClass"> Class the should be queried </param>
    /// <returns> The validating result and a list of matching <see cref="DnsRecordBase">records</see> </returns>
    public static DnsSecResult<T> ResolveSecure<T>(this IDnsSecResolver resolver, string name, RecordType recordType = RecordType.A, RecordClass recordClass = RecordClass.INet)
        where T : DnsRecordBase
    {
        return resolver.ResolveSecure<T>(DomainName.Parse(name), recordType, recordClass);
    }

    /// <summary>
    ///   Queries a dns resolver for specified records as an asynchronous operation.
    /// </summary>
    /// <typeparam name="T"> Type of records, that should be returned </typeparam>
    /// <param name="resolver"> The resolver instance, that should be used for queries </param>
    /// <param name="name"> Domain, that should be queried </param>
    /// <param name="recordType"> Type the should be queried </param>
    /// <param name="recordClass"> Class the should be queried </param>
    /// <param name="token"> The token to monitor cancellation requests </param>
    /// <returns> A list of matching <see cref="DnsRecordBase">records</see> </returns>
    public static Task<DnsSecResult<T>> ResolveSecureAsync<T>(this IDnsSecResolver resolver, string name, RecordType recordType = RecordType.A, RecordClass recordClass = RecordClass.INet, CancellationToken token = default(CancellationToken))
        where T : DnsRecordBase
    {
        return resolver.ResolveSecureAsync<T>(DomainName.Parse(name), recordType, recordClass, token);
    }
}

/// <summary>
///   The response of a secure DNS resolver
/// </summary>
public class DnsSecResult<T>
    where T : DnsRecordBase
{
    /// <summary>
    ///   The result of the validation process
    /// </summary>
    public DnsSecValidationResult ValidationResult { get; private set; }

    /// <summary>
    ///   The records representing the response
    /// </summary>
    public List<T> Records { get; private set; }

    internal DnsSecResult(List<T> records, DnsSecValidationResult validationResult)
    {
        Records = records;
        ValidationResult = validationResult;
    }
}

/// <summary>
///   The result of a DNSSEC validation
/// </summary>
public enum DnsSecValidationResult
{
    /// <summary>
    ///   It is indeterminate whether the validation is secure, insecure or bogus
    /// </summary>
    Indeterminate,

    /// <summary>
    ///   The response is signed and fully validated
    /// </summary>
    Signed,

    /// <summary>
    ///   The response is unsigned with a validated OptOut
    /// </summary>
    Unsigned,

    /// <summary>
    ///   The response is bogus
    /// </summary>
    Bogus,
}

/// <summary>
///   <para>Stub resolver</para>
///   <para>
///     Defined in
///     <see cref="!:http://tools.ietf.org/html/rfc1035">RFC 1035</see>
///   </para>
/// </summary>
public class DnsStubResolver : IDnsResolver
{
    private readonly DnsClient _dnsClient;
    private DnsCache _cache = new DnsCache();

    /// <summary>
    ///   Provides a new instance using the local configured DNS servers
    /// </summary>
    public DnsStubResolver()
        : this(DnsClient.Default) { }

    /// <summary>
    ///   Provides a new instance using a custom <see cref="DnsClient">DNS client</see>
    /// </summary>
    /// <param name="dnsClient"> The <see cref="DnsClient">DNS client</see> to use </param>
    public DnsStubResolver(DnsClient dnsClient)
    {
        _dnsClient = dnsClient;
    }

    /// <summary>
    ///   Provides a new instance using a list of custom DNS servers and a default query timeout of 10 seconds
    /// </summary>
    /// <param name="servers"> The list of servers to use </param>
    public DnsStubResolver(IEnumerable<IPAddress> servers)
        : this(new DnsClient(servers, 10000)) { }

    /// <summary>
    ///   Provides a new instance using a list of custom DNS servers and a custom query timeout
    /// </summary>
    /// <param name="servers"> The list of servers to use </param>
    /// <param name="queryTimeout"> The query timeout in milliseconds </param>
    public DnsStubResolver(IEnumerable<IPAddress> servers, int queryTimeout)
        : this(new DnsClient(servers, queryTimeout)) { }

    /// <summary>
    ///   Queries a the upstream DNS server(s) for specified records.
    /// </summary>
    /// <typeparam name="T"> Type of records, that should be returned </typeparam>
    /// <param name="name"> Domain, that should be queried </param>
    /// <param name="recordType"> Type the should be queried </param>
    /// <param name="recordClass"> Class the should be queried </param>
    /// <returns> A list of matching <see cref="DnsRecordBase">records</see> </returns>
    public List<T> Resolve<T>(DomainName name, RecordType recordType = RecordType.A, RecordClass recordClass = RecordClass.INet)
        where T : DnsRecordBase
    {
        if (name == null)
            throw new ArgumentNullException(nameof(name), "Name must be provided");

        List<T> records;
        if (_cache.TryGetRecords(name, recordType, recordClass, out records))
        {
            return records;
        }

        DnsMessage msg = _dnsClient.Resolve(name, recordType, recordClass);

        if ((msg == null) || ((msg.ReturnCode != ReturnCode.NoError) && (msg.ReturnCode != ReturnCode.NxDomain)))
        {
            throw new Exception("DNS request failed");
        }

        CNameRecord cName = msg.AnswerRecords.Where(x => (x.RecordType == RecordType.CName) && (x.RecordClass == recordClass) && x.Name.Equals(name)).OfType<CNameRecord>().FirstOrDefault();

        if (cName != null)
        {
            records = msg.AnswerRecords.Where(x => x.Name.Equals(cName.CanonicalName)).OfType<T>().ToList();
            if (records.Count > 0)
            {
                _cache.Add(name, recordType, recordClass, records, DnsSecValidationResult.Indeterminate, records.Min(x => x.TimeToLive));
                return records;
            }

            records = Resolve<T>(cName.CanonicalName, recordType, recordClass);

            if (records.Count > 0)
                _cache.Add(name, recordType, recordClass, records, DnsSecValidationResult.Indeterminate, records.Min(x => x.TimeToLive));

            return records;
        }

        records = msg.AnswerRecords.Where(x => x.Name.Equals(name)).OfType<T>().ToList();

        if (records.Count > 0)
            _cache.Add(name, recordType, recordClass, records, DnsSecValidationResult.Indeterminate, records.Min(x => x.TimeToLive));

        return records;
    }

    /// <summary>
    ///   Queries a the upstream DNS server(s) for specified records as an asynchronous operation.
    /// </summary>
    /// <typeparam name="T"> Type of records, that should be returned </typeparam>
    /// <param name="name"> Domain, that should be queried </param>
    /// <param name="recordType"> Type the should be queried </param>
    /// <param name="recordClass"> Class the should be queried </param>
    /// <param name="token"> The token to monitor cancellation requests </param>
    /// <returns> A list of matching <see cref="DnsRecordBase">records</see> </returns>
    public async Task<List<T>> ResolveAsync<T>(DomainName name, RecordType recordType = RecordType.A, RecordClass recordClass = RecordClass.INet, CancellationToken token = default(CancellationToken))
        where T : DnsRecordBase
    {
        if (name == null)
            throw new ArgumentNullException(nameof(name), "Name must be provided");

        List<T> records;
        if (_cache.TryGetRecords(name, recordType, recordClass, out records))
        {
            return records;
        }

        DnsMessage msg = await _dnsClient.ResolveAsync(name, recordType, recordClass, null, token);

        if ((msg == null) || ((msg.ReturnCode != ReturnCode.NoError) && (msg.ReturnCode != ReturnCode.NxDomain)))
        {
            throw new Exception("DNS request failed");
        }

        CNameRecord cName = msg.AnswerRecords.Where(x => (x.RecordType == RecordType.CName) && (x.RecordClass == recordClass) && x.Name.Equals(name)).OfType<CNameRecord>().FirstOrDefault();

        if (cName != null)
        {
            records = msg.AnswerRecords.Where(x => (x.RecordType == recordType) && (x.RecordClass == recordClass) && x.Name.Equals(cName.CanonicalName)).OfType<T>().ToList();
            if (records.Count > 0)
            {
                _cache.Add(name, recordType, recordClass, records, DnsSecValidationResult.Indeterminate, Math.Min(cName.TimeToLive, records.Min(x => x.TimeToLive)));
                return records;
            }

            records = await ResolveAsync<T>(cName.CanonicalName, recordType, recordClass, token);

            if (records.Count > 0)
                _cache.Add(name, recordType, recordClass, records, DnsSecValidationResult.Indeterminate, Math.Min(cName.TimeToLive, records.Min(x => x.TimeToLive)));

            return records;
        }

        records = msg.AnswerRecords.Where(x => x.Name.Equals(name)).OfType<T>().ToList();

        if (records.Count > 0)
            _cache.Add(name, recordType, recordClass, records, DnsSecValidationResult.Indeterminate, records.Min(x => x.TimeToLive));

        return records;
    }

    /// <summary>
    ///   Clears the record cache
    /// </summary>
    public void ClearCache()
    {
        _cache = new DnsCache();
    }
}

/// <summary>
///   Interface of a DNS resolver
/// </summary>
public interface IDnsResolver
{
    /// <summary>
    ///   Queries a dns resolver for specified records.
    /// </summary>
    /// <typeparam name="T"> Type of records, that should be returned </typeparam>
    /// <param name="name"> Domain, that should be queried </param>
    /// <param name="recordType"> Type the should be queried </param>
    /// <param name="recordClass"> Class the should be queried </param>
    /// <returns> A list of matching <see cref="DnsRecordBase">records</see> </returns>
    List<T> Resolve<T>(DomainName name, RecordType recordType = RecordType.A, RecordClass recordClass = RecordClass.INet)
        where T : DnsRecordBase;

    /// <summary>
    ///   Queries a dns resolver for specified records as an asynchronous operation.
    /// </summary>
    /// <typeparam name="T"> Type of records, that should be returned </typeparam>
    /// <param name="name"> Domain, that should be queried </param>
    /// <param name="recordType"> Type the should be queried </param>
    /// <param name="recordClass"> Class the should be queried </param>
    /// <param name="token"> The token to monitor cancellation requests </param>
    /// <returns> A list of matching <see cref="DnsRecordBase">records</see> </returns>
    Task<List<T>> ResolveAsync<T>(DomainName name, RecordType recordType = RecordType.A, RecordClass recordClass = RecordClass.INet, CancellationToken token = default(CancellationToken))
        where T : DnsRecordBase;

    /// <summary>
    ///   Clears the record cache
    /// </summary>
    void ClearCache();
}

/// <summary>
///   Interface of a DNSSEC validating resolver
/// </summary>
public interface IDnsSecResolver : IDnsResolver
{
    /// <summary>
    ///   Queries a dns resolver for specified records.
    /// </summary>
    /// <typeparam name="T"> Type of records, that should be returned </typeparam>
    /// <param name="name"> Domain, that should be queried </param>
    /// <param name="recordType"> Type the should be queried </param>
    /// <param name="recordClass"> Class the should be queried </param>
    /// <returns> The validating result and a list of matching <see cref="DnsRecordBase">records</see> </returns>
    DnsSecResult<T> ResolveSecure<T>(DomainName name, RecordType recordType = RecordType.A, RecordClass recordClass = RecordClass.INet)
        where T : DnsRecordBase;

    /// <summary>
    ///   Queries a dns resolver for specified records as an asynchronous operation.
    /// </summary>
    /// <typeparam name="T"> Type of records, that should be returned </typeparam>
    /// <param name="name"> Domain, that should be queried </param>
    /// <param name="recordType"> Type the should be queried </param>
    /// <param name="recordClass"> Class the should be queried </param>
    /// <param name="token"> The token to monitor cancellation requests </param>
    /// <returns> A list of matching <see cref="DnsRecordBase">records</see> </returns>
    Task<DnsSecResult<T>> ResolveSecureAsync<T>(DomainName name, RecordType recordType = RecordType.A, RecordClass recordClass = RecordClass.INet, CancellationToken token = default(CancellationToken))
        where T : DnsRecordBase;
}

/// <summary>
///   Interface to provide hints used by resolvers
/// </summary>
public interface IResolverHintStore
{
    /// <summary>
    ///   List of hints to the root servers
    /// </summary>
    List<IPAddress> RootServers { get; }

    /// <summary>
    ///   List of DsRecords of the root zone
    /// </summary>
    List<DsRecord> RootKeys { get; }
}

/// <summary>
///   <para>Recursive resolver</para>
///   <para>
///     Defined in
///     <see cref="!:http://tools.ietf.org/html/rfc1035">RFC 1035</see>
///   </para>
/// </summary>
public class RecursiveDnsResolver : IDnsResolver
{
    private class State
    {
        public int QueryCount;
    }

    private DnsCache _cache = new DnsCache();
    private NameserverCache _nameserverCache = new NameserverCache();

    private readonly IResolverHintStore _resolverHintStore;

    /// <summary>
    ///   Provides a new instance with custom root server hints
    /// </summary>
    /// <param name="resolverHintStore"> The resolver hint store with the IP addresses of the root server hints</param>
    public RecursiveDnsResolver(IResolverHintStore resolverHintStore = null)
    {
        _resolverHintStore = resolverHintStore ?? new StaticResolverHintStore();
        IsResponseValidationEnabled = true;
        QueryTimeout = 2000;
        MaximumReferalCount = 20;
    }

    /// <summary>
    ///   Gets or sets a value indicating how much referals for a single query could be performed
    /// </summary>
    public int MaximumReferalCount { get; set; }

    /// <summary>
    ///   Milliseconds after which a query times out.
    /// </summary>
    public int QueryTimeout { get; set; }

    /// <summary>
    ///   Gets or set a value indicating whether the response is validated as described in
    ///   <see
    ///     cref="!:http://tools.ietf.org/id/draft-vixie-dnsext-dns0x20-00.txt">
    ///     draft-vixie-dnsext-dns0x20-00
    ///   </see>
    /// </summary>
    public bool IsResponseValidationEnabled { get; set; }

    /// <summary>
    ///   Gets or set a value indicating whether the query labels are used for additional validation as described in
    ///   <see
    ///     cref="!:http://tools.ietf.org/id/draft-vixie-dnsext-dns0x20-00.txt">
    ///     draft-vixie-dnsext-dns0x20-00
    ///   </see>
    /// </summary>
    // ReSharper disable once InconsistentNaming
    public bool Is0x20ValidationEnabled { get; set; }

    /// <summary>
    ///   Clears the record cache
    /// </summary>
    public void ClearCache()
    {
        _cache = new DnsCache();
        _nameserverCache = new NameserverCache();
    }

    /// <summary>
    ///   Resolves specified records.
    /// </summary>
    /// <typeparam name="T"> Type of records, that should be returned </typeparam>
    /// <param name="name"> Domain, that should be queried </param>
    /// <param name="recordType"> Type the should be queried </param>
    /// <param name="recordClass"> Class the should be queried </param>
    /// <returns> A list of matching <see cref="DnsRecordBase">records</see> </returns>
    public List<T> Resolve<T>(DomainName name, RecordType recordType = RecordType.A, RecordClass recordClass = RecordClass.INet)
        where T : DnsRecordBase
    {
        var res = ResolveAsync<T>(name, recordType, recordClass);
        res.Wait();
        return res.Result;
    }

    /// <summary>
    ///   Resolves specified records as an asynchronous operation.
    /// </summary>
    /// <typeparam name="T"> Type of records, that should be returned </typeparam>
    /// <param name="name"> Domain, that should be queried </param>
    /// <param name="recordType"> Type the should be queried </param>
    /// <param name="recordClass"> Class the should be queried </param>
    /// <param name="token"> The token to monitor cancellation requests </param>
    /// <returns> A list of matching <see cref="DnsRecordBase">records</see> </returns>
    public Task<List<T>> ResolveAsync<T>(DomainName name, RecordType recordType = RecordType.A, RecordClass recordClass = RecordClass.INet, CancellationToken token = default(CancellationToken))
        where T : DnsRecordBase
    {
        if (name == null)
            throw new ArgumentNullException(nameof(name), "Name must be provided");

        return ResolveAsyncInternal<T>(name, recordType, recordClass, new State(), token);
    }

    private async Task<DnsMessage> ResolveMessageAsync(DomainName name, RecordType recordType, RecordClass recordClass, State state, CancellationToken token)
    {
        for (; state.QueryCount <= MaximumReferalCount; state.QueryCount++)
        {
            DnsMessage msg = await new DnsClient(GetBestNameservers(recordType == RecordType.Ds ? name.GetParentName() : name), QueryTimeout)
            {
                IsResponseValidationEnabled = IsResponseValidationEnabled,
                Is0x20ValidationEnabled = Is0x20ValidationEnabled
            }.ResolveAsync(name, recordType, recordClass, new DnsQueryOptions()
            {
                IsRecursionDesired = false,
                IsEDnsEnabled = true
            }, token);

            if ((msg != null) && ((msg.ReturnCode == ReturnCode.NoError) || (msg.ReturnCode == ReturnCode.NxDomain)))
            {
                if (msg.IsAuthoritiveAnswer)
                    return msg;

                List<NsRecord> referalRecords = msg.AuthorityRecords
                    .Where(x =>
                        (x.RecordType == RecordType.Ns)
                        && (name.Equals(x.Name) || name.IsSubDomainOf(x.Name)))
                    .OfType<NsRecord>()
                    .ToList();

                if (referalRecords.Count > 0)
                {
                    if (referalRecords.GroupBy(x => x.Name).Count() == 1)
                    {
                        var newServers = referalRecords.Join(msg.AdditionalRecords.OfType<AddressRecordBase>(), x => x.NameServer, x => x.Name, (x, y) => new { y.Address, TimeToLive = Math.Min(x.TimeToLive, y.TimeToLive) }).ToList();

                        if (newServers.Count > 0)
                        {
                            DomainName zone = referalRecords.First().Name;

                            foreach (var newServer in newServers)
                            {
                                _nameserverCache.Add(zone, newServer.Address, newServer.TimeToLive);
                            }

                            continue;
                        }
                        else
                        {
                            NsRecord firstReferal = referalRecords.First();

                            var newLookedUpServers = await ResolveHostWithTtlAsync(firstReferal.NameServer, state, token);

                            foreach (var newServer in newLookedUpServers)
                            {
                                _nameserverCache.Add(firstReferal.Name, newServer.Item1, Math.Min(firstReferal.TimeToLive, newServer.Item2));
                            }

                            if (newLookedUpServers.Count > 0)
                                continue;
                        }
                    }
                }

                // Response of best known server is not authoritive and has no referrals --> No chance to get a result
                throw new Exception("Could not resolve " + name);
            }
        }

        // query limit reached without authoritive answer
        throw new Exception("Could not resolve " + name);
    }

    private async Task<List<T>> ResolveAsyncInternal<T>(DomainName name, RecordType recordType, RecordClass recordClass, State state, CancellationToken token)
        where T : DnsRecordBase
    {
        List<T> cachedResults;
        if (_cache.TryGetRecords(name, recordType, recordClass, out cachedResults))
        {
            return cachedResults;
        }

        List<CNameRecord> cachedCNames;
        if (_cache.TryGetRecords(name, RecordType.CName, recordClass, out cachedCNames))
        {
            return await ResolveAsyncInternal<T>(cachedCNames.First().CanonicalName, recordType, recordClass, state, token);
        }

        DnsMessage msg = await ResolveMessageAsync(name, recordType, recordClass, state, token);

        // check for cname
        List<DnsRecordBase> cNameRecords = msg.AnswerRecords.Where(x => (x.RecordType == RecordType.CName) && (x.RecordClass == recordClass) && x.Name.Equals(name)).ToList();
        if (cNameRecords.Count > 0)
        {
            _cache.Add(name, RecordType.CName, recordClass, cNameRecords, DnsSecValidationResult.Indeterminate, cNameRecords.Min(x => x.TimeToLive));

            DomainName canonicalName = ((CNameRecord)cNameRecords.First()).CanonicalName;

            List<DnsRecordBase> matchingAdditionalRecords = msg.AnswerRecords.Where(x => (x.RecordType == recordType) && (x.RecordClass == recordClass) && x.Name.Equals(canonicalName)).ToList();
            if (matchingAdditionalRecords.Count > 0)
            {
                _cache.Add(canonicalName, recordType, recordClass, matchingAdditionalRecords, DnsSecValidationResult.Indeterminate, matchingAdditionalRecords.Min(x => x.TimeToLive));
                return matchingAdditionalRecords.OfType<T>().ToList();
            }

            return await ResolveAsyncInternal<T>(canonicalName, recordType, recordClass, state, token);
        }

        // check for "normal" answer
        List<DnsRecordBase> answerRecords = msg.AnswerRecords.Where(x => (x.RecordType == recordType) && (x.RecordClass == recordClass) && x.Name.Equals(name)).ToList();
        if (answerRecords.Count > 0)
        {
            _cache.Add(name, recordType, recordClass, answerRecords, DnsSecValidationResult.Indeterminate, answerRecords.Min(x => x.TimeToLive));
            return answerRecords.OfType<T>().ToList();
        }

        // check for negative answer
        SoaRecord soaRecord = msg.AuthorityRecords
            .Where(x =>
                (x.RecordType == RecordType.Soa)
                && (name.Equals(x.Name) || name.IsSubDomainOf(x.Name)))
            .OfType<SoaRecord>()
            .FirstOrDefault();

        if (soaRecord != null)
        {
            _cache.Add(name, recordType, recordClass, new List<DnsRecordBase>(), DnsSecValidationResult.Indeterminate, soaRecord.NegativeCachingTTL);
            return new List<T>();
        }

        // authoritive response does not contain answer
        throw new Exception("Could not resolve " + name);
    }

    private async Task<List<Tuple<IPAddress, int>>> ResolveHostWithTtlAsync(DomainName name, State state, CancellationToken token)
    {
        List<Tuple<IPAddress, int>> result = new List<Tuple<IPAddress, int>>();

        var aaaaRecords = await ResolveAsyncInternal<AaaaRecord>(name, RecordType.Aaaa, RecordClass.INet, state, token);
        result.AddRange(aaaaRecords.Select(x => new Tuple<IPAddress, int>(x.Address, x.TimeToLive)));

        var aRecords = await ResolveAsyncInternal<ARecord>(name, RecordType.A, RecordClass.INet, state, token);
        result.AddRange(aRecords.Select(x => new Tuple<IPAddress, int>(x.Address, x.TimeToLive)));

        return result;
    }

    private IEnumerable<IPAddress> GetBestNameservers(DomainName name)
    {
        Random rnd = new Random();

        while (name.LabelCount > 0)
        {
            List<IPAddress> cachedAddresses;
            if (_nameserverCache.TryGetAddresses(name, out cachedAddresses))
            {
                return cachedAddresses.OrderBy(x => x.AddressFamily == AddressFamily.InterNetworkV6 ? 0 : 1).ThenBy(x => rnd.Next());
            }

            name = name.GetParentName();
        }

        return _resolverHintStore.RootServers.OrderBy(x => x.AddressFamily == AddressFamily.InterNetworkV6 ? 0 : 1).ThenBy(x => rnd.Next());
    }
}

/// <summary>
///   <para>Self validating security aware stub resolver</para>
///   <para>
///     Defined in
///     <see cref="!:http://tools.ietf.org/html/rfc4033">RFC 4033</see>,
///     <see cref="!:http://tools.ietf.org/html/rfc4034">RFC 4034</see>
///     and <see cref="!:http://tools.ietf.org/html/rfc4035">RFC 4035</see>
///   </para>
/// </summary>
public class SelfValidatingInternalDnsSecStubResolver : IDnsSecResolver, IInternalDnsSecResolver<object>
{
    private readonly DnsClient _dnsClient;
    private DnsCache _cache;
    private readonly DnsSecValidator<object> _validator;

    /// <summary>
    ///   Provides a new instance using a custom <see cref="DnsClient">DNS client</see>
    /// </summary>
    /// <param name="dnsClient"> The <see cref="DnsClient">DNS client</see> to use </param>
    /// <param name="resolverHintStore"> The resolver hint store with the root DnsKey hints</param>
    public SelfValidatingInternalDnsSecStubResolver(DnsClient dnsClient = null, IResolverHintStore resolverHintStore = null)
    {
        _dnsClient = dnsClient ?? DnsClient.Default;
        _cache = new DnsCache();
        _validator = new DnsSecValidator<object>(this, resolverHintStore ?? new StaticResolverHintStore());
    }

    /// <summary>
    ///   Provides a new instance using a list of custom DNS servers and a default query timeout of 10 seconds
    /// </summary>
    /// <param name="servers"> The list of servers to use </param>
    public SelfValidatingInternalDnsSecStubResolver(IEnumerable<IPAddress> servers)
        : this(servers, 10000) { }

    /// <summary>
    ///   Provides a new instance using a list of custom DNS servers and a custom query timeout
    /// </summary>
    /// <param name="servers"> The list of servers to use </param>
    /// <param name="queryTimeout"> The query timeout in milliseconds </param>
    public SelfValidatingInternalDnsSecStubResolver(IEnumerable<IPAddress> servers, int queryTimeout)
        : this(new DnsClient(servers, queryTimeout)) { }

    /// <summary>
    ///   Resolves specified records.
    /// </summary>
    /// <typeparam name="T"> Type of records, that should be returned </typeparam>
    /// <param name="name"> Domain, that should be queried </param>
    /// <param name="recordType"> Type the should be queried </param>
    /// <param name="recordClass"> Class the should be queried </param>
    /// <returns> A list of matching <see cref="DnsRecordBase">records</see> </returns>
    public List<T> Resolve<T>(DomainName name, RecordType recordType = RecordType.A, RecordClass recordClass = RecordClass.INet)
        where T : DnsRecordBase
    {
        var res = ResolveAsync<T>(name, recordType, recordClass);
        res.Wait();
        return res.Result;
    }

    /// <summary>
    ///   Resolves specified records as an asynchronous operation.
    /// </summary>
    /// <typeparam name="T"> Type of records, that should be returned </typeparam>
    /// <param name="name"> Domain, that should be queried </param>
    /// <param name="recordType"> Type the should be queried </param>
    /// <param name="recordClass"> Class the should be queried </param>
    /// <param name="token"> The token to monitor cancellation requests </param>
    /// <returns> A list of matching <see cref="DnsRecordBase">records</see> </returns>
    public async Task<List<T>> ResolveAsync<T>(DomainName name, RecordType recordType = RecordType.A, RecordClass recordClass = RecordClass.INet, CancellationToken token = default(CancellationToken))
        where T : DnsRecordBase
    {
        if (name == null)
            throw new ArgumentNullException(nameof(name), "Name must be provided");

        var res = await ResolveSecureAsync<T>(name, recordType, recordClass, token);

        return res.Records;
    }

    /// <summary>
    ///   Resolves specified records.
    /// </summary>
    /// <typeparam name="T"> Type of records, that should be returned </typeparam>
    /// <param name="name"> Domain, that should be queried </param>
    /// <param name="recordType"> Type the should be queried </param>
    /// <param name="recordClass"> Class the should be queried </param>
    /// <returns> A list of matching <see cref="DnsRecordBase">records</see> </returns>
    public DnsSecResult<T> ResolveSecure<T>(DomainName name, RecordType recordType = RecordType.A, RecordClass recordClass = RecordClass.INet)
        where T : DnsRecordBase
    {
        var res = ResolveSecureAsync<T>(name, recordType, recordClass);
        res.Wait();
        return res.Result;
    }

    /// <summary>
    ///   Resolves specified records as an asynchronous operation.
    /// </summary>
    /// <typeparam name="T"> Type of records, that should be returned </typeparam>
    /// <param name="name"> Domain, that should be queried </param>
    /// <param name="recordType"> Type the should be queried </param>
    /// <param name="recordClass"> Class the should be queried </param>
    /// <param name="token"> The token to monitor cancellation requests </param>
    /// <returns> A list of matching <see cref="DnsRecordBase">records</see> </returns>
    public async Task<DnsSecResult<T>> ResolveSecureAsync<T>(DomainName name, RecordType recordType = RecordType.A, RecordClass recordClass = RecordClass.INet, CancellationToken token = default(CancellationToken))
        where T : DnsRecordBase
    {
        if (name == null)
            throw new ArgumentNullException(nameof(name), "Name must be provided");

        DnsCacheRecordList<T> cacheResult;
        if (_cache.TryGetRecords(name, recordType, recordClass, out cacheResult))
        {
            return new DnsSecResult<T>(cacheResult, cacheResult.ValidationResult);
        }

        DnsMessage msg = await _dnsClient.ResolveAsync(name, recordType, recordClass, new DnsQueryOptions()
        {
            IsEDnsEnabled = true,
            IsDnsSecOk = true,
            IsCheckingDisabled = true,
            IsRecursionDesired = true
        }, token);

        if ((msg == null) || ((msg.ReturnCode != ReturnCode.NoError) && (msg.ReturnCode != ReturnCode.NxDomain)))
        {
            throw new Exception("DNS request failed");
        }

        DnsSecValidationResult validationResult;

        CNameRecord cName = msg.AnswerRecords.Where(x => (x.RecordType == RecordType.CName) && (x.RecordClass == recordClass) && x.Name.Equals(name)).OfType<CNameRecord>().FirstOrDefault();

        if (cName != null)
        {
            DnsSecValidationResult cNameValidationResult = await _validator.ValidateAsync(name, RecordType.CName, recordClass, msg, new List<CNameRecord>() { cName }, null, token);
            if ((cNameValidationResult == DnsSecValidationResult.Bogus) || (cNameValidationResult == DnsSecValidationResult.Indeterminate))
                throw new DnsSecValidationException("CNAME record could not be validated");

            var records = msg.AnswerRecords.Where(x => (x.RecordType == recordType) && (x.RecordClass == recordClass) && x.Name.Equals(cName.CanonicalName)).OfType<T>().ToList();
            if (records.Count > 0)
            {
                DnsSecValidationResult recordsValidationResult = await _validator.ValidateAsync(cName.CanonicalName, recordType, recordClass, msg, records, null, token);
                if ((recordsValidationResult == DnsSecValidationResult.Bogus) || (recordsValidationResult == DnsSecValidationResult.Indeterminate))
                    throw new DnsSecValidationException("CNAME matching records could not be validated");

                validationResult = cNameValidationResult == recordsValidationResult ? cNameValidationResult : DnsSecValidationResult.Unsigned;
                _cache.Add(name, recordType, recordClass, records, validationResult, Math.Min(cName.TimeToLive, records.Min(x => x.TimeToLive)));

                return new DnsSecResult<T>(records, validationResult);
            }

            var cNameResults = await ResolveSecureAsync<T>(cName.CanonicalName, recordType, recordClass, token);
            validationResult = cNameValidationResult == cNameResults.ValidationResult ? cNameValidationResult : DnsSecValidationResult.Unsigned;

            if (cNameResults.Records.Count > 0)
                _cache.Add(name, recordType, recordClass, cNameResults.Records, validationResult, Math.Min(cName.TimeToLive, cNameResults.Records.Min(x => x.TimeToLive)));

            return new DnsSecResult<T>(cNameResults.Records, validationResult);
        }

        List<T> res = msg.AnswerRecords.Where(x => (x.RecordType == recordType) && (x.RecordClass == recordClass) && x.Name.Equals(name)).OfType<T>().ToList();

        validationResult = await _validator.ValidateAsync(name, recordType, recordClass, msg, res, null, token);

        if ((validationResult == DnsSecValidationResult.Bogus) || (validationResult == DnsSecValidationResult.Indeterminate))
            throw new DnsSecValidationException("Response records could not be validated");

        if (res.Count > 0)
            _cache.Add(name, recordType, recordClass, res, validationResult, res.Min(x => x.TimeToLive));

        return new DnsSecResult<T>(res, validationResult);
    }

    /// <summary>
    ///   Clears the record cache
    /// </summary>
    public void ClearCache()
    {
        _cache = new DnsCache();
    }

    Task<DnsMessage> IInternalDnsSecResolver<object>.ResolveMessageAsync(DomainName name, RecordType recordType, RecordClass recordClass, object state, CancellationToken token)
    {
        return _dnsClient.ResolveAsync(name, RecordType.Ds, recordClass, new DnsQueryOptions() { IsEDnsEnabled = true, IsDnsSecOk = true, IsCheckingDisabled = true, IsRecursionDesired = true }, token);
    }

    Task<DnsSecResult<TRecord>> IInternalDnsSecResolver<object>.ResolveSecureAsync<TRecord>(DomainName name, RecordType recordType, RecordClass recordClass, object state, CancellationToken token)
    {
        return ResolveSecureAsync<TRecord>(name, recordType, recordClass, token);
    }
}

/// <summary>
///   Implementation of IResolverHintStore, which uses statically linked hints
/// </summary>
public class StaticResolverHintStore : IResolverHintStore
{
    private static readonly List<IPAddress> _rootServers = new List<IPAddress>()
        {
			// a.root-servers.net
			IPAddress.Parse("198.41.0.4"),
            IPAddress.Parse("2001:503:ba3e::2:30"),

			// b.root-servers.net
			IPAddress.Parse("192.228.79.201"),
            IPAddress.Parse("2001:500:200::b"),

			// c.root-servers.net
			IPAddress.Parse("192.33.4.12"),
            IPAddress.Parse("2001:500:2::c"),

			// d.root-servers.net
			IPAddress.Parse("199.7.91.13"),
            IPAddress.Parse("2001:500:2d::d"),

			// e.root-servers.net
			IPAddress.Parse("192.203.230.10"),
            IPAddress.Parse("2001:500:a8::e"),

			// f.root-servers.net
			IPAddress.Parse("192.5.5.241"),
            IPAddress.Parse("2001:500:2f::f"),

			// g.root-servers.net
			IPAddress.Parse("192.112.36.4"),
            IPAddress.Parse("2001:500:12::d0d"),

			// h.root-servers.net
			IPAddress.Parse("198.97.190.53"),
            IPAddress.Parse("2001:500:1::53"),

			// i.root-servers.net
			IPAddress.Parse("192.36.148.17"),
            IPAddress.Parse("2001:7fe::53"),

			// j.root-servers.net
			IPAddress.Parse("192.58.128.30"),
            IPAddress.Parse("2001:503:c27::2:30"),

			// k.root-servers.net
			IPAddress.Parse("193.0.14.129"),
            IPAddress.Parse("2001:7fd::1"),

			// l.root-servers.net
			IPAddress.Parse("199.7.83.42"),
            IPAddress.Parse("2001:500:9f::42"),

			// m.root-servers.net
			IPAddress.Parse("202.12.27.33"),
            IPAddress.Parse("2001:dc3::35")
        };

    /// <summary>
    ///   List of hints to the root servers
    /// </summary>
    public List<IPAddress> RootServers => _rootServers;

    private static readonly List<DsRecord> _rootKeys = new List<DsRecord>()
        {
            new DsRecord(DomainName.Root, RecordClass.INet, 0, 19036, DnsSecAlgorithm.RsaSha256, DnsSecDigestType.Sha256, "49AAC11D7B6F6446702E54A1607371607A1A41855200FD2CE1CDDE32F24E8FB5".FromBase16String()),
            new DsRecord(DomainName.Root, RecordClass.INet, 0, 20326, DnsSecAlgorithm.RsaSha256, DnsSecDigestType.Sha256, "E06D44B80B8F1D39A95C0B0D7C65D08458E880409BBC683457104237C7F8EC8D".FromBase16String()),
        };

    /// <summary>
    ///   List of DsRecords of the root zone
    /// </summary>
    public List<DsRecord> RootKeys => _rootKeys;
}

/// <summary>
///   Base class for a ResolverHintStore, which has an updateable local storage for the hints
/// </summary>
public abstract class UpdateableResolverHintStoreBase : IResolverHintStore
{
    private bool _isInitiated;
    private List<IPAddress> _rootServers;
    private List<DsRecord> _rootKeys;

    /// <summary>
    ///   List of hints to the root servers
    /// </summary>
    public List<IPAddress> RootServers
    {
        get
        {
            EnsureInit();
            return _rootServers;
        }
        private set { _rootServers = value; }
    }

    /// <summary>
    ///   List of DsRecords of the root zone
    /// </summary>
    public List<DsRecord> RootKeys
    {
        get
        {
            EnsureInit();
            return _rootKeys;
        }
        private set { _rootKeys = value; }
    }

    /// <summary>
    ///   Forces to update all hints using the given resolver
    /// </summary>
    /// <param name="resolver">The resolver to use for resolving the new hints</param>
    public void Update(IDnsResolver resolver)
    {
        Zone zone = new Zone(DomainName.Root);

        var nameServer = resolver.Resolve<NsRecord>(DomainName.Root, RecordType.Ns);
        zone.AddRange(nameServer);

        foreach (var nsRecord in nameServer)
        {
            zone.AddRange(resolver.Resolve<ARecord>(nsRecord.NameServer, RecordType.A));
            zone.AddRange(resolver.Resolve<AaaaRecord>(nsRecord.NameServer, RecordType.Aaaa));
        }

        zone.AddRange(resolver.Resolve<DnsKeyRecord>(DomainName.Root, RecordType.DnsKey).Where(x => x.IsSecureEntryPoint));

        LoadZoneInternal(zone);

        Save(zone);
    }

    private void EnsureInit()
    {
        if (!_isInitiated)
        {
            Zone zone = Load();

            LoadZoneInternal(zone);

            _isInitiated = true;
        }
    }

    private void LoadZoneInternal(Zone zone)
    {
        var nameServers = zone.OfType<NsRecord>().Where(x => x.Name == DomainName.Root).Select(x => x.NameServer);
        RootServers = zone.Where(x => x.RecordType == RecordType.A || x.RecordType == RecordType.Aaaa).Join(nameServers, x => x.Name, x => x, (x, y) => ((IAddressRecord)x).Address).ToList();
        RootKeys = zone.OfType<DnsKeyRecord>().Where(x => (x.Name == DomainName.Root) && x.IsSecureEntryPoint).Select(x => new DsRecord(x, x.TimeToLive, DnsSecDigestType.Sha256)).ToList();
    }

    /// <summary>
    ///   Saves the hints to a local storage
    /// </summary>
    /// <param name="zone"></param>
    protected abstract void Save(Zone zone);

    /// <summary>
    ///   Loads the hints from a local storage
    /// </summary>
    /// <returns></returns>
    protected abstract Zone Load();
}

/// <summary>
///   Updateable Resolver HintStore using a local zone file for the hints
/// </summary>
public class ZoneFileResolverHintStore : UpdateableResolverHintStoreBase
{
    private readonly string _fileName;

    /// <summary>
    ///   Creates a new instance of the ZoneFileResolverHintStore class
    /// </summary>
    /// <param name="fileName">The path to the local zone file containing the hints</param>
    public ZoneFileResolverHintStore(string fileName)
    {
        _fileName = fileName;
    }

    /// <summary>
    ///   Saves the hints to the local file
    /// </summary>
    /// <param name="zone">The zone to save</param>
    protected override void Save(Zone zone)
    {
        using (StreamWriter writer = new StreamWriter(_fileName))
        {
            foreach (DnsRecordBase record in zone)
            {
                writer.WriteLine(record.ToString());
            }
        }
    }

    /// <summary>
    ///   Loads the hints from the local file
    /// </summary>
    /// <returns></returns>
    protected override Zone Load()
    {
        if (!File.Exists(_fileName))
            throw new FileNotFoundException();

        return Zone.ParseMasterFile(DomainName.Root, _fileName);
    }
}


#endif
