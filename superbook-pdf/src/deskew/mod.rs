//! Deskew (Skew Correction) module
//!
//! Provides functionality to detect and correct image skew/rotation.
//!
//! # Features
//!
//! - Multiple detection algorithms (Hough, Projection, Combined)
//! - Configurable quality modes (Fast, Standard, High Quality)
//! - Threshold-based correction skipping
//! - Batch processing support
//!
//! # Example
//!
//! ```rust,no_run
//! use superbook_pdf::{DeskewOptions, ImageProcDeskewer};
//! use std::path::Path;
//!
//! let options = DeskewOptions::builder()
//!     .max_angle(15.0)
//!     .threshold_angle(0.5)
//!     .build();
//!
//! let detection = ImageProcDeskewer::detect_skew(
//!     Path::new("scanned.png"),
//!     &options
//! ).unwrap();
//!
//! println!("Detected angle: {:.2}°", detection.angle);
//! ```

// Submodules
mod algorithm;
mod types;

// Re-export public API
pub use algorithm::ImageProcDeskewer;
pub use types::{
    DeskewAlgorithm, DeskewError, DeskewOptions, DeskewOptionsBuilder, DeskewResult, Deskewer,
    QualityMode, Result, SkewDetection, DEFAULT_BACKGROUND_COLOR, DEFAULT_MAX_ANGLE,
    DEFAULT_THRESHOLD_ANGLE,
};

#[cfg(test)]
mod tests {
    use super::*;
    use image::GrayImage;
    use std::path::PathBuf;

    #[test]
    fn test_default_options() {
        let opts = DeskewOptions::default();

        assert!(matches!(opts.algorithm, DeskewAlgorithm::HoughLines));
        assert_eq!(opts.max_angle, 15.0);
        assert_eq!(opts.threshold_angle, 0.1);
        assert_eq!(opts.background_color, [255, 255, 255]);
        assert!(matches!(opts.quality_mode, QualityMode::Standard));
    }

    #[test]
    fn test_builder_pattern() {
        let options = DeskewOptions::builder()
            .algorithm(DeskewAlgorithm::Combined)
            .max_angle(10.0)
            .threshold_angle(0.5)
            .background_color([128, 128, 128])
            .quality_mode(QualityMode::HighQuality)
            .build();

        assert!(matches!(options.algorithm, DeskewAlgorithm::Combined));
        assert_eq!(options.max_angle, 10.0);
        assert_eq!(options.threshold_angle, 0.5);
        assert_eq!(options.background_color, [128, 128, 128]);
        assert!(matches!(options.quality_mode, QualityMode::HighQuality));
    }

    #[test]
    fn test_high_quality_preset() {
        let options = DeskewOptions::high_quality();

        assert!(matches!(options.algorithm, DeskewAlgorithm::Combined));
        assert!(matches!(options.quality_mode, QualityMode::HighQuality));
    }

    #[test]
    fn test_fast_preset() {
        let options = DeskewOptions::fast();

        assert!(matches!(
            options.algorithm,
            DeskewAlgorithm::ProjectionProfile
        ));
        assert!(matches!(options.quality_mode, QualityMode::Fast));
        assert_eq!(options.threshold_angle, 0.5);
    }

    #[test]
    fn test_max_angle_setting() {
        let options = DeskewOptions::builder().max_angle(10.0).build();
        assert_eq!(options.max_angle, 10.0);

        // Negative angles should be converted to positive
        let options = DeskewOptions::builder().max_angle(-5.0).build();
        assert_eq!(options.max_angle, 5.0);
    }

    #[test]
    fn test_background_color_setting() {
        let options = DeskewOptions::builder()
            .background_color([128, 128, 128])
            .build();
        assert_eq!(options.background_color, [128, 128, 128]);

        let options = DeskewOptions::builder().background_color([0, 0, 0]).build();
        assert_eq!(options.background_color, [0, 0, 0]);
    }

