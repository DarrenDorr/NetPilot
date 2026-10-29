using NetPilot.Platform.Windows;

namespace NetPilot.Platform.Windows.Tests;

public sealed class WindowsUserProfileStoreTests
{
    private const string FirstSid = "S-1-5-21-1000-1000-1000-1001";
    private const string SecondSid = "S-1-5-21-1000-1000-1000-1002";

    [Fact]
    public void Different_sids_receive_separate_profile_roots()
    {
        using var fixture = new StoreFixture();

        var first = fixture.Store.EnsureProfile(FirstSid);
        var second = fixture.Store.EnsureProfile(SecondSid);

        Assert.Equal(FirstSid, first.UserSid);
        Assert.Equal(SecondSid, second.UserSid);
        Assert.NotEqual(first.ProfileRoot, second.ProfileRoot);
        Assert.True(Directory.Exists(first.ProfileRoot));
        Assert.True(Directory.Exists(second.ProfileRoot));
        Assert.StartsWith(fixture.UsersRoot, first.ProfileRoot, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith(fixture.UsersRoot, second.ProfileRoot, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Repeated_access_returns_same_sid_profile()
    {
        using var fixture = new StoreFixture();

        var first = fixture.Store.EnsureProfile(FirstSid);
        var again = fixture.Store.EnsureProfile(FirstSid);

        Assert.Equal(first, again);
    }

    [Theory]
    [InlineData("S-1-5-18")]
    [InlineData("S-1-5-19")]
    [InlineData("S-1-5-20")]
    [InlineData("S-1-5-7")]
    public void Service_and_anonymous_sids_are_rejected(string sid)
    {
        using var fixture = new StoreFixture();

        Assert.Throws<InvalidDataException>(() => fixture.Store.EnsureProfile(sid));
    }

    [Fact]
    public void Invalid_sid_is_rejected_before_profile_creation()
    {
        using var fixture = new StoreFixture();

        Assert.Throws<InvalidDataException>(() => fixture.Store.EnsureProfile("..\\escape"));
        Assert.Empty(Directory.Exists(fixture.UsersRoot)
            ? Directory.GetDirectories(fixture.UsersRoot)
            : Array.Empty<string>());
    }

    private sealed class StoreFixture : IDisposable
    {
        public StoreFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), $"NetPilot-Users-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Root);
            UsersRoot = Path.Combine(Root, "Users");
            Store = new WindowsUserProfileStore(Root);
        }

        public string Root { get; }
        public string UsersRoot { get; }
        public WindowsUserProfileStore Store { get; }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
