namespace NetPilot.Backup;

public sealed record BackupManifest(
    int FormatVersion,
    string NetPilotVersion,
    DateTimeOffset CreatedAt,
    bool ContainsSecrets,
    IReadOnlyList<string> Sections);
