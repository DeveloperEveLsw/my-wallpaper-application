using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Wallpaper.App.ViewModels;
using Wallpaper.Core.FileOperations;

namespace Wallpaper.App;

public partial class MainWindow : Window
{
    private const string DockCardDataFormat = "Wallpaper.DockCardId";
    private const string FileMoveDataFormat = "Wallpaper.InternalFileMove";

    private readonly DispatcherTimer _clockTimer;
    private readonly DispatcherTimer _settingsHoverTimer;
    private Point _dockDragStart;
    private string? _dockDragCardId;
    private Point _fileDragStart;
    private FileCommandTarget? _fileDragSource;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = viewModel;

        _clockTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1),
        };
        _clockTimer.Tick += (_, _) => UpdateClock();

        _settingsHoverTimer = new DispatcherTimer(DispatcherPriority.Input)
        {
            Interval = TimeSpan.FromSeconds(1),
        };
        _settingsHoverTimer.Tick += SettingsHoverTimer_OnTick;

        Loaded += (_, _) =>
        {
            UpdateClock();
            _clockTimer.Start();
            UpdateDockWidth();
        };
        Closed += (_, _) =>
        {
            _clockTimer.Stop();
            _settingsHoverTimer.Stop();
            ViewModel.Dispose();
        };
    }

    public MainViewModel ViewModel { get; }

    private void UpdateClock()
    {
        var now = DateTimeOffset.Now;
        ClockText.Text = now.ToString("HH:mm");
        DateText.Text = now.ToString("yyyy년 M월 d일 dddd");
    }

    private void SettingsHotspot_OnMouseEnter(object sender, MouseEventArgs e)
    {
        if (!ViewModel.IsSettingsOpen)
        {
            _settingsHoverTimer.Stop();
            _settingsHoverTimer.Start();
        }
    }

    private void SettingsHotspot_OnMouseLeave(object sender, MouseEventArgs e) =>
        _settingsHoverTimer.Stop();

    private void SettingsHoverTimer_OnTick(object? sender, EventArgs e)
    {
        _settingsHoverTimer.Stop();
        if (SettingsHotspot.IsMouseOver)
        {
            ViewModel.OpenSettingsCommand.Execute(null);
        }
    }

    private void BackgroundSurface_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        ViewModel.CloseTransientUi();
        e.Handled = true;
    }

    private void Window_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            ViewModel.CloseTransientUi();
            e.Handled = true;
        }
    }

    private void Window_OnSizeChanged(object sender, SizeChangedEventArgs e) => UpdateDockWidth();

    private async void FileVisual_OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is Image image)
        {
            await EnsureFileVisualLoadedAsync(image);
        }
    }

    private async void FileVisual_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.WidthChanged && sender is Image { IsLoaded: true } image)
        {
            await EnsureFileVisualLoadedAsync(image);
        }
    }

    private async void Window_OnDpiChanged(object sender, DpiChangedEventArgs e)
    {
        var reloadTasks = EnumerateVisualDescendants<Image>(this)
            .Select(EnsureFileVisualLoadedAsync);
        await Task.WhenAll(reloadTasks);
    }

    private static Task EnsureFileVisualLoadedAsync(Image image)
    {
        if (image.DataContext is not FileTileViewModel file)
        {
            return Task.CompletedTask;
        }

        var displayWidth = image.ActualWidth > 0
            ? image.ActualWidth
            : image.Width;
        if (double.IsNaN(displayWidth) || displayWidth <= 0)
        {
            return Task.CompletedTask;
        }

        var dpi = VisualTreeHelper.GetDpi(image);
        var targetPixelWidth = Math.Max(1, (int)Math.Ceiling(displayWidth * dpi.DpiScaleX));
        return file.EnsureVisualLoadedAsync(targetPixelWidth);
    }

    private static IEnumerable<T> EnumerateVisualDescendants<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in EnumerateVisualDescendants<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private async void FileTile_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border { DataContext: FileTileViewModel file })
        {
            return;
        }

        ViewModel.SelectFile(file);
        if (e.ClickCount >= 2)
        {
            _fileDragSource = null;
            await ViewModel.OpenFileAsync(file);
        }
        else if (ViewModel.RootPath is not null && !ViewModel.IsBusy)
        {
            _fileDragStart = e.GetPosition(this);
            _fileDragSource = new FileCommandTarget(
                ViewModel.RootPath,
                file.RelativePath,
                FileCommandItemKind.File);
        }

        e.Handled = true;
    }

    private async void FileTile_OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        var source = _fileDragSource;
        if (e.LeftButton != MouseButtonState.Pressed ||
            source is null ||
            sender is not Border { DataContext: FileTileViewModel file } tile ||
            !string.Equals(source.RelativePath, file.RelativePath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var current = e.GetPosition(this);
        if (Math.Abs(current.X - _fileDragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - _fileDragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _fileDragSource = null;
        var data = new DataObject();
        data.SetData(FileMoveDataFormat, new FileDragPayload(source));
        ViewModel.ClearReorderTargets();
        ViewModel.ClearFileDropTargets();
        ShowFileDragPreview(file.Name, current);

        DragDropEffects result;
        try
        {
            result = DragDrop.DoDragDrop(tile, data, DragDropEffects.Move);
        }
        finally
        {
            HideFileDragPreview();
            ViewModel.ClearFileDropTargets();
        }

        if (result == DragDropEffects.None)
        {
            await ViewModel.RecoverAfterFileDragCancellationAsync();
        }

        e.Handled = true;
    }

    private void FileTile_OnGiveFeedback(object sender, GiveFeedbackEventArgs e)
    {
        if (FileDragPreview.Visibility == Visibility.Visible &&
            TryGetFileDragPosition(out var position))
        {
            UpdateFileDragPreview(position);
        }

        e.UseDefaultCursors = true;
        e.Handled = true;
    }

    private bool TryGetFileDragPosition(out Point position)
    {
        position = default;
        if (GetCursorPos(out var cursorPosition) == 0)
        {
            return false;
        }

        position = CompositionRoot.PointFromScreen(new Point(
            cursorPosition.X,
            cursorPosition.Y));
        return true;
    }

    private void FileTile_OnMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border { DataContext: FileTileViewModel file })
        {
            var position = GetItemContextMenuPosition(e);
            ViewModel.OpenFileContextMenu(file, position.X, position.Y);
            e.Handled = true;
        }
    }

    private void RenameTextBox_OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox textBox)
        {
            textBox.Focus();
            textBox.SelectAll();
        }
    }

    private void MoveTextBox_OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox textBox)
        {
            textBox.Focus();
            textBox.SelectAll();
        }
    }

    private void DockCard_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is Button { DataContext: CardViewModel card })
        {
            _dockDragStart = e.GetPosition(this);
            _dockDragCardId = card.Id;
        }
    }

    private void DockCard_OnPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is Button { DataContext: CardViewModel { IsVirtual: false } card })
        {
            var position = GetItemContextMenuPosition(e);
            ViewModel.OpenFolderContextMenu(card, position.X, position.Y);
            e.Handled = true;
        }
    }

    private void DockCard_OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed ||
            sender is not Button { DataContext: CardViewModel card } button ||
            !string.Equals(card.Id, _dockDragCardId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var current = e.GetPosition(this);
        if (Math.Abs(current.X - _dockDragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - _dockDragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var data = new DataObject();
        data.SetData(DockCardDataFormat, card.Id);
        try
        {
            _ = DragDrop.DoDragDrop(button, data, DragDropEffects.Move);
        }
        finally
        {
            _dockDragCardId = null;
            ViewModel.ClearReorderTargets();
        }

        e.Handled = true;
    }

    private void DockCard_OnDragOver(object sender, DragEventArgs e)
    {
        if (sender is not Button { DataContext: CardViewModel target } button)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        if (TryGetDraggedFile(e.Data, out var filePayload))
        {
            var isValid = ViewModel.CanMoveFileToCard(filePayload.Source, target);
            ViewModel.ClearReorderTargets();
            ViewModel.SetFileDropTarget(target, isValid);
            UpdateFileDragPreview(e.GetPosition(CompositionRoot));
            e.Effects = isValid ? DragDropEffects.Move : DragDropEffects.None;
            e.Handled = true;
            return;
        }

        if (target.IsVirtual ||
            !TryGetDraggedCardId(e.Data, out var sourceId) ||
            string.Equals(sourceId, target.Id, StringComparison.OrdinalIgnoreCase))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        ViewModel.ClearReorderTargets();
        target.SetReorderTarget(e.GetPosition(button).X >= button.ActualWidth / 2);
        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    private void DockCard_OnDragLeave(object sender, DragEventArgs e)
    {
        if (sender is Button { DataContext: CardViewModel card })
        {
            card.ClearReorderTarget();
            card.ClearFileDropTarget();
        }
    }

    private async void DockCard_OnDrop(object sender, DragEventArgs e)
    {
        if (sender is not Button { DataContext: CardViewModel target })
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        if (TryGetDraggedFile(e.Data, out var filePayload))
        {
            var isValid = ViewModel.CanMoveFileToCard(filePayload.Source, target);
            ViewModel.ClearFileDropTargets();
            e.Effects = isValid ? DragDropEffects.Move : DragDropEffects.None;
            e.Handled = true;
            if (isValid)
            {
                await ViewModel.DropFileOnCardAsync(filePayload.Source, target);
            }

            return;
        }

        if (target.IsVirtual ||
            !TryGetDraggedCardId(e.Data, out var sourceId) ||
            string.Equals(sourceId, target.Id, StringComparison.OrdinalIgnoreCase))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        var insertAfter = target.ShowInsertAfter;
        ViewModel.ClearReorderTargets();
        e.Effects = DragDropEffects.Move;
        e.Handled = true;
        await ViewModel.ReorderFolderCardAsync(sourceId, target.Id, insertAfter);
    }

    private static bool TryGetDraggedFile(IDataObject data, out FileDragPayload payload)
    {
        payload = null!;
        if (!data.GetDataPresent(FileMoveDataFormat) ||
            data.GetData(FileMoveDataFormat) is not FileDragPayload value)
        {
            return false;
        }

        payload = value;
        return true;
    }

    private static bool TryGetDraggedCardId(IDataObject data, out string cardId)
    {
        cardId = string.Empty;
        if (!data.GetDataPresent(DockCardDataFormat) || data.GetData(DockCardDataFormat) is not string value)
        {
            return false;
        }

        cardId = value;
        return !string.IsNullOrWhiteSpace(cardId);
    }

    private Point GetItemContextMenuPosition(MouseEventArgs e)
    {
        const double menuWidth = 260;
        const double menuHeight = 285;
        const double margin = 12;

        var position = e.GetPosition(CompositionRoot);
        var maxLeft = Math.Max(margin, CompositionRoot.ActualWidth - menuWidth - margin);
        var maxTop = Math.Max(margin, CompositionRoot.ActualHeight - menuHeight - margin);
        return new Point(
            Math.Clamp(position.X, margin, maxLeft),
            Math.Clamp(position.Y, margin, maxTop));
    }

    private void ShowFileDragPreview(string fileName, Point position)
    {
        FileDragPreviewName.Text = fileName;
        FileDragPreview.Visibility = Visibility.Visible;
        FileDragPreview.UpdateLayout();
        UpdateFileDragPreview(position);
    }

    private void UpdateFileDragPreview(Point position)
    {
        const double previewWidth = 250;
        const double previewHeight = 52;
        const double offset = 18;
        const double margin = 12;

        FileDragPreviewTransform.X = Math.Clamp(
            position.X + offset,
            margin,
            Math.Max(margin, CompositionRoot.ActualWidth - previewWidth - margin));
        FileDragPreviewTransform.Y = Math.Clamp(
            position.Y + offset,
            margin,
            Math.Max(margin, CompositionRoot.ActualHeight - previewHeight - margin));
    }

    private void HideFileDragPreview()
    {
        FileDragPreview.Visibility = Visibility.Collapsed;
        FileDragPreviewName.Text = string.Empty;
    }

    private void UpdateDockWidth()
    {
        if (DockContainer is not null && ActualWidth > 0)
        {
            DockContainer.MaxWidth = Math.Max(640, ActualWidth * 0.8);
        }
    }

    private sealed record FileDragPayload(FileCommandTarget Source);

#pragma warning disable SYSLIB1054
    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern int GetCursorPos(out NativePoint point);
#pragma warning restore SYSLIB1054

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;

        public int Y;
    }
}
