using NetPilot.Updater;

namespace NetPilot.Core.Tests;

public sealed class ByeDpiReviewedReleaseCatalogTests
{
    [Fact]
    public void Current_descriptor_matches_exact_r42_5_distribution_identity()
    {
        var descriptor = ByeDpiReviewedReleaseCatalog.Current;

        Assert.Equal("byedpi", descriptor.EngineId);
        Assert.Equal("0.17.3", descriptor.Version);
        Assert.Equal("x64", descriptor.Architecture);
        Assert.Equal("DarrenDorr", descriptor.RepositoryOwner);
        Assert.Equal("NetPilot", descriptor.RepositoryName);
        Assert.Equal("byedpi-v0.17.3", descriptor.ReleaseTag);
        Assert.Equal("7efde1b1296eaaa187b70e951894dde17527489c", descriptor.ReviewedSourceCommit);
        Assert.Equal("8d13f6f5939190cd27ee68696247073a00b3e7dc", descriptor.ReviewedSourceTree);

        Assert.Equal(496_964, descriptor.Payload.SizeBytes);
        Assert.Equal(
            "c0fe998e3d81ccb871f09d126e7b5ee0ab82d15daf78bc44bcd63b7762313db7",
            descriptor.Payload.Sha256Hex);
        Assert.Equal(511, descriptor.Manifest.SizeBytes);
        Assert.Equal(
            "eaa6f063fcb8e012810f849ed08b6bd82d4d046d4796c2fb4b903c4195985f54",
            descriptor.Manifest.Sha256Hex);
        Assert.Equal(1_726, descriptor.Provenance.SizeBytes);
        Assert.Equal(
            "e1f98760eed94f5daa7d37a868015755220cb17d389000bc49e031da511f7f31",
            descriptor.Provenance.Sha256Hex);
    }

    [Fact]
    public void Exact_reviewed_descriptor_round_trips_through_strict_r42_6_document()
    {
        var descriptor = ByeDpiReviewedReleaseCatalog.Current;
        var json = ByeDpiImmutableReleaseDocument.Serialize(descriptor);
        var restored = ByeDpiImmutableReleaseDocument.Parse(json);

        Assert.Equal(descriptor, restored);
    }

    [Fact]
    public void Reviewed_release_assets_fit_r42_7_aggregate_cap()
    {
        var descriptor = ByeDpiReviewedReleaseCatalog.Current;
        var total = descriptor.Payload.SizeBytes
            + descriptor.Manifest.SizeBytes
            + descriptor.Provenance.SizeBytes;

        Assert.Equal(499_201, total);
        Assert.True(total < ByeDpiAcquisitionTrustPolicy.MaxTotalAssetBytes);
    }
}
