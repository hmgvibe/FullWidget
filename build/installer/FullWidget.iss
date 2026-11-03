; The FullWidget installer (Inno Setup 6.5 or newer).
;   iscc /DAppVersion=1.2.2 /DSourceDir=..\..\dist\FullWidget-1.2.2-win-x64-full\FullWidget /DOutputDir=..\..\dist build\installer\FullWidget.iss
; build\publish.ps1 -Step installer does this after building the full (self-contained) folder.
;
; Per user, no admin rights: %LocalAppData%\Programs\FullWidget. Uninstalling leaves the user's
; widgets and settings (%AppData%\FullWidget) in place, so reinstalling picks them up again.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceDir
  #error Pass /DSourceDir=<the published FullWidget folder>
#endif
#ifndef OutputDir
  #define OutputDir "."
#endif

[Setup]
AppId={{6F1C2A0E-5B7D-4E39-9C61-8E2F4D1A7B35}
AppName=FullWidget
AppVersion={#AppVersion}
AppVerName=FullWidget {#AppVersion}
AppPublisher=hmgvibe
AppPublisherURL=https://github.com/hmgvibe/FullWidget
AppSupportURL=https://github.com/hmgvibe/FullWidget/issues
AppUpdatesURL=https://github.com/hmgvibe/FullWidget/releases
VersionInfoVersion={#AppVersion}
VersionInfoProductName=FullWidget
VersionInfoDescription=FullWidget Setup
VersionInfoCopyright=Copyright (C) 2026 hmgvibe. Licensed under the GPL-3.0.
DefaultDirName={autopf}\FullWidget
DisableProgramGroupPage=yes
DisableDirPage=auto
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir={#OutputDir}
OutputBaseFilename=FullWidget-{#AppVersion}-setup
SetupIconFile=..\..\src\FullWidget\app.ico
UninstallDisplayIcon={app}\FullWidget.exe
UninstallDisplayName=FullWidget
WizardStyle=modern
WizardSizePercent=110
Compression=lzma2/ultra64
SolidCompression=yes
ShowLanguageDialog=auto
LanguageDetectionMethod=uilanguage
; FullWidget is closed with its own --exit below, not by the Restart Manager
CloseApplications=no

[Languages]
; the Chinese translations are Inno Setup's own (jrsoftware/issrc, Files/Languages), kept here
; because the released compiler doesn't include them yet
Name: "zhtw"; MessagesFile: "ChineseTraditional.isl"
Name: "zhcn"; MessagesFile: "ChineseSimplified.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
zhtw.DesktopIcon=在桌面建立捷徑
zhcn.DesktopIcon=在桌面创建快捷方式
en.DesktopIcon=Create a desktop shortcut
zhtw.AutoStart=開機時自動啟動 FullWidget
zhcn.AutoStart=开机时自动启动 FullWidget
en.AutoStart=Start FullWidget with Windows
zhtw.Extras=其他：
zhcn.Extras=其他：
en.Extras=Extras:
zhtw.LaunchNow=現在打開 FullWidget
zhcn.LaunchNow=现在打开 FullWidget
en.LaunchNow=Open FullWidget now

[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopIcon}"; GroupDescription: "{cm:Extras}"; Flags: unchecked
Name: "autostart"; Description: "{cm:AutoStart}"; GroupDescription: "{cm:Extras}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[InstallDelete]
; an upgrade replaces the built-in widgets and SDK wholesale, so removed files don't linger
Type: filesandordirs; Name: "{app}\widgets"
Type: filesandordirs; Name: "{app}\sdk"
Type: filesandordirs; Name: "{app}\ui"
Type: filesandordirs; Name: "{app}\templates"

[Icons]
Name: "{autoprograms}\FullWidget"; Filename: "{app}\FullWidget.exe"
Name: "{autodesktop}\FullWidget"; Filename: "{app}\FullWidget.exe"; Tasks: desktopicon

[Registry]
; the same entry the control center's "Start with Windows" switch writes
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "FullWidget"; ValueData: """{app}\FullWidget.exe"""; Tasks: autostart

[Run]
Filename: "{app}\FullWidget.exe"; Description: "{cm:LaunchNow}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; quit, then take this copy out of the startup list and the .fwz file type
Filename: "{app}\FullWidget.exe"; Parameters: "--exit"; Flags: runhidden waituntilterminated; RunOnceId: "Exit"
Filename: "{app}\FullWidget.exe"; Parameters: "--cleanup"; Flags: runhidden waituntilterminated; RunOnceId: "Cleanup"

[UninstallDelete]
Type: dirifempty; Name: "{app}"

[Code]
// An upgrade over a running copy: ask it to quit first, so its files can be replaced.
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Exe: String;
  Code: Integer;
begin
  Result := '';
  Exe := ExpandConstant('{app}\FullWidget.exe');
  if FileExists(Exe) then
  begin
    Exec(Exe, '--exit', '', SW_HIDE, ewWaitUntilTerminated, Code);
    Sleep(1500);
  end;
end;
