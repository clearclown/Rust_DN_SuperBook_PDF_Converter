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

using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.IO.Pipes;
using System.Security.Cryptography;

namespace IPA.Cores.Tools.DebugHost
{
    public static class Utils
    {
        public static Process ExecProcess(string cmdLine)
        {
            DivideCommandLine(cmdLine, out string programName, out string arguments);

            ProcessStartInfo info = new ProcessStartInfo()
            {
                FileName = programName,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = false,
            };

            Process p = Process.Start(info);

            return p;
        }

        public static bool IsSplitChar(char c, string splitStr)
        {
            return (splitStr.IndexOf("" + c, StringComparison.OrdinalIgnoreCase) != -1);
        }

        public static bool GetKeyAndValue(string str, out string key, out string value, string splitStr = " ")
        {
            uint mode = 0;
            string keystr = "", valuestr = "";

            foreach (char c in str)
            {
                switch (mode)
                {
                    case 0:
                        if (IsSplitChar(c, splitStr) == false)
                        {
                            mode = 1;
                            keystr += c;
                        }
                        break;

                    case 1:
                        if (IsSplitChar(c, splitStr) == false)
                        {
                            keystr += c;
                        }
                        else
                        {
                            mode = 2;
                        }
                        break;

                    case 2:
                        if (IsSplitChar(c, splitStr) == false)
                        {
                            mode = 3;
                            valuestr += c;
                        }
                        break;

                    case 3:
                        valuestr += c;
                        break;
                }
            }

            if (mode == 0)
            {
                value = "";
                key = "";
                return false;
            }
            else
            {
                value = valuestr.Trim();
                key = keystr.Trim();
                return true;
            }
        }

        public static string ByteToHex(byte[] data, string paddingStr)
        {
            StringBuilder ret = new StringBuilder();

            int i;
            for (i = 0; i < data.Length; i++)
            {
                byte b = data[i];

                string s = b.ToString("X");
                if (s.Length == 1)
                {
                    s = "0" + s;
                }

                ret.Append(s);

                if (paddingStr != null)
                {
                    if (i != (data.Length - 1))
                    {
                        ret.Append(paddingStr);
                    }
                }
            }

            return ret.ToString().Trim();
        }

        public static byte[] HashSHA1(byte[] src)
        {
            SHA1 sha = new SHA1Managed();

            return sha.ComputeHash(src);
        }

        public static void DivideCommandLine(string src, out string myProgramName, out string arguments)
        {
            int len = src.Length;
            int mode = 0;
            StringBuilder sb1 = new StringBuilder();
            StringBuilder sb2 = new StringBuilder();

            for (int i = 0; i < len; i++)
            {
                char c = src[i];
                if (mode == 0)
                {
                    if (c == '\"')
                    {
                        mode = 1;
                    }
                    else if (c == ' ')
                    {
                        mode = 2;
                    }
                }
                else if (mode == 1)
                {
                    if (c == '\"')
                    {
                        mode = 0;
                    }
                }
                else if (mode == 2)
                {
                    if (c != ' ')
                    {
                        mode = 3;
                    }
                }

                if (mode == 0 || mode == 1)
                {
                    sb1.Append(c);
                }
                else if (mode == 3)
                {
                    sb2.Append(c);
                }
            }

            myProgramName = sb1.ToString();
            arguments = sb2.ToString();
        }
    }

    public struct Once
    {
        volatile private int flag;
        public void Set() => IsFirstCall();
        public bool IsFirstCall() => (Interlocked.CompareExchange(ref this.flag, 1, 0) == 0);
        public bool IsSet => (this.flag != 0);
        public static implicit operator bool(Once once) => once.flag != 0;
        public void Reset() => this.flag = 0;

        public override string ToString() => IsSet.ToString();
    }

    public class GlobalMutex
    {
        readonly string InternalName;

        int LockedCount = 0;

        Mutex CurrentMutexObj = null;

        readonly static HashSet<object> MutexList = new HashSet<object>();

        public GlobalMutex(string name)
        {
            InternalName = @"Global\utils_si_" + name;
        }

        public bool TryLock()
        {
            try
            {
                Lock();
                return true;
            }
            catch
            {
                return false;
            }
        }

        public void Lock()
        {
            if (LockedCount == 0)
            {
                Mutex mutex = new Mutex(false, InternalName, out bool createdNew);

                if (createdNew == false)
                {
                    mutex.Dispose();
                    throw new ApplicationException($"Cannot create the new mutex object.");
                }

                CurrentMutexObj = mutex;
            }

            LockedCount++;
        }

        public void Unlock()
        {
            if (LockedCount <= 0) throw new ApplicationException("locked_count <= 0");
            if (LockedCount == 1)
            {
                CurrentMutexObj.Dispose();
                CurrentMutexObj = null;
            }
            LockedCount--;
        }
    }
}
