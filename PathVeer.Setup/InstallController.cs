using System.Diagnostics;
using System.Text;
using System.Text.Json;
using PathVeer.Core.Installer;

namespace PathVeer.Setup;

/// <summary>
/// Progress stage names emitted by Install-PathVeer.ps1 (Write-ProgressRecord).
/// Kept in one place so the UI and tests agree on the vocabulary.
/// </summary>
public static class ProgressStages
{
    public const string VerifyingPackage = "VerifyingPackage";
    public const string StoppingLegacy = "StoppingLegacy";
    public const string StoppingService = "StoppingService";
    public const string Installing = "Installing";
    public const string StartingService = "StartingService";
    public const string CreatingShortcuts = "CreatingShortcuts";
    public const string RemovingShortcuts = "RemovingShortcuts";
    public const string ReleasingRoutes = "ReleasingRoutes";
    public const string Finished = "Finished";
    public const string ServiceFailed = "ServiceFailed";
    public const string ReadinessFailed = "ReadinessFailed";
    public const string DowngradeBlocked = "DowngradeBlocked";
    public const string PackageVerificationFailed = "PackageVerificationFailed";
    public const string PurgeFailed = "PurgeFailed";
}

/// <summary>
/// A single progress record from the deployment script.
/// </summary>
public sealed class ProgressRecord
{
    public string Stage { get; init; } = "";
    public string Message { get; init; } = "";
    public DateTimeOffset Timestamp { get; init; }
}

/// <summary>
/// The final result record written by the deployment script.
/// </summary>
public sealed class ResultRecord
{
    public bool Success { get; init; }
    public string Category { get; init; } = "Success";
    public string Message { get; init; } = "";
    public string? Version { get; init; }
    public DateTimeOffset Timestamp { get; init; }
}

/// <summary>
/// User-selectable install options collected by the UI before invoking the
/// authoritative deployment contract. The UI owns no install semantics.
/// </summary>
public sealed class InstallOptions
{
    public bool InstallTray { get; init; } = true;
    public bool LaunchTrayAfterInstall { get; init; } = true;
    public bool RegisterShell { get; init; } = true;
}

public sealed class UninstallOptions
{
    public bool PurgeState { get; init; }
}

/// <summary>
/// Orchestrates a single install/uninstall operation by invoking the embedded
/// PowerShell deployment script and translating its structured output (progress
/// file + result file + process exit code) into controller events. This class
/// contains NO install/migration/state logic — it delegates entirely to the
/// authoritative Install-PathVeer.ps1 + PathVeer.Core.Installation contract.
/// </summary>
public sealed class InstallController
{
    private readonly string _scriptPath;
    private readonly string _packageDirectory;

    public event Action<ProgressRecord>? Progress;
    public event Action<string>? LogLine;
    public event Action<ResultRecord>? Completed;

    public InstallController(string scriptPath, string packageDirectory)
    {
        _scriptPath = scriptPath;
        _packageDirectory = packageDirectory;
    }

    public int RunInstall(InstallOptions options, CancellationToken ct = default)
    {
        var args = BuildArgs("install", options.RegisterShell, installTray: options.InstallTray);
        return Execute(args, ct);
    }

    public int RunUninstall(UninstallOptions options, bool registerShell, CancellationToken ct = default)
    {
        var args = BuildArgs("uninstall", registerShell, purge: options.PurgeState);
        return Execute(args, ct);
    }

    public int RunStatus(CancellationToken ct = default)
    {
        var args = new List<string> { "-Action", "status" };
        return Execute(args, ct);
    }

    /// <summary>
    /// The authoritative exit-code resolution the bootstrapper returns for a
    /// completed operation. Success yields 0; any failure yields the stable code
    /// mapped from the deployment script's <c>category</c> (via
    /// <see cref="SetupExitCodes.MapResultCategory"/>). This is the single path
    /// <see cref="Execute"/> uses, and the same mapping the interactive form
    /// applies to the <see cref="Completed"/> result — so console, unattended,
    /// and UI paths cannot disagree (NEW ISSUE #2 contract).
    /// </summary>
    public int ResolveExitCode(ResultRecord result)
        => result.Success ? SetupExitCodes.Success : MapCategory(result.Category);

