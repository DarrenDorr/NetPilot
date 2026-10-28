using NetPilot.Core.Models;

namespace NetPilot.App.ViewModels;

public sealed record DiagnosticHistoryItemViewModel(
    string TimeText,
    string ResourceName,
    string StateText,
    string DetailsText)
{
    public static DiagnosticHistoryItemViewModel Create(
        ResourceHealthHistoryEntry entry,
        string resourceName)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceName);

        var stateText = entry.State switch
        {
            ResourceHealthState.Healthy => "Работает",
            ResourceHealthState.Degraded => "Есть ограничения",
            ResourceHealthState.Unavailable => "Недоступно",
            _ => "Неизвестно"
        };

        var diagnosticText = GetDiagnosticText(entry.DiagnosticCode);
        var latencyText = entry.Latency is { } latency
            ? $"{Math.Max(1, (int)Math.Round(latency.TotalMilliseconds))} мс"
            : string.Empty;
        var details = CombineDetails(diagnosticText, latencyText, entry.DiagnosticCode);

        return new DiagnosticHistoryItemViewModel(
            entry.CheckedAt.ToLocalTime().ToString("HH:mm:ss"),
            resourceName,
            stateText,
            details);
    }

    private static string GetDiagnosticText(string? diagnosticCode) =>
        diagnosticCode switch
        {
            null => string.Empty,
            "DnsResolutionFailed" => "DNS",
            "TcpConnectFailed" => "TCP-соединение",
            "TlsHandshakeFailed" => "HTTPS/TLS",
            "Timeout" => "Время ожидания превышено",
            "EndpointRejected" => "Endpoint отклонён политикой безопасности",
            "NoHealthEndpoints" => "Нет безопасного endpoint проверки",
            "TooManyRedirects" => "Слишком много перенаправлений",
            "RedirectWithoutLocation" => "Некорректное перенаправление",
            "NetworkError" => "Сетевая ошибка",
            "HealthCheckFailed" => "Проверка не завершена",
            _ when diagnosticCode.StartsWith("Http", StringComparison.Ordinal) => "HTTP-ответ",
            _ => "Диагностический результат"
        };

    private static string CombineDetails(
        string diagnosticText,
        string latencyText,
        string? diagnosticCode)
    {
        var parts = new List<string>(3);
        if (!string.IsNullOrEmpty(diagnosticText))
        {
            parts.Add(diagnosticText);
        }

        if (!string.IsNullOrEmpty(latencyText))
        {
            parts.Add(latencyText);
        }

        if (!string.IsNullOrEmpty(diagnosticCode))
        {
            parts.Add($"код {diagnosticCode}");
        }

        return string.Join(" · ", parts);
    }
}
