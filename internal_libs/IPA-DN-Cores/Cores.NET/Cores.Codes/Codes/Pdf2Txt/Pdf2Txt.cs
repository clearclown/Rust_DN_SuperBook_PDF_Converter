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

// <PackageReference Include="PdfPig" Version="0.1.9" />

#if CORES_CODES_PDF2TXT

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
using System.Runtime.Serialization;
using System.Runtime.CompilerServices;

using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Writer;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using UglyToad.PdfPig.Tokens;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

using IPA.Cores.Codes;
using IPA.Cores.Helper.Codes;
using static IPA.Cores.Globals.Codes;
using System.Text.RegularExpressions;
using System.Globalization;

namespace IPA.Cores.Codes;

public static class Pdf2Txt
{
    public const int DefaultMaxPdfFileSize = 2_100_000_000;

    public static async Task<PdfDocument> LoadPdfFromFileAsync(string fileName, int maxSize = DefaultMaxPdfFileSize, FileFlags flags = FileFlags.None, FileSystem? fs = null, CancellationToken cancel = default)
    {

        fs ??= Lfs;

        var data = await fs.ReadDataFromFileAsync(fileName, maxSize, flags, cancel);

        return LoadPdf(data);
    }
    public static PdfDocument LoadPdfFromFile(string fileName, int maxSize = DefaultMaxPdfFileSize, FileFlags flags = FileFlags.None, FileSystem? fs = null, CancellationToken cancel = default)
        => LoadPdfFromFileAsync(fileName, maxSize, flags, fs, cancel)._GetResult();

    public static PdfDocument LoadPdf(ReadOnlyMemory<byte> pdfBody)
    {
        return PdfDocument.Open(pdfBody.ToArray());
    }

    public static byte[] SavePdf_Shitagaki(this PdfDocument pdf)
    {
        using var builder = new PdfDocumentBuilder();

        foreach (var page in pdf.GetPages())
        {
            builder.AddPage(pdf, page.Number);
        }

        builder.DocumentInformation.CustomMetadata._PrintAsJson();

        return builder.Build();
    }

    public static async Task<string> ExtraceTextFromPdfAsync(FilePath filePath, int maxSize = DefaultMaxPdfFileSize, CancellationToken cancel = default)
    {
        using var pdf = await LoadPdfFromFileAsync(filePath, flags: filePath.Flags, fs: filePath.FileSystem, cancel: cancel);

        return pdf.ExtractTextFromPdf();
    }
    public static Task<string> ExtraceTextFromPdfAsync(string filePath, int maxSize = DefaultMaxPdfFileSize, CancellationToken cancel = default)
        => ExtraceTextFromPdfAsync(new FilePath(filePath), maxSize, cancel);

    public static async Task<PdfDocInfo> GetDocInfoFromPdfFileAsync(string pdfPath, CancellationToken cancel = default)
    {
        using var pdf = await LoadPdfFromFileAsync(pdfPath, cancel: cancel);

        return GetDocInfoFromPdf(pdf);
    }

