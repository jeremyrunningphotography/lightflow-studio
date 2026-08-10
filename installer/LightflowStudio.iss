#ifndef MyAppVersion
  #error MyAppVersion must be supplied by Build-Release.ps1
#endif
#ifndef SourceDir
  #error SourceDir must be supplied by Build-Release.ps1
#endif
#ifndef OutputDir
  #error OutputDir must be supplied by Build-Release.ps1
#endif

#define MyAppName "Lightflow Studio"
#define MyAppPublisher "Jeremy Running Photography"
#define MyAppExeName "LightflowStudio.exe"

[Setup]
AppId={{E67863CA-C113-438B-B1C5-6019D174CCFD}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\Lightflow Studio
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir={#OutputDir}
OutputBaseFilename=LightflowStudio-{#MyAppVersion}-win-x64-setup
SetupIconFile={#SourceDir}\LightflowStudio.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
#ifdef ValidationBuild
Compression=zip
SolidCompression=no
#else
Compression=lzma2/ultra64
SolidCompression=yes
#endif
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
LicenseFile={#SourceDir}\THIRD-PARTY-NOTICES.md
VersionInfoVersion={#MyAppVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription={#MyAppName} installer
VersionInfoProductName={#MyAppName}

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent
