[Setup]
AppId={{A1C7D0C1-9E4B-4DAB-9E9D-7AA0E5E01F10}
AppName=Game Photo Auto Converter
AppVersion=1.0.0
AppPublisher=akiken-lab
DefaultDirName={autopf}\GamePhotoAutoConverter
DefaultGroupName=Game Photo Auto Converter
OutputDir=..\dist
OutputBaseFilename=GamePhotoAutoConverter-Setup
Compression=lzma
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"

[Tasks]
Name: "desktopicon"; Description: "デスクトップアイコンを作成"; GroupDescription: "追加タスク:"; Flags: unchecked

[Files]
Source: "..\publish\win-x64\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{group}\Game Photo Auto Converter"; Filename: "{app}\App.Wpf.exe"
Name: "{autodesktop}\Game Photo Auto Converter"; Filename: "{app}\App.Wpf.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\App.Wpf.exe"; Description: "Game Photo Auto Converter を起動"; Flags: nowait postinstall skipifsilent
