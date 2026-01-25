//! Metrics collection module for server monitoring
//!
//! Provides job statistics, system metrics, and Prometheus-compatible output.
//!
//! Spec Reference: specs/23-metrics.spec.md

use serde::Serialize;
use std::sync::atomic::{AtomicU64, Ordering};
use std::time::Instant;

/// Job statistics
#[derive(Debug, Clone, Serialize)]
pub struct JobStatistics {
    /// Total number of jobs
    pub total_jobs: u64,
    /// Completed jobs
    pub completed_jobs: u64,
    /// Failed jobs
    pub failed_jobs: u64,
    /// Currently processing jobs
    pub active_jobs: u64,
    /// Jobs waiting in queue
    pub queued_jobs: u64,
    /// Average processing time in seconds
    pub avg_processing_time: f64,
    /// Total pages processed
    pub total_pages_processed: u64,
}

/// Batch statistics
#[derive(Debug, Clone, Serialize)]
pub struct BatchStatistics {
    /// Total batches
    pub total: u64,
    /// Completed batches
    pub completed: u64,
    /// Currently processing batches
    pub processing: u64,
}

/// System metrics
#[derive(Debug, Clone, Serialize)]
pub struct SystemMetrics {
    /// Memory used in MB
    pub memory_used_mb: u64,
    /// Worker count
    pub worker_count: usize,
    /// Active WebSocket connections
    pub websocket_connections: usize,
}

/// Server information
#[derive(Debug, Clone, Serialize)]
pub struct ServerInfo {
    /// Server version
    pub version: String,
    /// Uptime in seconds
    pub uptime_seconds: u64,
    /// Server start time (ISO 8601)
    pub started_at: String,
}

/// Complete statistics response
#[derive(Debug, Clone, Serialize)]
pub struct StatsResponse {
    pub server: ServerInfo,
    pub jobs: JobStatistics,
    pub batches: BatchStatistics,
    pub system: SystemMetrics,
}

/// Metrics collector with atomic counters for thread-safe updates
pub struct MetricsCollector {
    /// Server start time
    started_at: Instant,
    /// ISO 8601 start time string
    started_at_str: String,
    /// Total jobs submitted
    total_jobs: AtomicU64,
    /// Completed jobs
    completed_jobs: AtomicU64,
    /// Failed jobs
    failed_jobs: AtomicU64,
    /// Currently active jobs
    active_jobs: AtomicU64,
    /// Total processing time in milliseconds
    total_processing_ms: AtomicU64,
    /// Total pages processed
    total_pages: AtomicU64,
    /// Total batches
    total_batches: AtomicU64,
    /// Completed batches
    completed_batches: AtomicU64,
    /// Active batches
    active_batches: AtomicU64,
}

impl MetricsCollector {
    /// Create a new metrics collector
    pub fn new() -> Self {
        Self {
            started_at: Instant::now(),
            started_at_str: chrono::Utc::now().to_rfc3339(),
            total_jobs: AtomicU64::new(0),
            completed_jobs: AtomicU64::new(0),
            failed_jobs: AtomicU64::new(0),
            active_jobs: AtomicU64::new(0),
            total_processing_ms: AtomicU64::new(0),
            total_pages: AtomicU64::new(0),
            total_batches: AtomicU64::new(0),
            completed_batches: AtomicU64::new(0),
            active_batches: AtomicU64::new(0),
        }
    }

    /// Record a job being started
    pub fn record_job_started(&self) {
        self.total_jobs.fetch_add(1, Ordering::Relaxed);
        self.active_jobs.fetch_add(1, Ordering::Relaxed);
    }

    /// Record a job completion
    pub fn record_job_completed(&self, duration_ms: u64, pages: u64) {
        self.completed_jobs.fetch_add(1, Ordering::Relaxed);
        self.active_jobs.fetch_sub(1, Ordering::Relaxed);
        self.total_processing_ms.fetch_add(duration_ms, Ordering::Relaxed);
        self.total_pages.fetch_add(pages, Ordering::Relaxed);
    }

    /// Record a job failure
    pub fn record_job_failed(&self) {
        self.failed_jobs.fetch_add(1, Ordering::Relaxed);
        self.active_jobs.fetch_sub(1, Ordering::Relaxed);
    }

    /// Record a batch being started
    pub fn record_batch_started(&self) {
        self.total_batches.fetch_add(1, Ordering::Relaxed);
        self.active_batches.fetch_add(1, Ordering::Relaxed);
    }

    /// Record a batch completion
    pub fn record_batch_completed(&self) {
        self.completed_batches.fetch_add(1, Ordering::Relaxed);
        self.active_batches.fetch_sub(1, Ordering::Relaxed);
    }

    /// Get server uptime in seconds
    pub fn get_uptime(&self) -> u64 {
        self.started_at.elapsed().as_secs()
    }

