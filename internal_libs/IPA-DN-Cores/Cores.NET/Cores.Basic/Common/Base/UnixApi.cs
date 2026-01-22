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
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

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


[Flags]
public enum UnixPermissions
{
    Mask = S_IRWXU | S_IRWXG | S_IRWXO,

    S_IRWXU = S_IRUSR | S_IWUSR | S_IXUSR,
    S_IRUSR = 0x100,
    S_IWUSR = 0x80,
    S_IXUSR = 0x40,

    S_IRWXG = S_IRGRP | S_IWGRP | S_IXGRP,
    S_IRGRP = 0x20,
    S_IWGRP = 0x10,
    S_IXGRP = 0x8,

    S_IRWXO = S_IROTH | S_IWOTH | S_IXOTH,
    S_IROTH = 0x4,
    S_IWOTH = 0x2,
    S_IXOTH = 0x1,

    S_IXUGO = S_IXUSR | S_IXGRP | S_IXOTH,
}

public static class UnixApi
{
    static class Libraries
    {
        public const string SystemNative = "libSystem.Native";
        public const string CryptoNative_OpenSsl = "libSystem.Security.Cryptography.Native.OpenSsl";
    }

    public enum Signals : int
    {
        None = 0,
        SIGKILL = 9,
        SIGTERM = 15,
    }

    public enum LockOperations : int
    {
        LOCK_SH = 1,    /* shared lock */
        LOCK_EX = 2,    /* exclusive lock */
        LOCK_NB = 4,    /* don't block when locking*/
        LOCK_UN = 8,    /* unlock */
    }

    public enum OpenFlags
    {
        // Access modes (mutually exclusive)
        O_RDONLY = 0x0000,
        O_WRONLY = 0x0001,
        O_RDWR = 0x0002,

        // Flags (combinable)
        O_CLOEXEC = 0x0010,
        O_CREAT = 0x0020,
        O_EXCL = 0x0040,
        O_TRUNC = 0x0080,
        O_SYNC = 0x0100,
    }

    [DllImport(Libraries.CryptoNative_OpenSsl, EntryPoint = "CryptoNative_OpenSslVersionNumber")]
    public static extern long OpenSslVersionNumber();

    [DllImport(Libraries.SystemNative, EntryPoint = "SystemNative_ChMod", SetLastError = true)]
    internal static extern int ChMod(string path, int mode);

    [DllImport(Libraries.SystemNative, EntryPoint = "SystemNative_FLock", SetLastError = true)]
    public static extern int FLock(IntPtr fd, LockOperations operation);

    [DllImport(Libraries.SystemNative, EntryPoint = "SystemNative_Open", SetLastError = true)]
    public static extern IntPtr Open(string filename, OpenFlags flags, int mode);

    [DllImport(Libraries.SystemNative, EntryPoint = "SystemNative_Close", SetLastError = true)]
    public static extern int Close(IntPtr fd);

    public enum PipeFlags
    {
        O_CLOEXEC = 0x0010,
    }

    [DllImport(Libraries.SystemNative, EntryPoint = "SystemNative_Pipe", SetLastError = true)]
    public static extern unsafe int Pipe(int* pipefd, PipeFlags flags = 0);

    [DllImport(Libraries.SystemNative, EntryPoint = "SystemNative_FcntlSetIsNonBlocking", SetLastError = true)]
    public static extern int SetIsNonBlocking(IntPtr fd, int isNonBlocking);

    [DllImport(Libraries.SystemNative, EntryPoint = "SystemNative_Kill", SetLastError = true)]
    internal static extern int Kill(int pid, Signals signal);

    [DllImport(Libraries.SystemNative, EntryPoint = "SystemNative_GetSid")]
    internal static extern int GetSid(int pid);

    [SkipLocalsInit]
    public static unsafe void NewPipe(out IntPtr p0_read, out IntPtr p1_write)
    {
        int* fds = stackalloc int[2];

        int ret = Pipe(fds);

        if (ret < 0)
        {
            throw new SystemException("SystemNative_Pipe failed.");
        }

        p0_read = new IntPtr(fds[0]);
        p1_write = new IntPtr(fds[1]);

        SetIsNonBlocking(p0_read, 1);
        SetIsNonBlocking(p1_write, 1);
    }

    [DllImport(Libraries.SystemNative, EntryPoint = "SystemNative_Write", SetLastError = true)]
    public static extern unsafe int Write(IntPtr fd, byte* buffer, int bufferSize);

