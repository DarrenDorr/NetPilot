using NetPilot.Updater;

namespace NetPilot.Core.Tests;

public sealed class ZapretInstallationInspectorTests : IDisposable
{
    private readonly string _workspace = Path.Combine(
        Path.GetTempPath(),
        "NetPilotTests",
        Guid.NewGuid().ToString("N"));

    public ZapretInstallationInspectorTests() => Directory.CreateDirectory(_workspace);

    [Fact]
    public async Task Missing_current_is_reported_as_not_installed()
    {
        var inspector = new ZapretInstallationInspector(_workspace);

        var state = await inspector.GetStateAsync(TestContext.Current.CancellationToken);

        Assert.False(state.IsInstalled);
        Assert.Null(state.Version);
    }

    [Fact]
    public async Task Valid_current_with_netpilot_manifest_is_reported_as_installed()
    {
        var current = CreateExpectedCurrentLayout();
        var release = CreateRelease("1.10.4");
        await EngineInstallationManifest.WriteNewAsync(
            current,
            EngineInstallationManifest.CreateZapret(release),
            TestContext.Current.CancellationToken);

        var inspector = new ZapretInstallationInspector(_workspace);
        var state = await inspector.GetStateAsync(TestContext.Current.CancellationToken);

        Assert.True(state.IsInstalled);
        Assert.Equal("1.10.4", state.Version);
    }

    [Fact]
    public async Task Lookalike_package_without_netpilot_manifest_is_not_trusted()
    {
        CreateExpectedCurrentLayout();
        var inspector = new ZapretInstallationInspector(_workspace);

        var exception = await Assert.ThrowsAsync<UpdateSecurityException>(
            () => inspector.GetStateAsync(TestContext.Current.CancellationToken));

        Assert.Equal("InstalledVersionUnknown", exception.Code);
    }

    [Fact]
    public async Task Malformed_manifest_version_is_rejected()
    {
        var current = CreateExpectedCurrentLayout();
        var manifestPath = Path.Combine(current, EngineInstallationManifest.FileName);
        await File.WriteAllTextAsync(
            manifestPath,
            """
            {
              "schemaVersion": 1,
              "upstreamId": "flowseal-zapret-discord-youtube",
              "version": "1.10.4-beta",
              "sha256Hex": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
              "installedAtUtc": "2026-10-28T00:00:00+00:00"
            }
            """,
            TestContext.Current.CancellationToken);

        var inspector = new ZapretInstallationInspector(_workspace);

        var exception = await Assert.ThrowsAsync<UpdateSecurityException>(
            () => inspector.GetStateAsync(TestContext.Current.CancellationToken));

        Assert.Equal("InstalledVersionUnknown", exception.Code);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_workspace))
            {
                Directory.Delete(_workspace, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private string CreateExpectedCurrentLayout()
    {
        var current = Path.Combine(_workspace, "current");
        Directory.CreateDirectory(Path.Combine(current, "bin"));
        Directory.CreateDirectory(Path.Combine(current, "lists"));
        File.WriteAllBytes(Path.Combine(current, "bin", "winws.exe"), [1, 2, 3]);
        File.WriteAllText(Path.Combine(current, "general.bat"), "@echo off\r\n");
        return current;
    }

    private static GithubZapretRelease CreateRelease(string version)
    {
        var fileName = $"zapret-discord-youtube-{version}.zip";
        var asset = new ReleaseAsset(
            version,
            fileName,
            new Uri($"https://github.com/Flowseal/zapret-discord-youtube/releases/download/{version}/{fileName}"),
            1024,
            new string('a', 64));

        return new GithubZapretRelease(
            1,
            version,
            new Uri($"https://github.com/Flowseal/zapret-discord-youtube/releases/tag/{version}"),
            2,
            asset);
    }
}
