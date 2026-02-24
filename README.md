<p align="center">
  <b>🌐 Language / 言語</b><br>
  <b>日本語</b> |
  <a href="docs/readmeLangs/README.en.md">English</a> |
  <a href="docs/readmeLangs/README.zh-CN.md">简体中文</a> |
  <a href="docs/readmeLangs/README.zh-TW.md">繁體中文</a> |
  <a href="docs/readmeLangs/README.ru.md">Русский</a> |
  <a href="docs/readmeLangs/README.uk.md">Українська</a> |
  <a href="docs/readmeLangs/README.fa.md">فارسی</a> |
  <a href="docs/readmeLangs/README.ar.md">العربية</a>
</p>

# superbook-pdf

[![License: AGPL v3](https://img.shields.io/badge/License-AGPL%20v3-blue.svg)](https://www.gnu.org/licenses/agpl-3.0)
[![Rust](https://img.shields.io/badge/rust-1.82%2B-orange.svg)](https://www.rust-lang.org/)
[![CI](https://github.com/clearclown/Rust_DN_SuperBook_PDF_Converter/actions/workflows/ci.yml/badge.svg)](https://github.com/clearclown/Rust_DN_SuperBook_PDF_Converter/actions/workflows/ci.yml)

> **Fork of [dnobori/DN_SuperBook_PDF_Converter](https://github.com/dnobori/DN_SuperBook_PDF_Converter)**
>
> [フォーク元の素晴らしき芸術的なREADME.md](https://github.com/dnobori/DN_SuperBook_PDF_Converter/blob/master/README.md) : 正直、これを読めばすべてがわかる
>
> Rust で完全リライトしたスキャン書籍 PDF 高品質化ツール

**オリジナル著者:** 登 大遊 (Daiyuu Nobori) 様
**Rust リライト:** clearclown
**ライセンス:** AGPL v3.0

---

## Before / After

![Before and After comparison](doc_img/ba.png)

| | Before (左) | After (右) |
|---|---|---|
| **解像度** | 1242x2048 px | 2363x3508 px |
| **ファイルサイズ** | 981 KB | 1.6 MB |
| **品質** | ぼやけ、低コントラスト | 鮮明、高コントラスト |

RealESRGAN による AI 超解像で、文字のエッジが鮮明になり、読みやすさが大幅に向上します。

---

## 特徴

- **Rust 実装** - C# 版を完全リライト。メモリ効率とパフォーマンスが大幅に改善
- **AI 超解像** - RealESRGAN で画像を 2x 高解像度化
- **日本語 OCR** - YomiToku による高精度文字認識
- **Markdown 変換** - PDF から構造化された Markdown を生成 (図・表の自動検出付き)
- **傾き補正** - 大津二値化 + Hough 変換で自動補正
- **180度回転検出** - 上下逆のページを自動検出・補正
- **影除去** - 製本時の影を自動検出・除去
- **マーカー除去** - 蛍光ペンのハイライトを検出・除去
- **ブレ補正** - ぼやけた画像のシャープ化 (Unsharp Mask / NAFNet / DeblurGAN-v2)
- **カラー補正** - HSV 裏写り抑制、紙色の白化
- **Web UI** - ブラウザから直感的に操作可能

---

## クイックスタート

```bash
# ソースからビルド
git clone https://github.com/clearclown/Rust_DN_SuperBook_PDF_Converter.git
cd Rust_DN_SuperBook_PDF_Converter/superbook-pdf
cargo build --release --features web

# 基本変換
superbook-pdf convert input.pdf -o output/

# 高品質変換 (AI超解像 + カラー補正 + オフセット調整)
superbook-pdf convert input.pdf -o output/ --advanced --ocr

# Markdown 変換
superbook-pdf markdown input.pdf -o markdown_output/

# Web UI 起動
superbook-pdf serve --port 8080
```

---

## コマンド体系

superbook-pdf は 5 つのサブコマンドを提供します:

| コマンド | 説明 |
|---------|------|
| [`convert`](#convert---pdf-高品質化) | PDF を AI 強化して高品質 PDF に変換 |
| [`markdown`](#markdown---pdf-から-markdown-変換) | PDF から構造化された Markdown を生成 |
| [`reprocess`](#reprocess---失敗ページの再処理) | 変換に失敗したページを再処理 |
| `info` | システム環境情報を表示 (GPU、依存ツール等) |
| `cache-info` | 出力 PDF のキャッシュ情報を表示 |

### `convert` - PDF 高品質化

スキャンした PDF を AI 強化して高品質 PDF に変換します。

```bash
# 基本 (傾き補正 + マージントリム + AI超解像)
superbook-pdf convert input.pdf -o output/

# 最高品質 (全機能有効)
superbook-pdf convert input.pdf -o output/ --advanced --ocr

# 影除去 + マーカー除去 + ブレ補正
superbook-pdf convert input.pdf -o output/ --shadow-removal auto --remove-markers --deblur

# テスト (最初の5ページ、実行計画のみ)
superbook-pdf convert input.pdf -o output/ --max-pages 5 --dry-run
```

**主なオプション:**

| オプション | デフォルト | 説明 |
|-----------|-----------|------|
| `-o, --output <DIR>` | `./output` | 出力先ディレクトリ |
| `--advanced` | off | 高品質処理を有効化 (内部解像度正規化 + カラー補正 + オフセット調整) |
| `--ocr` | off | 日本語OCR を有効化 |
| `--dpi <N>` | 300 | 出力 DPI |
| `--jpeg-quality <N>` | 90 | PDF 内 JPEG 圧縮品質 (1-100) |
| `-m, --margin-trim <N>` | 0.7 | マージントリム率 (%) |
| `--shadow-removal <MODE>` | auto | 影除去モード (none/auto/left/right/both) |
| `--remove-markers` | off | 蛍光マーカー除去を有効化 |
| `--deblur` | off | ブレ補正を有効化 |
| `--no-upscale` | - | AI超解像をスキップ |
| `--no-deskew` | - | 傾き補正をスキップ |
| `--no-gpu` | - | GPU処理を無効化 |
| `--dry-run` | - | 実行計画を表示 (実処理なし) |
| `--max-pages <N>` | - | 処理ページ数を制限 |
| `-v, -vv, -vvv` | - | ログ詳細度 |

全オプションは `superbook-pdf convert --help` で確認できます。

### `markdown` - PDF から Markdown 変換

PDF を OCR し、構造化された Markdown に変換します。図の自動検出・抽出、表の検出、読み順序の自動判定に対応しています。

```bash
# 基本変換
superbook-pdf markdown input.pdf -o output/

# 縦書き指定 + AI超解像
superbook-pdf markdown input.pdf -o output/ --text-direction vertical --upscale

# 品質検証付き
superbook-pdf markdown input.pdf -o output/ --validate --api-provider claude

# 中断した処理を再開
superbook-pdf markdown input.pdf -o output/ --resume
```

**主なオプション:**

| オプション | デフォルト | 説明 |
|-----------|-----------|------|
| `-o, --output <DIR>` | `./markdown_output` | 出力先ディレクトリ |
| `--text-direction` | auto | テキスト方向 (auto/horizontal/vertical) |
| `--upscale` | off | OCR前にAI超解像を適用 |
| `--dpi <N>` | 300 | 出力DPI |
| `--figure-sensitivity <N>` | - | 図検出の感度 (0.0-1.0) |
| `--no-extract-images` | - | 画像抽出を無効化 |
| `--no-detect-tables` | - | 表検出を無効化 |
| `--include-page-numbers` | off | ページ番号を出力に含める |
| `--validate` | off | 出力Markdownの品質検証 |
| `--resume` | - | 中断した処理を再開 |

全オプションは `superbook-pdf markdown --help` で確認できます。

### `reprocess` - 失敗ページの再処理

変換中にエラーが発生したページだけを再処理します。

```bash
# 状態ファイルから自動検出して再処理
superbook-pdf reprocess output/.superbook-state.json

# 特定ページのみ再処理
superbook-pdf reprocess output/.superbook-state.json -p 5,12,30

# 状態確認のみ
superbook-pdf reprocess output/.superbook-state.json --status
```

---

## 処理パイプライン

`convert` コマンドの処理フロー:

```
入力PDF
  │
  ├─ Step 1: PDF画像抽出 (pdftoppm, 指定DPI)
  ├─ Step 2: マージントリム (デフォルト 0.7%)
  ├─ Step 3: 影除去 (--shadow-removal)
  ├─ Step 4: AI超解像 (RealESRGAN 2x)
  ├─ Step 5: ブレ補正 (--deblur)
  ├─ Step 6: 180度回転検出・補正
  ├─ Step 7: 傾き補正 (大津二値化 + Hough変換)
  ├─ Step 8: カラー補正 (HSV裏写り抑制)
  ├─ Step 9: マーカー除去 (--remove-markers)
  ├─ Step 10: グループクロップ (均一マージン)
  ├─ Step 11: PDF生成 (JPEG DCT圧縮)
  └─ Step 12: OCR (YomiToku, --ocr)
  │
  出力PDF
```

空白ページは自動検出 (閾値 2%) され、処理をスキップします。

---

## インストール

### 必要なもの

| 項目 | 要件 |
|------|------|
| OS | Linux / macOS / Windows |
| Rust | 1.82 以上 (ソースビルド時) |
| Poppler | `pdftoppm` コマンド |

AI機能を使う場合は、Python 3.10 以上と NVIDIA GPU (CUDA 11.8+) が必要です。

### 1. システム依存パッケージ

```bash
# Ubuntu/Debian
sudo apt update && sudo apt install -y poppler-utils python3 python3-venv

# Fedora
sudo dnf install -y poppler-utils python3

# macOS (Homebrew)
brew install poppler python

# Windows (Chocolatey)
choco install poppler python
```

### 2. superbook-pdf のインストール

```bash
# ソースからビルド
git clone https://github.com/clearclown/Rust_DN_SuperBook_PDF_Converter.git
cd Rust_DN_SuperBook_PDF_Converter/superbook-pdf
cargo build --release --features web
```

### 3. AI機能のセットアップ (任意)

> Docker/Podman を使う場合はこの手順は不要です。

```bash
cd superbook-pdf/ai_bridge

# Python 仮想環境を作成
python3 -m venv .venv
source .venv/bin/activate

# GPU版 PyTorch をインストール
pip install torch torchvision --index-url https://download.pytorch.org/whl/cu121

# AI依存パッケージをインストール
pip install -r requirements.txt
```

実行時に環境変数を設定:

```bash
export SUPERBOOK_VENV=/path/to/ai_bridge/.venv
```

### 4. Docker/Podman で実行 (推奨)

```bash
# NVIDIA GPU
docker compose up -d

# AMD GPU (ROCm)
docker compose -f docker-compose.yml -f docker-compose.rocm.yml up -d

# CPUのみ
docker compose -f docker-compose.yml -f docker-compose.cpu.yml up -d
```

ブラウザで http://localhost:8080 を開けば使えます。

---

## Web UI

![Web UI](doc_img/webUI.png)

ブラウザベースのインターフェースで、ファイルをドラッグ&ドロップするだけで変換が始まります。WebSocket によるリアルタイム進捗表示に対応しています。

```bash
superbook-pdf serve --port 8080 --bind 0.0.0.0
```

---

## 詳細ドキュメント

| ドキュメント | 内容 |
|-------------|------|
| [docs/pipeline.md](docs/pipeline.md) | 処理パイプラインの詳細設計 |
| [docs/commands.md](docs/commands.md) | 全コマンド・全オプションのリファレンス |
| [docs/configuration.md](docs/configuration.md) | 設定ファイル (TOML) によるカスタマイズ |
| [docs/docker.md](docs/docker.md) | Docker/Podman 環境の詳細ガイド |
| [docs/development.md](docs/development.md) | 開発者向けガイド (ビルド、テスト、アーキテクチャ) |

---

## トラブルシューティング

| 問題 | 解決策 |
|------|--------|
| `pdftoppm: command not found` | `sudo apt install poppler-utils` |
| RealESRGAN が動かない | `SUPERBOOK_VENV` 環境変数を設定 |
| GPU が使用されない | `pip install torch --index-url https://download.pytorch.org/whl/cu121` |
| メモリ不足 | `--max-pages 10` か `--chunk-size 5` で分割処理 |
| 傾き補正で画像が崩れる | `--no-deskew` で無効化 |
| マージンで文字が切れる | `--margin-safety 1.0` で安全バッファを増加 |

---

## ライセンス

AGPL v3.0 - [LICENSE](LICENSE)

---

## 謝辞

- **登 大遊 (Daiyuu Nobori) 様** - オリジナル実装
- **[RealESRGAN](https://github.com/xinntao/Real-ESRGAN)** - AI超解像
- **[YomiToku](https://github.com/kotaro-kinoshita/yomitoku)** - 日本語OCR

---

## 開発について

このプロジェクトの開発には、AIエージェントツールを活用しています:

- **[claude-code-aida](https://github.com/clearclown/claude-code-aida)** - Claude Code用AIDAプラグイン
- **[AIDA](https://github.com/clearclown/aida)** - マルチエージェント開発フレームワーク (現在メンテナンス中)

TDD (テスト駆動開発) に基づいた品質の高いコード生成と、効率的な開発サイクルを実現しています。
