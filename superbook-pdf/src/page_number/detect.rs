//! Page Number Detection Implementation
//!
//! Tesseract-based page number detection with 4-stage fallback matching.

use super::types::{
    DetectedPageNumber, MatchStage, OffsetCorrection, PageNumberAnalysis, PageNumberCandidate,
    PageNumberError, PageNumberMatch, PageNumberOptions, PageNumberPosition, PageNumberRect,
    Rectangle, Result,
};
use image::GenericImageView;
use rayon::prelude::*;
use std::collections::HashSet;
use std::path::{Path, PathBuf};

// ============================================================
// Fallback Matching Constants (Phase 2.1)
// ============================================================

/// Minimum Jaro-Winkler similarity for Stage 2 matching
pub const MIN_SIMILARITY_THRESHOLD: f64 = 0.7;

/// Margin percentage for expanding search region (3% as per spec)
pub const SEARCH_REGION_MARGIN_PERCENT: f32 = 3.0;

/// Default reference point for distance calculation (bottom center)
#[allow(dead_code)]
pub const DEFAULT_REFERENCE_Y_RATIO: f32 = 0.95;

// ============================================================
// 4-Stage Fallback Matching (Phase 2.1)
// ============================================================

/// Find page number with 4-stage fallback matching
///
/// # Stages
/// 1. **ExactMatch**: Exact number match + within region + minimum distance
/// 2. **SimilarityMatch**: Maximum similarity (Jaro-Winkler) + within region
/// 3. **OcrSuccessMatch**: OCR success region + minimum distance
/// 4. **FallbackMatch**: All detected regions + minimum distance
///
/// # Arguments
/// * `candidates` - OCR detection candidates
/// * `expected_number` - The page number we're looking for
/// * `search_region` - The region to prioritize (with 3% margin expansion)
///
/// # Returns
/// The best matching candidate, or None if no candidates available
pub fn find_page_number_with_fallback(
    candidates: &[PageNumberCandidate],
    expected_number: u32,
    search_region: &Rectangle,
) -> Option<PageNumberMatch> {
    if candidates.is_empty() {
        return None;
    }

    let expected_str = expected_number.to_string();
    let (ref_x, ref_y) = search_region.center();

    // Expand search region by 3% margin
    let expanded_region = search_region.expand(SEARCH_REGION_MARGIN_PERCENT);

    // Stage 1: Exact match + within region + minimum distance
    if let Some(m) = stage1_exact_match(candidates, expected_number, &expanded_region, ref_x, ref_y)
    {
        return Some(m);
    }

    // Stage 2: Maximum similarity (Jaro-Winkler) + within region
    if let Some(m) =
        stage2_similarity_match(candidates, &expected_str, &expanded_region, ref_x, ref_y)
    {
        return Some(m);
    }

    // Stage 3: OCR success region + minimum distance
    if let Some(m) = stage3_ocr_success_match(candidates, expected_number, ref_x, ref_y) {
        return Some(m);
    }

    // Stage 4: All detected regions + minimum distance (fallback)
    stage4_fallback_match(candidates, expected_number, ref_x, ref_y)
}

/// Stage 1: Exact match + within region + minimum distance
fn stage1_exact_match(
    candidates: &[PageNumberCandidate],
    expected_number: u32,
    region: &Rectangle,
    ref_x: i32,
    ref_y: i32,
) -> Option<PageNumberMatch> {
    let mut best: Option<(PageNumberCandidate, f64)> = None;

    for candidate in candidates {
        // Check for exact number match
        if candidate.number == Some(expected_number) {
            let (cx, cy) = candidate.bbox.center();
            // Check if within expanded region
            if region.contains(cx, cy) {
                let distance = candidate.distance_to(ref_x, ref_y);
                if best.as_ref().is_none_or(|(_, d)| distance < *d) {
                    best = Some((candidate.clone(), distance));
                }
            }
        }
    }

    best.map(|(candidate, distance)| {
        PageNumberMatch::new(
            candidate,
            MatchStage::ExactMatch,
            1.0, // Perfect score for exact match
            distance,
            expected_number,
        )
    })
}

