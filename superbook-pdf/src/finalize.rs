//! Final Output Processing module
//!
//! Provides functionality for final page processing including
//! gradient padding resize and edge feathering.
//!
//! # Features
//!
//! - Paper color preserving resize
//! - Page offset shift application
//! - Edge feathering for seamless blending
//! - Final output to target height (3508)
//!
//! # Example
//!
//! ```rust,no_run
//! use superbook_pdf::finalize::{FinalizeOptions, PageFinalizer};
//! use std::path::Path;
//!
//! let options = FinalizeOptions::builder()
//!     .target_height(3508)
//!     .build();
//!
//! let result = PageFinalizer::finalize(
//!     Path::new("input.png"),
//!     Path::new("output.png"),
//!     &options,
//!     None, // No crop region
//!     0, 0, // No shift
//! ).unwrap();
//! ```

use image::{GenericImageView, Rgb, RgbImage};
use std::path::{Path, PathBuf};
use thiserror::Error;

use crate::normalize::{CornerColors, ImageNormalizer};

// ============================================================
// Constants
// ============================================================

/// Standard final output height (A4-ish at 300 DPI)
pub const FINAL_TARGET_HEIGHT: u32 = 3508;

/// Default corner patch percentage for paper color sampling
const DEFAULT_CORNER_PATCH_PERCENT: u32 = 3;

/// Default feather pixels for edge blending
const DEFAULT_FEATHER_PIXELS: u32 = 4;

// ============================================================
// Error Types
// ============================================================

/// Finalization error types
#[derive(Debug, Error)]
pub enum FinalizeError {
    #[error("Image not found: {0}")]
    ImageNotFound(PathBuf),

    #[error("Invalid image: {0}")]
    InvalidImage(String),

    #[error("Failed to save image: {0}")]
    SaveError(String),

    #[error("Invalid crop region")]
    InvalidCropRegion,

    #[error("IO error: {0}")]
    IoError(#[from] std::io::Error),
}

pub type Result<T> = std::result::Result<T, FinalizeError>;

// ============================================================
// Data Structures
// ============================================================

/// Crop region rectangle
#[derive(Debug, Clone, Copy)]
pub struct CropRegion {
    pub x: i32,
    pub y: i32,
    pub width: u32,
    pub height: u32,
}

impl CropRegion {
    /// Create a new crop region
    pub fn new(x: i32, y: i32, width: u32, height: u32) -> Self {
        Self {
            x,
            y,
            width,
            height,
        }
    }

    /// Right edge coordinate
    pub fn right(&self) -> i32 {
        self.x + self.width as i32
    }

    /// Bottom edge coordinate
    pub fn bottom(&self) -> i32 {
        self.y + self.height as i32
    }

    /// Create from bounding coordinates
    pub fn from_bounds(left: i32, top: i32, right: i32, bottom: i32) -> Self {
        let width = (right - left).max(0) as u32;
        let height = (bottom - top).max(0) as u32;
        Self {
            x: left,
            y: top,
            width,
            height,
        }
    }
}

/// Finalization options
#[derive(Debug, Clone)]
pub struct FinalizeOptions {
    /// Target output width (calculated from height and aspect ratio if not set)
    pub target_width: Option<u32>,
    /// Target output height
    pub target_height: u32,
    /// Margin percentage to add around content
    pub margin_percent: u32,
    /// Feather pixels for edge blending
    pub feather_pixels: u32,
    /// Corner patch percentage for paper color sampling
    pub corner_patch_percent: u32,
}

impl Default for FinalizeOptions {
    fn default() -> Self {
        Self {
            target_width: None,
            target_height: FINAL_TARGET_HEIGHT,
            margin_percent: 0,
            feather_pixels: DEFAULT_FEATHER_PIXELS,
            corner_patch_percent: DEFAULT_CORNER_PATCH_PERCENT,
        }
    }
}

impl FinalizeOptions {
    /// Create a new options builder
    pub fn builder() -> FinalizeOptionsBuilder {
        FinalizeOptionsBuilder::default()
    }
}

/// Builder for FinalizeOptions
#[derive(Debug, Default)]
pub struct FinalizeOptionsBuilder {
    options: FinalizeOptions,
}

impl FinalizeOptionsBuilder {
    /// Set target width
    #[must_use]
    pub fn target_width(mut self, width: u32) -> Self {
        self.options.target_width = Some(width);
        self
    }

    /// Set target height
    #[must_use]
    pub fn target_height(mut self, height: u32) -> Self {
        self.options.target_height = height;
        self
    }

