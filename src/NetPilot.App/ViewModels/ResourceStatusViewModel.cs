using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Media;
using NetPilot.Core.Models;

namespace NetPilot.App.ViewModels;

public sealed class ResourceStatusViewModel : INotifyPropertyChanged
{
    private string _statusText;
    private string _detailsText;
    private IBrush _statusBrush;
    private string _statusGlyph;

    public ResourceStatusViewModel(
        string id,
        string displayName,
        string statusText = "Не проверено",
        string detailsText = "")
    {
        Id = id;
        DisplayName = displayName;
        _statusText = statusText;
        _detailsText = detailsText;
        _statusBrush = Brushes.Gray;
        _statusGlyph = "●";
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Id { get; }
    public string DisplayName { get; }

    public string StatusText
    {
        get => _statusText;
        private set => SetField(ref _statusText, value);
    }

    public string DetailsText
    {
        get => _detailsText;
        private set => SetField(ref _detailsText, value);
    }

    public IBrush StatusBrush
    {
        get => _statusBrush;
        private set => SetField(ref _statusBrush, value);
    }

    public string StatusGlyph
    {
        get => _statusGlyph;
        private set => SetField(ref _statusGlyph, value);
    }

    public void SetChecking()
    {
        StatusText = "Проверка…";
        DetailsText = string.Empty;
        StatusBrush = Brushes.DodgerBlue;
        StatusGlyph = "●";
    }

    public void Apply(ResourceHealth health)
    {
        var latencyText = health.Latency is { } latency
            ? $"{Math.Max(1, (int)Math.Round(latency.TotalMilliseconds))} мс"
            : string.Empty;

        switch (health.State)
        {
            case ResourceHealthState.Healthy:
                // This confirms only the configured HTTPS health endpoint, not the app.
                StatusText = "HTTPS отвечает";
                DetailsText = latencyText;
                StatusBrush = Brushes.MediumSeaGreen;
                StatusGlyph = "●";
                break;

            case ResourceHealthState.Degraded:
                StatusText = "Есть ограничения";
                DetailsText = BuildSafeDiagnostic(health.DiagnosticCode, latencyText);
                StatusBrush = Brushes.DarkOrange;
                StatusGlyph = "●";
                break;

            case ResourceHealthState.Unavailable:
                StatusText = "HTTPS недоступен";
                DetailsText = BuildSafeDiagnostic(health.DiagnosticCode, string.Empty);
                StatusBrush = Brushes.IndianRed;
                StatusGlyph = "●";
                break;

            case ResourceHealthState.Checking:
                SetChecking();
                break;

            default:
                StatusText = "Не проверено";
                DetailsText = string.Empty;
                StatusBrush = Brushes.Gray;
                StatusGlyph = "●";
                break;
        }
    }

    // Only a fixed allowlist may be rendered; never show arbitrary diagnostic
    // strings originating from network responses or exception messages.
    private static string BuildSafeDiagnostic(string? code, string fallback) => code switch
    {
        "ByeDpiNotRunning" => "ByeDPI не запущен",
        "ProxyTimeout" => "Тайм-аут SOCKS5/HTTPS",
        "ProxyNameResolutionError" => "Ошибка разрешения имени",
        "ProxyConnectionError" => "Ошибка сетевого соединения",
        "ProxyTlsError" => "Ошибка защищённого соединения TLS",
        "ProxyTunnelError" => "Ошибка туннеля прокси",
        "ProxyHttpProtocolError" => "Ошибка протокола HTTP",
        "ProxyNetworkError" => "Сетевая ошибка (этап не определён)",
        "ProxyRedirectRejected" => "Перенаправление отклонено",
        "EndpointRejected" => "Адрес проверки не разрешён",
        "NoHealthEndpoints" => "Нет разрешённых адресов проверки",
        "ProxyHealthCheckFailed" => "Проверка через прокси завершилась ошибкой",
        "Http401" => "HTTP 401",
        "Http403" => "HTTP 403",
        "Http408" => "HTTP 408",
        "Http429" => "HTTP 429",
        "Http500" => "HTTP 500",
        "Http502" => "HTTP 502",
        "Http503" => "HTTP 503",
        "Http504" => "HTTP 504",
        _ => fallback
    };

    public void SetNotVerified()
    {
        StatusText = "Не проверено";
        DetailsText = "Нет результата HTTPS-проверки.";
        StatusBrush = Brushes.Gray;
        StatusGlyph = "○";
    }

    public void SetNotAvailableYet(string details)
    {
        StatusText = "Пока не подключён";
        DetailsText = details;
        StatusBrush = Brushes.Gray;
        StatusGlyph = "○";
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
