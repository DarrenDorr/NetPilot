using System.Text;
using System.Text.RegularExpressions;

namespace NetPilot.Engine.Zapret;

public static partial class ZapretStrategyParser
{
    private const int MaxSourceCharacters = 256 * 1024;
    private const int MaxLogicalLineCharacters = 64 * 1024;
    private const int MaxArguments = 512;
    private const int MaxArgumentCharacters = 8 * 1024;

    private static readonly IReadOnlyDictionary<string, ZapretTemplatePartKind> KnownVariables =
        new Dictionary<string, ZapretTemplatePartKind>(StringComparer.OrdinalIgnoreCase)
        {
            ["%BIN%"] = ZapretTemplatePartKind.EngineBin,
            ["%LISTS%"] = ZapretTemplatePartKind.Lists,
            ["%GameFilterTCP%"] = ZapretTemplatePartKind.GameFilterTcp,
            ["%GameFilterUDP%"] = ZapretTemplatePartKind.GameFilterUdp
        };

    private static readonly string[] ForbiddenArgumentFileExtensions =
    [
        ".exe",
        ".bat",
        ".cmd",
        ".com",
        ".ps1"
    ];

    [GeneratedRegex(
        "^\\s*start\\s+\"[^\"]{0,128}\"\\s+/min\\s+\"%BIN%winws\\.exe\"\\s+(?<arguments>.+?)\\s*$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex WinwsInvocationRegex();

    [GeneratedRegex("%[^%\\r\\n]+%", RegexOptions.CultureInvariant)]
    private static partial Regex VariableRegex();

    public static ZapretStrategyTemplate Parse(string sourceFileName, string source)
    {
        if (!ZapretStrategyCatalog.IsOfficialStrategyFileName(sourceFileName))
        {
            throw new ZapretStrategyParseException(
                "InvalidStrategyFileName",
                "Источник стратегии имеет недопустимое имя файла.");
        }

        if (string.IsNullOrWhiteSpace(source) || source.Length > MaxSourceCharacters)
        {
            throw new ZapretStrategyParseException(
                "InvalidSourceSize",
                "Файл стратегии пуст или превышает допустимый размер.");
        }

        var logicalLines = BuildLogicalLines(source);
        string? argumentText = null;
        var invocationCount = 0;

        foreach (var line in logicalLines)
        {
            if (!line.Contains("winws.exe", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var match = WinwsInvocationRegex().Match(line);
            if (!match.Success)
            {
                throw new ZapretStrategyParseException(
                    "UnsafeWinwsInvocation",
                    "Обнаружен вызов winws, который не соответствует разрешённому формату официальной стратегии.");
            }

            invocationCount++;
            if (invocationCount > 1)
            {
                throw new ZapretStrategyParseException(
                    "MultipleWinwsInvocations",
                    "Одна стратегия не может содержать несколько запусков winws.");
            }

            argumentText = match.Groups["arguments"].Value;
        }

        if (invocationCount != 1 || string.IsNullOrWhiteSpace(argumentText))
        {
            throw new ZapretStrategyParseException(
                "WinwsInvocationMissing",
                "В стратегии не найден единственный разрешённый запуск winws.");
        }

        EnsureNoShellMetacharacters(argumentText);
        var rawArguments = TokenizeArguments(argumentText);
        var arguments = rawArguments.Select(ParseArgument).ToArray();
        var id = ZapretStrategyCatalog.GetStrategyId(sourceFileName);

        return new ZapretStrategyTemplate(
            id,
            id == "DEFAULT" ? "Стандартная" : id,
            sourceFileName,
            arguments);
    }

    private static IReadOnlyList<string> BuildLogicalLines(string source)
    {
        var lines = source.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');

        var result = new List<string>(lines.Length);
        var current = new StringBuilder();

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd();
            var continued = line.EndsWith('^');

            if (continued)
            {
                line = line[..^1].TrimEnd();
            }

            if (current.Length > 0 && line.Length > 0)
            {
                current.Append(' ');
            }

            current.Append(line);

            if (current.Length > MaxLogicalLineCharacters)
            {
                throw new ZapretStrategyParseException(
                    "LogicalLineTooLong",
                    "Логическая строка стратегии превышает допустимый размер.");
            }

            if (continued)
            {
                continue;
            }

            result.Add(current.ToString());
            current.Clear();
        }

        if (current.Length != 0)
        {
            throw new ZapretStrategyParseException(
                "DanglingContinuation",
                "Файл стратегии заканчивается незавершённым переносом строки.");
        }

        return result;
    }

    private static void EnsureNoShellMetacharacters(string argumentText)
    {
        foreach (var character in argumentText)
        {
            if (character is '&' or '|' or '<' or '>' or ';' or '^' or '`')
            {
                throw new ZapretStrategyParseException(
                    "ShellMetacharacterRejected",
                    "Аргументы стратегии содержат недопустимый управляющий символ.");
            }
        }
    }

    private static IReadOnlyList<string> TokenizeArguments(string argumentText)
    {
        var result = new List<string>();
        var token = new StringBuilder();
        var inQuotes = false;

        for (var index = 0; index < argumentText.Length; index++)
        {
            var character = argumentText[index];

            if (character == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (char.IsWhiteSpace(character) && !inQuotes)
            {
                AddToken(result, token);
                continue;
            }

            if (character is '\n' or '\r' or '\0')
            {
                throw new ZapretStrategyParseException(
                    "InvalidArgumentCharacter",
                    "Аргумент стратегии содержит недопустимый символ.");
            }

            token.Append(character);
            if (token.Length > MaxArgumentCharacters)
            {
                throw new ZapretStrategyParseException(
                    "ArgumentTooLong",
                    "Один из аргументов стратегии превышает допустимый размер.");
            }
        }

        if (inQuotes)
        {
            throw new ZapretStrategyParseException(
                "UnterminatedQuote",
                "В аргументах стратегии обнаружена незакрытая кавычка.");
        }

        AddToken(result, token);

        if (result.Count is 0 or > MaxArguments)
        {
            throw new ZapretStrategyParseException(
                "InvalidArgumentCount",
                "Количество аргументов стратегии выходит за допустимые пределы.");
        }

        return result;
    }

    private static void AddToken(List<string> result, StringBuilder token)
    {
        if (token.Length == 0)
        {
            return;
        }

        result.Add(token.ToString());
        token.Clear();
    }

    private static ZapretArgumentTemplate ParseArgument(string argument)
    {
        if (!argument.StartsWith("--", StringComparison.Ordinal)
            || argument.Length < 3
            || char.IsWhiteSpace(argument[2]))
        {
            throw new ZapretStrategyParseException(
                "InvalidArgumentShape",
                "После winws разрешены только отдельные параметры, начинающиеся с '--'.");
        }

        if (argument.Contains('"') || argument.Contains('\''))
        {
            throw new ZapretStrategyParseException(
                "UnexpectedQuote",
                "Кавычки внутри значения аргумента не поддерживаются.");
        }

        foreach (var extension in ForbiddenArgumentFileExtensions)
        {
            if (argument.Contains(extension, StringComparison.OrdinalIgnoreCase))
            {
                throw new ZapretStrategyParseException(
                    "ExecutablePayloadRejected",
                    "Аргумент стратегии не может ссылаться на исполняемый или скриптовый файл.");
            }
        }

        ValidatePathPlaceholders(argument);

        var parts = new List<ZapretTemplatePart>();
        var position = 0;

        foreach (Match match in VariableRegex().Matches(argument))
        {
            if (match.Index > position)
            {
                parts.Add(new ZapretTemplatePart(
                    ZapretTemplatePartKind.Literal,
                    argument[position..match.Index]));
            }

            if (!KnownVariables.TryGetValue(match.Value, out var kind))
            {
                throw new ZapretStrategyParseException(
                    "UnknownVariable",
                    $"Стратегия использует неизвестную переменную: {match.Value}.");
            }

            parts.Add(new ZapretTemplatePart(kind, string.Empty));
            position = match.Index + match.Length;
        }

        if (position < argument.Length)
        {
            parts.Add(new ZapretTemplatePart(
                ZapretTemplatePartKind.Literal,
                argument[position..]));
        }

        if (argument[position..].Contains('%')
            || (parts.Count == 0 && argument.Contains('%')))
        {
            throw new ZapretStrategyParseException(
                "MalformedVariable",
                "Аргумент стратегии содержит некорректную переменную.");
        }

        return new ZapretArgumentTemplate(parts);
    }

    private static void ValidatePathPlaceholders(string argument)
    {
        ValidatePathPlaceholder(argument, "%BIN%");
        ValidatePathPlaceholder(argument, "%LISTS%");
    }

    private static void ValidatePathPlaceholder(string argument, string placeholder)
    {
        var searchStart = 0;
        while (true)
        {
            var index = argument.IndexOf(placeholder, searchStart, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                return;
            }

            var suffixStart = index + placeholder.Length;
            var suffixEnd = argument.IndexOf(',', suffixStart);
            if (suffixEnd < 0)
            {
                suffixEnd = argument.Length;
            }

            var relativePath = argument[suffixStart..suffixEnd];
            if (string.IsNullOrWhiteSpace(relativePath)
                || relativePath.StartsWith('/')
                || relativePath.StartsWith('\\')
                || relativePath.Contains(':')
                || relativePath.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries)
                    .Any(static segment => segment is "." or ".."))
            {
                throw new ZapretStrategyParseException(
                    "UnsafeRelativePath",
                    "Путь внутри стратегии выходит за разрешённый каталог движка.");
            }

            searchStart = suffixEnd;
        }
    }
}
