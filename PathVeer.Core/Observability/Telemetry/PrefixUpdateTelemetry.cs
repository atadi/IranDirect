using System.Diagnostics;
using System.Diagnostics.Metrics;
using PathVeer.Core.Prefixes;

namespace PathVeer.Core.Observability.Telemetry;

/// <summary>
/// Prefix update-check telemetry: one <c>PathVeer.PrefixUpdateCheck</c>
/// root Activity per <see cref="CountryPrefixUpdateChecker.CheckAsync"/>
/// attempt, with optional <c>Prefix.HttpHead</c>, <c>Prefix.HttpGet</c>,
/// <c>Prefix.Compare</c> child Activities. Exactly one
/// <c>pathveer.prefix.checks</c> counter increment and one
/// <c>pathveer.prefix.check.duration</c> histogram sample per root attempt.
///
/// Privacy: no URL, domain, prefix, ETag, Last-Modified, file path, response
/// body, or exception message is attached. Child Activities carry only bounded
/// <c>operation</c> and <c>outcome</c> tags.
/// </summary>
public static class PrefixUpdateTelemetry
{
    private static readonly Counter<long> s_checks = PathVeerTelemetry.Meter
        .CreateCounter<long>(
            PathVeerMetricNames.PrefixChecks,
            unit: "{check}",
            description: "Prefix update-check attempts started.");

    private static readonly Histogram<double> s_duration = PathVeerTelemetry
        .Meter.CreateHistogram<double>(
            PathVeerMetricNames.PrefixCheckDuration,
            unit: "ms",
            description: "Elapsed time of one prefix update-check attempt.");

    internal static PrefixCheckScope StartCheck(
        TelemetryTrigger trigger = TelemetryTrigger.Unknown)
    {
        Activity? activity = PathVeerTelemetry.ActivitySource.StartActivity(
            PathVeerActivityNames.PrefixUpdateCheck,
            ActivityKind.Internal);

        activity?.SetTag(PathVeerTagNames.Operation,
            PathVeerTagValues.OperationPrefixUpdateCheck);
        activity?.SetTag(PathVeerTagNames.Source,
            PathVeerTagValues.SourceOfficial);
        activity?.SetTag(PathVeerTagNames.Trigger,
            ToTriggerString(trigger));

        s_checks.Add(1, new KeyValuePair<string, object?>(
            PathVeerTagNames.Operation,
            PathVeerTagValues.OperationPrefixUpdateCheck));

        return new PrefixCheckScope(activity);
    }

    private static string ToTriggerString(TelemetryTrigger trigger) =>
        trigger switch
        {
            TelemetryTrigger.Scheduled =>
                PathVeerTagValues.TriggerScheduled,
            TelemetryTrigger.Forced => PathVeerTagValues.TriggerForced,
            TelemetryTrigger.Startup => PathVeerTagValues.TriggerStartup,
            TelemetryTrigger.Repair => PathVeerTagValues.TriggerRepair,
            _ => PathVeerTagValues.TriggerUnknown,
        };

    internal static void RecordDuration(
        double elapsedMs,
        string outcome,
        string? failureCategory)
    {
        if (failureCategory is not null)
        {
            s_duration.Record(
                elapsedMs,
                new KeyValuePair<string, object?>(
                    PathVeerTagNames.Operation,
                    PathVeerTagValues.OperationPrefixUpdateCheck),
                new KeyValuePair<string, object?>(
                    PathVeerTagNames.Outcome, outcome),
                new KeyValuePair<string, object?>(
                    PathVeerTagNames.FailureCategory, failureCategory));
        }
        else
        {
            s_duration.Record(
                elapsedMs,
                new KeyValuePair<string, object?>(
                    PathVeerTagNames.Operation,
                    PathVeerTagValues.OperationPrefixUpdateCheck),
                new KeyValuePair<string, object?>(
                    PathVeerTagNames.Outcome, outcome));
        }
    }

    /// <summary>
    /// Root scope for one prefix update-check attempt.
    /// </summary>
    public sealed class PrefixCheckScope : IDisposable
    {
        private readonly Activity? _activity;
        private long _completed;

        internal PrefixCheckScope(Activity? activity)
        {
            _activity = activity;
        }

        public PrefixChildScope StartHead() =>
            StartChild(
                PathVeerActivityNames.PrefixHttpHead,
                PathVeerTagValues.OperationPrefixHttpHead);

        public PrefixChildScope StartGet() =>
            StartChild(
                PathVeerActivityNames.PrefixHttpGet,
                PathVeerTagValues.OperationPrefixHttpGet);

        public PrefixChildScope StartCompare() =>
            StartChild(
                PathVeerActivityNames.PrefixCompare,
                PathVeerTagValues.OperationPrefixCompare);

        internal PrefixChildScope StartChild(
            string name,
            string operation)
        {
            Activity? child = PathVeerTelemetry.ActivitySource
                .StartActivity(name, ActivityKind.Internal);
            child?.SetTag(PathVeerTagNames.Operation, operation);
            return new PrefixChildScope(child);
        }

