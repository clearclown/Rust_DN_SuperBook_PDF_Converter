//! Book manifest generation with normalized chapter list (issue #56)
//!
//! Vertical-text books repeat running headers (柱) like「第1部」「第1章」on
//! alternating pages. When those survive into the per-page markdown as
//! headings, a naive chapter list counts each repetition as a new chapter.
//! This module derives the chapter list from the per-page markdown files,
//! normalizes each title to a comparison key, and keeps only the first
//! occurrence of each key as a chapter start.

use serde::{Deserialize, Serialize};
use std::collections::HashMap;
use std::path::Path;

/// A repeated title within this many pages of its previous occurrence is
/// treated as a running-header (柱) repeat and dropped from the chapter list.
const RUNNING_HEADER_PAGE_WINDOW: usize = 3;

/// Book-level manifest written next to the merged markdown output
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct BookManifest {
    /// Manifest format version
    pub version: u32,
    /// Book title (from the input filename)
    pub title: String,
    /// Total number of pages processed
    pub total_pages: usize,
    /// Last page of the main content (1-based), when trailing back matter
    /// (奥付・出版目録・索引 — issue #60) was detected. Pages after this are
    /// colophon/catalog noise; they stay in the markdown (a false positive
    /// must never destroy body text) and downstream slices using this field.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub main_content_end_page: Option<usize>,
    /// Deduplicated chapter list in reading order
    pub chapters: Vec<ChapterEntry>,
}

/// One chapter in the manifest
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ChapterEntry {
    /// 1-based chapter number after deduplication
    pub index: usize,
    /// Heading text of the first occurrence
    pub title: String,
    /// Markdown heading level (2 = `##`, 3 = `###`)
    pub level: u8,
    /// 1-based page number where the chapter starts
    pub page: usize,
}

impl BookManifest {
    /// Build a manifest by scanning per-page markdown files
    /// (`pages_dir/page_NNN.md`, 1-based) for `##`/`###` headings.
    /// Unreadable or missing page files are skipped.
    pub fn from_page_files(title: &str, pages_dir: &Path, page_count: usize) -> Self {
        let mut raw = Vec::new();
        for i in 0..page_count {
            let path = pages_dir.join(format!("page_{:03}.md", i + 1));
            let Ok(content) = std::fs::read_to_string(&path) else {
                continue;
            };
            for line in content.lines() {
                let (level, text) = if let Some(t) = line.strip_prefix("### ") {
                    (3u8, t)
                } else if let Some(t) = line.strip_prefix("## ") {
                    (2u8, t)
                } else {
                    continue;
                };
                let text = text.trim();
                if !text.is_empty() {
                    raw.push((i + 1, level, text.to_string()));
                }
            }
        }
        Self::from_raw_headings(title, page_count, raw)
    }

    /// Build a manifest from raw `(page, level, heading)` tuples in reading
    /// order, dropping running-header (柱) repetitions.
    ///
    /// A repeated title counts as a running header only when it reappears
    /// within [`RUNNING_HEADER_PAGE_WINDOW`] pages of its previous
    /// occurrence — running headers recur every page or two, while a
    /// legitimately repeated section title (「まとめ」per chapter, etc.)
    /// reappears much further away and is kept as a new chapter (issue #64).
    /// A complete fix would detect the alternating periodicity of running
    /// headers instead of using a fixed page window.
    pub fn from_raw_headings(
        title: &str,
        total_pages: usize,
        raw: Vec<(usize, u8, String)>,
    ) -> Self {
        let mut last_seen: HashMap<String, usize> = HashMap::new();
        let mut chapters = Vec::new();
        for (page, level, text) in raw {
            let key = title_key(&text);
            if key.is_empty() {
                continue;
            }
            let is_running_header_repeat = last_seen
                .get(&key)
                .is_some_and(|&prev| page.saturating_sub(prev) <= RUNNING_HEADER_PAGE_WINDOW);
            last_seen.insert(key, page);
            if is_running_header_repeat {
                continue;
            }
            chapters.push(ChapterEntry {
                index: chapters.len() + 1,
                title: text,
                level,
                page,
            });
        }
        Self {
            version: 1,
            title: title.to_string(),
            total_pages,
            // Filled in by the pipeline after back-matter detection (#60)
            main_content_end_page: None,
            chapters,
        }
    }