    #[test]
    fn test_all_quality_modes() {
        let modes = vec![
            QualityMode::Fast,
            QualityMode::Standard,
            QualityMode::HighQuality,
        ];

        for mode in modes {
            let options = DeskewOptions::builder().quality_mode(mode).build();
            match (mode, options.quality_mode) {
                (QualityMode::Fast, QualityMode::Fast) => {}
                (QualityMode::Standard, QualityMode::Standard) => {}
                (QualityMode::HighQuality, QualityMode::HighQuality) => {}
                _ => panic!("Mode mismatch"),
            }
        }
    }

    #[test]
    fn test_all_algorithms() {
        let algorithms = vec![
            DeskewAlgorithm::HoughLines,
            DeskewAlgorithm::ProjectionProfile,
            DeskewAlgorithm::TextLineDetection,
            DeskewAlgorithm::Combined,
        ];

        for algo in algorithms {
            let options = DeskewOptions::builder().algorithm(algo).build();
            match (algo, options.algorithm) {
                (DeskewAlgorithm::HoughLines, DeskewAlgorithm::HoughLines) => {}
                (DeskewAlgorithm::ProjectionProfile, DeskewAlgorithm::ProjectionProfile) => {}
                (DeskewAlgorithm::TextLineDetection, DeskewAlgorithm::TextLineDetection) => {}
                (DeskewAlgorithm::Combined, DeskewAlgorithm::Combined) => {}
                _ => panic!("Algorithm mismatch"),
            }
        }
    }

    #[test]
    fn test_skew_detection_construction() {
        let detection = SkewDetection {
            angle: 2.5,
            confidence: 0.85,
            feature_count: 42,
        };

        assert_eq!(detection.angle, 2.5);
        assert_eq!(detection.confidence, 0.85);
        assert_eq!(detection.feature_count, 42);
    }

    #[test]
    fn test_deskew_result_construction() {
        let detection = SkewDetection {
            angle: 2.5,
            confidence: 0.85,
            feature_count: 42,
        };

        let result = DeskewResult {
            detection,
            corrected: true,
            output_path: PathBuf::from("/output/corrected.png"),
            original_size: (1000, 1500),
            corrected_size: (1020, 1520),
        };

        assert!(result.corrected);
        assert_eq!(result.original_size, (1000, 1500));
        assert_eq!(result.corrected_size, (1020, 1520));
        assert_eq!(result.detection.angle, 2.5);
    }

    #[test]
    fn test_error_types() {
        let _err1 = DeskewError::ImageNotFound(PathBuf::from("/test/path"));
        let _err2 = DeskewError::InvalidFormat("Invalid image format".to_string());
        let _err3 = DeskewError::DetectionFailed("Failed to detect edges".to_string());
        let _err4 = DeskewError::CorrectionFailed("Failed to rotate".to_string());
        let _err5: DeskewError = std::io::Error::new(std::io::ErrorKind::NotFound, "test").into();
    }

    #[test]
    fn test_threshold_angle_setting() {
        let options = DeskewOptions::builder().threshold_angle(0.5).build();
        assert_eq!(options.threshold_angle, 0.5);

        // Negative should become positive
        let options = DeskewOptions::builder().threshold_angle(-0.3).build();
        assert_eq!(options.threshold_angle, 0.3);
    }

    #[test]
    fn test_options_debug_impl() {
        let options = DeskewOptions::builder().max_angle(10.0).build();
        let debug_str = format!("{:?}", options);
        assert!(debug_str.contains("DeskewOptions"));
        assert!(debug_str.contains("10"));
    }

    #[test]
    fn test_options_clone() {
        let original = DeskewOptions::builder()
            .max_angle(12.0)
            .threshold_angle(0.3)
            .background_color([100, 100, 100])
            .build();
        let cloned = original.clone();
        assert_eq!(cloned.max_angle, original.max_angle);
        assert_eq!(cloned.threshold_angle, original.threshold_angle);
        assert_eq!(cloned.background_color, original.background_color);
    }

    #[test]
    fn test_algorithm_debug_impl() {
        let algo = DeskewAlgorithm::Combined;
        let debug_str = format!("{:?}", algo);
        assert!(debug_str.contains("Combined"));
    }

    #[test]
    fn test_quality_mode_debug_impl() {
        let mode = QualityMode::HighQuality;
        let debug_str = format!("{:?}", mode);
        assert!(debug_str.contains("HighQuality"));
    }

