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

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

namespace IPA.Cores.Basic.DnsLib;

/// <summary>
///   Add record action
/// </summary>
public class AddRecordUpdate : UpdateBase
{
    /// <summary>
    ///   Record which should be added
    /// </summary>
    public DnsRecordBase Record { get; }

    internal AddRecordUpdate() { }

    /// <summary>
    ///   Creates a new instance of the AddRecordUpdate
    /// </summary>
    /// <param name="record"> Record which should be added </param>
    public AddRecordUpdate(DnsRecordBase record)
        : base(record.Name, record.RecordType, record.RecordClass, record.TimeToLive)
    {
        Record = record;
    }

    internal override void ParseRecordData(ReadOnlySpan<byte> resultData, int startPosition, int length) { }

    internal override string RecordDataToString()
    {
        return Record?.RecordDataToString();
    }

    protected internal override int MaximumRecordDataLength => Record.MaximumRecordDataLength;

    protected internal override void EncodeRecordData(Span<byte> messageData, int offset, ref int currentPosition, Dictionary<DomainName, ushort> domainNames, bool useCanonical)
    {
        Record.EncodeRecordData(messageData, offset, ref currentPosition, domainNames, useCanonical);
    }
}

/// <summary>
///   Delete all records action
/// </summary>
public class DeleteAllRecordsUpdate : DeleteRecordUpdate
{
    internal DeleteAllRecordsUpdate() { }

    /// <summary>
    ///   Creates a new instance of the DeleteAllRecordsUpdate class
    /// </summary>
    /// <param name="name"> Name of records, that should be deleted </param>
    public DeleteAllRecordsUpdate(DomainName name)
        : base(name, RecordType.Any) { }
}

/// <summary>
///   Delete record action
/// </summary>
public class DeleteRecordUpdate : UpdateBase
{
    /// <summary>
    ///   Record that should be deleted
    /// </summary>
    public DnsRecordBase Record { get; }

    internal DeleteRecordUpdate() { }

    /// <summary>
    ///   Creates a new instance of the DeleteRecordUpdate class
    /// </summary>
    /// <param name="name"> Name of the record that should be deleted </param>
    /// <param name="recordType"> Type of the record that should be deleted </param>
    public DeleteRecordUpdate(DomainName name, RecordType recordType)
        : base(name, recordType, RecordClass.Any, 0) { }

    /// <summary>
    ///   Creates a new instance of the DeleteRecordUpdate class
    /// </summary>
    /// <param name="record"> Record that should be deleted </param>
    public DeleteRecordUpdate(DnsRecordBase record)
        : base(record.Name, record.RecordType, RecordClass.None, 0)
    {
        Record = record;
    }

    internal override void ParseRecordData(ReadOnlySpan<byte> resultData, int startPosition, int length) { }

    internal override string RecordDataToString()
    {
        return Record?.RecordDataToString();
    }

    protected internal override int MaximumRecordDataLength => Record?.MaximumRecordDataLength ?? 0;

    protected internal override void EncodeRecordData(Span<byte> messageData, int offset, ref int currentPosition, Dictionary<DomainName, ushort> domainNames, bool useCanonical)
    {
        Record?.EncodeRecordData(messageData, offset, ref currentPosition, domainNames, useCanonical);
    }
}

/// <summary>
///   <para>Dynamic DNS update message</para>
///   <para>
///     Defined in
///     <see cref="!:http://tools.ietf.org/html/rfc2136">RFC 2136</see>
///   </para>
/// </summary>
public class DnsUpdateMessage : DnsMessageBase
{
    /// <summary>
    ///   Parses a the contents of a byte array as DnsUpdateMessage
    /// </summary>
    /// <param name="data">Buffer, that contains the message data</param>
    /// <returns>A new instance of the DnsUpdateMessage class</returns>
    public static DnsUpdateMessage Parse(byte[] data)
    {
        return Parse<DnsUpdateMessage>(data);
    }

    /// <summary>
    ///   Creates a new instance of the DnsUpdateMessage class
    /// </summary>
    public DnsUpdateMessage()
    {
        OperationCode = OperationCode.Update;
    }

    private List<PrequisiteBase> _prequisites;
    private List<UpdateBase> _updates;

    /// <summary>
    ///   Gets or sets the zone name
    /// </summary>
    public DomainName ZoneName
    {
        get { return Questions.Count > 0 ? Questions[0].Name : null; }
        set { Questions = new List<DnsQuestion>() { new DnsQuestion(value, RecordType.Soa, RecordClass.INet) }; }
    }

