namespace Wallpaper.Core.Sorting;

public static class FolderOrderPolicy
{
    public static IReadOnlyList<string> Merge(
        IEnumerable<string> naturallyOrderedIds,
        IEnumerable<string>? persistedIds)
    {
        ArgumentNullException.ThrowIfNull(naturallyOrderedIds);

        var natural = Distinct(naturallyOrderedIds);
        var available = natural.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>(natural.Count);
        var added = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (persistedIds is not null)
        {
            foreach (var id in persistedIds)
            {
                if (!string.IsNullOrWhiteSpace(id) && available.Contains(id) && added.Add(id))
                {
                    result.Add(natural.First(candidate =>
                        StringComparer.OrdinalIgnoreCase.Equals(candidate, id)));
                }
            }
        }

        foreach (var id in natural)
        {
            if (added.Add(id))
            {
                result.Add(id);
            }
        }

        return result;
    }

    public static IReadOnlyList<string> Move(
        IEnumerable<string> orderedIds,
        string sourceId,
        string targetId,
        bool insertAfter)
    {
        ArgumentNullException.ThrowIfNull(orderedIds);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetId);

        var result = Distinct(orderedIds);
        var sourceIndex = FindIndex(result, sourceId);
        var targetIndex = FindIndex(result, targetId);
        if (sourceIndex < 0 || targetIndex < 0 || sourceIndex == targetIndex)
        {
            return result;
        }

        var source = result[sourceIndex];
        result.RemoveAt(sourceIndex);
        targetIndex = FindIndex(result, targetId);
        result.Insert(targetIndex + (insertAfter ? 1 : 0), source);
        return result;
    }

    private static List<string> Distinct(IEnumerable<string> ids)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in ids)
        {
            if (!string.IsNullOrWhiteSpace(id) && seen.Add(id))
            {
                result.Add(id);
            }
        }

        return result;
    }

    private static int FindIndex(IReadOnlyList<string> ids, string id)
    {
        for (var index = 0; index < ids.Count; index++)
        {
            if (StringComparer.OrdinalIgnoreCase.Equals(ids[index], id))
            {
                return index;
            }
        }

        return -1;
    }
}
