using NetPilot.Automation;
using NetPilot.Ipc;

namespace NetPilot.Platform.Windows;

public sealed class WindowsZapretStrategyRuntime(
    IPrivilegedServiceClient serviceClient) : IStrategyRuntime
{
    public async Task<IReadOnlyList<StrategyDescriptor>> ListStrategiesAsync(
        CancellationToken cancellationToken = default)
    {
        var response = await serviceClient.ListZapretStrategiesAsync(cancellationToken);
        EnsureSuccess(response);

        if (response.Strategies is null)
        {
            throw new IpcProtocolException(
                "MissingStrategiesPayload",
                "Служба NetPilot не вернула каталог стратегий Zapret.");
        }

        return response.Strategies
            .Select(static strategy => new StrategyDescriptor(strategy.Id, strategy.DisplayName))
            .ToArray();
    }

    public async Task<StrategyRuntimeState> GetStateAsync(
        CancellationToken cancellationToken = default)
    {
        var response = await serviceClient.GetZapretStatusAsync(cancellationToken);
        EnsureSuccess(response);

        var status = response.ZapretStatus
            ?? throw new IpcProtocolException(
                "MissingZapretStatus",
                "Служба NetPilot не вернула состояние Zapret.");

        return status.State switch
        {
            IpcZapretProcessState.Stopped => StrategyRuntimeState.Stopped,
            IpcZapretProcessState.Running when !string.IsNullOrWhiteSpace(status.StrategyId) =>
                StrategyRuntimeState.Running(status.StrategyId),
            IpcZapretProcessState.Running => throw new IpcProtocolException(
                "MissingRunningStrategyId",
                "Служба скрыла или не вернула идентификатор запущенной стратегии Zapret."),
            _ => throw new IpcProtocolException(
                "UnknownZapretState",
                "Служба вернула неизвестное состояние Zapret.")
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
            var stopResponse = await serviceClient.StopZapretAsync(cancellationToken);
            EnsureSuccess(stopResponse);

            if (stopResponse.ZapretStatus is not
                {
                    State: IpcZapretProcessState.Stopped
                })
            {
                throw new IpcProtocolException(
                    "StopStateMismatch",
                    "После остановки служба не подтвердила состояние Zapret=Stopped.");
            }
        }

        var startResponse = await serviceClient.StartZapretStrategyAsync(
            strategyId,
            cancellationToken);
        EnsureSuccess(startResponse);

        if (startResponse.ZapretStatus is not
            {
                State: IpcZapretProcessState.Running,
                StrategyId: not null
            } started
            || !string.Equals(started.StrategyId, strategyId, StringComparison.Ordinal))
        {
            throw new IpcProtocolException(
                "StartStateMismatch",
                "После запуска служба не подтвердила ожидаемую стратегию Zapret.");
        }
    }

    private static void EnsureSuccess(IpcResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        if (!response.Success)
        {
            throw new StrategyRuntimeException(
                response.Code,
                response.Message ?? "Служба NetPilot отклонила операцию Zapret.");
        }
    }
}
