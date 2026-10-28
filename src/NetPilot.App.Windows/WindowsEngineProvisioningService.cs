using NetPilot.App.Services;
using NetPilot.Ipc;

namespace NetPilot.App.Windows;

internal sealed class WindowsEngineProvisioningService(
    IPrivilegedServiceClient serviceClient) : IEngineProvisioningService
{
    public async Task<EngineInstallationState> GetInstallationStateAsync(
        CancellationToken cancellationToken = default)
    {
        var response = await serviceClient.GetZapretInstallationStatusAsync(cancellationToken);
        EnsureSuccess(response);

        var status = response.ZapretInstallation
            ?? throw new IpcProtocolException(
                "MissingInstallationStatus",
                "Служба NetPilot не вернула состояние установки Zapret.");

        return new EngineInstallationState(status.IsInstalled, status.Version);
    }

    public async Task<EngineInstallResult> InstallLatestAsync(
        CancellationToken cancellationToken = default)
    {
        var response = await serviceClient.InstallLatestZapretAsync(cancellationToken);
        EnsureSuccess(response);

        var result = response.ZapretInstall;
        if (result is null || !result.Installed || string.IsNullOrWhiteSpace(result.Version))
        {
            throw new IpcProtocolException(
                "MissingInstallResult",
                "Служба NetPilot не подтвердила установку Zapret.");
        }

        return new EngineInstallResult(
            result.Version,
            result.TemporaryCleanupCompleted);
    }

    private static void EnsureSuccess(IpcResponse response)
    {
        if (!response.Success)
        {
            throw new IpcProtocolException(
                response.Code,
                response.Message ?? "Служба NetPilot отклонила операцию установки движка.");
        }
    }
}
