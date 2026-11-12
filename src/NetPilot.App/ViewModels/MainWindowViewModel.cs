using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Avalonia.Media;
using NetPilot.App.Services;
using NetPilot.Automation;
using NetPilot.Core.Models;
using NetPilot.Ipc;

namespace NetPilot.App.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IDashboardService _dashboardService;
    private readonly Dictionary<string, ResourceStatusViewModel> _resourceById;
    private readonly AsyncCommand _toggleEngineCommand;
    private readonly AsyncCommand _checkCommand;
    private readonly AsyncCommand _checkDirectCommand;
    private readonly AsyncCommand _findStrategyCommand;

    private string _overallStatus = "Готов к проверке";
    private string _overallDetails = "Запустите ByeDPI для проверки через локальный SOCKS5. Можно отдельно проверить прямое соединение.";
    private string _healthRouteDescription = "Маршрут: ByeDPI SOCKS5 (127.0.0.1), без системного прокси";
    private string _strategyDiagnostics = "Диагностика подбора появится после проверки стратегии.";
    private string _engineStatus = "Состояние сетевого движка ещё не получено.";
    private string _engineActionText = "Запустить ByeDPI";
    private IBrush _engineStatusBrush = Brushes.SlateGray;
    private bool _isEngineRunning;
    private bool _isMenuExpanded = true;
    private string _lastAction = "Действий ещё не выполнялось.";
    private bool _isBusy;
    private bool _isAdvancedMode;
    private bool _disposed;

    public MainWindowViewModel(IDashboardService dashboardService)
    {
        _dashboardService = dashboardService ?? throw new ArgumentNullException(nameof(dashboardService));

        Resources = new ObservableCollection<ResourceStatusViewModel>(
            dashboardService.Resources.Select(resource =>
                new ResourceStatusViewModel(resource.Id, resource.DisplayName)));

        Telegram = new ResourceStatusViewModel("telegram", "Telegram");
        Telegram.SetNotAvailableYet("Поддержка Telegram будет добавлена отдельным движком.");
        Resources.Add(Telegram);

        _resourceById = Resources
            .Where(resource => !string.Equals(resource.Id, "telegram", StringComparison.Ordinal))
            .ToDictionary(resource => resource.Id, StringComparer.Ordinal);

        _toggleEngineCommand = new AsyncCommand(
            ToggleEngineAsync,
            () => !IsBusy,
            HandleUnexpectedError);

        _checkCommand = new AsyncCommand(
            CheckNowAsync,
            () => !IsBusy,
            HandleUnexpectedError);

        _checkDirectCommand = new AsyncCommand(
            CheckDirectNowAsync,
            () => !IsBusy,
            HandleUnexpectedError);

        _findStrategyCommand = new AsyncCommand(
            FindStrategyAsync,
            () => !IsBusy,
            HandleUnexpectedError);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Title => "NetPilot";
    public string Subtitle => "Менеджер сетевого доступа";

    public ObservableCollection<ResourceStatusViewModel> Resources { get; }
    public ResourceStatusViewModel Telegram { get; }

    public string OverallStatus
    {
        get => _overallStatus;
        private set => SetField(ref _overallStatus, value);
    }

    public string OverallDetails
    {
        get => _overallDetails;
        private set => SetField(ref _overallDetails, value);
    }

    public string EngineStatus
    {
        get => _engineStatus;
        private set => SetField(ref _engineStatus, value);
    }

    public string EngineActionText
    {
        get => _engineActionText;
        private set => SetField(ref _engineActionText, value);
    }

    public string HealthRouteDescription
    {
        get => _healthRouteDescription;
        private set => SetField(ref _healthRouteDescription, value);
    }

    public string StrategyDiagnostics
    {
        get => _strategyDiagnostics;
        private set => SetField(ref _strategyDiagnostics, value);
    }

    public string LastAction
    {
        get => _lastAction;
        private set => SetField(ref _lastAction, value);
    }

    // These presentation-only properties reflect the *confirmed* runtime
    // state, not the user's last requested action. Never display a running
    // indicator before GetStrategyRuntimeStateAsync confirms it.
    public bool IsEngineRunning
    {
        get => _isEngineRunning;
        private set => SetField(ref _isEngineRunning, value);
    }

    public IBrush EngineStatusBrush
    {
        get => _engineStatusBrush;
        private set => SetField(ref _engineStatusBrush, value);
    }

    public bool IsMenuExpanded
    {
        get => _isMenuExpanded;
        set
        {
            if (SetField(ref _isMenuExpanded, value))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SidebarWidth)));
            }
        }
    }

    public double SidebarWidth => IsMenuExpanded ? 228 : 76;

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetField(ref _isBusy, value))
            {
                _toggleEngineCommand.RaiseCanExecuteChanged();
                _checkCommand.RaiseCanExecuteChanged();
                _checkDirectCommand.RaiseCanExecuteChanged();
                _findStrategyCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsAdvancedMode
    {
        get => _isAdvancedMode;
        set => SetField(ref _isAdvancedMode, value);
    }

    public ICommand ToggleEngineCommand => _toggleEngineCommand;
    public ICommand CheckCommand => _checkCommand;
    public ICommand CheckDirectCommand => _checkDirectCommand;
    public ICommand FindStrategyCommand => _findStrategyCommand;

    public async Task InitializeAsync()
    {
        ThrowIfDisposed();
        await RefreshEngineStatusAsync(CancellationToken.None);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _dashboardService.Dispose();
    }

    private async Task ToggleEngineAsync()
    {
        ThrowIfDisposed();
        IsBusy = true;

        try
        {
            var state = await _dashboardService.GetStrategyRuntimeStateAsync();
            if (state.IsRunning)
            {
                OverallStatus = "Останавливаем ByeDPI…";
                OverallDetails = "NetPilot завершает только принадлежащий ему user-mode процесс.";
                await _dashboardService.StopStrategyRuntimeAsync();
                OverallStatus = "ByeDPI остановлен";
                OverallDetails = "Локальный SOCKS5-процесс завершён; системные сетевые настройки не изменялись.";
                LastAction = "ByeDPI остановлен штатно.";
            }
            else
            {
                OverallStatus = "Запускаем ByeDPI…";
                OverallDetails = "Проверяем точный SHA-256 и запускаем локальный SOCKS5 без повышения прав.";
                await _dashboardService.StartDefaultStrategyAsync();
                OverallStatus = "ByeDPI запущен";
                OverallDetails = "Проверенный user-mode движок работает только на loopback без WinDivert.";
                LastAction = "ByeDPI запущен без службы и без повышения прав.";
            }

            await RefreshEngineStatusAsync(CancellationToken.None);
        }
        catch (Exception exception) when (IsExpectedOperationalException(exception))
        {
            OverallStatus = "Операция с ByeDPI остановлена";
            OverallDetails = GetUserMessage(exception);
            LastAction = "NetPilot завершил операцию fail-closed.";
            await RefreshEngineStatusAsync(CancellationToken.None);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private Task CheckNowAsync() => CheckHealthAsync(useDirect: false);

    private Task CheckDirectNowAsync() => CheckHealthAsync(useDirect: true);

    private async Task CheckHealthAsync(bool useDirect)
    {
        ThrowIfDisposed();
        IsBusy = true;
        OverallStatus = "Проверяем доступность…";
        HealthRouteDescription = useDirect
            ? "Маршрут: прямое HTTPS-подключение Windows (без ByeDPI)"
            : "Маршрут: локальный ByeDPI SOCKS5 (без системного прокси)";
        OverallDetails = useDirect
            ? "Прямая проверка HTTPS без ByeDPI, авторизации и cookie."
            : "Проверка HTTPS только через локальный SOCKS5 ByeDPI; это не проверка трафика браузера.";

        foreach (var resource in _resourceById.Values)
        {
            resource.SetChecking();
        }

        try
        {
            var results = useDirect
                ? await _dashboardService.CheckDirectResourcesAsync()
                : await _dashboardService.CheckResourcesAsync();
            ApplyHealthResults(results);
            await RefreshEngineStatusAsync(CancellationToken.None);
            LastAction = useDirect
                ? "Прямая HTTPS-проверка завершена (ByeDPI не использовался)."
                : "Проверка через ByeDPI SOCKS5 завершена (браузер отдельно не проверялся).";
        }
        catch (Exception exception) when (IsExpectedOperationalException(exception))
        {
            OverallStatus = "Не удалось выполнить проверку";
            OverallDetails = GetUserMessage(exception);
            LastAction = "Проверка завершилась технической ошибкой.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task FindStrategyAsync()
    {
        ThrowIfDisposed();
        IsBusy = true;
        OverallStatus = "Подбираем рабочую стратегию…";
        StrategyDiagnostics = "Подбор выполняется; результат ещё не подтверждён.";
        HealthRouteDescription = "Маршрут: локальный ByeDPI SOCKS5 (подбор стратегии)";
        OverallDetails = "NetPilot сначала подтверждает проблему, затем проверяет ограниченное число альтернатив через локальный ByeDPI SOCKS5.";

        foreach (var resource in _resourceById.Values)
        {
            resource.SetChecking();
        }

        try
        {
            var result = await _dashboardService.FindWorkingStrategyAsync();
            ApplyStrategySelectionResult(result);
            StrategyDiagnostics = BuildStrategyDiagnosticSummary(result);

            var health = await _dashboardService.CheckResourcesAsync();
            ApplyHealthResults(health, preserveActionText: true);
            await RefreshEngineStatusAsync(CancellationToken.None);
        }
        catch (Exception exception) when (IsExpectedOperationalException(exception))
        {
            OverallStatus = "Подбор не выполнен";
            OverallDetails = GetUserMessage(exception);
            LastAction = "Автоматический подбор остановлен без скрытого продолжения.";
            StrategyDiagnostics = "Подбор прерван до подтверждённого результата.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplyHealthResults(
        IReadOnlyList<ResourceHealth> results,
        bool preserveActionText = false)
    {
        foreach (var result in results)
        {
            if (_resourceById.TryGetValue(result.ResourceId, out var resource))
            {
                resource.Apply(result);
            }
        }

        if (results.Count > 0 && results.All(result => result.State == ResourceHealthState.Healthy))
        {
            OverallStatus = "Доступ в порядке";
            OverallDetails = "Discord и YouTube отвечают на проверку по выбранному маршруту. Это не гарантирует доступ браузера.";
        }
        else if (results.Any(result => result.State == ResourceHealthState.Unavailable))
        {
            OverallStatus = "Есть недоступные сервисы";
            OverallDetails = "Проверьте указанный маршрут. Для SOCKS5 можно подобрать стратегию; прямой маршрут от этого не изменится.";
        }
        else
        {
            OverallStatus = "Есть ограничения";
            OverallDetails = "Часть сервисов отвечает нестабильно или с ограничениями.";
        }

        if (!preserveActionText)
        {
            LastAction = "Состояние ресурсов обновлено.";
        }
    }

    private void ApplyStrategySelectionResult(StrategySelectionResult result)
    {
        switch (result.Status)
        {
            case StrategySelectionStatus.CurrentHealthy:
                LastAction = "Текущая стратегия уже работает. Переключение не выполнялось.";
                break;

            case StrategySelectionStatus.TransientFailure:
                LastAction = "Обнаружен кратковременный сбой. Текущая стратегия сохранена.";
                break;

            case StrategySelectionStatus.Switched:
                LastAction = "NetPilot подтвердил рабочую альтернативу и переключил стратегию.";
                break;

            case StrategySelectionStatus.NotRunning:
                LastAction = "ByeDPI сейчас не запущен. Автоматический запуск не выполнялся.";
                OverallStatus = "Сетевой движок не запущен";
                OverallDetails = "Сначала запустите ByeDPI, затем станет доступен автоматический подбор.";
                break;

            case StrategySelectionStatus.OriginalRestoreFailed:
                LastAction = "Не удалось вернуть исходную стратегию после неудачного подбора.";
                OverallStatus = "Требуется внимание";
                OverallDetails = "NetPilot остановил автоматизацию. Проверьте движок в расширенном режиме.";
                break;

            default:
                LastAction = "Подтверждённая рабочая альтернатива не найдена; исходная стратегия восстановлена.";
                break;
        }
    }

    // Aggregate-only result: no endpoint, exception strings, paths or process arguments.
    private static string BuildStrategyDiagnosticSummary(StrategySelectionResult result)
    {
        var evaluations = result.Evaluations;
        if (evaluations.Count == 0)
        {
            return $"Итог: {result.Status}. Прогонов: 0.";
        }

        var confirmed = evaluations.Count(e => e.IsViable);
        var empty = evaluations.Count(e => e.ResourceResults.Count == 0);
        var total = evaluations.Sum(e => e.ResourceResults.Count);
        var healthy = evaluations.Sum(e => e.ResourceResults.Count(r => r.State == ResourceHealthState.Healthy));
        var degraded = evaluations.Sum(e => e.ResourceResults.Count(r => r.State == ResourceHealthState.Degraded));
        var unavailable = evaluations.Sum(e => e.ResourceResults.Count(r => r.State == ResourceHealthState.Unavailable));
        return $"Итог: {result.Status}. Прогонов: {evaluations.Count}; пригодных: {confirmed}; " +
            $"без ответов: {empty}. HTTPS-проверок: {total}; работает: {healthy}; " +
            $"ограничения: {degraded}; недоступно: {unavailable}. " +
            "Успех одного сервиса не означает, что стратегия работает для всех.";
    }

    private async Task RefreshEngineStatusAsync(CancellationToken cancellationToken)
    {
        try
        {
            var state = await _dashboardService.GetStrategyRuntimeStateAsync(cancellationToken);
            var running = state.IsRunning && !string.IsNullOrWhiteSpace(state.StrategyId);
            IsEngineRunning = running;
            EngineStatusBrush = running ? Brushes.MediumSeaGreen : Brushes.SlateGray;
            EngineStatus = running
                ? $"ByeDPI: запущен · стратегия {state.StrategyId}"
                : "ByeDPI: остановлен";
            EngineActionText = running ? "Остановить ByeDPI" : "Запустить ByeDPI";
        }
        catch (Exception exception) when (IsExpectedOperationalException(exception))
        {
            IsEngineRunning = false;
            EngineStatusBrush = Brushes.IndianRed;
            EngineStatus = "Проверенный пакет ByeDPI недоступен или ещё не установлен.";
            EngineActionText = "Запустить ByeDPI";
        }
    }

    private void HandleUnexpectedError(Exception exception)
    {
        OverallStatus = "Произошла ошибка";
        OverallDetails = "Операция остановлена. Подробности не выводятся в обычном режиме.";
        LastAction = IsAdvancedMode
            ? $"Код ошибки: {exception.GetType().Name}"
            : "Операция остановлена безопасно.";
        IsBusy = false;
    }

    private static bool IsExpectedOperationalException(Exception exception) =>
        exception is IpcProtocolException
            or StrategyRuntimeException
            or HttpRequestException
            or OperationCanceledException
            or InvalidOperationException;

    private static string GetUserMessage(Exception exception) =>
        exception switch
        {
            IpcProtocolException => "Недоступен изолированный привилегированный компонент NetPilot.",
            StrategyRuntimeException => "ByeDPI не смог выполнить операцию. NetPilot остановил её безопасно.",
            HttpRequestException => "Не удалось проверить доступность сети.",
            OperationCanceledException => "Операция была отменена.",
            _ => "Не удалось завершить операцию."
        };

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(_disposed, this);
}
