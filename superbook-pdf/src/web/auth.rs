//! Authentication for the web server
//!
//! Provides API key based authentication.

use chrono::{DateTime, Utc};
use serde::{Deserialize, Serialize};

/// Permission scope for API keys
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "lowercase")]
pub enum Scope {
    /// Read-only access (view jobs, status)
    Read,
    /// Write access (create jobs, upload)
    Write,
    /// Admin access (all operations)
    Admin,
}

impl Scope {
    /// Check if this scope includes another scope
    pub fn includes(&self, other: Scope) -> bool {
        match self {
            Scope::Admin => true, // Admin includes all
            Scope::Write => matches!(other, Scope::Read | Scope::Write),
            Scope::Read => matches!(other, Scope::Read),
        }
    }
}

/// API key configuration
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ApiKey {
    /// The key value
    pub key: String,
    /// Key name/description
    pub name: String,
    /// Expiration time (None = never expires)
    #[serde(skip_serializing_if = "Option::is_none")]
    pub expires_at: Option<DateTime<Utc>>,
    /// Allowed scopes
    pub scopes: Vec<Scope>,
}

impl ApiKey {
    /// Create a new API key with all scopes
    pub fn new(key: impl Into<String>, name: impl Into<String>) -> Self {
        Self {
            key: key.into(),
            name: name.into(),
            expires_at: None,
            scopes: vec![Scope::Read, Scope::Write],
        }
    }

    /// Create an admin API key
    pub fn admin(key: impl Into<String>, name: impl Into<String>) -> Self {
        Self {
            key: key.into(),
            name: name.into(),
            expires_at: None,
            scopes: vec![Scope::Admin],
        }
    }

    /// Set expiration time
    pub fn with_expires_at(mut self, expires_at: DateTime<Utc>) -> Self {
        self.expires_at = Some(expires_at);
        self
    }

    /// Set scopes
    pub fn with_scopes(mut self, scopes: Vec<Scope>) -> Self {
        self.scopes = scopes;
        self
    }

    /// Check if key is expired
    pub fn is_expired(&self) -> bool {
        if let Some(expires_at) = self.expires_at {
            Utc::now() > expires_at
        } else {
            false
        }
    }

    /// Check if key has a specific scope
    pub fn has_scope(&self, scope: Scope) -> bool {
        self.scopes.iter().any(|s| s.includes(scope))
    }
}

/// Authentication configuration
#[derive(Debug, Clone)]
pub struct AuthConfig {
    /// Enable authentication
    pub enabled: bool,
    /// Valid API keys
    pub api_keys: Vec<ApiKey>,
    /// Public endpoints (no auth required)
    pub public_endpoints: Vec<String>,
}

impl Default for AuthConfig {
    fn default() -> Self {
        Self {
            enabled: false,
            api_keys: vec![],
            public_endpoints: vec!["/api/health".to_string(), "/".to_string()],
        }
    }
}

impl AuthConfig {
    /// Create a new auth config with authentication enabled
    pub fn enabled_with_keys(api_keys: Vec<ApiKey>) -> Self {
        Self {
            enabled: true,
            api_keys,
            public_endpoints: vec!["/api/health".to_string(), "/".to_string()],
        }
    }

    /// Add a public endpoint
    pub fn add_public_endpoint(&mut self, path: impl Into<String>) {
        self.public_endpoints.push(path.into());
    }
}

/// Authentication result
#[derive(Debug, Clone)]
pub enum AuthResult {
    /// Successfully authenticated
    Authenticated {
        key_name: String,
        scopes: Vec<Scope>,
    },
    /// Authentication is disabled (all requests allowed)
    Disabled,
    /// Invalid API key
    InvalidKey,
    /// API key has expired
    Expired,
    /// No API key provided
    Missing,
}

impl AuthResult {
    /// Check if authentication succeeded
    pub fn is_authenticated(&self) -> bool {
        matches!(
            self,
            AuthResult::Authenticated { .. } | AuthResult::Disabled
        )
    }

    /// Get the key name if authenticated
    pub fn key_name(&self) -> Option<&str> {
        match self {
            AuthResult::Authenticated { key_name, .. } => Some(key_name),
            _ => None,
        }
    }
}

