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
using System.IO;
using System.Net.Mail;
using System.Net.Mime;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

#pragma warning disable 0618

namespace IPA.Cores.Basic;

public enum SendMailVersion
{
    Ver2_With_NetMail,
}

public class SendMail
{
    string smtpServer;
    public string SmtpServer
    {
        get { return smtpServer; }
        set { smtpServer = value; }
    }

    SendMailVersion version;
    public SendMailVersion Version
    {
        get { return version; }
        set { version = value; }
    }

    public string Username = "";
    public string Password = "";

    public int SmtpPort = 25;

    public const SendMailVersion DefaultVersion = SendMailVersion.Ver2_With_NetMail;

    public static readonly string DefaultMailer = Env.FrameworkInfoString;
    public static readonly string DefaultMimeOLE = "Produced By " + Env.FrameworkInfoString;
    public const string DefaultPriority = "3";
    public const string DefaultMSMailPriority = "Normal";
    public const string DefaultTransferEncoding = "7bit";
    private static Encoding defaultEncoding = Str.Utf8Encoding;
    public static Encoding DefaultEncoding
    {
        get { return SendMail.defaultEncoding; }
    }

    string header_mailer = DefaultMailer;
    public string Mailer
    {
        get { return header_mailer; }
        set { header_mailer = value; }
    }
    string header_mimeole = DefaultMimeOLE;
    public string MimeOLE
    {
        get { return header_mimeole; }
        set { header_mimeole = value; }
    }
    string header_priority = DefaultPriority;
    public string Priority
    {
        get { return header_priority; }
        set { header_priority = value; }
    }
    string header_msmail_priority = DefaultMSMailPriority;
    public string MsMailPriority
    {
        get { return header_msmail_priority; }
        set { header_msmail_priority = value; }
    }
    string header_transfer_encoding = DefaultTransferEncoding;
    public string TransferEncoding
    {
        get { return header_transfer_encoding; }
        set { header_transfer_encoding = value; }
    }
    Encoding encoding = DefaultEncoding;
    public Encoding Encoding
    {
        get { return encoding; }
        set { encoding = value; }
    }

    public SendMail(string smtpServer, SendMailVersion version = DefaultVersion, string username = "", string password = "")
    {
        this.smtpServer = smtpServer;
        this.version = version;
        this.Username = username;
        this.Password = password;
    }

    public bool Send(string from, string to, string subject, string body, bool debugError = false)
    {
        return Send(new MailAddress(from), new MailAddress(to), subject, body, debugError);
    }

    public bool Send(MailAddress from, MailAddress to, string subject, string body, bool debugError = false)
    {
        try
        {
            switch (this.version)
            {
                case SendMailVersion.Ver2_With_NetMail:
                    send2(from, to, subject, body);
                    break;

                default:
                    throw new CoresLibException($"invalid version: {this.version}");
            }
        }
        catch (Exception ex)
        {
            if (debugError)
            {
                $"Error: from = {from.ToString()}  to = {to.ToString()}"._Debug();
                ex._Debug();
            }
            return false;
        }

        return true;
    }

    void send2(MailAddress from, MailAddress to, string subject, string body)
    {
        Encoding encoding = this.encoding;
        TransferEncoding tranEnc = System.Net.Mime.TransferEncoding.SevenBit;

        if (Str.IsSuitableEncodingForString(subject, Str.AsciiEncoding) &&
            Str.IsSuitableEncodingForString(body, Str.AsciiEncoding))
        {
            encoding = Str.AsciiEncoding;
            tranEnc = System.Net.Mime.TransferEncoding.SevenBit;
        }
        else
        {
            if (!Str.IsSuitableEncodingForString(subject, encoding) || !Str.IsSuitableEncodingForString(body, encoding))
            {
                encoding = Str.Utf8Encoding;
                tranEnc = System.Net.Mime.TransferEncoding.Base64;
            }
        }

        using SmtpClient c = new SmtpClient(this.smtpServer);
        c.DeliveryMethod = SmtpDeliveryMethod.Network;
        c.EnableSsl = false;
        c.Port = this.SmtpPort;

        if (Str.IsEmptyStr(this.Username) == false && Str.IsEmptyStr(this.Password) == false)
        {
            c.UseDefaultCredentials = false;
            c.Credentials = new System.Net.NetworkCredential(this.Username, this.Password);
        }

        System.Net.Mail.MailMessage mail = new System.Net.Mail.MailMessage(from, to);

        byte[] buffer = encoding.GetBytes(body);

        MemoryStream mem = new MemoryStream(buffer);

        AlternateView alt = new AlternateView(mem, new System.Net.Mime.ContentType("text/plain; charset=" + encoding.WebName));

        alt.TransferEncoding = tranEnc;

        mail.AlternateViews.Add(alt);
        mail.Body = "";

        byte[] sub = encoding.GetBytes(subject);
        string subjectText = string.Format("=?{0}?B?{1}?=", encoding.WebName.ToUpperInvariant(),
            Convert.ToBase64String(sub, Base64FormattingOptions.None));

        mail.Subject = subjectText;

        mail.Headers.Add("X-Mailer", this.header_mailer);
        mail.Headers.Add("X-MSMail-Priority", this.header_msmail_priority);
        mail.Headers.Add("X-Priority", this.header_priority);
        mail.Headers.Add("X-MimeOLE", this.header_mimeole);

        c.Send(mail);
    }

    public static MailAddress NewMailAddress(string address, string displayName)
    {
        return NewMailAddress(address, displayName, SendMail.DefaultEncoding);
    }

    public static MailAddress NewMailAddress(string address, string displayName, Encoding encoding)
    {
        MailAddress a = new MailAddress(address, displayName, encoding);

        return a;
    }
}

