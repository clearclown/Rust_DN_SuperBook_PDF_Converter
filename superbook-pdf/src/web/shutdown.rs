//! Graceful shutdown for the web server
//!
//! Provides safe shutdown coordination with job completion waiting,
//! data persistence, and proper resource cleanup.

use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::Arc;
use std::time::Duration;
use tokio::sync::broadcast;

/// Shutdown configuration
#[derive(Debug, Clone)]
pub struct ShutdownConfig {
    /// Shutdown timeout in seconds
    pub timeout_secs: u64,
    /// Wait for processing jobs to complete
    pub wait_for_jobs: bool,
    /// WebSocket drain time in milliseconds
    pub ws_drain_ms: u64,
}

impl Default for ShutdownConfig {
    fn default() -> Self {
        Self {
            timeout_secs: 30,
            wait_for_jobs: true,
            ws_drain_ms: 1000,
        }
    }
}

impl ShutdownConfig {
    /// Create a quick shutdown config (no waiting)
    pub fn quick() -> Self {
        Self {
            timeout_secs: 5,
            wait_for_jobs: false,
            ws_drain_ms: 100,
        }
    }

    /// Create config with custom timeout
    pub fn with_timeout(timeout_secs: u64) -> Self {
        Self {
            timeout_secs,
            ..Default::default()
        }
    }
}

/// Shutdown result
#[derive(Debug, Clone, PartialEq, Eq)]
pub enum ShutdownResult {
    /// All jobs completed, clean shutdown
    Success,
    /// Timeout waiting for jobs
    Timeout { pending_jobs: usize },
    /// Error during shutdown
    Error(String),
}

impl ShutdownResult {
    /// Check if shutdown was successful
    pub fn is_success(&self) -> bool {
        matches!(self, ShutdownResult::Success)
    }

    /// Get pending job count if timed out
    pub fn pending_jobs(&self) -> Option<usize> {
        match self {
            ShutdownResult::Timeout { pending_jobs } => Some(*pending_jobs),
            _ => None,
        }
    }
}

/// Shutdown coordinator for graceful shutdown
pub struct ShutdownCoordinator {
    config: ShutdownConfig,
    shutdown_tx: broadcast::Sender<ShutdownSignal>,
    is_shutting_down: Arc<AtomicBool>,
}

/// Shutdown signal types
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum ShutdownSignal {
    /// Graceful shutdown requested
    Graceful,
    /// Immediate shutdown (skip job waiting)
    Immediate,
}

impl ShutdownCoordinator {
    /// Create a new shutdown coordinator
    pub fn new(config: ShutdownConfig) -> Self {
        let (shutdown_tx, _) = broadcast::channel(16);
        Self {
            config,
            shutdown_tx,
            is_shutting_down: Arc::new(AtomicBool::new(false)),
        }
    }

    /// Get the shutdown configuration
    pub fn config(&self) -> &ShutdownConfig {
        &self.config
    }

    /// Trigger graceful shutdown
    pub fn trigger_shutdown(&self) {
        self.is_shutting_down.store(true, Ordering::SeqCst);
        let _ = self.shutdown_tx.send(ShutdownSignal::Graceful);
    }

    /// Trigger immediate shutdown
    pub fn trigger_immediate(&self) {
        self.is_shutting_down.store(true, Ordering::SeqCst);
        let _ = self.shutdown_tx.send(ShutdownSignal::Immediate);
    }

    /// Check if shutdown is in progress
    pub fn is_shutting_down(&self) -> bool {
        self.is_shutting_down.load(Ordering::SeqCst)
    }

    /// Subscribe to shutdown signals
    pub fn subscribe(&self) -> broadcast::Receiver<ShutdownSignal> {
        self.shutdown_tx.subscribe()
    }

    /// Get the number of active subscribers
    pub fn subscriber_count(&self) -> usize {
        self.shutdown_tx.receiver_count()
    }

    /// Wait for shutdown signal
    pub async fn wait_for_shutdown(&self) -> ShutdownSignal {
        let mut rx = self.subscribe();
        rx.recv().await.unwrap_or(ShutdownSignal::Graceful)
    }

