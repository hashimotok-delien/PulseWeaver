# 商品サイト テンプレート（`templates/product/`）

`public/cultivate/` の構成をそのまま流用した、新しい商品（Bot / SaaS / ツールなど）用の
公式サイトひな形です。**このフォルダ自体は公開されません**（`public/` の外にあるため FTP 対象外）。

コピーして `public/<slug>/` に置けば、そのまま動く状態にしてあります。

---

## 1. 構成

```
templates/product/
├── README.md                  … このファイル
├── index.php                  … ランディングページ（自己完結型・インラインCSS）
├── privacy.php                … プライバシーポリシー（要 文面作成）
├── terms.php                  … 利用規約（要 文面作成）
├── plans/
│   ├── index.php              … プラン一覧／比較／契約の流れ／FAQ
│   ├── success.php            … 決済成功ページ（noindex）
│   ├── cancel.php             … 決済キャンセルページ（noindex）
│   └── css/
│       └── style.css          … プラン・法務ページ共通のデザインシステム
└── assets/
    ├── include/
    │   ├── config.php         … 商品名・URL・著作権表記などの一元管理
    │   ├── header.php         … 共通ヘッダー
    │   └── footer.php         … 共通フッター
    └── img/                   … アイコン・スクリーンショット置き場（空）
```

| ページ | URL（コピー後） | スタイル |
|---|---|---|
| `index.php` | `/product-name` (`/product-name/`) | ページ内にインライン `<style>` |
| `plans/index.php` | `/product-name/plans/` | `plans/css/style.css` |
| `plans/success.php` | `/product-name/plans/success` | 〃 |
| `plans/cancel.php` | `/product-name/plans/cancel` | 〃 |
| `privacy.php` | `/product-name/privacy` | `plans/css/style.css` + 最小限の追記 |
| `terms.php` | `/product-name/terms` | `plans/css/style.css` + 最小限の追記 |

---

## 2. 新規商品を追加する手順

### (1) フォルダをコピー

```powershell
Copy-Item templates\product public\<slug> -Recurse
```

`<slug>` は URL に出る英小文字の名前（例: `cultivate`, `magonote-bot`）。

### (2) `assets/include/config.php` を書き換える

ここだけで商品名・リンクが全ページに反映されます。

| 変数 | 内容 |
|---|---|
| `$PRODUCT_NAME` | 表示名（例: `Cultivate`） |
| `$PRODUCT_EMOJI` | ロゴ横の絵文字（不要なら `''`） |
| `$PRODUCT_SLUG` | フォルダ名。ランディングのページ遷移アニメの判定に使用 |
| `$LOGO_LABEL` | ヘッダーロゴのラベル（`Bot` / `Premium` など。各ページで上書き可） |
| `$DOCS_URL` | ドキュメントサイトの URL（別ドメインなら絶対URL） |
| `$SUPPORT_URL` | サポート窓口（Discord 招待など） |
| `$ICON_PATH` | アイコン画像への相対パス |
| `$ACCENT_RGB` | アクセント色の `R,G,B`（JS の遷移アニメ用） |
| `$SITE_OWNER` | `<title>` の `\|` 以降（例: `けい🤖`） |
| `$COPYRIGHT_HOLDER` / `$COPYRIGHT_URL` | フッターの著作権表記 |

> **注意:** `config.php` は必ず「何も出力しない」状態に保つこと。
> 各ページが `<!DOCTYPE html>` より前に `require` しているため、閉じタグ後の空白や改行でも
> 出力が始まるとレイアウトが崩れます（末尾に `?>` を書かないのが安全）。

### (3) 本文の `（TODO: ...）` を埋める

- `index.php` … キャッチコピー、できること、導入メリット、導入手順、使用イメージ
- `plans/index.php` … プラン名・価格・特典・比較表・契約の流れ・FAQ
- `plans/success.php` / `plans/cancel.php` … 決済後の案内文
- `privacy.php` / `terms.php` … **法務文面は必ず自作する**（テンプレート文のまま公開しない）

### (4) 画像を自前配信する

`assets/img/` に置き、`index.php` からは `./assets/img/...` で参照します。

```
assets/img/icon.webp          … ヒーローのアイコン（120px 表示 / 400px 程度を用意）
assets/img/screenshot-1.webp  … 使用イメージ
assets/img/screenshot-2.webp
```

Gyazo などの外部URLを直接貼るのは避けてください（リンク切れ・外部依存・表示遅延の原因）。

### (5) ポートフォリオ／ドキュメントからリンクする

- `public/index.php`（ポートフォリオ）の該当カードに `href="./<slug>/"` を追加
- ドキュメントサイトがある場合は `docs/<slug>/` を作成し、`docs/index.php` のポータルにも追加
- 内部リンクは**拡張子なし**で書く（`.php` は付けない。フォルダのトップは `./` か `<slug>/`）

### (6) フォントのサブセット対象に追加

`tools/build_fonts.py` の `PUBLIC_PAGES` に新ページを追加しないと、そのページだけ
文字がフォントに含まれず別フォントで表示されます。

```python
PUBLIC_PAGES = [
    "index.php",
    "calendar.php",
    "cultivate/index.php",
    "<slug>/index.php",        # ← 追加
    "<slug>/plans/index.php",  # ← 追加
    ...
]
```

追加後に `python tools/build_fonts.py` を実行して `public/assets/fonts/` を再生成します。

### (7) エージェント定義の構成図を更新

