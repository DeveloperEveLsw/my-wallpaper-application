namespace Wallpaper.Core.Sorting;

public sealed class NaturalNameComparer : IComparer<string>
{
    public static NaturalNameComparer OrdinalIgnoreCase { get; } = new();

    private NaturalNameComparer()
    {
    }

    public int Compare(string? left, string? right)
    {
        if (ReferenceEquals(left, right))
        {
            return 0;
        }

        if (left is null)
        {
            return -1;
        }

        if (right is null)
        {
            return 1;
        }

        var leftIndex = 0;
        var rightIndex = 0;

        while (leftIndex < left.Length && rightIndex < right.Length)
        {
            var leftIsDigit = char.IsAsciiDigit(left[leftIndex]);
            var rightIsDigit = char.IsAsciiDigit(right[rightIndex]);

            if (leftIsDigit && rightIsDigit)
            {
                var numericComparison = CompareDigitRuns(left, ref leftIndex, right, ref rightIndex);
                if (numericComparison != 0)
                {
                    return numericComparison;
                }

                continue;
            }

            var leftCharacter = char.ToUpperInvariant(left[leftIndex]);
            var rightCharacter = char.ToUpperInvariant(right[rightIndex]);
            if (leftCharacter != rightCharacter)
            {
                return leftCharacter.CompareTo(rightCharacter);
            }

            leftIndex++;
            rightIndex++;
        }

        return (left.Length - leftIndex).CompareTo(right.Length - rightIndex);
    }

    private static int CompareDigitRuns(
        string left,
        ref int leftIndex,
        string right,
        ref int rightIndex)
    {
        var leftStart = leftIndex;
        var rightStart = rightIndex;

        while (leftIndex < left.Length && char.IsAsciiDigit(left[leftIndex]))
        {
            leftIndex++;
        }

        while (rightIndex < right.Length && char.IsAsciiDigit(right[rightIndex]))
        {
            rightIndex++;
        }

        var leftSignificant = leftStart;
        var rightSignificant = rightStart;

        while (leftSignificant < leftIndex - 1 && left[leftSignificant] == '0')
        {
            leftSignificant++;
        }

        while (rightSignificant < rightIndex - 1 && right[rightSignificant] == '0')
        {
            rightSignificant++;
        }

        var leftLength = leftIndex - leftSignificant;
        var rightLength = rightIndex - rightSignificant;
        if (leftLength != rightLength)
        {
            return leftLength.CompareTo(rightLength);
        }

        for (var offset = 0; offset < leftLength; offset++)
        {
            var comparison = left[leftSignificant + offset].CompareTo(right[rightSignificant + offset]);
            if (comparison != 0)
            {
                return comparison;
            }
        }

        return (leftIndex - leftStart).CompareTo(rightIndex - rightStart);
    }
}
