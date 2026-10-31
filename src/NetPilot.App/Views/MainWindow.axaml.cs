using Avalonia.Controls;
using Avalonia.Interactivity;
using NetPilot.App.Services;
using NetPilot.App.ViewModels;

namespace NetPilot.App.Views;

public sealed partial class MainWindow : Window
{
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
        Opened += OnOpened;
        Closed += OnClosed;
    }

    private void SetSupportInfo(ApplicationSupportInfo supportInfo)
    {
        this.FindControl<TextBlock>("SupportVersionText")!.Text = supportInfo.Version;
        this.FindControl<TextBlock>("SupportPlatformText")!.Text = supportInfo.Platform;
        this.FindControl<TextBlock>("SupportArchitectureText")!.Text = supportInfo.Architecture;
    }

    private void ConfigureByeDpiExperimentalCard()
    {
        var card = this.FindControl<Border>("ByeDpiExperimentalCard")!;
        card.IsVisible = ByeDpiControl.IsExperimentalAvailable;
        this.FindControl<TextBlock>("ByeDpiEndpointText")!.Text = ByeDpiControl.ProxyEndpoint;
    }

    private async void OnOpened(object? sender, EventArgs eventArgs)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            try
            {
                await viewModel.InitializeAsync();
            }
            catch
            {
                // The window remains usable when optional platform state is unavailable.
            }
        }

        if (ByeDpiControl.IsExperimentalAvailable)
        {
            await RefreshByeDpiStatusAsync();
        }
    }

    private async void OnStartByeDpiClicked(object? sender, RoutedEventArgs eventArgs)
    {
        await RunByeDpiOperationAsync(
            cancellationToken => ByeDpiControl.StartAsync(cancellationToken),
            "Запускаем проверенный ByeDPI…",
            refreshResourcesWhenRunning: true);
    }

    private async void OnStopByeDpiClicked(object? sender, RoutedEventArgs eventArgs)
    {
        await RunByeDpiOperationAsync(
            cancellationToken => ByeDpiControl.StopAsync(cancellationToken),
            "Останавливаем ByeDPI…",
            refreshResourcesWhenRunning: false);
    }

    private async void OnRefreshByeDpiClicked(object? sender, RoutedEventArgs eventArgs)
    {
        await RefreshByeDpiStatusAsync();
    }

    private async Task RunByeDpiOperationAsync(
        Func<CancellationToken, Task<ByeDpiUiStatus>> operation,
        string progressText,
        bool refreshResourcesWhenRunning)
    {
        SetByeDpiButtonsEnabled(false, false);
        this.FindControl<TextBlock>("ByeDpiStatusText")!.Text = progressText;

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var status = await operation(timeout.Token);
            ApplyByeDpiStatus(status);

            if (refreshResourcesWhenRunning
                && status.State == ByeDpiUiState.Running
                && DataContext is MainWindowViewModel viewModel
                && viewModel.CheckCommand.CanExecute(null))
            {
                // AsyncCommand owns its exception boundary and busy state. The experimental
                // build's health transport is fixed to the reviewed ByeDPI SOCKS5 endpoint.
                viewModel.CheckCommand.Execute(null);
            }
        }
        catch (Exception exception) when (IsExpectedByeDpiUiFailure(exception))
        {
            ApplyByeDpiUiFailure();
        }
        catch (Exception)
        {
            // Never let an unexpected platform exception escape an async UI event handler and
            // terminate the desktop process. Platform-specific exception types stay below the
            // NetPilot.App boundary; the UI only reports a sanitized failure.
            ApplyByeDpiUiFailure();
        }
    }

    private async Task RefreshByeDpiStatusAsync()
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(6));
            ApplyByeDpiStatus(await ByeDpiControl.GetStatusAsync(timeout.Token));
        }
        catch (Exception exception) when (IsExpectedByeDpiUiFailure(exception))
        {
            ApplyByeDpiUiFailure("Состояние недоступно", "Защищённый IPC не подтвердил состояние experimental ByeDPI.");
        }
        catch (Exception)
        {
            ApplyByeDpiUiFailure("Состояние недоступно", "Не удалось безопасно обновить состояние ByeDPI. Интерфейс продолжает работу.");
        }
    }

    private static bool IsExpectedByeDpiUiFailure(Exception exception) =>
        exception is InvalidOperationException
            or IOException
            or UnauthorizedAccessException
            or OperationCanceledException;

    private void ApplyByeDpiUiFailure(
        string statusText = "Ошибка управления ByeDPI",
        string detailsText = "Операция остановлена безопасно. Нажмите обновление, чтобы повторно получить состояние службы.")
    {
        this.FindControl<TextBlock>("ByeDpiStatusText")!.Text = statusText;
        this.FindControl<TextBlock>("ByeDpiDetailsText")!.Text = detailsText;
        SetByeDpiButtonsEnabled(canStart: true, canStop: false);
    }

    private void ApplyByeDpiStatus(ByeDpiUiStatus status)
    {
        this.FindControl<TextBlock>("ByeDpiDetailsText")!.Text = status.Details;

        switch (status.State)
        {
            case ByeDpiUiState.Running:
                this.FindControl<TextBlock>("ByeDpiStatusText")!.Text = "NetPilot активен";
                SetByeDpiButtonsEnabled(false, true);
                break;
            case ByeDpiUiState.Stopped:
                this.FindControl<TextBlock>("ByeDpiStatusText")!.Text = "Защита выключена";
                SetByeDpiButtonsEnabled(true, false);
                break;
            default:
                this.FindControl<TextBlock>("ByeDpiStatusText")!.Text = "Состояние недоступно";
                SetByeDpiButtonsEnabled(false, false);
                break;
        }
    }

    private void SetByeDpiButtonsEnabled(bool canStart, bool canStop)
    {
        var startButton = this.FindControl<Button>("ByeDpiStartButton")!;
        var stopButton = this.FindControl<Button>("ByeDpiStopButton")!;

        startButton.IsEnabled = canStart;
        stopButton.IsEnabled = canStop;

        // The approved VPN-like control surface shows one power action at a time.
        // If the state is unavailable, keep the start glyph visible but disabled;
        // Refresh remains available as the recovery action.
        stopButton.IsVisible = canStop && !canStart;
        startButton.IsVisible = !stopButton.IsVisible;
    }

    private void OnClosed(object? sender, EventArgs eventArgs)
    {
        if (DataContext is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
