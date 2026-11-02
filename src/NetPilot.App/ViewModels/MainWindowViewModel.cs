using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
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
    private readonly AsyncCommand _findStrategyCommand;

    private string _overallStatus = "Готов к проверке";
    private string _overallDetails = "Проверьте доступность сервисов одной кнопкой.";
    private string _engineStatus = "Состояние сетевого движка ещё не получено.";
    private string _engineActionText = "Запустить ByeDPI";
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

    public string LastAction
    {
        get => _lastAction;
        private set => SetField(ref _lastAction, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetField(ref _isBusy, value))
            {
                _toggleEngineCommand.RaiseCanExecuteChanged();
                _checkCommand.RaiseCanExecuteChanged();
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

    private async Task CheckNowAsync()
    {
        ThrowIfDisposed();
        IsBusy = true;
        OverallStatus = "Проверяем доступность…";
        OverallDetails = "NetPilot проверяет только публичные HTTPS-адреса без авторизации и cookie.";

        foreach (var resource in _resourceById.Values)
        {
            resource.SetChecking();
        }

        try
        {
            var results = await _dashboardService.CheckResourcesAsync();
            ApplyHealthResults(results);
            await RefreshEngineStatusAsync(CancellationToken.None);
            LastAction = "Проверка доступности завершена.";
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
        OverallDetails = "NetPilot сначала подтверждает проблему, затем проверяет ограниченное число альтернатив через локальный ByeDPI SOCKS5.";

        foreach (var resource in _resourceById.Values)
        {
            resource.SetChecking();
        }

        try
        {
            var result = await _dashboardService.FindWorkingStrategyAsync();
            ApplyStrategySelectionResult(result);

            var health = await _dashboardService.CheckResourcesAsync();
            ApplyHealthResults(health, preserveActionText: true);
            await RefreshEngineStatusAsync(CancellationToken.None);
        }
        catch (Exception exception) when (IsExpectedOperationalException(exception))
        {
            OverallStatus = "Подбор не выполнен";
            OverallDetails = GetUserMessage(exception);
            LastAction = "Автоматический подбор остановлен без скрытого продолжения.";
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
            OverallDetails = "Discord и YouTube отвечают на проверку NetPilot.";
        }
        else if (results.Any(result => result.State == ResourceHealthState.Unavailable))
        {
            OverallStatus = "Есть недоступные сервисы";
            OverallDetails = "Можно запустить ByeDPI, подобрать стратегию или повторить проверку позже.";
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

    private async Task RefreshEngineStatusAsync(CancellationToken cancellationToken)
    {
        try
        {
            var state = await _dashboardService.GetStrategyRuntimeStateAsync(cancellationToken);
            var running = state.IsRunning && !string.IsNullOrWhiteSpace(state.StrategyId);
            EngineStatus = running
                ? $"ByeDPI: запущен · стратегия {state.StrategyId}"
                : "ByeDPI: остановлен";
            EngineActionText = running ? "Остановить ByeDPI" : "Запустить ByeDPI";
        }
        catch (Exception exception) when (IsExpectedOperationalException(exception))
        {
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
