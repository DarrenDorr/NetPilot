using NetPilot.Core.Models;
using NetPilot.Health;

namespace NetPilot.Automation;

public sealed class PublicResourceHealthProbe : IResourceHealthProbe
{
    private readonly IResourceHealthChecker _checker;

    public PublicResourceHealthProbe(PublicHttpsHealthChecker checker)
        : this(new PublicHttpsHealthCheckerAdapter(checker))
    {
    }

    public PublicResourceHealthProbe(IResourceHealthChecker checker)
    {
        _checker = checker ?? throw new ArgumentNullException(nameof(checker));
    }

    public Task<ResourceHealth> CheckAsync(
        ResourceDefinition resource,
        CancellationToken cancellationToken = default) =>
        _checker.CheckAsync(resource, cancellationToken);
}
