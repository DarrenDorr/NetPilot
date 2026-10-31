using System.Buffers;
using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace NetPilot.Updater;

/// <summary>
/// R42.9 isolated bounded HTTP transport for ByeDPI acquisition facts.
///
/// This type is intentionally not wired into the product downloader, filesystem,
/// staging, Service/IPC or native execution paths. Tests inject an in-memory
/// HttpMessageHandler. A live network handler is not created by this class.
/// </summary>
public sealed class ByeDpiIsolatedHttpTransport : IDisposable
{
    public const string UserAgent = "NetPilot-ByeDPI-Acquisition";

    private readonly HttpClient _httpClient;
    private readonly TimeSpan _totalTimeout;
    private bool _disposed;

    public ByeDpiIsolatedHttpTransport(
        HttpMessageHandler handler,
        TimeSpan? totalTimeout = null,
        bool disposeHandler = false)
    {
        ArgumentNullException.ThrowIfNull(handler);

        _totalTimeout = totalTimeout
            ?? TimeSpan.FromSeconds(ByeDpiAcquisitionTrustPolicy.NetworkTimeoutSeconds);

        var maxTimeout = TimeSpan.FromSeconds(ByeDpiAcquisitionTrustPolicy.NetworkTimeoutSeconds);
        if (_totalTimeout <= TimeSpan.Zero || _totalTimeout > maxTimeout)
        {
            throw Invalid("ByeDPI isolated transport timeout must be positive and may not exceed the fixed R42.7 network timeout.");
        }

        _httpClient = new HttpClient(handler, disposeHandler)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
    }

    public async Task<ByeDpiAcquisitionReleaseMetadata> FetchReleaseMetadataAsync(
        ByeDpiImmutableReleaseDescriptor descriptor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ThrowIfDisposed();

        _ = ByeDpiImmutableReleaseDocument.Serialize(descriptor);
        var uri = GetReleaseMetadataApiUri(descriptor.ReleaseTag);

        using var timeout = CreateTimeout(cancellationToken);
        try
        {
            using var request = CreateGithubApiRequest(
                uri,
                ByeDpiAcquisitionTrustPolicy.ReleaseMetadataAccept);
            using var response = await SendObservedAsync(request, timeout.Token);

            ByeDpiTransportResponsePolicy.ValidateReleaseMetadataResponse(ToFacts(response));
            ValidateMetadataContentType(response.Content.Headers.ContentType);

            var bytes = await ReadBoundedAsync(
                response.Content,
                ByeDpiAcquisitionTrustPolicy.MaxReleaseMetadataBytes,
                timeout.Token);

            return GithubByeDpiReleaseParser.Parse(bytes);
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested && timeout.IsCancellationRequested)
        {
            throw new UpdateSecurityException(
                "ByeDpiHttpTransportTimeout",
                "ByeDPI isolated HTTP transport exceeded the fixed total timeout.");
        }
        catch (HttpRequestException)
        {
            throw new UpdateSecurityException(
                "ByeDpiHttpTransportFailure",
                "ByeDPI isolated HTTP transport failed before a trusted response was established.");
        }
    }

    public async Task<byte[]> FetchVerifiedAssetAsync(
        ByeDpiAcquisitionPlan plan,
        ByeDpiAcquisitionAssetPlan asset,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(asset);
        ThrowIfDisposed();
        ValidatePlanAndAsset(plan, asset);

        using var timeout = CreateTimeout(cancellationToken);
        try
        {
            using var firstRequest = CreateGithubApiRequest(
                asset.AssetApiUri,
                ByeDpiAcquisitionTrustPolicy.ReleaseAssetAccept);
            using var firstResponse = await SendObservedAsync(firstRequest, timeout.Token);

            var redirect = ByeDpiTransportResponsePolicy.ValidateAssetApiResponse(
                ToFacts(firstResponse),
                asset.SizeBytes);

            if (redirect is null)
            {
                return await ReadExactAndVerifyAsync(
                    firstResponse.Content,
                    asset.SizeBytes,
                    asset.Sha256Hex,
                    timeout.Token);
            }

            firstResponse.Dispose();

            using var redirectedRequest = CreateRedirectRequest(redirect);
            using var redirectedResponse = await SendObservedAsync(redirectedRequest, timeout.Token);

            ByeDpiTransportResponsePolicy.ValidateRedirectedAssetResponse(
                ToFacts(redirectedResponse),
                asset.SizeBytes);

            return await ReadExactAndVerifyAsync(
                redirectedResponse.Content,
                asset.SizeBytes,
                asset.Sha256Hex,
                timeout.Token);
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested && timeout.IsCancellationRequested)
        {
            throw new UpdateSecurityException(
                "ByeDpiHttpTransportTimeout",
                "ByeDPI isolated HTTP transport exceeded the fixed total timeout.");
        }
        catch (HttpRequestException)
        {
            throw new UpdateSecurityException(
                "ByeDpiHttpTransportFailure",
                "ByeDPI isolated HTTP transport failed before a trusted asset was obtained.");
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

    private CancellationTokenSource CreateTimeout(CancellationToken cancellationToken)
    {
        var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_totalTimeout);
        return timeout;
    }

    private async Task<HttpResponseMessage> SendObservedAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var requestedUri = request.RequestUri
            ?? throw Invalid("ByeDPI isolated transport request URI is missing.");

        var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        var observedUri = response.RequestMessage?.RequestUri;
        if (observedUri is not null
            && !string.Equals(observedUri.AbsoluteUri, requestedUri.AbsoluteUri, StringComparison.Ordinal))
        {
            response.Dispose();
            throw Invalid("ByeDPI isolated transport detected a hidden automatic redirect.");
        }

        return response;
    }

