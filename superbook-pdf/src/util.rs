//! Common utilities for superbook-pdf
//!
//! Provides shared functionality across modules to reduce code duplication.
//!
//! # Examples
//!
//! ## Unit Conversions
//!
//! ```rust
//! use superbook_pdf::{mm_to_pixels, pixels_to_mm, mm_to_points, points_to_mm};
//!
//! // Convert 25.4mm (1 inch) to pixels at 300 DPI
//! let pixels = mm_to_pixels(25.4, 300);
//! assert_eq!(pixels, 300); // 1 inch = 300 pixels at 300 DPI
//!
//! // Convert back
//! let mm = pixels_to_mm(300, 300);
//! assert!((mm - 25.4).abs() < 0.1);
//!
//! // PDF points (72 points per inch)
//! let points = mm_to_points(25.4);
//! assert!((points - 72.0).abs() < 0.1);
//! ```
//!
//! ## Utility Functions
//!
//! ```rust
//! use superbook_pdf::{clamp, percentage, format_file_size, format_duration};
//! use std::time::Duration;
//!
//! // Clamp values to range
//! assert_eq!(clamp(150, 0, 100), 100);
//! assert_eq!(clamp(-50, 0, 100), 0);
//! assert_eq!(clamp(50, 0, 100), 50);
//!
//! // Calculate percentage
//! assert_eq!(percentage(25, 100), 25.0);
//!
//! // Format file sizes
//! let size = format_file_size(1024 * 1024);
//! assert!(size.contains("1") || size.contains("M"));
//!
//! // Format durations
//! let dur = format_duration(Duration::from_secs(90));
//! assert!(dur.contains("1") || dur.contains("min") || dur.contains(":"));
//! ```

use image::DynamicImage;
use std::path::{Path, PathBuf};

/// Millimeters per inch (exactly 25.4)
const MM_PER_INCH: f32 = 25.4;
/// Points per inch (PDF standard: 72 points = 1 inch)
const POINTS_PER_INCH: f64 = 72.0;

/// Load an image from path with consistent error handling
pub fn load_image<P: AsRef<Path>>(path: P) -> Result<DynamicImage, String> {
    let path = path.as_ref();
    if !path.exists() {
        return Err(format!("Image not found: {}", path.display()));
    }
    image::open(path).map_err(|e| format!("Failed to load image: {}", e))
}

/// Check if a path exists and is a file
pub fn ensure_file_exists<P: AsRef<Path>>(path: P) -> Result<(), String> {
    let path = path.as_ref();
    if !path.exists() {
        return Err(format!("File not found: {}", path.display()));
    }
    if !path.is_file() {
        return Err(format!("Path is not a file: {}", path.display()));
    }
    Ok(())
}

/// Check if a directory exists and is writable
pub fn ensure_dir_writable<P: AsRef<Path>>(path: P) -> Result<(), String> {
    let path = path.as_ref();
    if !path.exists() {
        std::fs::create_dir_all(path).map_err(|e| format!("Failed to create directory: {}", e))?;
    }

    // Test writability
    let test_file = path.join(".write_test");
    std::fs::write(&test_file, b"test")
        .map_err(|_| format!("Directory not writable: {}", path.display()))?;
    let _ = std::fs::remove_file(test_file);

    Ok(())
}

/// Convert pixels to millimeters at given DPI
#[inline]
pub fn pixels_to_mm(pixels: u32, dpi: u32) -> f32 {
    (pixels as f32 / dpi as f32) * MM_PER_INCH
}

/// Convert millimeters to pixels at given DPI
#[inline]
pub fn mm_to_pixels(mm: f32, dpi: u32) -> u32 {
    (mm * dpi as f32 / MM_PER_INCH) as u32
}

/// Convert points to millimeters
#[inline]
pub fn points_to_mm(points: f64) -> f32 {
    (points / POINTS_PER_INCH * f64::from(MM_PER_INCH)) as f32
}

