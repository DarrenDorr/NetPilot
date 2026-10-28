using System.Text.RegularExpressions;

namespace NetPilot.Engine.Zapret;

public static partial class ZapretStrategyCatalog
{
    private const int MaxStrategyFileBytes = 256 * 1024;

    [GeneratedRegex(
        "^general(?: \\((?<id>[A-Za-z0-9][A-Za-z0-9_-]{0,31})\\))?\\.bat$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex OfficialFileNameRegex();

    public static bool IsOfficialStrategyFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        if (!string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal))
        {
            return false;
        }

        return OfficialFileNameRegex().IsMatch(fileName);
    }

    public static string GetStrategyId(string fileName)
    {
        var match = OfficialFileNameRegex().Match(fileName);
        if (!match.Success)
        {
            throw new ZapretStrategyParseException(
                "InvalidStrategyFileName",
                "Имя файла не соответствует формату официальной стратегии Zapret.");
        }

        return match.Groups["id"].Success
            ? match.Groups["id"].Value.ToUpperInvariant()
            : "DEFAULT";
    }

    public static IReadOnlyList<string> DiscoverStrategyFiles(string engineRoot)
    {
        if (string.IsNullOrWhiteSpace(engineRoot))
        {
            throw new ArgumentException("Корневой каталог движка не указан.", nameof(engineRoot));
        }

        var root = Path.GetFullPath(engineRoot);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException("Каталог движка Zapret не найден.");
        }

        var rootAttributes = File.GetAttributes(root);
        if ((rootAttributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new ZapretStrategyParseException(
                "UnsafeEngineRoot",
                "Корневой каталог движка не может быть символической ссылкой или reparse-point.");
        }

        var result = new List<string>();

        foreach (var path in Directory.EnumerateFiles(root, "general*.bat", SearchOption.TopDirectoryOnly))
        {
            var fullPath = Path.GetFullPath(path);
            if (!IsDirectChild(root, fullPath))
            {
                continue;
            }

            var fileName = Path.GetFileName(fullPath);
            if (!IsOfficialStrategyFileName(fileName))
            {
                continue;
            }

            var attributes = File.GetAttributes(fullPath);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                continue;
            }

            var length = new FileInfo(fullPath).Length;
            if (length is <= 0 or > MaxStrategyFileBytes)
            {
                continue;
            }

            result.Add(fullPath);
        }

        result.Sort(static (left, right) =>
            StringComparer.OrdinalIgnoreCase.Compare(Path.GetFileName(left), Path.GetFileName(right)));

        return result;
    }

    private static bool IsDirectChild(string root, string path)
    {
        var parent = Directory.GetParent(path)?.FullName;
        if (parent is null)
        {
            return false;
        }

        return string.Equals(
            Path.TrimEndingDirectorySeparator(parent),
            Path.TrimEndingDirectorySeparator(root),
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal);
    }
}
