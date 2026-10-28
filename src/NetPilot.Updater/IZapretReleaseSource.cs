namespace NetPilot.Updater;

/// <summary>
/// Legacy discovery-capable source. Privileged install path должен получать этот
/// интерфейс только через admission-wrapper, а не напрямую от GitHub transport.
/// </summary>
public interface IZapretReleaseSource : IZapretReleaseTransport
{
    Task<GithubZapretRelease> GetLatestReleaseAsync(
        CancellationToken cancellationToken = default);
}
