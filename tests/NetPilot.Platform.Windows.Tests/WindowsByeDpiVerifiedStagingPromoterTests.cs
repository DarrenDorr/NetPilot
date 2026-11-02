using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using NetPilot.Updater;

namespace NetPilot.Platform.Windows.Tests;

public sealed class WindowsByeDpiVerifiedStagingPromoterTests : IDisposable
{
    private readonly string tempRoot = Path.Combine(
        Path.GetTempPath(),
        "NetPilot-R42-11-1-" + Guid.NewGuid().ToString("N"));

    private string StagingBase => Path.Combine(tempRoot, "staging");

    private string ManagedBase => Path.Combine(tempRoot, "managed");

    [Fact]
    public async Task InstallPromotesFullVerifiedDownloadAndStagingChainIntoManagedState()
    {
        var fixture = CreateFixture("0.17.3", "install");
        var bundle = await DownloadBundleAsync(fixture, currentInstalledVersion: null);
        var staged = await WindowsByeDpiVerifiedBundleStager.StageForTestingAsync(
            bundle,
            StagingBase,
            TestContext.Current.CancellationToken);

        var result = await WindowsByeDpiVerifiedStagingPromoter.PromoteForTestingAsync(
            bundle,
            staged,
            ByeDpiPackageLifecycleOperation.Install,
            engineLeaseActive: false,
            StagingBase,
            ManagedBase,
            TestContext.Current.CancellationToken);

        Assert.Equal(ByeDpiPackageLifecycleOperation.Install, result.Operation);
        Assert.Equal("0.17.3", result.CurrentVersion);
        Assert.Null(result.RetainedPreviousVersion);
        Assert.False(Directory.Exists(staged.StagingRoot));
        Assert.True(File.Exists(Path.Combine(result.ManagedPackageRoot, "bin", "ciadpi.exe")));
        Assert.True(File.Exists(Path.Combine(result.ManagedPackageRoot, "LICENSE")));

        var state = WindowsByeDpiAtomicPackagePromoter.GetStateForTesting(ManagedBase);
        Assert.Equal("0.17.3", state.CurrentVersion);
        Assert.Null(state.RetainedPreviousVersion);
    }

    [Fact]
    public async Task InstallThenUpgradeThroughVerifiedPathRetainsPreviousImmutableVersion()
    {
        var v1 = CreateFixture("0.17.2", "previous");
        var bundle1 = await DownloadBundleAsync(v1, currentInstalledVersion: null);
        var staged1 = await WindowsByeDpiVerifiedBundleStager.StageForTestingAsync(
            bundle1,
            StagingBase,
            TestContext.Current.CancellationToken);
        var installed = await WindowsByeDpiVerifiedStagingPromoter.PromoteForTestingAsync(
            bundle1,
            staged1,
            ByeDpiPackageLifecycleOperation.Install,
            false,
            StagingBase,
            ManagedBase,
            TestContext.Current.CancellationToken);

        var v2 = CreateFixture("0.17.3", "current");
        var bundle2 = await DownloadBundleAsync(v2, currentInstalledVersion: "0.17.2");
        var staged2 = await WindowsByeDpiVerifiedBundleStager.StageForTestingAsync(
            bundle2,
            StagingBase,
            TestContext.Current.CancellationToken);
        var upgraded = await WindowsByeDpiVerifiedStagingPromoter.PromoteForTestingAsync(
            bundle2,
            staged2,
            ByeDpiPackageLifecycleOperation.Upgrade,
            false,
            StagingBase,
            ManagedBase,
            TestContext.Current.CancellationToken);

        Assert.Equal("0.17.3", upgraded.CurrentVersion);
        Assert.Equal("0.17.2", upgraded.RetainedPreviousVersion);
        Assert.True(Directory.Exists(installed.ManagedPackageRoot));
        Assert.True(Directory.Exists(upgraded.ManagedPackageRoot));

        var state = WindowsByeDpiAtomicPackagePromoter.GetStateForTesting(ManagedBase);
        Assert.Equal("0.17.3", state.CurrentVersion);
        Assert.Equal("0.17.2", state.RetainedPreviousVersion);
    }

    [Fact]
    public async Task ActiveEngineLeaseBlocksPromotionAndPreservesVerifiedStaging()
    {
        var fixture = CreateFixture("0.17.3", "lease-blocked");
        var bundle = await DownloadBundleAsync(fixture, currentInstalledVersion: null);
        var staged = await WindowsByeDpiVerifiedBundleStager.StageForTestingAsync(
            bundle,
            StagingBase,
            TestContext.Current.CancellationToken);

        var exception = await Assert.ThrowsAsync<WindowsByeDpiPromotionException>(() =>
            WindowsByeDpiVerifiedStagingPromoter.PromoteForTestingAsync(
                bundle,
                staged,
                ByeDpiPackageLifecycleOperation.Install,
                engineLeaseActive: true,
                StagingBase,
                ManagedBase,
                TestContext.Current.CancellationToken));

        Assert.Contains("BlockActiveEngineLease", exception.Code, StringComparison.Ordinal);
        Assert.True(Directory.Exists(staged.StagingRoot));
        Assert.Null(WindowsByeDpiAtomicPackagePromoter.GetStateForTesting(ManagedBase).CurrentVersion);
    }

