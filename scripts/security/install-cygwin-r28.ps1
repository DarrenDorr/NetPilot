param(
    [string]$Root = 'C:\cygwin',
    [string]$LocalPackageDir = 'C:\cygwin-packages',
    [string]$Mirror = 'https://mirrors.kernel.org/sourceware/cygwin/'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$SetupVersion = '2.940'
$SetupUri = "https://cygwin.com/setup/setup-$SetupVersion.x86_64.exe"
$ExpectedSha512 = 'fc46e3ead0312ea632251c16334b23caab31c9b38db30db4e5418b43ffeec5c003cc867b4166c8222a622ad107660e2ce6e3d855499d08e695dd13010f43a396'
$Packages = 'gcc-core,make,zlib-devel'
$SetupPath = Join-Path $env:RUNNER_TEMP "cygwin-setup-$SetupVersion.x86_64.exe"

if (-not $Mirror.StartsWith('https://', [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'Cygwin mirror must use HTTPS.'
}

try {
    & curl.exe --fail --location --proto '=https' --tlsv1.2 --output $SetupPath $SetupUri
    if ($LASTEXITCODE -ne 0) {
        throw 'Exact Cygwin setup download failed.'
    }

    $ActualSha512 = (Get-FileHash -Algorithm SHA512 -LiteralPath $SetupPath).Hash.ToLowerInvariant()
    if ($ActualSha512 -ne $ExpectedSha512) {
        throw "Cygwin setup SHA-512 mismatch: $ActualSha512"
    }

    New-Item -ItemType Directory -Force -Path $LocalPackageDir | Out-Null

    $arguments = @(
        '-qgnO',
        '-l', $LocalPackageDir,
        '-R', $Root,
        '-s', $Mirror,
        '-P', $Packages
    )

    $process = Start-Process -FilePath $SetupPath -ArgumentList $arguments -Wait -PassThru -NoNewWindow
    if ($process.ExitCode -ne 0) {
        throw "Cygwin setup failed with exit code $($process.ExitCode)."
    }

    $cygcheck = Join-Path $Root 'bin\cygcheck.exe'
    $cygwinDll = Join-Path $Root 'bin\cygwin1.dll'
    $gcc = Join-Path $Root 'bin\gcc.exe'
    $make = Join-Path $Root 'bin\make.exe'

    foreach ($required in @($cygcheck, $cygwinDll, $gcc, $make)) {
        if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
            throw "Expected Cygwin toolchain file is missing: $required"
        }
    }

    Write-Output "cygwin_setup_version=$SetupVersion"
    Write-Output "cygwin_setup_sha512=$ActualSha512"
    Write-Output "cygwin_root=$Root"
    Write-Output "cygwin_mirror=$Mirror"
}
finally {
    Remove-Item -LiteralPath $SetupPath -Force -ErrorAction SilentlyContinue
}
