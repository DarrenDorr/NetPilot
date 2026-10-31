param(
    [string]$Version = "0.1.0-alpha",
    [string]$VersionInfoVersion = "0.1.0.0",
    [string]$OutputDir = "",
    [switch]$EnableExperimentalByeDpi,
    [string]$ByeDpiPayloadPath = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-Path $repoRoot "artifacts\installer"
}

if ($VersionInfoVersion -notmatch '^\d+\.\d+\.\d+(?:\.\d+)?$') {
    throw "VersionInfoVersion должен иметь вид 0.1.0.0."
}

$versionParts = $VersionInfoVersion.Split('.')
$packageVersion = ($versionParts[0..2] -join '.')

$appPublishDir = Join-Path $repoRoot "artifacts\publish\app-win-x64"
$servicePublishDir = Join-Path $repoRoot "artifacts\publish\service-win-x64"
$toolsDir = Join-Path $repoRoot "artifacts\tools"
$innoInstallDir = Join-Path $toolsDir "inno-7.1.1"
$innoInstaller = Join-Path $toolsDir "innosetup-7.1.1-x64.exe"
$innoUrl = "https://github.com/jrsoftware/issrc/releases/download/is-7_1_1/innosetup-7.1.1-x64.exe"
$innoSha256 = "8a5a6711c3766a6e5fb74f280ba0e3551e719ce94538d901fdbd1f8f9920b3f0"
$byeDpiSha256 = "866e99c3c7690326cc6e365fe02980f06ae2b394c045555674fb95db52b0dbdd"
$byeDpiPackageVersion = "0.17.3-netpilot-r34-lab"

New-Item -ItemType Directory -Force -Path $appPublishDir, $servicePublishDir, $toolsDir, $OutputDir | Out-Null

$publishVersionArgs = @(
    "-p:Version=$packageVersion",
    "-p:FileVersion=$VersionInfoVersion",
    "-p:AssemblyVersion=$VersionInfoVersion",
    "-p:InformationalVersion=$Version"
)

if ($EnableExperimentalByeDpi) {
    if ([string]::IsNullOrWhiteSpace($ByeDpiPayloadPath)) {
        throw "Experimental ByeDPI build требует -ByeDpiPayloadPath."
    }

    $ByeDpiPayloadPath = (Resolve-Path $ByeDpiPayloadPath).Path
    $payloadItem = Get-Item -LiteralPath $ByeDpiPayloadPath
    if ($payloadItem.Length -le 0) {
        throw "ByeDPI payload пуст."
    }

    $actualByeDpiHash = (Get-FileHash -LiteralPath $ByeDpiPayloadPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualByeDpiHash -ne $byeDpiSha256) {
        throw "SHA-256 ByeDPI не совпал с exact R34-R40 candidate."
    }

    $publishVersionArgs += "-p:NetPilotExperimentalByeDpi=true"
    Write-Host "Experimental ByeDPI payload verified: SHA256=$actualByeDpiHash SIZE=$($payloadItem.Length)"
}

Write-Host "Публикация NetPilot.App.Windows (self-contained win-x64)..."
& dotnet publish (Join-Path $repoRoot "src\NetPilot.App.Windows\NetPilot.App.Windows.csproj") `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    --output $appPublishDir `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -p:PublishSingleFile=false `
    @publishVersionArgs

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish NetPilot.App.Windows завершился с ошибкой."
}

Write-Host "Публикация NetPilot.Service (self-contained win-x64)..."
& dotnet publish (Join-Path $repoRoot "src\NetPilot.Service\NetPilot.Service.csproj") `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    --output $servicePublishDir `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -p:PublishSingleFile=false `
    @publishVersionArgs

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish NetPilot.Service завершился с ошибкой."
}

if (-not (Test-Path $innoInstaller)) {
    Write-Host "Загрузка закреплённого Inno Setup 7.1.1 x64..."
    Invoke-WebRequest -Uri $innoUrl -OutFile $innoInstaller -UseBasicParsing
}

