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
        var diagnosticText = GetDiagnosticText(health.DiagnosticCode);
        var detailsText = CombineDetails(diagnosticText, latencyText);

        switch (health.State)
        {
            case ResourceHealthState.Healthy:
                StatusText = "Работает";
                DetailsText = latencyText;
                StatusBrush = Brushes.MediumSeaGreen;
                StatusGlyph = "●";
                break;

            case ResourceHealthState.Degraded:
                StatusText = "Есть ограничения";
                DetailsText = detailsText;
                StatusBrush = Brushes.DarkOrange;
                StatusGlyph = "●";
                break;

            case ResourceHealthState.Unavailable:
                StatusText = "Недоступно";
                DetailsText = detailsText;
                StatusBrush = Brushes.IndianRed;
                StatusGlyph = "●";
                break;

            case ResourceHealthState.Checking:
                SetChecking();
                break;

            default:
                StatusText = "Не проверено";
                DetailsText = diagnosticText;
                StatusBrush = Brushes.Gray;
                StatusGlyph = "●";
                break;
        }
    }

    public void SetNotAvailableYet(string details)
    {
        StatusText = "Пока не подключён";
        DetailsText = details;
        StatusBrush = Brushes.Gray;
        StatusGlyph = "○";
    }

    private static string GetDiagnosticText(string? diagnosticCode)
    {
        if (string.IsNullOrWhiteSpace(diagnosticCode))
        {
            return string.Empty;
        }

        return diagnosticCode switch
        {
            "DnsResolutionFailed" => "Проблема с разрешением DNS-имени.",
            "TcpConnectFailed" => "Не удалось установить сетевое соединение.",
            "TlsHandshakeFailed" => "Не удалось установить защищённое HTTPS-соединение.",
            "Timeout" => "Превышено время ожидания ответа.",
            "EndpointRejected" => "Адрес проверки отклонён политикой безопасности NetPilot.",
            "NoHealthEndpoints" => "Для сервиса не настроена безопасная проверка.",
            "TooManyRedirects" => "Сервис вернул слишком много перенаправлений.",
            "RedirectWithoutLocation" => "Сервис вернул некорректное перенаправление.",
            "NetworkError" => "Ошибка сетевого соединения.",
            "HealthCheckFailed" => "Проверка доступности не завершена.",
            _ when TryGetHttpStatusCode(diagnosticCode, out var statusCode) =>
                $"Сервис ответил HTTP {statusCode}.",
            _ => "Доступность сервиса не подтверждена."
        };
    }

    private static bool TryGetHttpStatusCode(string diagnosticCode, out int statusCode)
    {
        statusCode = 0;
        return diagnosticCode.Length == 7
            && diagnosticCode.StartsWith("Http", StringComparison.Ordinal)
            && int.TryParse(diagnosticCode.AsSpan(4), out statusCode)
            && statusCode is >= 100 and <= 599;
    }

    private static string CombineDetails(string diagnosticText, string latencyText)
    {
        if (string.IsNullOrEmpty(diagnosticText))
        {
            return latencyText;
        }

        if (string.IsNullOrEmpty(latencyText))
        {
            return diagnosticText;
        }

        return $"{diagnosticText} · {latencyText}";
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
