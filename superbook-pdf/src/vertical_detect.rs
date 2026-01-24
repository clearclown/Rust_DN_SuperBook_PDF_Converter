//! Vertical text detection for Japanese books
//!
//! # Overview
//!
//! This module detects whether a scanned book page contains vertical (top-to-bottom)
//! or horizontal (left-to-right) text by analyzing the line structure of binarized images.
//!
//! # Algorithm
//!
//! 1. Scan the image horizontally to compute a "horizontal score"
//! 2. Rotate the image 90° and apply the same scan for a "vertical score"
//! 3. Normalize the scores to get vertical writing probability
//!
//! # Example
//!
//! ```ignore
//! use superbook_pdf::vertical_detect::{detect_vertical_probability, VerticalDetectOptions};
//!
//! let options = VerticalDetectOptions::default();
//! let result = detect_vertical_probability(&gray_image, &options)?;
//! println!("Vertical probability: {:.2}", result.vertical_probability);
//! ```

use image::{GrayImage, ImageBuffer};

#[cfg(test)]
use image::Luma;
use std::error::Error;
use std::fmt;

/// Error type for vertical detection operations
#[derive(Debug, Clone)]
pub enum VerticalDetectError {
    /// Image is empty or has invalid dimensions
    InvalidImage(String),
    /// Processing error
    ProcessingError(String),
}

impl fmt::Display for VerticalDetectError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self {
            Self::InvalidImage(msg) => write!(f, "Invalid image: {}", msg),
            Self::ProcessingError(msg) => write!(f, "Processing error: {}", msg),
        }
    }
}

impl Error for VerticalDetectError {}

/// Options for vertical text detection
#[derive(Debug, Clone)]
pub struct VerticalDetectOptions {
    /// Threshold for considering a pixel as black (0-255)
    /// Pixels with value <= this threshold are considered black
    pub black_threshold: u8,
    /// Number of horizontal blocks to divide the image into
    /// Used to handle multi-column layouts
    pub block_count: u32,
    /// Minimum probability threshold for vertical writing (default: 0.5)
    pub vertical_threshold: f64,
}

impl Default for VerticalDetectOptions {
    fn default() -> Self {
        Self {
            black_threshold: 128,
            block_count: 4,
            vertical_threshold: 0.5,
        }
    }
}

impl VerticalDetectOptions {
    /// Create options for high-contrast binary images
    pub fn for_binary() -> Self {
        Self {
            black_threshold: 10,
            ..Default::default()
        }
    }

    /// Create options with custom black threshold
    pub fn with_threshold(threshold: u8) -> Self {
        Self {
            black_threshold: threshold,
            ..Default::default()
        }
    }
}

/// Result of vertical text detection
#[derive(Debug, Clone)]
pub struct VerticalDetectResult {
    /// Probability that the text is vertical (0.0-1.0)
    pub vertical_probability: f64,
    /// Score for horizontal text structure
    pub horizontal_score: f64,
    /// Score for vertical text structure
    pub vertical_score: f64,
    /// Whether the text is determined to be vertical
    pub is_vertical: bool,
}

impl VerticalDetectResult {
    /// Get the horizontal probability (1.0 - vertical_probability)
    pub fn horizontal_probability(&self) -> f64 {
        1.0 - self.vertical_probability
    }
}

/// Result for an entire book's vertical writing detection
#[derive(Debug, Clone)]
pub struct BookVerticalResult {
    /// Average vertical probability across all pages
    pub vertical_probability: f64,
    /// Whether the book is determined to be vertical writing
    pub is_vertical: bool,
    /// Number of pages analyzed
    pub page_count: usize,
    /// Individual page results
    pub page_results: Vec<VerticalDetectResult>,
}

