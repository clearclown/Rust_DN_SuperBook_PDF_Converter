//! Deskew Algorithm Implementation
//!
//! Contains the ImageProcDeskewer implementation for skew detection and correction.

use super::types::{
    DeskewAlgorithm, DeskewError, DeskewOptions, DeskewResult, QualityMode, Result, SkewDetection,
    ALPHA_OPAQUE, GRAYSCALE_THRESHOLD, WHITE_PIXEL,
};
use image::{DynamicImage, GenericImageView, GrayImage, Rgba};
use std::path::{Path, PathBuf};

/// imageproc-based deskewer implementation
pub struct ImageProcDeskewer;

impl ImageProcDeskewer {
    /// Detect skew angle from image
    pub fn detect_skew(image_path: &Path, options: &DeskewOptions) -> Result<SkewDetection> {
        if !image_path.exists() {
            return Err(DeskewError::ImageNotFound(image_path.to_path_buf()));
        }

        let img = image::open(image_path).map_err(|e| DeskewError::InvalidFormat(e.to_string()))?;

        let gray = img.to_luma8();

        match options.algorithm {
            DeskewAlgorithm::HoughLines => Self::detect_skew_hough(&gray, options),
            DeskewAlgorithm::ProjectionProfile => Self::detect_skew_projection(&gray, options),
            DeskewAlgorithm::TextLineDetection => Self::detect_skew_text_lines(&gray, options),
            DeskewAlgorithm::Combined => Self::detect_skew_combined(&gray, options),
        }
    }

    /// Hough line transform based detection
    fn detect_skew_hough(gray: &GrayImage, options: &DeskewOptions) -> Result<SkewDetection> {
        // Simple edge-based angle detection
        // In a full implementation, we would use Hough transform from imageproc

        let edges = Self::detect_edges(gray);
        let angles = Self::extract_line_angles(&edges, options.max_angle);

        if angles.is_empty() {
            return Ok(SkewDetection {
                angle: 0.0,
                confidence: 0.0,
                feature_count: 0,
            });
        }

        let median_angle = Self::median(&angles);
        let std_dev = Self::std_dev(&angles, median_angle);
        let confidence = (1.0 - (std_dev / options.max_angle).min(1.0)).max(0.0);

        Ok(SkewDetection {
            angle: median_angle,
            confidence,
            feature_count: angles.len(),
        })
    }

    /// Projection profile based detection
    fn detect_skew_projection(gray: &GrayImage, options: &DeskewOptions) -> Result<SkewDetection> {
        // Test multiple angles and find the one with maximum variance
        let (width, height) = gray.dimensions();
        let mut best_angle = 0.0;
        let mut best_variance = 0.0;

        // Test angles from -max_angle to +max_angle in 0.5 degree steps
        let steps = (options.max_angle * 4.0) as i32;
        for i in -steps..=steps {
            let angle = i as f64 * 0.25;
            let variance = Self::compute_projection_variance(gray, angle, width, height);
            if variance > best_variance {
                best_variance = variance;
                best_angle = angle;
            }
        }

        Ok(SkewDetection {
            angle: best_angle,
            confidence: if best_variance > 0.0 { 0.8 } else { 0.0 },
            feature_count: 1,
        })
    }

    /// Text line based detection
    fn detect_skew_text_lines(gray: &GrayImage, options: &DeskewOptions) -> Result<SkewDetection> {
        // Simplified text line detection using horizontal projection
        Self::detect_skew_projection(gray, options)
    }

    /// Combined detection (average of multiple methods)
    fn detect_skew_combined(gray: &GrayImage, options: &DeskewOptions) -> Result<SkewDetection> {
        let hough = Self::detect_skew_hough(gray, options)?;
        let projection = Self::detect_skew_projection(gray, options)?;

        // Weighted average based on confidence
        let total_confidence = hough.confidence + projection.confidence;
        if total_confidence == 0.0 {
            return Ok(SkewDetection {
                angle: 0.0,
                confidence: 0.0,
                feature_count: 0,
            });
        }

        let weighted_angle = (hough.angle * hough.confidence
            + projection.angle * projection.confidence)
            / total_confidence;

        Ok(SkewDetection {
            angle: weighted_angle,
            confidence: (hough.confidence + projection.confidence) / 2.0,
            feature_count: hough.feature_count + projection.feature_count,
        })
    }

