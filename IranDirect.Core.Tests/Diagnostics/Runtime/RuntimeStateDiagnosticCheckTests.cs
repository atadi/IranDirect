using IranDirect.Core.Diagnostics;
using IranDirect.Core.Diagnostics.Runtime;
using IranDirect.Core.Runtime.Execution;

namespace IranDirect.Core.Tests.Diagnostics.Runtime;

public sealed class RuntimeStateDiagnosticCheckTests
{
    private sealed class FakeStatusProvider :
        IIranDirectStatusProvider
    {
        private readonly Func<CancellationToken,
            Task<IranDirectStatus>> _handler;

        public FakeStatusProvider(
            Func<CancellationToken,
                Task<IranDirectStatus>> handler)
        {
            _handler = handler;
        }

        public Task<IranDirectStatus> GetStatusAsync(
            CancellationToken cancellationToken = default)
        {
            return _handler(cancellationToken);
        }
    }

    private sealed class ThrowingStatusProvider :
        IIranDirectStatusProvider
    {
        public Task<IranDirectStatus> GetStatusAsync(
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException(
                "Service not running.");
        }
    }

    private static IranDirectStatus ValidStatus() =>
        new()
        {
            Enabled = false,
            DesiredEnabled = false,
            Operation = new RuntimeOperationSnapshot
            {
                State = OperationState.Idle,
                StartedAt = DateTimeOffset.UtcNow
            }
        };

    [Fact]
    public async Task CheckAsync_ConsistentState_ReturnsPassed()
    {
        var check = new RuntimeStateDiagnosticCheck(
            new FakeStatusProvider(
                _ => Task.FromResult(ValidStatus())));

        DiagnosticResult result = await check.CheckAsync(
            CancellationToken.None);

        Assert.Equal(DiagnosticStatus.Passed, result.Status);
        Assert.Equal(DiagnosticSeverity.Info, result.Severity);
    }

    [Fact]
    public async Task CheckAsync_NullStatus_ReturnsFailed()
    {
        var check = new RuntimeStateDiagnosticCheck(
            new FakeStatusProvider(
                _ => Task.FromResult<IranDirectStatus>(null!)));

        DiagnosticResult result = await check.CheckAsync(
            CancellationToken.None);

        Assert.Equal(DiagnosticStatus.Failed, result.Status);
        Assert.Equal(DiagnosticSeverity.Error, result.Severity);
        Assert.Contains("null", result.Message);
    }

    [Fact]
    public async Task CheckAsync_NullDesiredEnabled_ReturnsFailed()
    {
        IranDirectStatus status =
            ValidStatus() with { DesiredEnabled = null };

        var check = new RuntimeStateDiagnosticCheck(
            new FakeStatusProvider(
                _ => Task.FromResult(status)));

        DiagnosticResult result = await check.CheckAsync(
            CancellationToken.None);

        Assert.Equal(DiagnosticStatus.Failed, result.Status);
        Assert.Contains("DesiredEnabled", result.Message);
    }

    [Fact]
    public async Task CheckAsync_DesiredEnabledButDisabled_ReturnsWarning()
    {
        IranDirectStatus status = ValidStatus() with
        {
            DesiredEnabled = true,
            Enabled = false,
            Operation = new RuntimeOperationSnapshot
            {
                State = OperationState.Idle,
                StartedAt = DateTimeOffset.UtcNow
            }
        };

        var check = new RuntimeStateDiagnosticCheck(
            new FakeStatusProvider(
                _ => Task.FromResult(status)));

        DiagnosticResult result = await check.CheckAsync(
            CancellationToken.None);

        Assert.Equal(DiagnosticStatus.Warning, result.Status);
        Assert.Equal(DiagnosticSeverity.Warning, result.Severity);
        Assert.Contains("Desired is enabled", result.Message);
    }

    [Fact]
    public async Task CheckAsync_DesiredDisabledButEnabled_ReturnsWarning()
    {
        IranDirectStatus status = ValidStatus() with
        {
            DesiredEnabled = false,
            Enabled = true,
            Operation = new RuntimeOperationSnapshot
            {
                State = OperationState.Idle,
                StartedAt = DateTimeOffset.UtcNow
            }
        };

        var check = new RuntimeStateDiagnosticCheck(
            new FakeStatusProvider(
                _ => Task.FromResult(status)));

        DiagnosticResult result = await check.CheckAsync(
            CancellationToken.None);

        Assert.Equal(DiagnosticStatus.Warning, result.Status);
        Assert.Contains("Desired is disabled", result.Message);
    }

    [Fact]
    public async Task CheckAsync_OperationInProgress_ReturnsPassed()
    {
        IranDirectStatus status = ValidStatus() with
        {
            Enabled = true,
            DesiredEnabled = true,
            Operation = new RuntimeOperationSnapshot
            {
                State = OperationState.Repairing,
                StartedAt = DateTimeOffset.UtcNow
            }
        };

        var check = new RuntimeStateDiagnosticCheck(
            new FakeStatusProvider(
                _ => Task.FromResult(status)));

        DiagnosticResult result = await check.CheckAsync(
            CancellationToken.None);

        Assert.Equal(DiagnosticStatus.Passed, result.Status);
        Assert.Contains("Operation in progress", result.Message);
    }

    [Fact]
    public async Task CheckAsync_ThrowsException_ReturnsFailed()
    {
        var check = new RuntimeStateDiagnosticCheck(
            new ThrowingStatusProvider());

        DiagnosticResult result = await check.CheckAsync(
            CancellationToken.None);

        Assert.Equal(DiagnosticStatus.Failed, result.Status);
        Assert.Equal(DiagnosticSeverity.Error, result.Severity);
        Assert.NotNull(result.SuggestedAction);
    }

    [Fact]
    public void CheckAsync_HasCorrectIdAndTitle()
    {
        var check = new RuntimeStateDiagnosticCheck(
            new FakeStatusProvider(
                _ => Task.FromResult(ValidStatus())));

        Assert.Equal("runtime-state", check.Id);
        Assert.Equal("Runtime state", check.Title);
    }
}
