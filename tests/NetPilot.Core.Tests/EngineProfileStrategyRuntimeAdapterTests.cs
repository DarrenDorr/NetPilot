using NetPilot.Automation;
using NetPilot.Engine.Abstractions;

namespace NetPilot.Core.Tests;

public sealed class EngineProfileStrategyRuntimeAdapterTests
{
    [Fact]
    public async Task ListStrategiesAsync_MapsOnlyCanonicalProfileIds_WhenRuntimeIsAdmitted()
    {
        var runtime = new FakeProfileRuntime
        {
            Profiles =
            [
                new EngineProfileDescriptor(EngineProfileId.Parse("discord"), "Discord"),
                new EngineProfileDescriptor(EngineProfileId.Parse("youtube"), "YouTube")
            ]
        };
        var admission = new FixedAdmissionPolicy(true);
        var adapter = new EngineProfileStrategyRuntimeAdapter(runtime, admission);

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
        Assert.Equal(1, admission.CallCount);
        Assert.Same(runtime, admission.LastRuntime);
        Assert.Same(runtime.CapabilityDeclaration, admission.LastDeclaration);
    }

    [Fact]
    public async Task ListStrategiesAsync_TrustedCapabilitiesDoNotImplyAdmission()
    {
        var runtime = new FakeProfileRuntime
        {
            Profiles =
            [
                new EngineProfileDescriptor(EngineProfileId.Parse("youtube"), "YouTube")
            ]
        };
        var adapter = new EngineProfileStrategyRuntimeAdapter(
            runtime,
            DenyAllEngineRuntimeAdmissionPolicy.Instance);

        var exception = await Assert.ThrowsAsync<StrategyRuntimeException>(
            () => adapter.ListStrategiesAsync());

        Assert.Equal("EngineRuntimeNotAdmitted", exception.Code);
        Assert.Null(runtime.LastActivatedProfileId);
    }

    [Fact]
    public async Task ListStrategiesAsync_FiltersProxyOnlyBackendFromFullProfiles_WhenAdmittedForTest()
    {
        var runtime = new FakeProfileRuntime
        {
            CapabilityDeclaration = BuiltInEngineBackendCapabilityCatalog.Get(
                BuiltInEngineBackendKind.ByeDpi,
                EngineHostPlatform.Windows),
            Profiles =
            [
                new EngineProfileDescriptor(EngineProfileId.Parse("discord"), "Discord"),
                new EngineProfileDescriptor(EngineProfileId.Parse("youtube"), "YouTube")
            ]
        };
        var adapter = new EngineProfileStrategyRuntimeAdapter(
            runtime,
            new FixedAdmissionPolicy(true));

        var strategies = await adapter.ListStrategiesAsync();

        Assert.Empty(strategies);
    }

    [Fact]
    public async Task ListStrategiesAsync_RejectsNonEmptyCatalogWithoutTrustedDeclaration()
    {
        var runtime = new LegacyProfileRuntime(
        [
            new EngineProfileDescriptor(EngineProfileId.Parse("youtube"), "YouTube")
        ]);
        var admission = new FixedAdmissionPolicy(true);
        var adapter = new EngineProfileStrategyRuntimeAdapter(runtime, admission);

        var exception = await Assert.ThrowsAsync<StrategyRuntimeException>(
            () => adapter.ListStrategiesAsync());

        Assert.Equal("MissingTrustedEngineCapabilityDeclaration", exception.Code);
        Assert.Equal(0, admission.CallCount);
    }

    [Fact]
    public async Task ListStrategiesAsync_RejectsWeakProviderWithManuallyClaimedCapabilities()
    {
        var runtime = new WeakOnlyCapabilityRuntime(
        [
            new EngineProfileDescriptor(EngineProfileId.Parse("discord"), "Discord")
        ]);
        var admission = new FixedAdmissionPolicy(true);
        var adapter = new EngineProfileStrategyRuntimeAdapter(runtime, admission);

        var exception = await Assert.ThrowsAsync<StrategyRuntimeException>(
            () => adapter.ListStrategiesAsync());

        Assert.Equal("MissingTrustedEngineCapabilityDeclaration", exception.Code);
        Assert.Equal(0, admission.CallCount);
    }

