# MovieMaker

静止画（カバー）と音楽ファイルから、YouTube向けの動画を生成するWindows用WPFアプリです。
縦画像は9:16（YouTube Shorts）、横画像は16:9（YouTube）としてエンコードします。
画像はCanva由来の前提で、比率が合わない場合はエラーにします。

## 主な機能
- 画像と音楽をドラッグ&ドロップで指定
- 縦横比で自動判定（9:16 / 16:9のみ許可）
- タイトルは画像ファイル名から自動補完（必要に応じて手動編集）
- 出力先とアーカイブ先を別指定
- ffmpegで高音質エンコード（AAC 320kbps / 48kHz）
- GPU対応エンコーダが利用可能なら自動で使用（NVENC/QSV/AMF）
- 本番画質・軽量音声モードでは本番解像度のまま AAC 128kbps / 32kHz で出力
- 仮動画モードでは横固定の仮画像を自動生成し、画質を軽くしつつ音声は高音質 / 低音質を切替可能

## 動作環境
- Windows 10/11
- .NET 8 SDK 以上
- ffmpeg（同梱またはPATHに登録）

## セットアップ
1. .NET 8 SDK をインストール
2. ffmpeg をインストールし PATH を通す
- `ffmpeg` が `Get-Command` で見える状態にする

## 起動
```powershell
dotnet run --project .\MovieMaker.csproj
```

## 配布（ZIP）
自己完結の単一ファイルで発行し、`publish` フォルダをZIPにして配布します。
ffmpegは別途インストールし、`PATH` が通っている必要があります。

```powershell
dotnet publish .\MovieMaker.csproj -c Release -r win-x64 --self-contained true `
  /p:PublishSingleFile=true `
  /p:IncludeNativeLibrariesForSelfExtract=true `
  /p:PublishTrimmed=false
```

出力先:
```
bin\Release\net8.0-windows\win-x64\publish\
```

## 使い方
1. 設定画面で「出力先」と「アーカイブ」を設定
2. タイトルを確認（画像ファイル名から自動補完されます）
3. 出力モードを選択（通常 / 本番画質・軽量音声 / 仮動画）
4. 通常 / 本番画質・軽量音声では画像と音楽をドロップ、仮動画では音声だけでも可（複数音声はファイル名順で連結）
5. 「エンコード開始」を押す

## 出力ルール
- 出力先
`出力先\{タイトル}_yyyyMMdd_HHmmss.mp4`
- アーカイブ
`アーカイブ\{タイトル}\` に元データをコピー
- 同名タイトルが存在する場合
`{タイトル}_yyyyMMdd` を付与し、さらに重複時は `_1` などを追加

## 制約
- 画像の縦横比は 9:16 または 16:9 のみ許可（±1%）
- それ以外はエラー
- YouTube Shorts向け（縦動画）は音楽が 0:57〜1:00 の場合は 0:57 に、2:57以上の場合は 2:57 に収めるため末尾1秒をフェードアウトして短縮
- Shorts 短縮時は、フェードアウト後に発生した末尾の実質無音を自動で除去
- 本番画質・軽量音声モードでは 1920x1080 / 1080x1920, 30fps, AAC 128kbps / 32kHz の設定を使用
- 仮動画モードでは横 960x540, 24fps を使用し、音声は高音質 `AAC 256kbps / 44.1kHz` または低音質 `AAC 128kbps / 32kHz` を選択可能
- 本番画質・軽量音声モードの出力ファイル名には `light-audio_` プレフィックスを付与
- 仮動画モードの出力ファイル名には `draft-preview_` プレフィックスを付与

## ログ
- `logs/` にffmpegのログを出力
- 設定画面で 1分枠 / 3分枠 の Shorts オフセット秒を変更可能

## ライセンス
GPLv3

