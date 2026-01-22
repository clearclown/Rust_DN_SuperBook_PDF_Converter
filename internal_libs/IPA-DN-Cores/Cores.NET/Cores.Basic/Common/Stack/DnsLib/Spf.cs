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
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

namespace IPA.Cores.Basic.DnsLib;

/// <summary>
///   <para>Parsed instance of the textual representation of a SenderID record</para>
///   <para>
///     Defined in
///     <see cref="!:http://tools.ietf.org/html/rfc4406">RFC 4406</see>
///   </para>
/// </summary>
public class SenderIDRecord : SpfRecordBase
{
    private static readonly Regex _prefixRegex = new Regex(@"^v=spf((?<version>1)|(?<version>2)\.(?<minor>\d)/(?<scopes>(([a-z0-9]+,)*[a-z0-9]+)))$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    ///   Version of the SenderID record.
    /// </summary>
    public int Version { get; set; }

    /// <summary>
    ///   Minor version of the SenderID record
    /// </summary>
    public int MinorVersion { get; set; }

    /// <summary>
    ///   List of Scopes of the SenderID record
    /// </summary>
    public List<SenderIDScope> Scopes { get; set; }

    /// <summary>
    ///   Returns the textual representation of the SenderID record
    /// </summary>
    /// <returns> Textual representation </returns>
    public override string ToString()
    {
        StringBuilder res = new StringBuilder();

        if (Version == 1)
        {
            res.Append("v=spf1");
        }
        else
        {
            res.Append("v=spf");
            res.Append(Version);
            res.Append(".");
            res.Append(MinorVersion);
            res.Append("/");
            res.Append(String.Join(",", Scopes.Where(s => s != SenderIDScope.Unknown).Select(s => EnumHelper<SenderIDScope>.ToString(s).ToLowerInvariant())));
        }

        if ((Terms != null) && (Terms.Count > 0))
        {
            foreach (SpfTerm term in Terms)
            {
                SpfModifier modifier = term as SpfModifier;
                if ((modifier == null) || (modifier.Type != SpfModifierType.Unknown))
                {
                    res.Append(" ");
                    res.Append(term);
                }
            }
        }

        return res.ToString();
    }

    /// <summary>
    ///   Checks, whether a given string starts with a correct SenderID prefix of a given scope
    /// </summary>
    /// <param name="s"> Textual representation to check </param>
    /// <param name="scope"> Scope, which should be matched </param>
    /// <returns> true in case of correct prefix </returns>
    public static bool IsSenderIDRecord(string s, SenderIDScope scope)
    {
        if (String.IsNullOrEmpty(s))
            return false;

        string[] terms = s.Split(new[] { ' ' }, 2);

        if (terms.Length < 2)
            return false;

        int version;
        int minor;
        List<SenderIDScope> scopes;
        if (!TryParsePrefix(terms[0], out version, out minor, out scopes))
        {
            return false;
        }

        if ((version == 1) && ((scope == SenderIDScope.MFrom) || (scope == SenderIDScope.Pra)))
        {
            return true;
        }
        else
        {
            return scopes.Contains(scope);
        }
    }

    private static bool TryParsePrefix(string prefix, out int version, out int minor, out List<SenderIDScope> scopes)
    {
        Match match = _prefixRegex.Match(prefix);
        if (!match.Success)
        {
            version = 0;
            minor = 0;
            scopes = null;

            return false;
        }

        version = Int32.Parse(match.Groups["version"].Value);
        minor = Int32.Parse("0" + match.Groups["minor"].Value);
        scopes = match.Groups["scopes"].Value.Split(',').Select(t => EnumHelper<SenderIDScope>.Parse(t, true, SenderIDScope.Unknown)).ToList();

        return true;
    }

    /// <summary>
    ///   Tries to parse the textual representation of a SenderID record
    /// </summary>
    /// <param name="s"> Textual representation to check </param>
    /// <param name="value"> Parsed SenderID record in case of successful parsing </param>
    /// <returns> true in case of successful parsing </returns>
    public static bool TryParse(string s, out SenderIDRecord value)
    {
        if (String.IsNullOrEmpty(s))
        {
            value = null;
            return false;
        }

        string[] terms = s.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

        if (terms.Length < 1)
        {
            value = null;
            return false;
        }

        int version;
        int minor;
        List<SenderIDScope> scopes;
        if (!TryParsePrefix(terms[0], out version, out minor, out scopes))
        {
            value = null;
            return false;
        }

        List<SpfTerm> parsedTerms;
        if (TryParseTerms(terms, out parsedTerms))
        {
            value =
                new SenderIDRecord
                {
                    Version = version,
                    MinorVersion = minor,
                    Scopes = scopes,
                    Terms = parsedTerms
                };
            return true;
        }
        else
        {
            value = null;
            return false;
        }
    }
}

/// <summary>
///   Scope of a SenderID record
/// </summary>
public enum SenderIDScope
{
    /// <summary>
    ///   Unknown scope
    /// </summary>
    Unknown,

    /// <summary>
    ///   MFrom scope, used for lookups of SMTP MAIL FROM address
    /// </summary>
    MFrom,

    /// <summary>
    ///   PRA scope, used for lookups of the Purported Responsible Address
    /// </summary>
    Pra,
}

/// <summary>
///   Validator for SenderID records
/// </summary>
public class SenderIDValidator : ValidatorBase<SenderIDRecord>
{
    /// <summary>
    ///   Scope to examin
    /// </summary>
    public SenderIDScope Scope { get; set; }

