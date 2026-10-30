using NetPilot.Updater;

namespace NetPilot.Core.Tests;

public sealed class ByeDpiTransportResponsePolicyTests
{
    [Fact]
    public void MetadataHttp200WithinLimitIsAccepted()
    {
        ByeDpiTransportResponsePolicy.ValidateReleaseMetadataResponse(
            new ByeDpiTransportResponseFacts(200, 1024, null));
    }

    [Fact]
    public void MetadataWithoutContentLengthIsAcceptedForFutureBoundedStreaming()
    {
        ByeDpiTransportResponsePolicy.ValidateReleaseMetadataResponse(
            new ByeDpiTransportResponseFacts(200, null, null));
    }

    [Fact]
    public void MetadataRedirectOrOversizeIsRejected()
    {
        Assert.Throws<UpdateSecurityException>(() =>
            ByeDpiTransportResponsePolicy.ValidateReleaseMetadataResponse(
                new ByeDpiTransportResponseFacts(302, null, new Uri("https://api.github.com/redirect"))));

        Assert.Throws<UpdateSecurityException>(() =>
            ByeDpiTransportResponsePolicy.ValidateReleaseMetadataResponse(
                new ByeDpiTransportResponseFacts(
                    200,
                    ByeDpiAcquisitionTrustPolicy.MaxReleaseMetadataBytes + 1L,
                    null)));
    }

    [Fact]
    public void DirectAssetHttp200WithExactLengthIsAccepted()
    {
        var redirect = ByeDpiTransportResponsePolicy.ValidateAssetApiResponse(
            new ByeDpiTransportResponseFacts(200, 1234, null),
            1234);

        Assert.Null(redirect);
    }

    [Fact]
    public void DirectAssetWithoutContentLengthIsAcceptedForFutureExactStreamCounting()
    {
        var redirect = ByeDpiTransportResponsePolicy.ValidateAssetApiResponse(
            new ByeDpiTransportResponseFacts(200, null, null),
            1234);

        Assert.Null(redirect);
    }

    [Fact]
    public void DirectAssetLengthMismatchIsRejected()
    {
        Assert.Throws<UpdateSecurityException>(() =>
            ByeDpiTransportResponsePolicy.ValidateAssetApiResponse(
                new ByeDpiTransportResponseFacts(200, 1235, null),
                1234));
    }

    [Fact]
    public void TrustedSingleAssetRedirectIsReturned()
    {
        var location = new Uri(
            "https://release-assets.githubusercontent.com/github-production-release-asset/123/file.zip?sig=test");

        var redirect = ByeDpiTransportResponsePolicy.ValidateAssetApiResponse(
            new ByeDpiTransportResponseFacts(302, null, location),
            1234);

        Assert.Equal(location, redirect);
    }

    [Fact]
    public void MissingOrUntrustedAssetRedirectLocationIsRejected()
    {
        Assert.Throws<UpdateSecurityException>(() =>
            ByeDpiTransportResponsePolicy.ValidateAssetApiResponse(
                new ByeDpiTransportResponseFacts(302, null, null),
                1234));

        Assert.Throws<UpdateSecurityException>(() =>
            ByeDpiTransportResponsePolicy.ValidateAssetApiResponse(
                new ByeDpiTransportResponseFacts(302, null, new Uri("https://example.com/file.zip")),
                1234));
    }

    [Fact]
    public void RedirectedAssetMustTerminateAtHttp200()
    {
        Assert.Throws<UpdateSecurityException>(() =>
            ByeDpiTransportResponsePolicy.ValidateRedirectedAssetResponse(
                new ByeDpiTransportResponseFacts(
                    302,
                    null,
                    new Uri("https://release-assets.githubusercontent.com/another")),
                1234));
    }

    [Fact]
    public void RedirectedAssetHttp200WithExactLengthIsAccepted()
    {
        ByeDpiTransportResponsePolicy.ValidateRedirectedAssetResponse(
            new ByeDpiTransportResponseFacts(200, 1234, null),
            1234);
    }

    [Fact]
    public void Http200WithUnexpectedLocationIsRejected()
    {
        Assert.Throws<UpdateSecurityException>(() =>
            ByeDpiTransportResponsePolicy.ValidateAssetApiResponse(
                new ByeDpiTransportResponseFacts(
                    200,
                    1234,
                    new Uri("https://release-assets.githubusercontent.com/unexpected")),
                1234));
    }

    [Fact]
    public void UnsupportedAssetStatusIsRejected()
    {
        Assert.Throws<UpdateSecurityException>(() =>
            ByeDpiTransportResponsePolicy.ValidateAssetApiResponse(
                new ByeDpiTransportResponseFacts(307, null, null),
                1234));
    }
}
