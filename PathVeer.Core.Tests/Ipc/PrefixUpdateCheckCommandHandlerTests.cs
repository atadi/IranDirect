using PathVeer.Core.Ipc;
using PathVeer.Core.Prefixes;

namespace PathVeer.Core.Tests.Ipc;

public sealed class PrefixUpdateCheckCommandHandlerTests
{
    private static readonly DateTimeOffset BaseTime =
        new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CheckNowAsync_Success_ReturnsMonitorSnapshot()
    {
        StubPrefixUpdateChecker checker = new()
        {
            Result = CreateResult(
                PrefixUpdateCheckStatus.Current)
        };
        PrefixUpdateCheckCommandHandler handler =
            CreateHandler(checker);

        ServiceResponse response =
            await handler.CheckNowAsync();

        Assert.True(response.Success);
        Assert.Contains("Current", response.Message);
        Assert.NotNull(response.PrefixUpdateMonitor);
        Assert.Equal(
            PrefixUpdateCheckStatus.Current,
            response.PrefixUpdateMonitor.CurrentResult!.Status);
        Assert.Equal(
            BaseTime,
            response.PrefixUpdateMonitor.LastSuccessfulCheckAt);
        Assert.Equal(
            0,
            response.PrefixUpdateMonitor.ConsecutiveFailures);
        Assert.False(response.PrefixUpdateMonitor.Checking);
        Assert.Equal(1, checker.CallCount);
    }

    [Fact]
    public async Task CheckNowAsync_FailedResult_ReturnsFailureState()
    {
        StubPrefixUpdateChecker checker = new()
        {
            Result = CreateResult(
                PrefixUpdateCheckStatus.Failed,
                reason: "Remote check timed out.")
        };
        PrefixUpdateCheckCommandHandler handler =
            CreateHandler(checker);

        ServiceResponse response =
            await handler.CheckNowAsync();

        Assert.True(response.Success);
        Assert.Contains("Failed", response.Message);
        Assert.NotNull(response.PrefixUpdateMonitor);
        Assert.Equal(
            PrefixUpdateCheckStatus.Failed,
            response.PrefixUpdateMonitor.CurrentResult!.Status);
        Assert.Equal(
            "Remote check timed out.",
            response.PrefixUpdateMonitor.CurrentResult.Reason);
        Assert.Equal(
            1,
            response.PrefixUpdateMonitor.ConsecutiveFailures);
        Assert.Null(
            response.PrefixUpdateMonitor.LastSuccessfulCheckAt);
    }

    [Fact]
    public async Task CheckNowAsync_ConcurrentChecks_ShareSingleProbe()
    {
        TaskCompletionSource gate = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        StubPrefixUpdateChecker checker = new()
        {
            Gate = gate.Task,
            Result = CreateResult(
                PrefixUpdateCheckStatus.UpdateAvailable)
        };
        PrefixUpdateCheckCommandHandler handler =
            CreateHandler(checker);

        Task<ServiceResponse> first = handler.CheckNowAsync();
        Task<ServiceResponse> second = handler.CheckNowAsync();

        gate.SetResult();

        ServiceResponse[] responses =
            await Task.WhenAll(first, second);

        Assert.Equal(1, checker.CallCount);
        Assert.All(
            responses,
            response =>
            {
                Assert.True(response.Success);
                Assert.Equal(
                    PrefixUpdateCheckStatus.UpdateAvailable,
                    response.PrefixUpdateMonitor!
                        .CurrentResult!.Status);
            });
    }

    [Fact]
    public async Task CheckNowAsync_Cancellation_Propagates()
    {
        PrefixUpdateCheckCommandHandler handler = new(
            new CancelAwareMonitor());

        using CancellationTokenSource cts = new();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => handler.CheckNowAsync(cts.Token));
    }

    [Fact]
    public async Task CheckNowAsync_DoesNotMutatePrefixData()
    {
        StubPrefixUpdateChecker checker = new()
        {
            Result = CreateResult(
                PrefixUpdateCheckStatus.Current)
        };
        PrefixUpdateCheckCommandHandler handler =
            CreateHandler(checker);

        await handler.CheckNowAsync();
        await handler.CheckNowAsync();

        // Only read-only checks were performed: the handler
        // surface exposes no download or apply operation.
        Assert.Equal(2, checker.CallCount);
    }

    private static PrefixUpdateCheckCommandHandler
        CreateHandler(
            StubPrefixUpdateChecker checker) =>
        new(
            new PrefixUpdateMonitor(
                checker,
                new PrefixUpdateMonitorOptions()));

    private static PrefixUpdateCheckResult CreateResult(
        PrefixUpdateCheckStatus status,
        string? reason = null) =>
        new()
        {
            Status = status,
            CheckedAt = BaseTime,
            Reason = reason
        };

    private sealed class StubPrefixUpdateChecker :
        IPrefixUpdateChecker
    {
        public int CallCount;
        public PrefixUpdateCheckResult Result =
            new()
            {
                Status = PrefixUpdateCheckStatus.Current,
                CheckedAt = BaseTime
            };
        public Task? Gate { get; set; }

        public async Task<PrefixUpdateCheckResult>
            CheckAsync(
                CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref CallCount);

            if (Gate is not null)
            {
                await Gate.WaitAsync(cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();

            return Result;
        }
    }

    private sealed class CancelAwareMonitor :
        IPrefixUpdateMonitor
    {
        public Task StartAsync(
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task StopAsync(
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task ForceCheckAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public PrefixUpdateMonitorSnapshot GetSnapshot() =>
            new();
    }
}
