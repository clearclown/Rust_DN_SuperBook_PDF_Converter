# 22-batch.spec.md - バッチ処理API仕様

## 概要

複数PDFファイルの一括変換API。キュー管理とバッチジョブ追跡機能を提供。

## 目的

- 複数ファイルの効率的な一括処理
- バッチジョブの進捗追跡
- ジョブ優先度管理
- リソース使用量の最適化

## 設計

### アーキテクチャ

```
┌─────────────────────────────────────────────────────┐
│                     Web Server                       │
├─────────────────────────────────────────────────────┤
│  Batch API:                                         │
│    POST /api/batch           - バッチジョブ作成     │
│    GET  /api/batch/:id       - バッチ状態取得       │
│    GET  /api/batch/:id/jobs  - 個別ジョブ一覧       │
│    DELETE /api/batch/:id     - バッチキャンセル     │
├─────────────────────────────────────────────────────┤
│  Internal:                                          │
│    - BatchQueue              - バッチキュー管理     │
│    - BatchScheduler          - ジョブスケジューリング│
│    - ResourceManager         - リソース制限管理     │
└─────────────────────────────────────────────────────┘
```

### REST API

#### POST /api/batch

複数PDFファイルをアップロードしてバッチジョブを作成。

**Request:**
```
Content-Type: multipart/form-data

files[]: <PDF binary 1>
files[]: <PDF binary 2>
...
options: {
  "dpi": 300,
  "deskew": true,
  "upscale": true,
  "ocr": false,
  "advanced": false,
  "priority": "normal"
}
```

**Response:**
```json
{
  "batch_id": "uuid-v4",
  "status": "queued",
  "job_count": 5,
  "created_at": "2024-01-01T00:00:00Z"
}
```

#### GET /api/batch/:id

バッチジョブの状態を取得。

**Response:**
```json
{
  "batch_id": "uuid-v4",
  "status": "processing",
  "progress": {
    "completed": 3,
    "processing": 1,
    "pending": 1,
    "failed": 0,
    "total": 5
  },
  "created_at": "2024-01-01T00:00:00Z",
  "started_at": "2024-01-01T00:00:10Z",
  "estimated_completion": "2024-01-01T00:05:00Z"
}
```

#### GET /api/batch/:id/jobs

バッチ内の個別ジョブ一覧を取得。

**Response:**
```json
{
  "batch_id": "uuid-v4",
  "jobs": [
    {
      "job_id": "uuid-1",
      "filename": "document1.pdf",
      "status": "completed",
      "download_url": "/api/jobs/uuid-1/download"
    },
    {
      "job_id": "uuid-2",
      "filename": "document2.pdf",
      "status": "processing",
      "progress": { "percent": 45, "step_name": "Deskew" }
    }
  ]
}
```

#### DELETE /api/batch/:id

バッチジョブ全体をキャンセル。

**Response:**
```json
{
  "batch_id": "uuid-v4",
  "status": "cancelled",
  "cancelled_jobs": 2,
  "completed_jobs": 3
}
```

### データ構造

```rust
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct BatchJob {
    pub id: Uuid,
    pub status: BatchStatus,
    pub options: ConvertOptions,
    pub jobs: Vec<Uuid>,
    pub priority: Priority,
    pub created_at: DateTime<Utc>,
    pub started_at: Option<DateTime<Utc>>,
    pub completed_at: Option<DateTime<Utc>>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub enum BatchStatus {
    Queued,
    Processing,
    Completed,
    PartiallyCompleted,
    Failed,
    Cancelled,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub enum Priority {
    Low,
    Normal,
    High,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct BatchProgress {
    pub completed: usize,
    pub processing: usize,
    pub pending: usize,
    pub failed: usize,
    pub total: usize,
}

pub struct BatchQueue {
    batches: RwLock<HashMap<Uuid, BatchJob>>,
    job_queue: JobQueue,
}
```

### WebSocket拡張

```rust
#[derive(Debug, Clone, Serialize)]
#[serde(tag = "type", content = "data")]
pub enum WsBatchMessage {
    /// バッチ進捗更新
    #[serde(rename = "batch_progress")]
    BatchProgress {
        batch_id: Uuid,
        completed: usize,
        total: usize,
    },
    /// バッチ完了通知
    #[serde(rename = "batch_completed")]
    BatchCompleted {
        batch_id: Uuid,
        success_count: usize,
        failed_count: usize,
    },
}
```

## API

| 関数/構造体 | 説明 |
|-------------|------|
| `BatchJob::new()` | バッチジョブ作成 |
| `BatchQueue::submit()` | バッチ投入 |
| `BatchQueue::get()` | バッチ取得 |
| `BatchQueue::cancel()` | バッチキャンセル |
| `BatchScheduler::schedule()` | ジョブスケジューリング |

## テストケース

| TC ID | テスト内容 |
|-------|------------|
| BATCH-001 | バッチジョブ作成 |
| BATCH-002 | 複数ファイルアップロード |
| BATCH-003 | バッチ進捗追跡 |
| BATCH-004 | 個別ジョブ一覧取得 |
| BATCH-005 | バッチキャンセル |
| BATCH-006 | 部分完了ステータス |
| BATCH-007 | 優先度による順序制御 |
| BATCH-008 | リソース制限 (同時処理数) |
| BATCH-009 | WebSocketバッチ進捗通知 |
| BATCH-010 | エラー時のリカバリー |

## 実装ステータス

| 機能 | 状態 | 備考 |
|------|------|------|
| BatchJob構造体 | ✅ | 完了 (batch.rs) |
| BatchQueue | ✅ | 完了 (batch.rs) |
| REST API | ✅ | 完了 (routes.rs) |
| WebSocket拡張 | ✅ | 完了 (websocket.rs) |
| 統合テスト | ✅ | 完了 (web_integration.rs) |

## 依存クレート

既存の依存関係で実装可能。

## 注意事項

- 最大バッチサイズ: 100ファイル
- 同時処理数: ワーカー数に依存
- メモリ使用量: ファイル数 × 平均サイズに注意
- タイムアウト: 個別ジョブのタイムアウトを適用
