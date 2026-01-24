//! Page Number module core types
//!
//! Contains basic data structures for page number detection and offset analysis.

use std::path::{Path, PathBuf};
use thiserror::Error;

// ============================================================
// Constants
// ============================================================

/// Default search region percentage (percentage of image height)
pub const DEFAULT_SEARCH_REGION_PERCENT: f32 = 10.0;

/// Larger search region for vertical text (Japanese books)
pub const VERTICAL_SEARCH_REGION_PERCENT: f32 = 12.0;

/// Default minimum OCR confidence threshold
pub const DEFAULT_MIN_CONFIDENCE: f32 = 60.0;

/// Strict confidence threshold for high precision
pub const STRICT_MIN_CONFIDENCE: f32 = 80.0;

/// Minimum search region clamp value
pub const MIN_SEARCH_REGION: f32 = 5.0;

/// Maximum search region clamp value
pub const MAX_SEARCH_REGION: f32 = 50.0;

/// Minimum confidence clamp value
pub const MIN_CONFIDENCE_CLAMP: f32 = 0.0;

/// Maximum confidence clamp value
pub const MAX_CONFIDENCE_CLAMP: f32 = 100.0;

// ============================================================
// Error Types
// ============================================================

/// Page number detection error types
#[derive(Debug, Error)]
pub enum PageNumberError {
    #[error("Image not found: {0}")]
    ImageNotFound(PathBuf),

    #[error("OCR failed: {0}")]
    OcrFailed(String),

    #[error("No page numbers detected")]
    NoPageNumbersDetected,

    #[error("Inconsistent page numbers")]
    InconsistentPageNumbers,

    #[error("IO error: {0}")]
    IoError(#[from] std::io::Error),
}

pub type Result<T> = std::result::Result<T, PageNumberError>;

// ============================================================
// Core Data Structures
// ============================================================

/// Page number position types
#[derive(Debug, Clone, Copy, PartialEq)]
pub enum PageNumberPosition {
    /// Bottom center
    BottomCenter,
    /// Bottom outside (odd: right, even: left)
    BottomOutside,
    /// Bottom inside
    BottomInside,
    /// Top center
    TopCenter,
    /// Top outside
    TopOutside,
}

/// Page number rectangle
#[derive(Debug, Clone, Copy)]
pub struct PageNumberRect {
    pub x: u32,
    pub y: u32,
    pub width: u32,
    pub height: u32,
}

/// Detected page number
#[derive(Debug, Clone)]
pub struct DetectedPageNumber {
    /// Page index (0-indexed)
    pub page_index: usize,
    /// Detected number
    pub number: Option<i32>,
    /// Detection position
    pub position: PageNumberRect,
    /// OCR confidence
    pub confidence: f32,
    /// Raw OCR text
    pub raw_text: String,
}

/// Page number analysis result
#[derive(Debug, Clone)]
pub struct PageNumberAnalysis {
    /// Detection results for each page
    pub detections: Vec<DetectedPageNumber>,
    /// Detected position pattern
    pub position_pattern: PageNumberPosition,
    /// Odd page X offset (pixels)
    pub odd_page_offset_x: i32,
    /// Even page X offset
    pub even_page_offset_x: i32,
    /// Overall detection confidence
    pub overall_confidence: f32,
    /// Missing page numbers
    pub missing_pages: Vec<usize>,
    /// Duplicate page numbers
    pub duplicate_pages: Vec<i32>,
}

/// Offset correction result
#[derive(Debug, Clone)]
pub struct OffsetCorrection {
    /// Per-page horizontal offset
    pub page_offsets: Vec<(usize, i32)>,
    /// Recommended unified offset
    pub unified_offset: i32,
}

// ============================================================
// Fallback Matching Types (Phase 2.1)
// ============================================================

/// 2D Point
#[derive(Debug, Clone, Copy, PartialEq, Default)]
pub struct Point {
    pub x: i32,
    pub y: i32,
}

impl Point {
    /// Create a new point
    pub fn new(x: i32, y: i32) -> Self {
        Self { x, y }
    }

