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
using System.Net.Mail;

using IPA.Cores.Basic;
using IPA.Cores.Basic.Legacy;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

namespace IPA.Cores.Basic;

public class MailUser : IComparable<MailUser>
{
    public string MailAddress;
    public string Company;
    public string FullName;
    public CoreLanguageClass Language;
    public List<KeyValuePair<string, string>> ParamList = new List<KeyValuePair<string, string>>();

    public MailUser(string mail, string name, string company, string language, List<KeyValuePair<string, string>> paramList)
    {
        this.MailAddress = mail;
        this.Company = company;
        this.FullName = name;
        this.Language = CoreLanguageList.GetLanguageClassByName(language);
        if (paramList == null)
        {
            this.ParamList = new List<KeyValuePair<string, string>>();
        }
        else
        {
            this.ParamList = paramList;
        }

        normalize();
    }

    public KeyValuePair<string, string> GetKeyPair(string paramName)
    {
        foreach (KeyValuePair<string, string> kv in ParamList)
        {
            if (Str.StrCmpi(kv.Key, paramName))
            {
                return kv;
            }
        }

        return new KeyValuePair<string, string>(paramName, "");
    }

    public string this[string paramName]
    {
        get
        {
            foreach (KeyValuePair<string, string> kv in ParamList)
            {
                if (Str.StrCmpi(kv.Key, paramName))
                {
                    return kv.Value;
                }
            }

            return "";
        }

        set
        {
            int i = 0;
            foreach (KeyValuePair<string, string> kv in ParamList)
            {
                if (Str.StrCmpi(kv.Key, paramName))
                {
                    ParamList.Remove(kv);
                    ParamList.Insert(i, new KeyValuePair<string, string>(paramName, value));

                    return;
                }

                i++;
            }

            ParamList.Add(new KeyValuePair<string, string>(paramName, value));
        }
    }

    public MailUser(CsvEntry e)
    {
        this.MailAddress = e[0];
        this.Company = e[1];
        this.FullName = e[2];
        this.Language = CoreLanguageList.GetLanguageClassByName(e[3]);

        if (e.Count >= 5)
        {
            string pStr = e[4];

            this.ParamList = MailUtil.StrToParamList(pStr);
        }

        normalize();
    }

    void normalize()
    {
        Str.NormalizeStringStandard(ref this.Company);
        Str.NormalizeStringStandard(ref this.FullName);
        Str.NormalizeString(ref this.MailAddress, true, true, false, false);

        if (MailUtil.IsPersonal(this.Company))
        {
            this.Company = "";
        }

        if (Str.IsEmptyStr(this.MailAddress) || Str.CheckMailAddress(this.MailAddress) == false)
        {
            throw new Exception("Mail Address '" + this.MailAddress + "' is incorrect.");
        }
    }

    public CsvEntry ToCsvEntry()
    {
        CsvEntry e = new CsvEntry(this.MailAddress, this.Company, this.FullName, this.Language.Name, MailUtil.ParamListToStr(this.ParamList));

        return e;
    }

    public int CompareTo(MailUser? other)
    {
        return this.MailAddress.CompareTo(other!.MailAddress);
    }
}

public class MailUserList
{
    public readonly List<MailUser> UserList = new List<MailUser>();

    public Csv ToCsv()
    {
        Csv csv = new Csv(Str.Utf8Encoding);

        foreach (MailUser u in UserList)
        {
            csv.Add(u.ToCsvEntry());
        }

        return csv;
    }

    public void FromCsv(Csv csv)
    {
        UserList.Clear();

        foreach (CsvEntry? e in csv.Items)
        {
            if (e == null) continue;

            if (e.Count >= 4)
            {
                try
                {
                    MailUser u = new MailUser(e);

                    this.UserList.Add(u);
                }
                catch
                {
                }
            }
            else if (e.Count == 1)
            {
                try
                {
                    string mail = e[0];

                    if (Str.CheckMailAddress(mail))
                    {
                        MailUser u = new MailUser(mail, mail, "", "ja", new List<KeyValuePair<string, string>>());

                        this.UserList.Add(u);
                    }
                }
                catch
                {
                }
            }
        }
    }
}

