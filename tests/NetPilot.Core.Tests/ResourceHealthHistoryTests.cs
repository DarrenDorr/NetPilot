using NetPilot.Core.Models;

namespace NetPilot.Core.Tests;

public sealed class ResourceHealthHistoryTests
{
    [Fact]
    public void History_is_bounded_and_evicts_oldest_entries()
    {
        var history = new ResourceHealthHistory(capacity: 3);

        history.Add(Create("one", ResourceHealthState.Healthy));
        history.Add(Create("two", ResourceHealthState.Degraded, "Http429"));
        history.Add(Create("three", ResourceHealthState.Unavailable, "Timeout"));
        history.Add(Create("four", ResourceHealthState.Healthy));

        var snapshot = history.Snapshot();

        Assert.Equal(3, snapshot.Count);
        Assert.Equal(["two", "three", "four"], snapshot.Select(static entry => entry.ResourceId));
    }

    [Fact]
    public void Snapshot_is_a_copy_and_clear_removes_all_entries()
    {
        var history = new ResourceHealthHistory();
        history.Add(Create("discord", ResourceHealthState.Unavailable, "TlsHandshakeFailed"));

        var first = history.Snapshot();
        history.Clear();
        var second = history.Snapshot();

        Assert.Single(first);
        Assert.Empty(second);
    }

    [Theory]
    [InlineData("https://example.com")]
    [InlineData("discord\nsecret")]
    [InlineData("../../secret")]
    [InlineData("discord user")]
    public void Unsafe_resource_id_is_rejected(string resourceId)
    {
        var history = new ResourceHealthHistory();

        Assert.Throws<ArgumentException>(
            () => history.Add(Create(resourceId, ResourceHealthState.Unavailable, "Timeout")));
    }

    [Theory]
    [InlineData("Timeout\nhttps://secret.example")]
    [InlineData("TLS:secret")]
    [InlineData("code with spaces")]
    public void Unsafe_diagnostic_code_is_rejected(string diagnosticCode)
    {
        var history = new ResourceHealthHistory();

        Assert.Throws<ArgumentException>(
            () => history.Add(Create("discord", ResourceHealthState.Unavailable, diagnosticCode)));
    }

    [Fact]
    public void Intermediate_state_is_not_persisted_in_history()
    {
        var history = new ResourceHealthHistory();

        Assert.Throws<ArgumentException>(
            () => history.Add(Create("discord", ResourceHealthState.Checking)));
    }

    [Fact]
    public void Entry_contract_contains_no_sensitive_transport_fields()
    {
        var properties = typeof(ResourceHealthHistoryEntry)
            .GetProperties()
            .Select(static property => property.Name)
            .ToArray();

        Assert.Equal(
            ["ResourceId", "State", "CheckedAt", "Latency", "DiagnosticCode"],
            properties);
        Assert.DoesNotContain(properties, static name =>
            name.Contains("Uri", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Url", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Ip", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Message", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Exception", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Body", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Capacity_is_strictly_bounded()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ResourceHealthHistory(0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ResourceHealthHistory(ResourceHealthHistory.MaximumCapacity + 1));
    }

    private static ResourceHealth Create(
        string resourceId,
        ResourceHealthState state,
        string? diagnosticCode = null) =>
        new(
            resourceId,
            state,
            DateTimeOffset.UtcNow,
            state == ResourceHealthState.Healthy ? TimeSpan.FromMilliseconds(50) : null,
            diagnosticCode);
}
