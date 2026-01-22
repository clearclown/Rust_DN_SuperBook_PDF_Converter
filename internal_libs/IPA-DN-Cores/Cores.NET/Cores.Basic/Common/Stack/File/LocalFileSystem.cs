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
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;
using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.FileProviders;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using System.Security.AccessControl;

#pragma warning disable CS1998
#pragma warning disable CA1416 // プラットフォームの互換性の検証

namespace IPA.Cores.Basic;

public static partial class CoresConfig
{
    public static partial class LocalFileSystemSettings
    {
        public static readonly Copenhagen<int> SparseFileMinBlockSize = 32 * 1024;
    }
}

public partial class LocalFileSystem : FileSystem
{
    public const long Win32MaxAlternateStreamSize = 65536;
    public const int Win32MaxAlternateStreamNum = 16;

    public static LocalFileSystem Local { get; private set; } = null!;

    public static Utf8BomFileSystem LocalUtf8 { get; private set; } = null!;

    public static ChrootFileSystem AppRoot { get; private set; } = null!;

    public static StaticModule Module { get; } = new StaticModule(ModuleInit, ModuleFree);

    static void ModuleInit()
    {
        Local = new LocalFileSystem();

        LocalUtf8 = new Utf8BomFileSystem(new Utf8BomFileSystemParam(Local));

        var opt = new ChrootFileSystemParam(Local, Local.PathParser.Combine(Env.AppRootDir, "TestData"), FileSystemMode.ReadOnly);
        opt.EasyAccessPathFindMode.Set(EasyAccessPathFindMode.MostMatch);
        AppRoot = new ChrootFileSystem(opt);
    }

    static void ModuleFree()
    {
        AppRoot._DisposeSafe();
        AppRoot = null!;

        LocalUtf8._DisposeSafe();
        LocalUtf8 = null!;

        Local._DisposeSafe();
        Local = null!;
    }


    private LocalFileSystem(FileSystemMode mode = FileSystemMode.Default)
        : base(new FileSystemParams(Env.LocalPathParser, mode))
    {
    }

    protected override Task<string> GetAbsolutePathFromRelativePathIfSupportedImplAsync(string path, FileFlags flags, CancellationToken cancel)
    {
        if (this.PathParser.IsAbsolutePath(path, true))
        {
            return path._TR();
        }

        string cd = this.GetCurrentDirectory();

        cd = this.PathParser.NormalizeDirectorySeparatorAndCheckIfAbsolutePath(cd);

        path = this.PathParser.Combine(cd, path);

        return path._TR();
    }

    protected override Task<FileObject> CreateFileImplAsync(FileParameters fileParams, CancellationToken cancel = default)
    {
        return LocalFileObject.CreateFileAsync(this, fileParams, cancel);
    }

    IReadOnlyList<FileSystemEntity> Win32EnumUncPathSpecialDirectory(string normalizedUncPath, EnumDirectoryFlags flags, CancellationToken cancel = default)
    {
        List<FileSystemEntity> ret = new List<FileSystemEntity>();

        IReadOnlyList<string> shareNameList = Win32ApiUtil.EnumNetworkShareDirectories(normalizedUncPath);

        DateTimeOffset now = DateTimeOffset.Now;

        // root directory
        FileSystemEntity root = new FileSystemEntity(
            name: ".",
            fullPath: normalizedUncPath,
            size: 0,
            physicalSize: 0,
            attributes: FileAttributes.Directory,
            creationTime: now,
            lastWriteTime: now,
            lastAccessTime: now
            );

        ret.Add(root);

        foreach (string shareName in shareNameList)
        {
            FileSystemEntity entity = new FileSystemEntity(
                name: shareName,
                fullPath: normalizedUncPath + @"\" + shareName,
                size: 0,
                physicalSize: 0,
                attributes: FileAttributes.Directory,
                creationTime: now,
                lastWriteTime: now,
                lastAccessTime: now
                );

            ret.Add(entity);
        }

        return ret;
    }

    protected override async Task<FileSystemEntity[]> EnumDirectoryImplAsync(string directoryPath, EnumDirectoryFlags flags, string wildcard, CancellationToken cancel = default)
    {
        // Where(directoryPath);
        if (Env.IsWindows && Win32ApiUtil.IsUncServerRootPath(directoryPath, out string? normalizedUncPath))
            return Win32EnumUncPathSpecialDirectory(normalizedUncPath, flags, cancel).ToArray();

        DirectoryInfo di = new DirectoryInfo(directoryPath);

        if (di.Attributes.Bit(FileAttributes.Directory) == false)
        {
            throw new CoresException($"Path '{directoryPath}' is not a directory. This is a file.");
        }

        List<FileSystemEntity> o = new List<FileSystemEntity>();

        FileSystemEntity currentDirectory = ConvertFileSystemInfoToFileSystemEntity(di);
        currentDirectory.Name = ".";

        o.Add(currentDirectory);

        foreach (FileSystemInfo info in di.GetFileSystemInfos().Where(x => x.Exists))
        {
            FileSystemEntity entity = ConvertFileSystemInfoToFileSystemEntity(info);

            // Actual file size
            if (flags.Bit(EnumDirectoryFlags.NoGetPhysicalSize) == false)
                if (entity.IsDirectory == false)
                    if (entity.Attributes.Bit(FileAttributes.Compressed) || entity.Attributes.Bit(FileAttributes.SparseFile))
                        entity.PhysicalSize = GetPhysicalFileSizeInternal(info.FullName, entity.Size);

            if (entity.IsSymbolicLink)
            {
                entity.SymbolicLinkTarget = ReadSymbolicLinkTarget(entity.FullPath);
            }

            if (this.PathParser.WildcardMatch(entity.Name, wildcard))
            {
                o.Add(entity);
            }
        }

        return o.ToArray();
    }

    static long GetPhysicalFileSizeInternal(string path, long defaultSizeOnError)
    {
        try
        {
            if (Env.IsWindows)
            {
                return Win32ApiUtil.GetCompressedFileSize(path);
            }
        }
        catch { }
        return defaultSizeOnError;
    }

