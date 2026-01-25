# 05-deskew.spec.md - Deskew (Skew Correction) Specification

## Overview

スキャン画像の傾き検出と補正を行うモジュール。
OpenCV（imageproc/opencv-rust）またはHough変換アルゴリズムを使用。

---

## Responsibilities

1. 画像の傾き角度を検出
2. 傾き補正の実行
3. 補正後の余白処理
4. バッチ処理での並列化

---

## Data Structures

```rust
use std::path::Path;

/// 傾き検出オプション
#[derive(Debug, Clone)]
pub struct DeskewOptions {
    /// 検出アルゴリズム
    pub algorithm: DeskewAlgorithm,
    /// 最大検出角度（度）
    pub max_angle: f64,
    /// 補正閾値（この角度以下は無視）
    pub threshold_angle: f64,
    /// 背景色（補正後の余白）
    pub background_color: [u8; 3],
    /// 画質維持モード
    pub quality_mode: QualityMode,
}

#[derive(Debug, Clone, Copy, Default)]
pub enum DeskewAlgorithm {
    /// Hough変換による線検出
    #[default]
    HoughLines,
    /// 投影プロファイル法
    ProjectionProfile,
    /// テキストライン検出
    TextLineDetection,
    /// 組み合わせ（複数手法の平均）
    Combined,
    /// ページ境界検出（スキャン書籍用）
    /// Sobel勾配フィルターでページ影を検出
    PageEdge,
}

#[derive(Debug, Clone, Copy, Default)]
pub enum QualityMode {
    /// 高速（バイリニア補間）
    Fast,
    /// 標準（バイキュービック補間）
    #[default]
    Standard,
    /// 高品質（Lanczos補間）
    HighQuality,
}

/// 傾き検出結果
#[derive(Debug, Clone)]
pub struct SkewDetection {
    /// 検出された角度（度、時計回りが正）
    pub angle: f64,
    /// 検出の信頼度 (0.0-1.0)
    pub confidence: f64,
    /// 検出に使用された特徴点数
    pub feature_count: usize,
}

/// 傾き補正結果
#[derive(Debug)]
pub struct DeskewResult {
    /// 元の検出結果
    pub detection: SkewDetection,
    /// 補正が実行されたか
    pub corrected: bool,
    /// 出力画像パス
    pub output_path: PathBuf,
    /// 元の画像サイズ
    pub original_size: (u32, u32),
    /// 補正後の画像サイズ
    pub corrected_size: (u32, u32),
}

/// エラー型
#[derive(Debug, thiserror::Error)]
pub enum DeskewError {
    #[error("Image not found: {0}")]
    ImageNotFound(PathBuf),

    #[error("Invalid image format: {0}")]
    InvalidFormat(String),

    #[error("Detection failed: {0}")]
    DetectionFailed(String),

    #[error("Correction failed: {0}")]
    CorrectionFailed(String),

    #[error("IO error: {0}")]
    IoError(#[from] std::io::Error),
}

pub type Result<T> = std::result::Result<T, DeskewError>;
```

---

## Public API

```rust
/// Deskewer トレイト
pub trait Deskewer {
    /// 傾きを検出
    fn detect_skew(image_path: &Path, options: &DeskewOptions) -> Result<SkewDetection>;

    /// 傾きを補正
    fn correct_skew(
        input_path: &Path,
        output_path: &Path,
        options: &DeskewOptions,
    ) -> Result<DeskewResult>;

    /// 検出と補正を一括実行
    fn deskew(
        input_path: &Path,
        output_path: &Path,
        options: &DeskewOptions,
    ) -> Result<DeskewResult>;

    /// バッチ処理
    fn deskew_batch(
        images: &[(PathBuf, PathBuf)],
        options: &DeskewOptions,
    ) -> Vec<Result<DeskewResult>>;
}

/// OpenCV/imageproc実装
pub struct ImageProcDeskewer;

impl Default for DeskewOptions {
    fn default() -> Self {
        Self {
            algorithm: DeskewAlgorithm::HoughLines,
            max_angle: 15.0,
            threshold_angle: 0.1,
            background_color: [255, 255, 255],
            quality_mode: QualityMode::Standard,
        }
    }
}
```

