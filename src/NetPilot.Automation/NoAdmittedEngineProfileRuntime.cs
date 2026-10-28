using NetPilot.Engine.Abstractions;

namespace NetPilot.Automation;

/// <summary>
/// Fail-closed runtime для состояния, когда ни один native engine не прошёл ADMIT.
/// Он не имеет executable, пути, process launcher или сетевого transport и физически
/// не может активировать профиль.
/// </summary>
public sealed class NoAdmittedEngineProfileRuntime : IEngineProfileRuntime
{
    public string EngineId => "no-admitted-engine";

    public ValueTask<IReadOnlyList<EngineProfileDescriptor>> ListProfilesAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<IReadOnlyList<EngineProfileDescriptor>>([]);
    }

    public ValueTask<EngineProfileRuntimeState> GetProfileStateAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(EngineProfileRuntimeState.Inactive);
    }

    public ValueTask ActivateProfileAsync(
        EngineProfileId profileId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (profileId.IsEmpty)
        {
            throw new EngineProfileRuntimeException(
                "InvalidProfileId",
                "Пустой идентификатор профиля отклонён.");
        }

        throw new EngineProfileRuntimeException(
            "NoAdmittedEngine",
            "Запуск сетевого профиля запрещён: ни один native engine не прошёл security admission.");
    }
}
