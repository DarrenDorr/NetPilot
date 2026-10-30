using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NetPilot.Updater;

namespace NetPilot.Platform.Windows.Tests;

public sealed class WindowsByeDpiProtectedStagingTransactionTests : IDisposable
{
    private readonly string tempRoot = Path.Combine(
        Path.GetTempPath(),
        "NetPilot-R42-2-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SyntheticPackageStagesAndVerifiedJournalIsPreserved()
    {
        var files = CreateSyntheticFiles();
        var manifest = CreateManifest(files);
        await using var zip = CreateZip(files);

        var result = await WindowsByeDpiProtectedStagingTransaction.StageAndVerifyForTestingAsync(
            zip,
            manifest,
            tempRoot);

        Assert.Equal(manifest.CanonicalManifestSha256, result.ManifestSha256);
        Assert.Equal(files.Count, result.VerifiedFileCount);
        Assert.Equal(0, result.VerifiedSecurityObjectCount);
        Assert.True(File.Exists(Path.Combine(result.StagingRoot, "bin", "ciadpi.exe")));
        Assert.True(File.Exists(Path.Combine(result.StagingRoot, "LICENSE")));

        var journalPath = Path.Combine(tempRoot, "Journal", result.TransactionId + ".json");
        Assert.True(File.Exists(journalPath));
        var journal = await File.ReadAllTextAsync(journalPath);
        Assert.Contains("\"phase\":\"Verified\"", journal, StringComparison.Ordinal);
        Assert.DoesNotContain(result.StagingRoot, journal, StringComparison.OrdinalIgnoreCase);

        var recovery = WindowsByeDpiProtectedStagingTransaction.RecoverForTesting(tempRoot);
        Assert.Equal(0, recovery.RecoveredIncompleteTransactions);
        Assert.Equal(1, recovery.PreservedVerifiedTransactions);
        Assert.Equal(0, recovery.RemovedOrphanTransactions);
        Assert.True(Directory.Exists(result.StagingRoot));
    }

    [Fact]
    public void ExtractingJournalIsRecoveredWithoutTrustingAPathFromJson()
    {
        Directory.CreateDirectory(Path.Combine(tempRoot, "Journal"));
        var transactionId = new string('a', 32);
        var transactionRoot = Path.Combine(tempRoot, transactionId);
        Directory.CreateDirectory(transactionRoot);
        File.WriteAllText(Path.Combine(transactionRoot, "partial.bin"), "partial");

        var journalPath = Path.Combine(tempRoot, "Journal", transactionId + ".json");
        var journal = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            transactionId,
            manifestSha256 = new string('b', 64),
            version = "0.17.3",
            phase = "Extracting",
            updatedUtc = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)
        });
        File.WriteAllText(journalPath, journal);

        var recovery = WindowsByeDpiProtectedStagingTransaction.RecoverForTesting(tempRoot);

        Assert.Equal(1, recovery.RecoveredIncompleteTransactions);
        Assert.Equal(0, recovery.PreservedVerifiedTransactions);
        Assert.False(Directory.Exists(transactionRoot));
        Assert.False(File.Exists(journalPath));
    }

    [Fact]
    public async Task ManifestFailureRemovesPayloadAndJournal()
    {
        var files = CreateSyntheticFiles();
        var badManifest = new ByeDpiPackageManifest(
            "byedpi",
            "0.17.3",
            "x64",
            "bin/ciadpi.exe",
            "LICENSE",
            [
                new ByeDpiPackageFileManifest(
                    "bin/ciadpi.exe",
                    files["bin/ciadpi.exe"].Length,
                    new string('0', 64)),
                ToManifestFile("LICENSE", files["LICENSE"])
            ]);
        await using var zip = CreateZip(files);

        await Assert.ThrowsAsync<UpdateSecurityException>(() =>
            WindowsByeDpiProtectedStagingTransaction.StageAndVerifyForTestingAsync(
                zip,
                badManifest,
                tempRoot));

        var journalRoot = Path.Combine(tempRoot, "Journal");
        Assert.Empty(Directory.EnumerateFiles(journalRoot, "*.json"));
        Assert.Empty(Directory.EnumerateDirectories(tempRoot)
            .Where(path => !string.Equals(
                Path.GetFileName(path),
                "Journal",
                StringComparison.Ordinal)));
    }

    [Fact]
    public async Task ArchiveTraversalFailureLeavesNoTransactionResidue()
    {
        var files = CreateSyntheticFiles();
        var manifest = CreateManifest(files);
        await using var zip = new MemoryStream();
        using (var archive = new ZipArchive(zip, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("../escape.bin", CompressionLevel.NoCompression);
            await using var stream = entry.Open();
            await stream.WriteAsync(new byte[] { 1, 2, 3 });
        }
        zip.Position = 0;

        await Assert.ThrowsAsync<UpdateSecurityException>(() =>
            WindowsByeDpiProtectedStagingTransaction.StageAndVerifyForTestingAsync(
                zip,
                manifest,
                tempRoot));

        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(tempRoot)!, "escape.bin")));
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(tempRoot, "Journal"), "*.json"));
        Assert.Empty(Directory.EnumerateDirectories(tempRoot)
            .Where(path => !string.Equals(
                Path.GetFileName(path),
                "Journal",
                StringComparison.Ordinal)));
    }

    public void Dispose()
    {
        if (Directory.Exists(tempRoot))
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    private static Dictionary<string, byte[]> CreateSyntheticFiles() => new(StringComparer.Ordinal)
    {
        ["bin/ciadpi.exe"] = Encoding.ASCII.GetBytes("synthetic-not-executable-r42-2"),
        ["LICENSE"] = Encoding.UTF8.GetBytes("Synthetic MIT notice for R42.2 tests only.\n")
    };

    private static ByeDpiPackageManifest CreateManifest(IReadOnlyDictionary<string, byte[]> files) =>
        new(
            "byedpi",
            "0.17.3",
            "x64",
            "bin/ciadpi.exe",
            "LICENSE",
            files.Select(static pair => ToManifestFile(pair.Key, pair.Value)));

    private static ByeDpiPackageFileManifest ToManifestFile(string path, byte[] content) =>
        new(
            path,
            content.Length,
            Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant());

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