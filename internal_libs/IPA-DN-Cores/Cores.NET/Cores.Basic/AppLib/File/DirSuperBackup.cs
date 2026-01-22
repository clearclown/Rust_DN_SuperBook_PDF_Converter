// IPA Cores.NET
// 
// Copyright (c) 2019- IPA CyberLab.
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

// Author: Daiyuu Nobori
// Description

#if CORES_BASIC_JSON

#pragma warning disable CA2235 // Mark all non-serializable fields

using System;
using System.Buffers;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Serialization;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

namespace IPA.Cores.Basic;

// バックアップした先のディレクトリに保存されるメタデータ
public class DirSuperBackupMetadata
{
    public DateTimeOffset TimeStamp;
    public FileMetadata DirMetadata = null!;
    public List<DirSuperBackupMetadataFile> FileList = null!;
    public List<string> DirList = null!;
}
public class DirSuperBackupMetadataFile
{
    public string FileName = null!;
    public string? EncrypedFileName;
    public long? EncryptedPhysicalSize;
    public string? Md5;
    public FileMetadata MetaData = null!;
}

[Flags]
public enum DirSuperBackupFlags : long
{
    Default = 0,

    RestoreOnlyNewer = 1,
    RestoreDoNotSkipExactSame = 2,
    RestoreMakeBackup = 4,
    RestoreNoAcl = 8,
    RestoreNoVerify = 16,

    BackupMakeHistory = 65536,
    BackupSync = 131072,
    BackupNoVerify = 262144,

    VerifyIgnoreMetaData = 524288,

    BackupNoMd5 = 1048576,
    RestoreNoMd5 = 2097152,

    NoFileNameLenLimit = 4194304,
    IgnoreReadError = 8388608,

    UseLegacyXtsAes256 = 16777216,

    BackupIncludeSymbolicLink = 33554432,
}

public class DirSuperBackupOptions
{
    public FileSystem LocalFs { get; }
    public FileSystem ArchiveFs { get; }
    public string? InfoLogFileName { get; }
    public string? ErrorLogFileName { get; }
    public DirSuperBackupFlags Flags { get; }
    public string EncryptPassword { get; }
    public int NumThreads { get; }

    public DirSuperBackupOptions(FileSystem? localFs = null, FileSystem? archiveFs = null, string? infoLogFileName = null, string? errorLogFileName = null, DirSuperBackupFlags flags = DirSuperBackupFlags.Default, string encryptPassword = "", int numThreads = 0)
    {
        LocalFs = localFs ?? Lfs;
        ArchiveFs = archiveFs ?? Lfs;
        InfoLogFileName = infoLogFileName;
        ErrorLogFileName = errorLogFileName;
        Flags = flags;
        EncryptPassword = encryptPassword._NonNull();

        if (numThreads <= 0)
        {
            numThreads = Math.Max(Env.NumCpus, 8);
        }

        numThreads = Math.Min(numThreads, 128);

        this.NumThreads = numThreads;
    }
}

[Flags]
public enum DirSuperBackupLogType
{
    Info = 1,
    Error = 2,
}

public class DirSuperBackupStat
{
    public long Copy_NumFiles;
    public long Copy_TotalSize;

    public long Create_NumDirs;

    public long Skip_NumFiles;
    public long Skip_TotalSize;

    public long CreateSkip_NumDirs;

    public long SyncDelete_NumFiles;
    public long SyncDelete_NumDirs;

    public long Error_Dir;

    public long Error_NumCreateDirs;

    public long Error_NumFiles;
    public long Error_TotalSize;

    public long Error_NumDeleteDirs;
    public long Error_NumDeleteFiles;

    public long RecoveredError_NumFiles;
    public long RecoveredError_TotalSize;
}

// ディレクトリ単位の世代対応バックアップユーティリティ
public class DirSuperBackup : AsyncService
{
    public DirSuperBackupOptions Options { get; }
    public FileSystem LocalFs => Options.LocalFs;

    public FileSystem ArchiveFs => Options.ArchiveFs;

    readonly FileSystem LogFs;
    readonly FileSystem LogFsUtf8;

    readonly FileObject? InfoLogFileObj;
    readonly Stream? InfoLogFileStream;
    readonly StreamWriter? InfoLogWriter;

    readonly FileObject? ErrorLogFileObj;
    readonly Stream? ErrorLogFileStream;
    readonly StreamWriter? ErrorLogWriter;

    public const string PrefixMetadata = ".super_metadata_";
    public const string SuffixMetadata = ".metadat.json";

    public readonly DirSuperBackupStat Stat = new DirSuperBackupStat();

    public DirSuperBackup(DirSuperBackupOptions? options = null)
    {
        try
        {
            this.Options = options ?? new DirSuperBackupOptions();
            this.LogFs = new LargeFileSystem(new LargeFileSystemParams(Lfs));
            this.LogFsUtf8 = new Utf8BomFileSystem(new Utf8BomFileSystemParam(this.LogFs));

            if (Options.InfoLogFileName._IsFilled())
            {
                InfoLogFileObj = this.LogFsUtf8.OpenOrCreateAppend(Options.InfoLogFileName, flags: FileFlags.AutoCreateDirectory | FileFlags.BackupMode | FileFlags.LargeFs_ProhibitWriteWithCrossBorder);
                InfoLogFileStream = InfoLogFileObj.GetStream(true);
                InfoLogWriter = new StreamWriter(InfoLogFileStream);
            }

            if (Options.ErrorLogFileName._IsFilled())
            {
                ErrorLogFileObj = this.LogFsUtf8.OpenOrCreateAppend(Options.ErrorLogFileName, flags: FileFlags.AutoCreateDirectory | FileFlags.BackupMode | FileFlags.LargeFs_ProhibitWriteWithCrossBorder);
                ErrorLogFileStream = ErrorLogFileObj.GetStream(true);
                ErrorLogWriter = new StreamWriter(ErrorLogFileStream);
            }

            WriteLogAsync(DirSuperBackupLogType.Error, "------------------")._GetResult();
            WriteLogAsync(DirSuperBackupLogType.Error, "Start")._GetResult();

            WriteLogAsync(DirSuperBackupLogType.Error, Str.CombineStringArrayForCsv("Options_Flags", this.Options.Flags.ToString()))._GetResult();
            WriteLogAsync(DirSuperBackupLogType.Error, Str.CombineStringArrayForCsv("Options_NumThreads", this.Options.NumThreads.ToString()))._GetResult();
            WriteLogAsync(DirSuperBackupLogType.Error, Str.CombineStringArrayForCsv("Options_Encryption", (!this.Options.EncryptPassword._IsNullOrZeroLen()).ToString()))._GetResult();
        }
        catch
        {
            this._DisposeSafe();
            throw;
        }
    }


    protected override async Task CleanupImplAsync(Exception? ex)
    {
        try
        {
            await WriteLogAsync(DirSuperBackupLogType.Error, "---");
            await WriteLogAsync(DirSuperBackupLogType.Error, "Finish");
            await WriteLogAsync(DirSuperBackupLogType.Error, Str.CombineStringArrayForCsv("Options_Flags", this.Options.Flags.ToString()));
            await WriteLogAsync(DirSuperBackupLogType.Error, Str.CombineStringArrayForCsv("Options_NumThreads", this.Options.NumThreads.ToString()));
            await WriteLogAsync(DirSuperBackupLogType.Error, Str.CombineStringArrayForCsv("Options_Encryption", (!this.Options.EncryptPassword._IsNullOrZeroLen()).ToString()));

            await WriteStatAsync()._TryAwait();

            await WriteLogAsync(DirSuperBackupLogType.Error, "------------------")._TryAwait();

            await InfoLogWriter._DisposeSafeAsync();
            await InfoLogFileStream._DisposeSafeAsync();
            await InfoLogFileObj._DisposeSafeAsync();

            await ErrorLogWriter._DisposeSafeAsync();
            await ErrorLogFileStream._DisposeSafeAsync();
            await ErrorLogFileObj._DisposeSafeAsync();

            await this.LogFsUtf8._DisposeSafeAsync();
            await this.LogFs._DisposeSafeAsync();
        }
        finally
        {
            await base.CleanupImplAsync(ex);
        }
    }

    readonly AsyncLock WriteLogLock = new AsyncLock();

