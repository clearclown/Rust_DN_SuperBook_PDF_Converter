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

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using System.Diagnostics;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;

#pragma warning disable 0618

#pragma warning disable CA2101 // Specify marshaling for P/Invoke string arguments
#pragma warning disable CA1401 // P/Invokes should not be visible

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

namespace IPA.Cores.Basic;

public static class Win32Api
{
    // DLL names
    public static partial class Libraries
    {
        public const string Advapi32 = "advapi32.dll";
        public const string BCrypt = "BCrypt.dll";
        public const string Crypt32 = "crypt32.dll";
        public const string Kernel32 = "kernel32.dll";
        public const string Shell32 = "shell32.dll";
        public const string NetApi32 = "Netapi32.dll";
        public const string Ole32 = "ole32.dll";
        public const string OleAut32 = "oleaut32.dll";
        public const string User32 = "user32.dll";
        public const string NtDll = "ntdll.dll";
    }

    // DLL import functions
    public static partial class Kernel32
    {
        [DllImport(Libraries.Kernel32, SetLastError = true)]
        public static extern bool CloseHandle(IntPtr handle);

        [DllImport(Libraries.Kernel32, SetLastError = true)]
        public static extern uint GetConsoleOutputCP();

        [DllImport(Libraries.Kernel32, SetLastError = true)]
        public static extern SafeProcessHandle GetCurrentProcess();

        [DllImport(Libraries.Kernel32, SetLastError = true, ExactSpelling = true)]

        public static extern bool SetThreadErrorMode(uint dwNewMode, out uint lpOldMode);

        [DllImport(Libraries.Kernel32, EntryPoint = "CreateFileW", SetLastError = true, CharSet = CharSet.Unicode, BestFitMapping = false, ExactSpelling = true)]
        private unsafe static extern IntPtr CreateFilePrivate(
            string lpFileName,
            int dwDesiredAccess,
            FileShare dwShareMode,
            SECURITY_ATTRIBUTES* securityAttrs,
            FileMode dwCreationDisposition,
            int dwFlagsAndAttributes,
            IntPtr hTemplateFile);

        public unsafe static SafeFileHandle CreateFile(
            string lpFileName,
            int dwDesiredAccess,
            FileShare dwShareMode,
            ref SECURITY_ATTRIBUTES securityAttrs,
            FileMode dwCreationDisposition,
            int dwFlagsAndAttributes,
            IntPtr hTemplateFile)
        {
            lpFileName = Win32PathInternal.EnsureExtendedPrefixIfNeeded(lpFileName);
            fixed (SECURITY_ATTRIBUTES* sa = &securityAttrs)
            {
                IntPtr handle = CreateFilePrivate(lpFileName, dwDesiredAccess, dwShareMode, sa, dwCreationDisposition, dwFlagsAndAttributes, hTemplateFile);
                try
                {
                    return new SafeFileHandle(handle, ownsHandle: true);
                }
                catch
                {
                    CloseHandle(handle);
                    throw;
                }
            }
        }

        public unsafe static SafeFileHandle CreateFile(
            string lpFileName,
            int dwDesiredAccess,
            FileShare dwShareMode,
            FileMode dwCreationDisposition,
            int dwFlagsAndAttributes)
        {
            IntPtr handle = CreateFile_IntPtr(lpFileName, dwDesiredAccess, dwShareMode, dwCreationDisposition, dwFlagsAndAttributes);
            try
            {
                return new SafeFileHandle(handle, ownsHandle: true);
            }
            catch
            {
                CloseHandle(handle);
                throw;
            }
        }

        public unsafe static IntPtr CreateFile_IntPtr(
            string lpFileName,
            int dwDesiredAccess,
            FileShare dwShareMode,
            FileMode dwCreationDisposition,
            int dwFlagsAndAttributes)
        {
            lpFileName = Win32PathInternal.EnsureExtendedPrefixIfNeeded(lpFileName);
            return CreateFilePrivate(lpFileName, dwDesiredAccess, dwShareMode, null, dwCreationDisposition, dwFlagsAndAttributes, IntPtr.Zero);
        }

        [DllImport(Libraries.Kernel32, SetLastError = true)]
        public static extern unsafe bool CancelIoEx(SafeHandle handle, NativeOverlapped* lpOverlapped);

        [DllImport(Libraries.Kernel32, SetLastError = true, CharSet = CharSet.Auto)]
        unsafe public static extern bool GetOverlappedResult(
            SafeFileHandle hFile,
            NativeOverlapped* lpOverlapped,
            ref int lpNumberOfBytesTransferred,
            bool bWait);

