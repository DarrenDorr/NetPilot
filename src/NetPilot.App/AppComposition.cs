using Avalonia;
using NetPilot.App.ViewModels;

namespace NetPilot.App;

public static class AppComposition
{
    private static Func<MainWindowViewModel>? _mainWindowFactory;

    public static void Configure(Func<MainWindowViewModel> mainWindowFactory)
    {
        ArgumentNullException.ThrowIfNull(mainWindowFactory);

        if (_mainWindowFactory is not null)
        {
            throw new InvalidOperationException("Композиция NetPilot.App уже настроена.");
        }

        _mainWindowFactory = mainWindowFactory;
    }

    internal static MainWindowViewModel CreateMainWindowViewModel() =>
        _mainWindowFactory?.Invoke()
        ?? throw new InvalidOperationException(
            "Платформенный host не настроил NetPilot.App перед запуском интерфейса.");

    public static AppBuilder BuildAvaloniaApp()
    {
        var builder = AppBuilder.Configure<App>()
            .UsePlatformDetect();

#if DEBUG
        builder = builder.LogToTrace();
#endif

        return builder;
    }
}