    protected override async Task CreateDirectoryImplAsync(string directoryPath, FileFlags flags = FileFlags.None, CancellationToken cancel = default)
    {
        if (Directory.Exists(directoryPath) == false)
        {
            Directory.CreateDirectory(directoryPath);

            if (Env.IsWindows)
            {
                try
                {
                    if (flags.Bit(FileFlags.OnCreateSetCompressionFlag))
                        await Win32ApiUtil.SetCompressionFlagAsync(directoryPath, true, true, cancel);
                    else if (flags.Bit(FileFlags.OnCreateRemoveCompressionFlag))
                        await Win32ApiUtil.SetCompressionFlagAsync(directoryPath, true, false, cancel);
                }
                catch { }
            }
        }
    }

    protected override async Task DeleteDirectoryImplAsync(string directoryPath, bool recursive, CancellationToken cancel = default)
    {
        Directory.Delete(directoryPath, recursive);
    }

    protected override Task<bool> IsFileExistsImplAsync(string path, CancellationToken cancel = default)
    {
        // Where(path);
        return Task.FromResult(File.Exists(path));
    }

    protected override Task<bool> IsDirectoryExistsImplAsync(string path, CancellationToken cancel = default)
    {
        // Where(path);
        if (Env.IsWindows && Win32ApiUtil.IsUncServerRootPath(path, out string? normalizedUncPath))
        {
            // UNC server root path
            try
            {
                return Task.FromResult(Win32EnumUncPathSpecialDirectory(normalizedUncPath, EnumDirectoryFlags.None, cancel).Where(x => x.FullPath._IsSamei(normalizedUncPath)).Any());
            }
            catch { return Task.FromResult(false); }
        }

        return Task.FromResult(Directory.Exists(path));
    }

    static FileSystemEntity ConvertFileSystemInfoToFileSystemEntity(FileSystemInfo info)
    {
        FileSystemEntity ret = new FileSystemEntity(
            name: info.Name,
            fullPath: info.FullName,
            size: info.Attributes.Bit(FileAttributes.Directory) ? 0 : ((FileInfo)info).Length,
            attributes: info.Attributes,
            creationTime: info.CreationTime._AsDateTimeOffset(true),
            lastWriteTime: info.LastWriteTime._AsDateTimeOffset(true),
            lastAccessTime: info.LastAccessTime._AsDateTimeOffset(true)
        );

        ret.PhysicalSize = ret.Size;

        return ret;
    }

    static FileMetadata ConvertFileSystemInfoToFileMetadata(FileSystemInfo info, FileMetadataGetFlags flags)
    {
        FileMetadata ret = new FileMetadata()
        {
            Size = info.Attributes.Bit(FileAttributes.Directory) ? 0 : ((FileInfo)info).Length,
            Attributes = flags.Bit(FileMetadataGetFlags.NoAttributes) == false ? info.Attributes : (FileAttributes?)null,
            CreationTime = flags.Bit(FileMetadataGetFlags.NoTimes) == false ? info.CreationTime._AsDateTimeOffset(true) : (DateTimeOffset?)null,
            LastWriteTime = flags.Bit(FileMetadataGetFlags.NoTimes) == false ? info.LastWriteTime._AsDateTimeOffset(true) : (DateTimeOffset?)null,
            LastAccessTime = flags.Bit(FileMetadataGetFlags.NoTimes) == false ? info.LastAccessTime._AsDateTimeOffset(true) : (DateTimeOffset?)null,
            IsDirectory = info.Attributes.Bit(FileAttributes.Directory),
        };

        ret.PhysicalSize = ret.Size;

        return ret;
    }

    void SetDirectoryCreationTimeUtc(string path, DateTime dt) => Directory.SetCreationTimeUtc(path, dt);
    void SetDirectoryLastWriteTimeUtc(string path, DateTime dt) => Directory.SetLastWriteTimeUtc(path, dt);
    void SetDirectoryLastAccessTimeUtc(string path, DateTime dt) => Directory.SetLastAccessTimeUtc(path, dt);

    void SetDirectoryAttributes(string path, FileAttributes fileAttributes)
    {
        DirectoryInfo di = new DirectoryInfo(path);
        di.Attributes = fileAttributes;
    }

    void SetFileCreationTimeUtc(string path, DateTime dt)
    {
        try
        {
            File.SetCreationTimeUtc(path, dt);
        }
        catch (UnauthorizedAccessException)
        {
            if (Env.IsWindows)
            {
                Win32ApiUtil.SetCreationTime(path, dt._AsDateTimeOffset(false), false, true);
                return;
            }

            throw;
        }
    }

    void SetFileLastWriteTimeUtc(string path, DateTime dt)
    {
        try
        {
            File.SetLastWriteTimeUtc(path, dt);
        }
        catch (UnauthorizedAccessException)
        {
            if (Env.IsWindows)
            {
                Win32ApiUtil.SetLastWriteTime(path, dt._AsDateTimeOffset(false), false, true);
                return;
            }

            throw;
        }
    }

    void SetFileLastAccessTimeUtc(string path, DateTime dt)
    {
        try
        {
            File.SetLastAccessTimeUtc(path, dt);
        }
        catch (UnauthorizedAccessException)
        {
            if (Env.IsWindows)
            {
                Win32ApiUtil.SetLastAccessTime(path, dt._AsDateTimeOffset(false), false, true);
                return;
            }

            throw;
        }
    }

    void SetFileAttributes(string path, FileAttributes fileAttributes)
    {
        File.SetAttributes(path, fileAttributes);
        //PalWin32FileSystem.SetAttributes(path, fileAttributes);
    }

    void SetFileMetadataToFileSystemInfo(FileSystemInfo info, FileMetadata metadata)
    {
        if (metadata.CreationTime is DateTimeOffset creationTime)
            info.CreationTimeUtc = creationTime.UtcDateTime;

        if (metadata.LastWriteTime is DateTimeOffset lastWriteTime)
            info.LastWriteTimeUtc = lastWriteTime.UtcDateTime;

        if (metadata.LastAccessTime is DateTimeOffset lastAccessTime)
            info.LastAccessTimeUtc = lastAccessTime.UtcDateTime;

        if (metadata.Attributes is FileAttributes attr)
            info.Attributes = attr;
    }