    #[test]
    fn test_error_debug_impl() {
        let err = DeskewError::DetectionFailed("test".to_string());
        let debug_str = format!("{:?}", err);
        assert!(debug_str.contains("DetectionFailed"));
    }

    #[test]
    fn test_skew_detection_debug_impl() {
        let detection = SkewDetection {
            angle: 2.5,
            confidence: 0.9,
            feature_count: 50,
        };
        let debug_str = format!("{:?}", detection);
        assert!(debug_str.contains("SkewDetection"));
        assert!(debug_str.contains("2.5"));
    }

    #[test]
    fn test_skew_detection_clone() {
        let original = SkewDetection {
            angle: 3.0,
            confidence: 0.85,
            feature_count: 100,
        };
        let cloned = original.clone();
        assert_eq!(cloned.angle, original.angle);
        assert_eq!(cloned.confidence, original.confidence);
        assert_eq!(cloned.feature_count, original.feature_count);
    }

    #[test]
    fn test_deskew_result_debug_impl() {
        let detection = SkewDetection {
            angle: 1.0,
            confidence: 0.8,
            feature_count: 30,
        };
        let result = DeskewResult {
            detection,
            corrected: true,
            output_path: PathBuf::from("/out.png"),
            original_size: (100, 100),
            corrected_size: (110, 110),
        };
        let debug_str = format!("{:?}", result);
        assert!(debug_str.contains("DeskewResult"));
    }

    #[test]
    fn test_max_angle_boundary_values() {
        let opts_zero = DeskewOptions::builder().max_angle(0.0).build();
        assert_eq!(opts_zero.max_angle, 0.0);

        let opts_small = DeskewOptions::builder().max_angle(0.001).build();
        assert_eq!(opts_small.max_angle, 0.001);

        let opts_large = DeskewOptions::builder().max_angle(45.0).build();
        assert_eq!(opts_large.max_angle, 45.0);

        let opts_extreme = DeskewOptions::builder().max_angle(90.0).build();
        assert_eq!(opts_extreme.max_angle, 90.0);
    }

    #[test]
    fn test_threshold_angle_boundary_values() {
        let opts_zero = DeskewOptions::builder().threshold_angle(0.0).build();
        assert_eq!(opts_zero.threshold_angle, 0.0);

        let opts_tiny = DeskewOptions::builder().threshold_angle(0.001).build();
        assert_eq!(opts_tiny.threshold_angle, 0.001);

        let opts_large = DeskewOptions::builder().threshold_angle(10.0).build();
        assert_eq!(opts_large.threshold_angle, 10.0);
    }

    #[test]
    fn test_confidence_values() {
        let low_conf = SkewDetection {
            angle: 1.0,
            confidence: 0.0,
            feature_count: 0,
        };
        assert_eq!(low_conf.confidence, 0.0);

        let perfect_conf = SkewDetection {
            angle: 1.0,
            confidence: 1.0,
            feature_count: 1000,
        };
        assert_eq!(perfect_conf.confidence, 1.0);

        let typical_conf = SkewDetection {
            angle: 1.0,
            confidence: 0.75,
            feature_count: 100,
        };
        assert!(typical_conf.confidence > 0.5 && typical_conf.confidence < 1.0);
    }

    #[test]
    fn test_feature_count_boundary_values() {
        let detection = SkewDetection {
            angle: 1.0,
            confidence: 0.5,
            feature_count: 0,
        };
        assert_eq!(detection.feature_count, 0);

        let detection_large = SkewDetection {
            angle: 2.0,
            confidence: 0.99,
            feature_count: 10000,
        };
        assert_eq!(detection_large.feature_count, 10000);
    }

    #[test]
    fn test_algorithm_clone() {
        let original = DeskewAlgorithm::TextLineDetection;
        let cloned = original;
        assert!(matches!(cloned, DeskewAlgorithm::TextLineDetection));
    }

    #[test]
    fn test_quality_mode_clone() {
        let original = QualityMode::HighQuality;
        let cloned = original;
        assert!(matches!(cloned, QualityMode::HighQuality));
    }

