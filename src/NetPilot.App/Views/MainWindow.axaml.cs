using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using NetPilot.App.Services;
using NetPilot.App.ViewModels;

namespace NetPilot.App.Views;

public sealed partial class MainWindow : Window
{
    private ByeDpiUiState _byeDpiState = ByeDpiUiState.Unavailable;
    private bool _byeDpiBusy;

    private IByeDpiControlService ByeDpiControl => AppComposition.ByeDpiControlService;

    public MainWindow()
        : this(ApplicationSupportInfo.Unknown)
    {
    }

    internal MainWindow(ApplicationSupportInfo supportInfo)
    {
        ArgumentNullException.ThrowIfNull(supportInfo);

        InitializeComponent();
        SetSupportInfo(supportInfo);
        ConfigureByeDpiExperimentalCard();
        SyncShellMode(advanced: false);
        ShowExpertPage("main");
        Opened += OnOpened;
        Closed += OnClosed;
    }

    private void SetSupportInfo(ApplicationSupportInfo supportInfo)
    {
        this.FindControl<TextBlock>("SupportVersionText")!.Text = supportInfo.Version;
        this.FindControl<TextBlock>("SupportPlatformText")!.Text = supportInfo.Platform;
        this.FindControl<TextBlock>("SupportArchitectureText")!.Text = supportInfo.Architecture;
        this.FindControl<TextBlock>("AboutVersionText")!.Text = $"Версия {supportInfo.Version}";
    }

    private void ConfigureByeDpiExperimentalCard()
    {
        this.FindControl<Border>("ByeDpiExperimentalCard")!.IsVisible = ByeDpiControl.IsExperimentalAvailable;
        this.FindControl<TextBlock>("ByeDpiEndpointText")!.Text = ByeDpiControl.ProxyEndpoint;
        SetPowerPresentation();
    }

