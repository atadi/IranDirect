using PathVeer.Core.Ipc;

namespace PathVeer.Tray;

public sealed class SupportBundleRequest
{
    public SupportBundleRequest(
        string defaultFileName)
    {
        if (string.IsNullOrWhiteSpace(defaultFileName))
        {
            throw new ArgumentException(
                "Default file name is required.",
                nameof(defaultFileName));
        }

        DefaultFileName = defaultFileName;
    }

    public string DefaultFileName { get; }
}

public sealed record SupportBundleRequestResult(
    bool ChosePath,
    string? Path);

public sealed record SupportBundleExportOutcome(
    bool Success,
    string? OutputPath,
    long? BytesWritten,
    string Message);

public interface ISupportBundleDialog
{
    SupportBundleRequestResult? Prompt(
        SupportBundleRequest request);
}

public interface ISupportBundleStatusSink
{
    void ShowSuccess(SupportBundleExportOutcome outcome);

    void ShowError(string message);
}

public static class SupportBundleDefaultFileName
{
    public static string Build(
        TimeProvider timeProvider,
        DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        DateTimeOffset stamp = now
            ?? timeProvider.GetUtcNow()
                .ToLocalTime();

        string fileName =
            $"IranDirect-Support-{stamp:yyyyMMdd-HHmmss}.zip";

        return fileName;
    }
}
