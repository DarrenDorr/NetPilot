using NetPilot.Automation;
using NetPilot.Engine.Abstractions;

namespace NetPilot.Core.Tests;

public sealed class EngineProfileStrategyRuntimeAdapterTests
{
    [Fact]
    public async Task ListStrategiesAsync_MapsOnlyCanonicalProfileIds()
    {
        var runtime = new FakeProfileRuntime
        {
            Profiles =
            [
                new EngineProfileDescriptor(EngineProfileId.Parse("discord"), "Discord"),
                new EngineProfileDescriptor(EngineProfileId.Parse("youtube"), "YouTube")
            ]
        };
        var adapter = new EngineProfileStrategyRuntimeAdapter(runtime);

        var strategies = await adapter.ListStrategiesAsync();

        Assert.Collection(
            strategies,
            first =>
            {
                Assert.Equal("discord", first.Id);
                Assert.Equal("Discord", first.DisplayName);
            },
            second =>
            {
                Assert.Equal("youtube", second.Id);
                Assert.Equal("YouTube", second.DisplayName);
            });
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("../profile")]
    [InlineData("profile.lua")]
    [InlineData("luaexec:code")]
    [InlineData("x=1")]
    [InlineData("a;b")]
    public async Task ActivateAsync_RejectsCommandLikeInputBeforeRuntime(string value)
    {
        var runtime = new FakeProfileRuntime();
        var adapter = new EngineProfileStrategyRuntimeAdapter(runtime);

        var exception = await Assert.ThrowsAsync<StrategyRuntimeException>(
            () => adapter.ActivateAsync(value));

        Assert.Equal("InvalidProfileId", exception.Code);
        Assert.Null(runtime.LastActivatedProfileId);
    }

    [Fact]
    public async Task ActivateAsync_PassesTypedProfileIdToRuntime()
    {
        var runtime = new FakeProfileRuntime();
        var adapter = new EngineProfileStrategyRuntimeAdapter(runtime);

        await adapter.ActivateAsync("discord-youtube");

        Assert.Equal(
            EngineProfileId.Parse("discord-youtube"),
            runtime.LastActivatedProfileId);
    }

    [Fact]
    public async Task ActivateAsync_TranslatesEngineRuntimeFailure()
    {
        var runtime = new FakeProfileRuntime
        {
            ActivationException = new EngineProfileRuntimeException(
                "UnknownProfile",
                "Профиль отсутствует в локальном allowlist.")
        };
        var adapter = new EngineProfileStrategyRuntimeAdapter(runtime);

        var exception = await Assert.ThrowsAsync<StrategyRuntimeException>(
            () => adapter.ActivateAsync("discord"));

        Assert.Equal("UnknownProfile", exception.Code);
        Assert.IsType<EngineProfileRuntimeException>(exception.InnerException);
    }

    [Fact]
    public async Task GetStateAsync_MapsActiveTypedProfile()
    {
        var runtime = new FakeProfileRuntime
        {
            State = EngineProfileRuntimeState.Active(
                EngineProfileId.Parse("discord"))
        };
        var adapter = new EngineProfileStrategyRuntimeAdapter(runtime);

        var state = await adapter.GetStateAsync();

        Assert.True(state.IsRunning);
        Assert.Equal("discord", state.StrategyId);
    }

    private sealed class FakeProfileRuntime : IEngineProfileRuntime
    {
        public string EngineId => "fake";

        public IReadOnlyList<EngineProfileDescriptor> Profiles { get; init; } = [];

        public EngineProfileRuntimeState State { get; init; } =
            EngineProfileRuntimeState.Inactive;

        public EngineProfileRuntimeException? ActivationException { get; init; }

        public EngineProfileId? LastActivatedProfileId { get; private set; }

        public ValueTask<IReadOnlyList<EngineProfileDescriptor>> ListProfilesAsync(
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(Profiles);

        public ValueTask<EngineProfileRuntimeState> GetProfileStateAsync(
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(State);

        public ValueTask ActivateProfileAsync(
            EngineProfileId profileId,
            CancellationToken cancellationToken)
        {
            if (ActivationException is not null)
            {
                throw ActivationException;
            }

            LastActivatedProfileId = profileId;
            return ValueTask.CompletedTask;
        }
    }
}
