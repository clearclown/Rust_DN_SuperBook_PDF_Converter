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

#if CORES_BASIC_WEBAPP || CORES_BASIC_HTTPSERVER

using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Security.Authentication;
using System.Net;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

namespace IPA.Cores.Basic;

public sealed class MsLogger : ILogger, IDisposable
{
    public MsLoggerProvider Provider { get; }
    public string CategoryName { get; }
    public string CategoryNameShort { get; }

    string? CurrentTransactionId = null;

    public MsLogger(MsLoggerProvider provider, string categoryName)
    {
        this.Provider = provider;
        this.CategoryName = categoryName._NonNullTrim();
        this.CategoryNameShort = this.CategoryName.Split('.', StringSplitOptions.RemoveEmptyEntries).LastOrDefault()._NonNullTrim();
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        string? msg = null;
        try
        {
            msg = formatter(state, exception);
        }
        catch { }

        if (exception != null)
        {
            msg += " Exception: " + exception.ToString();
        }

        var obj = new MsLogData()
        {
            Category = this.CategoryNameShort,
            TranscationId = this.CurrentTransactionId,
            EventId = eventId.Id,
            Message = msg,
            Data = msg._IsEmpty() ? state : default,
        };
        LocalLogRouter.PostAccessLog(obj, this.Provider.Tag);

        if (exception != null)
        {
            //Dbg.WriteLine($"{this.CategoryNameShort} Error: {msg}");
        }
    }

    public bool IsEnabled(LogLevel logLevel) => true;

    class TransactionScope : IDisposable
    {
        MsLogger Logger;

        public TransactionScope(MsLogger logger, string id)
        {
            this.Logger = logger;
            Logger.CurrentTransactionId = id;
        }
        public void Dispose()
        {
            Logger.CurrentTransactionId = null;
        }
    }

    public IDisposable BeginScope<TState>(TState state)
    {
        string transactionId = state?.ToString() ?? "";
        return new TransactionScope(this, transactionId);
    }

    public void Dispose() { }
}

public sealed class MsLoggerProvider : ILoggerProvider
{
    public LogPriority Priority { get; }
    public LogFlags Flags { get; }
    public string Tag { get; }

    public MsLoggerProvider(LogPriority priority = LogPriority.Info, LogFlags flags = LogFlags.None, string tag = LogTag.Kestrel)
    {
        this.Priority = priority;
        this.Flags = flags;
        this.Tag = tag;
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new MsLogger(this, categoryName);
    }

    public void Dispose() { }
}

#endif // CORES_BASIC_WEBAPP

