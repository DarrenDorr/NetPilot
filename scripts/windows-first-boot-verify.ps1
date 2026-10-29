[CmdletBinding()]
param(
    [switch]$AllowExistingEngine
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Require-OutputLine(
    [string[]]$Output,
    [string]$Expected,
    [string]$FailureMessage) {
    if (-not ($Output -contains $Expected)) {
        throw "$FailureMessage Expected line: $Expected"
    }
}

if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
    throw 'R31 First Boot verifier должен выполняться на Windows.'
}

if (-not [Environment]::Is64BitOperatingSystem) {
    throw 'Для NetPilot First Boot требуется Windows x64.'
}

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
$currentSid = $identity.User.Value
$isAdmin = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
$sessionId = (Get-Process -Id $PID).SessionId

Write-Host '=== NetPilot R31 First Boot verifier ==='
Write-Host "IDENTITY=$($identity.Name)"
Write-Host "SID=$currentSid"
Write-Host "SESSION_ID=$sessionId"
Write-Host "IS_ADMIN=$isAdmin"

if ($identity.IsSystem) {
    throw 'Verifier нельзя запускать как LocalSystem.'
}
if ($sessionId -eq 0) {
    throw 'Verifier должен выполняться в интерактивной пользовательской сессии.'
}
if ($isAdmin) {
    throw 'Verifier должен запускаться обычным пользователем. Не используйте Run as administrator.'
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$installationRoot = Join-Path $env:ProgramFiles 'NetPilot'
$appExe = Join-Path $installationRoot 'App\NetPilot.exe'
$serviceExe = Join-Path $installationRoot 'Service\NetPilot.Service.exe'
$ownerSidPath = Join-Path $installationRoot 'Config\authorized-user.sid'
$noncePath = Join-Path $installationRoot 'Config\enrollment.nonce'
$sessionPath = Join-Path $installationRoot 'Config\enrollment.session'
$clientProject = Join-Path $repoRoot 'tools\NetPilot.WindowsIpcLabClient\NetPilot.WindowsIpcLabClient.csproj'

foreach ($requiredPath in @($appExe, $serviceExe, $ownerSidPath, $clientProject)) {
    if (-not (Test-Path $requiredPath)) {
        throw "Обязательный First Boot объект не найден: $requiredPath"
    }
}

$service = Get-CimInstance Win32_Service -Filter "Name='NetPilot.Service'" -ErrorAction Stop
if ($null -eq $service) {
    throw 'NetPilot.Service не зарегистрирована.'
}
if ($service.StartName -ne 'LocalSystem') {
    throw "NetPilot.Service работает не от LocalSystem: $($service.StartName)"
}
if ($service.StartMode -ne 'Auto') {
    throw "NetPilot.Service имеет неожиданный StartMode: $($service.StartMode)"
}
if ($service.State -ne 'Running') {
    throw "NetPilot.Service не запущена: $($service.State)"
}

Write-Host 'SERVICE_ACCOUNT=LocalSystem'
Write-Host 'SERVICE_START_MODE=Auto'
Write-Host 'SERVICE_STATE=Running'

$ownerSid = (Get-Content $ownerSidPath -Raw).Trim()
if ($ownerSid -ne $currentSid) {
    throw "First Boot должен выполняться installation owner. Expected $currentSid, found $ownerSid"
}
Write-Host 'INSTALLATION_OWNER_MATCH=True'

if (Test-Path $noncePath) {
    throw 'Enrollment nonce остался после установки.'
}
if (Test-Path $sessionPath) {
    throw 'Enrollment session marker остался после установки.'
}
Write-Host 'ENROLLMENT_ARTIFACTS_CLEAN=True'

$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if ($null -eq $dotnet) {
    throw '.NET SDK не найден; невозможно собрать read-only First Boot IPC probe.'
}

Write-Host ''
Write-Host 'Running read-only authenticated IPC probe...'
$output = @(& dotnet run --project $clientProject --configuration Release -- --expect-authorized 2>&1)
$probeExitCode = $LASTEXITCODE
$output | ForEach-Object { Write-Host $_ }

if ($probeExitCode -ne 0) {
    throw "First Boot IPC probe завершился с кодом $probeExitCode."
}

Require-OutputLine $output 'IS_ADMIN=False' 'IPC probe запущен в неправильном security context.'
Require-OutputLine $output 'SERVICE_SUCCESS=True' 'Ping к LocalSystem-службе не подтверждён.'
Require-OutputLine $output 'SERVICE_CODE=Ok' 'Служба не вернула ожидаемый Ping=Ok.'
Require-OutputLine $output 'ENGINE_STATUS_SUCCESS=True' 'Read-only engine status IPC не подтверждён.'
Require-OutputLine $output 'ENGINE_STATUS_CODE=Ok' 'Служба не вернула безопасный engine status.'
Require-OutputLine $output 'PROFILE_DIRECT_ACCESS=DENIED' 'Per-SID профиль не подтверждён как service-only.'
Require-OutputLine $output 'RESULT=AUTHORIZED_PASS' 'Authenticated IPC probe не завершился успешно.'

$engineInstalledLine = $output | Where-Object { $_ -like 'ENGINE_INSTALLED=*' } | Select-Object -Last 1
if ([string]::IsNullOrWhiteSpace($engineInstalledLine)) {
    throw 'IPC probe не сообщил read-only состояние engine installation.'
}

$engineInstalled = $engineInstalledLine.Substring('ENGINE_INSTALLED='.Length).Trim()
if (-not $AllowExistingEngine -and $engineInstalled -ne 'False') {
    throw 'Для чистого R31 First Boot native/legacy engine не должен быть установлен. Используйте -AllowExistingEngine только для осознанной проверки существующей установки.'
}

$serviceAfter = Get-CimInstance Win32_Service -Filter "Name='NetPilot.Service'" -ErrorAction Stop
if ($serviceAfter.State -ne 'Running') {
    throw "NetPilot.Service перестала работать после IPC probe: $($serviceAfter.State)"
}

Write-Host ''
Write-Host '=== FIRST BOOT PASS ==='
Write-Host 'GUI_INSTALLATION_PRESENT=True'
Write-Host 'LOCALSYSTEM_SERVICE=True'
Write-Host 'INSTALLATION_OWNER_ENROLLED=True'
Write-Host 'AUTHENTICATED_IPC=True'
Write-Host 'PER_SID_PROFILE_SERVICE_ONLY=True'
Write-Host "ENGINE_INSTALLED=$engineInstalled"
Write-Host 'NATIVE_ENGINE_EXECUTION_TESTED=False'
Write-Host 'NATIVE_ENGINE_ADMISSION=BLOCKED'
Write-Host 'RESULT=FIRST_BOOT_PASS'