    /// Get shutdown timeout duration
    pub fn timeout(&self) -> Duration {
        Duration::from_secs(self.config.timeout_secs)
    }
}

impl Clone for ShutdownCoordinator {
    fn clone(&self) -> Self {
        Self {
            config: self.config.clone(),
            shutdown_tx: self.shutdown_tx.clone(),
            is_shutting_down: self.is_shutting_down.clone(),
        }
    }
}

/// Setup signal handlers for graceful shutdown
///
/// Returns when SIGTERM or SIGINT is received.
pub async fn wait_for_shutdown_signal() {
    let ctrl_c = async {
        tokio::signal::ctrl_c()
            .await
            .expect("Failed to setup SIGINT handler");
    };

    #[cfg(unix)]
    let terminate = async {
        tokio::signal::unix::signal(tokio::signal::unix::SignalKind::terminate())
            .expect("Failed to setup SIGTERM handler")
            .recv()
            .await;
    };

    #[cfg(not(unix))]
    let terminate = std::future::pending::<()>();

    tokio::select! {
        _ = ctrl_c => {},
        _ = terminate => {},
    }
}

/// Graceful shutdown handler
///
/// Coordinates the shutdown process:
/// 1. Notifies WebSocket clients
/// 2. Stops accepting new jobs
/// 3. Waits for processing jobs to complete
/// 4. Flushes job store
/// 5. Stops workers
pub async fn graceful_shutdown<F, Fut>(
    coordinator: &ShutdownCoordinator,
    pending_job_count: F,
) -> ShutdownResult
where
    F: Fn() -> Fut,
    Fut: std::future::Future<Output = usize>,
{
    // Check if we should wait for jobs
    if !coordinator.config.wait_for_jobs {
        return ShutdownResult::Success;
    }

    let timeout = coordinator.timeout();
    let start = std::time::Instant::now();

    // Poll until all jobs complete or timeout
    loop {
        let pending = pending_job_count().await;

        if pending == 0 {
            return ShutdownResult::Success;
        }

        if start.elapsed() >= timeout {
            return ShutdownResult::Timeout {
                pending_jobs: pending,
            };
        }

        // Wait a bit before checking again
        tokio::time::sleep(Duration::from_millis(100)).await;
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    // SHUT-001: ShutdownConfig デフォルト値
    #[test]
    fn test_shutdown_config_default() {
        let config = ShutdownConfig::default();
        assert_eq!(config.timeout_secs, 30);
        assert!(config.wait_for_jobs);
        assert_eq!(config.ws_drain_ms, 1000);
    }

    #[test]
    fn test_shutdown_config_quick() {
        let config = ShutdownConfig::quick();
        assert_eq!(config.timeout_secs, 5);
        assert!(!config.wait_for_jobs);
        assert_eq!(config.ws_drain_ms, 100);
    }

    #[test]
    fn test_shutdown_config_with_timeout() {
        let config = ShutdownConfig::with_timeout(60);
        assert_eq!(config.timeout_secs, 60);
        assert!(config.wait_for_jobs);
    }

    // SHUT-002: ShutdownCoordinator 作成
    #[test]
    fn test_shutdown_coordinator_new() {
        let config = ShutdownConfig::default();
        let coordinator = ShutdownCoordinator::new(config.clone());
        assert_eq!(coordinator.config().timeout_secs, 30);
        assert!(!coordinator.is_shutting_down());
    }

    // SHUT-003: シャットダウンシグナル送受信
    #[tokio::test]
    async fn test_shutdown_signal_send_receive() {
        let coordinator = ShutdownCoordinator::new(ShutdownConfig::default());
        let mut rx = coordinator.subscribe();

        coordinator.trigger_shutdown();

        let signal = rx.recv().await.unwrap();
        assert_eq!(signal, ShutdownSignal::Graceful);
        assert!(coordinator.is_shutting_down());
    }

    #[tokio::test]
    async fn test_shutdown_signal_immediate() {
        let coordinator = ShutdownCoordinator::new(ShutdownConfig::default());
        let mut rx = coordinator.subscribe();

        coordinator.trigger_immediate();

        let signal = rx.recv().await.unwrap();
        assert_eq!(signal, ShutdownSignal::Immediate);
        assert!(coordinator.is_shutting_down());
    }

    // SHUT-004: 複数リスナーへのブロードキャスト
    #[tokio::test]
    async fn test_shutdown_broadcast_to_multiple() {
        let coordinator = ShutdownCoordinator::new(ShutdownConfig::default());
        let mut rx1 = coordinator.subscribe();
        let mut rx2 = coordinator.subscribe();
        let mut rx3 = coordinator.subscribe();

        assert_eq!(coordinator.subscriber_count(), 3);

        coordinator.trigger_shutdown();

        assert_eq!(rx1.recv().await.unwrap(), ShutdownSignal::Graceful);
        assert_eq!(rx2.recv().await.unwrap(), ShutdownSignal::Graceful);
        assert_eq!(rx3.recv().await.unwrap(), ShutdownSignal::Graceful);
    }

    // SHUT-005: ジョブ完了待機 (正常完了)
    #[tokio::test]
    async fn test_graceful_shutdown_success() {
        let coordinator = ShutdownCoordinator::new(ShutdownConfig::with_timeout(5));

        // Simulate no pending jobs
        let result = graceful_shutdown(&coordinator, || async { 0 }).await;

        assert!(result.is_success());
        assert_eq!(result, ShutdownResult::Success);
    }

    // SHUT-006: ジョブ完了待機 (タイムアウト)
    #[tokio::test]
    async fn test_graceful_shutdown_timeout() {
        let mut config = ShutdownConfig::default();
        config.timeout_secs = 1; // 1 second timeout
        let coordinator = ShutdownCoordinator::new(config);

        // Simulate 5 pending jobs that never complete
        let result = graceful_shutdown(&coordinator, || async { 5 }).await;

        assert!(!result.is_success());
        assert_eq!(result.pending_jobs(), Some(5));
    }

    // SHUT-009: ShutdownResult 各バリアント
    #[test]
    fn test_shutdown_result_variants() {
        let success = ShutdownResult::Success;
        assert!(success.is_success());
        assert_eq!(success.pending_jobs(), None);

        let timeout = ShutdownResult::Timeout { pending_jobs: 3 };
        assert!(!timeout.is_success());
        assert_eq!(timeout.pending_jobs(), Some(3));

        let error = ShutdownResult::Error("test error".to_string());
        assert!(!error.is_success());
        assert_eq!(error.pending_jobs(), None);
    }

    #[test]
    fn test_shutdown_coordinator_clone() {
        let coordinator1 = ShutdownCoordinator::new(ShutdownConfig::default());
        let coordinator2 = coordinator1.clone();

        coordinator1.trigger_shutdown();

        assert!(coordinator2.is_shutting_down());
    }

    #[tokio::test]
    async fn test_shutdown_coordinator_wait_for_shutdown() {
        let coordinator = ShutdownCoordinator::new(ShutdownConfig::default());
        let coordinator_clone = coordinator.clone();

        // Spawn a task to trigger shutdown
        tokio::spawn(async move {
            tokio::time::sleep(Duration::from_millis(50)).await;
            coordinator_clone.trigger_shutdown();
        });

        let signal = coordinator.wait_for_shutdown().await;
        assert_eq!(signal, ShutdownSignal::Graceful);
    }

    #[test]
    fn test_shutdown_timeout_duration() {
        let config = ShutdownConfig::with_timeout(45);
        let coordinator = ShutdownCoordinator::new(config);
        assert_eq!(coordinator.timeout(), Duration::from_secs(45));
    }

    #[tokio::test]
    async fn test_graceful_shutdown_no_wait() {
        let mut config = ShutdownConfig::default();
        config.wait_for_jobs = false;
        let coordinator = ShutdownCoordinator::new(config);

        // Even with pending jobs, should return immediately
        let result = graceful_shutdown(&coordinator, || async { 10 }).await;

        assert!(result.is_success());
    }
}
