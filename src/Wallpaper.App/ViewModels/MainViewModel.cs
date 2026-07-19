using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using Wallpaper.App.Commands;
using Wallpaper.App.Services;
using Wallpaper.Core.Models;
using Wallpaper.Core.Scanning;
using Wallpaper.Core.Sorting;
using Wallpaper.Infrastructure.Windows.Settings;
using Wallpaper.Infrastructure.Windows.Visuals;
using Wallpaper.Infrastructure.Windows.Watching;

namespace Wallpaper.App.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private const int FileTilesPerRow = 5;

    private readonly IDesktopScanner _scanner;
    private readonly IAppSettingsStore _settingsStore;
    private readonly IFolderPicker _folderPicker;
    private readonly IRootChangeWatcher _changeWatcher;
    private readonly IFileVisualService _fileVisualService;
    private readonly AsyncRelayCommand _chooseRootCommand;
    private readonly AsyncRelayCommand _rescanCommand;
    private CardViewModel _rootFilesCard;
    private IReadOnlyList<string> _folderOrder = Array.Empty<string>();
    private SynchronizationContext? _uiContext;
    private string? _rootPath;
    private string _rootDisplayName = "루트 미설정";
    private string _statusText = "루트 폴더를 선택해 주세요.";
    private string _modalTitle = string.Empty;
    private string _visibleFileCountText = string.Empty;
    private bool _isModalOpen;
    private bool _isSettingsOpen;
    private bool _isBusy;
    private bool _hasVisibleFiles;
    private bool _isRootAvailable;
    private bool _rescanInProgress;
    private bool _rescanRequested;
    private string? _openCardId;
    private bool _disposed;

    public MainViewModel(
        IDesktopScanner scanner,
        IAppSettingsStore settingsStore,
        IFolderPicker folderPicker,
        IRootChangeWatcher changeWatcher,
        IFileVisualService fileVisualService)
    {
        _scanner = scanner;
        _settingsStore = settingsStore;
        _folderPicker = folderPicker;
        _changeWatcher = changeWatcher;
        _fileVisualService = fileVisualService;
        _rootFilesCard = CreateRootFilesCard(Array.Empty<DesktopFile>(), string.Empty);

        _chooseRootCommand = new AsyncRelayCommand(ChooseRootAsync, () => !IsBusy);
        _rescanCommand = new AsyncRelayCommand(RescanAsync, () => !IsBusy && HasRoot);
        ChooseRootCommand = _chooseRootCommand;
        RescanCommand = _rescanCommand;
        OpenCardCommand = new RelayCommand<CardViewModel>(OpenCard);
        CloseModalCommand = new RelayCommand(CloseModal);
        OpenSettingsCommand = new RelayCommand(OpenSettings);
        CloseSettingsCommand = new RelayCommand(CloseSettings);
        _changeWatcher.Changed += ChangeWatcher_OnChanged;
    }

    public ObservableCollection<CardViewModel> FolderCards { get; } = [];

    public ObservableCollection<FileTileRowViewModel> VisibleFileRows { get; } = [];

    public ICommand ChooseRootCommand { get; }

    public ICommand RescanCommand { get; }

    public ICommand OpenCardCommand { get; }

    public ICommand CloseModalCommand { get; }

    public ICommand OpenSettingsCommand { get; }

    public ICommand CloseSettingsCommand { get; }

    public CardViewModel RootFilesCard
    {
        get => _rootFilesCard;
        private set => SetProperty(ref _rootFilesCard, value);
    }

    public string? RootPath
    {
        get => _rootPath;
        private set
        {
            if (SetProperty(ref _rootPath, value))
            {
                OnPropertyChanged(nameof(HasRoot));
                NotifyCommandStates();
            }
        }
    }

    public bool HasRoot => !string.IsNullOrWhiteSpace(RootPath);

    public bool HasFolderCards => FolderCards.Count > 0;

    public bool HasVisibleFiles
    {
        get => _hasVisibleFiles;
        private set => SetProperty(ref _hasVisibleFiles, value);
    }

    public string RootDisplayName
    {
        get => _rootDisplayName;
        private set => SetProperty(ref _rootDisplayName, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string ModalTitle
    {
        get => _modalTitle;
        private set => SetProperty(ref _modalTitle, value);
    }

    public string VisibleFileCountText
    {
        get => _visibleFileCountText;
        private set => SetProperty(ref _visibleFileCountText, value);
    }

    public bool IsModalOpen
    {
        get => _isModalOpen;
        private set => SetProperty(ref _isModalOpen, value);
    }

    public bool IsSettingsOpen
    {
        get => _isSettingsOpen;
        private set => SetProperty(ref _isSettingsOpen, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                NotifyCommandStates();
            }
        }
    }

    public string HostStatus => "Standalone · M2 read-only sync";

    public async Task InitializeAsync()
    {
        _uiContext = SynchronizationContext.Current;
        var loadResult = await _settingsStore.LoadAsync();
        RootPath = loadResult.Settings.RootPath;
        _folderOrder = loadResult.Settings.FolderOrder ?? Array.Empty<string>();

        if (loadResult.WasCorrupted)
        {
            StatusText = "설정 파일을 읽을 수 없습니다. 루트를 다시 선택해 주세요.";
            IsSettingsOpen = true;
            return;
        }

        if (!HasRoot)
        {
            IsSettingsOpen = true;
            return;
        }

        ConfigureWatcher();
        await RescanAsync();
    }

    public async Task ReorderFolderCardAsync(string sourceId, string targetId, bool insertAfter)
    {
        if (IsBusy || !HasRoot || string.Equals(sourceId, targetId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var previousOrder = FolderCards.Select(card => card.Id).ToArray();
        var reorderedIds = FolderOrderPolicy.Move(previousOrder, sourceId, targetId, insertAfter);
        if (previousOrder.SequenceEqual(reorderedIds, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        ApplyCardOrder(reorderedIds);
        _folderOrder = reorderedIds;

        try
        {
            await SaveCurrentSettingsAsync();
            StatusText = "Dock 카드 순서를 저장했습니다.";
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            ApplyCardOrder(previousOrder);
            _folderOrder = previousOrder;
            StatusText = "Dock 순서를 저장할 수 없어 이전 순서로 복구했습니다.";
            IsSettingsOpen = true;
        }
    }

    public void ClearReorderTargets()
    {
        foreach (var card in FolderCards)
        {
            card.ClearReorderTarget();
        }
    }

    public void CloseTransientUi()
    {
        CloseModal();
        CloseSettings();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _changeWatcher.Changed -= ChangeWatcher_OnChanged;
        _changeWatcher.Dispose();
    }

    private async Task ChooseRootAsync()
    {
        var selectedPath = _folderPicker.PickFolder(RootPath);
        if (string.IsNullOrWhiteSpace(selectedPath))
        {
            return;
        }

        var previousRoot = RootPath;
        var previousOrder = _folderOrder;
        var previousRootAvailability = _isRootAvailable;
        RootPath = selectedPath;
        _folderOrder = Array.Empty<string>();
        _isRootAvailable = false;
        try
        {
            await SaveCurrentSettingsAsync();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            RootPath = previousRoot;
            _folderOrder = previousOrder;
            _isRootAvailable = previousRootAvailability;
            StatusText = "루트 설정을 저장할 수 없습니다.";
            IsSettingsOpen = true;
            return;
        }

        ConfigureWatcher();
        await RescanAsync();
    }

    private async Task RescanAsync()
    {
        if (!HasRoot || RootPath is null)
        {
            IsSettingsOpen = true;
            return;
        }

        if (_rescanInProgress)
        {
            _rescanRequested = true;
            return;
        }

        _rescanInProgress = true;
        IsBusy = true;
        try
        {
            do
            {
                _rescanRequested = false;
                await ScanOnceAsync(RootPath);
            }
            while (_rescanRequested && HasRoot);
        }
        finally
        {
            IsBusy = false;
            _rescanInProgress = false;
        }
    }

    private async Task ScanOnceAsync(string rootPath)
    {
        StatusText = "파일 시스템을 읽는 중…";

        try
        {
            var snapshot = await Task.Run(() => _scanner.Scan(rootPath));
            ApplySnapshot(snapshot);
            _isRootAvailable = true;
            IsSettingsOpen = false;
            var watchStatus = ConfigureWatcher();
            StatusText = CreateSynchronizedStatus(snapshot, watchStatus);
        }
        catch (RootScanException exception)
        {
            _isRootAvailable = false;
            ClearSnapshot();
            var watchStatus = ConfigureWatcher();
            StatusText = AppendWatcherWarning(exception.Message, watchStatus);
            IsSettingsOpen = true;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            _isRootAvailable = false;
            ClearSnapshot();
            var watchStatus = ConfigureWatcher();
            StatusText = AppendWatcherWarning(
                "폴더를 읽는 중 오류가 발생했습니다.",
                watchStatus);
            IsSettingsOpen = true;
        }
    }

    private void ApplySnapshot(DesktopSnapshot snapshot)
    {
        var openCardId = IsModalOpen ? _openCardId : null;
        RootDisplayName = snapshot.RootName;
        FolderCards.Clear();

        var foldersById = snapshot.Folders.ToDictionary(folder => folder.Id, StringComparer.OrdinalIgnoreCase);
        var orderedIds = FolderOrderPolicy.Merge(snapshot.Folders.Select(folder => folder.Id), _folderOrder);
        foreach (var id in orderedIds)
        {
            var folder = foldersById[id];
            FolderCards.Add(new CardViewModel(
                folder.Id,
                folder.Name,
                isVirtual: false,
                CreateFileTiles(snapshot.RootPath, folder.Files)));
        }

        OnPropertyChanged(nameof(HasFolderCards));
        RootFilesCard = CreateRootFilesCard(snapshot.RootFiles, snapshot.RootPath);

        if (openCardId is not null)
        {
            var updatedCard = string.Equals(openCardId, RootFilesCard.Id, StringComparison.OrdinalIgnoreCase)
                ? RootFilesCard
                : FolderCards.FirstOrDefault(card =>
                    string.Equals(card.Id, openCardId, StringComparison.OrdinalIgnoreCase));
            if (updatedCard is not null)
            {
                ShowCard(updatedCard);
                return;
            }
        }

        CloseModal();
    }

    private void ClearSnapshot()
    {
        RootDisplayName = "루트 오류";
        FolderCards.Clear();
        OnPropertyChanged(nameof(HasFolderCards));
        RootFilesCard = CreateRootFilesCard(Array.Empty<DesktopFile>(), RootPath ?? string.Empty);
        CloseModal();
    }

    private void OpenCard(CardViewModel card)
    {
        if (IsModalOpen && string.Equals(_openCardId, card.Id, StringComparison.OrdinalIgnoreCase))
        {
            CloseModal();
            return;
        }

        IsSettingsOpen = false;
        ShowCard(card);
    }

    private void ShowCard(CardViewModel card)
    {
        _openCardId = card.Id;
        ModalTitle = card.IsVirtual ? "루트 파일" : card.Name;
        VisibleFileRows.Clear();

        for (var offset = 0; offset < card.Files.Count; offset += FileTilesPerRow)
        {
            VisibleFileRows.Add(new FileTileRowViewModel(
                card.Files.Skip(offset).Take(FileTilesPerRow).ToArray()));
        }

        HasVisibleFiles = card.Files.Count > 0;
        VisibleFileCountText = card.Files.Count == 0 ? "파일 없음" : $"파일 {card.Files.Count:N0}개";
        IsModalOpen = true;
    }

    private void CloseModal()
    {
        IsModalOpen = false;
        _openCardId = null;
        VisibleFileRows.Clear();
        HasVisibleFiles = false;
        VisibleFileCountText = string.Empty;
    }

    private void OpenSettings()
    {
        CloseModal();
        IsSettingsOpen = true;
    }

    private void CloseSettings()
    {
        if (_isRootAvailable)
        {
            IsSettingsOpen = false;
        }
    }

    private void ChangeWatcher_OnChanged(object? sender, RootChangedEventArgs args)
    {
        var context = _uiContext;
        if (context is null || _disposed)
        {
            return;
        }

        context.Post(
            _ =>
            {
                if (_disposed)
                {
                    return;
                }

                StatusText = args.Reason == RootChangeReason.WatcherError
                    ? "변경 감시 오류를 감지해 전체 상태를 확인하는 중…"
                    : "외부 파일 변경을 감지해 다시 스캔하는 중…";
                _ = RescanAsync();
            },
            null);
    }

    private RootWatchStatus ConfigureWatcher()
    {
        if (RootPath is null)
        {
            _changeWatcher.Stop();
            return new RootWatchStatus(false, false, null);
        }

        try
        {
            return _changeWatcher.Watch(RootPath);
        }
        catch (Exception exception) when (
            exception is IOException or
                UnauthorizedAccessException or
                System.Security.SecurityException or
                ArgumentException)
        {
            return new RootWatchStatus(
                false,
                false,
                "실시간 변경 감시를 사용할 수 없어 수동 재스캔이 필요합니다.");
        }
    }

    private void ApplyCardOrder(IReadOnlyList<string> orderedIds)
    {
        var cardsById = FolderCards.ToDictionary(card => card.Id, StringComparer.OrdinalIgnoreCase);
        FolderCards.Clear();
        foreach (var id in orderedIds)
        {
            if (cardsById.TryGetValue(id, out var card))
            {
                FolderCards.Add(card);
            }
        }
    }

    private Task SaveCurrentSettingsAsync() => _settingsStore.SaveAsync(new AppSettings(
        AppSettings.CurrentSchemaVersion,
        RootPath,
        _folderOrder));

    private IReadOnlyList<FileTileViewModel> CreateFileTiles(
        string rootPath,
        IEnumerable<DesktopFile> files) => files
        .Select(file => new FileTileViewModel(
            file,
            Path.Combine(
                rootPath,
                file.RelativePath.Replace('/', Path.DirectorySeparatorChar)),
            _fileVisualService))
        .ToArray();

    private CardViewModel CreateRootFilesCard(IEnumerable<DesktopFile> files, string rootPath) => new(
        "virtual:root-files",
        "…",
        isVirtual: true,
        CreateFileTiles(rootPath, files));

    private static string CreateSynchronizedStatus(DesktopSnapshot snapshot, RootWatchStatus watchStatus)
    {
        string status;
        if (snapshot.Folders.Count == 0 && snapshot.RootFiles.Count == 0)
        {
            status = "파일 시스템과 동기화됨 · 빈 루트";
        }
        else if (snapshot.Warnings.Count == 0)
        {
            status = "파일 시스템과 동기화됨";
        }
        else
        {
            var inaccessibleCount = snapshot.Warnings.Count(warning =>
                warning.Code == ScanWarningCode.Inaccessible);
            status = inaccessibleCount > 0
                ? $"동기화됨 · 접근할 수 없는 항목 {inaccessibleCount}개 · 전체 제외 {snapshot.Warnings.Count}개"
                : $"동기화됨 · 제외된 항목 {snapshot.Warnings.Count}개";
        }

        return AppendWatcherWarning(status, watchStatus);
    }

    private static string AppendWatcherWarning(string status, RootWatchStatus watchStatus) =>
        string.IsNullOrWhiteSpace(watchStatus.Warning)
            ? status
            : $"{status} · {watchStatus.Warning}";

    private void NotifyCommandStates()
    {
        _chooseRootCommand.NotifyCanExecuteChanged();
        _rescanCommand.NotifyCanExecuteChanged();
    }
}
