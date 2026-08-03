using IranDirect.Core.Diagnostics;
using IranDirect.Core.Testing.FaultInjection;

namespace IranDirect.Core.Tests.Diagnostics;

public sealed class DiagnosticRunnerFaultInjectionTests
{
    private sealed class CountingCheck : IDiagnosticCheck
    {
        public string Id => "counting-check";
        public string Title => "Counting Check";

        public int InvocationCount { get; private set; }

        public Task<DiagnosticResult> CheckAsync(
            CancellationToken cancellationToken)
        {
            InvocationCount++;
            return Task.FromResult(
                new DiagnosticResult(
                    Id: Id,
                    Title: Title,
                    Status: DiagnosticStatus.Passed,
                    Severity: DiagnosticSeverity.Info,
                    Message: "All good.",
                    SuggestedAction: null));
        }
    }

    private sealed class ThrowingCheck : IDiagnosticCheck
    {
        public string Id => "throwing-check";
        public string Title => "Throwing Check";

        public Task<DiagnosticResult> CheckAsync(
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException(
                "Unexpected failure.");
    }

    [Fact]
    public async Task RunAllAsync_DiagnosticsRunFault_ThrowsCorrectPoint()
    {
        CountingCheck check = new();
        DiagnosticRunner runner = CreateRunner(
            FaultInjectionPolicy.For(
                [FaultInjectionPoint.DiagnosticsRun]),
            check);

        FaultInjectionException exception =
            await Assert.ThrowsAsync<FaultInjectionException>(
                () => runner.RunAllAsync(CancellationToken.None));

        Assert.Equal(
            FaultInjectionPoint.DiagnosticsRun,
            exception.Point);
        Assert.Equal(0, check.InvocationCount);
    }

    [Fact]
    public async Task RunAllAsync_DiagnosticsRunFault_ZeroChecksRun()
    {
        CountingCheck check = new();
        DiagnosticRunner runner = CreateRunner(
            FaultInjectionPolicy.For(
                [FaultInjectionPoint.DiagnosticsRun]),
            check);

        await Assert.ThrowsAsync<FaultInjectionException>(
            () => runner.RunAllAsync(CancellationToken.None));

        Assert.Equal(0, check.InvocationCount);
    }

    [Fact]
    public async Task RunAllAsync_DiagnosticsRunFault_NoPartialReport()
    {
        CountingCheck check = new();
        DiagnosticRunner runner = CreateRunner(
            FaultInjectionPolicy.For(
                [FaultInjectionPoint.DiagnosticsRun]),
            check);

        await Assert.ThrowsAsync<FaultInjectionException>(
            () => runner.RunAllAsync(CancellationToken.None));

        Assert.Equal(0, check.InvocationCount);
    }

    [Fact]
    public async Task RunAllAsync_NextRunSucceeds()
    {
        CountingCheck check = new();
        DiagnosticRunner runner = CreateRunner(checks: check);

        FaultInjectionException exception;
        using (FaultInjectionScope scope =
            FaultInjectionScope.Fail(
                FaultInjectionPoint.DiagnosticsRun))
        {
            exception = await Assert.ThrowsAsync<FaultInjectionException>(
                () => runner.RunAllAsync(CancellationToken.None));
        }

        Assert.Equal(
            FaultInjectionPoint.DiagnosticsRun,
            exception.Point);
        Assert.Equal(0, check.InvocationCount);

        DiagnosticReport report =
            await runner.RunAllAsync(CancellationToken.None);

        Assert.True(report.Healthy);
        Assert.Single(report.Results);
        Assert.Equal(1, check.InvocationCount);
    }

    [Fact]
    public async Task RunAllAsync_UnrelatedFaultPoint_HasNoEffect()
    {
        CountingCheck check = new();
        DiagnosticRunner runner = CreateRunner(
            FaultInjectionPolicy.For(
                [FaultInjectionPoint.HttpRequest]),
            check);

        DiagnosticReport report =
            await runner.RunAllAsync(CancellationToken.None);

        Assert.True(report.Healthy);
        Assert.Equal(1, check.InvocationCount);
    }

