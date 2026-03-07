# VRC JPEG Auto Generator 設計書（整理版）

## 1. 目的
VRChat 等で生成される PNG を JPEG に自動変換する。  
運用ミスを減らすため、設定と実行フローをシンプルに保つ。

## 2. 現行仕様（実装ベース）
### 2.1 変換パイプライン
1. `SourceDir` から PNG を列挙する（再帰は `IncludeSubdirectories` で制御）。
2. スキップ判定を実行する。
3. JPEG 出力先パスを決定する。
4. 重複ポリシーを適用する（`Rename / Overwrite / Skip`）。
5. JPEG を保存する（`DryRun=false` のとき）。
6. PNG を `Move / Copy / Delete` のいずれかで処理する（`DryRun=false` のとき）。
7. 処理履歴を保存する（`DryRun=true` のときは保存しない）。

### 2.2 現行の重複チェック
重複チェックは「同一ファイルの再処理防止」と「同名 JPEG 出力競合回避」の2系統がある。

1. 再処理防止（入力側）
1. `RecentFileGuardSeconds` 未満の更新直後ファイルはスキップ。
2. メモリキャッシュキー `sourcePath|lastWriteTicks|fileSize` に一致したらスキップ。
3. SQLite 履歴 `source_path + last_write_utc_ticks + file_size` に存在したらスキップ。

2. 同名競合回避（出力側）
1. 出力先 JPEG が未存在ならそのまま保存。
2. `DuplicatePolicy=Overwrite` は上書き保存。
3. `DuplicatePolicy=Skip` は変換をスキップ。
4. `DuplicatePolicy=Rename` は `_1`, `_2` ... を付与して保存。

### 2.3 備考（VRChat運用上）
VRChat 画像は通常ファイル名重複が起きにくいため、`DuplicatePolicy` の実運用価値は低い。  
主に想定外ケースや手動投入ファイル向けの安全弁として機能している。

## 3. 改訂方針（実装前）
### 3.1 PNG後処理を2択化
`PngHandlingMode` は以下の2択へ整理する。

1. `Delete`
1. JPEG保存成功後に元PNGを削除する。
2. `Keep`
1. 元PNGに対して何もしない。

### 3.2 廃止/非表示候補
1. `Copy` と `Move` は廃止。
2. `PngArchiveDir` は不要化（Delete/Keep のみなら使わない）。

### 3.3 重複チェック整理方針
VRChat前提で簡素化する場合の候補は次の通り。

1. 維持するもの
1. Recent guard（更新直後スキップ）
2. 履歴DBによる再処理防止

2. 簡素化候補
1. `DuplicatePolicy` を廃止し、JPEG同名時は原則上書き
2. または `DuplicatePolicy` を内部固定（UI非表示）

## 4. 推奨決定案（次実装向け）
1. PNG後処理は `Delete / Keep` の2択にする。
2. `PngArchiveDir` は設定UIから外す。
3. `DuplicatePolicy` はUIから外し、内部は `Overwrite` 固定にする。
4. 再処理防止は「Recent guard + 履歴DB」を維持する。

## 5. 変更影響（実装時の見積り）
1. モデル
1. `PngHandlingMode` の列挙値整理（`Delete/Keep`）。
2. `ConversionProfile` から `PngArchiveDir` を段階的に非推奨化。

2. バリデーション
1. `PngArchiveDir` 必須チェックを削除。
2. 入出力同一パス禁止の PNG 側条件を削除。

3. 変換処理
1. PNG後処理分岐を `Delete/Keep` に置換。
2. `DuplicatePolicy` 分岐を削減または固定化。

4. UI
1. PNGモードを2択表示へ変更。
2. PNG保管先入力欄を削除。
3. 重複ポリシーUIを削除。

5. マイグレーション
1. 旧設定互換は持たない。
2. `PngHandlingMode` は `Delete / Keep` のみを扱う。


