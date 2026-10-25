<?php
/**
 * 商品ランディングページ — テンプレート
 *
 * 構成: 自己完結型（CSS はこのファイル内の <style> にインラインで持つ）
 * 　　　商品名・URL は assets/include/config.php に集約
 *
 * 置き場所の前提: public/<slug>/index.php（＝ドキュメンルート直下の1階層）
 * そのため favicon やフォントは ../ で参照している。
 */
require __DIR__ . '/assets/include/config.php';
?>
<!DOCTYPE html>
<html lang="ja">
<head>
<meta charset="UTF-8">
<meta name="viewport" content="width=device-width, initial-scale=1.0">
<title><?= $PRODUCT_NAME ?> | <?= $SITE_OWNER ?></title>
<meta name="description" content="Windows向けの無料オーディオミキサー。H/W入力6・バーチャル入力3をミックスし、4つの出力へ振り分け。DiscordやOBSへ音声を分けて届けられます。">
<meta property="og:type" content="website">
<meta property="og:title" content="<?= $PRODUCT_NAME ?> | <?= $SITE_OWNER ?>">
<meta property="og:description" content="9入力を4出力へ自由に振り分ける、Windows向けの無料オーディオミキサー。入力と出力それぞれの音量調整、構成の保存・切替、自動更新に対応。">
<meta property="og:image" content="https://hmk.delien.xyz/pulseweaver/assets/img/og.png">
<meta name="twitter:card" content="summary_large_image">

<link rel="icon" type="image/png" sizes="32x32" href="../favicon.png">
<link rel="icon" type="image/png" sizes="16x16" href="../favicon.png">
<link rel="shortcut icon" href="../favicon.ico">
<link rel="apple-touch-icon" href="../favicon.png">

<style>
/* ============================================================
   デザイントークン
   アクセント色を変えるときは --accent と --accent-rgb の両方を更新する
   （rgba(var(--accent-rgb), .x) の形で半透明に使っているため）
   ============================================================ */
:root{
  --bg:#0a0a0f;
  --text:#e0e0e0;
  --accent:#00e5ff;
  --accent-rgb:0,229,255;
  --accent-2:#00bcd4;
  --card-bg:rgba(18,18,26,0.7);
  --radius:12px;
}

/* セルフホストフォント（日本語サブセット）
   フォントを差し替えたら tools/build_fonts.py で再生成する */
@font-face{
  font-family:'Noto Sans JP';
  src:url("../assets/fonts/noto-sans-jp-400.woff2?v=<?= @filemtime(__DIR__ . '/../assets/fonts/noto-sans-jp-400.woff2') ?: time() ?>") format("woff2");
  font-weight:400;
  font-style:normal;
  font-display:swap;
}
@font-face{
  font-family:'Noto Sans JP';
  src:url("../assets/fonts/noto-sans-jp-600.woff2?v=<?= @filemtime(__DIR__ . '/../assets/fonts/noto-sans-jp-600.woff2') ?: time() ?>") format("woff2");
  font-weight:600;
  font-style:normal;
  font-display:swap;
}
@font-face{
  font-family:'Noto Sans JP';
  src:url("../assets/fonts/noto-sans-jp-700.woff2?v=<?= @filemtime(__DIR__ . '/../assets/fonts/noto-sans-jp-700.woff2') ?: time() ?>") format("woff2");
  font-weight:700;
  font-style:normal;
  font-display:swap;
}
@font-face{
  font-family:'Orbitron';
  src:url("../assets/fonts/orbitron.woff2?v=<?= @filemtime(__DIR__ . '/../assets/fonts/orbitron.woff2') ?: time() ?>") format("woff2");
  font-weight:400 900;
  font-style:normal;
  font-display:swap;
}

body{
  margin:0;
  font-family:'Noto Sans JP',sans-serif;
  background:var(--bg);
  color:var(--text);
  overflow-x:hidden;
}

/* 背景アニメ */
body::before{
  content:"";
  position:fixed;
  width:200%;
  height:200%;
  background:
    radial-gradient(circle at 20% 20%, rgba(var(--accent-rgb),0.15), transparent),
    radial-gradient(circle at 80% 80%, rgba(var(--accent-rgb),0.1), transparent);
  animation:moveBg 15s linear infinite alternate;
  z-index:-1;
  pointer-events:none;
}

@keyframes moveBg{
  from{transform:translate(0,0);}
  to{transform:translate(-80px,-80px);}
}

/* ノイズっぽい線（IT感） */
body::after{
  content:"";
  position:fixed;
  width:100%;
  height:100%;
  background:linear-gradient(rgba(255,255,255,0.02) 1px, transparent 1px);
  background-size:100% 3px;
  pointer-events:none;
}

