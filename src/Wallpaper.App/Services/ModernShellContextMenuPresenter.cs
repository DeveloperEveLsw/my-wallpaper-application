using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Wallpaper.Infrastructure.Windows.Shell;

namespace Wallpaper.App.Services;

internal static class ModernShellContextMenuPresenter
{
    private static readonly IReadOnlyDictionary<string, CommonCommandAppearance> CommonCommands =
        new Dictionary<string, CommonCommandAppearance>(StringComparer.OrdinalIgnoreCase)
        {
            ["cut"] = new("\uE8C6", "잘라내기"),
            ["copy"] = new("\uE8C8", "복사"),
            ["rename"] = new("\uE8AC", "이름 바꾸기"),
            ["share"] = new("\uE72D", "공유"),
            ["windows.share"] = new("\uE72D", "공유"),
            ["delete"] = new("\uE74D", "삭제"),
        };

    public static Task<ModernShellContextMenuResult> ShowAsync(
        FrameworkElement placementTarget,
        IReadOnlyList<ShellContextMenuEntry> entries,
        int screenX,
        int screenY)
    {
        ArgumentNullException.ThrowIfNull(placementTarget);
        ArgumentNullException.ThrowIfNull(entries);

        var completion = new TaskCompletionSource<ModernShellContextMenuResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var menu = CreateMenu(placementTarget, entries, completion);
        var localPosition = placementTarget.PointFromScreen(new Point(screenX, screenY));
        menu.PlacementTarget = placementTarget;
        menu.Placement = PlacementMode.RelativePoint;
        menu.PlacementRectangle = new Rect(localPosition, new Size(1, 1));
        menu.Closed += (_, _) => completion.TrySetResult(ModernShellContextMenuResult.Cancelled);
        menu.IsOpen = true;
        return completion.Task;
    }

    private static ContextMenu CreateMenu(
        FrameworkElement placementTarget,
        IReadOnlyList<ShellContextMenuEntry> entries,
        TaskCompletionSource<ModernShellContextMenuResult> completion)
    {
        var isLightTheme = IsSystemLightTheme();
        var menu = new ContextMenu
        {
            MinWidth = 278,
            MaxWidth = 420,
            MaxHeight = Math.Max(320, placementTarget.ActualHeight - 48),
            HasDropShadow = true,
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"),
            FontSize = 14,
            Background = CreateBrush(isLightTheme ? "#F7F9F9F9" : "#F72C2C2C"),
            Foreground = CreateBrush(isLightTheme ? "#E4000000" : "#FFFFFFFF"),
            BorderBrush = CreateBrush(isLightTheme ? "#24000000" : "#38FFFFFF"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(4),
        };
        menu.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri(
                "pack://application:,,,/PresentationFramework.Fluent;component/Themes/Fluent.xaml",
                UriKind.Absolute),
        });

        var commonEntries = entries
            .Where(entry =>
                entry.Kind == ShellContextMenuEntryKind.Command &&
                entry.CanonicalVerb is not null &&
                CommonCommands.ContainsKey(entry.CanonicalVerb))
            .GroupBy(entry => entry.CanonicalVerb!, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
        var commonIds = commonEntries.Select(entry => entry.CommandId).ToHashSet();

        if (commonEntries.Length > 0)
        {
            menu.Items.Add(CreateCommonCommandBar(menu, commonEntries, completion));
            menu.Items.Add(new Separator());
        }

        AddEntries(menu, menu.Items, entries, commonIds, completion);
        TrimTrailingSeparator(menu.Items);
        if (menu.Items.Count > 0 && menu.Items[^1] is not Separator)
        {
            menu.Items.Add(new Separator());
        }

        var classicItem = new MenuItem
        {
            Header = "더 많은 옵션 표시",
            InputGestureText = "Shift+F10",
            Icon = CreateGlyph("\uE712"),
        };
        AutomationProperties.SetName(classicItem, "더 많은 옵션 표시");
        classicItem.Click += (_, _) => Complete(
            menu,
            completion,
            ModernShellContextMenuResult.ShowClassic);
        menu.Items.Add(classicItem);

        return menu;
    }

