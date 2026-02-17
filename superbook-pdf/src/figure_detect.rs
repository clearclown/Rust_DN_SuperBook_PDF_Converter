//! Figure detection module for scanned book pages
//!
//! Detects figures, full-page images, and covers in scanned book pages
//! using connected component analysis and texture analysis.

use image::{DynamicImage, GrayImage, Luma};
use imageproc::contours::{find_contours, BorderType};
use thiserror::Error;

use crate::yomitoku::{OcrResult, TextBlock};

/// Error type for figure detection
#[derive(Debug, Error)]
pub enum FigureDetectError {
    #[error("Image loading failed: {0}")]
    ImageLoadFailed(String),

    #[error("Processing error: {0}")]
    ProcessingError(String),

    #[error("IO error: {0}")]
    IoError(#[from] std::io::Error),
}

/// Type of detected region
#[derive(Debug, Clone, PartialEq, Eq)]
pub enum RegionType {
    /// In-page figure (diagram, chart, photo)
    Figure,
    /// Full-page image (entire page is an image)
    FullPageImage,
    /// Cover page
    Cover,
}

/// A detected figure region
#[derive(Debug, Clone)]
pub struct FigureRegion {
    /// Bounding box: (x, y, width, height)
    pub bbox: (u32, u32, u32, u32),
    /// Area in pixels
    pub area: u32,
    /// Type of detected region
    pub region_type: RegionType,
}

/// Page classification result
#[derive(Debug, Clone)]
pub enum PageClassification {
    /// Cover page (first page, mostly image)
    Cover,
    /// Full-page image with no significant text
    FullPageImage,
    /// Mixed content: text with embedded figures
    Mixed { figures: Vec<FigureRegion> },
    /// Text-only page
    TextOnly,
}

/// Options for figure detection
#[derive(Debug, Clone)]
pub struct FigureDetectOptions {
    /// Minimum figure area as fraction of page area (default: 0.02 = 2%)
    pub min_area_fraction: f32,
    /// Maximum aspect ratio for valid figures (default: 10.0)
    pub max_aspect_ratio: f32,
    /// Text coverage threshold for full-page image classification (default: 0.05 = 5%)
    pub fullpage_text_threshold: f32,
    /// Text coverage threshold for text-only classification (default: 0.80 = 80%)
    pub textonly_text_threshold: f32,
    /// Dilation kernel size for text region merging (default: 15)
    pub dilation_size: u32,
    /// Binarization threshold (default: 200)
    pub binary_threshold: u8,
}

impl Default for FigureDetectOptions {
    fn default() -> Self {
        Self {
            min_area_fraction: 0.02,
            max_aspect_ratio: 10.0,
            fullpage_text_threshold: 0.05,
            textonly_text_threshold: 0.80,
            dilation_size: 15,
            binary_threshold: 200,
        }
    }
}

/// Figure detector for scanned book pages
pub struct FigureDetector;

impl FigureDetector {
    /// Classify a page and detect figures based on OCR results
    pub fn classify_page(
        image: &DynamicImage,
        ocr_result: &OcrResult,
        page_index: usize,
        options: &FigureDetectOptions,
    ) -> PageClassification {
        let (img_w, img_h) = (image.width(), image.height());
        let page_area = (img_w as f64) * (img_h as f64);

        if page_area == 0.0 {
            return PageClassification::TextOnly;
        }

        // Calculate text coverage from OCR bounding boxes
        let text_area = Self::calculate_text_area(&ocr_result.text_blocks, img_w, img_h);
        let text_coverage = text_area as f64 / page_area;

        // Cover: first page with very little text
        if page_index == 0 && text_coverage < options.fullpage_text_threshold as f64 {
            return PageClassification::Cover;
        }

        // Full-page image: very little text
        if text_coverage < options.fullpage_text_threshold as f64 {
            return PageClassification::FullPageImage;
        }

        // Text-only: mostly text
        if text_coverage > options.textonly_text_threshold as f64 {
            return PageClassification::TextOnly;
        }

        // Mixed: detect figure regions in non-text areas
        let figures = Self::detect_figures(image, ocr_result, options);

        if figures.is_empty() {
            PageClassification::TextOnly
        } else {
            PageClassification::Mixed { figures }
        }
    }

