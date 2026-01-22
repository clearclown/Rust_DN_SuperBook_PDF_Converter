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

// Author: Daiyuu Nobori
// Physical Raw Disk File System

#if true

#pragma warning disable CA2235 // Mark all non-serializable fields

using System;
using System.Buffers;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Serialization;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using Microsoft.Extensions.FileProviders;

namespace IPA.Cores.Basic;

public class Win32LocalRawDiskVfsFile : RawDiskFileSystemBasedVfsFile
{
    public Win32LocalRawDiskVfsFile(LocalRawDiskFileSystem fileSystem, RawDiskItemData itemData) : base(fileSystem, itemData)
    {
    }

    public override long Size { get => this.ItemData.Length; protected set => throw new NotImplementedException(); }
    public override long PhysicalSize { get => this.ItemData.Length; protected set => throw new NotImplementedException(); }
    public override string Name { get => this.ItemData.Name; protected set => throw new NotImplementedException(); }
    public override FileAttributes Attributes { get => FileAttributes.Normal; protected set => throw new NotImplementedException(); }
    public override DateTimeOffset CreationTime { get => DateTimeOffset.Now; protected set => throw new NotImplementedException(); }
    public override DateTimeOffset LastWriteTime { get => DateTimeOffset.Now; protected set => throw new NotImplementedException(); }
    public override DateTimeOffset LastAccessTime { get => DateTimeOffset.Now; protected set => throw new NotImplementedException(); }

    public override async Task<FileObject> OpenAsync(FileParameters option, string fullPath, CancellationToken cancel = default)
    {
        FileStream fs = PalWin32FileStream.Create(this.ItemData.RawPath, option.Mode, option.Access, option.Share, 4096, FileOptions.None);

        try
        {
            var geometry = await Win32ApiUtil.DiskGetDriveGeometryAsync(fs.SafeFileHandle, this.ItemData.RawPath, cancel);

            var randomAccess = new SeekableStreamBasedRandomAccess(fs, autoDisposeBase: true, fixedFileSize: geometry.DiskSize);

            try
            {
                return new RandomAccessFileObject(this.FileSystem, option, randomAccess);
            }
            catch
            {
                randomAccess._DisposeSafe();
                throw;
            }
        }
        catch
        {
            fs._DisposeSafe();
            throw;
        }
    }

    protected override Task ReleaseLinkImplAsync()
    {
        return Task.CompletedTask;
    }
}

public class UnixLocalRawDiskVfsFile : RawDiskFileSystemBasedVfsFile
{
    public UnixLocalRawDiskVfsFile(LocalRawDiskFileSystem fileSystem, RawDiskItemData itemData) : base(fileSystem, itemData)
    {
    }

    public override long Size { get => this.ItemData.Length; protected set => throw new NotImplementedException(); }
    public override long PhysicalSize { get => this.ItemData.Length; protected set => throw new NotImplementedException(); }
    public override string Name { get => this.ItemData.Name; protected set => throw new NotImplementedException(); }
    public override FileAttributes Attributes { get => FileAttributes.Normal; protected set => throw new NotImplementedException(); }
    public override DateTimeOffset CreationTime { get => DateTimeOffset.Now; protected set => throw new NotImplementedException(); }
    public override DateTimeOffset LastWriteTime { get => DateTimeOffset.Now; protected set => throw new NotImplementedException(); }
    public override DateTimeOffset LastAccessTime { get => DateTimeOffset.Now; protected set => throw new NotImplementedException(); }

    public override async Task<FileObject> OpenAsync(FileParameters option, string fullPath, CancellationToken cancel = default)
    {
        long diskSize = await UnixApi.GetBlockDeviceSizeAsync(this.ItemData.RawPath, cancel);

        FileStream fs = new FileStream(this.ItemData.RawPath, option.Mode, option.Access, option.Share, 4096, FileOptions.None);

        try
        {
            var randomAccess = new SeekableStreamBasedRandomAccess(fs, autoDisposeBase: true, fixedFileSize: diskSize);

            try
            {
                return new RandomAccessFileObject(this.FileSystem, option, randomAccess);
            }
            catch
            {
                randomAccess._DisposeSafe();
                throw;
            }
        }
        catch
        {
            fs._DisposeSafe();
            throw;
        }
    }

    protected override Task ReleaseLinkImplAsync()
    {
        return Task.CompletedTask;
    }
}

