using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
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

            // Stream progress records as the script appends them. The tailer is
            // terminated by CHILD PROCESS EXIT (see progressCts below), not by a
            // fixed timeout: once proc.WaitForExit returns, we cancel the tailer
            // and drain any final records deterministically, then read the
            // result. This prevents the previous 600s post-exit hang.
            var tailer = new ProgressTailer(progressFile, rec => Progress?.Invoke(rec));
            var progressCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var reader = Task.Run(() => tailer.RunLoop(progressCts.Token));

            try
            {
                proc.WaitForExit();
            }
            finally
            {
                // Child has exited: stop the tailer, then drain the final
                // records (the script writes the "Finished" progress line and
                // result.json immediately before exiting, so both are fully
                // flushed by the time WaitForExit returns). Cancelling first and
                // waiting for the reader to stop avoids racing the final write;
                // the drain then observes any records the loop had not yet seen.
                progressCts.Cancel();
                try { reader.Wait(TimeSpan.FromSeconds(2)); }
                catch { /* ignore */ }

                try { tailer.Drain(); }
                catch { /* ignore */ }
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

    // Tailers the deployment progress file. The looping variant (RunLoop) is
    // terminated by child-process exit / caller cancellation, NEVER by a fixed
    // timeout (the 600s cap is only a defensive backstop). Drain() performs one
    // final read to EOF so the last record (e.g. "Finished") and result.json are
    // always observed after the child exits. The shared position guarantees each
    // record is raised exactly once across loop and drain.
    private sealed class ProgressTailer
    {
        private long _position;
        private readonly string _file;
        private readonly Action<ProgressRecord> _onRecord;

        public ProgressTailer(string file, Action<ProgressRecord> onRecord)
        {
            _file = file;
            _onRecord = onRecord;
        }

        public void RunLoop(CancellationToken ct)
        {
            // Defensive maximum only; normal termination is signal-driven.
            var sw = Stopwatch.StartNew();
            while (!ct.IsCancellationRequested && sw.Elapsed.TotalSeconds < 600)
            {
                PumpOnce();
                if (ct.IsCancellationRequested) break;
                Thread.Sleep(150);
            }
        }

        // Final read to EOF after the child process has exited. Guarantees the
        // last progress record (Finished) is observed without racing the cancel.
        public void Drain() => PumpOnce();

        private void PumpOnce()
        {
            if (!File.Exists(_file)) return;
            using var fs = new FileStream(
                _file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            fs.Seek(_position, SeekOrigin.Begin);
            using var reader = new StreamReader(fs, Encoding.UTF8);
            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    var rec = JsonSerializer.Deserialize<ProgressRecord>(line, JsonOptions);
                    if (rec is not null) _onRecord(rec);
                }
                catch
                {
                    // ignore malformed line; later valid records still processed
                }
            }

            _position = fs.Position;
        }
    }

    private static ResultRecord? ReadResult(string resultFile)
    {
        if (!File.Exists(resultFile)) return null;
        try
        {
            string json = File.ReadAllText(resultFile);
            return JsonSerializer.Deserialize<ResultRecord>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// The deployment script (Install-PathVeer.ps1) emits progress/result records
    /// with LOWERCASE JSON keys (PowerShell's ConvertTo-Json default), while the
    /// C# record types use PascalCase properties. System.Text.Json is
    /// case-sensitive by default, so without this option every record would
    /// deserialize with default (empty/false) values — making the controller
    /// report a failed install on a successful backend. Both pwsh.exe and
    /// Windows PowerShell 5.1 emit lowercase keys, so case-insensitive matching
    /// is the correct contract for the real script and for the tests.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

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
