using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using NetPilot.Updater;

namespace NetPilot.Platform.Windows.Tests;

public sealed class WindowsByeDpiLiveAcquisitionPipelineTests : IDisposable
{
    private readonly string tempRoot = Path.Combine(
        Path.GetTempPath(),
        "NetPilot-R42-13-" + Guid.NewGuid().ToString("N"));

    private string StagingBase => Path.Combine(tempRoot, "staging");
    private string ManagedBase => Path.Combine(tempRoot, "managed");

    [Fact]
    public async Task Synthetic_reviewed_chain_downloads_stages_and_atomically_installs()
    {
        var fixture = CreateFixture("0.17.3", "install");
        var factoryCalls = 0;
        var pipeline = new WindowsByeDpiLiveAcquisitionPipeline(() =>
        {
            factoryCalls++;
            return CreateHandler(fixture);
        });

        var result = await pipeline.ApplyForTestingAsync(
            fixture.Descriptor,
            engineLeaseActive: false,
            StagingBase,
            ManagedBase,
            TestContext.Current.CancellationToken);

        Assert.Equal(WindowsByeDpiLiveAcquisitionAction.Install, result.Action);
        Assert.True(result.Changed);
        Assert.Equal("0.17.3", result.CurrentVersion);
        Assert.Equal(1, factoryCalls);

        var state = WindowsByeDpiAtomicPackagePromoter.GetStateForTesting(ManagedBase);
        Assert.Equal("0.17.3", state.CurrentVersion);
        Assert.True(File.Exists(Path.Combine(
            ManagedBase,
            "0.17.3",
            "x64",
            "bin",
            "ciadpi.exe")));
    }

    [Fact]
    public async Task Exact_current_version_is_noop_without_constructing_second_network_handler()
    {
        var fixture = CreateFixture("0.17.3", "no-change");
        var factoryCalls = 0;
        var pipeline = new WindowsByeDpiLiveAcquisitionPipeline(() =>
        {
            factoryCalls++;
            return CreateHandler(fixture);
        });

        var first = await pipeline.ApplyForTestingAsync(
            fixture.Descriptor,
            false,
            StagingBase,
            ManagedBase,
            TestContext.Current.CancellationToken);
        var second = await pipeline.ApplyForTestingAsync(
            fixture.Descriptor,
            false,
            StagingBase,
            ManagedBase,
            TestContext.Current.CancellationToken);

        Assert.Equal(WindowsByeDpiLiveAcquisitionAction.Install, first.Action);
        Assert.Equal(WindowsByeDpiLiveAcquisitionAction.NoChange, second.Action);
        Assert.False(second.Changed);
        Assert.Equal(1, factoryCalls);
    }

    [Fact]
    public async Task Active_engine_lease_is_rejected_before_any_network_handler_is_created()
    {
        var fixture = CreateFixture("0.17.3", "lease");
        var factoryCalls = 0;
        var pipeline = new WindowsByeDpiLiveAcquisitionPipeline(() =>
        {
            factoryCalls++;
            return CreateHandler(fixture);
        });

        var exception = await Assert.ThrowsAsync<UpdateSecurityException>(() =>
            pipeline.ApplyForTestingAsync(
                fixture.Descriptor,
                engineLeaseActive: true,
                StagingBase,
                ManagedBase,
                TestContext.Current.CancellationToken));

        Assert.Equal("ByeDpiLiveAcquisitionActiveLease", exception.Code);
        Assert.Equal(0, factoryCalls);
        Assert.False(Directory.Exists(StagingBase));
        Assert.False(Directory.Exists(ManagedBase));
    }

    [Fact]
    public async Task Upgrade_uses_existing_state_and_retains_previous_version()
    {
        var previous = CreateFixture("0.17.2", "previous");
        var current = CreateFixture("0.17.3", "current");
        Fixture active = previous;

        var pipeline = new WindowsByeDpiLiveAcquisitionPipeline(() => CreateHandler(active));
        var installed = await pipeline.ApplyForTestingAsync(
            previous.Descriptor,
            false,
            StagingBase,
            ManagedBase,
            TestContext.Current.CancellationToken);

        active = current;
        var upgraded = await pipeline.ApplyForTestingAsync(
            current.Descriptor,
            false,
            StagingBase,
            ManagedBase,
            TestContext.Current.CancellationToken);

        Assert.Equal(WindowsByeDpiLiveAcquisitionAction.Install, installed.Action);
        Assert.Equal(WindowsByeDpiLiveAcquisitionAction.Upgrade, upgraded.Action);
        Assert.Equal("0.17.3", upgraded.CurrentVersion);
        Assert.Equal("0.17.2", upgraded.RetainedPreviousVersion);
    }

    public void Dispose()
    {
        if (Directory.Exists(tempRoot))
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    private static StubHandler CreateHandler(Fixture fixture) =>
        new((request, _) =>
        {
            var uri = request.RequestUri
                ?? throw new InvalidOperationException("R42.13 test request URI missing.");

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

            throw new InvalidOperationException("Unexpected R42.13 fake request URI: " + uri);
        });

    private static Fixture CreateFixture(string version, string label)
    {
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["bin/ciadpi.exe"] = Encoding.ASCII.GetBytes(
                "synthetic-r42-13-not-executable-" + version + "-" + label),
            ["LICENSE"] = Encoding.UTF8.GetBytes("Synthetic license for R42.13 tests only.\n")
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
            $"{{\"schemaVersion\":1,\"scope\":\"synthetic-r42.13-{version}\"}}");

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
            "\"id\":5130," +
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
