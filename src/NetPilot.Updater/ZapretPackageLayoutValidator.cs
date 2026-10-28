namespace NetPilot.Updater;

public static class ZapretPackageLayoutValidator
{
    public static string ResolvePackageRoot(string stagingRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stagingRoot);
        var staging = Path.GetFullPath(stagingRoot);

        if (!Directory.Exists(staging))
        {
            throw new UpdateSecurityException("StagingMissing", "Каталог staging не существует.");
        }

        RejectReparsePointsRecursively(staging);

        var candidates = new List<string>();
        if (HasExpectedLayout(staging))
        {
            candidates.Add(staging);
        }

        foreach (var directory in Directory.EnumerateDirectories(staging, "*", SearchOption.TopDirectoryOnly))
        {
            if (HasExpectedLayout(directory))
            {
                candidates.Add(Path.GetFullPath(directory));
            }
        }

        if (candidates.Count != 1)
        {
            throw new UpdateSecurityException(
                "InvalidPackageLayout",
                candidates.Count == 0
                    ? "Распакованный пакет не содержит ожидаемую структуру Zapret."
                    : "Распакованный пакет содержит неоднозначную структуру Zapret.");
        }

        var packageRoot = candidates[0];
        ValidateExpectedFiles(packageRoot);
        return packageRoot;
    }

    private static bool HasExpectedLayout(string root)
    {
        return File.Exists(Path.Combine(root, "bin", "winws.exe"))
            && Directory.Exists(Path.Combine(root, "lists"))
            && Directory.EnumerateFiles(root, "general*.bat", SearchOption.TopDirectoryOnly).Any();
    }

    private static void ValidateExpectedFiles(string root)
    {
        var executable = Path.Combine(root, "bin", "winws.exe");
        var info = new FileInfo(executable);

        if (info.Length is <= 0 or > UpdateSecurityLimits.MaxSingleExtractedFileBytes)
        {
            throw new UpdateSecurityException("InvalidEngineExecutable", "winws.exe имеет недопустимый размер.");
        }

        var strategies = Directory.EnumerateFiles(root, "general*.bat", SearchOption.TopDirectoryOnly).ToArray();
        if (strategies.Length is <= 0 or > 256)
        {
            throw new UpdateSecurityException("InvalidStrategySet", "Количество официальных стратегий Zapret недопустимо.");
        }

        foreach (var strategy in strategies)
        {
            var strategyInfo = new FileInfo(strategy);
            if (strategyInfo.Length is <= 0 or > 1024 * 1024)
            {
                throw new UpdateSecurityException("InvalidStrategyFile", "Файл стратегии имеет недопустимый размер.");
            }
        }
    }

    private static void RejectReparsePointsRecursively(string root)
    {
        RejectReparsePoint(root);

        foreach (var path in Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories))
        {
            RejectReparsePoint(path);
        }
    }

    private static void RejectReparsePoint(string path)
    {
        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new UpdateSecurityException(
                "ReparsePointRejected",
                "В распакованном пакете обнаружен reparse point.");
        }
    }
}
