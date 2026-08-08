using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Threading;

namespace PathVeer.Core.Observability.Telemetry;

/// <summary>
/// Runtime-cycle telemetry: one root <c>IranDirect.RuntimeCycle</c> activity
/// per cycle plus four counters and one duration histogram, all created once
/// against the Phase 32.2 <see cref="IranDirectTelemetry.Meter"/>.
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

    private static readonly Counter<long> s_cyclesStarted = IranDirectTelemetry
        .Meter.CreateCounter<long>(
            IranDirectMetricNames.RuntimeCyclesStarted,
            unit: "{cycle}",
            description: "Number of runtime reconciliation cycles that started.");

    private static readonly Counter<long> s_cyclesCompleted = IranDirectTelemetry
        .Meter.CreateCounter<long>(
            IranDirectMetricNames.RuntimeCyclesCompleted,
            unit: "{cycle}",
            description: "Number of runtime reconciliation cycles that completed successfully or with no changes.");

    private static readonly Counter<long> s_cyclesFailed = IranDirectTelemetry
        .Meter.CreateCounter<long>(
            IranDirectMetricNames.RuntimeCyclesFailed,
            unit: "{cycle}",
            description: "Number of runtime reconciliation cycles that failed or timed out.");

    private static readonly Counter<long> s_cyclesCancelled = IranDirectTelemetry
        .Meter.CreateCounter<long>(
            IranDirectMetricNames.RuntimeCyclesCancelled,
            unit: "{cycle}",
            description: "Number of runtime reconciliation cycles cancelled by caller or service shutdown.");

    private static readonly Histogram<double> s_cycleDuration = IranDirectTelemetry
        .Meter.CreateHistogram<double>(
            IranDirectMetricNames.RuntimeCycleDuration,
            unit: "ms",
            description: "Duration of a runtime reconciliation cycle, in milliseconds.");

    /// <summary>
    /// Maps a bounded <see cref="TelemetryTrigger"/> to its tag string. No
    /// free-form or caller-name inference.
    /// </summary>
    internal static string MapTrigger(TelemetryTrigger trigger) =>
        trigger switch
        {
            TelemetryTrigger.Scheduled => IranDirectTagValues.TriggerScheduled,
            TelemetryTrigger.Forced => IranDirectTagValues.TriggerForced,
            TelemetryTrigger.Startup => IranDirectTagValues.TriggerStartup,
            TelemetryTrigger.Repair => IranDirectTagValues.TriggerRepair,
            _ => IranDirectTagValues.TriggerUnknown,
        };

    /// <summary>
    /// Begins one root activity for a runtime cycle and returns a scope that
    /// records the terminal outcome, counters, and duration. The scope is
    /// resilient to a null activity (no listener) and never throws into the
    /// business path.
    /// </summary>
    internal static RuntimeCycleTelemetryScope Start(TelemetryTrigger trigger)
    {
        Activity? activity = IranDirectTelemetry.ActivitySource.StartActivity(
            IranDirectActivityNames.RuntimeCycle,
            ActivityKind.Internal);

        if (activity is not null)
        {
            activity.SetTag(IranDirectTagNames.Operation, OperationValue);
            activity.SetTag(IranDirectTagNames.Trigger, MapTrigger(trigger));
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
                new(IranDirectTagNames.Operation, OperationValue),
                new(IranDirectTagNames.Trigger, MapTrigger(trigger)),
                new(IranDirectTagNames.Outcome, outcome),
            ];
        }

        return
        [
            new(IranDirectTagNames.Operation, OperationValue),
            new(IranDirectTagNames.Trigger, MapTrigger(trigger)),
            new(IranDirectTagNames.Outcome, outcome),
            new(IranDirectTagNames.FailureCategory, failureCategory),
        ];
    }

    internal static void RecordStarted(TelemetryTrigger trigger) =>
        s_cyclesStarted.Add(
            1,
            new KeyValuePair<string, object?>[]
            {
                new(IranDirectTagNames.Operation, OperationValue),
                new(IranDirectTagNames.Trigger, MapTrigger(trigger)),
            });

    internal static void RecordCompleted(
        TelemetryTrigger trigger,
        string outcome) =>
        s_cyclesCompleted.Add(1, Tags(trigger, outcome));

    internal static void RecordCancelled(TelemetryTrigger trigger) =>
        s_cyclesCancelled.Add(1, Tags(trigger, IranDirectTagValues.Cancelled));

    internal static void RecordFailed(
        TelemetryTrigger trigger,
        string failureCategory) =>
        s_cyclesFailed.Add(
            1,
            Tags(trigger, IranDirectTagValues.Failure, failureCategory));

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
        RecordTerminal(IranDirectTagValues.Cancelled);

        if (_activity is not null)
        {
            _activity.SetTag(
                IranDirectTagNames.Outcome,
                IranDirectTagValues.Cancelled);
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
            case IranDirectTagValues.Success:
            case IranDirectTagValues.NoChange:
                RuntimeCycleTelemetry.RecordCompleted(_trigger, outcome);
                break;
            case IranDirectTagValues.Cancelled:
                RuntimeCycleTelemetry.RecordCancelled(_trigger);
                break;
            case IranDirectTagValues.Failure:
            case IranDirectTagValues.Timeout:
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
            IranDirectTagValues.OutcomeUnknown);
        _activity?.Dispose();
    }
}