    #[test]
    fn test_all_error_variants_display() {
        let errors: Vec<DeskewError> = vec![
            DeskewError::ImageNotFound(PathBuf::from("/test.png")),
            DeskewError::InvalidFormat("HEIC not supported".to_string()),
            DeskewError::DetectionFailed("no edges found".to_string()),
            DeskewError::CorrectionFailed("rotation error".to_string()),
            std::io::Error::other("io error").into(),
        ];

        for err in errors {
            let msg = err.to_string();
            assert!(!msg.is_empty());
        }
    }

    #[test]
    fn test_result_path_types() {
        let detection = SkewDetection {
            angle: 1.0,
            confidence: 0.8,
            feature_count: 50,
        };

        // Absolute path
        let result_abs = DeskewResult {
            detection: detection.clone(),
            corrected: true,
            output_path: PathBuf::from("/absolute/path/output.png"),
            original_size: (100, 100),
            corrected_size: (100, 100),
        };
        assert!(result_abs.output_path.is_absolute());

        // Relative path
        let result_rel = DeskewResult {
            detection,
            corrected: true,
            output_path: PathBuf::from("relative/output.png"),
            original_size: (100, 100),
            corrected_size: (100, 100),
        };
        assert!(result_rel.output_path.is_relative());
    }

    #[test]
    fn test_builder_default_creates_valid_options() {
        let opts = DeskewOptionsBuilder::default().build();
        assert!(opts.max_angle > 0.0);
        assert!(opts.threshold_angle >= 0.0);
    }

    #[test]
    fn test_large_image_dimensions() {
        let detection = SkewDetection {
            angle: 0.5,
            confidence: 0.9,
            feature_count: 1000,
        };

        // A3 at 600 DPI
        let result = DeskewResult {
            detection,
            corrected: true,
            output_path: PathBuf::from("/output.png"),
            original_size: (7016, 9933),
            corrected_size: (7050, 9970),
        };

        assert!(result.original_size.0 > 7000);
        assert!(result.original_size.1 > 9000);
    }

    #[test]
    fn test_small_image_dimensions() {
        let detection = SkewDetection {
            angle: 1.0,
            confidence: 0.5,
            feature_count: 10,
        };

        // Small thumbnail
        let result = DeskewResult {
            detection,
            corrected: true,
            output_path: PathBuf::from("/thumb.png"),
            original_size: (64, 64),
            corrected_size: (68, 68),
        };

        assert!(result.original_size.0 <= 100);
    }

    #[test]
    fn test_preset_consistency() {
        let fast = DeskewOptions::fast();
        let high = DeskewOptions::high_quality();
        let default_opts = DeskewOptions::default();

        // Fast should have higher threshold than default
        assert!(fast.threshold_angle >= default_opts.threshold_angle);

        // High quality should use combined algorithm
        assert!(matches!(high.algorithm, DeskewAlgorithm::Combined));
        assert!(matches!(high.quality_mode, QualityMode::HighQuality));
    }

    #[test]
    fn test_error_path_extraction() {
        let path = PathBuf::from("/some/path/file.png");
        let err = DeskewError::ImageNotFound(path.clone());

        if let DeskewError::ImageNotFound(p) = err {
            assert_eq!(p, path);
        } else {
            panic!("Wrong error variant");
        }
    }

    #[test]
    fn test_error_image_not_found_display() {
        let path = PathBuf::from("/test/image.png");
        let err = DeskewError::ImageNotFound(path);
        let msg = format!("{}", err);
        assert!(msg.contains("Image not found"));
        assert!(msg.contains("/test/image.png"));
    }

    #[test]
    fn test_error_invalid_format_display() {
        let err = DeskewError::InvalidFormat("Unsupported format: BMP".to_string());
        let msg = format!("{}", err);
        assert!(msg.contains("Invalid image format"));
        assert!(msg.contains("BMP"));
    }

    #[test]
    fn test_error_detection_failed_display() {
        let err = DeskewError::DetectionFailed("No edges detected".to_string());
        let msg = format!("{}", err);
        assert!(msg.contains("Detection failed"));
        assert!(msg.contains("No edges"));
    }

    #[test]
    fn test_error_correction_failed_display() {
        let err = DeskewError::CorrectionFailed("Rotation failed".to_string());
        let msg = format!("{}", err);
        assert!(msg.contains("Correction failed"));
    }