    [Fact]
    public async Task ListStrategiesAsync_RejectsBackendIdentityMismatchBeforeAdmission()
    {
        var runtime = new FakeProfileRuntime
        {
            EngineIdOverride = "zapret2",
            CapabilityDeclaration = BuiltInEngineBackendCapabilityCatalog.Get(
                BuiltInEngineBackendKind.ByeDpi,
                EngineHostPlatform.Windows),
            Profiles =
            [
                new EngineProfileDescriptor(EngineProfileId.Parse("youtube"), "YouTube")
            ]
        };
        var admission = new FixedAdmissionPolicy(true);
        var adapter = new EngineProfileStrategyRuntimeAdapter(runtime, admission);

        var exception = await Assert.ThrowsAsync<StrategyRuntimeException>(
            () => adapter.ListStrategiesAsync());

        Assert.Equal("EngineCapabilityIdentityMismatch", exception.Code);
        Assert.Equal(0, admission.CallCount);
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("../profile")]
    [InlineData("profile.lua")]
    [InlineData("luaexec:code")]
    [InlineData("x=1")]
    [InlineData("a;b")]
    public async Task ActivateAsync_RejectsCommandLikeInputBeforeCatalogOrAdmission(string value)
    {
        var runtime = new FakeProfileRuntime();
        var admission = new FixedAdmissionPolicy(true);
        var adapter = new EngineProfileStrategyRuntimeAdapter(runtime, admission);

        var exception = await Assert.ThrowsAsync<StrategyRuntimeException>(
            () => adapter.ActivateAsync(value));

        Assert.Equal("InvalidProfileId", exception.Code);
        Assert.Null(runtime.LastActivatedProfileId);
        Assert.Equal(0, admission.CallCount);
    }

    [Fact]
    public async Task ActivateAsync_PassesTypedPublishedProfileIdToRuntime_WhenAdmitted()
    {
        var runtime = new FakeProfileRuntime
        {
            Profiles =
            [
                new EngineProfileDescriptor(
                    EngineProfileId.Parse("discord-youtube"),
                    "Discord + YouTube")
            ]
        };
        var adapter = new EngineProfileStrategyRuntimeAdapter(
            runtime,
            new FixedAdmissionPolicy(true));

        await adapter.ActivateAsync("discord-youtube");

        Assert.Equal(
            EngineProfileId.Parse("discord-youtube"),
            runtime.LastActivatedProfileId);
    }

    [Fact]
    public async Task ActivateAsync_RejectsUnpublishedProfileBeforeRuntime()
    {
        var runtime = new FakeProfileRuntime
        {
            Profiles =
            [
                new EngineProfileDescriptor(EngineProfileId.Parse("youtube"), "YouTube")
            ]
        };
        var admission = new FixedAdmissionPolicy(true);
        var adapter = new EngineProfileStrategyRuntimeAdapter(runtime, admission);

        var exception = await Assert.ThrowsAsync<StrategyRuntimeException>(
            () => adapter.ActivateAsync("discord"));

        Assert.Equal("UnknownProfile", exception.Code);
        Assert.Null(runtime.LastActivatedProfileId);
        Assert.Equal(0, admission.CallCount);
    }

    [Fact]
    public async Task ActivateAsync_RejectsPublishedProfileWhenRuntimeIsNotAdmitted()
    {
        var runtime = new FakeProfileRuntime
        {
            Profiles =
            [
                new EngineProfileDescriptor(EngineProfileId.Parse("youtube"), "YouTube")
            ]
        };
        var adapter = new EngineProfileStrategyRuntimeAdapter(
            runtime,
            DenyAllEngineRuntimeAdmissionPolicy.Instance);

        var exception = await Assert.ThrowsAsync<StrategyRuntimeException>(
            () => adapter.ActivateAsync("youtube"));

        Assert.Equal("EngineRuntimeNotAdmitted", exception.Code);
        Assert.Null(runtime.LastActivatedProfileId);
    }

