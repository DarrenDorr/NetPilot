using System.Security.AccessControl;
using System.Security.Principal;
using NetPilot.Engine.Abstractions;

namespace NetPilot.Platform.Windows;

public sealed class WindowsEngineSecurityVerificationException(
    string code,
    string message,
    Exception? innerException = null) : Exception(message, innerException)
{
    public string Code { get; } = code;
}

/// <summary>
/// Доказательство того, что ACL/owner exact verified package прошли fail-closed
/// проверку. Объект не является admission или execution token.
/// </summary>
public sealed class WindowsProtectedEngineSecurityVerification
{
    internal WindowsProtectedEngineSecurityVerification(
        WindowsVerifiedEnginePackage verifiedPackage,
        int verifiedObjectCount)
    {
        VerifiedPackage = verifiedPackage;
        VerifiedObjectCount = verifiedObjectCount;
    }

    internal WindowsVerifiedEnginePackage VerifiedPackage { get; }

    public int VerifiedObjectCount { get; }
}

/// <summary>
/// Неисполняемый handoff между launch identity, immutable package verification
/// и ACL/owner verification. Он не содержит arguments и сам ничего не запускает.
/// </summary>
public sealed class WindowsVerifiedEnginePreLaunchHandoff
{
    internal WindowsVerifiedEnginePreLaunchHandoff(
        EngineLaunchIdentityBinding launchIdentityBinding,
        WindowsVerifiedEnginePackage verifiedPackage,
        WindowsProtectedEngineSecurityVerification securityVerification)
    {
        LaunchIdentityBinding = launchIdentityBinding;
        VerifiedPackage = verifiedPackage;
        SecurityVerification = securityVerification;
    }

    public EngineLaunchIdentityBinding LaunchIdentityBinding { get; }

    public WindowsVerifiedEnginePackage VerifiedPackage { get; }

    public WindowsProtectedEngineSecurityVerification SecurityVerification { get; }

    public void EnsureReady()
    {
        VerifiedPackage.EnsureVerificationLockHeld();

        if (!ReferenceEquals(
                LaunchIdentityBinding.ExecutableIdentity,
                VerifiedPackage.ExecutableIdentity))
        {
            throw new WindowsEngineSecurityVerificationException(
                "HandoffIdentityMismatch",
                "Pre-launch handoff больше не соответствует exact immutable executable identity.");
        }

        if (!ReferenceEquals(
                SecurityVerification.VerifiedPackage,
                VerifiedPackage))
        {
            throw new WindowsEngineSecurityVerificationException(
                "HandoffSecurityVerificationMismatch",
                "ACL/owner verification относится к другому verified package.");
        }
    }
}

public static class WindowsVerifiedEnginePreLaunchHandoffFactory
{
    public static WindowsVerifiedEnginePreLaunchHandoff Create(
        EngineLaunchIdentityBinding launchIdentityBinding,
        WindowsVerifiedEnginePackage verifiedPackage,
        WindowsProtectedEngineSecurityVerification securityVerification)
    {
        ArgumentNullException.ThrowIfNull(launchIdentityBinding);
        ArgumentNullException.ThrowIfNull(verifiedPackage);
        ArgumentNullException.ThrowIfNull(securityVerification);

        if (launchIdentityBinding.Platform != EngineHostPlatform.Windows)
        {
            throw new WindowsEngineSecurityVerificationException(
                "HandoffPlatformMismatch",
                "Windows pre-launch handoff требует Windows launch identity binding.");
        }

        if (!ReferenceEquals(
                launchIdentityBinding.ExecutableIdentity,
                verifiedPackage.ExecutableIdentity))
        {
            throw new WindowsEngineSecurityVerificationException(
                "HandoffIdentityMismatch",
                "Launch binding и verified package относятся к разным immutable identities.");
        }

        if (!ReferenceEquals(
                securityVerification.VerifiedPackage,
                verifiedPackage))
        {
            throw new WindowsEngineSecurityVerificationException(
                "HandoffSecurityVerificationMismatch",
                "ACL/owner verification относится к другому verified package.");
        }

        verifiedPackage.EnsureVerificationLockHeld();

        var handoff = new WindowsVerifiedEnginePreLaunchHandoff(
            launchIdentityBinding,
            verifiedPackage,
            securityVerification);
        handoff.EnsureReady();
        return handoff;
    }
}

/// <summary>
/// Read-only ACL/owner gate для уже проверенного immutable package.
/// Ничего не создаёт и не изменяет в security descriptor.
/// </summary>
public static class WindowsProtectedEngineSecurityVerifier
{
    private const FileSystemRights MutationRights =
        FileSystemRights.Write
        | FileSystemRights.Delete
        | FileSystemRights.DeleteSubdirectoriesAndFiles
        | FileSystemRights.ChangePermissions
        | FileSystemRights.TakeOwnership;

