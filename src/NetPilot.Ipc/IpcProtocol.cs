using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NetPilot.Ipc;

public static class IpcProtocol
{
    public const int Version = 4;
    public const string WindowsPipeName = "NetPilot.Service.v4";
    public const int MaxMessageBytes = 64 * 1024;
    public const int ConnectTimeoutMilliseconds = 2_000;
    public const int RequestTimeoutMilliseconds = 5_000;
    public const int EngineInstallTimeoutMilliseconds = 120_000;
    public const int MaxJsonDepth = 16;
    public const int MaxStrategyIdCharacters = 64;
    public const int EnrollmentNonceCharacters = 64;

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
    EnrollAuthorizedUser,
    ZapretGetInstallationStatus,
    ZapretListStrategies,
    ZapretGetStatus,
    ZapretStartStrategy,
    ZapretStop,
    ZapretInstallLatest,
    ByeDpiGetStatus,
    ByeDpiStart,
    ByeDpiStop
}

public sealed record IpcRequest(
    int ProtocolVersion,
    Guid RequestId,
    IpcCommand Command,
    string? StrategyId = null,
    string? EnrollmentNonce = null)
{
    public static IpcRequest CreatePing() =>
        new(IpcProtocol.Version, Guid.NewGuid(), IpcCommand.Ping);

    public static IpcRequest CreateEnrollment(string enrollmentNonce) =>
        new(
            IpcProtocol.Version,
            Guid.NewGuid(),
            IpcCommand.EnrollAuthorizedUser,
            EnrollmentNonce: enrollmentNonce);

    public static IpcRequest CreateZapretGetInstallationStatus() =>
        new(IpcProtocol.Version, Guid.NewGuid(), IpcCommand.ZapretGetInstallationStatus);

    public static IpcRequest CreateZapretListStrategies() =>
        new(IpcProtocol.Version, Guid.NewGuid(), IpcCommand.ZapretListStrategies);

    public static IpcRequest CreateZapretGetStatus() =>
        new(IpcProtocol.Version, Guid.NewGuid(), IpcCommand.ZapretGetStatus);

    public static IpcRequest CreateZapretStartStrategy(string strategyId) =>
        new(IpcProtocol.Version, Guid.NewGuid(), IpcCommand.ZapretStartStrategy, strategyId);

    public static IpcRequest CreateZapretStop() =>
        new(IpcProtocol.Version, Guid.NewGuid(), IpcCommand.ZapretStop);

    public static IpcRequest CreateZapretInstallLatest() =>
        new(IpcProtocol.Version, Guid.NewGuid(), IpcCommand.ZapretInstallLatest);

    public static IpcRequest CreateByeDpiGetStatus() =>
        new(IpcProtocol.Version, Guid.NewGuid(), IpcCommand.ByeDpiGetStatus);

    public static IpcRequest CreateByeDpiStart() =>
        new(IpcProtocol.Version, Guid.NewGuid(), IpcCommand.ByeDpiStart);

    public static IpcRequest CreateByeDpiStop() =>
        new(IpcProtocol.Version, Guid.NewGuid(), IpcCommand.ByeDpiStop);
}

public enum IpcZapretProcessState
{
    Stopped,
    Running
}

public enum IpcByeDpiProcessState
{
    Stopped,
    Running
}

public sealed record IpcZapretStrategy(
    string Id,
    string DisplayName);

public sealed record IpcZapretStatus(
    IpcZapretProcessState State,
    string? StrategyId = null);

/// <summary>
/// Deliberately minimal ByeDPI status. Process id, listener port and owner SID are not
/// exposed through user IPC because clients do not need them to control NetPilot safely.
/// </summary>
public sealed record IpcByeDpiStatus(IpcByeDpiProcessState State);

public sealed record IpcZapretInstallationStatus(
    bool IsInstalled,
    string? Version = null);

public sealed record IpcZapretInstallResult(
    string Version,
    bool Installed,
    bool TemporaryCleanupCompleted);

public sealed record IpcResponse(
    int ProtocolVersion,
    Guid RequestId,
    bool Success,
    string Code,
    string? Message = null,
    IReadOnlyList<IpcZapretStrategy>? Strategies = null,
    IpcZapretStatus? ZapretStatus = null,
    IpcZapretInstallationStatus? ZapretInstallation = null,
    IpcZapretInstallResult? ZapretInstall = null,
    IpcByeDpiStatus? ByeDpiStatus = null);

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