    [Fact]
    public async Task ActivateAsync_RejectsPublishedProfileMissingRequiredCapabilities()
    {
        var runtime = new FakeProfileRuntime
        {
            CapabilityDeclaration = BuiltInEngineBackendCapabilityCatalog.Get(
                BuiltInEngineBackendKind.ByeDpi,
                EngineHostPlatform.Windows),
            Profiles =
            [
                new EngineProfileDescriptor(EngineProfileId.Parse("discord"), "Discord")
            ]
        };
        var adapter = new EngineProfileStrategyRuntimeAdapter(
            runtime,
            new FixedAdmissionPolicy(true));

        var exception = await Assert.ThrowsAsync<StrategyRuntimeException>(
            () => adapter.ActivateAsync("discord"));

        Assert.Equal("UnsupportedProfileCapabilities", exception.Code);
        Assert.Null(runtime.LastActivatedProfileId);
    }

    [Fact]
    public async Task ActivateAsync_TranslatesEngineRuntimeFailureAfterAllOuterGatesPass()
    {
        var runtime = new FakeProfileRuntime
        {
            Profiles =
            [
                new EngineProfileDescriptor(EngineProfileId.Parse("discord"), "Discord")
            ],
            ActivationException = new EngineProfileRuntimeException(
                "UnknownProfile",
                "Профиль отсутствует в локальном allowlist.")
        };
        var adapter = new EngineProfileStrategyRuntimeAdapter(
            runtime,
            new FixedAdmissionPolicy(true));

        var exception = await Assert.ThrowsAsync<StrategyRuntimeException>(
            () => adapter.ActivateAsync("discord"));

        Assert.Equal("UnknownProfile", exception.Code);
        Assert.IsType<EngineProfileRuntimeException>(exception.InnerException);
    }

    [Fact]
    public async Task GetStateAsync_MapsActivePublishedProfile_WhenAdmitted()
    {
        var runtime = new FakeProfileRuntime
        {
            Profiles =
            [
                new EngineProfileDescriptor(EngineProfileId.Parse("discord"), "Discord")
            ],
            State = EngineProfileRuntimeState.Active(
                EngineProfileId.Parse("discord"))
        };
        var adapter = new EngineProfileStrategyRuntimeAdapter(
            runtime,
            new FixedAdmissionPolicy(true));

        var state = await adapter.GetStateAsync();

        Assert.True(state.IsRunning);
        Assert.Equal("discord", state.StrategyId);
    }

    [Fact]
    public async Task GetStateAsync_RejectsActiveProfileNotPublished()
    {
        var runtime = new FakeProfileRuntime
        {
            Profiles =
            [
                new EngineProfileDescriptor(EngineProfileId.Parse("youtube"), "YouTube")
            ],
            State = EngineProfileRuntimeState.Active(
                EngineProfileId.Parse("discord"))
        };
        var admission = new FixedAdmissionPolicy(true);
        var adapter = new EngineProfileStrategyRuntimeAdapter(runtime, admission);

        var exception = await Assert.ThrowsAsync<StrategyRuntimeException>(
            () => adapter.GetStateAsync());

        Assert.Equal("ActiveProfileNotPublished", exception.Code);
        Assert.Equal(0, admission.CallCount);
    }

    [Fact]
    public async Task GetStateAsync_RejectsActiveRuntimeWithoutAdmission()
    {
        var runtime = new FakeProfileRuntime
        {
            Profiles =
            [
                new EngineProfileDescriptor(EngineProfileId.Parse("youtube"), "YouTube")
            ],
            State = EngineProfileRuntimeState.Active(
                EngineProfileId.Parse("youtube"))
        };
        var adapter = new EngineProfileStrategyRuntimeAdapter(
            runtime,
            DenyAllEngineRuntimeAdmissionPolicy.Instance);

        var exception = await Assert.ThrowsAsync<StrategyRuntimeException>(
            () => adapter.GetStateAsync());

        Assert.Equal("EngineRuntimeNotAdmitted", exception.Code);
    }

