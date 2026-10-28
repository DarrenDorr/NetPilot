namespace NetPilot.Updater;

/// <summary>
/// Узкий сетевой transport: умеет скачать только уже выбранную exact release identity.
/// В интерфейсе намеренно отсутствует discovery/latest API, чтобы download transport
/// не мог принимать решение о доверии к новой upstream-версии.
/// </summary>
public interface IZapretReleaseTransport
{
    Task<string> DownloadVerifiedAssetAsync(
        GithubZapretRelease release,
        string downloadRoot,
        CancellationToken cancellationToken = default);
}
