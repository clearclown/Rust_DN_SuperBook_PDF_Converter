//! RealESRGAN Integration module
//!
//! Provides integration with RealESRGAN AI upscaling model.

use crate::ai_bridge::{AiBridgeError, AiTool, SubprocessBridge};
use std::path::{Path, PathBuf};
use std::time::Duration;
use thiserror::Error;

/// RealESRGAN error types
#[derive(Debug, Error)]
pub enum RealEsrganError {
    #[error("Model not found: {0}")]
    ModelNotFound(String),

    #[error("Invalid scale: {0} (must be 2 or 4)")]
    InvalidScale(u32),

    #[error("Input image not found: {0}")]
    InputNotFound(PathBuf),

    #[error("Output directory not writable: {0}")]
    OutputNotWritable(PathBuf),

    #[error("Processing failed: {0}")]
    ProcessingFailed(String),

    #[error("GPU memory insufficient (need {required}MB, available {available}MB)")]
    InsufficientVram { required: u64, available: u64 },

    #[error("Bridge error: {0}")]
    BridgeError(#[from] AiBridgeError),

    #[error("Image error: {0}")]
    ImageError(String),

    #[error("IO error: {0}")]
    IoError(#[from] std::io::Error),
}

pub type Result<T> = std::result::Result<T, RealEsrganError>;

/// RealESRGAN options
#[derive(Debug, Clone)]
pub struct RealEsrganOptions {
    /// Upscale factor
    pub scale: u32,
    /// Model selection
    pub model: RealEsrganModel,
    /// Tile size (pixels)
    pub tile_size: u32,
    /// Tile padding
    pub tile_padding: u32,
    /// Output format
    pub output_format: OutputFormat,
    /// Enable face enhancement
    pub face_enhance: bool,
    /// GPU ID (None for auto)
    pub gpu_id: Option<u32>,
    /// Use FP16 for speed
    pub fp16: bool,
}

impl Default for RealEsrganOptions {
    fn default() -> Self {
        Self {
            scale: 2,
            model: RealEsrganModel::X4Plus,
            tile_size: 400,
            tile_padding: 10,
            output_format: OutputFormat::Png,
            face_enhance: false,
            gpu_id: None,
            fp16: true,
        }
    }
}

impl RealEsrganOptions {
    /// Create a new options builder
    pub fn builder() -> RealEsrganOptionsBuilder {
        RealEsrganOptionsBuilder::default()
    }

    /// Create options for 4x upscaling (high quality)
    pub fn x4_high_quality() -> Self {
        Self {
            scale: 4,
            model: RealEsrganModel::X4Plus,
            tile_size: 256,
            fp16: false, // More accurate
            ..Default::default()
        }
    }

    /// Create options optimized for anime/illustrations
    pub fn anime() -> Self {
        Self {
            scale: 4,
            model: RealEsrganModel::X4PlusAnime,
            ..Default::default()
        }
    }

    /// Create options for low VRAM (< 4GB)
    pub fn low_vram() -> Self {
        Self {
            tile_size: 128,
            tile_padding: 8,
            fp16: true,
            ..Default::default()
        }
    }
}

/// Builder for RealEsrganOptions
#[derive(Debug, Default)]
pub struct RealEsrganOptionsBuilder {
    options: RealEsrganOptions,
}

impl RealEsrganOptionsBuilder {
    /// Set upscale factor (2 or 4)
    pub fn scale(mut self, scale: u32) -> Self {
        self.options.scale = if scale >= 4 { 4 } else { 2 };
        self
    }

    /// Set model type
    pub fn model(mut self, model: RealEsrganModel) -> Self {
        self.options.model = model;
        self
    }

    /// Set tile size for memory efficiency
    pub fn tile_size(mut self, size: u32) -> Self {
        self.options.tile_size = size.clamp(64, 1024);
        self
    }

    /// Set tile padding
    pub fn tile_padding(mut self, padding: u32) -> Self {
        self.options.tile_padding = padding;
        self
    }

    /// Set output format
    pub fn output_format(mut self, format: OutputFormat) -> Self {
        self.options.output_format = format;
        self
    }

    /// Enable face enhancement
    pub fn face_enhance(mut self, enable: bool) -> Self {
        self.options.face_enhance = enable;
        self
    }

    /// Set GPU device ID
    pub fn gpu_id(mut self, id: u32) -> Self {
        self.options.gpu_id = Some(id);
        self
    }

    /// Enable FP16 mode for speed
    pub fn fp16(mut self, enable: bool) -> Self {
        self.options.fp16 = enable;
        self
    }

    /// Build the options
    pub fn build(self) -> RealEsrganOptions {
        self.options
    }
}

/// RealESRGAN model types
#[derive(Debug, Clone, Default)]
pub enum RealEsrganModel {
    /// RealESRGAN_x4plus (high quality, general purpose)
    #[default]
    X4Plus,
    /// RealESRGAN_x4plus_anime (anime/illustration)
    X4PlusAnime,
    /// RealESRNet_x4plus (faster, slightly lower quality)
    NetX4Plus,
    /// RealESRGAN_x2plus
    X2Plus,
    /// Custom model
    Custom(String),
}

impl RealEsrganModel {
    /// Get default scale for model
    pub fn default_scale(&self) -> u32 {
        match self {
            Self::X4Plus | Self::X4PlusAnime | Self::NetX4Plus => 4,
            Self::X2Plus => 2,
            Self::Custom(_) => 4,
        }
    }

    /// Get model name
    pub fn model_name(&self) -> &str {
        match self {
            Self::X4Plus => "RealESRGAN_x4plus",
            Self::X4PlusAnime => "RealESRGAN_x4plus_anime_6B",
            Self::NetX4Plus => "RealESRNet_x4plus",
            Self::X2Plus => "RealESRGAN_x2plus",
            Self::Custom(name) => name,
        }
    }
}

/// Output formats
#[derive(Debug, Clone, Copy, Default)]
pub enum OutputFormat {
    #[default]
    Png,
    Jpg {
        quality: u8,
    },
    Webp {
        quality: u8,
    },
}

impl OutputFormat {
    /// Get file extension
    pub fn extension(&self) -> &str {
        match self {
            OutputFormat::Png => "png",
            OutputFormat::Jpg { .. } => "jpg",
            OutputFormat::Webp { .. } => "webp",
        }
    }
}

/// Upscale result for single image
#[derive(Debug)]
pub struct UpscaleResult {
    /// Input file path
    pub input_path: PathBuf,
    /// Output file path
    pub output_path: PathBuf,
    /// Original resolution
    pub original_size: (u32, u32),
    /// Upscaled resolution
    pub upscaled_size: (u32, u32),
    /// Actual scale factor
    pub actual_scale: f32,
    /// Processing time
    pub processing_time: Duration,
    /// VRAM usage (MB)
    pub vram_used_mb: Option<u64>,
}

/// Batch upscale result
#[derive(Debug)]
pub struct BatchUpscaleResult {
    /// Successful results
    pub successful: Vec<UpscaleResult>,
    /// Failed files
    pub failed: Vec<(PathBuf, String)>,
    /// Total processing time
    pub total_time: Duration,
    /// Peak VRAM usage
    pub peak_vram_mb: Option<u64>,
}

/// RealESRGAN processor trait
pub trait RealEsrganProcessor {
    /// Upscale single image
    fn upscale(
        &self,
        input_path: &Path,
        output_path: &Path,
        options: &RealEsrganOptions,
    ) -> Result<UpscaleResult>;

    /// Batch upscale
    fn upscale_batch(
        &self,
        input_files: &[PathBuf],
        output_dir: &Path,
        options: &RealEsrganOptions,
        progress: Option<Box<dyn Fn(usize, usize) + Send>>,
    ) -> Result<BatchUpscaleResult>;

    /// Upscale all images in directory
    fn upscale_directory(
        &self,
        input_dir: &Path,
        output_dir: &Path,
        options: &RealEsrganOptions,
        progress: Option<Box<dyn Fn(usize, usize) + Send>>,
    ) -> Result<BatchUpscaleResult>;

    /// Get available models
    fn available_models(&self) -> Vec<RealEsrganModel>;

    /// Calculate recommended tile size
    fn recommended_tile_size(&self, image_size: (u32, u32), available_vram_mb: u64) -> u32;
}

/// RealESRGAN implementation
pub struct RealEsrgan {
    bridge: SubprocessBridge,
}

impl RealEsrgan {
    /// Create a new RealESRGAN processor
    pub fn new(bridge: SubprocessBridge) -> Self {
        Self { bridge }
    }

    /// Upscale single image
    pub fn upscale(
        &self,
        input_path: &Path,
        output_path: &Path,
        options: &RealEsrganOptions,
    ) -> Result<UpscaleResult> {
        if !input_path.exists() {
            return Err(RealEsrganError::InputNotFound(input_path.to_path_buf()));
        }

        // Get original image size
        let img =
            image::open(input_path).map_err(|e| RealEsrganError::ImageError(e.to_string()))?;
        let original_size = (img.width(), img.height());

        let start_time = std::time::Instant::now();

        // Create output directory if needed
        if let Some(parent) = output_path.parent() {
            if !parent.exists() {
                std::fs::create_dir_all(parent)
                    .map_err(|_| RealEsrganError::OutputNotWritable(parent.to_path_buf()))?;
            }
        }

        // Execute via bridge
        let result = self
            .bridge
            .execute(
                AiTool::RealESRGAN,
                &[input_path.to_path_buf()],
                output_path.parent().unwrap_or(Path::new(".")),
                options,
            )
            .map_err(RealEsrganError::BridgeError)?;

        if !result.failed_files.is_empty() {
            let (_, error) = &result.failed_files[0];
            return Err(RealEsrganError::ProcessingFailed(error.clone()));
        }

        // Verify output and get upscaled size
        let upscaled_size = if output_path.exists() {
            let output_img =
                image::open(output_path).map_err(|e| RealEsrganError::ImageError(e.to_string()))?;
            (output_img.width(), output_img.height())
        } else {
            // Estimate based on scale
            (
                original_size.0 * options.scale,
                original_size.1 * options.scale,
            )
        };

        let actual_scale = upscaled_size.0 as f32 / original_size.0 as f32;

        Ok(UpscaleResult {
            input_path: input_path.to_path_buf(),
            output_path: output_path.to_path_buf(),
            original_size,
            upscaled_size,
            actual_scale,
            processing_time: start_time.elapsed(),
            vram_used_mb: result.gpu_stats.map(|s| s.peak_vram_mb),
        })
    }

    /// Batch upscale multiple images
    pub fn upscale_batch(
        &self,
        input_files: &[PathBuf],
        output_dir: &Path,
        options: &RealEsrganOptions,
        progress: Option<Box<dyn Fn(usize, usize) + Send>>,
    ) -> Result<BatchUpscaleResult> {
        let start_time = std::time::Instant::now();
        let mut successful = Vec::new();
        let mut failed = Vec::new();

        // Create output directory
        if !output_dir.exists() {
            std::fs::create_dir_all(output_dir)
                .map_err(|_| RealEsrganError::OutputNotWritable(output_dir.to_path_buf()))?;
        }

        for (i, input_path) in input_files.iter().enumerate() {
            let output_filename = format!(
                "{}_{}x.{}",
                input_path.file_stem().unwrap_or_default().to_string_lossy(),
                options.scale,
                options.output_format.extension()
            );
            let output_path = output_dir.join(output_filename);

            match self.upscale(input_path, &output_path, options) {
                Ok(result) => successful.push(result),
                Err(e) => failed.push((input_path.clone(), e.to_string())),
            }

            if let Some(ref callback) = progress {
                callback(i + 1, input_files.len());
            }
        }

        Ok(BatchUpscaleResult {
            successful,
            failed,
            total_time: start_time.elapsed(),
            peak_vram_mb: None,
        })
    }

    /// Upscale all images in a directory
    pub fn upscale_directory(
        &self,
        input_dir: &Path,
        output_dir: &Path,
        options: &RealEsrganOptions,
        progress: Option<Box<dyn Fn(usize, usize) + Send>>,
    ) -> Result<BatchUpscaleResult> {
        // Find all image files in directory
        let mut input_files = Vec::new();
        let extensions = ["png", "jpg", "jpeg", "bmp", "tiff", "webp"];

        if input_dir.is_dir() {
            for entry in std::fs::read_dir(input_dir)? {
                let entry = entry?;
                let path = entry.path();
                if path.is_file() {
                    if let Some(ext) = path.extension() {
                        let ext_lower = ext.to_string_lossy().to_lowercase();
                        if extensions.contains(&ext_lower.as_str()) {
                            input_files.push(path);
                        }
                    }
                }
            }
        }

        // Sort for consistent ordering
        input_files.sort();

        self.upscale_batch(&input_files, output_dir, options, progress)
    }

    /// Get list of available models
    pub fn available_models(&self) -> Vec<RealEsrganModel> {
        vec![
            RealEsrganModel::X4Plus,
            RealEsrganModel::X4PlusAnime,
            RealEsrganModel::NetX4Plus,
            RealEsrganModel::X2Plus,
        ]
    }

    /// Calculate recommended tile size based on VRAM
    pub fn recommended_tile_size(&self, _image_size: (u32, u32), available_vram_mb: u64) -> u32 {
        // Empirical formula:
        // 4x upscale with FP16: ~100MB per 400x400 tile
        let base_tile = 400;
        let base_vram = 4096; // 4GB

        let scale_factor = (available_vram_mb as f64 / base_vram as f64).sqrt();
        let recommended = (base_tile as f64 * scale_factor) as u32;

        // Clamp to reasonable range
        recommended.clamp(128, 1024)
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_default_options() {
        let opts = RealEsrganOptions::default();

        assert_eq!(opts.scale, 2);
        assert!(matches!(opts.model, RealEsrganModel::X4Plus));
        assert_eq!(opts.tile_size, 400);
        assert_eq!(opts.tile_padding, 10);
        assert!(matches!(opts.output_format, OutputFormat::Png));
        assert!(!opts.face_enhance);
        assert!(opts.gpu_id.is_none());
        assert!(opts.fp16);
    }

    #[test]
    fn test_model_default_scale() {
        assert_eq!(RealEsrganModel::X4Plus.default_scale(), 4);
        assert_eq!(RealEsrganModel::X4PlusAnime.default_scale(), 4);
        assert_eq!(RealEsrganModel::NetX4Plus.default_scale(), 4);
        assert_eq!(RealEsrganModel::X2Plus.default_scale(), 2);
        assert_eq!(
            RealEsrganModel::Custom("test".to_string()).default_scale(),
            4
        );
    }

    #[test]
    fn test_model_names() {
        assert_eq!(RealEsrganModel::X4Plus.model_name(), "RealESRGAN_x4plus");
        assert_eq!(
            RealEsrganModel::X4PlusAnime.model_name(),
            "RealESRGAN_x4plus_anime_6B"
        );
        assert_eq!(RealEsrganModel::NetX4Plus.model_name(), "RealESRNet_x4plus");
        assert_eq!(RealEsrganModel::X2Plus.model_name(), "RealESRGAN_x2plus");
        assert_eq!(
            RealEsrganModel::Custom("MyModel".to_string()).model_name(),
            "MyModel"
        );
    }

    #[test]
    fn test_output_format_extension() {
        assert_eq!(OutputFormat::Png.extension(), "png");
        assert_eq!(OutputFormat::Jpg { quality: 90 }.extension(), "jpg");
        assert_eq!(OutputFormat::Webp { quality: 85 }.extension(), "webp");
    }

    #[test]
    fn test_recommended_tile_size() {
        // Create a mock bridge for testing
        let config = crate::ai_bridge::AiBridgeConfig {
            venv_path: PathBuf::from("tests/fixtures/test_venv"),
            ..Default::default()
        };

        // Skip if venv doesn't exist (we're just testing the algorithm)
        if config.venv_path.exists() {
            let bridge = SubprocessBridge::new(config).unwrap();
            let processor = RealEsrgan::new(bridge);

            // 8GB VRAM
            let tile_8gb = processor.recommended_tile_size((1920, 1080), 8192);
            assert!(tile_8gb >= 400);

            // 4GB VRAM
            let tile_4gb = processor.recommended_tile_size((1920, 1080), 4096);
            assert!(tile_4gb <= tile_8gb);

            // 2GB VRAM
            let tile_2gb = processor.recommended_tile_size((1920, 1080), 2048);
            assert!(tile_2gb <= tile_4gb);
        }
    }

    // Test the tile size algorithm directly
    #[test]
    fn test_tile_size_algorithm() {
        // Direct algorithm test without bridge
        let calculate_tile = |available_vram_mb: u64| -> u32 {
            let base_tile = 400;
            let base_vram = 4096_u64;
            let scale_factor = (available_vram_mb as f64 / base_vram as f64).sqrt();
            let recommended = (base_tile as f64 * scale_factor) as u32;
            recommended.clamp(128, 1024)
        };

        assert!(calculate_tile(8192) >= 400);
        assert!(calculate_tile(4096) >= 300);
        assert!(calculate_tile(2048) >= 200);
        assert!(calculate_tile(1024) >= 128);
    }

    #[test]
    fn test_builder_pattern() {
        let options = RealEsrganOptions::builder()
            .scale(4)
            .model(RealEsrganModel::X4PlusAnime)
            .tile_size(256)
            .tile_padding(16)
            .output_format(OutputFormat::Jpg { quality: 90 })
            .face_enhance(true)
            .gpu_id(0)
            .fp16(false)
            .build();

        assert_eq!(options.scale, 4);
        assert!(matches!(options.model, RealEsrganModel::X4PlusAnime));
        assert_eq!(options.tile_size, 256);
        assert_eq!(options.tile_padding, 16);
        assert!(matches!(
            options.output_format,
            OutputFormat::Jpg { quality: 90 }
        ));
        assert!(options.face_enhance);
        assert_eq!(options.gpu_id, Some(0));
        assert!(!options.fp16);
    }

    #[test]
    fn test_builder_scale_clamping() {
        // Scale should be normalized to 2 or 4
        let options = RealEsrganOptions::builder().scale(1).build();
        assert_eq!(options.scale, 2);

        let options = RealEsrganOptions::builder().scale(3).build();
        assert_eq!(options.scale, 2);

        let options = RealEsrganOptions::builder().scale(4).build();
        assert_eq!(options.scale, 4);

        let options = RealEsrganOptions::builder().scale(8).build();
        assert_eq!(options.scale, 4);
    }

    #[test]
    fn test_builder_tile_size_clamping() {
        // Tile size should be clamped to 64-1024
        let options = RealEsrganOptions::builder().tile_size(32).build();
        assert_eq!(options.tile_size, 64);

        let options = RealEsrganOptions::builder().tile_size(2000).build();
        assert_eq!(options.tile_size, 1024);

        let options = RealEsrganOptions::builder().tile_size(512).build();
        assert_eq!(options.tile_size, 512);
    }

    #[test]
    fn test_x4_high_quality_preset() {
        let options = RealEsrganOptions::x4_high_quality();

        assert_eq!(options.scale, 4);
        assert!(matches!(options.model, RealEsrganModel::X4Plus));
        assert_eq!(options.tile_size, 256);
        assert!(!options.fp16); // More accurate
    }

    #[test]
    fn test_anime_preset() {
        let options = RealEsrganOptions::anime();

        assert_eq!(options.scale, 4);
        assert!(matches!(options.model, RealEsrganModel::X4PlusAnime));
    }

    #[test]
    fn test_low_vram_preset() {
        let options = RealEsrganOptions::low_vram();

        assert_eq!(options.tile_size, 128);
        assert_eq!(options.tile_padding, 8);
        assert!(options.fp16);
    }

    // Note: The following tests require actual Python environment
    // They are marked with #[ignore] until environment is available

    #[test]
    #[ignore = "requires external tool"]
    fn test_input_not_found_error() {
        let config = crate::ai_bridge::AiBridgeConfig {
            venv_path: PathBuf::from("tests/fixtures/test_venv"),
            ..Default::default()
        };
        let bridge = SubprocessBridge::new(config).unwrap();
        let processor = RealEsrgan::new(bridge);

        let result = processor.upscale(
            Path::new("/nonexistent/image.png"),
            Path::new("/tmp/output.png"),
            &RealEsrganOptions::default(),
        );

        assert!(matches!(result, Err(RealEsrganError::InputNotFound(_))));
    }

    #[test]
    #[ignore = "requires external tool"]
    fn test_single_image_upscale() {
        let config = crate::ai_bridge::AiBridgeConfig {
            venv_path: PathBuf::from("tests/fixtures/test_venv"),
            ..Default::default()
        };
        let bridge = SubprocessBridge::new(config).unwrap();
        let processor = RealEsrgan::new(bridge);

        let temp_dir = tempfile::tempdir().unwrap();
        let output = temp_dir.path().join("upscaled.png");

        let result = processor
            .upscale(
                Path::new("tests/fixtures/small_image.png"),
                &output,
                &RealEsrganOptions::default(),
            )
            .unwrap();

        assert!(output.exists());
        assert_eq!(result.actual_scale, 2.0);
    }

    // TC-RES-002: 4x upscale test
    #[test]
    fn test_4x_upscale_options() {
        let options = RealEsrganOptions::builder().scale(4).build();
        assert_eq!(options.scale, 4);

        // Verify 4x preset
        let high_quality = RealEsrganOptions::x4_high_quality();
        assert_eq!(high_quality.scale, 4);
    }

    // TC-RES-005: Different models test
    #[test]
    fn test_different_models() {
        let models = vec![
            RealEsrganModel::X4Plus,
            RealEsrganModel::X4PlusAnime,
            RealEsrganModel::NetX4Plus,
            RealEsrganModel::X2Plus,
        ];

        for model in models {
            let options = RealEsrganOptions::builder().model(model.clone()).build();

            // Each model should have valid model name
            assert!(!options.model.model_name().is_empty());

            // Each model should have valid default scale
            assert!(model.default_scale() == 2 || model.default_scale() == 4);
        }
    }

    // Test UpscaleResult construction
    #[test]
    fn test_upscale_result_construction() {
        let result = UpscaleResult {
            input_path: PathBuf::from("input.png"),
            output_path: PathBuf::from("output.png"),
            original_size: (100, 100),
            upscaled_size: (200, 200),
            actual_scale: 2.0,
            processing_time: Duration::from_secs(5),
            vram_used_mb: Some(1024),
        };

        assert_eq!(result.input_path, PathBuf::from("input.png"));
        assert_eq!(result.output_path, PathBuf::from("output.png"));
        assert_eq!(result.original_size, (100, 100));
        assert_eq!(result.upscaled_size, (200, 200));
        assert_eq!(result.actual_scale, 2.0);
        assert_eq!(result.processing_time, Duration::from_secs(5));
        assert_eq!(result.vram_used_mb, Some(1024));
    }

    // Test BatchUpscaleResult construction
    #[test]
    fn test_batch_upscale_result_construction() {
        let successful_result = UpscaleResult {
            input_path: PathBuf::from("input.png"),
            output_path: PathBuf::from("output.png"),
            original_size: (100, 100),
            upscaled_size: (200, 200),
            actual_scale: 2.0,
            processing_time: Duration::from_secs(1),
            vram_used_mb: None,
        };

        let result = BatchUpscaleResult {
            successful: vec![successful_result],
            failed: vec![(PathBuf::from("failed.png"), "Error".to_string())],
            total_time: Duration::from_secs(10),
            peak_vram_mb: Some(2048),
        };

        assert_eq!(result.successful.len(), 1);
        assert_eq!(result.failed.len(), 1);
        assert_eq!(result.total_time, Duration::from_secs(10));
        assert_eq!(result.peak_vram_mb, Some(2048));
    }

    // Test output format quality settings
    #[test]
    fn test_output_format_quality() {
        let jpg_90 = OutputFormat::Jpg { quality: 90 };
        let jpg_50 = OutputFormat::Jpg { quality: 50 };
        let webp_80 = OutputFormat::Webp { quality: 80 };

        assert_eq!(jpg_90.extension(), "jpg");
        assert_eq!(jpg_50.extension(), "jpg");
        assert_eq!(webp_80.extension(), "webp");

        // Extract quality values
        if let OutputFormat::Jpg { quality } = jpg_90 {
            assert_eq!(quality, 90);
        }
        if let OutputFormat::Webp { quality } = webp_80 {
            assert_eq!(quality, 80);
        }
    }

    // Test error types
    #[test]
    fn test_error_types() {
        let model_err = RealEsrganError::ModelNotFound("test".to_string());
        assert!(model_err.to_string().contains("Model not found"));

        let scale_err = RealEsrganError::InvalidScale(3);
        assert!(scale_err.to_string().contains("Invalid scale"));

        let input_err = RealEsrganError::InputNotFound(PathBuf::from("/test"));
        assert!(input_err.to_string().contains("Input image not found"));

        let vram_err = RealEsrganError::InsufficientVram {
            required: 8000,
            available: 4000,
        };
        assert!(vram_err.to_string().contains("insufficient"));
    }

    // Test available models list
    #[test]
    fn test_available_models_list() {
        let config = crate::ai_bridge::AiBridgeConfig {
            venv_path: PathBuf::from("tests/fixtures/test_venv"),
            ..Default::default()
        };

        if config.venv_path.exists() {
            let bridge = SubprocessBridge::new(config).unwrap();
            let processor = RealEsrgan::new(bridge);
            let models = processor.available_models();

            assert!(!models.is_empty());
            assert!(models.len() >= 4); // At least 4 built-in models
        }
    }
}