---

## Test Cases

### TC-DSK-001: 傾き検出（正の角度）

```rust
#[test]
fn test_detect_positive_skew() {
    // 5度時計回りに傾いた画像
    let detection = ImageProcDeskewer::detect_skew(
        Path::new("tests/fixtures/skewed_5deg.png"),
        &DeskewOptions::default(),
    ).unwrap();

    // 許容誤差 ±0.5度
    assert!((detection.angle - 5.0).abs() < 0.5);
    assert!(detection.confidence > 0.7);
}
```

### TC-DSK-002: 傾き検出（負の角度）

```rust
#[test]
fn test_detect_negative_skew() {
    // -3度（反時計回り）に傾いた画像
    let detection = ImageProcDeskewer::detect_skew(
        Path::new("tests/fixtures/skewed_neg3deg.png"),
        &DeskewOptions::default(),
    ).unwrap();

    assert!((detection.angle - (-3.0)).abs() < 0.5);
}
```

### TC-DSK-003: 傾きなし画像

```rust
#[test]
fn test_detect_no_skew() {
    let detection = ImageProcDeskewer::detect_skew(
        Path::new("tests/fixtures/straight.png"),
        &DeskewOptions::default(),
    ).unwrap();

    // 傾きが0.1度以下
    assert!(detection.angle.abs() < 0.1);
}
```

### TC-DSK-004: 傾き補正実行

```rust
#[test]
fn test_correct_skew() {
    let temp_dir = tempfile::tempdir().unwrap();
    let output = temp_dir.path().join("corrected.png");

    let result = ImageProcDeskewer::correct_skew(
        Path::new("tests/fixtures/skewed_5deg.png"),
        &output,
        &DeskewOptions::default(),
    ).unwrap();

    assert!(result.corrected);
    assert!(output.exists());

    // 補正後の画像の傾きを再検出
    let recheck = ImageProcDeskewer::detect_skew(&output, &DeskewOptions::default()).unwrap();
    assert!(recheck.angle.abs() < 0.5);
}
```

### TC-DSK-005: 閾値以下は補正しない

```rust
#[test]
fn test_threshold_skip() {
    let temp_dir = tempfile::tempdir().unwrap();
    let output = temp_dir.path().join("output.png");

    // 0.05度の傾き（閾値0.1度以下）
    let result = ImageProcDeskewer::correct_skew(
        Path::new("tests/fixtures/skewed_005deg.png"),
        &output,
        &DeskewOptions {
            threshold_angle: 0.1,
            ..Default::default()
        },
    ).unwrap();

    assert!(!result.corrected);
}
```

### TC-DSK-006: 最大角度制限

```rust
#[test]
fn test_max_angle_limit() {
    // 20度傾いた画像（最大15度設定）
    let detection = ImageProcDeskewer::detect_skew(
        Path::new("tests/fixtures/skewed_20deg.png"),
        &DeskewOptions {
            max_angle: 15.0,
            ..Default::default()
        },
    ).unwrap();

    // 信頼度が低いか、検出角度が制限される
    assert!(detection.confidence < 0.5 || detection.angle.abs() <= 15.0);
}
```

### TC-DSK-007: 背景色設定

