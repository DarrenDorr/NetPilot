using NetPilot.Updater;

namespace NetPilot.Core.Tests;

public sealed class ZapretTransactionalInstallerTests
{
    [Fact]
    public async Task First_install_creates_current_with_manifest()
    {
        using var fixture = new DeploymentFixture();
        var staging = fixture.CreatePackage("staging-1");
        var release = CreateRelease("1.10.3", 'a');
        var installer = new ZapretTransactionalInstaller(fixture.EngineRoot);

        var result = await installer.ApplyAsync(
            release,
            staging,
            TestContext.Current.CancellationToken);

        Assert.Equal("1.10.3", result.Version);
        Assert.False(result.PreviousVersionAvailable);
        Assert.True(File.Exists(Path.Combine(fixture.EngineRoot, "current", "bin", "winws.exe")));

        var manifest = await EngineInstallationManifest.ReadAsync(
            Path.Combine(fixture.EngineRoot, "current"),
            TestContext.Current.CancellationToken);
        Assert.Equal("1.10.3", manifest.Version);
    }

    [Fact]
    public async Task Upgrade_moves_old_current_to_previous()
    {
        using var fixture = new DeploymentFixture();
        var installer = new ZapretTransactionalInstaller(fixture.EngineRoot);

        await installer.ApplyAsync(
            CreateRelease("1.10.3", 'a'),
            fixture.CreatePackage("staging-old"),
            TestContext.Current.CancellationToken);

        var result = await installer.ApplyAsync(
            CreateRelease("1.10.4", 'b'),
            fixture.CreatePackage("staging-new"),
            TestContext.Current.CancellationToken);

        Assert.True(result.PreviousVersionAvailable);
        Assert.Equal(
            "1.10.4",
            (await EngineInstallationManifest.ReadAsync(
                Path.Combine(fixture.EngineRoot, "current"),
                TestContext.Current.CancellationToken)).Version);
        Assert.Equal(
            "1.10.3",
            (await EngineInstallationManifest.ReadAsync(
                Path.Combine(fixture.EngineRoot, "previous"),
                TestContext.Current.CancellationToken)).Version);
    }

    [Fact]
    public async Task Explicit_rollback_swaps_current_and_previous()
    {
        using var fixture = new DeploymentFixture();
        var installer = new ZapretTransactionalInstaller(fixture.EngineRoot);

        await installer.ApplyAsync(
            CreateRelease("1.10.3", 'a'),
            fixture.CreatePackage("staging-old"),
            TestContext.Current.CancellationToken);
        await installer.ApplyAsync(
            CreateRelease("1.10.4", 'b'),
            fixture.CreatePackage("staging-new"),
            TestContext.Current.CancellationToken);

        var result = await installer.RollbackAsync(TestContext.Current.CancellationToken);

        Assert.Equal("1.10.3", result.Version);
        Assert.Equal(
            "1.10.3",
            (await EngineInstallationManifest.ReadAsync(
                Path.Combine(fixture.EngineRoot, "current"),
                TestContext.Current.CancellationToken)).Version);
        Assert.Equal(
            "1.10.4",
            (await EngineInstallationManifest.ReadAsync(
                Path.Combine(fixture.EngineRoot, "previous"),
                TestContext.Current.CancellationToken)).Version);
    }

    [Fact]
    public async Task Downgrade_is_rejected_before_current_is_moved()
    {
        using var fixture = new DeploymentFixture();
        var installer = new ZapretTransactionalInstaller(fixture.EngineRoot);

        await installer.ApplyAsync(
            CreateRelease("1.10.4", 'b'),
            fixture.CreatePackage("staging-new"),
            TestContext.Current.CancellationToken);

        var exception = await Assert.ThrowsAsync<UpdateSecurityException>(
            () => installer.ApplyAsync(
                CreateRelease("1.10.3", 'a'),
                fixture.CreatePackage("staging-old"),
                TestContext.Current.CancellationToken));

        Assert.Equal("DowngradeRejected", exception.Code);
        Assert.Equal(
            "1.10.4",
            (await EngineInstallationManifest.ReadAsync(
                Path.Combine(fixture.EngineRoot, "current"),
                TestContext.Current.CancellationToken)).Version);
    }

