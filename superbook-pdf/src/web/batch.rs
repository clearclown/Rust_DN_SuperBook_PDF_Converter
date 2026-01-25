//! Batch processing module for handling multiple PDF conversions
//!
//! Provides batch job management, progress tracking, and scheduling.
//!
//! Spec Reference: specs/22-batch.spec.md

use chrono::{DateTime, Utc};
use serde::{Deserialize, Serialize};
use std::collections::HashMap;
use std::sync::Arc;
use tokio::sync::RwLock;
use uuid::Uuid;

use super::job::{ConvertOptions, Job, JobQueue, JobStatus};

/// Batch job status
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "snake_case")]
pub enum BatchStatus {
    /// Waiting in queue
    Queued,
    /// Currently processing
    Processing,
    /// All jobs completed successfully
    Completed,
    /// Some jobs completed, some failed
    PartiallyCompleted,
    /// All jobs failed
    Failed,
    /// Batch was cancelled
    Cancelled,
}

impl std::fmt::Display for BatchStatus {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        match self {
            BatchStatus::Queued => write!(f, "queued"),
            BatchStatus::Processing => write!(f, "processing"),
            BatchStatus::Completed => write!(f, "completed"),
            BatchStatus::PartiallyCompleted => write!(f, "partially_completed"),
            BatchStatus::Failed => write!(f, "failed"),
            BatchStatus::Cancelled => write!(f, "cancelled"),
        }
    }
}

/// Job priority level
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize, Default)]
#[serde(rename_all = "snake_case")]
pub enum Priority {
    /// Low priority
    Low,
    /// Normal priority (default)
    #[default]
    Normal,
    /// High priority
    High,
}

impl Priority {
    /// Get numeric value for sorting (higher = more priority)
    pub fn value(&self) -> u8 {
        match self {
            Priority::Low => 0,
            Priority::Normal => 1,
            Priority::High => 2,
        }
    }
}

/// Batch progress information
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct BatchProgress {
    /// Number of completed jobs
    pub completed: usize,
    /// Number of currently processing jobs
    pub processing: usize,
    /// Number of pending jobs
    pub pending: usize,
    /// Number of failed jobs
    pub failed: usize,
    /// Total number of jobs
    pub total: usize,
}

impl BatchProgress {
    /// Create new progress tracker
    pub fn new(total: usize) -> Self {
        Self {
            completed: 0,
            processing: 0,
            pending: total,
            failed: 0,
            total,
        }
    }

    /// Calculate completion percentage
    pub fn percent(&self) -> u8 {
        if self.total == 0 {
            100
        } else {
            ((self.completed as f32 / self.total as f32) * 100.0) as u8
        }
    }

    /// Check if batch is complete
    pub fn is_complete(&self) -> bool {
        self.completed + self.failed == self.total
    }
}

/// Batch job containing multiple individual jobs
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct BatchJob {
    /// Unique batch identifier
    pub id: Uuid,
    /// Current batch status
    pub status: BatchStatus,
    /// Conversion options applied to all jobs
    pub options: ConvertOptions,
    /// List of individual job IDs
    pub job_ids: Vec<Uuid>,
    /// Job priority
    pub priority: Priority,
    /// Creation timestamp
    pub created_at: DateTime<Utc>,
    /// Processing start timestamp
    pub started_at: Option<DateTime<Utc>>,
    /// Completion timestamp
    pub completed_at: Option<DateTime<Utc>>,
}

impl BatchJob {
    /// Create a new batch job
    pub fn new(options: ConvertOptions, priority: Priority) -> Self {
        Self {
            id: Uuid::new_v4(),
            status: BatchStatus::Queued,
            options,
            job_ids: Vec::new(),
            priority,
            created_at: Utc::now(),
            started_at: None,
            completed_at: None,
        }
    }

    /// Add a job to the batch
    pub fn add_job(&mut self, job_id: Uuid) {
        self.job_ids.push(job_id);
    }

    /// Get the number of jobs in the batch
    pub fn job_count(&self) -> usize {
        self.job_ids.len()
    }

