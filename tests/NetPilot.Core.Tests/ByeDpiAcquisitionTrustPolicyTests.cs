using NetPilot.Updater;

namespace NetPilot.Core.Tests;

public sealed class ByeDpiAcquisitionTrustPolicyTests
{
    [Fact]
    public void CreatePlanUsesExactTagEndpointAndImmutableAssets()
    {
        var descriptor = CreateDescriptor();
        var metadata = CreateMetadata(descriptor);

        var plan = ByeDpiAcquisitionTrustPolicy.CreatePlan(descriptor, metadata, "0.17.3");

        Assert.Equal(
            "https://api.github.com/repos/DarrenDorr/NetPilot/releases/tags/byedpi-v0.17.3",
            plan.ReleaseMetadataApiUri.AbsoluteUri);
        Assert.Equal(3, plan.Assets.Count);
        Assert.Equal(ByeDpiAcquisitionTrustPolicy.MaxRedirects, plan.MaxRedirects);
        Assert.Equal(ByeDpiAcquisitionTrustPolicy.NetworkTimeoutSeconds, plan.NetworkTimeoutSeconds);
        Assert.Equal(
            new[] { descriptor.Payload.Name, descriptor.Manifest.Name, descriptor.Provenance.Name },
            plan.Assets.Select(static asset => asset.Name));
    }

    [Fact]
    public void MutableReleaseIsRejected()
    {
        var descriptor = CreateDescriptor();
        var metadata = CreateMetadata(descriptor) with { Immutable = false };

        Assert.Throws<UpdateSecurityException>(() =>
            ByeDpiAcquisitionTrustPolicy.CreatePlan(descriptor, metadata));
    }

    [Fact]
    public void DraftOrPrereleaseIsRejected()
    {
        var descriptor = CreateDescriptor();

        Assert.Throws<UpdateSecurityException>(() =>
            ByeDpiAcquisitionTrustPolicy.CreatePlan(
                descriptor,
                CreateMetadata(descriptor) with { Draft = true }));

        Assert.Throws<UpdateSecurityException>(() =>
            ByeDpiAcquisitionTrustPolicy.CreatePlan(
                descriptor,
                CreateMetadata(descriptor) with { Prerelease = true }));
    }

    [Fact]
    public void ReleaseTagDriftIsRejected()
    {
        var descriptor = CreateDescriptor();
        var metadata = CreateMetadata(descriptor) with { Tag = "byedpi-v0.17.4" };

        Assert.Throws<UpdateSecurityException>(() =>
            ByeDpiAcquisitionTrustPolicy.CreatePlan(descriptor, metadata));
    }

    [Fact]
    public void ExtraReleaseAssetIsRejected()
    {
        var descriptor = CreateDescriptor();
        var metadata = CreateMetadata(descriptor);
        var assets = metadata.Assets.ToList();
        assets.Add(new ByeDpiAcquisitionAssetMetadata(
            2000,
            "unexpected.txt",
            1,
            new string('d', 64),
            AssetApiUri(2000)));

        Assert.Throws<UpdateSecurityException>(() =>
            ByeDpiAcquisitionTrustPolicy.CreatePlan(descriptor, metadata with { Assets = assets }));
    }

    [Fact]
    public void DuplicateAssetIdIsRejected()
    {
        var descriptor = CreateDescriptor();
        var metadata = CreateMetadata(descriptor);
        var assets = metadata.Assets.ToArray();
        assets[1] = assets[1] with
        {
            AssetId = assets[0].AssetId,
            AssetApiUri = AssetApiUri(assets[0].AssetId)
        };

        Assert.Throws<UpdateSecurityException>(() =>
            ByeDpiAcquisitionTrustPolicy.CreatePlan(descriptor, metadata with { Assets = assets }));
    }

    [Fact]
    public void AssetDigestDriftIsRejected()
    {
        var descriptor = CreateDescriptor();
        var metadata = CreateMetadata(descriptor);
        var assets = metadata.Assets.ToArray();
        assets[0] = assets[0] with { Sha256Hex = new string('e', 64) };

        Assert.Throws<UpdateSecurityException>(() =>
            ByeDpiAcquisitionTrustPolicy.CreatePlan(descriptor, metadata with { Assets = assets }));
    }

