using System.Diagnostics;
using System.Text;

namespace PathVeer.Core.SystemTools;

public sealed class CommandRunner
{
    public async Task<CommandResult> RunAsync(
        string executable,
        string arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        using Process process = new();

        process.StartInfo = new ProcessStartInfo
        {
            FileName = executable,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        if (!process.Start())
        {
            throw new InvalidOperationException(
                $"Failed to start '{executable}'.");
        }

        Task<string> stdoutTask =
            process.StandardOutput.ReadToEndAsync(cancellationToken);

        Task<string> stderrTask =
            process.StandardError.ReadToEndAsync(cancellationToken);

        using CancellationTokenSource timeoutSource =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);

        timeoutSource.CancelAfter(timeout);

        try
        {
            await process.WaitForExitAsync(timeoutSource.Token);
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Process may already have exited.
            }

            throw new TimeoutException(
                $"'{executable}' exceeded the timeout of {timeout}.");
        }

        return new CommandResult
        {
            ExitCode = process.ExitCode,
            StandardOutput = await stdoutTask,
            StandardError = await stderrTask
        };
    }
}