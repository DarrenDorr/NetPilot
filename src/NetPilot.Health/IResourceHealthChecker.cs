using NetPilot.Core.Models;

namespace NetPilot.Health;

public interface IResourceHealthChecker : IDisposable
{
    Task<ResourceHealth> CheckAsync(
        ResourceDefinition resource,
        CancellationToken cancellationToken = default);
}

public sealed class PublicHttpsHealthCheckerAdapter : IResourceHealthChecker
{
    private readonly PublicHttpsHealthChecker _inner;

    public PublicHttpsHealthCheckerAdapter(PublicHttpsHealthChecker inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    public Task<ResourceHealth> CheckAsync(
        ResourceDefinition resource,
        CancellationToken cancellationToken = default) =>
        _inner.CheckAsync(resource, cancellationToken);

    public void Dispose() => _inner.Dispose();
}
