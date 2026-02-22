//! Rate limiting for the web server
//!
//! Implements token bucket algorithm for fair API usage.

use dashmap::DashMap;
use serde::Serialize;
use std::net::IpAddr;
use std::sync::atomic::{AtomicU64, Ordering};
use std::time::{Duration, Instant};

/// Rate limit configuration
#[derive(Debug, Clone)]
pub struct RateLimitConfig {
    /// Maximum requests per minute
    pub requests_per_minute: u32,
    /// Maximum burst size (tokens)
    pub burst_size: u32,
    /// Token refill interval in milliseconds
    pub refill_interval_ms: u64,
    /// Whitelisted IPs (bypass rate limiting)
    pub whitelist: Vec<IpAddr>,
    /// Enable rate limiting
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

/// Token bucket for rate limiting
#[derive(Debug)]
pub struct TokenBucket {
    /// Current number of tokens
    tokens: f64,
    /// Maximum tokens
    max_tokens: f64,
    /// Tokens added per second
    refill_rate: f64,
    /// Last update time
    last_update: Instant,
}

impl TokenBucket {
    /// Create a new token bucket
    pub fn new(max_tokens: f64, refill_rate: f64) -> Self {
        Self {
            tokens: max_tokens,
            max_tokens,
            refill_rate,
            last_update: Instant::now(),
        }
    }

    /// Try to consume a token. Returns true if successful.
    pub fn try_consume(&mut self) -> bool {
        self.refill();
        if self.tokens >= 1.0 {
            self.tokens -= 1.0;
            true
        } else {
            false
        }
    }

    /// Get remaining tokens
    pub fn tokens_remaining(&mut self) -> f64 {
        self.refill();
        self.tokens
    }

    /// Get time until next token is available
    pub fn time_until_refill(&self) -> Duration {
        if self.tokens >= 1.0 {
            Duration::ZERO
        } else {
            let needed = 1.0 - self.tokens;
            let seconds = needed / self.refill_rate;
            Duration::from_secs_f64(seconds)
        }
    }

    /// Refill tokens based on elapsed time
    fn refill(&mut self) {
        let now = Instant::now();
        let elapsed = now.duration_since(self.last_update);
        let new_tokens = elapsed.as_secs_f64() * self.refill_rate;
        self.tokens = (self.tokens + new_tokens).min(self.max_tokens);
        self.last_update = now;
    }

    /// Get last update time
    pub fn last_update(&self) -> Instant {
        self.last_update
    }
}

/// Result of rate limit check
#[derive(Debug, Clone)]
pub enum RateLimitResult {
    /// Request allowed
    Allowed {
        /// Remaining requests
        remaining: u32,
        /// Reset timestamp (Unix)
        reset_at: u64,
    },
    /// Request rate limited
    Limited {
        /// Seconds until retry
        retry_after: u64,
    },
}

/// Rate limiter using token bucket per IP
pub struct RateLimiter {
    /// Per-IP buckets
    buckets: DashMap<IpAddr, TokenBucket>,
    /// Configuration
    config: RateLimitConfig,
    /// Start time for reset calculation (reserved for future use)
    #[allow(dead_code)]
    start_time: Instant,
    /// Request counter (for statistics)
    total_requests: AtomicU64,
    /// Rejected counter
    rejected_requests: AtomicU64,
}

impl RateLimiter {
    /// Create a new rate limiter
    pub fn new(config: RateLimitConfig) -> Self {
        Self {
            buckets: DashMap::new(),
            config,
            start_time: Instant::now(),
            total_requests: AtomicU64::new(0),
            rejected_requests: AtomicU64::new(0),
        }
    }