    [Fact]
    public async Task RunAllAsync_InjectedPolicy_WorksWithoutAmbientScope()
    {
        CountingCheck check = new();
        DiagnosticRunner runner = CreateRunner(
            FaultInjectionPolicy.For(
                [FaultInjectionPoint.DiagnosticsRun]),
            check);

        FaultInjectionException exception =
            await Assert.ThrowsAsync<FaultInjectionException>(
                () => runner.RunAllAsync(CancellationToken.None));

        Assert.Equal(
            FaultInjectionPoint.DiagnosticsRun,
            exception.Point);
        Assert.Equal(0, check.InvocationCount);
    }

    [Fact]
    public async Task RunAllAsync_AmbientScope_OverridesInjectedPolicy_TriggersFault()
    {
        CountingCheck check = new();
        DiagnosticRunner runner = CreateRunner(checks: check);

        using (FaultInjectionScope scope =
            FaultInjectionScope.Fail(
                FaultInjectionPoint.DiagnosticsRun))
        {
            await Assert.ThrowsAsync<FaultInjectionException>(
                () => runner.RunAllAsync(CancellationToken.None));
        }

        Assert.Equal(0, check.InvocationCount);
    }

    [Fact]
    public async Task RunAllAsync_AmbientScope_OverridesInjectedPolicy_SuppressesFault()
    {
        CountingCheck check = new();
        DiagnosticRunner runner = CreateRunner(
            FaultInjectionPolicy.For(
                [FaultInjectionPoint.DiagnosticsRun]),
            check);

        using (FaultInjectionScope scope =
            FaultInjectionScope.Fail(
                FaultInjectionPoint.HttpRequest))
        {
            DiagnosticReport report =
                await runner.RunAllAsync(CancellationToken.None);

            Assert.True(report.Healthy);
        }

        Assert.Equal(1, check.InvocationCount);
    }

    [Fact]
    public async Task RunAllAsync_NestedScopes_InnerScopeTriggersCorrectPoint()
    {
        CountingCheck check = new();
        DiagnosticRunner runner = CreateRunner(checks: check);

        using (FaultInjectionScope outer =
            FaultInjectionScope.Fail(
                FaultInjectionPoint.HttpRequest))
        {
            using (FaultInjectionScope inner =
                FaultInjectionScope.Fail(
                    FaultInjectionPoint.DiagnosticsRun))
            {
                FaultInjectionException exception =
                    await Assert.ThrowsAsync<FaultInjectionException>(
                        () => runner.RunAllAsync(CancellationToken.None));

                Assert.Equal(
                    FaultInjectionPoint.DiagnosticsRun,
                    exception.Point);
            }
        }

        Assert.Equal(0, check.InvocationCount);
    }

    [Fact]
    public async Task RunAllAsync_NestedScopes_UnselectedInnerScope_NoFault()
    {
        CountingCheck check = new();
        DiagnosticRunner runner = CreateRunner(checks: check);

        using (FaultInjectionScope outer =
            FaultInjectionScope.Fail(
                FaultInjectionPoint.DiagnosticsRun))
        {
            using (FaultInjectionScope inner =
                FaultInjectionScope.Fail(
                    FaultInjectionPoint.HttpRequest))
            {
                DiagnosticReport report =
                    await runner.RunAllAsync(CancellationToken.None);

                Assert.True(report.Healthy);
            }
        }

        Assert.Equal(1, check.InvocationCount);
    }

    [Fact]
    public async Task RunAllAsync_ScopeDisposal_PreventsLeakage()
    {
        CountingCheck check = new();
        DiagnosticRunner runner = CreateRunner(checks: check);

        FaultInjectionScope scope =
            FaultInjectionScope.Fail(
                FaultInjectionPoint.DiagnosticsRun);
        scope.Dispose();

        DiagnosticReport report =
            await runner.RunAllAsync(CancellationToken.None);

        Assert.True(report.Healthy);
        Assert.Equal(1, check.InvocationCount);
    }

    [Fact]
    public async Task RunAllAsync_Cancellation_RemainsUnchanged()
    {
        using CancellationTokenSource cts = new();
        cts.Cancel();

        CountingCheck check = new();
        DiagnosticRunner runner = CreateRunner(checks: check);

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => runner.RunAllAsync(cts.Token));

        Assert.Equal(0, check.InvocationCount);
    }

    [Fact]
    public async Task RunAllAsync_OrdinaryCheckException_BecomesFailedResult()
    {
        DiagnosticRunner runner = CreateRunner(
            checks: new ThrowingCheck());

        DiagnosticReport report =
            await runner.RunAllAsync(CancellationToken.None);

        Assert.False(report.Healthy);
        DiagnosticResult result = Assert.Single(report.Results);
        Assert.Equal(DiagnosticStatus.Failed, result.Status);
        Assert.Equal(DiagnosticSeverity.Error, result.Severity);
        Assert.Equal("Unexpected failure.", result.Message);
    }

