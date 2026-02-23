//! Markdown generation module
//!
//! Generates Markdown files from OCR results and detected figures.
//! Supports page-by-page generation with final merge.

use std::fmt::Write as FmtWrite;
use std::path::{Path, PathBuf};
use thiserror::Error;

use crate::figure_detect::{FigureRegion, PageClassification};
use crate::yomitoku::{OcrResult, TextBlock, TextDirection};

/// Minimum OCR confidence to include a text block (0.0-1.0)
const MIN_CONFIDENCE: f32 = 0.3;

/// Font size ratio above median to classify as heading (e.g., 1.4 = 40% larger)
const HEADING_FONT_SIZE_RATIO: f32 = 1.4;

/// Font size ratio for sub-headings (between heading and body text)
const SUBHEADING_FONT_SIZE_RATIO: f32 = 1.2;

/// Minimum Y-gap ratio (relative to median line height) to insert a paragraph break
const PARAGRAPH_GAP_RATIO: f32 = 1.5;

/// Maximum ratio of digits+symbols to total chars before a block is considered noise
const MAX_NOISE_RATIO: f32 = 0.6;

/// Minimum text length to apply noise filtering (short blocks like "1900" are kept)
const NOISE_FILTER_MIN_LEN: usize = 8;

/// Error type for Markdown generation
#[derive(Debug, Error)]
pub enum MarkdownGenError {
    #[error("IO error: {0}")]
    IoError(#[from] std::io::Error),

    #[error("Output directory not writable: {0}")]
    OutputNotWritable(PathBuf),

    #[error("Generation error: {0}")]
    GenerationError(String),
}

/// A content element within a page
#[derive(Debug, Clone)]
pub enum ContentElement {
    /// Text content with direction info
    Text {
        content: String,
        direction: TextDirection,
    },
    /// Figure image reference
    Figure {
        image_path: PathBuf,
        caption: Option<String>,
    },
    /// Full-page image (cover or illustration)
    FullPageImage { image_path: PathBuf },
    /// Page break separator
    PageBreak,
}

/// Processed content for a single page
#[derive(Debug, Clone)]
pub struct PageContent {
    pub page_index: usize,
    pub elements: Vec<ContentElement>,
}

/// Markdown generator
pub struct MarkdownGenerator {
    output_dir: PathBuf,
    images_dir: PathBuf,
    pages_dir: PathBuf,
}

impl MarkdownGenerator {
    /// Create a new generator with output directories
    pub fn new(output_dir: &Path) -> Result<Self, MarkdownGenError> {
        let images_dir = output_dir.join("images");
        let pages_dir = output_dir.join("pages");

        std::fs::create_dir_all(&images_dir)?;
        std::fs::create_dir_all(&pages_dir)?;

        Ok(Self {
            output_dir: output_dir.to_path_buf(),
            images_dir,
            pages_dir,
        })
    }

    /// Generate Markdown for a single page
    pub fn generate_page_markdown(
        &self,
        page_content: &PageContent,
    ) -> Result<String, MarkdownGenError> {
        let mut md = String::new();

        for element in &page_content.elements {
            match element {
                ContentElement::Text { content, .. } => {
                    // Write text content, preserving paragraph structure
                    for paragraph in content.split("\n\n") {
                        let trimmed = paragraph.trim();
                        if !trimmed.is_empty() {
                            writeln!(md, "{}", trimmed).ok();
                            writeln!(md).ok();
                        }
                    }
                }
                ContentElement::Figure {
                    image_path,
                    caption,
                } => {
                    let rel_path = self.relative_image_path(image_path);
                    match caption {
                        Some(cap) => writeln!(md, "![{}]({})", cap, rel_path).ok(),
                        None => writeln!(md, "![図]({})", rel_path).ok(),
                    };
                    writeln!(md).ok();
                }
                ContentElement::FullPageImage { image_path } => {
                    let rel_path = self.relative_image_path(image_path);
                    writeln!(md, "![]({})", rel_path).ok();
                    writeln!(md).ok();
                }
                ContentElement::PageBreak => {
                    writeln!(md, "---").ok();
                    writeln!(md).ok();
                }
            }
        }

        // Post-process: normalize spacing
        Ok(Self::normalize_markdown(&md))
    }

    /// Save page markdown to pages directory
    pub fn save_page_markdown(
        &self,
        page_index: usize,
        content: &str,
    ) -> Result<PathBuf, MarkdownGenError> {
        let page_path = self
            .pages_dir
            .join(format!("page_{:03}.md", page_index + 1));
        std::fs::write(&page_path, content)?;
        Ok(page_path)
    }

    /// Build PageContent from OCR result and page classification
    pub fn build_page_content(
        &self,
        page_index: usize,
        ocr_result: &OcrResult,
        classification: &PageClassification,
        figure_images: &[(FigureRegion, PathBuf)],
    ) -> PageContent {
        let mut elements = Vec::new();

        match classification {
            PageClassification::Cover => {
                // Look for a saved cover image
                let cover_path = self
                    .images_dir
                    .join(format!("cover_{:03}.png", page_index + 1));
                elements.push(ContentElement::FullPageImage {
                    image_path: cover_path,
                });
            }
            PageClassification::FullPageImage => {
                let img_path = self
                    .images_dir
                    .join(format!("page_{:03}_full.png", page_index + 1));
                elements.push(ContentElement::FullPageImage {
                    image_path: img_path,
                });
            }
            PageClassification::TextOnly => {
                let text = Self::sort_and_join_text_blocks(
                    &ocr_result.text_blocks,
                    &ocr_result.text_direction,
                );
                if !text.is_empty() {
                    elements.push(ContentElement::Text {
                        content: text,
                        direction: ocr_result.text_direction,
                    });
                }
            }
            PageClassification::Mixed { figures } => {
                // Filter and sort text blocks
                let filtered = Self::filter_low_confidence(&ocr_result.text_blocks);
                let sorted_blocks = Self::sort_text_blocks(&filtered, &ocr_result.text_direction);

                // Calculate metrics for heading/paragraph detection
                let median_size = Self::median_font_size(&sorted_blocks);
                let median_height = Self::median_line_height(&sorted_blocks);

                // Interleave text and figures based on vertical position
                let mut figure_idx = 0;
                let mut current_blocks: Vec<&TextBlock> = Vec::new();

                for block in &sorted_blocks {
                    // Check if any figure should be inserted before this text block
                    while figure_idx < figures.len() {
                        let fig = &figures[figure_idx];
                        let fig_y = fig.bbox.1;
                        let block_y = block.bbox.1;

                        if fig_y < block_y {
                            // Flush accumulated text blocks as structured text
                            if !current_blocks.is_empty() {
                                let text = Self::format_block_group(
                                    &current_blocks,
                                    median_size,
                                    median_height,
                                );
                                if !text.is_empty() {
                                    elements.push(ContentElement::Text {
                                        content: text,
                                        direction: ocr_result.text_direction,
                                    });
                                }
                                current_blocks.clear();
                            }

                            // Insert figure
                            if let Some((_, fig_path)) = figure_images.get(figure_idx) {
                                elements.push(ContentElement::Figure {
                                    image_path: fig_path.clone(),
                                    caption: None,
                                });
                            }
                            figure_idx += 1;
                        } else {
                            break;
                        }
                    }

                    current_blocks.push(block);
                }

                // Flush remaining text blocks
                if !current_blocks.is_empty() {
                    let text =
                        Self::format_block_group(&current_blocks, median_size, median_height);
                    if !text.is_empty() {
                        elements.push(ContentElement::Text {
                            content: text,
                            direction: ocr_result.text_direction,
                        });
                    }
                }

                // Flush remaining figures
                while figure_idx < figures.len() {
                    if let Some((_, fig_path)) = figure_images.get(figure_idx) {
                        elements.push(ContentElement::Figure {
                            image_path: fig_path.clone(),
                            caption: None,
                        });
                    }
                    figure_idx += 1;
                }
            }
        }

        // Add page break
        elements.push(ContentElement::PageBreak);

        PageContent {
            page_index,
            elements,
        }
    }

