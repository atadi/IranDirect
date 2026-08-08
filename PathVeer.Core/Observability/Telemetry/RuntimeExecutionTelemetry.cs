using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Threading;
using PathVeer.Core.Runtime.Execution;

namespace PathVeer.Core.Observability.Telemetry;

/// <summary>
/// Runtime execution telemetry: one child <c>Runtime.Execute</c> activity per
/// execution attempt plus an execution-duration histogram and an
/// operations-per-cycle histogram, created once against the Phase 32.2
/// <see cref="IranDirectTelemetry.Meter"/>.
///
/// This instruments exactly one production operation — the single invocation of
/// <c>IRuntimeExecutor.ExecuteAsync</c> inside the runtime-cycle orchestration
/// (see <c>PathVeerController.RunCycleCoreAsync</c>). It does NOT instrument
/// the executor implementation, per-step loops, prefix-group mutation loops,
/// route handler implementations, or individual execution handlers.
///
/// The execution activity is a child of <c>IranDirect.RuntimeCycle</c> whenever
/// execution occurs inside an instrumented runtime cycle (it naturally nests
/// under <see cref="Activity.Current"/>); if started without an active parent
/// it simply becomes a root activity — no fake parent is manufactured.
///
/// Telemetry is failure-isolated: a null <see cref="ActivitySource.StartActivity"/>
/// result, a missing listener, or a failed measurement can never alter execution
/// behavior. The executor's existing exception/recovery boundary is preserved.
/// </summary>
public static class RuntimeExecutionTelemetry
{
    private const string OperationValue = IranDirectTagValues.OperationExecute;

    private static readonly Histogram<double> s_executionDuration = IranDirectTelemetry
        .Meter.CreateHistogram<double>(
            IranDirectMetricNames.RuntimeExecutionDuration,
            unit: "ms",
            description: "Elapsed time spent executing the runtime plan.");

    private static readonly Histogram<double> s_operationsPerCycle = IranDirectTelemetry
        .Meter.CreateHistogram<double>(
            IranDirectMetricNames.RuntimeOperationsPerCycle,
            unit: "{operation}",
            description: "Number of runtime execution operations attempted in one cycle.");

    /// <summary>
    /// Begins one execution child activity (nested under the active runtime-cycle
    /// activity when present) and returns a scope that records the terminal
    /// outcome, duration, and operation count. Resilient to a null activity.
    /// </summary>
    internal static RuntimeExecutionTelemetryScope Start(int operationCount)
    {
        Activity? activity = IranDirectTelemetry.ActivitySource.StartActivity(
            IranDirectActivityNames.RuntimeExecute,
            ActivityKind.Internal);

        if (activity is not null)
        {
            activity.SetTag(IranDirectTagNames.Operation, OperationValue);
        }

        return new RuntimeExecutionTelemetryScope(activity, operationCount);
    }

    internal static KeyValuePair<string, object?>[] Tags(
        string outcome,
        string? failureCategory = null)
    {
        if (failureCategory is null)
        {
            return
            [
                new(IranDirectTagNames.Operation, OperationValue),
                new(IranDirectTagNames.Outcome, outcome),
            ];
        }

        return
        [
            new(IranDirectTagNames.Operation, OperationValue),
            new(IranDirectTagNames.Outcome, outcome),
            new(IranDirectTagNames.FailureCategory, failureCategory),
        ];
    }

    internal static void RecordDuration(string outcome, double elapsedMs)
    {
        s_executionDuration.Record(elapsedMs, Tags(outcome));
    }

    internal static void RecordOperations(int operationCount, string outcome)
    {
        s_operationsPerCycle.Record(operationCount, Tags(outcome));
    }
}

/// <summary>
/// Disposable scope owning one complete execution attempt. Records exactly one
/// execution-duration sample and exactly one operations-per-cycle sample,
/// regardless of outcome. Top-level type (not nested) so it is reachable via a
/// namespace <c>using</c>.
/// </summary>
internal struct RuntimeExecutionTelemetryScope : IDisposable
{
    private readonly Activity? _activity;
    private readonly int _operationCount;
    private readonly long _startTimestamp;
    private int _completed;

