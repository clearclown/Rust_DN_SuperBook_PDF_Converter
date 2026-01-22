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

#pragma warning disable CA1063 // Implement IDisposable Correctly

using System;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

using IPA.Cores.Basic;
using IPA.Cores.Basic.Legacy;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

namespace IPA.Cores.Basic
{
    // 古いファイルから順番に削除する
    public class OldFileEraser : AsyncServiceWithMainLoop
    {
        string[] DirList;
        string ExtensionList;
        public long TotalMinSize { get; }

        public const int DefaultInterval = 60 * 60 * 1000;
        public int Interval { get; }

        public OldFileEraser(long totalMinSize, string[] dirs, string extensions = "*", int interval = DefaultInterval)
            : base()
        {
            this.Interval = interval;
            List<string> tmp = new List<string>();
            foreach (string dir in dirs) tmp.Add(IO.InnerFilePath(dir));
            this.DirList = tmp.ToArray();

            this.ExtensionList = extensions;
            this.TotalMinSize = totalMinSize;

            StartMainLoop(MainLoopAsync);
        }

        // 定期的に削除を実行するスレッド
        public async Task MainLoopAsync(CancellationToken cancel)
        {
            while (await cancel._WaitUntilCanceledAsync(Util.GenRandInterval(this.Interval)) == false)
            {
                ProcessNow(this.DirList, this.ExtensionList, this.TotalMinSize, cancel);
            }
        }

        // すぐに削除実行
        public static void ProcessNow(string[] dirList, string extensionList, long maxTotalSize, CancellationToken cancel = default(CancellationToken))
        {
            try
            {
                // 列挙
                List<DirEntry> list = IO.EnumDirsWithCancel(dirList, extensionList, cancel)!;

                // 更新日時でソート
                list.Sort((x, y) => (x.UpdateDate.CompareTo(y.UpdateDate)));

                // 合計サイズを取得
                long total_size = 0;
                foreach (var v in list) if (v.IsFolder == false) total_size += v.FileSize;

                List<DirEntry> delete_files = new List<DirEntry>();

                // 削除をしていきます
                long delete_size = total_size - maxTotalSize;
                foreach (var v in list)
                {
                    if (delete_size <= 0) break;
                    if (v.IsFolder == false)
                    {
                        try
                        {
                            File.Delete(v.FullPath);

                            //Dbg.WriteLine($"OldFileEraser: File '{v.FullPath}' deleted.");

                            // 削除に成功したら delete_size を減じる
                            delete_size -= v.FileSize;

                            // 親ディレクトリが削除できる場合は削除する
                            // (検索した対象ディレクトリは削除しない)
                            if (v.RelativePath._FindStringsMulti(0, StringComparison.OrdinalIgnoreCase, out _, "\\", "/") != -1)
                            {
                                Directory.Delete(v.FullPath._GetDirectoryName()!);

                                //Dbg.WriteLine($"OldFileEraser: Directory '{v.FullPath._GetDirectoryName()}' deleted.");
                            }
                        }
                        catch
                        {
                        }
                    }
                }
            }
            catch
            {
            }
        }
    }

    public class SystemUniqueDirectoryProvider
    {
        public string SubDirNamePrefix { get; }
        public string BaseDirPath { get; }
        public string CurrentDirPath { get; }
        public int DirLength { get; }

        public SystemUniqueDirectoryProvider(string baseDirPath, string subDirNamePrefix)
        {
            this.SubDirNamePrefix = subDirNamePrefix._NonNullTrim();
            this.BaseDirPath = Path.GetFullPath(baseDirPath);

            DirLength = SubDirNamePrefix.Length + 1 + 8;

            Exception lastException = new CoresException("Init");

            DeleteUnusedDirectories();

            for (int i = 0; ; i++)
            {
                if (i >= CoresConfig.BasicConfig.MaxPossibleConcurrentProcessCounts)
                {
                    throw new ApplicationException($"UniqueDirectoryProvider error: failed to obtain the unique path. baseDirPath = \"{baseDirPath}\", subDirNamePrefix = \"{subDirNamePrefix}\", lastException = {lastException.ToString()}");
                }

                string candidate = Path.Combine(this.BaseDirPath, $"{SubDirNamePrefix}_{i:D8}");

                try
                {
                    if (Directory.Exists(candidate))
                    {
                        lastException = new CoresException($"Directory '{candidate}' already exists");
                        continue;
                    }
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    continue;
                }

                SingleInstance? si = SingleInstance.TryGet(GenerateSingleInstanceName(candidate), true);

                if (si != null)
                {
                    // Add the SingleInstance object to the blackhole so that it will not be the target of GC.
                    Util.AddToBlackhole(si);

                    try
                    {
                        Directory.CreateDirectory(candidate);
                        this.CurrentDirPath = candidate;
                        break;
                    }
                    catch (Exception ex)
                    {
                        lastException = ex;
                    }
                }
                else
                {
                    lastException = new CoresException($"Directory '{candidate}': failed to obtain single instance");
                }
            }
        }

