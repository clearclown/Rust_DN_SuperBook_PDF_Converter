//! Progress tracking module for PDF processing.
//!
//! This module provides structured progress tracking and display,
//! ported from the C# ProgressTracker.cs implementation.

use std::fmt;
use std::io::{self, Write};
use std::time::Instant;

/// Processing stages for PDF conversion
#[derive(Debug, Clone, Copy, PartialEq, Eq, Default)]
pub enum ProcessingStage {
    /// Initializing
    #[default]
    Initializing,
    /// Extracting images from PDF
    Extracting,
    /// Deskewing images
    Deskewing,
    /// Normalizing to internal resolution
    Normalizing,
    /// Applying color correction
    ColorCorrecting,
    /// Cropping margins
    Cropping,
    /// AI upscaling (RealESRGAN)
    Upscaling,
    /// Final output processing
    Finalizing,
    /// Writing PDF
    WritingPdf,
    /// OCR processing (YomiToku)
    OCR,
    /// Completed
    Completed,
}

impl ProcessingStage {
    /// Get the English name of the stage
    pub fn name(&self) -> &'static str {
        match self {
            ProcessingStage::Initializing => "Initializing",
            ProcessingStage::Extracting => "Extracting",
            ProcessingStage::Deskewing => "Deskewing",
            ProcessingStage::Normalizing => "Normalizing",
            ProcessingStage::ColorCorrecting => "ColorCorrecting",
            ProcessingStage::Cropping => "Cropping",
            ProcessingStage::Upscaling => "Upscaling",
            ProcessingStage::Finalizing => "Finalizing",
            ProcessingStage::WritingPdf => "WritingPdf",
            ProcessingStage::OCR => "OCR",
            ProcessingStage::Completed => "Completed",
        }
    }

    /// Get the Japanese description of the stage
    pub fn description_ja(&self) -> &'static str {
        match self {
            ProcessingStage::Initializing => "初期化中",
            ProcessingStage::Extracting => "抽出中",
            ProcessingStage::Deskewing => "傾き補正中",
            ProcessingStage::Normalizing => "正規化中",
            ProcessingStage::ColorCorrecting => "色補正中",
            ProcessingStage::Cropping => "クロップ中",
            ProcessingStage::Upscaling => "AI高画質化中",
            ProcessingStage::Finalizing => "最終処理中",
            ProcessingStage::WritingPdf => "PDF生成中",
            ProcessingStage::OCR => "文字認識中",
            ProcessingStage::Completed => "完了",
        }
    }
}

impl fmt::Display for ProcessingStage {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        write!(f, "{} ({})", self.name(), self.description_ja())
    }
}

/// Output verbosity mode
#[derive(Debug, Clone, Copy, PartialEq, Eq, Default)]
pub enum OutputMode {
    /// No output
    Quiet,
    /// Normal output (stage display only)
    #[default]
    Normal,
    /// Verbose output (page-level progress)
    Verbose,
    /// Very verbose (all items displayed)
    VeryVerbose,
}

impl OutputMode {
    /// Create OutputMode from verbosity level
    pub fn from_verbosity(level: u8) -> Self {
        match level {
            0 => OutputMode::Normal,
            1 => OutputMode::Verbose,
            _ => OutputMode::VeryVerbose,
        }
    }

    /// Check if output should be shown at this mode
    pub fn should_show(&self, required: OutputMode) -> bool {
        use OutputMode::*;
        match (self, required) {
            (Quiet, _) => false,
            (Normal, Quiet | Normal) => true,
            (Verbose, Quiet | Normal | Verbose) => true,
            (VeryVerbose, _) => true,
            _ => false,
        }
    }
}

/// Progress bar width in characters
const PROGRESS_BAR_WIDTH: usize = 40;

/// Build a progress bar string
pub fn build_progress_bar(percent: u8) -> String {
    let percent = percent.min(100);
    let filled = (percent as usize * PROGRESS_BAR_WIDTH) / 100;
    let empty = PROGRESS_BAR_WIDTH - filled;
    format!("[{}{}]", "=".repeat(filled), "-".repeat(empty))
}

