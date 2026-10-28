using NetPilot.Core.Models;
using NetPilot.Health;

namespace NetPilot.Automation;

public sealed class PublicResourceHealthProbe(
    PublicHttpsHealthChecker checker) : IResourceHealthProbe
{
    public Task<ResourceHealth> CheckAsync(
        ResourceDefinition resource,
        CancellationToken cancellationToken = default) =>
        checker.CheckAsync(resource, cancellationToken);
}
