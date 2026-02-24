<p align="center">
  <b>🌐 Language</b><br>
  <a href="../../README.md">日本語</a> |
  <b>English</b> |
  <a href="README.zh-CN.md">简体中文</a> |
  <a href="README.zh-TW.md">繁體中文</a> |
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
> A high-quality PDF enhancement tool for scanned books, fully rewritten in Rust

**Original Author:** Daiyuu Nobori
**Rust Rewrite:** clearclown
**License:** AGPL v3.0

---

## Before / After

![Before and After comparison](../../doc_img/ba.png)

| | Before (Left) | After (Right) |
|---|---|---|
| **Resolution** | 1242x2048 px | 2363x3508 px |
| **File Size** | 981 KB | 1.6 MB |
| **Quality** | Blurry, low contrast | Sharp, high contrast |

AI super-resolution with RealESRGAN sharpens text edges and dramatically improves readability.

---

## Features

- **Rust Implementation** - Complete rewrite from C#. Greatly improved memory efficiency and performance
- **AI Super-Resolution** - 2x upscaling with RealESRGAN
- **Japanese OCR** - High-accuracy text recognition with YomiToku
- **Markdown Conversion** - Generate structured Markdown from PDFs (with automatic figure/table detection)
- **Deskew Correction** - Automatic correction via Otsu binarization + Hough transform
- **180-Degree Rotation Detection** - Automatically detect and correct upside-down pages
- **Shadow Removal** - Automatically detect and remove book binding shadows
- **Marker Removal** - Detect and remove highlighter marks
- **Deblur** - Sharpen blurry images (Unsharp Mask / NAFNet / DeblurGAN-v2)
- **Color Correction** - HSV bleed-through suppression, paper whitening
- **Web UI** - Intuitive browser-based operation

---

## Quick Start

```bash
# Build from source
git clone https://github.com/clearclown/Rust_DN_SuperBook_PDF_Converter.git
cd Rust_DN_SuperBook_PDF_Converter/superbook-pdf
cargo build --release --features web

# Basic conversion
superbook-pdf convert input.pdf -o output/

# High-quality conversion (AI super-resolution + color correction + offset alignment)
superbook-pdf convert input.pdf -o output/ --advanced --ocr

# Markdown conversion
superbook-pdf markdown input.pdf -o markdown_output/

# Launch Web UI
superbook-pdf serve --port 8080
```

---

## Commands

superbook-pdf provides 5 subcommands:

| Command | Description |
|---------|-------------|
| `convert` | Enhance PDF with AI processing to produce high-quality PDF |
| `markdown` | Generate structured Markdown from PDF |
| `reprocess` | Reprocess failed pages from a previous conversion |
| `info` | Display system environment information (GPU, dependencies, etc.) |
| `cache-info` | Display cache information for an output PDF |

### `convert` - PDF Enhancement

Enhance scanned PDFs with AI processing to produce high-quality output.

```bash
# Basic (deskew + margin trim + AI super-resolution)
superbook-pdf convert input.pdf -o output/

# Best quality (all features enabled)
superbook-pdf convert input.pdf -o output/ --advanced --ocr

# Shadow removal + marker removal + deblur
superbook-pdf convert input.pdf -o output/ --shadow-removal auto --remove-markers --deblur

# Test (first 5 pages, plan only)
superbook-pdf convert input.pdf -o output/ --max-pages 5 --dry-run
```

**Key Options:**

| Option | Default | Description |
|--------|---------|-------------|
| `-o, --output <DIR>` | `./output` | Output directory |
| `--advanced` | off | Enable high-quality processing (internal resolution + color correction + offset alignment) |
| `--ocr` | off | Enable Japanese OCR |
| `--dpi <N>` | 300 | Output DPI |
| `--jpeg-quality <N>` | 90 | JPEG compression quality in PDF (1-100) |
| `-m, --margin-trim <N>` | 0.7 | Margin trim percentage (%) |
| `--shadow-removal <MODE>` | auto | Shadow removal mode (none/auto/left/right/both) |
| `--remove-markers` | off | Enable highlighter marker removal |
| `--deblur` | off | Enable deblur correction |
| `--no-upscale` | - | Skip AI super-resolution |
| `--no-deskew` | - | Skip deskew correction |
| `--no-gpu` | - | Disable GPU processing |
| `--dry-run` | - | Show execution plan only (no processing) |
| `--max-pages <N>` | - | Limit number of pages to process |
| `-v, -vv, -vvv` | - | Log verbosity |

See `superbook-pdf convert --help` for all options.

### `markdown` - PDF to Markdown Conversion

OCR PDFs and convert to structured Markdown. Supports automatic figure detection/extraction, table detection, and reading order determination.

```bash
# Basic conversion
superbook-pdf markdown input.pdf -o output/

# Specify vertical text + AI super-resolution
superbook-pdf markdown input.pdf -o output/ --text-direction vertical --upscale

# With quality validation
superbook-pdf markdown input.pdf -o output/ --validate --api-provider claude

# Resume interrupted processing
superbook-pdf markdown input.pdf -o output/ --resume
```

**Key Options:**

