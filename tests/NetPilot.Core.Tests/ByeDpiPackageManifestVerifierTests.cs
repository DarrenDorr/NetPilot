using System.Security.Cryptography;
using NetPilot.Updater;

namespace NetPilot.Core.Tests;

public sealed class ByeDpiPackageManifestVerifierTests
{
    [Fact]
    public async Task Exact_synthetic_tree_matches_manifest()
    {
        using var fixture = new Fixture();
        fixture.Write("bin/ciadpi.exe", "synthetic-engine-r42-1");
        fixture.Write("LICENSE", "MIT synthetic notice");
        fixture.Write("README.md", "synthetic package");
        var manifest = fixture.BuildManifest("0.17.3");

        var verified = await ByeDpiPackageManifestVerifier.VerifyAsync(
            fixture.Root,
            manifest,
            TestContext.Current.CancellationToken);

        Assert.Equal("0.17.3", verified.Manifest.Version);
        Assert.Equal(3, verified.VerifiedFileCount);
        Assert.Equal(manifest.TotalSizeBytes, verified.VerifiedTotalBytes);
        Assert.Equal(64, manifest.CanonicalManifestSha256.Length);
        Assert.Contains("bin/ciadpi.exe", verified.RelativePaths);
        Assert.Contains("LICENSE", verified.RelativePaths);
    }

    [Fact]
    public async Task Extra_unmanifested_file_is_rejected()
    {
        using var fixture = new Fixture();
        fixture.Write("bin/ciadpi.exe", "synthetic-engine");
        fixture.Write("LICENSE", "MIT");
        var manifest = fixture.BuildManifest("0.17.3");
        fixture.Write("unexpected.dll", "extra");

        var exception = await Assert.ThrowsAsync<UpdateSecurityException>(
            () => ByeDpiPackageManifestVerifier.VerifyAsync(
                fixture.Root,
                manifest,
                TestContext.Current.CancellationToken));

        Assert.Equal("ByeDpiManifestFileSetMismatch", exception.Code);
    }

    [Fact]
    public async Task Missing_manifest_file_is_rejected()
    {
        using var fixture = new Fixture();
        fixture.Write("bin/ciadpi.exe", "synthetic-engine");
        fixture.Write("LICENSE", "MIT");
        var manifest = fixture.BuildManifest("0.17.3");
        File.Delete(Path.Combine(fixture.Root, "LICENSE"));

        var exception = await Assert.ThrowsAsync<UpdateSecurityException>(
            () => ByeDpiPackageManifestVerifier.VerifyAsync(
                fixture.Root,
                manifest,
                TestContext.Current.CancellationToken));

        Assert.Equal("ByeDpiManifestFileSetMismatch", exception.Code);
    }

    [Fact]
    public async Task Hash_mismatch_is_rejected()
    {
        using var fixture = new Fixture();
        fixture.Write("bin/ciadpi.exe", "synthetic-engine-A");
        fixture.Write("LICENSE", "MIT");
        var manifest = fixture.BuildManifest("0.17.3");
        fixture.Write("bin/ciadpi.exe", "synthetic-engine-B");

        var exception = await Assert.ThrowsAsync<UpdateSecurityException>(
            () => ByeDpiPackageManifestVerifier.VerifyAsync(
                fixture.Root,
                manifest,
                TestContext.Current.CancellationToken));

        Assert.Equal("ByeDpiManifestHashMismatch", exception.Code);
    }

    [Fact]
    public async Task Size_mismatch_is_rejected_before_hash_acceptance()
    {
        using var fixture = new Fixture();
        fixture.Write("bin/ciadpi.exe", "short");
        fixture.Write("LICENSE", "MIT");
        var files = fixture.BuildFiles().Select(file =>
            file.RelativePath == "bin/ciadpi.exe"
                ? new ByeDpiPackageFileManifest(
                    file.RelativePath,
                    file.SizeBytes + 1,
                    file.Sha256Hex)
                : file);
        var manifest = Fixture.CreateManifest("0.17.3", files);

        var exception = await Assert.ThrowsAsync<UpdateSecurityException>(
            () => ByeDpiPackageManifestVerifier.VerifyAsync(
                fixture.Root,
                manifest,
                TestContext.Current.CancellationToken));

        Assert.Equal("ByeDpiManifestSizeMismatch", exception.Code);
    }

    [Theory]
    [InlineData("../ciadpi.exe")]
    [InlineData("/bin/ciadpi.exe")]
    [InlineData("bin\\ciadpi.exe")]
    [InlineData("bin/ciadpi.exe:evil")]
    [InlineData("bin//ciadpi.exe")]
    [InlineData("bin/CON")]
    [InlineData("bin/ciadpi?.exe")]
    public void Manifest_rejects_ambiguous_or_Windows_unsafe_paths(string path)
    {
        var exception = Assert.Throws<ArgumentException>(
            () => new ByeDpiPackageFileManifest(
                path,
                1,
                new string('0', 64)));

        Assert.Contains("path", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Manifest_rejects_case_collisions()
    {
        var files = new[]
        {
            new ByeDpiPackageFileManifest("bin/ciadpi.exe", 1, new string('0', 64)),
            new ByeDpiPackageFileManifest("BIN/CIADPI.EXE", 1, new string('1', 64)),
            new ByeDpiPackageFileManifest("LICENSE", 1, new string('2', 64))
        };

        Assert.Throws<ArgumentException>(
            () => Fixture.CreateManifest("0.17.3", files));
    }

    [Theory]
    [InlineData("v0.17.3")]
    [InlineData("0.017.3")]
    [InlineData("0.17")]
    [InlineData("0.17.3.0")]
    [InlineData("0.17.3-beta")]
    public void Manifest_version_is_exact_three_part_numeric_identity(string version)
    {
        var files = new[]
        {
            new ByeDpiPackageFileManifest("bin/ciadpi.exe", 1, new string('0', 64)),
            new ByeDpiPackageFileManifest("LICENSE", 1, new string('1', 64))
        };

        Assert.Throws<ArgumentException>(
            () => Fixture.CreateManifest(version, files));
    }

    private sealed class Fixture : IDisposable
    {
        public Fixture()
        {
            Root = Path.Combine(
                Path.GetTempPath(),
                "NetPilot.ByeDpiManifestTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public void Write(string relativePath, string content)
        {
            var fullPath = Path.Combine(
                Root,
                relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, content);
        }

        public ByeDpiPackageManifest BuildManifest(string version) =>
            CreateManifest(version, BuildFiles());

        public IReadOnlyList<ByeDpiPackageFileManifest> BuildFiles() =>
            Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories)
                .Select(path =>
                {
                    var relative = Path.GetRelativePath(Root, path)
                        .Replace(Path.DirectorySeparatorChar, '/');
                    var bytes = File.ReadAllBytes(path);
                    return new ByeDpiPackageFileManifest(
                        relative,
                        bytes.LongLength,
                        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
                })
                .OrderBy(static file => file.RelativePath, StringComparer.Ordinal)
                .ToArray();

        public static ByeDpiPackageManifest CreateManifest(
            string version,
            IEnumerable<ByeDpiPackageFileManifest> files) =>
            new(
                ByeDpiPackageManifest.RequiredEngineId,
                version,
                ByeDpiPackageManifest.RequiredArchitecture,
                ByeDpiPackageManifest.RequiredExecutableRelativePath,
                ByeDpiPackageManifest.RequiredLicenseRelativePath,
                files);

        public void Dispose()
        {
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch
            {
                // Test cleanup is best-effort and must not hide the assertion result.
            }
        }
    }
}