    /// Merge all page markdowns into a single output file
    pub fn merge_pages(
        &self,
        title: &str,
        total_pages: usize,
    ) -> Result<PathBuf, MarkdownGenError> {
        let output_path = self
            .output_dir
            .join(format!("{}.md", sanitize_filename(title)));
        let mut merged = String::new();

        // Title header
        writeln!(merged, "# {}", title).ok();
        writeln!(merged).ok();

        // Concatenate page files in order
        for i in 0..total_pages {
            let page_path = self.pages_dir.join(format!("page_{:03}.md", i + 1));
            if page_path.exists() {
                let content = std::fs::read_to_string(&page_path)?;
                merged.push_str(&content);
            }
        }

        std::fs::write(&output_path, &merged)?;
        Ok(output_path)
    }

    /// Get images directory path
    pub fn images_dir(&self) -> &Path {
        &self.images_dir
    }

    /// Get pages directory path
    pub fn pages_dir(&self) -> &Path {
        &self.pages_dir
    }

    /// Sort text blocks by reading order and join into structured text
    /// with heading detection, confidence filtering, and paragraph breaks
    fn sort_and_join_text_blocks(blocks: &[TextBlock], direction: &TextDirection) -> String {
        Self::build_structured_text(blocks, direction)
    }

    /// Sort text blocks by reading order
    /// Vertical (Japanese): right-to-left columns, then top-to-bottom within each column
    /// Horizontal: top-to-bottom rows, then left-to-right within each row
    fn sort_text_blocks(blocks: &[TextBlock], direction: &TextDirection) -> Vec<TextBlock> {
        let mut sorted = blocks.to_vec();

        match direction {
            TextDirection::Vertical => {
                // Right-to-left, then top-to-bottom
                sorted.sort_by(|a, b| {
                    // Compare X in reverse (right to left)
                    let ax = a.bbox.0;
                    let bx = b.bbox.0;
                    let x_cmp = bx.cmp(&ax);
                    if x_cmp != std::cmp::Ordering::Equal {
                        return x_cmp;
                    }
                    // Then top to bottom
                    a.bbox.1.cmp(&b.bbox.1)
                });
            }
            TextDirection::Horizontal | TextDirection::Mixed => {
                // Top-to-bottom, then left-to-right
                sorted.sort_by(|a, b| {
                    let ay = a.bbox.1;
                    let by = b.bbox.1;
                    let y_cmp = ay.cmp(&by);
                    if y_cmp != std::cmp::Ordering::Equal {
                        return y_cmp;
                    }
                    a.bbox.0.cmp(&b.bbox.0)
                });
            }
        }

        sorted
    }

    /// Format a group of text blocks with heading detection and paragraph breaks
    fn format_block_group(
        blocks: &[&TextBlock],
        median_size: Option<f32>,
        median_height: f32,
    ) -> String {
        let mut result = String::new();

        for (i, block) in blocks.iter().enumerate() {
            // Check for paragraph gap
            if i > 0 {
                if Self::is_paragraph_gap(blocks[i - 1], block, median_height) {
                    // Paragraph break: double newline
                    if !result.ends_with('\n') {
                        result.push('\n');
                    }
                    result.push('\n');
                } else {
                    result.push('\n');
                }
            }

            // Determine if this is a heading
            let heading = median_size.and_then(|ms| Self::heading_level(block, ms));

            match heading {
                Some(2) => {
                    if !result.is_empty() && !result.ends_with("\n\n") {
                        if !result.ends_with('\n') {
                            result.push('\n');
                        }
                        result.push('\n');
                    }
                    write!(result, "## {}", block.text.trim()).ok();
                }
                Some(3) => {
                    if !result.is_empty() && !result.ends_with("\n\n") {
                        if !result.ends_with('\n') {
                            result.push('\n');
                        }
                        result.push('\n');
                    }
                    write!(result, "### {}", block.text.trim()).ok();
                }
                _ => {
                    result.push_str(block.text.trim());
                }
            }
        }

        result
    }

    /// Filter out low-confidence and noisy OCR blocks, clean remaining text
    fn filter_low_confidence(blocks: &[TextBlock]) -> Vec<TextBlock> {
        blocks
            .iter()
            .filter(|b| b.confidence >= MIN_CONFIDENCE)
            .filter(|b| !Self::is_noise_text(&b.text))
            .map(|b| {
                let mut cleaned = b.clone();
                cleaned.text = Self::clean_block_text(&b.text);
                cleaned
            })
            .filter(|b| !b.text.trim().is_empty())
            .collect()
    }

    /// Clean trailing OCR noise from text (stray furigana, punctuation artifacts)
    fn clean_block_text(text: &str) -> String {
        let trimmed = text.trim();
        // Remove trailing stray characters: single non-CJK-text chars after a space
        // e.g., "はじめに "" → "はじめに", "をめぐって m。" → "をめぐって"
        let cleaned = Self::strip_trailing_noise(trimmed);
        cleaned.to_string()
    }

