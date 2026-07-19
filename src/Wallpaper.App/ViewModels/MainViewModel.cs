using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using Wallpaper.App.Commands;
using Wallpaper.App.Services;
using Wallpaper.Core.Models;
using Wallpaper.Core.Scanning;
using Wallpaper.Infrastructure.Windows.Settings;

namespace Wallpaper.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly IDesktopScanner _scanner;
    private readonly IAppSettingsStore _settingsStore;
    private readonly IFolderPicker _folderPicker;
    private readonly AsyncRelayCommand _chooseRootCommand;
    private readonly AsyncRelayCommand _rescanCommand;
    private CardViewModel _rootFilesCard = CreateRootFilesCard(Array.Empty<DesktopFile>());
    private string? _rootPath;
    private string _rootDisplayName = "루트 미설정";
    private string _statusText = "루트 폴더를 선택해 주세요.";
    private string _modalTitle = string.Empty;
    private bool _isModalOpen;
    private bool _isSettingsOpen;
    private bool _isBusy;
    private string? _openCardId;

    public MainViewModel(
        IDesktopScanner scanner,
        IAppSettingsStore settingsStore,
        IFolderPicker folderPicker)
    {
        _scanner = scanner;
        _settingsStore = settingsStore;
        _folderPicker = folderPicker;

        _chooseRootCommand = new AsyncRelayCommand(ChooseRootAsync, () => !IsBusy);
        _rescanCommand = new AsyncRelayCommand(RescanAsync, () => !IsBusy && HasRoot);
        ChooseRootCommand = _chooseRootCommand;
        RescanCommand = _rescanCommand;
        OpenCardCommand = new RelayCommand<CardViewModel>(OpenCard);
        CloseModalCommand = new RelayCommand(CloseModal);
        OpenSettingsCommand = new RelayCommand(OpenSettings);
        CloseSettingsCommand = new RelayCommand(CloseSettings);
    }

    public ObservableCollection<CardViewModel> FolderCards { get; } = [];

    public ObservableCollection<FileTileViewModel> VisibleFiles { get; } = [];

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

    public string HostStatus => "Standalone · MVP preview";

    public async Task InitializeAsync()
    {
        var loadResult = await _settingsStore.LoadAsync();
        RootPath = loadResult.Settings.RootPath;

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

        await RescanAsync();
    }

    public void CloseTransientUi()
    {
        CloseModal();
        CloseSettings();
    }

    private async Task ChooseRootAsync()
    {
        var selectedPath = _folderPicker.PickFolder(RootPath);
        if (string.IsNullOrWhiteSpace(selectedPath))
        {
            return;
        }

        RootPath = selectedPath;
        try
        {
            await _settingsStore.SaveAsync(new AppSettings(
                AppSettings.CurrentSchemaVersion,
                RootPath,
                FolderOrder: Array.Empty<string>()));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            StatusText = "루트 설정을 저장할 수 없습니다.";
            IsSettingsOpen = true;
            return;
        }

        await RescanAsync();
    }

    private async Task RescanAsync()
    {
        if (!HasRoot || RootPath is null)
        {
            IsSettingsOpen = true;
            return;
        }

        IsBusy = true;
        StatusText = "파일 시스템을 읽는 중…";

        try
        {
            var snapshot = await Task.Run(() => _scanner.Scan(RootPath));
            ApplySnapshot(snapshot);
            IsSettingsOpen = false;
            StatusText = snapshot.Warnings.Count == 0
                ? "파일 시스템과 동기화됨"
                : $"동기화됨 · 제외된 항목 {snapshot.Warnings.Count}개";
        }
        catch (RootScanException exception)
        {
            ClearSnapshot();
            StatusText = exception.Message;
            IsSettingsOpen = true;
        }
        catch (IOException)
        {
            ClearSnapshot();
            StatusText = "설정을 저장하거나 폴더를 읽는 중 오류가 발생했습니다.";
            IsSettingsOpen = true;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplySnapshot(DesktopSnapshot snapshot)
    {
        RootDisplayName = snapshot.RootName;
        FolderCards.Clear();

        foreach (var folder in snapshot.Folders)
        {
            FolderCards.Add(new CardViewModel(
                folder.Id,
                folder.Name,
                IsVirtual: false,
                folder.Files.Select(file => new FileTileViewModel(file)).ToArray()));
        }

        RootFilesCard = CreateRootFilesCard(snapshot.RootFiles);
        CloseModal();
    }

    private void ClearSnapshot()
    {
        RootDisplayName = "루트 오류";
        FolderCards.Clear();
        RootFilesCard = CreateRootFilesCard(Array.Empty<DesktopFile>());
        CloseModal();
    }

    private void OpenCard(CardViewModel card)
    {
        if (IsModalOpen && string.Equals(_openCardId, card.Id, StringComparison.Ordinal))
        {
            CloseModal();
            return;
        }

        IsSettingsOpen = false;
        _openCardId = card.Id;
        ModalTitle = card.IsVirtual ? "루트 파일" : card.Name;
        VisibleFiles.Clear();
        foreach (var file in card.Files)
        {
            VisibleFiles.Add(file);
        }

        IsModalOpen = true;
    }

    private void CloseModal()
    {
        IsModalOpen = false;
        _openCardId = null;
        VisibleFiles.Clear();
    }

    private void OpenSettings()
    {
        CloseModal();
        IsSettingsOpen = true;
    }

    private void CloseSettings()
    {
        if (HasRoot)
        {
            IsSettingsOpen = false;
        }
    }

    private void NotifyCommandStates()
    {
        _chooseRootCommand.NotifyCanExecuteChanged();
        _rescanCommand.NotifyCanExecuteChanged();
    }

    private static CardViewModel CreateRootFilesCard(IEnumerable<DesktopFile> files) => new(
        "virtual:root-files",
        "…",
        IsVirtual: true,
        files.Select(file => new FileTileViewModel(file)).ToArray());
}