    async Task WriteLogAsync(DirSuperBackupLogType type, string str)
    {
        try
        {
            string line = $"{DateTimeOffset.Now._ToDtStr()},{str}";

            line = line._OneLine();

            using (await WriteLogLock.LockWithAwait())
            {
                lock (Con.ConsoleWriteLock)
                {
                    Console.WriteLine(line);
                }

                if (type.Bit(DirSuperBackupLogType.Error))
                {
                    await WriteLogMainAsync(this.ErrorLogWriter, line);

                    if (this.ErrorLogWriter != null)
                    {
                        await this.ErrorLogWriter.FlushAsync();
                    }
                }

                await WriteLogMainAsync(this.InfoLogWriter, line);

                if (this.InfoLogWriter != null)
                {
                    // 2023/1/3 Flush するようにした
                    await this.InfoLogWriter.FlushAsync();
                }
            }
        }
        catch
        {
        }
    }

    async Task WriteLogMainAsync(StreamWriter? writer, string line)
    {
        if (writer == null) return;

        await writer.WriteLineAsync(line);
    }

    // Stat をログに書き込む
    public async Task WriteStatAsync()
    {
        await WriteLogAsync(DirSuperBackupLogType.Error, Str.CombineStringArrayForCsv("Stat", this.Stat._ObjectToJson(compact: true)));
    }

    // 1 つのディレクトリを検証する
    public async Task DoSingleDirVerifyAsync(string localDirPath, string archivedDirPath, CancellationToken cancel = default, string? ignoreDirNames = null)
    {
        DateTimeOffset now = DateTimeOffset.Now;

        FileSystemEntity[]? archivedDirEnum = null;
        FileSystemEntity[]? localDirEnum = null;

        string[] ignoreDirNamesList = ignoreDirNames._NonNull()._RemoveQuotation()._Split(StringSplitOptions.RemoveEmptyEntries, ",", ";");

        bool noError = true;

        try
        {
            if (archivedDirPath._IsSamei(localDirPath))
            {
                throw new CoresException($"archivedDir == localDir. Directory path: '{archivedDirPath}'");
            }

            // ローカルディレクトリが存在していることを確認する
            if (await LocalFs.IsDirectoryExistsAsync(localDirPath, cancel) == false)
            {
                throw new CoresException($"The directory '{localDirPath}' not found.");
            }

            // バックアップ先ディレクトリが存在していることを確認する
            if (await ArchiveFs.IsDirectoryExistsAsync(archivedDirPath, cancel) == false)
            {
                throw new CoresException($"The directory '{archivedDirPath}' not found.");
            }

            Stat.CreateSkip_NumDirs++;

            // バックアップ先ディレクトリを列挙する
            archivedDirEnum = (await ArchiveFs.EnumDirectoryAsync(archivedDirPath, false, EnumDirectoryFlags.NoGetPhysicalSize, null, cancel)).OrderBy(x => x.Name, StrComparer.IgnoreCaseComparer).ToArray();

            // バックアップディレクトリに存在するメタデータファイルのうち最新のファイルを取得する
            // なお、メタデータファイルのパースがエラーになったら、必ずエラーを発生し中断する
            DirSuperBackupMetadata? archivedDirMetaData = await GetLatestMetaDataFileNameAsync(ArchiveFs, archivedDirPath, archivedDirEnum, cancel, throwJsonParseError: true)!;
            if (archivedDirMetaData == null)
            {
                throw new CoresException($"Metadata not found on the directory '{archivedDirPath}'.");
            }

            // バックアップ先ディレクトリのメタデータのファイル一覧をディクショナリに変形する
            StrDictionary<DirSuperBackupMetadataFile> archivesDirFileMetaDataDic = new StrDictionary<DirSuperBackupMetadataFile>(StrCmpi);
            archivedDirMetaData.FileList._DoForEach(x => archivesDirFileMetaDataDic.Add(x.FileName, x));

            // ローカルディレクトリに存在するファイルを列挙する
            localDirEnum = (await LocalFs.EnumDirectoryAsync(localDirPath, false, EnumDirectoryFlags.NoGetPhysicalSize | (this.Options.Flags.Bit(DirSuperBackupFlags.NoFileNameLenLimit) ? EnumDirectoryFlags.None : EnumDirectoryFlags.SkipTooLongFileName), null, cancel)).OrderBy(x => x.Name, StrComparer.IgnoreCaseComparer).ToArray();

            // ローカルディレクトリに存在するファイルを 1 つずつ読み出し、バックアップ先ディレクトリに存在するファイルと比較する
            var localFileEntries = localDirEnum.Where(x => x.IsFile);

            RefInt concurrentNum = new RefInt();

            AsyncLock SafeLock = new AsyncLock();
            await TaskUtil.ForEachAsync(Options.NumThreads, localFileEntries, async (localFile, taskIndex, cancel) =>
            {
                string errDescription = "GenericFileError";

                await Task.Yield();

                string archivedFilePath = ArchiveFs.PathParser.Combine(archivedDirPath, localFile.Name);

                FileMetadata? localFileMetadata = null;

                concurrentNum.Increment();
                try
                {
                    // バックアップ先のメタデータからこのファイルを検索する
                    if (archivesDirFileMetaDataDic.TryGetValue(localFile.Name, out var archivedFileMetaData2) == false)
                    {
                        // バックアップ先のメタデータ上にこのファイルが存在しない
                        errDescription = "FileNotFoundOnMetadata";
                        throw new CoresException($"File '{localFile.Name}' not found on the metadata of the directory '{archivedDirPath}'");
                    }

                    var archivedFileMetaData = archivedFileMetaData2.MetaData;

                    // ローカルファイルとバックアップ先ファイルとの主要なメタデータを比較する
                    localFileMetadata = await LocalFs.GetFileMetadataAsync(localFile.FullPath, cancel: cancel);

                    // 比較しないフィールドを消す
                    localFileMetadata.Security = null;
                    archivedFileMetaData.Security = null;
                    localFileMetadata.LastAccessTime = DtOffsetZero;
                    archivedFileMetaData.LastAccessTime = DtOffsetZero;

                    string localFileMetadataJson = localFileMetadata._ObjectToJson(compact: true);
                    string archivedFileMetadataJson = archivedFileMetaData._ObjectToJson(compact: true);

                    if (this.Options.Flags.Bit(DirSuperBackupFlags.VerifyIgnoreMetaData) == false)
                    {
                        if (localFileMetadataJson != archivedFileMetadataJson)
                        {
                            // メタデータが異なる
                            errDescription = "FileMetadataDifferent";
                            throw new CoresException($"Local metadata [{localFileMetadataJson}] != archived metadata [{archivedFileMetadataJson}]");
                        }
                    }

                    bool isEncrypted = false;
                    bool isEncryptedWithLegacyXtsAes256 = false;
                    string encryptPassword = "";

                    if (archivedFileMetaData2.EncrypedFileName._IsNullOrZeroLen() == false)
                    {
                        // 暗号化ファイルである
                        archivedFilePath = ArchiveFs.PathParser.Combine(archivedDirPath, archivedFileMetaData2.EncrypedFileName);

                        // 暗号化ファイルである
                        if (Options.EncryptPassword._IsNullOrZeroLen())
                        {
                            // パスワードが指定されていない
                            throw new CoresException($"The file '{archivedFilePath}' is encrypted, but no password is specified.");
                        }

                        isEncrypted = true;
                        encryptPassword = this.Options.EncryptPassword;

                        if (archivedFilePath.EndsWith(Consts.Extensions.EncryptedSecureCompress, StringComparison.OrdinalIgnoreCase))
                        {
                            // SecureCompress, 2023/09 ～
                            isEncryptedWithLegacyXtsAes256 = false;
                        }
                        else
                        {
                            // Legacy XTS, ～ 2023/09
                            isEncryptedWithLegacyXtsAes256 = true;
                        }
                    }

                    Ref<string> hashStr1 = new Ref<string>();
                    Ref<string> hashStr2 = new Ref<string>();

                    // 2 つのファイルの内容を比較する
                    await TaskUtil.RetryAsync(async c =>
                    {
                        ResultOrError<int> sameRet;

                        string funcName;

                        Ref<Exception?> exception = new Ref<Exception?>();

                        if (isEncrypted == false)
                        {
                            // NoCheckFileSize を付けないと、一部の Windows クライアントと一部の Samba サーバーとの間でヘンなエラーが発生する。
                            funcName = "CompareFileHashAsync";
                            sameRet = await FileUtil.CompareFileHashAsync(new FilePath(localFile.FullPath, LocalFs, flags: FileFlags.BackupMode | FileFlags.NoCheckFileSize), new FilePath(archivedFilePath, ArchiveFs, flags: FileFlags.BackupMode | FileFlags.NoCheckFileSize), cancel: cancel, hashStr1: hashStr1, hashStr2: hashStr2, exception: exception);
                        }
                        else
                        {
                            // NoCheckFileSize を付けないと、一部の Windows クライアントと一部の Samba サーバーとの間でヘンなエラーが発生する。
                            funcName = "CompareEncryptedFileHashAsync";
                            if (isEncryptedWithLegacyXtsAes256 == false)
                            {
                                // SecureCompress, 2023/09 ～
                                sameRet = await FileUtil.CompareEncryptedFileHashAsync(encryptPassword, EncryptOption.Compress | EncryptOption.Decrypt_v2_SecureCompress, new FilePath(localFile.FullPath, LocalFs, flags: FileFlags.BackupMode | FileFlags.NoCheckFileSize), new FilePath(archivedFilePath, ArchiveFs, flags: FileFlags.BackupMode | FileFlags.NoCheckFileSize), cancel: cancel, hashStr1: hashStr1, hashStr2: hashStr2, exception: exception);
                            }
                            else
                            {
                                // Legacy XTS, ～ 2023/09
                                sameRet = await FileUtil.CompareEncryptedFileHashAsync(encryptPassword, EncryptOption.Compress, new FilePath(localFile.FullPath, LocalFs, flags: FileFlags.BackupMode | FileFlags.NoCheckFileSize), new FilePath(archivedFilePath, ArchiveFs, flags: FileFlags.BackupMode | FileFlags.NoCheckFileSize), cancel: cancel, hashStr1: hashStr1, hashStr2: hashStr2, exception: exception);
                            }
                        }

                        if (sameRet.IsOk == false)
                        {
                            // ファイルの比較中にエラーが発生した
                            errDescription = "FileReadError";
                            throw new CoresException($"Compare function '{funcName}' returned an error: {exception.Value?.Message ?? "Unknown error"}");
                        }

                        if (sameRet.Value != 0)
                        {
                            // ファイルの比較結果が異なる
                            errDescription = "FileDataDifferent";
                            throw new CoresRetryableException($"Compare function '{funcName}' returned different result between two files. Local_MD5 = {hashStr1}, Remote_MD5 = {hashStr2}");
                        }

                        return 0;
                    },
                    1000,
                    4,
                    cancel,
                    true,
                    true);

                    // 合格
                    Stat.Copy_NumFiles++;
                    Stat.Copy_TotalSize += localFileMetadata.Size;

                    await WriteLogAsync(DirSuperBackupLogType.Info, Str.CombineStringArrayForCsv("FileOk", localFile.FullPath, archivedFilePath, hashStr1));
                }
                catch (Exception ex)
                {
                    Stat.Error_NumFiles++;
                    Stat.Error_TotalSize += localFile.Size;

                    // ファイル単位のエラー発生
                    await WriteLogAsync(DirSuperBackupLogType.Error, Str.CombineStringArrayForCsv(errDescription, localFile.FullPath, archivedFilePath, ex.Message));

                    noError = false;
                }
                finally
                {
                    concurrentNum.Decrement();
                }
            }, cancel: cancel);
        }
        catch (Exception ex)
        {
            Stat.Error_Dir++;

            noError = false;

            // なんか ex.Message の取得に失敗している可能性があるので少し冗長なことをする
            string errMessage = "Unknown";
            try
            {
                errMessage = ex.ToString();
                errMessage = ex.Message;
            }
            catch { }
            if (errMessage._IsEmpty()) errMessage = "Unknown2";

            ex._Error();

            // ディレクトリ単位のエラー発生
            await WriteLogAsync(DirSuperBackupLogType.Error, Str.CombineStringArrayForCsv("DirError1", localDirPath, archivedDirPath, errMessage));
        }

        if (localDirEnum != null)
        {
            bool ok = false;

            try
            {
                // ソースディレクトリの列挙に成功した場合は、サブディレクトリに対して再帰的に実行する
                foreach (var subDir in localDirEnum.Where(x => x.IsDirectory && x.IsCurrentOrParentDirectory == false))
                {
                    // シンボリックリンクは無視する
                    if (subDir.IsSymbolicLink == false || this.Options.Flags.Bit(DirSuperBackupFlags.BackupIncludeSymbolicLink))
                    {
                        // 無視リストのいずれにも合致しない場合のみ
                        if (ignoreDirNamesList.Where(x => x._IsSamei(subDir.Name)).Any() == false)
                        {
                            await DoSingleDirVerifyAsync(LocalFs.PathParser.Combine(localDirPath, subDir.Name), ArchiveFs.PathParser.Combine(archivedDirPath, subDir.Name), cancel, ignoreDirNames);
                        }
                    }
                }

                ok = true;
            }
            catch (Exception ex)
            {
                // 何らかのディレクトリ単位のエラーで catch されていないものが発生
                Stat.Error_Dir++;

                // なんか ex.Message の取得に失敗している可能性があるので少し冗長なことをする
                string errMessage = "Unknown";
                try
                {
                    errMessage = ex.ToString();
                    errMessage = ex.Message;
                }
                catch { }
                if (errMessage._IsEmpty()) errMessage = "Unknown2";

                ex._Error();

                // ディレクトリ単位のエラー発生
                await WriteLogAsync(DirSuperBackupLogType.Error, Str.CombineStringArrayForCsv("DirError2", localDirPath, archivedDirPath, errMessage));
            }

            Limbo.ObjectVolatileSlow = ok;
        }

        Limbo.ObjectVolatileSlow = noError;
    }