    public static unsafe int Write(IntPtr fd, byte[] buffer, int offset, int size)
    {
        fixed (byte* p = buffer)
        {
            byte* p2 = p + offset;
            return Write(fd, p2, size);
        }
    }

    [DllImport(Libraries.SystemNative, EntryPoint = "SystemNative_Read", SetLastError = true)]
    internal static extern unsafe int Read(IntPtr fd, byte* buffer, int count);

    public static unsafe int Read(IntPtr fd, byte[] buffer, int offset, int size)
    {
        fixed (byte* p = buffer)
        {
            byte* p2 = p + offset;
            return Read(fd, p2, size);
        }
    }

    public enum PollEvents : short
    {
        POLLNONE = 0x0000,  // No events occurred.
        POLLIN = 0x0001,  // non-urgent readable data available
        POLLPRI = 0x0002,  // urgent readable data available
        POLLOUT = 0x0004,  // data can be written without blocked
        POLLERR = 0x0008,  // an error occurred
        POLLHUP = 0x0010,  // the file descriptor hung up
        POLLNVAL = 0x0020,  // the requested events were invalid
    }

    public struct PollEvent
    {
        public int FileDescriptor;         // The file descriptor to poll
        public PollEvents Events;          // The events to poll for
        public PollEvents TriggeredEvents; // The events that occurred which triggered the poll
    }

    [DllImport(Libraries.SystemNative, EntryPoint = "SystemNative_Poll")]
    public static extern unsafe int Poll(PollEvent* pollEvents, uint eventCount, int timeout, uint* triggered);

    public static unsafe int Poll(PollEvent[] event_list, int timeout)
    {
        fixed (PollEvent* pe = event_list)
        {
            uint* triggered = stackalloc uint[1];

            int ret = Poll(pe, (uint)event_list.Length, timeout, triggered);

            return ret;
        }
    }

    public static unsafe void Poll(IntPtr[] reads, IntPtr[] writes, int timeout)
    {
        if (timeout == 0) return;

        PollEvent[] p;
        int num, n, num_read_total, num_write_total;

        num_read_total = num_write_total = 0;
        foreach (IntPtr fd in reads) if ((int)fd != -1) num_read_total++;
        foreach (IntPtr fd in writes) if ((int)fd != -1) num_write_total++;

        num = num_read_total + num_write_total;

        p = new PollEvent[num];

        n = 0;

        foreach (IntPtr fd in reads)
        {
            if ((int)fd != -1)
            {
                p[n].FileDescriptor = (int)fd;
                p[n].Events = PollEvents.POLLIN | PollEvents.POLLPRI | PollEvents.POLLERR | PollEvents.POLLHUP;
                n++;
            }
        }

        foreach (IntPtr fd in writes)
        {
            if ((int)fd != -1)
            {
                p[n].FileDescriptor = (int)fd;
                p[n].Events = PollEvents.POLLIN | PollEvents.POLLPRI | PollEvents.POLLERR | PollEvents.POLLHUP | PollEvents.POLLOUT;
                n++;
            }
        }

        if (num == 0)
        {
            ThreadObj.Sleep(timeout);
        }
        else
        {
            //Dbg.WriteLine("Poll Begin.");

            int ret = Poll(p, timeout);

            //Dbg.WriteLine($"Poll end: ret = {ret}, reads = {reads.Length}, writes = {writes.Length}, pfds = {p.Length}");

            //for (int i = 0; i < reads.Length; i++) Dbg.WriteLine($"reads[{i}] = {reads[i]}");
            //for (int i = 0; i < writes.Length; i++) Dbg.WriteLine($"writes[{i}] = {writes[i]}");
        }
    }

