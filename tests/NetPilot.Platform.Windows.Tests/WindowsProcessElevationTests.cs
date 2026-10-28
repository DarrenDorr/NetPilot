using NetPilot.Platform.Windows;

namespace NetPilot.Platform.Windows.Tests;

public sealed class WindowsProcessElevationTests
{
    [Theory]
    [InlineData(WindowsProcessElevationState.NotElevated, true)]
    [InlineData(WindowsProcessElevationState.Elevated, false)]
    [InlineData(WindowsProcessElevationState.Unknown, false)]
    public void Installer_enrollment_policy_is_fail_closed(
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
