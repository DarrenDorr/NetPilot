using NetPilot.Engine.Zapret;
using NetPilot.Engine.Zapret.Windows;
using NetPilot.Ipc;

namespace NetPilot.Service;

public sealed class ZapretServiceException(
    string code,
    string message,
    Exception? innerException = null) : Exception(message, innerException)
{
    public string Code { get; } = code;
}

public interface IZapretInstallationLocator
{
    string? TryGetEngineRoot();
}

public interface IZapretRuntimeConfigurationProvider
{
    ZapretRuntimeVariables? TryGetRuntimeVariables();
}

public sealed class UnconfiguredZapretInstallationLocator : IZapretInstallationLocator
{
    public string? TryGetEngineRoot() => null;
}

public sealed class UnconfiguredZapretRuntimeConfigurationProvider : IZapretRuntimeConfigurationProvider
{
    public ZapretRuntimeVariables? TryGetRuntimeVariables() => null;
}

public sealed class ZapretServiceController(
    IZapretInstallationLocator installationLocator,
    IZapretRuntimeConfigurationProvider runtimeConfigurationProvider,
    ZapretProcessRunner processRunner)
{
    private const int MaxStrategySourceBytes = 256 * 1024;

    public Task<IReadOnlyList<ZapretStrategyInfo>> ListStrategiesAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var catalog = LoadCatalog();

        IReadOnlyList<ZapretStrategyInfo> result = catalog.Values
            .OrderBy(static strategy => strategy.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(static strategy => new ZapretStrategyInfo(strategy.Id, strategy.DisplayName))
            .ToArray();

        return Task.FromResult(result);
    }

    public async Task<ZapretProcessSnapshot> GetStatusAsync(
        CancellationToken cancellationToken = default) =>
        await processRunner.GetStatusAsync(cancellationToken);

    public async Task<ZapretProcessSnapshot> StartAsync(
        string strategyId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(strategyId))
        {
            throw new ZapretServiceException(
                "InvalidStrategyId",
                "Идентификатор стратегии Zapret не указан.");
        }

        var catalog = LoadCatalog();
        if (!catalog.TryGetValue(strategyId, out var strategy))
        {
            throw new ZapretServiceException(
                "StrategyNotFound",
                "Запрошенная стратегия отсутствует в проверенном каталоге движка.");
        }

        var runtimeVariables = runtimeConfigurationProvider.TryGetRuntimeVariables()
            ?? throw new ZapretServiceException(
                "RuntimeConfigurationUnavailable",
                "Runtime-настройки Zapret ещё не настроены защищённым системным компонентом.");

        var engineRoot = GetTrustedEngineRoot();

        ZapretLaunchPlan plan;
        try
        {
            plan = ZapretLaunchPlanBuilder.Build(strategy, engineRoot, runtimeVariables);
        }
        catch (ZapretStrategyParseException exception)
        {
            throw new ZapretServiceException(
                exception.Code,
                "Не удалось построить безопасный план запуска выбранной стратегии.",
                exception);
        }

        return await processRunner.StartAsync(plan, cancellationToken);
    }

    public async Task<ZapretProcessSnapshot> StopAsync(
        CancellationToken cancellationToken = default) =>
        await processRunner.StopAsync(cancellationToken);

    private IReadOnlyDictionary<string, ZapretStrategyTemplate> LoadCatalog()
    {
        var engineRoot = GetTrustedEngineRoot();
        var result = new Dictionary<string, ZapretStrategyTemplate>(StringComparer.Ordinal);

        IReadOnlyList<string> files;
        try
        {
            files = ZapretStrategyCatalog.DiscoverStrategyFiles(engineRoot);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ZapretStrategyParseException)
        {
            throw new ZapretServiceException(
                "StrategyCatalogUnavailable",
                "Не удалось безопасно прочитать каталог стратегий Zapret.",
                exception);
        }

        foreach (var file in files)
        {
            ZapretStrategyTemplate strategy;
            try
            {
                var source = ReadBoundedTextFile(file);
                strategy = ZapretStrategyParser.Parse(Path.GetFileName(file), source);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ZapretStrategyParseException)
            {
                // Повреждённая/небезопасная стратегия не получает право на запуск и не попадает в каталог.
                continue;
            }

            if (!result.TryAdd(strategy.Id, strategy))
            {
                throw new ZapretServiceException(
                    "DuplicateStrategyId",
                    "В каталоге движка обнаружены стратегии с одинаковым безопасным идентификатором.");
            }
        }

        return result;
    }

    private string GetTrustedEngineRoot()
    {
        var configuredRoot = installationLocator.TryGetEngineRoot();
        if (string.IsNullOrWhiteSpace(configuredRoot))
        {
            throw new ZapretServiceException(
                "EngineNotInstalled",
                "Защищённая установка движка Zapret ещё не зарегистрирована в NetPilot.Service.");
        }

        var root = Path.GetFullPath(configuredRoot);
        if (!Path.IsPathFullyQualified(root) || !Directory.Exists(root))
        {
            throw new ZapretServiceException(
                "EngineRootUnavailable",
                "Зарегистрированный каталог движка Zapret недоступен.");
        }

        var attributes = File.GetAttributes(root);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new ZapretServiceException(
                "UnsafeEngineRoot",
                "Зарегистрированный каталог движка Zapret является reparse-point.");
        }

        return root;
    }

    private static string ReadBoundedTextFile(string path)
    {
        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new ZapretStrategyParseException(
                "UnsafeStrategyFile",
                "Файл стратегии является reparse-point.");
        }

        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            FileOptions.SequentialScan);

        if (stream.Length is <= 0 or > MaxStrategySourceBytes)
        {
            throw new ZapretStrategyParseException(
                "InvalidSourceSize",
                "Файл стратегии пуст или превышает допустимый размер.");
        }

        using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);
        var source = reader.ReadToEnd();
        if (source.Length > MaxStrategySourceBytes)
        {
            throw new ZapretStrategyParseException(
                "InvalidSourceSize",
                "Текст стратегии превышает допустимый размер.");
        }

        return source;
    }
}