    /// Detect figure regions by analyzing non-text areas
    fn detect_figures(
        image: &DynamicImage,
        ocr_result: &OcrResult,
        options: &FigureDetectOptions,
    ) -> Vec<FigureRegion> {
        let (img_w, img_h) = (image.width(), image.height());
        let page_area = (img_w as u64) * (img_h as u64);
        let min_area = (page_area as f64 * options.min_area_fraction as f64) as u64;

        // Create text mask: mark text regions as white (occupied)
        let mut text_mask = GrayImage::new(img_w, img_h);
        let dilation = options.dilation_size;

        for block in &ocr_result.text_blocks {
            let (bx, by, bw, bh) = block.bbox;
            // Apply dilation to text regions (add margin)
            let x_start = bx.saturating_sub(dilation);
            let y_start = by.saturating_sub(dilation);
            let x_end = (bx + bw + dilation).min(img_w);
            let y_end = (by + bh + dilation).min(img_h);

            for y in y_start..y_end {
                for x in x_start..x_end {
                    text_mask.put_pixel(x, y, Luma([255]));
                }
            }
        }

        // Binarize the original image (non-white = potential content)
        let gray = image.to_luma8();
        let mut content_mask = GrayImage::new(img_w, img_h);
        for y in 0..img_h {
            for x in 0..img_w {
                let pixel = gray.get_pixel(x, y);
                // Mark non-white pixels as content
                if pixel[0] < options.binary_threshold {
                    content_mask.put_pixel(x, y, Luma([255]));
                }
            }
        }

        // Non-text content: content that is NOT in text regions
        let mut non_text_content = GrayImage::new(img_w, img_h);
        for y in 0..img_h {
            for x in 0..img_w {
                let is_content = content_mask.get_pixel(x, y)[0] > 0;
                let is_text = text_mask.get_pixel(x, y)[0] > 0;
                if is_content && !is_text {
                    non_text_content.put_pixel(x, y, Luma([255]));
                }
            }
        }

        // Find connected components using contours
        let contours = find_contours::<u32>(&non_text_content);

        // Extract bounding boxes from contours
        let mut figures = Vec::new();

        for contour in &contours {
            if contour.border_type == BorderType::Hole {
                continue;
            }

            if contour.points.is_empty() {
                continue;
            }

            let mut min_x = u32::MAX;
            let mut min_y = u32::MAX;
            let mut max_x = 0u32;
            let mut max_y = 0u32;

            for p in &contour.points {
                min_x = min_x.min(p.x);
                min_y = min_y.min(p.y);
                max_x = max_x.max(p.x);
                max_y = max_y.max(p.y);
            }

            let w = max_x.saturating_sub(min_x);
            let h = max_y.saturating_sub(min_y);
            let area = (w as u64) * (h as u64);

            // Filter by area
            if area < min_area {
                continue;
            }

            // Filter by aspect ratio
            if w > 0 && h > 0 {
                let aspect = if w > h {
                    w as f32 / h as f32
                } else {
                    h as f32 / w as f32
                };
                if aspect > options.max_aspect_ratio {
                    continue;
                }
            }

            figures.push(FigureRegion {
                bbox: (min_x, min_y, w, h),
                area: area as u32,
                region_type: RegionType::Figure,
            });
        }

        // Merge overlapping figure regions
        Self::merge_overlapping(&mut figures);

        figures
    }

    /// Calculate total text area from OCR text blocks, clamped to image bounds
    fn calculate_text_area(blocks: &[TextBlock], img_w: u32, img_h: u32) -> u64 {
        let mut total = 0u64;
        for block in blocks {
            let (bx, by, bw, bh) = block.bbox;
            let w = bw.min(img_w.saturating_sub(bx));
            let h = bh.min(img_h.saturating_sub(by));
            total += (w as u64) * (h as u64);
        }
        total
    }

    /// Merge overlapping figure regions
    fn merge_overlapping(figures: &mut Vec<FigureRegion>) {
        if figures.len() <= 1 {
            return;
        }

        let mut merged = true;
        while merged {
            merged = false;
            let mut i = 0;
            while i < figures.len() {
                let mut j = i + 1;
                while j < figures.len() {
                    if Self::regions_overlap(&figures[i], &figures[j]) {
                        // Merge j into i
                        let a = &figures[i];
                        let b = &figures[j];
                        let x1 = a.bbox.0.min(b.bbox.0);
                        let y1 = a.bbox.1.min(b.bbox.1);
                        let x2 = (a.bbox.0 + a.bbox.2).max(b.bbox.0 + b.bbox.2);
                        let y2 = (a.bbox.1 + a.bbox.3).max(b.bbox.1 + b.bbox.3);
                        let w = x2 - x1;
                        let h = y2 - y1;
                        figures[i] = FigureRegion {
                            bbox: (x1, y1, w, h),
                            area: w * h,
                            region_type: RegionType::Figure,
                        };
                        figures.remove(j);
                        merged = true;
                    } else {
                        j += 1;
                    }
                }
                i += 1;
            }
        }
    }