    /// Start processing the batch
    pub fn start(&mut self) {
        self.status = BatchStatus::Processing;
        self.started_at = Some(Utc::now());
    }

    /// Mark batch as completed
    pub fn complete(&mut self, has_failures: bool) {
        self.status = if has_failures {
            BatchStatus::PartiallyCompleted
        } else {
            BatchStatus::Completed
        };
        self.completed_at = Some(Utc::now());
    }

    /// Mark batch as failed
    pub fn fail(&mut self) {
        self.status = BatchStatus::Failed;
        self.completed_at = Some(Utc::now());
    }

    /// Cancel the batch
    pub fn cancel(&mut self) {
        self.status = BatchStatus::Cancelled;
        self.completed_at = Some(Utc::now());
    }
}

/// Batch queue for managing multiple batch jobs
#[derive(Clone)]
pub struct BatchQueue {
    batches: Arc<RwLock<HashMap<Uuid, BatchJob>>>,
    job_queue: JobQueue,
}

impl BatchQueue {
    /// Create a new batch queue
    pub fn new(job_queue: JobQueue) -> Self {
        Self {
            batches: Arc::new(RwLock::new(HashMap::new())),
            job_queue,
        }
    }

    /// Submit a new batch job
    pub async fn submit(&self, batch: BatchJob) -> Uuid {
        let batch_id = batch.id;
        let mut batches = self.batches.write().await;
        batches.insert(batch_id, batch);
        batch_id
    }

    /// Get a batch job by ID
    pub async fn get(&self, batch_id: Uuid) -> Option<BatchJob> {
        let batches = self.batches.read().await;
        batches.get(&batch_id).cloned()
    }

    /// Update a batch job
    pub async fn update<F>(&self, batch_id: Uuid, f: F)
    where
        F: FnOnce(&mut BatchJob),
    {
        let mut batches = self.batches.write().await;
        if let Some(batch) = batches.get_mut(&batch_id) {
            f(batch);
        }
    }

    /// Cancel a batch and all its jobs
    pub async fn cancel(&self, batch_id: Uuid) -> Option<(usize, usize)> {
        let mut batches = self.batches.write().await;
        if let Some(batch) = batches.get_mut(&batch_id) {
            let mut cancelled = 0;
            let mut completed = 0;

            for job_id in &batch.job_ids {
                if let Some(job) = self.job_queue.get(*job_id) {
                    match job.status {
                        JobStatus::Completed => completed += 1,
                        JobStatus::Queued | JobStatus::Processing => {
                            self.job_queue.cancel(*job_id);
                            cancelled += 1;
                        }
                        _ => {}
                    }
                }
            }

            batch.cancel();
            Some((cancelled, completed))
        } else {
            None
        }
    }

    /// Get progress for a batch
    pub async fn get_progress(&self, batch_id: Uuid) -> Option<BatchProgress> {
        let batches = self.batches.read().await;
        let batch = batches.get(&batch_id)?;

        let mut progress = BatchProgress::new(batch.job_ids.len());

        for job_id in &batch.job_ids {
            if let Some(job) = self.job_queue.get(*job_id) {
                match job.status {
                    JobStatus::Completed => {
                        progress.completed += 1;
                        progress.pending -= 1;
                    }
                    JobStatus::Processing => {
                        progress.processing += 1;
                        progress.pending -= 1;
                    }
                    JobStatus::Failed => {
                        progress.failed += 1;
                        progress.pending -= 1;
                    }
                    JobStatus::Cancelled => {
                        progress.failed += 1;
                        progress.pending -= 1;
                    }
                    JobStatus::Queued => {}
                }
            }
        }

        Some(progress)
    }

    /// List all batches
    pub async fn list(&self) -> Vec<BatchJob> {
        let batches = self.batches.read().await;
        batches.values().cloned().collect()
    }

    /// Get number of active batches
    pub async fn active_count(&self) -> usize {
        let batches = self.batches.read().await;
        batches
            .values()
            .filter(|b| matches!(b.status, BatchStatus::Queued | BatchStatus::Processing))
            .count()
    }

