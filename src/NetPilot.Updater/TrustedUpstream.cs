namespace NetPilot.Updater;

public sealed record TrustedUpstream(string Id, Uri RepositoryUri);

public static class TrustedUpstreams
{
    public static readonly TrustedUpstream FlowsealZapret = new(
        "flowseal-zapret",
        new Uri("https://github.com/Flowseal/zapret-discord-youtube"));

    public static readonly TrustedUpstream FlowsealTelegram = new(
        "flowseal-tg-ws-proxy",
        new Uri("https://github.com/Flowseal/tg-ws-proxy"));
}
