# 仕様書 v1.3（2画面 + プロファイル対応）

## 1. 概要
- PNGをJPEGへ変換し、PNGを `Move` / `Copy` で保管する。
- 複数プロファイルを保存し、ゲームごとに実行プロファイルを紐づける。
- ゲーム非紐づけのプロファイルも保存できる（手動実行用途）。

## 2. UI構成
### 2.1 実行画面（MainWindow）
- 実行プロファイル選択
- 既定プロファイル表示
- 監視開始 / 監視停止 / 今すぐ実行
- 設定画面を開く
- ログを開く / 出力を開く
- 進捗バー / 実行サマリー

### 2.2 設定画面（SettingsWindow）
- プロファイル管理（新規・削除・編集）
- 既定プロファイル選択
- ゲーム紐づけ（Mode / ExeName / AppID / Profile）
- ログレベル設定
- 起動時監視設定
- 履歴DBリセット要求
- 保存 / キャンセル

## 3. データモデル
### 3.1 ConversionProfile
- `Id`, `Name`
- `SourceDir`, `JpegOutputDir`, `PngArchiveDir`
- `PngHandlingMode`, `JpegQuality`, `IncludeSubdirectories`
- `DuplicatePolicy`, `DryRun`, `RecentFileGuardSeconds`

### 3.2 WatchTarget
- `Mode`, `ExeName`, `AppId`
- `ProfileId`, `ProfileName`

### 3.3 AppConfig
- `Profiles`, `WatchTargets`, `DefaultProfileId`
- `LogLevel`, `MonitorEnabledOnStartup`
- 旧キーは後方互換で保持

## 4. 既定テンプレート（初回）
- プロファイル未登録時、以下を自動作成:
  - Name: `VRChat_Default`
  - SourceDir: `%USERPROFILE%\Pictures\VRChat`
  - JpegOutputDir: `%USERPROFILE%\Pictures\VRChat_jpeg`
  - PngArchiveDir: `%USERPROFILE%\Pictures\VRChat_png`
  - PngHandlingMode: `Copy`
  - Quality: `90`
  - IncludeSubdirectories: `true`
  - DuplicatePolicy: `Rename`
  - DryRun: `false`
  - RecentFileGuardSeconds: `10`

## 5. 実行ルール
### 5.1 手動実行
- 実行画面で選択中の1プロファイルを実行。

### 5.2 監視実行
- `ExeName` 一致の `WatchTarget` を抽出。
- `ProfileId` を解決し、重複を除いたプロファイルを順次実行。
- `ProfileId` 未設定時は `DefaultProfileId` を使用。

### 5.3 変換パイプライン
1. PNG列挙
2. スキップ判定
3. JPEG保存
4. PNG Move/Copy
5. 履歴登録（DryRun時は登録しない）

## 6. 重複防止
- ジョブ重複: `SemaphoreSlim`
- ファイル重複:
  - メモリキャッシュ
  - SQLite履歴（`source_path + last_write_ticks + file_size`）

## 7. ログ
- レベル: `Error` / `Warning` / `Information` / `Debug`
- 保存先: `%LOCALAPPDATA%\GamePhotoAutoConverter\logs\app-*.log`

## 8. 履歴DB
- `%LOCALAPPDATA%\GamePhotoAutoConverter\processing-history.db`
- テーブル: `processed_files`
- 設定画面からリセット要求可能（保存時に実行）

