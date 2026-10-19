; ScreenClip installer (Inno Setup 6), in English and French (picked from the Windows display language).
; Built by scripts\build.ps1, which prepares dist\app first:
;   ISCC.exe /DAppVersion=1.0.0 installer\ScreenClip.iss
; The app also runs it to install an update (src\ScreenClip\Services\Updater.cs): silent, closing the running app
; (its hidden window answers the Restart Manager's WM_CLOSE), /RELAUNCH=1.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

; Windows file versions are numbers only: 1.1.0 for a 1.1.0-beta.1 pre-release.
#if Pos("-", AppVersion) > 0
  #define AppFileVersion Copy(AppVersion, 1, Pos("-", AppVersion) - 1)
#else
  #define AppFileVersion AppVersion
#endif

#define AppName "ScreenClip"
#define AppExe "ScreenClip.exe"

[Setup]
AppId={{4F0B6C2E-9A37-4C1D-B5E8-3D7A2F61C0B9}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=QuentinPerou
AppPublisherURL=https://github.com/quentinperou
AppSupportURL=https://github.com/quentinperou/ScreenClip
AppUpdatesURL=https://github.com/quentinperou/ScreenClip/releases
VersionInfoVersion={#AppFileVersion}
DefaultDirName={autopf}\ScreenClip
DisableProgramGroupPage=yes
; Admin rights: installs for every user of the PC, and lets the .NET runtime be installed.
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
OutputDir=..\dist
OutputBaseFilename=ScreenClip-Setup-{#AppVersion}
SetupIconFile=..\src\ScreenClip\Assets\ScreenClip.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes
CloseApplications=yes
RestartApplications=no
; Only ask for the language when Windows uses one the installer does not have.
ShowLanguageDialog=auto

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "french"; MessagesFile: "compiler:Languages\French.isl"

[CustomMessages]
english.RuntimeMissing=ScreenClip needs the Microsoft ".NET 10 Desktop Runtime", which is not installed on this PC.%nInstall it now?
french.RuntimeMissing=ScreenClip a besoin du « .NET 10 Desktop Runtime » (Microsoft), absent de ce PC.%nL'installer maintenant ?
english.RuntimeInstalling=Installing the .NET 10 Desktop Runtime...
french.RuntimeInstalling=Installation du .NET 10 Desktop Runtime…
english.RuntimeFailed=The automatic installation failed. The download page will open:%nchoose ".NET Desktop Runtime 10" (x64).
french.RuntimeFailed=L'installation automatique a échoué. La page de téléchargement va s'ouvrir :%nchoisissez « .NET Desktop Runtime 10 » (x64).
english.DeleteUserData=Also delete your ScreenClip settings?%n%nKeep them if you are going to install ScreenClip again. Your captures (Pictures\ScreenClip) are always kept.
french.DeleteUserData=Supprimer aussi vos réglages ScreenClip ?%n%nGardez-les si vous allez réinstaller ScreenClip. Vos captures (Images\ScreenClip) sont toujours conservées.

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\dist\app\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Registry]
; "Start with Windows" is written by the app itself (Settings › General, on by default once installed): removed with it.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "ScreenClip"; Flags: uninsdeletevalue dontcreatekey
Root: HKCU; Subkey: "Software\ScreenClip"; ValueType: none; Flags: uninsdeletekey dontcreatekey

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[UninstallDelete]
; Temporary files of the app: captures handed to other apps by drag and drop, the last update downloaded.
Type: filesandordirs; Name: "{%TEMP}\ScreenClip"
Type: filesandordirs; Name: "{%TEMP}\ScreenClip-Update"

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent runasoriginaluser
; Update installed from the app (silent, /RELAUNCH=1): start the app again, as the user, not elevated, without
; opening its settings.
Filename: "{app}\{#AppExe}"; Parameters: "--background"; Flags: nowait runasoriginaluser skipifnotsilent; Check: RelaunchRequested

[Code]
const
  WM_CLOSE = $0010;

{ Set by the app when it installs an update (Updater.InstallerArguments). }
function RelaunchRequested: Boolean;
begin
  Result := ExpandConstant('{param:RELAUNCH|0}') = '1';
end;

{ The app is framework-dependent: it needs the .NET 10 Desktop Runtime. }
function IsDesktopRuntimeInstalled: Boolean;
var
  FindRec: TFindRec;
begin
  Result := FindFirst(ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.WindowsDesktop.App\10.*'), FindRec);
  if Result then
    FindClose(FindRec);
end;

procedure EnsureDesktopRuntime;
var
  ResultCode: Integer;
begin
  if IsDesktopRuntimeInstalled then
    Exit;
  if MsgBox(CustomMessage('RuntimeMissing'), mbConfirmation, MB_YESNO) <> IDYES then
    Exit;
  WizardForm.StatusLabel.Caption := CustomMessage('RuntimeInstalling');
  if not Exec('winget.exe', 'install --id Microsoft.DotNet.DesktopRuntime.10 -e --silent --accept-source-agreements --accept-package-agreements',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode) or not IsDesktopRuntimeInstalled then
  begin
    MsgBox(CustomMessage('RuntimeFailed'), mbInformation, MB_OK);
    ShellExec('open', 'https://dotnet.microsoft.com/download/dotnet/10.0', '', '', SW_SHOWNORMAL, ewNoWait, ResultCode);
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    EnsureDesktopRuntime;
end;

{ ScreenClip runs in the notification area: close it before its files are removed. Its windows are all titled
  "ScreenClip"; WM_CLOSE on the hidden one quits the app (on a thumbnail, it only closes that thumbnail). }
procedure CloseRunningApp;
var
  Wnd: HWND;
  Tries, ResultCode: Integer;
begin
  for Tries := 1 to 10 do
  begin
    Wnd := FindWindowByWindowName('{#AppName}');
    if Wnd = 0 then
      Exit;
    SendMessage(Wnd, WM_CLOSE, 0, 0);
    Sleep(200);
  end;
  Exec('taskkill.exe', '/f /im {#AppExe}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

function InitializeUninstall: Boolean;
begin
  CloseRunningApp;
  Result := True;
end;

{ If ScreenClip took the Print Screen key from Windows' Snipping Tool (Software\ScreenClip\TookPrintScreen, written by
  the app), give it back: without ScreenClip, the key would do nothing. }
procedure GivePrintScreenBack;
var
  Took: Cardinal;
begin
  if RegQueryDWordValue(HKCU, 'Software\ScreenClip', 'TookPrintScreen', Took) and (Took = 1) then
    RegWriteDWordValue(HKCU, 'Control Panel\Keyboard', 'PrintScreenKeyForSnippingEnabled', 1);
end;

{ Settings (%LOCALAPPDATA%\ScreenClip, of the account that uninstalls): kept unless the user says otherwise, "No"
  being the default, so that a reinstall finds them. A silent uninstall keeps them. The captures are never touched. }
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
    GivePrintScreenBack;
  if (CurUninstallStep = usPostUninstall) and not UninstallSilent and
     (MsgBox(CustomMessage('DeleteUserData'), mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES) then
    DelTree(ExpandConstant('{localappdata}\ScreenClip'), True, True, True);
end;
