using IranDirect.Core.Cli;
using IranDirect.Core.Diagnostics;
using IranDirect.Core.Ipc;

namespace IranDirect.Cli;

public static class DiagnosticCliRunner
{
    private const int ExitHealthy = 0;
    private const int ExitWarnings = 1;
    private const int ExitFailed = 2;
    private const int ExitTimeout = 3;
    private const int ExitCanceled = 4;
    private const int ExitUsage = 6;

    public static async Task<int> RunAsync(
        string[] args,
        ICustomRouteCommandSender sender,
        TextWriter? stdout = null,
        TextWriter? stderr = null)
    {
        stdout ??= Console.Out;
        stderr ??= Console.Error;

        DiagnosticFormat format = DiagnosticFormat.Detailed;

        foreach (string arg in args)
        {
            string lower = arg.ToLowerInvariant();

            if (lower == "--summary")
            {
                format = DiagnosticFormat.Summary;
            }
            else if (lower == "--compact")
            {
                format = DiagnosticFormat.Compact;
            }
            else if (lower is "--detailed" or "-d")
            {
                format = DiagnosticFormat.Detailed;
            }
            else
            {
                stderr.WriteLine(
                    "Usage: IranDirect.Cli doctor " +
                    "[--summary|--detailed|--compact]");
                return ExitUsage;
            }
        }

        try
        {
            ServiceResponse response =
                await sender.SendAsync(
                    IranDirectCommand.Diagnostics);

            if (!response.Success)
            {
                string prefix =
                    string.IsNullOrWhiteSpace(response.ErrorCode)
                        ? ""
                        : $"[{response.ErrorCode}] ";

                stderr.WriteLine(prefix + response.Message);
                return ExitFailed;
            }

            DiagnosticReport? report = response.Report;

            if (report is null)
            {
                stderr.WriteLine(
                    "The service did not return " +
                    "a diagnostic report.");
                return ExitFailed;
            }

            foreach (string line in
                     DiagnosticReportCliRenderer.Render(
                         report, format))
            {
                stdout.WriteLine(line);
            }

            if (report.FailedCount > 0)
            {
                return ExitFailed;
            }

            if (report.WarningCount > 0)
            {
                return ExitWarnings;
            }

            return ExitHealthy;
        }
        catch (TimeoutException exception)
        {
            stderr.WriteLine(exception.Message);
            return ExitTimeout;
        }
        catch (OperationCanceledException)
        {
            stderr.WriteLine(
                "The operation was canceled.");
            return ExitCanceled;
        }
        catch (Exception exception)
        {
            stderr.WriteLine(
                $"IranDirect command failed: " +
                $"{exception.Message}");
            return ExitFailed;
        }
    }
}
