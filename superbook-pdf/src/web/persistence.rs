//! Job persistence for the web server
//!
//! Provides job storage and recovery capabilities.

use chrono::{DateTime, Utc};
use serde::{Deserialize, Serialize};
use std::collections::HashMap;
use std::path::PathBuf;
use std::sync::{Arc, RwLock};
use uuid::Uuid;

use super::job::{Job, JobStatus};

/// Storage backend type
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "lowercase")]
pub enum StorageBackend {
    /// JSON file storage (simple, portable)
    #[default]
    Json,
    /// SQLite database (recommended for production)
    Sqlite,
}

/// Persistence configuration
#[derive(Debug, Clone)]
pub struct PersistenceConfig {
    /// Enable persistence
    pub enabled: bool,
    /// Storage directory path
    pub storage_path: PathBuf,
    /// Storage backend type
    pub backend: StorageBackend,
    /// Auto-save interval in seconds
    pub auto_save_interval: u64,
    /// History retention in days
    pub retention_days: u32,
}

impl Default for PersistenceConfig {
    fn default() -> Self {
        Self {
            enabled: false,
            storage_path: PathBuf::from("./data"),
            backend: StorageBackend::Json,
            auto_save_interval: 30,
            retention_days: 30,
        }
    }
}

impl PersistenceConfig {
    /// Create an enabled config with default settings
    pub fn enabled() -> Self {
        Self {
            enabled: true,
            ..Default::default()
        }
    }

    /// Set storage path
    pub fn with_path(mut self, path: impl Into<PathBuf>) -> Self {
        self.storage_path = path.into();
        self
    }

    /// Set backend type
    pub fn with_backend(mut self, backend: StorageBackend) -> Self {
        self.backend = backend;
        self
    }
}

