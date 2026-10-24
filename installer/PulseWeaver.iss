; PulseWeaver インストーラ（Inno Setup 6）
; ビルド例: ISCC.exe /DVERSION=0.1.0 installer\PulseWeaver.iss
; 事前に dist\publish へ dotnet publish しておくこと。

#ifndef VERSION
  #define VERSION "0.1.0"
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
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(AppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
Filename: "{#CableUrl}"; Description: "VB-CABLE の配布ページを開く（Discord 連携に必要）"; Flags: shellexec nowait postinstall skipifsilent; Tasks: cableguide