    /// Calculate distance to another point
    pub fn distance_to(&self, other: &Point) -> f64 {
        let dx = (self.x - other.x) as f64;
        let dy = (self.y - other.y) as f64;
        (dx * dx + dy * dy).sqrt()
    }
}

/// Rectangle for search region
#[derive(Debug, Clone, Copy, PartialEq)]
pub struct Rectangle {
    pub x: i32,
    pub y: i32,
    pub width: u32,
    pub height: u32,
}

impl Rectangle {
    /// Create a new rectangle
    pub fn new(x: i32, y: i32, width: u32, height: u32) -> Self {
        Self { x, y, width, height }
    }

    /// Check if a point is inside the rectangle
    pub fn contains(&self, px: i32, py: i32) -> bool {
        px >= self.x
            && px < self.x + self.width as i32
            && py >= self.y
            && py < self.y + self.height as i32
    }

    /// Check if this rectangle overlaps with another
    pub fn overlaps(&self, other: &Rectangle) -> bool {
        self.x < other.x + other.width as i32
            && self.x + self.width as i32 > other.x
            && self.y < other.y + other.height as i32
            && self.y + self.height as i32 > other.y
    }

    /// Get center point
    pub fn center(&self) -> (i32, i32) {
        (
            self.x + self.width as i32 / 2,
            self.y + self.height as i32 / 2,
        )
    }

    /// Calculate distance from center to a point
    pub fn distance_to(&self, px: i32, py: i32) -> f64 {
        let (cx, cy) = self.center();
        (((px - cx).pow(2) + (py - cy).pow(2)) as f64).sqrt()
    }

    /// Expand rectangle by margin percentage
    pub fn expand(&self, margin_percent: f32) -> Self {
        let margin_x = (self.width as f32 * margin_percent / 100.0) as i32;
        let margin_y = (self.height as f32 * margin_percent / 100.0) as i32;
        Self {
            x: self.x - margin_x,
            y: self.y - margin_y,
            width: self.width + (margin_x * 2) as u32,
            height: self.height + (margin_y * 2) as u32,
        }
    }

    /// Calculate area
    pub fn area(&self) -> u64 {
        self.width as u64 * self.height as u64
    }

    /// Calculate intersection with another rectangle
    pub fn intersection(&self, other: &Rectangle) -> Option<Rectangle> {
        let x1 = self.x.max(other.x);
        let y1 = self.y.max(other.y);
        let x2 = (self.x + self.width as i32).min(other.x + other.width as i32);
        let y2 = (self.y + self.height as i32).min(other.y + other.height as i32);

        if x2 > x1 && y2 > y1 {
            Some(Rectangle {
                x: x1,
                y: y1,
                width: (x2 - x1) as u32,
                height: (y2 - y1) as u32,
            })
        } else {
            None
        }
    }

    /// Check if this rectangle contains another rectangle completely
    pub fn contains_rect(&self, other: &Rectangle) -> bool {
        other.x >= self.x
            && other.y >= self.y
            && other.x + other.width as i32 <= self.x + self.width as i32
            && other.y + other.height as i32 <= self.y + self.height as i32
    }

    /// Get center point
    pub fn center_point(&self) -> Point {
        let (x, y) = self.center();
        Point::new(x, y)
    }
}

/// Page number candidate from OCR
#[derive(Debug, Clone)]
pub struct PageNumberCandidate {
    /// Detected text
    pub text: String,
    /// Parsed number (if parseable)
    pub number: Option<u32>,
    /// Bounding box
    pub bbox: Rectangle,
    /// OCR confidence (0.0 - 1.0)
    pub confidence: f32,
    /// Whether this candidate was successfully parsed
    pub ocr_success: bool,
}

impl PageNumberCandidate {
    /// Create a new candidate
    pub fn new(text: String, bbox: Rectangle, confidence: f32) -> Self {
        let number = text.trim().parse::<u32>().ok();
        let ocr_success = number.is_some() || !text.trim().is_empty();
        Self {
            text,
            number,
            bbox,
            confidence,
            ocr_success,
        }
    }

