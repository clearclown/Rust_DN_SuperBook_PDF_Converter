# コマンドリファレンス

superbook-pdf の全コマンドと全オプションの詳細リファレンスです。

---

## `convert` - PDF 高品質化

スキャンした PDF を AI 強化して高品質 PDF に変換します。

```
superbook-pdf convert [OPTIONS] <INPUT>
```

### 引数

| 引数 | 説明 |
|------|------|
| `<INPUT>` | 入力 PDF ファイルまたはディレクトリ |

### 出力設定

| オプション | デフォルト | 説明 |
|-----------|-----------|------|
| `-o, --output <DIR>` | `./output` | 出力先ディレクトリ |
| `--dpi <N>` | 300 | 出力 DPI (1-4800) |
| `--output-height <N>` | 3508 | 出力画像の高さ (px) |
| `--jpeg-quality <N>` | 90 | PDF 内 JPEG 圧縮品質 (1-100) |

### 処理モード

| オプション | 説明 |
|-----------|------|
| `--advanced` | 高品質処理を一括有効化 (内部解像度正規化 + カラー補正 + オフセット調整) |
| `--ocr` | 日本語 OCR を有効化 (YomiToku) |
| `--dry-run` | 実行計画を表示するのみ。実処理は行わない |
| `--skip-existing` | 出力ファイルが既に存在する場合はスキップ |
| `-f, --force` | キャッシュが有効でも強制的に再処理 |

### AI 超解像

| オプション | デフォルト | 説明 |
|-----------|-----------|------|
| `-u, --upscale <BOOL>` | true | AI 超解像の有効/無効 |
| `--no-upscale` | - | AI 超解像を無効化 |

### 傾き補正

| オプション | デフォルト | 説明 |
|-----------|-----------|------|
| `-d, --deskew <BOOL>` | true | 傾き補正の有効/無効 |
| `--no-deskew` | - | 傾き補正を無効化 |

### マージントリム

| オプション | デフォルト | 説明 |
|-----------|-----------|------|
| `-m, --margin-trim <N>` | 0.7 | マージントリム率 (%) |
| `--content-aware-margins <BOOL>` | true | コンテンツ認識マージン検出 |
| `--no-content-aware-margins` | - | コンテンツ認識マージンを無効化 |
| `--margin-safety <N>` | 0.5 | 安全バッファ率 (0.0-5.0) |
| `--aggressive-trim` | - | アグレッシブトリム (文字欠損リスクあり) |

### 影除去

| オプション | デフォルト | 説明 |
|-----------|-----------|------|
| `--shadow-removal <MODE>` | auto | 影除去モード |

**モード一覧:**

| 値 | 説明 |
|----|------|
| `none` | 影除去を行わない |
| `auto` | 影を自動検出して除去 |
| `left` | 左端の影のみ除去 |
| `right` | 右端の影のみ除去 |
| `both` | 両端の影を除去 |

### マーカー除去

| オプション | デフォルト | 説明 |
|-----------|-----------|------|
| `--remove-markers` | off | 蛍光マーカー除去を有効化 |
| `--marker-colors <COLORS>` | yellow,pink,green,blue | 除去する色 (カンマ区切り) |

指定可能な色: `yellow`, `pink`, `green`, `blue`, `orange`

### ブレ補正

| オプション | デフォルト | 説明 |
|-----------|-----------|------|
| `--deblur` | off | ブレ補正を有効化 |
| `--deblur-algorithm <ALG>` | unsharp-mask | 使用アルゴリズム |

**アルゴリズム一覧:**

| 値 | 説明 | GPU |
|----|------|-----|
| `unsharp-mask` | Unsharp Mask (高速、ローカル処理) | 不要 |
| `nafnet` | NAFNet AI ブレ補正 | 必要 |
| `deblurgan-v2` | DeblurGAN-v2 AI ブレ補正 | 必要 |

### カラー補正

| オプション | 説明 |
|-----------|------|
| `--color-correction` | グローバルカラー補正を有効化 |
| `--internal-resolution` | 内部解像度正規化 (4960x7016) |
| `--offset-alignment` | ページ番号オフセット調整 |

> `--advanced` はこれら 3 つをまとめて有効化するショートカットです。

### パフォーマンス

| オプション | デフォルト | 説明 |
|-----------|-----------|------|
| `-t, --threads <N>` | 自動 | 並列スレッド数 |
| `--chunk-size <N>` | 0 | チャンクサイズ (0 = 一括処理) |
| `-g, --gpu <BOOL>` | true | GPU 処理の有効/無効 |
| `--no-gpu` | - | GPU を無効化 |

### デバッグ

