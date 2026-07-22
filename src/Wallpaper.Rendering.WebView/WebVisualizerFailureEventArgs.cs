namespace Wallpaper.Rendering.WebView;

public sealed class WebVisualizerFailureEventArgs(Exception exception) : EventArgs
{
    public Exception Exception { get; } = exception ?? throw new ArgumentNullException(nameof(exception));
}
