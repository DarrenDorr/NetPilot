param(
    [string]$Destination = "src/NetPilot.App/Assets/NetPilotUiKit"
)

$ErrorActionPreference = "Stop"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$archive = Join-Path $scriptDir "NetPilot_UI_Kit_v1_1.zip"

if (-not (Test-Path $archive)) {
    throw "UI Kit archive not found: $archive"
}

$repoRoot = Resolve-Path (Join-Path $scriptDir "..\..\..")
$target = Join-Path $repoRoot $Destination

if (Test-Path $target) {
    Remove-Item $target -Recurse -Force
}

New-Item -ItemType Directory -Path $target -Force | Out-Null
Expand-Archive -Path $archive -DestinationPath $target -Force
Write-Host "NetPilot UI Kit extracted to: $target"
