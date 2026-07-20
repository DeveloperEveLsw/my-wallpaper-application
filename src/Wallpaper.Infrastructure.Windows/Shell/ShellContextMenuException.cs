namespace Wallpaper.Infrastructure.Windows.Shell;

public sealed class ShellContextMenuException : Exception
{
    public ShellContextMenuException(string message)
        : base(message)
    {
    }

    public ShellContextMenuException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
