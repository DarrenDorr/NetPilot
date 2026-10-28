namespace NetPilot.App.Services;

public sealed class ApplicationSupportInfo
{
    private const int MaxValueLength = 128;
    public const string UnknownValue = "неизвестно";

    public ApplicationSupportInfo(
        string? version,
        string? platform,
        string? architecture)
    {
        Version = Normalize(version);
        Platform = Normalize(platform);
        Architecture = Normalize(architecture);
    }

    public string Version { get; }
    public string Platform { get; }
    public string Architecture { get; }

    public static ApplicationSupportInfo Unknown { get; } =
        new(UnknownValue, UnknownValue, UnknownValue);

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return UnknownValue;
        }

        var normalized = new string(
            value
                .Select(static character => char.IsControl(character) ? ' ' : character)
                .ToArray())
            .Trim();

        if (normalized.Length == 0)
        {
            return UnknownValue;
        }

        return normalized.Length <= MaxValueLength
            ? normalized
            : normalized[..MaxValueLength];
    }
}
