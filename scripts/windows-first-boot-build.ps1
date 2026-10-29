[CmdletBinding()]
param(
    [string]$Version = "0.1.0-alpha-r31",
    [string]$VersionInfoVersion = "0.1.0.0"
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Assert-LastExitCode([string]$Step) {
    if ($LASTEXITCODE -ne 0) {
        throw "$Step завершился с кодом $LASTEXITCODE."
    }
}

if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
    throw 'R31 First Boot build gate должен выполняться на Windows.'
}

if (-not [Environment]::Is64BitOperatingSystem) {
    throw 'Для NetPilot First Boot требуется Windows x64.'
}

$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if ($null -eq $dotnet) {
    throw '.NET SDK не найден. Для сборки NetPilot требуется .NET 10 SDK.'
}

$sdks = @(& dotnet --list-sdks)
Assert-LastExitCode '.NET SDK enumeration'
if (-not ($sdks | Where-Object { $_ -match '^10\.' })) {
    throw '.NET 10 SDK не найден.'
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $repoRoot 'NetPilot.sln'
$installerBuild = Join-Path $PSScriptRoot 'build-installer.ps1'
$installerPath = Join-Path $repoRoot "artifacts\installer\NetPilot-Setup-$Version-x64.exe"

Push-Location $repoRoot
try {
    Write-Host '=== NetPilot R31 First Boot build gate ==='
    Write-Host "Repository: $repoRoot"
    Write-Host "Version:    $Version"
    Write-Host ''

    Write-Host '[1/7] Restore solution'
    & dotnet restore $solution
    Assert-LastExitCode 'dotnet restore'

    Write-Host '[2/7] Build solution (Release)'
    & dotnet build $solution --configuration Release --no-restore
    Assert-LastExitCode 'dotnet build'

    Write-Host '[3/7] Core / IPC tests'
    & dotnet test 'tests\NetPilot.Core.Tests\NetPilot.Core.Tests.csproj' --configuration Release --no-build
    Assert-LastExitCode 'NetPilot.Core.Tests'

    Write-Host '[4/7] Zapret parser / updater tests'
    & dotnet test 'tests\NetPilot.Engine.Zapret.Tests\NetPilot.Engine.Zapret.Tests.csproj' --configuration Release --no-build
    Assert-LastExitCode 'NetPilot.Engine.Zapret.Tests'

    Write-Host '[5/7] Windows security tests'
    & dotnet test 'tests\NetPilot.Platform.Windows.Tests\NetPilot.Platform.Windows.Tests.csproj' --configuration Release
    Assert-LastExitCode 'NetPilot.Platform.Windows.Tests'

    Write-Host '[6/7] Build First Boot IPC probe'
    & dotnet build 'tools\NetPilot.WindowsIpcLabClient\NetPilot.WindowsIpcLabClient.csproj' --configuration Release
    Assert-LastExitCode 'NetPilot.WindowsIpcLabClient build'

    Write-Host '[7/7] Build verified installer'
    & pwsh -NoLogo -NoProfile -File $installerBuild `
        -Version $Version `
        -VersionInfoVersion $VersionInfoVersion
    Assert-LastExitCode 'NetPilot installer build'

    if (-not (Test-Path $installerPath)) {
        throw "Собранный установщик не найден: $installerPath"
    }

    $installer = Get-Item $installerPath
    $hash = Get-FileHash -Algorithm SHA256 -Path $installerPath

    Write-Host ''
    Write-Host '=== FIRST BOOT BUILD READY ==='
    Write-Host "INSTALLER=$($installer.FullName)"
    Write-Host "INSTALLER_SIZE=$($installer.Length)"
    Write-Host "INSTALLER_SHA256=$($hash.Hash)"
    Write-Host 'NATIVE_ENGINE_ADMISSION=BLOCKED'
    Write-Host 'RESULT=FIRST_BOOT_BUILD_READY'
}
finally {
    Pop-Location
}
