using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using NetPilot.Platform.Windows;

namespace NetPilot.ByeDpiUserModeValidation;

internal static class Program
{
    private const string CandidateSha256 = ByeDpiUserModeOptions.ReviewedSha256;
    private const long CandidateSize = ByeDpiUserModeOptions.ReviewedSize;
    private const string LicenseSha256 = "18c854c6c25dffefe4b522cf1bdf5d15278a25ca785948258bddb94393f575eb";
    private const long LicenseSize = 1063;
    private static readonly Uri ExternalTarget = new("https://example.com/");

    public static async Task<int> Main(string[] args)
    {
        if (args.Length > 0 && string.Equals(args[0], "--job-owner-helper", StringComparison.Ordinal))
        {
            return await RunJobOwnerHelperAsync(args.Skip(1).ToArray());
        }

        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("R42.12.4 validation is Windows-only.");
            return 2;
        }

        var options = ParseOptions(args);
        Directory.CreateDirectory(options.EvidenceDirectory);
        var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        var evidencePath = Path.Combine(options.EvidenceDirectory, $"ByeDPI-NoAdmin-Validation-{stamp}-UTC.txt");
        string? diagnosticsZip = null;
        string? proxyBefore = null;
        string? winDivertBefore = null;
        bool? serviceBefore = null;
        var overallPass = false;

        await using var evidence = new EvidenceWriter(evidencePath);
        await evidence.WriteAsync("R42_12_4_VALIDATION_STARTED=True");
        await evidence.WriteAsync("VALIDATION_SCOPE=STANDARD_USER_BYEDPI_ONLY");
        await evidence.WriteAsync("RELEASE_PUBLISHED=False");
        await evidence.WriteAsync("PRODUCTION_ADMISSION=BLOCK");

