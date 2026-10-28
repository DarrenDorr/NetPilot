using NetPilot.Platform.Windows;

namespace NetPilot.Platform.Windows.Tests;

public sealed class WindowsAuthorizedUserStoreTests
{
    private const string FirstSid = "S-1-5-21-1000-1000-1000-1001";
    private const string SecondSid = "S-1-5-21-1000-1000-1000-1002";

    [Fact]
    public void Correct_nonce_and_session_bind_exact_authenticated_sid_once()
    {
        using var fixture = new StoreFixture(sessionId: 7);
        var nonce = fixture.Store.EnsureEnrollmentNonce();

        Assert.NotNull(nonce);
        var result = fixture.Store.CompleteEnrollment(nonce!, FirstSid, 7);

        Assert.True(result.Success);
        Assert.Equal(AuthorizedUserEnrollmentStatus.Enrolled, result.Status);
        Assert.True(fixture.Store.IsAuthorized(FirstSid));
        Assert.False(fixture.Store.IsAuthorized(SecondSid));
        Assert.False(File.Exists(Path.Combine(fixture.ConfigRoot, "enrollment.nonce")));
        Assert.False(File.Exists(Path.Combine(fixture.ConfigRoot, "enrollment.session")));
    }

    [Fact]
    public void Wrong_session_is_rejected_even_with_correct_nonce()
    {
        using var fixture = new StoreFixture(sessionId: 7);
        var nonce = fixture.Store.EnsureEnrollmentNonce();

        var result = fixture.Store.CompleteEnrollment(nonce!, FirstSid, 8);

        Assert.False(result.Success);
        Assert.Equal(AuthorizedUserEnrollmentStatus.InvalidSession, result.Status);
        Assert.False(fixture.Store.IsEnrolled);
    }

    [Fact]
    public void Wrong_nonce_is_rejected()
    {
        using var fixture = new StoreFixture(sessionId: 7);
        fixture.Store.EnsureEnrollmentNonce();

        var result = fixture.Store.CompleteEnrollment(
            new string('A', WindowsAuthorizedUserStore.EnrollmentNonceCharacters),
            FirstSid,
            7);

        Assert.False(result.Success);
        Assert.Equal(AuthorizedUserEnrollmentStatus.InvalidNonce, result.Status);
        Assert.False(fixture.Store.IsEnrolled);
    }

    [Fact]
    public void Second_sid_cannot_replace_existing_owner()
    {
        using var fixture = new StoreFixture(sessionId: 7);
        var nonce = fixture.Store.EnsureEnrollmentNonce();
        var first = fixture.Store.CompleteEnrollment(nonce!, FirstSid, 7);
        Assert.True(first.Success);

        var second = fixture.Store.CompleteEnrollment(
            new string('B', WindowsAuthorizedUserStore.EnrollmentNonceCharacters),
            SecondSid,
            7);

        Assert.False(second.Success);
        Assert.Equal("AuthorizedUserAlreadyBound", second.Code);
        Assert.True(fixture.Store.IsAuthorized(FirstSid));
        Assert.False(fixture.Store.IsAuthorized(SecondSid));
    }

    [Fact]
    public void Missing_protected_session_prevents_nonce_creation()
    {
        using var fixture = new StoreFixture(sessionId: null);

        Assert.Throws<InvalidDataException>(() => fixture.Store.EnsureEnrollmentNonce());
    }

    private sealed class StoreFixture : IDisposable
    {
        public StoreFixture(uint? sessionId)
        {
            Root = Path.Combine(Path.GetTempPath(), $"NetPilot-Auth-{Guid.NewGuid():N}");
            ConfigRoot = Path.Combine(Root, "Config");
            Directory.CreateDirectory(ConfigRoot);

            if (sessionId.HasValue)
            {
                File.WriteAllText(
                    Path.Combine(ConfigRoot, "enrollment.session"),
                    sessionId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            Store = new WindowsAuthorizedUserStore(Root);
        }

        public string Root { get; }
        public string ConfigRoot { get; }
        public WindowsAuthorizedUserStore Store { get; }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
