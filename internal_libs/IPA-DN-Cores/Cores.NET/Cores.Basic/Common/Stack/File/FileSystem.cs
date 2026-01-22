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
using Microsoft.Extensions.FileProviders;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using System.Text;
using Microsoft.Extensions.Primitives;
using System.Collections.Concurrent;

namespace IPA.Cores.Basic;

public static partial class CoresConfig
{
    public static partial class FileSystemSettings
    {
        public static readonly Copenhagen<int> PooledHandleLifetime = 60 * 1000;
        public static readonly Copenhagen<int> MaxPooledHandleCount = 256;
        public static readonly Copenhagen<int> DefaultMicroOperationSize = 8 * 1024 * 1024; // 8MB
    }

    public static partial class FileSystemEventWatcherSettings
    {
        public static readonly Copenhagen<int> DefaultPollingInterval = 5 * 1000;
    }

    public static partial class FileUtilSettings
    {
        public static readonly Copenhagen<int> FileCopyBufferSize = 1 * 1024 * 1024; // 1MB
        public static readonly Copenhagen<int> DefaultSectorSize = 4096;
    }
}

public class RandomAccessFileObject : FileObject
{
    readonly IRandomAccess<byte> Access;

    public RandomAccessFileObject(FileSystem fileSystem, FileParameters fileParams, IRandomAccess<byte> baseRandomAccess) : base(fileSystem, fileParams)
    {
        this.Access = baseRandomAccess;

        InitAndCheckFileSizeAndPosition(0, Access.GetFileSize(true));
    }

    Once Once;
    protected override Task CloseImplAsync()
    {
        if (Once.IsFirstCall())
        {
            this.Access._DisposeSafe();
        }

        return Task.CompletedTask;
    }

    protected override Task FlushImplAsync(CancellationToken cancel = default)
        => Access.FlushAsync(cancel);

    protected override Task<long> GetFileSizeImplAsync(CancellationToken cancel = default)
        => Access.GetFileSizeAsync(cancel: cancel);

    protected override Task<int> ReadRandomImplAsync(long position, Memory<byte> data, CancellationToken cancel = default)
        => Access.ReadRandomAsync(position, data, cancel);

    protected override Task WriteRandomImplAsync(long position, ReadOnlyMemory<byte> data, CancellationToken cancel = default)
        => Access.WriteRandomAsync(position, data, cancel);

    protected override Task SetFileSizeImplAsync(long size, CancellationToken cancel = default)
        => Access.SetFileSizeAsync(size, cancel);
}

public class FileSystemException : Exception
{
    public FileSystemException(string message) : base(message) { }
}

public abstract class FileObject : FileBase
{
    public FileSystem FileSystem { get; }
    public sealed override bool IsOpened => !this.ClosedFlag.IsSet;
    public sealed override Exception? LastError { get; protected set; } = null;

    public int MicroOperationSize { get; set; } = CoresConfig.FileSystemSettings.DefaultMicroOperationSize.Value;

    long InternalPosition = 0;
    long InternalFileSize = 0;
    CancellationTokenSource CancelSource = new CancellationTokenSource();
    CancellationToken CancelToken => CancelSource.Token;

    AsyncLock AsyncLockObj = new AsyncLock();

    protected FileObject(FileSystem fileSystem, FileParameters fileParams) : base(fileParams)
    {
        this.FileSystem = fileSystem;

        Con.WriteTrace($"CreateFile ({FileSystem.ToString()}): '{fileParams.Path}'");
    }

    public override string ToString() => $"FileObject('{FileParams.Path}')";

    protected void InitAndCheckFileSizeAndPosition(long initialPosition, long initialFileSize, CancellationToken cancel = default)
    {
        using (TaskUtil.CreateCombinedCancellationToken(out CancellationToken operationCancel, this.CancelToken, cancel))
        {
            this.InternalPosition = initialPosition;
            this.InternalFileSize = initialFileSize;

            if (this.InternalPosition > this.InternalFileSize)
                throw new FileException(this.FileParams.Path, $"Current position is out of range. Current position: {this.InternalPosition}, File size: {this.InternalFileSize}. (InitAndCheckFileSizeAndPosition)");

            if (this.InternalPosition < 0)
                throw new FileException(this.FileParams.Path, $"Current position is invalid. Current position: {this.InternalPosition}. (InitAndCheckFileSizeAndPosition)");

            if (this.InternalFileSize < 0)
                throw new FileException(this.FileParams.Path, $"Current filesize is invalid. Current filesize: {this.InternalFileSize}. (InitAndCheckFileSizeAndPosition)");
        }
    }

    protected abstract Task<int> ReadRandomImplAsync(long position, Memory<byte> data, CancellationToken cancel = default);
    protected abstract Task WriteRandomImplAsync(long position, ReadOnlyMemory<byte> data, CancellationToken cancel = default);
    protected abstract Task<long> GetFileSizeImplAsync(CancellationToken cancel = default);
    protected abstract Task SetFileSizeImplAsync(long size, CancellationToken cancel = default);
    protected abstract Task FlushImplAsync(CancellationToken cancel = default);
    protected abstract Task CloseImplAsync();

    Once ClosedFlag;

