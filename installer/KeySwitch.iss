#define AppVersion "1.0.3"
#ifndef PublishDir
  #define PublishDir "..\artifacts\publish"
#endif
[Setup]
AppId={{6F6F8B29-77DA-4514-A8E3-A91C2D195891}
AppName=KeySwitch
AppVersion={#AppVersion}
AppPublisher=KeySwitch
SetupIconFile=..\src\KeySwitch.App\Resources\keyswitch.ico
AppPublisherURL=https://github.com/oover120/KeySwitch-GPT
AppSupportURL=https://github.com/oover120/KeySwitch-GPT/issues
AppUpdatesURL=https://github.com/oover120/KeySwitch-GPT/releases/latest
DefaultDirName={localappdata}\Programs\KeySwitch
DefaultGroupName=KeySwitch
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.22000
WizardStyle=modern
Compression=lzma2
SolidCompression=yes
OutputDir=..\artifacts\installer
OutputBaseFilename=KeySwitch-Setup-{#AppVersion}-x64
UninstallDisplayIcon={app}\KeySwitch.exe
CloseApplications=yes
CloseApplicationsFilter=KeySwitch.exe
RestartApplications=no
LicenseFile=..\LICENSE

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "autostart"; Description: "Запускать KeySwitch при входе в Windows"; Flags: unchecked
Name: "desktopicon"; Description: "Создать ярлык на рабочем столе"; Flags: unchecked

[Files]
Source: "{#PublishDir}\KeySwitch.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\licenses\*"; DestDir: "{app}\licenses"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\*.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\docs\banner.svg"; DestDir: "{app}\docs"; Flags: ignoreversion

[Icons]
Name: "{group}\KeySwitch"; Filename: "{app}\KeySwitch.exe"
Name: "{group}\Настройки KeySwitch"; Filename: "{app}\KeySwitch.exe"; Parameters: "--settings"
Name: "{autodesktop}\KeySwitch"; Filename: "{app}\KeySwitch.exe"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "KeySwitch"; ValueData: """{app}\KeySwitch.exe"""; Tasks: autostart; Flags: uninsdeletevalue

[Run]
Filename: "{app}\KeySwitch.exe"; Description: "Запустить KeySwitch"; Flags: nowait postinstall skipifsilent

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var ResultCode: Integer; TaskName: String;
begin
  if CurUninstallStep = usUninstall then begin
    { Current user's task only. schtasks may require UAC for elevated tasks. }
    RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'KeySwitch');
    if RegQueryStringValue(HKCU, 'Software\KeySwitch', 'ElevatedTaskName', TaskName) then begin
      if not Exec(ExpandConstant('{sys}\schtasks.exe'), '/Delete /TN "' + TaskName + '" /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) or (ResultCode <> 0) then
        ShellExec('runas', ExpandConstant('{sys}\schtasks.exe'), '/Delete /TN "' + TaskName + '" /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
      if ResultCode = 0 then RegDeleteValue(HKCU, 'Software\KeySwitch', 'ElevatedTaskName');
    end;
  end;
end;