public class UnixMountInfo
{
    public string Target = "";
    public string Source = "";
    public string FsType = "";
    public string Options = "";
}

public class LocalRawDiskFileSystem : RawDiskFileSystem
{
    public LocalRawDiskFileSystem(RawDiskFileSystemParams? param = null) : base(param)
    {
    }

    public static async Task<List<UnixMountInfo>> GetLinuxMountInfoListAsync(CancellationToken cancel = default)
    {
        List<UnixMountInfo> ret = new List<UnixMountInfo>();

        string retStr = (await EasyExec.ExecAsync(Consts.LinuxCommands.FindMnt, "--list --submounts", easyOutputMaxSize: 1_000_000, cancel: cancel)).OutputStr;

        var lines = retStr._GetLines(true);

        int num = 0;

        foreach (var line in lines)
        {
            var tokens = line._Split(StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries, " ", "\t");

            if (tokens.Length >= 4)
            {
                UnixMountInfo item = new UnixMountInfo
                {
                    Target = tokens[0],
                    Source = tokens[1],
                    FsType = tokens[2],
                    Options = tokens[3],
                };


                num++;

                if (num >= 2) // Skip first line
                {
                    ret.Add(item);
                }
            }
        }

        return ret;
    }

    public static string GeneratePrintableSafeFileNameFromUnixFullPath(string path, string combineStr = "-")
    {
        var pathList = PPLinux.SplitAbsolutePathToElementsUnixStyle(path, true);

        List<string> tmp = new List<string>();

        tmp.Add("rootdir");

        foreach (var element in pathList)
        {
            string a = Str.MakeVerySafeAsciiOnlyNonSpaceFileName(element, true).ToLowerInvariant();

            tmp.Add(a);
        }

        return tmp._Combine(combineStr);
    }

    protected override Task<RawDiskFileSystemBasedVfsFile> CreateRawDiskFileImplAsync(RawDiskItemData item, CancellationToken cancel = default)
    {
        RawDiskFileSystemBasedVfsFile f;

        if (Env.IsWindows)
        {
            f = new Win32LocalRawDiskVfsFile(this, item);
        }
        else
        {
            f = new UnixLocalRawDiskVfsFile(this, item);
        }

        return TR<RawDiskFileSystemBasedVfsFile>(f);
    }

