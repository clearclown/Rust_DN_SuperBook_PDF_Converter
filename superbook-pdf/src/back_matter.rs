//! Back-matter (巻末) detection — issue #60
//!
//! Scanned books often end with pages that are not part of the work itself:
//! the colophon (奥付), publisher catalogs (出版目録・全集広告), indexes,
//! and lists of other books. Downstream consumers (audiobook generation,
//! summarization) treat those as body text unless told otherwise.
//!
//! This module estimates where the main content ends from per-page markdown
//! text using keyword-category density, a position gate (only the trailing
//! quarter of the book is considered), and a trailing-run requirement.
//! The detected boundary is recorded in `book_manifest.json`
//! (`main_content_end_page`) so downstream can slice; pages are NOT removed
//! from the markdown itself — a false positive must never destroy body text.
//!
//! A complete fix would additionally use YomiToku layout roles to classify
//! advertisement/index page layouts individually and offer an opt-in flag to
//! exclude the detected pages from the markdown output.

use std::path::Path;

/// Fraction of the book (from the start) that is never considered back
/// matter. Only pages in the trailing 25% can form the back-matter run.
const POSITION_GATE: f64 = 0.75;

/// Minimum number of distinct keyword categories a page must hit to be
/// counted as back-matter-like (a single stray 「発行」 in body text stays
/// below this).
const MIN_CATEGORY_HITS: usize = 2;

/// Maximum number of consecutive non-matching pages tolerated inside the
/// trailing back-matter run (catalogs often alternate with near-blank pages).
const MAX_RUN_GAP: usize = 1;

/// Keyword categories typical of Japanese colophon / catalog / rights pages.
/// A page's score is the number of DISTINCT categories that match, so
/// repeating one word many times cannot qualify a page by itself.
const KEYWORD_CATEGORIES: &[&[&str]] = &[
    // 奥付: publication credits
    &[
        "発行所",
        "発行者",
        "発行日",
        "印刷所",
        "製本所",
        "印刷・製本",
    ],
    // 奥付: rights / defect-exchange boilerplate
    &["ISBN", "落丁", "乱丁", "無断転載", "無断複製", "検印廃止"],
    // 目録・広告: publisher catalogs and series ads
    &[
        "目録",
        "既刊",
        "好評発売中",
        "続刊",
        "全集",
        "選書",
        "文庫版",
    ],
    // 価格表記
    &["定価", "円+税", "円（税込）", "円(税込)", "本体価格"],
];

/// Count how many distinct keyword categories match the page text.
pub(crate) fn back_matter_score(text: &str) -> usize {
    KEYWORD_CATEGORIES
        .iter()
        .filter(|category| category.iter().any(|kw| text.contains(kw)))
        .count()
}

/// Detect the last main-content page from `(page_number, text)` pairs
/// (1-based page numbers, ascending). Returns `None` when no back matter is
/// found — or when detection would claim the whole book, which is treated as
/// a false positive.
pub fn detect_from_texts(pages: &[(usize, String)], page_count: usize) -> Option<usize> {
    if page_count == 0 {
        return None;
    }
    let gate_page = ((page_count as f64) * POSITION_GATE).floor() as usize;

    // Walk backwards from the last page, extending the run while pages are
    // back-matter-like, tolerating up to MAX_RUN_GAP quiet pages in between.
    let mut run_start: Option<usize> = None;
    let mut gap = 0usize;

    for &(page_num, ref text) in pages.iter().rev() {
        if page_num <= gate_page {
            break;
        }
        if back_matter_score(text) >= MIN_CATEGORY_HITS {
            run_start = Some(page_num);
            gap = 0;
        } else if run_start.is_some() {
            gap += 1;
            if gap > MAX_RUN_GAP {
                break;
            }
        } else {
            // The very last pages may be quiet (blank scan) before the
            // catalog appears; tolerate the same gap budget at the tail.
            gap += 1;
            if gap > MAX_RUN_GAP {
                break;
            }
        }
    }

    match run_start {
        Some(first_back_page) if first_back_page > 1 => Some(first_back_page - 1),
        _ => None,
    }
}

