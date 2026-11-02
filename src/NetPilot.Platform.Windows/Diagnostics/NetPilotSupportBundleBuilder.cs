using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NetPilot.Platform.Windows.Diagnostics;

public sealed record NetPilotSupportBundleContext(
    string? LastOperation = null,
    string? DiagnosticCode = null,
    string? CorrelationId = null,
    IReadOnlyDictionary<string, string>? SafeProperties = null);

public sealed record NetPilotSupportBundleResult(
    string BundlePath,
    int IncludedUserLogs,
    int IncludedServiceLogs,
    bool ServiceLogsReadable);

public static class NetPilotSupportBundleBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static NetPilotSupportBundleResult Build(NetPilotSupportBundleContext? context = null)
    {
        var supportDirectory = WindowsDiagnosticPaths.GetSupportBundleDirectory();
        Directory.CreateDirectory(supportDirectory);

        var now = DateTimeOffset.UtcNow;
        var staging = Path.Combine(supportDirectory, ".staging-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);

        try
        {
            var diagnosticsDirectory = Path.Combine(staging, "logs");
            Directory.CreateDirectory(diagnosticsDirectory);

            var userLogCount = CopyLogs(
                WindowsDiagnosticPaths.GetUserLogDirectory(),
                Path.Combine(diagnosticsDirectory, "app"));

            var serviceLogsReadable = true;
            var serviceLogCount = 0;
            try
            {
                serviceLogCount = CopyLogs(
                    WindowsDiagnosticPaths.GetServiceLogDirectory(),
                    Path.Combine(diagnosticsDirectory, "service"));
            }
            catch (UnauthorizedAccessException)
            {
                serviceLogsReadable = false;
            }
            catch (IOException)
            {
                serviceLogsReadable = false;
            }

            WriteDiagnosticsJson(staging, now, context, userLogCount, serviceLogCount, serviceLogsReadable);
            WriteEnvironment(staging);
            WriteEngineStatus(staging);
            WritePrivacyNotice(staging, serviceLogsReadable);
            WriteChecksums(staging);

            var bundleName = $"NetPilot-Diagnostics-{now:yyyyMMdd-HHmmss}-UTC.zip";
            var bundlePath = Path.Combine(supportDirectory, bundleName);
            if (File.Exists(bundlePath))
            {
                File.Delete(bundlePath);
            }

            ZipFile.CreateFromDirectory(staging, bundlePath, CompressionLevel.Optimal, includeBaseDirectory: false);
            File.WriteAllText(
                Path.Combine(supportDirectory, "latest-bundle.txt"),
                bundlePath + Environment.NewLine,
                Encoding.UTF8);

            SafeDiagnosticLog.WriteUser(
                "app",
                NetPilotDiagnosticLevel.Information,
                "NP-DIAG-BUNDLE-001",
                "Локальный диагностический пакет создан. Автоматическая отправка не выполнялась.",
                context?.CorrelationId,
                properties: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["UserLogs"] = userLogCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["ServiceLogs"] = serviceLogCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["ServiceLogsReadable"] = serviceLogsReadable.ToString()
                });

            return new NetPilotSupportBundleResult(
                bundlePath,
                userLogCount,
                serviceLogCount,
                serviceLogsReadable);
        }
        finally
        {
            TryDeleteDirectory(staging);
        }
    }

    private static int CopyLogs(string sourceDirectory, string destinationDirectory)
    {
        if (!Directory.Exists(sourceDirectory))
        {
            return 0;
        }

        Directory.CreateDirectory(destinationDirectory);
        var count = 0;
        foreach (var file in new DirectoryInfo(sourceDirectory)
                     .EnumerateFiles("netpilot-*.jsonl", SearchOption.TopDirectoryOnly)
                     .OrderBy(static item => item.Name, StringComparer.Ordinal))
        {
            var destination = Path.Combine(destinationDirectory, file.Name);
            file.CopyTo(destination, overwrite: false);
            count++;
        }

        return count;
    }

    private static void WriteDiagnosticsJson(
        string staging,
        DateTimeOffset now,
        NetPilotSupportBundleContext? context,
        int userLogCount,
        int serviceLogCount,
        bool serviceLogsReadable)
    {
        Dictionary<string, string>? safeProperties = null;
        if (context?.SafeProperties is { Count: > 0 })
        {
            safeProperties = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in context.SafeProperties.Take(16))
            {
                safeProperties[SafeDiagnosticLog.Sanitize(pair.Key, 64)] =
                    SafeDiagnosticLog.Sanitize(pair.Value, 512);
            }
        }

        var payload = new
        {
            Schema = 1,
            CreatedAtUtc = now,
            NetPilotVersion = GetApplicationVersion(),
            LastOperation = SafeOptional(context?.LastOperation, 128),
            DiagnosticCode = SafeOptional(context?.DiagnosticCode, 128),
            CorrelationId = SafeOptional(context?.CorrelationId, 128),
            SafeProperties = safeProperties,
            UserLogFiles = userLogCount,
            ServiceLogFiles = serviceLogCount,
            ServiceLogsReadable = serviceLogsReadable,
            AutomaticUpload = false
        };

        File.WriteAllText(
            Path.Combine(staging, "diagnostics.json"),
            JsonSerializer.Serialize(payload, JsonOptions),
            Encoding.UTF8);
    }

    private static void WriteEnvironment(string staging)
    {
        var lines = new[]
        {
            $"OS={SafeDiagnosticLog.Sanitize(RuntimeInformation.OSDescription, 256)}",
            $"OSArchitecture={RuntimeInformation.OSArchitecture}",
            $"ProcessArchitecture={RuntimeInformation.ProcessArchitecture}",
            $"Framework={SafeDiagnosticLog.Sanitize(RuntimeInformation.FrameworkDescription, 256)}",
            $"64BitOperatingSystem={Environment.Is64BitOperatingSystem}",
            $"64BitProcess={Environment.Is64BitProcess}"
        };

        File.WriteAllLines(Path.Combine(staging, "environment.txt"), lines, Encoding.UTF8);
    }

    private static void WriteEngineStatus(string staging)
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "NetPilot",
            "Engines",
            "Managed",
            "byedpi");

        var engines = new List<object>();
        string? error = null;

        try
        {
            if (Directory.Exists(root))
            {
                foreach (var versionDirectory in new DirectoryInfo(root)
                             .EnumerateDirectories("*", SearchOption.TopDirectoryOnly)
                             .OrderBy(static directory => directory.Name, StringComparer.Ordinal))
                {
                    if ((versionDirectory.Attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        continue;
                    }

                    foreach (var architectureDirectory in versionDirectory
                                 .EnumerateDirectories("*", SearchOption.TopDirectoryOnly)
                                 .OrderBy(static directory => directory.Name, StringComparer.Ordinal))
                    {
                        if ((architectureDirectory.Attributes & FileAttributes.ReparsePoint) != 0)
                        {
                            continue;
                        }

                        var executable = new FileInfo(
                            Path.Combine(architectureDirectory.FullName, "bin", "ciadpi.exe"));
                        if (!executable.Exists || (executable.Attributes & FileAttributes.ReparsePoint) != 0)
                        {
                            continue;
                        }

                        using var stream = executable.OpenRead();
                        var sha256 = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
                        engines.Add(new
                        {
                            Engine = "ByeDPI",
                            Version = SafeDiagnosticLog.Sanitize(versionDirectory.Name, 128),
                            Architecture = SafeDiagnosticLog.Sanitize(architectureDirectory.Name, 32),
                            Executable = "bin/ciadpi.exe",
                            executable.Length,
                            Sha256 = sha256
                        });
                    }
                }
            }
        }
        catch (UnauthorizedAccessException)
        {
            error = "EngineMetadataAccessDenied";
        }
        catch (IOException)
        {
            error = "EngineMetadataIoError";
        }

        var payload = new
        {
            Schema = 1,
            ManagedByeDpiRootPresent = Directory.Exists(root),
            Error = error,
            Engines = engines
        };

        File.WriteAllText(
            Path.Combine(staging, "engine-status.json"),
            JsonSerializer.Serialize(payload, JsonOptions),
            Encoding.UTF8);
    }

    private static void WritePrivacyNotice(string staging, bool serviceLogsReadable)
    {
        var text = string.Join(
            Environment.NewLine,
            "NetPilot diagnostic support bundle",
            "",
            "The bundle is created locally and is never uploaded automatically.",
            "It intentionally excludes cookies, passwords, API keys, environment-variable dumps, IPC message bodies, browser data and user network traffic.",
            "Local user name, machine name and user-profile paths are redacted from diagnostic text. Network addresses are not intentionally collected.",
            $"Service logs included: {serviceLogsReadable}.",
            "Review the ZIP before sharing it with a developer.",
            "");

        File.WriteAllText(Path.Combine(staging, "privacy.txt"), text, Encoding.UTF8);
    }

    private static void WriteChecksums(string staging)
    {
        var files = new DirectoryInfo(staging)
            .EnumerateFiles("*", SearchOption.AllDirectories)
            .Where(static file => !file.Name.Equals("checksums.txt", StringComparison.OrdinalIgnoreCase))
            .OrderBy(static file => file.FullName, StringComparer.Ordinal)
            .ToArray();

        var lines = new List<string>(files.Length);
        foreach (var file in files)
        {
            using var stream = file.OpenRead();
            var hash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            var relative = Path.GetRelativePath(staging, file.FullName).Replace('\\', '/');
            lines.Add($"{hash}  {relative}");
        }

        File.WriteAllLines(Path.Combine(staging, "checksums.txt"), lines, Encoding.UTF8);
    }

    private static string GetApplicationVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ?? typeof(NetPilotSupportBundleBuilder).Assembly;
        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "unknown";
    }

    private static string? SafeOptional(string? value, int maxLength) =>
        string.IsNullOrWhiteSpace(value) ? null : SafeDiagnosticLog.Sanitize(value, maxLength);

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
