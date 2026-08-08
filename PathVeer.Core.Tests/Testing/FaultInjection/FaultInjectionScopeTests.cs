using System.Collections.Immutable;
using PathVeer.Core.Testing.FaultInjection;

namespace PathVeer.Core.Tests.Testing.FaultInjection;

public sealed class FaultInjectionScopeTests
{
    [Fact]
    public void Dispose_RestoresPreviousState()
    {
        using (FaultInjectionScope outer =
            FaultInjectionScope.Fail(FaultInjectionPoint.JsonLoad))
        {
            Assert.True(FaultInjectionScope.ShouldFail(
                FaultInjectionPoint.JsonLoad));

            using (FaultInjectionScope inner =
                FaultInjectionScope.Fail(FaultInjectionPoint.JsonSave))
            {
                Assert.True(FaultInjectionScope.ShouldFail(
                    FaultInjectionPoint.JsonSave));
                Assert.False(FaultInjectionScope.ShouldFail(
                    FaultInjectionPoint.JsonLoad));
            }

            Assert.True(FaultInjectionScope.ShouldFail(
                FaultInjectionPoint.JsonLoad));
            Assert.False(FaultInjectionScope.ShouldFail(
                FaultInjectionPoint.JsonSave));
        }

        Assert.False(FaultInjectionScope.IsActive);
        Assert.Empty(FaultInjectionScope.CurrentPoints);
    }

    [Fact]
    public void NestedScope_RestoresOuterScope()
    {
        using FaultInjectionScope outer =
            FaultInjectionScope.Fail(FaultInjectionPoint.FileRead);

        using (FaultInjectionScope inner =
            FaultInjectionScope.Fail(
                FaultInjectionPoint.FileMove,
                FaultInjectionPoint.RouteDelete))
        {
            Assert.True(FaultInjectionScope.ShouldFail(
                FaultInjectionPoint.FileMove));
            Assert.True(FaultInjectionScope.ShouldFail(
                FaultInjectionPoint.RouteDelete));
            Assert.False(FaultInjectionScope.ShouldFail(
                FaultInjectionPoint.FileRead));
        }

        Assert.True(FaultInjectionScope.ShouldFail(
            FaultInjectionPoint.FileRead));
    }

    [Fact]
    public void MultipleNestingLevels_RestoresEachLevel()
    {
        using (FaultInjectionScope level1 =
            FaultInjectionScope.Fail(FaultInjectionPoint.DnsLookup))
        {
            using (FaultInjectionScope level2 =
                FaultInjectionScope.Fail(FaultInjectionPoint.HttpRequest))
            {
                using (FaultInjectionScope level3 =
                    FaultInjectionScope.Fail(
                        FaultInjectionPoint.FileWrite))
                {
                    Assert.True(FaultInjectionScope.ShouldFail(
                        FaultInjectionPoint.FileWrite));
                    Assert.False(FaultInjectionScope.ShouldFail(
                        FaultInjectionPoint.HttpRequest));
                }

                Assert.True(FaultInjectionScope.ShouldFail(
                    FaultInjectionPoint.HttpRequest));
                Assert.False(FaultInjectionScope.ShouldFail(
                    FaultInjectionPoint.DnsLookup));
            }

            Assert.True(FaultInjectionScope.ShouldFail(
                FaultInjectionPoint.DnsLookup));
        }

        Assert.False(FaultInjectionScope.IsActive);
    }

    [Fact]
    public void Dispose_Repeated_IsSafe()
    {
        FaultInjectionScope scope =
            FaultInjectionScope.Fail(FaultInjectionPoint.RouteCreate);

        Assert.True(FaultInjectionScope.ShouldFail(
            FaultInjectionPoint.RouteCreate));

        scope.Dispose();
        scope.Dispose();
        scope.Dispose();

        Assert.False(FaultInjectionScope.ShouldFail(
            FaultInjectionPoint.RouteCreate));
    }

    [Fact]
    public void InterleavedSiblingDisposal_DoesNotClobberActiveScope()
    {
        FaultInjectionScope first =
            FaultInjectionScope.Fail(FaultInjectionPoint.FileRead);
        FaultInjectionScope second =
            FaultInjectionScope.Fail(FaultInjectionPoint.FileWrite);

        first.Dispose();

        Assert.True(FaultInjectionScope.ShouldFail(
            FaultInjectionPoint.FileWrite));

        second.Dispose();

        Assert.False(FaultInjectionScope.IsActive);
    }

    [Fact]
    public void Scope_DoesNotLeakAfterCompletion()
    {
        RunScopedWork();

        Assert.False(FaultInjectionScope.IsActive);
        Assert.Empty(FaultInjectionScope.CurrentPoints);

        static void RunScopedWork()
        {
            using FaultInjectionScope scope =
                FaultInjectionScope.Fail(FaultInjectionPoint.DnsLookup);

            Assert.True(FaultInjectionScope.ShouldFail(
                FaultInjectionPoint.DnsLookup));
        }
    }

