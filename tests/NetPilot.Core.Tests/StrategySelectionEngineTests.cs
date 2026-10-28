using NetPilot.Automation;
using NetPilot.Core.Models;

namespace NetPilot.Core.Tests;

public sealed class StrategySelectionEngineTests
{
    private static readonly ResourceDefinition Discord = new(
        "discord",
        "Discord",
        [new Uri("https://discord.com/")],
        []);

    private static readonly ResourceDefinition YouTube = new(
        "youtube",
        "YouTube",
        [new Uri("https://www.youtube.com/generate_204")],
        []);

    private static readonly IReadOnlyList<ResourceDefinition> Resources = [Discord, YouTube];

    [Fact]
    public async Task Healthy_current_strategy_is_kept_without_switching()
    {
        var runtime = new FakeRuntime("ALT11", "ALT11", "ALT12");
        var probe = new ScriptedProbe(runtime, static (_, _) => Healthy());
        var engine = CreateEngine(runtime, probe);

        var result = await engine.EvaluateAndRecoverAsync(
            Resources,
            TestContext.Current.CancellationToken);

        Assert.Equal(StrategySelectionStatus.CurrentHealthy, result.Status);
        Assert.Equal("ALT11", result.SelectedStrategyId);
        Assert.Empty(runtime.Activations);
    }

    [Fact]
    public async Task One_transient_failure_does_not_switch_strategy()
    {
        var runtime = new FakeRuntime("ALT11", "ALT11", "ALT12");
        var calls = 0;
        var probe = new ScriptedProbe(runtime, (_, _) =>
        {
            calls++;
            return calls <= Resources.Count
                ? Unavailable()
                : Healthy();
        });
        var engine = CreateEngine(runtime, probe);

        var result = await engine.EvaluateAndRecoverAsync(
            Resources,
            TestContext.Current.CancellationToken);

        Assert.Equal(StrategySelectionStatus.TransientFailure, result.Status);
        Assert.Equal("ALT11", runtime.ActiveStrategyId);
        Assert.Empty(runtime.Activations);
    }

    [Fact]
    public async Task Persistent_failure_selects_best_confirmed_candidate()
    {
        var runtime = new FakeRuntime("ALT11", "ALT11", "ALT12", "ALT13");
        var probe = new ScriptedProbe(runtime, static (strategy, _) => strategy switch
        {
            "ALT11" => Unavailable(),
            "ALT12" => Healthy(80),
            "ALT13" => Healthy(450),
            _ => Unavailable()
        });
        var engine = CreateEngine(runtime, probe);

        var result = await engine.EvaluateAndRecoverAsync(
            Resources,
            TestContext.Current.CancellationToken);

        Assert.Equal(StrategySelectionStatus.Switched, result.Status);
        Assert.Equal("ALT12", result.SelectedStrategyId);
        Assert.Equal("ALT12", runtime.ActiveStrategyId);
        Assert.Equal(["ALT12", "ALT13", "ALT12"], runtime.Activations);
    }

    [Fact]
    public async Task Candidate_that_fails_final_confirmation_is_not_selected()
    {
        var runtime = new FakeRuntime("ALT11", "ALT11", "ALT12", "ALT13");
        var alt12Calls = 0;
        var probe = new ScriptedProbe(runtime, (strategy, _) => strategy switch
        {
            "ALT11" => Unavailable(),
            "ALT12" => ++alt12Calls <= Resources.Count
                ? Healthy(30)
                : Unavailable(),
            "ALT13" => Healthy(120),
            _ => Unavailable()
        });
        var engine = CreateEngine(runtime, probe);

        var result = await engine.EvaluateAndRecoverAsync(
            Resources,
            TestContext.Current.CancellationToken);

        Assert.Equal(StrategySelectionStatus.Switched, result.Status);
        Assert.Equal("ALT13", result.SelectedStrategyId);
        Assert.Equal("ALT13", runtime.ActiveStrategyId);
    }

    [Fact]
    public async Task No_working_candidate_restores_original_strategy()
    {
        var runtime = new FakeRuntime("ALT11", "ALT11", "ALT12", "ALT13");
        var probe = new ScriptedProbe(runtime, static (_, _) => Unavailable());
        var engine = CreateEngine(runtime, probe);

        var result = await engine.EvaluateAndRecoverAsync(
            Resources,
            TestContext.Current.CancellationToken);

        Assert.Equal(StrategySelectionStatus.NoWorkingAlternative, result.Status);
        Assert.Equal("ALT11", result.SelectedStrategyId);
        Assert.Equal("ALT11", runtime.ActiveStrategyId);
        Assert.Equal("ALT11", runtime.Activations[^1]);
    }