/// Detect vertical writing probability for a single grayscale image
///
/// # Arguments
///
/// * `image` - Grayscale image (binarized or near-binary preferred)
/// * `options` - Detection options
///
/// # Returns
///
/// Detection result with vertical probability
pub fn detect_vertical_probability(
    image: &GrayImage,
    options: &VerticalDetectOptions,
) -> Result<VerticalDetectResult, VerticalDetectError> {
    let width = image.width();
    let height = image.height();

    if width == 0 || height == 0 {
        return Err(VerticalDetectError::InvalidImage(
            "Image has zero dimensions".to_string(),
        ));
    }

    // 1. Compute horizontal score (scanning rows)
    let horizontal_score = compute_linear_score(image, options);

    // 2. Rotate image 90° clockwise and compute vertical score
    let rotated = rotate_90_clockwise(image);
    let vertical_score = compute_linear_score(&rotated, options);

    // 3. Normalize to get vertical probability
    let sum = horizontal_score + vertical_score + 1e-9;
    let mut vertical_probability = vertical_score / sum;

    // Clamp to [0.0, 1.0]
    vertical_probability = vertical_probability.clamp(0.0, 1.0);

    let is_vertical = vertical_probability >= options.vertical_threshold;

    Ok(VerticalDetectResult {
        vertical_probability,
        horizontal_score,
        vertical_score,
        is_vertical,
    })
}

/// Detect vertical writing for an entire book (multiple pages)
///
/// Uses the average probability across pages with >= 10 pages,
/// otherwise uses the simple average.
pub fn detect_book_vertical_writing(
    images: &[GrayImage],
    options: &VerticalDetectOptions,
) -> Result<BookVerticalResult, VerticalDetectError> {
    if images.is_empty() {
        return Err(VerticalDetectError::InvalidImage(
            "No images provided".to_string(),
        ));
    }

    let mut page_results = Vec::with_capacity(images.len());

    for image in images {
        let result = detect_vertical_probability(image, options)?;
        page_results.push(result);
    }

    let vertical_probability = if page_results.len() >= 10 {
        // Use average of all pages
        page_results.iter().map(|r| r.vertical_probability).sum::<f64>() / page_results.len() as f64
    } else {
        // For small books, use simple average
        page_results.iter().map(|r| r.vertical_probability).sum::<f64>() / page_results.len() as f64
    };

    let is_vertical = vertical_probability >= options.vertical_threshold;

    Ok(BookVerticalResult {
        vertical_probability,
        is_vertical,
        page_count: page_results.len(),
        page_results,
    })
}

/// Compute the linear score for text structure detection
///
/// Scans the image row by row and evaluates:
/// 1. Variation coefficient of intersection counts
/// 2. Zero-line ratio (empty rows)
/// 3. Separation ratio (gap between lines)
fn compute_linear_score(image: &GrayImage, options: &VerticalDetectOptions) -> f64 {
    let width = image.width() as usize;
    let height = image.height() as usize;

    if width == 0 || height == 0 {
        return 0.0;
    }

    let block_count = options.block_count.max(1) as usize;
    let block_width = width / block_count;

    if block_width == 0 {
        return 0.0;
    }

    let mut block_scores = Vec::with_capacity(block_count);

    for blk in 0..block_count {
        let start_x = blk * block_width;
        let end_x = if blk == block_count - 1 {
            width
        } else {
            start_x + block_width
        };

        // 1. Count intersections per row using Welford's method
        let mut intersections_per_row = vec![0usize; height];
        let mut zero_lines: usize = 0;
        let mut mean: f64 = 0.0;
        let mut m2: f64 = 0.0;
        let mut count: usize = 0;

        for (y, row_intersects) in intersections_per_row.iter_mut().enumerate() {
            let mut intersects = 0;
            let mut in_black = false;

            // Count black pixel clusters in this row
            for x in start_x..end_x {
                let pixel = image.get_pixel(x as u32, y as u32).0[0];
                let is_black = pixel <= options.black_threshold;

                if is_black {
                    if !in_black {
                        intersects += 1;
                        in_black = true;
                    }
                } else {
                    in_black = false;
                }
            }

            *row_intersects = intersects;

            if intersects == 0 {
                zero_lines += 1;
            }

            // Welford's online algorithm for mean and variance
            count += 1;
            let delta = intersects as f64 - mean;
            mean += delta / count as f64;
            let delta2 = intersects as f64 - mean;
            m2 += delta * delta2;
        }

        if count == 0 {
            block_scores.push(0.0);
            continue;
        }

        let variance = m2 / count as f64;
        let stddev = variance.sqrt();
        let variation_coefficient = if mean > 0.0 { stddev / mean } else { 0.0 };
        let zero_ratio = zero_lines as f64 / count as f64;

        // 2. Extract line thickness and gap heights
        let threshold = mean.max(1.0);
        let mut line_thicknesses = Vec::new();
        let mut gap_heights = Vec::new();

        let mut in_line = false;
        let mut run_len = 0;

        for &intersects in &intersections_per_row {
            let is_line = intersects as f64 >= threshold;

            if is_line == in_line {
                run_len += 1;
            } else {
                // Record previous run
                if run_len > 0 {
                    if in_line {
                        line_thicknesses.push(run_len);
                    } else {
                        gap_heights.push(run_len);
                    }
                }
                in_line = is_line;
                run_len = 1;
            }
        }

        // Final run
        if run_len > 0 {
            if in_line {
                line_thicknesses.push(run_len);
            } else {
                gap_heights.push(run_len);
            }
        }

        // Calculate separation ratio
        let separation_ratio = if !line_thicknesses.is_empty() && !gap_heights.is_empty() {
            let median_line = median(&mut line_thicknesses);
            let median_gap = median(&mut gap_heights);
            median_gap as f64 / (median_line as f64 + median_gap as f64 + 1e-9)
        } else {
            0.0
        };

        // 3. Combine three metrics into block score
        // Weights: variation=0.4, zeroLine=0.2, separation=0.4
        let score =
            (variation_coefficient * 0.4) + (zero_ratio * 0.2) + (separation_ratio * 0.4);

        block_scores.push(score.clamp(0.0, 1.0));
    }

    // Return average of block scores
    if block_scores.is_empty() {
        0.0
    } else {
        block_scores.iter().sum::<f64>() / block_scores.len() as f64
    }
}

