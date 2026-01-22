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
using Microsoft.Extensions.FileProviders;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using System.Collections.Immutable;

namespace IPA.Cores.Basic;

public class SourceCodePathAndMarkerFileName
{
    public string SourceCodePath { get; }
    public string MarkerFileName { get; }

    public SourceCodePathAndMarkerFileName(string sourceCodePath, string markerFileName)
    {
        this.SourceCodePath = sourceCodePath;
        this.MarkerFileName = markerFileName;
    }
}

public class AssemblyWithSourceInfo
{
    public IReadOnlyList<DirectoryPath> SourceRootList { get; }
    public Assembly Assembly { get; }

    public AssemblyWithSourceInfo(Type sampleType, params SourceCodePathAndMarkerFileName[] sourceInfoList)
    {
        this.Assembly = sampleType.Assembly;

        List<DirectoryPath> srcRootList = new List<DirectoryPath>();

        foreach (SourceCodePathAndMarkerFileName srcInfo in sourceInfoList)
        {
            try
            {
                DirectoryPath? root = Lfs.DetermineRootPathWithMarkerFile(srcInfo.SourceCodePath, srcInfo.MarkerFileName);
                if (root != null)
                {
                    srcRootList.Add(root);
                }
            }
            catch { }
        }

        this.SourceRootList = srcRootList;
    }

    public override bool Equals(object? obj)
    {
        obj._NullCheck();

        AssemblyWithSourceInfo other = (AssemblyWithSourceInfo)obj;

        if (this.Assembly.Equals(other.Assembly) == false) return false;

        string thisSourceListConcat = this.SourceRootList.Select(x => x.PathString).OrderBy(x => x)._Combine(",");
        string otherSourceListConcat = other.SourceRootList.Select(x => x.PathString).OrderBy(x => x)._Combine(",");

        if (thisSourceListConcat != otherSourceListConcat) return false;

        return true;
    }
    public override int GetHashCode()
    {
        int code1 = this.Assembly.GetHashCode();
        string thisSourceListConcat = this.SourceRootList.Select(x => x.PathString).OrderBy(x => x)._Combine(",");
        int code2 = thisSourceListConcat.GetHashCode();

        return HashCode.Combine(code1, code2);
    }
}

[Flags]
public enum ResFileReadFrom
{
    None = 0,
    Physical = 1,
    EmbeddedResource = 2,
    Both = Physical | EmbeddedResource,
}

public class ResourceFileSystem : FileProviderBasedFileSystem
{
    static Singleton<AssemblyWithSourceInfo, ResourceFileSystem> Singleton = null!;

    public static StaticModule Module { get; } = new StaticModule(ModuleInit, ModuleFree);

    public AssemblyWithSourceInfo AssemblyInfo { get; }

    public IReadOnlyList<DirectoryPath> ResourceRootSourceDirectoryList { get; }

    static void ModuleInit()
    {
        Singleton = new Singleton<AssemblyWithSourceInfo, ResourceFileSystem>((asm) => new ResourceFileSystem(asm));
    }

    static void ModuleFree()
    {
        Singleton._DisposeSafe();

        Singleton = null!;
    }

    public ResourceFileSystem(AssemblyWithSourceInfo assemblyInfo) : base(new FileProviderFileSystemParams(new ManifestEmbeddedFileProvider(assemblyInfo.Assembly)))
    {
        this.AssemblyInfo = assemblyInfo;

        List<DirectoryPath> resourceRootList = new List<DirectoryPath>();

        // List all ResourcRoot directories (which contains the 'resource_root' file)
        foreach (DirectoryPath srcRootPath in this.AssemblyInfo.SourceRootList)
        {
            try
            {
                foreach (FileSystemEntity entity in srcRootPath.EnumDirectory(true, EnumDirectoryFlags.NoGetPhysicalSize))
                {
                    if (entity.IsCurrentOrParentDirectory == false && entity.IsDirectory)
                    {
                        DirectoryPath subDir = new DirectoryPath(entity.FullPath);
                        try
                        {
                            if (subDir.GetFiles().Where(x => x.GetFileName()._IsSamei(Consts.FileNames.RootMarker_Resource)).Any())
                            {
                                resourceRootList.Add(subDir);
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }

        this.ResourceRootSourceDirectoryList = resourceRootList;

        this.Params.EasyAccessPathFindMode.Set(EasyAccessPathFindMode.MostMatch);
    }

    public static ResourceFileSystem CreateOrGet(AssemblyWithSourceInfo assembly) => Singleton.CreateOrGet(assembly);

    public FileSystemBasedProvider[] CreateEmbeddedAndPhysicalFileProviders(string rootDirectoryOnResourceRootDir, ResFileReadFrom flags = ResFileReadFrom.Both)
    {
        List<FileSystemBasedProvider> ret = new List<FileSystemBasedProvider>();

        string relativeRoot = rootDirectoryOnResourceRootDir;

        if (this.PathParser.IsAbsolutePath(relativeRoot))
        {
            relativeRoot = this.PathParser.NormalizeUnixStylePathWithRemovingRelativeDirectoryElements(relativeRoot);

            relativeRoot = this.PathParser.GetRelativeDirectoryName(relativeRoot, "/");
        }

        if (this.PathParser.IsAbsolutePath(relativeRoot))
        {
            throw new ApplicationException($"relativeRoot '{relativeRoot}' is absolute.");
        }

        if (relativeRoot._IsFilled())
        {
            if (flags.Bit(ResFileReadFrom.Physical))
            {
                foreach (DirectoryPath srcRoot in this.ResourceRootSourceDirectoryList)
                {
                    DirectoryPath resourceRoot = srcRoot.GetSubDirectory(relativeRoot, true);

                    if (resourceRoot.IsDirectoryExists())
                    {
                        ret.Add(resourceRoot.FileSystem.CreateFileProvider(resourceRoot));
                    }
                }
            }

            if (flags.Bit(ResFileReadFrom.EmbeddedResource))
            {
                string rootDirectoryInResourceAbsolute = PathParser.Combine(Consts.FileNames.ResourceRootAbsoluteDirName, relativeRoot);

                ret.Add(this.CreateFileProvider(rootDirectoryInResourceAbsolute));
            }
        }

        return ret.ToArray();
    }
}

