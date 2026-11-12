# Applies and verifies least-privilege DACLs on the disposable R42.12.4
# validation pack only. Never grants Users, Everyone or Authenticated Users.
# Run before any protected standard-user PowerShell process is launched.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$PackRoot,
    [Parameter(Mandatory = $true)][string]$StandardUserSid
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$null = [System.Security.Principal.SecurityIdentifier]::new($StandardUserSid)
if (-not [System.IO.Path]::IsPathFullyQualified($PackRoot)) {
    throw 'Restricted test pack path must be absolute.'
}
$root = Get-Item -LiteralPath $PackRoot -Force -ErrorAction Stop
if (-not $root.PSIsContainer) {
    throw 'Restricted test pack root must be a directory.'
}

# Snapshot the tree before modifying any ACL. Reject links and anything
# other than ordinary files/directories to avoid touching outside the pack.
$entries = @($root) + @(Get-ChildItem -LiteralPath $PackRoot -Recurse -Force -ErrorAction Stop)
if (@($entries | Where-Object { -not $_.PSIsContainer }).Count -eq 0) {
    throw 'Restricted test pack must contain regular files.'
}
foreach ($entry in $entries) {
    if (($entry.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw 'Restricted test pack cannot contain reparse points.'
    }
}

$testPrincipal = '*' + $StandardUserSid
$systemPrincipal = '*S-1-5-18'
$adminPrincipal = '*S-1-5-32-544'

# Use directory inheritance flags ONLY on directories. Recursion with /C
# can continue past individual file failures and must not be used here.
# Apply explicit rights to every copied file; granting RX on the root alone
# does not establish that the target installer EXE is readable.
foreach ($entry in $entries) {
    if ($entry.PSIsContainer) {
        $userGrant = $testPrincipal + ':(OI)(CI)RX'
        $systemGrant = $systemPrincipal + ':(OI)(CI)F'
        $adminGrant = $adminPrincipal + ':(OI)(CI)F'
    }
    else {
        $userGrant = $testPrincipal + ':RX'
        $systemGrant = $systemPrincipal + ':F'
        $adminGrant = $adminPrincipal + ':F'
    }

    & icacls.exe $entry.FullName /inheritance:r /grant:r $userGrant $systemGrant $adminGrant 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw 'Exact-SID pack ACL assignment failed on an individual entry.'
    }
}

# File-level validation must be done after all grants. No raw ACL text,
# account names, SIDs or complete file paths enter the evidence bundle.
$allowedIds = @($StandardUserSid, 'S-1-5-18', 'S-1-5-32-544')
$requiredRx = [int64][System.Security.AccessControl.FileSystemRights]::ReadAndExecute
$writeMask =
    [int64][System.Security.AccessControl.FileSystemRights]::Write -bor
    [int64][System.Security.AccessControl.FileSystemRights]::Delete -bor
    [int64][System.Security.AccessControl.FileSystemRights]::DeleteSubdirectoriesAndFiles -bor
    [int64][System.Security.AccessControl.FileSystemRights]::ChangePermissions -bor
    [int64][System.Security.AccessControl.FileSystemRights]::TakeOwnership

foreach ($entry in $entries) {
    $acl = Get-Acl -LiteralPath $entry.FullName -ErrorAction Stop
    if (-not $acl.AreAccessRulesProtected) {
        throw 'Restricted pack entry unexpectedly inherits its DACL.'
    }
    $rules = @($acl.GetAccessRules(
        $true, $false, [System.Security.Principal.SecurityIdentifier]))
    $exactSidReadOnly = $false
    foreach ($rule in $rules) {
        $sid = $rule.IdentityReference.Value
        if ($sid -notin $allowedIds -or
            $rule.AccessControlType -ne [System.Security.AccessControl.AccessControlType]::Allow) {
            throw 'Restricted pack contains an unexpected explicit ACE.'
        }
        if ($sid -ceq $StandardUserSid) {
            $rights = [int64]$rule.FileSystemRights
            if (($rights -band $requiredRx) -ne $requiredRx -or
                ($rights -band $writeMask) -ne 0) {
                throw 'Exact standard user requires RX, never write/modify/delete.'
            }
            $exactSidReadOnly = $true
        }
    }
    if (-not $exactSidReadOnly) {
        throw 'Restricted test user has no explicit file-level RX grant.'
    }
}
'R42_12_4_PACK_FILE_LEVEL_ACL_VERIFIED=PASS'
