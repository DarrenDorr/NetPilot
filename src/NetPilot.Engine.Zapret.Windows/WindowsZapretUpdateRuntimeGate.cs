using NetPilot.Updater;

namespace NetPilot.Engine.Zapret.Windows;

public sealed class WindowsZapretUpdateRuntimeGate(
    ZapretEngineController controller) : IZapretUpdateRuntimeGate
{
    public async Task<ZapretUpdateRuntimeState> CaptureAndStopAsync(
        CancellationToken cancellationToken = default)
    {
        var snapshot = await controller.GetStatusAsync(cancellationToken);
        if (snapshot.State == ZapretProcessState.Stopped)
        {
            return ZapretUpdateRuntimeState.Stopped;
        }

        if (string.IsNullOrWhiteSpace(snapshot.StrategyId))
        {
            throw new UpdateSecurityException(
                "InvalidRuntimeState",
                "Запущенный Zapret не сообщает точный идентификатор активной стратегии.");
        }

        var state = ZapretUpdateRuntimeState.Running(snapshot.StrategyId);
        var stopped = await controller.StopAsync(cancellationToken);

        if (stopped.State != ZapretProcessState.Stopped)
        {
            throw new UpdateSecurityException(
                "RuntimeStopFailed",
                "Не удалось подтвердить остановку Zapret перед переключением версии.");
        }

        return state;
    }

    public async Task<ZapretRuntimeRestoreResult> RestoreAsync(
        ZapretUpdateRuntimeState state,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (!state.WasRunning)
        {
            return ZapretRuntimeRestoreResult.NotRequired;
        }

        if (string.IsNullOrWhiteSpace(state.StrategyId))
        {
            return ZapretRuntimeRestoreResult.Failed(
                "UNKNOWN",
                "InvalidRuntimeState");
        }

        var strategyId = state.StrategyId;

        try
        {
            var exactStrategy = controller.ListStrategies()
                .SingleOrDefault(strategy =>
                    string.Equals(strategy.Id, strategyId, StringComparison.Ordinal));

            if (exactStrategy is null)
            {
                return ZapretRuntimeRestoreResult.Failed(
                    strategyId,
                    "StrategyUnavailableOrIncompatibleAfterUpdate");
            }

            var restored = await controller.StartAsync(strategyId, cancellationToken);
            if (restored.State != ZapretProcessState.Running
                || !string.Equals(restored.StrategyId, strategyId, StringComparison.Ordinal))
            {
                return ZapretRuntimeRestoreResult.Failed(
                    strategyId,
                    "RuntimeRestoreVerificationFailed");
            }

            return ZapretRuntimeRestoreResult.Restored(strategyId);
        }
        catch (ZapretProcessRunnerException exception)
        {
            return ZapretRuntimeRestoreResult.Failed(strategyId, exception.Code);
        }
        catch (InvalidOperationException)
        {
            return ZapretRuntimeRestoreResult.Failed(
                strategyId,
                "AmbiguousStrategyAfterUpdate");
        }
        catch (IOException)
        {
            return ZapretRuntimeRestoreResult.Failed(
                strategyId,
                "RuntimeRestoreIoFailure");
        }
        catch (UnauthorizedAccessException)
        {
            return ZapretRuntimeRestoreResult.Failed(
                strategyId,
                "RuntimeRestoreAccessDenied");
        }
    }
}
