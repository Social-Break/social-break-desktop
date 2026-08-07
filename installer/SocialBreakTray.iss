; Inno Setup script for Social Break Desktop (Windows tray app).
;
; Installs per-user (no admin/UAC prompt needed) to match the app's own
; no-elevation philosophy - see AutoStart.cs's HKCU-only "Start with
; Windows" toggle. Packages the self-contained single-file publish output,
; so end users don't need the .NET runtime installed separately.

#define MyAppName "Social Break"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Social Break"
#define MyAppExeName "SocialBreakTray.exe"
#define MyAppSourceExe "..\SocialBreakTray\bin\Release\net8.0-windows\win-x64\publish\SocialBreakTray.exe"

[Setup]
; Fixed GUID - keep stable across versions so Windows recognizes upgrades
; and the uninstaller entry stays consistent rather than duplicating.
AppId={{527ECDDE-4AAF-453B-982E-54E980643616}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\SocialBreakTray
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=Output
OutputBaseFilename=SocialBreakTraySetup
SetupIconFile=..\SocialBreakTray\app.ico
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
InfoBeforeFile=smartscreen_notice.txt
UninstallDisplayIcon={app}\{#MyAppExeName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "{#MyAppSourceExe}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent
