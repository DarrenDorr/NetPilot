param(
    [Parameter(Mandatory = $true)]
    [string]$PackPath,
    [Parameter(Mandatory = $true)]
    [string]$HostedEvidenceDir,
    [Parameter(Mandatory = $true)]
    [string]$TestVersion,
    [string]$UserName = 'NetPilotR42124'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$PackPath = [System.IO.Path]::GetFullPath($PackPath)
$HostedEvidenceDir = [System.IO.Path]::GetFullPath($HostedEvidenceDir)
$publicPack = 'C:\Users\Public\NetPilot-R42.12.4-Pack'
$publicEvidence = 'C:\Users\Public\NetPilot-R42.12.4-Evidence'
$userAccount = "$env:COMPUTERNAME\$UserName"
$createdUserSid = $null
$validationSucceeded = $false
$failure = $null
$childProcess = $null

function Copy-EvidenceBestEffort {
    New-Item -ItemType Directory -Force -Path $HostedEvidenceDir | Out-Null
    if (-not (Test-Path -LiteralPath $publicEvidence -PathType Container)) {
        return
    }

    Get-ChildItem -LiteralPath $publicEvidence -Force -ErrorAction SilentlyContinue | ForEach-Object {
        try {
            Copy-Item -LiteralPath $_.FullName -Destination $HostedEvidenceDir -Recurse -Force
        }
        catch {
            Write-Warning "Could not preserve hosted evidence item: $($_.Name)"
        }
    }
}

function Require-Marker {
    param(
        [Parameter(Mandatory = $true)] [string]$Text,
        [Parameter(Mandatory = $true)] [string]$Marker
    )

    if (-not $Text.Contains($Marker, [StringComparison]::Ordinal)) {
        throw "Required no-admin evidence marker missing: $Marker"
    }
}

try {
    if (-not (Test-Path -LiteralPath $PackPath -PathType Container)) {
        throw "Validation pack directory is missing: $PackPath"
    }

    if (Get-Service -Name 'NetPilot.Service' -ErrorAction SilentlyContinue) {
        throw 'NetPilot.Service already exists before the no-admin hosted boundary.'
    }
    if (@(Get-Process -Name ciadpi -ErrorAction SilentlyContinue).Count -ne 0) {
        throw 'ciadpi already exists before the no-admin hosted boundary.'
    }

    $existing = Get-LocalUser -Name $UserName -ErrorAction SilentlyContinue
    if ($null -ne $existing) {
        Remove-LocalUser -Name $UserName
    }

    if (Test-Path -LiteralPath $publicPack) {
        Remove-Item -LiteralPath $publicPack -Recurse -Force
    }
    if (Test-Path -LiteralPath $publicEvidence) {
        Remove-Item -LiteralPath $publicEvidence -Recurse -Force
    }
    New-Item -ItemType Directory -Force -Path $publicPack, $publicEvidence, $HostedEvidenceDir | Out-Null

    $passwordPlain = 'Aa1!' + [guid]::NewGuid().ToString('N')
    Write-Output "::add-mask::$passwordPlain"
    $passwordSecure = ConvertTo-SecureString $passwordPlain -AsPlainText -Force
    $createdUser = New-LocalUser `
        -Name $UserName `
        -Password $passwordSecure `
        -PasswordNeverExpires `
        -UserMayNotChangePassword
    $createdUserSid = $createdUser.SID.Value

    $usersSid = New-Object System.Security.Principal.SecurityIdentifier('S-1-5-32-545')
    $usersGroup = Get-LocalGroup -SID $usersSid
    Add-LocalGroupMember -Group $usersGroup -Member $userAccount

    $adminSid = New-Object System.Security.Principal.SecurityIdentifier('S-1-5-32-544')
    $adminGroup = Get-LocalGroup -SID $adminSid
    $adminMembers = @(Get-LocalGroupMember -Group $adminGroup -ErrorAction Stop)
    if ($adminMembers.Name -contains $userAccount) {
        throw 'Standard test user unexpectedly belongs to Administrators.'
    }

    Get-ChildItem -LiteralPath $PackPath -Force | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $publicPack -Recurse -Force
    }

    $runner = Join-Path $publicPack 'run-standard-user-validation.ps1'
    $runnerTemplate = @'
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$pack = 'C:\Users\Public\NetPilot-R42.12.4-Pack'
$evidence = 'C:\Users\Public\NetPilot-R42.12.4-Evidence'
$result = Join-Path $evidence 'standard-user-boundary.txt'
try {
    $identity = [System.Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object System.Security.Principal.WindowsPrincipal($identity)
    if ($null -eq $identity.User -or $identity.User.Value -cne '__USER_SID__') {
        throw 'Direct credential launch did not receive the expected standard-user SID.'
    }
    if ($principal.IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'Direct credential launch received an Administrator token.'
    }
    'RUNNER_IDENTITY_MATCH=PASS' | Set-Content -LiteralPath $result -Encoding utf8NoBOM
    'RUNNER_ADMIN_ROLE=False' | Add-Content -LiteralPath $result

    $setup = Join-Path $pack 'NetPilot-UserMode-Setup-__VERSION__-x64.exe'
    $install = Start-Process `
        -FilePath $setup `
        -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-' `
        -Wait `
        -PassThru
    "INSTALLER_EXIT_CODE=$($install.ExitCode)" | Add-Content -LiteralPath $result
    if ($install.ExitCode -ne 0) {
        throw "User-mode installer failed: $($install.ExitCode)"
    }

    $installRoot = Join-Path $env:LOCALAPPDATA 'Programs\NetPilot'
    if (-not (Test-Path -LiteralPath (Join-Path $installRoot 'App\NetPilot.exe') -PathType Leaf)) {
        throw 'Per-user NetPilot.exe was not installed beneath LocalAppData.'
    }
    if (Get-Service -Name 'NetPilot.Service' -ErrorAction SilentlyContinue) {
        throw 'NetPilot.Service unexpectedly exists during no-admin validation.'
    }
    'PER_USER_INSTALL=PASS' | Add-Content -LiteralPath $result
    'NETPILOT_SERVICE_PRESENT=False' | Add-Content -LiteralPath $result

    $validator = Join-Path $pack 'NetPilot-ByeDPI-NoAdmin-Validation.exe'
    $validation = Start-Process `
        -FilePath $validator `
        -ArgumentList '--install-root',$installRoot,'--evidence-dir',$evidence `
        -Wait `
        -PassThru
    "VALIDATOR_EXIT_CODE=$($validation.ExitCode)" | Add-Content -LiteralPath $result
    if ($validation.ExitCode -ne 0) {
        throw "No-admin validator failed: $($validation.ExitCode)"
    }
    'STANDARD_USER_VALIDATION=PASS' | Add-Content -LiteralPath $result

    $uninstaller = Join-Path $installRoot 'unins000.exe'
    if (Test-Path -LiteralPath $uninstaller -PathType Leaf) {
        $uninstall = Start-Process `
            -FilePath $uninstaller `
            -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART' `
            -Wait `
            -PassThru
        "UNINSTALLER_EXIT_CODE=$($uninstall.ExitCode)" | Add-Content -LiteralPath $result
        if ($uninstall.ExitCode -ne 0) {
            throw "Per-user uninstall failed: $($uninstall.ExitCode)"
        }
    }

    'DONE=PASS' | Add-Content -LiteralPath $result
}
catch {
    if (-not (Test-Path -LiteralPath $result -PathType Leaf)) {
        'RUNNER_STARTED=True' | Set-Content -LiteralPath $result -Encoding utf8NoBOM
    }
    "RUNNER_FAILURE_TYPE=$($_.Exception.GetType().FullName)" | Add-Content -LiteralPath $result
    'DONE=FAIL' | Add-Content -LiteralPath $result
    exit 1
}
'@
    $runnerTemplate.Replace('__VERSION__', $TestVersion).Replace('__USER_SID__', $createdUserSid) |
        Set-Content -LiteralPath $runner -Encoding utf8NoBOM

    & icacls.exe `
        $publicPack `
        /inheritance:r `
        /grant:r `
        "${userAccount}:(OI)(CI)RX" `
        '*S-1-5-18:(OI)(CI)F' `
        '*S-1-5-32-544:(OI)(CI)F' `
        /T `
        /C | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw 'Could not apply read-only pack ACL for the standard user.'
    }

    & icacls.exe `
        $publicEvidence `
        /inheritance:r `
        /grant:r `
        "${userAccount}:(OI)(CI)M" `
        '*S-1-5-18:(OI)(CI)F' `
        '*S-1-5-32-544:(OI)(CI)F' `
        /T `
        /C | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw 'Could not apply evidence ACL for the standard user.'
    }

    'STANDARD_USER_CREATED=PASS' | Add-Content (Join-Path $HostedEvidenceDir 'boundary.txt')
    'STANDARD_USER_ADMIN_GROUP=False' | Add-Content (Join-Path $HostedEvidenceDir 'boundary.txt')
    'STANDARD_USER_LAUNCH_METHOD=DirectCredential' | Add-Content (Join-Path $HostedEvidenceDir 'boundary.txt')
    'PACK_ACL_STANDARD_USER=RX' | Add-Content (Join-Path $HostedEvidenceDir 'boundary.txt')
    'EVIDENCE_ACL_STANDARD_USER=M' | Add-Content (Join-Path $HostedEvidenceDir 'boundary.txt')

    $credential = [pscredential]::new($userAccount, $passwordSecure)
    $powershell = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    $stdoutPath = Join-Path $publicEvidence 'standard-user-stdout.txt'
    $stderrPath = Join-Path $publicEvidence 'standard-user-stderr.txt'
    $arguments = @(
        '-NoLogo',
        '-NoProfile',
        '-NonInteractive',
        '-ExecutionPolicy', 'Bypass',
        '-File', "`"$runner`""
    )

    $childProcess = Start-Process `
        -FilePath $powershell `
        -ArgumentList $arguments `
        -Credential $credential `
        -LoadUserProfile `
        -WorkingDirectory $publicPack `
        -WindowStyle Hidden `
        -RedirectStandardOutput $stdoutPath `
        -RedirectStandardError $stderrPath `
        -PassThru

    if ($null -eq $childProcess) {
        throw 'Direct standard-user process was not created.'
    }
    'DIRECT_STANDARD_USER_PROCESS_CREATED=PASS' | Add-Content (Join-Path $HostedEvidenceDir 'boundary.txt')

    $deadline = (Get-Date).AddMinutes(10)
    while ((Get-Date) -lt $deadline -and -not $childProcess.HasExited) {
        Start-Sleep -Seconds 2
        $childProcess.Refresh()
    }

    if (-not $childProcess.HasExited) {
        try { $childProcess.Kill($true) } catch { }
        throw 'Direct standard-user validation process timed out.'
    }

    "STANDARD_USER_PROCESS_EXIT_CODE=$($childProcess.ExitCode)" | Add-Content (Join-Path $HostedEvidenceDir 'boundary.txt')
    if ($childProcess.ExitCode -ne 0) {
        throw "Direct standard-user validation process failed with exit code $($childProcess.ExitCode)."
    }

    $boundary = Join-Path $publicEvidence 'standard-user-boundary.txt'
    if (-not (Test-Path -LiteralPath $boundary -PathType Leaf)) {
        throw 'Standard-user boundary evidence was not created.'
    }
    $boundaryText = Get-Content -LiteralPath $boundary -Raw
    Require-Marker -Text $boundaryText -Marker 'RUNNER_IDENTITY_MATCH=PASS'
    Require-Marker -Text $boundaryText -Marker 'RUNNER_ADMIN_ROLE=False'
    Require-Marker -Text $boundaryText -Marker 'INSTALLER_EXIT_CODE=0'
    Require-Marker -Text $boundaryText -Marker 'PER_USER_INSTALL=PASS'
    Require-Marker -Text $boundaryText -Marker 'NETPILOT_SERVICE_PRESENT=False'
    Require-Marker -Text $boundaryText -Marker 'VALIDATOR_EXIT_CODE=0'
    Require-Marker -Text $boundaryText -Marker 'STANDARD_USER_VALIDATION=PASS'
    Require-Marker -Text $boundaryText -Marker 'DONE=PASS'

    $validationTxt = Get-ChildItem `
        -LiteralPath $publicEvidence `
        -Filter 'ByeDPI-NoAdmin-Validation-*-UTC.txt' `
        -File | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
    if ($null -eq $validationTxt) {
        throw 'Detailed no-admin validation evidence is missing.'
    }

    $validationText = Get-Content -LiteralPath $validationTxt.FullName -Raw
    $requiredMarkers = @(
        'CURRENT_TOKEN_NON_ELEVATED=PASS',
        'INSTALL_SCOPE=CURRENT_USER_LOCALAPPDATA',
        "CANDIDATE_SHA256=$env:BYEDPI_WINDOWS_SHA256",
        'INSTALLED_EXACT_CANDIDATE=PASS',
        'NETPILOT_SERVICE_PRESENT_BEFORE=False',
        'BYEDPI_USER_MODE_START=PASS',
        'BYEDPI_LISTENER_LOOPBACK_ONLY=PASS',
        'CIADPI_OWNER_MATCH=PASS',
        'CIADPI_TOKEN_NON_ELEVATED=PASS',
        'EXTERNAL_SOCKS5_HTTPS=PASS',
        'NORMAL_STOP_CLEANUP=PASS',
        'RUNTIME_RESIDUE_AFTER_NORMAL_STOP=NONE',
        'JOB_OBJECT_CRASH_CLEANUP=PASS',
        'RUNTIME_RESIDUE_AFTER_OWNER_CRASH=NONE',
        'SUPPORT_BUNDLE_ENGINE_SHA256=PASS',
        'SUPPORT_BUNDLE_USER_MODE_SCOPE=PASS',
        'NETPILOT_SERVICE_PRESENT_AFTER=False',
        'SYSTEM_PROXY_UNCHANGED=PASS',
        'WINDIVERT_UNCHANGED=PASS',
        'SYSTEM_PROXY_MUTATION=NOT_REQUESTED',
        'WINDIVERT=NOT_REQUESTED',
        'FINAL_RUNTIME_RESIDUE=NONE',
        'FINAL_SERVICE_BOUNDARY=PASS',
        'FINAL_SYSTEM_PROXY_UNCHANGED=PASS',
        'FINAL_WINDIVERT_UNCHANGED=PASS',
        'PRODUCTION_ADMISSION=BLOCK',
        'RESULT=R42_12_4_NO_ADMIN_BYEDPI_PASS_BLOCK'
    )
    foreach ($marker in $requiredMarkers) {
        Require-Marker -Text $validationText -Marker $marker
    }

    $combined = Get-ChildItem `
        -LiteralPath $publicEvidence `
        -Filter 'NetPilot-ByeDPI-NoAdmin-Validation-*-UTC.zip' `
        -File | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
    if ($null -eq $combined) {
        throw 'Combined no-admin evidence ZIP is missing.'
    }

    if (Get-Service -Name 'NetPilot.Service' -ErrorAction SilentlyContinue) {
        throw 'NetPilot.Service exists after no-admin validation.'
    }
    if (@(Get-Process -Name ciadpi -ErrorAction SilentlyContinue).Count -ne 0) {
        throw 'ciadpi residue exists after no-admin validation.'
    }

    $validationSucceeded = $true
}
catch {
    $failure = $_
    try {
        "HOSTED_FAILURE_TYPE=$($_.Exception.GetType().FullName)" | Add-Content (Join-Path $HostedEvidenceDir 'boundary.txt')
        "HOSTED_FAILURE_HRESULT=0x$($_.Exception.HResult.ToString('X8'))" | Add-Content (Join-Path $HostedEvidenceDir 'boundary.txt')
    }
    catch {
    }
}
finally {
    Copy-EvidenceBestEffort

    if ($validationSucceeded) {
        'HOSTED_STANDARD_USER_VALIDATION=PASS' | Add-Content (Join-Path $HostedEvidenceDir 'boundary.txt')
        'HOSTED_CIADPI_RESIDUE=NONE' | Add-Content (Join-Path $HostedEvidenceDir 'boundary.txt')
        'HOSTED_SERVICE_BOUNDARY=PASS' | Add-Content (Join-Path $HostedEvidenceDir 'boundary.txt')
    }

    if ($null -ne $childProcess) {
        try {
            if (-not $childProcess.HasExited) {
                $childProcess.Kill($true)
                $childProcess.WaitForExit(5000)
            }
        }
        catch {
        }
        finally {
            $childProcess.Dispose()
        }
    }

    Get-Process -Name ciadpi -ErrorAction SilentlyContinue |
        Stop-Process -Force -ErrorAction SilentlyContinue

    $user = Get-LocalUser -Name $UserName -ErrorAction SilentlyContinue
    if ($null -ne $user) {
        Remove-LocalUser -Name $UserName -ErrorAction SilentlyContinue
    }

    if ($null -ne $createdUserSid) {
        try {
            Get-CimInstance Win32_UserProfile -ErrorAction SilentlyContinue |
                Where-Object { $_.SID -eq $createdUserSid -and -not $_.Loaded } |
                Remove-CimInstance -ErrorAction SilentlyContinue
        }
        catch {
        }
    }

    Remove-Item -LiteralPath $publicPack -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $publicEvidence -Recurse -Force -ErrorAction SilentlyContinue
}

if (-not $validationSucceeded) {
    if ($null -ne $failure) {
        throw $failure
    }
    throw 'R42.12.4 hosted standard-user validation did not complete.'
}
