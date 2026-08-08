using PathVeer.Core.Observability;
using PathVeer.Core.Routing;
using PathVeer.Core.Testing.FaultInjection;

namespace PathVeer.Core.Tests.Observability;

using Fixture =
    PathVeer.Core.Tests.Observability.
        RuntimeSnapshotProviderTests.Fixture;

public sealed class RuntimeSnapshotProviderFaultInjectionTests
{
    [Fact]
    public async Task GetSnapshotAsync_SnapshotCaptureFault_ThrowsCorrectPoint()
    {
        await using Fixture fixture = Fixture.Create(
            faultPolicy: FaultInjectionPolicy.For(
                [FaultInjectionPoint.SnapshotCapture]));

        FaultInjectionException exception =
            await Assert.ThrowsAsync<FaultInjectionException>(
                () => fixture.Provider.GetSnapshotAsync());

        Assert.Equal(
            FaultInjectionPoint.SnapshotCapture,
            exception.Point);
    }

    [Fact]
    public async Task GetSnapshotAsync_SnapshotCaptureFault_ZeroDependencyCalls()
    {
        CountingRouteInventory inventory = new();
        await using Fixture fixture = Fixture.Create(
            faultPolicy: FaultInjectionPolicy.For(
                [FaultInjectionPoint.SnapshotCapture]),
            routeInventory: inventory);

        await Assert.ThrowsAsync<FaultInjectionException>(
            () => fixture.Provider.GetSnapshotAsync());

        Assert.Equal(0, inventory.LoadCallCount);
        Assert.Equal(0, fixture.UpdateChecker.InvocationCount);
    }

    [Fact]
    public async Task GetSnapshotAsync_SnapshotCaptureFault_DoesNotRetry()
    {
        CountingRouteInventory inventory = new();
        await using Fixture fixture = Fixture.Create(
            faultPolicy: FaultInjectionPolicy.For(
                [FaultInjectionPoint.SnapshotCapture]),
            routeInventory: inventory);

        FaultInjectionException exception =
            await Assert.ThrowsAsync<FaultInjectionException>(
                () => fixture.Provider.GetSnapshotAsync());

        Assert.Equal(
            FaultInjectionPoint.SnapshotCapture,
            exception.Point);
        Assert.Equal(0, inventory.LoadCallCount);
        Assert.Equal(0, fixture.UpdateChecker.InvocationCount);
    }

    [Fact]
    public async Task GetSnapshotAsync_NextCaptureSucceeds()
    {
        await using Fixture fixture = Fixture.Create();

        FaultInjectionException exception;
        using (FaultInjectionScope scope =
            FaultInjectionScope.Fail(
                FaultInjectionPoint.SnapshotCapture))
        {
            exception = await Assert.ThrowsAsync<FaultInjectionException>(
                () => fixture.Provider.GetSnapshotAsync());
        }

        Assert.Equal(
            FaultInjectionPoint.SnapshotCapture,
            exception.Point);
        Assert.Equal(0, fixture.UpdateChecker.InvocationCount);

        RuntimeSnapshot snapshot =
            await fixture.Provider.GetSnapshotAsync();

        Assert.Equal(1, snapshot.SchemaVersion);
        Assert.Equal(
            1,
            fixture.UpdateChecker.InvocationCount);
    }

    [Fact]
    public async Task GetSnapshotAsync_UnrelatedFaultPoint_HasNoEffect()
    {
        await using Fixture fixture = Fixture.Create(
            faultPolicy: FaultInjectionPolicy.For(
                [FaultInjectionPoint.HttpRequest]));

        RuntimeSnapshot snapshot =
            await fixture.Provider.GetSnapshotAsync();

        Assert.Equal(1, snapshot.SchemaVersion);
        Assert.Equal(
            1,
            fixture.UpdateChecker.InvocationCount);
    }

    [Fact]
    public async Task GetSnapshotAsync_InjectedPolicy_WorksWithoutAmbientScope()
    {
        await using Fixture fixture = Fixture.Create(
            faultPolicy: FaultInjectionPolicy.For(
                [FaultInjectionPoint.SnapshotCapture]));

        FaultInjectionException exception =
            await Assert.ThrowsAsync<FaultInjectionException>(
                () => fixture.Provider.GetSnapshotAsync());

        Assert.Equal(
            FaultInjectionPoint.SnapshotCapture,
            exception.Point);
        Assert.Equal(0, fixture.UpdateChecker.InvocationCount);
    }

    [Fact]
    public async Task GetSnapshotAsync_AmbientScope_OverridesInjectedPolicy_TriggersFault()
    {
        await using Fixture fixture = Fixture.Create();

        using (FaultInjectionScope scope =
            FaultInjectionScope.Fail(
                FaultInjectionPoint.SnapshotCapture))
        {
            await Assert.ThrowsAsync<FaultInjectionException>(
                () => fixture.Provider.GetSnapshotAsync());
        }

        Assert.Equal(0, fixture.UpdateChecker.InvocationCount);
    }

    [Fact]
    public async Task GetSnapshotAsync_AmbientScope_OverridesInjectedPolicy_SuppressesFault()
    {
        await using Fixture fixture = Fixture.Create(
            faultPolicy: FaultInjectionPolicy.For(
                [FaultInjectionPoint.SnapshotCapture]));

        using (FaultInjectionScope scope =
            FaultInjectionScope.Fail(
                FaultInjectionPoint.HttpRequest))
        {
            RuntimeSnapshot snapshot =
                await fixture.Provider.GetSnapshotAsync();

            Assert.Equal(1, snapshot.SchemaVersion);
        }

        Assert.Equal(
            1,
            fixture.UpdateChecker.InvocationCount);
    }

