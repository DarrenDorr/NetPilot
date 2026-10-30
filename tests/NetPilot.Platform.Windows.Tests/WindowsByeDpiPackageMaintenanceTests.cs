using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using NetPilot.Updater;

namespace NetPilot.Platform.Windows.Tests;

public sealed class WindowsByeDpiPackageMaintenanceTests : IDisposable
{
    private readonly string tempRoot = Path.Combine(
        Path.GetTempPath(),
        "NetPilot-R42-4-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task RepairRestoresExactCanonicalCurrentPackageWithoutChangingState()
    {
        var stagingBase = Path.Combine(tempRoot, "staging");
        var managedBase = Path.Combine(tempRoot, "managed");
        var maintenanceBase = Path.Combine(tempRoot, "maintenance");
        var payload = CreateFiles("0.17.3", "canonical");
        var installedSource = await StageAndTrustPayloadAsync(stagingBase, "0.17.3", payload);
        var installed = await WindowsByeDpiAtomicPackagePromoter.PromoteForTestingAsync(
            installedSource,
            ByeDpiPackageLifecycleOperation.Install,
            false,
            managedBase,
            stagingBase);

        var executable = Path.Combine(installed.ManagedPackageRoot, "bin", "ciadpi.exe");
        await File.WriteAllBytesAsync(executable, Encoding.ASCII.GetBytes("corrupted-current"));
        var repairSource = await StageAndTrustPayloadAsync(stagingBase, "0.17.3", payload);

        var result = await WindowsByeDpiPackageMaintenance.RepairForTestingAsync(
            repairSource,
            false,
            managedBase,
            stagingBase,
            maintenanceBase);

        Assert.Equal("0.17.3", result.Version);
        Assert.Equal(payload["bin/ciadpi.exe"], await File.ReadAllBytesAsync(executable));
        Assert.True(result.VerifiedFileCount > 0);
        var state = WindowsByeDpiAtomicPackagePromoter.GetStateForTesting(managedBase);
        Assert.Equal("0.17.3", state.CurrentVersion);
        Assert.Null(state.RetainedPreviousVersion);
        Assert.Empty(Directory.EnumerateFiles(
            Path.Combine(maintenanceBase, "Journal"),
            "*.json"));
        Assert.False(File.Exists(Path.Combine(
            stagingBase,
            "Journal",
            repairSource.TransactionId + ".json")));
    }

    [Fact]
    public async Task RepairRejectsDifferentBitsForSameVersionBeforeAnyMove()
    {
        var stagingBase = Path.Combine(tempRoot, "staging");
        var managedBase = Path.Combine(tempRoot, "managed");
        var maintenanceBase = Path.Combine(tempRoot, "maintenance");
        var canonicalPayload = CreateFiles("0.17.3", "canonical");
        var installedSource = await StageAndTrustPayloadAsync(
            stagingBase,
            "0.17.3",
            canonicalPayload);
        var installed = await WindowsByeDpiAtomicPackagePromoter.PromoteForTestingAsync(
            installedSource,
            ByeDpiPackageLifecycleOperation.Install,
            false,
            managedBase,
            stagingBase);

        var differentSource = await StageAndTrustPayloadAsync(
            stagingBase,
            "0.17.3",
            CreateFiles("0.17.3", "different"));
        var differentStagingRoot = Path.Combine(stagingBase, differentSource.TransactionId);

        var exception = await Assert.ThrowsAsync<WindowsByeDpiMaintenanceException>(() =>
            WindowsByeDpiPackageMaintenance.RepairForTestingAsync(
                differentSource,
                false,
                managedBase,
                stagingBase,
                maintenanceBase));

        Assert.Equal("ByeDpiRepairManifestMismatch", exception.Code);
        Assert.True(Directory.Exists(installed.ManagedPackageRoot));
        Assert.True(Directory.Exists(differentStagingRoot));
        Assert.Equal(
            canonicalPayload["bin/ciadpi.exe"],
            await File.ReadAllBytesAsync(Path.Combine(installed.ManagedPackageRoot, "bin", "ciadpi.exe")));
    }

    [Fact]
    public async Task ActiveEngineLeaseBlocksRepairAndUninstallBeforeMutation()
    {
        var stagingBase = Path.Combine(tempRoot, "staging");
        var managedBase = Path.Combine(tempRoot, "managed");
        var maintenanceBase = Path.Combine(tempRoot, "maintenance");
        var payload = CreateFiles("0.17.3", "lease");
        var installedSource = await StageAndTrustPayloadAsync(stagingBase, "0.17.3", payload);
        var installed = await WindowsByeDpiAtomicPackagePromoter.PromoteForTestingAsync(
            installedSource,
            ByeDpiPackageLifecycleOperation.Install,
            false,
            managedBase,
            stagingBase);
        var repairSource = await StageAndTrustPayloadAsync(stagingBase, "0.17.3", payload);

        var repairException = await Assert.ThrowsAsync<WindowsByeDpiMaintenanceException>(() =>
            WindowsByeDpiPackageMaintenance.RepairForTestingAsync(
                repairSource,
                true,
                managedBase,
                stagingBase,
                maintenanceBase));
        Assert.Contains("BlockActiveEngineLease", repairException.Code, StringComparison.Ordinal);

        var uninstallException = await Assert.ThrowsAsync<WindowsByeDpiMaintenanceException>(() =>
            WindowsByeDpiPackageMaintenance.UninstallForTestingAsync(
                true,
                managedBase,
                maintenanceBase));
        Assert.Contains("BlockActiveEngineLease", uninstallException.Code, StringComparison.Ordinal);
        Assert.True(Directory.Exists(installed.ManagedPackageRoot));
        Assert.True(Directory.Exists(Path.Combine(stagingBase, repairSource.TransactionId)));
    }

    [Fact]
    public async Task UninstallAtomicallyRemovesWholeManagedByeDpiRootIncludingRetainedPrevious()
    {
        var stagingBase = Path.Combine(tempRoot, "staging");
        var managedBase = Path.Combine(tempRoot, "managed");
        var maintenanceBase = Path.Combine(tempRoot, "maintenance");
        var v1 = await StageAndTrustPayloadAsync(
            stagingBase,
            "0.17.2",
            CreateFiles("0.17.2", "previous"));
        await WindowsByeDpiAtomicPackagePromoter.PromoteForTestingAsync(
            v1,
            ByeDpiPackageLifecycleOperation.Install,
            false,
            managedBase,
            stagingBase);
        var v2 = await StageAndTrustPayloadAsync(
            stagingBase,
            "0.17.3",
            CreateFiles("0.17.3", "current"));
        await WindowsByeDpiAtomicPackagePromoter.PromoteForTestingAsync(
            v2,
            ByeDpiPackageLifecycleOperation.Upgrade,
            false,
            managedBase,
            stagingBase);

        var result = await WindowsByeDpiPackageMaintenance.UninstallForTestingAsync(
            false,
            managedBase,
            maintenanceBase);

        Assert.Equal("0.17.3", result.RemovedCurrentVersion);
        Assert.Equal("0.17.2", result.RemovedRetainedPreviousVersion);
        Assert.False(Directory.Exists(managedBase));
        Assert.Empty(Directory.EnumerateFiles(
            Path.Combine(maintenanceBase, "Journal"),
            "*.json"));
        Assert.Empty(Directory.EnumerateDirectories(
            Path.Combine(maintenanceBase, "UninstallTombstones")));
    }

    [Fact]
    public async Task RecoveryRestoresOriginalPackageAfterCrashFollowingCurrentMove()
    {
        var stagingBase = Path.Combine(tempRoot, "staging");
        var managedBase = Path.Combine(tempRoot, "managed");
        var maintenanceBase = Path.Combine(tempRoot, "maintenance");
        var payload = CreateFiles("0.17.3", "restore");
        var installedSource = await StageAndTrustPayloadAsync(stagingBase, "0.17.3", payload);
        var installed = await WindowsByeDpiAtomicPackagePromoter.PromoteForTestingAsync(
            installedSource,
            ByeDpiPackageLifecycleOperation.Install,
            false,
            managedBase,
            stagingBase);
        var repairSource = await StageAndTrustPayloadAsync(stagingBase, "0.17.3", payload);
        var repairStagingRoot = Path.Combine(stagingBase, repairSource.TransactionId);

        var exception = await Assert.ThrowsAsync<WindowsByeDpiMaintenanceException>(() =>
            WindowsByeDpiPackageMaintenance.RepairForTestingAsync(
                repairSource,
                false,
                managedBase,
                stagingBase,
                maintenanceBase,
                WindowsByeDpiMaintenanceTestFaultPoint.AfterRepairCurrentMove));
        Assert.Equal("SyntheticTestFault", exception.Code);
        Assert.False(Directory.Exists(installed.ManagedPackageRoot));
        Assert.True(Directory.Exists(repairStagingRoot));

        var report = await WindowsByeDpiPackageMaintenance.RecoverForTestingAsync(
            managedBase,
            stagingBase,
            maintenanceBase);

        Assert.Equal(1, report.RestoredRepairs);
        Assert.True(Directory.Exists(installed.ManagedPackageRoot));
        Assert.True(Directory.Exists(repairStagingRoot));
        Assert.Empty(Directory.EnumerateFiles(
            Path.Combine(maintenanceBase, "Journal"),
            "*.json"));
    }

    [Fact]
    public async Task RecoveryCompletesValidRepairAfterReplacementMove()
    {
        var stagingBase = Path.Combine(tempRoot, "staging");
        var managedBase = Path.Combine(tempRoot, "managed");
        var maintenanceBase = Path.Combine(tempRoot, "maintenance");
        var payload = CreateFiles("0.17.3", "complete");
        var installedSource = await StageAndTrustPayloadAsync(stagingBase, "0.17.3", payload);
        var installed = await WindowsByeDpiAtomicPackagePromoter.PromoteForTestingAsync(
            installedSource,
            ByeDpiPackageLifecycleOperation.Install,
            false,
            managedBase,
            stagingBase);
        var executable = Path.Combine(installed.ManagedPackageRoot, "bin", "ciadpi.exe");
        await File.WriteAllBytesAsync(executable, Encoding.ASCII.GetBytes("corrupted-before-repair"));
        var repairSource = await StageAndTrustPayloadAsync(stagingBase, "0.17.3", payload);

        var exception = await Assert.ThrowsAsync<WindowsByeDpiMaintenanceException>(() =>
            WindowsByeDpiPackageMaintenance.RepairForTestingAsync(
                repairSource,
                false,
                managedBase,
                stagingBase,
                maintenanceBase,
                WindowsByeDpiMaintenanceTestFaultPoint.AfterRepairReplacementMove));
        Assert.Equal("SyntheticTestFault", exception.Code);
        Assert.False(Directory.Exists(Path.Combine(stagingBase, repairSource.TransactionId)));

        var report = await WindowsByeDpiPackageMaintenance.RecoverForTestingAsync(
            managedBase,
            stagingBase,
            maintenanceBase);

        Assert.Equal(1, report.CompletedRepairs);
        Assert.Equal(payload["bin/ciadpi.exe"], await File.ReadAllBytesAsync(executable));
        Assert.Empty(Directory.EnumerateFiles(
            Path.Combine(maintenanceBase, "Journal"),
            "*.json"));
        Assert.False(File.Exists(Path.Combine(
            stagingBase,
            "Journal",
            repairSource.TransactionId + ".json")));
    }

    [Fact]
    public async Task RecoveryCompletesUninstallAfterManagedRootWasAtomicallyMoved()
    {
        var stagingBase = Path.Combine(tempRoot, "staging");
        var managedBase = Path.Combine(tempRoot, "managed");
        var maintenanceBase = Path.Combine(tempRoot, "maintenance");
        var source = await StageAndTrustPayloadAsync(
            stagingBase,
            "0.17.3",
            CreateFiles("0.17.3", "uninstall-recovery"));
        await WindowsByeDpiAtomicPackagePromoter.PromoteForTestingAsync(
            source,
            ByeDpiPackageLifecycleOperation.Install,
            false,
            managedBase,
            stagingBase);

        var exception = await Assert.ThrowsAsync<WindowsByeDpiMaintenanceException>(() =>
            WindowsByeDpiPackageMaintenance.UninstallForTestingAsync(
                false,
                managedBase,
                maintenanceBase,
                WindowsByeDpiMaintenanceTestFaultPoint.AfterUninstallManagedMove));
        Assert.Equal("SyntheticTestFault", exception.Code);
        Assert.False(Directory.Exists(managedBase));
        Assert.NotEmpty(Directory.EnumerateDirectories(
            Path.Combine(maintenanceBase, "UninstallTombstones")));

        var report = await WindowsByeDpiPackageMaintenance.RecoverForTestingAsync(
            managedBase,
            stagingBase,
            maintenanceBase);

        Assert.Equal(1, report.CompletedUninstalls);
        Assert.False(Directory.Exists(managedBase));
        Assert.Empty(Directory.EnumerateDirectories(
            Path.Combine(maintenanceBase, "UninstallTombstones")));
        Assert.Empty(Directory.EnumerateFiles(
            Path.Combine(maintenanceBase, "Journal"),
            "*.json"));
    }

    [Fact]
    public void PublicMaintenanceApiAcceptsNoRawFilesystemPathOrUninstallTarget()
    {
        var publicMethods = typeof(WindowsByeDpiPackageMaintenance)
            .GetMethods()
            .Where(static method => method.DeclaringType == typeof(WindowsByeDpiPackageMaintenance))
            .ToArray();

        Assert.DoesNotContain(
            publicMethods.SelectMany(static method => method.GetParameters()),
            parameter => parameter.ParameterType == typeof(string));

        var uninstall = publicMethods.Single(
            method => method.Name == nameof(WindowsByeDpiPackageMaintenance.UninstallAsync));
        Assert.DoesNotContain(
            uninstall.GetParameters(),
            parameter => parameter.ParameterType == typeof(WindowsByeDpiVerifiedPromotionSource));
    }

    public void Dispose()
    {
        if (Directory.Exists(tempRoot))
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    private static async Task<WindowsByeDpiVerifiedPromotionSource> StageAndTrustPayloadAsync(
        string stagingBase,
        string version,
        IReadOnlyDictionary<string, byte[]> files)
    {
        var manifest = CreateManifestFromFiles(version, files);
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
                "synthetic-r42-4-not-executable-" + version + "-" + label),
            ["LICENSE"] = Encoding.UTF8.GetBytes(
                "Synthetic MIT notice for R42.4 tests only.\n")
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
}