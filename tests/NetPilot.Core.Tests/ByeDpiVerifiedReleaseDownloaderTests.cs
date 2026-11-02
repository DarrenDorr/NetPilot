using System.Net;
using System.Security.Cryptography;
using System.Text;
using NetPilot.Updater;

namespace NetPilot.Core.Tests;

public sealed class ByeDpiVerifiedReleaseDownloaderTests
{
    private static readonly byte[] PayloadBytes = Encoding.UTF8.GetBytes("payload-r42.10-no-execution");
    private static readonly byte[] ProvenanceBytes = Encoding.UTF8.GetBytes("provenance-r42.10");

    [Fact]
    public async Task DownloadsExactlyThreeVerifiedAssetsAndReturnsDefensiveBundle()
    {
        var manifestBytes = CreateManifestBytes("0.17.3");
        var descriptor = CreateDescriptor(manifestBytes);
        var metadataJson = CreateMetadataJson(descriptor);
        var requestedAssets = new List<string>();

        using var handler = new StubHandler((request, _) =>
        {
            if (request.RequestUri!.AbsolutePath.Contains("/releases/tags/", StringComparison.Ordinal))
            {
                return Task.FromResult(JsonResponse(request, metadataJson));
            }

            requestedAssets.Add(request.RequestUri.AbsoluteUri);
            var id = request.RequestUri.Segments[^1];
            return Task.FromResult(id switch
            {
                "1001" => BinaryResponse(request, PayloadBytes),
                "1002" => BinaryResponse(request, manifestBytes),
                "1003" => BinaryResponse(request, ProvenanceBytes),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound) { RequestMessage = request }
            });
        });
        using var transport = new ByeDpiIsolatedHttpTransport(handler);
        var downloader = new ByeDpiVerifiedReleaseDownloader(transport);

        var bundle = await downloader.DownloadAsync(
            descriptor,
            "0.17.3",
            TestContext.Current.CancellationToken);

        Assert.Equal(3, requestedAssets.Count);
        Assert.Equal("0.17.3", bundle.Manifest.Version);
        Assert.Equal(
            PayloadBytes.Length + manifestBytes.Length + ProvenanceBytes.Length,
            bundle.TotalBytes);