    /// <summary>
    ///   Initializes a new instance of the SenderIDValidator class.
    /// </summary>
    public SenderIDValidator()
    {
        Scope = SenderIDScope.MFrom;
    }

    protected override async Task<LoadRecordResult> LoadRecordsAsync(DomainName domain, CancellationToken token)
    {
        DnsResolveResult<TxtRecord> dnsResult = await ResolveDnsAsync<TxtRecord>(domain, RecordType.Txt, token);
        if ((dnsResult == null) || ((dnsResult.ReturnCode != ReturnCode.NoError) && (dnsResult.ReturnCode != ReturnCode.NxDomain)))
        {
            return new LoadRecordResult() { CouldBeLoaded = false, ErrorResult = SpfQualifier.TempError };
        }
        else if ((Scope == SenderIDScope.Pra) && (dnsResult.ReturnCode == ReturnCode.NxDomain))
        {
            return new LoadRecordResult() { CouldBeLoaded = false, ErrorResult = SpfQualifier.Fail };
        }

        var senderIDTextRecords = dnsResult.Records
            .Select(r => r.TextData)
            .Where(t => SenderIDRecord.IsSenderIDRecord(t, Scope))
            .ToList();

        if (senderIDTextRecords.Count >= 1)
        {
            var potentialRecords = new List<SenderIDRecord>();
            foreach (var senderIDTextRecord in senderIDTextRecords)
            {
                SenderIDRecord tmpRecord;
                if (SenderIDRecord.TryParse(senderIDTextRecord, out tmpRecord))
                {
                    potentialRecords.Add(tmpRecord);
                }
                else
                {
                    return new LoadRecordResult() { CouldBeLoaded = false, ErrorResult = SpfQualifier.PermError };
                }
            }

            if (potentialRecords.GroupBy(r => r.Version).Any(g => g.Count() > 1))
            {
                return new LoadRecordResult() { CouldBeLoaded = false, ErrorResult = SpfQualifier.PermError };
            }
            else
            {
                return new LoadRecordResult() { CouldBeLoaded = true, ErrorResult = default(SpfQualifier), Record = potentialRecords.OrderByDescending(r => r.Version).First() };
            }
        }
        else
        {
            return new LoadRecordResult() { CouldBeLoaded = false, ErrorResult = SpfQualifier.None };
        }
    }
}

internal class SpfCheckHostParameter
{
    public SpfCheckHostParameter(IPAddress clientAddress, string clientName, string heloName, string domain, string sender)
    {
        LoopCount = 0;
        ClientAddress = clientAddress;
        ClientName = clientName;
        HeloName = heloName;
        CurrentDomain = domain;
        Sender = sender;
    }

    public SpfCheckHostParameter(string newDomain, SpfCheckHostParameter oldParameters)
    {
        LoopCount = oldParameters.LoopCount + 1;
        ClientAddress = oldParameters.ClientAddress;
        ClientName = oldParameters.ClientName;
        HeloName = oldParameters.HeloName;
        CurrentDomain = newDomain;
        Sender = oldParameters.Sender;
    }

    public int LoopCount;
    public IPAddress ClientAddress;
    public string ClientName;
    public string HeloName;
    public string CurrentDomain;
    public string Sender;
}

/// <summary>
///   Represents a single mechanism term in a SPF record
/// </summary>
public class SpfMechanism : SpfTerm
{
    /// <summary>
    ///   Qualifier of the mechanism
    /// </summary>
    public SpfQualifier Qualifier { get; set; }

    /// <summary>
    ///   Type of the mechanism
    /// </summary>
    public SpfMechanismType Type { get; set; }

    /// <summary>
    ///   Domain part of the mechanism
    /// </summary>
    public string Domain { get; set; }

    /// <summary>
    ///   IPv4 prefix of the mechanism
    /// </summary>
    public int? Prefix { get; set; }

    /// <summary>
    ///   IPv6 prefix of the mechanism
    /// </summary>
    public int? Prefix6 { get; set; }

    /// <summary>
    ///   Returns the textual representation of a mechanism term
    /// </summary>
    /// <returns> Textual representation </returns>
    public override string ToString()
    {
        StringBuilder res = new StringBuilder();

        switch (Qualifier)
        {
            case SpfQualifier.Fail:
                res.Append("-");
                break;
            case SpfQualifier.SoftFail:
                res.Append("~");
                break;
            case SpfQualifier.Neutral:
                res.Append("?");
                break;
        }

        res.Append(EnumHelper<SpfMechanismType>.ToString(Type).ToLowerInvariant());

        if (!String.IsNullOrEmpty(Domain))
        {
            res.Append(":");
            res.Append(Domain);
        }

        if (Prefix.HasValue)
        {
            res.Append("/");
            res.Append(Prefix.Value);
        }

        if (Prefix6.HasValue)
        {
            res.Append("//");
            res.Append(Prefix6.Value);
        }

        return res.ToString();
    }
}

/// <summary>
///   Type of spf mechanism
/// </summary>
public enum SpfMechanismType
{
    /// <summary>
    ///   Unknown mechanism
    /// </summary>
    Unknown,

    /// <summary>
    ///   All mechanism, matches always
    /// </summary>
    All,

    /// <summary>
    ///   IP4 mechanism, matches if ip address (IPv4) is within the given network
    /// </summary>
    Ip4,

    /// <summary>
    ///   IP6 mechanism, matches if ip address (IPv6) is within the given network
    /// </summary>
    Ip6,

    /// <summary>
    ///   A mechanism, matches if the ip address is the target of a hostname lookup for the given domain
    /// </summary>
    A,

