//! WebSocket module for real-time job progress updates
//!
//! Provides WebSocket endpoints for streaming job progress to clients.

use axum::{
    extract::{
        ws::{Message, WebSocket, WebSocketUpgrade},
        Path, State,
    },
    response::IntoResponse,
};
use serde::Serialize;
use std::collections::HashMap;
use std::sync::Arc;
use tokio::sync::{broadcast, RwLock};
use uuid::Uuid;

use super::job::JobStatus;

/// WebSocket message types sent to clients
#[derive(Debug, Clone, Serialize)]
#[serde(tag = "type", content = "data")]
pub enum WsMessage {
    /// Progress update
    #[serde(rename = "progress")]
    Progress {
        job_id: Uuid,
        current_step: u32,
        total_steps: u32,
        step_name: String,
        percent: u8,
    },
    /// Status change notification
    #[serde(rename = "status_change")]
    StatusChange {
        job_id: Uuid,
        old_status: JobStatus,
        new_status: JobStatus,
    },
    /// Job completed notification
    #[serde(rename = "completed")]
    Completed {
        job_id: Uuid,
        download_url: String,
        elapsed_seconds: f64,
        page_count: usize,
    },
    /// Error notification
    #[serde(rename = "error")]
    Error { job_id: Uuid, message: String },
    /// Batch progress update
    #[serde(rename = "batch_progress")]
    BatchProgress {
        batch_id: Uuid,
        completed: usize,
        processing: usize,
        pending: usize,
        failed: usize,
        total: usize,
    },
    /// Batch completed notification
    #[serde(rename = "batch_completed")]
    BatchCompleted {
        batch_id: Uuid,
        success_count: usize,
        failed_count: usize,
    },
    /// Server shutdown notification
    #[serde(rename = "server_shutdown")]
    ServerShutdown {
        /// Reason for shutdown (e.g., "graceful", "maintenance")
        reason: String,
        /// Time until server shuts down (in seconds)
        countdown_secs: u64,
    },
    /// Page preview for real-time visualization (Phase 4.1)
    #[serde(rename = "page_preview")]
    PagePreview {
        job_id: Uuid,
        page_number: usize,
        /// Base64-encoded preview image (JPEG, thumbnail size)
        preview_base64: String,
        /// Processing stage: "original", "deskewed", "upscaled", "normalized", "final"
        stage: String,
        /// Image dimensions
        width: u32,
        height: u32,
    },
}

/// Broadcaster for sending messages to connected WebSocket clients
pub struct WsBroadcaster {
    /// Map of job_id to broadcast sender
    channels: RwLock<HashMap<Uuid, broadcast::Sender<WsMessage>>>,
    /// Global channel for server-wide messages (shutdown, etc.)
    global_sender: broadcast::Sender<WsMessage>,
    /// Channel capacity
    capacity: usize,
}

impl WsBroadcaster {
    /// Create a new broadcaster
    pub fn new() -> Self {
        let (global_sender, _) = broadcast::channel(100);
        Self {
            channels: RwLock::new(HashMap::new()),
            global_sender,
            capacity: 100,
        }
    }

    /// Create a new broadcaster with custom capacity
    pub fn with_capacity(capacity: usize) -> Self {
        let (global_sender, _) = broadcast::channel(capacity);
        Self {
            channels: RwLock::new(HashMap::new()),
            global_sender,
            capacity,
        }
    }

    /// Subscribe to global server-wide messages
    pub fn subscribe_global(&self) -> broadcast::Receiver<WsMessage> {
        self.global_sender.subscribe()
    }

    /// Subscribe to updates for a specific job
    pub async fn subscribe(&self, job_id: Uuid) -> broadcast::Receiver<WsMessage> {
        let mut channels = self.channels.write().await;

        if let Some(sender) = channels.get(&job_id) {
            sender.subscribe()
        } else {
            let (sender, receiver) = broadcast::channel(self.capacity);
            channels.insert(job_id, sender);
            receiver
        }
    }

    /// Broadcast a message to all subscribers of a job
    pub async fn broadcast(&self, job_id: Uuid, message: WsMessage) {
        let channels = self.channels.read().await;

        if let Some(sender) = channels.get(&job_id) {
            // Ignore send errors (no receivers)
            let _ = sender.send(message);
        }
    }

