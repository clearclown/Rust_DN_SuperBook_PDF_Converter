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

#if true
#pragma warning disable CS0162 // 到達できないコードが検出されました

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
using System.Net;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Serialization;
using System.Net.Mail;
using System.Security.Cryptography;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using Castle.Core.Logging;
using Microsoft.Extensions.Options;
using System.Xml;

using IPA.Cores.Basic.Legacy;

using HtmlAgilityPack;
using System.Text.Json.Serialization;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using System.Globalization;

namespace IPA.Cores.Basic;

public static partial class CoresConfig
{
    public static partial class DefaultExpandIncludesSettings
    {
        public static readonly Copenhagen<int> WebTimeoutMsecs = 5 * 1000;
        public static readonly Copenhagen<int> WebTryCount = 2;
        public static readonly Copenhagen<int> WebRetryIntervalMsecs = 100;
    }

    public static partial class DefaultFfMpegExecSettings
    {
        public static readonly Copenhagen<int> FfMpegDefaultMaxStdOutBufferSize = 32 * 1024 * 1024;
        public static readonly Copenhagen<int> FfMpegDefaultAudioKbps = 192;
    }
}

public class PdfDocInfo
{
    public DateTimeOffset CreateDt = DtOffsetZero;
    public DateTimeOffset ModifyDt = DtOffsetZero;
    public bool IsVertical = false;
    public int? PhysicalPageStart = null;
    public int? LogicalPageStart = null;
}

public class ImageMagickOptions
{
    public string MagickExePath = "";
    public string MogrifyPath = "";
    public string ExifToolPath = "";
    public string QPdfPath = "";
    public string PdfCpuPath = "";
    public Encoding Encoding = Str.Utf8Encoding;
    public int MaxStdOutBufferSize = CoresConfig.DefaultFfMpegExecSettings.FfMpegDefaultMaxStdOutBufferSize;

    public ImageMagickOptions(string magickExePath, string mogrifyPath, string exifToolPath, string qpdfPath, string pdfCpuPath)
    {
        this.MagickExePath = magickExePath;
        this.MogrifyPath = mogrifyPath;
        this.ExifToolPath = exifToolPath;
        this.QPdfPath = qpdfPath;
        this.PdfCpuPath = pdfCpuPath;
    }
}

[Flags]
public enum ImageMagickExtractImageFormat
{
    Bmp = 0,
    Png,
}

public class ImageMagickExtractImageOption
{
    public int Width = 2480;
    public int Height = 3508;
    public int Density = 300;
    public ImageMagickExtractImageFormat Format = ImageMagickExtractImageFormat.Bmp;
    public int NumPages = int.MaxValue;
}

public class ImageMagickBuildPdfOption
{
    public int Density = 300;
    public string ExtWildcard = "*.png";
    public string Compress = "jpeg";
    public int Quality = 70;
}

public class ImageMagickUtil
{
    public ImageMagickOptions Options;

    public ImageMagickUtil(ImageMagickOptions options)
    {
        this.Options = options;
    }

    public async Task BuildPdfFromImagesAsync(string srcImgDirPath, string dstPdfPath, ImageMagickBuildPdfOption? option = null, int? physicalPageStart = null, int? logicalPageStart = null, bool verticalWriting = false, CancellationToken cancel = default)
    {
        option ??= new();

        await Lfs.DeleteFileIfExistsAsync(dstPdfPath, raiseException: true, cancel: cancel);

        await Lfs.EnsureCreateDirectoryForFileAsync(dstPdfPath, cancel: cancel);

        string srcStr = PP.Combine(srcImgDirPath._RemoveQuotation(), option.ExtWildcard)._EnsureQuotation();

        var result = await RunMagickAsync(
            $"-density {option.Density} -units PixelsPerInch {srcStr} -gravity center -background white -compress {option.Compress} -quality {option.Quality} {dstPdfPath._EnsureQuotation()}",
            cancel: cancel);

        await ClearPdfTitleMetaDataAsync(dstPdfPath, cancel);

        if (physicalPageStart.HasValue && logicalPageStart.HasValue)
        {
            await SetPdfPageLabelAsync(dstPdfPath, physicalPageStart.Value, logicalPageStart.Value, cancel);
        }

        if (verticalWriting)
        {
            await SetPdfRightToLeftAsync(dstPdfPath, cancel);
        }
    }

    public async Task ExtractImagesFromPdfAsync(string pdfPath, string dstDir, ImageMagickExtractImageOption? option = null, CancellationToken cancel = default)
    {
        option ??= new();

        string ext = ".bmp";

        if (option.Format == ImageMagickExtractImageFormat.Png)
        {
            ext = ".png";
        }

        await Lfs.CreateDirectoryAsync(dstDir, cancel: cancel);

        var existingFiles = (await Lfs.EnumDirectoryAsync(dstDir, false, cancel: cancel)).Where(x => x.IsFile && x.Name._IsExtensionMatch(ext));

        foreach (var file in existingFiles)
        {
            await Lfs.DeleteFileIfExistsAsync(file.FullPath, raiseException: true, cancel: cancel);
        }

        string dstStr = (PP.RemoveLastSeparatorChar(dstDir) + @"\page_%05d" + ext)._EnsureQuotation();

        string bmpOptions = "-depth 8 -type TrueColor BMP3:";

        if (option.Format == ImageMagickExtractImageFormat.Png)
        {
            bmpOptions = "";
        }

        string pageRange = "";

        if (option.NumPages >= 1 && option.NumPages != int.MaxValue)
        {
            pageRange = $"[0-{option.NumPages - 1}]";
        }

        var result = await RunMagickAsync(
            $"-density {option.Density} {(pdfPath + pageRange)._EnsureQuotation()} -resize {option.Width}x{option.Height} {bmpOptions}{dstStr}",
            cancel: cancel);
    }

    public async Task ApplyDocInfoToPdfFileAsync(string pdfPath, PdfDocInfo info, CancellationToken cancel = default)
    {
        await ClearPdfTitleMetaDataAsync(pdfPath, cancel: cancel);

        if (info.LogicalPageStart.HasValue && info.PhysicalPageStart.HasValue)
        {
            await SetPdfPageLabelAsync(pdfPath, info.PhysicalPageStart.Value, info.LogicalPageStart.Value, cancel: cancel);
        }

        await SetPdfPageDirectionAsync(pdfPath, info.IsVertical, cancel: cancel);

        await SetPdfDateTimeAsync(pdfPath, info.CreateDt, info.ModifyDt, cancel: cancel);
    }

    public async Task ClearPdfTitleMetaDataAsync(string pdfPath, CancellationToken cancel = default)
    {
        // 日本語を含むファイル名が正しく扱えないので一時ディレクトリにコピーして処理
        string tmpPdfPath = await Lfs.GenerateUniqueTempFilePathAsync("pdf", ".pdf", cancel: cancel);

        await Lfs.CopyFileAsync(pdfPath, tmpPdfPath, cancel: cancel);
        try
        {
            var result = await RunExifToolAsync(
                $"-overwrite_original -all:all=\"\" {tmpPdfPath._EnsureQuotation()}",
                cancel: cancel);

            await Lfs.CopyFileAsync(tmpPdfPath, pdfPath, cancel: cancel);
        }
        finally
        {
            await Lfs.DeleteFileIfExistsAsync(tmpPdfPath, cancel: cancel);
        }
    }

    public async Task SetPdfDateTimeAsync(string pdfPath, DateTimeOffset createDt, DateTimeOffset modifyDt, CancellationToken cancel = default)
    {
        // 日本語を含むファイル名が正しく扱えないので一時ディレクトリにコピーして処理
        string tmpPdfPath = await Lfs.GenerateUniqueTempFilePathAsync("pdf", ".pdf", cancel: cancel);

        List<string> paramList = new();

        if (createDt._IsZeroDateTimeForFileSystem() == false)
        {
            paramList.Add("-CreateDate=\"" + DateTimeOffsetToExitToolDateTimeStr(createDt) + "\"");
        }

        if (modifyDt._IsZeroDateTimeForFileSystem() == false)
        {
            paramList.Add("-ModifyDate=\"" + DateTimeOffsetToExitToolDateTimeStr(modifyDt) + "\"");
        }

        if (paramList.Count == 0) return;

        await Lfs.CopyFileAsync(pdfPath, tmpPdfPath, cancel: cancel);
        try
        {
            var result = await RunExifToolAsync(
                $" -overwrite_original {paramList._Combine(" ")} {tmpPdfPath._EnsureQuotation()}",
                cancel: cancel);

            await Lfs.CopyFileAsync(tmpPdfPath, pdfPath, cancel: cancel);
        }
        finally
        {
            await Lfs.DeleteFileIfExistsAsync(tmpPdfPath, cancel: cancel);
        }
    }

    static string DateTimeOffsetToExitToolDateTimeStr(DateTimeOffset value)
    {
        // 日付・時刻部分
        var dateTimePart = value.ToString("yyyy:MM:dd HH:mm:ss", CultureInfo.InvariantCulture);

        // オフセット（符号は保持したいので先に取り出しておく）
        var sign = value.Offset >= TimeSpan.Zero ? "+" : "-";
        var offset = value.Offset.Duration();           // 絶対値を取得

        return string.Create(
            24,                        // トータル文字数（"yyyy:MM:dd HH:mm:ss" = 19文字  + ±hhmm = 5文字）
            (dateTimePart, sign, offset),
            static (span, state) =>
            {
                var (dt, s, off) = state;
                dt.AsSpan().CopyTo(span);                // yyyy:MM:dd HH:mm:ss
                span[19] = (char)s[0];                   // + または -
                off.Hours.TryFormat(span.Slice(20, 2), out _, "00");
                off.Minutes.TryFormat(span.Slice(22, 2), out _, "00");
            });
    }

    public async Task SetPdfPageLabelAsync(string pdfPath, int physicalPage, int logicalPage, CancellationToken cancel = default)
    {
        if (physicalPage <= 0)
        {
            return;
        }

        // 念のため一時ディレクトリにコピーして処理
        string tmpSrcPdfPath = await Lfs.GenerateUniqueTempFilePathAsync("qpdf_src", ".pdf", cancel: cancel);
        string tmpDstPdfPath = await Lfs.GenerateUniqueTempFilePathAsync("qpdf_dst", ".pdf", cancel: cancel);

        await Lfs.CopyFileAsync(pdfPath, tmpSrcPdfPath, cancel: cancel);
        try
        {
            string tag1 = "";

            if (physicalPage >= 2)
            {
                tag1 = "1:D/1";
            }

            var result = await RunQPdfAsync(
                $"{tmpSrcPdfPath._EnsureQuotation()} --set-page-labels {tag1} {physicalPage}:D/{logicalPage} -- {tmpDstPdfPath._EnsureQuotation()}",
                cancel: cancel);

            await Lfs.CopyFileAsync(tmpDstPdfPath, pdfPath, cancel: cancel);
        }
        finally
        {
            await Lfs.DeleteFileIfExistsAsync(tmpSrcPdfPath, cancel: cancel);
            await Lfs.DeleteFileIfExistsAsync(tmpDstPdfPath, cancel: cancel);
        }
    }

    public async Task SetPdfPageDirectionAsync(string pdfPath, bool isVertical, CancellationToken cancel = default)
    {
        // 念のため一時ディレクトリにコピーして処理
        string tmpPath = await Lfs.GenerateUniqueTempFilePathAsync("pdfcpu_src", ".pdf", cancel: cancel);

        string str1 = isVertical ? "R2L" : "L2R";

        string str2 = isVertical ? "TwoPageLeft" : "TwoPageRight";

        await Lfs.CopyFileAsync(pdfPath, tmpPath, cancel: cancel);
        try
        {
            var result = await RunPdfCpuAsync(
                $"viewerpref set {tmpPath._RemoveQuotation()} \"{{\\\"Direction\\\":\\\"{str1}\\\"}}\"",
                cancel: cancel);

            result = await RunPdfCpuAsync(
                $"pagelayout set {tmpPath._RemoveQuotation()} {str2}",
                cancel: cancel);

            await Lfs.CopyFileAsync(tmpPath, pdfPath, cancel: cancel);
        }
        finally
        {
            await Lfs.DeleteFileIfExistsAsync(tmpPath, cancel: cancel);
        }
    }

    public async Task SetPdfRightToLeftAsync(string pdfPath, CancellationToken cancel = default)
    {
        // 念のため一時ディレクトリにコピーして処理
        string tmpPath = await Lfs.GenerateUniqueTempFilePathAsync("pdfcpu_src", ".pdf", cancel: cancel);

        await Lfs.CopyFileAsync(pdfPath, tmpPath, cancel: cancel);
        try
        {
            var result = await RunPdfCpuAsync(
                $"viewerpref set {tmpPath._RemoveQuotation()} \"{{\\\"Direction\\\":\\\"R2L\\\"}}\"",
                cancel: cancel);

            result = await RunPdfCpuAsync(
                $"pagelayout set {tmpPath._RemoveQuotation()} TwoPageRight",
                cancel: cancel);

            await Lfs.CopyFileAsync(tmpPath, pdfPath, cancel: cancel);
        }
        finally
        {
            await Lfs.DeleteFileIfExistsAsync(tmpPath, cancel: cancel);
        }
    }

    public async Task<double> GetDeskewRotateDegreeAsync(string filePath, int sampleSize = 1920, int thresholdPercent = 40, double maxDegree = 1.0, CancellationToken cancel = default)
    {
        string resizeStr = "";

        if (sampleSize >= 1)
        {
            resizeStr = $"-resize \"{sampleSize}x{sampleSize}>\"";
        }

        var result = await RunMagickAsync(
            $"{filePath._EnsureQuotation()} {resizeStr} -deskew {thresholdPercent}% -format \"%[deskew:angle]\" info:",
            cancel: cancel);

        double ret = result.OutputStr._GetFirstFilledLineFromLines()._ToDouble();

        if (Math.Abs(ret) > maxDegree)
        {
            return 0;
        }

        return ret;
    }

    public async Task<EasyExecResult> RunMagickAsync(string arguments, CancellationToken cancel = default)
    {
        Con.WriteLine($"[*Run*] {Options.MagickExePath} {arguments}");

        EasyExecResult ret = await EasyExec.ExecAsync(Options.MagickExePath, arguments, PP.GetDirectoryName(Options.MagickExePath),
            flags: ExecFlags.Default | ExecFlags.EasyPrintRealtimeStdOut | ExecFlags.EasyPrintRealtimeStdErr,
            timeout: Timeout.Infinite, cancel: cancel, throwOnErrorExitCode: true,
            easyOutputMaxSize: Options.MaxStdOutBufferSize,
            inputEncoding: Options.Encoding, outputEncoding: Options.Encoding, errorEncoding: Options.Encoding);

        return ret;
    }

    public async Task<EasyExecResult> RunMogrifyAsync(string arguments, CancellationToken cancel = default)
    {
        Con.WriteLine($"[*Run*] {Options.MogrifyPath} {arguments}");

        EasyExecResult ret = await EasyExec.ExecAsync(Options.MogrifyPath, arguments, PP.GetDirectoryName(Options.MogrifyPath),
            flags: ExecFlags.Default | ExecFlags.EasyPrintRealtimeStdOut | ExecFlags.EasyPrintRealtimeStdErr,
            timeout: Timeout.Infinite, cancel: cancel, throwOnErrorExitCode: true,
            easyOutputMaxSize: Options.MaxStdOutBufferSize,
            inputEncoding: Options.Encoding, outputEncoding: Options.Encoding, errorEncoding: Options.Encoding);

        return ret;
    }

    public async Task<EasyExecResult> RunExifToolAsync(string arguments, CancellationToken cancel = default)
    {
        Con.WriteLine($"[*Run*] {Options.ExifToolPath} {arguments}");

        EasyExecResult ret = await EasyExec.ExecAsync(Options.ExifToolPath, arguments, PP.GetDirectoryName(Options.ExifToolPath),
            flags: ExecFlags.Default | ExecFlags.EasyPrintRealtimeStdOut | ExecFlags.EasyPrintRealtimeStdErr,
            timeout: Timeout.Infinite, cancel: cancel, throwOnErrorExitCode: true,
            easyOutputMaxSize: Options.MaxStdOutBufferSize,
            inputEncoding: Options.Encoding, outputEncoding: Options.Encoding, errorEncoding: Options.Encoding);

        return ret;
    }

    public async Task<EasyExecResult> RunQPdfAsync(string arguments, CancellationToken cancel = default)
    {
        Con.WriteLine($"[*Run*] {Options.QPdfPath} {arguments}");

        EasyExecResult ret = await EasyExec.ExecAsync(Options.QPdfPath, arguments, PP.GetDirectoryName(Options.QPdfPath),
            flags: ExecFlags.Default | ExecFlags.EasyPrintRealtimeStdOut | ExecFlags.EasyPrintRealtimeStdErr,
            timeout: Timeout.Infinite, cancel: cancel, throwOnErrorExitCode: true,
            easyOutputMaxSize: Options.MaxStdOutBufferSize,
            inputEncoding: Options.Encoding, outputEncoding: Options.Encoding, errorEncoding: Options.Encoding);

        return ret;
    }

    public async Task<EasyExecResult> RunPdfCpuAsync(string arguments, CancellationToken cancel = default)
    {
        Con.WriteLine($"[*Run*] {Options.PdfCpuPath} {arguments}");

        EasyExecResult ret = await EasyExec.ExecAsync(Options.PdfCpuPath, arguments, PP.GetDirectoryName(Options.PdfCpuPath),
            flags: ExecFlags.Default | ExecFlags.EasyPrintRealtimeStdOut | ExecFlags.EasyPrintRealtimeStdErr,
            timeout: Timeout.Infinite, cancel: cancel, throwOnErrorExitCode: true,
            easyOutputMaxSize: Options.MaxStdOutBufferSize,
            inputEncoding: Options.Encoding, outputEncoding: Options.Encoding, errorEncoding: Options.Encoding);

        return ret;
    }


}

public class FfMpegUtilOptions
{
    public string FfMpegExePath = "";
    public string FfProbeExePath = "";
    public Encoding Encoding = Str.Utf8Encoding;
    public int MaxStdOutBufferSize = CoresConfig.DefaultFfMpegExecSettings.FfMpegDefaultMaxStdOutBufferSize;

    public FfMpegUtilOptions(string ffMpegExePath, string ffProbeExePath)
    {
        this.FfMpegExePath = ffMpegExePath;
        this.FfProbeExePath = ffProbeExePath;
    }
}

public class MediaMetaData
{
    public string Album = "";
    public string AlbumArtist = "";
    public string Title = "";
    public string Artist = "";
    public int Track = 0;
    public int TrackTotal = 0;
    public string Lyrics = "";

    public bool HasValue()
    {
        if (this.Album._IsFilled() ||
            this.AlbumArtist._IsFilled() ||
            this.Title._IsFilled() ||
            this.Artist._IsFilled())
        {
            return true;
        }

        return false;
    }
}

public class FfMpegParsed
{
    public KeyValueList<string, string> Items = new KeyValueList<string, string>();

    public int GetDurationMsecs()
    {
        try
        {
            string ffprobeDuration = Items._GetFirstValueOrDefault("Duration", StrCmpi);
            if (ffprobeDuration._IsFilled())
            {
                // カンマ以降の文字列を除去し、duration 部分のみを取り出す
                int commaIndex = ffprobeDuration.IndexOf(',');
                string durationPart = commaIndex >= 0 ? ffprobeDuration.Substring(0, commaIndex).Trim() : ffprobeDuration.Trim();

                // TimeSpan でパースする。ffprobe の出力は "HH:mm:ss.ff" 形式となるため、
                // TimeSpan.Parse は柔軟に解釈してくれます。
                if (TimeSpan.TryParse(durationPart, out TimeSpan duration))
                {
                    // ミリ秒の合計値を計算
                    double totalMilliseconds = duration.TotalMilliseconds;

                    // int の範囲内か確認
                    if (totalMilliseconds > int.MaxValue)
                        throw new OverflowException("duration のミリ秒値が int の範囲を超えています。");

                    return (int)totalMilliseconds;
                }
                else
                {
                    throw new FormatException("duration の形式が正しくありません。");
                }
            }

            return -1;
        }
        catch (Exception ex)
        {
            ex._Error();
            return -1;
        }
    }
}

public class MediaVoiceSegment
{
    public long DataPosition; // この値は怪しい？
    public long DataLength; // この値は怪しい？
    public double TimePosition;
    public double TimeLength;
    public string? VoiceText;
    public string? TagStr;
    public int SpeakerId;
    public bool IsBlank;
    public bool IsTag;
    public double BlankDuration;
    public bool IsSleep;
    public double SleepDuration;

    public int Level;
    public string? FilterName;
    [Newtonsoft.Json.JsonConverter(typeof(StringEnumConverter))]
    public AiAudioEffectSpeedType? FilterSpeedType;
    public JObject? FilterSettings;
}

public class MediaUsedMaterialsSegment
{
    public string WavPath = "";
    public double StartSecs;
    public double LengthSecs;

    public string? FilterName;
    [Newtonsoft.Json.JsonConverter(typeof(StringEnumConverter))]
    public AiAudioEffectSpeedType? FilterSpeedType;
    public JObject? FilterSettings;
}

public class FfMpegParsedList
{
    public FfMpegParsed? Input = new FfMpegParsed();
    public FfMpegParsed? Output = new FfMpegParsed();

    public List<FfMpegParsed> All = new List<FfMpegParsed>();

    public List<AiWaveConcatenatedSrcWavList>? Options_UsedBgmSrcMusicList = null;
    public List<AiWaveConcatenatedSrcWavList>? Options_UsedOverwriteSrcMusicList = null;

    public List<MediaVoiceSegment>? Options_VoiceSegmentsList = null;

    public List<MediaUsedMaterialsSegment>? Options_UsedMaterialsList = null;

    public FfMpegParsedList()
    {
        this.All.Add(this.Input);
        this.All.Add(this.Output);
    }

    public FfMpegParsedList(string body)
    {
        this.All.Add(this.Input);
        this.All.Add(this.Output);

        var lines = body._GetLines();

        int mode = 0;

        foreach (var line in lines)
        {
            if (line.StartsWith("Input #0"))
            {
                mode = 1;
            }
            else if (line.StartsWith("Input #"))
            {
                mode = 0;
            }
            else if (line.StartsWith("Output #0"))
            {
                mode = 2;
            }
            else if (line.StartsWith("Output #"))
            {
                mode = 0;
            }

            FfMpegParsed? current = null;
            if (mode == 1) current = this.Input;
            if (mode == 2) current = this.Output;

            if (current != null)
            {
                if (line._GetKeyAndValueExact(out var key, out var value, ": "))
                {
                    key = key._Split(StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries, " ").LastOrDefault();

                    key = key._NonNullTrim();
                    value = value._NonNullTrim();

                    if (key._IsFilled() && value._IsFilled())
                    {
                        current.Items.Add(key, value);
                    }
                }
            }
        }

        ParseMain();
    }

    public MediaMetaData Meta = new MediaMetaData();

    public double VolumeDetect_MeanVolume;
    public double VolumeDetect_MaxVolume;

    public void ReParseMain() => ParseMain();

    void ParseMain()
    {
        this.Meta.Album = this.All.Select(x => x.Items._GetStrFirst("album")).Where(x => x._IsFilled()).FirstOrDefault("");
        this.Meta.AlbumArtist = this.All.Select(x => x.Items._GetStrFirst("album_artist")).Where(x => x._IsFilled()).FirstOrDefault("");
        this.Meta.Title = this.All.Select(x => x.Items._GetStrFirst("title")).Where(x => x._IsFilled()).FirstOrDefault("");
        this.Meta.Artist = this.All.Select(x => x.Items._GetStrFirst("artist")).Where(x => x._IsFilled()).FirstOrDefault("");

        string trackStr = this.All.Select(x => x.Items._GetStrFirst("track")).Where(x => x._IsFilled()).FirstOrDefault("");

        if (trackStr._GetKeyAndValueExact(out var currentTrackStr, out var totalTrackStr, "/"))
        {
            this.Meta.Track = currentTrackStr._ToInt();
            this.Meta.TrackTotal = totalTrackStr._ToInt();
        }
        else if (trackStr._GetKeyAndValueExact(out var currentTrackStr2, out var totalTrackStr2, "_")) // bugfix
        {
            this.Meta.Track = currentTrackStr2._ToInt();
            this.Meta.TrackTotal = totalTrackStr2._ToInt();
        }
        else
        {
            this.Meta.Track = trackStr._ToInt();
        }

        string meanVolumeStr = this.All.Select(x => x.Items._GetStrFirst("mean_volume")).Where(x => x._IsFilled() && x.EndsWith(" dB")).FirstOrDefault("");
        string maxVolumeStr = this.All.Select(x => x.Items._GetStrFirst("max_volume")).Where(x => x._IsFilled() && x.EndsWith(" dB")).FirstOrDefault("");

        if (meanVolumeStr._IsFilled())
        {
            this.VolumeDetect_MeanVolume = meanVolumeStr.Substring(0, meanVolumeStr.Length - 3).Trim()._ToDouble();
        }

        if (maxVolumeStr._IsFilled())
        {
            this.VolumeDetect_MaxVolume = maxVolumeStr.Substring(0, meanVolumeStr.Length - 3).Trim()._ToDouble();
        }
    }
}

public enum FfMpegAudioCodec
{
    Wav = 0,
    Flac,
    Mp3,
    Aac,
}

public enum FfmpegAdjustVolumeOptiono
{
    MeanAndMax = 0,
    MeanOnly,
}

public class FfMpegUtil
{
    public FfMpegUtilOptions Options;

    public FfMpegUtil(FfMpegUtilOptions options)
    {
        this.Options = options;
    }

    public static string GetExtensionFromCodec(FfMpegAudioCodec codec)
    {
        switch (codec)
        {
            case FfMpegAudioCodec.Aac:
                return ".m4a";

            case FfMpegAudioCodec.Flac:
                return ".flac";

            case FfMpegAudioCodec.Mp3:
                return ".mp3";

            case FfMpegAudioCodec.Wav:
                return ".wav";
        }

        throw new CoresLibException(nameof(codec));
    }

    public async Task<FfMpegParsedList> AddBgmToVoiceFileAsync(string srcVoiceFilePath, string srcBgmFilePath, string dstWavFilePath, bool smoothMode = true, bool cutPeek = false, CancellationToken cancel = default)
    {
        string cmdLine = $"-y -i {srcVoiceFilePath._EnsureQuotation()} -i {srcBgmFilePath._EnsureQuotation()} -vn ";

        cmdLine += "-reset_timestamps 1 -ar 44100 -ac 2 -c:a pcm_s16le -map_metadata -1 -f wav ";

        string filterStr;

        string normalizeFilterAddStr = "";
        if (cutPeek)
        {
            normalizeFilterAddStr = ":normalize=1";
        }

        if (smoothMode == false)
        {
            filterStr = "-filter_complex \"amix=inputs=2:duration=longest:dropout_transition=0\"" + normalizeFilterAddStr + " ";
        }
        else
        {
            //filterStr = "-filter_complex \"[1:a][0:a]sidechaincompress=threshold=0.1:ratio=10:attack=20:release=200[ducked]; [0:a][ducked]amix=inputs=2:duration=longest[out]\" -map \"[out]\" ";

            filterStr = "-filter_complex \"[1:a][0:a]sidechaincompress=threshold=0.1:ratio=20:attack=200:release=4000[ducked]; [0:a][ducked]amix=inputs=2:duration=longest[out]\" -map \"[out]\" ";
        }

        cmdLine += filterStr;

        cmdLine += $"{dstWavFilePath._EnsureQuotation()}";

        await EnsureCreateDirectoryForFileAsync(dstWavFilePath, cancel);

        await Lfs.DeleteFileIfExistsAsync(dstWavFilePath, cancel: cancel);

        FfMpegParsedList ret = await RunFfMpegAndParseAsync(cmdLine, cancel);

        return ret;
    }

