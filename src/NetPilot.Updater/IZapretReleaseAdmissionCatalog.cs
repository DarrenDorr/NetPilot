namespace NetPilot.Updater;

/// <summary>
/// Локальный доверенный источник identity релиза, разрешённого к установке.
/// Сетевая metadata не может сама сделать release доверенным.
/// </summary>
public interface IZapretReleaseAdmissionCatalog
{
    GithubZapretRelease? GetAdmittedRelease();
}

/// <summary>
/// Безопасное значение по умолчанию: ни один legacy Zapret/Flowseal release
/// не допущен к установке.
/// </summary>
public sealed class NoAdmittedZapretReleaseCatalog : IZapretReleaseAdmissionCatalog
{
    private NoAdmittedZapretReleaseCatalog()
    {
    }

    public static NoAdmittedZapretReleaseCatalog Instance { get; } = new();

    public GithubZapretRelease? GetAdmittedRelease() => null;
}
