namespace NetPilot.Updater;

/// <summary>
/// Fail-closed transport для состояния, когда native admission не содержит
/// ни одного разрешённого release. Реализация намеренно не имеет HttpClient,
/// URL, файловых или иных сетевых зависимостей.
/// </summary>
public sealed class NoAdmittedZapretReleaseTransport : IZapretReleaseTransport
{
    public static NoAdmittedZapretReleaseTransport Instance { get; } = new();

    private NoAdmittedZapretReleaseTransport()
    {
    }

    public Task<string> DownloadVerifiedAssetAsync(
        GithubZapretRelease release,
        string downloadRoot,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromException<string>(new UpdateSecurityException(
            "NoAdmittedEngineRelease",
            "Скачивание сетевого движка запрещено: ни один native release не имеет статуса ADMIT."));
    }
}
