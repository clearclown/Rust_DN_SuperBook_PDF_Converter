//! Resolution Normalization module
//!
//! Provides functionality to normalize images to internal resolution
//! with paper color preservation.
//!
//! # Features
//!
//! - Target resolution: 4960×7016 (internal high-res)
//! - Paper color estimation from corner sampling
//! - Gradient background fill with bilinear interpolation
//! - Edge feathering for seamless blending
//!
//! # Example
//!
//! ```rust,no_run
//! use superbook_pdf::normalize::{NormalizeOptions, ImageNormalizer};
//! use std::path::Path;
//!
//! let options = NormalizeOptions::builder()
//!     .target_width(4960)
//!     .target_height(7016)
//!     .build();
//!
//! let result = ImageNormalizer::normalize(
//!     Path::new("input.png"),
//!     Path::new("output.png"),
//!     &options
//! ).unwrap();
//!
//! println!("Normalized: {:?} -> {:?}", result.original_size, result.normalized_size);
//! ```

use image::{GenericImageView, Rgb, RgbImage};
use std::path::{Path, PathBuf};
use thiserror::Error;

// ============================================================
// Constants
// ============================================================

/// Internal high-resolution width (standard)
pub const INTERNAL_WIDTH: u32 = 4960;

/// Internal high-resolution height (standard)
pub const INTERNAL_HEIGHT: u32 = 7016;

/// Final output height (standard)
pub const FINAL_OUTPUT_HEIGHT: u32 = 3508;

/// Default corner patch percentage for paper color sampling
const DEFAULT_CORNER_PATCH_PERCENT: u32 = 3;

/// Default feather pixels for edge blending
const DEFAULT_FEATHER_PIXELS: u32 = 4;

/// Low saturation threshold for paper detection
const PAPER_SATURATION_THRESHOLD: u8 = 40;

/// Minimum luminance for paper pixels
const PAPER_LUMINANCE_MIN: u8 = 150;

// ============================================================
// Error Types
// ============================================================

/// Normalization error types
#[derive(Debug, Error)]
pub enum NormalizeError {
    #[error("Image not found: {0}")]
    ImageNotFound(PathBuf),

    #[error("Invalid image: {0}")]
    InvalidImage(String),

    #[error("Failed to save image: {0}")]
    SaveError(String),

    #[error("IO error: {0}")]
    IoError(#[from] std::io::Error),
}

pub type Result<T> = std::result::Result<T, NormalizeError>;

// ============================================================
// Options
// ============================================================

/// Resampler type for resizing
#[derive(Debug, Clone, Copy, Default)]
pub enum Resampler {
    /// Nearest neighbor (fastest, lowest quality)
    Nearest,
    /// Bilinear interpolation
    Bilinear,
    /// Bicubic interpolation
    Bicubic,
    /// Lanczos3 (high quality)
    #[default]
    Lanczos3,
}

/// Padding mode for background fill
#[derive(Debug, Clone, Copy, Default)]
pub enum PaddingMode {
    /// Solid color fill
    Solid([u8; 3]),
    /// Gradient fill using corner colors
    #[default]
    Gradient,
    /// Mirror edges
    Mirror,
}

/// Normalization options
#[derive(Debug, Clone)]
pub struct NormalizeOptions {
    /// Target width
    pub target_width: u32,
    /// Target height
    pub target_height: u32,
    /// Resampler type
    pub resampler: Resampler,
    /// Padding mode
    pub padding_mode: PaddingMode,
    /// Corner patch percentage for paper color sampling
    pub corner_patch_percent: u32,
    /// Feather pixels for edge blending
    pub feather_pixels: u32,
}

impl Default for NormalizeOptions {
    fn default() -> Self {
        Self {
            target_width: INTERNAL_WIDTH,
            target_height: INTERNAL_HEIGHT,
            resampler: Resampler::Lanczos3,
            padding_mode: PaddingMode::Gradient,
            corner_patch_percent: DEFAULT_CORNER_PATCH_PERCENT,
            feather_pixels: DEFAULT_FEATHER_PIXELS,
        }
    }
}

impl NormalizeOptions {
    /// Create a new options builder
    pub fn builder() -> NormalizeOptionsBuilder {
        NormalizeOptionsBuilder::default()
    }

