//! Markdown conversion pipeline
//!
//! Provides a pipeline for converting scanned PDFs to Markdown,
//! reusing existing processing steps (extraction, deskew, upscale)
//! and adding OCR + figure detection + Markdown generation.

use serde::{Deserialize, Serialize};
use std::path::{Path, PathBuf};
use std::time::Instant;
use thiserror::Error;

use crate::cli::MarkdownArgs;
use crate::figure_detect::{FigureDetectOptions, FigureDetector, FigureRegion, PageClassification};
use crate::markdown_gen::{MarkdownGenError, MarkdownGenerator};
use crate::pipeline::{PipelineConfig, PipelineError, ProgressCallback};
use crate::yomitoku::{OcrResult, YomiTokuOptions};

/// Error type for Markdown pipeline
#[derive(Debug, Error)]
pub enum MarkdownPipelineError {
    #[error("Input file not found: {0}")]
    InputNotFound(PathBuf),

    #[error("Pipeline error: {0}")]
    Pipeline(#[from] PipelineError),

    #[error("Markdown generation error: {0}")]
    MarkdownGen(#[from] MarkdownGenError),

    #[error("OCR error: {0}")]
    OcrError(String),

    #[error("Figure detection error: {0}")]
    FigureDetectError(String),

    #[error("IO error: {0}")]
    Io(#[from] std::io::Error),

    #[error("JSON error: {0}")]
    Json(#[from] serde_json::Error),
}

/// Progress state for recovery
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ProgressState {
    pub total_pages: usize,
    pub processed_pages: Vec<usize>,
    pub started_at: String,
    pub last_updated: String,
    pub input_pdf: PathBuf,
    pub title: String,
}

impl ProgressState {
    fn new(total_pages: usize, input_pdf: &Path, title: &str) -> Self {
        let now = chrono::Utc::now().to_rfc3339();
        Self {
            total_pages,
            processed_pages: Vec::new(),
            started_at: now.clone(),
            last_updated: now,
            input_pdf: input_pdf.to_path_buf(),
            title: title.to_string(),
        }
    }

    fn mark_processed(&mut self, page_index: usize) {
        if !self.processed_pages.contains(&page_index) {
            self.processed_pages.push(page_index);
            self.processed_pages.sort();
        }
        self.last_updated = chrono::Utc::now().to_rfc3339();
    }

    fn is_processed(&self, page_index: usize) -> bool {
        self.processed_pages.contains(&page_index)
    }

    fn save(&self, path: &Path) -> Result<(), MarkdownPipelineError> {
        let json = serde_json::to_string_pretty(self)?;
        std::fs::write(path, json)?;
        Ok(())
    }

    fn load(path: &Path) -> Result<Self, MarkdownPipelineError> {
        let json = std::fs::read_to_string(path)?;
        let state: Self = serde_json::from_str(&json)?;
        Ok(state)
    }
}

/// Result of Markdown pipeline processing
#[derive(Debug)]
pub struct MarkdownPipelineResult {
    pub page_count: usize,
    pub output_path: PathBuf,
    pub images_count: usize,
    pub elapsed_seconds: f64,
}

/// Markdown conversion pipeline
pub struct MarkdownPipeline {
    config: PipelineConfig,
    figure_options: FigureDetectOptions,
}

impl MarkdownPipeline {
    /// Create a new Markdown pipeline from CLI args
    pub fn from_args(args: &MarkdownArgs) -> Self {
        let config = PipelineConfig {
            dpi: args.dpi,
            deskew: args.effective_deskew(),
            upscale: args.upscale,
            gpu: args.gpu,
            ocr: true, // Always enabled for Markdown
            max_pages: args.max_pages,
            save_debug: args.verbose >= 3,
            ..PipelineConfig::default()
        };

        let mut figure_options = FigureDetectOptions::default();
        if let Some(sensitivity) = args.figure_sensitivity {
            // Lower min_area_fraction = more sensitive
            figure_options.min_area_fraction = 0.05 * (1.0 - sensitivity.clamp(0.0, 1.0));
        }

        Self {
            config,
            figure_options,
        }
    }

    /// Run the full Markdown conversion pipeline
    pub fn run<P: ProgressCallback>(
        &self,
        input: &Path,
        output_dir: &Path,
        resume: bool,
        progress: &P,
    ) -> Result<MarkdownPipelineResult, MarkdownPipelineError> {
        let start_time = Instant::now();

        // Validate input
        if !input.exists() {
            return Err(MarkdownPipelineError::InputNotFound(input.to_path_buf()));
        }

        // Create output directory structure
        std::fs::create_dir_all(output_dir)?;
        let progress_path = output_dir.join(".progress.json");

        // Determine title from filename
        let title = input
            .file_stem()
            .unwrap_or_default()
            .to_string_lossy()
            .to_string();

        // Create work directory for intermediate files
        let work_dir = output_dir.join(format!(".work_{}", &title));
        std::fs::create_dir_all(&work_dir)?;

        // Step 1: Extract images from PDF
        progress.on_step_start("PDF画像抽出中...");
        let extract_options = crate::ExtractOptions::builder()
            .dpi(self.config.dpi)
            .build();
        let extracted_dir = work_dir.join("extracted");
        std::fs::create_dir_all(&extracted_dir)?;

        let mut extracted_pages =
            crate::LopdfExtractor::extract_auto(input, &extracted_dir, &extract_options)
                .map_err(|e| PipelineError::ExtractionFailed(e.to_string()))?;

        // Apply max_pages limit
        if let Some(max_pages) = self.config.max_pages {
            if extracted_pages.len() > max_pages {
                progress.on_debug(&format!("{}ページに制限", max_pages));
                extracted_pages.truncate(max_pages);
            }
        }

        let page_count = extracted_pages.len();
        progress.on_step_complete("PDF画像抽出", &format!("{}ページ", page_count));

        let mut current_images: Vec<PathBuf> =
            extracted_pages.iter().map(|p| p.path.clone()).collect();

        // Step 2: Margin trimming
        if self.config.margin_trim > 0.0 {
            progress.on_step_start("マージントリミング中...");
            let trimmed_dir = work_dir.join("trimmed");
            std::fs::create_dir_all(&trimmed_dir)?;

            let trimmed = self.step_margin_trim(&trimmed_dir, &current_images, progress)?;
            current_images = trimmed;
            progress.on_step_complete("マージントリミング", "完了");
        }

        // Step 3: AI Upscaling (optional)
        if self.config.upscale {
            progress.on_step_start("AI超解像処理中...");
            let upscaled_dir = work_dir.join("upscaled");
            std::fs::create_dir_all(&upscaled_dir)?;

            let upscaled = self.step_upscale(&upscaled_dir, &current_images, progress)?;
            current_images = upscaled;
            progress.on_step_complete("AI超解像", "完了");
        }

        // Step 3.5: 180-degree rotation detection
        if self.config.deskew {
            progress.on_step_start("ページ回転検出中...");
            let rotated_dir = work_dir.join("rotation_corrected");
            std::fs::create_dir_all(&rotated_dir)?;

            let corrected = self.step_rotation_detect(&rotated_dir, &current_images, progress)?;
            current_images = corrected;
            progress.on_step_complete("回転検出", "完了");
        }

        // Step 4: Deskew
        if self.config.deskew {
            progress.on_step_start("傾き補正中...");
            let deskewed_dir = work_dir.join("deskewed");
            std::fs::create_dir_all(&deskewed_dir)?;

            let deskewed = self.step_deskew(&deskewed_dir, &current_images, progress)?;
            current_images = deskewed;
            progress.on_step_complete("傾き補正", "完了");
        }

        // Load or create progress state
        let mut state = if resume && progress_path.exists() {
            let s = ProgressState::load(&progress_path)?;
            // Validate that the resume state matches the current input PDF
            if s.input_pdf != input {
                progress.on_debug(&format!(
                    "リカバリーstate不一致: 保存={}, 現在={} — 新規開始します",
                    s.input_pdf.display(),
                    input.display()
                ));
                ProgressState::new(page_count, input, &title)
            } else {
                progress.on_step_start(&format!(
                    "リカバリーモード: {}/{}ページ処理済み",
                    s.processed_pages.len(),
                    s.total_pages
                ));
                // Filter out any processed_pages that exceed current page_count
                let mut valid_state = s;
                valid_state.processed_pages.retain(|&p| p < page_count);
                valid_state.total_pages = page_count;
                valid_state
            }
        } else {
            ProgressState::new(page_count, input, &title)
        };

        // Create Markdown generator
        let md_gen = MarkdownGenerator::new(output_dir)?;

        // Step 5-9: OCR + Figure Detection + Markdown Generation (per page)
        progress.on_step_start(&format!(
            "OCR・図検出・Markdown生成 ({}ページ)...",
            page_count
        ));

        // Setup YomiToku (graceful fallback if venv unavailable)
        let venv_path = crate::resolve_venv_path();
        let bridge_config = crate::AiBridgeConfig::builder()
            .venv_path(venv_path.clone())
            .build();
        let yomitoku = match crate::SubprocessBridge::new(bridge_config) {
            Ok(bridge) => Some(crate::YomiToku::new(bridge)),
            Err(e) => {
                progress.on_warning(&format!(
                    "YomiToku利用不可 (venvが見つからないか初期化失敗): {} — 図検出のみで続行します",
                    e
                ));
                None
            }
        };

        let yomitoku_options = YomiTokuOptions::builder()
            .use_gpu(self.config.gpu)
            .detect_vertical(true)
            .confidence_threshold(0.3) // Lower threshold for book scanning
            .build();

        let mut images_count = 0usize;

        for (page_idx, image_path) in current_images.iter().enumerate() {
            // Skip already processed pages (resume mode)
            if state.is_processed(page_idx) {
                progress.on_debug(&format!("ページ {} スキップ (処理済み)", page_idx + 1));
                continue;
            }

            progress.on_step_progress(page_idx + 1, page_count);

            // Run OCR (or create empty result if YomiToku unavailable)
            let ocr_result = if let Some(ref yt) = yomitoku {
                match yt.ocr(image_path, &yomitoku_options) {
                    Ok(result) => result,
                    Err(e) => {
                        progress.on_debug(&format!(
                            "ページ {} OCRエラー: {} (空テキストとして続行)",
                            page_idx + 1,
                            e
                        ));
                        Self::empty_ocr_result(image_path)
                    }
                }
            } else {
                Self::empty_ocr_result(image_path)
            };

            // Load image for figure detection
            let image = image::open(image_path)
                .map_err(|e| MarkdownPipelineError::FigureDetectError(e.to_string()))?;

            // Classify page and detect figures
            let classification =
                FigureDetector::classify_page(&image, &ocr_result, page_idx, &self.figure_options);

            // Save figure/cover/full-page images
            let figure_images =
                self.save_page_images(&image, page_idx, &classification, md_gen.images_dir())?;
            images_count += figure_images.len();

            // Build page content
            let page_content =
                md_gen.build_page_content(page_idx, &ocr_result, &classification, &figure_images);

            // Generate and save page Markdown
            let page_md = md_gen.generate_page_markdown(&page_content)?;
            md_gen.save_page_markdown(page_idx, &page_md)?;

            // Update progress
            state.mark_processed(page_idx);
            state.save(&progress_path)?;

            progress.on_debug(&format!(
                "ページ {} 完了: {:?}",
                page_idx + 1,
                match &classification {
                    PageClassification::Cover => "表紙",
                    PageClassification::FullPageImage => "全面画像",
                    PageClassification::TextOnly => "テキスト",
                    PageClassification::Mixed { figures } =>
                        if figures.is_empty() {
                            "テキスト"
                        } else {
                            "テキスト+図"
                        },
                }
            ));
        }

        progress.on_step_complete(
            "OCR・図検出・Markdown生成",
            &format!("{}ページ, {}画像", page_count, images_count),
        );

        // Step 10: Merge all page markdowns
        progress.on_step_start("最終Markdown結合中...");
        let output_path = md_gen.merge_pages(&title, page_count)?;
        progress.on_step_complete("Markdown結合", &format!("{}", output_path.display()));

        // Cleanup work directory
        if !self.config.save_debug {
            std::fs::remove_dir_all(&work_dir).ok();
        }

        let elapsed = start_time.elapsed().as_secs_f64();

        Ok(MarkdownPipelineResult {
            page_count,
            output_path,
            images_count,
            elapsed_seconds: elapsed,
        })
    }

    /// Save images for a page (covers, full-page images, figure crops)
    /// Full-page images and covers are trimmed to their actual content area,
    /// removing scan margins and white borders.
    fn save_page_images(
        &self,
        image: &image::DynamicImage,
        page_index: usize,
        classification: &PageClassification,
        images_dir: &Path,
    ) -> Result<Vec<(FigureRegion, PathBuf)>, MarkdownPipelineError> {
        let mut saved = Vec::new();
        // Threshold for white detection (pixels brighter than this are "white")
        let content_threshold: u8 = 240;

        match classification {
            PageClassification::Cover => {
                let path = images_dir.join(format!("cover_{:03}.png", page_index + 1));
                let trimmed = FigureDetector::crop_to_content(image, content_threshold);
                trimmed
                    .save(&path)
                    .map_err(|e| MarkdownPipelineError::FigureDetectError(e.to_string()))?;
                saved.push((
                    FigureRegion {
                        bbox: (0, 0, trimmed.width(), trimmed.height()),
                        area: trimmed.width() * trimmed.height(),
                        region_type: crate::figure_detect::RegionType::Cover,
                    },
                    path,
                ));
            }
            PageClassification::FullPageImage => {
                let path = images_dir.join(format!("page_{:03}_full.png", page_index + 1));
                let trimmed = FigureDetector::crop_to_content(image, content_threshold);
                trimmed
                    .save(&path)
                    .map_err(|e| MarkdownPipelineError::FigureDetectError(e.to_string()))?;
                saved.push((
                    FigureRegion {
                        bbox: (0, 0, trimmed.width(), trimmed.height()),
                        area: trimmed.width() * trimmed.height(),
                        region_type: crate::figure_detect::RegionType::FullPageImage,
                    },
                    path,
                ));
            }
            PageClassification::Mixed { figures } => {
                for (fig_idx, figure) in figures.iter().enumerate() {
                    let path = images_dir.join(format!(
                        "page_{:03}_fig_{:03}.png",
                        page_index + 1,
                        fig_idx + 1
                    ));
                    let cropped = FigureDetector::crop_figure(image, figure);
                    cropped
                        .save(&path)
                        .map_err(|e| MarkdownPipelineError::FigureDetectError(e.to_string()))?;
                    saved.push((figure.clone(), path));
                }
            }
            PageClassification::TextOnly => {}
        }

        Ok(saved)
    }

    /// Create an empty OCR result for fallback when YomiToku is unavailable
    fn empty_ocr_result(image_path: &Path) -> OcrResult {
        OcrResult {
            input_path: image_path.to_path_buf(),
            text_blocks: vec![],
            confidence: 0.0,
            processing_time: std::time::Duration::from_secs(0),
            text_direction: crate::TextDirection::Vertical,
        }
    }

    // ============ Reused pipeline steps ============

    /// Margin trim step (reuses same logic as pipeline: simple fixed % crop)
    fn step_margin_trim<P: ProgressCallback>(
        &self,
        output_dir: &Path,
        images: &[PathBuf],
        _progress: &P,
    ) -> Result<Vec<PathBuf>, MarkdownPipelineError> {
        let trim_percent = self.config.margin_trim / 100.0;
        let mut output_paths = Vec::with_capacity(images.len());

        for (idx, img_path) in images.iter().enumerate() {
            let name = img_path
                .file_name()
                .map(|n| n.to_os_string())
                .unwrap_or_else(|| format!("page_{:04}.png", idx).into());
            let output_path = output_dir.join(&name);

            if let Ok(img) = image::open(img_path) {
                let (w, h) = (img.width(), img.height());
                let trim_x = (w as f64 * trim_percent) as u32;
                let trim_y = (h as f64 * trim_percent) as u32;
                let new_w = w.saturating_sub(trim_x * 2);
                let new_h = h.saturating_sub(trim_y * 2);

                if new_w > 0 && new_h > 0 {
                    let cropped = img.crop_imm(trim_x, trim_y, new_w, new_h);
                    cropped.save(&output_path).ok();
                } else {
                    img.save(&output_path).ok();
                }
            } else {
                std::fs::copy(img_path, &output_path)?;
            }
            output_paths.push(output_path);
        }

        Ok(output_paths)
    }

    /// 180-degree rotation detection and correction step
    fn step_rotation_detect<P: ProgressCallback>(
        &self,
        output_dir: &Path,
        images: &[PathBuf],
        progress: &P,
    ) -> Result<Vec<PathBuf>, MarkdownPipelineError> {
        let mut output_paths = Vec::with_capacity(images.len());
        let mut corrected = 0usize;

        for (idx, img_path) in images.iter().enumerate() {
            let name = img_path
                .file_name()
                .map(|n| n.to_os_string())
                .unwrap_or_else(|| format!("page_{:04}.png", idx).into());
            let output_path = output_dir.join(&name);

            match crate::ImageProcDeskewer::detect_upside_down(img_path) {
                Ok(true) => {
                    match crate::ImageProcDeskewer::correct_upside_down(img_path, &output_path) {
                        Ok(()) => {
                            corrected += 1;
                            progress.on_debug(&format!("page {} を180度回転補正", idx));
                        }
                        Err(_) => {
                            std::fs::copy(img_path, &output_path)?;
                        }
                    }
                }
                _ => {
                    std::fs::copy(img_path, &output_path)?;
                }
            }
            output_paths.push(output_path);
        }

        if corrected > 0 {
            progress.on_debug(&format!("{}ページの180度回転を補正", corrected));
        }
        Ok(output_paths)
    }

    /// Deskew step (reuses logic from pipeline)
    fn step_deskew<P: ProgressCallback>(
        &self,
        output_dir: &Path,
        images: &[PathBuf],
        progress: &P,
    ) -> Result<Vec<PathBuf>, MarkdownPipelineError> {
        let deskew_options = crate::DeskewOptions::builder()
            .algorithm(crate::DeskewAlgorithm::PageEdge)
            .build();

        let mut output_paths = Vec::with_capacity(images.len());

        for (idx, img_path) in images.iter().enumerate() {
            let name = img_path
                .file_name()
                .map(|n| n.to_os_string())
                .unwrap_or_else(|| format!("page_{:04}.png", idx).into());
            let output_path = output_dir.join(&name);

            match crate::ImageProcDeskewer::deskew(img_path, &output_path, &deskew_options) {
                Ok(_) => output_paths.push(output_path),
                Err(e) => {
                    progress.on_debug(&format!("傾き補正失敗 page {}: {}", idx, e));
                    std::fs::copy(img_path, &output_path)?;
                    output_paths.push(output_path);
                }
            }
        }

        Ok(output_paths)
    }

    /// AI upscale step (reuses same pattern as pipeline)
    fn step_upscale<P: ProgressCallback>(
        &self,
        output_dir: &Path,
        images: &[PathBuf],
        progress: &P,
    ) -> Result<Vec<PathBuf>, MarkdownPipelineError> {
        let venv_path = crate::resolve_venv_path();

        let bridge_config = crate::AiBridgeConfig::builder()
            .venv_path(venv_path)
            .build();

        let bridge = match crate::SubprocessBridge::new(bridge_config) {
            Ok(b) => b,
            Err(e) => {
                progress.on_warning(&format!("RealESRGAN利用不可: {}", e));
                return Ok(images.to_vec());
            }
        };

        let esrgan = crate::RealEsrgan::new(bridge);
        let mut options = crate::RealEsrganOptions::builder().scale(2);
        if self.config.gpu {
            options = options.gpu_id(0);
        }
        let options = options.build();

        match esrgan.upscale_batch(images, output_dir, &options, None) {
            Ok(result) => {
                progress.on_step_complete("超解像", &format!("{}画像", result.successful.len()));
                Ok(result
                    .successful
                    .iter()
                    .map(|r| r.output_path.clone())
                    .collect())
            }
            Err(e) => {
                progress.on_warning(&format!("超解像失敗: {}", e));
                Ok(images.to_vec())
            }
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_progress_state_new() {
        let state = ProgressState::new(10, Path::new("test.pdf"), "test");
        assert_eq!(state.total_pages, 10);
        assert!(state.processed_pages.is_empty());
        assert_eq!(state.title, "test");
    }

    #[test]
    fn test_progress_state_mark_processed() {
        let mut state = ProgressState::new(10, Path::new("test.pdf"), "test");
        state.mark_processed(0);
        state.mark_processed(5);
        state.mark_processed(0); // Duplicate

        assert!(state.is_processed(0));
        assert!(state.is_processed(5));
        assert!(!state.is_processed(3));
        assert_eq!(state.processed_pages.len(), 2);
    }

    #[test]
    fn test_progress_state_save_load() {
        let tmpdir = tempfile::tempdir().unwrap();
        let path = tmpdir.path().join("progress.json");

        let mut state = ProgressState::new(10, Path::new("test.pdf"), "テスト");
        state.mark_processed(0);
        state.mark_processed(3);
        state.save(&path).unwrap();

        let loaded = ProgressState::load(&path).unwrap();
        assert_eq!(loaded.total_pages, 10);
        assert_eq!(loaded.processed_pages, vec![0, 3]);
        assert_eq!(loaded.title, "テスト");
    }

    #[test]
    fn test_markdown_pipeline_from_args() {
        use crate::cli::Cli;
        use clap::Parser;

        let cli = Cli::try_parse_from([
            "superbook-pdf",
            "markdown",
            "input.pdf",
            "--dpi",
            "300",
            "--gpu",
        ])
        .unwrap();

        if let crate::cli::Commands::Markdown(args) = cli.command {
            let pipeline = MarkdownPipeline::from_args(&args);
            assert_eq!(pipeline.config.dpi, 300);
            assert!(pipeline.config.gpu);
            assert!(pipeline.config.ocr);
        } else {
            panic!("Expected Markdown command");
        }
    }

    // ============ Additional Tests (Issue #41+ quality assurance) ============

    #[test]
    fn test_progress_state_duplicate_mark() {
        let mut state = ProgressState::new(5, Path::new("test.pdf"), "test");
        state.mark_processed(2);
        state.mark_processed(2);
        state.mark_processed(2);
        assert_eq!(state.processed_pages.len(), 1);
        assert!(state.is_processed(2));
    }

    #[test]
    fn test_progress_state_sorted_order() {
        let mut state = ProgressState::new(10, Path::new("test.pdf"), "test");
        state.mark_processed(5);
        state.mark_processed(1);
        state.mark_processed(8);
        state.mark_processed(3);
        assert_eq!(state.processed_pages, vec![1, 3, 5, 8]);
    }

    #[test]
    fn test_progress_state_boundary_pages() {
        let mut state = ProgressState::new(100, Path::new("test.pdf"), "test");
        state.mark_processed(0);
        state.mark_processed(99);
        assert!(state.is_processed(0));
        assert!(state.is_processed(99));
        assert!(!state.is_processed(50));
        assert!(!state.is_processed(100)); // out of range but no panic
    }

    #[test]
    fn test_empty_ocr_result_structure() {
        let result = MarkdownPipeline::empty_ocr_result(Path::new("test.png"));
        assert!(result.text_blocks.is_empty());
        assert_eq!(result.confidence, 0.0);
        assert_eq!(result.input_path, PathBuf::from("test.png"));
        // Verify text_direction is explicitly set to Vertical (the fallback default for Japanese books)
        assert_eq!(
            result.text_direction,
            crate::TextDirection::Vertical,
            "empty_ocr_result should default to Vertical for Japanese book scanning"
        );
        // Verify processing_time is zero
        assert_eq!(
            result.processing_time,
            std::time::Duration::from_secs(0),
            "empty_ocr_result should have zero processing time"
        );
    }

    #[test]
    fn test_markdown_pipeline_no_upscale_no_deskew() {
        use crate::cli::Cli;
        use clap::Parser;

        let cli =
            Cli::try_parse_from(["superbook-pdf", "markdown", "input.pdf", "--no-deskew"]).unwrap();

        if let crate::cli::Commands::Markdown(args) = cli.command {
            let pipeline = MarkdownPipeline::from_args(&args);
            assert!(!pipeline.config.deskew);
            assert!(!pipeline.config.upscale);
            assert!(!pipeline.config.gpu);
        } else {
            panic!("Expected Markdown command");
        }
    }

    #[test]
    fn test_figure_options_sensitivity_zero() {
        use crate::cli::Cli;
        use clap::Parser;

        let cli = Cli::try_parse_from([
            "superbook-pdf",
            "markdown",
            "input.pdf",
            "--figure-sensitivity",
            "0.0",
        ])
        .unwrap();

        if let crate::cli::Commands::Markdown(args) = cli.command {
            let pipeline = MarkdownPipeline::from_args(&args);
            // sensitivity=0.0 → min_area_fraction = 0.05 * (1.0 - 0.0) = 0.05
            assert!((pipeline.figure_options.min_area_fraction - 0.05).abs() < f32::EPSILON);
        } else {
            panic!("Expected Markdown command");
        }
    }

    #[test]
    fn test_figure_options_sensitivity_one() {
        use crate::cli::Cli;
        use clap::Parser;

        let cli = Cli::try_parse_from([
            "superbook-pdf",
            "markdown",
            "input.pdf",
            "--figure-sensitivity",
            "1.0",
        ])
        .unwrap();

        if let crate::cli::Commands::Markdown(args) = cli.command {
            let pipeline = MarkdownPipeline::from_args(&args);
            // sensitivity=1.0 → min_area_fraction = 0.05 * (1.0 - 1.0) = 0.0
            assert!(pipeline.figure_options.min_area_fraction.abs() < f32::EPSILON);
        } else {
            panic!("Expected Markdown command");
        }
    }

    #[test]
    fn test_progress_state_load_corrupted_json() {
        let tmpdir = tempfile::tempdir().unwrap();
        let path = tmpdir.path().join("corrupted.json");

        // Write completely invalid JSON
        std::fs::write(&path, "this is not json at all {{{").unwrap();
        let result = ProgressState::load(&path);
        assert!(
            result.is_err(),
            "Loading corrupted JSON should return Err, not silently succeed"
        );

        // Verify the error is a JSON parse error, not some other error type
        let err = result.unwrap_err();
        assert!(
            matches!(err, MarkdownPipelineError::Json(_)),
            "Expected Json error variant, got {:?}",
            err
        );
    }

    #[test]
    fn test_progress_state_load_partial_json() {
        let tmpdir = tempfile::tempdir().unwrap();
        let path = tmpdir.path().join("partial.json");

        // Write valid JSON but missing required fields
        std::fs::write(&path, r#"{"total_pages": 5}"#).unwrap();
        let result = ProgressState::load(&path);
        assert!(
            result.is_err(),
            "Loading JSON with missing fields should return Err"
        );
    }

    #[test]
    fn test_progress_state_load_nonexistent_file() {
        let result = ProgressState::load(Path::new("/nonexistent/path/to/file.json"));
        assert!(
            result.is_err(),
            "Loading from nonexistent file should return Err"
        );
        let err = result.unwrap_err();
        assert!(
            matches!(err, MarkdownPipelineError::Io(_)),
            "Expected Io error variant for missing file, got {:?}",
            err
        );
    }

    #[test]
    fn test_progress_state_very_large_page_index() {
        let mut state = ProgressState::new(10, Path::new("test.pdf"), "test");
        // Mark a page index far beyond total_pages — should not panic
        state.mark_processed(usize::MAX);
        assert!(state.is_processed(usize::MAX));
        assert_eq!(state.processed_pages.len(), 1);

        // Verify it can be serialized and deserialized
        let tmpdir = tempfile::tempdir().unwrap();
        let path = tmpdir.path().join("large_idx.json");
        state.save(&path).unwrap();
        let loaded = ProgressState::load(&path).unwrap();
        assert!(loaded.is_processed(usize::MAX));
    }

    #[test]
    fn test_progress_state_empty_title() {
        let state = ProgressState::new(5, Path::new(""), "");
        assert_eq!(state.title, "");
        assert_eq!(state.input_pdf, PathBuf::from(""));

        // Verify serialization roundtrip with empty strings
        let tmpdir = tempfile::tempdir().unwrap();
        let path = tmpdir.path().join("empty_title.json");
        state.save(&path).unwrap();
        let loaded = ProgressState::load(&path).unwrap();
        assert_eq!(loaded.title, "");
    }

    #[test]
    fn test_figure_options_sensitivity_clamped() {
        use crate::cli::Cli;
        use clap::Parser;

        // Value > 1.0 should be clamped to 1.0
        let cli = Cli::try_parse_from([
            "superbook-pdf",
            "markdown",
            "input.pdf",
            "--figure-sensitivity",
            "5.0",
        ])
        .unwrap();

        if let crate::cli::Commands::Markdown(args) = cli.command {
            let pipeline = MarkdownPipeline::from_args(&args);
            // clamped to 1.0 → min_area_fraction = 0.0
            assert!(pipeline.figure_options.min_area_fraction.abs() < f32::EPSILON);
        } else {
            panic!("Expected Markdown command");
        }
    }

    #[test]
    fn test_progress_state_mark_idempotent() {
        let mut state = ProgressState::new(5, Path::new("test.pdf"), "test");
        state.mark_processed(2);
        state.mark_processed(2); // duplicate
        let completed: Vec<_> = (0..5).filter(|i| state.is_processed(*i)).collect();
        // Page 2 should appear exactly once in the completed set
        assert_eq!(
            completed.iter().filter(|&&p| p == 2).count(),
            1,
            "Duplicate mark should be idempotent"
        );
    }

    #[test]
    fn test_empty_ocr_result_processing_time_zero() {
        let result = MarkdownPipeline::empty_ocr_result(Path::new("zero.png"));
        assert_eq!(
            result.processing_time,
            std::time::Duration::ZERO,
            "Empty OCR processing time should be exactly zero"
        );
    }

    #[test]
    fn test_markdown_pipeline_default_config() {
        use crate::cli::Cli;
        use clap::Parser;

        let cli = Cli::try_parse_from(["superbook-pdf", "markdown", "input.pdf"]).unwrap();
        if let crate::cli::Commands::Markdown(args) = cli.command {
            let pipeline = MarkdownPipeline::from_args(&args);
            // Verify defaults
            assert!(pipeline.config.deskew, "deskew should be true by default");
            assert!(
                !pipeline.config.upscale,
                "upscale should be false by default"
            );
            assert_eq!(pipeline.config.dpi, 300, "DPI should default to 300");
        } else {
            panic!("Expected Markdown command");
        }
    }
}
