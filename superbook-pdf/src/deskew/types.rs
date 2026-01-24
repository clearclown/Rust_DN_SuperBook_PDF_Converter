//! Deskew module core types
//!
//! Contains basic data structures for skew detection and correction.

use std::path::{Path, PathBuf};
use thiserror::Error;

// ============================================================
// Constants
// ============================================================

/// Default maximum angle for deskew detection (degrees)
pub const DEFAULT_MAX_ANGLE: f64 = 15.0;

/// Default threshold angle - angles below this are not corrected (degrees)
pub const DEFAULT_THRESHOLD_ANGLE: f64 = 0.1;

/// Default background color (white) for filled areas after rotation
pub const DEFAULT_BACKGROUND_COLOR: [u8; 3] = [255, 255, 255];

/// Grayscale threshold for binarization in projection analysis
pub const GRAYSCALE_THRESHOLD: u8 = 128;

/// White pixel value for image processing
pub const WHITE_PIXEL: u8 = 255;

/// Fully opaque alpha value for RGBA images
pub const ALPHA_OPAQUE: u8 = 255;

// ============================================================
// Error Types
// ============================================================

/// Deskew error types
#[derive(Debug, Error)]
pub enum DeskewError {
    #[error("Image not found: {0}")]
    ImageNotFound(PathBuf),

    #[error("Invalid image format: {0}")]
    InvalidFormat(String),

    #[error("Detection failed: {0}")]
    DetectionFailed(String),

    #[error("Correction failed: {0}")]
    CorrectionFailed(String),

    #[error("IO error: {0}")]
    IoError(#[from] std::io::Error),
}

pub type Result<T> = std::result::Result<T, DeskewError>;

// ============================================================
// Options and Enums
// ============================================================

/// Deskew detection algorithms
#[derive(Debug, Clone, Copy, Default)]
pub enum DeskewAlgorithm {
    /// Hough line transform
    #[default]
    HoughLines,
    /// Projection profile method
    ProjectionProfile,
    /// Text line detection
    TextLineDetection,
    /// Combined (average of multiple methods)
    Combined,
}

/// Quality modes for rotation
#[derive(Debug, Clone, Copy, Default)]
pub enum QualityMode {
    /// Fast (bilinear interpolation)
    Fast,
    /// Standard (bicubic interpolation)
    #[default]
    Standard,
    /// High quality (Lanczos interpolation)
    HighQuality,
}

/// Deskew detection options
#[derive(Debug, Clone)]
pub struct DeskewOptions {
    /// Detection algorithm
    pub algorithm: DeskewAlgorithm,
    /// Maximum detection angle (degrees)
    pub max_angle: f64,
    /// Correction threshold (angles below this are ignored)
    pub threshold_angle: f64,
    /// Background color for filled areas after rotation
    pub background_color: [u8; 3],
    /// Quality mode for interpolation
    pub quality_mode: QualityMode,
}

impl Default for DeskewOptions {
    fn default() -> Self {
        Self {
            algorithm: DeskewAlgorithm::HoughLines,
            max_angle: DEFAULT_MAX_ANGLE,
            threshold_angle: DEFAULT_THRESHOLD_ANGLE,
            background_color: DEFAULT_BACKGROUND_COLOR,
            quality_mode: QualityMode::Standard,
        }
    }
}

impl DeskewOptions {
    /// Create a new options builder
    pub fn builder() -> DeskewOptionsBuilder {
        DeskewOptionsBuilder::default()
    }

    /// Create options optimized for high quality output
    pub fn high_quality() -> Self {
        Self {
            algorithm: DeskewAlgorithm::Combined,
            quality_mode: QualityMode::HighQuality,
            ..Default::default()
        }
    }

    /// Create options optimized for fast processing
    pub fn fast() -> Self {
        Self {
            algorithm: DeskewAlgorithm::ProjectionProfile,
            quality_mode: QualityMode::Fast,
            threshold_angle: 0.5, // Skip small corrections
            ..Default::default()
        }
    }
}

/// Builder for DeskewOptions
#[derive(Debug, Default)]
pub struct DeskewOptionsBuilder {
    options: DeskewOptions,
}

impl DeskewOptionsBuilder {
    /// Set the detection algorithm
    #[must_use]
    pub fn algorithm(mut self, algorithm: DeskewAlgorithm) -> Self {
        self.options.algorithm = algorithm;
        self
    }

    /// Set the maximum detection angle
    #[must_use]
    pub fn max_angle(mut self, angle: f64) -> Self {
        self.options.max_angle = angle.abs();
        self
    }

    /// Set the correction threshold angle
    #[must_use]
    pub fn threshold_angle(mut self, angle: f64) -> Self {
        self.options.threshold_angle = angle.abs();
        self
    }

    /// Set the background color for rotated areas
    #[must_use]
    pub fn background_color(mut self, color: [u8; 3]) -> Self {
        self.options.background_color = color;
        self
    }

    /// Set the quality mode
    #[must_use]
    pub fn quality_mode(mut self, mode: QualityMode) -> Self {
        self.options.quality_mode = mode;
        self
    }