        var firstCopy = bundle.CopyPayloadBytes();
        firstCopy[0] ^= 0x20;
        Assert.Equal(PayloadBytes, bundle.CopyPayloadBytes());
        Assert.Equal(manifestBytes, bundle.CopyManifestDocumentBytes());
        Assert.Equal(ProvenanceBytes, bundle.CopyProvenanceBytes());
    }

    [Fact]
    public async Task DowngradeIsBlockedBeforeAnyAssetRequest()
    {
        var manifestBytes = CreateManifestBytes("0.17.3");
        var descriptor = CreateDescriptor(manifestBytes);
        var metadataJson = CreateMetadataJson(descriptor);
        var calls = 0;

        using var handler = new StubHandler((request, _) =>
        {
            calls++;
            return Task.FromResult(JsonResponse(request, metadataJson));
        });
        using var transport = new ByeDpiIsolatedHttpTransport(handler);
        var downloader = new ByeDpiVerifiedReleaseDownloader(transport);

        var exception = await Assert.ThrowsAsync<UpdateSecurityException>(() =>
            downloader.DownloadAsync(descriptor, "0.17.4", TestContext.Current.CancellationToken));

        Assert.Equal("ByeDpiAcquisitionDowngradeBlocked", exception.Code);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task HashValidButMalformedManifestIsRejectedAfterTransportVerification()
    {
        var malformedManifest = Encoding.UTF8.GetBytes("{\"not\":\"a-package-manifest\"}");
        var descriptor = CreateDescriptor(malformedManifest);
        var metadataJson = CreateMetadataJson(descriptor);

        using var handler = CreateSuccessfulHandler(descriptor, metadataJson, malformedManifest);
        using var transport = new ByeDpiIsolatedHttpTransport(handler);
        var downloader = new ByeDpiVerifiedReleaseDownloader(transport);

        await Assert.ThrowsAsync<UpdateSecurityException>(() =>
            downloader.DownloadAsync(descriptor, null, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ManifestIdentityMustMatchImmutableReleaseDescriptor()
    {
        var mismatchedManifest = CreateManifestBytes("0.17.2");
        var descriptor = CreateDescriptor(mismatchedManifest, version: "0.17.3");
        var metadataJson = CreateMetadataJson(descriptor);

        using var handler = CreateSuccessfulHandler(descriptor, metadataJson, mismatchedManifest);
        using var transport = new ByeDpiIsolatedHttpTransport(handler);
        var downloader = new ByeDpiVerifiedReleaseDownloader(transport);

        var exception = await Assert.ThrowsAsync<UpdateSecurityException>(() =>
            downloader.DownloadAsync(descriptor, null, TestContext.Current.CancellationToken));

        Assert.Equal("ByeDpiVerifiedDownloadManifestMismatch", exception.Code);
    }

    [Fact]
    public async Task AssetHashFailureNeverProducesBundle()
    {
        var manifestBytes = CreateManifestBytes("0.17.3");
        var descriptor = CreateDescriptor(manifestBytes);
        var metadataJson = CreateMetadataJson(descriptor);
        var corruptedPayload = PayloadBytes.ToArray();
        corruptedPayload[^1] ^= 0x01;

        using var handler = new StubHandler((request, _) =>
        {
            if (request.RequestUri!.AbsolutePath.Contains("/releases/tags/", StringComparison.Ordinal))
            {
                return Task.FromResult(JsonResponse(request, metadataJson));
            }

            var id = request.RequestUri.Segments[^1];
            return Task.FromResult(id switch
            {
                "1001" => BinaryResponse(request, corruptedPayload),
                "1002" => BinaryResponse(request, manifestBytes),
                "1003" => BinaryResponse(request, ProvenanceBytes),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound) { RequestMessage = request }
            });
        });
        using var transport = new ByeDpiIsolatedHttpTransport(handler);
        var downloader = new ByeDpiVerifiedReleaseDownloader(transport);

        await Assert.ThrowsAsync<UpdateSecurityException>(() =>
            downloader.DownloadAsync(descriptor, null, TestContext.Current.CancellationToken));
    }

    private static StubHandler CreateSuccessfulHandler(
        ByeDpiImmutableReleaseDescriptor descriptor,
        string metadataJson,
        byte[] manifestBytes) =>
        new((request, _) =>
        {
            if (request.RequestUri!.AbsolutePath.Contains("/releases/tags/", StringComparison.Ordinal))
            {
                return Task.FromResult(JsonResponse(request, metadataJson));
            }

            var id = request.RequestUri.Segments[^1];
            return Task.FromResult(id switch
            {
                "1001" => BinaryResponse(request, PayloadBytes),
                "1002" => BinaryResponse(request, manifestBytes),
                "1003" => BinaryResponse(request, ProvenanceBytes),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound) { RequestMessage = request }
            });
        });

    private static ByeDpiImmutableReleaseDescriptor CreateDescriptor(
        byte[] manifestBytes,
        string version = "0.17.3") =>
        new(
            "byedpi",
            version,
            "x64",
            "DarrenDorr",
            "NetPilot",
            $"byedpi-v{version}",
            "7efde1b1296eaaa187b70e951894dde17527489c",
            "8d13f6f5939190cd27ee68696247073a00b3e7dc",
            new ByeDpiImmutableReleaseAsset(
                $"NetPilot-ByeDPI-{version}-x64.zip",
                PayloadBytes.Length,
                Sha256Hex(PayloadBytes)),
            new ByeDpiImmutableReleaseAsset(
                $"NetPilot-ByeDPI-{version}-x64.manifest.json",
                manifestBytes.Length,
                Sha256Hex(manifestBytes)),
            new ByeDpiImmutableReleaseAsset(
                $"NetPilot-ByeDPI-{version}-x64.provenance.json",
                ProvenanceBytes.Length,
                Sha256Hex(ProvenanceBytes)));

    private static byte[] CreateManifestBytes(string version)
    {
        var executable = Encoding.UTF8.GetBytes($"synthetic-ciadpi-{version}");
        var license = Encoding.UTF8.GetBytes("MIT synthetic fixture");
        var manifest = new ByeDpiPackageManifest(
            "byedpi",
            version,
            "x64",
            "bin/ciadpi.exe",
            "LICENSE",
            [
                new ByeDpiPackageFileManifest("bin/ciadpi.exe", executable.Length, Sha256Hex(executable)),
                new ByeDpiPackageFileManifest("LICENSE", license.Length, Sha256Hex(license))
            ]);
        return ByeDpiPackageManifestDocument.Serialize(manifest);
    }

    private static string CreateMetadataJson(ByeDpiImmutableReleaseDescriptor descriptor) =>
        "{" +
        "\"id\":5010," +
        $"\"tag_name\":\"{descriptor.ReleaseTag}\"," +
        "\"draft\":false," +
        "\"prerelease\":false," +
        "\"immutable\":true," +
        "\"assets\":[" +
        AssetJson(1001, descriptor.Payload) + "," +
        AssetJson(1002, descriptor.Manifest) + "," +
        AssetJson(1003, descriptor.Provenance) +
        "]}";

    private static string AssetJson(long id, ByeDpiImmutableReleaseAsset asset) =>
        "{" +
        $"\"url\":\"https://api.github.com/repos/DarrenDorr/NetPilot/releases/assets/{id}\"," +
        $"\"id\":{id}," +
        $"\"name\":\"{asset.Name}\"," +
        "\"state\":\"uploaded\"," +
        $"\"size\":{asset.SizeBytes}," +
        $"\"digest\":\"sha256:{asset.Sha256Hex}\"" +
        "}";

    private static string Sha256Hex(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static HttpResponseMessage JsonResponse(HttpRequestMessage request, string json) =>
        new(HttpStatusCode.OK)
        {
            RequestMessage = request,
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private static HttpResponseMessage BinaryResponse(HttpRequestMessage request, byte[] bytes)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentLength = bytes.Length;
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = request,
            Content = content
        };
    }

    private sealed class StubHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> callback)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            callback(request, cancellationToken);
    }
}
