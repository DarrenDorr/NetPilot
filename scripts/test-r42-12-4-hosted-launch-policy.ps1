# Static regression guard for the hosted standard-user launcher.
# Does not create accounts, install drivers, or run privileged services.
$ErrorActionPreference = 'Stop'
$scriptPath = Join-Path $PSScriptRoot 'r42-12-4-hosted-standard-user.ps1'
if (-not (Test-Path -LiteralPath $scriptPath -PathType Leaf)) {
    throw 'R42.12.4 hosted launcher was not found.'
}

$tokens = $null
$parseErrors = $null
[System.Management.Automation.Language.Parser]::ParseFile(
    $scriptPath, [ref]$tokens, [ref]$parseErrors) | Out-Null
if (@($parseErrors).Count -ne 0) {
    throw 'R42.12.4 hosted launcher has invalid PowerShell syntax.'
}

$source = Get-Content -LiteralPath $scriptPath -Raw -Encoding utf8
$required = @(
    'if (commandLine.Length >= 1024)',
    '$runnerScript = Join-Path $publicPack',
    'NetPilot-StandardUser-Runner.ps1',
    '[System.IO.File]::WriteAllText(',
    'icacls.exe $runnerScript /inheritance:r /grant:r',
    '"*${createdUserSid}:RX"',
    'HOSTED_RUNNER_SCRIPT_ACL=EXACT_TEST_USER_RX',
    'r42-12-4-apply-pack-acl.ps1',
    '-PackRoot $publicPack -StandardUserSid $createdUserSid',
    'PACK_FILE_LEVEL_ACL_VERIFIED=PASS',
    'LOGON_WITH_PROFILE',
    'RUNNER_STARTED=True',
    'INSTALLER_LAUNCH_BEGIN=PASS',
    'VALIDATOR_LAUNCH_BEGIN=PASS',
    'UNINSTALLER_LAUNCH_BEGIN=PASS',
    "HOSTED_PREFLIGHT_LAUNCH_BEGIN=YES",
    "HOSTED_PREFLIGHT_TIMEOUT_SECONDS=45",
    "HOSTED_PREFLIGHT=PASS",
    'preflightChild.Wait(45000)',
    'Test-HostedFixedStartup',
    "[ValidateSet('CMD','POWERSHELL','PWSH7','POWERSHELL_USERENV','POWERSHELL_CONSOLE','CMD_LOGON','POWERSHELL_LOGON')]",
    "System32\cmd.exe",
    "'/D /Q /C exit /B 0'",
    '-Command "exit 0"',
    'probe.Wait(15000)',
    'HOSTED_NATIVE_${Kind}_TIMEOUT_SECONDS=15',
    'HOSTED_NATIVE_${Kind}_EXIT_CODE',
    'HOSTED_NATIVE_${Kind}_STARTUP=PASS',
    'HOSTED_NATIVE_${Kind}_FAILURE_HRESULT',
    'CreateEnvironmentBlock(',
    'DestroyEnvironmentBlock(',
    'CREATE_UNICODE_ENVIRONMENT',
    'CREATE_NEW_CONSOLE',
    'diagnosticNewConsole ? CREATE_NEW_CONSOLE : CREATE_NO_WINDOW',
    'GetDiagnosticState()',
    'GetSessionRelation()',
    'HOSTED_NATIVE_${Kind}_STATE',
    'HOSTED_NATIVE_${Kind}_SESSION',
    'POWERSHELL_CONSOLE',
    'CMD_LOGON',
    'POWERSHELL_LOGON',
    'CreateProcessWithLogonW(',
    'StartWithLogon(',
    '-UseCredentialLogon $true',
    'LOGON_WITH_PROFILE',
    'Credential API diagnostic executable/arguments mismatch.',
    'HOSTED_CREDENTIAL_STARTUP=PASS',
    'HOSTED_CREDENTIAL_EXACT_SID_PREFLIGHT=PASS',
    'STANDARD_USER_LAUNCH_METHOD=CreateProcessWithLogonW',
    '$preflightChild = [NetPilotStandardUserTokenLauncher]::StartWithLogon(',
    '$nativeChild = [NetPilotStandardUserTokenLauncher]::StartWithLogon(',
    'Test-HostedFixedStartup -Kind CMD_LOGON',
    'Test-HostedFixedStartup -Kind POWERSHELL_LOGON',
    'Hosted credential API PowerShell startup baseline failed.',
    'Require-Marker -Text $preflightText -Marker ''PREFLIGHT_SID_MATCH=PASS''',
    'Require-Marker -Text $preflightText -Marker ''PREFLIGHT_ADMIN_ROLE=False''',
    'PREFLIGHT_SID_MATCH=PASS',
    'PREFLIGHT_EVIDENCE_WRITE=PASS',
    'RUNNER_IDENTITY_MATCH=PASS',
    'RUNNER_ADMIN_ROLE=False',
    'function Invoke-BoundedUserExe',
    "[ValidateSet('CMD_BASELINE','INSTALLER','VALIDATOR','UNINSTALLER')]",
    '$startInfo.UseShellExecute = $false',
    '$startInfo.CreateNoWindow = $true',
    '-not $child.WaitForExit($TimeoutSeconds * 1000)',
    '$child.Kill()',
    'ProcessStartInfo',
    'EXECUTABLE_PRESENT=PASS',
    'LAUNCH_MODE=DIRECT_NO_SHELL',
    'FAILURE_HRESULT=0x',
    'WIN32_ERROR=',
    'ReparsePoint',
    'Invoke-BoundedUserExe -Stage CMD_BASELINE -FilePath $cmdExe',
    'CMD_BASELINE_EXIT_CODE=$cmdExit',
    'NESTED_STANDARD_USER_CMD=PASS',
    'Nested process diagnostic must use the fixed Windows CMD command.',
    'INSTALLER_FILE_READ_MZ=PASS',
    '[System.IO.File]::Open(',
    '[System.IO.FileAccess]::Read',
    '[System.IO.FileShare]::Read',
    '0x4D',
    '0x5A',
    'Invoke-BoundedUserExe -Stage INSTALLER -FilePath $setup',
    'Invoke-BoundedUserExe -Stage VALIDATOR -FilePath $validator',
    'Invoke-BoundedUserExe -Stage UNINSTALLER -FilePath $uninstaller',
    'INSTALLER_EXIT_CODE=',
    'VALIDATOR_EXIT_CODE=',
    'UNINSTALLER_EXIT_CODE=',
    'STANDARD_USER_VALIDATION=PASS',
    'RESULT=R42_12_4_NO_ADMIN_BYEDPI_PASS_BLOCK'
)
foreach ($value in $required) {
    if (-not $source.Contains($value, [System.StringComparison]::Ordinal)) {
        throw 'R42.12.4 protected short-launch policy is missing required code.'
    }
}
# This harness starts Windows PowerShell 5.1; it does not support
# Set-Content -Encoding utf8NoBOM (PowerShell 7 syntax).
if ($source.Contains('-Encoding utf8NoBOM', [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'R42.12.4 hosted runner must use PowerShell 5.1-compatible evidence encoding.'
}
if (-not $source.Contains('-File ', [System.StringComparison]::Ordinal)) {
    throw 'R42.12.4 must launch the protected runner script by file path.'
}
if ($source -match '(?m)^\s*\$encodedRunner\s*=' -or
    $source -match '(?m)^\s*\$arguments\s*=.*-EncodedCommand') {
    throw 'R42.12.4 must not pass an encoded script through native process creation.'
}

if ($source -match '(?m)^\s*\$nativeChild\s*=\s*\[NetPilotStandardUserTokenLauncher\]::Start\(' -or
    $source -match '(?m)^\s*\$preflightChild\s*=\s*\[NetPilotStandardUserTokenLauncher\]::Start\(') {
    throw 'R42.12.4 primary hosted runner and preflight must use credential-based launch.'
}

# The restricted-user script must not depend on Windows shell
# execution to launch the headless installer/validator/uninstaller.
$runnerBegin = $source.IndexOf(('$runnerTemplate = @' + "'"), [System.StringComparison]::Ordinal)
$runnerEnd = $source.IndexOf("`n'@", $runnerBegin + 1, [System.StringComparison]::Ordinal)
if ($runnerBegin -lt 0 -or $runnerEnd -le $runnerBegin) {
    throw 'R42.12.4 protected user runner script template missing.'
}
$runnerCode = $source.Substring($runnerBegin, $runnerEnd - $runnerBegin)
if ($runnerCode.Contains('Start-Process', [System.StringComparison]::Ordinal)) {
    throw 'R42.12.4 protected user runner must launch EXEs directly without Start-Process.'
}
$identityAt = $runnerCode.IndexOf('RUNNER_IDENTITY_MATCH=PASS', [System.StringComparison]::Ordinal)
$nestedCmdAt = $runnerCode.IndexOf('Invoke-BoundedUserExe -Stage CMD_BASELINE', [System.StringComparison]::Ordinal)
$installAt = $runnerCode.IndexOf('Invoke-BoundedUserExe -Stage INSTALLER', [System.StringComparison]::Ordinal)
if ($identityAt -lt 0 -or $nestedCmdAt -lt 0 -or $installAt -lt 0 -or
    $identityAt -ge $nestedCmdAt -or $nestedCmdAt -ge $installAt) {
    throw 'R42.12.4 must prove exact user, pass bounded nested CMD, then launch installer.'
}

# The ACL helper and its independent canary are mandatory on Windows.
# Do not revert to one recursive icacls /T /C success marker.
$aclHelperPath = Join-Path $PSScriptRoot 'r42-12-4-apply-pack-acl.ps1'
$aclCanaryPath = Join-Path $PSScriptRoot 'test-r42-12-4-pack-acl.ps1'
foreach ($aclScript in @($aclHelperPath, $aclCanaryPath)) {
    if (-not (Test-Path -LiteralPath $aclScript -PathType Leaf)) {
        throw 'R42.12.4 per-file ACL safety check script missing.'
    }
    $aclTokens = $null
    $aclErrors = $null
    [System.Management.Automation.Language.Parser]::ParseFile(
        $aclScript, [ref]$aclTokens, [ref]$aclErrors) | Out-Null
    if (@($aclErrors).Count -ne 0) {
        throw 'R42.12.4 per-file ACL safety check script has invalid syntax.'
    }
}
$aclText = Get-Content -LiteralPath $aclHelperPath -Raw -Encoding utf8
foreach ($requiredAcl in @(
    'Get-ChildItem -LiteralPath $PackRoot -Recurse -Force',
    'ReparsePoint',
    'if ($entry.PSIsContainer)',
    '$testPrincipal + '':RX''',
    '$testPrincipal + '':(OI)(CI)RX''',
    'icacls.exe $entry.FullName /inheritance:r /grant:r',
    'Get-Acl -LiteralPath $entry.FullName',
    'AreAccessRulesProtected',
    'GetAccessRules(',
    'AccessControlType',
    'ReadAndExecute',
    'ChangePermissions',
    'TakeOwnership',
    'R42_12_4_PACK_FILE_LEVEL_ACL_VERIFIED=PASS'
)) {
    if (-not $aclText.Contains($requiredAcl, [System.StringComparison]::Ordinal)) {
        throw 'R42.12.4 strict per-file ACL check is missing required code.'
    }
}
if ($source.Contains('$publicPack', [System.StringComparison]::Ordinal) -and
    $source.Contains('PACK_FILE_LEVEL_ACL_VERIFIED=PASS', [System.StringComparison]::Ordinal) -eq $false) {
    throw 'R42.12.4 cannot claim pack ACL RX without per-file proof.'
}

$workflowPath = Join-Path $PSScriptRoot '..\.github\workflows\r42-12-4-no-admin-byedpi-validation.yml'
$workflow = Get-Content -LiteralPath $workflowPath -Raw -Encoding utf8
if (-not $workflow.Contains("'PRODUCTION_ADMISSION=BLOCK'", [System.StringComparison]::Ordinal)) {
    throw 'R42.12.4 production admission gate is missing.'
}

# The physical-PC artifact is a *validated-only* short-lived package.
# An unsuccessful hosted test must never advertise an installable alpha.
foreach ($requiredDelivery in @(
    "hosted_validation = 'NOT_YET_PROVEN'",
    'hosted_run_id = [long]0',
    'gui = [ordered]@{',
    "name = 'NetPilot.exe'",
    "Copy-Item -LiteralPath 'docs\WINDOWS-ALPHA-GUI-SMOKE.md'",
    "Copy-Item -LiteralPath 'scripts\r42-12-4-verify-physical-pack.ps1'",
    'id: finalize_pc_pack',
    "steps.hosted_validation.outcome == 'success'",
    "steps.recheck_draft.outcome == 'success'",
    "steps.finalize_pc_pack.outcome == 'success'",
    "$manifest.hosted_validation = 'PASS_BLOCK'",
    'PHYSICAL_PC_PACK_HOSTED_PASS_ONLY=PASS_BLOCK',
    'retention-days: 1'
)) {
    if (-not $workflow.Contains($requiredDelivery, [System.StringComparison]::Ordinal)) {
        throw 'R42.12.4 physical pack must be emitted only after full hosted PASS.'
    }
}
$verifyPackPath = Join-Path $PSScriptRoot 'r42-12-4-verify-physical-pack.ps1'
if (-not (Test-Path -LiteralPath $verifyPackPath -PathType Leaf)) {
    throw 'R42.12.4 physical pack verification script missing.'
}
$verifyPackTokens = $null
$verifyPackErrors = $null
[System.Management.Automation.Language.Parser]::ParseFile(
    $verifyPackPath, [ref]$verifyPackTokens, [ref]$verifyPackErrors) | Out-Null
if (@($verifyPackErrors).Count -ne 0) {
    throw 'R42.12.4 physical pack verification PowerShell syntax is invalid.'
}
$verifyText = Get-Content -LiteralPath $verifyPackPath -Raw -Encoding utf8
foreach ($requiredVerifier in @(
    "hosted_validation -cne 'PASS_BLOCK'",
    "production_admission -cne 'BLOCK'",
    'draft_release_id -ne 431054375',
    'manifest.netpilot_commit',
    'manifest.hosted_run_id',
    'manifest.installer.sha256',
    'manifest.validator.sha256',
    'manifest.gui.sha256',
    'Get-FileHash -LiteralPath $FilePath -Algorithm SHA256',
    'WindowsBuiltInRole',
    'ReparsePoint',
    'R42_12_4_PACK_CHECK=PASS'
)) {
    if (-not $verifyText.Contains($requiredVerifier, [System.StringComparison]::Ordinal)) {
        throw 'R42.12.4 physical pack verifier is missing a fail-closed requirement.'
    }
}
if ($verifyText -match 'Start-Process|Process\.Start|Invoke-WebRequest|Invoke-RestMethod|ExecutionPolicy\s+Bypass|Start-Job|runas') {
    throw 'R42.12.4 physical pack verifier must be read-only and offline.'
}

$preflight = Join-Path $PSScriptRoot 'r42-12-4-hosted-preflight.ps1'
if (-not (Test-Path -LiteralPath $preflight -PathType Leaf)) {
    throw 'R42.12.4 hosted preflight script is missing.'
}
$preflightTokens = $null
$preflightErrors = $null
[System.Management.Automation.Language.Parser]::ParseFile(
    $preflight, [ref]$preflightTokens, [ref]$preflightErrors) | Out-Null
if (@($preflightErrors).Count -ne 0) {
    throw 'R42.12.4 hosted preflight syntax invalid.'
}
$preflightText = Get-Content -LiteralPath $preflight -Raw -Encoding utf8
foreach ($requiredPreflight in @(
    '__USER_SID__',
    'PREFLIGHT_SID_MATCH=PASS',
    'PREFLIGHT_ADMIN_ROLE=False',
    'PREFLIGHT_EVIDENCE_WRITE=PASS',
    'WindowsBuiltInRole',
    '[System.IO.File]::WriteAllText('
)) {
    if (-not $preflightText.Contains($requiredPreflight, [System.StringComparison]::Ordinal)) {
        throw 'R42.12.4 hosted preflight must prove identity, privilege and write boundary.'
    }
}
# Compile the embedded, test-only native launcher without invoking any
# Win32 function or creating a local account. Parser-only CI would miss
# P/Invoke/overload mistakes in the PowerShell here-string.
$hereStart = '$nativeLauncherSource = @' + "'"
$begin = $source.IndexOf($hereStart, [System.StringComparison]::Ordinal)
if ($begin -lt 0) {
    throw 'R42.12.4 native launcher here-string start missing.'
}
$bodyStart = $source.IndexOf("`n", $begin)
$bodyEnd = $source.IndexOf("`n'@", $bodyStart + 1, [System.StringComparison]::Ordinal)
if ($bodyStart -lt 0 -or $bodyEnd -le $bodyStart) {
    throw 'R42.12.4 native launcher here-string end missing.'
}
$launcherCs = $source.Substring($bodyStart + 1, $bodyEnd - $bodyStart - 1)
Add-Type -TypeDefinition $launcherCs -Language CSharp -ErrorAction Stop
if ($null -eq ([System.Management.Automation.PSTypeName]'NetPilotStandardUserTokenLauncher').Type) {
    throw 'R42.12.4 native launcher C# type was not compiled.'
}
'R42_12_4_HOSTED_NATIVE_INTEROP_COMPILE=PASS'
'R42_12_4_HOSTED_SHORT_LAUNCH_POLICY=PASS'