    public static PdfDocInfo GetDocInfoFromPdf(this PdfDocument doc)
    {
        PdfDocInfo ret = new();

        // 物理 / 論理ページ対応データ
        try
        {
            // 1) 物理ページ枚数
            int n = doc.NumberOfPages;

            // 2) /PageLabels をカタログから取得（無ければ物理=論理(1..N)）
            var catalogDict = doc.Structure.Catalog.CatalogDictionary;

            if (TryGetCatalogEntry(catalogDict, "PageLabels", out var pageLabelsRefOrDict))
            {
                var entries = new SortedDictionary<int, DictionaryToken>();

                var pageLabelsDict = DerefToDictionary(doc.Structure, pageLabelsRefOrDict);

                if (pageLabelsDict != null)
                {

                    ReadNumberTree(doc.Structure, pageLabelsDict, entries);

                    // PDF仕様上、0ページ目の定義が無い場合もあるのでフォールバックを入れる
                    if (!entries.ContainsKey(0))
                    {
                        entries[0] = new DictionaryToken(new Dictionary<NameToken, IToken>());
                    }

                    // 4) 範囲にして各ページのラベルを生成
                    var starts = entries.Keys.OrderBy(x => x).ToArray();
                    var result = new (int PhysicalPage1Based, string LogicalLabel)[n];

                    for (int si = 0; si < starts.Length; si++)
                    {
                        int start = starts[si];
                        int endExclusive = (si + 1 < starts.Length) ? Math.Min(starts[si + 1], n) : n;

                        var spec = entries[start];
                        var prefix = GetOptionalString(spec, "P") ?? "";
                        var style = GetOptionalName(spec, "S");         // D / R / r / A / a など
                        var st = GetOptionalInt(spec, "St") ?? 1;

                        for (int pageIndex0 = start; pageIndex0 < endExclusive; pageIndex0++)
                        {
                            int offset = pageIndex0 - start;
                            int value = st + offset;
                            string core = FormatNumber(value, style);
                            result[pageIndex0] = (pageIndex0 + 1, prefix + core);
                        }
                    }

                    var mappingTable = result.OrderBy(x => x.PhysicalPage1Based).ToList();

                    foreach (var thisPageInfo in mappingTable)
                    {
                        int logicalPageNumber = thisPageInfo.LogicalLabel._ToInt();

                        if (logicalPageNumber >= 1 && thisPageInfo.PhysicalPage1Based != logicalPageNumber)
                        {
                            ret.PhysicalPageStart = thisPageInfo.PhysicalPage1Based;
                            ret.LogicalPageStart = logicalPageNumber;
                            break;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            ex._Error();
        }

        // 日時データ
        try
        {
            ret.CreateDt = Pdf2Txt.PdfDateTimeStringToDtOffset(doc.Information.CreationDate ?? "");
        }
        catch (Exception ex)
        {
            ex._Error();
        }

        try
        {
            ret.ModifyDt = Pdf2Txt.PdfDateTimeStringToDtOffset(doc.Information.ModifiedDate ?? "");
        }
        catch (Exception ex)
        {
            ex._Error();
        }

        // 縦書きフラグ
        try
        {
            // 2) /PageLabels をカタログから取得（無ければ物理=論理(1..N)）
            var catalog = doc.Structure.Catalog.CatalogDictionary;

            if (catalog.Data.TryGetValue(NameToken.Create("ViewerPreferences"), out var vpTok))
            {
                var vpDict = DerefToDictionary(doc, vpTok);
                if (vpDict != null)
                {
                    if (vpDict.Data.TryGetValue(NameToken.Create("Direction"), out var dirTok))
                    {
                        if (dirTok is NameToken name)
                        {
                            if (name.Data._IsSamei("R2L"))
                            {
                                ret.IsVertical = true;
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            ex._Error();
        }

        return ret;
    }

    static DictionaryToken? DerefToDictionary(PdfDocument doc, IToken tok)
    {
        if (tok is IndirectReferenceToken ir)
        {
            var obj = doc.Structure.GetObject(ir.Data);
            return obj?.Data as DictionaryToken;
        }
        return tok as DictionaryToken;
    }

    private static string ToRoman(int n)
    {
        if (n <= 0) return n.ToString();
        var map = new (int v, string s)[] {
            (1000,"M"),(900,"CM"),(500,"D"),(400,"CD"),
            (100,"C"),(90,"XC"),(50,"L"),(40,"XL"),
            (10,"X"),(9,"IX"),(5,"V"),(4,"IV"),(1,"I")
        };
        var r = "";
        foreach (var (v, s) in map)
            while (n >= v) { r += s; n -= v; }
        return r;
    }

    private static string ToAlpha(int n)
    {
        // 1->A, 26->Z, 27->AA ...
        if (n <= 0) return n.ToString();
        var s = "";
        while (n > 0)
        {
            n--;
            s = (char)('A' + (n % 26)) + s;
            n /= 26;
        }
        return s;
    }

    private static string FormatNumber(int value, NameToken? style)
    {
        // style が無い or /D は通常の10進
        if (style == null) return value.ToString();

        switch (style.Data) // "D","R","r","A","a"
        {
            case "D": return value.ToString();
            case "R": return ToRoman(value).ToUpperInvariant();
            case "r": return ToRoman(value).ToLowerInvariant();
            case "A": return ToAlpha(value).ToUpperInvariant();
            case "a": return ToAlpha(value).ToLowerInvariant();
            default: return value.ToString();
        }
    }
    private static NameToken? GetOptionalName(DictionaryToken dict, string key)
    {
        if (!dict.Data.TryGetValue(NameToken.Create(key), out var tok)) return null;
        return tok as NameToken;
    }

    private static int? GetOptionalInt(DictionaryToken dict, string key)
    {
        if (!dict.Data.TryGetValue(NameToken.Create(key), out var tok)) return null;
        return (tok as NumericToken)?.Int;
    }

    private static string? GetOptionalString(DictionaryToken dict, string key)
    {
        if (!dict.Data.TryGetValue(NameToken.Create(key), out var tok)) return null;
        return tok switch
        {
            StringToken s => s.Data,
            HexToken h => h.Data, // 実際は必要に応じてデコード
            _ => null
        };
    }

    private static void ReadNumberTree(Structure structure, DictionaryToken numberTree,
        SortedDictionary<int, DictionaryToken> output)
    {
        // /Nums があれば [key value key value ...]
        if (numberTree.Data.TryGetValue(NameToken.Create("Nums"), out var numsTok) && numsTok is ArrayToken numsArr)
        {
            var items = numsArr.Data;
            for (int i = 0; i + 1 < items.Count; i += 2)
            {
                int key = (items[i] as NumericToken)?.Int ?? 0;
                var valDict = DerefToDictionary(structure, items[i + 1]) ?? (items[i + 1] as DictionaryToken);
                if (valDict != null) output[key] = valDict;
            }
            return;
        }

        // /Kids があれば再帰
        if (numberTree.Data.TryGetValue(NameToken.Create("Kids"), out var kidsTok) && kidsTok is ArrayToken kidsArr)
        {
            foreach (var kid in kidsArr.Data)
            {
                var kidDict = DerefToDictionary(structure, kid);
                if (kidDict != null) ReadNumberTree(structure, kidDict, output);
            }
        }
    }

    private static DictionaryToken? DerefToDictionary(Structure structure, IToken token)
    {
        if (token is IndirectReferenceToken ir)
        {
            var obj = structure.GetObject(ir.Data);
            return obj?.Data as DictionaryToken;
        }
        return token as DictionaryToken;
    }

    private static bool TryGetCatalogEntry(DictionaryToken dict, string key, out IToken token)
    {
        // NameToken.Create が無い場合は実装に合わせて生成
        var name = NameToken.Create(key);
        return dict.Data.TryGetValue(name, out token);
    }

    public static string ExtractTextFromPdf(this PdfDocument pdf)
    {
        StringWriter w = new StringWriter();

        foreach (var page in pdf.GetPages())
        {
            string a = page.ExtractTextFromPage();

            w.WriteLine(a);
        }

        return w.ToString();
    }

    public static string ExtractTextFromPage(this Page page)
    {
        string a = page.Text;

        return a;
    }

    public static bool CalcHasPdfText(this PdfDocument pdf)
    {
        int num = 0;

        foreach (var page in pdf.GetPages())
        {
            string a = page.ExtractTextFromPage();

            if (a.Length >= 16)
            {
                num++;
            }
        }

        return num >= Math.Max((pdf.NumberOfPages / 10), 4);
    }

    public static bool CalcIsPdfVertical(this PdfDocument pdf)
    {
        int numVertical = 0;

        foreach (var page in pdf.GetPages())
        {
            if (CalcIsPageVertical(page))
            {
                numVertical++;
            }
        }

        if (numVertical >= (pdf.NumberOfPages / 2))
        {
            return true;
        }

        return false;
    }

    public static bool CalcIsPageVertical(this Page page)
    {
        var letters = page.Letters;

        int count = letters.Count;

        if (count <= 10)
        {
            return false;
        }

        double totalX = 0, totalY = 0;

        for (int i = 1; i < count - 1; i++)
        {
            Letter prev = letters[i - 1];
            Letter cur = letters[i];

            double relativeX = cur.GlyphRectangle.Left - prev.GlyphRectangle.Left;
            double relativeY = cur.GlyphRectangle.Top - prev.GlyphRectangle.Top;

            relativeX = Math.Abs(relativeX);
            relativeY = Math.Abs(relativeY);

            totalX += relativeX;
            totalY += relativeY;
        }

        if (totalY > totalX)
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// PDF の Date 文字列（例: "D:19981223195200-08'00'"）
    /// を DateTimeOffset に変換する
    /// </summary>
    /// <exception cref="ArgumentNullException"/>
    /// <exception cref="FormatException"/>
    public static DateTimeOffset PdfDateTimeStringToDtOffset(string pdfDateString)
    {
        try
        {
            return PdfDateTimeStringToDtOffsetCore(pdfDateString);
        }
        catch
        {
            return ZeroDateTimeOffsetValue;
        }
    }
    // ──主要パターンを 1 本の Regex で吸収──
    private static readonly Regex _rx = new(
        @"^D?:?(?<year>\d{4})" +
        @"(?<month>\d{2})?" +
        @"(?<day>\d{2})?" +
        @"(?<hour>\d{2})?" +
        @"(?<minute>\d{2})?" +
        @"(?<second>\d{2})?" +
        @"(?<offset>Z|[+\-]\d{2}('?[:]?\d{2})?'?)?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// PDF 日時文字列 → <see cref="DateTimeOffset"/> へ変換します。
    /// 解析できない場合は <see cref="FormatException"/> を送出します。
    /// </summary>
    static DateTimeOffset PdfDateTimeStringToDtOffsetCore(string pdfDateString)
    {
        if (pdfDateString is null) throw new ArgumentNullException(nameof(pdfDateString));

        // () や D: を除去
        pdfDateString = pdfDateString.Trim();
        if (pdfDateString.StartsWith("(") && pdfDateString.EndsWith(")"))
            pdfDateString = pdfDateString[1..^1];
        if (pdfDateString.StartsWith("D:")) pdfDateString = pdfDateString[2..];

        var m = _rx.Match(pdfDateString);
        if (!m.Success)
            throw new FormatException($"'{pdfDateString}' is not a valid PDF date string.");

        // 欠損フィールドは既定値
        int year = int.Parse(m.Groups["year"].Value, CultureInfo.InvariantCulture);
        int month = m.Groups["month"].Success ? int.Parse(m.Groups["month"].Value) : 1;
        int day = m.Groups["day"].Success ? int.Parse(m.Groups["day"].Value) : 1;
        int hour = m.Groups["hour"].Success ? int.Parse(m.Groups["hour"].Value) : 0;
        int minute = m.Groups["minute"].Success ? int.Parse(m.Groups["minute"].Value) : 0;
        int second = m.Groups["second"].Success ? int.Parse(m.Groups["second"].Value) : 0;

        var localDateTime = new DateTime(year, month, day, hour, minute, second, DateTimeKind.Unspecified);
        var offset = ParseOffset(m.Groups["offset"].Value, localDateTime);

        return new DateTimeOffset(localDateTime, offset);
    }

    /// <summary>タイムゾーン部を TimeSpan に変換</summary>
    private static TimeSpan ParseOffset(string raw, DateTime baseDate)
    {
        if (string.IsNullOrEmpty(raw))
            return TimeZoneInfo.Local.GetUtcOffset(baseDate);      // 明示なし＝ローカル

        if (raw.Equals("Z", StringComparison.OrdinalIgnoreCase))
            return TimeSpan.Zero;

        // "+09'00'", "+0900", "+09:00" 等を統一
        string cleaned = raw.Replace("'", "").Replace(":", "");
        int sign = cleaned[0] == '-' ? -1 : 1;
        int hours = int.Parse(cleaned[1..3]);
        int minutes = cleaned.Length > 3 ? int.Parse(cleaned[3..5]) : 0;

        return TimeSpan.FromMinutes(sign * (hours * 60 + minutes));
    }


}

public static class Pdf2TxtApp
{
    public static void CopyAllNonOcrPdfFiles(string srcDir, string destDir)
    {
        var srcPdfFiles = Lfs.EnumDirectory(srcDir, true, wildcard: "*.pdf");

        foreach (var srcPdfFile in srcPdfFiles)
        {
            try
            {
                string relativeFileName = PP.GetRelativeFileName(srcPdfFile.FullPath, srcDir);

                $"Loading '{relativeFileName}' ..."._Print();

                using var pdfDoc = Pdf2Txt.LoadPdf(Lfs.ReadDataFromFile(srcPdfFile.FullPath));

                bool hasText = pdfDoc.CalcHasPdfText();

                if (hasText == false)
                {
                    string destFullPath = PP.Combine(destDir, relativeFileName);

                    $"   Copying..."._Print();
                    Lfs.CopyFile(srcPdfFile.FullPath, destFullPath, new CopyFileParams(flags: FileFlags.AutoCreateDirectory, metadataCopier: new FileMetadataCopier(FileMetadataCopyMode.TimeAll)));
                }
            }
            catch (Exception ex)
            {
                srcPdfFile.FullPath._Error();
                ex._Error();
            }
        }
    }
}

/// <summary>
/// 各ページのサイズ情報（mm単位）を格納するデータクラス
/// </summary>
public class PdfPageInfo
{
    /// <summary>1始まりのページ番号</summary>
    public int PageNumber1Origin { get; set; }
    /// <summary>幅 (mm)</summary>
    public double WidthMm { get; set; }
    /// <summary>高さ (mm)</summary>
    public double HeightMm { get; set; }
}

public static class PdfPageInfoLib
{
    // ポイント(pt)をミリメートル(mm)に変換する定数
    private const double PointToMillimeter = 25.4 / 72.0;

    /// <summary>
    /// PDFファイルのページ数を取得する
    /// </summary>
    /// <param name="pdfPath">PDFファイルのパス</param>
    /// <returns>ページ数</returns>
    public static int GetPdfNumPaged(string pdfPath)
    {
        // PdfDocument.Open は内部で最低限の情報だけを読み込むため高速
        using (var document = PdfDocument.Open(pdfPath))
        {
            return document.NumberOfPages;
        }
    }

    /// <summary>
    /// PDFファイルの各ページサイズをミリメートル単位で取得する
    /// </summary>
    /// <param name="pdfPath">PDFファイルのパス</param>
    /// <returns>各ページの PageSizeMm リスト</returns>
    public static List<PdfPageInfo> GetPdfPageInfo(string pdfPath)
    {
        var sizes = new List<PdfPageInfo>();

        using (var document = PdfDocument.Open(pdfPath))
        {
            // GetPages() は内部でページツリーをイテレートするだけ
            foreach (var page in document.GetPages())
            {
                double widthPt = page.Width;
                double heightPt = page.Height;

                sizes.Add(new PdfPageInfo
                {
                    PageNumber1Origin = page.Number,
                    WidthMm = Math.Round(widthPt * PointToMillimeter, 2),
                    HeightMm = Math.Round(heightPt * PointToMillimeter, 2),
                });
            }
        }

        return sizes;
    }

    /// <summary>
    /// 余白トリム後の「でこぼこ PDF」（種類 2）と推定される場合 true。
    /// 紙をただスキャンしただけ（種類 1）または判定不能の場合は false。
    /// </summary>
    public static bool DetermineIsDekobokoPdf(string pdfPath)
        => DetermineIsDekobokoPdf(GetPdfPageInfo(pdfPath));

    /// <summary>
    /// 余白トリムによりページサイズが「でこぼこ」かどうか推定する。
    /// true なら <種類 2>（トリム済み）、false なら <種類 1>（スキャンそのまま）または判定不能。
    /// </summary>
    public static bool DetermineIsDekobokoPdf(List<PdfPageInfo> pageInfoList)
    {
        if (pageInfoList == null || pageInfoList.Count < 2)
            return false;                     // ページが 0～1 枚なら判断できないので安全側

        const double TOLERANCE_RATIO = 0.04;  // 4% までを「許容ゆらぎ」とみなす
        int totalPages = pageInfoList.Count;
        int dekobokoCount = 0;

        // 先頭ページの面積を基準に走査
        double prevArea = pageInfoList[0].WidthMm * pageInfoList[0].HeightMm;

        for (int i = 1; i < totalPages; i++)
        {
            var page = pageInfoList[i];
            double area = page.WidthMm * page.HeightMm;
            double ratio = Math.Abs(area - prevArea) / prevArea;

            if (ratio > TOLERANCE_RATIO)
                dekobokoCount++;

            prevArea = area;  // 次比較用に更新
        }

        // 「でこぼこ発生」がページ数の一定割合以上なら <種類 2> と推定
        return dekobokoCount >= totalPages * 0.03;
    }
}

#endif
