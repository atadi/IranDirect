using IranDirect.Cli;
using IranDirect.Core.Cli;
using IranDirect.Core.Diagnostics;
using IranDirect.Core.Ipc;
using IranDirect.Core.Testing.FaultInjection;
using IranDirect.Core.Tests.Observability;

namespace IranDirect.Core.Tests.Ipc;

public sealed class ObservabilityConsumerTests
{
    [Fact]
    public async Task RuntimeSnapshotHandler_SnapshotCaptureFault_PropagatesDirectly()
    {
        await using RuntimeSnapshotProviderTests.Fixture fixture =
            RuntimeSnapshotProviderTests.Fixture.Create(
                faultPolicy: FaultInjectionPolicy.For(
                    [FaultInjectionPoint.SnapshotCapture]));
        RuntimeSnapshotCommandHandler handler =
            new(fixture.Provider);

        FaultInjectionException exception =
            await Assert.ThrowsAsync<FaultInjectionException>(
                () => handler.GetAsync());

        Assert.Equal(
            FaultInjectionPoint.SnapshotCapture,
            exception.Point);
        Assert.Equal(0, fixture.UpdateChecker.InvocationCount);
    }

    [Fact]
    public async Task RuntimeSnapshotHandler_SubsequentUnfaultedRequest_Succeeds()
    {
        await using RuntimeSnapshotProviderTests.Fixture fixture =
            RuntimeSnapshotProviderTests.Fixture.Create();
        RuntimeSnapshotCommandHandler handler =
            new(fixture.Provider);

        FaultInjectionException exception;
        using (FaultInjectionScope scope =
            FaultInjectionScope.Fail(
                FaultInjectionPoint.SnapshotCapture))
        {
            exception = await Assert.ThrowsAsync<FaultInjectionException>(
                () => handler.GetAsync());
        }

        Assert.Equal(
            FaultInjectionPoint.SnapshotCapture,
            exception.Point);

        ServiceResponse response = await handler.GetAsync();

        Assert.True(response.Success);
        Assert.Equal(
            "Runtime snapshot captured.",
            response.Message);
        Assert.NotNull(response.Snapshot);
    }

    [Fact]
    public async Task DiagnosticHandler_DiagnosticsRunFault_PropagatesDirectly()
    {
        DiagnosticRunner runner = new(
            [new PassingCheck()],
            FaultInjectionPolicy.For(
                [FaultInjectionPoint.DiagnosticsRun]));
        DiagnosticCommandHandler handler = new(runner);

        FaultInjectionException exception =
            await Assert.ThrowsAsync<FaultInjectionException>(
                () => handler.RunAsync());

        Assert.Equal(
            FaultInjectionPoint.DiagnosticsRun,
            exception.Point);
    }

    [Fact]
    public async Task DiagnosticHandler_SubsequentUnfaultedRequest_Succeeds()
    {
        DiagnosticRunner runner = new(
            [new PassingCheck()]);
        DiagnosticCommandHandler handler = new(runner);

        FaultInjectionException exception;
        using (FaultInjectionScope scope =
            FaultInjectionScope.Fail(
                FaultInjectionPoint.DiagnosticsRun))
        {
            exception = await Assert.ThrowsAsync<FaultInjectionException>(
                () => handler.RunAsync());
        }

        Assert.Equal(
            FaultInjectionPoint.DiagnosticsRun,
            exception.Point);

        ServiceResponse response = await handler.RunAsync();

        Assert.True(response.Success);
        Assert.Contains(
            "Diagnostics completed:",
            response.Message);
        Assert.NotNull(response.Report);
    }

    [Fact]
    public async Task DoctorCli_FaultInjectedDiagnostics_ReceivesExistingFailureOutcome()
    {
        using StringWriter stdout = new();
        using StringWriter stderr = new();

        int exitCode = await DiagnosticCliRunner.RunAsync(
            ["--compact"],
            new ThrowingSender(),
            stdout,
            stderr);

        Assert.Equal(2, exitCode);
        Assert.Empty(stdout.ToString());
        Assert.Contains(
            "IranDirect command failed: " +
            "Fault injected at DiagnosticsRun.",
            stderr.ToString());
    }

    private sealed class PassingCheck : IDiagnosticCheck
    {
        public string Id => "passing-check";
        public string Title => "Passing Check";

        public Task<DiagnosticResult> CheckAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(
                new DiagnosticResult(
                    Id: Id,
                    Title: Title,
                    Status: DiagnosticStatus.Passed,
                    Severity: DiagnosticSeverity.Info,
                    Message: "All good.",
                    SuggestedAction: null));
    }

    private sealed class ThrowingSender :
        ICustomRouteCommandSender
    {
        public async Task<ServiceResponse> SendAsync(
            IranDirectCommand command,
            string? value = null,
            string? description = null,
            CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            throw new FaultInjectionException(
                FaultInjectionPoint.DiagnosticsRun);
        }
    }
}