    /// <summary>
    ///   Gets or sets the entries in the prerequisites section
    /// </summary>
    public List<PrequisiteBase> Prequisites
    {
        get { return _prequisites ?? (_prequisites = new List<PrequisiteBase>()); }
        set { _prequisites = value; }
    }

    /// <summary>
    ///   Gets or sets the entries in the update section
    /// </summary>
    public List<UpdateBase> Updates
    {
        get { return _updates ?? (_updates = new List<UpdateBase>()); }
        set { _updates = value; }
    }

    /// <summary>
    ///   Creates a new instance of the DnsUpdateMessage as response to the current instance
    /// </summary>
    /// <returns>A new instance of the DnsUpdateMessage as response to the current instance</returns>
    public DnsUpdateMessage CreateResponseInstance()
    {
        DnsUpdateMessage result = new DnsUpdateMessage()
        {
            TransactionID = TransactionID,
            IsEDnsEnabled = IsEDnsEnabled,
            IsQuery = false,
            OperationCode = OperationCode,
            Questions = new List<DnsQuestion>(Questions),
        };

        if (IsEDnsEnabled)
        {
            result.EDnsOptions.Version = EDnsOptions.Version;
            result.EDnsOptions.UdpPayloadSize = EDnsOptions.UdpPayloadSize;
        }

        return result;
    }

    internal override bool IsTcpUsingRequested => false;

    internal override bool IsTcpResendingRequested => false;

    internal override bool IsTcpNextMessageWaiting(bool isSubsequentResponseMessage)
    {
        return false;
    }

    protected override void PrepareEncoding()
    {
        AnswerRecords = Prequisites?.Cast<DnsRecordBase>().ToList() ?? new List<DnsRecordBase>();
        AuthorityRecords = Updates?.Cast<DnsRecordBase>().ToList() ?? new List<DnsRecordBase>();
    }

    protected override void FinishParsing()
    {
        Prequisites =
            AnswerRecords.ConvertAll<PrequisiteBase>(
                record =>
                {
                    if ((record.RecordClass == RecordClass.Any) && (record.RecordDataLength == 0))
                    {
                        return new RecordExistsPrequisite(record.Name, record.RecordType);
                    }
                    else if (record.RecordClass == RecordClass.Any)
                    {
                        return new RecordExistsPrequisite(record);
                    }
                    else if ((record.RecordClass == RecordClass.None) && (record.RecordDataLength == 0))
                    {
                        return new RecordNotExistsPrequisite(record.Name, record.RecordType);
                    }
                    else if ((record.RecordClass == RecordClass.Any) && (record.RecordType == RecordType.Any))
                    {
                        return new NameIsInUsePrequisite(record.Name);
                    }
                    else if ((record.RecordClass == RecordClass.None) && (record.RecordType == RecordType.Any))
                    {
                        return new NameIsNotInUsePrequisite(record.Name);
                    }
                    else
                    {
                        return null;
                    }
                }).Where(prequisite => (prequisite != null)).ToList();

        Updates =
            AuthorityRecords.ConvertAll<UpdateBase>(
                record =>
                {
                    if (record.TimeToLive != 0)
                    {
                        return new AddRecordUpdate(record);
                    }
                    else if ((record.RecordType == RecordType.Any) && (record.RecordClass == RecordClass.Any) && (record.RecordDataLength == 0))
                    {
                        return new DeleteAllRecordsUpdate(record.Name);
                    }
                    else if ((record.RecordClass == RecordClass.Any) && (record.RecordDataLength == 0))
                    {
                        return new DeleteRecordUpdate(record.Name, record.RecordType);
                    }
                    else if (record.RecordClass == RecordClass.None)
                    {
                        return new DeleteRecordUpdate(record);
                    }
                    else
                    {
                        return null;
                    }
                }).Where(update => (update != null)).ToList();
    }
}

/// <summary>
///   Prequisite, that a name exists
/// </summary>
public class NameIsInUsePrequisite : PrequisiteBase
{
    internal NameIsInUsePrequisite() { }

    /// <summary>
    ///   Creates a new instance of the NameIsInUsePrequisite class
    /// </summary>
    /// <param name="name"> Name that should be checked </param>
    public NameIsInUsePrequisite(DomainName name)
        : base(name, RecordType.Any, RecordClass.Any, 0) { }

    internal override void ParseRecordData(ReadOnlySpan<byte> resultData, int startPosition, int length) { }