| オプション | 説明 |
|-----------|------|
| `-v, -vv, -vvv` | ログ詳細度 (INFO / DEBUG / TRACE) |
| `-q, --quiet` | 進捗出力を抑制 |
| `--max-pages <N>` | 処理ページ数の上限 |
| `--save-debug` | 中間画像を保存 |

### 設定ファイル

| オプション | 説明 |
|-----------|------|
| `-c, --config <PATH>` | TOML 設定ファイルのパス |

→ 設定ファイルの詳細は [configuration.md](configuration.md) を参照

---

## `markdown` - PDF から Markdown 変換

PDF を OCR し、構造化された Markdown に変換します。

```
superbook-pdf markdown [OPTIONS] <INPUT>
```

### 引数

| 引数 | 説明 |
|------|------|
| `<INPUT>` | 入力 PDF ファイル |

### 出力設定

| オプション | デフォルト | 説明 |
|-----------|-----------|------|
| `-o, --output <DIR>` | `./markdown_output` | 出力先ディレクトリ |
| `--text-direction <DIR>` | auto | テキスト方向 |
| `--include-page-numbers` | off | ページ番号を出力に含める |
| `--generate-metadata` | off | メタデータ JSON を生成 |

**テキスト方向:**

| 値 | 説明 |
|----|------|
| `auto` | 自動検出 |
| `horizontal` | 横書き (左→右、上→下) |
| `vertical` | 縦書き (右→左、上→下) - 日本語書籍向け |

### コンテンツ検出

| オプション | デフォルト | 説明 |
|-----------|-----------|------|
| `--extract-images <BOOL>` | true | 画像抽出 |
| `--no-extract-images` | - | 画像抽出を無効化 |
| `--detect-tables <BOOL>` | true | 表検出 |
| `--no-detect-tables` | - | 表検出を無効化 |
| `--figure-sensitivity <N>` | - | 図検出感度 (0.0-1.0) |

### 画像処理

| オプション | デフォルト | 説明 |
|-----------|-----------|------|
| `--dpi <N>` | 300 | 出力 DPI |
| `--upscale` | off | OCR前にAI超解像を適用 |
| `--deskew <BOOL>` | true | 傾き補正 |
| `--no-deskew` | - | 傾き補正を無効化 |
| `-g, --gpu` | off | GPU 処理を有効化 |

### 品質検証

| オプション | デフォルト | 説明 |
|-----------|-----------|------|
| `--validate` | off | 出力 Markdown の品質検証を有効化 |
| `--api-provider <PROVIDER>` | local | 検証プロバイダー |

**検証プロバイダー:**

| 値 | 説明 | 環境変数 |
|----|------|---------|
| `local` | ローカル検証のみ | - |
| `claude` | Claude API による検証 | `ANTHROPIC_API_KEY` |
| `openai` | OpenAI API による検証 | `OPENAI_API_KEY` |

### 実行制御

| オプション | 説明 |
|-----------|------|
| `--resume` | 中断した処理を途中から再開 |
| `--max-pages <N>` | 処理ページ数の上限 |
| `-v, -vv, -vvv` | ログ詳細度 |
| `-q, --quiet` | 進捗出力を抑制 |

### 出力構造

```
markdown_output/
├── BookTitle.md          # 結合された Markdown
├── pages/
│   ├── page_001.md       # ページ単位の Markdown
│   ├── page_002.md
│   └── ...
└── images/
    ├── cover_001.png     # 表紙画像
    ├── page_003_fig1.png # 図
    └── ...
```

---

## `reprocess` - 失敗ページの再処理

変換中にエラーが発生したページを再処理します。

```
superbook-pdf reprocess [OPTIONS] <INPUT>
```

### 引数

| 引数 | 説明 |
|------|------|
| `<INPUT>` | PDF ファイルまたは `.superbook-state.json` ファイル |

### オプション

| オプション | デフォルト | 説明 |
|-----------|-----------|------|
| `-p, --pages <PAGES>` | - | 再処理するページ (カンマ区切り、0始まり) |
| `--max-retries <N>` | 3 | ページ毎の最大リトライ回数 |
| `-f, --force` | - | 失敗ページを全て強制再処理 |
| `--status` | - | 状態表示のみ (処理なし) |
| `-o, --output <DIR>` | - | 出力ディレクトリ (入力がPDFの場合) |
| `--keep-intermediates` | - | 中間ファイルを保持 |

---

## `info` - システム情報

```
superbook-pdf info
```

GPU の有無、pdftoppm のバージョン、Python 環境の状態など、処理に必要な環境情報を表示します。

---

## `cache-info` - キャッシュ情報

```
superbook-pdf cache-info <OUTPUT_PDF>
```

指定した出力 PDF のキャッシュ情報 (処理パラメータ、タイムスタンプ等) を表示します。
