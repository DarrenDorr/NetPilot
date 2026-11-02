using System.Text.Json;
using System.Text.RegularExpressions;

namespace NetPilot.Platform.Windows.Diagnostics;

public enum NetPilotDiagnosticLevel
{
    Debug,
    Information,
    Warning,
    Error,
    Critical
}

public sealed record NetPilotDiagnosticRecord(
    DateTimeOffset TimestampUtc,
    NetPilotDiagnosticLevel Level,
    string Component,
    string EventCode,
    string? CorrelationId,
    string Message,
    string? ExceptionType,
    int? ExceptionHResult,
    string? StackTrace,
    IReadOnlyDictionary<string, string>? Properties);

public static partial class SafeDiagnosticLog
{
    private const long MaxLogFileBytes = 5L * 1024L * 1024L;
    private const long MaxDirectoryBytes = 20L * 1024L * 1024L;
    private const int RetentionDays = 7;
    private static readonly object Sync = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false
    };

    public static string WriteUser(
        string component,
        NetPilotDiagnosticLevel level,
        string eventCode,
        string message,
        string? correlationId = null,
        Exception? exception = null,
        IReadOnlyDictionary<string, string>? properties = null) =>
        Write(
            WindowsDiagnosticPaths.GetUserLogDirectory(),
            component,
            level,
            eventCode,
            message,
            correlationId,
            exception,
            properties);

    public static string WriteService(
        string component,
        NetPilotDiagnosticLevel level,
        string eventCode,
        string message,
        string? correlationId = null,
        Exception? exception = null,
        IReadOnlyDictionary<string, string>? properties = null) =>
        Write(
            WindowsDiagnosticPaths.GetServiceLogDirectory(),
            component,
            level,
            eventCode,
            message,
            correlationId,
            exception,
            properties);

    public static string Write(
        string directory,
        string component,
        NetPilotDiagnosticLevel level,
        string eventCode,
        string message,
        string? correlationId = null,
        Exception? exception = null,
        IReadOnlyDictionary<string, string>? properties = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(component);
        ArgumentException.ThrowIfNullOrWhiteSpace(eventCode);

        var now = DateTimeOffset.UtcNow;
        var safeComponent = NormalizeIdentifier(component, 48);
        var record = new NetPilotDiagnosticRecord(
            now,
            level,
            safeComponent,
            NormalizeIdentifier(eventCode, 64),
            SanitizeOptional(correlationId, 128),
            Sanitize(message, 4096),
            exception?.GetType().FullName,
            exception?.HResult,
            SanitizeOptional(exception?.StackTrace, 8192),
            SanitizeProperties(properties));

        lock (Sync)
        {
            Directory.CreateDirectory(directory);
            ApplyRetention(directory, now.UtcDateTime);
            var path = ResolveLogPath(directory, safeComponent, now.UtcDateTime);
            var json = JsonSerializer.Serialize(record, JsonOptions);
            File.AppendAllText(path, json + Environment.NewLine);
            return path;
        }
    }

    public static string Sanitize(string? value, int maxLength = 4096)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var sanitized = value.Replace('\r', ' ').Replace('\n', ' ');
        sanitized = ReplaceIdentity(sanitized, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "%USERPROFILE%");
        sanitized = ReplaceIdentity(sanitized, Environment.UserName, "%USERNAME%");
        sanitized = ReplaceIdentity(sanitized, Environment.MachineName, "%COMPUTERNAME%");
        sanitized = SensitiveValuePattern().Replace(sanitized, "$1=<redacted>");

        return sanitized.Length <= maxLength
            ? sanitized
            : sanitized[..maxLength] + "…";
    }

    private static string? SanitizeOptional(string? value, int maxLength) =>
        string.IsNullOrWhiteSpace(value) ? null : Sanitize(value, maxLength);

    private static IReadOnlyDictionary<string, string>? SanitizeProperties(
        IReadOnlyDictionary<string, string>? properties)
    {
        if (properties is null || properties.Count == 0)
        {
            return null;
        }

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in properties.Take(16))
        {
            result[NormalizeIdentifier(pair.Key, 64)] = Sanitize(pair.Value, 512);
        }

        return result;
    }

    private static string ResolveLogPath(string directory, string component, DateTime utcNow)
    {
        var prefix = $"netpilot-{component.ToLowerInvariant()}-{utcNow:yyyyMMdd}";
        for (var index = 0; index < 100; index++)
        {
            var suffix = index == 0 ? string.Empty : $"-{index:D2}";
            var path = Path.Combine(directory, prefix + suffix + ".jsonl");
            if (!File.Exists(path) || new FileInfo(path).Length < MaxLogFileBytes)
            {
                return path;
            }
        }

        throw new IOException("Diagnostic log rotation limit reached.");
    }

    private static void ApplyRetention(string directory, DateTime utcNow)
    {
        var files = new DirectoryInfo(directory)
            .EnumerateFiles("netpilot-*.jsonl", SearchOption.TopDirectoryOnly)
            .OrderBy(static file => file.LastWriteTimeUtc)
            .ToList();

        var cutoff = utcNow.AddDays(-RetentionDays);
        foreach (var file in files.Where(file => file.LastWriteTimeUtc < cutoff).ToArray())
        {
            TryDelete(file);
            files.Remove(file);
        }

        var total = files.Sum(static file => file.Exists ? file.Length : 0L);
        foreach (var file in files)
        {
            if (total <= MaxDirectoryBytes)
            {
                break;
            }

            var length = file.Exists ? file.Length : 0L;
            if (TryDelete(file))
            {
                total -= length;
            }
        }
    }

    private static bool TryDelete(FileInfo file)
    {
        try
        {
            file.Delete();
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string NormalizeIdentifier(string value, int maxLength)
    {
        var chars = value
            .Where(static ch => char.IsLetterOrDigit(ch) || ch is '.' or '-' or '_')
            .Take(maxLength)
            .ToArray();

        return chars.Length == 0 ? "unknown" : new string(chars);
    }

    private static string ReplaceIdentity(string source, string identity, string replacement)
    {
        if (string.IsNullOrWhiteSpace(identity))
        {
            return source;
        }

        return source.Replace(identity, replacement, StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex("(?i)(token|password|passwd|secret|authorization|cookie|api[-_]?key)\\s*[:=]\\s*[^\\s;,]+", RegexOptions.CultureInvariant)]
    private static partial Regex SensitiveValuePattern();
}
