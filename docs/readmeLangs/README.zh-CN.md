<p align="center">
  <b>🌐 语言</b><br>
  <a href="../../README.md">日本語</a> |
  <a href="README.en.md">English</a> |
  <b>简体中文</b> |
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
> 使用 Rust 完全重写的扫描书籍 PDF 高质量增强工具

**原作者:** 登 大遊 (Daiyuu Nobori)
**Rust 重写:** clearclown
**许可证:** AGPL v3.0

---

## 处理前 / 处理后

![处理前后对比](../../doc_img/ba.png)

| | 处理前 (左) | 处理后 (右) |
|---|---|---|
| **分辨率** | 1242x2048 px | 2363x3508 px |
| **文件大小** | 981 KB | 1.6 MB |
| **质量** | 模糊、低对比度 | 清晰、高对比度 |

通过 RealESRGAN AI 超分辨率技术，文字边缘变得锐利，可读性大幅提升。

---

## 功能特性

- **Rust 实现** - 从 C# 完全重写，内存效率和性能大幅提升
- **AI 超分辨率** - 使用 RealESRGAN 进行 2 倍图像放大
- **日文 OCR** - 使用 YomiToku 进行高精度文字识别
- **Markdown 转换** - 从 PDF 生成结构化 Markdown（自动检测图表）
- **倾斜校正** - 通过大津二值化 + 霍夫变换自动校正
- **180度旋转检测** - 自动检测和校正上下颠倒的页面
- **阴影去除** - 自动检测和去除装订阴影
- **标记去除** - 检测和去除荧光笔标记
- **去模糊** - 锐化模糊图像 (Unsharp Mask / NAFNet / DeblurGAN-v2)
- **色彩校正** - HSV 透印抑制、纸张白化
- **Web UI** - 通过浏览器直观操作

---

## 快速开始

```bash
# 从源码构建
git clone https://github.com/clearclown/Rust_DN_SuperBook_PDF_Converter.git
cd Rust_DN_SuperBook_PDF_Converter/superbook-pdf
cargo build --release --features web

# 基本转换
superbook-pdf convert input.pdf -o output/

# 高质量转换（AI 超分辨率 + 色彩校正 + 偏移对齐）
superbook-pdf convert input.pdf -o output/ --advanced --ocr

# Markdown 转换
superbook-pdf markdown input.pdf -o markdown_output/

# 启动 Web UI
superbook-pdf serve --port 8080
```

---

## 命令体系

superbook-pdf 提供 5 个子命令:

| 命令 | 说明 |
|------|------|
| `convert` | 使用 AI 增强 PDF 生成高质量 PDF |
| `markdown` | 从 PDF 生成结构化 Markdown |
| `reprocess` | 重新处理转换失败的页面 |
| `info` | 显示系统环境信息（GPU、依赖工具等） |
| `cache-info` | 显示输出 PDF 的缓存信息 |

### `convert` - PDF 高质量增强

```bash
# 基本（倾斜校正 + 边距修剪 + AI 超分辨率）
superbook-pdf convert input.pdf -o output/

# 最高质量（所有功能启用）
superbook-pdf convert input.pdf -o output/ --advanced --ocr

# 阴影去除 + 标记去除 + 去模糊
superbook-pdf convert input.pdf -o output/ --shadow-removal auto --remove-markers --deblur
```

### `markdown` - PDF 转 Markdown

```bash
# 基本转换
superbook-pdf markdown input.pdf -o output/

# 指定竖排文本 + AI 超分辨率
superbook-pdf markdown input.pdf -o output/ --text-direction vertical --upscale

# 恢复中断的处理
superbook-pdf markdown input.pdf -o output/ --resume
```

---

## 处理流水线

```
输入 PDF
  |
  +- Step 1:  PDF 图像提取 (pdftoppm)
  +- Step 2:  边距修剪 (默认 0.7%)
  +- Step 3:  阴影去除
  +- Step 4:  AI 超分辨率 (RealESRGAN 2x)
  +- Step 5:  去模糊
  +- Step 6:  180度旋转检测/校正
  +- Step 7:  倾斜校正 (大津二值化 + 霍夫变换)
  +- Step 8:  色彩校正 (HSV 透印抑制)
  +- Step 9:  标记去除
  +- Step 10: 分组裁剪 (统一边距)
  +- Step 11: PDF 生成 (JPEG DCT 压缩)
  +- Step 12: OCR (YomiToku)
  |
  输出 PDF
```

空白页自动检测（阈值 2%）并跳过所有处理。

---

## 安装

### 系统要求

| 项目 | 要求 |
|------|------|
| 操作系统 | Linux / macOS / Windows |
| Rust | 1.82 以上（源码构建时） |
| Poppler | `pdftoppm` 命令 |

使用 AI 功能需要 Python 3.10+ 和 NVIDIA GPU (CUDA 11.8+)。

### Docker/Podman 运行（推荐）

```bash
# NVIDIA GPU
docker compose up -d

# AMD GPU (ROCm)
docker compose -f docker-compose.yml -f docker-compose.rocm.yml up -d

# 仅 CPU
docker compose -f docker-compose.yml -f docker-compose.cpu.yml up -d
```

在浏览器中打开 http://localhost:8080。

---

## 详细文档

| 文档 | 内容 |
|------|------|
| [docs/pipeline.md](../pipeline.md) | 处理流水线详细设计 |
| [docs/commands.md](../commands.md) | 全部命令和选项参考 |
| [docs/configuration.md](../configuration.md) | 配置文件 (TOML) 自定义 |
| [docs/docker.md](../docker.md) | Docker/Podman 环境详细指南 |
| [docs/development.md](../development.md) | 开发者指南 |

---

## 许可证

AGPL v3.0 - [LICENSE](../../LICENSE)

## 致谢

- **登 大遊 (Daiyuu Nobori)** - 原始实现
- **[RealESRGAN](https://github.com/xinntao/Real-ESRGAN)** - AI 超分辨率
- **[YomiToku](https://github.com/kotaro-kinoshita/yomitoku)** - 日文 OCR
