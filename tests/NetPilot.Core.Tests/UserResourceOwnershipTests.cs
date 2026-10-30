using NetPilot.Security;

namespace NetPilot.Core.Tests;

public sealed class UserResourceOwnershipTests
{
    [Fact]
    public void First_user_acquires_and_same_user_reenters_same_resource()
    {
        var ownership = new UserResourceOwnership();

        Assert.True(ownership.TryAcquire("S-1-5-21-1000", "zapret"));
        Assert.True(ownership.TryAcquire("S-1-5-21-1000", "zapret"));
        Assert.True(ownership.IsOwnedBy("S-1-5-21-1000", "zapret"));
        Assert.True(ownership.IsOwnedByUser("S-1-5-21-1000"));
        Assert.True(ownership.IsOwnedByResource("zapret"));
    }

    [Fact]
    public void Different_user_cannot_take_existing_resource()
    {
        var ownership = new UserResourceOwnership();
        Assert.True(ownership.TryAcquire("S-1-5-21-1000", "zapret"));

        Assert.False(ownership.TryAcquire("S-1-5-21-2000", "zapret"));
        Assert.False(ownership.IsOwnedBy("S-1-5-21-2000", "zapret"));
        Assert.True(ownership.IsOwnedBy("S-1-5-21-1000", "zapret"));
    }

    [Fact]
    public void Same_user_cannot_acquire_second_engine_while_first_is_owned()
    {
        var ownership = new UserResourceOwnership();
        Assert.True(ownership.TryAcquire("S-1-5-21-1000", "zapret"));

        Assert.False(ownership.TryAcquire("S-1-5-21-1000", "byedpi"));
        Assert.False(ownership.IsOwnedBy("S-1-5-21-1000", "byedpi"));
        Assert.True(ownership.IsOwnedBy("S-1-5-21-1000", "zapret"));
    }

    [Fact]
    public void Resource_scoped_clear_cannot_release_another_engine()
    {
        var ownership = new UserResourceOwnership();
        Assert.True(ownership.TryAcquire("S-1-5-21-1000", "zapret"));

        Assert.False(ownership.Clear("byedpi"));
        Assert.True(ownership.HasOwner);
        Assert.True(ownership.IsOwnedBy("S-1-5-21-1000", "zapret"));

        Assert.True(ownership.Clear("zapret"));
        Assert.False(ownership.HasOwner);
        Assert.True(ownership.TryAcquire("S-1-5-21-2000", "byedpi"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_owner_identity_is_rejected(string sid)
    {
        var ownership = new UserResourceOwnership();
        Assert.Throws<ArgumentException>(() => ownership.TryAcquire(sid, "zapret"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Zapret")]
    [InlineData("zapret--test")]
    [InlineData("zapret_test")]
    public void Invalid_resource_identity_is_rejected(string resourceId)
    {
        var ownership = new UserResourceOwnership();
        Assert.Throws<ArgumentException>(() =>
            ownership.TryAcquire("S-1-5-21-1000", resourceId));
    }
}