    private static HttpRequestMessage CreateGithubApiRequest(Uri uri, string accept)
    {
        ValidateGithubApiUri(uri);

        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd(UserAgent);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));
        request.Headers.TryAddWithoutValidation(
            "X-GitHub-Api-Version",
            ByeDpiAcquisitionTrustPolicy.GithubApiVersion);
        return request;
    }

    private static HttpRequestMessage CreateRedirectRequest(Uri uri)
    {
        ByeDpiAcquisitionTrustPolicy.ValidateReleaseAssetRedirect(uri, 1);

        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd(UserAgent);
        return request;
    }

    private static Uri GetReleaseMetadataApiUri(string releaseTag)
    {
        if (string.IsNullOrWhiteSpace(releaseTag) || releaseTag.Length > 96)
        {
            throw Invalid("ByeDPI release tag is invalid for the isolated transport.");
        }

        return new Uri(
            $"https://api.github.com/repos/{ByeDpiImmutableReleaseDocument.RequiredRepositoryOwner}/" +
            $"{ByeDpiImmutableReleaseDocument.RequiredRepositoryName}/releases/tags/{Uri.EscapeDataString(releaseTag)}");
    }

    private static void ValidatePlanAndAsset(
        ByeDpiAcquisitionPlan plan,
        ByeDpiAcquisitionAssetPlan asset)
    {
        if (!string.Equals(plan.GithubApiVersion, ByeDpiAcquisitionTrustPolicy.GithubApiVersion, StringComparison.Ordinal)
            || !string.Equals(plan.ReleaseMetadataAccept, ByeDpiAcquisitionTrustPolicy.ReleaseMetadataAccept, StringComparison.Ordinal)
            || !string.Equals(plan.ReleaseAssetAccept, ByeDpiAcquisitionTrustPolicy.ReleaseAssetAccept, StringComparison.Ordinal)
            || plan.MaxReleaseMetadataBytes != ByeDpiAcquisitionTrustPolicy.MaxReleaseMetadataBytes
            || plan.NetworkTimeoutSeconds != ByeDpiAcquisitionTrustPolicy.NetworkTimeoutSeconds
            || plan.MaxRedirects != ByeDpiAcquisitionTrustPolicy.MaxRedirects
            || plan.MaxTotalAssetBytes != ByeDpiAcquisitionTrustPolicy.MaxTotalAssetBytes)
        {
            throw Invalid("ByeDPI acquisition plan drifted from the fixed R42.7 transport contract.");
        }

        ValidateGithubApiUri(plan.ReleaseMetadataApiUri);
        ValidateAssetApiUri(asset);
        ValidateSha256Hex(asset.Sha256Hex);

        if (asset.SizeBytes <= 0 || asset.SizeBytes > ByeDpiAcquisitionTrustPolicy.MaxTotalAssetBytes)
        {
            throw Invalid("ByeDPI acquisition asset size is outside the fixed R42.7 limit.");
        }

        if (plan.Assets is null || plan.Assets.Count == 0)
        {
            throw Invalid("ByeDPI acquisition plan does not contain any trusted assets.");
        }

        var matchingAssets = plan.Assets.Count(candidate =>
            candidate.AssetId == asset.AssetId
            && string.Equals(candidate.Name, asset.Name, StringComparison.Ordinal)
            && candidate.SizeBytes == asset.SizeBytes
            && string.Equals(candidate.Sha256Hex, asset.Sha256Hex, StringComparison.Ordinal)
            && string.Equals(candidate.AssetApiUri.AbsoluteUri, asset.AssetApiUri.AbsoluteUri, StringComparison.Ordinal));

        if (matchingAssets != 1)
        {
            throw Invalid("ByeDPI acquisition asset is not an unambiguous member of the trusted R42.7 plan.");
        }
    }

    private static void ValidateAssetApiUri(ByeDpiAcquisitionAssetPlan asset)
    {
        if (asset.AssetId <= 0)
        {
            throw Invalid("ByeDPI acquisition asset id must be positive.");
        }

        ValidateGithubApiUri(asset.AssetApiUri);

        var expected = new Uri(
            $"https://api.github.com/repos/{ByeDpiImmutableReleaseDocument.RequiredRepositoryOwner}/" +
            $"{ByeDpiImmutableReleaseDocument.RequiredRepositoryName}/releases/assets/{asset.AssetId}");

        if (!string.Equals(asset.AssetApiUri.AbsoluteUri, expected.AbsoluteUri, StringComparison.Ordinal))
        {
            throw Invalid("ByeDPI acquisition asset API URI is outside the exact GitHub release boundary.");
        }
    }

    private static void ValidateGithubApiUri(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);

        if (!uri.IsAbsoluteUri
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(uri.Host, "api.github.com", StringComparison.OrdinalIgnoreCase)
            || !uri.IsDefaultPort
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            throw Invalid("ByeDPI isolated transport permits only canonical HTTPS GitHub API URIs.");
        }
    }

    private static ByeDpiTransportResponseFacts ToFacts(HttpResponseMessage response) =>
        new(
            (int)response.StatusCode,
            response.Content.Headers.ContentLength,
            response.Headers.Location);

    private static void ValidateMetadataContentType(MediaTypeHeaderValue? contentType)
    {
        var mediaType = contentType?.MediaType;
        if (!string.Equals(mediaType, "application/json", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(mediaType, ByeDpiAcquisitionTrustPolicy.ReleaseMetadataAccept, StringComparison.OrdinalIgnoreCase))
        {
            throw Invalid("ByeDPI release metadata response has an unexpected Content-Type.");
        }
    }

    private static async Task<byte[]> ReadBoundedAsync(
        HttpContent content,
        int maxBytes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);

        await using var source = await content.ReadAsStreamAsync(cancellationToken);
        using var target = new MemoryStream(capacity: Math.Min(maxBytes, 64 * 1024));
        var buffer = ArrayPool<byte>.Shared.Rent(16 * 1024);
        var total = 0;

        try
        {
            while (true)
            {
                var read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                if (read == 0)
                {
                    break;
                }

                total = checked(total + read);
                if (total > maxBytes)
                {
                    throw Invalid("ByeDPI release metadata stream exceeds the fixed R42.7 byte cap.");
                }

                target.Write(buffer, 0, read);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
        }

        return target.ToArray();
    }

    private static async Task<byte[]> ReadExactAndVerifyAsync(
        HttpContent content,
        long expectedSizeBytes,
        string expectedSha256Hex,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        ValidateSha256Hex(expectedSha256Hex);

        if (expectedSizeBytes <= 0
            || expectedSizeBytes > ByeDpiAcquisitionTrustPolicy.MaxTotalAssetBytes
            || expectedSizeBytes > int.MaxValue)
        {
            throw Invalid("ByeDPI expected asset size is outside the isolated in-memory transport limit.");
        }

        await using var source = await content.ReadAsStreamAsync(cancellationToken);
        using var target = new MemoryStream(capacity: (int)expectedSizeBytes);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
        long total = 0;

        try
        {
            while (true)
            {
                var read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                if (read == 0)
                {
                    break;
                }

                total = checked(total + read);
                if (total > expectedSizeBytes)
                {
                    throw Invalid("ByeDPI asset stream exceeds the immutable descriptor size.");
                }

                hash.AppendData(buffer.AsSpan(0, read));
                target.Write(buffer, 0, read);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
        }

        if (total != expectedSizeBytes)
        {
            throw Invalid("ByeDPI asset stream ended before the immutable descriptor size was reached.");
        }

        var actualHash = hash.GetHashAndReset();
        var expectedHash = Convert.FromHexString(expectedSha256Hex);
        if (!CryptographicOperations.FixedTimeEquals(actualHash, expectedHash))
        {
            throw Invalid("ByeDPI asset SHA-256 does not match the immutable descriptor.");
        }

        return target.ToArray();
    }

    private static void ValidateSha256Hex(string value)
    {
        if (string.IsNullOrEmpty(value)
            || value.Length != 64
            || value.Any(static character =>
                character is not (>= '0' and <= '9')
                    and not (>= 'a' and <= 'f')))
        {
            throw Invalid("ByeDPI asset SHA-256 must be exact lowercase hexadecimal.");
        }
    }

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(_disposed, this);

    private static UpdateSecurityException Invalid(string message) =>
        new("ByeDpiHttpTransportInvalid", message);
}
