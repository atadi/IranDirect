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

    public static int Main(string[] args)
    {
        Console.Title = "PathVeer Setup";

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
            Console.Error.WriteLine(
                "ERROR: another instance of PathVeer Setup is already running.");
            return SetupExitCodes.InvalidArguments;
        }

        string? packageDirectory = ResolvePackageDirectory(args);
        if (packageDirectory is null)
        {
            Console.Error.WriteLine(
                "ERROR: could not locate the PathVeer package directory " +
                "(expected a sibling 'PathVeer-<version>' folder).");
            return SetupExitCodes.PackageDirectoryNotFound;
        }

        string? scriptPath = ExtractScript();
        if (scriptPath is null)
        {
            Console.Error.WriteLine(
                "ERROR: embedded deployment script was not found in this " +
                "executable. The installer is corrupt.");
            return SetupExitCodes.ScriptResourceMissing;
        }

        Console.WriteLine($"PathVeer Setup {ThisVersion()}");
        Console.WriteLine($"Package: {packageDirectory}");
        Console.WriteLine();

        // Phase 37.2 mandatory: the hosted components are framework-dependent.
        var runtime = RuntimePrerequisite.Check();
        if (!runtime.Satisfied)
        {
            Console.Error.WriteLine("ERROR: " + runtime.Message);
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
        using var form = new InstallForm(controller, ThisVersion());
        Application.Run(form);

        // The form carries the authoritative operation result. The explicit
        // contract prevents a closed failure window from being silently reported
        // as success (NEW ISSUE #2). Closing before mutation == user cancel.
        return form.ResultExitCode;
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

        Console.WriteLine(
            exit == SetupExitCodes.Success ? "SUCCESS" : "FAILED:" + exit);
        return exit;
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
        Console.WriteLine(
            "PathVeer Setup requires administrator privileges to install the " +
            "Windows Service and write to Program Files.");
        Console.WriteLine("Requesting elevation...");

        var startInfo = new ProcessStartInfo
        {
            FileName = Environment.ProcessPath ?? AppContext.BaseDirectory,
            UseShellExecute = true,
            Verb = "runas",
        };

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
            return process.ExitCode;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // runas UAC prompt was dismissed / denied. This is an explicit,
            // stable denial — NOT a generic argument error or relaunch failure.
            Console.Error.WriteLine(
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
        Console.WriteLine("PathVeer Setup — Windows installation bootstrapper");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  PathVeerSetup.exe [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  (no args)  Interactive install / upgrade");
        Console.WriteLine("  /quiet     Unattended mode (uses --install/--uninstall)");
        Console.WriteLine("  --uninstall            Remove the Service and binaries,");
        Console.WriteLine("                            preserve %ProgramData%\\PathVeer");
        Console.WriteLine("  --purge-state           Also delete %ProgramData%\\PathVeer");
        Console.WriteLine("  --install-tray          Register per-user Tray startup");
        Console.WriteLine("  --no-tray               Do not register Tray startup (quiet)");
        Console.WriteLine("  --status                Report installation state and exit");
        Console.WriteLine();
        Console.WriteLine("All install/migration logic is delegated to the embedded");
        Console.WriteLine("Install-PathVeer.ps1 deployment script.");
    }
}