    /// <summary>
    /// Queries the authoritative deployment contract for the current machine
    /// state. Pure detection — no mutation.
    /// </summary>
    public InstallState DetectState()
    {
        string tempFile = Path.Combine(
            Path.GetTempPath(),
            "PathVeer.State." + Guid.NewGuid().ToString("N") + ".json");

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = TryFindExecutable("pwsh.exe") ? "pwsh.exe" : "powershell.exe",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            psi.ArgumentList.Add("-NoLogo");
            psi.ArgumentList.Add("-NoProfile");
            psi.ArgumentList.Add("-ExecutionPolicy");
            psi.ArgumentList.Add("Bypass");
            psi.ArgumentList.Add("-File");
            psi.ArgumentList.Add(_scriptPath);
            psi.ArgumentList.Add("-Action");
            psi.ArgumentList.Add("statejson");
            psi.ArgumentList.Add("-StateFile");
            psi.ArgumentList.Add(tempFile);

            using var proc = new Process { StartInfo = psi };
            if (!proc.Start()) return new InstallState();

            string json = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(15000);

            if (File.Exists(tempFile))
            {
                json = File.ReadAllText(tempFile);
            }

            return InstallStateClassifier.ParseStateJson(json);
        }
        catch
        {
            return new InstallState();
        }
        finally
        {
            try { File.Delete(tempFile); } catch { /* ignore */ }
        }
    }

    private List<string> BuildArgs(string action, bool registerShell, bool installTray = false, bool purge = false)
    {
        var args = new List<string>
        {
            "-Action", action,
            "-PackageDirectory", _packageDirectory,
        };

        if (action == "install" && installTray) args.Add("-InstallTray");
        if (action == "uninstall" && purge) args.Add("-PurgeState");
        if (registerShell) args.Add("-RegisterShell");

        return args;
    }

    private int Execute(List<string> actionArgs, CancellationToken ct)
    {
        string tempDir = Path.Combine(
            Path.GetTempPath(),
            "PathVeer.Setup." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        string progressFile = Path.Combine(tempDir, "progress.jsonl");
        string resultFile = Path.Combine(tempDir, "result.json");

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "pwsh.exe",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            if (!TryFindExecutable("pwsh.exe"))
            {
                psi.FileName = "powershell.exe";
            }

            psi.ArgumentList.Add("-NoLogo");
            psi.ArgumentList.Add("-NoProfile");
            psi.ArgumentList.Add("-ExecutionPolicy");
            psi.ArgumentList.Add("Bypass");
            psi.ArgumentList.Add("-File");
            psi.ArgumentList.Add(_scriptPath);
            foreach (string a in actionArgs)
            {
                psi.ArgumentList.Add(a);
            }

            psi.ArgumentList.Add("-ProgressFile");
            psi.ArgumentList.Add(progressFile);
            psi.ArgumentList.Add("-ResultFile");
            psi.ArgumentList.Add(resultFile);

            using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };

            proc.OutputDataReceived += (_, e) =>
            {
                if (e.Data is not null) LogLine?.Invoke(e.Data);
            };
            proc.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is not null) LogLine?.Invoke(e.Data);
            };

            if (!proc.Start())
            {
                return SetupExitCodes.PowerShellUnavailable;
            }

            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            // Stream progress records as the script appends them.
            var reader = Task.Run(() => StreamProgress(progressFile, ct));

            try
            {
                proc.WaitForExit();
            }
            finally
            {
                try { reader.Wait(ct); } catch { /* ignore */ }
            }

            ResultRecord? result = ReadResult(resultFile);
            if (result is not null)
            {
                Completed?.Invoke(result);
                return result.Success ? SetupExitCodes.Success : MapCategory(result.Category);
            }

            // Fallback to process exit code if no result file was written.
            return MapProcessExit(proc.ExitCode);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            LogLine?.Invoke("ERROR: could not start PowerShell to run the installer.");
            return SetupExitCodes.PowerShellUnavailable;
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* ignore */ }
        }
    }

    private void StreamProgress(string progressFile, CancellationToken ct)
    {
        long position = 0;
        var sw = new Stopwatch();
        sw.Start();
        while (sw.Elapsed.TotalSeconds < 600)
        {
            if (File.Exists(progressFile))
            {
                using var fs = new FileStream(
                    progressFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                fs.Seek(position, SeekOrigin.Begin);
                using var reader = new StreamReader(fs, Encoding.UTF8);
                string? line;
                while ((line = reader.ReadLine()) is not null)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    try
                    {
                        var rec = JsonSerializer.Deserialize<ProgressRecord>(line);
                        if (rec is not null) Progress?.Invoke(rec);
                    }
                    catch { /* ignore malformed line */ }
                }

                position = fs.Position;
            }

            if (ct.IsCancellationRequested) return;
            Thread.Sleep(150);
        }
    }

    private static ResultRecord? ReadResult(string resultFile)
    {
        if (!File.Exists(resultFile)) return null;
        try
        {
            string json = File.ReadAllText(resultFile);
            return JsonSerializer.Deserialize<ResultRecord>(json);
        }
        catch
        {
            return null;
        }
    }

    private static int MapCategory(string category)
        => SetupExitCodes.MapResultCategory(category);

    private static int MapProcessExit(int code) => code switch
    {
        0 => SetupExitCodes.Success,
        >= 100 and <= 109 => code,
        _ => SetupExitCodes.GenericFailure,
    };

    private static bool TryFindExecutable(string name)
    {
        string? pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (pathEnv is null) return false;
        foreach (string dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            if (File.Exists(Path.Combine(dir, name))) return true;
        }

        return false;
    }
}
