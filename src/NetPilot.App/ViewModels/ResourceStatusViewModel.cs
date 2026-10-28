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
                StatusText = "Работает";
                DetailsText = latencyText;
                StatusBrush = Brushes.MediumSeaGreen;
                StatusGlyph = "●";
                break;

            case ResourceHealthState.Degraded:
                StatusText = "Есть ограничения";
                DetailsText = latencyText;
                StatusBrush = Brushes.DarkOrange;
                StatusGlyph = "●";
                break;

            case ResourceHealthState.Unavailable:
                StatusText = "Недоступно";
                DetailsText = string.Empty;
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
