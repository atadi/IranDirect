using IranDirect.Core.Ipc;

namespace IranDirect.Tray;

public static class SupportBundleTrayFlow
{
    public static async Task ExportAsync(
        ICustomRouteCommandSender sender,
        ISupportBundleDialog dialog,
        ISupportBundleStatusSink statusSink,
        TimeProvider timeProvider,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sender);
        ArgumentNullException.ThrowIfNull(dialog);
        ArgumentNullException.ThrowIfNull(statusSink);
        ArgumentNullException.ThrowIfNull(timeProvider);

        SupportBundleRequest request = new(
            SupportBundleDefaultFileName.Build(timeProvider));

        SupportBundleRequestResult? promptResult =
            dialog.Prompt(request);

        if (promptResult is null || !promptResult.ChosePath)
        {
            return;
        }

        await ExportToAsync(
            sender,
            promptResult.Path!,
            statusSink,
            cancellationToken);
    }

    public static async Task ExportToAsync(
        ICustomRouteCommandSender sender,
        string outputPath,
        ISupportBundleStatusSink statusSink,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sender);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            outputPath);
        ArgumentNullException.ThrowIfNull(statusSink);

        try
        {
            ServiceResponse response =
                await sender.SendAsync(
                    IranDirectCommand.SupportBundleExport,
                    outputPath,
                    cancellationToken: cancellationToken);

            if (!response.Success)
            {
                statusSink.ShowError(response.Message);
                return;
            }

            statusSink.ShowSuccess(
                new SupportBundleExportOutcome(
                    Success: true,
                    OutputPath:
                        response.SupportBundlePath
                        ?? outputPath,
                    BytesWritten:
                        response.SupportBundleBytesWritten,
                    Message: response.Message));
        }
        catch (Exception exception)
        {
            statusSink.ShowError(exception.Message);
        }
    }
}
