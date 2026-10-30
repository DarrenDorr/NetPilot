using System.Security.Cryptography;
using System.Text;
using NetPilot.Updater;

namespace NetPilot.Core.Tests;

public sealed class ByeDpiPackageManifestDocumentTests
{
    [Fact]
    public void DeterministicRoundTripPreservesCanonicalManifest()
    {
        var manifest = CreateManifest();

        var first = ByeDpiPackageManifestDocument.Serialize(manifest);
        var second = ByeDpiPackageManifestDocument.Serialize(manifest);
        var parsed = ByeDpiPackageManifestDocument.Parse(first);

        Assert.Equal(first, second);
        Assert.Equal(manifest.CanonicalManifestSha256, parsed.CanonicalManifestSha256);
        Assert.Equal(manifest.Version, parsed.Version);
        Assert.Equal(
            manifest.Files.Select(static file => file.RelativePath),
            parsed.Files.Select(static file => file.RelativePath));
    }

    [Fact]
    public void UnknownRootPropertyIsRejected()
    {
        var json = Encoding.UTF8.GetString(
            ByeDpiPackageManifestDocument.Serialize(CreateManifest()));
        json = json[..^1] + ",\"unexpected\":true}";

        var exception = Assert.Throws<UpdateSecurityException>(() =>
            ByeDpiPackageManifestDocument.Parse(Encoding.UTF8.GetBytes(json)));

        Assert.Equal("ByeDpiManifestDocumentInvalid", exception.Code);
    }

    [Fact]
    public void DuplicatePropertyIsRejected()
    {
        var json = Encoding.UTF8.GetString(
            ByeDpiPackageManifestDocument.Serialize(CreateManifest()));
        json = json.Replace(
            "\"schemaVersion\":1",
            "\"schemaVersion\":1,\"schemaVersion\":1",
            StringComparison.Ordinal);

        var exception = Assert.Throws<UpdateSecurityException>(() =>
            ByeDpiPackageManifestDocument.Parse(Encoding.UTF8.GetBytes(json)));

        Assert.Equal("ByeDpiManifestDocumentInvalid", exception.Code);
    }

    [Fact]
    public void CanonicalDigestDriftIsRejected()
    {
        var json = Encoding.UTF8.GetString(
            ByeDpiPackageManifestDocument.Serialize(CreateManifest()));
        var manifest = CreateManifest();
        var replacement = new string('0', 64);
        Assert.NotEqual(manifest.CanonicalManifestSha256, replacement);
        json = json.Replace(manifest.CanonicalManifestSha256, replacement, StringComparison.Ordinal);

        var exception = Assert.Throws<UpdateSecurityException>(() =>
            ByeDpiPackageManifestDocument.Parse(Encoding.UTF8.GetBytes(json)));

        Assert.Equal("ByeDpiManifestDocumentDigestMismatch", exception.Code);
    }

    [Fact]
    public void NonCanonicalFileHashIsRejected()
    {
        var json = Encoding.UTF8.GetString(
            ByeDpiPackageManifestDocument.Serialize(CreateManifest()));
        var manifest = CreateManifest();
        var hash = manifest.Files[0].Sha256Hex;
        json = json.Replace(hash, hash.ToUpperInvariant(), StringComparison.Ordinal);

        var exception = Assert.Throws<UpdateSecurityException>(() =>
            ByeDpiPackageManifestDocument.Parse(Encoding.UTF8.GetBytes(json)));

        Assert.Equal("ByeDpiManifestDocumentInvalid", exception.Code);
    }

    private static ByeDpiPackageManifest CreateManifest()
    {
        var executable = Encoding.ASCII.GetBytes("synthetic-r42-5-manifest-document-not-executable");
        var license = Encoding.UTF8.GetBytes("Synthetic MIT license for R42.5 tests only.\n");
        return new ByeDpiPackageManifest(
            ByeDpiPackageManifest.RequiredEngineId,
            "0.17.3",
            ByeDpiPackageManifest.RequiredArchitecture,
            ByeDpiPackageManifest.RequiredExecutableRelativePath,
            ByeDpiPackageManifest.RequiredLicenseRelativePath,
            new[]
            {
                File("bin/ciadpi.exe", executable),
                File("LICENSE", license)
            });
    }

    private static ByeDpiPackageFileManifest File(string path, byte[] bytes) =>
        new(
            path,
            bytes.Length,
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
}