    string? ReadSymbolicLinkTarget(string linkPath)
    {
        if (Env.IsUnix)
        {
            return UnixApi.ReadLink(linkPath);
        }
        else
        {
            // Currently this will return error in Windows
            return null;
        }
    }

    static string? Win32GetFileOrDirectorySecuritySddlInternal(string path, bool isDirectory, AccessControlSections section)
    {
        if (Env.IsWindows == false) return null;
        try
        {
            FileSystemSecurity sec = isDirectory ? (FileSystemSecurity)(new DirectorySecurity(path, section)) : (FileSystemSecurity)(new FileSecurity(path, section));
            string ret = sec.GetSecurityDescriptorSddlForm(section);

            return ret;
        }
        catch
        {
            return null;
        }
    }

    void Win32SetFileOrDirectorySecuritySddlInternal(string path, bool isDirectory, string? sddl, AccessControlSections section)
    {
        if (Env.IsWindows == false) return;
        if (sddl._IsEmpty()) return;

        bool setProtected = false;
        if (sddl.StartsWith("!"))
        {
            sddl = sddl.Substring(1);
            setProtected = true;
        }

        if (sddl._IsEmpty()) return;

        FileSystemSecurity sec = isDirectory ? (FileSystemSecurity)(new DirectorySecurity()) : (FileSystemSecurity)(new FileSecurity());

        sec.SetSecurityDescriptorSddlForm(sddl, section);

        if (setProtected)
        {
            if (section == AccessControlSections.Audit)
                sec.SetAuditRuleProtection(true, true);

            if (section == AccessControlSections.Access)
                sec.SetAccessRuleProtection(true, true);
        }

        if (isDirectory == false)
        {
            FileInfo fi = new FileInfo(path);
            fi.SetAccessControl((FileSecurity)sec);
        }
        else
        {
            DirectoryInfo di = new DirectoryInfo(path);
            di.SetAccessControl((DirectorySecurity)sec);
        }
    }

    async Task SetFileAlternateStreamMetadataAsync(string path, FileAlternateStreamMetadata data, CancellationToken cancel = default)
    {
        if (data._IsEmpty())
            return;

        if (Env.IsWindows)
        {
            var currentStreams = Win32ApiUtil.EnumAlternateStreams(path, Win32MaxAlternateStreamSize, Win32MaxAlternateStreamNum);

            if (currentStreams == null)
                throw new FileException(path, "Win32EnumAlternateStreamsInternal() failed.");

            // Copy streams
            if (data.Items != null)
            {
                foreach (var d in data.Items)
                {
                    if (d._IsFilled())
                    {
                        if (d.Name._IsFilled())
                        {
                            if (d.Name.IndexOfAny(PathParser.PossibleDirectorySeparators) == -1)
                            {
                                string fullpath = path + d.Name;

                                try
                                {
                                    await this.WriteDataToFileAsync(fullpath, d.Data, FileFlags.None, cancel: cancel);
                                }
                                catch
                                {
                                    await this.WriteDataToFileAsync(fullpath, d.Data, FileFlags.BackupMode, cancel: cancel);
                                }
                            }
                        }
                    }
                }
            }

            // Remove any streams on the destination which is not existing on the source
            foreach (var existingStream in currentStreams)
            {
                if (existingStream.Item1._IsEmpty() == false)
                {
                    if (existingStream.Item2 <= Win32MaxAlternateStreamSize)
                    {
                        if (data.Items!.Select(x => x.Name).Where(x => x._IsSamei(existingStream.Item1)).Any() == false)
                        {
                            string fullpath = path + existingStream.Item1;

                            try
                            {
                                Con.WriteDebug($"Deleting {fullpath}");
                                await this.DeleteFileImplAsync(fullpath);
                            }
                            catch (Exception ex)
                            {
                                ex._Debug();
                            }
                        }
                    }
                }
            }
        }
    }

    async Task<FileAlternateStreamMetadata?> GetFileAlternateStreamMetadataAsync(string path, CancellationToken cancel = default)
    {
        FileAlternateStreamMetadata ret = new FileAlternateStreamMetadata();

        List<FileAlternateStreamItemMetadata> itemList = new List<FileAlternateStreamItemMetadata>();

        if (Env.IsWindows)
        {
            var list = Win32ApiUtil.EnumAlternateStreams(path, Win32MaxAlternateStreamSize, Win32MaxAlternateStreamNum);

            if (list == null)
            {
                return null;
            }

            foreach (var item in list)
            {
                if (item.Item1._IsFilled() && item.Item2 >= 1)
                {
                    int readSize = (int)Math.Min(item.Item2, Win32MaxAlternateStreamSize);
                    string fileName = path + item.Item1;

                    Memory<byte> memory;

                    try
                    {
                        memory = await this.ReadDataFromFileAsync(fileName, (int)Win32MaxAlternateStreamSize, FileFlags.None, cancel);
                    }
                    catch
                    {
                        memory = await this.ReadDataFromFileAsync(fileName, (int)Win32MaxAlternateStreamSize, FileFlags.BackupMode, cancel);
                    }

                    FileAlternateStreamItemMetadata newItem = new FileAlternateStreamItemMetadata()
                    {
                        Name = item.Item1,
                        Data = memory.ToArray(),
                    };

                    itemList.Add(newItem);
                }
            }
        }

        ret.Items = itemList.ToArray();

        return ret;
    }

    public static void RunStartupTest()
    {
        if (Env.IsWindows)
        {
            var sddl = Win32GetFileOrDirectorySecuritySddlInternal(Path.Combine(Env.Win32_SystemDir, "ntoskrnl.exe"), false, AccessControlSections.Access);
            Dbg.TestTrue(sddl!._InStri(":"));
        }
    }

