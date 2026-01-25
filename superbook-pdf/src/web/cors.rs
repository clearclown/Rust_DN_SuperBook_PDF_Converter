//! CORS (Cross-Origin Resource Sharing) configuration
//!
//! Provides configurable CORS support for the web server.

use std::time::Duration;
use tower_http::cors::{Any, CorsLayer};

/// CORS configuration
#[derive(Debug, Clone)]
pub struct CorsConfig {
    /// Enable CORS
    pub enabled: bool,
    /// Allowed origins (None = allow all)
    pub allowed_origins: Option<Vec<String>>,
    /// Allowed HTTP methods
    pub allowed_methods: Vec<String>,
    /// Allowed request headers
    pub allowed_headers: Vec<String>,
    /// Headers to expose in responses
    pub expose_headers: Vec<String>,
    /// Allow credentials (cookies, authorization headers)
    pub allow_credentials: bool,
    /// Preflight cache duration in seconds
    pub max_age_secs: u64,
}

impl Default for CorsConfig {
    fn default() -> Self {
        Self {
            enabled: true,
            allowed_origins: None, // Allow all origins
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
            max_age_secs: 86400, // 24 hours
        }
    }
}

impl CorsConfig {
    /// Create a permissive config (allow everything) - for development
    pub fn permissive() -> Self {
        Self {
            enabled: true,
            allowed_origins: None,
            allowed_methods: vec![
                "GET".to_string(),
                "POST".to_string(),
                "PUT".to_string(),
                "PATCH".to_string(),
                "DELETE".to_string(),
                "OPTIONS".to_string(),
                "HEAD".to_string(),
            ],
            allowed_headers: vec!["*".to_string()],
            expose_headers: vec!["*".to_string()],
            allow_credentials: true,
            max_age_secs: 86400,
        }
    }

    /// Create a strict config with specific origins - for production
    pub fn strict(origins: Vec<String>) -> Self {
        Self {
            enabled: true,
            allowed_origins: Some(origins),
            allowed_methods: vec![
                "GET".to_string(),
                "POST".to_string(),
                "DELETE".to_string(),
            ],
            allowed_headers: vec![
                "Content-Type".to_string(),
                "Authorization".to_string(),
            ],
            expose_headers: vec![],
            allow_credentials: false,
            max_age_secs: 3600, // 1 hour
        }
    }

    /// Create a disabled config
    pub fn disabled() -> Self {
        Self {
            enabled: false,
            ..Default::default()
        }
    }

    /// Check if a specific origin is allowed
    pub fn is_origin_allowed(&self, origin: &str) -> bool {
        if !self.enabled {
            return false;
        }

        match &self.allowed_origins {
            None => true, // Allow all
            Some(origins) => origins.iter().any(|o| o == origin || o == "*"),
        }
    }

    /// Check if a method is allowed
    pub fn is_method_allowed(&self, method: &str) -> bool {
        if !self.enabled {
            return false;
        }

        self.allowed_methods
            .iter()
            .any(|m| m.eq_ignore_ascii_case(method))
    }

    /// Check if a header is allowed
    pub fn is_header_allowed(&self, header: &str) -> bool {
        if !self.enabled {
            return false;
        }

        self.allowed_headers.iter().any(|h| {
            h == "*" || h.eq_ignore_ascii_case(header)
        })
    }

    /// Convert to tower-http CorsLayer
    pub fn into_layer(self) -> CorsLayer {
        if !self.enabled {
            // Return a minimal layer that doesn't add any headers
            return CorsLayer::new();
        }

        let mut layer = CorsLayer::new();

        // Set allowed origins
        if self.allowed_origins.is_none() {
            layer = layer.allow_origin(Any);
        } else if let Some(origins) = &self.allowed_origins {
            let origins: Vec<_> = origins
                .iter()
                .filter_map(|o| o.parse().ok())
                .collect();
            if !origins.is_empty() {
                layer = layer.allow_origin(origins);
            }
        }

        // Set allowed methods
        let methods: Vec<_> = self
            .allowed_methods
            .iter()
            .filter_map(|m| m.parse().ok())
            .collect();
        if !methods.is_empty() {
            layer = layer.allow_methods(methods);
        }

        // Set allowed headers
        if self.allowed_headers.iter().any(|h| h == "*") {
            layer = layer.allow_headers(Any);
        } else {
            let headers: Vec<_> = self
                .allowed_headers
                .iter()
                .filter_map(|h| h.parse().ok())
                .collect();
            if !headers.is_empty() {
                layer = layer.allow_headers(headers);
            }
        }

        // Set exposed headers
        if !self.expose_headers.is_empty() && !self.expose_headers.iter().any(|h| h == "*") {
            let headers: Vec<_> = self
                .expose_headers
                .iter()
                .filter_map(|h| h.parse().ok())
                .collect();
            if !headers.is_empty() {
                layer = layer.expose_headers(headers);
            }
        }

        // Set credentials
        if self.allow_credentials {
            layer = layer.allow_credentials(true);
        }

        // Set max age
        layer = layer.max_age(Duration::from_secs(self.max_age_secs));

        layer
    }

    /// Add an allowed origin
    pub fn with_origin(mut self, origin: impl Into<String>) -> Self {
        let origins = self.allowed_origins.get_or_insert_with(Vec::new);
        origins.push(origin.into());
        self
    }

    /// Add an allowed method
    pub fn with_method(mut self, method: impl Into<String>) -> Self {
        self.allowed_methods.push(method.into());
        self
    }

    /// Add an allowed header
    pub fn with_header(mut self, header: impl Into<String>) -> Self {
        self.allowed_headers.push(header.into());
        self
    }

    /// Set credentials allowed
    pub fn with_credentials(mut self, allow: bool) -> Self {
        self.allow_credentials = allow;
        self
    }