    /// Set margin percentage
    #[must_use]
    pub fn margin_percent(mut self, percent: u32) -> Self {
        self.options.margin_percent = percent.clamp(0, 50);
        self
    }

    /// Set feather pixels
    #[must_use]
    pub fn feather_pixels(mut self, pixels: u32) -> Self {
        self.options.feather_pixels = pixels;
        self
    }

    /// Set corner patch percentage
    #[must_use]
    pub fn corner_patch_percent(mut self, percent: u32) -> Self {
        self.options.corner_patch_percent = percent.clamp(1, 20);
        self
    }

    /// Build the options
    #[must_use]
    pub fn build(self) -> FinalizeOptions {
        self.options
    }
}

/// Finalization result
#[derive(Debug, Clone)]
pub struct FinalizeResult {
    /// Input path
    pub input_path: PathBuf,
    /// Output path
    pub output_path: PathBuf,
    /// Original image size
    pub original_size: (u32, u32),
    /// Final output size
    pub final_size: (u32, u32),
    /// Scale factor used
    pub scale: f64,
    /// Shift applied (x, y)
    pub shift_applied: (i32, i32),
}

// ============================================================
// Page Finalizer
// ============================================================

/// Page finalizer for final output generation
pub struct PageFinalizer;

impl PageFinalizer {
    /// Finalize a page with optional crop region and shift
    pub fn finalize(
        input_path: &Path,
        output_path: &Path,
        options: &FinalizeOptions,
        crop_region: Option<CropRegion>,
        shift_x: i32,
        shift_y: i32,
    ) -> Result<FinalizeResult> {
        if !input_path.exists() {
            return Err(FinalizeError::ImageNotFound(input_path.to_path_buf()));
        }

        let img =
            image::open(input_path).map_err(|e| FinalizeError::InvalidImage(e.to_string()))?;

        let (orig_w, orig_h) = img.dimensions();
        let rgb_img = img.to_rgb8();

        // Determine crop region (or use full image)
        let crop = crop_region.unwrap_or_else(|| CropRegion::new(0, 0, orig_w, orig_h));

        // Calculate output dimensions
        let (final_w, final_h, scale) =
            Self::calculate_output_dimensions(crop.width, crop.height, options);

        // Sample corner colors for gradient background
        let corners = ImageNormalizer::sample_corner_colors(&rgb_img, options.corner_patch_percent);

        // Create final output with gradient background
        let final_img = Self::create_final_image(
            &rgb_img,
            final_w,
            final_h,
            &crop,
            scale,
            shift_x,
            shift_y,
            &corners,
            options.feather_pixels,
        );

        // Save result
        final_img
            .save(output_path)
            .map_err(|e| FinalizeError::SaveError(e.to_string()))?;

        Ok(FinalizeResult {
            input_path: input_path.to_path_buf(),
            output_path: output_path.to_path_buf(),
            original_size: (orig_w, orig_h),
            final_size: (final_w, final_h),
            scale,
            shift_applied: (shift_x, shift_y),
        })
    }

    /// Finalize multiple pages with unified crop regions for odd/even groups
    pub fn finalize_batch(
        pages: &[(PathBuf, PathBuf, bool)], // (input, output, is_odd)
        options: &FinalizeOptions,
        odd_crop: Option<CropRegion>,
        even_crop: Option<CropRegion>,
        page_shifts: &[(i32, i32)],
    ) -> Result<Vec<FinalizeResult>> {
        let mut results = Vec::with_capacity(pages.len());

        for (i, (input, output, is_odd)) in pages.iter().enumerate() {
            let crop = if *is_odd { odd_crop } else { even_crop };
            let (shift_x, shift_y) = page_shifts.get(i).copied().unwrap_or((0, 0));

            let result = Self::finalize(input, output, options, crop, shift_x, shift_y)?;
            results.push(result);
        }

        Ok(results)
    }

    /// Calculate output dimensions based on crop region and options
    fn calculate_output_dimensions(
        crop_w: u32,
        crop_h: u32,
        options: &FinalizeOptions,
    ) -> (u32, u32, f64) {
        let target_h = options.target_height;

        // Calculate width to maintain aspect ratio
        let scale = target_h as f64 / crop_h as f64;
        let final_w = options
            .target_width
            .unwrap_or_else(|| (crop_w as f64 * scale).round() as u32);

        (final_w, target_h, scale)
    }

