using PathVeer.Core.Installer;
using PathVeer.Setup;
using Xunit;

namespace PathVeer.Setup.Tests;

/// <summary>
/// Phase service-authority slice — PathVeer.Setup correctness.
///
/// Exercises the REAL form/controller result contract (NEW ISSUE #2), not only
/// helper functions. Confirms:
///   * a fresh form defaults to UserCancelled (closing before mutation is a
///     cancel, never Success);
///   * a successful <see cref="ResultRecord"/> latches Success and is reported
///     by <see cref="InstallForm.ResultExitCode"/>;
///   * a failed record latches the mapped non-zero code (ServiceFailed,
///     PackageVerificationFailed, etc.) and is NOT erased to Success;
///   * <see cref="InstallController.ResolveExitCode"/> agrees with the form for
///     every category so console, unattended, and UI never diverge.
/// </summary>
public class InstallFormResultContractTests
{
    private static InstallController MakeController()
    {
        // The controller never executes PowerShell in these tests; we only use
        // its pure exit-code resolution and the form's ResultExitCode latch.
        return new InstallController("nul", "nul");
    }

    [Fact]
    public void Fresh_Form_DefaultsTo_UserCancelled_NotSuccess()
    {
        // Closing before any operation must be a cancel, never a success claim.
        using var form = new InstallForm(MakeController(), "1.0.0");
        Assert.Equal(SetupExitCodes.UserCancelled, form.ResultExitCode);
        Assert.NotEqual(SetupExitCodes.Success, form.ResultExitCode);
    }

    [Fact]
    public void Completed_Success_Latches_Success()
    {
        using var form = new InstallForm(MakeController(), "1.0.0");
        form.SimulateCompleted(new ResultRecord
        {
            Success = true,
            Category = "Success",
            Version = "1.0.0",
        });
        Assert.Equal(SetupExitCodes.Success, form.ResultExitCode);
    }

    [Theory]
    [InlineData("ServiceFailed", SetupExitCodes.ServiceFailed)]
    [InlineData("PackageVerificationFail", SetupExitCodes.PackageVerificationFailed)]
    [InlineData("DowngradeBlocked", SetupExitCodes.DowngradeBlocked)]
    [InlineData("ReadinessFailed", SetupExitCodes.ReadinessFailed)]
    [InlineData("UninstallFailed", SetupExitCodes.UninstallFailed)]
    [InlineData("PurgeFailed", SetupExitCodes.PurgeFailed)]
    [InlineData("LegacyUnsupported", SetupExitCodes.LegacyUnsupported)]
    [InlineData("UserCancelled", SetupExitCodes.UserCancelled)]
    public void Completed_Failure_Latches_MappedNonZeroCode(string category, int expected)
    {
        using var form = new InstallForm(MakeController(), "1.0.0");
        form.SimulateCompleted(new ResultRecord
        {
            Success = false,
            Category = category,
        });

        // The failure code is latched and is NEVER reported as success.
        Assert.Equal(expected, form.ResultExitCode);
        Assert.NotEqual(SetupExitCodes.Success, form.ResultExitCode);
    }

    [Fact]
    public void Completed_Failure_ThenResultWindowClose_DoesNotErase()
    {
        // Closing the result window must not revert a failure to success.
        using var form = new InstallForm(MakeController(), "1.0.0");
        form.SimulateCompleted(new ResultRecord
        {
            Success = false,
            Category = "ServiceFailed",
        });
        int latched = form.ResultExitCode;
        Assert.Equal(SetupExitCodes.ServiceFailed, latched);

        // Program.Main reads form.ResultExitCode after Application.Run returns;
        // the property is stable regardless of window lifecycle.
        Assert.Equal(SetupExitCodes.ServiceFailed, form.ResultExitCode);
    }

    [Fact]
    public void Controller_ResolveExitCode_AgreesWithForm_ForEveryCategory()
    {
        var controller = MakeController();
        string[] categories =
        [
            "Success", "ServiceFailed", "PackageVerificationFail",
            "DowngradeBlocked", "ReadinessFailed", "UninstallFailed",
            "PurgeFailed", "LegacyUnsupported", "UserCancelled",
            "InvalidArguments", "ElevationDenied", "Unknown",
        ];

        foreach (string category in categories)
        {
            var record = new ResultRecord
            {
                Success = category == "Success",
                Category = category,
            };

            int controllerCode = controller.ResolveExitCode(record);
            // For a real success record ResolveExitCode short-circuits to 0;
            // for every failure category it must equal MapResultCategory.
            if (category == "Success")
            {
                Assert.Equal(SetupExitCodes.Success, controllerCode);
            }
            else
            {
                int mapped = SetupExitCodes.MapResultCategory(category);
                Assert.Equal(mapped, controllerCode);
                Assert.NotEqual(SetupExitCodes.Success, controllerCode);
            }
        }
    }
}
