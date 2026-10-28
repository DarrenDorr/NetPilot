param(
    [string]$Version = "0.1.0-alpha",
    [string]$VersionInfoVersion = "0.1.0.0",
    [string]$OutputDir = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-Path $repoRoot "artifacts\installer"
}

$appPublishDir = Join-Path $repoRoot "artifacts\publish\app-win-x64"
$servicePublishDir = Join-Path $repoRoot "artifacts\publish\service-win-x64"
$toolsDir = Join-Path $repoRoot "artifacts\tools"
$innoInstallDir = Join-Path $toolsDir "inno-7.1.0"
$innoInstaller = Join-Path $toolsDir "innosetup-7.1.0-x64.exe"
$innoUrl = "https://github.com/jrsoftware/issrc/releases/download/is-7_1_0/innosetup-7.1.0-x64.exe"
$innoSha256 = "0362a383ed217d4c4239b5933866dd96d3eb2102737da92f80f6057a4b40df2f"

New-Item -ItemType Directory -Force -Path $appPublishDir, $servicePublishDir, $toolsDir, $OutputDir | Out-Null

Write-Host "Публикация NetPilot.App.Windows (self-contained win-x64)..."
dotnet publish (Join-Path $repoRoot "src\NetPilot.App.Windows\NetPilot.App.Windows.csproj") `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    --output $appPublishDir `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -p:PublishSingleFile=false `
    -p:Version=$VersionInfoVersion

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish NetPilot.App.Windows завершился с ошибкой."
}

Write-Host "Публикация NetPilot.Service (self-contained win-x64)..."
dotnet publish (Join-Path $repoRoot "src\NetPilot.Service\NetPilot.Service.csproj") `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    --output $servicePublishDir `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -p:PublishSingleFile=false `
    -p:Version=$VersionInfoVersion

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish NetPilot.Service завершился с ошибкой."
}

if (-not (Test-Path $innoInstaller)) {
    Write-Host "Загрузка закреплённого Inno Setup 7.1.0 x64..."
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
    Write-Host "Локальная установка Inno Setup 7.1.0 для сборки..."
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
