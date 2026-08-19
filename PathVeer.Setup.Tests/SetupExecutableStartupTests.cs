using System.Diagnostics;
using Xunit;
using Xunit.Abstractions;

namespace PathVeer.Setup.Tests;

/// <summary>
/// Executable startup smoke test for the PUBLISHED WindowsGui Setup bootstrapper.
///
/// A direct call to Program.Main would run inside the test runner's console and
/// therefore would NOT reproduce the devsign.8 regression (Console.Title only
/// throws when the process genuinely has no console). So this test publishes the
/// real single-file WindowsGui EXE and launches it as a detached process with no
/// attached console — exactly the Explorer double-click / Start-Process model —
/// then asserts it exits cleanly with no CLR crash (Event 1026) and no
/// Application Error (Event 1000).
/// </summary>
public class SetupExecutableStartupTests
{
    private readonly ITestOutputHelper _output;

    public SetupExecutableStartupTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static string PublishSetupExe()
    {
        // Publish the real WindowsGui single-file Setup to a temp dir so we
        // exercise the actual subsystem (no console), not the in-process Main.
        string temp = Path.Combine(
            Path.GetTempPath(), "PathVeer.Setup.Smoke." + Path.GetRandomFileName());
        Directory.CreateDirectory(temp);

        string setupProj = Path.GetFullPath(
            Path.Combine(
                AppContext.BaseDirectory,
                "..", "..", "..", "..",
                "PathVeer.Setup", "PathVeer.Setup.csproj"));

        if (!File.Exists(setupProj))
        {
            throw new FileNotFoundException("Setup project not found.", setupProj);
        }

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"publish \"{setupProj}\" -c Release -r win-x64 " +
                        $"-p:PublishSingleFile=true -o \"{temp}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var p = Process.Start(psi)
            ?? throw new InvalidOperationException("dotnet publish failed to start.");
        string stdout = p.StandardOutput.ReadToEnd();
        string stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();

        if (p.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"dotnet publish exited {p.ExitCode}.\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
        }

        string? exe = Directory.EnumerateFiles(temp, "PathVeer.Setup.exe")
            .FirstOrDefault()
            ?? Directory.EnumerateFiles(temp, "PathVeerSetup*.exe")
                .FirstOrDefault();
        if (exe is null)
        {
            throw new FileNotFoundException(
                "Published EXE not found in " + temp, temp);
        }

        return exe;
    }

    private static int LaunchDetached(string exe, string arguments)
    {
        // Start-Process / detached model: no console attached, no redirection.
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = arguments,
            UseShellExecute = false,   // do NOT inherit/allocate a console
            CreateNoWindow = true,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
        };

        using var p = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start Setup EXE.");
        p.WaitForExit();
        return p.ExitCode;
    }

    private static int CountEvents(string logName, long instanceId, string? contains = null)
    {
        try
        {
            var entries = (System.Diagnostics.EventLog.GetEventLogs()
                .FirstOrDefault(l => l.Log == logName) ?? null)?
                .Entries.Cast<System.Diagnostics.EventLogEntry>()
                .Where(e => e.InstanceId == instanceId);
            if (contains is not null)
            {
                entries = entries!.Where(e =>
                    (e.Message?.Contains(contains, StringComparison.OrdinalIgnoreCase) ?? false));
            }
            return entries?.Count() ?? 0;
        }
        catch
        {
            return -1; // event log unavailable (e.g. non-Windows CI)
        }
    }

    [Fact]
    public void Published_WinExe_Help_ExitsZero_NoCrash()
    {
        string exe = PublishSetupExe();
        _output.WriteLine($"Published EXE: {exe}");

        // Baseline event counts before launch.
        int before1026 = CountEvents("Application", 1026, "PathVeerSetup");
        int before1000 = CountEvents("Application", 1000, "PathVeerSetup");

        int exit = LaunchDetached(exe, "--help");

        int after1026 = CountEvents("Application", 1026, "PathVeerSetup");
        int after1000 = CountEvents("Application", 1000, "PathVeerSetup");

        _output.WriteLine($"--help exit code: {exit}");
        _output.WriteLine($"Event 1026 delta: {after1026 - before1026}");
        _output.WriteLine($"Event 1000 delta: {after1000 - before1000}");

        Assert.Equal(0, exit);
        Assert.Equal(0, after1026 - before1026);
        Assert.Equal(0, after1000 - before1000);
    }
}