    /// Calculate distance to a reference point
    pub fn distance_to(&self, px: i32, py: i32) -> f64 {
        self.bbox.distance_to(px, py)
    }
}

/// Matching stage for fallback matching
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum MatchStage {
    /// Stage 1: Exact match + within region + minimum distance
    ExactMatch,
    /// Stage 2: Maximum similarity (Jaro-Winkler) + within region
    SimilarityMatch,
    /// Stage 3: OCR success region + minimum distance
    OcrSuccessMatch,
    /// Stage 4: All detected regions + minimum distance
    FallbackMatch,
}

impl MatchStage {
    /// Get stage number (1-4)
    pub fn stage_number(&self) -> u8 {
        match self {
            MatchStage::ExactMatch => 1,
            MatchStage::SimilarityMatch => 2,
            MatchStage::OcrSuccessMatch => 3,
            MatchStage::FallbackMatch => 4,
        }
    }

    /// Get stage description
    pub fn description(&self) -> &'static str {
        match self {
            MatchStage::ExactMatch => "Exact match + within region + minimum distance",
            MatchStage::SimilarityMatch => "Maximum similarity (Jaro-Winkler) + within region",
            MatchStage::OcrSuccessMatch => "OCR success region + minimum distance",
            MatchStage::FallbackMatch => "Fallback: All detected regions + minimum distance",
        }
    }
}

/// Page number match result
#[derive(Debug, Clone)]
pub struct PageNumberMatch {
    /// Matched candidate
    pub candidate: PageNumberCandidate,
    /// Stage at which the match was found
    pub stage: MatchStage,
    /// Match score (interpretation depends on stage)
    pub score: f64,
    /// Distance from reference point
    pub distance: f64,
    /// Expected number that was being searched for
    pub expected_number: u32,
}

impl PageNumberMatch {
    /// Create a new match
    pub fn new(
        candidate: PageNumberCandidate,
        stage: MatchStage,
        score: f64,
        distance: f64,
        expected_number: u32,
    ) -> Self {
        Self {
            candidate,
            stage,
            score,
            distance,
            expected_number,
        }
    }

    /// Check if this is an exact match
    pub fn is_exact(&self) -> bool {
        self.stage == MatchStage::ExactMatch
    }

    /// Get match quality (higher is better)
    pub fn quality(&self) -> f64 {
        match self.stage {
            MatchStage::ExactMatch => 1.0 + self.score,
            MatchStage::SimilarityMatch => 0.75 + self.score * 0.25,
            MatchStage::OcrSuccessMatch => 0.5 + (1.0 - self.distance / 1000.0).max(0.0) * 0.25,
            MatchStage::FallbackMatch => 0.25 + (1.0 - self.distance / 1000.0).max(0.0) * 0.25,
        }
    }
}

// ============================================================
// Options
// ============================================================

/// Page number detection options
#[derive(Debug, Clone)]
pub struct PageNumberOptions {
    /// Search region (percentage of image height to search)
    pub search_region_percent: f32,
    /// OCR language
    pub ocr_language: String,
    /// Minimum confidence threshold
    pub min_confidence: f32,
    /// Detect numbers only
    pub numbers_only: bool,
    /// Position hint
    pub position_hint: Option<PageNumberPosition>,
}

impl Default for PageNumberOptions {
    fn default() -> Self {
        Self {
            search_region_percent: DEFAULT_SEARCH_REGION_PERCENT,
            ocr_language: "jpn+eng".to_string(),
            min_confidence: DEFAULT_MIN_CONFIDENCE,
            numbers_only: true,
            position_hint: None,
        }
    }
}

impl PageNumberOptions {
    /// Create a new options builder
    pub fn builder() -> PageNumberOptionsBuilder {
        PageNumberOptionsBuilder::default()
    }

