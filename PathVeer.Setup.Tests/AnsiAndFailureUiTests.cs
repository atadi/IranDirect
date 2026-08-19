using System.Diagnostics;
using System.Reflection;
using PathVeer.Core.Installer;
using Xunit;

namespace PathVeer.Setup.Tests;

/// <summary>
/// devsign.10 — regression coverage for:
///   * Blockers C: the embedded Install-PathVeer.ps1 must render installer
///     output as PLAIN TEXT (no VT/ANSI escape sequences) when captured by the
///     GUI (redirected host). The GUI log must receive the real text without
///     raw ESC sequences like "[31;1m".
///   * Failure UI wording (section 9): the ContractViolation message must NOT
///     say "no changes are assumed"; it must tell the user changes may have
///     been applied and to re-run Setup.
///   * No-final-result path remains fail-closed (non-zero ContractViolation).
/// </summary>
public class AnsiAndFailureUiTests
{
    private static string ExtractEmbeddedScript()
    {
        var asm = typeof(InstallForm).Assembly;
        using var stream = asm.GetManifestResourceStream(
            "PathVeer.Setup.Resources.Install-PathVeer.ps1")
            ?? throw new InvalidOperationException("Embedded script resource missing.");

        string path = Path.Combine(
            Path.GetTempPath(),
            "PathVeer.Setup.Embedded." + Guid.NewGuid().ToString("N") + ".ps1");
        using (var fs = File.Create(path))
        {
            stream.CopyTo(fs);
        }
        return path;
    }

    private static string RunPowerShellCapture(string scriptPath)
    {
        // Source the embedded script in test-mode (returns before dispatching an
        // action, leaving all helper functions defined) and exercise the
        // color-style helpers, capturing stdout. The script sets
        // $PSStyle.OutputRendering='PlainText' and the helpers no longer emit
        // color, so the captured text must contain no VT escape sequences.
        string command = "$env:PATHVEER_INSTALL_TEST='1'; " +
                         ". '" + scriptPath.Replace("'", "''") + "'; " +
                         "Write-Step 'PathVeer install step'; " +
                         "Write-Detail 'detail line'; " +
                         "Write-Ok 'SUCCESS: PathVeer installed'";

        var psi = new ProcessStartInfo
        {
            FileName = "pwsh.exe",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            ArgumentList = { "-NoLogo", "-NoProfile", "-NonInteractive",
                "-ExecutionPolicy", "Bypass", "-Command", command },
        };

        using var p = Process.Start(psi)
            ?? throw new InvalidOperationException("pwsh failed to start.");
        string stdout = p.StandardOutput.ReadToEnd();
        string stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();
        return stdout + stderr;
    }

    [Fact]
    public void EmbeddedScript_EmitsNoAnsiEscape_OnColoredHostOutput()
    {
        // Only meaningful where pwsh exists (Windows host / CI).
        if (!FindPwsh())
        {
            // Skip gracefully if pwsh is unavailable in this environment.
            return;
        }

        string script = ExtractEmbeddedScript();
        try
        {
            // The script sets $PSStyle.OutputRendering='PlainText' and its
            // Write-Step/Write-Detail/Write-Ok helpers no longer pass
            // -ForegroundColor. We exercise them directly via the test seam
            // (PATHVEER_INSTALL_TEST makes the script return before dispatching
            // an action, leaving all helpers defined) and capture stdout.
            string output = RunPowerShellCapture(script);
            Assert.DoesNotContain("\u001b[", output, StringComparison.Ordinal);
            // The actual human-readable text must still be present.
            Assert.Contains("PathVeer", output, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { File.Delete(script); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void FailureUi_ContractViolation_NoLongerSaysNoChangesAssumed()
    {
        string text = SetupExitCodes.Describe(SetupExitCodes.ContractViolation);
        Assert.DoesNotContain("no changes are assumed", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("may have been applied", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Re-run Setup", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NoFinalResult_RemainsFailClosed_NonZero()
    {
        // The no-result / contract-violation path must be explicitly non-zero
        // and never reported as success.
        Assert.NotEqual(0, SetupExitCodes.ContractViolation);
        Assert.Equal(111, SetupExitCodes.ContractViolation);
    }

    private static bool FindPwsh()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "pwsh.exe",
                Arguments = "-NoLogo -NoProfile -Command \"exit 0\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi);
            p?.WaitForExit();
            return p is not null && p.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
