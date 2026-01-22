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

#if false // 2020/11/14 libgit は native ライブラリなので BASIC から外した。後日 Advance にでも入れる予定
using System;
using System.IO;
using System.IO.Enumeration;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using LibGit2Sharp;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

namespace IPA.TestDev;

partial class TestDevCommands
{
    const string GitUrl = "https://github.com/IPA-CyberLab/IPA-DN-Cores.git";
    //const string GitUrl = "https://github.com/IPA-CyberLab/IPA-DN-TestIDS.git";
    const string GitTestBaseDir = @"c:\tmp\git_test_root\4";

    [ConsoleCommand(
        "Git command",
        "Git [arg]",
        "Git test")]
    static int Git(ConsoleService c, string cmdName, string str)
    {
        ConsoleParam[] args = { };
        ConsoleParamValueList vl = c.ParseCommandList(cmdName, str, args);

        if (Lfs.IsDirectoryExists(GitTestBaseDir) == false)
        {
            Git_Test_Clone();
        }

        Git_Test_1();

        return 0;
    }

    static void Git_Test_1()
    {
        while (true)
        {
            using (var rep = new GitRepository(GitTestBaseDir))
            {
                using (var fs = new GitFileSystem(new GitFileSystemParams(rep)))
                {
                    while (true)
                    {
                        fs.ReadDataFromFile("/test.txt");
                        Sleep(500);
                    }
                }
            }
        }
    }

    static void Git_Test_Clone()
    {
        GitUtil.Clone(GitTestBaseDir, GitUrl);
    }
}
#endif
