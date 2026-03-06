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
SetupIconFile=..\src\App.Wpf\Assets\TrayIcon.ico
UninstallDisplayIcon={app}\App.Wpf.exe
CloseApplications=yes

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

[Code]
const
  UpgradeMarkerRelativePath = 'GamePhotoAutoConverter\.preserve-userdata-upgrade';

function GetUpgradeMarkerPath: string;
begin
  Result := ExpandConstant('{commonappdata}\') + UpgradeMarkerRelativePath;
end;

function GetUserDataPath: string;
begin
  Result := ExpandConstant('{localappdata}\GamePhotoAutoConverter');
end;

procedure MarkUpgradePreserve;
var
  MarkerPath: string;
begin
  if not FileExists(ExpandConstant('{app}\unins000.exe')) then
    exit;

  MarkerPath := GetUpgradeMarkerPath;
  ForceDirectories(ExtractFileDir(MarkerPath));
  SaveStringToFile(MarkerPath, 'preserve', False);
end;

function InitializeSetup: Boolean;
begin
  Result := True;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  MarkerPath: string;
begin
  if CurStep = ssInstall then
  begin
    MarkUpgradePreserve;
    exit;
  end;

  if CurStep <> ssPostInstall then
    exit;

  MarkerPath := GetUpgradeMarkerPath;
  if FileExists(MarkerPath) then
    DeleteFile(MarkerPath);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  MarkerPath: string;
  UserDataPath: string;
  ResultCode: Integer;
begin
  if CurUninstallStep <> usUninstall then
    exit;

  Exec(ExpandConstant('{cmd}'), '/C taskkill /F /IM App.Wpf.exe /T >nul 2>nul', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

  MarkerPath := GetUpgradeMarkerPath;
  if FileExists(MarkerPath) then
    exit;

  UserDataPath := GetUserDataPath;
  if DirExists(UserDataPath) then
    DelTree(UserDataPath, True, True, True);
end;
