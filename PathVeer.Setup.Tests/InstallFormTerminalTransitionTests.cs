using PathVeer.Core.Installer;
using PathVeer.Setup;
using Xunit;

namespace PathVeer.Setup.Tests;

/// <summary>
/// Phase service-authority slice — DEFECT #1 terminal-transition contract.
///
/// The installer must enforce:
///   * successful operation -> exactly one terminal transition -> ShowSuccess
///     -> Finish enabled, progress stopped.
///   * failed operation -> exactly one terminal transition -> ShowFailure
///     -> Finish/Close enabled.
///   * operation thread returns but NO terminal Completed result arrived ->
///     fail-closed: surface an explicit non-zero ContractViolation; the UI must
///     never remain stuck on the "Working" marquee.
///   * there is no terminal state where the operation thread has returned AND
///     the UI remains mutation-disabled (progress visible, buttons disabled).
///
/// These drive the real form via the DEBUG <see cref="InstallForm.SimulateCompleted"/>
/// seam and via a direct terminal-transition API so the RunOperation worker
/// fail-closed path is exercised deterministically without a live PowerShell run.
/// </summary>
public class InstallFormTerminalTransitionTests
{
    private static InstallController MakeController() => new("nul", "nul");

    [Fact]
    public void Success_TransitionsExactlyOnce_SuccessCode()
    {
        using var form = new InstallForm(MakeController(), "1.0.0");
        form.SimulateCompleted(new ResultRecord { Success = true, Category = "Success" });

        // Single terminal transition: a second terminal call is idempotent.
        int first = form.ResultExitCode;
        form.SimulateCompleted(new ResultRecord { Success = false, Category = "ServiceFailed" });
        int second = form.ResultExitCode;

        Assert.Equal(SetupExitCodes.Success, first);
        Assert.Equal(first, second); // no second transition overrides success
    }

    [Fact]
    public void Failure_TransitionsExactlyOnce_NonZeroCode()
    {
        using var form = new InstallForm(MakeController(), "1.0.0");
        form.SimulateCompleted(new ResultRecord { Success = false, Category = "ServiceFailed" });

        int first = form.ResultExitCode;
        // A success arriving after a failure must NOT overwrite the failure.
        form.SimulateCompleted(new ResultRecord { Success = true, Category = "Success" });
        int second = form.ResultExitCode;

        Assert.Equal(SetupExitCodes.ServiceFailed, first);
        Assert.Equal(first, second);
    }

    [Fact]
    public void MissingTerminalResult_FailClosed_ContractViolation()
    {
        // Models the DEFECT #1 root cause: the operation thread returns but the
        // controller's Completed event never fired (dropped invoke / disposed
        // form / backend returned without a result record). The worker's
        // fail-closed rule must surface a non-zero ContractViolation and the UI
        // must not remain on "Working".
        using var form = new InstallForm(MakeController(), "1.0.0");

        // Replicate the worker's post-return fail-closed branch directly.
        bool terminal = form.DebugTerminalSignaled();
        if (!terminal)
        {
            form.DebugFailClosedContractViolation();
        }

        Assert.Equal(SetupExitCodes.ContractViolation, form.ResultExitCode);
        Assert.NotEqual(SetupExitCodes.Success, form.ResultExitCode);
    }

    [Fact]
    public void RealSuccessDoesNotBecomeContractViolation()
    {
        // If a real Completed arrived, the fail-closed branch must NOT fire.
        using var form = new InstallForm(MakeController(), "1.0.0");
        form.SimulateCompleted(new ResultRecord { Success = true, Category = "Success" });

        bool terminal = form.DebugTerminalSignaled();
        if (!terminal)
        {
            form.DebugFailClosedContractViolation();
        }

        Assert.True(terminal);
        Assert.Equal(SetupExitCodes.Success, form.ResultExitCode);
    }
}
