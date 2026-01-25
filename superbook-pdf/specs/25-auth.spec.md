# 25-auth.spec.md - 認証・APIキー機能仕様

## 概要

APIへのアクセス制御のためのシンプルなAPIキー認証機能。

## 目的

- APIへの不正アクセス防止
- ユーザー/アプリケーション識別
- 使用量トラッキングの基盤

## 設計

### アーキテクチャ

```
┌─────────────────────────────────────────────────────┐
│                     Web Server                       │
├─────────────────────────────────────────────────────┤
│  Auth Middleware:                                   │
│    - API Key validation                             │
│    - Bearer token support                           │
│    - Optional (configurable)                        │
├─────────────────────────────────────────────────────┤
│  Headers:                                           │
│    Authorization: Bearer <api-key>                  │
│    X-API-Key: <api-key>                            │
└─────────────────────────────────────────────────────┘
```

### 設定

```rust
#[derive(Debug, Clone)]
pub struct AuthConfig {
    /// 認証有効化
    pub enabled: bool,
    /// 有効なAPIキー一覧
    pub api_keys: Vec<ApiKey>,
    /// 認証不要エンドポイント
    pub public_endpoints: Vec<String>,
}

#[derive(Debug, Clone)]
pub struct ApiKey {
    /// キー値 (ハッシュ化推奨)
    pub key: String,
    /// キー名/説明
    pub name: String,
    /// 有効期限 (None = 無期限)
    pub expires_at: Option<DateTime<Utc>>,
    /// 許可スコープ
    pub scopes: Vec<Scope>,
}

#[derive(Debug, Clone, PartialEq)]
pub enum Scope {
    /// 読み取りのみ
    Read,
    /// 書き込み (変換実行)
    Write,
    /// 管理者
    Admin,
}

impl Default for AuthConfig {
    fn default() -> Self {
        Self {
            enabled: false,
            api_keys: vec![],
            public_endpoints: vec![
                "/api/health".to_string(),
                "/".to_string(),
            ],
        }
    }
}
```

### 認証マネージャー

```rust
pub struct AuthManager {
    config: AuthConfig,
}

impl AuthManager {
    pub fn new(config: AuthConfig) -> Self;
    pub fn validate(&self, key: &str) -> AuthResult;
    pub fn is_public(&self, path: &str) -> bool;
    pub fn has_scope(&self, key: &str, scope: Scope) -> bool;
}

pub enum AuthResult {
    /// 認証成功
    Authenticated { key_name: String, scopes: Vec<Scope> },
    /// 認証無効 (認証不要)
    Disabled,
    /// 無効なキー
    InvalidKey,
    /// キー期限切れ
    Expired,
    /// キー未提供
    Missing,
}
```

### REST API

#### GET /api/auth/status

認証状態を取得。

**Response (認証済み):**
```json
{
  "authenticated": true,
  "key_name": "my-app",
  "scopes": ["read", "write"],
  "expires_at": "2025-12-31T23:59:59Z"
}
```

**Response (未認証):**
```json
{
  "authenticated": false,
  "auth_required": true,
  "message": "API key required"
}
```

### HTTPヘッダー

認証方法 (どちらか一方):

| ヘッダー | 形式 |
|---------|------|
| `Authorization` | `Bearer <api-key>` |
| `X-API-Key` | `<api-key>` |

### エラーレスポンス

認証失敗時 (HTTP 401):

```json
{
  "error": "unauthorized",
  "message": "Invalid or missing API key"
}
```

権限不足時 (HTTP 403):

```json
{
  "error": "forbidden",
  "message": "Insufficient permissions for this operation"
}
```

## API

| 関数/構造体 | 説明 |
|-------------|---------|
| `AuthConfig::default()` | デフォルト設定 (認証無効) |
| `ApiKey::new()` | APIキー作成 |
| `AuthManager::new()` | 認証マネージャー作成 |
| `AuthManager::validate()` | キー検証 |
| `AuthManager::is_public()` | パブリックエンドポイント判定 |
| `extract_api_key()` | リクエストからキー抽出 |

## テストケース

| TC ID | テスト内容 |
|-------|------------|
| AUTH-001 | AuthConfig デフォルト値 |
| AUTH-002 | ApiKey 作成 |
| AUTH-003 | AuthManager 作成 |
| AUTH-004 | 有効なキーで認証成功 |
| AUTH-005 | 無効なキーで認証失敗 |
| AUTH-006 | 期限切れキーで認証失敗 |
| AUTH-007 | キー未提供で認証失敗 |
| AUTH-008 | パブリックエンドポイント判定 |
| AUTH-009 | スコープ検証 |
| AUTH-010 | Bearer トークン抽出 |
| AUTH-011 | X-API-Key ヘッダー抽出 |
| AUTH-012 | 認証無効時は全て許可 |

## 実装ステータス

| 機能 | 状態 | 備考 |
|------|------|------|
| AuthConfig | ✅ | 完了 (auth.rs) |
| ApiKey | ✅ | 完了 (auth.rs) |
| AuthManager | ✅ | 完了 (auth.rs) |
| Scope | ✅ | 完了 (Read/Write/Admin) |
| /api/auth/status | ✅ | 完了 (routes.rs) |
| 統合テスト | ✅ | 完了 (27テスト) |

## CLIオプション

```
--api-key <KEY>         APIキーを追加
--auth-required         認証を必須化
--no-auth               認証を無効化 (デフォルト)
```

## 環境変数

```
SUPERBOOK_API_KEY=<key>     単一APIキー設定
SUPERBOOK_API_KEYS=<k1,k2>  複数APIキー設定 (カンマ区切り)
SUPERBOOK_AUTH_REQUIRED=1   認証必須化
```
