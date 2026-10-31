namespace NetPilot.Updater;

public sealed record ByeDpiTransportResponseFacts(
    int StatusCode,
    long? ContentLength,
    Uri? Location);

/// <summary>
/// Pure R42.8/R42.9 response-shape policy for the bounded ByeDPI HTTP transport.
/// This type performs no network access and owns no streams/files.
/// </summary>
public static class ByeDpiTransportResponsePolicy
{
    public static void ValidateReleaseMetadataResponse(ByeDpiTransportResponseFacts response)
    {
        ArgumentNullException.ThrowIfNull(response);

        if (response.StatusCode != 200)
        {
            throw Invalid("ByeDPI release metadata endpoint must return HTTP 200.");
        }
        RejectUnexpectedLocation(response);

        if (response.ContentLength is { } length
            && (length <= 0 || length > ByeDpiAcquisitionTrustPolicy.MaxReleaseMetadataBytes))
        {
            throw Invalid("ByeDPI release metadata Content-Length is outside the fixed R42.7 limit.");
        }
    }

    /// <summary>
    /// Validates the first GitHub release-asset API response.
    /// Returns null for direct HTTP 200 content, or the validated single redirect URI for HTTP 302.
    /// </summary>
    public static Uri? ValidateAssetApiResponse(
        ByeDpiTransportResponseFacts response,
        long expectedSizeBytes)
    {
        ArgumentNullException.ThrowIfNull(response);
        ValidateExpectedAssetSize(expectedSizeBytes);

        if (response.StatusCode == 200)
        {
            RejectUnexpectedLocation(response);
            ValidateOptionalExactContentLength(response.ContentLength, expectedSizeBytes);
            return null;
        }

        if (response.StatusCode == 302)
        {
            if (response.ContentLength is not null)
            {
                throw Invalid("ByeDPI release asset HTTP 302 response must not carry an asset Content-Length.");
            }

            if (response.Location is null)
            {
                throw Invalid("ByeDPI release asset HTTP 302 response is missing Location.");
            }

            ByeDpiAcquisitionTrustPolicy.ValidateReleaseAssetRedirect(response.Location, 1);
            return response.Location;
        }

        throw Invalid("ByeDPI release asset API response must be direct HTTP 200 or one validated HTTP 302.");
    }

    public static void ValidateRedirectedAssetResponse(
        ByeDpiTransportResponseFacts response,
        long expectedSizeBytes)
    {
        ArgumentNullException.ThrowIfNull(response);
        ValidateExpectedAssetSize(expectedSizeBytes);

        if (response.StatusCode != 200)
        {
            throw Invalid("ByeDPI redirected release asset response must terminate at HTTP 200.");
        }

        RejectUnexpectedLocation(response);
        ValidateOptionalExactContentLength(response.ContentLength, expectedSizeBytes);
    }

    private static void ValidateOptionalExactContentLength(long? contentLength, long expectedSizeBytes)
    {
        if (contentLength is { } length && length != expectedSizeBytes)
        {
            throw Invalid("ByeDPI asset Content-Length does not match the immutable descriptor size.");
        }
    }

    private static void ValidateExpectedAssetSize(long expectedSizeBytes)
    {
        if (expectedSizeBytes <= 0 || expectedSizeBytes > ByeDpiAcquisitionTrustPolicy.MaxTotalAssetBytes)
        {
            throw Invalid("ByeDPI expected asset size is outside the fixed acquisition limit.");
        }
    }

    private static void RejectUnexpectedLocation(ByeDpiTransportResponseFacts response)
    {
        if (response.Location is not null)
        {
            throw Invalid("ByeDPI HTTP 200 response must not carry a redirect Location in the trusted transport contract.");
        }
    }

    private static UpdateSecurityException Invalid(string message) =>
        new("ByeDpiTransportResponseInvalid", message);
}