```rust
#[test]
fn test_background_color() {
    let temp_dir = tempfile::tempdir().unwrap();

    // 黒背景で補正
    let output_black = temp_dir.path().join("black_bg.png");
    ImageProcDeskewer::correct_skew(
        Path::new("tests/fixtures/skewed_5deg.png"),
        &output_black,
        &DeskewOptions {
            background_color: [0, 0, 0],
            ..Default::default()
        },
    ).unwrap();

    // 白背景で補正
    let output_white = temp_dir.path().join("white_bg.png");
    ImageProcDeskewer::correct_skew(
        Path::new("tests/fixtures/skewed_5deg.png"),
        &output_white,
        &DeskewOptions {
            background_color: [255, 255, 255],
            ..Default::default()
        },
    ).unwrap();

    // 隅のピクセルを比較
    let img_black = image::open(&output_black).unwrap().to_rgb8();
    let img_white = image::open(&output_white).unwrap().to_rgb8();

    // 隅のピクセルが設定した背景色になっている
    let corner_black = img_black.get_pixel(0, 0);
    let corner_white = img_white.get_pixel(0, 0);

    assert_eq!(corner_black.0, [0, 0, 0]);
    assert_eq!(corner_white.0, [255, 255, 255]);
}
```

### TC-DSK-008: 品質モード比較

```rust
#[test]
fn test_quality_modes() {
    let temp_dir = tempfile::tempdir().unwrap();
    let input = Path::new("tests/fixtures/skewed_5deg.png");

    for mode in [QualityMode::Fast, QualityMode::Standard, QualityMode::HighQuality] {
        let output = temp_dir.path().join(format!("{:?}.png", mode));
        ImageProcDeskewer::correct_skew(
            input,
            &output,
            &DeskewOptions {
                quality_mode: mode,
                ..Default::default()
            },
        ).unwrap();

        assert!(output.exists());
    }
}
```

### TC-DSK-009: バッチ処理

```rust
#[test]
fn test_batch_deskew() {
    let temp_dir = tempfile::tempdir().unwrap();

    let images: Vec<_> = (1..=5)
        .map(|i| {
            let input = PathBuf::from(format!("tests/fixtures/skewed_{}.png", i));
            let output = temp_dir.path().join(format!("corrected_{}.png", i));
            (input, output)
        })
        .collect();

    let results = ImageProcDeskewer::deskew_batch(&images, &DeskewOptions::default());

    assert_eq!(results.len(), 5);
    assert!(results.iter().all(|r| r.is_ok()));
}
```

### TC-DSK-010: 異なるアルゴリズム

```rust
#[test]
fn test_different_algorithms() {
    let input = Path::new("tests/fixtures/document_page.png");

    for algorithm in [
        DeskewAlgorithm::HoughLines,
        DeskewAlgorithm::ProjectionProfile,
        DeskewAlgorithm::TextLineDetection,
        DeskewAlgorithm::Combined,
        DeskewAlgorithm::PageEdge,
    ] {
        let detection = ImageProcDeskewer::detect_skew(
            input,
            &DeskewOptions {
                algorithm,
                ..Default::default()
            },
        ).unwrap();

        // 各アルゴリズムで妥当な結果が得られる
        assert!(detection.angle.abs() < 15.0);
    }
}
```

### TC-DSK-011: PageEdgeアルゴリズム（スキャン書籍）

```rust
#[test]
fn test_page_edge_algorithm() {
    // スキャン書籍画像（ページ境界の影がある）
    let input = Path::new("tests/fixtures/scanned_book_page.png");

    let detection = ImageProcDeskewer::detect_skew(
        input,
        &DeskewOptions {
            algorithm: DeskewAlgorithm::PageEdge,
            ..Default::default()
        },
    ).unwrap();

    // PageEdgeはページ境界の傾きを検出
    // テキスト行ではなくページの物理的な傾きを検出
    assert!(detection.confidence > 0.5);
    assert!(detection.feature_count > 100);  // 十分な境界点を検出
}
```

### TC-DSK-012: PageEdge vs HoughLines比較

