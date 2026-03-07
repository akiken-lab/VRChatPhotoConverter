# Portable 配布手順

## 方針
- インストーラーは使わず、フォルダ配布（zip）で配布する。
- 設定/履歴/ログの保存先は従来通り `%LOCALAPPDATA%\VRCJpegAutoGenerator`。
- 監視機能・常駐機能はそのまま利用可能。

## ビルド
```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\publish.ps1 -Configuration Release
```

生成物:
- フォルダ: `publish\win-x64\`
- zip: `dist\VRCJpegAutoGenerator-portable.zip`

## ユーザー配布時の使い方
1. zip を任意フォルダへ展開
2. `VRCJpegAutoGenerator.exe` を起動
3. 必要ならアプリ設定で「Windows起動時にアプリ起動」を有効化

## アンインストール
1. 配布フォルダを削除
2. 併せて設定を消す場合のみ `%LOCALAPPDATA%\VRCJpegAutoGenerator` を削除

