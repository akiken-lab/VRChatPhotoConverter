Add-Type -AssemblyName System.Drawing

#VRChat�ʐ^�t�H���_
$targetDirectiry = "C:\Users\****\Pictures\VRChat"
#�ۑ���p�X
$saveDirectory = "C:\Users\****\Pictures\VRChat_Jpeg\"
#�o�b�N�A�b�v��p�X
$backupDirectory = "C:\Users\****\Pictures\VRChat_Backup\"


#Exif ����p�֐�
function GetExifPropertyItem($image, $id)
{
  $item = $image.PropertyItems | Where-Object { $_.Id -eq $id }
  if($item -ne $null) {
    $image.RemovePropertyItem($id)
  }
  return $image.PropertyItems | Select-Object -First 1
}

# $image�I�u�W�F�N�g��PropertyItem��o�^����B
function SetPropertyItem($image, $id, $len, $type, $value)
{
  $item = GetExifPropertyItem -image $image -id $id
  $item.Id = $id
  $item.Len = $len
  $item.Type = $type
  $item.Value = $value
  $image.SetPropertyItem($item)
}
 
# ��dateTime�Ŏw�肵���B�e�������Exif�p�z��f�[�^���擾����B
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

# $image�I�u�W�F�N�g��PropertyItem��o�^����B
function SetDateTime($image, $dateTime)
{
  $value = getDateTimeValue -dateTime $dateTime
  SetPropertyItem -image $image -id 0x9003 -len 20 -type 2 -value $value
  SetPropertyItem -image $image -id 0x9004 -len 20 -type 2 -value $value
}


#�t�@�C���p�X�z���錾����
$targetFileListArray01 = @()
echo $targetFileListArray01
# �����Ώۃt�@�C���̐�΃p�X��z��Ɋi�[
$targetFileListArray01 = @(Get-ChildItem -LiteralPath  $targetDirectiry -Recurse -Filter *.png).FullName

# �Ώۃt�@�C�����ƂɃ��[�v����
for($i = 0; $i -lt $targetFileListArray01.Length; $i++ ){
	
	#jpeg�g���q�̐�΃p�X�t�@�C�������쐬
	$filenameJpg = $saveDirectory +(Get-ChildItem -LiteralPath  $targetFileListArray01[$i]).BaseName + ".jpeg"

	#�t�@�C����ǂݍ���
	$pngImage =  [System.Drawing.Image]::FromFile($targetFileListArray01[$i])

	#�t�@�C���̍X�V�������擾
	$fileDate = (Get-ItemProperty -LiteralPath  $targetFileListArray01[$i]).LastWriteTime

	#SetDateTime�֐����Ăяo���ēǂݍ���png�Ɍ���Exif�𔽉f
	SetDateTime -image $pngImage -dateTime $fileDate

	# �ʂ̌`���ŕۑ�����
	$pngImage.Save($filenameJpg, [System.Drawing.Imaging.ImageFormat]::jpeg)

	#�I�u�W�F�N�g��j��
	$pngImage.Dispose()

	#���t�@�C�����o�b�N�A�b�v�t�H���_�Ɉړ�
	move $targetFileListArray01[$i] $backupDirectory
}
