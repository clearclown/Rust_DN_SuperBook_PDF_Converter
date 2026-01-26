# superbook-pdf

[![License: AGPL v3](https://img.shields.io/badge/License-AGPL%20v3-blue.svg)](https://www.gnu.org/licenses/agpl-3.0)
[![Rust](https://img.shields.io/badge/rust-1.75%2B-orange.svg)](https://www.rust-lang.org/)
[![crates.io](https://img.shields.io/crates/v/superbook-pdf.svg)](https://crates.io/crates/superbook-pdf)

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

![Before and After comparison](https://raw.githubusercontent.com/clearclown/DN_SuperBook_PDF_Converter_Linux/master/doc_img/ba.png)

| | Before (左) | After (右) |
|---|---|---|
| **解像度** | 1242×2048 px | 2363×3508 px |
| **ファイルサイズ** | 981 KB | 1.6 MB |
| **品質** | ぼやけ、低コントラスト | 鮮明、高コントラスト |

RealESRGAN による AI 超解像で、文字のエッジが鮮明になり、読みやすさが大幅に向上します。

---

## 特徴

- **Rust 実装** - C# 版を完全リライト。メモリ効率が大幅に改善されています
- **AI 超解像** - RealESRGAN で画像を高解像度化
- **日本語 OCR** - YomiToku による文字認識
- **傾き補正** - 大津二値化 + Hough 変換で自動補正
- **Web UI** - ブラウザから直感的に操作できます

---

## Web UI

![Web UI](https://raw.githubusercontent.com/clearclown/DN_SuperBook_PDF_Converter_Linux/master/doc_img/webUI.png)

ブラウザベースのインターフェースで、コマンドラインに慣れていない方でも簡単に使えます。ファイルをドラッグ&ドロップするだけで変換が始まります。

---

## インストール

### 必要なもの

| 項目 | 要件 |
|------|------|
| OS | Linux / macOS / Windows |
| Rust | 1.75 以上 (ソースビルド時) |
| Poppler | `pdftoppm` コマンド |

AI機能を使う場合は、Python 3.10以上と NVIDIA GPU (CUDA 11.8+) が必要です。

> **Note:** 開発とテストは主に Linux で行っていますが、Rust で書かれているため macOS や Windows でも動作します。

### 1. システム依存パッケージ

```bash
# Ubuntu/Debian
sudo apt update
sudo apt install -y poppler-utils python3 python3-venv

# Fedora
sudo dnf install -y poppler-utils python3

# macOS (Homebrew)
brew install poppler python

# Windows (Chocolatey)
choco install poppler python
```

### 2. superbook-pdf のインストール

一番簡単な方法は crates.io からインストールすることです:

```bash
cargo install superbook-pdf --features web
```

これで `superbook-pdf` コマンドが使えるようになります。

ソースからビルドしたい場合:

```bash
git clone https://github.com/clearclown/DN_SuperBook_PDF_Converter_Linux.git
cd DN_SuperBook_PDF_Converter_Linux/superbook-pdf
cargo build --release --features web
```

### 3. AI機能のセットアップ (オプション)

AI超解像 (RealESRGAN) と OCR (YomiToku) を使いたい場合は、Python環境をセットアップします:

```bash
cd ai_bridge

# Python 仮想環境を作成
python3 -m venv .venv
source .venv/bin/activate

# GPU版 PyTorch をインストール
pip install torch torchvision --index-url https://download.pytorch.org/whl/cu121

# AI依存パッケージをインストール
pip install -r requirements.txt
```

実行時に環境変数を設定してください:

```bash
export SUPERBOOK_VENV=/path/to/ai_bridge/.venv
```

### 4. Docker/Podman で実行 (オプション)

環境構築が面倒な場合は、コンテナを使うと楽です:

```bash
cd superbook-pdf

# Docker
docker compose up -d

# Podman (4.7+)
podman compose up -d
```

ブラウザで http://localhost:8080 を開けば使えます。

---

## コマンドの使い方

### 基本的な使い方

```bash
# シンプルな変換
superbook-pdf convert input.pdf -o output/

# 高品質変換 (AI超解像 + カラー補正 + オフセット調整)
superbook-pdf convert input.pdf -o output/ --advanced

# OCR付き高品質変換
superbook-pdf convert input.pdf -o output/ --advanced --ocr

# GPUを使わない場合
superbook-pdf convert input.pdf -o output/ --no-gpu
```

### Web UI を起動する

```bash
superbook-pdf serve --port 8080
```

ブラウザで http://localhost:8080 を開いてください。

### コマンド一覧

```
superbook-pdf <COMMAND>

Commands:
  convert     PDFを変換する
  serve       Web UIを起動する
  reprocess   失敗したページを再処理する
  info        システム情報を表示する
  cache-info  キャッシュ情報を表示する
```

### convert コマンドのオプション

よく使うオプションをまとめました:

| オプション | 説明 |
|-----------|------|
| `-o, --output <DIR>` | 出力先ディレクトリ (デフォルト: ./output) |
| `--advanced` | 高品質処理を有効化 (おすすめ) |
| `--ocr` | 日本語OCRを有効化 |
| `--no-gpu` | GPUを使わない |
| `--no-upscale` | AI超解像をスキップ |
| `--no-deskew` | 傾き補正をスキップ |
| `--dpi <N>` | 出力DPI (デフォルト: 300) |
| `--max-pages <N>` | 処理するページ数を制限 (テスト用) |
| `-v, -vv, -vvv` | ログの詳細度を上げる |
| `--dry-run` | 実際には処理せず、実行計画を表示 |

全オプションは `superbook-pdf convert --help` で確認できます。

### serve コマンドのオプション

| オプション | 説明 |
|-----------|------|
| `-p, --port <PORT>` | ポート番号 (デフォルト: 8080) |
| `-b, --bind <ADDR>` | バインドアドレス (デフォルト: 127.0.0.1) |
| `--upload-limit <MB>` | アップロード上限 (デフォルト: 500MB) |

---

## 処理パイプライン

1. **PDF画像抽出** - pdftoppm で 300 DPI 抽出
2. **マージントリム** - 0.5% の余白を除去
3. **AI超解像** - RealESRGAN で 2x アップスケール
4. **傾き補正** - 大津二値化 + Hough変換
5. **カラー補正** - 紙色の白化 (--advanced)
6. **PDF生成** - メタデータ同期
7. **OCR** - YomiToku (--ocr)

---

## トラブルシューティング

| 問題 | 解決策 |
|------|--------|
| `pdftoppm: command not found` | `sudo apt install poppler-utils` |
| RealESRGAN が動かない | `SUPERBOOK_VENV` 環境変数を設定してください |
| GPU が使用されない | PyTorchのCUDA版をインストール: `pip install torch --index-url https://download.pytorch.org/whl/cu121` |
| メモリ不足 | `--max-pages 10` で分割処理するか、`--chunk-size 5` でチャンク処理 |

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

これらのツールにより、TDD (テスト駆動開発) に基づいた品質の高いコード生成と、効率的な開発サイクルを実現しています。