/// Rotate an image 90 degrees clockwise
fn rotate_90_clockwise(image: &GrayImage) -> GrayImage {
    let (width, height) = image.dimensions();
    let mut rotated: GrayImage = ImageBuffer::new(height, width);

    for y in 0..height {
        for x in 0..width {
            let pixel = image.get_pixel(x, y);
            // (x, y) -> (height - 1 - y, x) after 90° clockwise rotation
            // But we want new_width = height, new_height = width
            // Original (x, y) maps to (y, width - 1 - x) in the new image
            // Actually for 90° clockwise: new_x = old_y, new_y = old_width - 1 - old_x
            let new_x = y;
            let new_y = width - 1 - x;
            rotated.put_pixel(new_x, new_y, *pixel);
        }
    }

    rotated
}

/// Calculate median of a slice (modifies the slice by sorting)
fn median(data: &mut [usize]) -> usize {
    if data.is_empty() {
        return 0;
    }

    data.sort_unstable();
    let mid = data.len() / 2;

    if data.len() % 2 == 1 {
        data[mid]
    } else {
        (data[mid - 1] + data[mid]) / 2
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    // ============ TC VD-001: Vertical probability calculation ============

    #[test]
    fn test_vd001_vertical_probability_range() {
        // Create a simple test image
        let image: GrayImage = ImageBuffer::from_fn(100, 100, |_, _| Luma([255u8]));
        let options = VerticalDetectOptions::default();

        let result = detect_vertical_probability(&image, &options).unwrap();

        // Probability should be in [0, 1]
        assert!(result.vertical_probability >= 0.0);
        assert!(result.vertical_probability <= 1.0);
    }

    // ============ TC VD-002: Horizontal probability calculation ============

    #[test]
    fn test_vd002_horizontal_probability() {
        let image: GrayImage = ImageBuffer::from_fn(100, 100, |_, _| Luma([255u8]));
        let options = VerticalDetectOptions::default();

        let result = detect_vertical_probability(&image, &options).unwrap();

        // Horizontal probability should be 1.0 - vertical_probability
        let expected = 1.0 - result.vertical_probability;
        assert!((result.horizontal_probability() - expected).abs() < 1e-9);
    }

    // ============ TC VD-004: Empty image handling ============

    #[test]
    fn test_vd004_empty_image_error() {
        let image: GrayImage = ImageBuffer::new(0, 0);
        let options = VerticalDetectOptions::default();

        let result = detect_vertical_probability(&image, &options);
        assert!(result.is_err());
    }

    #[test]
    fn test_vd004_zero_width() {
        let image: GrayImage = ImageBuffer::new(0, 100);
        let options = VerticalDetectOptions::default();

        let result = detect_vertical_probability(&image, &options);
        assert!(result.is_err());
    }

    #[test]
    fn test_vd004_zero_height() {
        let image: GrayImage = ImageBuffer::new(100, 0);
        let options = VerticalDetectOptions::default();

        let result = detect_vertical_probability(&image, &options);
        assert!(result.is_err());
    }

    // ============ TC VD-005: Block division ============

    #[test]
    fn test_vd005_block_count_options() {
        let image: GrayImage = ImageBuffer::from_fn(400, 100, |_, _| Luma([255u8]));

        for block_count in [1, 2, 4, 8] {
            let options = VerticalDetectOptions {
                block_count,
                ..Default::default()
            };

            let result = detect_vertical_probability(&image, &options);
            assert!(result.is_ok());
        }
    }

    // ============ TC VD-006: Intersection counting ============

    #[test]
    fn test_vd006_horizontal_lines_detected() {
        // Create image with horizontal black lines
        let image: GrayImage = ImageBuffer::from_fn(100, 100, |_, y| {
            if y % 10 < 5 {
                Luma([0u8]) // Black line
            } else {
                Luma([255u8]) // White gap
            }
        });

        let options = VerticalDetectOptions::default();
        let result = detect_vertical_probability(&image, &options).unwrap();

        // Horizontal lines should give higher horizontal score
        assert!(result.horizontal_score > 0.0);
    }

    #[test]
    fn test_vd006_vertical_lines_detected() {
        // Create image with vertical black lines
        let image: GrayImage = ImageBuffer::from_fn(100, 100, |x, _| {
            if x % 10 < 5 {
                Luma([0u8]) // Black line
            } else {
                Luma([255u8]) // White gap
            }
        });

        let options = VerticalDetectOptions::default();
        let result = detect_vertical_probability(&image, &options).unwrap();

        // Vertical lines should give higher vertical score
        assert!(result.vertical_score > 0.0);
    }

    // ============ TC VD-009: Score synthesis ============

    #[test]
    fn test_vd009_score_clamped() {
        let image: GrayImage = ImageBuffer::from_fn(100, 100, |x, y| {
            if (x + y) % 2 == 0 {
                Luma([0u8])
            } else {
                Luma([255u8])
            }
        });

        let options = VerticalDetectOptions::default();
        let result = detect_vertical_probability(&image, &options).unwrap();

        // All scores should be clamped to [0, 1]
        assert!(result.horizontal_score >= 0.0 && result.horizontal_score <= 1.0);
        assert!(result.vertical_score >= 0.0 && result.vertical_score <= 1.0);
    }

    // ============ TC VD-010: Image rotation ============

    #[test]
    fn test_vd010_rotation_90_clockwise() {
        // Create asymmetric image to verify rotation
        let image: GrayImage = ImageBuffer::from_fn(10, 5, |x, y| {
            if x == 0 && y == 0 {
                Luma([100u8])
            } else {
                Luma([255u8])
            }
        });

        let rotated = rotate_90_clockwise(&image);

        // Original: 10x5, Rotated: 5x10
        assert_eq!(rotated.dimensions(), (5, 10));

        // Original (0,0) with value 100 should be at (0, 9) after rotation
        assert_eq!(rotated.get_pixel(0, 9).0[0], 100);
    }

    // ============ TC VD-012: Book vertical detection ============

    #[test]
    fn test_vd012_book_empty_error() {
        let images: Vec<GrayImage> = vec![];
        let options = VerticalDetectOptions::default();

        let result = detect_book_vertical_writing(&images, &options);
        assert!(result.is_err());
    }

    #[test]
    fn test_vd012_book_single_page() {
        let image: GrayImage = ImageBuffer::from_fn(100, 100, |_, _| Luma([255u8]));
        let images = vec![image];
        let options = VerticalDetectOptions::default();

        let result = detect_book_vertical_writing(&images, &options).unwrap();
        assert_eq!(result.page_count, 1);
    }

    #[test]
    fn test_vd012_book_multiple_pages() {
        let images: Vec<GrayImage> = (0..10)
            .map(|_| ImageBuffer::from_fn(100, 100, |_, _| Luma([255u8])))
            .collect();

        let options = VerticalDetectOptions::default();
        let result = detect_book_vertical_writing(&images, &options).unwrap();

        assert_eq!(result.page_count, 10);
        assert_eq!(result.page_results.len(), 10);
    }

    // ============ Additional tests ============

    #[test]
    fn test_options_default() {
        let options = VerticalDetectOptions::default();
        assert_eq!(options.black_threshold, 128);
        assert_eq!(options.block_count, 4);
        assert_eq!(options.vertical_threshold, 0.5);
    }

    #[test]
    fn test_options_for_binary() {
        let options = VerticalDetectOptions::for_binary();
        assert_eq!(options.black_threshold, 10);
    }

    #[test]
    fn test_options_with_threshold() {
        let options = VerticalDetectOptions::with_threshold(50);
        assert_eq!(options.black_threshold, 50);
    }

    #[test]
    fn test_median_calculation() {
        let mut data = vec![5, 2, 9, 1, 7];
        assert_eq!(median(&mut data), 5);

        let mut data_even = vec![1, 2, 3, 4];
        assert_eq!(median(&mut data_even), 2); // (2+3)/2 = 2 (integer division)

        let mut empty: Vec<usize> = vec![];
        assert_eq!(median(&mut empty), 0);
    }

    #[test]
    fn test_result_debug_impl() {
        let result = VerticalDetectResult {
            vertical_probability: 0.7,
            horizontal_score: 0.3,
            vertical_score: 0.7,
            is_vertical: true,
        };
        let debug_str = format!("{:?}", result);
        assert!(debug_str.contains("VerticalDetectResult"));
    }

    #[test]
    fn test_error_display() {
        let err = VerticalDetectError::InvalidImage("test".to_string());
        assert!(err.to_string().contains("Invalid image"));

        let err2 = VerticalDetectError::ProcessingError("test".to_string());
        assert!(err2.to_string().contains("Processing error"));
    }

    #[test]
    fn test_white_image() {
        // Pure white image should have low scores for both directions
        let image: GrayImage = ImageBuffer::from_fn(100, 100, |_, _| Luma([255u8]));
        let options = VerticalDetectOptions::default();

        let result = detect_vertical_probability(&image, &options).unwrap();

        // Both scores should be low (or equal) for uniform image
        assert!(result.horizontal_score >= 0.0);
        assert!(result.vertical_score >= 0.0);
    }

    #[test]
    fn test_black_image() {
        // Pure black image
        let image: GrayImage = ImageBuffer::from_fn(100, 100, |_, _| Luma([0u8]));
        let options = VerticalDetectOptions::default();

        let result = detect_vertical_probability(&image, &options).unwrap();

        // Should not panic, probability should be valid
        assert!(result.vertical_probability >= 0.0);
        assert!(result.vertical_probability <= 1.0);
    }

    #[test]
    fn test_small_image() {
        // Very small image (edge case)
        let image: GrayImage = ImageBuffer::from_fn(5, 5, |_, _| Luma([128u8]));
        let options = VerticalDetectOptions::default();

        let result = detect_vertical_probability(&image, &options);
        assert!(result.is_ok());
    }

    #[test]
    fn test_thread_safety() {
        use std::sync::Arc;
        use std::thread;

        let image = Arc::new(ImageBuffer::from_fn(100, 100, |_, _| Luma([255u8])));
        let options = Arc::new(VerticalDetectOptions::default());

        let handles: Vec<_> = (0..4)
            .map(|_| {
                let img = Arc::clone(&image);
                let opts = Arc::clone(&options);
                thread::spawn(move || detect_vertical_probability(&img, &opts))
            })
            .collect();

        for handle in handles {
            let result = handle.join().unwrap();
            assert!(result.is_ok());
        }
    }

    // ============ More edge case tests ============

    #[test]
    fn test_large_block_count() {
        // Block count larger than image width/height
        let image: GrayImage = ImageBuffer::from_fn(50, 50, |_, _| Luma([128u8]));
        let options = VerticalDetectOptions {
            block_count: 100, // More blocks than pixels
            ..Default::default()
        };

        let result = detect_vertical_probability(&image, &options);
        assert!(result.is_ok());
    }

    #[test]
    fn test_extreme_threshold() {
        let image: GrayImage = ImageBuffer::from_fn(100, 100, |_, _| Luma([128u8]));

        // Very low threshold
        let options_low = VerticalDetectOptions {
            black_threshold: 1,
            ..Default::default()
        };
        let result_low = detect_vertical_probability(&image, &options_low);
        assert!(result_low.is_ok());

        // Very high threshold
        let options_high = VerticalDetectOptions {
            black_threshold: 254,
            ..Default::default()
        };
        let result_high = detect_vertical_probability(&image, &options_high);
        assert!(result_high.is_ok());
    }

    #[test]
    fn test_vertical_threshold_extremes() {
        let image: GrayImage = ImageBuffer::from_fn(100, 100, |_, _| Luma([255u8]));

        // Threshold 0.0 - always vertical
        let options_zero = VerticalDetectOptions {
            vertical_threshold: 0.0,
            ..Default::default()
        };
        let result_zero = detect_vertical_probability(&image, &options_zero).unwrap();
        assert!(result_zero.is_vertical);

        // Threshold 1.0 - never vertical
        let options_one = VerticalDetectOptions {
            vertical_threshold: 1.0,
            ..Default::default()
        };
        let result_one = detect_vertical_probability(&image, &options_one).unwrap();
        assert!(!result_one.is_vertical);
    }

    #[test]
    fn test_book_result_fields() {
        let images: Vec<GrayImage> = (0..5)
            .map(|_| ImageBuffer::from_fn(100, 100, |_, _| Luma([255u8])))
            .collect();

        let options = VerticalDetectOptions::default();
        let result = detect_book_vertical_writing(&images, &options).unwrap();

        assert_eq!(result.page_count, 5);
        assert_eq!(result.page_results.len(), 5);
        assert!(result.vertical_probability >= 0.0);
        assert!(result.vertical_probability <= 1.0);
    }

    #[test]
    fn test_book_mixed_results() {
        // Create images with different characteristics
        let horizontal_image: GrayImage = ImageBuffer::from_fn(100, 100, |_, y| {
            if y % 10 < 5 {
                Luma([0u8])
            } else {
                Luma([255u8])
            }
        });

        let vertical_image: GrayImage = ImageBuffer::from_fn(100, 100, |x, _| {
            if x % 10 < 5 {
                Luma([0u8])
            } else {
                Luma([255u8])
            }
        });

        let images = vec![horizontal_image.clone(), vertical_image, horizontal_image];
        let options = VerticalDetectOptions::default();

        let result = detect_book_vertical_writing(&images, &options).unwrap();
        assert_eq!(result.page_count, 3);
    }

    #[test]
    fn test_rotate_90_clockwise() {
        let image: GrayImage = ImageBuffer::from_fn(20, 10, |x, y| Luma([(x + y * 10) as u8]));

        let rotated = rotate_90_clockwise(&image);

        // After 90° clockwise: new_width = old_height, new_height = old_width
        assert_eq!(rotated.width(), 10);
        assert_eq!(rotated.height(), 20);
    }

    #[test]
    fn test_result_horizontal_probability() {
        let result = VerticalDetectResult {
            vertical_probability: 0.3,
            horizontal_score: 0.7,
            vertical_score: 0.3,
            is_vertical: false,
        };

        assert!((result.horizontal_probability() - 0.7).abs() < 1e-9);
    }

    #[test]
    fn test_error_debug_impl() {
        let err = VerticalDetectError::InvalidImage("test".to_string());
        let debug_str = format!("{:?}", err);
        assert!(debug_str.contains("InvalidImage"));
    }
}
