using Avalonia.Controls;
using Avalonia.Interactivity;
using NetPilot.App.Services;
using NetPilot.App.ViewModels;

namespace NetPilot.App.Views;

public sealed partial class MainWindow : Window
{
    public MainWindow()
        : this(ApplicationSupportInfo.Unknown)
    {
    }

    internal MainWindow(ApplicationSupportInfo supportInfo)
    {
        ArgumentNullException.ThrowIfNull(supportInfo);

        InitializeComponent();
        SetSupportInfo(supportInfo);
        Opened += OnOpened;
        Closed += OnClosed;
    }

    private void SetSupportInfo(ApplicationSupportInfo supportInfo)
    {
        this.FindControl<TextBlock>("SupportVersionText")!.Text = supportInfo.Version;
        this.FindControl<TextBlock>("SupportPlatformText")!.Text = supportInfo.Platform;
        this.FindControl<TextBlock>("SupportArchitectureText")!.Text = supportInfo.Architecture;
    }

    private async void OnOpened(object? sender, EventArgs eventArgs)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        try
        {
            await viewModel.InitializeAsync();
        }
        catch
        {
            // InitializeAsync only reads optional platform state. The window must remain usable
            // for direct public health checks even when NetPilot.Service is not installed yet.
        }
    }

    private void OnClosed(object? sender, EventArgs eventArgs)
    {
        if (DataContext is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
