#pragma warning disable CA2235 // Mark all non-serializable fields

using System;
using System.Text;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

namespace SuperBookTools;

/// <summary>
/// PDF処理のステージを表す列挙型
/// </summary>
public enum ProcessingStage
{
    /// <summary>初期化中</summary>
    Initializing,
    /// <summary>ページ読み込み中</summary>
    Loading,
    /// <summary>傾き補正中</summary>
    Deskewing,
    /// <summary>画像処理中</summary>
    Processing,
    /// <summary>AI高画質化中 (RealESRGAN)</summary>
    Upscaling,
    /// <summary>PDF生成中</summary>
    Finalizing,
    /// <summary>文字認識中 (YomiToku)</summary>
    OCR,
    /// <summary>完了</summary>
    Completed
}

/// <summary>
/// 処理進捗を追跡・表示するクラス
/// </summary>
public class ProgressTracker
{
    private readonly object _lock = new object();

    /// <summary>現在処理中のファイル番号（1から開始）</summary>
    public int CurrentFileNumber { get; private set; }

    /// <summary>総ファイル数</summary>
    public int TotalFiles { get; private set; }

    /// <summary>現在のファイル名</summary>
    public string CurrentFileName { get; private set; } = "";

    /// <summary>現在の処理ステージ</summary>
    public ProcessingStage CurrentStage { get; private set; } = ProcessingStage.Initializing;

    /// <summary>現在処理中のページ番号（1から開始）</summary>
    public int CurrentPage { get; private set; }

    /// <summary>総ページ数</summary>
    public int TotalPages { get; private set; }

    /// <summary>現在処理中のアイテム名（例: page_0134.bmp）</summary>
    public string CurrentItem { get; private set; } = "";

    /// <summary>進捗バーの幅</summary>
    private const int ProgressBarWidth = 40;

    /// <summary>
    /// 新しいファイルの処理を開始
    /// </summary>
    /// <param name="fileNumber">ファイル番号（1から開始）</param>
    /// <param name="totalFiles">総ファイル数</param>
    /// <param name="fileName">ファイル名</param>
    public void StartFile(int fileNumber, int totalFiles, string fileName)
    {
        lock (_lock)
        {
            CurrentFileNumber = fileNumber;
            TotalFiles = totalFiles;
            CurrentFileName = fileName;
            CurrentStage = ProcessingStage.Initializing;
            CurrentPage = 0;
            TotalPages = 0;
            CurrentItem = "";

            PrintFileHeader();
        }
    }

    /// <summary>
    /// 処理ステージを更新
    /// </summary>
    /// <param name="stage">新しいステージ</param>
    /// <param name="totalPages">総ページ数（わかっている場合）</param>
    public void SetStage(ProcessingStage stage, int totalPages = 0)
    {
        lock (_lock)
        {
            CurrentStage = stage;
            if (totalPages > 0)
            {
                TotalPages = totalPages;
            }
            CurrentPage = 0;
            PrintProgress();
        }
    }

    /// <summary>
    /// ページ進捗を更新
    /// </summary>
    /// <param name="pageNumber">現在のページ番号（1から開始）</param>
    /// <param name="itemName">処理中のアイテム名</param>
    public void UpdatePage(int pageNumber, string itemName = "")
    {
        lock (_lock)
        {
            CurrentPage = pageNumber;
            if (!string.IsNullOrEmpty(itemName))
            {
                CurrentItem = itemName;
            }
            PrintProgress();
        }
    }

    /// <summary>
    /// 現在のファイル処理を完了としてマーク
    /// </summary>
    public void CompleteFile()
    {
        lock (_lock)
        {
            CurrentStage = ProcessingStage.Completed;
            PrintProgress();
            Console.WriteLine();
        }
    }