| Option | Default | Description |
|--------|---------|-------------|
| `-o, --output <DIR>` | `./markdown_output` | Output directory |
| `--text-direction` | auto | Text direction (auto/horizontal/vertical) |
| `--upscale` | off | Apply AI super-resolution before OCR |
| `--dpi <N>` | 300 | Output DPI |
| `--figure-sensitivity <N>` | - | Figure detection sensitivity (0.0-1.0) |
| `--no-extract-images` | - | Disable image extraction |
| `--no-detect-tables` | - | Disable table detection |
| `--validate` | off | Enable output Markdown quality validation |
| `--resume` | - | Resume interrupted processing |

See `superbook-pdf markdown --help` for all options.

### `reprocess` - Reprocess Failed Pages

Reprocess only pages that encountered errors during conversion.

```bash
# Auto-detect and reprocess from state file
superbook-pdf reprocess output/.superbook-state.json

# Reprocess specific pages only
superbook-pdf reprocess output/.superbook-state.json -p 5,12,30

# Check status only
superbook-pdf reprocess output/.superbook-state.json --status
```

---

## Processing Pipeline

Processing flow for the `convert` command:

```
Input PDF
  |
  +- Step 1:  PDF Image Extraction (pdftoppm, specified DPI)
  +- Step 2:  Margin Trim (default 0.7%)
  +- Step 3:  Shadow Removal (--shadow-removal)
  +- Step 4:  AI Super-Resolution (RealESRGAN 2x)
  +- Step 5:  Deblur (--deblur)
  +- Step 6:  180-Degree Rotation Detection/Correction
  +- Step 7:  Deskew Correction (Otsu binarization + Hough transform)
  +- Step 8:  Color Correction (HSV bleed-through suppression)
  +- Step 9:  Marker Removal (--remove-markers)
  +- Step 10: Group Crop (uniform margins)
  +- Step 11: PDF Generation (JPEG DCT compression)
  +- Step 12: OCR (YomiToku, --ocr)
  |
  Output PDF
```

Blank pages are automatically detected (threshold 2%) and skip all processing.

---

## Installation

### Requirements

| Item | Requirement |
|------|-------------|
| OS | Linux / macOS / Windows |
| Rust | 1.82+ (for source builds) |
| Poppler | `pdftoppm` command |

For AI features, Python 3.10+ and an NVIDIA GPU (CUDA 11.8+) are required.

### 1. System Dependencies

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

### 2. Install superbook-pdf

```bash
# Build from source
git clone https://github.com/clearclown/Rust_DN_SuperBook_PDF_Converter.git
cd Rust_DN_SuperBook_PDF_Converter/superbook-pdf
cargo build --release --features web
```

### 3. AI Feature Setup (Optional)

> Not needed when using Docker/Podman.

```bash
cd superbook-pdf/ai_bridge

# Create Python virtual environment
python3 -m venv .venv
source .venv/bin/activate

# Install GPU PyTorch
pip install torch torchvision --index-url https://download.pytorch.org/whl/cu121

# Install AI dependencies
pip install -r requirements.txt
```

Set the environment variable at runtime:

```bash
export SUPERBOOK_VENV=/path/to/ai_bridge/.venv
```

### 4. Run with Docker/Podman (Recommended)

```bash
# NVIDIA GPU
docker compose up -d

# AMD GPU (ROCm)
docker compose -f docker-compose.yml -f docker-compose.rocm.yml up -d

# CPU only
docker compose -f docker-compose.yml -f docker-compose.cpu.yml up -d
```

Open http://localhost:8080 in your browser.

---

## Web UI

![Web UI](../../doc_img/webUI.png)

A browser-based interface where you can simply drag and drop files to start conversion. Real-time progress display via WebSocket.

```bash
superbook-pdf serve --port 8080 --bind 0.0.0.0
```

---

## Documentation

| Document | Contents |
|----------|----------|
| [docs/pipeline.md](../pipeline.md) | Detailed processing pipeline design |
| [docs/commands.md](../commands.md) | Full command and option reference |
| [docs/configuration.md](../configuration.md) | Customization via configuration files (TOML) |
| [docs/docker.md](../docker.md) | Detailed Docker/Podman environment guide |
| [docs/development.md](../development.md) | Developer guide (build, test, architecture) |

---

## Troubleshooting

| Problem | Solution |
|---------|----------|
| `pdftoppm: command not found` | `sudo apt install poppler-utils` |
| RealESRGAN not working | Set the `SUPERBOOK_VENV` environment variable |
| GPU not being used | `pip install torch --index-url https://download.pytorch.org/whl/cu121` |
| Out of memory | Use `--max-pages 10` or `--chunk-size 5` for chunked processing |
| Deskew distorts image | Disable with `--no-deskew` |
| Margins clip text | Increase safety buffer with `--margin-safety 1.0` |

---

## License

AGPL v3.0 - [LICENSE](../../LICENSE)

---

## Acknowledgments

- **Daiyuu Nobori** - Original implementation
- **[RealESRGAN](https://github.com/xinntao/Real-ESRGAN)** - AI super-resolution
- **[YomiToku](https://github.com/kotaro-kinoshita/yomitoku)** - Japanese OCR