/// Authentication manager
pub struct AuthManager {
    config: AuthConfig,
}

impl AuthManager {
    /// Create a new auth manager
    pub fn new(config: AuthConfig) -> Self {
        Self { config }
    }

    /// Check if authentication is enabled
    pub fn is_enabled(&self) -> bool {
        self.config.enabled
    }

    /// Check if a path is public (no auth required)
    pub fn is_public(&self, path: &str) -> bool {
        // If auth is disabled, everything is public
        if !self.config.enabled {
            return true;
        }

        self.config.public_endpoints.iter().any(|p| {
            if p.ends_with('*') {
                // Wildcard match
                let prefix = &p[..p.len() - 1];
                path.starts_with(prefix)
            } else {
                path == p
            }
        })
    }

    /// Validate an API key
    pub fn validate(&self, key: &str) -> AuthResult {
        // If auth is disabled, always succeed
        if !self.config.enabled {
            return AuthResult::Disabled;
        }

        // Find matching key
        for api_key in &self.config.api_keys {
            if api_key.key == key {
                if api_key.is_expired() {
                    return AuthResult::Expired;
                }
                return AuthResult::Authenticated {
                    key_name: api_key.name.clone(),
                    scopes: api_key.scopes.clone(),
                };
            }
        }

        AuthResult::InvalidKey
    }

    /// Check if a key has a specific scope
    pub fn has_scope(&self, key: &str, scope: Scope) -> bool {
        if !self.config.enabled {
            return true;
        }

        for api_key in &self.config.api_keys {
            if api_key.key == key {
                return api_key.has_scope(scope);
            }
        }

        false
    }

    /// Get the number of configured API keys
    pub fn key_count(&self) -> usize {
        self.config.api_keys.len()
    }
}

/// Extract API key from request headers
pub fn extract_api_key(authorization: Option<&str>, x_api_key: Option<&str>) -> Option<String> {
    // Try Authorization header first (Bearer token)
    if let Some(auth) = authorization {
        if let Some(key) = auth.strip_prefix("Bearer ") {
            return Some(key.to_string());
        }
    }

    // Try X-API-Key header
    if let Some(key) = x_api_key {
        return Some(key.to_string());
    }

    None
}

/// Auth status response
#[derive(Debug, Clone, Serialize)]
pub struct AuthStatusResponse {
    pub authenticated: bool,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub key_name: Option<String>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub scopes: Option<Vec<Scope>>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub expires_at: Option<String>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub auth_required: Option<bool>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub message: Option<String>,
}

impl AuthStatusResponse {
    /// Create an authenticated response
    pub fn authenticated(key_name: String, scopes: Vec<Scope>) -> Self {
        Self {
            authenticated: true,
            key_name: Some(key_name),
            scopes: Some(scopes),
            expires_at: None,
            auth_required: None,
            message: None,
        }
    }

    /// Create an unauthenticated response
    pub fn unauthenticated(auth_required: bool) -> Self {
        Self {
            authenticated: false,
            key_name: None,
            scopes: None,
            expires_at: None,
            auth_required: Some(auth_required),
            message: if auth_required {
                Some("API key required".to_string())
            } else {
                Some("Authentication not required".to_string())
            },
        }
    }

    /// Create a disabled response (no auth required)
    pub fn disabled() -> Self {
        Self {
            authenticated: true,
            key_name: None,
            scopes: None,
            expires_at: None,
            auth_required: Some(false),
            message: Some("Authentication disabled".to_string()),
        }
    }
}

/// Auth error response
#[derive(Debug, Clone, Serialize)]
pub struct AuthError {
    pub error: String,
    pub message: String,
}

impl AuthError {
    /// Create an unauthorized error
    pub fn unauthorized() -> Self {
        Self {
            error: "unauthorized".to_string(),
            message: "Invalid or missing API key".to_string(),
        }
    }

    /// Create a forbidden error
    pub fn forbidden() -> Self {
        Self {
            error: "forbidden".to_string(),
            message: "Insufficient permissions for this operation".to_string(),
        }
    }

