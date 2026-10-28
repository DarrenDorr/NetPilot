using System.Security.Cryptography;
using System.Security.Principal;

namespace NetPilot.Platform.Windows;

public enum AuthorizedUserEnrollmentStatus
{
    Enrolled,
    AlreadyEnrolled,
    InvalidNonce,
    EnrollmentUnavailable
}

public sealed record AuthorizedUserEnrollmentResult(
    AuthorizedUserEnrollmentStatus Status,
    string Code)
{
    public bool Success => Status is AuthorizedUserEnrollmentStatus.Enrolled
        or AuthorizedUserEnrollmentStatus.AlreadyEnrolled;
}

/// <summary>
/// Хранит единственный разрешённый пользовательский SID и одноразовый nonce регистрации.
/// Файлы располагаются только под защищённым системным корнем установки NetPilot.
/// </summary>
public sealed class WindowsAuthorizedUserStore
{
    public const int EnrollmentNonceBytes = 32;
    public const int EnrollmentNonceCharacters = EnrollmentNonceBytes * 2;

    private const string AuthorizedSidFileName = "authorized-user.sid";
    private const string EnrollmentNonceFileName = "enrollment.nonce";

    private readonly object _sync = new();
    private readonly string _installationRoot;
    private readonly string _configRoot;
    private readonly string _authorizedSidPath;
    private readonly string _enrollmentNoncePath;

    public WindowsAuthorizedUserStore(string installationRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installationRoot);

        _installationRoot = Path.GetFullPath(installationRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        _configRoot = Path.Combine(_installationRoot, "Config");
        _authorizedSidPath = Path.Combine(_configRoot, AuthorizedSidFileName);
        _enrollmentNoncePath = Path.Combine(_configRoot, EnrollmentNonceFileName);
    }