    /// Create individual jobs for a batch
    pub fn create_jobs(&self, batch: &mut BatchJob, filenames: &[String]) {
        for filename in filenames {
            let job = Job::new(filename, batch.options.clone());
            let job_id = job.id;
            self.job_queue.submit(job);
            batch.add_job(job_id);
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    // TC-BATCH-001: Batch job creation
    #[test]
    fn test_batch_job_creation() {
        let options = ConvertOptions::default();
        let batch = BatchJob::new(options, Priority::Normal);

        assert_eq!(batch.status, BatchStatus::Queued);
        assert_eq!(batch.priority, Priority::Normal);
        assert!(batch.job_ids.is_empty());
        assert!(batch.started_at.is_none());
        assert!(batch.completed_at.is_none());
    }

    // TC-BATCH-002: Add jobs to batch
    #[test]
    fn test_batch_add_jobs() {
        let options = ConvertOptions::default();
        let mut batch = BatchJob::new(options, Priority::Normal);

        let job1 = Uuid::new_v4();
        let job2 = Uuid::new_v4();

        batch.add_job(job1);
        batch.add_job(job2);

        assert_eq!(batch.job_count(), 2);
        assert!(batch.job_ids.contains(&job1));
        assert!(batch.job_ids.contains(&job2));
    }

    // TC-BATCH-003: Batch status transitions
    #[test]
    fn test_batch_status_transitions() {
        let options = ConvertOptions::default();
        let mut batch = BatchJob::new(options, Priority::Normal);

        assert_eq!(batch.status, BatchStatus::Queued);

        batch.start();
        assert_eq!(batch.status, BatchStatus::Processing);
        assert!(batch.started_at.is_some());

        batch.complete(false);
        assert_eq!(batch.status, BatchStatus::Completed);
        assert!(batch.completed_at.is_some());
    }

    // TC-BATCH-004: Batch partial completion
    #[test]
    fn test_batch_partial_completion() {
        let options = ConvertOptions::default();
        let mut batch = BatchJob::new(options, Priority::Normal);

        batch.start();
        batch.complete(true);

        assert_eq!(batch.status, BatchStatus::PartiallyCompleted);
    }

    // TC-BATCH-005: Batch cancellation
    #[test]
    fn test_batch_cancellation() {
        let options = ConvertOptions::default();
        let mut batch = BatchJob::new(options, Priority::Normal);

        batch.cancel();
        assert_eq!(batch.status, BatchStatus::Cancelled);
        assert!(batch.completed_at.is_some());
    }

    // TC-BATCH-006: Priority values
    #[test]
    fn test_priority_values() {
        assert!(Priority::High.value() > Priority::Normal.value());
        assert!(Priority::Normal.value() > Priority::Low.value());
    }

    // TC-BATCH-007: Batch progress calculation
    #[test]
    fn test_batch_progress() {
        let mut progress = BatchProgress::new(10);

        assert_eq!(progress.total, 10);
        assert_eq!(progress.pending, 10);
        assert_eq!(progress.percent(), 0);
        assert!(!progress.is_complete());

        progress.completed = 5;
        progress.pending = 5;
        assert_eq!(progress.percent(), 50);

        progress.completed = 8;
        progress.failed = 2;
        progress.pending = 0;
        assert_eq!(progress.percent(), 80);
        assert!(progress.is_complete());
    }

    // TC-BATCH-008: Empty batch progress
    #[test]
    fn test_empty_batch_progress() {
        let progress = BatchProgress::new(0);
        assert_eq!(progress.percent(), 100);
        assert!(progress.is_complete());
    }

    // TC-BATCH-009: Batch status display
    #[test]
    fn test_batch_status_display() {
        assert_eq!(BatchStatus::Queued.to_string(), "queued");
        assert_eq!(BatchStatus::Processing.to_string(), "processing");
        assert_eq!(BatchStatus::Completed.to_string(), "completed");
        assert_eq!(
            BatchStatus::PartiallyCompleted.to_string(),
            "partially_completed"
        );
        assert_eq!(BatchStatus::Failed.to_string(), "failed");
        assert_eq!(BatchStatus::Cancelled.to_string(), "cancelled");
    }

    // TC-BATCH-010: Priority default
    #[test]
    fn test_priority_default() {
        let priority = Priority::default();
        assert_eq!(priority, Priority::Normal);
    }

    // TC-BATCH-011: Batch queue creation
    #[tokio::test]
    async fn test_batch_queue_creation() {
        let job_queue = JobQueue::new();
        let batch_queue = BatchQueue::new(job_queue);

        assert_eq!(batch_queue.active_count().await, 0);
    }

    // TC-BATCH-012: Submit and get batch
    #[tokio::test]
    async fn test_batch_queue_submit_get() {
        let job_queue = JobQueue::new();
        let batch_queue = BatchQueue::new(job_queue);

        let options = ConvertOptions::default();
        let batch = BatchJob::new(options, Priority::Normal);
        let batch_id = batch.id;

        batch_queue.submit(batch).await;

        let retrieved = batch_queue.get(batch_id).await;
        assert!(retrieved.is_some());
        assert_eq!(retrieved.unwrap().id, batch_id);
    }

    // TC-BATCH-013: Update batch
    #[tokio::test]
    async fn test_batch_queue_update() {
        let job_queue = JobQueue::new();
        let batch_queue = BatchQueue::new(job_queue);

        let options = ConvertOptions::default();
        let batch = BatchJob::new(options, Priority::Normal);
        let batch_id = batch.id;

        batch_queue.submit(batch).await;

        batch_queue
            .update(batch_id, |b| {
                b.start();
            })
            .await;

        let retrieved = batch_queue.get(batch_id).await.unwrap();
        assert_eq!(retrieved.status, BatchStatus::Processing);
    }

    // TC-BATCH-014: List batches
    #[tokio::test]
    async fn test_batch_queue_list() {
        let job_queue = JobQueue::new();
        let batch_queue = BatchQueue::new(job_queue);

        let options = ConvertOptions::default();
        batch_queue
            .submit(BatchJob::new(options.clone(), Priority::Normal))
            .await;
        batch_queue
            .submit(BatchJob::new(options, Priority::High))
            .await;

        let batches = batch_queue.list().await;
        assert_eq!(batches.len(), 2);
    }

    // TC-BATCH-015: Active batch count
    #[tokio::test]
    async fn test_batch_active_count() {
        let job_queue = JobQueue::new();
        let batch_queue = BatchQueue::new(job_queue);

        let options = ConvertOptions::default();

        // Add queued batch
        batch_queue
            .submit(BatchJob::new(options.clone(), Priority::Normal))
            .await;

        // Add processing batch
        let mut processing = BatchJob::new(options.clone(), Priority::Normal);
        processing.start();
        batch_queue.submit(processing).await;

        // Add completed batch
        let mut completed = BatchJob::new(options, Priority::Normal);
        completed.complete(false);
        batch_queue.submit(completed).await;

        assert_eq!(batch_queue.active_count().await, 2);
    }

    // TC-BATCH-016: Batch serialization
    #[test]
    fn test_batch_serialization() {
        let options = ConvertOptions::default();
        let batch = BatchJob::new(options, Priority::High);

        let json = serde_json::to_string(&batch).unwrap();
        assert!(json.contains("\"priority\":\"high\""));
        assert!(json.contains("\"status\":\"queued\""));
    }

    // TC-BATCH-017: Priority serialization
    #[test]
    fn test_priority_serialization() {
        let priorities = [Priority::Low, Priority::Normal, Priority::High];

        for p in priorities {
            let json = serde_json::to_string(&p).unwrap();
            let deserialized: Priority = serde_json::from_str(&json).unwrap();
            assert_eq!(p, deserialized);
        }
    }

    // TC-BATCH-018: Batch progress serialization
    #[test]
    fn test_batch_progress_serialization() {
        let progress = BatchProgress {
            completed: 3,
            processing: 1,
            pending: 1,
            failed: 0,
            total: 5,
        };

        let json = serde_json::to_string(&progress).unwrap();
        assert!(json.contains("\"completed\":3"));
        assert!(json.contains("\"total\":5"));
    }
}