    /// <summary>
    /// ファイルヘッダーを出力
    /// </summary>
    private void PrintFileHeader()
    {
        Console.WriteLine();
        Console.WriteLine(new string('=', 80));
        Console.WriteLine($"[ファイル {CurrentFileNumber}/{TotalFiles}] {CurrentFileName}");
        Console.WriteLine(new string('=', 80));
    }

    /// <summary>
    /// 進捗状況を出力
    /// </summary>
    private void PrintProgress()
    {
        string stageName = GetStageName(CurrentStage);
        string stageDescription = GetStageDescription(CurrentStage);

        StringBuilder sb = new StringBuilder();
        sb.AppendLine($"ステージ: {stageName} ({stageDescription})");

        if (TotalPages > 0 && CurrentStage != ProcessingStage.Completed)
        {
            int percent = (int)((double)CurrentPage / TotalPages * 100);
            string progressBar = BuildProgressBar(percent);
            sb.AppendLine($"ページ進捗: {progressBar} {percent,3}% ({CurrentPage}/{TotalPages})");
        }

        if (!string.IsNullOrEmpty(CurrentItem))
        {
            sb.AppendLine($"現在の処理: {CurrentItem}");
        }

        sb.Append(new string('-', 80));

        // コンソールをクリアして更新（カーソル位置を戻す）
        Console.Write("\r" + new string(' ', Console.WindowWidth > 0 ? Console.WindowWidth - 1 : 80) + "\r");
        Console.WriteLine(sb.ToString());
    }

    /// <summary>
    /// プログレスバーを構築
    /// </summary>
    private static string BuildProgressBar(int percent)
    {
        int filled = (int)((double)percent / 100 * ProgressBarWidth);
        int empty = ProgressBarWidth - filled;
        return "[" + new string('=', filled) + new string('-', empty) + "]";
    }

    /// <summary>
    /// ステージ名を取得
    /// </summary>
    private static string GetStageName(ProcessingStage stage)
    {
        return stage switch
        {
            ProcessingStage.Initializing => "Initializing",
            ProcessingStage.Loading => "Loading",
            ProcessingStage.Deskewing => "Deskewing",
            ProcessingStage.Processing => "Processing",
            ProcessingStage.Upscaling => "Upscaling",
            ProcessingStage.Finalizing => "Finalizing",
            ProcessingStage.OCR => "OCR",
            ProcessingStage.Completed => "Completed",
            _ => "Unknown"
        };
    }

    /// <summary>
    /// ステージの日本語説明を取得
    /// </summary>
    private static string GetStageDescription(ProcessingStage stage)
    {
        return stage switch
        {
            ProcessingStage.Initializing => "初期化中",
            ProcessingStage.Loading => "ページ読み込み中",
            ProcessingStage.Deskewing => "傾き補正中",
            ProcessingStage.Processing => "画像処理中",
            ProcessingStage.Upscaling => "AI高画質化中",
            ProcessingStage.Finalizing => "PDF生成中",
            ProcessingStage.OCR => "文字認識中",
            ProcessingStage.Completed => "完了",
            _ => "不明"
        };
    }

    /// <summary>
    /// 最終結果のサマリーを出力
    /// </summary>
    /// <param name="totalFiles">総ファイル数</param>
    /// <param name="okCount">成功数</param>
    /// <param name="skipCount">スキップ数</param>
    /// <param name="errorCount">エラー数</param>
    public static void PrintSummary(int totalFiles, int okCount, int skipCount, int errorCount)
    {
        Console.WriteLine();
        Console.WriteLine(new string('=', 80));
        Console.WriteLine("処理結果サマリー");
        Console.WriteLine(new string('=', 80));
        Console.WriteLine($"  総ファイル数: {totalFiles}");
        Console.WriteLine($"  成功:         {okCount}");
        Console.WriteLine($"  スキップ:     {skipCount}");
        Console.WriteLine($"  エラー:       {errorCount}");
        Console.WriteLine(new string('=', 80));
        Console.WriteLine();
    }
}
