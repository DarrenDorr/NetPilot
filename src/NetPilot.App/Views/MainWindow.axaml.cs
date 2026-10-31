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
            "Запускаем проверенный ByeDPI…");
    }

    private async void OnStopByeDpiClicked(object? sender, RoutedEventArgs eventArgs)
    {
        await RunByeDpiOperationAsync(
            cancellationToken => ByeDpiControl.StopAsync(cancellationToken),
            "Останавливаем ByeDPI…");
    }

    private async void OnRefreshByeDpiClicked(object? sender, RoutedEventArgs eventArgs)
    {
        await RefreshByeDpiStatusAsync();
    }

    private async Task RunByeDpiOperationAsync(
        Func<CancellationToken, Task<ByeDpiUiStatus>> operation,
        string progressText)
    {
        SetByeDpiButtonsEnabled(false, false);
        this.FindControl<TextBlock>("ByeDpiStatusText")!.Text = progressText;

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            ApplyByeDpiStatus(await operation(timeout.Token));
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
                or IOException
                or UnauthorizedAccessException
                or OperationCanceledException)
        {
            this.FindControl<TextBlock>("ByeDpiStatusText")!.Text = "Ошибка управления ByeDPI";
            this.FindControl<TextBlock>("ByeDpiDetailsText")!.Text =
                "Операция остановлена безопасно. Проверьте службу NetPilot и целостность experimental-пакета.";
            SetByeDpiButtonsEnabled(true, true);
        }
    }

    private async Task RefreshByeDpiStatusAsync()
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(6));
            ApplyByeDpiStatus(await ByeDpiControl.GetStatusAsync(timeout.Token));
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
                or IOException
                or UnauthorizedAccessException
                or OperationCanceledException)
        {
            this.FindControl<TextBlock>("ByeDpiStatusText")!.Text = "Состояние недоступно";
            this.FindControl<TextBlock>("ByeDpiDetailsText")!.Text =
                "Защищённый IPC не подтвердил состояние experimental ByeDPI.";
            SetByeDpiButtonsEnabled(true, true);
        }
    }

    private void ApplyByeDpiStatus(ByeDpiUiStatus status)
    {
        this.FindControl<TextBlock>("ByeDpiDetailsText")!.Text = status.Details;

        switch (status.State)
        {
            case ByeDpiUiState.Running:
                this.FindControl<TextBlock>("ByeDpiStatusText")!.Text = "Работает";
                SetByeDpiButtonsEnabled(false, true);
                break;
            case ByeDpiUiState.Stopped:
                this.FindControl<TextBlock>("ByeDpiStatusText")!.Text = "Остановлен";
                SetByeDpiButtonsEnabled(true, false);
                break;
            default:
                this.FindControl<TextBlock>("ByeDpiStatusText")!.Text = "Недоступен";
                SetByeDpiButtonsEnabled(false, false);
                break;
        }
    }

    private void SetByeDpiButtonsEnabled(bool canStart, bool canStop)
    {
        this.FindControl<Button>("ByeDpiStartButton")!.IsEnabled = canStart;
        this.FindControl<Button>("ByeDpiStopButton")!.IsEnabled = canStop;
    }

    private void OnClosed(object? sender, EventArgs eventArgs)
    {
        if (DataContext is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