    public async Task<FfMpegParsedList> EncodeAudioAsync(string srcFilePath, string dstFilePath, FfMpegAudioCodec codec, int kbps = 0, int speedPercent = 100, MediaMetaData? metaData = null, string tagTitle = "", bool useOkFile = true, IEnumerable<AiWaveConcatenatedSrcWavList>? sourceFilePathList = null, int headOnlySecs = 0, List<MediaVoiceSegment>? voiceSegments = null, CancellationToken cancel = default)
    {
        if (kbps <= 0) kbps = CoresConfig.DefaultFfMpegExecSettings.FfMpegDefaultAudioKbps;
        if (speedPercent <= 0) speedPercent = 100;
        if (tagTitle._IsEmpty()) tagTitle = PP.GetFileNameWithoutExtension(srcFilePath);

        string digest = $"codec={codec.ToString()},kbps={kbps},metaData={metaData._ObjectToJson()._Digest()},speed={speedPercent}";

        if (useOkFile)
        {
            var okResult = await Lfs.ReadOkFileAsync<FfMpegParsedList>(dstFilePath, digest, AiUtilVersion.CurrentVersion, cancel: cancel);
            if (okResult.IsOk && okResult.Value != null) return okResult.Value;
        }

        // 歌詞メタデータうまくいかない (プレイヤーの問題か？)
        //string? txtFileMetaData = null;

        //if (metaData != null && codec != FfMpegAudioCodec.Wav && metaData.Lyrics._IsFilled())
        //{
        //    txtFileMetaData = await Lfs.GenerateUniqueTempFilePathAsync("lyrics", ".txt", cancel: cancel);
        //}

        //string additionalInputs = "";
        //if (txtFileMetaData._IsFilled())
        //{
        //    additionalInputs = $"-i {txtFileMetaData._EnsureQuotation()} ";
        //}

        string additionalInputs = "";

        if (headOnlySecs >= 1)
        {
            additionalInputs += $"-t {headOnlySecs} ";
        }

        string cmdLine = $"-y -i {srcFilePath._EnsureQuotation()} {additionalInputs}-vn ";

        if (speedPercent != 100)
        {
            double p = (double)speedPercent / 100.0;
            string xstr = p.ToString(".00");
            cmdLine += $"-af atempo={xstr} ";
        }

        switch (codec)
        {
            case FfMpegAudioCodec.Wav:
                cmdLine += $"-reset_timestamps 1 -ar 44100 -ac 2 -c:a pcm_s16le -map_metadata -1 -f wav ";
                break;

            case FfMpegAudioCodec.Flac:
                cmdLine += $"-reset_timestamps 1 -ar 44100 -ac 2 -c:a flac -f flac ";
                break;

            case FfMpegAudioCodec.Mp3:
                cmdLine += $"-reset_timestamps 1 -ar 44100 -ac 2 -c:a libmp3lame -b:a {kbps}k -f mp3 ";
                break;

            case FfMpegAudioCodec.Aac:
                cmdLine += $"-reset_timestamps 1 -ar 44100 -ac 2 -c:a aac -aac_coder twoloop -b:a {kbps}k -f mp4 ";
                break;

            default:
                throw new CoresLibException(nameof(codec));
        }

        if (metaData != null && codec != FfMpegAudioCodec.Wav)
        {
            KeyValueList<string, string> ml = new KeyValueList<string, string>();

            if (metaData.Title._IsFilled()) ml.Add("title", metaData.Title);
            if (metaData.Album._IsFilled()) ml.Add("album", metaData.Album);
            if (metaData.Artist._IsFilled()) ml.Add("artist", metaData.Artist);
            if (metaData.AlbumArtist._IsFilled()) ml.Add("album_artist", metaData.AlbumArtist);

            if (metaData.Track >= 1)
            {
                if (metaData.TrackTotal >= 1 && metaData.Track <= metaData.TrackTotal)
                {
                    ml.Add("track", $"{metaData.Track}/{metaData.TrackTotal}");
                }
                else
                {
                    ml.Add("track", $"{metaData.Track}");
                }
            }

            //if (txtFileMetaData._IsEmpty())
            //{
            foreach (var kv in ml)
            {
                string value = kv.Value._NonNullTrim();

                if (kv.Key._IsDiffi("track"))
                {
                    value = PPWin.MakeSafeFileName(value, false, true, true);
                }

                cmdLine += $"-metadata {kv.Key}={value._EnsureQuotation()} ";
            }
            //}
            // /* 歌詞メタデータうまくいかない (プレイヤーの問題か？) */
            //else
            //{
            //    StringWriter w = new StringWriter();
            //    w.NewLine = Str.NewLine_Str_Unix;
            //    w.WriteLine(";FFMETADATA1");
            //    foreach (var kv in ml)
            //    {
            //        string value = kv.Value._NonNullTrim();

            //        value = PPWin.MakeSafeFileName(value, false, true, true);

            //        w.WriteLine($"{kv.Key}={kv.Value._EnsureQuotation()}");
            //    }
            //    w.WriteLine();
            //    //w.Write("unsyncedlyrics=");
            //    w.Write("lyr=");
            //    foreach (var line in metaData.Lyrics._GetLines(singleLineAtLeast: true))
            //    {
            //        string line2 = line._ReplaceStr("\\", "\\\\")._ReplaceStr("=", "\\=")._ReplaceStr(";", "\\;")._ReplaceStr("#", "\\#");
            //        w.WriteLine(line2);
            //    }
            //    w.Flush();

            //    await Lfs.WriteStringToFileAsync(txtFileMetaData, w.ToString());

            //    cmdLine += $"-map_metadata 1 ";
            //}
        }

        cmdLine += $"{dstFilePath._EnsureQuotation()}";

        await EnsureCreateDirectoryForFileAsync(dstFilePath, cancel);

        await Lfs.DeleteFileIfExistsAsync(dstFilePath, cancel: cancel);

        FfMpegParsedList ret = await RunFfMpegAndParseAsync(cmdLine, cancel);

        try
        {
            var mp3MetaData = await MiscUtil.ReadMP3MetaDataAsync(dstFilePath, cancel);
            if (mp3MetaData != null)
            {
                ret.Meta = mp3MetaData;
            }
        }
        catch { }

        ret.Options_VoiceSegmentsList = voiceSegments;

        if (sourceFilePathList != null)
        {
            ret.Options_UsedBgmSrcMusicList = sourceFilePathList.ToList();
        }

        if (useOkFile)
        {
            await Lfs.WriteOkFileAsync(dstFilePath, ret, digest, AiUtilVersion.CurrentVersion, cancel);
        }

        return ret;
    }

    public async Task<Tuple<FfMpegParsedList, FfMpegParsedList>> AdjustAudioVolumeAsync(string srcFilePath, string dstWavFilePath, double targetMaxVolume, double targetMeanVolume, FfmpegAdjustVolumeOptiono mode, string tagTitle = "", bool useOkFile = true, CancellationToken cancel = default)
    {
        if (tagTitle._IsEmpty()) tagTitle = PP.GetFileNameWithoutExtension(srcFilePath);

        string digest = $"targetMaxVolume={targetMaxVolume},targetMeanVolume={targetMeanVolume}";

        if (useOkFile)
        {
            var okResult = await Lfs.ReadOkFileAsync<Tuple<FfMpegParsedList, FfMpegParsedList>>(dstWavFilePath, digest, AiUtilVersion.CurrentVersion, cancel: cancel);
            if (okResult.IsOk && okResult.Value != null) return okResult.Value;
        }

        // まず、入力オーディオファイルの音量を検出
        var srcParsed = await AnalyzeAudioVolumeDetectAsync(srcFilePath, cancel);

        // 平均音量をどれだけ調整するか (増加量)
        double meanVolumeDelta = targetMeanVolume - srcParsed.VolumeDetect_MeanVolume;

        // 最大音量をどれだけ調整するか (増加量)
        double maxVolumeDelta = targetMaxVolume - srcParsed.VolumeDetect_MaxVolume;

        double adjustDelta;
        if (mode == FfmpegAdjustVolumeOptiono.MeanOnly)
        {
            // 平均音量のみに着目
            adjustDelta = meanVolumeDelta;
        }
        else
        {
            // この 2 つの値のうち小さいほうを採用
            adjustDelta = Math.Min(meanVolumeDelta, maxVolumeDelta);
        }

        string cmdLine = $"-y -i {srcFilePath._EnsureQuotation()} -reset_timestamps 1 -vn -af \"volume={adjustDelta:F1}dB\" -ar 44100 -ac 2 -c:a pcm_s16le -map_metadata -1 -f wav {dstWavFilePath._EnsureQuotation()}";

        await Lfs.DeleteFileIfExistsAsync(dstWavFilePath, cancel: cancel);

        await EnsureCreateDirectoryForFileAsync(dstWavFilePath, cancel);

        await RunFfMpegAndParseAsync(cmdLine, cancel);

        var dstParsed = await AnalyzeAudioVolumeDetectAsync(dstWavFilePath, cancel);

        var ret = new Tuple<FfMpegParsedList, FfMpegParsedList>(srcParsed, dstParsed);

        if (useOkFile)
        {
            await Lfs.WriteOkFileAsync(dstWavFilePath, ret, digest, AiUtilVersion.CurrentVersion, cancel);
        }

        return ret;
    }

    public async Task<FfMpegParsedList> AdjustAudioVolumeAsync(string srcFilePath, string dstWavFilePath, double adjustDelta, CancellationToken cancel = default)
    {
        string cmdLine = $"-y -i {srcFilePath._EnsureQuotation()} -reset_timestamps 1 -vn -af \"volume={adjustDelta:F1}dB\" -ar 44100 -ac 2 -c:a pcm_s16le -map_metadata -1 -f wav {dstWavFilePath._EnsureQuotation()}";

        await Lfs.DeleteFileIfExistsAsync(dstWavFilePath, cancel: cancel);

        await EnsureCreateDirectoryForFileAsync(dstWavFilePath, cancel);

        await RunFfMpegAndParseAsync(cmdLine, cancel);

        var dstParsed = await AnalyzeAudioVolumeDetectAsync(dstWavFilePath, cancel);

        return dstParsed;
    }

    public async Task<string> GenerateNewTmpFilePathAsync(string sampleFileName, string ext = "", CancellationToken cancel = default)
    {
        if (sampleFileName._IsEmpty()) sampleFileName = "test.dat";

        if (ext._IsEmpty())
        {
            ext = PPWin.GetExtension(sampleFileName, emptyWhenNoExtension: true);
        }

        if (ext._IsEmpty()) ext = ".dat";

        string baseFileName = PPWin.GetFileNameWithoutExtension(sampleFileName);

        return await Lfs.GenerateUniqueTempFilePathAsync(baseFileName, ext, cancel: cancel);
    }

    async Task EnsureCreateDirectoryForFileAsync(string filePath, CancellationToken cancel = default)
    {
        try
        {
            await Lfs.CreateDirectoryAsync(PP.GetDirectoryName(filePath), cancel: cancel);
        }
        catch { }
    }

    public async Task<FfMpegParsedList> AnalyzeAudioVolumeDetectAsync(string filePath, CancellationToken cancel = default)
    {
        string cmdLine = $"-i {filePath._EnsureQuotation()} -vn -af volumedetect -f null -";

        var parsed = await RunFfMpegAndParseAsync(cmdLine, cancel);

        try
        {
            var mp3MetaData = await MiscUtil.ReadMP3MetaDataAsync(filePath, cancel);
            if (mp3MetaData != null)
            {
                parsed.Meta = mp3MetaData;
            }
        }
        catch { }

        return parsed;
    }

    public async Task<FfMpegParsedList> ReadMetaDataWithFfProbeAsync(string filePath, bool useMP3OwnImplOverride = true, CancellationToken cancel = default)
    {
        string cmdLine = $"-i {filePath._EnsureQuotation()}";

        var parsed = await RunFfProbeAndParseAsync(cmdLine, cancel);

        if (useMP3OwnImplOverride)
        {
            try
            {
                var mp3MetaData = await MiscUtil.ReadMP3MetaDataAsync(filePath, cancel);
                if (mp3MetaData != null)
                {
                    parsed.Meta = mp3MetaData;
                }
            }
            catch { }
        }

        return parsed;
    }

    public async Task<FfMpegParsedList> RunFfMpegAndParseAsync(string arguments, CancellationToken cancel = default)
    {
        var ret = await RunFfMpegAsync(arguments, cancel);

        var parsed = new FfMpegParsedList(ret.ErrorStr);

        return parsed;
    }

    public async Task<FfMpegParsedList> RunFfProbeAndParseAsync(string arguments, CancellationToken cancel = default)
    {
        var ret = await RunFfProbeAsync(arguments, cancel);

        var parsed = new FfMpegParsedList(ret.ErrorStr);

        return parsed;
    }

    public async Task<EasyExecResult> RunFfMpegAsync(string arguments, CancellationToken cancel = default)
    {
        Con.WriteLine($"[*Run*] {Options.FfMpegExePath} {arguments}");

        EasyExecResult ret = await EasyExec.ExecAsync(Options.FfMpegExePath, arguments, PP.GetDirectoryName(Options.FfMpegExePath),
            flags: ExecFlags.Default | ExecFlags.EasyPrintRealtimeStdOut | ExecFlags.EasyPrintRealtimeStdErr,
            timeout: Timeout.Infinite, cancel: cancel, throwOnErrorExitCode: true,
            easyOutputMaxSize: Options.MaxStdOutBufferSize,
            inputEncoding: Options.Encoding, outputEncoding: Options.Encoding, errorEncoding: Options.Encoding);

        return ret;
    }

    public async Task<EasyExecResult> RunFfProbeAsync(string arguments, CancellationToken cancel = default)
    {
        Con.WriteLine($"[*Run*] {Options.FfProbeExePath} {arguments}");

        EasyExecResult ret = await EasyExec.ExecAsync(Options.FfProbeExePath, arguments, PP.GetDirectoryName(Options.FfMpegExePath),
            flags: ExecFlags.Default | ExecFlags.EasyPrintRealtimeStdOut | ExecFlags.EasyPrintRealtimeStdErr,
            timeout: Timeout.Infinite, cancel: cancel, throwOnErrorExitCode: true,
            easyOutputMaxSize: Options.MaxStdOutBufferSize,
            inputEncoding: Options.Encoding, outputEncoding: Options.Encoding, errorEncoding: Options.Encoding);

        return ret;
    }
}



// 動画データ変換ユーティリティの設定
public class MovEncodeUtilSettings : IValidatable, INormalizable
{
    public string FfMpegExePath = "";
    public string SrcDir = "";
    public string DestDir = "";
    public string SrcExtList = "";
    public double MaxVolume = 0.0;
    public bool AdjustVolume = false;
    public bool Overwrite = false;
    public string DestFormatExt = ".mkv";
    public string FfmpegParams = "";

    public void Validate()
    {
        FfMpegExePath._NotEmptyCheck(nameof(FfMpegExePath));
        SrcDir._NotEmptyCheck(nameof(SrcDir));
        DestDir._NotEmptyCheck(nameof(DestDir));
    }

    public void Normalize()
    {
        if (this.SrcExtList._IsEmpty())
        {
            this.SrcExtList = ".mp4 .avi .mkv .m4v";
        }
        if (this.DestFormatExt._IsEmpty())
        {
            this.DestFormatExt = ".mkv";
        }
        if (this.DestFormatExt.StartsWith(".") == false)
        {
            this.DestFormatExt = "." + this.DestFormatExt;
        }
    }
}

// 動画データ変換ユーティリティ
public class MovEncodeUtil
{
    MovEncodeUtilSettings Settings;

    public MovEncodeUtil(MovEncodeUtilSettings settings)
    {
        this.Settings = settings._CloneDeep();
        this.Settings.Normalize();
        this.Settings.Validate();
    }

    public async Task ExecAsync(CancellationToken cancel = default)
    {
        if (this.Settings.SrcDir._IsSamei(this.Settings.DestDir))
        {
            throw new CoresException("Src is same to dst.");
        }

        var encoding = Str.Utf8Encoding;

        // 元ディレクトリのファイルを列挙
        var items = await Lfs.EnumDirectoryAsync(Settings.SrcDir, recursive: true, cancel: cancel, flags: EnumDirectoryFlags.AllowDirectFilePath);

        // 指定された拡張子リストに一致するファイル一覧を取得
        FileSystemEntity[] srcFiles = items.Where(x => x.IsFile && PP.GetExtension(x.FullPath)._IsFilled() && x.FullPath._IsExtensionMatch(Settings.SrcExtList)).OrderBy(x => x.FullPath, StrComparer.Get(StringComparison.CurrentCultureIgnoreCase)).ToArray();

        int counter = 0;

        foreach (var srcFile in srcFiles)
        {
            try
            {
                string srcRelativePath = PP.GetRelativeFileName(srcFile.FullPath, Settings.SrcDir);
                if (srcRelativePath._IsEmpty())
                {
                    srcRelativePath = PP.GetFileName(srcFile.FullPath);
                }

                string destFilePath = PP.Combine(this.Settings.DestDir, srcRelativePath);

                destFilePath = PP.Combine(PP.GetDirectoryName(destFilePath), PP.GetFileNameWithoutExtension(destFilePath) + this.Settings.DestFormatExt);

                $"Loading '{srcRelativePath}' ({counter + 1} / {srcFiles.Count()}) ..."._Print();

                // 出力先ディレクトリに、fileHashStr を含み、かつ本体および .ok.txt で終わるファイルが存在するかどうか検査
                try
                {
                    await Lfs.CreateDirectoryAsync(Settings.DestDir, cancel: cancel);
                }
                catch { }

                string okTxtPath = PP.Combine(PP.GetDirectoryName(destFilePath), ".okfiles", PP.GetFileName(destFilePath)) + ".ok.txt";
                string okTxtDir = PP.GetDirectoryName(okTxtPath);

                bool dstOkFilesExists = await Lfs.IsFileExistsAsync(okTxtPath, cancel);
                bool dstMovFilesExists = await Lfs.IsFileExistsAsync(destFilePath, cancel: cancel);
                if (this.Settings.Overwrite == false && dstOkFilesExists && dstMovFilesExists)
                {
                    // すでに対象ファイルが存在するので何もしない
                    $"  Skip. Already exists."._Print();
                }
                else
                {
                    List<string> audioFilters = new List<string>();

                    if (this.Settings.AdjustVolume)
                    {
                        // 現在の max_volume 値を取得
                        var result = await EasyExec.ExecAsync(Settings.FfMpegExePath, $"-i \"{srcFile.FullPath._RemoveQuotation()}\" -vn -af volumedetect -f null -", PP.GetDirectoryName(Settings.FfMpegExePath),
                            flags: ExecFlags.Default | ExecFlags.EasyPrintRealtimeStdOut | ExecFlags.EasyPrintRealtimeStdErr,
                            timeout: Timeout.Infinite, cancel: cancel, throwOnErrorExitCode: true,
                            inputEncoding: encoding, outputEncoding: encoding, errorEncoding: encoding);

                        double currentMaxVolume = double.NaN;

                        foreach (var line in result.OutputAndErrorStr._GetLines())
                        {
                            string tag = "max_volume:";
                            int a = line._Search(tag, 0, true);
                            if (a != -1)
                            {
                                string tmp = line.Substring(a + tag.Length);
                                if (tmp.EndsWith(" dB"))
                                {
                                    tmp = tmp.Substring(0, tmp.Length - 3);
                                    tmp = tmp.Trim();

                                    currentMaxVolume = tmp._ToDouble();
                                }
                            }
                        }

                        if (currentMaxVolume != double.NaN && currentMaxVolume < Settings.MaxVolume)
                        {
                            // 何 dB 上げるべきか計算
                            double addVolume = Settings.MaxVolume - currentMaxVolume;

                            audioFilters.Add($"volume={addVolume:F1}dB");
                        }
                    }

                    ("********** " + audioFilters._Combine(" / "))._Print();

                    string audioFilterArgs = " ";
                    if (audioFilters.Any())
                    {
                        audioFilterArgs = $"-af \"{audioFilters._Combine(" , ")}\"";
                    }

                    // 動画を変換
                    await ProcessOneFileAsync(srcFile.FullPath, destFilePath, $"{this.Settings.FfmpegParams} {audioFilterArgs}",
                        encoding, cancel);
                }
            }
            catch (Exception ex)
            {
                ex._Error();
            }

            counter++;
        }
    }

    async Task ProcessOneFileAsync(string srcPath, string dstPath, string args, Encoding encoding, CancellationToken cancel = default)
    {
        var now = DtOffsetNow;
        string okTxtPath = PP.Combine(PP.GetDirectoryName(dstPath), ".okfiles", PP.GetFileName(dstPath)) + ".ok.txt";
        string okTxtDir = PP.GetDirectoryName(okTxtPath);

        try
        {
            await Lfs.CreateDirectoryAsync(PP.GetDirectoryName(dstPath), cancel: cancel);
        }
        catch { }

        if (Settings.Overwrite == false)
        {
            // 宛先パス + .ok.txt ファイルが存在していれば何もしない
            if (await Lfs.IsFileExistsAsync(okTxtPath, cancel) && await Lfs.IsFileExistsAsync(dstPath, cancel))
            {
                return;
            }
        }

        // 変換を実施
        string cmdLine = $"-y -i \"{srcPath._RemoveQuotation()}\" {args} \"{dstPath._RemoveQuotation()}\"";

        (" * cmdline = " + cmdLine)._Print();

        var result = await EasyExec.ExecAsync(Settings.FfMpegExePath, cmdLine, PP.GetDirectoryName(Settings.FfMpegExePath),
            flags: ExecFlags.Default | ExecFlags.EasyPrintRealtimeStdOut | ExecFlags.EasyPrintRealtimeStdErr,
            timeout: Timeout.Infinite, cancel: cancel, throwOnErrorExitCode: true,
            inputEncoding: encoding, outputEncoding: encoding, errorEncoding: encoding);

        // 成功したら ok.txt を保存
        string okTxtBody = now._ToDtStr(true, DtStrOption.All, true) + "\r\n\r\n" + cmdLine + "\r\n\r\n" + result.ErrorAndOutputStr;
        try
        {
            await Lfs.TryAddOrRemoveAttributeFromExistingFileAsync(okTxtPath, 0, FileAttributes.Hidden, cancel: cancel);
            await Lfs.WriteStringToFileAsync(okTxtPath, okTxtBody._NormalizeCrlf(CrlfStyle.CrLf, true), writeBom: true, flags: FileFlags.AutoCreateDirectory, cancel: cancel);
            await Lfs.TryAddOrRemoveAttributeFromExistingDirAsync(okTxtDir, FileAttributes.Hidden, cancel: cancel);
            await Lfs.TryAddOrRemoveAttributeFromExistingFileAsync(okTxtPath, FileAttributes.Hidden, cancel: cancel);
        }
        catch (Exception ex)
        {
            ex._Error();
        }
    }
}





// 動画データ変換整理ユーティリティの設定
public class MovYaiUtilSettings : IValidatable, INormalizable
{
    public string FfMpegExePath = "";
    public string SrcDir = "";
    public string DestDir = "";
    public string SrcExtList = "";
    public string SeriesStr = "";
    public double MaxVolume = 0.0;
    public bool Overwrite = false;
    public string DestFormatExt = ".mkv";

    public int Hash_MaxPrefixDirLevel1Int = 20;
    public int Hash_MaxPrefixDirLevel2Int = 99999999;
    public string Hash_DelimiterStr = "-";
    public int Hash_InsertStrPositionOnLevel2 = 4;

    public void Validate()
    {
        FfMpegExePath._NotEmptyCheck(nameof(FfMpegExePath));
        SrcDir._NotEmptyCheck(nameof(SrcDir));
        DestDir._NotEmptyCheck(nameof(DestDir));
    }

    public void Normalize()
    {
        if (this.SeriesStr._IsEmpty())
        {
            this.SeriesStr = "Unknown";
        }
        if (this.SrcExtList._IsEmpty())
        {
            this.SrcExtList = ".mp4 .avi .mkv .m4v";
        }
        if (this.Hash_MaxPrefixDirLevel1Int <= 0)
        {
            this.Hash_MaxPrefixDirLevel1Int = 20;
        }
        if (this.Hash_MaxPrefixDirLevel2Int <= 0)
        {
            this.Hash_MaxPrefixDirLevel2Int = 99999999;
        }
        if (this.Hash_DelimiterStr._IsNullOrZeroLen())
        {
            this.Hash_DelimiterStr = "-";
        }
        if (this.Hash_InsertStrPositionOnLevel2 <= 0)
        {
            this.Hash_InsertStrPositionOnLevel2 = 4;
        }
        if (this.DestFormatExt._IsEmpty())
        {
            this.DestFormatExt = ".mkv";
        }
        if (this.DestFormatExt.StartsWith(".") == false)
        {
            this.DestFormatExt = "." + this.DestFormatExt;
        }
    }
}

// 動画データ変換整理ユーティリティ
public class MovYaiUtil
{
    MovYaiUtilSettings Settings;

    public MovYaiUtil(MovYaiUtilSettings settings)
    {
        this.Settings = settings._CloneDeep();
        this.Settings.Normalize();
        this.Settings.Validate();
    }

    public static string GenerateFilePrefixStr(byte[] fileHashSha1, int maxLevel1Number, long maxLevel2Number, string delimiterStr = "-", int insertStrPositionOnLevel2 = 4)
    {
        maxLevel1Number += 1;
        maxLevel2Number += 1;
        if (fileHashSha1.Length != Secure.SHA1Size)
        {
            throw new ArgumentException(nameof(fileHashSha1));
        }
        if (maxLevel1Number <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxLevel1Number));
        }

        SeedBasedRandomGenerator rand = new SeedBasedRandomGenerator(fileHashSha1, SHA1.Create());

        long v1 = rand.GetSInt63();

        int n1 = (int)(v1 % maxLevel1Number);

        string level1Str = n1.ToString();
        int maxLevel1Len = (maxLevel1Number - 1).ToString().Length;
        int level1PadSize = maxLevel1Len - level1Str.Length;
        level1Str = Str.MakeCharArray('0', level1PadSize) + level1Str;

        long v2 = rand.GetSInt63();
        long n2 = v2 % maxLevel2Number;

        string level2Str = n2.ToString();
        int maxLevel2Len = (maxLevel2Number - 1).ToString().Length;
        int level2PadSize = maxLevel2Len - level2Str.Length;
        level2Str = Str.MakeCharArray('0', level2PadSize) + level2Str;

        level2Str = level2Str._InsertStrIntoStr(delimiterStr, insertStrPositionOnLevel2);

        return level1Str + "-" + level2Str;
    }

    public async Task ExecAsync(CancellationToken cancel = default)
    {
        if (this.Settings.SrcDir._IsSamei(this.Settings.DestDir))
        {
            throw new CoresException("Src is same to dst.");
        }

        var encoding = Str.Utf8Encoding;

        // 元ディレクトリのファイルを列挙
        var items = await Lfs.EnumDirectoryAsync(Settings.SrcDir, recursive: true, cancel: cancel, flags: EnumDirectoryFlags.AllowDirectFilePath);

        // 指定された拡張子リストに一致するファイル一覧を取得
        FileSystemEntity[] srcFiles = items.Where(x => x.IsFile && PP.GetExtension(x.FullPath)._IsFilled() && x.FullPath._IsExtensionMatch(Settings.SrcExtList)).OrderBy(x => x.FullPath, StrComparer.Get(StringComparison.CurrentCultureIgnoreCase)).ToArray();

        int counter = 0;

        using var sha1Algorithm = SHA1.Create();

        foreach (var srcFile in srcFiles)
        {
            try
            {
                string srcRelativePath = PP.GetRelativeFileName(srcFile.FullPath, Settings.SrcDir);
                if (srcRelativePath._IsEmpty())
                {
                    srcRelativePath = PP.GetFileName(srcFile.FullPath);
                }

                $"Loading '{srcRelativePath}' ({counter + 1} / {srcFiles.Count()}) ..."._Print();

                byte[] fileHash = await Lfs.CalcFileHashAsync(srcFile.FullPath, sha1Algorithm, cancel: cancel);
                //byte[] fileHash = Secure.HashSHA1("Hello"._GetBytes_Ascii());

                string fileHashStr = GenerateFilePrefixStr(fileHash, Settings.Hash_MaxPrefixDirLevel1Int, Settings.Hash_MaxPrefixDirLevel2Int, Settings.Hash_DelimiterStr, Settings.Hash_InsertStrPositionOnLevel2);
                string fileHashStrFirstToken = fileHashStr._Split(StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries, Settings.Hash_DelimiterStr)[0];

                int fileHashInt = fileHashStr._ReplaceStr(Settings.Hash_DelimiterStr, "")._ToInt();

                string fileHashStrWithBrakets = "[" + fileHashStr + "]";

                // 出力先ディレクトリに、fileHashStr を含み、かつ本体および .ok.txt で終わるファイルが存在するかどうか検査
                try
                {
                    await Lfs.CreateDirectoryAsync(Settings.DestDir, cancel: cancel);
                }
                catch { }

                var dstOkFilesExists = await Lfs.EnumDirectoryAsync(Settings.DestDir, true, wildcard: $"*{fileHashStrWithBrakets}*.ok.txt", cancel: cancel);
                var dstMovFilesExists = await Lfs.EnumDirectoryAsync(Settings.DestDir, true, wildcard: $"*{fileHashStrWithBrakets}*{Settings.DestFormatExt}", cancel: cancel);
                if (dstOkFilesExists.Any() && dstMovFilesExists.Any())
                {
                    // すでに対象ファイルが存在するので何もしない
                    $"  Skip. Already exists."._Print();
                }
                else
                {
                    // 出力先フルパスを決定
                    string destRelativePath = fileHashStrFirstToken + Lfs.PathParser.DirectorySeparator + Settings.SeriesStr._MakeVerySafeAsciiOnlyNonSpaceFileName() + " - " + fileHashStrWithBrakets + " " + Lfs.PathParser.GetFileNameWithoutExtension(srcFile.Name)._NormalizeSoftEther(true) + Settings.DestFormatExt;

                    destRelativePath._Print();

                    List<string> audioFilters = new List<string>();

                    // 現在の max_volume 値を取得
                    var result = await EasyExec.ExecAsync(Settings.FfMpegExePath, $"-i \"{srcFile.FullPath._RemoveQuotation()}\" -vn -af volumedetect -f null -", PP.GetDirectoryName(Settings.FfMpegExePath),
                        flags: ExecFlags.Default | ExecFlags.EasyPrintRealtimeStdOut | ExecFlags.EasyPrintRealtimeStdErr,
                        timeout: Timeout.Infinite, cancel: cancel, throwOnErrorExitCode: true,
                        inputEncoding: encoding, outputEncoding: encoding, errorEncoding: encoding);

                    double currentMaxVolume = double.NaN;

                    foreach (var line in result.OutputAndErrorStr._GetLines())
                    {
                        string tag = "max_volume:";
                        int a = line._Search(tag, 0, true);
                        if (a != -1)
                        {
                            string tmp = line.Substring(a + tag.Length);
                            if (tmp.EndsWith(" dB"))
                            {
                                tmp = tmp.Substring(0, tmp.Length - 3);
                                tmp = tmp.Trim();

                                currentMaxVolume = tmp._ToDouble();
                            }
                        }
                    }

                    if (currentMaxVolume != double.NaN && currentMaxVolume < Settings.MaxVolume)
                    {
                        // 何 dB 上げるべきか計算
                        double addVolume = Settings.MaxVolume - currentMaxVolume;

                        audioFilters.Add($"volume={addVolume:F1}dB");
                    }

                    ("********** " + audioFilters._Combine(" / "))._Print();

                    string artist = Settings.SeriesStr._NormalizeSoftEther();

                    string destFullPath = Lfs.PP.Combine(Settings.DestDir, destRelativePath);

                    string audioFilterArgs = " ";
                    if (audioFilters.Any())
                    {
                        audioFilterArgs = $"-af \"{audioFilters._Combine(" , ")}\"";
                    }

                    // 動画を変換
                    await ProcessOneFileAsync(srcFile.FullPath, destFullPath, $"-c:v libx264 -pix_fmt yuv420p -profile:v high -crf 18 {audioFilterArgs}",
                        artist,
                        artist,
                        Settings.SeriesStr._MakeVerySafeAsciiOnlyNonSpaceFileName() + " - " + fileHashStrWithBrakets + " " + Lfs.PathParser.GetFileNameWithoutExtension(srcFile.Name)._NormalizeSoftEther(true),
                        fileHashInt, int.MaxValue,
                        encoding, cancel);
                }
            }
            catch (Exception ex)
            {
                ex._Error();
            }

            counter++;
        }
    }

    async Task ProcessOneFileAsync(string srcPath, string dstPath, string args, string artist, string album, string title, int track, int maxTracks, Encoding encoding, CancellationToken cancel = default)
    {
        var now = DtOffsetNow;
        string okTxtPath = PP.Combine(PP.GetDirectoryName(dstPath), ".okfiles", PP.GetFileName(dstPath)) + ".ok.txt";
        string okTxtDir = PP.GetDirectoryName(okTxtPath);

        try
        {
            await Lfs.CreateDirectoryAsync(PP.GetDirectoryName(dstPath), cancel: cancel);
        }
        catch { }

        if (Settings.Overwrite == false)
        {
            // 宛先パス + .ok.txt ファイルが存在していれば何もしない
            if (await Lfs.IsFileExistsAsync(okTxtPath, cancel) && await Lfs.IsFileExistsAsync(dstPath, cancel))
            {
                return;
            }
        }

        // 変換を実施
        string cmdLine = $"-y -i \"{srcPath._RemoveQuotation()}\" {args} -metadata title=\"{title}\" -metadata album=\"{album}\" -metadata artist=\"{artist}\" -metadata track=\"{track}/{maxTracks}\" \"{dstPath._RemoveQuotation()}\"";

        (" * cmdline = " + cmdLine)._Print();

        var result = await EasyExec.ExecAsync(Settings.FfMpegExePath, cmdLine, PP.GetDirectoryName(Settings.FfMpegExePath),
            flags: ExecFlags.Default | ExecFlags.EasyPrintRealtimeStdOut | ExecFlags.EasyPrintRealtimeStdErr,
            timeout: Timeout.Infinite, cancel: cancel, throwOnErrorExitCode: true,
            inputEncoding: encoding, outputEncoding: encoding, errorEncoding: encoding);

        // 成功したら ok.txt を保存
        string okTxtBody = now._ToDtStr(true, DtStrOption.All, true) + "\r\n\r\n" + cmdLine + "\r\n\r\n" + result.ErrorAndOutputStr;
        try
        {
            await Lfs.TryAddOrRemoveAttributeFromExistingFileAsync(okTxtPath, 0, FileAttributes.Hidden, cancel: cancel);
            await Lfs.WriteStringToFileAsync(okTxtPath, okTxtBody._NormalizeCrlf(CrlfStyle.CrLf, true), writeBom: true, flags: FileFlags.AutoCreateDirectory, cancel: cancel);
            await Lfs.TryAddOrRemoveAttributeFromExistingDirAsync(okTxtDir, FileAttributes.Hidden, cancel: cancel);
            await Lfs.TryAddOrRemoveAttributeFromExistingFileAsync(okTxtPath, FileAttributes.Hidden, cancel: cancel);
        }
        catch (Exception ex)
        {
            ex._Error();
        }
    }
}




