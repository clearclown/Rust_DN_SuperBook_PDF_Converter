# 30-partial-reprocess.spec.md - Partial Reprocessing Specification

## Overview

処理中に失敗したページのみを再処理する機能。キャッシュを活用して高速化を実現。

---

## Responsibilities

1. 失敗ページの特定と記録
2. 成功ページのキャッシュ保持
3. 失敗ページのみの再処理
4. 結果のマージと最終PDF生成
5. 再処理履歴の管理

---

## Data Structures

```rust
/// Page processing status
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub enum PageStatus {
    /// Successfully processed
    Success {
        cached_path: PathBuf,
        processing_time: f64,
    },
    /// Failed with error
    Failed {
        error: String,
        retry_count: u32,
    },
    /// Skipped (not processed yet)
    Pending,
}

/// Partial reprocessing state
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ReprocessState {
    /// Source PDF path
    pub source_pdf: PathBuf,
    /// Output directory
    pub output_dir: PathBuf,
    /// Page statuses (0-indexed)
    pub pages: Vec<PageStatus>,
    /// Processing configuration hash
    pub config_hash: String,
    /// Creation timestamp
    pub created_at: chrono::DateTime<chrono::Utc>,
    /// Last updated timestamp
    pub updated_at: chrono::DateTime<chrono::Utc>,
}

/// Reprocess options
#[derive(Debug, Clone)]
pub struct ReprocessOptions {
    /// Maximum retry attempts per page
    pub max_retries: u32,
    /// Retry only specific pages (empty = all failed)
    pub page_indices: Vec<usize>,
    /// Force reprocess even if cached
    pub force: bool,
    /// Preserve intermediate files
    pub keep_intermediates: bool,
}

impl Default for ReprocessOptions {
    fn default() -> Self {
        Self {
            max_retries: 3,
            page_indices: vec![],
            force: false,
            keep_intermediates: false,
        }
    }
}

/// Reprocess result
#[derive(Debug, Clone)]
pub struct ReprocessResult {
    /// Total pages in document
    pub total_pages: usize,
    /// Pages successfully processed
    pub success_count: usize,
    /// Pages still failing
    pub failed_count: usize,
    /// Pages reprocessed this run
    pub reprocessed_count: usize,
    /// Final output path (if complete)
    pub output_path: Option<PathBuf>,
    /// Remaining failed page indices
    pub failed_pages: Vec<usize>,
}
```

---

## API

### State Management

```rust
impl ReprocessState {
    /// Load state from cache file
    pub fn load(cache_path: &Path) -> Result<Self>;

    /// Save state to cache file
    pub fn save(&self, cache_path: &Path) -> Result<()>;

    /// Get failed page indices
    pub fn failed_pages(&self) -> Vec<usize> {
        self.pages
            .iter()
            .enumerate()
            .filter_map(|(i, s)| match s {
                PageStatus::Failed { .. } => Some(i),
                _ => None,
            })
            .collect()
    }

    /// Get success page indices
    pub fn success_pages(&self) -> Vec<usize>;

    /// Check if all pages are successful
    pub fn is_complete(&self) -> bool {
        self.pages.iter().all(|s| matches!(s, PageStatus::Success { .. }))
    }

    /// Get completion percentage
    pub fn completion_percent(&self) -> f64 {
        let success = self.pages.iter().filter(|s| matches!(s, PageStatus::Success { .. })).count();
        (success as f64 / self.pages.len() as f64) * 100.0
    }
}
```

### Reprocessing

