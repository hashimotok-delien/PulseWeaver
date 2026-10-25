<?php
/**
 * PulseWeaver 共通設定
 *
 * ここだけ書き換えれば全ページに反映される。
 * assets/include/ は public/.htaccess の `RewriteRule (^|/)include(/|$) - [F,L]`
 * で直接アクセス禁止になっているため、設定ファイルの置き場所として安全。
 *
 * 注意: このファイルは出力を一切してはいけない（各ページの <!DOCTYPE html> より前に
 *       require するため、空白や改行を出すとレイアウトが崩れる）。
 */

// 商品の表示名
$PRODUCT_NAME = 'PulseWeaver';

// ロゴ横に出す絵文字（不要なら ''）
$PRODUCT_EMOJI = '🎚️';

// 公式サイトのフォルダ名。ランディングのページ遷移アニメの判定に使う
$PRODUCT_SLUG = 'pulseweaver';

// ヘッダーロゴのラベル（Bot / Premium など）。各ページで上書き可
$LOGO_LABEL ??= 'Mixer';

// ドキュメントサイト（README・使い方）
$DOCS_URL = 'https://github.com/hashimotok-delien/PulseWeaver#readme';

// ダウンロード（リリース一覧）
$DOWNLOAD_URL = 'https://github.com/hashimotok-delien/PulseWeaver/releases';

// サポート窓口（GitHub Issues）
$SUPPORT_URL = 'https://github.com/hashimotok-delien/PulseWeaver/issues';

// ヘッダーのアイコン画像。assets/img/ に置き、商品ルートからの相対パスで書く
$ICON_PATH = './assets/img/icon.png';

// アクセント色の RGB 値。「R,G,B」形式で、CSS の rgba(var(--accent-rgb), .x) と揃える
$ACCENT_RGB = '0,229,255';

// <title> の「|」以降に出すサイト名
$SITE_OWNER = 'けい🤖';

// フッターの著作権表記
$COPYRIGHT_HOLDER = 'けい';
$COPYRIGHT_URL = 'https://hmk.delien.xyz/';