    #[test]
    fn test_error_from_io_error() {
        let io_err = std::io::Error::new(std::io::ErrorKind::NotFound, "file not found");
        let deskew_err: DeskewError = io_err.into();
        let msg = format!("{}", deskew_err);
        assert!(msg.contains("IO error"));
    }

    #[test]
    fn test_deskew_result_unchanged_size() {
        let result = DeskewResult {
            detection: SkewDetection {
                angle: 0.05,
                confidence: 0.8,
                feature_count: 50,
            },
            corrected: false,
            output_path: PathBuf::from("/output/image.png"),
            original_size: (1000, 1500),
            corrected_size: (1000, 1500),
        };
        assert!(!result.corrected);
        assert_eq!(result.original_size, result.corrected_size);
    }

    #[test]
    fn test_deskew_result_size_changed() {
        let result = DeskewResult {
            detection: SkewDetection {
                angle: 5.0,
                confidence: 0.9,
                feature_count: 200,
            },
            corrected: true,
            output_path: PathBuf::from("/output/corrected.png"),
            original_size: (1000, 1500),
            corrected_size: (1050, 1550),
        };
        assert!(result.corrected);
        assert!(result.corrected_size.0 >= result.original_size.0);
    }

    #[test]
    fn test_algorithm_default() {
        let algo = DeskewAlgorithm::default();
        assert!(matches!(algo, DeskewAlgorithm::HoughLines));
    }

    #[test]
    fn test_quality_mode_default() {
        let mode = QualityMode::default();
        assert!(matches!(mode, QualityMode::Standard));
    }

    #[test]
    fn test_algorithm_copy_trait() {
        let algo = DeskewAlgorithm::Combined;
        let copied = algo; // Copy
        let also_original = algo; // Still valid because Copy
        assert!(matches!(copied, DeskewAlgorithm::Combined));
        assert!(matches!(also_original, DeskewAlgorithm::Combined));
    }

    #[test]
    fn test_quality_mode_copy_trait() {
        let mode = QualityMode::HighQuality;
        let copied = mode; // Copy
        let also_original = mode; // Still valid because Copy
        assert!(matches!(copied, QualityMode::HighQuality));
        assert!(matches!(also_original, QualityMode::HighQuality));
    }

    // ==================== Concurrency Tests ====================

    #[test]
    fn test_deskew_types_send_sync() {
        fn assert_send_sync<T: Send + Sync>() {}

        assert_send_sync::<DeskewOptions>();
        assert_send_sync::<DeskewAlgorithm>();
        assert_send_sync::<QualityMode>();
        assert_send_sync::<SkewDetection>();
        assert_send_sync::<DeskewResult>();
        assert_send_sync::<DeskewError>();
    }

    #[test]
    fn test_concurrent_options_building() {
        use std::thread;

        let handles: Vec<_> = (0..10)
            .map(|i| {
                thread::spawn(move || {
                    let opts = DeskewOptions::builder()
                        .max_angle(5.0 + i as f64)
                        .threshold_angle(0.1 * i as f64)
                        .build();
                    (opts.max_angle, opts.threshold_angle)
                })
            })
            .collect();

        for (i, handle) in handles.into_iter().enumerate() {
            let (max_angle, threshold) = handle.join().unwrap();
            assert_eq!(max_angle, 5.0 + i as f64);
            assert!((threshold - 0.1 * i as f64).abs() < 0.001);
        }
    }

    #[test]
    fn test_detection_result_thread_transfer() {
        use std::thread;

        let detection = SkewDetection {
            angle: 3.5,
            confidence: 0.88,
            feature_count: 150,
        };

        let cloned = detection.clone();
        let handle = thread::spawn(move || {
            assert_eq!(cloned.angle, 3.5);
            assert_eq!(cloned.confidence, 0.88);
            assert_eq!(cloned.feature_count, 150);
            cloned
        });

        let received = handle.join().unwrap();
        assert_eq!(received.angle, detection.angle);
    }

