using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Threading;

namespace PathVeer.Core.Observability.Telemetry;

/// <summary>
/// Runtime change-set planning telemetry: one child <c>Runtime.PlanChanges</c>
/// activity per planner invocation plus a planning-duration histogram and a
/// changed-routes histogram, created once against the Phase 32.2
/// <see cref="IranDirectTelemetry.Meter"/>.
///
/// This instruments exactly one production operation — the single invocation of
/// <c>RuntimeChangeSetPlanner.Plan</c> inside the runtime reconciliation
/// orchestration. It does NOT instrument the planner implementation, route
/// operations, execution, prefix updates, DNS, IPC, or support exports.
///
/// The planning activity is a child of <c>IranDirect.RuntimeCycle</c> whenever
/// planning occurs inside an instrumented runtime cycle (it naturally nests
/// under <see cref="Activity.Current"/>); if started without an active parent
/// it simply becomes a root activity — no fake parent is manufactured.
///
/// Telemetry is failure-isolated: a null <see cref="ActivitySource.StartActivity"/>
/// result, a missing listener, or a failed measurement can never alter planning
/// behavior. The planner's existing exception/recovery boundary is preserved.
/// </summary>
public static class RuntimePlanningTelemetry
{
    private const string OperationValue = IranDirectTagValues.OperationPlanChanges;

    private static readonly Histogram<double> s_planningDuration = IranDirectTelemetry
        .Meter.CreateHistogram<double>(
            IranDirectMetricNames.RuntimePlanningDuration,
            unit: "ms",
            description: "Elapsed time spent producing the runtime change set.");

    private static readonly Histogram<double> s_changedRoutes = IranDirectTelemetry
        .Meter.CreateHistogram<double>(
            IranDirectMetricNames.RuntimeChangedRoutes,
            unit: "{route}",
            description: "Number of planned runtime changes in a reconciliation.");

    /// <summary>
    /// Begins one planning child activity (nested under the active runtime-cycle
    /// activity when present) and returns a scope that records the terminal
    /// outcome, duration, and changed-route count. Resilient to a null activity.
    /// </summary>
    internal static RuntimePlanningTelemetryScope Start()
    {
        Activity? activity = IranDirectTelemetry.ActivitySource.StartActivity(
            IranDirectActivityNames.RuntimePlanChanges,
            ActivityKind.Internal);

        if (activity is not null)
        {
            activity.SetTag(IranDirectTagNames.Operation, OperationValue);
        }

        return new RuntimePlanningTelemetryScope(activity);
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
        s_planningDuration.Record(elapsedMs, Tags(outcome));
    }

    internal static void RecordChangedRoutes(
        string outcome,
        int changeCount)
    {
        s_changedRoutes.Record(changeCount, Tags(outcome));
    }
}

/// <summary>
/// Disposable scope owning one complete planning invocation. Records exactly
/// one planning-duration sample and, on success/no-change, exactly one
/// changed-routes sample. Top-level type (not nested) so it is reachable via a
/// namespace <c>using</c>.
/// </summary>
internal struct RuntimePlanningTelemetryScope : IDisposable
{
    private readonly Activity? _activity;
    private readonly long _startTimestamp;
    private int _completed;

    internal RuntimePlanningTelemetryScope(Activity? activity)
    {
        _activity = activity;
        _startTimestamp = Stopwatch.GetTimestamp();
        _completed = 0;
    }

    public void CompleteSuccess(int changeCount)
    {
        string outcome = changeCount > 0
            ? IranDirectTagValues.Success
            : IranDirectTagValues.NoChange;

        RecordTerminal(outcome);

        if (_activity is not null)
        {
            _activity.SetTag(IranDirectTagNames.Outcome, outcome);
            _activity.SetStatus(ActivityStatusCode.Ok);
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

    private void RecordTerminal(string outcome, string? failureCategory = null)
    {
        if (Interlocked.Exchange(ref _completed, 1) == 1)
            return; // guard against double completion

        double elapsedMs = Stopwatch.GetElapsedTime(_startTimestamp)
            .TotalMilliseconds;
        RuntimePlanningTelemetry.RecordDuration(outcome, elapsedMs);
    }

    public void Dispose()
    {
        // If the owner forgets a terminal call, treat disposal as a terminal
        // duration record with the unknown outcome so duration is never
        // dropped. Terminal counters/samples other than duration are
        // intentionally NOT recorded here.
        if (Interlocked.Exchange(ref _completed, 1) == 1)
        {
            _activity?.Dispose();
            return;
        }

        double elapsedMs = Stopwatch.GetElapsedTime(_startTimestamp)
            .TotalMilliseconds;
        RuntimePlanningTelemetry.RecordDuration(
            IranDirectTagValues.OutcomeUnknown,
            elapsedMs);
        _activity?.Dispose();
    }
}
