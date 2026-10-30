using System.Text;
using NetPilot.Updater;

namespace NetPilot.Core.Tests;

public sealed class ByeDpiImmutableReleaseDocumentTests
{
    [Fact]
    public void DeterministicRoundTripPreservesExactReleaseIdentity()
    {
        var descriptor = CreateDescriptor();

        var first = ByeDpiImmutableReleaseDocument.Serialize(descriptor);
        var second = ByeDpiImmutableReleaseDocument.Serialize(descriptor);
        var parsed = ByeDpiImmutableReleaseDocument.Parse(first);

        Assert.Equal(first, second);
        Assert.Equal(descriptor, parsed);
    }

    [Fact]
    public void UnknownRootPropertyIsRejected()
    {
        var json = Encoding.UTF8.GetString(ByeDpiImmutableReleaseDocument.Serialize(CreateDescriptor()));
        json = json[..^1] + ",\"unexpected\":true}";

        var exception = Assert.Throws<UpdateSecurityException>(() =>
            ByeDpiImmutableReleaseDocument.Parse(Encoding.UTF8.GetBytes(json)));

        Assert.Equal("ByeDpiImmutableReleaseDocumentInvalid", exception.Code);
    }

    [Fact]
    public void DuplicatePropertyIsRejected()
    {
        var json = Encoding.UTF8.GetString(ByeDpiImmutableReleaseDocument.Serialize(CreateDescriptor()));
        json = json.Replace(
            "\"schemaVersion\":1",
            "\"schemaVersion\":1,\"schemaVersion\":1",
            StringComparison.Ordinal);

        var exception = Assert.Throws<UpdateSecurityException>(() =>
            ByeDpiImmutableReleaseDocument.Parse(Encoding.UTF8.GetBytes(json)));

        Assert.Equal("ByeDpiImmutableReleaseDocumentInvalid", exception.Code);
    }

    [Fact]
    public void RepositoryDriftIsRejected()
    {
        var descriptor = CreateDescriptor() with { RepositoryOwner = "someone-else" };

        var exception = Assert.Throws<UpdateSecurityException>(() =>
            ByeDpiImmutableReleaseDocument.Serialize(descriptor));

        Assert.Equal("ByeDpiImmutableReleaseDocumentInvalid", exception.Code);
    }

    [Fact]
    public void ReleaseTagDriftIsRejected()
    {
        var descriptor = CreateDescriptor() with { ReleaseTag = "byedpi-v0.17.4" };

        var exception = Assert.Throws<UpdateSecurityException>(() =>
            ByeDpiImmutableReleaseDocument.Serialize(descriptor));

        Assert.Equal("ByeDpiImmutableReleaseDocumentInvalid", exception.Code);
    }

    [Fact]
    public void NonCanonicalVersionIsRejected()
    {
        var descriptor = CreateDescriptor() with { Version = "0.017.3", ReleaseTag = "byedpi-v0.017.3" };

        var exception = Assert.Throws<UpdateSecurityException>(() =>
            ByeDpiImmutableReleaseDocument.Serialize(descriptor));

        Assert.Equal("ByeDpiImmutableReleaseDocumentInvalid", exception.Code);
    }

    [Fact]
    public void AssetNameDriftIsRejected()
    {
        var descriptor = CreateDescriptor() with
        {
            Payload = CreateDescriptor().Payload with { Name = "other.zip" }
        };

        var exception = Assert.Throws<UpdateSecurityException>(() =>
            ByeDpiImmutableReleaseDocument.Serialize(descriptor));

        Assert.Equal("ByeDpiImmutableReleaseDocumentInvalid", exception.Code);
    }

    [Fact]
    public void UppercaseAssetHashIsRejected()
    {
        var descriptor = CreateDescriptor() with
        {
            Manifest = CreateDescriptor().Manifest with
            {
                Sha256Hex = CreateDescriptor().Manifest.Sha256Hex.ToUpperInvariant()
            }
        };

        var exception = Assert.Throws<UpdateSecurityException>(() =>
            ByeDpiImmutableReleaseDocument.Serialize(descriptor));

        Assert.Equal("ByeDpiImmutableReleaseDocumentInvalid", exception.Code);
    }

    [Fact]
    public void NonPositiveAssetSizeIsRejected()
    {
        var descriptor = CreateDescriptor() with
        {
            Provenance = CreateDescriptor().Provenance with { SizeBytes = 0 }
        };

        var exception = Assert.Throws<UpdateSecurityException>(() =>
            ByeDpiImmutableReleaseDocument.Serialize(descriptor));

        Assert.Equal("ByeDpiImmutableReleaseDocumentInvalid", exception.Code);
    }

    private static ByeDpiImmutableReleaseDescriptor CreateDescriptor() =>
        new(
            "byedpi",
            "0.17.3",
            "x64",
            "DarrenDorr",
            "NetPilot",
            "byedpi-v0.17.3",
            "7efde1b1296eaaa187b70e951894dde17527489c",
            "8d13f6f5939190cd27ee68696247073a00b3e7dc",
            new ByeDpiImmutableReleaseAsset(
                "NetPilot-ByeDPI-0.17.3-x64.zip",
                100,
                "c0fe998e3d81ccb871f09d126e7b5ee0ab82d15daf78bc44bcd63b7762313db7"),
            new ByeDpiImmutableReleaseAsset(
                "NetPilot-ByeDPI-0.17.3-x64.manifest.json",
                200,
                "eaa6f063fcb8e012810f849ed08b6bd82d4d046d4796c2fb4b903c4195985f54"),
            new ByeDpiImmutableReleaseAsset(
                "NetPilot-ByeDPI-0.17.3-x64.provenance.json",
                300,
                "e1f98760eed94f5daa7d37a868015755220cb17d389000bc49e031da511f7f31"));
}
