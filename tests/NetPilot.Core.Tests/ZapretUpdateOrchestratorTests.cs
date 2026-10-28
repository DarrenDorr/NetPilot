using System.IO.Compression;
using System.Security.Cryptography;
using NetPilot.Updater;

namespace NetPilot.Core.Tests;

public sealed class ZapretUpdateOrchestratorTests
{
    [Fact]
    public async Task Apply_latest_installs_verified_release_and_cleans_transaction_staging()
    {
        using var fixture = new OrchestratorFixture("1.10.4");

        var result = await fixture.Orchestrator.ApplyLatestAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal("1.10.4", result.Version);
        Assert.False(result.PreviousVersionAvailable);
        Assert.True(result.TemporaryCleanupCompleted);
        Assert.Equal(ZapretRuntimeRestoreStatus.NotRequired, result.RuntimeRestore.Status);
        Assert.Equal(1, fixture.ReleaseSource.MetadataCalls);
        Assert.Equal(1, fixture.ReleaseSource.DownloadCalls);
        Assert.Equal(1, fixture.RuntimeGate.CaptureCalls);
        Assert.Equal(0, fixture.RuntimeGate.RestoreCalls);
        Assert.True(File.Exists(Path.Combine(fixture.EngineRoot, "current", "bin", "winws.exe")));
        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(fixture.EngineRoot, ".staging")));
    }

    [Fact]
    public async Task Upgrade_flows_through_r10_and_preserves_previous_version()
    {
        using var fixture = new OrchestratorFixture("1.10.3");

        await fixture.Orchestrator.ApplyLatestAsync(TestContext.Current.CancellationToken);
        fixture.ReleaseSource.SetRelease("1.10.4");

        var result = await fixture.Orchestrator.ApplyLatestAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal("1.10.4", result.Version);
        Assert.True(result.PreviousVersionAvailable);
        Assert.Equal(2, fixture.RuntimeGate.CaptureCalls);

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
    public async Task Previously_running_strategy_is_restored_after_successful_update()
    {
        using var fixture = new OrchestratorFixture("1.10.4");
        fixture.RuntimeGate.StateToCapture = ZapretUpdateRuntimeState.Running("ALT11");

        var result = await fixture.Orchestrator.ApplyLatestAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(ZapretRuntimeRestoreStatus.Restored, result.RuntimeRestore.Status);
        Assert.Equal("ALT11", result.RuntimeRestore.StrategyId);
        Assert.Null(result.RuntimeRestore.ErrorCode);
        Assert.Equal(1, fixture.RuntimeGate.CaptureCalls);
        Assert.Equal(1, fixture.RuntimeGate.RestoreCalls);
        Assert.Equal("ALT11", fixture.RuntimeGate.LastRestoreState?.StrategyId);
    }

    [Fact]
    public async Task Restore_failure_is_reported_without_reverting_successful_install()
    {
        using var fixture = new OrchestratorFixture("1.10.4");
        fixture.RuntimeGate.StateToCapture = ZapretUpdateRuntimeState.Running("ALT11");
        fixture.RuntimeGate.RestoreResult = ZapretRuntimeRestoreResult.Failed(
            "ALT11",
            "StrategyUnavailableOrIncompatibleAfterUpdate");

        var result = await fixture.Orchestrator.ApplyLatestAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal("1.10.4", result.Version);
        Assert.Equal(ZapretRuntimeRestoreStatus.Failed, result.RuntimeRestore.Status);
        Assert.Equal("ALT11", result.RuntimeRestore.StrategyId);
        Assert.Equal(
            "StrategyUnavailableOrIncompatibleAfterUpdate",
            result.RuntimeRestore.ErrorCode);
        Assert.True(File.Exists(Path.Combine(fixture.EngineRoot, "current", "bin", "winws.exe")));
        Assert.Equal(1, fixture.RuntimeGate.RestoreCalls);
    }

    [Fact]
    public async Task Restore_cannot_claim_a_different_strategy_identity()
    {
        using var fixture = new OrchestratorFixture("1.10.4");
        fixture.RuntimeGate.StateToCapture = ZapretUpdateRuntimeState.Running("ALT11");
        fixture.RuntimeGate.RestoreResult = ZapretRuntimeRestoreResult.Restored("ALT12");

        var result = await fixture.Orchestrator.ApplyLatestAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(ZapretRuntimeRestoreStatus.Failed, result.RuntimeRestore.Status);
        Assert.Equal("ALT11", result.RuntimeRestore.StrategyId);
        Assert.Equal("RuntimeRestoreIdentityMismatch", result.RuntimeRestore.ErrorCode);
    }

    [Fact]
    public async Task Same_version_is_rejected_before_download_and_runtime_capture()
    {
        using var fixture = new OrchestratorFixture("1.10.4");

        await fixture.Orchestrator.ApplyLatestAsync(TestContext.Current.CancellationToken);
        var downloadsBefore = fixture.ReleaseSource.DownloadCalls;
        var capturesBefore = fixture.RuntimeGate.CaptureCalls;

        var exception = await Assert.ThrowsAsync<UpdateSecurityException>(
            () => fixture.Orchestrator.ApplyLatestAsync(TestContext.Current.CancellationToken));

        Assert.Equal("VersionAlreadyInstalled", exception.Code);
        Assert.Equal(downloadsBefore, fixture.ReleaseSource.DownloadCalls);
        Assert.Equal(capturesBefore, fixture.RuntimeGate.CaptureCalls);
    }

    [Fact]
    public async Task Invalid_archive_is_rejected_before_runtime_capture()
    {
        using var fixture = new OrchestratorFixture("1.10.4", validZip: false);

        var exception = await Assert.ThrowsAsync<UpdateSecurityException>(
            () => fixture.Orchestrator.ApplyLatestAsync(TestContext.Current.CancellationToken));

        Assert.Equal("InvalidZipArchive", exception.Code);
        Assert.Equal(0, fixture.RuntimeGate.CaptureCalls);
        Assert.False(Directory.Exists(Path.Combine(fixture.EngineRoot, "current")));
    }

    [Fact]
    public async Task Tampered_download_is_reverified_and_rejected_before_runtime_capture()
    {
        using var fixture = new OrchestratorFixture("1.10.4");
        fixture.ReleaseSource.TamperDownload = true;

        var exception = await Assert.ThrowsAsync<UpdateSecurityException>(
            () => fixture.Orchestrator.ApplyLatestAsync(TestContext.Current.CancellationToken));

        Assert.Equal("DigestMismatch", exception.Code);
        Assert.Equal(0, fixture.RuntimeGate.CaptureCalls);
        Assert.False(Directory.Exists(Path.Combine(fixture.EngineRoot, "current")));
    }

    [Fact]
    public async Task Download_source_cannot_return_path_outside_owned_transaction_root()
    {
        using var fixture = new OrchestratorFixture("1.10.4");
        fixture.ReleaseSource.ReturnExternalPath = true;

        var exception = await Assert.ThrowsAsync<UpdateSecurityException>(
            () => fixture.Orchestrator.ApplyLatestAsync(TestContext.Current.CancellationToken));

        Assert.Equal("UntrustedDownloadedAssetPath", exception.Code);
        Assert.Equal(0, fixture.RuntimeGate.CaptureCalls);
        Assert.False(Directory.Exists(Path.Combine(fixture.EngineRoot, "current")));
    }

    [Fact]
    public async Task Runtime_gate_failure_does_not_switch_current()
    {
        using var fixture = new OrchestratorFixture("1.10.4");
        fixture.RuntimeGate.CaptureFailure = new InvalidOperationException("engine did not stop");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Orchestrator.ApplyLatestAsync(TestContext.Current.CancellationToken));

        Assert.Equal(1, fixture.RuntimeGate.CaptureCalls);
        Assert.Equal(0, fixture.RuntimeGate.RestoreCalls);
        Assert.False(Directory.Exists(Path.Combine(fixture.EngineRoot, "current")));
        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(fixture.EngineRoot, ".staging")));
    }

    [Fact]
    public async Task Invalid_running_runtime_snapshot_is_rejected_before_install_switch()
    {
        using var fixture = new OrchestratorFixture("1.10.4");
        fixture.RuntimeGate.StateToCapture = new ZapretUpdateRuntimeState(true, null);

        var exception = await Assert.ThrowsAsync<UpdateSecurityException>(
            () => fixture.Orchestrator.ApplyLatestAsync(TestContext.Current.CancellationToken));

        Assert.Equal("InvalidRuntimeState", exception.Code);
        Assert.False(Directory.Exists(Path.Combine(fixture.EngineRoot, "current")));
    }

    private sealed class OrchestratorFixture : IDisposable
    {
        private readonly string _workspace;

        public OrchestratorFixture(string version, bool validZip = true)
        {
            _workspace = Path.Combine(
                Path.GetTempPath(),
                "NetPilotTests",
                Guid.NewGuid().ToString("N"));
            EngineRoot = Path.Combine(_workspace, "engine");
            Directory.CreateDirectory(_workspace);

            ReleaseSource = new FakeReleaseSource(_workspace, version, validZip);
            RuntimeGate = new FakeRuntimeGate();
            Orchestrator = new ZapretUpdateOrchestrator(
                EngineRoot,
                ReleaseSource,
                RuntimeGate);
        }

        public string EngineRoot { get; }
        public FakeReleaseSource ReleaseSource { get; }
        public FakeRuntimeGate RuntimeGate { get; }
        public ZapretUpdateOrchestrator Orchestrator { get; }

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

    private sealed class FakeRuntimeGate : IZapretUpdateRuntimeGate
    {
        public int CaptureCalls { get; private set; }
        public int RestoreCalls { get; private set; }
        public Exception? CaptureFailure { get; set; }
        public ZapretUpdateRuntimeState StateToCapture { get; set; } =
            ZapretUpdateRuntimeState.Stopped;
        public ZapretRuntimeRestoreResult? RestoreResult { get; set; }
        public ZapretUpdateRuntimeState? LastRestoreState { get; private set; }

        public Task<ZapretUpdateRuntimeState> CaptureAndStopAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CaptureCalls++;

            return CaptureFailure is null
                ? Task.FromResult(StateToCapture)
                : Task.FromException<ZapretUpdateRuntimeState>(CaptureFailure);
        }

        public Task<ZapretRuntimeRestoreResult> RestoreAsync(
            ZapretUpdateRuntimeState state,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RestoreCalls++;
            LastRestoreState = state;

            if (RestoreResult is not null)
            {
                return Task.FromResult(RestoreResult);
            }

            return Task.FromResult(
                state.WasRunning
                    ? ZapretRuntimeRestoreResult.Restored(state.StrategyId!)
                    : ZapretRuntimeRestoreResult.NotRequired);
        }
    }

    private sealed class FakeReleaseSource : IZapretReleaseSource
    {
        private readonly string _workspace;
        private readonly bool _validZip;
        private byte[] _zipBytes = [];
        private GithubZapretRelease _release = null!;

        public FakeReleaseSource(string workspace, string version, bool validZip)
        {
            _workspace = workspace;
            _validZip = validZip;
            SetRelease(version);
        }

        public int MetadataCalls { get; private set; }
        public int DownloadCalls { get; private set; }
        public bool ReturnExternalPath { get; set; }
        public bool TamperDownload { get; set; }

        public void SetRelease(string version)
        {
            _zipBytes = _validZip ? CreateValidZapretZip() : "not-a-zip"u8.ToArray();
            var digest = Convert.ToHexString(SHA256.HashData(_zipBytes)).ToLowerInvariant();
            var fileName = $"zapret-discord-youtube-{version}.zip";
            var asset = new ReleaseAsset(
                version,
                fileName,
                new Uri($"https://github.com/Flowseal/zapret-discord-youtube/releases/download/{version}/{fileName}"),
                _zipBytes.LongLength,
                digest);

            _release = new GithubZapretRelease(
                100,
                version,
                new Uri($"https://github.com/Flowseal/zapret-discord-youtube/releases/tag/{version}"),
                200,
                asset);
        }

        public Task<GithubZapretRelease> GetLatestReleaseAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            MetadataCalls++;
            return Task.FromResult(_release);
        }

        public async Task<string> DownloadVerifiedAssetAsync(
            GithubZapretRelease release,
            string downloadRoot,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(release);
            DownloadCalls++;
            Directory.CreateDirectory(downloadRoot);

            var bytes = _zipBytes.ToArray();
            if (TamperDownload && bytes.Length > 0)
            {
                bytes[^1] ^= 0x01;
            }

            if (ReturnExternalPath)
            {
                var external = Path.Combine(_workspace, "rogue.zip");
                await File.WriteAllBytesAsync(external, bytes, cancellationToken);
                return external;
            }

            var destination = Path.Combine(downloadRoot, "source.zip");
            await File.WriteAllBytesAsync(destination, bytes, cancellationToken);
            return destination;
        }

        private static byte[] CreateValidZapretZip()
        {
            using var memory = new MemoryStream();
            using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
            {
                WriteEntry(archive, "bin/winws.exe", [0x4D, 0x5A, 0x01, 0x02]);
                WriteEntry(archive, "lists/list-general.txt", "example.com\n"u8.ToArray());
                WriteEntry(
                    archive,
                    "general (ALT11).bat",
                    "@echo off\r\nstart \"zapret\" /min \"%BIN%winws.exe\" --wf-tcp=443,%GameFilterTCP%\r\n"u8.ToArray());
            }

            return memory.ToArray();
        }

        private static void WriteEntry(ZipArchive archive, string name, byte[] content)
        {
            var entry = archive.CreateEntry(name, CompressionLevel.NoCompression);
            using var stream = entry.Open();
            stream.Write(content);
        }
    }
}
