using NetPilot.Automation;
using NetPilot.Ipc;
using NetPilot.Platform.Windows;

namespace NetPilot.Platform.Windows.Tests;

public sealed class WindowsZapretStrategyRuntimeTests
{
    [Fact]
    public async Task Activate_same_running_strategy_is_noop()
    {
        var client = new FakeClient
        {
            StatusResponse = SuccessStatus(IpcZapretProcessState.Running, "ALT11")
        };
        var runtime = new WindowsZapretStrategyRuntime(client);

        await runtime.ActivateAsync("ALT11", TestContext.Current.CancellationToken);

        Assert.Equal(1, client.StatusCalls);
        Assert.Equal(0, client.StopCalls);
        Assert.Equal(0, client.StartCalls);
    }

    [Fact]
    public async Task Activate_different_strategy_stops_then_starts_exact_id()
    {
        var client = new FakeClient
        {
            StatusResponse = SuccessStatus(IpcZapretProcessState.Running, "ALT11"),
            StopResponse = SuccessStatus(IpcZapretProcessState.Stopped),
            StartResponse = SuccessStatus(IpcZapretProcessState.Running, "ALT12")
        };
        var runtime = new WindowsZapretStrategyRuntime(client);

        await runtime.ActivateAsync("ALT12", TestContext.Current.CancellationToken);

        Assert.Equal(1, client.StopCalls);
        Assert.Equal(1, client.StartCalls);
        Assert.Equal("ALT12", client.LastStartedStrategyId);
    }

    [Fact]
    public async Task Service_rejection_is_mapped_to_strategy_runtime_failure()
    {
        var client = new FakeClient
        {
            StatusResponse = SuccessStatus(IpcZapretProcessState.Stopped),
            StartResponse = Failure("StrategyNotFound")
        };
        var runtime = new WindowsZapretStrategyRuntime(client);

        var exception = await Assert.ThrowsAsync<StrategyRuntimeException>(
            () => runtime.ActivateAsync("ALT12", TestContext.Current.CancellationToken));

        Assert.Equal("StrategyNotFound", exception.Code);
    }

    [Fact]
    public async Task Running_status_without_strategy_id_is_neutral_runtime_error()
    {
        var client = new FakeClient
        {
            StatusResponse = SuccessStatus(IpcZapretProcessState.Running, null)
        };
        var runtime = new WindowsZapretStrategyRuntime(client);

        var exception = await Assert.ThrowsAsync<StrategyRuntimeException>(
            () => runtime.GetStateAsync(TestContext.Current.CancellationToken));

        Assert.Equal("MissingRunningStrategyId", exception.Code);
    }

    [Fact]
    public async Task Ipc_protocol_error_does_not_escape_platform_adapter()
    {
        var client = new FakeClient
        {
            StatusException = new IpcProtocolException(
                "ServerAuthenticationFailed",
                "Проверка службы не пройдена.")
        };
        var runtime = new WindowsZapretStrategyRuntime(client);

        var exception = await Assert.ThrowsAsync<StrategyRuntimeException>(
            () => runtime.GetStateAsync(TestContext.Current.CancellationToken));

        Assert.Equal("ServerAuthenticationFailed", exception.Code);
        Assert.IsType<IpcProtocolException>(exception.InnerException);
    }

    [Fact]
    public async Task Strategy_catalog_is_mapped_without_extra_commands()
    {
        var client = new FakeClient
        {
            StrategiesResponse = new IpcResponse(
                IpcProtocol.Version,
                Guid.NewGuid(),
                true,
                "Ok",
                Strategies:
                [
                    new IpcZapretStrategy("ALT11", "ALT11"),
                    new IpcZapretStrategy("ALT12", "ALT12")
                ])
        };
        var runtime = new WindowsZapretStrategyRuntime(client);

        var strategies = await runtime.ListStrategiesAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, strategies.Count);
        Assert.Equal("ALT11", strategies[0].Id);
        Assert.Equal("ALT12", strategies[1].Id);
        Assert.Equal(1, client.ListCalls);
    }

    private static IpcResponse SuccessStatus(
        IpcZapretProcessState state,
        string? strategyId = null) =>
        new(
            IpcProtocol.Version,
            Guid.NewGuid(),
            true,
            "Ok",
            ZapretStatus: new IpcZapretStatus(state, strategyId));

    private static IpcResponse Failure(string code) =>
        new(
            IpcProtocol.Version,
            Guid.NewGuid(),
            false,
            code,
            "Операция отклонена.");

    private sealed class FakeClient : IPrivilegedServiceClient
    {
        public IpcResponse StrategiesResponse { get; set; } = new(
            IpcProtocol.Version,
            Guid.NewGuid(),
            true,
            "Ok",
            Strategies: []);

        public IpcResponse StatusResponse { get; set; } = SuccessStatus(IpcZapretProcessState.Stopped);
        public IpcResponse StopResponse { get; set; } = SuccessStatus(IpcZapretProcessState.Stopped);
        public IpcResponse StartResponse { get; set; } = SuccessStatus(IpcZapretProcessState.Running, "ALT11");
        public Exception? StatusException { get; set; }

        public int ListCalls { get; private set; }
        public int StatusCalls { get; private set; }
        public int StartCalls { get; private set; }
        public int StopCalls { get; private set; }
        public string? LastStartedStrategyId { get; private set; }

        public Task<IpcResponse> PingAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new IpcResponse(IpcProtocol.Version, Guid.NewGuid(), true, "Ok"));

        public Task<IpcResponse> EnrollAuthorizedUserAsync(
            string enrollmentNonce,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new IpcResponse(IpcProtocol.Version, Guid.NewGuid(), true, "Ok"));

        public Task<IpcResponse> GetZapretInstallationStatusAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new IpcResponse(
                IpcProtocol.Version,
                Guid.NewGuid(),
                true,
                "Ok",
                ZapretInstallation: new IpcZapretInstallationStatus(true, "1.0.0")));

        public Task<IpcResponse> InstallLatestZapretAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new IpcResponse(
                IpcProtocol.Version,
                Guid.NewGuid(),
                true,
                "Ok",
                ZapretInstall: new IpcZapretInstallResult("1.0.0", true, true)));

        public Task<IpcResponse> ListZapretStrategiesAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ListCalls++;
            return Task.FromResult(StrategiesResponse);
        }

        public Task<IpcResponse> GetZapretStatusAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StatusCalls++;

            if (StatusException is not null)
            {
                return Task.FromException<IpcResponse>(StatusException);
            }

            return Task.FromResult(StatusResponse);
        }

        public Task<IpcResponse> StartZapretStrategyAsync(
            string strategyId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StartCalls++;
            LastStartedStrategyId = strategyId;
            return Task.FromResult(StartResponse);
        }

        public Task<IpcResponse> StopZapretAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StopCalls++;
            return Task.FromResult(StopResponse);
        }

        // This fake is intentionally scoped to Zapret adapter tests. ByeDPI operations
        // fail closed so extending IPrivilegedServiceClient cannot accidentally make
        // unrelated Zapret tests exercise or simulate a second native engine.
        public Task<IpcResponse> GetByeDpiStatusAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Failure("NotSupportedByZapretTestFake"));

        public Task<IpcResponse> StartByeDpiAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Failure("NotSupportedByZapretTestFake"));

        public Task<IpcResponse> StopByeDpiAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Failure("NotSupportedByZapretTestFake"));
    }
}
