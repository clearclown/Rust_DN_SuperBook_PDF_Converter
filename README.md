# superbook-pdf

> **Fork of [dnobori/DN_SuperBook_PDF_Converter](https://github.com/dnobori/DN_SuperBook_PDF_Converter)**
>
> Rust で完全リライトしたスキャン書籍 PDF 高品質化ツール

**オリジナル著者:** 登 大遊 (Daiyuu Nobori)
**Rust リライト:** clearclown

---

## 特徴

| 特徴 | 説明 |
|------|------|
| **Rust 実装** | C# 版を完全リライト。メモリ効率 10 倍改善 (10-30GB → 1-3GB) |
| **シングルバイナリ** | 依存関係最小化。コンテナ不要で直接実行可能 |
| **Web UI** | ブラウザからドラッグ&ドロップで PDF 変換 |
| **WebSocket** | リアルタイム進捗表示・ページプレビュー |
| **TDD 開発** | 1,260+ テストで品質担保 |

## インストール

### ビルド済みバイナリ (推奨)

```bash
# TODO: リリース後に追加
```

### ソースからビルド

```bash
git clone https://github.com/clearclown/DN_SuperBook_PDF_Converter_Linux.git
cd DN_SuperBook_PDF_Converter_Linux/superbook-pdf
cargo build --release --features web
```

#### 依存ツール

- **必須:** pdftoppm (Poppler)
- **推奨:** RealESRGAN (Python), YomiToku (Python, OCR用)

```bash
# Ubuntu/Debian
sudo apt install poppler-utils

# RealESRGAN (オプション)
pip install realesrgan

# YomiToku OCR (オプション)
pip install yomitoku
```

## 使い方

### CLI

```bash
# 基本変換
superbook-pdf convert input.pdf -o output/

# 高度なオプション付き
superbook-pdf convert input.pdf -o output/ \
  --dpi 600 \
  --advanced \
  --ocr

# 環境情報
superbook-pdf info
```

### Web UI

```bash
# Web サーバー起動
superbook-pdf serve --port 8080

# ブラウザで http://localhost:8080 にアクセス
```

## 処理パイプライン

1. **PDF 画像抽出** - pdftoppm で高解像度抽出
2. **傾き補正** - 大津二値化 + Hough 変換で自動検出・補正
3. **マージントリミング** - 0.5% マージン除去
4. **AI 高画質化** - RealESRGAN 2x アップスケール
5. **カラー補正** - HSV 空間で裏写り除去
6. **ページ番号検出** - 4 段階フォールバックマッチング
7. **オフセット整列** - グループベース基準位置決定
8. **余白統一** - 全ページで一貫したマージン
9. **PDF 生成** - メタデータ同期
10. **OCR (オプション)** - YomiToku 日本語 AI OCR

## 設定ファイル

`superbook.toml` で設定をカスタマイズ:

```toml
[pipeline]
dpi = 300
deskew = true
upscale = true
gpu = true

[advanced]
internal_resolution = false
color_correction = false
offset_alignment = false
```

## API リファレンス

### REST API

| エンドポイント | メソッド | 説明 |
|---------------|---------|------|
| `/api/jobs` | POST | ジョブ作成 (PDF アップロード) |
| `/api/jobs/{id}` | GET | ジョブ状態取得 |
| `/api/jobs/{id}/result` | GET | 結果ダウンロード |
| `/api/jobs/{id}` | DELETE | ジョブキャンセル |
| `/api/stats` | GET | サーバー統計 |
| `/ws` | WebSocket | リアルタイム通知 |

### WebSocket メッセージ

```json
{"type": "progress", "job_id": "...", "current": 5, "total": 13, "message": "Deskewing"}
{"type": "page_preview", "job_id": "...", "page_number": 1, "preview_base64": "...", "stage": "deskewed"}
{"type": "completed", "job_id": "...", "elapsed_seconds": 45.2, "page_count": 100}
```

## 開発

```bash
cd superbook-pdf

# テスト実行
cargo test

# Web 機能付きテスト
cargo test --features web

# フォーマット & Lint
cargo fmt && cargo clippy
```

## レガシー C# 版

オリジナルの C# 実装は `legacy-csharp` ブランチで保存されています:

```bash
git checkout legacy-csharp
```

## ライセンス

AGPL v3.0 - [LICENSE](LICENSE)

## 謝辞

- **登 大遊 (Daiyuu Nobori)** - オリジナル実装、PDF 処理アルゴリズム
- **RealESRGAN** - AI 画像高画質化
- **YomiToku** - 日本語 AI OCR
