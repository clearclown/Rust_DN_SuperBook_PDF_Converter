# Docker / Podman ガイド

superbook-pdf を Docker / Podman で実行するためのガイドです。

---

## サポートする GPU 環境

| 環境 | Compose ファイル | ベースイメージ |
|------|----------------|--------------|
| NVIDIA GPU | `docker-compose.yml` | `nvidia/cuda:12.1.1-devel-ubuntu22.04` |
| AMD GPU (ROCm) | `docker-compose.rocm.yml` | ROCm ベース |
| CPU のみ | `docker-compose.cpu.yml` | `ubuntu:22.04` |

---

## クイックスタート

### Web UI の起動

```bash
# NVIDIA GPU (デフォルト)
docker compose up -d

# AMD GPU (ROCm)
docker compose -f docker-compose.yml -f docker-compose.rocm.yml up -d

# CPU のみ
docker compose -f docker-compose.yml -f docker-compose.cpu.yml up -d
```

ブラウザで http://localhost:8080 を開いてください。

### CLI での PDF 変換

```bash
# イメージをビルド
docker compose build

# 変換実行 (GPU使用)
docker run --rm --gpus all \
  -v $(pwd)/input:/data/input:ro \
  -v $(pwd)/output:/data/output:rw \
  superbook-pdf:latest \
  convert /data/input/book.pdf -o /data/output/ --advanced --ocr
```

### Podman の場合

```bash
# NVIDIA GPU
podman compose up -d

# AMD GPU (ROCm)
podman compose -f docker-compose.yml -f docker-compose.rocm.yml up -d

# CPU のみ
podman compose -f docker-compose.yml -f docker-compose.cpu.yml up -d

# CLI 変換
podman run --rm --device nvidia.com/gpu=all \
  -v $(pwd)/input:/data/input:ro \
  -v $(pwd)/output:/data/output:rw \
  superbook-pdf:latest \
  convert /data/input/book.pdf -o /data/output/ --advanced --ocr
```

---

## CPU 専用 Dockerfile

GPU なしの環境向けに `Dockerfile.cpu` を提供しています。

```
superbook-pdf/docker/backend/Dockerfile.cpu
```

特徴:
- ベースイメージ: `ubuntu:22.04` (CUDA 不要、イメージサイズが小さい)
- PyTorch: CPU版 (`--index-url https://download.pytorch.org/whl/cpu`)
- 環境変数 `SUPERBOOK_NO_GPU=1` が設定済み

### 手動ビルド

```bash
docker build -f superbook-pdf/docker/backend/Dockerfile.cpu -t superbook-pdf:cpu superbook-pdf/
```

---

## Dockerfile の構成

```
superbook-pdf/docker/
└── backend/
    ├── Dockerfile         # NVIDIA GPU (デフォルト)
    ├── Dockerfile.cpu     # CPU のみ
    └── Dockerfile.rocm    # AMD GPU (ROCm)
```

### マルチステージビルド

各 Dockerfile は以下の構成です:

1. **Rust ビルドステージ** - `cargo build --release --features web`
2. **Python 環境ステージ** - AI ブリッジのセットアップ
3. **ランタイムステージ** - 最終イメージ (poppler-utils + バイナリ + venv)

---

## ボリュームマウント

| ホスト | コンテナ | 用途 |
|--------|---------|------|
| `./input` | `/data/input` | 入力 PDF (読み取り専用) |
| `./output` | `/data/output` | 出力ファイル |

---

## 環境変数

コンテナ内で使える環境変数:

| 変数 | デフォルト | 説明 |
|------|-----------|------|
| `SUPERBOOK_PORT` | 8080 | Web UI のポート |
| `SUPERBOOK_BIND` | 0.0.0.0 | バインドアドレス |
| `SUPERBOOK_NO_GPU` | (未設定) | `1` で GPU 無効化 |

---

## トラブルシューティング

### GPU が認識されない

```bash
# NVIDIA GPU の確認
nvidia-smi

# Docker で GPU が使えるか確認
docker run --rm --gpus all nvidia/cuda:12.1.1-base-ubuntu22.04 nvidia-smi
```

`nvidia-container-toolkit` がインストールされているか確認してください。

### ビルドが遅い

Rust のコンパイルに時間がかかります。初回ビルドは 10-30 分程度かかることがあります。Docker のビルドキャッシュを活用してください。

### メモリ不足

大きな PDF を処理する場合、Docker のメモリ制限に注意してください:

```bash
# メモリ制限を 8GB に設定
docker run --rm --gpus all --memory=8g \
  -v $(pwd)/input:/data/input:ro \
  -v $(pwd)/output:/data/output:rw \
  superbook-pdf:latest \
  convert /data/input/large_book.pdf -o /data/output/ --chunk-size 5
```
