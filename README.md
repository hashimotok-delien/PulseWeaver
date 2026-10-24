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

Discord とゲームなどをそれぞれ個別のフェーダー／出力先へ振り分ける機能、プロファイル保存、マイクモニター機能はまだありません。現在は選択した再生デバイス上の音声を一括で取り込みます。

NAudio は MIT License のライブラリです。
