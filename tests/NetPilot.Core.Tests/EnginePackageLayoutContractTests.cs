using System.Reflection;
using NetPilot.Engine.Abstractions;

namespace NetPilot.Core.Tests;

public sealed class EnginePackageLayoutContractTests
{
    private const string DigestA =
        "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string DigestB =
        "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string DigestC =
        "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";

    [Fact]
    public void Trusted_package_layout_catalog_is_empty_before_owned_fork_package()
    {
        Assert.Empty(BuiltInEnginePackageLayoutCatalog.All);
    }

    [Fact]
    public void Layout_and_component_have_no_public_constructors()
    {
        Assert.Empty(typeof(EnginePackageLayoutDeclaration)
            .GetConstructors(BindingFlags.Instance | BindingFlags.Public));
        Assert.Empty(typeof(EnginePackageComponentLayout)
            .GetConstructors(BindingFlags.Instance | BindingFlags.Public));
    }

    [Fact]
    public void Provider_exposes_only_package_layout_object()
    {
        var property = Assert.Single(typeof(IEnginePackageLayoutProvider)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public));

        Assert.Equal("PackageLayout", property.Name);
        Assert.Equal(typeof(EnginePackageLayoutDeclaration), property.PropertyType);
    }

    [Theory]
    [InlineData("../engine.exe")]
    [InlineData("bin/../engine.exe")]
    [InlineData("/bin/engine.exe")]
    [InlineData("C:/bin/engine.exe")]
    [InlineData("bin\\engine.exe")]
    [InlineData("bin//engine.exe")]
    [InlineData("bin/CON.txt")]
    [InlineData("bin/nul")]
    [InlineData("bin/COM1.dll")]
    [InlineData("bin/engine.")]
    public void Component_layout_rejects_traversal_alias_or_noncanonical_paths(string relativePath)
    {
        var constructor = GetComponentLayoutConstructor();

        var exception = Assert.Throws<TargetInvocationException>(() =>
            constructor.Invoke(new object[] { "engine", relativePath }));

        Assert.IsType<ArgumentException>(exception.InnerException);
    }

    [Fact]
    public void Package_layout_requires_exact_identity_component_set()
    {
        var identity = CreateIdentity();
        var engineLayout = CreateComponentLayout("engine", "bin/winws2.exe");
        var constructor = GetPackageLayoutConstructor();

        var exception = Assert.Throws<TargetInvocationException>(() =>
            constructor.Invoke(
                new object[]
                {
                    identity,
                    new[] { engineLayout }
                }));

        Assert.IsType<ArgumentException>(exception.InnerException);
    }

    [Fact]
    public void Package_layout_takes_read_only_snapshot_and_accepts_fixed_relative_paths()
    {
        var identity = CreateIdentity();
        var engineLayout = CreateComponentLayout("engine", "bin/winws2.exe");
        var runtimeLayout = CreateComponentLayout("runtime-library", "bin/runtime.dll");
        var source = new List<EnginePackageComponentLayout>
        {
            engineLayout,
            runtimeLayout
        };
        var constructor = GetPackageLayoutConstructor();

        var layout = (EnginePackageLayoutDeclaration)constructor.Invoke(
            new object[] { identity, source });

        source.Clear();

        Assert.Equal(2, layout.Components.Count);
        Assert.Equal("bin/winws2.exe", layout.Components[0].RelativePath);
        Assert.Equal("bin/runtime.dll", layout.Components[1].RelativePath);
    }

    private static EngineExecutableIdentityDeclaration CreateIdentity()
    {
        var componentConstructor = typeof(EngineExecutableComponentIdentity)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(static candidate => candidate.GetParameters().Length == 3);

        var engine = (EngineExecutableComponentIdentity)componentConstructor.Invoke(
            new object[]
            {
                "engine",
                EngineExecutableComponentKind.PrimaryExecutable,
                DigestA
            });
        var runtime = (EngineExecutableComponentIdentity)componentConstructor.Invoke(
            new object[]
            {
                "runtime-library",
                EngineExecutableComponentKind.RuntimeLibrary,
                DigestB
            });

        var identityConstructor = typeof(EngineExecutableIdentityDeclaration)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(static candidate => candidate.GetParameters().Length == 8);

        return (EngineExecutableIdentityDeclaration)identityConstructor.Invoke(
            new object[]
            {
                BuiltInEngineBackendKind.Zapret2,
                EngineHostPlatform.Windows,
                EngineBinaryArchitecture.X64,
                "1.0.6-np.1",
                DigestA,
                DigestB,
                DigestC,
                new[] { engine, runtime }
            });
    }

    private static EnginePackageComponentLayout CreateComponentLayout(
        string componentId,
        string relativePath) =>
        (EnginePackageComponentLayout)GetComponentLayoutConstructor().Invoke(
            new object[] { componentId, relativePath });

    private static ConstructorInfo GetComponentLayoutConstructor() =>
        typeof(EnginePackageComponentLayout)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(static candidate =>
            {
                var parameters = candidate.GetParameters();
                return parameters.Length == 2
                    && parameters[0].ParameterType == typeof(string)
                    && parameters[1].ParameterType == typeof(string);
            });

    private static ConstructorInfo GetPackageLayoutConstructor() =>
        typeof(EnginePackageLayoutDeclaration)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(static candidate =>
            {
                var parameters = candidate.GetParameters();
                return parameters.Length == 2
                    && parameters[0].ParameterType == typeof(EngineExecutableIdentityDeclaration)
                    && parameters[1].ParameterType == typeof(IReadOnlyList<EnginePackageComponentLayout>);
            });
}