    [Fact]
    public async Task AsyncContinuation_RetainsState()
    {
        await Task.Yield();

        using (FaultInjectionScope scope =
            FaultInjectionScope.Fail(FaultInjectionPoint.NamedPipeSend))
        {
            Assert.True(FaultInjectionScope.ShouldFail(
                FaultInjectionPoint.NamedPipeSend));

            await Task.Yield();
            await Task.Delay(10);

            Assert.True(FaultInjectionScope.ShouldFail(
                FaultInjectionPoint.NamedPipeSend));
        }

        Assert.False(FaultInjectionScope.ShouldFail(
            FaultInjectionPoint.NamedPipeSend));
    }

    [Fact]
    public async Task SiblingTasks_RemainIsolated()
    {
        TaskCompletionSource firstEntered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource secondEntered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        Task first = RunIsolated(
            FaultInjectionPoint.RouteEnumeration,
            firstEntered,
            release);

        Task second = RunIsolated(
            FaultInjectionPoint.DiagnosticsRun,
            secondEntered,
            release);

        await Task.WhenAll(firstEntered.Task, secondEntered.Task);

        Assert.Empty(FaultInjectionScope.CurrentPoints);

        release.SetResult();

        await Task.WhenAll(first, second);

        Assert.Empty(FaultInjectionScope.CurrentPoints);

        static async Task RunIsolated(
            FaultInjectionPoint point,
            TaskCompletionSource entered,
            TaskCompletionSource release)
        {
            using (FaultInjectionScope scope =
                FaultInjectionScope.Fail(point))
            {
                Assert.True(FaultInjectionScope.ShouldFail(point));

                entered.SetResult();

                await release.Task;

                Assert.True(FaultInjectionScope.ShouldFail(point));
            }

            Assert.False(FaultInjectionScope.ShouldFail(point));
        }
    }

    [Fact]
    public async Task ConcurrentTasks_UseDifferentFaultPoints()
    {
        TaskCompletionSource firstEntered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource secondEntered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        Task first = RunConcurrent(
            FaultInjectionPoint.JsonLoad,
            firstEntered,
            release);

        Task second = RunConcurrent(
            FaultInjectionPoint.HttpRequest,
            secondEntered,
            release);

        await Task.WhenAll(firstEntered.Task, secondEntered.Task);

        Assert.Empty(FaultInjectionScope.CurrentPoints);

        release.SetResult();

        await Task.WhenAll(first, second);

        Assert.Empty(FaultInjectionScope.CurrentPoints);

        static async Task RunConcurrent(
            FaultInjectionPoint point,
            TaskCompletionSource entered,
            TaskCompletionSource release)
        {
            using (FaultInjectionScope scope =
                FaultInjectionScope.Fail(point))
            {
                entered.SetResult();

                await release.Task;

                ImmutableHashSet<FaultInjectionPoint> active =
                    FaultInjectionScope.CurrentPoints;

                Assert.Equal(point, Assert.Single(active));
            }

            Assert.False(FaultInjectionScope.ShouldFail(point));
        }
    }

    [Fact]
    public async Task OuterState_Unchanged_AfterChildTaskCompletes()
    {
        using FaultInjectionScope outer =
            FaultInjectionScope.Fail(FaultInjectionPoint.SnapshotCapture);

        Task child = Task.Run(() =>
        {
            using FaultInjectionScope inner =
                FaultInjectionScope.Fail(FaultInjectionPoint.FileRead);

            Assert.True(FaultInjectionScope.ShouldFail(
                FaultInjectionPoint.FileRead));
            Assert.False(FaultInjectionScope.ShouldFail(
                FaultInjectionPoint.SnapshotCapture));
        });

        await child;

        Assert.True(FaultInjectionScope.ShouldFail(
            FaultInjectionPoint.SnapshotCapture));
        Assert.False(FaultInjectionScope.ShouldFail(
            FaultInjectionPoint.FileRead));
    }

    [Fact]
    public async Task ParallelScopes_EachTaskSeesOnlyItsOwnPoints()
    {
        FaultInjectionPoint[] points =
        [
            FaultInjectionPoint.FileRead,
            FaultInjectionPoint.FileWrite,
            FaultInjectionPoint.JsonLoad,
            FaultInjectionPoint.JsonSave,
            FaultInjectionPoint.HttpRequest,
            FaultInjectionPoint.DnsLookup,
            FaultInjectionPoint.NamedPipeSend,
            FaultInjectionPoint.RouteEnumeration,
            FaultInjectionPoint.RouteCreate,
            FaultInjectionPoint.RouteDelete,
            FaultInjectionPoint.SnapshotCapture,
            FaultInjectionPoint.DiagnosticsRun
        ];

        Task[] tasks = points.Select(async point =>
        {
            await Task.Yield();

            using FaultInjectionScope scope =
                FaultInjectionScope.Fail(point);

            Assert.True(FaultInjectionScope.ShouldFail(point));

            await Task.Delay(10);

            ImmutableHashSet<FaultInjectionPoint> active =
                FaultInjectionScope.CurrentPoints;

            Assert.Single(active);
            Assert.Contains(point, active);
        }).ToArray();

        await Task.WhenAll(tasks);

        Assert.Empty(FaultInjectionScope.CurrentPoints);
    }
}
