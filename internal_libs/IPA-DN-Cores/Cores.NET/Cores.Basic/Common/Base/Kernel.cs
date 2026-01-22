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
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IPA.Cores.Basic;
using IPA.Cores.Basic.Legacy;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

namespace IPA.Cores.Basic
{
    public static class Kernel
    {
        [DllImport("kernel32.dll", SetLastError = true, CallingConvention = CallingConvention.Winapi)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWow64Process(
            [In] IntPtr hProcess,
            [Out] out bool wow64Process
        );

        public static PlatformID GetOsPlatform()
        {
            return Environment.OSVersion.Platform;
        }

        public static bool InternalCheckIsWow64()
        {
            if (GetOsPlatform() == PlatformID.Win32NT)
            {
                if ((Environment.OSVersion.Version.Major == 5 && Environment.OSVersion.Version.Minor >= 1) ||
                    Environment.OSVersion.Version.Major >= 6)
                {
                    using (Process p = Process.GetCurrentProcess())
                    {
                        bool retVal;
                        if (!IsWow64Process(p.Handle, out retVal))
                        {
                            return false;
                        }
                        return retVal;
                    }
                }
                else
                {
                    return false;
                }
            }

            return false;
        }

        // スリープ
        public static void SleepThread(int millisec)
        {
            ThreadObj.Sleep(millisec);
        }

        public static void SleepThreadInfinite() => SleepThread(Timeout.Infinite);

        // デバッグのため停止
        public static void SuspendForDebug()
        {
            Dbg.WriteLine("SuspendForDebug() called.");
            SleepThread(ThreadObj.Infinite);
        }

        // 環境変数文字列の取得
        public static string GetEnvStr(string name)
            => Environment.GetEnvironmentVariable(name)._NonNull();

        // 現在のプロセスを強制終了する
        static public void SelfKill(string msg = "")
        {
            if (msg._IsFilled()) msg._Print();
            System.Diagnostics.Process.GetCurrentProcess().Kill();

            Environment.Exit(-1); // 実行されないはずであるが念のため
        }

        // プログラムを起動する
        public static Process Run(string exeName, string args, bool shellExecute = false)
        {
            Process p = new Process();
            p.StartInfo.FileName = IO.InnerFilePath(exeName);
            p.StartInfo.Arguments = args;

            if (shellExecute)
            {
                p.StartInfo.UseShellExecute = true;
            }

            p.Start();

            return p;
        }

        // OS の再起動
        static Once RebootOnceFlag;
        public static void RebootOperatingSystemForcefullyDangerous()
        {
            if (Env.IsLinux)
            {
                if (RebootOnceFlag.IsFirstCall())
                {
                    // sync, sync, sync
                    for (int i = 0; i < 3; i++)
                    {
                        UnixTryRunSystemProgram(EnsureInternal.Yes, Consts.LinuxCommands.Sync, "", CoresConfig.Timeouts.RebootDangerous_Sync_Timeout);
                    }

                    Sleep(300);

                    // reboot
                    for (int i = 0; i < 3; i++)
                    {
                        UnixTryRunSystemProgram(EnsureInternal.Yes, Consts.LinuxCommands.Reboot, "--reboot --force", CoresConfig.Timeouts.RebootDangerous_Reboot_Timeout);
                    }

                    Sleep(300);

                    // reboot with BIOS (最後の手段)
                    Lfs.WriteStringToFile(@"/proc/sys/kernel/sysrq", "1");

                    Sleep(300);

                    Lfs.WriteStringToFile(@"/proc/sysrq-trigger", "b");
                }
                else
                {
                    throw new CoresException("Already rebooting");
                }
            }
            else
            {
                throw new NotImplementedException();
            }
        }

        public static bool UnixTryRunSystemProgram(EnsureInternal yes, string commandName, string args, int timeout = Timeout.Infinite)
        {
            string[] dirs = { "/sbin/", "/bin/", "/usr/bin/", "/usr/local/bin/", "/usr/local/sbin/", "/usr/local/bin/" };

            foreach (string dir in dirs)
            {
                if (UnixTryRunProgramInternal(yes, Path.Combine(dir, commandName), args, timeout))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool UnixTryRunProgramInternal(EnsureInternal yes, string exe, string args, int timeout = Timeout.Infinite)
        {
            if (exe._IsEmpty()) throw new ArgumentNullException(nameof(exe));
            args = args._NonNull();

            try
            {
                ProcessStartInfo info = new ProcessStartInfo()
                {
                    FileName = exe,
                    Arguments = args,
                    UseShellExecute = false,
                    RedirectStandardOutput = false,
                    RedirectStandardError = false,
                    RedirectStandardInput = false,
                    CreateNoWindow = true,
                    WorkingDirectory = Env.AppRootDir,
                };

                using (Process p = Process.Start(info)!)
                {
                    return p.WaitForExit(timeout);
                }
            }
            catch (Exception ex)
            {
                ex._Debug();
            }

            return false;
        }
    }
    // 子プロセスの実行パラメータのフラグ
    [Flags]
    public enum ExecFlags : long
    {
        None = 0,
        KillProcessGroup = 1,                   // Kill する場合はプロセスグループを Kill する
        EasyInputOutputMode = 2,                // 標準出力およびエラー出力の結果を簡易的に受信し、標準入力の結果を簡易的に送信する (結果を確認しながらの対話はできない)
        EasyPrintRealtimeStdOut = 4,            // stdout データをリアルタイムで表示する
        EasyPrintRealtimeStdErr = 8,            // stderr データをリアルタイムで表示する
        UnixAutoFullPath = 16,                  // UNIX 用コマンドで自動フルパス解決をする
        DoNotConnectPipe = 32,                  // パイプを接続しない
        DoNotKill = 64,                         // Dispose 時にプロセスを Kill しない

        Default = KillProcessGroup | ExecFlags.EasyInputOutputMode | UnixAutoFullPath,
    }

    // 簡易実行
    public static class EasyExec
    {
        public static async Task<string> ExecBashRetFirstLineAsync(string command, string? currentDirectory = null,
            int easyOutputMaxSize = Consts.Numbers.DefaultLargeBufferSize, string? easyInputStr = null, int? timeout = null,
            CancellationToken cancel = default, bool debug = false, bool throwOnErrorExitCode = true,
            StrDictionary<string>? additionalEnvVars = null)
        {
            var result = await ExecBashAsync(command, currentDirectory,
                easyOutputMaxSize: easyOutputMaxSize,
                easyInputStr: easyInputStr,
                timeout: timeout,
                cancel: cancel,
                debug: debug,
                throwOnErrorExitCode: throwOnErrorExitCode,
                additionalEnvVars: additionalEnvVars);

            return result.OutputStr._GetFirstFilledLineFromLines();
        }

        public static async Task<string> ExecRetFirstLineAsync(string cmdName, string? arguments = null, string? currentDirectory = null,
            int easyOutputMaxSize = Consts.Numbers.DefaultLargeBufferSize, string? easyInputStr = null, int? timeout = null,
            CancellationToken cancel = default, bool debug = false, bool throwOnErrorExitCode = true,
            StrDictionary<string>? additionalEnvVars = null)
        {
            var result = await ExecAsync(cmdName, arguments, currentDirectory,
                easyOutputMaxSize: easyOutputMaxSize,
                easyInputStr: easyInputStr,
                timeout: timeout,
                cancel: cancel,
                debug: debug,
                throwOnErrorExitCode: throwOnErrorExitCode,
                additionalEnvVars: additionalEnvVars);

            return result.OutputStr._GetFirstFilledLineFromLines();
        }

        public static async Task<EasyExecResult> ExecBashAsync(string command, string? currentDirectory = null,
            ExecFlags flags = ExecFlags.Default | ExecFlags.EasyInputOutputMode,
            int easyOutputMaxSize = Consts.Numbers.DefaultLargeBufferSize, string? easyInputStr = null, int? timeout = null,
            CancellationToken cancel = default, bool debug = false, bool throwOnErrorExitCode = true,
            string printTag = "",
            Func<string, Task<bool>>? easyOneLineRecvCallbackAsync = null,
            Func<ReadOnlyMemory<byte>, ReadOnlyMemory<byte>, Task<bool>>? easyRealtimeRecvBufCallbackAsync = null,
            int easyRealtimeRecvBufCallbackDelayTickMsecs = 0, StrDictionary<string>? additionalEnvVars = null,
            Encoding? inputEncoding = null, Encoding? outputEncoding = null, Encoding? errorEncoding = null)
        {
            if (timeout <= 0) timeout = Timeout.Infinite;

            List<string> args = new List<string>();
            args.Add("-c");
            args.Add(command);

            ExecOptions opt = new ExecOptions(Lfs.UnixGetFullPathFromCommandName(Consts.LinuxCommands.Bash),
                args, currentDirectory, flags, easyOutputMaxSize, easyInputStr, printTag,
                easyOneLineRecvCallbackAsync, easyRealtimeRecvBufCallbackAsync, easyRealtimeRecvBufCallbackDelayTickMsecs, additionalEnvVars,
                inputEncoding, outputEncoding, errorEncoding);

            if (debug)
            {
                Dbg.WriteLine($"ExecBashAsync: --- Starting bash command \"{command}\" ---");
            }

            EasyExecResult result;

            try
            {
                using ExecInstance exec = new ExecInstance(opt);

                try
                {
                    await exec.WaitForExitAsync(timeout._NormalizeTimeout(CoresConfig.Timeouts.DefaultEasyExecTimeout), cancel);
                }
                finally
                {
                    await exec.CancelAsync();
                }

                result = new EasyExecResult(exec);
            }
            catch (Exception ex)
            {
                Dbg.WriteLine($"Error on bash process \"{command}\". Exception: {ex.Message}");
                throw;
            }

            if (debug)
            {
                Dbg.WriteLine($"ExecAsync: The result of bash \"{command}\": " + result.ToString(Str.GetCrlfStr(), false));
            }

            if (throwOnErrorExitCode)
            {
                result.ThrowExceptionIfError();
            }

            return result;
        }

        public static async Task<EasyExecResult> ExecAsync(string cmdName, string? arguments = null, string? currentDirectory = null,
            ExecFlags flags = ExecFlags.Default | ExecFlags.EasyInputOutputMode,
            int easyOutputMaxSize = Consts.Numbers.DefaultLargeBufferSize, string? easyInputStr = null, int? timeout = null,
            CancellationToken cancel = default, bool debug = false, bool throwOnErrorExitCode = true, string printTag = "",
            Func<string, Task<bool>>? easyOneLineRecvCallbackAsync = null,
            Func<ReadOnlyMemory<byte>, ReadOnlyMemory<byte>, Task<bool>>? easyRealtimeRecvBufCallbackAsync = null,
            int easyRealtimeRecvBufCallbackDelayTickMsecs = 0, StrDictionary<string>? additionalEnvVars = null,
            Encoding? inputEncoding = null, Encoding? outputEncoding = null, Encoding? errorEncoding = null)
        {
            if (timeout <= 0) timeout = Timeout.Infinite;

            ExecOptions opt = new ExecOptions(cmdName, arguments, currentDirectory, flags, easyOutputMaxSize, easyInputStr, printTag,
                easyOneLineRecvCallbackAsync, easyRealtimeRecvBufCallbackAsync, easyRealtimeRecvBufCallbackDelayTickMsecs, additionalEnvVars,
                inputEncoding, outputEncoding, errorEncoding);

            if (debug)
            {
                Dbg.WriteLine($"ExecAsync: --- Starting process \"{cmdName}{(arguments._IsFilled() ? " " : "")}{arguments}\" ---");
            }

            EasyExecResult result;

            try
            {
                await using ExecInstance exec = new ExecInstance(opt);

                try
                {
                    await exec.WaitForExitAsync(timeout._NormalizeTimeout(CoresConfig.Timeouts.DefaultEasyExecTimeout), cancel);
                }
                finally
                {
                    await exec.CancelAsync();
                }

                result = new EasyExecResult(exec);
            }
            catch (Exception ex)
            {
                Dbg.WriteLine($"Error on starting process \"{cmdName}{(arguments._IsFilled() ? " " : "")}{arguments}\". Exception: {ex.Message}");
                throw;
            }

            if (debug)
            {
                Dbg.WriteLine($"ExecAsync: The result of process \"{cmdName}{(arguments._IsFilled() ? " " : "")}{arguments}\": " + result.ToString(Str.GetCrlfStr(), false));
            }

            if (throwOnErrorExitCode)
            {
                result.ThrowExceptionIfError();
            }

            return result;
        }
    }

    public class CoresEasyExecException : CoresException
    {
        public CoresEasyExecException(EasyExecResult result)
            : base(result.ToString())
        {
        }
    }

    // 簡易実行の結果
    public class EasyExecResult
    {
        public int ExitCode => Instance.ExitCode;
        public bool IsOk => (ExitCode == 0);

        public int ProcessId => Instance.ProcessId;

        public string OutputStr { get; }
        public string ErrorStr { get; }

        ReadOnlyMemory<byte> OutputData => Instance.EasyOutputData;
        ReadOnlyMemory<byte> ErrorData => Instance.EasyErrorData;

        public string OutputAndErrorStr { get; }
        public string ErrorAndOutputStr { get; }

        public long TookTime => Instance.TookTime;

        public bool IsTimeouted => Instance.IsTimeouted;

        public ExecInstance Instance { get; }

        public EasyExecResult(ExecInstance exec)
        {
            this.Instance = exec;

            this.OutputStr = exec.EasyOutputStr._NonNull()._NormalizeCrlf(true);
            this.ErrorStr = exec.EasyErrorStr._NonNull()._NormalizeCrlf(true);

            this.OutputAndErrorStr = this.OutputStr + this.ErrorStr;
            this.ErrorAndOutputStr = this.ErrorStr + this.OutputStr;
        }

        public Exception? GetExceptionIfError()
        {
            if (this.IsOk) return null;

            return new CoresEasyExecException(this);
        }

        public void ThrowExceptionIfError()
        {
            Exception? ex = GetExceptionIfError();
            if (ex != null) throw ex;
        }

        public override string ToString()
            => ToString(", ", true);

        public string ToString(string separator, bool oneLine)
        {
            List<string> w = new List<string>();

            string argsStr;

            if (Instance.Options.ArgumentsList != null)
            {
                argsStr = Instance.Options.ArgumentsList._Combine(" ");
            }
            else
            {
                argsStr = Instance.Options.Arguments;
            }

            w.Add($"Command: \"{Instance.Options.FileName}{(argsStr._IsFilled() ? " " : "")}{argsStr}\"");
            if (Instance.ExitCode == 0)
            {
                w.Add($"Result: OK (Exit code = 0)");
            }
            else
            {
                if (this.IsTimeouted == false)
                {
                    w.Add($"Result: Error (Exit code = {ExitCode})");
                }
                else
                {
                    w.Add($"Result: Timeout Error");
                }
            }

            w.Add($"Took time: {TookTime._ToString3()}");
            w.Add($"Process Id: {ProcessId}");

            string errorStr = oneLine ? this.ErrorStr._OneLine(" / ") : this.ErrorStr;
            string outputStr = oneLine ? this.OutputStr._OneLine(" / ") : this.OutputStr;

            if (this.ErrorStr._IsFilled()) w.Add($"ErrorStr: \"{errorStr.Trim()}\"");
            if (this.OutputStr._IsFilled()) w.Add($"OutputStr: \"{outputStr.Trim()}\"");

            return w._Combine(separator);
        }
    }

    // 子プロセスの実行パラメータ
    public class ExecOptions
    {
        public string CommandName { get; }
        public string FileName { get; }
        public string Arguments { get; }
        public IEnumerable<string>? ArgumentsList { get; }
        public string CurrentDirectory { get; }
        public ExecFlags Flags { get; }
        public int EasyOutputMaxSize { get; }
        public string? EasyInputStr { get; }
        public string PrintTag { get; }
        public Func<string, Task<bool>>? EasyOneLineRecvCallbackAsync { get; }
        public Func<ReadOnlyMemory<byte>, ReadOnlyMemory<byte>, Task<bool>>? EasyRealtimeRecvBufCallbackAsync { get; }
        public int EasyRealtimeRecvBufCallbackDelayTickMsecs { get; }
        public StrDictionary<string>? AdditionalEnvVars { get; }
        public Encoding InputEncoding { get; }
        public Encoding OutputEncoding { get; }
        public Encoding ErrorEncoding { get; }

        public ExecOptions(string commandName, string? arguments = null, string? currentDirectory = null, ExecFlags flags = ExecFlags.Default,
            int easyOutputMaxSize = Consts.Numbers.DefaultLargeBufferSize, string? easyInputStr = null, string printTag = "",
            Func<string, Task<bool>>? easyOneLineRecvCallbackAsync = null,
            Func<ReadOnlyMemory<byte>, ReadOnlyMemory<byte>, Task<bool>>? easyRealtimeRecvBufCallbackAsync = null,
            int easyRealtimeRecvBufCallbackDelayTickMsecs = 0,
            StrDictionary<string>? additionalEnvVars = null,
            Encoding? inputEncoding = null, Encoding? outputEncoding = null, Encoding? errorEncoding = null)
        {
            if (flags.Bit(ExecFlags.EasyInputOutputMode) && flags.Bit(ExecFlags.DoNotKill))
            {
                throw new CoresLibException($"DoNotKill flag cannot set when EasyInputOutputMode is set");
            }

            if (flags.Bit(ExecFlags.EasyInputOutputMode) && flags.Bit(ExecFlags.DoNotConnectPipe))
            {
                throw new CoresLibException($"DoNotConnectPipe flag cannot set when EasyInputOutputMode is set");
            }

            if (flags.Bit(ExecFlags.DoNotKill) && flags.Bit(ExecFlags.DoNotConnectPipe) == false)
            {
                throw new CoresLibException($"DoNotConnectPipe flag must set when DoNotKill is set");
            }

            this.CommandName = commandName._NullCheck();
            if (Env.IsUnix == false || flags.Bit(ExecFlags.UnixAutoFullPath) == false)
            {
                this.FileName = this.CommandName._NullCheck();
            }
            else
            {
                try
                {
                    this.FileName = Lfs.UnixGetFullPathFromCommandName(this.CommandName._NullCheck());
                }
                catch
                {
                    this.FileName = this.CommandName._NullCheck();
                }
            }

            this.Arguments = arguments._NonNull();
            this.ArgumentsList = null;
            this.CurrentDirectory = currentDirectory._NonNull();
            this.Flags = flags;
            this.EasyOutputMaxSize = easyOutputMaxSize._Max(1);
            this.EasyInputStr = easyInputStr._NullIfZeroLen();
            this.PrintTag = printTag;
            this.EasyOneLineRecvCallbackAsync = easyOneLineRecvCallbackAsync;
            this.EasyRealtimeRecvBufCallbackAsync = easyRealtimeRecvBufCallbackAsync;
            this.EasyRealtimeRecvBufCallbackDelayTickMsecs = easyRealtimeRecvBufCallbackDelayTickMsecs;
            this.AdditionalEnvVars = additionalEnvVars;
            this.InputEncoding = inputEncoding ?? Env.ConsoleInputEncoding;
            this.OutputEncoding = outputEncoding ?? Env.ConsoleOutputEncoding;
            this.ErrorEncoding = errorEncoding ?? Env.ConsoleErrorEncoding;
        }

        public ExecOptions(string commandName, IEnumerable<string> argumentsList, string? currentDirectory = null, ExecFlags flags = ExecFlags.Default,
            int easyOutputMaxSize = Consts.Numbers.DefaultLargeBufferSize, string? easyInputStr = null, string printTag = "",
            Func<string, Task<bool>>? easyOneLineRecvCallbackAsync = null,
            Func<ReadOnlyMemory<byte>, ReadOnlyMemory<byte>, Task<bool>>? easyRealtimeRecvBufCallbackAsync = null,
            int easyRealtimeRecvBufCallbackDelayTickMsecs = 0, StrDictionary<string>? additionalEnvVars = null,
            Encoding? inputEncoding = null, Encoding? outputEncoding = null, Encoding? errorEncoding = null)
        {
            this.CommandName = commandName._NullCheck();
            if (Env.IsUnix == false || flags.Bit(ExecFlags.UnixAutoFullPath) == false)
            {
                this.FileName = this.CommandName._NullCheck();
            }
            else
            {
                try
                {
                    this.FileName = Lfs.UnixGetFullPathFromCommandName(this.CommandName._NullCheck());
                }
                catch
                {
                    this.FileName = this.CommandName._NullCheck();
                }
            }

            this.Arguments = "";
            this.ArgumentsList = argumentsList;
            this.CurrentDirectory = currentDirectory._NonNull();
            this.Flags = flags;
            this.EasyOutputMaxSize = easyOutputMaxSize._Max(1);
            this.EasyInputStr = easyInputStr._NullIfZeroLen();
            this.PrintTag = printTag;
            this.EasyOneLineRecvCallbackAsync = easyOneLineRecvCallbackAsync;
            this.EasyRealtimeRecvBufCallbackAsync = easyRealtimeRecvBufCallbackAsync;
            this.EasyRealtimeRecvBufCallbackDelayTickMsecs = easyRealtimeRecvBufCallbackDelayTickMsecs;
            this.AdditionalEnvVars = additionalEnvVars;
            this.InputEncoding = inputEncoding ?? Env.ConsoleInputEncoding;
            this.OutputEncoding = outputEncoding ?? Env.ConsoleOutputEncoding;
            this.ErrorEncoding = errorEncoding ?? Env.ConsoleErrorEncoding;
        }
    }

    // 実行中および実行後の子プロセスのインスタンス
    public class ExecInstance : AsyncServiceWithMainLoop
    {
        public ExecOptions Options { get; }

        public AsyncManualResetEvent ExitEvent { get; } = new AsyncManualResetEvent();

        public Encoding InputEncoding => Options.InputEncoding;
        public Encoding OutputEncoding => Options.OutputEncoding;
        public Encoding ErrorEncoding => Options.ErrorEncoding;

        PipePoint StandardPipePoint_MySide;
        PipePointStreamWrapper StandardPipeWrapper;
        PipePoint _InputOutputPipePoint;

        PipePoint ErrorPipePoint_MySide;
        PipePointStreamWrapper ErrorPipeWrapper;
        PipePoint _ErrorPipePoint;

        volatile bool EasyOutputAndErrorDataCompleted = false;
        Memory<byte> _EasyOutputData;
        Memory<byte> _EasyErrorData;

        public PipePoint InputOutputPipePoint
        {
            get
            {
                if (this.Options.Flags.Bit(ExecFlags.EasyInputOutputMode)) throw new NotSupportedException();
                return _InputOutputPipePoint;
            }
        }
        public PipePoint ErrorPipePoint
        {
            get
            {
                if (this.Options.Flags.Bit(ExecFlags.EasyInputOutputMode)) throw new NotSupportedException();
                return _ErrorPipePoint;
            }
        }

        public ReadOnlyMemory<byte> EasyOutputData
        {
            get
            {
                if (this.Options.Flags.Bit(ExecFlags.EasyInputOutputMode) == false) throw new NotSupportedException();
                if (EasyOutputAndErrorDataCompleted == false) throw new CoresException("The result string is not received yet.");
                return _EasyOutputData;
            }
        }

        public ReadOnlyMemory<byte> EasyErrorData
        {
            get
            {
                if (this.Options.Flags.Bit(ExecFlags.EasyInputOutputMode) == false) throw new NotSupportedException();
                if (EasyOutputAndErrorDataCompleted == false) throw new CoresException("The result string is not received yet.");
                return _EasyErrorData;
            }
        }

        string? _EasyOutputStr = null;
        public string EasyOutputStr
        {
            get
            {
                if (_EasyOutputStr == null)
                {
                    _EasyOutputStr = EasyOutputData._GetString(this.OutputEncoding);
                }
                return _EasyOutputStr;
            }
        }

        string? _EasyErrorStr = null;
        public string EasyErrorStr
        {
            get
            {
                if (_EasyErrorStr == null)
                {
                    _EasyErrorStr = EasyErrorData._GetString(this.ErrorEncoding);
                }
                return _EasyErrorStr;
            }
        }

        int? _ExitCode = null;

        public int ExitCode
        {
            get
            {
                if (this.Options.Flags.Bit(ExecFlags.DoNotConnectPipe) == false)
                {
                    return _ExitCode ?? throw new CoresException("Process is still running.");
                }
                else
                {
                    if (Proc.HasExited == false)
                    {
                        throw new CoresException("Process is still running.");
                    }

                    return Proc.ExitCode;
                }
            }
        }

        public int ProcessId { get; private set; }

        readonly Process Proc;

        readonly Task? EasyInputTask = null;
        readonly Task<Memory<byte>>? EasyOutputTask = null;
        readonly Task<Memory<byte>>? EasyErrorTask = null;
        readonly Task? EasyRealtimeBufferMainteTask = null;

        readonly NetAppStub? EasyInputOutputStub = null;
        readonly PipeStream? EasyInputOutputStream = null;
        readonly NetAppStub? EasyErrorStub = null;
        readonly PipeStream? EasyErrorStream = null;

        readonly CriticalSection<ExecInstance> EasyRealtimeBufLock = new CriticalSection<ExecInstance>();
        readonly MemoryBuffer<byte> EasyOutputRealtimeBuf = new MemoryBuffer<byte>();
        readonly MemoryBuffer<byte> EasyErrRealtimeBuf = new MemoryBuffer<byte>();
        readonly AsyncPulse EasyRealtimeBufLastWritePulse = new AsyncPulse();
        long EasyRealtimeBufLastWriteTick = 0;

        public long StartTick { get; }
        public long EndTick { get; private set; }

        public bool IsTimeouted
        {
            get
            {
                if (this.Options.Flags.Bit(ExecFlags.DoNotConnectPipe) == false)
                {
                    if (_ExitCode == null) return false;
                    if ((_ExitCode ?? 0) == 0) return false;
                    return _TimeoutedFlag;
                }
                else
                {
                    return false;
                }
            }
        }

        bool _TimeoutedFlag = false;

        public long TookTime
        {
            get
            {
                if (this.Options.Flags.Bit(ExecFlags.DoNotConnectPipe) == false)
                {
                    if (StartTick == 0 || EndTick == 0) return 0;

                    return EndTick - StartTick;
                }
                else
                {
                    return long.MaxValue;
                }
            }
        }

        public ExecInstance(ExecOptions options)
        {
            try
            {
                this.Options = options._NullCheck();

                ProcessStartInfo info = new ProcessStartInfo()
                {
                    FileName = options.FileName,
                    UseShellExecute = false,
                    RedirectStandardOutput = !this.Options.Flags.Bit(ExecFlags.DoNotConnectPipe),
                    RedirectStandardError = !this.Options.Flags.Bit(ExecFlags.DoNotConnectPipe),
                    RedirectStandardInput = !this.Options.Flags.Bit(ExecFlags.DoNotConnectPipe),
                    CreateNoWindow = !this.Options.Flags.Bit(ExecFlags.DoNotConnectPipe),
                    WorkingDirectory = options.CurrentDirectory._NullIfEmpty()!,
                };

                if (this.Options.AdditionalEnvVars != null)
                {
                    foreach (var kv in this.Options.AdditionalEnvVars)
                    {
                        if (kv.Key._IsFilled() && kv.Value._IsFilled())
                        {
                            info.EnvironmentVariables.Add(kv.Key, kv.Value);
                        }
                    }
                }

                if (options.ArgumentsList != null)
                {
                    options.ArgumentsList._DoForEach(a => info.ArgumentList.Add(a._NonNull()));
                }
                else
                {
                    info.Arguments = options.Arguments._NullIfZeroLen()!;
                }

                StartTick = Time.HighResTick64;

                Proc = Process.Start(info)!;

                Proc._FixProcessObjectHandleLeak(); // メモリリーク解消

                this.ProcessId = Proc.Id;

                if (this.Options.Flags.Bit(ExecFlags.DoNotConnectPipe) == false)
                {
                    // 標準入出力を接続する
                    this.StandardPipePoint_MySide = PipePoint.NewDuplexPipeAndGetOneSide(PipePointSide.A_LowerSide);
                    this._InputOutputPipePoint = this.StandardPipePoint_MySide.CounterPart._NullCheck();
                    this.StandardPipeWrapper = new PipePointStreamWrapper(StandardPipePoint_MySide, Proc.StandardOutput.BaseStream, Proc.StandardInput.BaseStream);

                    // 標準エラー出力を接続する
                    this.ErrorPipePoint_MySide = PipePoint.NewDuplexPipeAndGetOneSide(PipePointSide.A_LowerSide);
                    this._ErrorPipePoint = this.ErrorPipePoint_MySide.CounterPart._NullCheck();
                    this.ErrorPipeWrapper = new PipePointStreamWrapper(ErrorPipePoint_MySide, Proc.StandardError.BaseStream, Stream.Null);
                }
                else
                {
                    // 警告抑制
                    this.StandardPipePoint_MySide = null!;
                    this.StandardPipeWrapper = null!;
                    this.ErrorPipePoint_MySide = null!;
                    this.ErrorPipeWrapper = null!;
                    this._InputOutputPipePoint = null!;
                    this._ErrorPipePoint = null!;
                }

                if (options.Flags.Bit(ExecFlags.EasyInputOutputMode))
                {
                    this.EasyInputOutputStub = this._InputOutputPipePoint!.GetNetAppProtocolStub(noCheckDisconnected: true);
                    this.EasyInputOutputStream = this.EasyInputOutputStub.GetStream();

                    this.EasyErrorStub = this._ErrorPipePoint!.GetNetAppProtocolStub(noCheckDisconnected: true);
                    this.EasyErrorStream = this.EasyErrorStub.GetStream();

                    if (options.EasyInputStr != null && options.EasyInputStr.Length >= 1)
                    {
                        // 標準入力の注入タスク
                        this.EasyInputTask = this.EasyInputOutputStream.SendAsync(this.Options.EasyInputStr!._GetBytes(this.InputEncoding), this.GrandCancel)._LeakCheck();
                    }

                    // 標準出力の読み出しタスク
                    this.EasyOutputTask = this.EasyInputOutputStream._ReadWithMaxBufferSizeAsync(this.Options.EasyOutputMaxSize, this.GrandCancel,
                        async (data, cancel) => await EasyPrintRealTimeRecvDataCallbackAsync(data, false, cancel))._LeakCheck();

                    // 標準エラー出力の読み出しタスク
                    this.EasyErrorTask = this.EasyErrorStream._ReadWithMaxBufferSizeAsync(this.Options.EasyOutputMaxSize, this.GrandCancel,
                        async (data, cancel) => await EasyPrintRealTimeRecvDataCallbackAsync(data, true, cancel))._LeakCheck();

                    // リアルタイムバッファ監視タスク
                    if (Options.EasyRealtimeRecvBufCallbackAsync != null)
                    {
                        this.EasyRealtimeBufferMainteTask = EasyRealtimeBufferMainteTaskAsync(this.GrandCancel)._LeakCheck();
                    }
                }

                if (this.Options.Flags.Bit(ExecFlags.DoNotConnectPipe) == false)
                {
                    this.StartMainLoop(MainLoopAsync);
                }
            }
            catch
            {
                this._DisposeSafe();
                throw;
            }
        }

        PipeStreamPairWithSubTask? EasyRealTimeStdOut = null;
        PipeStreamPairWithSubTask? EasyRealTimeStdErr = null;

        PipeStreamPairWithSubTask? EasyOneLineRecvStdOut = null;
        PipeStreamPairWithSubTask? EasyOneLineRecvStdErr = null;

        // リアルタイムバッファ監視タスク
        async Task EasyRealtimeBufferMainteTaskAsync(CancellationToken cancel)
        {
            // delay time
            int delay = this.Options.EasyRealtimeRecvBufCallbackDelayTickMsecs;
            delay = Math.Max(delay, 10);

            int interval = Math.Max(delay / 10, 1);

            long lastHash = Secure.RandSInt64_Caution();
            long lastStableTick = 0;

            while (cancel.IsCancellationRequested == false && Proc.HasExited == false)
            {
                long currentHash;
                long currentTick;

                ReadOnlyMemory<byte> currentMemOutput;
                ReadOnlyMemory<byte> currentMemErr;

                lock (this.EasyRealtimeBufLock)
                {
                    MemoryBuffer<byte> tmp = new MemoryBuffer<byte>();
                    tmp.Write(this.EasyErrRealtimeBuf.Span);
                    tmp.WriteStr("----test----");
                    tmp.Write(this.EasyOutputRealtimeBuf.Span);
                    currentHash = Secure.HashSHA1AsLong(tmp.Span);
                    currentTick = this.EasyRealtimeBufLastWriteTick;

                    currentMemOutput = this.EasyOutputRealtimeBuf.Clone();
                    currentMemErr = this.EasyErrRealtimeBuf.Clone();
                }

                bool exit = false;

                if (lastHash != currentHash)
                {
                    //Dbg.Where();
                    lastHash = currentHash;
                    lastStableTick = currentTick;
                }
                else
                {
                    //Dbg.Where($"lastStableTick = {lastStableTick}, currentTick = {Time.Tick64}, lastHash = {lastHash}");
                    if ((lastStableTick + delay) <= Time.Tick64)
                    {
                        //Dbg.Where("**********");
                        try
                        {
                            bool ret = await this.Options.EasyRealtimeRecvBufCallbackAsync!(currentMemOutput, currentMemErr);
                            exit = ret;
                        }
                        catch (Exception ex)
                        {
                            ex._Debug();
                            exit = true;
                        }
                    }
                }

                if (exit)
                {
                    //Dbg.Where();
                    TaskUtil.StartAsyncTaskAsync(async () =>
                    {
                        await Task.Yield();
                        KillProcessInternal();
                        await Task.CompletedTask;
                    })._LaissezFaire(true);

                    return;
                }

                await cancel._WaitUntilCanceledAsync(interval);
            }
        }

        // リアルタイムでデータが届いたときに呼ばれるコールバック関数
        Task EasyPrintRealTimeRecvDataCallbackAsync(ReadOnlyMemory<byte> data, bool stderr, CancellationToken cancel)
        {
            if (Options.EasyRealtimeRecvBufCallbackAsync != null)
            {
                lock (this.EasyRealtimeBufLock)
                {
                    if (stderr)
                    {
                        this.EasyErrRealtimeBuf.Write(data);
                    }
                    else
                    {
                        this.EasyOutputRealtimeBuf.Write(data);
                    }

                    this.EasyRealtimeBufLastWriteTick = Time.Tick64;
                }

                this.EasyRealtimeBufLastWritePulse.FirePulse(true);
            }

            PipeStreamPairWithSubTask? pairForPrint = null;
            PipeStreamPairWithSubTask? pairForOneLineRecv = null;

            if (stderr)
            {
                if (this.Options.Flags.Bit(ExecFlags.EasyPrintRealtimeStdOut))
                {
                    pairForPrint = (this.EasyRealTimeStdErr ??= new PipeStreamPairWithSubTask(EasyPrintRealTimeRecvDataPrintCallbackAsync));
                }
            }
            else
            {
                if (this.Options.Flags.Bit(ExecFlags.EasyPrintRealtimeStdErr))
                {
                    pairForPrint = (this.EasyRealTimeStdOut ??= new PipeStreamPairWithSubTask(EasyPrintRealTimeRecvDataPrintCallbackAsync));
                }
            }

            if (this.Options.EasyOneLineRecvCallbackAsync != null)
            {
                if (stderr)
                {
                    pairForOneLineRecv = (this.EasyOneLineRecvStdErr ??= new PipeStreamPairWithSubTask(EasyRecvDataOneLineRecvCallbackAsync));
                }
                else
                {
                    pairForOneLineRecv = (this.EasyOneLineRecvStdOut ??= new PipeStreamPairWithSubTask(EasyRecvDataOneLineRecvCallbackAsync));
                }
            }

            if (pairForPrint != null)
            {
                if (pairForPrint.StreamA.IsReadyToSend())
                {
                    pairForPrint.StreamA.FastSendNonBlock(data._CloneMemory(), true);
                }
            }

            if (pairForOneLineRecv != null)
            {
                if (pairForOneLineRecv.StreamA.IsReadyToSend())
                {
                    pairForOneLineRecv.StreamA.FastSendNonBlock(data._CloneMemory(), true);
                }
            }

            return TaskCompleted;
        }

        readonly AsyncLock WriteLineLocker = new AsyncLock();

        async Task EasyPrintRealTimeRecvDataPrintCallbackAsync(PipeStream st)
        {
            var r = new BinaryLineReader(st);
            while (true)
            {
                List<Tuple<Memory<byte>, bool>>? lines = await r.ReadLinesWithTimeoutAsync(1000);

                if (lines == null)
                {
                    break;
                }

                foreach (var line in lines)
                {
                    string tagStr = "";
                    if (this.Options.PrintTag._IsFilled()) tagStr = $"{this.Options.PrintTag}: ";

                    string lineDecoded = line.Item1._GetString(this.OutputEncoding);

                    string str = tagStr + lineDecoded;

                    if (line.Item2 == false)
                    {
                        str += " [...!!pending!!...]";
                    }

                    using (await WriteLineLocker.LockWithAwait())
                    {
                        Con.WriteLine(str);
                    }
                }
            }
        }

        async Task EasyRecvDataOneLineRecvCallbackAsync(PipeStream st)
        {
            bool returnedFalseOnce = false;

            var r = new BinaryLineReader(st);
            while (true)
            {
                List<Tuple<Memory<byte>, bool>>? lines = await r.ReadLinesWithTimeoutAsync(1000);

                if (lines == null)
                {
                    break;
                }

                foreach (var line in lines)
                {
                    string lineDecoded = line.Item1._GetString(this.OutputEncoding);

                    if (returnedFalseOnce == false)
                    {
                        if (this.Options.EasyOneLineRecvCallbackAsync != null)
                        {
                            try
                            {
                                bool ret = await this.Options.EasyOneLineRecvCallbackAsync(lineDecoded);
                                if (ret == false) returnedFalseOnce = true;
                            }
                            catch (Exception ex)
                            {
                                ex._Debug();
                                returnedFalseOnce = true;
                            }

                            if (returnedFalseOnce)
                            {
                                TaskUtil.StartAsyncTaskAsync(async () =>
                                {
                                    await Task.Yield();
                                    KillProcessInternal();
                                    await Task.CompletedTask;
                                })._LaissezFaire(true);
                            }
                        }
                    }
                }
            }
        }

        async Task MainLoopAsync(CancellationToken cancel)
        {
            await Task.Yield();

            // Assert
            if (this.Options.Flags.Bit(ExecFlags.DoNotConnectPipe))
            {
                throw new CoresLibException("Fatal error. Cannot be reached here.");
            }

            try
            {
                // 終了するまで待機する (Cancel された場合は Kill されるので以下の待機は自動的に解除される)
                Proc.WaitForExit();
                if (this.EndTick == 0) this.EndTick = Time.HighResTick64;
            }
            catch (Exception ex)
            {
                if (this.EndTick == 0) this.EndTick = Time.HighResTick64;
                ex._Debug();
                throw;
            }
            finally
            {
                if (this.EndTick == 0) this.EndTick = Time.HighResTick64;

                // 終了した or 待機に失敗した
                // 戻り値をセットする
                try
                {
                    _ExitCode = Proc.ExitCode;
                }
                catch
                {
                    _ExitCode = -1;
                }

                if (this.EasyInputTask != null)
                {
                    await this.EasyInputTask._TryAwait();
                }

                try
                {
                    if (this.EasyOutputTask != null)
                    {
                        this._EasyOutputData = await this.EasyOutputTask;
                    }

                    if (this.EasyErrorTask != null)
                    {
                        this._EasyErrorData = await this.EasyErrorTask;
                    }
                }
                catch (Exception ex)
                {
                    ex._Debug();
                }

                if (this.EasyRealtimeBufferMainteTask != null)
                {
                    await this.EasyRealtimeBufferMainteTask._TryWaitAsync();
                }

                EasyOutputAndErrorDataCompleted = true;

                Interlocked.MemoryBarrier();

                // 完了フラグを立てる
                ExitEvent.Set(true);
            }
        }

        // 終了まで待機する
        public async Task<int> WaitForExitAsync(int timeout = Timeout.Infinite, CancellationToken cancel = default)
        {
            if (this.Options.Flags.Bit(ExecFlags.DoNotConnectPipe) == false)
            {
                await ExitEvent.WaitAsync(timeout, cancel);

                return this._ExitCode ?? -1;
            }
            else
            {
                if (Proc.WaitForExit(timeout) == false)
                {
                    return -1;
                }

                return Proc.ExitCode;
            }
        }
        public int WaitForExit(int timeout = Timeout.Infinite, CancellationToken cancel = default)
            => WaitForExitAsync(timeout, cancel)._GetResult();

        Once ProcessKilledOnceFlag = new Once();

        void KillProcessInternal()
        {
            if (Proc != null)
            {
                if (ProcessKilledOnceFlag.IsFirstCall())
                {
                    if (Proc.HasExited == false)
                    {
                        try
                        {
#if !NETSTANDARD
                            Proc.Kill(Options.Flags.Bit(ExecFlags.KillProcessGroup));
#else
                            Proc.Kill();
#endif
                            _TimeoutedFlag = true;
                        }
                        catch
                        {
                        }
                    }
                }
            }
        }

        protected override async Task CancelImplAsync(Exception? ex)
        {
            try
            {
                if (this.Options.Flags.Bit(ExecFlags.DoNotKill) == false)
                {
                    KillProcessInternal();
                }
            }
            finally
            {
                await base.CancelImplAsync(ex);
            }
        }


        protected override async Task CleanupImplAsync(Exception? ex)
        {
            try
            {
                await this.EasyInputOutputStub._DisposeSafeAsync();
                await this.EasyInputOutputStream._DisposeSafeAsync();

                await this.EasyErrorStub._DisposeSafeAsync();
                await this.EasyErrorStream._DisposeSafeAsync();

                await this.StandardPipeWrapper._DisposeSafeAsync();
                await this.ErrorPipeWrapper._DisposeSafeAsync();

                await this.StandardPipePoint_MySide._DisposeSafeAsync();
                await this.ErrorPipePoint_MySide._DisposeSafeAsync();

                await this.EasyRealTimeStdOut._DisposeSafeAsync();
                await this.EasyRealTimeStdErr._DisposeSafeAsync();

                await this.EasyOneLineRecvStdOut._DisposeSafeAsync();
                await this.EasyOneLineRecvStdErr._DisposeSafeAsync();
            }
            finally
            {
                await base.CleanupImplAsync(ex);
            }
        }
    }

    namespace Legacy
    {
        // 子プロセスの起動・制御用クラス
        public class ChildProcess
        {
            string stdout = "", stderr = "";
            int exitcode = -1;
            int timeout;
            Event? timeout_thread_event = null;
            Process proc;
            bool finished = false;
            bool killed = false;

            void timeout_thread(object? param)
            {
                this.timeout_thread_event!.Wait(this.timeout);

                if (finished == false)
                {
                    try
                    {
                        proc.Kill();
                        killed = true;
                    }
                    catch
                    {
                    }
                }
            }

            public string StdOut => stdout;
            public string StdErr => stderr;
            public int ExitCode => exitcode;
            public bool TimeoutKilled => killed;
            public bool IsOk => exitcode == 0;
            public bool IsError => !IsOk;

            public ChildProcess(string exe, string args = "", string input = "", bool throwExceptionOnExitError = false, int timeout = ThreadObj.Infinite)
            {
                this.timeout = timeout;

                Str.NormalizeString(ref args);

                ProcessStartInfo info = new ProcessStartInfo()
                {
                    FileName = IO.InnerFilePath(exe),
                    Arguments = args,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    RedirectStandardInput = !Str.IsEmptyStr(input),
                };

                ThreadObj? t = null;

                using (Process p = Process.Start(info)!)
                {
                    this.proc = p;

                    if (timeout != ThreadObj.Infinite)
                    {
                        timeout_thread_event = new Event();

                        t = new ThreadObj(timeout_thread);
                    }

                    if (Str.IsEmptyStr(input) == false)
                    {
                        p.StandardInput.Write(input);
                        p.StandardInput.Flush();
                        p.StandardInput.Close();
                    }

                    stdout = p.StandardOutput.ReadToEnd();
                    stderr = p.StandardError.ReadToEnd();

                    p.WaitForExit();
                    finished = true;

                    if (timeout_thread_event != null)
                    {
                        timeout_thread_event.Set();
                    }

                    if (t != null) t.WaitForEnd();

                    if (killed)
                    {
                        if (Str.IsEmptyStr(stderr))
                        {
                            stderr = $"Process run timeout ({timeout._ToString3()} msecs).";
                        }
                    }

                    exitcode = p.ExitCode;

                    if (throwExceptionOnExitError)
                    {
                        if (exitcode != 0)
                        {
                            throw new ApplicationException($"ChildProcess: '{exe}': exitcode = {exitcode}, errorstr = {stderr._OneLine()}");
                        }
                    }
                }
            }
        }
    }
}
