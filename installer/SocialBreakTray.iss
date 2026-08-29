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
; Checked by default (no "Flags: unchecked") - without the app actually
; running, native-app tracking simply can't happen at all (it has to be the
; foreground-window watcher that's alive to see a focus change), so this
; closes the single biggest real-world tracking gap: the user forgetting to
; relaunch it after a reboot. Still a visible, uncheckable-if-you-want-to
; installer option, not a silent registration - see AutoStart.cs's own
; comment on why that distinction matters.
Name: "autostart"; Description: "Start {#MyAppName} automatically when Windows starts"; GroupDescription: "Additional options:"

[Files]
Source: "{#MyAppSourceExe}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
; Same HKCU Run key AutoStart.cs itself writes when toggled on from the tray
; menu (SetEnabled(true)) - pre-seeding it here means IsEnabled() already
; reads true the first time the app runs, so the tray menu's checkbox
; correctly shows as checked with no extra wiring needed on the app side.
; Flags: uninsdeletevalue is required for Inno Setup to actually remove this
; value on uninstall - without it, a value written via [Registry] survives
; uninstall by default (verified: it does NOT get auto-removed on its own,
; regardless of having been created by this installer).
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "SocialBreakTray"; ValueData: """{app}\{#MyAppExeName}"""; Tasks: autostart; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; TokenStore.cs persists the encrypted token AND the DisclosureAcknowledged/
; HideWelcomeOnStartup flags to %APPDATA%\SocialBreak\config.dat - outside
; {app}, so the uninstaller never touched it before this. Two real
; consequences that fixes: (1) legal.html promises the login token is
; "removed from your device immediately" on uninstall, which wasn't actually
; happening; (2) a stale HideWelcomeOnStartup=true from a previous install
; would silently suppress the first-run "what this app does" dialog on a
; fresh reinstall, making it look like that dialog had been removed.
Type: filesandordirs; Name: "{userappdata}\SocialBreak"

[Code]
// Warns on leaving the Tasks page if "autostart" was deselected - the app
// can only track anything while it's actually running, so unchecking this
// silently turns off tracking for every desktop_app entry on the user's
// Media List until they remember to open it by hand. A one-time heads-up
// here is cheaper than a confused support message later.
function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if (CurPageID = wpSelectTasks) and (not WizardIsTaskSelected('autostart')) then
  begin
    MsgBox(
      'Social Break won''t start automatically with Windows.' + #13#10 + #13#10 +
      'That means it won''t track any desktop apps on your Media List until you ' +
      'open it yourself - it can only see what''s running while it''s actually open. ' +
      'You can turn this on later from the tray icon''s "Start with Windows" option.',
      mbInformation, MB_OK);
  end;
end;
