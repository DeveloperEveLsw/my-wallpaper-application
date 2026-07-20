namespace Wallpaper.Core.Naming;

public static class CollisionNameGenerator
{
    private const int MaximumNameLength = 255;

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

        var lastPeriod = desiredName.LastIndexOf('.');
        var hasExtension = lastPeriod > 0;
        var extension = hasExtension ? desiredName[lastPeriod..] : string.Empty;
        var baseName = hasExtension ? desiredName[..lastPeriod] : desiredName;

        for (var suffix = 1; suffix < int.MaxValue; suffix++)
        {
            var suffixText = $" ({suffix})";
            var maximumBaseLength = MaximumNameLength - suffixText.Length - extension.Length;
            if (maximumBaseLength < 1)
            {
                throw new InvalidOperationException("확장자를 보존한 충돌 이름을 만들 수 없습니다.");
            }

            var candidateBase = baseName.Length <= maximumBaseLength
                ? baseName
                : baseName[..maximumBaseLength];
            var candidate = $"{candidateBase}{suffixText}{extension}";
            if (!occupiedNames.Contains(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("사용 가능한 충돌 이름을 만들 수 없습니다.");
    }
}
