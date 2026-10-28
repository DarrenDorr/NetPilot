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
    private readonly AsyncCommand _checkCommand;
    private readonly AsyncCommand _findStrategyCommand;
    private readonly AsyncCommand _provisionEngineCommand;

    private string _overallStatus = "Готов к проверке";
    private string _overallDetails = "Проверьте доступность сервисов одной кнопкой.";
    private string _engineStatus = "Состояние сетевого движка ещё не получено.";
    private string _engineInstallationStatus = "Проверяем установку Zapret…";
    private string _engineActionText = "Установить Zapret";
    private string _lastAction = "Действий ещё не выполнялось.";
    private bool _isEngineInstalled;
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

        _checkCommand = new AsyncCommand(
            CheckNowAsync,
            () => !IsBusy,
            HandleUnexpectedError);

        _findStrategyCommand = new AsyncCommand(
            FindStrategyAsync,
            () => !IsBusy && IsEngineInstalled,
            HandleUnexpectedError);

        _provisionEngineCommand = new AsyncCommand(
            ProvisionEngineAsync,
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

    public string EngineInstallationStatus
    {
        get => _engineInstallationStatus;
        private set => SetField(ref _engineInstallationStatus, value);
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

    public bool IsEngineInstalled
    {
        get => _isEngineInstalled;
        private set
        {
            if (SetField(ref _isEngineInstalled, value))
            {
                _findStrategyCommand.RaiseCanExecuteChanged();
            }
        }
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
                _provisionEngineCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsAdvancedMode
    {
        get => _isAdvancedMode;
        set => SetField(ref _isAdvancedMode, value);
    }

    public ICommand CheckCommand => _checkCommand;
    public ICommand FindStrategyCommand => _findStrategyCommand;
    public ICommand ProvisionEngineCommand => _provisionEngineCommand;

    public async Task InitializeAsync()
    {
        ThrowIfDisposed();
        await RefreshInstallationStatusAsync(CancellationToken.None);
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

    private async Task ProvisionEngineAsync()
    {
        ThrowIfDisposed();
        IsBusy = true;
        var wasInstalled = IsEngineInstalled;
        OverallStatus = wasInstalled
            ? "Обновляем официальный Zapret…"
            : "Устанавливаем официальный Zapret…";
        OverallDetails = "NetPilot использует только закреплённый официальный источник Flowseal и проверяет пакет до установки.";

        try
        {
            var outcome = await _dashboardService.InstallOrUpdateOfficialZapretLatestAsync();
            ApplyInstallationStatus(outcome.Installation);

            if (string.Equals(outcome.Code, "AlreadyLatest", StringComparison.Ordinal))
            {
                OverallStatus = "Zapret уже актуален";
                OverallDetails = $"Установлена актуальная версия {outcome.Installation.Version}.";
                LastAction = "Обновление не требовалось; файлы движка не заменялись.";
            }
            else if (outcome.Details?.RuntimeRestore == IpcZapretRuntimeRestoreState.Failed)
            {
                OverallStatus = "Zapret обновлён, но требуется внимание";
                OverallDetails = "Новая версия установлена, однако прежнюю активную стратегию не удалось восстановить автоматически.";
                LastAction = "Автоматическая подмена стратегии не выполнялась.";
            }
            else
            {
                OverallStatus = wasInstalled ? "Zapret обновлён" : "Zapret установлен";
                OverallDetails = $"Официальная версия {outcome.Installation.Version} установлена и проверена.";
                LastAction = outcome.Details?.RuntimeRestore == IpcZapretRuntimeRestoreState.Restored
                    ? "Обновление завершено; прежняя активная стратегия восстановлена."
                    : "Установка движка завершена.";
            }

            await RefreshEngineStatusAsync(CancellationToken.None);
        }
        catch (Exception exception) when (IsExpectedOperationalException(exception))
        {
            OverallStatus = wasInstalled ? "Обновление не выполнено" : "Установка не выполнена";
            OverallDetails = GetUserMessage(exception);
            LastAction = "Provisioning остановлен безопасно; произвольный источник или fallback не использовался.";
            await RefreshInstallationStatusAsync(CancellationToken.None);
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
            await RefreshInstallationStatusAsync(CancellationToken.None);
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
        OverallDetails = "NetPilot сначала подтверждает проблему, затем проверяет ограниченное число альтернатив.";

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
            OverallStatus = "Проверенные сервисы работают";
            OverallDetails = "Discord и YouTube отвечают на проверку NetPilot.";
        }
        else if (results.Any(result => result.State == ResourceHealthState.Unavailable))
        {
            OverallStatus = "Есть недоступные сервисы";
            OverallDetails = IsEngineInstalled
                ? "Можно запустить подбор стратегии или повторить проверку позже."
                : "Сначала установите Zapret, если для доступа требуется сетевой движок.";
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
                LastAction = "Zapret сейчас не запущен. Автоматический запуск не выполнялся.";
                OverallStatus = "Сетевой движок не запущен";
                OverallDetails = "Запустите выбранную стратегию после установки движка.";
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

    private async Task RefreshInstallationStatusAsync(CancellationToken cancellationToken)
    {
        try
        {
            var installation = await _dashboardService.GetZapretInstallationStatusAsync(cancellationToken);
            ApplyInstallationStatus(installation);
        }
        catch (Exception exception) when (IsExpectedOperationalException(exception))
        {
            IsEngineInstalled = false;
            EngineInstallationStatus = "Служба NetPilot недоступна или состояние установки не подтверждено.";
            EngineActionText = "Установить Zapret";
        }
    }

    private void ApplyInstallationStatus(IpcZapretInstallationStatus installation)
    {
        IsEngineInstalled = installation.IsInstalled;
        EngineInstallationStatus = installation.IsInstalled
            ? $"Zapret установлен · версия {installation.Version}"
            : "Zapret ещё не установлен";
        EngineActionText = installation.IsInstalled
            ? "Обновить Zapret"
            : "Установить Zapret";
    }

    private async Task RefreshEngineStatusAsync(CancellationToken cancellationToken)
    {
        try
        {
            var state = await _dashboardService.GetStrategyRuntimeStateAsync(cancellationToken);
            EngineStatus = state.IsRunning && !string.IsNullOrWhiteSpace(state.StrategyId)
                ? $"Zapret: запущен · стратегия {state.StrategyId}"
                : "Zapret: остановлен";
        }
        catch (Exception exception) when (IsExpectedOperationalException(exception))
        {
            EngineStatus = "Служба NetPilot недоступна или движок ещё не установлен.";
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
            IpcProtocolException ipc when ipc.Code == "EngineOwnedByAnotherUser" =>
                "Zapret сейчас управляется другой Windows-сессией. NetPilot не будет вмешиваться в её соединение.",
            IpcProtocolException ipc when ipc.Code == "UpdateNetworkError" =>
                "Не удалось получить официальный релиз Zapret. Текущая установка не заменена.",
            IpcProtocolException ipc when ipc.Code == "OperationTimeout" =>
                "Операция превысила безопасный лимит времени и была остановлена.",
            IpcProtocolException => "Служба NetPilot не смогла выполнить безопасную операцию.",
            StrategyRuntimeException => "Служба не смогла выполнить операцию с сетевой стратегией.",
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
