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
using System.Reflection;
using System.Collections.Immutable;
using Microsoft.Extensions.FileProviders;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

namespace IPA.Cores.Basic;

public class VfsFileProviderBasedFile : VfsRandomAccessFile
{
    protected new FileProviderBasedFileSystem FileSystem => (FileProviderBasedFileSystem)base.FileSystem;
    public IFileInfo FileInfo { get; }
    public string FullName { get; }

    public VfsFileProviderBasedFile(FileProviderBasedFileSystem fileSystem, IFileInfo fileInfo, string fileName) : base(fileSystem, fileName)
    {
        this.FullName = fileName;
        this.FileInfo = fileInfo;
    }

    protected override IRandomAccess<byte> GetSharedRandomAccessBaseImpl()
    {
        return new SeekableStreamBasedRandomAccess(this.FileInfo.CreateReadStream());
    }
}

public class FileProviderFileSystemParams : VirtualFileSystemParams
{
    public IFileProvider UnderlayProvider { get; }

    public FileProviderFileSystemParams(IFileProvider underlayProvider) : base(FileSystemMode.ReadOnly)
    {
        this.UnderlayProvider = underlayProvider;
    }
}

public class FileProviderBasedFileSystem : VirtualFileSystem
{
    public new FileProviderFileSystemParams Params => (FileProviderFileSystemParams)base.Params;

    IFileProvider Provider => Params.UnderlayProvider;

    public FileProviderBasedFileSystem(FileProviderFileSystemParams param) : base(param)
    {
        ScanDirAndRegisterFiles("/");
    }

#pragma warning disable CS1998
    void ScanDirAndRegisterFiles(string dir)
    {
        if (this.IsDirectoryExistsImplAsync(dir)._GetResult() == false)
        {
            this.CreateDirectoryImplAsync(dir)._GetResult();
        }

        IDirectoryContents entityList = Provider.GetDirectoryContents(dir);

        foreach (IFileInfo entity in entityList)
        {
            if (entity.Exists)
            {
                if (entity.IsDirectory)
                {
                    string subDirFullPath = PathParser.Mac.Combine(dir, entity.Name);

                    ScanDirAndRegisterFiles(subDirFullPath);
                }
                else
                {
                    string fileFullPath = PathParser.Mac.Combine(dir, entity.Name);

                    using (this.AddFileAsync(new FileParameters(fileFullPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite),
                        async (newFilename, newFileOption, c) =>
                        {
                            return new VfsFileProviderBasedFile(this, entity, newFilename);
                        })._GetResult())
                    {
                    }
                }
            }
        }
    }
#pragma warning restore CS1998

    protected override IFileProvider CreateFileProviderForWatchImpl(string root)
    {
        return base.CreateDefaultNullFileProvider();
    }
}

