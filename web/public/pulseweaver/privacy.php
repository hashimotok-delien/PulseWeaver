<?php
/**
 * プライバシーポリシー — テンプレート
 * テーマはプランページと共通の plans/css/style.css を流用し、
 * 法務ページ用の最小限のスタイルだけ <style> に足す。
 *
 * 注意: 文面は必ず自サービスに合わせて書き直すこと（テンプレートのまま公開しない）。
 */
require __DIR__ . '/assets/include/config.php';
?>
<!DOCTYPE html>
<html lang="ja">
<head>
  <meta charset="UTF-8">
  <meta name="viewport" content="width=device-width, initial-scale=1.0">
  <title>プライバシーポリシー - <?= $PRODUCT_NAME ?></title>
  <meta name="description" content="<?= $PRODUCT_NAME ?> のプライバシーポリシーです。取得する情報・利用目的・第三者提供について定めています。">
  <link rel="stylesheet" href="plans/css/style.css?v=<?= @filemtime(__DIR__ . '/plans/css/style.css') ?: time() ?>">
  <style>
    .legal {
      max-width: 860px;
      margin: 0 auto;
      padding: 48px 24px 24px;
    }

    .legal-meta {
      font-size: 0.85rem;
      color: var(--text-muted);
      margin-bottom: 8px;
    }

    .legal-nav {
      background: var(--bg-card);
      border: 1px solid var(--border);
      border-radius: var(--radius);
      padding: 20px 24px;
      margin: 28px 0 40px;
    }

    .legal-nav-title {
      font-size: 0.9rem;
      font-weight: 600;
      color: var(--accent);
      margin-bottom: 10px;
    }

    .legal-nav ol {
      list-style: none;
      padding: 0;
      margin: 0;
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(260px, 1fr));
      gap: 4px 24px;
    }

    .legal-nav a {
      font-size: 0.88rem;
      color: var(--text-secondary);
    }
    .legal-nav a:hover { color: var(--accent); }

    .legal-section {
      margin-bottom: 40px;
      scroll-margin-top: 90px;
    }

    .legal-section h2 {
      font-size: 1.25rem;
      margin-bottom: 14px;
    }

    .legal-section h3 {
      margin: 20px 0 8px;
      color: var(--text-primary);
    }

    .legal-section p { margin-bottom: 12px; }

    .legal-section ul,
    .legal-section ol.legal-list {
      margin: 0 0 12px;
      padding-left: 24px;
      color: var(--text-secondary);
    }

    .legal-section li { margin-bottom: 6px; }

    .legal-section ol.legal-list {
      list-style: decimal;
    }

    .legal-section ol.legal-list > li::marker {
      color: var(--accent);
      font-weight: 600;
    }

    .legal-section ul > li::marker {
      color: var(--accent);
    }

    @media (max-width: 720px) {
      .legal { padding: 32px 16px 16px; }
    }
  </style>
