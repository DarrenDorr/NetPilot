# Windows CI canary: exercise the exact ACL helper against disposable files.
# Does not create test accounts, elevate, run installers or touch WinDivert.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$helper = Join-Path $PSScriptRoot 'r42-12-4-apply-pack-acl.ps1'
$tokens = $null
$errors = $null
[System.Management.Automation.Language.Parser]::ParseFile(
    $helper, [ref]$tokens, [ref]$errors) | Out-Null
if (@($errors).Count -ne 0) {
    throw 'R42.12.4 ACL helper has invalid PowerShell syntax.'
}

$sid = [System.Security.Principal.WindowsIdentity]::GetCurrent().User.Value
$root = Join-Path $env:RUNNER_TEMP ('NetPilot-R42.12.4-Acl-Canary-' + [guid]::NewGuid().ToString('N'))
try {
    New-Item -ItemType Directory -Path $root -ErrorAction Stop | Out-Null
    $subdir = Join-Path $root 'nested'
    New-Item -ItemType Directory -Path $subdir -ErrorAction Stop | Out-Null
    $setup = Join-Path $root 'NetPilot-UserMode-Test.exe'
    $other = Join-Path $subdir 'nested.txt'
    [System.IO.File]::WriteAllBytes($setup, [byte[]]@(0x4D,0x5A,0,0))
    [System.IO.File]::WriteAllText($other, 'safe', [System.Text.Encoding]::UTF8)

    & $helper -PackRoot $root -StandardUserSid $sid | Out-Null
    foreach ($file in @($setup, $other)) {
        $acl = Get-Acl -LiteralPath $file
        if (-not $acl.AreAccessRulesProtected) {
            throw 'CI pack file did not disable DACL inheritance.'
        }
        $stream = [System.IO.File]::Open(
            $file, [System.IO.FileMode]::Open,
            [System.IO.FileAccess]::Read, [System.IO.FileShare]::Read)
        try {
            if ($stream.Length -eq 0) {
                throw 'CI file-level read proof failed.'
            }
        }
        finally {
            $stream.Dispose()
        }
    }

    # An explicit Everyone grant on a disposable canary file must be
    # REJECTED, not silently tolerated/stripped in order to make CI green.
    & icacls.exe $other /grant '*S-1-1-0:R' 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw 'CI could not prepare explicit-broad-grant rejection test.'
    }
    $rejected = $false
    try {
        & $helper -PackRoot $root -StandardUserSid $sid | Out-Null
    }
    catch {
        $rejected = $true
    }
    if (-not $rejected) {
        throw 'ACL helper must reject a broad existing explicit allow ACE.'
    }

    'R42_12_4_PACK_ACL_CANARY=PASS'
}
finally {
    if (Test-Path -LiteralPath $root -PathType Container) {
        Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
    }
}