    /// Broadcast progress update
    pub async fn broadcast_progress(
        &self,
        job_id: Uuid,
        current_step: u32,
        total_steps: u32,
        step_name: &str,
    ) {
        let percent = if total_steps > 0 {
            ((current_step as f32 / total_steps as f32) * 100.0) as u8
        } else {
            0
        };

        self.broadcast(
            job_id,
            WsMessage::Progress {
                job_id,
                current_step,
                total_steps,
                step_name: step_name.to_string(),
                percent,
            },
        )
        .await;
    }

    /// Broadcast status change
    pub async fn broadcast_status_change(
        &self,
        job_id: Uuid,
        old_status: JobStatus,
        new_status: JobStatus,
    ) {
        self.broadcast(
            job_id,
            WsMessage::StatusChange {
                job_id,
                old_status,
                new_status,
            },
        )
        .await;
    }

    /// Broadcast completion
    pub async fn broadcast_completed(
        &self,
        job_id: Uuid,
        elapsed_seconds: f64,
        page_count: usize,
    ) {
        self.broadcast(
            job_id,
            WsMessage::Completed {
                job_id,
                download_url: format!("/api/jobs/{}/download", job_id),
                elapsed_seconds,
                page_count,
            },
        )
        .await;
    }

    /// Broadcast error
    pub async fn broadcast_error(&self, job_id: Uuid, message: &str) {
        self.broadcast(
            job_id,
            WsMessage::Error {
                job_id,
                message: message.to_string(),
            },
        )
        .await;
    }

    /// Broadcast page preview (Phase 4.1)
    ///
    /// # Arguments
    /// * `job_id` - Job UUID
    /// * `page_number` - Page number (1-indexed)
    /// * `preview_base64` - Base64-encoded JPEG thumbnail
    /// * `stage` - Processing stage name
    /// * `width` - Preview image width
    /// * `height` - Preview image height
    pub async fn broadcast_page_preview(
        &self,
        job_id: Uuid,
        page_number: usize,
        preview_base64: String,
        stage: &str,
        width: u32,
        height: u32,
    ) {
        self.broadcast(
            job_id,
            WsMessage::PagePreview {
                job_id,
                page_number,
                preview_base64,
                stage: stage.to_string(),
                width,
                height,
            },
        )
        .await;
    }

    /// Broadcast batch progress update
    pub async fn broadcast_batch_progress(
        &self,
        batch_id: Uuid,
        completed: usize,
        processing: usize,
        pending: usize,
        failed: usize,
        total: usize,
    ) {
        self.broadcast(
            batch_id,
            WsMessage::BatchProgress {
                batch_id,
                completed,
                processing,
                pending,
                failed,
                total,
            },
        )
        .await;
    }

    /// Broadcast batch completed notification
    pub async fn broadcast_batch_completed(
        &self,
        batch_id: Uuid,
        success_count: usize,
        failed_count: usize,
    ) {
        self.broadcast(
            batch_id,
            WsMessage::BatchCompleted {
                batch_id,
                success_count,
                failed_count,
            },
        )
        .await;
    }

    /// Broadcast server shutdown notification to all connected clients
    pub fn broadcast_shutdown(&self, reason: &str, countdown_secs: u64) {
        let message = WsMessage::ServerShutdown {
            reason: reason.to_string(),
            countdown_secs,
        };
        // Ignore send errors (no receivers)
        let _ = self.global_sender.send(message);
    }

    /// Remove channel for a job (cleanup)
    pub async fn remove_job(&self, job_id: Uuid) {
        let mut channels = self.channels.write().await;
        channels.remove(&job_id);
    }

    /// Get number of active channels
    pub async fn channel_count(&self) -> usize {
        self.channels.read().await.len()
    }
}

impl Default for WsBroadcaster {
    fn default() -> Self {
        Self::new()
    }
}

/// WebSocket handler for job progress updates
pub async fn ws_job_handler(
    ws: WebSocketUpgrade,
    Path(job_id): Path<Uuid>,
    State(broadcaster): State<Arc<WsBroadcaster>>,
) -> impl IntoResponse {
    ws.on_upgrade(move |socket| handle_socket(socket, job_id, broadcaster))
}

