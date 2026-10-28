using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Principal;
using NetPilot.Platform.Windows;

namespace NetPilot.Platform.Windows.Tests;

public sealed class WindowsNamedPipePeerAuthenticatorTests
{
    [Fact]
    public void Client_policy_accepts_normal_interactive_user()
    {
        var result = WindowsPipeIdentityPolicy.EvaluateClient(
            processId: 4242,
            pipeSessionId: 3,
            processSessionId: 3,
            userSidValue: "S-1-5-21-1000-1000-1000-1001",
            isAnonymous: false,
            isGuest: false,
            impersonationLevel: TokenImpersonationLevel.Identification,
            isInteractive: true);

        Assert.True(result.IsAuthenticated);
        Assert.NotNull(result.Identity);
        Assert.Equal((uint)4242, result.Identity!.ProcessId);
        Assert.Equal((uint)3, result.Identity.SessionId);
    }

    [Fact]
    public void Client_policy_rejects_session_zero()
    {
        var result = WindowsPipeIdentityPolicy.EvaluateClient(
            4242,
            0,
            0,
            "S-1-5-21-1000-1000-1000-1001",
            false,
            false,
            TokenImpersonationLevel.Identification,
            true);

        Assert.False(result.IsAuthenticated);
        Assert.Equal("ClientNotInteractive", result.Code);
    }

    [Fact]
    public void Client_policy_rejects_session_mismatch()
    {
        var result = WindowsPipeIdentityPolicy.EvaluateClient(
            4242,
            3,
            4,
            "S-1-5-21-1000-1000-1000-1001",
            false,
            false,
            TokenImpersonationLevel.Identification,
            true);

        Assert.False(result.IsAuthenticated);
        Assert.Equal("ClientSessionMismatch", result.Code);
    }

    [Theory]
    [InlineData(WellKnownSidType.LocalSystemSid)]
    [InlineData(WellKnownSidType.LocalServiceSid)]
    [InlineData(WellKnownSidType.NetworkServiceSid)]
    public void Client_policy_rejects_service_accounts(WellKnownSidType sidType)
    {
        var sid = new SecurityIdentifier(sidType, null).Value;

        var result = WindowsPipeIdentityPolicy.EvaluateClient(
            4242,
            3,
            3,
            sid,
            false,
            false,
            TokenImpersonationLevel.Identification,
            true);

        Assert.False(result.IsAuthenticated);
        Assert.Equal("ServiceAccountNotAllowed", result.Code);
    }

    [Fact]
    public void Server_policy_requires_local_system_owner_and_session_zero()
    {
        var localSystem = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null).Value;
        var ordinaryUser = "S-1-5-21-1000-1000-1000-1001";

        var valid = WindowsPipeIdentityPolicy.EvaluateServer(555, 0, localSystem);
        var wrongSession = WindowsPipeIdentityPolicy.EvaluateServer(555, 2, localSystem);
        var wrongOwner = WindowsPipeIdentityPolicy.EvaluateServer(555, 0, ordinaryUser);

        Assert.True(valid.IsAuthenticated);
        Assert.False(wrongSession.IsAuthenticated);
        Assert.Equal("ServerNotServiceSession", wrongSession.Code);
        Assert.False(wrongOwner.IsAuthenticated);
        Assert.Equal("ServerOwnerMismatch", wrongOwner.Code);
    }

    [Fact]
    public async Task Server_reads_identity_from_connected_client_instead_of_json()
    {
        var pipeName = $"NetPilot.Test.{Guid.NewGuid():N}";
        await using var server = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);

        using var client = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous,
            TokenImpersonationLevel.Identification);

        var waitForClient = server.WaitForConnectionAsync();
        await client.ConnectAsync(2_000);
        await waitForClient;

        var authenticator = new WindowsNamedPipePeerAuthenticator();
        var result = authenticator.AuthenticateClient(server);

        using var currentIdentity = WindowsIdentity.GetCurrent();
        var interactiveSid = new SecurityIdentifier(WellKnownSidType.InteractiveSid, null).Value;
        var isInteractive = currentIdentity.Groups?.Any(group => group.Value == interactiveSid) == true;
        var isServiceAccount = currentIdentity.User?.IsWellKnown(WellKnownSidType.LocalSystemSid) == true
            || currentIdentity.User?.IsWellKnown(WellKnownSidType.LocalServiceSid) == true
            || currentIdentity.User?.IsWellKnown(WellKnownSidType.NetworkServiceSid) == true;
        var currentSessionId = (uint)Process.GetCurrentProcess().SessionId;

        var shouldAuthenticate = currentSessionId != 0
            && isInteractive
            && !currentIdentity.IsAnonymous
            && !currentIdentity.IsGuest
            && !isServiceAccount;

        Assert.Equal(shouldAuthenticate, result.IsAuthenticated);

        if (shouldAuthenticate)
        {
            Assert.NotNull(result.Identity);
            Assert.Equal((uint)Environment.ProcessId, result.Identity!.ProcessId);
            Assert.Equal(currentSessionId, result.Identity.SessionId);
            Assert.Equal(currentIdentity.User!.Value, result.Identity.UserSid);
        }
    }

    [Fact]
    public async Task Client_rejects_ordinary_user_owned_pipe_as_service()
    {
        using var currentIdentity = WindowsIdentity.GetCurrent();
        var currentSessionId = (uint)Process.GetCurrentProcess().SessionId;

        if (currentIdentity.IsSystem && currentSessionId == 0)
        {
            return;
        }

        var pipeName = $"NetPilot.FakeService.{Guid.NewGuid():N}";
        await using var server = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);

        using var client = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeAccessRights.ReadWrite | PipeAccessRights.ReadPermissions,
            PipeOptions.Asynchronous,
            TokenImpersonationLevel.Identification,
            HandleInheritability.None);

        var waitForClient = server.WaitForConnectionAsync();
        await client.ConnectAsync(2_000);
        await waitForClient;

        var authenticator = new WindowsNamedPipePeerAuthenticator();
        var result = authenticator.AuthenticateServer(client);

        Assert.False(result.IsAuthenticated);
        Assert.Contains(
            result.Code,
            new[] { "ServerNotServiceSession", "ServerOwnerMismatch" });
    }
}
