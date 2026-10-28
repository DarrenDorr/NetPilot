using NetPilot.Platform.Windows;

namespace NetPilot.Platform.Windows.Tests;

public sealed class InstalledServiceIpcV3Tests
{
    [Fact]
    [Trait("Category", "InstalledService")]
    public async Task Installed_service_accepts_authenticated_v3_client_and_reports_no_zapret()
    {
        // Обычный Windows test job запускает тесты до установки Setup. Повторный
        // отфильтрованный запуск после installer smoke-test включает этот сценарий явно.
        if (!string.Equals(
                Environment.GetEnvironmentVariable("NETPILOT_INSTALLED_SERVICE_TEST"),
                "1",
                StringComparison.Ordinal))
        {
            return;
        }

        var client = new WindowsPrivilegedServiceClient();

        var ping = await client.PingAsync(TestContext.Current.CancellationToken);
        Assert.True(ping.Success);
        Assert.Equal("Ok", ping.Code);

        var installation = await client.GetZapretInstallationStatusAsync(
            TestContext.Current.CancellationToken);

        Assert.True(installation.Success);
        Assert.NotNull(installation.ZapretInstallation);
        Assert.False(installation.ZapretInstallation.IsInstalled);
        Assert.Null(installation.ZapretInstallation.Version);
    }
}