public class MailItem : IComparable<MailItem>
{
    public string From;
    public MailUser DestMailUser;
    public string SrcSubject;
    public string SrcBody;
    public string SrcBodyHtml;
    public MailUtilResourceFile[] ResourceFiles;
    public MailUtilAttachedFile[] AttachedFiles;
    public List<KeyValuePair<string, string>> ParamList;

    public MailItem(string from, MailUser dest, string subject, string body, string bodyHtml, MailUtilResourceFile[] resourceFiles, MailUtilAttachedFile[] attachedFiles)
    {
        this.ResourceFiles = resourceFiles;
        this.AttachedFiles = attachedFiles;

        this.From = from;
        this.SrcBody = body;
        this.SrcSubject = subject;
        this.SrcBodyHtml = bodyHtml;
        this.DestMailUser = dest;
        this.ParamList = Util.CloneList<KeyValuePair<string, string>>(dest.ParamList);
        this.ParamList.Add(new KeyValuePair<string, string>("mail", dest.MailAddress));
        this.ParamList.Add(new KeyValuePair<string, string>("name", dest.FullName));
        this.ParamList.Add(new KeyValuePair<string, string>("company", dest.Company));

        string tmp = (dest.Company + " " + dest.FullName).Trim();
        this.ParamList.Add(new KeyValuePair<string, string>("company_and_name", tmp));

        tmp = "";
        if (Str.IsEmptyStr(dest.Company) == false)
        {
            tmp += dest.Company += "\r\n";
        }
        tmp += dest.FullName;
        this.ParamList.Add(new KeyValuePair<string, string>("company_and_name_crlf", tmp));
    }

    public string Subject
    {
        get
        {
            return MailUtil.ProcStr(this.SrcSubject, DestMailUser.Language, this.ParamList);
        }
    }

    public string Body
    {
        get
        {
            return MailUtil.ProcStr(this.SrcBody, DestMailUser.Language, this.ParamList);
        }
    }

    public string BodyHtml
    {
        get
        {
            if (Str.IsEmptyStr(this.SrcBodyHtml))
            {
                return "";
            }
            else
            {
                return MailUtil.ProcStr(this.SrcBodyHtml, DestMailUser.Language, this.ParamList);
            }
        }
    }

    string? hashCache = null;
    public string Hash
    {
        get
        {
            if (hashCache == null)
            {
                string s = Subject + Body + DestMailUser.MailAddress;
                if (Str.IsEmptyStr(this.BodyHtml) == false)
                {
                    s += this.BodyHtml;
                }

                // 追加: メールアドレスのみをキーにする
                s = DestMailUser.MailAddress.ToUpperInvariant();

                hashCache = Str.ByteToStr(Str.HashStr(s));
            }

            return hashCache;
        }
    }

    public int CompareTo(MailItem? other)
    {
        return this.DestMailUser.CompareTo(other!.DestMailUser);
    }

    public bool Send(string server)
    {
        return Send(server, 3);
    }

    public bool Send(string server, int numRetrys)
    {
        int i;
        for (i = 0; i < numRetrys; i++)
        {
            if (sendMain(server))
            {
                return true;
            }
        }
        return false;
    }

    bool sendMain(string server)
    {
        try
        {
            SmtpConfig config = new SmtpConfig(server, 25);
            SmtpBody b = new SmtpBody(new MailAddress(this.From),
                new MailAddress(new MailAddress(this.DestMailUser.MailAddress).Address, DestMailUser.FullName),
                this.Subject, this.Body);

            string bodyHtml = this.BodyHtml;
            if (Str.IsEmptyStr(bodyHtml) == false)
            {
                b.BodyHtml = bodyHtml;

                foreach (MailUtilResourceFile f in this.ResourceFiles)
                {
                    b.AddLinkedResourceFile(f.Data, f.Type, f.Id);
                }
            }

            foreach (MailUtilAttachedFile f in this.AttachedFiles)
            {
                b.AddAttachedFile(f.Data, f.Type, f.Filename, null);
            }

            b.Send(config);

            return true;
        }
        catch
        {
            return false;
        }
    }
}

public class MailUtilResourceFile
{
    public readonly byte[] Data;
    public readonly string Id;
    public readonly string Type;

    public MailUtilResourceFile(byte[] data, string id, string type)
    {
        this.Data = data;
        this.Id = id;
        this.Type = type;
    }
}

