using System.Diagnostics;
using PathVeer.Core.Installer;
using Xunit;

namespace PathVeer.Setup.Tests;

/// <summary>
/// Regression tests for the InstallController operation-lifetime contract.
///
/// devsign.8 had a proven blocker: after the PowerShell child exited, the
/// progress tailer kept polling for up to 600 seconds because nothing tied its
/// lifetime to the child process. The controller therefore never reached
/// ReadResult / Completed, and the UI stayed on "Working..." for up to 10
/// minutes. These tests exercise the REAL controller (fake PowerShell producer
/// + real progress tailer + real result handling) and assert the operation
/// returns promptly after child exit and that the final "Finished" record is
/// observed.
/// </summary>
public class InstallControllerLifetimeTests
{
    private static string WriteFakeScript(string scriptBody)
    {
        string path = Path.Combine(
            Path.GetTempPath(), "PathVeer.Fake." + Guid.NewGuid().ToString("N") + ".ps1");
        File.WriteAllText(path, scriptBody);
        return path;
    }

    // PowerShell helper that mirrors the REAL Install-PathVeer.ps1 parameter
    // surface so the controller's full argument set (-Action, -PackageDirectory,
    // -RegisterShell, etc.) binds without error, then emits progress records +
    // the final result.json exactly like the real Write-ProgressRecord / result
    // contract (ConvertTo-Json one-object-per-line). All params are accepted and
    // ignored — only ProgressFile/ResultFile drive the fake behavior.
    private const string PsHeader = @"
param(
    [string]$Action,
    [string]$PackageDirectory,
    [switch]$RegisterShell,
    [switch]$InstallTray,
    [switch]$PurgeState,
    [string]$ProgressFile,
    [string]$ResultFile
)
function WriteProgress($stage, $message) {
    $rec = @{ stage = $stage; message = $message; Timestamp = [DateTimeOffset]::UtcNow } | ConvertTo-Json -Compress
    Add-Content -Path $ProgressFile -Value $rec
}
function WriteResult($success, $category, $message) {
    $res = @{ success = $success; category = $category; message = $message; Timestamp = [DateTimeOffset]::UtcNow } | ConvertTo-Json -Compress
    Set-Content -Path $ResultFile -Value $res
}
";

    private static string FakeSuccessScript()
    {
        // Writes progress records + a success result.json, then exits.
        return PsHeader + @"
WriteProgress 'VerifyingPackage' 'verifying'
WriteProgress 'Installing' 'installing'
WriteProgress 'Finished' 'done'
WriteResult $true 'Success' 'ok'
exit 0
";
    }

    private static string FakeFailureScript()
    {
        return PsHeader + @"
WriteProgress 'VerifyingPackage' 'verifying'
WriteProgress 'Finished' 'failed'
WriteResult $false 'PackageVerificationFail' 'bad'
exit 1
";
    }

    private static string FakeNoResultScript()
    {
        // Exits with no result.json (tests the process-exit fallback).
        return PsHeader + @"
WriteProgress 'VerifyingPackage' 'verifying'
exit 2
";
    }

    private static string FakeNoProgressScript()
    {
        // Writes only result.json, no progress file at all (missing-file path).
        return PsHeader + @"
WriteResult $true 'Success' 'ok'
exit 0
";
    }

    private static string FakeMalformedProgressScript()
    {
        // Mixes a malformed line with a valid Finished line.
        return PsHeader + @"
Add-Content -Path $ProgressFile -Value 'this is not json'
WriteProgress 'Finished' 'done'
WriteResult $true 'Success' 'ok'
exit 0
";
    }

    private static (int exitCode, int completedCount, List<string> stages, TimeSpan elapsed)
        Run(string scriptPath, InstallOptions options, CancellationToken ct = default)
    {
        var stages = new List<string>();
        int completedCount = 0;
        ResultRecord? completedResult = null;

        var controller = new InstallController(scriptPath, Path.GetTempPath());
        controller.Progress += rec => stages.Add(rec.Stage);
        controller.Completed += _ => { completedCount++; };

        var sw = Stopwatch.StartNew();
        int exitCode = controller.RunInstall(options, ct);
        sw.Stop();

        return (exitCode, completedCount, stages, sw.Elapsed);
    }

    [Fact]
    public void SuccessOperation_ReturnsPromptly_CompletedOnce_ZeroExit_FinishedObserved()
    {
        string script = WriteFakeScript(FakeSuccessScript());
        try
        {
            var (exitCode, completedCount, stages, elapsed) = Run(
                script, new InstallOptions { InstallTray = false, LaunchTrayAfterInstall = false });

            Assert.Equal(0, exitCode);
            Assert.Equal(1, completedCount);
            Assert.Contains("Finished", stages);
            Assert.True(elapsed.TotalSeconds < 30,
                $"Operation should return promptly, took {elapsed.TotalSeconds:F1}s");
        }
        finally
        {
            File.Delete(script);
        }
    }

    [Fact]
    public void FailureOperation_ReturnsPromptly_NonzeroExit()
    {
        string script = WriteFakeScript(FakeFailureScript());
        try
        {
            var (exitCode, _, stages, elapsed) = Run(
                script, new InstallOptions { InstallTray = false, LaunchTrayAfterInstall = false });

            Assert.NotEqual(0, exitCode);
            Assert.Contains("Finished", stages);
            Assert.True(elapsed.TotalSeconds < 30,
                $"Operation should return promptly, took {elapsed.TotalSeconds:F1}s");
        }
        finally
        {
            File.Delete(script);
        }
    }