    protected override async Task<IEnumerable<RawDiskItemData>> RescanRawDisksImplAsync(CancellationToken cancel = default)
    {
        List<RawDiskItemData> ret = new List<RawDiskItemData>();

        if (Env.IsWindows)
        {
            if (Env.IsAdmin == false)
            {
                throw new CoresException("Administrator privilege is required.");
            }

            for (int i = 0; i < 100; i++)
            {
                string name = "PhysicalDrive" + i.ToString();
                string rawPath = @"\\.\" + name;

                try
                {
                    using (var handle = PalWin32FileStream.CreateFileOpenHandle(FileMode.Open, FileShare.ReadWrite, FileOptions.None, FileAccess.Read, rawPath))
                    {
                        try
                        {
                            var geometry = await Win32ApiUtil.DiskGetDriveGeometryAsync(handle, rawPath, cancel);

                            RawDiskItemType type = RawDiskItemType.Unknown;
                            if (geometry.MediaType == Win32Api.Kernel32.NativeDiskType.FixedMedia)
                                type = RawDiskItemType.FixedMedia;
                            else if (geometry.MediaType == Win32Api.Kernel32.NativeDiskType.RemovableMedia)
                                type = RawDiskItemType.RemovableMedia;

                            ret.Add(new RawDiskItemData(name, rawPath, type, geometry.DiskSize, false));
                        }
                        catch
                        { }
                    }
                }
                catch
                {
                }
            }

            foreach (var realDisk in ret.Where(x => x.AliasOf._IsEmpty()).OrderBy(x => x.RawPath).ToArray())
            {
                string bySizeName = $"by-disksize-{realDisk.Length}";

                if (ret.Any(x => x.Name == bySizeName) == false)
                {
                    ret.Add(new RawDiskItemData(bySizeName, realDisk.RawPath, realDisk.Type, realDisk.Length, false, realDisk.Name));
                }
            }

            ret = ret.OrderBy(x => x.Name).ToList();
        }
        else
        {
            // fdisk を用いてディスクを列挙し、Disk ID を取得
            var result = await EasyExec.ExecAsync(Lfs.UnixGetFullPathFromCommandName("fdisk"), "-l", easyOutputMaxSize: 1_000_000, timeout: 30 * 1000, cancel: cancel, throwOnErrorExitCode: false);

            int mode = 0;

            string fdiskCurrentDiskName = "";

            Dictionary<string, string> diskIdDict = new Dictionary<string, string>();

            try
            {
                foreach (var line in result.OutputStr._GetLines(trim: true))
                {
                    if (line.StartsWith("Disk /"))
                    {
                        if (mode == 0)
                        {
                            mode = 1;
                            fdiskCurrentDiskName = line.Substring(5);
                            int index1 = fdiskCurrentDiskName.IndexOf(":");
                            if (index1 != -1)
                            {
                                fdiskCurrentDiskName = fdiskCurrentDiskName.Substring(0, index1);
                            }
                        }
                    }
                    else if (line == "")
                    {
                        mode = 0;
                        fdiskCurrentDiskName = "";
                    }
                    else if (line.StartsWith("Disk identifier:"))
                    {
                        string diskId = line.Substring(16).Trim().ToLowerInvariant();
                        if (fdiskCurrentDiskName != "")
                        {
                            diskIdDict.TryAdd(fdiskCurrentDiskName, diskId);

                            fdiskCurrentDiskName = "";
                        }
                    }
                }
            }
            catch { }

            // パーティションを列挙
            List<RawDiskItemData> tmpDiskItemList = new List<RawDiskItemData>();

            List<string> diskDirPathList = new();

            diskDirPathList.Add("/dev/disk/by-id/");
            diskDirPathList.Add("/dev/disk/by-path/");

            Dictionary<string, bool> realDiskPathDict = new();

            foreach (var diskDirPath in diskDirPathList)
            {
                try
                {
                    var diskObjects = await Lfs.EnumDirectoryAsync(diskDirPath, cancel: cancel);

                    foreach (var diskObj in diskObjects.OrderBy(x => x.Name))
                    {
                        if (diskObj.IsSymbolicLink && diskObj.SymbolicLinkTarget._IsFilled())
                        {
                            string diskRealPath = Lfs.PathParser.NormalizeUnixStylePathWithRemovingRelativeDirectoryElements(Lfs.PathParser.Combine(diskDirPath, diskObj.SymbolicLinkTarget));

                            try
                            {
                                bool isPartition = true;

                                if (diskObj.Name.Substring(diskObj.Name.Length - 6).StartsWith("-part") == false &&
                                    diskObj.Name.Substring(diskObj.Name.Length - 7).StartsWith("-part") == false &&
                                    diskObj.Name.Substring(diskObj.Name.Length - 8).StartsWith("-part") == false)
                                {
                                    isPartition = false;
                                }

                                long diskSize = await UnixApi.GetBlockDeviceSizeAsync(diskRealPath, cancel);

                                var diskItem = new RawDiskItemData(
                                    diskDirPath.Split("/", StringSplitOptions.RemoveEmptyEntries).Last() + "-" + Str.MakeVerySafeAsciiOnlyNonSpaceFileName(diskObj.Name.Replace(":", "_"), true), diskRealPath, RawDiskItemType.FixedMedia, diskSize,
                                    isPartition,
                                    (isPartition ? "by-partname-" : "by-devname-") + Lfs.PathParser.GetFileName(diskRealPath));

                                tmpDiskItemList.Add(diskItem);

                                realDiskPathDict.Add(diskItem.RawPath, isPartition);
                            }
                            catch
                            {
                            }
                        }
                    }
                }
                catch { }
            }

            foreach (var diskRealPath in realDiskPathDict.ToArray())
            {
                try
                {
                    long diskSize = await UnixApi.GetBlockDeviceSizeAsync(diskRealPath.Key, cancel);

                    var diskItem = new RawDiskItemData((diskRealPath.Value ? "by-partname-" : "by-devname-") + Lfs.PathParser.GetFileName(diskRealPath.Key), diskRealPath.Key, RawDiskItemType.FixedMedia, diskSize, diskRealPath.Value);

                    tmpDiskItemList.Add(diskItem);

                    realDiskPathDict.Add(diskItem.RawPath, diskRealPath.Value);
                }
                catch
                {
                }
            }

            try
            {
                var partUuidObjects = await Lfs.EnumDirectoryAsync("/dev/disk/by-partuuid/", cancel: cancel);

                foreach (var partUuidObj in partUuidObjects.OrderBy(x => x.Name))
                {
                    if (partUuidObj.IsSymbolicLink && partUuidObj.SymbolicLinkTarget._IsFilled())
                    {
                        string partRealPath = Lfs.PathParser.NormalizeUnixStylePathWithRemovingRelativeDirectoryElements(Lfs.PathParser.Combine("/dev/disk/by-partuuid/", partUuidObj.SymbolicLinkTarget));
                        string uuid = partUuidObj.Name;

                        var a = tmpDiskItemList.Where(x => x.IsPartition == false && x.AliasOf._IsEmpty() && partRealPath.StartsWith(x.RawPath)).OrderByDescending(x => x.RawPath.Length).ThenBy(x => x.RawPath).FirstOrDefault();

                        if (a != null)
                        {
                            // パーティションを包含する親ディスク
                            string byPartUuidName = $"have-partuuid-{Str.MakeVerySafeAsciiOnlyNonSpaceFileName(uuid, true)}";

                            byPartUuidName = byPartUuidName.ToLowerInvariant();

                            if (tmpDiskItemList.Any(x => x.Name == byPartUuidName) == false)
                            {
                                tmpDiskItemList.Add(new RawDiskItemData(byPartUuidName, a.RawPath, a.Type, a.Length, a.IsPartition, a.Name));
                            }
                        }

                        a = tmpDiskItemList.Where(x => x.IsPartition && x.AliasOf._IsEmpty() && partRealPath.StartsWith(x.RawPath)).OrderByDescending(x => x.RawPath.Length).ThenBy(x => x.RawPath).FirstOrDefault();

                        if (a != null)
                        {
                            // パーティションそのもの
                            string byPartUuidName = $"is-partuuid-{Str.MakeVerySafeAsciiOnlyNonSpaceFileName(uuid, true)}";

                            byPartUuidName = byPartUuidName.ToLowerInvariant();

                            if (tmpDiskItemList.Any(x => x.Name == byPartUuidName) == false)
                            {
                                tmpDiskItemList.Add(new RawDiskItemData(byPartUuidName, a.RawPath, a.Type, a.Length, a.IsPartition, a.Name));
                            }
                        }
                    }
                }
            }
            catch { }

            try
            {
                var labelObjects = await Lfs.EnumDirectoryAsync("/dev/disk/by-label/", cancel: cancel);

                foreach (var labelObj in labelObjects.OrderBy(x => x.Name))
                {
                    if (labelObj.IsSymbolicLink && labelObj.SymbolicLinkTarget._IsFilled())
                    {
                        string partRealPath = Lfs.PathParser.NormalizeUnixStylePathWithRemovingRelativeDirectoryElements(Lfs.PathParser.Combine("/dev/disk/by-label/", labelObj.SymbolicLinkTarget));
                        string name = labelObj.Name;

                        var a = tmpDiskItemList.Where(x => x.IsPartition == false && x.AliasOf._IsEmpty() && partRealPath.StartsWith(x.RawPath)).OrderByDescending(x => x.RawPath.Length).ThenBy(x => x.RawPath).FirstOrDefault();

                        if (a != null)
                        {
                            // パーティションを包含する親ディスク
                            string byLabelName = $"have-label-{Str.MakeVerySafeAsciiOnlyNonSpaceFileName(name, true)}";

                            if (tmpDiskItemList.Any(x => x.Name == byLabelName) == false)
                            {
                                tmpDiskItemList.Add(new RawDiskItemData(byLabelName, a.RawPath, a.Type, a.Length, a.IsPartition, a.Name));
                            }
                        }

                        a = tmpDiskItemList.Where(x => x.IsPartition && x.AliasOf._IsEmpty() && partRealPath.StartsWith(x.RawPath)).OrderByDescending(x => x.RawPath.Length).ThenBy(x => x.RawPath).FirstOrDefault();

                        if (a != null)
                        {
                            // パーティションそのもの
                            string byLabelName = $"is-label-{Str.MakeVerySafeAsciiOnlyNonSpaceFileName(name, true)}";

                            if (tmpDiskItemList.Any(x => x.Name == byLabelName) == false)
                            {
                                tmpDiskItemList.Add(new RawDiskItemData(byLabelName, a.RawPath, a.Type, a.Length, a.IsPartition, a.Name));
                            }
                        }
                    }
                }
            }
            catch { }

            try
            {
                var partLabelObjects = await Lfs.EnumDirectoryAsync("/dev/disk/by-partlabel/", cancel: cancel);

                foreach (var partLabelObj in partLabelObjects.OrderBy(x => x.Name))
                {
                    if (partLabelObj.IsSymbolicLink && partLabelObj.SymbolicLinkTarget._IsFilled())
                    {
                        string partRealPath = Lfs.PathParser.NormalizeUnixStylePathWithRemovingRelativeDirectoryElements(Lfs.PathParser.Combine("/dev/disk/by-partlabel/", partLabelObj.SymbolicLinkTarget));
                        string name = partLabelObj.Name;

                        var a = tmpDiskItemList.Where(x => x.IsPartition == false && x.AliasOf._IsEmpty() && partRealPath.StartsWith(x.RawPath)).OrderByDescending(x => x.RawPath.Length).ThenBy(x => x.RawPath).FirstOrDefault();

                        if (a != null)
                        {
                            // パーティションを包含する親ディスク
                            string byLabelName = $"have-partlabel-{Str.MakeVerySafeAsciiOnlyNonSpaceFileName(name, true)}";

                            if (tmpDiskItemList.Any(x => x.Name == byLabelName) == false)
                            {
                                tmpDiskItemList.Add(new RawDiskItemData(byLabelName, a.RawPath, a.Type, a.Length, a.IsPartition, a.Name));
                            }
                        }

                        a = tmpDiskItemList.Where(x => x.IsPartition && x.AliasOf._IsEmpty() && partRealPath.StartsWith(x.RawPath)).OrderByDescending(x => x.RawPath.Length).ThenBy(x => x.RawPath).FirstOrDefault();

                        if (a != null)
                        {
                            // パーティションそのもの
                            string byLabelName = $"is-partlabel-{Str.MakeVerySafeAsciiOnlyNonSpaceFileName(name, true)}";

                            if (tmpDiskItemList.Any(x => x.Name == byLabelName) == false)
                            {
                                tmpDiskItemList.Add(new RawDiskItemData(byLabelName, a.RawPath, a.Type, a.Length, a.IsPartition, a.Name));
                            }
                        }
                    }
                }
            }
            catch { }

            foreach (var realDisk in tmpDiskItemList.Where(x => x.AliasOf._IsEmpty()).OrderBy(x => x.RawPath).ToArray())
            {
                string bySizeName = $"by-disksize-{realDisk.Length}";

                if (tmpDiskItemList.Any(x => x.Name == bySizeName) == false)
                {
                    tmpDiskItemList.Add(new RawDiskItemData(bySizeName, realDisk.RawPath, realDisk.Type, realDisk.Length, realDisk.IsPartition, realDisk.Name));
                }

                if (diskIdDict.TryGetValue(realDisk.RawPath, out string? diskId))
                {
                    tmpDiskItemList.Add(new RawDiskItemData($"is-diskid-" + diskId, realDisk.RawPath, realDisk.Type, realDisk.Length, realDisk.IsPartition, realDisk.Name));
                }
            }

            try
            {
                var mountPoints = await GetLinuxMountInfoListAsync(cancel);

                foreach (var p in mountPoints.OrderBy(x => x.Target).ThenBy(x => x.Source))
                {
                    if (p.Target.StartsWith("/") && p.Source.StartsWith("/"))
                    {
                        var a = tmpDiskItemList.Where(x => x.IsPartition == false && x.AliasOf._IsEmpty() && p.Source.StartsWith(x.RawPath)).OrderByDescending(x => x.RawPath.Length).ThenBy(x => x.RawPath).FirstOrDefault();

                        if (a != null)
                        {
                            // パーティションを包含する親ディスク
                            string byMountName = $"have-mountpoint-{GeneratePrintableSafeFileNameFromUnixFullPath(p.Target)}";

                            if (tmpDiskItemList.Any(x => x.Name == byMountName) == false)
                            {
                                tmpDiskItemList.Add(new RawDiskItemData(byMountName, a.RawPath, a.Type, a.Length, a.IsPartition, a.Name));
                            }
                        }

                        a = tmpDiskItemList.Where(x => x.IsPartition && x.AliasOf._IsEmpty() && p.Source.StartsWith(x.RawPath)).OrderByDescending(x => x.RawPath.Length).ThenBy(x => x.RawPath).FirstOrDefault();

                        if (a != null)
                        {
                            // パーティションそのもの
                            string byMountName = $"is-mountpoint-{GeneratePrintableSafeFileNameFromUnixFullPath(p.Target)}";

                            if (tmpDiskItemList.Any(x => x.Name == byMountName) == false)
                            {
                                tmpDiskItemList.Add(new RawDiskItemData(byMountName, a.RawPath, a.Type, a.Length, a.IsPartition, a.Name));
                            }
                        }
                    }
                }
            }
            catch { }

            tmpDiskItemList.OrderBy(x => x.Name)._DoForEach(x => ret.Add(x));
        }

        return ret;
    }
}

