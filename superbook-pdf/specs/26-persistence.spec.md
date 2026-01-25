# 26-persistence.spec.md - ジョブ永続化機能仕様

## 概要

サーバー再起動後もジョブ状態を保持し、未完了ジョブを復旧する機能。

## 目的

- サーバークラッシュ時のジョブ復旧
- 処理中ジョブの自動再開
- ジョブ履歴の永続保存
- 運用信頼性の向上

## 設計

### アーキテクチャ

```
┌─────────────────────────────────────────────────────┐
│                     Web Server                       │
├─────────────────────────────────────────────────────┤
│  JobStore:                                          │
│    - SQLite backend                                 │
│    - File-based fallback                            │
│    - In-memory cache                                │
├─────────────────────────────────────────────────────┤
│  Recovery:                                          │
│    - Auto-resume on startup                         │
│    - Stale job cleanup                              │
│    - Failed job retry                               │
└─────────────────────────────────────────────────────┘
```

### 設定

```rust
#[derive(Debug, Clone)]
pub struct PersistenceConfig {
    /// 永続化有効化
    pub enabled: bool,
    /// ストレージパス
    pub storage_path: PathBuf,
    /// バックエンド種別
    pub backend: StorageBackend,
    /// 自動保存間隔 (秒)
    pub auto_save_interval: u64,
    /// 履歴保持期間 (日)
    pub retention_days: u32,
}

#[derive(Debug, Clone, Copy)]
pub enum StorageBackend {
    /// JSONファイル (シンプル)
    Json,
    /// SQLite (推奨)
    Sqlite,
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
```

### ジョブストア

```rust
pub trait JobStore: Send + Sync {
    /// ジョブ保存
    fn save(&self, job: &Job) -> Result<(), StoreError>;
    /// ジョブ取得
    fn get(&self, id: Uuid) -> Result<Option<Job>, StoreError>;
    /// 全ジョブ取得
    fn list(&self) -> Result<Vec<Job>, StoreError>;
    /// ジョブ削除
    fn delete(&self, id: Uuid) -> Result<(), StoreError>;
    /// 未完了ジョブ取得
    fn get_pending(&self) -> Result<Vec<Job>, StoreError>;
    /// 古いジョブ削除
    fn cleanup(&self, older_than: DateTime<Utc>) -> Result<usize, StoreError>;
}
```

### JSONストア実装

```rust
pub struct JsonJobStore {
    path: PathBuf,
    cache: RwLock<HashMap<Uuid, Job>>,
}

impl JsonJobStore {
    pub fn new(path: PathBuf) -> Result<Self, StoreError>;
    pub fn load(&self) -> Result<(), StoreError>;
    pub fn flush(&self) -> Result<(), StoreError>;
}
```

### リカバリーマネージャー

```rust
pub struct RecoveryManager {
    store: Arc<dyn JobStore>,
    queue: JobQueue,
}

impl RecoveryManager {
    pub fn new(store: Arc<dyn JobStore>, queue: JobQueue) -> Self;
    /// 起動時リカバリー
    pub async fn recover_on_startup(&self) -> RecoveryResult;
    /// 処理中ジョブをキューに戻す
    pub async fn requeue_processing(&self) -> usize;
    /// 失敗ジョブのリトライ
    pub async fn retry_failed(&self, max_retries: u32) -> usize;
}

pub struct RecoveryResult {
    pub recovered: usize,
    pub requeued: usize,
    pub failed: usize,
}
```

### REST API

#### GET /api/jobs/history

ジョブ履歴取得。

**Query Parameters:**
- `limit` - 取得件数 (デフォルト: 50)
- `offset` - オフセット
- `status` - ステータスフィルター

**Response:**
```json
{
  "jobs": [...],
  "total": 150,
  "limit": 50,
  "offset": 0
}
```

#### POST /api/jobs/{id}/retry

失敗ジョブのリトライ。

**Response:**
```json
{
  "job_id": "...",
  "status": "queued",
  "message": "Job requeued for processing"
}
```

## API

| 関数/構造体 | 説明 |
|-------------|------|
| `PersistenceConfig::default()` | デフォルト設定 |
| `JsonJobStore::new()` | JSONストア作成 |
| `JsonJobStore::load()` | ファイル読み込み |
| `JsonJobStore::flush()` | ファイル書き込み |
| `RecoveryManager::recover_on_startup()` | 起動時リカバリー |

## テストケース

| TC ID | テスト内容 |
|-------|------------|
| PERSIST-001 | PersistenceConfig デフォルト値 |
| PERSIST-002 | JsonJobStore 作成 |
| PERSIST-003 | ジョブ保存 |
| PERSIST-004 | ジョブ取得 |
| PERSIST-005 | ジョブ一覧取得 |
| PERSIST-006 | ジョブ削除 |
| PERSIST-007 | 未完了ジョブ取得 |
| PERSIST-008 | ファイル永続化 |
| PERSIST-009 | ファイル読み込み |
| PERSIST-010 | 古いジョブクリーンアップ |
| PERSIST-011 | 起動時リカバリー |
| PERSIST-012 | 処理中ジョブ再キュー |

## 実装ステータス

| 機能 | 状態 | 備考 |
|------|------|------|
| PersistenceConfig | ✅ | 完了 (persistence.rs) |
| JobStore trait | ✅ | 完了 (persistence.rs) |
| JsonJobStore | ✅ | 完了 (persistence.rs) |
| RecoveryManager | ✅ | 完了 (persistence.rs) |
| /api/jobs/history | ✅ | 完了 (routes.rs) |
| /api/jobs/{id}/retry | ✅ | 完了 (routes.rs) |
| 統合テスト | ✅ | 完了 (24テスト) |

## CLIオプション

```
--persist              ジョブ永続化有効化
--data-dir <PATH>      データ保存ディレクトリ
--retention-days <N>   履歴保持日数 (デフォルト: 30)
```
