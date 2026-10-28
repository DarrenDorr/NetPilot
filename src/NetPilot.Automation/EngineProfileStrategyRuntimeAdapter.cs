using NetPilot.Engine.Abstractions;

namespace NetPilot.Automation;

/// <summary>
/// Переходный адаптер: существующий selector стратегий работает поверх нового
/// engine-neutral профиля. Строковый ID не проходит к движку напрямую —
/// перед вызовом runtime он обязан быть разобран как EngineProfileId.
/// </summary>
public sealed class EngineProfileStrategyRuntimeAdapter(
    IEngineProfileRuntime profileRuntime) : IStrategyRuntime
{
    private readonly IEngineProfileRuntime _profileRuntime =
        profileRuntime ?? throw new ArgumentNullException(nameof(profileRuntime));

    public async Task<IReadOnlyList<StrategyDescriptor>> ListStrategiesAsync(
        CancellationToken cancellationToken = default)
    {
        var profiles = await _profileRuntime.ListProfilesAsync(cancellationToken);
        if (profiles is null)
        {
            throw new StrategyRuntimeException(
                "MissingProfileCatalog",
                "Движок Net.Pilot не вернул каталог профилей.");
        }

        var result = new List<StrategyDescriptor>(profiles.Count);
        var seenIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var profile in profiles)
        {
            if (profile is null || profile.Id.IsEmpty)
            {
                throw new StrategyRuntimeException(
                    "InvalidProfileCatalog",
                    "Каталог движка содержит недопустимый профиль.");
            }

            if (!seenIds.Add(profile.Id.Value))
            {
                throw new StrategyRuntimeException(
                    "DuplicateProfileId",
                    "Каталог движка содержит повторяющийся идентификатор профиля.");
            }

            result.Add(new StrategyDescriptor(profile.Id.Value, profile.DisplayName));
        }

        return result;
    }

    public async Task<StrategyRuntimeState> GetStateAsync(
        CancellationToken cancellationToken = default)
    {
        var state = await _profileRuntime.GetProfileStateAsync(cancellationToken);

        if (!state.IsActive)
        {
            return StrategyRuntimeState.Stopped;
        }

        if (state.ActiveProfileId is not { } activeProfileId || activeProfileId.IsEmpty)
        {
            throw new StrategyRuntimeException(
                "MissingActiveProfileId",
                "Движок сообщил об активном профиле без корректного идентификатора.");
        }

        return StrategyRuntimeState.Running(activeProfileId.Value);
    }

    public async Task ActivateAsync(
        string strategyId,
        CancellationToken cancellationToken = default)
    {
        if (!EngineProfileId.TryParse(strategyId, out var profileId))
        {
            throw new StrategyRuntimeException(
                "InvalidProfileId",
                "Идентификатор профиля не соответствует безопасному формату Net.Pilot.");
        }

        try
        {
            await _profileRuntime.ActivateProfileAsync(profileId, cancellationToken);
        }
        catch (EngineProfileRuntimeException exception)
        {
            throw new StrategyRuntimeException(
                exception.Code,
                exception.Message,
                exception);
        }
    }
}
