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
using System.Text;
using System.Collections.Generic;
using System.IO;
using System.Net.Mail;
using System.Net.Mime;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using System.Threading.Tasks;
using System.Threading;
using System.Net.Security;
using System.Net;

namespace IPA.Cores.Basic;

public enum SmtpLanguage
{
    Japanese,
    Simpled_Chinese,
    Engligh,
}

public class SmtpCharsetList
{
    public readonly List<Encoding> CharsetList;

    public SmtpCharsetList(SmtpLanguage language)
    {
        CharsetList = new List<Encoding>();

        switch (language)
        {
            case SmtpLanguage.Japanese:
                //CharsetList.Add(Str.ISO88591Encoding);
                CharsetList.Add(Str.AsciiEncoding);
                //                    CharsetList.Add(Str.ISO2022JPEncoding);
                //                    CharsetList.Add(Str.GB2312Encoding);
                CharsetList.Add(Str.Utf8Encoding);
                break;

            case SmtpLanguage.Simpled_Chinese:
                //CharsetList.Add(Str.ISO88591Encoding);
                CharsetList.Add(Str.AsciiEncoding);
                //                    CharsetList.Add(Str.GB2312Encoding);
                //                    CharsetList.Add(Str.ISO2022JPEncoding);
                CharsetList.Add(Str.Utf8Encoding);
                break;

            case SmtpLanguage.Engligh:
                //CharsetList.Add(Str.ISO88591Encoding);
                CharsetList.Add(Str.AsciiEncoding);
                CharsetList.Add(Str.Utf8Encoding);
                //                    CharsetList.Add(Str.ISO2022JPEncoding);
                //                    CharsetList.Add(Str.GB2312Encoding);
                break;
        }
    }

    public Encoding GetAppropriateCharset(string text)
    {
        foreach (Encoding enc in CharsetList)
        {
            if (Str.IsSuitableEncodingForString(text, enc))
            {
                return enc;
            }
        }

        return Str.Utf8Encoding;
    }

    public static TransferEncoding GetTransferEncoding(Encoding enc)
    {
        if (Str.StrCmpi(enc.WebName, "iso-2022-jp"))
        {
            return TransferEncoding.SevenBit;
        }
        else if (Str.StrCmpi(enc.WebName, "us-ascii"))
        {
            return TransferEncoding.SevenBit;
        }
        else if (Str.StrCmpi(enc.WebName, "iso-8859-1"))
        {
            return TransferEncoding.SevenBit;
        }
        else
        {
            return TransferEncoding.Base64;
        }
    }

    public string NormalizeMailAddress(MailAddress src)
    {
        string name = src.DisplayName;
        string addr = src.Address;

        if (Str.IsAscii(name) && Str.InStr(name, "\"") == false)
        {
            return string.Format("\"{0}\" <{1}>", name, addr);
        }
        else
        {
            Encoding enc = GetAppropriateCharset(name);
            byte[] data = enc.GetBytes(name);
            string text = string.Format("=?{0}?B?{1}?= <{2}>", enc.WebName.ToUpperInvariant(),
                Convert.ToBase64String(data, Base64FormattingOptions.None), addr);
            return text;
        }
    }
}

public class SmtpBody
{
    public string XMailer = Env.FrameworkInfoString;
    public string MimeOLE = "Produced By " + Env.FrameworkInfoString;
    public string MSMailPriority = "Normal";
    public string MailPriority = "3";
    public MailAddress From;
    public MailAddress To;
    public MailAddress? ReplyTo = null;
    public string Subject;
    public string Body;
    public string BodyHtml = "";
    public SmtpCharsetList CharsetList;
    public List<Attachment> AttatchedFileList;
    public List<LinkedResource> LinkedResourceList;
    public List<MailAddress> CcList = new List<MailAddress>();
    public List<MailAddress> BccList = new List<MailAddress>();

