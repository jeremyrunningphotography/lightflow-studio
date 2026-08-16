#ifndef MyAppVersion
  #error MyAppVersion must be supplied by Build-Release.ps1
#endif
#ifndef SourceDir
  #error SourceDir must be supplied by Build-Release.ps1
#endif
#ifndef OutputDir
  #error OutputDir must be supplied by Build-Release.ps1
#endif
#ifndef InstallerAssetsDir
  #error InstallerAssetsDir must be supplied by Build-Release.ps1
#endif

#define MyAppName "Lightflow Studio"
#define MyAppPublisher "Jeremy Running Photography"
#define MyAppExeName "LightflowStudio.exe"
#define MyAppId "{E67863CA-C113-438B-B1C5-6019D174CCFD}"
#define LegacyUninstallKey "Software\Microsoft\Windows\CurrentVersion\Uninstall\{E67863CA-C113-438B-B1C5-6019D174CCFD}_is1"

[Setup]
AppId={{#MyAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppCopyright=Copyright (c) {#MyAppPublisher}
AppComments=Professional media workflow tools from {#MyAppPublisher}
DefaultDirName={autopf}\Lightflow Studio
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir={#OutputDir}
OutputBaseFilename=LightflowStudio-{#MyAppVersion}-win-x64-setup
SetupIconFile={#SourceDir}\LightflowStudio.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
#ifdef ValidationBuild
Compression=zip
SolidCompression=no
#else
Compression=lzma2/ultra64
SolidCompression=yes
#endif
WizardStyle=modern dark slate includetitlebar hidebevels
WizardImageFile={#InstallerAssetsDir}\LightflowWizard.png
WizardSmallImageFile={#InstallerAssetsDir}\LightflowWizardSmall.png
WizardImageBackColor=black
WizardSmallImageBackColor=black
WizardSizePercent=120,120
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
LicenseFile={#SourceDir}\THIRD-PARTY-NOTICES.md
VersionInfoVersion={#MyAppVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription={#MyAppName} installer
VersionInfoProductName={#MyAppName}

[Messages]
WelcomeLabel1=Welcome to Lightflow Studio Setup
WelcomeLabel2=Install [name/ver] for everyone who uses this computer.%n%nA Jeremy Running Photography application.
ReadyLabel1=Setup is ready to install Lightflow Studio for all users.
FinishedHeadingLabel=Lightflow Studio is ready
FinishedLabel=Setup has installed Lightflow Studio for everyone who uses this computer. Launch it from the Start Menu or desktop shortcut.

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent

[Code]
function ReadLegacyPerUserUninstaller(var Uninstaller: String): Boolean;
begin
  Result := RegQueryStringValue(HKCU64, '{#LegacyUninstallKey}', 'UninstallString', Uninstaller);
  if not Result then
    Result := RegQueryStringValue(HKCU32, '{#LegacyUninstallKey}', 'UninstallString', Uninstaller);
end;

function ExecutableFromCommandLine(const CommandLine: String): String;
var
  ClosingQuote: Integer;
  Space: Integer;
begin
  Result := Trim(CommandLine);
  if (Length(Result) > 0) and (Result[1] = '"') then
  begin
    Delete(Result, 1, 1);
    ClosingQuote := Pos('"', Result);
    if ClosingQuote > 0 then
      Result := Copy(Result, 1, ClosingQuote - 1);
  end
  else
  begin
    Space := Pos(' ', Result);
    if Space > 0 then
      Result := Copy(Result, 1, Space - 1);
  end;
end;

function LegacyPerUserInstallExists(): Boolean;
var
  Uninstaller: String;
begin
  Result := ReadLegacyPerUserUninstaller(Uninstaller);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Uninstaller: String;
  ExitCode: Integer;
begin
  Result := '';
  if not ReadLegacyPerUserUninstaller(Uninstaller) then
    exit;

  Uninstaller := ExecutableFromCommandLine(Uninstaller);
  if not FileExists(Uninstaller) then
  begin
    Result := 'Lightflow found a previous per-user installation, but its uninstaller is missing. ' +
      'Remove the old Lightflow Studio entry from Installed Apps, then run this setup again.';
    exit;
  end;

  if (not WizardSilent) and
     (MsgBox('A previous per-user installation of Lightflow Studio was found.' + #13#10 + #13#10 +
       'Setup will remove the old application files before installing the all-users version. ' +
       'Your Catalog, Previews, settings, and logs will remain untouched.' + #13#10 + #13#10 +
       'Continue?', mbConfirmation, MB_YESNO or MB_DEFBUTTON1) <> IDYES) then
  begin
    Result := 'The existing per-user installation must be removed before the all-users installation can continue.';
    exit;
  end;

  Log('Removing the previous per-user Lightflow Studio installation before per-machine setup.');
  if (not Exec(Uninstaller, '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART', '', SW_HIDE,
      ewWaitUntilTerminated, ExitCode)) or (ExitCode <> 0) then
  begin
    Result := 'Setup could not remove the previous per-user Lightflow Studio installation. ' +
      'Close Lightflow Studio, uninstall the old version from Installed Apps, and try again.';
    exit;
  end;

  if LegacyPerUserInstallExists() then
    Result := 'The previous per-user Lightflow Studio registration remains after uninstall. ' +
      'Remove it from Installed Apps before continuing.';
end;