    /// Write the manifest as pretty-printed JSON
    pub fn save(&self, path: &Path) -> std::io::Result<()> {
        let json = serde_json::to_string_pretty(self)
            .map_err(|e| std::io::Error::new(std::io::ErrorKind::InvalidData, e))?;
        std::fs::write(path, json)
    }
}

/// Normalize a heading title to a comparison key so that OCR variations of
/// the same running header (「第1部」/「第 1 部」/「第１部」) collapse together:
/// - fold full-width ASCII to half-width, lowercase (BEFORE filtering, so
///   `～` U+FF5E folds to `~` and is then stripped like `〜` U+301C — the
///   wave-dash pair is a classic Japanese OCR variation, issue #64)
/// - strip all whitespace (ASCII and full-width)
/// - drop punctuation and bracket characters
///
/// A complete fix would apply full NFKC normalization and fuzzy (edit
/// distance) matching; folding + punctuation stripping covers the common
/// OCR variations without new dependencies.
pub fn title_key(title: &str) -> String {
    title
        .chars()
        .map(normalize_char)
        .filter(|c| !c.is_whitespace())
        .filter(|c| !is_title_punctuation(*c))
        .collect()
}

/// Punctuation/bracket characters ignored when comparing titles
fn is_title_punctuation(c: char) -> bool {
    matches!(
        c,
        '。' | '、'
            | '，'
            | ','
            | '.'
            | '．'
            | '・'
            | '·'
            | '…'
            | '‥'
            | '：'
            | ':'
            | '；'
            | ';'
            | '！'
            | '!'
            | '？'
            | '?'
            | '「'
            | '」'
            | '『'
            | '』'
            | '（'
            | '）'
            | '('
            | ')'
            | '【'
            | '】'
            | '〈'
            | '〉'
            | '《'
            | '》'
            | '［'
            | '］'
            | '['
            | ']'
            | '〜'
            | '~'
    )
    // NOTE (issue #64): hyphen/long-vowel characters (`-` `－` `ー` `―`) are
    // deliberately NOT stripped — removing them collapsed distinct titles
    // like 第1-2章 and 第12章 (or スーパー and スパ) into one key, deleting
    // real chapters as false running-header repeats.
}