        try
        {
            RequireWindowsX64();
            RequireNonElevatedToken();
            RequireInstallRootUnderCurrentUser(options.InstallRoot);
            await evidence.WriteAsync("WINDOWS_X64=PASS");
            await evidence.WriteAsync("CURRENT_TOKEN_NON_ELEVATED=PASS");
            await evidence.WriteAsync("INSTALL_SCOPE=CURRENT_USER_LOCALAPPDATA");

            var packageRoot = Path.Combine(
                options.InstallRoot,
                "Engines",
                "Managed",
                "byedpi",
                ByeDpiUserModeOptions.ReviewedVersion,
                "x64");
            var candidate = Path.Combine(packageRoot, "bin", "ciadpi.exe");
            var license = Path.Combine(packageRoot, "LICENSE");
            var app = Path.Combine(options.InstallRoot, "App", "NetPilot.exe");

            RequireExactRegularFile(candidate, CandidateSize, CandidateSha256, "ByeDPI candidate");
            RequireExactRegularFile(license, LicenseSize, LicenseSha256, "ByeDPI license");
            RequireRegularFile(app, "Installed NetPilot.exe is missing.");
            await evidence.WriteAsync($"CANDIDATE_SHA256={CandidateSha256}");
            await evidence.WriteAsync("INSTALLED_EXACT_CANDIDATE=PASS");

            if (GetCiadpiProcesses().Count != 0)
            {
                throw new InvalidOperationException("A ciadpi process already exists before standard-user validation.");
            }

            serviceBefore = ServiceRegistrationExists();
            if (serviceBefore.Value)
            {
                throw new InvalidOperationException("NetPilot.Service is registered; the no-admin validation requires an isolated user-mode installation.");
            }
            await evidence.WriteAsync("NETPILOT_SERVICE_PRESENT_BEFORE=False");

            proxyBefore = GetCurrentUserProxyFingerprint();
            winDivertBefore = GetWinDivertFingerprint();

            var runtimeOptions = new ByeDpiUserModeOptions
            {
                PackageRoot = packageRoot,
                ExecutablePath = candidate
            };

            int normalPort;
            using (var runtime = new WindowsByeDpiStrategyRuntime(runtimeOptions))
            {
                await runtime.ActivateAsync("byedpi-windows-recommended");
                var state = await runtime.GetStateAsync();
                if (!state.IsRunning
                    || !string.Equals(state.StrategyId, "byedpi-windows-recommended", StringComparison.Ordinal)
                    || runtime.ProxyPort is null)
                {
                    throw new InvalidOperationException("ByeDPI user-mode runtime did not reach the expected running state.");
                }

                normalPort = runtime.ProxyPort.Value;
                RequireLoopbackOnlyListener(normalPort);
                using (var candidateProcess = RequireSingleCiadpiProcess())
                {
                    RequireOwnedByCurrentNonElevatedUser(candidateProcess);
                }

                await evidence.WriteAsync("BYEDPI_USER_MODE_START=PASS");
                await evidence.WriteAsync("BYEDPI_LISTENER_LOOPBACK_ONLY=PASS");
                await evidence.WriteAsync("CIADPI_OWNER_MATCH=PASS");
                await evidence.WriteAsync("CIADPI_TOKEN_NON_ELEVATED=PASS");

                var direct = await ProbeHttpsAsync(ExternalTarget, proxyPort: null);
                var proxied = await ProbeHttpsAsync(ExternalTarget, normalPort);
                await evidence.WriteAsync($"EXTERNAL_DIRECT_SUCCESS={direct.Success}");
                await evidence.WriteAsync($"EXTERNAL_DIRECT_STATUS={direct.StatusCode?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "NONE"}");
                await evidence.WriteAsync($"EXTERNAL_DIRECT_ERROR={direct.ErrorType ?? "NONE"}");
                await evidence.WriteAsync($"EXTERNAL_BYEDPI_SUCCESS={proxied.Success}");
                await evidence.WriteAsync($"EXTERNAL_BYEDPI_STATUS={proxied.StatusCode?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "NONE"}");
                await evidence.WriteAsync($"EXTERNAL_BYEDPI_ERROR={proxied.ErrorType ?? "NONE"}");

                if (!proxied.Success)
                {
                    throw new InvalidOperationException("External HTTPS request through user-mode ByeDPI SOCKS5 failed.");
                }
                await evidence.WriteAsync("EXTERNAL_SOCKS5_HTTPS=PASS");

                await runtime.StopAsync();
            }

            RequireNoCiadpiProcesses();
            await WaitForNoListenerAsync(normalPort, TimeSpan.FromSeconds(5));
            await evidence.WriteAsync("NORMAL_STOP_CLEANUP=PASS");
            await evidence.WriteAsync("RUNTIME_RESIDUE_AFTER_NORMAL_STOP=NONE");

            await ValidateJobObjectCrashCleanupAsync(options, evidence);

            diagnosticsZip = await CollectDiagnosticsAsync(app);
            RequireDiagnosticsCandidateHash(diagnosticsZip);
            await evidence.WriteAsync("SUPPORT_BUNDLE_ENGINE_SHA256=PASS");
            await evidence.WriteAsync("SUPPORT_BUNDLE_USER_MODE_SCOPE=PASS");

            if (ServiceRegistrationExists())
            {
                throw new InvalidOperationException("NetPilot.Service appeared during user-mode validation.");
            }
            await evidence.WriteAsync("NETPILOT_SERVICE_PRESENT_AFTER=False");

            var proxyAfter = GetCurrentUserProxyFingerprint();
            var winDivertAfter = GetWinDivertFingerprint();
            if (!string.Equals(proxyBefore, proxyAfter, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Current-user system proxy fingerprint changed during validation.");
            }
            if (!string.Equals(winDivertBefore, winDivertAfter, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("WinDivert service fingerprint changed during validation.");
            }

            await evidence.WriteAsync("SYSTEM_PROXY_UNCHANGED=PASS");
            await evidence.WriteAsync("WINDIVERT_UNCHANGED=PASS");
            await evidence.WriteAsync("SYSTEM_PROXY_MUTATION=NOT_REQUESTED");
            await evidence.WriteAsync("WINDIVERT=NOT_REQUESTED");
            overallPass = true;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            overallPass = false;
            await evidence.WriteAsync($"VALIDATION_FAILURE_TYPE={SafeTypeName(exception)}");
            await evidence.WriteAsync($"VALIDATION_FAILURE_HRESULT=0x{exception.HResult:X8}");
        }
        finally
        {
            try
            {
                RequireNoCiadpiProcesses();
                await evidence.WriteAsync("FINAL_RUNTIME_RESIDUE=NONE");
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                overallPass = false;
                await evidence.WriteAsync($"FINAL_RUNTIME_RESIDUE_FAILURE={SafeTypeName(exception)}");
            }

            if (serviceBefore is not null)
            {
                try
                {
                    var serviceAfter = ServiceRegistrationExists();
                    if (serviceAfter != serviceBefore.Value)
                    {
                        overallPass = false;
                        await evidence.WriteAsync("FINAL_SERVICE_BOUNDARY=FAIL");
                    }
                    else
                    {
                        await evidence.WriteAsync("FINAL_SERVICE_BOUNDARY=PASS");
                    }
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    overallPass = false;
                    await evidence.WriteAsync($"FINAL_SERVICE_BOUNDARY_FAILURE={SafeTypeName(exception)}");
                }
            }

            if (proxyBefore is not null)
            {
                try
                {
                    var proxyAfter = GetCurrentUserProxyFingerprint();
                    var unchanged = string.Equals(proxyBefore, proxyAfter, StringComparison.Ordinal);
                    await evidence.WriteAsync($"FINAL_SYSTEM_PROXY_UNCHANGED={(unchanged ? "PASS" : "FAIL")}");
                    overallPass &= unchanged;
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    overallPass = false;
                    await evidence.WriteAsync($"FINAL_PROXY_BOUNDARY_FAILURE={SafeTypeName(exception)}");
                }
            }

            if (winDivertBefore is not null)
            {
                try
                {
                    var winDivertAfter = GetWinDivertFingerprint();
                    var unchanged = string.Equals(winDivertBefore, winDivertAfter, StringComparison.Ordinal);
                    await evidence.WriteAsync($"FINAL_WINDIVERT_UNCHANGED={(unchanged ? "PASS" : "FAIL")}");
                    overallPass &= unchanged;
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    overallPass = false;
                    await evidence.WriteAsync($"FINAL_WINDIVERT_BOUNDARY_FAILURE={SafeTypeName(exception)}");
                }
            }

            await evidence.WriteAsync("PRODUCTION_ADMISSION=BLOCK");
            await evidence.WriteAsync(
                overallPass
                    ? "RESULT=R42_12_4_NO_ADMIN_BYEDPI_PASS_BLOCK"
                    : "RESULT=R42_12_4_NO_ADMIN_BYEDPI_FAIL_BLOCK");
        }

        await evidence.DisposeAsync();
        var bundle = CreateEvidenceBundle(options.EvidenceDirectory, stamp, evidencePath, diagnosticsZip);
        Console.WriteLine(overallPass
            ? "R42.12.4 no-admin ByeDPI validation PASSED."
            : "R42.12.4 no-admin ByeDPI validation FAILED.");
        Console.WriteLine($"Evidence bundle: {bundle}");
        Console.WriteLine("Production admission remains BLOCKED.");
        return overallPass ? 0 : 1;
    }

    private static async Task<int> RunJobOwnerHelperAsync(string[] args)
    {
        try
        {
            var options = ParseOptions(args);
            var readyPath = options.ReadyPath
                ?? throw new ArgumentException("--ready-path is required in job-owner helper mode.");
            RequireWindowsX64();
            RequireNonElevatedToken();
            RequireInstallRootUnderCurrentUser(options.InstallRoot);

            var packageRoot = Path.Combine(
                options.InstallRoot,
                "Engines",
                "Managed",
                "byedpi",
                ByeDpiUserModeOptions.ReviewedVersion,
                "x64");
            var candidate = Path.Combine(packageRoot, "bin", "ciadpi.exe");
            RequireExactRegularFile(candidate, CandidateSize, CandidateSha256, "ByeDPI candidate");
            if (GetCiadpiProcesses().Count != 0)
            {
                throw new InvalidOperationException("Helper refuses pre-existing ciadpi processes.");
            }

            var runtime = new WindowsByeDpiStrategyRuntime(new ByeDpiUserModeOptions
            {
                PackageRoot = packageRoot,
                ExecutablePath = candidate
            });
            await runtime.ActivateAsync("byedpi-windows-recommended");
            var port = runtime.ProxyPort
                ?? throw new InvalidOperationException("Helper runtime did not expose its SOCKS5 port.");
            using var candidateProcess = RequireSingleCiadpiProcess();
            RequireOwnedByCurrentNonElevatedUser(candidateProcess);

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(readyPath))!);
            await File.WriteAllLinesAsync(
                readyPath,
                new[]
                {
                    "HELPER_NON_ELEVATED=PASS",
                    $"CIADPI_PID={candidateProcess.Id}",
                    $"PROXY_PORT={port}",
                    "READY=PASS"
                },
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            await Task.Delay(Timeout.InfiniteTimeSpan);
            return 0;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Console.Error.WriteLine(SafeTypeName(exception));
            return 3;
        }
    }

