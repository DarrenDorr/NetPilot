namespace NetPilot.Updater;

/// <summary>
/// Security boundary между privileged updater и сетевым transport.
/// Legacy-метод GetLatestReleaseAsync намеренно НЕ обращается к upstream latest:
/// он возвращает только локально допущенную release identity.
/// </summary>
public sealed class AdmittedZapretReleaseSource(
    IZapretReleaseSource transport,
    IZapretReleaseAdmissionCatalog admissionCatalog) : IZapretReleaseSource
{
    private readonly IZapretReleaseSource _transport =
        transport ?? throw new ArgumentNullException(nameof(transport));
    private readonly IZapretReleaseAdmissionCatalog _admissionCatalog =
        admissionCatalog ?? throw new ArgumentNullException(nameof(admissionCatalog));

    public Task<GithubZapretRelease> GetLatestReleaseAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(GetAdmittedReleaseOrThrow());
    }

    public Task<string> DownloadVerifiedAssetAsync(
        GithubZapretRelease release,
        string downloadRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(release);
        ArgumentException.ThrowIfNullOrWhiteSpace(downloadRoot);
        cancellationToken.ThrowIfCancellationRequested();

        var admitted = GetAdmittedReleaseOrThrow();
        EnsureSameIdentity(release, admitted);

        return _transport.DownloadVerifiedAssetAsync(
            admitted,
            downloadRoot,
            cancellationToken);
    }

    private GithubZapretRelease GetAdmittedReleaseOrThrow()
    {
        var release = _admissionCatalog.GetAdmittedRelease()
            ?? throw new UpdateSecurityException(
                "NoAdmittedEngineRelease",
                "Установка сетевого движка заблокирована: нет release со статусом ADMIT.");

        ValidateAdmittedIdentity(release);
        return release;
    }

    private static void ValidateAdmittedIdentity(GithubZapretRelease release)
    {
        if (release.ReleaseId <= 0
            || release.AssetId <= 0
            || string.IsNullOrWhiteSpace(release.Version)
            || release.Version.Length > 64
            || release.ReleasePageUri is null
            || release.Asset is null
            || !string.Equals(release.Version, release.Asset.Version, StringComparison.Ordinal))
        {
            throw new UpdateSecurityException(
                "InvalidAdmittedReleaseIdentity",
                "Локальный admission catalog содержит неполную identity релиза.");
        }

        var expectedPage =
            $"https://github.com/Flowseal/zapret-discord-youtube/releases/tag/{Uri.EscapeDataString(release.Version)}";
        if (!string.Equals(release.ReleasePageUri.AbsoluteUri, expectedPage, StringComparison.Ordinal))
        {
            throw new UpdateSecurityException(
                "InvalidAdmittedReleaseIdentity",
                "Admission catalog указывает релиз вне разрешённого legacy upstream.");
        }
    }

    private static void EnsureSameIdentity(
        GithubZapretRelease supplied,
        GithubZapretRelease admitted)
    {
        if (supplied.ReleaseId != admitted.ReleaseId
            || supplied.AssetId != admitted.AssetId
            || !string.Equals(supplied.Version, admitted.Version, StringComparison.Ordinal)
            || supplied.Asset is null
            || admitted.Asset is null
            || !string.Equals(supplied.Asset.FileName, admitted.Asset.FileName, StringComparison.Ordinal)
            || supplied.Asset.SizeBytes != admitted.Asset.SizeBytes
            || !string.Equals(supplied.Asset.Sha256Hex, admitted.Asset.Sha256Hex, StringComparison.Ordinal)
            || supplied.Asset.DownloadUri != admitted.Asset.DownloadUri)
        {
            throw new UpdateSecurityException(
                "ReleaseNotAdmitted",
                "Updater отклонил release identity, которая не совпадает с локальным admission catalog.");
        }
    }
}
