using NetPilot.Updater;

namespace NetPilot.Core.Tests;

public sealed class AdmittedZapretReleaseSourceTests
{
    [Fact]
    public async Task No_admitted_release_is_rejected_before_transport_calls()
    {
        var transport = new FakeTransport();
        var source = new AdmittedZapretReleaseSource(
            transport,
            NoAdmittedZapretReleaseCatalog.Instance);

        var exception = await Assert.ThrowsAsync<UpdateSecurityException>(
            () => source.GetLatestReleaseAsync(TestContext.Current.CancellationToken));

        Assert.Equal("NoAdmittedEngineRelease", exception.Code);
        Assert.Equal(0, transport.MetadataCalls);
        Assert.Equal(0, transport.DownloadCalls);
    }

    [Fact]
    public async Task Admitted_release_is_returned_without_upstream_metadata_lookup()
    {
        var admitted = CreateRelease("1.10.4", releaseId: 100, assetId: 200, digestCharacter: 'a');
        var transport = new FakeTransport();
        var source = new AdmittedZapretReleaseSource(
            transport,
            new FixedCatalog(admitted));

        var result = await source.GetLatestReleaseAsync(TestContext.Current.CancellationToken);

        Assert.Same(admitted, result);
        Assert.Equal(0, transport.MetadataCalls);
        Assert.Equal(0, transport.DownloadCalls);
    }

    [Fact]
    public async Task Non_admitted_release_is_rejected_before_download_transport()
    {
        var admitted = CreateRelease("1.10.4", releaseId: 100, assetId: 200, digestCharacter: 'a');
        var supplied = CreateRelease("1.10.3", releaseId: 90, assetId: 190, digestCharacter: 'b');
        var transport = new FakeTransport();
        var source = new AdmittedZapretReleaseSource(
            transport,
            new FixedCatalog(admitted));

        var exception = await Assert.ThrowsAsync<UpdateSecurityException>(
            () => source.DownloadVerifiedAssetAsync(
                supplied,
                Path.Combine(Path.GetTempPath(), "NetPilotTests", Guid.NewGuid().ToString("N")),
                TestContext.Current.CancellationToken));

        Assert.Equal("ReleaseNotAdmitted", exception.Code);
        Assert.Equal(0, transport.DownloadCalls);
    }

    [Fact]
    public async Task Exact_admitted_release_is_delegated_to_download_transport()
    {
        var admitted = CreateRelease("1.10.4", releaseId: 100, assetId: 200, digestCharacter: 'a');
        var transport = new FakeTransport();
        var source = new AdmittedZapretReleaseSource(
            transport,
            new FixedCatalog(admitted));
        var root = Path.Combine(
            Path.GetTempPath(),
            "NetPilotTests",
            Guid.NewGuid().ToString("N"));

        var path = await source.DownloadVerifiedAssetAsync(
            admitted,
            root,
            TestContext.Current.CancellationToken);

        Assert.Equal(Path.Combine(root, "source.zip"), path);
        Assert.Equal(1, transport.DownloadCalls);
        Assert.Same(admitted, transport.LastDownloadedRelease);
    }

    private static GithubZapretRelease CreateRelease(
        string version,
        long releaseId,
        long assetId,
        char digestCharacter)
    {
        var fileName = $"zapret-discord-youtube-{version}.zip";
        var asset = new ReleaseAsset(
            version,
            fileName,
            new Uri(
                $"https://github.com/Flowseal/zapret-discord-youtube/releases/download/{version}/{fileName}"),
            1024,
            new string(digestCharacter, 64));

        return new GithubZapretRelease(
            releaseId,
            version,
            new Uri($"https://github.com/Flowseal/zapret-discord-youtube/releases/tag/{version}"),
            assetId,
            asset);
    }

    private sealed class FixedCatalog(GithubZapretRelease? admittedRelease)
        : IZapretReleaseAdmissionCatalog
    {
        public GithubZapretRelease? GetAdmittedRelease() => admittedRelease;
    }

    private sealed class FakeTransport : IZapretReleaseSource
    {
        public int MetadataCalls { get; private set; }
        public int DownloadCalls { get; private set; }
        public GithubZapretRelease? LastDownloadedRelease { get; private set; }

        public Task<GithubZapretRelease> GetLatestReleaseAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            MetadataCalls++;
            return Task.FromException<GithubZapretRelease>(
                new InvalidOperationException("Upstream metadata must not be consulted by admission wrapper."));
        }

        public Task<string> DownloadVerifiedAssetAsync(
            GithubZapretRelease release,
            string downloadRoot,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DownloadCalls++;
            LastDownloadedRelease = release;
            return Task.FromResult(Path.Combine(downloadRoot, "source.zip"));
        }
    }
}
