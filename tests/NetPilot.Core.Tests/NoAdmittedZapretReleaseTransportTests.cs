using System.Net.Http;
using NetPilot.Updater;

namespace NetPilot.Core.Tests;

public sealed class NoAdmittedZapretReleaseTransportTests
{
    [Fact]
    public async Task Download_is_rejected_without_release_or_path_processing()
    {
        var exception = await Assert.ThrowsAsync<UpdateSecurityException>(
            () => NoAdmittedZapretReleaseTransport.Instance.DownloadVerifiedAssetAsync(
                null!,
                "ignored",
                TestContext.Current.CancellationToken));

        Assert.Equal("NoAdmittedEngineRelease", exception.Code);
    }

    [Fact]
    public void Transport_has_no_network_or_mutable_instance_state()
    {
        var type = typeof(NoAdmittedZapretReleaseTransport);
        var instanceFields = type
            .GetFields(
                System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.NonPublic);

        Assert.Empty(instanceFields);
        Assert.DoesNotContain(
            type.GetConstructors(),
            static constructor => constructor.IsPublic);
        Assert.DoesNotContain(
            instanceFields,
            static field => typeof(HttpClient).IsAssignableFrom(field.FieldType));
    }
}