/* ヒーロー */
header{
  height:100vh;
  display:flex;
  flex-direction:column;
  justify-content:center;
  align-items:center;
  text-align:center;
  gap:10px;
}

header img{
  width:120px;
  border-radius:20px;
  animation:float 4s ease-in-out infinite;
  box-shadow:0 0 30px rgba(var(--accent-rgb),0.7);
}

@keyframes float{
  0%{transform:translateY(0);}
  50%{transform:translateY(-15px);}
  100%{transform:translateY(0);}
}

h1{
  font-family:'Orbitron';
  font-size:2.8rem;
  text-shadow:0 0 20px rgba(var(--accent-rgb),0.6);
}

.tagline{
  color:var(--accent);
  font-size:1.2rem;
}

.benefit{
  opacity:0.8;
}

/* ボタン */
.btn{
  display:inline-block;
  padding:14px 28px;
  background:linear-gradient(45deg,var(--accent),var(--accent-2));
  color:#000;
  border-radius:var(--radius);
  font-weight:bold;
  text-decoration:none;
  transition:0.3s;
}

.btn:hover{
  transform:scale(1.08);
  box-shadow:0 0 30px rgba(var(--accent-rgb),0.8);
}

/* セクション */
section{
  max-width:1000px;
  margin:auto;
  padding:100px 20px;
  opacity:0;
  transform:translateY(40px);
  transition:0.8s;
}

section.active{
  opacity:1;
  transform:translateY(0);
}

/* JS 無効時でも内容が消えないようにする */
@media (prefers-reduced-motion: reduce){
  section{ opacity:1; transform:none; transition:none; }
  body::before, header img{ animation:none; }
}

h2{
  border-left:4px solid var(--accent);
  padding-left:10px;
  margin-bottom:30px;
}

/* グリッド */
.grid{
  display:grid;
  grid-template-columns:repeat(auto-fit,minmax(250px,1fr));
  gap:25px;
}

/* カード */
.card{
  background:var(--card-bg);
  backdrop-filter:blur(15px);
  padding:25px;
  border-radius:18px;
  transition:0.3s;
  border:1px solid rgba(var(--accent-rgb),0.1);
}

.card:hover{
  transform:translateY(-8px) scale(1.02);
  box-shadow:0 0 30px rgba(var(--accent-rgb),0.25);
}

.category{
  color:var(--accent);
  margin-bottom:10px;
  font-weight:bold;
}

/* 画像 */
.card img{
  width:100%;
  border-radius:var(--radius);
  margin-top:10px;
  transition:0.3s;
}

.card img:hover{
  transform:scale(1.02);
}

/* 固定CTA */
.fixed-cta{
  position:fixed;
  bottom:20px;
  right:20px;
  z-index:999;
}

/* ポートフォリオへ戻る */
.back-link{
  position:fixed;
  top:20px;
  left:20px;
  z-index:1000;
  display:inline-flex;
  align-items:center;
  gap:6px;
  padding:10px 18px;
  border:1px solid rgba(var(--accent-rgb),0.45);
  border-radius:999px;
  background:rgba(10,10,15,0.75);
  backdrop-filter:blur(10px);
  color:var(--text);
  font-size:0.9rem;
  font-weight:600;
  text-decoration:none;
  transition:0.3s;
}

.back-link:hover{
  background:rgba(var(--accent-rgb),0.15);
  box-shadow:0 0 20px rgba(var(--accent-rgb),0.4);
  transform:translateX(-2px);
}

@media (max-width:600px){
  .back-link{
    top:12px;
    left:12px;
    padding:8px 14px;
    font-size:0.8rem;
  }
}

/* スクロールバー */
::-webkit-scrollbar{
  width:8px;
}
::-webkit-scrollbar-thumb{
  background:var(--accent);
  border-radius:10px;
}

#progress-bar{
  position:fixed;
  top:0;
  left:0;
  height:3px;
  width:0%;
  background:var(--accent);
  z-index:9999;
  box-shadow:0 0 10px var(--accent);
}

#page-transition{
  position:fixed;
  width:100%;
  height:100%;
  top:0;
  left:0;
  background:var(--bg);
  transform:scaleY(0);
  transform-origin:bottom;
  z-index:99999;
  filter:blur(10px);
  pointer-events:none;
  transition:transform 0.5s ease, filter 0.5s ease;
}

@media (max-width:600px){
  h1{ font-size:2rem; }
  section{ padding:70px 16px; }
}
</style>
</head>
<body>
<div id="page-transition"></div>
<div id="progress-bar"></div>

