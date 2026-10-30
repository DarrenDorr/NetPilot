namespace NetPilot.Security;

/// <summary>
/// Global machine engine lease. Ownership is bound to both an authenticated Windows SID
/// and one canonical resource id (for example "zapret" or "byedpi"). The same SID may
/// re-enter the same resource, but cannot silently acquire a different engine while one
/// is active. Resource-scoped release prevents one stopped backend from clearing another
/// backend's live lease.
/// </summary>
public sealed class UserResourceOwnership
{
    private readonly object _sync = new();
    private string? _ownerSid;
    private string? _resourceId;

    public bool TryAcquire(string userSid, string resourceId)
    {
        ValidateSidValue(userSid);
        ValidateResourceId(resourceId);

        lock (_sync)
        {
            if (_ownerSid is null)
            {
                _ownerSid = userSid;
                _resourceId = resourceId;
                return true;
            }

            return string.Equals(_ownerSid, userSid, StringComparison.Ordinal)
                && string.Equals(_resourceId, resourceId, StringComparison.Ordinal);
        }
    }

    public bool IsOwnedBy(string userSid, string resourceId)
    {
        ValidateSidValue(userSid);
        ValidateResourceId(resourceId);

        lock (_sync)
        {
            return _ownerSid is not null
                && string.Equals(_ownerSid, userSid, StringComparison.Ordinal)
                && string.Equals(_resourceId, resourceId, StringComparison.Ordinal);
        }
    }

    public bool IsOwnedByUser(string userSid)
    {
        ValidateSidValue(userSid);

        lock (_sync)
        {
            return _ownerSid is not null
                && string.Equals(_ownerSid, userSid, StringComparison.Ordinal);
        }
    }

    public bool IsOwnedByResource(string resourceId)
    {
        ValidateResourceId(resourceId);

        lock (_sync)
        {
            return _ownerSid is not null
                && string.Equals(_resourceId, resourceId, StringComparison.Ordinal);
        }
    }

    public bool HasOwner
    {
        get
        {
            lock (_sync)
            {
                return _ownerSid is not null;
            }
        }
    }

    /// <summary>
    /// Releases only the specified engine resource. A stopped ByeDPI session therefore
    /// cannot clear a concurrently-owned Zapret lease, and vice versa.
    /// </summary>
    public bool Clear(string resourceId)
    {
        ValidateResourceId(resourceId);

        lock (_sync)
        {
            if (_ownerSid is null
                || !string.Equals(_resourceId, resourceId, StringComparison.Ordinal))
            {
                return false;
            }

            _ownerSid = null;
            _resourceId = null;
            return true;
        }
    }

    private static void ValidateSidValue(string userSid)
    {
        if (string.IsNullOrWhiteSpace(userSid) || userSid.Length > 184)
        {
            throw new ArgumentException(
                "SID пользователя отсутствует или имеет недопустимый размер.",
                nameof(userSid));
        }
    }

    private static void ValidateResourceId(string resourceId)
    {
        if (string.IsNullOrEmpty(resourceId)
            || resourceId.Length > 32
            || !char.IsAsciiLetterLower(resourceId[0])
            || !char.IsAsciiLetterOrDigit(resourceId[^1]))
        {
            throw new ArgumentException(
                "Resource id должен быть каноническим lower-ASCII token.",
                nameof(resourceId));
        }

        var previousHyphen = false;
        foreach (var character in resourceId)
        {
            if (character is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                previousHyphen = false;
                continue;
            }

            if (character == '-' && !previousHyphen)
            {
                previousHyphen = true;
                continue;
            }

            throw new ArgumentException(
                "Resource id должен быть каноническим lower-ASCII token.",
                nameof(resourceId));
        }
    }
}
