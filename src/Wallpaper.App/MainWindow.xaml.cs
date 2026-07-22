using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Wallpaper.App.ViewModels;
using Wallpaper.Core.FileOperations;
using Wallpaper.Hosts;
using Wallpaper.Infrastructure.Windows.Shell;

namespace Wallpaper.App;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _clockTimer;
    private readonly DispatcherTimer _settingsHoverTimer;
    private readonly IShellContextMenuService _shellContextMenuService;
    private readonly IWallpaperHost _wallpaperHost;
    private Point _dockDragStart;
    private string? _dockDragCardId;
    private Point _fileDragStart;
    private FileCommandTarget? _fileDragSource;
    private FileTileViewModel? _fileDragSourceFile;
    private InternalDragKind _internalDragKind;
    private bool _inputInteractionActive;
    private bool _isShellContextMenuOpen;
    private NativePoint? _itemShellMenuScreenPosition;

    public MainWindow(
        MainViewModel viewModel,
        IShellContextMenuService shellContextMenuService,
        IWallpaperHost wallpaperHost)
    {
        InitializeComponent();
        ViewModel = viewModel;
        _shellContextMenuService = shellContextMenuService;
        _wallpaperHost = wallpaperHost;
        DataContext = viewModel;

        if (_wallpaperHost.Kind == HostKind.WallpaperEngine)
        {
            WindowState = WindowState.Normal;
            ResizeMode = ResizeMode.NoResize;
            ShowActivated = false;
            ShowInTaskbar = false;
            Opacity = 0;
        }

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

        SourceInitialized += MainWindow_OnSourceInitialized;
        _wallpaperHost.StatusChanged += WallpaperHost_OnStatusChanged;
        _wallpaperHost.ExitRequested += WallpaperHost_OnExitRequested;

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
            _wallpaperHost.StatusChanged -= WallpaperHost_OnStatusChanged;
            _wallpaperHost.ExitRequested -= WallpaperHost_OnExitRequested;
            ViewModel.Dispose();
        };
    }

    public MainViewModel ViewModel { get; }

    private void MainWindow_OnSourceInitialized(object? sender, EventArgs e)
    {
        var windowHandle = new WindowInteropHelper(this).Handle;
        _wallpaperHost.Attach(windowHandle);
    }

    private void WallpaperHost_OnStatusChanged(object? sender, HostStatusChangedEventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(() => WallpaperHost_OnStatusChanged(sender, e));
            return;
        }

        ViewModel.UpdateHostStatus(e.Status.DisplayText);
        if (_wallpaperHost.Kind == HostKind.WallpaperEngine)
        {
            Opacity = e.Status.State is HostRuntimeState.Active or HostRuntimeState.Paused
                ? 1
                : 0;
        }
    }

    private void WallpaperHost_OnExitRequested(object? sender, EventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(() => WallpaperHost_OnExitRequested(sender, e));
            return;
        }

        Application.Current.Shutdown();
    }

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

    private async void BackgroundSurface_OnMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        var screenPosition = GetContextMenuScreenPosition(e);
        e.Handled = true;
        await ShowDesktopContextMenuAsync(screenPosition);
    }

    private async void SettingsHotspot_OnMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        _settingsHoverTimer.Stop();
        var screenPosition = GetContextMenuScreenPosition(e);
        e.Handled = true;
        await ShowDesktopContextMenuAsync(screenPosition);
    }

    private async void Window_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            ViewModel.CloseTransientUi();
            e.Handled = true;
            return;
        }

        if (IsContextMenuKey(e))
        {
            e.Handled = true;
            await ShowDesktopContextMenuAsync(GetElementCenterOnScreen(CompositionRoot));
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
            ClearPendingInternalDrag();
            await ViewModel.OpenFileAsync(file);
        }
        else if (ViewModel.RootPath is not null && !ViewModel.IsBusy)
        {
            _dockDragCardId = null;
            _fileDragStart = e.GetPosition(this);
            _fileDragSource = new FileCommandTarget(
                ViewModel.RootPath,
                file.RelativePath,
                FileCommandItemKind.File);
            _fileDragSourceFile = file;
        }
        else
        {
            ClearPendingInternalDrag();
        }

        e.Handled = true;
    }

    private void FileTile_OnMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border { DataContext: FileTileViewModel file })
        {
            var position = GetItemContextMenuPosition(e);
            _itemShellMenuScreenPosition = GetContextMenuScreenPosition(e);
            ViewModel.OpenFileContextMenu(file, position.X, position.Y);
            e.Handled = true;
        }
    }

    private void FileTile_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (!IsContextMenuKey(e) || sender is not Border { DataContext: FileTileViewModel file } tile)
        {
            return;
        }

        var position = GetItemContextMenuPosition(GetElementCenter(tile));
        _itemShellMenuScreenPosition = GetElementCenterOnScreen(tile);
        ViewModel.OpenFileContextMenu(file, position.X, position.Y);
        e.Handled = true;
    }

    private async void WindowsOptions_OnClick(object sender, RoutedEventArgs e)
    {
        var target = ViewModel.PrepareItemShellContextMenu();
        if (target is null)
        {
            return;
        }

        var screenPosition = _itemShellMenuScreenPosition;
        _itemShellMenuScreenPosition = null;
        if (screenPosition is null)
        {
            return;
        }

        await ShowShellContextMenuAsync(
            screenPosition.Value,
            ownerWindow => _shellContextMenuService.CreateItemContextMenu(target, ownerWindow));
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
        if (sender is Button { DataContext: CardViewModel { IsVirtual: false } card } &&
            !ViewModel.IsBusy)
        {
            _fileDragSource = null;
            _fileDragSourceFile = null;
            _dockDragStart = e.GetPosition(this);
            _dockDragCardId = card.Id;
        }
        else
        {
            ClearPendingInternalDrag();
        }
    }

    private void DockCard_OnPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is Button { DataContext: CardViewModel { IsVirtual: false } card })
        {
            var position = GetItemContextMenuPosition(e);
            _itemShellMenuScreenPosition = GetContextMenuScreenPosition(e);
            ViewModel.OpenFolderContextMenu(card, position.X, position.Y);
            e.Handled = true;
        }
    }

    private void DockCard_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!IsContextMenuKey(e) ||
            sender is not Button { DataContext: CardViewModel { IsVirtual: false } card } button)
        {
            return;
        }

        var position = GetItemContextMenuPosition(GetElementCenter(button));
        _itemShellMenuScreenPosition = GetElementCenterOnScreen(button);
        ViewModel.OpenFolderContextMenu(card, position.X, position.Y);
        e.Handled = true;
    }

    private async void Window_OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            if (_internalDragKind != InternalDragKind.None)
            {
                await CancelInternalDragAsync();
            }
            else
            {
                ClearPendingInternalDrag();
            }

            return;
        }

        var position = e.GetPosition(CompositionRoot);
        if (_internalDragKind == InternalDragKind.None &&
            HasExceededDragThreshold(e.GetPosition(this)))
        {
            BeginInternalDrag(position);
        }

        if (_internalDragKind == InternalDragKind.None)
        {
            return;
        }

        UpdateInternalDrag(position);
        e.Handled = true;
    }

    private async void Window_OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_internalDragKind == InternalDragKind.None)
        {
            ClearPendingInternalDrag();
            return;
        }

        var dragKind = _internalDragKind;
        var sourceCardId = _dockDragCardId;
        var sourceFile = _fileDragSource;
        var hasTarget = TryGetDockCardAt(
            e.GetPosition(CompositionRoot),
            out _,
            out var targetCard);
        var insertAfter = targetCard?.ShowInsertAfter == true;
        var validFileTarget = sourceFile is not null &&
            targetCard is not null &&
            ViewModel.CanMoveFileToCard(sourceFile, targetCard);
        var validCardTarget = sourceCardId is not null &&
            targetCard is { IsVirtual: false } &&
            !string.Equals(sourceCardId, targetCard.Id, StringComparison.OrdinalIgnoreCase);

        EndInternalDragVisuals();
        e.Handled = true;
        try
        {
            if (hasTarget && dragKind == InternalDragKind.File && validFileTarget)
            {
                await ViewModel.DropFileOnCardAsync(sourceFile!, targetCard!);
            }
            else if (hasTarget && dragKind == InternalDragKind.DockCard && validCardTarget)
            {
                await ViewModel.ReorderFolderCardAsync(
                    sourceCardId!,
                    targetCard!.Id,
                    insertAfter);
            }
        }
        finally
        {
            await EndInputInteractionAsync();
        }
    }

    private async void Window_OnMouseLeave(object sender, MouseEventArgs e)
    {
        if (_internalDragKind != InternalDragKind.None)
        {
            await CancelInternalDragAsync();
        }
        else if (e.LeftButton != MouseButtonState.Pressed)
        {
            ClearPendingInternalDrag();
        }
    }

    private bool HasExceededDragThreshold(Point current)
    {
        var start = _dockDragCardId is not null
            ? _dockDragStart
            : _fileDragStart;
        return Math.Abs(current.X - start.X) >= SystemParameters.MinimumHorizontalDragDistance ||
            Math.Abs(current.Y - start.Y) >= SystemParameters.MinimumVerticalDragDistance;
    }

    private void BeginInternalDrag(Point position)
    {
        if (ViewModel.IsBusy)
        {
            ClearPendingInternalDrag();
            return;
        }

        if (_dockDragCardId is not null)
        {
            _internalDragKind = InternalDragKind.DockCard;
            _ = Mouse.Capture(null);
            Mouse.OverrideCursor = Cursors.SizeAll;
        }
        else if (_fileDragSource is not null && _fileDragSourceFile is not null)
        {
            _internalDragKind = InternalDragKind.File;
            Mouse.OverrideCursor = Cursors.Arrow;
            ShowFileDragPreview(_fileDragSourceFile.Name, position);
        }

        if (_internalDragKind == InternalDragKind.None)
        {
            return;
        }

        ViewModel.BeginInputInteraction();
        _inputInteractionActive = true;
        UpdateInternalDrag(position);
    }

    private void UpdateInternalDrag(Point position)
    {
        ViewModel.ClearReorderTargets();
        ViewModel.ClearFileDropTargets();
        if (_internalDragKind == InternalDragKind.File)
        {
            UpdateFileDragPreview(position);
        }

        if (!TryGetDockCardAt(position, out var targetButton, out var targetCard))
        {
            return;
        }

        if (_internalDragKind == InternalDragKind.File && _fileDragSource is not null)
        {
            ViewModel.SetFileDropTarget(
                targetCard,
                ViewModel.CanMoveFileToCard(_fileDragSource, targetCard));
            return;
        }

        if (_internalDragKind == InternalDragKind.DockCard &&
            !targetCard.IsVirtual &&
            !string.Equals(_dockDragCardId, targetCard.Id, StringComparison.OrdinalIgnoreCase))
        {
            var targetPosition = CompositionRoot.TranslatePoint(position, targetButton);
            targetCard.SetReorderTarget(targetPosition.X >= targetButton.ActualWidth / 2);
        }
    }

    private bool TryGetDockCardAt(
        Point position,
        out Button targetButton,
        out CardViewModel targetCard)
    {
        targetButton = null!;
        targetCard = null!;
        var current = CompositionRoot.InputHitTest(position) as DependencyObject;
        while (current is not null)
        {
            if (current is Button { DataContext: CardViewModel card } button &&
                DockContainer.IsAncestorOf(button))
            {
                targetButton = button;
                targetCard = card;
                return true;
            }

            current = GetVisualOrLogicalParent(current);
        }

        return false;
    }

    private static DependencyObject? GetVisualOrLogicalParent(DependencyObject current)
    {
        if (current is Visual or System.Windows.Media.Media3D.Visual3D)
        {
            return VisualTreeHelper.GetParent(current);
        }

        return LogicalTreeHelper.GetParent(current);
    }

    private async Task CancelInternalDragAsync()
    {
        EndInternalDragVisuals();
        await EndInputInteractionAsync();
    }

    private void EndInternalDragVisuals()
    {
        _internalDragKind = InternalDragKind.None;
        Mouse.OverrideCursor = null;
        HideFileDragPreview();
        ViewModel.ClearReorderTargets();
        ViewModel.ClearFileDropTargets();
        ClearPendingInternalDrag();
    }

    private void ClearPendingInternalDrag()
    {
        _dockDragCardId = null;
        _fileDragSource = null;
        _fileDragSourceFile = null;
    }

    private async Task EndInputInteractionAsync()
    {
        if (!_inputInteractionActive)
        {
            return;
        }

        _inputInteractionActive = false;
        await ViewModel.EndInputInteractionAsync();
    }

    private Point GetItemContextMenuPosition(MouseEventArgs e)
        => GetItemContextMenuPosition(e.GetPosition(CompositionRoot));

    private Point GetItemContextMenuPosition(Point position)
    {
        const double menuWidth = 260;
        const double menuHeight = 340;
        const double margin = 12;

        var maxLeft = Math.Max(margin, CompositionRoot.ActualWidth - menuWidth - margin);
        var maxTop = Math.Max(margin, CompositionRoot.ActualHeight - menuHeight - margin);
        return new Point(
            Math.Clamp(position.X, margin, maxLeft),
            Math.Clamp(position.Y, margin, maxTop));
    }

    private Point GetElementCenter(FrameworkElement element) => element.TranslatePoint(
        new Point(element.ActualWidth / 2, element.ActualHeight / 2),
        CompositionRoot);

    private static NativePoint GetElementCenterOnScreen(FrameworkElement element)
    {
        var center = element.PointToScreen(new Point(
            element.ActualWidth / 2,
            element.ActualHeight / 2));
        return new NativePoint(
            (int)Math.Round(center.X, MidpointRounding.AwayFromZero),
            (int)Math.Round(center.Y, MidpointRounding.AwayFromZero));
    }

    private static bool IsContextMenuKey(KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        return key == Key.Apps ||
            (key == Key.F10 && (Keyboard.Modifiers & ModifierKeys.Shift) != 0);
    }

    private NativePoint GetContextMenuScreenPosition(MouseEventArgs e)
    {
        var originalClick = PointToScreen(e.GetPosition(this));
        return new NativePoint(
            (int)Math.Round(originalClick.X, MidpointRounding.AwayFromZero),
            (int)Math.Round(originalClick.Y, MidpointRounding.AwayFromZero));
    }

    private Task ShowDesktopContextMenuAsync(NativePoint screenPosition)
    {
        if (!ViewModel.PrepareDesktopShellContextMenu())
        {
            return Task.CompletedTask;
        }

        return ShowShellContextMenuAsync(
            screenPosition,
            _shellContextMenuService.CreateDesktopContextMenu);
    }

    private async Task ShowShellContextMenuAsync(
        NativePoint screenPosition,
        Func<nint, IShellContextMenuSession> createSession)
    {
        if (_isShellContextMenuOpen)
        {
            return;
        }

        _isShellContextMenuOpen = true;
        ViewModel.BeginInputInteraction();
        string? errorMessage = null;
        try
        {
            var ownerWindow = new WindowInteropHelper(this).Handle;
            using var session = createSession(ownerWindow);
            session.Show(screenPosition.X, screenPosition.Y);
        }
        catch (FileCommandException exception)
        {
            errorMessage = exception.Message;
        }
        catch (ShellContextMenuException exception)
        {
            errorMessage = exception.Message;
        }
        finally
        {
            try
            {
                await ViewModel.RecoverAfterShellContextMenuAsync(errorMessage);
            }
            finally
            {
                try
                {
                    await ViewModel.EndInputInteractionAsync();
                }
                finally
                {
                    _isShellContextMenuOpen = false;
                }
            }
        }
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

    private enum InternalDragKind
    {
        None,
        DockCard,
        File,
    }

    private readonly record struct NativePoint(int X, int Y);
}