    /// Check if a request from the given IP is allowed
    pub fn check(&self, ip: IpAddr) -> RateLimitResult {
        self.total_requests.fetch_add(1, Ordering::Relaxed);

        // Check if rate limiting is disabled
        if !self.config.enabled {
            return RateLimitResult::Allowed {
                remaining: u32::MAX,
                reset_at: 0,
            };
        }

        // Check whitelist
        if self.config.whitelist.contains(&ip) {
            return RateLimitResult::Allowed {
                remaining: u32::MAX,
                reset_at: 0,
            };
        }

        // Get or create bucket for this IP
        let mut bucket = self.buckets.entry(ip).or_insert_with(|| {
            let refill_rate = self.config.requests_per_minute as f64 / 60.0;
            TokenBucket::new(self.config.burst_size as f64, refill_rate)
        });

        if bucket.try_consume() {
            let remaining = bucket.tokens_remaining() as u32;
            let reset_at = self.calculate_reset_time();
            RateLimitResult::Allowed {
                remaining,
                reset_at,
            }
        } else {
            self.rejected_requests.fetch_add(1, Ordering::Relaxed);
            let retry_after = bucket.time_until_refill().as_secs().max(1);
            RateLimitResult::Limited { retry_after }
        }
    }

    /// Calculate reset timestamp
    fn calculate_reset_time(&self) -> u64 {
        use std::time::{SystemTime, UNIX_EPOCH};
        let now = SystemTime::now()
            .duration_since(UNIX_EPOCH)
            .unwrap_or_default();
        // Reset at next minute boundary
        let current_secs = now.as_secs();
        (current_secs / 60 + 1) * 60
    }

    /// Clean up expired entries (buckets not used for a while)
    pub fn cleanup_expired(&self, max_age: Duration) {
        let now = Instant::now();
        self.buckets.retain(|_: &IpAddr, bucket: &mut TokenBucket| {
            now.duration_since(bucket.last_update()) < max_age
        });
    }

    /// Get the number of tracked IPs
    pub fn tracked_ips(&self) -> usize {
        self.buckets.len()
    }

    /// Get total request count
    pub fn total_requests(&self) -> u64 {
        self.total_requests.load(Ordering::Relaxed)
    }

    /// Get rejected request count
    pub fn rejected_requests(&self) -> u64 {
        self.rejected_requests.load(Ordering::Relaxed)
    }

    /// Check if rate limiting is enabled
    pub fn is_enabled(&self) -> bool {
        self.config.enabled
    }

    /// Get the configured requests per minute
    pub fn requests_per_minute(&self) -> u32 {
        self.config.requests_per_minute
    }

