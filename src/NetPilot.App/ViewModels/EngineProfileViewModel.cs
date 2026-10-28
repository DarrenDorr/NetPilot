using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace NetPilot.App.ViewModels;

public sealed class EngineProfileViewModel : INotifyPropertyChanged
{
    private string _statusText = "Ожидает допущенный движок";
    private bool _isActive;

    public EngineProfileViewModel(string id, string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        Id = id;
        DisplayName = displayName;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Id { get; }
    public string DisplayName { get; }

    public string StatusText
    {
        get => _statusText;
        private set => SetField(ref _statusText, value);
    }

    public bool IsActive
    {
        get => _isActive;
        private set => SetField(ref _isActive, value);
    }

    public void SetUnavailable()
    {
        IsActive = false;
        StatusText = "Ожидает допущенный движок";
    }

    public void SetAvailable(bool isActive)
    {
        IsActive = isActive;
        StatusText = isActive ? "Активен" : "Доступен";
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
