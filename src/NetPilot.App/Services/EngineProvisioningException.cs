namespace NetPilot.App.Services;

/// <summary>
/// Платформенно-нейтральная ошибка управления установкой сетевого движка.
/// Общий GUI не должен зависеть от Windows IPC, Named Pipe или конкретного engine adapter.
/// </summary>
public sealed class EngineProvisioningException(
    string code,
    string message,
    Exception? innerException = null) : Exception(message, innerException)
{
    public string Code { get; } = string.IsNullOrWhiteSpace(code)
        ? "EngineProvisioningError"
        : code;
}