[Flags]
public enum RawDiskItemType
{
    Unknown = 0,
    FixedMedia,
    RemovableMedia,
}

public class RawDiskItemData
{
    public string Name { get; }
    public string RawPath { get; }
    public RawDiskItemType Type { get; }
    public long Length { get; }
    public string AliasOf { get; }
    public bool IsPartition { get; }

    public RawDiskItemData(string name, string rawPath, RawDiskItemType type, long length, bool isPartiton, string aliasOf = "")
    {
        Name = name;
        RawPath = rawPath;
        Type = type;
        Length = length;
        AliasOf = aliasOf;
        IsPartition = isPartiton;
    }
}

public abstract class RawDiskFileSystemBasedVfsFile : VfsFile
{
    protected new RawDiskFileSystem FileSystem => (RawDiskFileSystem)base.FileSystem;
    public RawDiskItemData ItemData { get; }

    public RawDiskFileSystemBasedVfsFile(RawDiskFileSystem fileSystem, RawDiskItemData itemData) : base(fileSystem)
    {
        this.ItemData = itemData;
    }
}

public class RawDiskFileSystemParams : VirtualFileSystemParams
{
    public RawDiskFileSystemParams(FileSystemMode mode = FileSystemMode.Default) : base(mode)
    {
    }
}