/// Store error type
#[derive(Debug, thiserror::Error)]
pub enum StoreError {
    #[error("IO error: {0}")]
    Io(#[from] std::io::Error),
    #[error("Serialization error: {0}")]
    Serialization(#[from] serde_json::Error),
    #[error("Job not found: {0}")]
    NotFound(Uuid),
    #[error("Storage error: {0}")]
    Storage(String),
}

/// Job store trait for different storage backends
pub trait JobStore: Send + Sync {
    /// Save a job
    fn save(&self, job: &Job) -> Result<(), StoreError>;
    /// Get a job by ID
    fn get(&self, id: Uuid) -> Result<Option<Job>, StoreError>;
    /// List all jobs
    fn list(&self) -> Result<Vec<Job>, StoreError>;
    /// Delete a job
    fn delete(&self, id: Uuid) -> Result<(), StoreError>;
    /// Get pending (non-terminal) jobs
    fn get_pending(&self) -> Result<Vec<Job>, StoreError>;
    /// Cleanup old jobs
    fn cleanup(&self, older_than: DateTime<Utc>) -> Result<usize, StoreError>;
    /// Flush to disk
    fn flush(&self) -> Result<(), StoreError>;
}

/// Stored jobs data structure
#[derive(Debug, Clone, Serialize, Deserialize)]
struct StoredJobs {
    version: u32,
    jobs: HashMap<Uuid, Job>,
}

impl Default for StoredJobs {
    fn default() -> Self {
        Self {
            version: 1,
            jobs: HashMap::new(),
        }
    }
}

/// JSON file-based job store
pub struct JsonJobStore {
    path: PathBuf,
    cache: RwLock<HashMap<Uuid, Job>>,
    dirty: RwLock<bool>,
}

impl JsonJobStore {
    /// Create a new JSON job store
    pub fn new(path: impl Into<PathBuf>) -> Result<Self, StoreError> {
        let path = path.into();

        // Ensure directory exists
        if let Some(parent) = path.parent() {
            std::fs::create_dir_all(parent)?;
        }

        let store = Self {
            path,
            cache: RwLock::new(HashMap::new()),
            dirty: RwLock::new(false),
        };

        // Load existing data if present
        store.load()?;

        Ok(store)
    }

    /// Load jobs from file
    pub fn load(&self) -> Result<(), StoreError> {
        if !self.path.exists() {
            return Ok(());
        }

        let content = std::fs::read_to_string(&self.path)?;
        let stored: StoredJobs = serde_json::from_str(&content)?;

        let mut cache = self.cache.write().map_err(|e| {
            StoreError::Storage(format!("Lock error: {}", e))
        })?;
        *cache = stored.jobs;

        Ok(())
    }

    /// Check if there are unsaved changes
    pub fn is_dirty(&self) -> bool {
        *self.dirty.read().unwrap_or_else(|e| e.into_inner())
    }

    /// Get the storage path
    pub fn path(&self) -> &PathBuf {
        &self.path
    }

    /// Get the number of stored jobs
    pub fn len(&self) -> usize {
        self.cache.read().map(|c| c.len()).unwrap_or(0)
    }

    /// Check if store is empty
    pub fn is_empty(&self) -> bool {
        self.len() == 0
    }
}

impl JobStore for JsonJobStore {
    fn save(&self, job: &Job) -> Result<(), StoreError> {
        let mut cache = self.cache.write().map_err(|e| {
            StoreError::Storage(format!("Lock error: {}", e))
        })?;
        cache.insert(job.id, job.clone());

        let mut dirty = self.dirty.write().map_err(|e| {
            StoreError::Storage(format!("Lock error: {}", e))
        })?;
        *dirty = true;

        Ok(())
    }

    fn get(&self, id: Uuid) -> Result<Option<Job>, StoreError> {
        let cache = self.cache.read().map_err(|e| {
            StoreError::Storage(format!("Lock error: {}", e))
        })?;
        Ok(cache.get(&id).cloned())
    }

    fn list(&self) -> Result<Vec<Job>, StoreError> {
        let cache = self.cache.read().map_err(|e| {
            StoreError::Storage(format!("Lock error: {}", e))
        })?;
        Ok(cache.values().cloned().collect())
    }

    fn delete(&self, id: Uuid) -> Result<(), StoreError> {
        let mut cache = self.cache.write().map_err(|e| {
            StoreError::Storage(format!("Lock error: {}", e))
        })?;
        cache.remove(&id);

        let mut dirty = self.dirty.write().map_err(|e| {
            StoreError::Storage(format!("Lock error: {}", e))
        })?;
        *dirty = true;

        Ok(())
    }

    fn get_pending(&self) -> Result<Vec<Job>, StoreError> {
        let cache = self.cache.read().map_err(|e| {
            StoreError::Storage(format!("Lock error: {}", e))
        })?;

        Ok(cache
            .values()
            .filter(|job| !job.is_terminal())
            .cloned()
            .collect())
    }

    fn cleanup(&self, older_than: DateTime<Utc>) -> Result<usize, StoreError> {
        let mut cache = self.cache.write().map_err(|e| {
            StoreError::Storage(format!("Lock error: {}", e))
        })?;

        let before = cache.len();
        cache.retain(|_, job| {
            if let Some(completed_at) = job.completed_at {
                completed_at > older_than
            } else {
                true // Keep non-completed jobs
            }
        });
        let removed = before - cache.len();

        if removed > 0 {
            let mut dirty = self.dirty.write().map_err(|e| {
                StoreError::Storage(format!("Lock error: {}", e))
            })?;
            *dirty = true;
        }

        Ok(removed)
    }

    fn flush(&self) -> Result<(), StoreError> {
        let cache = self.cache.read().map_err(|e| {
            StoreError::Storage(format!("Lock error: {}", e))
        })?;

        let stored = StoredJobs {
            version: 1,
            jobs: cache.clone(),
        };

        let content = serde_json::to_string_pretty(&stored)?;
        std::fs::write(&self.path, content)?;

        let mut dirty = self.dirty.write().map_err(|e| {
            StoreError::Storage(format!("Lock error: {}", e))
        })?;
        *dirty = false;

        Ok(())
    }
}

/// Recovery result
#[derive(Debug, Clone, Default)]
pub struct RecoveryResult {
    /// Number of recovered jobs
    pub recovered: usize,
    /// Number of jobs requeued
    pub requeued: usize,
    /// Number of failed recoveries
    pub failed: usize,
}

/// Recovery manager for job persistence
///
/// Handles startup recovery, requeuing of interrupted jobs,
/// and retry of failed jobs.
pub struct RecoveryManager {
    store: Arc<dyn JobStore>,
    queue: super::job::JobQueue,
}

impl RecoveryManager {
    /// Create a new recovery manager
    pub fn new(store: Arc<dyn JobStore>, queue: super::job::JobQueue) -> Self {
        Self { store, queue }
    }

    /// Recover jobs on server startup
    ///
    /// This method:
    /// 1. Loads all pending jobs from the store
    /// 2. Requeues jobs that were processing (interrupted by crash)
    /// 3. Returns a summary of the recovery
    pub async fn recover_on_startup(&self) -> RecoveryResult {
        let mut result = RecoveryResult::default();

        // Get all pending jobs from the store
        let pending_jobs = match self.store.get_pending() {
            Ok(jobs) => jobs,
            Err(e) => {
                eprintln!("Failed to load pending jobs: {}", e);
                result.failed = 1;
                return result;
            }
        };

        result.recovered = pending_jobs.len();

        for job in pending_jobs {
            // Resubmit to queue
            self.queue.submit(job.clone());

            // If job was processing, it needs to be restarted
            if job.status == JobStatus::Processing {
                result.requeued += 1;
            }
        }

        result
    }

    /// Requeue all jobs that were in processing state
    ///
    /// This handles the case where jobs were interrupted by a crash
    /// while they were being processed.
    pub async fn requeue_processing(&self) -> usize {
        let pending_jobs = match self.store.get_pending() {
            Ok(jobs) => jobs,
            Err(_) => return 0,
        };

        let mut requeued = 0;
        for job in pending_jobs {
            if job.status == JobStatus::Processing {
                // Reset to queued and resubmit
                let mut reset_job = job.clone();
                reset_job.status = JobStatus::Queued;
                reset_job.started_at = None;
                reset_job.progress = None;

                self.queue.submit(reset_job);
                requeued += 1;
            }
        }

        requeued
    }

    /// Retry failed jobs up to max_retries
    ///
    /// Returns the number of jobs that were requeued for retry.
    pub async fn retry_failed(&self, max_retries: u32) -> usize {
        let all_jobs = match self.store.list() {
            Ok(jobs) => jobs,
            Err(_) => return 0,
        };

        let mut retried = 0;
        for job in all_jobs {
            if job.status == JobStatus::Failed {
                // Check retry count (stored in a simple way for now)
                let retry_count = job.error.as_ref()
                    .and_then(|e| e.strip_prefix("Retry "))
                    .and_then(|s| s.chars().next())
                    .and_then(|c| c.to_digit(10))
                    .unwrap_or(0);

                if retry_count < max_retries {
                    // Create a new job for retry
                    let mut retry_job = Job::new(&job.input_filename, job.options.clone());
                    retry_job.error = Some(format!("Retry {}: {}", retry_count + 1,
                        job.error.as_deref().unwrap_or("Unknown error")));

                    self.queue.submit(retry_job);
                    retried += 1;
                }
            }
        }

        retried
    }

    /// Get the underlying job store
    pub fn store(&self) -> &Arc<dyn JobStore> {
        &self.store
    }

    /// Get the job queue
    pub fn queue(&self) -> &super::job::JobQueue {
        &self.queue
    }
}

/// Job history query parameters
#[derive(Debug, Clone, Deserialize)]
pub struct HistoryQuery {
    /// Maximum number of jobs to return
    #[serde(default = "default_limit")]
    pub limit: usize,
    /// Offset for pagination
    #[serde(default)]
    pub offset: usize,
    /// Filter by status
    pub status: Option<JobStatus>,
}

fn default_limit() -> usize {
    50
}

impl Default for HistoryQuery {
    fn default() -> Self {
        Self {
            limit: default_limit(),
            offset: 0,
            status: None,
        }
    }
}

/// Job history response
#[derive(Debug, Clone, Serialize)]
pub struct HistoryResponse {
    /// Jobs in this page
    pub jobs: Vec<Job>,
    /// Total number of jobs
    pub total: usize,
    /// Current limit
    pub limit: usize,
    /// Current offset
    pub offset: usize,
}

/// Retry response
#[derive(Debug, Clone, Serialize)]
pub struct RetryResponse {
    /// Job ID
    pub job_id: Uuid,
    /// New status
    pub status: String,
    /// Message
    pub message: String,
}

impl RetryResponse {
    /// Create a success response
    pub fn success(job_id: Uuid) -> Self {
        Self {
            job_id,
            status: "queued".to_string(),
            message: "Job requeued for processing".to_string(),
        }
    }

    /// Create an error response
    pub fn error(job_id: Uuid, message: impl Into<String>) -> Self {
        Self {
            job_id,
            status: "error".to_string(),
            message: message.into(),
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::web::job::ConvertOptions;
    use tempfile::tempdir;

    // PERSIST-001: PersistenceConfig デフォルト値
    #[test]
    fn test_persistence_config_default() {
        let config = PersistenceConfig::default();
        assert!(!config.enabled);
        assert_eq!(config.backend, StorageBackend::Json);
        assert_eq!(config.auto_save_interval, 30);
        assert_eq!(config.retention_days, 30);
    }

    #[test]
    fn test_persistence_config_enabled() {
        let config = PersistenceConfig::enabled();
        assert!(config.enabled);
    }

    #[test]
    fn test_persistence_config_builder() {
        let config = PersistenceConfig::enabled()
            .with_path("/custom/path")
            .with_backend(StorageBackend::Sqlite);
        assert!(config.enabled);
        assert_eq!(config.storage_path, PathBuf::from("/custom/path"));
        assert_eq!(config.backend, StorageBackend::Sqlite);
    }

    // PERSIST-002: JsonJobStore 作成
    #[test]
    fn test_json_store_new() {
        let dir = tempdir().unwrap();
        let path = dir.path().join("jobs.json");
        let store = JsonJobStore::new(&path).unwrap();
        assert!(store.is_empty());
        assert_eq!(store.path(), &path);
    }

    // PERSIST-003: ジョブ保存
    #[test]
    fn test_json_store_save() {
        let dir = tempdir().unwrap();
        let path = dir.path().join("jobs.json");
        let store = JsonJobStore::new(&path).unwrap();

        let job = Job::new("test.pdf", ConvertOptions::default());
        store.save(&job).unwrap();

        assert_eq!(store.len(), 1);
        assert!(store.is_dirty());
    }

    // PERSIST-004: ジョブ取得
    #[test]
    fn test_json_store_get() {
        let dir = tempdir().unwrap();
        let path = dir.path().join("jobs.json");
        let store = JsonJobStore::new(&path).unwrap();

        let job = Job::new("test.pdf", ConvertOptions::default());
        let id = job.id;
        store.save(&job).unwrap();

        let retrieved = store.get(id).unwrap();
        assert!(retrieved.is_some());
        assert_eq!(retrieved.unwrap().id, id);

        let not_found = store.get(Uuid::new_v4()).unwrap();
        assert!(not_found.is_none());
    }

    // PERSIST-005: ジョブ一覧取得
    #[test]
    fn test_json_store_list() {
        let dir = tempdir().unwrap();
        let path = dir.path().join("jobs.json");
        let store = JsonJobStore::new(&path).unwrap();

        store.save(&Job::new("test1.pdf", ConvertOptions::default())).unwrap();
        store.save(&Job::new("test2.pdf", ConvertOptions::default())).unwrap();
        store.save(&Job::new("test3.pdf", ConvertOptions::default())).unwrap();

        let jobs = store.list().unwrap();
        assert_eq!(jobs.len(), 3);
    }

    // PERSIST-006: ジョブ削除
    #[test]
    fn test_json_store_delete() {
        let dir = tempdir().unwrap();
        let path = dir.path().join("jobs.json");
        let store = JsonJobStore::new(&path).unwrap();

        let job = Job::new("test.pdf", ConvertOptions::default());
        let id = job.id;
        store.save(&job).unwrap();
        assert_eq!(store.len(), 1);

        store.delete(id).unwrap();
        assert_eq!(store.len(), 0);
        assert!(store.get(id).unwrap().is_none());
    }

    // PERSIST-007: 未完了ジョブ取得
    #[test]
    fn test_json_store_get_pending() {
        let dir = tempdir().unwrap();
        let path = dir.path().join("jobs.json");
        let store = JsonJobStore::new(&path).unwrap();

        let mut completed = Job::new("completed.pdf", ConvertOptions::default());
        completed.complete(PathBuf::from("/out/completed.pdf"));

        let queued = Job::new("queued.pdf", ConvertOptions::default());

        let mut processing = Job::new("processing.pdf", ConvertOptions::default());
        processing.start();

        store.save(&completed).unwrap();
        store.save(&queued).unwrap();
        store.save(&processing).unwrap();

        let pending = store.get_pending().unwrap();
        assert_eq!(pending.len(), 2); // queued + processing
    }

    // PERSIST-008: ファイル永続化
    #[test]
    fn test_json_store_flush() {
        let dir = tempdir().unwrap();
        let path = dir.path().join("jobs.json");
        let store = JsonJobStore::new(&path).unwrap();

        let job = Job::new("test.pdf", ConvertOptions::default());
        store.save(&job).unwrap();
        store.flush().unwrap();

        assert!(path.exists());
        assert!(!store.is_dirty());
    }

    // PERSIST-009: ファイル読み込み
    #[test]
    fn test_json_store_load() {
        let dir = tempdir().unwrap();
        let path = dir.path().join("jobs.json");

        let job_id;
        {
            let store = JsonJobStore::new(&path).unwrap();
            let job = Job::new("test.pdf", ConvertOptions::default());
            job_id = job.id;
            store.save(&job).unwrap();
            store.flush().unwrap();
        }

        // Create new store and load
        let store2 = JsonJobStore::new(&path).unwrap();
        assert_eq!(store2.len(), 1);

        let job = store2.get(job_id).unwrap();
        assert!(job.is_some());
        assert_eq!(job.unwrap().input_filename, "test.pdf");
    }

    // PERSIST-010: 古いジョブクリーンアップ
    #[test]
    fn test_json_store_cleanup() {
        let dir = tempdir().unwrap();
        let path = dir.path().join("jobs.json");
        let store = JsonJobStore::new(&path).unwrap();

        // Create an old completed job
        let mut old_job = Job::new("old.pdf", ConvertOptions::default());
        old_job.complete(PathBuf::from("/out/old.pdf"));
        // Manually set completed_at to old date
        old_job.completed_at = Some(Utc::now() - chrono::Duration::days(60));

        // Create a recent completed job
        let mut recent_job = Job::new("recent.pdf", ConvertOptions::default());
        recent_job.complete(PathBuf::from("/out/recent.pdf"));

        // Create a pending job
        let pending_job = Job::new("pending.pdf", ConvertOptions::default());

        store.save(&old_job).unwrap();
        store.save(&recent_job).unwrap();
        store.save(&pending_job).unwrap();

        assert_eq!(store.len(), 3);

        // Cleanup jobs older than 30 days
        let cutoff = Utc::now() - chrono::Duration::days(30);
        let removed = store.cleanup(cutoff).unwrap();

        assert_eq!(removed, 1);
        assert_eq!(store.len(), 2);
    }

    #[test]
    fn test_storage_backend_default() {
        assert_eq!(StorageBackend::default(), StorageBackend::Json);
    }

    #[test]
    fn test_history_query_default() {
        let query = HistoryQuery::default();
        assert_eq!(query.limit, 50);
        assert_eq!(query.offset, 0);
        assert!(query.status.is_none());
    }

    #[test]
    fn test_history_response_serialize() {
        let response = HistoryResponse {
            jobs: vec![],
            total: 100,
            limit: 50,
            offset: 0,
        };
        let json = serde_json::to_string(&response).unwrap();
        assert!(json.contains("\"total\":100"));
        assert!(json.contains("\"limit\":50"));
    }

    #[test]
    fn test_retry_response_success() {
        let id = Uuid::new_v4();
        let response = RetryResponse::success(id);
        assert_eq!(response.job_id, id);
        assert_eq!(response.status, "queued");
    }

    #[test]
    fn test_retry_response_error() {
        let id = Uuid::new_v4();
        let response = RetryResponse::error(id, "Not retryable");
        assert_eq!(response.status, "error");
        assert!(response.message.contains("Not retryable"));
    }

    #[test]
    fn test_recovery_result_default() {
        let result = RecoveryResult::default();
        assert_eq!(result.recovered, 0);
        assert_eq!(result.requeued, 0);
        assert_eq!(result.failed, 0);
    }

    #[test]
    fn test_store_error_display() {
        let err = StoreError::NotFound(Uuid::new_v4());
        assert!(err.to_string().contains("not found"));

        let err = StoreError::Storage("test error".to_string());
        assert!(err.to_string().contains("test error"));
    }

    // PERSIST-011: 起動時リカバリー
    #[tokio::test]
    async fn test_recovery_manager_new() {
        let dir = tempdir().unwrap();
        let path = dir.path().join("jobs.json");
        let store: Arc<dyn JobStore> = Arc::new(JsonJobStore::new(&path).unwrap());
        let queue = crate::web::job::JobQueue::new();

        let manager = RecoveryManager::new(store.clone(), queue.clone());

        assert!(manager.store().list().unwrap().is_empty());
    }

    #[tokio::test]
    async fn test_recovery_manager_recover_on_startup() {
        let dir = tempdir().unwrap();
        let path = dir.path().join("jobs.json");
        let store = Arc::new(JsonJobStore::new(&path).unwrap());
        let queue = crate::web::job::JobQueue::new();

        // Add some pending jobs to the store
        let queued_job = Job::new("queued.pdf", ConvertOptions::default());
        let mut processing_job = Job::new("processing.pdf", ConvertOptions::default());
        processing_job.start();

        store.save(&queued_job).unwrap();
        store.save(&processing_job).unwrap();

        let manager = RecoveryManager::new(store as Arc<dyn JobStore>, queue.clone());
        let result = manager.recover_on_startup().await;

        assert_eq!(result.recovered, 2);
        assert_eq!(result.requeued, 1); // Only processing job
        assert_eq!(result.failed, 0);
        assert_eq!(queue.pending_count(), 2);
    }

    // PERSIST-012: 処理中ジョブ再キュー
    #[tokio::test]
    async fn test_recovery_manager_requeue_processing() {
        let dir = tempdir().unwrap();
        let path = dir.path().join("jobs.json");
        let store = Arc::new(JsonJobStore::new(&path).unwrap());
        let queue = crate::web::job::JobQueue::new();

        // Add a processing job
        let mut processing_job = Job::new("processing.pdf", ConvertOptions::default());
        processing_job.start();
        store.save(&processing_job).unwrap();

        // Add a queued job (should not be affected)
        let queued_job = Job::new("queued.pdf", ConvertOptions::default());
        store.save(&queued_job).unwrap();

        let manager = RecoveryManager::new(store as Arc<dyn JobStore>, queue.clone());
        let requeued = manager.requeue_processing().await;

        assert_eq!(requeued, 1);
    }

    #[tokio::test]
    async fn test_recovery_manager_retry_failed() {
        let dir = tempdir().unwrap();
        let path = dir.path().join("jobs.json");
        let store = Arc::new(JsonJobStore::new(&path).unwrap());
        let queue = crate::web::job::JobQueue::new();

        // Add a failed job
        let mut failed_job = Job::new("failed.pdf", ConvertOptions::default());
        failed_job.fail("Initial failure");
        store.save(&failed_job).unwrap();

        // Add a completed job (should not be retried)
        let mut completed_job = Job::new("completed.pdf", ConvertOptions::default());
        completed_job.complete(PathBuf::from("/out/completed.pdf"));
        store.save(&completed_job).unwrap();

        let manager = RecoveryManager::new(store as Arc<dyn JobStore>, queue.clone());
        let retried = manager.retry_failed(3).await;

        assert_eq!(retried, 1);
        assert_eq!(queue.pending_count(), 1);
    }

    #[tokio::test]
    async fn test_recovery_manager_accessors() {
        let dir = tempdir().unwrap();
        let path = dir.path().join("jobs.json");
        let store: Arc<dyn JobStore> = Arc::new(JsonJobStore::new(&path).unwrap());
        let queue = crate::web::job::JobQueue::new();

        let manager = RecoveryManager::new(store.clone(), queue.clone());

        assert!(Arc::ptr_eq(manager.store(), &store));
    }
}