</head>
<body>

  <?php $BASE = '.'; $LOGO_LABEL = 'Bot'; ?>
  <?php include __DIR__ . '/assets/include/header.php'; ?>

  <main>
    <div class="legal">
      <h1>プライバシーポリシー</h1>
      <p class="legal-meta">制定日：2026年10月25日／最終改定日：2026年10月25日</p>
      <p>
        本プライバシーポリシー（以下「本ポリシー」といいます）は、
        <?= $COPYRIGHT_HOLDER ?>（以下「運営」といいます）が提供する
        <?= $PRODUCT_NAME ?>（以下「本サービス」といいます）における、
        利用者に関する情報の取扱いを定めるものです。
      </p>
      <p>
        本ポリシーは、<a href="terms">利用規約</a> に基づく個別規定として定めるものです。
        本サービスを利用するすべての利用者（以下「利用者」といいます）は、
        本ポリシーに同意したものとみなします。
      </p>

      <nav class="legal-nav" aria-label="目次">
        <div class="legal-nav-title">目次</div>
        <ol>
          <li><a href="#art1">第1条（取得する情報）</a></li>
          <li><a href="#art2">第2条（利用目的）</a></li>
          <li><a href="#art3">第3条（第三者提供・外部サービスの利用）</a></li>
          <li><a href="#art4">第4条（保存期間および削除）</a></li>
          <li><a href="#art5">第5条（開示・訂正・削除等の請求）</a></li>
          <li><a href="#art6">第6条（本ポリシーの変更）</a></li>
          <li><a href="#art7">第7条（お問い合わせ）</a></li>
        </ol>
      </nav>

      <!-- ===== 第1条 ===== -->
      <section class="legal-section" id="art1">
        <h2>第1条（取得する情報）</h2>
        <p>運営は、本サービスの提供に必要な範囲で、次の情報を取得・保存します。</p>
        <ol class="legal-list">
          <li>アプリ内で設定した情報（音声デバイス名、音量、出力への振り分け、構成名など）。これらは利用者の端末内にのみ保存されます。</li>
          <li>アップデート確認時に GitHub へ送信される通信情報（リクエストに含まれる IP アドレスやユーザーエージェントなど）。運営が独自のサーバーで受け取ることはありません。</li>
        </ol>
      </section>

      <!-- ===== 第2条 ===== -->
      <section class="legal-section" id="art2">
        <h2>第2条（利用目的）</h2>
        <p>運営は、取得した情報を次の目的で利用します。</p>
        <ul>
          <li>前回終了時の設定を復元し、同じ構成で利用できるようにするため</li>
          <li>新しいバージョンの有無を確認し、更新を案内するため</li>
        </ul>
      </section>

      <!-- ===== 第3条 ===== -->
      <section class="legal-section" id="art3">
        <h2>第3条（第三者提供・外部サービスの利用）</h2>
        <p>本アプリは決済機能を持たず、アカウント登録も不要です。アップデートの確認および配布物のダウンロードには GitHub（GitHub, Inc.）のサービスを利用しており、同社のプライバシーポリシーが適用されます。また、音声の送出先として VB-CABLE などの仮想オーディオデバイスを利用する場合、当該ソフトウェアの提供元の定めに従います。これらは本アプリに同梱されておらず、利用者自身で導入するものです。</p>
      </section>

      <!-- ===== 第4条 ===== -->
      <section class="legal-section" id="art4">
        <h2>第4条（保存期間および削除）</h2>
        <p>設定情報は利用者の端末内にのみ保存され、アプリのアンインストール、または <code>%LOCALAPPDATA%\PulseWeaver</code> フォルダの削除により消去されます。運営が取得・保管する情報はありません。</p>
      </section>

      <!-- ===== 第5条 ===== -->
      <section class="legal-section" id="art5">
        <h2>第5条（開示・訂正・削除等の請求）</h2>
        <p>本アプリは運営が利用者の個人情報を保有しないため、開示・訂正・削除の対象となる情報はありません。端末内の設定情報は、利用者ご自身でいつでも削除できます。ご不明な点は下記の窓口へお問い合わせください。</p>
      </section>

      <!-- ===== 第6条 ===== -->
      <section class="legal-section" id="art6">
        <h2>第6条（本ポリシーの変更）</h2>
        <p>本ポリシーを変更する場合は、本ページにて公表します。変更後の内容は公表した時点から適用されます。</p>
      </section>

      <!-- ===== 第7条 ===== -->
      <section class="legal-section" id="art7">
        <h2>第7条（お問い合わせ）</h2>
        <p>
          本ポリシーに関するお問い合わせは、
          <a href="<?= $SUPPORT_URL ?>" target="_blank" rel="noopener">GitHub Issues</a>
          までお願いします。
        </p>
      </section>
    </div>
  </main>

  <?php include __DIR__ . '/assets/include/footer.php'; ?>

</body>
</html>

