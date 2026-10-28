using NetPilot.Security;

namespace NetPilot.Core.Tests;

public sealed class UserResourceOwnershipTests
{
    [Fact]
    public void First_user_acquires_and_same_user_keeps_ownership()
    {
        var ownership = new UserResourceOwnership();

        Assert.True(ownership.TryAcquire("S-1-5-21-1000"));
        Assert.True(ownership.TryAcquire("S-1-5-21-1000"));
        Assert.True(ownership.IsOwnedBy("S-1-5-21-1000"));
    }

    [Fact]
    public void Different_user_cannot_take_existing_ownership()
    {
        var ownership = new UserResourceOwnership();
        Assert.True(ownership.TryAcquire("S-1-5-21-1000"));

        Assert.False(ownership.TryAcquire("S-1-5-21-2000"));
        Assert.False(ownership.IsOwnedBy("S-1-5-21-2000"));
        Assert.True(ownership.IsOwnedBy("S-1-5-21-1000"));
    }

    [Fact]
    public void Clear_releases_ownership()
    {
        var ownership = new UserResourceOwnership();
        Assert.True(ownership.TryAcquire("S-1-5-21-1000"));

        ownership.Clear();

        Assert.False(ownership.HasOwner);
        Assert.True(ownership.TryAcquire("S-1-5-21-2000"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_owner_identity_is_rejected(string sid)
    {
        var ownership = new UserResourceOwnership();
        Assert.Throws<ArgumentException>(() => ownership.TryAcquire(sid));
    }
}