// e ラーニング用画像教材生成ユーティリティの設定
public class MovLearnUtilSettings : IValidatable, INormalizable
{
    public string FfMpegExePath = "";
    public string SrcDir = "";
    public string DestDir = "";
    public string SrcExtList = "";
    public double MaxVolume = 0.0;
    public bool Overwrite = false;

    public void Validate()
    {
        FfMpegExePath._NotEmptyCheck(nameof(FfMpegExePath));
        SrcDir._NotEmptyCheck(nameof(SrcDir));
        DestDir._NotEmptyCheck(nameof(DestDir));
    }

    public void Normalize()
    {
        if (this.SrcExtList._IsEmpty())
        {
            this.SrcExtList = ".mp4";
        }
    }
}

// e ラーニング用教材生成ユーティリティ
public class MovLearnUtil
{
    MovLearnUtilSettings Settings;

    public MovLearnUtil(MovLearnUtilSettings settings)
    {
        this.Settings = settings._CloneDeep();
        this.Settings.Normalize();
        this.Settings.Validate();
    }

    public async Task ExecAsync(CancellationToken cancel = default)
    {
        var encoding = Str.Utf8Encoding;

        // 元ディレクトリのファイルを列挙
        var items = await Lfs.EnumDirectoryAsync(Settings.SrcDir, recursive: true, cancel: cancel);

        // 指定された拡張子リストに一致するファイル一覧を取得
        var srcFiles = items.Where(x => x.IsFile && PP.GetExtension(x.FullPath)._IsFilled() && x.FullPath._IsExtensionMatch(Settings.SrcExtList)).OrderBy(x => x.FullPath, StrComparer.Get(StringComparison.CurrentCultureIgnoreCase));

        int counter = 0;

        foreach (var srcFile in srcFiles)
        {
            try
            {
                string relativeFileName = PP.GetRelativeFileName(srcFile.FullPath, Settings.SrcDir);

                var tokens = PP.SplitTokens(relativeFileName);
                if (tokens.Length == 3)
                {
                    // このファイルと同じディレクトリのファイルを列挙
                    string srcFileDirPath = PP.GetDirectoryName(srcFile.FullPath);
                    var sameDirFiles = srcFiles.Where(x => PP.GetDirectoryName(x.FullPath) == srcFileDirPath).ToArray();

                    int maxTracks = sameDirFiles.Length;
                    int trackNumber = 1;

                    for (int i = 0; i < sameDirFiles.Length; i++)
                    {
                        if (sameDirFiles[i] == srcFile)
                        {
                            trackNumber = (i + 1);
                            break;
                        }
                    }

                    maxTracks = Math.Max(maxTracks, trackNumber);

                    string artist = tokens[0]._NormalizeSoftEther(true);
                    string albumBase = tokens[1]._NormalizeSoftEther(true);
                    string titleBase = PP.GetFileNameWithoutExtension(tokens[2]._NormalizeSoftEther(true));

                    artist = Str.MakeSafePathNameShiftJis(artist).Replace("\"", "_").Replace("\'", "_");
                    albumBase = Str.MakeSafePathNameShiftJis(albumBase).Replace("\"", "_").Replace("\'", "_");
                    titleBase = Str.MakeSafePathNameShiftJis(titleBase).Replace("\"", "_").Replace("\'", "_");

                    string fn = Str.MakeSafePathNameShiftJis(PP.GetFileNameWithoutExtension(srcFile.FullPath)).Replace("\"", "_").Replace("\'", "_")._NormalizeSoftEther(true);

                    string albumSimple = albumBase._FilledOrDefault(fn);

                    artist = artist._FilledOrDefault(fn);
                    albumBase = artist + " - " + albumBase._FilledOrDefault(fn);
                    titleBase = titleBase._FilledOrDefault(fn);

                    string relativeDirName = PP.GetDirectoryName(relativeFileName);
                    string destDirPath = PP.Combine(Settings.DestDir, relativeDirName);
                    string srcFileMain = PP.GetFileNameWithoutExtension(srcFile.FullPath);

                    $"Processing '{relativeFileName}' ({counter + 1} / {srcFiles.Count()}) ..."._Print();

                    if (true)
                    {
                        // 1. まず、音声ファイル群の生成
                        List<string> audioFilters = new List<string>();

                        // 1.1. ベースの x1.00 ファイルの生成

                        // 現在の max_volume 値を取得
                        var result = await EasyExec.ExecAsync(Settings.FfMpegExePath, $"-i \"{srcFile.FullPath._RemoveQuotation()}\" -vn -af volumedetect -f null -", PP.GetDirectoryName(Settings.FfMpegExePath),
                            flags: ExecFlags.Default | ExecFlags.EasyPrintRealtimeStdOut | ExecFlags.EasyPrintRealtimeStdErr,
                            timeout: Timeout.Infinite, cancel: cancel, throwOnErrorExitCode: true,
                            inputEncoding: encoding, outputEncoding: encoding, errorEncoding: encoding);

                        double currentMaxVolume = double.NaN;

                        foreach (var line in result.OutputAndErrorStr._GetLines())
                        {
                            string tag = "max_volume:";
                            int a = line._Search(tag, 0, true);
                            if (a != -1)
                            {
                                string tmp = line.Substring(a + tag.Length);
                                if (tmp.EndsWith(" dB"))
                                {
                                    tmp = tmp.Substring(0, tmp.Length - 3);
                                    tmp = tmp.Trim();

                                    currentMaxVolume = tmp._ToDouble();
                                }
                            }
                        }

                        if (currentMaxVolume != double.NaN && currentMaxVolume < Settings.MaxVolume)
                        {
                            // 何 dB 上げるべきか計算
                            double addVolume = Settings.MaxVolume - currentMaxVolume;

                            audioFilters.Add($"volume={addVolume:F1}dB");
                        }

                        // 無音除去を実施、音量調整も実施
                        string audio_base_path = PP.Combine(destDirPath, albumBase + $" - audio.x1.00", $"{albumSimple} [{trackNumber:D2}] {titleBase} - audio.x1.00.m4a");
                        audioFilters.Add($"silenceremove=window=5:detection=peak:stop_mode=all:start_mode=all:stop_periods=-1:stop_threshold=-30dB");
                        await ProcessOneFileAsync(srcFile.FullPath, audio_base_path, $"-vn -f mp4 -b:a 192k -aac_coder twoloop -af \"{audioFilters._Combine(" , ")}\"",
                            artist + $" - audio.x1.00",
                            albumBase + " - audio.x1.00",
                            albumSimple + $" [{trackNumber:D2}] - " + titleBase + " - audio.x1.00",
                            trackNumber, maxTracks,
                            encoding, cancel);

                        // 2.2. 数倍速再生版も作る
                        string[] xList = { "1.25", "1.50", "2.00" };

                        foreach (var xstr in xList)
                        {
                            string audio_x_path = PP.Combine(destDirPath, albumBase + $" - audio.x{xstr}", $"{albumSimple} [{trackNumber:D2}] {titleBase} - audio.x{xstr}.m4a");
                            await ProcessOneFileAsync(audio_base_path, audio_x_path, $"-vn -f mp4 -b:a 192k -aac_coder twoloop -af atempo={xstr}",
                                artist + $" - audio.x{xstr}",
                                albumBase + $" - audio.x{xstr}",
                                albumSimple + $" [{trackNumber:D2}] - " + titleBase + $" - audio.x{xstr}",
                                trackNumber, maxTracks,
                                encoding, cancel);
                        }
                    }

                    if (false)
                    {
                        // 2. 次に、動画ファイル群の生成
                        List<string> audioFilters = new List<string>();

                        // 2.1. ベースの x1.0 ファイルの生成

                        // 現在の max_volume 値を取得
                        var result = await EasyExec.ExecAsync(Settings.FfMpegExePath, $"-i \"{srcFile.FullPath._RemoveQuotation()}\" -vn -af volumedetect -f null -", PP.GetDirectoryName(Settings.FfMpegExePath),
                            flags: ExecFlags.Default | ExecFlags.EasyPrintRealtimeStdOut | ExecFlags.EasyPrintRealtimeStdErr,
                            timeout: Timeout.Infinite, cancel: cancel, throwOnErrorExitCode: true,
                            inputEncoding: encoding, outputEncoding: encoding, errorEncoding: encoding);

                        double currentMaxVolume = double.NaN;

                        foreach (var line in result.OutputAndErrorStr._GetLines())
                        {
                            string tag = "max_volume:";
                            int a = line._Search(tag, 0, true);
                            if (a != -1)
                            {
                                string tmp = line.Substring(a + tag.Length);
                                if (tmp.EndsWith(" dB"))
                                {
                                    tmp = tmp.Substring(0, tmp.Length - 3);
                                    tmp = tmp.Trim();

                                    currentMaxVolume = tmp._ToDouble();
                                }
                            }
                        }

                        if (currentMaxVolume != double.NaN && currentMaxVolume < Settings.MaxVolume)
                        {
                            // 何 dB 上げるべきか計算
                            double addVolume = Settings.MaxVolume - currentMaxVolume;

                            audioFilters.Add($"volume={addVolume:F1}dB");
                        }
                        else
                        {
                            audioFilters.Add($"volume=0.0dB");
                        }

                        // 無音除去を実施、音量調整実施
                        string video_base_path = PP.Combine(destDirPath, albumBase + $" - video.x1.0", $"{albumSimple} [{trackNumber:D2}] {titleBase} - video.x1.0.mp4");
                        await ProcessOneFileAsync(srcFile.FullPath, video_base_path, $"-af \"{audioFilters._Combine(" , ")}\"",
                            artist + $" - video.x1.0",
                            albumBase + " - video.x1.0",
                            albumSimple + $" [{trackNumber:D2}] - " + titleBase + " - video.x1.0",
                            trackNumber, maxTracks,
                            encoding, cancel);

                        // 2.2. 数倍速再生版も作る
                        string[] xList = { "1.5", "2.0", "2.5", "3.0", "3.5" };

                        foreach (var xstr in xList)
                        {
                            string video_x_path = PP.Combine(destDirPath, albumBase + $" - video.x{xstr}", $"{albumSimple} [{trackNumber:D2}] {titleBase} - video.x{xstr}.mp4");
                            await ProcessOneFileAsync(video_base_path, video_x_path, $"-vf setpts=PTS/{xstr} -af atempo={xstr}",
                                artist + $" - video.x{xstr}",
                                albumBase + $" - video.x{xstr}",
                                albumSimple + $" [{trackNumber:D2}] - " + titleBase + $" - video.x{xstr}",
                                trackNumber, maxTracks,
                                encoding, cancel);
                        }
                    }
                }
                else
                {
                    $"Skip: '{relativeFileName}' ({counter + 1} / {srcFiles.Count()})"._Print();
                }
            }
            catch (Exception ex)
            {
                ex._Error();
            }

            counter++;
        }
    }

    async Task ProcessOneFileAsync(string srcPath, string dstPath, string args, string artist, string album, string title, int track, int maxTracks, Encoding encoding, CancellationToken cancel = default)
    {
        var now = DtOffsetNow;
        string okTxtPath = PP.Combine(PP.GetDirectoryName(dstPath), ".okfiles", PP.GetFileName(dstPath)) + ".ok.txt";
        string okTxtDir = PP.GetDirectoryName(okTxtPath);

        try
        {
            await Lfs.CreateDirectoryAsync(PP.GetDirectoryName(dstPath), cancel: cancel);
        }
        catch { }

        if (Settings.Overwrite == false)
        {
            // 宛先パス + .ok.txt ファイルが存在していれば何もしない
            if (await Lfs.IsFileExistsAsync(okTxtPath, cancel))
            {
                return;
            }
        }

        // 変換を実施
        string cmdLine = $"-y -i \"{srcPath._RemoveQuotation()}\" {args} -metadata title=\"{title}\" -metadata album=\"{album}\" -metadata artist=\"{artist}\" -metadata track=\"{track}/{maxTracks}\" \"{dstPath._RemoveQuotation()}\"";

        var result = await EasyExec.ExecAsync(Settings.FfMpegExePath, cmdLine, PP.GetDirectoryName(Settings.FfMpegExePath),
            flags: ExecFlags.Default | ExecFlags.EasyPrintRealtimeStdOut | ExecFlags.EasyPrintRealtimeStdErr,
            timeout: Timeout.Infinite, cancel: cancel, throwOnErrorExitCode: true,
            inputEncoding: encoding, outputEncoding: encoding, errorEncoding: encoding);

        // 成功したら ok.txt を保存
        string okTxtBody = now._ToDtStr(true, DtStrOption.All, true) + "\r\n\r\n" + cmdLine + "\r\n\r\n" + result.ErrorAndOutputStr;
        try
        {
            await Lfs.TryAddOrRemoveAttributeFromExistingFileAsync(okTxtPath, 0, FileAttributes.Hidden, cancel: cancel);
            await Lfs.WriteStringToFileAsync(okTxtPath, okTxtBody._NormalizeCrlf(CrlfStyle.CrLf, true), writeBom: true, flags: FileFlags.AutoCreateDirectory, cancel: cancel);
            await Lfs.TryAddOrRemoveAttributeFromExistingDirAsync(okTxtDir, FileAttributes.Hidden, cancel: cancel);
            await Lfs.TryAddOrRemoveAttributeFromExistingFileAsync(okTxtPath, FileAttributes.Hidden, cancel: cancel);
        }
        catch (Exception ex)
        {
            ex._Error();
        }
    }
}

// タイムスタンプ・ユーティリティの設定
public class TimeStampDocsSetting : INormalizable
{
    public SmtpClientSettings SmtpSettings = new SmtpClientSettings();

    public string MailZipPassword = "";

    public string TimeStampConfigFilename = "";

    public string OutputFilenamePrefix = "";

    public int MaxFilesInProject = 0;
    public int MaxFilesTotal = 0;

    public int SingleMailZipFileSize = 7140000;

    public int MailTryCount = 3;

    public string LogDir = "";

    public string TimeStampProjectLogDirName = "";

    public string MailFrom = "";
    public string MailTo = "";
    public string MailCc = "";

    public string MailSubjectPrefix = "";

    public void Normalize()
    {
        if (this.MailZipPassword._IsEmpty()) this.MailZipPassword = "password";

        if (this.SmtpSettings == null) this.SmtpSettings = new SmtpClientSettings();
        this.SmtpSettings.Normalize();

        if (this.TimeStampConfigFilename._IsEmpty()) this.TimeStampConfigFilename = "_timestamp.txt";

        if (this.MaxFilesInProject <= 0) this.MaxFilesInProject = 10_0000;
        if (this.MaxFilesTotal <= 0) this.MaxFilesTotal = 100_0000;

        if (OutputFilenamePrefix._IsEmpty()) OutputFilenamePrefix = Env.MachineName.ToLower();
        OutputFilenamePrefix = Str.MakeVerySafeAsciiOnlyNonSpaceFileName(OutputFilenamePrefix, true);

        if (this.LogDir._IsEmpty()) this.LogDir = PP.Combine(Env.AppRootDir, "TimeStampArchives_DoNotDeleteMe");

        if (this.TimeStampProjectLogDirName._IsEmpty()) this.TimeStampProjectLogDirName = "_timestamp_archives.do_not_delete_me";

        if (this.MailFrom._IsEmpty()) this.MailFrom = "nobody@example.org";
        if (this.MailTo._IsEmpty()) this.MailTo = "nobody@example.org";
        if (this.MailCc._IsEmpty()) this.MailCc = "nobody@example.org";

        if (this.MailSubjectPrefix._IsEmpty()) this.MailSubjectPrefix = "[TimeStampDocs]";
    }
}

// タイムスタンプ・ユーティリティ
public class TimeStampDocsUtil : AsyncService
{
    readonly HiveData<TimeStampDocsSetting> SettingsHive;
    public TimeStampDocsSetting Settings => SettingsHive.CachedFastSnapshot;

    [Flags]
    public enum TimeStampFileFilterAction
    {
        Include = 0,
        Exclude,
    }

    [Flags]
    public enum TimeStampFileFilterType
    {
        ForFile = 0,
        ForDir,
    }

    [Flags]
    public enum WriteFlags
    {
        None = 0,
        FullPath = 1,
    }

    public class TimeStampFileFilter
    {
        public TimeStampFileFilterAction Action = TimeStampFileFilterAction.Include;
        public TimeStampFileFilterType Type = TimeStampFileFilterType.ForFile;
        public string Wildcard = "";
    }

    public class FileData
    {
        public string FullPath = "";
        public string RelativePath = "";
        public long FileSize = 0;
        public DateTimeOffset DateCreated;
        public DateTimeOffset DateLastWrite;
        public string Md5 = "";
        public string Sha512 = "";
    }

    public class DirData
    {
        public string FullPath = "";
        public string RelativePath = "";

        public List<FileData> FileList = new List<FileData>();

        public void WriteTo(TextWriter t, DateTimeOffset now, WriteFlags flags)
        {
            t.WriteLine($"# このフォルダには、{this.FileList.Count._ToString3()} 個のファイルが存在します。");
            t.WriteLine($"# これらのファイルの合計サイズは、{this.FileList.Sum(x => x.FileSize)._ToString3()} bytes ({this.FileList.Sum(x => x.FileSize)._GetFileSizeStr()}) です。");
            t.WriteLine($"# ファイルの名前やサイズ、ハッシュ値は以下のとおりです。");
            t.WriteLine($"# 以下のファイルサイズおよびハッシュ値の計算結果は、{now._ToDtStr(false)} 時点のものです。");
            t.WriteLine($"# created (作成日時) と lastwrite (更新日時) は、ファイルシステム上の情報なので、これらは実際の作成・更新時よりも後を示す可能性があります。");
            t.WriteLine($"# このハッシュ表のテキストファイルを、タイムスタンプ署名で署名している場合、その署名のタイムスタンプ日時において、以下のハッシュ値を有するファイルが既に確かに存在していたことが、本システムによって、証明されます。");
            t.WriteLine($"# このテキストファイルには、ハッシュ値しかありません。ファイル本体そのものは、別に保存されていなければなりません。ファイル本体がなければ、ハッシュ値だけが残っていても、肝心のファイル内容を証明することはできません。");
            t.WriteLine("# index,filename,size,md5,sha512,created,lastwrite");

            int index = 0;

            foreach (var file in FileList.OrderBy(x => x.FullPath, StrCmpi))
            {
                index++;

                List<string> tokens = new List<string>
                {
                    index.ToString(),
                    PPMac.GetFileName(flags.Bit( WriteFlags.FullPath) ? file.FullPath : file.RelativePath).Replace(",", "，"),
                    file.FileSize.ToString(),
                    file.Md5,
                    file.Sha512,
                    file.DateCreated._ToDtStr(true),
                    file.DateLastWrite._ToDtStr(true)
                };

                t.WriteLine(tokens._Combine(","));
            }
        }
    }

    public class ProjectResult
    {
        public int NumFiles_Inc;

        public List<DirData> DirList = new List<DirData>();
        public string RootDirPath = "";
        public string RootDirName = "";

        public int NumFiles => this.DirList.Sum(x => x.FileList.Count);
        public long TotalFileSize => this.DirList.Sum(x => x.FileList.Sum(y => y.FileSize));

        public List<string> SettingsFiles = new List<string>();

        public void WriteTo(TextWriter t, DateTimeOffset now, WriteFlags flags)
        {
            t.WriteLine($"# このプロジェクトには、{this.DirList.Count._ToString3()} 個のフォルダ中に、{this.DirList.Sum(x => x.FileList.Count)._ToString3()} 個のファイルが存在します。");
            t.WriteLine($"# これらのファイルの合計サイズは、{this.DirList.Sum(y => y.FileList.Sum(x => x.FileSize))._ToString3()} bytes ({this.DirList.Sum(y => y.FileList.Sum(x => x.FileSize))._GetFileSizeStr()}) です。");
            t.WriteLine($"# 以下で、フォルダごとのファイルの一覧を並べ上げて、各ファイルのハッシュ値を明示いたします。");
            t.WriteLine();

            var list = this.DirList.OrderBy(x => x.FullPath, StrCmpi).ToList();

            for (int i = 0; i < list.Count; i++)
            {
                var dir = list[i];

                t.WriteLine($"■ フォルダ ({i + 1}/{list.Count}) 『{(flags.Bit(WriteFlags.FullPath) ? dir.FullPath : dir.RelativePath)}』");

                dir.WriteTo(t, now, flags);

                t.WriteLine();
            }
        }
    }

    public class TimeStampConfig
    {
        public List<TimeStampFileFilter> Filters = new List<TimeStampFileFilter>();
        public StrDictionary<string> Settings = new StrDictionary<string>(StrTrimCmpi);

        public TimeStampConfig() { }

        public TimeStampConfig(string body)
        {
            string[] lines = body._GetLines(true, true, new string[] { "#" }, trim: true);

            foreach (var line in lines)
            {
                if (line._GetKeyAndValue(out string key, out string value, ":"))
                {
                    key = key._NonNullTrim();
                    value = value._NonNullTrim();
                    if (key._IsFilled())
                    {
                        this.Settings.Add(key, value);
                    }
                }
                else
                {
                    string wildcard = PPMac.NormalizeDirectorySeparatorIncludeWindowsBackslash(line);

                    TimeStampFileFilter f = new TimeStampFileFilter();

                    if (wildcard.StartsWith("+"))
                    {
                        f.Action = TimeStampFileFilterAction.Include;
                        f.Wildcard = wildcard.Substring(1);
                    }
                    else if (wildcard.StartsWith("-") || wildcard.StartsWith("!"))
                    {
                        f.Action = TimeStampFileFilterAction.Exclude;
                        f.Wildcard = wildcard.Substring(1);
                    }
                    else
                    {
                        f.Action = TimeStampFileFilterAction.Include;
                        f.Wildcard = wildcard;
                    }

                    if (f.Wildcard.EndsWith("/"))
                    {
                        f.Type = TimeStampFileFilterType.ForDir;
                        f.Wildcard = f.Wildcard.Substring(0, f.Wildcard.Length - 1);
                    }

                    f.Wildcard = f.Wildcard.Trim();

                    if (f.Wildcard._IsFilled())
                    {
                        this.Filters.Add(f);
                    }
                }
            }
        }

        public TimeStampConfig MergeConfig(TimeStampConfig? child)
        {
            TimeStampConfig ret = this._CloneDeep();

            // フィルタのマージ (ファイル名に関するフィルタのみ)
            ret.Filters = (child == null ? new TimeStampFileFilter[0] : child.Filters.ToArray()).Concat(this.Filters.Where(x => x.Type == TimeStampFileFilterType.ForFile)).ToList();

            // 設定のマージ
            if (child != null)
            {
                foreach (var kv in child.Settings)
                {
                    if (kv.Key.StartsWith("-") == false)
                    {
                        ret.Settings[kv.Key] = kv.Value;
                    }
                    else
                    {
                        ret.Settings.Remove(kv.Key.Substring(1));
                    }
                }
            }

            return ret;
        }

        public bool ProcessFilterForFile(FileSystemEntity file, FileMetadata meta)
        {
            if (ProcessFilterForFileName(file, meta) == false)
            {
                return false;
            }

            if (ProcessFilterForFileDate(file, meta) == false)
            {
                return false;
            }

            return true;
        }

        bool ProcessFilterForFileDate(FileSystemEntity file, FileMetadata meta)
        {
            int daysInSettings = this.Settings._GetOrDefault("maxages")._ToInt();

            if (daysInSettings <= 0)
            {
                return true;
            }

            var date = meta.LastWriteTime ?? DtOffsetZero;
            DateTimeOffset now = DtOffsetNow;

            double days = (now - date).TotalDays;

            if (days < daysInSettings)
            {
                return true;
            }

            return false;
        }

        bool ProcessFilterForFileName(FileSystemEntity file, FileMetadata meta)
        {
            foreach (var f in this.Filters.Where(x => x.Type == TimeStampFileFilterType.ForFile))
            {
                if (file.Name._WildcardMatch(f.Wildcard, true))
                {
                    return f.Action == TimeStampFileFilterAction.Include ? true : false;
                }
            }

            return true;
        }

        public bool ProcessFilterForDirectory(string name)
        {
            foreach (var f in this.Filters.Where(x => x.Type == TimeStampFileFilterType.ForDir))
            {
                if (name._WildcardMatch(f.Wildcard, true))
                {
                    return f.Action == TimeStampFileFilterAction.Include ? true : false;
                }
            }

            return true;
        }

        public List<FileSystemEntity> ProcessFilterForSubDirectoryList(IEnumerable<FileSystemEntity> subDirs)
        {
            var tmpList = subDirs.ToList();

            int maxYyyymmddDirs = this.Settings._GetOrDefault("MaxYYMMDDDirs")._ToInt();
            if (maxYyyymmddDirs >= 1)
            {
                var yyyymmddDirs = tmpList.Where(x => x.IsDirectory && x.IsCurrentOrParentDirectory == false && Str.TryParseYYMMDDDirName(x.Name, out _));
                yyyymmddDirs = yyyymmddDirs.OrderBy(x =>
                {
                    Str.TryParseYYMMDDDirName(x.Name, out var dt);
                    return dt;
                });

                tmpList = tmpList.Except(yyyymmddDirs).Concat(yyyymmddDirs.TakeLast(maxYyyymmddDirs)).ToList();
            }

            tmpList = tmpList.OrderBy(x => x.Name, StrCmpi).ToList();

            return tmpList;
        }

    }

    public FileSystem Fs { get; }

    public TimeStampDocsUtil(FileSystem fs)
    {
        try
        {
            this.Fs = fs;

            // Settings を読み込む
            this.SettingsHive = new HiveData<TimeStampDocsSetting>(Hive.SharedLocalConfigHive, $"TimeStampDocsSetting", null, HiveSyncPolicy.AutoReadWriteFile);
        }
        catch (Exception ex)
        {
            this._DisposeSafe(ex);
            throw;
        }
    }

    async Task<TimeStampConfig?> ParseConfigAsync(string fileName, CancellationToken cancel = default)
    {
        if (await this.Fs.IsFileExistsAsync(fileName, cancel) == false)
        {
            return null;
        }

        try
        {
            string body = await this.Fs.ReadStringFromFileAsync(fileName, maxSize: 1_000_000, cancel: cancel);

            TimeStampConfig config = new TimeStampConfig(body);

            return config;
        }
        catch (Exception ex)
        {
            ex._Error();

            return null;
        }
    }

    Once OnceFlag;

    public int TotalNumFiles = 0;

