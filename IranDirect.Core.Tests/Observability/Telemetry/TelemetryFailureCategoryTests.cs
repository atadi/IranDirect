using System.IO;
using System.Net.Http;
using System.Text.Json;
using IranDirect.Core.Testing.FaultInjection;
using IranDirect.Core.Observability.Telemetry;
using Xunit;

namespace IranDirect.Core.Tests.Observability.Telemetry;

public sealed class TelemetryFailureCategoryTests
{
    [Theory]
    [InlineData(typeof(OperationCanceledException), TelemetryFailureCategory.Cancellation)]
    [InlineData(typeof(TimeoutException), TelemetryFailureCategory.Timeout)]
    [InlineData(typeof(HttpRequestException), TelemetryFailureCategory.Http)]
    [InlineData(typeof(IOException), TelemetryFailureCategory.Io)]
    [InlineData(typeof(UnauthorizedAccessException), TelemetryFailureCategory.Io)]
    [InlineData(typeof(JsonException), TelemetryFailureCategory.Serialization)]
    public void Map_KnownExceptionTypes(Type exType, TelemetryFailureCategory expected)
    {
        Exception ex = (Exception)Activator.CreateInstance(exType, "msg")!;
        Assert.Equal(expected, TelemetryFailureCategoryMapper.Map(ex));
        AssertCategoryBelongsToApprovedSet(expected);
    }

    [Fact]
    public void Map_ArbitraryException_FallsBackToUnknown()
    {
        Assert.Equal(
            TelemetryFailureCategory.Unknown,
            TelemetryFailureCategoryMapper.Map(new InvalidOperationException("x")));
    }

    [Fact]
    public void Map_NullException_FallsBackToUnknown()
    {
        Assert.Equal(
            TelemetryFailureCategory.Unknown,
            TelemetryFailureCategoryMapper.Map(null!));
    }

    [Theory]
    [InlineData(FaultInjectionPoint.HttpRequest, TelemetryFailureCategory.Http)]
    [InlineData(FaultInjectionPoint.DnsLookup, TelemetryFailureCategory.Dns)]
    [InlineData(FaultInjectionPoint.RouteEnumeration, TelemetryFailureCategory.Routing)]
    [InlineData(FaultInjectionPoint.RouteCreate, TelemetryFailureCategory.Routing)]
    [InlineData(FaultInjectionPoint.RouteDelete, TelemetryFailureCategory.Routing)]
    [InlineData(FaultInjectionPoint.FileRead, TelemetryFailureCategory.Io)]
    [InlineData(FaultInjectionPoint.FileWrite, TelemetryFailureCategory.Io)]
    [InlineData(FaultInjectionPoint.FileMove, TelemetryFailureCategory.Io)]
    [InlineData(FaultInjectionPoint.JsonLoad, TelemetryFailureCategory.Serialization)]
    [InlineData(FaultInjectionPoint.JsonSave, TelemetryFailureCategory.Serialization)]
    [InlineData(FaultInjectionPoint.NamedPipeSend, TelemetryFailureCategory.Io)]
    [InlineData(FaultInjectionPoint.SnapshotCapture, TelemetryFailureCategory.Unknown)]
    [InlineData(FaultInjectionPoint.DiagnosticsRun, TelemetryFailureCategory.Unknown)]
    public void Map_FaultInjectionPoint(FaultInjectionPoint point, TelemetryFailureCategory expected)
    {
        var ex = new FaultInjectionException(point);
        Assert.Equal(expected, TelemetryFailureCategoryMapper.Map(ex));
    }

    [Fact]
    public void Category_DoesNotLeakExceptionText()
    {
        var ex = new IOException("secret path C:\\secret\\file.txt denied");
        string category = TelemetryFailureCategoryMapper.ToCategoryString(
            TelemetryFailureCategoryMapper.Map(ex));
        Assert.Equal("io", category);
        Assert.DoesNotContain("secret", category);
        Assert.DoesNotContain("C:\\", category);
    }

    [Fact]
    public void InnerException_Text_NotUsedForCategory()
    {
        var outer = new InvalidOperationException(
            "outer", new IOException("inner secret"));
        // Outer type is not mapped specially -> unknown; inner text ignored.
        Assert.Equal(
            TelemetryFailureCategory.Unknown,
            TelemetryFailureCategoryMapper.Map(outer));
    }

    private static void AssertCategoryBelongsToApprovedSet(TelemetryFailureCategory category)
    {
        string s = TelemetryFailureCategoryMapper.ToCategoryString(category);
        Assert.True(TelemetryTagValidator.IsValidBoundedValue("failure_category", s));
    }
}
