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
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Buffers;
using System.Diagnostics;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

namespace IPA.Cores.Basic;

public class ChrootFileSystemParam : RewriteFileSystemParam
{
    public string PhysicalRootDirectory { get; }

    public ChrootFileSystemParam(FileSystem underlayFileSystem, string physicalRootDirectory, FileSystemMode mode = FileSystemMode.Default, bool disposeUnderlay = false)
        : base(underlayFileSystem, mode, disposeUnderlay)
    {
        physicalRootDirectory = underlayFileSystem.NormalizePath(physicalRootDirectory);
        physicalRootDirectory = underlayFileSystem.PathParser.NormalizeDirectorySeparatorAndCheckIfAbsolutePath(physicalRootDirectory);

        this.PhysicalRootDirectory = physicalRootDirectory;
    }
}


public class ChrootFileSystem : RewriteFileSystem
{
    protected new ChrootFileSystemParam Params => (ChrootFileSystemParam)base.Params;
    protected string PhysicalRootDirectory => Params.PhysicalRootDirectory;

    public ChrootFileSystem(ChrootFileSystemParam param) : base(param)
    {
    }

    protected override string MapPathPhysicalToVirtualImpl(string relativeSafeUnderlayFsStyleVirtualPath)
    {
        // From:
        // the examples of physicalPath:
        // c:\view_root
        // c:\view_root\readme.txt
        // c:\view_root\abc\def\
        // c:\view_root\abc\def\readme.txt
        // 
        // To:
        // the contents of virtualPath:
        // '' (empty)  - representing the root directory
        // readme.txt
        // abc\def
        // abc\def\readme.txt
        return UnderlayPathParser.GetRelativeFileName(relativeSafeUnderlayFsStyleVirtualPath, this.PhysicalRootDirectory);
    }

    protected override string MapPathVirtualToPhysicalImpl(string underlayFsStylePhysicalPath)
    {
        // From:
        // the contents of underlayFsStylePhysicalPath:
        // '' (empty)  - representing the root directory
        // readme.txt
        // abc\def
        // abc\def\readme.txt
        // Note: underlayFsStylePhysicalPath never be absolute path.
        //
        // To:
        // the contents of physicalPath:
        // c:\view_root
        // c:\view_root\readme.txt
        // c:\view_root\abc\def
        // c:\view_root\abc\def\readme.txt
        return UnderlayPathParser.Combine(this.PhysicalRootDirectory, underlayFsStylePhysicalPath, true);
    }
}

