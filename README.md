# VRC JPEG Auto Generator

VRChat などのゲームで保存された PNG スクリーンショットを、ゲーム終了時または手動操作で JPEG に変換する Windows 向けデスクトップアプリです。常駐監視、初回セットアップウィザード、ログ出力、重複変換防止用の履歴 DB を備えています。

## このソフトでできること
- PNG を JPEG へ一括変換
- ゲーム終了を検知して自動実行
- タスクトレイ常駐
- 「今すぐ変換」による手動実行
- 起動時に監視を開始
- Windows ログオン時の最小化起動
- SQLite 履歴 DB による再処理防止
- 直近更新ファイルの除外秒数設定
- 元 PNG を残す / 変換後に削除する の切り替え

## 想定ユーザー
- VRChat のスクリーンショットを PNG のまま大量に保存しており、JPEG 版も自動で作りたい人
- ゲーム終了時にまとめて変換したい人
- インストーラーなしの Portable 配布を使いたい人

## 動作環境
- Windows 11 64bit で動作確認
- .NET 8 ベースの自己完結配布を想定
- ネットワーク接続は不要

## クイックスタート
1. GitHub Releases または BOOTH から zip を取得します。
2. zip を任意のフォルダへ展開します。
3. `VRCJpegAutoGenerator.exe` を起動します。
4. 初回ウィザードで以下を設定します。
   - 監視対象ゲーム
   - PNG の入力フォルダ
   - JPEG の出力先
   - 元 PNG を残すか、変換後に削除するか
   - 初回設定後に監視を開始するか
   - 既存 PNG を初回対象に含めるか
5. 以後はゲーム終了時の自動変換、またはメイン画面 / トレイメニューの「今すぐ変換」を使います。

## 実際の挙動
### 変換ルール
- 入力対象は `*.png`
- サブフォルダ走査は設定で切り替え
- 出力拡張子は `.jpeg`
- JPEG 品質は 1〜100
- JPEG 出力先は入力フォルダと同一、または相互に内包するパスを禁止

### 重複処理
- 現在の実装では JPEG 重複時は上書きです
- 再処理防止は以下の組み合わせで行います
  - 更新直後ファイルの除外
  - メモリキャッシュ
  - SQLite 履歴 DB

### PNG の扱い
- `Keep`: 元 PNG を残します
- `Delete`: JPEG 保存成功後に元 PNG を削除します

### 常駐と自動起動
- ウィンドウを閉じても終了せず、タスクトレイへ格納されます
- 最小化時と「閉じる」でトレイ格納された時は、その旨をタスクトレイ通知で案内します
- Windows 起動時の自動起動はスタートアップフォルダに `VRCJpegAutoGenerator.cmd` を作成して実現します

## 監視負荷について
- 待機中の監視負荷は非常に低く、現在の一般的な CPU 性能であれば通常利用ではほぼ無視できる水準です
- ただし負荷が完全にゼロではありません
- 監視の一次方式は WMI の `Win32_ProcessStopTrace` イベント購読です
- WMI が使えない環境では、2 秒ごとの軽いポーリング監視へ自動でフォールバックします
- 待機中は画像変換やフォルダ全走査を行わず、変換処理は手動実行またはゲーム終了検知後にだけ動きます

技術的な根拠:
- 監視コードは対象 EXE 名だけを保持し、プロセス終了イベントを受けた時にだけ変換処理へ進みます
- フォールバック時も 2 秒周期でプロセス一覧との差分を見るだけで、PNG 列挙や JPEG 変換は行いません
- そのため公開表現としては「低負荷」「常時スキャンしない」が正確であり、「負荷ゼロ」とは表現しない方が実装に忠実です

## 保存先
アプリ本体を Portable 配布で展開しても、設定やログは次の場所に保存されます。

`%LOCALAPPDATA%\VRCJpegAutoGenerator`

保存される内容:
- `settings.json`
- `processing-history.db`
- `logs\app-YYYYMMDD.log`


## トラブルシュート
### 変換されない
- 入力フォルダが実在するか確認
- JPEG 出力先が入力フォルダと同一または内包関係になっていないか確認
- 除外秒数が大きすぎないか確認
- すでに履歴 DB に登録されたファイルではないか確認

### 再変換したい
- 設定画面から履歴 DB をリセットします
- その後に「今すぐ変換」を実行します

### ログを確認したい
- アプリ内のログ表示、または `%LOCALAPPDATA%\VRCJpegAutoGenerator\logs` を確認してください

## プライバシーと安全性
- 外部ネットワーク通信は行いません
- テレメトリ、広告、アカウント連携、自動更新機能はありません
- 画像変換やファイル削除を伴うため、運用前にスクリーンショットのバックアップを推奨します

## ビルド
前提:
- Windows
- .NET SDK
- PowerShell

実行コマンド:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\publish.ps1 -Configuration Release
```

生成物:
- `publish\win-x64\`
- `dist\VRCJpegAutoGenerator-v<version>-portable.zip`

zip の内容:
- 先頭フォルダは `VRCJpegAutoGenerator\`
- `distribution\readme.txt`
- `distribution\license.txt`
- `*.pdb` は除外

## リポジトリ構成
- `src/`: アプリ本体
- `tests/`: テスト
- `scripts/`: ビルド / 配布スクリプト
- `docs/`: 設計メモ・配布メモ
- `distribution/`: 配布物に同梱するテキスト

## 配布向け補助ドキュメント
- `docs/portable-distribution.md`: Portable 配布の手順
- `docs/release-checklist.md`: GitHub / BOOTH 配布前チェック
- `distribution/booth_content.txt`: BOOTH 掲載文の下書き

## ライセンス
本体ライセンスは MIT License です。配布物に同梱する依存ライブラリを含む詳細は `distribution\license.txt` を参照してください。
