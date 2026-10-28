using System.IO.Compression;

namespace NetPilot.Updater;

public static class SafeZipExtractor
{
    private const int UnixFileTypeMask = 0xF000;
    private const int UnixSymbolicLink = 0xA000;

    public static async Task ExtractAsync(
        Stream zipStream,
        string stagingRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(zipStream);
        ArgumentException.ThrowIfNullOrWhiteSpace(stagingRoot);

        try
        {
            await ExtractCoreAsync(zipStream, stagingRoot, cancellationToken);
        }
        catch (InvalidDataException exception)
        {
            throw new UpdateSecurityException(
                "InvalidZipArchive",
                "ZIP-пакет обновления повреждён или имеет неподдерживаемую структуру.",
                exception);
        }
    }

    private static async Task ExtractCoreAsync(
        Stream zipStream,
        string stagingRoot,
        CancellationToken cancellationToken)
    {
        var root = EnsureCleanStagingRoot(stagingRoot);
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: true);

        if (archive.Entries.Count is <= 0 or > UpdateSecurityLimits.MaxArchiveEntries)
        {
            throw new UpdateSecurityException(
                "ArchiveEntryLimit",
                "Количество файлов в ZIP выходит за безопасный лимит.");
        }

        long totalExtractedBytes = 0;
        var destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            ValidateEntryMetadata(entry);
            var destination = GetSafeDestination(root, entry.FullName);

            if (!destinations.Add(destination))
            {
                throw new UpdateSecurityException(
                    "DuplicateArchivePath",
                    "ZIP содержит несколько записей, указывающих на один и тот же путь.");
            }

            var isDirectory = entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\');

            if (isDirectory)
            {
                Directory.CreateDirectory(destination);
                RejectReparsePoint(destination);
                continue;
            }

            checked
            {
                totalExtractedBytes += entry.Length;
            }

            if (totalExtractedBytes > UpdateSecurityLimits.MaxTotalExtractedBytes)
            {
                throw new UpdateSecurityException(
                    "ArchiveExpandedSizeLimit",
                    "Суммарный распакованный размер ZIP превышает безопасный лимит.");
            }

            var parent = Path.GetDirectoryName(destination)
                ?? throw new UpdateSecurityException("InvalidArchivePath", "ZIP содержит путь без родительского каталога.");

            Directory.CreateDirectory(parent);
            EnsureNoReparsePointsBetween(root, parent);

            await using var source = entry.Open();
            await using var target = new FileStream(
                destination,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            await CopyWithLimitAsync(source, target, entry.Length, cancellationToken);
        }
    }

    private static string EnsureCleanStagingRoot(string stagingRoot)
    {
        var root = Path.GetFullPath(stagingRoot);
        if (Directory.Exists(root))
        {
            if (Directory.EnumerateFileSystemEntries(root).Any())
            {
                throw new UpdateSecurityException(
                    "StagingNotEmpty",
                    "Каталог staging должен быть пустым перед распаковкой.");
            }
        }
        else
        {
            Directory.CreateDirectory(root);
        }

        RejectReparsePoint(root);
        return root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
    }

    private static void ValidateEntryMetadata(ZipArchiveEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.FullName)
            || entry.FullName.Length > UpdateSecurityLimits.MaxArchivePathCharacters
            || entry.FullName.Contains('\0'))
        {
            throw new UpdateSecurityException("InvalidArchivePath", "ZIP содержит недопустимое имя записи.");
        }

        if (entry.Length < 0 || entry.Length > UpdateSecurityLimits.MaxSingleExtractedFileBytes)
        {
            throw new UpdateSecurityException(
                "ArchiveFileSizeLimit",
                "Один из файлов ZIP превышает безопасный лимит распакованного размера.");
        }

        if (entry.Length > 0)
        {
            if (entry.CompressedLength <= 0)
            {
                throw new UpdateSecurityException("SuspiciousCompression", "ZIP содержит подозрительную запись с нулевым сжатым размером.");
            }

            var ratio = (double)entry.Length / entry.CompressedLength;
            if (ratio > UpdateSecurityLimits.MaxCompressionRatio)
            {
                throw new UpdateSecurityException(
                    "CompressionRatioLimit",
                    "Коэффициент распаковки ZIP превышает безопасный лимит.");
            }
        }

        var unixMode = (entry.ExternalAttributes >> 16) & 0xFFFF;
        if ((unixMode & UnixFileTypeMask) == UnixSymbolicLink)
        {
            throw new UpdateSecurityException("ArchiveLinkRejected", "Символические ссылки в ZIP запрещены.");
        }
    }

    private static string GetSafeDestination(string rootWithSeparator, string entryName)
    {
        var normalized = entryName.Replace('\\', '/');

        if (normalized.StartsWith("/", StringComparison.Ordinal)
            || normalized.Contains(':'))
        {
            throw new UpdateSecurityException("InvalidArchivePath", "Абсолютные пути и имена дисков в ZIP запрещены.");
        }

        var segments = normalized.Split('/', StringSplitOptions.None);
        if (segments.Any(static segment => segment == ".."))
        {
            throw new UpdateSecurityException("ArchiveTraversal", "ZIP содержит попытку выхода за staging-каталог.");
        }

        var relative = normalized.Replace('/', Path.DirectorySeparatorChar);
        var destination = Path.GetFullPath(Path.Combine(rootWithSeparator, relative));

        if (!destination.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            throw new UpdateSecurityException("ArchiveTraversal", "ZIP содержит путь за пределами staging-каталога.");
        }

        return destination;
    }

    private static async Task CopyWithLimitAsync(
        Stream source,
        Stream target,
        long declaredLength,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[64 * 1024];
        long written = 0;

        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            checked
            {
                written += read;
            }

            if (written > declaredLength || written > UpdateSecurityLimits.MaxSingleExtractedFileBytes)
            {
                throw new UpdateSecurityException(
                    "ArchiveLengthMismatch",
                    "Фактический распакованный размер не соответствует безопасным метаданным ZIP.");
            }

            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        if (written != declaredLength)
        {
            throw new UpdateSecurityException(
                "ArchiveLengthMismatch",
                "Фактический распакованный размер не совпадает с метаданными ZIP.");
        }
    }

    private static void EnsureNoReparsePointsBetween(string rootWithSeparator, string directory)
    {
        var root = rootWithSeparator.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var current = Path.GetFullPath(directory);

        while (current.Length >= root.Length)
        {
            RejectReparsePoint(current);
            if (string.Equals(current, root, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            current = Path.GetDirectoryName(current)
                ?? throw new UpdateSecurityException("InvalidStagingPath", "Не удалось проверить staging-каталог.");
        }

        throw new UpdateSecurityException("InvalidStagingPath", "Путь staging вышел за разрешённый корень.");
    }

    private static void RejectReparsePoint(string path)
    {
        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new UpdateSecurityException(
                "ReparsePointRejected",
                "Reparse point в staging-пути запрещён.");
        }
    }
}
