#ifndef AppVersion
  #define AppVersion "0.1.0-alpha"
#endif
#ifndef VersionInfoVersion
  #define VersionInfoVersion "0.1.0.0"
#endif
#ifndef AppPublishDir
  #error AppPublishDir is required
#endif
#ifndef ByeDpiPackageDir
  #error ByeDpiPackageDir is required
#endif
#ifndef InstallerOutputDir
  #define InstallerOutputDir "..\artifacts\installer-user-mode"
#endif

#define AppName "NetPilot"
#define PublisherName "NetPilot Project"
#define ProjectUrl "https://github.com/DarrenDorr/NetPilot"

[Setup]
AppId={{8F3C254B-7C94-4A8E-B5AA-12000142124D}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#PublisherName}
AppPublisherURL={#ProjectUrl}
AppSupportURL={#ProjectUrl}/issues
AppUpdatesURL={#ProjectUrl}/releases
DefaultDirName={localappdata}\Programs\NetPilot
DefaultGroupName=NetPilot
DisableDirPage=yes
DisableProgramGroupPage=yes
AllowNoIcons=yes
AllowUNCPath=no
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=
SetupArchitecture=x64
ArchitecturesAllowed=x64os
MinVersion=10.0
WizardStyle=modern dynamic
Compression=lzma2/max
SolidCompression=yes
OutputDir={#InstallerOutputDir}
OutputBaseFilename=NetPilot-UserMode-Setup-{#AppVersion}-x64
SetupLogging=yes
UninstallLogging=yes
UninstallDisplayIcon={app}\App\NetPilot.exe
VersionInfoVersion={#VersionInfoVersion}
VersionInfoCompany={#PublisherName}
VersionInfoDescription=NetPilot user-mode installer
VersionInfoProductName=NetPilot
VersionInfoProductVersion={#VersionInfoVersion}
VersionInfoProductTextVersion={#AppVersion}
CloseApplications=yes
RestartApplications=no
RestartIfNeededByRun=no
SetupMutex=Local\NetPilot.UserMode.Setup.R42.12.4

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Tasks]
Name: "desktopicon"; Description: "Создать ярлык на рабочем столе"; GroupDescription: "Дополнительные задачи:"; Flags: unchecked

[Files]
Source: "{#AppPublishDir}\*"; DestDir: "{app}\App"; Flags: ignoreversion recursesubdirs createallsubdirs notimestamp
Source: "{#ByeDpiPackageDir}\bin\ciadpi.exe"; DestDir: "{app}\Engines\Managed\byedpi\0.17.3\x64\bin"; Flags: ignoreversion notimestamp
Source: "{#ByeDpiPackageDir}\LICENSE"; DestDir: "{app}\Engines\Managed\byedpi\0.17.3\x64"; Flags: ignoreversion notimestamp
Source: "..\LICENSE"; DestDir: "{app}\Docs"; DestName: "LICENSE.txt"; Flags: ignoreversion notimestamp
Source: "..\docs\THIRD-PARTY-NOTICES.md"; DestDir: "{app}\Docs"; Flags: ignoreversion notimestamp

[Icons]
Name: "{userprograms}\NetPilot"; Filename: "{app}\App\NetPilot.exe"; WorkingDir: "{app}\App"
Name: "{userprograms}\NetPilot — собрать диагностику"; Filename: "{app}\App\NetPilot.exe"; Parameters: "--collect-diagnostics"; WorkingDir: "{app}\App"
Name: "{userdesktop}\NetPilot"; Filename: "{app}\App\NetPilot.exe"; WorkingDir: "{app}\App"; Tasks: desktopicon

[Run]
Filename: "{app}\App\NetPilot.exe"; Description: "Запустить NetPilot"; WorkingDir: "{app}\App"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}\Engines"
