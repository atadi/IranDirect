using PathVeer.Core.Prefixes;

namespace PathVeer.Core.Tests.Prefixes;

public sealed class PrefixDatasetComparerTests
{
    [Fact]
    public void Compare_IdenticalDatasets_NoChanges()
    {
        PrefixDatasetDiff diff = PrefixDatasetComparer.Compare(
            ["1.2.3.0/24", "10.0.0.0/8"],
            ["1.2.3.0/24", "10.0.0.0/8"]);

        Assert.False(diff.HasChanges);
        Assert.Equal(0, diff.AddedCount);
        Assert.Equal(0, diff.RemovedCount);
        Assert.Equal(2, diff.UnchangedCount);
        Assert.Empty(diff.AddedPrefixes);
        Assert.Empty(diff.RemovedPrefixes);
    }

    [Fact]
    public void Compare_OnlyAdditions_ReportsAddedPrefixes()
    {
        PrefixDatasetDiff diff = PrefixDatasetComparer.Compare(
            ["1.2.3.0/24"],
            ["1.2.3.0/24", "10.0.0.0/8", "192.0.2.0/24"]);

        Assert.True(diff.HasChanges);
        Assert.Equal(2, diff.AddedCount);
        Assert.Equal(0, diff.RemovedCount);
        Assert.Equal(1, diff.UnchangedCount);
        Assert.Equal(
            ["10.0.0.0/8", "192.0.2.0/24"],
            diff.AddedPrefixes);
        Assert.Empty(diff.RemovedPrefixes);
    }

    [Fact]
    public void Compare_OnlyRemovals_ReportsRemovedPrefixes()
    {
        PrefixDatasetDiff diff = PrefixDatasetComparer.Compare(
            ["1.2.3.0/24", "10.0.0.0/8", "192.0.2.0/24"],
            ["1.2.3.0/24"]);

        Assert.True(diff.HasChanges);
        Assert.Equal(0, diff.AddedCount);
        Assert.Equal(2, diff.RemovedCount);
        Assert.Equal(1, diff.UnchangedCount);
        Assert.Empty(diff.AddedPrefixes);
        Assert.Equal(
            ["10.0.0.0/8", "192.0.2.0/24"],
            diff.RemovedPrefixes);
    }

    [Fact]
    public void Compare_MixedChanges_ReportsBothSides()
    {
        PrefixDatasetDiff diff = PrefixDatasetComparer.Compare(
            ["1.2.3.0/24", "10.0.0.0/8"],
            ["10.0.0.0/8", "192.0.2.0/24"]);

        Assert.True(diff.HasChanges);
        Assert.Equal(1, diff.AddedCount);
        Assert.Equal(1, diff.RemovedCount);
        Assert.Equal(1, diff.UnchangedCount);
        Assert.Equal(
            ["192.0.2.0/24"],
            diff.AddedPrefixes);
        Assert.Equal(
            ["1.2.3.0/24"],
            diff.RemovedPrefixes);
    }

    [Fact]
    public void Compare_DuplicateInput_Ignored()
    {
        PrefixDatasetDiff diff = PrefixDatasetComparer.Compare(
            ["1.2.3.0/24", "1.2.3.0/24"],
            ["1.2.3.0/24", "10.0.0.0/8", "10.0.0.0/8"]);

        Assert.True(diff.HasChanges);
        Assert.Equal(1, diff.AddedCount);
        Assert.Equal(0, diff.RemovedCount);
        Assert.Equal(1, diff.UnchangedCount);
        Assert.Equal(["10.0.0.0/8"], diff.AddedPrefixes);
    }

    [Fact]
    public void Compare_OrderingIndependence()
    {
        PrefixDatasetDiff forward = PrefixDatasetComparer.Compare(
            ["1.2.3.0/24", "10.0.0.0/8"],
            ["10.0.0.0/8", "192.0.2.0/24"]);
        PrefixDatasetDiff reversed = PrefixDatasetComparer.Compare(
            ["10.0.0.0/8", "1.2.3.0/24"],
            ["192.0.2.0/24", "10.0.0.0/8"]);

        Assert.Equal(forward.AddedCount, reversed.AddedCount);
        Assert.Equal(forward.RemovedCount, reversed.RemovedCount);
        Assert.Equal(forward.UnchangedCount, reversed.UnchangedCount);
        Assert.Equal(forward.HasChanges, reversed.HasChanges);
        Assert.Equal(forward.AddedPrefixes, reversed.AddedPrefixes);
        Assert.Equal(forward.RemovedPrefixes, reversed.RemovedPrefixes);
    }

    [Fact]
    public void Compare_CaseInsensitive()
    {
        PrefixDatasetDiff diff = PrefixDatasetComparer.Compare(
            ["ABC/24"],
            ["abc/24"]);

        Assert.False(diff.HasChanges);
        Assert.Equal(1, diff.UnchangedCount);
    }

    [Fact]
    public void Compare_BlankAndWhitespaceLines_Ignored()
    {
        PrefixDatasetDiff diff = PrefixDatasetComparer.Compare(
            ["1.2.3.0/24", ""],
            ["1.2.3.0/24", "   "]);

        Assert.False(diff.HasChanges);
        Assert.Equal(1, diff.UnchangedCount);
    }

    [Fact]
    public void Compare_SurroundingWhitespace_Normalized()
    {
        PrefixDatasetDiff diff = PrefixDatasetComparer.Compare(
            ["  1.2.3.0/24  "],
            ["1.2.3.0/24"]);

        Assert.False(diff.HasChanges);
    }

    [Fact]
    public void Compare_DoesNotMutateInputs()
    {
        string[] oldDataset = ["1.2.3.0/24", "10.0.0.0/8"];
        string[] newDataset = ["10.0.0.0/8", "192.0.2.0/24"];

        string[] oldCopy = (string[])oldDataset.Clone();
        string[] newCopy = (string[])newDataset.Clone();

        _ = PrefixDatasetComparer.Compare(
            oldDataset, newDataset);

        Assert.Equal(oldCopy, oldDataset);
        Assert.Equal(newCopy, newDataset);
    }

    [Fact]
    public void Compare_EmptyDatasets_NoChanges()
    {
        PrefixDatasetDiff diff = PrefixDatasetComparer.Compare(
            Array.Empty<string>(),
            Array.Empty<string>());

        Assert.False(diff.HasChanges);
        Assert.Equal(0, diff.UnchangedCount);
        Assert.Empty(diff.AddedPrefixes);
        Assert.Empty(diff.RemovedPrefixes);
    }
}
