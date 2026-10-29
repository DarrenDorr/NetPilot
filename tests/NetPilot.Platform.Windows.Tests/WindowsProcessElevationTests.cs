using NetPilot.Platform.Windows;

namespace NetPilot.Platform.Windows.Tests;

public sealed class WindowsProcessElevationTests
{
    [Theory]
    [InlineData(WindowsProcessElevationState.NotElevated, true)]
    [InlineData(WindowsProcessElevationState.Elevated, true)]
    [InlineData(WindowsProcessElevationState.Unknown, false)]
    public void Installer_enrollment_policy_accepts_detectable_interactive_token_state(
        WindowsProcessElevationState state,
        bool expected)
    {
        Assert.Equal(
            expected,
            WindowsInstallerEnrollmentPolicy.MayCompleteEnrollment(state));
    }

    [Fact]
    public void Current_windows_process_elevation_is_detectable()
    {
        Assert.True(OperatingSystem.IsWindows());
        Assert.NotEqual(
            WindowsProcessElevationState.Unknown,
            WindowsProcessElevation.GetCurrentState());
    }
}
