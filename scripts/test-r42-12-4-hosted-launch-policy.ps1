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
'R42_12_4_HOSTED_SHORT_LAUNCH_POLICY=PASS'