    #[test]
    fn test_options_shared_across_threads() {
        use std::sync::Arc;
        use std::thread;

        let options = Arc::new(
            DeskewOptions::builder()
                .max_angle(10.0)
                .threshold_angle(0.5)
                .build(),
        );

        let handles: Vec<_> = (0..5)
            .map(|_| {
                let opts = Arc::clone(&options);
                thread::spawn(move || (opts.max_angle, opts.threshold_angle))
            })
            .collect();

        for handle in handles {
            let (max_angle, threshold) = handle.join().unwrap();
            assert_eq!(max_angle, 10.0);
            assert_eq!(threshold, 0.5);
        }
    }

    #[test]
    fn test_error_thread_transfer() {
        use std::thread;

        let err = DeskewError::DetectionFailed("test error".to_string());
        let handle = thread::spawn(move || err.to_string());

        let msg = handle.join().unwrap();
        assert!(msg.contains("test error"));
    }

    #[test]
    fn test_concurrent_algorithm_enum_usage() {
        use std::thread;

        let algorithms = [
            DeskewAlgorithm::HoughLines,
            DeskewAlgorithm::ProjectionProfile,
            DeskewAlgorithm::TextLineDetection,
            DeskewAlgorithm::Combined,
        ];

        let handles: Vec<_> = algorithms
            .into_iter()
            .map(|algo| thread::spawn(move || format!("{:?}", algo)))
            .collect();

        for handle in handles {
            let debug = handle.join().unwrap();
            assert!(!debug.is_empty());
        }
    }

    #[test]
    fn test_concurrent_quality_mode_usage() {
        use std::thread;

        let modes = [
            QualityMode::Fast,
            QualityMode::Standard,
            QualityMode::HighQuality,
        ];

        let handles: Vec<_> = modes
            .into_iter()
            .map(|mode| thread::spawn(move || format!("{:?}", mode)))
            .collect();

        for handle in handles {
            let debug = handle.join().unwrap();
            assert!(!debug.is_empty());
        }
    }

    #[test]
    fn test_parallel_detection_result_creation() {
        use std::thread;

        let handles: Vec<_> = (0..10)
            .map(|i| {
                thread::spawn(move || SkewDetection {
                    angle: i as f64 * 0.5,
                    confidence: (i as f64 + 1.0) / 11.0,
                    feature_count: (i + 1) * 10,
                })
            })
            .collect();

        let results: Vec<SkewDetection> = handles.into_iter().map(|h| h.join().unwrap()).collect();

        assert_eq!(results.len(), 10);
        for (i, detection) in results.iter().enumerate() {
            assert!((detection.angle - i as f64 * 0.5).abs() < 0.001);
        }
    }

    #[test]
    fn test_builder_debug_impl() {
        let builder = DeskewOptionsBuilder::default();
        let debug_str = format!("{:?}", builder);
        assert!(debug_str.contains("DeskewOptionsBuilder"));
    }

    #[test]
    fn test_options_builder_partial_config() {
        // Only configure some options, leave others as default
        let opts = DeskewOptions::builder().max_angle(20.0).build();

        assert_eq!(opts.max_angle, 20.0);
        // threshold_angle should still be default
        assert_eq!(opts.threshold_angle, DEFAULT_THRESHOLD_ANGLE);
    }

    #[test]
    fn test_deskew_options_all_algorithms_with_all_modes() {
        let algorithms = [
            DeskewAlgorithm::HoughLines,
            DeskewAlgorithm::ProjectionProfile,
            DeskewAlgorithm::TextLineDetection,
            DeskewAlgorithm::Combined,
        ];
        let modes = [
            QualityMode::Fast,
            QualityMode::Standard,
            QualityMode::HighQuality,
        ];

        for algo in &algorithms {
            for mode in &modes {
                let opts = DeskewOptions::builder()
                    .algorithm(*algo)
                    .quality_mode(*mode)
                    .build();
                // Verify the options are correctly set
                let debug = format!("{:?}", opts);
                assert!(!debug.is_empty());
            }
        }
    }

