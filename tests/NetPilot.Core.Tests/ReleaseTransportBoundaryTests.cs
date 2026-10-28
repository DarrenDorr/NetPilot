using NetPilot.Updater;

namespace NetPilot.Core.Tests;

public sealed class ReleaseTransportBoundaryTests
{
    [Fact]
    public void Download_transport_has_no_release_discovery_surface()
    {
        var methods = typeof(IZapretReleaseTransport).GetMethods();

        var method = Assert.Single(methods);
        Assert.Equal(nameof(IZapretReleaseTransport.DownloadVerifiedAssetAsync), method.Name);
        Assert.DoesNotContain(
            methods,
            static candidate =>
                candidate.Name.Contains("Latest", StringComparison.OrdinalIgnoreCase)
                || candidate.Name.Contains("Discover", StringComparison.OrdinalIgnoreCase)
                || candidate.Name.Contains("Metadata", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Admission_wrapper_constructor_accepts_only_narrow_transport_and_catalog()
    {
        var constructor = Assert.Single(typeof(AdmittedZapretReleaseSource).GetConstructors());
        var parameters = constructor.GetParameters();

        Assert.Equal(2, parameters.Length);
        Assert.Equal(typeof(IZapretReleaseTransport), parameters[0].ParameterType);
        Assert.Equal(typeof(IZapretReleaseAdmissionCatalog), parameters[1].ParameterType);
        Assert.DoesNotContain(
            parameters,
            static parameter => parameter.ParameterType == typeof(GithubZapretReleaseClient));
    }
}
