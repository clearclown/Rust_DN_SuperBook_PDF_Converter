<p align="center">
  <b>🌐 語言</b><br>
  <a href="../../README.md">日本語</a> |
  <a href="README.en.md">English</a> |
  <a href="README.zh-CN.md">简体中文</a> |
  <b>繁體中文</b> |
  <a href="README.ru.md">Русский</a> |
  <a href="README.uk.md">Українська</a> |
  <a href="README.fa.md">فارسی</a> |
  <a href="README.ar.md">العربية</a>
</p>

# superbook-pdf

[![License: AGPL v3](https://img.shields.io/badge/License-AGPL%20v3-blue.svg)](https://www.gnu.org/licenses/agpl-3.0)
[![Rust](https://img.shields.io/badge/rust-1.82%2B-orange.svg)](https://www.rust-lang.org/)
[![CI](https://github.com/clearclown/Rust_DN_SuperBook_PDF_Converter/actions/workflows/ci.yml/badge.svg)](https://github.com/clearclown/Rust_DN_SuperBook_PDF_Converter/actions/workflows/ci.yml)

> **Fork of [dnobori/DN_SuperBook_PDF_Converter](https://github.com/dnobori/DN_SuperBook_PDF_Converter)**
>
> 使用 Rust 完全重寫的掃描書籍 PDF 高品質增強工具

**原作者:** 登 大遊 (Daiyuu Nobori)
**Rust 重寫:** clearclown
**授權:** AGPL v3.0

---

## 處理前 / 處理後

![處理前後對比](../../doc_img/ba.png)

| | 處理前 (左) | 處理後 (右) |
|---|---|---|
| **解析度** | 1242x2048 px | 2363x3508 px |
| **檔案大小** | 981 KB | 1.6 MB |
| **品質** | 模糊、低對比度 | 清晰、高對比度 |

透過 RealESRGAN AI 超解析度技術，文字邊緣變得銳利，可讀性大幅提升。

---

## 功能特色

- **Rust 實作** - 從 C# 完全重寫，記憶體效率與效能大幅提升
- **AI 超解析度** - 使用 RealESRGAN 進行 2 倍影像放大
- **日文 OCR** - 使用 YomiToku 進行高精度文字辨識
- **Markdown 轉換** - 從 PDF 產生結構化 Markdown（自動偵測圖表）
- **傾斜校正** - 透過大津二值化 + 霍夫轉換自動校正
- **180度旋轉偵測** - 自動偵測和校正上下顛倒的頁面
- **陰影去除** - 自動偵測和去除裝訂陰影
- **標記去除** - 偵測和去除螢光筆標記
- **去模糊** - 銳化模糊影像 (Unsharp Mask / NAFNet / DeblurGAN-v2)
- **色彩校正** - HSV 透印抑制、紙張白化
- **Web UI** - 透過瀏覽器直覺操作

---

## 快速開始

```bash
# 從原始碼建置
git clone https://github.com/clearclown/Rust_DN_SuperBook_PDF_Converter.git
cd Rust_DN_SuperBook_PDF_Converter/superbook-pdf
cargo build --release --features web

# 基本轉換
superbook-pdf convert input.pdf -o output/

# 高品質轉換（AI 超解析度 + 色彩校正 + 偏移對齊）
superbook-pdf convert input.pdf -o output/ --advanced --ocr

# Markdown 轉換
superbook-pdf markdown input.pdf -o markdown_output/

# 啟動 Web UI
superbook-pdf serve --port 8080
```

---

## 指令體系

| 指令 | 說明 |
|------|------|
| `convert` | 使用 AI 增強 PDF 產生高品質 PDF |
| `markdown` | 從 PDF 產生結構化 Markdown |
| `reprocess` | 重新處理轉換失敗的頁面 |
| `info` | 顯示系統環境資訊 |
| `cache-info` | 顯示輸出 PDF 的快取資訊 |

---

## 處理流程

```
輸入 PDF
  |
  +- Step 1:  PDF 影像擷取 (pdftoppm)
  +- Step 2:  邊距修剪 (預設 0.7%)
  +- Step 3:  陰影去除
  +- Step 4:  AI 超解析度 (RealESRGAN 2x)
  +- Step 5:  去模糊
  +- Step 6:  180度旋轉偵測/校正
  +- Step 7:  傾斜校正 (大津二值化 + 霍夫轉換)
  +- Step 8:  色彩校正 (HSV 透印抑制)
  +- Step 9:  標記去除
  +- Step 10: 分組裁剪 (統一邊距)
  +- Step 11: PDF 產生 (JPEG DCT 壓縮)
  +- Step 12: OCR (YomiToku)
  |
  輸出 PDF
```

---

## 安裝

### Docker/Podman 執行（建議）

```bash
# NVIDIA GPU
docker compose up -d

# AMD GPU (ROCm)
docker compose -f docker-compose.yml -f docker-compose.rocm.yml up -d

# 僅 CPU
docker compose -f docker-compose.yml -f docker-compose.cpu.yml up -d
```

在瀏覽器中開啟 http://localhost:8080。

---

## 授權

AGPL v3.0 - [LICENSE](../../LICENSE)

## 致謝

- **登 大遊 (Daiyuu Nobori)** - 原始實作
- **[RealESRGAN](https://github.com/xinntao/Real-ESRGAN)** - AI 超解析度
- **[YomiToku](https://github.com/kotaro-kinoshita/yomitoku)** - 日文 OCR
