using NetPilot.Core.Models;

namespace NetPilot.Automation;

public sealed record StrategyDescriptor(string Id, string DisplayName);

public sealed record StrategyRuntimeState(bool IsRunning, string? StrategyId)
{
    public static StrategyRuntimeState Stopped { get; } = new(false, null);

    public static StrategyRuntimeState Running(string strategyId) =>
        new(true, strategyId);
}

public interface IStrategyRuntime
{
    Task<IReadOnlyList<StrategyDescriptor>> ListStrategiesAsync(
        CancellationToken cancellationToken = default);

    Task<StrategyRuntimeState> GetStateAsync(
        CancellationToken cancellationToken = default);

    Task ActivateAsync(
        string strategyId,
        CancellationToken cancellationToken = default);
}

public interface IResourceHealthProbe
{
    Task<ResourceHealth> CheckAsync(
        ResourceDefinition resource,
        CancellationToken cancellationToken = default);
}

public sealed class StrategyRuntimeException(
    string code,
    string message,
    Exception? innerException = null) : Exception(message, innerException)
{
    public string Code { get; } = code;
}

public enum StrategySelectionStatus
{
    NotRunning,
    CurrentHealthy,
    TransientFailure,
    Switched,
    NoWorkingAlternative,
    OriginalRestoreFailed
}

public sealed record StrategyEvaluation(
    string StrategyId,
    int Score,
    bool IsViable,
    TimeSpan? AverageLatency,
    IReadOnlyList<ResourceHealth> ResourceResults,
    string? DiagnosticCode = null);

public sealed record StrategySelectionResult(
    StrategySelectionStatus Status,
    string? OriginalStrategyId,
    string? SelectedStrategyId,
    IReadOnlyList<StrategyEvaluation> Evaluations,
    string? DiagnosticCode = null);

public sealed record StrategySelectionOptions
{
    public int FailureConfirmationAttempts { get; init; } = 2;
    public int MaxAlternativeCandidates { get; init; } = 6;
    public TimeSpan ConfirmationDelay { get; init; } = TimeSpan.FromMilliseconds(750);
    public TimeSpan PostSwitchSettleDelay { get; init; } = TimeSpan.FromMilliseconds(750);

    internal void Validate()
    {
        if (FailureConfirmationAttempts is < 2 or > 5)
        {
            throw new ArgumentOutOfRangeException(
                nameof(FailureConfirmationAttempts),
                "Количество подтверждающих проверок должно быть от 2 до 5.");
        }

        if (MaxAlternativeCandidates is < 1 or > 10)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaxAlternativeCandidates),
                "Количество альтернативных стратегий должно быть от 1 до 10.");
        }

        if (ConfirmationDelay < TimeSpan.Zero
            || ConfirmationDelay > TimeSpan.FromSeconds(10))
        {
            throw new ArgumentOutOfRangeException(nameof(ConfirmationDelay));
        }

        if (PostSwitchSettleDelay < TimeSpan.Zero
            || PostSwitchSettleDelay > TimeSpan.FromSeconds(10))
        {
            throw new ArgumentOutOfRangeException(nameof(PostSwitchSettleDelay));
        }
    }
}