    [Fact]
    public void AssetSizeDriftIsRejected()
    {
        var descriptor = CreateDescriptor();
        var metadata = CreateMetadata(descriptor);
        var assets = metadata.Assets.ToArray();
        assets[1] = assets[1] with { SizeBytes = assets[1].SizeBytes + 1 };

        Assert.Throws<UpdateSecurityException>(() =>
            ByeDpiAcquisitionTrustPolicy.CreatePlan(descriptor, metadata with { Assets = assets }));
    }

    [Fact]
    public void AssetApiUriDriftIsRejected()
    {
        var descriptor = CreateDescriptor();
        var metadata = CreateMetadata(descriptor);
        var assets = metadata.Assets.ToArray();
        assets[2] = assets[2] with
        {
            AssetApiUri = new Uri("https://api.github.com/repos/attacker/repo/releases/assets/1003")
        };

        Assert.Throws<UpdateSecurityException>(() =>
            ByeDpiAcquisitionTrustPolicy.CreatePlan(descriptor, metadata with { Assets = assets }));
    }

    [Fact]
    public void DowngradeBelowInstalledVersionIsRejected()
    {
        var descriptor = CreateDescriptor();
        var metadata = CreateMetadata(descriptor);

        var exception = Assert.Throws<UpdateSecurityException>(() =>
            ByeDpiAcquisitionTrustPolicy.CreatePlan(descriptor, metadata, "0.17.4"));

        Assert.Equal("ByeDpiAcquisitionDowngradeBlocked", exception.Code);
    }

    [Fact]
    public void EqualInstalledVersionIsAllowedForFutureRepairAcquisition()
    {
        var descriptor = CreateDescriptor();
        var metadata = CreateMetadata(descriptor);

        var plan = ByeDpiAcquisitionTrustPolicy.CreatePlan(descriptor, metadata, "0.17.3");

        Assert.Equal(3, plan.Assets.Count);
    }

    [Fact]
    public void NonCanonicalInstalledVersionIsRejected()
    {
        var descriptor = CreateDescriptor();
        var metadata = CreateMetadata(descriptor);

        Assert.Throws<UpdateSecurityException>(() =>
            ByeDpiAcquisitionTrustPolicy.CreatePlan(descriptor, metadata, "0.017.3"));
    }

    [Fact]
    public void TrustedSingleGithubAssetRedirectIsAccepted()
    {
        ByeDpiAcquisitionTrustPolicy.ValidateReleaseAssetRedirect(
            new Uri("https://release-assets.githubusercontent.com/github-production-release-asset/123/file.zip?sp=r&sig=test"),
            1);
    }

    [Fact]
    public void UntrustedRedirectOriginIsRejected()
    {
        Assert.Throws<UpdateSecurityException>(() =>
            ByeDpiAcquisitionTrustPolicy.ValidateReleaseAssetRedirect(
                new Uri("https://example.com/file.zip"),
                1));
    }

    [Fact]
    public void AdditionalRedirectHopIsRejected()
    {
        Assert.Throws<UpdateSecurityException>(() =>
            ByeDpiAcquisitionTrustPolicy.ValidateReleaseAssetRedirect(
                new Uri("https://release-assets.githubusercontent.com/github-production-release-asset/123/file.zip"),
                2));
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
                12345,
                new string('a', 64)),
            new ByeDpiImmutableReleaseAsset(
                "NetPilot-ByeDPI-0.17.3-x64.manifest.json",
                2345,
                new string('b', 64)),
            new ByeDpiImmutableReleaseAsset(
                "NetPilot-ByeDPI-0.17.3-x64.provenance.json",
                3456,
                new string('c', 64)));

    private static ByeDpiAcquisitionReleaseMetadata CreateMetadata(
        ByeDpiImmutableReleaseDescriptor descriptor)
    {
        var expected = new[]
        {
            descriptor.Payload,
            descriptor.Manifest,
            descriptor.Provenance
        };

        var assets = expected
            .Select((asset, index) =>
            {
                var assetId = 1001L + index;
                return new ByeDpiAcquisitionAssetMetadata(
                    assetId,
                    asset.Name,
                    asset.SizeBytes,
                    asset.Sha256Hex,
                    AssetApiUri(assetId));
            })
            .ToArray();

        return new ByeDpiAcquisitionReleaseMetadata(
            5001,
            descriptor.ReleaseTag,
            Draft: false,
            Prerelease: false,
            Immutable: true,
            assets);
    }

    private static Uri AssetApiUri(long assetId) =>
        new($"https://api.github.com/repos/DarrenDorr/NetPilot/releases/assets/{assetId}");
}