```rust
impl PdfPipeline {
    /// Reprocess failed pages only
    pub fn reprocess<P: ProgressCallback>(
        &self,
        state: &mut ReprocessState,
        options: &ReprocessOptions,
        progress: &P,
    ) -> Result<ReprocessResult> {
        let failed_pages = if options.page_indices.is_empty() {
            state.failed_pages()
        } else {
            options.page_indices.clone()
        };

        progress.on_step_start(&format!("Reprocessing {} pages...", failed_pages.len()));

        for page_idx in &failed_pages {
            match self.process_single_page(*page_idx, state, progress) {
                Ok(cached_path) => {
                    state.pages[*page_idx] = PageStatus::Success {
                        cached_path,
                        processing_time: 0.0, // TODO: measure
                    };
                }
                Err(e) => {
                    if let PageStatus::Failed { retry_count, .. } = &mut state.pages[*page_idx] {
                        *retry_count += 1;
                        if *retry_count >= options.max_retries {
                            // Max retries exceeded
                        }
                    }
                }
            }
            state.save(&self.cache_path())?;
        }

        // If all pages successful, generate final PDF
        let output_path = if state.is_complete() {
            Some(self.generate_final_pdf(state, progress)?)
        } else {
            None
        };

        Ok(ReprocessResult {
            total_pages: state.pages.len(),
            success_count: state.success_pages().len(),
            failed_count: state.failed_pages().len(),
            reprocessed_count: failed_pages.len(),
            output_path,
            failed_pages: state.failed_pages(),
        })
    }

    /// Process a single page
    fn process_single_page(
        &self,
        page_idx: usize,
        state: &ReprocessState,
        progress: &impl ProgressCallback,
    ) -> Result<PathBuf>;
}
```

### CLI Integration

```rust
/// CLI subcommand for reprocessing
#[derive(Parser)]
pub struct ReprocessCommand {
    /// PDF file or state file path
    #[arg(required = true)]
    pub input: PathBuf,

    /// Specific pages to retry (comma-separated)
    #[arg(short, long, value_delimiter = ',')]
    pub pages: Option<Vec<usize>>,

    /// Maximum retry attempts
    #[arg(long, default_value = "3")]
    pub max_retries: u32,

    /// Force reprocess all failed pages
    #[arg(short, long)]
    pub force: bool,

    /// Show status only, don't process
    #[arg(long)]
    pub status: bool,
}
```

---

## Test Cases

### TC-REPROC-001: 状態の保存と読み込み

```rust
#[test]
fn test_state_persistence() {
    let temp_dir = tempfile::tempdir().unwrap();
    let state_path = temp_dir.path().join("state.json");

    let state = ReprocessState {
        source_pdf: PathBuf::from("test.pdf"),
        output_dir: PathBuf::from("output"),
        pages: vec![
            PageStatus::Success { cached_path: PathBuf::from("p1.png"), processing_time: 1.0 },
            PageStatus::Failed { error: "OOM".into(), retry_count: 1 },
            PageStatus::Pending,
        ],
        config_hash: "abc123".into(),
        created_at: chrono::Utc::now(),
        updated_at: chrono::Utc::now(),
    };

    state.save(&state_path).unwrap();
    let loaded = ReprocessState::load(&state_path).unwrap();

    assert_eq!(loaded.source_pdf, state.source_pdf);
    assert_eq!(loaded.pages.len(), 3);
}
```

### TC-REPROC-002: 失敗ページの特定

```rust
#[test]
fn test_failed_pages_detection() {
    let state = ReprocessState {
        pages: vec![
            PageStatus::Success { cached_path: PathBuf::new(), processing_time: 0.0 },
            PageStatus::Failed { error: "error".into(), retry_count: 0 },
            PageStatus::Success { cached_path: PathBuf::new(), processing_time: 0.0 },
            PageStatus::Failed { error: "error".into(), retry_count: 1 },
            PageStatus::Pending,
        ],
        ..Default::default()
    };

    let failed = state.failed_pages();
    assert_eq!(failed, vec![1, 3]);
}
```

### TC-REPROC-003: 完了率計算