/// Handle a WebSocket connection
async fn handle_socket(mut socket: WebSocket, job_id: Uuid, broadcaster: Arc<WsBroadcaster>) {
    let mut receiver = broadcaster.subscribe(job_id).await;

    loop {
        tokio::select! {
            // Receive message from broadcaster and send to client
            result = receiver.recv() => {
                match result {
                    Ok(msg) => {
                        if let Ok(json) = serde_json::to_string(&msg) {
                            if socket.send(Message::Text(json.into())).await.is_err() {
                                // Client disconnected
                                break;
                            }
                        }
                    }
                    Err(broadcast::error::RecvError::Closed) => {
                        // Channel closed
                        break;
                    }
                    Err(broadcast::error::RecvError::Lagged(_)) => {
                        // Receiver lagged, continue
                        continue;
                    }
                }
            }
            // Handle incoming messages from client (ping/pong, close)
            result = socket.recv() => {
                match result {
                    Some(Ok(Message::Close(_))) | None => {
                        // Client closed connection
                        break;
                    }
                    Some(Ok(Message::Ping(data))) => {
                        if socket.send(Message::Pong(data)).await.is_err() {
                            break;
                        }
                    }
                    _ => {
                        // Ignore other messages
                    }
                }
            }
        }
    }
}

// ============================================================
// Preview Generation Utilities (Phase 4.1)
// ============================================================

/// Default thumbnail width for preview images
pub const PREVIEW_WIDTH: u32 = 200;

/// Generate a base64-encoded JPEG thumbnail from an image file
///
/// # Arguments
/// * `image_path` - Path to the source image
/// * `max_width` - Maximum width for the thumbnail (height scaled proportionally)
///
/// # Returns
/// Tuple of (base64_string, width, height) or None if generation fails
pub fn generate_preview_base64(
    image_path: &std::path::Path,
    max_width: u32,
) -> Option<(String, u32, u32)> {
    use base64::{engine::general_purpose::STANDARD, Engine};
    use image::{imageops::FilterType, GenericImageView};
    use std::io::Cursor;

    // Load image
    let img = image::open(image_path).ok()?;

    // Calculate thumbnail dimensions maintaining aspect ratio
    let (orig_width, orig_height) = img.dimensions();
    let scale = max_width as f32 / orig_width as f32;
    let thumb_width = max_width;
    let thumb_height = (orig_height as f32 * scale) as u32;

    // Create thumbnail
    let thumbnail = img.resize(thumb_width, thumb_height, FilterType::Triangle);

    // Convert to RGB for JPEG encoding
    let rgb_img = thumbnail.to_rgb8();

    // Encode as JPEG
    let mut buffer = Cursor::new(Vec::new());
    let mut encoder = image::codecs::jpeg::JpegEncoder::new_with_quality(&mut buffer, 75);
    encoder
        .encode(
            rgb_img.as_raw(),
            thumb_width,
            thumb_height,
            image::ExtendedColorType::Rgb8,
        )
        .ok()?;

    // Convert to base64
    let base64_str = STANDARD.encode(buffer.into_inner());

    Some((base64_str, thumb_width, thumb_height))
}

/// Processing stage names for preview
pub mod preview_stage {
    pub const ORIGINAL: &str = "original";
    pub const DESKEWED: &str = "deskewed";
    pub const TRIMMED: &str = "trimmed";
    pub const UPSCALED: &str = "upscaled";
    pub const NORMALIZED: &str = "normalized";
    pub const COLOR_CORRECTED: &str = "color_corrected";
    pub const FINAL: &str = "final";
}

#[cfg(test)]
mod tests {
    use super::*;

    // TC-WS-001: Broadcaster creation
    #[tokio::test]
    async fn test_broadcaster_creation() {
        let broadcaster = WsBroadcaster::new();
        assert_eq!(broadcaster.channel_count().await, 0);
    }

    // TC-WS-002: Broadcaster with custom capacity
    #[tokio::test]
    async fn test_broadcaster_with_capacity() {
        let broadcaster = WsBroadcaster::with_capacity(50);
        assert_eq!(broadcaster.capacity, 50);
    }

    // TC-WS-003: Subscribe creates channel
    #[tokio::test]
    async fn test_subscribe_creates_channel() {
        let broadcaster = WsBroadcaster::new();
        let job_id = Uuid::new_v4();

        let _receiver = broadcaster.subscribe(job_id).await;

        assert_eq!(broadcaster.channel_count().await, 1);
    }

