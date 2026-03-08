# Portable 配布手順

このプロジェクトはインストーラーを使わず、zip 展開だけで利用できる Portable 配布を前提にしています。GitHub Releases と BOOTH のどちらでも同じ配布物を使えます。

## 配布方針
- 配布形式は zip
- インストーラーなし
- 実行ファイルは自己完結 publish
- 設定 / 履歴 / ログは実行フォルダではなく `%LOCALAPPDATA%\VRCJpegAutoGenerator` に保存
- 更新時は新しい zip へ差し替え

## ビルド
```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\publish.ps1 -Configuration Release
```

生成物:
- フォルダ: `publish\win-x64\`
- zip: `dist\VRCJpegAutoGenerator-portable.zip`

## 配布物の中身
`dist\VRCJpegAutoGenerator-portable.zip` には次が含まれます。

- `VRCJpegAutoGenerator\VRCJpegAutoGenerator.exe`
- 実行に必要な publish 一式
- `VRCJpegAutoGenerator\readme.txt`
- `VRCJpegAutoGenerator\license.txt`

補足:
- zip の最上位に `VRCJpegAutoGenerator` フォルダを持たせています
- `*.pdb` は含めません

## エンドユーザー向け導入手順
1. zip を任意フォルダへ展開
2. `VRCJpegAutoGenerator.exe` を起動
3. 初回ウィザードで入力フォルダ・JPEG 出力先・PNG 処理方法を設定
4. 必要なら「Windows 起動時に起動」と「起動時に監視開始」を有効化

## 更新手順
1. アプリを終了
2. 新しい zip を展開
3. 既存の展開フォルダを新しい内容で置き換え
4. `%LOCALAPPDATA%\VRCJpegAutoGenerator` 配下の設定・履歴・ログはそのまま引き継がれる

## アンインストール
1. 展開した配布フォルダを削除
2. 完全削除したい場合のみ `%LOCALAPPDATA%\VRCJpegAutoGenerator` を削除

## GitHub 向け説明の要点
- 無料配布の Portable 版であること
- zip 展開だけで使えること
- ログ保存先と設定保存先
- 不具合報告先
- 既知の制約

## BOOTH 向け説明の要点
- VRChat スクリーンショット向けであること
- ゲーム終了時の自動変換が主用途であること
- インストーラーがないこと
- 外部通信を行わないこと
- ファイル削除を伴う設定があるためバックアップ推奨であること
