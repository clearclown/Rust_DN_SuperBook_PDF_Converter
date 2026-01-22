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
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Text;
using System.Collections.Generic;
using System.Linq;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

namespace IPA.Cores.Basic;

public class SourceCodeCounter
{
    DirectoryPath RootDir;
    FileSystem Fs => RootDir.FileSystem;

    public int NumLines { get; private set; } = 0;
    public int NumFiles { get; private set; } = 0;
    public long TotalSize { get; private set; } = 0;

    HashSet<string> ExcludeHashSet = new HashSet<string>(StrComparer.IgnoreCaseComparer);

    public SourceCodeCounter(DirectoryPath rootDir, params string[] excludeFileNames)
    {
        this.RootDir = rootDir;

        excludeFileNames._DoForEach(x => ExcludeHashSet.Add(x));

        DirectoryWalker walk = new DirectoryWalker(RootDir.FileSystem);

        walk.WalkDirectory(RootDir.PathString,
            (pathInfo, entities, cancel) =>
            {
                foreach (FileSystemEntity entity in entities)
                {
                    if (entity.IsDirectory == false && entity.Name._IsExtensionMatch(Consts.Extensions.Filter_SourceCodes))
                    {
                        if (ExcludeHashSet.Contains(entity.Name) == false)
                        {
                            int numLines = Fs.ReadStringFromFile(entity.FullPath)._GetLines().Length;

                            this.NumLines += numLines;

                            this.NumFiles++;

                            this.TotalSize += entity.Size;
                        }
                    }
                }
                return true;
            },
            exceptionHandler: (pathInfo, err, cancel) =>
            {
                return true;
            });
    }
}

