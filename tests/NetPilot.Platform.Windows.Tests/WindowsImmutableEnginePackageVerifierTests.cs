using System.ComponentModel;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using NetPilot.Engine.Abstractions;
using NetPilot.Platform.Windows;

namespace NetPilot.Platform.Windows.Tests;

public sealed class WindowsImmutableEnginePackageVerifierTests
{
    private const string DigestB =
        "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string DigestC =
        "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";

    [Fact]
    public void Public_verify_api_accepts_no_user_defined_path()
    {
        var method = Assert.Single(
            typeof(WindowsImmutableEnginePackageVerifier)
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(static candidate => candidate.Name == nameof(WindowsImmutableEnginePackageVerifier.Verify)));

        Assert.Collection(
            method.GetParameters(),
            first => Assert.Equal(typeof(EngineLaunchIdentityBinding), first.ParameterType),
            second => Assert.Equal(typeof(EnginePackageLayoutDeclaration), second.ParameterType));
        Assert.DoesNotContain(
            method.GetParameters(),
            static parameter => parameter.ParameterType == typeof(string));

        Assert.Empty(typeof(WindowsVerifiedEnginePackage)
            .GetConstructors(BindingFlags.Instance | BindingFlags.Public));
        Assert.Empty(typeof(WindowsVerifiedEngineComponent)
            .GetConstructors(BindingFlags.Instance | BindingFlags.Public));
    }

    [Fact]
    public void Verify_accepts_exact_component_and_holds_write_delete_lock()
    {
        var fixture = CreateFixture("verified-content");
        WindowsVerifiedEnginePackage? verified = null;

        try
        {
            verified = InvokeVerifyResolved(
                fixture.Identity,
                fixture.PackageLayout,
                fixture.ResolvedLayout);

            Assert.True(verified.IsVerificationLockHeld);
            Assert.Same(fixture.Identity, verified.ExecutableIdentity);
            Assert.Same(fixture.PackageLayout, verified.PackageLayout);

            var component = Assert.Single(verified.Components);
            Assert.Equal("engine", component.ComponentId);
            Assert.Equal(EngineExecutableComponentKind.PrimaryExecutable, component.Kind);
            Assert.Equal(fixture.ExpectedSha256, component.Sha256);
            Assert.True(string.Equals(
                Path.GetFullPath(fixture.ComponentPath),
                component.FullPath,
                StringComparison.OrdinalIgnoreCase));

            Assert.ThrowsAny<IOException>(() =>
            {
                using var writer = new FileStream(
                    fixture.ComponentPath,
                    FileMode.Open,
                    FileAccess.Write,
                    FileShare.ReadWrite | FileShare.Delete);
            });

            verified.Dispose();
            Assert.False(verified.IsVerificationLockHeld);

            using var writableAfterDispose = new FileStream(
                fixture.ComponentPath,
                FileMode.Open,
                FileAccess.Write,
                FileShare.ReadWrite | FileShare.Delete);
        }
        finally
        {
            verified?.Dispose();
            fixture.Dispose();
        }
    }

