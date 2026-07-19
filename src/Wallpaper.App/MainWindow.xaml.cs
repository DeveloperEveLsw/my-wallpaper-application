using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Wallpaper.App.ViewModels;

namespace Wallpaper.App;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _clockTimer;
    private readonly DispatcherTimer _settingsHoverTimer;

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

    private void UpdateDockWidth()
    {
        if (DockContainer is not null && ActualWidth > 0)
        {
            DockContainer.MaxWidth = Math.Max(640, ActualWidth * 0.8);
        }
    }
}
