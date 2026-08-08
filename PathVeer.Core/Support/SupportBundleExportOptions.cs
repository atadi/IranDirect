namespace PathVeer.Core.Support;

public sealed record SupportBundleExportOptions
{
    public static SupportBundleExportOptions Default { get; } =
        new();

    public bool OverwriteExisting { get; init; } = true;

    public string ZipTempSuffix { get; init; } = ".tmp";

    public string SnapshotEntryName { get; init; } =
        "support-snapshot.json";
}
