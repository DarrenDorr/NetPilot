using NetPilot.Core.Models;

namespace NetPilot.Health;

public static class BuiltInResourceCatalog
{
    public static ResourceDefinition Discord { get; } = new(
        "discord",
        "Discord",
        [new Uri("https://discord.com/")],
        []);

    public static ResourceDefinition YouTube { get; } = new(
        "youtube",
        "YouTube",
        [new Uri("https://www.youtube.com/generate_204")],
        []);

    // Review registry is intentionally closed to compiled-in service definitions.
    // A local HTTP/SOCKS5 probe does not imply arbitrary application traffic works.
    public static IReadOnlyList<ReviewedService> Reviewed { get; } =
    [
        new(Discord, ServiceCategory.Communication, ServiceConnectivity.HttpsOverLocalSocks5ProbeOnly),
        new(YouTube, ServiceCategory.Video, ServiceConnectivity.HttpsOverLocalSocks5ProbeOnly)
    ];

    public static IReadOnlyList<ResourceDefinition> All { get; } =
        Reviewed.Select(static entry => entry.Resource).ToArray();

    public static ReviewedService GetReviewed(string resourceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        return Reviewed.SingleOrDefault(item =>
            string.Equals(item.Resource.Id, resourceId, StringComparison.Ordinal))
            ?? throw new ArgumentException("Сервис отсутствует в проверенном каталоге.", nameof(resourceId));
    }
}
