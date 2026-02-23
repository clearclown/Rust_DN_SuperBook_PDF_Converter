//! Processing cache module for smart re-processing skip
//!
//! This module implements hash-based caching to skip re-processing
//! of unchanged PDFs with the same options.

use serde::{Deserialize, Serialize};
use sha2::{Digest, Sha256};
use std::fs;
use std::io;
use std::path::{Path, PathBuf};
use std::time::SystemTime;

/// Cache file extension
pub const CACHE_EXTENSION: &str = ".superbook-cache";

/// Current cache version
pub const CACHE_VERSION: u32 = 1;

/// Digest that uniquely identifies a processing run
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct CacheDigest {
    /// Source file last modified time (Unix timestamp)
    pub source_modified: u64,
    /// Source file size in bytes
    pub source_size: u64,
    /// Hash of processing options
    pub options_hash: String,
}

impl CacheDigest {
    /// Create a new digest from source path and options
    ///
    /// # Arguments
    /// * `source_path` - Path to the source PDF file
    /// * `options_json` - JSON string of processing options
    ///
    /// # Returns
    /// A new CacheDigest or an error if the file cannot be accessed
    pub fn new<P: AsRef<Path>>(source_path: P, options_json: &str) -> io::Result<Self> {
        let metadata = fs::metadata(source_path.as_ref())?;
        let modified = metadata
            .modified()
            .unwrap_or(SystemTime::UNIX_EPOCH)
            .duration_since(SystemTime::UNIX_EPOCH)
            .unwrap_or_default()
            .as_secs();
        let size = metadata.len();

        let mut hasher = Sha256::new();
        hasher.update(options_json.as_bytes());
        let hash = format!("sha256:{:x}", hasher.finalize());

        Ok(Self {
            source_modified: modified,
            source_size: size,
            options_hash: hash,
        })
    }

    /// Create a digest with explicit values (for testing)
    pub fn with_values(source_modified: u64, source_size: u64, options_hash: &str) -> Self {
        Self {
            source_modified,
            source_size,
            options_hash: options_hash.to_string(),
        }
    }
}

/// Processing result metadata
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ProcessingResult {
    /// Number of pages processed
    pub page_count: usize,
    /// Detected page number shift (if any)
    pub page_number_shift: Option<i32>,
    /// Whether vertical text was detected
    pub is_vertical: bool,
    /// Processing time in seconds
    pub elapsed_seconds: f64,
    /// Output file size in bytes
    pub output_size: u64,
}

impl Default for ProcessingResult {
    fn default() -> Self {
        Self {
            page_count: 0,
            page_number_shift: None,
            is_vertical: false,
            elapsed_seconds: 0.0,
            output_size: 0,
        }
    }
}

impl ProcessingResult {
    /// Create a new ProcessingResult
    pub fn new(
        page_count: usize,
        page_number_shift: Option<i32>,
        is_vertical: bool,
        elapsed_seconds: f64,
        output_size: u64,
    ) -> Self {
        Self {
            page_count,
            page_number_shift,
            is_vertical,
            elapsed_seconds,
            output_size,
        }
    }
}

/// Processing cache entry
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ProcessingCache {
    /// Cache version for compatibility check
    pub version: u32,
    /// Processing timestamp (Unix timestamp)
    pub processed_at: u64,
    /// Digest that identifies this processing run
    pub digest: CacheDigest,
    /// Processing result metadata
    pub result: ProcessingResult,
}

impl ProcessingCache {
    /// Create a new ProcessingCache
    pub fn new(digest: CacheDigest, result: ProcessingResult) -> Self {
        let processed_at = SystemTime::now()
            .duration_since(SystemTime::UNIX_EPOCH)
            .unwrap_or_default()
            .as_secs();

        Self {
            version: CACHE_VERSION,
            processed_at,
            digest,
            result,
        }
    }

    /// Get the cache file path for an output file
    pub fn cache_path<P: AsRef<Path>>(output_path: P) -> PathBuf {
        let mut path = output_path.as_ref().as_os_str().to_owned();
        path.push(CACHE_EXTENSION);
        PathBuf::from(path)
    }

    /// Load cache from file
    ///
    /// # Arguments
    /// * `output_path` - Path to the output PDF file (cache file is derived from this)
    ///
    /// # Returns
    /// The loaded cache or an error
    pub fn load<P: AsRef<Path>>(output_path: P) -> io::Result<Self> {
        let cache_path = Self::cache_path(output_path);
        let content = fs::read_to_string(&cache_path)?;
        serde_json::from_str(&content).map_err(|e| io::Error::new(io::ErrorKind::InvalidData, e))
    }