    /// Create options for internal resolution
    pub fn internal_resolution() -> Self {
        Self::default()
    }

    /// Create options for final output
    pub fn final_output(width: u32) -> Self {
        Self {
            target_width: width,
            target_height: FINAL_OUTPUT_HEIGHT,
            ..Default::default()
        }
    }
}

/// Builder for NormalizeOptions
#[derive(Debug, Default)]
pub struct NormalizeOptionsBuilder {
    options: NormalizeOptions,
}

impl NormalizeOptionsBuilder {
    /// Set target width
    #[must_use]
    pub fn target_width(mut self, width: u32) -> Self {
        self.options.target_width = width;
        self
    }

    /// Set target height
    #[must_use]
    pub fn target_height(mut self, height: u32) -> Self {
        self.options.target_height = height;
        self
    }

    /// Set resampler
    #[must_use]
    pub fn resampler(mut self, resampler: Resampler) -> Self {
        self.options.resampler = resampler;
        self
    }

    /// Set padding mode
    #[must_use]
    pub fn padding_mode(mut self, mode: PaddingMode) -> Self {
        self.options.padding_mode = mode;
        self
    }

    /// Set corner patch percentage
    #[must_use]
    pub fn corner_patch_percent(mut self, percent: u32) -> Self {
        self.options.corner_patch_percent = percent.clamp(1, 20);
        self
    }

    /// Set feather pixels
    #[must_use]
    pub fn feather_pixels(mut self, pixels: u32) -> Self {
        self.options.feather_pixels = pixels;
        self
    }

    /// Build the options
    #[must_use]
    pub fn build(self) -> NormalizeOptions {
        self.options
    }
}

// ============================================================
// Result Types
// ============================================================

/// Paper color extracted from image corners
#[derive(Debug, Clone, Copy, Default)]
pub struct PaperColor {
    pub r: u8,
    pub g: u8,
    pub b: u8,
}

impl PaperColor {
    /// Create from RGB values
    pub fn new(r: u8, g: u8, b: u8) -> Self {
        Self { r, g, b }
    }

    /// Convert to RGB array
    pub fn to_rgb(&self) -> [u8; 3] {
        [self.r, self.g, self.b]
    }