/// Fold full-width ASCII (U+FF01..=U+FF5E) to half-width and lowercase
fn normalize_char(c: char) -> char {
    let folded = match c {
        '\u{FF01}'..='\u{FF5E}' => char::from_u32(c as u32 - 0xFEE0).unwrap_or(c),
        _ => c,
    };
    folded.to_ascii_lowercase()
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_title_key_normalizes_whitespace_and_width() {
        assert_eq!(title_key("第1部"), title_key("第 1 部"));
        assert_eq!(title_key("第1部"), title_key("第１部"));
        assert_eq!(title_key("第1章　出発"), title_key("第1章 出発"));
        assert_eq!(title_key("ABC"), title_key("ａｂｃ"));
    }

    #[test]
    fn test_title_key_ignores_punctuation() {
        assert_eq!(title_key("「第1章」出発"), title_key("第1章 出発"));
        assert_ne!(title_key("第1章"), title_key("第2章"));
    }

    #[test]
    fn test_title_key_wave_dash_variants_collapse() {
        // Issue #64: U+FF5E must fold before punctuation filtering so both
        // wave-dash forms produce the same key
        assert_eq!(title_key("序章～出会い"), title_key("序章〜出会い"));
        assert_eq!(title_key("序章~出会い"), title_key("序章〜出会い"));
    }

    #[test]
    fn test_title_key_keeps_hyphen_distinctions() {
        // Issue #64: stripping hyphens/long vowels collapsed distinct titles
        assert_ne!(title_key("第1-2章"), title_key("第12章"));
        assert_ne!(title_key("スーパー"), title_key("スパ"));
        // Full-width hyphen folds to ASCII hyphen, staying consistent
        assert_eq!(title_key("第１－２章"), title_key("第1-2章"));
    }

    #[test]
    fn test_distant_repeated_section_titles_are_kept() {
        // Issue #64: a per-chapter「まとめ」far from its previous occurrence
        // is a real section, not a running header
        let raw = vec![
            (2, 2, "第1章".to_string()),
            (10, 3, "まとめ".to_string()),
            (12, 2, "第2章".to_string()),
            (40, 3, "まとめ".to_string()), // 30 pages later → keep
        ];
        let manifest = BookManifest::from_raw_headings("test", 50, raw);
        assert_eq!(manifest.chapters.len(), 4);
        assert_eq!(manifest.chapters[3].title, "まとめ");
        assert_eq!(manifest.chapters[3].page, 40);
    }

    #[test]
    fn test_continuous_running_header_chain_is_collapsed() {
        // A running header repeating every other page stays suppressed
        // because each repeat is within the window of the previous one
        let raw = vec![
            (1, 2, "第1部".to_string()),
            (3, 2, "第1部".to_string()),
            (5, 2, "第1部".to_string()),
            (7, 2, "第1部".to_string()),
        ];
        let manifest = BookManifest::from_raw_headings("test", 10, raw);
        assert_eq!(manifest.chapters.len(), 1);
        assert_eq!(manifest.chapters[0].page, 1);
    }

    #[test]
    fn test_running_header_dedupe() {
        // 柱 (running headers) repeat「第1部」on alternating pages
        let raw = vec![
            (1, 2, "第1部".to_string()),
            (2, 2, "第1章 出発".to_string()),
            (3, 2, "第1部".to_string()),      // running header repeat
            (4, 2, "第 1 部".to_string()),    // OCR variation of the same header
            (5, 2, "第1章 出発".to_string()), // running header repeat
            (7, 2, "第2章 到着".to_string()),
        ];
        let manifest = BookManifest::from_raw_headings("test", 10, raw);

        assert_eq!(manifest.chapters.len(), 3);
        assert_eq!(manifest.chapters[0].title, "第1部");
        assert_eq!(manifest.chapters[0].page, 1);
        assert_eq!(manifest.chapters[1].title, "第1章 出発");
        assert_eq!(manifest.chapters[1].page, 2);
        assert_eq!(manifest.chapters[2].title, "第2章 到着");
        assert_eq!(manifest.chapters[2].page, 7);
        // Indexes are sequential after dedupe
        assert_eq!(
            manifest
                .chapters
                .iter()
                .map(|c| c.index)
                .collect::<Vec<_>>(),
            vec![1, 2, 3]
        );
    }

    #[test]
    fn test_from_page_files_scans_headings() {
        let tmp = tempfile::tempdir().unwrap();
        let pages = tmp.path();
        std::fs::write(pages.join("page_001.md"), "## 第1章 出発\n\n本文です。\n").unwrap();
        std::fs::write(
            pages.join("page_002.md"),
            "## 第1章 出発\n\n### 節タイトル\n\n続きの本文。\n",
        )
        .unwrap();
        // page_003.md intentionally missing — must be skipped

        let manifest = BookManifest::from_page_files("本のタイトル", pages, 3);

        assert_eq!(manifest.title, "本のタイトル");
        assert_eq!(manifest.total_pages, 3);
        assert_eq!(manifest.chapters.len(), 2);
        assert_eq!(manifest.chapters[0].title, "第1章 出発");
        assert_eq!(manifest.chapters[0].level, 2);
        assert_eq!(manifest.chapters[0].page, 1);
        assert_eq!(manifest.chapters[1].title, "節タイトル");
        assert_eq!(manifest.chapters[1].level, 3);
        assert_eq!(manifest.chapters[1].page, 2);
    }

    #[test]
    fn test_save_writes_valid_json() {
        let tmp = tempfile::tempdir().unwrap();
        let path = tmp.path().join("book_manifest.json");
        let manifest =
            BookManifest::from_raw_headings("test", 5, vec![(1, 2, "第1章".to_string())]);
        manifest.save(&path).unwrap();

        let loaded: BookManifest =
            serde_json::from_str(&std::fs::read_to_string(&path).unwrap()).unwrap();
        assert_eq!(loaded.version, 1);
        assert_eq!(loaded.chapters.len(), 1);
        assert_eq!(loaded.chapters[0].title, "第1章");
    }
}