        public void Complete(PrefixUpdateCheckStatus status)
        {
            (TelemetryOutcome outcome, string? failureCategory) =
                Map(status);
            CompleteOutcome(outcome, failureCategory);
        }

        public void CompleteFailure(Exception exception)
        {
            TelemetryFailureCategory category =
                TelemetryFailureCategoryMapper.Map(exception);
            CompleteOutcome(
                TelemetryOutcome.Failure,
                TelemetryFailureCategoryMapper.ToCategoryString(category));
        }

        public void CompleteCancelled()
        {
            CompleteOutcome(TelemetryOutcome.Cancelled, null);
        }

        private void CompleteOutcome(
            TelemetryOutcome outcome,
            string? failureCategory)
        {
            if (Interlocked.Exchange(ref _completed, 1) == 1)
            {
                return;
            }

            double elapsedMs = Stopwatch.GetElapsedTime(
                _startTimestamp).TotalMilliseconds;
            string outcomeString =
                TelemetryOutcomeMapper.ToOutcomeString(outcome);

            if (_activity is not null)
            {
                _activity.SetTag(
                    PathVeerTagNames.Outcome, outcomeString);
                if (outcome == TelemetryOutcome.Cancelled)
                {
                    // Cancellation is not an error: leave status Unset and
                    // omit the failure_category tag.
                }
                else if (outcome == TelemetryOutcome.Failure)
                {
                    _activity.SetStatus(ActivityStatusCode.Error);
                    if (failureCategory is not null)
                    {
                        _activity.SetTag(
                            PathVeerTagNames.FailureCategory,
                            failureCategory);
                    }
                }
                else
                {
                    _activity.SetStatus(ActivityStatusCode.Ok);
                }
            }

            RecordDuration(elapsedMs, outcomeString, failureCategory);
        }

        private static (TelemetryOutcome, string?) Map(
            PrefixUpdateCheckStatus status) => status switch
        {
            // UpdateAvailable is a successful check that found an update;
            // the 32.1 contract defines no update_available outcome, so it
            // maps to success (spec 32.7 section 11).
            PrefixUpdateCheckStatus.Current =>
                (TelemetryOutcome.Success, null),
            PrefixUpdateCheckStatus.UpdateAvailable =>
                (TelemetryOutcome.Success, null),
            PrefixUpdateCheckStatus.Unknown =>
                (TelemetryOutcome.Unknown, null),
            PrefixUpdateCheckStatus.Failed =>
                // The check swallows exceptions into a Failed status; without
                // a concrete exception there is no mappable failure category.
                (TelemetryOutcome.Failure, null),
            _ => (TelemetryOutcome.Unknown, null),
        };

        private readonly long _startTimestamp =
            Stopwatch.GetTimestamp();

        public void Dispose()
        {
            // Safety net: if a terminal call was forgotten, classify as
            // unknown rather than a false success.
            CompleteOutcome(TelemetryOutcome.Unknown, null);
            _activity?.Dispose();
        }
    }

    /// <summary>
    /// Child scope for one prefix sub-operation (HEAD/GET/Compare). Carries
    /// only bounded <c>operation</c> and <c>outcome</c> tags; no counter or
    /// histogram of its own.
    /// </summary>
    public sealed class PrefixChildScope : IDisposable
    {
        private readonly Activity? _activity;
        private long _completed;

        internal PrefixChildScope(Activity? activity)
        {
            _activity = activity;
        }

        public void CompleteSuccess()
        {
            if (Interlocked.Exchange(ref _completed, 1) == 1)
            {
                return;
            }

            _activity?.SetTag(
                PathVeerTagNames.Outcome, PathVeerTagValues.Success);
            _activity?.SetStatus(ActivityStatusCode.Ok);
        }

        public void CompleteFailure(Exception exception)
        {
            if (Interlocked.Exchange(ref _completed, 1) == 1)
            {
                return;
            }

            TelemetryFailureCategory category =
                TelemetryFailureCategoryMapper.Map(exception);
            string categoryString =
                TelemetryFailureCategoryMapper.ToCategoryString(category);

            _activity?.SetTag(
                PathVeerTagNames.Outcome, PathVeerTagValues.Failure);
            _activity?.SetStatus(
                ActivityStatusCode.Error, categoryString);
            _activity?.SetTag(
                PathVeerTagNames.FailureCategory, categoryString);
        }

        public void Dispose()
        {
            // Safety net: a child that ended without explicit completion
            // attempted but did not report success -> unknown (not success).
            if (Interlocked.Exchange(ref _completed, 1) == 1)
            {
                _activity?.Dispose();
                return;
            }

            _activity?.SetTag(
                PathVeerTagNames.Outcome, PathVeerTagValues.OutcomeUnknown);
            _activity?.Dispose();
        }
    }
}
