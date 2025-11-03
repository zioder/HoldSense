; Inno Setup Script for HoldSense
; Download Inno Setup from: https://jrsoftware.org/isdl.php

#define MyAppName "HoldSense"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Zioder"
#define MyAppURL "https://github.com/zioder/HoldSense"
#define MyAppExeName "HoldSense.exe"
#define MyAppAssocName MyAppName + " File"
#define MyAppAssocExt ".hscfg"
#define MyAppAssocKey StringChange(MyAppAssocName, " ", "") + MyAppAssocExt

[Setup]
; NOTE: The value of AppId uniquely identifies this application. Do not use the same AppId value in installers for other applications.
AppId={{A8B7C6D5-E4F3-G2H1-I0J9-K8L7M6N5O4P3}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
AppUpdatesURL={#MyAppURL}/releases
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
LicenseFile=LICENSE
; Uncomment the following line to run in non administrative install mode (install for current user only.)
;PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir=installer_output
OutputBaseFilename=HoldSense-Setup-v{#MyAppVersion}
SetupIconFile=HoldSense\assets\HoldSense.ico
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#MyAppExeName}
DisableProgramGroupPage=yes
DisableWelcomePage=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "launchonstartup"; Description: "Launch on Windows startup"; GroupDescription: "Additional options:"; Flags: unchecked

[Files]
Source: "publish\HoldSense\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "publish\HoldSense\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; NOTE: Don't use "Flags: ignoreversion" on any shared system files

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#MyAppName}"; ValueData: """{app}\{#MyAppExeName}"""; Flags: uninsdeletevalue; Tasks: launchonstartup

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Code]
function InitializeSetup(): Boolean;
var
  ResultCode: Integer;
  Version: TWindowsVersion;
begin
  GetWindowsVersionEx(Version);
  
  // Check for Windows 10 version 2004 or newer (build 19041)
  if (Version.Major < 10) or ((Version.Major = 10) and (Version.Build < 19041)) then
  begin
    MsgBox('HoldSense requires Windows 10 version 2004 (build 19041) or newer.' + #13#10 + 
           'Please update your Windows installation.', mbError, MB_OK);
    Result := False;
  end
  else
    Result := True;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    // Any post-installation tasks can be added here
  end;
end;

function InitializeUninstall(): Boolean;
var
  ResultCode: Integer;
begin
  // Kill the process if it's running
  Exec('taskkill', '/F /IM HoldSense.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := True;
end;

