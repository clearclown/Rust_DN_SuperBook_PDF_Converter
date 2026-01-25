//! Partial Reprocessing module
//!
//! Provides functionality to reprocess only failed pages from a previous run,
//! utilizing cached successful pages to speed up recovery.
//!
//! # Features
//!
//! - Track page processing status (Success/Failed/Pending)
//! - Persist state to JSON for recovery
//! - Reprocess only failed pages
//! - Merge cached and reprocessed pages
//!
//! # Example
//!
//! ```rust,no_run
//! use superbook_pdf::reprocess::{ReprocessState, ReprocessOptions, PageStatus};
//! use std::path::Path;
//!
//! // Load existing state
//! let mut state = ReprocessState::load(Path::new("output/.superbook-state.json")).unwrap();
//!
//! // Check failed pages
//! println!("Failed pages: {:?}", state.failed_pages());
//! println!("Completion: {:.1}%", state.completion_percent());
//! ```

use serde::{Deserialize, Serialize};
use std::path::{Path, PathBuf};
use thiserror::Error;

// ============================================================
// Error Types
// ============================================================

/// Reprocess error types
#[derive(Debug, Error)]
pub enum ReprocessError {
    #[error("State file not found: {0}")]
    StateNotFound(PathBuf),

    #[error("Invalid state file: {0}")]
    InvalidState(String),

    #[error("Page index out of bounds: {0}")]
    PageIndexOutOfBounds(usize),

    #[error("No failed pages to reprocess")]
    NoFailedPages,