    FileSecurityMetadata? GetFileOrDirectorySecurityMetadata(string path, bool isDirectory)
    {
        FileSecurityMetadata ret = new FileSecurityMetadata();
        string? sddl;

        if (Env.IsWindows)
        {
            sddl = Win32GetFileOrDirectorySecuritySddlInternal(path, isDirectory, AccessControlSections.Owner);
            if (sddl._IsFilled())
                ret.Owner = new FileSecurityOwner() { Win32OwnerSddl = sddl };

            sddl = Win32GetFileOrDirectorySecuritySddlInternal(path, isDirectory, AccessControlSections.Group);
            if (sddl._IsFilled())
                ret.Group = new FileSecurityGroup() { Win32GroupSddl = sddl };

            sddl = Win32GetFileOrDirectorySecuritySddlInternal(path, isDirectory, AccessControlSections.Access);
            if (sddl._IsFilled())
                ret.Acl = new FileSecurityAcl() { Win32AclSddl = sddl };

            sddl = Win32GetFileOrDirectorySecuritySddlInternal(path, isDirectory, AccessControlSections.Audit);
            if (sddl._IsFilled())
                ret.Audit = new FileSecurityAudit() { Win32AuditSddl = sddl };
        }

        return ret._FilledOrDefault();
    }

    async Task ApplyFileOrDirectorySpecialOperationFlagsMedatataAsync(string path, bool isDirectory, FileSpecialOperationFlags operationFlags, CancellationToken cancel = default)
    {
        await Util.DoMultipleActionsAsync(MultipleActionsFlag.AllOk, cancel,
            async () =>
            {
                if (operationFlags.Bit(FileSpecialOperationFlags.SetCompressionFlag))
                    if (Env.IsWindows)
                        await Win32ApiUtil.SetCompressionFlagAsync(path, isDirectory, true, cancel);
            },
            async () =>
            {
                if (operationFlags.Bit(FileSpecialOperationFlags.RemoveCompressionFlag))
                    if (Env.IsWindows)
                        await Win32ApiUtil.SetCompressionFlagAsync(path, isDirectory, false, cancel);
            }
            );
    }

    void SetFileOrDirectorySecurityMetadata(string path, bool isDirectory, FileSecurityMetadata data)
    {
        if (Env.IsWindows)
        {
            if (data._IsFilled())
            {
                Util.DoMultipleActions(MultipleActionsFlag.AllOk, default,
                    () => Win32SetFileOrDirectorySecuritySddlInternal(path, isDirectory, data?.Owner?.Win32OwnerSddl, AccessControlSections.Owner),
                    () => Win32SetFileOrDirectorySecuritySddlInternal(path, isDirectory, data?.Group?.Win32GroupSddl, AccessControlSections.Group),
                    () => Win32SetFileOrDirectorySecuritySddlInternal(path, isDirectory, data?.Acl?.Win32AclSddl, AccessControlSections.Access),
                    () => Win32SetFileOrDirectorySecuritySddlInternal(path, isDirectory, data?.Audit?.Win32AuditSddl, AccessControlSections.Audit)
                    );
            }
        }
    }

    protected override Task<string> NormalizePathImplAsync(string path, CancellationToken cancel = default)
    {
        // The exception will thrown if the path is not an absolute path
        return Task.FromResult(Path.GetFullPath(PathParser.NormalizeDirectorySeparatorAndCheckIfAbsolutePath(path)));
    }

    protected override async Task<FileMetadata> GetFileMetadataImplAsync(string path, FileMetadataGetFlags flags = FileMetadataGetFlags.DefaultAll, CancellationToken cancel = default)
    {
        // Where(path);
        FileInfo fileInfo = new FileInfo(path);

        if (fileInfo.Exists == false)
            throw new FileNotFoundException($"The file '{path}' not found.");

        FileMetadata ret = ConvertFileSystemInfoToFileMetadata(fileInfo, flags);

        if (flags.Bit(FileMetadataGetFlags.NoSecurity) == false)
        {
            ret.Security = GetFileOrDirectorySecurityMetadata(path, false);
        }

        var obtainedAttributes = ret.Attributes ?? 0;

        if (flags.Bit(FileMetadataGetFlags.NoPhysicalFileSize) == false)
            if (obtainedAttributes.Bit(FileAttributes.Compressed) || obtainedAttributes.Bit(FileAttributes.SparseFile))
                ret.PhysicalSize = GetPhysicalFileSizeInternal(path, ret.Size);

        // Try to open to retrieve the actual physical file
        if (flags.Bit(FileMetadataGetFlags.NoPreciseFileSize) == false)
        {
            FileObject? f = null;
            try
            {
                f = await LocalFileObject.CreateFileAsync(this, new FileParameters(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, FileFlags.None), cancel);
            }
            catch
            {
                try
                {
                    f = await LocalFileObject.CreateFileAsync(this, new FileParameters(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, FileFlags.BackupMode), cancel);
                }
                catch { }
            }

            if (f != null)
            {
                try
                {
                    ret.Size = await f.GetFileSizeAsync();
                }
                finally
                {
                    f._DisposeSafe();
                }
            }
        }

        if (flags.Bit(FileMetadataGetFlags.NoAlternateStream) == false)
        {
            try
            {
                ret.AlternateStream = await GetFileAlternateStreamMetadataAsync(path, cancel);
            }
            catch (Exception ex)
            {
                Dbg.WriteError($"{path} - {ex.Message}");
            }
        }

        return ret;
    }

    protected async override Task SetFileMetadataImplAsync(string path, FileMetadata metadata, CancellationToken cancel = default)
    {
        await Util.DoMultipleActionsAsync(MultipleActionsFlag.AllOk, cancel,
            () =>
            {
                if (metadata.AlternateStream != null)
                    SetFileAlternateStreamMetadataAsync(path, metadata.AlternateStream, cancel)._GetResult();
                return Task.CompletedTask;
            },
            () =>
            {
                if (metadata.CreationTime != null)
                    SetFileCreationTimeUtc(path, ((DateTimeOffset)metadata.CreationTime).UtcDateTime);
                return Task.CompletedTask;
            },
            () =>
            {
                if (metadata.LastWriteTime != null)
                    SetFileLastWriteTimeUtc(path, ((DateTimeOffset)metadata.LastWriteTime).UtcDateTime);
                return Task.CompletedTask;
            },
            () =>
            {
                if (metadata.LastAccessTime != null)
                    SetFileLastAccessTimeUtc(path, ((DateTimeOffset)metadata.LastAccessTime).UtcDateTime);
                return Task.CompletedTask;
            },
            () =>
            {
                if (metadata.Attributes != null)
                    SetFileAttributes(path, (FileAttributes)metadata.Attributes);
                return Task.CompletedTask;
            },
            async () =>
            {
                await ApplyFileOrDirectorySpecialOperationFlagsMedatataAsync(path, false, metadata.SpecialOperationFlags, cancel);
            },
            () =>
            {
                if (metadata.Security != null)
                    SetFileOrDirectorySecurityMetadata(path, false, metadata.Security);
                return Task.CompletedTask;
            }
            );
    }

