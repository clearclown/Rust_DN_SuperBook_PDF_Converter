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

namespace IPA.Cores.Basic;

public partial class LocalFileSystem
{
    public DirectoryPath? DetermineRootPathWithMarkerFile(FilePath sampleFilePath, string markerFileName, string stopSearchFileExtensions = Consts.FileNames.DefaultStopRootSearchFileExtsForSafety)
    {
        IEnumerable<string> markerFiles = Env.IsHostedByDotNetProcess ? Consts.FileNames.AppRootMarkerFileNames : Consts.FileNames.AppRootMarkerFileNamesForBinary;

        try
        {
            DirectoryPath currentDir = sampleFilePath.GetParentDirectory();

            while (currentDir.IsRootDirectory == false)
            {
                FileSystemEntity[] elements = currentDir.EnumDirectory(flags: EnumDirectoryFlags.NoGetPhysicalSize);

                if (elements.Where(x => x.IsFile && x.Name._IsSamei(markerFileName)).Any())
                {
                    // Found
                    return currentDir;
                }

                if (elements.Where(x => x.IsFile && x.Name._IsExtensionMatch(stopSearchFileExtensions)).Any())
                {
                    return null;
                }

                if (elements.Where(x => x.IsFile && markerFiles.Where(marker => marker._IsSamei(x.Name)).Any()).Any())
                {
                    return null;
                }

                if (elements.Where(x => x.IsFile && markerFiles.Where(marker => marker.StartsWith(".") && x.Name.EndsWith(marker, StringComparison.OrdinalIgnoreCase)).Any()).Any())
                {
                    return null;
                }

                currentDir = currentDir.GetParentDirectory();
            }
        }
        catch// (Exception ex)
        {
            //ex._Debug();
        }

        return null;
    }

    public DirectoryPath ConfigPathStringToPhysicalDirectoryPath(string pathString)
    {
        pathString = pathString._NonNullTrim();

        if (pathString._IsEmpty())
            throw new ArgumentNullException(nameof(pathString));

        pathString = PathParser.NormalizeDirectorySeparator(pathString, true);

        if (PathParser.IsAbsolutePath(pathString))
        {
            return pathString;
        }
        else
        {
            pathString = "/" + PathParser.Linux.NormalizeDirectorySeparator(pathString, true);

            string[] elements = PathParser.Linux.SplitAbsolutePathToElementsUnixStyle(pathString);

            string tmp = PathParser.Linux.BuildAbsolutePathStringFromElements(elements);

            tmp = PathParser.Linux.NormalizeDirectorySeparator(tmp, true);

            if (tmp[0] != '/')
                throw new ApplicationException("tmp[0] != '/'");

            tmp = tmp.Substring(1);

            tmp = PathParser.Combine(Env.AppRootDir, tmp, true);

            return tmp;
        }
    }

    static int TempFileSeqNo = 0;

    public string SaveToTempFile(string ext, ReadOnlyMemory<byte> data, long lifeTimeMsecs = Consts.Timeouts.GcTempDefaultFileLifeTime, CancellationToken cancel = default)
    {
        string tmpDirPath = Env.MyGlobalTempDir._CombinePath("_tmpfiles");
        DateTime now = DateTime.Now;
        DateTime expires;

        if (lifeTimeMsecs <= 0)
        {
            expires = Util.MaxDateTimeValue;
        }
        else
        {
            expires = now.AddMilliseconds(lifeTimeMsecs);
        }

        if (ext._IsEmpty()) ext = "dat";

        if (ext.StartsWith(".") == false) ext = "." + ext;

        int seqNo = Interlocked.Increment(ref TempFileSeqNo);

        if ((seqNo % 100) == 0)
        {
            try
            {
                GcTempFile();
            }
            catch { }
        }

        string fn = $"{seqNo:D8}-{Str.DateTimeToStrShortWithMilliSecs(expires)}{ext}";

        string filePath = tmpDirPath._CombinePath(fn);

        this.WriteDataToFile(filePath, data, flags: FileFlags.AutoCreateDirectory, cancel: cancel);

        return filePath;
    }

    void GcTempFile()
    {
        string tmpDirPath = Env.MyGlobalTempDir._CombinePath("_tmpfiles");

        if (this.IsDirectoryExists(tmpDirPath) == false) return;

        DateTime now = DateTime.Now;

        List<string> deleteList = new List<string>();

        foreach (var e in this.EnumDirectory(tmpDirPath, flags: EnumDirectoryFlags.NoGetPhysicalSize).Where(x => x.IsFile))
        {
            try
            {
                string fn = e.Name;
                if (Str.GetKeyAndValue(fn, out _, out string dateTimeAndExt, "-"))
                {
                    string dateTimeStr = this.PathParser.GetFileNameWithoutExtension(dateTimeAndExt, false);

                    DateTime expires = Str.StrToDateTime(dateTimeStr);

                    if (now >= expires)
                    {
                        deleteList.Add(e.FullPath);
                    }
                }
            }
            catch { }
        }

        foreach (var path in deleteList)
        {
            try
            {
                this.DeleteFile(path);
            }
            catch { }
        }
    }

    readonly Dictionary<string, string> CommandNamdToFullPathCache = new Dictionary<string, string>();

    // UNIX におけるコマンド名からフルパスを取得する (指定されたファイル名がフルパスとして見つからない場合)
    public string UnixGetFullPathFromCommandName(string commandName)
    {
        if (Env.IsUnix == false) throw new ArgumentException("Env.IsUnix == false");
        if (commandName._IsEmpty()) throw new ArgumentException(nameof(commandName));

        commandName = commandName.Trim();

        if (this.PathParser.IsAbsolutePath(commandName))
        {
            if (Lfs.IsFileExists(commandName))
            {
                // 最初から指定されたフルパスが存在する
                return commandName;
            }

            // 指定されたフルパスにファイルが存在しない。コマンド名だけを取り出して解決しようと試みる。
            commandName = this.PathParser.GetFileName(commandName);
        }

        lock (CommandNamdToFullPathCache)
        {
            return CommandNamdToFullPathCache._GetOrNew(commandName, () =>
            {
                string fullPath = UnixGetFullPathFromCommandNameInternal(commandName);

                if (fullPath._IsEmpty())
                {
                    throw new CoresException($"The full path of UNIX command '{commandName}' not found.");
                }

                return fullPath;
            });
        }
    }

    string UnixGetFullPathFromCommandNameInternal(string commandName)
    {
        if (Env.IsUnix == false) throw new ArgumentException("Env.IsUnix == false");
        if (commandName._IsEmpty()) throw new ArgumentException(nameof(commandName));

        try
        {
            foreach (var dir in Consts.LinuxPaths.BasicBinDirList)
            {
                string fpath = this.PathParser.Combine(dir, commandName);

                if (this.IsFileExists(fpath))
                {
                    return fpath;
                }
            }
        }
        catch { }

        return "";
    }
}

