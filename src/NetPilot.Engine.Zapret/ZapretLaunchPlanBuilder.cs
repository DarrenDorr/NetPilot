using System.Text;

namespace NetPilot.Engine.Zapret;

public static class ZapretLaunchPlanBuilder
{
    private const int MaxResolvedArgumentCharacters = 16 * 1024;

    public static ZapretLaunchPlan Build(
        ZapretStrategyTemplate strategy,
        string engineRoot,
        ZapretRuntimeVariables variables)
    {
        ArgumentNullException.ThrowIfNull(strategy);
        ArgumentNullException.ThrowIfNull(variables);

        var root = ValidateDirectory(engineRoot, "EngineRootMissing", "Корневой каталог движка Zapret недоступен.");
        var binRoot = ValidateDirectory(Path.Combine(root, "bin"), "BinDirectoryMissing", "Каталог bin движка Zapret недоступен.");
        var listsRoot = ValidateDirectory(Path.Combine(root, "lists"), "ListsDirectoryMissing", "Каталог lists движка Zapret недоступен.");

        EnsureDirectChild(root, binRoot, "UnsafeBinDirectory");
        EnsureDirectChild(root, listsRoot, "UnsafeListsDirectory");

        var executablePath = ValidateFileInsideRoot(
            binRoot,
            "winws.exe",
            "WinwsMissing",
            "Исполняемый файл winws.exe отсутствует или небезопасен.");

        var gameFilterTcp = NormalizePortList(variables.GameFilterTcp, "GameFilterTcpInvalid");
        var gameFilterUdp = NormalizePortList(variables.GameFilterUdp, "GameFilterUdpInvalid");

        var resolvedArguments = strategy.Arguments
            .Select(argument => ResolveArgument(argument, binRoot, listsRoot, gameFilterTcp, gameFilterUdp))
            .ToArray();

        return new ZapretLaunchPlan(
            strategy.Id,
            executablePath,
            binRoot,
            resolvedArguments);
    }

    private static string ResolveArgument(
        ZapretArgumentTemplate argument,
        string binRoot,
        string listsRoot,
        string gameFilterTcp,
        string gameFilterUdp)
    {
        var builder = new StringBuilder();

        for (var index = 0; index < argument.Parts.Count; index++)
        {
            var part = argument.Parts[index];
            switch (part.Kind)
            {
                case ZapretTemplatePartKind.Literal:
                    builder.Append(part.Value);
                    break;

                case ZapretTemplatePartKind.GameFilterTcp:
                    builder.Append(gameFilterTcp);
                    break;

                case ZapretTemplatePartKind.GameFilterUdp:
                    builder.Append(gameFilterUdp);
                    break;

                case ZapretTemplatePartKind.EngineBin:
                case ZapretTemplatePartKind.Lists:
                {
                    if (index + 1 >= argument.Parts.Count
                        || argument.Parts[index + 1].Kind != ZapretTemplatePartKind.Literal)
                    {
                        throw new ZapretStrategyParseException(
                            "PathPlaceholderWithoutSuffix",
                            "Путь стратегии не содержит имени файла после доверенного placeholder.");
                    }

                    var followingLiteral = argument.Parts[index + 1].Value;
                    var separatorIndex = followingLiteral.IndexOf(',');
                    var relativePath = separatorIndex >= 0
                        ? followingLiteral[..separatorIndex]
                        : followingLiteral;
                    var trailingLiteral = separatorIndex >= 0
                        ? followingLiteral[separatorIndex..]
                        : string.Empty;

                    var trustedRoot = part.Kind == ZapretTemplatePartKind.EngineBin ? binRoot : listsRoot;
                    var resolvedFile = ValidateFileInsideRoot(
                        trustedRoot,
                        relativePath,
                        "ReferencedFileMissing",
                        "Стратегия ссылается на отсутствующий или небезопасный файл движка.");

                    builder.Append(resolvedFile);
                    builder.Append(trailingLiteral);
                    index++;
                    break;
                }

                default:
                    throw new ZapretStrategyParseException(
                        "UnknownTemplatePart",
                        "Стратегия содержит неизвестную часть шаблона.");
            }

            if (builder.Length > MaxResolvedArgumentCharacters)
            {
                throw new ZapretStrategyParseException(
                    "ResolvedArgumentTooLong",
                    "Разрешённый аргумент стратегии превышает допустимый размер.");
            }
        }

        var result = builder.ToString();
        if (!result.StartsWith("--", StringComparison.Ordinal))
        {
            throw new ZapretStrategyParseException(
                "ResolvedArgumentInvalid",
                "Разрешённый аргумент стратегии имеет недопустимый формат.");
        }

        return result;
    }

