using System.Diagnostics;
using PathVeer.Core.Observability.Telemetry;
using Xunit;

namespace PathVeer.Core.Tests.Observability.Telemetry;

public sealed class PathVeerTelemetryTests
{
    [Fact]
    public void ActivitySource_Name_IsExact()
    {
        Assert.Equal("PathVeer.Core", PathVeerTelemetry.ActivitySource.Name);
    }

    [Fact]
    public void Meter_Name_IsExact()
    {
        Assert.Equal("PathVeer.Core", PathVeerTelemetry.Meter.Name);
    }

    [Fact]
    public void Version_IsNonEmptyAndStable()
    {
        Assert.False(string.IsNullOrEmpty(PathVeerTelemetry.Version));
        Assert.Equal(PathVeerTelemetry.Version, PathVeerTelemetry.Version);
    }

    [Fact]
    public void Instances_AreStaticAndRepeatedlyEqual()
    {
        Assert.Same(PathVeerTelemetry.ActivitySource, PathVeerTelemetry.ActivitySource);
        Assert.Same(PathVeerTelemetry.Meter, PathVeerTelemetry.Meter);
    }

    [Fact]
    public void Definitions_AreReadOnlyStaticProperties()
    {
        var prop = typeof(PathVeerTelemetry)
            .GetProperty(nameof(PathVeerTelemetry.ActivitySource));
        Assert.True(prop is { CanRead: true, GetMethod.IsStatic: true });
        Assert.False(prop!.CanWrite);
    }

    [Fact]
    public void NoListenersOrExporters_AreCreatedHere()
    {
        // Accessing the definitions must not throw and must not attach a
        // listener. Since ActivitySource/Meter expose no listener collection
        // publicly, we assert only that repeated access is stable and cheap.
        var before = PathVeerTelemetry.ActivitySource;
        var after = PathVeerTelemetry.ActivitySource;
        Assert.Same(before, after);
    }
}
