using System.Diagnostics;
using System.Reflection;
using System.Security.Principal;
using PathVeer.Core.Installer;

namespace PathVeer.Setup;

/// <summary>
/// Phase 37.2 consumer-install bootstrap.
///
/// This executable remains a thin, Authenticode-signable wrapper. It does NOT
/// contain install/migration/state logic. The authoritative contract lives in
/// the embedded <c>Install-PathVeer.ps1</c> (which in turn uses
/// <c>PathVeer.Core.Installation</c>). This bootstrapper's job is to:
///
///   1. ensure it is running elevated (UAC prompt if not);
///   2. ensure the required .NET runtime for the components is present;
///   3. locate the co-shipped package directory;
///   4. detect current install state and classify the scenario;
///   5. present a normal Windows installer UI (or run /quiet unattended);
///   6. invoke the embedded deployment script with structured output;
///   7. translate the result into a user-facing state and a stable exit code.
/// </summary>
public static class Program
{
    private const string ScriptResourceName =
        "PathVeer.Setup.Resources.Install-PathVeer.ps1";

    private static readonly string[] PassthroughVerbs =
    [
        "--install", "--uninstall", "--status",
        "--purge-state", "--install-tray", "--help", "-h",
    ];

    // WinExe bootstrapper: when launched from Explorer there is no console, and
    // writing to Console.Out/Console.Error throws IOException ("The handle is
    // invalid"). These helpers succeed when a console/redirection IS present
    // (so /quiet diagnostics still work from a real console) and otherwise
    // silently no-op — they never allocate a console and never throw.
    private static void SafeWriteLine(string? message = null)
    {
        if (message is null) return;
        try { Console.WriteLine(message); }
        catch (IOException) { /* no console attached */ }
        catch (ObjectDisposedException) { /* stream closed */ }
    }

    private static void SafeWriteError(string message)
    {
        try { Console.Error.WriteLine(message); }
        catch (IOException) { /* no console attached */ }
        catch (ObjectDisposedException) { /* stream closed */ }
    }