    #[error("IO error: {0}")]
    IoError(#[from] std::io::Error),

    #[error("JSON error: {0}")]
    JsonError(#[from] serde_json::Error),
}

pub type Result<T> = std::result::Result<T, ReprocessError>;

// ============================================================
// Data Structures
// ============================================================

/// Page processing status
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub enum PageStatus {
    /// Successfully processed
    Success {
        /// Path to cached processed image
        cached_path: PathBuf,
        /// Processing time in seconds
        processing_time: f64,
    },
    /// Failed with error
    Failed {
        /// Error message
        error: String,
        /// Number of retry attempts
        retry_count: u32,
    },
    /// Not yet processed
    Pending,
}

impl PageStatus {
    /// Check if status is Success
    pub fn is_success(&self) -> bool {
        matches!(self, PageStatus::Success { .. })
    }

    /// Check if status is Failed
    pub fn is_failed(&self) -> bool {
        matches!(self, PageStatus::Failed { .. })
    }

    /// Check if status is Pending
    pub fn is_pending(&self) -> bool {
        matches!(self, PageStatus::Pending)
    }

    /// Get retry count (0 for non-failed statuses)
    pub fn retry_count(&self) -> u32 {
        match self {
            PageStatus::Failed { retry_count, .. } => *retry_count,
            _ => 0,
        }
    }

    /// Create a new Success status
    pub fn success(cached_path: PathBuf, processing_time: f64) -> Self {
        PageStatus::Success {
            cached_path,
            processing_time,
        }
    }

    /// Create a new Failed status
    pub fn failed(error: impl Into<String>) -> Self {
        PageStatus::Failed {
            error: error.into(),
            retry_count: 0,
        }
    }

    /// Increment retry count for Failed status
    pub fn increment_retry(&mut self) {
        if let PageStatus::Failed { retry_count, .. } = self {
            *retry_count += 1;
        }
    }
}

impl Default for PageStatus {
    fn default() -> Self {
        PageStatus::Pending
    }
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
    /// Processing configuration hash (for cache invalidation)
    pub config_hash: String,
    /// Creation timestamp (ISO 8601)
    pub created_at: String,
    /// Last updated timestamp (ISO 8601)
    pub updated_at: String,
}

impl ReprocessState {
    /// Create a new ReprocessState for a PDF
    pub fn new(source_pdf: PathBuf, output_dir: PathBuf, page_count: usize, config_hash: String) -> Self {
        let now = chrono_now();
        Self {
            source_pdf,
            output_dir,
            pages: vec![PageStatus::Pending; page_count],
            config_hash,
            created_at: now.clone(),
            updated_at: now,
        }
    }

    /// Load state from JSON file
    pub fn load(path: &Path) -> Result<Self> {
        if !path.exists() {
            return Err(ReprocessError::StateNotFound(path.to_path_buf()));
        }
        let content = std::fs::read_to_string(path)?;
        let state: Self = serde_json::from_str(&content)?;
        Ok(state)
    }

    /// Save state to JSON file
    pub fn save(&self, path: &Path) -> Result<()> {
        let mut state = self.clone();
        state.updated_at = chrono_now();
        let content = serde_json::to_string_pretty(&state)?;
        std::fs::write(path, content)?;
        Ok(())
    }

    /// Get indices of failed pages
    pub fn failed_pages(&self) -> Vec<usize> {
        self.pages
            .iter()
            .enumerate()
            .filter_map(|(i, s)| if s.is_failed() { Some(i) } else { None })
            .collect()
    }

    /// Get indices of successful pages
    pub fn success_pages(&self) -> Vec<usize> {
        self.pages
            .iter()
            .enumerate()
            .filter_map(|(i, s)| if s.is_success() { Some(i) } else { None })
            .collect()
    }

    /// Get indices of pending pages
    pub fn pending_pages(&self) -> Vec<usize> {
        self.pages
            .iter()
            .enumerate()
            .filter_map(|(i, s)| if s.is_pending() { Some(i) } else { None })
            .collect()
    }

    /// Check if all pages are successfully processed
    pub fn is_complete(&self) -> bool {
        self.pages.iter().all(|s| s.is_success())
    }

    /// Get completion percentage (0.0 - 100.0)
    pub fn completion_percent(&self) -> f64 {
        if self.pages.is_empty() {
            return 100.0;
        }
        let success_count = self.pages.iter().filter(|s| s.is_success()).count();
        (success_count as f64 / self.pages.len() as f64) * 100.0
    }

    /// Get total page count
    pub fn page_count(&self) -> usize {
        self.pages.len()
    }

    /// Mark a page as successful
    pub fn mark_success(&mut self, page_idx: usize, cached_path: PathBuf, processing_time: f64) -> Result<()> {
        if page_idx >= self.pages.len() {
            return Err(ReprocessError::PageIndexOutOfBounds(page_idx));
        }
        self.pages[page_idx] = PageStatus::success(cached_path, processing_time);
        self.updated_at = chrono_now();
        Ok(())
    }

    /// Mark a page as failed
    pub fn mark_failed(&mut self, page_idx: usize, error: impl Into<String>) -> Result<()> {
        if page_idx >= self.pages.len() {
            return Err(ReprocessError::PageIndexOutOfBounds(page_idx));
        }
        let retry_count = self.pages[page_idx].retry_count();
        self.pages[page_idx] = PageStatus::Failed {
            error: error.into(),
            retry_count,
        };
        self.updated_at = chrono_now();
        Ok(())
    }

    /// Increment retry count for a page
    pub fn increment_retry(&mut self, page_idx: usize) -> Result<()> {
        if page_idx >= self.pages.len() {
            return Err(ReprocessError::PageIndexOutOfBounds(page_idx));
        }
        self.pages[page_idx].increment_retry();
        self.updated_at = chrono_now();
        Ok(())
    }

    /// Get cached paths for all successful pages
    pub fn cached_paths(&self) -> Vec<Option<PathBuf>> {
        self.pages
            .iter()
            .map(|s| match s {
                PageStatus::Success { cached_path, .. } => Some(cached_path.clone()),
                _ => None,
            })
            .collect()
    }

    /// Check if config has changed (requires reprocessing)
    pub fn config_changed(&self, new_hash: &str) -> bool {
        self.config_hash != new_hash
    }

    /// Invalidate all pages (mark as Pending)
    pub fn invalidate_all(&mut self) {
        for status in &mut self.pages {
            *status = PageStatus::Pending;
        }
        self.updated_at = chrono_now();
    }
}

impl Default for ReprocessState {
    fn default() -> Self {
        Self {
            source_pdf: PathBuf::new(),
            output_dir: PathBuf::new(),
            pages: vec![],
            config_hash: String::new(),
            created_at: chrono_now(),
            updated_at: chrono_now(),
        }
    }
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

impl ReprocessOptions {
    /// Create options to reprocess all failed pages
    pub fn all_failed() -> Self {
        Self::default()
    }

    /// Create options to reprocess specific pages
    pub fn specific_pages(pages: Vec<usize>) -> Self {
        Self {
            page_indices: pages,
            ..Default::default()
        }
    }

    /// Builder: set max retries
    pub fn with_max_retries(mut self, max_retries: u32) -> Self {
        self.max_retries = max_retries;
        self
    }

    /// Builder: set force flag
    pub fn with_force(mut self, force: bool) -> Self {
        self.force = force;
        self
    }

    /// Builder: set keep intermediates flag
    pub fn with_keep_intermediates(mut self, keep: bool) -> Self {
        self.keep_intermediates = keep;
        self
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

impl ReprocessResult {
    /// Check if processing is complete
    pub fn is_complete(&self) -> bool {
        self.failed_count == 0
    }

    /// Get completion percentage
    pub fn completion_percent(&self) -> f64 {
        if self.total_pages == 0 {
            return 100.0;
        }
        (self.success_count as f64 / self.total_pages as f64) * 100.0
    }
}

// ============================================================
// Helper Functions
// ============================================================

/// Get current timestamp in ISO 8601 format
fn chrono_now() -> String {
    use std::time::SystemTime;
    let now = SystemTime::now()
        .duration_since(SystemTime::UNIX_EPOCH)
        .unwrap_or_default();
    format!("{}Z", now.as_secs())
}

/// Calculate config hash from pipeline config
pub fn calculate_config_hash(config: &crate::PipelineConfig) -> String {
    use std::collections::hash_map::DefaultHasher;
    use std::hash::{Hash, Hasher};

    let mut hasher = DefaultHasher::new();
    format!("{:?}", config).hash(&mut hasher);
    format!("{:016x}", hasher.finish())
}

// ============================================================
// Tests
// ============================================================

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_page_status_default() {
        let status = PageStatus::default();
        assert!(status.is_pending());
    }

    #[test]
    fn test_page_status_success() {
        let status = PageStatus::success(PathBuf::from("test.png"), 1.5);
        assert!(status.is_success());
        assert!(!status.is_failed());
        assert!(!status.is_pending());
    }

    #[test]
    fn test_page_status_failed() {
        let status = PageStatus::failed("test error");
        assert!(status.is_failed());
        assert_eq!(status.retry_count(), 0);
    }

    #[test]
    fn test_page_status_increment_retry() {
        let mut status = PageStatus::failed("error");
        status.increment_retry();
        assert_eq!(status.retry_count(), 1);
        status.increment_retry();
        assert_eq!(status.retry_count(), 2);
    }

    #[test]
    fn test_reprocess_state_new() {
        let state = ReprocessState::new(
            PathBuf::from("test.pdf"),
            PathBuf::from("output"),
            5,
            "hash123".into(),
        );
        assert_eq!(state.page_count(), 5);
        assert_eq!(state.completion_percent(), 0.0);
        assert!(state.pending_pages().len() == 5);
    }

    #[test]
    fn test_reprocess_state_failed_pages() {
        let mut state = ReprocessState::new(
            PathBuf::from("test.pdf"),
            PathBuf::from("output"),
            5,
            "hash".into(),
        );
        state.pages[0] = PageStatus::success(PathBuf::new(), 0.0);
        state.pages[1] = PageStatus::failed("error1");
        state.pages[2] = PageStatus::success(PathBuf::new(), 0.0);
        state.pages[3] = PageStatus::failed("error2");
        state.pages[4] = PageStatus::Pending;

        let failed = state.failed_pages();
        assert_eq!(failed, vec![1, 3]);
    }

    #[test]
    fn test_reprocess_state_completion_percent() {
        let mut state = ReprocessState::new(
            PathBuf::from("test.pdf"),
            PathBuf::from("output"),
            4,
            "hash".into(),
        );
        state.pages[0] = PageStatus::success(PathBuf::new(), 0.0);
        state.pages[1] = PageStatus::success(PathBuf::new(), 0.0);
        state.pages[2] = PageStatus::failed("error");
        state.pages[3] = PageStatus::Pending;

        assert!((state.completion_percent() - 50.0).abs() < 0.01);
    }

    #[test]
    fn test_reprocess_state_is_complete() {
        let mut state = ReprocessState::new(
            PathBuf::from("test.pdf"),
            PathBuf::from("output"),
            2,
            "hash".into(),
        );
        assert!(!state.is_complete());

        state.pages[0] = PageStatus::success(PathBuf::new(), 0.0);
        assert!(!state.is_complete());

        state.pages[1] = PageStatus::success(PathBuf::new(), 0.0);
        assert!(state.is_complete());
    }

    #[test]
    fn test_reprocess_state_mark_success() {
        let mut state = ReprocessState::new(
            PathBuf::from("test.pdf"),
            PathBuf::from("output"),
            3,
            "hash".into(),
        );

        state.mark_success(1, PathBuf::from("cached.png"), 2.5).unwrap();

        assert!(state.pages[1].is_success());
        if let PageStatus::Success { cached_path, processing_time } = &state.pages[1] {
            assert_eq!(cached_path.to_str().unwrap(), "cached.png");
            assert!((processing_time - 2.5).abs() < 0.01);
        }
    }

    #[test]
    fn test_reprocess_state_mark_failed() {
        let mut state = ReprocessState::new(
            PathBuf::from("test.pdf"),
            PathBuf::from("output"),
            3,
            "hash".into(),
        );

        state.mark_failed(0, "test error").unwrap();

        assert!(state.pages[0].is_failed());
        if let PageStatus::Failed { error, retry_count } = &state.pages[0] {
            assert_eq!(error, "test error");
            assert_eq!(*retry_count, 0);
        }
    }

    #[test]
    fn test_reprocess_state_page_index_out_of_bounds() {
        let mut state = ReprocessState::new(
            PathBuf::from("test.pdf"),
            PathBuf::from("output"),
            3,
            "hash".into(),
        );

        let result = state.mark_success(10, PathBuf::new(), 0.0);
        assert!(matches!(result, Err(ReprocessError::PageIndexOutOfBounds(10))));
    }

    #[test]
    fn test_reprocess_state_save_load() {
        let temp_dir = tempfile::tempdir().unwrap();
        let state_path = temp_dir.path().join("state.json");

        let mut state = ReprocessState::new(
            PathBuf::from("test.pdf"),
            PathBuf::from("output"),
            3,
            "testhash".into(),
        );
        state.pages[0] = PageStatus::success(PathBuf::from("p0.png"), 1.0);
        state.pages[1] = PageStatus::failed("error");
        state.pages[2] = PageStatus::Pending;

        state.save(&state_path).unwrap();
        let loaded = ReprocessState::load(&state_path).unwrap();

        assert_eq!(loaded.source_pdf, state.source_pdf);
        assert_eq!(loaded.config_hash, state.config_hash);
        assert_eq!(loaded.pages.len(), 3);
        assert!(loaded.pages[0].is_success());
        assert!(loaded.pages[1].is_failed());
        assert!(loaded.pages[2].is_pending());
    }

    #[test]
    fn test_reprocess_state_config_changed() {
        let state = ReprocessState::new(
            PathBuf::from("test.pdf"),
            PathBuf::from("output"),
            1,
            "hash_v1".into(),
        );

        assert!(!state.config_changed("hash_v1"));
        assert!(state.config_changed("hash_v2"));
    }

    #[test]
    fn test_reprocess_state_invalidate_all() {
        let mut state = ReprocessState::new(
            PathBuf::from("test.pdf"),
            PathBuf::from("output"),
            3,
            "hash".into(),
        );
        state.pages[0] = PageStatus::success(PathBuf::new(), 0.0);
        state.pages[1] = PageStatus::failed("error");

        state.invalidate_all();

        assert!(state.pages.iter().all(|s| s.is_pending()));
    }

    #[test]
    fn test_reprocess_options_default() {
        let opts = ReprocessOptions::default();
        assert_eq!(opts.max_retries, 3);
        assert!(opts.page_indices.is_empty());
        assert!(!opts.force);
    }

    #[test]
    fn test_reprocess_options_specific_pages() {
        let opts = ReprocessOptions::specific_pages(vec![1, 3, 5]);
        assert_eq!(opts.page_indices, vec![1, 3, 5]);
    }

    #[test]
    fn test_reprocess_options_builder() {
        let opts = ReprocessOptions::all_failed()
            .with_max_retries(5)
            .with_force(true)
            .with_keep_intermediates(true);

        assert_eq!(opts.max_retries, 5);
        assert!(opts.force);
        assert!(opts.keep_intermediates);
    }

    #[test]
    fn test_reprocess_result_is_complete() {
        let result = ReprocessResult {
            total_pages: 10,
            success_count: 10,
            failed_count: 0,
            reprocessed_count: 2,
            output_path: Some(PathBuf::from("output.pdf")),
            failed_pages: vec![],
        };
        assert!(result.is_complete());

        let result_incomplete = ReprocessResult {
            total_pages: 10,
            success_count: 8,
            failed_count: 2,
            reprocessed_count: 2,
            output_path: None,
            failed_pages: vec![3, 7],
        };
        assert!(!result_incomplete.is_complete());
    }

    #[test]
    fn test_reprocess_result_completion_percent() {
        let result = ReprocessResult {
            total_pages: 10,
            success_count: 7,
            failed_count: 3,
            reprocessed_count: 0,
            output_path: None,
            failed_pages: vec![],
        };
        assert!((result.completion_percent() - 70.0).abs() < 0.01);
    }

    #[test]
    fn test_reprocess_state_cached_paths() {
        let mut state = ReprocessState::new(
            PathBuf::from("test.pdf"),
            PathBuf::from("output"),
            3,
            "hash".into(),
        );
        state.pages[0] = PageStatus::success(PathBuf::from("p0.png"), 0.0);
        state.pages[1] = PageStatus::failed("error");
        state.pages[2] = PageStatus::success(PathBuf::from("p2.png"), 0.0);

        let cached = state.cached_paths();
        assert_eq!(cached.len(), 3);
        assert_eq!(cached[0], Some(PathBuf::from("p0.png")));
        assert_eq!(cached[1], None);
        assert_eq!(cached[2], Some(PathBuf::from("p2.png")));
    }

    #[test]
    fn test_page_status_serialization() {
        let success = PageStatus::success(PathBuf::from("test.png"), 1.5);
        let json = serde_json::to_string(&success).unwrap();
        let deserialized: PageStatus = serde_json::from_str(&json).unwrap();
        assert!(deserialized.is_success());
    }

    #[test]
    fn test_reprocess_state_empty() {
        let state = ReprocessState::new(
            PathBuf::from("test.pdf"),
            PathBuf::from("output"),
            0,
            "hash".into(),
        );
        assert!(state.is_complete());
        assert_eq!(state.completion_percent(), 100.0);
    }

    #[test]
    fn test_increment_retry_preserves_error() {
        let mut state = ReprocessState::new(
            PathBuf::from("test.pdf"),
            PathBuf::from("output"),
            1,
            "hash".into(),
        );
        state.pages[0] = PageStatus::Failed {
            error: "original error".into(),
            retry_count: 0,
        };

        state.increment_retry(0).unwrap();

        if let PageStatus::Failed { error, retry_count } = &state.pages[0] {
            assert_eq!(error, "original error");
            assert_eq!(*retry_count, 1);
        }
    }
}
