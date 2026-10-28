using System.Text.RegularExpressions;

namespace NetPilot.Ipc;

public sealed record IpcValidationResult(bool IsValid, string Code, string? Message = null)
{
    public static IpcValidationResult Valid() => new(true, "Ok");
    public static IpcValidationResult Invalid(string code, string message) => new(false, code, message);
}

public static partial class IpcRequestValidator
{
    [GeneratedRegex("^[A-Z0-9](?:[A-Z0-9_]{0,47})$", RegexOptions.CultureInvariant)]
    private static partial Regex StrategyIdRegex();

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
            IpcCommand.Ping => ValidateNoPayload(request, "Ping"),

            IpcCommand.ExecutePrivilegedOperation when request.ZapretStart is not null =>
                IpcValidationResult.Invalid(
                    "UnexpectedZapretPayload",
                    "Общая привилегированная операция не должна содержать Zapret-параметры."),
            IpcCommand.ExecutePrivilegedOperation when request.Operation is null =>
                IpcValidationResult.Invalid(
                    "MissingOperation",
                    "Не указана привилегированная операция."),
            IpcCommand.ExecutePrivilegedOperation when !Enum.IsDefined(request.Operation.Value) =>
                IpcValidationResult.Invalid(
                    "UnknownOperation",
                    "Указана неизвестная привилегированная операция."),
            IpcCommand.ExecutePrivilegedOperation => IpcValidationResult.Valid(),

            IpcCommand.ZapretListStrategies => ValidateNoPayload(request, "ZapretListStrategies"),
            IpcCommand.ZapretGetStatus => ValidateNoPayload(request, "ZapretGetStatus"),
            IpcCommand.ZapretStop => ValidateNoPayload(request, "ZapretStop"),
            IpcCommand.ZapretStart => ValidateZapretStart(request),

            _ => IpcValidationResult.Invalid(
                "UnknownCommand",
                "Указана неизвестная IPC-команда.")
        };
    }

    private static IpcValidationResult ValidateNoPayload(IpcRequest request, string commandName)
    {
        if (request.Operation is not null || request.ZapretStart is not null)
        {
            return IpcValidationResult.Invalid(
                "UnexpectedPayload",
                $"Команда {commandName} не должна содержать дополнительные параметры.");
        }

        return IpcValidationResult.Valid();
    }

    private static IpcValidationResult ValidateZapretStart(IpcRequest request)
    {
        if (request.Operation is not null)
        {
            return IpcValidationResult.Invalid(
                "UnexpectedOperation",
                "ZapretStart не должен содержать общую привилегированную операцию.");
        }

        if (request.ZapretStart is null)
        {
            return IpcValidationResult.Invalid(
                "MissingZapretStart",
                "Не указан идентификатор стратегии Zapret.");
        }

        if (!StrategyIdRegex().IsMatch(request.ZapretStart.StrategyId))
        {
            return IpcValidationResult.Invalid(
                "InvalidStrategyId",
                "Идентификатор стратегии Zapret имеет недопустимый формат.");
        }

        return IpcValidationResult.Valid();
    }
}

public interface IPrivilegedServiceClient
{
    Task<IpcResponse> PingAsync(CancellationToken cancellationToken = default);

    Task<IpcResponse> RequestOperationAsync(
        NetPilot.Security.PrivilegedOperation operation,
        CancellationToken cancellationToken = default);

    Task<IpcResponse> ListZapretStrategiesAsync(CancellationToken cancellationToken = default);
    Task<IpcResponse> GetZapretStatusAsync(CancellationToken cancellationToken = default);
    Task<IpcResponse> StartZapretAsync(string strategyId, CancellationToken cancellationToken = default);
    Task<IpcResponse> StopZapretAsync(CancellationToken cancellationToken = default);
}