    /// Calculate luminance (ITU-R BT.601)
    pub fn luminance(&self) -> u8 {
        let y = 0.299 * self.r as f32 + 0.587 * self.g as f32 + 0.114 * self.b as f32;
        y.round() as u8
    }
}

/// Corner colors for gradient fill
#[derive(Debug, Clone, Copy, Default)]
pub struct CornerColors {
    pub top_left: PaperColor,
    pub top_right: PaperColor,
    pub bottom_left: PaperColor,
    pub bottom_right: PaperColor,
}

impl CornerColors {
    /// Bilinear interpolation at (u, v) where u,v in [0, 1]
    pub fn interpolate(&self, u: f32, v: f32) -> PaperColor {
        fn lerp(a: u8, b: u8, t: f32) -> u8 {
            (a as f32 + (b as f32 - a as f32) * t).round() as u8
        }

        let top_r = lerp(self.top_left.r, self.top_right.r, u);
        let top_g = lerp(self.top_left.g, self.top_right.g, u);
        let top_b = lerp(self.top_left.b, self.top_right.b, u);

        let bot_r = lerp(self.bottom_left.r, self.bottom_right.r, u);
        let bot_g = lerp(self.bottom_left.g, self.bottom_right.g, u);
        let bot_b = lerp(self.bottom_left.b, self.bottom_right.b, u);

        PaperColor {
            r: lerp(top_r, bot_r, v),
            g: lerp(top_g, bot_g, v),
            b: lerp(top_b, bot_b, v),
        }
    }
}

/// Normalization result
#[derive(Debug, Clone)]
pub struct NormalizeResult {
    /// Input path
    pub input_path: PathBuf,
    /// Output path
    pub output_path: PathBuf,
    /// Original image size
    pub original_size: (u32, u32),
    /// Normalized image size
    pub normalized_size: (u32, u32),
    /// Fitted size (after scaling, before padding)
    pub fitted_size: (u32, u32),
    /// Offset (x, y) where fitted image is placed
    pub offset: (i32, i32),
    /// Scale factor used
    pub scale: f64,
    /// Estimated paper color
    pub paper_color: PaperColor,
}

// ============================================================
// Main Implementation
// ============================================================

/// Image normalizer
pub struct ImageNormalizer;

impl ImageNormalizer {
    /// Normalize an image to target resolution with paper color padding
    pub fn normalize(
        input_path: &Path,
        output_path: &Path,
        options: &NormalizeOptions,
    ) -> Result<NormalizeResult> {
        if !input_path.exists() {
            return Err(NormalizeError::ImageNotFound(input_path.to_path_buf()));
        }

        let img =
            image::open(input_path).map_err(|e| NormalizeError::InvalidImage(e.to_string()))?;

        let (orig_w, orig_h) = img.dimensions();
        let rgb_img = img.to_rgb8();

        // Calculate scale to fit within target
        let scale = (options.target_width as f64 / orig_w as f64)
            .min(options.target_height as f64 / orig_h as f64);

        let fitted_w = (orig_w as f64 * scale).round() as u32;
        let fitted_h = (orig_h as f64 * scale).round() as u32;

        // Resize image
        let fitted_img = Self::resize_image(&rgb_img, fitted_w, fitted_h, options.resampler);

        // Sample corner colors for paper color estimation
        let corners = Self::sample_corner_colors(&fitted_img, options.corner_patch_percent);
        let paper_color = Self::average_paper_color(&corners);

        // Create canvas with gradient background
        let (canvas, offset) = Self::create_canvas_with_background(
            &fitted_img,
            options.target_width,
            options.target_height,
            &corners,
            &options.padding_mode,
        );

        // Apply feathering at edges
        let final_img = Self::apply_feather(
            canvas,
            offset.0 as i32,
            offset.1 as i32,
            fitted_w,
            fitted_h,
            options.feather_pixels,
        );

        // Save result
        final_img
            .save(output_path)
            .map_err(|e| NormalizeError::SaveError(e.to_string()))?;

        Ok(NormalizeResult {
            input_path: input_path.to_path_buf(),
            output_path: output_path.to_path_buf(),
            original_size: (orig_w, orig_h),
            normalized_size: (options.target_width, options.target_height),
            fitted_size: (fitted_w, fitted_h),
            offset: (offset.0 as i32, offset.1 as i32),
            scale,
            paper_color,
        })
    }

    /// Normalize with shift (for final output with page offset correction)
    pub fn normalize_with_shift(
        input_path: &Path,
        output_path: &Path,
        options: &NormalizeOptions,
        shift_x: i32,
        shift_y: i32,
        custom_scale: Option<f64>,
    ) -> Result<NormalizeResult> {
        if !input_path.exists() {
            return Err(NormalizeError::ImageNotFound(input_path.to_path_buf()));
        }

        let img =
            image::open(input_path).map_err(|e| NormalizeError::InvalidImage(e.to_string()))?;

        let (orig_w, orig_h) = img.dimensions();
        let rgb_img = img.to_rgb8();

        // Use custom scale or calculate
        let scale = custom_scale.unwrap_or_else(|| {
            (options.target_width as f64 / orig_w as f64)
                .min(options.target_height as f64 / orig_h as f64)
        });

        let fitted_w = (orig_w as f64 * scale).round() as u32;
        let fitted_h = (orig_h as f64 * scale).round() as u32;

        // Resize image
        let fitted_img = Self::resize_image(&rgb_img, fitted_w, fitted_h, options.resampler);

        // Sample corner colors
        let corners = Self::sample_corner_colors(&fitted_img, options.corner_patch_percent);
        let paper_color = Self::average_paper_color(&corners);

        // Calculate offset with shift
        let scaled_shift_x = (shift_x as f64 * scale).round() as i32;
        let scaled_shift_y = (shift_y as f64 * scale).round() as i32;

        let offset_x = scaled_shift_x;
        let offset_y = scaled_shift_y;

        // Create canvas with gradient background and shifted placement
        let canvas = Self::create_canvas_with_shift(
            &fitted_img,
            options.target_width,
            options.target_height,
            &corners,
            &options.padding_mode,
            offset_x,
            offset_y,
        );

        // Apply feathering
        let final_img = Self::apply_feather(
            canvas,
            offset_x,
            offset_y,
            fitted_w,
            fitted_h,
            options.feather_pixels,
        );

        // Save result
        final_img
            .save(output_path)
            .map_err(|e| NormalizeError::SaveError(e.to_string()))?;

        Ok(NormalizeResult {
            input_path: input_path.to_path_buf(),
            output_path: output_path.to_path_buf(),
            original_size: (orig_w, orig_h),
            normalized_size: (options.target_width, options.target_height),
            fitted_size: (fitted_w, fitted_h),
            offset: (offset_x, offset_y),
            scale,
            paper_color,
        })
    }