    #[test]
    fn test_deskew_result_output_path_variations() {
        let detection = SkewDetection {
            angle: 1.0,
            confidence: 0.9,
            feature_count: 50,
        };

        // Test with various path formats
        let paths = [
            "/absolute/path.png",
            "relative/path.png",
            "./current/dir.png",
            "../parent/dir.png",
            "file.png",
        ];

        for path_str in paths {
            let result = DeskewResult {
                detection: detection.clone(),
                corrected: true,
                output_path: PathBuf::from(path_str),
                original_size: (100, 100),
                corrected_size: (100, 100),
            };
            assert_eq!(result.output_path.to_str().unwrap(), path_str);
        }
    }

    #[test]
    fn test_float_precision_in_angle() {
        let angles = [
            0.123456789,
            std::f64::consts::PI,
            std::f64::consts::E,
            0.333333333,
        ];

        for angle in angles {
            let detection = SkewDetection {
                angle,
                confidence: 0.5,
                feature_count: 10,
            };
            assert!((detection.angle - angle).abs() < f64::EPSILON);
        }
    }

    // ============================================================
    // TC-DSK Additional Spec Tests
    // ============================================================

    // TC-DSK-007: 背景色設定
    #[test]
    fn test_tc_dsk_007_background_color_setting() {
        // Test DeskewOptions with different background colors
        let black_opts = DeskewOptions::builder()
            .background_color([0, 0, 0])
            .build();
        assert_eq!(black_opts.background_color, [0, 0, 0]);

        let white_opts = DeskewOptions::builder()
            .background_color([255, 255, 255])
            .build();
        assert_eq!(white_opts.background_color, [255, 255, 255]);

        let custom_opts = DeskewOptions::builder()
            .background_color([128, 128, 128])
            .build();
        assert_eq!(custom_opts.background_color, [128, 128, 128]);

        // Default should be white
        let default_opts = DeskewOptions::default();
        assert_eq!(default_opts.background_color, [255, 255, 255]);
    }

    // TC-DSK-008: 品質モード比較
    #[test]
    fn test_tc_dsk_008_quality_modes() {
        let modes = [
            QualityMode::Fast,
            QualityMode::Standard,
            QualityMode::HighQuality,
        ];

        for mode in modes {
            let opts = DeskewOptions::builder().quality_mode(mode).build();
            // Verify mode is set correctly by pattern matching
            match mode {
                QualityMode::Fast => {
                    assert!(matches!(opts.quality_mode, QualityMode::Fast))
                }
                QualityMode::Standard => {
                    assert!(matches!(opts.quality_mode, QualityMode::Standard))
                }
                QualityMode::HighQuality => {
                    assert!(matches!(opts.quality_mode, QualityMode::HighQuality))
                }
            }
        }

        // Default should be Standard
        let default_opts = DeskewOptions::default();
        assert!(matches!(default_opts.quality_mode, QualityMode::Standard));

        // Fast preset should use Fast mode
        let fast_opts = DeskewOptions::fast();
        assert!(matches!(fast_opts.quality_mode, QualityMode::Fast));

        // High quality preset should use HighQuality mode
        let hq_opts = DeskewOptions::high_quality();
        assert!(matches!(hq_opts.quality_mode, QualityMode::HighQuality));
    }

    // TC-DSK-010: 異なるアルゴリズム
    #[test]
    fn test_tc_dsk_010_different_algorithms() {
        let algorithms = [
            DeskewAlgorithm::HoughLines,
            DeskewAlgorithm::ProjectionProfile,
            DeskewAlgorithm::TextLineDetection,
            DeskewAlgorithm::Combined,
            DeskewAlgorithm::PageEdge,
        ];

        for algorithm in algorithms {
            let opts = DeskewOptions::builder().algorithm(algorithm).build();
            // Verify algorithm is set correctly
            match algorithm {
                DeskewAlgorithm::HoughLines => {
                    assert!(matches!(opts.algorithm, DeskewAlgorithm::HoughLines))
                }
                DeskewAlgorithm::ProjectionProfile => {
                    assert!(matches!(opts.algorithm, DeskewAlgorithm::ProjectionProfile))
                }
                DeskewAlgorithm::TextLineDetection => {
                    assert!(matches!(opts.algorithm, DeskewAlgorithm::TextLineDetection))
                }
                DeskewAlgorithm::Combined => {
                    assert!(matches!(opts.algorithm, DeskewAlgorithm::Combined))
                }
                DeskewAlgorithm::PageEdge => {
                    assert!(matches!(opts.algorithm, DeskewAlgorithm::PageEdge))
                }
            }
        }

        // Default should be HoughLines
        let default_opts = DeskewOptions::default();
        assert!(matches!(default_opts.algorithm, DeskewAlgorithm::HoughLines));

        // Fast preset uses ProjectionProfile
        let fast_opts = DeskewOptions::fast();
        assert!(matches!(
            fast_opts.algorithm,
            DeskewAlgorithm::ProjectionProfile
        ));

        // High quality preset uses Combined
        let hq_opts = DeskewOptions::high_quality();
        assert!(matches!(hq_opts.algorithm, DeskewAlgorithm::Combined));
    }

