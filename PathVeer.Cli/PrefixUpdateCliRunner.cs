using PathVeer.Core.Cli;
using PathVeer.Core.Ipc;
using PathVeer.Core.Prefixes;

namespace PathVeer.Cli;

public static class PrefixUpdateCliRunner
{
    private const int ExitSuccess = 0;
    private const int ExitFailure = 1;
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

        string subcommand =
            args.FirstOrDefault()?.ToLowerInvariant() ?? "";

        if (subcommand != "check" || args.Length > 1)
        {
            stderr.WriteLine(
                "Usage: IranDirect.Cli prefix-update check");
            return ExitUsage;
        }

        try
        {
            ServiceResponse response =
                await sender.SendAsync(
                    IranDirectCommand.PrefixUpdateCheckNow);

            if (!response.Success)
            {
                string prefix =
                    string.IsNullOrWhiteSpace(response.ErrorCode)
                        ? ""
                        : $"[{response.ErrorCode}] ";

                stderr.WriteLine(prefix + response.Message);
                return ExitFailure;
            }

            if (response.PrefixUpdateMonitor is not { } snapshot)
            {
                stderr.WriteLine(
                    "The service did not return prefix " +
                    "update monitor state.");
                return ExitFailure;
            }

            foreach (string line in
                     PrefixUpdateCheckCliRenderer.Render(
                         snapshot))
            {
                stdout.WriteLine(line);
            }

            return snapshot.CurrentResult?.Status ==
                   PrefixUpdateCheckStatus.Failed
                ? ExitFailure
                : ExitSuccess;
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
                $"IranDirect command failed: " +
                $"{exception.Message}");
            return ExitFailure;
        }
    }
}