    [Fact]
    public async Task Same_version_is_rejected_as_update()
    {
        using var fixture = new DeploymentFixture();
        var installer = new ZapretTransactionalInstaller(fixture.EngineRoot);

        await installer.ApplyAsync(
            CreateRelease("1.10.4", 'b'),
            fixture.CreatePackage("staging-first"),
            TestContext.Current.CancellationToken);

        var exception = await Assert.ThrowsAsync<UpdateSecurityException>(
            () => installer.ApplyAsync(
                CreateRelease("1.10.4", 'b'),
                fixture.CreatePackage("staging-second"),
                TestContext.Current.CancellationToken));

        Assert.Equal("VersionAlreadyInstalled", exception.Code);
    }

    [Fact]
    public async Task Pending_previous_is_restored_when_current_is_missing()
    {
        using var fixture = new DeploymentFixture();
        var installer = new ZapretTransactionalInstaller(fixture.EngineRoot);

        await installer.ApplyAsync(
            CreateRelease("1.10.3", 'a'),
            fixture.CreatePackage("staging"),
            TestContext.Current.CancellationToken);

        Directory.Move(
            Path.Combine(fixture.EngineRoot, "current"),
            Path.Combine(fixture.EngineRoot, ".rollback-pending"));

        installer.RecoverPendingTransaction();

        Assert.True(Directory.Exists(Path.Combine(fixture.EngineRoot, "current")));
        Assert.False(Directory.Exists(Path.Combine(fixture.EngineRoot, ".rollback-pending")));
    }

    [Fact]
    public async Task Manifest_collision_in_incoming_package_is_rejected()
    {
        using var fixture = new DeploymentFixture();
        var staging = fixture.CreatePackage("staging");
        File.WriteAllText(Path.Combine(staging, EngineInstallationManifest.FileName), "{}");
        var installer = new ZapretTransactionalInstaller(fixture.EngineRoot);

        var exception = await Assert.ThrowsAsync<UpdateSecurityException>(
            () => installer.ApplyAsync(
                CreateRelease("1.10.4", 'b'),
                staging,
                TestContext.Current.CancellationToken));

        Assert.Equal("InstallManifestCollision", exception.Code);
        Assert.False(Directory.Exists(Path.Combine(fixture.EngineRoot, "current")));
    }

    private static GithubZapretRelease CreateRelease(string version, char digestCharacter)
    {
        var fileName = $"zapret-discord-youtube-{version}.zip";
        var asset = new ReleaseAsset(
            version,
            fileName,
            new Uri($"https://github.com/Flowseal/zapret-discord-youtube/releases/download/{version}/{fileName}"),
            1024,
            new string(digestCharacter, 64));

        return new GithubZapretRelease(
            100,
            version,
            new Uri($"https://github.com/Flowseal/zapret-discord-youtube/releases/tag/{version}"),
            200,
            asset);
    }

    private sealed class DeploymentFixture : IDisposable
    {
        private readonly string _workspace;

        public DeploymentFixture()
        {
            _workspace = Path.Combine(
                Path.GetTempPath(),
                "NetPilotTests",
                Guid.NewGuid().ToString("N"));
            EngineRoot = Path.Combine(_workspace, "engine");
            Directory.CreateDirectory(_workspace);
        }

        public string EngineRoot { get; }

        public string CreatePackage(string name)
        {
            var root = Path.Combine(_workspace, name);
            Directory.CreateDirectory(Path.Combine(root, "bin"));
            Directory.CreateDirectory(Path.Combine(root, "lists"));
            File.WriteAllBytes(Path.Combine(root, "bin", "winws.exe"), [0x4D, 0x5A]);
            File.WriteAllText(
                Path.Combine(root, "general (ALT11).bat"),
                "@echo off\r\nstart \"zapret\" /min \"%BIN%winws.exe\" --wf-tcp=443,%GameFilterTCP%\r\n");
            return root;
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(_workspace, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