    private const string LocalSystemSid = "S-1-5-18";
    private const string BuiltinAdministratorsSid = "S-1-5-32-544";
    private const string TrustedInstallerSid =
        "S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464";
    private const string CreatorOwnerSid = "S-1-3-0";
    private const string OwnerRightsSid = "S-1-3-4";

    public static WindowsProtectedEngineSecurityVerification Verify(
        WindowsVerifiedEnginePackage verifiedPackage)
    {
        ArgumentNullException.ThrowIfNull(verifiedPackage);

        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Windows protected engine ACL verification доступна только в Windows.");
        }

        verifiedPackage.EnsureVerificationLockHeld();

        try
        {
            var directoryPaths = BuildDirectoryChain(verifiedPackage);
            foreach (var directoryPath in directoryPaths)
            {
                VerifyFilesystemObject(directoryPath, expectDirectory: true);
            }

            foreach (var component in verifiedPackage.Components)
            {
                VerifyFilesystemObject(component.FullPath, expectDirectory: false);
            }

            verifiedPackage.EnsureVerificationLockHeld();

            return new WindowsProtectedEngineSecurityVerification(
                verifiedPackage,
                directoryPaths.Count + verifiedPackage.Components.Count);
        }
        catch (WindowsEngineSecurityVerificationException)
        {
            throw;
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new WindowsEngineSecurityVerificationException(
                "AclReadAccessDenied",
                "Windows отказала в чтении ACL/owner managed engine package.",
                exception);
        }
        catch (IOException exception)
        {
            throw new WindowsEngineSecurityVerificationException(
                "AclReadIoError",
                "Не удалось безопасно прочитать ACL/owner managed engine package.",
                exception);
        }
        catch (SystemException exception)
        {
            throw new WindowsEngineSecurityVerificationException(
                "AclReadSystemError",
                "Windows не позволила однозначно проверить ACL/owner managed engine package.",
                exception);
        }
    }

    internal static void EvaluateSecurityDescriptor(FileSystemSecurity security)
    {
        ArgumentNullException.ThrowIfNull(security);

        var owner = security.GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier
            ?? throw new WindowsEngineSecurityVerificationException(
                "MissingSecurityOwner",
                "Filesystem object не имеет проверяемого Windows owner SID.");

        if (!IsPrivilegedPrincipal(owner))
        {
            throw new WindowsEngineSecurityVerificationException(
                "UntrustedSecurityOwner",
                "Managed engine filesystem object принадлежит непривилегированному Windows principal.");
        }

        var rawDescriptor = new RawSecurityDescriptor(
            security.GetSecurityDescriptorBinaryForm(),
            0);

        if (rawDescriptor.DiscretionaryAcl is null)
        {
            throw new WindowsEngineSecurityVerificationException(
                "NullDiscretionaryAcl",
                "Null DACL запрещён для managed engine filesystem object.");
        }

        if (!security.AreAccessRulesCanonical)
        {
            throw new WindowsEngineSecurityVerificationException(
                "NonCanonicalDiscretionaryAcl",
                "Неканонический DACL отклонён fail-closed.");
        }

        var rules = security.GetAccessRules(
            includeExplicit: true,
            includeInherited: true,
            targetType: typeof(SecurityIdentifier));

        foreach (FileSystemAccessRule rule in rules)
        {
            if (rule.AccessControlType != AccessControlType.Allow
                || (rule.FileSystemRights & MutationRights) == 0)
            {
                continue;
            }

            if (rule.IdentityReference is not SecurityIdentifier principal)
            {
                throw new WindowsEngineSecurityVerificationException(
                    "UnresolvedAclPrincipal",
                    "ACL содержит principal без однозначного SID.");
            }

            if (IsPrivilegedPrincipal(principal))
            {
                continue;
            }

            // CREATOR OWNER / OWNER RIGHTS могут представлять только уже проверенного
            // privileged owner. Остальные principals с mutation rights отклоняются,
            // включая inheritance-only ACE: unsafe write inheritance не допускается.
            if (IsOwnerAlias(principal) && IsPrivilegedPrincipal(owner))
            {
                continue;
            }

            throw new WindowsEngineSecurityVerificationException(
                "UntrustedMutationAcl",
                "ACL предоставляет write/delete/ACL mutation rights непривилегированному principal.");
        }
    }

    private static IReadOnlyList<string> BuildDirectoryChain(
        WindowsVerifiedEnginePackage verifiedPackage)
    {
        var programFiles = NormalizeDirectory(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
        if (string.IsNullOrWhiteSpace(programFiles))
        {
            throw new WindowsEngineSecurityVerificationException(
                "ProgramFilesUnavailable",
                "Не удалось определить системный Program Files для ACL verification.");
        }

        var managedRoot = NormalizeDirectory(
            Path.Combine(programFiles, "NetPilot", "Engines", "Managed"));
        var engineRoot = NormalizeDirectory(verifiedPackage.EngineRoot);

        EnsureInsideOrEqual(managedRoot, engineRoot, "EngineRootOutsideManagedRoot");

        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            programFiles
        };

        AddDirectoryChain(programFiles, engineRoot, result);

        foreach (var component in verifiedPackage.Components)
        {
            var componentPath = Path.GetFullPath(component.FullPath);
            EnsureStrictChild(engineRoot, componentPath, "ComponentOutsideEngineRoot");

            var parent = Path.GetDirectoryName(componentPath)
                ?? throw new WindowsEngineSecurityVerificationException(
                    "ComponentParentMissing",
                    "Не удалось определить parent directory immutable component.");
            AddDirectoryChain(engineRoot, NormalizeDirectory(parent), result);
        }

        return Array.AsReadOnly(
            result
                .OrderBy(static path => path.Length)
                .ThenBy(static path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray());
    }

    private static void AddDirectoryChain(
        string root,
        string target,
        ISet<string> output)
    {
        var normalizedRoot = NormalizeDirectory(root);
        var normalizedTarget = NormalizeDirectory(target);
        EnsureInsideOrEqual(normalizedRoot, normalizedTarget, "DirectoryChainOutsideRoot");

        output.Add(normalizedRoot);
        if (string.Equals(
                normalizedRoot,
                normalizedTarget,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var relative = Path.GetRelativePath(normalizedRoot, normalizedTarget);
        var current = normalizedRoot;
        foreach (var segment in relative.Split(Path.DirectorySeparatorChar))
        {
            if (string.IsNullOrEmpty(segment) || segment is "." or "..")
            {
                throw new WindowsEngineSecurityVerificationException(
                    "UnsafeDirectoryChain",
                    "Protected engine directory chain содержит неоднозначный segment.");
            }

            current = NormalizeDirectory(Path.Combine(current, segment));
            EnsureInsideOrEqual(normalizedRoot, current, "DirectoryChainOutsideRoot");
            output.Add(current);
        }
    }

    private static void VerifyFilesystemObject(string path, bool expectDirectory)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
        {
            throw new WindowsEngineSecurityVerificationException(
                "SecurityObjectMissing",
                "Filesystem object исчез до ACL verification.");
        }

        var attributes = File.GetAttributes(fullPath);
        var isDirectory = (attributes & FileAttributes.Directory) != 0;
        if (isDirectory != expectDirectory)
        {
            throw new WindowsEngineSecurityVerificationException(
                "SecurityObjectTypeMismatch",
                "Filesystem object имеет неожиданный тип во время ACL verification.");
        }

        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new WindowsEngineSecurityVerificationException(
                "UnsafeSecurityReparsePoint",
                "Reparse point в protected engine security chain запрещён.");
        }

        FileSystemSecurity security = expectDirectory
            ? FileSystemAclExtensions.GetAccessControl(
                new DirectoryInfo(fullPath),
                AccessControlSections.Access | AccessControlSections.Owner)
            : FileSystemAclExtensions.GetAccessControl(
                new FileInfo(fullPath),
                AccessControlSections.Access | AccessControlSections.Owner);

        EvaluateSecurityDescriptor(security);

        var afterAttributes = File.GetAttributes(fullPath);
        if (((afterAttributes & FileAttributes.Directory) != 0) != expectDirectory
            || (afterAttributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new WindowsEngineSecurityVerificationException(
                "SecurityObjectChangedDuringVerification",
                "Filesystem object изменился во время ACL verification.");
        }
    }

    private static bool IsPrivilegedPrincipal(SecurityIdentifier principal) =>
        principal.Value is LocalSystemSid
            or BuiltinAdministratorsSid
            or TrustedInstallerSid;

    private static bool IsOwnerAlias(SecurityIdentifier principal) =>
        principal.Value is CreatorOwnerSid or OwnerRightsSid;

    private static string NormalizeDirectory(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static void EnsureInsideOrEqual(
        string root,
        string candidate,
        string errorCode)
    {
        var normalizedRoot = NormalizeDirectory(root);
        var normalizedCandidate = NormalizeDirectory(candidate);

        if (!string.Equals(normalizedRoot, normalizedCandidate, StringComparison.OrdinalIgnoreCase)
            && !normalizedCandidate.StartsWith(
                normalizedRoot + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new WindowsEngineSecurityVerificationException(
                errorCode,
                "Filesystem path вышел за пределы ожидаемой protected boundary.");
        }
    }

    private static void EnsureStrictChild(
        string root,
        string candidate,
        string errorCode)
    {
        var normalizedRoot = NormalizeDirectory(root);
        var normalizedCandidate = Path.GetFullPath(candidate);

        if (string.Equals(normalizedRoot, normalizedCandidate, StringComparison.OrdinalIgnoreCase)
            || !normalizedCandidate.StartsWith(
                normalizedRoot + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new WindowsEngineSecurityVerificationException(
                errorCode,
                "Filesystem path не является strict child ожидаемой protected boundary.");
        }
    }
}
