namespace Wallpaper.App.ViewModels;

public sealed class CardViewModel(
    string id,
    string name,
    string? relativePath,
    bool isVirtual,
    IReadOnlyList<FileTileViewModel> files) : ObservableObject
{
    private bool _showInsertBefore;
    private bool _showInsertAfter;
    private bool _isFileDropTargetValid;
    private bool _isFileDropTargetInvalid;

    public string Id { get; } = id;

    public string Name { get; } = name;

    public string? RelativePath { get; } = relativePath;

    public bool IsVirtual { get; } = isVirtual;

    public IReadOnlyList<FileTileViewModel> Files { get; } = files;

    public bool ShowInsertBefore
    {
        get => _showInsertBefore;
        private set => SetProperty(ref _showInsertBefore, value);
    }

    public bool ShowInsertAfter
    {
        get => _showInsertAfter;
        private set => SetProperty(ref _showInsertAfter, value);
    }

    public bool IsFileDropTargetValid
    {
        get => _isFileDropTargetValid;
        private set => SetProperty(ref _isFileDropTargetValid, value);
    }

    public bool IsFileDropTargetInvalid
    {
        get => _isFileDropTargetInvalid;
        private set => SetProperty(ref _isFileDropTargetInvalid, value);
    }

    public void SetReorderTarget(bool insertAfter)
    {
        ShowInsertBefore = !insertAfter;
        ShowInsertAfter = insertAfter;
    }

    public void ClearReorderTarget()
    {
        ShowInsertBefore = false;
        ShowInsertAfter = false;
    }

    public void SetFileDropTarget(bool isValid)
    {
        IsFileDropTargetValid = isValid;
        IsFileDropTargetInvalid = !isValid;
    }

    public void ClearFileDropTarget()
    {
        IsFileDropTargetValid = false;
        IsFileDropTargetInvalid = false;
    }
}