    [Fact]
    public async Task GetStateAsync_RejectsActiveProfileCapabilitiesMismatch()
    {
        var runtime = new FakeProfileRuntime
        {
            CapabilityDeclaration = BuiltInEngineBackendCapabilityCatalog.Get(
                BuiltInEngineBackendKind.ByeDpi,
                EngineHostPlatform.Windows),
            Profiles =
            [
                new EngineProfileDescriptor(EngineProfileId.Parse("discord"), "Discord")
            ],
            State = EngineProfileRuntimeState.Active(
                EngineProfileId.Parse("discord"))
        };
        var adapter = new EngineProfileStrategyRuntimeAdapter(
            runtime,
            new FixedAdmissionPolicy(true));

        var exception = await Assert.ThrowsAsync<StrategyRuntimeException>(
            () => adapter.GetStateAsync());

        Assert.Equal("ActiveProfileCapabilitiesMismatch", exception.Code);
    }

    [Fact]
    public void DenyAllAdmissionPolicy_AlwaysRejectsTrustedDeclaration()
    {
        var runtime = new FakeProfileRuntime();

        var admitted = DenyAllEngineRuntimeAdmissionPolicy.Instance.IsAdmitted(
            runtime,
            runtime.CapabilityDeclaration);

        Assert.False(admitted);
    }

    private sealed class FakeProfileRuntime :
        IEngineProfileRuntime,
        IEngineBackendCapabilityProvider
    {
        public EngineBackendCapabilityDeclaration CapabilityDeclaration { get; init; } =
            BuiltInEngineBackendCapabilityCatalog.Get(
                BuiltInEngineBackendKind.Zapret2,
                EngineHostPlatform.Windows);

        public string? EngineIdOverride { get; init; }

        public string EngineId => EngineIdOverride ?? CapabilityDeclaration.EngineId;

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

    private sealed class FixedAdmissionPolicy(bool admitted) : IEngineRuntimeAdmissionPolicy
    {
        public int CallCount { get; private set; }

        public IEngineProfileRuntime? LastRuntime { get; private set; }

        public EngineBackendCapabilityDeclaration? LastDeclaration { get; private set; }

        public bool IsAdmitted(
            IEngineProfileRuntime profileRuntime,
            EngineBackendCapabilityDeclaration declaration)
        {
            CallCount++;
            LastRuntime = profileRuntime;
            LastDeclaration = declaration;
            return admitted;
        }
    }

    private sealed class LegacyProfileRuntime(
        IReadOnlyList<EngineProfileDescriptor> profiles) : IEngineProfileRuntime
    {
        public string EngineId => "legacy-no-capabilities";

        public ValueTask<IReadOnlyList<EngineProfileDescriptor>> ListProfilesAsync(
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(profiles);

        public ValueTask<EngineProfileRuntimeState> GetProfileStateAsync(
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(EngineProfileRuntimeState.Inactive);

        public ValueTask ActivateProfileAsync(
            EngineProfileId profileId,
            CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }

    private sealed class WeakOnlyCapabilityRuntime(
        IReadOnlyList<EngineProfileDescriptor> profiles) :
        IEngineProfileRuntime,
        IEngineCapabilityProvider
    {
        public string EngineId => "manual-capabilities";

        public EngineCapabilitySet Capabilities { get; } = new(
            EngineTrafficCapability.TransparentTcp
            | EngineTrafficCapability.TransparentUdp);

        public ValueTask<IReadOnlyList<EngineProfileDescriptor>> ListProfilesAsync(
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(profiles);

        public ValueTask<EngineProfileRuntimeState> GetProfileStateAsync(
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(EngineProfileRuntimeState.Inactive);

        public ValueTask ActivateProfileAsync(
            EngineProfileId profileId,
            CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }
}
