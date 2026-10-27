; PulseWeaver インストーラ（Inno Setup 6）
; ビルド例: ISCC.exe /DVERSION=0.1.1 installer\PulseWeaver.iss
; 事前に dist\publish へ dotnet publish しておくこと。

#ifndef VERSION
  #define VERSION "0.1.1"
#endif

#define AppName "PulseWeaver"
#define AppPublisher "PulseWeaver"
#define AppExeName "PulseWeaver.exe"
#define CableUrl "https://vb-audio.com/Cable/"

[Setup]
AppId={{6F1C7E52-3B4D-4E2A-9F1B-7C0D5A8E2B31}
AppName={#AppName}
AppVersion={#VERSION}
AppVerName={#AppName} {#VERSION}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
UninstallDisplayIcon={app}\{#AppExeName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
SetupIconFile=..\PulseWeaver\Assets\PulseWeaver.ico
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\dist
OutputBaseFilename={#AppName}-{#VERSION}-setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
SetupLogging=yes
AllowNoIcons=yes
; 自動アップデートでは完了後にアプリを明示的に起動し直すため、
; Restart Manager による自動再起動と二重起動しないようにする。
RestartApplications=no

[Languages]
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "cableguide"; Description: "仮想オーディオケーブル（VB-CABLE）の入手ページを開く"; GroupDescription: "追加タスク:"

[Files]
Source: "..\dist\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(AppName, '&', '&&')}}"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent
Filename: "{#CableUrl}"; Description: "VB-CABLE の配布ページを開く（Discord 連携に必要）"; Flags: shellexec nowait postinstall skipifsilent; Tasks: cableguide

[Code]
{ アプリ内アップデートは /SILENT /RELAUNCH=1 でセットアップを起動する。
  サイレント実行では完了画面が出ないため、インストール後にアプリを自動で起動し直す。
  ウィザード実行時は完了画面の「PulseWeaver を起動」チェックボックスに任せる。 }
procedure CurStepChanged(CurStep: TSetupStep);
var
  AppPath: String;
  ResultCode: Integer;
begin
  if CurStep <> ssPostInstall then
    Exit;
  if not WizardSilent then
    Exit;
  if ExpandConstant('{param:RELAUNCH|0}') <> '1' then
    Exit;

  AppPath := ExpandConstant('{app}\{#AppExeName}');
  if not FileExists(AppPath) then
  begin
    Log('更新後の自動起動をスキップしました（実行ファイルなし）: ' + AppPath);
    Exit;
  end;

  if Exec(AppPath, '', ExpandConstant('{app}'), SW_SHOWNORMAL, False, ResultCode) then
    Log('更新後にアプリを起動しました: ' + AppPath)
  else
    Log('更新後の自動起動に失敗しました: ' + AppPath);
end;
