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

#if CORES_BASIC_JSON

using System.Diagnostics.CodeAnalysis;

using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using System.IO;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace IPA.Cores.Helper.Basic
{
    public static class JsonHelper
    {
        public static string _ObjectToJson(this object? obj, bool includeNull = false, bool escapeHtml = false, int? maxDepth = Json.DefaultMaxDepth, bool compact = false, bool referenceHandling = false, bool base64url = false, Type? type = null, JsonFlags jsonFlags = JsonFlags.None)
            => Json.Serialize(obj, includeNull, escapeHtml, maxDepth, compact, referenceHandling, base64url, type, jsonFlags);

        public static string _ObjectToJson<T>([AllowNull] this T obj, EnsurePresentInterface yes, bool includeNull = false, bool escapeHtml = false, int? maxDepth = Json.DefaultMaxDepth, bool compact = false, bool referenceHandling = false, bool base64url = false, JsonFlags jsonFlags = JsonFlags.None)
            => _ObjectToJson(obj, includeNull, escapeHtml, maxDepth, compact, referenceHandling, base64url, typeof(T), jsonFlags);

        public static byte[] _CalcObjectDigestAsJson(this object? obj, bool includeNull = false, bool escapeHtml = false, int? maxDepth = Json.DefaultMaxDepth, bool compact = true, bool referenceHandling = false, Type? type = null, JsonFlags jsonFlags = JsonFlags.None)
            => Json.GetDigest(obj, includeNull, escapeHtml, maxDepth, compact, referenceHandling, type, jsonFlags);

        public static byte[] _CalcObjectDigestAsJson<T>([AllowNull] this T obj, bool includeNull = false, bool escapeHtml = false, int? maxDepth = Json.DefaultMaxDepth, bool compact = true, bool referenceHandling = false, JsonFlags jsonFlags = JsonFlags.None)
            => _CalcObjectDigestAsJson(obj, includeNull, escapeHtml, maxDepth, compact, referenceHandling, typeof(T), jsonFlags);

        public static void _ObjectToJsonTextWriter(this object? obj, TextWriter destTextWriter, bool includeNull = false, bool escapeHtml = false, int? maxDepth = Json.DefaultMaxDepth, bool compact = false, bool referenceHandling = false, Type? type = null, JsonFlags jsonFlags = JsonFlags.None)
            => Json.Serialize(destTextWriter, obj, includeNull, escapeHtml, maxDepth, compact, referenceHandling, type, jsonFlags);

        public static void _ObjectToJsonTextWriter<T>([AllowNull] this T obj, TextWriter destTextWriter, bool includeNull = false, bool escapeHtml = false, int? maxDepth = Json.DefaultMaxDepth, bool compact = false, bool referenceHandling = false, JsonFlags jsonFlags = JsonFlags.None)
            => _ObjectToJsonTextWriter(obj, destTextWriter, includeNull, escapeHtml, maxDepth, compact, referenceHandling, typeof(T), jsonFlags);

        [return: MaybeNull]
        public static T _JsonToObject<T>(this string str, bool includeNull = false, int? maxDepth = Json.DefaultMaxDepth, bool base64url = false, JsonFlags jsonFlags = JsonFlags.None, IList<JsonConverter>? converters = null)
            => Json.Deserialize<T>(str, includeNull, maxDepth, base64url, jsonFlags, converters);

        public static object? _JsonToObject(this string str, Type type, bool includeNull = false, int? maxDepth = Json.DefaultMaxDepth, bool base64url = false, JsonFlags jsonFlags = JsonFlags.None, IList<JsonConverter>? converters = null)
            => Json.Deserialize(str, type, includeNull, maxDepth, base64url, jsonFlags, converters);

        [return: MaybeNull]
        public static T _JsonToObject<T>(this TextReader srcTextReader, bool includeNull = false, int? maxDepth = Json.DefaultMaxDepth, JsonFlags jsonFlags = JsonFlags.None, IList<JsonConverter>? converters = null)
            => Json.Deserialize<T>(srcTextReader, includeNull, maxDepth, jsonFlags, converters);

        public static object? _JsonToObject(this TextReader srcTextReader, Type type, bool includeNull = false, int? maxDepth = Json.DefaultMaxDepth, JsonFlags jsonFlags = JsonFlags.None, IList<JsonConverter>? converters = null)
            => Json.Deserialize(srcTextReader, type, includeNull, maxDepth, jsonFlags, converters);

        [return: MaybeNull]
        public static T _ConvertJsonObject<T>(this object obj, bool includeNull = false, int? maxDepth = Json.DefaultMaxDepth, bool referenceHandling = false, JsonFlags jsonFlags = JsonFlags.None)
            => Json.ConvertObject<T>(obj, includeNull, maxDepth, referenceHandling, jsonFlags);

        public static object? _ConvertJsonObject(this object obj, Type type, bool includeNull = false, int? maxDepth = Json.DefaultMaxDepth, bool referenceHandling = false, JsonFlags jsonFlags = JsonFlags.None)
            => Json.ConvertObject(obj, type, includeNull, maxDepth, referenceHandling, jsonFlags);

        public static dynamic? _JsonToDynamic(this string str)
            => Json.DeserializeDynamic(str);

        public static JObject? _ToJObject(this object? obj, bool includeNull = false, bool escapeHtml = false, int? maxDepth = Json.DefaultMaxDepth, bool compact = false, bool referenceHandling = false, bool base64url = false, Type? type = null, JsonFlags jsonFlags = JsonFlags.None)
        {
            string str = obj._ObjectToJson(includeNull, escapeHtml, maxDepth, compact, referenceHandling, base64url, type, jsonFlags);
            return _JsonToJsonObject(str, includeNull, maxDepth, jsonFlags);
        }

        public static JObject? _JsonToJsonObject(this string str, bool includeNull = false, int? maxDepth = Json.DefaultMaxDepth, JsonFlags jsonFlags = JsonFlags.None)
            => str._JsonToObject<JObject>(includeNull, maxDepth, jsonFlags: jsonFlags);

        public static ulong _CalcObjectHashByJson(this object o)
            => Util.CalcObjectHashByJson(o);

        public static string _JsonNormalize(this string s)
            => Json.Normalize(s);

        public static string _JsonNormalizeArray(this string s, int? maxDepth = 8)
            => Json.NormalizeArray(s, maxDepth);

        public static long _ObjectToFile<T>([AllowNull] this T obj, string path, FileSystem? fs = null, FileFlags flags = FileFlags.None, bool doNotOverwrite = false, CancellationToken cancel = default,
            bool includeNull = false, bool escapeHtml = false, int? maxDepth = Json.DefaultMaxDepth, bool compact = false, bool referenceHandling = false, JsonFlags jsonFlags = JsonFlags.None)
            => (fs ?? Lfs).WriteJsonToFile<T>(path, obj, flags, doNotOverwrite, cancel, includeNull, escapeHtml, maxDepth, compact, referenceHandling, false, jsonFlags);

        [return: MaybeNull]
        public static T _FileToObject<T>(this string path, FileSystem? fs = null, FileFlags flags = FileFlags.None, CancellationToken cancel = default,
            bool includeNull = false, int? maxDepth = Json.DefaultMaxDepth, bool nullIfError = false, JsonFlags jsonFlags = JsonFlags.None)
            => (fs ?? Lfs).ReadJsonFromFile<T>(path, flags, cancel, includeNull, maxDepth, nullIfError, false, jsonFlags);

        [return: NotNullIfNotNull("obj")]
        [return: MaybeNull]
        public static T _CloneWithJson<T>([AllowNull] this T obj, bool escapeHtml = false, int? maxDepth = Json.DefaultMaxDepth, bool referenceHandling = false, Type? type = null, JsonFlags jsonFlags = JsonFlags.None)
            => Json.CloneWithJson(obj, escapeHtml, maxDepth, referenceHandling, type, jsonFlags);

        [return: NotNullIfNotNull("obj")]
        public static object? _CloneObjectWithJson(this object? obj, bool escapeHtml = false, int? maxDepth = Json.DefaultMaxDepth, bool referenceHandling = false, Type? type = null, JsonFlags jsonFlags = JsonFlags.None)
            => Json.CloneObjectWithJson(obj, escapeHtml, maxDepth, referenceHandling, type, jsonFlags);

        public static JObject _NormalizeEasyJsonStrAttributes(this JObject? src, IComparer<string>? comparer = null)
            => EasyJsonStrAttributes.NormalizeJsonObject(src, comparer);
    }

    public static class JsonConsoleHelper
    {
        [return: NotNullIfNotNull("o")]
        public static object? _PrintAsJson(this object? o, bool includeNull = false, bool escapeHtml = false, int? maxDepth = Json.DefaultMaxDepth, bool compact = false, bool referenceHandling = false, Type? type = null, JsonFlags jsonFlags = JsonFlags.None)
        {
            Con.WriteJsonLine(o, includeNull, escapeHtml, maxDepth, compact, referenceHandling, type, jsonFlags);
            return o;
        }

        [return: NotNullIfNotNull("o")]
        public static T _PrintAsJson<T>([AllowNull] this T o, EnsurePresentInterface yes, bool includeNull = false, bool escapeHtml = false, int? maxDepth = Json.DefaultMaxDepth, bool compact = false, bool referenceHandling = false, JsonFlags jsonFlags = JsonFlags.None)
            => (T)_PrintAsJson(o, includeNull, escapeHtml, maxDepth, compact, referenceHandling, typeof(T), jsonFlags)!;

        [return: NotNullIfNotNull("o")]
        public static object? _DebugAsJson(this object? o, bool includeNull = false, bool escapeHtml = false, int? maxDepth = Json.DefaultMaxDepth, bool compact = false, bool referenceHandling = false, Type? type = null, JsonFlags jsonFlags = JsonFlags.None)
        {
            Con.WriteJsonDebug(o, includeNull, escapeHtml, maxDepth, compact, referenceHandling, type, jsonFlags);
            return o;
        }

        [return: NotNullIfNotNull("o")]
        public static T _DebugAsJson<T>([AllowNull] this T o, EnsurePresentInterface yes, bool includeNull = false, bool escapeHtml = false, int? maxDepth = Json.DefaultMaxDepth, bool compact = false, bool referenceHandling = false, JsonFlags jsonFlags = JsonFlags.None)
            => (T)_DebugAsJson(o, includeNull, escapeHtml, maxDepth, compact, referenceHandling, typeof(T), jsonFlags)!;

        public static void _JsonNormalizeAndPrint(this string s)
        {
            Json.Normalize(s)._Print();
        }
        public static void _JsonNormalizeAndDebug(this string s)
        {
            Json.Normalize(s)._Debug();
        }
    }
}


