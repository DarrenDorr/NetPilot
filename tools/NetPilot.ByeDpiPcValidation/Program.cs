using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32;

namespace NetPilot.ByeDpiPcValidation;

internal static class Program
{
    private const string CandidateSha256 = "866e99c3c7690326cc6e365fe02980f06ae2b394c045555674fb95db52b0dbdd";
    private const long CandidateSize = 495685;
    private const string LicenseSha256 = "18c854c6c25dffefe4b522cf1bdf5d15278a25ca785948258bddb94393f575eb";
    private const long LicenseSize = 1063;
    private const string PackageVersion = "0.17.3-netpilot-r34-lab";
    private const string DefaultTarget = "https://example.com/";

    public static async Task<int> Main(string[] args)
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("R42.12.3 validation is Windows-only.");
            return 2;
        }

        var correlationId = Guid.NewGuid().ToString("N");
        var supportRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NetPilot",
            "Support");
        Directory.CreateDirectory(supportRoot);

        var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        var evidencePath = Path.Combine(supportRoot, $"ByeDPI-PrePublish-Validation-{stamp}-UTC.txt");
        string? diagnosticsZip = null;
        var overallPass = false;
        var stagingOwned = false;
        var cleanupPass = true;
        var proxyUnchanged = false;
        var winDivertUnchanged = false;

        var productRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "NetPilot");
        var versionRoot = Path.Combine(productRoot, "Engines", "Managed", "byedpi", PackageVersion);
        var architectureRoot = Path.Combine(versionRoot, "x64");
        var binRoot = Path.Combine(architectureRoot, "bin");
        var stagedCandidate = Path.Combine(binRoot, "ciadpi.exe");
        var ownershipMarker = Path.Combine(productRoot, ".r42-12-3-validation-owned");
        var installedApp = Path.Combine(productRoot, "App", "NetPilot.exe");

        await using var evidence = new EvidenceWriter(evidencePath);
        await evidence.WriteAsync("R42_12_3_VALIDATION_STARTED=True");
        await evidence.WriteAsync($"CORRELATION_ID={correlationId}");
        await evidence.WriteAsync("VALIDATION_SCOPE=PREPUBLISH_PC_ONLY");
        await evidence.WriteAsync("RELEASE_PUBLISHED=False");
        await evidence.WriteAsync("PRODUCTION_ADMISSION=BLOCK");

        string? proxyBefore = null;
        string? winDivertBefore = null;

        try
        {
            RequireAdministrator();
            RequireWindowsX64();
            await evidence.WriteAsync("WINDOWS_X64=PASS");
            await evidence.WriteAsync("RUNNER_ADMIN=PASS");

            var target = ParseTarget(args, out var customTarget);
            await evidence.WriteAsync($"EXTERNAL_TARGET_MODE={(customTarget ? "CUSTOM_HTTPS" : "DEFAULT_HTTPS")}");

            var packRoot = AppContext.BaseDirectory;
            var sourceCandidate = Path.Combine(packRoot, "candidate", "bin", "ciadpi.exe");
            var sourceLicense = Path.Combine(packRoot, "candidate", "LICENSE");
            var harness = Path.Combine(packRoot, "validator", "NetPilot.ByeDpiR34Lab.exe");

            RequireExactFile(sourceCandidate, CandidateSize, CandidateSha256, "candidate");
            RequireExactFile(sourceLicense, LicenseSize, LicenseSha256, "license");
            RequireRegularFile(harness, "Pinned R34 validation harness is missing.");
            await evidence.WriteAsync($"CANDIDATE_SHA256={CandidateSha256}");
            await evidence.WriteAsync("PACK_BYTES_VERIFIED=PASS");

            if (!File.Exists(installedApp))
            {
                throw new InvalidOperationException(
                    "NetPilot is not installed. Run the setup EXE from this validation pack first.");
            }

            if (Directory.Exists(versionRoot))
            {
                throw new InvalidOperationException(
                    "The R42.12.3 validation engine directory already exists. Refusing to overwrite it.");
            }

            if (File.Exists(ownershipMarker))
            {
                throw new InvalidOperationException(
                    "A stale R42.12.3 ownership marker exists. Refusing to alter the installation.");
            }

            if (GetCiadpiProcesses().Count != 0)
            {
                throw new InvalidOperationException("A ciadpi process is already running before validation.");
            }

            proxyBefore = await GetProxyFingerprintAsync();
            winDivertBefore = await GetWinDivertFingerprintAsync();

            Directory.CreateDirectory(binRoot);
            await File.WriteAllTextAsync(ownershipMarker, correlationId, Encoding.UTF8);
            stagingOwned = true;
            File.Copy(sourceCandidate, stagedCandidate, overwrite: false);
            ProtectValidationTree(versionRoot);
            RequireExactFile(stagedCandidate, CandidateSize, CandidateSha256, "staged candidate");
            await evidence.WriteAsync("PROTECTED_CANDIDATE_STAGING=PASS");

            var harnessResult = await RunCaptureAsync(
                harness,
                Array.Empty<string>(),
                TimeSpan.FromSeconds(90));
            await evidence.WriteAsync($"R34_EXIT_CODE={harnessResult.ExitCode}");
            foreach (var line in SplitEvidenceLines(harnessResult.StandardOutput))
            {
                await evidence.WriteAsync($"R34|{line}");
            }
            foreach (var line in SplitEvidenceLines(harnessResult.StandardError))
            {
                await evidence.WriteAsync($"R34_ERR|{line}");
            }

            RequireHarnessPass(harnessResult);
            await evidence.WriteAsync("SYNTHETIC_R34_RUNTIME=PASS");

            var external = await RunExternalProbeAsync(stagedCandidate, target);
            await evidence.WriteAsync($"EXTERNAL_DIRECT_SUCCESS={external.Direct.Success}");
            await evidence.WriteAsync($"EXTERNAL_DIRECT_STATUS={external.Direct.StatusCode?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "NONE"}");
            await evidence.WriteAsync($"EXTERNAL_DIRECT_MS={external.Direct.ElapsedMilliseconds}");
            await evidence.WriteAsync($"EXTERNAL_DIRECT_ERROR={external.Direct.ErrorType ?? "NONE"}");
            await evidence.WriteAsync($"EXTERNAL_BYEDPI_SUCCESS={external.ViaByeDpi.Success}");
            await evidence.WriteAsync($"EXTERNAL_BYEDPI_STATUS={external.ViaByeDpi.StatusCode?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "NONE"}");
            await evidence.WriteAsync($"EXTERNAL_BYEDPI_MS={external.ViaByeDpi.ElapsedMilliseconds}");
            await evidence.WriteAsync($"EXTERNAL_BYEDPI_ERROR={external.ViaByeDpi.ErrorType ?? "NONE"}");

            if (!external.ViaByeDpi.Success)
            {
                throw new InvalidOperationException("External HTTPS request through ByeDPI SOCKS5 did not succeed.");
            }

            await evidence.WriteAsync("EXTERNAL_SOCKS5_HTTPS=PASS");

            diagnosticsZip = await CollectNetPilotDiagnosticsAsync(installedApp, supportRoot);
            RequireSupportBundleEngineHash(diagnosticsZip);
            await evidence.WriteAsync("SUPPORT_BUNDLE_ENGINE_SHA256=PASS");
            await evidence.WriteAsync("SUPPORT_BUNDLE_CREATED=PASS");

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
            if (diagnosticsZip is null && File.Exists(installedApp))
            {
                try
                {
                    diagnosticsZip = await CollectNetPilotDiagnosticsAsync(installedApp, supportRoot);
                    if (File.Exists(stagedCandidate))
                    {
                        RequireSupportBundleEngineHash(diagnosticsZip);
                        await evidence.WriteAsync("SUPPORT_BUNDLE_ENGINE_SHA256=PASS");
                    }

                    await evidence.WriteAsync("SUPPORT_BUNDLE_CREATED=PASS");
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    overallPass = false;
                    await evidence.WriteAsync($"SUPPORT_BUNDLE_FAILURE_TYPE={SafeTypeName(exception)}");
                }
            }

            try
            {
                if (GetCiadpiProcesses().Count != 0)
                {
                    foreach (var process in GetCiadpiProcesses())
                    {
                        try
                        {
                            process.Kill(entireProcessTree: true);
                            await process.WaitForExitAsync();
                        }
                        finally
                        {
                            process.Dispose();
                        }
                    }
                }

                if (stagingOwned)
                {
                    if (!File.Exists(ownershipMarker)
                        || !string.Equals(
                            (await File.ReadAllTextAsync(ownershipMarker)).Trim('\uFEFF', '\r', '\n', ' '),
                            correlationId,
                            StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            "Validation ownership marker changed; refusing cleanup.");
                    }

                    if (Directory.Exists(versionRoot))
                    {
                        Directory.Delete(versionRoot, recursive: true);
                    }

                    File.Delete(ownershipMarker);
                    TryDeleteIfEmpty(Path.Combine(productRoot, "Engines", "Managed", "byedpi"));
                    TryDeleteIfEmpty(Path.Combine(productRoot, "Engines", "Managed"));
                }

                if (GetCiadpiProcesses().Count != 0)
                {
                    throw new InvalidOperationException("ciadpi process residue remained after cleanup.");
                }

                cleanupPass = true;
                await evidence.WriteAsync("CANDIDATE_CLEANUP=PASS");
                await evidence.WriteAsync("RUNTIME_RESIDUE=NONE");
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                cleanupPass = false;
                overallPass = false;
                await evidence.WriteAsync($"CLEANUP_FAILURE_TYPE={SafeTypeName(exception)}");
            }

            try
            {
                if (proxyBefore is not null)
                {
                    var proxyAfter = await GetProxyFingerprintAsync();
                    proxyUnchanged = string.Equals(proxyBefore, proxyAfter, StringComparison.Ordinal);
                }

                if (winDivertBefore is not null)
                {
                    var winDivertAfter = await GetWinDivertFingerprintAsync();
                    winDivertUnchanged = string.Equals(winDivertBefore, winDivertAfter, StringComparison.Ordinal);
                }

                await evidence.WriteAsync($"SYSTEM_PROXY_UNCHANGED={(proxyUnchanged ? "PASS" : "FAIL")}");
                await evidence.WriteAsync($"WINDIVERT_UNCHANGED={(winDivertUnchanged ? "PASS" : "FAIL")}");
                if (!proxyUnchanged || !winDivertUnchanged)
                {
                    overallPass = false;
                }
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                overallPass = false;
                await evidence.WriteAsync($"BOUNDARY_CHECK_FAILURE_TYPE={SafeTypeName(exception)}");
            }

            if (!cleanupPass)
            {
                overallPass = false;
            }

            await evidence.WriteAsync("SYSTEM_PROXY_MUTATION=NOT_REQUESTED");
            await evidence.WriteAsync("WINDIVERT=NOT_REQUESTED");
            await evidence.WriteAsync("PRODUCTION_ADMISSION=BLOCK");
            await evidence.WriteAsync(
                overallPass
                    ? "RESULT=R42_12_3_REAL_PC_VALIDATION_PASS_BLOCK"
                    : "RESULT=R42_12_3_REAL_PC_VALIDATION_FAIL_BLOCK");
        }

        await evidence.DisposeAsync();

        var finalBundle = CreateCombinedEvidenceBundle(
            supportRoot,
            stamp,
            evidencePath,
            diagnosticsZip,
            AppContext.BaseDirectory);

        Console.WriteLine(overallPass
            ? "R42.12.3 real-PC pre-publication validation PASSED."
            : "R42.12.3 real-PC pre-publication validation FAILED.");
        Console.WriteLine($"Evidence bundle: {finalBundle}");
        Console.WriteLine("Production admission remains BLOCKED.");
        return overallPass ? 0 : 1;
    }

    private static void RequireAdministrator()
    {
        var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        if (!principal.IsInRole(WindowsBuiltInRole.Administrator))
        {
            throw new UnauthorizedAccessException("Administrator elevation is required for protected staging.");
        }
    }

    private static void RequireWindowsX64()
    {
        if (!Environment.Is64BitOperatingSystem || !Environment.Is64BitProcess)
        {
            throw new PlatformNotSupportedException("Windows x64 process on Windows x64 is required.");
        }
    }

    private static Uri ParseTarget(string[] args, out bool customTarget)
    {
        string value = DefaultTarget;
        customTarget = false;

        for (var index = 0; index < args.Length; index++)
        {
            if (!string.Equals(args[index], "--target-url", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Only --target-url <https-url> is supported.");
            }

            if (index + 1 >= args.Length)
            {
                throw new ArgumentException("--target-url requires a value.");
            }

            value = args[++index];
            customTarget = true;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || string.IsNullOrWhiteSpace(uri.Host)
            || uri.IsLoopback
            || IPAddress.TryParse(uri.Host, out _))
        {
            throw new ArgumentException("Validation target must be an absolute public HTTPS hostname URL.");
        }

        return uri;
    }

    private static void RequireExactFile(string path, long size, string sha256, string label)
    {
        RequireRegularFile(path, $"Missing {label}: {Path.GetFileName(path)}");
        var info = new FileInfo(path);
        if (info.Length != size)
        {
            throw new InvalidDataException($"Unexpected {label} size.");
        }

        using var stream = info.OpenRead();
        var actual = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        if (!string.Equals(actual, sha256, StringComparison.Ordinal))
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
            throw new InvalidDataException("Validation pack refuses reparse-point files.");
        }
    }

    private static void ProtectValidationTree(string root)
    {
        RunRequiredProcess("icacls.exe", [root, "/inheritance:r"]);
        RunRequiredProcess(
            "icacls.exe",
            [
                root,
                "/grant:r",
                "*S-1-5-18:(OI)(CI)F",
                "*S-1-5-32-544:(OI)(CI)F",
                "*S-1-5-32-545:(OI)(CI)RX"
            ]);
        RunRequiredProcess(
            "icacls.exe",
            [root, "/setowner", "*S-1-5-32-544", "/T", "/C"]);
        RunRequiredProcess(
            "icacls.exe",
            [
                root,
                "/grant:r",
                "*S-1-5-18:(OI)(CI)F",
                "*S-1-5-32-544:(OI)(CI)F",
                "*S-1-5-32-545:(OI)(CI)RX",
                "/T",
                "/C"
            ]);
    }

    private static void RunRequiredProcess(string fileName, IReadOnlyList<string> arguments)
    {
        using var process = new Process
        {
            StartInfo = CreateProcessStartInfo(fileName, arguments, redirect: true)
        };
        process.Start();
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Required system utility failed: {Path.GetFileName(fileName)} ({process.ExitCode}). " +
                $"Output lengths: {stdout.Length}/{stderr.Length}.");
        }
    }

    private static async Task<CapturedProcess> RunCaptureAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        TimeSpan timeout)
    {
        using var process = new Process
        {
            StartInfo = CreateProcessStartInfo(fileName, arguments, redirect: true)
        };
        process.Start();
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        using var timeoutCts = new CancellationTokenSource(timeout);
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
            throw new TimeoutException($"Process timed out: {Path.GetFileName(fileName)}");
        }

        return new CapturedProcess(
            process.ExitCode,
            await stdoutTask,
            await stderrTask);
    }

    private static ProcessStartInfo CreateProcessStartInfo(
        string fileName,
        IReadOnlyList<string> arguments,
        bool redirect)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = redirect,
            RedirectStandardError = redirect
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }
        return startInfo;
    }

    private static IEnumerable<string> SplitEvidenceLines(string value) =>
        value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static line => line.Length <= 1024)
            .Take(128);

    private static void RequireHarnessPass(CapturedProcess result)
    {
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException("Pinned R34 harness returned a non-zero exit code.");
        }

        foreach (var marker in new[]
                 {
                     "TYPED_ARGV=PASS",
                     "ROUND1_SOCKS_HTTP=PASS",
                     "ROUND1_CLEANUP=PASS",
                     "ROUND2_SOCKS_HTTP=PASS",
                     "ROUND2_CLEANUP=PASS",
                     "CRASH_RECONCILE=PASS",
                     "FINAL_STATE=STOPPED",
                     "SYSTEM_PROXY_MUTATION=NOT_REQUESTED",
                     "WINDIVERT=NOT_REQUESTED",
                     "PRODUCTION_ADMISSION=BLOCK",
                     "RESULT=BYEDPI_SERVICE_OWNED_FUNCTIONAL_R34_PASS"
                 })
        {
            if (!result.StandardOutput.Contains(marker, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Pinned R34 harness marker missing: {marker}");
            }
        }
    }

    private static async Task<ExternalProbeResult> RunExternalProbeAsync(
        string candidate,
        Uri target)
    {
        var port = ReserveFreeLoopbackPort();
        var direct = await ProbeHttpsAsync(target, proxyPort: null);

        using var process = new Process
        {
            StartInfo = CreateProcessStartInfo(
                candidate,
                [
                    "-i", "127.0.0.1",
                    "-p", port.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    "-N", "-U", "-c", "8", "-x", "0"
                ],
                redirect: true)
        };

        process.Start();
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        try
        {
            await WaitForLoopbackListenerAsync(port, TimeSpan.FromSeconds(10));
            var viaByeDpi = await ProbeHttpsAsync(target, port);
            return new ExternalProbeResult(direct, viaByeDpi);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            await process.WaitForExitAsync();
            _ = await stdoutTask;
            _ = await stderrTask;
            await WaitForNoListenerAsync(port, TimeSpan.FromSeconds(10));
        }
    }

    private static async Task<HttpProbe> ProbeHttpsAsync(Uri target, int? proxyPort)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var handler = new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                ConnectTimeout = TimeSpan.FromSeconds(15),
                UseProxy = proxyPort is not null,
                Proxy = proxyPort is null
                    ? null
                    : new WebProxy(new Uri($"socks5://127.0.0.1:{proxyPort.Value}"))
            };
            using var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
            using var request = new HttpRequestMessage(HttpMethod.Get, target);
            request.Headers.UserAgent.ParseAdd("NetPilot-R42.12.3-PC-Validation/1.0");
            using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead);
            stopwatch.Stop();
            return new HttpProbe(
                true,
                (int)response.StatusCode,
                stopwatch.ElapsedMilliseconds,
                null);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            stopwatch.Stop();
            return new HttpProbe(
                false,
                null,
                stopwatch.ElapsedMilliseconds,
                SafeTypeName(exception));
        }
    }

    private static int ReserveFreeLoopbackPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        try
        {
            listener.Start(1);
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    private static async Task WaitForLoopbackListenerAsync(int port, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var endpoints = IPGlobalProperties.GetIPGlobalProperties()
                .GetActiveTcpListeners()
                .Where(endpoint => endpoint.Port == port)
                .ToArray();
            if (endpoints.Length > 0)
            {
                if (endpoints.Any(endpoint => !IPAddress.IsLoopback(endpoint.Address)))
                {
                    throw new InvalidOperationException("ByeDPI listener escaped the loopback boundary.");
                }
                return;
            }
            await Task.Delay(100);
        }
        throw new TimeoutException("ByeDPI loopback listener did not become ready.");
    }

    private static async Task WaitForNoListenerAsync(int port, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var exists = IPGlobalProperties.GetIPGlobalProperties()
                .GetActiveTcpListeners()
                .Any(endpoint => endpoint.Port == port);
            if (!exists)
            {
                return;
            }
            await Task.Delay(100);
        }
        throw new TimeoutException("ByeDPI listener remained after cleanup.");
    }

    private static async Task<string> CollectNetPilotDiagnosticsAsync(
        string installedApp,
        string supportRoot)
    {
        var started = DateTime.UtcNow.AddSeconds(-2);
        var result = await RunCaptureAsync(
            installedApp,
            ["--collect-diagnostics"],
            TimeSpan.FromSeconds(60));
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException("Installed NetPilot diagnostics mode returned non-zero.");
        }

        var bundle = new DirectoryInfo(supportRoot)
            .EnumerateFiles("NetPilot-Diagnostics-*-UTC.zip", SearchOption.TopDirectoryOnly)
            .Where(file => file.LastWriteTimeUtc >= started)
            .OrderByDescending(static file => file.LastWriteTimeUtc)
            .FirstOrDefault();
        return bundle?.FullName
               ?? throw new FileNotFoundException("NetPilot diagnostics ZIP was not created.");
    }

    private static void RequireSupportBundleEngineHash(string diagnosticsZip)
    {
        using var archive = ZipFile.OpenRead(diagnosticsZip);
        var entry = archive.GetEntry("engine-status.json")
                    ?? throw new InvalidDataException("Support bundle is missing engine-status.json.");
        using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
        var content = reader.ReadToEnd();
        if (!content.Contains(CandidateSha256, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Support bundle does not identify the exact staged ByeDPI SHA-256.");
        }
    }

    private static async Task<string> GetProxyFingerprintAsync()
    {
        using var internet = Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Internet Settings",
            writable: false);
        var proxyEnable = internet?.GetValue("ProxyEnable")?.ToString() ?? "<null>";
        var proxyServer = internet?.GetValue("ProxyServer")?.ToString() ?? "<null>";
        var autoConfig = internet?.GetValue("AutoConfigURL")?.ToString() ?? "<null>";
        var winHttp = await RunCaptureAsync(
            "netsh.exe",
            ["winhttp", "show", "proxy"],
            TimeSpan.FromSeconds(10));
        return Fingerprint($"{proxyEnable}\n{proxyServer}\n{autoConfig}\n{winHttp.StandardOutput}");
    }

    private static async Task<string> GetWinDivertFingerprintAsync()
    {
        var drivers = await RunCaptureAsync(
            "sc.exe",
            ["query", "type=", "driver", "state=", "all"],
            TimeSpan.FromSeconds(15));
        var services = await RunCaptureAsync(
            "sc.exe",
            ["query", "type=", "service", "state=", "all"],
            TimeSpan.FromSeconds(15));
        var relevant = string.Join(
            "\n",
            SplitEvidenceLines(drivers.StandardOutput + "\n" + services.StandardOutput)
                .Where(line => line.Contains("WinDivert", StringComparison.OrdinalIgnoreCase)));
        return Fingerprint(relevant);
    }

    private static string Fingerprint(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static List<Process> GetCiadpiProcesses() =>
        Process.GetProcessesByName("ciadpi").ToList();

    private static void TryDeleteIfEmpty(string directory)
    {
        try
        {
            if (Directory.Exists(directory)
                && !Directory.EnumerateFileSystemEntries(directory).Any())
            {
                Directory.Delete(directory);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static string CreateCombinedEvidenceBundle(
        string supportRoot,
        string stamp,
        string evidencePath,
        string? diagnosticsZip,
        string packRoot)
    {
        var staging = Path.Combine(supportRoot, ".r42-12-3-bundle-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            File.Copy(evidencePath, Path.Combine(staging, Path.GetFileName(evidencePath)));
            if (diagnosticsZip is not null && File.Exists(diagnosticsZip))
            {
                File.Copy(diagnosticsZip, Path.Combine(staging, Path.GetFileName(diagnosticsZip)));
            }

            var manifest = Path.Combine(packRoot, "package-manifest.json");
            if (File.Exists(manifest))
            {
                File.Copy(manifest, Path.Combine(staging, "package-manifest.json"));
            }

            var output = Path.Combine(
                supportRoot,
                $"NetPilot-ByeDPI-PC-Validation-{stamp}-UTC.zip");
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

    private sealed class EvidenceWriter : IAsyncDisposable
    {
        private readonly StreamWriter writer;
        private bool disposed;

        public EvidenceWriter(string path)
        {
            writer = new StreamWriter(
                new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
            {
                AutoFlush = true
            };
        }

        public Task WriteAsync(string line)
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(EvidenceWriter));
            }
            return writer.WriteLineAsync(line);
        }

        public async ValueTask DisposeAsync()
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            await writer.DisposeAsync();
        }
    }

    private sealed record CapturedProcess(
        int ExitCode,
        string StandardOutput,
        string StandardError);

    private sealed record HttpProbe(
        bool Success,
        int? StatusCode,
        long ElapsedMilliseconds,
        string? ErrorType);

    private sealed record ExternalProbeResult(
        HttpProbe Direct,
        HttpProbe ViaByeDpi);
}
