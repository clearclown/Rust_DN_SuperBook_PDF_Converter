# 19-config.spec.md - 設定ファイルモジュール仕様

## 概要

TOML形式の設定ファイル対応。CLIオプションを設定ファイルから読み込み可能にする。

## 目的

- 頻繁に使用するオプションの永続化
- プロジェクト単位の設定管理
- CLIオプションと設定ファイルのマージ

## 設計

### 設定ファイル検索順序

1. `--config <path>` で指定されたファイル
2. カレントディレクトリの `superbook.toml`
3. `~/.config/superbook-pdf/config.toml`
4. デフォルト値

### 設定ファイル形式 (TOML)

```toml
# superbook.toml

[general]
dpi = 300
threads = 4
verbose = 1

[processing]
deskew = true
margin_trim = 0.5
upscale = true
gpu = true

[advanced]
internal_resolution = false
color_correction = false
offset_alignment = false
output_height = 3508

[ocr]
enabled = false
language = "ja"

[output]
jpeg_quality = 90
skip_existing = false
```

### Config (構造体)

```rust
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct Config {
    pub general: GeneralConfig,
    pub processing: ProcessingConfig,
    pub advanced: AdvancedConfig,
    pub ocr: OcrConfig,
    pub output: OutputConfig,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct GeneralConfig {
    pub dpi: Option<u32>,
    pub threads: Option<usize>,
    pub verbose: Option<u8>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ProcessingConfig {
    pub deskew: Option<bool>,
    pub margin_trim: Option<f64>,
    pub upscale: Option<bool>,
    pub gpu: Option<bool>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct AdvancedConfig {
    pub internal_resolution: Option<bool>,
    pub color_correction: Option<bool>,
    pub offset_alignment: Option<bool>,
    pub output_height: Option<u32>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct OcrConfig {
    pub enabled: Option<bool>,
    pub language: Option<String>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct OutputConfig {
    pub jpeg_quality: Option<u8>,
    pub skip_existing: Option<bool>,
}
```

## API

| 関数 | 説明 |
|------|------|
| `Config::load()` | 検索順序に従い設定ファイル読み込み |
| `Config::load_from_path(path)` | 指定パスから読み込み |
| `Config::default()` | デフォルト設定生成 |
| `Config::merge(cli_args)` | CLI引数とマージ (CLI優先) |
| `Config::to_pipeline_config()` | PipelineConfigに変換 |

## 優先順位

CLI引数 > 設定ファイル > デフォルト値

## テストケース

| TC ID | テスト内容 |
|-------|------------|
| CFG-001 | Config::default |
| CFG-002 | Config::load_from_path (存在するファイル) |
| CFG-003 | Config::load_from_path (存在しないファイル) |
| CFG-004 | Config::load (検索順序確認) |
| CFG-005 | Config::merge (CLI優先) |
| CFG-006 | Config::to_pipeline_config |
| CFG-007 | TOML パース (完全な設定) |
| CFG-008 | TOML パース (部分的な設定) |
| CFG-009 | TOML パース (空ファイル) |
| CFG-010 | TOML パース (不正な形式) |

## 実装ステータス

| 機能 | 状態 | 備考 |
|------|------|------|
| Config構造体 | 🟢 | 完了 |
| TOMLパース | 🟢 | 完了 |
| ファイル検索 | 🟢 | 完了 |
| CLI マージ | 🟢 | 完了 |
| PipelineConfig変換 | 🟢 | 完了 |
| テスト | 🟢 | 16件テストケース |

## 依存クレート

- `toml` - TOML パース
- `dirs` - ホームディレクトリ取得