    [Fact]
    public async Task Automatic_run_tests_no_more_than_six_alternatives_by_default()
    {
        var strategies = Enumerable.Range(11, 9)
            .Select(static number => $"ALT{number}")
            .ToArray();
        var runtime = new FakeRuntime("ALT11", strategies);
        var probe = new ScriptedProbe(runtime, static (_, _) => Unavailable());
        var engine = CreateEngine(runtime, probe);

        var result = await engine.EvaluateAndRecoverAsync(
            Resources,
            TestContext.Current.CancellationToken);

        Assert.Equal(StrategySelectionStatus.NoWorkingAlternative, result.Status);
        Assert.Equal(7, runtime.Activations.Count); // 6 candidates + restore ALT11.
        Assert.DoesNotContain("ALT18", runtime.Activations);
        Assert.DoesNotContain("ALT19", runtime.Activations);
    }

    [Fact]
    public async Task Missing_original_strategy_in_catalog_prevents_disruptive_testing()
    {
        var runtime = new FakeRuntime("ALT11", "ALT12", "ALT13");
        var probe = new ScriptedProbe(runtime, static (_, _) => Unavailable());
        var engine = CreateEngine(runtime, probe);

        var result = await engine.EvaluateAndRecoverAsync(
            Resources,
            TestContext.Current.CancellationToken);

        Assert.Equal(StrategySelectionStatus.NoWorkingAlternative, result.Status);
        Assert.Equal("OriginalStrategyUnavailable", result.DiagnosticCode);
        Assert.Empty(runtime.Activations);
        Assert.Equal("ALT11", runtime.ActiveStrategyId);
    }

    [Fact]
    public async Task Stopped_runtime_is_never_started_implicitly()
    {
        var runtime = new FakeRuntime(null, "ALT11", "ALT12");
        var probe = new ScriptedProbe(runtime, static (_, _) => Healthy());
        var engine = CreateEngine(runtime, probe);

        var result = await engine.EvaluateAndRecoverAsync(
            Resources,
            TestContext.Current.CancellationToken);

        Assert.Equal(StrategySelectionStatus.NotRunning, result.Status);
        Assert.Empty(runtime.Activations);
    }

    private static StrategySelectionEngine CreateEngine(
        FakeRuntime runtime,
        ScriptedProbe probe) =>
        new(
            runtime,
            probe,
            new StrategySelectionOptions
            {
                ConfirmationDelay = TimeSpan.Zero,
                PostSwitchSettleDelay = TimeSpan.Zero
            });

    private static ResourceHealth Healthy(int milliseconds = 50) =>
        new(
            "resource",
            ResourceHealthState.Healthy,
            DateTimeOffset.UtcNow,
            TimeSpan.FromMilliseconds(milliseconds));

    private static ResourceHealth Unavailable() =>
        new(
            "resource",
            ResourceHealthState.Unavailable,
            DateTimeOffset.UtcNow,
            null,
            "NetworkError");

    private sealed class FakeRuntime : IStrategyRuntime
    {
        private readonly IReadOnlyList<StrategyDescriptor> _strategies;

        public FakeRuntime(string? activeStrategyId, params string[] strategies)
        {
            ActiveStrategyId = activeStrategyId;
            _strategies = strategies
                .Select(static id => new StrategyDescriptor(id, id))
                .ToArray();
        }

        public string? ActiveStrategyId { get; private set; }
        public List<string> Activations { get; } = [];

        public Task<IReadOnlyList<StrategyDescriptor>> ListStrategiesAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_strategies);
        }

        public Task<StrategyRuntimeState> GetStateAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(
                ActiveStrategyId is null
                    ? StrategyRuntimeState.Stopped
                    : StrategyRuntimeState.Running(ActiveStrategyId));
        }

        public Task ActivateAsync(
            string strategyId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!_strategies.Any(strategy =>
                    string.Equals(strategy.Id, strategyId, StringComparison.Ordinal)))
            {
                throw new StrategyRuntimeException(
                    "StrategyNotFound",
                    "Стратегия не найдена.");
            }

            Activations.Add(strategyId);
            ActiveStrategyId = strategyId;
            return Task.CompletedTask;
        }
    }

    private sealed class ScriptedProbe(
        FakeRuntime runtime,
        Func<string, string, ResourceHealth> resultFactory) : IResourceHealthProbe
    {
        public Task<ResourceHealth> CheckAsync(
            ResourceDefinition resource,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var strategy = runtime.ActiveStrategyId
                ?? throw new InvalidOperationException("Runtime is stopped.");
            var template = resultFactory(strategy, resource.Id);
            return Task.FromResult(template with { ResourceId = resource.Id });
        }
    }
}
