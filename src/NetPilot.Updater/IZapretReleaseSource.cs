namespace NetPilot.Updater;

public interface IZapretReleaseSource
{
    Task<GithubZapretRelease> GetLatestReleaseAsync(
        CancellationToken cancellationToken = default);

    Task<string> DownloadVerifiedAssetAsync(
        GithubZapretRelease release,
        string downloadRoot,
        CancellationToken cancellationToken = default);
}