    [DllImport(Libraries.SystemNative, EntryPoint = "SystemNative_ReadLink", SetLastError = true)]
    private static extern unsafe int ReadLink(string path, byte[] buffer, int bufferSize);
    public static string? ReadLink(string path)
    {
        int bufferSize = 256;
        do
        {
            byte[] buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
            try
            {
                int resultLength = ReadLink(path, buffer, buffer.Length);
                if (resultLength < 0)
                {
                    // error
                    return null;
                }
                else if (resultLength < buffer.Length)
                {
                    // success
                    return Str.Utf8Encoding.GetString(buffer, 0, resultLength);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            // buffer was too small, loop around again and try with a larger buffer.
            bufferSize *= 2;
        } while (true);
    }

    public static bool IsProcess(int pid)
    {
        try
        {
            if (GetSid(pid) == -1)
            {
                return false;
            }

            return true;
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

    internal const ulong RLIM_INFINITY = ulong.MaxValue;

    internal enum RlimitResources : int
    {
        RLIMIT_CPU = 0,        // CPU limit in seconds
        RLIMIT_FSIZE = 1,        // Largest file that can be created, in bytes
        RLIMIT_DATA = 2,        // Maximum size of data segment, in bytes
        RLIMIT_STACK = 3,        // Maximum size of stack segment, in bytes
        RLIMIT_CORE = 4,        // Largest core file that can be created, in bytes
        RLIMIT_AS = 5,        // Address space limit
        RLIMIT_RSS = 6,        // Largest resident set size, in bytes
        RLIMIT_MEMLOCK = 7,        // Locked-in-memory address space
        RLIMIT_NPROC = 8,        // Number of processes
        RLIMIT_NOFILE = 9,        // Number of open files
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RLimit
    {
        internal ulong CurrentLimit;
        internal ulong MaximumLimit;
    }

    [DllImport(Libraries.SystemNative, EntryPoint = "SystemNative_GetRLimit", SetLastError = true)]
    internal static extern int GetRLimit(RlimitResources resourceType, out RLimit limits);

    [DllImport(Libraries.SystemNative, EntryPoint = "SystemNative_SetRLimit", SetLastError = true)]
    internal static extern int SetRLimit(RlimitResources resourceType, ref RLimit limits);

    internal static class UnixLimitConsts
    {
        public const uint UNIX_MAX_CHILD_PROCESSES = 2000000;       // Maximum number of child processes
        public const uint UNIX_LINUX_MAX_THREADS = 200000000;       // Maximum number of threads
        public const uint UNIX_MAX_LOCKS = 65536;                   // Maximum number of locks
        public const uint UNIX_MAX_MEMORY = int.MaxValue - 1;
        public const ulong UNIX_MAX_MEMORY_64 = (65536UL * 2147483647UL);  // Maximum memory capacity (64-bit)
        public const uint UNIX_MAX_FD = (655360);               // Maximum number of FDs
        public const uint UNIX_MAX_FD_MACOS = (10000);          // Maximum number of FDs (Mac OS X)
    }

    internal static void SetResourceLimits(RlimitResources id, ulong value)
    {
        RLimit t = new RLimit();

        int r1 = GetRLimit(id, out t);

        ulong hard_limit = t.MaximumLimit;

        t = new RLimit();
        t.CurrentLimit = Math.Min(value, hard_limit);
        t.MaximumLimit = hard_limit;

        int r2 = SetRLimit(id, ref t);

        t = new RLimit();
        t.CurrentLimit = value;
        t.MaximumLimit = value;

        int r3 = SetRLimit(id, ref t);
    }

    internal static void InitUnixLimitsValue(bool isMac, bool is64bit)
    {
        SetResourceLimits(RlimitResources.RLIMIT_DATA, is64bit ? UnixLimitConsts.UNIX_MAX_MEMORY_64 : UnixLimitConsts.UNIX_MAX_MEMORY);
        SetResourceLimits(RlimitResources.RLIMIT_RSS, is64bit ? UnixLimitConsts.UNIX_MAX_MEMORY_64 : UnixLimitConsts.UNIX_MAX_MEMORY);

        SetResourceLimits(RlimitResources.RLIMIT_NOFILE, isMac ? UnixLimitConsts.UNIX_MAX_FD_MACOS : UnixLimitConsts.UNIX_MAX_FD);

        SetResourceLimits(RlimitResources.RLIMIT_MEMLOCK, UnixLimitConsts.UNIX_MAX_LOCKS);
        SetResourceLimits(RlimitResources.RLIMIT_NPROC, UnixLimitConsts.UNIX_MAX_CHILD_PROCESSES);
    }

    public static async Task<long> GetBlockDeviceSizeAsync(string path, CancellationToken cancel = default)
    {
        if (Env.IsLinux == false)
        {
            throw new NotImplementedException();
        }

        string line = await EasyExec.ExecRetFirstLineAsync(Consts.LinuxCommands.BlockDev, $"--getsize64 {path}");
        
        line = line._GetFirstFilledLineFromLines();

        return line._ToLong();
    }
}

