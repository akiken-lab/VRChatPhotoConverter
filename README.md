# VRChatPhotoConverter
# Summary

VRChatで撮影した写真をjpeg に変換するPowerShellスクリプトです。  
友人に送付したりするときに容量が大きすぎて扱いづらいため  
送付用や確認用にファイルサイズが小さいJpeg を生成することを目的としています。  

元ファイルのPngファイルはバックアップフォルダに保存し、指定した保存先フォルダにjpegファイルを書き出します。  
また、VRChatの仕様でカメラのExif 情報で撮影日時の記載がないため、ファイルが生成された時間を撮影日時としてExif に書き込みも行っています。  
Google Photoや Amazo Photo に読み込んだ際にExifの撮影日時情報がないと、  
アプリ内写真の時系列が時差の分だけずれるためこの処理も追加しています。  


# Installation  
ps1 ファイル内の設定欄にあるフォルダパス設定をご自身の環境に合わせて設定してください。  
また、保存先パスとバックアップ先パスのフォルダはあらかじめエクスプローラーで作成ください。  

編集したps1ファイルをローカルのいずれかのフォルダに配置し、  
Windows のタスクスケジューラ機能で自動的に実行するように設定します。  

タスクには下記のように記載します。  

## トリガー  
タスクの開始：ログオン時  
設定　　　　：任意のユーザー  
繰り返し間隔：5分間　継続時間：無制限  
有効　　　　：☑  

## 操作  
操作：プログラムの開始  
プログラム：%Systemroot%\System32\WindowsPowerShell\v1.0\powershell.exe  
引数の追加：-ExecutionPolicy RemoteSigned C:\{PS1ファイルを置いたパス}\VRC_jpeg.ps1  

（引数の追加の例）-ExecutionPolicy RemoteSigned C:\vrc_task\VRC_jpeg.ps1  


