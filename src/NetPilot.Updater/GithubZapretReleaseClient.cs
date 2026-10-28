using System.Net;
using System.Net.Http.Headers;

namespace NetPilot.Updater;

public sealed class GithubZapretReleaseClient : IDisposable
{
    private const string GithubApiVersion = "2026-03-10";
    private const string UserAgent = "NetPilot-Updater";
    private readonly HttpClient _httpClient;
    private bool _disposed;

    public GithubZapretReleaseClient()
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            Credentials = null,
            PreAuthenticate = false,
            AutomaticDecompression = DecompressionMethods.None,
            MaxResponseHeadersLength = 32,
            ConnectTimeout = TimeSpan.FromSeconds(10),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        };

        _httpClient = new HttpClient(handler, disposeHandler: true)
        {
            Timeout = TimeSpan.FromSeconds(UpdateNetworkLimits.NetworkTimeoutSeconds)
        };
    }

    public async Task<GithubZapretRelease> GetLatestReleaseAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        using var request = CreateGithubApiRequest(
            HttpMethod.Get,
            new Uri(GithubZapretReleaseParser.LatestReleaseApiUrl),
            "application/vnd.github+json");

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new UpdateSecurityException(
                "ReleaseMetadataHttpError",
                $"GitHub вернул неожиданный статус metadata релиза: {(int)response.StatusCode}.");
        }

        ValidateJsonContentType(response.Content.Headers.ContentType);
        var bytes = await ReadLimitedAsync(
            response.Content,
            UpdateNetworkLimits.MaxReleaseMetadataBytes,
            cancellationToken);

        return GithubZapretReleaseParser.Parse(bytes);
    }

    public async Task<string> DownloadVerifiedAssetAsync(
        GithubZapretRelease release,
        string downloadRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(release);
        ArgumentException.ThrowIfNullOrWhiteSpace(downloadRoot);
        ThrowIfDisposed();

        var root = PrepareEmptyDownloadRoot(downloadRoot);
        var destination = Path.Combine(root, "source.zip");

        try
        {
            using var firstRequest = CreateGithubApiRequest(
                HttpMethod.Get,
                GithubZapretReleaseParser.GetAssetApiUri(release.AssetId),
                "application/octet-stream");

            using var firstResponse = await _httpClient.SendAsync(
                firstRequest,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (firstResponse.StatusCode == HttpStatusCode.OK)
            {
                await SaveAndVerifyResponseAsync(
                    firstResponse,
                    release.Asset,
                    destination,
                    cancellationToken);
                return destination;
            }

            if (firstResponse.StatusCode != HttpStatusCode.Found)
            {
                throw new UpdateSecurityException(
                    "ReleaseAssetHttpError",
                    $"GitHub вернул неожиданный статус release asset: {(int)firstResponse.StatusCode}.");
            }

            var redirect = firstResponse.Headers.Location
                ?? throw new UpdateSecurityException(
                    "MissingReleaseAssetRedirect",
                    "GitHub вернул redirect без Location.");

            ValidateReleaseAssetRedirect(redirect);

            using var redirectedRequest = new HttpRequestMessage(HttpMethod.Get, redirect);
            redirectedRequest.Headers.UserAgent.ParseAdd(UserAgent);
            redirectedRequest.Headers.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/octet-stream"));

            using var redirectedResponse = await _httpClient.SendAsync(
                redirectedRequest,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (redirectedResponse.StatusCode != HttpStatusCode.OK)
            {
                throw new UpdateSecurityException(
                    "ReleaseAssetRedirectHttpError",
                    $"Хранилище GitHub вернуло неожиданный статус asset: {(int)redirectedResponse.StatusCode}.");
            }

            if (redirectedResponse.Headers.Location is not null)
            {
                throw new UpdateSecurityException(
                    "UnexpectedAdditionalRedirect",
                    "Дополнительные redirect при загрузке release asset запрещены.");
            }

            await SaveAndVerifyResponseAsync(
                redirectedResponse,
                release.Asset,
                destination,
                cancellationToken);

            return destination;
        }
        catch
        {
            TryDelete(destination);
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _httpClient.Dispose();
    }

    private static HttpRequestMessage CreateGithubApiRequest(
        HttpMethod method,
        Uri uri,
        string accept)
    {
        ValidateGithubApiUri(uri);

        var request = new HttpRequestMessage(method, uri);
        request.Headers.UserAgent.ParseAdd(UserAgent);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));
        request.Headers.Add("X-GitHub-Api-Version", GithubApiVersion);
        return request;
    }

    private static void ValidateGithubApiUri(Uri uri)
    {
        if (!uri.IsAbsoluteUri
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(uri.Host, "api.github.com", StringComparison.OrdinalIgnoreCase)
            || !uri.IsDefaultPort
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new UpdateSecurityException(
                "UntrustedGithubApiUri",
                "Updater может обращаться только к HTTPS GitHub API без пользовательских параметров.");
        }
    }

    internal static void ValidateReleaseAssetRedirect(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);

        if (!uri.IsAbsoluteUri
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(uri.Host, "release-assets.githubusercontent.com", StringComparison.OrdinalIgnoreCase)
            || !uri.IsDefaultPort
            || !string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new UpdateSecurityException(
                "UntrustedReleaseAssetRedirect",
                "Release asset перенаправлен на недоверенный хост.");
        }
    }

    private static async Task SaveAndVerifyResponseAsync(
        HttpResponseMessage response,
        ReleaseAsset asset,
        string destination,
        CancellationToken cancellationToken)
    {
        var contentLength = response.Content.Headers.ContentLength;
        if (contentLength is not null && contentLength.Value != asset.SizeBytes)
        {
            throw new UpdateSecurityException(
                "ReleaseAssetSizeMismatch",
                "Размер загружаемого asset не совпадает с metadata официального релиза.");
        }

        await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
        await using (var target = new FileStream(
            destination,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            await CopyExactSizeAsync(source, target, asset.SizeBytes, cancellationToken);
        }

        await using var verificationStream = new FileStream(
            destination,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        await Sha256Verifier.VerifyAsync(
            verificationStream,
            asset.Sha256Hex,
            cancellationToken);
    }

    private static async Task CopyExactSizeAsync(
        Stream source,
        Stream target,
        long expectedSize,
        CancellationToken cancellationToken)
    {
        if (expectedSize is <= 0 or > UpdateSecurityLimits.MaxDownloadBytes)
        {
            throw new UpdateSecurityException("ReleaseAssetSizeLimit", "Размер asset выходит за безопасный лимит.");
        }

        var buffer = new byte[64 * 1024];
        long written = 0;

        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            checked
            {
                written += read;
            }

            if (written > expectedSize || written > UpdateSecurityLimits.MaxDownloadBytes)
            {
                throw new UpdateSecurityException(
                    "ReleaseAssetSizeMismatch",
                    "Загружаемый asset превышает размер из metadata.");
            }

            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        if (written != expectedSize)
        {
            throw new UpdateSecurityException(
                "ReleaseAssetSizeMismatch",
                "Фактический размер загруженного asset не совпадает с metadata.");
        }
    }

    private static async Task<byte[]> ReadLimitedAsync(
        HttpContent content,
        int maxBytes,
        CancellationToken cancellationToken)
    {
        var contentLength = content.Headers.ContentLength;
        if (contentLength is > 0 && contentLength > maxBytes)
        {
            throw new UpdateSecurityException(
                "ReleaseMetadataSize",
                "Metadata релиза превышает безопасный лимит.");
        }

        await using var source = await content.ReadAsStreamAsync(cancellationToken);
        using var target = new MemoryStream(capacity: Math.Min(maxBytes, 64 * 1024));
        var buffer = new byte[16 * 1024];
        var total = 0;

        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            total = checked(total + read);
            if (total > maxBytes)
            {
                throw new UpdateSecurityException(
                    "ReleaseMetadataSize",
                    "Metadata релиза превышает безопасный лимит.");
            }

            target.Write(buffer, 0, read);
        }

        return target.ToArray();
    }

    private static string PrepareEmptyDownloadRoot(string downloadRoot)
    {
        var root = Path.GetFullPath(downloadRoot);
        if (Directory.Exists(root))
        {
            var attributes = File.GetAttributes(root);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new UpdateSecurityException("ReparsePointRejected", "Download staging не может быть reparse point.");
            }

            if (Directory.EnumerateFileSystemEntries(root).Any())
            {
                throw new UpdateSecurityException("DownloadStagingNotEmpty", "Download staging должен быть пустым.");
            }
        }
        else
        {
            Directory.CreateDirectory(root);
        }

        return root;
    }

    private static void ValidateJsonContentType(MediaTypeHeaderValue? contentType)
    {
        if (contentType is null
            || (!string.Equals(contentType.MediaType, "application/json", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(contentType.MediaType, "application/vnd.github+json", StringComparison.OrdinalIgnoreCase)))
        {
            throw new UpdateSecurityException(
                "UnexpectedReleaseMetadataContentType",
                "GitHub metadata имеет неожиданный Content-Type.");
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
