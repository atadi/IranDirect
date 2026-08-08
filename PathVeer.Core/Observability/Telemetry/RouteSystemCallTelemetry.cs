using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Threading;

namespace PathVeer.Core.Observability.Telemetry;

/// <summary>
/// Route system-call telemetry: one <see cref="Activity"/> per native Windows
/// route API call (enumerate / create / delete), an approved set of
/// requested/succeeded/failed operation counters, and one system-call duration
/// histogram — all created exactly once against the Phase 32.2
/// <see cref="IranDirectTelemetry.Meter"/>.
///
/// This instruments the narrowest native boundary — the process submission to
/// powershell.exe (enumerate) and netsh.exe (create/delete) inside
/// <c>WindowsRouteApi</c>, exposed to the rest of the system through the
/// <see cref="TelemetryRouteApi"/> decorator. It does NOT instrument
/// <c>WindowsRouteManager</c> orchestration, batching loops, the
/// <c>RuntimeExecutor</c>, or individual <c>RuntimeExecutionStep</c> objects.
///
/// One native batch submission (regardless of route count) produces exactly
/// one Activity, one requested counter increment, one terminal counter
/// increment, and one duration measurement. A 50K-route batch emits the same
/// single set of measurements as a one-route batch.
///
/// Telemetry is failure-isolated: a null <see cref="ActivitySource.StartActivity"/>
/// result, a missing listener, or a failed measurement can never alter route
/// behavior. The native exception/timeout/cancellation contract is preserved
/// exactly — exceptions caught only to complete telemetry are rethrown
/// unchanged.
/// </summary>
public static class RouteSystemCallTelemetry
{
    /// <summary>
    /// Bounded kind of a route batch as known at the native boundary. The
    /// <c>WindowsRouteApi</c> boundary receives <see cref="ManagedRoute"/>
    /// records that carry no kind discriminator, so the entire-batch kind
    /// cannot be authoritatively determined there; <see cref="Unknown"/> is the
    /// honest default rather than inspecting individual routes to infer one.
    /// </summary>
    internal enum RouteSystemCallKind
    {
        Unknown = 0,
        Prefix = 1,
        Endpoint = 2,
    }

    private static readonly Counter<long> s_requested = IranDirectTelemetry
        .Meter.CreateCounter<long>(
            IranDirectMetricNames.RoutesOperationsRequested,
            unit: "{operation}",
            description:
                "Number of native route system calls requested " +
                "(enumerate/create/delete), one increment per native call.");

    private static readonly Counter<long> s_succeeded = IranDirectTelemetry
        .Meter.CreateCounter<long>(
            IranDirectMetricNames.RoutesOperationsSucceeded,
            unit: "{operation}",
            description:
                "Number of native route system calls that completed " +
                "successfully or changed nothing.");

    private static readonly Counter<long> s_failed = IranDirectTelemetry
        .Meter.CreateCounter<long>(
            IranDirectMetricNames.RoutesOperationsFailed,
            unit: "{operation}",
            description:
                "Number of native route system calls that failed or " +
                "timed out.");

    private static readonly Histogram<double> s_duration = IranDirectTelemetry
        .Meter.CreateHistogram<double>(
            IranDirectMetricNames.RoutesSystemCallDuration,
            unit: "ms",
            description:
                "Elapsed time of a single native route system call, " +
                "including process setup, execution, output parsing, " +
                "and command-result validation.");

    /// <summary>
    /// Begins one enumeration child activity. Empty enumeration results are
    /// still a successful native call and are instrumented normally.
    /// </summary>
    internal static RouteSystemCallTelemetryScope StartEnumeration() =>
        Start(
            IranDirectActivityNames.RoutesEnumerate,
            IranDirectTagValues.OperationEnumerateRoutes,
            changeKind: null,
            routeKind: null);

    /// <summary>
    /// Begins one create child activity for a native batch submission.
    /// </summary>
    internal static RouteSystemCallTelemetryScope StartCreate(
        RouteSystemCallKind routeKind) =>
        Start(
            IranDirectActivityNames.RoutesCreate,
            IranDirectTagValues.OperationCreateRoutes,
            IranDirectTagValues.ChangeKindCreate,
            ToRouteKindValue(routeKind));

    /// <summary>
    /// Begins one delete child activity for a native batch submission.
    /// </summary>
    internal static RouteSystemCallTelemetryScope StartDelete(
        RouteSystemCallKind routeKind) =>
        Start(
            IranDirectActivityNames.RoutesDelete,
            IranDirectTagValues.OperationDeleteRoutes,
            IranDirectTagValues.ChangeKindDelete,
            ToRouteKindValue(routeKind));

