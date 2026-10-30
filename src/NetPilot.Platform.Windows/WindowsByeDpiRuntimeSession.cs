namespace NetPilot.Platform.Windows;

/// <summary>
/// Service-side source of a fully verified and compiled ByeDPI launch plan.
/// The contract accepts no path, argv, URL, environment value or user-controlled data.
/// A concrete admitted provider may exist only after a separate runtime/admission decision.
/// </summary>
public interface IWindowsByeDpiRuntimePlanProvider
{
    WindowsCompiledEngineArgumentPlan CreatePlan();
}

/// <summary>
/// Production default while ByeDPI runtime admission is BLOCK.
/// It cannot manufacture a launch plan under any input because the contract has no input.
/// </summary>
public sealed class NoAdmittedWindowsByeDpiRuntimePlanProvider
    : IWindowsByeDpiRuntimePlanProvider
{
    public static NoAdmittedWindowsByeDpiRuntimePlanProvider Instance { get; } = new();

    private NoAdmittedWindowsByeDpiRuntimePlanProvider()
    {
    }

    public WindowsCompiledEngineArgumentPlan CreatePlan() =>
        throw new WindowsServiceOwnedEngineProcessException(
            "NoAdmittedByeDpiPlan",
            "ByeDPI launch plan недоступен: production runtime admission остаётся BLOCK.");
}

/// <summary>
/// Thin service integration boundary around the already bounded process controller.
/// Status and stop remain available for cleanup/reconciliation. Start requires a plan
/// from IWindowsByeDpiRuntimePlanProvider, so production composition remains fail-closed
/// until an explicitly admitted provider replaces the no-admit implementation.
/// </summary>
public sealed class WindowsByeDpiRuntimeSession(
    IWindowsByeDpiRuntimePlanProvider planProvider,
    WindowsByeDpiServiceOwnedProcessController controller)
{
    public Task<WindowsServiceOwnedEngineProcessSnapshot> GetStatusAsync(
        CancellationToken cancellationToken) =>
        controller.GetStatusAsync(cancellationToken);

    public async Task<WindowsServiceOwnedEngineProcessSnapshot> StartAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var plan = planProvider.CreatePlan();
        return await controller.StartAsync(plan, cancellationToken);
    }

    public Task<WindowsServiceOwnedEngineProcessSnapshot> StopAsync(
        CancellationToken cancellationToken) =>
        controller.StopAsync(cancellationToken);
}
