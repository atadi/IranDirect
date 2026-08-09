using System.Diagnostics;
using System.Reflection;
using System.Security.Principal;

namespace PathVeer.Setup;

/// <summary>
/// Phase 37.1 consumer-install bootstrap.
///
/// This executable is a thin, Authenticode-signable wrapper. It does NOT
/// contain install/migration/state logic. The authoritative deployment
/// contract lives in <c>Install-PathVeer.ps1</c> (embedded as a resource) and
/// in <c>PathVeer.Core.Installation</c>. This bootstrapper's entire job is:
///
///   1. ensure it is running elevated (UAC prompt if not);
///   2. locate the co-shipped package directory
///      (<c>PathVeerSetup-&lt;ver&gt;-win-x64\PathVeer-&lt;ver&gt;</c>);
///   3. hand control to the embedded PowerShell deployment script;
///   4. report the script's exit status.
///
/// Keeping PowerShell as the single source of install truth means the
/// bootstrapper and the dev/CI paths share identical upgrade/migration
/// behavior, satisfying the "one deployment contract" requirement.
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

        if (args.Any(a =>
                string.Equals(a, "--help", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(a, "-h", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(a, "/?", StringComparison.OrdinalIgnoreCase)))
        {
            PrintUsage();
            return 0;
        }

        if (!IsAdministrator())
        {
            return RelaunchElevated(args);
        }

        string? packageDirectory = ResolvePackageDirectory(args);
        if (packageDirectory is null)
        {
            Console.Error.WriteLine(
                "ERROR: could not locate the PathVeer package directory " +
                "(expected a sibling 'PathVeer-<version>' folder).");
            return 2;
        }

        string scriptPath = ExtractScript();
        if (scriptPath is null)
        {
            Console.Error.WriteLine(
                "ERROR: embedded deployment script was not found in this " +
                "executable. The installer is corrupt.");
            return 3;
        }

        Console.WriteLine($"PathVeer Setup {ThisVersion()}");
        Console.WriteLine($"Package: {packageDirectory}");
        Console.WriteLine();

        return InvokeDeployment(scriptPath, packageDirectory, args);
    }

    private static int InvokeDeployment(
        string scriptPath,
        string packageDirectory,
        string[] args)
    {
        List<string> psArgs =
        [
            "-NoLogo",
            "-NoProfile",
            "-ExecutionPolicy", "Bypass",
            "-File", scriptPath,
            "-PackageDirectory", packageDirectory,
        ];

        // Forward only known-safe verbs; never forward arbitrary input.
        foreach (string a in args)
        {
            if (PassthroughVerbs.Contains(a, StringComparer.OrdinalIgnoreCase))
            {
                psArgs.Add(a);
            }
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = "pwsh.exe",
            ArgumentList = { },
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = false,
        };

        foreach (string a in psArgs)
        {
            startInfo.ArgumentList.Add(a);
        }

        // If pwsh is unavailable, fall back to Windows PowerShell.
        if (!TryFindExecutable("pwsh.exe"))
        {
            startInfo.FileName = "powershell.exe";
        }

        using var process = new Process { StartInfo = startInfo };
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null) Console.WriteLine(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null) Console.Error.WriteLine(e.Data);
        };

        try
        {
            process.Start();
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            Console.Error.WriteLine(
                "ERROR: could not start PowerShell to run the installer: " +
                ex.Message);
            return 4;
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        process.WaitForExit();

        return process.ExitCode;
    }

    private static string? ResolvePackageDirectory(string[] args)
    {
        string baseDir = AppContext.BaseDirectory;

        // Try an explicit -PackageDirectory override first.
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

        // The release layout co-locates the package beside the setup exe:
        //   PathVeerSetup-1.0.0-win-x64/
        //     PathVeerSetup-1.0.0-win-x64.exe
        //     PathVeer-1.0.0/                <- package
        string? version = ThisVersion();
        string simple = Path.Combine(baseDir, $"PathVeer-{version}");
        if (Directory.Exists(simple)) return simple;

        // Fallback: first sibling directory matching PathVeer-*.
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

    private static string ExtractScript()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using Stream? stream = assembly.GetManifestResourceStream(
            ScriptResourceName);
        if (stream is null)
        {
            throw new InvalidOperationException(
                "Embedded deployment script not found in this executable.");
        }

        string tempDir = Path.Combine(Path.GetTempPath(), "PathVeer.Setup");
        Directory.CreateDirectory(tempDir);
        string path = Path.Combine(tempDir, "Install-PathVeer.ps1");

        using var fileStream = File.Create(path);
        stream.CopyTo(fileStream);

        return path;
    }

    private static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(
            WindowsBuiltInRole.Administrator);
    }

    private static int RelaunchElevated(string[] args)
    {
        Console.WriteLine(
            "PathVeer Setup requires administrator privileges to install the " +
            "Windows Service and write to Program Files.");
        Console.WriteLine("Requesting elevation...");

        var startInfo = new ProcessStartInfo
        {
            FileName = Environment.ProcessPath ??
                       AppContext.BaseDirectory,
            UseShellExecute = true,
            Verb = "runas",
        };

        foreach (string a in args)
        {
            startInfo.ArgumentList.Add(a);
        }

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null) return 5;
            process.WaitForExit();
            return process.ExitCode;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            Console.Error.WriteLine(
                "Elevation was cancelled by the user. Installation aborted.");
            return 6;
        }
    }

    private static string ThisVersion()
    {
        string? version = typeof(Program).Assembly
            .GetName().Version?.ToString();
        // AssemblyVersion is MAJOR.0.0.0; use InformationalVersion for the
        // real prerelease-aware version when available.
        var attr = typeof(Program).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        if (attr?.InformationalVersion is { } info)
        {
            int space = info.IndexOf(' ');
            return space >= 0 ? info[..space] : info;
        }

        return version ?? "0.0.0";
    }

    private static bool TryFindExecutable(string name)
    {
        string? pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (pathEnv is null) return false;
        foreach (string dir in pathEnv.Split(
                     Path.PathSeparator,
                     StringSplitOptions.RemoveEmptyEntries))
        {
            string full = Path.Combine(dir, name);
            if (File.Exists(full)) return true;
        }

        return false;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("PathVeer Setup — Windows installation bootstrapper");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  PathVeerSetup.exe [--uninstall] [--purge-state]");
        Console.WriteLine("                   [--install-tray] [--status]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  (no args)     Fresh install / upgrade from the");
        Console.WriteLine("                co-located PathVeer-<version> package");
        Console.WriteLine("  --uninstall   Remove the Service and binaries,");
        Console.WriteLine("                preserve %ProgramData%\\PathVeer");
        Console.WriteLine("  --purge-state Also delete %ProgramData%\\PathVeer");
        Console.WriteLine("                (explicit, destructive)");
        Console.WriteLine("  --install-tray Register per-user Tray startup");
        Console.WriteLine("  --status      Report installation state and exit");
        Console.WriteLine();
        Console.WriteLine("All install/migration logic is delegated to the");
        Console.WriteLine("embedded Install-PathVeer.ps1 deployment script.");
    }
}
