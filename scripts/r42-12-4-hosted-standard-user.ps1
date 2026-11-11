param(
    [Parameter(Mandatory = $true)]
    [string]$PackPath,
    [Parameter(Mandatory = $true)]
    [string]$HostedEvidenceDir,
    [Parameter(Mandatory = $true)]
    [string]$TestVersion,
    [string]$UserName = 'NetPilotR42124',
    [string]$TaskName = 'NetPilot-R42.12.4-StandardUser'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$null = $TaskName

$PackPath = [System.IO.Path]::GetFullPath($PackPath)
$HostedEvidenceDir = [System.IO.Path]::GetFullPath($HostedEvidenceDir)
$publicPack = 'C:\Users\Public\NetPilot-R42.12.4-Pack'
$publicEvidence = 'C:\Users\Public\NetPilot-R42.12.4-Evidence'
$userAccount = "$env:COMPUTERNAME\$UserName"
$createdUserSid = $null
$validationSucceeded = $false
$failure = $null
$nativeChild = $null

function Copy-EvidenceBestEffort {
    New-Item -ItemType Directory -Force -Path $HostedEvidenceDir | Out-Null
    if (-not (Test-Path -LiteralPath $publicEvidence -PathType Container)) {
        return
    }

    Get-ChildItem -LiteralPath $publicEvidence -Force -ErrorAction SilentlyContinue | ForEach-Object {
        try {
            Copy-Item -LiteralPath $_.FullName -Destination $HostedEvidenceDir -Recurse -Force
        }
        catch {
            Write-Warning "Could not preserve hosted evidence item: $($_.Name)"
        }
    }
}

function Require-Marker {
    param(
        [Parameter(Mandatory = $true)] [string]$Text,
        [Parameter(Mandatory = $true)] [string]$Marker
    )

    if (-not $Text.Contains($Marker, [StringComparison]::Ordinal)) {
        throw "Required no-admin evidence marker missing: $Marker"
    }
}

function Get-DeepestException {
    param([Parameter(Mandatory = $true)] [System.Exception]$Exception)
    $current = $Exception
    while ($null -ne $current.InnerException) {
        $current = $current.InnerException
    }
    return $current
}

$nativeLauncherSource = @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

public static class NetPilotStandardUserTokenLauncher
{
    private const int LOGON32_LOGON_INTERACTIVE = 2;
    private const int LOGON32_PROVIDER_DEFAULT = 0;
    private const int LOGON_WITH_PROFILE = 0x00000001;
    private const int CREATE_NO_WINDOW = 0x08000000;
    private const uint WAIT_OBJECT_0 = 0x00000000;
    private const uint WAIT_TIMEOUT = 0x00000102;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO
    {
        public int cb;
        public string lpReserved;
        public string lpDesktop;
        public string lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public int dwProcessId;
        public int dwThreadId;
    }

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LogonUserW(
        string lpszUsername,
        string lpszDomain,
        string lpszPassword,
        int dwLogonType,
        int dwLogonProvider,
        out IntPtr phToken);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcessWithTokenW(
        IntPtr hToken,
        int dwLogonFlags,
        string lpApplicationName,
        StringBuilder lpCommandLine,
        int dwCreationFlags,
        IntPtr lpEnvironment,
        string lpCurrentDirectory,
        ref STARTUPINFO lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetExitCodeProcess(IntPtr hProcess, out uint lpExitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateProcess(IntPtr hProcess, uint uExitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    public sealed class ChildProcess : IDisposable
    {
        private IntPtr _process;
        public int Id { get; }
        public string LaunchMethod { get; }

        internal ChildProcess(IntPtr process, int id, string launchMethod)
        {
            _process = process;
            Id = id;
            LaunchMethod = launchMethod;
        }

        public int Wait(int milliseconds)
        {
            if (_process == IntPtr.Zero)
                throw new ObjectDisposedException(nameof(ChildProcess));

            uint result = WaitForSingleObject(_process, checked((uint)milliseconds));
            if (result == WAIT_TIMEOUT)
                return int.MinValue;
            if (result != WAIT_OBJECT_0)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "WaitForSingleObject failed.");

            if (!GetExitCodeProcess(_process, out uint exitCode))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "GetExitCodeProcess failed.");

            return unchecked((int)exitCode);
        }

        public void Kill()
        {
            if (_process == IntPtr.Zero)
                return;

            if (!TerminateProcess(_process, 1))
            {
                int error = Marshal.GetLastWin32Error();
                if (error != 5)
                    throw new Win32Exception(error, "TerminateProcess failed.");
            }
        }

        public void Dispose()
        {
            if (_process != IntPtr.Zero)
            {
                CloseHandle(_process);
                _process = IntPtr.Zero;
            }
        }
    }

    public static ChildProcess Start(
        string userName,
        string password,
        string applicationPath,
        string arguments,
        string workingDirectory)
    {
        IntPtr token = IntPtr.Zero;
        PROCESS_INFORMATION processInfo = default(PROCESS_INFORMATION);

        if (!LogonUserW(
            userName,
            ".",
            password,
            LOGON32_LOGON_INTERACTIVE,
            LOGON32_PROVIDER_DEFAULT,
            out token))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "LogonUserW failed.");
        }

        try
        {
            var startup = new STARTUPINFO { cb = Marshal.SizeOf<STARTUPINFO>() };
            var commandLine = new StringBuilder();
            commandLine.Append('"').Append(applicationPath).Append('"');
            if (!string.IsNullOrWhiteSpace(arguments))
                commandLine.Append(' ').Append(arguments);

            // CreateProcessWithTokenW caps lpCommandLine at 1,024 UTF-16
            // characters including NUL; never pass an encoded script here.
            if (commandLine.Length >= 1024)
                throw new ArgumentException("Hosted standard-user command line exceeds Windows API limit.");

            if (!CreateProcessWithTokenW(
                token,
                LOGON_WITH_PROFILE,
                applicationPath,
                commandLine,
                CREATE_NO_WINDOW,
                IntPtr.Zero,
                workingDirectory,
                ref startup,
                out processInfo))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateProcessWithTokenW failed.");
            }

            if (processInfo.hThread != IntPtr.Zero)
            {
                CloseHandle(processInfo.hThread);
                processInfo.hThread = IntPtr.Zero;
            }

            return new ChildProcess(
                processInfo.hProcess,
                processInfo.dwProcessId,
                "LogonUserW+CreateProcessWithTokenW");
        }
        catch
        {
            if (processInfo.hThread != IntPtr.Zero)
                CloseHandle(processInfo.hThread);
            if (processInfo.hProcess != IntPtr.Zero)
                CloseHandle(processInfo.hProcess);
            throw;
        }
        finally
        {
            if (token != IntPtr.Zero)
                CloseHandle(token);
        }
    }
}
'@

