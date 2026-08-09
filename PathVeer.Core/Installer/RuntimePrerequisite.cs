using System.Diagnostics;
using System.Text.RegularExpressions;

namespace PathVeer.Core.Installer;

/// <summary>
/// Phase 37.2 (mandatory, section 43): the Service/CLI/Tray components are
/// framework-dependent .NET applications. A normal consumer install will fail
/// at Service start if the runtime is absent. This detector checks for the
/// required Microsoft.NETCore.App runtime so the UI can block early with an
/// actionable message instead of failing deep inside Service start.
///
/// The bootstrapper itself is self-contained (.NET bundled), so it can run
/// this check without any runtime present.
/// </summary>
public static class RuntimePrerequisite
{
    public const string RequiredRuntimeVersion = "10.0";

    public sealed record RuntimeCheck(bool Satisfied, string? DetectedVersion, string Message);

    public static RuntimeCheck Check()
    {
        string? detected = QueryInstalledRuntimes();
        if (detected is not null)
            return new RuntimeCheck(true, detected, $".NET runtime {detected} is present.");

        return new RuntimeCheck(
            false,
            null,
            "The required .NET " + RequiredRuntimeVersion +
            " runtime was not found. PathVeer cannot run until it is installed. " +
            "Download it from https://dotnet.microsoft.com/download (or run the " +
            "offline PathVeer installer if provided).");
    }

    private static string? QueryInstalledRuntimes()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "dotnet.exe",
                ArgumentList = { "--list-runtimes" },
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            using var proc = Process.Start(psi);
            if (proc is null) return null;

            string output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(10000);
            return ParseHighestMatching(output, RequiredRuntimeVersion);
        }
        catch
        {
            return null;
        }
    }

    public static string? ParseHighestMatching(string runtimesOutput, string requiredMajorMinor)
    {
        var regex = new Regex(@"Microsoft\.NETCore\.App\s+(\d+\.\d+\.\d+)", RegexOptions.Compiled);
        string? best = null;
        Version? bestVersion = null;

        foreach (Match m in regex.Matches(runtimesOutput))
        {
            string ver = m.Groups[1].Value;
            if (!ver.StartsWith(requiredMajorMinor, StringComparison.Ordinal)) continue;
            if (!Version.TryParse(ver, out var v)) continue;
            if (bestVersion is null || v > bestVersion)
            {
                bestVersion = v;
                best = ver;
            }
        }

        return best;
    }
}
