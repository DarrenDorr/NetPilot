namespace NetPilot.Security;

public sealed class UserResourceOwnership
{
    private readonly object _sync = new();
    private string? _ownerSid;

    public bool TryAcquire(string userSid)
    {
        ValidateSidValue(userSid);

        lock (_sync)
        {
            if (_ownerSid is null)
            {
                _ownerSid = userSid;
                return true;
            }

            return string.Equals(_ownerSid, userSid, StringComparison.Ordinal);
        }
    }

    public bool IsOwnedBy(string userSid)
    {
        ValidateSidValue(userSid);

        lock (_sync)
        {
            return _ownerSid is not null
                && string.Equals(_ownerSid, userSid, StringComparison.Ordinal);
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

    public void Clear()
    {
        lock (_sync)
        {
            _ownerSid = null;
        }
    }

    private static void ValidateSidValue(string userSid)
    {
        if (string.IsNullOrWhiteSpace(userSid) || userSid.Length > 184)
        {
            throw new ArgumentException("SID пользователя отсутствует или имеет недопустимый размер.", nameof(userSid));
        }
    }
}
