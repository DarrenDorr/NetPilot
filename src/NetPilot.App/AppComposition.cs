using Avalonia;
using NetPilot.App.Services;
using NetPilot.App.ViewModels;

namespace NetPilot.App;

public static class AppComposition
{
    private static Func<MainWindowViewModel>? _mainWindowFactory;
    private static ApplicationSupportInfo _supportInfo = ApplicationSupportInfo.Unknown;
    private static IByeDpiControlService _byeDpiControlService = UnavailableByeDpiControlService.Instance;

    public static void Configure(
        Func<MainWindowViewModel> mainWindowFactory,
        ApplicationSupportInfo? supportInfo = null,
        IByeDpiControlService? byeDpiControlService = null)
    {
        ArgumentNullException.ThrowIfNull(mainWindowFactory);

        if (_mainWindowFactory is not null)
        {
            throw new InvalidOperationException("Композиция NetPilot.App уже настроена.");
        }

        _mainWindowFactory = mainWindowFactory;
        _supportInfo = supportInfo ?? ApplicationSupportInfo.Unknown;
        _byeDpiControlService = byeDpiControlService ?? UnavailableByeDpiControlService.Instance;
    }

    internal static MainWindowViewModel CreateMainWindowViewModel() =>
        _mainWindowFactory?.Invoke()
        ?? throw new InvalidOperationException(
            "Платформенный host не настроил NetPilot.App перед запуском интерфейса.");

    internal static ApplicationSupportInfo SupportInfo => _supportInfo;

    internal static IByeDpiControlService ByeDpiControlService => _byeDpiControlService;

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
