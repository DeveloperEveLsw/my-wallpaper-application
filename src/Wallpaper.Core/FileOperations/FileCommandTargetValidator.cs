namespace Wallpaper.Core.FileOperations;

public static class FileCommandTargetValidator
{
    private const FileAttributes UnsupportedAttributes =
        FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint;

    public static ValidatedFileCommandTarget ValidateExisting(FileCommandTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);

        var normalizedRoot = NormalizeRoot(target.RootPath);
        var segments = ParseRelativePath(target.RelativePath);
        ValidateDepth(target.Kind, segments);

        var absolutePath = Path.GetFullPath(Path.Combine(normalizedRoot, Path.Combine(segments)));
        EnsureInsideRoot(normalizedRoot, absolutePath);
        EnsureSupportedDirectory(normalizedRoot, isRoot: true);

        if (segments.Length == 2)
        {
            EnsureSupportedDirectory(Path.Combine(normalizedRoot, segments[0]), isRoot: false);
        }

        FileAttributes attributes;
        try
        {
            attributes = File.GetAttributes(absolutePath);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            throw new FileCommandException(
                FileCommandError.TargetMissing,
                "선택한 항목이 더 이상 존재하지 않습니다.",
                exception);
        }
        catch (Exception exception) when (
            exception is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            throw new FileCommandException(
                FileCommandError.UnsupportedTarget,
                "선택한 항목에 접근할 수 없습니다.",
                exception);
        }

        var isDirectory = (attributes & FileAttributes.Directory) != 0;
        if (isDirectory != (target.Kind == FileCommandItemKind.Folder))
        {
            throw new FileCommandException(
                FileCommandError.InvalidTarget,
                "선택한 항목의 종류가 스캔 이후 변경되었습니다.");
        }

        if ((attributes & UnsupportedAttributes) != 0)
        {
            throw new FileCommandException(
                FileCommandError.UnsupportedTarget,
                "숨김·시스템·재분석 지점 항목에는 명령을 실행할 수 없습니다.");
        }

        return new ValidatedFileCommandTarget(
            target,
            normalizedRoot,
            absolutePath,
            Path.GetDirectoryName(absolutePath)!,
            Path.GetFileName(absolutePath));
    }

    private static string NormalizeRoot(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath) || !Path.IsPathFullyQualified(rootPath))
        {
            throw new FileCommandException(
                FileCommandError.InvalidTarget,
                "파일 명령의 루트 경로가 올바르지 않습니다.");
        }

        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            throw new FileCommandException(
                FileCommandError.InvalidTarget,
                "파일 명령의 루트 경로가 올바르지 않습니다.",
                exception);
        }
    }

    private static string[] ParseRelativePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathFullyQualified(relativePath))
        {
            throw new FileCommandException(
                FileCommandError.InvalidTarget,
                "파일 명령의 상대 경로가 올바르지 않습니다.");
        }

        var segments = relativePath
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 ||
            segments.Any(segment => segment is "." or ".." || string.IsNullOrWhiteSpace(segment)))
        {
            throw new FileCommandException(
                FileCommandError.InvalidTarget,
                "루트 경계를 벗어나는 경로에는 명령을 실행할 수 없습니다.");
        }

        return segments;
    }

    private static void ValidateDepth(FileCommandItemKind kind, IReadOnlyList<string> segments)
    {
        var isSupported = kind switch
        {
            FileCommandItemKind.Folder => segments.Count == 1,
            FileCommandItemKind.File => segments.Count is 1 or 2,
            _ => false,
        };

        if (!isSupported)
        {
            throw new FileCommandException(
                FileCommandError.InvalidTarget,
                "MVP 탐색 깊이 밖의 항목에는 명령을 실행할 수 없습니다.");
        }
    }

    private static void EnsureInsideRoot(string rootPath, string targetPath)
    {
        var relative = Path.GetRelativePath(rootPath, targetPath);
        if (Path.IsPathFullyQualified(relative) ||
            relative == ".." ||
            relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
            relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal))
        {
            throw new FileCommandException(
                FileCommandError.InvalidTarget,
                "루트 경계를 벗어나는 경로에는 명령을 실행할 수 없습니다.");
        }
    }

    private static void EnsureSupportedDirectory(string path, bool isRoot)
    {
        FileAttributes attributes;
        try
        {
            attributes = File.GetAttributes(path);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            throw new FileCommandException(
                FileCommandError.TargetMissing,
                isRoot ? "설정된 루트가 더 이상 존재하지 않습니다." : "대상 폴더가 더 이상 존재하지 않습니다.",
                exception);
        }
        catch (Exception exception) when (
            exception is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            throw new FileCommandException(
                FileCommandError.UnsupportedTarget,
                isRoot ? "설정된 루트에 접근할 수 없습니다." : "대상 폴더에 접근할 수 없습니다.",
                exception);
        }

        if ((attributes & FileAttributes.Directory) == 0 ||
            (attributes & UnsupportedAttributes) != 0)
        {
            throw new FileCommandException(
                FileCommandError.UnsupportedTarget,
                isRoot
                    ? "현재 루트는 지원하는 일반 로컬 폴더가 아닙니다."
                    : "재분석 지점 또는 지원하지 않는 폴더에는 명령을 실행할 수 없습니다.");
        }
    }
}
