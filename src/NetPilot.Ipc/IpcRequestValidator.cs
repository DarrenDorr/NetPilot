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
            IpcCommand.Ping => ValidateNoPayload(request),
            IpcCommand.EnrollAuthorizedUser => ValidateEnrollment(request),
            IpcCommand.ZapretGetInstallationStatus => ValidateNoPayload(request),
            IpcCommand.ZapretListStrategies => ValidateNoPayload(request),
            IpcCommand.ZapretGetStatus => ValidateNoPayload(request),
            IpcCommand.ZapretStop => ValidateNoPayload(request),
            IpcCommand.ZapretInstallLatest => ValidateNoPayload(request),
            IpcCommand.ZapretStartStrategy => ValidateStrategyStart(request),
            _ => IpcValidationResult.Invalid(
                "UnknownCommand",
                "Указана неизвестная IPC-команда.")
        };
    }

    private static IpcValidationResult ValidateNoPayload(IpcRequest request)
    {
        if (request.StrategyId is not null)
        {
            return IpcValidationResult.Invalid(
                "UnexpectedStrategyId",
                "Эта IPC-команда не должна содержать идентификатор стратегии.");
        }

        if (request.EnrollmentNonce is not null)
        {
            return IpcValidationResult.Invalid(
                "UnexpectedEnrollmentNonce",
                "Эта IPC-команда не должна содержать nonce регистрации.");
        }

        return IpcValidationResult.Valid();
    }

    private static IpcValidationResult ValidateEnrollment(IpcRequest request)
    {
        if (request.StrategyId is not null)
        {
            return IpcValidationResult.Invalid(
                "UnexpectedStrategyId",
                "Регистрация пользователя не должна содержать идентификатор стратегии.");
        }

        var nonce = request.EnrollmentNonce;
        if (string.IsNullOrWhiteSpace(nonce)
            || nonce.Length != IpcProtocol.EnrollmentNonceCharacters
            || nonce.Any(static character => !Uri.IsHexDigit(character)))
        {
            return IpcValidationResult.Invalid(
                "InvalidEnrollmentNonce",
                "Nonce регистрации имеет недопустимый формат.");
        }

        return IpcValidationResult.Valid();
    }

    private static IpcValidationResult ValidateStrategyStart(IpcRequest request)
    {
        if (request.EnrollmentNonce is not null)
        {
            return IpcValidationResult.Invalid(
                "UnexpectedEnrollmentNonce",
                "Команда запуска стратегии не должна содержать nonce регистрации.");
        }

        return ValidateStrategyId(request.StrategyId);
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
}

public interface IPrivilegedServiceClient
{
    Task<IpcResponse> PingAsync(CancellationToken cancellationToken = default);
    Task<IpcResponse> EnrollAuthorizedUserAsync(
        string enrollmentNonce,
        CancellationToken cancellationToken = default);
    Task<IpcResponse> GetZapretInstallationStatusAsync(CancellationToken cancellationToken = default);
    Task<IpcResponse> ListZapretStrategiesAsync(CancellationToken cancellationToken = default);
    Task<IpcResponse> GetZapretStatusAsync(CancellationToken cancellationToken = default);
    Task<IpcResponse> StartZapretStrategyAsync(
        string strategyId,
        CancellationToken cancellationToken = default);
    Task<IpcResponse> StopZapretAsync(CancellationToken cancellationToken = default);
    Task<IpcResponse> InstallLatestZapretAsync(CancellationToken cancellationToken = default);
}
