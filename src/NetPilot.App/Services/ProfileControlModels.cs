namespace NetPilot.App.Services;

public enum ProfileSelectionStatus
{
    NotRunning,
    CurrentHealthy,
    TransientFailure,
    RecoveryNotApplicable,
    Switched,
    NoWorkingAlternative,
    OriginalRestoreFailed
}

public sealed record ProfileSelectionResult(
    ProfileSelectionStatus Status,
    string? OriginalProfileId,
    string? SelectedProfileId,
    string? DiagnosticCode = null);

public sealed record ProfileRuntimeState(bool IsRunning, string? ProfileId)
{
    public static ProfileRuntimeState Stopped { get; } = new(false, null);

    public static ProfileRuntimeState Running(string profileId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        return new ProfileRuntimeState(true, profileId);
    }
}

/// <summary>
/// Платформенно-нейтральная ошибка управления профилем.
/// Общий GUI не должен зависеть от legacy Strategy runtime, Windows IPC
/// или конкретного native engine adapter.
/// </summary>
public sealed class ProfileOperationException(
    string code,
    string message,
    Exception? innerException = null) : Exception(message, innerException)
{
    public string Code { get; } = string.IsNullOrWhiteSpace(code)
        ? "ProfileOperationError"
        : code;
}
