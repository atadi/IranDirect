using PathVeer.Core.Ipc;
using PathVeer.Core.Observability;

namespace PathVeer.Core.Tests.Ipc;

public sealed class RuntimeSnapshotCommandHandlerTests
{
    [Fact]
    public async Task GetAsync_ReturnsSuccessWithProviderSnapshot()
    {
        RuntimeSnapshot expected = CreateSnapshot();
        FakeSnapshotProvider provider = new(expected);
        RuntimeSnapshotCommandHandler handler = new(provider);

        ServiceResponse response =
            await handler.GetAsync();

        Assert.True(response.Success);
        Assert.Equal(expected, response.Snapshot);
        Assert.False(
            string.IsNullOrWhiteSpace(response.Message));
    }

    [Fact]
    public async Task GetAsync_QueriesProviderOnly()
    {
        FakeSnapshotProvider provider = new(CreateSnapshot());
        RuntimeSnapshotCommandHandler handler = new(provider);

        await handler.GetAsync();

        Assert.Equal(1, provider.CallCount);
    }

    [Fact]
    public async Task GetAsync_WithCancellation_PassesToken()
    {
        using CancellationTokenSource cts = new();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new RuntimeSnapshotCommandHandler(
                    new ThrowingSnapshotProvider())
                .GetAsync(cts.Token));
    }

    private static RuntimeSnapshot CreateSnapshot()
    {
        return new RuntimeSnapshot
        {
            CapturedAt = new DateTimeOffset(
                2026, 1, 1, 0, 0, 0, TimeSpan.Zero)
        };
    }

    private sealed class FakeSnapshotProvider :
        IRuntimeSnapshotProvider
    {
        private readonly RuntimeSnapshot _snapshot;

        public FakeSnapshotProvider(RuntimeSnapshot snapshot)
        {
            _snapshot = snapshot;
        }

        public int CallCount { get; private set; }

        public Task<RuntimeSnapshot> GetSnapshotAsync(
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(_snapshot);
        }
    }

    private sealed class ThrowingSnapshotProvider :
        IRuntimeSnapshotProvider
    {
        public Task<RuntimeSnapshot> GetSnapshotAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new NotSupportedException();
        }
    }
}