    /// Save cache to file
    ///
    /// # Arguments
    /// * `output_path` - Path to the output PDF file (cache file is derived from this)
    pub fn save<P: AsRef<Path>>(&self, output_path: P) -> io::Result<()> {
        let cache_path = Self::cache_path(output_path);
        let content = serde_json::to_string_pretty(self)
            .map_err(|e| io::Error::new(io::ErrorKind::InvalidData, e))?;
        fs::write(&cache_path, content)
    }

    /// Check if the cache is valid for a given digest
    ///
    /// # Arguments
    /// * `digest` - The digest to compare against
    ///
    /// # Returns
    /// `true` if the cache is valid (version matches and digest matches)
    pub fn is_valid(&self, digest: &CacheDigest) -> bool {
        self.version == CACHE_VERSION && self.digest == *digest
    }

    /// Delete the cache file
    pub fn delete<P: AsRef<Path>>(output_path: P) -> io::Result<()> {
        let cache_path = Self::cache_path(output_path);
        if cache_path.exists() {
            fs::remove_file(cache_path)?;
        }
        Ok(())
    }
}

/// Check if processing should be skipped based on cache
///
/// # Arguments
/// * `source_path` - Path to the source PDF
/// * `output_path` - Path to the output PDF
/// * `options_json` - JSON string of processing options
/// * `force` - If true, always return false (force re-processing)
///
/// # Returns
/// `Some(cache)` if processing should be skipped, `None` otherwise
pub fn should_skip_processing<P1: AsRef<Path>, P2: AsRef<Path>>(
    source_path: P1,
    output_path: P2,
    options_json: &str,
    force: bool,
) -> Option<ProcessingCache> {
    if force {
        return None;
    }

    // Check if output exists
    if !output_path.as_ref().exists() {
        return None;
    }

    // Try to create digest
    let digest = CacheDigest::new(&source_path, options_json).ok()?;

    // Try to load and validate cache
    let cache = ProcessingCache::load(&output_path).ok()?;
    if cache.is_valid(&digest) {
        Some(cache)
    } else {
        None
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::io::Write;
    use tempfile::NamedTempFile;

    // ============ CacheDigest Tests ============

    #[test]
    fn test_cache_digest_new() {
        // TC: CACHE-001
        let mut temp = NamedTempFile::new().unwrap();
        temp.write_all(b"test content").unwrap();

        let digest = CacheDigest::new(temp.path(), r#"{"dpi": 300}"#).unwrap();

        assert!(digest.source_modified > 0);
        assert_eq!(digest.source_size, 12); // "test content" = 12 bytes
        assert!(digest.options_hash.starts_with("sha256:"));
    }

    #[test]
    fn test_cache_digest_with_values() {
        let digest = CacheDigest::with_values(1234567890, 999, "sha256:abc123");

        assert_eq!(digest.source_modified, 1234567890);
        assert_eq!(digest.source_size, 999);
        assert_eq!(digest.options_hash, "sha256:abc123");
    }

    #[test]
    fn test_cache_digest_same_options_same_hash() {
        // TC: CACHE-002
        let mut temp = NamedTempFile::new().unwrap();
        temp.write_all(b"test").unwrap();

        let digest1 = CacheDigest::new(temp.path(), r#"{"dpi": 300}"#).unwrap();
        let digest2 = CacheDigest::new(temp.path(), r#"{"dpi": 300}"#).unwrap();

        assert_eq!(digest1, digest2);
    }

    #[test]
    fn test_cache_digest_different_file() {
        // TC: CACHE-003
        let mut temp1 = NamedTempFile::new().unwrap();
        temp1.write_all(b"content1").unwrap();

        let mut temp2 = NamedTempFile::new().unwrap();
        temp2.write_all(b"different content").unwrap();

        let digest1 = CacheDigest::new(temp1.path(), r#"{"dpi": 300}"#).unwrap();
        let digest2 = CacheDigest::new(temp2.path(), r#"{"dpi": 300}"#).unwrap();

        // Different sizes
        assert_ne!(digest1.source_size, digest2.source_size);
    }

    #[test]
    fn test_cache_digest_different_options() {
        // TC: CACHE-004
        let mut temp = NamedTempFile::new().unwrap();
        temp.write_all(b"test").unwrap();

        let digest1 = CacheDigest::new(temp.path(), r#"{"dpi": 300}"#).unwrap();
        let digest2 = CacheDigest::new(temp.path(), r#"{"dpi": 600}"#).unwrap();

        assert_ne!(digest1.options_hash, digest2.options_hash);
    }

    #[test]
    fn test_cache_digest_nonexistent_file() {
        let result = CacheDigest::new("/nonexistent/file.pdf", "{}");
        assert!(result.is_err());
    }

    // ============ ProcessingResult Tests ============

    #[test]
    fn test_processing_result_default() {
        let result = ProcessingResult::default();

        assert_eq!(result.page_count, 0);
        assert_eq!(result.page_number_shift, None);
        assert!(!result.is_vertical);
        assert_eq!(result.elapsed_seconds, 0.0);
        assert_eq!(result.output_size, 0);
    }

    #[test]
    fn test_processing_result_new() {
        let result = ProcessingResult::new(100, Some(2), true, 45.5, 12345678);

        assert_eq!(result.page_count, 100);
        assert_eq!(result.page_number_shift, Some(2));
        assert!(result.is_vertical);
        assert_eq!(result.elapsed_seconds, 45.5);
        assert_eq!(result.output_size, 12345678);
    }

    // ============ ProcessingCache Tests ============

    #[test]
    fn test_processing_cache_new() {
        let digest = CacheDigest::with_values(1234567890, 999, "sha256:abc");
        let result = ProcessingResult::default();
        let cache = ProcessingCache::new(digest.clone(), result);

        assert_eq!(cache.version, CACHE_VERSION);
        assert!(cache.processed_at > 0);
        assert_eq!(cache.digest, digest);
    }

    #[test]
    fn test_processing_cache_path() {
        let path = ProcessingCache::cache_path("/output/file.pdf");
        assert_eq!(path.to_string_lossy(), "/output/file.pdf.superbook-cache");
    }

    #[test]
    fn test_processing_cache_save_load() {
        // TC: CACHE-005, CACHE-006
        let temp_dir = tempfile::tempdir().unwrap();
        let output_path = temp_dir.path().join("output.pdf");

        // Create and save cache
        let digest = CacheDigest::with_values(1234567890, 999, "sha256:abc");
        let result = ProcessingResult::new(50, Some(3), true, 10.5, 5000000);
        let cache = ProcessingCache::new(digest.clone(), result);
        cache.save(&output_path).unwrap();

        // Load and verify
        let loaded = ProcessingCache::load(&output_path).unwrap();
        assert_eq!(loaded.version, cache.version);
        assert_eq!(loaded.digest, cache.digest);
        assert_eq!(loaded.result.page_count, 50);
        assert_eq!(loaded.result.page_number_shift, Some(3));
        assert!(loaded.result.is_vertical);
    }

    #[test]
    fn test_processing_cache_load_nonexistent() {
        // TC: CACHE-007
        let result = ProcessingCache::load("/nonexistent/file.pdf");
        assert!(result.is_err());
    }

    #[test]
    fn test_processing_cache_is_valid_same() {
        let digest = CacheDigest::with_values(1234567890, 999, "sha256:abc");
        let cache = ProcessingCache::new(digest.clone(), ProcessingResult::default());

        assert!(cache.is_valid(&digest));
    }

    #[test]
    fn test_processing_cache_is_valid_different_digest() {
        let digest1 = CacheDigest::with_values(1234567890, 999, "sha256:abc");
        let digest2 = CacheDigest::with_values(1234567890, 1000, "sha256:abc");
        let cache = ProcessingCache::new(digest1, ProcessingResult::default());

        assert!(!cache.is_valid(&digest2));
    }

    #[test]
    fn test_processing_cache_version_mismatch() {
        // TC: CACHE-008
        let temp_dir = tempfile::tempdir().unwrap();
        let output_path = temp_dir.path().join("output.pdf");

        // Create cache with future version
        let cache_content = r#"{
            "version": 999,
            "processed_at": 1234567890,
            "digest": {
                "source_modified": 1234567890,
                "source_size": 999,
                "options_hash": "sha256:abc"
            },
            "result": {
                "page_count": 10,
                "page_number_shift": null,
                "is_vertical": false,
                "elapsed_seconds": 5.0,
                "output_size": 1000
            }
        }"#;

        let cache_path = ProcessingCache::cache_path(&output_path);
        fs::write(&cache_path, cache_content).unwrap();

        let loaded = ProcessingCache::load(&output_path).unwrap();
        let digest = CacheDigest::with_values(1234567890, 999, "sha256:abc");

        // Version mismatch should make it invalid
        assert!(!loaded.is_valid(&digest));
    }

    #[test]
    fn test_processing_cache_corrupted() {
        // TC: CACHE-009
        let temp_dir = tempfile::tempdir().unwrap();
        let output_path = temp_dir.path().join("output.pdf");
        let cache_path = ProcessingCache::cache_path(&output_path);

        fs::write(&cache_path, "not valid json").unwrap();

        let result = ProcessingCache::load(&output_path);
        assert!(result.is_err());
    }

    #[test]
    fn test_processing_cache_delete() {
        let temp_dir = tempfile::tempdir().unwrap();
        let output_path = temp_dir.path().join("output.pdf");

        // Create cache
        let digest = CacheDigest::with_values(1234567890, 999, "sha256:abc");
        let cache = ProcessingCache::new(digest, ProcessingResult::default());
        cache.save(&output_path).unwrap();

        // Verify exists
        let cache_path = ProcessingCache::cache_path(&output_path);
        assert!(cache_path.exists());

        // Delete
        ProcessingCache::delete(&output_path).unwrap();
        assert!(!cache_path.exists());
    }

    #[test]
    fn test_processing_cache_delete_nonexistent() {
        // Should not error when deleting nonexistent cache
        let result = ProcessingCache::delete("/nonexistent/file.pdf");
        assert!(result.is_ok());
    }

    // ============ should_skip_processing Tests ============

    #[test]
    fn test_should_skip_with_force() {
        // TC: CACHE-010
        let temp_dir = tempfile::tempdir().unwrap();
        let source_path = temp_dir.path().join("source.pdf");
        let output_path = temp_dir.path().join("output.pdf");

        fs::write(&source_path, "source").unwrap();
        fs::write(&output_path, "output").unwrap();

        // Create valid cache
        let digest = CacheDigest::new(&source_path, "{}").unwrap();
        let cache = ProcessingCache::new(digest, ProcessingResult::default());
        cache.save(&output_path).unwrap();

        // With force=true, should not skip
        let result = should_skip_processing(&source_path, &output_path, "{}", true);
        assert!(result.is_none());
    }

    #[test]
    fn test_should_skip_no_output() {
        let temp_dir = tempfile::tempdir().unwrap();
        let source_path = temp_dir.path().join("source.pdf");
        let output_path = temp_dir.path().join("output.pdf");

        fs::write(&source_path, "source").unwrap();
        // output does not exist

        let result = should_skip_processing(&source_path, &output_path, "{}", false);
        assert!(result.is_none());
    }

    #[test]
    fn test_should_skip_no_cache() {
        let temp_dir = tempfile::tempdir().unwrap();
        let source_path = temp_dir.path().join("source.pdf");
        let output_path = temp_dir.path().join("output.pdf");

        fs::write(&source_path, "source").unwrap();
        fs::write(&output_path, "output").unwrap();
        // No cache file

        let result = should_skip_processing(&source_path, &output_path, "{}", false);
        assert!(result.is_none());
    }

    #[test]
    fn test_should_skip_valid_cache() {
        let temp_dir = tempfile::tempdir().unwrap();
        let source_path = temp_dir.path().join("source.pdf");
        let output_path = temp_dir.path().join("output.pdf");

        fs::write(&source_path, "source").unwrap();
        fs::write(&output_path, "output").unwrap();

        // Create valid cache
        let options = r#"{"dpi": 300}"#;
        let digest = CacheDigest::new(&source_path, options).unwrap();
        let result = ProcessingResult::new(10, None, false, 5.0, 1000);
        let cache = ProcessingCache::new(digest, result);
        cache.save(&output_path).unwrap();

        // Should skip with same options
        let skip_result = should_skip_processing(&source_path, &output_path, options, false);
        assert!(skip_result.is_some());
        assert_eq!(skip_result.unwrap().result.page_count, 10);
    }

    #[test]
    fn test_should_skip_options_changed() {
        let temp_dir = tempfile::tempdir().unwrap();
        let source_path = temp_dir.path().join("source.pdf");
        let output_path = temp_dir.path().join("output.pdf");

        fs::write(&source_path, "source").unwrap();
        fs::write(&output_path, "output").unwrap();

        // Create cache with different options
        let digest = CacheDigest::new(&source_path, r#"{"dpi": 300}"#).unwrap();
        let cache = ProcessingCache::new(digest, ProcessingResult::default());
        cache.save(&output_path).unwrap();

        // Should NOT skip with different options
        let result = should_skip_processing(&source_path, &output_path, r#"{"dpi": 600}"#, false);
        assert!(result.is_none());
    }
}
