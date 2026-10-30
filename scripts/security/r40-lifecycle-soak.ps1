Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not $IsWindows) {
    throw 'R40 lifecycle soak is Windows-only.'
}

if ($args.Count -ne 0) {
    throw 'R40 lifecycle soak accepts no command-line arguments.'
}

$serviceName = 'NetPilot.R40.Lab'
$secondUserName = 'NetPilotR40B'
$proxyPort = 39036
$gracefulCycles = 8
$crashCycles = 4
$handoffCycles = 4
$root = Join-Path $env:ProgramFiles 'NetPilot'
$ownerClient = Join-Path $root 'Lab\R40OwnerClient\NetPilot.ByeDpiR36LabClient.exe'
$restartClient = Join-Path $root 'Lab\R40RestartClient\NetPilot.ByeDpiR39RestartClient.exe'
$scratch = Join-Path $env:ProgramData 'NetPilotR40'
$evidence = Join-Path $PWD 'evidence\r40-soak.txt'

function Add-Evidence([string]$Value) {
    $Value | Add-Content -LiteralPath $evidence
    Write-Host $Value
}

function Assert-ServiceRunning {
    $svc = Get-CimInstance Win32_Service -Filter "Name='$serviceName'"
    if ($null -eq $svc -or $svc.State -ne 'Running' -or $svc.StartName -ne 'LocalSystem' -or [int]$svc.ProcessId -le 0) {
        throw 'R40 expected a running LocalSystem lab service.'
    }
    return $svc
}

function Assert-NoEngineResidue {
    if (@(Get-Process -Name ciadpi -ErrorAction SilentlyContinue).Count -ne 0) {
        throw 'R40 detected residual ciadpi.exe.'
    }
    if (@(Get-NetTCPConnection -State Listen -LocalPort $proxyPort -ErrorAction SilentlyContinue).Count -ne 0) {
        throw 'R40 detected residual ByeDPI loopback listener.'
    }
}

function Assert-OneEngineRunning {
    $processes = @(Get-Process -Name ciadpi -ErrorAction SilentlyContinue)
    if ($processes.Count -ne 1) {
        throw "R40 expected exactly one ciadpi.exe but found $($processes.Count)."
    }
    $listeners = @(Get-NetTCPConnection -State Listen -LocalPort $proxyPort -ErrorAction SilentlyContinue)
    if ($listeners.Count -ne 1) {
        throw "R40 expected exactly one loopback listener but found $($listeners.Count)."
    }
}

function Invoke-Client([string]$Executable, [string[]]$Arguments, [string]$Stage) {
    $output = & $Executable @Arguments 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) {
        Write-Host $output
        throw "$Stage failed with exit code $LASTEXITCODE."
    }
    return $output
}

function Wait-ServiceState([string]$Expected, [int]$Seconds = 20) {
    $deadline = (Get-Date).AddSeconds($Seconds)
    do {
        Start-Sleep -Milliseconds 200
        $state = (Get-Service -Name $serviceName).Status.ToString()
        if ($state -eq $Expected) { return }
    } while ((Get-Date) -lt $deadline)
    throw "R40 service did not reach $Expected."
}

function Wait-NoEngineResidue([int]$Seconds = 15) {
    $deadline = (Get-Date).AddSeconds($Seconds)
    do {
        Start-Sleep -Milliseconds 150
        $engineGone = @(Get-Process -Name ciadpi -ErrorAction SilentlyContinue).Count -eq 0
        $listenerGone = @(Get-NetTCPConnection -State Listen -LocalPort $proxyPort -ErrorAction SilentlyContinue).Count -eq 0
        if ($engineGone -and $listenerGone) { return }
    } while ((Get-Date) -lt $deadline)
    throw 'R40 old engine/listener did not disappear after service termination.'
}