/// Stage 2: Maximum similarity (Jaro-Winkler) + within region
fn stage2_similarity_match(
    candidates: &[PageNumberCandidate],
    expected_str: &str,
    region: &Rectangle,
    ref_x: i32,
    ref_y: i32,
) -> Option<PageNumberMatch> {
    use strsim::jaro_winkler;

    let mut best: Option<(PageNumberCandidate, f64, f64)> = None; // (candidate, similarity, distance)

    for candidate in candidates {
        let (cx, cy) = candidate.bbox.center();
        // Check if within expanded region
        if region.contains(cx, cy) && !candidate.text.trim().is_empty() {
            let similarity = jaro_winkler(expected_str, candidate.text.trim());
            if similarity >= MIN_SIMILARITY_THRESHOLD {
                let distance = candidate.distance_to(ref_x, ref_y);
                // Prefer higher similarity, then closer distance
                let is_better = match &best {
                    None => true,
                    Some((_, best_sim, best_dist)) => {
                        similarity > *best_sim
                            || (similarity == *best_sim && distance < *best_dist)
                    }
                };
                if is_better {
                    best = Some((candidate.clone(), similarity, distance));
                }
            }
        }
    }

    best.map(|(candidate, similarity, distance)| {
        PageNumberMatch::new(
            candidate,
            MatchStage::SimilarityMatch,
            similarity,
            distance,
            expected_str.parse().unwrap_or(0),
        )
    })
}

/// Stage 3: OCR success region + minimum distance
fn stage3_ocr_success_match(
    candidates: &[PageNumberCandidate],
    expected_number: u32,
    ref_x: i32,
    ref_y: i32,
) -> Option<PageNumberMatch> {
    let mut best: Option<(PageNumberCandidate, f64, f32)> = None; // (candidate, distance, confidence)

    for candidate in candidates {
        // Only consider OCR success candidates (text was successfully detected)
        if candidate.ocr_success {
            let distance = candidate.distance_to(ref_x, ref_y);
            if best.as_ref().is_none_or(|(_, d, _)| distance < *d) {
                best = Some((candidate.clone(), distance, candidate.confidence));
            }
        }
    }

    best.map(|(candidate, distance, confidence)| {
        PageNumberMatch::new(
            candidate,
            MatchStage::OcrSuccessMatch,
            confidence as f64,
            distance,
            expected_number,
        )
    })
}

/// Stage 4: All detected regions + minimum distance (fallback)
fn stage4_fallback_match(
    candidates: &[PageNumberCandidate],
    expected_number: u32,
    ref_x: i32,
    ref_y: i32,
) -> Option<PageNumberMatch> {
    let mut best: Option<(PageNumberCandidate, f64)> = None;

    for candidate in candidates {
        let distance = candidate.distance_to(ref_x, ref_y);
        if best.as_ref().is_none_or(|(_, d)| distance < *d) {
            best = Some((candidate.clone(), distance));
        }
    }

    best.map(|(candidate, distance)| {
        PageNumberMatch::new(
            candidate,
            MatchStage::FallbackMatch,
            0.0, // No score for fallback
            distance,
            expected_number,
        )
    })
}

/// Batch process multiple pages with fallback matching
pub fn find_page_numbers_batch(
    page_candidates: &[Vec<PageNumberCandidate>],
    start_page_number: u32,
    search_regions: &[Rectangle],
) -> Vec<Option<PageNumberMatch>> {
    page_candidates
        .par_iter()
        .enumerate()
        .map(|(i, candidates)| {
            let expected_number = start_page_number + i as u32;
            let region = search_regions.get(i).cloned().unwrap_or_else(|| {
                // Default search region if not specified
                Rectangle::new(0, 0, 1000, 100)
            });
            find_page_number_with_fallback(candidates, expected_number, &region)
        })
        .collect()
}