    /// Estimate paper color from entire image
    pub fn estimate_paper_color(image: &RgbImage) -> PaperColor {
        let (w, h) = image.dimensions();
        let step = 4u32; // Sample every 4th pixel

        // Build luminance histogram
        let mut histogram = [0u64; 256];
        let mut total = 0u64;

        for y in (0..h).step_by(step as usize) {
            for x in (0..w).step_by(step as usize) {
                let pixel = image.get_pixel(x, y);
                let lum = Self::luminance(pixel.0[0], pixel.0[1], pixel.0[2]);
                histogram[lum as usize] += 1;
                total += 1;
            }
        }

        // Find 95th percentile luminance (paper threshold)
        let target = (total as f64 * 0.95) as u64;
        let mut acc = 0u64;
        let mut threshold = 255u8;

        for i in (0..=255).rev() {
            acc += histogram[i];
            if acc >= (total - target) {
                threshold = i as u8;
                break;
            }
        }

        // Average pixels above threshold with low saturation
        let mut sum_r = 0u64;
        let mut sum_g = 0u64;
        let mut sum_b = 0u64;
        let mut count = 0u64;

        for y in (0..h).step_by(step as usize) {
            for x in (0..w).step_by(step as usize) {
                let pixel = image.get_pixel(x, y);
                let (r, g, b) = (pixel.0[0], pixel.0[1], pixel.0[2]);
                let lum = Self::luminance(r, g, b);

                if lum >= threshold {
                    let sat = Self::saturation(r, g, b);
                    if sat < PAPER_SATURATION_THRESHOLD {
                        sum_r += r as u64;
                        sum_g += g as u64;
                        sum_b += b as u64;
                        count += 1;
                    }
                }
            }
        }

        if count == 0 {
            // Fallback to white
            PaperColor::new(255, 255, 255)
        } else {
            PaperColor::new(
                (sum_r / count) as u8,
                (sum_g / count) as u8,
                (sum_b / count) as u8,
            )
        }
    }

    /// Sample paper colors from image corners
    pub fn sample_corner_colors(image: &RgbImage, patch_percent: u32) -> CornerColors {
        let (w, h) = image.dimensions();
        let patch_w = (w * patch_percent / 100).max(8);
        let patch_h = (h * patch_percent / 100).max(8);

        let top_left = Self::average_patch_color(image, 0, 0, patch_w, patch_h);
        let top_right = Self::average_patch_color(image, w - patch_w, 0, patch_w, patch_h);
        let bottom_left = Self::average_patch_color(image, 0, h - patch_h, patch_w, patch_h);
        let bottom_right =
            Self::average_patch_color(image, w - patch_w, h - patch_h, patch_w, patch_h);

        CornerColors {
            top_left,
            top_right,
            bottom_left,
            bottom_right,
        }
    }

    // ============================================================
    // Private Helper Functions
    // ============================================================

