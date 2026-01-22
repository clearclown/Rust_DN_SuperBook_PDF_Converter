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

#pragma warning disable CA2235 // Mark all non-serializable fields

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Linq;
using System.Text;

using IPA.Cores.Basic;
using IPA.Cores.Basic.Legacy;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using System.Threading;
using System.Threading.Tasks;
using System.Collections;

namespace IPA.Cores.Basic;

// 注: DaemonCenter で利用しているためいじらないこと (名前を変えないこと、項目を減らさないこと)
[Serializable]
public class EnvInfoSnapshot
{
    public EnvInfoSnapshot() { } // 消さないこと

    public EnvInfoSnapshot(string headerText)
    {
        HeaderText = headerText;
    }

    // 注意: すべて通常の public 変数とすること (読み取り専用にしないこと)

    public string HeaderText = "";
    public DateTimeOffset TimeStamp = DateTime.Now;
    public DateTimeOffset BootTime = Env.BootTime;
    public DateTimeOffset BuildTimeStamp = Env.BuildTimeStamp;
    public string MachineName = Env.MachineName;
    public string FrameworkVersion = Env.FrameworkVersion.ToString();
    public string AppRealProcessExeFileName = Env.AppRealProcessExeFileName;
    public string AppExecutableExeOrDllFileName = Env.AppExecutableExeOrDllFileName;
    public string ApplicationNameSupposed = Env.ApplicationNameSupposed;
    public string BuildConfigurationName = Env.BuildConfigurationName;
    public string AppRootDir = Env.AppRootDir;
    public string UserName = Env.UserName;
    public string UserNameEx = Env.UserNameEx;
    public string CommandLine = Env.CommandLine;
    public bool IsAdmin = Env.IsAdmin;
    public long ProcessId = Env.ProcessId;
    public bool IsDotNetCore = Env.IsDotNetCore;
    public string ExeAssemblySimpleName = Env.ExeAssemblySimpleName;
    public string ExeAssemblyFullName = Env.ExeAssemblyFullName;
    public bool IsWindows = Env.IsWindows;
    public bool IsUnix = Env.IsUnix;
    public bool IsMac = Env.IsMac;
    public bool IsLittenEndian = Env.IsLittleEndian;
    public bool IsBigEndian = Env.IsBigEndian;
    public bool Is64BitProcess = Env.Is64BitProcess;
    public bool Is64BitWindows = Env.Is64BitWindows;
    public bool IsWow64 = Env.IsWow64;
    public Architecture CpuInfo = Env.CpuInfo;
    public string CpuInfoStr = Env.CpuInfo.ToString();
    public string FrameworkInfoString = Env.FrameworkInfoString;
    public string OsInfoString = Env.OsInfoString;
    public string SslLibInfoString = Env.SslLibInfoStr;
    public bool IsCoresLibraryDebugBuild = Env.IsCoresLibraryDebugBuild;
    public bool IsHostedByDotNetProcess = Env.IsHostedByDotNetProcess;
    public string DotNetHostProcessExeName = Env.DotNetHostProcessExeName;
    public bool IsDebuggerAttached = Env.IsDebuggerAttached;
    public int NumCpus = Env.NumCpus;
    public string DnsHostName = Env.DnsHostName;
    public string DnsDomainName = Env.DnsDomainName;
    public string DnsFqdnHostName = Env.DnsFqdnHostName;
    public string GcMode = Env.GcMode;
    public string GcCompactionMode = Env.GcCompactionMode;
    public string GcLatencyMode = Env.GcLatencyMode;
    public string WindowsFamily = Env.WindowsFamily.ToString();
    public bool IsOnGitHubActions = Env.IsOnGitHubActions;
    public string GitCoresLibCommitId = Env.GitCoresLibCommitId;
    public string GitAppCommitId = Env.GitAppCommitId;
    public string ConsoleInputEncoding = $"{Env.ConsoleInputEncoding.BodyName} / {Env.ConsoleInputEncoding.CodePage} / {Env.ConsoleInputEncoding.EncodingName}";
    public string ConsoleOutputEncoding = $"{Env.ConsoleOutputEncoding.BodyName} / {Env.ConsoleOutputEncoding.CodePage} / {Env.ConsoleOutputEncoding.EncodingName}";
    public string ConsoleErrorEncoding = $"{Env.ConsoleErrorEncoding.BodyName} / {Env.ConsoleErrorEncoding.CodePage} / {Env.ConsoleErrorEncoding.EncodingName}";
}

[Flags]
public enum SslLibFamily
{
    OpenSSL = 0,
    Win32_Schannel,
}