    /// Create options for Japanese documents
    pub fn japanese() -> Self {
        Self {
            ocr_language: "jpn".to_string(),
            search_region_percent: VERTICAL_SEARCH_REGION_PERCENT,
            ..Default::default()
        }
    }

    /// Create options for English documents
    pub fn english() -> Self {
        Self {
            ocr_language: "eng".to_string(),
            ..Default::default()
        }
    }

    /// Create options with high confidence threshold
    pub fn strict() -> Self {
        Self {
            min_confidence: STRICT_MIN_CONFIDENCE,
            ..Default::default()
        }
    }
}

/// Builder for PageNumberOptions
#[derive(Debug, Default)]
pub struct PageNumberOptionsBuilder {
    options: PageNumberOptions,
}

impl PageNumberOptionsBuilder {
    /// Set search region (percentage of image height, clamped to 5-50)
    #[must_use]
    pub fn search_region_percent(mut self, percent: f32) -> Self {
        self.options.search_region_percent = percent.clamp(MIN_SEARCH_REGION, MAX_SEARCH_REGION);
        self
    }

    /// Set OCR language
    #[must_use]
    pub fn ocr_language(mut self, lang: impl Into<String>) -> Self {
        self.options.ocr_language = lang.into();
        self
    }

    /// Set minimum confidence threshold (clamped to 0-100)
    #[must_use]
    pub fn min_confidence(mut self, confidence: f32) -> Self {
        self.options.min_confidence = confidence.clamp(MIN_CONFIDENCE_CLAMP, MAX_CONFIDENCE_CLAMP);
        self
    }

    /// Set whether to detect numbers only
    #[must_use]
    pub fn numbers_only(mut self, only: bool) -> Self {
        self.options.numbers_only = only;
        self
    }

    /// Set position hint
    #[must_use]
    pub fn position_hint(mut self, position: PageNumberPosition) -> Self {
        self.options.position_hint = Some(position);
        self
    }

    /// Build the options
    #[must_use]
    pub fn build(self) -> PageNumberOptions {
        self.options
    }
}

// ============================================================
// Detector Trait
// ============================================================

/// Page number detector trait
pub trait PageNumberDetector {
    /// Detect page number from single image
    fn detect_single(
        image_path: &Path,
        page_index: usize,
        options: &PageNumberOptions,
    ) -> Result<DetectedPageNumber>;

    /// Analyze multiple images
    fn analyze_batch(images: &[PathBuf], options: &PageNumberOptions)
        -> Result<PageNumberAnalysis>;

    /// Calculate offset correction
    fn calculate_offset(
        analysis: &PageNumberAnalysis,
        image_width: u32,
    ) -> Result<OffsetCorrection>;

    /// Validate page order
    fn validate_order(analysis: &PageNumberAnalysis) -> Result<bool>;
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_page_number_options_default() {
        let opts = PageNumberOptions::default();
        assert_eq!(opts.search_region_percent, 10.0);
        assert_eq!(opts.min_confidence, 60.0);
        assert!(opts.numbers_only);
    }

    #[test]
    fn test_page_number_options_japanese() {
        let opts = PageNumberOptions::japanese();
        assert_eq!(opts.ocr_language, "jpn");
        assert_eq!(opts.search_region_percent, 12.0);
    }

    #[test]
    fn test_page_number_options_english() {
        let opts = PageNumberOptions::english();
        assert_eq!(opts.ocr_language, "eng");
    }

    #[test]
    fn test_page_number_options_strict() {
        let opts = PageNumberOptions::strict();
        assert_eq!(opts.min_confidence, 80.0);
    }

    #[test]
    fn test_page_number_options_builder() {
        let opts = PageNumberOptions::builder()
            .search_region_percent(15.0)
            .ocr_language("fra")
            .min_confidence(75.0)
            .numbers_only(false)
            .position_hint(PageNumberPosition::BottomCenter)
            .build();

        assert_eq!(opts.search_region_percent, 15.0);
        assert_eq!(opts.ocr_language, "fra");
        assert_eq!(opts.min_confidence, 75.0);
        assert!(!opts.numbers_only);
        assert!(matches!(
            opts.position_hint,
            Some(PageNumberPosition::BottomCenter)
        ));
    }