public class MailUtilAttachedFile
{
    public readonly byte[] Data;
    public readonly string Filename;
    public readonly string Type;

    public MailUtilAttachedFile(byte[] data, string fileName, string type)
    {
        this.Data = data;
        this.Filename = fileName;
        this.Type = type;
    }
}

public static class MailUtil
{
    public static MailItem[] GenerateMailItemListToSend(string from, MailUserList destList, string subject, string body)
    {
        return GenerateMailItemListToSend(from, destList, subject, body, "", null, null);
    }
    public static MailItem[] GenerateMailItemListToSend(string from, MailUserList destList, string subject, string body, string bodyHtml,
        MailUtilResourceFile[]? resourceFiles, MailUtilAttachedFile[]? attachedFiles)
    {
        if (resourceFiles == null)
        {
            resourceFiles = new MailUtilResourceFile[0];
        }
        if (attachedFiles == null)
        {
            attachedFiles = new MailUtilAttachedFile[0];
        }

        SortedList<string, MailItem> list = new SortedList<string, MailItem>();

        foreach (MailUser u in destList.UserList)
        {
            MailItem m = new MailItem(from, u, subject, body, bodyHtml, resourceFiles, attachedFiles);

            if (list.ContainsKey(m.Hash) == false)
            {
                list.Add(m.Hash, m);
            }
        }

        List<MailItem> ret = new List<MailItem>();

        foreach (MailItem m in list.Values)
        {
            ret.Add(m);
        }

        ret.Sort();

        return ret.ToArray();
    }

    public static bool IsPersonal(string str)
    {
        if (Str.StrCmpi(str, "個人") || Str.StrCmpi(str, "person") ||
            Str.StrCmpi(str, "个人") || Str.StrCmpi(str, "无") || Str.StrCmpi(str, "無") ||
            Str.StrCmpi(str, "没有") || Str.StrCmpi(str, "没公") ||
            Str.StrCmpi(str, "personal") || Str.StrCmpi(str, "individual") ||
            Str.StrCmpi(str, "none") || Str.StrCmpi(str, "na") || Str.StrCmpi(str, "nothing") ||
            Str.StrCmpi(str, "n/a") || Str.StrCmpi(str, "-") ||
            Str.IsEmptyStr(str))
        {
            return true;
        }

        return false;
    }

    public static string ProcStr(string str, CoreLanguageClass lang, List<KeyValuePair<string, string>> paramList)
    {
        foreach (KeyValuePair<string, string> p in paramList)
        {
            string tag = "<$" + p.Key.Trim() + "$>";
            string value = p.Value.Trim();

            str = Str.ReplaceStr(str, tag, value);
        }

        StringBuilder sb = new StringBuilder();

        bool b = false;
        int i;
        for (i = 0; i < str.Length; i++)
        {
            char c = str[i];
            bool ok = true;

            if (b == false)
            {
                if (c == '<' && str[i + 1] == '$')
                {
                    i++;
                    b = true;
                    ok = false;
                }
            }
            else
            {
                if (c == '$' && str[i + 1] == '>')
                {
                    i++;
                    b = false;
                    ok = false;
                }
            }

            if (b == false && ok == true)
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }

    public static List<KeyValuePair<string, string>> StrToParamList(string str)
    {
        List<KeyValuePair<string, string>> ret = new List<KeyValuePair<string, string>>();

        char[] sps = { ';' };
        string[] tokens = str.Split(sps, StringSplitOptions.RemoveEmptyEntries);

        foreach (string token in tokens)
        {
            int r = token.IndexOf("=");

            if (r != -1)
            {
                string name = token.Substring(0, r).Trim();
                string value = token.Substring(r + 1).Trim();

                if (Str.IsEmptyStr(name) == false)
                {
                    ret.Add(new KeyValuePair<string, string>(name, value));
                }
            }
        }

        return ret;
    }

    public static string ParamListToStr(List<KeyValuePair<string, string>> list)
    {
        string ret = "";
        foreach (KeyValuePair<string, string> p in list)
        {
            ret += p.Key.Trim().Replace(";", ".").Replace("=", "-") +
                "=" +
                p.Value.Trim().Replace(";", ".").Replace("=", "-") +
                ";";
        }
        return ret.Trim(); ;
    }
}