/// Statistics for fallback matching results
#[derive(Debug, Clone, Default)]
pub struct FallbackMatchStats {
    pub total: usize,
    pub stage1_exact: usize,
    pub stage2_similarity: usize,
    pub stage3_ocr_success: usize,
    pub stage4_fallback: usize,
    pub not_found: usize,
}

impl FallbackMatchStats {
    /// Create stats from match results
    pub fn from_matches(matches: &[Option<PageNumberMatch>]) -> Self {
        let mut stats = Self {
            total: matches.len(),
            ..Default::default()
        };

        for m in matches.iter().flatten() {
            match m.stage {
                MatchStage::ExactMatch => stats.stage1_exact += 1,
                MatchStage::SimilarityMatch => stats.stage2_similarity += 1,
                MatchStage::OcrSuccessMatch => stats.stage3_ocr_success += 1,
                MatchStage::FallbackMatch => stats.stage4_fallback += 1,
            }
        }
        stats.not_found = matches.iter().filter(|m| m.is_none()).count();

        stats
    }

    /// Get success rate (Stage 1 + Stage 2)
    pub fn high_confidence_rate(&self) -> f64 {
        if self.total == 0 {
            return 0.0;
        }
        (self.stage1_exact + self.stage2_similarity) as f64 / self.total as f64
    }

    /// Get overall detection rate
    pub fn detection_rate(&self) -> f64 {
        if self.total == 0 {
            return 0.0;
        }
        (self.total - self.not_found) as f64 / self.total as f64
    }
}

/// Tesseract-based page number detector
pub struct TesseractPageDetector;

impl TesseractPageDetector {
    /// Detect page number from single image
    pub fn detect_single(
        image_path: &Path,
        page_index: usize,
        options: &PageNumberOptions,
    ) -> Result<DetectedPageNumber> {
        if !image_path.exists() {
            return Err(PageNumberError::ImageNotFound(image_path.to_path_buf()));
        }

        let img = image::open(image_path)
            .map_err(|_| PageNumberError::ImageNotFound(image_path.to_path_buf()))?;

        let (width, height) = img.dimensions();

        // Determine search region based on position hint
        let (search_y, search_height) = match options.position_hint {
            Some(PageNumberPosition::TopCenter | PageNumberPosition::TopOutside) => {
                let h = (height as f32 * options.search_region_percent / 100.0) as u32;
                (0, h)
            }
            _ => {
                let h = (height as f32 * options.search_region_percent / 100.0) as u32;
                (height.saturating_sub(h), h)
            }
        };

        // Crop search region
        let search_region = img.crop_imm(0, search_y, width, search_height);

        // For now, use simple image analysis instead of Tesseract
        // In a full implementation, this would call tesseract OCR
        let (number, raw_text, confidence) =
            Self::analyze_region_for_numbers(&search_region, options);

        Ok(DetectedPageNumber {
            page_index,
            number: if confidence >= options.min_confidence {
                number
            } else {
                None
            },
            position: PageNumberRect {
                x: 0,
                y: search_y,
                width,
                height: search_height,
            },
            confidence: confidence / 100.0,
            raw_text,
        })
    }