    fn resize_image(img: &RgbImage, width: u32, height: u32, resampler: Resampler) -> RgbImage {
        let filter = match resampler {
            Resampler::Nearest => image::imageops::FilterType::Nearest,
            Resampler::Bilinear => image::imageops::FilterType::Triangle,
            Resampler::Bicubic => image::imageops::FilterType::CatmullRom,
            Resampler::Lanczos3 => image::imageops::FilterType::Lanczos3,
        };

        image::imageops::resize(img, width, height, filter)
    }

    fn average_patch_color(image: &RgbImage, sx: u32, sy: u32, w: u32, h: u32) -> PaperColor {
        let (img_w, img_h) = image.dimensions();

        // Clamp bounds
        let sx = sx.min(img_w.saturating_sub(1));
        let sy = sy.min(img_h.saturating_sub(1));
        let w = w.min(img_w - sx);
        let h = h.min(img_h - sy);

        // Build luminance histogram (sample every 2nd pixel)
        let mut histogram = [0u64; 256];
        let mut samples = 0u64;

        for y in (sy..sy + h).step_by(2) {
            for x in (sx..sx + w).step_by(2) {
                let pixel = image.get_pixel(x, y);
                let lum = Self::luminance(pixel.0[0], pixel.0[1], pixel.0[2]);
                histogram[lum as usize] += 1;
                samples += 1;
            }
        }

        if samples == 0 {
            return PaperColor::new(255, 255, 255);
        }

        // Find top 5% luminance threshold
        let target = (samples as f64 * 0.05) as u64;
        let mut acc = 0u64;
        let mut threshold = 255u8;

        for i in (0..=255).rev() {
            acc += histogram[i];
            if acc >= target {
                threshold = i as u8;
                break;
            }
        }

        // Fallback if threshold too low
        if threshold < PAPER_LUMINANCE_MIN {
            return Self::estimate_paper_color(image);
        }

        // Average low-saturation pixels above threshold
        let mut sum_r = 0u64;
        let mut sum_g = 0u64;
        let mut sum_b = 0u64;
        let mut count = 0u64;

        for y in (sy..sy + h).step_by(2) {
            for x in (sx..sx + w).step_by(2) {
                let pixel = image.get_pixel(x, y);
                let (r, g, b) = (pixel.0[0], pixel.0[1], pixel.0[2]);
                let lum = Self::luminance(r, g, b);

                if lum >= threshold {
                    let sat = Self::saturation(r, g, b);
                    if sat < PAPER_SATURATION_THRESHOLD {
                        sum_r += r as u64;
                        sum_g += g as u64;
                        sum_b += b as u64;
                        count += 1;
                    }
                }
            }
        }

        if count == 0 {
            Self::estimate_paper_color(image)
        } else {
            PaperColor::new(
                (sum_r / count) as u8,
                (sum_g / count) as u8,
                (sum_b / count) as u8,
            )
        }
    }

    fn average_paper_color(corners: &CornerColors) -> PaperColor {
        let r = (corners.top_left.r as u16
            + corners.top_right.r as u16
            + corners.bottom_left.r as u16
            + corners.bottom_right.r as u16)
            / 4;
        let g = (corners.top_left.g as u16
            + corners.top_right.g as u16
            + corners.bottom_left.g as u16
            + corners.bottom_right.g as u16)
            / 4;
        let b = (corners.top_left.b as u16
            + corners.top_right.b as u16
            + corners.bottom_left.b as u16
            + corners.bottom_right.b as u16)
            / 4;

        PaperColor::new(r as u8, g as u8, b as u8)
    }

    fn create_canvas_with_background(
        fitted: &RgbImage,
        target_w: u32,
        target_h: u32,
        corners: &CornerColors,
        padding_mode: &PaddingMode,
    ) -> (RgbImage, (u32, u32)) {
        let (fitted_w, fitted_h) = fitted.dimensions();

        // Calculate center offset
        let offset_x = (target_w.saturating_sub(fitted_w)) / 2;
        let offset_y = (target_h.saturating_sub(fitted_h)) / 2;

        // Create canvas with background
        let mut canvas = match padding_mode {
            PaddingMode::Solid(color) => RgbImage::from_pixel(target_w, target_h, Rgb(*color)),
            PaddingMode::Gradient => Self::create_gradient_canvas(target_w, target_h, corners),
            PaddingMode::Mirror => {
                // For mirror mode, start with gradient and would add mirroring later
                Self::create_gradient_canvas(target_w, target_h, corners)
            }
        };

        // Draw fitted image on canvas
        for y in 0..fitted_h {
            for x in 0..fitted_w {
                let px = offset_x + x;
                let py = offset_y + y;
                if px < target_w && py < target_h {
                    canvas.put_pixel(px, py, *fitted.get_pixel(x, y));
                }
            }
        }

        (canvas, (offset_x, offset_y))
    }