    public async Task DoAsync(string targetDirsList, CancellationToken cancel = default)
    {
        if (OnceFlag.IsFirstCall() == false)
        {
            throw new CoresLibException("OnceFlag.IsFirstCall() == false");
        }

        string uniqueId = Str.GenRandStr();

        string[] targetDirs = targetDirsList._Split(StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries, ";", ",");

        List<ProjectResult> projectResultsList = new List<ProjectResult>();

        // まず、指定されたディレクトリをどんどんとサブディレクトリに向かって走査する
        await this.Fs.DirectoryWalker.WalkDirectoriesAsync(targetDirs, async (di, list, c) =>
        {
            await Task.CompletedTask;

            try
            {
                if (this.TotalNumFiles >= this.Settings.MaxFilesTotal)
                {
                    // ファイル個数超過 (全体)
                    return false;
                }

                // このディレクトリ内に設定ファイルがあるか?
                string configPath = di.FileSystem.PathParser.Combine(di.FullPath, this.Settings.TimeStampConfigFilename);

                var config = await ParseConfigAsync(configPath, cancel); // これははエラーにならないはず

                if (config != null)
                {
                    ProjectResult result = new ProjectResult
                    {
                        RootDirPath = di.FullPath,
                        RootDirName = this.Fs.PathParser.GetFileName(di.FullPath),
                    };

                    result.SettingsFiles.Add(configPath);

                    await ProcessProjectDirAsync(config, di, list, cancel, di.FullPath, result);

                    projectResultsList.Add(result);

                    // config が発見されたので、これよりも下部のサブディリクトリの走査は行なわない
                    // (CoresNoSubDirException 例外を発生させれば、walker はより深いディレクトリへの走査を中止する)
                    throw new CoresNoSubDirException();
                }
            }
            catch (CoresNoSubDirException)
            {
                throw;
            }
            catch (Exception ex)
            {
                ex._Error();
            }

            return true;
        },
        exceptionHandler: async (di, ex, c) =>
        {
            await Task.CompletedTask;
            // ディレクトリ列挙の例外が発生したら、エラー出力する
            ex._Error();

            return true;
        },
        cancel: cancel);

        DateTimeOffset now = DtOffsetNow;

        string yyyymmdd = now.LocalDateTime._ToYymmddStr() + "-" + now.DateTime._ToHhmmssStr();

        StringWriter forLocalFullPathText = new StringWriter();
        forLocalFullPathText.NewLine = Str.NewLine_Str_Windows;

        StringWriter forZipText = new StringWriter();
        forZipText.NewLine = Str.NewLine_Str_Windows;

        // 署名対象電文の生成
        WriteTo(forZipText, projectResultsList, now, WriteFlags.None, uniqueId);
        string forZipTextBody = forZipText.ToString();

        // ローカル保存用ハッシュ表の作成
        WriteTo(forLocalFullPathText, projectResultsList, now, WriteFlags.FullPath, uniqueId);
        string forLocalFullPathTextBody = forLocalFullPathText.ToString();

        //forSignBody._Print();

        //return;

        //// テスト用に増幅
        //if (false)
        //{
        //    StringBuilder sb = new StringBuilder();
        //    for (int i = 0; i < 250; i++)
        //    {
        //        sb.Append(forZipTextBody);
        //    }
        //    forZipTextBody = sb.ToString();
        //}

        var zipDataList = FileUtil.CompressTextToZipFilesSplittedWithMinSize(forZipTextBody, this.Settings.SingleMailZipFileSize, $"timestamptxt-{yyyymmdd}-{this.Settings.OutputFilenamePrefix}", this.Settings.MailZipPassword, ".txt");

        Util.PutGitIgnoreFileOnDirectory(this.Settings.LogDir, FileFlags.AutoCreateDirectory);
        string entireLogsDirPath = this.Fs.PP.Combine(this.Settings.LogDir, $"{yyyymmdd}-{this.Settings.OutputFilenamePrefix}");

        await this.Fs.CreateDirectoryAsync(entireLogsDirPath, cancel: cancel);

        // ハッシュ表本文を出力 (フルパスが入っているもの)
        await this.Fs.WriteStringToFileAsync(this.Fs.PP.Combine(entireLogsDirPath, $"timestamptxt-fullpath-{yyyymmdd}-{this.Settings.OutputFilenamePrefix}.txt"), forLocalFullPathTextBody, FileFlags.AutoCreateDirectory | FileFlags.OnCreateSetCompressionFlag, writeBom: true);

        // ハッシュ表本文を出力 (フルパスが入っていないもの)
        await this.Fs.WriteStringToFileAsync(this.Fs.PP.Combine(entireLogsDirPath, $"timestamptxt-sametozip-{yyyymmdd}-{this.Settings.OutputFilenamePrefix}.txt"), forZipTextBody, FileFlags.AutoCreateDirectory | FileFlags.OnCreateSetCompressionFlag, writeBom: true);

        StringWriter mailLog = new StringWriter();
        mailLog.NewLine = Str.NewLine_Str_Windows;

        mailLog.WriteLine($"タイムスタンプ署名用 ZIP ファイルを添付したメールの送信ログ");
        mailLog.WriteLine();
        mailLog.WriteLine($"ハッシュ計算ジョブユニーク ID: {uniqueId}");
        mailLog.WriteLine();

        CoresLib.Report_SimpleResult = $"Projects: {projectResultsList.Count}, NumFiles: {TotalNumFiles._ToString3()}, ZIPs: {zipDataList.Count} (Total: {zipDataList.Sum(x => x.Length)._GetFileSizeStr()}), UID: {uniqueId}";

        Con.WriteLine($"Unique ID: {uniqueId}");

        for (int i = 0; i < zipDataList.Count; i++)
        {
            var zipData = zipDataList[i];

            string zipFnSimple = $"timestampzip-{yyyymmdd}-{this.Settings.OutputFilenamePrefix}-{(i + 1):D3}.zip";
            string zipFn = this.Fs.PP.Combine(entireLogsDirPath, zipFnSimple);
            string zipPwFn = zipFn + ".password.txt";

            // 分割された ZIP ファイル本体をファイルに保存
            //Dbg.Where(zipFn);
            await this.Fs.WriteDataToFileAsync(zipFn, zipData, flags: FileFlags.AutoCreateDirectory);

            await this.Fs.WriteStringToFileAsync(zipPwFn, this.Settings.MailZipPassword + "\r\n", flags: FileFlags.AutoCreateDirectory);

            // メールを送信
            string subject = $"{this.Settings.MailSubjectPrefix} {yyyymmdd}-{this.Settings.OutputFilenamePrefix} ({i + 1}/{zipDataList.Count}) - {zipData.Length._ToString3()} bytes";

            StringWriter mailText = new StringWriter();
            mailText.NewLine = Str.NewLine_Str_Windows;
            mailText.WriteLine($"タイムスタンプ署名用ハッシュ表が本メールに添付されて送信されております。");
            mailText.WriteLine();
            mailText.WriteLine($"送信日時: {DtOffsetNow._ToDtStr()}");
            mailText.WriteLine($"Subject: {subject}");
            mailText.WriteLine();
            mailText.WriteLine($"ハッシュ計算ジョブユニーク ID: {uniqueId}");
            mailText.WriteLine();
            mailText.WriteLine($"この ZIP ファイル: {i + 1} 個目 / {zipDataList.Count} 個中");
            mailText.WriteLine($"この ZIP ファイルのファイル名: {zipFnSimple}");
            mailText.WriteLine($"この ZIP ファイルのサイズ: {zipData.Length._ToString3()} bytes ({zipData.Length._GetFileSizeStr()})");
            mailText.WriteLine($"この ZIP ファイルの MD5 ハッシュ値: {Secure.HashMD5(zipData.Span)._GetHexString().ToLowerInvariant()}");
            mailText.WriteLine($"この ZIP ファイルの SHA256 ハッシュ値: {Secure.HashSHA256(zipData.Span)._GetHexString().ToLowerInvariant()}");
            mailText.WriteLine($"この ZIP ファイルの SHA512 ハッシュ値: {Secure.HashSHA512(zipData.Span)._GetHexString().ToLowerInvariant()}");
            mailText.WriteLine();
            mailText.WriteLine($"このメールを含めて、合計 {zipDataList.Count} 通のメールが送付されています。");
            mailText.WriteLine($"これらのメールにそれぞれ添付されている合計 {zipDataList.Count} 個の ZIP ファイルに分割されています。");
            mailText.WriteLine($"合計 ZIP ファイルサイズ: {zipDataList.Sum(x => x.Length)._ToString3()} bytes ({zipDataList.Sum(x => x.Length)._GetFileSizeStr()})");
            mailText.WriteLine();
            mailText.WriteLine($"これらの ZIP ファイルは、タイムスタンプ署名が施されることになります。");
            mailText.WriteLine($"しばらくすると、署名サービスにより、ZIP ファイルのハッシュ値 (上記) を記載した PDF ファイルにタイムスタンプ署名が施されたものが、返信メールとして送り返されてきます。");
            mailText.WriteLine("送り返されてきたタイムスタンプ署名が施されたメールは、このメール本体とともに、長期間保存しましょう。");
            mailText.WriteLine($"これにより、{DtOffsetNow._ToDtStr()} の時点で、ZIP ファイル内のハッシュ表に含まれているファイルが存在していたことが証明されます。");
            mailText.WriteLine("タイムスタンプ署名とは、そのようなものなのです。");
            mailText.WriteLine();
            mailText.WriteLine($"ZIP ファイルは、暗号化されています。パスワードは、システム管理者が管理しています。");
            mailText.WriteLine();
            mailText.WriteLine();
            mailText.WriteLine($"# デバッグ情報 (技術者向け): ({new EnvInfoSnapshot()._ObjectToJson(compact: true)})");
            mailText.WriteLine();

            SmtpConfig smtpConfig = this.Settings.SmtpSettings.ToSmtpConfig();

            SmtpBody mail = new SmtpBody(new MailAddress(this.Settings.MailFrom), new MailAddress(this.Settings.MailTo), subject,
                mailText.ToString());

            mail.AddAttachedFile(zipData.ToArray(), Consts.MimeTypes.Zip, zipFnSimple, null);

            mail.CcList.Add(new MailAddress(this.Settings.MailCc));

            mailLog.WriteLine($"========== メール ({i + 1}/{zipDataList.Count}) 通目 ==========");
            mailLog.WriteLine($"送信日時: {DtOffsetNow._ToDtStr(true)}");
            mailLog.WriteLine($"メールサーバー: {smtpConfig.SmtpServer}:{smtpConfig.SmtpPort}");
            mailLog.WriteLine($"From: {this.Settings.MailFrom}");
            mailLog.WriteLine($"To: {this.Settings.MailTo}");
            mailLog.WriteLine($"Cc: {this.Settings.MailCc}");
            mailLog.WriteLine($"Subject: {subject}");
            mailLog.WriteLine($"Body size: {mailText.ToString().Length._ToString3()} characters");
            mailLog.WriteLine("");
            mailLog.WriteLine("---- 本文 ここから ----");
            mailLog.WriteLine(mailText.ToString());
            mailLog.WriteLine("---- 本文 ここまで ----");
            mailLog.WriteLine("");

            await TaskUtil.RetryAsync(async () =>
            {
                try
                {
                    Con.WriteLine($"Sending email from {this.Settings.MailFrom} to {this.Settings.MailTo} (subject: {subject}, length = {mailText.ToString().Length._ToString3()} characters)");
                    await mail.SendAsync(smtpConfig, cancel);
                    Con.WriteLine($"Ok.");
                }
                catch (Exception ex)
                {
                    ex._Error();
                    throw;
                }
                return 0;
            },
            1000,
            this.Settings.MailTryCount,
            cancel,
            true);
        }

        mailLog.WriteLine("");

        // メールデータ出力
        await this.Fs.WriteStringToFileAsync(this.Fs.PP.Combine(entireLogsDirPath, $"maillog-{yyyymmdd}-{this.Settings.OutputFilenamePrefix}.txt"), mailLog.ToString(), FileFlags.AutoCreateDirectory | FileFlags.OnCreateSetCompressionFlag, writeBom: true);

        StringWriter settingsFileNameTxt = new StringWriter();
        settingsFileNameTxt.NewLine = Str.NewLine_Str_Windows;

        // プロジェクト単位でのログ出力
        foreach (var proj in projectResultsList)
        {
            if (proj.SettingsFiles.Count() >= 1)
            {
                settingsFileNameTxt.WriteLine($"■ {proj.SettingsFiles.First()}");

                foreach (var settingsFile in proj.SettingsFiles)
                {
                    settingsFileNameTxt.WriteLine($"{settingsFile}");
                }
            }

            settingsFileNameTxt.WriteLine();

            StringWriter forProjLogFull = new StringWriter();
            forProjLogFull.NewLine = Str.NewLine_Str_Windows;

            StringWriter forProjLogSameToZip = new StringWriter();
            forProjLogSameToZip.NewLine = Str.NewLine_Str_Windows;

            WriteTo(forProjLogFull, projectResultsList, now, WriteFlags.FullPath, uniqueId, proj);
            WriteTo(forProjLogSameToZip, projectResultsList, now, WriteFlags.None, uniqueId, proj);

            string logBaseDir = this.Fs.PP.Combine(proj.RootDirPath, this.Settings.TimeStampProjectLogDirName);

            try
            {
                // ディレクトリを隠しディレクトリにする
                var dirMeta = await this.Fs.GetDirectoryMetadataAsync(logBaseDir, cancel: cancel);
                dirMeta.Attributes = dirMeta.Attributes.BitAdd(FileAttributes.Hidden);
                await this.Fs.SetDirectoryMetadataAsync(logBaseDir, dirMeta, cancel: cancel);
            }
            catch { }

            Util.PutGitIgnoreFileOnDirectory(logBaseDir, FileFlags.AutoCreateDirectory);

            string logDir = this.Fs.PP.Combine(logBaseDir, $"{yyyymmdd}-{this.Settings.OutputFilenamePrefix}");

            // ハッシュ表本文を出力 (フルパスが入っているもの)
            await this.Fs.WriteStringToFileAsync(this.Fs.PP.Combine(logDir, $"timestamptxt-fullpath-{yyyymmdd}-{this.Settings.OutputFilenamePrefix}.txt"), forProjLogFull.ToString(), FileFlags.AutoCreateDirectory | FileFlags.OnCreateSetCompressionFlag, writeBom: true);

            // ハッシュ表本文を出力 (フルパスが入っていないもの)
            await this.Fs.WriteStringToFileAsync(this.Fs.PP.Combine(logDir, $"timestamptxt-sametozip-{yyyymmdd}-{this.Settings.OutputFilenamePrefix}.txt"), forProjLogSameToZip.ToString(), FileFlags.AutoCreateDirectory | FileFlags.OnCreateSetCompressionFlag, writeBom: true);

            // メールデータ出力
            await this.Fs.WriteStringToFileAsync(this.Fs.PP.Combine(logDir, $"maillog-{yyyymmdd}-{this.Settings.OutputFilenamePrefix}.txt"), mailLog.ToString(), FileFlags.AutoCreateDirectory | FileFlags.OnCreateSetCompressionFlag, writeBom: true);
        }

        // 設定ファイル一覧出力
        await this.Fs.WriteStringToFileAsync(this.Fs.PP.Combine(entireLogsDirPath, $"configfilelist-{yyyymmdd}-{this.Settings.OutputFilenamePrefix}.txt"), settingsFileNameTxt.ToString(), FileFlags.AutoCreateDirectory | FileFlags.OnCreateSetCompressionFlag, writeBom: true);
    }

    void WriteTo(TextWriter allText, List<ProjectResult> projectResultsList, DateTimeOffset now, WriteFlags flags, string uniqueId, ProjectResult? targetProj = null)
    {
        allText.WriteLine($"# タイムスタンプ署名用の署名対象ファイルハッシュ表");
        allText.WriteLine("# ");
        allText.WriteLine($"# 作成日時: {now._ToDtStr(true)}");
        allText.WriteLine($"# 作成コンピュータ: {Env.DnsFqdnHostName}");
        allText.WriteLine($"# ハッシュ計算ジョブユニーク ID: {uniqueId}");
        allText.WriteLine();
        allText.WriteLine();

        for (int i = 0; i < projectResultsList.Count; i++)
        {
            var proj = projectResultsList[i];

            if (targetProj == null || proj == targetProj)
            {
                allText.WriteLine($"★★ プロジェクト ({i + 1}/{projectResultsList.Count}) 『{(flags.Bit(WriteFlags.FullPath) ? proj.RootDirPath : proj.RootDirName)}』");

                proj.WriteTo(allText, now, flags);
            }
        }

        allText.WriteLine();
        allText.WriteLine();
        allText.WriteLine("# 以上");
        allText.WriteLine();
        allText.WriteLine($"# デバッグ情報 (技術者向け): ({new EnvInfoSnapshot()._ObjectToJson(compact: true)})");
        allText.WriteLine($"");
    }

    // config が設定されている 1 つのディレクトリ (ルートから走査していって出現する最初の config が存在する深さ) の処理
    async Task ProcessProjectDirAsync(TimeStampConfig config, DirectoryPathInfo dir, FileSystemEntity[] list, CancellationToken cancel, string rootDirPath, ProjectResult result)
    {
        await ProcessParentManagedSubDirAsync(config, dir.FullPath, cancel, rootDirPath, result);
    }

    async Task ProcessParentManagedSubDirAsync(TimeStampConfig config, string dirPath, CancellationToken cancel, string parentDirPathForFilter, ProjectResult result)
    {
        // フルパスから相対パスに変換
        string relativeDirPath = this.Fs.PathParser.GetRelativeDirectoryName(dirPath, parentDirPathForFilter);

        // 相対パスをスラッシュ区切りに変換
        relativeDirPath = PPMac.NormalizeDirectorySeparatorIncludeWindowsBackslash(relativeDirPath);

        if (relativeDirPath.EndsWith("/") == false) relativeDirPath = relativeDirPath + "/";

        var dirEntityList = await this.Fs.EnumDirectoryAsync(dirPath, false, cancel: cancel);

        DirData? dirData = null;

        // ファイルの処理
        foreach (var fileEntry in dirEntityList.Where(x => x.IsFile).OrderBy(x => x.Name, this.Fs.PathParser.PathStringComparer).Where(x => x.Name._IsDiffi(this.Settings.TimeStampConfigFilename)))
        {
            var fileMetaData = await Fs.GetFileMetadataAsync(fileEntry.FullPath, cancel: cancel);

            if (config.ProcessFilterForFile(fileEntry, fileMetaData))
            {
                try
                {
                    if (result.NumFiles_Inc >= this.Settings.MaxFilesInProject)
                    {
                        // ファイル個数超過 (プロジェクト単位)
                        $"Error: NumFiles ({result.NumFiles_Inc}) >= MaxFilesInProject ({Settings.MaxFilesInProject}). Project: \"{result.RootDirPath}\", FilePath: \"{fileEntry.FullPath}\""._Error();
                        return;
                    }

                    if (this.TotalNumFiles >= this.Settings.MaxFilesTotal)
                    {
                        // ファイル個数超過 (全体)
                        $"Error: TotalNumFiles ({TotalNumFiles}) >= MaxFilesTotal ({Settings.MaxFilesTotal}). Project: \"{result.RootDirPath}\", FilePath: \"{fileEntry.FullPath}\""._Error();
                        return;
                    }

                    //// フルパスから相対パスに変換
                    string relativeFilePath = this.Fs.PathParser.GetRelativeFileName(fileEntry.FullPath, parentDirPathForFilter);

                    //// 相対パスをスラッシュ区切りに変換
                    relativeFilePath = PPMac.NormalizeDirectorySeparatorIncludeWindowsBackslash(relativeFilePath);

                    if (dirData == null)
                    {
                        dirData = new DirData
                        {
                            FullPath = dirPath,
                            RelativePath = relativeDirPath,
                        };

                        result.DirList.Add(dirData);
                    }

                    var fileData = new FileData
                    {
                        DateCreated = fileMetaData.CreationTime.GetValueOrDefault(DtOffsetZero),
                        DateLastWrite = fileMetaData.LastWriteTime.GetValueOrDefault(DtOffsetZero),
                        FullPath = fileEntry.FullPath,
                        RelativePath = relativeFilePath,
                    };

                    RefLong size = new();
                    fileData.Md5 = (await Fs.CalcFileHashAsync(fileEntry.FullPath, MD5.Create(), flags: FileFlags.BackupMode, cancel: cancel, totalReadSize: size))._GetHexString();
                    fileData.Sha512 = (await Fs.CalcFileHashAsync(fileEntry.FullPath, SHA512.Create(), flags: FileFlags.BackupMode, cancel: cancel))._GetHexString();
                    fileData.FileSize = size;

                    dirData.FileList.Add(fileData);
                    this.TotalNumFiles++;
                    result.NumFiles_Inc++;
                }
                catch (Exception ex)
                {
                    ex._Error();
                }
            }
        }

        // サブディレクトリの処理
        var subDirList = dirEntityList.Where(x => x.IsDirectory && x.IsSymbolicLink == false && x.IsCurrentOrParentDirectory == false).OrderBy(x => x.Name, this.Fs.PathParser.PathStringComparer).ToList();

        subDirList = config.ProcessFilterForSubDirectoryList(subDirList).OrderBy(x => x.Name, this.Fs.PathParser.PathStringComparer).ToList();

        foreach (var subDirEntry in subDirList)
        {
            try
            {
                if (result.NumFiles_Inc >= this.Settings.MaxFilesInProject)
                {
                    // ファイル個数超過 (プロジェクト単位)
                    return;
                }

                if (this.TotalNumFiles >= this.Settings.MaxFilesTotal)
                {
                    // ファイル個数超過 (全体)
                    return;
                }

                if (subDirEntry.Name._IsDiffi(this.Settings.TimeStampProjectLogDirName))
                {
                    if (config.ProcessFilterForDirectory(subDirEntry.Name))
                    {
                        // このサブディレクトリに独自の Config はあるか?
                        string configPath = this.Fs.PathParser.Combine(subDirEntry.FullPath, this.Settings.TimeStampConfigFilename);
                        var subDirConfig = await this.ParseConfigAsync(configPath, cancel);

                        if (subDirConfig != null)
                        {
                            // 独自の Config がある場合は、一階層上の Config を承継した上で必要に応じて上書きをした新たな Config を作る
                            result.SettingsFiles.Add(configPath);

                            subDirConfig = config.MergeConfig(subDirConfig);
                        }
                        else
                        {
                            // 独自の Config がなければ、親 Config のうちディレクトリに関する設定を除いたフィルタをそのまま利用する
                            subDirConfig = config.MergeConfig(null);
                        }

                        // 再帰
                        await ProcessParentManagedSubDirAsync(subDirConfig, subDirEntry.FullPath, cancel, parentDirPathForFilter, result);
                    }
                }
            }
            catch (Exception ex)
            {
                ex._Error();
            }
        }
    }

    protected override async Task CleanupImplAsync(Exception? ex)
    {
        try
        {
            await Task.CompletedTask;

            this.SettingsHive._DisposeSafe();
        }
        finally
        {
            await base.CleanupImplAsync(ex);
        }
    }

}


public class PoderosaSettingsContents
{
    public string? HostName;
    public int Port;
    public string? Method;
    public string? Username;
    public string? Password;

    public PoderosaSettingsContents() { }

    public PoderosaSettingsContents(string settingsBody)
    {
        var xml = new XmlDocument();
        xml.LoadXml(settingsBody);

        var shortcut = xml.SelectSingleNode("poderosa-shortcut");
        if (shortcut == null) throw new CoresLibException("Not a poderosa-shortcut file.");

        var ver = shortcut.Attributes!["version"]!.Value;
        if (ver != "4.0") throw new CoresLibException("ver != '4.0'");

        var sshParam = shortcut.SelectSingleNode("Poderosa.Protocols.SSHLoginParameter");
        if (sshParam == null) throw new CoresLibException("Not a SSH shortcut file.");

        this.HostName = sshParam!.Attributes!["destination"]?.Value._NonNull();
        this.Port = sshParam.Attributes["port"]?.Value._NonNull()._ToInt() ?? 0;
        if (this.Port == 0) this.Port = Consts.Ports.Ssh;

        this.Username = sshParam.Attributes["account"]?.Value._NonNull();
        this.Password = sshParam.Attributes["passphrase"]?.Value._NonNull();
    }

    public SecureShellClientSettings GetSshClientSettings(int connectTimeoutMsecs = 0, int commTimeoutMsecs = 0)
    {
        return new SecureShellClientSettings(this.HostName._NullCheck(), this.Port, this.Username._NullCheck(), this.Password._NonNull(), connectTimeoutMsecs, commTimeoutMsecs);
    }

    public SecureShellClient CreateSshClient(int connectTimeoutMsecs = 0, int commTimeoutMsecs = 0)
    {
        return new SecureShellClient(this.GetSshClientSettings(connectTimeoutMsecs, commTimeoutMsecs));
    }
}

public static class PoderosaSettingsContentsHelper
{
    public static async Task<PoderosaSettingsContents> ReadPoderosaFileAsync(this FileSystem fs, string fileName, CancellationToken cancel = default)
        => new PoderosaSettingsContents(await fs.ReadStringFromFileAsync(fileName, cancel: cancel));

    public static PoderosaSettingsContents ReadPoderosaFile(this FileSystem fs, string fileName, CancellationToken cancel = default)
        => ReadPoderosaFileAsync(fs, fileName, cancel)._GetResult();
}

public class BatchExecSshItem
{
    public string Host = "";
    public int Port = Consts.Ports.Ssh;
    public string Username = "root";
    public string Password = "";
    public string CommandLine = "";
}

public class ExpandIncludesSettings
{
    public int MaxIncludes { init; get; } = 16;
    public CachedDownloaderSettings DownloaderSettings { init; get; } = new CachedDownloaderSettings();
    public bool AllowIncludeLocalFileByLocalFile { init; get; } = true;
    public bool AllowIncludeLocalFileByNetworkFile { init; get; } = false;
    public Encoding? Encoding { init; get; } = null;
    public int MaxReadFileSize { init; get; } = Consts.MaxLens.MaxCachedFileDownloadSizeDefault;
}

// 色々なおまけユーティリティ
public static partial class MiscUtil
{


    // 取得したいフレームIDと、それを格納するためのキーを紐づける辞書
    private static readonly Dictionary<string, string> FrameToKeyMap = new Dictionary<string, string>
        {
            { "TALB", "album" },
            { "TPE2", "album_artist" },
            { "TIT2", "title" },
            { "TPE1", "artist" },
            { "TRCK", "track" }
        };

    public static MediaMetaData? ReadMP3MetaData(ReadOnlySpan<byte> data, CancellationToken cancel = default)
    {
        MemoryStream ms = new MemoryStream();
        ms.Write(data);

        return ReadMP3MetaDataAsync(ms, cancel)._GetResult();
    }

    public static async Task<MediaMetaData?> ReadMP3MetaDataAsync(string filePath, CancellationToken cancel = default, FileSystem? fs = null)
    {
        fs ??= Lfs;

        await using (var file = await fs.OpenAsync(filePath, cancel: cancel))
        {
            return await ReadMP3MetaDataAsync(file.GetStream(true), cancel);
        }
    }

    public static async Task<MediaMetaData?> ReadMP3MetaDataAsync(Stream fs, CancellationToken cancel = default)
    {
        fs._SeekToBegin();

        // 取得するメタデータを格納するディクショナリ
        Dictionary<string, string> metadata = new Dictionary<string, string>
            {
                { "album",         "" },
                { "album_artist",  "" },
                { "title",         "" },
                { "artist",        "" },
                { "track",         "" }
            };

        // ID3v2 タグをパースしてメタデータを取得する
        bool success = await ParseID3v2TagAsync(fs, metadata);

        // ID3v2で取得できなかったものがある場合、ID3v1 (TAG) を最後にチェックする
        if (!success || IsAnyFieldEmpty(metadata))
        {
            await ParseID3v1TagAsync(fs, metadata);
        }

        MediaMetaData meta = new MediaMetaData();

        meta.Album = metadata["album"];
        meta.AlbumArtist = metadata["album_artist"];
        meta.Title = metadata["title"];
        meta.Artist = metadata["artist"];
        string trackStr = metadata["track"];

        if (trackStr._GetKeyAndValueExact(out var currentTrackStr, out var totalTrackStr, "/"))
        {
            meta.Track = currentTrackStr._ToInt();
            meta.TrackTotal = totalTrackStr._ToInt();
        }
        else
        {
            meta.Track = trackStr._ToInt();
        }

        if (meta.Album._IsFilled() || meta.AlbumArtist._IsFilled() || meta.Title._IsFilled() || meta.Artist._IsFilled())
        {
            return meta;
        }
        else
        {
            return null;
        }
    }

    /// <summary>
    /// ID3v2 タグをパースしてメタデータをディクショナリに格納する。
    /// </summary>
    /// <param name="fs">ファイルストリーム</param>
    /// <param name="metadata">メタデータを格納するディクショナリ</param>
    /// <returns>ID3v2 タグが存在してパースが行われた場合 true、無い場合やエラーの場合は false</returns>
    private static async Task<bool> ParseID3v2TagAsync(Stream fs, Dictionary<string, string> metadata, CancellationToken cancel = default)
    {
        // MP3 ファイルの先頭 10 バイトが ID3v2 ヘッダ
        byte[] header = new byte[10];
        if (await fs.ReadAsync(header, 0, 10, cancel) < 10)
        {
            // ファイルサイズが小さすぎる
            return false;
        }

        // "ID3" かどうかをチェック
        if (header[0] != 'I' || header[1] != 'D' || header[2] != '3')
        {
            // ID3v2 ではない
            return false;
        }

        // バージョン
        byte version = header[3];    // 例: 3 => ID3v2.3, 4 => ID3v2.4
        byte revision = header[4];   // 副バージョン
                                     // フラグ (header[5]) は今回あまり使わない

        // タグサイズ (4 バイト SyncSafe Integer)
        // 7ビットずつ使う合計28ビットがタグ全体のサイズ(ヘッダ除く)
        int tagSize = (header[6] & 0x7F) << 21
                    | (header[7] & 0x7F) << 14
                    | (header[8] & 0x7F) << 7
                    | (header[9] & 0x7F);

        // タグの末尾位置 (先頭10バイト + タグサイズ)
        long tagEnd = fs.Position + tagSize;

        // ID3v2.3 または ID3v2.4 を想定してフレームを順次読み込む
        while (fs.Position < tagEnd)
        {
            // フレームヘッダは 10 バイト (ID3v2.3/2.4 共通)
            // [0..3]: Frame ID (ASCII 4文字)
            // [4..7]: Size (ID3v2.3なら通常整数, ID3v2.4ならSyncSafeだが多くの場合通常でOKとする)
            // [8..9]: Flags
            byte[] frameHeader = new byte[10];
            int read = await fs.ReadAsync(frameHeader, 0, 10, cancel);
            if (read < 10)
            {
                // フレームヘッダが読み取れなかった -> タグのパディング域など
                break;
            }

            // フレームID (ASCII)
            string frameID = Encoding.ASCII.GetString(frameHeader, 0, 4);

            // フレームサイズ
            // ID3v2.3 の場合は通常の 32bit 整数
            // ID3v2.4 の場合は sync-safe (ただし多くのファイルはID3v2.3が多い)
            int frameSize;
            if (version == 4)
            {
                // ID3v2.4 の sync-safe 取得
                frameSize = (frameHeader[4] & 0x7F) << 21
                          | (frameHeader[5] & 0x7F) << 14
                          | (frameHeader[6] & 0x7F) << 7
                          | (frameHeader[7] & 0x7F);
            }
            else
            {
                // ID3v2.3 の通常整数
                frameSize = (frameHeader[4] << 24)
                          | (frameHeader[5] << 16)
                          | (frameHeader[6] << 8)
                          | (frameHeader[7]);
            }

            // フレームサイズが 0 以下なら異常、もしくはタグ終了
            if (frameSize <= 0)
            {
                // パディングや異常フレームなので次へ
                // （通常はフレームサイズが0の場合は読み飛ばし）
                break;
            }

            // フレームデータ領域を読み込む
            byte[] frameData = new byte[frameSize];
            int frameDataRead = await fs.ReadAsync(frameData, 0, frameSize, cancel);
            if (frameDataRead < frameSize)
            {
                // 途中で終わった -> タグがおかしい
                break;
            }

            // 目的のフレーム (TALB, TPE2, TIT2, TPE1, TRCK) のみを対象にパース
            if (FrameToKeyMap.ContainsKey(frameID))
            {
                string encodingType = "";
                // frameData[0] がテキストのエンコーディングを表す
                // 0x00: ISO-8859-1
                // 0x01: UTF-16 (with BOM)
                // 0x02: UTF-16BE
                // 0x03: UTF-8

                switch (frameData[0])
                {
                    case 0x00:
                        encodingType = "shift_jis";
                        break;
                    case 0x01:
                        // UTF-16 with BOM として扱う
                        encodingType = "UTF-16";
                        break;
                    case 0x02:
                        // UTF-16 BE (BOMなし) は読み取りが少し面倒なので簡易的にはUTF-16にしてしまう場合も
                        encodingType = "UTF-16BE";
                        break;
                    case 0x03:
                        encodingType = "UTF-8";
                        break;
                    default:
                        // 不明なエンコーディングは ISO-8859-1 として扱う
                        encodingType = "ISO-8859-1";
                        break;
                }

                // 先頭1バイト(エンコーディング情報)を除いたデータを文字列化する
                byte[] textData = new byte[frameSize - 1];
                Array.Copy(frameData, 1, textData, 0, frameSize - 1);

                string value = DecodeTextData(textData, encodingType);
                metadata[FrameToKeyMap[frameID]] = value;
            }

            // タグの終わり近くで何か処理したい場合や、特定条件で break したい場合はここで可能
            // 今回は最後までフレームを読み取る
        }

        return true;
    }

