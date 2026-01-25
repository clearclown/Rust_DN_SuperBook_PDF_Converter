# 27-graceful-shutdown.spec.md - グレースフルシャットダウン機能仕様

## 概要

サーバー終了時に処理中のジョブを安全に完了させ、データ損失を防ぐ機能。

## 目的

- 処理中ジョブの安全な完了
- 永続化データの確実な保存
- WebSocket接続の正常切断
- リソースの適切な解放

## 設計

### アーキテクチャ

```
SIGTERM/SIGINT受信
       │
       ▼
┌─────────────────────────────────────────────────────┐
│                  Shutdown Coordinator                │
├─────────────────────────────────────────────────────┤
│  1. 新規リクエストの受付停止                          │
│  2. WebSocket クライアントへ通知                      │
│  3. 処理中ジョブの完了待機                            │
│  4. JobStore のフラッシュ                            │
│  5. ワーカープールの停止                              │
│  6. HTTPサーバーの停止                               │
└─────────────────────────────────────────────────────┘
```

### 設定

```rust
#[derive(Debug, Clone)]
pub struct ShutdownConfig {
    /// シャットダウンタイムアウト (秒)
    pub timeout_secs: u64,
    /// 処理中ジョブの完了を待つか
    pub wait_for_jobs: bool,
    /// WebSocket接続の切断待機時間 (ミリ秒)
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
```

### シャットダウンコーディネーター

```rust
pub struct ShutdownCoordinator {
    config: ShutdownConfig,
    shutdown_tx: broadcast::Sender<()>,
    shutdown_rx: broadcast::Receiver<()>,
}

impl ShutdownCoordinator {
    pub fn new(config: ShutdownConfig) -> Self;

    /// シャットダウンシグナルを送信
    pub fn trigger_shutdown(&self);

    /// シャットダウンシグナルを待機
    pub async fn wait_for_shutdown(&mut self);

    /// シャットダウン状態を確認
    pub fn is_shutting_down(&self) -> bool;
}
```

### グレースフルシャットダウン処理

```rust
pub async fn graceful_shutdown(
    coordinator: ShutdownCoordinator,
    worker_pool: WorkerPool,
    job_store: Option<Arc<dyn JobStore>>,
    broadcaster: Arc<WsBroadcaster>,
) -> ShutdownResult {
    // 1. WebSocket クライアントへ通知
    broadcaster.broadcast_shutdown().await;

    // 2. ワーカープールの新規ジョブ受付停止
    worker_pool.pause();

    // 3. 処理中ジョブの完了待機
    let timeout = Duration::from_secs(coordinator.config.timeout_secs);
    match tokio::time::timeout(timeout, worker_pool.drain()).await {
        Ok(_) => println!("All jobs completed"),
        Err(_) => println!("Timeout waiting for jobs"),
    }

    // 4. JobStore のフラッシュ
    if let Some(store) = job_store {
        store.flush()?;
    }

    // 5. ワーカープールの停止
    worker_pool.shutdown().await;

    ShutdownResult::Success
}
```

### シャットダウン結果

```rust
#[derive(Debug)]
pub enum ShutdownResult {
    /// 全ジョブ完了後にシャットダウン
    Success,
    /// タイムアウトによる強制シャットダウン
    Timeout { pending_jobs: usize },
    /// エラーによるシャットダウン
    Error(String),
}
```

### シグナルハンドリング

```rust
pub async fn setup_signal_handlers(
    coordinator: ShutdownCoordinator,
) {
    let ctrl_c = tokio::signal::ctrl_c();

    #[cfg(unix)]
    let terminate = async {
        tokio::signal::unix::signal(
            tokio::signal::unix::SignalKind::terminate()
        )
        .expect("Failed to setup SIGTERM handler")
        .recv()
        .await;
    };

    #[cfg(not(unix))]
    let terminate = std::future::pending::<()>();

    tokio::select! {
        _ = ctrl_c => coordinator.trigger_shutdown(),
        _ = terminate => coordinator.trigger_shutdown(),
    }
}
```

## API

| 関数/構造体 | 説明 |
|-------------|------|
| `ShutdownConfig::default()` | デフォルト設定 (30秒タイムアウト) |
| `ShutdownCoordinator::new()` | コーディネーター作成 |
| `ShutdownCoordinator::trigger_shutdown()` | シャットダウン開始 |
| `graceful_shutdown()` | グレースフルシャットダウン実行 |
| `setup_signal_handlers()` | シグナルハンドラー設定 |

## テストケース

| TC ID | テスト内容 |
|-------|------------|
| SHUT-001 | ShutdownConfig デフォルト値 |
| SHUT-002 | ShutdownCoordinator 作成 |
| SHUT-003 | シャットダウンシグナル送受信 |
| SHUT-004 | 複数リスナーへのブロードキャスト |
| SHUT-005 | ジョブ完了待機 (正常完了) |
| SHUT-006 | ジョブ完了待機 (タイムアウト) |
| SHUT-007 | JobStore フラッシュ |
| SHUT-008 | WebSocket 通知 |
| SHUT-009 | ShutdownResult 各バリアント |
| SHUT-010 | 統合テスト (シグナル→完了) |

## 実装ステータス

| 機能 | 状態 | 備考 |
|------|------|------|
| ShutdownConfig | ✅ | 完了 (shutdown.rs) |
| ShutdownCoordinator | ✅ | 完了 (shutdown.rs) |
| graceful_shutdown | ✅ | 完了 (shutdown.rs) |
| シグナルハンドラー | ✅ | 完了 (wait_for_shutdown_signal) |
| サーバー統合 | ✅ | 完了 (server.rs) |
| WebSocket通知 | ⏳ | 未実装 |
| 統合テスト | ✅ | 完了 (14テスト) |

## CLIオプション

```
--shutdown-timeout <SECS>   シャットダウンタイムアウト (デフォルト: 30)
--no-wait-for-jobs          処理中ジョブを待たずにシャットダウン
```

## 依存関係

- `tokio::signal` - シグナルハンドリング
- `tokio::sync::broadcast` - シャットダウン通知