    private static RouteSystemCallTelemetryScope Start(
        string activityName,
        string operation,
        string? changeKind,
        string? routeKind)
    {
        Activity? activity = IranDirectTelemetry.ActivitySource.StartActivity(
            activityName,
            ActivityKind.Internal);

        if (activity is not null)
        {
            activity.SetTag(IranDirectTagNames.Operation, operation);
            if (changeKind is not null)
                activity.SetTag(IranDirectTagNames.ChangeKind, changeKind);
            if (routeKind is not null)
                activity.SetTag(IranDirectTagNames.RouteKind, routeKind);
        }

        // requested is recorded at start; it carries operation (and the
        // bounded change/route kinds where applicable) but never outcome or
        // failure_category — those are only known at completion.
        s_requested.Add(1, RequestedTags(operation, changeKind, routeKind));

        return new RouteSystemCallTelemetryScope(
            activity, operation, changeKind, routeKind);
    }

    private static KeyValuePair<string, object?>[] RequestedTags(
        string operation,
        string? changeKind,
        string? routeKind)
    {
        if (changeKind is null && routeKind is null)
            return [new(IranDirectTagNames.Operation, operation)];

        if (routeKind is null)
            return
            [
                new(IranDirectTagNames.Operation, operation),
                new(IranDirectTagNames.ChangeKind, changeKind!),
            ];

        if (changeKind is null)
            return
            [
                new(IranDirectTagNames.Operation, operation),
                new(IranDirectTagNames.RouteKind, routeKind),
            ];

        return
        [
            new(IranDirectTagNames.Operation, operation),
            new(IranDirectTagNames.ChangeKind, changeKind),
            new(IranDirectTagNames.RouteKind, routeKind),
        ];
    }

    private static KeyValuePair<string, object?>[] TerminalTags(
        string operation,
        string outcome,
        string? changeKind,
        string? routeKind,
        string? failureCategory)
    {
        if (failureCategory is null)
        {
            if (changeKind is null && routeKind is null)
                return
                [
                    new(IranDirectTagNames.Operation, operation),
                    new(IranDirectTagNames.Outcome, outcome),
                ];

            if (routeKind is null)
                return
                [
                    new(IranDirectTagNames.Operation, operation),
                    new(IranDirectTagNames.Outcome, outcome),
                    new(IranDirectTagNames.ChangeKind, changeKind!),
                ];

            if (changeKind is null)
                return
                [
                    new(IranDirectTagNames.Operation, operation),
                    new(IranDirectTagNames.Outcome, outcome),
                    new(IranDirectTagNames.RouteKind, routeKind),
                ];

            return
            [
                new(IranDirectTagNames.Operation, operation),
                new(IranDirectTagNames.Outcome, outcome),
                new(IranDirectTagNames.ChangeKind, changeKind),
                new(IranDirectTagNames.RouteKind, routeKind),
            ];
        }

        // failure/timeout carry a bounded failure_category.
        if (changeKind is null && routeKind is null)
            return
            [
                new(IranDirectTagNames.Operation, operation),
                new(IranDirectTagNames.Outcome, outcome),
                new(IranDirectTagNames.FailureCategory, failureCategory),
            ];

        if (routeKind is null)
            return
            [
                new(IranDirectTagNames.Operation, operation),
                new(IranDirectTagNames.Outcome, outcome),
                new(IranDirectTagNames.ChangeKind, changeKind!),
                new(IranDirectTagNames.FailureCategory, failureCategory),
            ];

        if (changeKind is null)
            return
            [
                new(IranDirectTagNames.Operation, operation),
                new(IranDirectTagNames.Outcome, outcome),
                new(IranDirectTagNames.RouteKind, routeKind),
                new(IranDirectTagNames.FailureCategory, failureCategory),
            ];

        return
        [
            new(IranDirectTagNames.Operation, operation),
            new(IranDirectTagNames.Outcome, outcome),
            new(IranDirectTagNames.ChangeKind, changeKind),
            new(IranDirectTagNames.RouteKind, routeKind),
            new(IranDirectTagNames.FailureCategory, failureCategory),
        ];
    }

    private static string ToRouteKindValue(RouteSystemCallKind kind) =>
        kind switch
        {
            RouteSystemCallKind.Prefix => IranDirectTagValues.RouteKindPrefix,
            RouteSystemCallKind.Endpoint =>
                IranDirectTagValues.RouteKindEndpoint,
            _ => IranDirectTagValues.RouteKindUnknown,
        };

    internal static void RecordDuration(
        double elapsedMs,
        string operation,
        string outcome,
        string? changeKind,
        string? routeKind,
        string? failureCategory)
    {
        s_duration.Record(
            elapsedMs,
            TerminalTags(operation, outcome, changeKind, routeKind,
                failureCategory));
    }

    internal static void RecordSucceeded(
        string operation,
        string outcome,
        string? changeKind,
        string? routeKind)
    {
        s_succeeded.Add(
            1,
            TerminalTags(operation, outcome, changeKind, routeKind, null));
    }

    internal static void RecordFailed(
        string operation,
        string outcome,
        string? changeKind,
        string? routeKind,
        string? failureCategory)
    {
        s_failed.Add(
            1,
            TerminalTags(operation, outcome, changeKind, routeKind,
                failureCategory));
    }
}