/// Convert millimeters to points
#[inline]
pub fn mm_to_points(mm: f32) -> f64 {
    (f64::from(mm) / f64::from(MM_PER_INCH)) * POINTS_PER_INCH
}

/// Format file size in human-readable format
pub fn format_file_size(bytes: u64) -> String {
    const KB: u64 = 1024;
    const MB: u64 = KB * 1024;
    const GB: u64 = MB * 1024;

    if bytes >= GB {
        format!("{:.2} GB", bytes as f64 / GB as f64)
    } else if bytes >= MB {
        format!("{:.2} MB", bytes as f64 / MB as f64)
    } else if bytes >= KB {
        format!("{:.2} KB", bytes as f64 / KB as f64)
    } else {
        format!("{} B", bytes)
    }
}

/// Format duration in human-readable format
pub fn format_duration(duration: std::time::Duration) -> String {
    let secs = duration.as_secs();
    let millis = duration.subsec_millis();

    if secs >= 3600 {
        let hours = secs / 3600;
        let mins = (secs % 3600) / 60;
        format!("{}h {}m", hours, mins)
    } else if secs >= 60 {
        let mins = secs / 60;
        let remaining_secs = secs % 60;
        format!("{}m {}s", mins, remaining_secs)
    } else if secs > 0 {
        format!("{}.{:03}s", secs, millis)
    } else {
        format!("{}ms", millis)
    }
}

/// Resolve the Python venv path for AI bridge tools (RealESRGAN, YomiToku).
///
/// Resolution order:
/// 1. `SUPERBOOK_VENV` environment variable (if set)
/// 2. Relative to the executable: `<exe_dir>/ai_bridge/ai_venv`
/// 3. Relative to the executable's parent: `<exe_dir>/../ai_bridge/ai_venv`
/// 4. CWD-relative fallback: `./ai_bridge/ai_venv`
pub fn resolve_venv_path() -> PathBuf {
    // 1. Explicit env var takes priority
    if let Ok(path) = std::env::var("SUPERBOOK_VENV") {
        return PathBuf::from(path);
    }

    // 2-3. Try paths relative to the executable
    if let Ok(exe) = std::env::current_exe() {
        if let Some(exe_dir) = exe.parent() {
            let candidates = [
                exe_dir.join("ai_bridge/ai_venv"),
                exe_dir.join("../ai_bridge/ai_venv"),
            ];
            for candidate in &candidates {
                if candidate.is_dir() {
                    return candidate.to_path_buf();
                }
            }
        }
    }

    // 4. CWD-relative fallback
    PathBuf::from("./ai_bridge/ai_venv")
}

/// Clamp a value to a range
#[inline]
pub fn clamp<T: PartialOrd>(value: T, min: T, max: T) -> T {
    if value < min {
        min
    } else if value > max {
        max
    } else {
        value
    }
}

