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
using System.Buffers;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using System.Linq;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using System.Diagnostics;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Win32;

#pragma warning disable 0618

#pragma warning disable CA2101 // Specify marshaling for P/Invoke string arguments
#pragma warning disable CA1401 // P/Invokes should not be visible
#pragma warning disable CA1416 // プラットフォームの互換性の検証

// Some parts of this program are from Microsoft CoreCLR - https://github.com/dotnet/coreclr
// 
// The MIT License (MIT)
// 
// Copyright (c) .NET Foundation and Contributors
// 
// All rights reserved.
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.


namespace IPA.Cores.Helper.Basic
{
    public static class MsRegHelper
    {
        public static RegistryKey GetKey(this RegRoot root)
        {
            switch (root)
            {
                case RegRoot.ClassesRoot:
                    return Registry.ClassesRoot;
                case RegRoot.LocalMachine:
                    return Registry.LocalMachine;
                case RegRoot.CurrentUser:
                    return Registry.CurrentUser;
                case RegRoot.Users:
                    return Registry.Users;
                default:
                    throw new ArgumentException(nameof(root));
            }
        }
    }
}
namespace IPA.Cores.Basic
{
    public static partial class Win32ApiUtil
    {
        static readonly PathParser WindowsPathParser = PathParser.GetInstance(FileSystemStyle.Windows);

        public static bool IsUserAnAdmin()
        {
            return Win32Api.Shell32.IsUserAnAdmin();
        }

        public static Encoding? GetConsoleOutputEncoding()
        {
            try
            {
                uint codepage = Win32Api.Kernel32.GetConsoleOutputCP();

                if (codepage >= 1)
                {
                    return Encoding.GetEncoding((int)codepage);
                }
            }
            catch { }

            return null;
        }