public abstract class RawDiskFileSystem : VirtualFileSystem
{
    public new RawDiskFileSystemParams Params => (RawDiskFileSystemParams)base.Params;

    readonly AsyncLock Lock = new AsyncLock();

    public RawDiskFileSystem(RawDiskFileSystemParams? param = null) : base(param ?? new RawDiskFileSystemParams())
    {
        try
        {
            RescanRawDisksInternalAsync()._GetResult();
        }
        catch (Exception ex)
        {
            this._DisposeSafe(ex);
            throw;
        }
    }

    protected override async Task<FileSystemEntity[]> EnumDirectoryImplAsync(string directoryPath, EnumDirectoryFlags flags, string wildcard, CancellationToken cancel = default)
    {
        await RescanRawDisksInternalAsync(cancel);

        return await base.EnumDirectoryImplAsync(directoryPath, flags, wildcard, cancel);
    }

    protected abstract Task<IEnumerable<RawDiskItemData>> RescanRawDisksImplAsync(CancellationToken cancel = default);
    protected abstract Task<RawDiskFileSystemBasedVfsFile> CreateRawDiskFileImplAsync(RawDiskItemData item, CancellationToken cancel = default);

    public async Task<IEnumerable<RawDiskItemData>> EnumRawDisksAsync(CancellationToken cancel = default)
    {
        return await RescanRawDisksImplAsync(cancel);
    }

    async Task RescanRawDisksInternalAsync(CancellationToken cancel = default)
    {
        using (await Lock.LockWithAwait(cancel))
        {
            FileSystemEntity[] existingFiles = await base.EnumDirectoryImplAsync("/", EnumDirectoryFlags.None, "", cancel);

            foreach (var existingFile in existingFiles)
            {
                if (existingFile.IsFile)
                {
                    await base.DeleteFileImplAsync(existingFile.FullPath, cancel: cancel);
                }
            }

            var diskItems = await RescanRawDisksImplAsync(cancel);

            foreach (var item in diskItems)
            {
                if (item.Name._InStr("/") == false && item.Name._InStr(@"\") == false)
                {
                    await using (this.AddFileAsync(new FileParameters("/" + item.Name, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite),
                        async (newFilename, newFileOption, c) =>
                        {
                            return await CreateRawDiskFileImplAsync(item, cancel);
                        }, noOpen: true)._GetResult())
                    {
                    }
                }
            }
        }
    }

}


#endif