```rust
#[test]
fn test_page_edge_vs_hough() {
    // ページ境界が傾いているがテキスト行は水平な画像
    let input = Path::new("tests/fixtures/tilted_page_horizontal_text.png");

    let hough_detection = ImageProcDeskewer::detect_skew(
        input,
        &DeskewOptions {
            algorithm: DeskewAlgorithm::HoughLines,
            ..Default::default()
        },
    ).unwrap();

    let page_edge_detection = ImageProcDeskewer::detect_skew(
        input,
        &DeskewOptions {
            algorithm: DeskewAlgorithm::PageEdge,
            ..Default::default()
        },
    ).unwrap();

    // HoughLinesはテキスト行を検出（0度に近い）
    // PageEdgeはページ境界を検出（傾きがある）
    // 両者の結果が異なることを確認
    assert!((hough_detection.angle - page_edge_detection.angle).abs() > 0.5);
}
```

---

## Implementation Notes

### Hough変換による実装

```rust
use imageproc::hough::*;
use image::{DynamicImage, GrayImage};

impl ImageProcDeskewer {
    pub fn detect_skew(image_path: &Path, options: &DeskewOptions) -> Result<SkewDetection> {
        let img = image::open(image_path)
            .map_err(|e| DeskewError::InvalidFormat(e.to_string()))?;

        let gray = img.to_luma8();

        // エッジ検出
        let edges = imageproc::edges::canny(&gray, 50.0, 150.0);

        // Hough変換
        let lines = detect_lines(&edges, LineDetectionOptions::default());

        // 線の角度から傾きを算出
        let angles: Vec<f64> = lines
            .iter()
            .filter(|line| {
                let angle = (line.angle as f64 - 90.0).abs();
                angle < options.max_angle
            })
            .map(|line| line.angle as f64 - 90.0)
            .collect();

        if angles.is_empty() {
            return Ok(SkewDetection {
                angle: 0.0,
                confidence: 0.0,
                feature_count: 0,
            });
        }

        // 中央値を使用（外れ値に強い）
        let median_angle = Self::median(&angles);
        let confidence = 1.0 - (Self::std_dev(&angles, median_angle) / options.max_angle).min(1.0);

        Ok(SkewDetection {
            angle: median_angle,
            confidence,
            feature_count: angles.len(),
        })
    }

    pub fn correct_skew(
        input_path: &Path,
        output_path: &Path,
        options: &DeskewOptions,
    ) -> Result<DeskewResult> {
        let detection = Self::detect_skew(input_path, options)?;

        if detection.angle.abs() < options.threshold_angle {
            // 傾きが閾値以下なら補正しない（コピーのみ）
            std::fs::copy(input_path, output_path)?;
            return Ok(DeskewResult {
                detection,
                corrected: false,
                output_path: output_path.to_path_buf(),
                original_size: todo!(),
                corrected_size: todo!(),
            });
        }

        let img = image::open(input_path)?;
        let (w, h) = (img.width(), img.height());

        // 回転補正
        let rotated = imageproc::geometric_transformations::rotate_about_center(
            &img.to_rgba8(),
            -detection.angle.to_radians() as f32,
            imageproc::geometric_transformations::Interpolation::Bicubic,
            image::Rgba(options.background_color.map(|c| c).chain([255]).collect::<Vec<_>>().try_into().unwrap()),
        );

        rotated.save(output_path)?;

        Ok(DeskewResult {
            detection,
            corrected: true,
            output_path: output_path.to_path_buf(),
            original_size: (w, h),
            corrected_size: (rotated.width(), rotated.height()),
        })
    }

    fn median(values: &[f64]) -> f64 {
        let mut sorted = values.to_vec();
        sorted.sort_by(|a, b| a.partial_cmp(b).unwrap());
        sorted[sorted.len() / 2]
    }

    fn std_dev(values: &[f64], mean: f64) -> f64 {
        let variance: f64 = values.iter().map(|v| (v - mean).powi(2)).sum::<f64>() / values.len() as f64;
        variance.sqrt()
    }
}
```

### PageEdgeアルゴリズムによる実装