    public sealed override async Task<int> ReadAsync(Memory<byte> data, CancellationToken cancel = default)
    {
        checked
        {
            try
            {
                CheckSequentialAccessProhibited();

                EventListeners.Fire(this, FileObjectEventType.Read);

                if (data.IsEmpty) return 0;

                await using (TaskUtil.CreateCombinedCancellationToken(out CancellationToken operationCancel, this.CancelToken, cancel))
                {
                    using (await AsyncLockObj.LockWithAwait(operationCancel))
                    {
                        CheckIsOpened();

                        CheckAccessBit(FileAccess.Read);

                        if (this.FileParams.Flags.Bit(FileFlags.NoCheckFileSize) == false && this.FileCharacteristics.Bit(FileCharacteristics.IsNonSeekable) == false)
                        {
                            if (this.InternalFileSize < this.InternalPosition)
                            {
                                await GetFileSizeInternalAsync(true, operationCancel);
                                if (this.InternalFileSize < this.InternalPosition)
                                {
                                    await GetFileSizeInternalAsync(true, operationCancel);
                                    throw new FileException(this.FileParams.Path, $"Current position is out of range. Current position: {this.InternalPosition}, File size: {this.InternalFileSize}. (ReadAsync)");
                                }
                            }
                        }

                        long newPosition = this.InternalPosition + data.Length;
                        if (this.FileParams.Flags.Bit(FileFlags.NoPartialRead))
                        {
                            if (this.InternalFileSize < newPosition)
                            {
                                await GetFileSizeInternalAsync(true, operationCancel);
                                if (this.InternalFileSize < newPosition)
                                    throw new FileException(this.FileParams.Path, $"New position is out of range. New position: {newPosition}, File size: {this.InternalFileSize}.");
                            }
                        }

                        long originalPosition = this.InternalPosition;

                        operationCancel.ThrowIfCancellationRequested();

                        try
                        {
                            int r = await TaskUtil.DoMicroReadOperations(async (target, pos, c) =>
                            {
                                return await ReadRandomImplAsync(pos, target, c);
                            },
                            data, MicroOperationSize, this.InternalPosition, operationCancel);

                            if (r < 0) throw new FileException(this.FileParams.Path, $"ReadImplAsync returned {r}.");

                            if (this.FileParams.Flags.Bit(FileFlags.NoPartialRead))
                                if (r != data.Length)
                                    throw new FileException(this.FileParams.Path, $"ReadImplAsync returned {r} while {data.Length} requested.");

                            this.InternalPosition += r;

                            return r;
                        }
                        catch
                        {
                            this.InternalPosition = originalPosition;
                            throw;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                this.LastError = ex;
                throw;
            }
        }
    }

    public sealed override async Task<int> ReadRandomAsync(long position, Memory<byte> data, CancellationToken cancel = default)
    {
        if (this.FileCharacteristics.Bit(FileCharacteristics.IsNonSeekable))
        {
            throw new CoresLibException($"This file object doesn't support SeekAsync, but ReadRandomAsync is called");
        }

        checked
        {
            try
            {
                if (position < 0) throw new ArgumentOutOfRangeException("position < 0");
                EventListeners.Fire(this, FileObjectEventType.ReadRandom);

                if (data.IsEmpty) return 0;

                await using (TaskUtil.CreateCombinedCancellationToken(out CancellationToken operationCancel, this.CancelToken, cancel))
                {
                    using (await AsyncLockObj.LockWithAwait(operationCancel))
                    {
                        CheckIsOpened();

                        CheckAccessBit(FileAccess.Read);

                        if (this.InternalFileSize < position)
                        {
                            await GetFileSizeInternalAsync(true, operationCancel);
                            if (this.InternalFileSize < position)
                                throw new FileException(this.FileParams.Path, $"The random position is out of range. Position: {position}, File size: {this.InternalFileSize}.");
                        }

                        long newPosition = position + data.Length;
                        if (this.FileParams.Flags.Bit(FileFlags.NoPartialRead))
                        {
                            if (this.InternalFileSize < newPosition)
                            {
                                await GetFileSizeInternalAsync(true, operationCancel);
                                if (this.InternalFileSize < newPosition)
                                    throw new FileException(this.FileParams.Path, $"New position is out of range. New position: {newPosition}, File size: {this.InternalFileSize}.");
                            }
                        }

                        operationCancel.ThrowIfCancellationRequested();

                        try
                        {
                            int r = await TaskUtil.DoMicroReadOperations(async (target, pos, c) =>
                            {
                                return await ReadRandomImplAsync(pos, target, c);
                            },
                            data, MicroOperationSize, position, operationCancel);

                            if (r < 0) throw new FileException(this.FileParams.Path, $"ReadImplAsync returned {r}.");

                            if (this.FileParams.Flags.Bit(FileFlags.NoPartialRead))
                                if (r != data.Length)
                                    throw new FileException(this.FileParams.Path, $"ReadImplAsync returned {r} while {data.Length} requested.");

                            return r;
                        }
                        catch
                        {
                            throw;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                this.LastError = ex;
                throw;
            }
        }
    }

    public sealed override async Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancel = default)
    {
        checked
        {
            try
            {
                CheckSequentialAccessProhibited();

                EventListeners.Fire(this, FileObjectEventType.Write);

                if (data.IsEmpty) return;

                await using (TaskUtil.CreateCombinedCancellationToken(out CancellationToken operationCancel, this.CancelToken, cancel))
                {
                    using (await AsyncLockObj.LockWithAwait(operationCancel))
                    {
                        CheckIsOpened();

                        CheckAccessBit(FileAccess.Write);

                        if (this.FileCharacteristics.Bit(FileCharacteristics.IsNonSeekable) == false)
                        {
                            if (this.InternalFileSize < this.InternalPosition)
                            {
                                await GetFileSizeInternalAsync(true, operationCancel);
                                if (this.InternalFileSize < this.InternalPosition)
                                    throw new FileException(this.FileParams.Path, $"Current position is out of range. Current position: {this.InternalPosition}, File size: {this.InternalFileSize}. (WriteAsync)");
                            }
                        }

                        operationCancel.ThrowIfCancellationRequested();

                        await TaskUtil.DoMicroWriteOperations(async (target, pos, c) =>
                        {
                            await WriteRandomImplAsync(pos, target, c);
                        },
                        data, MicroOperationSize, this.InternalPosition, operationCancel);

                        this.InternalPosition += data.Length;

                        if (this.FileCharacteristics.Bit(FileCharacteristics.IsNonSeekable) == false)
                        {
                            if (this.InternalFileSize < this.InternalPosition)
                            {
                                this.InternalFileSize = this.InternalPosition;
                            }
                        }

                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                this.LastError = ex;
                throw;
            }
        }
    }

    public sealed override async Task WriteRandomAsync(long position, ReadOnlyMemory<byte> data, CancellationToken cancel = default)
    {
        checked
        {
            if (this.FileCharacteristics.Bit(FileCharacteristics.IsNonSeekable))
            {
                throw new CoresLibException($"This file object doesn't support SeekAsync, but WriteRandomAsync is called");
            }

            try
            {
                EventListeners.Fire(this, FileObjectEventType.WriteRandom);

                if (data.IsEmpty) return;

                await using (TaskUtil.CreateCombinedCancellationToken(out CancellationToken operationCancel, this.CancelToken, cancel))
                {
                    using (await AsyncLockObj.LockWithAwait(operationCancel))
                    {
                        CheckIsOpened();

                        CheckAccessBit(FileAccess.Write);

                        if (position < 0)
                        {
                            // Append mode
                            position = this.InternalFileSize;
                        }

                        if (this.InternalFileSize < position)
                        {
                            await GetFileSizeInternalAsync(true, operationCancel);

                            if (this.InternalFileSize < position)
                            {
                                await SetFileSizeImplAsync(position, operationCancel);
                            }
                        }

                        operationCancel.ThrowIfCancellationRequested();

                        await TaskUtil.DoMicroWriteOperations(async (target, pos, c) =>
                        {
                            await WriteRandomImplAsync(pos, target, c);

                            if (this.InternalFileSize < (pos + target.Length))
                                this.InternalFileSize = (pos + target.Length);
                        },
                        data, MicroOperationSize, position, operationCancel);

                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                this.LastError = ex;
                throw;
            }
        }
    }

    public sealed override async Task<long> SeekAsync(long offset, SeekOrigin origin, CancellationToken cancel = default)
    {
        checked
        {
            try
            {
                CheckSequentialAccessProhibited();

                EventListeners.Fire(this, FileObjectEventType.Seek);

                await using (TaskUtil.CreateCombinedCancellationToken(out CancellationToken operationCancel, this.CancelToken, cancel))
                {
                    using (await AsyncLockObj.LockWithAwait(operationCancel))
                    {
                        CheckIsOpened();

                        long newPosition = 0;

                        if (origin == SeekOrigin.Begin)
                            newPosition = offset;
                        else if (origin == SeekOrigin.Current)
                            newPosition = this.InternalPosition + offset;
                        else if (origin == SeekOrigin.End)
                            newPosition = (await GetFileSizeInternalAsync(true, operationCancel)) + offset;
                        else
                            throw new FileException(this.FileParams.Path, $"Invalid origin value: {(int)origin}");

                        if (newPosition < 0)
                            throw new FileException(this.FileParams.Path, $"newPosition < 0");

                        if (this.FileCharacteristics.Bit(FileCharacteristics.IsNonSeekable))
                        {
                            if (this.InternalPosition != newPosition)
                            {
                                throw new CoresLibException($"This file object doesn't support SeekAsync, but SeekAsync({newPosition}) is called when InternalPosition = {InternalPosition}");
                            }
                        }

                        if (this.FileCharacteristics.Bit(FileCharacteristics.IsNonSeekable) == false)
                        {
                            if (this.InternalFileSize < newPosition)
                            {
                                await GetFileSizeInternalAsync(true, operationCancel);
                                if (this.InternalFileSize < newPosition)
                                {
                                    if (this.FileParams.Access.Bit(FileAccess.Write) == false)
                                        throw new FileException(this.FileParams.Path, $"New position is out of range. New position: {newPosition}, File size: {this.InternalFileSize}.");
                                }
                            }
                        }

                        if (this.InternalPosition != newPosition)
                        {
                            this.InternalPosition = newPosition;
                        }

                        return this.InternalPosition;
                    }
                }
            }
            catch (Exception ex)
            {
                this.LastError = ex;
                throw;
            }
        }
    }

    public sealed override Task<long> GetCurrentPositionAsync(CancellationToken cancel = default)
    {
        cancel.ThrowIfCancellationRequested();

        CheckSequentialAccessProhibited();

        return Task.FromResult(this.InternalPosition);
    }

    async Task<long> GetFileSizeInternalAsync(bool refresh, CancellationToken cancel = default)
    {
        checked
        {
            try
            {
                if (this.FileCharacteristics.Bit(FileCharacteristics.IsNonSeekable))
                {
                    return -1;
                }

                if (refresh == false)
                    return this.InternalFileSize;

                cancel.ThrowIfCancellationRequested();

                long r = await GetFileSizeImplAsync(cancel);

                if (r < 0)
                    throw new FileException(this.FileParams.Path, $"GetFileSizeImplAsync returned {r}.");

                this.InternalFileSize = r;

                return r;
            }
            catch (Exception ex)
            {
                this.LastError = ex;
                throw;
            }
        }
    }

    public sealed override async Task<long> GetFileSizeAsync(bool refresh = false, CancellationToken cancel = default)
    {
        checked
        {
            try
            {
                await using (TaskUtil.CreateCombinedCancellationToken(out CancellationToken operationCancel, this.CancelToken, cancel))
                {
                    using (await AsyncLockObj.LockWithAwait(operationCancel))
                    {
                        CheckIsOpened();

                        return await GetFileSizeInternalAsync(refresh, cancel);
                    }
                }
            }
            catch (Exception ex)
            {
                this.LastError = ex;
                throw;
            }
        }
    }

    public sealed override async Task SetFileSizeAsync(long size, CancellationToken cancel = default)
    {
        checked
        {
            if (this.FileCharacteristics.Bit(FileCharacteristics.IsNonSeekable))
            {
                throw new CoresLibException($"This file object doesn't support SetFileSize, but SetFileSize({size}) is called");
            }

            if (size < 0)
                throw new ArgumentOutOfRangeException("size < 0");

            try
            {
                EventListeners.Fire(this, FileObjectEventType.SetFileSize);

                await using (TaskUtil.CreateCombinedCancellationToken(out CancellationToken operationCancel, this.CancelToken, cancel))
                {
                    using (await AsyncLockObj.LockWithAwait(operationCancel))
                    {
                        CheckIsOpened();
                        CheckAccessBit(FileAccess.Write);

                        operationCancel.ThrowIfCancellationRequested();

                        await SetFileSizeImplAsync(size, operationCancel);

                        this.InternalFileSize = size;
                    }
                }
            }
            catch (Exception ex)
            {
                this.LastError = ex;
                throw;
            }
        }
    }

    public sealed override async Task FlushAsync(CancellationToken cancel = default)
    {
        try
        {
            EventListeners.Fire(this, FileObjectEventType.Flush);

            await using (TaskUtil.CreateCombinedCancellationToken(out CancellationToken operationCancel, this.CancelToken, cancel))
            {
                using (await AsyncLockObj.LockWithAwait(operationCancel))
                {
                    CheckIsOpened();

                    operationCancel.ThrowIfCancellationRequested();

                    await FlushImplAsync(operationCancel);
                }
            }
        }
        catch (Exception ex)
        {
            this.LastError = ex;
            throw;
        }
    }


    Once DisposeFlag;
    public override async ValueTask DisposeAsync()
    {
        try
        {
            if (DisposeFlag.IsFirstCall() == false) return;
            await DisposeInternalAsync();
        }
        finally
        {
            await base.DisposeAsync();
        }
    }
    protected override void Dispose(bool disposing)
    {
        try
        {
            if (!disposing || DisposeFlag.IsFirstCall() == false) return;
            DisposeInternalAsync()._GetResult();
        }
        finally { base.Dispose(disposing); }
    }
    async Task DisposeInternalAsync()
    {
        Con.WriteTrace($"CloseAsync({this.FileSystem}) '{FileParams.Path}'");

        CancelSource._TryCancelNoBlock();

        if (ClosedFlag.IsSet) return;

        using (await AsyncLockObj.LockWithAwait())
        {
            if (ClosedFlag.IsFirstCall())
            {
                this.LastError = new ApplicationException($"File '{this.FileParams.Path}' is closed.");

                try
                {
                    await CloseImplAsync();
                }
                finally
                {
                    try
                    {
                        EventListeners.Fire(this, FileObjectEventType.Closed);
                    }
                    catch { }

                    if (this.FileParams.Flags.Bit(FileFlags.DeleteFileOnClose))
                    {
                        // ファイルを閉じる際に削除する
                        string fullPath = this.FileParams.Path;

                        if (fullPath._IsFilled() && this.FileSystem.PathParser.IsAbsolutePath(fullPath, true))
                        {
                            try
                            {
                                await this.FileSystem.DeleteFileAsync(fullPath);
                            }
                            catch (Exception ex)
                            {
                                ex._Debug();
                            }

                            // 上位ディレクトリが空ならばそれも削除する
                            if (this.FileParams.Flags.Bit(FileFlags.DeleteParentDirOnClose))
                            {
                                string parentDir = this.FileSystem.PathParser.GetDirectoryName(fullPath);
                                if (parentDir._IsFilled())
                                {
                                    if (this.FileSystem.EnumDirectory(parentDir, false, EnumDirectoryFlags.NoGetPhysicalSize).Any() == false)
                                    {
                                        try
                                        {
                                            this.FileSystem.DeleteDirectory(parentDir);
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
            }
        }
    }
}

public class FileSystemEntity
{
    public FileSystemEntity(string fullPath, string name, FileAttributes attributes, DateTimeOffset creationTime, DateTimeOffset lastWriteTime, DateTimeOffset lastAccessTime, long size = 0, long physicalSize = 0, string? symbolicLinkTarget = null)
    {
        FullPath = fullPath;
        Name = name;
        Size = size;
        PhysicalSize = physicalSize;
        SymbolicLinkTarget = symbolicLinkTarget;
        Attributes = attributes;
        CreationTime = creationTime;
        LastWriteTime = lastWriteTime;
        LastAccessTime = lastAccessTime;
    }

    public string FullPath { get; set; }
    public string Name { get; set; }
    public bool IsDirectory => Attributes.Bit(FileAttributes.Directory);
    public bool IsFile => !IsDirectory;
    public bool IsSymbolicLink => Attributes.Bit(FileAttributes.ReparsePoint);
    public bool IsCurrentDirectory => (Name == ".");
    public bool IsParentDirectory => (Name == "..");
    public bool IsCurrentOrParentDirectory => IsCurrentDirectory | IsParentDirectory;
    public long Size { get; set; }
    public long PhysicalSize { get; set; }
    public string? SymbolicLinkTarget { get; set; }
    public FileAttributes Attributes { get; set; }
    public DateTimeOffset CreationTime { get; set; }
    public DateTimeOffset LastWriteTime { get; set; }
    public DateTimeOffset LastAccessTime { get; set; }

    public override string ToString()
        => this.FullPath;
}

[Flags]
public enum SpecialFileNameKind
{
    Normal = 0,
    CurrentDirectory = 1,
    ParentDirectory = 2,
}

public class FileSystemObjectPool : ObjectPoolBase<FileBase, FileFlags>
{
    public FileSystem FileSystem { get; }
    public FileFlags DefaultFileOperationFlags { get; }
    public bool IsWriteMode { get; }

    public FileSystemObjectPool(FileSystem FileSystem, bool writeMode, int lifeTime, int maxObjects, FileFlags defaultFileOperationFlags = FileFlags.None)
        : base(lifeTime, maxObjects, StrComparer.Get(FileSystem.PathParser.PathStringComparison))
    {
        this.FileSystem = FileSystem;
        this.IsWriteMode = writeMode;

        this.DefaultFileOperationFlags = defaultFileOperationFlags;
        this.DefaultFileOperationFlags |= FileFlags.AutoCreateDirectory | FileFlags.RandomAccessOnly;
    }

    protected override async Task<FileBase> OpenImplAsync(string name, FileFlags flags, CancellationToken cancel)
    {
        if (this.IsWriteMode == false)
        {
            string path = await FileSystem.NormalizePathAsync(name, cancel: cancel);

            return await FileSystem.OpenAsync(path, cancel: cancel, flags: this.DefaultFileOperationFlags | flags);
        }
        else
        {
            string path = await FileSystem.NormalizePathAsync(name, cancel: cancel);

            return await FileSystem.OpenOrCreateAsync(path, cancel: cancel, flags: this.DefaultFileOperationFlags | flags);
        }
    }
}

[Flags]
public enum FileSystemStyle
{
    Windows = 0,
    Linux = 1,
    Mac = 2,
    // append above

    LocalSystem = 31,
}

public class PathParser
{
    public static FileSystemStyle LocalSystemStyle { get; } = Env.IsWindows ? FileSystemStyle.Windows : (Env.IsMac ? FileSystemStyle.Mac : FileSystemStyle.Linux);

    readonly static PathParser[] Cached = new PathParser[(int)Util.GetMaxEnumValue<FileSystemStyle>() + 1];

    public FileSystemStyle Style { get; }
    public char DirectorySeparator { get; }
    public char[] PossibleDirectorySeparators { get; }
    public StringComparison PathStringComparison { get; }
    public StrComparer PathStringComparer { get; }
    public bool IgnoreCase { get; }

    readonly char[] InvalidPathChars;
    readonly char[] InvalidFileNameChars;

    public static PathParser Local { get => PathParser.GetInstance(FileSystemStyle.LocalSystem); }
    public static PathParser Windows { get => PathParser.GetInstance(FileSystemStyle.Windows); }
    public static PathParser Linux { get => PathParser.GetInstance(FileSystemStyle.Linux); }
    public static PathParser Mac { get => PathParser.GetInstance(FileSystemStyle.Mac); }

    public static PathParser GetInstance(FileSystemStyle style = FileSystemStyle.LocalSystem)
    {
        if (style == FileSystemStyle.LocalSystem)
            style = LocalSystemStyle;

        if (Cached[(int)style] == null)
        {
            PathParser newObj = new PathParser(style);
            Cached[(int)style] = newObj;
            return newObj;
        }
        else
        {
            return Cached[(int)style];
        }
    }

    private PathParser(FileSystemStyle style)
    {
        Debug.Assert(style != FileSystemStyle.LocalSystem);

        this.Style = style;

        switch (this.Style)
        {
            case FileSystemStyle.Windows:
                this.DirectorySeparator = '\\';
                this.IgnoreCase = true;
                this.PathStringComparison = StringComparison.OrdinalIgnoreCase;
                this.PossibleDirectorySeparators = new char[] { '\\', '/' };
                break;

            case FileSystemStyle.Mac:
                this.DirectorySeparator = '/';
                this.IgnoreCase = true;
                this.PathStringComparison = StringComparison.OrdinalIgnoreCase;
                this.PossibleDirectorySeparators = new char[] { '/' };
                break;

            default:
                this.DirectorySeparator = '/';
                this.IgnoreCase = false;
                this.PathStringComparison = StringComparison.Ordinal;
                this.PossibleDirectorySeparators = new char[] { '/' };
                break;
        }

        this.PathStringComparer = StrComparer.Get(this.PathStringComparison);

        this.InvalidPathChars = GetInvalidPathChars();
        this.InvalidFileNameChars = GetInvalidFileNameChars();
    }

    public bool WildcardMatch(string fileName, string wildcard)
    {
        if (wildcard._IsEmpty() || wildcard == "*" || wildcard == "*.*")
        {
            return true;
        }

        return fileName._WildcardMatch(wildcard, this.IgnoreCase);
    }

    public bool IsFullPathExcludedByExcludeDirList(string fullPath, IEnumerable<string>? dirList = null)
    {
        dirList ??= Consts.FileNames.StandardExcludeDirNames;

        fullPath = this.NormalizeDirectorySeparator(fullPath, true);

        string[] tokens = fullPath._Split(StringSplitOptions.RemoveEmptyEntries, this.DirectorySeparator);

        foreach (var token in tokens)
        {
            if (dirList.Any(x => x._IsSamei(token)))
            {
                return true;
            }
        }

        return false;
    }

    public string AppendDirectorySeparatorTail(string path, bool normalize = false)
    {
        path = path._NonNull();

        char c = path.LastOrDefault();
        if (this.PossibleDirectorySeparators.Where(x => x == c).Any() == false)
        {
            path += this.DirectorySeparator;
        }

        if (normalize)
        {
            StringBuilder sb = new StringBuilder();
            foreach (char a in path)
            {
                char tmp = a;
                if (this.PossibleDirectorySeparators.Where(x => x == c).Any())
                {
                    tmp = this.DirectorySeparator;
                }
                sb.Append(tmp);
            }
            path = sb.ToString();
        }

        return path;
    }

    static readonly char[] PossibleDirectorySeparatorsForAllPlatform = new char[] { '\\', '/' };
    public string RemoveDangerousDirectoryTraversal(string path)
    {
        path = path._NonNull();

        string[] tokens = path.Split(PossibleDirectorySeparatorsForAllPlatform, StringSplitOptions.RemoveEmptyEntries);

        for (int i = 0; i < tokens.Length; i++)
        {
            string s = tokens[i]._NonNullTrim();

            if (s == "." || s == "..")
                tokens[i] = "";
        }

        return BuildAbsolutePathStringFromElements(tokens);
    }

    public string NormalizeUnixStylePathWithRemovingRelativeDirectoryElements(string path)
    {
        Debug.Assert(this.Style != FileSystemStyle.Windows);

        return BuildAbsolutePathStringFromElements(SplitAbsolutePathToElementsUnixStyle(path));
    }

    public string BuildRelativePathToElements(IEnumerable<string> elements)
    {
        return elements._Combine(this.DirectorySeparator);
    }

    public string NormalizeRelativePath(string path)
    {
        return BuildRelativePathToElements(SplitRelativePathToElements(path));
    }

    public string[] SplitTokens(string path)
    {
        return path.Split(this.PossibleDirectorySeparators, StringSplitOptions.RemoveEmptyEntries);
    }

    public string[] SplitRelativePathToElements(string path)
    {
        path = path._NonNull();

        if (path._IsEmpty())
        {
            throw new ArgumentOutOfRangeException($"Path '{path}' is empty.");
        }

        if (this.IsAbsolutePath(path, true) && (PossibleDirectorySeparators.Where(x => x == path[0]).Any()) == false)
        {
            throw new ArgumentOutOfRangeException($"Path '{path}' is not a relative path.");
        }

        List<string> pathStack = new List<string>();

        string[] tokens = path.Split(this.PossibleDirectorySeparators, StringSplitOptions.RemoveEmptyEntries);

        foreach (string s in tokens)
        {
            string trimmed = s.Trim();
            if (trimmed == ".") { }
            else if (trimmed == "..")
            {
                if (pathStack.Count >= 1)
                {
                    if (pathStack.ElementAt(pathStack.Count - 1)._IsSamei(".."))
                    {
                        pathStack.Add("..");
                    }
                    else
                    {
                        pathStack.RemoveAt(pathStack.Count - 1);
                    }
                }
                else
                {
                    pathStack.Add("..");
                }
            }
            else if (trimmed.Length >= 1 && trimmed[0] == '.' && trimmed.ToCharArray().Where(c => c != '.').Any() == false)
            {
                // "....." 等の '.' のみで構成される文字列
            }
            else
            {
                pathStack.Add(s);
            }
        }

        return pathStack.ToArray();
    }

    public string[] SplitAbsolutePathToElementsUnixStyle(string path, bool allowOnWindows = false, bool noProcessStacking = false)
    {
        if (allowOnWindows == false)
        {
            Debug.Assert(this.Style != FileSystemStyle.Windows);

            if (this.Style == FileSystemStyle.Windows)
            {
                throw new CoresException("SplitAbsolutePathToElementsUnixStyle: this.Style == FileSystemStyle.Windows");
            }
        }

        path = path._NonNull();

        if (path.StartsWith("/") == false)
            throw new ArgumentException($"The speficied path \"{path}\" is not an absolute path.");

        string[] tokens = path.Split(this.PossibleDirectorySeparators, StringSplitOptions.RemoveEmptyEntries);

        if (noProcessStacking)
        {
            return tokens;
        }

        List<string> pathStack = new List<string>();

        foreach (string s in tokens)
        {
            string trimmed = s.Trim();
            if (trimmed == ".") { }
            else if (trimmed == "..")
            {
                if (pathStack.Count >= 1)
                    pathStack.RemoveAt(pathStack.Count - 1);
            }
            else if (trimmed.Length >= 1 && trimmed[0] == '.' && trimmed.ToCharArray().Where(c => c != '.').Any() == false)
            {
                // "....." 等の '.' のみで構成される文字列
            }
            else
            {
                pathStack.Add(s);
            }
        }

        return pathStack.ToArray();
    }

    public string BuildAbsolutePathStringFromElements(IEnumerable<string> elements)
    {
        StringBuilder sb = new StringBuilder();
        foreach (string name in elements)
        {
            if (name != null && name.Length >= 1)
            {
                if (this.Style != FileSystemStyle.Windows || sb.Length >= 1)
                    sb.Append(this.DirectorySeparator);

                sb.Append(name);
            }
        }

        if (sb.Length == 0)
            sb.Append(this.DirectorySeparator);

        return sb.ToString();
    }

    public bool IsLastCharDirectoryDirectorySeparator(string path)
    {
        path = path._NonNull();

        if (path.Length >= 1)
        {
            char c = path.Last();
            return this.PossibleDirectorySeparators.Where(x => x == c).Any();
        }

        return false;
    }

    public string NormalizeDirectorySeparatorIncludeWindowsBackslash(string srcPath)
    {
        srcPath = srcPath._NonNull();

        StringBuilder sb = new StringBuilder();
        foreach (char c in srcPath)
        {
            char d = c;
            if (d == '\\' || d == '/')
            {
                d = this.DirectorySeparator;
            }
            sb.Append(d);
        }
        return sb.ToString();
    }

    public string NormalizeDirectorySeparator(string srcPath, bool includeBackSlashForcefully = false)
    {
        srcPath = srcPath._NonNull();

        int mode = -1;

        char[] possible = PossibleDirectorySeparators;

        if (includeBackSlashForcefully)
            possible = PathParser.Windows.PossibleDirectorySeparators;

        StringBuilder sb = new StringBuilder();
        foreach (char c in srcPath)
        {
            bool isDirectorySeparatorChar = false;
            foreach (char pos in possible)
                if (pos == c) isDirectorySeparatorChar = true;

            char d = c;

            if (isDirectorySeparatorChar)
                d = this.DirectorySeparator;

            if (isDirectorySeparatorChar == false)
            {
                sb.Append(d);
                mode = 0;
            }
            else
            {
                if (mode != 1)
                {
                    sb.Append(d);
                    mode = 1;
                }
                else
                {
                    if (this.Style == FileSystemStyle.Windows)
                    {
                        if (sb.Length == 1)
                        {
                            sb.Append(d);
                        }
                    }
                }
            }
        }
        return sb.ToString();
    }

    public bool IsAbsolutePath(string path, bool normalizeDirectorySeparator = false)
    {
        if (normalizeDirectorySeparator)
            path = NormalizeDirectorySeparator(path);

        if (this.Style == FileSystemStyle.Windows)
        {
            // C:\path
            if (path.Length >= 2 && ((path[0] >= 'a' && path[0] <= 'z') || (path[0] >= 'A' && path[0] <= 'Z')) && path[1] == ':')
                return true;

            // \\server\name
            if (path.Length >= 3 && path[0] == '\\' && path[1] == '\\' && path[2] != '\\')
                return true;

            return false;
        }
        else
        {
            if (path.Length >= 1 && path[0] == '/')
                return true;

            return false;
        }
    }

    public string NormalizeDirectorySeparatorAndCheckIfAbsolutePath(string srcPath)
    {
        srcPath = NormalizeDirectorySeparator(srcPath);

        if (IsAbsolutePath(srcPath) == false)
        {
            throw new ArgumentException($"The specified path \"{srcPath}\" is not an absolute path.");
        }

        return srcPath;
    }

    public string ConvertDirectorySeparatorToOtherSystem(string srcPath, PathParser destPathParser)
    {
        srcPath = srcPath._NonNull();

        StringBuilder sb = new StringBuilder();
        foreach (char c in srcPath)
        {
            char d = c;
            foreach (char sep in this.PossibleDirectorySeparators)
            {
                if (sep == c)
                {
                    d = destPathParser.DirectorySeparator;
                    break;
                }
            }
            sb.Append(d);
        }
        return sb.ToString();
    }

    public void ValidateFileOrDirectoryName(string name)
    {
        if (name == null || name == "")
            throw new ArgumentNullException("The entity name is null or empty.");
        if (IsValidFileOrDirectoryName(name) == false)
            throw new ArgumentException($"The entity name \"{name}\" is invalid to this file system.");
    }

    public bool IsValidFileOrDirectoryName(string name)
    {
        if (name == null || name == "") return false;
        string trimmed = name.Trim();
        if (trimmed == "." || trimmed == "..") return false;

        foreach (char c in name)
        {
            foreach (char sep in this.PossibleDirectorySeparators)
            {
                if (c == sep)
                {
                    return false;
                }
            }

            foreach (char invalidChar in this.InvalidFileNameChars)
            {
                if (c == invalidChar)
                {
                    return false;
                }
            }

            foreach (char invalidChar in this.InvalidPathChars)
            {
                if (c == invalidChar)
                {
                    return false;
                }
            }
        }

        return true;
    }

    public string RemoveLastSeparatorChar(string path)
    {
        path = path._NonNull();

        if (path.All(c => PossibleDirectorySeparators.Where(x => x == c).Any()))
        {
            return path;
        }

        if (path.Length == 3 &&
            ((path[0] >= 'a' && path[0] <= 'z') || (path[0] >= 'A' && path[0] <= 'Z')) &&
            path[1] == ':' &&
            PossibleDirectorySeparators.Where(x => x == path[2]).Any())
        {
            return path;
        }

        while (path.Length >= 1)
        {
            char c = path[path.Length - 1];
            if (PossibleDirectorySeparators.Where(x => x == c).Any())
            {
                path = path.Substring(0, path.Length - 1);
            }
            else
            {
                break;
            }
        }

        return path;
    }

    public bool IsRootDirectory(string path)
    {
        if (path._IsNullOrZeroLen()) throw new CoresEmptyException(nameof(path));

        SepareteDirectoryAndFileName(path, out string dirPath, out string fileName);

        return fileName._IsEmpty();
    }

    public string GetDirectoryName(string path)
    {
        if (path._IsNullOrZeroLen()) throw new CoresEmptyException(nameof(path));
        SepareteDirectoryAndFileName(path, out string dirPath, out _);
        return dirPath;
    }

    public string GetFileName(string path)
    {
        if (path._IsNullOrZeroLen()) throw new CoresEmptyException(nameof(path));
        SepareteDirectoryAndFileName(path, out _, out string fileName);
        return fileName;
    }

    public string GetRelativeDirectoryName(string dirPath, string baseDirPath)
    {
        dirPath = dirPath._NonNull();
        baseDirPath = baseDirPath._NonNull();

        dirPath = this.NormalizeDirectorySeparator(dirPath);
        dirPath = this.RemoveLastSeparatorChar(dirPath);

        baseDirPath = this.NormalizeDirectorySeparator(baseDirPath);
        baseDirPath = this.RemoveLastSeparatorChar(baseDirPath);

        if (this.PathStringComparer.Equals(dirPath, baseDirPath))
        {
            return this.NormalizeDirectorySeparator("./");
        }

        return GetRelativeFileName(dirPath, baseDirPath);
    }

    public string ReplaceFullFilePathExtension(string srcFilePath, string newExtension)
    {
        newExtension = newExtension.Trim();

        if (newExtension.StartsWith(".") == false)
        {
            newExtension = "." + newExtension;
        }

        string dir = PP.GetDirectoryName(srcFilePath);
        string fn1 = PP.GetFileNameWithoutExtension(srcFilePath, false);

        return PP.Combine(dir, fn1 + newExtension);
    }

    public string GetRelativeFileName(string filePath, string baseDirPath)
    {
        filePath = filePath._NonNull();
        baseDirPath = baseDirPath._NonNull();

        baseDirPath = this.NormalizeDirectorySeparator(baseDirPath);
        baseDirPath = this.RemoveLastSeparatorChar(baseDirPath);

        if (baseDirPath.Length == 0) throw new ArgumentException("baseDirName is empty.");

        filePath = this.NormalizeDirectorySeparator(filePath);
        filePath = this.RemoveLastSeparatorChar(filePath);

        if (filePath.Length < baseDirPath.Length)
        {
            throw new ArgumentException($"fileName.Length < baseDirName.Length: \"{filePath}\" < \"{baseDirPath}\"");
        }

        if (filePath.StartsWith(baseDirPath, this.PathStringComparison) == false)
        {
            throw new ArgumentException($"The fileName \"{filePath}\" does not include the baseDirName \"{baseDirPath}\".");
        }

        string ret = filePath.Substring(baseDirPath.Length);

        if (ret.Length >= 1 && this.PossibleDirectorySeparators.Where(x => x == ret[0]).Any())
        {
            ret = ret.Substring(1);
        }

        return ret;
    }

    public string ReplaceExtension(string src, string newExtension)
    {
        if (newExtension.StartsWith("."))
        {
            newExtension = newExtension.Substring(1);
        }

        newExtension._NotEmptyCheck(nameof(newExtension));

        string dir = PP.GetDirectoryName(src);
        string fn = PP.GetFileName(src);
        string fnWithoutExt = PP.GetFileNameWithoutExtension(fn, false);
        string fn2 = fnWithoutExt + "." + newExtension;

        return PP.Combine(dir, fn2);
    }

    public string Combine(string path1, string path2)
        => Combine(path1, path2, false);
    public string Combine(string path1, string path2, bool path2NeverAbsolutePath = false)
    {
        if (path1._IsNullOrZeroLen() && path2._IsNullOrZeroLen()) throw new CoresEmptyException("path1 == empty && path2 == empty");

        path1 = path1._NonNull();
        path2 = path2._NonNull();

        if (path2NeverAbsolutePath && path2._IsFilled() && IsAbsolutePath(path2))
        {
            throw new ArgumentException($"path2 '{path2}' is an absolute path.");
        }

        if (path1._IsEmpty())
        {
            if (path2NeverAbsolutePath == false)
                return path2;
            else
                return "";
        }

        if (path2._IsEmpty())
            return path1;

        path1 = NormalizeDirectorySeparator(path1);

        path2 = NormalizeDirectorySeparator(path2);

        if (path2._IsFilled() && !(this.IsAbsolutePath(path2, true) && (PossibleDirectorySeparators.Where(x => x == path2[0]).Any()) == false))
        {
            path2 = this.NormalizeRelativePath(path2);
        }

        if (path2.Length >= 1)
        {
            if (path2NeverAbsolutePath == false)
            {
                if (IsAbsolutePath(path2))
                    return path2;

                if (PossibleDirectorySeparators.Where(x => x == path2[0]).Any())
                    return path2;
            }
        }

        path1 = RemoveLastSeparatorChar(path1);

        string sepStr = "" + this.DirectorySeparator;
        if (path1.Length >= 1 && PossibleDirectorySeparators.Where(x => x == path1[path1.Length - 1]).Any())
        {
            sepStr = "";
        }

        return path1 + sepStr + path2;
    }

    public string Combine(params string[] pathList)
    {
        if (pathList._IsNullOrZeroLen())
            throw new CoresEmptyException(nameof(pathList));
        if (pathList.Length == 1) return pathList[0];

        string path1 = pathList[0];

        for (int i = 0; i < pathList.Length - 1; i++)
        {
            string path2 = pathList[i + 1];

            path1 = Combine(path1, path2);
        }

        return path1;
    }

    public string GetFileNameWithoutExtension(string path, bool longExtension = false)
    {
        if (path == null) throw new ArgumentNullException(nameof(path));
        if (path == "") return "";
        path = GetFileName(path);
        int[] dots = path._FindStringIndexes(".", true);
        if (dots.Length == 0)
            return path;

        int i = longExtension ? dots.First() : dots.Last();
        return path.Substring(0, i);
    }

    public string GetPathWithoutExtension(string path, bool longExtension = false)
    {
        string dirName = this.GetDirectoryName(path);
        string fileName = this.GetFileName(path);

        return PP.Combine(dirName, GetFileNameWithoutExtension(fileName, longExtension));
    }

    // 拡張子を取得する (. を含む)
    public string GetExtension(string path, bool longExtension = false, bool emptyWhenNoExtension = false)
    {
        if (path == null) throw new ArgumentNullException(nameof(path));
        if (path == "") return "";
        path = GetFileName(path);
        int[] dots = path._FindStringIndexes(".", true);
        if (dots.Length == 0)
        {
            if (emptyWhenNoExtension)
            {
                return "";
            }
            return path;
        }

        int i = longExtension ? dots.First() : dots.Last();
        return path.Substring(i);
    }

    public void SepareteDirectoryAndFileName(string path, out string dirPath, out string fileName)
    {
        if (path._IsEmpty())
            throw new ArgumentNullException("path");

        path = path._NonNull();

        int i = 0;

        // Skip head separators (e.g. /usr/local/ or \\server\share\)
        for (int j = 0; j < path.Length; j++)
        {
            char c = path[j];

            if (PossibleDirectorySeparators.Where(x => x == c).Any())
            {
                i = j;
            }
            else
            {
                break;
            }
        }

        if (path.StartsWith(@"\\") || path.StartsWith(@"//"))
        {
            // Windows UNC Path
            for (int j = 2; j < path.Length; j++)
            {
                char c = path[j];

                if (PossibleDirectorySeparators.Where(x => x == c).Any())
                {
                    break;
                }
                else
                {
                    i = j;
                }
            }
        }

        int lastMatch = -1;
        while (true)
        {
            i = path.IndexOfAny(this.PossibleDirectorySeparators, i);
            if (i == -1)
            {
                break;
            }
            else
            {
                lastMatch = i;
                i++;
            }
        }

        if (lastMatch == -1)
        {
            if (path.Any(c => PossibleDirectorySeparators.Where(x => x == c).Any()))
            {
                dirPath = RemoveLastSeparatorChar(path);
                fileName = "";
            }
            else
            {
                dirPath = "";
                fileName = path;
            }
        }
        else
        {
            dirPath = RemoveLastSeparatorChar(path.Substring(0, lastMatch + 1));
            fileName = path.Substring(lastMatch + 1);
        }
    }

    public bool IsValidPathChars(string path)
    {
        if (this.Style == FileSystemStyle.Windows)
            return Win32PathInternal.IsValidPathChars(path);
        else
            return UnixPathInternal.IsValidPathChars(path);
    }

    public bool IsValidFileNameChars(string fileName)
    {
        if (this.Style == FileSystemStyle.Windows)
            return Win32PathInternal.IsValidFileNameChars(fileName);
        else
            return UnixPathInternal.IsValidFileNameChars(fileName);
    }

    public char[] GetInvalidFileNameChars()
    {
        if (this.Style == FileSystemStyle.Windows)
            return Win32PathInternal.GetInvalidFileNameChars();
        else
            return UnixPathInternal.GetInvalidFileNameChars();
    }

    public char[] GetInvalidPathChars()
    {
        if (this.Style == FileSystemStyle.Windows)
            return Win32PathInternal.GetInvalidPathChars();
        else
            return UnixPathInternal.GetInvalidPathChars();
    }

    public string MakeSafePathName(string name)
    {
        char[] a = name.ToCharArray();
        StringBuilder sb = new StringBuilder();

        int i;
        for (i = 0; i < a.Length; i++)
        {
            int j;
            bool ok = true;

            for (j = 0; j < InvalidPathChars.Length; j++)
            {
                if (InvalidPathChars[j] == a[i])
                {
                    ok = false;
                    break;
                }
            }

            if (a[i] == '\\' || a[i] == '/')
            {
                ok = true;
                a[i] = this.DirectorySeparator;
            }

            if (i == 1 && a[i] == ':')
            {
                ok = true;
            }

            string s;

            if (ok == false)
            {
                s = "_" + ((int)a[i]).ToString() + "_";
            }
            else
            {
                s = "" + a[i];
            }

            sb.Append(s);
        }

        return sb.ToString();
    }

    public bool IsSafeFileName(string name)
    {
        name = name._NonNullTrim();

        char[] a = name.ToCharArray();

        int i;
        for (i = 0; i < a.Length; i++)
        {
            int j;

            for (j = 0; j < InvalidFileNameChars.Length; j++)
            {
                if (InvalidFileNameChars[j] == a[i])
                {
                    return false;
                }
            }
        }

        if (name._IsEmpty()) return false;

        if (name == "." || name == "..") return false;

        if (name.Length >= 1 && name[0] == '.')
        {
            if (a.Where(c => c != '.').Any() == false) return false;
        }

        return true;
    }

    public string MakeSafeParentDirAndFilenameWithoutExtension(string fullPath)
    {
        string parentDirName = PP.GetFileName(PP.GetDirectoryName(fullPath));
        string fileName = PP.GetFileNameWithoutExtension(fullPath);

        return PP.MakeSafeFileName(parentDirName + "__" + fileName, false, true, true);
    }

    public string MakeSafeFileName(string? name, bool prohititSpace = false, bool additionalCheck = false, bool simpleReplaceToUnderscore = false, bool additionCheck2_SJisAndEuc = false)
    {
        name = name._NonNull();

        char[] a = name.ToCharArray();
        StringBuilder sb = new StringBuilder();

        int i;
        for (i = 0; i < a.Length; i++)
        {
            int j;
            bool ok = true;

            if (prohititSpace)
            {
                if (a[i] == ' ' || a[i] == '　' || a[i] == '\t')
                {
                    a[i] = '_';
                }
            }

            for (j = 0; j < InvalidFileNameChars.Length; j++)
            {
                if (InvalidFileNameChars[j] == a[i])
                {
                    ok = false;
                    break;
                }
            }

            if (additionalCheck || additionCheck2_SJisAndEuc)
            {
                if (Str.IsSafeAndPrintable(a[i]) == false)
                {
                    ok = false;
                }

                if (a[i] == '\'' || a[i] == '\"' || a[i] == '/' || a[i] == '\\')
                {
                    ok = false;
                }
            }

            if (additionCheck2_SJisAndEuc)
            {
                if (Str.IsSuitableCharForEncodings(a[i], Str.EucJpEncoding, Str.ShiftJisEncoding, Str.ISO2022JPEncoding) == false)
                {
                    ok = false;
                }
            }

            string s;

            if (ok == false)
            {
                if (simpleReplaceToUnderscore == false)
                {
                    s = "_" + ((int)a[i]).ToString() + "_";
                }
                else
                {
                    s = "_";
                }
            }
            else
            {
                s = "" + a[i];
            }

            sb.Append(s);
        }

        string ret = sb.ToString();

        string trim = ret.Trim();
        if (trim == "." || trim == "..") ret = "_";

        if (ret.ToCharArray().Where(x => x != '.').Any() == false) ret = "_";

        return ret;
    }

    // 指定された文字列がディレクトリトラバーサルを引き起こす危険なパスかどうか安全寄りに判定
    public static bool IsDangerousStrForDirectoryTraversal(string str)
    {
        try
        {
            str = str._NonNullTrim();

            // デコード前の文字列の最初が %2F (/) または %5C (\) の場合は危険
            if (str.StartsWith("%2F", StringComparison.OrdinalIgnoreCase) || str.StartsWith("%5C", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (str._IsSafeAndPrintable(crlfIsOk: false, htmlTagAreNotGood: true) == false)
            {
                // 危険な可能性のある制御文字は禁止
                return true;
            }

            // 1 回デコード
            string tmp2 = str._DecodeUrlPath()._NonNullTrim();

            if (tmp2._IsSafeAndPrintable(crlfIsOk: false, htmlTagAreNotGood: true) == false)
            {
                // 危険な可能性のある制御文字は禁止
                return true;
            }

            // 1 回デコード後の文字列の最初が %2F (/) または %5C (\) の場合は危険
            if (tmp2.StartsWith("%2F", StringComparison.OrdinalIgnoreCase) || tmp2.StartsWith("%5C", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (tmp2._IsSafeAndPrintable(crlfIsOk: false, htmlTagAreNotGood: true) == false)
            {
                // 危険な可能性のある制御文字は禁止
                return true;
            }

            // 1 回デコードした結果にさらなるエンコードされた危なそうな文字が入っていないかどうかの検査
            if (tmp2._FindStringsMulti(0, StringComparison.OrdinalIgnoreCase, out _, "%2F", "%5C", "%2E", "%25") != -1)
            {
                // 入っていた。危険。
                return true;
            }

            // 2 回デコード
            string tmp3 = tmp2._DecodeUrlPath()._NonNullTrim();

            if (tmp3._IsSafeAndPrintable(crlfIsOk: false, htmlTagAreNotGood: true) == false)
            {
                // 危険な可能性のある制御文字は禁止
                return true;
            }

            // 2 回デコードした結果にさらなるエンコードされた危なそうな文字が入っていないかどうかの検査
            if (tmp3._FindStringsMulti(0, StringComparison.OrdinalIgnoreCase, out _, "%2F", "%5C", "%2E", "%25") != -1)
            {
                // 入っていた。危険。
                return true;
            }

            if (tmp3._IsSafeAndPrintable(crlfIsOk: false, htmlTagAreNotGood: true) == false)
            {
                // 危険な可能性のある制御文字は禁止
                return true;
            }

            // 元の文字のトークン分割
            var tokens = str._Split(StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries, '/', '\\');
            if (tokens.Select(x => x.Trim()).Where(x => x._IsSamei("..") || x.StartsWith("%2F", StringComparison.OrdinalIgnoreCase) || x.StartsWith("%5C", StringComparison.OrdinalIgnoreCase)).Any())
            {
                // トークンの間に .. が入っていた。危険。
                return true;
            }
            if (tokens.Select(x => x.Trim()._DecodeUrlPath()).Where(x => x._IsSamei("..") || x.StartsWith("%2F", StringComparison.OrdinalIgnoreCase) || x.StartsWith("%5C", StringComparison.OrdinalIgnoreCase) || x.StartsWith("/", StringComparison.OrdinalIgnoreCase) || x.StartsWith("\\", StringComparison.OrdinalIgnoreCase)).Any())
            {
                // トークンの間にデコードされたら .. となる文字 または / \ で始まる文字が入っていた。危険。
                return true;
            }

            // 1 回デコード後の文字のトークン分割
            tokens = tmp2._Split(StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries, '/', '\\');
            if (tokens.Select(x => x.Trim()).Where(x => x._IsSamei("..") || x.StartsWith("%2F", StringComparison.OrdinalIgnoreCase) || x.StartsWith("%5C", StringComparison.OrdinalIgnoreCase)).Any())
            {
                // トークンの間に .. が入っていた。危険。
                return true;
            }
            if (tokens.Select(x => x.Trim()._DecodeUrlPath()).Where(x => x._IsSamei("..") || x.StartsWith("%2F", StringComparison.OrdinalIgnoreCase) || x.StartsWith("%5C", StringComparison.OrdinalIgnoreCase) || x.StartsWith("/", StringComparison.OrdinalIgnoreCase) || x.StartsWith("\\", StringComparison.OrdinalIgnoreCase)).Any())
            {
                // トークンの間に 1 回デコードされたら .. となる文字 または / \ で始まる文字が入っていた。危険。
                return true;
            }
            if (tokens.Select(x => x.Trim()._DecodeUrlPath()).Where(x => x._IsSamei("..") || x.StartsWith("%2F", StringComparison.OrdinalIgnoreCase) || x.StartsWith("%5C", StringComparison.OrdinalIgnoreCase) || x.StartsWith("/", StringComparison.OrdinalIgnoreCase) || x.StartsWith("\\", StringComparison.OrdinalIgnoreCase)).Any())
            {
                // トークンの間に 2 回デコードされたら .. となる文字 または / \ で始まる文字が入っていた。危険。
                return true;
            }

            // 2 回デコード後の文字のトークン分割
            tokens = tmp3._Split(StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries, '/', '\\');
            if (tokens.Select(x => x.Trim()).Where(x => x._IsSamei("..") || x.StartsWith("%2F", StringComparison.OrdinalIgnoreCase) || x.StartsWith("%5C", StringComparison.OrdinalIgnoreCase)).Any())
            {
                // トークンの間に .. が入っていた。危険。
                return true;
            }
            if (tokens.Select(x => x.Trim()._DecodeUrlPath()).Where(x => x._IsSamei("..") || x.StartsWith("%2F", StringComparison.OrdinalIgnoreCase) || x.StartsWith("%5C", StringComparison.OrdinalIgnoreCase) || x.StartsWith("/", StringComparison.OrdinalIgnoreCase) || x.StartsWith("\\", StringComparison.OrdinalIgnoreCase)).Any())
            {
                // トークンの間に 1 回デコードされたら .. となる文字が入っていた。危険。
                return true;
            }
            if (tokens.Select(x => x.Trim()._DecodeUrlPath()).Where(x => x._IsSamei("..") || x.StartsWith("%2F", StringComparison.OrdinalIgnoreCase) || x.StartsWith("%5C", StringComparison.OrdinalIgnoreCase) || x.StartsWith("/", StringComparison.OrdinalIgnoreCase) || x.StartsWith("\\", StringComparison.OrdinalIgnoreCase)).Any())
            {
                // トークンの間に 2 回デコードされたら .. となる文字が入っていた。危険。
                return true;
            }

            return false;
        }
        catch
        {
            // 例外が発生したら危険とみなす
            return true;
        }
    }
}

[Flags]
public enum EnumDirectoryFlags
{
    None = 0,
    NoGetPhysicalSize = 1,
    IncludeCurrentDirectory = 2,
    IncludeParentDirectory = 4,
    SkipTooLongFileName = 8,
    AllowRelativePath = 16,
    AllowDirectFilePath = 32,
    IgnoreErrorDuringRecursiveEnum = 64,
}

[Flags]
public enum EasyAccessPathFindMode
{
    NotSupported,
    ExactFullPath,
    MostMatch,
    MostMatchExact,
}

[Flags]
public enum FileSystemMode
{
    ReadOnly = 0,
    Writeable = 1,

    Default = Writeable,
}

public class FileSystemEnumDirectoryRecursiveOptions
{
    public bool DoNotThrowExceptionWhenMaxExceeds { get; set; } = false;
    public int MaxEntities { get; set; } = int.MaxValue;
    public int MaxFiles { get; set; } = int.MaxValue;
    public int MaxDirs { get; set; } = int.MaxValue;
}

public class FileSystemParams
{
    public PathParser PathParser { get; }
    public Copenhagen<EasyAccessPathFindMode> EasyAccessPathFindMode { get; } = new Copenhagen<EasyAccessPathFindMode>(Basic.EasyAccessPathFindMode.NotSupported);
    public FileSystemMode Mode { get; }

    public FileSystemParams(PathParser pathParser, FileSystemMode mode = FileSystemMode.Default)
    {
        this.PathParser = pathParser;
        this.Mode = mode;
    }
}

public abstract partial class FileSystem : AsyncService
{
    public DirectoryWalker DirectoryWalker { get; }
    public PathParser PathParser { get; }
    public PathParser PP => PathParser;
    protected FileSystemParams Params { get; }
    public bool CanWrite => Params.Mode.Bit(FileSystemMode.Writeable);

    readonly CriticalSection LockObj = new CriticalSection<FileSystem>();
    HashSet<FileBase> OpenedHandleList = new HashSet<FileBase>();

    public Singleton<FileSystemObjectPool> ObjectPoolForRead { get; }
    public Singleton<FileSystemObjectPool> ObjectPoolForWrite { get; }

    public LargeFileSystem? LargeFileSystem { get; }

    // Do not change
    public static readonly DateTimeOffset ZeroDateTimeOffsetForFileSystem = new DateTimeOffset(1980, 1, 1, 0, 0, 0, new TimeSpan(0, 0, 0));
    public static readonly DateTime ZeroDateTimeForFileSystem = new DateTime(1980, 1, 1, 0, 0, 0);

    public FileSystem(FileSystemParams param) : base()
    {
        this.Params = param;
        this.PathParser = this.Params.PathParser;
        DirectoryWalker = new DirectoryWalker(this);

        ObjectPoolForRead = new Singleton<FileSystemObjectPool>(() => new FileSystemObjectPool(this, false, CoresConfig.FileSystemSettings.PooledHandleLifetime, CoresConfig.FileSystemSettings.MaxPooledHandleCount));
        ObjectPoolForWrite = new Singleton<FileSystemObjectPool>(() => new FileSystemObjectPool(this, true, CoresConfig.FileSystemSettings.PooledHandleLifetime, CoresConfig.FileSystemSettings.MaxPooledHandleCount));

        try
        {
            InitEasyFileAccessSingleton();
        }
        catch { }
    }

    public Task<string> GetAbsolutePathFromRelativePathAsync(string path, FileFlags flags = FileFlags.None, CancellationToken cancel = default)
        => this.GetAbsolutePathFromRelativePathIfSupportedImplAsync(path, flags, cancel);

    public string GetAbsolutePathFromRelativePath(string path, FileFlags flags = FileFlags.None, CancellationToken cancel = default)
        => this.GetAbsolutePathFromRelativePathAsync(path, flags, cancel)._GetResult();

    protected virtual Task<string> GetAbsolutePathFromRelativePathIfSupportedImplAsync(string path, FileFlags flags, CancellationToken cancel)
    {
        return path._TR();
    }

    internal DisposableFileProvider _CreateFileProviderForWatchInternal(EnsureInternal yes, string root, bool noDispose = false)
    {
        IFileProvider p = this.CreateFileProviderForWatchImpl(root);

        return new DisposableFileProvider(p, noDispose);
    }

    protected void CheckWriteable(string path)
    {
        if (this.CanWrite == false)
            throw new FileException(path, "This file system is read-only mode.");
    }

    public async Task<RandomAccessHandle> GetRandomAccessHandleAsync(string fileName, bool writeMode, FileFlags flags = FileFlags.None, CancellationToken cancel = default)
    {
        CheckNotCanceled();

        if (writeMode) CheckWriteable(fileName);

        FileSystemObjectPool pool = writeMode ? ObjectPoolForWrite : ObjectPoolForRead;

        RefCounterObjectHandle<FileBase> refFileBase = await pool.OpenOrGetAsync(fileName, flags, cancel);

        return new RandomAccessHandle(refFileBase);
    }
    public RandomAccessHandle GetRandomAccessHandle(string fileName, bool writeMode, FileFlags flags = FileFlags.None, CancellationToken cancel = default)
        => GetRandomAccessHandleAsync(fileName, writeMode, flags, cancel)._GetResult();

    protected override Task CancelImplAsync(Exception? ex)
        => base.CancelImplAsync(ex);

    protected override async Task CleanupImplAsync(Exception? ex)
    {
        try
        {
            FileBase[] fileHandles;

            lock (LockObj)
            {
                fileHandles = OpenedHandleList.ToArray();
                OpenedHandleList.Clear();
            }

            foreach (var fileHandle in fileHandles)
            {
                await fileHandle.CloseAsync();
            }

            await ObjectPoolForRead._DisposeSafeAsync();
            await ObjectPoolForWrite._DisposeSafeAsync();
        }
        finally
        {
            await base.CleanupImplAsync(ex);
        }
    }

    protected abstract Task<string> NormalizePathImplAsync(string path, CancellationToken cancel = default);

    protected abstract Task<FileObject> CreateFileImplAsync(FileParameters option, CancellationToken cancel = default);
    protected abstract Task DeleteFileImplAsync(string path, FileFlags flags = FileFlags.None, CancellationToken cancel = default);

    protected abstract Task CreateDirectoryImplAsync(string directoryPath, FileFlags flags = FileFlags.None, CancellationToken cancel = default);
    protected abstract Task DeleteDirectoryImplAsync(string directoryPath, bool recursive, CancellationToken cancel = default);
    protected abstract Task<FileSystemEntity[]> EnumDirectoryImplAsync(string directoryPath, EnumDirectoryFlags flags, string wildcard, CancellationToken cancel = default);

    protected abstract Task<FileMetadata> GetFileMetadataImplAsync(string path, FileMetadataGetFlags flags = FileMetadataGetFlags.DefaultAll, CancellationToken cancel = default);
    protected abstract Task SetFileMetadataImplAsync(string path, FileMetadata metadata, CancellationToken cancel = default);

    protected abstract Task<FileMetadata> GetDirectoryMetadataImplAsync(string path, FileMetadataGetFlags flags = FileMetadataGetFlags.DefaultAll, CancellationToken cancel = default);
    protected abstract Task SetDirectoryMetadataImplAsync(string path, FileMetadata metadata, CancellationToken cancel = default);

    protected abstract Task MoveFileImplAsync(string srcPath, string destPath, CancellationToken cancel = default);
    protected abstract Task MoveDirectoryImplAsync(string srcPath, string destPath, CancellationToken cancel = default);

    protected abstract Task<bool> IsFileExistsImplAsync(string path, CancellationToken cancel = default);
    protected abstract Task<bool> IsDirectoryExistsImplAsync(string path, CancellationToken cancel = default);

    protected abstract IFileProvider CreateFileProviderForWatchImpl(string root);

    protected IFileProvider CreateDefaultNullFileProvider() => new NullFileProvider();

    readonly ConcurrentDictionary<string, string> CaseCorrectionCache = new ConcurrentDictionary<string, string>(StrComparer.IgnoreCaseComparer);

    public void FlushNormalizedCaseCorrectionCache()
    {
        CaseCorrectionCache.Clear();
    }

    async Task<string> NormalizePathWithCaseCorrectionInternalAsync(string path, bool forDirectory, CancellationToken cancel = default)
    {
        path = PathParser.NormalizeDirectorySeparatorAndCheckIfAbsolutePath(path);

        if (PathParser.Style == FileSystemStyle.Windows) return path;

        string[] elements = PathParser.SplitAbsolutePathToElementsUnixStyle(path);

        string cacheKey = PathParser.Combine(elements);

        if (CaseCorrectionCache.TryGetValue(cacheKey, out string? cachedValue))
        {
            return cachedValue;
        }

        string currentFullPath = "/";

        for (int i = 0; i < elements.Length; i++)
        {
            string element = elements[i];

            bool isThisElementDirectory = forDirectory || (i != (elements.Length - 1));

            string originalFullPath = PathParser.Combine(currentFullPath, element);
            string? element2 = null;

            try
            {
                if ((forDirectory == false && await this.IsFileExistsImplAsync(originalFullPath, cancel)) ||
                    (forDirectory == true && await this.IsDirectoryExistsImplAsync(originalFullPath, cancel)))
                {
                    element2 = element;
                }
            }
            catch { }

            if (element2._IsEmpty())
            {
                try
                {
                    FileSystemEntity[] dirItems = await this.EnumDirectoryImplAsync(currentFullPath, EnumDirectoryFlags.NoGetPhysicalSize, "", cancel);
                    element2 = dirItems.Where(x => x.IsDirectory == isThisElementDirectory && x.IsCurrentOrParentDirectory == false).Select(x => x.Name).Where(x => x._IsSamei(element)).FirstOrDefault();
                }
                catch
                {
                }
            }

            if (element2._IsEmpty())
            {
                element2 = element;
            }

            currentFullPath = PathParser.Combine(currentFullPath, element2);
        }

        CaseCorrectionCache[cacheKey] = currentFullPath;

        return currentFullPath;
    }

    public async Task<string> NormalizePathAsync(string path, NormalizePathOption options = NormalizePathOption.None, CancellationToken cancel = default, bool normalizeRelativePathIfSupported = false)
    {
        path = path._NonNull();

        await using (CreatePerTaskCancellationToken(out CancellationToken opCancel, cancel))
        {
            using (EnterCriticalCounter())
            {
                cancel.ThrowIfCancellationRequested();

                if (normalizeRelativePathIfSupported)
                {
                    path = await this.GetAbsolutePathFromRelativePathIfSupportedImplAsync(path, FileFlags.None, cancel);
                }

                if (options == NormalizePathOption.NormalizeCaseDirectory)
                    path = await NormalizePathWithCaseCorrectionInternalAsync(path, true, cancel);
                else if (options == NormalizePathOption.NormalizeCaseFileName)
                    path = await NormalizePathWithCaseCorrectionInternalAsync(path, false, cancel);

                string ret = await NormalizePathImplAsync(path, cancel);

                return ret;
            }
        }
    }
    public string NormalizePath(string path, NormalizePathOption options = NormalizePathOption.None, CancellationToken cancel = default, bool normalizeRelativePathIfSupported = false)
        => NormalizePathAsync(path, options, cancel, normalizeRelativePathIfSupported)._GetResult();

    public async Task<FileObject> CreateFileAsync(FileParameters option, CancellationToken cancel = default)
    {
        if (option.Mode == FileMode.Append || option.Mode == FileMode.Create || option.Mode == FileMode.CreateNew || option.Mode == FileMode.OpenOrCreate || option.Mode == FileMode.Truncate)
            CheckWriteable(option.Path);

        await using (CreatePerTaskCancellationToken(out CancellationToken opCancel, cancel))
        {
            using (EnterCriticalCounter())
            {
                await option.NormalizePathAsync(this, opCancel);

                if (option.Mode == FileMode.Append || option.Mode == FileMode.Create || option.Mode == FileMode.CreateNew ||
                    option.Mode == FileMode.OpenOrCreate || option.Mode == FileMode.Truncate)
                {
                    if (option.Access.Bit(FileAccess.Write) == false)
                    {
                        throw new ArgumentException("The Access member must contain the FileAccess.Write bit when opening a file with create mode.");
                    }

                    if (option.Flags.Bit(FileFlags.AutoCreateDirectory))
                    {
                        string dirName = this.PathParser.GetDirectoryName(option.Path);
                        if (dirName._IsFilled())
                        {
                            await CreateDirectoryImplAsync(dirName, option.Flags, opCancel);
                        }
                    }
                }

                FileObject f = await CreateFileImplAsync(option, opCancel);

                lock (LockObj)
                {
                    OpenedHandleList.Add(f);
                }

                f.EventListeners.RegisterCallback(FileEventListenerCallback);

                return f;
            }
        }
    }

    void FileEventListenerCallback(FileBase obj, FileObjectEventType eventType, object? userState, object? eventState)
    {
        switch (eventType)
        {
            case FileObjectEventType.Closed:
                lock (LockObj)
                {
                    OpenedHandleList.Remove((FileObject)obj);
                }
                break;
        }
    }

    public FileObject CreateFile(FileParameters option, CancellationToken cancel = default)
        => CreateFileAsync(option, cancel)._GetResult();

    public Task<FileObject> CreateAsync(string path, bool noShare = false, FileFlags flags = FileFlags.None, bool doNotOverwrite = false, CancellationToken cancel = default)
        => CreateFileAsync(new FileParameters(path, doNotOverwrite ? FileMode.CreateNew : FileMode.Create, FileAccess.ReadWrite, noShare ? FileShare.None : FileShare.Read, flags), cancel);

    public FileObject Create(string path, bool noShare = false, FileFlags flags = FileFlags.None, bool doNotOverwrite = false, CancellationToken cancel = default)
        => CreateAsync(path, noShare, flags, doNotOverwrite, cancel)._GetResult();

    public Task<FileObject> OpenAsync(string path, bool writeMode = false, bool noShare = false, bool readLock = false, FileFlags flags = FileFlags.None, CancellationToken cancel = default)
        => CreateFileAsync(new FileParameters(path, FileMode.Open, (writeMode ? FileAccess.ReadWrite : FileAccess.Read),
            (noShare ? FileShare.None : ((writeMode || readLock) ? FileShare.Read : (FileShare.ReadWrite | FileShare.Delete))), flags), cancel);

    public FileObject Open(string path, bool writeMode = false, bool noShare = false, bool readLock = false, FileFlags flags = FileFlags.None, CancellationToken cancel = default)
        => OpenAsync(path, writeMode, noShare, readLock, flags, cancel)._GetResult();

    public Task<FileObject> OpenOrCreateAsync(string path, bool noShare = false, FileFlags flags = FileFlags.None, CancellationToken cancel = default)
        => CreateFileAsync(new FileParameters(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, noShare ? FileShare.None : FileShare.Read, flags), cancel);

    public FileObject OpenOrCreate(string path, bool noShare = false, FileFlags flags = FileFlags.None, CancellationToken cancel = default)
        => OpenOrCreateAsync(path, noShare, flags, cancel)._GetResult();

    public Task<FileObject> OpenOrCreateAppendAsync(string path, bool noShare = false, FileFlags flags = FileFlags.None, CancellationToken cancel = default)
        => CreateFileAsync(new FileParameters(path, FileMode.Append, FileAccess.Write, noShare ? FileShare.None : FileShare.Read, flags), cancel);

    public FileObject OpenOrCreateAppend(string path, bool noShare = false, FileFlags flags = FileFlags.None, CancellationToken cancel = default)
        => OpenOrCreateAppendAsync(path, noShare, flags, cancel)._GetResult();

    public async Task CreateDirectoryAsync(string path, FileFlags flags = FileFlags.None, CancellationToken cancel = default)
    {
        CheckWriteable(path);

        await using (CreatePerTaskCancellationToken(out CancellationToken opCancel, cancel))
        {
            using (EnterCriticalCounter())
            {
                path = await NormalizePathAsync(path, cancel: opCancel);

                opCancel.ThrowIfCancellationRequested();

                await CreateDirectoryImplAsync(path, flags, opCancel);
            }
        }
    }

    public void CreateDirectory(string path, FileFlags flags = FileFlags.None, CancellationToken cancel = default)
        => CreateDirectoryAsync(path, flags, cancel)._GetResult();

    public async Task DeleteDirectoryAsync(string path, bool recursive = false, CancellationToken cancel = default, bool forcefulUseInternalRecursiveDelete = false, bool normalizeRelativePathIfSupported = false)
    {
        CheckWriteable(path);

        await using (CreatePerTaskCancellationToken(out CancellationToken opCancel, cancel))
        {
            using (EnterCriticalCounter())
            {
                path = await NormalizePathAsync(path, cancel: opCancel, normalizeRelativePathIfSupported: normalizeRelativePathIfSupported);

                opCancel.ThrowIfCancellationRequested();

                if (recursive && forcefulUseInternalRecursiveDelete)
                {
                    await this.DeleteDirectoryRecursiveInternalAsync(path, opCancel);
                }
                else
                {
                    await DeleteDirectoryImplAsync(path, recursive, opCancel);
                }
            }
        }
    }
    public void DeleteDirectory(string path, bool recursive = false, CancellationToken cancel = default, bool forcefulUseInternalRecursiveDelete = false, bool normalizeRelativePathIfSupported = false)
        => DeleteDirectoryAsync(path, recursive, cancel, forcefulUseInternalRecursiveDelete, normalizeRelativePathIfSupported)._GetResult();

    async Task<FileSystemEntity[]> EnumDirectoryInternalAsync(string directoryPath, EnumDirectoryFlags flags, string wildcard, CancellationToken opCancel)
    {
        using (EnterCriticalCounter())
        {
            opCancel.ThrowIfCancellationRequested();

            FileSystemEntity[] list = await EnumDirectoryImplAsync(directoryPath, (flags | EnumDirectoryFlags.IncludeCurrentDirectory).BitRemove(EnumDirectoryFlags.IncludeParentDirectory), wildcard, opCancel);

            int distinctCount = list.Select(x => x.Name).Distinct(this.PathParser.PathStringComparer).Count();

            if (distinctCount != list.Count())
            {
                throw new ApplicationException($"There are duplicated entities returned by EnumDirectoryImplAsync(). ({distinctCount} != {list.Count()})");
            }

            if (list.Where(x => x.IsParentDirectory).Any())
            {
                throw new ApplicationException("An entry returned by EnumDirectoryImplAsync() is IsParentDirectory.");
            }

            if (list.First().IsCurrentDirectory == false || list.First().IsDirectory == false)
            {
                throw new ApplicationException("The first entry returned by EnumDirectoryImplAsync() is not a current directory.");
            }

            IEnumerable<FileSystemEntity> ret = list.Skip(1).Where(x => GetSpecialFileNameKind(x.Name) == SpecialFileNameKind.Normal).Where(x => x.Name.Length < 256 || flags.Bit(EnumDirectoryFlags.SkipTooLongFileName));

            if (flags.Bit(EnumDirectoryFlags.IncludeParentDirectory))
            {
                // Parent directory
                string parentDirectory = await this.NormalizePathImplAsync(directoryPath, opCancel);

                if (this.PathParser.IsRootDirectory(parentDirectory) == false)
                {
                    parentDirectory = this.PathParser.GetDirectoryName(parentDirectory);

                    FileMetadata meta = await this.GetDirectoryMetadataImplAsync(parentDirectory, cancel: opCancel);
                    ret = ret.Prepend(meta.ToFileSystemEntity(this.PathParser, parentDirectory, ".."));
                }
            }

            if (flags.Bit(EnumDirectoryFlags.IncludeCurrentDirectory))
            {
                // Current directory
                ret = ret.Prepend(list[0]);
            }

            return ret.ToArray();
        }
    }

    public class EnumDirCtx
    {
        public int CurrentNumDirs = 0;
        public int CurrentNumFiles = 0;
    }

    async Task<bool> EnumDirectoryRecursiveInternalAsync(EnumDirCtx ctx, int depth, List<FileSystemEntity> currentList, string directoryPath, string wildcard, bool recursive, EnumDirectoryFlags flags, FileSystemEnumDirectoryRecursiveOptions options, CancellationToken opCancel)
    {
        opCancel.ThrowIfCancellationRequested();

        FileSystemEntity[] entityList;

        try
        {
            entityList = await EnumDirectoryInternalAsync(directoryPath, flags, recursive ? "" : wildcard, opCancel); // ファイルシステムネイティブのワイルドカード指定パラメータは、再帰モードでは使用しない
        }
        catch (Exception ex) when (flags.Bit(EnumDirectoryFlags.IgnoreErrorDuringRecursiveEnum))
        {
            ex._Error();
            return true;
        }

        foreach (FileSystemEntity entity in entityList)
        {
            if (depth == 0 || entity.IsCurrentOrParentDirectory == false)
            {
                if (this.PathParser.WildcardMatch(entity.Name, wildcard))
                {
                    if (entity.IsFile)
                    {
                        if (ctx.CurrentNumFiles >= options.MaxFiles)
                        {
                            if (options.DoNotThrowExceptionWhenMaxExceeds)
                            {
                                continue;
                            }
                            throw new CoresLibException($"ctx.CurrentNumFiles ({ctx.CurrentNumFiles}) >= options.MaxFiles ({options.MaxFiles})");
                        }
                        if ((ctx.CurrentNumDirs + ctx.CurrentNumFiles) >= options.MaxEntities)
                        {
                            if (options.DoNotThrowExceptionWhenMaxExceeds)
                            {
                                continue;
                            }
                            throw new CoresLibException($"(ctx.CurrentNumDirs ({ctx.CurrentNumDirs}) + ctx.CurrentNumFiles ({ctx.CurrentNumFiles})) >= options.MaxEntities ({options.MaxEntities})");
                        }
                        ctx.CurrentNumFiles++;
                    }
                    else
                    {
                        if (ctx.CurrentNumDirs >= options.MaxDirs)
                        {
                            if (options.DoNotThrowExceptionWhenMaxExceeds)
                            {
                                continue;
                            }
                            throw new CoresLibException($"ctx.CurrentNumDirs ({ctx.CurrentNumDirs}) >= options.MaxDirs ({options.MaxDirs})");
                        }
                        if ((ctx.CurrentNumDirs + ctx.CurrentNumFiles) >= options.MaxEntities)
                        {
                            if (options.DoNotThrowExceptionWhenMaxExceeds)
                            {
                                continue;
                            }
                            throw new CoresLibException($"(ctx.CurrentNumDirs ({ctx.CurrentNumDirs}) + ctx.CurrentNumFiles ({ctx.CurrentNumFiles})) >= options.MaxEntities ({options.MaxEntities})");
                        }
                        ctx.CurrentNumDirs++;
                    }

                    currentList.Add(entity);
                }
            }

            if (recursive)
            {
                if (entity.IsDirectory && entity.IsCurrentOrParentDirectory == false)
                {
                    if (await EnumDirectoryRecursiveInternalAsync(ctx, depth + 1, currentList, entity.FullPath, wildcard, true, flags, options, opCancel) == false)
                    {
                        return false;
                    }
                }
            }
        }

        return true;
    }

    public Task<FileSystemEntity[]> EnumDirectoryAsync(string directoryPath, bool recursive = false, EnumDirectoryFlags flags = EnumDirectoryFlags.None, string? wildcard = null, CancellationToken cancel = default)
        => EnumDirectoryAsync(directoryPath, null, recursive, flags, wildcard, cancel);
    public async Task<FileSystemEntity[]> EnumDirectoryAsync(string directoryPath, FileSystemEnumDirectoryRecursiveOptions? options, bool recursive = false, EnumDirectoryFlags flags = EnumDirectoryFlags.None, string? wildcard = null, CancellationToken cancel = default)
    {
        CheckNotCanceled();

        options ??= new();

        wildcard = wildcard._NonNullTrim();

        if (flags.Bit(EnumDirectoryFlags.AllowDirectFilePath))
        {
            if (flags.Bit(EnumDirectoryFlags.AllowRelativePath))
            {
                throw new ArgumentException(nameof(flags));
            }

            if (wildcard._IsFilled() && wildcard != "*" && wildcard != "*.*")
            {
                throw new ArgumentException(nameof(wildcard));
            }

            if (await this.IsFileExistsAsync(directoryPath))
            {
                // ファイルパス直接指定を許容する場合で、そのファイルが存在する場合
                string dir = Lfs.PathParser.GetDirectoryName(directoryPath);
                string fn = Lfs.PathParser.GetFileName(directoryPath);

                var ret = await EnumDirectoryAsync(dir, options, false, flags.BitRemove(EnumDirectoryFlags.AllowDirectFilePath), fn, cancel);

                if (ret.Where(x => x.IsFile).Count() != 1)
                {
                    throw new CoresLibException($"ret has {ret.Where(x => x.IsFile).Count()} files.");
                }

                return ret;
            }
        }

        await using (CreatePerTaskCancellationToken(out CancellationToken opCancel, cancel))
        {
            opCancel.ThrowIfCancellationRequested();

            directoryPath = await NormalizePathAsync(directoryPath, cancel: opCancel, normalizeRelativePathIfSupported: flags.Bit(EnumDirectoryFlags.AllowRelativePath));

            List<FileSystemEntity> currentList = new List<FileSystemEntity>();

            EnumDirCtx ctx = new();

            if (await EnumDirectoryRecursiveInternalAsync(ctx, 0, currentList, directoryPath, wildcard, recursive, flags, options, opCancel) == false)
            {
                throw new OperationCanceledException();
            }

            return currentList.ToArray();
        }
    }

    public FileSystemEntity[] EnumDirectory(string directoryPath, bool recursive = false, EnumDirectoryFlags flags = EnumDirectoryFlags.None, string? wildcard = null, CancellationToken cancel = default)
        => EnumDirectory(directoryPath, null, recursive, flags, wildcard, cancel);

    public FileSystemEntity[] EnumDirectory(string directoryPath, FileSystemEnumDirectoryRecursiveOptions? options, bool recursive = false, EnumDirectoryFlags flags = EnumDirectoryFlags.None, string? wildcard = null, CancellationToken cancel = default)
        => EnumDirectoryAsync(directoryPath, options, recursive, flags, wildcard, cancel)._GetResult();

    public async Task<FileMetadata> GetFileMetadataAsync(string path, FileMetadataGetFlags flags = FileMetadataGetFlags.DefaultAll, CancellationToken cancel = default)
    {
        await using (CreatePerTaskCancellationToken(out CancellationToken opCancel, cancel))
        {
            using (EnterCriticalCounter())
            {
                cancel.ThrowIfCancellationRequested();

                path = await NormalizePathImplAsync(path, opCancel);

                return await GetFileMetadataImplAsync(path, flags, cancel);
            }
        }
    }
    public FileMetadata GetFileMetadata(string path, FileMetadataGetFlags flags = FileMetadataGetFlags.DefaultAll, CancellationToken cancel = default)
        => GetFileMetadataAsync(path, flags, cancel)._GetResult();

    public async Task<FileMetadata> GetDirectoryMetadataAsync(string path, FileMetadataGetFlags flags = FileMetadataGetFlags.DefaultAll, CancellationToken cancel = default)
    {
        await using (CreatePerTaskCancellationToken(out CancellationToken opCancel, cancel))
        {
            using (EnterCriticalCounter())
            {
                cancel.ThrowIfCancellationRequested();

                path = await NormalizePathImplAsync(path, opCancel);

                return await GetDirectoryMetadataImplAsync(path, flags, cancel);
            }
        }
    }
    public FileMetadata GetDirectoryMetadata(string path, FileMetadataGetFlags flags = FileMetadataGetFlags.DefaultAll, CancellationToken cancel = default)
        => GetDirectoryMetadataAsync(path, flags, cancel)._GetResult();

    public async Task<bool> IsFileExistsAsync(string path, CancellationToken cancel = default)
    {
        CheckNotCanceled();

        try
        {

            return await IsFileExistsImplAsync(path, cancel);
        }
        catch { }

        return false;
    }
    public bool IsFileExists(string path, CancellationToken cancel = default)
        => IsFileExistsAsync(path, cancel)._GetResult();

    public async Task<bool> IsDirectoryExistsAsync(string path, CancellationToken cancel = default)
    {
        CheckNotCanceled();

        try
        {
            return await IsDirectoryExistsImplAsync(path, cancel);
        }
        catch { }

        return false;
    }
    public bool IsDirectoryExists(string path, CancellationToken cancel = default, bool normalizeRelativePathIfSupported = false)
        => IsDirectoryExistsAsync(path, cancel)._GetResult();

    public async Task SetFileMetadataAsync(string path, FileMetadata metadata, CancellationToken cancel = default)
    {
        CheckWriteable(path);

        await using (CreatePerTaskCancellationToken(out CancellationToken opCancel, cancel))
        {
            using (EnterCriticalCounter())
            {
                cancel.ThrowIfCancellationRequested();

                path = await NormalizePathImplAsync(path, opCancel);

                await SetFileMetadataImplAsync(path, metadata, opCancel);
            }
        }
    }
    public void SetFileMetadata(string path, FileMetadata metadata, CancellationToken cancel = default, bool normalizeRelativePathIfSupported = false)
        => SetFileMetadataAsync(path, metadata, cancel)._GetResult();

    public async Task SetDirectoryMetadataAsync(string path, FileMetadata metadata, CancellationToken cancel = default)
    {
        CheckWriteable(path);

        await using (CreatePerTaskCancellationToken(out CancellationToken opCancel, cancel))
        {
            using (EnterCriticalCounter())
            {
                cancel.ThrowIfCancellationRequested();

                path = await NormalizePathImplAsync(path, opCancel);

                await SetDirectoryMetadataImplAsync(path, metadata, opCancel);
            }
        }
    }
    public void SetDirectoryMetadata(string path, FileMetadata metadata, CancellationToken cancel = default, bool normalizeRelativePathIfSupported = false)
        => SetDirectoryMetadataAsync(path, metadata, cancel)._GetResult();

    public async Task DeleteFileAsync(string path, FileFlags flags = FileFlags.None, CancellationToken cancel = default)
    {
        CheckWriteable(path);

        await using (CreatePerTaskCancellationToken(out CancellationToken opCancel, cancel))
        {
            using (EnterCriticalCounter())
            {
                cancel.ThrowIfCancellationRequested();

                path = await NormalizePathImplAsync(path, opCancel);

                await DeleteFileImplAsync(path, flags, opCancel);
            }
        }
    }

    public void DeleteFile(string path, FileFlags flags = FileFlags.None, CancellationToken cancel = default)
        => DeleteFileAsync(path, flags, cancel)._GetResult();

    public async Task EnsureCreateDirectoryForFileAsync(string filePath, FileFlags flags = FileFlags.None, CancellationToken cancel = default)
    {
        try
        {
            await Lfs.CreateDirectoryAsync(PP.GetDirectoryName(filePath), flags, cancel: cancel);
        }
        catch { }
    }

    public async Task DeleteFileIfExistsAsync(string path, FileFlags flags = FileFlags.None, bool raiseException = false, CancellationToken cancel = default)
    {
        try
        {
            if (await Lfs.IsFileExistsAsync(path, cancel) == false)
            {
                return;
            }

            await this.DeleteFileAsync(path, flags, cancel);
        }
        catch (Exception ex)
        {
            if (raiseException)
            {
                throw;
            }
            else
            {
                ex._Error();
            }
        }
    }

    public async Task<string> MoveFileAutoIncNameAsync(string srcPath, string destPath, CancellationToken cancel = default)
    {
        await Lfs.CreateDirectoryAsync(PP.GetDirectoryName(destPath));

        string ret = await RetryHelper.RunAsync(async () =>
        {
            for (int i = 0; i < 10000; i++)
            {
                string tmp1 = destPath;
                if (i != 0)
                {
                    string dir = PathParser.GetDirectoryName(destPath);
                    string fn = PathParser.GetFileNameWithoutExtension(destPath, false);
                    string ext = PathParser.GetExtension(destPath, false, true);

                    tmp1 = PathParser.Combine(dir, $"{fn} ({i:D4}){ext}");
                }

                if (await Lfs.IsFileExistsAsync(tmp1, cancel) == false)
                {
                    await MoveFileAsync(srcPath, tmp1, cancel);
                    return destPath;
                }
            }

            throw new CoresException($"MoveFileAutoIncNameAsync: Destination path '{destPath}' alreay exists and failed to make incremented filename");
        }, 1, 3, cancel);

        return ret;
    }

    public async Task MoveFileAsync(string srcPath, string destPath, CancellationToken cancel = default)
    {
        CheckWriteable(srcPath);

        await using (CreatePerTaskCancellationToken(out CancellationToken opCancel, cancel))
        {
            using (EnterCriticalCounter())
            {
                cancel.ThrowIfCancellationRequested();

                srcPath = await NormalizePathImplAsync(srcPath, opCancel);
                destPath = await NormalizePathImplAsync(destPath, opCancel);

                await MoveFileImplAsync(srcPath, destPath, cancel);
            }
        }
    }
    public void MoveFile(string srcPath, string destPath, CancellationToken cancel = default)
        => MoveFileAsync(srcPath, destPath, cancel)._GetResult();

    public async Task MoveDirectoryAsync(string srcPath, string destPath, CancellationToken cancel = default)
    {
        CheckWriteable(srcPath);

        await using (CreatePerTaskCancellationToken(out CancellationToken opCancel, cancel))
        {
            using (EnterCriticalCounter())
            {
                cancel.ThrowIfCancellationRequested();

                srcPath = await NormalizePathImplAsync(srcPath, opCancel);
                destPath = await NormalizePathImplAsync(destPath, opCancel);

                await MoveDirectoryImplAsync(srcPath, destPath, cancel);
            }
        }
    }
    public void MoveDirectory(string srcPath, string destPath, CancellationToken cancel = default)
        => MoveDirectoryAsync(srcPath, destPath, cancel)._GetResult();

    public static SpecialFileNameKind GetSpecialFileNameKind(string fileName)
    {
        SpecialFileNameKind ret = SpecialFileNameKind.Normal;

        if (fileName == ".") ret |= SpecialFileNameKind.CurrentDirectory;
        if (fileName == "..") ret |= SpecialFileNameKind.ParentDirectory;

        return ret;
    }

    public FileSystemEventWatcher CreateFileSystemEventWatcher(string root, string filter = "**/*", object? state = null, bool enforcePolling = false, int? pollingInterval = null)
    {
        DisposableFileProvider p = this._CreateFileProviderForWatchInternal(EnsureInternal.Yes, root);

        try
        {
            return new FileSystemEventWatcher(p, filter, state, enforcePolling, pollingInterval);
        }
        catch
        {
            p._DisposeSafe();
            throw;
        }
    }

    public FileSystemBasedProvider CreateFileProvider(string rootDirectory)
    {
        return new FileSystemBasedProvider(EnsureInternal.Yes, this, rootDirectory);
    }
}

public class DisposableFileProvider : IFileProvider, IDisposable, IAsyncDisposable
{
    readonly IFileProvider Provider;
    readonly bool NoDispose;
    readonly IHolder LeakHolder;

    public DisposableFileProvider(IFileProvider baseInstance, bool noDispose = false)
    {
        this.Provider = baseInstance;
        this.NoDispose = noDispose;
        this.LeakHolder = LeakChecker.Enter();
    }
    public IDirectoryContents GetDirectoryContents(string subpath) => Provider.GetDirectoryContents(subpath);
    public IFileInfo GetFileInfo(string subpath) => Provider.GetFileInfo(subpath);
    public IChangeToken Watch(string filter) => Provider.Watch(filter);

    public void Dispose() { this.Dispose(true); GC.SuppressFinalize(this); }
    Once DisposeFlag;
    public virtual async ValueTask DisposeAsync()
    {
        if (DisposeFlag.IsFirstCall() == false) return;
        await DisposeInternalAsync();
    }
    protected virtual void Dispose(bool disposing)
    {
        if (!disposing || DisposeFlag.IsFirstCall() == false) return;
        DisposeInternalAsync()._GetResult();
    }
    async Task DisposeInternalAsync()
    {
        if (NoDispose == false)
        {
            if (Provider is IAsyncDisposable target2)
            {
                await target2._DisposeSafeAsync();
            }
            else if (Provider is IDisposable target)
            {
                target._DisposeSafe();
            }
        }

        LeakHolder._DisposeSafe();
    }

}

public class FileSystemEventWatcher : AsyncService
{
    readonly DisposableFileProvider Provider;
    public string Filter { get; }
    public object? State { get; }
    public bool EnforcePolling { get; }
    public bool IsPollingMode { get; private set; }
    public int PollingInterval { get; }

    IChangeToken ChangeToken = null!;
    IDisposable? CallbackDisposable = null;

    Task? CurrentPollingTask = null;

    readonly CriticalSection LockObj = new CriticalSection<FileSystemEventWatcher>();

    public FastEventListenerList<FileSystemEventWatcher, NonsenseEventType> EventListeners { get; } = new FastEventListenerList<FileSystemEventWatcher, NonsenseEventType>();

    public AsyncPulse AsyncPulse { get; } = new();

    public FileSystemEventWatcher(DisposableFileProvider provider, string filter = "**/*", object? state = null, bool enforcePolling = false, int? pollingInterval = null)
    {
        this.Provider = provider;
        this.Filter = filter;
        this.State = state;
        this.EnforcePolling = enforcePolling;
        this.PollingInterval = pollingInterval ?? CoresConfig.FileSystemEventWatcherSettings.DefaultPollingInterval;

        this.PollingInterval = Math.Max(this.PollingInterval, 100);

        try
        {
            ChangeToken = this.Provider.Watch(this.Filter);

            if (EnforcePolling || ChangeToken.ActiveChangeCallbacks == false)
            {
                IsPollingMode = true;

                CurrentPollingTask = PollDelayAsync();
            }
            else
            {
                IsPollingMode = false;

                CallbackDisposable = ChangeToken.RegisterChangeCallback(Poll, null);
            }
        }
        catch
        {
            this._DisposeSafe();
            throw;
        }
    }

    void Poll(object? internalState)
    {
        lock (LockObj)
        {
            if (ChangeToken.HasChanged)
            {
                CallbackDisposable._DisposeSafe();

                try
                {
                    ChangeToken = this.Provider.Watch(this.Filter);

                    try
                    {
                        this.EventListeners.FireSoftly(this, NonsenseEventType.Nonsense);
                    }
                    catch { }

                    try
                    {
                        this.AsyncPulse.FirePulse(true);
                    }
                    catch { }

                    if (EnforcePolling || ChangeToken.ActiveChangeCallbacks == false)
                    {
                        IsPollingMode = true;

                        CurrentPollingTask = PollDelayAsync();
                    }
                    else
                    {
                        IsPollingMode = false;

                        CallbackDisposable = ChangeToken.RegisterChangeCallback(Poll, null);
                    }
                }
                catch (Exception ex)
                {
                    ex._Debug();
                }
            }
            else
            {
                if (EnforcePolling)
                {
                    IsPollingMode = true;

                    CurrentPollingTask = PollDelayAsync();
                }
            }
        }
    }

    async Task PollDelayAsync()
    {
        await Task.Yield();

        this.GrandCancel.ThrowIfCancellationRequested();

        await this.GrandCancel._WaitUntilCanceledAsync(this.PollingInterval);

        this.GrandCancel.ThrowIfCancellationRequested();

        Poll(null);
    }

    protected override void DisposeImpl(Exception? ex)
    {
        try
        {
            lock (LockObj)
            {
                this.CallbackDisposable._DisposeSafe();

                CurrentPollingTask._TryWait(noDebugMessage: true);
            }

            this.Provider._DisposeSafe();
        }
        finally
        {
            base.DisposeImpl(ex);
        }
    }
}

[Flags]
public enum NormalizePathOption
{
    None = 0,
    NormalizeCaseFileName,
    NormalizeCaseDirectory,
}

