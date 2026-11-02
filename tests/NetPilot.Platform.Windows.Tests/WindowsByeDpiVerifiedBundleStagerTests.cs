using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using NetPilot.Updater;

namespace NetPilot.Platform.Windows.Tests;

public sealed class WindowsByeDpiVerifiedBundleStagerTests : IDisposable
{
    private readonly string tempRoot = Path.Combine(
        Path.GetTempPath(),
        "NetPilot-R42-11-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task VerifiedDownloaderBundleStagesThroughExistingProtectedTransactionBoundary()
    {
        var fixture = CreateFixture(tamperPackageTree: false);
        var bundle = await DownloadBundleAsync(fixture);

        var result = await WindowsByeDpiVerifiedBundleStager.StageForTestingAsync(
            bundle,
            tempRoot,
            TestContext.Current.CancellationToken);

        Assert.Equal(fixture.Manifest.CanonicalManifestSha256, result.ManifestSha256);
        Assert.Equal(fixture.Manifest.Files.Count, result.VerifiedFileCount);
        Assert.Equal(0, result.VerifiedSecurityObjectCount);
        Assert.True(File.Exists(Path.Combine(result.StagingRoot, "bin", "ciadpi.exe")));
        Assert.True(File.Exists(Path.Combine(result.StagingRoot, "LICENSE")));

        var journalPath = Path.Combine(tempRoot, "Journal", result.TransactionId + ".json");
        Assert.True(File.Exists(journalPath));
        var journal = await File.ReadAllTextAsync(journalPath, TestContext.Current.CancellationToken);
        Assert.Contains("\"phase\":\"Verified\"", journal, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BundleExportCopiesCannotMutateBytesLaterUsedForStaging()
    {
        var fixture = CreateFixture(tamperPackageTree: false);
        var bundle = await DownloadBundleAsync(fixture);

        var exportedPayload = bundle.CopyPayloadBytes();
        var exportedManifest = bundle.CopyManifestDocumentBytes();
        var exportedProvenance = bundle.CopyProvenanceBytes();
        exportedPayload[0] ^= 0x20;
        exportedManifest[0] ^= 0x20;
        exportedProvenance[0] ^= 0x20;

        var result = await WindowsByeDpiVerifiedBundleStager.StageForTestingAsync(
            bundle,
            tempRoot,
            TestContext.Current.CancellationToken);

        Assert.Equal(fixture.Manifest.CanonicalManifestSha256, result.ManifestSha256);
        Assert.True(Directory.Exists(result.StagingRoot));
    }

    [Fact]
    public async Task PackageTreeDriftIsRejectedByExistingStagingVerificationAndLeavesNoTransactionResidue()
    {
        var fixture = CreateFixture(tamperPackageTree: true);
        var bundle = await DownloadBundleAsync(fixture);

        await Assert.ThrowsAsync<UpdateSecurityException>(() =>
            WindowsByeDpiVerifiedBundleStager.StageForTestingAsync(
                bundle,
                tempRoot,
                TestContext.Current.CancellationToken));

        var journalRoot = Path.Combine(tempRoot, "Journal");
        Assert.True(Directory.Exists(journalRoot));
        Assert.Empty(Directory.EnumerateFiles(journalRoot, "*.json"));
        Assert.Empty(Directory.EnumerateDirectories(tempRoot)
            .Where(path => !string.Equals(
                Path.GetFileName(path),
                "Journal",
                StringComparison.Ordinal)));
    }

    [Fact]
    public async Task VerifiedBundleAggregateIdentityMatchesImmutableDescriptorAssets()
    {
        var fixture = CreateFixture(tamperPackageTree: false);
        var bundle = await DownloadBundleAsync(fixture);

        Assert.Equal(
            fixture.Descriptor.Payload.SizeBytes
            + fixture.Descriptor.Manifest.SizeBytes
            + fixture.Descriptor.Provenance.SizeBytes,
            bundle.TotalBytes);

        Assert.Equal(
            fixture.Descriptor.Payload.Sha256Hex,
            Sha256Hex(bundle.CopyPayloadBytes()));
        Assert.Equal(
            fixture.Descriptor.Manifest.Sha256Hex,
            Sha256Hex(bundle.CopyManifestDocumentBytes()));
        Assert.Equal(
            fixture.Descriptor.Provenance.Sha256Hex,
            Sha256Hex(bundle.CopyProvenanceBytes()));
    }

    public void Dispose()
    {
        if (Directory.Exists(tempRoot))
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    private static async Task<ByeDpiVerifiedReleaseBundle> DownloadBundleAsync(Fixture fixture)
    {
        using var handler = new StubHandler((request, _) =>
        {
            var uri = request.RequestUri
                ?? throw new InvalidOperationException("Test request URI missing.");

            if (uri.AbsolutePath.EndsWith(
                    "/releases/tags/byedpi-v0.17.3",
                    StringComparison.Ordinal))
            {
                return Task.FromResult(JsonResponse(request, fixture.MetadataJson));
            }

            if (uri.AbsolutePath.EndsWith("/releases/assets/1001", StringComparison.Ordinal))
            {
                return Task.FromResult(BinaryResponse(request, fixture.PayloadBytes));
            }

            if (uri.AbsolutePath.EndsWith("/releases/assets/1002", StringComparison.Ordinal))
            {
                return Task.FromResult(BinaryResponse(request, fixture.ManifestDocumentBytes));
            }

            if (uri.AbsolutePath.EndsWith("/releases/assets/1003", StringComparison.Ordinal))
            {
                return Task.FromResult(BinaryResponse(request, fixture.ProvenanceBytes));
            }

            throw new InvalidOperationException("Unexpected R42.11 fake-handler request URI: " + uri);
        });

        using var transport = new ByeDpiIsolatedHttpTransport(handler);
        var downloader = new ByeDpiVerifiedReleaseDownloader(transport);
        return await downloader.DownloadAsync(
            fixture.Descriptor,
            "0.17.3",
            TestContext.Current.CancellationToken);
    }

    private static Fixture CreateFixture(bool tamperPackageTree)
    {
        var expectedFiles = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["bin/ciadpi.exe"] = Encoding.ASCII.GetBytes("synthetic-not-executable-r42-11"),
            ["LICENSE"] = Encoding.UTF8.GetBytes("Synthetic MIT notice for R42.11 tests only.\n")
        };

        var manifest = new ByeDpiPackageManifest(
            "byedpi",
            "0.17.3",
            "x64",
            "bin/ciadpi.exe",
            "LICENSE",
            expectedFiles.Select(static pair => new ByeDpiPackageFileManifest(
                pair.Key,
                pair.Value.Length,
                Sha256Hex(pair.Value))));

        var manifestDocumentBytes = ByeDpiPackageManifestDocument.Serialize(manifest);
        var packageFiles = expectedFiles.ToDictionary(
            static pair => pair.Key,
            static pair => pair.Value.ToArray(),
            StringComparer.Ordinal);

        if (tamperPackageTree)
        {
            packageFiles["bin/ciadpi.exe"][0] ^= 0x20;
        }

        var payloadBytes = CreateZipBytes(packageFiles);
        var provenanceBytes = Encoding.UTF8.GetBytes(
            "{\"schemaVersion\":1,\"scope\":\"synthetic-r42.11-test-only\"}");

        var descriptor = new ByeDpiImmutableReleaseDescriptor(
            "byedpi",
            "0.17.3",
            "x64",
            "DarrenDorr",
            "NetPilot",
            "byedpi-v0.17.3",
            "7efde1b1296eaaa187b70e951894dde17527489c",
            "8d13f6f5939190cd27ee68696247073a00b3e7dc",
            new ByeDpiImmutableReleaseAsset(
                "NetPilot-ByeDPI-0.17.3-x64.zip",
                payloadBytes.Length,
                Sha256Hex(payloadBytes)),
            new ByeDpiImmutableReleaseAsset(
                "NetPilot-ByeDPI-0.17.3-x64.manifest.json",
                manifestDocumentBytes.Length,
                Sha256Hex(manifestDocumentBytes)),
            new ByeDpiImmutableReleaseAsset(
                "NetPilot-ByeDPI-0.17.3-x64.provenance.json",
                provenanceBytes.Length,
                Sha256Hex(provenanceBytes)));

        var metadataJson = "{" +
            "\"id\":5111," +
            $"\"tag_name\":\"{descriptor.ReleaseTag}\"," +
            "\"draft\":false," +
            "\"prerelease\":false," +
            "\"immutable\":true," +
            "\"assets\":[" +
            AssetJson(1001, descriptor.Payload) + "," +
            AssetJson(1002, descriptor.Manifest) + "," +
            AssetJson(1003, descriptor.Provenance) +
            "]}";

        return new Fixture(
            descriptor,
            manifest,
            payloadBytes,
            manifestDocumentBytes,
            provenanceBytes,
            metadataJson);
    }

    private static byte[] CreateZipBytes(IReadOnlyDictionary<string, byte[]> files)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var pair in files.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
            {
                var entry = archive.CreateEntry(pair.Key, CompressionLevel.NoCompression);
                using var target = entry.Open();
                target.Write(pair.Value);
            }
        }

        return stream.ToArray();
    }

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

    private sealed record Fixture(
        ByeDpiImmutableReleaseDescriptor Descriptor,
        ByeDpiPackageManifest Manifest,
        byte[] PayloadBytes,
        byte[] ManifestDocumentBytes,
        byte[] ProvenanceBytes,
        string MetadataJson);

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
