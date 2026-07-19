using Wallpaper.Core.Sorting;

namespace Wallpaper.Core.Tests;

public sealed class FolderOrderPolicyTests
{
    [Fact]
    public void Merge_PreservesKnownUserOrderAndAppendsNewFoldersInNaturalOrder()
    {
        string[] natural = ["folder:A", "folder:B", "folder:C", "folder:D"];
        string[] persisted = ["folder:C", "folder:A", "folder:REMOVED"];

        var result = FolderOrderPolicy.Merge(natural, persisted);

        Assert.Equal(["folder:C", "folder:A", "folder:B", "folder:D"], result);
    }

    [Fact]
    public void Merge_IgnoresDuplicateIdsWithoutChangingTheirCanonicalCasing()
    {
        string[] natural = ["folder:WORK", "folder:Photos"];
        string[] persisted = ["folder:photos", "folder:PHOTOS", "folder:work"];

        var result = FolderOrderPolicy.Merge(natural, persisted);

        Assert.Equal(["folder:Photos", "folder:WORK"], result);
    }

    [Theory]
    [InlineData(false, "B,A,C,D")]
    [InlineData(true, "B,C,A,D")]
    public void Move_InsertsBeforeOrAfterTarget(bool insertAfter, string expected)
    {
        var result = FolderOrderPolicy.Move(["A", "B", "C", "D"], "A", "C", insertAfter);

        Assert.Equal(expected.Split(','), result);
    }
}