/// Progress tracker for PDF processing
#[derive(Debug)]
pub struct ProgressTracker {
    /// Current file number (1-based)
    pub current_file: usize,
    /// Total number of files
    pub total_files: usize,
    /// Current filename
    pub current_filename: String,
    /// Current processing stage
    pub current_stage: ProcessingStage,
    /// Current page number (1-based)
    pub current_page: usize,
    /// Total number of pages
    pub total_pages: usize,
    /// Current item being processed
    pub current_item: String,
    /// Start time
    start_time: Instant,
    /// Output mode
    output_mode: OutputMode,
}

impl Default for ProgressTracker {
    fn default() -> Self {
        Self::new(1, OutputMode::Normal)
    }
}

impl ProgressTracker {
    /// Create a new progress tracker
    pub fn new(total_files: usize, output_mode: OutputMode) -> Self {
        Self {
            current_file: 0,
            total_files,
            current_filename: String::new(),
            current_stage: ProcessingStage::Initializing,
            current_page: 0,
            total_pages: 0,
            current_item: String::new(),
            start_time: Instant::now(),
            output_mode,
        }
    }

    /// Start processing a new file
    pub fn start_file(&mut self, file_number: usize, filename: &str) {
        self.current_file = file_number;
        self.current_filename = filename.to_string();
        self.current_stage = ProcessingStage::Initializing;
        self.current_page = 0;
        self.total_pages = 0;
        self.current_item.clear();
        self.start_time = Instant::now();

        if self.output_mode.should_show(OutputMode::Normal) {
            self.print_file_header();
        }
    }

    /// Set the current processing stage
    pub fn set_stage(&mut self, stage: ProcessingStage, total_pages: usize) {
        self.current_stage = stage;
        if total_pages > 0 {
            self.total_pages = total_pages;
        }
        self.current_page = 0;

        if self.output_mode.should_show(OutputMode::Normal) {
            self.print_stage();
        }
    }

    /// Update page progress
    pub fn update_page(&mut self, page_number: usize, item_name: &str) {
        self.current_page = page_number;
        if !item_name.is_empty() {
            self.current_item = item_name.to_string();
        }

        if self.output_mode.should_show(OutputMode::Verbose) {
            self.print_progress();
        }
    }

    /// Mark the current file as complete
    pub fn complete_file(&mut self) {
        self.current_stage = ProcessingStage::Completed;

        if self.output_mode.should_show(OutputMode::Normal) {
            let elapsed = self.start_time.elapsed();
            println!("  Completed in {:.2}s", elapsed.as_secs_f64());
            println!();
        }
    }

    /// Get elapsed time in seconds
    pub fn elapsed_secs(&self) -> f64 {
        self.start_time.elapsed().as_secs_f64()
    }

    /// Print file header
    fn print_file_header(&self) {
        println!();
        println!("{}", "=".repeat(80));
        println!(
            "[File {}/{}] {}",
            self.current_file, self.total_files, self.current_filename
        );
        println!("{}", "=".repeat(80));
    }

    /// Print current stage
    fn print_stage(&self) {
        println!("  Stage: {}", self.current_stage);
    }

    /// Print progress
    fn print_progress(&self) {
        if self.total_pages > 0 && self.current_stage != ProcessingStage::Completed {
            let percent = ((self.current_page as f64 / self.total_pages as f64) * 100.0) as u8;
            let bar = build_progress_bar(percent);
            print!(
                "\r    {} {:3}% ({}/{})",
                bar, percent, self.current_page, self.total_pages
            );
            if self.output_mode.should_show(OutputMode::VeryVerbose)
                && !self.current_item.is_empty()
            {
                print!(" {}", self.current_item);
            }
            let _ = io::stdout().flush();
        }
    }