<a class="back-link" href="../">← ポートフォリオに戻る</a>

<header>
  <picture>
    <source srcset="./assets/img/icon-120.webp?v=<?= @filemtime(__DIR__ . '/assets/img/icon-120.webp') ?: time() ?> 1x, ./assets/img/icon.webp?v=<?= @filemtime(__DIR__ . '/assets/img/icon.webp') ?: time() ?> 2x" type="image/webp">
    <img src="./assets/img/icon.png?v=<?= @filemtime(__DIR__ . '/assets/img/icon.png') ?: time() ?>" alt="<?= $PRODUCT_NAME ?> のアイコン" width="120" height="120" decoding="async">
  </picture>
  <h1><?= $PRODUCT_NAME ?></h1>
  <div class="tagline">音を混ぜて、好きな場所へ。</div>
  <div class="benefit">H/W入力6・バーチャル入力3・出力4。Windowsで動く無料のオーディオミキサーです。</div>
  <a href="<?= $DOWNLOAD_URL ?>" class="btn" target="_blank" rel="noopener">ダウンロード</a>
</header>

<main>

<!-- ===== 何ができる？ ===== -->
<section>
  <h2>💡 何ができる？</h2>
  <div class="grid">
    <div class="card">
      <div class="category">マトリクスミックス</div>
      <p>H/W入力6・バーチャル入力3の計9系統を、H/W出力A1〜A4へ自由に振り分け。入力ごとの音量、出力ごとの音量をそれぞれ個別に調整できます。</p>
    </div>
    <div class="card">
      <div class="category">バーチャル入力</div>
      <p>Windowsの既定出力やDiscordの出力など、指定した再生デバイスに流れている音をループバックで取り込みます。アプリごとに再生先を分ければ、ゲームと通話を別々にミックスできます。</p>
    </div>
    <div class="card">
      <div class="category">自動化と構成</div>
      <p>起動時の自動ミックス、Windows起動時の自動起動、タスクトレイ常駐に対応。用途別の構成をJSONで保存でき、選ぶだけで即時に切り替わります。</p>
    </div>
  </div>
</section>

<!-- ===== なぜ使う？ ===== -->
<section>
  <h2>🚀 なぜ使う？</h2>
  <div class="grid">
    <div class="card">🔊 <strong>複数の出力へ同時配信</strong><br>監視用ヘッドホンと配信用ケーブルを分けたまま、同じ音を同時に出せます。</div>
    <div class="card">⚡ <strong>動作中に調整できる</strong><br>音量と A1〜A4 の振り分けはミックス中でも即時反映。止め直す必要はありません。</div>
    <div class="card">💾 <strong>構成をすぐ切り替え</strong><br>「ゲーム配信」「Discord通話」などの構成を保存し、一覧から選ぶだけで切替できます。</div>
    <div class="card">🔄 <strong>無料・自動更新</strong><br>追加のランタイム不要。24時間に1回アップデートを確認し、アプリからそのまま更新できます。</div>
  </div>
</section>

<!-- ===== 導入方法 ===== -->
<section>
  <h2>📥 導入方法</h2>
  <div class="card">
    <p>リリースページからインストーラー（またはポータブルZIP）をダウンロードして実行するだけ。Discordなどへ音声を送る場合は、VB-CABLE などの仮想オーディオデバイスを別途用意してください。</p>
    <div style="display:flex; gap:12px; flex-wrap:wrap; margin-top:6px;">
      <a href="<?= $DOWNLOAD_URL ?>" class="btn" target="_blank" rel="noopener">⬇ リリースを開く</a>
      <a href="<?= $DOCS_URL ?>" class="btn" style="background:linear-gradient(45deg,#7c4dff,var(--accent));" target="_blank" rel="noopener">📚 使い方を見る</a>
    </div>
  </div>
</section>

<!-- ===== 使用イメージ =====
     スクリーンショットは assets/img/ に置いて自前配信するのが基本。
     外部URL（Gyazo 等）を使うと、リンク切れ・外部依存・遅延の原因になる。 -->
