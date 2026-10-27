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
            IpcCommand.Ping when request.Operation is null => IpcValidationResult.Valid(),
            IpcCommand.Ping => IpcValidationResult.Invalid(
                "UnexpectedOperation",
                "Команда Ping не должна содержать привилегированную операцию."),
            IpcCommand.ExecutePrivilegedOperation when request.Operation is null =>
                IpcValidationResult.Invalid(
                    "MissingOperation",
                    "Не указана привилегированная операция."),
            IpcCommand.ExecutePrivilegedOperation when !Enum.IsDefined(request.Operation.Value) =>
                IpcValidationResult.Invalid(
                    "UnknownOperation",
                    "Указана неизвестная привилегированная операция."),
            IpcCommand.ExecutePrivilegedOperation => IpcValidationResult.Valid(),
            _ => IpcValidationResult.Invalid(
                "UnknownCommand",
                "Указана неизвестная IPC-команда.")
        };
    }
}

public interface IPrivilegedServiceClient
{
    Task<IpcResponse> PingAsync(CancellationToken cancellationToken = default);

    Task<IpcResponse> RequestOperationAsync(
        NetPilot.Security.PrivilegedOperation operation,
        CancellationToken cancellationToken = default);
}