    /// <summary>
    ///   MX mechanism, matches if the ip address is a mail exchanger for the given domain
    /// </summary>
    Mx,

    /// <summary>
    ///   PTR mechanism, matches if a correct reverse mapping exists
    /// </summary>
    Ptr,

    /// <summary>
    ///   EXISTS mechanism, matches if the given domain exists
    /// </summary>
    Exists,

    /// <summary>
    ///   INCLUDE mechanism, triggers a recursive evaluation
    /// </summary>
    Include,
}

/// <summary>
///   Represents a single modifier term in a SPF record
/// </summary>
public class SpfModifier : SpfTerm
{
    /// <summary>
    ///   Type of the modifier
    /// </summary>
    public SpfModifierType Type { get; set; }

    /// <summary>
    ///   Domain part of the modifier
    /// </summary>
    public string Domain { get; set; }

    /// <summary>
    ///   Returns the textual representation of a modifier term
    /// </summary>
    /// <returns> Textual representation </returns>
    public override string ToString()
    {
        StringBuilder res = new StringBuilder();

        res.Append(EnumHelper<SpfModifierType>.ToString(Type).ToLowerInvariant());
        res.Append("=");
        res.Append(Domain);

        return res.ToString();
    }
}

/// <summary>
///   Type of the spf modifier
/// </summary>
public enum SpfModifierType
{
    /// <summary>
    ///   Unknown mechanism
    /// </summary>
    Unknown,

    /// <summary>
    ///   REDIRECT modifier, redirects the evaluation to another record, if of all tests fail
    /// </summary>
    Redirect,

    /// <summary>
    ///   EXP modifier, used for lookup of a explanation in case of failed test
    /// </summary>
    Exp,
}

/// <summary>
///   Qualifier of spf mechanism
/// </summary>
public enum SpfQualifier
{
    /// <summary>
    ///   No records were published or no checkable sender could be determined
    /// </summary>
    None,

    /// <summary>
    ///   Client is allowed to send mail with the given identity
    /// </summary>
    Pass,

    /// <summary>
    ///   Client is explicit not allowed to send mail with the given identity
    /// </summary>
    Fail,

    /// <summary>
    ///   Client is not allowed to send mail with the given identity
    /// </summary>
    SoftFail,

    /// <summary>
    ///   No statement if a client is allowed or not allowed to send mail with the given identity
    /// </summary>
    Neutral,

    /// <summary>
    ///   A transient error encountered while performing the check
    /// </summary>
    TempError,

    /// <summary>
    ///   The published record could not be correctly interpreted
    /// </summary>
    PermError,
}

/// <summary>
///   <para>Parsed instance of the textual representation of a SPF record</para>
///   <para>
///     Defined in
///     <see cref="!:http://tools.ietf.org/html/rfc4408">RFC 4408</see>
///   </para>
/// </summary>
public class SpfRecord : SpfRecordBase
{
    /// <summary>
    ///   Returns the textual representation of a SPF record
    /// </summary>
    /// <returns> Textual representation </returns>
    public override string ToString()
    {
        StringBuilder res = new StringBuilder();
        res.Append("v=spf1");

        if ((Terms != null) && (Terms.Count > 0))
        {
            foreach (SpfTerm term in Terms)
            {
                SpfModifier modifier = term as SpfModifier;
                if ((modifier == null) || (modifier.Type != SpfModifierType.Unknown))
                {
                    res.Append(" ");
                    res.Append(term);
                }
            }
        }

        return res.ToString();
    }

    /// <summary>
    ///   Checks, whether a given string starts with a correct SPF prefix
    /// </summary>
    /// <param name="s"> Textual representation to check </param>
    /// <returns> true in case of correct prefix </returns>
    public static bool IsSpfRecord(string s)
    {
        return !String.IsNullOrEmpty(s) && s.StartsWith("v=spf1 ");
    }

    /// <summary>
    ///   Tries to parse the textual representation of a SPF string
    /// </summary>
    /// <param name="s"> Textual representation to check </param>
    /// <param name="value"> Parsed spf record in case of successful parsing </param>
    /// <returns> true in case of successful parsing </returns>
    public static bool TryParse(string s, out SpfRecord value)
    {
        if (!IsSpfRecord(s))
        {
            value = null;
            return false;
        }

        string[] terms = s.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

        List<SpfTerm> parsedTerms;
        if (TryParseTerms(terms, out parsedTerms))
        {
            value = new SpfRecord { Terms = parsedTerms };
            return true;
        }
        else
        {
            value = null;
            return false;
        }
    }
}

/// <summary>
///   Base class of a SPF or SenderID record
/// </summary>
public class SpfRecordBase
{
    /// <summary>
    ///   Modifiers and mechanisms of a record
    /// </summary>
    public List<SpfTerm> Terms { get; set; }