`.github/agents/navi.agent.md` の「全体構成」にフォルダを追記しておくと、
以後の作業で構成の把握がぶれません。

---

## 3. 気をつけること（重要）

### URL・パス

- **拡張子なしURLが正規形。** `public/.htaccess` が `/foo.php` → `/foo` に301し、
  拡張子なしアクセスは内部で `foo.php` を実行します。`/foo/index.php` は `/foo/` に集約。
  - 内部リンクは `terms`、`plans/`、`./` のように書く。`terms.php` は書かない。
- **`$BASE` はページごとに違う。**
  - `privacy.php` / `terms.php`（商品ルート）… `$BASE = '.'`
  - `plans/*.php`（1階層下）… `$BASE = '..'`
  - `header.php` / `footer.php` は `<?= $BASE ?? '.' ?>` を使うので、**include 前に必ず代入する**
- **このテンプレートはドキュメントルート直下の1階層（`public/<slug>/`）前提。**
  `index.php` の favicon が `../favicon.png`、フォントが `../assets/fonts/` なのはそのため。
  さらに深い階層に置く場合はパスを全部見直すこと。
- 法務ページの CSS は `plans/css/style.css` を**相対パス**で読んでいる（`plans/css/style.css`）。
  商品ルートからの相対で書く。

### include の扱い

- `assets/include/` はサーバーサイド専用。`public/.htaccess` の
  `RewriteRule (^|/)include(/|$) - [F,L]` で直接アクセスが 403 になります。
  設定ファイルを置く場所としても安全ですが、**必ず `include/` の中に置く**こと。
- include は PHP の `include` で行う（JS で読み込まない）。
- `header.php` / `footer.php` は `require_once __DIR__ . '/config.php';` で設定を自己完結して
  読み込むので、ページ側で二重に require しても問題ありません。

### CSS

- **アクセント色は `#00e5ff`。** テーマカラーを勝手に変えない。
- ハードコードせず、カスタムプロパティを使う。
  - `index.php` のインラインCSS … `:root { --accent; --accent-rgb }`
  - `plans/css/style.css` … `:root { --bg-primary; --accent; --border; --radius; ... }`
- 半透明は `rgba(var(--accent-rgb), .15)` の形で書く。
  **`--accent` を変えたら `--accent-rgb`（と `config.php` の `$ACCENT_RGB`）も必ず揃える。**
- `plans/css/style.css` は `privacy.php` / `terms.php` と共有。編集すると3ページに影響する。
  法務ページ固有の見た目は各ページの `<style>` に閉じ込める。

### キャッシュ破棄

- CSS は `?v=<?= @filemtime(__DIR__ . '/css/style.css') ?: time() ?>` の形で読み込む。
  更新日時がクエリに入るので、ファイルを差し替えるだけでブラウザキャッシュが切れる。
- 画像はこの仕組みの対象外。**差し替えたらファイル名を変える**（または `?v=` を手で付ける）。

### SEO・メタ情報

- 各ページに `<title>` と `<meta name="description">` を必ず書く。
- ランディングには OGP / Twitter カード（`og:image` は 1200x630 の絶対URL）を設定する。
- **`plans/success.php` / `plans/cancel.php` は `noindex` 必須**
  （決済フローの結果ページを検索結果に出さない）。
- 新しいページを公開したら、サイトマップや `robots.txt` を運用している場合は追加する。

### 法務

- `privacy.php` / `terms.php` は**雛形**。公開前に必ず自サービス用に書き直す。
- 有料プランを提供する場合は、特定商取引法に基づく表記（事業者名・連絡先・支払時期など）も必要。
- 決済事業者（Stripe など）の名前・データの取扱いはプライバシーポリシーに明記する。

### アクセシビリティ・表示

- `index.php` の `section` は IntersectionObserver でフェードインする。
  `prefers-reduced-motion: reduce` の環境ではアニメーションを止めているので、消さないこと。
- 画像には必ず `alt` を書く（スクリーンショットは内容が分かる説明にする）。
- モバイル（〜600px）で文字サイズ・余白が崩れないか確認する。

### 壊しやすい箇所

- `index.php` の**ページ遷移アニメ**（`#page-transition`）は、`a` 要素のクリックを全部
  横取りして 500ms 後に遷移させます。`target="_blank"` と `#` 始まりのリンクは除外済み。
  リンクを増やすときはこの条件に注意（外部リンクには `target="_blank" rel="noopener"`）。
- `$PRODUCT_SLUG` は遷移アニメの色分け判定に使うので、**フォルダ名と一致させる**。

---

## 4. 動作確認

```powershell
# 構文チェック
Get-ChildItem templates\product -Recurse -Filter *.php | ForEach-Object { php -l $_.FullName }

# ローカルプレビュー（public をドキュメントルートにする）
php -S localhost:8000 -t public
# → http://localhost:8000/<slug>/ を開く
```

- [ ] PHP の Warning / Fatal が出ていない（`@filemtime` は失敗時に `time()` にフォールバック）
- [ ] アイコン・スクリーンショット・favicon が 404 になっていない
- [ ] ヘッダーのロゴ／ナビ、フッターの利用規約リンクが正しい階層を指している
- [ ] モバイル幅でレイアウトが崩れていない
- [ ] `plans/success` / `plans/cancel` に `noindex` が入っている

---

## 5. 参考

- 実例: `public/cultivate/`（このテンプレートの元）
- デザインの基準: `docs/cultivate/assets/css/style.css`（ドキュメントサイト共通のカラースキーム）
- フォント生成: `tools/build_fonts.py`
