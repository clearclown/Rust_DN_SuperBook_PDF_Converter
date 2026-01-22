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
// 汎用ファイルコンテナ操作クラス

#if true

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
using System.Diagnostics.CodeAnalysis;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

namespace IPA.Cores.Basic;

[Flags]
public enum FileContainerFlags : ulong
{
    None = 0,
    // モード
    Read = 1,
    CreateNewSequential = 2,
}

[Flags]
public enum FileContainerEntityFlags : ulong
{
    None = 0,
    EnableCompression = 1,      // 通常の圧縮を ON にする
    CompressionMode_Fast = 2,   // 速度優先の圧縮
    FileNameUseShiftJisIfPossible = 4,  // ファイル名が SHIFT_JIS で表現可能な場合は SHIFT_JIS 化する
    CompressionMode_SmallestSize = 8,   // 最小サイズへの圧縮
}

[Serializable]
public sealed class FileContainerEntityParam : IValidatable
{
    public string PathString { get; internal set; }
    public FileMetadata MetaData { get; internal set; }
    public FileContainerEntityFlags Flags { get; internal set; }
    public string EncodingWebName { get; internal set; }
    public string EncryptPassword { get; internal set; }

    public bool IsEncryptionEnabled => !EncryptPassword._IsNullOrZeroLen();

    public FileContainerEntityParam(string? pathString = null, FileMetadata? metaData = null, FileContainerEntityFlags flags = FileContainerEntityFlags.None, Encoding? encoding = null,
        string? encryptPassword = null)
    {
        pathString = pathString._NonNull();

        PathString = pathString;
        MetaData = metaData ?? new FileMetadata();
        Flags = flags;

        encoding ??= Str.Utf8Encoding;

        this.EncodingWebName = encoding.WebName;

        this.EncryptPassword = encryptPassword._NonNull();
    }

    public Encoding GetEncoding()
    {
        bool useShiftJis = false;

        if (this.Flags.Bit(FileContainerEntityFlags.FileNameUseShiftJisIfPossible))
        {
            if (Str.IsSuitableEncodingForString(PathString, Str.ShiftJisEncoding))
            {
                useShiftJis = true;
            }
        }

        if (useShiftJis == false)
        {
            return Encoding.GetEncoding(this.EncodingWebName);
        }
        else
        {
            return Encoding.GetEncoding(Str.ShiftJisEncoding.WebName);
        }
    }

    public void Validate()
    {
        this.PathString._FilledOrException();
        this.MetaData._NullCheck();
    }
}

public abstract class FileContainerOptions
{
    public FileContainerFlags Flags { get; }
    public IRandomAccess<byte> PhysicalFile { get; }
    public PathParser PathParser { get; }

    public FileContainerOptions(IRandomAccess<byte> physicalFile, FileContainerFlags flags, PathParser pathParser)
    {
        if (flags == FileContainerFlags.None)
            throw new ArgumentOutOfRangeException(nameof(flags));

        this.PhysicalFile = physicalFile;
        this.Flags = flags;
        this.PathParser = pathParser;
    }
}

// 任意のコンテナフォーマットを読み書きするための抽象クラス
// 注: 各オペレーションメソッドはスレッドセーフではない。シリアルな利用が前提である。
//     スレッドセーフにする必要がある場合 (例: 上位のファイルシステムと接続する) は、自前で同期を取る必要があるので注意すること。
public abstract class FileContainer : AsyncService
{
    public FileContainerOptions Options { get; }

    protected IRandomAccess<byte> PhysicalFile => Options.PhysicalFile;

    public PathParser PathParser => Options.PathParser;

    readonly SingleEntryDoor Door = new SingleEntryDoor();

    public bool CanWrite = false;

    protected FileContainer(FileContainerOptions options, CancellationToken cancel = default) : base(cancel)
    {
        try
        {
            this.Options = options;

            // モードフラグをもとに可能状態を設定
            if (this.Options.Flags.Bit(FileContainerFlags.CreateNewSequential))
            {
                this.CanWrite = true;
            }
        }
        catch
        {
            this._DisposeSafe();
            throw;
        }
    }