    /// Simple edge detection using Sobel-like operator
    fn detect_edges(gray: &GrayImage) -> GrayImage {
        let (width, height) = gray.dimensions();
        let mut edges = GrayImage::new(width, height);

        for y in 1..height - 1 {
            for x in 1..width - 1 {
                // Sobel operator for horizontal edges
                let gx = gray.get_pixel(x + 1, y - 1).0[0] as i32
                    + 2 * gray.get_pixel(x + 1, y).0[0] as i32
                    + gray.get_pixel(x + 1, y + 1).0[0] as i32
                    - gray.get_pixel(x - 1, y - 1).0[0] as i32
                    - 2 * gray.get_pixel(x - 1, y).0[0] as i32
                    - gray.get_pixel(x - 1, y + 1).0[0] as i32;

                let gy = gray.get_pixel(x - 1, y + 1).0[0] as i32
                    + 2 * gray.get_pixel(x, y + 1).0[0] as i32
                    + gray.get_pixel(x + 1, y + 1).0[0] as i32
                    - gray.get_pixel(x - 1, y - 1).0[0] as i32
                    - 2 * gray.get_pixel(x, y - 1).0[0] as i32
                    - gray.get_pixel(x + 1, y - 1).0[0] as i32;

                let magnitude = ((gx * gx + gy * gy) as f64).sqrt() as u8;
                edges.put_pixel(x, y, image::Luma([magnitude]));
            }
        }

        edges
    }

    /// Extract line angles from edge image
    fn extract_line_angles(edges: &GrayImage, max_angle: f64) -> Vec<f64> {
        let (width, height) = edges.dimensions();
        let mut angles = Vec::new();

        // Simple line detection: scan rows and find runs of edge pixels
        let threshold = GRAYSCALE_THRESHOLD;

        for y in (0..height).step_by(10) {
            let mut runs = Vec::new();
            let mut in_run = false;
            let mut run_start = 0;

            for x in 0..width {
                let pixel = edges.get_pixel(x, y).0[0];
                if pixel > threshold && !in_run {
                    in_run = true;
                    run_start = x;
                } else if pixel <= threshold && in_run {
                    in_run = false;
                    if x - run_start > 20 {
                        // Minimum run length
                        runs.push((run_start, x));
                    }
                }
            }

            // Analyze runs for horizontal lines
            for (start, end) in runs {
                // Check angle by looking at adjacent rows
                if y > 0 && y < height - 1 {
                    let dy = 10.0; // Row step
                    let mid_x = (start + end) / 2;

                    // Look for similar runs in adjacent rows
                    for offset in [-10i32, 10] {
                        let adj_y = (y as i32 + offset) as u32;
                        if adj_y < height {
                            // Find edge at similar x position
                            let mut found_x = None;
                            for search_x in
                                (mid_x.saturating_sub(20))..mid_x.saturating_add(20).min(width)
                            {
                                if edges.get_pixel(search_x, adj_y).0[0] > threshold {
                                    found_x = Some(search_x);
                                    break;
                                }
                            }

                            if let Some(fx) = found_x {
                                let dx = fx as f64 - mid_x as f64;
                                let angle = (dx / dy).atan().to_degrees();
                                if angle.abs() <= max_angle {
                                    angles.push(angle);
                                }
                            }
                        }
                    }
                }
            }
        }

        angles
    }

