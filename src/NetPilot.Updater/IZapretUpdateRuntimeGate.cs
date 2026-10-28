namespace NetPilot.Updater;

public sealed record ZapretUpdateRuntimeState(
    bool WasRunning,
    string? StrategyId)
{
    public static ZapretUpdateRuntimeState Stopped { get; } = new(false, null);

    public static ZapretUpdateRuntimeState Running(string strategyId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(strategyId);
        return new ZapretUpdateRuntimeState(true, strategyId);
    }
}

public enum ZapretRuntimeRestoreStatus
{
    NotRequired,
    Restored,
    Failed
}

public sealed record ZapretRuntimeRestoreResult(
    ZapretRuntimeRestoreStatus Status,
    string? StrategyId = null,
    string? ErrorCode = null)
{
    public static ZapretRuntimeRestoreResult NotRequired { get; } =
        new(ZapretRuntimeRestoreStatus.NotRequired);

    public static ZapretRuntimeRestoreResult Restored(string strategyId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(strategyId);
        return new ZapretRuntimeRestoreResult(
            ZapretRuntimeRestoreStatus.Restored,
            strategyId);
    }

    public static ZapretRuntimeRestoreResult Failed(string strategyId, string errorCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(strategyId);
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        return new ZapretRuntimeRestoreResult(
            ZapretRuntimeRestoreStatus.Failed,
            strategyId,
            errorCode);
    }
}

public interface IZapretUpdateRuntimeGate
{
    Task<ZapretUpdateRuntimeState> CaptureAndStopAsync(
        CancellationToken cancellationToken = default);

    Task<ZapretRuntimeRestoreResult> RestoreAsync(
        ZapretUpdateRuntimeState state,
        CancellationToken cancellationToken = default);
}