[Flags]
public enum WindowsFamily : long
{
    Unknown = 0,
    WindowsXP = 2600,
    WindowsServer2003 = 3790,
    WindowsVista = 6002,
    WindowsServer2008 = 6003,
    WindowsServer2008R2 = 7601,
    WindowsServer2012_Windows8 = 9200,
    WindowsServer2012R2_Windows81 = 9600,
    Windows10_1507 = 10240,
    Windows10_1511 = 10586,
    WindowsServer2016_Windows10_1607 = 14393,
    Windows10_1703 = 15063,
    Windows10_1709 = 16299,
    Windows10_1803 = 17134,
    WindowsServer2019_Windows10_1809 = 17763,
    Windows10_1903 = 18362,
    Windows10_1909 = 18363,
    Windows10_2004 = 19041,
    Windows10_20H2 = 19042,
    Windows10_21H1 = 19043,
    Windows10_21H2 = 19044,
    Windows10_22H2 = 19045,
    WindowsServer2022 = 20348,
    Windows11_21H2 = 22000,
    Windows11_22H2 = 22621,
}

public static class EnvFastOsInfo
{
    public static bool IsWindows { get; }
    public static bool IsUnix => !IsWindows;
    public static OperatingSystem OsInfo { get; }
    public static WindowsFamily WindowsFamily { get; } = WindowsFamily.Unknown;

    static readonly WindowsFamily[] WinFamilyDefs;

    static EnvFastOsInfo()
    {
        WinFamilyDefs = WindowsFamily.Unknown.GetEnumValuesList().OrderBy(x => (long)x).Distinct().ToArray();

        OsInfo = Environment.OSVersion;
        IsWindows = (OsInfo.Platform == PlatformID.Win32NT);

        if (IsWindows)
        {
            int build = OsInfo.Version.Build;

            WindowsFamily = GetWindowsVersionFromBuildNumber(build);
        }
    }

    public static WindowsFamily GetWindowsVersionFromBuildNumber(int build)
    {
        WindowsFamily ret = WindowsFamily.Unknown;

        foreach (WindowsFamily ver in WinFamilyDefs)
        {
            if ((long)build >= (long)ver)
            {
                ret = ver;
            }
        }

        return ret;
    }
}

public static class Env
{
    static object lockObj = new object();

    // 初期化の必要のあるプロパティ値
    static public Version FrameworkVersion { get; }
    public static bool IsNET4OrGreater => (FrameworkVersion.Major >= 4);
    static public string HomeDir { get; }
    static public string UnixMutantDir { get; } = "";
    static public string AppRealProcessExeFileName { get; }
    static public string AppExecutableExeOrDllFileName { get; }
    static public string BuildConfigurationName { get; }
    static public string AppExecutableExeOrDllFileDir { get; }
    static public string AppRootDir { get; }
    static public string AppLocalDir => CoresLocalDirs.AppLocalDir;
    static public string Win32_WindowsDir { get; } = "";
    static public string Win32_SystemDir { get; }
    static public string TempDir { get; }
    static public string Win32_WinTempDir { get; }
    static public string Win32_WindowsDrive { get; } = "";
    static public string Win32_ProgramFilesDir { get; }
    static public string Win32_PersonalStartMenuDir { get; }
    static public string Win32_PersonalProgramsDir { get; }
    static public string Win32_PersonalStartupDir { get; }
    static public string Win32_PersonalAppDataDir { get; }
    static public string Win32_PersonalDesktopDir { get; }
    static public string Win32_MyDocumentsDir { get; }
    static public string Win32_LocalAppDataDir { get; }
    static public string UserName { get; }
    static public string UserNameEx { get; }
    static public string MachineName { get; }
    public static string CommandLine { get; private set; }
    public static OperatingSystem OsInfo { get; }
    public static WindowsFamily WindowsFamily { get; }
    public static SslLibFamily SslLibFamily { get; }
    public static string SslLibVersion { get; }
    public static string SslLibInfoStr => (SslLibFamily.ToString() + " " + SslLibVersion).Trim();
    public static bool IsWindows { get; }
    public static bool IsUnix => !IsWindows;
    public static bool IsMac { get; }
    public static bool IsLinux { get; }
    public static bool IsLittleEndian { get; }
    public static bool IsBigEndian => !IsLittleEndian;
    public static bool IsAdmin { get; }
    public static long ProcessId { get; }
    public static string MyGlobalTempDir => CoresLocalDirs.MyGlobalTempDir;
    public static string PathSeparator { get; }
    public static char PathSeparatorChar { get; }
    public static string StartupCurrentDir { get; }
    public static bool IsDotNetCore { get; }
    public static Assembly ExeAssembly { get; }
    public static string ExeAssemblySimpleName { get; }
    public static string ExeAssemblyFullName { get; }
    public static string ApplicationNameSupposed { get; }
    public static Assembly CoresBasicLibAssembly { get; }
    public static bool IgnoreCaseInFileSystem => (IsWindows || IsMac);
    public static StrComparer FilePathStringComparer { get; }
    public static PathParser LocalPathParser => PathParser.Local;
    public static bool IsCoresLibraryDebugBuild { get; }
    public static bool IsHostedByDotNetProcess { get; }
    public static string DotNetHostProcessExeName { get; } = "";
    public static int NumCpus { get; }
    public static bool IsOnGitHubActions { get; }
    public static DateTimeOffset BuildTimeStamp { get; }