    public void UnixSetPermissions(string path, UnixPermissions permissions)
    {
        if (Env.IsUnix == false) return;

        if (UnixApi.ChMod(path, (int)permissions) < 0)
        {
            throw new CoresException($"UnixApi.ChMod failed. Path = '{path}'");
        }
    }

    protected override async Task<FileMetadata> GetDirectoryMetadataImplAsync(string path, FileMetadataGetFlags flags = FileMetadataGetFlags.DefaultAll, CancellationToken cancel = default)
    {
        if (Env.IsWindows && Win32ApiUtil.IsUncServerRootPath(path, out string? normalizedUncPath))
        {
            // UNC server root path
            DateTimeOffset now = DateTimeOffset.Now;
            return new FileMetadata()
            {
                Attributes = FileAttributes.Directory,
                CreationTime = now,
                LastWriteTime = now,
                LastAccessTime = now,
                IsDirectory = true,
            };
        }

        DirectoryInfo dirInfo = new DirectoryInfo(path);

        if (dirInfo.Exists == false)
            throw new FileNotFoundException($"The directory '{path}' not found.");

        FileMetadata ret = ConvertFileSystemInfoToFileMetadata(dirInfo, flags);

        if (flags.Bit(FileMetadataGetFlags.NoSecurity) == false)
        {
            ret.Security = GetFileOrDirectorySecurityMetadata(path, true);
        }

        return ret;
    }


    protected async override Task SetDirectoryMetadataImplAsync(string path, FileMetadata metadata, CancellationToken cancel = default)
    {
        await Util.DoMultipleActionsAsync(MultipleActionsFlag.AllOk, cancel,
            () =>
            {
                if (metadata.Attributes != null)
                    SetDirectoryAttributes(path, (FileAttributes)metadata.Attributes);
                return Task.CompletedTask;
            },
            async () =>
            {
                await ApplyFileOrDirectorySpecialOperationFlagsMedatataAsync(path, true, metadata.SpecialOperationFlags, cancel);
            },
            () =>
            {
                if (metadata.CreationTime != null)
                    SetDirectoryCreationTimeUtc(path, ((DateTimeOffset)metadata.CreationTime).UtcDateTime);
                return Task.CompletedTask;
            },
            () =>
            {
                if (metadata.LastWriteTime != null)
                    SetDirectoryLastWriteTimeUtc(path, ((DateTimeOffset)metadata.LastWriteTime).UtcDateTime);
                return Task.CompletedTask;
            },
            () =>
            {
                if (metadata.LastAccessTime != null)
                    SetDirectoryLastAccessTimeUtc(path, ((DateTimeOffset)metadata.LastAccessTime).UtcDateTime);
                return Task.CompletedTask;
            },
            () =>
            {
                if (metadata.Security != null)
                    SetFileOrDirectorySecurityMetadata(path, true, metadata.Security);
                return Task.CompletedTask;
            }
            );
    }

    protected override async Task DeleteFileImplAsync(string path, FileFlags flags = FileFlags.None, CancellationToken cancel = default)
    {
        if (flags.Bit(FileFlags.BackupMode) || flags.Bit(FileFlags.ForceClearReadOnlyOrHiddenBitsOnNeed))
        {
            await this.TryAddOrRemoveAttributeFromExistingFileAsync(path, 0, FileAttributes.ReadOnly, cancel);
        }

        File.Delete(path);
    }

    protected override async Task MoveFileImplAsync(string srcPath, string destPath, CancellationToken cancel = default)
    {
        File.Move(srcPath, destPath);
    }

    protected override async Task MoveDirectoryImplAsync(string srcPath, string destPath, CancellationToken cancel = default)
    {
        Directory.Move(srcPath, destPath);
    }

    public void EnableBackupPrivilege()
    {
        if (Env.IsWindows)
        {
            Util.DoMultipleActions(MultipleActionsFlag.AllOk, default,
                () => Win32Api.EnablePrivilege(Win32Api.Advapi32.SeBackupPrivilege, true),
                () => Win32Api.EnablePrivilege(Win32Api.Advapi32.SeRestorePrivilege, true),
                () => Win32Api.EnablePrivilege(Win32Api.Advapi32.SeTakeOwnershipPrivilege, true)
                );
        }
    }

    public ValueHolder<IDisposable> EnterDisableMediaInsertionPrompt()
    {
        IDisposable token = new EmptyDisposable();

        if (Env.IsWindows)
        {
            token = Win32Api.Win32DisableMediaInsertionPrompt.Create();
        }

        return new ValueHolder<IDisposable>(x => x._DisposeSafe(), token);
    }

    protected override IFileProvider CreateFileProviderForWatchImpl(string root)
    {
        return new PhysicalFileProvider(root);
    }

    public Task<FileObject> CreateDynamicTempFileAsync(string extension = ".dat", string prefix = "", CancellationToken cancel = default)
    {
        extension = extension._NonNullTrim();
        prefix = PP.MakeSafeFileName(prefix);

        if (prefix._IsFilled()) prefix = prefix + "_";

        if (extension.StartsWith(".") == false) extension = "." + extension;

        string guid = Str.NewGuid();

        string fn = prefix + guid + extension;

        string fullPath = PP.Combine(Env.MyLocalTempDir, Consts.FileNames.MyDynamicTempSubDirName, guid.Substring(0, 2), fn);

        return this.CreateAsync(fullPath, flags: FileFlags.AutoCreateDirectory | FileFlags.DeleteFileOnClose | FileFlags.DeleteParentDirOnClose);
    }
    public FileObject CreateDynamicTempFile(string extension = ".dat", string prefix = "", CancellationToken cancel = default)
        => CreateDynamicTempFileAsync(extension, prefix, cancel)._GetResult();

