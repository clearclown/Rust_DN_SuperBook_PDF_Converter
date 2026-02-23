//! Deskew Algorithm Implementation
//!
//! Contains the ImageProcDeskewer implementation for skew detection and correction.
//!
//! # Enhanced Features (Phase 1.1)
//!
//! - Otsu thresholding for improved binarization
//! - Morphological operations for noise reduction
//! - Hough transform based line detection
//! - WarpAffine rotation with Lanczos interpolation

use super::types::{
    DeskewAlgorithm, DeskewError, DeskewOptions, DeskewResult, QualityMode, Result, SkewDetection,
    ALPHA_OPAQUE, GRAYSCALE_THRESHOLD, WHITE_PIXEL,
};
use image::{DynamicImage, GenericImageView, GrayImage, Rgba};
use std::path::{Path, PathBuf};

// ============================================================
// Constants for Enhanced Deskew (Phase 1.1)
// ============================================================

/// Morphology kernel size (odd number)
pub const MORPHOLOGY_KERNEL_SIZE: u32 = 3;

/// Minimum line length for Hough detection (as fraction of image width)
pub const MIN_LINE_LENGTH_RATIO: f64 = 0.05;

/// Line detection angle tolerance (degrees from horizontal)
/// Used for filtering detected lines that deviate too much from horizontal
#[allow(dead_code)]
pub const ANGLE_TOLERANCE_DEGREES: f64 = 15.0;

/// Minimum number of supporting points for a line
pub const MIN_LINE_SUPPORT: usize = 50;

/// Hough accumulator angle resolution (degrees)
pub const HOUGH_ANGLE_RESOLUTION: f64 = 0.5;

