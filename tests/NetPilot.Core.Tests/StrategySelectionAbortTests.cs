using NetPilot.Automation;
using NetPilot.Core.Models;

namespace NetPilot.Core.Tests;

public sealed class StrategySelectionAbortTests
{
    [Fact]
    public async Task Cancellation_after_candidate_activation_restores_original_strategy()
    {
        var runtime = new FakeRuntime();
        var probe = new CancellingProbe(runtime);
        var engine = new StrategySelectionEngine(
            runtime,
            probe,
            new StrategySelectionOptions
            {
                ConfirmationDelay = TimeSpan.Zero,
                PostSwitchSettleDelay = TimeSpan.Zero
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => engine.EvaluateAndRecoverAsync(
                [
                    new ResourceDefinition(
                        "discord",
                        "Discord",
                        [new Uri("https://discord.com/")],
                        [])
                ],
                TestContext.Current.CancellationToken));

        Assert.Equal("ALT11", runtime.ActiveStrategyId);
        Assert.Equal(["ALT12", "ALT11"], runtime.Activations);
    }

    private sealed class FakeRuntime : IStrategyRuntime
    {
        private static readonly IReadOnlyList<StrategyDescriptor> Strategies =
        [
            new("ALT11", "ALT11"),
            new("ALT12", "ALT12")
        ];

        public string ActiveStrategyId { get; private set; } = "ALT11";
        public List<string> Activations { get; } = [];

        public Task<IReadOnlyList<StrategyDescriptor>> ListStrategiesAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Strategies);

        public Task<StrategyRuntimeState> GetStateAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(StrategyRuntimeState.Running(ActiveStrategyId));

        public Task ActivateAsync(
            string strategyId,
            CancellationToken cancellationToken = default)
        {
            // Cleanup with CancellationToken.None must remain possible after the original
            // operation has been cancelled.
            if (cancellationToken.CanBeCanceled)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            Activations.Add(strategyId);
            ActiveStrategyId = strategyId;
            return Task.CompletedTask;
        }
    }

    private sealed class CancellingProbe(FakeRuntime runtime) : IResourceHealthProbe
    {
        private int _originalChecks;

        public Task<ResourceHealth> CheckAsync(
            ResourceDefinition resource,
            CancellationToken cancellationToken = default)
        {
            if (runtime.ActiveStrategyId == "ALT11")
            {
                _originalChecks++;
                return Task.FromResult(new ResourceHealth(
                    resource.Id,
                    ResourceHealthState.Unavailable,
                    DateTimeOffset.UtcNow,
                    DiagnosticCode: "NetworkError"));
            }

            throw new OperationCanceledException(cancellationToken);
        }
    }
}
