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
# ビルド
cargo build --release --features web

# 基本変換
superbook-pdf convert input.pdf -o output/

# 高品質変換
superbook-pdf convert input.pdf -o output/ --advanced --ocr

# Markdown 変換
superbook-pdf markdown input.pdf -o markdown_output/

# Web UI 起動
superbook-pdf serve --port 8080
```

---

## コマンド体系

| コマンド | 説明 |
|---------|------|
| `convert` | PDF を AI 強化して高品質 PDF に変換 |
| `markdown` | PDF から構造化された Markdown を生成 |
| `reprocess` | 変換に失敗したページを再処理 |
| `info` | システム環境情報を表示 |
| `cache-info` | 出力 PDF のキャッシュ情報を表示 |

詳細は [プロジェクトルートの README](../README.md) と [docs/](../docs/) を参照してください。

---

## 処理パイプライン

```
入力PDF → 画像抽出 → マージントリム → 影除去 → AI超解像 → ブレ補正
  → 回転検出 → 傾き補正 → カラー補正 → マーカー除去
  → グループクロップ → PDF生成 → OCR → 出力PDF
```

詳細: [docs/pipeline.md](../docs/pipeline.md)

---

## Docker

```bash
# NVIDIA GPU
docker compose up -d

# CPU のみ
docker compose -f docker-compose.yml -f docker-compose.cpu.yml up -d
```

詳細: [docs/docker.md](../docs/docker.md)

---

## 開発

```bash
cargo test --features web
cargo clippy --features web -- -D warnings
cargo fmt
```

詳細: [docs/development.md](../docs/development.md)

---

## ライセンス

AGPL v3.0 - [LICENSE](../LICENSE)