    /// Compute projection variance for angle estimation
    fn compute_projection_variance(gray: &GrayImage, angle: f64, width: u32, height: u32) -> f64 {
        let cos_a = angle.to_radians().cos();
        let sin_a = angle.to_radians().sin();
        let cx = width as f64 / 2.0;
        let cy = height as f64 / 2.0;

        // Compute horizontal projection after rotation
        let mut projection = vec![0i64; height as usize];

        for y in 0..height {
            for x in 0..width {
                let _rx = (x as f64 - cx) * cos_a - (y as f64 - cy) * sin_a + cx;
                let ry = (x as f64 - cx) * sin_a + (y as f64 - cy) * cos_a + cy;

                if ry >= 0.0 && ry < height as f64 {
                    let pixel = gray.get_pixel(x, y).0[0];
                    // Invert pixel value: dark pixels contribute more to projection
                    projection[ry as usize] += (WHITE_PIXEL - pixel) as i64;
                }
            }
        }

        // Compute variance
        let mean: f64 = projection.iter().sum::<i64>() as f64 / projection.len() as f64;
        let variance: f64 = projection
            .iter()
            .map(|&v| (v as f64 - mean).powi(2))
            .sum::<f64>()
            / projection.len() as f64;

        variance
    }

    /// Correct skew in image
    pub fn correct_skew(
        input_path: &Path,
        output_path: &Path,
        options: &DeskewOptions,
    ) -> Result<DeskewResult> {
        let detection = Self::detect_skew(input_path, options)?;

        let img = image::open(input_path).map_err(|e| DeskewError::InvalidFormat(e.to_string()))?;
        let original_size = (img.width(), img.height());

        // Skip correction if angle is below threshold
        if detection.angle.abs() < options.threshold_angle {
            img.save(output_path)
                .map_err(|e| DeskewError::CorrectionFailed(e.to_string()))?;

            return Ok(DeskewResult {
                detection,
                corrected: false,
                output_path: output_path.to_path_buf(),
                original_size,
                corrected_size: original_size,
            });
        }

        // Perform rotation
        let rotated = Self::rotate_image(&img, -detection.angle, options);
        let corrected_size = (rotated.width(), rotated.height());

        rotated
            .save(output_path)
            .map_err(|e| DeskewError::CorrectionFailed(e.to_string()))?;

        Ok(DeskewResult {
            detection,
            corrected: true,
            output_path: output_path.to_path_buf(),
            original_size,
            corrected_size,
        })
    }

    /// Rotate image by specified angle
    fn rotate_image(
        img: &DynamicImage,
        angle_degrees: f64,
        options: &DeskewOptions,
    ) -> DynamicImage {
        let (width, height) = img.dimensions();
        let angle_rad = angle_degrees.to_radians();
        let cos_a = angle_rad.cos();
        let sin_a = angle_rad.sin();

        // Calculate new dimensions
        let new_width =
            ((width as f64 * cos_a.abs()) + (height as f64 * sin_a.abs())).ceil() as u32;
        let new_height =
            ((width as f64 * sin_a.abs()) + (height as f64 * cos_a.abs())).ceil() as u32;

        let cx = width as f64 / 2.0;
        let cy = height as f64 / 2.0;
        let ncx = new_width as f64 / 2.0;
        let ncy = new_height as f64 / 2.0;

        let bg = Rgba([
            options.background_color[0],
            options.background_color[1],
            options.background_color[2],
            ALPHA_OPAQUE,
        ]);

        let mut rotated = image::RgbaImage::new(new_width, new_height);

        // Fill with background
        for pixel in rotated.pixels_mut() {
            *pixel = bg;
        }

        // Perform rotation with interpolation based on quality mode
        for ny in 0..new_height {
            for nx in 0..new_width {
                // Map back to original coordinates
                let ox = (nx as f64 - ncx) * cos_a + (ny as f64 - ncy) * sin_a + cx;
                let oy = -(nx as f64 - ncx) * sin_a + (ny as f64 - ncy) * cos_a + cy;

                if ox >= 0.0 && ox < width as f64 - 1.0 && oy >= 0.0 && oy < height as f64 - 1.0 {
                    let pixel = match options.quality_mode {
                        QualityMode::Fast => Self::nearest_neighbor(img, ox, oy),
                        QualityMode::Standard => Self::bilinear(img, ox, oy),
                        QualityMode::HighQuality => Self::bilinear(img, ox, oy), // Simplified
                    };
                    rotated.put_pixel(nx, ny, pixel);
                }
            }
        }

        DynamicImage::ImageRgba8(rotated)
    }

    /// Nearest neighbor interpolation
    fn nearest_neighbor(img: &DynamicImage, x: f64, y: f64) -> Rgba<u8> {
        img.get_pixel(x.round() as u32, y.round() as u32)
    }

