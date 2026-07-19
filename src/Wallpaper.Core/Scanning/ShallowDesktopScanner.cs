using Wallpaper.Core.Models;
using Wallpaper.Core.Sorting;

namespace Wallpaper.Core.Scanning;

public sealed class ShallowDesktopScanner(TimeProvider? timeProvider = null) : IDesktopScanner
{
    private const FileAttributes UnsupportedAttributes =
        FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint;

    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public DesktopSnapshot Scan(string rootPath)
    {
        var normalizedRoot = ValidateRoot(rootPath);
        var warnings = new List<ScanWarning>();
        var folders = EnumerateFolders(normalizedRoot, warnings)
            .OrderBy(folder => folder.Name, NaturalNameComparer.OrdinalIgnoreCase)
            .ToArray();
        var rootFiles = EnumerateFiles(normalizedRoot, normalizedRoot, warnings)
            .OrderBy(file => file.Name, NaturalNameComparer.OrdinalIgnoreCase)
            .ToArray();
        var rootInfo = new DirectoryInfo(normalizedRoot);

        return new DesktopSnapshot(
            normalizedRoot,
            string.IsNullOrWhiteSpace(rootInfo.Name) ? normalizedRoot : rootInfo.Name,
            folders,
            rootFiles,
            warnings,
            _timeProvider.GetUtcNow());
    }

    private static string ValidateRoot(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            throw new RootScanException(RootScanError.EmptyPath, "루트 폴더가 지정되지 않았습니다.");
        }

        if (!Path.IsPathFullyQualified(rootPath))
        {
            throw new RootScanException(
                RootScanError.PathNotFullyQualified,
                "루트 폴더는 절대 경로여야 합니다.");
        }

        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
        if (!Directory.Exists(normalizedRoot))
        {
            throw new RootScanException(
                RootScanError.DirectoryNotFound,
                "설정된 루트 폴더를 찾을 수 없습니다.");
        }

        try
        {
            var attributes = File.GetAttributes(normalizedRoot);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new RootScanException(
                    RootScanError.RootIsReparsePoint,
                    "재분석 지점은 루트 폴더로 사용할 수 없습니다.");
            }
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new RootScanException(
                RootScanError.AccessDenied,
                "루트 폴더에 접근할 수 없습니다.",
                exception);
        }
        catch (IOException exception)
        {
            throw new RootScanException(
                RootScanError.IoFailure,
                "루트 폴더를 확인하는 중 I/O 오류가 발생했습니다.",
                exception);
        }

        return normalizedRoot;
    }

    private static IEnumerable<DesktopFolder> EnumerateFolders(
        string rootPath,
        ICollection<ScanWarning> warnings)
    {
        IEnumerable<string> paths;
        try
        {
            paths = Directory.EnumerateDirectories(rootPath, "*", SearchOption.TopDirectoryOnly).ToArray();
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new RootScanException(RootScanError.AccessDenied, "루트 폴더를 읽을 수 없습니다.", exception);
        }
        catch (IOException exception)
        {
            throw new RootScanException(RootScanError.IoFailure, "루트 폴더를 읽는 중 오류가 발생했습니다.", exception);
        }

        foreach (var path in paths)
        {
            var relativePath = NormalizeRelativePath(rootPath, path);
            if (!TryGetSupportedAttributes(path, relativePath, warnings, out _))
            {
                continue;
            }

            var directory = new DirectoryInfo(path);
            var files = EnumerateFiles(rootPath, path, warnings)
                .OrderBy(file => file.Name, NaturalNameComparer.OrdinalIgnoreCase)
                .ToArray();

            yield return new DesktopFolder(
                CreateId("folder", relativePath),
                directory.Name,
                relativePath,
                files);
        }
    }

    private static IEnumerable<DesktopFile> EnumerateFiles(
        string rootPath,
        string directoryPath,
        ICollection<ScanWarning> warnings)
    {
        IEnumerable<string> paths;
        try
        {
            paths = Directory.EnumerateFiles(directoryPath, "*", SearchOption.TopDirectoryOnly).ToArray();
        }
        catch (UnauthorizedAccessException)
        {
            warnings.Add(new ScanWarning(
                NormalizeRelativePath(rootPath, directoryPath),
                ScanWarningCode.Inaccessible));
            yield break;
        }
        catch (IOException)
        {
            warnings.Add(new ScanWarning(
                NormalizeRelativePath(rootPath, directoryPath),
                ScanWarningCode.DisappearedDuringScan));
            yield break;
        }

        foreach (var path in paths)
        {
            var relativePath = NormalizeRelativePath(rootPath, path);
            if (!TryGetSupportedAttributes(path, relativePath, warnings, out _))
            {
                continue;
            }

            FileInfo file;
            try
            {
                file = new FileInfo(path);
                file.Refresh();
                if (!file.Exists)
                {
                    warnings.Add(new ScanWarning(relativePath, ScanWarningCode.DisappearedDuringScan));
                    continue;
                }
            }
            catch (UnauthorizedAccessException)
            {
                warnings.Add(new ScanWarning(relativePath, ScanWarningCode.Inaccessible));
                continue;
            }
            catch (IOException)
            {
                warnings.Add(new ScanWarning(relativePath, ScanWarningCode.DisappearedDuringScan));
                continue;
            }

            yield return new DesktopFile(
                CreateId("file", relativePath),
                file.Name,
                relativePath,
                file.Extension,
                file.Length,
                file.LastWriteTimeUtc);
        }
    }

    private static bool TryGetSupportedAttributes(
        string path,
        string relativePath,
        ICollection<ScanWarning> warnings,
        out FileAttributes attributes)
    {
        try
        {
            attributes = File.GetAttributes(path);
        }
        catch (UnauthorizedAccessException)
        {
            warnings.Add(new ScanWarning(relativePath, ScanWarningCode.Inaccessible));
            attributes = default;
            return false;
        }
        catch (IOException)
        {
            warnings.Add(new ScanWarning(relativePath, ScanWarningCode.DisappearedDuringScan));
            attributes = default;
            return false;
        }

        if ((attributes & UnsupportedAttributes) != 0)
        {
            warnings.Add(new ScanWarning(relativePath, ScanWarningCode.UnsupportedEntry));
            return false;
        }

        return true;
    }

    private static string NormalizeRelativePath(string rootPath, string path) =>
        Path.GetRelativePath(rootPath, path).Replace(Path.DirectorySeparatorChar, '/');

    private static string CreateId(string kind, string relativePath) =>
        $"{kind}:{relativePath.ToUpperInvariant()}";
}