    protected internal override int MaximumRecordDataLength => 0;

    protected internal override void EncodeRecordData(Span<byte> messageData, int offset, ref int currentPosition, Dictionary<DomainName, ushort> domainNames, bool useCanonical) { }
}

/// <summary>
///   Prequisite, that a name does not exist
/// </summary>
public class NameIsNotInUsePrequisite : PrequisiteBase
{
    internal NameIsNotInUsePrequisite() { }

    /// <summary>
    ///   Creates a new instance of the NameIsNotInUsePrequisite class
    /// </summary>
    /// <param name="name"> Name that should be checked </param>
    public NameIsNotInUsePrequisite(DomainName name)
        : base(name, RecordType.Any, RecordClass.None, 0) { }

    internal override void ParseRecordData(ReadOnlySpan<byte> resultData, int startPosition, int length) { }

    protected internal override int MaximumRecordDataLength => 0;

    protected internal override void EncodeRecordData(Span<byte> messageData, int offset, ref int currentPosition, Dictionary<DomainName, ushort> domainNames, bool useCanonical) { }
}

/// <summary>
///   Base class for prequisites of dynamic dns updates
/// </summary>
public abstract class PrequisiteBase : DnsRecordBase
{
    internal PrequisiteBase() { }

    protected PrequisiteBase(DomainName name, RecordType recordType, RecordClass recordClass, int timeToLive)
        : base(name, recordType, recordClass, timeToLive) { }

    internal override string RecordDataToString()
    {
        return null;
    }

    internal override void ParseRecordData(DomainName origin, string[] stringRepresentation)
    {
        throw new NotSupportedException();
    }
}

/// <summary>
///   Prequisite, that a record exists
/// </summary>
public class RecordExistsPrequisite : PrequisiteBase
{
    /// <summary>
    ///   Record that should exist
    /// </summary>
    public DnsRecordBase Record { get; }

    internal RecordExistsPrequisite() { }

    /// <summary>
    ///   Creates a new instance of the RecordExistsPrequisite class
    /// </summary>
    /// <param name="name"> Name of record that should be checked </param>
    /// <param name="recordType"> Type of record that should be checked </param>
    public RecordExistsPrequisite(DomainName name, RecordType recordType)
        : base(name, recordType, RecordClass.Any, 0) { }

    /// <summary>
    ///   Creates a new instance of the RecordExistsPrequisite class
    /// </summary>
    /// <param name="record"> tecord that should be checked </param>
    public RecordExistsPrequisite(DnsRecordBase record)
        : base(record.Name, record.RecordType, record.RecordClass, 0)
    {
        Record = record;
    }

    internal override void ParseRecordData(ReadOnlySpan<byte> resultData, int startPosition, int length) { }

    protected internal override int MaximumRecordDataLength => Record?.MaximumRecordDataLength ?? 0;

    protected internal override void EncodeRecordData(Span<byte> messageData, int offset, ref int currentPosition, Dictionary<DomainName, ushort> domainNames, bool useCanonical)
    {
        Record?.EncodeRecordData(messageData, offset, ref currentPosition, domainNames, useCanonical);
    }
}

/// <summary>
///   Prequisite, that a record does not exist
/// </summary>
public class RecordNotExistsPrequisite : PrequisiteBase
{
    internal RecordNotExistsPrequisite() { }

    /// <summary>
    ///   Creates a new instance of the RecordNotExistsPrequisite class
    /// </summary>
    /// <param name="name"> Name of record that should be checked </param>
    /// <param name="recordType"> Type of record that should be checked </param>
    public RecordNotExistsPrequisite(DomainName name, RecordType recordType)
        : base(name, recordType, RecordClass.None, 0) { }

    internal override void ParseRecordData(ReadOnlySpan<byte> resultData, int startPosition, int length) { }

    protected internal override int MaximumRecordDataLength => 0;

    protected internal override void EncodeRecordData(Span<byte> messageData, int offset, ref int currentPosition, Dictionary<DomainName, ushort> domainNames, bool useCanonical) { }
}

/// <summary>
///   Base update action of dynamic dns update
/// </summary>
public abstract class UpdateBase : DnsRecordBase
{
    internal UpdateBase() { }

    protected UpdateBase(DomainName name, RecordType recordType, RecordClass recordClass, int timeToLive)
        : base(name, recordType, recordClass, timeToLive) { }

    internal override void ParseRecordData(DomainName origin, string[] stringRepresentation)
    {
        throw new NotSupportedException();
    }
}


#endif