    [Fact]
    public async Task GetSnapshotAsync_NestedScopes_InnerScopeTriggersCorrectPoint()
    {
        await using Fixture fixture = Fixture.Create();

        using (FaultInjectionScope outer =
            FaultInjectionScope.Fail(
                FaultInjectionPoint.HttpRequest))
        {
            using (FaultInjectionScope inner =
                FaultInjectionScope.Fail(
                    FaultInjectionPoint.SnapshotCapture))
            {
                FaultInjectionException exception =
                    await Assert.ThrowsAsync<FaultInjectionException>(
                        () => fixture.Provider.GetSnapshotAsync());

                Assert.Equal(
                    FaultInjectionPoint.SnapshotCapture,
                    exception.Point);
            }
        }

        Assert.Equal(0, fixture.UpdateChecker.InvocationCount);
    }

    [Fact]
    public async Task GetSnapshotAsync_NestedScopes_UnselectedInnerScope_NoFault()
    {
        await using Fixture fixture = Fixture.Create();

        using (FaultInjectionScope outer =
            FaultInjectionScope.Fail(
                FaultInjectionPoint.SnapshotCapture))
        {
            using (FaultInjectionScope inner =
                FaultInjectionScope.Fail(
                    FaultInjectionPoint.HttpRequest))
            {
                RuntimeSnapshot snapshot =
                    await fixture.Provider.GetSnapshotAsync();

                Assert.Equal(1, snapshot.SchemaVersion);
            }
        }

        Assert.Equal(
            1,
            fixture.UpdateChecker.InvocationCount);
    }

    [Fact]
    public async Task GetSnapshotAsync_ScopeDisposal_PreventsLeakage()
    {
        await using Fixture fixture = Fixture.Create();

        FaultInjectionScope scope =
            FaultInjectionScope.Fail(
                FaultInjectionPoint.SnapshotCapture);
        scope.Dispose();

        RuntimeSnapshot snapshot =
            await fixture.Provider.GetSnapshotAsync();

        Assert.Equal(1, snapshot.SchemaVersion);
        Assert.Equal(
            1,
            fixture.UpdateChecker.InvocationCount);
    }

    [Fact]
    public async Task GetSnapshotAsync_Cancellation_RemainsUnchanged()
    {
        await using Fixture fixture = Fixture.Create();
        fixture.UpdateChecker.ExceptionToThrow =
            new OperationCanceledException();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => fixture.Provider.GetSnapshotAsync());
    }

    [Fact]
    public async Task GetSnapshotAsync_PreCancelledTokenWithActiveFault_ThrowsCancellationFirst()
    {
        CountingRouteInventory inventory = new();
        await using Fixture fixture = Fixture.Create(
            routeInventory: inventory);

        using CancellationTokenSource cts = new();
        cts.Cancel();

        using (FaultInjectionScope scope =
            FaultInjectionScope.Fail(
                FaultInjectionPoint.SnapshotCapture))
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => fixture.Provider.GetSnapshotAsync(cts.Token));
        }

        Assert.Equal(0, inventory.LoadCallCount);
        Assert.Equal(0, fixture.UpdateChecker.InvocationCount);
    }

    [Fact]
    public async Task GetSnapshotAsync_ConcurrentUnrelatedProvider_RemainsFunctional()
    {
        await using Fixture faulted = Fixture.Create(
            faultPolicy: FaultInjectionPolicy.For(
                [FaultInjectionPoint.SnapshotCapture]));
        await using Fixture healthy = Fixture.Create();

        Task<RuntimeSnapshot> faultedTask =
            faulted.Provider.GetSnapshotAsync();
        Task<RuntimeSnapshot> healthyTask =
            healthy.Provider.GetSnapshotAsync();

        FaultInjectionException exception =
            await Assert.ThrowsAsync<FaultInjectionException>(
                () => faultedTask);

        RuntimeSnapshot snapshot = await healthyTask;

        Assert.Equal(
            FaultInjectionPoint.SnapshotCapture,
            exception.Point);
        Assert.Equal(0, faulted.UpdateChecker.InvocationCount);
        Assert.Equal(1, snapshot.SchemaVersion);
        Assert.Equal(
            1,
            healthy.UpdateChecker.InvocationCount);
    }

    [Fact]
    public async Task GetSnapshotAsync_ScopeInChildTask_DoesNotLeakToParentContext()
    {
        await using Fixture fixture = Fixture.Create();

        await Task.Run(() =>
        {
            using (FaultInjectionScope scope =
                FaultInjectionScope.Fail(
                    FaultInjectionPoint.SnapshotCapture))
            {
                Assert.True(FaultInjectionScope.IsActive);
            }
        });

        Assert.False(FaultInjectionScope.IsActive);

        RuntimeSnapshot snapshot =
            await fixture.Provider.GetSnapshotAsync();

        Assert.Equal(1, snapshot.SchemaVersion);
        Assert.Equal(
            1,
            fixture.UpdateChecker.InvocationCount);
    }

    private sealed class CountingRouteInventory :
        IRouteInventoryPersistence
    {
        public int LoadCallCount { get; private set; }

        public Task<RouteInventory> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            LoadCallCount++;
            return Task.FromResult(new RouteInventory());
        }

        public Task SaveAsync(
            RouteInventory inventory,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task MutateAsync(
            Func<RouteInventory, RouteInventory> transform,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