    public string GetFullPath(string src) => Path.GetFullPath(src);
    public string GetCurrentDirectory() => Directory.GetCurrentDirectory();
}

public class LocalFileObject : FileObject
{
    string? _PhysicalFinalPath = null;
    public override string FinalPhysicalPath => _PhysicalFinalPath._FilledOrException();

    protected LocalFileObject(FileSystem fileSystem, FileParameters fileParams) : base(fileSystem, fileParams) { }

    protected FileStream BaseStream = null!;
    long CurrentPosition;
    bool UseAsyncMode = false;

    public long? FixedSize = null;

    public static async Task<FileObject> CreateFileAsync(LocalFileSystem fileSystem, FileParameters fileParams, CancellationToken cancel = default)
    {
        // Where(fileParams.Path);
        cancel.ThrowIfCancellationRequested();

        /*if (fileParams.Path._InStri("hello.txt"))
        {
            BreakPoint();
        }*/

        LocalFileObject f = new LocalFileObject(fileSystem, fileParams);
        try
        {
            await f.InternalInitAsync(cancel);

            return f;
        }
        catch
        {
            f._DisposeSafe();
            throw;
        }
    }

    static FileStream Win32CreateFileStreamInternal(string path, FileMode mode, FileAccess access, FileShare share, int bufferSize, FileOptions options)
    {
        FileStream ret;

        if ((options & (FileOptions)Win32Api.Kernel32.FileOperations.FILE_FLAG_BACKUP_SEMANTICS) != 0)
        {
            // Use our private FileStream implementation
            ret = PalWin32FileStream.Create(path, mode, access, share, bufferSize, options);
        }
        else
        {
            // Use normal FileStream
            ret = new FileStream(path, mode, access, share, bufferSize, options);
        }

        return ret;
    }

    protected async Task InternalInitAsync(CancellationToken cancel = default)
    {
        cancel.ThrowIfCancellationRequested();

        try
        {
            FileOptions options = FileOptions.None;

            if (this.FileParams.Flags.Bit(FileFlags.Async))
            {
                options |= FileOptions.Asynchronous;
                UseAsyncMode = true;
            }

            if (Env.IsWindows)
            {
                if (FileParams.Flags.Bit(FileFlags.BackupMode))
                    options |= (FileOptions)Win32Api.Kernel32.FileOperations.FILE_FLAG_BACKUP_SEMANTICS;

                if (FileParams.Access.Bit(FileAccess.Write))
                {
                    if (FileParams.Flags.Bit(FileFlags.BackupMode) || FileParams.Flags.Bit(FileFlags.ForceClearReadOnlyOrHiddenBitsOnNeed))
                    {
                        FileAttributes attributesToRemove = 0;

                        if (FileParams.Mode == FileMode.Create)
                            attributesToRemove |= FileAttributes.Hidden | FileAttributes.ReadOnly;

                        if (FileParams.Mode == FileMode.Append || FileParams.Mode == FileMode.Open || FileParams.Mode == FileMode.OpenOrCreate || FileParams.Mode == FileMode.Truncate)
                            attributesToRemove |= FileAttributes.ReadOnly;

                        if (attributesToRemove != 0)
                            await FileSystem.TryAddOrRemoveAttributeFromExistingFileAsync(FileParams.Path, 0, attributesToRemove, cancel);
                    }
                }

                FileMode fileMode = (FileParams.Mode != FileMode.Append) ? FileParams.Mode : FileMode.OpenOrCreate;

                BaseStream = Win32CreateFileStreamInternal(FileParams.Path, fileMode, FileParams.Access, FileParams.Share, 4096, options);
            }
            else
            {
                FileMode fileMode = (FileParams.Mode != FileMode.Append) ? FileParams.Mode : FileMode.OpenOrCreate;

                BaseStream = new FileStream(FileParams.Path, fileMode, FileParams.Access, FileParams.Share, 4096, FileOptions.Asynchronous);
            }

            if (FileParams.Mode == FileMode.Append)
                BaseStream.Seek(0, SeekOrigin.End);

            if (Env.IsWindows)
            {
                if (FileParams.Mode == FileMode.Create || FileParams.Mode == FileMode.CreateNew || FileParams.Mode == FileMode.OpenOrCreate || FileParams.Mode == FileMode.Append)
                {
                    if (BaseStream.Length == 0)
                    {
                        // Special operations on file creation
                        await Util.DoMultipleActionsAsync(MultipleActionsFlag.IgnoreError, cancel,
                            async () =>
                            {
                                if (FileParams.Flags.Bit(FileFlags.OnCreateSetCompressionFlag))
                                    await Win32ApiUtil.SetCompressionFlagAsync(BaseStream.SafeFileHandle, true, FileParams.Path, cancel);
                            },
                            async () =>
                            {
                                if (FileParams.Flags.Bit(FileFlags.OnCreateRemoveCompressionFlag))
                                    await Win32ApiUtil.SetCompressionFlagAsync(BaseStream.SafeFileHandle, false, FileParams.Path, cancel);
                            }
                            );
                    }
                }

                if (FileParams.Access.Bit(FileAccess.Write))
                {
                    await Util.DoMultipleActionsAsync(MultipleActionsFlag.IgnoreError, cancel,
                        async () =>
                        {
                            if (FileParams.Flags.Bit(FileFlags.SparseFile))
                                await SetAsSparseFileAsync();
                        });
                }
            }

            string physicalFinalPathTmp = "";
            if (Env.IsWindows)
            {
                try
                {
                    physicalFinalPathTmp = Win32ApiUtil.GetFinalPath(BaseStream.SafeFileHandle);
                }
                catch { }
            }

            if (physicalFinalPathTmp._IsEmpty())
                physicalFinalPathTmp = FileParams.Path;

            _PhysicalFinalPath = physicalFinalPathTmp;

            if (BaseStream.CanSeek == false)
            {
                this.FileCharacteristics |= FileCharacteristics.IsNonSeekable;
            }

            if (this.FileCharacteristics.Bit(FileCharacteristics.IsNonSeekable) == false)
            {
                this.CurrentPosition = BaseStream.Position;
            }

            if (Env.IsLinux)
            {
                if (physicalFinalPathTmp.StartsWith("/dev/") && BaseStream.Length == 0)
                {
                    // Linux のブロックデバイスの実サイズは、blockdev コマンドで取得する
                    long blockDevSize = await UnixApi.GetBlockDeviceSizeAsync(physicalFinalPathTmp, cancel);

                    if (blockDevSize >= 1)
                    {
                        this.FixedSize = blockDevSize;
                    }
                }
            }

            if (this.FileCharacteristics.Bit(FileCharacteristics.IsNonSeekable) == false)
            {
                InitAndCheckFileSizeAndPosition(this.CurrentPosition, await GetFileSizeImplAsync(cancel), cancel);
            }
            else
            {
                InitAndCheckFileSizeAndPosition(this.CurrentPosition, 0, cancel);
            }
        }
        catch
        {
            BaseStream._DisposeSafe();
            BaseStream = null!;
            throw;
        }
    }