$actualHash = (Get-FileHash -Path $innoInstaller -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actualHash -ne $innoSha256) {
    Remove-Item -Force $innoInstaller -ErrorAction SilentlyContinue
    throw "SHA-256 Inno Setup не совпал с закреплённым значением."
}

$signature = Get-AuthenticodeSignature -FilePath $innoInstaller
if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid) {
    throw "Authenticode-подпись Inno Setup недействительна: $($signature.Status)."
}

if ($null -eq $signature.SignerCertificate -or $signature.SignerCertificate.Subject -notmatch "Pyrsys B\.V\.") {
    throw "Подписант Inno Setup не совпадает с ожидаемым издателем Pyrsys B.V."
}

$iscc = Join-Path $innoInstallDir "ISCC.exe"
if (-not (Test-Path $iscc)) {
    Write-Host "Локальная установка Inno Setup 7.1.1 для сборки..."
    $arguments = @(
        "/VERYSILENT",
        "/SUPPRESSMSGBOXES",
        "/NORESTART",
        "/CURRENTUSER",
        "/DIR=`"$innoInstallDir`""
    )

    $process = Start-Process -FilePath $innoInstaller -ArgumentList $arguments -Wait -PassThru
    if ($process.ExitCode -ne 0) {
        throw "Установка Inno Setup для сборки завершилась с кодом $($process.ExitCode)."
    }
}

if (-not (Test-Path $iscc)) {
    throw "ISCC.exe не найден после установки Inno Setup."
}

$iss = Join-Path $repoRoot "installer\NetPilot.iss"
if ($EnableExperimentalByeDpi) {
    $issText = Get-Content -LiteralPath $iss -Raw
    if ($issText -notmatch '(?m)^\[Files\]\s*$') {
        throw "В installer script не найден [Files] для controlled ByeDPI injection."
    }

    $escapedPayload = $ByeDpiPayloadPath.Replace('"', '""')
    $byeDpiDest = "{app}\Engines\Managed\byedpi\$byeDpiPackageVersion\x64\bin"
    $byeDpiLicense = (Join-Path $repoRoot "third_party\licenses\ByeDPI-MIT.txt").Replace('"', '""')
    if (-not (Test-Path -LiteralPath $byeDpiLicense)) {
        throw "Локальная копия ByeDPI MIT license не найдена."
    }

    $injectedFiles = @(
        "Source: `"$escapedPayload`"; DestDir: `"$byeDpiDest`"; DestName: `"ciadpi.exe`"; Flags: ignoreversion notimestamp",
        "Source: `"$byeDpiLicense`"; DestDir: `"{app}\Docs`"; DestName: `"ByeDPI-MIT.txt`"; Flags: ignoreversion notimestamp"
    ) -join "`r`n"

    $issText = [regex]::Replace(
        $issText,
        '(?m)^\[Files\]\s*$',
        "[Files]`r`n$injectedFiles",
        1)

    $experimentalIss = Join-Path $toolsDir "NetPilot.experimental-byedpi.iss"
    Set-Content -LiteralPath $experimentalIss -Value $issText -Encoding utf8
    $iss = $experimentalIss
}

Write-Host "Компиляция NetPilot Setup..."
& $iscc `
    "/DAppVersion=$Version" `
    "/DVersionInfoVersion=$VersionInfoVersion" `
    "/DAppPublishDir=$appPublishDir" `
    "/DServicePublishDir=$servicePublishDir" `
    "/DInstallerOutputDir=$OutputDir" `
    $iss

if ($LASTEXITCODE -ne 0) {
    throw "ISCC завершился с ошибкой."
}

$installer = Get-ChildItem -Path $OutputDir -Filter "NetPilot-Setup-$Version-x64.exe" -File | Select-Object -First 1
if ($null -eq $installer) {
    throw "Собранный установщик NetPilot не найден."
}

Write-Host "Установщик готов: $($installer.FullName)"