    // ============================================================
    // TC-DSK-011: PageEdge Algorithm Tests
    // ============================================================

    #[test]
    fn test_tc_dsk_011_page_edge_algorithm_basic() {
        // Create a test image with a vertical edge (simulating page boundary)
        let mut img = GrayImage::new(200, 100);

        // Fill with white
        for pixel in img.pixels_mut() {
            *pixel = image::Luma([255u8]);
        }

        // Draw a vertical line at x=50 (darker)
        for y in 0..100 {
            img.put_pixel(48, y, image::Luma([200u8]));
            img.put_pixel(49, y, image::Luma([150u8]));
            img.put_pixel(50, y, image::Luma([100u8]));
        }

        let options = DeskewOptions::builder()
            .algorithm(DeskewAlgorithm::PageEdge)
            .build();

        let result = ImageProcDeskewer::detect_skew_page_edge(&img, &options);
        assert!(result.is_ok());

        let detection = result.unwrap();
        // Vertical line should have angle close to 0
        assert!(
            detection.angle.abs() < 1.0,
            "Expected angle near 0, got {}",
            detection.angle
        );
    }

    #[test]
    fn test_tc_dsk_011_page_edge_with_tilt() {
        // Create a test image with a tilted edge
        let mut img = GrayImage::new(200, 100);

        // Fill with white
        for pixel in img.pixels_mut() {
            *pixel = image::Luma([255u8]);
        }

        // Draw a tilted line (2 degrees = approximately 3.5 pixels over 100 rows)
        // x position increases as y increases
        for y in 0..100 {
            let x = 50 + (y as f64 * 0.035) as u32; // ~2 degree tilt
            if x < 200 && x >= 2 {
                img.put_pixel(x - 2, y, image::Luma([200u8]));
                img.put_pixel(x - 1, y, image::Luma([150u8]));
                img.put_pixel(x, y, image::Luma([100u8]));
            }
        }

        let options = DeskewOptions::builder()
            .algorithm(DeskewAlgorithm::PageEdge)
            .build();

        let result = ImageProcDeskewer::detect_skew_page_edge(&img, &options);
        assert!(result.is_ok());

        let detection = result.unwrap();
        // Should detect some angle (exact value depends on implementation)
        // Just verify it detects a non-zero angle in the right direction
        assert!(
            detection.angle > 0.5,
            "Expected positive angle, got {}",
            detection.angle
        );
    }

    #[test]
    fn test_tc_dsk_011_page_edge_no_edge() {
        // Create a uniform white image (no edge)
        let img = GrayImage::from_fn(200, 100, |_, _| image::Luma([255u8]));

        let options = DeskewOptions::builder()
            .algorithm(DeskewAlgorithm::PageEdge)
            .build();

        let result = ImageProcDeskewer::detect_skew_page_edge(&img, &options);
        assert!(result.is_ok());

        let detection = result.unwrap();
        // No edge should result in 0 angle and low confidence
        assert_eq!(detection.angle, 0.0);
        assert!(detection.confidence < 0.5);
    }

    #[test]
    fn test_tc_dsk_011_page_edge_small_image() {
        // Very small image should still work
        let img = GrayImage::new(50, 20);

        let options = DeskewOptions::builder()
            .algorithm(DeskewAlgorithm::PageEdge)
            .build();

        let result = ImageProcDeskewer::detect_skew_page_edge(&img, &options);
        assert!(result.is_ok());
    }
}