    bool isSparseFile = false;
    async Task SetAsSparseFileAsync(CancellationToken cancel = default)
    {
        if (this.FileCharacteristics.Bit(FileCharacteristics.IsNonSeekable) == false) return;
        if (Env.IsWindows == false) return;
        if (isSparseFile) return;

        await Win32ApiUtil.SetSparseFileAsync(BaseStream.SafeFileHandle, FileParams.Path, cancel);

        isSparseFile = true;

        this.MicroOperationSize = int.MaxValue;
    }

    protected override async Task CloseImplAsync()
    {
        BaseStream._DisposeSafe();
        BaseStream = null!;
    }

    protected override async Task<long> GetFileSizeImplAsync(CancellationToken cancel = default)
    {
        if (this.FileCharacteristics.Bit(FileCharacteristics.IsNonSeekable))
        {
            return -1;
        }

        return this.FixedSize ?? BaseStream.Length;
    }

    protected override async Task SetFileSizeImplAsync(long size, CancellationToken cancel = default)
    {
        if (this.FileCharacteristics.Bit(FileCharacteristics.IsNonSeekable))
        {
            throw new CoresLibException($"This file object doesn't support SetFileSize, but SetFileSize({size}) is called");
        }

        long? fixedSizeCopy = this.FixedSize;

        if (fixedSizeCopy.HasValue)
        {
            if (size != fixedSizeCopy.Value)
            {
                throw new CoresLibException($"This file object has fixed size ({this.FixedSize}), but SetFileSize({size}) is called");
            }
        }

        try
        {
            long oldFileSize = BaseStream.Length;

            BaseStream.SetLength(size);

            if (Env.IsWindows)
            {
                if (oldFileSize < size)
                {
                    // In Windows, Use the FSCTL_SET_ZERO_DATA ioctl to ensure zero-clear the region.
                    // See https://docs.microsoft.com/en-us/windows/desktop/api/fileapi/nf-fileapi-setendoffile
                    // "The SetEndOfFile function can be used to truncate or extend a file. If the file is extended,
                    //  the contents of the file between the old end of the file and the new end of the file are not defined."
                    await FileZeroClearDataAsync(oldFileSize, size - oldFileSize, cancel);
                }
            }

            this.CurrentPosition = BaseStream.Position;
        }
        catch
        {
            // When Any error occurs, we need to obtain the position.
            try
            {
                this.CurrentPosition = BaseStream.Position;
            }
            catch
            {
                // Failed to obtain the position.
                this.CurrentPosition = long.MinValue;
            }
            throw;
        }
    }

    protected override async Task FlushImplAsync(CancellationToken cancel = default)
    {
        //if (UseAsyncMode)
        //    await BaseStream.FlushAsync(cancel);
        //else
        try
        {
            BaseStream.Flush(true); // 2022/03/15: true にしないと物理ディスクに Flush されません。また、Async メソッドではディスクへの Flush がサポートされていません。
        }
        catch { } // 例外が発生するかも知れませんが、無視します。
    }

    protected override async Task<int> ReadRandomImplAsync(long position, Memory<byte> data, CancellationToken cancel = default)
    {
        checked
        {
            try
            {
                if (this.CurrentPosition != position)
                {
                    if (this.FileCharacteristics.Bit(FileCharacteristics.IsNonSeekable))
                    {
                        throw new CoresLibException($"The file object is non seekable, and current position = {this.CurrentPosition}, but ReadRandomImplAsync(position = {position}) is called");
                    }

                    BaseStream.Seek(position, SeekOrigin.Begin);
                    this.CurrentPosition = position;
                }

                int ret;

                if (UseAsyncMode)
                    ret = await BaseStream.ReadAsync(data, cancel);
                else
                    ret = BaseStream.Read(data.Span);

                if (ret >= 1)
                {
                    this.CurrentPosition += ret;
                }

                return ret;
            }
            catch
            {
                if (this.FileCharacteristics.Bit(FileCharacteristics.IsNonSeekable) == false)
                {
                    // When ReadAsync occurs the error, we need to obtain the position.
                    try
                    {
                        this.CurrentPosition = BaseStream.Position;
                    }
                    catch
                    {
                        // Failed to obtain the position.
                        this.CurrentPosition = long.MinValue;
                    }
                }
                throw;
            }
        }
    }

    protected override async Task WriteRandomImplAsync(long position, ReadOnlyMemory<byte> data, CancellationToken cancel = default)
    {
        if (this.FileParams.Flags.Bit(FileFlags.WriteOnlyIfChanged))
        {
            try
            {
                using (MemoryHelper.FastAllocMemoryWithUsing(data.Length, out Memory<byte> readBuffer))
                {
                    int readSize = await ReadRandomImplAsync(position, readBuffer, cancel);

                    if (readSize == data.Length)
                        if (data.Span.SequenceEqual(readBuffer.Span))
                        {
                            return;
                        }
                }
            }
            catch { }
        }

        if (isSparseFile && this.FileCharacteristics.Bit(FileCharacteristics.IsNonSeekable) == false)
        {
            await WriteRandomAutoSparseAsync(position, data, cancel);
        }
        else
        {
            await WriteRandomImplInternalAsync(position, data, cancel);
        }
    }