    /// Create an expired key error
    pub fn expired() -> Self {
        Self {
            error: "unauthorized".to_string(),
            message: "API key has expired".to_string(),
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use chrono::Duration;

    // AUTH-001: AuthConfig デフォルト値
    #[test]
    fn test_auth_config_default() {
        let config = AuthConfig::default();
        assert!(!config.enabled);
        assert!(config.api_keys.is_empty());
        assert!(config.public_endpoints.contains(&"/api/health".to_string()));
        assert!(config.public_endpoints.contains(&"/".to_string()));
    }

    // AUTH-002: ApiKey 作成
    #[test]
    fn test_api_key_new() {
        let key = ApiKey::new("test-key-123", "Test Key");
        assert_eq!(key.key, "test-key-123");
        assert_eq!(key.name, "Test Key");
        assert!(key.expires_at.is_none());
        assert!(key.scopes.contains(&Scope::Read));
        assert!(key.scopes.contains(&Scope::Write));
    }

    #[test]
    fn test_api_key_admin() {
        let key = ApiKey::admin("admin-key", "Admin");
        assert!(key.scopes.contains(&Scope::Admin));
        assert!(key.has_scope(Scope::Read));
        assert!(key.has_scope(Scope::Write));
        assert!(key.has_scope(Scope::Admin));
    }

    // AUTH-003: AuthManager 作成
    #[test]
    fn test_auth_manager_new() {
        let config = AuthConfig::default();
        let manager = AuthManager::new(config);
        assert!(!manager.is_enabled());
        assert_eq!(manager.key_count(), 0);
    }

    // AUTH-004: 有効なキーで認証成功
    #[test]
    fn test_validate_valid_key() {
        let key = ApiKey::new("valid-key", "Test");
        let config = AuthConfig::enabled_with_keys(vec![key]);
        let manager = AuthManager::new(config);

        let result = manager.validate("valid-key");
        assert!(result.is_authenticated());
        assert_eq!(result.key_name(), Some("Test"));
    }

    // AUTH-005: 無効なキーで認証失敗
    #[test]
    fn test_validate_invalid_key() {
        let key = ApiKey::new("valid-key", "Test");
        let config = AuthConfig::enabled_with_keys(vec![key]);
        let manager = AuthManager::new(config);

        let result = manager.validate("wrong-key");
        assert!(!result.is_authenticated());
        assert!(matches!(result, AuthResult::InvalidKey));
    }

    // AUTH-006: 期限切れキーで認証失敗
    #[test]
    fn test_validate_expired_key() {
        let expired = Utc::now() - Duration::hours(1);
        let key = ApiKey::new("expired-key", "Expired").with_expires_at(expired);
        let config = AuthConfig::enabled_with_keys(vec![key]);
        let manager = AuthManager::new(config);

        let result = manager.validate("expired-key");
        assert!(!result.is_authenticated());
        assert!(matches!(result, AuthResult::Expired));
    }

    // AUTH-007: キー未提供時
    #[test]
    fn test_validate_missing_key() {
        let result = extract_api_key(None, None);
        assert!(result.is_none());
    }

    // AUTH-008: パブリックエンドポイント判定
    #[test]
    fn test_is_public_endpoint() {
        let config = AuthConfig::enabled_with_keys(vec![ApiKey::new("key", "Test")]);
        let manager = AuthManager::new(config);

        assert!(manager.is_public("/api/health"));
        assert!(manager.is_public("/"));
        assert!(!manager.is_public("/api/convert"));
    }

    #[test]
    fn test_is_public_wildcard() {
        let mut config = AuthConfig::enabled_with_keys(vec![ApiKey::new("key", "Test")]);
        config.add_public_endpoint("/public/*");
        let manager = AuthManager::new(config);

        assert!(manager.is_public("/public/"));
        assert!(manager.is_public("/public/anything"));
        assert!(!manager.is_public("/private/data"));
    }

    // AUTH-009: スコープ検証
    #[test]
    fn test_scope_includes() {
        assert!(Scope::Admin.includes(Scope::Read));
        assert!(Scope::Admin.includes(Scope::Write));
        assert!(Scope::Admin.includes(Scope::Admin));

        assert!(Scope::Write.includes(Scope::Read));
        assert!(Scope::Write.includes(Scope::Write));
        assert!(!Scope::Write.includes(Scope::Admin));

        assert!(Scope::Read.includes(Scope::Read));
        assert!(!Scope::Read.includes(Scope::Write));
        assert!(!Scope::Read.includes(Scope::Admin));
    }

    #[test]
    fn test_has_scope() {
        let key = ApiKey::new("key", "Test"); // Read + Write
        let config = AuthConfig::enabled_with_keys(vec![key]);
        let manager = AuthManager::new(config);

        assert!(manager.has_scope("key", Scope::Read));
        assert!(manager.has_scope("key", Scope::Write));
        assert!(!manager.has_scope("key", Scope::Admin));
    }

    // AUTH-010: Bearer トークン抽出
    #[test]
    fn test_extract_bearer_token() {
        let key = extract_api_key(Some("Bearer my-api-key"), None);
        assert_eq!(key, Some("my-api-key".to_string()));
    }

    // AUTH-011: X-API-Key ヘッダー抽出
    #[test]
    fn test_extract_x_api_key() {
        let key = extract_api_key(None, Some("my-api-key"));
        assert_eq!(key, Some("my-api-key".to_string()));
    }

    #[test]
    fn test_extract_prefers_bearer() {
        let key = extract_api_key(Some("Bearer bearer-key"), Some("x-api-key"));
        assert_eq!(key, Some("bearer-key".to_string()));
    }

    // AUTH-012: 認証無効時は全て許可
    #[test]
    fn test_auth_disabled_allows_all() {
        let config = AuthConfig::default(); // disabled
        let manager = AuthManager::new(config);

        assert!(manager.is_public("/api/convert"));
        assert!(manager.is_public("/anything"));

        let result = manager.validate("any-key");
        assert!(result.is_authenticated());
        assert!(matches!(result, AuthResult::Disabled));

        assert!(manager.has_scope("any-key", Scope::Admin));
    }

    #[test]
    fn test_api_key_is_expired() {
        let expired = Utc::now() - Duration::hours(1);
        let key = ApiKey::new("key", "Test").with_expires_at(expired);
        assert!(key.is_expired());

        let future = Utc::now() + Duration::hours(1);
        let key2 = ApiKey::new("key2", "Test2").with_expires_at(future);
        assert!(!key2.is_expired());

        let key3 = ApiKey::new("key3", "Test3"); // No expiration
        assert!(!key3.is_expired());
    }

    #[test]
    fn test_auth_status_response_authenticated() {
        let response = AuthStatusResponse::authenticated(
            "my-key".to_string(),
            vec![Scope::Read, Scope::Write],
        );
        assert!(response.authenticated);
        assert_eq!(response.key_name, Some("my-key".to_string()));
        assert!(response.scopes.is_some());
    }

    #[test]
    fn test_auth_status_response_unauthenticated() {
        let response = AuthStatusResponse::unauthenticated(true);
        assert!(!response.authenticated);
        assert_eq!(response.auth_required, Some(true));
        assert!(response.message.is_some());
    }

    #[test]
    fn test_auth_error_unauthorized() {
        let error = AuthError::unauthorized();
        assert_eq!(error.error, "unauthorized");
    }

    #[test]
    fn test_auth_error_forbidden() {
        let error = AuthError::forbidden();
        assert_eq!(error.error, "forbidden");
    }

    #[test]
    fn test_auth_error_expired() {
        let error = AuthError::expired();
        assert_eq!(error.error, "unauthorized");
        assert!(error.message.contains("expired"));
    }

    #[test]
    fn test_auth_config_enabled_with_keys() {
        let keys = vec![ApiKey::new("key1", "Key 1"), ApiKey::new("key2", "Key 2")];
        let config = AuthConfig::enabled_with_keys(keys);
        assert!(config.enabled);
        assert_eq!(config.api_keys.len(), 2);
    }

    #[test]
    fn test_api_key_with_scopes() {
        let key = ApiKey::new("key", "Test").with_scopes(vec![Scope::Read]);
        assert!(key.has_scope(Scope::Read));
        assert!(!key.has_scope(Scope::Write));
    }
}
