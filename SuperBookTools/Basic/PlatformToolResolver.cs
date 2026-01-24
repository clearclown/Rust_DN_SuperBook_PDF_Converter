#pragma warning disable CA2235 // Mark all non-serializable fields

using System;
using System.IO;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

namespace SuperBookTools;

/// <summary>
/// Linux/Windows クロスプラットフォーム対応のためのツールパス解決クラス
/// </summary>
public static class PlatformToolResolver
{
    /// <summary>
    /// 現在のプラットフォームが Linux かどうか
    /// </summary>
    public static bool IsLinux => Env.IsLinux;

    /// <summary>
    /// 現在のプラットフォームが Windows かどうか
    /// </summary>
    public static bool IsWindows => Env.IsWindows;

    /// <summary>
    /// 現在のプラットフォームが macOS かどうか
    /// </summary>
    public static bool IsMac => Env.IsMac;

    /// <summary>
    /// シェルコマンドを取得（Windows: cmd.exe, Linux/Mac: /bin/bash）
    /// </summary>
    public static string GetShellCommand()
    {
        if (IsWindows)
        {
            return Path.Combine(Env.Win32_SystemDir, "cmd.exe");
        }
        return "/bin/bash";
    }

    /// <summary>
    /// シェルコマンドの引数プレフィックスを取得（Windows: /c, Linux/Mac: -c）
    /// </summary>
    public static string GetShellArgumentPrefix()
    {
        return IsWindows ? "/c" : "-c";
    }

    /// <summary>
    /// Python仮想環境のactivateスクリプトパスを取得
    /// </summary>
    /// <param name="venvBasePath">仮想環境のベースパス</param>
    public static string GetVenvActivateCommand(string venvBasePath)
    {
        if (IsWindows)
        {
            return Path.Combine(venvBasePath, @"venv\Scripts\activate");
        }
        // Linux/Mac: source コマンドを使用
        return $"source {Path.Combine(venvBasePath, "venv/bin/activate")}";
    }

    /// <summary>
    /// Windows用パスとLinux用パスを解決
    /// </summary>
    /// <param name="winPath">Windows用パス</param>
    /// <param name="linuxPath">Linux用パス（省略時はシステムPATHから検索）</param>
    public static string ResolveToolPath(string winPath, string? linuxPath = null)
    {
        if (IsWindows)
        {
            return winPath;
        }

        // Linux/Mac
        if (!string.IsNullOrEmpty(linuxPath) && File.Exists(linuxPath))
        {
            return linuxPath;
        }

        // システムPATHから検索を試みる
        string toolName = Path.GetFileNameWithoutExtension(winPath);
        string? pathFromEnv = FindInPath(toolName);
        if (pathFromEnv != null)
        {
            return pathFromEnv;
        }

        // 見つからない場合は、Windows拡張子を除いたパスを返す
        return winPath.Replace(".exe", "");
    }

    /// <summary>
    /// ImageMagick の magick コマンドパスを解決
    /// </summary>
    /// <param name="winPath">Windows用の実行ファイルパス</param>
    public static string ResolveMagickPath(string winPath)
    {
        return ResolveToolPath(winPath, "/usr/bin/magick");
    }

    /// <summary>
    /// ExifTool のパスを解決
    /// </summary>
    /// <param name="winPath">Windows用の実行ファイルパス</param>
    public static string ResolveExifToolPath(string winPath)
    {
        return ResolveToolPath(winPath, "/usr/bin/exiftool");
    }

    /// <summary>
    /// QPDF のパスを解決
    /// </summary>
    /// <param name="winPath">Windows用の実行ファイルパス</param>
    public static string ResolveQpdfPath(string winPath)
    {
        return ResolveToolPath(winPath, "/usr/bin/qpdf");
    }

    /// <summary>
    /// pdfcpu のパスを解決
    /// </summary>
    /// <param name="winPath">Windows用の実行ファイルパス</param>
    public static string ResolvePdfcpuPath(string winPath)
    {
        return ResolveToolPath(winPath, "/usr/bin/pdfcpu");
    }

    /// <summary>
    /// システムPATH環境変数からツールを検索
    /// </summary>
    private static string? FindInPath(string toolName)
    {
        string? pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathEnv))
        {
            return null;
        }

        char pathSeparator = IsWindows ? ';' : ':';
        string[] paths = pathEnv.Split(pathSeparator);

        foreach (string path in paths)
        {
            string fullPath = Path.Combine(path, toolName);
            if (File.Exists(fullPath))
            {
                return fullPath;
            }

            // Linux/Mac でも念のため拡張子なしを確認
            if (!IsWindows)
            {
                if (File.Exists(fullPath))
                {
                    return fullPath;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// パス区切り文字をプラットフォームに合わせて正規化
    /// </summary>
    public static string NormalizePath(string path)
    {
        if (IsWindows)
        {
            return path.Replace('/', '\\');
        }
        return path.Replace('\\', '/');
    }
}