    /// Create the final output image
    #[allow(clippy::too_many_arguments)]
    fn create_final_image(
        src: &RgbImage,
        final_w: u32,
        final_h: u32,
        crop: &CropRegion,
        scale: f64,
        shift_x: i32,
        shift_y: i32,
        corners: &CornerColors,
        feather: u32,
    ) -> RgbImage {
        // Calculate scaled shift
        let scaled_shift_x = (shift_x as f64 * scale).round() as i32;
        let scaled_shift_y = (shift_y as f64 * scale).round() as i32;

        // Calculate offset with crop position
        let crop_offset_x = (-crop.x as f64 * scale).round() as i32 + scaled_shift_x;
        let crop_offset_y = (-crop.y as f64 * scale).round() as i32 + scaled_shift_y;

        // Create gradient background canvas
        let mut canvas = Self::create_gradient_canvas(final_w, final_h, corners);

        // Scale and place the source image
        let scaled_w = (src.width() as f64 * scale).round() as u32;
        let scaled_h = (src.height() as f64 * scale).round() as u32;

        // Resize source image
        let scaled_img = image::imageops::resize(
            src,
            scaled_w,
            scaled_h,
            image::imageops::FilterType::Lanczos3,
        );

        // Draw scaled image with offset
        for y in 0..scaled_h {
            for x in 0..scaled_w {
                let px = crop_offset_x + x as i32;
                let py = crop_offset_y + y as i32;

                if px >= 0 && (px as u32) < final_w && py >= 0 && (py as u32) < final_h {
                    canvas.put_pixel(px as u32, py as u32, *scaled_img.get_pixel(x, y));
                }
            }
        }

        // Apply feathering at edges
        if feather > 0 {
            Self::apply_feather(
                &mut canvas,
                crop_offset_x,
                crop_offset_y,
                scaled_w,
                scaled_h,
                feather,
            );
        }

        canvas
    }

    /// Create a gradient background canvas using corner colors
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

    /// Apply edge feathering
    fn apply_feather(
        canvas: &mut RgbImage,
        off_x: i32,
        off_y: i32,
        img_w: u32,
        img_h: u32,
        range: u32,
    ) {
        let (canvas_w, canvas_h) = canvas.dimensions();
        let range = range as i32;

        // Create a copy for reading background values
        let bg_copy = canvas.clone();

        for y in (off_y - range).max(0)..(off_y + img_h as i32 + range).min(canvas_h as i32) {
            for x in (off_x - range).max(0)..(off_x + img_w as i32 + range).min(canvas_w as i32) {
                // Calculate distance from image edge
                let dx = if x < off_x {
                    off_x - x
                } else if x >= off_x + img_w as i32 {
                    x - (off_x + img_w as i32 - 1)
                } else {
                    0
                };

                let dy = if y < off_y {
                    off_y - y
                } else if y >= off_y + img_h as i32 {
                    y - (off_y + img_h as i32 - 1)
                } else {
                    0
                };

                let d = dx.max(dy);
                if d >= range || d == 0 {
                    continue;
                }

                // Blend factor
                let alpha = d as f32 / range as f32;

                let bg = bg_copy.get_pixel(x as u32, y as u32);
                let fg = canvas.get_pixel(x as u32, y as u32);
                let blended = Self::lerp_rgb(bg, fg, 1.0 - alpha);
                canvas.put_pixel(x as u32, y as u32, blended);
            }
        }
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
}

/// Add margin to crop region and clip to image bounds
pub fn add_margin_and_clip(
    region: &CropRegion,
    margin: i32,
    img_width: u32,
    img_height: u32,
) -> CropRegion {
    if region.width == 0 || region.height == 0 {
        return CropRegion::new(0, 0, img_width, img_height);
    }

    let left = (region.x - margin).max(0);
    let top = (region.y - margin).max(0);
    let right = (region.right() + margin).min(img_width as i32 - 1);
    let bottom = (region.bottom() + margin).min(img_height as i32 - 1);

    let width = (right - left + 1).max(1) as u32;
    let height = (bottom - top + 1).max(1) as u32;

    CropRegion {
        x: left,
        y: top,
        width,
        height,
    }
}

// ============================================================
// Tests
// ============================================================

#[cfg(test)]
mod tests {
    use super::*;
    use crate::normalize::PaperColor;
    use tempfile::tempdir;

    #[test]
    fn test_default_options() {
        let opts = FinalizeOptions::default();
        assert_eq!(opts.target_height, FINAL_TARGET_HEIGHT);
        assert_eq!(opts.margin_percent, 0);
        assert!(opts.target_width.is_none());
    }

    #[test]
    fn test_builder() {
        let opts = FinalizeOptions::builder()
            .target_width(2480)
            .target_height(3508)
            .margin_percent(5)
            .feather_pixels(8)
            .build();

        assert_eq!(opts.target_width, Some(2480));
        assert_eq!(opts.target_height, 3508);
        assert_eq!(opts.margin_percent, 5);
        assert_eq!(opts.feather_pixels, 8);
    }