    /// Check if two regions overlap
    fn regions_overlap(a: &FigureRegion, b: &FigureRegion) -> bool {
        let a_right = a.bbox.0 + a.bbox.2;
        let a_bottom = a.bbox.1 + a.bbox.3;
        let b_right = b.bbox.0 + b.bbox.2;
        let b_bottom = b.bbox.1 + b.bbox.3;

        a.bbox.0 < b_right && a_right > b.bbox.0 && a.bbox.1 < b_bottom && a_bottom > b.bbox.1
    }

    /// Crop a figure region from the source image
    pub fn crop_figure(image: &DynamicImage, region: &FigureRegion) -> DynamicImage {
        let (x, y, w, h) = region.bbox;
        // Add 3% margin
        let margin_x = (w as f32 * 0.03) as u32;
        let margin_y = (h as f32 * 0.03) as u32;

        let crop_x = x.saturating_sub(margin_x);
        let crop_y = y.saturating_sub(margin_y);
        let crop_w = (w + margin_x * 2).min(image.width().saturating_sub(crop_x));
        let crop_h = (h + margin_y * 2).min(image.height().saturating_sub(crop_y));

        image.crop_imm(crop_x, crop_y, crop_w, crop_h)
    }

    /// Detect the actual content bounding box of an image by finding non-white pixels.
    /// Returns `(x, y, width, height)` of the content area, or `None` if the image is blank.
    /// `threshold` controls what counts as "white" (default ~240).
    pub fn find_content_bounds(image: &DynamicImage, threshold: u8) -> Option<(u32, u32, u32, u32)> {
        let gray = image.to_luma8();
        let (img_w, img_h) = (gray.width(), gray.height());

        let mut min_x = img_w;
        let mut min_y = img_h;
        let mut max_x = 0u32;
        let mut max_y = 0u32;

        for y in 0..img_h {
            for x in 0..img_w {
                if gray.get_pixel(x, y)[0] < threshold {
                    min_x = min_x.min(x);
                    min_y = min_y.min(y);
                    max_x = max_x.max(x);
                    max_y = max_y.max(y);
                }
            }
        }

        if max_x < min_x || max_y < min_y {
            return None; // Blank image
        }

        let w = max_x - min_x + 1;
        let h = max_y - min_y + 1;
        Some((min_x, min_y, w, h))
    }

