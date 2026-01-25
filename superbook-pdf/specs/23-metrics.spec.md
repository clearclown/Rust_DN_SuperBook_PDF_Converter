# 23-metrics.spec.md - メトリクス・監視機能仕様

## 概要

サーバーの稼働状況、パフォーマンス、リソース使用量を監視するためのメトリクス機能。

## 目的

- サーバー稼働状況のリアルタイム監視
- パフォーマンスボトルネックの特定
- リソース使用量の追跡
- 運用問題の早期検出

## 設計

### アーキテクチャ

```
┌─────────────────────────────────────────────────────┐
│                     Web Server                       │
├─────────────────────────────────────────────────────┤
│  Metrics API:                                       │
│    GET  /api/metrics         - Prometheusフォーマット│
│    GET  /api/stats           - JSONステータス       │
│    GET  /api/stats/jobs      - ジョブ統計           │
│    GET  /api/stats/system    - システム情報         │
├─────────────────────────────────────────────────────┤
│  Internal:                                          │
│    - MetricsCollector        - メトリクス収集       │
│    - SystemMonitor           - システム監視         │
│    - JobStatistics           - ジョブ統計           │
└─────────────────────────────────────────────────────┘
```

### メトリクス

#### ジョブ統計

```rust
#[derive(Debug, Clone, Serialize)]
pub struct JobStatistics {
    /// 総ジョブ数
    pub total_jobs: u64,
    /// 完了ジョブ数
    pub completed_jobs: u64,
    /// 失敗ジョブ数
    pub failed_jobs: u64,
    /// 現在処理中のジョブ数
    pub active_jobs: u64,
    /// キュー待ちジョブ数
    pub queued_jobs: u64,
    /// 平均処理時間 (秒)
    pub avg_processing_time: f64,
    /// 総処理ページ数
    pub total_pages_processed: u64,
}
```

#### システムメトリクス

```rust
#[derive(Debug, Clone, Serialize)]
pub struct SystemMetrics {
    /// サーバー稼働時間 (秒)
    pub uptime_seconds: u64,
    /// メモリ使用量 (bytes)
    pub memory_used: u64,
    /// CPU使用率 (%)
    pub cpu_usage: f32,
    /// ディスク使用量 (bytes)
    pub disk_used: u64,
    /// ワーカー数
    pub worker_count: usize,
    /// アクティブWebSocket接続数
    pub websocket_connections: usize,
}
```

### REST API

#### GET /api/metrics

Prometheus形式でメトリクスを出力。

```
# HELP superbook_jobs_total Total number of jobs
# TYPE superbook_jobs_total counter
superbook_jobs_total{status="completed"} 150
superbook_jobs_total{status="failed"} 5
superbook_jobs_total{status="processing"} 2
superbook_jobs_total{status="queued"} 10

# HELP superbook_processing_seconds Job processing time
# TYPE superbook_processing_seconds histogram
superbook_processing_seconds_bucket{le="10"} 50
superbook_processing_seconds_bucket{le="30"} 100
superbook_processing_seconds_bucket{le="60"} 140
superbook_processing_seconds_bucket{le="+Inf"} 150

# HELP superbook_pages_processed_total Total pages processed
# TYPE superbook_pages_processed_total counter
superbook_pages_processed_total 5000

# HELP superbook_uptime_seconds Server uptime
# TYPE superbook_uptime_seconds gauge
superbook_uptime_seconds 86400
```

#### GET /api/stats

JSON形式で詳細統計を返す。

**Response:**
```json
{
  "server": {
    "version": "0.7.0",
    "uptime_seconds": 86400,
    "started_at": "2024-01-01T00:00:00Z"
  },
  "jobs": {
    "total": 167,
    "completed": 150,
    "failed": 5,
    "processing": 2,
    "queued": 10,
    "avg_processing_time": 45.5,
    "total_pages": 5000
  },
  "batches": {
    "total": 20,
    "completed": 18,
    "processing": 2
  },
  "system": {
    "memory_used_mb": 512,
    "worker_count": 4,
    "websocket_connections": 3
  }
}
```

### データ構造

```rust
use std::sync::atomic::{AtomicU64, Ordering};
use std::time::Instant;

/// メトリクス収集器
pub struct MetricsCollector {
    /// サーバー起動時刻
    started_at: Instant,
    /// 総ジョブ数
    total_jobs: AtomicU64,
    /// 完了ジョブ数
    completed_jobs: AtomicU64,
    /// 失敗ジョブ数
    failed_jobs: AtomicU64,
    /// 総処理時間 (ミリ秒)
    total_processing_ms: AtomicU64,
    /// 総処理ページ数
    total_pages: AtomicU64,
}

impl MetricsCollector {
    pub fn new() -> Self;
    pub fn record_job_started(&self);
    pub fn record_job_completed(&self, duration_ms: u64, pages: u64);
    pub fn record_job_failed(&self);
    pub fn get_statistics(&self) -> JobStatistics;
    pub fn get_uptime(&self) -> u64;
}
```

## API

| 関数/構造体 | 説明 |
|-------------|------|
| `MetricsCollector::new()` | メトリクス収集器作成 |
| `MetricsCollector::record_job_completed()` | ジョブ完了記録 |
| `MetricsCollector::get_statistics()` | 統計取得 |
| `format_prometheus()` | Prometheus形式出力 |

## テストケース

| TC ID | テスト内容 |
|-------|------------|
| METRICS-001 | メトリクス収集器作成 |
| METRICS-002 | ジョブ完了記録 |
| METRICS-003 | ジョブ失敗記録 |
| METRICS-004 | 平均処理時間計算 |
| METRICS-005 | Prometheus形式出力 |
| METRICS-006 | JSON統計出力 |
| METRICS-007 | 稼働時間計算 |
| METRICS-008 | 並行アクセス安全性 |
| METRICS-009 | バッチ統計 |
| METRICS-010 | WebSocket接続数 |

## 実装ステータス

| 機能 | 状態 | 備考 |
|------|------|------|
| MetricsCollector | ✅ | 完了 (metrics.rs) |
| /api/metrics | ✅ | 完了 (routes.rs) |
| /api/stats | ✅ | 完了 (routes.rs) |
| 統合テスト | ✅ | 完了 (12テスト) |

## 依存クレート

既存の依存関係で実装可能。
- `std::sync::atomic` - ロックフリーカウンター
- `std::time::Instant` - 時間計測
