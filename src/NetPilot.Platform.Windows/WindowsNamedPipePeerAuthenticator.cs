using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace NetPilot.Platform.Windows;

public sealed record WindowsPipePeerIdentity(
    uint ProcessId,
    uint SessionId,
    string UserSid);

public sealed record WindowsPipeAuthenticationResult(
    bool IsAuthenticated,
    string Code,
    string Message,
    WindowsPipePeerIdentity? Identity = null)
{
    public static WindowsPipeAuthenticationResult Accepted(WindowsPipePeerIdentity identity) =>
        new(true, "Ok", "Участник IPC успешно проверен.", identity);

    public static WindowsPipeAuthenticationResult Rejected(string code, string message) =>
        new(false, code, message);
}

public static class WindowsPipeIdentityPolicy
{
    private static readonly SecurityIdentifier LocalSystemSid =
        new(WellKnownSidType.LocalSystemSid, null);

    private static readonly SecurityIdentifier LocalServiceSid =
        new(WellKnownSidType.LocalServiceSid, null);

    private static readonly SecurityIdentifier NetworkServiceSid =
        new(WellKnownSidType.NetworkServiceSid, null);

    public static WindowsPipeAuthenticationResult EvaluateClient(
        uint processId,
        uint pipeSessionId,
        uint processSessionId,
        string? userSidValue,
        bool isAnonymous,
        bool isGuest,
        TokenImpersonationLevel impersonationLevel,
        bool isInteractive)
    {
        if (processId == 0)
        {
            return WindowsPipeAuthenticationResult.Rejected(
                "ClientProcessUnknown",
                "Не удалось определить процесс IPC-клиента.");
        }

        if (pipeSessionId != processSessionId)
        {
            return WindowsPipeAuthenticationResult.Rejected(
                "ClientSessionMismatch",
                "Сеанс процесса не соответствует сеансу IPC-подключения.");
        }

        if (string.IsNullOrWhiteSpace(userSidValue))
        {
            return WindowsPipeAuthenticationResult.Rejected(
                "ClientSidMissing",
                "Не удалось определить SID IPC-клиента.");
        }

        if (isAnonymous || isGuest)
        {
            return WindowsPipeAuthenticationResult.Rejected(
                "ClientIdentityNotAllowed",
                "Анонимные и гостевые IPC-клиенты запрещены.");
        }

        if (impersonationLevel is TokenImpersonationLevel.None or TokenImpersonationLevel.Anonymous)
        {
            return WindowsPipeAuthenticationResult.Rejected(
                "ClientImpersonationTooWeak",
                "Уровень идентификации IPC-клиента недостаточен.");
        }

        if (pipeSessionId == 0 || !isInteractive)
        {
            return WindowsPipeAuthenticationResult.Rejected(
                "ClientNotInteractive",
                "IPC-клиент должен принадлежать интерактивной пользовательской сессии.");
        }

        SecurityIdentifier userSid;
        try
        {
            userSid = new SecurityIdentifier(userSidValue);
        }
        catch (ArgumentException)
        {
            return WindowsPipeAuthenticationResult.Rejected(
                "ClientSidInvalid",
                "SID IPC-клиента имеет недопустимый формат.");
        }

        if (userSid == LocalSystemSid || userSid == LocalServiceSid || userSid == NetworkServiceSid)
        {
            return WindowsPipeAuthenticationResult.Rejected(
                "ServiceAccountNotAllowed",
                "Системные служебные учётные записи не могут выступать пользовательским IPC-клиентом.");
        }

        return WindowsPipeAuthenticationResult.Accepted(
            new WindowsPipePeerIdentity(processId, pipeSessionId, userSid.Value));
    }

    public static WindowsPipeAuthenticationResult EvaluateServer(
        uint processId,
        uint sessionId,
        string? ownerSidValue)
    {
        if (processId == 0)
        {
            return WindowsPipeAuthenticationResult.Rejected(
                "ServerProcessUnknown",
                "Не удалось определить процесс IPC-сервера.");
        }

        if (sessionId != 0)
        {
            return WindowsPipeAuthenticationResult.Rejected(
                "ServerNotServiceSession",
                "IPC-сервер не работает в системной сессии Windows.");
        }

        if (string.IsNullOrWhiteSpace(ownerSidValue))
        {
            return WindowsPipeAuthenticationResult.Rejected(
                "ServerOwnerMissing",
                "Не удалось определить владельца ACL IPC-сервера.");
        }

        SecurityIdentifier ownerSid;
        try
        {
            ownerSid = new SecurityIdentifier(ownerSidValue);
        }
        catch (ArgumentException)
        {
            return WindowsPipeAuthenticationResult.Rejected(
                "ServerOwnerInvalid",
                "SID владельца IPC-сервера имеет недопустимый формат.");
        }

        if (ownerSid != LocalSystemSid)
        {
            return WindowsPipeAuthenticationResult.Rejected(
                "ServerOwnerMismatch",
                "Владельцем IPC-канала не является LocalSystem.");
        }

        return WindowsPipeAuthenticationResult.Accepted(
            new WindowsPipePeerIdentity(processId, sessionId, LocalSystemSid.Value));
    }
}

public sealed class WindowsNamedPipePeerAuthenticator
{
    private static readonly SecurityIdentifier InteractiveSid =
        new(WellKnownSidType.InteractiveSid, null);