    private static MenuItem CreateCommonCommandBar(
        ContextMenu menu,
        IReadOnlyList<ShellContextMenuEntry> entries,
        TaskCompletionSource<ModernShellContextMenuResult> completion)
    {
        var panel = new UniformGrid
        {
            Rows = 1,
            Margin = new Thickness(4, 2, 4, 2),
        };

        foreach (var entry in entries)
        {
            var appearance = CommonCommands[entry.CanonicalVerb!];
            var button = new Button
            {
                Width = 46,
                Height = 42,
                Margin = new Thickness(2),
                Padding = new Thickness(0),
                Content = CreateGlyph(appearance.Glyph),
                ToolTip = appearance.Label,
                IsEnabled = entry.IsEnabled,
            };
            AutomationProperties.SetName(button, appearance.Label);
            button.Click += (_, eventArgs) =>
            {
                eventArgs.Handled = true;
                Complete(
                    menu,
                    completion,
                    ModernShellContextMenuResult.ForCommand(entry.CommandId));
            };
            panel.Children.Add(button);
        }

        return new MenuItem
        {
            Header = panel,
            StaysOpenOnClick = true,
            Focusable = false,
        };
    }

    private static void AddEntries(
        ContextMenu rootMenu,
        ItemCollection destination,
        IReadOnlyList<ShellContextMenuEntry> entries,
        IReadOnlySet<uint> excludedCommandIds,
        TaskCompletionSource<ModernShellContextMenuResult> completion)
    {
        foreach (var entry in entries)
        {
            if (entry.Kind == ShellContextMenuEntryKind.Command &&
                excludedCommandIds.Contains(entry.CommandId))
            {
                continue;
            }

            if (entry.Kind == ShellContextMenuEntryKind.Separator)
            {
                if (destination.Count > 0 && destination[^1] is not Separator)
                {
                    destination.Add(new Separator());
                }

                continue;
            }

            var item = new MenuItem
            {
                Header = entry.Text,
                IsEnabled = entry.IsEnabled,
                IsCheckable = entry.IsChecked,
                IsChecked = entry.IsChecked,
                Icon = TryCreateBitmapIcon(entry.BitmapHandle),
            };
            AutomationProperties.SetName(item, entry.Text);

            if (entry.Kind == ShellContextMenuEntryKind.Submenu)
            {
                AddEntries(rootMenu, item.Items, entry.Children, excludedCommandIds, completion);
                TrimTrailingSeparator(item.Items);
                if (item.Items.Count == 0)
                {
                    item.IsEnabled = false;
                }
            }
            else
            {
                item.Click += (_, _) => Complete(
                    rootMenu,
                    completion,
                    ModernShellContextMenuResult.ForCommand(entry.CommandId));
            }

            destination.Add(item);
        }
    }

    private static void TrimTrailingSeparator(ItemCollection items)
    {
        while (items.Count > 0 && items[^1] is Separator)
        {
            items.RemoveAt(items.Count - 1);
        }
    }

    private static void Complete(
        ContextMenu menu,
        TaskCompletionSource<ModernShellContextMenuResult> completion,
        ModernShellContextMenuResult result)
    {
        if (completion.TrySetResult(result))
        {
            menu.IsOpen = false;
        }
    }

    private static TextBlock CreateGlyph(string glyph) => new()
    {
        Text = glyph,
        FontFamily = new FontFamily("Segoe Fluent Icons"),
        FontSize = 16,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private static Image? TryCreateBitmapIcon(nint bitmapHandle)
    {
        if (bitmapHandle.ToInt64() <= 12)
        {
            return null;
        }

        try
        {
            var source = Imaging.CreateBitmapSourceFromHBitmap(
                bitmapHandle,
                0,
                Int32Rect.Empty,
                BitmapSizeOptions.FromWidthAndHeight(16, 16));
            source.Freeze();
            return new Image
            {
                Source = source,
                Width = 16,
                Height = 16,
            };
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException or ExternalException)
        {
            return null;
        }
    }

    private static bool IsSystemLightTheme()
    {
        try
        {
            return Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "AppsUseLightTheme",
                0) is int value && value != 0;
        }
        catch (Exception exception) when (
            exception is IOException or System.Security.SecurityException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static SolidColorBrush CreateBrush(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }

    private sealed record CommonCommandAppearance(string Glyph, string Label);
}

internal enum ModernShellContextMenuResultKind
{
    Cancelled,
    Command,
    ShowClassic,
}

internal readonly record struct ModernShellContextMenuResult(
    ModernShellContextMenuResultKind Kind,
    uint CommandId)
{
    public static ModernShellContextMenuResult Cancelled { get; } =
        new(ModernShellContextMenuResultKind.Cancelled, 0);

    public static ModernShellContextMenuResult ShowClassic { get; } =
        new(ModernShellContextMenuResultKind.ShowClassic, 0);

    public static ModernShellContextMenuResult ForCommand(uint commandId) =>
        new(ModernShellContextMenuResultKind.Command, commandId);
}
