using System.Security.Principal;

namespace NetPilot.Platform.Windows;

public sealed record WindowsUserProfile(
    string UserSid,
    string ProfileRoot);

/// <summary>
/// Создаёт отдельное service-owned пространство данных для каждого проверенного
/// интерактивного Windows SID. Имя учётной записи не используется как идентификатор.
/// </summary>
public sealed class WindowsUserProfileStore
{
    private readonly object _sync = new();
    private readonly string _installationRoot;
    private readonly string _usersRoot;
    private readonly IWindowsUserProfileDirectorySecurity _directorySecurity;

    public WindowsUserProfileStore(
        string installationRoot,
        IWindowsUserProfileDirectorySecurity directorySecurity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installationRoot);
        ArgumentNullException.ThrowIfNull(directorySecurity);

        _installationRoot = Path.GetFullPath(installationRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        _usersRoot = Path.Combine(_installationRoot, "Users");
        _directorySecurity = directorySecurity;
    }

    public void EnsureRoot()
    {
        lock (_sync)
        {
            EnsureExistingNonReparseDirectory(_installationRoot);
            EnsureExistingNonReparseDirectory(_usersRoot);
            _directorySecurity.SecureUsersRoot(_usersRoot);
        }
    }

    public WindowsUserProfile EnsureProfile(string userSid)
    {
        var sid = ParseAllowedInteractiveUserSid(userSid);

        lock (_sync)
        {
            EnsureExistingNonReparseDirectory(_installationRoot);
            EnsureExistingNonReparseDirectory(_usersRoot);
            _directorySecurity.SecureUsersRoot(_usersRoot);

            var profileRoot = Path.Combine(_usersRoot, sid.Value);
            EnsureStrictChild(_usersRoot, profileRoot);
            _directorySecurity.CreateOrSecureProfileRoot(profileRoot);
            EnsureExistingNonReparseDirectory(profileRoot);

            return new WindowsUserProfile(sid.Value, profileRoot);
        }
    }

    private static SecurityIdentifier ParseAllowedInteractiveUserSid(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 184)
        {
            throw new InvalidDataException("SID пользовательского профиля отсутствует или имеет недопустимую длину.");
        }

        SecurityIdentifier sid;
        try
        {
            sid = new SecurityIdentifier(value);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException("SID пользовательского профиля имеет недопустимый формат.", exception);
        }

        if (sid.IsWellKnown(WellKnownSidType.LocalSystemSid)
            || sid.IsWellKnown(WellKnownSidType.LocalServiceSid)
            || sid.IsWellKnown(WellKnownSidType.NetworkServiceSid)
            || sid.IsWellKnown(WellKnownSidType.AnonymousSid))
        {
            throw new InvalidDataException("Служебный SID не может иметь пользовательский профиль NetPilot.");
        }

        return sid;
    }

    private static void EnsureExistingNonReparseDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            throw new DirectoryNotFoundException($"Защищённый каталог NetPilot не найден: {path}");
        }

        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("Reparse point в пользовательском пространстве NetPilot запрещён.");
        }
    }

    private static void EnsureStrictChild(string parent, string child)
    {
        var normalizedParent = Path.GetFullPath(parent)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var normalizedChild = Path.GetFullPath(child);

        if (!normalizedChild.StartsWith(normalizedParent, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Путь пользовательского профиля вышел за защищённый корень NetPilot.");
        }
    }
}