    // TC-WS-004: Multiple subscribers share channel
    #[tokio::test]
    async fn test_multiple_subscribers() {
        let broadcaster = WsBroadcaster::new();
        let job_id = Uuid::new_v4();

        let _rx1 = broadcaster.subscribe(job_id).await;
        let _rx2 = broadcaster.subscribe(job_id).await;

        // Still only one channel
        assert_eq!(broadcaster.channel_count().await, 1);
    }

    // TC-WS-005: Broadcast progress message
    #[tokio::test]
    async fn test_broadcast_progress() {
        let broadcaster = WsBroadcaster::new();
        let job_id = Uuid::new_v4();

        let mut receiver = broadcaster.subscribe(job_id).await;

        broadcaster
            .broadcast_progress(job_id, 5, 10, "Processing")
            .await;

        let msg = receiver.recv().await.unwrap();
        match msg {
            WsMessage::Progress {
                current_step,
                total_steps,
                percent,
                step_name,
                ..
            } => {
                assert_eq!(current_step, 5);
                assert_eq!(total_steps, 10);
                assert_eq!(percent, 50);
                assert_eq!(step_name, "Processing");
            }
            _ => panic!("Expected Progress message"),
        }
    }

    // TC-WS-006: Broadcast status change
    #[tokio::test]
    async fn test_broadcast_status_change() {
        let broadcaster = WsBroadcaster::new();
        let job_id = Uuid::new_v4();

        let mut receiver = broadcaster.subscribe(job_id).await;

        broadcaster
            .broadcast_status_change(job_id, JobStatus::Queued, JobStatus::Processing)
            .await;

        let msg = receiver.recv().await.unwrap();
        match msg {
            WsMessage::StatusChange {
                old_status,
                new_status,
                ..
            } => {
                assert_eq!(old_status, JobStatus::Queued);
                assert_eq!(new_status, JobStatus::Processing);
            }
            _ => panic!("Expected StatusChange message"),
        }
    }

    // TC-WS-007: Broadcast completion
    #[tokio::test]
    async fn test_broadcast_completed() {
        let broadcaster = WsBroadcaster::new();
        let job_id = Uuid::new_v4();

        let mut receiver = broadcaster.subscribe(job_id).await;

        broadcaster.broadcast_completed(job_id, 45.5, 12).await;

        let msg = receiver.recv().await.unwrap();
        match msg {
            WsMessage::Completed {
                elapsed_seconds,
                page_count,
                download_url,
                ..
            } => {
                assert_eq!(elapsed_seconds, 45.5);
                assert_eq!(page_count, 12);
                assert!(download_url.contains(&job_id.to_string()));
            }
            _ => panic!("Expected Completed message"),
        }
    }

    // TC-WS-008: Broadcast error
    #[tokio::test]
    async fn test_broadcast_error() {
        let broadcaster = WsBroadcaster::new();
        let job_id = Uuid::new_v4();

        let mut receiver = broadcaster.subscribe(job_id).await;

        broadcaster
            .broadcast_error(job_id, "Pipeline failed")
            .await;

        let msg = receiver.recv().await.unwrap();
        match msg {
            WsMessage::Error { message, .. } => {
                assert_eq!(message, "Pipeline failed");
            }
            _ => panic!("Expected Error message"),
        }
    }

    // TC-WS-009: Remove job channel
    #[tokio::test]
    async fn test_remove_job() {
        let broadcaster = WsBroadcaster::new();
        let job_id = Uuid::new_v4();

        let _receiver = broadcaster.subscribe(job_id).await;
        assert_eq!(broadcaster.channel_count().await, 1);

        broadcaster.remove_job(job_id).await;
        assert_eq!(broadcaster.channel_count().await, 0);
    }

    // TC-WS-010: Message serialization
    #[tokio::test]
    async fn test_message_serialization() {
        let job_id = Uuid::new_v4();

        let msg = WsMessage::Progress {
            job_id,
            current_step: 3,
            total_steps: 10,
            step_name: "Deskew".to_string(),
            percent: 30,
        };

        let json = serde_json::to_string(&msg).unwrap();
        assert!(json.contains("\"type\":\"progress\""));
        assert!(json.contains("\"current_step\":3"));
    }

