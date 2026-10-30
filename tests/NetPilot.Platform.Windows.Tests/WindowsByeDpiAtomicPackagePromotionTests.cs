using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using NetPilot.Updater;

namespace NetPilot.Platform.Windows.Tests;

public sealed class WindowsByeDpiAtomicPackagePromotionTests : IDisposable
{
    private readonly string tempRoot = Path.Combine(
        Path.GetTempPath(),
        "NetPilot-R42-3-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task InstallAtomicallyPromotesVerifiedTransactionAndCommitsState()
    {
        var stagingBase = Path.Combine(tempRoot, "staging");
        var managedBase = Path.Combine(tempRoot, "managed");
        var manifest = CreateManifest("0.17.2", "install");
        var source = await StageAndTrustAsync(stagingBase, manifest);
        var originalStagingRoot = GetTransactionRoot(stagingBase, source.TransactionId);

        var result = await WindowsByeDpiAtomicPackagePromoter.PromoteForTestingAsync(
            source,
            ByeDpiPackageLifecycleOperation.Install,
            engineLeaseActive: false,
            managedBase,
            stagingBase);

        Assert.Equal(ByeDpiPackageLifecycleOperation.Install, result.Operation);
        Assert.Equal("0.17.2", result.CurrentVersion);
        Assert.Null(result.RetainedPreviousVersion);
        Assert.False(Directory.Exists(originalStagingRoot));
        Assert.True(File.Exists(Path.Combine(result.ManagedPackageRoot, "bin", "ciadpi.exe")));
        Assert.True(File.Exists(Path.Combine(result.ManagedPackageRoot, "LICENSE")));

        var state = WindowsByeDpiAtomicPackagePromoter.GetStateForTesting(managedBase);
        Assert.Equal("0.17.2", state.CurrentVersion);
        Assert.Null(state.RetainedPreviousVersion);
        Assert.False(File.Exists(Path.Combine(stagingBase, "Journal", source.TransactionId + ".json")));
        Assert.Empty(Directory.EnumerateFiles(
            Path.Combine(managedBase, ".state", "journal"),
            "*.json"));
        Assert.Empty(Directory.EnumerateFiles(
            Path.Combine(managedBase, ".state", "incoming-manifests"),
            "*.json"));
    }

    [Fact]
    public async Task UpgradeRetainsPreviousImmutableVersionAndRollbackSwapsOnlyTrustedState()
    {
        var stagingBase = Path.Combine(tempRoot, "staging");
        var managedBase = Path.Combine(tempRoot, "managed");

        var v1 = CreateManifest("0.17.2", "previous");
        var source1 = await StageAndTrustAsync(stagingBase, v1);
        var installed = await WindowsByeDpiAtomicPackagePromoter.PromoteForTestingAsync(
            source1,
            ByeDpiPackageLifecycleOperation.Install,
            engineLeaseActive: false,
            managedBase,
            stagingBase);

        var v2 = CreateManifest("0.17.3", "current");
        var source2 = await StageAndTrustAsync(stagingBase, v2);
        var upgraded = await WindowsByeDpiAtomicPackagePromoter.PromoteForTestingAsync(
            source2,
            ByeDpiPackageLifecycleOperation.Upgrade,
            engineLeaseActive: false,
            managedBase,
            stagingBase);

        Assert.Equal("0.17.3", upgraded.CurrentVersion);
        Assert.Equal("0.17.2", upgraded.RetainedPreviousVersion);
        Assert.True(Directory.Exists(installed.ManagedPackageRoot));
        Assert.True(Directory.Exists(upgraded.ManagedPackageRoot));

        var rollback = await WindowsByeDpiAtomicPackagePromoter.RollbackForTestingAsync(
            engineLeaseActive: false,
            managedBase);

        Assert.Equal(ByeDpiPackageLifecycleOperation.Rollback, rollback.Operation);
        Assert.Equal("0.17.2", rollback.CurrentVersion);
        Assert.Equal("0.17.3", rollback.RetainedPreviousVersion);
        Assert.True(Directory.Exists(installed.ManagedPackageRoot));
        Assert.True(Directory.Exists(upgraded.ManagedPackageRoot));

        var state = WindowsByeDpiAtomicPackagePromoter.GetStateForTesting(managedBase);
        Assert.Equal("0.17.2", state.CurrentVersion);
        Assert.Equal("0.17.3", state.RetainedPreviousVersion);
    }

    [Fact]
    public async Task ActiveEngineLeaseBlocksPromotionBeforePackageMove()
    {
        var stagingBase = Path.Combine(tempRoot, "staging");
        var managedBase = Path.Combine(tempRoot, "managed");
        var manifest = CreateManifest("0.17.2", "blocked");
        var source = await StageAndTrustAsync(stagingBase, manifest);
        var stagingRoot = GetTransactionRoot(stagingBase, source.TransactionId);

        var exception = await Assert.ThrowsAsync<WindowsByeDpiPromotionException>(() =>
            WindowsByeDpiAtomicPackagePromoter.PromoteForTestingAsync(
                source,
                ByeDpiPackageLifecycleOperation.Install,
                engineLeaseActive: true,
                managedBase,
                stagingBase));

        Assert.Contains("BlockActiveEngineLease", exception.Code, StringComparison.Ordinal);
        Assert.True(Directory.Exists(stagingRoot));
        Assert.Null(WindowsByeDpiAtomicPackagePromoter.GetStateForTesting(managedBase).CurrentVersion);
    }

    [Fact]
    public async Task ExistingImmutableTargetIsNeverOverwritten()
    {
        var stagingBase = Path.Combine(tempRoot, "staging");
        var managedBase = Path.Combine(tempRoot, "managed");
        var manifest = CreateManifest("0.17.2", "first");
        var first = await StageAndTrustAsync(stagingBase, manifest);
        await WindowsByeDpiAtomicPackagePromoter.PromoteForTestingAsync(
            first,
            ByeDpiPackageLifecycleOperation.Install,
            engineLeaseActive: false,
            managedBase,
            stagingBase);

        // A second verified tree for the same version is valid staging metadata, but
        // R42.3 must refuse to overwrite the immutable managed version.
        var secondManifest = CreateManifest("0.17.2", "different-content");
        var second = await StageAndTrustAsync(stagingBase, secondManifest);

        var exception = await Assert.ThrowsAsync<WindowsByeDpiPromotionException>(() =>
            WindowsByeDpiAtomicPackagePromoter.PromoteForTestingAsync(
                second,
                ByeDpiPackageLifecycleOperation.Install,
                engineLeaseActive: false,
                managedBase,
                stagingBase));

        Assert.Contains("BlockCurrentPackageAlreadyExists", exception.Code, StringComparison.Ordinal);
        Assert.True(Directory.Exists(GetTransactionRoot(stagingBase, second.TransactionId)));
    }

    [Fact]
    public async Task RollbackIsTargetFreeAndBlockedWhenLeaseIsActive()
    {
        var publicRollback = typeof(WindowsByeDpiAtomicPackagePromoter)
            .GetMethods()
            .Single(method => method.Name == nameof(WindowsByeDpiAtomicPackagePromoter.RollbackAsync));
        Assert.DoesNotContain(
            publicRollback.GetParameters(),
            parameter => parameter.ParameterType == typeof(string));

        var stagingBase = Path.Combine(tempRoot, "staging");
        var managedBase = Path.Combine(tempRoot, "managed");
        var v1 = CreateManifest("0.17.2", "one");
        var v2 = CreateManifest("0.17.3", "two");
        var source1 = await StageAndTrustAsync(stagingBase, v1);
        await WindowsByeDpiAtomicPackagePromoter.PromoteForTestingAsync(
            source1,
            ByeDpiPackageLifecycleOperation.Install,
            false,
            managedBase,
            stagingBase);
        var source2 = await StageAndTrustAsync(stagingBase, v2);
        await WindowsByeDpiAtomicPackagePromoter.PromoteForTestingAsync(
            source2,
            ByeDpiPackageLifecycleOperation.Upgrade,
            false,
            managedBase,
            stagingBase);

        var exception = await Assert.ThrowsAsync<WindowsByeDpiPromotionException>(() =>
            WindowsByeDpiAtomicPackagePromoter.RollbackForTestingAsync(
                engineLeaseActive: true,
                managedBase));

        Assert.Contains("BlockActiveEngineLease", exception.Code, StringComparison.Ordinal);
        var state = WindowsByeDpiAtomicPackagePromoter.GetStateForTesting(managedBase);
        Assert.Equal("0.17.3", state.CurrentVersion);
        Assert.Equal("0.17.2", state.RetainedPreviousVersion);
    }

    [Fact]
    public async Task PreparedPromotionRecoveryAbortsWithoutMovingVerifiedStaging()
    {
        var stagingBase = Path.Combine(tempRoot, "staging");
        var managedBase = Path.Combine(tempRoot, "managed");
        var manifest = CreateManifest("0.17.2", "recover-prepared");
        var source = await StageAndTrustAsync(stagingBase, manifest);

        // Trigger a deterministic pre-move failure after managed metadata preparation by
        // creating the canonical target directory first. The promotion journal/incoming
        // manifest are not written because overwrite is rejected before Prepared.
        var targetRoot = Path.Combine(managedBase, "0.17.2", "x64");
        Directory.CreateDirectory(targetRoot);

        await Assert.ThrowsAsync<WindowsByeDpiPromotionException>(() =>
            WindowsByeDpiAtomicPackagePromoter.PromoteForTestingAsync(
                source,
                ByeDpiPackageLifecycleOperation.Install,
                false,
                managedBase,
                stagingBase));

        var recovery = await WindowsByeDpiAtomicPackagePromoter.RecoverForTestingAsync(
            managedBase,
            stagingBase);
        Assert.Equal(0, recovery.CompletedMovedPromotions);
        Assert.True(Directory.Exists(GetTransactionRoot(stagingBase, source.TransactionId)));
    }

    public void Dispose()
    {
        if (Directory.Exists(tempRoot))
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    private static async Task<WindowsByeDpiVerifiedPromotionSource> StageAndTrustAsync(
        string stagingBase,
        ByeDpiPackageManifest manifest)
    {
        var files = CreateFiles(manifest.Version, manifest.CanonicalManifestSha256[..8]);
        // Rebuild the manifest over the exact payload that will be staged. The caller's
        // label is intentionally irrelevant to the trust boundary.
        manifest = CreateManifestFromFiles(manifest.Version, files);
        await using var zip = CreateZip(files);
        var staged = await WindowsByeDpiProtectedStagingTransaction.StageAndVerifyForTestingAsync(
            zip,
            manifest,
            stagingBase);
        return await WindowsByeDpiPromotionSourceVerifier.VerifyForTestingAsync(
            staged,
            manifest,
            stagingBase);
    }

    private static ByeDpiPackageManifest CreateManifest(string version, string label)
    {
        var files = CreateFiles(version, label);
        return CreateManifestFromFiles(version, files);
    }

    private static ByeDpiPackageManifest CreateManifestFromFiles(
        string version,
        IReadOnlyDictionary<string, byte[]> files) =>
        new(
            ByeDpiPackageManifest.RequiredEngineId,
            version,
            ByeDpiPackageManifest.RequiredArchitecture,
            ByeDpiPackageManifest.RequiredExecutableRelativePath,
            ByeDpiPackageManifest.RequiredLicenseRelativePath,
            files.Select(static pair => new ByeDpiPackageFileManifest(
                pair.Key,
                pair.Value.Length,
                Convert.ToHexString(SHA256.HashData(pair.Value)).ToLowerInvariant())));

    private static Dictionary<string, byte[]> CreateFiles(string version, string label) =>
        new(StringComparer.Ordinal)
        {
            ["bin/ciadpi.exe"] = Encoding.ASCII.GetBytes(
                "synthetic-r42-3-not-executable-" + version + "-" + label),
            ["LICENSE"] = Encoding.UTF8.GetBytes(
                "Synthetic MIT notice for R42.3 tests only.\n")
        };

    private static MemoryStream CreateZip(IReadOnlyDictionary<string, byte[]> files)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var pair in files)
            {
                var entry = archive.CreateEntry(pair.Key, CompressionLevel.NoCompression);
                using var target = entry.Open();
                target.Write(pair.Value);
            }
        }
        stream.Position = 0;
        return stream;
    }

    private static string GetTransactionRoot(string stagingBase, string transactionId) =>
        Path.Combine(stagingBase, transactionId);
}
