namespace NetPilot.Ipc;

public sealed record IpcValidationResult(bool IsValid, string Code, string? Message = null)
{
    public static IpcValidationResult Valid() => new(true, "Ok");
    public static IpcValidationResult Invalid(string code, string message) => new(false, code, message);
}

public static class IpcRequestValidator
{
    public static IpcValidationResult Validate(IpcRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.ProtocolVersion != IpcProtocol.Version)
        {
            return IpcValidationResult.Invalid(
                "UnsupportedProtocolVersion",
                "Версия IPC-протокола не поддерживается.");
        }

        if (request.RequestId == Guid.Empty)
        {
            return IpcValidationResult.Invalid(
                "InvalidRequestId",
                "Идентификатор IPC-запроса не может быть пустым.");
        }

        return request.Command switch
        {
            IpcCommand.Ping when request.StrategyId is null => IpcValidationResult.Valid(),
            IpcCommand.ZapretListStrategies when request.StrategyId is null => IpcValidationResult.Valid(),
            IpcCommand.ZapretGetStatus when request.StrategyId is null => IpcValidationResult.Valid(),
            IpcCommand.ZapretStop when request.StrategyId is null => IpcValidationResult.Valid(),
            IpcCommand.ZapretStartStrategy => ValidateStrategyId(request.StrategyId),
            IpcCommand.Ping => UnexpectedStrategyId(),
            IpcCommand.ZapretListStrategies => UnexpectedStrategyId(),
            IpcCommand.ZapretGetStatus => UnexpectedStrategyId(),
            IpcCommand.ZapretStop => UnexpectedStrategyId(),
            _ => IpcValidationResult.Invalid(
                "UnknownCommand",
                "Указана неизвестная IPC-команда.")
        };
    }

    private static IpcValidationResult ValidateStrategyId(string? strategyId)
    {
        if (string.IsNullOrWhiteSpace(strategyId))
        {
            return IpcValidationResult.Invalid(
                "MissingStrategyId",
                "Не указан идентификатор стратегии Zapret.");
        }

        if (strategyId.Length > IpcProtocol.MaxStrategyIdCharacters)
        {
            return IpcValidationResult.Invalid(
                "InvalidStrategyId",
                "Идентификатор стратегии Zapret слишком длинный.");
        }

        foreach (var character in strategyId)
        {
            if (!(character is >= 'A' and <= 'Z' or >= '0' and <= '9' or '_'))
            {
                return IpcValidationResult.Invalid(
                    "InvalidStrategyId",
                    "Идентификатор стратегии Zapret содержит недопустимые символы.");
            }
        }

        return IpcValidationResult.Valid();
    }

    private static IpcValidationResult UnexpectedStrategyId() =>
        IpcValidationResult.Invalid(
            "UnexpectedStrategyId",
            "Эта IPC-команда не должна содержать идентификатор стратегии.");
}

public interface IPrivilegedServiceClient
{
    Task<IpcResponse> PingAsync(CancellationToken cancellationToken = default);
    Task<IpcResponse> ListZapretStrategiesAsync(CancellationToken cancellationToken = default);
    Task<IpcResponse> GetZapretStatusAsync(CancellationToken cancellationToken = default);
    Task<IpcResponse> StartZapretStrategyAsync(
        string strategyId,
        CancellationToken cancellationToken = default);
    Task<IpcResponse> StopZapretAsync(CancellationToken cancellationToken = default);
}
