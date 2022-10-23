Add-Type -AssemblyName System.Drawing


#======設定=====================================================================

#VRChat写真フォルダ
$targetDirectiry = "C:\Users\****\Pictures\VRChat"
#保存先パス
$saveDirectory = "C:\Users\****\Pictures\VRChat_Jpeg\"
#バックアップ先パス
$backupDirectory = "C:\Users\****\Pictures\VRChat_Backup\"

#======関数=====================================================================

#Exif 操作用関数
function GetExifPropertyItem($image, $id)
{
  $item = $image.PropertyItems | Where-Object { $_.Id -eq $id }
  if($item -ne $null) {
    $image.RemovePropertyItem($id)
  }
  return $image.PropertyItems | Select-Object -First 1
}

# $imageオブジェクトにPropertyItemを登録する。
function SetPropertyItem($image, $id, $len, $type, $value)
{
  $item = GetExifPropertyItem -image $image -id $id
  $item.Id = $id
  $item.Len = $len
  $item.Type = $type
  $item.Value = $value
  $image.SetPropertyItem($item)
}
 
# ＄dateTimeで指定した撮影日時よりExif用配列データを取得する。
function getDateTimeValue($dateTime)
{
  $chars = $dateTime.ToString("yyyy:MM:dd HH:mm:ss").ToCharArray()
  $ascii = @()
  foreach($char in $chars)
  {
    $ascii += [Byte][Char]$char
  }
  $ascii += 0
  return $ascii
}

# $imageオブジェクトにExif時間情報を登録する。
function SetDateTime($image, $dateTime)
{
  $value = getDateTimeValue -dateTime $dateTime
  SetPropertyItem -image $image -id 0x9003 -len 20 -type 2 -value $value
  SetPropertyItem -image $image -id 0x9004 -len 20 -type 2 -value $value
}


#======メイン処理=====================================================================

#ファイルパス配列を宣言する
$targetFileListArray01 = @()
echo $targetFileListArray01
# 処理対象ファイルの絶対パスを配列に格納
$targetFileListArray01 = @(Get-ChildItem -LiteralPath  $targetDirectiry -Recurse -Filter *.png).FullName

# 対象ファイルごとにループ処理
for($i = 0; $i -lt $targetFileListArray01.Length; $i++ ){
	
	#jpeg拡張子の絶対パスファイル名を作成
	$filenameJpg = $saveDirectory +(Get-ChildItem -LiteralPath  $targetFileListArray01[$i]).BaseName + ".jpeg"

	#ファイルを読み込む
	$pngImage =  [System.Drawing.Image]::FromFile($targetFileListArray01[$i])

	#ファイルの更新日時を取得
	$fileDate = (Get-ItemProperty -LiteralPath  $targetFileListArray01[$i]).LastWriteTime

	#SetDateTime関数を呼び出して読み込みpngに元のExifを反映
	SetDateTime -image $pngImage -dateTime $fileDate

	# 別の形式で保存する
	$pngImage.Save($filenameJpg, [System.Drawing.Imaging.ImageFormat]::jpeg)

	#オブジェクトを破棄
	$pngImage.Dispose()

	#元ファイルをバックアップフォルダに移動
	move $targetFileListArray01[$i] $backupDirectory
}