    [Fact]
    public void Verify_rejects_hash_mismatch()
    {
        var fixture = CreateFixture(
            "actual-content",
            identityContent: "different-expected-content");

        try
        {
            var exception = Assert.Throws<WindowsEnginePackageVerificationException>(() =>
                InvokeVerifyResolved(
                    fixture.Identity,
                    fixture.PackageLayout,
                    fixture.ResolvedLayout));

            Assert.Equal("ComponentHashMismatch", exception.Code);
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public void Verify_rejects_missing_component()
    {
        var fixture = CreateFixture("expected-content");

        try
        {
            File.Delete(fixture.ComponentPath);

            var exception = Assert.Throws<WindowsEnginePackageVerificationException>(() =>
                InvokeVerifyResolved(
                    fixture.Identity,
                    fixture.PackageLayout,
                    fixture.ResolvedLayout));

            Assert.Equal("ComponentMissing", exception.Code);
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public void Verify_rejects_extra_file_in_package_tree()
    {
        var fixture = CreateFixture("expected-content");

        try
        {
            File.WriteAllText(
                Path.Combine(fixture.EngineRoot, "unexpected.dll"),
                "not part of immutable identity");

            var exception = Assert.Throws<WindowsEnginePackageVerificationException>(() =>
                InvokeVerifyResolved(
                    fixture.Identity,
                    fixture.PackageLayout,
                    fixture.ResolvedLayout));

            Assert.Equal("UnexpectedPackageEntry", exception.Code);
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public void Verify_rejects_hardlinked_component()
    {
        var fixture = CreateFixture("expected-content");
        var aliasPath = Path.Combine(fixture.TempRoot, "hardlink-alias.exe");

        try
        {
            if (!CreateHardLink(aliasPath, fixture.ComponentPath, IntPtr.Zero))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            var exception = Assert.Throws<WindowsEnginePackageVerificationException>(() =>
                InvokeVerifyResolved(
                    fixture.Identity,
                    fixture.PackageLayout,
                    fixture.ResolvedLayout));

            Assert.Equal("ComponentHardlinkRejected", exception.Code);
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public void Verify_rejects_resolved_path_that_does_not_match_trusted_layout()
    {
        var fixture = CreateFixture("expected-content");

        try
        {
            var alternatePath = Path.Combine(fixture.EngineRoot, "alternate.exe");
            File.Copy(fixture.ComponentPath, alternatePath);
            var forgedResolved = CreateResolvedLayout(
                fixture.EngineRoot,
                "engine",
                alternatePath);

            var exception = Assert.Throws<WindowsEnginePackageVerificationException>(() =>
                InvokeVerifyResolved(
                    fixture.Identity,
                    fixture.PackageLayout,
                    forgedResolved));

            Assert.Equal("ResolvedPathMismatch", exception.Code);
        }
        finally
        {
            fixture.Dispose();
        }
    }

    private static PackageFixture CreateFixture(
        string fileContent,
        string? identityContent = null)
    {
        var tempRoot = Path.Combine(
            Path.GetTempPath(),
            $"NetPilot-R28.16-{Guid.NewGuid():N}");
        var engineRoot = Path.Combine(tempRoot, "engine-root");
        var binDirectory = Path.Combine(engineRoot, "bin");
        Directory.CreateDirectory(binDirectory);

        var componentPath = Path.Combine(binDirectory, "engine.exe");
        File.WriteAllText(componentPath, fileContent, new UTF8Encoding(false));

        var expectedBytes = Encoding.UTF8.GetBytes(identityContent ?? fileContent);
        var expectedSha256 = Convert
            .ToHexString(SHA256.HashData(expectedBytes))
            .ToLowerInvariant();

        var identity = CreateIdentity(expectedSha256);
        var packageLayout = CreatePackageLayout(identity, "bin/engine.exe");
        var resolvedLayout = CreateResolvedLayout(
            engineRoot,
            "engine",
            componentPath);

        return new PackageFixture(
            tempRoot,
            engineRoot,
            componentPath,
            expectedSha256,
            identity,
            packageLayout,
            resolvedLayout);
    }

    private static EngineExecutableIdentityDeclaration CreateIdentity(string componentSha256)
    {
        var componentConstructor = typeof(EngineExecutableComponentIdentity)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(static candidate => candidate.GetParameters().Length == 3);
        var component = (EngineExecutableComponentIdentity)componentConstructor.Invoke(
            new object[]
            {
                "engine",
                EngineExecutableComponentKind.PrimaryExecutable,
                componentSha256
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
                "1.0.6-np.test",
                componentSha256,
                DigestB,
                DigestC,
                new[] { component }
            });
    }

    private static EnginePackageLayoutDeclaration CreatePackageLayout(
        EngineExecutableIdentityDeclaration identity,
        string relativePath)
    {
        var componentConstructor = typeof(EnginePackageComponentLayout)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(static candidate => candidate.GetParameters().Length == 2);
        var component = (EnginePackageComponentLayout)componentConstructor.Invoke(
            new object[] { "engine", relativePath });

        var layoutConstructor = typeof(EnginePackageLayoutDeclaration)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(static candidate => candidate.GetParameters().Length == 2);

        return (EnginePackageLayoutDeclaration)layoutConstructor.Invoke(
            new object[] { identity, new[] { component } });
    }

    private static WindowsProtectedEngineInstallLayout CreateResolvedLayout(
        string engineRoot,
        string componentId,
        string componentPath)
    {
        var componentConstructor = typeof(WindowsResolvedEngineComponentPath)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(static candidate => candidate.GetParameters().Length == 2);
        var component = (WindowsResolvedEngineComponentPath)componentConstructor.Invoke(
            new object[] { componentId, componentPath });

        var layoutConstructor = typeof(WindowsProtectedEngineInstallLayout)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(static candidate => candidate.GetParameters().Length == 2);

        return (WindowsProtectedEngineInstallLayout)layoutConstructor.Invoke(
            new object[]
            {
                engineRoot,
                Array.AsReadOnly(new[] { component })
            });
    }

    private static WindowsVerifiedEnginePackage InvokeVerifyResolved(
        EngineExecutableIdentityDeclaration identity,
        EnginePackageLayoutDeclaration packageLayout,
        WindowsProtectedEngineInstallLayout resolvedLayout)
    {
        var method = typeof(WindowsImmutableEnginePackageVerifier)
            .GetMethod(
                "VerifyResolvedLayout",
                BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("VerifyResolvedLayout test seam not found.");

        try
        {
            return (WindowsVerifiedEnginePackage)method.Invoke(
                null,
                new object[] { identity, packageLayout, resolvedLayout })!;
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    private sealed class PackageFixture(
        string tempRoot,
        string engineRoot,
        string componentPath,
        string expectedSha256,
        EngineExecutableIdentityDeclaration identity,
        EnginePackageLayoutDeclaration packageLayout,
        WindowsProtectedEngineInstallLayout resolvedLayout) : IDisposable
    {
        public string TempRoot { get; } = tempRoot;
        public string EngineRoot { get; } = engineRoot;
        public string ComponentPath { get; } = componentPath;
        public string ExpectedSha256 { get; } = expectedSha256;
        public EngineExecutableIdentityDeclaration Identity { get; } = identity;
        public EnginePackageLayoutDeclaration PackageLayout { get; } = packageLayout;
        public WindowsProtectedEngineInstallLayout ResolvedLayout { get; } = resolvedLayout;

        public void Dispose()
        {
            if (Directory.Exists(TempRoot))
            {
                Directory.Delete(TempRoot, recursive: true);
            }
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLink(
        string lpFileName,
        string lpExistingFileName,
        IntPtr lpSecurityAttributes);
}