    fn create_canvas_with_shift(
        fitted: &RgbImage,
        target_w: u32,
        target_h: u32,
        corners: &CornerColors,
        padding_mode: &PaddingMode,
        offset_x: i32,
        offset_y: i32,
    ) -> RgbImage {
        let (fitted_w, fitted_h) = fitted.dimensions();

        // Create canvas with background
        let mut canvas = match padding_mode {
            PaddingMode::Solid(color) => RgbImage::from_pixel(target_w, target_h, Rgb(*color)),
            PaddingMode::Gradient | PaddingMode::Mirror => {
                Self::create_gradient_canvas(target_w, target_h, corners)
            }
        };

        // Draw fitted image with shift (may clip outside bounds)
        for y in 0..fitted_h {
            for x in 0..fitted_w {
                let px = offset_x + x as i32;
                let py = offset_y + y as i32;
                if px >= 0 && (px as u32) < target_w && py >= 0 && (py as u32) < target_h {
                    canvas.put_pixel(px as u32, py as u32, *fitted.get_pixel(x, y));
                }
            }
        }

        canvas
    }

    fn create_gradient_canvas(width: u32, height: u32, corners: &CornerColors) -> RgbImage {
        let mut canvas = RgbImage::new(width, height);

        for y in 0..height {
            let v = y as f32 / (height - 1).max(1) as f32;
            for x in 0..width {
                let u = x as f32 / (width - 1).max(1) as f32;
                let color = corners.interpolate(u, v);
                canvas.put_pixel(x, y, Rgb([color.r, color.g, color.b]));
            }
        }

        canvas
    }

    fn apply_feather(
        mut canvas: RgbImage,
        off_x: i32,
        off_y: i32,
        fitted_w: u32,
        fitted_h: u32,
        range: u32,
    ) -> RgbImage {
        if range == 0 {
            return canvas;
        }

        let (canvas_w, canvas_h) = canvas.dimensions();
        let range = range as i32;

        // Process feather zone around fitted image
        for y in (off_y - range)..(off_y + fitted_h as i32 + range) {
            if y < 0 || y >= canvas_h as i32 {
                continue;
            }

            for x in (off_x - range)..(off_x + fitted_w as i32 + range) {
                if x < 0 || x >= canvas_w as i32 {
                    continue;
                }

                // Calculate distance from fitted image edge
                let dx = if x < off_x {
                    off_x - x
                } else if x >= off_x + fitted_w as i32 {
                    x - (off_x + fitted_w as i32 - 1)
                } else {
                    0
                };

                let dy = if y < off_y {
                    off_y - y
                } else if y >= off_y + fitted_h as i32 {
                    y - (off_y + fitted_h as i32 - 1)
                } else {
                    0
                };

                let d = dx.max(dy);
                if d >= range || d == 0 {
                    continue;
                }

                // Blend factor: 0 at edge, 1 at range distance
                let alpha = d as f32 / range as f32;

                // Get current pixel (background in feather zone)
                let bg = canvas.get_pixel(x as u32, y as u32);

                // Get foreground (from fitted image or background)
                let inside = x >= off_x
                    && x < off_x + fitted_w as i32
                    && y >= off_y
                    && y < off_y + fitted_h as i32;

                if !inside {
                    // Outside: blend towards background (already there)
                    continue;
                }

                // Inside edge zone: blend with background
                let fg = canvas.get_pixel(x as u32, y as u32);
                let blended = Self::lerp_rgb(bg, fg, 1.0 - alpha);
                canvas.put_pixel(x as u32, y as u32, blended);
            }
        }

        canvas
    }