        public static bool IsUncServerRootPath(string path, [NotNullWhen(true)] out string? normalizedPath)
        {
            normalizedPath = null;

            path = path._NonNullTrim();

            path = WindowsPathParser.NormalizeDirectorySeparator(path);

            path = WindowsPathParser.RemoveLastSeparatorChar(path);

            if (path.StartsWith(@"\\"))
            {
                if (path.Length >= 3 && path[2] != '\\')
                {
                    int r = path.IndexOf(@"\", 2);
                    if (r == -1)
                    {
                        normalizedPath = path;
                        return true;
                    }
                }
            }

            return false;
        }

        public static IReadOnlyList<string> EnumNetworkShareDirectories(string path)
        {
            path = path._NonNullTrim();

            path = WindowsPathParser.NormalizeDirectorySeparator(path);

            if (path.StartsWith(@"\\") == false)
                throw new ArgumentException($"\"{path}\" is not an UNC path.");

            int r = path.IndexOf(@"\", 2);
            if (r != -1)
                path = path.Substring(0, r);

            var entries = Win32Api.NetApi32.EnumNetShares(path);
            List<string> ret = new List<string>();

            foreach (var item in entries
                .Where(x => x.shi1_type.BitAny(Win32Api.NetApi32.SHARE_TYPE.STYPE_DEVICE | Win32Api.NetApi32.SHARE_TYPE.STYPE_IPC | Win32Api.NetApi32.SHARE_TYPE.STYPE_PRINTQ) == false)
                .Where(x => x.shi1_netname._IsFilled() && x.shi1_netname._IsSamei("print$") == false))
            {
                ret.Add(item.shi1_netname.Trim());
            }

            return ret;
        }

        public static int FillAttributeInfo(string path, ref Win32Api.Kernel32.WIN32_FILE_ATTRIBUTE_DATA data, bool returnErrorOnNotFound)
        {
            int errorCode = Win32Api.Errors.ERROR_SUCCESS;

            // Neither GetFileAttributes or FindFirstFile like trailing separators
            path = Win32PathInternal.TrimEndingDirectorySeparator(path);

            using (Win32Api.Win32DisableMediaInsertionPrompt.Create())
            {
                if (!Win32Api.Kernel32.GetFileAttributesEx(path, Win32Api.Kernel32.GET_FILEEX_INFO_LEVELS.GetFileExInfoStandard, ref data))
                {
                    errorCode = Marshal.GetLastWin32Error();
                    if (errorCode != Win32Api.Errors.ERROR_FILE_NOT_FOUND
                        && errorCode != Win32Api.Errors.ERROR_PATH_NOT_FOUND
                        && errorCode != Win32Api.Errors.ERROR_NOT_READY
                        && errorCode != Win32Api.Errors.ERROR_INVALID_NAME
                        && errorCode != Win32Api.Errors.ERROR_BAD_PATHNAME
                        && errorCode != Win32Api.Errors.ERROR_BAD_NETPATH
                        && errorCode != Win32Api.Errors.ERROR_BAD_NET_NAME
                        && errorCode != Win32Api.Errors.ERROR_INVALID_PARAMETER
                        && errorCode != Win32Api.Errors.ERROR_NETWORK_UNREACHABLE
                        && errorCode != Win32Api.Errors.ERROR_NETWORK_ACCESS_DENIED
                        && errorCode != Win32Api.Errors.ERROR_INVALID_HANDLE  // eg from \\.\CON
                        )
                    {
                        // Assert so we can track down other cases (if any) to add to our test suite
                        Debug.Assert(errorCode == Win32Api.Errors.ERROR_ACCESS_DENIED || errorCode == Win32Api.Errors.ERROR_SHARING_VIOLATION,
                            $"Unexpected error code getting attributes {errorCode}");

                        // Files that are marked for deletion will not let you GetFileAttributes,
                        // ERROR_ACCESS_DENIED is given back without filling out the data struct.
                        // FindFirstFile, however, will. Historically we always gave back attributes
                        // for marked-for-deletion files.
                        //
                        // Another case where enumeration works is with special system files such as
                        // pagefile.sys that give back ERROR_SHARING_VIOLATION on GetAttributes.
                        //
                        // Ideally we'd only try again for known cases due to the potential performance
                        // hit. The last attempt to do so baked for nearly a year before we found the
                        // pagefile.sys case. As such we're probably stuck filtering out specific
                        // cases that we know we don't want to retry on.

                        var findData = new Win32Api.Kernel32.WIN32_FIND_DATA();
                        using (Win32Api.SafeFindHandle handle = Win32Api.Kernel32.FindFirstFile(path, ref findData))
                        {
                            if (handle.IsInvalid)
                            {
                                errorCode = Marshal.GetLastWin32Error();
                            }
                            else
                            {
                                errorCode = Win32Api.Errors.ERROR_SUCCESS;
                                data.PopulateFrom(ref findData);
                            }
                        }
                    }
                }
            }

            if (errorCode != Win32Api.Errors.ERROR_SUCCESS && !returnErrorOnNotFound)
            {
                switch (errorCode)
                {
                    case Win32Api.Errors.ERROR_FILE_NOT_FOUND:
                    case Win32Api.Errors.ERROR_PATH_NOT_FOUND:
                    case Win32Api.Errors.ERROR_NOT_READY: // Removable media not ready
                        // Return default value for backward compatibility
                        data.dwFileAttributes = -1;
                        return Win32Api.Errors.ERROR_SUCCESS;
                }
            }

            return errorCode;
        }

        public static FileAttributes GetAttributes(string fullPath, bool backupMode = false)
        {
            int flags = backupMode ? Win32Api.Kernel32.FileOperations.FILE_FLAG_BACKUP_SEMANTICS : 0;

            Win32Api.Kernel32.WIN32_FILE_ATTRIBUTE_DATA data = new Win32Api.Kernel32.WIN32_FILE_ATTRIBUTE_DATA();
            int errorCode = FillAttributeInfo(fullPath, ref data, returnErrorOnNotFound: true);
            if (errorCode != 0)
                throw PalWin32FileStream.GetExceptionForWin32Error(errorCode, fullPath);

            return (FileAttributes)data.dwFileAttributes;
        }

        public static void SetAttributes(string fullPath, FileAttributes attributes)
        {
            if (!Win32Api.Kernel32.SetFileAttributes(fullPath, (int)attributes))
            {
                int errorCode = Marshal.GetLastWin32Error();
                if (errorCode == Win32Api.Errors.ERROR_INVALID_PARAMETER)
                    throw new ArgumentException(nameof(attributes));
                throw PalWin32FileStream.GetExceptionForWin32Error(errorCode, fullPath);
            }
        }

        public static void SetLastWriteTime(string fullPath, DateTimeOffset time, bool asDirectory, bool backupMode = false)
        {
            using (SafeFileHandle handle = OpenHandle(fullPath, asDirectory, true, backupMode))
            {
                if (!Win32Api.Kernel32.SetFileTime(handle, lastWriteTime: time.ToFileTime()))
                {
                    throw PalWin32FileStream.GetExceptionForLastWin32Error(fullPath);
                }
            }
        }

        public static void SetCreationTime(string fullPath, DateTimeOffset time, bool asDirectory, bool backupMode = false)
        {
            using (SafeFileHandle handle = OpenHandle(fullPath, asDirectory, true, backupMode))
            {
                if (!Win32Api.Kernel32.SetFileTime(handle, creationTime: time.ToFileTime()))
                {
                    throw PalWin32FileStream.GetExceptionForLastWin32Error(fullPath);
                }
            }
        }

        public static void SetLastAccessTime(string fullPath, DateTimeOffset time, bool asDirectory, bool backupMode = false)
        {
            using (SafeFileHandle handle = OpenHandle(fullPath, asDirectory, true, backupMode))
            {
                if (!Win32Api.Kernel32.SetFileTime(handle, lastAccessTime: time.ToFileTime()))
                {
                    throw PalWin32FileStream.GetExceptionForLastWin32Error(fullPath);
                }
            }
        }

        public static SafeFileHandle OpenHandle(string fullPath, bool asDirectory, bool writeMode = false, bool backupMode = false, bool asyncMode = true, int additionalFlags = 0)
        {
            string root = fullPath.Substring(0, Win32PathInternal.GetRootLength(fullPath.AsSpan()));
            if (root == fullPath && root[1] == Path.VolumeSeparatorChar)
            {
                // intentionally not fullpath, most upstack public APIs expose this as path.
                throw new ArgumentException("path");
            }

            if (asyncMode)
                additionalFlags |= (int)FileOptions.Asynchronous;

            using (Lfs.EnterDisableMediaInsertionPrompt())
            {
                SafeFileHandle handle = Win32Api.Kernel32.CreateFile(
                fullPath,
                writeMode ? Win32Api.Kernel32.GenericOperations.GENERIC_WRITE | Win32Api.Kernel32.GenericOperations.GENERIC_READ : Win32Api.Kernel32.GenericOperations.GENERIC_READ,
                FileShare.ReadWrite | FileShare.Delete,
                FileMode.Open,
                ((asDirectory || backupMode) ? Win32Api.Kernel32.FileOperations.FILE_FLAG_BACKUP_SEMANTICS : 0) | additionalFlags);

                if (handle.IsInvalid)
                {
                    int errorCode = Marshal.GetLastWin32Error();

                    // NT5 oddity - when trying to open "C:\" as a File,
                    // we usually get ERROR_PATH_NOT_FOUND from the OS.  We should
                    // probably be consistent w/ every other directory.
                    if (!asDirectory && errorCode == Win32Api.Errors.ERROR_PATH_NOT_FOUND && fullPath.Equals(Directory.GetDirectoryRoot(fullPath)))
                        errorCode = Win32Api.Errors.ERROR_ACCESS_DENIED;

                    throw PalWin32FileStream.GetExceptionForWin32Error(errorCode, fullPath);
                }

                handle._EnsureThreadPoolBindingInitialized();

                return handle;
            }
        }

        public static void ThrowLastWin32Error(string? argument = null)
            => ThrowWin32Error(null, argument);

        public static Exception ThrowWin32Error(int? errorCode = null, string? argument = null)
        {
            var exception = GetWin32ErrorException(errorCode, argument);
            throw exception;
#pragma warning disable CS0162
            return exception;
#pragma warning restore CS0162
        }

        public static Exception GetWin32ErrorException(int? errorCode = null, string? argument = null)
            => PalWin32FileStream.GetExceptionForWin32Error(errorCode ?? Marshal.GetLastWin32Error(), argument);


        public static string GetFinalPath(SafeFileHandle handle,
            Win32Api.Kernel32.FinalPathFlags flags = Win32Api.Kernel32.FinalPathFlags.FILE_NAME_NORMALIZED | Win32Api.Kernel32.FinalPathFlags.VOLUME_NAME_DOS)
        {
            StringBuilder str = new StringBuilder(260 + 10);
            int r;

            r = Win32Api.Kernel32.GetFinalPathNameByHandle(handle, str, 260, flags);

            if (r >= 260)
            {
                str = new StringBuilder(65536 + 10);
                r = Win32Api.Kernel32.GetFinalPathNameByHandle(handle, str, 65536, flags);
            }

            if (r == 0)
                Win32ApiUtil.ThrowLastWin32Error();

            return str.ToString();
        }

        public static async Task FileZeroClearAsync(SafeFileHandle handle, string pathForReference, long offset, long size, CancellationToken cancel = default)
        {
            if (Env.IsWindows == false) throw new PlatformNotSupportedException();

            if (offset < 0) throw new ArgumentOutOfRangeException("offset");
            if (size < 0) throw new ArgumentOutOfRangeException("size");
            if (size == 0)
                return;

            Win32Api.Kernel32.FILE_ZERO_DATA_INFORMATION data = new Win32Api.Kernel32.FILE_ZERO_DATA_INFORMATION(offset, size);
            ReadOnlyMemoryBuffer<byte> inBuffer = ReadOnlyMemoryBuffer<byte>.FromStruct(data);

            await Win32Api.Kernel32.DeviceIoControlAsync(handle, Win32Api.Kernel32.EIOControlCode.FsctlSetZeroData, inBuffer, null, pathForReference, cancel);
        }

        public static async Task<Win32Api.Kernel32.DISK_GEOMETRY> DiskGetDriveGeometryAsync(SafeFileHandle handle, string pathForReference, CancellationToken cancel = default)
        {
            if (Env.IsWindows == false) throw new PlatformNotSupportedException();

            MemoryBuffer<byte> outBuffer = new MemoryBuffer<byte>(Util.SizeOfStruct<Win32Api.Kernel32.DISK_GEOMETRY>());

            await Win32Api.Kernel32.DeviceIoControlAsync(handle, Win32Api.Kernel32.EIOControlCode.DiskGetDriveGeometry, null, outBuffer, pathForReference, cancel);

            return outBuffer.Memory._AsStructSafe<Win32Api.Kernel32.DISK_GEOMETRY>();
        }

        public static async Task SetSparseFileAsync(SafeFileHandle handle, string pathForReference, CancellationToken cancel = default)
        {
            if (Env.IsWindows == false) return;

            await Win32Api.Kernel32.DeviceIoControlAsync(handle, Win32Api.Kernel32.EIOControlCode.FsctlSetSparse, null, null, pathForReference, cancel);
        }

        public static long GetCompressedFileSize(string filename)
            => (long)Win32Api.Kernel32.GetCompressedFileSize(filename);

        public static Task SetCompressionFlagAsync(string path, bool isDirectory, bool compressionEnabled, CancellationToken cancel = default)
            => Util.DoMultipleActionsAsync(MultipleActionsFlag.AnyOk, cancel,
                () => SetCompressionFlagAsync(path, isDirectory, compressionEnabled, false, cancel),
                () => SetCompressionFlagAsync(path, isDirectory, compressionEnabled, true, cancel)
                );

        public static async Task SetCompressionFlagAsync(string path, bool isDirectory, bool compressionEnabled, bool isBackupMode, CancellationToken cancel = default)
        {
            using (var handle = OpenHandle(path, isDirectory, true, isBackupMode))
            {
                await SetCompressionFlagAsync(handle, compressionEnabled, path, cancel);
            }
        }

        public static async Task SetCompressionFlagAsync(SafeFileHandle handle, bool compressionEnabled, string? pathForReference = null, CancellationToken cancel = default)
        {
            if (Env.IsWindows == false) return;

            ushort inFlag = (ushort)(compressionEnabled ? Win32Api.Kernel32.COMPRESSION_FORMAT_DEFAULT : Win32Api.Kernel32.COMPRESSION_FORMAT_NONE);

            var bufferIn = ReadOnlyMemoryBuffer<byte>.FromStruct(inFlag);
            var bufferOut = new MemoryBuffer<byte>();

            await Win32Api.Kernel32.DeviceIoControlAsync(handle, Win32Api.Kernel32.EIOControlCode.FsctlSetCompression, bufferIn, bufferOut, pathForReference, cancel);
        }

        public static List<Tuple<string, long>>? EnumAlternateStreams(string path, long maxSize, int maxNum)
        {
            if (Env.IsWindows == false) return null;

            return Util.DoMultipleFuncs(MultipleActionsFlag.AnyOk, default,
                () => EnumAlternateStreamsInternal_UseFindFirstApi(path, maxSize, maxNum),
                () => EnumAlternateStreamsInternal_UseNtDllApi(path, maxSize, maxNum, true)
                );
        }

        public static List<Tuple<string, long>>? EnumAlternateStreamsInternal_UseNtDllApi(string path, long maxSize, int maxNum, bool backupMode = false)
        {
            if (Env.IsWindows == false) return null;

            using (var fileHandle = Win32ApiUtil.OpenHandle(path, false, false, backupMode))
            {
                var list = Win32Api.NtDll.EnumAlternateStreamInformation(fileHandle);

                List<Tuple<string, long>> ret = new List<Tuple<string, long>>();

                foreach (var info in list)
                {
                    if (info.StreamName._IsSamei("::$DATA") == false && info.StreamName.StartsWith(":") && info.StreamName.EndsWith(":$DATA", StringComparison.OrdinalIgnoreCase)
                           && info.StreamSize <= maxSize)
                    {
                        ret.Add(new Tuple<string, long>(info.StreamName, info.StreamSize));
                        if (ret.Count >= maxNum)
                        {
                            break;
                        }
                    }
                }

                return ret;
            }
        }

        public static List<Tuple<string, long>>? EnumAlternateStreamsInternal_UseFindFirstApi(string path, long maxSize, int maxNum)
        {
            if (Env.IsWindows == false) return null;

            Win32Api.Kernel32.WIN32_FIND_STREAM_DATA data;

            var findHandle = Win32Api.Kernel32.FindFirstStreamW(path, out data);

            if (findHandle.IsInvalid)
            {
                throw new ApplicationException($"FindFirstStreamW Error: {Marshal.GetLastWin32Error()}");
            }

            List<Tuple<string, long>> ret = new List<Tuple<string, long>>();

            using (findHandle)
            {
                while (true)
                {
                    if (data.cStreamName._IsSamei("::$DATA") == false && data.cStreamName.StartsWith(":") && data.cStreamName.EndsWith(":$DATA", StringComparison.OrdinalIgnoreCase)
                        && data.StreamSize.QuadPart <= maxSize)
                    {
                        ret.Add(new Tuple<string, long>(data.cStreamName, data.StreamSize.QuadPart));
                        if (ret.Count >= maxNum)
                        {
                            break;
                        }
                    }

                    if (Win32Api.Kernel32.FindNextStreamW(findHandle, out data) == false)
                    {
                        break;
                    }
                }
            }

            return ret;
        }

        //class OverlappedContext : IAsyncResult
        //{
        //    public object AsyncState { get; }

        //    public OverlappedContext(object state)
        //    {
        //        this.AsyncState = state;
        //    }

        //    public WaitHandle AsyncWaitHandle => throw new NotImplementedException();

        //    public bool CompletedSynchronously => throw new NotImplementedException();

        //    public bool IsCompleted => throw new NotImplementedException();

        //    public unsafe void IOCompletionCallback(uint errorCode, uint numBytes, NativeOverlapped* overlapped)
        //    {
        //        Dbg.Where();
        //        Overlapped.Unpack(overlapped);
        //        Overlapped.Free(overlapped);
        //    }
        //}

        unsafe class InternalOverlappedContext<TResult>
        {
            readonly public CriticalSection LockObj = new CriticalSection<InternalOverlappedContext<TResult>>();

            public ReadOnlyMemoryBuffer<byte>? InBuffer = null;
            public ValueHolder InBufferPinHolder;

            public MemoryBuffer<byte>? OutBuffer = null;
            public ValueHolder OutBufferPinHolder;

            public Overlapped? Overlapped = null;
            public NativeOverlapped* NativeOverlapped = null;

            public Win32CallOverlappedCompleteProc<TResult>? UserCompleteProc;

            public CancellationTokenRegistration CancelRegistration;

            public TaskCompletionSource<TResult> CompletionSource = new TaskCompletionSource<TResult>(CoresConfig.TaskAsyncSettings.AsyncEventTaskCreationOption);

            public unsafe void IOCompletionCallback(uint errorCode, uint numBytes, NativeOverlapped* overlapped)
            {
                Debug.Assert(overlapped != null);
                Debug.Assert(overlapped == NativeOverlapped);
                Completed(null, (int)errorCode, (int)numBytes);
            }

            Once CompletedFlag;

            public void Completed(Exception? exception, int errorCode, int returnedBytes)
            {
                if (CompletedFlag.IsFirstCall() == false)
                {
                    //Debug.Assert(false);
                    return;
                }

                try
                {
                    if (exception != null)
                        throw exception;

                    if (returnedBytes >= 0)
                    {
                        OutBuffer!.Seek(returnedBytes, SeekOrigin.Begin);
                    }

                    TResult ret = this.UserCompleteProc!(errorCode, returnedBytes);

                    CompletionSource.SetResult(ret);
                }
                catch (Exception ex)
                {
                    CompletionSource.SetException(ex);
                }
                finally
                {
                    FreeMemory();
                }
            }

            Once FreeFlag;

            public void FreeMemory()
            {
                if (FreeFlag.IsFirstCall())
                {
                    try
                    {
                        CancelRegistration._DisposeSafe();

                        InBufferPinHolder._DisposeSafe();

                        OutBufferPinHolder._DisposeSafe();

                        lock (LockObj)
                        {
                            if (Overlapped != null)
                            {
                                if (NativeOverlapped != null)
                                {
                                    Overlapped.Unpack(NativeOverlapped);
                                    Overlapped.Free(NativeOverlapped);
                                    NativeOverlapped = null;
                                }
                                Overlapped = null;
                            }
                        }
                    }
                    catch { }
                }
            }
        }

        public static unsafe Task<TResult> CallOverlappedAsync<TResult>(SafeFileHandle handle,
            Win32CallOverlappedMainProc mainProc, Win32CallOverlappedCompleteProc<TResult> completeProc, ReadOnlyMemoryBuffer<byte>? inBuffer, MemoryBuffer<byte>? outBuffer,
            CancellationToken cancel = default)
        {
            cancel.ThrowIfCancellationRequested();

            bool isAsync = handle.IsAsync;

            if (inBuffer == null) inBuffer = new ReadOnlyMemoryBuffer<byte>();
            if (outBuffer == null) outBuffer = new MemoryBuffer<byte>();

            inBuffer.SeekToBegin();
            outBuffer.SeekToBegin();

            InternalOverlappedContext<TResult> ctx = new InternalOverlappedContext<TResult>();
            bool isPending = false;

            try
            {
                ctx.UserCompleteProc = completeProc;

                ctx.InBuffer = inBuffer;
                ctx.InBufferPinHolder = inBuffer.PinLock();

                ctx.OutBuffer = outBuffer;
                ctx.OutBufferPinHolder = outBuffer.PinLock();

                if (isAsync)
                {
                    ctx.Overlapped = new Overlapped();
                    ctx.NativeOverlapped = ctx.Overlapped.Pack(ctx.IOCompletionCallback, null);
                    Debug.Assert(ctx.NativeOverlapped != null);
                }

                fixed (void* inPtr = &inBuffer.GetRefForFixedPtr())
                {
                    fixed (void* outPtr = &outBuffer.GetRefForFixedPtr())
                    {
                        RefInt returnedSize = new RefInt();
                        int err = mainProc(
                            inBuffer._IsEmpty() ? IntPtr.Zero : (IntPtr)inPtr,
                            inBuffer.Length,
                            outBuffer._IsEmpty() ? IntPtr.Zero : (IntPtr)outPtr,
                            outBuffer.Length,
                            returnedSize, (IntPtr)ctx.NativeOverlapped);

                        if (isAsync && err == Win32Api.Errors.ERROR_IO_PENDING)
                        {
                            if (isAsync)
                            {
                                ctx.CancelRegistration = cancel.Register(() =>
                                {
                                    lock (ctx.LockObj)
                                    {
                                        if (ctx.NativeOverlapped != null)
                                        {
                                            bool cancelOk = Win32Api.Kernel32.CancelIoEx(handle, ctx.NativeOverlapped);
                                            Con.WriteDebug($"cancelok = {cancelOk}");
                                        }
                                    }
                                });
                            }
                            Con.WriteTrace("CallOverlappedAsync: Pending.");
                            isPending = true;
                        }
                        else if (err == Win32Api.Errors.ERROR_SUCCESS)
                        {
                            if (isAsync == false)
                                ctx.Completed(null, Win32Api.Errors.ERROR_SUCCESS, returnedSize);
                            else
                                isPending = true;

                            //int outSz = 0;
                            //if (Win32Api.Kernel32.GetOverlappedResult(handle, ctx.NativeOverlapped, ref outSz, true) == false)
                            //{
                            //    Console.WriteLine("GetOverlappedResult error.");
                            //    ctx.Completed(null, err, 0);
                            //}
                            //else
                            //{
                            //    //Console.WriteLine("GetOverlappedResult ok.");

                            //    ctx.Completed(null, err, outSz);
                            //}
                        }
                        else
                        {
                            Con.WriteTrace("CallOverlappedAsync: Error: " + err);
                            ctx.Completed(null, err, returnedSize);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ctx.Completed(ex, 0, 0);
            }
            finally
            {
                if (isPending == false)
                {
                    ctx.FreeMemory();
                }
            }

            return ctx.CompletionSource.Task;
        }

        public static bool IsProcess(int pid)
        {
            try
            {
                using (var p = Process.GetProcessById(pid))
                {
                    if (p.HasExited)
                    {
                        return false;
                    }
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        public static bool WaitProcessExit(int pid, int timeout, CancellationToken cancel = default)
        {
            return TaskUtil.WaitWithPoll(timeout, 100, () => (IsProcess(pid) == false), cancel);
        }

        public static unsafe bool IsServiceInstalled(string name)
        {
            using (var sc = Win32Api.Advapi32.OpenSCManager(null, null, Win32Api.Kernel32.GenericOperations.GENERIC_READ))
            {
                if (sc.IsInvalid) ThrowLastWin32Error(name);

                using (var service = Win32Api.Advapi32.OpenService(sc, name, Win32Api.Kernel32.GenericOperations.GENERIC_READ))
                {
                    if (service.IsInvalid == false)
                    {
                        return true;
                    }

                    return false;
                }
            }
        }

        public static unsafe bool IsServiceRunning(string name)
        {
            using (var sc = Win32Api.Advapi32.OpenSCManager(null, null, Win32Api.Kernel32.GenericOperations.GENERIC_READ))
            {
                if (sc.IsInvalid) ThrowLastWin32Error(name);

                using (var service = Win32Api.Advapi32.OpenService(sc, name, Win32Api.Kernel32.GenericOperations.GENERIC_READ))
                {
                    if (service.IsInvalid == false)
                    {
                        if (Win32Api.Advapi32.QueryServiceStatus(service, out Win32Api.Advapi32.SERVICE_STATUS status))
                        {
                            if (status.currentState == Win32Api.Advapi32.ServiceControlStatus.STATE_CONTINUE_PENDING ||
                                status.currentState == Win32Api.Advapi32.ServiceControlStatus.STATE_PAUSE_PENDING ||
                                status.currentState == Win32Api.Advapi32.ServiceControlStatus.STATE_PAUSED ||
                                status.currentState == Win32Api.Advapi32.ServiceControlStatus.STATE_RUNNING ||
                                status.currentState == Win32Api.Advapi32.ServiceControlStatus.STATE_START_PENDING ||
                                status.currentState == Win32Api.Advapi32.ServiceControlStatus.STATE_STOP_PENDING)
                            {
                                return true;
                            }
                        }
                    }

                    return false;
                }
            }
        }

        public static unsafe void StopService(string name)
        {
            if (IsServiceRunning(name) == false) return;

            using (var sc = Win32Api.Advapi32.OpenSCManager(null, null, Win32Api.Advapi32.ServiceControllerOptions.SC_MANAGER_ALL))
            {
                if (sc.IsInvalid) ThrowLastWin32Error(name);

                using (var service = Win32Api.Advapi32.OpenService(sc, name, Win32Api.Advapi32.ServiceControllerOptions.SC_MANAGER_ALL))
                {
                    if (service.IsInvalid) ThrowLastWin32Error(name);

                    if (Win32Api.Advapi32.ControlService(service, IPA.Cores.Basic.Win32Api.Advapi32.ControlOptions.CONTROL_STOP, out _) == false)
                        ThrowLastWin32Error(name);
                }
            }

            TaskUtil.WaitWithPoll(30000, 250, () => !IsServiceRunning(name));
        }

        public static unsafe void StartService(string name)
        {
            if (IsServiceRunning(name)) return;

            using (var sc = Win32Api.Advapi32.OpenSCManager(null, null, Win32Api.Advapi32.ServiceControllerOptions.SC_MANAGER_ALL))
            {
                if (sc.IsInvalid) ThrowLastWin32Error(name);

                using (var service = Win32Api.Advapi32.OpenService(sc, name, Win32Api.Advapi32.ServiceControllerOptions.SC_MANAGER_ALL))
                {
                    if (service.IsInvalid) ThrowLastWin32Error(name);

                    if (Win32Api.Advapi32.StartService(service, 0, IntPtr.Zero) == false)
                        ThrowLastWin32Error(name);
                }
            }

            TaskUtil.WaitWithPoll(30000, 250, () => IsServiceRunning(name));
        }

        public static unsafe void UninstallService(string name)
        {
            StopService(name);

            using (var sc = Win32Api.Advapi32.OpenSCManager(null, null, Win32Api.Advapi32.ServiceControllerOptions.SC_MANAGER_ALL))
            {
                if (sc.IsInvalid) ThrowLastWin32Error(name);

                using (var service = Win32Api.Advapi32.OpenService(sc, name, Win32Api.Advapi32.ServiceControllerOptions.SC_MANAGER_ALL))
                {
                    if (service.IsInvalid) ThrowLastWin32Error(name);

                    if (Win32Api.Advapi32.DeleteService(service) == false)
                        ThrowLastWin32Error(name);
                }
            }
        }

        public static unsafe void InstallService(string name, string title, string description, string path)
        {
            using (var sc = Win32Api.Advapi32.OpenSCManager(null, null, Win32Api.Advapi32.ServiceControllerOptions.SC_MANAGER_ALL))
            {
                if (sc.IsInvalid) ThrowLastWin32Error(name);

                using (var service = Win32Api.Advapi32.CreateService(sc, name, title, Win32Api.Advapi32.ServiceOptions.SERVICE_ALL_ACCESS,
                    Win32Api.Advapi32.ServiceTypeOptions.SERVICE_TYPE_WIN32_OWN_PROCESS,
                    Win32Api.Advapi32.ServiceStartModes.START_TYPE_AUTO,
                    Win32Api.Advapi32.ServiceStartErrorModes.ERROR_CONTROL_NORMAL,
                    path, null, IntPtr.Zero, null, null, null))
                {
                    if (service.IsInvalid) ThrowLastWin32Error(name);

                    Win32Api.Advapi32.SERVICE_DESCRIPTION d;
                    d.description = Marshal.StringToHGlobalUni(description);
                    try
                    {
                        Win32Api.Advapi32.SERVICE_FAILURE_ACTIONS action = new Win32Api.Advapi32.SERVICE_FAILURE_ACTIONS();

                        int numActions = 3;

                        MemoryBuffer<int> actionArray = new MemoryBuffer<int>();
                        for (int i = 0; i < numActions; i++)
                        {
                            actionArray.WriteOne(Win32Api.Advapi32.SC_ACTION_RESTART);
                            actionArray.WriteOne(10 * 1000);
                        }

                        fixed (void* actionArrayPtr = &actionArray.GetRefForFixedPtr())
                        {
                            action.cActions = numActions;
                            action.dwResetPeriod = 1 * 60 * 60 * 24;
                            action.lpsaActions = (IntPtr)actionArrayPtr;

                            Win32Api.Advapi32.ChangeServiceConfig2(service, Win32Api.Advapi32.ServiceConfigOptions.SERVICE_CONFIG_DESCRIPTION, ref d);

                            Win32Api.Advapi32.ChangeServiceConfig2(service, Win32Api.Advapi32.ServiceConfigOptions.SERVICE_CONFIG_FAILURE_ACTIONS, ref action);
                        }
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(d.description);
                    }
                }
            }
        }
    }

    [Flags]
    public enum RegRoot
    {
        ClassesRoot = 0,
        LocalMachine = 1,
        CurrentUser = 2,
        Users = 3,
    }

    // レジストリアクセスクラスライブラリ
    public static class MsReg
    {
        public static bool WriteBin(RegRoot root, string keyName, string valueName, ReadOnlyMemory<byte> data)
        {
            return WriteValue(root, keyName, valueName, RegistryValueKind.Binary, data.ToArray());
        }

        public static bool WriteInt64(RegRoot root, string keyName, string valueName, long data)
        {
            return WriteValue(root, keyName, valueName, RegistryValueKind.QWord, data);
        }

        public static bool WriteInt32(RegRoot root, string keyName, string valueName, int data)
        {
            return WriteValue(root, keyName, valueName, RegistryValueKind.DWord, data);
        }

        public static bool WriteStr(RegRoot root, string keyName, string valueName, string data)
        {
            return WriteValue(root, keyName, valueName, RegistryValueKind.String, data);
        }

        public static bool WriteValue(RegRoot root, string keyName, string valueName, RegistryValueKind type, object data)
        {
            try
            {
                CreateKey(root, keyName);

                using (var key = root.GetKey().OpenSubKey(keyName, true))
                {
                    if (key == null) return false;

                    key.SetValue(valueName, data, type);

                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        public static bool DeleteValue(RegRoot root, string keyName, string valueName)
        {
            try
            {
                using (var key = root.GetKey().OpenSubKey(keyName, true))
                {
                    if (key == null) return false;

                    key.DeleteValue(valueName);

                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        public static bool DeleteKey(RegRoot root, string keyName)
        {
            try
            {
                root.GetKey().DeleteSubKey(keyName, true);

                return true;
            }
            catch
            {
                return false;
            }
        }

        public static bool CreateKey(RegRoot root, string keyName)
        {
            try
            {
                using (var key = root.GetKey().OpenSubKey(keyName, true))
                {
                    if (key != null) return true;
                }
                using (var key = root.GetKey().CreateSubKey(keyName, true))
                {
                    if (key == null) return false;
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        public static Memory<byte> ReadBin(RegRoot root, string keyName, string valueName, ReadOnlyMemory<byte> defaultValue = default)
        {
            object? o = ReadValue(root, keyName, valueName);

            if (o is byte[])
            {
                return (byte[])o;
            }
            else
            {
                return defaultValue._CloneMemory();
            }
        }

        public static long ReadInt64(RegRoot root, string keyName, string valueName, long defaultValue = 0)
        {
            object? o = ReadValue(root, keyName, valueName);

            if (o is long)
            {
                return (long)o;
            }
            else
            {
                return defaultValue;
            }
        }

        public static int ReadInt32(RegRoot root, string keyName, string valueName, int defaultValue = 0)
        {
            object? o = ReadValue(root, keyName, valueName);

            if (o is int)
            {
                return (int)o;
            }
            else
            {
                return defaultValue;
            }
        }

        public static string ReadStr(RegRoot root, string keyName, string valueName, string defaultValue = "")
        {
            object? o = ReadValue(root, keyName, valueName);

            if (o is string)
            {
                return (string)o;
            }
            else if (o is string[])
            {
                string? s = ((string[])o).FirstOrDefault();
                return s ?? defaultValue;
            }
            else
            {
                return defaultValue;
            }
        }

        public static object? ReadValue(RegRoot root, string keyName, string valueName, object? defaultValue = null)
        {
            try
            {
                using (var key = root.GetKey().OpenSubKey(keyName, false))
                {
                    if (key == null) return defaultValue;

                    object? value = key.GetValue(valueName);

                    if (value == null) return defaultValue;

                    return value;
                }
            }
            catch
            {
                return defaultValue;
            }
        }

        public static RegistryValueKind GetValueType(RegRoot root, string keyName, string valueName)
        {
            try
            {
                using (var key = root.GetKey().OpenSubKey(keyName, false))
                {
                    if (key == null) return RegistryValueKind.Unknown;

                    return key.GetValueKind(valueName);
                }
            }
            catch
            {
                return RegistryValueKind.Unknown;
            }
        }

        public static bool IsValue(RegRoot root, string keyName, string valueName)
        {
            try
            {
                using (var key = root.GetKey().OpenSubKey(keyName, false))
                {
                    if (key == null) return false;

                    if (key.GetValue(valueName) == null)
                    {
                        return false;
                    }

                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        public static string[]? EnumValue(RegRoot root, string keyName)
        {
            try
            {
                using (var key = root.GetKey().OpenSubKey(keyName, false))
                {
                    return key!.GetValueNames();
                }
            }
            catch
            {
                return null;
            }
        }

        public static string[]? EnumKey(RegRoot root, string keyName)
        {
            try
            {
                using (var key = root.GetKey().OpenSubKey(keyName, false))
                {
                    return key!.GetSubKeyNames();
                }
            }
            catch
            {
                return null;
            }
        }

        public static bool IsKey(RegRoot root, string keyName)
        {
            try
            {
                using (var key = root.GetKey().OpenSubKey(keyName, false))
                {
                    if (key == null) return false;
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        public static void PrintRegKeys(RegRoot root, string key, int depth = 0)
        {
            const int padUnit = 2;

            key._TryTrimStartWith(out key, StrCmpi, @"\");

            string pad1 = ' '._MakeCharArray(depth * padUnit);
            string pad2 = ' '._MakeCharArray((depth + 1) * padUnit);

            Con.WriteLine($"{pad1}[{root}\\{key}]");

            var x = MsReg.EnumKey(root, key);

            var subkeys = MsReg.EnumKey(root, key)._EmptyArrayIfNull().OrderBy(x => x, StrCmpi);
            var values = MsReg.EnumValue(root, key)._EmptyArrayIfNull().OrderBy(x => x, StrCmpi);

            foreach (var value in values)
            {
                object? data = MsReg.ReadValue(root, key, value);
                Con.WriteLine($"{pad2}{value}='{data._ObjectToJson(compact: true)}'");
            }

            Con.WriteLine();
            foreach (var subkey in subkeys)
            {
                PrintRegKeys(root, key + @"\" + subkey, depth + 1);
            }
        }
    }

    public delegate int Win32CallOverlappedMainProc(IntPtr inPtr, int inSize, IntPtr outPtr, int outSize, RefInt outReturnedSize, IntPtr overlapped);
    public delegate TResult Win32CallOverlappedCompleteProc<TResult>(int errorCode, int numReturnedSize);



}


