namespace NetPilot.Ipc;

public sealed record ZapretStartRequest(string StrategyId);

public sealed record ZapretStrategyInfo(
    string Id,
    string DisplayName);

public enum ZapretIpcState
{
    Stopped,
    Running
}

public sealed record ZapretIpcPayload(
    IReadOnlyList<ZapretStrategyInfo>? Strategies = null,
    ZapretIpcState? State = null,
    int? ProcessId = null,
    string? StrategyId = null);