/// <summary>
/// Disposable scope owning one complete native route system call. Records
/// exactly one duration sample and exactly one terminal counter increment
/// (succeeded/failed/none on cancellation), regardless of outcome. Top-level
/// type (not nested) so it is reachable via a namespace <c>using</c>.
/// </summary>
internal sealed class RouteSystemCallTelemetryScope : IDisposable
{
    private readonly Activity? _activity;
    private readonly string _operation;
    private readonly string? _changeKind;
    private readonly string? _routeKind;
    private readonly long _startTimestamp;
    private int _completed;

    internal RouteSystemCallTelemetryScope(
        Activity? activity,
        string operation,
        string? changeKind,
        string? routeKind)
    {
        _activity = activity;
        _operation = operation;
        _changeKind = changeKind;
        _routeKind = routeKind;
        _startTimestamp = Stopwatch.GetTimestamp();
        _completed = 0;
    }

    public void CompleteSuccess()
    {
        if (Interlocked.Exchange(ref _completed, 1) == 1)
            return;

        double elapsedMs =
            Stopwatch.GetElapsedTime(_startTimestamp).TotalMilliseconds;
        RecordTerminal(
            elapsedMs, _operation, _changeKind, _routeKind,
            IranDirectTagValues.Success, failureCategory: null,
            countAsSuccess: true);

        if (_activity is not null)
        {
            _activity.SetTag(IranDirectTagNames.Outcome, IranDirectTagValues.Success);
            _activity.SetStatus(ActivityStatusCode.Ok);
        }
    }

    public void CompleteNoChange()
    {
        if (Interlocked.Exchange(ref _completed, 1) == 1)
            return;

        double elapsedMs =
            Stopwatch.GetElapsedTime(_startTimestamp).TotalMilliseconds;
        RecordTerminal(
            elapsedMs, _operation, _changeKind, _routeKind,
            IranDirectTagValues.NoChange, failureCategory: null,
            countAsSuccess: true);

        if (_activity is not null)
        {
            _activity.SetTag(IranDirectTagNames.Outcome, IranDirectTagValues.NoChange);
            _activity.SetStatus(ActivityStatusCode.Ok);
        }
    }

    public void CompleteFailure(Exception exception)
    {
        if (Interlocked.Exchange(ref _completed, 1) == 1)
            return;

        double elapsedMs =
            Stopwatch.GetElapsedTime(_startTimestamp).TotalMilliseconds;
        TelemetryFailureCategory category =
            TelemetryFailureCategoryMapper.Map(exception);
        string categoryString =
            TelemetryFailureCategoryMapper.ToCategoryString(category);
        string outcome = category == TelemetryFailureCategory.Timeout
            ? IranDirectTagValues.Timeout
            : IranDirectTagValues.Failure;

        RecordTerminal(
            elapsedMs, _operation, _changeKind, _routeKind,
            outcome, categoryString, countAsSuccess: false);

        if (_activity is not null)
        {
            _activity.SetTag(IranDirectTagNames.Outcome, outcome);
            _activity.SetTag(
                IranDirectTagNames.FailureCategory, categoryString);
            _activity.SetStatus(ActivityStatusCode.Error, categoryString);
        }
    }

    public void CompleteCancelled()
    {
        if (Interlocked.Exchange(ref _completed, 1) == 1)
            return;

        double elapsedMs =
            Stopwatch.GetElapsedTime(_startTimestamp).TotalMilliseconds;
        RecordTerminal(
            elapsedMs, _operation, _changeKind, _routeKind,
            IranDirectTagValues.Cancelled, failureCategory: null,
            countAsSuccess: false);

        if (_activity is not null)
        {
            _activity.SetTag(
                IranDirectTagNames.Outcome, IranDirectTagValues.Cancelled);
            // Cancellation is not an error: leave status Unset and omit the
            // failure_category tag entirely.
        }
    }

    private static void RecordTerminal(
        double elapsedMs,
        string operation,
        string? changeKind,
        string? routeKind,
        string outcome,
        string? failureCategory,
        bool countAsSuccess)
    {
        RouteSystemCallTelemetry.RecordDuration(
            elapsedMs, operation, outcome, changeKind, routeKind,
            failureCategory);
        if (countAsSuccess)
        {
            RouteSystemCallTelemetry.RecordSucceeded(
                operation, outcome, changeKind, routeKind);
        }
        else if (failureCategory is not null ||
                 outcome == IranDirectTagValues.Failure)
        {
            RouteSystemCallTelemetry.RecordFailed(
                operation, outcome, changeKind, routeKind, failureCategory);
        }
        // Cancellation: neither succeeded nor failed is recorded.
    }

    public void Dispose()
    {
        // If the owner forgets a terminal call, treat disposal as a terminal
        // success duration so the measurement is not dropped. Terminal
        // counters/samples other than duration are intentionally NOT recorded
        // here to avoid double-counting.
        if (Interlocked.Exchange(ref _completed, 1) == 1)
        {
            _activity?.Dispose();
            return;
        }

        double elapsedMs =
            Stopwatch.GetElapsedTime(_startTimestamp).TotalMilliseconds;
        RouteSystemCallTelemetry.RecordDuration(
            elapsedMs, _operation, IranDirectTagValues.Success, _changeKind,
            _routeKind, failureCategory: null);
        _activity?.Dispose();
    }
}