    /// Get the configured burst size
    pub fn burst_size(&self) -> u32 {
        self.config.burst_size
    }
}

/// Rate limit status response
#[derive(Debug, Clone, Serialize)]
pub struct RateLimitStatus {
    /// Whether rate limiting is enabled
    pub enabled: bool,
    /// Requests allowed per minute
    pub requests_per_minute: u32,
    /// Burst size
    pub burst_size: u32,
    /// Your remaining requests
    pub your_remaining: u32,
    /// Reset timestamp
    pub reset_at: u64,
}

/// Rate limit error response
#[derive(Debug, Clone, Serialize)]
pub struct RateLimitError {
    /// Error code
    pub error: String,
    /// Human readable message
    pub message: String,
    /// Seconds to wait before retry
    pub retry_after: u64,
}

impl RateLimitError {
    /// Create a new rate limit error
    pub fn new(retry_after: u64) -> Self {
        Self {
            error: "rate_limit_exceeded".to_string(),
            message: format!(
                "Too many requests. Please retry after {} seconds.",
                retry_after
            ),
            retry_after,
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::net::Ipv4Addr;
    use std::thread;

    // RATE-001: トークンバケット作成
    #[test]
    fn test_token_bucket_new() {
        let bucket = TokenBucket::new(10.0, 1.0);
        assert_eq!(bucket.max_tokens, 10.0);
        assert_eq!(bucket.tokens, 10.0);
        assert_eq!(bucket.refill_rate, 1.0);
    }

    // RATE-002: トークン消費成功
    #[test]
    fn test_token_bucket_consume_success() {
        let mut bucket = TokenBucket::new(10.0, 1.0);
        assert!(bucket.try_consume());
        assert!(bucket.tokens_remaining() < 10.0);
    }

    // RATE-003: トークン枯渇で拒否
    #[test]
    fn test_token_bucket_exhausted() {
        let mut bucket = TokenBucket::new(2.0, 0.1);
        assert!(bucket.try_consume()); // 1st
        assert!(bucket.try_consume()); // 2nd
        assert!(!bucket.try_consume()); // exhausted
    }

    // RATE-004: トークン自動補充
    #[test]
    fn test_token_bucket_refill() {
        let mut bucket = TokenBucket::new(10.0, 100.0); // 100 tokens/sec

        // Consume all tokens
        for _ in 0..10 {
            bucket.try_consume();
        }
        assert!(bucket.tokens_remaining() < 1.0);

        // Wait for refill
        thread::sleep(Duration::from_millis(50));

        // Should have some tokens now
        assert!(bucket.tokens_remaining() > 0.0);
    }

    // RATE-005: レートリミッター作成
    #[test]
    fn test_rate_limiter_new() {
        let config = RateLimitConfig::default();
        let limiter = RateLimiter::new(config);
        assert!(limiter.is_enabled());
        assert_eq!(limiter.requests_per_minute(), 60);
        assert_eq!(limiter.burst_size(), 10);
    }

    // RATE-006: IP別トラッキング
    #[test]
    fn test_rate_limiter_per_ip() {
        let config = RateLimitConfig {
            burst_size: 2,
            ..Default::default()
        };
        let limiter = RateLimiter::new(config);

        let ip1: IpAddr = IpAddr::V4(Ipv4Addr::new(192, 168, 1, 1));
        let ip2: IpAddr = IpAddr::V4(Ipv4Addr::new(192, 168, 1, 2));

        // Each IP has its own bucket
        assert!(matches!(
            limiter.check(ip1),
            RateLimitResult::Allowed { .. }
        ));
        assert!(matches!(
            limiter.check(ip1),
            RateLimitResult::Allowed { .. }
        ));
        assert!(matches!(
            limiter.check(ip1),
            RateLimitResult::Limited { .. }
        ));

        // IP2 should still be allowed
        assert!(matches!(
            limiter.check(ip2),
            RateLimitResult::Allowed { .. }
        ));

        assert_eq!(limiter.tracked_ips(), 2);
    }

    // RATE-007: ホワイトリストIP許可
    #[test]
    fn test_rate_limiter_whitelist() {
        let whitelisted_ip: IpAddr = IpAddr::V4(Ipv4Addr::new(10, 0, 0, 1));
        let config = RateLimitConfig {
            burst_size: 1,
            whitelist: vec![whitelisted_ip],
            ..Default::default()
        };
        let limiter = RateLimiter::new(config);

        // Whitelisted IP is always allowed
        for _ in 0..100 {
            assert!(matches!(
                limiter.check(whitelisted_ip),
                RateLimitResult::Allowed { .. }
            ));
        }
    }

    // RATE-008: 残り回数追跡
    #[test]
    fn test_rate_limit_remaining() {
        let config = RateLimitConfig {
            burst_size: 5,
            ..Default::default()
        };
        let limiter = RateLimiter::new(config);
        let ip: IpAddr = IpAddr::V4(Ipv4Addr::new(192, 168, 1, 1));

        if let RateLimitResult::Allowed { remaining, .. } = limiter.check(ip) {
            assert!(remaining <= 4); // After consuming one
        } else {
            panic!("Should be allowed");
        }
    }

    // RATE-009: 429レスポンス
    #[test]
    fn test_rate_limit_exceeded() {
        let config = RateLimitConfig {
            burst_size: 1,
            requests_per_minute: 1,
            ..Default::default()
        };
        let limiter = RateLimiter::new(config);
        let ip: IpAddr = IpAddr::V4(Ipv4Addr::new(192, 168, 1, 1));

        // First request allowed
        assert!(matches!(limiter.check(ip), RateLimitResult::Allowed { .. }));

        // Second request limited
        assert!(matches!(limiter.check(ip), RateLimitResult::Limited { .. }));
    }

    // RATE-010: Retry-Afterヘッダー
    #[test]
    fn test_rate_limit_retry_after() {
        let config = RateLimitConfig {
            burst_size: 1,
            requests_per_minute: 60, // 1 per second
            ..Default::default()
        };
        let limiter = RateLimiter::new(config);
        let ip: IpAddr = IpAddr::V4(Ipv4Addr::new(192, 168, 1, 1));

        limiter.check(ip); // Consume

        if let RateLimitResult::Limited { retry_after } = limiter.check(ip) {
            assert!(retry_after >= 1); // At least 1 second
        } else {
            panic!("Should be limited");
        }
    }

    // RATE-011: 並行アクセス安全性
    #[test]
    fn test_rate_limiter_concurrent() {
        use std::sync::Arc;

        let config = RateLimitConfig {
            burst_size: 100,
            ..Default::default()
        };
        let limiter = Arc::new(RateLimiter::new(config));
        let ip: IpAddr = IpAddr::V4(Ipv4Addr::new(192, 168, 1, 1));

        let handles: Vec<_> = (0..10)
            .map(|_| {
                let limiter = Arc::clone(&limiter);
                thread::spawn(move || {
                    for _ in 0..10 {
                        let _ = limiter.check(ip);
                    }
                })
            })
            .collect();

        for handle in handles {
            handle.join().unwrap();
        }

        assert_eq!(limiter.total_requests(), 100);
    }

    // RATE-012: 期限切れエントリクリーンアップ
    #[test]
    fn test_rate_limiter_cleanup() {
        let config = RateLimitConfig::default();
        let limiter = RateLimiter::new(config);

        let ip1: IpAddr = IpAddr::V4(Ipv4Addr::new(192, 168, 1, 1));
        let ip2: IpAddr = IpAddr::V4(Ipv4Addr::new(192, 168, 1, 2));

        limiter.check(ip1);
        limiter.check(ip2);
        assert_eq!(limiter.tracked_ips(), 2);

        // Cleanup with very short max age
        limiter.cleanup_expired(Duration::from_nanos(1));

        // All entries should be removed (they're older than 1 nanosecond)
        assert_eq!(limiter.tracked_ips(), 0);
    }

    #[test]
    fn test_rate_limit_config_default() {
        let config = RateLimitConfig::default();
        assert_eq!(config.requests_per_minute, 60);
        assert_eq!(config.burst_size, 10);
        assert_eq!(config.refill_interval_ms, 1000);
        assert!(config.whitelist.is_empty());
        assert!(config.enabled);
    }

    #[test]
    fn test_rate_limit_disabled() {
        let config = RateLimitConfig {
            enabled: false,
            ..Default::default()
        };
        let limiter = RateLimiter::new(config);
        let ip: IpAddr = IpAddr::V4(Ipv4Addr::new(192, 168, 1, 1));

        // All requests allowed when disabled
        for _ in 0..1000 {
            assert!(matches!(
                limiter.check(ip),
                RateLimitResult::Allowed {
                    remaining: u32::MAX,
                    ..
                }
            ));
        }
    }

    #[test]
    fn test_rate_limit_error_new() {
        let error = RateLimitError::new(60);
        assert_eq!(error.error, "rate_limit_exceeded");
        assert_eq!(error.retry_after, 60);
        assert!(error.message.contains("60"));
    }

    #[test]
    fn test_rate_limit_statistics() {
        let config = RateLimitConfig {
            burst_size: 2,
            ..Default::default()
        };
        let limiter = RateLimiter::new(config);
        let ip: IpAddr = IpAddr::V4(Ipv4Addr::new(192, 168, 1, 1));

        limiter.check(ip); // 1st - allowed
        limiter.check(ip); // 2nd - allowed
        limiter.check(ip); // 3rd - rejected

        assert_eq!(limiter.total_requests(), 3);
        assert_eq!(limiter.rejected_requests(), 1);
    }
}