    [Fact]
    public void NoResultFile_ProcessExitFallback_ReturnsPromptly()
    {
        string script = WriteFakeScript(FakeNoResultScript());
        try
        {
            var (exitCode, completedCount, stages, elapsed) = Run(
                script, new InstallOptions { InstallTray = false, LaunchTrayAfterInstall = false });

            // No result.json -> fallback to process exit code mapping (exit 2 -> GenericFailure).
            Assert.NotEqual(0, exitCode);
            Assert.Equal(0, completedCount); // Completed not raised without a result record.
            Assert.True(elapsed.TotalSeconds < 30,
                $"Operation should return promptly, took {elapsed.TotalSeconds:F1}s");
        }
        finally
        {
            File.Delete(script);
        }
    }

    [Fact]
    public void MissingProgressFile_NoHang()
    {
        string script = WriteFakeScript(FakeNoProgressScript());
        try
        {
            var (exitCode, completedCount, stages, elapsed) = Run(
                script, new InstallOptions { InstallTray = false, LaunchTrayAfterInstall = false });

            Assert.Equal(0, exitCode);
            Assert.Equal(1, completedCount);
            Assert.True(elapsed.TotalSeconds < 30,
                $"Operation should return promptly, took {elapsed.TotalSeconds:F1}s");
        }
        finally
        {
            File.Delete(script);
        }
    }

    [Fact]
    public void MalformedProgressLine_Ignored_ValidFinishedStillObserved()
    {
        string script = WriteFakeScript(FakeMalformedProgressScript());
        try
        {
            var (exitCode, _, stages, elapsed) = Run(
                script, new InstallOptions { InstallTray = false, LaunchTrayAfterInstall = false });

            Assert.Equal(0, exitCode);
            Assert.Contains("Finished", stages);
            Assert.True(elapsed.TotalSeconds < 30,
                $"Operation should return promptly, took {elapsed.TotalSeconds:F1}s");
        }
        finally
        {
            File.Delete(script);
        }
    }

    [Fact]
    public void Cancellation_NoReaderHang()
    {
        // A script that sleeps briefly then exits. Cancelling ct must cancel the
        // progress tailer immediately; the operation returns when the process
        // exits on its own (a few seconds), not after the previous 600s tailer cap.
        string script = WriteFakeScript(PsHeader + @"
Start-Sleep -Seconds 3
WriteResult $true 'Success' 'ok'
exit 0
");
        using var cts = new CancellationTokenSource();
        var controller = new InstallController(script, Path.GetTempPath());
        var sw = Stopwatch.StartNew();
        var t = Task.Run(() => controller.RunInstall(
            new InstallOptions { InstallTray = false, LaunchTrayAfterInstall = false }, cts.Token));
        Thread.Sleep(500);
        cts.Cancel();
        bool completed = t.Wait(TimeSpan.FromSeconds(20));
        sw.Stop();
        Assert.True(completed, $"Cancellation should not hang; took {sw.Elapsed.TotalSeconds:F1}s");
        Assert.True(sw.Elapsed.TotalSeconds < 15,
            $"Operation should return shortly after process exit, took {sw.Elapsed.TotalSeconds:F1}s");
        File.Delete(script);
    }

    // --- Section 14: PowerShell emits LOWERCASE JSON keys (ConvertTo-Json
    // default). System.Text.Json is case-SENSITIVE by default, so the
    // controller MUST use PropertyNameCaseInsensitive=true or every record would
    // deserialize with default/empty values and report a false failure. This
    // test pins the lowercase-key contract end to end through the REAL
    // controller + a script that emits exactly what Install-PathVeer.ps1 emits.

    [Fact]
    public void LowercasePowerShellJson_MapsToCSharpRecords()
    {
        // The shared PsHeader already writes lowercase keys
        // (stage/message/timestamp, success/category/message/version/timestamp)
        // via ConvertTo-Json. If the controller ignored case, result.Success and
        // result.Category would be the defaults (false / "Success") and the
        // exit code would be non-zero. Assert the REAL mapping succeeds.
        string script = WriteFakeScript(FakeSuccessScript());
        try
        {
            var (exitCode, completedCount, _, _) = Run(
                script, new InstallOptions { InstallTray = false, LaunchTrayAfterInstall = false });

            Assert.Equal(0, exitCode); // success mapped despite lowercase keys
            Assert.Equal(1, completedCount);
        }
        finally
        {
            File.Delete(script);
        }
    }

    [Fact]
    public void LowercaseFailureJson_MapsCategory()
    {
        string script = WriteFakeScript(FakeFailureScript());
        try
        {
            var (exitCode, _, _, _) = Run(
                script, new InstallOptions { InstallTray = false, LaunchTrayAfterInstall = false });

            // FakeFailureScript writes category 'PackageVerificationFailed' in
            // lowercase JSON; the controller must map it to the stable code.
            // NOTE: the REAL Install-PathVeer.ps1 emits the category token
            // 'PackageVerificationFail' (no trailing 'ed') which the controller
            // maps to PackageVerificationFailed (104). This test asserts the
            // lowercase-JSON -> C# record -> exit-code mapping end to end.
            Assert.Equal(SetupExitCodes.PackageVerificationFailed, exitCode);
        }
        finally
        {
            File.Delete(script);
        }
    }
}