    private static string NormalizePortList(string value, string errorCode)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 4 * 1024)
        {
            throw new ZapretStrategyParseException(errorCode, "Список игровых портов пуст или слишком велик.");
        }

        var normalizedItems = new List<string>();
        foreach (var rawItem in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var dashIndex = rawItem.IndexOf('-');
            if (dashIndex < 0)
            {
                var port = ParsePort(rawItem, errorCode);
                normalizedItems.Add(port.ToString());
                continue;
            }

            if (dashIndex == 0
                || dashIndex == rawItem.Length - 1
                || rawItem.IndexOf('-', dashIndex + 1) >= 0)
            {
                throw new ZapretStrategyParseException(errorCode, "Диапазон игровых портов имеет недопустимый формат.");
            }

            var start = ParsePort(rawItem[..dashIndex], errorCode);
            var end = ParsePort(rawItem[(dashIndex + 1)..], errorCode);
            if (start > end)
            {
                throw new ZapretStrategyParseException(errorCode, "Начало диапазона игровых портов больше его конца.");
            }

            normalizedItems.Add($"{start}-{end}");
        }

        if (normalizedItems.Count == 0)
        {
            throw new ZapretStrategyParseException(errorCode, "Список игровых портов не содержит допустимых значений.");
        }

        return string.Join(',', normalizedItems);
    }

    private static int ParsePort(string value, string errorCode)
    {
        if (!int.TryParse(value, out var port) || port is < 1 or > 65535)
        {
            throw new ZapretStrategyParseException(errorCode, "Игровой порт должен находиться в диапазоне 1..65535.");
        }

        return port;
    }

    private static string ValidateDirectory(string path, string errorCode, string message)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ZapretStrategyParseException(errorCode, message);
        }

        var fullPath = Path.GetFullPath(path);
        if (!Directory.Exists(fullPath))
        {
            throw new ZapretStrategyParseException(errorCode, message);
        }

        RejectReparsePoint(fullPath, errorCode, message);
        return Path.TrimEndingDirectorySeparator(fullPath);
    }

    private static string ValidateFileInsideRoot(
        string trustedRoot,
        string relativePath,
        string errorCode,
        string message)
    {
        if (string.IsNullOrWhiteSpace(relativePath)
            || Path.IsPathRooted(relativePath)
            || relativePath.Contains(':'))
        {
            throw new ZapretStrategyParseException(errorCode, message);
        }

        var platformRelative = relativePath
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);

        var candidate = Path.GetFullPath(Path.Combine(trustedRoot, platformRelative));
        if (!IsInsideRoot(trustedRoot, candidate) || !File.Exists(candidate))
        {
            throw new ZapretStrategyParseException(errorCode, message);
        }

        RejectReparsePath(trustedRoot, candidate, errorCode, message);
        return candidate;
    }

    private static void EnsureDirectChild(string root, string child, string errorCode)
    {
        var parent = Directory.GetParent(child)?.FullName;
        if (parent is null
            || !string.Equals(
                Path.TrimEndingDirectorySeparator(parent),
                Path.TrimEndingDirectorySeparator(root),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        {
            throw new ZapretStrategyParseException(errorCode, "Каталог движка находится за пределами доверенного корня.");
        }
    }

    private static bool IsInsideRoot(string root, string candidate)
    {
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var normalizedCandidate = Path.GetFullPath(candidate);
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (string.Equals(normalizedRoot, normalizedCandidate, comparison))
        {
            return false;
        }

        return normalizedCandidate.StartsWith(
            normalizedRoot + Path.DirectorySeparatorChar,
            comparison);
    }

    private static void RejectReparsePath(
        string trustedRoot,
        string candidate,
        string errorCode,
        string message)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(trustedRoot));
        var current = Path.GetFullPath(candidate);
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        while (!string.Equals(current, root, comparison))
        {
            RejectReparsePoint(current, errorCode, message);
            var parent = Path.GetDirectoryName(current);
            if (string.IsNullOrWhiteSpace(parent) || !IsInsideRootOrSame(root, parent, comparison))
            {
                throw new ZapretStrategyParseException(errorCode, message);
            }

            current = parent;
        }

        RejectReparsePoint(root, errorCode, message);
    }

    private static bool IsInsideRootOrSame(string root, string path, StringComparison comparison)
    {
        var normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        return string.Equals(root, normalized, comparison)
            || normalized.StartsWith(root + Path.DirectorySeparatorChar, comparison);
    }

    private static void RejectReparsePoint(string path, string errorCode, string message)
    {
        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new ZapretStrategyParseException(errorCode, message);
        }
    }
}