    private async void OnOpened(object? sender, EventArgs eventArgs)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            SyncShellMode(viewModel.IsAdvancedMode);
            try
            {
                await viewModel.InitializeAsync();
            }
            catch
            {
                // The desktop shell remains usable when optional platform state is unavailable.
            }
        }

        if (ByeDpiControl.IsExperimentalAvailable)
        {
            await RefreshByeDpiStatusAsync();
        }
    }

    private async void OnByeDpiPowerClicked(object? sender, RoutedEventArgs eventArgs)
    {
        if (_byeDpiBusy || !ByeDpiControl.IsExperimentalAvailable)
        {
            return;
        }

        switch (_byeDpiState)
        {
            case ByeDpiUiState.Running:
                await RunByeDpiOperationAsync(
                    cancellationToken => ByeDpiControl.StopAsync(cancellationToken),
                    "Останавливаем NetPilot…",
                    refreshResourcesWhenRunning: false);
                break;

            case ByeDpiUiState.Stopped:
                await RunByeDpiOperationAsync(
                    cancellationToken => ByeDpiControl.StartAsync(cancellationToken),
                    "Запускаем стратегию ByeDPI…",
                    refreshResourcesWhenRunning: true);
                break;

            default:
                await RefreshByeDpiStatusAsync();
                break;
        }
    }

    private async void OnRefreshByeDpiClicked(object? sender, RoutedEventArgs eventArgs)
    {
        if (!_byeDpiBusy)
        {
            await RefreshByeDpiStatusAsync();
        }
    }

    private void OnShowAdvancedClicked(object? sender, RoutedEventArgs eventArgs)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.IsAdvancedMode = true;
        }
        SyncShellMode(advanced: true);
    }

    private void OnReturnCompactClicked(object? sender, RoutedEventArgs eventArgs)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.IsAdvancedMode = false;
        }
        SyncShellMode(advanced: false);
    }

    private void OnExpertModeChanged(object? sender, RoutedEventArgs eventArgs)
    {
        var advanced = sender is ToggleSwitch toggle && toggle.IsChecked == true;
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.IsAdvancedMode = advanced;
        }
        SyncShellMode(advanced);
    }

    private void OnNavigationClicked(object? sender, RoutedEventArgs eventArgs)
    {
        if (sender is Button button && button.Tag is string page)
        {
            ShowExpertPage(page);
        }
    }

    private async Task RunByeDpiOperationAsync(
        Func<CancellationToken, Task<ByeDpiUiStatus>> operation,
        string progressText,
        bool refreshResourcesWhenRunning)
    {
        _byeDpiBusy = true;
        this.FindControl<TextBlock>("ByeDpiStatusText")!.Text = progressText;
        this.FindControl<TextBlock>("ExpertStatusText")!.Text = progressText;
        SetPowerPresentation();

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            var status = await operation(timeout.Token);
            ApplyByeDpiStatus(status);

            if (refreshResourcesWhenRunning
                && status.State == ByeDpiUiState.Running
                && DataContext is MainWindowViewModel viewModel
                && viewModel.CheckCommand.CanExecute(null))
            {
                viewModel.CheckCommand.Execute(null);
            }
        }
        catch (Exception exception) when (IsExpectedByeDpiUiFailure(exception))
        {
            ApplyByeDpiUiFailure();
        }
        catch (Exception)
        {
            // A platform exception must never escape an async UI event handler and terminate
            // the desktop process. Native/runtime details remain below the application boundary.
            ApplyByeDpiUiFailure();
        }
        finally
        {
            _byeDpiBusy = false;
            SetPowerPresentation();
        }
    }

    private async Task RefreshByeDpiStatusAsync()
    {
        _byeDpiBusy = true;
        SetPowerPresentation();

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(6));
            ApplyByeDpiStatus(await ByeDpiControl.GetStatusAsync(timeout.Token));
        }
        catch (Exception exception) when (IsExpectedByeDpiUiFailure(exception))
        {
            ApplyByeDpiUiFailure(
                "Состояние недоступно",
                "Защищённый IPC не подтвердил состояние ByeDPI. Нажмите обновление и повторите проверку.");
        }
        catch (Exception)
        {
            ApplyByeDpiUiFailure(
                "Состояние недоступно",
                "Не удалось безопасно обновить состояние ByeDPI. Интерфейс продолжает работу.");
        }
        finally
        {
            _byeDpiBusy = false;
            SetPowerPresentation();
        }
    }

    private static bool IsExpectedByeDpiUiFailure(Exception exception) =>
        exception is InvalidOperationException
            or IOException
            or UnauthorizedAccessException
            or OperationCanceledException;

    private void ApplyByeDpiUiFailure(
        string statusText = "Ошибка запуска",
        string detailsText = "Операция остановлена безопасно. Проверьте состояние службы и повторите попытку.")
    {
        _byeDpiState = ByeDpiUiState.Unavailable;
        this.FindControl<TextBlock>("ByeDpiStatusText")!.Text = statusText;
        this.FindControl<TextBlock>("ByeDpiDetailsText")!.Text = detailsText;
        this.FindControl<TextBlock>("ExpertStatusText")!.Text = "NetPilot требует проверки";
        this.FindControl<TextBlock>("ExpertStatusDetailsText")!.Text = detailsText;
        SetPowerPresentation();
    }

    private void ApplyByeDpiStatus(ByeDpiUiStatus status)
    {
        _byeDpiState = status.State;
        this.FindControl<TextBlock>("ByeDpiDetailsText")!.Text = status.Details;
        this.FindControl<TextBlock>("ExpertStatusDetailsText")!.Text = status.Details;

        switch (status.State)
        {
            case ByeDpiUiState.Running:
                this.FindControl<TextBlock>("ByeDpiStatusText")!.Text = "NetPilot работает";
                this.FindControl<TextBlock>("ExpertStatusText")!.Text = "NetPilot работает";
                break;
            case ByeDpiUiState.Stopped:
                this.FindControl<TextBlock>("ByeDpiStatusText")!.Text = "NetPilot не запущен";
                this.FindControl<TextBlock>("ExpertStatusText")!.Text = "NetPilot остановлен";
                break;
            default:
                this.FindControl<TextBlock>("ByeDpiStatusText")!.Text = "Состояние не подтверждено";
                this.FindControl<TextBlock>("ExpertStatusText")!.Text = "Состояние не подтверждено";
                break;
        }

        SetPowerPresentation();
    }

    private void SetPowerPresentation()
    {
        var powerButton = this.FindControl<Button>("ByeDpiPowerButton")!;
        var expertButton = this.FindControl<Button>("ExpertPowerButton")!;
        var label = this.FindControl<TextBlock>("ByeDpiPowerLabel")!;
        var ring = this.FindControl<Border>("ByeDpiPowerRing")!;
        var core = this.FindControl<Border>("ByeDpiPowerCore")!;

        powerButton.IsVisible = true;
        powerButton.IsEnabled = ByeDpiControl.IsExperimentalAvailable && !_byeDpiBusy;
        expertButton.IsEnabled = powerButton.IsEnabled;

        if (_byeDpiBusy)
        {
            label.Text = _byeDpiState == ByeDpiUiState.Running ? "ОСТАНОВКА…" : "ЗАПУСК…";
            expertButton.Content = "Выполняется…";
            ApplyPowerColors(ring, core, "#2F80C9", "#12304D");
            return;
        }

        switch (_byeDpiState)
        {
            case ByeDpiUiState.Running:
                label.Text = "РАБОТАЕТ";
                expertButton.Content = "Остановить";
                ApplyPowerColors(ring, core, "#35E58C", "#0E442E");
                break;
            case ByeDpiUiState.Stopped:
                label.Text = "ЗАПУСТИТЬ";
                expertButton.Content = "Запустить";
                ApplyPowerColors(ring, core, "#3A9CFF", "#12345A");
                break;
            default:
                label.Text = "ОБНОВИТЬ";
                expertButton.Content = "Проверить состояние";
                ApplyPowerColors(ring, core, "#F2A93B", "#4A3514");
                break;
        }
    }

    private static void ApplyPowerColors(Border ring, Border core, string accent, string fill)
    {
        var accentBrush = new SolidColorBrush(Color.Parse(accent));
        ring.BorderBrush = accentBrush;
        core.BorderBrush = accentBrush;
        core.Background = new SolidColorBrush(Color.Parse(fill));
    }

    private void SyncShellMode(bool advanced)
    {
        this.FindControl<Grid>("CompactShell")!.IsVisible = !advanced;
        this.FindControl<Grid>("ExpertShell")!.IsVisible = advanced;
        var toggle = this.FindControl<ToggleSwitch>("ExpertModeToggle")!;
        if (toggle.IsChecked != advanced)
        {
            toggle.IsChecked = advanced;
        }
    }

    private void ShowExpertPage(string page)
    {
        var main = this.FindControl<Grid>("ExpertMainPanel")!;
        var strategies = this.FindControl<Grid>("StrategiesPanel")!;
        var settings = this.FindControl<Grid>("SettingsPanel")!;
        var about = this.FindControl<Grid>("AboutPanel")!;

        main.IsVisible = page is "main" or "services" or "engines" or "diagnostics" or "journal";
        strategies.IsVisible = page == "strategies";
        settings.IsVisible = page == "settings";
        about.IsVisible = page == "about";
    }

    private void OnClosed(object? sender, EventArgs eventArgs)
    {
        if (DataContext is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
