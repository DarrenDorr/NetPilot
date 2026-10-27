namespace NetPilot.Core.Models;

public sealed record ResourceDefinition(
    string Id,
    string DisplayName,
    IReadOnlyList<Uri> HealthCheckUris,
    IReadOnlyList<string> StrategyDomains);
