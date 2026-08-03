using System.Collections.Immutable;
using IranDirect.Core.Testing.FaultInjection;

namespace IranDirect.Core.Tests.Testing.FaultInjection;

public sealed class FaultInjectionPolicyTests
{
    [Fact]
    public void DefaultPolicy_NeverFails()
    {
        Assert.Empty(FaultInjectionScope.CurrentPoints);

        foreach (FaultInjectionPoint point in
            Enum.GetValues<FaultInjectionPoint>())
        {
            Assert.False(FaultInjectionScope.ShouldFail(point));
        }
    }

    [Fact]
    public void Fail_SelectedPoint_Fails()
    {
        using FaultInjectionScope scope =
            FaultInjectionScope.Fail(FaultInjectionPoint.JsonSave);

        Assert.True(FaultInjectionScope.ShouldFail(
            FaultInjectionPoint.JsonSave));
    }

    [Fact]
    public void Fail_UnselectedPoint_DoesNotFail()
    {
        using FaultInjectionScope scope =
            FaultInjectionScope.Fail(FaultInjectionPoint.JsonSave);

        Assert.False(FaultInjectionScope.ShouldFail(
            FaultInjectionPoint.DnsLookup));
    }

    [Fact]
    public void Fail_MultiplePoints_AllFail()
    {
        using FaultInjectionScope scope =
            FaultInjectionScope.Fail(
                FaultInjectionPoint.FileRead,
                FaultInjectionPoint.HttpRequest,
                FaultInjectionPoint.RouteCreate);

        Assert.True(FaultInjectionScope.ShouldFail(
            FaultInjectionPoint.FileRead));
        Assert.True(FaultInjectionScope.ShouldFail(
            FaultInjectionPoint.HttpRequest));
        Assert.True(FaultInjectionScope.ShouldFail(
            FaultInjectionPoint.RouteCreate));
    }

    [Fact]
    public void Fail_DuplicatePoints_Deduplicated()
    {
        using FaultInjectionScope scope =
            FaultInjectionScope.Fail(
                FaultInjectionPoint.SnapshotCapture,
                FaultInjectionPoint.SnapshotCapture,
                FaultInjectionPoint.SnapshotCapture);

        ImmutableHashSet<FaultInjectionPoint> points =
            FaultInjectionScope.CurrentPoints;

        Assert.Single(points);
        Assert.Contains(
            FaultInjectionPoint.SnapshotCapture,
            points);
    }

    [Fact]
    public void Fail_EmptyPoints_InjectsNothing()
    {
        using FaultInjectionScope scope =
            FaultInjectionScope.Fail();

        Assert.True(FaultInjectionScope.IsActive);

        foreach (FaultInjectionPoint point in
            Enum.GetValues<FaultInjectionPoint>())
        {
            Assert.False(FaultInjectionScope.ShouldFail(point));
        }
    }

    [Fact]
    public void Fail_NullPointArray_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => FaultInjectionScope.Fail(null!));
    }

    [Fact]
    public void Policy_For_ThrowsOnNull()
    {
        Assert.Throws<ArgumentNullException>(
            () => FaultInjectionPolicy.For(null!));
    }

    [Fact]
    public void Policy_Never_FailsNothing()
    {
        Assert.False(FaultInjectionPolicy.Never.ShouldFail(
            FaultInjectionPoint.FileRead));
    }

    [Fact]
    public void Policy_For_RespectsSelectedPoints()
    {
        FaultInjectionPolicy policy =
            FaultInjectionPolicy.For(
            [
                FaultInjectionPoint.DnsLookup,
                FaultInjectionPoint.NamedPipeSend
            ]);

        Assert.True(policy.ShouldFail(FaultInjectionPoint.DnsLookup));
        Assert.True(policy.ShouldFail(
            FaultInjectionPoint.NamedPipeSend));
        Assert.False(policy.ShouldFail(FaultInjectionPoint.JsonLoad));
    }
}
