//! Markdown generation module
//!
//! Generates Markdown files from OCR results and detected figures.
//! Supports page-by-page generation with final merge.

use std::fmt::Write as FmtWrite;
use std::path::{Path, PathBuf};
use thiserror::Error;

use crate::figure_detect::{FigureRegion, PageClassification};
use crate::yomitoku::{OcrResult, TextBlock, TextDirection};

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
                    // Write text content, preserving paragraphs
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

        Ok(md)
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
                // Sort text blocks by reading order
                let sorted_blocks =
                    Self::sort_text_blocks(&ocr_result.text_blocks, &ocr_result.text_direction);

                // Interleave text and figures based on vertical position
                let mut figure_idx = 0;
                let mut current_text = String::new();

                for block in &sorted_blocks {
                    // Check if any figure should be inserted before this text block
                    while figure_idx < figures.len() {
                        let fig = &figures[figure_idx];
                        let fig_y = fig.bbox.1;
                        let block_y = block.bbox.1;

                        if fig_y < block_y {
                            // Insert accumulated text
                            if !current_text.is_empty() {
                                elements.push(ContentElement::Text {
                                    content: std::mem::take(&mut current_text),
                                    direction: ocr_result.text_direction,
                                });
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

                    // Accumulate text
                    if !current_text.is_empty() {
                        current_text.push('\n');
                    }
                    current_text.push_str(&block.text);
                }

                // Flush remaining text
                if !current_text.is_empty() {
                    elements.push(ContentElement::Text {
                        content: current_text,
                        direction: ocr_result.text_direction,
                    });
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

    /// Sort text blocks by reading order and join into a single string
    fn sort_and_join_text_blocks(blocks: &[TextBlock], direction: &TextDirection) -> String {
        let sorted = Self::sort_text_blocks(blocks, direction);
        let mut result = String::new();
        for block in &sorted {
            if !result.is_empty() {
                result.push('\n');
            }
            result.push_str(&block.text);
        }
        result
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
}