    public SmtpBody(MailAddress from, MailAddress to, string subject, string body)
        : this(from, to, subject, body, new SmtpCharsetList(SmtpLanguage.Japanese))
    {
    }
    public SmtpBody(MailAddress from, MailAddress to, string subject, string body, SmtpCharsetList charsetList)
    {
        this.From = from;
        this.To = to;
        this.Subject = subject;
        this.Body = body;
        this.CharsetList = charsetList;
        this.AttatchedFileList = new List<Attachment>();
        this.LinkedResourceList = new List<LinkedResource>();
    }

    // リソースファイルを追加
    public void AddLinkedResourceFile(LinkedResource a)
    {
        this.LinkedResourceList.Add(a);
    }
    public void AddLinkedResourceFile(byte[] data, string contentType, string id)
    {
        MemoryStream ms = new MemoryStream(data);
        LinkedResource a = new LinkedResource(ms, contentType);
        if (Str.IsEmptyStr(contentType) == false)
        {
            a.ContentType = new ContentType(contentType);
        }
        if (Str.IsEmptyStr(id) == false)
        {
            a.ContentId = id;
        }
        a.TransferEncoding = TransferEncoding.Base64;

        AddLinkedResourceFile(a);
    }

    // 添付ファイルを追加
    public void AddAttachedFile(Attachment a)
    {
        this.AttatchedFileList.Add(a);
    }
    public void AddAttachedFile(byte[] data, string contentType, string name, string? id)
    {
        MemoryStream ms = new MemoryStream(data);
        Attachment a = new Attachment(ms, contentType);
        if (Str.IsEmptyStr(contentType) == false)
        {
            a.ContentType = new ContentType(contentType);
        }
        if (Str.IsEmptyStr(id) == false)
        {
            a.ContentId = id;
        }
        a.TransferEncoding = TransferEncoding.Base64;
        if (Str.IsEmptyStr(name) == false)
        {
            a.Name = name;
            if (Str.IsAscii(name))
            {
                a.NameEncoding = Str.AsciiEncoding;
            }
            else
            {
                a.NameEncoding = Str.Utf8Encoding;
            }
        }

        AddAttachedFile(a);
    }

    // 送信
    public bool SendSafe(SmtpConfig smtp)
    {
        try
        {
            Send(smtp);
            return true;
        }
        catch
        {
            return false;
        }
    }

