Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not $IsWindows) {
    throw 'R40 lifecycle soak is Windows-only.'
}

if ($args.Count -ne 0) {
    throw 'R40 lifecycle soak accepts no command-line arguments.'
}

$serviceName = 'NetPilot.R40.Lab'
$proxyPort = 39036
$gracefulCycles = 8
$root = Join-Path $env:ProgramFiles 'NetPilot'
$ownerClient = Join-Path $root 'Lab\R40OwnerClient\NetPilot.ByeDpiR36LabClient.exe'
$evidence = Join-Path $PWD 'evidence\r40-graceful-soak.txt'

function Add-Evidence([string]$Value) {
    $Value | Add-Content -LiteralPath $evidence
    Write-Host $Value
}

function Assert-NoEngineResidue {
    if (@(Get-Process -Name ciadpi -ErrorAction SilentlyContinue).Count -ne 0) {
        throw 'R40 detected residual ciadpi.exe.'
    }

    if (@(Get-NetTCPConnection -State Listen -LocalPort $proxyPort -ErrorAction SilentlyContinue).Count -ne 0) {
        throw 'R40 detected residual ByeDPI loopback listener.'
    }
}

function Invoke-OwnerClient([string]$Stage) {
    $output = & $ownerClient 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) {
        Write-Host $output
        throw "$Stage failed with exit code $LASTEXITCODE."
    }

    return $output
}

New-Item -ItemType Directory -Force -Path (Split-Path -Parent $evidence) | Out-Null
Set-Content -LiteralPath $evidence -Value 'R40_GRACEFUL_SOAK_BEGIN=True'
Add-Evidence 'PRODUCTION_ADMISSION=BLOCK'
Add-Evidence "R40_GRACEFUL_CYCLES=$gracefulCycles"

$service = Get-CimInstance Win32_Service -Filter "Name='$serviceName'"
if ($null -eq $service -or $service.State -ne 'Running' -or $service.StartName -ne 'LocalSystem') {
    throw 'R40 expected a running LocalSystem lab service.'
}

if ([Diagnostics.Process]::GetCurrentProcess().SessionId -le 0) {
    throw 'R40 primary client requires a non-zero interactive session.'
}

if (-not (Test-Path -LiteralPath $ownerClient)) {
    throw 'R40 staged R36 typed IPC client is missing.'
}

Assert-NoEngineResidue

for ($cycle = 1; $cycle -le $gracefulCycles; $cycle++) {
    $output = Invoke-OwnerClient -Stage "graceful-cycle-$cycle"
    foreach ($marker in @(
        'AUTHENTICATED_IPC=PASS',
        'IPC_START=PASS',
        'IPC_SOCKS_HTTP=PASS',
        'IPC_STOP_CLEANUP=PASS',
        'RESULT=BYEDPI_LOCALSYSTEM_IPC_R36_PASS')) {
        if (-not $output.Contains($marker, [StringComparison]::Ordinal)) {
            throw "R40 graceful cycle $cycle missing marker $marker."
        }
    }

    Assert-NoEngineResidue
    Add-Evidence "R40_GRACEFUL_CYCLE_${cycle}=PASS"
}

Add-Evidence 'R40_GRACEFUL_SOAK=PASS'
Add-Evidence 'R40_GRACEFUL_FINAL_RESIDUE_ZERO=True'
Add-Evidence 'RESULT=R40_GRACEFUL_SOAK_PASS'