    /// Crop an image to its actual content area, removing white margins.
    /// Adds a small padding (1% of content size) around the detected content.
    /// Returns the original image if no content bounds are detected.
    pub fn crop_to_content(image: &DynamicImage, threshold: u8) -> DynamicImage {
        let bounds = match Self::find_content_bounds(image, threshold) {
            Some(b) => b,
            None => return image.clone(),
        };

        let (x, y, w, h) = bounds;

        // Add 1% padding around the content
        let pad_x = (w as f32 * 0.01).ceil() as u32;
        let pad_y = (h as f32 * 0.01).ceil() as u32;

        let crop_x = x.saturating_sub(pad_x);
        let crop_y = y.saturating_sub(pad_y);
        let crop_w = (w + pad_x * 2).min(image.width().saturating_sub(crop_x));
        let crop_h = (h + pad_y * 2).min(image.height().saturating_sub(crop_y));

        image.crop_imm(crop_x, crop_y, crop_w, crop_h)
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::yomitoku::TextDirection;
    use std::time::Duration;

    fn make_ocr_result(blocks: Vec<TextBlock>) -> OcrResult {
        OcrResult {
            input_path: "test.png".into(),
            text_blocks: blocks,
            confidence: 0.9,
            processing_time: Duration::from_millis(100),
            text_direction: TextDirection::Vertical,
        }
    }

    #[test]
    fn test_classify_text_only_page() {
        let img = DynamicImage::new_rgb8(1000, 1500);
        let blocks = vec![TextBlock {
            text: "テスト".into(),
            bbox: (50, 50, 900, 1350),
            confidence: 0.95,
            direction: TextDirection::Vertical,
            font_size: Some(12.0),
        }];
        let ocr = make_ocr_result(blocks);
        let opts = FigureDetectOptions::default();

        let result = FigureDetector::classify_page(&img, &ocr, 1, &opts);
        assert!(matches!(result, PageClassification::TextOnly));
    }

    #[test]
    fn test_classify_cover_page() {
        let img = DynamicImage::new_rgb8(1000, 1500);
        let ocr = make_ocr_result(vec![]);
        let opts = FigureDetectOptions::default();

        let result = FigureDetector::classify_page(&img, &ocr, 0, &opts);
        assert!(matches!(result, PageClassification::Cover));
    }

    #[test]
    fn test_classify_fullpage_image() {
        let img = DynamicImage::new_rgb8(1000, 1500);
        let ocr = make_ocr_result(vec![]);
        let opts = FigureDetectOptions::default();

        // Page index > 0, no text -> FullPageImage
        let result = FigureDetector::classify_page(&img, &ocr, 5, &opts);
        assert!(matches!(result, PageClassification::FullPageImage));
    }

    #[test]
    fn test_regions_overlap() {
        let a = FigureRegion {
            bbox: (0, 0, 100, 100),
            area: 10000,
            region_type: RegionType::Figure,
        };
        let b = FigureRegion {
            bbox: (50, 50, 100, 100),
            area: 10000,
            region_type: RegionType::Figure,
        };
        assert!(FigureDetector::regions_overlap(&a, &b));

        let c = FigureRegion {
            bbox: (200, 200, 100, 100),
            area: 10000,
            region_type: RegionType::Figure,
        };
        assert!(!FigureDetector::regions_overlap(&a, &c));
    }

    #[test]
    fn test_figure_detect_options_default() {
        let opts = FigureDetectOptions::default();
        assert!((opts.min_area_fraction - 0.02).abs() < f32::EPSILON);
        assert!((opts.max_aspect_ratio - 10.0).abs() < f32::EPSILON);
    }

    #[test]
    fn test_crop_figure() {
        let img = DynamicImage::new_rgb8(500, 500);
        let region = FigureRegion {
            bbox: (100, 100, 200, 200),
            area: 40000,
            region_type: RegionType::Figure,
        };
        let cropped = FigureDetector::crop_figure(&img, &region);
        assert!(cropped.width() > 0);
        assert!(cropped.height() > 0);
    }

    #[test]
    fn test_find_content_bounds_blank_image() {
        use image::{Rgb, RgbImage};
        // All-white image should return None
        let raw = RgbImage::from_pixel(100, 100, Rgb([255, 255, 255]));
        let img = DynamicImage::ImageRgb8(raw);
        assert!(FigureDetector::find_content_bounds(&img, 240).is_none());
    }

    #[test]
    fn test_find_content_bounds_with_content() {
        use image::{Rgb, RgbImage};
        let mut raw = RgbImage::from_pixel(200, 200, Rgb([255, 255, 255]));
        // Draw a black rectangle at (50,60) to (120,140)
        for y in 60..=140 {
            for x in 50..=120 {
                raw.put_pixel(x, y, Rgb([0, 0, 0]));
            }
        }
        let img = DynamicImage::ImageRgb8(raw);
        let bounds = FigureDetector::find_content_bounds(&img, 240).unwrap();
        assert_eq!(bounds, (50, 60, 71, 81));
    }

    #[test]
    fn test_crop_to_content() {
        use image::{Rgb, RgbImage};
        let mut raw = RgbImage::from_pixel(500, 500, Rgb([255, 255, 255]));
        // Draw content in center area (100,100) to (399,399)
        for y in 100..400 {
            for x in 100..400 {
                raw.put_pixel(x, y, Rgb([50, 50, 50]));
            }
        }
        let img = DynamicImage::ImageRgb8(raw);
        let cropped = FigureDetector::crop_to_content(&img, 240);
        // Cropped image should be roughly 300x300 + small padding, much smaller than 500x500
        assert!(cropped.width() < 320);
        assert!(cropped.height() < 320);
        assert!(cropped.width() >= 300);
        assert!(cropped.height() >= 300);
    }
}
