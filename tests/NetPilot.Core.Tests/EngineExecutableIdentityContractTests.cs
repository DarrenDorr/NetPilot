using System.Reflection;
using NetPilot.Engine.Abstractions;

namespace NetPilot.Core.Tests;

public sealed class EngineExecutableIdentityContractTests
{
    private const string DigestA =
        "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string DigestB =
        "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string DigestC =
        "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";

    [Fact]
    public void Trusted_executable_identity_catalog_is_empty_before_owned_verified_build()
    {
        Assert.Empty(BuiltInEngineExecutableIdentityCatalog.All);
    }

    [Fact]
    public void TryGet_fails_closed_without_promoted_identity()
    {
        var found = BuiltInEngineExecutableIdentityCatalog.TryGet(
            BuiltInEngineBackendKind.Zapret2,
            EngineHostPlatform.Windows,
            EngineBinaryArchitecture.X64,
            out var declaration);

        Assert.False(found);
        Assert.Null(declaration);
    }

    [Fact]
    public void Get_rejects_missing_identity()
    {
        Assert.Throws<InvalidOperationException>(() =>
            BuiltInEngineExecutableIdentityCatalog.Get(
                BuiltInEngineBackendKind.Zapret2,
                EngineHostPlatform.Windows,
                EngineBinaryArchitecture.X64));
    }

    [Fact]
    public void Identity_and_component_have_no_public_constructors()
    {
        Assert.Empty(typeof(EngineExecutableIdentityDeclaration)
            .GetConstructors(BindingFlags.Instance | BindingFlags.Public));
        Assert.Empty(typeof(EngineExecutableComponentIdentity)
            .GetConstructors(BindingFlags.Instance | BindingFlags.Public));
    }

    [Fact]
    public void Provider_exposes_only_trusted_identity_object()
    {
        var properties = typeof(IEngineExecutableIdentityProvider)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public);