    // TC-WS-011: Progress percent calculation
    #[tokio::test]
    async fn test_progress_percent_calculation() {
        let broadcaster = WsBroadcaster::new();
        let job_id = Uuid::new_v4();

        let mut receiver = broadcaster.subscribe(job_id).await;

        // Test various progress values
        broadcaster.broadcast_progress(job_id, 0, 10, "Start").await;
        let msg = receiver.recv().await.unwrap();
        if let WsMessage::Progress { percent, .. } = msg {
            assert_eq!(percent, 0);
        }

        broadcaster.broadcast_progress(job_id, 10, 10, "End").await;
        let msg = receiver.recv().await.unwrap();
        if let WsMessage::Progress { percent, .. } = msg {
            assert_eq!(percent, 100);
        }

        // Edge case: zero total
        broadcaster.broadcast_progress(job_id, 5, 0, "Zero").await;
        let msg = receiver.recv().await.unwrap();
        if let WsMessage::Progress { percent, .. } = msg {
            assert_eq!(percent, 0);
        }
    }

    // TC-WS-012: Default trait implementation
    #[tokio::test]
    async fn test_default_impl() {
        let broadcaster = WsBroadcaster::default();
        assert_eq!(broadcaster.channel_count().await, 0);
        assert_eq!(broadcaster.capacity, 100);
    }

    // TC-WS-013: Broadcast without subscribers
    #[tokio::test]
    async fn test_broadcast_no_subscribers() {
        let broadcaster = WsBroadcaster::new();
        let job_id = Uuid::new_v4();

        // Should not panic
        broadcaster
            .broadcast_progress(job_id, 1, 10, "Test")
            .await;
    }

    // TC-WS-014: Multiple jobs
    #[tokio::test]
    async fn test_multiple_jobs() {
        let broadcaster = WsBroadcaster::new();
        let job1 = Uuid::new_v4();
        let job2 = Uuid::new_v4();

        let _rx1 = broadcaster.subscribe(job1).await;
        let mut rx2 = broadcaster.subscribe(job2).await;

        // Broadcast to job1 should not be received by job2
        broadcaster.broadcast_progress(job1, 1, 10, "Job1").await;

        // Broadcast to job2
        broadcaster.broadcast_progress(job2, 2, 10, "Job2").await;

        let msg = rx2.recv().await.unwrap();
        if let WsMessage::Progress {
            step_name,
            current_step,
            ..
        } = msg
        {
            assert_eq!(step_name, "Job2");
            assert_eq!(current_step, 2);
        }
    }

    // TC-WS-015: Batch progress message
    #[tokio::test]
    async fn test_broadcast_batch_progress() {
        let broadcaster = WsBroadcaster::new();
        let batch_id = Uuid::new_v4();

        let mut receiver = broadcaster.subscribe(batch_id).await;

        broadcaster
            .broadcast_batch_progress(batch_id, 3, 1, 1, 0, 5)
            .await;

        let msg = receiver.recv().await.unwrap();
        match msg {
            WsMessage::BatchProgress {
                completed,
                processing,
                pending,
                failed,
                total,
                ..
            } => {
                assert_eq!(completed, 3);
                assert_eq!(processing, 1);
                assert_eq!(pending, 1);
                assert_eq!(failed, 0);
                assert_eq!(total, 5);
            }
            _ => panic!("Expected BatchProgress message"),
        }
    }

    // TC-WS-016: Batch completed message
    #[tokio::test]
    async fn test_broadcast_batch_completed() {
        let broadcaster = WsBroadcaster::new();
        let batch_id = Uuid::new_v4();

        let mut receiver = broadcaster.subscribe(batch_id).await;

        broadcaster
            .broadcast_batch_completed(batch_id, 8, 2)
            .await;

        let msg = receiver.recv().await.unwrap();
        match msg {
            WsMessage::BatchCompleted {
                success_count,
                failed_count,
                ..
            } => {
                assert_eq!(success_count, 8);
                assert_eq!(failed_count, 2);
            }
            _ => panic!("Expected BatchCompleted message"),
        }
    }

