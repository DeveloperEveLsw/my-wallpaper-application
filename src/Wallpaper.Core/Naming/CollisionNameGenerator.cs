namespace Wallpaper.Core.Naming;

public static class CollisionNameGenerator
{
    public static string ProposeAvailableFileName(
        string desiredName,
        IEnumerable<string> existingNames)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(desiredName);
        ArgumentNullException.ThrowIfNull(existingNames);

        var occupiedNames = new HashSet<string>(existingNames, StringComparer.OrdinalIgnoreCase);
        if (!occupiedNames.Contains(desiredName))
        {
            return desiredName;
        }

        var extension = Path.GetExtension(desiredName);
        var baseName = Path.GetFileNameWithoutExtension(desiredName);

        for (var suffix = 1; suffix < int.MaxValue; suffix++)
        {
            var candidate = $"{baseName} ({suffix}){extension}";
            if (!occupiedNames.Contains(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("사용 가능한 충돌 이름을 만들 수 없습니다.");
    }
}