<section>
  <h2>🖼 使用イメージ</h2>

  <div class="card">
    <p><strong>ミキサー画面</strong>：上からH/W入力6・バーチャル入力3・H/W出力4。行ごとにデバイスと音量、右端のA1〜A4で振り分けを指定します。</p>
    <picture>
      <source type="image/webp"
              srcset="./assets/img/screenshot-1-600.webp?v=<?= @filemtime(__DIR__ . '/assets/img/screenshot-1-600.webp') ?: time() ?> 600w,
                      ./assets/img/screenshot-1-900.webp?v=<?= @filemtime(__DIR__ . '/assets/img/screenshot-1-900.webp') ?: time() ?> 900w,
                      ./assets/img/screenshot-1-1200.webp?v=<?= @filemtime(__DIR__ . '/assets/img/screenshot-1-1200.webp') ?: time() ?> 1200w"
              sizes="(max-width: 1060px) calc(100vw - 90px), 900px">
      <img src="./assets/img/screenshot-1.png?v=<?= @filemtime(__DIR__ . '/assets/img/screenshot-1.png') ?: time() ?>" alt="PulseWeaver のミキサー画面。H/W入力・バーチャル入力・H/W出力の一覧と音量スライダーが並んでいる"
           width="1200" height="980" loading="lazy" decoding="async"
           style="width:100%; height:auto; border-radius:12px; box-shadow:0 0 20px rgba(var(--accent-rgb),0.3);">
    </picture>
  </div>

  <div class="card">
    <p><strong>設定・更新情報</strong>：起動時の動作や出力バッファ、構成の保存・切替、アップデートの確認ができます。各リリースの変更内容もその場で確認できます。</p>
    <picture>
      <source type="image/webp"
              srcset="./assets/img/screenshot-2-600.webp?v=<?= @filemtime(__DIR__ . '/assets/img/screenshot-2-600.webp') ?: time() ?> 600w,
                      ./assets/img/screenshot-2-900.webp?v=<?= @filemtime(__DIR__ . '/assets/img/screenshot-2-900.webp') ?: time() ?> 900w,
                      ./assets/img/screenshot-2-1200.webp?v=<?= @filemtime(__DIR__ . '/assets/img/screenshot-2-1200.webp') ?: time() ?> 1200w"
              sizes="(max-width: 1060px) calc(100vw - 90px), 900px">
      <img src="./assets/img/screenshot-2.png?v=<?= @filemtime(__DIR__ . '/assets/img/screenshot-2.png') ?: time() ?>" alt="PulseWeaver の設定画面。一般・音声・構成・更新情報・このアプリについてのメニューがある"
           width="1200" height="980" loading="lazy" decoding="async"
           style="width:100%; height:auto; border-radius:12px; box-shadow:0 0 20px rgba(var(--accent-rgb),0.3);">
    </picture>
  </div>
</section>

<div class="fixed-cta">
  <a href="<?= $DOWNLOAD_URL ?>" class="btn" target="_blank" rel="noopener">ダウンロード</a>
</div>

</main>
<script>
const sections=document.querySelectorAll("section");
const observer=new IntersectionObserver(entries=>{
 entries.forEach(e=>{
   if(e.isIntersecting){
     e.target.classList.add("active");
   }
 });
},{threshold:0.2});
sections.forEach(s=>observer.observe(s));

const productSlug = <?= json_encode($PRODUCT_SLUG) ?>;
const accentRgb = <?= json_encode($ACCENT_RGB) ?>;

document.querySelectorAll("a").forEach(link=>{
  link.addEventListener("click", function(e){
    const href = this.getAttribute("href");

    if(href && !href.startsWith("#") && !this.target){
      e.preventDefault();

      const overlay = document.getElementById("page-transition");

      if(href.includes(productSlug)){
        overlay.style.background =
          "radial-gradient(circle at 20% 20%, rgba(" + accentRgb + ", 0.15), transparent), " +
          "radial-gradient(circle at 80% 80%, rgba(" + accentRgb + ", 0.1), transparent)";
      } else {
        overlay.style.background = "";
      }

      overlay.style.pointerEvents = "auto";
      overlay.style.transformOrigin = "bottom";
      overlay.style.transform = "scaleY(1)";

      setTimeout(()=>{
        window.location = href;
      },500);
    }
  });
});

// プログレスバー
// スクロール範囲は layout 読み取りを伴うため、読み込み時とリサイズ時だけ測る
const progressBar = document.getElementById("progress-bar");
let scrollRange = 0;
let scrollFrame = 0;

const measureScrollRange = ()=>{
  scrollRange = document.documentElement.scrollHeight - window.innerHeight;
};
measureScrollRange();
window.addEventListener("load", measureScrollRange);
window.addEventListener("resize", measureScrollRange, { passive: true });

window.addEventListener("scroll", ()=>{
  if(scrollFrame) return;

  scrollFrame = requestAnimationFrame(()=>{
    scrollFrame = 0;
    const y = window.scrollY;
    progressBar.style.width = (scrollRange > 0 ? (y / scrollRange) * 100 : 0) + "%";
  });
}, { passive: true });
</script>

</body>
</html>