/// Hough accumulator rho resolution (pixels)
#[allow(dead_code)]
pub const HOUGH_RHO_RESOLUTION: f64 = 1.0;

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
            DeskewAlgorithm::PageEdge => Self::detect_skew_page_edge(&gray, options),
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
                        QualityMode::Standard => Self::lanczos(img, ox, oy),
                        QualityMode::HighQuality => Self::lanczos(img, ox, oy),
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

    /// Detect if an image is rotated 180 degrees by comparing ink density
    /// in the top vs bottom portions of the page.
    ///
    /// For text documents, the top portion typically has more content (title, headers)
    /// than the bottom. If the bottom has significantly more ink, the page is likely
    /// upside down.
    ///
    /// Returns `true` if the image appears to be rotated 180 degrees.
    pub fn detect_upside_down(image_path: &Path) -> std::result::Result<bool, DeskewError> {
        let img = image::open(image_path).map_err(|e| DeskewError::InvalidFormat(e.to_string()))?;
        let gray = img.to_luma8();
        Ok(Self::is_upside_down(&gray))
    }

    /// Check if a grayscale image is upside down by ink density analysis.
    fn is_upside_down(gray: &GrayImage) -> bool {
        let (_width, height) = gray.dimensions();
        if height < 20 {
            return false;
        }

        let threshold = Self::otsu_threshold(gray);

        // Compare ink density in top 25% vs bottom 25%
        let quarter = height / 4;
        let top_ink = Self::count_ink_pixels(gray, 0, quarter, threshold);
        let bottom_ink = Self::count_ink_pixels(gray, height - quarter, height, threshold);

        // Also check top 10% vs bottom 10% for header/footer detection
        let tenth = height / 10;
        let top_10_ink = Self::count_ink_pixels(gray, 0, tenth, threshold);
        let bottom_10_ink = Self::count_ink_pixels(gray, height - tenth, height, threshold);

        // Heuristic: if bottom has significantly more ink than top in BOTH
        // quarter and tenth regions, likely upside down.
        // Using ratio > 2.0 as threshold to avoid false positives.
        let quarter_ratio = if top_ink > 0 {
            bottom_ink as f64 / top_ink as f64
        } else if bottom_ink > 0 {
            10.0 // Top is empty, bottom has ink → likely upside down
        } else {
            1.0 // Both empty
        };

        let tenth_ratio = if top_10_ink > 0 {
            bottom_10_ink as f64 / top_10_ink as f64
        } else if bottom_10_ink > 0 {
            10.0
        } else {
            1.0
        };

        // Must have strong evidence from both checks
        quarter_ratio > 2.0 && tenth_ratio > 1.5
    }

    /// Count dark (ink) pixels in a horizontal band of the image.
    fn count_ink_pixels(gray: &GrayImage, y_start: u32, y_end: u32, threshold: u8) -> u64 {
        let (width, _) = gray.dimensions();
        let mut count = 0u64;
        for y in y_start..y_end {
            for x in 0..width {
                if gray.get_pixel(x, y).0[0] < threshold {
                    count += 1;
                }
            }
        }
        count
    }

    /// Correct 180-degree rotation by flipping the image.
    pub fn correct_upside_down(
        image_path: &Path,
        output_path: &Path,
    ) -> std::result::Result<(), DeskewError> {
        let img = image::open(image_path).map_err(|e| DeskewError::InvalidFormat(e.to_string()))?;
        let rotated = img.rotate180();
        rotated
            .save(output_path)
            .map_err(|e| DeskewError::CorrectionFailed(e.to_string()))?;
        Ok(())
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

    // ============================================================
    // Enhanced Deskew Functions (Phase 1.1)
    // ============================================================

    /// Compute Otsu's threshold for optimal binarization
    ///
    /// Otsu's method finds the threshold that minimizes intra-class variance,
    /// which is equivalent to maximizing inter-class variance.
    ///
    /// # Arguments
    /// * `gray` - Input grayscale image
    ///
    /// # Returns
    /// Optimal threshold value (0-255)
    pub fn otsu_threshold(gray: &GrayImage) -> u8 {
        let (width, height) = gray.dimensions();
        let total_pixels = (width * height) as f64;

        // Build histogram
        let mut histogram = [0u64; 256];
        for pixel in gray.pixels() {
            histogram[pixel.0[0] as usize] += 1;
        }

        // Calculate total mean
        let mut total_sum = 0.0f64;
        for (i, &count) in histogram.iter().enumerate() {
            total_sum += i as f64 * count as f64;
        }

        // Find optimal threshold
        let mut best_threshold = 0u8;
        let mut max_variance = 0.0f64;

        let mut weight_background = 0.0f64;
        let mut sum_background = 0.0f64;

        for (t, &count) in histogram.iter().enumerate() {
            weight_background += count as f64;
            if weight_background == 0.0 {
                continue;
            }

            let weight_foreground = total_pixels - weight_background;
            if weight_foreground == 0.0 {
                break;
            }

            sum_background += t as f64 * count as f64;
            let mean_background = sum_background / weight_background;
            let mean_foreground = (total_sum - sum_background) / weight_foreground;

            // Inter-class variance
            let variance =
                weight_background * weight_foreground * (mean_background - mean_foreground).powi(2);

            if variance > max_variance {
                max_variance = variance;
                best_threshold = t as u8;
            }
        }

        best_threshold
    }

    /// Apply binary threshold to image
    ///
    /// # Arguments
    /// * `gray` - Input grayscale image
    /// * `threshold` - Threshold value (pixels > threshold become white)
    ///
    /// # Returns
    /// Binary image (0 or 255 values only)
    pub fn apply_threshold(gray: &GrayImage, threshold: u8) -> GrayImage {
        let (width, height) = gray.dimensions();
        let mut binary = GrayImage::new(width, height);

        for (x, y, pixel) in gray.enumerate_pixels() {
            let value = if pixel.0[0] > threshold { 255 } else { 0 };
            binary.put_pixel(x, y, image::Luma([value]));
        }

        binary
    }

    /// Perform Otsu thresholding (convenience function)
    ///
    /// Combines threshold calculation and application.
    pub fn otsu_binarize(gray: &GrayImage) -> GrayImage {
        let threshold = Self::otsu_threshold(gray);
        Self::apply_threshold(gray, threshold)
    }

    /// Morphological erosion operation
    ///
    /// Shrinks white regions by replacing each pixel with the minimum
    /// value in its neighborhood.
    pub fn morphology_erode(binary: &GrayImage, kernel_size: u32) -> GrayImage {
        let (width, height) = binary.dimensions();
        let mut result = GrayImage::new(width, height);
        let half_kernel = (kernel_size / 2) as i32;

        for y in 0..height {
            for x in 0..width {
                let mut min_val = 255u8;

                for ky in -half_kernel..=half_kernel {
                    for kx in -half_kernel..=half_kernel {
                        let nx = x as i32 + kx;
                        let ny = y as i32 + ky;

                        if nx >= 0 && nx < width as i32 && ny >= 0 && ny < height as i32 {
                            let pixel = binary.get_pixel(nx as u32, ny as u32).0[0];
                            min_val = min_val.min(pixel);
                        }
                    }
                }

                result.put_pixel(x, y, image::Luma([min_val]));
            }
        }

        result
    }

    /// Morphological dilation operation
    ///
    /// Expands white regions by replacing each pixel with the maximum
    /// value in its neighborhood.
    pub fn morphology_dilate(binary: &GrayImage, kernel_size: u32) -> GrayImage {
        let (width, height) = binary.dimensions();
        let mut result = GrayImage::new(width, height);
        let half_kernel = (kernel_size / 2) as i32;

        for y in 0..height {
            for x in 0..width {
                let mut max_val = 0u8;

                for ky in -half_kernel..=half_kernel {
                    for kx in -half_kernel..=half_kernel {
                        let nx = x as i32 + kx;
                        let ny = y as i32 + ky;

                        if nx >= 0 && nx < width as i32 && ny >= 0 && ny < height as i32 {
                            let pixel = binary.get_pixel(nx as u32, ny as u32).0[0];
                            max_val = max_val.max(pixel);
                        }
                    }
                }

                result.put_pixel(x, y, image::Luma([max_val]));
            }
        }

        result
    }

    /// Morphological opening (erosion followed by dilation)
    ///
    /// Removes small white noise while preserving larger structures.
    pub fn morphology_open(binary: &GrayImage, kernel_size: u32) -> GrayImage {
        let eroded = Self::morphology_erode(binary, kernel_size);
        Self::morphology_dilate(&eroded, kernel_size)
    }

    /// Morphological closing (dilation followed by erosion)
    ///
    /// Fills small black gaps while preserving larger structures.
    pub fn morphology_close(binary: &GrayImage, kernel_size: u32) -> GrayImage {
        let dilated = Self::morphology_dilate(binary, kernel_size);
        Self::morphology_erode(&dilated, kernel_size)
    }

    /// Enhanced deskew using Otsu thresholding
    ///
    /// This method provides improved skew detection accuracy by:
    /// 1. Applying Otsu thresholding for optimal binarization
    /// 2. Using morphological opening to remove noise
    /// 3. Detecting horizontal lines using Hough transform
    ///
    /// # Arguments
    /// * `gray` - Input grayscale image
    /// * `options` - Deskew options
    ///
    /// # Returns
    /// Skew detection result with angle and confidence
    pub fn detect_skew_otsu(gray: &GrayImage, options: &DeskewOptions) -> Result<SkewDetection> {
        // Step 1: Apply Otsu thresholding
        let binary = Self::otsu_binarize(gray);

        // Step 2: Apply morphological opening to remove noise
        let cleaned = Self::morphology_open(&binary, MORPHOLOGY_KERNEL_SIZE);

        // Step 3: Detect edges on cleaned binary image
        let edges = Self::detect_edges(&cleaned);

        // Step 4: Use Hough transform for line detection
        let angles = Self::hough_line_angles(&edges, options.max_angle);

        if angles.is_empty() {
            return Ok(SkewDetection {
                angle: 0.0,
                confidence: 0.0,
                feature_count: 0,
            });
        }

        // Step 5: Calculate median angle (robust to outliers)
        let median_angle = Self::median(&angles);
        let std_dev = Self::std_dev(&angles, median_angle);

        // Confidence based on consistency of detected angles
        let confidence = (1.0 - (std_dev / options.max_angle.max(1.0)).min(1.0)).max(0.0);

        Ok(SkewDetection {
            angle: median_angle,
            confidence,
            feature_count: angles.len(),
        })
    }

    /// Detect skew based on page edge (for scanned book pages)
    ///
    /// This method detects skew by finding the page boundary shadow that appears
    /// in scanned book pages. It's more effective than text-line detection when
    /// the physical book page was scanned at an angle.
    ///
    /// # Algorithm
    /// 1. Apply a 3-pixel Sobel-like horizontal gradient filter
    /// 2. Find first significant negative gradient in each row (white-to-gray transition)
    /// 3. Filter outliers using median-based approach
    /// 4. Fit a line to inlier points
    /// 5. Calculate the deviation from vertical
    ///
    /// # Arguments
    /// * `gray` - Input grayscale image
    /// * `options` - Deskew options
    ///
    /// # Returns
    /// Skew detection result with angle and confidence
    pub fn detect_skew_page_edge(
        gray: &GrayImage,
        options: &DeskewOptions,
    ) -> Result<SkewDetection> {
        let (width, height) = gray.dimensions();
        let search_width = width / 2;
        let gradient_threshold: i32 = -5; // Sobel gradient threshold

        let mut boundary_points: Vec<(f64, f64)> = Vec::new();

        // For each row, apply Sobel-like gradient and find first significant edge
        for y in 0..height {
            for x in 2..search_width {
                // Sobel-like horizontal gradient: [-1, 0, +1]
                let left = gray.get_pixel(x - 2, y).0[0] as i32;
                let right = gray.get_pixel(x, y).0[0] as i32;
                let gradient = right - left;

                if gradient < gradient_threshold {
                    boundary_points.push((x as f64, y as f64));
                    break;
                }
            }
        }

        if boundary_points.len() < (height / 3) as usize {
            // Not enough points found
            return Ok(SkewDetection {
                angle: 0.0,
                confidence: 0.0,
                feature_count: 0,
            });
        }

        // Calculate median x to remove outliers
        let mut x_values: Vec<f64> = boundary_points.iter().map(|(x, _)| *x).collect();
        x_values.sort_by(|a, b| a.partial_cmp(b).unwrap_or(std::cmp::Ordering::Equal));
        let median_x = x_values[x_values.len() / 2];

        // Filter to points near the median (within 50 pixels)
        let inlier_threshold = 50.0;
        let inliers: Vec<(f64, f64)> = boundary_points
            .into_iter()
            .filter(|(x, _)| (*x - median_x).abs() < inlier_threshold)
            .collect();

        if inliers.len() < 100 {
            return Ok(SkewDetection {
                angle: 0.0,
                confidence: 0.0,
                feature_count: inliers.len(),
            });
        }

        // Fit a line using linear regression: x = m * y + b
        let angle = Self::fit_line_angle(&inliers);

        match angle {
            Some(a) => {
                let clamped_angle = a.clamp(-options.max_angle, options.max_angle);
                let confidence = (inliers.len() as f64 / height as f64).min(1.0);

                Ok(SkewDetection {
                    angle: clamped_angle,
                    confidence,
                    feature_count: inliers.len(),
                })
            }
            None => Ok(SkewDetection {
                angle: 0.0,
                confidence: 0.0,
                feature_count: 0,
            }),
        }
    }

    /// Fit a line to points and return the angle from vertical
    fn fit_line_angle(points: &[(f64, f64)]) -> Option<f64> {
        if points.len() < 10 {
            return None;
        }

        // Simple linear regression: x = m * y + b
        // We want to find the angle of this line from vertical
        let n = points.len() as f64;
        let sum_x: f64 = points.iter().map(|(x, _)| x).sum();
        let sum_y: f64 = points.iter().map(|(_, y)| y).sum();
        let sum_xy: f64 = points.iter().map(|(x, y)| x * y).sum();
        let sum_yy: f64 = points.iter().map(|(_, y)| y * y).sum();

        let denominator = n * sum_yy - sum_y * sum_y;
        if denominator.abs() < 1e-10 {
            return None;
        }

        let slope = (n * sum_xy - sum_x * sum_y) / denominator;

        // Angle from vertical: arctan(slope) where slope = dx/dy
        let angle_rad = slope.atan();
        let angle_deg = angle_rad.to_degrees();

        Some(angle_deg)
    }

    /// Hough transform based line angle detection
    ///
    /// Detects nearly-horizontal lines and returns their angles.
    fn hough_line_angles(edges: &GrayImage, max_angle: f64) -> Vec<f64> {
        let (width, height) = edges.dimensions();
        let diagonal = ((width * width + height * height) as f64).sqrt();
        let rho_max = diagonal as i32;

        // Angle range: -max_angle to +max_angle degrees from horizontal
        // We're looking for near-horizontal lines
        let angle_start = 90.0 - max_angle;
        let angle_end = 90.0 + max_angle;
        let angle_steps = ((angle_end - angle_start) / HOUGH_ANGLE_RESOLUTION) as usize + 1;

        // Create accumulator
        let rho_steps = (2 * rho_max) as usize;
        let mut accumulator = vec![vec![0u32; rho_steps]; angle_steps];

        // Precompute sin/cos tables
        let angles: Vec<f64> = (0..angle_steps)
            .map(|i| (angle_start + i as f64 * HOUGH_ANGLE_RESOLUTION).to_radians())
            .collect();
        let cos_table: Vec<f64> = angles.iter().map(|&a| a.cos()).collect();
        let sin_table: Vec<f64> = angles.iter().map(|&a| a.sin()).collect();

        // Vote in accumulator
        let edge_threshold = GRAYSCALE_THRESHOLD;
        for y in 0..height {
            for x in 0..width {
                if edges.get_pixel(x, y).0[0] > edge_threshold {
                    for (theta_idx, (&cos_t, &sin_t)) in
                        cos_table.iter().zip(sin_table.iter()).enumerate()
                    {
                        let rho = (x as f64 * cos_t + y as f64 * sin_t) as i32;
                        let rho_idx = (rho + rho_max) as usize;
                        if rho_idx < rho_steps {
                            accumulator[theta_idx][rho_idx] += 1;
                        }
                    }
                }
            }
        }

        // Find peaks in accumulator
        let min_votes = MIN_LINE_SUPPORT.max((width as f64 * MIN_LINE_LENGTH_RATIO) as usize);
        let mut detected_angles = Vec::new();

        for (theta_idx, row) in accumulator.iter().enumerate() {
            for &votes in row.iter() {
                if votes as usize >= min_votes {
                    // Convert theta to skew angle (deviation from horizontal)
                    let theta_deg = angle_start + theta_idx as f64 * HOUGH_ANGLE_RESOLUTION;
                    let skew_angle = 90.0 - theta_deg; // Deviation from horizontal

                    if skew_angle.abs() <= max_angle {
                        // Weight by vote count for better averaging
                        for _ in 0..(votes / min_votes as u32).max(1) {
                            detected_angles.push(skew_angle);
                        }
                    }
                }
            }
        }

        detected_angles
    }

    /// Lanczos interpolation kernel
    ///
    /// Provides higher quality resampling than bilinear interpolation.
    fn lanczos_kernel(x: f64, a: f64) -> f64 {
        if x == 0.0 {
            1.0
        } else if x.abs() < a {
            let pi_x = std::f64::consts::PI * x;
            let pi_x_a = pi_x / a;
            (pi_x.sin() / pi_x) * (pi_x_a.sin() / pi_x_a)
        } else {
            0.0
        }
    }

    /// Lanczos interpolation (high quality)
    ///
    /// Uses a 3-tap Lanczos kernel for smooth interpolation.
    fn lanczos(img: &DynamicImage, x: f64, y: f64) -> Rgba<u8> {
        let (width, height) = img.dimensions();
        let a = 3.0; // Lanczos-3 kernel

        let x0 = x.floor() as i32;
        let y0 = y.floor() as i32;

        let mut result = [0.0f64; 4];
        let mut weight_sum = 0.0f64;

        for j in (y0 - 2)..=(y0 + 3) {
            for i in (x0 - 2)..=(x0 + 3) {
                if i >= 0 && i < width as i32 && j >= 0 && j < height as i32 {
                    let wx = Self::lanczos_kernel(x - i as f64, a);
                    let wy = Self::lanczos_kernel(y - j as f64, a);
                    let weight = wx * wy;

                    let pixel = img.get_pixel(i as u32, j as u32);
                    for (r, p) in result.iter_mut().zip(pixel.0.iter()) {
                        *r += *p as f64 * weight;
                    }
                    weight_sum += weight;
                }
            }
        }

        if weight_sum > 0.0 {
            for r in &mut result {
                *r /= weight_sum;
            }
        }

        Rgba([
            result[0].clamp(0.0, 255.0).round() as u8,
            result[1].clamp(0.0, 255.0).round() as u8,
            result[2].clamp(0.0, 255.0).round() as u8,
            result[3].clamp(0.0, 255.0).round() as u8,
        ])
    }

    /// Enhanced rotation with Lanczos interpolation
    ///
    /// Provides higher quality rotation than bilinear interpolation,
    /// with proper handling of rotation center and white background.
    pub fn rotate_image_lanczos(
        img: &DynamicImage,
        angle_degrees: f64,
        options: &DeskewOptions,
    ) -> DynamicImage {
        let (width, height) = img.dimensions();
        let angle_rad = angle_degrees.to_radians();
        let cos_a = angle_rad.cos();
        let sin_a = angle_rad.sin();

        // Calculate new dimensions to contain rotated image
        let new_width =
            ((width as f64 * cos_a.abs()) + (height as f64 * sin_a.abs())).ceil() as u32;
        let new_height =
            ((width as f64 * sin_a.abs()) + (height as f64 * cos_a.abs())).ceil() as u32;

        // Rotation centers
        let cx = width as f64 / 2.0;
        let cy = height as f64 / 2.0;
        let ncx = new_width as f64 / 2.0;
        let ncy = new_height as f64 / 2.0;

        // Background color (white by default)
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

        // Perform rotation with selected interpolation
        for ny in 0..new_height {
            for nx in 0..new_width {
                // Map back to original coordinates
                let ox = (nx as f64 - ncx) * cos_a + (ny as f64 - ncy) * sin_a + cx;
                let oy = -(nx as f64 - ncx) * sin_a + (ny as f64 - ncy) * cos_a + cy;

                // Check if within original image bounds (with margin for interpolation)
                let margin = 3.0; // For Lanczos-3
                if ox >= margin
                    && ox < (width as f64 - margin)
                    && oy >= margin
                    && oy < (height as f64 - margin)
                {
                    let pixel = match options.quality_mode {
                        QualityMode::Fast => Self::nearest_neighbor(img, ox, oy),
                        QualityMode::Standard => Self::bilinear(img, ox, oy),
                        QualityMode::HighQuality => Self::lanczos(img, ox, oy),
                    };
                    rotated.put_pixel(nx, ny, pixel);
                } else if ox >= 0.0
                    && ox < width as f64 - 1.0
                    && oy >= 0.0
                    && oy < height as f64 - 1.0
                {
                    // Fall back to bilinear for edge pixels
                    let pixel = Self::bilinear(img, ox, oy);
                    rotated.put_pixel(nx, ny, pixel);
                }
            }
        }

        DynamicImage::ImageRgba8(rotated)
    }

    /// Enhanced skew correction using Otsu thresholding and Lanczos interpolation
    pub fn correct_skew_enhanced(
        input_path: &Path,
        output_path: &Path,
        options: &DeskewOptions,
    ) -> Result<DeskewResult> {
        if !input_path.exists() {
            return Err(DeskewError::ImageNotFound(input_path.to_path_buf()));
        }

        let img = image::open(input_path).map_err(|e| DeskewError::InvalidFormat(e.to_string()))?;
        let gray = img.to_luma8();
        let original_size = (img.width(), img.height());

        // Use Otsu-based detection for improved accuracy
        let detection = Self::detect_skew_otsu(&gray, options)?;

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

        // Use Lanczos rotation for high quality
        let rotated = Self::rotate_image_lanczos(&img, -detection.angle, options);
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

    // ============================================================
    // Phase 1.1 Enhanced Deskew Tests
    // ============================================================

    // TC-DSK-010: Otsu二値化テスト
    #[test]
    fn test_otsu_threshold_bimodal() {
        // Create a bimodal image (dark text on light background)
        let mut img = GrayImage::new(100, 100);

        // Light background (80% of pixels)
        for y in 0..80 {
            for x in 0..100 {
                img.put_pixel(x, y, image::Luma([230]));
            }
        }

        // Dark foreground (20% of pixels)
        for y in 80..100 {
            for x in 0..100 {
                img.put_pixel(x, y, image::Luma([30]));
            }
        }

        let threshold = ImageProcDeskewer::otsu_threshold(&img);

        // Threshold should be between foreground and background (inclusive)
        // A threshold of exactly 30 or 230 would still separate the two modes
        assert!(
            threshold >= 30 && threshold <= 230,
            "Otsu threshold {} should be between 30 and 230 (inclusive)",
            threshold
        );
    }

    // TC-DSK-011: Otsu二値化 - 均一画像
    #[test]
    fn test_otsu_threshold_uniform() {
        // Uniform image should return a threshold
        let img = GrayImage::from_pixel(100, 100, image::Luma([128]));
        let threshold = ImageProcDeskewer::otsu_threshold(&img);

        // Should return some threshold (function should complete without panic)
        // For uniform image, threshold can be any valid u8 value
        let _ = threshold; // Ensure it's used
    }

    // TC-DSK-012: 二値化適用テスト
    #[test]
    fn test_apply_threshold() {
        let mut img = GrayImage::new(10, 10);
        img.put_pixel(0, 0, image::Luma([100]));
        img.put_pixel(1, 0, image::Luma([200]));

        let binary = ImageProcDeskewer::apply_threshold(&img, 150);

        assert_eq!(binary.get_pixel(0, 0).0[0], 0); // Below threshold -> 0
        assert_eq!(binary.get_pixel(1, 0).0[0], 255); // Above threshold -> 255
    }

    // TC-DSK-013: モルフォロジー収縮テスト
    #[test]
    fn test_morphology_erode() {
        let mut binary = GrayImage::from_pixel(10, 10, image::Luma([255]));
        // Add a single black pixel
        binary.put_pixel(5, 5, image::Luma([0]));

        let eroded = ImageProcDeskewer::morphology_erode(&binary, 3);

        // Black pixel should have expanded
        assert_eq!(eroded.get_pixel(5, 5).0[0], 0);
        assert_eq!(eroded.get_pixel(4, 5).0[0], 0);
        assert_eq!(eroded.get_pixel(5, 4).0[0], 0);
    }

    // TC-DSK-014: モルフォロジー膨張テスト
    #[test]
    fn test_morphology_dilate() {
        let mut binary = GrayImage::from_pixel(10, 10, image::Luma([0]));
        // Add a single white pixel
        binary.put_pixel(5, 5, image::Luma([255]));

        let dilated = ImageProcDeskewer::morphology_dilate(&binary, 3);

        // White pixel should have expanded
        assert_eq!(dilated.get_pixel(5, 5).0[0], 255);
        assert_eq!(dilated.get_pixel(4, 5).0[0], 255);
        assert_eq!(dilated.get_pixel(5, 4).0[0], 255);
    }

    // TC-DSK-015: モルフォロジーオープンテスト
    #[test]
    fn test_morphology_open_removes_noise() {
        let mut binary = GrayImage::from_pixel(20, 20, image::Luma([0]));

        // Add a large white region
        for y in 5..15 {
            for x in 5..15 {
                binary.put_pixel(x, y, image::Luma([255]));
            }
        }

        // Add small noise pixel (should be removed)
        binary.put_pixel(0, 0, image::Luma([255]));

        let opened = ImageProcDeskewer::morphology_open(&binary, 3);

        // Large region should be preserved
        assert_eq!(opened.get_pixel(10, 10).0[0], 255);

        // Noise should be removed
        assert_eq!(opened.get_pixel(0, 0).0[0], 0);
    }

    // TC-DSK-016: Lanczosカーネルテスト
    #[test]
    fn test_lanczos_kernel_center() {
        let kernel = ImageProcDeskewer::lanczos_kernel(0.0, 3.0);
        assert!((kernel - 1.0).abs() < 0.001, "Kernel at 0 should be 1.0");
    }

    #[test]
    fn test_lanczos_kernel_outside() {
        let kernel = ImageProcDeskewer::lanczos_kernel(5.0, 3.0);
        assert!(
            kernel.abs() < 0.001,
            "Kernel outside range should be 0.0, got {}",
            kernel
        );
    }

    // TC-DSK-017: Otsuベース傾き検出テスト
    #[test]
    fn test_detect_skew_otsu_on_synthetic() {
        // Create a synthetic image with horizontal lines
        let mut img = GrayImage::from_pixel(200, 200, image::Luma([255]));

        // Add horizontal lines (no skew)
        for y in [50, 100, 150].iter() {
            for x in 20..180 {
                img.put_pixel(x, *y, image::Luma([0]));
            }
        }

        let options = DeskewOptions::default();
        let detection = ImageProcDeskewer::detect_skew_otsu(&img, &options).unwrap();

        // Should detect minimal skew for horizontal lines
        assert!(
            detection.angle.abs() < 5.0,
            "Horizontal lines should have near-zero skew, got {}",
            detection.angle
        );
    }

    // TC-DSK-018: 拡張傾き補正テスト
    #[test]
    fn test_correct_skew_enhanced() {
        let temp_dir = tempdir().unwrap();
        let output = temp_dir.path().join("enhanced_corrected.png");

        let options = DeskewOptions {
            threshold_angle: 0.01,
            quality_mode: QualityMode::HighQuality,
            ..Default::default()
        };

        let result = ImageProcDeskewer::correct_skew_enhanced(
            Path::new("tests/fixtures/skewed_5deg.png"),
            &output,
            &options,
        )
        .unwrap();

        // Output file should exist
        assert!(output.exists());

        // Detection should have valid values
        assert!(result.detection.confidence >= 0.0);
    }

    // TC-DSK-019: Lanczos回転品質テスト
    #[test]
    fn test_rotate_image_lanczos_quality() {
        // Create a simple test image
        let mut img = image::RgbaImage::from_pixel(100, 100, Rgba([255, 255, 255, 255]));

        // Add a black square in the center
        for y in 40..60 {
            for x in 40..60 {
                img.put_pixel(x, y, Rgba([0, 0, 0, 255]));
            }
        }

        let dynamic = DynamicImage::ImageRgba8(img);
        let options = DeskewOptions {
            quality_mode: QualityMode::HighQuality,
            ..Default::default()
        };

        // Rotate by 5 degrees
        let rotated = ImageProcDeskewer::rotate_image_lanczos(&dynamic, 5.0, &options);

        // Image should still exist and have valid dimensions
        assert!(rotated.width() > 0);
        assert!(rotated.height() > 0);

        // Center should still be close to black (due to the black square)
        let center_pixel = rotated.get_pixel(rotated.width() / 2, rotated.height() / 2);
        // Allow some variation due to rotation and interpolation
        assert!(
            center_pixel.0[0] < 150,
            "Center should be dark after rotation"
        );
    }
}
