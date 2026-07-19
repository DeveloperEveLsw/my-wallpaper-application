namespace Wallpaper.Core.Tests;

internal sealed class TemporaryDirectory : IDisposable
{
    public TemporaryDirectory()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"wallpaper-core-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string CreateDirectory(string relativePath)
    {
        var fullPath = System.IO.Path.Combine(Path, relativePath);
        Directory.CreateDirectory(fullPath);
        return fullPath;
    }

    public string CreateFile(string relativePath, string contents = "fixture")
    {
        var fullPath = System.IO.Path.Combine(Path, relativePath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, contents);
        return fullPath;
    }

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