    public string? EnsureEnrollmentNonce()
    {
        lock (_sync)
        {
            EnsureProtectedConfigRoot();

            var authorizedSid = ReadAuthorizedSidCore();
            if (authorizedSid is not null)
            {
                TryDeleteNonceCore();
                return null;
            }

            if (File.Exists(_enrollmentNoncePath))
            {
                RejectReparsePoint(_enrollmentNoncePath);
                return ValidateNonce(File.ReadAllText(_enrollmentNoncePath).Trim());
            }

            var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(EnrollmentNonceBytes));
            var temporaryPath = Path.Combine(_configRoot, $".{EnrollmentNonceFileName}.{Guid.NewGuid():N}.tmp");

            try
            {
                using (var stream = new FileStream(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 4096,
                    FileOptions.WriteThrough))
                using (var writer = new StreamWriter(stream))
                {
                    writer.Write(nonce);
                    writer.Flush();
                    stream.Flush(flushToDisk: true);
                }

                File.Move(temporaryPath, _enrollmentNoncePath, overwrite: false);
            }
            finally
            {
                TryDeleteFile(temporaryPath);
            }

            RejectReparsePoint(_enrollmentNoncePath);
            return nonce;
        }
    }

    public bool IsAuthorized(string userSid)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userSid);

        lock (_sync)
        {
            EnsureProtectedConfigRoot();
            var authorizedSid = ReadAuthorizedSidCore();
            return authorizedSid is not null
                && string.Equals(authorizedSid, userSid, StringComparison.Ordinal);
        }
    }

    public bool IsEnrolled
    {
        get
        {
            lock (_sync)
            {
                EnsureProtectedConfigRoot();
                return ReadAuthorizedSidCore() is not null;
            }
        }
    }

    public AuthorizedUserEnrollmentResult CompleteEnrollment(
        string enrollmentNonce,
        string authenticatedUserSid)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(enrollmentNonce);
        ArgumentException.ThrowIfNullOrWhiteSpace(authenticatedUserSid);

        lock (_sync)
        {
            EnsureProtectedConfigRoot();

            var sid = ParseAllowedInteractiveUserSid(authenticatedUserSid);
            var existingSid = ReadAuthorizedSidCore();
            if (existingSid is not null)
            {
                return string.Equals(existingSid, sid.Value, StringComparison.Ordinal)
                    ? new AuthorizedUserEnrollmentResult(
                        AuthorizedUserEnrollmentStatus.AlreadyEnrolled,
                        "AlreadyEnrolled")
                    : new AuthorizedUserEnrollmentResult(
                        AuthorizedUserEnrollmentStatus.EnrollmentUnavailable,
                        "AuthorizedUserAlreadyBound");
            }

            if (!File.Exists(_enrollmentNoncePath))
            {
                return new AuthorizedUserEnrollmentResult(
                    AuthorizedUserEnrollmentStatus.EnrollmentUnavailable,
                    "EnrollmentNonceMissing");
            }

            RejectReparsePoint(_enrollmentNoncePath);
            var expectedNonce = ValidateNonce(File.ReadAllText(_enrollmentNoncePath).Trim());

            if (!FixedTimeNonceEquals(expectedNonce, enrollmentNonce))
            {
                return new AuthorizedUserEnrollmentResult(
                    AuthorizedUserEnrollmentStatus.InvalidNonce,
                    "EnrollmentNonceMismatch");
            }

            var temporaryPath = Path.Combine(_configRoot, $".{AuthorizedSidFileName}.{Guid.NewGuid():N}.tmp");
            try
            {
                using (var stream = new FileStream(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 4096,
                    FileOptions.WriteThrough))
                using (var writer = new StreamWriter(stream))
                {
                    writer.Write(sid.Value);
                    writer.Flush();
                    stream.Flush(flushToDisk: true);
                }

                File.Move(temporaryPath, _authorizedSidPath, overwrite: false);
                RejectReparsePoint(_authorizedSidPath);
                File.Delete(_enrollmentNoncePath);
            }
            finally
            {
                TryDeleteFile(temporaryPath);
            }

            return new AuthorizedUserEnrollmentResult(
                AuthorizedUserEnrollmentStatus.Enrolled,
                "Enrolled");
        }
    }

    private void EnsureProtectedConfigRoot()
    {
        if (!Directory.Exists(_installationRoot))
        {
            throw new DirectoryNotFoundException("Корневой каталог установки NetPilot не найден.");
        }

        RejectReparsePoint(_installationRoot);
        Directory.CreateDirectory(_configRoot);
        RejectReparsePoint(_configRoot);

        if (File.Exists(_authorizedSidPath))
        {
            RejectReparsePoint(_authorizedSidPath);
        }

        if (File.Exists(_enrollmentNoncePath))
        {
            RejectReparsePoint(_enrollmentNoncePath);
        }
    }

    private string? ReadAuthorizedSidCore()
    {
        if (!File.Exists(_authorizedSidPath))
        {
            return null;
        }

        RejectReparsePoint(_authorizedSidPath);
        var value = File.ReadAllText(_authorizedSidPath).Trim();
        return ParseAllowedInteractiveUserSid(value).Value;
    }

    private void TryDeleteNonceCore()
    {
        if (!File.Exists(_enrollmentNoncePath))
        {
            return;
        }

        RejectReparsePoint(_enrollmentNoncePath);
        File.Delete(_enrollmentNoncePath);
    }

    private static SecurityIdentifier ParseAllowedInteractiveUserSid(string value)
    {
        if (value.Length is < 3 or > 184)
        {
            throw new InvalidDataException("SID разрешённого пользователя имеет недопустимую длину.");
        }

        SecurityIdentifier sid;
        try
        {
            sid = new SecurityIdentifier(value);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException("SID разрешённого пользователя имеет недопустимый формат.", exception);
        }

        if (sid.IsWellKnown(WellKnownSidType.LocalSystemSid)
            || sid.IsWellKnown(WellKnownSidType.LocalServiceSid)
            || sid.IsWellKnown(WellKnownSidType.NetworkServiceSid)
            || sid.IsWellKnown(WellKnownSidType.AnonymousSid))
        {
            throw new InvalidDataException("Служебный SID не может быть назначен владельцем NetPilot.");
        }

        return sid;
    }

    private static string ValidateNonce(string value)
    {
        if (value.Length != EnrollmentNonceCharacters
            || value.Any(static character => !Uri.IsHexDigit(character)))
        {
            throw new InvalidDataException("Nonce регистрации NetPilot имеет недопустимый формат.");
        }

        return value.ToUpperInvariant();
    }

    private static bool FixedTimeNonceEquals(string expected, string provided)
    {
        try
        {
            var expectedBytes = Convert.FromHexString(ValidateNonce(expected));
            var providedBytes = Convert.FromHexString(ValidateNonce(provided));
            return CryptographicOperations.FixedTimeEquals(expectedBytes, providedBytes);
        }
        catch (InvalidDataException)
        {
            return false;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static void RejectReparsePoint(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("Reparse point в защищённой конфигурации NetPilot запрещён.");
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
