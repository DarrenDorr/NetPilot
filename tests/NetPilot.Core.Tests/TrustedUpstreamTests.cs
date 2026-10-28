using NetPilot.Updater;

namespace NetPilot.Core.Tests;

public sealed class TrustedUpstreamTests
{
    public static TheoryData<TrustedUpstream, string> TrustedSources => new()
    {
        { TrustedUpstreams.FlowsealZapret, "/Flowseal/zapret-discord-youtube" },
        { TrustedUpstreams.FlowsealTelegram, "/Flowseal/tg-ws-proxy" }
    };

    [Theory]
    [MemberData(nameof(TrustedSources))]
    public void Trusted_upstream_is_exact_https_github_repository(
        TrustedUpstream upstream,
        string expectedPath)
    {
        Assert.Equal(Uri.UriSchemeHttps, upstream.RepositoryUri.Scheme);
        Assert.Equal("github.com", upstream.RepositoryUri.Host);
        Assert.True(upstream.RepositoryUri.IsDefaultPort);
        Assert.Equal(expectedPath, upstream.RepositoryUri.AbsolutePath);
        Assert.Equal(string.Empty, upstream.RepositoryUri.UserInfo);
        Assert.Equal(string.Empty, upstream.RepositoryUri.Query);
        Assert.Equal(string.Empty, upstream.RepositoryUri.Fragment);
    }
}
