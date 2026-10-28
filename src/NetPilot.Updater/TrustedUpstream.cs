namespace NetPilot.Updater;

public sealed record TrustedUpstream
{
    internal TrustedUpstream(string id, Uri repositoryUri)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(repositoryUri);

        if (!repositoryUri.IsAbsoluteUri
            || !string.Equals(repositoryUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(repositoryUri.Host, "github.com", StringComparison.OrdinalIgnoreCase)
            || !repositoryUri.IsDefaultPort
            || !string.IsNullOrEmpty(repositoryUri.UserInfo)
            || !string.IsNullOrEmpty(repositoryUri.Query)
            || !string.IsNullOrEmpty(repositoryUri.Fragment))
        {
            throw new ArgumentException(
                "Доверенный upstream должен быть чистым HTTPS-адресом репозитория github.com.",
                nameof(repositoryUri));
        }

        Id = id;
        RepositoryUri = repositoryUri;
    }

    public string Id { get; }
    public Uri RepositoryUri { get; }
}

public static class TrustedUpstreams
{
    public static readonly TrustedUpstream FlowsealZapret = new(
        "flowseal-zapret",
        new Uri("https://github.com/Flowseal/zapret-discord-youtube"));

    public static readonly TrustedUpstream FlowsealTelegram = new(
        "flowseal-tg-ws-proxy",
        new Uri("https://github.com/Flowseal/tg-ws-proxy"));
}
