using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input.Platform;
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
            // Startup state discovery is optional. The window remains usable even when the
            // reviewed ByeDPI package is missing or rejected by the fail-closed runtime.
        }
    }

    private async void OnCopySocks5AddressClicked(object? sender, RoutedEventArgs eventArgs)
    {
        if (DataContext is not MainWindowViewModel viewModel || !viewModel.IsEngineRunning)
        {
            CopySocks5Feedback.Text = "Запустите ByeDPI перед копированием.";
            return;
        }

        // Fail closed: never copy unconfirmed status strings or arbitrary endpoints.
        var address = viewModel.LocalSocks5Address;
        const string prefix = "127.0.0.1:";
        if (!address.StartsWith(prefix, StringComparison.Ordinal)
            || !int.TryParse(address.AsSpan(prefix.Length), out var port)
            || port is < 1 or > 65535
            || !string.Equals(address, $"{prefix}{port}", StringComparison.Ordinal))
        {
            CopySocks5Feedback.Text = "Локальный адрес SOCKS5 не подтверждён.";
            return;
        }

        var clipboard = Clipboard;
        if (clipboard is null)
        {
            CopySocks5Feedback.Text = "Буфер обмена недоступен.";
            return;
        }

        try
        {
            await clipboard.SetTextAsync(address);
            CopySocks5Feedback.Text = "Адрес скопирован. Настройте поддерживаемое приложение вручную.";
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            CopySocks5Feedback.Text = "Не удалось скопировать адрес.";
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