    // 1 つのディレクトリを復元する
    public async Task DoSingleDirRestoreAsync(string archiveDirPath, string localDirPath, CancellationToken cancel = default, string? ignoreDirNames = null)
    {
        DateTimeOffset now = DateTimeOffset.Now;

        FileSystemEntity[]? srcDirEnum = null;

        string[] ignoreDirNamesList = ignoreDirNames._NonNull()._RemoveQuotation()._Split(StringSplitOptions.RemoveEmptyEntries, ",", ";");

        try
        {
            if (archiveDirPath._IsSamei(localDirPath))
            {
                throw new CoresException($"srcDir == destDir. Directory path: '{archiveDirPath}'");
            }

            // 元ディレクトリが存在していることを確認する
            if (await ArchiveFs.IsDirectoryExistsAsync(archiveDirPath, cancel) == false)
            {
                throw new CoresException($"The directory '{archiveDirPath}' not found.");
            }

            // 元ディレクトリを列挙する
            srcDirEnum = (await ArchiveFs.EnumDirectoryAsync(archiveDirPath, false, EnumDirectoryFlags.NoGetPhysicalSize, null, cancel)).OrderBy(x => x.Name, StrComparer.IgnoreCaseComparer).ToArray();

            // 元ディレクトリに存在するメタデータファイルのうち最新のファイルを取得する
            // なお、メタデータファイルのパースがエラーになったら、必ずエラーを発生し中断する
            DirSuperBackupMetadata? dirMetaData = await GetLatestMetaDataFileNameAsync(ArchiveFs, archiveDirPath, srcDirEnum, cancel, throwJsonParseError: true)!;
            if (dirMetaData == null)
            {
                throw new CoresException($"Metadata not found on the directory '{archiveDirPath}'.");
            }

            // 先ディレクトリがまだ存在していない場合は作成をする
            if (await LocalFs.IsDirectoryExistsAsync(localDirPath, cancel) == false)
            {
                FileFlags newDirFlags = FileFlags.None;

                // ディレクトリの圧縮フラグを適切に設定する
                if (dirMetaData.DirMetadata.SpecialOperationFlags.Bit(FileSpecialOperationFlags.SetCompressionFlag) || (dirMetaData.DirMetadata.Attributes?.Bit(FileAttributes.Compressed) ?? false))
                {
                    newDirFlags |= FileFlags.OnCreateSetCompressionFlag;
                }
                else
                {
                    newDirFlags |= FileFlags.OnCreateRemoveCompressionFlag;
                }

                try
                {
                    await LocalFs.CreateDirectoryAsync(localDirPath, newDirFlags, cancel);

                    Stat.Create_NumDirs++;
                }
                catch
                {
                    // ヘンな圧縮フラグの設定に失敗した場合もう一度作成試行する
                    try
                    {
                        await LocalFs.CreateDirectoryAsync(localDirPath, FileFlags.None, cancel);

                        Stat.Create_NumDirs++;
                    }
                    catch
                    {
                        // ディレクトリ作成コマンドでなぜかエラーになっても、結果としてディレクトリが作成されればそれでよい
                        if (await LocalFs.IsDirectoryExistsAsync(localDirPath, cancel) == false)
                        {
                            // やはりディレクトリが存在しないならばここでエラーを発生させる
                            Stat.Error_NumCreateDirs++;
                            throw;
                        }

                        Stat.Create_NumDirs++;
                    }
                }
            }
            else
            {
                Stat.CreateSkip_NumDirs++;
            }

            // ディレクトリの属性を設定する
            try
            {
                var newDirMetadata = dirMetaData.DirMetadata;

                if (Options.Flags.Bit(DirSuperBackupFlags.RestoreNoAcl))
                {
                    newDirMetadata.Security = null;
                }

                await LocalFs.SetDirectoryMetadataAsync(localDirPath, newDirMetadata, cancel);
            }
            catch (Exception ex)
            {
                // ディレクトリの属性の設定に失敗したが、軽微なエラーなのでエラーを出して続行する
                await WriteLogAsync(DirSuperBackupLogType.Error, Str.CombineStringArrayForCsv("DirAttributeSetError", archiveDirPath, localDirPath, ex.Message));
                Stat.Error_Dir++;
            }

            // 元ディレクトリに存在するはずのファイル (メタデータに書いてある) を 1 つずつ復元する
            RefInt concurrentNum = new RefInt();

            AsyncLock SafeLock = new AsyncLock();

            await dirMetaData.FileList._DoForEachParallelAsync(async (srcFile, taskIndex) =>
            {
                await Task.Yield();

                string srcFilePath = ArchiveFs.PathParser.Combine(archiveDirPath, srcFile.FileName);
                string destFilePath = LocalFs.PathParser.Combine(localDirPath, srcFile.FileName);
                FileMetadata? srcFileMetadata = null;

                concurrentNum.Increment();

                try
                {
                    bool isEncrypted = false;
                    bool isEncryptedWithLegacyXtsAes256 = false;
                    string encryptPassword = "";

                    if (srcFile.EncrypedFileName._IsNullOrZeroLen() == false)
                    {
                        srcFilePath = ArchiveFs.PathParser.Combine(archiveDirPath, srcFile.EncrypedFileName);

                        // 暗号化ファイルである
                        if (Options.EncryptPassword._IsNullOrZeroLen())
                        {
                            // パスワードが指定されていない
                            throw new CoresException($"The file '{srcFilePath}' is encrypted, but no password is specified.");
                        }

                        isEncrypted = true;
                        encryptPassword = this.Options.EncryptPassword;

                        if (srcFilePath.EndsWith(Consts.Extensions.EncryptedSecureCompress, StringComparison.OrdinalIgnoreCase))
                        {
                            // SecureCompress, 2023/09 ～
                            isEncryptedWithLegacyXtsAes256 = false;
                        }
                        else
                        {
                            // Legacy XTS, ～ 2023/09
                            isEncryptedWithLegacyXtsAes256 = true;
                        }
                    }

                    srcFileMetadata = srcFile.MetaData;

                    // このファイルと同一の先ファイル名がすでに宛先ディレクトリに物理的に存在するかどうか確認する
                    bool exists = await LocalFs.IsFileExistsAsync(destFilePath, cancel);

                    bool restoreThisFile = false;

                    if (exists)
                    {
                        // すでに宛先ディレクトリに存在する物理的なファイルのメタデータを取得する
                        FileMetadata destExistsMetadata = await LocalFs.GetFileMetadataAsync(destFilePath, cancel: cancel);

                        if (Options.Flags.Bit(DirSuperBackupFlags.RestoreOnlyNewer) == false)
                        {
                            // 古いファイルも復元する
                            if (Options.Flags.Bit(DirSuperBackupFlags.RestoreDoNotSkipExactSame))
                            {
                                // 必ず上書きする
                                restoreThisFile = true;
                            }
                            else
                            {
                                // ファイルサイズを比較する
                                if (destExistsMetadata.Size != srcFileMetadata.Size)
                                {
                                    // ファイルサイズが異なる
                                    restoreThisFile = true;
                                }

                                // 日付を比較する
                                if (srcFileMetadata!.LastWriteTime!.Value.Ticks != destExistsMetadata.LastWriteTime!.Value.Ticks)
                                {
                                    // 最終更新日時が異なる
                                    restoreThisFile = true;
                                }

                                if (restoreThisFile == false)
                                {
                                    // 新旧両方のファイルが存在する場合で、ファイルサイズも日付も同じであれば、復元先ファイル内容がバックアップファイルと同一かチェックし、同一の場合はコピーをスキップする
                                    ResultOrError<int> sameRet;

                                    if (isEncrypted == false)
                                    {
                                        // NoCheckFileSize を付けないと、一部の Windows クライアントと一部の Samba サーバーとの間でヘンなエラーが発生する。
                                        sameRet = await FileUtil.CompareFileHashAsync(new FilePath(srcFilePath, ArchiveFs, flags: FileFlags.BackupMode | FileFlags.NoCheckFileSize), new FilePath(destFilePath, LocalFs, flags: FileFlags.BackupMode | FileFlags.NoCheckFileSize), cancel: cancel);
                                    }
                                    else
                                    {
                                        // NoCheckFileSize を付けないと、一部の Windows クライアントと一部の Samba サーバーとの間でヘンなエラーが発生する。
                                        if (isEncryptedWithLegacyXtsAes256)
                                        {
                                            // SecureCompress, 2023/09 ～
                                            sameRet = await FileUtil.CompareEncryptedFileHashAsync(encryptPassword, EncryptOption.Compress, new FilePath(destFilePath, LocalFs, flags: FileFlags.BackupMode | FileFlags.NoCheckFileSize), new FilePath(srcFilePath, ArchiveFs, flags: FileFlags.BackupMode | FileFlags.NoCheckFileSize), cancel: cancel);
                                        }
                                        else
                                        {
                                            // Legacy XTS, ～ 2023/09
                                            sameRet = await FileUtil.CompareEncryptedFileHashAsync(encryptPassword, EncryptOption.Compress | EncryptOption.Decrypt_v2_SecureCompress, new FilePath(destFilePath, LocalFs, flags: FileFlags.BackupMode | FileFlags.NoCheckFileSize), new FilePath(srcFilePath, ArchiveFs, flags: FileFlags.BackupMode | FileFlags.NoCheckFileSize), cancel: cancel);
                                        }
                                    }

                                    if (sameRet.IsOk == false || sameRet.Value != 0)
                                    {
                                        restoreThisFile = true;
                                    }
                                }
                            }
                        }
                        else
                        {
                            // バックアップのほうが新しい場合のみ復元するモード
                            // 日付を比較する
                            if (srcFileMetadata!.LastWriteTime!.Value.Ticks > destExistsMetadata.LastWriteTime!.Value.Ticks)
                            {
                                // 最終更新日時がバックアップのほうが新しい
                                restoreThisFile = true;
                            }
                        }
                    }
                    else
                    {
                        restoreThisFile = true;
                    }

                    if (restoreThisFile)
                    {
                        // すべての判断に合格したら、このファイルの復元を実施する
                        if (exists)
                        {
                            if (Options.Flags.Bit(DirSuperBackupFlags.RestoreMakeBackup))
                            {
                                // 復元先に同名のファイルがすでに存在する場合は、
                                // .original.xxxx.0123.original のような形式でまだ存在しない連番に古いファイル名をリネームする
                                using (await SafeLock.LockWithAwait(cancel))
                                {
                                    string newOldFileName;

                                    // 連番でかつ存在していないファイル名を決定する
                                    for (int i = 0; ; i++)
                                    {
                                        string newOldFileNameCandidate = $".original.{srcFile.FileName}.{i:D4}.original";

                                        if (await LocalFs.IsFileExistsAsync(LocalFs.PathParser.Combine(localDirPath, newOldFileNameCandidate), cancel) == false)
                                        {
                                            newOldFileName = newOldFileNameCandidate;
                                            break;
                                        }
                                    }

                                    // 変更されたファイル名を .old ファイルにリネーム実行する
                                    string newOldFilePath = LocalFs.PathParser.Combine(localDirPath, newOldFileName);
                                    await WriteLogAsync(DirSuperBackupLogType.Info, Str.CombineStringArrayForCsv("FileRename", destFilePath, newOldFilePath));
                                    await LocalFs.MoveFileAsync(destFilePath, newOldFilePath, cancel);

                                    // 隠しファイルにする
                                    try
                                    {
                                        var meta = await LocalFs.GetFileMetadataAsync(newOldFilePath, cancel: cancel);
                                        if (meta.Attributes != null)
                                        {
                                            FileMetadata meta2 = new FileMetadata(attributes: meta.Attributes?.BitAdd(FileAttributes.Hidden));

                                            await LocalFs.SetFileMetadataAsync(newOldFilePath, meta2, cancel);
                                        }
                                    }
                                    catch { }
                                }
                            }
                        }

                        // 復元メイン
                        await WriteLogAsync(DirSuperBackupLogType.Info, Str.CombineStringArrayForCsv("FileCopy", srcFilePath, destFilePath));

                        FileFlags flags = FileFlags.BackupMode | FileFlags.Async;

                        bool ignoreReadError = false;

                        if (this.Options.Flags.Bit(DirSuperBackupFlags.IgnoreReadError) == false)
                        {
                            ignoreReadError = true;
                        }
                        else if (this.Options.Flags.Bit(DirSuperBackupFlags.RestoreNoVerify) == false)
                        {
                            flags |= FileFlags.CopyFile_Verify;
                        }

                        Ref<string> actualMd5hash = new Ref<string>();

                        string? metaDataMD5Hash = srcFile.Md5;

                        bool checkMd5 = false;
                        if (metaDataMD5Hash._IsFilled() && (this.Options.Flags.Bit(DirSuperBackupFlags.RestoreNoMd5) == false))
                        {
                            checkMd5 = true;
                        }

                        RefBool dstWriteErrorRecovered = false;
                        RefBool md5ChecksumErrorRecovered = false;

                        // ファイルをコピーする
                        await TaskUtil.RetryAsync(async c =>
                        {
                            await ArchiveFs.CopyFileAsync(srcFilePath, destFilePath,
                                new CopyFileParams(flags: flags,
                                    metadataCopier: new FileMetadataCopier(FileMetadataCopyMode.TimeAll),
                                    encryptOption: isEncrypted ? (isEncryptedWithLegacyXtsAes256 ? EncryptOption.Decrypt_v1_XtsLts : EncryptOption.Decrypt_v2_SecureCompress) | EncryptOption.Compress : EncryptOption.None,
                                    encryptPassword: encryptPassword,
                                    calcDigest: checkMd5,
                                    retryCount: 0,
                                    ignoreReadError: ignoreReadError),
                                cancel: cancel,
                                newFileMeatadata: Options.Flags.Bit(DirSuperBackupFlags.RestoreNoAcl) ? null : srcFileMetadata,
                                digest: actualMd5hash, errorOccuredButRecovered: dstWriteErrorRecovered,
                                destFileSystem: LocalFs);

                            if (checkMd5 && actualMd5hash.Value._IsFilled())
                            {
                                if (actualMd5hash.Value._IsSamei(metaDataMD5Hash) == false)
                                {
                                    md5ChecksumErrorRecovered.Set(true);
                                    throw new CoresRetryableException($"Different MD5 hash between metadata and actual file. MD5_Metadata = '{metaDataMD5Hash}', MD5_ActualFile = '{actualMd5hash.Value}'");
                                }
                            }

                            return 0;
                        },
                        1000,
                        4,
                        cancel,
                        true,
                        true);

                        if (md5ChecksumErrorRecovered || dstWriteErrorRecovered)
                        {
                            Stat.RecoveredError_NumFiles++;
                            Stat.RecoveredError_TotalSize += srcFile.MetaData.Size;
                        }

                        Stat.Copy_NumFiles++;
                        Stat.Copy_TotalSize += srcFile.MetaData.Size;

                        // メタデータを再度復元
                        try
                        {
                            var meta = Options.Flags.Bit(DirSuperBackupFlags.RestoreNoAcl) ? srcFileMetadata.Clone(FileMetadataCopyMode.TimeAll | FileMetadataCopyMode.Attributes | FileMetadataCopyMode.ReplicateArchiveBit | FileMetadataCopyMode.AlternateStream | FileMetadataCopyMode.Author) : srcFileMetadata;

                            await LocalFs.SetFileMetadataAsync(destFilePath, meta, cancel);
                        }
                        catch (Exception ex)
                        {
                            FileMetadata? existingFileMetadata = null;
                            try
                            {
                                existingFileMetadata = await LocalFs.GetFileMetadataAsync(destFilePath, cancel: cancel);
                            }
                            catch { }
                            if ((existingFileMetadata?.Attributes?.Bit(FileAttributes.ReadOnly) ?? true) == false)
                            {
                                // メタデータの属性の設定に失敗したが、軽微なエラーなのでエラーを出して続行する
                                // (宛先ファイルが ReadOnly の場合は、この操作はエラーとなる可能性が高い。このような場合は、予期されている動作なので、エラーは表示しない)
                                await WriteLogAsync(DirSuperBackupLogType.Error, Str.CombineStringArrayForCsv("FileAttributeSetError", srcFilePath, destFilePath, ex.Message));
                                Stat.Error_NumFiles++;
                            }
                        }
                    }
                    else
                    {
                        Stat.Skip_NumFiles++;
                        Stat.Skip_TotalSize += srcFile.MetaData.Size;
                    }
                }
                catch (Exception ex)
                {
                    Stat.Error_NumFiles++;
                    Stat.Error_TotalSize += srcFileMetadata?.Size ?? 0;

                    // ファイル単位のエラー発生
                    await WriteLogAsync(DirSuperBackupLogType.Error, Str.CombineStringArrayForCsv("FileError", srcFilePath, destFilePath, ex.Message));
                }
                finally
                {
                    concurrentNum.Decrement();
                }
            },
            Options.NumThreads, MultitaskDivideOperation.RoundRobin, cancel);

            // このディレクトリの全ファイルの復元が終わったら、ディレクトリのタイムスタンプ情報を再書き込みする
            // (中のファイルが新しくなったことが原因で、ディレクトリの更新日時が新しくなってしまう可能性があるためである)
            try
            {
                await LocalFs.SetFileMetadataAsync(localDirPath, dirMetaData.DirMetadata.Clone(FileMetadataCopyMode.TimeAll), cancel);
            }
            catch
            {
                // 属性書き込みは失敗してもよい
            }

            try
            {
                // ソースディレクトリの列挙に成功した場合は、サブディレクトリに対して再帰的に実行する
                foreach (var subDir in dirMetaData.DirList)
                {
                    // 無視リストのいずれにも合致しない場合のみ
                    if (ignoreDirNamesList.Where(x => x._IsSamei(subDir)).Any() == false)
                    {
                        await DoSingleDirRestoreAsync(ArchiveFs.PathParser.Combine(archiveDirPath, subDir), LocalFs.PathParser.Combine(localDirPath, subDir), cancel, ignoreDirNames);
                    }
                }
            }
            catch (Exception ex)
            {
                // 何らかのディレクトリ単位のエラーで catch されていないものが発生
                Stat.Error_Dir++;

                // ディレクトリ単位のエラー発生
                await WriteLogAsync(DirSuperBackupLogType.Error, Str.CombineStringArrayForCsv("DirError1", archiveDirPath, localDirPath, ex.Message));
            }

            // 再度 宛先ディレクトリの日付情報のみ属性書き込みする (Linux の場合、中のファイルを更新するとディレクトリの日時が変ってしまうため)
            try
            {
                if (dirMetaData != null)
                {
                    await LocalFs.SetFileMetadataAsync(localDirPath, dirMetaData.DirMetadata.Clone(FileMetadataCopyMode.TimeAll), cancel);
                }
            }
            catch
            {
                // 属性書き込みは失敗してもよい
            }
        }
        catch (Exception ex)
        {
            Stat.Error_Dir++;

            // ディレクトリ単位のエラー発生
            await WriteLogAsync(DirSuperBackupLogType.Error, Str.CombineStringArrayForCsv("DirError2", archiveDirPath, localDirPath, ex.Message));
        }
    }

