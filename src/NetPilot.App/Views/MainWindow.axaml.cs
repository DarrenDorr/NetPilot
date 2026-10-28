using Avalonia.Controls;
using Avalonia.Interactivity;
using NetPilot.App.ViewModels;

namespace NetPilot.App.Views;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Opened += OnOpened;
        Closed += OnClosed;
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
