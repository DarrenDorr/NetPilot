using System.Text.Json;
using NetPilot.Platform.Windows.Diagnostics;

namespace NetPilot.Platform.Windows.Tests;

public sealed class DiagnosticsTests
{
    [Fact]
    public void Sanitize_redacts_identity_and_secret_values()
    {
        var source = $"user={Environment.UserName} machine={Environment.MachineName} token=abc123 password:secret-value";

        var sanitized = SafeDiagnosticLog.Sanitize(source);

        Assert.DoesNotContain(Environment.UserName, sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Environment.MachineName, sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("abc123", sanitized, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-value", sanitized, StringComparison.Ordinal);
        Assert.Contains("<redacted>", sanitized, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_creates_parseable_jsonl_without_raw_secret()
    {
        var directory = Path.Combine(Path.GetTempPath(), "netpilot-diagnostics-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var path = SafeDiagnosticLog.Write(
                directory,
                "test",
                NetPilotDiagnosticLevel.Error,
                "NP-TEST-001",
                "operation failed token=do-not-store",
                "correlation-1",
                new InvalidOperationException("password=do-not-store-either"),
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["ResultCode"] = "TestFailure",
                    ["secret"] = "api_key=private-value"
                });

            Assert.True(File.Exists(path));
            var line = Assert.Single(File.ReadAllLines(path));
            Assert.DoesNotContain("do-not-store", line, StringComparison.Ordinal);
            Assert.DoesNotContain("private-value", line, StringComparison.Ordinal);

            using var document = JsonDocument.Parse(line);
            Assert.Equal("NP-TEST-001", document.RootElement.GetProperty("EventCode").GetString());
            Assert.Equal("correlation-1", document.RootElement.GetProperty("CorrelationId").GetString());
            Assert.Equal("TestFailure", document.RootElement.GetProperty("Properties").GetProperty("ResultCode").GetString());
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
