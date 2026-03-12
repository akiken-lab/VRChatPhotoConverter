# GitHub / BOOTH 配布チェックリスト

GitHub Releases と BOOTH の両方で配布する前提で、毎回確認する項目を整理したメモです。

## 1. ビルド
1. `powershell -ExecutionPolicy Bypass -File .\scripts\publish.ps1 -Configuration Release` を実行する
2. `publish\win-x64\VRCJpegAutoGenerator.exe` が生成されることを確認する
3. `dist\VRCJpegAutoGenerator-v<version>-portable.zip` が生成されることを確認する
4. zip 内に `readme.txt` と `license.txt` が入っていることを確認する
5. zip 内に `*.pdb` が含まれていないことを確認する

## 2. 動作確認
1. 新規環境想定で初回起動ウィザードが表示されることを確認する
2. VRChat 想定の入力フォルダと出力先を設定し、手動変換が成功することを確認する
3. トレイ格納、トレイからの「今すぐ変換」、終了が動作することを確認する
4. ゲーム終了監視が動作することを確認する
5. `Keep` と `Delete` の両モードを確認する
6. `%LOCALAPPDATA%\VRCJpegAutoGenerator\logs` にログが出ることを確認する

## 3. GitHub 向け確認
1. `README.md` の説明が現在の挙動と一致していることを確認する
2. リリースノートに次を含める
   - 更新日
   - 変更点
   - 既知の制約
   - 配布形式が Portable zip であること
3. 添付ファイル名が `VRCJpegAutoGenerator-v<version>-portable.zip` になっていることを確認する
4. 不具合報告先として GitHub Issues または連絡先を明記する

## 4. BOOTH 向け確認
1. 商品説明に次を含める
   - 何をするソフトか
   - 対応 OS
   - 導入手順
   - 保存先
   - 注意事項
   - ネットワーク通信なし
2. `distribution\booth_content.txt` の内容を最新化する
3. サムネイルや説明画像を、初回ウィザード / メイン画面 / 設定画面ベースで更新する
4. 更新履歴の日付とバージョンを商品ページへ反映する
5. BOOTH メッセージで受ける問い合わせ範囲を明記する

## 5. 同梱テキスト確認
1. `distribution\readme.txt` がエンドユーザー向け文面になっていることを確認する
2. `distribution\license.txt` が最新であることを確認する
3. README と同梱 readme の説明が矛盾していないことを確認する

## 6. 削除・更新案内
1. 更新方法として「新しい zip に置き換え」と案内していることを確認する
2. 完全削除時は `%LOCALAPPDATA%\VRCJpegAutoGenerator` も削除対象であることを案内する
3. 履歴 DB を保持したまま更新できることを案内する
