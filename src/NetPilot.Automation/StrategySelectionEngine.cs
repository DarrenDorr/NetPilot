using NetPilot.Core.Models;

namespace NetPilot.Automation;

public sealed class StrategySelectionEngine
{
    private const int MaxResourcesPerRun = 16;

    private readonly IStrategyRuntime _runtime;
    private readonly IResourceHealthProbe _healthProbe;
    private readonly StrategySelectionOptions _options;

    public StrategySelectionEngine(
        IStrategyRuntime runtime,
        IResourceHealthProbe healthProbe,
        StrategySelectionOptions? options = null)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _healthProbe = healthProbe ?? throw new ArgumentNullException(nameof(healthProbe));
        _options = options ?? new StrategySelectionOptions();
        _options.Validate();
    }

    public async Task<StrategySelectionResult> EvaluateAndRecoverAsync(
        IReadOnlyList<ResourceDefinition> resources,
        CancellationToken cancellationToken = default)
    {
        ValidateResources(resources);

        var runtimeState = await _runtime.GetStateAsync(cancellationToken);
        if (!runtimeState.IsRunning || string.IsNullOrWhiteSpace(runtimeState.StrategyId))
        {
            return new StrategySelectionResult(
                StrategySelectionStatus.NotRunning,
                null,
                null,
                []);
        }

        var originalStrategyId = runtimeState.StrategyId;
        var strategies = await _runtime.ListStrategiesAsync(cancellationToken);
        ValidateStrategyCatalog(strategies);

        if (!strategies.Any(strategy =>
                string.Equals(strategy.Id, originalStrategyId, StringComparison.Ordinal)))
        {
            return new StrategySelectionResult(
                StrategySelectionStatus.NoWorkingAlternative,
                originalStrategyId,
                originalStrategyId,
                [],
                "OriginalStrategyUnavailable");
        }

        var evaluations = new List<StrategyEvaluation>();

        for (var attempt = 0; attempt < _options.FailureConfirmationAttempts; attempt++)
        {
            var currentEvaluation = await EvaluateStrategyAsync(
                originalStrategyId,
                resources,
                cancellationToken);
            evaluations.Add(currentEvaluation);

            if (currentEvaluation.IsViable)
            {
                return new StrategySelectionResult(
                    attempt == 0
                        ? StrategySelectionStatus.CurrentHealthy
                        : StrategySelectionStatus.TransientFailure,
                    originalStrategyId,
                    originalStrategyId,
                    evaluations);
            }

            if (attempt + 1 < _options.FailureConfirmationAttempts)
            {
                await DelayAsync(_options.ConfirmationDelay, cancellationToken);
            }
        }

        var candidates = strategies
            .Where(strategy =>
                !string.Equals(strategy.Id, originalStrategyId, StringComparison.Ordinal))
            .Take(_options.MaxAlternativeCandidates)
            .ToArray();

        if (candidates.Length == 0)
        {
            return new StrategySelectionResult(
                StrategySelectionStatus.NoWorkingAlternative,
                originalStrategyId,
                originalStrategyId,
                evaluations,
                "NoAlternativeStrategies");
        }

        var viableCandidates = new List<StrategyEvaluation>();
        string? activeStrategyId = originalStrategyId;

        try
        {
            foreach (var candidate in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    await _runtime.ActivateAsync(candidate.Id, cancellationToken);
                    activeStrategyId = candidate.Id;
                }
                catch (StrategyRuntimeException exception)
                {
                    evaluations.Add(new StrategyEvaluation(
                        candidate.Id,
                        0,
                        false,
                        null,
                        [],
                        $"Activation:{exception.Code}"));
                    continue;
                }

                await DelayAsync(_options.PostSwitchSettleDelay, cancellationToken);

                var evaluation = await EvaluateStrategyAsync(
                    candidate.Id,
                    resources,
                    cancellationToken);
                evaluations.Add(evaluation);

                if (evaluation.IsViable)
                {
                    viableCandidates.Add(evaluation);
                }
            }

            foreach (var candidate in viableCandidates
                         .OrderByDescending(static candidate => candidate.Score)
                         .ThenBy(static candidate => candidate.AverageLatency ?? TimeSpan.MaxValue)
                         .ThenBy(static candidate => candidate.StrategyId, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    if (!string.Equals(activeStrategyId, candidate.StrategyId, StringComparison.Ordinal))
                    {
                        await _runtime.ActivateAsync(candidate.StrategyId, cancellationToken);
                        activeStrategyId = candidate.StrategyId;
                        await DelayAsync(_options.PostSwitchSettleDelay, cancellationToken);
                    }
                }
                catch (StrategyRuntimeException exception)
                {
                    evaluations.Add(candidate with
                    {
                        IsViable = false,
                        DiagnosticCode = $"FinalActivation:{exception.Code}"
                    });
                    continue;
                }

                var confirmation = await EvaluateStrategyAsync(
                    candidate.StrategyId,
                    resources,
                    cancellationToken);
                evaluations.Add(confirmation);

                if (confirmation.IsViable)
                {
                    return new StrategySelectionResult(
                        StrategySelectionStatus.Switched,
                        originalStrategyId,
                        candidate.StrategyId,
                        evaluations);
                }
            }

            try
            {
                if (!string.Equals(activeStrategyId, originalStrategyId, StringComparison.Ordinal))
                {
                    await _runtime.ActivateAsync(originalStrategyId, cancellationToken);
                    activeStrategyId = originalStrategyId;
                }

                return new StrategySelectionResult(
                    StrategySelectionStatus.NoWorkingAlternative,
                    originalStrategyId,
                    originalStrategyId,
                    evaluations,
                    "NoConfirmedHealthyAlternative");
            }
            catch (StrategyRuntimeException exception)
            {
                return new StrategySelectionResult(
                    StrategySelectionStatus.OriginalRestoreFailed,
                    originalStrategyId,
                    activeStrategyId,
                    evaluations,
                    $"Restore:{exception.Code}");
            }
        }
        catch (Exception originalException)
        {
            if (!string.Equals(activeStrategyId, originalStrategyId, StringComparison.Ordinal))
            {
                try
                {
                    await _runtime.ActivateAsync(originalStrategyId, CancellationToken.None);
                }
                catch (Exception restoreException)
                {
                    throw new StrategyRuntimeException(
                        "AbortRestoreFailed",
                        "Автоматический подбор был прерван, и исходную стратегию восстановить не удалось.",
                        new AggregateException(originalException, restoreException));
                }
            }

            throw;
        }
    }

    private async Task<StrategyEvaluation> EvaluateStrategyAsync(
        string strategyId,
        IReadOnlyList<ResourceDefinition> resources,
        CancellationToken cancellationToken)
    {
        var results = new List<ResourceHealth>(resources.Count);

        foreach (var resource in resources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(await _healthProbe.CheckAsync(resource, cancellationToken));
        }

        var isViable = results.All(static result => result.State == ResourceHealthState.Healthy);
        var averageLatency = CalculateAverageLatency(results);
        var score = CalculateScore(results, averageLatency);

        return new StrategyEvaluation(
            strategyId,
            score,
            isViable,
            averageLatency,
            results);
    }

    private static int CalculateScore(
        IReadOnlyList<ResourceHealth> results,
        TimeSpan? averageLatency)
    {
        var healthScore = results.Average(static result => result.State switch
        {
            ResourceHealthState.Healthy => 100d,
            ResourceHealthState.Degraded => 45d,
            ResourceHealthState.Checking => 20d,
            ResourceHealthState.Unknown => 10d,
            ResourceHealthState.Unavailable => 0d,
            _ => 0d
        });

        var latencyPenalty = averageLatency is null
            ? 0d
            : Math.Min(15d, averageLatency.Value.TotalMilliseconds / 100d);

        return (int)Math.Clamp(
            Math.Round(healthScore - latencyPenalty, MidpointRounding.AwayFromZero),
            0d,
            100d);
    }

    private static TimeSpan? CalculateAverageLatency(IReadOnlyList<ResourceHealth> results)
    {
        var latencyTicks = results
            .Where(static result => result.Latency.HasValue)
            .Select(static result => result.Latency!.Value.Ticks)
            .ToArray();

        return latencyTicks.Length == 0
            ? null
            : TimeSpan.FromTicks((long)latencyTicks.Average());
    }

    private static async Task DelayAsync(
        TimeSpan delay,
        CancellationToken cancellationToken)
    {
        if (delay > TimeSpan.Zero)
        {
            await Task.Delay(delay, cancellationToken);
        }
    }

    private static void ValidateResources(IReadOnlyList<ResourceDefinition> resources)
    {
        ArgumentNullException.ThrowIfNull(resources);

        if (resources.Count is < 1 or > MaxResourcesPerRun)
        {
            throw new ArgumentOutOfRangeException(
                nameof(resources),
                $"За один автоматический прогон разрешено от 1 до {MaxResourcesPerRun} ресурсов.");
        }

        if (resources.Any(static resource => resource is null))
        {
            throw new ArgumentException("Список ресурсов содержит null.", nameof(resources));
        }

        if (resources
            .GroupBy(static resource => resource.Id, StringComparer.Ordinal)
            .Any(static group => group.Count() > 1))
        {
            throw new ArgumentException("Идентификаторы ресурсов должны быть уникальными.", nameof(resources));
        }
    }

    private static void ValidateStrategyCatalog(IReadOnlyList<StrategyDescriptor> strategies)
    {
        ArgumentNullException.ThrowIfNull(strategies);

        if (strategies.Count > 128)
        {
            throw new StrategyRuntimeException(
                "StrategyCatalogTooLarge",
                "Каталог стратегий превышает допустимый предел автоматизации.");
        }

        foreach (var strategy in strategies)
        {
            if (string.IsNullOrWhiteSpace(strategy.Id)
                || strategy.Id.Length > 64)
            {
                throw new StrategyRuntimeException(
                    "InvalidStrategyCatalog",
                    "Каталог содержит недопустимый идентификатор стратегии.");
            }
        }

        if (strategies
            .GroupBy(static strategy => strategy.Id, StringComparer.Ordinal)
            .Any(static group => group.Count() > 1))
        {
            throw new StrategyRuntimeException(
                "DuplicateStrategyId",
                "Каталог содержит повторяющийся идентификатор стратегии.");
        }
    }
}