namespace IPA.Cores.Basic
{
    public static partial class Con
    {
        public static void WriteJsonLine(object? obj, bool includeNull = false, bool escapeHtml = false, int? maxDepth = Json.DefaultMaxDepth, bool compact = false, bool referenceHandling = false, Type? type = null, JsonFlags jsonFlags = JsonFlags.None)
            => Con.WriteLine(obj._ObjectToJson(includeNull, escapeHtml, maxDepth, compact, referenceHandling, type: type, jsonFlags: jsonFlags));

        public static void WriteJsonError(object? obj, bool includeNull = false, bool escapeHtml = false, int? maxDepth = Json.DefaultMaxDepth, bool compact = false, bool referenceHandling = false, Type? type = null, JsonFlags jsonFlags = JsonFlags.None)
            => Con.WriteError(obj._ObjectToJson(includeNull, escapeHtml, maxDepth, compact, referenceHandling, type: type, jsonFlags: jsonFlags));

        public static void WriteJsonDebug(object? obj, bool includeNull = false, bool escapeHtml = false, int? maxDepth = Json.DefaultMaxDepth, bool compact = false, bool referenceHandling = false, Type? type = null, JsonFlags jsonFlags = JsonFlags.None)
            => Con.WriteDebug(obj._ObjectToJson(includeNull, escapeHtml, maxDepth, compact, referenceHandling, type: type, jsonFlags: jsonFlags));
    }