    fn lerp_rgb(a: &Rgb<u8>, b: &Rgb<u8>, t: f32) -> Rgb<u8> {
        fn lerp(a: u8, b: u8, t: f32) -> u8 {
            (a as f32 + (b as f32 - a as f32) * t)
                .round()
                .clamp(0.0, 255.0) as u8
        }

        Rgb([
            lerp(a.0[0], b.0[0], t),
            lerp(a.0[1], b.0[1], t),
            lerp(a.0[2], b.0[2], t),
        ])
    }

    fn luminance(r: u8, g: u8, b: u8) -> u8 {
        (0.299 * r as f32 + 0.587 * g as f32 + 0.114 * b as f32).round() as u8
    }

    fn saturation(r: u8, g: u8, b: u8) -> u8 {
        let max = r.max(g).max(b);
        let min = r.min(g).min(b);
        if max == 0 {
            0
        } else {
            ((max - min) as u16 * 255 / max as u16) as u8
        }
    }
}

// ============================================================
// Tests
// ============================================================

#[cfg(test)]
mod tests {
    use super::*;
    use tempfile::tempdir;

    #[test]
    fn test_default_options() {
        let opts = NormalizeOptions::default();
        assert_eq!(opts.target_width, INTERNAL_WIDTH);
        assert_eq!(opts.target_height, INTERNAL_HEIGHT);
        assert!(matches!(opts.resampler, Resampler::Lanczos3));
        assert!(matches!(opts.padding_mode, PaddingMode::Gradient));
    }

    #[test]
    fn test_builder() {
        let opts = NormalizeOptions::builder()
            .target_width(1920)
            .target_height(1080)
            .resampler(Resampler::Bicubic)
            .padding_mode(PaddingMode::Solid([255, 255, 255]))
            .corner_patch_percent(5)
            .feather_pixels(8)
            .build();

        assert_eq!(opts.target_width, 1920);
        assert_eq!(opts.target_height, 1080);
        assert!(matches!(opts.resampler, Resampler::Bicubic));
        assert_eq!(opts.corner_patch_percent, 5);
        assert_eq!(opts.feather_pixels, 8);
    }

    #[test]
    fn test_paper_color_luminance() {
        let color = PaperColor::new(255, 255, 255);
        assert_eq!(color.luminance(), 255);

        let color = PaperColor::new(0, 0, 0);
        assert_eq!(color.luminance(), 0);

        // Gray
        let color = PaperColor::new(128, 128, 128);
        assert_eq!(color.luminance(), 128);
    }

    #[test]
    fn test_corner_colors_interpolate() {
        let corners = CornerColors {
            top_left: PaperColor::new(0, 0, 0),
            top_right: PaperColor::new(255, 0, 0),
            bottom_left: PaperColor::new(0, 255, 0),
            bottom_right: PaperColor::new(255, 255, 0),
        };

        // Top-left corner
        let c = corners.interpolate(0.0, 0.0);
        assert_eq!(c.r, 0);

        // Top-right corner
        let c = corners.interpolate(1.0, 0.0);
        assert_eq!(c.r, 255);
    }

    #[test]
    fn test_image_not_found() {
        let result = ImageNormalizer::normalize(
            Path::new("/nonexistent/image.png"),
            Path::new("/output.png"),
            &NormalizeOptions::default(),
        );
        assert!(matches!(result, Err(NormalizeError::ImageNotFound(_))));
    }

    #[test]
    fn test_luminance_calculation() {
        assert_eq!(ImageNormalizer::luminance(255, 255, 255), 255);
        assert_eq!(ImageNormalizer::luminance(0, 0, 0), 0);
        // Pure red
        let lum = ImageNormalizer::luminance(255, 0, 0);
        assert!(lum > 70 && lum < 80); // ~76
    }

