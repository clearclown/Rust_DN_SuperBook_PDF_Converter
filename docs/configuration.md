# 設定ファイル

superbook-pdf は TOML 形式の設定ファイルによるカスタマイズに対応しています。

---

## 使い方

```bash
superbook-pdf convert input.pdf -o output/ --config my_config.toml
```

CLI 引数は設定ファイルの値を上書きします。

---

## 設定ファイルの例

```toml
# 出力設定
dpi = 600
output_height = 5600
jpeg_quality = 97

# マージントリム
margin_trim = 0.7
content_aware_margins = true
margin_safety = 0.5

# AI超解像
upscale = true
gpu = true

# 傾き補正
deskew = true

# カラー補正
color_correction = true

# 影除去
shadow_removal = "auto"

# マーカー除去
remove_markers = false
marker_colors = ["yellow", "pink", "green", "blue"]

# ブレ補正
deblur = false
deblur_algorithm = "unsharp-mask"

# パフォーマンス
chunk_size = 0
```

---

## 全パラメータ一覧

### 基本設定

| パラメータ | 型 | デフォルト | 説明 |
|-----------|-----|-----------|------|
| `dpi` | int | 300 | 出力 DPI |
| `output_height` | int | 3508 | 出力画像の高さ (px) |
| `jpeg_quality` | int | 90 | JPEG 圧縮品質 (1-100) |
| `upscale` | bool | true | AI超解像の有効/無効 |
| `gpu` | bool | true | GPU処理の有効/無効 |

### マージン

| パラメータ | 型 | デフォルト | 説明 |
|-----------|-----|-----------|------|
| `margin_trim` | float | 0.7 | マージントリム率 (%) |
| `content_aware_margins` | bool | true | コンテンツ認識マージン |
| `margin_safety` | float | 0.5 | 安全バッファ率 (%) |

### 画像補正

| パラメータ | 型 | デフォルト | 説明 |
|-----------|-----|-----------|------|
| `deskew` | bool | true | 傾き補正 |
| `color_correction` | bool | true | カラー補正 |

### クリーンアップ

| パラメータ | 型 | デフォルト | 説明 |
|-----------|-----|-----------|------|
| `shadow_removal` | string | "auto" | 影除去モード |
| `remove_markers` | bool | false | マーカー除去 |
| `marker_colors` | array | ["yellow","pink","green","blue"] | 除去する色 |
| `deblur` | bool | false | ブレ補正 |
| `deblur_algorithm` | string | "unsharp-mask" | ブレ補正アルゴリズム |

### パフォーマンス

| パラメータ | 型 | デフォルト | 説明 |
|-----------|-----|-----------|------|
| `chunk_size` | int | 0 | チャンクサイズ (0 = 一括) |

---

## 環境変数

| 変数名 | 説明 |
|--------|------|
| `SUPERBOOK_VENV` | Python venv パス (AI機能に必要) |
| `SUPERBOOK_NO_GPU` | `1` を設定すると GPU を無効化 |
| `ANTHROPIC_API_KEY` | Claude API キー (Markdown 検証用) |
| `OPENAI_API_KEY` | OpenAI API キー (Markdown 検証用) |

---

## CLI 引数との優先順位

```
CLI 引数 > 設定ファイル > デフォルト値
```

例: 設定ファイルに `dpi = 600` と書いても、CLI で `--dpi 300` を指定すれば 300 が使われます。
