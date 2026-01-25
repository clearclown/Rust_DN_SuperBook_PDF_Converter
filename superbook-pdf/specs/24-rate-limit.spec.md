# 24-rate-limit.spec.md - レートリミット機能仕様

## 概要

APIの安定性と公平な利用を確保するためのレートリミット機能。

## 目的

- APIへの過度なリクエストを防止
- サーバーリソースの保護
- 公平なリソース配分
- DoS攻撃の緩和

## 設計

### アーキテクチャ

```
┌─────────────────────────────────────────────────────┐
│                     Web Server                       │
├─────────────────────────────────────────────────────┤
│  Rate Limit Middleware:                             │
│    - Token Bucket Algorithm                         │
│    - Per-IP tracking                                │
│    - Configurable limits                            │
├─────────────────────────────────────────────────────┤
│  Headers:                                           │
│    X-RateLimit-Limit: 100                          │
│    X-RateLimit-Remaining: 95                       │
│    X-RateLimit-Reset: 1704067200                   │
│    Retry-After: 60 (when limited)                  │
└─────────────────────────────────────────────────────┘
```

### 設定

```rust
#[derive(Debug, Clone)]
pub struct RateLimitConfig {
    /// 1分あたりの最大リクエスト数
    pub requests_per_minute: u32,
    /// バケットの最大トークン数
    pub burst_size: u32,
    /// トークン補充間隔 (ミリ秒)
    pub refill_interval_ms: u64,
    /// ホワイトリストIP
    pub whitelist: Vec<IpAddr>,
    /// レートリミット有効化
    pub enabled: bool,
}

impl Default for RateLimitConfig {
    fn default() -> Self {
        Self {
            requests_per_minute: 60,
            burst_size: 10,
            refill_interval_ms: 1000,
            whitelist: vec![],
            enabled: true,
        }
    }
}
```

### Token Bucket アルゴリズム

```rust
pub struct TokenBucket {
    /// 現在のトークン数
    tokens: f64,
    /// 最大トークン数
    max_tokens: f64,
    /// 秒あたりの補充レート
    refill_rate: f64,
    /// 最終更新時刻
    last_update: Instant,
}

impl TokenBucket {
    pub fn new(max_tokens: f64, refill_rate: f64) -> Self;
    pub fn try_consume(&mut self) -> bool;
    pub fn tokens_remaining(&self) -> f64;
    pub fn time_until_refill(&self) -> Duration;
}
```

### レートリミッター

```rust
pub struct RateLimiter {
    /// IP別のバケット
    buckets: DashMap<IpAddr, TokenBucket>,
    /// 設定
    config: RateLimitConfig,
    /// クリーンアップ間隔
    cleanup_interval: Duration,
}

impl RateLimiter {
    pub fn new(config: RateLimitConfig) -> Self;
    pub fn check(&self, ip: IpAddr) -> RateLimitResult;
    pub fn cleanup_expired(&self);
}

pub enum RateLimitResult {
    Allowed {
        remaining: u32,
        reset_at: u64,
    },
    Limited {
        retry_after: u64,
    },
}
```

### REST API

#### GET /api/rate-limit/status

現在のレートリミット状態を取得。

**Response:**
```json
{
  "enabled": true,
  "requests_per_minute": 60,
  "burst_size": 10,
  "your_remaining": 55,
  "reset_at": 1704067200
}
```

### HTTPヘッダー

すべてのAPIレスポンスに以下のヘッダーを追加:

| ヘッダー | 説明 |
|---------|------|
| `X-RateLimit-Limit` | 1分あたりの最大リクエスト数 |
| `X-RateLimit-Remaining` | 残りリクエスト数 |
| `X-RateLimit-Reset` | リセット時刻 (Unix timestamp) |
| `Retry-After` | 制限時、次のリクエストまでの秒数 |

### エラーレスポンス

レート制限超過時 (HTTP 429):

```json
{
  "error": "rate_limit_exceeded",
  "message": "Too many requests. Please retry after 60 seconds.",
  "retry_after": 60
}
```

## API

| 関数/構造体 | 説明 |
|-------------|------|
| `RateLimitConfig::default()` | デフォルト設定作成 |
| `TokenBucket::new()` | トークンバケット作成 |
| `TokenBucket::try_consume()` | トークン消費試行 |
| `RateLimiter::new()` | レートリミッター作成 |
| `RateLimiter::check()` | リクエストチェック |
| `rate_limit_middleware()` | Axumミドルウェア |

## テストケース

| TC ID | テスト内容 |
|-------|------------|
| RATE-001 | トークンバケット作成 |
| RATE-002 | トークン消費成功 |
| RATE-003 | トークン枯渇で拒否 |
| RATE-004 | トークン自動補充 |
| RATE-005 | レートリミッター作成 |
| RATE-006 | IP別トラッキング |
| RATE-007 | ホワイトリストIP許可 |
| RATE-008 | HTTPヘッダー付与 |
| RATE-009 | 429レスポンス |
| RATE-010 | Retry-Afterヘッダー |
| RATE-011 | 並行アクセス安全性 |
| RATE-012 | 期限切れエントリクリーンアップ |

## 実装ステータス

| 機能 | 状態 | 備考 |
|------|------|------|
| RateLimitConfig | ✅ | 完了 (rate_limit.rs) |
| TokenBucket | ✅ | 完了 (rate_limit.rs) |
| RateLimiter | ✅ | 完了 (rate_limit.rs) |
| Middleware helper | ✅ | 完了 (routes.rs) |
| /api/rate-limit/status | ✅ | 完了 (routes.rs) |
| 統合テスト | ✅ | 完了 (23テスト) |

## CLIオプション

```
--rate-limit <N>        1分あたりの最大リクエスト数 (デフォルト: 60)
--rate-limit-burst <N>  バーストサイズ (デフォルト: 10)
--no-rate-limit         レートリミット無効化
```

## 依存クレート

- `dashmap` - 並行ハッシュマップ (既存依存)
- `std::time::Instant` - 時間計測