    /// Set max age
    pub fn with_max_age(mut self, secs: u64) -> Self {
        self.max_age_secs = secs;
        self
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    // CORS-001: CorsConfig デフォルト値
    #[test]
    fn test_cors_config_default() {
        let config = CorsConfig::default();
        assert!(config.enabled);
        assert!(config.allowed_origins.is_none());
        assert!(config.allowed_methods.contains(&"GET".to_string()));
        assert!(config.allowed_methods.contains(&"POST".to_string()));
        assert!(config.allowed_headers.contains(&"Content-Type".to_string()));
        assert!(!config.allow_credentials);
        assert_eq!(config.max_age_secs, 86400);
    }

    // CORS-002: CorsConfig permissive
    #[test]
    fn test_cors_config_permissive() {
        let config = CorsConfig::permissive();
        assert!(config.enabled);
        assert!(config.allowed_origins.is_none());
        assert!(config.allowed_headers.contains(&"*".to_string()));
        assert!(config.allow_credentials);
    }

    // CORS-003: CorsConfig strict
    #[test]
    fn test_cors_config_strict() {
        let origins = vec!["https://example.com".to_string()];
        let config = CorsConfig::strict(origins);
        assert!(config.enabled);
        assert!(config.allowed_origins.is_some());
        assert_eq!(
            config.allowed_origins.as_ref().unwrap()[0],
            "https://example.com"
        );
        assert!(!config.allow_credentials);
    }

    // CORS-004: CorsConfig disabled
    #[test]
    fn test_cors_config_disabled() {
        let config = CorsConfig::disabled();
        assert!(!config.enabled);
    }

    // CORS-005: CorsLayer 変換
    #[test]
    fn test_cors_into_layer() {
        let config = CorsConfig::default();
        let _layer = config.into_layer();
        // Layer creation should succeed
    }

    #[test]
    fn test_cors_into_layer_disabled() {
        let config = CorsConfig::disabled();
        let _layer = config.into_layer();
        // Layer creation should succeed even when disabled
    }

    // CORS-006: 許可オリジン判定
    #[test]
    fn test_cors_is_origin_allowed() {
        // All origins allowed
        let config = CorsConfig::default();
        assert!(config.is_origin_allowed("https://example.com"));
        assert!(config.is_origin_allowed("http://localhost:3000"));

        // Specific origins
        let config = CorsConfig::strict(vec![
            "https://example.com".to_string(),
            "https://app.example.com".to_string(),
        ]);
        assert!(config.is_origin_allowed("https://example.com"));
        assert!(config.is_origin_allowed("https://app.example.com"));
        assert!(!config.is_origin_allowed("https://other.com"));

        // Disabled
        let config = CorsConfig::disabled();
        assert!(!config.is_origin_allowed("https://example.com"));
    }

    // CORS-007: メソッド許可判定
    #[test]
    fn test_cors_is_method_allowed() {
        let config = CorsConfig::default();
        assert!(config.is_method_allowed("GET"));
        assert!(config.is_method_allowed("get"));
        assert!(config.is_method_allowed("POST"));
        assert!(!config.is_method_allowed("PATCH"));

        let config = CorsConfig::permissive();
        assert!(config.is_method_allowed("PATCH"));

        let config = CorsConfig::disabled();
        assert!(!config.is_method_allowed("GET"));
    }

    // CORS-008: ヘッダー許可判定
    #[test]
    fn test_cors_is_header_allowed() {
        let config = CorsConfig::default();
        assert!(config.is_header_allowed("Content-Type"));
        assert!(config.is_header_allowed("content-type"));
        assert!(config.is_header_allowed("Authorization"));
        assert!(config.is_header_allowed("X-API-Key"));
        assert!(!config.is_header_allowed("X-Custom-Header"));

        let config = CorsConfig::permissive();
        assert!(config.is_header_allowed("X-Custom-Header")); // * allows all

        let config = CorsConfig::disabled();
        assert!(!config.is_header_allowed("Content-Type"));
    }

    // CORS-009: クレデンシャル設定
    #[test]
    fn test_cors_credentials() {
        let config = CorsConfig::default();
        assert!(!config.allow_credentials);

        let config = CorsConfig::permissive();
        assert!(config.allow_credentials);

        let config = CorsConfig::default().with_credentials(true);
        assert!(config.allow_credentials);
    }

    // CORS-010: max_age 設定
    #[test]
    fn test_cors_max_age() {
        let config = CorsConfig::default();
        assert_eq!(config.max_age_secs, 86400);

        let config = CorsConfig::strict(vec![]);
        assert_eq!(config.max_age_secs, 3600);

        let config = CorsConfig::default().with_max_age(7200);
        assert_eq!(config.max_age_secs, 7200);
    }

    #[test]
    fn test_cors_builder_methods() {
        let config = CorsConfig::default()
            .with_origin("https://custom.com")
            .with_method("PATCH")
            .with_header("X-Custom")
            .with_credentials(true)
            .with_max_age(1800);

        assert!(config.allowed_origins.is_some());
        assert!(config
            .allowed_origins
            .as_ref()
            .unwrap()
            .contains(&"https://custom.com".to_string()));
        assert!(config.allowed_methods.contains(&"PATCH".to_string()));
        assert!(config.allowed_headers.contains(&"X-Custom".to_string()));
        assert!(config.allow_credentials);
        assert_eq!(config.max_age_secs, 1800);
    }

    #[test]
    fn test_cors_into_layer_with_origins() {
        let config = CorsConfig::strict(vec!["https://example.com".to_string()]);
        let _layer = config.into_layer();
    }

    #[test]
    fn test_cors_into_layer_permissive() {
        let config = CorsConfig::permissive();
        let _layer = config.into_layer();
    }
}
