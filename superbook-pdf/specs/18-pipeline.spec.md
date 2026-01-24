# 18-pipeline.spec.md - パイプライン処理モジュール仕様

## 概要

PDF処理パイプラインを独立モジュールとして分離。main.rsから処理ロジックを抽出し、テスト可能・再利用可能な構造にする。

## 目的

- main.rsのサイズ削減（1,234行→300行程度）
- パイプライン処理のテスタビリティ向上
- 処理結果メタデータの正確な返却
- 将来的なWebインターフェース統合の準備

## 設計

### PipelineConfig (構造体)

パイプライン処理設定。CLIオプションから生成。

```rust
pub struct PipelineConfig {
    /// Output DPI
    pub dpi: u32,
    /// Enable deskew
    pub deskew: bool,
    /// Margin trim percentage
    pub margin_trim: f64,
    /// Enable AI upscaling
    pub upscale: bool,
    /// Enable GPU
    pub gpu: bool,
    /// Enable internal resolution normalization
    pub internal_resolution: bool,
    /// Enable color correction
    pub color_correction: bool,
    /// Enable offset alignment
    pub offset_alignment: bool,
    /// Output height
    pub output_height: u32,
    /// Enable OCR
    pub ocr: bool,
    /// Max pages for debug
    pub max_pages: Option<usize>,
    /// Save debug images
    pub save_debug: bool,
    /// JPEG quality
    pub jpeg_quality: u8,
    /// Thread count
    pub threads: Option<usize>,
}
```

### PipelineResult (構造体)

処理結果メタデータ。

```rust
pub struct PipelineResult {
    /// Number of pages processed
    pub page_count: usize,
    /// Detected page number shift
    pub page_number_shift: Option<i32>,
    /// Whether vertical text was detected
    pub is_vertical: bool,
    /// Processing time in seconds
    pub elapsed_seconds: f64,
    /// Output file path
    pub output_path: PathBuf,
    /// Output file size
    pub output_size: u64,
}
```

### PdfPipeline (構造体)

パイプライン処理の実行者。

```rust
pub struct PdfPipeline {
    config: PipelineConfig,
}

impl PdfPipeline {
    pub fn new(config: PipelineConfig) -> Self;
    pub fn process(&self, input: &Path, output_dir: &Path) -> Result<PipelineResult, PipelineError>;
}
```

## API

### 変換

| 関数 | 説明 |
|------|------|
| `PipelineConfig::from_convert_args(args)` | CLIオプションから設定生成 |
| `PipelineConfig::to_json()` | キャッシュ用JSON生成 |
| `PdfPipeline::new(config)` | パイプライン作成 |
| `PdfPipeline::process(input, output_dir)` | PDF処理実行 |

### 処理ステップ

1. PDF読み込み・メタデータ抽出
2. 画像抽出
3. 傾き補正 (Deskew)
4. マージントリミング
5. AI超解像 (RealESRGAN)
6. 内部解像度正規化
7. 色統計分析・グローバル色補正
8. Tukey fenceグループクロップ
9. ページ番号オフセット計算
10. 最終出力リサイズ
11. 縦書き検出
12. YomiToku OCR
13. PDF生成

## テストケース

| TC ID | テスト内容 |
|-------|----------|
| PIPE-001 | PipelineConfig::from_convert_args |
| PIPE-002 | PipelineConfig::to_json |
| PIPE-003 | PipelineConfig::default |
| PIPE-004 | PipelineResult作成 |
| PIPE-005 | PdfPipeline::new |
| PIPE-006 | 処理ステップ順序確認 |

## 実装ステータス

| 機能 | 状態 | 備考 |
|------|------|------|
| PipelineConfig | 🟢 | 実装完了 (ビルダーパターン含む) |
| PipelineResult | 🟢 | 実装完了 (to_cache_result含む) |
| PdfPipeline | 🟡 | スタブ実装 (ページ数取得のみ) |
| main.rs分離 | 🔴 | 未実装 (段階的移行予定) |
