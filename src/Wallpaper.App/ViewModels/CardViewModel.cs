namespace Wallpaper.App.ViewModels;

public sealed record CardViewModel(
    string Id,
    string Name,
    bool IsVirtual,
    IReadOnlyList<FileTileViewModel> Files);