    /// Build the options
    #[must_use]
    pub fn build(self) -> DeskewOptions {
        self.options
    }
}

// ============================================================
// Result Types
// ============================================================

/// Skew detection result
#[derive(Debug, Clone)]
pub struct SkewDetection {
    /// Detected angle in degrees (positive = clockwise)
    pub angle: f64,
    /// Detection confidence (0.0 - 1.0)
    pub confidence: f64,
    /// Number of features used for detection
    pub feature_count: usize,
}

/// Deskew operation result
#[derive(Debug)]
pub struct DeskewResult {
    /// Original detection result
    pub detection: SkewDetection,
    /// Whether correction was applied
    pub corrected: bool,
    /// Output image path
    pub output_path: PathBuf,
    /// Original image size
    pub original_size: (u32, u32),
    /// Corrected image size
    pub corrected_size: (u32, u32),
}

// ============================================================
// Deskewer Trait
// ============================================================

/// Deskewer trait
pub trait Deskewer {
    /// Detect skew angle
    fn detect_skew(image_path: &Path, options: &DeskewOptions) -> Result<SkewDetection>;

    /// Correct skew
    fn correct_skew(
        input_path: &Path,
        output_path: &Path,
        options: &DeskewOptions,
    ) -> Result<DeskewResult>;

    /// Detect and correct in one operation
    fn deskew(
        input_path: &Path,
        output_path: &Path,
        options: &DeskewOptions,
    ) -> Result<DeskewResult>;

    /// Batch processing
    fn deskew_batch(
        images: &[(PathBuf, PathBuf)],
        options: &DeskewOptions,
    ) -> Vec<Result<DeskewResult>>;
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_deskew_options_default() {
        let opts = DeskewOptions::default();
        assert_eq!(opts.max_angle, 15.0);
        assert_eq!(opts.threshold_angle, 0.1);
        assert_eq!(opts.background_color, [255, 255, 255]);
        assert!(matches!(opts.algorithm, DeskewAlgorithm::HoughLines));
        assert!(matches!(opts.quality_mode, QualityMode::Standard));
    }

    #[test]
    fn test_deskew_options_high_quality() {
        let opts = DeskewOptions::high_quality();
        assert!(matches!(opts.algorithm, DeskewAlgorithm::Combined));
        assert!(matches!(opts.quality_mode, QualityMode::HighQuality));
    }

    #[test]
    fn test_deskew_options_fast() {
        let opts = DeskewOptions::fast();
        assert!(matches!(opts.algorithm, DeskewAlgorithm::ProjectionProfile));
        assert!(matches!(opts.quality_mode, QualityMode::Fast));
        assert_eq!(opts.threshold_angle, 0.5);
    }

    #[test]
    fn test_deskew_options_builder() {
        let opts = DeskewOptions::builder()
            .algorithm(DeskewAlgorithm::TextLineDetection)
            .max_angle(20.0)
            .threshold_angle(0.3)
            .background_color([0, 0, 0])
            .quality_mode(QualityMode::HighQuality)
            .build();

        assert!(matches!(opts.algorithm, DeskewAlgorithm::TextLineDetection));
        assert_eq!(opts.max_angle, 20.0);
        assert_eq!(opts.threshold_angle, 0.3);
        assert_eq!(opts.background_color, [0, 0, 0]);
        assert!(matches!(opts.quality_mode, QualityMode::HighQuality));
    }

    #[test]
    fn test_builder_abs_angle() {
        let opts = DeskewOptions::builder().max_angle(-10.0).build();
        assert_eq!(opts.max_angle, 10.0);

        let opts = DeskewOptions::builder().threshold_angle(-0.5).build();
        assert_eq!(opts.threshold_angle, 0.5);
    }

    #[test]
    fn test_skew_detection() {
        let detection = SkewDetection {
            angle: 2.5,
            confidence: 0.95,
            feature_count: 150,
        };
        assert_eq!(detection.angle, 2.5);
        assert_eq!(detection.confidence, 0.95);
        assert_eq!(detection.feature_count, 150);
    }

    #[test]
    fn test_algorithm_variants() {
        let algorithms = [
            DeskewAlgorithm::HoughLines,
            DeskewAlgorithm::ProjectionProfile,
            DeskewAlgorithm::TextLineDetection,
            DeskewAlgorithm::Combined,
        ];
        for alg in algorithms {
            let _copy = alg;
        }
    }

    #[test]
    fn test_quality_mode_variants() {
        let modes = [
            QualityMode::Fast,
            QualityMode::Standard,
            QualityMode::HighQuality,
        ];
        for mode in modes {
            let _copy = mode;
        }
    }

    #[test]
    fn test_error_types() {
        let _err1 = DeskewError::ImageNotFound(PathBuf::from("/test"));
        let _err2 = DeskewError::InvalidFormat("bad".to_string());
        let _err3 = DeskewError::DetectionFailed("fail".to_string());
        let _err4 = DeskewError::CorrectionFailed("fail".to_string());
        let _err5: DeskewError = std::io::Error::other("test").into();
    }
}
