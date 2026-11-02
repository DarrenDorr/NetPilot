using NetPilot.Platform.Windows;
using NetPilot.Updater;

namespace NetPilot.Platform.Windows.Tests;

public sealed class WindowsByeDpiPackageProvisioningBoundaryTests
{
    [Fact]
    public void Service_package_surface_accepts_no_user_target_path_url_version_or_archive()
    {
        var methods = typeof(IWindowsByeDpiPackageProvisioner).GetMethods();
        Assert.NotEmpty(methods);

        foreach (var method in methods)
        {
            Assert.DoesNotContain(
                method.GetParameters(),
                parameter => parameter.ParameterType == typeof(string)
                    || parameter.ParameterType == typeof(Uri)
                    || parameter.ParameterType == typeof(Stream)
                    || parameter.ParameterType == typeof(byte[]));
        }
    }

    [Fact]
    public async Task Production_r42_12_provisioner_rejects_package_mutation_fail_closed()
    {
        var exception = await Assert.ThrowsAsync<UpdateSecurityException>(() =>
            NoAdmittedWindowsByeDpiPackageProvisioner.Instance.ApplyReviewedPackageAsync(
                engineLeaseActive: false,
                TestContext.Current.CancellationToken));

        Assert.Equal("ByeDpiPackageProvisioningNotAdmitted", exception.Code);
    }

    [Fact]
    public async Task Production_r42_12_provisioner_rejects_even_when_engine_lease_is_active()
    {
        var exception = await Assert.ThrowsAsync<UpdateSecurityException>(() =>
            NoAdmittedWindowsByeDpiPackageProvisioner.Instance.ApplyReviewedPackageAsync(
                engineLeaseActive: true,
                TestContext.Current.CancellationToken));

        Assert.Equal("ByeDpiPackageProvisioningNotAdmitted", exception.Code);
    }
}
