using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Wallpaper.App.ViewModels;

namespace Wallpaper.App;

public partial class MainWindow : Window
{
    private const string DockCardDataFormat = "Wallpaper.DockCardId";

    private readonly DispatcherTimer _clockTimer;
    private readonly DispatcherTimer _settingsHoverTimer;
    private Point _dockDragStart;
    private string? _dockDragCardId;

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
        if (sender is Image { DataContext: FileTileViewModel file })
        {
            await file.EnsureVisualLoadedAsync();
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
        if (sender is not Button { DataContext: CardViewModel target } button ||
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
        }
    }

    private async void DockCard_OnDrop(object sender, DragEventArgs e)
    {
        if (sender is not Button { DataContext: CardViewModel target } ||
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

    private void UpdateDockWidth()
    {
        if (DockContainer is not null && ActualWidth > 0)
        {
            DockContainer.MaxWidth = Math.Max(640, ActualWidth * 0.8);
        }
    }
}
