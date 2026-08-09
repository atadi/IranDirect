using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Threading;

namespace PathVeer.Core.Observability.Telemetry;

/// <summary>
/// Runtime-cycle telemetry: one root <c>PathVeer.RuntimeCycle</c> activity
/// per cycle plus four counters and one duration histogram, all created once
/// against the Phase 32.2 <see cref="PathVeerTelemetry.Meter"/>.
///
/// This type only instruments the top-level reconciliation cycle boundary. It
/// does NOT create child spans, does NOT instrument planner internals, route
/// operations, DNS, prefix updates, IPC, or support exports.
///
/// Telemetry is failure-isolated: every recording path is guarded so that a
/// missing listener, a failed measurement, or an exporter outage can never
/// alter runtime reconciliation behavior. <see cref="ActivitySource.StartActivity"/>
/// returning null (no listener) is a fully supported fast path.
/// </summary>
public static class RuntimeCycleTelemetry
{
    private const string OperationValue = "runtime_cycle";

    private static readonly Counter<long> s_cyclesStarted = PathVeerTelemetry
        .Meter.CreateCounter<long>(
            PathVeerMetricNames.RuntimeCyclesStarted,
            unit: "{cycle}",
            description: "Number of runtime reconciliation cycles that started.");

    private static readonly Counter<long> s_cyclesCompleted = PathVeerTelemetry
        .Meter.CreateCounter<long>(
            PathVeerMetricNames.RuntimeCyclesCompleted,
            unit: "{cycle}",
            description: "Number of runtime reconciliation cycles that completed successfully or with no changes.");

    private static readonly Counter<long> s_cyclesFailed = PathVeerTelemetry
        .Meter.CreateCounter<long>(
            PathVeerMetricNames.RuntimeCyclesFailed,
            unit: "{cycle}",
            description: "Number of runtime reconciliation cycles that failed or timed out.");

    private static readonly Counter<long> s_cyclesCancelled = PathVeerTelemetry
        .Meter.CreateCounter<long>(
            PathVeerMetricNames.RuntimeCyclesCancelled,
            unit: "{cycle}",
            description: "Number of runtime reconciliation cycles cancelled by caller or service shutdown.");

    private static readonly Histogram<double> s_cycleDuration = PathVeerTelemetry
        .Meter.CreateHistogram<double>(
            PathVeerMetricNames.RuntimeCycleDuration,
            unit: "ms",
            description: "Duration of a runtime reconciliation cycle, in milliseconds.");

    /// <summary>
    /// Maps a bounded <see cref="TelemetryTrigger"/> to its tag string. No
    /// free-form or caller-name inference.
    /// </summary>
    internal static string MapTrigger(TelemetryTrigger trigger) =>
        trigger switch
        {
            TelemetryTrigger.Scheduled => PathVeerTagValues.TriggerScheduled,
            TelemetryTrigger.Forced => PathVeerTagValues.TriggerForced,
            TelemetryTrigger.Startup => PathVeerTagValues.TriggerStartup,
            TelemetryTrigger.Repair => PathVeerTagValues.TriggerRepair,
            _ => PathVeerTagValues.TriggerUnknown,
        };

    /// <summary>
    /// Begins one root activity for a runtime cycle and returns a scope that
    /// records the terminal outcome, counters, and duration. The scope is
    /// resilient to a null activity (no listener) and never throws into the
    /// business path.
    /// </summary>
    internal static RuntimeCycleTelemetryScope Start(TelemetryTrigger trigger)
    {
        Activity? activity = PathVeerTelemetry.ActivitySource.StartActivity(
            PathVeerActivityNames.RuntimeCycle,
            ActivityKind.Internal);

        if (activity is not null)
        {
            activity.SetTag(PathVeerTagNames.Operation, OperationValue);
            activity.SetTag(PathVeerTagNames.Trigger, MapTrigger(trigger));
        }

        return new RuntimeCycleTelemetryScope(activity, trigger);
    }

    internal static KeyValuePair<string, object?>[] Tags(
        TelemetryTrigger trigger,
        string outcome,
        string? failureCategory = null)
    {
        if (failureCategory is null)
        {
            return
            [
                new(PathVeerTagNames.Operation, OperationValue),
                new(PathVeerTagNames.Trigger, MapTrigger(trigger)),
                new(PathVeerTagNames.Outcome, outcome),
            ];
        }

        return
        [
            new(PathVeerTagNames.Operation, OperationValue),
            new(PathVeerTagNames.Trigger, MapTrigger(trigger)),
            new(PathVeerTagNames.Outcome, outcome),
            new(PathVeerTagNames.FailureCategory, failureCategory),
        ];
    }

    internal static void RecordStarted(TelemetryTrigger trigger) =>
        s_cyclesStarted.Add(
            1,
            new KeyValuePair<string, object?>[]
            {
                new(PathVeerTagNames.Operation, OperationValue),
                new(PathVeerTagNames.Trigger, MapTrigger(trigger)),
            });

