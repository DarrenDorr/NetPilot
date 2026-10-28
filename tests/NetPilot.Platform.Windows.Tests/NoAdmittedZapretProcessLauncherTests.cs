using System.Diagnostics;
using NetPilot.Engine.Zapret.Windows;

namespace NetPilot.Platform.Windows.Tests;

public sealed class NoAdmittedZapretProcessLauncherTests
{
    [Fact]
    public void Launch_is_always_rejected_without_plan_processing()
    {
        var exception = Assert.Throws<ZapretProcessRunnerException>(
            () => NoAdmittedZapretProcessLauncher.Instance.Launch(null!));

        Assert.Equal("NoAdmittedEngine", exception.Code);
    }

    [Fact]
    public void Launcher_has_no_process_or_mutable_instance_state()
    {
        var type = typeof(NoAdmittedZapretProcessLauncher);
        var instanceFields = type.GetFields(
            System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.Public
            | System.Reflection.BindingFlags.NonPublic);

        Assert.Empty(instanceFields);
        Assert.DoesNotContain(
            type.GetConstructors(),
            static constructor => constructor.IsPublic);
        Assert.DoesNotContain(
            instanceFields,
            static field => typeof(Process).IsAssignableFrom(field.FieldType));
    }
}