    /// Get job statistics
    pub fn get_job_statistics(&self, queued_jobs: u64) -> JobStatistics {
        let completed = self.completed_jobs.load(Ordering::Relaxed);
        let total_ms = self.total_processing_ms.load(Ordering::Relaxed);

        let avg_time = if completed > 0 {
            (total_ms as f64 / completed as f64) / 1000.0
        } else {
            0.0
        };

        JobStatistics {
            total_jobs: self.total_jobs.load(Ordering::Relaxed),
            completed_jobs: completed,
            failed_jobs: self.failed_jobs.load(Ordering::Relaxed),
            active_jobs: self.active_jobs.load(Ordering::Relaxed),
            queued_jobs,
            avg_processing_time: avg_time,
            total_pages_processed: self.total_pages.load(Ordering::Relaxed),
        }
    }

    /// Get batch statistics
    pub fn get_batch_statistics(&self) -> BatchStatistics {
        BatchStatistics {
            total: self.total_batches.load(Ordering::Relaxed),
            completed: self.completed_batches.load(Ordering::Relaxed),
            processing: self.active_batches.load(Ordering::Relaxed),
        }
    }

    /// Get server info
    pub fn get_server_info(&self) -> ServerInfo {
        ServerInfo {
            version: env!("CARGO_PKG_VERSION").to_string(),
            uptime_seconds: self.get_uptime(),
            started_at: self.started_at_str.clone(),
        }
    }

    /// Format metrics in Prometheus format
    pub fn format_prometheus(&self, queued_jobs: u64, websocket_connections: usize, worker_count: usize) -> String {
        let stats = self.get_job_statistics(queued_jobs);
        let batch_stats = self.get_batch_statistics();
        let uptime = self.get_uptime();

        let mut output = String::new();

        // Jobs counter
        output.push_str("# HELP superbook_jobs_total Total number of jobs by status\n");
        output.push_str("# TYPE superbook_jobs_total counter\n");
        output.push_str(&format!("superbook_jobs_total{{status=\"completed\"}} {}\n", stats.completed_jobs));
        output.push_str(&format!("superbook_jobs_total{{status=\"failed\"}} {}\n", stats.failed_jobs));
        output.push_str(&format!("superbook_jobs_total{{status=\"processing\"}} {}\n", stats.active_jobs));
        output.push_str(&format!("superbook_jobs_total{{status=\"queued\"}} {}\n", stats.queued_jobs));

        // Pages processed
        output.push_str("\n# HELP superbook_pages_processed_total Total pages processed\n");
        output.push_str("# TYPE superbook_pages_processed_total counter\n");
        output.push_str(&format!("superbook_pages_processed_total {}\n", stats.total_pages_processed));

        // Average processing time
        output.push_str("\n# HELP superbook_avg_processing_seconds Average job processing time\n");
        output.push_str("# TYPE superbook_avg_processing_seconds gauge\n");
        output.push_str(&format!("superbook_avg_processing_seconds {:.2}\n", stats.avg_processing_time));

        // Batches
        output.push_str("\n# HELP superbook_batches_total Total batches by status\n");
        output.push_str("# TYPE superbook_batches_total counter\n");
        output.push_str(&format!("superbook_batches_total{{status=\"completed\"}} {}\n", batch_stats.completed));
        output.push_str(&format!("superbook_batches_total{{status=\"processing\"}} {}\n", batch_stats.processing));

        // Uptime
        output.push_str("\n# HELP superbook_uptime_seconds Server uptime in seconds\n");
        output.push_str("# TYPE superbook_uptime_seconds gauge\n");
        output.push_str(&format!("superbook_uptime_seconds {}\n", uptime));

        // WebSocket connections
        output.push_str("\n# HELP superbook_websocket_connections Active WebSocket connections\n");
        output.push_str("# TYPE superbook_websocket_connections gauge\n");
        output.push_str(&format!("superbook_websocket_connections {}\n", websocket_connections));

        // Workers
        output.push_str("\n# HELP superbook_workers Worker count\n");
        output.push_str("# TYPE superbook_workers gauge\n");
        output.push_str(&format!("superbook_workers {}\n", worker_count));

        output
    }
}

