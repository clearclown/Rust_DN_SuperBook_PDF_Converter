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

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

namespace IPA.Cores.Basic.Legacy;

public class FileLogger
{
    readonly CriticalSection LockObj = new CriticalSection<FileLogger>();
    string? logDir;
    string lastFileName;
    IO? fs;
    public bool Flush = false;

    public FileLogger(string logDir)
    {
        SetLogDir(logDir);

        lastFileName = "";

        fs = null;
    }

    public void SetLogDir(string logDir)
    {
        lock (LockObj)
        {
            this.logDir = IO.InnerFilePath(logDir);
        }
    }

    string generateFileName(DateTime dt)
    {
        return string.Format("{0:0000}{1:00}{2:00}.log", dt.Year, dt.Month, dt.Day);
    }

    string generateFullFileName(DateTime dt)
    {
        lock (LockObj)
        {
            return IO.CombinePath(logDir!, generateFileName(dt));
        }
    }

    void write(DateTime now, byte[] data, bool flush)
    {
        lock (LockObj)
        {
            string filename = generateFullFileName(now);

            if (logDir == null || logDir == "")
            {
                return;
            }

            if (IO.IsDirExists(logDir) == false)
            {
                if (IO.MakeDir(logDir) == false)
                {
                    return;
                }
            }

            if (lastFileName != filename || fs == null)
            {
                if (fs != null)
                {
                    try
                    {
                        fs.Close();
                    }
                    catch
                    {
                    }
                }

                fs = IO.FileCreateOrAppendOpen(filename);
            }

            lastFileName = filename;

            fs.Write(data);

            if (flush)
            {
                fs.Flush();
            }
        }
    }

    public void Write(params string[] strings)
    {
        StringBuilder b = new StringBuilder();
        int i;
        for (i = 0; i < strings.Length; i++)
        {
            string s2 = normalizeStr(strings[i]);

            b.Append(s2);

            if (i != (strings.Length - 1))
            {
                b.Append(",");
            }
        }

        Write(b.ToString());
    }

    public void Write(string str)
    {
        try
        {
            lock (LockObj)
            {
                DateTime now = DateTime.Now;
                string nowStr = Str.DateTimeToDtstr(now, true);

                string tmp = nowStr + "," + str + "\r\n";

                write(now, Str.Utf8Encoding.GetBytes(tmp), Flush);
            }
        }
        catch
        {
        }
    }

    string normalizeStr(string s)
    {
        return s.Replace("\\", "\\\\").Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\\n").Replace(",", ";");
    }

    public void Close()
    {
        if (fs != null)
        {
            try
            {
                fs.Close();
            }
            catch
            {
            }

            fs = null;
        }
    }
}
