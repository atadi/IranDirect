using PathVeer.Core.Support;

namespace PathVeer.Core.Ipc;

public sealed class SupportBundleCommandHandler
{
    private readonly ISupportBundleExporter _exporter;

    public SupportBundleCommandHandler(
        ISupportBundleExporter exporter)
    {
        ArgumentNullException.ThrowIfNull(exporter);

        _exporter = exporter;
    }

    public async Task<ServiceResponse> ExportAsync(
        string outputPath,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            return new ServiceResponse
            {
                Success = false,
                ErrorCode = "INVALID_BUNDLE_PATH",
                Message =
                    "An output path is required."
            };
        }

        SupportBundleExportResult result =
            await _exporter.ExportAsync(
                outputPath,
                cancellationToken);

        return new ServiceResponse
        {
            Success = true,
            Message =
                $"Support bundle written: " +
                $"{result.BundlePath} " +
                $"({result.BytesWritten} bytes).",
            SupportBundlePath = result.BundlePath,
            SupportBundleBytesWritten = result.BytesWritten
        };
    }
}
