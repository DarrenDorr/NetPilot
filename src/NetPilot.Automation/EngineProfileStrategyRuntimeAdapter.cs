using NetPilot.Engine.Abstractions;

namespace NetPilot.Automation;

/// <summary>
/// Переходный адаптер: существующий selector стратегий работает поверх нового
/// engine-neutral профиля. Строковый ID не проходит к движку напрямую —
/// перед вызовом runtime он обязан быть разобран как EngineProfileId.
/// Дополнительно адаптер fail-closed проверяет доверенную backend declaration,
/// её канонический EngineId и возможности, необходимые встроенному профилю.
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

        var declaration = RequireTrustedCapabilityDeclaration();
        var result = new List<StrategyDescriptor>(profiles.Count);

        foreach (var profile in profiles)
        {
            if (!SupportsProfile(profile.Id, declaration.DirectCapabilities))
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

        var declaration = RequireTrustedCapabilityDeclaration();
        if (!SupportsProfile(activeProfileId, declaration.DirectCapabilities))
        {
            throw new StrategyRuntimeException(
                "ActiveProfileCapabilitiesMismatch",
                "Движок сообщил об активном профиле, требования которого не покрываются доверенной backend declaration.");
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
            var declaration = RequireTrustedCapabilityDeclaration();
            if (!SupportsProfile(profileId, declaration.DirectCapabilities))
            {
                throw new StrategyRuntimeException(
                    "UnsupportedProfileCapabilities",
                    "Профиль отклонён: доверенная backend declaration не содержит все необходимые сетевые возможности.");
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
            _ = RequireTrustedCapabilityDeclaration();
        }

        return profiles;
    }

    private EngineBackendCapabilityDeclaration RequireTrustedCapabilityDeclaration()
    {
        if (_profileRuntime is not IEngineBackendCapabilityProvider capabilityProvider)
        {
            throw new StrategyRuntimeException(
                "MissingTrustedEngineCapabilityDeclaration",
                "Движок публикует или использует профиль без доверенной backend capability declaration.");
        }

        var declaration = capabilityProvider.CapabilityDeclaration;
        if (declaration is null)
        {
            throw new StrategyRuntimeException(
                "MissingTrustedEngineCapabilityDeclaration",
                "Движок вернул пустую backend capability declaration.");
        }

        if (!string.Equals(
                _profileRuntime.EngineId,
                declaration.EngineId,
                StringComparison.Ordinal))
        {
            throw new StrategyRuntimeException(
                "EngineCapabilityIdentityMismatch",
                "EngineId runtime не соответствует доверенной backend capability declaration.");
        }

        return declaration;
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
