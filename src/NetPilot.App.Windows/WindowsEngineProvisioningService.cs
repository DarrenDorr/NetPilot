using NetPilot.App.Services;
using NetPilot.Ipc;
using NetPilot.Security;

namespace NetPilot.App.Windows;

internal sealed class WindowsEngineProvisioningService(
    IPrivilegedServiceClient serviceClient) : IEngineProvisioningService
{
    public async Task<EngineInstallationState> GetInstallationStateAsync(
        CancellationToken cancellationToken = default)
    {
        var response = await ExecuteIpcAsync(
            () => serviceClient.GetZapretInstallationStatusAsync(cancellationToken));
        EnsureSuccess(response);

        var status = response.ZapretInstallation
            ?? throw new EngineProvisioningException(
                "MissingInstallationStatus",
                "Служба NetPilot не вернула состояние установки сетевого движка.");

        // Клиент и служба собираются из одного admission-профиля. При отсутствии
        // ADMIT безопасные значения — запрет установки и запуска. Сама служба
        // остаётся окончательной границей и повторно проверяет OperationPolicy.
        var runtimePolicy = OperationPolicy.CreateNoAdmittedEnginePolicy();

        return new EngineInstallationState(
            status.IsInstalled,
            status.Version,
            CanInstall: runtimePolicy.IsAllowed(PrivilegedOperation.EngineInstall),
            CanStart: runtimePolicy.IsAllowed(PrivilegedOperation.EngineStart));
    }

    public async Task<EngineInstallResult> InstallLatestAsync(
        CancellationToken cancellationToken = default)
    {
        var response = await ExecuteIpcAsync(
            () => serviceClient.InstallLatestZapretAsync(cancellationToken));
        EnsureSuccess(response);

        var result = response.ZapretInstall;
        if (result is null || !result.Installed || string.IsNullOrWhiteSpace(result.Version))
        {
            throw new EngineProvisioningException(
                "MissingInstallResult",
                "Служба NetPilot не подтвердила установку сетевого движка.");
        }

        return new EngineInstallResult(
            result.Version,
            result.TemporaryCleanupCompleted);
    }

    private static async Task<IpcResponse> ExecuteIpcAsync(Func<Task<IpcResponse>> operation)
    {
        try
        {
            return await operation();
        }
        catch (IpcProtocolException exception)
        {
            throw new EngineProvisioningException(
                exception.Code,
                exception.Message,
                exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new EngineProvisioningException(
                "ServiceAccessDenied",
                "Windows не разрешила доступ к локальной службе NetPilot.",
                exception);
        }
        catch (IOException exception)
        {
            throw new EngineProvisioningException(
                "ServiceTransportError",
                "Не удалось безопасно обменяться данными с локальной службой NetPilot.",
                exception);
        }
    }

    private static void EnsureSuccess(IpcResponse response)
    {
        if (!response.Success)
        {
            throw new EngineProvisioningException(
                response.Code,
                response.Message ?? "Служба NetPilot отклонила операцию установки движка.");
        }
    }
}