    /// Analyze image region for numbers using Tesseract OCR
    fn analyze_region_for_numbers(
        img: &image::DynamicImage,
        _options: &PageNumberOptions,
    ) -> (Option<i32>, String, f32) {
        // Create temp file for the cropped region
        let temp_dir = std::env::temp_dir();
        let temp_path = temp_dir.join(format!("page_num_region_{}.png", std::process::id()));

        // Save the region to temp file
        if img.save(&temp_path).is_err() {
            return (None, String::new(), 0.0);
        }

        // Call Tesseract with digits-only configuration
        // tesseract input.png stdout --psm 7 -c tessedit_char_whitelist=0123456789
        let output = std::process::Command::new("tesseract")
            .arg(&temp_path)
            .arg("stdout")
            .arg("--psm")
            .arg("7") // Single line mode
            .arg("-c")
            .arg("tessedit_char_whitelist=0123456789")
            .output();

        // Cleanup temp file
        let _ = std::fs::remove_file(&temp_path);

        match output {
            Ok(result) if result.status.success() => {
                let raw_text = String::from_utf8_lossy(&result.stdout).trim().to_string();

                // Extract digits from the text
                let digits: String = raw_text.chars().filter(|c| c.is_ascii_digit()).collect();

                if digits.is_empty() {
                    return (None, raw_text, 0.0);
                }

                // Parse as number
                match digits.parse::<i32>() {
                    Ok(num) if num > 0 && num < 10000 => {
                        // Valid page number range (1-9999)
                        // Confidence based on text cleanliness
                        let confidence = if digits == raw_text.trim() {
                            95.0 // Clean digits only
                        } else {
                            70.0 // Had to filter some characters
                        };
                        (Some(num), raw_text, confidence)
                    }
                    _ => (None, raw_text, 30.0),
                }
            }
            _ => {
                // Tesseract not available or failed
                (None, String::new(), 0.0)
            }
        }
    }

    /// Analyze multiple images
    pub fn analyze_batch(
        images: &[PathBuf],
        options: &PageNumberOptions,
    ) -> Result<PageNumberAnalysis> {
        let detections: Vec<DetectedPageNumber> = images
            .par_iter()
            .enumerate()
            .map(|(i, path)| Self::detect_single(path, i, options))
            .collect::<Result<Vec<_>>>()?;

        // Analyze pattern
        let (position_pattern, odd_offset, even_offset) = Self::analyze_pattern(&detections);

        // Find missing and duplicate pages
        let detected_numbers: Vec<i32> = detections.iter().filter_map(|d| d.number).collect();
        let missing_pages = Self::find_missing_pages(&detected_numbers);
        let duplicate_pages = Self::find_duplicate_pages(&detected_numbers);

        let overall_confidence = if detections.is_empty() {
            0.0
        } else {
            detections.iter().map(|d| d.confidence).sum::<f32>() / detections.len() as f32
        };

        Ok(PageNumberAnalysis {
            detections,
            position_pattern,
            odd_page_offset_x: odd_offset,
            even_page_offset_x: even_offset,
            overall_confidence,
            missing_pages,
            duplicate_pages,
        })
    }

    /// Analyze position pattern from detections
    fn analyze_pattern(detections: &[DetectedPageNumber]) -> (PageNumberPosition, i32, i32) {
        // Analyze X positions of detected page numbers
        let mut odd_positions: Vec<i32> = Vec::new();
        let mut even_positions: Vec<i32> = Vec::new();

        for detection in detections {
            if let Some(num) = detection.number {
                let center_x = detection.position.x as i32 + detection.position.width as i32 / 2;
                if num % 2 == 1 {
                    odd_positions.push(center_x);
                } else {
                    even_positions.push(center_x);
                }
            }
        }

        let odd_avg = if odd_positions.is_empty() {
            0
        } else {
            odd_positions.iter().sum::<i32>() / odd_positions.len() as i32
        };

        let even_avg = if even_positions.is_empty() {
            0
        } else {
            even_positions.iter().sum::<i32>() / even_positions.len() as i32
        };

        // Determine pattern based on position difference
        let position_pattern = if (odd_avg - even_avg).abs() < 50 {
            PageNumberPosition::BottomCenter
        } else if odd_avg > even_avg {
            PageNumberPosition::BottomOutside
        } else {
            PageNumberPosition::BottomInside
        };

        (position_pattern, odd_avg, even_avg)
    }

