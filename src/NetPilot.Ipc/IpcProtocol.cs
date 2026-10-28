using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Serialization;
using NetPilot.Security;

namespace NetPilot.Ipc;

public static class IpcProtocol
{
    public const int Version = 1;
    public const string WindowsPipeName = "NetPilot.Service.v1";
    public const int MaxMessageBytes = 64 * 1024;
    public const int ConnectTimeoutMilliseconds = 2_000;
    public const int RequestTimeoutMilliseconds = 5_000;
    public const int MaxJsonDepth = 16;

    internal static JsonSerializerOptions JsonOptions { get; } = CreateJsonOptions();

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = false,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            MaxDepth = MaxJsonDepth
        };

        options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        return options;
    }
}

public enum IpcCommand
{
    Ping,
    ExecutePrivilegedOperation
}

public sealed record IpcRequest(
    int ProtocolVersion,
    Guid RequestId,
    IpcCommand Command,
    PrivilegedOperation? Operation)
{
    public static IpcRequest CreatePing() =>
        new(IpcProtocol.Version, Guid.NewGuid(), IpcCommand.Ping, null);

    public static IpcRequest CreateOperation(PrivilegedOperation operation) =>
        new(IpcProtocol.Version, Guid.NewGuid(), IpcCommand.ExecutePrivilegedOperation, operation);
}

public sealed record IpcResponse(
    int ProtocolVersion,
    Guid RequestId,
    bool Success,
    string Code,
    string? Message = null);

public sealed class IpcProtocolException(string code, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public string Code { get; } = code;
}

public static class IpcFraming
{
    public static async Task WriteAsync<T>(
        Stream stream,
        T message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(message);

        var payload = JsonSerializer.SerializeToUtf8Bytes(message, IpcProtocol.JsonOptions);
        if (payload.Length is <= 0 or > IpcProtocol.MaxMessageBytes)
        {
            throw new IpcProtocolException(
                "MessageTooLarge",
                $"Размер IPC-сообщения должен быть от 1 до {IpcProtocol.MaxMessageBytes} байт.");
        }

        var header = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);

        await stream.WriteAsync(header, cancellationToken);
        await stream.WriteAsync(payload, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    public static async Task<T> ReadAsync<T>(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var header = new byte[sizeof(int)];
        await stream.ReadExactlyAsync(header, cancellationToken);

        var payloadLength = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (payloadLength is <= 0 or > IpcProtocol.MaxMessageBytes)
        {
            throw new IpcProtocolException(
                "InvalidMessageLength",
                "Получено IPC-сообщение недопустимого размера.");
        }

        var payload = GC.AllocateUninitializedArray<byte>(payloadLength);
        await stream.ReadExactlyAsync(payload, cancellationToken);

        try
        {
            return JsonSerializer.Deserialize<T>(payload, IpcProtocol.JsonOptions)
                ?? throw new IpcProtocolException("EmptyMessage", "IPC-сообщение не содержит данных.");
        }
        catch (JsonException exception)
        {
            throw new IpcProtocolException(
                "InvalidJson",
                "IPC-сообщение не соответствует ожидаемой схеме.",
                exception);
        }
    }
}
