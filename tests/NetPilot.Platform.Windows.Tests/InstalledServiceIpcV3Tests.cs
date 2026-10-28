using System.Diagnostics;
using System.Security.Principal;
using NetPilot.Ipc;
using NetPilot.Platform.Windows;

namespace NetPilot.Platform.Windows.Tests;

public sealed class InstalledServiceIpcV3Tests
{
    [Fact]
    [Trait("Category", "InstalledService")]
    public async Task Installed_service_enforces_client_session_policy_and_v3_contract()
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

        using var identity = WindowsIdentity.GetCurrent();
        var interactiveSid = new SecurityIdentifier(WellKnownSidType.InteractiveSid, null).Value;
        var isInteractive = identity.Groups?.Any(group => group.Value == interactiveSid) == true;
        var isServiceAccount = identity.User?.IsWellKnown(WellKnownSidType.LocalSystemSid) == true
            || identity.User?.IsWellKnown(WellKnownSidType.LocalServiceSid) == true
            || identity.User?.IsWellKnown(WellKnownSidType.NetworkServiceSid) == true;
        var sessionId = Process.GetCurrentProcess().SessionId;

        var shouldAuthenticate = sessionId > 0
            && isInteractive
            && !identity.IsAnonymous
            && !identity.IsGuest
            && !isServiceAccount;

        var client = new WindowsPrivilegedServiceClient();

        if (!shouldAuthenticate)
        {
            // GitHub-hosted Windows runner обычно работает из service/session-0 контекста.
            // Такой процесс production-служба обязана отклонить; ослаблять policy ради CI нельзя.
            var exception = await Assert.ThrowsAsync<IpcProtocolException>(
                () => client.PingAsync(TestContext.Current.CancellationToken));

            Assert.Equal("ServiceConnectionClosed", exception.Code);
            return;
        }

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
