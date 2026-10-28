using System.Runtime.InteropServices;
using System.Security;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace NetPilot.Platform.Windows;

public enum WindowsProcessElevationState
{
    Unknown,
    NotElevated,
    Elevated
}

public static class WindowsInstallerEnrollmentPolicy
{
    public static bool MayCompleteEnrollment(WindowsProcessElevationState elevationState) =>
        elevationState == WindowsProcessElevationState.NotElevated;
}

public static class WindowsProcessElevation
{
    public static WindowsProcessElevationState GetCurrentState()
    {
        if (!OperatingSystem.IsWindows())
        {
            return WindowsProcessElevationState.Unknown;
        }

        try
        {
            using var identity = WindowsIdentity.GetCurrent(TokenAccessLevels.Query);
            var token = identity.AccessToken;
            if (token.IsInvalid || token.IsClosed)
            {
                return WindowsProcessElevationState.Unknown;
            }

            var elevation = new TokenElevation();
            var expectedSize = Marshal.SizeOf<TokenElevation>();
            if (!NativeMethods.GetTokenInformation(
                    token,
                    TokenInformationClass.TokenElevation,
                    ref elevation,
                    expectedSize,
                    out var returnedSize)
                || returnedSize < expectedSize)
            {
                return WindowsProcessElevationState.Unknown;
            }

            return elevation.TokenIsElevated != 0
                ? WindowsProcessElevationState.Elevated
                : WindowsProcessElevationState.NotElevated;
        }
        catch (UnauthorizedAccessException)
        {
            return WindowsProcessElevationState.Unknown;
        }
        catch (InvalidOperationException)
        {
            return WindowsProcessElevationState.Unknown;
        }
        catch (SecurityException)
        {
            return WindowsProcessElevationState.Unknown;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TokenElevation
    {
        public int TokenIsElevated;
    }

    private enum TokenInformationClass
    {
        TokenElevation = 20
    }

    private static class NativeMethods
    {
        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetTokenInformation(
            SafeAccessTokenHandle tokenHandle,
            TokenInformationClass tokenInformationClass,
            ref TokenElevation tokenInformation,
            int tokenInformationLength,
            out int returnLength);
    }
}
