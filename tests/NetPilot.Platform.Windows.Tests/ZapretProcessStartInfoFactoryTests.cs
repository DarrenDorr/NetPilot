using NetPilot.Engine.Zapret;
using NetPilot.Engine.Zapret.Windows;

namespace NetPilot.Platform.Windows.Tests;

public sealed class ZapretProcessStartInfoFactoryTests
{
    [Fact]
    public void Creates_non_shell_process_start_info_with_argument_list()
    {
        using var fixture = new WinwsFixture();
        var plan = new ZapretLaunchPlan(
            "ALT11",
            fixture.ExecutablePath,
            fixture.BinDirectory,
            ["--wf-tcp=80,443", "--dpi-desync=fake", "--new"]);

        var startInfo = ZapretProcessStartInfoFactory.Create(plan);

        Assert.Equal(fixture.ExecutablePath, startInfo.FileName);
        Assert.Equal(fixture.BinDirectory, startInfo.WorkingDirectory);
        Assert.False(startInfo.UseShellExecute);
        Assert.True(startInfo.CreateNoWindow);
        Assert.Equal(string.Empty, startInfo.Arguments);
        Assert.Equal(3, startInfo.ArgumentList.Count);
        Assert.Equal("--wf-tcp=80,443", startInfo.ArgumentList[0]);
        Assert.Equal("--dpi-desync=fake", startInfo.ArgumentList[1]);
        Assert.Equal("--new", startInfo.ArgumentList[2]);
    }

    [Fact]
    public void Rejects_executable_with_unexpected_name()
    {
        using var fixture = new WinwsFixture();
        var otherPath = Path.Combine(fixture.BinDirectory, "other.exe");
        File.Copy(fixture.ExecutablePath, otherPath);

        var plan = new ZapretLaunchPlan(
            "ALT11",
            otherPath,
            fixture.BinDirectory,
            ["--wf-tcp=443"]);

        Assert.Throws<InvalidOperationException>(() =>
            ZapretProcessStartInfoFactory.Create(plan));
    }

    [Fact]
    public void Rejects_working_directory_different_from_executable_directory()
    {
        using var fixture = new WinwsFixture();
        var otherDirectory = Directory.CreateDirectory(
            Path.Combine(fixture.Root, "other")).FullName;

        var plan = new ZapretLaunchPlan(
            "ALT11",
            fixture.ExecutablePath,
            otherDirectory,
            ["--wf-tcp=443"]);

        Assert.Throws<InvalidOperationException>(() =>
            ZapretProcessStartInfoFactory.Create(plan));
    }

    [Fact]
    public void Rejects_argument_with_control_character()
    {
        using var fixture = new WinwsFixture();
        var plan = new ZapretLaunchPlan(
            "ALT11",
            fixture.ExecutablePath,
            fixture.BinDirectory,
            ["--wf-tcp=443\n--new"]);

        Assert.Throws<InvalidOperationException>(() =>
            ZapretProcessStartInfoFactory.Create(plan));
    }

    [Fact]
    public void Rejects_non_option_argument()
    {
        using var fixture = new WinwsFixture();
        var plan = new ZapretLaunchPlan(
            "ALT11",
            fixture.ExecutablePath,
            fixture.BinDirectory,
            ["unexpected-value"]);

        Assert.Throws<InvalidOperationException>(() =>
            ZapretProcessStartInfoFactory.Create(plan));
    }

    private sealed class WinwsFixture : IDisposable
    {
        public WinwsFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), $"NetPilot-Winws-{Guid.NewGuid():N}");
            BinDirectory = Directory.CreateDirectory(Path.Combine(Root, "bin")).FullName;
            ExecutablePath = Path.Combine(BinDirectory, "winws.exe");
            File.WriteAllBytes(ExecutablePath, [0x4D, 0x5A]);
        }

        public string Root { get; }
        public string BinDirectory { get; }
        public string ExecutablePath { get; }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
