using System.Diagnostics;
using System.Reflection;
using NetPilot.Engine.Zapret.Windows;
using NetPilot.Platform.Windows;

namespace NetPilot.Platform.Windows.Tests;

public sealed class WindowsServiceOwnedEngineProcessControllerTests
{
    [Fact]
    public void No_admitted_launcher_rejects_before_plan_processing()
    {
        var exception = Assert.Throws<WindowsServiceOwnedEngineProcessException>(() =>
            NoAdmittedWindowsVerifiedEngineProcessLauncher.Instance.Launch(null!));

        Assert.Equal("NoAdmittedEngine", exception.Code);
    }

    [Fact]
    public void No_admitted_launcher_has_no_process_or_mutable_instance_state()
    {
        var type = typeof(NoAdmittedWindowsVerifiedEngineProcessLauncher);
        var fields = type.GetFields(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        Assert.Empty(fields);
        Assert.DoesNotContain(
            type.GetConstructors(),
            static constructor => constructor.IsPublic);
        Assert.DoesNotContain(
            fields,
            static field => typeof(Process).IsAssignableFrom(field.FieldType));
    }

    [Fact]
    public void Audited_system_launcher_has_verified_plan_overload_without_raw_strings()
    {
        var launch = typeof(SystemZapretProcessLauncher)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Single(method =>
                string.Equals(method.Name, nameof(SystemZapretProcessLauncher.Launch), StringComparison.Ordinal)
                && method.GetParameters().Length == 1
                && method.GetParameters()[0].ParameterType == typeof(WindowsCompiledEngineArgumentPlan));

        var parameter = Assert.Single(launch.GetParameters());
        Assert.Equal(typeof(WindowsCompiledEngineArgumentPlan), parameter.ParameterType);
        Assert.DoesNotContain(
            launch.GetParameters(),
            static candidate => candidate.ParameterType == typeof(string));
        Assert.True(typeof(IWindowsVerifiedEngineProcessLauncher)
            .IsAssignableFrom(typeof(SystemZapretProcessLauncher)));
    }

    [Fact]
    public void Controller_start_accepts_no_raw_path_cli_url_or_environment_input()
    {
        var start = typeof(WindowsByeDpiServiceOwnedProcessController)
            .GetMethod(
                nameof(WindowsByeDpiServiceOwnedProcessController.StartAsync),
                BindingFlags.Instance | BindingFlags.Public)
            ?? throw new InvalidOperationException("StartAsync method not found.");

        Assert.Collection(
            start.GetParameters(),
            first => Assert.Equal(typeof(WindowsCompiledEngineArgumentPlan), first.ParameterType),
            second => Assert.Equal(typeof(CancellationToken), second.ParameterType));
        Assert.DoesNotContain(
            start.GetParameters(),
            static candidate => candidate.ParameterType == typeof(string));
    }

    [Fact]
    public void Controller_exposes_only_bounded_lifecycle_methods()
    {
        var publicMethods = typeof(WindowsByeDpiServiceOwnedProcessController)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(static method => method.DeclaringType == typeof(WindowsByeDpiServiceOwnedProcessController))
            .Select(static method => method.Name)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[]
            {
                nameof(WindowsByeDpiServiceOwnedProcessController.DisposeAsync),
                nameof(WindowsByeDpiServiceOwnedProcessController.GetStatusAsync),
                nameof(WindowsByeDpiServiceOwnedProcessController.StartAsync),
                nameof(WindowsByeDpiServiceOwnedProcessController.StopAsync)
            }.OrderBy(static name => name, StringComparer.Ordinal),
            publicMethods);
    }
}
