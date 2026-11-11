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
    "[ValidateSet('CMD','POWERSHELL','PWSH7','POWERSHELL_USERENV','POWERSHELL_CONSOLE')]",
    "System32\cmd.exe",
    "'/D /Q /C exit /B 0'",
    '-Command "exit 0"',
    'probe.Wait(15000)',
    'HOSTED_NATIVE_${Kind}_TIMEOUT_SECONDS=15',
    'HOSTED_NATIVE_${Kind}_EXIT_CODE',
    'HOSTED_NATIVE_${Kind}_STARTUP=PASS',
    'HOSTED_NATIVE_${Kind}_FAILURE_HRESULT',
    'HOSTED_NATIVE_PWSH7=NOT_INSTALLED',
    'PowerShell\7\pwsh.exe',
    'CreateEnvironmentBlock(',
    'DestroyEnvironmentBlock(',
    'CREATE_UNICODE_ENVIRONMENT',
    'CREATE_NEW_CONSOLE',
    'diagnosticNewConsole ? CREATE_NEW_CONSOLE : CREATE_NO_WINDOW',
    'GetDiagnosticState()',
    'GetSessionRelation()',
    'HOSTED_NATIVE_${Kind}_STATE',
    'HOSTED_NATIVE_${Kind}_SESSION',
    '-DiagnosticNewConsole $true',
    'POWERSHELL_CONSOLE',
    'if (-not $ps51Passed)',
    'throw ''Hosted exact-SID Windows PowerShell 5.1 startup baseline failed.''',
    'PREFLIGHT_SID_MATCH=PASS',
    'PREFLIGHT_EVIDENCE_WRITE=PASS',
    'RUNNER_IDENTITY_MATCH=PASS',
    'RUNNER_ADMIN_ROLE=False',
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
    throw 'R42.12.4 must not pass an encoded script through CreateProcessWithTokenW.'
}

$workflowPath = Join-Path $PSScriptRoot '..\.github\workflows\r42-12-4-no-admin-byedpi-validation.yml'
$workflow = Get-Content -LiteralPath $workflowPath -Raw -Encoding utf8
if (-not $workflow.Contains("'PRODUCTION_ADMISSION=BLOCK'", [System.StringComparison]::Ordinal)) {
    throw 'R42.12.4 production admission gate is missing.'
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