function Invoke-SecondUserClient(
    [Management.Automation.PSCredential]$Credential,
    [string]$Mode,
    [string]$Stage)
{
    $clientDir = Split-Path -Parent $restartClient
    $stdout = Join-Path $scratch "$Stage.out.txt"
    $stderr = Join-Path $scratch "$Stage.err.txt"
    Remove-Item -LiteralPath $stdout,$stderr -Force -ErrorAction SilentlyContinue

    $process = Start-Process `
        -FilePath $restartClient `
        -ArgumentList $Mode `
        -WorkingDirectory $clientDir `
        -Credential $Credential `
        -LoadUserProfile `
        -Wait `
        -PassThru `
        -RedirectStandardOutput $stdout `
        -RedirectStandardError $stderr

    $out = if (Test-Path -LiteralPath $stdout) { Get-Content -LiteralPath $stdout -Raw } else { '' }
    $err = if (Test-Path -LiteralPath $stderr) { Get-Content -LiteralPath $stderr -Raw } else { '' }
    if ($process.ExitCode -ne 0) {
        Write-Host $out
        Write-Host $err
        throw "$Stage failed with exit code $($process.ExitCode)."
    }
    return ($out + [Environment]::NewLine + $err)
}

New-Item -ItemType Directory -Force -Path (Split-Path -Parent $evidence),$scratch | Out-Null
Set-Content -LiteralPath $evidence -Value 'R40_LIFECYCLE_SOAK_BEGIN=True'
Add-Evidence 'PRODUCTION_ADMISSION=BLOCK'
Add-Evidence "R40_GRACEFUL_CYCLES=$gracefulCycles"
Add-Evidence "R40_CRASH_CYCLES=$crashCycles"
Add-Evidence "R40_HANDOFF_CYCLES=$handoffCycles"

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'R40 lifecycle soak requires an administrative hosted Windows runner.'
}
if ([Diagnostics.Process]::GetCurrentProcess().SessionId -le 0) {
    throw 'R40 primary client requires a non-zero interactive session.'
}
if (-not (Test-Path -LiteralPath $ownerClient) -or -not (Test-Path -LiteralPath $restartClient)) {
    throw 'R40 staged fixed clients are missing.'
}

Assert-ServiceRunning | Out-Null
Assert-NoEngineResidue

# The first normal R36 cycle also completes installation-owner enrollment if needed.
for ($cycle = 1; $cycle -le $gracefulCycles; $cycle++) {
    $output = Invoke-Client $ownerClient @() "graceful-cycle-$cycle"
    foreach ($marker in @('AUTHENTICATED_IPC=PASS','IPC_START=PASS','IPC_SOCKS_HTTP=PASS','IPC_STOP_CLEANUP=PASS','RESULT=BYEDPI_LOCALSYSTEM_IPC_R36_PASS')) {
        if (-not $output.Contains($marker,[StringComparison]::Ordinal)) {
            throw "R40 graceful cycle $cycle missing marker $marker."
        }
    }
    Assert-NoEngineResidue
    Add-Evidence "R40_GRACEFUL_CYCLE_${cycle}=PASS"
}
Add-Evidence 'R40_GRACEFUL_SOAK=PASS'

for ($cycle = 1; $cycle -le $crashCycles; $cycle++) {
    $start = Invoke-Client $ownerClient @('--start-for-service-stop') "crash-start-$cycle"
    foreach ($marker in @('AUTHENTICATED_IPC=PASS','IPC_START=PASS','START_FOR_SERVICE_STOP=PASS','RESULT=R36_START_FOR_SERVICE_STOP_PASS')) {
        if (-not $start.Contains($marker,[StringComparison]::Ordinal)) {
            throw "R40 crash cycle $cycle missing start marker $marker."
        }
    }
    Assert-OneEngineRunning

    $svc = Assert-ServiceRunning
    $servicePid = [int]$svc.ProcessId
    $expectedServicePath = [IO.Path]::GetFullPath((Join-Path $root 'Lab\R40Service\NetPilot.ByeDpiR36ServiceLab.exe'))
    $actualServicePath = [IO.Path]::GetFullPath((Get-Process -Id $servicePid).Path)
    if (-not [string]::Equals($expectedServicePath,$actualServicePath,[StringComparison]::OrdinalIgnoreCase)) {
        throw 'R40 refusing to terminate service because PID executable path mismatched.'
    }

    Stop-Process -Id $servicePid -Force
    Wait-ServiceState 'Stopped'
    Wait-NoEngineResidue

    Start-Service -Name $serviceName
    Wait-ServiceState 'Running'
    $status = Invoke-Client $restartClient @('--expect-stopped-after-restart') "crash-reconcile-$cycle"
    foreach ($marker in @('R39_AUTHENTICATED_IPC=PASS','R39_POST_RESTART_STATUS_STOPPED=PASS','R39_POST_RESTART_NO_LISTENER=PASS','RESULT=R39_POST_RESTART_STOPPED_PASS')) {
        if (-not $status.Contains($marker,[StringComparison]::Ordinal)) {
            throw "R40 crash cycle $cycle missing reconcile marker $marker."
        }
    }
    Assert-NoEngineResidue
    Add-Evidence "R40_CRASH_RESTART_CYCLE_${cycle}=PASS"
}
Add-Evidence 'R40_CRASH_RESTART_SOAK=PASS'

$passwordPlain = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(24)) + 'aA1!'
$password = ConvertTo-SecureString $passwordPlain -AsPlainText -Force
New-LocalUser -Name $secondUserName -Password $password -AccountNeverExpires -PasswordNeverExpires | Out-Null
$usersGroup = (Get-LocalGroup -SID 'S-1-5-32-545').Name
Add-LocalGroupMember -Group $usersGroup -Member $secondUserName
& icacls.exe $scratch /grant:r '*S-1-5-32-544:(OI)(CI)F' '*S-1-5-32-545:(OI)(CI)M' | Out-Host
if ($LASTEXITCODE -ne 0) { throw 'R40 failed to prepare second-user scratch ACL.' }

$primarySid = $identity.User.Value
$secondSid = (Get-LocalUser -Name $secondUserName).SID.Value
if ([string]::Equals($primarySid,$secondSid,[StringComparison]::OrdinalIgnoreCase)) {
    throw 'R40 second user SID unexpectedly matches primary SID.'
}
Add-Evidence 'R40_SECOND_SID_DISTINCT=True'
$credential = [Management.Automation.PSCredential]::new("$env:COMPUTERNAME\$secondUserName",$password)

for ($cycle = 1; $cycle -le $handoffCycles; $cycle++) {
    $freshStart = Invoke-SecondUserClient $credential '--start-fresh-owner' "handoff-start-$cycle"
    foreach ($marker in @('R39_AUTHENTICATED_IPC=PASS','R39_SESSION_NONZERO=True','R39_FRESH_OWNER_START=PASS','R39_FRESH_OWNER_LISTENER=PASS','RESULT=R39_FRESH_OWNER_START_PASS')) {
        if (-not $freshStart.Contains($marker,[StringComparison]::Ordinal)) {
            throw "R40 handoff cycle $cycle missing second-owner marker $marker."
        }
    }
    Assert-OneEngineRunning

    $oldOwner = Invoke-Client $restartClient @('--expect-nonowner-denied') "handoff-old-owner-$cycle"
    foreach ($marker in @('R39_OLD_OWNER_START_DENIED=PASS','R39_OLD_OWNER_STOP_DENIED=PASS','R39_FRESH_LEASE_PRESERVED=PASS','RESULT=R39_OLD_OWNER_DENIAL_PASS')) {
        if (-not $oldOwner.Contains($marker,[StringComparison]::Ordinal)) {
            throw "R40 handoff cycle $cycle missing old-owner denial marker $marker."
        }
    }
    Assert-OneEngineRunning

    $freshStop = Invoke-SecondUserClient $credential '--fresh-owner-stop' "handoff-stop-$cycle"
    foreach ($marker in @('R39_FRESH_OWNER_STOP=PASS','R39_FRESH_OWNER_CLEANUP=PASS','RESULT=R39_FRESH_OWNER_STOP_PASS')) {
        if (-not $freshStop.Contains($marker,[StringComparison]::Ordinal)) {
            throw "R40 handoff cycle $cycle missing fresh-owner stop marker $marker."
        }
    }
    Assert-NoEngineResidue
    Add-Evidence "R40_SID_HANDOFF_CYCLE_${cycle}=PASS"
}
Add-Evidence 'R40_SID_HANDOFF_SOAK=PASS'

$profileRoot = Join-Path $root 'Users'
$profileNames = @(Get-ChildItem -LiteralPath $profileRoot -Directory -Force | Select-Object -ExpandProperty Name)
$expectedProfiles = @($primarySid,$secondSid)
if ($profileNames.Count -ne 2) {
    throw "R40 expected exactly two service-owned SID profiles but found $($profileNames.Count)."
}
foreach ($sid in $expectedProfiles) {
    if ($profileNames -notcontains $sid) {
        throw "R40 expected SID profile is missing: $sid"
    }
}
Add-Evidence 'R40_PROFILE_SET_EXACT=True'

$configRoot = Join-Path $root 'Config'
if (Test-Path -LiteralPath (Join-Path $configRoot 'enrollment.nonce')) {
    throw 'R40 enrollment nonce unexpectedly survived completed enrollment.'
}
if (Test-Path -LiteralPath (Join-Path $configRoot 'enrollment.session')) {
    throw 'R40 enrollment session unexpectedly survived completed enrollment.'
}
Add-Evidence 'R40_ENROLLMENT_ARTIFACTS_CLEAN=True'

Assert-NoEngineResidue
Add-Evidence 'R40_FINAL_ENGINE_RESIDUE_ZERO=True'
Add-Evidence 'RESULT=R40_LIFECYCLE_SOAK_PASS'

Remove-LocalUser -Name $secondUserName
Remove-Item -LiteralPath $scratch -Recurse -Force -ErrorAction SilentlyContinue
Add-Evidence 'R40_SECOND_USER_CLEANUP=PASS'
