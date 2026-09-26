; Inno Setup Script for OpenNotes (legacy namespace/data identity remains Caelum)
; This creates a proper Windows installer with Start Menu shortcuts,
; Program Files installation, and an uninstaller.

#ifndef MyAppName
  #define MyAppName "OpenNotes"
#endif
#ifndef MyAppVersion
#define MyAppVersion "5.2.15"
#endif
#ifndef MyAppPublisher
  #define MyAppPublisher "Learnmore_smart"
#endif
#ifndef MyAppURL
  #define MyAppURL "https://github.com/Learnmore-smart/OpenNotes"
#endif
#ifndef MyAppExeName
  #define MyAppExeName "OpenNotes.exe"
#endif
#ifndef MyAppSourceDir
  ; Payload directory staged by the CI publish step. The WPF job publishes
  ; to .\publish (default); the WinUI/V6 job publishes to .\publish-winui
  ; and invokes ISCC with
  ;   /DMyAppSourceDir=publish-winui /DMyAppExeName=OpenNotes.WinUI.exe
  ;   /DMyAppWinUIPayload
  #define MyAppSourceDir "publish"
#endif
#ifndef MyAppOutputBaseFilename
  #define MyAppOutputBaseFilename "OpenNotes-Setup-" + MyAppVersion
#endif

[Setup]
AppId={{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
LicenseFile=LICENSE
OutputDir=installer_output
OutputBaseFilename={#MyAppOutputBaseFilename}
SetupIconFile=Assets\app-icon.ico
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
UninstallDisplayIcon={app}\{#MyAppExeName}
MinVersion=10.0

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "startmenu"; Description: "Create a Start Menu shortcut"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
; Install all published files (WPF: publish\*, WinUI/V6: publish-winui\* —
; both are self-contained single-folder outputs including pdfium.dll).
Source: "{#MyAppSourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

#ifdef MyAppWinUIPayload
[InstallDelete]
; Same AppId + install dir means a V6 setup upgrades a 5.x (WPF) install
; in place. Sweep the stale WPF payload so only OpenNotes.WinUI.exe remains.
; InstallDelete runs before [Files] extraction, so sweeping *.dll/*.pdb is
; safe — the new payload re-extracts every shared dependency.
Type: files; Name: "{app}\*.dll"
Type: files; Name: "{app}\*.pdb"
Type: files; Name: "{app}\OpenNotes.exe"
Type: files; Name: "{app}\OpenNotes.deps.json"
Type: files; Name: "{app}\OpenNotes.runtimeconfig.json"
#endif

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[Code]
// T13-H hotfix: repair stale "OpenNotes" shortcuts left behind by 5.x
// (WPF) installs. A 5.x link targets {app}\OpenNotes.exe, which the WinUI
// payload no longer installs — the dead link shows a blank generic icon
// and won't launch. The 5.x installer could also have written shortcuts
// into the common (all-users) desktop/Start Menu while this install runs
// per-user (PrivilegesRequired=lowest), and [Icons] only rewrites this
// install's own {auto*} scope, so stale copies can survive in the other
// scope's folders.
//
// Inno can't read a .lnk's target, so use the removed exe as the proxy:
// once {app}\OpenNotes.exe is gone, every "OpenNotes.lnk" on disk either
// predates the new exe layout (stale -> repair) or was just written by
// [Icons] against {#MyAppExeName} (delete + recreate is an identical
// no-op). With the WPF payload OpenNotes.exe exists, so nothing runs.
procedure CurStepChanged(CurStep: TSetupStep);
var
  ShortcutDirs: array[0..4] of String;
  AppDir, ExePath, LnkPath: String;
  I: Integer;
begin
  if CurStep <> ssPostInstall then
    Exit;
  AppDir := ExpandConstant('{app}');
  if FileExists(AppDir + '\OpenNotes.exe') then
    Exit;
  ShortcutDirs[0] := ExpandConstant('{userdesktop}');
  ShortcutDirs[1] := ExpandConstant('{commondesktop}');
  ShortcutDirs[2] := ExpandConstant('{userprograms}');
  ShortcutDirs[3] := ExpandConstant('{commonprograms}');
  // Pinned taskbar links live in the per-user Quick Launch store. The dir
  // may not exist — FileExists on a .lnk inside it safely returns false.
  ShortcutDirs[4] := ExpandConstant('{userappdata}') +
    '\Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar';
  ExePath := AppDir + '\{#MyAppExeName}';
  for I := 0 to 4 do
  begin
    LnkPath := ShortcutDirs[I] + '\{#MyAppName}.lnk';
    if FileExists(LnkPath) then
    begin
      DeleteFile(LnkPath);
      // CreateShellLink raises on failure (e.g. a common-scope link under
      // a per-user install); log it and move on — worst case the stale
      // link remains, no worse than before this fix.
      try
        CreateShellLink(LnkPath, '', ExePath, '', AppDir, ExePath, 0,
          SW_SHOWNORMAL);
      except
        Log('OpenNotes setup: could not repair shortcut ' + LnkPath);
      end;
    end;
  end;
end;
