namespace Wallpaper.Core.FileOperations;

public sealed record FileCommandTarget(
    string RootPath,
    string RelativePath,
    FileCommandItemKind Kind)
{
    public string Name => Path.GetFileName(RelativePath.Replace('/', Path.DirectorySeparatorChar));
}

public enum FileCommandItemKind
{
    File,
    Folder,
}

public sealed record ValidatedFileCommandTarget(
    FileCommandTarget Target,
    string RootPath,
    string AbsolutePath,
    string ParentPath,
    string Name);
