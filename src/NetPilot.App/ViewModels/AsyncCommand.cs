using System.Windows.Input;

namespace NetPilot.App.ViewModels;

public sealed class AsyncCommand(
    Func<Task> execute,
    Func<bool>? canExecute = null,
    Action<Exception>? onException = null) : ICommand
{
    private bool _isExecuting;

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) =>
        !_isExecuting && (canExecute?.Invoke() ?? true);

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter))
        {
            return;
        }

        _isExecuting = true;
        RaiseCanExecuteChanged();

        try
        {
            await execute();
        }
        catch (Exception exception)
        {
            onException?.Invoke(exception);
        }
        finally
        {
            _isExecuting = false;
            RaiseCanExecuteChanged();
        }
    }

    public void RaiseCanExecuteChanged() =>
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
