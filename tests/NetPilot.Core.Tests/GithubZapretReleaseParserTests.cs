using System.Text;
using NetPilot.Updater;

namespace NetPilot.Core.Tests;

public sealed class GithubZapretReleaseParserTests
{
    [Fact]
    public void Stable_immutable_official_release_is_accepted()
    {
        var release = GithubZapretReleaseParser.Parse(Encoding.UTF8.GetBytes(ValidMetadata));

        Assert.Equal(418006256, release.ReleaseId);
        Assert.Equal("1.10.4", release.Version);
        Assert.Equal(658286339, release.AssetId);
        Assert.Equal("zapret-discord-youtube-1.10.4.zip", release.Asset.FileName);
        Assert.Equal(1_518_649, release.Asset.SizeBytes);
        Assert.Equal(
            "291af673bd0238506f9548e0e94ca0abf244000f4c9a06960544fb01c5f97b34",
            release.Asset.Sha256Hex);
    }

    [Theory]
    [InlineData("\"draft\":false", "\"draft\":true", "UntrustedReleaseState")]
    [InlineData("\"prerelease\":false", "\"prerelease\":true", "UntrustedReleaseState")]
    [InlineData("\"immutable\":true", "\"immutable\":false", "UntrustedReleaseState")]
    public void Nonstable_release_state_is_rejected(
        string oldValue,
        string newValue,
        string expectedCode)
    {
        var json = ValidMetadata.Replace(oldValue, newValue, StringComparison.Ordinal);

        var exception = Assert.Throws<UpdateSecurityException>(
            () => GithubZapretReleaseParser.Parse(Encoding.UTF8.GetBytes(json)));

        Assert.Equal(expectedCode, exception.Code);
    }

    [Fact]
    public void Asset_api_url_from_another_repository_is_rejected()
    {
        var json = ValidMetadata.Replace(
            "https://api.github.com/repos/Flowseal/zapret-discord-youtube/releases/assets/658286339",
            "https://api.github.com/repos/Other/zapret-discord-youtube/releases/assets/658286339",
            StringComparison.Ordinal);

        var exception = Assert.Throws<UpdateSecurityException>(
            () => GithubZapretReleaseParser.Parse(Encoding.UTF8.GetBytes(json)));

        Assert.Equal("UnexpectedAssetApiUri", exception.Code);
    }

    [Fact]
    public void Missing_digest_is_rejected()
    {
        var json = ValidMetadata.Replace(
            "\"digest\":\"sha256:291af673bd0238506f9548e0e94ca0abf244000f4c9a06960544fb01c5f97b34\",",
            string.Empty,
            StringComparison.Ordinal);

        var exception = Assert.Throws<UpdateSecurityException>(
            () => GithubZapretReleaseParser.Parse(Encoding.UTF8.GetBytes(json)));

        Assert.Equal("InvalidReleaseMetadata", exception.Code);
    }

    [Fact]
    public void Wrong_release_page_is_rejected()
    {
        var json = ValidMetadata.Replace(
            "https://github.com/Flowseal/zapret-discord-youtube/releases/tag/1.10.4",
            "https://github.com/Other/zapret-discord-youtube/releases/tag/1.10.4",
            StringComparison.Ordinal);

        var exception = Assert.Throws<UpdateSecurityException>(
            () => GithubZapretReleaseParser.Parse(Encoding.UTF8.GetBytes(json)));

        Assert.Equal("UnexpectedReleasePage", exception.Code);
    }

    [Fact]
    public void Duplicate_expected_zip_asset_is_rejected()
    {
        var asset = ZipAssetJson;
        var json = ValidMetadata.Replace(
            $"\"assets\":[{asset}]",
            $"\"assets\":[{asset},{asset}]",
            StringComparison.Ordinal);

        var exception = Assert.Throws<UpdateSecurityException>(
            () => GithubZapretReleaseParser.Parse(Encoding.UTF8.GetBytes(json)));

        Assert.Equal("AmbiguousReleaseAsset", exception.Code);
    }

    private const string ZipAssetJson = """
        {"url":"https://api.github.com/repos/Flowseal/zapret-discord-youtube/releases/assets/658286339","id":658286339,"name":"zapret-discord-youtube-1.10.4.zip","content_type":"application/zip","state":"uploaded","size":1518649,"digest":"sha256:291af673bd0238506f9548e0e94ca0abf244000f4c9a06960544fb01c5f97b34","browser_download_url":"https://github.com/Flowseal/zapret-discord-youtube/releases/download/1.10.4/zapret-discord-youtube-1.10.4.zip"}
        """;

    private const string ValidMetadata = """
        {"url":"https://api.github.com/repos/Flowseal/zapret-discord-youtube/releases/418006256","html_url":"https://github.com/Flowseal/zapret-discord-youtube/releases/tag/1.10.4","id":418006256,"tag_name":"1.10.4","draft":false,"immutable":true,"prerelease":false,"assets":[{"url":"https://api.github.com/repos/Flowseal/zapret-discord-youtube/releases/assets/658286339","id":658286339,"name":"zapret-discord-youtube-1.10.4.zip","content_type":"application/zip","state":"uploaded","size":1518649,"digest":"sha256:291af673bd0238506f9548e0e94ca0abf244000f4c9a06960544fb01c5f97b34","browser_download_url":"https://github.com/Flowseal/zapret-discord-youtube/releases/download/1.10.4/zapret-discord-youtube-1.10.4.zip"}]}
        """;
}