    private static ValidationOptions ParseOptions(string[] args)
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var installRoot = Path.Combine(localAppData, "Programs", "NetPilot");
        var evidenceDirectory = Path.Combine(localAppData, "NetPilot", "Support");
        string? readyPath = null;

        for (var index = 0; index < args.Length; index++)
        {
            if (index + 1 >= args.Length)
            {
                throw new ArgumentException($"Missing value for {args[index]}.");
            }

            var value = args[++index];
            switch (args[index - 1])
            {
                case "--install-root":
                    installRoot = value;
                    break;
                case "--evidence-dir":
                    evidenceDirectory = value;
                    break;
                case "--ready-path":
                    readyPath = value;
                    break;
                default:
                    throw new ArgumentException("Only --install-root, --evidence-dir and --ready-path are supported.");
            }
        }

        return new ValidationOptions(
            Path.GetFullPath(installRoot),
            Path.GetFullPath(evidenceDirectory),
            readyPath is null ? null : Path.GetFullPath(readyPath));
    }

    private static void RequireWindowsX64()
    {
        if (!Environment.Is64BitOperatingSystem || !Environment.Is64BitProcess)
        {
            throw new PlatformNotSupportedException("Windows x64 process on Windows x64 is required.");
        }
    }

    private static void RequireNonElevatedToken()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        if (principal.IsInRole(WindowsBuiltInRole.Administrator))
        {
            throw new UnauthorizedAccessException("Administrator token is forbidden for R42.12.4 validation.");
        }

        if (IsTokenElevated(identity.AccessToken))
        {
            throw new UnauthorizedAccessException("Elevated token is forbidden for R42.12.4 validation.");
        }
    }

    private static void RequireInstallRootUnderCurrentUser(string installRoot)
    {
        var localAppData = Path.GetFullPath(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData))
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var fullInstallRoot = Path.GetFullPath(installRoot);
        if (!fullInstallRoot.StartsWith(localAppData, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("User-mode NetPilot must be installed beneath the current user's LocalAppData.");
        }
    }

    private static void RequireExactRegularFile(string path, long expectedSize, string expectedSha256, string label)
    {
        RequireRegularFile(path, $"Missing {label}.");
        var file = new FileInfo(path);
        if (file.Length != expectedSize)
        {
            throw new InvalidDataException($"Unexpected {label} size.");
        }
        using var stream = file.OpenRead();
        var actual = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        if (!string.Equals(actual, expectedSha256, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Unexpected {label} SHA-256.");
        }
    }

    private static void RequireRegularFile(string path, string message)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(message);
        }
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("Validation refuses reparse-point files.");
        }
    }

    private static Process RequireSingleCiadpiProcess()
    {
        var processes = GetCiadpiProcesses();
        if (processes.Count != 1)
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
            throw new InvalidOperationException($"Expected exactly one owned ciadpi process, found {processes.Count}.");
        }
        return processes[0];
    }

    private static void RequireNoCiadpiProcesses()
    {
        var processes = GetCiadpiProcesses();
        try
        {
            if (processes.Count != 0)
            {
                throw new InvalidOperationException("ciadpi process residue remained after validation.");
            }
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }
    }

    private static List<Process> GetCiadpiProcesses() =>
        Process.GetProcessesByName("ciadpi").ToList();

    private static void RequireOwnedByCurrentNonElevatedUser(Process process)
    {
        using var current = WindowsIdentity.GetCurrent();
        if (!NativeMethods.OpenProcessToken(process.Handle, NativeMethods.TokenQuery, out var token))
        {
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Could not query ciadpi token.");
        }

        using (token)
        {
            using var candidateIdentity = new WindowsIdentity(token.DangerousGetHandle());
            if (current.User is null
                || candidateIdentity.User is null
                || !string.Equals(current.User.Value, candidateIdentity.User.Value, StringComparison.Ordinal))
            {
                throw new UnauthorizedAccessException("ciadpi process owner does not match the current standard user.");
            }
            if (IsTokenElevated(token))
            {
                throw new UnauthorizedAccessException("ciadpi unexpectedly has an elevated token.");
            }
        }
    }

    private static bool IsTokenElevated(SafeAccessTokenHandle token)
    {
        var size = Marshal.SizeOf<TokenElevation>();
        if (!NativeMethods.GetTokenInformation(
                token,
                TokenInformationClass.TokenElevation,
                out var elevation,
                size,
                out _))
        {
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Could not inspect token elevation.");
        }
        return elevation.TokenIsElevated != 0;
    }

    private static void RequireLoopbackOnlyListener(int port)
    {
        var listeners = IPGlobalProperties.GetIPGlobalProperties()
            .GetActiveTcpListeners()
            .Where(endpoint => endpoint.Port == port)
            .ToArray();
        if (listeners.Length == 0 || listeners.Any(endpoint => !IPAddress.IsLoopback(endpoint.Address)))
        {
            throw new InvalidOperationException("ByeDPI listener is missing or escaped the loopback boundary.");
        }
    }

    private static async Task ValidateJobObjectCrashCleanupAsync(
        ValidationOptions options,
        EvidenceWriter evidence)
    {
        var executable = Environment.ProcessPath
            ?? throw new InvalidOperationException("Validator executable path is unavailable.");
        var readyPath = Path.Combine(options.EvidenceDirectory, ".r42-12-4-job-ready-" + Guid.NewGuid().ToString("N") + ".txt");
        Process? helper = null;
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("--job-owner-helper");
            startInfo.ArgumentList.Add("--install-root");
            startInfo.ArgumentList.Add(options.InstallRoot);
            startInfo.ArgumentList.Add("--evidence-dir");
            startInfo.ArgumentList.Add(options.EvidenceDirectory);
            startInfo.ArgumentList.Add("--ready-path");
            startInfo.ArgumentList.Add(readyPath);

            helper = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Could not start Job Object crash helper.");

            var ready = await WaitForReadyFileAsync(readyPath, helper, TimeSpan.FromSeconds(20));
            if (!string.Equals(ready.GetValueOrDefault("HELPER_NON_ELEVATED"), "PASS", StringComparison.Ordinal)
                || !int.TryParse(ready.GetValueOrDefault("CIADPI_PID"), out var candidatePid)
                || !int.TryParse(ready.GetValueOrDefault("PROXY_PORT"), out var port)
                || !string.Equals(ready.GetValueOrDefault("READY"), "PASS", StringComparison.Ordinal))
            {
                throw new InvalidDataException("Job Object crash helper readiness evidence is invalid.");
            }

            helper.Kill(entireProcessTree: false);
            await helper.WaitForExitAsync();
            helper.Dispose();
            helper = null;

            await WaitForProcessExitAsync(candidatePid, TimeSpan.FromSeconds(10));
            await WaitForNoListenerAsync(port, TimeSpan.FromSeconds(10));
            RequireNoCiadpiProcesses();
            await evidence.WriteAsync("JOB_OBJECT_CRASH_CLEANUP=PASS");
            await evidence.WriteAsync("RUNTIME_RESIDUE_AFTER_OWNER_CRASH=NONE");
        }
        finally
        {
            if (helper is not null)
            {
                try
                {
                    if (!helper.HasExited)
                    {
                        helper.Kill(entireProcessTree: false);
                        await helper.WaitForExitAsync();
                    }
                }
                catch (InvalidOperationException)
                {
                }
                finally
                {
                    helper.Dispose();
                }
            }
            try
            {
                if (File.Exists(readyPath))
                {
                    File.Delete(readyPath);
                }
            }
            catch (IOException)
            {
            }
        }
    }

    private static async Task<Dictionary<string, string>> WaitForReadyFileAsync(
        string path,
        Process helper,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (helper.HasExited)
            {
                throw new InvalidOperationException($"Job Object crash helper exited early with code {helper.ExitCode}.");
            }
            if (File.Exists(path))
            {
                var lines = await File.ReadAllLinesAsync(path);
                return lines
                    .Select(line => line.Split('=', 2))
                    .Where(parts => parts.Length == 2)
                    .ToDictionary(parts => parts[0], parts => parts[1], StringComparer.Ordinal);
            }
            await Task.Delay(100);
        }
        throw new TimeoutException("Job Object crash helper did not become ready.");
    }

    private static async Task WaitForProcessExitAsync(int processId, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var process = Process.GetProcessById(processId);
                if (process.HasExited)
                {
                    return;
                }
            }
            catch (ArgumentException)
            {
                return;
            }
            await Task.Delay(100);
        }
        throw new TimeoutException("ciadpi remained alive after Job Object owner termination.");
    }

    private static async Task WaitForNoListenerAsync(int port, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (!IPGlobalProperties.GetIPGlobalProperties()
                    .GetActiveTcpListeners()
                    .Any(endpoint => endpoint.Port == port))
            {
                return;
            }
            await Task.Delay(100);
        }
        throw new TimeoutException("ByeDPI loopback listener remained after cleanup.");
    }

    private static async Task<HttpProbe> ProbeHttpsAsync(Uri target, int? proxyPort)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var handler = new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                UseCookies = false,
                UseProxy = proxyPort is not null,
                Proxy = proxyPort is null ? null : new WebProxy(new Uri($"socks5://127.0.0.1:{proxyPort.Value}")),
                ConnectTimeout = TimeSpan.FromSeconds(15)
            };
            using var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
            using var request = new HttpRequestMessage(HttpMethod.Get, target);
            request.Headers.UserAgent.ParseAdd("NetPilot-R42.12.4-NoAdmin-Validation/1.0");
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            stopwatch.Stop();
            return new HttpProbe(true, (int)response.StatusCode, stopwatch.ElapsedMilliseconds, null);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            stopwatch.Stop();
            return new HttpProbe(false, null, stopwatch.ElapsedMilliseconds, SafeTypeName(exception));
        }
    }

    private static bool ServiceRegistrationExists()
    {
        using var key = Registry.LocalMachine.OpenSubKey(
            @"SYSTEM\CurrentControlSet\Services\NetPilot.Service",
            writable: false);
        return key is not null;
    }

    private static string GetCurrentUserProxyFingerprint()
    {
        using var internet = Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Internet Settings",
            writable: false);
        var proxyEnable = internet?.GetValue("ProxyEnable")?.ToString() ?? "<null>";
        var proxyServer = internet?.GetValue("ProxyServer")?.ToString() ?? "<null>";
        var autoConfig = internet?.GetValue("AutoConfigURL")?.ToString() ?? "<null>";
        return Fingerprint($"{proxyEnable}\n{proxyServer}\n{autoConfig}");
    }

    private static string GetWinDivertFingerprint()
    {
        using var services = Registry.LocalMachine.OpenSubKey(
            @"SYSTEM\CurrentControlSet\Services",
            writable: false);
        var relevant = services?.GetSubKeyNames()
            .Where(name => name.Contains("WinDivert", StringComparison.OrdinalIgnoreCase))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];
        return Fingerprint(string.Join("\n", relevant));
    }

    private static string Fingerprint(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static async Task<string> CollectDiagnosticsAsync(string installedApp)
    {
        var supportRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NetPilot",
            "Support");
        Directory.CreateDirectory(supportRoot);
        var started = DateTime.UtcNow.AddSeconds(-2);

        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = installedApp,
            UseShellExecute = false,
            CreateNoWindow = true,
            ArgumentList = { "--collect-diagnostics" }
        }) ?? throw new InvalidOperationException("Could not start NetPilot diagnostics mode.");

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await process.WaitForExitAsync(timeoutCts.Token);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException("NetPilot diagnostics mode returned a non-zero exit code.");
        }

        var bundle = new DirectoryInfo(supportRoot)
            .EnumerateFiles("NetPilot-Diagnostics-*-UTC.zip", SearchOption.TopDirectoryOnly)
            .Where(file => file.LastWriteTimeUtc >= started)
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .FirstOrDefault();
        return bundle?.FullName
            ?? throw new FileNotFoundException("NetPilot diagnostics ZIP was not created.");
    }

    private static void RequireDiagnosticsCandidateHash(string diagnosticsZip)
    {
        using var archive = ZipFile.OpenRead(diagnosticsZip);
        var entry = archive.GetEntry("engine-status.json")
            ?? throw new InvalidDataException("Support bundle is missing engine-status.json.");
        using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
        var content = reader.ReadToEnd();
        if (!content.Contains(CandidateSha256, StringComparison.Ordinal)
            || !content.Contains("AppInstall", StringComparison.Ordinal))
        {
            throw new InvalidDataException("Support bundle does not identify the exact user-mode ByeDPI candidate.");
        }
    }

    private static string CreateEvidenceBundle(
        string evidenceDirectory,
        string stamp,
        string evidencePath,
        string? diagnosticsZip)
    {
        var staging = Path.Combine(evidenceDirectory, ".r42-12-4-bundle-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            File.Copy(evidencePath, Path.Combine(staging, Path.GetFileName(evidencePath)));
            if (diagnosticsZip is not null && File.Exists(diagnosticsZip))
            {
                File.Copy(diagnosticsZip, Path.Combine(staging, Path.GetFileName(diagnosticsZip)));
            }
            var output = Path.Combine(evidenceDirectory, $"NetPilot-ByeDPI-NoAdmin-Validation-{stamp}-UTC.zip");
            if (File.Exists(output))
            {
                File.Delete(output);
            }
            ZipFile.CreateFromDirectory(staging, output, CompressionLevel.Optimal, includeBaseDirectory: false);
            return output;
        }
        finally
        {
            try
            {
                Directory.Delete(staging, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static string SafeTypeName(Exception exception) =>
        exception.GetType().FullName?.Replace(' ', '_') ?? "UnknownException";

    private sealed record ValidationOptions(
        string InstallRoot,
        string EvidenceDirectory,
        string? ReadyPath);

    private sealed record HttpProbe(
        bool Success,
        int? StatusCode,
        long ElapsedMilliseconds,
        string? ErrorType);

    private sealed class EvidenceWriter : IAsyncDisposable
    {
        private readonly StreamWriter _writer;
        private bool _disposed;

        public EvidenceWriter(string path)
        {
            _writer = new StreamWriter(
                new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
            {
                AutoFlush = true
            };
        }

        public Task WriteAsync(string line)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _writer.WriteLineAsync(line);
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            await _writer.DisposeAsync();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TokenElevation
    {
        public uint TokenIsElevated;
    }

    private enum TokenInformationClass
    {
        TokenElevation = 20
    }

    private static class NativeMethods
    {
        public const uint TokenQuery = 0x0008;

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool OpenProcessToken(
            IntPtr processHandle,
            uint desiredAccess,
            out SafeAccessTokenHandle tokenHandle);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetTokenInformation(
            SafeAccessTokenHandle tokenHandle,
            TokenInformationClass tokenInformationClass,
            out TokenElevation tokenInformation,
            int tokenInformationLength,
            out int returnLength);
    }
}