    /// Find missing page numbers
    fn find_missing_pages(numbers: &[i32]) -> Vec<usize> {
        if numbers.is_empty() {
            return vec![];
        }

        let min = *numbers.iter().min().unwrap();
        let max = *numbers.iter().max().unwrap();
        let set: HashSet<_> = numbers.iter().collect();

        (min..=max)
            .filter(|n| !set.contains(n))
            .map(|n| (n - min) as usize)
            .collect()
    }

    /// Find duplicate page numbers
    fn find_duplicate_pages(numbers: &[i32]) -> Vec<i32> {
        let mut seen = HashSet::new();
        numbers
            .iter()
            .filter(|n| !seen.insert(*n))
            .cloned()
            .collect()
    }

    /// Calculate offset correction
    pub fn calculate_offset(
        analysis: &PageNumberAnalysis,
        _image_width: u32,
    ) -> Result<OffsetCorrection> {
        let page_offsets: Vec<(usize, i32)> = analysis
            .detections
            .iter()
            .enumerate()
            .filter_map(|(i, d)| {
                d.number.map(|num| {
                    let offset = if num % 2 == 1 {
                        analysis.odd_page_offset_x
                    } else {
                        analysis.even_page_offset_x
                    };
                    (i, offset)
                })
            })
            .collect();

        let unified_offset = if !page_offsets.is_empty() {
            page_offsets.iter().map(|(_, o)| *o).sum::<i32>() / page_offsets.len() as i32
        } else {
            0
        };

        Ok(OffsetCorrection {
            page_offsets,
            unified_offset,
        })
    }

    /// Validate page order
    pub fn validate_order(analysis: &PageNumberAnalysis) -> Result<bool> {
        let numbers: Vec<i32> = analysis
            .detections
            .iter()
            .filter_map(|d| d.number)
            .collect();

        if numbers.len() < 2 {
            return Ok(true);
        }

        // Check if numbers are in ascending order
        for i in 1..numbers.len() {
            if numbers[i] <= numbers[i - 1] {
                return Ok(false);
            }
        }

        Ok(true)
    }

