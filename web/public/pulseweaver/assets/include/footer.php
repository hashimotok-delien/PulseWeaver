<?php
/**
 * 共通フッター
 *
 * 呼び出し側で事前に定義できる変数:
 *   $BASE        … 商品ルートへの相対パス
 *   $SHOW_TERMS  … true で利用規約リンクを出す
 */
require_once __DIR__ . '/config.php';
?>
<footer class="site-footer">
  <p>© <?= date('Y') ?> <a href="<?= $COPYRIGHT_URL ?>"><?= $COPYRIGHT_HOLDER ?></a> — <?= $PRODUCT_NAME ?><?php if ($SHOW_TERMS ?? false): ?> ／ <a href="<?= $BASE ?? '.' ?>/terms">利用規約</a><?php endif; ?></p>
</footer>
