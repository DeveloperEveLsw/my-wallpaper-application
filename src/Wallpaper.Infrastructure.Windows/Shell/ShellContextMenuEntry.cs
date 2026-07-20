namespace Wallpaper.Infrastructure.Windows.Shell;

public enum ShellContextMenuEntryKind
{
    Command,
    Separator,
    Submenu,
}

public sealed record ShellContextMenuEntry(
    ShellContextMenuEntryKind Kind,
    uint CommandId,
    string Text,
    string? CanonicalVerb,
    bool IsEnabled,
    bool IsChecked,
    nint BitmapHandle,
    IReadOnlyList<ShellContextMenuEntry> Children);