/// Calculate percentage
#[inline]
pub fn percentage(current: usize, total: usize) -> f32 {
    if total == 0 {
        0.0
    } else {
        (current as f32 / total as f32) * 100.0
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_pixels_to_mm() {
        // At 72 DPI, 72 pixels = 1 inch = 25.4 mm
        let mm = pixels_to_mm(72, 72);
        assert!((mm - 25.4).abs() < 0.01);

        // At 300 DPI, 300 pixels = 1 inch = 25.4 mm
        let mm = pixels_to_mm(300, 300);
        assert!((mm - 25.4).abs() < 0.01);
    }

    #[test]
    fn test_mm_to_pixels() {
        // 25.4 mm at 72 DPI = 72 pixels
        let px = mm_to_pixels(25.4, 72);
        assert_eq!(px, 72);

        // 25.4 mm at 300 DPI = 300 pixels
        let px = mm_to_pixels(25.4, 300);
        assert_eq!(px, 300);
    }

    #[test]
    fn test_points_to_mm() {
        // 72 points = 1 inch = 25.4 mm
        let mm = points_to_mm(72.0);
        assert!((mm - 25.4).abs() < 0.01);
    }

    #[test]
    fn test_mm_to_points() {
        // 25.4 mm = 1 inch = 72 points
        let pt = mm_to_points(25.4);
        assert!((pt - 72.0).abs() < 0.01);
    }

    #[test]
    fn test_ensure_file_exists_nonexistent() {
        let result = ensure_file_exists("/nonexistent/file.txt");
        assert!(result.is_err());
    }

    #[test]
    fn test_load_image_nonexistent() {
        let result = load_image("/nonexistent/image.png");
        assert!(result.is_err());
    }

    #[test]
    fn test_format_file_size() {
        assert_eq!(format_file_size(0), "0 B");
        assert_eq!(format_file_size(500), "500 B");
        assert_eq!(format_file_size(1024), "1.00 KB");
        assert_eq!(format_file_size(1536), "1.50 KB");
        assert_eq!(format_file_size(1_048_576), "1.00 MB");
        assert_eq!(format_file_size(1_073_741_824), "1.00 GB");
    }

    #[test]
    fn test_format_duration() {
        use std::time::Duration;

        assert_eq!(format_duration(Duration::from_millis(500)), "500ms");
        assert_eq!(format_duration(Duration::from_secs(5)), "5.000s");
        assert_eq!(format_duration(Duration::from_secs(65)), "1m 5s");
        assert_eq!(format_duration(Duration::from_secs(3665)), "1h 1m");
    }

    #[test]
    fn test_clamp() {
        assert_eq!(clamp(5, 0, 10), 5);
        assert_eq!(clamp(-5, 0, 10), 0);
        assert_eq!(clamp(15, 0, 10), 10);
        assert_eq!(clamp(0.5f32, 0.0, 1.0), 0.5);
    }

    #[test]
    fn test_percentage() {
        assert_eq!(percentage(50, 100), 50.0);
        assert_eq!(percentage(0, 100), 0.0);
        assert_eq!(percentage(100, 100), 100.0);
        assert_eq!(percentage(0, 0), 0.0); // Edge case: no div by zero
    }

    #[test]
    fn test_ensure_dir_writable() {
        let temp_dir = tempfile::tempdir().unwrap();
        let result = ensure_dir_writable(temp_dir.path());
        assert!(result.is_ok());
    }

    #[test]
    fn test_ensure_dir_writable_creates_dir() {
        let temp_dir = tempfile::tempdir().unwrap();
        let new_dir = temp_dir.path().join("new_subdir");
        let result = ensure_dir_writable(&new_dir);
        assert!(result.is_ok());
        assert!(new_dir.exists());
    }

    #[test]
    fn test_roundtrip_mm_pixels() {
        // Test roundtrip conversion
        let original_mm = 100.0f32;
        let dpi = 300u32;
        let pixels = mm_to_pixels(original_mm, dpi);
        let back_to_mm = pixels_to_mm(pixels, dpi);
        assert!((original_mm - back_to_mm).abs() < 0.1);
    }

    #[test]
    fn test_roundtrip_mm_points() {
        // Test roundtrip conversion
        let original_mm = 100.0f32;
        let points = mm_to_points(original_mm);
        let back_to_mm = points_to_mm(points);
        assert!((original_mm - back_to_mm).abs() < 0.1);
    }

    #[test]
    fn test_format_file_size_edge_cases() {
        // Just under KB
        assert_eq!(format_file_size(1023), "1023 B");
        // Exact boundaries
        assert_eq!(format_file_size(1024 * 1024 - 1), "1024.00 KB");
    }

    #[test]
    fn test_clamp_boundary_values() {
        // Exact boundary values
        assert_eq!(clamp(0, 0, 10), 0);
        assert_eq!(clamp(10, 0, 10), 10);
        // Same min and max
        assert_eq!(clamp(5, 5, 5), 5);
    }

    // ==================== 追加テスト ====================

    #[test]
    fn test_pixels_to_mm_high_dpi() {
        // At 600 DPI (high quality), 600 pixels = 1 inch = 25.4 mm
        let mm = pixels_to_mm(600, 600);
        assert!((mm - 25.4).abs() < 0.01);
    }

    #[test]
    fn test_pixels_to_mm_zero_pixels() {
        // Zero pixels should return 0 mm
        let mm = pixels_to_mm(0, 300);
        assert_eq!(mm, 0.0);
    }

    #[test]
    fn test_pixels_to_mm_large_value() {
        // Large pixel count (e.g., 8K image width at 300 DPI)
        let mm = pixels_to_mm(7680, 300);
        // 7680 / 300 * 25.4 = 650.24 mm
        assert!((mm - 650.24).abs() < 0.1);
    }

    #[test]
    fn test_mm_to_pixels_zero_mm() {
        // Zero mm should return 0 pixels
        let px = mm_to_pixels(0.0, 300);
        assert_eq!(px, 0);
    }

    #[test]
    fn test_mm_to_pixels_high_dpi() {
        // 25.4 mm at 600 DPI = 600 pixels
        let px = mm_to_pixels(25.4, 600);
        assert_eq!(px, 600);
    }

    #[test]
    fn test_mm_to_pixels_a4_width() {
        // A4 width is 210 mm, at 300 DPI
        let px = mm_to_pixels(210.0, 300);
        // 210 / 25.4 * 300 = 2480.31... → 2480
        assert_eq!(px, 2480);
    }

    #[test]
    fn test_points_to_mm_zero() {
        // Zero points should return 0 mm
        let mm = points_to_mm(0.0);
        assert_eq!(mm, 0.0);
    }

    #[test]
    fn test_points_to_mm_a4_height() {
        // A4 height in points is approximately 841.89 points
        let mm = points_to_mm(841.89);
        // Should be close to 297 mm
        assert!((mm - 297.0).abs() < 0.1);
    }

    #[test]
    fn test_mm_to_points_zero() {
        // Zero mm should return 0 points
        let pt = mm_to_points(0.0);
        assert_eq!(pt, 0.0);
    }

    #[test]
    fn test_mm_to_points_a4_width() {
        // A4 width is 210 mm
        let pt = mm_to_points(210.0);
        // Should be approximately 595.28 points
        assert!((pt - 595.28).abs() < 0.1);
    }

    #[test]
    fn test_format_file_size_large_gb() {
        // Test larger GB values
        let size = 5 * 1024 * 1024 * 1024u64; // 5 GB
        assert_eq!(format_file_size(size), "5.00 GB");
    }

    #[test]
    fn test_format_file_size_fractional_mb() {
        // 2.5 MB
        let size = (2.5 * 1024.0 * 1024.0) as u64;
        assert_eq!(format_file_size(size), "2.50 MB");
    }

    #[test]
    fn test_format_duration_zero() {
        use std::time::Duration;
        assert_eq!(format_duration(Duration::from_millis(0)), "0ms");
    }

    #[test]
    fn test_format_duration_exact_minute() {
        use std::time::Duration;
        assert_eq!(format_duration(Duration::from_secs(60)), "1m 0s");
    }

    #[test]
    fn test_format_duration_exact_hour() {
        use std::time::Duration;
        assert_eq!(format_duration(Duration::from_secs(3600)), "1h 0m");
    }

    #[test]
    fn test_format_duration_multiple_hours() {
        use std::time::Duration;
        // 2 hours, 30 minutes, 45 seconds
        assert_eq!(
            format_duration(Duration::from_secs(2 * 3600 + 30 * 60 + 45)),
            "2h 30m"
        );
    }

    #[test]
    fn test_format_duration_with_millis() {
        use std::time::Duration;
        assert_eq!(format_duration(Duration::from_millis(1500)), "1.500s");
    }

    #[test]
    fn test_clamp_negative_range() {
        // Negative range
        assert_eq!(clamp(-5, -10, -1), -5);
        assert_eq!(clamp(-15, -10, -1), -10);
        assert_eq!(clamp(0, -10, -1), -1);
    }

    #[test]
    fn test_clamp_float_precision() {
        // Float precision
        assert!((clamp(0.5f64, 0.0, 1.0) - 0.5).abs() < f64::EPSILON);
        assert!((clamp(-0.5f64, 0.0, 1.0) - 0.0).abs() < f64::EPSILON);
        assert!((clamp(1.5f64, 0.0, 1.0) - 1.0).abs() < f64::EPSILON);
    }

    #[test]
    fn test_percentage_small_values() {
        // 1 out of 1000
        let p = percentage(1, 1000);
        assert!((p - 0.1).abs() < 0.001);
    }

    #[test]
    fn test_percentage_over_100() {
        // current > total (unusual but valid)
        let p = percentage(150, 100);
        assert_eq!(p, 150.0);
    }

    #[test]
    fn test_percentage_large_numbers() {
        // Large numbers
        let p = percentage(500_000, 1_000_000);
        assert_eq!(p, 50.0);
    }

    #[test]
    fn test_ensure_file_exists_is_directory() {
        // Path exists but is a directory, not a file
        let temp_dir = tempfile::tempdir().unwrap();
        let result = ensure_file_exists(temp_dir.path());
        assert!(result.is_err());
        assert!(result.unwrap_err().contains("not a file"));
    }

    #[test]
    fn test_ensure_file_exists_valid_file() {
        // Create a temporary file and check it
        let temp_dir = tempfile::tempdir().unwrap();
        let file_path = temp_dir.path().join("test.txt");
        std::fs::write(&file_path, b"test").unwrap();

        let result = ensure_file_exists(&file_path);
        assert!(result.is_ok());
    }

    #[test]
    fn test_load_image_invalid_format() {
        // Create a file with invalid image data
        let temp_dir = tempfile::tempdir().unwrap();
        let file_path = temp_dir.path().join("invalid.png");
        std::fs::write(&file_path, b"not an image").unwrap();

        let result = load_image(&file_path);
        assert!(result.is_err());
        assert!(result.unwrap_err().contains("Failed to load image"));
    }

    #[test]
    fn test_ensure_dir_writable_nested() {
        // Create nested directories
        let temp_dir = tempfile::tempdir().unwrap();
        let nested_dir = temp_dir.path().join("a").join("b").join("c");

        let result = ensure_dir_writable(&nested_dir);
        assert!(result.is_ok());
        assert!(nested_dir.exists());
    }

    #[test]
    fn test_conversion_consistency() {
        // Test that all conversion functions are consistent
        let dpi = 300u32;

        // 1 inch = 25.4 mm = 72 points = dpi pixels
        let inch_in_mm = 25.4f32;
        let inch_in_points = 72.0f64;
        let inch_in_pixels = dpi;

        // mm -> pixels -> mm
        let pixels_from_mm = mm_to_pixels(inch_in_mm, dpi);
        assert_eq!(pixels_from_mm, inch_in_pixels);

        // points -> mm -> points
        let mm_from_points = points_to_mm(inch_in_points);
        assert!((mm_from_points - inch_in_mm).abs() < 0.01);
    }

    #[test]
    fn test_format_file_size_boundary_kb_mb() {
        // Exactly at KB/MB boundary
        let exactly_mb = 1024u64 * 1024;
        assert_eq!(format_file_size(exactly_mb), "1.00 MB");

        // One byte less than MB
        assert_eq!(format_file_size(exactly_mb - 1), "1024.00 KB");
    }

    #[test]
    fn test_format_file_size_boundary_mb_gb() {
        // Exactly at MB/GB boundary
        let exactly_gb = 1024u64 * 1024 * 1024;
        assert_eq!(format_file_size(exactly_gb), "1.00 GB");

        // One byte less than GB
        assert_eq!(format_file_size(exactly_gb - 1), "1024.00 MB");
    }

    // ============ Concurrency Tests ============

    #[test]
    fn test_format_file_size_thread_safe() {
        use std::thread;

        let handles: Vec<_> = (0..8)
            .map(|i| {
                thread::spawn(move || {
                    let size = 1024u64.pow(i % 4); // B, KB, MB, GB
                    format_file_size(size)
                })
            })
            .collect();

        let results: Vec<_> = handles.into_iter().map(|h| h.join().unwrap()).collect();
        assert_eq!(results.len(), 8);
    }

    #[test]
    fn test_format_duration_thread_safe() {
        use std::thread;
        use std::time::Duration;

        let handles: Vec<_> = (0..8)
            .map(|i| {
                thread::spawn(move || {
                    let duration = Duration::from_secs(i * 100);
                    format_duration(duration)
                })
            })
            .collect();

        let results: Vec<_> = handles.into_iter().map(|h| h.join().unwrap()).collect();
        assert_eq!(results.len(), 8);
    }

    #[test]
    fn test_conversion_functions_thread_safe() {
        use std::thread;

        let handles: Vec<_> = (0..8)
            .map(|i| {
                thread::spawn(move || {
                    let dpi = 150 + (i as u32 * 50);
                    let px = mm_to_pixels(100.0, dpi);
                    let mm = pixels_to_mm(px, dpi);
                    (px, mm)
                })
            })
            .collect();

        let results: Vec<_> = handles.into_iter().map(|h| h.join().unwrap()).collect();
        assert_eq!(results.len(), 8);
        for (_, mm) in &results {
            // Roundtrip should be close
            assert!((*mm - 100.0).abs() < 1.0);
        }
    }

    #[test]
    fn test_clamp_thread_safe() {
        use rayon::prelude::*;

        let results: Vec<_> = (0..1000)
            .into_par_iter()
            .map(|i| clamp(i, 100, 900))
            .collect();

        assert_eq!(results.len(), 1000);
        for (i, &val) in results.iter().enumerate() {
            if i < 100 {
                assert_eq!(val, 100);
            } else if i > 900 {
                assert_eq!(val, 900);
            } else {
                assert_eq!(val, i as i32);
            }
        }
    }

    #[test]
    fn test_percentage_thread_safe() {
        use rayon::prelude::*;

        let results: Vec<_> = (0..=100)
            .into_par_iter()
            .map(|i| percentage(i, 100))
            .collect();

        assert_eq!(results.len(), 101);
        for (i, &pct) in results.iter().enumerate() {
            // Use approximate comparison due to floating point precision
            assert!(
                (pct - i as f32).abs() < 0.001,
                "Expected {} but got {}",
                i,
                pct
            );
        }
    }

    // ============ Additional Boundary Tests ============

    #[test]
    fn test_pixels_to_mm_one_pixel() {
        // Single pixel at various DPIs
        let mm_72 = pixels_to_mm(1, 72);
        let mm_300 = pixels_to_mm(1, 300);
        let mm_600 = pixels_to_mm(1, 600);

        assert!(mm_72 > mm_300);
        assert!(mm_300 > mm_600);
    }

    #[test]
    fn test_pixels_to_mm_max_dpi() {
        // Very high DPI
        let mm = pixels_to_mm(2400, 2400);
        assert!((mm - 25.4).abs() < 0.01);
    }

    #[test]
    fn test_mm_to_pixels_fractional() {
        // Fractional mm values
        let px = mm_to_pixels(0.1, 300);
        // 0.1 / 25.4 * 300 = 1.18 → 1
        assert_eq!(px, 1);
    }

    #[test]
    fn test_points_to_mm_large_value() {
        // Large document (e.g., poster)
        let mm = points_to_mm(2834.65); // 1000mm in points
        assert!((mm - 1000.0).abs() < 1.0);
    }

    #[test]
    fn test_mm_to_points_small_value() {
        // Small value (0.1mm)
        let pt = mm_to_points(0.1);
        // 0.1 / 25.4 * 72 = 0.283
        assert!((pt - 0.283).abs() < 0.01);
    }

    #[test]
    fn test_format_file_size_max_u64() {
        // Maximum u64 value
        let size = u64::MAX;
        let result = format_file_size(size);
        assert!(result.contains("GB"));
    }

    #[test]
    fn test_format_duration_max_hours() {
        use std::time::Duration;

        // 999 hours
        let duration = Duration::from_secs(999 * 3600 + 59 * 60);
        let result = format_duration(duration);
        assert_eq!(result, "999h 59m");
    }

    #[test]
    fn test_format_duration_subsec_only() {
        use std::time::Duration;

        // 999 ms
        let duration = Duration::from_millis(999);
        assert_eq!(format_duration(duration), "999ms");
    }

    #[test]
    fn test_clamp_with_u8() {
        assert_eq!(clamp(0u8, 10, 200), 10);
        assert_eq!(clamp(255u8, 10, 200), 200);
        assert_eq!(clamp(128u8, 10, 200), 128);
    }

    #[test]
    fn test_clamp_with_i64() {
        assert_eq!(clamp(i64::MIN, -100, 100), -100);
        assert_eq!(clamp(i64::MAX, -100, 100), 100);
        assert_eq!(clamp(0i64, -100, 100), 0);
    }

    #[test]
    fn test_percentage_boundary_values() {
        // Zero total
        assert_eq!(percentage(0, 0), 0.0);
        assert_eq!(percentage(100, 0), 0.0);

        // Large values
        assert_eq!(percentage(usize::MAX / 2, usize::MAX / 2), 100.0);
    }

    #[test]
    fn test_ensure_dir_writable_existing_file() {
        // Try to ensure writable on a path that exists as a file
        let temp_dir = tempfile::tempdir().unwrap();
        let file_path = temp_dir.path().join("file.txt");
        std::fs::write(&file_path, b"test").unwrap();

        // This should fail because file_path is a file, not a directory
        let result = ensure_dir_writable(&file_path);
        assert!(result.is_err());
    }

    #[test]
    fn test_load_image_error_message_contains_path() {
        let path = "/nonexistent/path/to/image.png";
        let result = load_image(path);
        assert!(result.is_err());
        assert!(result.unwrap_err().contains(path));
    }

    #[test]
    fn test_ensure_file_exists_error_message_contains_path() {
        let path = "/nonexistent/path/to/file.txt";
        let result = ensure_file_exists(path);
        assert!(result.is_err());
        assert!(result.unwrap_err().contains(path));
    }

    #[test]
    fn test_pixels_mm_consistency_across_dpis() {
        // 1 inch should always be 25.4mm regardless of DPI
        let dpis = [72, 96, 150, 300, 600, 1200];
        for dpi in dpis {
            let mm = pixels_to_mm(dpi, dpi);
            assert!(
                (mm - 25.4).abs() < 0.01,
                "Failed for DPI {}: expected 25.4, got {}",
                dpi,
                mm
            );
        }
    }

    #[test]
    fn test_points_mm_bidirectional() {
        // Test multiple values for bidirectional consistency
        let mm_values = [0.0, 1.0, 10.0, 25.4, 100.0, 297.0, 1000.0];
        for &mm in &mm_values {
            let points = mm_to_points(mm);
            let back = points_to_mm(points);
            assert!(
                (mm - back).abs() < 0.01,
                "Roundtrip failed for mm={}: got {}",
                mm,
                back
            );
        }
    }
}
