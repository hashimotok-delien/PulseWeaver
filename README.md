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

- 選択した再生デバイスのループバック音声とマイク入力をミックス
- 入力ごとの音量調整
- 選択した再生デバイスへミックスを出力
- Windows の音量ミキサーでアプリごとに再生先を設定すれば、選択した再生デバイスに届く音声をまとめて取り込めます
- 上部タブ（ミキサー / 設定）で操作と設定を分離
- 設定はジャンル別（一般 / 音声 / 更新情報 / このアプリについて）
- 自動アップデート確認（起動時・24時間に1回）と、アプリからのインストール

Discord とゲームなどをそれぞれ個別のフェーダー／出力先へ振り分ける機能、プロファイル保存、マイクモニター機能はまだありません。現在は選択した再生デバイス上の音声を一括で取り込みます。

NAudio は MIT License のライブラリです。

## アップデート

起動時に24時間以上経過していれば、GitHub のリリースを自動確認します（設定 > 一般 で無効化可能）。新しいバージョンがある場合はミキサー画面に案内が出て、設定 > 更新情報 から次の操作ができます。

- **最新かどうかを調べる** … 現在のバージョンと最新リリースを比較
- **アップデートする** … インストーラーをダウンロード（SHA-256 検証あり）して起動し、アプリを終了
- **リリースページを開く** … ブラウザーで GitHub のリリースを開く

インストーラー版で導入している場合、そのまま上書き更新されます。

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