    // 送信
    public async Task<bool> SendSafeAsync(SmtpConfig smtp, CancellationToken cancel = default)
    {
        try
        {
            await SendAsync(smtp, cancel);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task SendAsync(SmtpConfig smtp, CancellationToken cancel = default)
    {
        using SmtpClient c = new SmtpClient(smtp.SmtpServer, smtp.SmtpPort);
        c.DeliveryMethod = SmtpDeliveryMethod.Network;
        c.EnableSsl = smtp.UseSSL;

        if (smtp.Username._IsFilled() && smtp.Password._IsFilled())
        {
            c.UseDefaultCredentials = false;
            c.Credentials = new System.Net.NetworkCredential(smtp.Username, smtp.Password);
        }

        MailMessage mail = new MailMessage(CharsetList.NormalizeMailAddress(this.From),
            CharsetList.NormalizeMailAddress(this.To));

        Encoding bodyEnc = CharsetList.GetAppropriateCharset(this.Body);
        TransferEncoding bodyTran = SmtpCharsetList.GetTransferEncoding(bodyEnc);

        byte[] bodyData = bodyEnc.GetBytes(this.Body);
        MemoryStream ms = new MemoryStream(bodyData);

        AlternateView alt = new AlternateView(ms,
            new ContentType("text/plain; charset=" + bodyEnc.WebName));

        alt.TransferEncoding = bodyTran;

        if (Str.IsEmptyStr(this.Body) == false)
        {
            mail.AlternateViews.Add(alt);
        }
        mail.Body = "";
        mail.BodyEncoding = bodyEnc;

        // HTML メールの場合
        if (Str.IsEmptyStr(this.BodyHtml) == false)
        {
            Encoding htmlEnc = CharsetList.GetAppropriateCharset(this.BodyHtml);
            TransferEncoding htmlTran = SmtpCharsetList.GetTransferEncoding(htmlEnc);

            byte[] htmlData = htmlEnc.GetBytes(this.BodyHtml);
            ms = new MemoryStream(htmlData);

            AlternateView alt2 = new AlternateView(ms,
                new ContentType("text/html; charset=" + htmlEnc.WebName));

            // リソースファイル
            foreach (LinkedResource a in LinkedResourceList)
            {
                alt2.LinkedResources.Add(a);
            }

            mail.AlternateViews.Add(alt2);
        }

        // 添付ファイル
        foreach (Attachment a in AttatchedFileList)
        {
            mail.Attachments.Add(a);
        }

        Encoding subjectEnc = CharsetList.GetAppropriateCharset(this.Subject);
        byte[] subjectData = subjectEnc.GetBytes(this.Subject);
        string subjectText = string.Format("=?{0}?B?{1}?=", subjectEnc.WebName.ToUpperInvariant(),
            Convert.ToBase64String(subjectData, Base64FormattingOptions.None));
        if (Str.IsAscii(this.Subject))
        {
            subjectText = this.Subject;
        }

        mail.Subject = subjectText;

        if (this.ReplyTo != null)
        {
            //                mail.ReplyTo = this.ReplyTo;
            mail.ReplyToList.Add(this.ReplyTo);
        }

        foreach (MailAddress cc in CcList)
        {
            mail.CC.Add(cc);
        }

        foreach (MailAddress bcc in BccList)
        {
            mail.Bcc.Add(bcc);
        }

        mail.Headers.Add("X-Mailer", XMailer);
        mail.Headers.Add("X-MSMail-Priority", MSMailPriority);
        mail.Headers.Add("X-Priority", MailPriority);
        mail.Headers.Add("X-MimeOLE", MimeOLE);

        // SSL 証明書をチェック いたしません！！
        RemoteCertificateValidationCallback? sslCallbackBackup = ServicePointManager.ServerCertificateValidationCallback;

        if (c.EnableSsl)
        {
            ServicePointManager.ServerCertificateValidationCallback = (a, b, c, d) => true;
        }

        try
        {
            await c.SendMailAsync(mail);
        }
        finally
        {
            if (c.EnableSsl)
            {
                try
                {
                    ServicePointManager.ServerCertificateValidationCallback = sslCallbackBackup;
                }
                catch { }
            }
        }
    }

    public void Send(SmtpConfig smtp, CancellationToken cancel = default) => SendAsync(smtp, cancel)._GetResult();
}

public class SmtpBassicSettings : INormalizable
{
    public string MailFrom = "";
    public List<string> MailToList = new List<string>();

    public void Normalize()
    {
        if (MailFrom._IsEmpty()) MailFrom = "from@example.org";
        if (MailToList.Count == 0) MailToList.Add("to@example.org");
    }
}

public class SmtpClientSettings : INormalizable
{
    public string SmtpServer = "";
    public int SmtpPort = 0;
    public bool UseSSL;
    public string Username = "";
    public string Password = "";

    public SmtpConfig ToSmtpConfig() => new SmtpConfig(this.SmtpServer, this.SmtpPort, this.UseSSL, this.Username, this.Password);
    public static implicit operator SmtpConfig(SmtpClientSettings r) => r.ToSmtpConfig();

    public void Normalize()
    {
        if (this.SmtpServer._IsEmpty()) this.SmtpServer = "smtp-server-hostname-here";
        if (this.SmtpPort == 0) this.SmtpPort = Consts.Ports.SmtpSubmission;
    }
}

public class SmtpConfig
{
    public string SmtpServer { get; }
    public int SmtpPort { get; }
    public bool UseSSL { get; }
    public string Username { get; }
    public string Password { get; }

    public SmtpConfig(string smtpServer, int smtpPort = Consts.Ports.Smtp, bool useSsl = false, string? username = null, string? password = null)
    {
        this.SmtpServer = smtpServer;
        this.SmtpPort = smtpPort;
        this.UseSSL = useSsl;
        this.Username = username._NonNull();
        this.Password = password._NonNull();
    }
}

public static class SmtpUtil
{
    public static async Task<bool> SendAsync(SmtpConfig config, string from, string to, string subject, string body, bool noException = false, CancellationToken cancel = default)
    {
        try
        {
            return await SendAsync(config, new MailAddress(from), new MailAddress(to), subject, body, noException, cancel);
        }
        catch (Exception ex)
        {
            if (noException == false)
            {
                throw;
            }

            ex._Debug();

            return false;
        }
    }
    public static bool Send(SmtpConfig config, string from, string to, string subject, string body, bool noException = false, CancellationToken cancel = default)
        => SendAsync(config, from, to, subject, body, noException, cancel)._GetResult();

    public static async Task<bool> SendAsync(SmtpConfig config, MailAddress from, MailAddress to, string subject, string body, bool noException = false, CancellationToken cancel = default)
    {
        try
        {
            SmtpBody sm = new SmtpBody(from, to, subject, body);

            await sm.SendAsync(config, cancel);

            return true;
        }
        catch (Exception ex)
        {
            if (noException == false)
            {
                throw;
            }

            ex._Debug();

            return false;
        }
    }
    public static bool Send(SmtpConfig config, MailAddress from, MailAddress to, string subject, string body, bool noException = false, CancellationToken cancel = default)
        => SendAsync(config, from, to, subject, body, noException, cancel)._GetResult();
}

public class SmtpLogRouteSettings
{
    public string SmtpServer { get; }
    public bool SmtpUseSsl { get; }
    public string SmtpUsername { get; }
    public string SmtpPassword { get; }
    public string MailFrom { get; }
    public string MailTo { get; }
    public int MaxLines { get; }

    public const int DefaultMaxLines = 20000;

    public SmtpLogRouteSettings(string smtpServer, bool smtpUseSsl, string smtpUsername, string smtpPassword, string mailFrom, string mailTo, int maxLines = DefaultMaxLines)
    {
        SmtpServer = smtpServer;
        SmtpUseSsl = smtpUseSsl;
        SmtpUsername = smtpUsername;
        SmtpPassword = smtpPassword;
        MailFrom = mailFrom;
        MailTo = mailTo;
        MaxLines = Math.Max(maxLines, 1);
    }
}

public class SmtpLogRoute : LogRouteBase
{
    readonly CriticalSection<SmtpLogRoute> Lock = new CriticalSection<SmtpLogRoute>();

    public SmtpLogRouteSettings Settings { get; }

    public GetMyIpInfoResult? MyIpInfo = null;

    public IPAddress? MyLocalIp = null;

    List<string> Lines2 = new List<string>();
    List<string> Lines = new List<string>();

    public SmtpLogRoute(string kind, LogPriority minimalPriority, SmtpLogRouteSettings settings) : base(kind, minimalPriority)
    {
        this.Settings = settings;
    }

    public override async Task FlushAsync(bool halfFlush = false, CancellationToken cancel = default)
    {
        List<string> linesToSend;
        List<string> lines2ToSend;

        try
        {
            if (MyLocalIp == null)
            {
                MyLocalIp = await GetMyPrivateIpNativeUtil.GetMyPrivateIpAsync(IPVersion.IPv4);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.ToString());
        }

        if (MyIpInfo == null)
        {
            try
            {
                await using GetMyIpClient c = new GetMyIpClient();

                MyIpInfo = await c.GetMyIpInfoAsync(IPVersion.IPv4, cancel);
            }
            catch
            {
            }
        }

        string globalInfo = "(Unknown)";
        if (MyIpInfo != null)
        {
            if (MyIpInfo.GlobalFqdn._IsSamei(MyIpInfo.GlobalIpAddress.ToString()))
            {
                globalInfo = MyIpInfo.GlobalFqdn;
            }
            else
            {
                globalInfo = $"{MyIpInfo.GlobalFqdn} - {MyIpInfo.GlobalIpAddress}";
            }
        }

        lock (this.Lock)
        {
            if (this.Lines.Count == 0 && this.Lines2.Count == 0) return;

            linesToSend = this.Lines;
            lines2ToSend = this.Lines2;

            this.Lines = new List<string>();
            this.Lines2 = new List<string>();
        }

        string cmdName = CoresLib.Report_CommandName;
        if (cmdName._IsEmpty())
        {
            cmdName = "Unknown";
        }

        string resultStr = CoresLib.Report_SimpleResult._OneLine();
        if (resultStr._IsEmpty())
        {
            resultStr = "Ok";
        }

        StringWriter w = new StringWriter();

        w.WriteLine($"Reported: {DtOffsetNow._ToDtStr()}");
        w.WriteLine($"Program: {CoresLib.AppName} Built: {Env.BuildTimeStamp._ToDtStr()}");
        w.WriteLine($"Hostname: {Env.DnsFqdnHostName}");
        w.WriteLine($"Global: {globalInfo}, Local: {MyLocalIp}");
        w.WriteLine($"Command: {cmdName}, Result: {(CoresLib.Report_HasError ? "*Error* - " : "OK - ")}{resultStr}");

        if (lines2ToSend.Count >= 1)
        {
            w.WriteLine("=====================");
            w.WriteLine();

            lines2ToSend.ForEach(x => w.WriteLine(x));

            w.WriteLine();
        }

        if (linesToSend.Count >= 1)
        {
            w.WriteLine("--------------------");
            w.WriteLine();

            linesToSend.ForEach(x => w.WriteLine(x));

            w.WriteLine();
        }

        w.WriteLine("--------------------");
        EnvInfoSnapshot snapshot = new EnvInfoSnapshot();
        snapshot.CommandLine = "";
        w.WriteLine($"Program Details: {snapshot._GetObjectDump()}");

        w.WriteLine();

        string subject = $"Report - {cmdName}{(CoresLib.Report_HasError ? " *Error*" : "")} - {linesToSend.Count} lines - {Env.DnsHostName} - {MyLocalIp} ({globalInfo}): {resultStr._NormalizeSoftEther(true)._TruncStrEx(60)}";

        var hostAndPort = this.Settings.SmtpServer._ParseHostnaneAndPort(Consts.Ports.Smtp);
        SmtpConfig cfg = new SmtpConfig(hostAndPort.Item1, hostAndPort.Item2, this.Settings.SmtpUseSsl, this.Settings.SmtpUsername, this.Settings.SmtpPassword);

        Console.WriteLine("Report Subject: " + subject);

        try
        {
            Console.WriteLine($"SMTP Log Sending to '{this.Settings.MailTo}' ...");

            await TaskUtil.RetryAsync(async () =>
            {
                await SmtpUtil.SendAsync(cfg, this.Settings.MailFrom, this.Settings.MailTo, subject, w.ToString(), false, cancel);

                return 0;
            }, 1000, 3, cancel: cancel, randomInterval: true);
            Console.WriteLine("SMTP Log Sent.");
        }
        catch (Exception ex)
        {
            Console.WriteLine("SMTP Log Send Error: " + ex.ToString());
        }
    }

    public override void ReceiveLog(LogRecord record, string kind)
    {
        if (record.Tag._IsSamei("boottime") == false)
        {
            string[] lines = record.ConsolePrintableString._GetLines(false, singleLineAtLeast: true);

            lock (this.Lock)
            {
                foreach (var line in lines)
                {
                    string s = $"[{record.TimeStamp.LocalDateTime._ToDtStr()}] {line}";

                    if (record.Flags.Bit(LogFlags.Heading) || record.Priority >= LogPriority.Error)
                    {
                        // 重要なメッセージはトップにも表示
                        this.Lines2.Add(s);
                    }

                    if (this.Lines.Count < this.Settings.MaxLines)
                    {
                        this.Lines.Add(s);
                    }
                }
            }
        }
    }

    protected override async Task CleanupImplAsync(Exception? ex)
    {
        try
        {
            await this.FlushAsync(false);
        }
        finally
        {
            await base.CleanupImplAsync(ex);
        }
    }
}
