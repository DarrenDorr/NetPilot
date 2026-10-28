namespace NetPilot.Updater;

public interface IZapretUpdateRuntimeGate
{
    Task EnsureStoppedForUpdateAsync(
        CancellationToken cancellationToken = default);
}