    /// Strip trailing noise: if the last space-separated token is <= 3 chars
    /// and mostly punctuation/symbols, remove it. Also strip trailing repeated punctuation.
    fn strip_trailing_noise(text: &str) -> &str {
        let mut result = text;

        // Pass 1: strip trailing space + short noise token
        if let Some(last_space) = result.rfind(' ') {
            let tail = &result[last_space + 1..];
            let tail_chars: usize = tail.chars().count();
            if (1..=3).contains(&tail_chars) {
                let meaningful = tail
                    .chars()
                    .filter(|c| {
                        (c.is_alphanumeric() && !c.is_ascii_digit())
                            || *c == '。'
                            || *c == '、'
                            || *c == '」'
                            || *c == '「'
                    })
                    .count();
                if meaningful <= 1 {
                    let is_real_word = tail.chars().count() == 1
                        && tail.chars().next().is_some_and(|c| {
                            matches!(
                                c,
                                'の' | 'は'
                                    | 'が'
                                    | 'を'
                                    | 'に'
                                    | 'で'
                                    | 'と'
                                    | 'も'
                                    | 'か'
                                    | 'な'
                                    | 'だ'
                                    | 'る'
                                    | 'た'
                                    | 'て'
                                    | 'へ'
                                    | 'や'
                            )
                        });
                    if !is_real_word {
                        result = result[..last_space].trim_end();
                    }
                }
            }
        }

        // Pass 2: strip trailing repeated punctuation like "。。", "··"
        let trimmed_end = result
            .trim_end_matches(['。', '·', '・', '.', ',']);
        // Only strip if we removed 2+ punctuation chars (to avoid stripping single valid 。)
        if result.len() - trimmed_end.len() >= 2 * '。'.len_utf8() {
            result = trimmed_end.trim_end();
        }

        result
    }

    /// Heuristic: detect noise text (garbled OCR from barcodes, numbers, etc.)
    fn is_noise_text(text: &str) -> bool {
        let trimmed = text.trim();
        if trimmed.len() < NOISE_FILTER_MIN_LEN {
            return false; // Short strings like page numbers are OK
        }

        let total_chars: usize = trimmed.chars().count();
        if total_chars == 0 {
            return true;
        }

        // Check 1: High ratio of digits + ASCII symbols
        let noise_chars: usize = trimmed
            .chars()
            .filter(|c| {
                c.is_ascii_digit()
                    || (*c != '。'
                        && *c != '、'
                        && *c != '」'
                        && *c != '「'
                        && *c != '）'
                        && *c != '（'
                        && *c != '『'
                        && *c != '』'
                        && c.is_ascii_punctuation())
            })
            .count();

        let noise_ratio = noise_chars as f32 / total_chars as f32;
        if noise_ratio > MAX_NOISE_RATIO {
            return true;
        }

        // Check 2: Long runs of repeated digits (e.g., "444443", "777777")
        let has_digit_run = Self::has_long_digit_run(trimmed, 5);
        if has_digit_run && noise_ratio > 0.3 {
            return true;
        }

        false
    }

    /// Check if text contains a run of 'min_len' or more consecutive digits
    fn has_long_digit_run(text: &str, min_len: usize) -> bool {
        let mut run = 0usize;
        for c in text.chars() {
            if c.is_ascii_digit() {
                run += 1;
                if run >= min_len {
                    return true;
                }
            } else {
                run = 0;
            }
        }
        false
    }

    /// Calculate the median font size from text blocks (returns None if no font sizes available)
    fn median_font_size(blocks: &[TextBlock]) -> Option<f32> {
        let mut sizes: Vec<f32> = blocks
            .iter()
            .filter_map(|b| b.font_size)
            .filter(|&s| s > 0.0)
            .collect();
        if sizes.is_empty() {
            return None;
        }
        sizes.sort_by(|a, b| a.partial_cmp(b).unwrap_or(std::cmp::Ordering::Equal));
        let mid = sizes.len() / 2;
        if sizes.len() % 2 == 0 {
            Some((sizes[mid - 1] + sizes[mid]) / 2.0)
        } else {
            Some(sizes[mid])
        }
    }

    /// Determine the heading level for a text block based on its font size relative to the median
    /// Returns None for body text, Some(2) for main headings, Some(3) for sub-headings
    fn heading_level(block: &TextBlock, median_size: f32) -> Option<u8> {
        if let Some(font_size) = block.font_size {
            if font_size >= median_size * HEADING_FONT_SIZE_RATIO {
                Some(2) // ## Heading
            } else if font_size >= median_size * SUBHEADING_FONT_SIZE_RATIO {
                Some(3) // ### Sub-heading
            } else {
                None
            }
        } else {
            None
        }
    }

    /// Calculate the median line height from sorted blocks for paragraph gap detection
    fn median_line_height(sorted_blocks: &[TextBlock]) -> f32 {
        let mut heights: Vec<f32> = sorted_blocks
            .iter()
            .map(|b| b.bbox.3 as f32) // bbox.3 = height
            .filter(|&h| h > 0.0)
            .collect();
        if heights.is_empty() {
            return 20.0; // fallback
        }
        heights.sort_by(|a, b| a.partial_cmp(b).unwrap_or(std::cmp::Ordering::Equal));
        let mid = heights.len() / 2;
        if heights.len() % 2 == 0 {
            (heights[mid - 1] + heights[mid]) / 2.0
        } else {
            heights[mid]
        }
    }

    /// Check if there should be a paragraph break between two blocks
    fn is_paragraph_gap(prev: &TextBlock, curr: &TextBlock, median_height: f32) -> bool {
        let prev_bottom = prev.bbox.1 + prev.bbox.3; // y + height
        let curr_top = curr.bbox.1;
        if curr_top > prev_bottom {
            let gap = (curr_top - prev_bottom) as f32;
            gap > median_height * PARAGRAPH_GAP_RATIO
        } else {
            false
        }
    }

    /// Build structured text from sorted, filtered blocks with heading detection and paragraph breaks
    fn build_structured_text(blocks: &[TextBlock], direction: &TextDirection) -> String {
        if blocks.is_empty() {
            return String::new();
        }

        // Filter low-confidence blocks
        let filtered = Self::filter_low_confidence(blocks);
        if filtered.is_empty() {
            return String::new();
        }

        // Sort by reading order
        let sorted = Self::sort_text_blocks(&filtered, direction);

        // Calculate metrics for heading detection and paragraph gaps
        let median_size = Self::median_font_size(&sorted);
        let median_height = Self::median_line_height(&sorted);

        let mut result = String::new();

        for (i, block) in sorted.iter().enumerate() {
            // Check for paragraph gap
            if i > 0 {
                if Self::is_paragraph_gap(&sorted[i - 1], block, median_height) {
                    // Paragraph break: double newline
                    if !result.ends_with('\n') {
                        result.push('\n');
                    }
                    result.push('\n');
                } else {
                    result.push('\n');
                }
            }

            // Determine if this is a heading
            let heading = median_size.and_then(|ms| Self::heading_level(block, ms));

            match heading {
                Some(2) => {
                    // Ensure blank line before heading
                    if !result.is_empty() && !result.ends_with("\n\n") {
                        if !result.ends_with('\n') {
                            result.push('\n');
                        }
                        result.push('\n');
                    }
                    write!(result, "## {}", block.text.trim()).ok();
                }
                Some(3) => {
                    if !result.is_empty() && !result.ends_with("\n\n") {
                        if !result.ends_with('\n') {
                            result.push('\n');
                        }
                        result.push('\n');
                    }
                    write!(result, "### {}", block.text.trim()).ok();
                }
                _ => {
                    result.push_str(block.text.trim());
                }
            }
        }

        result
    }

