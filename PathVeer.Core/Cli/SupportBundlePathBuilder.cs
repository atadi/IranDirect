namespace PathVeer.Core.Cli;

public static class SupportBundlePathBuilder
{
    public const string DefaultFilePrefix = "IranDirect-Support";

    public static string CreateDefaultPath(
        TimeProvider timeProvider,
        string? tempRootOverride = null)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        string root = string.IsNullOrWhiteSpace(tempRootOverride)
            ? Path.GetTempPath()
            : tempRootOverride;

        string directory = Path.Combine(
            root,
            "IranDirect",
            "SupportBundles");

        Directory.CreateDirectory(directory);

        DateTimeOffset now = timeProvider.GetUtcNow()
            .ToLocalTime();

        string stamp = now.ToString("yyyyMMdd-HHmmss");

        string fileName =
            $"{DefaultFilePrefix}-{stamp}.zip";

        return Path.Combine(directory, fileName);
    }
}