        void DeleteUnusedDirectories()
        {
            try
            {
                var dirs = Directory.EnumerateDirectories(this.BaseDirPath);

                foreach (string dirFullPath in dirs)
                {
                    try
                    {
                        string dirName = Path.GetFileName(dirFullPath);

                        if (dirName.StartsWith(this.SubDirNamePrefix, StringComparison.OrdinalIgnoreCase))
                        {
                            if (dirName.Length == this.DirLength)
                            {
                                if (SingleInstance.IsExistsAndLocked(GenerateSingleInstanceName(dirFullPath), true) == false)
                                {
                                    try
                                    {
                                        Directory.Delete(dirFullPath, true);
                                    }
                                    catch { }
                                }
                            }
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        string GenerateSingleInstanceName(string candidateFullPath)
        {
            return $"UniqueDirectoryProvider_" + candidateFullPath._NonNullTrim().ToLowerInvariant();
        }
    }

    namespace Legacy
    {
        // HamCore エントリ
        public class HamCoreEntry : IComparable
        {
            public string FileName = "";
            public uint Size = 0;
            public uint SizeCompressed = 0;
            public uint Offset = 0;
            public byte[]? Buffer = null;
            public long LastAccess = 0;

            public int CompareTo(object? obj)
            {
                HamCoreEntry hc1, hc2;
                hc1 = this;
                hc2 = (HamCoreEntry)obj!;

                return Str.StrCmpiRetInt(hc1.FileName, hc2.FileName);
            }
        }

        // HamCore ビルダー
        public class HamCoreBuilderFileEntry : IComparable<HamCoreBuilderFileEntry>
        {
            public string? Name;
            public Buf? RawData;
            public Buf? CompressedData;
            public int Offset = 0;

            int IComparable<HamCoreBuilderFileEntry>.CompareTo(HamCoreBuilderFileEntry? other)
            {
                return this.Name!.CompareTo(other!.Name);
            }
        }

        public class HamCoreBuilder
        {
            List<HamCoreBuilderFileEntry> fileList;
            public IReadOnlyList<HamCoreBuilderFileEntry> FileList
            {
                get { return fileList; }
            }

            public bool IsFile(string name)
            {
                foreach (HamCoreBuilderFileEntry f in fileList)
                {
                    if (f.Name!.Equals(name, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }

                return false;
            }

            public bool DeleteFile(string name)
            {
                foreach (HamCoreBuilderFileEntry f in fileList)
                {
                    if (f.Name!.Equals(name, StringComparison.OrdinalIgnoreCase))
                    {
                        fileList.Remove(f);
                        return true;
                    }
                }

                return false;
            }

            public HamCoreBuilder()
            {
                fileList = new List<HamCoreBuilderFileEntry>();
            }

            public void AddDir(string dirName)
            {
                dirName = IO.RemoveLastEnMark(dirName);

                DirEntry[] ee = IO.EnumDirEx(dirName);

                foreach (DirEntry e in ee)
                {
                    if (e.IsFolder == false)
                    {
                        AddFile(e.FullPath, dirName);
                    }
                }
            }

            public void AddFile(string fileName, string baseDirFileName)
            {
                string name = IO.GetRelativeFileName(fileName, baseDirFileName);

                AddFile(name, IO.ReadFile(fileName));
            }

            public void AddFile(string name, byte[] data)
            {
                if (IsFile(name))
                {
                    throw new InvalidOperationException("fileName");
                }

                HamCoreBuilderFileEntry f = new HamCoreBuilderFileEntry();

                Con.WriteLine("{0}: ", name);

                f.Name = name;
                f.RawData = new Buf(Util.CloneByteArray(data));
                Con.WriteLine("{0} -> ", f.RawData.Size);
                f.CompressedData = new Buf(ZLib.Compress(f.RawData.ByteData));
                Con.WriteLine("{0}", f.CompressedData.Size);

                this.fileList.Add(f);
            }

            public void Build(string dstFileName)
            {
                Buf b = Build();

                IO.SaveFile(dstFileName, b.ByteData);
            }

            public Buf Build()
            {
                int z;
                Buf b;

                this.fileList.Sort();

                z = 0;

                z += HamCore.HamcoreHeaderSize;

                // ファイルの個数
                z += sizeof(int);

                // ファイルテーブル
                foreach (HamCoreBuilderFileEntry f in this.fileList)
                {
                    // ファイル名
                    z += Str.ShiftJisEncoding.GetByteCount(f.Name!) + sizeof(int);
                    // ファイルサイズ
                    z += sizeof(int);
                    z += sizeof(int);
                    // オフセットデータ
                    z += sizeof(int);
                }
                // ファイル本体
                foreach (HamCoreBuilderFileEntry f in this.fileList)
                {
                    f.Offset = z;
                    z += (int)f.CompressedData!.Size;
                }

                // 書き込み
                b = new Buf();
                // ヘッダ
                b.Write(Str.ShiftJisEncoding.GetBytes(HamCore.HamcoreHeaderData));
                b.WriteInt((uint)this.fileList.Count);
                foreach (HamCoreBuilderFileEntry f in this.fileList)
                {
                    // ファイル名
                    b.WriteStr(f.Name!, true);
                    // ファイルサイズ
                    b.WriteInt(f.RawData!.Size);
                    b.WriteInt(f.CompressedData!.Size);
                    // オフセット
                    b.WriteInt((uint)f.Offset);
                }
                // 本体
                foreach (HamCoreBuilderFileEntry f in this.fileList)
                {
                    b.Write(f.CompressedData!.ByteData);
                }

                b.SeekToBegin();

                return b;
            }
        }

        // HamCore ファイル
        public class HamCore
        {
            public const string HamcoreDirName = "@hamcore";
            public const string HamcoreHeaderData = "HamCore";
            public const int HamcoreHeaderSize = 7;
            public const long HamcoreCacheExpires = 5 * 60 * 1000;
            bool disableReadRawFile = false;
            public bool DisableReadRawFile
            {
                get { return disableReadRawFile; }
                set { disableReadRawFile = value; }
            }

            Dictionary<string, HamCoreEntry> list;

            IO? hamcore_io;

            public string[] GetFileNames()
            {
                List<string> ret = new List<string>();

                foreach (HamCoreEntry e in list.Values)
                {
                    ret.Add(e.FileName);
                }

                return ret.ToArray();
            }

            public HamCore(string filename)
            {
                filename = IO.InnerFilePath(filename);
                string filenameOnly = Path.GetFileName(filename);
                string filenameAlt = Path.Combine(Path.GetDirectoryName(filename)!, "_" + filenameOnly);

                try
                {
                    IO.FileReplaceRename(filenameAlt, filename);
                }
                catch
                {
                }

                list = new Dictionary<string, HamCoreEntry>();

                try
                {
                    hamcore_io = IO.FileOpen(filename);
                }
                catch
                {
                    return;
                }

                try
                {
                    // ファイルヘッダを読み込む
                    byte[] header = hamcore_io.Read(HamcoreHeaderSize)!;
                    byte[] header2 = Str.AsciiEncoding.GetBytes(HamcoreHeaderData);
                    if (header == null || Util.MemEquals(header, header2) == false)
                    {
                        throw new SystemException();
                    }

                    uint num = 0;
                    byte[] buf = hamcore_io.Read(Util.SizeOfInt32)!;
                    num = Util.ByteToUInt(buf);
                    uint i;
                    for (i = 0; i < num; i++)
                    {
                        // ファイル名
                        uint str_size;

                        buf = hamcore_io.Read(Util.SizeOfInt32)!;
                        str_size = Util.ByteToUInt(buf);
                        if (str_size >= 1)
                        {
                            str_size--;
                        }

                        byte[] str_data = hamcore_io.Read((int)str_size)!;
                        string tmp = Str.ShiftJisEncoding.GetString(str_data);

                        HamCoreEntry c = new HamCoreEntry();
                        c.FileName = tmp;

                        buf = hamcore_io.Read(Util.SizeOfInt32)!;
                        c.Size = Util.ByteToUInt(buf);

                        buf = hamcore_io.Read(Util.SizeOfInt32)!;
                        c.SizeCompressed = Util.ByteToUInt(buf);

                        buf = hamcore_io.Read(Util.SizeOfInt32)!;
                        c.Offset = Util.ByteToUInt(buf);

                        list.Add(c.FileName.ToUpperInvariant(), c);
                    }
                }
                catch
                {
                    hamcore_io.Close();
                }
            }

            public Buf ReadHamcore(string name)
            {
                if (name[0] == '|')
                {
                    name = name.Substring(1);
                }
                if (name[0] == '/' || name[0] == '\\')
                {
                    name = name.Substring(1);
                }

                string filename = name;

                filename = filename.Replace("/", "\\");

                // ローカルディスクの hamcore/ ディレクトリにファイルがあればそれを読み込む
                Buf? b;

                if (this.disableReadRawFile == false)
                {
                    try
                    {
                        b = Buf.ReadFromFile(HamcoreDirName + Env.PathSeparator + filename);

                        return b;
                    }
                    catch
                    {
                    }
                }

                // 無い場合は hamcore システムで読み込む
                lock (list)
                {
                    HamCoreEntry c;
                    string key = filename.ToUpperInvariant();

                    b = null;

                    if (list.ContainsKey(key))
                    {
                        c = list[key];

                        if (c.Buffer != null)
                        {
                            // 既に読み込まれている
                            b = new Buf(c.Buffer);
                            b.SeekToBegin();
                            c.LastAccess = Time.Tick64;
                        }
                        else
                        {
                            // 読み込まれていないのでファイルから読み込む
                            if (hamcore_io!.Seek(SeekOrigin.Begin, (int)c.Offset))
                            {
                                // 圧縮データの読み込み
                                byte[] data = hamcore_io.Read((int)c.SizeCompressed)!;

                                // 展開する
                                int dstSize = (int)c.Size;
                                byte[] buffer = ZLib.Uncompress(data, dstSize);

                                c.Buffer = buffer;
                                b = new Buf(buffer);
                                b.SeekToBegin();
                                c.LastAccess = Time.Tick64;
                            }
                        }
                    }

                    // 有効期限の切れたキャッシュを削除する
                    long now = Time.Tick64;
                    foreach (HamCoreEntry cc in list.Values)
                    {
                        if (cc.Buffer != null)
                        {
                            if (((cc.LastAccess + HamcoreCacheExpires) < now) ||
                                cc.FileName.StartsWith("Li", StringComparison.OrdinalIgnoreCase))
                            {
                                cc.Buffer = null;
                            }
                        }
                    }
                }

                return b!;
            }
        }

        // ディレクトリエントリ
        public class DirEntry : IComparable<DirEntry>
        {
            internal bool folder;
            public bool IsFolder => folder;

            internal string fileName = "";
            public string FileName => fileName;

            internal string fullPath = "";
            public string FullPath => fullPath;

            internal string relativePath = "";
            public string RelativePath => relativePath;

            internal long fileSize;
            public long FileSize => fileSize;

            internal DateTime createDate;
            public DateTime CreateDate => createDate;

            internal DateTime updateDate;
            public DateTime UpdateDate => updateDate;

            public int CompareTo(DirEntry? other)
            {
                int i;
                i = Str.StrCmpiRetInt(this.fileName, other!.fileName);
                if (i == 0)
                {
                    i = Str.StrCmpRetInt(this.fileName, other!.fileName);
                }

                return i;
            }

            public override string ToString()
            {
                return FileName;
            }
        };

        // ファイル操作
        public class IO : IDisposable
        {
            // ディレクトリのコピー
            public delegate bool CopyDirPreCopyDelegate(FileInfo srcFileInfo);
            public static void CopyDir(string srcDirName, string destDirName, CopyDirPreCopyDelegate preCopy, bool ignoreError, bool printStatus)
            {
                CopyDir(srcDirName, destDirName, preCopy, ignoreError, printStatus, false, false, false);
            }
            public static void CopyDir(string srcDirName, string destDirName, CopyDirPreCopyDelegate preCopy, bool ignoreError, bool printStatus,
                bool skipIfNoChange, bool deleteBom)
            {
                CopyDir(srcDirName, destDirName, preCopy, ignoreError, printStatus, skipIfNoChange, deleteBom, false);
            }
            public static void CopyDir(string srcDirName, string destDirName, CopyDirPreCopyDelegate preCopy, bool ignoreError, bool printStatus,
                bool skipIfNoChange, bool deleteBom, bool useTimeStampToCheckNoChange)
            {
                string[] files = Directory.GetFiles(srcDirName, "*", SearchOption.AllDirectories);

                foreach (string srcFile in files)
                {
                    FileInfo info = new FileInfo(srcFile);

                    // 宛先ファイル名
                    string relativeFileName = IO.GetRelativeFileName(srcFile, srcDirName);
                    string destFileName = Path.Combine(destDirName, relativeFileName);
                    string destFileDirName = Path.GetDirectoryName(destFileName)!;

                    if (preCopy != null)
                    {
                        if (preCopy(info) == false)
                        {
                            continue;
                        }
                    }

                    try
                    {
                        if (Directory.Exists(destFileDirName) == false)
                        {
                            Directory.CreateDirectory(destFileDirName);
                        }

                        FileCopy(srcFile, destFileName, skipIfNoChange, deleteBom, useTimeStampToCheckNoChange);
                    }
                    catch
                    {
                        if (ignoreError == false)
                        {
                            throw;
                        }
                    }

                    if (printStatus)
                    {
                        Con.WriteLine(relativeFileName);
                    }
                }
            }

            // 定数
            public const string DefaultHamcoreFileName = "@hamcore.se2";

            public const int FileStreamBufferSize = 4096;

            // クラス変数
            static string hamcoreFileName = DefaultHamcoreFileName;
            public static string HamcoreFileName
            {
                get { return IO.hamcoreFileName; }
                set
                {
                    lock (hamLockObj)
                    {
                        if (hamCore != null)
                        {
                            throw new ApplicationException();
                        }

                        IO.hamcoreFileName = value;
                        tryToUseHamcore = false;
                    }
                }
            }

            static bool tryToUseHamcore = true;
            static HamCore? hamCore = null;
            static object hamLockObj = new object();
            public static HamCore? HamCore
            {
                get
                {
                    HamCore? ret = null;

                    lock (hamLockObj)
                    {
                        if (hamCore == null)
                        {
                            if (tryToUseHamcore)
                            {
                                if (hamCore == null)
                                {
                                    try
                                    {
                                        ret = hamCore = new HamCore(hamcoreFileName);
                                    }
                                    catch
                                    {
                                        tryToUseHamcore = false;
                                    }
                                }
                            }
                        }
                    }

                    return ret;
                }
            }

            // フィールド
            string name;
            public string Name
            {
                get { return name; }
            }
            FileStream? p;
            public FileStream? InnerFileStream
            {
                get { return p; }
            }
            bool writeMode;
            public bool WriteMode
            {
                get { return writeMode; }
            }
            bool hamMode;
            public bool HamMode
            {
                get { return hamMode; }
            }
            Buf? hamBuf;

            object lockObj;
            readonly AsyncLock AsyncLockObj = new AsyncLock();

            // コンストラクタ
            private IO()
            {
                name = "";
                p = null;
                writeMode = hamMode = false;
                lockObj = new object();
                hamBuf = null;
            }

            // ファイルの拡張子が一致するかどうかチェック
            static bool IsExtensionMatchInternal(string filename, string extension)
            {
                if (extension._IsEmpty()) return true;
                if (extension._IsSamei("*") || extension._IsSamei("*.*")) return true;

                extension = extension._TrimStartWith("*.");
                if (extension._IsEmpty()) return true;
                if (extension._IsSamei("*")) return true;
                if (extension.StartsWith(".") == false) extension = "." + extension;

                filename = Path.GetFileName(filename);
                return filename.EndsWith(extension, StringComparison.OrdinalIgnoreCase);
            }
            public static bool IsExtensionsMatch(string filename, string? extensions)
            {
                // 指定された拡張子を整理
                if (extensions != null)
                {
                    string[] tokens = extensions._Split(StringSplitOptions.RemoveEmptyEntries, ' ', '\t', ',', ';');
                    foreach (string ext in tokens)
                    {
                        if (ext._IsFilled())
                        {
                            if (IsExtensionMatchInternal(filename, ext))
                            {
                                return true;
                            }
                        }
                    }
                    return false;
                }
                else
                {
                    return true;
                }
            }
            public static bool IsExtensionsMatch(string filename, IEnumerable<string> extensions)
            {
                // 指定された拡張子を整理
                if (extensions != null)
                {
                    foreach (string ext in extensions)
                    {
                        if (IsExtensionMatchInternal(filename, ext))
                        {
                            return true;
                        }
                    }
                    return false;
                }
                else
                {
                    return true;
                }
            }

            // ファイルに文字列を書きこむ
            public static void WriteAllTextWithEncoding(string fileName, string str, Encoding encoding, bool appendBom = false, bool ifSameContentsDoNothing = false)
            {
                fileName = InnerFilePath(fileName);

                byte[] data = encoding.GetBytes(str);
                byte[]? bom = null;
                if (appendBom)
                {
                    bom = Str.GetBOM(encoding);
                }

                data = Util.CombineByteArray(bom, data);

                IO.SaveFile(fileName, data, 0, data.Length, ifSameContentsDoNothing);
            }

            // ファイルを自動的に文字コードを認識して文字列を読み込む
            public static string ReadAllTextWithAutoGetEncoding(string fileName) => ReadAllTextWithAutoGetEncoding(fileName, out _, out _);
            public static string ReadAllTextWithAutoGetEncoding(string fileName, out Encoding appliedEncoding, out bool bomExists)
            {
                fileName = InnerFilePath(fileName);

                byte[] data = IO.ReadFile(fileName);

                int bomSize;
                Encoding? enc = Str.GetEncoding(data, out bomSize);
                if (enc == null)
                {
                    enc = Encoding.Default;
                }

                data = Util.RemoveStartByteArray(data, bomSize);

                appliedEncoding = enc;

                bomExists = (bomSize != 0);

                return enc.GetString(data);
            }

            // 拡張子を元に一時ファイルを作成する
            public static IO CreateTempFileByExt(string ext)
            {
                return IO.FileCreate(CreateTempFileNameByExt(ext));
            }

            // 拡張子を指定するとその拡張子を持つ一時ファイル名を作成する
            public static string CreateTempFileNameByExt(string ext)
            {
                if (Str.IsEmptyStr(ext))
                {
                    ext = "tmp";
                }
                if (ext[0] == '.')
                {
                    ext = ext.Substring(1);
                }

                while (true)
                {
                    string newFilename;
                    string fullPath;
                    string randStr;

                    randStr = Str.GenRandStr();
                    newFilename = "__" + randStr + "." + ext;

                    fullPath = CreateTempFileName(newFilename);

                    if (IO.IsFileExists(fullPath) == false)
                    {
                        return fullPath;
                    }
                }
            }

            // 一時ファイルを作成する
            public static IO CreateTempFile(string name)
            {
                return IO.FileCreate(CreateTempFileName(name));
            }

            // 一時ファイル名を作成する
            public static string CreateTempFileName(string name)
            {
                return Path.Combine(Env.MyGlobalTempDir, name);
            }

            // サブディレクトリを含んだディレクトリの列挙 (キャンセル可能)
            class EnumDirParam
            {
                public CancellationToken cancel;
                public List<DirEntry> DirList = new List<DirEntry>();
                public bool ExcludeDirectory;
            }
            static bool EnumDirWithCancelCallback(DirEntry e, object param)
            {
                EnumDirParam p = (EnumDirParam)param;

                if (p.cancel.IsCancellationRequested)
                {
                    return false;
                }

                if (p.ExcludeDirectory == false || e.IsFolder == false)
                {
                    p.DirList.Add(e);
                }

                return true;
            }
            public static List<DirEntry>? EnumDirWithCancel(string dirList, string? fileExtensions = null, CancellationToken cancel = default(CancellationToken))
            {
                return EnumDirsWithCancel(new string[] { dirList }, fileExtensions, cancel);
            }
            public static List<DirEntry>? EnumDirsWithCancel(string[] dirList, string? fileExtensions = null, CancellationToken cancel = default(CancellationToken))
            {
                EnumDirParam p = new EnumDirParam();
                p.cancel = cancel;
                p.DirList = new List<DirEntry>();

                p.ExcludeDirectory = fileExtensions._IsFilled();

                bool ret = EnumDirsWithCallback(dirList, fileExtensions, EnumDirWithCancelCallback, p);

                if (ret == false)
                {
                    return null;
                }

                return p.DirList;
            }

            // サブディレクトリを含んだディレクトリの列挙 (コールバック)
            public delegate bool EnumDirCallbackProc(DirEntry e, object param);
            public static bool EnumDirsWithCallback(string[] dirList, string? fileExtensions, EnumDirCallbackProc callback, object callbackParam)
            {
                foreach (string dir in dirList)
                {
                    if (EnumDirWithCallback(dir, fileExtensions, callback, callbackParam) == false) return false;
                }
                return true;
            }
            public static bool EnumDirWithCallback(string dirName, string? fileExtensions, EnumDirCallbackProc callback, object callbackParam)
            {
                return enumDirWithCallback(dirName, dirName, callback, fileExtensions, callbackParam);
            }
            static bool enumDirWithCallback(string dirName, string baseDirName, EnumDirCallbackProc callback, string? fileExtensions, object cb_param)
            {
                string tmp = IO.InnerFilePath(dirName);

                string[]? dirs = null;

                try
                {
                    dirs = Directory.GetDirectories(tmp);
                }
                catch
                {
                }

                if (dirs != null)
                {
                    Array.Sort(dirs);
                    foreach (string name in dirs)
                    {
                        string fullPath = name;

                        DirectoryInfo? info = null;

                        try
                        {
                            info = new DirectoryInfo(fullPath);
                        }
                        catch
                        {
                        }

                        if (info != null)
                        {
                            DirEntry e = new DirEntry();

                            e.fileName = Path.GetFileName(name);
                            e.fileSize = 0;
                            e.createDate = info.CreationTimeUtc;
                            e.folder = true;
                            e.updateDate = info.LastWriteTimeUtc;
                            e.fullPath = fullPath;
                            e.relativePath = GetRelativeFileName(fullPath, baseDirName);

                            if (callback(e, cb_param) == false)
                            {
                                return false;
                            }

                            enumDirWithCallback(fullPath, baseDirName, callback, fileExtensions, cb_param);
                        }
                    }
                }

                string[]? files = null;

                try
                {
                    files = Directory.GetFiles(tmp);
                }
                catch
                {
                }

                if (files != null)
                {
                    Array.Sort(files);
                    foreach (string name in files)
                    {
                        string fullPath = name;

                        bool ok = false;

                        ok = IO.IsExtensionsMatch(fullPath, fileExtensions);

                        if (ok)
                        {
                            FileInfo? info = null;

                            try
                            {
                                info = new FileInfo(fullPath);
                            }
                            catch
                            {
                            }

                            if (info != null)
                            {
                                DirEntry e = new DirEntry();

                                e.fileName = Path.GetFileName(name);
                                try { e.fileSize = info.Length; } catch { }
                                try { e.createDate = info.CreationTimeUtc; } catch { }
                                try { e.updateDate = info.LastWriteTimeUtc; } catch { }
                                e.folder = false;
                                e.fullPath = fullPath;
                                e.relativePath = GetRelativeFileName(fullPath, baseDirName);

                                if (callback(e, cb_param) == false)
                                {
                                    return false;
                                }
                            }
                        }
                    }
                }

                return true;
            }

            // サブディレクトリを含んだディレクトリの列挙
            public static DirEntry[] EnumDirEx(string dirName)
            {
                List<DirEntry> list = new List<DirEntry>();

                enumDirEx(dirName, dirName, list);

                return list.ToArray();
            }
            static void enumDirEx(string dirName, string baseDirName, List<DirEntry> list)
            {
                string tmp = IO.InnerFilePath(dirName);

                string[] dirs = Directory.GetDirectories(tmp);
                foreach (string name in dirs)
                {
                    string fullPath = name;
                    DirectoryInfo info = new DirectoryInfo(fullPath);

                    DirEntry e = new DirEntry();

                    e.fileName = Path.GetFileName(name);
                    e.fileSize = 0;
                    e.createDate = info.CreationTimeUtc;
                    e.folder = true;
                    e.updateDate = info.LastWriteTimeUtc;
                    e.fullPath = fullPath;
                    e.relativePath = GetRelativeFileName(fullPath, baseDirName);

                    list.Add(e);

                    enumDirEx(fullPath, baseDirName, list);
                }

                string[] files = Directory.GetFiles(tmp);
                foreach (string name in files)
                {
                    string fullPath = name;
                    FileInfo info = new FileInfo(fullPath);

                    DirEntry e = new DirEntry();

                    e.fileName = Path.GetFileName(name);
                    e.fileSize = info.Length;
                    e.createDate = info.CreationTimeUtc;
                    e.folder = false;
                    e.updateDate = info.LastWriteTimeUtc;
                    e.fullPath = fullPath;
                    e.relativePath = GetRelativeFileName(fullPath, baseDirName);

                    list.Add(e);
                }
            }

            // ディレクトリの列挙
            public static DirEntry[] EnumDir(string dirName)
            {
                List<DirEntry> list = new List<DirEntry>();
                string tmp = IO.InnerFilePath(dirName);

                string[] dirs = Directory.GetDirectories(tmp);
                foreach (string name in dirs)
                {
                    string fullPath = name;
                    DirectoryInfo info = new DirectoryInfo(fullPath);

                    DirEntry e = new DirEntry();

                    e.fileName = Path.GetFileName(name);
                    e.fileSize = 0;
                    e.createDate = info.CreationTimeUtc;
                    e.folder = true;
                    e.updateDate = info.LastWriteTimeUtc;
                    e.fullPath = fullPath;
                    e.relativePath = GetRelativeFileName(fullPath, dirName);

                    list.Add(e);
                }

                string[] files = Directory.GetFiles(tmp);
                foreach (string name in files)
                {
                    string fullPath = name;
                    FileInfo info = new FileInfo(fullPath);

                    DirEntry e = new DirEntry();

                    e.fileName = Path.GetFileName(name);
                    e.fileSize = info.Length;
                    e.createDate = info.CreationTimeUtc;
                    e.folder = false;
                    e.updateDate = info.LastWriteTimeUtc;
                    e.fullPath = fullPath;
                    e.relativePath = GetRelativeFileName(fullPath, dirName);

                    list.Add(e);
                }

                list.Sort();

                return list.ToArray();
            }

            // ファイルを置換してリネームする
            public static void FileReplaceRename(string oldName, string newName)
            {
                FileCopy(oldName, newName);
                FileDelete(oldName);
            }

            // ファイルをコピーする
            public static void FileCopy(string oldName, string newName)
            {
                FileCopy(oldName, newName, false, false);
            }
            public static void FileCopy(string oldName, string newName, bool skipIfNoChange, bool deleteBom)
            {
                FileCopy(oldName, newName, skipIfNoChange, deleteBom, false);
            }
            public static void FileCopy(string oldName, string newName, bool skipIfNoChange, bool deleteBom, bool useTimeStampToCheckNoChange)
            {
                string tmp1 = InnerFilePath(oldName);
                string tmp2 = InnerFilePath(newName);

                if (useTimeStampToCheckNoChange && skipIfNoChange)
                {
                    DateTime dt1, dt2;

                    try
                    {
                        dt1 = Directory.GetLastWriteTimeUtc(tmp1);
                        dt2 = Directory.GetLastWriteTimeUtc(tmp2);

                        TimeSpan ts = dt2 - dt1;
                        if (ts.TotalSeconds >= -5.0)
                        {
                            return;
                        }
                    }
                    catch
                    {
                    }
                }

                if (skipIfNoChange || deleteBom)
                {
                    byte[] srcData = IO.ReadFile(tmp1);
                    byte[] destData = new byte[0];
                    bool changed = true;
                    int bomSize;

                    Str.GetEncoding(srcData, out bomSize);
                    if (bomSize >= 1)
                    {
                        srcData = Util.ExtractByteArray(srcData, bomSize, srcData.Length - bomSize);
                    }

                    if (skipIfNoChange)
                    {
                        try
                        {
                            FileStream fs = File.OpenRead(tmp2);
                            long size = 0xffffffff;
                            try
                            {
                                size = fs.Length;
                            }
                            finally
                            {
                                fs.Close();
                            }

                            if (size == srcData.Length || srcData.Length == 0)
                            {
                                destData = IO.ReadFile(tmp2);
                            }
                        }
                        catch
                        {
                        }

                        if (Util.MemEquals(srcData, destData))
                        {
                            changed = false;
                        }
                    }

                    if (changed)
                    {
                        IO.SaveFile(tmp2, srcData);
                        CopyFileTimestamp(tmp2, tmp1);
                    }
                }
                else
                {
                    File.Copy(tmp1, tmp2, true);
                }
            }

            // ファイルの日付をコピーする
            public static void CopyFileTimestamp(string dstFileName, string srcFileName)
            {
                DateTime dt1 = File.GetCreationTimeUtc(srcFileName);
                DateTime dt2 = File.GetLastAccessTimeUtc(srcFileName);
                DateTime dt3 = File.GetLastWriteTimeUtc(srcFileName);

                File.SetCreationTimeUtc(dstFileName, dt1);
                File.SetLastAccessTimeUtc(dstFileName, dt2);
                File.SetLastWriteTimeUtc(dstFileName, dt3);
            }

            // ファイルの日付を上書きする
            public static void SetFileTimestamp(string dstFileName, FileInfo fileInfo)
            {
                File.SetCreationTimeUtc(dstFileName, fileInfo.CreationTimeUtc);
                File.SetLastAccessTimeUtc(dstFileName, fileInfo.LastAccessTimeUtc);
                File.SetLastWriteTimeUtc(dstFileName, fileInfo.LastWriteTimeUtc);
            }

            // ファイルを読み込む
            static public byte[] ReadFile(string name)
            {
                IO io = FileOpen(name);
                try
                {
                    int size = io.FileSize;
                    byte[] ret = io.Read(size)!;
                    return ret;
                }
                finally
                {
                    io.Close();
                }
            }

            // ファイルを保存する
            static public void SaveFile(string name, byte[] data)
            {
                SaveFile(name, data, 0, data.Length);
            }
            static public void SaveFile(string name, byte[] data, int offset, int size, bool doNothingIfSameContents = false)
            {
                if (doNothingIfSameContents)
                {
                    try
                    {
                        byte[] current_data = IO.ReadFile(name);
                        if (Util.MemEquals(current_data, Util.ExtractByteArray(data, offset, size)))
                        {
                            return;
                        }
                    }
                    catch
                    {
                    }
                }

                IO io = FileCreate(name);
                try
                {
                    io.Write(data, offset, size);
                }
                finally
                {
                    io.Close();
                }
            }

            // 安全なファイル名を生成する
            static public string MakeSafeFileName(string src)
            {
                return src
                    .Replace("..", "__")
                    .Replace("/", "_")
                    .Replace("\\", "_")
                    .Replace("@", "_")
                    .Replace("|", "_");
            }

            // ディレクトリが存在するかどうか確認する
            public static bool IsDirExists(string name)
            {
                try
                {
                    string tmp = InnerFilePath(name);

                    return Directory.Exists(tmp);
                }
                catch { return false; }
            }

            // ファイルが存在するかどうか確認する
            public static bool IsFileExists(string name)
            {
                try
                {
                    string tmp = InnerFilePath(name);

                    return File.Exists(tmp);
                }
                catch { return false; }
            }

            // ファイルを削除する
            static void fileDeleteInner(string name)
            {
                string name2 = ConvertPath(name);

                File.Delete(name2);
            }
            public static void FileDelete(string name)
            {
                string tmp = InnerFilePath(name);

                fileDeleteInner(tmp);
            }

            // シークする
            public bool Seek(SeekOrigin mode, long offset)
            {
                lock (lockObj)
                {
                    if (p != null)
                    {
                        try
                        {
                            p.Seek(offset, mode);

                            return true;
                        }
                        catch
                        {
                            return false;
                        }
                    }
                    else
                    {
                        return false;
                    }
                }
            }

            // ファイルサイズの取得
            public long FileSize64
            {
                get
                {
                    lock (lockObj)
                    {
                        if (p != null)
                        {
                            return p.Length;
                        }
                        else
                        {
                            if (hamMode)
                            {
                                return (long)hamBuf!.Size;
                            }
                        }

                        return 0;
                    }
                }
            }
            public int FileSize
            {
                get
                {
                    long size64 = this.FileSize64;

                    if (size64 >= 2147483647)
                    {
                        size64 = 2147483647;
                    }

                    return (int)size64;
                }
            }
            public static int GetFileSize(string name)
            {
                IO io = IO.FileOpen(name, false);
                try
                {
                    return io.FileSize;
                }
                finally
                {
                    io.Close();
                }
            }

            // ファイルからすべて読み込む
            public byte[] ReadAll()
            {
                this.Seek(SeekOrigin.Begin, 0);
                int size = this.FileSize;

                byte[] data = new byte[size];
                this.Read(data, 0, size);

                this.Seek(SeekOrigin.Begin, 0);

                return data;
            }

            // ファイルから読み込む
            public byte[]? Read(int size)
            {
                byte[] buf = new byte[size];
                bool ret = Read(buf, size);
                if (ret == false)
                {
                    return null;
                }
                return buf;
            }
            public bool Read(byte[] buf, int size)
            {
                return Read(buf, 0, size);
            }
            public bool Read(byte[] buf, int offset, int size)
            {
                if (size == 0)
                {
                    return true;
                }

                lock (lockObj)
                {
                    if (this.HamMode)
                    {
                        byte[] ret = hamBuf!.Read((uint)size)!;

                        if (ret.Length != size)
                        {
                            return false;
                        }

                        Util.CopyByte(buf, offset, ret, 0, size);

                        return true;
                    }

                    if (p != null)
                    {
                        try
                        {
                            int ret = p.Read(buf, offset, size);
                            if (ret == size)
                            {
                                return true;
                            }
                            else
                            {
                                return false;
                            }
                        }
                        catch
                        {
                            return false;
                        }
                    }
                    else
                    {
                        return false;
                    }
                }
            }

            public Task<bool> ReadAsync(byte[] buf) => ReadAsync(buf, 0, buf.Length);
            public Task<bool> ReadAsync(byte[] buf, int size) => ReadAsync(buf, 0, size);
            public Task<bool> ReadAsync(byte[] buf, int offset, int size) => ReadAsync(buf.AsMemory(offset, size));
            public async Task<bool> ReadAsync(Memory<byte> memory)
            {
                if (memory.Length == 0)
                {
                    return true;
                }

                if (this.HamMode)
                {
                    byte[] ret = hamBuf!.Read((uint)memory.Length);

                    if (ret.Length != memory.Length)
                    {
                        return false;
                    }

                    ret.CopyTo(memory);

                    return true;
                }

                using (await AsyncLockObj.LockWithAwait())
                {
                    if (p != null)
                    {
                        try
                        {
                            int ret = await p.ReadAsync(memory);
                            if (ret == memory.Length)
                            {
                                return true;
                            }
                            else
                            {
                                return false;
                            }
                        }
                        catch (Exception ex)
                        {
                            Dbg.Where($"IO.ReadAsync('{this.Name}') failed. {ex.Message}");
                            return false;
                        }
                    }
                    else
                    {
                        return false;
                    }
                }
            }

            // ファイルに書き込む
            public bool Write(byte[] buf) => Write(buf, 0, buf.Length);
            public bool Write(byte[] buf, int size) => Write(buf, 0, size);
            public bool Write(byte[] buf, int offset, int size) => Write(new ReadOnlyMemory<byte>(buf, offset, size));
            public bool Write(ReadOnlyMemory<byte> memory, bool flush = false)
            {
                if (writeMode == false)
                {
                    return false;
                }
                if (memory.Length == 0)
                {
                    return true;
                }

                lock (lockObj)
                {
                    if (p != null)
                    {
                        try
                        {
                            p.Write(memory.Span);

                            if (flush)
                            {
                                p.Flush();
                            }

                            return true;
                        }
                        catch (Exception ex)
                        {
                            Con.WriteDebug($"IO.Write('{this.Name}') failed. {ex.Message}");
                            return false;
                        }
                    }
                    else
                    {
                        return false;
                    }
                }
            }

            public Task<bool> WriteAsync(byte[] buf) => WriteAsync(buf, 0, buf.Length);
            public Task<bool> WriteAsync(byte[] buf, int size) => WriteAsync(buf, 0, size);
            public Task<bool> WriteAsync(byte[] buf, int offset, int size) => WriteAsync(buf._AsReadOnlyMemory(offset, size));
            public async Task<bool> WriteAsync(ReadOnlyMemory<byte> memory, bool flush = false)
            {
                if (writeMode == false)
                {
                    return false;
                }
                if (memory.Length == 0)
                {
                    return true;
                }

                using (await AsyncLockObj.LockWithAwait())
                {
                    if (p != null)
                    {
                        try
                        {
                            await p.WriteAsync(memory);

                            if (flush)
                            {
                                await p.FlushAsync();
                            }

                            return true;
                        }
                        catch (Exception ex)
                        {
                            Con.WriteDebug($"IO.WriteAsync('{this.Name}') failed. {ex.Message}");
                            return false;
                        }
                    }
                    else
                    {
                        return false;
                    }
                }
            }

            public void Dispose() { this.Dispose(true); GC.SuppressFinalize(this); }
            Once DisposeFlag;
            protected virtual void Dispose(bool disposing)
            {
                if (!disposing || DisposeFlag.IsFirstCall() == false) return;
                Close();
            }

            // 閉じて削除する
            public bool CloseAndDelete()
            {
                string name = this.Name;

                Close();

                try
                {
                    FileDelete(name);
                    return true;
                }
                catch
                {
                    return false;
                }
            }

            // 閉じる
            public void Close()
            {
                Close(false);
            }
            public void Close(bool noFlush)
            {
                lock (this.lockObj)
                {
                    using (AsyncLockObj.LockLegacy())
                    {
                        try
                        {
                            if (this.hamMode == false)
                            {
                                if (this.p != null)
                                {
                                    if (this.writeMode && noFlush == false)
                                    {
                                        Flush();
                                    }

                                    this.p.Close();
                                }

                                this.p = null;
                            }
                        }
                        catch { }
                    }
                }
            }

            // フラッシュする
            public void Flush()
            {
                try
                {
                    lock (this.lockObj)
                    {
                        if (this.p != null)
                        {
                            this.p.Flush();
                        }
                    }
                }
                catch
                {
                }
            }

            public async Task FlushAsync()
            {
                try
                {
                    using (await this.AsyncLockObj.LockWithAwait())
                    {
                        if (this.p != null)
                        {
                            await this.p.FlushAsync();
                        }
                    }
                }
                catch
                {
                }
            }

            // ファイルを作成する
            public static IO FileCreate(string name, bool noShare = false, bool useAsync = false)
            {
                name = InnerFilePath(name);

                return fileCreateInner(name, noShare, useAsync);
            }
            static IO fileCreateInner(string name, bool noShare, bool useAsync)
            {
                IO o = new IO();

                string name2 = ConvertPath(name);

                lock (o.lockObj)
                {
                    o.p = new FileStream(name2, FileMode.Create, FileAccess.ReadWrite, noShare ? FileShare.None : FileShare.Read, FileStreamBufferSize, useAsync);
                    o.name = name2;
                    o.writeMode = true;
                }

                return o;
            }

            // ファイルを開く
            public static IO FileOpen(string name, bool writeMode = false, bool readLock = false, bool useAsync = false)
            {
                name = InnerFilePath(name);

                if (name[0] == '|')
                {
                    HamCore hc = IO.HamCore!;

                    Buf b = hc.ReadHamcore(name);
                    if (b == null)
                    {
                        throw new FileNotFoundException();
                    }

                    IO o = new IO();
                    o.name = name.Substring(1);
                    o.hamMode = true;
                    o.hamBuf = b;

                    return o;
                }
                else
                {
                    return fileOpenInner(name, writeMode, readLock, useAsync);
                }
            }
            static IO fileOpenInner(string name, bool writeMode, bool readLock, bool useAsync)
            {
                IO o = new IO();

                string name2 = ConvertPath(name);

                lock (o.lockObj)
                {
                    o.p = new FileStream(name2, FileMode.Open, (writeMode ? FileAccess.ReadWrite : FileAccess.Read),
                        (readLock ? FileShare.None : FileShare.Read), FileStreamBufferSize, useAsync);

                    o.name = name2;
                    o.writeMode = writeMode;
                }

                return o;
            }

            // ファイルを開くか作成する
            public static IO FileCreateOrAppendOpen(string name)
            {
                if (IsFileExists(name))
                {
                    IO io = FileOpen(name, true);
                    io.Seek(SeekOrigin.End, 0);
                    return io;
                }
                else
                {
                    return FileCreate(name);
                }
            }

            // 相対的ファイル名を計算する
            public static string GetRelativeFileName(string fileName, string baseDirName)
            {
                baseDirName = baseDirName.Trim();
                fileName = fileName.Trim();

                baseDirName = IO.NormalizePath(baseDirName);
                baseDirName = RemoveLastEnMark(baseDirName).Trim() + Env.PathSeparator;
                fileName = IO.NormalizePath(fileName);

                if (fileName.Length <= baseDirName.Length)
                {
                    throw new ArgumentException("fileName, baseDirName");
                }

                if (fileName.StartsWith(baseDirName, StringComparison.OrdinalIgnoreCase) == false)
                {
                    throw new ArgumentException("fileName, baseDirName");
                }

                return fileName.Substring(baseDirName.Length);
            }

            // パスの正規化
            public static string RemoteLastEnMark(string path) => RemoveLastEnMark(path); // older typo
            public static string RemoveLastEnMark(string path)
            {
                if (path == null)
                {
                    path = "";
                }
                if (path.EndsWith("/"))
                {
                    path = path.Substring(0, path.Length - 1);
                }
                if (path.EndsWith(@"\"))
                {
                    path = path.Substring(0, path.Length - 1);
                }
                return path;
            }

            // ファイル名の変更
            public static void FileRename(string oldName, string newName)
            {
                string tmp1 = InnerFilePath(oldName);
                string tmp2 = InnerFilePath(newName);

                File.Move(tmp1, tmp2);
            }

            // ディレクトリ内のすべてのファイルの削除
            public static void DeleteFilesAndSubDirsInDir(string dirName)
            {
                DeleteFilesAndSubDirsInDir(dirName, false);
            }
            public static void DeleteFilesAndSubDirsInDir(string dirName, bool ignoreError)
            {
                dirName = InnerFilePath(dirName);

                if (Directory.Exists(dirName) == false)
                {
                    Directory.CreateDirectory(dirName);
                    return;
                }

                string[] files = Directory.GetFiles(dirName);
                string[] dirs = Directory.GetDirectories(dirName);

                foreach (string file in files)
                {
                    if (ignoreError == false)
                    {
                        File.SetAttributes(file, FileAttributes.Normal);
                        File.Delete(file);
                    }
                    else
                    {
                        try
                        {
                            File.SetAttributes(file, FileAttributes.Normal);
                        }
                        catch
                        {
                        }

                        try
                        {
                            File.Delete(file);
                        }
                        catch
                        {
                        }
                    }
                }

                foreach (string dir in dirs)
                {
                    if (ignoreError == false)
                    {
                        Directory.Delete(dir, true);
                    }
                    else
                    {
                        try
                        {
                            Directory.Delete(dir, true);
                        }
                        catch
                        {
                        }
                    }
                }
            }

            // ディレクトリの削除
            public static bool DeleteDir(string dirName)
            {
                return DeleteDir(dirName, false);
            }
            public static bool DeleteDir(string dirName, bool deleteSubDirs)
            {
                try
                {
                    Directory.Delete(InnerFilePath(dirName), deleteSubDirs);
                    return true;
                }
                catch
                {
                    return false;
                }
            }

            // ディレクトリの作成
            public static bool MakeDir(string dirName)
            {
                try
                {
                    Directory.CreateDirectory(InnerFilePath(dirName));
                    return true;
                }
                catch
                {
                    return false;
                }
            }
            public static bool MakeDirIfNotExists(string dirName)
            {
                string path = InnerFilePath(dirName);

                if (Directory.Exists(path) == false)
                {
                    Directory.CreateDirectory(path);

                    return true;
                }

                return false;
            }

            // ファイルパスを正規化する
            public static string NormalizePath(string src)
            {
                bool first_double_slash = false;
                bool first_single_slash = false;
                string win32_drive_char = "";
                int i;
                string tmp;

                // パスを変換する (Win32, UNIX 変換)
                tmp = ConvertPath(src).Trim();

                // 先頭が "./" や "../" で始まっている場合はカレントディレクトリに置換する
                if (tmp.StartsWith(".\\") || tmp.StartsWith("..\\") || tmp.StartsWith("./") || tmp.StartsWith("../") || tmp.StartsWith(".") || tmp.StartsWith(".."))
                {
                    if (tmp.StartsWith(".."))
                    {
                        tmp = Env.CurrentDir + "/../" + tmp.Substring(2);
                    }
                    else
                    {
                        tmp = Env.CurrentDir + "/" + tmp;
                    }
                }

                // 先頭が "~/" で始まっている場合はホームディレクトリに置換する
                if (tmp.StartsWith("~/") || tmp.StartsWith("~\\"))
                {
                    tmp = Env.HomeDir + "/" + tmp.Substring(2);
                }

                if (tmp.StartsWith("//") || tmp.StartsWith("\\\\"))
                {
                    // 最初が "//" または "\\" で始まる
                    first_double_slash = true;
                }
                else
                {
                    if (tmp.StartsWith("/") || tmp.StartsWith("\\"))
                    {
                        first_single_slash = true;
                    }
                }

                if (tmp.Length >= 2)
                {
                    if (tmp[1] == ':')
                    {
                        // Win32 のドライブ文字列表記
                        win32_drive_char = "" + tmp[0];
                        tmp = tmp.Substring(2);
                    }
                }

                if (tmp == "/" || tmp == "\\")
                {
                    tmp = "";
                }

                // トークン分割
                char[] splitChars = { '/', '\\' };
                string[] t = tmp.Split(splitChars, StringSplitOptions.RemoveEmptyEntries);

                Stack<string> sk = new Stack<string>();

                for (i = 0; i < t.Length; i++)
                {
                    string s = t[i];

                    if (Str.StrCmpi(s, "."))
                    {
                        continue;
                    }
                    else if (Str.StrCmpi(s, ".."))
                    {
                        if (sk.Count >= 1 && (first_double_slash == false || sk.Count >= 2))
                        {
                            sk.Pop();
                        }
                    }
                    else
                    {
                        sk.Push(s);
                    }
                }

                // トークン結合
                tmp = "";

                if (first_double_slash)
                {
                    tmp += "//";
                }
                else if (first_single_slash)
                {
                    tmp += "/";
                }

                if (Str.IsEmptyStr(win32_drive_char) == false)
                {
                    tmp = win32_drive_char + ":/" + tmp;
                }

                string[] sks = sk.ToArray();
                Array.Reverse(sks);
                for (i = 0; i < sks.Length; i++)
                {
                    tmp += sks[i];
                    if (i != (sks.Length - 1))
                    {
                        tmp += "/";
                    }
                }

                tmp = ConvertPath(tmp);

                return tmp;
            }

            // パスの変換
            public static string ConvertPath(string path)
            {
                if (Env.PathSeparator == "\\")
                {
                    return path.Replace('/', '\\');
                }
                else
                {
                    return path.Replace('\\', '/');
                }
            }

            // パスの結合
            public static string ConbinePath(string dirname, string filename)
            {
                return CombinePath(dirname, filename);
            }
            public static string CombinePath(string dirname, string filename)
            {
                bool is_full_path;
                string filename_ident = NormalizePath(filename);

                is_full_path = false;

                if (filename_ident.StartsWith("\\") || filename_ident.StartsWith("/"))
                {
                    is_full_path = true;
                }

                filename = filename_ident;

                if (filename.Length >= 2)
                {
                    char c = filename[0];
                    if (('a' <= c && c <= 'z') || ('A' <= c && c <= 'Z'))
                    {
                        if (filename[1] == ':')
                        {
                            is_full_path = true;
                        }
                    }
                }

                string tmp;

                if (is_full_path == false)
                {
                    tmp = dirname;
                    if (tmp.EndsWith("/") == false && tmp.EndsWith("\\") == false)
                    {
                        tmp += "/";
                    }

                    tmp += filename;
                }
                else
                {
                    tmp = filename;
                }

                return NormalizePath(tmp);
            }

            // 内部ファイルパスの生成
            public static string InnerFilePath(string src)
            {
                if (src[0] != '@')
                {
                    return NormalizePath(src);
                }
                else
                {
                    return NormalizePath(CombinePath(Env.AppRootDir, src.Substring(1)));
                }
            }

            // 作成日時を取得する
            public static DateTime GetCreationTimeUtc(string filename)
            {
                return File.GetCreationTimeUtc(InnerFilePath(filename));
            }
            public static DateTime GetCreationTimeLocal(string filename)
            {
                return File.GetCreationTime(InnerFilePath(filename));
            }

            // 最終更新日時を取得する
            public static DateTime GetLastWriteTimeUtc(string filename)
            {
                return File.GetLastWriteTimeUtc(InnerFilePath(filename));
            }
            public static DateTime GetLastWriteTimeLocal(string filename)
            {
                return File.GetLastWriteTime(InnerFilePath(filename));
            }

            // 最終アクセス日時を取得する
            public static DateTime GetLastAccessTimeUtc(string filename)
            {
                return File.GetLastAccessTimeUtc(InnerFilePath(filename));
            }
            public static DateTime GetLastAccessTimeLocal(string filename)
            {
                return File.GetLastAccessTime(InnerFilePath(filename));
            }

            public static Stream ReadEmbeddedFileStream(string name, Type? assemblyType = null)
            {
                if (assemblyType == null) assemblyType = typeof(IO).GetType();
                return assemblyType.Assembly.GetManifestResourceStream(name)!;
            }

            public static byte[] ReadEmbeddedFileData(string name, Type? assemblyType = null) => ReadEmbeddedFileStream(name, assemblyType)._ReadToEnd();
        }

        // Win32 フォルダ圧縮操ユーティリティ
        public static class Win32FolderCompression
        {
            public static bool SetFolderCompression(string path, bool compressed)
            {
                if (Env.IsWindows == false)
                    return false;

                try
                {
                    path = IO.InnerFilePath(path);
                    Win32Api.Kernel32.SECURITY_ATTRIBUTES secAttrs = default;

                    using (SafeFileHandle h = Win32Api.Kernel32.CreateFile(path, (int)(FileAccess.Read | FileAccess.Write), FileShare.ReadWrite, ref secAttrs, FileMode.Open,
                        Win32Api.Kernel32.FileOperations.FILE_FLAG_BACKUP_SEMANTICS, IntPtr.Zero))
                    {
                        if (h.IsInvalid)
                        {
                            return false;
                        }

                        ushort lpInBuffer = (ushort)(compressed ? Win32Api.Kernel32.COMPRESSION_FORMAT_DEFAULT : Win32Api.Kernel32.COMPRESSION_FORMAT_NONE);
                        uint lpBytesReturned = 0;

                        if (Win32Api.Kernel32.DeviceIoControl(h, Win32Api.Kernel32.EIOControlCode.FsctlSetCompression, ref lpInBuffer, sizeof(short), IntPtr.Zero, 0, out lpBytesReturned, IntPtr.Zero) == false)
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
        }
    }
}