    // TC-WS-017: Batch message serialization
    #[test]
    fn test_batch_message_serialization() {
        let batch_id = Uuid::new_v4();

        let msg = WsMessage::BatchProgress {
            batch_id,
            completed: 5,
            processing: 2,
            pending: 3,
            failed: 0,
            total: 10,
        };

        let json = serde_json::to_string(&msg).unwrap();
        assert!(json.contains("\"type\":\"batch_progress\""));
        assert!(json.contains("\"completed\":5"));
        assert!(json.contains("\"total\":10"));

        let msg = WsMessage::BatchCompleted {
            batch_id,
            success_count: 8,
            failed_count: 2,
        };

        let json = serde_json::to_string(&msg).unwrap();
        assert!(json.contains("\"type\":\"batch_completed\""));
        assert!(json.contains("\"success_count\":8"));
        assert!(json.contains("\"failed_count\":2"));
    }

    // TC-WS-018: Server shutdown message
    #[test]
    fn test_broadcast_shutdown() {
        let broadcaster = WsBroadcaster::new();
        let mut receiver = broadcaster.subscribe_global();

        broadcaster.broadcast_shutdown("graceful", 30);

        let msg = receiver.try_recv().unwrap();
        match msg {
            WsMessage::ServerShutdown {
                reason,
                countdown_secs,
            } => {
                assert_eq!(reason, "graceful");
                assert_eq!(countdown_secs, 30);
            }
            _ => panic!("Expected ServerShutdown message"),
        }
    }

    // TC-WS-019: Shutdown message serialization
    #[test]
    fn test_shutdown_message_serialization() {
        let msg = WsMessage::ServerShutdown {
            reason: "maintenance".to_string(),
            countdown_secs: 60,
        };

        let json = serde_json::to_string(&msg).unwrap();
        assert!(json.contains("\"type\":\"server_shutdown\""));
        assert!(json.contains("\"reason\":\"maintenance\""));
        assert!(json.contains("\"countdown_secs\":60"));
    }

    // TC-WS-020: Global subscribe
    #[test]
    fn test_subscribe_global() {
        let broadcaster = WsBroadcaster::new();
        let _rx1 = broadcaster.subscribe_global();
        let _rx2 = broadcaster.subscribe_global();
        // Multiple global subscribers should work
    }

    // ============ Preview Tests (Phase 4.1) ============

    // TC-WS-021: Page preview message
    #[tokio::test]
    async fn test_broadcast_page_preview() {
        let broadcaster = WsBroadcaster::new();
        let job_id = Uuid::new_v4();

        let mut receiver = broadcaster.subscribe(job_id).await;

        broadcaster
            .broadcast_page_preview(
                job_id,
                1,
                "base64data".to_string(),
                preview_stage::ORIGINAL,
                200,
                300,
            )
            .await;

        let msg = receiver.recv().await.unwrap();
        match msg {
            WsMessage::PagePreview {
                page_number,
                stage,
                width,
                height,
                ..
            } => {
                assert_eq!(page_number, 1);
                assert_eq!(stage, "original");
                assert_eq!(width, 200);
                assert_eq!(height, 300);
            }
            _ => panic!("Expected PagePreview message"),
        }
    }

    // TC-WS-022: Page preview serialization
    #[test]
    fn test_page_preview_serialization() {
        let job_id = Uuid::new_v4();

        let msg = WsMessage::PagePreview {
            job_id,
            page_number: 5,
            preview_base64: "dGVzdGRhdGE=".to_string(),
            stage: "deskewed".to_string(),
            width: 200,
            height: 280,
        };

        let json = serde_json::to_string(&msg).unwrap();
        assert!(json.contains("\"type\":\"page_preview\""));
        assert!(json.contains("\"page_number\":5"));
        assert!(json.contains("\"stage\":\"deskewed\""));
        assert!(json.contains("\"width\":200"));
    }

    // TC-WS-023: Preview stage constants
    #[test]
    fn test_preview_stage_constants() {
        assert_eq!(preview_stage::ORIGINAL, "original");
        assert_eq!(preview_stage::DESKEWED, "deskewed");
        assert_eq!(preview_stage::TRIMMED, "trimmed");
        assert_eq!(preview_stage::UPSCALED, "upscaled");
        assert_eq!(preview_stage::NORMALIZED, "normalized");
        assert_eq!(preview_stage::COLOR_CORRECTED, "color_corrected");
        assert_eq!(preview_stage::FINAL, "final");
    }

    // TC-WS-024: Preview width constant
    #[test]
    fn test_preview_width_constant() {
        assert_eq!(PREVIEW_WIDTH, 200);
    }
}