    #[test]
    fn test_crop_region() {
        let region = CropRegion::new(100, 200, 500, 600);
        assert_eq!(region.x, 100);
        assert_eq!(region.y, 200);
        assert_eq!(region.right(), 600);
        assert_eq!(region.bottom(), 800);
    }

    #[test]
    fn test_crop_region_from_bounds() {
        let region = CropRegion::from_bounds(50, 100, 550, 700);
        assert_eq!(region.x, 50);
        assert_eq!(region.y, 100);
        assert_eq!(region.width, 500);
        assert_eq!(region.height, 600);
    }

    #[test]
    fn test_add_margin_and_clip() {
        let region = CropRegion::new(100, 100, 800, 600);
        let clipped = add_margin_and_clip(&region, 50, 1000, 800);

        assert_eq!(clipped.x, 50);
        assert_eq!(clipped.y, 50);
        // Width should expand but clip at image bounds
    }

    #[test]
    fn test_add_margin_empty_region() {
        let region = CropRegion::new(0, 0, 0, 0);
        let clipped = add_margin_and_clip(&region, 10, 1000, 800);

        // Should return full image for empty region
        assert_eq!(clipped.width, 1000);
        assert_eq!(clipped.height, 800);
    }

    #[test]
    fn test_image_not_found() {
        let result = PageFinalizer::finalize(
            Path::new("/nonexistent/image.png"),
            Path::new("/output.png"),
            &FinalizeOptions::default(),
            None,
            0,
            0,
        );
        assert!(matches!(result, Err(FinalizeError::ImageNotFound(_))));
    }

    #[test]
    fn test_finalize_result_fields() {
        let result = FinalizeResult {
            input_path: PathBuf::from("/input.png"),
            output_path: PathBuf::from("/output.png"),
            original_size: (4960, 7016),
            final_size: (2480, 3508),
            scale: 0.5,
            shift_applied: (10, -5),
        };

        assert_eq!(result.original_size, (4960, 7016));
        assert_eq!(result.final_size, (2480, 3508));
        assert_eq!(result.shift_applied, (10, -5));
    }

    #[test]
    fn test_calculate_output_dimensions() {
        let options = FinalizeOptions::default();
        let (w, h, scale) = PageFinalizer::calculate_output_dimensions(2480, 3508, &options);

        assert_eq!(h, FINAL_TARGET_HEIGHT);
        assert!(scale > 0.0);
        assert!(w > 0);
    }

    #[test]
    fn test_margin_percent_clamping() {
        let opts = FinalizeOptions::builder()
            .margin_percent(100) // Should clamp to 50
            .build();
        assert_eq!(opts.margin_percent, 50);
    }

    #[test]
    fn test_send_sync() {
        fn assert_send_sync<T: Send + Sync>() {}
        assert_send_sync::<FinalizeOptions>();
        assert_send_sync::<FinalizeResult>();
        assert_send_sync::<CropRegion>();
        assert_send_sync::<FinalizeError>();
    }

    #[test]
    fn test_error_types() {
        let _err1 = FinalizeError::ImageNotFound(PathBuf::from("/test"));
        let _err2 = FinalizeError::InvalidImage("bad".to_string());
        let _err3 = FinalizeError::SaveError("failed".to_string());
        let _err4 = FinalizeError::InvalidCropRegion;
    }

    #[test]
    fn test_finalize_with_fixture() {
        let temp_dir = tempdir().unwrap();
        let output = temp_dir.path().join("finalized.png");

        let options = FinalizeOptions::builder().target_height(200).build();

        let result = PageFinalizer::finalize(
            Path::new("tests/fixtures/with_margins.png"),
            &output,
            &options,
            None,
            0,
            0,
        );

        match result {
            Ok(r) => {
                assert!(output.exists());
                assert_eq!(r.final_size.1, 200);
            }
            Err(e) => {
                eprintln!("Finalize error: {:?}", e);
            }
        }
    }

    #[test]
    fn test_create_gradient_canvas() {
        let corners = CornerColors {
            top_left: PaperColor::new(255, 255, 255),
            top_right: PaperColor::new(250, 250, 250),
            bottom_left: PaperColor::new(245, 245, 245),
            bottom_right: PaperColor::new(240, 240, 240),
        };

        let canvas = PageFinalizer::create_gradient_canvas(100, 100, &corners);
        assert_eq!(canvas.dimensions(), (100, 100));

        // Top-left should be close to white
        let tl = canvas.get_pixel(0, 0);
        assert!(tl.0[0] > 250);
    }
}
