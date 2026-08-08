using PathVeer.Core.CustomRoutes;
using PathVeer.Core.Diagnostics;
using PathVeer.Core.Ipc;
using PathVeer.Core.Runtime.Execution;
using PathVeer.Core.Runtime.Profiling;
using PathVeer.Core.Observability.Telemetry;
using Xunit;

namespace PathVeer.Core.Tests.Observability.Telemetry;

public sealed class TelemetryEnumMappingTests
{
    [Theory]
    [InlineData(RuntimeExecutionResultStatus.Completed, TelemetryOutcome.Success)]
    [InlineData(RuntimeExecutionResultStatus.NoExecutionRequired, TelemetryOutcome.NoChange)]
    [InlineData(RuntimeExecutionResultStatus.Planned, TelemetryOutcome.Success)]
    [InlineData(RuntimeExecutionResultStatus.Failed, TelemetryOutcome.Failure)]
    [InlineData(RuntimeExecutionResultStatus.Cancelled, TelemetryOutcome.Cancelled)]
    [InlineData(RuntimeExecutionResultStatus.PartiallyCompleted, TelemetryOutcome.Failure)]
    public void Map_RuntimeExecutionResultStatus(RuntimeExecutionResultStatus status, TelemetryOutcome expected)
    {
        Assert.Equal(expected, TelemetryOutcomeMapper.Map(status));
        AssertOutcomeBelongsToApprovedSet(expected);
    }

    [Theory]
    [InlineData(CycleCompletionStatus.Completed, TelemetryOutcome.Success)]
    [InlineData(CycleCompletionStatus.PartiallyCompleted, TelemetryOutcome.Failure)]
    [InlineData(CycleCompletionStatus.Failed, TelemetryOutcome.Failure)]
    [InlineData(CycleCompletionStatus.Cancelled, TelemetryOutcome.Cancelled)]
    public void Map_CycleCompletionStatus(CycleCompletionStatus status, TelemetryOutcome expected)
    {
        Assert.Equal(expected, TelemetryOutcomeMapper.Map(status));
    }

    [Theory]
    [InlineData(DiagnosticSeverity.Pass, "pass")]
    [InlineData(DiagnosticSeverity.Info, "pass")]
    [InlineData(DiagnosticSeverity.Warning, "warning")]
    [InlineData(DiagnosticSeverity.Fail, "failure")]
    [InlineData(DiagnosticSeverity.Error, "failure")]
    public void Map_DiagnosticSeverity(DiagnosticSeverity severity, string expected)
    {
        Assert.Equal(expected, TelemetryOutcomeMapper.Map(severity));
    }

    [Theory]
    [InlineData(CustomRouteDnsCacheState.Fresh, "fresh")]
    [InlineData(CustomRouteDnsCacheState.Stale, "stale")]
    [InlineData(CustomRouteDnsCacheState.Expired, "miss")]
    [InlineData(CustomRouteDnsCacheState.Failed, "failed")]
    [InlineData(CustomRouteDnsCacheState.Missing, "miss")]
    [InlineData(CustomRouteDnsCacheState.Disabled, "failed")]
    public void Map_CustomRouteDnsCacheState(CustomRouteDnsCacheState state, string expected)
    {
        Assert.Equal(expected, TelemetryOutcomeMapper.Map(state));
    }

    [Fact]
    public void Map_IranDirectCommand_EveryMember()
    {
        foreach (PathVeerCommand command in Enum.GetValues<PathVeerCommand>())
        {
            string mapped = TelemetryOutcomeMapper.Map(command);
            Assert.Equal(command.ToString(), mapped);
            Assert.False(string.IsNullOrEmpty(mapped));
        }
    }

    [Fact]
    public void Map_UnknownNumericValue_FallsBack()
    {
        const RuntimeExecutionResultStatus bogus =
            (RuntimeExecutionResultStatus)999;
        Assert.Equal(TelemetryOutcome.Unknown, TelemetryOutcomeMapper.Map(bogus));
    }

    [Fact]
    public void NoEnumToStringLeakage_InOutcomeOrCacheMapping()
    {
        // Cache_state "Failed" maps to "failed" (lowercased, bounded), proving
        // we do not relay the enum name verbatim.
        Assert.Equal("failed", TelemetryOutcomeMapper.Map(CustomRouteDnsCacheState.Failed));
        Assert.NotEqual("Failed", TelemetryOutcomeMapper.Map(CustomRouteDnsCacheState.Failed));
    }

    private static void AssertOutcomeBelongsToApprovedSet(TelemetryOutcome outcome)
    {
        string s = TelemetryOutcomeMapper.ToOutcomeString(outcome);
        Assert.True(TelemetryTagValidator.IsValidBoundedValue("outcome", s));
    }
}
