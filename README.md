# PulseWeaver

Windows向けの無料オーディオミキサー試作です。システム音声とマイクを個別の音量で調整し、1つの出力デバイスへミックスします。

## 実行

必要なもの:

- Windows 10/11
- .NET 8 SDK
- 仮想オーディオケーブル（例: [VB-CABLE](https://vb-audio.com/Cable/)）

```powershell
dotnet run --project .\PulseWeaver\PulseWeaver.csproj
```

VB-CABLE は公式配布元から別途インストールしてください。このアプリはドライバーを同梱・再配布しません。出力デバイスに CABLE Input、Discord の入力デバイスに CABLE Output を選択します。

## 現在の範囲

- **H/W入力 6系統**（マイク・ライン入力などの録音デバイス）
- **バーチャル入力 3系統**（Windows デフォルト出力 / Discord 出力 / 音声出力先。指定した再生デバイスの音をループバックで取り込み）
- **H/W出力 4系統（A1〜A4）**、入力ごとに 4 出力へ振り分け
- 入力ごとの音量調整と A1〜A4 の振り分けは動作中でも即時反映
- 出力先と入力デバイスが同じになる組み合わせは既定で回避し、開始時にもハウリングの警告を表示
- 一部のデバイスが他アプリに占有されていても、残りのデバイスで動作を継続
- 上部タブ（ミキサー / 設定）で操作と設定を分離
- 設定はジャンル別（一般 / 音声 / 更新情報 / このアプリについて）
- 自動アップデート確認（起動時・24時間に1回）と、アプリからのインストール
- 更新情報に過去のリリース履歴を表示

プロファイル（複数構成の保存・切替）とマイクモニター機能はまだありません。

NAudio は MIT License のライブラリです。

## アップデート

起動時に24時間以上経過していれば、GitHub のリリースを自動確認します（設定 > 一般 で無効化可能）。新しいバージョンがある場合はミキサー画面に案内が出て、設定 > 更新情報 から次の操作ができます。

- **最新かどうかを調べる** … 現在のバージョンと最新リリースを比較
- **アップデートする** … インストーラーをダウンロード（SHA-256 検証あり）して起動し、アプリを終了
- **リリースページを開く** … ブラウザーで GitHub のリリースを開く

インストーラー版で導入している場合、そのまま上書き更新されます。更新情報には過去のリリース履歴も表示され、各行をクリックするとリリースページを開きます。

## 設定とログの保存先

- 設定: `%LOCALAPPDATA%\PulseWeaver\settings.json`
- ログ: `%LOCALAPPDATA%\PulseWeaver\logs\app-<日付>.log`（設定 > このアプリについて から開けます）

音声が開始できない場合は、ログに失敗したデバイス名と HRESULT（例: `0x8889000A` = デバイスが排他使用中）が記録されます。

## 配布物のビルド

GitHub Actions の `.github/workflows/release.yml` が、タグ push（例: `v0.1.0`）でインストーラと ZIP を作成してリリースします。ローカルで作る場合:

```powershell
dotnet publish .\PulseWeaver\PulseWeaver.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:Version=0.1.0 -o dist\publish
iscc /DVERSION=0.1.0 .\installer\PulseWeaver.iss
```

インストーラは [Inno Setup 6](https://jrsoftware.org/isdl.php) でビルドします（商用配布にはライセンス購入が必要です）。