    // FileContainer の派生クラスが具備すべきメソッド一覧
    protected abstract Task<SequentialWritableImpl<byte>> AddFileAsyncImpl(FileContainerEntityParam param, long? fileSizeHint, CancellationToken cancel = default);
    protected abstract Task FinishAsyncImpl(CancellationToken cancel = default);

    public async Task AddFileAsync(FileContainerEntityParam param, Func<ISequentialWritable<byte>, CancellationToken, Task<bool>> composeProc, long? fileSizeHint = null, CancellationToken cancel = default)
    {
        param.Validate();

        using var doorHolder = Door.Enter();
        await using var cancelHolder = this.CreatePerTaskCancellationToken(out CancellationToken c, cancel);

        if (this.CanWrite == false) throw new CoresException("Current state doesn't allow write operations.");

        // 実装の新規ファイル作成を呼び出す
        SequentialWritableImpl<byte> obj = await AddFileAsyncImpl(param, fileSizeHint, c);

        try
        {
            // ユーザー提供の composeProc を呼び出す
            // これによりユーザーは ISequentialWritable に対して書き込み操作を実施する
            bool ok = await composeProc(obj, c);

            // 実装のファイル追加処理を完了させる
            await obj.CompleteAsync(ok, c);
        }
        catch
        {
            // 途中で何らかのエラーが発生した
            // 実装のファイル追加処理を失敗させる
            await obj.CompleteAsync(false, c);
            throw;
        }
    }
    public void AddFile(FileContainerEntityParam param, Func<ISequentialWritable<byte>, CancellationToken, bool> composeProc, long? fileSizeHint = null, CancellationToken cancel = default)
        => AddFileAsync(param, (x, y) => Task.FromResult(composeProc(x, y)), fileSizeHint, cancel)._GetResult();

    public async Task AddFileSimpleDataAsync(FileContainerEntityParam param, ReadOnlyMemory<byte> data, CancellationToken cancel = default)
    {
        await AddFileAsync(param, async (w, c) =>
        {
            await w.AppendAsync(data, cancel);
            return true;
        },
        fileSizeHint: data.Length,
        cancel: cancel);
    }
    public void AddFileSimpleData(FileContainerEntityParam param, ReadOnlyMemory<byte> data, CancellationToken cancel = default)
        => AddFileSimpleDataAsync(param, data, cancel)._GetResult();

    public async Task FinishAsync(CancellationToken cancel = default)
    {
        using var doorHolder = Door.Enter();
        await using var cancelHolder = this.CreatePerTaskCancellationToken(out CancellationToken c, cancel);

        if (this.CanWrite == false) throw new CoresException("Current state doesn't allow write operations.");

        this.CanWrite = false;

        // 実装の Finish を呼び出す
        await FinishAsyncImpl(c);

        // 最後に書き込みバッファを Flush する
        await this.PhysicalFile.FlushAsync(cancel);
    }
    public void Finish(CancellationToken cancel = default)
        => FinishAsync(cancel)._GetResult();


    ///////////// 以下はユーティリティ関数

    // 任意のストリームからの仮想ファイルをインポートする
    public async Task<long> ImportVirtualFileAsync(Stream srcStream, FileContainerEntityParam destParam, CancellationToken cancel = default)
    {
        long totalSize = 0;

        destParam._NullCheck();

        destParam = destParam._CloneDeep();

        int bufferSize = CoresConfig.BufferSizes.FileCopyBufferSize;

        // 先ファイルを作成
        await this.AddFileAsync(destParam, async (w, c) =>
        {
                // 元ファイルから先ファイルにデータをコピー
                byte[] buffer = ArrayPool<byte>.Shared.Rent(bufferSize);

            try
            {
                while (true)
                {
                    int readSize = await srcStream.ReadAsync(buffer, cancel);
                    if (readSize == 0) break;

                    totalSize += readSize;

                    await w.AppendAsync(buffer.AsMemory(0, readSize), cancel);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer, false);
            }

            return true;
        },
        destParam.MetaData.Size,
        cancel);

        return totalSize;
    }
    public long ImportVirtualFile(Stream srcStream, FileContainerEntityParam destParam, CancellationToken cancel = default)
        => ImportVirtualFileAsync(srcStream, destParam, cancel)._GetResult();