    public class OkFileData<T>
    {
        public int Version;
        public string FileName = "";
        public string Digest = "";
        public DateTimeOffset TimeStamp;
        public T? MetaData;
    }

    public class OkFileEmptyMetaData { }

    public abstract partial class FileSystem
    {
        public async Task<bool> IsOkDirExistsAsync(string targetFilePath, CancellationToken cancel = default)
        {
            try
            {
                string okPath = GenerateOkFilePath(targetFilePath);
                string okDirPath = PathParser.GetDirectoryName(okPath);

                return await IsDirectoryExistsAsync(okDirPath, cancel: cancel);
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> IsOkFileExistsAsync(string targetFilePath, string digest = "", int minVersion = 0, bool ifOkDirNotExistsRetOk = false, bool skipTargetFilePathCheck = false, CancellationToken cancel = default)
        {
            if (ifOkDirNotExistsRetOk)
            {
                if (await IsOkDirExistsAsync(targetFilePath, cancel: cancel) == false)
                {
                    return true;
                }
            }

            var result = await this.ReadOkFileAsync<OkFileEmptyMetaData>(targetFilePath, digest, minVersion, skipTargetFilePathCheck, cancel);

            return result.IsOk;
        }

        public async Task<ResultOrError<T>> ReadOkFileAsync<T>(string targetFilePath, string digest = "", int minVersion = 0, bool skipTargetFilePathCheck = false, CancellationToken cancel = default)
        {
            try
            {
                if (skipTargetFilePathCheck == false)
                {
                    if (await this.IsFileExistsAsync(targetFilePath, cancel) == false)
                    {
                        return new ResultOrError<T>(EnsureError.Error);
                    }
                }

                string okPath = GenerateOkFilePath(targetFilePath);

                if (await this.IsFileExistsAsync(okPath, cancel) == false)
                {
                    return new ResultOrError<T>(EnsureError.Error);
                }

                var okData = await this.ReadJsonFromFileAsync<OkFileData<T>>(okPath, maxDepth: int.MaxValue, cancel: cancel);

                if (okData.Version < minVersion)
                {
                    return false;
                }

                if (digest._IsFilled())
                {
                    if (okData.Digest._IsDiffi(digest))
                    {
                        return false;
                    }
                }

                return okData.MetaData;
            }
            catch (Exception ex)
            {
                $"ReadOkFileAsync: TargetFilePath: '{targetFilePath}': Error = {ex.ToString()}"._Error();

                return new ResultOrError<T>(EnsureError.Error);
            }
        }

        public async Task WriteOkFileAsync<T>(string targetFilePath, T? metaData, string digest = "", int version = 0, CancellationToken cancel = default)
        {
            var now = DtOffsetNow;

            string okFilePath = GenerateOkFilePath(targetFilePath);

            string okDirPath = PP.GetDirectoryName(okFilePath);

            if (await this.IsDirectoryExistsAsync(okDirPath, cancel) == false)
            {
                await this.CreateDirectoryAsync(okDirPath, cancel: cancel);
            }

            OkFileData<T> data = new OkFileData<T>();
            data.Version = version;
            data.TimeStamp = now;
            data.FileName = PP.GetFileName(targetFilePath);
            data.MetaData = metaData;
            data.Digest = digest;

            await Lfs.TryAddOrRemoveAttributeFromExistingFileAsync(okFilePath, 0, FileAttributes.Hidden, cancel: cancel);
            await this.WriteJsonToFileAsync(okFilePath, data, FileFlags.AutoCreateDirectory, cancel: cancel);
            await Lfs.TryAddOrRemoveAttributeFromExistingDirAsync(okDirPath, FileAttributes.Hidden, cancel: cancel);
            await Lfs.TryAddOrRemoveAttributeFromExistingFileAsync(okFilePath, FileAttributes.Hidden, cancel: cancel);
        }

        public string GenerateOkFilePath(string filePath)
        {
            string dirPath = PP.GetDirectoryName(filePath);

            string okDirPath = PP.Combine(dirPath, Consts.FileNames.OkFileDirName);

            string fileName = PP.GetFileName(filePath);

            return PP.Combine(okDirPath, fileName + Consts.FileNames.OkFileExt);
        }

        public async Task<long> WriteJsonToFileEncryptedAsync<T>(string path, [AllowNull] T obj, string password, FileFlags flags = FileFlags.None, bool doNotOverwrite = false, CancellationToken cancel = default,
            bool includeNull = false, bool escapeHtml = false, int? maxDepth = Json.DefaultMaxDepth, bool compact = false, bool referenceHandling = false, JsonFlags jsonFlags = JsonFlags.None)
        {
            string jsonStr = obj._ObjectToJson(includeNull, escapeHtml, maxDepth, compact, referenceHandling, jsonFlags: jsonFlags);

            return await this.WriteStringToFileEncryptedAsync(path, jsonStr, password, flags, doNotOverwrite, writeBom: true, cancel: cancel);
        }
        public long WriteJsonToFileEncrypted<T>(string path, [AllowNull] T obj, string password, FileFlags flags = FileFlags.None, bool doNotOverwrite = false, CancellationToken cancel = default,
            bool includeNull = false, bool escapeHtml = false, int? maxDepth = Json.DefaultMaxDepth, bool compact = false, bool referenceHandling = false, JsonFlags jsonFlags = JsonFlags.None)
            => WriteJsonToFileEncryptedAsync(path, obj, password, flags, doNotOverwrite, cancel, includeNull, escapeHtml, maxDepth, compact, referenceHandling, jsonFlags)._GetResult();


        public async Task<T> ReadJsonFromFileEncryptedAsync<T>(string path, string password, int maxSize = int.MaxValue, FileFlags flags = FileFlags.None, CancellationToken cancel = default,
            bool includeNull = false, int? maxDepth = Json.DefaultMaxDepth, bool nullIfError = false, JsonFlags jsonFlags = JsonFlags.None)
        {
            try
            {
                if (nullIfError)
                {
                    if ((await this.IsFileExistsAsync(path, cancel)) == false)
                    {
                        return default!;
                    }
                }

                string jsonStr = await this.ReadStringFromFileEncryptedAsync(path, password, null, maxSize, flags, false, cancel);

                return jsonStr._JsonToObject<T>(includeNull, maxDepth, false, jsonFlags)!;
            }
            catch
            {
                if (nullIfError)
                    return default!;

                throw;
            }
        }
        public T ReadJsonFromFileEncrypted<T>(string path, string password, int maxSize = int.MaxValue, FileFlags flags = FileFlags.None, CancellationToken cancel = default,
            bool includeNull = false, int? maxDepth = Json.DefaultMaxDepth, bool nullIfError = false, JsonFlags jsonFlags = JsonFlags.None)
            => ReadJsonFromFileEncryptedAsync<T>(path, password, maxSize, flags, cancel, includeNull, maxDepth, nullIfError, jsonFlags)._GetResult();

        public async Task<bool> IsJsonFileAsync(string path, FileFlags flags = FileFlags.None, CancellationToken cancel = default)
        {
            try
            {
                if (await this.IsFileExistsAsync(path, cancel) == false)
                {
                    return false;
                }

                await using var file = await this.OpenAsync(path, flags: flags, cancel: cancel);

                await using var stream = file.GetStream();

                await using var bufReader = new BufferedStream(stream);

                using var reader = new StreamReader(bufReader, true);

                return await Json.IsJsonTextAsync(reader);
            }
            catch
            {
                return false;
            }
        }

        public async Task<long> WriteJsonToFileAsync<T>(string path, [AllowNull] T obj, FileFlags flags = FileFlags.None, bool doNotOverwrite = false, CancellationToken cancel = default,
            bool includeNull = false, bool escapeHtml = false, int? maxDepth = Json.DefaultMaxDepth, bool compact = false, bool referenceHandling = false, bool withBackup = false, JsonFlags jsonFlags = JsonFlags.None)
        {
            if (withBackup)
            {
                string backupFilePath = path + Consts.Extensions.Backup;

                // 元ファイルが存在する場合、その元ファイルが JSON 形式として破損していないかどうかチェックする
                if (await this.IsJsonFileAsync(path, flags, cancel))
                {
                    // 元ファイルが JSON 形式として正しい場合だけ、バックアップファイルに元ファイルの内容をコピーする。
                    // この場合、このコピー作業に失敗したら、この WriteJsonToFileAsync メソッドの処理は直ちに終了する。
                    // (コピー作業に失敗したということは、ファイルシステムの空き容量不足が主原因として考えられる。
                    //  この場合、このコピー作業の失敗エラーを無視して、メインファイルへの書き込みも実施してしまうと、
                    //  メインファイルも空き容量不足で破損した状態となり、データ喪失が発生するおそれがある。
                    //  これを防止するため、このようなケースにおいては、ここで例外を発生させることにより (キャッチしない)、バックアップファイルへの書き込みは実施してはならない。)
                    await this.CopyFileAsync(path, backupFilePath, new CopyFileParams(flags: flags | FileFlags.AutoCreateDirectory | FileFlags.WriteOnlyIfChanged, retryCount: 0), cancel: cancel);
                }
            }

            if (flags.Bit(FileFlags.WriteOnlyIfChanged))
            {
                // WriteOnlyIfChanged が設定されている場合: 一度メモリに全部出力してからバイト列として比較して書き出しする (メモリを食うが、仕方無い)
                HugeMemoryBuffer<byte> mem = new HugeMemoryBuffer<byte>();

                await using (BufferBasedStream stream = new BufferBasedStream(mem))
                {
                    await using (StreamWriter w = new StreamWriter(stream, new UTF8Encoding(true), Consts.Numbers.DefaultVeryLargeBufferSize))
                    {
                        obj._ObjectToJsonTextWriter(w, includeNull, escapeHtml, maxDepth, compact, referenceHandling, jsonFlags);
                    }
                }

                return await this.WriteHugeMemoryBufferToFileAsync(path, mem, flags, doNotOverwrite, cancel);
            }
            else
            {
                // WriteOnlyIfChanged が設定されていない場合: Stream ベースで書き出しをする
                await using var file = await this.CreateAsync(path, flags: flags, doNotOverwrite: doNotOverwrite, cancel: cancel);

                await using var stream = file.GetStream();

                await using var bufStream = new BufferedStream(stream);

                await using var writer = new StreamWriter(bufStream);

                obj._ObjectToJsonTextWriter(writer, includeNull, escapeHtml, maxDepth, compact, referenceHandling, jsonFlags);

                await writer.FlushAsync();

                await bufStream.FlushAsync(cancel);

                await stream.FlushAsync(cancel);

                return stream.Position;
            }
        }
        public long WriteJsonToFile<T>(string path, [AllowNull] T obj, FileFlags flags = FileFlags.None, bool doNotOverwrite = false, CancellationToken cancel = default,
            bool includeNull = false, bool escapeHtml = false, int? maxDepth = Json.DefaultMaxDepth, bool compact = false, bool referenceHandling = false, bool withBackup = false, JsonFlags jsonFlags = JsonFlags.None)
            => WriteJsonToFileAsync(path, obj, flags, doNotOverwrite, cancel, includeNull, escapeHtml, maxDepth, compact, referenceHandling, withBackup, jsonFlags)._GetResult();

        public async Task<T> ReadJsonFromFileAsync<T>(string path, FileFlags flags = FileFlags.None, CancellationToken cancel = default,
            bool includeNull = false, int? maxDepth = Json.DefaultMaxDepth, bool nullIfError = false, bool withBackup = false, JsonFlags jsonFlags = JsonFlags.None, IList<JsonConverter>? converters = null)
        {
            try
            {
                if (nullIfError)
                {
                    if ((await this.IsFileExistsAsync(path, cancel)) == false)
                    {
                        if (withBackup)
                        {
                            if ((await this.IsFileExistsAsync(path + Consts.Extensions.Backup, cancel)) == false)
                            {
                                return default!;
                            }
                        }
                        else
                        {
                            return default!;
                        }
                    }
                }

                L_RETRY:
                try
                {
                    await using var file = await this.OpenAsync(path, flags: flags, cancel: cancel);
                    await using var stream = file.GetStream();
                    await using var bufStream = new BufferedStream(stream);
                    using var reader = new StreamReader(bufStream, true);

                    return reader._JsonToObject<T>(includeNull, maxDepth, jsonFlags, converters)!;
                }
                catch (Exception ex)
                {
                    if (withBackup == false)
                    {
                        throw;
                    }
                    else
                    {
                        ex._Error();
                        withBackup = false;
                        path += Consts.Extensions.Backup;
                        $"Trying the backup file '{path}' ..."._Error();
                        goto L_RETRY;
                    }
                }
            }
            catch
            {
                if (nullIfError)
                    return default!;

                throw;
            }
        }
        public T ReadJsonFromFile<T>(string path, FileFlags flags = FileFlags.None, CancellationToken cancel = default,
            bool includeNull = false, int? maxDepth = Json.DefaultMaxDepth, bool nullIfError = false, bool withBackup = false, JsonFlags jsonFlags = JsonFlags.None, IList<JsonConverter>? converters = null)
            => ReadJsonFromFileAsync<T>(path, flags, cancel, includeNull, maxDepth, nullIfError, withBackup, jsonFlags, converters)._GetResult();

    }
}

#endif // CORES_BASIC_JSON