        [DllImport(Libraries.Kernel32, CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool DeviceIoControl
        (
            SafeFileHandle fileHandle,
            EIOControlCode ioControlCode,
            ref ushort inBuffer,
            uint cbInBuffer,
            IntPtr outBuffer,
            uint cbOutBuffer,
            out uint cbBytesReturned,
            IntPtr overlapped
        );

        [DllImport(Libraries.Kernel32, CharSet = CharSet.Unicode, SetLastError = true)]
        public unsafe static extern bool DeviceIoControl(SafeFileHandle fileHandle, EIOControlCode ioControlCode, IntPtr inBuffer, uint cbInBuffer, IntPtr outBuffer, uint cbOutBuffer,
            out uint cbBytesReturned, IntPtr overlapped);

        public static async Task<bool> DeviceIoControlAsync(
            SafeFileHandle fileHandle, EIOControlCode ioControlCode, ReadOnlyMemoryBuffer<byte>? inBuffer, MemoryBuffer<byte>? outBuffer, string? pathForReference, CancellationToken cancel = default)
        {
            bool ret = await Win32ApiUtil.CallOverlappedAsync<bool>(fileHandle,
                (inPtr, inSize, outPtr, outSize, outReturnedSize, overlapped) =>
                {
                    bool b = DeviceIoControl(fileHandle, ioControlCode, inPtr, (uint)inSize, outPtr, (uint)outSize, out uint retBytes, overlapped);
                    if (b) return Errors.ERROR_SUCCESS;
                    return Marshal.GetLastWin32Error();
                },
                (errorCode, numReturnedSize) =>
                {
                    if (errorCode != Errors.ERROR_SUCCESS)
                        throw Win32ApiUtil.ThrowWin32Error(errorCode, pathForReference);
                    return true;
                },
                inBuffer,
                outBuffer,
                cancel);

            return ret;
        }


        //public static unsafe Task<uint> DeviceIoControlAsync2<TIn, TOut>(
        //    SafeFileHandle fileHandle, uint ioControlCode, TIn inBuffer, ValueRef<TOut> outBuffer, string pathForReference, uint ?inBufferSize = null, uint ?outBufferSize = null)
        //    where TIn: unmanaged
        //    where TOut: unmanaged
        //{
        //    bool isAsync = fileHandle.IsAsync();
        //    uint returnedSize = 0;

        //    DeviceIoControlAsyncResult result = new DeviceIoControlAsyncResult(null);

        //    //Overlapped over = new Overlapped(0, 0, IntPtr.Zero, result);
        //    //NativeOverlapped *overNative = over.Pack(

        //    void* ptrIn = &inBuffer;
        //    {
        //        fixed (void* ptrOut = &outBuffer.Value)
        //        {
        //            bool ret = DeviceIoControl(fileHandle, ioControlCode, ptrIn, inBufferSize ?? (uint)Marshal.SizeOf<TIn>(), ptrOut, outBufferSize ?? (uint)outBuffer.Size,
        //                out returnedSize, IntPtr.Zero);

        //            if (ret == false)
        //            {
        //                Win32ApiUtil.ThrowLastWin32Error(pathForReference);
        //            }

        //            return Task.FromResult(returnedSize);
        //        }
        //    }
        //}

        [DllImport(Libraries.Kernel32, SetLastError = true, ExactSpelling = true)]
        public static extern bool SetFileInformationByHandle(SafeFileHandle hFile, FILE_INFO_BY_HANDLE_CLASS FileInformationClass, ref FILE_BASIC_INFO lpFileInformation, uint dwBufferSize);

        // Default values indicate "no change".  Use defaults so that we don't force callsites to be aware of the default values
        public static unsafe bool SetFileTime(
            SafeFileHandle hFile,
            long creationTime = -1,
            long lastAccessTime = -1,
            long lastWriteTime = -1,
            long changeTime = -1,
            uint fileAttributes = 0)
        {
            FILE_BASIC_INFO basicInfo = new FILE_BASIC_INFO()
            {
                CreationTime = creationTime,
                LastAccessTime = lastAccessTime,
                LastWriteTime = lastWriteTime,
                ChangeTime = changeTime,
                FileAttributes = fileAttributes
            };

            return SetFileInformationByHandle(hFile, FILE_INFO_BY_HANDLE_CLASS.FileBasicInfo, ref basicInfo, (uint)sizeof(FILE_BASIC_INFO));
        }

        [DllImport(Libraries.Kernel32, EntryPoint = "SetFileAttributesW", SetLastError = true, CharSet = CharSet.Unicode, BestFitMapping = false)]
        private static extern bool SetFileAttributesPrivate(string name, int attr);

        public static bool SetFileAttributes(string name, int attr)
        {
            name = Win32PathInternal.EnsureExtendedPrefixIfNeeded(name);
            return SetFileAttributesPrivate(name, attr);
        }

        [DllImport(Libraries.Kernel32, EntryPoint = "GetFileAttributesExW", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool GetFileAttributesExPrivate(string name, GET_FILEEX_INFO_LEVELS fileInfoLevel, ref WIN32_FILE_ATTRIBUTE_DATA lpFileInformation);

        public static bool GetFileAttributesEx(string name, GET_FILEEX_INFO_LEVELS fileInfoLevel, ref WIN32_FILE_ATTRIBUTE_DATA lpFileInformation)
        {
            name = Win32PathInternal.EnsureExtendedPrefixOverMaxPath(name);
            return GetFileAttributesExPrivate(name, fileInfoLevel, ref lpFileInformation);
        }

        [DllImport(Libraries.Kernel32, SetLastError = true)]
        public static extern bool FindClose(IntPtr hFindFile);

        [DllImport(Libraries.Kernel32, EntryPoint = "FindFirstFileExW", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern SafeFindHandle FindFirstFileExPrivate(string lpFileName, FINDEX_INFO_LEVELS fInfoLevelId, ref WIN32_FIND_DATA lpFindFileData, FINDEX_SEARCH_OPS fSearchOp, IntPtr lpSearchFilter, int dwAdditionalFlags);

        public static SafeFindHandle FindFirstFile(string fileName, ref WIN32_FIND_DATA data)
        {
            fileName = Win32PathInternal.EnsureExtendedPrefixIfNeeded(fileName);

            // use FindExInfoBasic since we don't care about short name and it has better perf
            return FindFirstFileExPrivate(fileName, FINDEX_INFO_LEVELS.FindExInfoBasic, ref data, FINDEX_SEARCH_OPS.FindExSearchNameMatch, IntPtr.Zero, 0);
        }

        [DllImport(Libraries.Kernel32, EntryPoint = "FindFirstStreamW", SetLastError = true, CharSet = CharSet.Unicode, BestFitMapping = false)]
        private static extern SafeFindHandle FindFirstStreamWPrivate(string lpFileName, STREAM_INFO_LEVELS InfoLevel, out WIN32_FIND_STREAM_DATA lpFindStreamData, int dwFlags);

        public static SafeFindHandle FindFirstStreamW(string lpFileName, out WIN32_FIND_STREAM_DATA lpFindStreamData)
        {
            lpFileName = Win32PathInternal.EnsureExtendedPrefixIfNeeded(lpFileName);

            return FindFirstStreamWPrivate(lpFileName, STREAM_INFO_LEVELS.FindStreamInfoStandard, out lpFindStreamData, 0);
        }

        [DllImport(Libraries.Kernel32, SetLastError = true, CharSet = CharSet.Auto, BestFitMapping = false)]
        public static extern bool FindNextStreamW(SafeFindHandle hFindStream, out WIN32_FIND_STREAM_DATA lpFindStreamData);

        [DllImport(Libraries.Kernel32, SetLastError = true, CharSet = CharSet.Auto)]
        public static extern int GetFinalPathNameByHandle(SafeFileHandle hFile, [MarshalAs(UnmanagedType.LPTStr)] StringBuilder lpszFilePath, int cchFilePath, FinalPathFlags dwFlags);

        [DllImport(Libraries.Kernel32, SetLastError = true)]
        static extern uint GetCompressedFileSize(string lpFileName, out uint lpFileSizeHigh);

        public static ulong GetCompressedFileSize(string filename)
        {
            uint high;
            uint low;
            low = GetCompressedFileSize(filename, out high);
            int error = Marshal.GetLastWin32Error();
            if (low == 0xFFFFFFFF && error != 0)
                throw Win32ApiUtil.ThrowWin32Error(error, filename);
            else
                return ((ulong)high << 32) + low;
        }
    }

    public static partial class User32
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct HWND
        {
            public IntPtr h;

            public static HWND Cast(IntPtr h)
            {
                HWND hTemp = new HWND();
                hTemp.h = h;
                return hTemp;
            }

            public static implicit operator IntPtr(HWND h)
            {
                return h.h;
            }

            public static HWND NULL
            {
                get
                {
                    HWND hTemp = new HWND();
                    hTemp.h = IntPtr.Zero;
                    return hTemp;
                }
            }

            public bool IsNull => this.h == IntPtr.Zero;

            public static bool operator ==(HWND hl, HWND hr)
            {
                return hl.h == hr.h;
            }

            public static bool operator !=(HWND hl, HWND hr)
            {
                return hl.h != hr.h;
            }

            override public bool Equals(object? oCompare)
            {
                HWND hr = Cast((HWND)oCompare!);
                return h == hr.h;
            }

            public override int GetHashCode()
            {
                return (int)h;
            }

            public override string ToString()
            {
                return $"0x{this.h.ToString("X")}";
            }
        }


        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int left;
            public int top;
            public int right;
            public int bottom;

            public RECT(int left, int top, int right, int bottom)
            {
                this.left = left;
                this.top = top;
                this.right = right;
                this.bottom = bottom;
            }

            public bool IsEmpty
            {
                get
                {
                    return left >= right || top >= bottom;
                }
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int x;
            public int y;

            public POINT(int x, int y)
            {
                this.x = x;
                this.y = y;
            }
        }

        // Window Messages
        public const int WM_COMMAND = 0x0111;
        public const int WM_SETTEXT = 0x000C;
        public const int WM_GETTEXT = 0x000D;
        public const int WM_GETTEXTLENGTH = 0x000E;

        // Window navigation
        public const int GW_HWNDFIRST = 0;
        public const int GW_HWNDLAST = 1;
        public const int GW_HWNDNEXT = 2;
        public const int GW_HWNDPREV = 3;
        public const int GW_OWNER = 4;
        public const int GW_CHILD = 5;

        public delegate bool EnumThreadWindowsCallback(HWND hWnd, IntPtr lParam);

        [DllImport(Libraries.User32)]
        public static extern bool EnumWindows(EnumThreadWindowsCallback callback, IntPtr extraData);

        [DllImport(Libraries.User32)]
        public static extern HWND GetParent(HWND hWnd);

        [DllImport(Libraries.User32, CharSet = CharSet.Auto, SetLastError = true)]
        public static extern int GetClassName(HWND hWnd, StringBuilder classname, int nMax);

        [DllImport(Libraries.User32, ExactSpelling = true, CharSet = CharSet.Auto)]
        public static extern HWND GetDlgItem(HWND hWnd, int nIDDlgItem);

        [DllImport(Libraries.User32, CharSet = CharSet.Unicode, ExactSpelling = true)]
        public static extern bool PostMessageW(HWND hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport(Libraries.User32, ExactSpelling = true, CharSet = CharSet.Unicode)]
        public static extern IntPtr SendMessageW(HWND hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport(Libraries.User32, ExactSpelling = true, CharSet = CharSet.Unicode)]
        public static extern IntPtr SendMessageW(HWND hWnd, int msg, IntPtr wParam, StringBuilder text);

        [DllImport(Libraries.User32, ExactSpelling = true, CharSet = CharSet.Unicode)]
        private static extern int GetWindowTextW(HWND hWnd, StringBuilder text, int nMaxCount);

        [DllImport(Libraries.User32, ExactSpelling = true)]
        public static extern int GetWindowTextLengthW(HWND hWnd);

        [DllImport(Libraries.User32, CharSet = CharSet.Unicode)]
        public static extern int SetWindowTextW(HWND hWnd, string text);

        [DllImport(Libraries.User32, ExactSpelling = true)]
        public static extern int GetWindowThreadProcessId(HWND hWnd, out int processId);

        [DllImport(Libraries.User32)]
        public static extern HWND GetWindow(HWND hWnd, int uCmd);

        public static void SetWindowTextWithSM(HWND hWnd, string text)
        {
            StringBuilder sb = new StringBuilder(text);

            SendMessageW(hWnd, WM_SETTEXT, IntPtr.Zero, sb);
        }

        public static string GetWindowText(HWND hWnd)
        {
            checked
            {
                int len = GetWindowTextLengthW(hWnd);

                StringBuilder tmp = new StringBuilder(len + 8);
                if (GetWindowTextW(hWnd, tmp, len + 1) == 0)
                {
                    return "";
                }
                return tmp.ToString();
            }
        }

        public static string GetWindowTextWithSM(HWND hWnd)
        {
            checked
            {
                int len = (int)SendMessageW(hWnd, WM_GETTEXTLENGTH, IntPtr.Zero, IntPtr.Zero);

                StringBuilder tmp = new StringBuilder(len + 8);
                if (SendMessageW(hWnd, WM_GETTEXT, (IntPtr)(len + 1), tmp) == (IntPtr)0)
                {
                    return "";
                }

                return tmp.ToString();
            }
        }

        public static string GetClassName(HWND hWnd)
        {
            StringBuilder tmp = new StringBuilder(260);
            if (GetClassName(hWnd, tmp, tmp.Capacity) == 0) return "";
            return tmp.ToString();
        }

        public static HashSet<HWND> EnumAllTopWindow()
        {
            HashSet<HWND> o = new HashSet<HWND>();

            EnumWindows((hWnd, param) =>
            {
                HWND hParent = GetParent(hWnd);

                string? c1 = GetClassName(hWnd);
                string? c2 = null;

                if (hParent != HWND.NULL)
                {
                    c2 = GetClassName(hParent);
                }

                if (c1._IsSamei("SysIPAddress32") == false && (c2._IsEmpty() || c2._IsSamei("SysIPAddress32") == false))
                {
                    o.Add(hWnd);
                }

                return true;
            }, IntPtr.Zero);

            return o;
        }
    }

    public static partial class Shell32
    {
        [DllImport(Libraries.Shell32, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsUserAnAdmin();
    }

    public static partial class NetApi32
    {
        // From http://www.pinvoke.net/default.aspx/netapi32/netshareenum.html

        #region External Calls
        [DllImport(Libraries.NetApi32, SetLastError = true)]
        static extern int NetApiBufferFree(IntPtr Buffer);
        [DllImport(Libraries.NetApi32, CharSet = CharSet.Unicode)]
        private static extern int NetShareEnum(
             StringBuilder ServerName,
             int level,
             ref IntPtr bufPtr,
             uint prefmaxlen,
             ref int entriesread,
             ref int totalentries,
             ref int resume_handle
             );
        #endregion
        #region External Structures
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct SHARE_INFO_1
        {
            public string shi1_netname;
            public SHARE_TYPE shi1_type;
            public string shi1_remark;
            public SHARE_INFO_1(string sharename, uint sharetype, string remark)
            {
                this.shi1_netname = sharename;
                this.shi1_type = (SHARE_TYPE)sharetype;
                this.shi1_remark = remark;
            }
            public override string ToString()
            {
                return shi1_netname;
            }
        }
        #endregion
        const uint MAX_PREFERRED_LENGTH = 0xFFFFFFFF;
        const int NERR_Success = 0;
        private enum NetError : uint
        {
            NERR_Success = 0,
            NERR_BASE = 2100,
            NERR_UnknownDevDir = (NERR_BASE + 16),
            NERR_DuplicateShare = (NERR_BASE + 18),
            NERR_BufTooSmall = (NERR_BASE + 23),
        }

        [Flags]
        public enum SHARE_TYPE : uint
        {
            STYPE_DISKTREE = 0,
            STYPE_PRINTQ = 1,
            STYPE_DEVICE = 2,
            STYPE_IPC = 3,
            STYPE_SPECIAL = 0x80000000,
        }
        public static SHARE_INFO_1[] EnumNetShares(string Server)
        {
            List<SHARE_INFO_1> ShareInfos = new List<SHARE_INFO_1>();
            int entriesread = 0;
            int totalentries = 0;
            int resume_handle = 0;
            int nStructSize = Marshal.SizeOf(typeof(SHARE_INFO_1));
            IntPtr bufPtr = IntPtr.Zero;
            StringBuilder server = new StringBuilder(Server);
            int ret = NetShareEnum(server, 1, ref bufPtr, MAX_PREFERRED_LENGTH, ref entriesread, ref totalentries, ref resume_handle);
            if (ret == NERR_Success)
            {
                IntPtr currentPtr = bufPtr;
                for (int i = 0; i < entriesread; i++)
                {
                    SHARE_INFO_1 shi1 = (SHARE_INFO_1)Marshal.PtrToStructure(currentPtr, typeof(SHARE_INFO_1))!;
                    ShareInfos.Add(shi1);
                    currentPtr += nStructSize;
                }
                NetApiBufferFree(bufPtr);
                return ShareInfos.ToArray();
            }
            else
            {
                throw Win32ApiUtil.ThrowWin32Error(ret, Server);
            }
        }
    }

    public static partial class NtDll
    {
        [DllImport(Libraries.NtDll, ExactSpelling = true)]
        unsafe public static extern int NtQueryInformationFile(
            SafeFileHandle FileHandle,
            out IO_STATUS_BLOCK IoStatusBlock,
            void* FileInformation,
            uint Length,
            uint FileInformationClass);

        public unsafe static FILE_STREAM_INFORMATION[] EnumAlternateStreamInformation(SafeFileHandle FileHandle)
        {
            // This code is original, written from scrach, but written with the following code as a reference.
            // Reference: https://github.com/joliebig/featurehouse_fstmerge_examples/blob/1a99c1788f0eb9f1e5d8c2ced3892d00cd9449ad/Eraser/rev1518-1610/left-trunk-1610/Eraser.Util/NTApi.cs

            IntPtr intPtr = IntPtr.Zero;
            IO_STATUS_BLOCK ioStatusBlock = new IO_STATUS_BLOCK();
            try
            {
                FILE_STREAM_INFORMATION fileStreamInfo = new FILE_STREAM_INFORMATION();
                int fileInfoPtrLength = (Marshal.SizeOf(fileStreamInfo) + 32768) / 2;

                int numError = 0;
                uint errCode = 0;
                do
                {
                    fileInfoPtrLength *= 2;
                    fileInfoPtrLength += 32;

                    numError++;
                    if (numError >= 8)
                    {
                        throw new ApplicationException($"NtQueryInformationFile error: 0x{errCode:X}");
                    }

                    if (intPtr != IntPtr.Zero)
                        Marshal.FreeHGlobal(intPtr);

                    intPtr = Marshal.AllocHGlobal(fileInfoPtrLength);

                    errCode = (uint)NtQueryInformationFile(FileHandle, out ioStatusBlock,
                        (void*)intPtr, (uint)fileInfoPtrLength, (uint)FILE_INFORMATION_CLASS.FileStreamInformation);
                }
                while (errCode != 0 || errCode == 0x80000005);

                List<FILE_STREAM_INFORMATION> ret = new List<FILE_STREAM_INFORMATION>();

                byte* currentPtr = (byte*)intPtr;

                int numTry = 0;

                while (currentPtr != null)
                {
                    numTry++;
                    if (numTry >= 100)
                    {
                        // For just in case of memory corruption
                        break;
                    }

                    byte* p = currentPtr;

                    fileStreamInfo.NextEntryOffset = *(uint*)p;
                    p += sizeof(uint);

                    fileStreamInfo.StreamNameLength = *(uint*)p;
                    p += sizeof(uint);

                    fileStreamInfo.StreamSize = *(long*)p;
                    p += sizeof(long);

                    fileStreamInfo.StreamAllocationSize = *(long*)p;
                    p += sizeof(long);

                    fileStreamInfo.StreamName = Marshal.PtrToStringUni((IntPtr)p, (int)fileStreamInfo.StreamNameLength / 2);

                    ret.Add(fileStreamInfo);

                    if (fileStreamInfo.NextEntryOffset == 0)
                    {
                        break;
                    }

                    currentPtr += fileStreamInfo.NextEntryOffset;
                }

                return ret.ToArray();
            }
            finally
            {
                if (intPtr != IntPtr.Zero)
                    Marshal.FreeHGlobal(intPtr);
            }
        }
    }

    public static partial class Advapi32
    {
        [DllImport(Libraries.Advapi32, CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool OpenProcessToken(SafeProcessHandle ProcessHandle, int DesiredAccess, out SafeTokenHandle TokenHandle);

        [DllImport(Libraries.Advapi32, CharSet = CharSet.Unicode, SetLastError = true, BestFitMapping = false, EntryPoint = "LookupPrivilegeValueW")]
        public static extern bool LookupPrivilegeValue([MarshalAs(UnmanagedType.LPTStr)] string? lpSystemName, [MarshalAs(UnmanagedType.LPTStr)] string lpName, out LUID lpLuid);

        [DllImport(Libraries.Advapi32, SetLastError = true)]
        public unsafe static extern bool AdjustTokenPrivileges(
            SafeTokenHandle TokenHandle,
            bool DisableAllPrivileges,
            TOKEN_PRIVILEGE* NewState,
            uint BufferLength,
            TOKEN_PRIVILEGE* PreviousState,
            uint* ReturnLength);

        [DllImport(Libraries.Advapi32, EntryPoint = "OpenSCManagerW", CharSet = CharSet.Unicode, SetLastError = true)]
        public extern static SafeServiceHandle OpenSCManager(string? machineName, string? databaseName, int access);

        [DllImport(Libraries.Advapi32, EntryPoint = "OpenSCManagerW", CharSet = CharSet.Unicode, SetLastError = true)]
        public extern static IntPtr OpenSCManager2(string machineName, string databaseName, int access);

        [DllImport(Libraries.Advapi32, CharSet = CharSet.Unicode, SetLastError = true)]
        public extern static SafeServiceHandle CreateService(SafeServiceHandle databaseHandle, string serviceName, string displayName, int access, int serviceType,
            int startType, int errorControl, string binaryPath, string? loadOrderGroup, IntPtr pTagId, string? dependencies,
            string? servicesStartName, string? password);

        [DllImport(Libraries.Advapi32, CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool ChangeServiceConfig2(SafeServiceHandle serviceHandle, uint infoLevel, ref SERVICE_DESCRIPTION serviceDesc);

        [DllImport(Libraries.Advapi32, CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool ChangeServiceConfig2(SafeServiceHandle serviceHandle, uint infoLevel, ref SERVICE_DELAYED_AUTOSTART_INFO autoStartInfo);

        [DllImport(Libraries.Advapi32, CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool ChangeServiceConfig2(SafeServiceHandle serviceHandle, uint infoLevel, ref SERVICE_FAILURE_ACTIONS failureActions);

        [DllImport(Libraries.Advapi32, CharSet = CharSet.Unicode, SetLastError = true)]
        public extern static bool CloseServiceHandle(IntPtr handle);

        [DllImport(Libraries.Advapi32, EntryPoint = "OpenServiceW", CharSet = CharSet.Unicode, SetLastError = true)]
        public extern static SafeServiceHandle OpenService(SafeServiceHandle databaseHandle, string serviceName, int access);

        [DllImport(Libraries.Advapi32, CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern unsafe bool QueryServiceStatus(SafeServiceHandle serviceHandle, out SERVICE_STATUS pStatus);

        [DllImport(Libraries.Advapi32, CharSet = CharSet.Unicode, SetLastError = true)]
        public extern unsafe static bool ControlService(SafeServiceHandle serviceHandle, int control, out SERVICE_STATUS pStatus);

        [DllImport(Libraries.Advapi32, CharSet = CharSet.Unicode, SetLastError = true)]
        public unsafe extern static bool SetServiceStatus(SafeServiceHandle serviceStatusHandle, in SERVICE_STATUS status);

        [DllImport(Libraries.Advapi32, CharSet = CharSet.Unicode, SetLastError = true)]
        public extern static bool DeleteService(SafeServiceHandle serviceHandle);

        [DllImport(Libraries.Advapi32, EntryPoint = "StartServiceW", CharSet = CharSet.Unicode, SetLastError = true)]
        public extern static bool StartService(SafeServiceHandle serviceHandle, int argNum, IntPtr argPtrs);
    }

    // Win32 types
    public enum BOOL : int
    {
        FALSE = 0,
        TRUE = 1,
    }

    public static partial class Errors
    {
        public const int ERROR_SUCCESS = 0x0;
        public const int ERROR_INVALID_FUNCTION = 0x1;
        public const int ERROR_FILE_NOT_FOUND = 0x2;
        public const int ERROR_PATH_NOT_FOUND = 0x3;
        public const int ERROR_ACCESS_DENIED = 0x5;
        public const int ERROR_INVALID_HANDLE = 0x6;
        public const int ERROR_NOT_ENOUGH_MEMORY = 0x8;
        public const int ERROR_INVALID_DATA = 0xD;
        public const int ERROR_INVALID_DRIVE = 0xF;
        public const int ERROR_NO_MORE_FILES = 0x12;
        public const int ERROR_NOT_READY = 0x15;
        public const int ERROR_BAD_COMMAND = 0x16;
        public const int ERROR_BAD_LENGTH = 0x18;
        public const int ERROR_SHARING_VIOLATION = 0x20;
        public const int ERROR_LOCK_VIOLATION = 0x21;
        public const int ERROR_HANDLE_EOF = 0x26;
        public const int ERROR_BAD_NETPATH = 0x35;
        public const int ERROR_NETWORK_ACCESS_DENIED = 0x41;
        public const int ERROR_BAD_NET_NAME = 0x43;
        public const int ERROR_FILE_EXISTS = 0x50;
        public const int ERROR_INVALID_PARAMETER = 0x57;
        public const int ERROR_BROKEN_PIPE = 0x6D;
        public const int ERROR_SEM_TIMEOUT = 0x79;
        public const int ERROR_CALL_NOT_IMPLEMENTED = 0x78;
        public const int ERROR_INSUFFICIENT_BUFFER = 0x7A;
        public const int ERROR_INVALID_NAME = 0x7B;
        public const int ERROR_NEGATIVE_SEEK = 0x83;
        public const int ERROR_DIR_NOT_EMPTY = 0x91;
        public const int ERROR_BAD_PATHNAME = 0xA1;
        public const int ERROR_LOCK_FAILED = 0xA7;
        public const int ERROR_BUSY = 0xAA;
        public const int ERROR_ALREADY_EXISTS = 0xB7;
        public const int ERROR_BAD_EXE_FORMAT = 0xC1;
        public const int ERROR_ENVVAR_NOT_FOUND = 0xCB;
        public const int ERROR_FILENAME_EXCED_RANGE = 0xCE;
        public const int ERROR_EXE_MACHINE_TYPE_MISMATCH = 0xD8;
        public const int ERROR_PIPE_BUSY = 0xE7;
        public const int ERROR_NO_DATA = 0xE8;
        public const int ERROR_PIPE_NOT_CONNECTED = 0xE9;
        public const int ERROR_MORE_DATA = 0xEA;
        public const int ERROR_NO_MORE_ITEMS = 0x103;
        public const int ERROR_DIRECTORY = 0x10B;
        public const int ERROR_PARTIAL_COPY = 0x12B;
        public const int ERROR_ARITHMETIC_OVERFLOW = 0x216;
        public const int ERROR_PIPE_CONNECTED = 0x217;
        public const int ERROR_PIPE_LISTENING = 0x218;
        public const int ERROR_OPERATION_ABORTED = 0x3E3;
        public const int ERROR_IO_INCOMPLETE = 0x3E4;
        public const int ERROR_IO_PENDING = 0x3E5;
        public const int ERROR_NO_TOKEN = 0x3f0;
        public const int ERROR_SERVICE_DOES_NOT_EXIST = 0x424;
        public const int ERROR_DLL_INIT_FAILED = 0x45A;
        public const int ERROR_COUNTER_TIMEOUT = 0x461;
        public const int ERROR_NO_ASSOCIATION = 0x483;
        public const int ERROR_DDE_FAIL = 0x484;
        public const int ERROR_DLL_NOT_FOUND = 0x485;
        public const int ERROR_NOT_FOUND = 0x490;
        public const int ERROR_NETWORK_UNREACHABLE = 0x4CF;
        public const int ERROR_NON_ACCOUNT_SID = 0x4E9;
        public const int ERROR_NOT_ALL_ASSIGNED = 0x514;
        public const int ERROR_UNKNOWN_REVISION = 0x519;
        public const int ERROR_INVALID_OWNER = 0x51B;
        public const int ERROR_INVALID_PRIMARY_GROUP = 0x51C;
        public const int ERROR_NO_SUCH_PRIVILEGE = 0x521;
        public const int ERROR_PRIVILEGE_NOT_HELD = 0x522;
        public const int ERROR_INVALID_ACL = 0x538;
        public const int ERROR_INVALID_SECURITY_DESCR = 0x53A;
        public const int ERROR_INVALID_SID = 0x539;
        public const int ERROR_BAD_IMPERSONATION_LEVEL = 0x542;
        public const int ERROR_CANT_OPEN_ANONYMOUS = 0x543;
        public const int ERROR_NO_SECURITY_ON_OBJECT = 0x546;
        public const int ERROR_CLASS_ALREADY_EXISTS = 0x582;
        public const int ERROR_EVENTLOG_FILE_CHANGED = 0x5DF;
        public const int ERROR_TRUSTED_RELATIONSHIP_FAILURE = 0x6FD;
        public const int ERROR_RESOURCE_LANG_NOT_FOUND = 0x717;
        public const int EFail = unchecked((int)0x80004005);
        public const int E_FILENOTFOUND = unchecked((int)0x80070002);
    }

    public static partial class Kernel32
    {
        [Flags]
        public enum FinalPathFlags : uint
        {
            VOLUME_NAME_DOS = 0x0,
            FILE_NAME_NORMALIZED = 0x0,
            VOLUME_NAME_GUID = 0x1,
            VOLUME_NAME_NT = 0x2,
            VOLUME_NAME_NONE = 0x4,
            FILE_NAME_OPENED = 0x8
        }

        // Some constants from http://www.pinvoke.net/default.aspx/kernel32/DeviceIoControl.html
        [Flags]
        public enum EMethod : uint
        {
            Buffered = 0,
            InDirect = 1,
            OutDirect = 2,
            Neither = 3
        }

        [Flags]
        public enum EFileDevice : uint
        {
            Beep = 0x00000001,
            CDRom = 0x00000002,
            CDRomFileSytem = 0x00000003,
            Controller = 0x00000004,
            Datalink = 0x00000005,
            Dfs = 0x00000006,
            Disk = 0x00000007,
            DiskFileSystem = 0x00000008,
            FileSystem = 0x00000009,
            InPortPort = 0x0000000a,
            Keyboard = 0x0000000b,
            Mailslot = 0x0000000c,
            MidiIn = 0x0000000d,
            MidiOut = 0x0000000e,
            Mouse = 0x0000000f,
            MultiUncProvider = 0x00000010,
            NamedPipe = 0x00000011,
            Network = 0x00000012,
            NetworkBrowser = 0x00000013,
            NetworkFileSystem = 0x00000014,
            Null = 0x00000015,
            ParallelPort = 0x00000016,
            PhysicalNetcard = 0x00000017,
            Printer = 0x00000018,
            Scanner = 0x00000019,
            SerialMousePort = 0x0000001a,
            SerialPort = 0x0000001b,
            Screen = 0x0000001c,
            Sound = 0x0000001d,
            Streams = 0x0000001e,
            Tape = 0x0000001f,
            TapeFileSystem = 0x00000020,
            Transport = 0x00000021,
            Unknown = 0x00000022,
            Video = 0x00000023,
            VirtualDisk = 0x00000024,
            WaveIn = 0x00000025,
            WaveOut = 0x00000026,
            Port8042 = 0x00000027,
            NetworkRedirector = 0x00000028,
            Battery = 0x00000029,
            BusExtender = 0x0000002a,
            Modem = 0x0000002b,
            Vdm = 0x0000002c,
            MassStorage = 0x0000002d,
            Smb = 0x0000002e,
            Ks = 0x0000002f,
            Changer = 0x00000030,
            Smartcard = 0x00000031,
            Acpi = 0x00000032,
            Dvd = 0x00000033,
            FullscreenVideo = 0x00000034,
            DfsFileSystem = 0x00000035,
            DfsVolume = 0x00000036,
            Serenum = 0x00000037,
            Termsrv = 0x00000038,
            Ksec = 0x00000039,
            // From Windows Driver Kit 7
            Fips = 0x0000003A,
            Infiniband = 0x0000003B,
            Vmbus = 0x0000003E,
            CryptProvider = 0x0000003F,
            Wpd = 0x00000040,
            Bluetooth = 0x00000041,
            MtComposite = 0x00000042,
            MtTransport = 0x00000043,
            Biometric = 0x00000044,
            Pmi = 0x00000045
        }

        [Flags]
        public enum EIOControlCode : uint
        {
            // STORAGE
            StorageCheckVerify = (EFileDevice.MassStorage << 16) | (0x0200 << 2) | EMethod.Buffered | (FileAccess.Read << 14),
            StorageCheckVerify2 = (EFileDevice.MassStorage << 16) | (0x0200 << 2) | EMethod.Buffered | (0 << 14), // FileAccess.Any
            StorageMediaRemoval = (EFileDevice.MassStorage << 16) | (0x0201 << 2) | EMethod.Buffered | (FileAccess.Read << 14),
            StorageEjectMedia = (EFileDevice.MassStorage << 16) | (0x0202 << 2) | EMethod.Buffered | (FileAccess.Read << 14),
            StorageLoadMedia = (EFileDevice.MassStorage << 16) | (0x0203 << 2) | EMethod.Buffered | (FileAccess.Read << 14),
            StorageLoadMedia2 = (EFileDevice.MassStorage << 16) | (0x0203 << 2) | EMethod.Buffered | (0 << 14),
            StorageReserve = (EFileDevice.MassStorage << 16) | (0x0204 << 2) | EMethod.Buffered | (FileAccess.Read << 14),
            StorageRelease = (EFileDevice.MassStorage << 16) | (0x0205 << 2) | EMethod.Buffered | (FileAccess.Read << 14),
            StorageFindNewDevices = (EFileDevice.MassStorage << 16) | (0x0206 << 2) | EMethod.Buffered | (FileAccess.Read << 14),
            StorageEjectionControl = (EFileDevice.MassStorage << 16) | (0x0250 << 2) | EMethod.Buffered | (0 << 14),
            StorageMcnControl = (EFileDevice.MassStorage << 16) | (0x0251 << 2) | EMethod.Buffered | (0 << 14),
            StorageGetMediaTypes = (EFileDevice.MassStorage << 16) | (0x0300 << 2) | EMethod.Buffered | (0 << 14),
            StorageGetMediaTypesEx = (EFileDevice.MassStorage << 16) | (0x0301 << 2) | EMethod.Buffered | (0 << 14),
            StorageResetBus = (EFileDevice.MassStorage << 16) | (0x0400 << 2) | EMethod.Buffered | (FileAccess.Read << 14),
            StorageResetDevice = (EFileDevice.MassStorage << 16) | (0x0401 << 2) | EMethod.Buffered | (FileAccess.Read << 14),
            StorageGetDeviceNumber = (EFileDevice.MassStorage << 16) | (0x0420 << 2) | EMethod.Buffered | (0 << 14),
            StoragePredictFailure = (EFileDevice.MassStorage << 16) | (0x0440 << 2) | EMethod.Buffered | (0 << 14),
            StorageObsoleteResetBus = (EFileDevice.MassStorage << 16) | (0x0400 << 2) | EMethod.Buffered | (FileAccess.ReadWrite << 14),
            StorageObsoleteResetDevice = (EFileDevice.MassStorage << 16) | (0x0401 << 2) | EMethod.Buffered | (FileAccess.ReadWrite << 14),
            StorageQueryProperty = (EFileDevice.MassStorage << 16) | (0x0500 << 2) | EMethod.Buffered | (0 << 14),
            // DISK
            DiskGetDriveGeometry = (EFileDevice.Disk << 16) | (0x0000 << 2) | EMethod.Buffered | (0 << 14),
            DiskGetDriveGeometryEx = (EFileDevice.Disk << 16) | (0x0028 << 2) | EMethod.Buffered | (0 << 14),
            DiskGetPartitionInfo = (EFileDevice.Disk << 16) | (0x0001 << 2) | EMethod.Buffered | (FileAccess.Read << 14),
            DiskGetPartitionInfoEx = (EFileDevice.Disk << 16) | (0x0012 << 2) | EMethod.Buffered | (0 << 14),
            DiskSetPartitionInfo = (EFileDevice.Disk << 16) | (0x0002 << 2) | EMethod.Buffered | (FileAccess.ReadWrite << 14),
            DiskGetDriveLayout = (EFileDevice.Disk << 16) | (0x0003 << 2) | EMethod.Buffered | (FileAccess.Read << 14),
            DiskSetDriveLayout = (EFileDevice.Disk << 16) | (0x0004 << 2) | EMethod.Buffered | (FileAccess.ReadWrite << 14),
            DiskVerify = (EFileDevice.Disk << 16) | (0x0005 << 2) | EMethod.Buffered | (0 << 14),
            DiskFormatTracks = (EFileDevice.Disk << 16) | (0x0006 << 2) | EMethod.Buffered | (FileAccess.ReadWrite << 14),
            DiskReassignBlocks = (EFileDevice.Disk << 16) | (0x0007 << 2) | EMethod.Buffered | (FileAccess.ReadWrite << 14),
            DiskPerformance = (EFileDevice.Disk << 16) | (0x0008 << 2) | EMethod.Buffered | (0 << 14),
            DiskIsWritable = (EFileDevice.Disk << 16) | (0x0009 << 2) | EMethod.Buffered | (0 << 14),
            DiskLogging = (EFileDevice.Disk << 16) | (0x000a << 2) | EMethod.Buffered | (0 << 14),
            DiskFormatTracksEx = (EFileDevice.Disk << 16) | (0x000b << 2) | EMethod.Buffered | (FileAccess.ReadWrite << 14),
            DiskHistogramStructure = (EFileDevice.Disk << 16) | (0x000c << 2) | EMethod.Buffered | (0 << 14),
            DiskHistogramData = (EFileDevice.Disk << 16) | (0x000d << 2) | EMethod.Buffered | (0 << 14),
            DiskHistogramReset = (EFileDevice.Disk << 16) | (0x000e << 2) | EMethod.Buffered | (0 << 14),
            DiskRequestStructure = (EFileDevice.Disk << 16) | (0x000f << 2) | EMethod.Buffered | (0 << 14),
            DiskRequestData = (EFileDevice.Disk << 16) | (0x0010 << 2) | EMethod.Buffered | (0 << 14),
            DiskControllerNumber = (EFileDevice.Disk << 16) | (0x0011 << 2) | EMethod.Buffered | (0 << 14),
            DiskSmartGetVersion = (EFileDevice.Disk << 16) | (0x0020 << 2) | EMethod.Buffered | (FileAccess.Read << 14),
            DiskSmartSendDriveCommand = (EFileDevice.Disk << 16) | (0x0021 << 2) | EMethod.Buffered | (FileAccess.ReadWrite << 14),
            DiskSmartRcvDriveData = (EFileDevice.Disk << 16) | (0x0022 << 2) | EMethod.Buffered | (FileAccess.ReadWrite << 14),
            DiskUpdateDriveSize = (EFileDevice.Disk << 16) | (0x0032 << 2) | EMethod.Buffered | (FileAccess.ReadWrite << 14),
            DiskGrowPartition = (EFileDevice.Disk << 16) | (0x0034 << 2) | EMethod.Buffered | (FileAccess.ReadWrite << 14),
            DiskGetCacheInformation = (EFileDevice.Disk << 16) | (0x0035 << 2) | EMethod.Buffered | (FileAccess.Read << 14),
            DiskSetCacheInformation = (EFileDevice.Disk << 16) | (0x0036 << 2) | EMethod.Buffered | (FileAccess.ReadWrite << 14),
            DiskDeleteDriveLayout = (EFileDevice.Disk << 16) | (0x0040 << 2) | EMethod.Buffered | (FileAccess.ReadWrite << 14),
            DiskFormatDrive = (EFileDevice.Disk << 16) | (0x00f3 << 2) | EMethod.Buffered | (FileAccess.ReadWrite << 14),
            DiskSenseDevice = (EFileDevice.Disk << 16) | (0x00f8 << 2) | EMethod.Buffered | (0 << 14),
            DiskCheckVerify = (EFileDevice.Disk << 16) | (0x0200 << 2) | EMethod.Buffered | (FileAccess.Read << 14),
            DiskMediaRemoval = (EFileDevice.Disk << 16) | (0x0201 << 2) | EMethod.Buffered | (FileAccess.Read << 14),
            DiskEjectMedia = (EFileDevice.Disk << 16) | (0x0202 << 2) | EMethod.Buffered | (FileAccess.Read << 14),
            DiskLoadMedia = (EFileDevice.Disk << 16) | (0x0203 << 2) | EMethod.Buffered | (FileAccess.Read << 14),
            DiskReserve = (EFileDevice.Disk << 16) | (0x0204 << 2) | EMethod.Buffered | (FileAccess.Read << 14),
            DiskRelease = (EFileDevice.Disk << 16) | (0x0205 << 2) | EMethod.Buffered | (FileAccess.Read << 14),
            DiskFindNewDevices = (EFileDevice.Disk << 16) | (0x0206 << 2) | EMethod.Buffered | (FileAccess.Read << 14),
            DiskGetMediaTypes = (EFileDevice.Disk << 16) | (0x0300 << 2) | EMethod.Buffered | (0 << 14),
            DiskSetPartitionInfoEx = (EFileDevice.Disk << 16) | (0x0013 << 2) | EMethod.Buffered | (FileAccess.ReadWrite << 14),
            DiskGetDriveLayoutEx = (EFileDevice.Disk << 16) | (0x0014 << 2) | EMethod.Buffered | (0 << 14),
            DiskSetDriveLayoutEx = (EFileDevice.Disk << 16) | (0x0015 << 2) | EMethod.Buffered | (FileAccess.ReadWrite << 14),
            DiskCreateDisk = (EFileDevice.Disk << 16) | (0x0016 << 2) | EMethod.Buffered | (FileAccess.ReadWrite << 14),
            DiskGetLengthInfo = (EFileDevice.Disk << 16) | (0x0017 << 2) | EMethod.Buffered | (FileAccess.Read << 14),
            // CHANGER
            ChangerGetParameters = (EFileDevice.Changer << 16) | (0x0000 << 2) | EMethod.Buffered | (FileAccess.Read << 14),
            ChangerGetStatus = (EFileDevice.Changer << 16) | (0x0001 << 2) | EMethod.Buffered | (FileAccess.Read << 14),
            ChangerGetProductData = (EFileDevice.Changer << 16) | (0x0002 << 2) | EMethod.Buffered | (FileAccess.Read << 14),
            ChangerSetAccess = (EFileDevice.Changer << 16) | (0x0004 << 2) | EMethod.Buffered | (FileAccess.ReadWrite << 14),
            ChangerGetElementStatus = (EFileDevice.Changer << 16) | (0x0005 << 2) | EMethod.Buffered | (FileAccess.ReadWrite << 14),
            ChangerInitializeElementStatus = (EFileDevice.Changer << 16) | (0x0006 << 2) | EMethod.Buffered | (FileAccess.Read << 14),
            ChangerSetPosition = (EFileDevice.Changer << 16) | (0x0007 << 2) | EMethod.Buffered | (FileAccess.Read << 14),
            ChangerExchangeMedium = (EFileDevice.Changer << 16) | (0x0008 << 2) | EMethod.Buffered | (FileAccess.Read << 14),
            ChangerMoveMedium = (EFileDevice.Changer << 16) | (0x0009 << 2) | EMethod.Buffered | (FileAccess.Read << 14),
            ChangerReinitializeTarget = (EFileDevice.Changer << 16) | (0x000A << 2) | EMethod.Buffered | (FileAccess.Read << 14),
            ChangerQueryVolumeTags = (EFileDevice.Changer << 16) | (0x000B << 2) | EMethod.Buffered | (FileAccess.ReadWrite << 14),
            // FILESYSTEM
            FsctlRequestOplockLevel1 = (EFileDevice.FileSystem << 16) | (0 << 2) | EMethod.Buffered | (0 << 14),
            FsctlRequestOplockLevel2 = (EFileDevice.FileSystem << 16) | (1 << 2) | EMethod.Buffered | (0 << 14),
            FsctlRequestBatchOplock = (EFileDevice.FileSystem << 16) | (2 << 2) | EMethod.Buffered | (0 << 14),
            FsctlOplockBreakAcknowledge = (EFileDevice.FileSystem << 16) | (3 << 2) | EMethod.Buffered | (0 << 14),
            FsctlOpBatchAckClosePending = (EFileDevice.FileSystem << 16) | (4 << 2) | EMethod.Buffered | (0 << 14),
            FsctlOplockBreakNotify = (EFileDevice.FileSystem << 16) | (5 << 2) | EMethod.Buffered | (0 << 14),
            FsctlLockVolume = (EFileDevice.FileSystem << 16) | (6 << 2) | EMethod.Buffered | (0 << 14),
            FsctlUnlockVolume = (EFileDevice.FileSystem << 16) | (7 << 2) | EMethod.Buffered | (0 << 14),
            FsctlDismountVolume = (EFileDevice.FileSystem << 16) | (8 << 2) | EMethod.Buffered | (0 << 14),
            FsctlIsVolumeMounted = (EFileDevice.FileSystem << 16) | (10 << 2) | EMethod.Buffered | (0 << 14),
            FsctlIsPathnameValid = (EFileDevice.FileSystem << 16) | (11 << 2) | EMethod.Buffered | (0 << 14),
            FsctlMarkVolumeDirty = (EFileDevice.FileSystem << 16) | (12 << 2) | EMethod.Buffered | (0 << 14),
            FsctlQueryRetrievalPointers = (EFileDevice.FileSystem << 16) | (14 << 2) | EMethod.Neither | (0 << 14),
            FsctlGetCompression = (EFileDevice.FileSystem << 16) | (15 << 2) | EMethod.Buffered | (0 << 14),
            FsctlSetCompression = (EFileDevice.FileSystem << 16) | (16 << 2) | EMethod.Buffered | (FileAccess.ReadWrite << 14),
            FsctlMarkAsSystemHive = (EFileDevice.FileSystem << 16) | (19 << 2) | EMethod.Neither | (0 << 14),
            FsctlOplockBreakAckNo2 = (EFileDevice.FileSystem << 16) | (20 << 2) | EMethod.Buffered | (0 << 14),
            FsctlInvalidateVolumes = (EFileDevice.FileSystem << 16) | (21 << 2) | EMethod.Buffered | (0 << 14),
            FsctlQueryFatBpb = (EFileDevice.FileSystem << 16) | (22 << 2) | EMethod.Buffered | (0 << 14),
            FsctlRequestFilterOplock = (EFileDevice.FileSystem << 16) | (23 << 2) | EMethod.Buffered | (0 << 14),
            FsctlFileSystemGetStatistics = (EFileDevice.FileSystem << 16) | (24 << 2) | EMethod.Buffered | (0 << 14),
            FsctlGetNtfsVolumeData = (EFileDevice.FileSystem << 16) | (25 << 2) | EMethod.Buffered | (0 << 14),
            FsctlGetNtfsFileRecord = (EFileDevice.FileSystem << 16) | (26 << 2) | EMethod.Buffered | (0 << 14),
            FsctlGetVolumeBitmap = (EFileDevice.FileSystem << 16) | (27 << 2) | EMethod.Neither | (0 << 14),
            FsctlGetRetrievalPointers = (EFileDevice.FileSystem << 16) | (28 << 2) | EMethod.Neither | (0 << 14),
            FsctlMoveFile = (EFileDevice.FileSystem << 16) | (29 << 2) | EMethod.Buffered | (0 << 14),
            FsctlIsVolumeDirty = (EFileDevice.FileSystem << 16) | (30 << 2) | EMethod.Buffered | (0 << 14),
            FsctlGetHfsInformation = (EFileDevice.FileSystem << 16) | (31 << 2) | EMethod.Buffered | (0 << 14),
            FsctlAllowExtendedDasdIo = (EFileDevice.FileSystem << 16) | (32 << 2) | EMethod.Neither | (0 << 14),
            FsctlReadPropertyData = (EFileDevice.FileSystem << 16) | (33 << 2) | EMethod.Neither | (0 << 14),
            FsctlWritePropertyData = (EFileDevice.FileSystem << 16) | (34 << 2) | EMethod.Neither | (0 << 14),
            FsctlFindFilesBySid = (EFileDevice.FileSystem << 16) | (35 << 2) | EMethod.Neither | (0 << 14),
            FsctlDumpPropertyData = (EFileDevice.FileSystem << 16) | (37 << 2) | EMethod.Neither | (0 << 14),
            FsctlSetObjectId = (EFileDevice.FileSystem << 16) | (38 << 2) | EMethod.Buffered | (0 << 14),
            FsctlGetObjectId = (EFileDevice.FileSystem << 16) | (39 << 2) | EMethod.Buffered | (0 << 14),
            FsctlDeleteObjectId = (EFileDevice.FileSystem << 16) | (40 << 2) | EMethod.Buffered | (0 << 14),
            FsctlSetReparsePoint = (EFileDevice.FileSystem << 16) | (41 << 2) | EMethod.Buffered | (0 << 14),
            FsctlGetReparsePoint = (EFileDevice.FileSystem << 16) | (42 << 2) | EMethod.Buffered | (0 << 14),
            FsctlDeleteReparsePoint = (EFileDevice.FileSystem << 16) | (43 << 2) | EMethod.Buffered | (0 << 14),
            FsctlEnumUsnData = (EFileDevice.FileSystem << 16) | (44 << 2) | EMethod.Neither | (0 << 14),
            FsctlSecurityIdCheck = (EFileDevice.FileSystem << 16) | (45 << 2) | EMethod.Neither | (FileAccess.Read << 14),
            FsctlReadUsnJournal = (EFileDevice.FileSystem << 16) | (46 << 2) | EMethod.Neither | (0 << 14),
            FsctlSetObjectIdExtended = (EFileDevice.FileSystem << 16) | (47 << 2) | EMethod.Buffered | (0 << 14),
            FsctlCreateOrGetObjectId = (EFileDevice.FileSystem << 16) | (48 << 2) | EMethod.Buffered | (0 << 14),
            FsctlSetSparse = (EFileDevice.FileSystem << 16) | (49 << 2) | EMethod.Buffered | (0 << 14),
            FsctlSetZeroData = (EFileDevice.FileSystem << 16) | (50 << 2) | EMethod.Buffered | (FileAccess.Write << 14),
            FsctlQueryAllocatedRanges = (EFileDevice.FileSystem << 16) | (51 << 2) | EMethod.Neither | (FileAccess.Read << 14),
            FsctlEnableUpgrade = (EFileDevice.FileSystem << 16) | (52 << 2) | EMethod.Buffered | (FileAccess.Write << 14),
            FsctlSetEncryption = (EFileDevice.FileSystem << 16) | (53 << 2) | EMethod.Neither | (0 << 14),
            FsctlEncryptionFsctlIo = (EFileDevice.FileSystem << 16) | (54 << 2) | EMethod.Neither | (0 << 14),
            FsctlWriteRawEncrypted = (EFileDevice.FileSystem << 16) | (55 << 2) | EMethod.Neither | (0 << 14),
            FsctlReadRawEncrypted = (EFileDevice.FileSystem << 16) | (56 << 2) | EMethod.Neither | (0 << 14),
            FsctlCreateUsnJournal = (EFileDevice.FileSystem << 16) | (57 << 2) | EMethod.Neither | (0 << 14),
            FsctlReadFileUsnData = (EFileDevice.FileSystem << 16) | (58 << 2) | EMethod.Neither | (0 << 14),
            FsctlWriteUsnCloseRecord = (EFileDevice.FileSystem << 16) | (59 << 2) | EMethod.Neither | (0 << 14),
            FsctlExtendVolume = (EFileDevice.FileSystem << 16) | (60 << 2) | EMethod.Buffered | (0 << 14),
            FsctlQueryUsnJournal = (EFileDevice.FileSystem << 16) | (61 << 2) | EMethod.Buffered | (0 << 14),
            FsctlDeleteUsnJournal = (EFileDevice.FileSystem << 16) | (62 << 2) | EMethod.Buffered | (0 << 14),
            FsctlMarkHandle = (EFileDevice.FileSystem << 16) | (63 << 2) | EMethod.Buffered | (0 << 14),
            FsctlSisCopyFile = (EFileDevice.FileSystem << 16) | (64 << 2) | EMethod.Buffered | (0 << 14),
            FsctlSisLinkFiles = (EFileDevice.FileSystem << 16) | (65 << 2) | EMethod.Buffered | (FileAccess.ReadWrite << 14),
            FsctlHsmMsg = (EFileDevice.FileSystem << 16) | (66 << 2) | EMethod.Buffered | (FileAccess.ReadWrite << 14),
            FsctlNssControl = (EFileDevice.FileSystem << 16) | (67 << 2) | EMethod.Buffered | (FileAccess.Write << 14),
            FsctlHsmData = (EFileDevice.FileSystem << 16) | (68 << 2) | EMethod.Neither | (FileAccess.ReadWrite << 14),
            FsctlRecallFile = (EFileDevice.FileSystem << 16) | (69 << 2) | EMethod.Neither | (0 << 14),
            FsctlNssRcontrol = (EFileDevice.FileSystem << 16) | (70 << 2) | EMethod.Buffered | (FileAccess.Read << 14),
            // VIDEO
            VideoQuerySupportedBrightness = (EFileDevice.Video << 16) | (0x0125 << 2) | EMethod.Buffered | (0 << 14),
            VideoQueryDisplayBrightness = (EFileDevice.Video << 16) | (0x0126 << 2) | EMethod.Buffered | (0 << 14),
            VideoSetDisplayBrightness = (EFileDevice.Video << 16) | (0x0127 << 2) | EMethod.Buffered | (0 << 14)
        }
        public const uint SEM_FAILCRITICALERRORS = 1;

        public const int FSCTL_SET_COMPRESSION = 0x9C040;
        public const short COMPRESSION_FORMAT_NONE = 0;
        public const short COMPRESSION_FORMAT_DEFAULT = 1;

        public static partial class GenericOperations
        {
            public const int GENERIC_READ = unchecked((int)0x80000000);
            public const int GENERIC_WRITE = 0x40000000;
        }

        public static partial class HandleOptions
        {
            public const int DUPLICATE_SAME_ACCESS = 2;
            public const int STILL_ACTIVE = 0x00000103;
            public const int TOKEN_ADJUST_PRIVILEGES = 0x20;
        }

        public static partial class IOReparseOptions
        {
            public const uint IO_REPARSE_TAG_FILE_PLACEHOLDER = 0x80000015;
            public const uint IO_REPARSE_TAG_MOUNT_POINT = 0xA0000003;
        }

        public static partial class FileOperations
        {
            public const int OPEN_EXISTING = 3;
            public const int COPY_FILE_FAIL_IF_EXISTS = 0x00000001;

            public const int FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
            public const int FILE_FLAG_FIRST_PIPE_INSTANCE = 0x00080000;
            public const int FILE_FLAG_OVERLAPPED = 0x40000000;

            public const int FILE_LIST_DIRECTORY = 0x0001;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SECURITY_ATTRIBUTES
        {
            public uint nLength;
            public IntPtr lpSecurityDescriptor;
            public BOOL bInheritHandle;
        }

        public static partial class SecurityOptions
        {
            public const int SECURITY_SQOS_PRESENT = 0x00100000;
            public const int SECURITY_ANONYMOUS = 0 << 16;
            public const int SECURITY_IDENTIFICATION = 1 << 16;
            public const int SECURITY_IMPERSONATION = 2 << 16;
            public const int SECURITY_DELEGATION = 3 << 16;
        }

        public struct FILE_BASIC_INFO
        {
            public long CreationTime;
            public long LastAccessTime;
            public long LastWriteTime;
            public long ChangeTime;
            public uint FileAttributes;
        }

        public enum FILE_INFO_BY_HANDLE_CLASS : uint
        {
            FileBasicInfo = 0x0u,
            FileStandardInfo = 0x1u,
            FileNameInfo = 0x2u,
            FileRenameInfo = 0x3u,
            FileDispositionInfo = 0x4u,
            FileAllocationInfo = 0x5u,
            FileEndOfFileInfo = 0x6u,
            FileStreamInfo = 0x7u,
            FileCompressionInfo = 0x8u,
            FileAttributeTagInfo = 0x9u,
            FileIdBothDirectoryInfo = 0xAu,
            FileIdBothDirectoryRestartInfo = 0xBu,
            FileIoPriorityHintInfo = 0xCu,
            FileRemoteProtocolInfo = 0xDu,
            FileFullDirectoryInfo = 0xEu,
            FileFullDirectoryRestartInfo = 0xFu,
            FileStorageInfo = 0x10u,
            FileAlignmentInfo = 0x11u,
            FileIdInfo = 0x12u,
            FileIdExtdDirectoryInfo = 0x13u,
            FileIdExtdDirectoryRestartInfo = 0x14u,
            MaximumFileInfoByHandleClass = 0x15u,
        }
        public enum GET_FILEEX_INFO_LEVELS : uint
        {
            GetFileExInfoStandard = 0x0u,
            GetFileExMaxInfoLevel = 0x1u,
        }

        public struct WIN32_FILE_ATTRIBUTE_DATA
        {
            public int dwFileAttributes;
            public uint ftCreationTimeLow;
            public uint ftCreationTimeHigh;
            public uint ftLastAccessTimeLow;
            public uint ftLastAccessTimeHigh;
            public uint ftLastWriteTimeLow;
            public uint ftLastWriteTimeHigh;
            public uint fileSizeHigh;
            public uint fileSizeLow;

            public void PopulateFrom(ref WIN32_FIND_DATA findData)
            {
                // Copy the information to data
                dwFileAttributes = (int)findData.dwFileAttributes;
                ftCreationTimeLow = findData.ftCreationTime.dwLowDateTime;
                ftCreationTimeHigh = findData.ftCreationTime.dwHighDateTime;
                ftLastAccessTimeLow = findData.ftLastAccessTime.dwLowDateTime;
                ftLastAccessTimeHigh = findData.ftLastAccessTime.dwHighDateTime;
                ftLastWriteTimeLow = findData.ftLastWriteTime.dwLowDateTime;
                ftLastWriteTimeHigh = findData.ftLastWriteTime.dwHighDateTime;
                fileSizeHigh = findData.nFileSizeHigh;
                fileSizeLow = findData.nFileSizeLow;
            }
        }

        public enum NativeDiskType : uint
        {
            Unknown,
            F5_1Pt2_512,
            F3_1Pt44_512,
            F3_2Pt88_512,
            F3_20Pt8_512,
            F3_720_512,
            F5_360_512,
            F5_320_512,
            F5_320_1024,
            F5_180_512,
            F5_160_512,
            RemovableMedia,
            FixedMedia,
            F3_120M_512,
            F3_640_512,
            F5_640_512,
            F5_720_512,
            F3_1Pt2_512,
            F3_1Pt23_1024,
            F5_1Pt23_1024,
            F3_128Mb_512,
            F3_230Mb_512,
            F8_256_128,
            F3_200Mb_512,
            F3_240M_512,
            F3_32M_512
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DISK_GEOMETRY
        {
            public long Cylinders;
            public NativeDiskType MediaType;
            public int TracksPerCylinder;
            public int SectorsPerTrack;
            public int BytesPerSector;

            public long DiskSize
            {
                get
                {
                    return Cylinders * (long)TracksPerCylinder * (long)SectorsPerTrack * (long)BytesPerSector;
                }
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct FILE_ZERO_DATA_INFORMATION
        {
            public FILE_ZERO_DATA_INFORMATION(long offset, long count)
            {
                checked
                {
                    FileOffset = offset;
                    BeyondFinalZero = offset + count;
                }
            }

            public long FileOffset;
            public long BeyondFinalZero;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        [BestFitMapping(false)]
        public unsafe struct WIN32_FIND_DATA
        {
            public uint dwFileAttributes;
            public FILE_TIME ftCreationTime;
            public FILE_TIME ftLastAccessTime;
            public FILE_TIME ftLastWriteTime;
            public uint nFileSizeHigh;
            public uint nFileSizeLow;
            public uint dwReserved0;
            public uint dwReserved1;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string cFileName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)]
            public string cAlternateFileName;
        }

        public enum STREAM_INFO_LEVELS
        {
            FindStreamInfoStandard,
            FindStreamInfoMaxInfoLevel
        }

        [StructLayout(LayoutKind.Explicit)]
        public unsafe struct LARGE_INTEGER
        {
            [FieldOffset(0)]
            public int LowPart;

            [FieldOffset(4)]
            public int HighPart;

            [FieldOffset(0)]
            public long QuadPart;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct WIN32_FIND_STREAM_DATA
        {
            public LARGE_INTEGER StreamSize;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260 + 36)]
            public string cStreamName;
        }

        public struct FILE_TIME
        {
            public uint dwLowDateTime;
            public uint dwHighDateTime;

            public FILE_TIME(long fileTime)
            {
                dwLowDateTime = (uint)fileTime;
                dwHighDateTime = (uint)(fileTime >> 32);
            }

            public long ToTicks()
            {
                return ((long)dwHighDateTime << 32) + dwLowDateTime;
            }
        }

        public enum FINDEX_INFO_LEVELS : uint
        {
            FindExInfoStandard = 0x0u,
            FindExInfoBasic = 0x1u,
            FindExInfoMaxInfoLevel = 0x2u,
        }

        public enum FINDEX_SEARCH_OPS : uint
        {
            FindExSearchNameMatch = 0x0u,
            FindExSearchLimitToDirectories = 0x1u,
            FindExSearchLimitToDevices = 0x2u,
            FindExSearchMaxSearchOp = 0x3u,
        }
    }

    public static partial class NtDll
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct IO_STATUS_BLOCK
        {
            IO_STATUS Status;
            IntPtr Information;
        }

        // This isn't an actual Windows type, we have to separate it out as the size of IntPtr varies by architecture
        // and we can't specify the size at compile time to offset the Information pointer in the status block.
        [StructLayout(LayoutKind.Explicit)]
        public struct IO_STATUS
        {
            [FieldOffset(0)]
            int Status;

            [FieldOffset(0)]
            IntPtr Pointer;
        }

        public const uint FileModeInformation = 16;
        public const uint FILE_SYNCHRONOUS_IO_ALERT = 0x00000010;
        public const uint FILE_SYNCHRONOUS_IO_NONALERT = 0x00000020;

        public const int STATUS_INVALID_HANDLE = unchecked((int)0xC0000008);

        public struct FILE_STREAM_INFORMATION
        {
            public uint NextEntryOffset;
            public uint StreamNameLength;
            public long StreamSize;
            public long StreamAllocationSize;
            public string StreamName;
        }

        // https://msdn.microsoft.com/en-us/library/windows/hardware/ff728840.aspx
        public enum FILE_INFORMATION_CLASS : uint
        {
            FileDirectoryInformation = 1,
            FileFullDirectoryInformation = 2,
            FileBothDirectoryInformation = 3,
            FileBasicInformation = 4,
            FileStandardInformation = 5,
            FileInternalInformation = 6,
            FileEaInformation = 7,
            FileAccessInformation = 8,
            FileNameInformation = 9,
            FileRenameInformation = 10,
            FileLinkInformation = 11,
            FileNamesInformation = 12,
            FileDispositionInformation = 13,
            FilePositionInformation = 14,
            FileFullEaInformation = 15,
            FileModeInformation = 16,
            FileAlignmentInformation = 17,
            FileAllInformation = 18,
            FileAllocationInformation = 19,
            FileEndOfFileInformation = 20,
            FileAlternateNameInformation = 21,
            FileStreamInformation = 22,
            FilePipeInformation = 23,
            FilePipeLocalInformation = 24,
            FilePipeRemoteInformation = 25,
            FileMailslotQueryInformation = 26,
            FileMailslotSetInformation = 27,
            FileCompressionInformation = 28,
            FileObjectIdInformation = 29,
            FileCompletionInformation = 30,
            FileMoveClusterInformation = 31,
            FileQuotaInformation = 32,
            FileReparsePointInformation = 33,
            FileNetworkOpenInformation = 34,
            FileAttributeTagInformation = 35,
            FileTrackingInformation = 36,
            FileIdBothDirectoryInformation = 37,
            FileIdFullDirectoryInformation = 38,
            FileValidDataLengthInformation = 39,
            FileShortNameInformation = 40,
            FileIoCompletionNotificationInformation = 41,
            FileIoStatusBlockRangeInformation = 42,
            FileIoPriorityHintInformation = 43,
            FileSfioReserveInformation = 44,
            FileSfioVolumeInformation = 45,
            FileHardLinkInformation = 46,
            FileProcessIdsUsingFileInformation = 47,
            FileNormalizedNameInformation = 48,
            FileNetworkPhysicalNameInformation = 49,
            FileIdGlobalTxDirectoryInformation = 50,
            FileIsRemoteDeviceInformation = 51,
            FileUnusedInformation = 52,
            FileNumaNodeInformation = 53,
            FileStandardLinkInformation = 54,
            FileRemoteProtocolInformation = 55,
            FileRenameInformationBypassAccessCheck = 56,
            FileLinkInformationBypassAccessCheck = 57,
            FileVolumeNameInformation = 58,
            FileIdInformation = 59,
            FileIdExtdDirectoryInformation = 60,
            FileReplaceCompletionInformation = 61,
            FileHardLinkFullIdInformation = 62,
            FileIdExtdBothDirectoryInformation = 63,
            FileDispositionInformationEx = 64,
            FileRenameInformationEx = 65,
            FileRenameInformationExBypassAccessCheck = 66,
            FileDesiredStorageClassInformation = 67,
            FileStatInformation = 68
        }
    }

    public static partial class Advapi32
    {
        public const string SeDebugPrivilege = "SeDebugPrivilege";
        public const string SeBackupPrivilege = "SeBackupPrivilege";
        public const string SeRestorePrivilege = "SeRestorePrivilege";
        public const string SeShutdownPrivilege = "SeShutdownPrivilege";
        public const string SeRemoteShutdownPrivilege = "SeRemoteShutdownPrivilege";
        public const string SeTakeOwnershipPrivilege = "SeTakeOwnershipPrivilege";

        public static partial class SEPrivileges
        {
            public const uint SE_PRIVILEGE_DISABLED = 0;
            public const int SE_PRIVILEGE_ENABLED = 2;
        }

        public static partial class PerfCounterOptions
        {
            public const int NtPerfCounterSizeLarge = 0x00000100;
        }

        public static partial class ProcessOptions
        {
            public const int PROCESS_TERMINATE = 0x0001;
            public const int PROCESS_VM_READ = 0x0010;
            public const int PROCESS_SET_QUOTA = 0x0100;
            public const int PROCESS_SET_INFORMATION = 0x0200;
            public const int PROCESS_QUERY_INFORMATION = 0x0400;
            public const int PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
            public const int PROCESS_ALL_ACCESS = STANDARD_RIGHTS_REQUIRED | SYNCHRONIZE | 0xFFF;


            public const int STANDARD_RIGHTS_REQUIRED = 0x000F0000;
            public const int SYNCHRONIZE = 0x00100000;
        }

        public static partial class RPCStatus
        {
            public const int RPC_S_SERVER_UNAVAILABLE = 1722;
            public const int RPC_S_CALL_FAILED = 1726;
        }

        public static partial class StartupInfoOptions
        {
            public const int STARTF_USESTDHANDLES = 0x00000100;
            public const int CREATE_UNICODE_ENVIRONMENT = 0x00000400;
            public const int CREATE_NO_WINDOW = 0x08000000;
            public const uint STATUS_INFO_LENGTH_MISMATCH = 0xC0000004;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct LUID
        {
            public int LowPart;
            public int HighPart;
        }

        [StructLayout(LayoutKind.Sequential)]
        public partial struct LUID_AND_ATTRIBUTES
        {
            public LUID Luid;
            public uint Attributes;
        }

        public struct TOKEN_PRIVILEGE
        {
            public uint PrivilegeCount;
            public LUID_AND_ATTRIBUTES Privileges /*[ANYSIZE_ARRAY]*/;
        }


        public sealed class SafeServiceHandle : SafeHandleZeroOrMinusOneIsInvalid
        {
            public SafeServiceHandle() : base(true) { }

            override protected bool ReleaseHandle()
            {
                return Win32Api.Advapi32.CloseServiceHandle(handle);
            }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct SERVICE_DESCRIPTION
        {
            public IntPtr description;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct SERVICE_DELAYED_AUTOSTART_INFO
        {
            public bool fDelayedAutostart;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SERVICE_STATUS
        {
            public int serviceType;
            public int currentState;
            public int controlsAccepted;
            public int win32ExitCode;
            public int serviceSpecificExitCode;
            public int checkPoint;
            public int waitHint;
        }


        public partial class AcceptOptions
        {
            public const int ACCEPT_POWEREVENT = 0x00000040;
            public const int ACCEPT_PAUSE_CONTINUE = 0x00000002;
            public const int ACCEPT_SESSIONCHANGE = 0x00000080;
            public const int ACCEPT_SHUTDOWN = 0x00000004;
            public const int ACCEPT_STOP = 0x00000001;
        }

        public partial class ControlOptions
        {
            public const int CONTROL_CONTINUE = 0x00000003;
            public const int CONTROL_INTERROGATE = 0x00000004;
            public const int CONTROL_PAUSE = 0x00000002;
            public const int CONTROL_POWEREVENT = 0x0000000D;
            public const int CONTROL_SESSIONCHANGE = 0x0000000E;
            public const int CONTROL_SHUTDOWN = 0x00000005;
            public const int CONTROL_STOP = 0x00000001;
        }

        public partial class ServiceConfigOptions
        {
            public const int SERVICE_CONFIG_DESCRIPTION = 0x00000001;
            public const int SERVICE_CONFIG_FAILURE_ACTIONS = 0x00000002;
            public const int SERVICE_CONFIG_DELAYED_AUTO_START_INFO = 0x00000003;
        }

        public partial class ServiceOptions
        {
            public const int SERVICE_QUERY_CONFIG = 0x0001;
            public const int SERVICE_CHANGE_CONFIG = 0x0002;
            public const int SERVICE_QUERY_STATUS = 0x0004;
            public const int SERVICE_ENUMERATE_DEPENDENTS = 0x0008;
            public const int SERVICE_START = 0x0010;
            public const int SERVICE_STOP = 0x0020;
            public const int SERVICE_PAUSE_CONTINUE = 0x0040;
            public const int SERVICE_INTERROGATE = 0x0080;
            public const int SERVICE_USER_DEFINED_CONTROL = 0x0100;

            public const int SERVICE_ALL_ACCESS =
                STANDARD_RIGHTS_REQUIRED |
                SERVICE_QUERY_CONFIG |
                SERVICE_CHANGE_CONFIG |
                SERVICE_QUERY_STATUS |
                SERVICE_ENUMERATE_DEPENDENTS |
                SERVICE_START |
                SERVICE_STOP |
                SERVICE_PAUSE_CONTINUE |
                SERVICE_INTERROGATE |
                SERVICE_USER_DEFINED_CONTROL;

            public const int STANDARD_RIGHTS_DELETE = 0x00010000;
            public const int STANDARD_RIGHTS_REQUIRED = 0x000F0000;
        }

        public partial class ServiceTypeOptions
        {
            public const int SERVICE_TYPE_ADAPTER = 0x00000004;
            public const int SERVICE_TYPE_FILE_SYSTEM_DRIVER = 0x00000002;
            public const int SERVICE_TYPE_INTERACTIVE_PROCESS = 0x00000100;
            public const int SERVICE_TYPE_KERNEL_DRIVER = 0x00000001;
            public const int SERVICE_TYPE_RECOGNIZER_DRIVER = 0x00000008;
            public const int SERVICE_TYPE_WIN32_OWN_PROCESS = 0x00000010;
            public const int SERVICE_TYPE_WIN32_SHARE_PROCESS = 0x00000020;
            public const int SERVICE_TYPE_WIN32 =
                SERVICE_TYPE_WIN32_OWN_PROCESS |
                SERVICE_TYPE_WIN32_SHARE_PROCESS;
            public const int SERVICE_TYPE_DRIVER =
                SERVICE_TYPE_KERNEL_DRIVER |
                SERVICE_TYPE_FILE_SYSTEM_DRIVER |
                SERVICE_TYPE_RECOGNIZER_DRIVER;
            public const int SERVICE_TYPE_ALL =
                SERVICE_TYPE_WIN32 |
                SERVICE_TYPE_ADAPTER |
                SERVICE_TYPE_DRIVER |
                SERVICE_TYPE_INTERACTIVE_PROCESS;
        }

        public partial class ServiceAccessOptions
        {
            public const int ACCESS_TYPE_CHANGE_CONFIG = 0x0002;
            public const int ACCESS_TYPE_ENUMERATE_DEPENDENTS = 0x0008;
            public const int ACCESS_TYPE_INTERROGATE = 0x0080;
            public const int ACCESS_TYPE_PAUSE_CONTINUE = 0x0040;
            public const int ACCESS_TYPE_QUERY_CONFIG = 0x0001;
            public const int ACCESS_TYPE_QUERY_STATUS = 0x0004;
            public const int ACCESS_TYPE_START = 0x0010;
            public const int ACCESS_TYPE_STOP = 0x0020;
            public const int ACCESS_TYPE_USER_DEFINED_CONTROL = 0x0100;
            public const int ACCESS_TYPE_ALL =
                ServiceOptions.STANDARD_RIGHTS_REQUIRED |
                ACCESS_TYPE_QUERY_CONFIG |
                ACCESS_TYPE_CHANGE_CONFIG |
                ACCESS_TYPE_QUERY_STATUS |
                ACCESS_TYPE_ENUMERATE_DEPENDENTS |
                ACCESS_TYPE_START |
                ACCESS_TYPE_STOP |
                ACCESS_TYPE_PAUSE_CONTINUE |
                ACCESS_TYPE_INTERROGATE |
                ACCESS_TYPE_USER_DEFINED_CONTROL;
        }

        public partial class ServiceStartModes
        {
            public const int START_TYPE_BOOT = 0x00000000;
            public const int START_TYPE_SYSTEM = 0x00000001;
            public const int START_TYPE_AUTO = 0x00000002;
            public const int START_TYPE_DEMAND = 0x00000003;
            public const int START_TYPE_DISABLED = 0x00000004;
        }

        public partial class ServiceState
        {
            public const int SERVICE_ACTIVE = 1;
            public const int SERVICE_INACTIVE = 2;
            public const int SERVICE_STATE_ALL = SERVICE_ACTIVE | SERVICE_INACTIVE;
        }

        public partial class StatusOptions
        {
            public const int STATUS_ACTIVE = 0x00000001;
            public const int STATUS_INACTIVE = 0x00000002;
            public const int STATUS_ALL = STATUS_ACTIVE | STATUS_INACTIVE;
        }

        public partial class ServiceControlStatus
        {
            public const int STATE_CONTINUE_PENDING = 0x00000005;
            public const int STATE_PAUSED = 0x00000007;
            public const int STATE_PAUSE_PENDING = 0x00000006;
            public const int STATE_RUNNING = 0x00000004;
            public const int STATE_START_PENDING = 0x00000002;
            public const int STATE_STOPPED = 0x00000001;
            public const int STATE_STOP_PENDING = 0x00000003;
            public const int ERROR_EXCEPTION_IN_SERVICE = 0x00000428;
        }

        public partial class ServiceStartErrorModes
        {
            public const int ERROR_CONTROL_CRITICAL = 0x00000003;
            public const int ERROR_CONTROL_IGNORE = 0x00000000;
            public const int ERROR_CONTROL_NORMAL = 0x00000001;
            public const int ERROR_CONTROL_SEVERE = 0x00000002;
        }

        public partial class ServiceControllerOptions
        {
            public const int SC_ENUM_PROCESS_INFO = 0;
            public const int SC_MANAGER_CONNECT = 0x0001;
            public const int SC_MANAGER_CREATE_SERVICE = 0x0002;
            public const int SC_MANAGER_ENUMERATE_SERVICE = 0x0004;
            public const int SC_MANAGER_LOCK = 0x0008;
            public const int SC_MANAGER_MODIFY_BOOT_CONFIG = 0x0020;
            public const int SC_MANAGER_QUERY_LOCK_STATUS = 0x0010;
            public const int SC_MANAGER_ALL =
                ServiceOptions.STANDARD_RIGHTS_REQUIRED |
                SC_MANAGER_CONNECT |
                SC_MANAGER_CREATE_SERVICE |
                SC_MANAGER_ENUMERATE_SERVICE |
                SC_MANAGER_LOCK |
                SC_MANAGER_QUERY_LOCK_STATUS |
                SC_MANAGER_MODIFY_BOOT_CONFIG;
        }

        public partial class PowerBroadcastStatus
        {
            public const int PBT_APMBATTERYLOW = 0x0009;
            public const int PBT_APMOEMEVENT = 0x000B;
            public const int PBT_APMPOWERSTATUSCHANGE = 0x000A;
            public const int PBT_APMQUERYSUSPEND = 0x0000;
            public const int PBT_APMQUERYSUSPENDFAILED = 0x0002;
            public const int PBT_APMRESUMEAUTOMATIC = 0x0012;
            public const int PBT_APMRESUMECRITICAL = 0x0006;
            public const int PBT_APMRESUMESUSPEND = 0x0007;
            public const int PBT_APMSUSPEND = 0x0004;
        }

        public partial class SessionStateChange
        {
            public const int WTS_CONSOLE_CONNECT = 0x1;
            public const int WTS_CONSOLE_DISCONNECT = 0x2;
            public const int WTS_REMOTE_CONNECT = 0x3;
            public const int WTS_REMOTE_DISCONNECT = 0x4;
            public const int WTS_SESSION_LOGON = 0x5;
            public const int WTS_SESSION_LOGOFF = 0x6;
            public const int WTS_SESSION_LOCK = 0x7;
            public const int WTS_SESSION_UNLOCK = 0x8;
            public const int WTS_SESSION_REMOTE_CONTROL = 0x9;
        }

        // From https://github.com/cloudfoundry/garden-windows-release/blob/754d9b0c6b3c60a1ee23028e25213e849274562d/GardenWindowsRelease/ServiceConfigurator/ServiceConfigurator.cs
        public const int SC_ACTION_NONE = 0;
        public const int SC_ACTION_RESTART = 1;
        private const int DELAY_IN_MILLISECONDS = 0;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct SERVICE_FAILURE_ACTIONS
        {
            public int dwResetPeriod;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string lpRebootMsg;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string lpCommand;
            public int cActions;
            public IntPtr lpsaActions;
        }
    }

    public sealed class SafeFindHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public SafeFindHandle() : base(true) { }

        override protected bool ReleaseHandle()
        {
            return Win32Api.Kernel32.FindClose(handle);
        }
    }

    // Support routines
    public sealed class SafeTokenHandle : SafeHandle
    {
        private const int DefaultInvalidHandleValue = 0;

        public static readonly SafeTokenHandle InvalidHandle = new SafeTokenHandle(new IntPtr(DefaultInvalidHandleValue));

        public SafeTokenHandle() : base(new IntPtr(DefaultInvalidHandleValue), true) { }

        public SafeTokenHandle(IntPtr handle)
            : base(new IntPtr(DefaultInvalidHandleValue), true)
        {
            SetHandle(handle);
        }

        public override bool IsInvalid
        {
            get { return handle == IntPtr.Zero || handle == new IntPtr(-1); }
        }

        protected override bool ReleaseHandle()
        {
            return Win32Api.Kernel32.CloseHandle(handle);
        }
    }

    public static void EnablePrivilege(string privilegeName, bool enabled)
    {
        SetPrivilege(privilegeName, enabled ? (int)Win32Api.Advapi32.SEPrivileges.SE_PRIVILEGE_ENABLED : 0);
    }

    public static unsafe void SetPrivilege(string privilegeName, int attrib)
    {
        // this is only a "pseudo handle" to the current process - no need to close it later
        SafeProcessHandle processHandle = Win32Api.Kernel32.GetCurrentProcess();

        SafeTokenHandle? hToken = null;

        try
        {
            // get the process token so we can adjust the privilege on it.  We DO need to
            // close the token when we're done with it.
            if (!Win32Api.Advapi32.OpenProcessToken(processHandle, Win32Api.Kernel32.HandleOptions.TOKEN_ADJUST_PRIVILEGES, out hToken))
            {
                throw new Win32Exception();
            }

            if (!Win32Api.Advapi32.LookupPrivilegeValue(null, privilegeName, out Win32Api.Advapi32.LUID luid))
            {
                throw new Win32Exception();
            }

            Win32Api.Advapi32.TOKEN_PRIVILEGE tp;
            tp.PrivilegeCount = 1;
            tp.Privileges.Luid = luid;
            tp.Privileges.Attributes = (uint)attrib;

            Win32Api.Advapi32.AdjustTokenPrivileges(hToken, false, &tp, 0, null, null);

            // AdjustTokenPrivileges can return true even if it failed to
            // set the privilege, so we need to use GetLastError
            if (Marshal.GetLastWin32Error() != Win32Api.Errors.ERROR_SUCCESS)
            {
                throw new Win32Exception();
            }
        }
        finally
        {
            if (hToken != null)
            {
                hToken.Dispose();
            }
        }
    }

    public struct Win32DisableMediaInsertionPrompt : IDisposable
    {
        private bool _disableSuccess;
        private uint _oldMode;

        public static Win32DisableMediaInsertionPrompt Create()
        {
            Win32DisableMediaInsertionPrompt prompt = new Win32DisableMediaInsertionPrompt();
            prompt._disableSuccess = Win32Api.Kernel32.SetThreadErrorMode(Win32Api.Kernel32.SEM_FAILCRITICALERRORS, out prompt._oldMode);
            return prompt;
        }

        public void Dispose()
        {
            uint ignore;
            if (_disableSuccess)
                Win32Api.Kernel32.SetThreadErrorMode(_oldMode, out ignore);
        }
    }
}

public static class PalWin32FileStream
{
    private const int FILE_ATTRIBUTE_NORMAL = 0x00000080;
    private const int FILE_ATTRIBUTE_ENCRYPTED = 0x00004000;
    private const int FILE_FLAG_OVERLAPPED = 0x40000000;
    public const int GENERIC_READ = unchecked((int)0x80000000);
    private const int GENERIC_WRITE = 0x40000000;

    public static FileStream Create(string path, FileMode mode, FileAccess access, FileShare share, int bufferSize, FileOptions options)
    {
        if (path == null)
            throw new ArgumentNullException(nameof(path));
        if (path.Length == 0)
            throw new ArgumentException(nameof(path));

        // don't include inheritable in our bounds check for share
        FileShare tempshare = share & ~FileShare.Inheritable;
        string? badArg = null;
        if (mode < FileMode.CreateNew || mode > FileMode.Append)
            badArg = nameof(mode);
        else if (access < FileAccess.Read || access > FileAccess.ReadWrite)
            badArg = nameof(access);
        else if (tempshare < FileShare.None || tempshare > (FileShare.ReadWrite | FileShare.Delete))
            badArg = nameof(share);

        if (badArg != null)
            throw new ArgumentOutOfRangeException(badArg);

        // NOTE: any change to FileOptions enum needs to be matched here in the error validation
        if (options != FileOptions.None && (options & ~(FileOptions.WriteThrough | FileOptions.Asynchronous | FileOptions.RandomAccess | FileOptions.DeleteOnClose | FileOptions.SequentialScan | FileOptions.Encrypted | (FileOptions)0x20000000 /* NoBuffering */
            | (FileOptions)Win32Api.Kernel32.FileOperations.FILE_FLAG_BACKUP_SEMANTICS // added for backup
            )) != 0)
            throw new ArgumentOutOfRangeException(nameof(options));

        if (bufferSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(bufferSize));

        // Write access validation
        if ((access & FileAccess.Write) == 0)
        {
            if (mode == FileMode.Truncate || mode == FileMode.CreateNew || mode == FileMode.Create || mode == FileMode.Append)
            {
                // No write access, mode and access disagree but flag access since mode comes first
                throw new ArgumentException(nameof(access));
            }
        }

        if ((access & FileAccess.Read) != 0 && mode == FileMode.Append)
            throw new ArgumentException(nameof(access));

        string fullPath = Path.GetFullPath(path);

        string _path = fullPath;
        FileAccess _access = access;
        int _bufferLength = bufferSize;

        bool _useAsyncIO = false;

        if ((options & FileOptions.Asynchronous) != 0)
            _useAsyncIO = true;

        SafeFileHandle _fileHandle = CreateFileOpenHandle(mode, share, options, _access, _path);

        FileStream ret = new FileStream(_fileHandle, _access, 4096, _useAsyncIO);

        //_fileHandle._SetAsync(_useAsyncIO);

        if (mode == FileMode.Append)
        {
            ret.Seek(0, SeekOrigin.End);
        }

        return ret;
    }

    public static unsafe SafeFileHandle CreateFileOpenHandle(FileMode mode, FileShare share, FileOptions options, FileAccess _access, string _path)
    {
        Win32Api.Kernel32.SECURITY_ATTRIBUTES secAttrs = GetSecAttrs(share);

        int fAccess =
            ((_access & FileAccess.Read) == FileAccess.Read ? GENERIC_READ : 0) |
            ((_access & FileAccess.Write) == FileAccess.Write ? GENERIC_WRITE : 0);

        // Our Inheritable bit was stolen from Windows, but should be set in
        // the security attributes class.  Don't leave this bit set.
        share &= ~FileShare.Inheritable;

        // Must use a valid Win32 constant here...
        if (mode == FileMode.Append)
            mode = FileMode.OpenOrCreate;

        int flagsAndAttributes = (int)options;

        // For mitigating local elevation of privilege attack through named pipes
        // make sure we always call CreateFile with SECURITY_ANONYMOUS so that the
        // named pipe server can't impersonate a high privileged client security context
        // (note that this is the effective default on CreateFile2)
        flagsAndAttributes |= (Win32Api.Kernel32.SecurityOptions.SECURITY_SQOS_PRESENT | Win32Api.Kernel32.SecurityOptions.SECURITY_ANONYMOUS);

        using (Lfs.EnterDisableMediaInsertionPrompt())
        {
            return ValidateFileHandle(
                Win32Api.Kernel32.CreateFile(_path, fAccess, share, mode, flagsAndAttributes), _path);
        }
    }

    public static unsafe Win32Api.Kernel32.SECURITY_ATTRIBUTES GetSecAttrs(FileShare share)
    {
        Win32Api.Kernel32.SECURITY_ATTRIBUTES secAttrs = default;
        if ((share & FileShare.Inheritable) != 0)
        {
            secAttrs = new Win32Api.Kernel32.SECURITY_ATTRIBUTES
            {
                nLength = (uint)sizeof(Win32Api.Kernel32.SECURITY_ATTRIBUTES),
                bInheritHandle = Win32Api.BOOL.TRUE
            };
        }
        return secAttrs;
    }


    public static SafeFileHandle ValidateFileHandle(SafeFileHandle fileHandle, string _path)
    {
        if (fileHandle.IsInvalid)
        {
            // Return a meaningful exception with the full path.

            // NT5 oddity - when trying to open "C:\" as a Win32FileStream,
            // we usually get ERROR_PATH_NOT_FOUND from the OS.  We should
            // probably be consistent w/ every other directory.
            int errorCode = Marshal.GetLastWin32Error();

            if (errorCode == Win32Api.Errors.ERROR_PATH_NOT_FOUND && _path.Length == Win32PathInternal.GetRootLength(_path))
                errorCode = Win32Api.Errors.ERROR_ACCESS_DENIED;

            //throw new Win32Exception(errorCode);
            throw GetExceptionForWin32Error(errorCode, _path);
        }

        //fileHandle.IsAsync = _useAsyncIO;
        return fileHandle;
    }

    public static Exception GetExceptionForLastWin32Error(string path = "")
        => GetExceptionForWin32Error(Marshal.GetLastWin32Error(), path);

    public static Exception GetExceptionForWin32Error(int errorCode, string? argument = "")
    {
        argument = argument._NonNull();

        List<string> o = new List<string>();

        string win32msg = "";
        try
        {
            win32msg = $"{new Win32Exception(errorCode).Message}";
        }
        catch { }

        if (win32msg._IsFilled())
            o.Add($"Message = '{win32msg}'");

        o.Add($"Code = {errorCode} (0x{MakeHRFromErrorCode(errorCode):X})");

        if (argument._IsFilled())
            o.Add($"Argument = '{argument}'");

        string argumentStr = (argument._IsFilled() ? $", Argument = '{argument}'" : "");

        string msg = "Win32 Error " + Str.CombineStringArray(o, ", ");

        switch (errorCode)
        {
            case Win32Api.Errors.ERROR_FILE_NOT_FOUND:
                return new FileNotFoundException(msg);
            case Win32Api.Errors.ERROR_PATH_NOT_FOUND:
                return new DirectoryNotFoundException(msg);
            case Win32Api.Errors.ERROR_ACCESS_DENIED:
                return new UnauthorizedAccessException(msg);
            case Win32Api.Errors.ERROR_ALREADY_EXISTS:
                if (string.IsNullOrEmpty(msg))
                    goto default;
                return new IOException(msg, MakeHRFromErrorCode(errorCode));
            case Win32Api.Errors.ERROR_FILENAME_EXCED_RANGE:
                return new PathTooLongException(msg);
            case Win32Api.Errors.ERROR_SHARING_VIOLATION:
                return new IOException(msg, MakeHRFromErrorCode(errorCode));
            case Win32Api.Errors.ERROR_FILE_EXISTS:
                if (string.IsNullOrEmpty(msg))
                    goto default;
                return new IOException(msg, MakeHRFromErrorCode(errorCode));
            case Win32Api.Errors.ERROR_OPERATION_ABORTED:
                return new OperationCanceledException(msg);
            case Win32Api.Errors.ERROR_INVALID_PARAMETER:
            default:
                return new Win32Exception(errorCode, msg);
        }
    }

    private static int MakeHRFromErrorCode(int errorCode)
    {
        // Don't convert it if it is already an HRESULT
        if ((0xFFFF0000 & errorCode) != 0)
            return errorCode;

        return unchecked(((int)0x80070000) | errorCode);
    }


}

