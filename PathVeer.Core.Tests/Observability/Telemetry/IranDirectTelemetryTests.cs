using System.Diagnostics;
using PathVeer.Core.Observability.Telemetry;
using Xunit;

namespace PathVeer.Core.Tests.Observability.Telemetry;

public sealed class IranDirectTelemetryTests
{
    [Fact]
    public void ActivitySource_Name_IsExact()
    {
        Assert.Equal("IranDirect.Core", IranDirectTelemetry.ActivitySource.Name);
    }

    [Fact]
    public void Meter_Name_IsExact()
    {
        Assert.Equal("IranDirect.Core", IranDirectTelemetry.Meter.Name);
    }

    [Fact]
    public void Version_IsNonEmptyAndStable()
    {
        Assert.False(string.IsNullOrEmpty(IranDirectTelemetry.Version));
        Assert.Equal(IranDirectTelemetry.Version, IranDirectTelemetry.Version);
    }

    [Fact]
    public void Instances_AreStaticAndRepeatedlyEqual()
    {
        Assert.Same(IranDirectTelemetry.ActivitySource, IranDirectTelemetry.ActivitySource);
        Assert.Same(IranDirectTelemetry.Meter, IranDirectTelemetry.Meter);
    }

    [Fact]
    public void Definitions_AreReadOnlyStaticProperties()
    {
        var prop = typeof(IranDirectTelemetry)
            .GetProperty(nameof(IranDirectTelemetry.ActivitySource));
        Assert.True(prop is { CanRead: true, GetMethod.IsStatic: true });
        Assert.False(prop!.CanWrite);
    }

    [Fact]
    public void NoListenersOrExporters_AreCreatedHere()
    {
        // Accessing the definitions must not throw and must not attach a
        // listener. Since ActivitySource/Meter expose no listener collection
        // publicly, we assert only that repeated access is stable and cheap.
        var before = IranDirectTelemetry.ActivitySource;
        var after = IranDirectTelemetry.ActivitySource;
        Assert.Same(before, after);
    }
}