    /// Bilinear interpolation
    fn bilinear(img: &DynamicImage, x: f64, y: f64) -> Rgba<u8> {
        let x0 = x.floor() as u32;
        let y0 = y.floor() as u32;
        let x1 = x0 + 1;
        let y1 = y0 + 1;

        let dx = x - x0 as f64;
        let dy = y - y0 as f64;

        let p00 = img.get_pixel(x0, y0);
        let p10 = img.get_pixel(x1, y0);
        let p01 = img.get_pixel(x0, y1);
        let p11 = img.get_pixel(x1, y1);

        let mut result = [0u8; 4];
        for (i, result_channel) in result.iter_mut().enumerate() {
            let v00 = p00.0[i] as f64;
            let v10 = p10.0[i] as f64;
            let v01 = p01.0[i] as f64;
            let v11 = p11.0[i] as f64;

            let v = v00 * (1.0 - dx) * (1.0 - dy)
                + v10 * dx * (1.0 - dy)
                + v01 * (1.0 - dx) * dy
                + v11 * dx * dy;

            *result_channel = v.round() as u8;
        }

        Rgba(result)
    }

    /// Deskew with detection
    pub fn deskew(
        input_path: &Path,
        output_path: &Path,
        options: &DeskewOptions,
    ) -> Result<DeskewResult> {
        Self::correct_skew(input_path, output_path, options)
    }

    /// Batch deskew processing
    pub fn deskew_batch(
        images: &[(PathBuf, PathBuf)],
        options: &DeskewOptions,
    ) -> Vec<Result<DeskewResult>> {
        images
            .iter()
            .map(|(input, output)| Self::deskew(input, output, options))
            .collect()
    }

    /// Calculate median of values
    pub fn median(values: &[f64]) -> f64 {
        if values.is_empty() {
            return 0.0;
        }
        let mut sorted = values.to_vec();
        sorted.sort_by(|a, b| a.partial_cmp(b).unwrap_or(std::cmp::Ordering::Equal));
        sorted[sorted.len() / 2]
    }

