namespace Wallpaper.App.ViewModels;

public sealed class CardViewModel(
    string id,
    string name,
    bool isVirtual,
    IReadOnlyList<FileTileViewModel> files) : ObservableObject
{
    private bool _showInsertBefore;
    private bool _showInsertAfter;

    public string Id { get; } = id;

    public string Name { get; } = name;

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
}
