using System.Text;
using NetPilot.Updater;

namespace NetPilot.Core.Tests;

public sealed class GithubByeDpiReleaseParserTests
{
    [Fact]
    public void ExactFixtureParsesAndCreatesTrustedPlan()
    {
        var descriptor = CreateDescriptor();
        var metadata = GithubByeDpiReleaseParser.Parse(Encoding.UTF8.GetBytes(CreateValidJson()));

        var plan = ByeDpiAcquisitionTrustPolicy.CreatePlan(descriptor, metadata, "0.17.3");

        Assert.Equal(5001, metadata.ReleaseId);
        Assert.Equal("byedpi-v0.17.3", metadata.Tag);
        Assert.True(metadata.Immutable);
        Assert.Equal(3, metadata.Assets.Count);
        Assert.Equal(3, plan.Assets.Count);
    }

    [Fact]
    public void UnknownGithubFieldsAreToleratedWithoutChangingRequiredMeaning()
    {
        var json = CreateValidJson().Replace(
            "\"tag_name\":\"byedpi-v0.17.3\"",
            "\"tag_name\":\"byedpi-v0.17.3\",\"future_field\":{\"nested\":true}",
            StringComparison.Ordinal);
        json = json.Replace(
            "\"state\":\"uploaded\"",
            "\"state\":\"uploaded\",\"future_asset_field\":42",
            StringComparison.Ordinal);

        var metadata = GithubByeDpiReleaseParser.Parse(Encoding.UTF8.GetBytes(json));
        var plan = ByeDpiAcquisitionTrustPolicy.CreatePlan(CreateDescriptor(), metadata);

        Assert.Equal(3, plan.Assets.Count);
    }

    [Fact]
    public void DuplicateRootPropertyIsRejected()
    {
        var json = CreateValidJson().Replace(
            "\"id\":5001",
            "\"id\":5001,\"id\":5001",
            StringComparison.Ordinal);

        Assert.Throws<UpdateSecurityException>(() =>
            GithubByeDpiReleaseParser.Parse(Encoding.UTF8.GetBytes(json)));
    }

    [Fact]
    public void DuplicateAssetPropertyIsRejected()
    {
        var json = CreateValidJson().Replace(
            "\"state\":\"uploaded\"",
            "\"state\":\"uploaded\",\"state\":\"uploaded\"",
            StringComparison.Ordinal);

        Assert.Throws<UpdateSecurityException>(() =>
            GithubByeDpiReleaseParser.Parse(Encoding.UTF8.GetBytes(json)));
    }

    [Fact]
    public void MissingRequiredRootPropertyIsRejected()
    {
        var json = CreateValidJson().Replace("\"immutable\":true,", string.Empty, StringComparison.Ordinal);

        Assert.Throws<UpdateSecurityException>(() =>
            GithubByeDpiReleaseParser.Parse(Encoding.UTF8.GetBytes(json)));
    }

    [Fact]
    public void UnsupportedDigestAlgorithmIsRejected()
    {
        var json = CreateValidJson().Replace("sha256:", "sha512:", StringComparison.Ordinal);

        Assert.Throws<UpdateSecurityException>(() =>
            GithubByeDpiReleaseParser.Parse(Encoding.UTF8.GetBytes(json)));
    }

    [Fact]
    public void UppercaseDigestIsRejected()
    {
        var lower = new string('a', 64);
        var json = CreateValidJson().Replace(lower, lower.ToUpperInvariant(), StringComparison.Ordinal);

        Assert.Throws<UpdateSecurityException>(() =>
            GithubByeDpiReleaseParser.Parse(Encoding.UTF8.GetBytes(json)));
    }

    [Fact]
    public void NonHttpsAssetApiUriIsRejected()
    {
        var json = CreateValidJson().Replace(
            "https://api.github.com/repos/DarrenDorr/NetPilot/releases/assets/1001",
            "http://api.github.com/repos/DarrenDorr/NetPilot/releases/assets/1001",
            StringComparison.Ordinal);

        Assert.Throws<UpdateSecurityException>(() =>
            GithubByeDpiReleaseParser.Parse(Encoding.UTF8.GetBytes(json)));
    }

    [Fact]
    public void NonPositiveAssetSizeIsRejected()
    {
        var json = CreateValidJson().Replace("\"size\":100", "\"size\":0", StringComparison.Ordinal);

        Assert.Throws<UpdateSecurityException>(() =>
            GithubByeDpiReleaseParser.Parse(Encoding.UTF8.GetBytes(json)));
    }

    [Fact]
    public void TooManyAssetsAreRejectedBeforePolicyEvaluation()
    {
        var asset = AssetJson(1001, "extra.bin", 1, new string('d', 64));
        var assets = string.Join(',', Enumerable.Repeat(asset, GithubByeDpiReleaseParser.MaxAssetsInMetadata + 1));
        var json = "{" +
            "\"id\":5001," +
            "\"tag_name\":\"byedpi-v0.17.3\"," +
            "\"draft\":false," +
            "\"prerelease\":false," +
            "\"immutable\":true," +
            $"\"assets\":[{assets}]" +
            "}";

        Assert.Throws<UpdateSecurityException>(() =>
            GithubByeDpiReleaseParser.Parse(Encoding.UTF8.GetBytes(json)));
    }

    [Fact]
    public void OversizedMetadataIsRejected()
    {
        var bytes = new byte[ByeDpiAcquisitionTrustPolicy.MaxReleaseMetadataBytes + 1];
        Array.Fill(bytes, (byte)' ');

        Assert.Throws<UpdateSecurityException>(() => GithubByeDpiReleaseParser.Parse(bytes));
    }

    [Fact]
    public void MalformedJsonIsRejectedWithTypedCode()
    {
        var exception = Assert.Throws<UpdateSecurityException>(() =>
            GithubByeDpiReleaseParser.Parse(Encoding.UTF8.GetBytes("{not-json")));

        Assert.Equal("ByeDpiGithubMetadataJsonInvalid", exception.Code);
    }

    private static string CreateValidJson() =>
        "{" +
        "\"url\":\"https://api.github.com/repos/DarrenDorr/NetPilot/releases/5001\"," +
        "\"id\":5001," +
        "\"tag_name\":\"byedpi-v0.17.3\"," +
        "\"draft\":false," +
        "\"prerelease\":false," +
        "\"immutable\":true," +
        "\"assets\":[" +
        AssetJson(1001, "NetPilot-ByeDPI-0.17.3-x64.zip", 100, new string('a', 64)) + "," +
        AssetJson(1002, "NetPilot-ByeDPI-0.17.3-x64.manifest.json", 200, new string('b', 64)) + "," +
        AssetJson(1003, "NetPilot-ByeDPI-0.17.3-x64.provenance.json", 300, new string('c', 64)) +
        "]}";

    private static string AssetJson(long id, string name, long size, string sha256) =>
        "{" +
        $"\"url\":\"https://api.github.com/repos/DarrenDorr/NetPilot/releases/assets/{id}\"," +
        $"\"id\":{id}," +
        $"\"name\":\"{name}\"," +
        "\"state\":\"uploaded\"," +
        "\"content_type\":\"application/octet-stream\"," +
        $"\"size\":{size}," +
        $"\"digest\":\"sha256:{sha256}\"" +
        "}";

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
                new string('a', 64)),
            new ByeDpiImmutableReleaseAsset(
                "NetPilot-ByeDPI-0.17.3-x64.manifest.json",
                200,
                new string('b', 64)),
            new ByeDpiImmutableReleaseAsset(
                "NetPilot-ByeDPI-0.17.3-x64.provenance.json",
                300,
                new string('c', 64)));
}