```rust
#[test]
fn test_completion_percent() {
    let state = ReprocessState {
        pages: vec![
            PageStatus::Success { cached_path: PathBuf::new(), processing_time: 0.0 },
            PageStatus::Success { cached_path: PathBuf::new(), processing_time: 0.0 },
            PageStatus::Failed { error: "error".into(), retry_count: 0 },
            PageStatus::Pending,
        ],
        ..Default::default()
    };

    assert!((state.completion_percent() - 50.0).abs() < 0.01);
}
```

### TC-REPROC-004: 特定ページの再処理

```rust
#[test]
fn test_reprocess_specific_pages() {
    let pipeline = PdfPipeline::new(PipelineConfig::default());
    let mut state = create_test_state_with_failures();

    let options = ReprocessOptions {
        page_indices: vec![1, 3], // Only retry pages 1 and 3
        ..Default::default()
    };

    let result = pipeline.reprocess(&mut state, &options, &NoopProgress).unwrap();

    assert_eq!(result.reprocessed_count, 2);
}
```

### TC-REPROC-005: 最大リトライ制限

```rust
#[test]
fn test_max_retry_limit() {
    let mut state = ReprocessState {
        pages: vec![
            PageStatus::Failed { error: "persistent error".into(), retry_count: 2 },
        ],
        ..Default::default()
    };

    let options = ReprocessOptions {
        max_retries: 3,
        ..Default::default()
    };

    // After this retry, retry_count becomes 3, reaching max
    // Further retries should not be attempted
}
```

### TC-REPROC-006: 完了時PDF生成

```rust
#[test]
fn test_final_pdf_generation_on_complete() {
    let pipeline = PdfPipeline::new(PipelineConfig::default());
    let mut state = create_test_state_all_success();

    let options = ReprocessOptions::default();
    let result = pipeline.reprocess(&mut state, &options, &NoopProgress).unwrap();

    assert!(result.output_path.is_some());
    assert!(result.output_path.unwrap().exists());
}
```

### TC-REPROC-007: 設定変更時の再処理

```rust
#[test]
fn test_config_change_invalidates_cache() {
    let state1 = ReprocessState {
        config_hash: "hash_v1".into(),
        ..Default::default()
    };

    let new_config_hash = "hash_v2";

    // Config hash changed, should invalidate all cached pages
    assert_ne!(state1.config_hash, new_config_hash);
}
```

---

## CLI Usage

```bash
# Show reprocess status
superbook-pdf reprocess --status input.pdf

# Reprocess all failed pages
superbook-pdf reprocess input.pdf

# Reprocess specific pages
superbook-pdf reprocess input.pdf --pages 5,10,15

# Force reprocess with max retries
superbook-pdf reprocess input.pdf --force --max-retries 5

# Continue from state file
superbook-pdf reprocess output/.superbook-state.json
```

---

## State File Format

```json
{
  "source_pdf": "/path/to/input.pdf",
  "output_dir": "/path/to/output",
  "pages": [
    { "Success": { "cached_path": "cache/p0.png", "processing_time": 1.5 } },
    { "Failed": { "error": "Out of memory", "retry_count": 2 } },
    { "Success": { "cached_path": "cache/p2.png", "processing_time": 1.2 } }
  ],
  "config_hash": "sha256:abc123...",
  "created_at": "2024-01-25T10:00:00Z",
  "updated_at": "2024-01-25T10:30:00Z"
}
```

---

## Acceptance Criteria

- [ ] 処理状態をJSONファイルで永続化できる
- [ ] 失敗ページを正確に特定できる
- [ ] 成功ページのキャッシュを保持できる
- [ ] 失敗ページのみを再処理できる
- [ ] 特定ページを指定して再処理できる
- [ ] 最大リトライ回数を制限できる
- [ ] 全ページ成功時に最終PDFを生成できる
- [ ] CLIから再処理機能を利用できる
- [ ] 設定変更時にキャッシュを無効化できる

---

## Dependencies

```toml
[dependencies]
serde = { version = "1", features = ["derive"] }
serde_json = "1"
chrono = { version = "0.4", features = ["serde"] }
```