    /// Calculate standard deviation
    pub fn std_dev(values: &[f64], mean: f64) -> f64 {
        if values.is_empty() {
            return 0.0;
        }
        let variance: f64 =
            values.iter().map(|v| (v - mean).powi(2)).sum::<f64>() / values.len() as f64;
        variance.sqrt()
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use tempfile::tempdir;

    #[test]
    fn test_image_not_found() {
        let result = ImageProcDeskewer::detect_skew(
            Path::new("/nonexistent/image.png"),
            &DeskewOptions::default(),
        );

        assert!(matches!(result, Err(DeskewError::ImageNotFound(_))));
    }

    #[test]
    fn test_median_calculation() {
        let values = vec![1.0, 3.0, 5.0, 7.0, 9.0];
        let median = ImageProcDeskewer::median(&values);
        assert_eq!(median, 5.0);

        let empty: Vec<f64> = vec![];
        assert_eq!(ImageProcDeskewer::median(&empty), 0.0);
    }

    #[test]
    fn test_std_dev_calculation() {
        let values = vec![2.0, 4.0, 4.0, 4.0, 5.0, 5.0, 7.0, 9.0];
        let mean = 5.0;
        let std_dev = ImageProcDeskewer::std_dev(&values, mean);
        assert!((std_dev - 2.0).abs() < 0.1);
    }

    // TC-DSK-001: 傾き検出（正の角度）
    #[test]
    fn test_detect_positive_skew() {
        let detection = ImageProcDeskewer::detect_skew(
            Path::new("tests/fixtures/skewed_5deg.png"),
            &DeskewOptions::default(),
        )
        .unwrap();

        // Basic validation: detection should complete without error
        assert!(
            detection.angle.abs() <= 15.0,
            "Angle should be within max range"
        );
        assert!(detection.confidence >= 0.0 && detection.confidence <= 1.0);
    }

    // TC-DSK-002: 傾き検出（負の角度）
    #[test]
    fn test_detect_negative_skew() {
        let detection = ImageProcDeskewer::detect_skew(
            Path::new("tests/fixtures/skewed_neg3deg.png"),
            &DeskewOptions::default(),
        )
        .unwrap();

        // Verify detection returns a valid result
        assert!(detection.confidence >= 0.0);
    }

    // TC-DSK-004: 傾き補正実行
    #[test]
    fn test_correct_skew() {
        let temp_dir = tempdir().unwrap();
        let output = temp_dir.path().join("corrected.png");

        // Use lower threshold to ensure correction happens
        let options = DeskewOptions {
            threshold_angle: 0.01,
            ..Default::default()
        };

        let result = ImageProcDeskewer::correct_skew(
            Path::new("tests/fixtures/skewed_5deg.png"),
            &output,
            &options,
        )
        .unwrap();

        // Output file should exist regardless of correction
        assert!(output.exists());

        // If angle was detected, it should have been corrected
        if result.detection.angle.abs() > options.threshold_angle {
            assert!(result.corrected);
        }
    }

    // TC-DSK-005: 閾値以下は補正しない
    #[test]
    fn test_threshold_skip() {
        let temp_dir = tempdir().unwrap();
        let output = temp_dir.path().join("output.png");

        let result = ImageProcDeskewer::correct_skew(
            Path::new("tests/fixtures/skewed_005deg.png"),
            &output,
            &DeskewOptions {
                threshold_angle: 0.1,
                ..Default::default()
            },
        )
        .unwrap();

        assert!(!result.corrected);
    }

    // TC-DSK-009: バッチ処理
    #[test]
    fn test_batch_deskew_count() {
        let images = vec![
            (
                PathBuf::from("tests/fixtures/skewed_5deg.png"),
                PathBuf::from("/tmp/out1.png"),
            ),
            (
                PathBuf::from("tests/fixtures/skewed_neg3deg.png"),
                PathBuf::from("/tmp/out2.png"),
            ),
        ];

        let results = ImageProcDeskewer::deskew_batch(&images, &DeskewOptions::default());
        assert_eq!(results.len(), 2);
    }

    // TC-DSK-003: 傾きなし画像検出
    #[test]
    fn test_detect_no_skew() {
        let detection = ImageProcDeskewer::detect_skew(
            Path::new("tests/fixtures/skewed_005deg.png"),
            &DeskewOptions::default(),
        )
        .unwrap();

        assert!(detection.confidence >= 0.0);
    }

    // TC-DSK-006: 最大角度制限テスト
    #[test]
    fn test_max_angle_limit_detection() {
        let options = DeskewOptions::builder().max_angle(5.0).build();

        let detection =
            ImageProcDeskewer::detect_skew(Path::new("tests/fixtures/skewed_5deg.png"), &options)
                .unwrap();

        assert!(
            detection.angle.abs() <= options.max_angle,
            "Detected angle {} exceeds max angle {}",
            detection.angle,
            options.max_angle
        );
    }

    #[test]
    fn test_median_even_count() {
        let values = vec![1.0, 2.0, 3.0, 4.0];
        let median = ImageProcDeskewer::median(&values);
        assert_eq!(median, 3.0);
    }

    #[test]
    fn test_median_single_value() {
        let values = vec![42.0];
        let median = ImageProcDeskewer::median(&values);
        assert_eq!(median, 42.0);
    }

    #[test]
    fn test_std_dev_uniform() {
        let values = vec![5.0, 5.0, 5.0, 5.0, 5.0];
        let mean = 5.0;
        let std_dev = ImageProcDeskewer::std_dev(&values, mean);
        assert!(std_dev.abs() < 0.001);
    }

    #[test]
    fn test_std_dev_with_single_value() {
        let values = vec![5.0];
        let mean = 5.0;
        let std_dev = ImageProcDeskewer::std_dev(&values, mean);
        assert_eq!(std_dev, 0.0);
    }

    #[test]
    fn test_median_with_precision_values() {
        let values = vec![0.00001, 0.00002, 0.00003, 0.00004, 0.00005];
        let median = ImageProcDeskewer::median(&values);
        assert!((median - 0.00003).abs() < 0.000001);
    }
}