    [Fact]
    public async Task RunAllAsync_RegistrationOrder_UnchangedWithoutFault()
    {
        CountingCheck first = new();
        CountingCheck second = new();
        DiagnosticRunner runner = CreateRunner(first, second);

        DiagnosticReport report =
            await runner.RunAllAsync(CancellationToken.None);

        Assert.Equal(2, report.Results.Count);
        Assert.Equal("counting-check", report.Results[0].Id);
        Assert.Equal("counting-check", report.Results[1].Id);
        Assert.Equal(1, first.InvocationCount);
        Assert.Equal(1, second.InvocationCount);
    }

    [Fact]
    public async Task RunAllAsync_EmptyChecks_DiagnosticsRunFault_Throws()
    {
        DiagnosticRunner runner = CreateRunner(
            FaultInjectionPolicy.For(
                [FaultInjectionPoint.DiagnosticsRun]));

        FaultInjectionException exception =
            await Assert.ThrowsAsync<FaultInjectionException>(
                () => runner.RunAllAsync(CancellationToken.None));

        Assert.Equal(
            FaultInjectionPoint.DiagnosticsRun,
            exception.Point);
    }

    [Fact]
    public async Task RunAllAsync_EmptyChecks_NoFault_ReturnsValidEmptyReport()
    {
        DiagnosticRunner runner = CreateRunner();

        DiagnosticReport report =
            await runner.RunAllAsync(CancellationToken.None);

        Assert.True(report.Healthy);
        Assert.Empty(report.Results);
        Assert.Equal(
            DiagnosticSeverity.Pass,
            report.HighestSeverity);
    }

    [Fact]
    public async Task RunAllAsync_InjectedPolicy_EvaluatedOncePerRun()
    {
        CountingPolicy policy = new();
        CountingCheck check = new();
        DiagnosticRunner runner = new([check], policy);

        DiagnosticReport first =
            await runner.RunAllAsync(CancellationToken.None);

        Assert.True(first.Healthy);
        Assert.Equal(1, policy.EvaluationCount);

        DiagnosticReport second =
            await runner.RunAllAsync(CancellationToken.None);

        Assert.True(second.Healthy);
        Assert.Equal(2, policy.EvaluationCount);
        Assert.Equal(2, check.InvocationCount);
    }

    [Fact]
    public async Task RunAllAsync_PreCancelledTokenWithActiveFault_ThrowsCancellationFirst()
    {
        CountingCheck check = new();
        DiagnosticRunner runner = CreateRunner(check);

        using CancellationTokenSource cts = new();
        cts.Cancel();

        using (FaultInjectionScope scope =
            FaultInjectionScope.Fail(
                FaultInjectionPoint.DiagnosticsRun))
        {
            await Assert.ThrowsAsync<OperationCanceledException>(
                () => runner.RunAllAsync(cts.Token));
        }

        Assert.Equal(0, check.InvocationCount);
    }

    [Fact]
    public async Task RunAllAsync_ScopeInChildTask_DoesNotLeakToParentContext()
    {
        CountingCheck check = new();
        DiagnosticRunner runner = CreateRunner(checks: check);

        await Task.Run(() =>
        {
            using (FaultInjectionScope scope =
                FaultInjectionScope.Fail(
                    FaultInjectionPoint.DiagnosticsRun))
            {
                Assert.True(FaultInjectionScope.IsActive);
            }
        });

        Assert.False(FaultInjectionScope.IsActive);

        DiagnosticReport report =
            await runner.RunAllAsync(CancellationToken.None);

        Assert.True(report.Healthy);
        Assert.Equal(1, check.InvocationCount);
    }

    private sealed class CountingPolicy : IFaultInjectionPolicy
    {
        public int EvaluationCount { get; private set; }

        public bool ShouldFail(FaultInjectionPoint point)
        {
            EvaluationCount++;
            return false;
        }
    }

    private static DiagnosticRunner CreateRunner(
        params IDiagnosticCheck[] checks) =>
        new(checks);

    private static DiagnosticRunner CreateRunner(
        IFaultInjectionPolicy policy,
        params IDiagnosticCheck[] checks) =>
        new(checks, policy);
}
