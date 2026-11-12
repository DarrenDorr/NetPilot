# R42.12.4 read-only verification; no EXE execution, UAC or network access.
[CmdletBinding()]
param([string]$PackRoot = $PSScriptRoot, [switch]$CheckInstalled)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ([System.Environment]::OSVersion.Platform -ne [System.PlatformID]::Win32NT) {
    throw 'This validation pack is Windows-only.'
}
$identity = [System.Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object System.Security.Principal.WindowsPrincipal($identity)
if ($principal.IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run as a non-elevated Windows user.'
}

$PackRoot = [System.IO.Path]::GetFullPath($PackRoot)
$root = Get-Item -LiteralPath $PackRoot -Force -ErrorAction Stop
if (-not $root.PSIsContainer -or
    ($root.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
    throw 'Expected a regular extracted validation-pack directory.'
}

function Assert-ExactFile {
    param([string]$FilePath, [long]$ExpectedSize, [string]$ExpectedSha256)
    if ($ExpectedSize -le 0 -or $ExpectedSha256 -cnotmatch '^[0-9a-f]{64}$') {
        throw 'Invalid expected file size or SHA-256.'
    }
    $item = Get-Item -LiteralPath $FilePath -Force -ErrorAction Stop
    if ($item.PSIsContainer -or
        ($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw 'The package file is not a regular file.'
    }
    if ($item.Length -ne $ExpectedSize) {
        throw 'File size differs from package manifest.'
    }
    $actual = (Get-FileHash -LiteralPath $FilePath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -cne $ExpectedSha256) {
        throw 'SHA-256 differs from package manifest.'
    }
}

$manifestPath = Join-Path $PackRoot 'package-manifest.json'
$m = Get-Item -LiteralPath $manifestPath -Force -ErrorAction Stop
if ($m.PSIsContainer -or ($m.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
    throw 'Manifest is not a regular file.'
}
$manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($manifest.schema -ne 1 -or
    $manifest.netpilot_commit -cnotmatch '^[0-9a-f]{40}$' -or
    [long]$manifest.draft_release_id -ne 431054375 -or
    $manifest.draft_tag -cne 'byedpi-v0.17.3' -or
    $manifest.release_published -ne $false -or
    $manifest.production_admission -cne 'BLOCK' -or
    $manifest.hosted_validation -cne 'PASS_BLOCK' -or
    [long]$manifest.hosted_run_id -le 0 -or
    $manifest.candidate.version -cne '0.17.3' -or
    [long]$manifest.candidate.size -ne 495685 -or
    $manifest.candidate.sha256 -cne '866e99c3c7690326cc6e365fe02980f06ae2b394c045555674fb95db52b0dbdd' -or
    $manifest.installer.requires_admin -ne $false -or
    $manifest.installer.install_scope -cne 'CurrentUserLocalAppData' -or
    $manifest.validator.execution_level -cne 'asInvoker' -or
    $manifest.gui.execution_level -cne 'asInvoker' -or
    $manifest.gui.name -cne 'NetPilot.exe') {
    throw 'Manifest fails required exact candidate and no-admin checks.'
}
if ($manifest.installer.name -cnotmatch '^NetPilot-UserMode-Setup-[0-9A-Za-z.-]+-x64\.exe$' -or
    $manifest.validator.name -cne 'NetPilot-ByeDPI-NoAdmin-Validation.exe') {
    throw 'Unexpected executable filename in manifest.'
}

$expectedNames = @(
    $manifest.installer.name,
    $manifest.validator.name,
    'package-manifest.json',
    'README.md',
    'WINDOWS-ALPHA-GUI-SMOKE.md',
    'verify-physical-pack.ps1'
)
$files = @(Get-ChildItem -LiteralPath $PackRoot -Force -ErrorAction Stop)
if ($files.Count -ne $expectedNames.Count) {
    throw 'Unexpected number of entries in pack.'
}
foreach ($entry in $files) {
    if ($entry.PSIsContainer -or $entry.Name -cnotin $expectedNames -or
        ($entry.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw 'Unexpected entry in extracted pack.'
    }
}
Assert-ExactFile -FilePath (Join-Path $PackRoot $manifest.installer.name) `
    -ExpectedSize ([long]$manifest.installer.size) `
    -ExpectedSha256 ([string]$manifest.installer.sha256)
Assert-ExactFile -FilePath (Join-Path $PackRoot $manifest.validator.name) `
    -ExpectedSize ([long]$manifest.validator.size) `
    -ExpectedSha256 ([string]$manifest.validator.sha256)

if ($CheckInstalled) {
    $installedGui = Join-Path $env:LOCALAPPDATA 'Programs\NetPilot\App\NetPilot.exe'
    Assert-ExactFile -FilePath $installedGui `
        -ExpectedSize ([long]$manifest.gui.size) `
        -ExpectedSha256 ([string]$manifest.gui.sha256)
    'INSTALLED_GUI_SHA256=PASS'
}
'R42_12_4_MANIFEST_BOUNDARY=PASS_BLOCK'
'R42_12_4_EXACT_FILES=PASS'
'R42_12_4_PACK_CHECK=PASS'
"NETPILOT_COMMIT=$($manifest.netpilot_commit)"
"HOSTED_RUN_ID=$($manifest.hosted_run_id)"