    #[test]
    fn test_saturation_calculation() {
        // White has 0 saturation
        assert_eq!(ImageNormalizer::saturation(255, 255, 255), 0);
        // Pure red has max saturation
        assert_eq!(ImageNormalizer::saturation(255, 0, 0), 255);
        // Gray has 0 saturation
        assert_eq!(ImageNormalizer::saturation(128, 128, 128), 0);
    }

    #[test]
    fn test_internal_resolution_preset() {
        let opts = NormalizeOptions::internal_resolution();
        assert_eq!(opts.target_width, 4960);
        assert_eq!(opts.target_height, 7016);
    }

    #[test]
    fn test_final_output_preset() {
        let opts = NormalizeOptions::final_output(2480);
        assert_eq!(opts.target_width, 2480);
        assert_eq!(opts.target_height, 3508);
    }

    #[test]
    fn test_normalize_with_fixture() {
        let temp_dir = tempdir().unwrap();
        let output = temp_dir.path().join("normalized.png");

        let options = NormalizeOptions::builder()
            .target_width(200)
            .target_height(300)
            .build();

        let result = ImageNormalizer::normalize(
            Path::new("tests/fixtures/with_margins.png"),
            &output,
            &options,
        );

        match result {
            Ok(r) => {
                assert!(output.exists());
                assert_eq!(r.normalized_size, (200, 300));
                assert!(r.scale > 0.0);
            }
            Err(e) => {
                eprintln!("Normalize error: {:?}", e);
            }
        }
    }

    #[test]
    fn test_estimate_paper_color() {
        // Create a simple white image
        let img = RgbImage::from_pixel(100, 100, Rgb([255, 255, 255]));
        let color = ImageNormalizer::estimate_paper_color(&img);
        assert_eq!(color.r, 255);
        assert_eq!(color.g, 255);
        assert_eq!(color.b, 255);
    }

    #[test]
    fn test_sample_corner_colors() {
        // Create white image
        let img = RgbImage::from_pixel(100, 100, Rgb([240, 240, 240]));
        let corners = ImageNormalizer::sample_corner_colors(&img, 10);

        // All corners should be similar
        assert!(corners.top_left.r > 230);
        assert!(corners.top_right.r > 230);
        assert!(corners.bottom_left.r > 230);
        assert!(corners.bottom_right.r > 230);
    }

    #[test]
    fn test_resampler_variants() {
        let _near = Resampler::Nearest;
        let _bi = Resampler::Bilinear;
        let _bic = Resampler::Bicubic;
        let _lan = Resampler::Lanczos3;
    }

    #[test]
    fn test_padding_mode_variants() {
        let _solid = PaddingMode::Solid([255, 255, 255]);
        let _grad = PaddingMode::Gradient;
        let _mirror = PaddingMode::Mirror;
    }

    #[test]
    fn test_normalize_result_fields() {
        let result = NormalizeResult {
            input_path: PathBuf::from("/input.png"),
            output_path: PathBuf::from("/output.png"),
            original_size: (1000, 1500),
            normalized_size: (4960, 7016),
            fitted_size: (4960, 7000),
            offset: (0, 8),
            scale: 4.96,
            paper_color: PaperColor::new(250, 248, 245),
        };

        assert_eq!(result.original_size, (1000, 1500));
        assert_eq!(result.normalized_size, (4960, 7016));
        assert!(result.scale > 4.0);
    }

    #[test]
    fn test_error_types() {
        let _err1 = NormalizeError::ImageNotFound(PathBuf::from("/test"));
        let _err2 = NormalizeError::InvalidImage("bad".to_string());
        let _err3 = NormalizeError::SaveError("failed".to_string());
    }

    #[test]
    fn test_corner_patch_clamping() {
        let opts = NormalizeOptions::builder().corner_patch_percent(50).build();
        assert_eq!(opts.corner_patch_percent, 20); // Clamped to max
    }

    #[test]
    fn test_send_sync() {
        fn assert_send_sync<T: Send + Sync>() {}
        assert_send_sync::<NormalizeOptions>();
        assert_send_sync::<NormalizeError>();
        assert_send_sync::<NormalizeResult>();
        assert_send_sync::<PaperColor>();
        assert_send_sync::<CornerColors>();
    }
}