impl Default for MetricsCollector {
    fn default() -> Self {
        Self::new()
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    // TC-METRICS-001: Metrics collector creation
    #[test]
    fn test_metrics_collector_creation() {
        let collector = MetricsCollector::new();
        assert_eq!(collector.total_jobs.load(Ordering::Relaxed), 0);
        assert_eq!(collector.completed_jobs.load(Ordering::Relaxed), 0);
    }

    // TC-METRICS-002: Job completion recording
    #[test]
    fn test_record_job_completed() {
        let collector = MetricsCollector::new();

        collector.record_job_started();
        assert_eq!(collector.active_jobs.load(Ordering::Relaxed), 1);

        collector.record_job_completed(5000, 10);

        let stats = collector.get_job_statistics(0);
        assert_eq!(stats.completed_jobs, 1);
        assert_eq!(stats.total_pages_processed, 10);
        assert_eq!(stats.active_jobs, 0);
    }

    // TC-METRICS-003: Job failure recording
    #[test]
    fn test_record_job_failed() {
        let collector = MetricsCollector::new();

        collector.record_job_started();
        collector.record_job_failed();

        let stats = collector.get_job_statistics(0);
        assert_eq!(stats.failed_jobs, 1);
        assert_eq!(stats.active_jobs, 0);
    }

    // TC-METRICS-004: Average processing time calculation
    #[test]
    fn test_avg_processing_time() {
        let collector = MetricsCollector::new();

        // Complete 3 jobs with different times
        collector.record_job_started();
        collector.record_job_completed(3000, 5); // 3 seconds

        collector.record_job_started();
        collector.record_job_completed(6000, 10); // 6 seconds

        collector.record_job_started();
        collector.record_job_completed(9000, 15); // 9 seconds

        let stats = collector.get_job_statistics(0);
        // Average: (3 + 6 + 9) / 3 = 6 seconds
        assert!((stats.avg_processing_time - 6.0).abs() < 0.01);
        assert_eq!(stats.total_pages_processed, 30);
    }

    // TC-METRICS-005: Prometheus format output
    #[test]
    fn test_format_prometheus() {
        let collector = MetricsCollector::new();

        collector.record_job_started();
        collector.record_job_completed(1000, 5);

        let output = collector.format_prometheus(2, 3, 4);

        assert!(output.contains("superbook_jobs_total{status=\"completed\"} 1"));
        assert!(output.contains("superbook_pages_processed_total 5"));
        assert!(output.contains("superbook_websocket_connections 3"));
        assert!(output.contains("superbook_workers 4"));
    }

    // TC-METRICS-006: JSON statistics output
    #[test]
    fn test_job_statistics_serialize() {
        let stats = JobStatistics {
            total_jobs: 100,
            completed_jobs: 90,
            failed_jobs: 5,
            active_jobs: 3,
            queued_jobs: 2,
            avg_processing_time: 45.5,
            total_pages_processed: 5000,
        };

        let json = serde_json::to_string(&stats).unwrap();
        assert!(json.contains("\"total_jobs\":100"));
        assert!(json.contains("\"avg_processing_time\":45.5"));
    }

    // TC-METRICS-007: Uptime calculation
    #[test]
    fn test_uptime() {
        let collector = MetricsCollector::new();

        // Uptime should be >= 0
        assert!(collector.get_uptime() < 1);

        // Sleep and check uptime increased
        std::thread::sleep(std::time::Duration::from_millis(10));
        // Still less than 1 second
        assert!(collector.get_uptime() < 1);
    }

    // TC-METRICS-008: Concurrent access safety
    #[test]
    fn test_concurrent_access() {
        use std::sync::Arc;
        use std::thread;

        let collector = Arc::new(MetricsCollector::new());
        let mut handles = vec![];

        // Spawn multiple threads to update metrics
        for _ in 0..10 {
            let c = Arc::clone(&collector);
            handles.push(thread::spawn(move || {
                for _ in 0..100 {
                    c.record_job_started();
                    c.record_job_completed(100, 1);
                }
            }));
        }

        for handle in handles {
            handle.join().unwrap();
        }

        let stats = collector.get_job_statistics(0);
        assert_eq!(stats.total_jobs, 1000);
        assert_eq!(stats.completed_jobs, 1000);
        assert_eq!(stats.total_pages_processed, 1000);
    }

    // TC-METRICS-009: Batch statistics
    #[test]
    fn test_batch_statistics() {
        let collector = MetricsCollector::new();

        collector.record_batch_started();
        collector.record_batch_started();

        let stats = collector.get_batch_statistics();
        assert_eq!(stats.total, 2);
        assert_eq!(stats.processing, 2);

        collector.record_batch_completed();

        let stats = collector.get_batch_statistics();
        assert_eq!(stats.completed, 1);
        assert_eq!(stats.processing, 1);
    }

    // TC-METRICS-010: Server info
    #[test]
    fn test_server_info() {
        let collector = MetricsCollector::new();
        let info = collector.get_server_info();

        assert!(!info.version.is_empty());
        assert!(!info.started_at.is_empty());
    }

    // TC-METRICS-011: Default implementation
    #[test]
    fn test_default() {
        let collector = MetricsCollector::default();
        assert_eq!(collector.total_jobs.load(Ordering::Relaxed), 0);
    }

    // TC-METRICS-012: Stats response serialization
    #[test]
    fn test_stats_response_serialize() {
        let response = StatsResponse {
            server: ServerInfo {
                version: "0.7.0".to_string(),
                uptime_seconds: 3600,
                started_at: "2024-01-01T00:00:00Z".to_string(),
            },
            jobs: JobStatistics {
                total_jobs: 100,
                completed_jobs: 90,
                failed_jobs: 5,
                active_jobs: 3,
                queued_jobs: 2,
                avg_processing_time: 45.5,
                total_pages_processed: 5000,
            },
            batches: BatchStatistics {
                total: 20,
                completed: 18,
                processing: 2,
            },
            system: SystemMetrics {
                memory_used_mb: 512,
                worker_count: 4,
                websocket_connections: 3,
            },
        };

        let json = serde_json::to_string(&response).unwrap();
        assert!(json.contains("\"version\":\"0.7.0\""));
        assert!(json.contains("\"uptime_seconds\":3600"));
    }
}