スキャン書籍のページ境界の影を検出して傾きを計算する。
テキスト行ではなく、物理的なページの傾きを検出する。

```rust
impl ImageProcDeskewer {
    /// PageEdgeアルゴリズム: ページ境界の傾きを検出
    ///
    /// # アルゴリズム
    /// 1. 3ピクセルSobel風水平勾配フィルターを適用
    /// 2. 各行で最初の有意な負勾配（白→灰色遷移）を検出
    /// 3. 中央値ベースで外れ値を除去
    /// 4. インライア点に対して線形回帰で角度計算
    /// 5. 垂直からの偏差を傾き角度として返却
    pub fn detect_skew_page_edge(gray: &GrayImage, options: &DeskewOptions) -> Result<SkewDetection> {
        let (width, height) = gray.dimensions();
        let search_width = width / 2;  // 左半分のみ検索
        let gradient_threshold: i32 = -5;

        let mut boundary_points: Vec<(f64, f64)> = Vec::new();

        // 各行でSobel勾配を計算し、境界点を検出
        for y in 0..height {
            for x in 2..search_width {
                let left = gray.get_pixel(x - 2, y).0[0] as i32;
                let right = gray.get_pixel(x, y).0[0] as i32;
                let gradient = right - left;

                if gradient < gradient_threshold {
                    boundary_points.push((x as f64, y as f64));
                    break;
                }
            }
        }

        // 中央値で外れ値フィルタリング
        let median_x = Self::median(&boundary_points.iter().map(|(x, _)| *x).collect::<Vec<_>>());
        let inliers: Vec<(f64, f64)> = boundary_points
            .into_iter()
            .filter(|(x, _)| (*x - median_x).abs() < 50.0)
            .collect();

        // 線形回帰で角度計算
        let angle = Self::fit_line_angle(&inliers)?;
        let confidence = (inliers.len() as f64 / height as f64).min(1.0);

        Ok(SkewDetection {
            angle: angle.clamp(-options.max_angle, options.max_angle),
            confidence,
            feature_count: inliers.len(),
        })
    }

    /// 点群に線をフィットして垂直からの角度を返す
    fn fit_line_angle(points: &[(f64, f64)]) -> Result<f64> {
        if points.len() < 10 {
            return Ok(0.0);
        }

        // x = m * y + b の形式で回帰
        let n = points.len() as f64;
        let sum_x: f64 = points.iter().map(|(x, _)| x).sum();
        let sum_y: f64 = points.iter().map(|(_, y)| y).sum();
        let sum_xy: f64 = points.iter().map(|(x, y)| x * y).sum();
        let sum_yy: f64 = points.iter().map(|(_, y)| y * y).sum();

        let denominator = n * sum_yy - sum_y * sum_y;
        if denominator.abs() < 1e-10 {
            return Ok(0.0);
        }

        let slope = (n * sum_xy - sum_x * sum_y) / denominator;
        Ok(slope.atan().to_degrees())
    }
}
```

---

## Acceptance Criteria

- [ ] 正の傾き角度を正確に検出できる（誤差±0.5度以内）
- [ ] 負の傾き角度を正確に検出できる
- [ ] 傾きなし画像で0度近くを返す
- [ ] 傾き補正が正しく実行される
- [ ] 閾値以下の傾きは補正をスキップする
- [ ] 最大角度制限が機能する
- [ ] 背景色が正しく設定される
- [ ] 各品質モードで正常に動作する
- [ ] バッチ処理が正しく動作する
- [ ] 異なるアルゴリズムで妥当な結果が得られる
- [ ] PageEdgeアルゴリズムがスキャン書籍ページで正しく動作する
- [ ] PageEdgeアルゴリズムがページ境界の傾きを検出できる

---

## Dependencies

```toml
[dependencies]
image = "0.25"
imageproc = "0.25"
thiserror = "2"
rayon = "1.10"

[dev-dependencies]
tempfile = "3"
```
