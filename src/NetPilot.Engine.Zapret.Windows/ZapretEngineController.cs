using System.Text;
using NetPilot.Engine.Zapret;

namespace NetPilot.Engine.Zapret.Windows;

public sealed record ZapretStrategyDescriptor(
    string Id,
    string DisplayName,
    string SourceFileName);

public sealed class ZapretEngineController
{
    private static readonly ZapretRuntimeVariables GameFilterDisabled = new("12", "12");
    private static readonly Encoding Utf8Strict = new UTF8Encoding(false, true);

    private readonly string _engineRoot;
    private readonly ZapretProcessRunner _runner;

    public ZapretEngineController(string engineRoot, ZapretProcessRunner runner)
    {
        if (string.IsNullOrWhiteSpace(engineRoot))
        {
            throw new ArgumentException("Корневой каталог движка Zapret не указан.", nameof(engineRoot));
        }

        _engineRoot = Path.GetFullPath(engineRoot);
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
    }

    public IReadOnlyList<ZapretStrategyDescriptor> ListStrategies()
    {
        var result = new List<ZapretStrategyDescriptor>();

        foreach (var path in ZapretStrategyCatalog.DiscoverStrategyFiles(_engineRoot))
        {
            try
            {
                var template = LoadTemplate(path);
                result.Add(new ZapretStrategyDescriptor(
                    template.Id,
                    ZapretStrategyCatalog.GetStrategyDisplayName(template.SourceFileName),
                    template.SourceFileName));
            }
            catch (ZapretStrategyParseException)
            {
                // Несовместимая стратегия не должна автоматически получать право на запуск.
                // Подробная диагностика совместимости будет отдельной функцией расширенного режима.
            }
            catch (DecoderFallbackException)
            {
                // Стратегии читаются только как корректный UTF-8 текст.
            }
        }

        return result;
    }

    public Task<ZapretProcessSnapshot> GetStatusAsync(CancellationToken cancellationToken = default) =>
        _runner.GetStatusAsync(cancellationToken);

    public async Task<ZapretProcessSnapshot> StartAsync(
        string strategyId,
        CancellationToken cancellationToken = default)
    {
        ValidateStrategyId(strategyId);

        var path = FindStrategyPath(strategyId);
        var template = LoadTemplate(path);

        if (!string.Equals(template.Id, strategyId, StringComparison.Ordinal))
        {
            throw new ZapretProcessRunnerException(
                "StrategyIdentityMismatch",
                "Идентификатор выбранной стратегии не соответствует содержимому официального файла.");
        }

        var plan = ZapretLaunchPlanBuilder.Build(template, _engineRoot, GameFilterDisabled);
        return await _runner.StartAsync(plan, cancellationToken);
    }

    public Task<ZapretProcessSnapshot> StopAsync(CancellationToken cancellationToken = default) =>
        _runner.StopAsync(cancellationToken);

    private string FindStrategyPath(string strategyId)
    {
        string? matchedPath = null;

        foreach (var path in ZapretStrategyCatalog.DiscoverStrategyFiles(_engineRoot))
        {
            var candidateId = ZapretStrategyCatalog.GetStrategyId(Path.GetFileName(path));
            if (!string.Equals(candidateId, strategyId, StringComparison.Ordinal))
            {
                continue;
            }

            if (matchedPath is not null)
            {
                throw new ZapretProcessRunnerException(
                    "DuplicateStrategyId",
                    "В каталоге движка обнаружено несколько официальных стратегий с одинаковым идентификатором.");
            }

            matchedPath = path;
        }

        return matchedPath ?? throw new ZapretProcessRunnerException(
            "StrategyNotFound",
            "Запрошенная стратегия Zapret не найдена среди безопасно обнаруженных официальных стратегий.");
    }

    private static void ValidateStrategyId(string strategyId)
    {
        if (string.IsNullOrWhiteSpace(strategyId) || strategyId.Length > 64)
        {
            throw new ZapretProcessRunnerException(
                "InvalidStrategyId",
                "Идентификатор стратегии Zapret имеет недопустимый размер.");
        }

        foreach (var character in strategyId)
        {
            if (!(character is >= 'A' and <= 'Z' or >= '0' and <= '9' or '_'))
            {
                throw new ZapretProcessRunnerException(
                    "InvalidStrategyId",
                    "Идентификатор стратегии Zapret содержит недопустимые символы.");
            }
        }
    }

    private static ZapretStrategyTemplate LoadTemplate(string path)
    {
        var source = File.ReadAllText(path, Utf8Strict);
        return ZapretStrategyParser.Parse(Path.GetFileName(path), source);
    }
}