    protected static bool TryParseTerms(string[] terms, out List<SpfTerm> parsedTerms)
    {
        parsedTerms = new List<SpfTerm>(terms.Length - 1);

        for (int i = 1; i < terms.Length; i++)
        {
            SpfTerm term;
            if (SpfTerm.TryParse(terms[i], out term))
            {
                parsedTerms.Add(term);
            }
            else
            {
                parsedTerms = null;
                return false;
            }
        }

        return true;
    }
}

/// <summary>
///   Represents a single term of a SPF record
/// </summary>
public class SpfTerm
{
    private static readonly Regex _parseMechanismRegex = new Regex(@"^(\s)*(?<qualifier>[~+?-]?)(?<type>[a-z0-9]+)(:(?<domain>[^/]+))?(/(?<prefix>[0-9]+)(/(?<prefix6>[0-9]+))?)?(\s)*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex _parseModifierRegex = new Regex(@"^(\s)*(?<type>[a-z]+)=(?<domain>[^\s]+)(\s)*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    internal static bool TryParse(string s, out SpfTerm value)
    {
        if (String.IsNullOrEmpty(s))
        {
            value = null;
            return false;
        }

        #region Parse Mechanism
        Match match = _parseMechanismRegex.Match(s);
        if (match.Success)
        {
            SpfMechanism mechanism = new SpfMechanism();

            switch (match.Groups["qualifier"].Value)
            {
                case "+":
                    mechanism.Qualifier = SpfQualifier.Pass;
                    break;
                case "-":
                    mechanism.Qualifier = SpfQualifier.Fail;
                    break;
                case "~":
                    mechanism.Qualifier = SpfQualifier.SoftFail;
                    break;
                case "?":
                    mechanism.Qualifier = SpfQualifier.Neutral;
                    break;

                default:
                    mechanism.Qualifier = SpfQualifier.Pass;
                    break;
            }

            SpfMechanismType type;
            mechanism.Type = EnumHelper<SpfMechanismType>.TryParse(match.Groups["type"].Value, true, out type) ? type : SpfMechanismType.Unknown;

            mechanism.Domain = match.Groups["domain"].Value;

            string tmpPrefix = match.Groups["prefix"].Value;
            int prefix;
            if (!String.IsNullOrEmpty(tmpPrefix) && Int32.TryParse(tmpPrefix, out prefix))
            {
                mechanism.Prefix = prefix;
            }

            tmpPrefix = match.Groups["prefix6"].Value;
            if (!String.IsNullOrEmpty(tmpPrefix) && Int32.TryParse(tmpPrefix, out prefix))
            {
                mechanism.Prefix6 = prefix;
            }

            value = mechanism;
            return true;
        }
        #endregion

        #region Parse Modifier
        match = _parseModifierRegex.Match(s);
        if (match.Success)
        {
            SpfModifier modifier = new SpfModifier();

            SpfModifierType type;
            modifier.Type = EnumHelper<SpfModifierType>.TryParse(match.Groups["type"].Value, true, out type) ? type : SpfModifierType.Unknown;
            modifier.Domain = match.Groups["domain"].Value;

            value = modifier;
            return true;
        }
        #endregion

        value = null;
        return false;
    }
}

/// <summary>
///   Validator for SPF records
/// </summary>
public class SpfValidator : ValidatorBase<SpfRecord>
{
    protected override async Task<LoadRecordResult> LoadRecordsAsync(DomainName domain, CancellationToken token)
    {
        DnsResolveResult<TxtRecord> dnsResult = await ResolveDnsAsync<TxtRecord>(domain, RecordType.Txt, token);
        if ((dnsResult == null) || ((dnsResult.ReturnCode != ReturnCode.NoError) && (dnsResult.ReturnCode != ReturnCode.NxDomain)))
        {
            return new LoadRecordResult() { CouldBeLoaded = false, ErrorResult = SpfQualifier.TempError };
        }

        var spfTextRecords = dnsResult.Records
            .Select(r => r.TextData)
            .Where(SpfRecord.IsSpfRecord)
            .ToList();

        SpfRecord record;

        if (spfTextRecords.Count == 0)
        {
            return new LoadRecordResult() { CouldBeLoaded = false, ErrorResult = SpfQualifier.None };
        }
        else if ((spfTextRecords.Count > 1) || !SpfRecord.TryParse(spfTextRecords[0], out record))
        {
            return new LoadRecordResult() { CouldBeLoaded = false, ErrorResult = SpfQualifier.PermError };
        }
        else
        {
            return new LoadRecordResult() { CouldBeLoaded = true, Record = record };
        }
    }
}

/// <summary>
///   The result of a SPF or SenderID validation
/// </summary>
public class ValidationResult
{
    /// <summary>
    ///   The result of the validation
    /// </summary>
    public SpfQualifier Result { get; internal set; }