    #[test]
    fn test_builder_clamping() {
        // Search region clamped to 5-50
        let opts = PageNumberOptions::builder()
            .search_region_percent(100.0)
            .build();
        assert_eq!(opts.search_region_percent, 50.0);

        let opts = PageNumberOptions::builder()
            .search_region_percent(1.0)
            .build();
        assert_eq!(opts.search_region_percent, 5.0);

        // Confidence clamped to 0-100
        let opts = PageNumberOptions::builder().min_confidence(150.0).build();
        assert_eq!(opts.min_confidence, 100.0);

        let opts = PageNumberOptions::builder().min_confidence(-10.0).build();
        assert_eq!(opts.min_confidence, 0.0);
    }

    #[test]
    fn test_page_number_position_variants() {
        let positions = [
            PageNumberPosition::BottomCenter,
            PageNumberPosition::BottomOutside,
            PageNumberPosition::BottomInside,
            PageNumberPosition::TopCenter,
            PageNumberPosition::TopOutside,
        ];

        for pos in positions {
            let _clone = pos;
            assert!(matches!(
                pos,
                PageNumberPosition::BottomCenter
                    | PageNumberPosition::BottomOutside
                    | PageNumberPosition::BottomInside
                    | PageNumberPosition::TopCenter
                    | PageNumberPosition::TopOutside
            ));
        }
    }

    #[test]
    fn test_page_number_rect() {
        let rect = PageNumberRect {
            x: 100,
            y: 200,
            width: 50,
            height: 30,
        };
        assert_eq!(rect.x, 100);
        assert_eq!(rect.y, 200);
        assert_eq!(rect.width, 50);
        assert_eq!(rect.height, 30);
    }

    #[test]
    fn test_detected_page_number() {
        let detected = DetectedPageNumber {
            page_index: 5,
            number: Some(42),
            position: PageNumberRect {
                x: 100,
                y: 900,
                width: 50,
                height: 30,
            },
            confidence: 95.5,
            raw_text: "42".to_string(),
        };

        assert_eq!(detected.page_index, 5);
        assert_eq!(detected.number, Some(42));
        assert_eq!(detected.confidence, 95.5);
        assert_eq!(detected.raw_text, "42");
    }

    #[test]
    fn test_error_types() {
        let _err1 = PageNumberError::ImageNotFound(PathBuf::from("/test/path"));
        let _err2 = PageNumberError::OcrFailed("OCR error".to_string());
        let _err3 = PageNumberError::NoPageNumbersDetected;
        let _err4 = PageNumberError::InconsistentPageNumbers;
        let _err5: PageNumberError = std::io::Error::other("test").into();
    }

    // ============================================================
    // Fallback Matching Types Tests (Phase 2.1)
    // ============================================================

    #[test]
    fn test_rectangle_new() {
        let rect = Rectangle::new(10, 20, 100, 50);
        assert_eq!(rect.x, 10);
        assert_eq!(rect.y, 20);
        assert_eq!(rect.width, 100);
        assert_eq!(rect.height, 50);
    }

    #[test]
    fn test_rectangle_contains() {
        let rect = Rectangle::new(0, 0, 100, 100);
        assert!(rect.contains(50, 50));
        assert!(rect.contains(0, 0));
        assert!(rect.contains(99, 99));
        assert!(!rect.contains(100, 50)); // edge
        assert!(!rect.contains(-1, 50));
        assert!(!rect.contains(50, 100));
    }

    #[test]
    fn test_rectangle_overlaps() {
        let rect1 = Rectangle::new(0, 0, 100, 100);
        let rect2 = Rectangle::new(50, 50, 100, 100);
        let rect3 = Rectangle::new(200, 200, 50, 50);

        assert!(rect1.overlaps(&rect2));
        assert!(rect2.overlaps(&rect1));
        assert!(!rect1.overlaps(&rect3));
    }