    public static string GcMode { get; } = System.Runtime.GCSettings.IsServerGC ? "ServerGC" : "WorkstationGC";
    public static string GcCompactionMode { get; } = System.Runtime.GCSettings.LargeObjectHeapCompactionMode.ToString();
    public static string GcLatencyMode { get; } = System.Runtime.GCSettings.LatencyMode.ToString();

    public static bool IsDebuggerAttached => System.Diagnostics.Debugger.IsAttached;

    public static bool Is64BitProcess => (IntPtr.Size == 8);
    public static bool Is64BitWindows => (Env.IsWindows && (Is64BitProcess || Kernel.InternalCheckIsWow64()));
    public static bool IsWow64 => Kernel.InternalCheckIsWow64();

    public static Architecture CpuInfo { get; } = RuntimeInformation.ProcessArchitecture;
    public static string FrameworkInfoString => RuntimeInformation.FrameworkDescription.Trim() + " with " + SslLibInfoStr;
    public static string OsInfoString = RuntimeInformation.OSDescription.Trim();

    public static string DnsHostName { get; }
    public static string DnsDomainName { get; }
    public static string DnsFqdnHostName { get; }

    public static DateTimeOffset BootTime { get; }

    public static string GitCoresLibCommitId { get; }
    public static string GitAppCommitId { get; }

    public static Encoding ConsoleInputEncoding => Str.ConsoleInputEncoding;
    public static Encoding ConsoleOutputEncoding => Str.ConsoleOutputEncoding;
    public static Encoding ConsoleErrorEncoding => Str.ConsoleErrorEncoding;


    // 初期化
    static Env()
    {
        BootTime = DateTimeOffset.Now;

        NumCpus = Math.Max(Environment.ProcessorCount, 1);

        int debugChecker = 0;
        Debug.Assert((++debugChecker) >= 1);
        Env.IsCoresLibraryDebugBuild = (debugChecker >= 1);

        CoresBasicLibAssembly = typeof(Env).Assembly;

        BuildTimeStamp = GetAssemblyBuildDate(CoresBasicLibAssembly);

        ExeAssembly = Assembly.GetExecutingAssembly();
        var asmName = ExeAssembly.GetName();
        ExeAssemblySimpleName = asmName.Name ?? throw new ArgumentNullException();
        ExeAssemblyFullName = asmName.FullName;

        FrameworkVersion = Environment.Version;
        IsDotNetCore = true;
        OsInfo = EnvFastOsInfo.OsInfo;
        IsWindows = EnvFastOsInfo.IsWindows;
        if (IsUnix)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                IsLinux = true;
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                IsMac = true;
            }

