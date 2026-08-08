using PathVeer.Core.CustomRoutes;
using PathVeer.Core.Diagnostics;
using PathVeer.Core.Ipc;
using PathVeer.Core.Runtime.Execution;
using PathVeer.Core.Runtime.Profiling;
using System.Diagnostics;
using PathVeer.Core.Observability.Telemetry;
using Xunit;

namespace PathVeer.Core.Tests.Observability.Telemetry;

/// <summary>
/// Lightweight allocation/zero-cost checks for the telemetry foundation.
/// These assert no allocation on the hot read path after static init, and
/// effectively-zero allocation for the mapping helpers. No permanent
/// BenchmarkDotNet class is introduced.
/// </summary>
public sealed class TelemetryAllocationTests
{
    [Fact]
    public void AccessingActivitySource_AllocatesNothingAfterInit()
    {
        // Warm up static initialization.
        _ = IranDirectTelemetry.ActivitySource;

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++)
            _ = IranDirectTelemetry.ActivitySource;
        long after = GC.GetAllocatedBytesForCurrentThread();

        Assert.Equal(0, after - before);
    }

    [Fact]
    public void AccessingMeter_AllocatesNothingAfterInit()
    {
        _ = IranDirectTelemetry.Meter;

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++)
            _ = IranDirectTelemetry.Meter;
        long after = GC.GetAllocatedBytesForCurrentThread();

        Assert.Equal(0, after - before);
    }

    [Fact]
    public void EnumMappingHelpers_AllocateZeroPerCall()
    {
        var ioException = new IOException();
        _ = TelemetryOutcomeMapper.Map(RuntimeExecutionResultStatus.Failed);
        _ = TelemetryOutcomeMapper.Map(CycleCompletionStatus.Completed);
        _ = TelemetryOutcomeMapper.Map(DiagnosticSeverity.Warning);
        _ = TelemetryOutcomeMapper.Map(IranDirectCommand.Repair);
        _ = TelemetryFailureCategoryMapper.Map(ioException);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++)
        {
            _ = TelemetryOutcomeMapper.Map(RuntimeExecutionResultStatus.Failed);
            _ = TelemetryOutcomeMapper.Map(CycleCompletionStatus.Completed);
            _ = TelemetryOutcomeMapper.Map(DiagnosticSeverity.Warning);
            _ = TelemetryOutcomeMapper.Map(IranDirectCommand.Repair);
            _ = TelemetryFailureCategoryMapper.Map(ioException);
        }
        long after = GC.GetAllocatedBytesForCurrentThread();

        // switch expressions over enums + string-returning mappers may incur
        // a trivial per-call cost only via the IOException construction above
        // (outside the loop body). Inside the loop, mapping is allocation-free.
        // The IOException is created once before the loop, so the loop itself
        // allocates nothing.
        Assert.Equal(0, after - before);
    }

    [Fact]
    public void TagValidation_ApprovedPath_AllocatesNothing()
    {
        _ = TelemetryTagValidator.IsApprovedTagName("outcome");

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++)
            _ = TelemetryTagValidator.IsApprovedTagName("outcome");
        long after = GC.GetAllocatedBytesForCurrentThread();

        Assert.Equal(0, after - before);
    }
}
