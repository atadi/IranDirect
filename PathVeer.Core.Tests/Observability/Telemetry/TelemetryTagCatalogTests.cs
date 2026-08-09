using PathVeer.Core.Observability.Telemetry;
using Xunit;

namespace PathVeer.Core.Tests.Observability.Telemetry;

public sealed class TelemetryTagCatalogTests
{
    [Fact]
    public void ApprovedTagNames_ExactAndSnakeCase()
    {
        Assert.Equal("operation", PathVeerTagNames.Operation);
        Assert.Equal("outcome", PathVeerTagNames.Outcome);
        Assert.Equal("trigger", PathVeerTagNames.Trigger);
        Assert.Equal("route_kind", PathVeerTagNames.RouteKind);
        Assert.Equal("change_kind", PathVeerTagNames.ChangeKind);
        Assert.Equal("source", PathVeerTagNames.Source);
        Assert.Equal("cache_state", PathVeerTagNames.CacheState);
        Assert.Equal("ipc_command", PathVeerTagNames.IpcCommand);
        Assert.Equal("diagnostic_severity", PathVeerTagNames.DiagnosticSeverity);
        Assert.Equal("service_state", PathVeerTagNames.ServiceState);
        Assert.Equal("failure_category", PathVeerTagNames.FailureCategory);
    }

    [Fact]
    public void ApprovedAndProhibited_HaveNoOverlap()
    {
        foreach (var approved in TelemetryTagValidator.ApprovedTagNames)
            Assert.DoesNotContain(approved, TelemetryTagValidator.ProhibitedTagNames);
    }

    [Fact]
    public void ProhibitedTagNames_Exact()
    {
        var expected = new HashSet<string>
        {
            "destination_prefix", "gateway", "next_hop", "interface_index",
            "interface_name", "domain", "dns_domain", "output_path", "file_path",
            "pipe_payload", "machine_name", "user_name", "exception_message",
            "url", "endpoint", "route_identity", "diagnostic_id",
            "execution_step_identity",
        };
        Assert.Equal(expected, new HashSet<string>(PathVeerTagNames.Prohibited));
    }

    [Theory]
    [InlineData("outcome", "success", true)]
    [InlineData("outcome", "failure", true)]
    [InlineData("trigger", "repair", true)]
    [InlineData("trigger", "cli", true)]
    [InlineData("cache_state", "fresh", true)]
    [InlineData("cache_state", "stale", true)]
    [InlineData("failure_category", "io", true)]
    [InlineData("failure_category", "dns", true)]
    [InlineData("outcome", "banana", false)]
    [InlineData("trigger", "RANDOM", false)]
    [InlineData("cache_state", "expired", false)]
    [InlineData("unknown_tag", "anything", false)]
    public void BoundedValueValidation(string tag, string value, bool valid)
    {
        Assert.Equal(valid, TelemetryTagValidator.IsValidBoundedValue(tag, value));
    }

    [Fact]
    public void ArbitraryAndExceptionMessageValues_Rejected()
    {
        Assert.False(TelemetryTagValidator.IsValidBoundedValue("outcome", "Route 203.0.113.0/24 failed"));
        Assert.False(TelemetryTagValidator.IsValidBoundedValue("failure_category", "System.IO.IOException: access denied"));
    }

    [Theory]
    [InlineData("operation")]
    [InlineData("outcome")]
    [InlineData("ipc_command")]
    public void ApprovedNames_AreRecognized(string name)
    {
        Assert.True(TelemetryTagValidator.IsApprovedTagName(name));
        Assert.False(TelemetryTagValidator.IsProhibitedTagName(name));
    }

    [Theory]
    [InlineData("gateway")]
    [InlineData("destination_prefix")]
    [InlineData("pipe_payload")]
    public void ProhibitedNames_AreRecognized(string name)
    {
        Assert.True(TelemetryTagValidator.IsProhibitedTagName(name));
        Assert.False(TelemetryTagValidator.IsApprovedTagName(name));
    }
}
