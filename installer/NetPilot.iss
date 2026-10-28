#ifndef AppVersion
  #define AppVersion "0.1.0-alpha"
#endif
#ifndef VersionInfoVersion
  #define VersionInfoVersion "0.1.0.0"
#endif
#ifndef AppPublishDir
  #error AppPublishDir is required
#endif
#ifndef ServicePublishDir
  #error ServicePublishDir is required
#endif
#ifndef InstallerOutputDir
  #define InstallerOutputDir "..\artifacts\installer"
#endif

#define AppName "NetPilot"
#define ServiceName "NetPilot.Service"
#define ServiceDisplayName "NetPilot Service"
#define PublisherName "NetPilot Project"
#define ProjectUrl "https://github.com/raybalado989-lang/NetPilot"

[Setup]
AppId={{6E9D5D5C-2D0A-4E10-96B0-C56A80FDE214}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#PublisherName}
AppPublisherURL={#ProjectUrl}
AppSupportURL={#ProjectUrl}/issues
AppUpdatesURL={#ProjectUrl}/releases
DefaultDirName={autopf}\NetPilot
DefaultGroupName=NetPilot
DisableDirPage=yes
DisableProgramGroupPage=yes
AllowNoIcons=yes
AllowUNCPath=no
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=
SetupArchitecture=x64
ArchitecturesAllowed=x64os
MinVersion=10.0
WizardStyle=modern dynamic
Compression=lzma2/max
SolidCompression=yes
OutputDir={#InstallerOutputDir}
OutputBaseFilename=NetPilot-Setup-{#AppVersion}-x64
SetupLogging=yes
UninstallLogging=yes
UninstallDisplayIcon={app}\App\NetPilot.exe
VersionInfoVersion={#VersionInfoVersion}
VersionInfoCompany={#PublisherName}
VersionInfoDescription=Установщик NetPilot
VersionInfoProductName=NetPilot
VersionInfoProductVersion={#AppVersion}
CloseApplications=yes
RestartApplications=no
RestartIfNeededByRun=no
SetupMutex=Global\NetPilot.Setup.R14

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Tasks]
Name: "desktopicon"; Description: "Создать ярлык на рабочем столе"; GroupDescription: "Дополнительные задачи:"; Flags: unchecked

[Dirs]
Name: "{app}\Engines"
Name: "{app}\Engines\Zapret"

[Files]
Source: "{#AppPublishDir}\*"; DestDir: "{app}\App"; Flags: ignoreversion recursesubdirs createallsubdirs notimestamp
Source: "{#ServicePublishDir}\*"; DestDir: "{app}\Service"; Flags: ignoreversion recursesubdirs createallsubdirs notimestamp
Source: "..\LICENSE"; DestDir: "{app}\Docs"; DestName: "LICENSE.txt"; Flags: ignoreversion notimestamp
Source: "..\docs\THIRD-PARTY-NOTICES.md"; DestDir: "{app}\Docs"; Flags: ignoreversion notimestamp

[Icons]
Name: "{autoprograms}\NetPilot"; Filename: "{app}\App\NetPilot.exe"; WorkingDir: "{app}\App"
Name: "{autodesktop}\NetPilot"; Filename: "{app}\App\NetPilot.exe"; WorkingDir: "{app}\App"; Tasks: desktopicon

[Run]
Filename: "{app}\App\NetPilot.exe"; Description: "Запустить NetPilot"; WorkingDir: "{app}\App"; Flags: nowait postinstall skipifsilent runasoriginaluser

[UninstallDelete]
Type: filesandordirs; Name: "{app}\Engines"

[Code]
const
  NetPilotServiceName = '{#ServiceName}';
  NetPilotServiceDisplayName = '{#ServiceDisplayName}';

function Quote(const Value: String): String;
begin
  Result := '"' + Value + '"';
end;

function ScExe: String;
begin
  Result := ExpandConstant('{sys}\sc.exe');
end;

function RunSc(const Parameters: String; const AllowNonZero: Boolean; var ResultCode: Integer): Boolean;
begin
  Log('SC: ' + Parameters);
  Result := Exec(
    ScExe,
    Parameters,
    '',
    SW_SHOWNORMAL,
    ewWaitUntilTerminated,
    ResultCode);

  if not Result then
  begin
    Log('Не удалось запустить sc.exe. Код Windows: ' + IntToStr(ResultCode));
    Exit;
  end;

  if (ResultCode <> 0) and (not AllowNonZero) then
  begin
    Log('sc.exe завершился с кодом: ' + IntToStr(ResultCode));
    Result := False;
  end;
end;

function ServiceExists: Boolean;
var
  ResultCode: Integer;
begin
  Result := RunSc('query ' + Quote(NetPilotServiceName), True, ResultCode) and (ResultCode = 0);
end;

function QueryServiceStopped: Boolean;
var
  I: Integer;
  ResultCode: Integer;
  Output: TExecOutput;
begin
  Result := False;

  try
    if not ExecAndCaptureOutput(
      ScExe,
      'query ' + Quote(NetPilotServiceName),
      '',
      SW_SHOWNORMAL,
      ewWaitUntilTerminated,
      ResultCode,
      Output) then
      Exit;
  except
    Log('Не удалось получить состояние службы: ' + GetExceptionMessage);
    Exit;
  end;

  if ResultCode = 1060 then
  begin
    Result := True;
    Exit;
  end;

  if (ResultCode <> 0) or Output.Error then
    Exit;

  for I := 0 to GetArrayLength(Output.StdOut) - 1 do
  begin
    { Числовое состояние 1 означает SERVICE_STOPPED и не зависит от языка Windows. }
    if Pos(': 1 ', Output.StdOut[I]) > 0 then
    begin
      Result := True;
      Exit;
    end;
  end;
end;

function StopServiceAndWait: Boolean;
var
  Attempt: Integer;
  ResultCode: Integer;
begin
  Result := True;

  if not ServiceExists then
    Exit;

  if QueryServiceStopped then
    Exit;

  { 1062 (service not active) допустим; фактическое состояние проверяем ниже. }
  RunSc('stop ' + Quote(NetPilotServiceName), True, ResultCode);

  for Attempt := 1 to 40 do
  begin
    if QueryServiceStopped then
      Exit;

    Sleep(250);
  end;

  Result := False;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  NeedsRestart := False;

  if not StopServiceAndWait then
    Result := 'Не удалось безопасно остановить службу NetPilot. Закройте NetPilot и повторите установку.';
end;

procedure ConfigureAndStartService;
var
  ResultCode: Integer;
  ServiceExe: String;
  Parameters: String;
begin
  ServiceExe := ExpandConstant('{app}\Service\NetPilot.Service.exe');

  if ServiceExists then
  begin
    Parameters :=
      'config ' + Quote(NetPilotServiceName) +
      ' binPath= ' + Quote(ServiceExe) +
      ' start= auto obj= LocalSystem DisplayName= ' + Quote(NetPilotServiceDisplayName);
  end
  else
  begin
    Parameters :=
      'create ' + Quote(NetPilotServiceName) +
      ' binPath= ' + Quote(ServiceExe) +
      ' start= auto obj= LocalSystem DisplayName= ' + Quote(NetPilotServiceDisplayName);
  end;

  if not RunSc(Parameters, False, ResultCode) then
    RaiseException('Не удалось зарегистрировать службу NetPilot.');

  if not RunSc(
    'description ' + Quote(NetPilotServiceName) +
    ' ' + Quote('Защищённая локальная служба сетевых движков NetPilot.'),
    False,
    ResultCode) then
    RaiseException('Не удалось настроить описание службы NetPilot.');

  { Доступ к Service Control Manager: только LocalSystem и локальные администраторы. }
  if not RunSc(
    'sdset ' + Quote(NetPilotServiceName) +
    ' D:(A;;GA;;;SY)(A;;GA;;;BA)',
    False,
    ResultCode) then
    RaiseException('Не удалось применить защищённый ACL службы NetPilot.');

  { Две контролируемые попытки перезапуска при аварии, затем остановка. }
  if not RunSc(
    'failure ' + Quote(NetPilotServiceName) +
    ' reset= 86400 actions= restart/5000/restart/15000',
    False,
    ResultCode) then
    RaiseException('Не удалось настроить восстановление службы NetPilot.');

  RunSc('failureflag ' + Quote(NetPilotServiceName) + ' 1', True, ResultCode);

  if not RunSc('start ' + Quote(NetPilotServiceName), False, ResultCode) then
    RaiseException('Служба NetPilot установлена, но не смогла запуститься.');
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    ConfigureAndStartService;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ResultCode: Integer;
begin
  if CurUninstallStep = usUninstall then
  begin
    if ServiceExists then
    begin
      StopServiceAndWait;
      RunSc('delete ' + Quote(NetPilotServiceName), True, ResultCode);
    end;
  end;
end;
