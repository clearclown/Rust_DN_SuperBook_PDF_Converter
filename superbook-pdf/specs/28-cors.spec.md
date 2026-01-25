# 28-cors.spec.md - CORS設定機能仕様

## 概要

ブラウザからのクロスオリジンリクエストを制御するCORS (Cross-Origin Resource Sharing) 設定。

## 目的

- ブラウザからのAPI直接アクセス許可
- セキュリティ制御（許可オリジン指定）
- プリフライトリクエスト対応

## 設計

### アーキテクチャ

```
Browser Request
       │
       ▼
┌─────────────────────────────────────────────────────┐
│                   CORS Layer                         │
├─────────────────────────────────────────────────────┤
│  Preflight (OPTIONS):                               │
│    - Access-Control-Allow-Origin                    │
│    - Access-Control-Allow-Methods                   │
│    - Access-Control-Allow-Headers                   │
│    - Access-Control-Max-Age                         │
├─────────────────────────────────────────────────────┤
│  Actual Request:                                    │
│    - Origin validation                              │
│    - Response headers injection                     │
└─────────────────────────────────────────────────────┘
```

### 設定

```rust
#[derive(Debug, Clone)]
pub struct CorsConfig {
    /// CORS有効化
    pub enabled: bool,
    /// 許可オリジン (None = 全て許可)
    pub allowed_origins: Option<Vec<String>>,
    /// 許可メソッド
    pub allowed_methods: Vec<String>,
    /// 許可ヘッダー
    pub allowed_headers: Vec<String>,
    /// レスポンスに含める公開ヘッダー
    pub expose_headers: Vec<String>,
    /// クレデンシャル許可
    pub allow_credentials: bool,
    /// プリフライトキャッシュ時間 (秒)
    pub max_age: u64,
}

impl Default for CorsConfig {
    fn default() -> Self {
        Self {
            enabled: true,
            allowed_origins: None, // 全オリジン許可
            allowed_methods: vec![
                "GET".to_string(),
                "POST".to_string(),
                "PUT".to_string(),
                "DELETE".to_string(),
                "OPTIONS".to_string(),
            ],
            allowed_headers: vec![
                "Content-Type".to_string(),
                "Authorization".to_string(),
                "X-API-Key".to_string(),
            ],
            expose_headers: vec![
                "X-RateLimit-Limit".to_string(),
                "X-RateLimit-Remaining".to_string(),
                "X-RateLimit-Reset".to_string(),
            ],
            allow_credentials: false,
            max_age: 86400, // 24時間
        }
    }
}
```

### CorsLayer ビルダー

```rust
impl CorsConfig {
    /// 開発用設定 (全て許可)
    pub fn permissive() -> Self;

    /// 本番用設定 (指定オリジンのみ)
    pub fn strict(origins: Vec<String>) -> Self;

    /// 無効化
    pub fn disabled() -> Self;

    /// tower_http::cors::CorsLayer に変換
    pub fn into_layer(self) -> CorsLayer;
}
```

## API

| 関数/構造体 | 説明 |
|-------------|------|
| `CorsConfig::default()` | デフォルト設定 |
| `CorsConfig::permissive()` | 開発用（全許可） |
| `CorsConfig::strict()` | 本番用（指定オリジン） |
| `CorsConfig::disabled()` | CORS無効化 |
| `CorsConfig::into_layer()` | tower CorsLayer変換 |

## テストケース

| TC ID | テスト内容 |
|-------|------------|
| CORS-001 | CorsConfig デフォルト値 |
| CORS-002 | CorsConfig permissive |
| CORS-003 | CorsConfig strict |
| CORS-004 | CorsConfig disabled |
| CORS-005 | CorsLayer 変換 |
| CORS-006 | 許可オリジン判定 |
| CORS-007 | メソッド許可判定 |
| CORS-008 | ヘッダー許可判定 |
| CORS-009 | クレデンシャル設定 |
| CORS-010 | max_age 設定 |

## 実装ステータス

| 機能 | 状態 | 備考 |
|------|------|------|
| CorsConfig | ✅ | 完了 (cors.rs) |
| CorsLayer変換 | ✅ | 完了 (into_layer) |
| サーバー統合 | ✅ | 完了 (server.rs) |
| テスト | ✅ | 完了 (22テスト) |

## CLIオプション

```
--cors                     CORS有効化 (デフォルト: 有効)
--cors-origin <ORIGIN>     許可オリジン (複数指定可)
--no-cors                  CORS無効化
```

## 依存関係

- `tower-http` - CorsLayer (既存依存)
