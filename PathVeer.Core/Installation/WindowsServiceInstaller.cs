using System.Diagnostics;
using System.Runtime.Versioning;
using System.ServiceProcess;

namespace PathVeer.Core.Installation;

/// <summary>
/// Real SCM implementation of <see cref="IServiceInstaller"/>, driving
/// <c>sc.exe</c>. Used by the installer at runtime; never by unit tests, which
/// use fakes instead so the developer's real service database is untouched.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsServiceInstaller : IServiceInstaller
{
    private readonly TimeSpan _commandTimeout;

    public WindowsServiceInstaller(TimeSpan? commandTimeout = null)
    {
        _commandTimeout = commandTimeout ?? TimeSpan.FromSeconds(30);
    }

    public void InstallOrUpdate(
        string serviceName,
        string displayName,
        string binaryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(binaryPath);

        string quoted = "\"" + binaryPath + "\"";

        string verb = Exists(serviceName) ? "config" : "create";

        RunSc(
            verb,
            serviceName,
            "binPath=", quoted,
            "start=", "delayed-auto",
            "displayName=", displayName);
    }

    public string? GetBinaryPath(string serviceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);

        if (!Exists(serviceName))
        {
            return null;
        }

        ScResult result = RunScRaw("qc", serviceName);

        if (result.ExitCode != 0)
        {
            return null;
        }

        foreach (string line in result.StandardOutput.Split('\n'))
        {
            int marker = line.IndexOf(
                "BINARY_PATH_NAME",
                StringComparison.OrdinalIgnoreCase);

            if (marker < 0)
            {
                continue;
            }

            int separator = line.IndexOf(':', marker);

            if (separator < 0)
            {
                continue;
            }

            return line[(separator + 1)..].Trim().Trim('"');
        }

        return null;
    }

    public void Disable(string serviceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);

        if (!Exists(serviceName))
        {
            return;
        }

        RunSc("config", serviceName, "start=", "disabled");
    }

    public bool TryDelete(string serviceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);

        if (!Exists(serviceName))
        {
            return true;
        }

        ScResult result = RunScRaw("delete", serviceName);

        if (result.ExitCode != 0)
        {
            return false;
        }

        // Deletion is asynchronous when a handle is still open (Services.msc).
        DateTimeOffset deadline =
            DateTimeOffset.UtcNow.AddSeconds(15);

        while (Exists(serviceName)
               && DateTimeOffset.UtcNow < deadline)
        {
            Thread.Sleep(500);
        }

        return !Exists(serviceName);
    }

    private static bool Exists(string serviceName)
    {
        try
        {
            using ServiceController controller = new(serviceName);
            _ = controller.Status;

            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private void RunSc(params string[] arguments)
    {
        ScResult result = RunScRaw(arguments);

        if (result.ExitCode != 0)
        {
            throw new InstallFailedException(
                $"sc.exe {arguments[0]} failed with exit code "
                + $"{result.ExitCode}: {result.StandardError.Trim()}");
        }
    }

    private ScResult RunScRaw(params string[] arguments)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = "sc.exe",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process =
            Process.Start(startInfo)
            ?? throw new InstallFailedException(
                "Failed to start sc.exe.");

        string standardOutput = process.StandardOutput.ReadToEnd();
        string standardError = process.StandardError.ReadToEnd();

        if (!process.WaitForExit((int)_commandTimeout.TotalMilliseconds))
        {
            throw new InstallFailedException(
                "sc.exe did not exit within the command timeout.");
        }

        return new ScResult(
            process.ExitCode,
            standardOutput,
            standardError);
    }

    private readonly record struct ScResult(
        int ExitCode,
        string StandardOutput,
        string StandardError);
}
