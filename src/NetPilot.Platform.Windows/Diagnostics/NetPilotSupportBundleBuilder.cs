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
        var safeProperties = context?.SafeProperties is null
            ? null
            : context.SafeProperties
                .Take(16)
                .ToDictionary(
                    static pair => SafeDiagnosticLog.Sanitize(pair.Key, 64),
                    static pair => SafeDiagnosticLog.Sanitize(pair.Value, 512),
                    StringComparer.Ordinal);

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

    private static void WritePrivacyNotice(string staging, bool serviceLogsReadable)
    {
        var text = string.Join(
            Environment.NewLine,
            "NetPilot diagnostic support bundle",
            "",
            "The bundle is created locally and is never uploaded automatically.",
            "It intentionally excludes cookies, passwords, API keys, environment variables, IPC message bodies, browser data, user name, machine name and network addresses.",
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