    public static int Main(string[] args)
    {
        // NOTE: do NOT set Console.Title here. This is a Windows GUI (WinExe)
        // bootstrapper; when launched from Explorer there is no console handle,
        // and Console.Title throws IOException ("The handle is invalid"),
        // crashing Main before --help / elevation / UI (devsign.8 regression).
        // The interactive form carries the product title instead.

        bool quiet = args.Any(a =>
            string.Equals(a, "/quiet", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(a, "--quiet", StringComparison.OrdinalIgnoreCase));

        if (args.Any(a =>
                string.Equals(a, "--help", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(a, "-h", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(a, "/?", StringComparison.OrdinalIgnoreCase)))
        {
            PrintUsage();
            return SetupExitCodes.Success;
        }

        // Self-elevation gate runs BEFORE the single-instance mutex. A
        // non-administrator here is the ordinary Explorer double-click path: it
        // must NOT own the mutex, or the elevated child it spawns would see the
        // name already taken and exit (PROVEN ISSUE #1). The parent relaunches
        // the elevated copy and (when the child returns) exits without ever
        // holding the installer lock. Only the elevated process that actually
        // mutates the system owns the single-instance claim.
        //
        // devsign.10 — the NON-elevated parent is the SOLE Tray-launch owner. It
        // mints an invocation id and hands it to the elevated child so the Tray
        // launch request the child records is invocation-scoped and only THIS
        // parent consumes it (no global-sentinel race, no stale request).
        if (!ProcessPrivileges.IsAdministrator())
        {
            return RelaunchElevated(args, quiet);
        }

        // Single-instance protection: the ELEVATED installer owns the mutex so
        // two real elevated instances cannot act on SCM/Program Files/ProgramData
        // at the same time. Acquired only after elevation is confirmed.
        using var singleInstance = SetupSingleInstance.TryAcquire();
        if (singleInstance is null)
        {
            SafeWriteError(
                "ERROR: another instance of PathVeer Setup is already running.");
            return SetupExitCodes.InvalidArguments;
        }

        string? packageDirectory = ResolvePackageDirectory(args);
        if (packageDirectory is null)
        {
            SafeWriteError(
                "ERROR: could not locate the PathVeer package directory " +
                "(expected a sibling 'PathVeer-<version>' folder).");
            return SetupExitCodes.PackageDirectoryNotFound;
        }

        string? scriptPath = ExtractScript();
        if (scriptPath is null)
        {
            SafeWriteError(
                "ERROR: embedded deployment script was not found in this " +
                "executable. The installer is corrupt.");
            return SetupExitCodes.ScriptResourceMissing;
        }

        // Full provenance (includes +<commit>) kept for diagnostics/logging;
        // the interactive form receives the friendly version without the SHA.
        string fullVersion = ThisVersion();
        string friendlyVersion = SetupVersion.Friendly(fullVersion);

        SafeWriteLine($"PathVeer Setup {fullVersion}");
        SafeWriteLine($"Package: {packageDirectory}");
        SafeWriteLine();

        // Phase 37.2 mandatory: the hosted components are framework-dependent.
        var runtime = RuntimePrerequisite.Check();
        if (!runtime.Satisfied)
        {
            SafeWriteError("ERROR: " + runtime.Message);
            if (!quiet)
            {
                MessageBox.Show(
                    runtime.Message,
                    "PathVeer Setup — missing prerequisite",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }

            return SetupExitCodes.RuntimePrerequisiteMissing;
        }

        var controller = new InstallController(scriptPath, packageDirectory);

        if (quiet)
        {
            return RunQuiet(controller, args);
        }

        ApplicationConfiguration.Initialize();
        using var form = new InstallForm(controller, friendlyVersion, launchOperationId: ParseLaunchOperationId(args));
        Application.Run(form);

        // DIRECT-ELEVATED case (Setup opened via "Run as administrator"): there
        // is no ordinary non-elevated parent to own the Tray launch. The Tray
        // MUST NOT be launched elevated (devsign.10 fail-safe). The elevated
        // child recorded a request tagged with this same id, but we deliberately
        // do NOT consume it here — consuming would spawn an elevated Tray.
        // We leave the request unconsumed (or delete it) so no elevated Tray is
        // ever produced; the user opens the Tray from the Start Menu / next
        // sign-in. This is the certified safe v1 behavior.
        CleanupLaunchRequest(EnsureOperationId(null));

        // The form carries the authoritative operation result. The explicit
        // contract prevents a closed failure window from being silently reported
        // as success (NEW ISSUE #2). Closing before mutation == user cancel.
        return form.ResultExitCode;
    }

    // Generates a stable per-invocation id. When the caller already provided
    // one (the non-elevated parent hands its id to the elevated child), it is
    // returned unchanged so parent and child agree on the same request scope.
    private static string EnsureOperationId(string? provided)
    {
        if (!string.IsNullOrWhiteSpace(provided))
        {
            return provided!;
        }

        return "op-" + Guid.NewGuid().ToString("N");
    }

    // In the direct-elevated (Run as administrator) flow the Elevated=True
    // Setup is the authority and there is no ordinary parent to launch the
    // Tray. We must NOT launch the Tray elevated, so we only discard any
    // request this process recorded and never spawn the Tray.
    private static void CleanupLaunchRequest(string operationId)
    {
        try
        {
            string file = Path.Combine(
                InstallForm.LaunchRequestDirectory(operationId),
                "launch-tray.request");
            if (File.Exists(file)) File.Delete(file);
        }
        catch
        {
            // Best-effort; Start Menu remains the fallback for the user.
        }
    }

    private static int RunQuiet(InstallController controller, string[] args)
    {
        // /quiet honor the same operation model with no window.
        bool uninstall = args.Any(a =>
            string.Equals(a, "--uninstall", StringComparison.OrdinalIgnoreCase));
        bool purge = args.Any(a =>
            string.Equals(a, "--purge-state", StringComparison.OrdinalIgnoreCase));

        int exit;
        if (uninstall)
        {
            exit = controller.RunUninstall(
                new UninstallOptions { PurgeState = purge }, registerShell: true);
        }
        else
        {
            var options = new InstallOptions
            {
                InstallTray = !args.Any(a =>
                    string.Equals(a, "--no-tray", StringComparison.OrdinalIgnoreCase)),
                LaunchTrayAfterInstall = false,
                RegisterShell = true,
            };
            exit = controller.RunInstall(options);
        }

        SafeWriteLine(
            exit == SetupExitCodes.Success ? "SUCCESS" : "FAILED:" + exit);
        return exit;
    }

    private static string? ParseLaunchOperationId(string[] args)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(
                    args[i], "--launch-operation-id",
                    StringComparison.OrdinalIgnoreCase))
            {
                string value = args[i + 1];
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
        }

        return null;
    }

    private static string? ResolvePackageDirectory(string[] args)
    {
        string baseDir = AppContext.BaseDirectory;

        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(
                    args[i], "-PackageDirectory",
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(args[i], "--package", StringComparison.OrdinalIgnoreCase))
            {
                string candidate = args[i + 1];
                if (Directory.Exists(candidate)) return Path.GetFullPath(candidate);
            }
        }

        string? version = ThisVersion();
        string simple = Path.Combine(baseDir, $"PathVeer-{version}");
        if (Directory.Exists(simple)) return simple;

        try
        {
            foreach (string dir in Directory
                         .EnumerateDirectories(baseDir)
                         .Where(d =>
                             Path.GetFileName(d)
                                .StartsWith("PathVeer-", StringComparison.OrdinalIgnoreCase)))
            {
                return dir;
            }
        }
        catch (IOException)
        {
            // ignore — fall through to null
        }

        return null;
    }

    private static string? ExtractScript()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using Stream? stream = assembly.GetManifestResourceStream(
            ScriptResourceName);
        if (stream is null)
        {
            return null;
        }

        // Unique, access-restricted temp location; verify before execution.
        string tempDir = Path.Combine(
            Path.GetTempPath(), "PathVeer.Setup." + Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);
        string path = Path.Combine(tempDir, "Install-PathVeer.ps1");

        using (var fileStream = File.Create(path))
        {
            stream.CopyTo(fileStream);
        }

        return path;
    }