    /// Post-process markdown: normalize spacing, remove duplicates, collapse blank lines,
    /// remove stray page numbers, convert +heading to ## heading, skip furigana lines
    fn normalize_markdown(md: &str) -> String {
        let mut result = String::with_capacity(md.len());
        let mut blank_count = 0u32;
        let mut prev_line: Option<String> = None;
        let mut is_first_content_line = true;

        for line in md.lines() {
            let trimmed = line.trim();
            if trimmed.is_empty() {
                blank_count += 1;
                if blank_count <= 1 {
                    result.push('\n');
                }
                continue;
            }

            // === Filter: Remove stray page numbers at the start of a page ===
            // Lines that are purely digits (e.g., "028", "034") at the beginning
            if is_first_content_line && Self::is_page_number_line(trimmed) {
                continue; // Skip page number
            }

            // === Filter: Skip furigana/ruby lines ===
            // Furigana lines are short, all-hiragana, with spaces (e.g., "さとみ ゆうすけ")
            if Self::is_furigana_line(trimmed) {
                continue;
            }

            // === Transform: Convert +heading to ## heading ===
            let line_to_write = if let Some(heading_text) = trimmed.strip_prefix('+') {
                let heading_text = heading_text.trim();
                if !heading_text.is_empty() && heading_text.len() > 1 {
                    // Ensure blank line before heading
                    if !result.is_empty() && !result.ends_with("\n\n") {
                        if !result.ends_with('\n') {
                            result.push('\n');
                        }
                        result.push('\n');
                    }
                    format!("## {}", heading_text)
                } else {
                    trimmed.to_string()
                }
            } else {
                trimmed.to_string()
            };
            let trimmed = line_to_write.trim();

            // === Filter: Skip duplicate or near-duplicate consecutive lines ===
            if let Some(ref prev) = prev_line {
                if prev == trimmed {
                    continue;
                }
                // Near-duplicate: one is prefix of the other (OCR noise at end)
                if prev.len() >= 5
                    && trimmed.len() >= 5
                    && (prev.starts_with(trimmed) || trimmed.starts_with(prev.as_str()))
                {
                    continue;
                }
            }

            // === Filter: Skip isolated short noise ===
            // but keep markdown syntax like "---", "## heading", "![img]"
            let char_count = trimmed.chars().count();
            if char_count <= 3
                && !trimmed.starts_with('#')
                && !trimmed.starts_with('-')
                && !trimmed.starts_with('!')
            {
                // Keep meaningful short strings: pure numbers, ASCII words
                let is_meaningful = trimmed.chars().all(|c| c.is_ascii_digit())
                    || (trimmed.len() >= 2 && trimmed.chars().all(|c| c.is_ascii_alphanumeric()));
                if !is_meaningful {
                    continue; // Skip stray chars like "め", "ゆ", "パ", "。", "m。", """
                }
            }

            // === Filter: Remove isolated all-uppercase ASCII noise (OCR artifacts) ===
            // e.g., "OIL", "FTSC" — short all-caps strings that aren't part of text
            if char_count <= 5
                && trimmed.chars().all(|c| c.is_ascii_uppercase())
                && !trimmed.starts_with('#')
            {
                continue;
            }

            blank_count = 0;
            is_first_content_line = false;
            if !result.is_empty() && !result.ends_with('\n') {
                result.push('\n');
            }
            result.push_str(trimmed);
            prev_line = Some(trimmed.to_string());
        }

        if !result.is_empty() && !result.ends_with('\n') {
            result.push('\n');
        }

        result
    }

    /// Check if a line is a stray page number (1-4 digits, possibly with leading zeros)
    fn is_page_number_line(text: &str) -> bool {
        let trimmed = text.trim();
        let char_count = trimmed.chars().count();
        // 1-4 digit string, e.g., "028", "1", "300"
        (1..=4).contains(&char_count) && trimmed.chars().all(|c| c.is_ascii_digit())
    }

    /// Check if a line is likely furigana (ruby text above kanji)
    /// Furigana lines are typically: short, all hiragana/katakana, with spaces
    fn is_furigana_line(text: &str) -> bool {
        let trimmed = text.trim();
        let char_count = trimmed.chars().count();
        // Must be short (furigana for a name is usually < 15 chars)
        if !(2..=15).contains(&char_count) {
            return false;
        }
        // Must contain a space (furigana for multiple words)
        if !trimmed.contains(' ') {
            return false;
        }
        // All characters must be hiragana, katakana, or space
        trimmed.chars().all(|c| {
            c == ' '
                || c == '\u{3000}' // full-width space
                || ('\u{3040}'..='\u{309F}').contains(&c) // hiragana
                || ('\u{30A0}'..='\u{30FF}').contains(&c) // katakana
        })
    }