    /// Print final summary
    pub fn print_summary(
        total_files: usize,
        ok_count: usize,
        skip_count: usize,
        error_count: usize,
    ) {
        println!();
        println!("{}", "=".repeat(80));
        println!("Processing Summary");
        println!("{}", "=".repeat(80));
        println!("  Total files:  {}", total_files);
        println!("  Succeeded:    {}", ok_count);
        println!("  Skipped:      {}", skip_count);
        println!("  Errors:       {}", error_count);
        println!("{}", "=".repeat(80));
        println!();
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    // PROG-001: ProgressTracker新規作成
    #[test]
    fn test_progress_tracker_new() {
        let tracker = ProgressTracker::new(5, OutputMode::Normal);
        assert_eq!(tracker.total_files, 5);
        assert_eq!(tracker.current_file, 0);
        assert_eq!(tracker.current_stage, ProcessingStage::Initializing);
    }

    // PROG-002: start_file()でファイル開始
    #[test]
    fn test_start_file() {
        let mut tracker = ProgressTracker::new(3, OutputMode::Quiet);
        tracker.start_file(1, "test.pdf");
        assert_eq!(tracker.current_file, 1);
        assert_eq!(tracker.current_filename, "test.pdf");
    }

    // PROG-003: set_stage()でステージ変更
    #[test]
    fn test_set_stage() {
        let mut tracker = ProgressTracker::new(1, OutputMode::Quiet);
        tracker.set_stage(ProcessingStage::Extracting, 100);
        assert_eq!(tracker.current_stage, ProcessingStage::Extracting);
        assert_eq!(tracker.total_pages, 100);
    }

    // PROG-004: update_page()で進捗更新
    #[test]
    fn test_update_page() {
        let mut tracker = ProgressTracker::new(1, OutputMode::Quiet);
        tracker.set_stage(ProcessingStage::Deskewing, 50);
        tracker.update_page(25, "page_025.png");
        assert_eq!(tracker.current_page, 25);
        assert_eq!(tracker.current_item, "page_025.png");
    }

    // PROG-005: complete_file()で完了マーク
    #[test]
    fn test_complete_file() {
        let mut tracker = ProgressTracker::new(1, OutputMode::Quiet);
        tracker.start_file(1, "test.pdf");
        tracker.complete_file();
        assert_eq!(tracker.current_stage, ProcessingStage::Completed);
    }

    // PROG-006: ProcessingStage名称取得
    #[test]
    fn test_processing_stage_name() {
        assert_eq!(ProcessingStage::Initializing.name(), "Initializing");
        assert_eq!(ProcessingStage::Extracting.name(), "Extracting");
        assert_eq!(ProcessingStage::Deskewing.name(), "Deskewing");
        assert_eq!(ProcessingStage::Normalizing.name(), "Normalizing");
        assert_eq!(ProcessingStage::ColorCorrecting.name(), "ColorCorrecting");
        assert_eq!(ProcessingStage::Cropping.name(), "Cropping");
        assert_eq!(ProcessingStage::Upscaling.name(), "Upscaling");
        assert_eq!(ProcessingStage::Finalizing.name(), "Finalizing");
        assert_eq!(ProcessingStage::WritingPdf.name(), "WritingPdf");
        assert_eq!(ProcessingStage::OCR.name(), "OCR");
        assert_eq!(ProcessingStage::Completed.name(), "Completed");
    }

    // PROG-007: ProcessingStage日本語説明取得
    #[test]
    fn test_processing_stage_description_ja() {
        assert_eq!(ProcessingStage::Initializing.description_ja(), "初期化中");
        assert_eq!(ProcessingStage::Extracting.description_ja(), "抽出中");
        assert_eq!(ProcessingStage::Deskewing.description_ja(), "傾き補正中");
        assert_eq!(ProcessingStage::Completed.description_ja(), "完了");
    }

    // PROG-008: プログレスバー構築
    #[test]
    fn test_build_progress_bar() {
        let bar_0 = build_progress_bar(0);
        assert_eq!(bar_0, "[----------------------------------------]");

        let bar_50 = build_progress_bar(50);
        assert_eq!(bar_50, "[====================--------------------]");

        let bar_100 = build_progress_bar(100);
        assert_eq!(bar_100, "[========================================]");
    }

    // PROG-009: プログレスバー境界値
    #[test]
    fn test_build_progress_bar_boundary() {
        // Over 100 should be clamped
        let bar_150 = build_progress_bar(150);
        assert_eq!(bar_150, "[========================================]");

        // 25%
        let bar_25 = build_progress_bar(25);
        assert_eq!(bar_25, "[==========------------------------------]");

        // 75%
        let bar_75 = build_progress_bar(75);
        assert_eq!(bar_75, "[==============================----------]");
    }

    // PROG-010: OutputMode::Quiet動作確認
    #[test]
    fn test_output_mode_quiet() {
        let mode = OutputMode::Quiet;
        assert!(!mode.should_show(OutputMode::Quiet));
        assert!(!mode.should_show(OutputMode::Normal));
        assert!(!mode.should_show(OutputMode::Verbose));
    }

    // PROG-011: OutputMode::Verbose動作確認
    #[test]
    fn test_output_mode_verbose() {
        let mode = OutputMode::Verbose;
        assert!(mode.should_show(OutputMode::Quiet));
        assert!(mode.should_show(OutputMode::Normal));
        assert!(mode.should_show(OutputMode::Verbose));
        assert!(!mode.should_show(OutputMode::VeryVerbose));
    }

    // PROG-012: 経過時間計算
    #[test]
    fn test_elapsed_secs() {
        let tracker = ProgressTracker::new(1, OutputMode::Quiet);
        std::thread::sleep(std::time::Duration::from_millis(10));
        let elapsed = tracker.elapsed_secs();
        assert!(elapsed >= 0.01);
    }

    // Additional tests

    #[test]
    fn test_output_mode_from_verbosity() {
        assert_eq!(OutputMode::from_verbosity(0), OutputMode::Normal);
        assert_eq!(OutputMode::from_verbosity(1), OutputMode::Verbose);
        assert_eq!(OutputMode::from_verbosity(2), OutputMode::VeryVerbose);
        assert_eq!(OutputMode::from_verbosity(10), OutputMode::VeryVerbose);
    }

    #[test]
    fn test_processing_stage_display() {
        let stage = ProcessingStage::Extracting;
        let display = format!("{}", stage);
        assert_eq!(display, "Extracting (抽出中)");
    }

    #[test]
    fn test_processing_stage_default() {
        let stage: ProcessingStage = Default::default();
        assert_eq!(stage, ProcessingStage::Initializing);
    }

    #[test]
    fn test_output_mode_default() {
        let mode: OutputMode = Default::default();
        assert_eq!(mode, OutputMode::Normal);
    }

    #[test]
    fn test_progress_tracker_default() {
        let tracker: ProgressTracker = Default::default();
        assert_eq!(tracker.total_files, 1);
        assert_eq!(tracker.current_file, 0);
    }

    #[test]
    fn test_update_page_empty_item() {
        let mut tracker = ProgressTracker::new(1, OutputMode::Quiet);
        tracker.current_item = "previous.png".to_string();
        tracker.update_page(10, "");
        assert_eq!(tracker.current_page, 10);
        assert_eq!(tracker.current_item, "previous.png"); // unchanged
    }

    #[test]
    fn test_set_stage_zero_pages() {
        let mut tracker = ProgressTracker::new(1, OutputMode::Quiet);
        tracker.total_pages = 100;
        tracker.set_stage(ProcessingStage::Deskewing, 0);
        assert_eq!(tracker.total_pages, 100); // unchanged when 0
    }

    #[test]
    fn test_output_mode_very_verbose() {
        let mode = OutputMode::VeryVerbose;
        assert!(mode.should_show(OutputMode::Quiet));
        assert!(mode.should_show(OutputMode::Normal));
        assert!(mode.should_show(OutputMode::Verbose));
        assert!(mode.should_show(OutputMode::VeryVerbose));
    }

    #[test]
    fn test_output_mode_normal() {
        let mode = OutputMode::Normal;
        assert!(mode.should_show(OutputMode::Quiet));
        assert!(mode.should_show(OutputMode::Normal));
        assert!(!mode.should_show(OutputMode::Verbose));
        assert!(!mode.should_show(OutputMode::VeryVerbose));
    }
}