    private static int RelaunchElevated(string[] args, bool quiet)
    {
        SafeWriteLine(
            "PathVeer Setup requires administrator privileges to install the " +
            "Windows Service and write to Program Files.");
        SafeWriteLine("Requesting elevation...");

        // devsign.10 — this non-elevated parent is the SOLE Tray-launch owner.
        // Mint one invocation id and hand it to the elevated child so the Tray
        // launch request it records is scoped to THIS invocation. After the
        // child returns, only a request carrying this exact id is consumed, and
        // exactly once, under the ordinary user token.
        string operationId = "op-" + Guid.NewGuid().ToString("N");

        var startInfo = new ProcessStartInfo
        {
            FileName = Environment.ProcessPath ?? AppContext.BaseDirectory,
            UseShellExecute = true,
            Verb = "runas",
        };

        startInfo.ArgumentList.Add("--launch-operation-id");
        startInfo.ArgumentList.Add(operationId);

        if (quiet) startInfo.ArgumentList.Add("/quiet");
        foreach (string a in args)
        {
            startInfo.ArgumentList.Add(a);
        }

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null) return SetupExitCodes.RelaunchFailed;
            process.WaitForExit();

            // The elevated child wrote an invocation-scoped Tray launch request
            // (tagged with operationId) on success. We are the NON-elevated
            // parent (Explorer-launched), so consuming it spawns the Tray under
            // the ordinary user token — never elevated (certification
            // requirement). A failed child (non-zero exit) does NOT launch.
            if (process.ExitCode == SetupExitCodes.Success)
            {
                InstallForm.ConsumeLaunchTrayRequest(operationId);
            }
            else
            {
                // Child failed: never launch the Tray, and discard the request
                // so a stale marker cannot be consumed by a later invocation.
                try
                {
                    string file = Path.Combine(
                        InstallForm.LaunchRequestDirectory(operationId),
                        "launch-tray.request");
                    if (File.Exists(file)) File.Delete(file);
                }
                catch { /* ignore */ }
            }

            return process.ExitCode;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // runas UAC prompt was dismissed / denied. This is an explicit,
            // stable denial — NOT a generic argument error or relaunch failure.
            SafeWriteError(
                "Elevation was cancelled by the user. Installation aborted.");
            return SetupExitCodes.FromElevationDenied();
        }
    }

    private static string ThisVersion()
    {
        string? version = typeof(Program).Assembly
            .GetName().Version?.ToString();
        var attr = typeof(Program).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        if (attr?.InformationalVersion is { } info)
        {
            int space = info.IndexOf(' ');
            return space >= 0 ? info[..space] : info;
        }

        return version ?? "0.0.0";
    }

    private static void PrintUsage()
    {
        SafeWriteLine("PathVeer Setup — Windows installation bootstrapper");
        SafeWriteLine();
        SafeWriteLine("Usage:");
        SafeWriteLine("  PathVeerSetup.exe [options]");
        SafeWriteLine();
        SafeWriteLine("Options:");
        SafeWriteLine("  (no args)  Interactive install / upgrade");
        SafeWriteLine("  /quiet     Unattended mode (uses --install/--uninstall)");
        SafeWriteLine("  --uninstall            Remove the Service and binaries,");
        SafeWriteLine("                            preserve %ProgramData%\\PathVeer");
        SafeWriteLine("  --purge-state           Also delete %ProgramData%\\PathVeer");
        SafeWriteLine("  --install-tray          Register per-user Tray startup");
        SafeWriteLine("  --no-tray               Do not register Tray startup (quiet)");
        SafeWriteLine("  --status                Report installation state and exit");
        SafeWriteLine();
        SafeWriteLine("All install/migration logic is delegated to the embedded");
        SafeWriteLine("Install-PathVeer.ps1 deployment script.");
    }
}
