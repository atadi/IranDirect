using IranDirect.Core.Cli;
using IranDirect.Core.Ipc;

namespace IranDirect.Cli;

public static class ExecutionPreviewCliRunner
{
    private const int ExitSuccess = 0;
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

        if (args.Length > 0)
        {
            stderr.WriteLine(
                "Usage: IranDirect.Cli plan");
            return ExitUsage;
        }

        try
        {
            ServiceResponse response =
                await sender.SendAsync(
                    IranDirectCommand.ExecutionPreview);

            if (!response.Success)
            {
                string prefix =
                    string.IsNullOrWhiteSpace(response.ErrorCode)
                        ? ""
                        : $"[{response.ErrorCode}] ";

                stderr.WriteLine(prefix + response.Message);
                return ExitFailed;
            }

            if (response.Preview is null)
            {
                stderr.WriteLine(
                    "The service did not return " +
                    "an execution preview.");
                return ExitFailed;
            }

            foreach (string line in
                     ExecutionPreviewCliRenderer.Render(
                         response.Preview))
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
