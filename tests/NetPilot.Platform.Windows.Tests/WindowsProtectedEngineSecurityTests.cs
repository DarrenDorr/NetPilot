using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Security.AccessControl;
using System.Security.Principal;
using NetPilot.Engine.Abstractions;
using NetPilot.Platform.Windows;

namespace NetPilot.Platform.Windows.Tests;

public sealed class WindowsProtectedEngineSecurityTests
{
    private const string DigestA =
        "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string DigestB =
        "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string DigestC =
        "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";

    [Fact]
    public void Public_acl_verifier_accepts_only_verified_package_not_arbitrary_path()
    {
        var method = typeof(WindowsProtectedEngineSecurityVerifier)
            .GetMethod(
                nameof(WindowsProtectedEngineSecurityVerifier.Verify),
                BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException("Verify method not found.");

        var parameter = Assert.Single(method.GetParameters());
        Assert.Equal(typeof(WindowsVerifiedEnginePackage), parameter.ParameterType);
        Assert.NotEqual(typeof(string), parameter.ParameterType);

        Assert.Empty(typeof(WindowsProtectedEngineSecurityVerification)
            .GetConstructors(BindingFlags.Instance | BindingFlags.Public));
        Assert.Empty(typeof(WindowsVerifiedEnginePreLaunchHandoff)
            .GetConstructors(BindingFlags.Instance | BindingFlags.Public));
    }

    [Fact]
    public void Acl_policy_allows_privileged_owner_and_readonly_users()
    {
        var security = CreateBaselineDirectorySecurity();

        InvokeEvaluateSecurityDescriptor(security);
    }

    [Fact]
    public void Acl_policy_rejects_unprivileged_mutation_allow_rule()
    {
        var security = CreateBaselineDirectorySecurity();
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
            FileSystemRights.Modify,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));

        var exception = Assert.Throws<WindowsEngineSecurityVerificationException>(() =>
            InvokeEvaluateSecurityDescriptor(security));

