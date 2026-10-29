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
        Assert.True(first.ProfileRoot.StartsWith(fixture.UsersRoot, StringComparison.OrdinalIgnoreCase));
        Assert.True(second.ProfileRoot.StartsWith(fixture.UsersRoot, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Repeated_access_returns_same_sid_profile()
    {
        using var fixture = new StoreFixture();

        var first = fixture.Store.EnsureProfile(FirstSid);
        var again = fixture.Store.EnsureProfile(FirstSid);

        Assert.Equal(first, again);
    }

    [Fact]
    public void Users_root_is_secured_before_profile_creation()
    {
        using var fixture = new StoreFixture();

        fixture.Store.EnsureRoot();

        Assert.Equal(1, fixture.Security.UsersRootSecureCalls);
        Assert.Equal(fixture.UsersRoot, fixture.Security.LastUsersRoot);
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
        Assert.Empty(Directory.GetDirectories(fixture.UsersRoot));
    }

    private sealed class StoreFixture : IDisposable
    {
        public StoreFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), $"NetPilot-Users-{Guid.NewGuid():N}");
            UsersRoot = Path.Combine(Root, "Users");
            Directory.CreateDirectory(UsersRoot);
            Security = new TestDirectorySecurity();
            Store = new WindowsUserProfileStore(Root, Security);
        }

        public string Root { get; }
        public string UsersRoot { get; }
        public TestDirectorySecurity Security { get; }
        public WindowsUserProfileStore Store { get; }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }

    private sealed class TestDirectorySecurity : IWindowsUserProfileDirectorySecurity
    {
        public int UsersRootSecureCalls { get; private set; }
        public string? LastUsersRoot { get; private set; }

        public void SecureUsersRoot(string path)
        {
            UsersRootSecureCalls++;
            LastUsersRoot = path;
        }

        public void CreateOrSecureProfileRoot(string path)
        {
            Directory.CreateDirectory(path);
        }
    }
}