    /// <summary>
    /// フレームデータをエンコーディングに基づき文字列化する。
    /// </summary>
    private static string DecodeTextData(byte[] data, string encodingType)
    {
        try
        {
            Encoding encoding = Str.ShiftJisEncoding;

            switch (encodingType)
            {
                case "shift_jis":
                    encoding = Str.ShiftJisEncoding;
                    break;

                case "UTF-16":
                    encoding = Encoding.Unicode;
                    break;

                case "UTF-16BE":
                    encoding = Encoding.BigEndianUnicode;
                    break;

                case "UTF-8":
                    encoding = Encoding.UTF8;
                    break;

                default:
                    encoding = Str.ShiftJisEncoding;
                    break;
            }

            string ret = Str.DecodeString(data, encoding, out _, true);

            return ret;
        }
        catch
        {
            // 予期しないエンコーディングエラーが起きた場合、ASCII fallback
            return Encoding.ASCII.GetString(data).TrimEnd('\0');
        }
    }

    /// <summary>
    /// ID3v1 (TAG) をパースしてメタデータが埋まっていない項目を補完する。
    /// </summary>
    /// <param name="fs">ファイルストリーム</param>
    /// <param name="metadata">メタデータ</param>
    private static async Task ParseID3v1TagAsync(Stream fs, Dictionary<string, string> metadata)
    {
        if (fs.Length < 128) return; // ID3v1が格納できるサイズ以下

        // ファイル末尾128バイトがID3v1
        fs.Seek(-128, SeekOrigin.End);

        byte[] tag = new byte[128];
        if (await fs.ReadAsync(tag, 0, 128) < 128)
            return;

        // "TAG" かどうか
        if (tag[0] == 'T' && tag[1] == 'A' && tag[2] == 'G')
        {
            // 順に[3..32]: Title (30 bytes), [33..62]: Artist (30 bytes), [63..92]: Album (30 bytes),
            // [93..96]: Year (4 bytes), [97..126]: Comment (30 bytes), [127]: Genre
            // Track番号はID3v1.1の仕様でCommentの最後1バイトを使うが、混在があるので注意

            var data = tag.AsMemory();
            var defaultEncoding = Str.ShiftJisEncoding;
            string title = Str.DecodeStringAutoDetect(data.Slice(3, 30).Span, out _, true).TrimEnd();
            string artist = Str.DecodeStringAutoDetect(data.Slice(33, 30).Span, out _, true).TrimEnd();
            string album = Str.DecodeStringAutoDetect(data.Slice(63, 30).Span, out _, true).TrimEnd();
            // 年やコメントは省略

            // Track (ID3v1.1の場合: コメントの29バイト目が0で 30バイト目がトラック番号)
            byte track = 0;
            // コメント領域
            byte[] comment = new byte[30];
            Array.Copy(tag, 97, comment, 0, 30);
            // ID3v1.1 形式かの簡易判定
            if (comment[28] == 0 && comment[29] != 0)
            {
                // トラック番号を取得
                track = comment[29];
            }

            // album_artist は ID3v1 には無いので設定できない
            if (string.IsNullOrEmpty(metadata["title"]) && !string.IsNullOrEmpty(title))
            {
                metadata["title"] = title;
            }
            if (string.IsNullOrEmpty(metadata["artist"]) && !string.IsNullOrEmpty(artist))
            {
                metadata["artist"] = artist;
            }
            if (string.IsNullOrEmpty(metadata["album"]) && !string.IsNullOrEmpty(album))
            {
                metadata["album"] = album;
            }
            if (string.IsNullOrEmpty(metadata["track"]) && track > 0)
            {
                metadata["track"] = track.ToString();
            }
        }
    }

    /// <summary>
    /// メタデータ辞書のいずれかが空かどうかをチェックする。
    /// </summary>
    private static bool IsAnyFieldEmpty(Dictionary<string, string> metadata)
    {
        foreach (var key in metadata.Keys)
        {
            if (string.IsNullOrEmpty(metadata[key]))
                return true;
        }
        return false;
    }