    #[test]
    fn test_rectangle_center() {
        let rect = Rectangle::new(0, 0, 100, 200);
        assert_eq!(rect.center(), (50, 100));

        let rect2 = Rectangle::new(10, 20, 100, 100);
        assert_eq!(rect2.center(), (60, 70));
    }

    #[test]
    fn test_rectangle_distance_to() {
        let rect = Rectangle::new(0, 0, 100, 100);
        // Center is at (50, 50)
        assert!((rect.distance_to(50, 50) - 0.0).abs() < 0.001);
        assert!((rect.distance_to(50, 100) - 50.0).abs() < 0.001);
    }

    #[test]
    fn test_rectangle_expand() {
        let rect = Rectangle::new(100, 100, 100, 100);
        let expanded = rect.expand(10.0); // 10% margin
        assert_eq!(expanded.x, 90);
        assert_eq!(expanded.y, 90);
        assert_eq!(expanded.width, 120);
        assert_eq!(expanded.height, 120);
    }

    #[test]
    fn test_page_number_candidate_new() {
        let candidate = PageNumberCandidate::new(
            "42".to_string(),
            Rectangle::new(100, 900, 50, 30),
            0.95,
        );
        assert_eq!(candidate.text, "42");
        assert_eq!(candidate.number, Some(42));
        assert!(candidate.ocr_success);
        assert_eq!(candidate.confidence, 0.95);
    }

    #[test]
    fn test_page_number_candidate_no_number() {
        let candidate = PageNumberCandidate::new(
            "abc".to_string(),
            Rectangle::new(100, 900, 50, 30),
            0.80,
        );
        assert_eq!(candidate.text, "abc");
        assert_eq!(candidate.number, None);
        assert!(candidate.ocr_success); // still considered success if text detected
    }

    #[test]
    fn test_page_number_candidate_empty() {
        let candidate = PageNumberCandidate::new(
            "".to_string(),
            Rectangle::new(100, 900, 50, 30),
            0.50,
        );
        assert!(!candidate.ocr_success);
    }

    #[test]
    fn test_match_stage_number() {
        assert_eq!(MatchStage::ExactMatch.stage_number(), 1);
        assert_eq!(MatchStage::SimilarityMatch.stage_number(), 2);
        assert_eq!(MatchStage::OcrSuccessMatch.stage_number(), 3);
        assert_eq!(MatchStage::FallbackMatch.stage_number(), 4);
    }

    #[test]
    fn test_match_stage_description() {
        assert!(MatchStage::ExactMatch.description().contains("Exact"));
        assert!(MatchStage::SimilarityMatch.description().contains("similarity"));
        assert!(MatchStage::OcrSuccessMatch.description().contains("OCR"));
        assert!(MatchStage::FallbackMatch.description().contains("Fallback"));
    }

    #[test]
    fn test_page_number_match_new() {
        let candidate = PageNumberCandidate::new(
            "42".to_string(),
            Rectangle::new(100, 900, 50, 30),
            0.95,
        );
        let match_result = PageNumberMatch::new(
            candidate,
            MatchStage::ExactMatch,
            1.0,
            10.0,
            42,
        );
        assert!(match_result.is_exact());
        assert_eq!(match_result.expected_number, 42);
        assert_eq!(match_result.distance, 10.0);
    }

    #[test]
    fn test_page_number_match_quality() {
        let candidate = PageNumberCandidate::new(
            "42".to_string(),
            Rectangle::new(100, 900, 50, 30),
            0.95,
        );

        let exact_match = PageNumberMatch::new(
            candidate.clone(),
            MatchStage::ExactMatch,
            1.0,
            10.0,
            42,
        );
        let fallback_match = PageNumberMatch::new(
            candidate,
            MatchStage::FallbackMatch,
            0.5,
            10.0,
            42,
        );

        // Exact match should have higher quality
        assert!(exact_match.quality() > fallback_match.quality());
    }
}
