using NetPilot.Engine.Abstractions;

namespace NetPilot.Automation;

/// <summary>
/// Переходный адаптер: существующий selector стратегий работает поверх нового
/// engine-neutral профиля. Строковый ID не проходит к движку напрямую —
/// перед вызовом runtime он обязан быть разобран как EngineProfileId.
/// Дополнительно адаптер fail-closed проверяет, что backend явно объявил
/// возможности, необходимые встроенному профилю.
/// </summary>
public sealed class EngineProfileStrategyRuntimeAdapter(
    IEngineProfileRuntime profileRuntime) : IStrategyRuntime
{
    private readonly IEngineProfileRuntime _profileRuntime =
        profileRuntime ?? throw new ArgumentNullException(nameof(profileRuntime));

    public async Task<IReadOnlyList<StrategyDescriptor>> ListStrategiesAsync(
        CancellationToken cancellationToken = default)
    {
        var profiles = await GetValidatedProfilesAsync(cancellationToken);
        if (profiles.Count == 0)
        {
            return [];
        }

        var capabilityProvider = RequireCapabilityProvider();
        var result = new List<StrategyDescriptor>(profiles.Count);

        foreach (var profile in profiles)
        {
            if (!SupportsProfile(profile.Id, capabilityProvider.Capabilities))
            {
                continue;
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

        var capabilityProvider = RequireCapabilityProvider();
        if (!SupportsProfile(activeProfileId, capabilityProvider.Capabilities))
        {
            throw new StrategyRuntimeException(
                "ActiveProfileCapabilitiesMismatch",
                "Движок сообщил об активном профиле, требования которого не покрываются объявленными возможностями backend.");
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

        var profiles = await GetValidatedProfilesAsync(cancellationToken);
        var isPublished = profiles.Any(profile => profile.Id == profileId);

        if (isPublished)
        {
            var capabilityProvider = RequireCapabilityProvider();
            if (!SupportsProfile(profileId, capabilityProvider.Capabilities))
            {
                throw new StrategyRuntimeException(
                    "UnsupportedProfileCapabilities",
                    "Профиль отклонён: backend не объявил все необходимые сетевые возможности.");
            }
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

    private async ValueTask<IReadOnlyList<EngineProfileDescriptor>> GetValidatedProfilesAsync(
        CancellationToken cancellationToken)
    {
        var profiles = await _profileRuntime.ListProfilesAsync(cancellationToken);
        if (profiles is null)
        {
            throw new StrategyRuntimeException(
                "MissingProfileCatalog",
                "Движок Net.Pilot не вернул каталог профилей.");
        }

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

            if (!BuiltInEngineProfileCatalog.IsBuiltIn(profile.Id))
            {
                throw new StrategyRuntimeException(
                    "UnknownProfile",
                    "Каталог движка содержит профиль вне доверенного встроенного каталога Net.Pilot.");
            }
        }

        if (profiles.Count > 0)
        {
            _ = RequireCapabilityProvider();
        }

        return profiles;
    }

    private IEngineCapabilityProvider RequireCapabilityProvider()
    {
        if (_profileRuntime is IEngineCapabilityProvider capabilityProvider)
        {
            return capabilityProvider;
        }

        throw new StrategyRuntimeException(
            "MissingEngineCapabilities",
            "Движок публикует или использует профиль без явной декларации сетевых возможностей.");
    }

    private static bool SupportsProfile(
        EngineProfileId profileId,
        EngineCapabilitySet capabilities)
    {
        try
        {
            return BuiltInEngineProfileCapabilityPolicy.IsSupported(
                profileId,
                capabilities);
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
