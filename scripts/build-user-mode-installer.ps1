param(
    [string]$Version = "0.1.0-alpha",
    [string]$VersionInfoVersion = "0.1.0.0",
    [Parameter(Mandatory = $true)]
    [string]$ByeDpiPackageDir,
    [string]$OutputDir = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-Path $repoRoot "artifacts\installer-user-mode"
}

if ($VersionInfoVersion -notmatch '^\d+\.\d+\.\d+(?:\.\d+)?$') {
    throw "VersionInfoVersion должен иметь вид 0.1.0.0."
}

$versionParts = $VersionInfoVersion.Split('.')
$packageVersion = ($versionParts[0..2] -join '.')
$ByeDpiPackageDir = [System.IO.Path]::GetFullPath($ByeDpiPackageDir)
$candidate = Join-Path $ByeDpiPackageDir "bin\ciadpi.exe"
$license = Join-Path $ByeDpiPackageDir "LICENSE"
$expectedCandidateSize = 495685
$expectedCandidateSha256 = "866e99c3c7690326cc6e365fe02980f06ae2b394c045555674fb95db52b0dbdd"
$expectedLicenseSize = 1063
$expectedLicenseSha256 = "18c854c6c25dffefe4b522cf1bdf5d15278a25ca785948258bddb94393f575eb"

function Assert-ExactFile {
    param(
        [Parameter(Mandatory = $true)] [string]$Path,
        [Parameter(Mandatory = $true)] [long]$Size,
        [Parameter(Mandatory = $true)] [string]$Sha256,
        [Parameter(Mandatory = $true)] [string]$Label
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "$Label отсутствует: $Path"
    }

    $item = Get-Item -LiteralPath $Path -Force
    if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "$Label не может быть reparse point."
    }
    if ($item.Length -ne $Size) {
        throw "$Label имеет неожиданный размер: $($item.Length)."
    }

    $actual = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -cne $Sha256) {
        throw "$Label SHA-256 не совпал с проверенным значением."
    }
}

Assert-ExactFile -Path $candidate -Size $expectedCandidateSize -Sha256 $expectedCandidateSha256 -Label "ByeDPI ciadpi.exe"
Assert-ExactFile -Path $license -Size $expectedLicenseSize -Sha256 $expectedLicenseSha256 -Label "ByeDPI LICENSE"

$appPublishDir = Join-Path $repoRoot "artifacts\publish\app-user-mode-win-x64"
$toolsDir = Join-Path $repoRoot "artifacts\tools"
$innoInstallDir = Join-Path $toolsDir "inno-7.1.1"
$innoInstaller = Join-Path $toolsDir "innosetup-7.1.1-x64.exe"
$innoUrl = "https://github.com/jrsoftware/issrc/releases/download/is-7_1_1/innosetup-7.1.1-x64.exe"
$innoSha256 = "8a5a6711c3766a6e5fb74f280ba0e3551e719ce94538d901fdbd1f8f9920b3f0"

New-Item -ItemType Directory -Force -Path $appPublishDir, $toolsDir, $OutputDir | Out-Null

$publishVersionArgs = @(
    "-p:Version=$packageVersion",
    "-p:FileVersion=$VersionInfoVersion",
    "-p:AssemblyVersion=$VersionInfoVersion",
    "-p:InformationalVersion=$Version"
)

Write-Host "Публикация NetPilot.App.Windows user-mode (self-contained win-x64)..."
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

if (-not (Test-Path $innoInstaller)) {
    Write-Host "Загрузка закреплённого Inno Setup 7.1.1 x64..."
    Invoke-WebRequest -Uri $innoUrl -OutFile $innoInstaller -UseBasicParsing
}

$actualHash = (Get-FileHash -LiteralPath $innoInstaller -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actualHash -cne $innoSha256) {
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

$iss = Join-Path $repoRoot "installer\NetPilot.UserMode.iss"
Write-Host "Компиляция NetPilot UserMode Setup..."
& $iscc `
    "/DAppVersion=$Version" `
    "/DVersionInfoVersion=$VersionInfoVersion" `
    "/DAppPublishDir=$appPublishDir" `
    "/DByeDpiPackageDir=$ByeDpiPackageDir" `
    "/DInstallerOutputDir=$OutputDir" `
    $iss

if ($LASTEXITCODE -ne 0) {
    throw "ISCC user-mode завершился с ошибкой."
}

$installer = Get-ChildItem -Path $OutputDir -Filter "NetPilot-UserMode-Setup-$Version-x64.exe" -File | Select-Object -First 1
if ($null -eq $installer) {
    throw "Собранный user-mode установщик NetPilot не найден."
}

Write-Host "User-mode установщик готов: $($installer.FullName)"