/// Detect the last main-content page by reading per-page markdown files
/// (`pages_dir/page_NNN.md`, 1-based). Missing pages are skipped.
pub fn detect_main_content_end(pages_dir: &Path, page_count: usize) -> Option<usize> {
    let mut pages = Vec::new();
    for i in 0..page_count {
        let path = pages_dir.join(format!("page_{:03}.md", i + 1));
        if let Ok(text) = std::fs::read_to_string(&path) {
            pages.push((i + 1, text));
        }
    }
    detect_from_texts(&pages, page_count)
}

#[cfg(test)]
mod tests {
    use super::*;

    fn body_page(n: usize) -> (usize, String) {
        (n, format!("これは第{}ページの本文です。物語は続く。", n))
    }

    fn colophon_page(n: usize) -> (usize, String) {
        (
            n,
            "発行所 株式会社テスト出版\n印刷所 テスト印刷\nISBN 978-4-00-000000-0\n\
             定価はカバーに表示してあります。落丁・乱丁本はお取替えいたします。"
                .to_string(),
        )
    }

    fn catalog_page(n: usize) -> (usize, String) {
        (
            n,
            "テスト出版目録\n二重人格 ドストエフスキー 訳\n八月の光 フォークナー 訳\n\
             好評発売中 定価 880円+税"
                .to_string(),
        )
    }

    #[test]
    fn test_detects_trailing_colophon_and_catalog() {
        let mut pages: Vec<(usize, String)> = (1..=75).map(body_page).collect();
        pages.push(colophon_page(76));
        for n in 77..=80 {
            pages.push(catalog_page(n));
        }
        assert_eq!(detect_from_texts(&pages, 80), Some(75));
    }

    #[test]
    fn test_no_back_matter_returns_none() {
        let pages: Vec<(usize, String)> = (1..=80).map(body_page).collect();
        assert_eq!(detect_from_texts(&pages, 80), None);
    }

    #[test]
    fn test_single_keyword_in_body_does_not_trigger() {
        let mut pages: Vec<(usize, String)> = (1..=79).map(body_page).collect();
        // One category hit (定価) on a late page is not enough
        pages.push((80, "彼は定価の意味について考え続けた。".to_string()));
        assert_eq!(detect_from_texts(&pages, 80), None);
    }

    #[test]
    fn test_early_colophon_like_page_is_ignored() {
        // Keyword-dense page in the first 75% (e.g. quoted colophon in the
        // body) must not clip the book
        let mut pages: Vec<(usize, String)> = (1..=80).map(body_page).collect();
        pages[39] = colophon_page(40);
        assert_eq!(detect_from_texts(&pages, 80), None);
    }

    #[test]
    fn test_gap_inside_run_is_tolerated() {
        let mut pages: Vec<(usize, String)> = (1..=75).map(body_page).collect();
        pages.push(catalog_page(76));
        pages.push((77, String::new())); // near-blank scan between catalog pages
        pages.push(catalog_page(78));
        pages.push(colophon_page(79));
        pages.push(catalog_page(80));
        assert_eq!(detect_from_texts(&pages, 80), Some(75));
    }

    #[test]
    fn test_trailing_blank_page_after_catalog() {
        let mut pages: Vec<(usize, String)> = (1..=77).map(body_page).collect();
        pages.push(colophon_page(78));
        pages.push(catalog_page(79));
        pages.push((80, String::new())); // blank last scan
        assert_eq!(detect_from_texts(&pages, 80), Some(77));
    }

    #[test]
    fn test_score_counts_categories_not_occurrences() {
        // Repeating one keyword many times stays a single category hit
        let text = "定価 定価 定価 定価 定価";
        assert_eq!(back_matter_score(text), 1);
        let colophon = "発行所 テスト出版 ISBN 978-4";
        assert!(back_matter_score(colophon) >= 2);
    }
}