    // 1 つのディレクトリをバックアップする
    public async Task DoSingleDirBackupAsync(string localDirPath, string archiveDirPath, CancellationToken cancel = default, string? ignoreDirNames = null)
    {
        DateTimeOffset now = DateTimeOffset.Now;

        FileSystemEntity[]? srcDirEnum = null;

        string[] ignoreDirNamesList = ignoreDirNames._NonNull()._RemoveQuotation()._Split(StringSplitOptions.RemoveEmptyEntries, ",", ";");

        FileMetadata? srcDirMetadata = null;

        bool noError = true;

        try
        {
            if (localDirPath._IsSamei(archiveDirPath))
            {
                throw new CoresException($"srcDir == destDir. Directory path: '{localDirPath}'");
            }

            srcDirMetadata = await LocalFs.GetDirectoryMetadataAsync(localDirPath, cancel: cancel);

            srcDirEnum = (await LocalFs.EnumDirectoryAsync(localDirPath, false, EnumDirectoryFlags.NoGetPhysicalSize | (this.Options.Flags.Bit(DirSuperBackupFlags.NoFileNameLenLimit) ? EnumDirectoryFlags.None : EnumDirectoryFlags.SkipTooLongFileName), null, cancel)).OrderBy(x => x.Name, StrComparer.IgnoreCaseComparer).ToArray();

            FileSystemEntity[] destDirEnum = new FileSystemEntity[0];

            DirSuperBackupMetadata? destDirOldMetaData = null;

            DirSuperBackupMetadata destDirNewMetaData;

            // 宛先ディレクトリがすでに存在しているかどうか検査する
            if (await ArchiveFs.IsDirectoryExistsAsync(archiveDirPath, cancel))
            {
                Stat.CreateSkip_NumDirs++;

                destDirEnum = await ArchiveFs.EnumDirectoryAsync(archiveDirPath, false, EnumDirectoryFlags.NoGetPhysicalSize, null, cancel);

                // 宛先ディレクトリに存在するメタデータファイルのうち最新のファイルを取得する
                destDirOldMetaData = await GetLatestMetaDataFileNameAsync(ArchiveFs, archiveDirPath, destDirEnum, cancel);
            }
            else
            {
                // 宛先ディレクトリがまだ存在していない場合は作成する
                try
                {
                    await ArchiveFs.CreateDirectoryAsync(archiveDirPath, FileFlags.BackupMode | FileFlags.AutoCreateDirectory, cancel);
                    Stat.Create_NumDirs++;
                }
                catch
                {
                    Stat.Error_NumCreateDirs++;
                    throw;
                }
            }

            // 宛先ディレクトリの日付情報のみ属性書き込みする
            try
            {
                await ArchiveFs.SetFileMetadataAsync(archiveDirPath, srcDirMetadata.Clone(FileMetadataCopyMode.TimeAll), cancel);
            }
            catch
            {
                // 属性書き込みは失敗してもよい
            }

            // 新しいメタデータを作成する
            destDirNewMetaData = new DirSuperBackupMetadata();
            destDirNewMetaData.FileList = new List<DirSuperBackupMetadataFile>();
            destDirNewMetaData.TimeStamp = now;
            destDirNewMetaData.DirMetadata = srcDirMetadata;
            destDirNewMetaData.DirList = new List<string>();

            // 元ディレクトリに存在するサブディレクトリ名一覧をメタデータに追記する
            foreach (var subDir in srcDirEnum.Where(x => x.IsDirectory && x.IsCurrentOrParentDirectory == false))
            {
                // シンボリックリンクは無視する
                if (subDir.IsSymbolicLink == false || this.Options.Flags.Bit(DirSuperBackupFlags.BackupIncludeSymbolicLink))
                {
                    // 無視リストのいずれにも合致しない場合のみ
                    if (ignoreDirNamesList.Where(x => x._IsSamei(subDir.Name)).Any() == false)
                    {
                        destDirNewMetaData.DirList.Add(subDir.Name);
                    }
                }
            }

            // 元ディレクトリに存在するファイルを 1 つずつバックアップする
            var fileEntries = srcDirEnum.Where(x => x.IsFile);

            RefInt concurrentNum = new RefInt();

            AsyncLock SafeLock = new AsyncLock();

            await fileEntries._DoForEachParallelAsync(async (srcFile, taskIndex) =>
            {
                long? encryptedPhysicalSize = null;

                await Task.Yield();

                string destFilePath = ArchiveFs.PathParser.Combine(archiveDirPath, srcFile.Name);

                if (Options.EncryptPassword._IsNullOrZeroLen() == false)
                {
                    if (Options.Flags.Bit(DirSuperBackupFlags.UseLegacyXtsAes256) == false)
                    {
                        // SecureCompress, 2023/09 ～
                        destFilePath += Consts.Extensions.EncryptedSecureCompress;
                    }
                    else
                    {
                        // Legacy XTS, ～ 2023/09
                        destFilePath += Consts.Extensions.CompressedXtsAes256;
                    }
                }

                FileMetadata? srcFileMetadata = null;

                concurrentNum.Increment();

                Ref<string> md5hash = new Ref<string>();

                try
                {
                    srcFileMetadata = await LocalFs.GetFileMetadataAsync(srcFile.FullPath, cancel: cancel);

                    // このファイルと同一のファイル名がすでに宛先ディレクトリに物理的に存在するかどうか確認する
                    bool exists = await ArchiveFs.IsFileExistsAsync(destFilePath, cancel);

                    bool fileChangedOrNew = false;

                    if (exists)
                    {
                        // すでに宛先ディレクトリに存在する物理的なファイルのメタデータを取得する
                        FileMetadata destExistsMetadata = await ArchiveFs.GetFileMetadataAsync(destFilePath, cancel: cancel);

                        if (Options.EncryptPassword._IsNullOrZeroLen())
                        {
                            // 暗号化なし
                            // ファイルサイズを比較する
                            if (destExistsMetadata.Size != srcFile.Size)
                            {
                                // ファイルサイズが異なる
                                fileChangedOrNew = true;
                            }
                        }
                        else
                        {
                            // 暗号化あり
                            // 宛先ディレクトリのメタデータにファイル情報が存在し、そのメタデータ情報におけるファイルサイズが元ファイルと同じであり、
                            // かつそのメタデータ情報に記載されている EncryptedPhysicalSize が宛先ディレクトリにある物理ファイルと全く同一である
                            // 場合は、宛先ファイルが正しく存在すると仮定する

                            string tmp1 = ArchiveFs.PathParser.GetFileName(destFilePath);
                            if ((destDirOldMetaData?.FileList.Where(x => x.EncrypedFileName._IsSamei(tmp1) && x.EncryptedPhysicalSize == destExistsMetadata.Size && x.MetaData.Size == srcFile.Size).Any() ?? false) == false)
                            {
                                // ファイルサイズが異なるとみなす
                                fileChangedOrNew = true;
                            }
                        }

                        bool usePhysicalFileUpdateDate = false;

                        // 日付を比較する。ただし宛先ディレクトリの物理的なファイルの日付は信用できないので、メタデータ上のファイルサイズと比較する
                        if (destDirOldMetaData != null)
                        {
                            DirSuperBackupMetadataFile? existsFileMetadataFromDirMetadata = destDirOldMetaData.FileList.Where(x => x.FileName._IsSamei(srcFile.Name)).SingleOrDefault();
                            if (existsFileMetadataFromDirMetadata == null)
                            {
                                // メタデータ上に存在しない場合、仕方がないので宛先の物理的なファイルの日付と比較する
                                usePhysicalFileUpdateDate = true;
                            }
                            else
                            {
                                if (existsFileMetadataFromDirMetadata.MetaData!.LastWriteTime!.Value.Ticks != srcFileMetadata.LastWriteTime!.Value.Ticks)
                                {
                                    // 最終更新日時が異なる
                                    fileChangedOrNew = true;
                                }
                            }
                        }
                        else
                        {
                            // 宛先ディレクトリ上にメタデータがない場合、仕方がないので宛先の物理的なファイルの日付と比較する
                            usePhysicalFileUpdateDate = true;
                        }

                        if (usePhysicalFileUpdateDate)
                        {
                            // 仕方がないので宛先の物理的なファイルの日付と比較する
                            var diff = destExistsMetadata.LastWriteTime!.Value - srcFileMetadata.LastWriteTime!.Value;
                            diff = new TimeSpan(Math.Abs(diff.Ticks));
                            if (diff > TimeSpan.FromSeconds(3))
                            {
                                // 宛先の物理的なファイルと比較する場合は、3 秒以上ずれている場合のみ更新があったとみなす
                                fileChangedOrNew = true;
                            }
                        }
                    }
                    else
                    {
                        // 新しいファイルである
                        fileChangedOrNew = true;
                    }

                    string? oldFilePathToDelete = null;

                    if (fileChangedOrNew)
                    {
                        // ファイルが新しいか、または更新された場合は、そのファイルをバックアップする
                        // ただし、バックアップ先に同名のファイルがすでに存在する場合は、
                        // .old.xxxx.YYYYMMDD_HHMMSS.0123._backup_history のような形式でまだ存在しない連番に古いファイル名をリネームする

                        if (exists)
                        {
                            using (await SafeLock.LockWithAwait(cancel))
                            {
                                string yymmdd = Str.DateTimeToStrShort(DateTime.UtcNow);
                                string newOldFileName;

                                // 連番でかつ存在していないファイル名を決定する
                                for (int i = 0; ; i++)
                                {
                                    string newOldFileNameCandidate = $".old.{ArchiveFs.PathParser.GetFileName(destFilePath)}.{yymmdd}.{i:D4}{Consts.Extensions.DirSuperBackupHistory}";

                                    if (srcDirEnum.Where(x => x.Name._IsSamei(newOldFileNameCandidate)).Any() == false)
                                    {
                                        if (await ArchiveFs.IsFileExistsAsync(ArchiveFs.PathParser.Combine(archiveDirPath, newOldFileNameCandidate), cancel) == false)
                                        {
                                            newOldFileName = newOldFileNameCandidate;
                                            break;
                                        }
                                    }
                                }

                                // 変更されたファイル名を ._backup_history ファイルにリネーム実行する
                                string newOldFilePath = ArchiveFs.PathParser.Combine(archiveDirPath, newOldFileName);
                                await WriteLogAsync(DirSuperBackupLogType.Info, Str.CombineStringArrayForCsv("FileRename", destFilePath, newOldFilePath));
                                await ArchiveFs.MoveFileAsync(destFilePath, newOldFilePath, cancel);

                                oldFilePathToDelete = newOldFilePath;

                                // 隠しファイルにする
                                try
                                {
                                    var meta = await ArchiveFs.GetFileMetadataAsync(newOldFilePath, cancel: cancel);
                                    if (meta.Attributes != null && meta.Attributes.Bit(FileAttributes.Hidden) == false)
                                    {
                                        FileMetadata meta2 = new FileMetadata(attributes: meta.Attributes.BitAdd(FileAttributes.Hidden));

                                        await ArchiveFs.SetFileMetadataAsync(newOldFilePath, meta2, cancel);
                                    }
                                }
                                catch { }
                            }
                        }

                        // ファイルをコピーする
                        // 属性は、ファイルの日付情報のみコピーする
                        await WriteLogAsync(DirSuperBackupLogType.Info, Str.CombineStringArrayForCsv("FileCopy", srcFile.FullPath, destFilePath));

                        FileFlags flags = FileFlags.BackupMode | FileFlags.Async;

                        bool ignoreReadError = false;

                        if (this.Options.Flags.Bit(DirSuperBackupFlags.IgnoreReadError) == false)
                        {
                            ignoreReadError = true;
                        }
                        else if (this.Options.Flags.Bit(DirSuperBackupFlags.BackupNoVerify) == false)
                        {
                            flags |= FileFlags.CopyFile_Verify;
                        }

                        RefBool verifyErrorRecovered = false;

                        await LocalFs.CopyFileAsync(srcFile.FullPath, destFilePath,
                            new CopyFileParams(flags: flags, metadataCopier: new FileMetadataCopier(FileMetadataCopyMode.TimeAll),
                            ignoreReadError: ignoreReadError,
                            encryptOption: Options.EncryptPassword._IsNullOrZeroLen() ? EncryptOption.None : this.Options.Flags.Bit(DirSuperBackupFlags.UseLegacyXtsAes256) ? (EncryptOption.Encrypt_v1_XtsLts | EncryptOption.Compress) : (EncryptOption.Encrypt_v2_SecureCompress | EncryptOption.Compress),
                            encryptPassword: Options.EncryptPassword, deleteFileIfVerifyFailed: true, calcDigest: this.Options.Flags.Bit(DirSuperBackupFlags.BackupNoMd5) == false),
                            cancel: cancel, digest: md5hash, errorOccuredButRecovered: verifyErrorRecovered,
                            destFileSystem: ArchiveFs);

                        if (verifyErrorRecovered)
                        {
                            Stat.RecoveredError_NumFiles++;
                            Stat.RecoveredError_TotalSize += srcFile.Size;
                        }

                        try
                        {
                            if (Options.EncryptPassword._IsNullOrZeroLen() == false)
                            {
                                var newFileMetadata = await ArchiveFs.GetFileMetadataAsync(destFilePath, FileMetadataGetFlags.NoPhysicalFileSize, cancel);

                                encryptedPhysicalSize = newFileMetadata.Size;
                            }
                        }
                        catch
                        {
                        }

                        if (Options.Flags.Bit(DirSuperBackupFlags.BackupMakeHistory) == false)
                        {
                            // History を残さない場合
                            // コピーに成功したので ._backup_history ファイルは削除する
                            if (oldFilePathToDelete._IsNotZeroLen())
                            {
                                try
                                {
                                    await ArchiveFs.DeleteFileAsync(oldFilePathToDelete, flags: FileFlags.BackupMode | FileFlags.ForceClearReadOnlyOrHiddenBitsOnNeed, cancel);
                                }
                                catch
                                {
                                }
                            }
                        }

                        Stat.Copy_NumFiles++;
                        Stat.Copy_TotalSize += srcFile.Size;
                    }
                    else
                    {
                        if (Options.EncryptPassword._IsNullOrZeroLen() == false)
                        {
                            string tmp1 = ArchiveFs.PathParser.GetFileName(destFilePath);

                            var currentFileInfoInMetaData = destDirOldMetaData?.FileList.Where(x => x.EncrypedFileName._IsSame(tmp1) && x.MetaData.Size == srcFile.Size).FirstOrDefault();

                            encryptedPhysicalSize = currentFileInfoInMetaData?.EncryptedPhysicalSize;

                            if (encryptedPhysicalSize.HasValue == false)
                            {
                                encryptedPhysicalSize = currentFileInfoInMetaData?.EncryptedPhysicalSize;
                            }

                            md5hash.Set(currentFileInfoInMetaData!.Md5._NonNull());
                        }

                        Stat.Skip_NumFiles++;
                        Stat.Skip_TotalSize += srcFile.Size;

                        // ファイルの日付情報のみ更新する
                        try
                        {
                            await ArchiveFs.SetFileMetadataAsync(destFilePath, srcFileMetadata.Clone(FileMetadataCopyMode.TimeAll), cancel);
                        }
                        catch
                        {
                            // ファイルの日付情報の更新は失敗してもよい
                        }
                    }
                }
                catch (Exception ex)
                {
                    Stat.Error_NumFiles++;
                    Stat.Error_TotalSize += srcFile.Size;

                    // ファイル単位のエラー発生
                    await WriteLogAsync(DirSuperBackupLogType.Error, Str.CombineStringArrayForCsv("FileError", srcFile.FullPath, destFilePath, ex.Message));

                    noError = false;
                }
                finally
                {
                    concurrentNum.Decrement();
                }

                // このファイルに関するメタデータを追加する
                if (srcFileMetadata != null)
                {
                    lock (destDirNewMetaData.FileList)
                    {
                        destDirNewMetaData.FileList.Add(new DirSuperBackupMetadataFile()
                        {
                            FileName = srcFile.Name,
                            EncrypedFileName = Options.EncryptPassword._IsNullOrZeroLen() ? null : srcFile.Name + (Options.Flags.Bit(DirSuperBackupFlags.UseLegacyXtsAes256) == false ? Consts.Extensions.EncryptedSecureCompress : Consts.Extensions.CompressedXtsAes256),
                            MetaData = srcFileMetadata,
                            EncryptedPhysicalSize = encryptedPhysicalSize,
                            Md5 = md5hash.Value._NullIfZeroLen(),
                        });
                    }
                }
            },
            Options.NumThreads, MultitaskDivideOperation.RoundRobin, cancel);

            // 新しいメタデータをファイル名でソートする
            destDirNewMetaData.FileList = destDirNewMetaData.FileList.OrderBy(x => x.FileName, StrComparer.IgnoreCaseComparer).ToList();
            destDirNewMetaData.DirList = destDirNewMetaData.DirList.OrderBy(x => x, StrComparer.IgnoreCaseComparer).ToList();

            // 新しいメタデータを書き込む
            string newMetadataFilePath = ArchiveFs.PathParser.Combine(archiveDirPath, $"{PrefixMetadata}{Str.DateTimeToStrShortWithMilliSecs(now.UtcDateTime)}{SuffixMetadata}");

            await ArchiveFs.WriteJsonToFileAsync(newMetadataFilePath, destDirNewMetaData, FileFlags.BackupMode | FileFlags.OnCreateSetCompressionFlag, cancel: cancel);
        }
        catch (Exception ex)
        {
            Stat.Error_Dir++;

            noError = false;

            // なんか ex.Message の取得に失敗している可能性があるので少し冗長なことをする
            string errMessage = "Unknown";
            try
            {
                errMessage = ex.ToString();
                errMessage = ex.Message;
            }
            catch { }
            if (errMessage._IsEmpty()) errMessage = "Unknown2";

            ex._Error();

            // ディレクトリ単位のエラー発生
            await WriteLogAsync(DirSuperBackupLogType.Error, Str.CombineStringArrayForCsv("DirError1", localDirPath, archiveDirPath, errMessage));
        }

        // 再度 宛先ディレクトリの日付情報のみ属性書き込みする (Linux の場合、中のファイルを更新するとディレクトリの日時が変ってしまうため)
        try
        {
            if (srcDirMetadata != null)
            {
                await ArchiveFs.SetFileMetadataAsync(archiveDirPath, srcDirMetadata.Clone(FileMetadataCopyMode.TimeAll), cancel);
            }
        }
        catch
        {
            // 属性書き込みは失敗してもよい
        }

        if (srcDirEnum != null)
        {
            bool ok = false;

            try
            {
                // ソースディレクトリの列挙に成功した場合は、サブディレクトリに対して再帰的に実行する
                foreach (var subDir in srcDirEnum.Where(x => x.IsDirectory && x.IsCurrentOrParentDirectory == false))
                {
                    // シンボリックリンクは無視する
                    if (subDir.IsSymbolicLink == false || this.Options.Flags.Bit(DirSuperBackupFlags.BackupIncludeSymbolicLink))
                    {
                        // 無視リストのいずれにも合致しない場合のみ
                        if (ignoreDirNamesList.Where(x => x._IsSamei(subDir.Name)).Any() == false)
                        {
                            await DoSingleDirBackupAsync(LocalFs.PathParser.Combine(localDirPath, subDir.Name), ArchiveFs.PathParser.Combine(archiveDirPath, subDir.Name), cancel, ignoreDirNames);
                        }
                    }
                }

                ok = true;
            }
            catch (Exception ex)
            {
                // 何らかのディレクトリ単位のエラーで catch されていないものが発生
                Stat.Error_Dir++;

                // なんか ex.Message の取得に失敗している可能性があるので少し冗長なことをする
                string errMessage = "Unknown";
                try
                {
                    errMessage = ex.ToString();
                    errMessage = ex.Message;
                }
                catch { }
                if (errMessage._IsEmpty()) errMessage = "Unknown2";

                ex._Error();

                // ディレクトリ単位のエラー発生
                await WriteLogAsync(DirSuperBackupLogType.Error, Str.CombineStringArrayForCsv("DirError2", localDirPath, archiveDirPath, errMessage));
            }

            if (ok)
            {
                if (noError)
                {
                    // ここまでの処理で何も問題がなければ (このディレクトリ内のすべてのファイルのコピーやメタデータの更新に成功しているなであれば)
                    // Sync オプションが付与されている場合、不要なサブディレクトリとファイルを削除する

                    if (this.Options.Flags.Bit(DirSuperBackupFlags.BackupSync))
                    {
                        try
                        {
                            // 両方のディレクトリを再列挙いたします
                            var srcDirEnum2 = (await LocalFs.EnumDirectoryAsync(localDirPath, false, EnumDirectoryFlags.NoGetPhysicalSize | (this.Options.Flags.Bit(DirSuperBackupFlags.NoFileNameLenLimit) ? EnumDirectoryFlags.None : EnumDirectoryFlags.SkipTooLongFileName), null, cancel)).OrderBy(x => x.Name, StrComparer.IgnoreCaseComparer).ToArray();
                            var destDirEnum2 = (await ArchiveFs.EnumDirectoryAsync(archiveDirPath, false, EnumDirectoryFlags.NoGetPhysicalSize, null, cancel)).OrderBy(x => x.Name, StrComparer.IgnoreCaseComparer).ToArray();

                            // 余分なファイルを削除いたします
                            var extraFiles = destDirEnum2.Where(x => x.IsFile && x.IsSymbolicLink == false)
                                .Where(x => x.Name._StartWithi(DirSuperBackup.PrefixMetadata) == false && x.Name._EndsWithi(DirSuperBackup.SuffixMetadata) == false)
                                .Where(x => srcDirEnum2.Where(y => y.IsFile && y.Name._IsSameiTrim(x.Name)).Any() == false)
                                .Where(x => srcDirEnum2.Where(y => y.IsFile && ((y.Name + Consts.Extensions.CompressedXtsAes256)._IsSameiTrim(x.Name) || (y.Name + Consts.Extensions.EncryptedSecureCompress)._IsSameiTrim(x.Name))).Any() == false);

                            foreach (var extraFile in extraFiles)
                            {
                                string fullPath = ArchiveFs.PathParser.Combine(archiveDirPath, extraFile.Name);

                                await WriteLogAsync(DirSuperBackupLogType.Info, Str.CombineStringArrayForCsv("DirSyncDeleteFile", fullPath));

                                try
                                {
                                    await ArchiveFs.DeleteFileAsync(fullPath, FileFlags.BackupMode | FileFlags.ForceClearReadOnlyOrHiddenBitsOnNeed, cancel);

                                    Stat.SyncDelete_NumFiles++;
                                }
                                catch (Exception ex)
                                {
                                    Stat.Error_NumDeleteFiles++;
                                    await WriteLogAsync(DirSuperBackupLogType.Error, Str.CombineStringArrayForCsv("DirSyncDeleteFileError", fullPath, ex.Message));
                                }
                            }

                            // 余分なサブディレクトリを削除いたします
                            var extraSubDirs = destDirEnum2.Where(x => x.IsDirectory && x.IsCurrentOrParentDirectory == false && x.IsSymbolicLink == false)
                               .Where(x => srcDirEnum2.Where(y => y.IsDirectory && y.IsCurrentOrParentDirectory == false && y.Name._IsSameiTrim(x.Name)).Any() == false);

                            foreach (var extraSubDir in extraSubDirs)
                            {
                                string fullPath = ArchiveFs.PathParser.Combine(archiveDirPath, extraSubDir.Name);

                                await WriteLogAsync(DirSuperBackupLogType.Info, Str.CombineStringArrayForCsv("DirSyncDeleteSubDir", fullPath));

                                try
                                {
                                    await ArchiveFs.DeleteDirectoryAsync(fullPath, true, cancel);

                                    Stat.SyncDelete_NumDirs++;
                                }
                                catch (Exception ex)
                                {
                                    Stat.Error_NumDeleteDirs++;
                                    await WriteLogAsync(DirSuperBackupLogType.Error, Str.CombineStringArrayForCsv("DirSyncDeleteSubDirError", fullPath, ex.Message));
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            // 何らかのディレクトリ単位のエラーで catch されていないものが発生
                            Stat.Error_Dir++;

                            // なんか ex.Message の取得に失敗している可能性があるので少し冗長なことをする
                            string errMessage = "Unknown";
                            try
                            {
                                errMessage = ex.ToString();
                                errMessage = ex.Message;
                            }
                            catch { }
                            if (errMessage._IsEmpty()) errMessage = "Unknown2";

                            ex._Error();

                            // ディレクトリ単位のエラー発生
                            await WriteLogAsync(DirSuperBackupLogType.Error, Str.CombineStringArrayForCsv("DirSyncEnumError", localDirPath, archiveDirPath, errMessage));
                        }
                    }
                }
            }
        }


        // 再度 宛先ディレクトリの日付情報のみ属性書き込みする (Linux の場合、中のファイルを更新するとディレクトリの日時が変ってしまうため)
        try
        {
            if (srcDirMetadata != null)
            {
                await ArchiveFs.SetFileMetadataAsync(archiveDirPath, srcDirMetadata.Clone(FileMetadataCopyMode.TimeAll), cancel);
            }
        }
        catch
        {
            // 属性書き込みは失敗してもよい
        }
    }

    // 指定されたディレクトリに存在するメタデータファイルのうち最新のファイルを取得する
    async Task<DirSuperBackupMetadata?> GetLatestMetaDataFileNameAsync(FileSystem fileSystem, string dirPath, FileSystemEntity[] dirList, CancellationToken cancel = default, bool throwJsonParseError = false)
    {
        // ディレクトリにあるファイル一覧を列挙し、メタデータファイル名を降順ソートする
        List<string> fileNameCandidates = new List<string>();

        foreach (var e in dirList)
        {
            if (e.Name.StartsWith(PrefixMetadata, StringComparison.OrdinalIgnoreCase) && e.Name.EndsWith(SuffixMetadata, StringComparison.OrdinalIgnoreCase))
            {
                fileNameCandidates.Add(e.Name);
            }
        }

        fileNameCandidates.Sort(StrComparer.IgnoreCaseComparer);

        fileNameCandidates.Reverse();

        foreach (string fileName in fileNameCandidates)
        {
            string fullPath = fileSystem.PathParser.Combine(dirPath, fileName);

            DirSuperBackupMetadata? ret = await fileSystem.ReadJsonFromFileAsync<DirSuperBackupMetadata>(fullPath, nullIfError: !throwJsonParseError, flags: FileFlags.BackupMode);

            if (ret != null) return ret;
        }

        return null;
    }
}

#endif