try {
    if (-not (Test-Path -LiteralPath $PackPath -PathType Container)) {
        throw "Validation pack directory is missing: $PackPath"
    }

    if (Get-Service -Name 'NetPilot.Service' -ErrorAction SilentlyContinue) {
        throw 'NetPilot.Service already exists before the no-admin hosted boundary.'
    }
    if (@(Get-Process -Name ciadpi -ErrorAction SilentlyContinue).Count -ne 0) {
        throw 'ciadpi already exists before the no-admin hosted boundary.'
    }

    $existing = Get-LocalUser -Name $UserName -ErrorAction SilentlyContinue
    if ($null -ne $existing) {
        Remove-LocalUser -Name $UserName
    }

    Remove-Item -LiteralPath $publicPack -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $publicEvidence -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force -Path $publicPack, $publicEvidence, $HostedEvidenceDir | Out-Null

    $passwordPlain = 'Aa1!' + [guid]::NewGuid().ToString('N')
    Write-Output "::add-mask::$passwordPlain"
    $passwordSecure = ConvertTo-SecureString $passwordPlain -AsPlainText -Force
    $createdUser = New-LocalUser `
        -Name $UserName `
        -Password $passwordSecure `
        -PasswordNeverExpires `
        -UserMayNotChangePassword
    $createdUserSid = $createdUser.SID.Value

    $usersSid = New-Object System.Security.Principal.SecurityIdentifier('S-1-5-32-545')
    $usersGroup = Get-LocalGroup -SID $usersSid
    Add-LocalGroupMember -Group $usersGroup -Member $userAccount

    $adminSid = New-Object System.Security.Principal.SecurityIdentifier('S-1-5-32-544')
    $adminGroup = Get-LocalGroup -SID $adminSid
    $adminMembers = @(Get-LocalGroupMember -Group $adminGroup -ErrorAction Stop)
    if ($adminMembers.Name -contains $userAccount) {
        throw 'Standard test user unexpectedly belongs to Administrators.'
    }

    Get-ChildItem -LiteralPath $PackPath -Force | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $publicPack -Recurse -Force
    }

    & icacls.exe `
        $publicPack `
        /inheritance:r `
        /grant:r `
        "*${createdUserSid}:(OI)(CI)RX" `
        '*S-1-5-18:(OI)(CI)F' `
        '*S-1-5-32-544:(OI)(CI)F' `
        /T `
        /C | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw 'Could not apply read-only pack ACL for the exact standard-user SID.'
    }

    & icacls.exe `
        $publicEvidence `
        /inheritance:r `
        /grant:r `
        "*${createdUserSid}:(OI)(CI)M" `
        '*S-1-5-18:(OI)(CI)F' `
        '*S-1-5-32-544:(OI)(CI)F' `
        /T `
        /C | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw 'Could not apply evidence ACL for the exact standard-user SID.'
    }

    'STANDARD_USER_CREATED=PASS' | Add-Content (Join-Path $HostedEvidenceDir 'boundary.txt')
    'STANDARD_USER_ADMIN_GROUP=False' | Add-Content (Join-Path $HostedEvidenceDir 'boundary.txt')
    'STANDARD_USER_LAUNCH_METHOD=LogonUserW+CreateProcessWithTokenW' | Add-Content (Join-Path $HostedEvidenceDir 'boundary.txt')
    'PACK_ACL_PRINCIPAL=EXACT_TEST_USER_SID' | Add-Content (Join-Path $HostedEvidenceDir 'boundary.txt')
    'PACK_ACL_STANDARD_USER=RX' | Add-Content (Join-Path $HostedEvidenceDir 'boundary.txt')
    'EVIDENCE_ACL_PRINCIPAL=EXACT_TEST_USER_SID' | Add-Content (Join-Path $HostedEvidenceDir 'boundary.txt')
    'EVIDENCE_ACL_STANDARD_USER=M' | Add-Content (Join-Path $HostedEvidenceDir 'boundary.txt')

    $runnerTemplate = @'
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$pack = 'C:\Users\Public\NetPilot-R42.12.4-Pack'
$evidence = 'C:\Users\Public\NetPilot-R42.12.4-Evidence'
$result = Join-Path $evidence 'standard-user-boundary.txt'
try {
    $identity = [System.Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object System.Security.Principal.WindowsPrincipal($identity)
    if ($null -eq $identity.User -or $identity.User.Value -cne '__USER_SID__') {
        throw 'Token launch did not receive the expected standard-user SID.'
    }
    if ($principal.IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'Token launch received an Administrator token.'
    }

    'RUNNER_IDENTITY_MATCH=PASS' | Set-Content -LiteralPath $result -Encoding UTF8
    'RUNNER_ADMIN_ROLE=False' | Add-Content -LiteralPath $result

    $localAppData = [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
    $roamingAppData = [Environment]::GetFolderPath([Environment+SpecialFolder]::ApplicationData)
    $userProfile = [Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)
    if ([string]::IsNullOrWhiteSpace($localAppData) -or [string]::IsNullOrWhiteSpace($userProfile)) {
        throw 'Loaded standard-user profile did not expose LocalAppData/UserProfile.'
    }

    $env:LOCALAPPDATA = $localAppData
    $env:APPDATA = $roamingAppData
    $env:USERPROFILE = $userProfile
    'STANDARD_USER_PROFILE_LOADED=PASS' | Add-Content -LiteralPath $result

    $setup = Join-Path $pack 'NetPilot-UserMode-Setup-__VERSION__-x64.exe'
    $install = Start-Process `
        -FilePath $setup `
        -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-' `
        -Wait `
        -PassThru
    "INSTALLER_EXIT_CODE=$($install.ExitCode)" | Add-Content -LiteralPath $result
    if ($install.ExitCode -ne 0) {
        throw "User-mode installer failed: $($install.ExitCode)"
    }

    $installRoot = Join-Path $localAppData 'Programs\NetPilot'
    if (-not (Test-Path -LiteralPath (Join-Path $installRoot 'App\NetPilot.exe') -PathType Leaf)) {
        throw 'Per-user NetPilot.exe was not installed beneath LocalAppData.'
    }
    if (Get-Service -Name 'NetPilot.Service' -ErrorAction SilentlyContinue) {
        throw 'NetPilot.Service unexpectedly exists during no-admin validation.'
    }

    'PER_USER_INSTALL=PASS' | Add-Content -LiteralPath $result
    'NETPILOT_SERVICE_PRESENT=False' | Add-Content -LiteralPath $result

    $validator = Join-Path $pack 'NetPilot-ByeDPI-NoAdmin-Validation.exe'
    $validation = Start-Process `
        -FilePath $validator `
        -ArgumentList '--install-root',$installRoot,'--evidence-dir',$evidence `
        -Wait `
        -PassThru
    "VALIDATOR_EXIT_CODE=$($validation.ExitCode)" | Add-Content -LiteralPath $result
    if ($validation.ExitCode -ne 0) {
        throw "No-admin validator failed: $($validation.ExitCode)"
    }

    'STANDARD_USER_VALIDATION=PASS' | Add-Content -LiteralPath $result

    $uninstaller = Join-Path $installRoot 'unins000.exe'
    if (Test-Path -LiteralPath $uninstaller -PathType Leaf) {
        $uninstall = Start-Process `
            -FilePath $uninstaller `
            -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART' `
            -Wait `
            -PassThru
        "UNINSTALLER_EXIT_CODE=$($uninstall.ExitCode)" | Add-Content -LiteralPath $result
        if ($uninstall.ExitCode -ne 0) {
            throw "Per-user uninstall failed: $($uninstall.ExitCode)"
        }
    }

    'DONE=PASS' | Add-Content -LiteralPath $result
}
catch {
    if (-not (Test-Path -LiteralPath $result -PathType Leaf)) {
        'RUNNER_STARTED=True' | Set-Content -LiteralPath $result -Encoding UTF8
    }

    "RUNNER_FAILURE_TYPE=$($_.Exception.GetType().FullName)" | Add-Content -LiteralPath $result
    "RUNNER_FAILURE_HRESULT=0x$($_.Exception.HResult.ToString('X8'))" | Add-Content -LiteralPath $result
    'DONE=FAIL' | Add-Content -LiteralPath $result
    exit 1
}
'@

    $runnerCode = $runnerTemplate.Replace('__VERSION__', $TestVersion).Replace('__USER_SID__', $createdUserSid)
    # The test SID already has RX, not write, on the protected pack root.
    # Store the runner there instead of passing 10K+ characters via -EncodedCommand:
    # CreateProcessWithTokenW rejects command lines longer than 1,024 chars.
    $runnerScript = Join-Path $publicPack 'NetPilot-StandardUser-Runner.ps1'
    if (Test-Path -LiteralPath $runnerScript) {
        throw 'Protected hosted runner path already exists.'
    }
    [System.IO.File]::WriteAllText(
        $runnerScript,
        $runnerCode,
        [System.Text.UTF8Encoding]::new($false))
    if (((Get-Item -LiteralPath $runnerScript -Force).Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw 'Hosted runner may not be a reparse point.'
    }

    # Apply exact-SID read/execute, SYSTEM and Administrators only; do not
    # grant modification of the script to the standard-user test identity.
    & icacls.exe $runnerScript /inheritance:r /grant:r "*${createdUserSid}:RX" '*S-1-5-18:F' '*S-1-5-32-544:F' | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw 'Could not protect hosted runner script with exact-SID read-only ACL.'
    }
    'HOSTED_RUNNER_SCRIPT_ACL=EXACT_TEST_USER_RX' | Add-Content (Join-Path $HostedEvidenceDir 'boundary.txt')

    $powershell = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    $arguments = "-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File `"$runnerScript`""
    if (($powershell.Length + $arguments.Length + 4) -ge 1024) {
        throw 'Hosted runner command line exceeds CreateProcessWithTokenW length limit.'
    }

    Add-Type -TypeDefinition $nativeLauncherSource -Language CSharp
    $nativeChild = [NetPilotStandardUserTokenLauncher]::Start(
        $UserName,
        $passwordPlain,
        $powershell,
        $arguments,
        $publicPack)

    if ($null -eq $nativeChild) {
        throw 'Native standard-user process was not created.'
    }

    "DIRECT_STANDARD_USER_PROCESS_ID=$($nativeChild.Id)" | Add-Content (Join-Path $HostedEvidenceDir 'boundary.txt')
    "DIRECT_STANDARD_USER_LAUNCH_METHOD=$($nativeChild.LaunchMethod)" | Add-Content (Join-Path $HostedEvidenceDir 'boundary.txt')
    'DIRECT_STANDARD_USER_PROCESS_CREATED=PASS' | Add-Content (Join-Path $HostedEvidenceDir 'boundary.txt')

    $exitCode = $nativeChild.Wait(600000)
    if ($exitCode -eq [int]::MinValue) {
        try { $nativeChild.Kill() } catch { }
        throw 'Native standard-user validation process timed out.'
    }

    "STANDARD_USER_PROCESS_EXIT_CODE=$exitCode" | Add-Content (Join-Path $HostedEvidenceDir 'boundary.txt')
    if ($exitCode -ne 0) {
        throw "Native standard-user validation process failed with exit code $exitCode."
    }

    $boundary = Join-Path $publicEvidence 'standard-user-boundary.txt'
    if (-not (Test-Path -LiteralPath $boundary -PathType Leaf)) {
        throw 'Standard-user boundary evidence was not created.'
    }

    $boundaryText = Get-Content -LiteralPath $boundary -Raw
    Require-Marker -Text $boundaryText -Marker 'RUNNER_IDENTITY_MATCH=PASS'
    Require-Marker -Text $boundaryText -Marker 'RUNNER_ADMIN_ROLE=False'
    Require-Marker -Text $boundaryText -Marker 'STANDARD_USER_PROFILE_LOADED=PASS'
    Require-Marker -Text $boundaryText -Marker 'INSTALLER_EXIT_CODE=0'
    Require-Marker -Text $boundaryText -Marker 'PER_USER_INSTALL=PASS'
    Require-Marker -Text $boundaryText -Marker 'NETPILOT_SERVICE_PRESENT=False'
    Require-Marker -Text $boundaryText -Marker 'VALIDATOR_EXIT_CODE=0'
    Require-Marker -Text $boundaryText -Marker 'STANDARD_USER_VALIDATION=PASS'
    Require-Marker -Text $boundaryText -Marker 'DONE=PASS'

    $validationTxt = Get-ChildItem `
        -LiteralPath $publicEvidence `
        -Filter 'ByeDPI-NoAdmin-Validation-*-UTC.txt' `
        -File |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1
    if ($null -eq $validationTxt) {
        throw 'Detailed no-admin validation evidence is missing.'
    }

    $validationText = Get-Content -LiteralPath $validationTxt.FullName -Raw
    $requiredMarkers = @(
        'CURRENT_TOKEN_NON_ELEVATED=PASS',
        'INSTALL_SCOPE=CURRENT_USER_LOCALAPPDATA',
        "CANDIDATE_SHA256=$env:BYEDPI_WINDOWS_SHA256",
        'INSTALLED_EXACT_CANDIDATE=PASS',
        'NETPILOT_SERVICE_PRESENT_BEFORE=False',
        'BYEDPI_USER_MODE_START=PASS',
        'BYEDPI_LISTENER_LOOPBACK_ONLY=PASS',
        'CIADPI_OWNER_MATCH=PASS',
        'CIADPI_TOKEN_NON_ELEVATED=PASS',
        'EXTERNAL_SOCKS5_HTTPS=PASS',
        'NORMAL_STOP_CLEANUP=PASS',
        'RUNTIME_RESIDUE_AFTER_NORMAL_STOP=NONE',
        'JOB_OBJECT_CRASH_CLEANUP=PASS',
        'RUNTIME_RESIDUE_AFTER_OWNER_CRASH=NONE',
        'SUPPORT_BUNDLE_ENGINE_SHA256=PASS',
        'SUPPORT_BUNDLE_USER_MODE_SCOPE=PASS',
        'NETPILOT_SERVICE_PRESENT_AFTER=False',
        'SYSTEM_PROXY_UNCHANGED=PASS',
        'WINDIVERT_UNCHANGED=PASS',
        'SYSTEM_PROXY_MUTATION=NOT_REQUESTED',
        'WINDIVERT=NOT_REQUESTED',
        'FINAL_RUNTIME_RESIDUE=NONE',
        'FINAL_SERVICE_BOUNDARY=PASS',
        'FINAL_SYSTEM_PROXY_UNCHANGED=PASS',
        'FINAL_WINDIVERT_UNCHANGED=PASS',
        'PRODUCTION_ADMISSION=BLOCK',
        'RESULT=R42_12_4_NO_ADMIN_BYEDPI_PASS_BLOCK'
    )
    foreach ($marker in $requiredMarkers) {
        Require-Marker -Text $validationText -Marker $marker
    }

    $combined = Get-ChildItem `
        -LiteralPath $publicEvidence `
        -Filter 'NetPilot-ByeDPI-NoAdmin-Validation-*-UTC.zip' `
        -File |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1
    if ($null -eq $combined) {
        throw 'Combined no-admin evidence ZIP is missing.'
    }

    if (Get-Service -Name 'NetPilot.Service' -ErrorAction SilentlyContinue) {
        throw 'NetPilot.Service exists after no-admin validation.'
    }
    if (@(Get-Process -Name ciadpi -ErrorAction SilentlyContinue).Count -ne 0) {
        throw 'ciadpi residue exists after no-admin validation.'
    }

    $validationSucceeded = $true
}
catch {
    $failure = $_
    try {
        $deep = Get-DeepestException -Exception $_.Exception
        "HOSTED_FAILURE_TYPE=$($_.Exception.GetType().FullName)" | Add-Content (Join-Path $HostedEvidenceDir 'boundary.txt')
        "HOSTED_FAILURE_HRESULT=0x$($_.Exception.HResult.ToString('X8'))" | Add-Content (Join-Path $HostedEvidenceDir 'boundary.txt')
        "HOSTED_DEEP_FAILURE_TYPE=$($deep.GetType().FullName)" | Add-Content (Join-Path $HostedEvidenceDir 'boundary.txt')
        "HOSTED_DEEP_FAILURE_HRESULT=0x$($deep.HResult.ToString('X8'))" | Add-Content (Join-Path $HostedEvidenceDir 'boundary.txt')
        if ($deep -is [System.ComponentModel.Win32Exception]) {
            "HOSTED_WIN32_ERROR=$($deep.NativeErrorCode)" | Add-Content (Join-Path $HostedEvidenceDir 'boundary.txt')
        }
    }
    catch {
    }
}
finally {
    Copy-EvidenceBestEffort

    if ($validationSucceeded) {
        'HOSTED_STANDARD_USER_VALIDATION=PASS' | Add-Content (Join-Path $HostedEvidenceDir 'boundary.txt')
        'HOSTED_CIADPI_RESIDUE=NONE' | Add-Content (Join-Path $HostedEvidenceDir 'boundary.txt')
        'HOSTED_SERVICE_BOUNDARY=PASS' | Add-Content (Join-Path $HostedEvidenceDir 'boundary.txt')
    }

    if ($null -ne $nativeChild) {
        try { $nativeChild.Dispose() } catch { }
    }

    Get-Process -Name ciadpi -ErrorAction SilentlyContinue |
        Stop-Process -Force -ErrorAction SilentlyContinue

    $user = Get-LocalUser -Name $UserName -ErrorAction SilentlyContinue
    if ($null -ne $user) {
        Remove-LocalUser -Name $UserName -ErrorAction SilentlyContinue
    }

    if ($null -ne $createdUserSid) {
        try {
            Get-CimInstance Win32_UserProfile -ErrorAction SilentlyContinue |
                Where-Object { $_.SID -eq $createdUserSid -and -not $_.Loaded } |
                Remove-CimInstance -ErrorAction SilentlyContinue
        }
        catch {
        }
    }

    Remove-Item -LiteralPath $publicPack -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $publicEvidence -Recurse -Force -ErrorAction SilentlyContinue
}

if (-not $validationSucceeded) {
    if ($null -ne $failure) {
        throw $failure
    }
    throw 'R42.12.4 hosted standard-user validation did not complete.'
}