    /// Parse Roman numeral to integer
    pub fn parse_roman_numeral(text: &str) -> Option<i32> {
        let text = text.to_lowercase().trim().to_string();
        let roman_map = [
            ("m", 1000),
            ("cm", 900),
            ("d", 500),
            ("cd", 400),
            ("c", 100),
            ("xc", 90),
            ("l", 50),
            ("xl", 40),
            ("x", 10),
            ("ix", 9),
            ("v", 5),
            ("iv", 4),
            ("i", 1),
        ];

        let mut result = 0;
        let mut remaining = text.as_str();

        for (numeral, value) in &roman_map {
            while remaining.starts_with(numeral) {
                result += value;
                remaining = &remaining[numeral.len()..];
            }
        }

        if remaining.is_empty() && result > 0 {
            Some(result)
        } else {
            None
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_detect_nonexistent_file() {
        let options = PageNumberOptions::default();
        let result =
            TesseractPageDetector::detect_single(Path::new("/nonexistent/image.png"), 0, &options);
        assert!(matches!(result, Err(PageNumberError::ImageNotFound(_))));
    }

    #[test]
    fn test_roman_numeral_parsing() {
        assert_eq!(TesseractPageDetector::parse_roman_numeral("I"), Some(1));
        assert_eq!(TesseractPageDetector::parse_roman_numeral("IV"), Some(4));
        assert_eq!(TesseractPageDetector::parse_roman_numeral("V"), Some(5));
        assert_eq!(TesseractPageDetector::parse_roman_numeral("IX"), Some(9));
        assert_eq!(TesseractPageDetector::parse_roman_numeral("X"), Some(10));
        assert_eq!(TesseractPageDetector::parse_roman_numeral("XL"), Some(40));
        assert_eq!(TesseractPageDetector::parse_roman_numeral("L"), Some(50));
        assert_eq!(TesseractPageDetector::parse_roman_numeral("XC"), Some(90));
        assert_eq!(TesseractPageDetector::parse_roman_numeral("C"), Some(100));
        assert_eq!(TesseractPageDetector::parse_roman_numeral("CD"), Some(400));
        assert_eq!(TesseractPageDetector::parse_roman_numeral("D"), Some(500));
        assert_eq!(TesseractPageDetector::parse_roman_numeral("CM"), Some(900));
        assert_eq!(TesseractPageDetector::parse_roman_numeral("M"), Some(1000));
        assert_eq!(
            TesseractPageDetector::parse_roman_numeral("MCMXCIX"),
            Some(1999)
        );
        assert_eq!(
            TesseractPageDetector::parse_roman_numeral("MMXXIII"),
            Some(2023)
        );
    }

    #[test]
    fn test_roman_numeral_invalid() {
        assert_eq!(TesseractPageDetector::parse_roman_numeral(""), None);
        assert_eq!(TesseractPageDetector::parse_roman_numeral("ABC"), None);
        assert_eq!(TesseractPageDetector::parse_roman_numeral("123"), None);
    }

    #[test]
    fn test_find_missing_pages() {
        let numbers = vec![1, 2, 4, 5, 7];
        let missing = TesseractPageDetector::find_missing_pages(&numbers);
        assert!(missing.contains(&2)); // 3 is missing (index 2 from min)
        assert!(missing.contains(&5)); // 6 is missing (index 5 from min)
    }

    #[test]
    fn test_find_duplicate_pages() {
        let numbers = vec![1, 2, 2, 3, 4, 4, 4];
        let duplicates = TesseractPageDetector::find_duplicate_pages(&numbers);
        assert!(duplicates.contains(&2));
        assert!(duplicates.contains(&4));
    }

    #[test]
    fn test_analyze_empty_batch() {
        let images: Vec<PathBuf> = vec![];
        let options = PageNumberOptions::default();
        let result = TesseractPageDetector::analyze_batch(&images, &options).unwrap();
        assert!(result.detections.is_empty());
        assert_eq!(result.overall_confidence, 0.0);
    }

    #[test]
    fn test_validate_order_ascending() {
        let analysis = PageNumberAnalysis {
            detections: vec![
                DetectedPageNumber {
                    page_index: 0,
                    number: Some(1),
                    position: PageNumberRect {
                        x: 0,
                        y: 0,
                        width: 100,
                        height: 50,
                    },
                    confidence: 0.9,
                    raw_text: "1".to_string(),
                },
                DetectedPageNumber {
                    page_index: 1,
                    number: Some(2),
                    position: PageNumberRect {
                        x: 0,
                        y: 0,
                        width: 100,
                        height: 50,
                    },
                    confidence: 0.9,
                    raw_text: "2".to_string(),
                },
                DetectedPageNumber {
                    page_index: 2,
                    number: Some(3),
                    position: PageNumberRect {
                        x: 0,
                        y: 0,
                        width: 100,
                        height: 50,
                    },
                    confidence: 0.9,
                    raw_text: "3".to_string(),
                },
            ],
            position_pattern: PageNumberPosition::BottomCenter,
            odd_page_offset_x: 0,
            even_page_offset_x: 0,
            overall_confidence: 0.9,
            missing_pages: vec![],
            duplicate_pages: vec![],
        };

        assert!(TesseractPageDetector::validate_order(&analysis).unwrap());
    }

    #[test]
    fn test_validate_order_not_ascending() {
        let analysis = PageNumberAnalysis {
            detections: vec![
                DetectedPageNumber {
                    page_index: 0,
                    number: Some(3),
                    position: PageNumberRect {
                        x: 0,
                        y: 0,
                        width: 100,
                        height: 50,
                    },
                    confidence: 0.9,
                    raw_text: "3".to_string(),
                },
                DetectedPageNumber {
                    page_index: 1,
                    number: Some(1),
                    position: PageNumberRect {
                        x: 0,
                        y: 0,
                        width: 100,
                        height: 50,
                    },
                    confidence: 0.9,
                    raw_text: "1".to_string(),
                },
            ],
            position_pattern: PageNumberPosition::BottomCenter,
            odd_page_offset_x: 0,
            even_page_offset_x: 0,
            overall_confidence: 0.9,
            missing_pages: vec![],
            duplicate_pages: vec![],
        };

        assert!(!TesseractPageDetector::validate_order(&analysis).unwrap());
    }

    // ============================================================
    // 4-Stage Fallback Matching Tests (Phase 2.1)
    // ============================================================

    #[test]
    fn test_fallback_empty_candidates() {
        let candidates: Vec<PageNumberCandidate> = vec![];
        let region = Rectangle::new(0, 900, 1000, 100);
        let result = find_page_number_with_fallback(&candidates, 42, &region);
        assert!(result.is_none());
    }

    #[test]
    fn test_fallback_stage1_exact_match() {
        let candidates = vec![
            PageNumberCandidate::new("42".to_string(), Rectangle::new(500, 950, 50, 30), 0.95),
            PageNumberCandidate::new("41".to_string(), Rectangle::new(100, 950, 50, 30), 0.90),
        ];
        let region = Rectangle::new(0, 900, 1000, 100);
        let result = find_page_number_with_fallback(&candidates, 42, &region);

        assert!(result.is_some());
        let m = result.unwrap();
        assert_eq!(m.stage, MatchStage::ExactMatch);
        assert_eq!(m.expected_number, 42);
        assert_eq!(m.candidate.number, Some(42));
    }

    #[test]
    fn test_fallback_stage1_prefers_closer() {
        // Two exact matches, should prefer the closer one
        let candidates = vec![
            PageNumberCandidate::new("42".to_string(), Rectangle::new(100, 950, 50, 30), 0.90),
            PageNumberCandidate::new("42".to_string(), Rectangle::new(500, 950, 50, 30), 0.95),
        ];
        let region = Rectangle::new(400, 900, 200, 100); // Center at (500, 950)
        let result = find_page_number_with_fallback(&candidates, 42, &region);

        assert!(result.is_some());
        let m = result.unwrap();
        assert_eq!(m.stage, MatchStage::ExactMatch);
        // The one at (500, 950) should be closer to region center
        assert!(m.distance < 100.0);
    }

    #[test]
    fn test_fallback_stage2_similarity_match() {
        // No exact match, but similar text (123 is similar to 124)
        let candidates = vec![
            PageNumberCandidate::new("124".to_string(), Rectangle::new(500, 950, 50, 30), 0.80),
            PageNumberCandidate::new("abc".to_string(), Rectangle::new(100, 950, 50, 30), 0.90),
        ];
        let region = Rectangle::new(400, 900, 200, 100);
        let result = find_page_number_with_fallback(&candidates, 123, &region);

        assert!(result.is_some());
        let m = result.unwrap();
        assert_eq!(m.stage, MatchStage::SimilarityMatch);
        // "124" is similar to "123" (Jaro-Winkler ~0.93)
        assert!(m.score >= MIN_SIMILARITY_THRESHOLD);
    }

    #[test]
    fn test_fallback_stage3_ocr_success() {
        // No exact or similar match, but OCR success
        let candidates = vec![
            PageNumberCandidate::new("xyz".to_string(), Rectangle::new(500, 950, 50, 30), 0.80),
        ];
        let region = Rectangle::new(0, 0, 100, 100); // Far from candidate
        let result = find_page_number_with_fallback(&candidates, 42, &region);

        assert!(result.is_some());
        let m = result.unwrap();
        assert_eq!(m.stage, MatchStage::OcrSuccessMatch);
    }

    #[test]
    fn test_fallback_stage4_fallback() {
        // Only empty text candidates (no OCR success)
        let mut candidate = PageNumberCandidate::new(
            "".to_string(),
            Rectangle::new(500, 950, 50, 30),
            0.10,
        );
        candidate.ocr_success = false; // Force OCR failure
        let candidates = vec![candidate];
        let region = Rectangle::new(0, 0, 100, 100);
        let result = find_page_number_with_fallback(&candidates, 42, &region);

        assert!(result.is_some());
        let m = result.unwrap();
        assert_eq!(m.stage, MatchStage::FallbackMatch);
    }

    #[test]
    fn test_fallback_stage_priority() {
        // Multiple candidates that would match different stages
        let candidates = vec![
            PageNumberCandidate::new("abc".to_string(), Rectangle::new(100, 950, 50, 30), 0.80),
            PageNumberCandidate::new("42".to_string(), Rectangle::new(500, 950, 50, 30), 0.95),
        ];
        let region = Rectangle::new(0, 900, 1000, 100);
        let result = find_page_number_with_fallback(&candidates, 42, &region);

        // Should match Stage 1 (exact) even though Stage 3 would also match
        assert!(result.is_some());
        assert_eq!(result.unwrap().stage, MatchStage::ExactMatch);
    }

    #[test]
    fn test_fallback_batch_processing() {
        let page1_candidates = vec![PageNumberCandidate::new(
            "1".to_string(),
            Rectangle::new(500, 950, 50, 30),
            0.95,
        )];
        let page2_candidates = vec![PageNumberCandidate::new(
            "2".to_string(),
            Rectangle::new(500, 950, 50, 30),
            0.95,
        )];
        let page3_candidates = vec![]; // No candidates

        let all_candidates = vec![page1_candidates, page2_candidates, page3_candidates];
        let regions = vec![
            Rectangle::new(0, 900, 1000, 100),
            Rectangle::new(0, 900, 1000, 100),
            Rectangle::new(0, 900, 1000, 100),
        ];

        let results = find_page_numbers_batch(&all_candidates, 1, &regions);

        assert_eq!(results.len(), 3);
        assert!(results[0].is_some());
        assert!(results[1].is_some());
        assert!(results[2].is_none());
    }

    #[test]
    fn test_fallback_stats() {
        let page1 = PageNumberCandidate::new("1".to_string(), Rectangle::new(500, 950, 50, 30), 0.95);
        let page2 = PageNumberCandidate::new("2X".to_string(), Rectangle::new(500, 950, 50, 30), 0.80);
        let page3 = PageNumberCandidate::new("abc".to_string(), Rectangle::new(500, 950, 50, 30), 0.70);

        let all_candidates = vec![vec![page1], vec![page2], vec![page3], vec![]];
        let regions = vec![
            Rectangle::new(0, 900, 1000, 100),
            Rectangle::new(0, 900, 1000, 100),
            Rectangle::new(0, 900, 1000, 100),
            Rectangle::new(0, 900, 1000, 100),
        ];

        let results = find_page_numbers_batch(&all_candidates, 1, &regions);
        let stats = FallbackMatchStats::from_matches(&results);

        assert_eq!(stats.total, 4);
        assert_eq!(stats.stage1_exact, 1); // "1" matched exactly
        assert_eq!(stats.not_found, 1);    // Empty candidates
        assert!(stats.detection_rate() > 0.5);
    }

    #[test]
    fn test_fallback_region_expansion() {
        // Candidate just outside the strict region but within 3% margin
        let candidate = PageNumberCandidate::new(
            "42".to_string(),
            Rectangle::new(503, 953, 50, 30), // Slightly outside
            0.95,
        );
        let candidates = vec![candidate];
        // Region center at (500, 950), with expansion should include (503, 953)
        let region = Rectangle::new(400, 900, 200, 100);
        let result = find_page_number_with_fallback(&candidates, 42, &region);

        assert!(result.is_some());
        assert_eq!(result.unwrap().stage, MatchStage::ExactMatch);
    }
}