    internal RuntimeExecutionTelemetryScope(
        Activity? activity,
        int operationCount)
    {
        _activity = activity;
        _operationCount = operationCount;
        _startTimestamp = Stopwatch.GetTimestamp();
        _completed = 0;
    }

    public void Complete(RuntimeExecutionResult result)
    {
        if (result is null)
        {
            RecordTerminal(IranDirectTagValues.OutcomeUnknown);
            return;
        }

        TelemetryOutcome outcome = TelemetryOutcomeMapper.Map(result.Status);
        string outcomeString = TelemetryOutcomeMapper.ToOutcomeString(outcome);

        RecordTerminal(outcomeString);

        if (_activity is not null)
        {
            _activity.SetTag(IranDirectTagNames.Outcome, outcomeString);

            // Cancelled is not an error; leave status Unset and omit the
            // failure_category tag entirely.
            if (outcome == TelemetryOutcome.Cancelled)
            {
                _activity.SetStatus(ActivityStatusCode.Unset);
                return;
            }

            _activity.SetStatus(
                outcome == TelemetryOutcome.Failure ||
                outcome == TelemetryOutcome.Timeout
                    ? ActivityStatusCode.Error
                    : ActivityStatusCode.Ok);

            // Execution failure is represented only by result status; free-form
            // result error messages are never parsed into a failure_category.
            // failure_category is only set when a concrete exception mapping
            // exists (see CompleteFailure), not from result text.
        }
    }

    public void CompleteFailure(Exception exception)
    {
        TelemetryFailureCategory category =
            TelemetryFailureCategoryMapper.Map(exception);
        string categoryString =
            TelemetryFailureCategoryMapper.ToCategoryString(category);

        RecordTerminal(IranDirectTagValues.Failure, categoryString);

        if (_activity is not null)
        {
            _activity.SetTag(
                IranDirectTagNames.Outcome,
                IranDirectTagValues.Failure);
            _activity.SetTag(
                IranDirectTagNames.FailureCategory,
                categoryString);
            // Phase 32.1 does not approve exception events; bounded
            // failure_category is set, exception text is never used.
            _activity.SetStatus(
                ActivityStatusCode.Error,
                categoryString);
        }
    }

    public void CompleteCancelled()
    {
        RecordTerminal(IranDirectTagValues.Cancelled);

        if (_activity is not null)
        {
            _activity.SetTag(
                IranDirectTagNames.Outcome,
                IranDirectTagValues.Cancelled);
            // Cancellation is not an error; leave status Unset and omit
            // failure_category.
        }
    }

    private void RecordTerminal(string outcome, string? failureCategory = null)
    {
        if (Interlocked.Exchange(ref _completed, 1) == 1)
            return; // guard against double completion

        double elapsedMs = Stopwatch.GetElapsedTime(_startTimestamp)
            .TotalMilliseconds;
        RuntimeExecutionTelemetry.RecordDuration(outcome, elapsedMs);
        RuntimeExecutionTelemetry.RecordOperations(_operationCount, outcome);
    }

    public void Dispose()
    {
        // If the owner forgets a terminal call, treat disposal as a terminal
        // duration + operation-count record with the unknown outcome so neither
        // measurement is dropped. Terminal counters/samples other than the two
        // histograms are intentionally NOT recorded here.
        if (Interlocked.Exchange(ref _completed, 1) == 1)
        {
            _activity?.Dispose();
            return;
        }

        double elapsedMs = Stopwatch.GetElapsedTime(_startTimestamp)
            .TotalMilliseconds;
        RuntimeExecutionTelemetry.RecordDuration(
            IranDirectTagValues.OutcomeUnknown,
            elapsedMs);
        RuntimeExecutionTelemetry.RecordOperations(
            _operationCount,
            IranDirectTagValues.OutcomeUnknown);
        _activity?.Dispose();
    }
}
