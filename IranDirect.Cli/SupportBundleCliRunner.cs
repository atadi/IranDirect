using IranDirect.Core.Cli;
using IranDirect.Core.Support;

namespace IranDirect.Cli;

public static class SupportBundleCliRunner
{
    private const int ExitSuccess = 0;
    private const int ExitExporterFailed = 1;
    private const int ExitTimeout = 3;
    private const int ExitCanceled = 4;
    private const int ExitUsage = 6;

    public static async Task<int> RunAsync(
        string[] args,
        ISupportBundleExporter exporter,
        TextWriter? stdout = null,
        TextWriter? stderr = null,
        TimeProvider? timeProvider = null,
        string? tempRootOverride = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(exporter);

        stdout ??= Console.Out;
        stderr ??= Console.Error;
        timeProvider ??= TimeProvider.System;

        SupportBundleCliParseResult parsed =
            SupportBundleCliParser.Parse(args);

        if (!parsed.IsValid)
        {
            stderr.WriteLine(
                "Usage: IranDirect.Cli support-bundle [<path>]");
            if (!string.IsNullOrWhiteSpace(parsed.Error))
            {
                stderr.WriteLine($"Error: {parsed.Error}");
            }

            return ExitUsage;
        }

        string outputPath = parsed.OutputPath
            ?? SupportBundlePathBuilder.CreateDefaultPath(
                timeProvider,
                tempRootOverride);

        try
        {
            SupportBundleExportResult result =
                await exporter.ExportAsync(
                    outputPath,
                    cancellationToken);

            foreach (string line in
                     SupportBundleCliRenderer.Render(result))
            {
                stdout.WriteLine(line);
            }

            return ExitSuccess;
        }
        catch (TimeoutException exception)
        {
            stderr.WriteLine(exception.Message);
            return ExitTimeout;
        }
        catch (OperationCanceledException)
        {
            stderr.WriteLine("The operation was canceled.");
            return ExitCanceled;
        }
        catch (Exception exception)
        {
            stderr.WriteLine(
                $"Support bundle export failed: " +
                $"{exception.Message}");
            return ExitExporterFailed;
        }
    }
}
