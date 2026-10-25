<?php
/**
 * 共通ヘッダー（plans / privacy / terms 用）
 *
 * 呼び出し側で事前に定義できる変数:
 *   $BASE        … 商品ルートへの相対パス（直下は '.'、plans/ は '..'）
 *   $LOGO_LABEL  … ロゴ横のラベル（'Bot' / 'Premium' など）
 *
 * スタイルは各ページが plans/css/style.css を読み込む前提。
 */
require_once __DIR__ . '/config.php';
?>
<header class="site-header">
  <div class="header-inner">
    <a class="logo" href="<?= $BASE ?? '.' ?>/"><?= $PRODUCT_EMOJI ?><?= $PRODUCT_NAME ?> <span><?= $LOGO_LABEL ?></span></a>
    <nav class="header-nav">
      <a href="<?= $DOCS_URL ?>">使い方</a>
      <a href="<?= $SUPPORT_URL ?>">サポート</a>
    </nav>
  </div>
</header>