    public static async Task HttpFileSpiderAsync(string baseUrl, FileDownloadOption? options = null, CancellationToken cancel = default)
    {
        options ??= new FileDownloadOption();

        List<Task> tasksList = new List<Task>();

        await using var cancelWatcher = new CancelWatcher(cancel);

        Memory<byte> tmpbuf = new byte[options.BufferSize];

        for (int i = 0; i < options.MaxConcurrentThreads; i++)
        {
            var task = TaskUtil.StartAsyncTaskAsync(async () =>
            {
                while (true)
                {
                    try
                    {
                        await PerformUrlAsync(baseUrl, cancelWatcher);
                    }
                    catch (Exception ex)
                    {
                        ex._Error();
                    }

                    await Task.Delay(100, cancelWatcher);

                    // 1 つの指定された URL へアクセスする。アクセス先の URL からファイルを取得する。ファイルの内容が html である場合は、内容をパースしてクロールする。
                    // ファイルの内容が html でない場合は、ファイル本文をすべてダウンロードする。
                    async Task PerformUrlAsync(string url, CancellationToken cancel = default)
                    {
                        url._Print();

                        await using var http = new WebApi(options.WebApiOptions);

                        try
                        {
                            await using var webret = await http.HttpSendRecvDataAsync(new WebSendRecvRequest(WebMethods.GET, url, cancel));

                            if (webret.DownloadContentType.StartsWith("text/html", StringComparison.OrdinalIgnoreCase))
                            {
                                var htmlData = await webret.DownloadStream._ReadToEndAsync(1_000_000, cancel);
                                string htmlStr = htmlData._GetString(untilNullByte: true);
                                var html = htmlStr._ParseHtml();
                                var children = html.DocumentNode.GetAllChildren();
                                var linkTargets = children.Where(x => x.Name._IsSamei("a")).Select(x => x.GetAttributeValue("href", "")).Where(x => x._IsFilled() && x._InStri("?") == false && x._InStri("#") == false).Distinct()._Shuffle();

                                await webret._DisposeSafeAsync();
                                await http._DisposeSafeAsync();

                                foreach (var linkTarget in linkTargets)
                                {
                                    string subUrl = url._CombineUrl(linkTarget).ToString();

                                    if (subUrl.StartsWith(url, StringComparison.OrdinalIgnoreCase) && subUrl.Length > url.Length)
                                    {
                                        await PerformUrlAsync(subUrl, cancel);
                                    }
                                }
                            }
                            else
                            {
                                var st = webret.DownloadStream;
                                while (true)
                                {
                                    if (await st.ReadAsync(tmpbuf, cancelWatcher) <= 0)
                                    {
                                        break;
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            ex._Error();
                        }
                    }
                }
            });

            tasksList.Add(task);
        }

        Con.ReadLine("Enter key to abort>");

        cancelWatcher.Cancel();

        foreach (var task in tasksList)
        {
            await task._TryWaitAsync();
        }
    }

    public static async Task ValidateIfMyLocalClockCorrectAsync(CancellationToken cancel = default)
    {
        var res = await CompareLocalClockToInternetServersClockAsync(cancel: cancel);

        res.ThrowIfException();
    }

    public static async Task<OkOrExeption> CompareLocalClockToInternetServersClockAsync(TimeSpan? allowDiff = null, bool ignoreInternetError = false, int commTimeoutMsecs = 10 * 1000, RefBool? internetCommError = null, Func<Task<DateTimeOffset>>? getLocalDtProc = null, CancellationToken cancel = default)
    {
        internetCommError ??= new RefBool();
        allowDiff ??= TimeSpan.FromSeconds(15); // 15 秒以内のずれを許容

        if (getLocalDtProc == null)
        {
            getLocalDtProc = () => DateTimeOffset.Now._TR();
        }

        internetCommError.Set(false);

        List<bool> disableProxyFlags = new();

        disableProxyFlags.Add(false);

        if (Environment.GetEnvironmentVariable("http_proxy")._IsFilled() || Environment.GetEnvironmentVariable("https_proxy")._IsFilled())
        {
            disableProxyFlags.Add(true);
        }

        var urlsList = @"
https://www.google.com/
http://www.google.co.jp/
https://www.yahoo.com/
http://www.yahoo.co.jp/
https://www.yahoo.co.jp/
http://www.youtube.com/
https://www.msn.com/
http://www.msn.co.jp/
https://www.facebook.com/
https://www.twitter.com/
http://www.apple.com/
https://www.apple.com/
"._Split(StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries, "\r", "\n", " ", "\t", "　", ",", ";").Distinct(StrCmpi);

        // A: URL
        // B: disableProxy フラグ
        List<Pair2<string, bool>> urlsList2 = new List<Pair2<string, bool>>();

        foreach (var urls in urlsList)
        {
            foreach (var disableProxyFlag in disableProxyFlags)
            {
                urlsList2.Add(new(urls, disableProxyFlag));
            }
        }

        // A: サーバーの時刻
        // B: ローカル時刻
        List<Pair3<DateTimeOffset, DateTimeOffset, string>> resultsList = new();

        await TaskUtil.ForEachExAsync(urlsList2, async (url, c) =>
        {
            try
            {
                var dt = await GetCurrentDateTimeFromWebSever(url.A, commTimeoutMsecs, url.B, c);

                var now = await getLocalDtProc();

                if (dt._IsZeroDateTime() == false)
                {
                    lock (resultsList)
                    {
                        resultsList.Add(new(dt, now.ToLocalTime(), url.A));
                    }
                }
            }
            catch { }
        },
        8,
        flags: ForEachExAsyncFlags.None,
        cancel: cancel);

        int okCount = resultsList.Where(x => x.A._IsZeroDateTime() == false).Count();

        //resultsList._PrintAsJson();

        if (okCount == 0)
        {
            internetCommError.Set(true);

            if (ignoreInternetError == false)
            {
                return new CoresException($"Check Local Clock: Failed to connect to any Internet servers.");
            }
            else
            {
                return new OkOrExeption();
            }
        }

        foreach (var item in resultsList.Where(x => x.A._IsZeroDateTime() == false))
        {
            var diff = (item.B - item.A);

            diff = TimeSpan.FromTicks(Math.Abs(diff.Ticks)); // 絶対値

            if (diff <= allowDiff)
            {
                // OK
                return new OkOrExeption();
            }
        }

        var nearestResult = resultsList.Where(x => x.A._IsZeroDateTime() == false).OrderBy(x => Math.Abs((x.B - x.A).Ticks)).First();

        // Error
        return new CoresException($"Local clock is different to Internet clock. Local = {nearestResult.B.ToLocalTime()._ToDtStr(withMSsecs: true)}, Internet = {nearestResult.A.ToLocalTime()._ToDtStr(withMSsecs: true)} (from {nearestResult.C})");
    }

    static async Task<DateTimeOffset> GetCurrentDateTimeFromWebSever(string url, int timeoutMsecs = 10 * 1000, bool disableProxy = false, CancellationToken cancel = default)
    {
        await using var web = new WebApi(new WebApiOptions(new WebApiSettings { SslAcceptAnyCerts = true, AllowAutoRedirect = false, UseProxy = !disableProxy, DoNotThrowHttpResultError = true, MaxRecvSize = 1_000_000, Timeout = timeoutMsecs }, doNotUseTcpStack: true));

        var ret = await web.SimpleQueryAsync(WebMethods.HEAD, url, cancel);

        return (ret.Headers.Date ?? Util.ZeroDateTimeOffsetValue).ToLocalTime();
    }

    // Web server health check
    public static async Task<OkOrExeption> HttpHealthCheckAsync(string url, int numTry = 3, int timeoutMsecs = 5 * 1000, CancellationToken cancel = default, WebApiOptions? options = null)
    {
        options ??= new WebApiOptions(new WebApiSettings { Timeout = timeoutMsecs, SslAcceptAnyCerts = true }, doNotUseTcpStack: true);

        try
        {
            await RetryHelper.RunAsync(async () =>
            {
                await using WebApi api = new WebApi(options);

                await api.SimpleQueryAsync(WebMethods.GET, url, cancel);
            },
            0, numTry, cancel, false, true);

            return new OkOrExeption();
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    // ファイルの #include を展開する
    public static async Task<string> ExpandIncludesToStrAsync(string srcFileBody, FilePath? srcFilePathIfPhysicalFile = null, ExpandIncludesSettings? settings = null, CancellationToken cancel = default, string? newLineStr = null)
    {
        var tmp = await ExpandIncludesToListAsync(srcFileBody, srcFilePathIfPhysicalFile, settings, cancel);

        return tmp._LinesToStr(newLineStr);
    }
    public static async Task<List<string>> ExpandIncludesToListAsync(string srcFileBody, FilePath? srcFilePathIfPhysicalFile = null, ExpandIncludesSettings? settings = null, CancellationToken cancel = default)
    {
        settings ??= new ExpandIncludesSettings
        {
            DownloaderSettings = new CachedDownloaderSettings(flags: CachedDownloaderFlags.None,
                webOptions: new WebApiOptions(new WebApiSettings { Timeout = CoresConfig.DefaultExpandIncludesSettings.WebTimeoutMsecs, MaxRecvSize = Consts.MaxLens.MaxCachedFileDownloadSizeDefault }),
                maxTry: CoresConfig.DefaultExpandIncludesSettings.WebTryCount,
                retryInterval: CoresConfig.DefaultExpandIncludesSettings.WebRetryIntervalMsecs),
        };

        List<string> destLines = new List<string>();

        RefInt counter = new RefInt();

        await ProcessAsync(destLines, srcFileBody, srcFilePathIfPhysicalFile, counter);

        return destLines;

        async Task ProcessAsync(List<string> destLines, string srcFileBody, FilePath? srcFilePathIfPhysicalFile, RefInt includeCount)
        {
            includeCount.Increment();

            var fs = srcFilePathIfPhysicalFile?.FileSystem ?? null;

            var lines = srcFileBody._GetLines();

            foreach (var line in lines)
            {
                string tmp = line.Trim(' ', '　', '\t');
                bool consumed = false;

                if (tmp.StartsWith("#include", StringComparison.InvariantCultureIgnoreCase) && tmp.Length >= 9)
                {
                    if (tmp[8] == ' ' || tmp[8] == '\t' || tmp[8] == '　')
                    {
                        string name = tmp.Substring(9);
                        name = name._RemoveQuotation().Trim();

                        if (name._IsFilled())
                        {
                            if (includeCount > settings!.MaxIncludes)
                            {
                                throw new CoresLibException($"'{line}': includeCount ({includeCount}) > settings.MaxIncludes ({settings.MaxIncludes})");
                            }

                            if (name.StartsWith("http://", StrCmpi) || name.StartsWith("https://", StrCmpi))
                            {
                                // HTTP URL
                                var r = await CachedDownloader.DownloadAsync(name, cancel, settings!.DownloaderSettings);

                                var encoding = settings.Encoding;

                                string targetBody;

                                if (encoding == null)
                                    targetBody = Str.DecodeStringAutoDetect(r.Data.Span, out _);
                                else
                                    targetBody = Str.DecodeString(r.Data.Span, encoding, out _);

                                await ProcessAsync(destLines, targetBody, null, includeCount);
                            }
                            else
                            {
                                // ローカルファイル
                                if (fs == null)
                                {
                                    throw new CoresLibException($"'{line}': Current file is not a local file.");
                                }

                                if (settings!.AllowIncludeLocalFileByNetworkFile == false && srcFilePathIfPhysicalFile == null)
                                {
                                    throw new CoresLibException($"'{line}': AllowIncludeLocalFileByNetworkFile == false");
                                }

                                name = fs.PathParser.NormalizeDirectorySeparatorIncludeWindowsBackslash(name);

                                string targetFilePath = srcFilePathIfPhysicalFile!.GetParentDirectory().Combine(name);

                                var targetBody = await fs.ReadStringFromFileAsync(targetFilePath, settings.Encoding, settings.MaxReadFileSize, FileFlags.None, cancel: cancel);

                                await ProcessAsync(destLines, targetBody, targetFilePath, includeCount);
                            }

                            consumed = true;
                        }
                    }
                }

                if (consumed == false)
                {
                    destLines.Add(line);
                }
            }
        }
    }

    public static async Task<List<string>> ReadIncludesFileLinesAsync(string srcFilePathOrUrl, ExpandIncludesSettings? settings = null, CancellationToken cancel = default)
    {
        string srcBody;
        FilePath? srcFilePathIfPhysicalFile;

        if (srcFilePathOrUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || srcFilePathOrUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            // URL
            srcBody = "#include " + srcFilePathOrUrl + "\n";
            srcFilePathIfPhysicalFile = null;
        }
        else
        {
            // Local file
            srcBody = await Lfs.ReadStringFromFileAsync(srcFilePathOrUrl, cancel: cancel);
            srcFilePathIfPhysicalFile = srcFilePathOrUrl;
        }

        return await MiscUtil.ExpandIncludesToListAsync(srcBody, srcFilePathIfPhysicalFile, settings, cancel);
    }

    public static async Task ExpandIncludesFileAsync(string srcFilePathOrUrl, string destFilePath, ExpandIncludesSettings? settings = null, CancellationToken cancel = default, string? newLineStr = null, bool writeBom = false)
    {
        var list = await ReadIncludesFileLinesAsync(srcFilePathOrUrl, settings, cancel);

        byte[] data = list._LinesToStr(newLineStr)._GetBytes_UTF8(writeBom);

        await Lfs.WriteDataToFileAsync(destFilePath, data, FileFlags.WriteOnlyIfChanged, false, cancel, true);
    }

    // バイナリファイルの内容をバイナリで置換する
    public static async Task<KeyValueList<string, int>> ReplaceBinaryFileAsync(FilePath srcFilePath, FilePath? destFilePath, KeyValueList<string, string> oldNewList, FileFlags additionalFlags = FileFlags.None, byte fillByte = 0x0A, int bufferSize = Consts.Numbers.DefaultVeryLargeBufferSize, CancellationToken cancel = default)
    {
        if (destFilePath == null) destFilePath = srcFilePath;

        checked
        {
            List<Pair4<string, ReadOnlyMemory<byte>, ReadOnlyMemory<byte>, HashSet<long>>> list = new List<Pair4<string, ReadOnlyMemory<byte>, ReadOnlyMemory<byte>, HashSet<long>>>();

            // 引数チェック等
            foreach (var kv in oldNewList)
            {
                string oldStr = kv.Key;
                string newStr = kv.Value;

                Memory<byte> oldData = oldStr._GetHexOrString();
                Memory<byte> newData = newStr._GetHexOrString();

                if (oldData.Length == 0 && newData.Length == 0)
                {
                    continue;
                }

                if (oldData.Length != newData.Length)
                {
                    if (oldData.Length == 0)
                    {
                        throw new CoresException("oldData.Length == 0");
                    }
                    else if (oldData.Length < newData.Length)
                    {
                        throw new CoresException("oldData.Length < newData.Length");
                    }
                    else
                    {
                        Memory<byte> newData2 = new byte[oldData.Length];

                        newData.CopyTo(newData2);

                        newData2.Slice(newData.Length).Span.Fill(fillByte);

                        newData = newData2;
                    }
                }

                list.Add(new Pair4<string, ReadOnlyMemory<byte>, ReadOnlyMemory<byte>, HashSet<long>>(oldStr, oldData, newData, new HashSet<long>()));
            }

            if (list.Any() == false)
            {
                // 置換なし
                return new KeyValueList<string, int>();
            }

            bool same = (srcFilePath.Equals(destFilePath));

            await using FileObject srcFile = same ? await srcFilePath.OpenAsync(true, cancel: cancel, additionalFlags: additionalFlags) : await srcFilePath.OpenAsync(false, cancel: cancel, additionalFlags: additionalFlags);
            await using FileObject destFile = same ? srcFile : await destFilePath.CreateAsync(cancel: cancel, additionalFlags: additionalFlags);

            long filesize = await srcFile.GetFileSizeAsync(cancel: cancel);

            bufferSize = (int)Math.Min(bufferSize, filesize);

            bufferSize = Math.Max(bufferSize, list.Max(x => x.B.Length) * 2);

            using (MemoryHelper.FastAllocMemoryWithUsing(bufferSize, out Memory<byte> buffer))
            {
                for (long pos = 0; pos < filesize; pos += ((bufferSize + 1) / 2))
                {
                    long blockSize = Math.Min(bufferSize, filesize - pos);

                    int actualSize = await srcFile.ReadRandomAsync(pos, buffer, cancel);

                    if (actualSize >= 1)
                    {
                        Memory<byte> target = buffer.Slice(0, actualSize);

                        bool modified = false;

                        Sync(() =>
                        {
                            var span = target.Span;

                            foreach (var item in list)
                            {
                                int start = 0;
                                while (true)
                                {
                                    int found = span._IndexOfAfter(item.B.Span, start);
                                    if (found == -1) break;
                                    start = found + 1;

                                    item.C.Span.CopyTo(span.Slice(found));
                                    item.D.Add(found + pos);
                                    modified = true;
                                }
                            }
                        });

                        if (same == false || modified)
                        {
                            await destFile.WriteRandomAsync(pos, target, cancel);
                        }
                    }
                }
            }

            KeyValueList<string, int> ret = new KeyValueList<string, int>();

            foreach (var item in list)
            {
                ret.Add(item.A, item.D.Count);
            }

            return ret;
        }
    }

    // 複数のホストに対して SSH コマンドをバッチ実行する
    public static async Task BatchExecSshAsync(IEnumerable<BatchExecSshItem> items, UnixShellProcessorSettings? settings = null)
    {
        foreach (var item in items)
        {
            $"------- {item.Host} ------"._Print();
            try
            {
                await using SecureShellClient ssh = new SecureShellClient(new SecureShellClientSettings(item.Host, item.Port, item.Username, item.Password));
                await using ShellClientSock sock = await ssh.ConnectAndGetSockAsync();
                await using var proc = sock.CreateUnixShellProcessor(settings);

                var result = await proc.ExecBashCommandAsync(item.CommandLine);


                $"\n\n----- OK -----\n\n"._Print();
                result.StringList._OneLine()._Print();
            }
            catch (Exception ex)
            {
                "\n\n-- !! ERROR !! --\n\n"._Print();
                ex._Debug();
                "\n\n"._Print();
            }
        }
    }

    public static void GenkoToHtml(string srcTxt, string dstHtml)
    {
        string body = Lfs.ReadStringFromFile(srcTxt);

        string[] lines = body._GetLines();

        StringWriter w = new StringWriter();

        foreach (string line in lines)
        {
            string s = line;

            if (s._IsEmpty())
            {
                s = $"<p>{Str.HtmlSpacing}</p>";
            }
            else if (s.All(c => c <= 0x7f))
            {
                s = $"<p style=\"text-align: center\">{s._EncodeHtml()}</p>";
            }
            else
            {
                s = $"<p>{s._EncodeHtml()}</p>";
            }

            w.WriteLine(s);
        }

        Lfs.WriteStringToFile(dstHtml, w.ToString(), FileFlags.AutoCreateDirectory, writeBom: true);
    }

    public static void ReplaceStringOfFiles(string dirName, string pattern, string oldString, string newString, bool caseSensitive)
    {
        IEnumerable<string> files;
        if (Directory.Exists(dirName) == false && File.Exists(dirName))
        {
            files = dirName._SingleArray();
        }
        else
        {
            files = Lfs.EnumDirectory(dirName, true)
                .Where(x => x.IsFile)
                .Where(x => Lfs.PathParser.IsFullPathExcludedByExcludeDirList(Lfs.PathParser.GetDirectoryName(x.FullPath)) == false)
                .Where(x => Str.MultipleWildcardMatch(x.Name, pattern, true))
                .Select(x => x.FullPath);
        }

        int n = 0;

        foreach (string file in files)
        {
            Con.WriteLine("処理中: '{0}'", file);

            byte[] data = File.ReadAllBytes(file);

            int bom;
            Encoding? enc = Str.GetEncoding(data, out bom);
            if (enc == null)
            {
                enc = Encoding.UTF8;
            }

            string srcStr = enc.GetString(Util.ExtractByteArray(data, bom, data.Length - bom));
            string dstStr = Str.ReplaceStr(srcStr, oldString, newString, caseSensitive);

            if (srcStr != dstStr)
            {
                Buf buf = new Buf();

                if (bom != 0)
                {
                    var bomData = Str.GetBOM(enc);
                    if (bomData != null)
                    {
                        buf.Write(bomData);
                    }
                }

                buf.Write(enc.GetBytes(dstStr));

                buf.SeekToBegin();

                File.WriteAllBytes(file, buf.Read());

                Con.WriteLine("  保存しました。");
                n++;
            }
            else
            {
                Con.WriteLine("  変更なし");
            }
        }

        Con.WriteLine("{0} 個のファイルを変換しましたよ!!", n);
    }

    public static void NormalizeCrLfOfFiles(string dirName, string pattern)
    {
        IEnumerable<string> files;
        if (Directory.Exists(dirName) == false && File.Exists(dirName))
        {
            files = dirName._SingleArray();
        }
        else
        {
            files = Lfs.EnumDirectory(dirName, true)
                .Where(x => x.IsFile)
                .Where(x => Lfs.PathParser.IsFullPathExcludedByExcludeDirList(Lfs.PathParser.GetDirectoryName(x.FullPath)) == false)
                .Where(x => Str.MultipleWildcardMatch(x.Name, pattern, true))
                .Select(x => x.FullPath);
        }

        int n = 0;

        foreach (string file in files)
        {
            Con.WriteLine("処理中: '{0}'", file);

            byte[] data = File.ReadAllBytes(file);

            var ret = Str.NormalizeCrlf(data, CrlfStyle.CrLf, true);

            File.WriteAllBytes(file, ret.ToArray());

            n++;
        }

        Con.WriteLine("{0} 個のファイルを変換しましたよ!!", n);
    }

    public static void ChangeEncodingOfFiles(string dirName, string pattern, bool bom, string encoding)
    {
        IEnumerable<string> files;
        if (Directory.Exists(dirName) == false && File.Exists(dirName))
        {
            files = dirName._SingleArray();
        }
        else
        {
            files = Lfs.EnumDirectory(dirName, true)
                .Where(x => x.IsFile)
                .Where(x => Lfs.PathParser.IsFullPathExcludedByExcludeDirList(Lfs.PathParser.GetDirectoryName(x.FullPath)) == false)
                .Where(x => Str.MultipleWildcardMatch(x.Name, pattern, true))
                .Select(x => x.FullPath);
        }

        Encoding enc = Encoding.GetEncoding(encoding);

        int n = 0;

        foreach (string file in files)
        {
            Con.WriteLine("処理中: '{0}'", file);

            byte[] data = File.ReadAllBytes(file);

            byte[] ret = Str.ConvertEncoding(data, enc, bom);

            File.WriteAllBytes(file, ret);

            n++;
        }

        Con.WriteLine("{0} 個のファイルを変換しましたよ!!", n);
    }
}

// ログファイルの JSON をパースするとこの型が出てくる
public class LogJsonParseAsRuntimeStat
{
    public DateTimeOffset? TimeStamp;
    public CoresRuntimeStat? Data;
    public string? TypeName;    // "CoresRuntimeStat"
    public string? Kind;        // "Stat"
    public string? Priority;
    public string? Tag;         // "Snapshot"
    public string? AppName;
    public string? MachineName;
    public string? Guid;
}

public class LogStatMemoryLeakAnalyzerCsvRow
{
    public DateTime Dt;
    public long Mem;
}

// stat ログをもとにメモリリークしていないかどうか分析するためのユーティリティクラス
public static class LogStatMemoryLeakAnalyzer
{
    public static List<LogStatMemoryLeakAnalyzerCsvRow> AnalyzeLogFiles(string logDir)
    {
        Dictionary<DateTime, long> table = new Dictionary<DateTime, long>();

        var files = Lfs.EnumDirectory(logDir).Where(x => x.IsFile && x.Name._IsExtensionMatch(".log")).OrderBy(x => x.Name, StrComparer.IgnoreCaseComparer);

        foreach (var file in files)
        {
            file.FullPath._Print();

            using var f = Lfs.Open(file.FullPath);
            using var stream = f.GetStream();
            var r = new BinaryLineReader(stream);
            while (true)
            {
                List<Memory<byte>>? list = r.ReadLines();
                if (list == null) break;

                foreach (var data in list)
                {
                    string line = data._GetString_UTF8();

                    try
                    {
                        var lineData = line._JsonToObject<LogJsonParseAsRuntimeStat>();

                        if (lineData != null)
                        {
                            if (lineData.TypeName == "CoresRuntimeStat" && lineData.Tag == "Snapshot")
                            {
                                CoresRuntimeStat? stat = lineData.Data;
                                if (stat != null)
                                {
                                    if (stat.Mem != 0)
                                    {
                                        DateTime dt = lineData.TimeStamp!.Value.LocalDateTime.Date;
                                        if (table.TryAdd(dt, stat.Mem) == false)
                                        {
                                            table[dt] = Math.Min(table[dt], stat.Mem);
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        ex._Debug();
                    }
                }
            }
        }

        List<LogStatMemoryLeakAnalyzerCsvRow> ret = new List<LogStatMemoryLeakAnalyzerCsvRow>();

        var dates = table.Keys.OrderBy(x => x);
        if (dates.Any())
        {
            for (DateTime date = dates.First(); date <= dates.Last(); date = date.AddDays(1))
            {
                long mem = table.GetValueOrDefault(date, 0);

                ret.Add(new LogStatMemoryLeakAnalyzerCsvRow { Dt = date, Mem = mem });
            }
        }

        return ret;
    }
}

public static partial class CoresConfig
{
    public static partial class FileDownloader
    {
        public static readonly Copenhagen<int> DefaultMaxConcurrentThreads = 20;
        public static readonly Copenhagen<int> DefaultRetryIntervalMsecs = 1000;
        public static readonly Copenhagen<int> DefaultTryCount = 5;
        public static readonly Copenhagen<int> DefaultBufferSize = 1 * 1024 * 1024; // 1MB
        public static readonly Copenhagen<int> DefaultAdditionalConnectionIntervalMsecs = 1000;
        public static readonly Copenhagen<int> DefaultMaxConcurrentFiles = 20;

        // 重いサーバー (大量のインスタンスや大量のコンテナが稼働、または大量のコネクションを処理) における定数変更
        public static void ApplyHeavyLoadServerConfig()
        {
            DefaultBufferSize.TrySet(65536);
        }
    }
}

// ファイルダウンロードオプション
public class FileDownloadOption
{
    public int MaxConcurrentThreads { get; }
    public int MaxConcurrentFiles { get; }
    public int RetryIntervalMsecs { get; }
    public int TryCount { get; }
    public WebApiOptions WebApiOptions { get; }
    public int BufferSize { get; }
    public int AdditionalConnectionIntervalMsecs { get; }
    public bool IgnoreErrorInMultiFileDownload { get; }

    public FileDownloadOption(int maxConcurrentThreads = -1, int maxConcurrentFiles = -1, int retryIntervalMsecs = -1, int tryCount = -1, int bufferSize = 0, int additionalConnectionIntervalMsecs = -1, WebApiOptions? webApiOptions = null, bool ignoreErrorInMultiFileDownload = false)
    {
        if (maxConcurrentThreads <= 0) maxConcurrentThreads = CoresConfig.FileDownloader.DefaultMaxConcurrentThreads;
        if (maxConcurrentFiles <= 0) maxConcurrentFiles = CoresConfig.FileDownloader.DefaultMaxConcurrentFiles;
        if (retryIntervalMsecs < 0) retryIntervalMsecs = CoresConfig.FileDownloader.DefaultRetryIntervalMsecs;
        if (tryCount <= 0) tryCount = CoresConfig.FileDownloader.DefaultTryCount;
        if (webApiOptions == null) webApiOptions = new WebApiOptions();
        if (bufferSize <= 0) bufferSize = CoresConfig.FileDownloader.DefaultBufferSize;
        if (additionalConnectionIntervalMsecs <= 0) additionalConnectionIntervalMsecs = CoresConfig.FileDownloader.DefaultAdditionalConnectionIntervalMsecs;

        MaxConcurrentThreads = maxConcurrentThreads;
        MaxConcurrentFiles = maxConcurrentFiles;
        RetryIntervalMsecs = retryIntervalMsecs;
        TryCount = tryCount;
        WebApiOptions = webApiOptions;
        BufferSize = bufferSize;
        AdditionalConnectionIntervalMsecs = additionalConnectionIntervalMsecs;
        IgnoreErrorInMultiFileDownload = ignoreErrorInMultiFileDownload;
    }
}

// 並行ダウンロードのようなタスクの部分マップの管理
public class ConcurrentDownloadPartialMaps
{
    public long TotalSize { get; }
    public int MaxPartialFragments { get; }

    public readonly CriticalSection Lock = new CriticalSection<ConcurrentDownloadPartialMaps>();

    internal readonly SortedList<long, ConcurrentDownloadPartial> List = new SortedList<long, ConcurrentDownloadPartial>();

    public ConcurrentDownloadPartialMaps(long totalSize, int maxPartialFragments = Consts.Numbers.DefaultMaxPartialFragments)
    {
        if (totalSize < 0) throw new ArgumentOutOfRangeException(nameof(totalSize));

        this.TotalSize = totalSize;
        this.MaxPartialFragments = maxPartialFragments;
    }

    // 未完了のバイト数を取得する
    public long CalcUnfinishedTotalSize()
    {
        lock (Lock)
        {
            if (List.Count == 0) return this.TotalSize;

            long calcDistanceTotal = 0;

            for (int i = 0; i < List.Count; i++)
            {
                // この partial の後に続く partial までの空白距離を計算する
                ConcurrentDownloadPartial thisPartial = List.Values[i];
                long nextPartialStartPos = this.TotalSize;

                if ((i + 1) < List.Count)
                {
                    nextPartialStartPos = List.Values[i + 1].StartPosition;
                }

                long distance = nextPartialStartPos - (thisPartial.StartPosition + thisPartial.CurrentLength);
                if (distance < 0) distance = 0;

                calcDistanceTotal += distance;
            }

            return Math.Min(this.TotalSize, calcDistanceTotal);
        }
    }

    // 完了したバイト数を取得する
    public long CalcFinishedTotalSize() => this.TotalSize - CalcUnfinishedTotalSize();

    // 現在未完了の領域のうち最小の部分の中心を返す (ない場合は null を返す)
    public long? GetMaxUnfinishedPartialStartCenterPosison()
    {
        lock (Lock)
        {
            if (List.Count == 0) return 0;

            long maxDistance = long.MinValue;
            int maxDistancePartialIndex = -1;

            for (int i = 0; i < List.Count; i++)
            {
                // この partial の後に続く partial までの空白距離を計算する
                ConcurrentDownloadPartial thisPartial = List.Values[i];
                long nextPartialStartPos = this.TotalSize;

                if ((i + 1) < List.Count)
                {
                    nextPartialStartPos = List.Values[i + 1].StartPosition;
                }

                long distance = nextPartialStartPos - (thisPartial.StartPosition + thisPartial.CurrentLength);
                if (distance < 0) distance = 0;

                if (distance > maxDistance)
                {
                    maxDistancePartialIndex = i;
                    maxDistance = distance;
                }
            }

            Debug.Assert(maxDistancePartialIndex != -1);

            Debug.Assert(maxDistance >= 0);

            if (maxDistance <= 0)
            {
                // もうない
                return null;
            }

            var maxDistancePartial = List.Values[maxDistancePartialIndex];

            return maxDistancePartial.StartPosition + maxDistancePartial.CurrentLength + maxDistance / 2;
        }
    }

    // 部分を開始する。startPosition が null の場合は、現在未完了の領域のうち最長の部分の中心を startPartial とする。
    // もうこれ以上部分を開始することができない場合は、null を返す。
    public ConcurrentDownloadPartial? StartPartial(long? startPosition = null)
    {
        lock (Lock)
        {
            if (startPosition == null) startPosition = GetMaxUnfinishedPartialStartCenterPosison();

            if (startPosition == null) return null; // もうない

            if (this.List.Count >= this.MaxPartialFragments)
            {
                // これ以上作成できない
                throw new CoresException("this.List.Count >= this.MaxPartialFragments");
            }

            if (this.List.ContainsKey(startPosition.Value))
            {
                // もうある
                return null;
            }

            var newPartial = new ConcurrentDownloadPartial(this, startPosition.Value);

            this.List.Add(newPartial.StartPosition, newPartial);

            return newPartial;
        }
    }

    // すべて完了しているかどうか
    public bool IsAllFinished()
    {
        if (GetMaxUnfinishedPartialStartCenterPosison() == null)
        {
            return true;
        }

        return false;
    }
}
public class ConcurrentDownloadPartial
{
    public ConcurrentDownloadPartialMaps Maps { get; }
    public long StartPosition { get; }
    public long CurrentLength { get; private set; }

    Once Finished;

    public ConcurrentDownloadPartial(ConcurrentDownloadPartialMaps maps, long startPosition)
    {
        this.Maps = maps;
        this.StartPosition = startPosition;
        this.CurrentLength = 0;
    }

    // CurrentLength を変更する。先の Partial の先頭とぶつかったら、すべて完了したということであるので false を返す。
    public bool UpdateCurrentLength(long currentLength)
    {
        if (currentLength < 0) throw new ArgumentOutOfRangeException(nameof(currentLength));
        if (Finished.IsSet) throw new CoresException("ConcurrentDownloadPartial.Finished.IsSet");

        lock (Maps.Lock)
        {
            if (currentLength < this.CurrentLength) throw new ArgumentException("currentLength < this.CurrentLength");
            this.CurrentLength = currentLength;

            int thisIndex = Maps.List.IndexOfKey(this.StartPosition);
            Debug.Assert(thisIndex != -1);

            long nextPartialStartPos;

            int nextIndex = thisIndex + 1;
            if (nextIndex >= this.Maps.List.Count)
            {
                nextPartialStartPos = this.Maps.TotalSize;
            }
            else
            {
                nextPartialStartPos = this.Maps.List.Values[nextIndex].StartPosition;
            }

            if ((this.StartPosition + this.CurrentLength) >= nextPartialStartPos)
            {
                // 次とぶつかった
                return false;
            }

            // まだぶつかっていない
            return true;
        }
    }

    // CurrentLength を増加する。先の Partial の先頭とぶつかったら、すべて完了したということであるので false を返す。
    public bool AdvanceCurrentLength(long size)
    {
        if (size < 0) throw new ArgumentOutOfRangeException(nameof(size));

        lock (Maps.Lock)
        {
            return UpdateCurrentLength(this.CurrentLength + size);
        }
    }

    // この partial の処理を完了またはキャンセルするときに呼び出される。進捗 Length が 0 の場合、親 Map から自分自身を GC (消去) する。
    public void FinishOrCancelPartial()
    {
        if (Finished.IsFirstCall())
        {
            lock (Maps.Lock)
            {
                if (this.CurrentLength == 0)
                {
                    bool b = this.Maps.List.Remove(this.StartPosition);
                    Debug.Assert(b);
                }
            }
        }
    }
}

public static class FileDownloader
{
    // 指定されたファイルを分割ダウンロードする
    public static async Task<long> DownloadFileParallelAsync(string url, Stream destStream, FileDownloadOption? option = null, Ref<WebSendRecvResponse>? responseHeader = null, ProgressReporterBase? progressReporter = null, CancellationToken cancel = default)
    {
        if (option == null) option = new FileDownloadOption();
        if (responseHeader == null) responseHeader = new Ref<WebSendRecvResponse>();
        if (progressReporter == null) progressReporter = new NullProgressReporter();
        AsyncLock streamLock = new AsyncLock();

        // まずファイルサイズを取得してみる
        RetryHelper<long> h = new RetryHelper<long>(option.RetryIntervalMsecs, option.TryCount);

        long fileSize = -1;
        bool supportPartialDownload = false;

        await h.RunAsync(async c =>
        {
            using var http = new WebApi(option.WebApiOptions);

            using var res = await http.HttpSendRecvDataAsync(new WebSendRecvRequest(WebMethods.HEAD, url, cancel));

            fileSize = res.DownloadContentLength ?? -1;

            // ヘッダ情報を参考情報として呼び出し元に返す
            responseHeader.Set(res);

            if (res.HttpResponseMessage.Headers.AcceptRanges.Where(x => x._IsSamei("bytes")).Any())
            {
                supportPartialDownload = true;
            }

            return 0;
        }, retryInterval: option.RetryIntervalMsecs, tryCount: option.TryCount, cancel: cancel);

        return await h.RunAsync(async cancel =>
        {
            destStream.Seek(0, SeekOrigin.Begin);
            destStream.SetLength(0);
            await destStream.FlushAsync(cancel);

            if (fileSize >= 0 && supportPartialDownload)
            {
                // 分割ダウンロードが可能な場合は、分割ダウンロードを開始する
                destStream.SetLength(fileSize);

                ConcurrentDownloadPartialMaps maps = new ConcurrentDownloadPartialMaps(fileSize);

                AsyncConcurrentTask concurrent = new AsyncConcurrentTask(option.MaxConcurrentThreads);

                //List<Task<bool>> runningTasks = new List<Task<bool>>();

                RefBool noMoreNeedNewTask = false;
                AsyncManualResetEvent noMoreNeedNewTaskEvent = new AsyncManualResetEvent();

                RefInt taskIdSeed = 0;
                RefLong totalDownloadSize = 0;

                RefInt currentNumConcurrentTasks = new RefInt();

                Ref<Exception?> lastException = new Ref<Exception?>(null);

                await using var cancel2 = new CancelWatcher(cancel);
                AsyncManualResetEvent finishedEvent = new AsyncManualResetEvent();
                //bool isTimeout = false;

                // 一定時間経ってもダウンロードサイズが全く増えないことを検知するタスク
                Task monitorTask = AsyncAwait(async () =>
                {
                    if (option.WebApiOptions.Settings.Timeout == Timeout.Infinite)
                    {
                        return;
                    }

                    long lastSize = 0;
                    long lastChangedTick = Time.Tick64;

                    while (finishedEvent.IsSet == false && cancel2.IsCancellationRequested == false)
                    {
                        long now = Time.Tick64;
                        long currentSize = totalDownloadSize;

                        if (lastSize != currentSize)
                        {
                            lastSize = currentSize;
                            lastChangedTick = now;
                        }

                        if (now > (lastChangedTick + option.WebApiOptions.Settings.Timeout))
                        {
                            // タイムアウト発生
                            cancel2.Cancel();
                            Dbg.Where();
                            //isTimeout = true;
                            lastException.Set(new TimeoutException());

                            break;
                        }

                        await TaskUtil.WaitObjectsAsync(cancels: cancel2.CancelToken._SingleArray(), manualEvents: finishedEvent._SingleArray(), timeout: 100);
                    }
                });

                while (noMoreNeedNewTask == false)
                {
                    cancel2.CancelToken.ThrowIfCancellationRequested();

                    // 同時に一定数までタスクを作成する
                    var newTask = await concurrent.StartTaskAsync<int, bool>(async (p1, c1) =>
                    {
                        //maps.CalcUnfinishedTotalSize()._Debug();
                        bool started = false;
                        int taskId = taskIdSeed.Increment();

                        try
                        {
                            // 新しい部分を開始
                            var partial = maps.StartPartial();
                            if (partial == null)
                            {
                                // もう新しい部分を開始する必要がない
                                //$"Task {taskId}: No more partial"._Debug();
                                if (maps.IsAllFinished())
                                {
                                    // IsAllFinished() は必ずチェックする。
                                    // そうしないと、1 バイトの空白領域が残っているときに取得未了になるおそれがあるためである。
                                    noMoreNeedNewTask.Set(true);
                                    noMoreNeedNewTaskEvent.Set(true);
                                }
                                return false;
                            }

                            try
                            {
                                currentNumConcurrentTasks.Increment();

                                //$"Task {taskId}: Start from {partial.StartPosition}"._Debug();

                                // ダウンロードの実施
                                await using var http = new WebApi(option.WebApiOptions);
                                await using var res = await http.HttpSendRecvDataAsync(new WebSendRecvRequest(WebMethods.GET, url + "", cancel2, rangeStart: partial.StartPosition));
                                await using var src = res.DownloadStream;

                                // Normal copy
                                using (MemoryHelper.FastAllocMemoryWithUsing(option.BufferSize, out Memory<byte> buffer))
                                {
                                    while (true)
                                    {
                                        cancel2.ThrowIfCancellationRequested();

                                        Memory<byte> thisTimeBuffer = buffer;

                                        int readSize = await src._ReadAsyncWithTimeout(thisTimeBuffer, timeout: option.WebApiOptions.Settings.Timeout, cancel: cancel2, allowEof: true);

                                        Debug.Assert(readSize <= thisTimeBuffer.Length);

                                        if (readSize <= 0)
                                        {
                                            //$"Task {taskId}: No more recv data"._Debug();
                                            break;
                                        }

                                        started = true;

                                        ReadOnlyMemory<byte> sliced = thisTimeBuffer.Slice(0, readSize);

                                        using (await streamLock.LockWithAwait(cancel2))
                                        {
                                            destStream.Position = partial.StartPosition + partial.CurrentLength;
                                            await destStream.WriteAsync(sliced, cancel2);
                                            totalDownloadSize.Add(sliced.Length);
                                        }

                                        progressReporter.ReportProgress(new ProgressData(maps.CalcFinishedTotalSize(), maps.TotalSize, false, $"{currentNumConcurrentTasks} connections"));

                                        if (partial.AdvanceCurrentLength(sliced.Length) == false)
                                        {
                                            // 次の partial または末尾にぶつかった
                                            //$"Task {taskId}: Reached to the next partial"._Debug();
                                            break;
                                        }
                                    }
                                }

                                //$"Task {taskId}: Finished. Position: {partial.StartPosition + partial.CurrentLength}, size: {partial.CurrentLength}"._Debug();
                                return false;
                            }
                            finally
                            {
                                currentNumConcurrentTasks.Decrement();
                                partial.FinishOrCancelPartial();
                            }
                        }
                        catch (Exception ex)
                        {
                            if (started == false)
                            {
                                //$"Task {taskId}: error. {ex._GetSingleException().Message}"._Debug();
                            }
                            lastException.Set(ex);
                            return false;
                        }
                    },
                    0,
                    cancel2.CancelToken);

                    await noMoreNeedNewTaskEvent.WaitAsync(option.AdditionalConnectionIntervalMsecs, cancel2);
                }

                //Dbg.Where();
                await concurrent.WaitAllTasksFinishAsync();
                //Dbg.Where();

                finishedEvent.Set(true);

                await monitorTask._TryAwait(false);

                //if (isTimeout) lastException.Set(new TimeoutException());

                if (maps.IsAllFinished() == false)
                {
                    if (lastException.Value != null)
                    {
                        // エラーが発生していた
                        lastException.Value._ReThrow();
                    }
                    else
                    {
                        throw new CoresException("maps.IsAllFinished() == false");
                    }
                }
                else
                {
                    progressReporter.ReportProgress(new ProgressData(maps.TotalSize, maps.TotalSize, true));
                }

                //$"File Size = {fileSize._ToString3()}, Total Down Size = {totalDownloadSize.Value._ToString3()}"._Debug();

                return totalDownloadSize;
            }
            else
            {
                // 分割ダウンロード NG の場合は、通常の方法でダウンロードする
                await using var http = new WebApi(option.WebApiOptions);

                await using var res = await http.HttpSendRecvDataAsync(new WebSendRecvRequest(WebMethods.GET, url, cancel));

                long totalSize = await res.DownloadStream.CopyBetweenStreamAsync(destStream, reporter: progressReporter, cancel: cancel, readTimeout: option.WebApiOptions.Settings.Timeout);

                progressReporter.ReportProgress(new ProgressData(totalSize, isFinish: true));

                return totalSize;
            }
        }, retryInterval: option.RetryIntervalMsecs, tryCount: option.TryCount, cancel: cancel);
    }

    // 指定された URL (のテキストファイル) をダウンロードし、その URL に記載されているすべてのファイルをダウンロードする
    public static async Task<bool> DownloadUrlListedAsync(string urlListedFileUrl, string destDir, string extensions, FileDownloadOption? option = null, ProgressReporterFactoryBase? reporterFactory = null, CancellationToken cancel = default)
    {
        if (option == null) option = new FileDownloadOption();
        if (reporterFactory == null) reporterFactory = new NullReporterFactory();

        string body;

        if (urlListedFileUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            urlListedFileUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            // ファイル一覧のファイルをダウンロードする
            using var web = new WebApi(new WebApiOptions(new WebApiSettings { SslAcceptAnyCerts = true }));

            var urlFileBody = await web.SimpleQueryAsync(WebMethods.GET, urlListedFileUrl, cancel);
            body = urlFileBody.Data._GetString_UTF8();
        }
        else
        {
            // ローカルのテキストファイルを読み込む
            body = Lfs.ReadStringFromFile(urlListedFileUrl);
        }

        int currentPos = 0;

        List<string> fileUrlList = new List<string>();

        List<Tuple<string, string>> errorList = new List<Tuple<string, string>>();

        while (true)
        {
            int r = body._FindStringsMulti2(currentPos, StringComparison.OrdinalIgnoreCase, out _, "http://", "https://");
            if (r == -1) break;
            currentPos = r + 1;

            string fileUrl = body.Substring(r);
            int t = fileUrl._FindStringsMulti2(0, StringComparison.OrdinalIgnoreCase, out _, " ", "　", "\t", "'", "\"", "\r", "\n", "]", ">");
            if (t != -1)
            {
                fileUrl = fileUrl.Substring(0, t);
            }

            if (fileUrl._IsExtensionMatch(extensions))
            {
                fileUrlList.Add(fileUrl);
            }
        }

        await TaskUtil.ForEachAsync(option.MaxConcurrentFiles, fileUrlList, async (fileUrl, taskIndex, cancel) =>
        {
            string destFileName = PathParser.Mac.GetFileName(fileUrl);
            string destFileFullPath = Lfs.PathParser.Combine(destDir, destFileName);

            using var reporter = reporterFactory.CreateNewReporter(destFileName);

            using var file = await Lfs.CreateAsync(destFileFullPath, false, FileFlags.AutoCreateDirectory, cancel: cancel);
            await using var fileStream = file.GetStream();

            try
            {
                await DownloadFileParallelAsync(fileUrl, fileStream, option, progressReporter: reporter, cancel: cancel);
            }
            catch (Exception ex)
            {
                Con.WriteError($"Error. URL: {fileUrl} ErrorMessage = {ex.Message}");

                errorList.Add(new Tuple<string, string>(fileUrl, ex.Message));

                if (option.IgnoreErrorInMultiFileDownload == false)
                {
                    throw;
                }
            }
        }, cancel: cancel);

        if (errorList.Count >= 1)
        {
            Con.WriteLine("----------");
        }

        for (int i = 0; i < errorList.Count; i++)
        {
            var err = errorList[i];

            Con.WriteError($"Error occured: {i + 1}/{errorList.Count}: {err.Item1} {err.Item2}");
        }

        return (errorList.Count == 0);
    }
}

public static partial class CoresConfig
{
    public static partial class GitParallelUpdater
    {
        public static readonly Copenhagen<int> GitCommandTimeoutMsecs = 10 * 60 * 1000;
        public static readonly Copenhagen<int> GitCommandOutputMaxSize = 10 * 1024 * 1024;
        public static readonly Copenhagen<string> GitParallelTxtFileName = "GitParallelUpdate.txt";
    }
}

public static class GitParallelUpdater
{
    class Entry
    {
        public string DirPath = null!;
        public string OriginName = "origin";
        public string BranchName = "";
        public bool Ignore = false;
    }

    public static async Task ExecGitParallelUpdaterAsync(string rootDirPath, int maxConcurrentTasks, string? settingFileName = null, CancellationToken cancel = default)
    {
        string gitExePath = Util.GetGitForWindowsExeFileName();

        List<Entry> entryList = new List<Entry>();

        List<Entry> settingsList = new List<Entry>();

        // 指定されたディレクトリに GitParallelUpdate.txt ファイルがあれば読み込む
        string txtFilePath = PP.Combine(rootDirPath, CoresConfig.GitParallelUpdater.GitParallelTxtFileName);

        if (settingFileName._IsFilled())
            txtFilePath = settingFileName;

        if (await Lfs.IsFileExistsAsync(txtFilePath))
        {
            string body = await Lfs.ReadStringFromFileAsync(txtFilePath, cancel: cancel);
            foreach (string line2 in body._GetLines(true))
            {
                string line = line2._StripCommentFromLine();

                if (line._IsFilled())
                {
                    string[] tokens = line._Split(StringSplitOptions.RemoveEmptyEntries, " ", "\t");

                    if (tokens.Length >= 1)
                    {
                        Entry e = new Entry
                        {
                            DirPath = tokens.ElementAtOrDefault(0)!,
                            OriginName = tokens.ElementAtOrDefault(1)!,
                            BranchName = tokens.ElementAtOrDefault(2)!,
                        };

                        if (e.OriginName._IsSamei("ignore"))
                        {
                            e.Ignore = true;
                        }
                        else
                        {
                            e.OriginName = e.OriginName._FilledOrDefault("origin");
                            e.BranchName = e.BranchName._FilledOrDefault("");
                        }

                        settingsList.Add(e);
                    }
                }
            }
        }

        // 指定されたディレクトリにあるサブディレクトリの一覧を列挙し、その中に .git サブディレクトリがあるものを git ローカルリポジトリとして列挙する
        var subDirList = await Lfs.EnumDirectoryAsync(rootDirPath);

        foreach (var subDir in subDirList.Where(x => x.IsDirectory && x.IsCurrentOrParentDirectory == false))
        {
            string gitDirPath = Lfs.PathParser.Combine(subDir.FullPath, ".git");

            if ((await Lfs.IsDirectoryExistsAsync(gitDirPath, cancel)))
            {
                subDir.FullPath._Debug();

                var e = new Entry { DirPath = subDir.FullPath };

                var setting = settingsList.Where(x => x.DirPath._IsSamei(subDir.Name)).FirstOrDefault();

                if (setting != null)
                {
                    if (setting.Ignore)
                    {
                        continue;
                    }

                    e.OriginName = setting.OriginName;
                    e.BranchName = setting.BranchName;
                }

                entryList.Add(e);
            }
        }

        List<Tuple<Entry, Task>> RunningTasksList = new List<Tuple<Entry, Task>>();

        using SemaphoreSlim sem = new SemaphoreSlim(maxConcurrentTasks, maxConcurrentTasks);
        int index = 0;
        foreach (var entry in entryList)
        {
            index++;
            Task t = AsyncAwait(async () =>
            {
                int thisIndex = index;
                var target = entry;

                await sem.WaitAsync(cancel);

                try
                {

                    string printTag = "[" + thisIndex + ": " + Lfs.PathParser.GetFileName(target.DirPath) + "]";

                    try
                    {
                        var result1 = await EasyExec.ExecAsync(gitExePath, $"pull {target.OriginName} {target.BranchName}", target.DirPath,
                            timeout: CoresConfig.GitParallelUpdater.GitCommandTimeoutMsecs,
                            easyOutputMaxSize: CoresConfig.GitParallelUpdater.GitCommandOutputMaxSize,
                            cancel: cancel,
                            printTag: printTag,
                            flags: ExecFlags.Default | ExecFlags.EasyPrintRealtimeStdErr | ExecFlags.EasyPrintRealtimeStdOut);

                        var result2 = await EasyExec.ExecAsync(gitExePath, $"submodule update --init --recursive", target.DirPath,
                            timeout: CoresConfig.GitParallelUpdater.GitCommandTimeoutMsecs,
                            easyOutputMaxSize: CoresConfig.GitParallelUpdater.GitCommandOutputMaxSize,
                            cancel: cancel,
                            printTag: printTag,
                            flags: ExecFlags.Default | ExecFlags.EasyPrintRealtimeStdErr | ExecFlags.EasyPrintRealtimeStdOut);
                    }
                    catch (Exception ex)
                    {
                        string error = $"*** Error - {Lfs.PathParser.GetFileName(target.DirPath)} ***\n{ex.Message}\n\n";

                        Con.WriteError(error);

                        throw;
                    }
                }
                finally
                {
                    sem.Release();
                }
            });

            RunningTasksList.Add(new Tuple<Entry, Task>(entry, t));
        }

        while (true)
        {
            if (cancel.IsCancellationRequested)
            {
                // キャンセルされた
                // すべてのタスクが終了するまで待機する
                RunningTasksList.ForEach(x => x.Item2._TryWait());
                return;
            }

            if (RunningTasksList.Select(x => x.Item2).All(x => x.IsCompleted))
            {
                // すべてのタスクが完了した
                break;
            }

            // 未完了タスク数を表示する
            int numCompleted = RunningTasksList.Where(x => x.Item2.IsCompleted).Count();

            string str = $"\n--- Completed: {numCompleted} / {RunningTasksList.Count}\n" +
                $"Running tasks: {RunningTasksList.Where(x => x.Item2.IsCompleted == false).Select(x => Lfs.PathParser.GetFileName(x.Item1.DirPath))._Combine(", ")}\n";

            Con.WriteLine(str);

            await TaskUtil.WaitObjectsAsync(tasks: RunningTasksList.Select(x => x.Item2).Where(x => x.IsCompleted == false), cancels: cancel._SingleArray(), timeout: 1000);
        }

        Con.WriteLine($"\n--- All tasks completed.");
        if (RunningTasksList.Select(x => x.Item2).All(x => x.IsCompletedSuccessfully))
        {
            Con.WriteLine($"All {RunningTasksList.Count} tasks completed with OK.\n\n");
        }
        else
        {
            Con.WriteLine($"OK tasks: {RunningTasksList.Where(x => x.Item2.IsCompletedSuccessfully).Count()}, Error tasks: {RunningTasksList.Where(x => x.Item2.IsCompletedSuccessfully == false).Count()}");

            foreach (var item in RunningTasksList.Where(x => x.Item2.IsCompletedSuccessfully == false))
            {
                Con.WriteLine($"  [{Lfs.PathParser.GetFileName(item.Item1.DirPath)}]: {(item.Item2.Exception?._GetSingleException().Message ?? "Unknown")}\n");
            }

            throw new CoresException("One or more git tasks resulted errors");
        }
    }
}

public class SqlVaultUtilReaderState
{
    public Dictionary<string, long> TableNameAndLastRowId = new Dictionary<string, long>(StrComparer.IgnoreCaseComparer);
}

public class SqlVaultUtil : AsyncService
{
    public IEnumerable<DataVaultClientOptions> DataVaultClientOptions { get; }
    public Database Db { get; }
    public string SettingsName { get; }

    readonly SingleInstance Instance;

    public HiveData<SqlVaultUtilReaderState> StateDb { get; }

    public SqlVaultUtil(Database database, string settingsName, params DataVaultClientOptions[] dataVaultClientOptions)
    {
        try
        {
            // 多重起動は許容しません!!
            this.Instance = new SingleInstance(settingsName);

            this.DataVaultClientOptions = dataVaultClientOptions;
            this.Db = database;
            this.SettingsName = settingsName;

            this.StateDb = Hive.SharedLocalConfigHive.CreateAutoSyncHive<SqlVaultUtilReaderState>("SqlVaultUtilLastState/" + settingsName, () => new SqlVaultUtilReaderState());
        }
        catch
        {
            this._DisposeSafe();
            throw;
        }
    }

    public async Task ReadRowsAndWriteToVaultAsync(string tableName, string rowIdColumnName, Func<Row, IEnumerable<DataVaultData>> rowToDataListFunc, int maxRowsToFetchOnce = 4096, bool shuffle = true, CancellationToken cancel = default,
        string? lastMaxRowIdName = null)
    {
        if (lastMaxRowIdName._IsEmpty()) lastMaxRowIdName = tableName;


        $"Start: tableName = {lastMaxRowIdName}, rowIdColumnName = {rowIdColumnName}, maxRowsToFetchOnce = {maxRowsToFetchOnce}"._DebugFunc();

        long totalWrittenRows = 0;


        List<DataVaultClient> clients = new List<DataVaultClient>();
        try
        {
            // DataVault クライアントの作成
            foreach (var opt in this.DataVaultClientOptions)
            {
                clients.Add(new DataVaultClient(opt));
            }

            long lastMaxRowId = 0;
            lock (this.StateDb.DataLock)
                lastMaxRowId = this.StateDb.ManagedData.TableNameAndLastRowId._GetOrNew(lastMaxRowIdName);

            $"lastMaxRowId get = {lastMaxRowId._ToString3()}   from  tableName = {tableName}"._DebugFunc();

            while (true)
            {
                long dbCurrentMaxRowId = (await Db.QueryWithValueAsync($"select max({rowIdColumnName}) from {tableName}", cancel)).Int64;

                await Db.QueryAsync($"select top {maxRowsToFetchOnce} * from {tableName} with (nolock) where {rowIdColumnName} > {lastMaxRowId} order by {rowIdColumnName} asc", cancel);

                Data data = await Db.ReadAllDataAsync(cancel);

                if (data.IsEmpty) break;

                foreach (var row in data.RowList!)
                {
                    foreach (var client in clients)
                    {
                        IEnumerable<DataVaultData>? dataList = null;

                        try
                        {
                            dataList = rowToDataListFunc(row);
                        }
                        catch (Exception ex)
                        {
                            ex._Debug();
                            continue;
                        }

                        if (shuffle)
                        {
                            dataList = dataList._Shuffle();
                        }

                        await dataList._DoForEachAsync(data => client.WriteDataAsync(data, cancel));
                    }

                    totalWrittenRows++;
                }

                lastMaxRowId = data.RowList.Last().ValueList[0].Int64;

                $"{lastMaxRowIdName}: Current WrittenRows: {totalWrittenRows._ToString3()}, Last Row ID: {lastMaxRowId._ToString3()}, DB Current Max Row ID: {dbCurrentMaxRowId._ToString3()}"._DebugFunc();

                lock (this.StateDb.DataLock)
                    this.StateDb.ManagedData.TableNameAndLastRowId[lastMaxRowIdName] = lastMaxRowId;

                await this.StateDb.SyncWithStorageAsync(HiveSyncFlags.ForceUpdate | HiveSyncFlags.SaveToFile, false, cancel);
            }
        }
        catch (Exception ex)
        {
            ex._Error();
            throw;
        }
        finally
        {
            // DataVault クライアントの解放
            clients._DoForEach(x => x._DisposeSafe());

            $"End: tableName = {lastMaxRowIdName}, rowIdColumnName = {rowIdColumnName}, maxRowsToFetchOnce = {maxRowsToFetchOnce}, totalWrittenRows = {totalWrittenRows._ToString3()}"._DebugFunc();
        }
    }

    protected override void DisposeImpl(Exception? ex)
    {
        try
        {
            this.StateDb._DisposeSafe();

            this.Instance._DisposeSafe();
        }
        finally
        {
            base.DisposeImpl(ex);
        }
    }
}

[Serializable]
public class DnsHostNameScannerSettings : INormalizable
{
    public int NumThreads { get; set; } = 64;
    public int Interval { get; set; } = 100;
    public bool RandomInterval { get; set; } = true;
    public bool Shuffle { get; set; } = true;
    public bool PrintStat { get; set; } = true;
    public bool PrintOrderByFqdn { get; set; } = true;
    public int NumTry { get; set; } = 3;
    public IEnumerable<int> TcpPorts { get; set; } = new List<int>();

    public void Normalize()
    {
        this.NumThreads = Math.Max(this.NumThreads, 1);
        if (Interval < 0) Interval = 100;
        if (NumTry <= 0) NumTry = 1;
        if (this.TcpPorts == null) this.TcpPorts = new List<int>();
    }
}

public class DnsHostNameScannerEntry
{
    public IPAddress Ip { get; set; } = null!;
    public List<string>? HostnameList { get; set; }
    public bool NotFound { get; set; }
    public List<int> TcpPorts { get; set; } = new List<int>();
}

public class DnsHostNameScanner : AsyncService
{
    readonly DnsResolver Dr;
    readonly DnsHostNameScannerSettings Settings;

    public DnsHostNameScanner(DnsHostNameScannerSettings? settings = null, DnsResolverSettings? dnsSettings = null)
    {
        if (settings == null) settings = new DnsHostNameScannerSettings();

        this.Settings = settings._CloneDeepWithNormalize();

        Dr = DnsResolver.CreateDnsResolverIfSupported(dnsSettings);
    }

    Once Started;

    Queue<DnsHostNameScannerEntry> BeforeQueue = null!;
    List<DnsHostNameScannerEntry> AfterResult = null!;

    public Task<List<DnsHostNameScannerEntry>> PerformAsync(string targetIpAddressList, CancellationToken cancel = default)
        => PerformAsync(IPUtil.GenerateIpAddressListFromIpSubnetList(targetIpAddressList).Select(x => x.ToString()), cancel);

    public async Task<List<DnsHostNameScannerEntry>> PerformAsync(IEnumerable<string> targetIpAddressList, CancellationToken cancel = default)
    {
        if (Started.IsFirstCall() == false) throw new CoresException("Already started.");

        IEnumerable<IPAddress?> ipList = targetIpAddressList.Select(x => x._ToIPAddress(noExceptionAndReturnNull: true)).Where(x => x != null).Distinct().OrderBy(x => x, IpComparer.Comparer);

        if (this.Settings.Shuffle) ipList = ipList._Shuffle();

        // キューを作成する
        BeforeQueue = new Queue<DnsHostNameScannerEntry>();
        AfterResult = new List<DnsHostNameScannerEntry>();

        // キューに挿入する
        ipList._DoForEach(x => BeforeQueue.Enqueue(new DnsHostNameScannerEntry { Ip = x! }));

        int numTry = Settings.NumTry;

        bool usePortScan = Settings.TcpPorts.Any();

        for (int tryCount = 0; tryCount < numTry; tryCount++)
        {
            if (Settings.PrintStat) $"--- Starting Try #{tryCount + 1}: {BeforeQueue.Count._ToString3()} hosts ---"._Print();

            // スレッドを開始する
            List<Task> tasksList = new List<Task>();
            for (int i = 0; i < Settings.NumThreads; i++)
            {
                Task t = AsyncAwait(async () =>
                {
                    while (true)
                    {
                        cancel.ThrowIfCancellationRequested();

                        DnsHostNameScannerEntry? target;
                        lock (BeforeQueue)
                        {
                            if (BeforeQueue.TryDequeue(out target) == false)
                            {
                                return;
                            }
                        }

                        List<int> okPorts = new List<int>();

                        if (usePortScan)
                        {
                            // ポートスキャンの実施
                            foreach (var port in Settings.TcpPorts)
                            {
                                if (await IPUtil.CheckTcpPortWithRetryAsync(target.Ip.ToString(), port, cancel: cancel))
                                {
                                    okPorts.Add(port);
                                }
                            }
                        }

                        bool anyPortOk = usePortScan && okPorts.Any();

                        Ref<DnsAdditionalResults> additional = new Ref<DnsAdditionalResults>();

                        List<string>? ret = await Dr.GetHostNameListAsync(target.Ip, additional, cancel);

                        string portsStr = "TCP Ports: " + (anyPortOk ? okPorts.Select(x => x.ToString())._Combine(" / ") : "None");
                        if (usePortScan == false) portsStr = "";

                        if (ret._IsFilled())
                        {
                            target.HostnameList = ret;

                            if (Settings.PrintStat)
                            {
                                $"Try #{tryCount + 1}: {target.Ip.ToString()._AddSpacePadding(19)} {target.HostnameList._Combine(" / ")}   {portsStr}".Trim()._Print();
                            }
                        }
                        else
                        {
                            if (usePortScan && anyPortOk)
                            {
                                target.HostnameList = target.Ip.ToString()._SingleList();

                                if (Settings.PrintStat)
                                {
                                    $"Try #{tryCount + 1}: {target.Ip.ToString()._AddSpacePadding(19)} {target.HostnameList._Combine(" / ")}   {portsStr}".Trim()._Print();
                                }
                            }
                            else
                            {
                                if (usePortScan == false)
                                {
                                    target.NotFound = additional?.Value?.IsNotFound ?? false;
                                }
                            }
                        }

                        target.TcpPorts = okPorts;

                        lock (AfterResult)
                        {
                            AfterResult.Add(target);
                        }

                        lock (BeforeQueue)
                        {
                            if (BeforeQueue.Count == 0) return;
                        }

                        int nextInterval = Settings.Interval;

                        if (Settings.RandomInterval)
                        {
                            nextInterval = Util.GenRandInterval(Settings.Interval);
                        }

                        await cancel._WaitUntilCanceledAsync(nextInterval);
                    }
                });

                tasksList.Add(t);
            }

            // スレッドが完了するまで待つ
            await Task.WhenAll(tasksList);

            if (Settings.PrintStat) $"--- Finished: Try #{tryCount + 1} ---"._Print();

            if (tryCount != (numTry - 1))
            {
                List<DnsHostNameScannerEntry> unsolvedHosts = new List<DnsHostNameScannerEntry>();

                foreach (var item in AfterResult)
                {
                    if ((item.HostnameList._IsEmpty() && item.NotFound == false) || (usePortScan && item.TcpPorts.Any() == false))
                    {
                        // 未解決ホストかつエラー発生ホストである
                        // または TCP で 1 つもポート応答がなかったホストである
                        unsolvedHosts.Add(item);
                    }
                }

                if (unsolvedHosts.Any() == false)
                {
                    // 未解決ホストなし
                    break;
                }

                unsolvedHosts.ForEach(x => AfterResult.Remove(x));

                if (Settings.Shuffle)
                {
                    unsolvedHosts._Shuffle()._DoForEach(x => BeforeQueue.Enqueue(x));
                }
                else
                {
                    unsolvedHosts.ForEach(x => BeforeQueue.Enqueue(x));
                }
            }
        }

        if (usePortScan)
        {
            // TCP ポートスキャンモードの場合、1 つもポートが開いていないホストはリストから消す
            AfterResult = AfterResult.Where(x => x.TcpPorts.Any()).ToList();
        }

        if (Settings.PrintStat)
        {
            Con.WriteLine();
            // おもしろソート
            var printResults = AfterResult.Where(x => x.HostnameList._IsFilled());

            if (Settings.PrintOrderByFqdn)
            {
                printResults = printResults.OrderBy(x => x.HostnameList!.First(), StrComparer.FqdnReverseStrComparer).ThenBy(x => x.Ip, IpComparer.Comparer);
            }
            else
            {
                printResults = printResults.OrderBy(x => x.Ip, IpComparer.Comparer);
            }

            Con.WriteLine($"--- Results: {printResults.Count()._ToString3()} ---");

            foreach (var item in printResults)
            {
                string portsStr = "TCP Ports: " + item.TcpPorts.Select(x => x.ToString())._Combine(" / ");
                if (usePortScan == false) portsStr = "";

                $"{item.Ip.ToString()._AddSpacePadding(19)} {item.HostnameList!._Combine(" / ")}   {portsStr}".Trim()._Print();
            }
        }

        return AfterResult.OrderBy(x => x.Ip, IpComparer.Comparer).ToList();
    }

    protected override async Task CleanupImplAsync(Exception? ex)
    {
        try
        {
            await Dr._DisposeSafeAsync();
        }
        finally
        {
            await base.CleanupImplAsync(ex);
        }
    }
}

public class CSharpEasyParse
{
    public HashSet<string> UsingList = new HashSet<string>();
    public Dictionary<string, StringWriter> CodeList = new Dictionary<string, StringWriter>();

    public static CSharpEasyParse ParseFile(FilePath path)
    {
        var ret = ParseCode(path.ReadStringFromFile());

        return ret;
    }

    public static CSharpEasyParse ParseCode(string code)
    {
        CSharpEasyParse ret = new CSharpEasyParse();

        var lines = code._GetLines();

        int mode = 0;

        StringWriter? currentWriter = null;

        foreach (var line in lines)
        {
            if (mode == 0)
            {
                if (line.StartsWith("using", StringComparison.Ordinal))
                {
                    mode = 1;
                }
            }

            if (line.StartsWith("namespace", StringComparison.Ordinal))
            {
                var tokens = line._Split(StringSplitOptions.RemoveEmptyEntries, ' ', '\t');
                if (tokens.Length >= 2)
                {
                    string ns = tokens[1];

                    currentWriter = ret.CodeList._GetOrNew(ns, () => new StringWriter());

                    mode = 2;
                }
            }

            if (mode == 2)
            {
                if (line.StartsWith("{"))
                {
                    mode = 3;
                }
            }

            if (mode == 3)
            {
                if (line.StartsWith("}"))
                {
                    currentWriter?.WriteLine();
                    mode = 0;
                    currentWriter = null;
                }
            }

            switch (mode)
            {
                case 1:
                    if (line.StartsWith("using", StringComparison.Ordinal))
                    {
                        ret.UsingList.Add(line._NormalizeSoftEther(true));
                    }
                    break;

                case 3:
                    if (line.StartsWith("{") == false)
                    {
                        currentWriter!.WriteLine(line);
                    }
                    break;
            }
        }

        return ret;
    }
}

public static class CSharpConcatUtil
{
    public static void DoConcat(string srcRootDir, string destRootDir)
    {
        var dirs = Lfs.EnumDirectory(srcRootDir, true).Where(x => x.IsDirectory).OrderBy(x => x.FullPath, StrComparer.IgnoreCaseComparer);

        List<Tuple<string, CSharpEasyParse>> data = new List<Tuple<string, CSharpEasyParse>>();

        foreach (var dir in dirs)
        {
            var files = Lfs.EnumDirectory(dir.FullPath, false).Where(x => Lfs.PathParser.GetExtension(x.Name)._IsSamei(".cs")).OrderBy(x => x.Name, StrComparer.IgnoreCaseComparer);

            foreach (var file in files)
            {
                Con.WriteLine($"Parsing '{file.FullPath}' ...");
                CSharpEasyParse parsed = CSharpEasyParse.ParseFile(file.FullPath);

                data.Add(new Tuple<string, CSharpEasyParse>(PP.GetRelativeDirectoryName(PP.GetDirectoryName(file.FullPath), srcRootDir), parsed));
            }
        }

        foreach (var dir in data.Select(x => x.Item1).Distinct(StrComparer.IgnoreCaseComparer).OrderBy(x => x, StrComparer.IgnoreCaseComparer))
        {
            HashSet<string> usingList = new HashSet<string>();
            Dictionary<string, StringWriter> codeList = new Dictionary<string, StringWriter>();

            foreach (var file in data.Where(x => x.Item1._IsSamei(dir)).Select(x => x.Item2))
            {
                file.UsingList._DoForEach(x => usingList.Add(x));

                foreach (var code in file.CodeList)
                {
                    var tmp = codeList._GetOrNew(code.Key, () => new StringWriter());
                    tmp.Write(code.Value.ToString());
                }
            }

            StringWriter w = new StringWriter();

            int mode = 0;

            usingList.OrderBy(x => x, StrComparer.DevToolsCsUsingComparer)._DoForEach(x =>
            {
                if (x.StartsWith("using"))
                {
                    Str.GetKeyAndValue(x, out _, out x);
                }

                if (x.StartsWith("System"))
                {
                    mode = 1;
                }
                else
                {
                    if (mode == 1)
                    {
                        w.WriteLine();
                    }
                    mode = 2;
                }

                w.WriteLine("using " + x);
            });

            codeList.OrderBy(x => x.Key, StrComparer.IgnoreCaseComparer)._DoForEach(x =>
            {
                w.WriteLine();
                w.WriteLine($"namespace {x.Key}");
                w.WriteLine("{");
                w.Write(x.Value.ToString());
                w.WriteLine("}");
            });

            w.WriteLine();

            string dstPath = PP.Combine(destRootDir, dir) + ".cs";

            Lfs.WriteStringToFile(dstPath, w.ToString(), FileFlags.AutoCreateDirectory, writeBom: true);
        }
    }
}


public class SimpleHttpDownloaderMetaData
{
    public string Url { get; set; } = "";
    public string ContentType { get; set; } = "";
    public string MediaType { get; set; } = "";
    public int Size { get; set; }
    public string HashSha256 { get; set; } = "";
    public DateTimeOffset TimeStamp { get; set; } = Util.ZeroDateTimeOffsetValue;
}

[Flags]
public enum CachedDownloaderFlags
{
    None = 0,
    AlwaysUseCacheIfExists,
}

// CloneDeep 禁止
public class CachedDownloaderSettings
{
    public DirectoryPath CacheRootDirPath { get; }
    public CachedDownloaderFlags Flags { get; }
    public WebApiOptions WebOptions { get; }
    public int MaxTry { get; }
    public int RetryInterval { get; }

    public CachedDownloaderSettings(DirectoryPath? cacheRootDirPath = null, CachedDownloaderFlags flags = CachedDownloaderFlags.None,
        WebApiOptions? webOptions = null, int maxTry = 1, int retryInterval = 1000)
    {
        this.CacheRootDirPath = cacheRootDirPath ?? new DirectoryPath(Lfs.PathParser.Combine(Env.AppLocalDir, "CachedDownloader"), Lfs);
        this.Flags = flags;
        this.WebOptions = webOptions ?? new WebApiOptions(new WebApiSettings { MaxRecvSize = Consts.MaxLens.MaxCachedFileDownloadSizeDefault });
        this.MaxTry = Math.Max(MaxTry, 1);
        this.RetryInterval = Math.Max(retryInterval, 0);
    }
}

public class CachedDownloaderResult
{
    public Memory<byte> Data { get; set; }

    public SimpleHttpDownloaderMetaData MetaData;

    public bool FromCache { get; set; }

    public CachedDownloaderResult(Memory<byte> data, SimpleHttpDownloaderMetaData metaData, bool fromCache)
    {
        this.Data = data;
        this.MetaData = metaData;
        this.FromCache = fromCache;
    }
}

public static class CachedDownloader
{
    public static async Task<CachedDownloaderResult> DownloadAsync(string url, CancellationToken cancel = default, CachedDownloaderSettings? settings = null)
    {
        settings ??= new CachedDownloaderSettings();

        var fs = settings.CacheRootDirPath.FileSystem;

        url = url.Trim();

        var flags = settings.Flags;

        // URL からハッシュを生成
        string urlHashStr = url._HashSHA256()._GetHexString().ToLowerInvariant();

        // URL ハッシュからサブディレクトリ名を生成
        string dirPath = settings.CacheRootDirPath.Combine(urlHashStr.Substring(0, 2), urlHashStr);
        string metaDataPath = fs.PathParser.Combine(dirPath, "metadata.json");
        string contentsPath = fs.PathParser.Combine(dirPath, "contents.dat");

        if (settings.Flags.Bit(CachedDownloaderFlags.AlwaysUseCacheIfExists))
        {
            // AlwaysUseCacheIfExists フラグが付いている場合は、キャッシュがもし存在すれば、ダウンロードはせずにキャッシュをすぐに返す
            var cached = await TryLoadCacheFileAndMetaDataAsync();
            if (cached != null)
            {
                return cached;
            }
        }

        // 本家からダウンロードを試みる。
        try
        {
            var downloaded = await DownloadRealAsync();

            // ダウンロード成功。キャッシュに保存する。
            await SaveCacheFileAndMetaDataAsync(downloaded.MetaData, downloaded.Data);

            return downloaded;
        }
        catch
        {
            // ダウンロード失敗。すでにキャッシュされているデータがないかどうか調べる。もしあれば、それを返す。
            var cached = await TryLoadCacheFileAndMetaDataAsync();

            if (cached != null)
            {
                return cached;
            }

            // キャッシュがなければ、例外をそのまま返す
            throw;
        }

        // ファイルキャッシュに書き込みする関数 (なお、全く同一のファイルがすでに存在する場合は、キャッシュへの書き込みはしない)
        async Task SaveCacheFileAndMetaDataAsync(SimpleHttpDownloaderMetaData metaData, Memory<byte> data)
        {
            try
            {
                // ルートディレクトリがまだない場合は作成する。NTFS 圧縮フラグを有効にする。
                await fs.CreateDirectoryAsync(settings.CacheRootDirPath, FileFlags.OnCreateSetCompressionFlag, cancel);
            }
            catch { }

            await fs.CreateDirectoryAsync(dirPath, cancel: cancel);

            await fs.WriteDataToFileAsync(contentsPath, data, cancel: cancel, flags: FileFlags.WriteOnlyIfChanged);

            await fs.WriteJsonToFileAsync(metaDataPath, metaData, cancel: cancel, flags: FileFlags.WriteOnlyIfChanged);
        }

        // キャッシュされているファイルとメタデータを読み込んでみて問題なければデータを返す関数
        async Task<CachedDownloaderResult?> TryLoadCacheFileAndMetaDataAsync()
        {
            try
            {
                checked
                {
                    if (await fs.IsFileExistsAsync(metaDataPath, cancel) == false) return null;
                    if (await fs.IsFileExistsAsync(contentsPath, cancel) == false) return null;

                    var metaData = await fs.ReadJsonFromFileAsync<SimpleHttpDownloaderMetaData>(metaDataPath, cancel: cancel);
                    if (metaData == null) return null;

                    var fileData = await fs.ReadDataFromFileAsync(contentsPath, metaData.Size + 8, cancel: cancel);
                    if (fileData.Length != metaData.Size) return null;

                    string sha256 = Secure.HashSHA256(fileData.Span)._GetHexString();
                    if (Str.IsSameHex(sha256, metaData.HashSha256) == false) return null;

                    return new CachedDownloaderResult(fileData, new SimpleHttpDownloaderMetaData
                    {
                        Size = fileData.Length,
                        ContentType = metaData.ContentType,
                        MediaType = metaData.MediaType,
                        Url = metaData.Url,
                        HashSha256 = sha256,
                        TimeStamp = metaData.TimeStamp,
                    }, true);
                }
            }
            catch
            {
                return null;
            }
        }

        // 実際にサーバーからダウンロードを行なう関数
        async Task<CachedDownloaderResult> DownloadRealAsync()
        {
            return await RetryHelper.RunAsync(async () =>
            {
                var r = await SimpleHttpDownloader.DownloadAsync(url, WebMethods.GET, options: settings.WebOptions, cancel: cancel);

                CachedDownloaderResult ret = new CachedDownloaderResult(r.Data, new SimpleHttpDownloaderMetaData
                {
                    Size = r.Data.Length,
                    ContentType = r.ContentType,
                    MediaType = r.MediaType,
                    Url = url,
                    HashSha256 = Secure.HashSHA256(r.Data)._GetHexString(),
                    TimeStamp = DtOffsetNow,
                }, false);

                return ret;
            },
            tryCount: settings.MaxTry,
            retryInterval: settings.RetryInterval,
            cancel: cancel,
            randomInterval: true,
            noDebugMessage: true);
        }
    }
}

public class LinuxTimeDateCtlResults
{
    public DateTime UniversalTime = Util.ZeroDateTimeValue;
    public DateTime RtcTime = Util.ZeroDateTimeValue;
    public bool SystemClockSynchronized;
    public bool NtpServiceActive;
}

public static class LinuxTimeDateCtlUtil
{
    public static async Task<DateTimeOffset> ExecuteNtpDigAndReturnResultDateTimeAsync(string ntpServer, int timeoutMsecs = 5 * 1000, CancellationToken cancel = default)
    {
        if (timeoutMsecs <= 0) timeoutMsecs = 5 * 1000;

        var res = await EasyExec.ExecAsync(Lfs.UnixGetFullPathFromCommandName("ntpdig"), arguments: $"--samples=1 --timeout=1 {ntpServer}", cancel: cancel, timeout: timeoutMsecs);

        var line = res.OutputStr._GetLines(trim: true, removeEmpty: true).Where(x => x.StartsWith("20") || x.StartsWith("21")).FirstOrDefault();

        if (line._IsFilled())
        {
            string[] tokens = line._Split(StringSplitOptions.None, " ", "\t");

            if (tokens.Length >= 4)
            {
                DateTime dateTimeBase = Str.StrToDateTime(tokens[0] + " " + tokens[1]);

                string timezone = tokens[2];

                if (timezone.StartsWith("(") && timezone.EndsWith(")"))
                {
                    timezone = timezone._RemoveQuotation('(', ')');

                    bool? positive = null;

                    if (timezone.StartsWith("+"))
                    {
                        positive = true;
                    }
                    else if (timezone.StartsWith("-"))
                    {
                        positive = false;
                    }

                    if (positive != null)
                    {
                        timezone = timezone.Substring(1);

                        int timezoneHour = timezone.Substring(0, 2)._ToInt();
                        int timezoneMinute = timezone.Substring(2, 2)._ToInt();

                        TimeSpan offset = new TimeSpan(timezoneHour, timezoneMinute, 0);
                        if (positive == false) offset = -offset;

                        return new DateTimeOffset(dateTimeBase.Year, dateTimeBase.Month, dateTimeBase.Day, dateTimeBase.Hour, dateTimeBase.Minute, dateTimeBase.Second, offset).AddTicks(dateTimeBase.Ticks % 10000000L);
                    }
                }
            }
        }

        return Util.ZeroDateTimeOffsetValue;
    }

    public static async Task<LinuxTimeDateCtlResults> GetStateFromTimeDateCtlCommandAsync(CancellationToken cancel = default)
    {
        LinuxTimeDateCtlResults ret = new LinuxTimeDateCtlResults();

        var res = await EasyExec.ExecAsync(Lfs.UnixGetFullPathFromCommandName("timedatectl"), cancel: cancel);

        var lines = res.OutputStr._GetLines();

        foreach (string line in lines)
        {
            if (line._GetKeyAndValue(out string? key, out string? value, ":"))
            {
                key = key._NonNullTrim();
                value = value._NonNullTrim();

                if (key._IsSamei("Universal time"))
                {
                    ret.UniversalTime = StrToDateTime(value);
                }
                else if (key._IsSamei("RTC time"))
                {
                    ret.RtcTime = StrToDateTime(value);
                }
                else if (key._IsSamei("System clock synchronized"))
                {
                    ret.SystemClockSynchronized = value._ToBool();
                }
                else if (key._IsSamei("NTP service"))
                {
                    ret.NtpServiceActive = value._ToBool();
                }
            }
        }

        return ret;
    }

    static DateTime StrToDateTime(string str)
    {
        try
        {
            str = str._NonNullTrim();

            string[] tokens = str._Split(StringSplitOptions.None, " ", "\t");

            if (tokens.Length >= 3)
            {
                return Str.StrToDateTime(tokens[1] + " " + tokens[2]);
            }
        }
        catch { }

        return Util.ZeroDateTimeValue;
    }
}



public class DirQueueFileNameInfo
{
    public string FileName { get; private set; }
    public string MainName { get; private set; }
    public string Extension { get; private set; }
    public QueryStringList Params { get; private set; } = new QueryStringList();

    public DirQueueFileNameInfo(string fileName)
    {
        this.FileName = fileName;

        // aaaaaa dddd g=0s1a2a4s1 m=3
        string fn1 = PP.GetFileNameWithoutExtension(this.FileName);

        this.Extension = PP.GetExtension(this.FileName, emptyWhenNoExtension: true);

        string fnBodyPart = fn1;

        string[] tk1 = fn1._Split(StringSplitOptions.None, "=");
        if (tk1.Length >= 2)
        {
            // 'aaaaaa dddd g' '0s1a2a4s1 m' '3'
            string tmp1 = tk1[0];

            int i = tmp1.LastIndexOf(" ");
            if (i != -1)
            {
                // 'aaaaaa dddd'
                tmp1 = tmp1.Substring(0, i);
            }

            fnBodyPart = tmp1;

            // 'g=0s1a2a4s1 m=3'
            string paramPart = fn1.Substring(fnBodyPart.Length + 1);

            this.Params = paramPart._ParseQueryString(Str.Utf8Encoding, ' ', trimKeyAndValue: true);
        }

        this.MainName = fnBodyPart;
    }

    public DirQueueFileNameInfo(string srcFileName, IEnumerable<KeyValuePair<string, string>> newParams)
    {
        var tmp = new DirQueueFileNameInfo(srcFileName);

        tmp.Params = new QueryStringList(newParams);

        this.FileName = tmp.GenerateFileName();
        this.MainName = tmp.MainName;
        this.Extension = tmp.Extension;
        this.Params = tmp.Params;
    }

    public string GenerateFileName()
    {
        StringBuilder b = new StringBuilder();

        b.Append(this.MainName);
        if (this.Params.Count >= 1)
        {
            b.Append(" ");
            b.Append(this.Params.ToString(Str.Utf8Encoding));
        }

        b.Append(this.Extension);

        return b.ToString();
    }
}

public class DirQueueTxtFile
{
    public string FullPath { get; private set; } = "";
    public string MainBody { get; private set; } = "";
    public bool HasEof { get; private set; }
    public DirQueueFileNameInfo NameInfo { get; private set; } = null!;
    public KeyValueList<string, string> Options = new();

    private DirQueueTxtFile()
    { }

    public static async Task<DirQueueTxtFile> LoadFileAsync(string fullPath, string defaultTxtBody, CancellationToken cancel, bool doNotReadContents)
    {
        string body = (doNotReadContents == false) ? await Lfs.ReadStringFromFileAsync(fullPath, cancel: cancel) : "";

        DirQueueTxtFile txt = new DirQueueTxtFile();

        txt.FullPath = fullPath;

        var lines = body._GetLines();

        int mode = 0;

        StringWriter w = new StringWriter();
        w.NewLine = Str.NewLine_Str_Local;

        foreach (var line in lines)
        {
            if (line._IsSameTrimi("[EOF]"))
            {
                mode = 1;
                txt.HasEof = true;
            }
            else
            {
                if (mode == 0)
                {
                    w.WriteLine(line);
                }
                else if (mode == 1) // [EOF] の後は制御文
                {
                    if (line._IsFilled())
                    {
                        string line2 = line._StripCommentFromLine(commentMustBeWholeLine: true);

                        if (line2._GetKeyAndValue(out var key, out var value, " \t:"))
                        {
                            key = key.Trim();
                            value = value.Trim();
                            if (key._IsFilled() && value._IsFilled())
                            {
                                txt.Options.Add(key, value);
                            }
                        }
                    }
                }
            }
        }

        // _default.txt のオプション
        foreach (var line in defaultTxtBody._GetLines())
        {
            if (line._IsFilled())
            {
                string line2 = line._StripCommentFromLine(commentMustBeWholeLine: true);

                if (line2._GetKeyAndValue(out var key, out var value, " \t:"))
                {
                    key = key.Trim();
                    value = value.Trim();
                    if (key._IsFilled() && value._IsFilled())
                    {
                        txt.Options.Add(key, value);
                    }
                }
            }
        }

        txt.MainBody = w.ToString();

        txt.NameInfo = new DirQueueFileNameInfo(txt.FullPath);

        return txt;
    }
}

public class DirQueueTaskResultMetaData
{
    public bool IsOk;
    public TimeSpan TookTime;
    public int SuspendMSecs;
    public string AiSystem = "";
    public string AiModel = "";
}

public class DirQueueTaskResult
{
    public DirQueueTaskResultMetaData MetaData = new DirQueueTaskResultMetaData();
    public string Body = "";
}

public class DirQueueSettings
{
    public readonly string RootDirPath;
    public readonly string Extensions;
    public readonly bool RequireEofInInputs;

    public DirQueueSettings(string rootDirPath, string extensions = ".txt", bool requireEofInInputs = false)
    {
        this.RootDirPath = rootDirPath;
        this.Extensions = extensions;
        this.RequireEofInInputs = requireEofInInputs;
    }
}

public class DirQueueManager : AsyncServiceWithMainLoop
{
    public readonly DirQueueSettings Settings;
    public readonly string InDir;
    public readonly string RunDir;
    public readonly string OutSrcDir;
    public readonly string OutDstDir;
    public readonly string SkipDir;
    public readonly string LogDir;
    public readonly int MaxRun;
    public readonly FileLogger Logger;

    string CurrentDefaultTxtBody = "";

    public readonly ConcurrentQueue<int> WorkSlotList = new();

    public readonly Func<DirQueueManager, int, DirQueueTxtFile, CancellationToken, bool, Task<DirQueueTaskResult>> TaskProc;

    readonly SingleInstance Si;

    public DirQueueManager(Func<DirQueueManager, int, DirQueueTxtFile, CancellationToken, bool, Task<DirQueueTaskResult>> taskProc, DirQueueSettings settings, int maxRun)
    {
        try
        {
            this.TaskProc = taskProc;
            this.MaxRun = maxRun;
            this.Settings = settings;

            this.Si = new SingleInstance("DirQueue_" + PP.RemoveLastSeparatorChar(this.Settings.RootDirPath), true);

            this.InDir = PP.Combine(Settings.RootDirPath, "1_in");
            this.RunDir = PP.Combine(Settings.RootDirPath, "2_run");
            this.OutSrcDir = PP.Combine(Settings.RootDirPath, "3_out/1_src");
            this.OutDstDir = PP.Combine(Settings.RootDirPath, "3_out/2_dst");
            this.SkipDir = PP.Combine(Settings.RootDirPath, "4_skip");
            this.LogDir = PP.Combine(Settings.RootDirPath, "5_log");

            this.Logger = new FileLogger(this.LogDir);
            this.Logger.Flush = true;

            Lfs.CreateDirectory(this.InDir);
            Lfs.CreateDirectory(this.RunDir);
            Lfs.CreateDirectory(this.OutSrcDir);
            Lfs.CreateDirectory(this.OutDstDir);
            Lfs.CreateDirectory(this.SkipDir);
            Lfs.CreateDirectory(this.LogDir);

            for (int i = 0; i < this.MaxRun; i++)
            {
                this.WorkSlotList.Enqueue(i);
            }

            LastTimeSucessFlag = new bool[this.MaxRun];

            this.StartMainLoop(MainLoopAsync);
        }
        catch (Exception ex)
        {
            this._DisposeSafe(ex);
            throw;
        }
    }

    async Task MainLoopAsync(CancellationToken cancel)
    {
        while (this.GrandCancel.IsCancellationRequested == false)
        {
            try
            {
                await DoSingleLoopAsync(cancel);
            }
            catch (Exception ex)
            {
                ex._Error();
            }

            await cancel._WaitUntilCanceledAsync(Util.GenRandInterval(3000));
        }

        // 現在動作しているすべてのタスクの終了を待機
        List<Task> taskList = null!;
        lock (this.InRunTasksList)
        {
            taskList = this.InRunTasksList.ToList();
        }

        if (taskList.Count >= 1)
        {
            Con.WriteLine($"Waiting for running {taskList.Count} tasks...");
            foreach (var task in taskList)
            {
                await task._TryWaitAsync();
            }
        }

        // 終了時には run から in に戻す
        try
        {
            var runFiles = await EnumFilesInQueueAsync(default, this.RunDir, false, false);
            await RestoreRunFilesMoveToInFilesAsync(default, runFiles);
        }
        catch (Exception ex)
        {
            ex._Error();
        }
    }

    readonly bool[] LastTimeSucessFlag;

    Once StartedOnce = new Once();

    public List<Task> InRunTasksList = new();

    RefInt CurrentRunningTasks = new RefInt();

    DateTimeOffset SuspendUntil = ZeroDateTimeOffsetValue;

    async Task DoSingleLoopAsync(CancellationToken cancel)
    {
        LABEL_START:

        Lfs.CreateDirectory(this.InDir);
        Lfs.CreateDirectory(this.RunDir);
        Lfs.CreateDirectory(this.OutSrcDir);
        Lfs.CreateDirectory(this.OutDstDir);
        Lfs.CreateDirectory(this.SkipDir);
        Lfs.CreateDirectory(this.LogDir);

        // _default.txt の読み込み
        string defaultTxtPath = PP.Combine(this.Settings.RootDirPath, "_default.txt");

        var defaultTxt = "";
        if (await Lfs.IsFileExistsAsync(defaultTxtPath, cancel))
        {
            defaultTxt = await Lfs.ReadStringFromFileAsync(defaultTxtPath, cancel: cancel);
            defaultTxt = defaultTxt._NormalizeCrlf(ensureLastLineCrlf: true);
        }

        this.CurrentDefaultTxtBody = defaultTxt;

        var inFiles = await EnumFilesInQueueAsync(cancel, this.InDir, this.Settings.RequireEofInInputs, false);
        var runFiles = await EnumFilesInQueueAsync(cancel, this.RunDir, false, false);
        var outSrcFiles = await EnumFilesInQueueAsync(cancel, this.OutSrcDir, false, true);

        if (StartedOnce.IsFirstCall())
        {
            // 開始時には run に前回動作中のものが残留している可能性があるのですべて in に戻す
            await RestoreRunFilesMoveToInFilesAsync(cancel, runFiles);
            goto LABEL_START;
        }

        // inFiles にあるファイルのうち c=x の指定があるものをすべて i=000n の形に展開
        bool changed = false;
        foreach (var inFile in inFiles)
        {
            int c = inFile.NameInfo.Params._GetIntFirst("c");
            int index = inFile.NameInfo.Params._GetIntFirst("i");
            if (c >= 1 && index == 0)
            {
                for (int i = 0; i < c; i++)
                {
                    var plist = inFile.NameInfo.Params._CloneDeep();

                    plist.RemoveWhenKey("c", StrCmpi);
                    plist.Add("i", i.ToString("D4"));

                    DirQueueFileNameInfo newInfo = new DirQueueFileNameInfo(inFile.FullPath, plist);

                    string newFullPath = PP.Combine(this.InDir, newInfo.FileName);

                    Con.WriteLine($"Copying from '{inFile.FullPath}' to '{newFullPath}'...");
                    await Lfs.CopyFileAsync(inFile.FullPath, newFullPath, cancel: cancel);
                }

                Con.WriteLine($"Deleting '{inFile.FullPath}'...");
                await Lfs.DeleteFileIfExistsAsync(inFile.FullPath, cancel: cancel);
                changed = true;
            }
        }
        if (changed)
        {
            goto LABEL_START;
        }

        changed = false;
        // inFile にあるファイルのうち g=<グループ名> m=123 という属性がある場合、すでに 3_out\dst\ にある同一のグループ名のアイテム数が 123 個以上の場合は、4_skip にファイルを移動しスキップする
        foreach (var inFile in inFiles)
        {
            int groupMaxValue = inFile.NameInfo.Params._GetIntFirst("m");
            string groupName = inFile.NameInfo.Params._GetStrFirst("g");
            bool isGroupFile = false;

            if (groupName._IsFilled() && groupMaxValue >= 1)
            {
                isGroupFile = true;
            }

            List<DirQueueTxtFile> outDstFiles2 = new();

            if (isGroupFile)
            {
                // g=<グループ名> m=123 という属性がある場合、すでに 3_out\dst\ にある同一のグループ名のアイテム数が 123 個以上の場合は、4_skip にファイルを移動しスキップする。
                outDstFiles2 = await EnumFilesInQueueAsync(cancel, this.OutDstDir, false, true);

                int existSameGroupInOutDir = outDstFiles2.Where(x => x.NameInfo.Params._GetStrFirst("g")._IsSamei(groupName)).Count();
                if (existSameGroupInOutDir >= groupMaxValue)
                {
                    var now = DtOffsetNow;
                    string timestamp = $"{now._ToYymmddStr(yearTwoDigits: true)}_{now._ToHhmmssStr().Substring(0, 4)}";
                    string skipDstPath = PP.Combine(this.SkipDir, timestamp + "-" + PP.GetFileName(inFile.FullPath));
                    await Lfs.MoveFileAutoIncNameAsync(inFile.FullPath, skipDstPath, cancel);

                    string msg = $"Skip: Number of existing files in the output dir: {existSameGroupInOutDir} >= {groupMaxValue}  (Group name: {groupName})";
                    this.Logger.Write(skipDstPath, msg);

                    Con.WriteLine(msg);

                    changed = true;
                }
            }
        }
        if (changed)
        {
            goto LABEL_START;
        }

        // inFiles にあるファイルについて、p= の値で優先順位の値の高い別に並べる (同一優先順位であればランダムにシャッフルする)
        HashSet<int> prioritySet = new HashSet<int>();
        foreach (var inFile in inFiles)
        {
            int p = inFile.NameInfo.Params._GetIntFirst("p");
            prioritySet.Add(p);
        }
        var priorityList = prioritySet.OrderByDescending(x => x).ToArray();
        List<DirQueueTxtFile> tmpList = new();
        foreach (int p in priorityList)
        {
            var tmp1 = inFiles.Where(x => x.NameInfo.Params._GetIntFirst("p") == p).ToList()._Shuffle().ToList();
            tmpList.AddRange(tmp1);
        }
        var inFilesShuffled = new Queue<DirQueueTxtFile>(tmpList);

        // inFiles にあるファイルを、maxRun の数に満ちるまで次々に実行
        while (true)
        {
            if (inFilesShuffled.TryDequeue(out var target) == false)
            {
                // これ以上実行すべきものがない
                break;
            }

            var now2 = DtOffsetNow;

            if (now2 < SuspendUntil)
            {
                // サスペンド中
                break;
            }

            int maxRun = target.Options._GetIntFirst("maxrun");

            List<DirQueueTxtFile> runFiles2 = await EnumFilesInQueueAsync(cancel, this.RunDir, false, false);

            if (maxRun >= 1 && CurrentRunningTasks >= maxRun)
            {
                // maxrun で指定されている数以上は同時に実行しない
                break;
            }

            int groupMaxValue = target.NameInfo.Params._GetIntFirst("m");
            string groupName = target.NameInfo.Params._GetStrFirst("g");
            bool isGroupFile = false;

            if (groupName._IsFilled() && groupMaxValue >= 1)
            {
                isGroupFile = true;
            }

            List<DirQueueTxtFile> outDstFiles2 = new();

            if (isGroupFile)
            {
                outDstFiles2 = await EnumFilesInQueueAsync(cancel, this.OutDstDir, false, true);
            }

            // g=<グループ名> m=123 という属性がある場合、すでに 2_run\ および 3_out\dst\ にある同一のグループ名のアイテム数の合計が 123 個以上の場合は何もしない。次のファイルを選択する。
            if (isGroupFile)
            {
                int existSameGroupInOutDir = outDstFiles2.Where(x => x.NameInfo.Params._GetStrFirst("g")._IsSamei(groupName)).Count();
                int existSameGroupInRunDir = runFiles2.Where(x => x.NameInfo.Params._GetStrFirst("g")._IsSamei(groupName)).Count();
                if ((existSameGroupInOutDir + existSameGroupInRunDir) >= groupMaxValue)
                {
                    continue;
                }
            }

            // 空きスロットがあるか？
            if (WorkSlotList.TryDequeue(out int currentWorkSlot) == false)
            {
                // 空きスロットがない
                break;
            }
            else
            {
                try
                {
                    // ファイルを in から run に移動
                    string newFullPath = PP.Combine(this.RunDir, PP.GetFileName(target.FullPath));
                    await Lfs.MoveFileAutoIncNameAsync(target.FullPath, newFullPath, cancel);
                    Con.WriteLine($"Slot {currentWorkSlot}: Task begin. '{target.FullPath}' moved to '{newFullPath}'");

                    AsyncManualResetEvent startSignal = new AsyncManualResetEvent();
                    Ref<int> assignenTaskIdHolder = new Ref<int>();
                    Ref<int> assignedWorkSlotHolder = new Ref<int>(currentWorkSlot);

                    Con.WriteLine("CurrentRunningTasks = " + CurrentRunningTasks.Increment());

                    // ファイルの移動が完了したら処理を開始
                    var task = TaskUtil.StartAsyncTaskAsync(async () =>
                    {
                        await startSignal.WaitAsync(Timeout.Infinite, cancel);

                        await Task.Yield();

                        int thisTaskId = assignenTaskIdHolder.Value;
                        int currentWorkSlot = assignedWorkSlotHolder.Value;

                        try
                        {
                            // タスクのメインルーチンを呼び出す
                            var result = await this.TaskProc(this, currentWorkSlot, target, cancel, this.LastTimeSucessFlag[currentWorkSlot]);

                            if (result.MetaData.IsOk == false)
                            {
                                this.LastTimeSucessFlag[currentWorkSlot] = false;

                                // アプリケーションレベルのエラーが返ってきた
                                this.Logger.Write(newFullPath, "Error (Application): " + result.Body._OneLine() + $", Took time = '{result.MetaData.TookTime._ToTsStr(true, false)}'");

                                if (result.MetaData.SuspendMSecs >= 1)
                                {
                                    DateTimeOffset suspendUntil = DtOffsetNow.AddMilliseconds(result.MetaData.SuspendMSecs);

                                    this.SuspendUntil = suspendUntil;

                                    this.Logger.Write(newFullPath, $"Suspend {(result.MetaData.SuspendMSecs / 1000)._ToString3()} secs until: " + suspendUntil._ToLocalDtStr());

                                    Con.WriteLine($"Slot {currentWorkSlot}: Suspend {(result.MetaData.SuspendMSecs / 1000)._ToString3()} secs until: " + suspendUntil._ToLocalDtStr());
                                }

                                Con.WriteLine($"Slot {currentWorkSlot}: Application-level error. '{newFullPath}'. Error = {result.Body.ToString()}" + $", Took time = '{result.MetaData.TookTime._ToTsStr(true, false)}'");

                                // run から in に戻す
                                try
                                {
                                    await Lfs.MoveFileAutoIncNameAsync(newFullPath, PP.Combine(this.InDir, PP.GetFileName(target.FullPath)), cancel);
                                }
                                catch (Exception ex2)
                                {
                                    ex2._Error();
                                }
                            }
                            else
                            {
                                this.LastTimeSucessFlag[currentWorkSlot] = true;

                                Con.WriteLine($"Slot {currentWorkSlot}: Task completed. '{newFullPath}'");

                                // 成功したので結果を書き出す
                                var now = DtOffsetNow;
                                string timestamp = $"{now._ToYymmddStr(yearTwoDigits: true)}_{now._ToHhmmssStr().Substring(0, 4)}";

                                bool srcFileNameHasTimeStamp = false;
                                string tmp2 = PP.GetFileName(target.FullPath);
                                if (tmp2.Length >= 6)
                                {
                                    if (tmp2.Substring(0, 6)._ToInt() >= 200000)
                                    {
                                        srcFileNameHasTimeStamp = true;
                                    }
                                }

                                string outputPath = PP.Combine(this.OutDstDir, (srcFileNameHasTimeStamp ? "" : timestamp + "-") + PP.GetFileName(target.FullPath));

                                string eoai_title = GetStringFromTag(result.Body, "EOAI_TITLE");
                                if (eoai_title._IsFilled())
                                {
                                    string tmpDir = PP.GetDirectoryName(outputPath);
                                    string tmpFn = PP.GetFileNameWithoutExtension(outputPath);
                                    string tmpExt = PP.GetExtension(outputPath, emptyWhenNoExtension: true);

                                    tmpFn += "-" + PP.MakeSafeFileName(eoai_title, true, true, true).Replace("-", "_")._TruncStrEx(32, "･･･");

                                    outputPath = PP.Combine(tmpDir, tmpFn + tmpExt);
                                }

                                {
                                    // ファイル名に文字数を入れる
                                    string tmpDir = PP.GetDirectoryName(outputPath);
                                    string tmpFn = PP.GetFileNameWithoutExtension(outputPath);
                                    string tmpExt = PP.GetExtension(outputPath, emptyWhenNoExtension: true);

                                    tmpFn += "-" + result.Body.Length.ToString() + "文字";

                                    outputPath = PP.Combine(tmpDir, tmpFn + tmpExt);
                                }

                                string writeBody = result.Body._NormalizeCrlf(true);
                                writeBody += "EOAI_METADATA:" + result.MetaData._ObjectToJson(compact: true) + Str.NewLine_Str_Local;

                                await Lfs.WriteStringToFileAsync(outputPath, writeBody, writeBom: true, cancel: cancel);

                                string outputSrcPath = PP.Combine(this.OutSrcDir, timestamp + "-" + PP.GetFileName(target.FullPath));
                                await Lfs.MoveFileAutoIncNameAsync(newFullPath, outputSrcPath, cancel);

                                this.Logger.Write(newFullPath, $"OK: Took time = '{result.MetaData.TookTime._ToTsStr(true, false)}', Filename = '{outputPath}', Src = '{outputSrcPath}'");

                                static string GetStringFromTag(string body, string tag)
                                {
                                    string tag2 = Str.ZenkakuToHankaku(tag);

                                    foreach (var line in body._GetLines())
                                    {
                                        int i = line.IndexOf(tag2 + ":", StringComparison.OrdinalIgnoreCase);
                                        if (i != -1)
                                        {
                                            string tmp1 = line.Substring(i + tag2.Length + 1).Trim();
                                            if (tmp1._IsFilled())
                                            {
                                                int j = tmp1.IndexOf("EOAI_", StringComparison.OrdinalIgnoreCase);
                                                if (j != -1)
                                                {
                                                    tmp1 = tmp1.Substring(0, j);
                                                }
                                                return tmp1.Trim().Trim(':').Trim().Trim(':').Trim().Trim(':');
                                            }
                                        }

                                        i = line.IndexOf(tag2 + " ", StringComparison.OrdinalIgnoreCase);
                                        if (i != -1)
                                        {
                                            string tmp1 = line.Substring(i + tag2.Length + 1).Trim();
                                            if (tmp1._IsFilled())
                                            {
                                                int j = tmp1.IndexOf("EOAI_", StringComparison.OrdinalIgnoreCase);
                                                if (j != -1)
                                                {
                                                    tmp1 = tmp1.Substring(0, j);
                                                }
                                                return tmp1.Trim().Trim(':').Trim().Trim(':').Trim().Trim(':');
                                            }
                                        }
                                    }

                                    return "";
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            // タスクのメインルーチンでエラーが発生した
                            this.Logger.Write(newFullPath, "Error (Internal): " + ex.ToString()._OneLine());

                            Con.WriteLine($"Slot {currentWorkSlot}: Task error. '{newFullPath}'. Error = {ex.ToString()}");

                            // run から in に戻す
                            try
                            {
                                await Lfs.MoveFileAutoIncNameAsync(newFullPath, PP.Combine(this.InDir, PP.GetFileName(target.FullPath)), cancel);
                            }
                            catch (Exception ex2)
                            {
                                ex2._Error();
                            }
                        }
                        finally
                        {
                            Con.WriteLine("CurrentRunningTasks = " + CurrentRunningTasks.Decrement());

                            try
                            {
                                bool ok = false;
                                lock (InRunTasksList)
                                {
                                    var taskToDelete = InRunTasksList.Where(x => x.Id == thisTaskId).ToList();

                                    foreach (var t in taskToDelete)
                                    {
                                        if (InRunTasksList.Remove(t)) ok = true;
                                    }
                                }
                                if (ok == false)
                                {
                                    throw new CoresLibException($"No task found in InRunTasksList. Task id = {thisTaskId}");
                                }
                            }
                            catch (Exception ex)
                            {
                                ex._Error();
                            }

                            WorkSlotList.Enqueue(currentWorkSlot);
                        }
                    });

                    lock (InRunTasksList)
                    {
                        InRunTasksList.Add(task);
                    }

                    assignenTaskIdHolder.Set(task.Id);

                    startSignal.Set(true);
                }
                catch (Exception ex)
                {
                    ex._Error();
                    this.Logger.Write(target.FullPath, ex.ToString()._OneLine());

                    WorkSlotList.Enqueue(currentWorkSlot);
                }
            }
        }
    }

    async Task RestoreRunFilesMoveToInFilesAsync(CancellationToken cancel, List<DirQueueTxtFile> runFiles)
    {
        foreach (var runFile in runFiles)
        {
            try
            {
                await Lfs.MoveFileAutoIncNameAsync(runFile.FullPath, PP.Combine(this.InDir, PP.GetFileName(runFile.FullPath)));
            }
            catch (Exception ex)
            {
                ex._Error();
            }
        }
    }

    protected async Task<List<DirQueueTxtFile>> EnumFilesInQueueAsync(CancellationToken cancel, string dir, bool checkEof, bool doNotReadContents)
    {
        List<DirQueueTxtFile> ret = new();

        var fileList = await Lfs.EnumDirectoryAsync(dir, false, cancel: cancel);

        foreach (var file in fileList.Where(x => x.IsFile && x.Name._IsExtensionMatch(this.Settings.Extensions)).OrderBy(x => x.FullPath, StrCmpi))
        {
            cancel.ThrowIfCancellationRequested();

            try
            {
                DirQueueTxtFile txt = await DirQueueTxtFile.LoadFileAsync(file.FullPath, this.CurrentDefaultTxtBody, cancel, (!checkEof) && doNotReadContents);

                bool ok = true;

                if (checkEof)
                {
                    if (txt.HasEof == false)
                    {
                        ok = false;
                    }
                }

                if (ok)
                {
                    ret.Add(txt);
                }
            }
            catch (Exception ex)
            {
                ex._Error();
            }
        }

        return ret;
    }

    protected override async Task CleanupImplAsync(Exception? ex)
    {
        try
        {
            await this.Si._DisposeSafeAsync2();
        }
        finally
        {
            await base.CleanupImplAsync(ex);
        }
    }
}


public class GitLabDownloaderDownloadPageInfo
{
    public string SpecifiedVersion { get; }
    public string Url { get; }

    public GitLabDownloaderDownloadPageInfo(string specifiedVersion, string url)
    {
        SpecifiedVersion = specifiedVersion;
        Url = url;
    }
}

public class GitLabDownloaderDownloadFileInfo
{
    public string SpecifiedVersion { get; }
    public string FileName { get; }
    public string Url { get; }
    public StrDictionary<string> Checksums { get; }

    public string GetSHA1Checksum() => Checksums._GetStrFirst("SHA1");

    public GitLabDownloaderDownloadFileInfo(string specifiedVersion, string fileName, string url, StrDictionary<string> checksums)
    {
        SpecifiedVersion = specifiedVersion;
        FileName = fileName;
        Url = url;
        Checksums = checksums;
    }
}

public class GitLabDownloaderEnumeratePackagePagesOptions
{
    public string SearchBaseUrl { get; }
    public IEnumerable<string> VersionStr { get; }

    public GitLabDownloaderEnumeratePackagePagesOptions(string searchBaseUrl, string versionStr)
    {
        this.SearchBaseUrl = searchBaseUrl;
        this.VersionStr = versionStr.Trim()._NotEmptyCheck()._SingleList();
    }

    public GitLabDownloaderEnumeratePackagePagesOptions(string searchBaseUrl, IEnumerable<string> versionStrList)
    {
        this.SearchBaseUrl = searchBaseUrl;
        this.VersionStr = versionStrList.Select(x => x.Trim()._NotEmptyCheck()).Distinct(StrCmpi).ToList().ToList();
    }

    public KeyValueList<string, string> GetVersionStringForSearch()
    {
        KeyValueList<string, string> ret = new();
        foreach (string key in this.VersionStr)
        {
            {
                string ver = key;
                if (ver.EndsWith("-") == false)
                {
                    ver = ver + "-";
                }

                if (ver.StartsWith("-") == false)
                {
                    ver = "-" + ver;
                }

                if (ver.Length >= 4)
                {
                    ret.Add(key, ver);
                }
            }
            {
                string ver = key;
                if (ver.EndsWith("_") == false)
                {
                    ver = ver + "_";
                }

                if (ver.StartsWith("-") == false)
                {
                    ver = "-" + ver;
                }

                if (ver.Length >= 4)
                {
                    ret.Add(key, ver);
                }
            }
            {
                string ver = key;
                if (ver.EndsWith("-") == false)
                {
                    ver = ver + "-";
                }

                if (ver.StartsWith("_") == false)
                {
                    ver = "_" + ver;
                }

                if (ver.Length >= 4)
                {
                    ret.Add(key, ver);
                }
            }
        }
        return ret;
    }

    string NormalizeVersionStr(string ver)
    {
        if (ver._IsEmpty()) throw new CoresLibException(nameof(ver));

        if (ver.EndsWith("-") == false)
        {
            ver += "-";
        }

        if (ver.StartsWith("-") == false)
        {
            ver = "-" + ver;
        }

        return ver;
    }

}

public class GitLabDownloader : AsyncService
{
    readonly WebApi WebClient;

    public GitLabDownloader()
    {
        try
        {
            this.WebClient = new WebApi(new WebApiOptions(new WebApiSettings { SslAcceptAnyCerts = true }, doNotUseTcpStack: true));
        }
        catch (Exception ex)
        {
            this._DisposeSafe(ex);
            throw;
        }
    }

    public async Task<GitLabDownloaderDownloadFileInfo> DownloadPackageFileAsync(DirectoryPath destDir, GitLabDownloaderDownloadFileInfo fileInfo, RefLong? fileSize = null, CancellationToken cancel = default)
    {
        var destFile = destDir.Combine(fileInfo.FileName);

        return await TaskUtil.RetryAsync(async () =>
        {
            var okRead = await destFile.FileSystem.ReadOkFileAsync<GitLabDownloaderDownloadFileInfo>(destFile, fileInfo.GetSHA1Checksum(), cancel: cancel);

            if (okRead.IsOk && okRead.Value != null)
            {
                Con.WriteLine($"'{destDir}' already exists. Skip.");

                fileSize?.Set((await destFile.GetFileMetadataAsync(cancel: cancel)).Size);
                return okRead.Value;
            }

            Con.WriteLine($"Downloading '{fileInfo.Url}'...");

            using var reporter = new ProgressReporter(new ProgressReporterSetting(fileSizeStr: true));

            await using var a = await SimpleHttpDownloader.DownloadToFileAsync(destFile, fileInfo.Url, reporter: reporter, options: new WebApiOptions(new WebApiSettings { SslAcceptAnyCerts = true }, doNotUseTcpStack: true));

            Con.WriteLine($"Download '{fileInfo.Url}' completed. File size = {a.DataSize!.Value._ToString3()}");

            Con.WriteLine($"Checking the checksum... (Original SHA1 = {fileInfo.GetSHA1Checksum()._NormalizeHexString()})");

            var cacled = await FileUtil.CalcFileHashAsync(destFile, System.Security.Cryptography.SHA1.Create(), bufferSize: 1024 * 1024, cancel: cancel);
            string calcedStr = cacled._GetHexString();
            Con.WriteLine($"                        (Real file SHA1 = {calcedStr})");

            if (calcedStr._IsSameHex(fileInfo.GetSHA1Checksum()._NormalizeHexString()) == false)
            {
                Con.WriteError($" ** Error: Invalid checksum.");
                throw new CoresRetryableException(" ** Error: Invalid checksum.");
            }
            Con.WriteLine($"Checksum OK. Download completed: '{destFile}'");

            await destFile.FileSystem.WriteOkFileAsync(destFile, fileInfo, fileInfo.GetSHA1Checksum(), cancel: cancel);

            fileSize?.Set(a.DataSize!.Value);

            return fileInfo;

        }, 100, 5, cancel, true);
    }

    public async Task<GitLabDownloaderDownloadFileInfo> GetDownloadUrlFromPageAsync(GitLabDownloaderDownloadPageInfo pageInfo, CancellationToken cancel = default)
    {
        Con.WriteLine($"Accessing the file page URL '{pageInfo.Url}'...");

        WebRet result = await TaskUtil.RetryAsync(async () =>
        {
            return await WebClient.SimpleQueryAsync(WebMethods.GET, pageInfo.Url, cancel);
        }, 100, 5, cancel, true);

        string htmlText = result.Data._GetString_UTF8();

        var html = htmlText._ParseHtml();

        var downloadFilenameTag = html.DocumentNode.SelectSingleNode("//div[@class='main-package-details']//h1");

        string downloadFilename = downloadFilenameTag.InnerText.Trim();

        var downloadLink = html.DocumentNode.SelectSingleNode("//div[@class='package-path']/following-sibling::div//a");

        string downloadUrl = downloadLink.Attributes.Where(x => x.Name._IsSamei("href")).First().Value;

        if (downloadUrl._InStri(downloadFilename) == false)
        {
            throw new CoresLibException($"downloadUrl '{downloadUrl}' does not contain '{downloadFilename}'.");
        }

        downloadFilename = PPWin.MakeSafeFileName(downloadFilename, true, true, true);

        var checksumTable = html.DocumentNode.SelectSingleNode("//div[contains(@class, 'checksums')]/table").ParseTable(new HtmlTableParseOption(new string[] { "Type", "Value" }, findTBody: true));

        StrDictionary<string> checksums = new();

        foreach (var row in checksumTable.DataList)
        {
            checksums.Add(row["Type"].SimpleText.Trim(), row["Value"].SimpleText.Trim());
        }

        return new GitLabDownloaderDownloadFileInfo(pageInfo.SpecifiedVersion, downloadFilename, downloadUrl, checksums);
    }

    public async Task<List<GitLabDownloaderDownloadPageInfo>> EnumeratePackagePagesAsync(GitLabDownloaderEnumeratePackagePagesOptions options, CancellationToken cancel = default)
    {
        List<GitLabDownloaderDownloadPageInfo> ret = new();

        var verItemList = options.GetVersionStringForSearch();

        foreach (var verStr in verItemList.Select(x => x.Key).Distinct(StrCmpi).OrderBy(x => x, StrCmpi))
        {
            var verStrForSearchList = verItemList.Where(x => x.Key._IsSamei(verStr)).Select(x => x.Value).Distinct(StrCmpi).OrderBy(x => x, StrCmpi);

            foreach (var varStrForSearch in verStrForSearchList)
            {
                for (int i = 1; ; i++)
                {
                    string url = $"{options.SearchBaseUrl}?dist=&filter=all&page={i}&q={varStrForSearch}";

                    Con.WriteLine($"Accessing the list URL '{url}'...");

                    WebRet result = await TaskUtil.RetryAsync(async () =>
                    {
                        return await WebClient.SimpleQueryAsync(WebMethods.GET, url, cancel);
                    }, 100, 5, cancel, true);

                    string htmlText = result.Data._GetString_UTF8();

                    var html = htmlText._ParseHtml();

                    try
                    {
                        var table = html.ParseTable("//table[@class='table-minimal basic results']", new HtmlTableParseOption(findTBody: true));

                        foreach (var row in table.DataList)
                        {
                            var link = row["Name"].TdNode.SelectSingleNode(".//a[1]");
                            string href = link.Attributes.Where(x => x.Name._IsSamei("href")).First().Value;

                            href = url._CombineUrl(href).ToString();

                            if (ret.Where(x => x.Url._IsSamei(href)).Any() == false) // 重複排除
                            {
                                string tmp1 = verStr;
                                if (tmp1._IsEmpty()) tmp1 = "_unknown";
                                ret.Add(new(verStr, href));
                            }
                        }
                    }
                    catch (NullReferenceException)
                    {
                        // これ以上なし
                        break;
                    }
                }
            }
        }

        return ret.ToList();
    }

    protected override async Task CleanupImplAsync(Exception? ex)
    {
        try
        {
            await this.WebClient._DisposeSafeAsync();
        }
        finally
        {
            await base.CleanupImplAsync(ex);
        }
    }
}

#endif