    [Fact]
    public async Task StagedTreeMutationIsRejectedBeforeAtomicPromotion()
    {
        var fixture = CreateFixture("0.17.3", "mutated-after-staging");
        var bundle = await DownloadBundleAsync(fixture, currentInstalledVersion: null);
        var staged = await WindowsByeDpiVerifiedBundleStager.StageForTestingAsync(
            bundle,
            StagingBase,
            TestContext.Current.CancellationToken);

        await File.AppendAllTextAsync(
            Path.Combine(staged.StagingRoot, "LICENSE"),
            "tamper",
            TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<UpdateSecurityException>(() =>
            WindowsByeDpiVerifiedStagingPromoter.PromoteForTestingAsync(
                bundle,
                staged,
                ByeDpiPackageLifecycleOperation.Install,
                false,
                StagingBase,
                ManagedBase,
                TestContext.Current.CancellationToken));

        Assert.True(Directory.Exists(staged.StagingRoot));
        Assert.Null(WindowsByeDpiAtomicPackagePromoter.GetStateForTesting(ManagedBase).CurrentVersion);
    }

    [Theory]
    [InlineData(ByeDpiPackageLifecycleOperation.Repair)]
    [InlineData(ByeDpiPackageLifecycleOperation.Rollback)]
    [InlineData(ByeDpiPackageLifecycleOperation.Uninstall)]
    public async Task BridgeRejectsOperationsOutsideInstallUpgradeBoundary(
        ByeDpiPackageLifecycleOperation operation)
    {
        var fixture = CreateFixture("0.17.3", "unsupported-operation");
        var bundle = await DownloadBundleAsync(fixture, currentInstalledVersion: null);
        var staged = await WindowsByeDpiVerifiedBundleStager.StageForTestingAsync(
            bundle,
            StagingBase,
            TestContext.Current.CancellationToken);

        var exception = await Assert.ThrowsAsync<UpdateSecurityException>(() =>
            WindowsByeDpiVerifiedStagingPromoter.PromoteForTestingAsync(
                bundle,
                staged,
                operation,
                false,
                StagingBase,
                ManagedBase,
                TestContext.Current.CancellationToken));

        Assert.Equal("ByeDpiVerifiedPromotionInvalid", exception.Code);
        Assert.True(Directory.Exists(staged.StagingRoot));
    }

    public void Dispose()
    {
        if (Directory.Exists(tempRoot))
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    private static async Task<ByeDpiVerifiedReleaseBundle> DownloadBundleAsync(
        Fixture fixture,
        string? currentInstalledVersion)
    {
        using var handler = new StubHandler((request, _) =>
        {
            var uri = request.RequestUri
                ?? throw new InvalidOperationException("Test request URI missing.");

            if (uri.AbsolutePath.EndsWith(
                    "/releases/tags/" + fixture.Descriptor.ReleaseTag,
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

            throw new InvalidOperationException("Unexpected R42.11.1 fake-handler request URI: " + uri);
        });

        using var transport = new ByeDpiIsolatedHttpTransport(handler);
        var downloader = new ByeDpiVerifiedReleaseDownloader(transport);
        return await downloader.DownloadAsync(
            fixture.Descriptor,
            currentInstalledVersion,
            TestContext.Current.CancellationToken);
    }

    private static Fixture CreateFixture(string version, string label)
    {
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["bin/ciadpi.exe"] = Encoding.ASCII.GetBytes(
                "synthetic-r42-11-1-not-executable-" + version + "-" + label),
            ["LICENSE"] = Encoding.UTF8.GetBytes(
                "Synthetic MIT notice for R42.11.1 tests only.\n")
        };

        var manifest = new ByeDpiPackageManifest(
            "byedpi",
            version,
            "x64",
            "bin/ciadpi.exe",
            "LICENSE",
            files.Select(static pair => new ByeDpiPackageFileManifest(
                pair.Key,
                pair.Value.Length,
                Sha256Hex(pair.Value))));

        var manifestDocumentBytes = ByeDpiPackageManifestDocument.Serialize(manifest);
        var payloadBytes = CreateZipBytes(files);
        var provenanceBytes = Encoding.UTF8.GetBytes(
            $"{{\"schemaVersion\":1,\"scope\":\"synthetic-r42.11.1-{version}\"}}");

        var descriptor = new ByeDpiImmutableReleaseDescriptor(
            "byedpi",
            version,
            "x64",
            "DarrenDorr",
            "NetPilot",
            "byedpi-v" + version,
            "7efde1b1296eaaa187b70e951894dde17527489c",
            "8d13f6f5939190cd27ee68696247073a00b3e7dc",
            new ByeDpiImmutableReleaseAsset(
                $"NetPilot-ByeDPI-{version}-x64.zip",
                payloadBytes.Length,
                Sha256Hex(payloadBytes)),
            new ByeDpiImmutableReleaseAsset(
                $"NetPilot-ByeDPI-{version}-x64.manifest.json",
                manifestDocumentBytes.Length,
                Sha256Hex(manifestDocumentBytes)),
            new ByeDpiImmutableReleaseAsset(
                $"NetPilot-ByeDPI-{version}-x64.provenance.json",
                provenanceBytes.Length,
                Sha256Hex(provenanceBytes)));

        var metadataJson = "{" +
            "\"id\":5112," +
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
