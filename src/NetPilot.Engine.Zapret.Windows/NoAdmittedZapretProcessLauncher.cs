namespace NetPilot.Engine.Zapret.Windows;

/// <summary>
/// Fail-closed launcher для состояния без admitted native engine.
/// Не содержит Process/System.Diagnostics и не способен создать процесс.
/// </summary>
public sealed class NoAdmittedZapretProcessLauncher : IZapretProcessLauncher
{
    public static NoAdmittedZapretProcessLauncher Instance { get; } = new();

    private NoAdmittedZapretProcessLauncher()
    {
    }

    public IZapretManagedProcess Launch(ZapretLaunchPlan plan) =>
        throw new ZapretProcessRunnerException(
            "NoAdmittedEngine",
            "Запуск native сетевого движка запрещён: ни один component set не имеет статуса ADMIT.");
}