    public WindowsPipeAuthenticationResult AuthenticateClient(NamedPipeServerStream pipe)
    {
        ArgumentNullException.ThrowIfNull(pipe);

        if (!OperatingSystem.IsWindows())
        {
            return WindowsPipeAuthenticationResult.Rejected(
                "UnsupportedPlatform",
                "Проверка Windows IPC доступна только в Windows.");
        }

        if (!pipe.IsConnected)
        {
            return WindowsPipeAuthenticationResult.Rejected(
                "PipeNotConnected",
                "IPC-канал не подключён.");
        }

        if (!NativeMethods.GetNamedPipeClientProcessId(pipe.SafePipeHandle, out var processId))
        {
            return WindowsPipeAuthenticationResult.Rejected(
                "ClientProcessUnknown",
                "Не удалось определить процесс IPC-клиента.");
        }

        if (!NativeMethods.GetNamedPipeClientSessionId(pipe.SafePipeHandle, out var pipeSessionId))
        {
            return WindowsPipeAuthenticationResult.Rejected(
                "ClientSessionUnknown",
                "Не удалось определить сеанс IPC-клиента.");
        }

        if (!NativeMethods.ProcessIdToSessionId(processId, out var processSessionId))
        {
            return WindowsPipeAuthenticationResult.Rejected(
                "ClientProcessSessionUnknown",
                "Не удалось проверить сеанс процесса IPC-клиента.");
        }

        ClientTokenSnapshot? token = null;
        try
        {
            pipe.RunAsClient(() =>
            {
                using var identity = WindowsIdentity.GetCurrent(TokenAccessLevels.Query);
                token = new ClientTokenSnapshot(
                    identity.User?.Value,
                    identity.IsAnonymous,
                    identity.IsGuest,
                    identity.ImpersonationLevel,
                    HasGroup(identity, InteractiveSid));
            });
        }
        catch (UnauthorizedAccessException)
        {
            return WindowsPipeAuthenticationResult.Rejected(
                "ClientTokenDenied",
                "Не удалось проверить токен IPC-клиента.");
        }
        catch (InvalidOperationException)
        {
            return WindowsPipeAuthenticationResult.Rejected(
                "ClientTokenUnavailable",
                "Токен IPC-клиента недоступен.");
        }

        if (token is null)
        {
            return WindowsPipeAuthenticationResult.Rejected(
                "ClientTokenUnavailable",
                "Токен IPC-клиента недоступен.");
        }

        return WindowsPipeIdentityPolicy.EvaluateClient(
            processId,
            pipeSessionId,
            processSessionId,
            token.UserSid,
            token.IsAnonymous,
            token.IsGuest,
            token.ImpersonationLevel,
            token.IsInteractive);
    }

    public WindowsPipeAuthenticationResult AuthenticateServer(NamedPipeClientStream pipe)
    {
        ArgumentNullException.ThrowIfNull(pipe);

        if (!OperatingSystem.IsWindows())
        {
            return WindowsPipeAuthenticationResult.Rejected(
                "UnsupportedPlatform",
                "Проверка Windows IPC доступна только в Windows.");
        }

        if (!pipe.IsConnected)
        {
            return WindowsPipeAuthenticationResult.Rejected(
                "PipeNotConnected",
                "IPC-канал не подключён.");
        }

        if (!NativeMethods.GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var processId))
        {
            return WindowsPipeAuthenticationResult.Rejected(
                "ServerProcessUnknown",
                "Не удалось определить процесс IPC-сервера.");
        }

        if (!NativeMethods.GetNamedPipeServerSessionId(pipe.SafePipeHandle, out var sessionId))
        {
            return WindowsPipeAuthenticationResult.Rejected(
                "ServerSessionUnknown",
                "Не удалось определить сеанс IPC-сервера.");
        }

        string? ownerSidValue;
        try
        {
            var security = pipe.GetAccessControl();
            var owner = security.GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
            ownerSidValue = owner?.Value;
        }
        catch (UnauthorizedAccessException)
        {
            return WindowsPipeAuthenticationResult.Rejected(
                "ServerAclUnreadable",
                "Не удалось проверить владельца ACL IPC-сервера.");
        }
        catch (InvalidOperationException)
        {
            return WindowsPipeAuthenticationResult.Rejected(
                "ServerAclUnavailable",
                "ACL IPC-сервера недоступен.");
        }

        return WindowsPipeIdentityPolicy.EvaluateServer(processId, sessionId, ownerSidValue);
    }

    private static bool HasGroup(WindowsIdentity identity, SecurityIdentifier groupSid)
    {
        if (identity.Groups is null)
        {
            return false;
        }

        foreach (var group in identity.Groups)
        {
            if (string.Equals(group.Value, groupSid.Value, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private sealed record ClientTokenSnapshot(
        string? UserSid,
        bool IsAnonymous,
        bool IsGuest,
        TokenImpersonationLevel ImpersonationLevel,
        bool IsInteractive);

    private static class NativeMethods
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetNamedPipeClientProcessId(
            SafePipeHandle pipe,
            out uint clientProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetNamedPipeClientSessionId(
            SafePipeHandle pipe,
            out uint clientSessionId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetNamedPipeServerProcessId(
            SafePipeHandle pipe,
            out uint serverProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetNamedPipeServerSessionId(
            SafePipeHandle pipe,
            out uint serverSessionId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ProcessIdToSessionId(
            uint processId,
            out uint sessionId);
    }
}
