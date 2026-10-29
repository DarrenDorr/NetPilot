[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$OtherUser,

    [string]$RunnerRoot = ""
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$failures = [System.Collections.Generic.List[string]]::new()
$warnings = [System.Collections.Generic.List[string]]::new()

function Write-Pass([string]$Message) {
    Write-Host "[PASS] $Message" -ForegroundColor Green
}

function Write-Warn([string]$Message) {
    $warnings.Add($Message)
    Write-Host "[WARN] $Message" -ForegroundColor Yellow
}

function Write-Fail([string]$Message) {
    $failures.Add($Message)
    Write-Host "[FAIL] $Message" -ForegroundColor Red
}

function Resolve-LocalUserName([string]$Value) {
    if ($Value.StartsWith('.\')) {
        return $Value.Substring(2)
    }

    $separatorIndex = $Value.IndexOf('\')
    if ($separatorIndex -ge 0) {
        $machinePart = $Value.Substring(0, $separatorIndex)
        $userPart = $Value.Substring($separatorIndex + 1)

        if ([string]::IsNullOrWhiteSpace($userPart)) {
            throw 'Имя Account B после разделителя не задано.'
        }

        if ($machinePart -ne $env:COMPUTERNAME) {
            throw "Для R29 preflight используйте локальную учётную запись Account B в формате .\\Имя или $env:COMPUTERNAME\\Имя."
        }

        return $userPart
    }

    return $Value
}

Write-Host 'NetPilot R29 Windows UAC lab preflight'
Write-Host 'Этот скрипт ничего не устанавливает, не повышает привилегии и не изменяет системные настройки.'
Write-Host ''

if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
    Write-Fail 'R29 lab должен выполняться на Windows.'
} else {
    Write-Pass 'Windows обнаружена.'
}

if (-not [Environment]::Is64BitOperatingSystem) {
    Write-Fail 'Требуется 64-битная Windows.'
} else {
    Write-Pass 'ОС x64.'
}

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
$currentSid = $identity.User.Value
$isAdmin = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
$sessionId = (Get-Process -Id $PID).SessionId

Write-Host "Current identity: $($identity.Name)"
Write-Host "Current SID:      $currentSid"
Write-Host "Session ID:       $sessionId"

if ($identity.IsSystem) {
    Write-Fail 'Текущий процесс работает как LocalSystem. Runner должен запускаться обычным пользователем.'
} else {
    Write-Pass 'Текущий процесс не LocalSystem.'
}

if ($sessionId -eq 0) {
    Write-Fail 'Session 0 запрещён. GitHub runner должен быть запущен интерактивно в пользовательской сессии.'
} else {
    Write-Pass 'Интерактивная пользовательская сессия обнаружена.'
}

if ($isAdmin) {
    Write-Fail 'Текущий token входит в Administrators. Account A для R29 должен быть standard user.'
} else {
    Write-Pass 'Account A работает без административного token.'
}

try {
    $uac = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System' -Name EnableLUA -ErrorAction Stop
    if ($uac.EnableLUA -ne 1) {
        Write-Fail 'UAC выключен (EnableLUA != 1).'
    } else {
        Write-Pass 'UAC включён (EnableLUA=1).'
    }
} catch {
    Write-Fail "Не удалось проверить UAC: $($_.Exception.Message)"
}

if ($null -eq (Get-Command pwsh -ErrorAction SilentlyContinue)) {
    Write-Fail 'Команда pwsh не найдена. Self-hosted workflow использует PowerShell 7+.'
} else {
    $pwshVersion = (& pwsh -NoLogo -NoProfile -Command '$PSVersionTable.PSVersion.ToString()').Trim()
    Write-Pass "PowerShell 7+ доступен: $pwshVersion"
}

if ($null -eq (Get-Command git -ErrorAction SilentlyContinue)) {
    Write-Warn 'git не найден в PATH. actions/checkout на self-hosted runner обычно требует Git for Windows.'
} else {
    $gitVersion = (& git --version).Trim()
    Write-Pass $gitVersion
}

if ($null -eq (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Warn '.NET SDK сейчас не найден. Workflow установит .NET 10 через actions/setup-dotnet, если runner имеет интернет-доступ.'
} else {
    $sdks = @(& dotnet --list-sdks)
    if ($sdks | Where-Object { $_ -match '^10\.' }) {
        Write-Pass '.NET 10 SDK уже установлен.'
    } else {
        Write-Warn '.NET SDK найден, но локального 10.x нет. Workflow попытается установить его через actions/setup-dotnet.'
    }
}

try {
    $secondaryLogon = Get-Service -Name 'seclogon' -ErrorAction Stop
    if ($secondaryLogon.StartType -eq 'Disabled') {
        Write-Fail 'Secondary Logon (seclogon) отключён. Он нужен для negative test под Account B.'
    } else {
        Write-Pass "Secondary Logon доступен (Status=$($secondaryLogon.Status), StartType=$($secondaryLogon.StartType))."
    }
} catch {
    Write-Fail "Не удалось проверить Secondary Logon: $($_.Exception.Message)"
}

try {
    $otherLocalName = Resolve-LocalUserName -Value $OtherUser
    $other = Get-LocalUser -Name $otherLocalName -ErrorAction Stop
    $otherSid = $other.SID.Value

    Write-Host "Account B:        $($other.Name)"
    Write-Host "Account B SID:    $otherSid"

    if (-not $other.Enabled) {
        Write-Fail 'Account B отключён.'
    } else {
        Write-Pass 'Account B включён.'
    }

    if ($otherSid -eq $currentSid) {
        Write-Fail 'Account B имеет тот же SID, что и Account A.'
    } else {
        Write-Pass 'Account B имеет отдельный SID.'
    }

    $admins = Get-LocalGroup -SID 'S-1-5-32-544' -ErrorAction Stop
    $adminMembers = @(Get-LocalGroupMember -Group $admins -ErrorAction Stop)
    if ($adminMembers | Where-Object { $_.SID.Value -eq $otherSid }) {
        Write-Fail 'Account B входит в локальную группу Administrators. Для negative test нужен standard user.'
    } else {
        Write-Pass 'Account B не является прямым членом локальной группы Administrators.'
    }
} catch {
    Write-Fail "Не удалось подтвердить локальный Account B '$OtherUser': $($_.Exception.Message)"
}

$existingService = Get-Service -Name 'NetPilot.Service' -ErrorAction SilentlyContinue
if ($null -ne $existingService) {
    Write-Fail "На машине уже зарегистрирован NetPilot.Service (Status=$($existingService.Status)). Для чистого R29 lab его не должно быть."
} else {
    Write-Pass 'NetPilot.Service до теста отсутствует.'
}

$existingInstall = Join-Path $env:ProgramFiles 'NetPilot'
if (Test-Path $existingInstall) {
    Write-Fail "Обнаружен существующий каталог установки: $existingInstall. R29 должен начинаться с чистого состояния."
} else {
    Write-Pass 'Существующий Program Files\\NetPilot не обнаружен.'
}

if (-not [string]::IsNullOrWhiteSpace($RunnerRoot)) {
    $runnerConfig = Join-Path $RunnerRoot '.runner'
    $runnerExe = Join-Path $RunnerRoot 'run.cmd'

    if (-not (Test-Path $runnerConfig)) {
        Write-Fail "В RunnerRoot отсутствует .runner: $RunnerRoot"
    } else {
        Write-Pass 'GitHub Actions runner зарегистрирован (.runner найден).'
    }

    if (-not (Test-Path $runnerExe)) {
        Write-Fail "В RunnerRoot отсутствует run.cmd: $RunnerRoot"
    } else {
        Write-Pass 'run.cmd найден. Для R29 запускайте runner именно интерактивно через run.cmd, не как Windows service.'
    }
} else {
    Write-Warn 'RunnerRoot не указан. Локальная регистрация GitHub Actions runner не проверялась.'
}

try {
    $https = Test-NetConnection -ComputerName 'github.com' -Port 443 -WarningAction SilentlyContinue
    if ($https.TcpTestSucceeded) {
        Write-Pass 'github.com:443 доступен.'
    } else {
        Write-Fail 'Нет TCP-доступа к github.com:443.'
    }
} catch {
    Write-Warn "Не удалось проверить github.com:443: $($_.Exception.Message)"
}

Write-Host ''
Write-Host '--- R29 preflight summary ---'
Write-Host "Failures: $($failures.Count)"
Write-Host "Warnings: $($warnings.Count)"

if ($warnings.Count -gt 0) {
    foreach ($warning in $warnings) {
        Write-Host "WARN: $warning" -ForegroundColor Yellow
    }
}

if ($failures.Count -gt 0) {
    foreach ($failure in $failures) {
        Write-Host "FAIL: $failure" -ForegroundColor Red
    }
    Write-Host 'RESULT=BLOCKED' -ForegroundColor Red
    exit 1
}

Write-Host 'RESULT=READY_FOR_R29_RUNNER_SETUP' -ForegroundColor Green
exit 0
