using System.Globalization;
using System.Security.Cryptography;
using System.Security.Principal;

namespace NetPilot.Platform.Windows;

public enum AuthorizedUserEnrollmentStatus
{
    Enrolled,
    AlreadyEnrolled,
    InvalidNonce,
    InvalidSession,
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
/// Хранит единственный разрешённый пользовательский SID и одноразовые данные регистрации.
/// Файлы располагаются только под защищённым системным корнем установки NetPilot.
/// </summary>
public sealed class WindowsAuthorizedUserStore
{
    public const int EnrollmentNonceBytes = 32;
    public const int EnrollmentNonceCharacters = EnrollmentNonceBytes * 2;

    private const string AuthorizedSidFileName = "authorized-user.sid";
    private const string EnrollmentNonceFileName = "enrollment.nonce";
    private const string EnrollmentSessionFileName = "enrollment.session";

    private readonly object _sync = new();
    private readonly string _installationRoot;
    private readonly string _configRoot;
    private readonly string _authorizedSidPath;
    private readonly string _enrollmentNoncePath;
    private readonly string _enrollmentSessionPath;

    public WindowsAuthorizedUserStore(string installationRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installationRoot);

        _installationRoot = Path.GetFullPath(installationRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        _configRoot = Path.Combine(_installationRoot, "Config");
        _authorizedSidPath = Path.Combine(_configRoot, AuthorizedSidFileName);
        _enrollmentNoncePath = Path.Combine(_configRoot, EnrollmentNonceFileName);
        _enrollmentSessionPath = Path.Combine(_configRoot, EnrollmentSessionFileName);
    }

    public string? EnsureEnrollmentNonce()
    {
        lock (_sync)
        {
            EnsureProtectedConfigRoot();

            var authorizedSid = ReadAuthorizedSidCore();
            if (authorizedSid is not null)
            {
                TryDeleteEnrollmentArtifactsCore();
                return null;
            }

            // Session ID создаёт только повышенный установщик до запуска службы.
            // Без него служба не открывает окно первичной регистрации.
            ReadExpectedEnrollmentSessionCore();

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
        string authenticatedUserSid,
        uint authenticatedSessionId)
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

            uint expectedSessionId;
            try
            {
                expectedSessionId = ReadExpectedEnrollmentSessionCore();
            }
            catch (InvalidDataException)
            {
                return new AuthorizedUserEnrollmentResult(
                    AuthorizedUserEnrollmentStatus.EnrollmentUnavailable,
                    "EnrollmentSessionMissingOrInvalid");
            }

            if (authenticatedSessionId == 0 || authenticatedSessionId != expectedSessionId)
            {
                return new AuthorizedUserEnrollmentResult(
                    AuthorizedUserEnrollmentStatus.InvalidSession,
                    "EnrollmentSessionMismatch");
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
                TryDeleteEnrollmentArtifactsCore();
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

        foreach (var path in new[]
                 {
                     _authorizedSidPath,
                     _enrollmentNoncePath,
                     _enrollmentSessionPath
                 })
        {
            if (File.Exists(path))
            {
                RejectReparsePoint(path);
            }
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

    private uint ReadExpectedEnrollmentSessionCore()
    {
        if (!File.Exists(_enrollmentSessionPath))
        {
            throw new InvalidDataException("Session ID первичной регистрации NetPilot отсутствует.");
        }

        RejectReparsePoint(_enrollmentSessionPath);
        var value = File.ReadAllText(_enrollmentSessionPath).Trim();
        if (!uint.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var sessionId)
            || sessionId == 0)
        {
            throw new InvalidDataException("Session ID первичной регистрации NetPilot имеет недопустимый формат.");
        }

        return sessionId;
    }

    private void TryDeleteEnrollmentArtifactsCore()
    {
        foreach (var path in new[] { _enrollmentNoncePath, _enrollmentSessionPath })
        {
            if (!File.Exists(path))
            {
                continue;
            }

            RejectReparsePoint(path);
            File.Delete(path);
        }
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