        Assert.Equal("UntrustedMutationAcl", exception.Code);
    }

    [Fact]
    public void Acl_policy_rejects_unprivileged_owner()
    {
        var security = CreateBaselineDirectorySecurity();
        security.SetOwner(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null));

        var exception = Assert.Throws<WindowsEngineSecurityVerificationException>(() =>
            InvokeEvaluateSecurityDescriptor(security));

        Assert.Equal("UntrustedSecurityOwner", exception.Code);
    }

    [Fact]
    public void Acl_policy_allows_creator_owner_mutation_only_with_privileged_owner()
    {
        var security = CreateBaselineDirectorySecurity();
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.CreatorOwnerSid, null),
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.InheritOnly,
            AccessControlType.Allow));

        InvokeEvaluateSecurityDescriptor(security);
    }

    [Fact]
    public void Handoff_requires_live_verified_package_and_matching_security_verification()
    {
        using var fixture = CreateHandoffFixture();

        var handoff = WindowsVerifiedEnginePreLaunchHandoffFactory.Create(
            fixture.Binding,
            fixture.Package,
            fixture.SecurityVerification);

        handoff.EnsureReady();
        Assert.Same(fixture.Binding, handoff.LaunchIdentityBinding);
        Assert.Same(fixture.Package, handoff.VerifiedPackage);
        Assert.Same(fixture.SecurityVerification, handoff.SecurityVerification);

        fixture.Package.Dispose();

        Assert.Throws<ObjectDisposedException>(() => handoff.EnsureReady());
        Assert.Throws<ObjectDisposedException>(() =>
            WindowsVerifiedEnginePreLaunchHandoffFactory.Create(
                fixture.Binding,
                fixture.Package,
                fixture.SecurityVerification));
    }

    [Fact]
    public void Handoff_rejects_security_verification_from_other_package()
    {
        using var first = CreateHandoffFixture();
        using var second = CreateHandoffFixture();

        var exception = Assert.Throws<WindowsEngineSecurityVerificationException>(() =>
            WindowsVerifiedEnginePreLaunchHandoffFactory.Create(
                first.Binding,
                first.Package,
                second.SecurityVerification));

        Assert.Equal("HandoffSecurityVerificationMismatch", exception.Code);
    }

    [Fact]
    public void Handoff_rejects_different_immutable_identity()
    {
        using var first = CreateHandoffFixture();
        using var second = CreateHandoffFixture();

        var exception = Assert.Throws<WindowsEngineSecurityVerificationException>(() =>
            WindowsVerifiedEnginePreLaunchHandoffFactory.Create(
                second.Binding,
                first.Package,
                first.SecurityVerification));

        Assert.Equal("HandoffIdentityMismatch", exception.Code);
    }

    private static DirectorySecurity CreateBaselineDirectorySecurity()
    {
        var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        var administrators = new SecurityIdentifier(
            WellKnownSidType.BuiltinAdministratorsSid,
            null);
        var users = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);

        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.SetOwner(system);
        security.AddAccessRule(new FileSystemAccessRule(
            system,
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(
            administrators,
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(
            users,
            FileSystemRights.ReadAndExecute,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
        return security;
    }

    private static void InvokeEvaluateSecurityDescriptor(FileSystemSecurity security)
    {
        var method = typeof(WindowsProtectedEngineSecurityVerifier)
            .GetMethod(
                "EvaluateSecurityDescriptor",
                BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("ACL policy test seam not found.");

        try
        {
            method.Invoke(null, new object[] { security });
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    private static HandoffFixture CreateHandoffFixture()
    {
        var tempFile = Path.Combine(
            Path.GetTempPath(),
            $"NetPilot-R28.17-{Guid.NewGuid():N}.bin");
        File.WriteAllText(tempFile, "handoff-lock");
        var stream = new FileStream(
            tempFile,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);

        try
        {
            var identity = CreateIdentity();
            var packageLayout = CreatePackageLayout(identity);
            var binding = CreateBinding(identity);
            var verifiedComponent = CreateVerifiedComponent(tempFile);
            var package = CreateVerifiedPackage(
                identity,
                packageLayout,
                tempFile,
                verifiedComponent,
                stream);
            var securityVerification = CreateSecurityVerification(package);

            return new HandoffFixture(
                tempFile,
                binding,
                package,
                securityVerification);
        }
        catch
        {
            stream.Dispose();
            File.Delete(tempFile);
            throw;
        }
    }

    private static EngineExecutableIdentityDeclaration CreateIdentity()
    {
        var componentConstructor = typeof(EngineExecutableComponentIdentity)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(static candidate => candidate.GetParameters().Length == 3);
        var primary = (EngineExecutableComponentIdentity)componentConstructor.Invoke(
            new object[]
            {
                "engine",
                EngineExecutableComponentKind.PrimaryExecutable,
                DigestA
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
                DigestA,
                DigestB,
                DigestC,
                new[] { primary }
            });
    }

    private static EnginePackageLayoutDeclaration CreatePackageLayout(
        EngineExecutableIdentityDeclaration identity)
    {
        var componentConstructor = typeof(EnginePackageComponentLayout)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(static candidate => candidate.GetParameters().Length == 2);
        var component = (EnginePackageComponentLayout)componentConstructor.Invoke(
            new object[] { "engine", "bin/engine.exe" });

        var layoutConstructor = typeof(EnginePackageLayoutDeclaration)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(static candidate => candidate.GetParameters().Length == 2);

        return (EnginePackageLayoutDeclaration)layoutConstructor.Invoke(
            new object[] { identity, new[] { component } });
    }

    private static EngineLaunchIdentityBinding CreateBinding(
        EngineExecutableIdentityDeclaration identity)
    {
        var capability = BuiltInEngineBackendCapabilityCatalog.Get(
            BuiltInEngineBackendKind.Zapret2,
            EngineHostPlatform.Windows);

        var budgetConstructor = typeof(EngineResourceBudgetDeclaration)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(static candidate => candidate.GetParameters().Length == 4);
        var budget = (EngineResourceBudgetDeclaration)budgetConstructor.Invoke(
            new object[]
            {
                BuiltInEngineBackendKind.Zapret2,
                EngineHostPlatform.Windows,
                (uint)1,
                (ulong)1
            });

        var prerequisiteConstructor = typeof(EngineRuntimePrerequisiteDeclaration)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(static candidate => candidate.GetParameters().Length == 2);
        var prerequisites = (EngineRuntimePrerequisiteDeclaration)prerequisiteConstructor.Invoke(
            new object[] { capability, budget });

        var metadataConstructor = typeof(EngineLaunchPlanMetadata)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(static candidate => candidate.GetParameters().Length == 2);
        var metadata = (EngineLaunchPlanMetadata)metadataConstructor.Invoke(
            new object[]
            {
                prerequisites,
                BuiltInEngineProfileCatalog.StandardId
            });

        var bindingConstructor = typeof(EngineLaunchIdentityBinding)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(static candidate => candidate.GetParameters().Length == 3);
        return (EngineLaunchIdentityBinding)bindingConstructor.Invoke(
            new object[]
            {
                metadata,
                identity,
                EngineBinaryArchitecture.X64
            });
    }

    private static WindowsVerifiedEngineComponent CreateVerifiedComponent(string fullPath)
    {
        var constructor = typeof(WindowsVerifiedEngineComponent)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(static candidate => candidate.GetParameters().Length == 4);
        return (WindowsVerifiedEngineComponent)constructor.Invoke(
            new object[]
            {
                "engine",
                EngineExecutableComponentKind.PrimaryExecutable,
                fullPath,
                DigestA
            });
    }

    private static WindowsVerifiedEnginePackage CreateVerifiedPackage(
        EngineExecutableIdentityDeclaration identity,
        EnginePackageLayoutDeclaration packageLayout,
        string tempFile,
        WindowsVerifiedEngineComponent component,
        FileStream stream)
    {
        var constructor = typeof(WindowsVerifiedEnginePackage)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(static candidate => candidate.GetParameters().Length == 5);
        return (WindowsVerifiedEnginePackage)constructor.Invoke(
            new object[]
            {
                identity,
                packageLayout,
                Path.GetDirectoryName(tempFile)!,
                Array.AsReadOnly(new[] { component }),
                new[] { stream }
            });
    }

    private static WindowsProtectedEngineSecurityVerification CreateSecurityVerification(
        WindowsVerifiedEnginePackage package)
    {
        var constructor = typeof(WindowsProtectedEngineSecurityVerification)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(static candidate => candidate.GetParameters().Length == 2);
        return (WindowsProtectedEngineSecurityVerification)constructor.Invoke(
            new object[] { package, 1 });
    }

    private sealed class HandoffFixture(
        string tempFile,
        EngineLaunchIdentityBinding binding,
        WindowsVerifiedEnginePackage package,
        WindowsProtectedEngineSecurityVerification securityVerification) : IDisposable
    {
        public EngineLaunchIdentityBinding Binding { get; } = binding;
        public WindowsVerifiedEnginePackage Package { get; } = package;
        public WindowsProtectedEngineSecurityVerification SecurityVerification { get; } =
            securityVerification;

        public void Dispose()
        {
            Package.Dispose();
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }
}
