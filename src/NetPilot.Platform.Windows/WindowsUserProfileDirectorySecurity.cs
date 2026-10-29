using System.Security.AccessControl;
using System.Security.Principal;

namespace NetPilot.Platform.Windows;

public interface IWindowsUserProfileDirectorySecurity
{
    void SecureUsersRoot(string path);
    void CreateOrSecureProfileRoot(string path);
}

/// <summary>
/// Применяет service-owned DACL к каталогу пользовательских профилей NetPilot.
/// Профили не читаются напрямую обычными пользователями: доступ идёт через
/// authenticated IPC к LocalSystem-службе.
/// </summary>
public sealed class WindowsUserProfileDirectorySecurity : IWindowsUserProfileDirectorySecurity
{
    private static readonly SecurityIdentifier LocalSystemSid =
        new(WellKnownSidType.LocalSystemSid, domainSid: null);

    private static readonly SecurityIdentifier AdministratorsSid =
        new(WellKnownSidType.BuiltinAdministratorsSid, domainSid: null);

    public void SecureUsersRoot(string path)
    {
        EnsureLocalSystem();
        EnsureExistingDirectory(path);
        ApplyServiceOnlyAcl(path);
    }

    public void CreateOrSecureProfileRoot(string path)
    {
        EnsureLocalSystem();

        if (Directory.Exists(path))
        {
            RejectReparsePoint(path);
            ApplyServiceOnlyAcl(path);
            return;
        }

        var directoryInfo = new DirectoryInfo(path);
        FileSystemAclExtensions.Create(directoryInfo, CreateServiceOnlyAcl());
        RejectReparsePoint(path);
        VerifyServiceOnlyAcl(path);
    }

    internal static DirectorySecurity CreateServiceOnlyAcl()
    {
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

        const FileSystemRights rights = FileSystemRights.FullControl;
        const InheritanceFlags inheritance =
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;

        security.AddAccessRule(new FileSystemAccessRule(
            LocalSystemSid,
            rights,
            inheritance,
            PropagationFlags.None,
            AccessControlType.Allow));

        security.AddAccessRule(new FileSystemAccessRule(
            AdministratorsSid,
            rights,
            inheritance,
            PropagationFlags.None,
            AccessControlType.Allow));

        return security;
    }

    private static void ApplyServiceOnlyAcl(string path)
    {
        RejectReparsePoint(path);
        var info = new DirectoryInfo(path);
        FileSystemAclExtensions.SetAccessControl(info, CreateServiceOnlyAcl());
        VerifyServiceOnlyAcl(path);
    }

    private static void VerifyServiceOnlyAcl(string path)
    {
        var security = FileSystemAclExtensions.GetAccessControl(
            new DirectoryInfo(path),
            AccessControlSections.Access);

        if (!security.AreAccessRulesProtected)
        {
            throw new UnauthorizedAccessException(
                "Каталог пользовательских профилей NetPilot сохранил наследуемый DACL.");
        }

        var rules = security.GetAccessRules(
                includeExplicit: true,
                includeInherited: true,
                targetType: typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>()
            .ToArray();

        if (rules.Any(rule =>
                rule.AccessControlType != AccessControlType.Allow
                || rule.IdentityReference is not SecurityIdentifier sid
                || (!sid.Equals(LocalSystemSid) && !sid.Equals(AdministratorsSid))))
        {
            throw new UnauthorizedAccessException(
                "Каталог пользовательских профилей NetPilot содержит недопустимую ACL-запись.");
        }

        if (!rules.Any(rule =>
                rule.IdentityReference is SecurityIdentifier sid
                && sid.Equals(LocalSystemSid)
                && rule.AccessControlType == AccessControlType.Allow)
            || !rules.Any(rule =>
                rule.IdentityReference is SecurityIdentifier sid
                && sid.Equals(AdministratorsSid)
                && rule.AccessControlType == AccessControlType.Allow))
        {
            throw new UnauthorizedAccessException(
                "Каталог пользовательских профилей NetPilot не содержит обязательные SYSTEM/Administrators ACL-записи.");
        }
    }

    private static void EnsureLocalSystem()
    {
        using var identity = WindowsIdentity.GetCurrent();
        if (!identity.IsSystem)
        {
            throw new UnauthorizedAccessException(
                "Защищать системный каталог пользовательских профилей NetPilot может только LocalSystem-служба.");
        }
    }

    private static void EnsureExistingDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            throw new DirectoryNotFoundException(
                $"Защищённый каталог пользовательских профилей NetPilot не найден: {path}");
        }

        RejectReparsePoint(path);
    }

    private static void RejectReparsePoint(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException(
                "Reparse point в пользовательском пространстве NetPilot запрещён.");
        }
    }
}