    async Task WriteRandomImplInternalAsync(long position, ReadOnlyMemory<byte> data, CancellationToken cancel = default)
    {
        checked
        {
            try
            {
                if (this.CurrentPosition != position)
                {
                    if (this.FileCharacteristics.Bit(FileCharacteristics.IsNonSeekable))
                    {
                        throw new CoresLibException($"The file object is non seekable, and current position = {this.CurrentPosition}, but ReadRandomImplAsync(position = {position}) is called");
                    }

                    BaseStream.Seek(position, SeekOrigin.Begin);
                    this.CurrentPosition = position;
                }

                if (UseAsyncMode)
                    await BaseStream.WriteAsync(data, cancel);
                else
                    BaseStream.Write(data.Span);

                this.CurrentPosition += data.Length;
            }
            catch
            {
                if (this.FileCharacteristics.Bit(FileCharacteristics.IsNonSeekable) == false)
                {
                    // When WriteAsync occurs the error, we need to obtain the position.
                    try
                    {
                        this.CurrentPosition = BaseStream.Position;
                    }
                    catch
                    {
                        // Failed to obtain the position.
                        this.CurrentPosition = long.MinValue;
                    }
                }
                throw;
            }
        }
    }

    static readonly ReadOnlyMemory<byte> FillZeroBlockSize = ZeroedMemory<byte>.Memory;
    async Task FileZeroClearDataAsync(long position, long size, CancellationToken cancel = default)
    {
        checked
        {
            if (position < 0) throw new ArgumentOutOfRangeException("position");
            if (size < 0) throw new ArgumentOutOfRangeException("size");
            if (this.FileCharacteristics.Bit(FileCharacteristics.IsNonSeekable)) throw new CoresLibException($"The file object is non seekable, but FileZeroClearDataAsync is called");
            if (size == 0) return;

            if (Env.IsWindows)
            {
                // Use FSCTL_SET_ZERO_DATA first
                try
                {
                    await Win32ApiUtil.FileZeroClearAsync(this.BaseStream.SafeFileHandle, this.FileParams.Path, position, size);
                    return;
                }
                catch { }
            }

            // Use normal zero-clear method
            while (size >= 1)
            {
                long currentSize = Math.Min(size, FillZeroBlockSize.Length);

                await WriteRandomImplInternalAsync(position, FillZeroBlockSize.Slice(0, (int)currentSize), cancel);

                //debug
                //Memory<byte> xxx = new byte[(int)currentSize];
                //xxx.Span.Fill((byte)'-');
                //await WriteRandomImplInternalAsync(position, xxx, cancel);

                position += currentSize;
                size -= currentSize;
            }
        }
    }

    async Task WriteRandomAutoSparseAsync(long position, ReadOnlyMemory<byte> data, CancellationToken cancel = default)
    {
        if (position < 0) throw new ArgumentOutOfRangeException("position");
        if (this.FileCharacteristics.Bit(FileCharacteristics.IsNonSeekable)) throw new CoresLibException($"The file object is non seekable, but WriteRandomAutoSparseAsync is called");

        checked
        {
            long currentFileSize = BaseStream.Length;
            long expandingSize = (position + data.Length) - currentFileSize;

            long existingDataRegionSize;
            long expandingDataRegionSize;

            if (expandingSize >= 1)
            {
                existingDataRegionSize = Math.Max(data.Length - expandingSize, 0);
                expandingDataRegionSize = data.Length - existingDataRegionSize;
            }
            else
            {
                existingDataRegionSize = data.Length;
                expandingDataRegionSize = 0;
            }

            long existingDataRegionPosition = position;
            long expandingDataRegionPosition = position + existingDataRegionSize;

            Debug.Assert(existingDataRegionPosition >= position);
            Debug.Assert(expandingDataRegionPosition >= existingDataRegionPosition);
            Debug.Assert(existingDataRegionSize >= 0);
            Debug.Assert(expandingDataRegionSize >= 0);
            Debug.Assert((existingDataRegionSize + expandingDataRegionSize) == data.Length);

            if (existingDataRegionSize >= 1)
            {
                var subData = data.Slice(0, (int)existingDataRegionSize);
                var chunkList = Util.GetSparseChunks(subData, CoresConfig.LocalFileSystemSettings.SparseFileMinBlockSize.Value);

                foreach (var chunk in chunkList)
                {
                    if (chunk.IsSparse)
                    {
                        await FileZeroClearDataAsync(existingDataRegionPosition + chunk.Offset, chunk.Size, cancel);
                    }
                    else
                    {
                        await WriteRandomImplInternalAsync(existingDataRegionPosition + chunk.Offset, chunk.Memory, cancel);
                    }
                }
            }

            if (expandingDataRegionSize >= 1)
            {
                var subData = data.Slice((int)existingDataRegionSize, (int)expandingDataRegionSize);
                var chunkList = Util.GetSparseChunks(subData, CoresConfig.LocalFileSystemSettings.SparseFileMinBlockSize.Value);

                foreach (var chunk in chunkList)
                {
                    if (chunk.IsSparse)
                    {
                        long newFileSize = expandingDataRegionPosition + chunk.Offset + chunk.Size;
                        Debug.Assert(this.BaseStream.Length <= newFileSize);

                        BaseStream.SetLength(newFileSize);

                        if (Env.IsWindows)
                        {
                            // In Windows, Use the FSCTL_SET_ZERO_DATA ioctl to ensure zero-clear the region.
                            // See https://docs.microsoft.com/en-us/windows/desktop/api/fileapi/nf-fileapi-setendoffile
                            // "The SetEndOfFile function can be used to truncate or extend a file. If the file is extended,
                            //  the contents of the file between the old end of the file and the new end of the file are not defined."
                            await FileZeroClearDataAsync(expandingDataRegionPosition + chunk.Offset, chunk.Size, cancel);
                        }

                        this.CurrentPosition = BaseStream.Position;
                    }
                    else
                    {
                        await WriteRandomImplInternalAsync(expandingDataRegionPosition + chunk.Offset, chunk.Memory, cancel);
                    }
                }
            }
        }
    }
}