        var property = Assert.Single(properties);
        Assert.Equal("ExecutableIdentity", property.Name);
        Assert.Equal(typeof(EngineExecutableIdentityDeclaration), property.PropertyType);
    }

    [Fact]
    public void Identity_contains_no_execution_location_or_command_surface()
    {
        var properties = typeof(EngineExecutableIdentityDeclaration)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public);

        Assert.DoesNotContain(
            properties,
            static property => property.Name.Contains("Path", StringComparison.OrdinalIgnoreCase)
                || property.Name.Contains("FileName", StringComparison.OrdinalIgnoreCase)
                || property.Name.Contains("Argument", StringComparison.OrdinalIgnoreCase)
                || property.Name.Contains("Command", StringComparison.OrdinalIgnoreCase)
                || property.Name.Contains("Url", StringComparison.OrdinalIgnoreCase)
                || property.Name.Contains("Process", StringComparison.OrdinalIgnoreCase)
                || property.Name.Contains("Service", StringComparison.OrdinalIgnoreCase)
                || property.Name.Contains("Environment", StringComparison.OrdinalIgnoreCase)
                || property.Name.Contains("Working", StringComparison.OrdinalIgnoreCase)
                || property.Name.Contains("Script", StringComparison.OrdinalIgnoreCase)
                || property.Name.Contains("Lua", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("")]
    [InlineData("ABCDEFabcdef0123456789abcdef0123456789abcdef0123456789abcdef0123")]
    [InlineData("not-a-sha256")]
    public void Component_rejects_noncanonical_sha256(string sha256)
    {
        var constructor = GetComponentConstructor();

        var exception = Assert.Throws<TargetInvocationException>(() =>
            constructor.Invoke(
                new object[]
                {
                    "primary",
                    EngineExecutableComponentKind.PrimaryExecutable,
                    sha256
                }));

        Assert.IsType<ArgumentException>(exception.InnerException);
    }

    [Theory]
    [InlineData("../primary")]
    [InlineData("primary.exe")]
    [InlineData("primary/path")]
    [InlineData("Primary")]
    public void Component_id_rejects_path_or_noncanonical_tokens(string componentId)
    {
        var constructor = GetComponentConstructor();

        var exception = Assert.Throws<TargetInvocationException>(() =>
            constructor.Invoke(
                new object[]
                {
                    componentId,
                    EngineExecutableComponentKind.PrimaryExecutable,
                    DigestA
                }));

        Assert.IsType<ArgumentException>(exception.InnerException);
    }

    [Theory]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("1.0.")]
    [InlineData("../1.0")]
    [InlineData("1.0/next")]
    public void Identity_rejects_version_path_aliases_or_separators(string version)
    {
        var primary = CreateComponent(
            "engine",
            EngineExecutableComponentKind.PrimaryExecutable,
            DigestA);
        var constructor = GetIdentityConstructor();

        var exception = Assert.Throws<TargetInvocationException>(() =>
            constructor.Invoke(
                new object[]
                {
                    BuiltInEngineBackendKind.Zapret2,
                    EngineHostPlatform.Windows,
                    EngineBinaryArchitecture.X64,
                    version,
                    DigestA,
                    DigestB,
                    DigestC,
                    new[] { primary }
                }));

        Assert.IsType<ArgumentException>(exception.InnerException);
    }

    [Fact]
    public void Identity_requires_exactly_one_primary_executable()
    {
        var library = CreateComponent(
            "runtime-library",
            EngineExecutableComponentKind.RuntimeLibrary,
            DigestA);
        var constructor = GetIdentityConstructor();

        var exception = Assert.Throws<TargetInvocationException>(() =>
            constructor.Invoke(
                new object[]
                {
                    BuiltInEngineBackendKind.Zapret2,
                    EngineHostPlatform.Windows,
                    EngineBinaryArchitecture.X64,
                    "1.0.6-np.1",
                    DigestA,
                    DigestB,
                    DigestC,
                    new[] { library }
                }));

        Assert.IsType<ArgumentException>(exception.InnerException);
    }

    [Fact]
    public void Identity_rejects_duplicate_component_ids()
    {
        var primary = CreateComponent(
            "engine",
            EngineExecutableComponentKind.PrimaryExecutable,
            DigestA);
        var duplicate = CreateComponent(
            "engine",
            EngineExecutableComponentKind.SharedLibrary,
            DigestB);
        var constructor = GetIdentityConstructor();

        var exception = Assert.Throws<TargetInvocationException>(() =>
            constructor.Invoke(
                new object[]
                {
                    BuiltInEngineBackendKind.Zapret2,
                    EngineHostPlatform.Windows,
                    EngineBinaryArchitecture.X64,
                    "1.0.6-np.1",
                    DigestA,
                    DigestB,
                    DigestC,
                    new[] { primary, duplicate }
                }));

        Assert.IsType<ArgumentException>(exception.InnerException);
    }

    [Fact]
    public void Identity_takes_read_only_snapshot_of_components()
    {
        var primary = CreateComponent(
            "engine",
            EngineExecutableComponentKind.PrimaryExecutable,
            DigestA);
        var library = CreateComponent(
            "runtime-library",
            EngineExecutableComponentKind.RuntimeLibrary,
            DigestB);
        var source = new List<EngineExecutableComponentIdentity> { primary };
        var constructor = GetIdentityConstructor();

        var identity = (EngineExecutableIdentityDeclaration)constructor.Invoke(
            new object[]
            {
                BuiltInEngineBackendKind.Zapret2,
                EngineHostPlatform.Windows,
                EngineBinaryArchitecture.X64,
                "1.0.6-np.1",
                DigestA,
                DigestB,
                DigestC,
                source
            });

        source.Add(library);

        Assert.Single(identity.Components);
        Assert.Equal("engine", identity.Components[0].ComponentId);
        Assert.IsAssignableFrom<IReadOnlyList<EngineExecutableComponentIdentity>>(
            identity.Components);
    }

    private static EngineExecutableComponentIdentity CreateComponent(
        string componentId,
        EngineExecutableComponentKind kind,
        string sha256) =>
        (EngineExecutableComponentIdentity)GetComponentConstructor().Invoke(
            new object[] { componentId, kind, sha256 });

    private static ConstructorInfo GetComponentConstructor() =>
        typeof(EngineExecutableComponentIdentity)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(static constructor =>
            {
                var parameters = constructor.GetParameters();
                return parameters.Length == 3
                    && parameters[0].ParameterType == typeof(string)
                    && parameters[1].ParameterType == typeof(EngineExecutableComponentKind)
                    && parameters[2].ParameterType == typeof(string);
            });

    private static ConstructorInfo GetIdentityConstructor() =>
        typeof(EngineExecutableIdentityDeclaration)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(static constructor =>
            {
                var parameters = constructor.GetParameters();
                return parameters.Length == 8
                    && parameters[0].ParameterType == typeof(BuiltInEngineBackendKind)
                    && parameters[1].ParameterType == typeof(EngineHostPlatform)
                    && parameters[2].ParameterType == typeof(EngineBinaryArchitecture)
                    && parameters[3].ParameterType == typeof(string)
                    && parameters[4].ParameterType == typeof(string)
                    && parameters[5].ParameterType == typeof(string)
                    && parameters[6].ParameterType == typeof(string)
                    && parameters[7].ParameterType == typeof(IReadOnlyList<EngineExecutableComponentIdentity>);
            });
}