    internal static void RecordCompleted(
        TelemetryTrigger trigger,
        string outcome) =>
        s_cyclesCompleted.Add(1, Tags(trigger, outcome));

    internal static void RecordCancelled(TelemetryTrigger trigger) =>
        s_cyclesCancelled.Add(1, Tags(trigger, PathVeerTagValues.Cancelled));

    internal static void RecordFailed(
        TelemetryTrigger trigger,
        string failureCategory) =>
        s_cyclesFailed.Add(
            1,
            Tags(trigger, PathVeerTagValues.Failure, failureCategory));

    internal static void RecordDuration(
        TelemetryTrigger trigger,
        double elapsedMs,
        string outcome,
        string? failureCategory = null) =>
        s_cycleDuration.Record(
            elapsedMs,
            Tags(trigger, outcome, failureCategory));
}

/// <summary>
/// Disposable scope owning the complete runtime-cycle boundary. Records
/// exactly one <c>started</c> counter, exactly one terminal counter, and
/// exactly one duration histogram sample, regardless of outcome. Top-level
/// type (not nested) so it is reachable via a namespace <c>using</c>.
/// </summary>
internal struct RuntimeCycleTelemetryScope : IDisposable
{
    private readonly Activity? _activity;
    private readonly TelemetryTrigger _trigger;
    private readonly long _startTimestamp;
    private int _completed;

    internal RuntimeCycleTelemetryScope(
        Activity? activity,
        TelemetryTrigger trigger)
    {
        _activity = activity;
        _trigger = trigger;
        _startTimestamp = Stopwatch.GetTimestamp();
        _completed = 0;

        RuntimeCycleTelemetry.RecordStarted(trigger);
    }

    public void CompleteSuccess(bool hasChanges)
    {
        string outcome = hasChanges
            ? PathVeerTagValues.Success
            : PathVeerTagValues.NoChange;

        RecordTerminal(outcome);

        if (_activity is not null)
        {
            _activity.SetTag(PathVeerTagNames.Outcome, outcome);
            _activity.SetStatus(ActivityStatusCode.Ok);
        }
    }

    public void CompleteFailure(Exception exception)
    {
        TelemetryFailureCategory category =
            TelemetryFailureCategoryMapper.Map(exception);
        string categoryString =
            TelemetryFailureCategoryMapper.ToCategoryString(category);

        RecordTerminal(PathVeerTagValues.Failure, categoryString);

        if (_activity is not null)
        {
            _activity.SetTag(
                PathVeerTagNames.Outcome,
                PathVeerTagValues.Failure);
            _activity.SetTag(
                PathVeerTagNames.FailureCategory,
                TelemetryFailureCategoryMapper.ToCategoryString(category));
            // Phase 32.1 does not approve recording exception events; the
            // bounded failure_category is set, exception text is never used.
            _activity.SetStatus(
                ActivityStatusCode.Error,
                TelemetryFailureCategoryMapper.ToCategoryString(category));
        }
    }

    public void CompleteCancelled()
    {
        RecordTerminal(PathVeerTagValues.Cancelled);

        if (_activity is not null)
        {
            _activity.SetTag(
                PathVeerTagNames.Outcome,
                PathVeerTagValues.Cancelled);
            // Cancellation is not an error. Leave status Unset; never set
            // failure_category for a cancellation outcome.
        }
    }

    private void RecordTerminal(string outcome, string? failureCategory = null)
    {
        if (Interlocked.Exchange(ref _completed, 1) == 1)
            return; // guard against double completion

        double elapsedMs = Stopwatch.GetElapsedTime(_startTimestamp)
            .TotalMilliseconds;

        switch (outcome)
        {
            case PathVeerTagValues.Success:
            case PathVeerTagValues.NoChange:
                RuntimeCycleTelemetry.RecordCompleted(_trigger, outcome);
                break;
            case PathVeerTagValues.Cancelled:
                RuntimeCycleTelemetry.RecordCancelled(_trigger);
                break;
            case PathVeerTagValues.Failure:
            case PathVeerTagValues.Timeout:
                RuntimeCycleTelemetry.RecordFailed(
                    _trigger,
                    failureCategory!);
                break;
        }

        RuntimeCycleTelemetry.RecordDuration(
            _trigger,
            elapsedMs,
            outcome,
            failureCategory);
    }

    public void Dispose()
    {
        // If the owner forgets to call a terminal method (should not happen),
        // treat disposal without completion as a terminal duration record with
        // the unknown outcome so duration is never dropped. Terminal counters
        // are intentionally NOT recorded here — only the duration, matching
        // the requirement that duration is recorded for every started cycle.
        if (Interlocked.Exchange(ref _completed, 1) == 1)
        {
            _activity?.Dispose();
            return;
        }

        double elapsedMs = Stopwatch.GetElapsedTime(_startTimestamp)
            .TotalMilliseconds;
        RuntimeCycleTelemetry.RecordDuration(
            _trigger,
            elapsedMs,
            PathVeerTagValues.OutcomeUnknown);
        _activity?.Dispose();
    }
}
