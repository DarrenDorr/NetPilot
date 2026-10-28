using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using NetPilot.Ipc;

namespace NetPilot.Platform.Windows;

public sealed class WindowsNamedPipeServerFactory
{
    public NamedPipeServerStream Create()
    {
        var security = new PipeSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.SetOwner(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null));

        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.NetworkSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Deny));

        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Allow));

        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Allow));

        // IPC предназначен только для процессов реальной интерактивной сессии Windows.
        // Authenticated Users здесь намеренно не используется: сервисные, пакетные и
        // прочие неинтерактивные учётные записи не должны даже открывать канал.
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.InteractiveSid, null),
            PipeAccessRights.ReadWrite | PipeAccessRights.ReadPermissions,
            AccessControlType.Allow));

        return NamedPipeServerStreamAcl.Create(
            IpcProtocol.WindowsPipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 4,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.WriteThrough,
            inBufferSize: IpcProtocol.MaxMessageBytes,
            outBufferSize: IpcProtocol.MaxMessageBytes,
            security);
    }
}