            UnixApi.InitUnixLimitsValue(IsMac, (IntPtr.Size == 8));
        }
        WindowsFamily = EnvFastOsInfo.WindowsFamily;

        // SSL ライブラリのバージョン番号の取得の試行
        long openSslVersion = 0;
        try
        {
            openSslVersion = UnixApi.OpenSslVersionNumber();
        }
        catch { }

        if (openSslVersion == 0)
        {
            SslLibFamily = SslLibFamily.Win32_Schannel;
            SslLibVersion = "";
        }
        else
        {
            SslLibFamily = SslLibFamily.OpenSSL;
            SslLibVersion = GenerateOpenSslVerStrFromNumber(openSslVersion);
        }

        IsOnGitHubActions = Environment.GetEnvironmentVariable("GITHUB_WORKFLOW")._IsFilled();

        PathSeparator = "" + Path.DirectorySeparatorChar;
        if (Str.IsEmptyStr(PathSeparator))
        {
            PathSeparator = "/";
            if (Environment.OSVersion.Platform == PlatformID.Win32NT) PathSeparator = "\\";
        }
        PathSeparatorChar = PathSeparator[0];
        AppRealProcessExeFileName = IO.RemoveLastEnMark(GetAppRealProcessExeFileNameInternal());

        try
        {
            AppExecutableExeOrDllFileName = IO.RemoveLastEnMark(GetAppExeOrDllImageFilePathInternal());

            if (AppExecutableExeOrDllFileName._IsSamei("<Unknown>"))
            {
                // .NET 6.0 で single file にしている場合は、何と "<Unknown>" という文字列が戻ってくる。
                // しかし、single file であることはこれで分かるので、AppExecutableExeOrDllFileName を AppRealProcessExeFileName のコピーとする。
                AppExecutableExeOrDllFileName = AppRealProcessExeFileName;
            }
        }
        catch (FileNotFoundException)
        {
            // .NET 5.0 で、single file にしている場合は、何と FileNotFoundException が発生する。
            // しかし、single file であることはこれで分かるので、AppExecutableExeOrDllFileName を AppRealProcessExeFileName のコピーとする。
            AppExecutableExeOrDllFileName = AppRealProcessExeFileName;
        }

        BuildConfigurationName = GetBuildConfigurationNameInternal();

        // dotnet プロセスによって起動されたプロセスであるか否かを判別

        // .NET Core 2.2 以前は以下の方法で判別できる
        Env.IsHostedByDotNetProcess = Path.GetFileNameWithoutExtension(Env.AppRealProcessExeFileName).Equals("dotnet", StringComparison.OrdinalIgnoreCase);

        if (Env.IsHostedByDotNetProcess)
        {
            Env.DotNetHostProcessExeName = Process.GetCurrentProcess().MainModule!.FileName!;
        }
        else
        {
            // .NET Core 3.0 以降はスタンドアロンプロセスモードでも起動できるようになった
            // この場合は、
            // 1. DOTNET_ROOT 環境変数にディレクトリ名が入っていること
            // 2. DOTNET_ROOT 環境変数 + "\dotnet" というファイル (dotnet の実行可能ファイルである) が存在すること
            // で判定を行なう
            // (TODO: Windows ではこの方法で判別ができない)

            static string? GetDotNetExeNameUnix()
            {
                List<Pair2<string, string>> list = new List<Pair2<string, string>>();
                var envs = Environment.GetEnvironmentVariables();
                foreach (DictionaryEntry env in envs)
                {
                    string name = (string)env.Key;
                    string value = (string)env.Value!;
                    if (name._IsFilled() && value._IsFilled())
                    {
                        if (name.StartsWith("DOTNET_ROOT", StringComparison.OrdinalIgnoreCase))
                        {
                            list.Add(new Pair2<string, string>(name, value));
                        }
                    }
                }

                foreach (var item in list.OrderBy(x => x.A, StrComparer.IgnoreCaseTrimComparer))
                {
                    if (Directory.Exists(item.B))
                    {
                        string exe = Path.Combine(item.B, "dotnet");
                        if (File.Exists(exe))
                        {
                            return exe;
                        }
                    }
                }

                return null;
            }

            if (IsUnix)
            {
                string? dotnetExeName = GetDotNetExeNameUnix();

                if (dotnetExeName._IsFilled())
                {
                    if (File.Exists(dotnetExeName))
                    {
                        // 判定成功
                        Env.IsHostedByDotNetProcess = true;
                        Env.DotNetHostProcessExeName = dotnetExeName;
                    }
                }
            }
        }

        // DNS ホスト名とドメイン名の取得
        PalHostNetInfo.GetHostNameAndDomainNameInfo(out string dnsHostName, out string dnsDomainName);

        DnsHostName = dnsHostName;
        DnsDomainName = dnsDomainName;
        DnsFqdnHostName = DnsHostName + (string.IsNullOrEmpty(DnsDomainName) ? "" : "." + DnsDomainName);

        if (Str.IsEmptyStr(AppExecutableExeOrDllFileName) == false)
        {
            AppRootDir = AppExecutableExeOrDllFileDir = IO.RemoveLastEnMark(System.AppContext.BaseDirectory);
            // プログラムのあるディレクトリから 1 つずつ遡ってアプリケーションの root ディレクトリを取得する
            string tmp = AppExecutableExeOrDllFileDir;

            bool isSingleFileBinary = false;

            string tmp2 = AppExecutableExeOrDllFileDir._ReplaceStr("\\", "/");
            if (tmp2._InStr("/tmp/.net/", true) || tmp2._InStr("/temp/.net/", true))
            {
                // temp ディレクトリ上で動作しておる
                isSingleFileBinary = true;
            }
            else
            {
                try
                {
                    var fi = new FileInfo(AppRealProcessExeFileName);
                    if (fi.Length >= 50_000_000)
                    {
                        // EXE ファイルのサイズが 50MB を超えている。これはきっと、 PublishSingleFile で生成されたファイルに違いない。
                        // .NET Core 3.1 では、これくらいしか 見分ける方法がありません !!
                        // https://github.com/dotnet/runtime/issues/13481
                        isSingleFileBinary = true;
                    }
                }
                catch { }
            }

            if (isSingleFileBinary)
            {
                // dotnet publish で -p:PublishSingleFile=true で生成されたファイルである。
                // この場合、AppExecutableExeOrDllFileDir は一時ディレクトリを指しているので、
                // AppRootDir は代わりに EXE ファイルのある本物のディレクトリを指すようにする。
                AppRootDir = tmp = Path.GetDirectoryName(AppRealProcessExeFileName) ?? AppRootDir;
            }

            IEnumerable<string> markerFiles = Env.IsHostedByDotNetProcess ? Consts.FileNames.AppRootMarkerFileNames : Consts.FileNames.AppRootMarkerFileNamesForBinary;

            while (true)
            {
                try
                {
                    bool found = false;

                    var filenames = Directory.GetFiles(tmp).Select(x => Path.GetFileName(x));

                    foreach (string fn in Consts.FileNames.AppRootMarkerFileNames)
                    {
                        if (filenames.Where(x => (IgnoreCaseTrim)x == fn).Any())
                        {
                            found = true;
                            break;
                        }

                        if (fn.StartsWith("."))
                        {
                            if (filenames.Where(x => x.EndsWith(fn, StringComparison.OrdinalIgnoreCase)).Any())
                            {
                                found = true;
                                break;
                            }
                        }
                    }

                    if (found)
                    {
                        AppRootDir = tmp;
                        break;
                    }
                    tmp = Path.GetDirectoryName(tmp)!;
                }
                catch
                {
                    break;
                }
            }
        }
        else
        {
            AppExecutableExeOrDllFileName = "/tmp/dummyexe";
            AppExecutableExeOrDllFileDir = "/tmp";
            AppRootDir = IO.RemoveLastEnMark(Environment.CurrentDirectory);
        }

        if (CoresLib.Caps.Bit(CoresCaps.BlazorApp))
        {
            AppExecutableExeOrDllFileName = Consts.BlazorApp.DummyImageFileName;
            AppExecutableExeOrDllFileDir = Consts.BlazorApp.DummyImageDirName;
            AppRootDir = Consts.BlazorApp.DummyImageDirName;
        }

        HomeDir = IO.RemoveLastEnMark(Kernel.GetEnvStr("HOME"));
        if (Str.IsEmptyStr(HomeDir))
        {
            HomeDir = IO.RemoveLastEnMark(Kernel.GetEnvStr("HOMEDRIVE") + Kernel.GetEnvStr("HOMEPATH"));
        }
        if (Str.IsEmptyStr(HomeDir) == false)
        {
            UnixMutantDir = Path.Combine(HomeDir, ".dotnet_temp/.Cores.NET.Mutex");
        }
        else
        {
            HomeDir = AppRootDir;
            if (IsUnix)
            {
                UnixMutantDir = Path.Combine("/tmp", ".dotnet_temp/.Cores.NET.Mutex");
            }
        }
        if (IsWindows) UnixMutantDir = "";
        if (Str.IsEmptyStr(UnixMutantDir) == false)
        {
            IO.MakeDirIfNotExists(UnixMutantDir);
        }
        if (IsWindows)
        {
            // Windows
            Win32_SystemDir = IO.RemoveLastEnMark(Environment.GetFolderPath(Environment.SpecialFolder.System));
            Win32_WindowsDir = IO.RemoveLastEnMark(Path.GetDirectoryName(Win32_SystemDir)!);
            TempDir = IO.RemoveLastEnMark(Path.GetTempPath());
            Win32_WinTempDir = IO.RemoveLastEnMark(Path.Combine(Win32_WindowsDir, "Temp"));
            IO.MakeDir(Win32_WinTempDir);
            if (Win32_WindowsDir.Length >= 2 && Win32_WindowsDir[1] == ':')
            {
                Win32_WindowsDrive = Win32_WindowsDir.Substring(0, 2).ToUpperInvariant();
            }
            else
            {
                Win32_WindowsDrive = "C:";
            }
        }
        else
        {
            // UNIX
            Win32_SystemDir = "/bin";
            Win32_WindowsDir = "/bin";
            Win32_WindowsDrive = "/";
            if (Str.IsEmptyStr(HomeDir) == false)
            {
                TempDir = Path.Combine(HomeDir, ".dotnet_temp/.Cores.NET.PerProcess.Temp");
            }
            else
            {
                TempDir = "/tmp";
            }
            Win32_WinTempDir = TempDir;
        }
        FilePathStringComparer = StrComparer.Get(!Env.IgnoreCaseInFileSystem);
        Win32_ProgramFilesDir = IO.RemoveLastEnMark(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
        Win32_PersonalStartMenuDir = IO.RemoveLastEnMark(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu));
        Win32_PersonalProgramsDir = IO.RemoveLastEnMark(Environment.GetFolderPath(Environment.SpecialFolder.Programs));
        Win32_PersonalStartupDir = IO.RemoveLastEnMark(Environment.GetFolderPath(Environment.SpecialFolder.Startup));
        Win32_PersonalAppDataDir = IO.RemoveLastEnMark(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
        Win32_PersonalDesktopDir = IO.RemoveLastEnMark(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
        Win32_MyDocumentsDir = IO.RemoveLastEnMark(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        Win32_LocalAppDataDir = IO.RemoveLastEnMark(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
        if (IsUnix)
        {
            // ダミーディレクトリ
            Win32_SystemDir = "/bin";
            Win32_WindowsDir = "/bin";
            Win32_WindowsDrive = "/";
            Win32_ProgramFilesDir = "/bin";
            Win32_PersonalStartMenuDir = Path.Combine(HomeDir, "dummy/starmenu");
            Win32_PersonalProgramsDir = Path.Combine(HomeDir, "dummy/starmenu/programs");
            Win32_PersonalStartupDir = Path.Combine(HomeDir, "dummy/starmenu/startup");
            Win32_LocalAppDataDir = Win32_PersonalAppDataDir = Path.Combine(HomeDir, ".dnappdata");
            Win32_PersonalDesktopDir = Path.Combine(HomeDir, "dummy/desktop");
            Win32_MyDocumentsDir = HomeDir;
        }
        StartupCurrentDir = CurrentDir;
        UserName = Environment.UserName;
        try
        {
            UserNameEx = Environment.UserDomainName + "\\" + UserName;
        }
        catch
        {
            UserNameEx = UserName;
        }
        MachineName = Environment.MachineName;

        CommandLine = GetRealCommandLineArgsStrFromProcessCmdLineStr(Environment.CommandLine);

        IsLittleEndian = BitConverter.IsLittleEndian;
        ProcessId = System.Diagnostics.Process.GetCurrentProcess().Id;
        IsAdmin = CheckIsAdmin();

        if (IsUnix)
        {
            MutantUnixImpl.DeleteUnusedMutantFiles();
        }

        ApplicationNameSupposed = Path.GetFileNameWithoutExtension(AppExecutableExeOrDllFileName);
        if (ApplicationNameSupposed._IsEmpty()) ApplicationNameSupposed = "_unknown_";

        GitCoresLibCommitId = Dbg.GetCurrentCoresLibGitCommitId();
        GitAppCommitId = Dbg.GetCurrentGitCommitId();
    }

    public static string GenerateOpenSslVerStrFromNumber(long ver)
    {
        uint verint = (uint)ver;

        uint ver_major = (verint >> 28) & 0x0F;
        uint ver_minor = (verint >> 20) & 0xFF;
        uint ver_fix = (verint >> 12) & 0xFF;
        uint ver_patch = (verint >> 4) & 0xFF;

        if (ver_major >= 3)
        {
            return $"{ver_major}.{ver_minor}.{ver_patch}";
        }
        else
        {
            char c = ' ';
            if (ver_patch >= 1)
            {
                c = (char)('a' + (ver_patch - 1));
            }
            return $"{ver_major}.{ver_minor}.{ver_fix}{c}".Trim();
        }
    }

    public static string MyLocalTempDir => CoresLocalDirs.MyLocalTempDir;

    // 現在のユーザーが管理者権限を有するかどうか確認をする
    static bool CheckIsAdmin()
    {
        if (Env.IsUnix == false)
        {
            // Windows
            return Win32ApiUtil.IsUserAnAdmin();
        }
        else
        {
            // Unix: 現在のユーザー名が「root」であるかどうかで判別をする
            return Env.UserName._IsSamei(Consts.Strings.RootUsername);
        }
    }

    internal static void _SetCommandLineInternal(string cmdLine)
    {
        Env.CommandLine = cmdLine;
    }

    public static string GetRealCommandLineArgsStrFromProcessCmdLineStr(string src)
    {
        try
        {
            int i;
            // 実行可能ファイル本体の部分を除去する
            if (src.Length >= 1 && src[0] == '\"')
            {
                i = src.IndexOf('\"', 1);
            }
            else
            {
                i = src.IndexOf(' ');
            }

            if (i == -1)
            {
                return "";
            }
            else
            {
                return src.Substring(i + 1).TrimStart(' ');
            }
        }
        catch
        {
            return "";
        }
    }

    static string GetBuildConfigurationNameInternal()
    {
        if (CoresLib.Caps.Bit(CoresCaps.BlazorApp))
        {
            return Consts.BlazorApp.DummyBuildConfigurationName;
        }

        Assembly mainAssembly = Assembly.GetEntryAssembly()!;
        return (string)mainAssembly.CustomAttributes.Where(x => x.AttributeType == typeof(AssemblyConfigurationAttribute))
            .First()
            .ConstructorArguments[0].Value!;
    }

#pragma warning disable IL3002 // Using member 'System.Reflection.Module.FullyQualifiedName' which has 'RequiresAssemblyFilesAttribute' can break functionality when embedded in a single-file app. Returns <Unknown> for modules with no file path.
    static string GetAppExeOrDllImageFilePathInternal()
    {
        if (CoresLib.Caps.Bit(CoresCaps.BlazorApp))
        {
            return Consts.BlazorApp.DummyImageFileName;
        }

        Assembly mainAssembly = Assembly.GetEntryAssembly()!;
        Module[] modules = mainAssembly.GetModules();
        return modules[0].FullyQualifiedName;
    }
#pragma warning restore IL3002 // Using member 'System.Reflection.Module.FullyQualifiedName' which has 'RequiresAssemblyFilesAttribute' can break functionality when embedded in a single-file app. Returns <Unknown> for modules with no file path.

    static string GetAppRealProcessExeFileNameInternal()
    {
        try
        {
            Process myProcess = Process.GetCurrentProcess();

            Process myProcess2 = Process.GetProcessById(myProcess.Id);

            return myProcess2.MainModule!.FileName!;
        }
        catch
        {
            throw new SystemException("GetAppRealProcessExeFileNameInternal: Failed to obtain the path.");
        }
    }

    // 初期化の必要のないプロパティ値
    static public string CurrentDir => IO.RemoveLastEnMark(Environment.CurrentDirectory);
    static public string NewLine => Environment.NewLine;

    public static void PutGitIgnoreFileOnAppLocalDirectory()
    {
        Util.PutGitIgnoreFileOnDirectory(Lfs.PathParser.Combine(Env.AppLocalDir));
    }

    public static KeyValueList<string, string> GetCoresEnvValuesList()
    {
        KeyValueList<string, string> vals = new KeyValueList<string, string>();

        vals.Add("BuildConfigurationName", Env.BuildConfigurationName);
        vals.Add("FrameworkInfoString", Env.FrameworkInfoString);
        vals.Add("OsInfoString", Env.OsInfoString);
        vals.Add("CpuInfo", Env.CpuInfo.ToString());
        vals.Add("NumCpus", Env.NumCpus.ToString());
        vals.Add("UserNameEx", Env.UserNameEx);
        vals.Add("MachineName", Env.MachineName);
        vals.Add("IsWindows", Env.IsWindows.ToString());
        vals.Add("IsUnix", Env.IsUnix.ToString());
        vals.Add("IsMac", Env.IsMac.ToString());
        vals.Add("Is64BitProcess", Env.Is64BitProcess.ToString());
        vals.Add("IsWow64", Env.IsWow64.ToString());
        vals.Add("IsUnix", Env.IsUnix.ToString());
        vals.Add("IsHostedByDotNetProcess", Env.IsHostedByDotNetProcess.ToString());
        vals.Add("GcMode", Env.GcMode.ToString());
        vals.Add("GcCompactionMode", Env.GcCompactionMode.ToString());
        vals.Add("GcLatencyMode", Env.GcLatencyMode.ToString());
        vals.Add("AppRealProcessExeFileName", Env.AppRealProcessExeFileName);
        vals.Add("AppExecutableExeOrDllFileName", Env.AppExecutableExeOrDllFileName);
        vals.Add("AppExecutableExeOrDllFileDir", Env.AppExecutableExeOrDllFileDir);
        vals.Add("AppRootDir", Env.AppRootDir);

        ThreadPool.GetMinThreads(out int minWorkerThreads, out int minCompletionPortThreads);
        ThreadPool.GetMaxThreads(out int maxWorkerThreads, out int maxCompletionPortThreads);

        vals.Add("MinThreads", $"WorkerThreads = {minWorkerThreads}, CompletionPortThreads = {minCompletionPortThreads}");
        vals.Add("MaxThreads", $"WorkerThreads = {maxWorkerThreads}, CompletionPortThreads = {maxCompletionPortThreads}");

        return vals;
    }

    // Thanks to: https://www.meziantou.net/getting-the-date-of-build-of-a-dotnet-assembly-at-runtime.htm
    static DateTimeOffset GetAssemblyBuildDate(Assembly assembly)
    {
        try
        {
            const string BuildVersionMetadataPrefix = "+build";

            var attribute = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            if (attribute?.InformationalVersion != null)
            {
                var value = attribute.InformationalVersion;
                var index = value.IndexOf(BuildVersionMetadataPrefix);
                if (index > 0)
                {
                    value = value.Substring(index + BuildVersionMetadataPrefix.Length);

                    DateTime dt = Str.StrToDateTime(value, emptyToZeroDateTime: true);

                    return dt._AsDateTimeOffset(false).ToOffset(Consts.Numbers.JapanStandardTimeOffset);
                }
            }
        }
        catch { }

        return Util.ZeroDateTimeOffsetValue;
    }

    public static async Task<MyProgramAndEnvironmentReport> GenerateMyProgramAndEnvironmentReport(CancellationToken cancel = default)
    {
        MyProgramAndEnvironmentReport ret = new MyProgramAndEnvironmentReport();
        await ret.UpdateAsync(cancel);
        return ret;
    }
}

public static class CoresLocalDirs
{
    static readonly CriticalSection MyLocalTempDirInitLock = new CriticalSection();
    public static readonly StaticModule Module = new StaticModule(InitModule, FreeModule);

    static string _MyLocalTempDir = "";

    public static string AppLocalDir { get; private set; } = "";
    public static string AppRootLocalTempDirRoot_Internal { get; private set; } = "";
    public static string MyGlobalTempDir { get; private set; } = "";

    static string LocalDir = "";

    static void InitModule()
    {
        string dirPrefix = "App";

        if (CoresLib.Mode == CoresMode.Library) dirPrefix = "Lib";

        LocalDir = Path.Combine(Env.AppRootDir, "Local");

        AppLocalDir = Path.Combine(LocalDir, $"{dirPrefix}_{CoresLib.AppNameFnSafe}");

        AppRootLocalTempDirRoot_Internal = Path.Combine(AppLocalDir, "Temp");

        _MyLocalTempDir = "";

        if (CoresLib.Caps.Bit(CoresCaps.BlazorApp) == false)
        {
            // Global app temp dir
            if (MyGlobalTempDir._IsEmpty())
            {
                SystemUniqueDirectoryProvider myGlobalTempDirProvider = new SystemUniqueDirectoryProvider(Path.Combine(Env.TempDir, "Cores.NET.Temp"), $"{dirPrefix}_{CoresLib.AppNameFnSafe}");
                MyGlobalTempDir = myGlobalTempDirProvider.CurrentDirPath;
            }
        }
    }

    public static void CreateLocalDirGitIgnore()
    {
        Util.PutGitIgnoreFileOnDirectory(LocalDir);
    }

    static void FreeModule()
    {
    }

    public static string MyLocalTempDir
    {
        get
        {
            if (_MyLocalTempDir._IsFilled()) return _MyLocalTempDir;

            lock (MyLocalTempDirInitLock)
            {
                if (_MyLocalTempDir._IsEmpty())
                {
                    // Local app temp dir
                    SystemUniqueDirectoryProvider myLocalTempDirProvider = new SystemUniqueDirectoryProvider(AppRootLocalTempDirRoot_Internal, CoresLib.AppNameFnSafe);
                    _MyLocalTempDir = myLocalTempDirProvider.CurrentDirPath;

                    Env.PutGitIgnoreFileOnAppLocalDirectory();
                }

                return _MyLocalTempDir;
            }
        }
    }
}


public class MyProgramAndEnvironmentReport
{
    public string? ProgramName;
    public string? ProgramExePath;
    public DateTimeOffset BuildTimeStamp;
    public string? LocalFQDN;
    public string? GlobalIPv4;
    public string? GlobalFQDNv4;
    public string? GlobalIPv6;
    public string? GlobalFQDNv6;
    public string? LocalIPv4;
    public string? OsInfo;
    public string? DotNetVersion;
    public string? CoresLibGitCommitId;
    public string? AppGitCommitId;

    public MyProgramAndEnvironmentReport()
    {
    }

    public async Task UpdateAsync(CancellationToken cancel = default)
    {
        this.ProgramName = CoresLib.AppName._NullIfEmpty(); ;
        this.ProgramExePath = Env.AppExecutableExeOrDllFileName._NullIfEmpty();
        this.BuildTimeStamp = Env.BuildTimeStamp;
        this.LocalFQDN = Env.DnsFqdnHostName._NullIfEmpty();

        try
        {
            this.LocalIPv4 = (await GetMyPrivateIpNativeUtil.GetMyPrivateIpAsync(IPVersion.IPv4)).ToString();
        }
        catch { }

        await using GetMyIpClient c = new GetMyIpClient();

        try
        {
            var v4info = await c.GetMyIpInfoAsync(IPVersion.IPv4, cancel);
            GlobalIPv4 = v4info.GlobalIpAddress.ToString()._NullIfEmpty();
            GlobalFQDNv4 = v4info.GlobalFqdn.ToString()._NullIfEmpty();
        }
        catch
        {
        }

        try
        {
            var v6info = await c.GetMyIpInfoAsync(IPVersion.IPv6, cancel);
            GlobalIPv6 = v6info.GlobalIpAddress.ToString()._NullIfEmpty();
            GlobalFQDNv6 = v6info.GlobalFqdn.ToString()._NullIfEmpty();
        }
        catch
        {
        }

        CoresLibGitCommitId = Env.GitCoresLibCommitId._NullIfEmpty();
        AppGitCommitId = Env.GitAppCommitId._NullIfEmpty();
        OsInfo = Env.OsInfoString._NullIfEmpty();
        DotNetVersion = Env.FrameworkInfoString._NullIfEmpty();
    }
}
