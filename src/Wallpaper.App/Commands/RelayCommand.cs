using System.Windows.Input;

namespace Wallpaper.App.Commands;

public sealed class RelayCommand(Action execute, Func<bool>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;

    public void Execute(object? parameter) => execute();

    public void NotifyCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public sealed class RelayCommand<T>(Action<T> execute, Func<T, bool>? canExecute = null) : ICommand
    where T : class
{
    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) =>
        parameter is T typedParameter && (canExecute?.Invoke(typedParameter) ?? true);

    public void Execute(object? parameter)
    {
        if (parameter is T typedParameter)
        {
            execute(typedParameter);
        }
    }

    public void NotifyCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