    // 任意のファイルシステムの物理ファイルをインポートする
    public async Task<long> ImportFileAsync(FilePath srcFilePath, FileContainerEntityParam destParam, CancellationToken cancel = default)
    {
        // 元ファイルのメタデータ読み込み
        var metaData = await srcFilePath.GetFileMetadataAsync(FileMetadataGetFlags.NoAlternateStream | FileMetadataGetFlags.NoAuthor | FileMetadataGetFlags.NoPhysicalFileSize | FileMetadataGetFlags.NoPreciseFileSize | FileMetadataGetFlags.NoSecurity);

        destParam.MetaData = metaData;

        // 元ファイルを開く
        await using (var srcFile = await srcFilePath.OpenAsync(false, cancel: cancel))
        {
            // ストリームの内容をインポートする
            return await ImportVirtualFileAsync(srcFile.GetStream(false), destParam, cancel);
        }
    }
    public long ImportFile(FilePath srcFilePath, FileContainerEntityParam destParam, CancellationToken cancel = default)
        => ImportFileAsync(srcFilePath, destParam, cancel)._GetResult();

    // 任意のファイルシステムのディレクトリ内のファイルを再帰的にインポートする
    public async Task<long> ImportDirectoryAsync(DirectoryPath srcRootDir,
        FileContainerEntityParam? paramTemplate = null,
        Func<FileSystemEntity, bool>? fileFilter = null,
        Func<DirectoryPathInfo, Exception, CancellationToken, Task<bool>>? exceptionHandler = null,
        string? directoryPrefix = null,
        CancellationToken cancel = default)
    {
        RefLong totalSize = 0;

        if (paramTemplate == null)
            paramTemplate = new FileContainerEntityParam("");

        directoryPrefix = directoryPrefix._NonNullTrim();

        if (directoryPrefix._IsFilled())
        {
            if (directoryPrefix[0] == '\\' || directoryPrefix[0] == '/')
                directoryPrefix = directoryPrefix.Substring(1);

            directoryPrefix = PathParser.Windows.RemoveLastSeparatorChar(directoryPrefix);
        }

        bool ret = await srcRootDir.FileSystem.DirectoryWalker.WalkDirectoryAsync(srcRootDir,
            async (dirInfo, entries, c) =>
            {
                foreach (var e in entries.Where(x => x.IsFile))
                {
                        // フィルタ検査
                        if (fileFilter != null && fileFilter(e) == false)
                        continue;

                        // ファイル名の決定
                        string relativeFileName = dirInfo.FileSystem.PathParser.GetRelativeFileName(e.FullPath, srcRootDir);
                    FileContainerEntityParam fileParam = paramTemplate._CloneDeep();

                    if (directoryPrefix._IsFilled())
                    {
                        relativeFileName = directoryPrefix + "/" + relativeFileName;
                    }

                    fileParam.PathString = relativeFileName;

                    long size = await this.ImportFileAsync(new FilePath(e.FullPath, dirInfo.FileSystem), fileParam, c);

                    totalSize.Add(size);
                }

                return true;
            },
            null,
            exceptionHandler,
            true,
            cancel);

        if (ret == false) throw new OperationCanceledException();

        return totalSize;
    }
    public long ImportDirectory(DirectoryPath srcRootDir,
        FileContainerEntityParam? paramTemplate = null,
        Func<FileSystemEntity, bool>? fileFilter = null,
        Func<DirectoryPathInfo, Exception, CancellationToken, Task<bool>>? exceptionHandler = null,
        string? directoryPrefix = null,
        CancellationToken cancel = default)
        => ImportDirectoryAsync(srcRootDir, paramTemplate, fileFilter, exceptionHandler, directoryPrefix, cancel)._GetResult();

}

#endif


