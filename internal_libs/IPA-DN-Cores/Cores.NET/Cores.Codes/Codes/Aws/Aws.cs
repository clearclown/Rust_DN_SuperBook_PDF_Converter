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
// AWS SDK Client

#if CORES_CODES_AWS

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

using IPA.Cores.Codes;
using IPA.Cores.Helper.Codes;
using static IPA.Cores.Globals.Codes;

using Amazon;
using Amazon.Runtime;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;

namespace IPA.Cores.Codes;

public class AwsSnsSettings
{
    public string RegionEndPointName { get; }
    public string AccessKeyId { get; }
    public string SecretAccessKey { get; }
    public string DefaultCountryCode { get; }

    public AwsSnsSettings(string regionEndPointName, string accessKeyId, string secretAccessKey, string defaultCountryCode = Consts.Strings.SmsDefaultCountryCode)
    {
        this.RegionEndPointName = regionEndPointName;
        this.AccessKeyId = accessKeyId;
        this.SecretAccessKey = secretAccessKey;
        this.DefaultCountryCode = defaultCountryCode;
    }
}

public class AwsSns : AsyncService
{
    public AwsSnsSettings Settings { get; }

    public AwsSns(AwsSnsSettings settings)
    {
        this.Settings = settings;
    }

    public static string NormalizePhoneNumber(string phoneNumber, string defaultCountryCode = Consts.Strings.SmsDefaultCountryCode)
    {
        string original = phoneNumber;

        if (original._IsEmpty())
        {
            throw new CoresException("The phone number is empty.");
        }

        phoneNumber = phoneNumber.Trim();
        Str.NormalizeString(ref phoneNumber, true, true, false, false);
        phoneNumber = phoneNumber._ReplaceStr(" ", "");

        if (phoneNumber.StartsWith("0"))
        {
            phoneNumber = defaultCountryCode + phoneNumber._Slice(1);
        }

        if (phoneNumber.StartsWith("+") == false)
        {
            phoneNumber = "+" + phoneNumber;
        }

        string pn1 = phoneNumber.Substring(0, 1);
        string pn2 = phoneNumber.Substring(1);

        pn2 = pn2._ReplaceStr("-", "");
        pn2 = pn2._ReplaceStr("/", "");
        pn2 = pn2._ReplaceStr("+", "");
        pn2 = pn2._ReplaceStr("#", "");
        pn2 = pn2._ReplaceStr("*", "");

        if (pn2.Where(x => !(x >= '0' && x <= '9')).Any())
        {
            throw new CoresException($"The phone number '{original}' has invalid characters.");
        }

        return pn1 + pn2;
    }

    public async Task SendAsync(string message, string phoneNumber, CancellationToken cancel = default)
    {
        using AmazonSimpleNotificationServiceClient c = new AmazonSimpleNotificationServiceClient(this.Settings.AccessKeyId, this.Settings.SecretAccessKey, RegionEndpoint.GetBySystemName(this.Settings.RegionEndPointName));

        PublishRequest req = new PublishRequest();
        req.Message = message;
        req.PhoneNumber = NormalizePhoneNumber(phoneNumber, this.Settings.DefaultCountryCode);
        //req.PhoneNumber._Print();

        var response = await c.PublishAsync(req, cancel);
        //response.MessageId._Print();
    }
}

#endif

