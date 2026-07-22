using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using Wallpaper.App.Commands;
using Wallpaper.App.Services;
using Wallpaper.Core.FileOperations;
using Wallpaper.Core.Models;
using Wallpaper.Core.Naming;
using Wallpaper.Core.Scanning;
using Wallpaper.Core.Sorting;
using Wallpaper.Infrastructure.Windows.FileOperations;
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
    private readonly IFileCommandService _fileCommandService;
    private readonly AsyncRelayCommand _chooseRootCommand;
    private readonly AsyncRelayCommand _rescanCommand;
    private readonly AsyncRelayCommand _openContextItemCommand;
    private readonly AsyncRelayCommand _showContextItemInExplorerCommand;
    private readonly RelayCommand _beginRenameCommand;
    private readonly AsyncRelayCommand _confirmRenameCommand;
    private readonly RelayCommand _cancelRenameCommand;
    private readonly RelayCommand _beginRecycleCommand;
    private readonly AsyncRelayCommand _confirmRecycleCommand;
    private readonly RelayCommand _cancelRecycleCommand;
    private readonly AsyncRelayCommand _confirmMoveCommand;
    private readonly AsyncRelayCommand _cancelMoveCommand;
    private readonly RelayCommand _closeContextMenuCommand;
    private readonly RelayCommand _closeNotificationCommand;
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
    private bool _rescanDeferredForInputInteraction;
    private int _inputInteractionDepth;
    private Task? _rescanCompletion;
    private string? _openCardId;
    private string? _selectedFileId;
    private FileCommandTarget? _contextTarget;
    private CardViewModel? _contextCard;
    private FileCommandTarget? _dialogTarget;
    private bool _isItemContextMenuOpen;
    private double _itemContextMenuLeft;
    private double _itemContextMenuTop;
    private string _itemContextTargetName = string.Empty;
    private bool _isRenameDialogOpen;
    private string _renameText = string.Empty;
    private string _renameValidationMessage = string.Empty;
    private bool _isRecycleDialogOpen;
    private string _recycleTitle = string.Empty;
    private string _recycleMessage = string.Empty;
    private string _recycleErrorMessage = string.Empty;
    private FileMovePreparation? _pendingMove;
    private bool _isMoveDialogOpen;
    private string _moveMessage = string.Empty;
    private string _moveName = string.Empty;
    private string _moveValidationMessage = string.Empty;
    private bool _isNotificationOpen;
    private string _notificationText = string.Empty;
    private bool _notificationIsError;
    private string _hostStatus = "Standalone · Starting";
    private bool _disposed;

    public MainViewModel(
        IDesktopScanner scanner,
        IAppSettingsStore settingsStore,
        IFolderPicker folderPicker,
        IRootChangeWatcher changeWatcher,
        IFileVisualService fileVisualService,
        IFileCommandService fileCommandService)
    {
        _scanner = scanner;
        _settingsStore = settingsStore;
        _folderPicker = folderPicker;
        _changeWatcher = changeWatcher;
        _fileVisualService = fileVisualService;
        _fileCommandService = fileCommandService;
        _rootFilesCard = CreateRootFilesCard(Array.Empty<DesktopFile>(), string.Empty);

        _chooseRootCommand = new AsyncRelayCommand(ChooseRootAsync, () => !IsBusy);
        _rescanCommand = new AsyncRelayCommand(RescanAsync, () => !IsBusy && HasRoot);
        ChooseRootCommand = _chooseRootCommand;
        RescanCommand = _rescanCommand;
        OpenCardCommand = new RelayCommand<CardViewModel>(OpenCard);
        CloseModalCommand = new RelayCommand(CloseModal);
        OpenSettingsCommand = new RelayCommand(OpenSettings);
        CloseSettingsCommand = new RelayCommand(CloseSettings);
        _openContextItemCommand = new AsyncRelayCommand(
            OpenContextItemAsync,
            () => _contextTarget is not null && !IsBusy);
        _showContextItemInExplorerCommand = new AsyncRelayCommand(
            ShowContextItemInExplorerAsync,
            () => _contextTarget is not null && !IsBusy);
        _beginRenameCommand = new RelayCommand(BeginRename, () => _contextTarget is not null && !IsBusy);
        _confirmRenameCommand = new AsyncRelayCommand(
            ConfirmRenameAsync,
            () => CanConfirmRename && !IsBusy);
        _cancelRenameCommand = new RelayCommand(CancelRename);
        _beginRecycleCommand = new RelayCommand(BeginRecycle, () => _contextTarget is not null && !IsBusy);
        _confirmRecycleCommand = new AsyncRelayCommand(
            ConfirmRecycleAsync,
            () => _dialogTarget is not null && !IsBusy);
        _cancelRecycleCommand = new RelayCommand(CancelRecycle);
        _confirmMoveCommand = new AsyncRelayCommand(
            ConfirmMoveAsync,
            () => CanConfirmMove && !IsBusy);
        _cancelMoveCommand = new AsyncRelayCommand(
            CancelMoveAsync,
            () => IsMoveDialogOpen && !IsBusy);
        _closeContextMenuCommand = new RelayCommand(CloseItemContextMenu);
        _closeNotificationCommand = new RelayCommand(CloseNotification);
        OpenContextItemCommand = _openContextItemCommand;
        ShowContextItemInExplorerCommand = _showContextItemInExplorerCommand;
        BeginRenameCommand = _beginRenameCommand;
        ConfirmRenameCommand = _confirmRenameCommand;
        CancelRenameCommand = _cancelRenameCommand;
        BeginRecycleCommand = _beginRecycleCommand;
        ConfirmRecycleCommand = _confirmRecycleCommand;
        CancelRecycleCommand = _cancelRecycleCommand;
        ConfirmMoveCommand = _confirmMoveCommand;
        CancelMoveCommand = _cancelMoveCommand;
        CloseContextMenuCommand = _closeContextMenuCommand;
        CloseNotificationCommand = _closeNotificationCommand;
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

    public ICommand OpenContextItemCommand { get; }

    public ICommand ShowContextItemInExplorerCommand { get; }

    public ICommand BeginRenameCommand { get; }

    public ICommand ConfirmRenameCommand { get; }

    public ICommand CancelRenameCommand { get; }

    public ICommand BeginRecycleCommand { get; }

    public ICommand ConfirmRecycleCommand { get; }

    public ICommand CancelRecycleCommand { get; }

    public ICommand ConfirmMoveCommand { get; }

    public ICommand CancelMoveCommand { get; }

    public ICommand CloseContextMenuCommand { get; }

    public ICommand CloseNotificationCommand { get; }

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

    public bool IsItemContextMenuOpen
    {
        get => _isItemContextMenuOpen;
        private set => SetProperty(ref _isItemContextMenuOpen, value);
    }

    public double ItemContextMenuLeft
    {
        get => _itemContextMenuLeft;
        private set => SetProperty(ref _itemContextMenuLeft, value);
    }

    public double ItemContextMenuTop
    {
        get => _itemContextMenuTop;
        private set => SetProperty(ref _itemContextMenuTop, value);
    }

    public string ItemContextTargetName
    {
        get => _itemContextTargetName;
        private set => SetProperty(ref _itemContextTargetName, value);
    }

    public bool IsRenameDialogOpen
    {
        get => _isRenameDialogOpen;
        private set => SetProperty(ref _isRenameDialogOpen, value);
    }

    public string RenameText
    {
        get => _renameText;
        set
        {
            if (SetProperty(ref _renameText, value))
            {
                ValidateRenameText();
            }
        }
    }

    public string RenameValidationMessage
    {
        get => _renameValidationMessage;
        private set => SetProperty(ref _renameValidationMessage, value);
    }

    public bool CanConfirmRename =>
        _dialogTarget is not null &&
        WindowsFileNameValidator.Validate(RenameText).IsValid &&
        !string.Equals(_dialogTarget.Name, RenameText, StringComparison.Ordinal);

    public bool IsRecycleDialogOpen
    {
        get => _isRecycleDialogOpen;
        private set => SetProperty(ref _isRecycleDialogOpen, value);
    }

    public string RecycleTitle
    {
        get => _recycleTitle;
        private set => SetProperty(ref _recycleTitle, value);
    }

    public string RecycleMessage
    {
        get => _recycleMessage;
        private set => SetProperty(ref _recycleMessage, value);
    }

    public string RecycleErrorMessage
    {
        get => _recycleErrorMessage;
        private set => SetProperty(ref _recycleErrorMessage, value);
    }

    public bool IsMoveDialogOpen
    {
        get => _isMoveDialogOpen;
        private set => SetProperty(ref _isMoveDialogOpen, value);
    }

    public string MoveMessage
    {
        get => _moveMessage;
        private set => SetProperty(ref _moveMessage, value);
    }

    public string MoveName
    {
        get => _moveName;
        set
        {
            if (SetProperty(ref _moveName, value))
            {
                ValidateMoveName();
            }
        }
    }

    public string MoveValidationMessage
    {
        get => _moveValidationMessage;
        private set => SetProperty(ref _moveValidationMessage, value);
    }

    public bool CanConfirmMove =>
        _pendingMove is not null && WindowsFileNameValidator.Validate(MoveName).IsValid;

    public bool IsNotificationOpen
    {
        get => _isNotificationOpen;
        private set => SetProperty(ref _isNotificationOpen, value);
    }

    public string NotificationText
    {
        get => _notificationText;
        private set => SetProperty(ref _notificationText, value);
    }

    public bool NotificationIsError
    {
        get => _notificationIsError;
        private set => SetProperty(ref _notificationIsError, value);
    }

    public string HostStatus
    {
        get => _hostStatus;
        private set => SetProperty(ref _hostStatus, value);
    }

    public void UpdateHostStatus(string hostStatus)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hostStatus);
        HostStatus = hostStatus;
    }

    public void BeginInputInteraction()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _inputInteractionDepth++;
    }

    public async Task EndInputInteractionAsync()
    {
        if (_inputInteractionDepth <= 0)
        {
            return;
        }

        _inputInteractionDepth--;
        if (_inputInteractionDepth != 0 || !_rescanDeferredForInputInteraction)
        {
            return;
        }

        _rescanDeferredForInputInteraction = false;
        await RescanAsync();
    }

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

    public bool CanMoveFileToCard(FileCommandTarget source, CardViewModel destinationCard)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destinationCard);

        if (IsBusy || RootPath is null || source.Kind != FileCommandItemKind.File ||
            !string.Equals(source.RootPath, RootPath, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var isCurrentCard = destinationCard.IsVirtual
            ? ReferenceEquals(destinationCard, RootFilesCard)
            : FolderCards.Contains(destinationCard);
        if (!isCurrentCard ||
            (!destinationCard.IsVirtual && string.IsNullOrWhiteSpace(destinationCard.RelativePath)))
        {
            return false;
        }

        var sourceParent = GetRelativeParent(source.RelativePath);
        var destinationParent = destinationCard.IsVirtual ? null : destinationCard.RelativePath;
        return !string.Equals(sourceParent, destinationParent, StringComparison.OrdinalIgnoreCase);
    }

    public void SetFileDropTarget(CardViewModel card, bool isValid)
    {
        ArgumentNullException.ThrowIfNull(card);
        ClearFileDropTargets();
        card.SetFileDropTarget(isValid);
    }

    public void ClearFileDropTargets()
    {
        foreach (var card in FolderCards)
        {
            card.ClearFileDropTarget();
        }

        RootFilesCard.ClearFileDropTarget();
    }

    public async Task DropFileOnCardAsync(FileCommandTarget source, CardViewModel destinationCard)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destinationCard);
        if (!CanMoveFileToCard(source, destinationCard))
        {
            await RecoverAfterFileDragCancellationAsync();
            return;
        }

        var destination = new FileMoveDestination(
            source.RootPath,
            destinationCard.IsVirtual ? null : destinationCard.RelativePath);
        CloseItemTransientUi();
        CloseNotification();
        IsBusy = true;
        try
        {
            var preparation = await _fileCommandService.PrepareMoveAsync(
                source,
                destination,
                source.Name);
            if (preparation.HasNameCollision)
            {
                OpenMoveDialog(
                    preparation,
                    "같은 이름이 있어 사용 가능한 이름을 제안했습니다.");
                return;
            }

            await CompleteMoveAsync(preparation, preparation.ProposedName);
        }
        catch (FileCommandException exception) when (exception.Error == FileCommandError.NameCollision)
        {
            await RescanAsync();
            await RefreshMoveCollisionAsync(source, destination, source.Name);
        }
        catch (FileCommandException exception)
        {
            ShowNotification(exception.Message, isError: true);
            await RescanAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task RecoverAfterFileDragCancellationAsync()
    {
        ClearFileDropTargets();
        if (HasRoot && !IsBusy)
        {
            StatusText = "파일 이동을 취소해 실제 상태를 다시 확인하는 중…";
            await RescanAsync();
        }
    }

    public void SelectFile(FileTileViewModel file)
    {
        ArgumentNullException.ThrowIfNull(file);
        CloseItemContextMenu();
        foreach (var visibleFile in VisibleFileRows.SelectMany(row => row.Files))
        {
            visibleFile.IsSelected = ReferenceEquals(visibleFile, file);
        }

        _selectedFileId = file.File.Id;
    }

    public async Task OpenFileAsync(FileTileViewModel file)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (RootPath is null || IsBusy)
        {
            return;
        }

        SelectFile(file);
        var target = new FileCommandTarget(RootPath, file.RelativePath, FileCommandItemKind.File);
        try
        {
            await _fileCommandService.OpenAsync(target);
        }
        catch (FileCommandException exception)
        {
            ShowNotification(exception.Message, isError: true);
            await RescanAsync();
        }
    }

    public void OpenFileContextMenu(FileTileViewModel file, double left, double top)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (RootPath is null || IsBusy)
        {
            return;
        }

        SelectFile(file);
        OpenItemContextMenu(
            new FileCommandTarget(RootPath, file.RelativePath, FileCommandItemKind.File),
            card: null,
            left,
            top);
    }

    public void OpenFolderContextMenu(CardViewModel card, double left, double top)
    {
        ArgumentNullException.ThrowIfNull(card);
        if (RootPath is null || IsBusy || card.IsVirtual || string.IsNullOrWhiteSpace(card.RelativePath))
        {
            return;
        }

        OpenItemContextMenu(
            new FileCommandTarget(RootPath, card.RelativePath, FileCommandItemKind.Folder),
            card,
            left,
            top);
    }

    public FileCommandTarget? PrepareItemShellContextMenu()
    {
        if (IsBusy || _contextTarget is null)
        {
            return null;
        }

        var target = _contextTarget;
        CloseItemContextMenu();
        return target;
    }

    public bool PrepareDesktopShellContextMenu()
    {
        if (IsBusy || IsMoveDialogOpen || IsRenameDialogOpen || IsRecycleDialogOpen)
        {
            return false;
        }

        CloseItemContextMenu();
        CloseNotification();
        CloseModal();
        CloseSettings();
        return true;
    }

    public async Task RecoverAfterShellContextMenuAsync(string? errorMessage = null)
    {
        if (!string.IsNullOrWhiteSpace(errorMessage))
        {
            ShowNotification(errorMessage, isError: true);
        }

        if (HasRoot)
        {
            await RescanAsync();
        }
    }

    public void CloseTransientUi()
    {
        if (IsMoveDialogOpen)
        {
            CancelMoveCore();
            _ = RescanAsync();
            return;
        }

        if (IsRenameDialogOpen)
        {
            CancelRename();
            return;
        }

        if (IsRecycleDialogOpen)
        {
            CancelRecycle();
            return;
        }

        if (IsItemContextMenuOpen)
        {
            CloseItemContextMenu();
            return;
        }

        if (IsNotificationOpen)
        {
            CloseNotification();
            return;
        }

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
        CloseItemTransientUi();
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
            var currentCompletion = _rescanCompletion;
            if (currentCompletion is not null)
            {
                await currentCompletion;
            }

            return;
        }

        _rescanInProgress = true;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _rescanCompletion = completion.Task;
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
            completion.TrySetResult();
            if (ReferenceEquals(_rescanCompletion, completion.Task))
            {
                _rescanCompletion = null;
            }
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
                folder.RelativePath,
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
        if (IsBusy)
        {
            return;
        }

        CloseItemTransientUi();
        if (IsModalOpen && string.Equals(_openCardId, card.Id, StringComparison.OrdinalIgnoreCase))
        {
            CloseModal();
            return;
        }

        IsSettingsOpen = false;
        _selectedFileId = null;
        ShowCard(card);
    }

    private void ShowCard(CardViewModel card)
    {
        _openCardId = card.Id;
        ModalTitle = card.IsVirtual ? "루트 파일" : card.Name;
        VisibleFileRows.Clear();

        for (var offset = 0; offset < card.Files.Count; offset += FileTilesPerRow)
        {
            var rowFiles = card.Files.Skip(offset).Take(FileTilesPerRow).ToArray();
            foreach (var file in rowFiles)
            {
                file.IsSelected = string.Equals(
                    file.File.Id,
                    _selectedFileId,
                    StringComparison.OrdinalIgnoreCase);
            }

            VisibleFileRows.Add(new FileTileRowViewModel(rowFiles));
        }

        HasVisibleFiles = card.Files.Count > 0;
        VisibleFileCountText = card.Files.Count == 0 ? "파일 없음" : $"파일 {card.Files.Count:N0}개";
        IsModalOpen = true;
    }

    private void CloseModal()
    {
        IsModalOpen = false;
        _openCardId = null;
        _selectedFileId = null;
        VisibleFileRows.Clear();
        HasVisibleFiles = false;
        VisibleFileCountText = string.Empty;
    }

    private void OpenSettings()
    {
        if (IsBusy)
        {
            return;
        }

        CloseItemTransientUi();
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

    private void OpenItemContextMenu(
        FileCommandTarget target,
        CardViewModel? card,
        double left,
        double top)
    {
        CancelRenameCore();
        CancelRecycleCore();
        CancelMoveCore();
        IsSettingsOpen = false;
        _contextTarget = target;
        _contextCard = card;
        ItemContextTargetName = target.Name;
        ItemContextMenuLeft = left;
        ItemContextMenuTop = top;
        IsItemContextMenuOpen = true;
        NotifyCommandStates();
    }

    private void CloseItemContextMenu()
    {
        IsItemContextMenuOpen = false;
        _contextTarget = null;
        _contextCard = null;
        ItemContextTargetName = string.Empty;
        NotifyCommandStates();
    }

    private async Task OpenContextItemAsync()
    {
        var target = _contextTarget;
        var contextCard = _contextCard;
        if (target is null)
        {
            return;
        }

        try
        {
            if (target.Kind == FileCommandItemKind.File)
            {
                await _fileCommandService.OpenAsync(target);
            }
            else
            {
                await _fileCommandService.EnsureValidAsync(target);
                var cardId = DesktopItemId.ForFolder(target.RelativePath);
                var currentCard = FolderCards.FirstOrDefault(card =>
                    string.Equals(card.Id, cardId, StringComparison.OrdinalIgnoreCase)) ?? contextCard;
                if (currentCard is null || !FolderCards.Contains(currentCard))
                {
                    throw new FileCommandException(
                        FileCommandError.TargetMissing,
                        "선택한 폴더가 더 이상 Dock에 없습니다.");
                }

                _selectedFileId = null;
                ShowCard(currentCard);
            }

            CloseItemContextMenu();
        }
        catch (FileCommandException exception)
        {
            CloseItemContextMenu();
            ShowNotification(exception.Message, isError: true);
            await RescanAsync();
        }
    }

    private async Task ShowContextItemInExplorerAsync()
    {
        var target = _contextTarget;
        if (target is null)
        {
            return;
        }

        try
        {
            await _fileCommandService.ShowInExplorerAsync(target);
            CloseItemContextMenu();
        }
        catch (FileCommandException exception)
        {
            CloseItemContextMenu();
            ShowNotification(exception.Message, isError: true);
            await RescanAsync();
        }
    }

    private void BeginRename()
    {
        var target = _contextTarget;
        if (target is null || IsBusy)
        {
            return;
        }

        CancelMoveCore();
        _dialogTarget = target;
        CloseItemContextMenu();
        RenameText = target.Name;
        RenameValidationMessage = string.Empty;
        IsRenameDialogOpen = true;
        ValidateRenameText();
    }

    private void CancelRename()
    {
        if (!IsBusy)
        {
            CancelRenameCore();
        }
    }

    private void CancelRenameCore()
    {
        IsRenameDialogOpen = false;
        if (!IsRecycleDialogOpen)
        {
            _dialogTarget = null;
        }

        RenameText = string.Empty;
        RenameValidationMessage = string.Empty;
        NotifyCommandStates();
    }

    private void ValidateRenameText()
    {
        var validation = WindowsFileNameValidator.Validate(RenameText);
        if (!validation.IsValid)
        {
            RenameValidationMessage = WindowsFileCommandService.CreateInvalidNameMessage(validation.Error);
        }
        else if (_dialogTarget is not null &&
                 string.Equals(_dialogTarget.Name, RenameText, StringComparison.Ordinal))
        {
            RenameValidationMessage = "현재 이름과 동일합니다.";
        }
        else
        {
            RenameValidationMessage = string.Empty;
        }

        OnPropertyChanged(nameof(CanConfirmRename));
        _confirmRenameCommand.NotifyCanExecuteChanged();
    }

    private async Task ConfirmRenameAsync()
    {
        var target = _dialogTarget;
        var newName = RenameText;
        if (target is null || !CanConfirmRename)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var renamedTarget = await _fileCommandService.RenameAsync(target, newName);
            var settingsSaved = await ApplyRenamedIdentityAsync(target, renamedTarget);
            CancelRenameCore();
            await RescanAsync();

            var message = settingsSaved
                ? $"'{target.Name}'의 이름을 '{renamedTarget.Name}'(으)로 변경했습니다."
                : $"이름은 변경했지만 Dock 순서 설정을 저장하지 못했습니다.";
            StatusText = message;
            ShowNotification(message, isError: !settingsSaved);
        }
        catch (FileCommandException exception)
        {
            RenameValidationMessage = exception.Message;
            var targetIsStale = exception.Error is
                FileCommandError.InvalidTarget or
                FileCommandError.TargetMissing or
                FileCommandError.UnsupportedTarget;
            if (targetIsStale)
            {
                CancelRenameCore();
                ShowNotification(exception.Message, isError: true);
            }

            await RescanAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task<bool> ApplyRenamedIdentityAsync(
        FileCommandTarget previousTarget,
        FileCommandTarget renamedTarget)
    {
        if (previousTarget.Kind == FileCommandItemKind.File)
        {
            var previousId = DesktopItemId.ForFile(previousTarget.RelativePath);
            if (string.Equals(_selectedFileId, previousId, StringComparison.OrdinalIgnoreCase))
            {
                _selectedFileId = DesktopItemId.ForFile(renamedTarget.RelativePath);
            }

            return true;
        }

        var oldId = DesktopItemId.ForFolder(previousTarget.RelativePath);
        var newId = DesktopItemId.ForFolder(renamedTarget.RelativePath);
        _folderOrder = _folderOrder
            .Select(id => string.Equals(id, oldId, StringComparison.OrdinalIgnoreCase) ? newId : id)
            .ToArray();
        if (string.Equals(_openCardId, oldId, StringComparison.OrdinalIgnoreCase))
        {
            _openCardId = newId;
        }

        try
        {
            await SaveCurrentSettingsAsync();
            return true;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return false;
        }
    }

    private void BeginRecycle()
    {
        var target = _contextTarget;
        if (target is null || IsBusy)
        {
            return;
        }

        CancelMoveCore();
        _dialogTarget = target;
        CloseItemContextMenu();
        RecycleTitle = "휴지통으로 이동할까요?";
        RecycleMessage = target.Kind == FileCommandItemKind.Folder
            ? $"'{target.Name}' 폴더와 화면에 표시되지 않는 모든 하위 폴더·파일을 함께 휴지통으로 이동합니다."
            : $"'{target.Name}' 파일을 Windows 휴지통으로 이동합니다.";
        RecycleErrorMessage = string.Empty;
        IsRecycleDialogOpen = true;
        NotifyCommandStates();
    }

    private void CancelRecycle()
    {
        if (!IsBusy)
        {
            CancelRecycleCore();
        }
    }

    private void CancelRecycleCore()
    {
        IsRecycleDialogOpen = false;
        if (!IsRenameDialogOpen)
        {
            _dialogTarget = null;
        }

        RecycleTitle = string.Empty;
        RecycleMessage = string.Empty;
        RecycleErrorMessage = string.Empty;
        NotifyCommandStates();
    }

    private async Task ConfirmRecycleAsync()
    {
        var target = _dialogTarget;
        if (target is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await _fileCommandService.RecycleAsync(target);
            var settingsSaved = await RemoveRecycledIdentityAsync(target);
            CancelRecycleCore();
            await RescanAsync();

            var message = settingsSaved
                ? $"'{target.Name}'을(를) 휴지통으로 이동했습니다."
                : "항목은 휴지통으로 이동했지만 Dock 순서 설정을 저장하지 못했습니다.";
            StatusText = message;
            ShowNotification(message, isError: !settingsSaved);
        }
        catch (FileCommandException exception)
        {
            RecycleErrorMessage = exception.Message;
            var targetIsStale = exception.Error is
                FileCommandError.InvalidTarget or
                FileCommandError.TargetMissing or
                FileCommandError.UnsupportedTarget;
            if (targetIsStale)
            {
                CancelRecycleCore();
                ShowNotification(exception.Message, isError: true);
            }

            await RescanAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task<bool> RemoveRecycledIdentityAsync(FileCommandTarget target)
    {
        if (target.Kind == FileCommandItemKind.File)
        {
            var fileId = DesktopItemId.ForFile(target.RelativePath);
            if (string.Equals(_selectedFileId, fileId, StringComparison.OrdinalIgnoreCase))
            {
                _selectedFileId = null;
            }

            return true;
        }

        var folderId = DesktopItemId.ForFolder(target.RelativePath);
        _folderOrder = _folderOrder
            .Where(id => !string.Equals(id, folderId, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (string.Equals(_openCardId, folderId, StringComparison.OrdinalIgnoreCase))
        {
            _openCardId = null;
        }

        try
        {
            await SaveCurrentSettingsAsync();
            return true;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return false;
        }
    }

    private void OpenMoveDialog(FileMovePreparation preparation, string validationMessage)
    {
        CancelRenameCore();
        CancelRecycleCore();
        CloseItemContextMenu();
        IsSettingsOpen = false;
        _pendingMove = preparation;
        MoveMessage = preparation.Destination.RelativeFolderPath is null
            ? $"'{preparation.Source.Name}' 파일을 … 카드의 루트 위치로 이동합니다."
            : $"'{preparation.Source.Name}' 파일을 '{preparation.Destination.RelativeFolderPath}' 폴더로 이동합니다.";
        MoveName = preparation.ProposedName;
        MoveValidationMessage = validationMessage;
        IsMoveDialogOpen = true;
        ValidateMoveName();
        if (!string.IsNullOrWhiteSpace(validationMessage) &&
            WindowsFileNameValidator.Validate(MoveName).IsValid)
        {
            MoveValidationMessage = validationMessage;
        }

        NotifyCommandStates();
    }

    private async Task ConfirmMoveAsync()
    {
        var preparation = _pendingMove;
        var destinationName = MoveName;
        if (preparation is null || !CanConfirmMove)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await CompleteMoveAsync(preparation, destinationName);
        }
        catch (FileCommandException exception) when (exception.Error == FileCommandError.NameCollision)
        {
            await RescanAsync();
            await RefreshMoveCollisionAsync(
                preparation.Source,
                preparation.Destination,
                destinationName);
        }
        catch (FileCommandException exception)
        {
            MoveValidationMessage = exception.Message;
            var targetIsStale = exception.Error is
                FileCommandError.InvalidTarget or
                FileCommandError.TargetMissing or
                FileCommandError.UnsupportedTarget or
                FileCommandError.NoChange;
            if (targetIsStale)
            {
                CancelMoveCore();
                ShowNotification(exception.Message, isError: true);
            }

            await RescanAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task CompleteMoveAsync(
        FileMovePreparation preparation,
        string destinationName)
    {
        var movedTarget = await _fileCommandService.MoveAsync(
            preparation.Source,
            preparation.Destination,
            destinationName);
        CancelMoveCore();
        _selectedFileId = DesktopItemId.ForFile(movedTarget.RelativePath);
        await RescanAsync();

        var message = _isRootAvailable
            ? $"'{preparation.Source.Name}'을(를) '{movedTarget.Name}' 이름으로 이동했습니다."
            : "파일은 이동했지만 현재 snapshot을 확인하지 못했습니다. 루트 상태를 확인해 주세요.";
        StatusText = message;
        ShowNotification(message, isError: !_isRootAvailable);
    }

    private async Task RefreshMoveCollisionAsync(
        FileCommandTarget source,
        FileMoveDestination destination,
        string attemptedName)
    {
        try
        {
            var refreshed = await _fileCommandService.PrepareMoveAsync(
                source,
                destination,
                attemptedName);
            OpenMoveDialog(
                refreshed,
                refreshed.HasNameCollision
                    ? "확정 직전에 이름 충돌이 발생해 새 이름을 제안했습니다."
                    : "외부 변경을 반영했습니다. 이름을 확인한 뒤 다시 이동해 주세요.");
        }
        catch (FileCommandException exception)
        {
            CancelMoveCore();
            ShowNotification(exception.Message, isError: true);
        }
    }

    private async Task CancelMoveAsync()
    {
        if (!IsMoveDialogOpen || IsBusy)
        {
            return;
        }

        CancelMoveCore();
        StatusText = "파일 이동을 취소해 실제 상태를 다시 확인하는 중…";
        await RescanAsync();
    }

    private void CancelMoveCore()
    {
        IsMoveDialogOpen = false;
        _pendingMove = null;
        MoveMessage = string.Empty;
        MoveName = string.Empty;
        MoveValidationMessage = string.Empty;
        NotifyCommandStates();
    }

    private void ValidateMoveName()
    {
        var validation = WindowsFileNameValidator.Validate(MoveName);
        MoveValidationMessage = validation.IsValid
            ? string.Empty
            : WindowsFileCommandService.CreateInvalidNameMessage(validation.Error);
        OnPropertyChanged(nameof(CanConfirmMove));
        _confirmMoveCommand.NotifyCanExecuteChanged();
    }

    private static string? GetRelativeParent(string relativePath)
    {
        var parent = Path.GetDirectoryName(
            relativePath.Replace('/', Path.DirectorySeparatorChar));
        return string.IsNullOrEmpty(parent)
            ? null
            : parent.Replace(Path.DirectorySeparatorChar, '/');
    }

    private void ShowNotification(string message, bool isError)
    {
        NotificationText = message;
        NotificationIsError = isError;
        IsNotificationOpen = true;
    }

    private void CloseNotification()
    {
        IsNotificationOpen = false;
        NotificationText = string.Empty;
        NotificationIsError = false;
    }

    private void CloseItemTransientUi()
    {
        CloseItemContextMenu();
        CancelRenameCore();
        CancelRecycleCore();
        CancelMoveCore();
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

                if (_inputInteractionDepth > 0)
                {
                    _rescanDeferredForInputInteraction = true;
                    StatusText = args.Reason == RootChangeReason.WatcherError
                        ? "입력 작업이 끝나면 변경 감시 오류를 확인합니다."
                        : "입력 작업이 끝나면 외부 파일 변경을 반영합니다.";
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
        relativePath: null,
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
        _openContextItemCommand.NotifyCanExecuteChanged();
        _showContextItemInExplorerCommand.NotifyCanExecuteChanged();
        _beginRenameCommand.NotifyCanExecuteChanged();
        _confirmRenameCommand.NotifyCanExecuteChanged();
        _beginRecycleCommand.NotifyCanExecuteChanged();
        _confirmRecycleCommand.NotifyCanExecuteChanged();
        _confirmMoveCommand.NotifyCanExecuteChanged();
        _cancelMoveCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanConfirmRename));
        OnPropertyChanged(nameof(CanConfirmMove));
    }
}
