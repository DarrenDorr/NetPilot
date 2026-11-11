# R42.12.4 hosted standard-user bootstrap preflight (Windows PowerShell 5.1).
# Must run as the exact isolated non-administrator SID. No installer, network
# request or privilege change occurs in this diagnostic preflight.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

try {
    $identity = [System.Security.Principal.WindowsIdentity]::GetCurrent()
    if ($null -eq $identity.User -or $identity.User.Value -cne '__USER_SID__') {
        exit 21
    }
    $principal = New-Object System.Security.Principal.WindowsPrincipal($identity)
    if ($principal.IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)) {
        exit 22
    }

    $destination = 'C:\Users\Public\NetPilot-R42.12.4-Evidence\standard-user-preflight.txt'
    if (Test-Path -LiteralPath $destination) {
        exit 23
    }

    [System.IO.File]::WriteAllText(
        $destination,
        "PREFLIGHT_SID_MATCH=PASS`nPREFLIGHT_ADMIN_ROLE=False`nPREFLIGHT_EVIDENCE_WRITE=PASS`n",
        [System.Text.UTF8Encoding]::new($false))
    exit 0
}
catch {
    # Never log exception messages or environment/user details.
    exit 24
}