    /// Get image path relative to the output directory for markdown references
    fn relative_image_path(&self, abs_path: &Path) -> String {
        if let Ok(rel) = abs_path.strip_prefix(&self.output_dir) {
            rel.to_string_lossy().to_string()
        } else {
            abs_path.to_string_lossy().to_string()
        }
    }
}

/// Sanitize a string for use as a filename
fn sanitize_filename(name: &str) -> String {
    name.chars()
        .map(|c| match c {
            '/' | '\\' | ':' | '*' | '?' | '"' | '<' | '>' | '|' => '_',
            _ => c,
        })
        .collect()
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_sanitize_filename() {
        assert_eq!(sanitize_filename("hello/world"), "hello_world");
        assert_eq!(sanitize_filename("test:file"), "test_file");
        assert_eq!(sanitize_filename("normal_file"), "normal_file");
        assert_eq!(sanitize_filename("日本語テスト"), "日本語テスト");
    }

    #[test]
    fn test_sort_text_blocks_vertical() {
        let blocks = vec![
            TextBlock {
                text: "左列".into(),
                bbox: (100, 0, 50, 500),
                confidence: 0.9,
                direction: TextDirection::Vertical,
                font_size: Some(12.0),
            },
            TextBlock {
                text: "右列".into(),
                bbox: (500, 0, 50, 500),
                confidence: 0.9,
                direction: TextDirection::Vertical,
                font_size: Some(12.0),
            },
        ];

        let sorted = MarkdownGenerator::sort_text_blocks(&blocks, &TextDirection::Vertical);
        assert_eq!(sorted[0].text, "右列"); // Right column first
        assert_eq!(sorted[1].text, "左列"); // Left column second
    }

    #[test]
    fn test_sort_text_blocks_horizontal() {
        let blocks = vec![
            TextBlock {
                text: "下行".into(),
                bbox: (0, 500, 200, 50),
                confidence: 0.9,
                direction: TextDirection::Horizontal,
                font_size: Some(12.0),
            },
            TextBlock {
                text: "上行".into(),
                bbox: (0, 100, 200, 50),
                confidence: 0.9,
                direction: TextDirection::Horizontal,
                font_size: Some(12.0),
            },
        ];

        let sorted = MarkdownGenerator::sort_text_blocks(&blocks, &TextDirection::Horizontal);
        assert_eq!(sorted[0].text, "上行");
        assert_eq!(sorted[1].text, "下行");
    }

    #[test]
    fn test_generate_page_markdown_text() {
        let tmpdir = tempfile::tempdir().unwrap();
        let gen = MarkdownGenerator::new(tmpdir.path()).unwrap();

        let content = PageContent {
            page_index: 0,
            elements: vec![
                ContentElement::Text {
                    content: "テスト段落です。".into(),
                    direction: TextDirection::Vertical,
                },
                ContentElement::PageBreak,
            ],
        };

        let md = gen.generate_page_markdown(&content).unwrap();
        assert!(md.contains("テスト段落です。"));
        assert!(md.contains("---"));
    }

    #[test]
    fn test_generate_page_markdown_figure() {
        let tmpdir = tempfile::tempdir().unwrap();
        let gen = MarkdownGenerator::new(tmpdir.path()).unwrap();
        let img_path = tmpdir.path().join("images").join("fig.png");

        let content = PageContent {
            page_index: 0,
            elements: vec![
                ContentElement::Figure {
                    image_path: img_path,
                    caption: Some("テスト図".into()),
                },
                ContentElement::PageBreak,
            ],
        };

        let md = gen.generate_page_markdown(&content).unwrap();
        assert!(md.contains("![テスト図]"));
        assert!(md.contains("images/fig.png"));
    }

    #[test]
    fn test_save_and_merge_pages() {
        let tmpdir = tempfile::tempdir().unwrap();
        let gen = MarkdownGenerator::new(tmpdir.path()).unwrap();

        gen.save_page_markdown(0, "Page 1 content\n\n---\n\n")
            .unwrap();
        gen.save_page_markdown(1, "Page 2 content\n\n---\n\n")
            .unwrap();

        let merged_path = gen.merge_pages("テストブック", 2).unwrap();
        assert!(merged_path.exists());

        let content = std::fs::read_to_string(&merged_path).unwrap();
        assert!(content.contains("# テストブック"));
        assert!(content.contains("Page 1 content"));
        assert!(content.contains("Page 2 content"));
    }

    // ============ Additional Tests (Issue #41+ quality assurance) ============

    #[test]
    fn test_build_page_content_cover() {
        use crate::figure_detect::PageClassification;
        use std::time::Duration;

        let tmpdir = tempfile::tempdir().unwrap();
        let gen = MarkdownGenerator::new(tmpdir.path()).unwrap();

        let ocr = OcrResult {
            input_path: "test.png".into(),
            text_blocks: vec![],
            confidence: 0.0,
            processing_time: Duration::from_millis(10),
            text_direction: TextDirection::Vertical,
        };

        let content = gen.build_page_content(0, &ocr, &PageClassification::Cover, &[]);
        assert!(!content.elements.is_empty());
        assert!(matches!(
            content.elements[0],
            ContentElement::FullPageImage { .. }
        ));
        // Last element should be PageBreak
        assert!(matches!(
            content.elements.last().unwrap(),
            ContentElement::PageBreak
        ));
    }

    #[test]
    fn test_build_page_content_fullpage_image() {
        use crate::figure_detect::PageClassification;
        use std::time::Duration;

        let tmpdir = tempfile::tempdir().unwrap();
        let gen = MarkdownGenerator::new(tmpdir.path()).unwrap();

        let ocr = OcrResult {
            input_path: "test.png".into(),
            text_blocks: vec![],
            confidence: 0.0,
            processing_time: Duration::from_millis(10),
            text_direction: TextDirection::Horizontal,
        };

        let content = gen.build_page_content(5, &ocr, &PageClassification::FullPageImage, &[]);
        assert!(matches!(
            content.elements[0],
            ContentElement::FullPageImage { .. }
        ));
        if let ContentElement::FullPageImage { image_path } = &content.elements[0] {
            assert!(image_path.to_string_lossy().contains("page_006_full.png"));
        }
    }

    #[test]
    fn test_build_page_content_text_only() {
        use crate::figure_detect::PageClassification;
        use std::time::Duration;

        let tmpdir = tempfile::tempdir().unwrap();
        let gen = MarkdownGenerator::new(tmpdir.path()).unwrap();

        let blocks = vec![
            TextBlock {
                text: "最初の段落".into(),
                bbox: (0, 0, 200, 50),
                confidence: 0.9,
                direction: TextDirection::Horizontal,
                font_size: Some(12.0),
            },
            TextBlock {
                text: "二番目の段落".into(),
                bbox: (0, 100, 200, 50),
                confidence: 0.9,
                direction: TextDirection::Horizontal,
                font_size: Some(12.0),
            },
        ];

        let ocr = OcrResult {
            input_path: "test.png".into(),
            text_blocks: blocks,
            confidence: 0.9,
            processing_time: Duration::from_millis(10),
            text_direction: TextDirection::Horizontal,
        };

        let content = gen.build_page_content(0, &ocr, &PageClassification::TextOnly, &[]);
        // Should have Text + PageBreak
        assert!(content.elements.len() >= 2);
        if let ContentElement::Text { content: text, .. } = &content.elements[0] {
            assert!(text.contains("最初の段落"));
            assert!(text.contains("二番目の段落"));
        } else {
            panic!("Expected Text element");
        }
    }

    #[test]
    fn test_build_page_content_mixed_with_figures() {
        use crate::figure_detect::{FigureRegion, PageClassification, RegionType};
        use std::time::Duration;

        let tmpdir = tempfile::tempdir().unwrap();
        let gen = MarkdownGenerator::new(tmpdir.path()).unwrap();

        let blocks = vec![
            TextBlock {
                text: "文章の前".into(),
                bbox: (0, 0, 200, 50),
                confidence: 0.9,
                direction: TextDirection::Horizontal,
                font_size: Some(12.0),
            },
            TextBlock {
                text: "文章の後".into(),
                bbox: (0, 400, 200, 50),
                confidence: 0.9,
                direction: TextDirection::Horizontal,
                font_size: Some(12.0),
            },
        ];

        let ocr = OcrResult {
            input_path: "test.png".into(),
            text_blocks: blocks,
            confidence: 0.9,
            processing_time: Duration::from_millis(10),
            text_direction: TextDirection::Horizontal,
        };

        let fig = FigureRegion {
            bbox: (0, 200, 200, 100),
            area: 20000,
            region_type: RegionType::Figure,
        };
        let fig_path = tmpdir.path().join("images").join("figure_001.png");
        let figures = vec![fig.clone()];
        let figure_images = vec![(fig, fig_path)];

        let content = gen.build_page_content(
            0,
            &ocr,
            &PageClassification::Mixed { figures },
            &figure_images,
        );

        // Should have: Text (before figure), Figure, Text (after figure), PageBreak
        // Verify the ORDER: Text -> Figure -> Text -> PageBreak
        assert!(
            content.elements.len() >= 4,
            "Expected at least 4 elements (Text, Figure, Text, PageBreak), got {}",
            content.elements.len()
        );

        // Element 0: Text containing "文章の前" (text before figure at y=0)
        match &content.elements[0] {
            ContentElement::Text { content: text, .. } => {
                assert!(
                    text.contains("文章の前"),
                    "First element should contain '文章の前', got '{}'",
                    text
                );
            }
            other => panic!("Expected Text as first element, got {:?}", other),
        }

        // Element 1: Figure
        assert!(
            matches!(content.elements[1], ContentElement::Figure { .. }),
            "Second element should be Figure, got {:?}",
            content.elements[1]
        );

        // Element 2: Text containing "文章の後" (text after figure at y=400)
        match &content.elements[2] {
            ContentElement::Text { content: text, .. } => {
                assert!(
                    text.contains("文章の後"),
                    "Third element should contain '文章の後', got '{}'",
                    text
                );
            }
            other => panic!("Expected Text as third element, got {:?}", other),
        }

        // Element 3: PageBreak
        assert!(
            matches!(content.elements.last().unwrap(), ContentElement::PageBreak),
            "Last element should be PageBreak"
        );
    }

    #[test]
    fn test_relative_image_path_absolute() {
        let tmpdir = tempfile::tempdir().unwrap();
        let gen = MarkdownGenerator::new(tmpdir.path()).unwrap();

        let abs_path = tmpdir.path().join("images").join("test.png");
        let rel = gen.relative_image_path(&abs_path);
        assert_eq!(rel, "images/test.png");
    }

    #[test]
    fn test_relative_image_path_outside_output() {
        let tmpdir = tempfile::tempdir().unwrap();
        let gen = MarkdownGenerator::new(tmpdir.path()).unwrap();

        // Path outside the output directory — should return as-is
        let outside_path = PathBuf::from("/some/other/path/image.png");
        let rel = gen.relative_image_path(&outside_path);
        assert_eq!(rel, "/some/other/path/image.png");
    }

    #[test]
    fn test_sanitize_filename_japanese() {
        assert_eq!(sanitize_filename("日本語のタイトル"), "日本語のタイトル");
        assert_eq!(sanitize_filename("テスト/ブック"), "テスト_ブック");
    }

    #[test]
    fn test_sanitize_filename_all_special_chars() {
        let input = r#"a/b\c:d*e?f"g<h>i|j"#;
        let result = sanitize_filename(input);
        assert!(!result.contains('/'));
        assert!(!result.contains('\\'));
        assert!(!result.contains(':'));
        assert!(!result.contains('*'));
        assert!(!result.contains('?'));
        assert!(!result.contains('"'));
        assert!(!result.contains('<'));
        assert!(!result.contains('>'));
        assert!(!result.contains('|'));
        assert_eq!(result, "a_b_c_d_e_f_g_h_i_j");
    }

    #[test]
    fn test_sort_text_blocks_same_position() {
        // Blocks at the same Y should be sorted by X (for horizontal)
        let blocks = vec![
            TextBlock {
                text: "右".into(),
                bbox: (300, 100, 50, 50),
                confidence: 0.9,
                direction: TextDirection::Horizontal,
                font_size: Some(12.0),
            },
            TextBlock {
                text: "左".into(),
                bbox: (100, 100, 50, 50),
                confidence: 0.9,
                direction: TextDirection::Horizontal,
                font_size: Some(12.0),
            },
        ];

        let sorted = MarkdownGenerator::sort_text_blocks(&blocks, &TextDirection::Horizontal);
        assert_eq!(sorted[0].text, "左");
        assert_eq!(sorted[1].text, "右");
    }

    #[test]
    fn test_sort_text_blocks_mixed_direction() {
        // Mixed direction should use horizontal sorting (top-to-bottom, left-to-right)
        let blocks = vec![
            TextBlock {
                text: "下".into(),
                bbox: (0, 500, 100, 50),
                confidence: 0.9,
                direction: TextDirection::Mixed,
                font_size: Some(12.0),
            },
            TextBlock {
                text: "上".into(),
                bbox: (0, 100, 100, 50),
                confidence: 0.9,
                direction: TextDirection::Mixed,
                font_size: Some(12.0),
            },
        ];

        let sorted = MarkdownGenerator::sort_text_blocks(&blocks, &TextDirection::Mixed);
        assert_eq!(sorted[0].text, "上");
        assert_eq!(sorted[1].text, "下");
    }

    #[test]
    fn test_generate_page_markdown_full_page_image() {
        let tmpdir = tempfile::tempdir().unwrap();
        let gen = MarkdownGenerator::new(tmpdir.path()).unwrap();
        let img_path = tmpdir.path().join("images").join("page_001_full.png");

        let content = PageContent {
            page_index: 0,
            elements: vec![
                ContentElement::FullPageImage {
                    image_path: img_path,
                },
                ContentElement::PageBreak,
            ],
        };

        let md = gen.generate_page_markdown(&content).unwrap();
        assert!(md.contains("![](images/page_001_full.png)"));
        assert!(md.contains("---"));
    }

    #[test]
    fn test_sanitize_filename_empty_string() {
        // Empty string should produce empty result — not panic
        let result = sanitize_filename("");
        assert_eq!(result, "");
    }

    #[test]
    fn test_merge_pages_zero_pages() {
        let tmpdir = tempfile::tempdir().unwrap();
        let gen = MarkdownGenerator::new(tmpdir.path()).unwrap();

        // Merging 0 pages should produce a file with only the title header
        let merged_path = gen.merge_pages("空の本", 0).unwrap();
        assert!(merged_path.exists());

        let content = std::fs::read_to_string(&merged_path).unwrap();
        assert!(content.contains("# 空の本"));
        // Should have only the title and a newline, no page content
        let lines: Vec<&str> = content.lines().collect();
        assert!(
            lines.len() <= 2,
            "0-page merge should have only title, got {} lines",
            lines.len()
        );
    }

    #[test]
    fn test_page_content_no_elements() {
        let tmpdir = tempfile::tempdir().unwrap();
        let gen = MarkdownGenerator::new(tmpdir.path()).unwrap();

        let content = PageContent {
            page_index: 0,
            elements: vec![],
        };

        let md = gen.generate_page_markdown(&content).unwrap();
        // Empty elements should produce empty markdown (no panic, no garbage)
        assert!(
            md.is_empty(),
            "PageContent with no elements should produce empty markdown, got '{}'",
            md
        );
    }

    #[test]
    fn test_build_page_content_very_large_page_index() {
        use crate::figure_detect::PageClassification;
        use std::time::Duration;

        let tmpdir = tempfile::tempdir().unwrap();
        let gen = MarkdownGenerator::new(tmpdir.path()).unwrap();

        let ocr = OcrResult {
            input_path: "test.png".into(),
            text_blocks: vec![],
            confidence: 0.0,
            processing_time: Duration::from_millis(10),
            text_direction: TextDirection::Vertical,
        };

        // Very large page index should not panic or overflow filename formatting
        let page_idx = 999_999;
        let content =
            gen.build_page_content(page_idx, &ocr, &PageClassification::FullPageImage, &[]);
        assert_eq!(content.page_index, page_idx);

        // Verify it can be saved and the file is created
        let md = gen.generate_page_markdown(&content).unwrap();
        let saved_path = gen.save_page_markdown(page_idx, &md).unwrap();
        // page_{:03} formatting with 999999+1=1000000 produces "page_1000000.md"
        assert!(
            saved_path.to_string_lossy().contains("page_"),
            "Save path should contain page_ prefix"
        );
    }

    #[test]
    fn test_merge_pages_missing_page() {
        let tmpdir = tempfile::tempdir().unwrap();
        let gen = MarkdownGenerator::new(tmpdir.path()).unwrap();

        // Save only page 0 and 2, skip page 1
        gen.save_page_markdown(0, "Page 1 content\n\n").unwrap();
        gen.save_page_markdown(2, "Page 3 content\n\n").unwrap();

        let merged_path = gen.merge_pages("テスト", 3).unwrap();
        let content = std::fs::read_to_string(&merged_path).unwrap();
        assert!(content.contains("Page 1 content"));
        assert!(content.contains("Page 3 content"));
        // Page 2 was skipped — no error
    }

    #[test]
    fn test_sanitize_filename_empty() {
        let result = sanitize_filename("");
        assert_eq!(result, "", "Empty string should remain empty");
    }

    #[test]
    fn test_sanitize_filename_all_special() {
        let result = sanitize_filename("/:*?\"<>|\\");
        assert!(
            !result.contains('/'),
            "Should not contain forward slash: {}",
            result
        );
        assert!(
            !result.contains('\\'),
            "Should not contain backslash: {}",
            result
        );
        assert!(
            !result.contains(':'),
            "Should not contain colon: {}",
            result
        );
        // All chars should be replaced with '_'
        assert_eq!(result, "_________");
    }

    #[test]
    fn test_build_page_content_empty_ocr() {
        let tmpdir = tempfile::tempdir().unwrap();
        let gen = MarkdownGenerator::new(tmpdir.path()).unwrap();

        let ocr = OcrResult {
            input_path: "empty.png".into(),
            text_blocks: vec![],
            confidence: 0.0,
            processing_time: std::time::Duration::from_secs(0),
            text_direction: TextDirection::Vertical,
        };

        let content = gen.build_page_content(
            0,
            &ocr,
            &crate::figure_detect::PageClassification::TextOnly,
            &[],
        );

        // Empty OCR should still produce a valid PageContent
        // The last element should be PageBreak
        assert!(
            !content.elements.is_empty(),
            "Even empty OCR should produce at least a page break"
        );
        assert!(
            matches!(content.elements.last().unwrap(), ContentElement::PageBreak),
            "Last element should be PageBreak"
        );
    }

    #[test]
    fn test_sort_text_blocks_empty() {
        let sorted = MarkdownGenerator::sort_text_blocks(&[], &TextDirection::Horizontal);
        assert!(sorted.is_empty(), "Empty input should produce empty output");
    }

    #[test]
    fn test_sort_text_blocks_single_block() {
        let blocks = vec![TextBlock {
            text: "唯一".into(),
            bbox: (10, 20, 100, 50),
            confidence: 0.95,
            direction: TextDirection::Horizontal,
            font_size: Some(12.0),
        }];
        let sorted = MarkdownGenerator::sort_text_blocks(&blocks, &TextDirection::Horizontal);
        assert_eq!(sorted.len(), 1);
        assert_eq!(sorted[0].text, "唯一");
    }

    // ============ Markdown Quality Improvement Tests ============

    #[test]
    fn test_filter_low_confidence() {
        let blocks = vec![
            TextBlock {
                text: "高信頼".into(),
                bbox: (0, 0, 100, 30),
                confidence: 0.9,
                direction: TextDirection::Horizontal,
                font_size: Some(12.0),
            },
            TextBlock {
                text: "低信頼ノイズ".into(),
                bbox: (0, 50, 100, 30),
                confidence: 0.1,
                direction: TextDirection::Horizontal,
                font_size: Some(12.0),
            },
            TextBlock {
                text: "中信頼".into(),
                bbox: (0, 100, 100, 30),
                confidence: 0.5,
                direction: TextDirection::Horizontal,
                font_size: Some(12.0),
            },
            TextBlock {
                text: "境界値".into(),
                bbox: (0, 150, 100, 30),
                confidence: 0.3,
                direction: TextDirection::Horizontal,
                font_size: Some(12.0),
            },
        ];

        let filtered = MarkdownGenerator::filter_low_confidence(&blocks);
        assert_eq!(filtered.len(), 3, "Should filter out confidence < 0.3");
        assert_eq!(filtered[0].text, "高信頼");
        assert_eq!(filtered[1].text, "中信頼");
        assert_eq!(filtered[2].text, "境界値");
    }

    #[test]
    fn test_heading_detection_by_font_size() {
        // Median of [12, 12, 12, 16, 24] = 12.0
        // 24.0 / 12.0 = 2.0 >= 1.4 → ## heading
        // 16.0 / 12.0 = 1.33 >= 1.2 → ### sub-heading
        let blocks = vec![
            TextBlock {
                text: "大見出し".into(),
                bbox: (0, 0, 400, 50),
                confidence: 0.9,
                direction: TextDirection::Horizontal,
                font_size: Some(24.0),
            },
            TextBlock {
                text: "本文テキスト".into(),
                bbox: (0, 100, 400, 30),
                confidence: 0.9,
                direction: TextDirection::Horizontal,
                font_size: Some(12.0),
            },
            TextBlock {
                text: "もう一つの本文".into(),
                bbox: (0, 150, 400, 30),
                confidence: 0.9,
                direction: TextDirection::Horizontal,
                font_size: Some(12.0),
            },
            TextBlock {
                text: "さらに本文".into(),
                bbox: (0, 200, 400, 30),
                confidence: 0.9,
                direction: TextDirection::Horizontal,
                font_size: Some(12.0),
            },
            TextBlock {
                text: "小見出し".into(),
                bbox: (0, 300, 400, 35),
                confidence: 0.9,
                direction: TextDirection::Horizontal,
                font_size: Some(16.0), // 16/12 = 1.33 >= 1.2 → sub-heading
            },
        ];

        let text = MarkdownGenerator::build_structured_text(&blocks, &TextDirection::Horizontal);
        assert!(
            text.contains("## 大見出し"),
            "Large font should become ## heading, got: {}",
            text
        );
        assert!(
            text.contains("### 小見出し"),
            "Medium-large font should become ### sub-heading, got: {}",
            text
        );
        assert!(
            !text.contains("## 本文テキスト"),
            "Body text should NOT be a heading"
        );
    }

    #[test]
    fn test_paragraph_gap_detection() {
        let blocks = vec![
            TextBlock {
                text: "段落1の行".into(),
                bbox: (0, 0, 400, 30),
                confidence: 0.9,
                direction: TextDirection::Horizontal,
                font_size: Some(12.0),
            },
            TextBlock {
                text: "段落1の続き".into(),
                bbox: (0, 35, 400, 30),
                confidence: 0.9,
                direction: TextDirection::Horizontal,
                font_size: Some(12.0),
            },
            TextBlock {
                text: "段落2（大きなギャップの後）".into(),
                bbox: (0, 150, 400, 30), // gap = 150 - 65 = 85 >> 30*1.5 = 45
                confidence: 0.9,
                direction: TextDirection::Horizontal,
                font_size: Some(12.0),
            },
        ];

        let text = MarkdownGenerator::build_structured_text(&blocks, &TextDirection::Horizontal);
        // There should be a double newline (paragraph break) before "段落2"
        assert!(
            text.contains("\n\n"),
            "Should contain paragraph break (double newline), got: {:?}",
            text
        );
    }

    #[test]
    fn test_low_confidence_blocks_excluded_from_output() {
        let blocks = vec![
            TextBlock {
                text: "正常テキスト".into(),
                bbox: (0, 0, 200, 30),
                confidence: 0.9,
                direction: TextDirection::Horizontal,
                font_size: Some(12.0),
            },
            TextBlock {
                text: "ゴミOCRノイズ##@!".into(),
                bbox: (0, 50, 200, 30),
                confidence: 0.05,
                direction: TextDirection::Horizontal,
                font_size: Some(12.0),
            },
        ];

        let text = MarkdownGenerator::build_structured_text(&blocks, &TextDirection::Horizontal);
        assert!(
            text.contains("正常テキスト"),
            "High-confidence text should be included"
        );
        assert!(
            !text.contains("ゴミOCRノイズ"),
            "Low-confidence noise should be excluded"
        );
    }

    #[test]
    fn test_normalize_markdown_collapses_blank_lines() {
        let input = "line1\n\n\n\n\nline2\n\n\n\nline3";
        let result = MarkdownGenerator::normalize_markdown(input);
        // Should have at most one blank line between lines
        assert!(
            !result.contains("\n\n\n"),
            "Should not have triple newlines, got: {:?}",
            result
        );
        assert!(result.contains("line1"));
        assert!(result.contains("line2"));
        assert!(result.contains("line3"));
    }

    #[test]
    fn test_median_font_size() {
        let blocks = vec![
            TextBlock {
                text: "a".into(),
                bbox: (0, 0, 10, 10),
                confidence: 0.9,
                direction: TextDirection::Horizontal,
                font_size: Some(10.0),
            },
            TextBlock {
                text: "b".into(),
                bbox: (0, 0, 10, 10),
                confidence: 0.9,
                direction: TextDirection::Horizontal,
                font_size: Some(12.0),
            },
            TextBlock {
                text: "c".into(),
                bbox: (0, 0, 10, 10),
                confidence: 0.9,
                direction: TextDirection::Horizontal,
                font_size: Some(24.0),
            },
        ];

        let median = MarkdownGenerator::median_font_size(&blocks);
        assert_eq!(median, Some(12.0), "Median of [10, 12, 24] should be 12");
    }

    #[test]
    fn test_median_font_size_no_sizes() {
        let blocks = vec![TextBlock {
            text: "no size".into(),
            bbox: (0, 0, 10, 10),
            confidence: 0.9,
            direction: TextDirection::Horizontal,
            font_size: None,
        }];

        assert_eq!(
            MarkdownGenerator::median_font_size(&blocks),
            None,
            "No font sizes should return None"
        );
    }

    #[test]
    fn test_all_low_confidence_produces_empty() {
        let blocks = vec![
            TextBlock {
                text: "noise1".into(),
                bbox: (0, 0, 100, 30),
                confidence: 0.1,
                direction: TextDirection::Horizontal,
                font_size: Some(12.0),
            },
            TextBlock {
                text: "noise2".into(),
                bbox: (0, 50, 100, 30),
                confidence: 0.2,
                direction: TextDirection::Horizontal,
                font_size: Some(12.0),
            },
        ];

        let text = MarkdownGenerator::build_structured_text(&blocks, &TextDirection::Horizontal);
        assert!(
            text.is_empty(),
            "All low-confidence blocks should produce empty text, got: '{}'",
            text
        );
    }

    #[test]
    fn test_is_page_number_line() {
        assert!(MarkdownGenerator::is_page_number_line("028"));
        assert!(MarkdownGenerator::is_page_number_line("1"));
        assert!(MarkdownGenerator::is_page_number_line("300"));
        assert!(MarkdownGenerator::is_page_number_line("0034"));
        assert!(!MarkdownGenerator::is_page_number_line("12345"));
        assert!(!MarkdownGenerator::is_page_number_line("abc"));
        assert!(!MarkdownGenerator::is_page_number_line(""));
        assert!(!MarkdownGenerator::is_page_number_line("Hello 123"));
    }

    #[test]
    fn test_is_furigana_line() {
        assert!(MarkdownGenerator::is_furigana_line("さとみ ゆうすけ"));
        assert!(MarkdownGenerator::is_furigana_line("たなか たろう"));
        assert!(MarkdownGenerator::is_furigana_line("カタカナ テスト"));
        assert!(!MarkdownGenerator::is_furigana_line(
            "これは長い文章なので振り仮名ではないと判定されるべきです"
        ));
        assert!(!MarkdownGenerator::is_furigana_line("漢字テスト"));
        assert!(!MarkdownGenerator::is_furigana_line("hello"));
        assert!(!MarkdownGenerator::is_furigana_line("あ")); // too short
    }

    #[test]
    fn test_normalize_removes_page_numbers() {
        let input = "028\nSome real text here\n";
        let result = MarkdownGenerator::normalize_markdown(input);
        assert!(!result.contains("028"));
        assert!(result.contains("Some real text"));
    }

    #[test]
    fn test_normalize_converts_plus_headings() {
        let input = "Some intro text\n+衛星画像が暴く大量の死\nBody text\n";
        let result = MarkdownGenerator::normalize_markdown(input);
        assert!(result.contains("## 衛星画像が暴く大量の死"));
        assert!(!result.contains("+衛星画像"));
    }

    #[test]
    fn test_normalize_removes_furigana() {
        let input = "里見祐介\nさとみ ゆうすけ\nSome text\n";
        let result = MarkdownGenerator::normalize_markdown(input);
        assert!(!result.contains("さとみ ゆうすけ"));
        assert!(result.contains("里見祐介"));
    }

    #[test]
    fn test_normalize_removes_uppercase_noise() {
        let input = "OIL\nReal text here\n";
        let result = MarkdownGenerator::normalize_markdown(input);
        assert!(!result.contains("OIL"));
        assert!(result.contains("Real text"));
    }
}
