using NetPilot.Automation;
using NetPilot.Ipc;

namespace NetPilot.Platform.Windows;

public sealed class WindowsZapretStrategyRuntime(
    IPrivilegedServiceClient serviceClient) : IStrategyRuntime
{
    public async Task<IReadOnlyList<StrategyDescriptor>> ListStrategiesAsync(
        CancellationToken cancellationToken = default)
    {
        var response = await ExecuteIpcAsync(
            () => serviceClient.ListZapretStrategiesAsync(cancellationToken));
        EnsureSuccess(response);

        if (response.Strategies is null)
        {
            throw new StrategyRuntimeException(
                "MissingStrategiesPayload",
                "Служба NetPilot не вернула каталог сетевых профилей.");
        }

        return response.Strategies
            .Select(static strategy => new StrategyDescriptor(strategy.Id, strategy.DisplayName))
            .ToArray();
    }

    public async Task<StrategyRuntimeState> GetStateAsync(
        CancellationToken cancellationToken = default)
    {
        var response = await ExecuteIpcAsync(
            () => serviceClient.GetZapretStatusAsync(cancellationToken));
        EnsureSuccess(response);

        var status = response.ZapretStatus
            ?? throw new StrategyRuntimeException(
                "MissingEngineStatus",
                "Служба NetPilot не вернула состояние сетевого движка.");

        return status.State switch
        {
            IpcZapretProcessState.Stopped => StrategyRuntimeState.Stopped,
            IpcZapretProcessState.Running when !string.IsNullOrWhiteSpace(status.StrategyId) =>
                StrategyRuntimeState.Running(status.StrategyId),
            IpcZapretProcessState.Running => throw new StrategyRuntimeException(
                "MissingRunningStrategyId",
                "Служба не вернула идентификатор активного сетевого профиля."),
            _ => throw new StrategyRuntimeException(
                "UnknownEngineState",
                "Служба вернула неизвестное состояние сетевого движка.")
        };
    }

    public async Task ActivateAsync(
        string strategyId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(strategyId);

        var current = await GetStateAsync(cancellationToken);
        if (current.IsRunning
            && string.Equals(current.StrategyId, strategyId, StringComparison.Ordinal))
        {
            return;
        }

        if (current.IsRunning)
        {
            var stopResponse = await ExecuteIpcAsync(
                () => serviceClient.StopZapretAsync(cancellationToken));
            EnsureSuccess(stopResponse);

            if (stopResponse.ZapretStatus is not
                {
                    State: IpcZapretProcessState.Stopped
                })
            {
                throw new StrategyRuntimeException(
                    "StopStateMismatch",
                    "После остановки служба не подтвердила остановленное состояние сетевого движка.");
            }
        }

        var startResponse = await ExecuteIpcAsync(
            () => serviceClient.StartZapretStrategyAsync(strategyId, cancellationToken));
        EnsureSuccess(startResponse);

        if (startResponse.ZapretStatus is not
            {
                State: IpcZapretProcessState.Running,
                StrategyId: not null
            } started
            || !string.Equals(started.StrategyId, strategyId, StringComparison.Ordinal))
        {
            throw new StrategyRuntimeException(
                "StartStateMismatch",
                "После запуска служба не подтвердила ожидаемый сетевой профиль.");
        }
    }

    private static async Task<IpcResponse> ExecuteIpcAsync(Func<Task<IpcResponse>> operation)
    {
        try
        {
            return await operation();
        }
        catch (IpcProtocolException exception)
        {
            throw new StrategyRuntimeException(
                exception.Code,
                exception.Message,
                exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new StrategyRuntimeException(
                "ServiceAccessDenied",
                "Windows не разрешила доступ к локальной службе NetPilot.",
                exception);
        }
        catch (IOException exception)
        {
            throw new StrategyRuntimeException(
                "ServiceTransportError",
                "Не удалось безопасно обменяться данными с локальной службой NetPilot.",
                exception);
        }
    }

    private static void EnsureSuccess(IpcResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        if (!response.Success)
        {
            throw new StrategyRuntimeException(
                response.Code,
                response.Message ?? "Служба NetPilot отклонила операцию сетевого движка.");
        }
    }
}
