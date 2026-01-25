//! Page Number Detection module
//!
//! Provides functionality to detect page numbers and calculate offsets.
//!
//! # Features
//!
//! - Tesseract-based OCR page number detection
//! - Roman numeral parsing
//! - Physical-to-logical page number shift calculation
//! - Per-page offset alignment
//! - Odd/even page grouping
//!
//! # Example
//!
//! ```rust,no_run
//! use superbook_pdf::{PageNumberOptions, TesseractPageDetector};
//! use std::path::PathBuf;
//!
//! let options = PageNumberOptions::builder()
//!     .ocr_language("jpn")
//!     .min_confidence(70.0)
//!     .build();
//!
//! let images: Vec<PathBuf> = vec![
//!     PathBuf::from("page_001.png"),
//!     PathBuf::from("page_002.png"),
//! ];
//!
//! let analysis = TesseractPageDetector::analyze_batch(&images, &options).unwrap();
//! println!("Position pattern: {:?}", analysis.position_pattern);
//! ```

// Submodules
mod detect;
mod offset;
mod types;

// Re-export public API
pub use detect::{
    find_page_number_with_fallback, find_page_numbers_batch, FallbackMatchStats,
    TesseractPageDetector,
};
pub use offset::{
    calc_group_reference_position, calc_overlap_center, BookOffsetAnalysis, PageOffsetAnalyzer,
    PageOffsetResult,
};
pub use types::{
    DetectedPageNumber, MatchStage, OffsetCorrection, PageNumberAnalysis, PageNumberCandidate,
    PageNumberDetector, PageNumberError, PageNumberMatch, PageNumberOptions,
    PageNumberOptionsBuilder, PageNumberPosition, PageNumberRect, Point, Rectangle, Result,
};

#[cfg(test)]
mod tests {
    use super::*;
    use std::path::{Path, PathBuf};

    #[test]
    fn test_page_number_options_default() {
        let opts = PageNumberOptions::default();
        assert_eq!(opts.search_region_percent, 10.0);
        assert_eq!(opts.min_confidence, 60.0);
        assert!(opts.numbers_only);
        assert_eq!(opts.ocr_language, "jpn+eng");
    }

    #[test]
    fn test_page_number_options_presets() {
        let jp = PageNumberOptions::japanese();
        assert_eq!(jp.ocr_language, "jpn");
        assert_eq!(jp.search_region_percent, 12.0);

        let en = PageNumberOptions::english();
        assert_eq!(en.ocr_language, "eng");

        let strict = PageNumberOptions::strict();
        assert_eq!(strict.min_confidence, 80.0);
    }

    #[test]
    fn test_tesseract_detect_nonexistent() {
        let options = PageNumberOptions::default();
        let result =
            TesseractPageDetector::detect_single(Path::new("/nonexistent/image.png"), 0, &options);
        assert!(matches!(result, Err(PageNumberError::ImageNotFound(_))));
    }

    #[test]
    fn test_roman_numeral_parsing() {
        assert_eq!(TesseractPageDetector::parse_roman_numeral("I"), Some(1));
        assert_eq!(TesseractPageDetector::parse_roman_numeral("V"), Some(5));
        assert_eq!(TesseractPageDetector::parse_roman_numeral("X"), Some(10));
        assert_eq!(TesseractPageDetector::parse_roman_numeral("L"), Some(50));
        assert_eq!(TesseractPageDetector::parse_roman_numeral("C"), Some(100));
        assert_eq!(TesseractPageDetector::parse_roman_numeral("D"), Some(500));
        assert_eq!(TesseractPageDetector::parse_roman_numeral("M"), Some(1000));
        assert_eq!(
            TesseractPageDetector::parse_roman_numeral("MCMXCIX"),
            Some(1999)
        );
    }

    #[test]
    fn test_empty_analysis() {
        let images: Vec<PathBuf> = vec![];
        let options = PageNumberOptions::default();
        let analysis = TesseractPageDetector::analyze_batch(&images, &options).unwrap();
        assert!(analysis.detections.is_empty());
    }

    #[test]
    fn test_page_offset_result() {
        let result = PageOffsetResult::no_offset(1);
        assert_eq!(result.physical_page, 1);
        assert!(result.is_odd);
        assert_eq!(result.shift_x, 0);
        assert_eq!(result.shift_y, 0);
    }

    #[test]
    fn test_book_offset_analysis() {
        let analysis = BookOffsetAnalysis::default();
        assert!(!analysis.is_reliable(10));
    }

    #[test]
    fn test_position_pattern_variants() {
        let patterns = [
            PageNumberPosition::BottomCenter,
            PageNumberPosition::BottomOutside,
            PageNumberPosition::BottomInside,
            PageNumberPosition::TopCenter,
            PageNumberPosition::TopOutside,
        ];
        for pattern in patterns {
            let _copy = pattern;
        }
    }

    #[test]
    fn test_error_types() {
        let _err1 = PageNumberError::ImageNotFound(PathBuf::from("/test"));
        let _err2 = PageNumberError::OcrFailed("fail".to_string());
        let _err3 = PageNumberError::NoPageNumbersDetected;
        let _err4 = PageNumberError::InconsistentPageNumbers;
    }
}
