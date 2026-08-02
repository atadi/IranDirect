namespace IranDirect.Core.Support;

public sealed record SupportSnapshotExportOptions
{
    public static SupportSnapshotExportOptions Default { get; } =
        new();

    public bool OverwriteExisting { get; init; } = true;

    public string TempSuffix { get; init; } = ".tmp";
}
