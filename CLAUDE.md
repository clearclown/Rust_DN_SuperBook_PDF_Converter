# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

superbook-pdf is a Rust implementation of a high-quality PDF converter for scanned books. It provides AI image enhancement, deskew correction, page offset alignment, margin optimization, and Japanese OCR capabilities.

**Original Author:** Daiyuu Nobori (登 大遊)
**Rust Rewrite:** clearclown
**License:** AGPL v3.0

---

## Quick Start

```bash
cd superbook-pdf

# Build
cargo build --release --features web

# Test
cargo test --features web

# Run CLI
cargo run -- convert input.pdf -o output/

# Run Web Server
cargo run --features web -- serve --port 8080
```

---

## Architecture

### Directory Structure

```
superbook-pdf/
├── Cargo.toml
├── src/
│   ├── main.rs              # CLI entry point
│   ├── lib.rs               # Library exports
│   ├── cli.rs               # CLI argument parsing
│   ├── pipeline.rs          # Main processing pipeline
│   ├── config.rs            # Configuration management
│   ├── pdf_reader.rs        # PDF image extraction
│   ├── pdf_writer.rs        # PDF generation
│   ├── deskew/              # Deskew correction
│   │   ├── mod.rs
│   │   └── algorithm.rs     # Otsu + Hough transform
│   ├── margin/              # Margin processing
│   ├── page_number/         # Page number detection
│   │   ├── mod.rs
│   │   ├── detect.rs        # 4-stage fallback matching
│   │   ├── offset.rs        # Group-based reference position
│   │   └── types.rs         # Type definitions
│   ├── color_stats.rs       # HSV color correction
│   ├── realesrgan.rs        # AI upscaling (Python bridge)
│   ├── yomitoku.rs          # Japanese OCR (Python bridge)
│   └── web/                 # Web API (feature: web)
│       ├── mod.rs
│       ├── server.rs        # Axum server
│       ├── routes.rs        # REST endpoints
│       ├── websocket.rs     # WebSocket handler
│       ├── job.rs           # Job queue
│       ├── worker.rs        # Background processing
│       └── static/          # Web UI assets
├── specs/                   # TDD specifications
├── tests/                   # Integration tests
└── ai_bridge/               # Python AI modules
```

### Key Modules

| Module | Purpose |
|--------|---------|
| `pipeline.rs` | Main processing orchestration, memory management |
| `deskew/algorithm.rs` | Otsu binarization, WarpAffine rotation |
| `page_number/detect.rs` | 4-stage fallback page number detection |
| `page_number/offset.rs` | calc_overlap_center algorithm |
| `color_stats.rs` | HSV bleed-through suppression |
| `web/websocket.rs` | Real-time progress, page previews |

---

## Development Guidelines

### TDD Workflow

```
1. Write spec in specs/*.spec.md
2. Create tests in tests/ or module tests (Red)
3. Implement in src/ (Green)
4. Refactor
5. Run full test suite
```

### Testing

```bash
# All tests
cargo test

# With web features
cargo test --features web

# Specific module
cargo test deskew
cargo test page_number

# Single test
cargo test test_otsu_threshold
```

### Code Style

- Use `cargo fmt` before committing
- Run `cargo clippy` for lints
- Follow Rust naming conventions
- Add tests for new functionality

---

## Key Algorithms (Ported from C#)

### 1. Otsu Binarization (`deskew/algorithm.rs`)
- Automatic threshold detection for deskew
- Morphology open for noise removal
- Hough lines for angle detection

### 2. 4-Stage Fallback Matching (`page_number/detect.rs`)
- Stage 1: Exact match + region + min distance
- Stage 2: Max similarity (Jaro-Winkler) + region
- Stage 3: OCR success region + min distance
- Stage 4: All detected regions + min distance

### 3. Group-Based Reference Position (`page_number/offset.rs`)
- Add 3% margin to each BBOX
- Count BBOXes contained in all pages
- Extract top 70%+ match count
- Calculate overlap center from top 30% by area

### 4. HSV Color Correction (`color_stats.rs`)
- Bleed-through suppression parameters
- Hue range: 20-65 (yellow-orange)
- Saturation max: 0.3
- Value min: 0.7

---

## Processing Pipeline

1. **PDF Image Extraction** - pdftoppm for high-resolution extraction
2. **Margin Trimming** - 0.7% content-aware margin removal
3. **Shadow Removal** - Book binding shadow detection/removal
4. **AI Upscaling** - RealESRGAN 2x
5. **Deblur** - Unsharp Mask / NAFNet / DeblurGAN-v2
6. **Rotation Detection** - 180-degree upside-down detection via ink density
7. **Deskew Correction** - Otsu binarization + Hough transform
8. **Color Correction** - HSV bleed-through suppression (enabled by default)
9. **Marker Removal** - Highlighter marker detection/removal
10. **Group Crop** - Uniform margins across pages
11. **PDF Generation** - JPEG DCT compression, metadata sync
12. **OCR (Optional)** - YomiToku Japanese AI OCR

---

## External Dependencies

### Required
- pdftoppm (Poppler) - PDF image extraction

### Optional (Python)
- RealESRGAN - AI upscaling
- YomiToku - Japanese OCR

---

## Hardware Requirements

- **RAM:** 4GB+ (target: 1-3GB peak usage)
- **GPU:** NVIDIA CUDA GPU (4GB+ VRAM) for AI features
- RealESRGAN and YomiToku require GPU

---

## Legacy C# Version

The original C# implementation is preserved in the `legacy-csharp` branch:

```bash
git checkout legacy-csharp
```

Reference files:
- `SuperBookTools/Basic/SuperPdfUtil.cs` - Main processing logic (5,136 lines)

---

## References

- [Original Repository](https://github.com/dnobori/DN_SuperBook_PDF_Converter)
- [RealESRGAN](https://github.com/xinntao/Real-ESRGAN)
- [YomiToku](https://github.com/kotaro-kinoshita/yomitoku)