    /// <summary>
    ///   A explanation in case of result Fail. Only filled if requested on validation call
    /// </summary>
    public string Explanation { get; internal set; }
}

/// <summary>
///   Base implementation of a validator for SPF and SenderID records
/// </summary>
/// <typeparam name="T"> Type of the record </typeparam>
public abstract class ValidatorBase<T>
    where T : SpfRecordBase
{
    protected class State
    {
        public int DnsLookupCount { get; set; }
    }

    // ReSharper disable once StaticMemberInGenericType
    private static readonly Regex _parseMacroRegex = new Regex(@"(%%|%_|%-|%\{(?<letter>[slodiphcrtv])(?<count>\d*)(?<reverse>r?)(?<delimiter>[\.\-+,/=]*)})", RegexOptions.Compiled);

    /// <summary>
    ///   DnsResolver which is used for DNS lookups
    ///   <para>Default is a Stub DNS resolver using the local configured upstream servers</para>
    /// </summary>
    public IDnsResolver DnsResolver { get; set; } = new DnsStubResolver();

    /// <summary>
    ///   Domain name which was used in HELO/EHLO
    /// </summary>
    public DomainName HeloDomain { get; set; }

    /// <summary>
    ///   IP address of the computer validating the record
    ///   <para>Default is the first IP the computer</para>
    /// </summary>
    public IPAddress LocalIP { get; set; }

    /// <summary>
    ///   Name of the computer validating the record
    ///   <para>Default is the computer name</para>
    /// </summary>
    public DomainName LocalDomain { get; set; }

    /// <summary>
    ///   The maximum number of DNS lookups allowed
    ///   <para>Default is 20</para>
    /// </summary>
    public int DnsLookupLimit { get; set; } = 20;

    protected abstract Task<LoadRecordResult> LoadRecordsAsync(DomainName domain, CancellationToken token);

    /// <summary>
    ///   Validates the record(s)
    /// </summary>
    /// <param name="ip"> The IP address of the SMTP client that is emitting the mail </param>
    /// <param name="domain"> The domain portion of the "MAIL FROM" or "HELO" identity </param>
    /// <param name="sender"> The "MAIL FROM" or "HELO" identity </param>
    /// <param name="expandExplanation"> A value indicating if the explanation should be retrieved in case of Fail</param>
    /// <returns> The result of the evaluation </returns>
    public ValidationResult CheckHost(IPAddress ip, DomainName domain, string sender, bool expandExplanation = false)
    {
        var result = CheckHostInternalAsync(ip, domain, sender, expandExplanation, new State(), default(CancellationToken));
        result.Wait();

        return result.Result;
    }

    /// <summary>
    ///   Validates the record(s)
    /// </summary>
    /// <param name="ip"> The IP address of the SMTP client that is emitting the mail </param>
    /// <param name="domain"> The domain portion of the "MAIL FROM" or "HELO" identity </param>
    /// <param name="sender"> The "MAIL FROM" or "HELO" identity </param>
    /// <param name="expandExplanation"> A value indicating if the explanation should be retrieved in case of Fail</param>
    /// <returns> The result of the evaluation </returns>
    public ValidationResult CheckHost(IPAddress ip, string domain, string sender, bool expandExplanation = false)
    {
        return CheckHost(ip, DomainName.Parse(domain), sender, expandExplanation);
    }

    /// <summary>
    ///   Validates the record(s)
    /// </summary>
    /// <param name="ip"> The IP address of the SMTP client that is emitting the mail </param>
    /// <param name="domain"> The domain portion of the "MAIL FROM" or "HELO" identity </param>
    /// <param name="sender"> The "MAIL FROM" or "HELO" identity </param>
    /// <param name="expandExplanation"> A value indicating if the explanation should be retrieved in case of Fail</param>
    /// <param name="token"> The token to monitor cancellation requests </param>
    /// <returns> The result of the evaluation </returns>
    public Task<ValidationResult> CheckHostAsync(IPAddress ip, DomainName domain, string sender, bool expandExplanation = false, CancellationToken token = default(CancellationToken))
    {
        return CheckHostInternalAsync(ip, domain, sender, expandExplanation, new State(), token);
    }

    /// <summary>
    ///   Validates the record(s)
    /// </summary>
    /// <param name="ip"> The IP address of the SMTP client that is emitting the mail </param>
    /// <param name="domain"> The domain portion of the "MAIL FROM" or "HELO" identity </param>
    /// <param name="sender"> The "MAIL FROM" or "HELO" identity </param>
    /// <param name="expandExplanation"> A value indicating if the explanation should be retrieved in case of Fail</param>
    /// <param name="token"> The token to monitor cancellation requests </param>
    /// <returns> The result of the evaluation </returns>
    public Task<ValidationResult> CheckHostAsync(IPAddress ip, string domain, string sender, bool expandExplanation = false, CancellationToken token = default(CancellationToken))
    {
        return CheckHostAsync(ip, DomainName.Parse(domain), sender, expandExplanation, token);
    }

    protected class LoadRecordResult
    {
        public bool CouldBeLoaded { get; internal set; }
        public T Record { get; internal set; }
        public SpfQualifier ErrorResult { get; internal set; }
    }

    private async Task<ValidationResult> CheckHostInternalAsync(IPAddress ip, DomainName domain, string sender, bool expandExplanation, State state, CancellationToken token)
    {
        if ((domain == null) || (domain.Equals(DomainName.Root)))
        {
            return new ValidationResult() { Result = SpfQualifier.None, Explanation = String.Empty };
        }

        if (String.IsNullOrEmpty(sender))
        {
            sender = "postmaster@unknown";
        }
        else if (!sender.Contains('@'))
        {
            sender = "postmaster@" + sender;
        }

        LoadRecordResult loadResult = await LoadRecordsAsync(domain, token);

        if (!loadResult.CouldBeLoaded)
        {
            return new ValidationResult() { Result = loadResult.ErrorResult, Explanation = String.Empty };
        }

        T record = loadResult.Record;

        if ((record.Terms == null) || (record.Terms.Count == 0))
            return new ValidationResult() { Result = SpfQualifier.Neutral, Explanation = String.Empty };

        if (record.Terms.OfType<SpfModifier>().GroupBy(m => m.Type).Where(g => (g.Key == SpfModifierType.Exp) || (g.Key == SpfModifierType.Redirect)).Any(g => g.Count() > 1))
            return new ValidationResult() { Result = SpfQualifier.PermError, Explanation = String.Empty };

        ValidationResult result = new ValidationResult() { Result = loadResult.ErrorResult };

        #region Evaluate mechanism
        foreach (SpfMechanism mechanism in record.Terms.OfType<SpfMechanism>())
        {
            if (state.DnsLookupCount > DnsLookupLimit)
                return new ValidationResult() { Result = SpfQualifier.PermError, Explanation = String.Empty };

            SpfQualifier qualifier = await CheckMechanismAsync(mechanism, ip, domain, sender, state, token);

            if (qualifier != SpfQualifier.None)
            {
                result.Result = qualifier;
                break;
            }
        }
        #endregion

        #region Evaluate modifiers
        if (result.Result == SpfQualifier.None)
        {
            SpfModifier redirectModifier = record.Terms.OfType<SpfModifier>().FirstOrDefault(m => m.Type == SpfModifierType.Redirect);
            if (redirectModifier != null)
            {
                if (++state.DnsLookupCount > 10)
                    return new ValidationResult() { Result = SpfQualifier.PermError, Explanation = String.Empty };

                DomainName redirectDomain = await ExpandDomainAsync(redirectModifier.Domain ?? String.Empty, ip, domain, sender, token);

                if ((redirectDomain == null) || (redirectDomain == DomainName.Root) || (redirectDomain.Equals(domain)))
                {
                    result.Result = SpfQualifier.PermError;
                }
                else
                {
                    result = await CheckHostInternalAsync(ip, redirectDomain, sender, expandExplanation, state, token);

                    if (result.Result == SpfQualifier.None)
                        result.Result = SpfQualifier.PermError;
                }
            }
        }
        else if ((result.Result == SpfQualifier.Fail) && expandExplanation)
        {
            SpfModifier expModifier = record.Terms.OfType<SpfModifier>().FirstOrDefault(m => m.Type == SpfModifierType.Exp);
            if (expModifier != null)
            {
                DomainName target = await ExpandDomainAsync(expModifier.Domain, ip, domain, sender, token);

                if ((target == null) || (target.Equals(DomainName.Root)))
                {
                    result.Explanation = String.Empty;
                }
                else
                {
                    DnsResolveResult<TxtRecord> dnsResult = await ResolveDnsAsync<TxtRecord>(target, RecordType.Txt, token);
                    if ((dnsResult != null) && (dnsResult.ReturnCode == ReturnCode.NoError))
                    {
                        TxtRecord txtRecord = dnsResult.Records.FirstOrDefault();
                        if (txtRecord != null)
                        {
                            result.Explanation = (await ExpandMacroAsync(txtRecord.TextData, ip, domain, sender, token)).ToString();
                        }
                    }
                }
            }
        }
        #endregion

        if (result.Result == SpfQualifier.None)
            result.Result = SpfQualifier.Neutral;

        return result;
    }

    private async Task<SpfQualifier> CheckMechanismAsync(SpfMechanism mechanism, IPAddress ip, DomainName domain, string sender, State state, CancellationToken token)
    {
        switch (mechanism.Type)
        {
            case SpfMechanismType.All:
                return mechanism.Qualifier;

            case SpfMechanismType.A:
                if (++state.DnsLookupCount > 10)
                    return SpfQualifier.PermError;

                DomainName aMechanismDomain = String.IsNullOrEmpty(mechanism.Domain) ? domain : await ExpandDomainAsync(mechanism.Domain, ip, domain, sender, token);

                bool? isAMatch = await IsIpMatchAsync(aMechanismDomain, ip, mechanism.Prefix, mechanism.Prefix6, token);
                if (!isAMatch.HasValue)
                    return SpfQualifier.TempError;

                if (isAMatch.Value)
                {
                    return mechanism.Qualifier;
                }
                break;

            case SpfMechanismType.Mx:
                if (++state.DnsLookupCount > 10)
                    return SpfQualifier.PermError;

                DomainName mxMechanismDomain = String.IsNullOrEmpty(mechanism.Domain) ? domain : await ExpandDomainAsync(mechanism.Domain, ip, domain, sender, token);

                DnsResolveResult<MxRecord> dnsMxResult = await ResolveDnsAsync<MxRecord>(mxMechanismDomain, RecordType.Mx, token);
                if ((dnsMxResult == null) || ((dnsMxResult.ReturnCode != ReturnCode.NoError) && (dnsMxResult.ReturnCode != ReturnCode.NxDomain)))
                    return SpfQualifier.TempError;

                int mxCheckedCount = 0;

                foreach (MxRecord mxRecord in dnsMxResult.Records)
                {
                    if (++mxCheckedCount == 10)
                        break;

                    bool? isMxMatch = await IsIpMatchAsync(mxRecord.ExchangeDomainName, ip, mechanism.Prefix, mechanism.Prefix6, token);
                    if (!isMxMatch.HasValue)
                        return SpfQualifier.TempError;

                    if (isMxMatch.Value)
                    {
                        return mechanism.Qualifier;
                    }
                }
                break;

            case SpfMechanismType.Ip4:
            case SpfMechanismType.Ip6:
                IPAddress compareAddress;
                if (IPAddress.TryParse(mechanism.Domain, out compareAddress))
                {
                    if (ip.AddressFamily != compareAddress.AddressFamily)
                        return SpfQualifier.None;

                    if (mechanism.Prefix.HasValue)
                    {
                        if ((mechanism.Prefix.Value < 0) || (mechanism.Prefix.Value > (compareAddress.AddressFamily == AddressFamily.InterNetworkV6 ? 128 : 32)))
                            return SpfQualifier.PermError;

                        if (ip.GetNetworkAddress(mechanism.Prefix.Value).Equals(compareAddress.GetNetworkAddress(mechanism.Prefix.Value)))
                        {
                            return mechanism.Qualifier;
                        }
                    }
                    else if (ip.Equals(compareAddress))
                    {
                        return mechanism.Qualifier;
                    }
                }
                else
                {
                    return SpfQualifier.PermError;
                }

                break;

            case SpfMechanismType.Ptr:
                if (++state.DnsLookupCount > 10)
                    return SpfQualifier.PermError;

                DnsResolveResult<PtrRecord> dnsPtrResult = await ResolveDnsAsync<PtrRecord>(ip.GetReverseLookupDomain(), RecordType.Ptr, token);
                if ((dnsPtrResult == null) || ((dnsPtrResult.ReturnCode != ReturnCode.NoError) && (dnsPtrResult.ReturnCode != ReturnCode.NxDomain)))
                    return SpfQualifier.TempError;

                DomainName ptrMechanismDomain = String.IsNullOrEmpty(mechanism.Domain) ? domain : await ExpandDomainAsync(mechanism.Domain, ip, domain, sender, token);

                int ptrCheckedCount = 0;
                foreach (PtrRecord ptrRecord in dnsPtrResult.Records)
                {
                    if (++ptrCheckedCount == 10)
                        break;

                    bool? isPtrMatch = await IsIpMatchAsync(ptrRecord.PointerDomainName, ip, 0, 0, token);
                    if (isPtrMatch.HasValue && isPtrMatch.Value)
                    {
                        if (ptrRecord.PointerDomainName.Equals(ptrMechanismDomain) || (ptrRecord.PointerDomainName.IsSubDomainOf(ptrMechanismDomain)))
                            return mechanism.Qualifier;
                    }
                }
                break;

            case SpfMechanismType.Exists:
                if (++state.DnsLookupCount > 10)
                    return SpfQualifier.PermError;

                if (String.IsNullOrEmpty(mechanism.Domain))
                    return SpfQualifier.PermError;

                DomainName existsMechanismDomain = String.IsNullOrEmpty(mechanism.Domain) ? domain : await ExpandDomainAsync(mechanism.Domain, ip, domain, sender, token);

                DnsResolveResult<ARecord> dnsAResult = await ResolveDnsAsync<ARecord>(existsMechanismDomain, RecordType.A, token);
                if ((dnsAResult == null) || ((dnsAResult.ReturnCode != ReturnCode.NoError) && (dnsAResult.ReturnCode != ReturnCode.NxDomain)))
                    return SpfQualifier.TempError;

                if (dnsAResult.Records.Count(record => (record.RecordType == RecordType.A)) > 0)
                {
                    return mechanism.Qualifier;
                }
                break;

            case SpfMechanismType.Include:
                if (++state.DnsLookupCount > 10)
                    return SpfQualifier.PermError;

                if (String.IsNullOrEmpty(mechanism.Domain))
                    return SpfQualifier.PermError;

                DomainName includeMechanismDomain = String.IsNullOrEmpty(mechanism.Domain) ? domain : await ExpandDomainAsync(mechanism.Domain, ip, domain, sender, token);

                if (includeMechanismDomain.Equals(domain))
                    return SpfQualifier.PermError;

                var includeResult = await CheckHostInternalAsync(ip, includeMechanismDomain, sender, false, state, token);
                switch (includeResult.Result)
                {
                    case SpfQualifier.Pass:
                        return mechanism.Qualifier;

                    case SpfQualifier.Fail:
                    case SpfQualifier.SoftFail:
                    case SpfQualifier.Neutral:
                        return SpfQualifier.None;

                    case SpfQualifier.TempError:
                        return SpfQualifier.TempError;

                    case SpfQualifier.PermError:
                    case SpfQualifier.None:
                        return SpfQualifier.PermError;
                }
                break;

            default:
                return SpfQualifier.PermError;
        }

        return SpfQualifier.None;
    }

    private Task<bool?> IsIpMatchAsync(DomainName domain, IPAddress ipAddress, int? prefix4, int? prefix6, CancellationToken token)
    {
        if (ipAddress.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (prefix6.HasValue)
                ipAddress = ipAddress.GetNetworkAddress(prefix6.Value);

            return IsIpMatchAsync<AaaaRecord>(domain, ipAddress, prefix6, RecordType.Aaaa, token);
        }
        else
        {
            if (prefix4.HasValue)
                ipAddress = ipAddress.GetNetworkAddress(prefix4.Value);

            return IsIpMatchAsync<ARecord>(domain, ipAddress, prefix4, RecordType.A, token);
        }
    }

    private async Task<bool?> IsIpMatchAsync<TRecord>(DomainName domain, IPAddress ipAddress, int? prefix, RecordType recordType, CancellationToken token)
        where TRecord : DnsRecordBase, IAddressRecord
    {
        DnsResolveResult<TRecord> dnsResult = await ResolveDnsAsync<TRecord>(domain, recordType, token);
        if ((dnsResult == null) || ((dnsResult.ReturnCode != ReturnCode.NoError) && (dnsResult.ReturnCode != ReturnCode.NxDomain)))
            return null;

        foreach (var dnsRecord in dnsResult.Records)
        {
            if (prefix.HasValue)
            {
                if (ipAddress.Equals(dnsRecord.Address.GetNetworkAddress(prefix.Value)))
                    return true;
            }
            else
            {
                if (ipAddress.Equals(dnsRecord.Address))
                    return true;
            }
        }

        return false;
    }

    protected class DnsResolveResult<TRecord>
        where TRecord : DnsRecordBase
    {
        public ReturnCode ReturnCode { get; }
        public List<TRecord> Records { get; }

        public DnsResolveResult(ReturnCode returnCode, List<TRecord> records)
        {
            ReturnCode = returnCode;
            Records = records;
        }
    }

    protected async Task<DnsResolveResult<TRecord>> ResolveDnsAsync<TRecord>(DomainName domain, RecordType recordType, CancellationToken token)
        where TRecord : DnsRecordBase
    {
        try
        {
            var records = await DnsResolver.ResolveAsync<TRecord>(domain, recordType, token: token);
            return new DnsResolveResult<TRecord>(ReturnCode.NoError, records);
        }
        catch
        {
            return new DnsResolveResult<TRecord>(ReturnCode.ServerFailure, null);
        }
    }

    private async Task<DomainName> ExpandDomainAsync(string pattern, IPAddress ip, DomainName domain, string sender, CancellationToken token)
    {
        string expanded = await ExpandMacroAsync(pattern, ip, domain, sender, token);

        if (String.IsNullOrEmpty(expanded))
            return DomainName.Root;

        return DomainName.Parse(expanded);
    }

    private async Task<string> ExpandMacroAsync(string pattern, IPAddress ip, DomainName domain, string sender, CancellationToken token)
    {
        if (String.IsNullOrEmpty(pattern))
            return String.Empty;

        Match match = _parseMacroRegex.Match(pattern);
        if (!match.Success)
        {
            return pattern;
        }

        StringBuilder sb = new StringBuilder();
        int pos = 0;
        do
        {
            if (match.Index != pos)
            {
                sb.Append(pattern, pos, match.Index - pos);
            }
            pos = match.Index + match.Length;
            sb.Append(await ExpandMacroAsync(match, ip, domain, sender, token));
            match = match.NextMatch();
        } while (match.Success);

        if (pos < pattern.Length)
        {
            sb.Append(pattern, pos, pattern.Length - pos);
        }

        return sb.ToString();
    }

    private async Task<string> ExpandMacroAsync(Match pattern, IPAddress ip, DomainName domain, string sender, CancellationToken token)
    {
        switch (pattern.Value)
        {
            case "%%":
                return "%";
            case "%_":
                return "_";
            case "%-":
                return "-";

            default:
                string letter;
                switch (pattern.Groups["letter"].Value)
                {
                    case "s":
                        letter = sender;
                        break;
                    case "l":
                        // no boundary check needed, sender is validated on start of CheckHost
                        letter = sender.Split('@')[0];
                        break;
                    case "o":
                        // no boundary check needed, sender is validated on start of CheckHost
                        letter = sender.Split('@')[1];
                        break;
                    case "d":
                        letter = domain.ToString();
                        break;
                    case "i":
                        letter = String.Join(".", ip.GetAddressBytes().Select(b => b.ToString()));
                        break;
                    case "p":
                        letter = "unknown";

                        DnsResolveResult<PtrRecord> dnsResult = await ResolveDnsAsync<PtrRecord>(ip.GetReverseLookupDomain(), RecordType.Ptr, token);
                        if ((dnsResult == null) || ((dnsResult.ReturnCode != ReturnCode.NoError) && (dnsResult.ReturnCode != ReturnCode.NxDomain)))
                        {
                            break;
                        }

                        int ptrCheckedCount = 0;
                        foreach (PtrRecord ptrRecord in dnsResult.Records)
                        {
                            if (++ptrCheckedCount == 10)
                                break;

                            bool? isPtrMatch = await IsIpMatchAsync(ptrRecord.PointerDomainName, ip, 0, 0, token);
                            if (isPtrMatch.HasValue && isPtrMatch.Value)
                            {
                                if (letter == "unknown" || ptrRecord.PointerDomainName.IsSubDomainOf(domain))
                                {
                                    // use value, if first record or subdomain
                                    // but evaluate the other records
                                    letter = ptrRecord.PointerDomainName.ToString();
                                }
                                else if (ptrRecord.PointerDomainName.Equals(domain))
                                {
                                    // ptr equal domain --> best match, use it
                                    letter = ptrRecord.PointerDomainName.ToString();
                                    break;
                                }
                            }
                        }
                        break;
                    case "v":
                        letter = (ip.AddressFamily == AddressFamily.InterNetworkV6) ? "ip6" : "in-addr";
                        break;
                    case "h":
                        letter = HeloDomain?.ToString() ?? "unknown";
                        break;
                    case "c":
                        IPAddress address =
                            LocalIP
                            ?? NetworkInterface.GetAllNetworkInterfaces()
                                .Where(n => (n.OperationalStatus == OperationalStatus.Up) && (n.NetworkInterfaceType != NetworkInterfaceType.Loopback))
                                .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                                .Select(u => u.Address)
                                .FirstOrDefault(a => a.AddressFamily == ip.AddressFamily)
                            ?? ((ip.AddressFamily == AddressFamily.InterNetwork) ? IPAddress.Loopback : IPAddress.IPv6Loopback);
                        letter = address.ToString();
                        break;
                    case "r":
                        letter = LocalDomain?.ToString() ?? System.Net.Dns.GetHostName();
                        break;
                    case "t":
                        letter = ((int)(new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc) - DateTime.Now).TotalSeconds).ToString();
                        break;
                    default:
                        return null;
                }

                // only letter
                if (pattern.Value.Length == 4)
                    return letter;

                char[] delimiters = pattern.Groups["delimiter"].Value.ToCharArray();
                if (delimiters.Length == 0)
                    delimiters = new[] { '.' };

                string[] parts = letter.Split(delimiters);

                if (pattern.Groups["reverse"].Value == "r")
                    parts = parts.Reverse().ToArray();

                int count = Int32.MaxValue;
                if (!String.IsNullOrEmpty(pattern.Groups["count"].Value))
                {
                    count = Int32.Parse(pattern.Groups["count"].Value);
                }

                if (count < 1)
                    return null;

                count = Math.Min(count, parts.Length);

                return String.Join(".", parts, (parts.Length - count), count);
        }
    }
}


#endif
