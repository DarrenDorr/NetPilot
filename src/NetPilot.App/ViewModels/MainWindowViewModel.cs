using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using NetPilot.App.Services;
using NetPilot.Automation;
using NetPilot.Core.Models;

namespace NetPilot.App.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IDashboardService _dashboardService;
    private readonly Dictionary<string, ResourceStatusViewModel> _resourceById;
    private readonly ResourceHealthHistory _healthHistory = new();
    private readonly AsyncCommand _checkCommand;
    private readonly AsyncCommand _findStrategyCommand;
    private readonly AsyncCommand _installEngineCommand;
    private readonly AsyncCommand _clearDiagnosticHistoryCommand;

    private string _overallStatus = "Готов к проверке";
    private string _overallDetails = "Проверьте доступность сервисов одной кнопкой.";
    private string _engineStatus = "Состояние сетевого движка ещё не получено.";
    private string _lastAction = "Действий ещё не выполнялось.";
    private bool _isBusy;
    private bool _isAdvancedMode;
    private bool _isEngineInstalled;
    private bool _canInstallEngine;
    private bool _canUseEngine;
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

        _checkCommand = new AsyncCommand(
            CheckNowAsync,
            () => !IsBusy,
            HandleUnexpectedError);

        _findStrategyCommand = new AsyncCommand(
            FindStrategyAsync,
            () => !IsBusy && CanUseEngine,
            HandleUnexpectedError);

        _installEngineCommand = new AsyncCommand(
            InstallEngineAsync,
            () => !IsBusy && CanInstallEngine,
            HandleUnexpectedError);

        _clearDiagnosticHistoryCommand = new AsyncCommand(
            ClearDiagnosticHistoryAsync,
            () => !IsBusy && DiagnosticHistory.Count > 0,
            HandleUnexpectedError);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Title => "NetPilot";
    public string Subtitle => "Менеджер сетевого доступа";

    public ObservableCollection<ResourceStatusViewModel> Resources { get; }
    public ResourceStatusViewModel Telegram { get; }
    public ObservableCollection<DiagnosticHistoryItemViewModel> DiagnosticHistory { get; } = [];
    public string DiagnosticHistorySummary => $"Записей в памяти: {_healthHistory.Snapshot().Count}";

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
                _checkCommand.RaiseCanExecuteChanged();
                _findStrategyCommand.RaiseCanExecuteChanged();
                _installEngineCommand.RaiseCanExecuteChanged();
                _clearDiagnosticHistoryCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsAdvancedMode
    {
        get => _isAdvancedMode;
        set => SetField(ref _isAdvancedMode, value);
    }

    public bool IsEngineInstalled
    {
        get => _isEngineInstalled;
        private set => SetField(ref _isEngineInstalled, value);
    }

    public bool CanInstallEngine
    {
        get => _canInstallEngine;
        private set
        {
            if (SetField(ref _canInstallEngine, value))
            {
                _installEngineCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool CanUseEngine
    {
        get => _canUseEngine;
        private set
        {
            if (SetField(ref _canUseEngine, value))
            {
                _findStrategyCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public ICommand CheckCommand => _checkCommand;
    public ICommand FindStrategyCommand => _findStrategyCommand;
    public ICommand InstallEngineCommand => _installEngineCommand;
    public ICommand ClearDiagnosticHistoryCommand => _clearDiagnosticHistoryCommand;

    public async Task InitializeAsync()
    {
        ThrowIfDisposed();
        await RefreshEngineInstallationAsync(CancellationToken.None);

        if (CanUseEngine)
        {
            await RefreshEngineStatusAsync(CancellationToken.None);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _healthHistory.Clear();
        DiagnosticHistory.Clear();
        _dashboardService.Dispose();
    }

    private Task ClearDiagnosticHistoryAsync()
    {
        ThrowIfDisposed();
        _healthHistory.Clear();
        DiagnosticHistory.Clear();
        NotifyDiagnosticHistoryChanged();
        LastAction = "Локальная история диагностики очищена.";
        return Task.CompletedTask;
    }

    private async Task InstallEngineAsync()
    {
        ThrowIfDisposed();
        IsBusy = true;
        OverallStatus = "Устанавливаем сетевой движок…";
        OverallDetails = "NetPilot устанавливает только версию движка, прошедшую security admission и закреплённую по точной идентичности.";
        LastAction = "Начата установка допущенного сетевого движка.";

        try
        {
            var result = await _dashboardService.InstallEngineAsync();
            await RefreshEngineInstallationAsync(CancellationToken.None);
            OverallStatus = "Сетевой движок установлен";
            OverallDetails = "Движок установлен. Его запуск остаётся отдельным явным действием и разрешается только при действующем admission.";
            LastAction = result.TemporaryCleanupCompleted
                ? "Установка завершена; временные файлы очищены."
                : "Установка завершена; временный каталог оставлен для последующей безопасной очистки.";
        }
        catch (Exception exception) when (IsExpectedOperationalException(exception))
        {
            OverallStatus = "Не удалось установить движок";
            OverallDetails = GetUserMessage(exception);
            LastAction = "Установка остановлена; неподтверждённый пакет не активирован.";
            await RefreshEngineInstallationAsync(CancellationToken.None);
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
            if (CanUseEngine)
            {
                await RefreshEngineStatusAsync(CancellationToken.None);
            }
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
        OverallStatus = "Подбираем рабочий профиль…";
        OverallDetails = "NetPilot сначала подтверждает проблему, затем проверяет ограниченное число заранее разрешённых профилей.";

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

        RecordHealthHistory(results);

        if (results.Count > 0 && results.All(result => result.State == ResourceHealthState.Healthy))
        {
            OverallStatus = "Доступ в порядке";
            OverallDetails = "Discord и YouTube отвечают на проверку NetPilot.";
        }
        else
        {
            OverallStatus = results.Any(result => result.State == ResourceHealthState.Unavailable)
                ? "Есть недоступные сервисы"
                : "Есть ограничения";
            OverallDetails = GetRecoveryGuidance(results);
        }

        if (!preserveActionText)
        {
            LastAction = "Состояние ресурсов обновлено.";
        }
    }

    private string GetRecoveryGuidance(IReadOnlyList<ResourceHealth> results) =>
        ResourceHealthFailureClassifier.ClassifyAggregate(results) switch
        {
            ResourceHealthFailureClass.DnsFailure =>
                "Обнаружена проблема DNS. Смена сетевого профиля автоматически не выполняется; проверьте подключение и DNS-настройки.",
            ResourceHealthFailureClass.ServiceResponse =>
                "Сервис отвечает по HTTPS, но вернул ошибку или ограничение. NetPilot не будет менять сетевой профиль только из-за такого ответа.",
            ResourceHealthFailureClass.SecurityOrConfiguration =>
                "Проверка ограничена политикой безопасности или конфигурацией NetPilot. Автоматическое сетевое вмешательство запрещено.",
            ResourceHealthFailureClass.RecoverableNetworkPath when CanUseEngine =>
                "Сбой похож на проблему сетевого пути. Можно запустить подбор заранее разрешённого профиля.",
            ResourceHealthFailureClass.RecoverableNetworkPath =>
                "Сбой похож на проблему сетевого пути, но безопасно допущенный движок пока недоступен. NetPilot не запустит непроверенный пакет.",
            ResourceHealthFailureClass.Unclassified =>
                "Причина сбоя недостаточно определена. NetPilot сохранит текущую конфигурацию и не будет автоматически менять профиль.",
            _ => "Часть сервисов отвечает нестабильно или с ограничениями."
        };

    private void RecordHealthHistory(IReadOnlyList<ResourceHealth> results)
    {
        _healthHistory.AddRange(results);
        DiagnosticHistory.Clear();

        foreach (var entry in _healthHistory.Snapshot().Reverse().Take(12))
        {
            var resourceName = _resourceById.TryGetValue(entry.ResourceId, out var resource)
                ? resource.DisplayName
                : entry.ResourceId;

            DiagnosticHistory.Add(
                DiagnosticHistoryItemViewModel.Create(entry, resourceName));
        }

        NotifyDiagnosticHistoryChanged();
    }

    private void NotifyDiagnosticHistoryChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DiagnosticHistorySummary)));
        _clearDiagnosticHistoryCommand.RaiseCanExecuteChanged();
    }

    private void ApplyStrategySelectionResult(StrategySelectionResult result)
    {
        switch (result.Status)
        {
            case StrategySelectionStatus.CurrentHealthy:
                LastAction = "Текущий профиль уже работает. Переключение не выполнялось.";
                break;

            case StrategySelectionStatus.TransientFailure:
                LastAction = "Обнаружен кратковременный сбой. Текущий профиль сохранён.";
                break;

            case StrategySelectionStatus.RecoveryNotApplicable:
                LastAction = result.DiagnosticCode switch
                {
                    "RecoverySuppressedDnsFailure" =>
                        "Обнаружена проблема DNS. Текущий профиль сохранён; перебор альтернатив не выполнялся.",
                    "RecoverySuppressedHttpResponse" =>
                        "Сервис отвечает по HTTPS, поэтому автоматическая смена сетевого профиля не выполнялась.",
                    "RecoverySuppressedSecurityOrConfiguration" =>
                        "Автоматическая смена профиля остановлена политикой безопасности или конфигурацией. Текущий профиль сохранён.",
                    "RecoverySuppressedUnclassifiedFailure" =>
                        "Причина сбоя недостаточно определена. NetPilot сохранил текущий профиль без перебора альтернатив.",
                    _ =>
                        "Причина сбоя не допускает автоматическую смену профиля. Текущий профиль сохранён."
                };
                break;

            case StrategySelectionStatus.Switched:
                LastAction = "NetPilot подтвердил рабочую альтернативу и переключил профиль.";
                break;

            case StrategySelectionStatus.NotRunning:
                LastAction = "Сетевой движок сейчас не запущен. Автоматический запуск не выполнялся.";
                OverallStatus = "Сетевой движок не запущен";
                OverallDetails = "Первичный запуск профиля выполняется только отдельным явным действием.";
                break;

            case StrategySelectionStatus.OriginalRestoreFailed:
                LastAction = "Не удалось вернуть исходный профиль после неудачного подбора.";
                OverallStatus = "Требуется внимание";
                OverallDetails = "NetPilot остановил автоматизацию. Проверьте движок в расширенном режиме.";
                break;

            default:
                LastAction = "Подтверждённая рабочая альтернатива не найдена; исходный профиль восстановлен.";
                break;
        }
    }

    private async Task RefreshEngineInstallationAsync(CancellationToken cancellationToken)
    {
        try
        {
            var state = await _dashboardService.GetEngineInstallationStateAsync(cancellationToken);
            IsEngineInstalled = state.IsInstalled;
            CanInstallEngine = !state.IsInstalled && state.CanInstall;
            CanUseEngine = state.IsInstalled && state.CanStart;

            if (!state.IsInstalled)
            {
                EngineStatus = state.CanInstall
                    ? "Сетевой движок: не установлен"
                    : "Сетевой движок: безопасная версия пока не допущена";
            }
            else if (!state.CanStart)
            {
                EngineStatus = string.IsNullOrWhiteSpace(state.Version)
                    ? "Legacy-движок установлен · запуск заблокирован проверкой безопасности"
                    : $"Legacy-движок {state.Version} установлен · запуск заблокирован проверкой безопасности";
            }
            else if (!string.IsNullOrWhiteSpace(state.Version))
            {
                EngineStatus = $"Сетевой движок {state.Version}: установлен";
            }
            else
            {
                EngineStatus = "Сетевой движок: установлен";
            }
        }
        catch (Exception exception) when (IsExpectedOperationalException(exception))
        {
            IsEngineInstalled = false;
            CanInstallEngine = false;
            CanUseEngine = false;
            EngineStatus = "Служба NetPilot недоступна или пользователь Windows не авторизован.";
        }
    }

    private async Task RefreshEngineStatusAsync(CancellationToken cancellationToken)
    {
        try
        {
            var state = await _dashboardService.GetStrategyRuntimeStateAsync(cancellationToken);
            EngineStatus = state.IsRunning && !string.IsNullOrWhiteSpace(state.StrategyId)
                ? $"Сетевой движок: запущен · профиль {state.StrategyId}"
                : "Сетевой движок: установлен · остановлен";
        }
        catch (Exception exception) when (IsExpectedOperationalException(exception))
        {
            EngineStatus = "Служба NetPilot недоступна или пользователь Windows не авторизован.";
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
        exception is EngineProvisioningException
            or StrategyRuntimeException
            or HttpRequestException
            or OperationCanceledException
            or InvalidOperationException;

    private static string GetUserMessage(Exception exception) =>
        exception switch
        {
            EngineProvisioningException engine when engine.Code == "UnauthorizedUser" =>
                "Текущая учётная запись Windows не имеет права управлять этой установкой NetPilot.",
            EngineProvisioningException engine when engine.Code == "EnrollmentRequired" =>
                "Установка NetPilot не завершила безопасную привязку пользователя. Выполните восстановление установки.",
            EngineProvisioningException =>
                "Не удалось связаться со службой NetPilot или выполнить защищённую операцию.",
            StrategyRuntimeException => "Служба не смогла выполнить операцию с сетевым профилем.",
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
