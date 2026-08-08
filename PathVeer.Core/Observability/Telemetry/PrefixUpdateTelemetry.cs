using System.Diagnostics;
using System.Diagnostics.Metrics;
using PathVeer.Core.Prefixes;

namespace PathVeer.Core.Observability.Telemetry;

/// <summary>
/// Prefix update-check telemetry: one <c>IranDirect.PrefixUpdateCheck</c>
/// root Activity per <see cref="CountryPrefixUpdateChecker.CheckAsync"/>
/// attempt, with optional <c>Prefix.HttpHead</c>, <c>Prefix.HttpGet</c>,
/// <c>Prefix.Compare</c> child Activities. Exactly one
/// <c>irandirect.prefix.checks</c> counter increment and one
/// <c>irandirect.prefix.check.duration</c> histogram sample per root attempt.
///
/// Privacy: no URL, domain, prefix, ETag, Last-Modified, file path, response
/// body, or exception message is attached. Child Activities carry only bounded
/// <c>operation</c> and <c>outcome</c> tags.
/// </summary>
public static class PrefixUpdateTelemetry
{
    private static readonly Counter<long> s_checks = IranDirectTelemetry.Meter
        .CreateCounter<long>(
            IranDirectMetricNames.PrefixChecks,
            unit: "{check}",
            description: "Prefix update-check attempts started.");

    private static readonly Histogram<double> s_duration = IranDirectTelemetry
        .Meter.CreateHistogram<double>(
            IranDirectMetricNames.PrefixCheckDuration,
            unit: "ms",
            description: "Elapsed time of one prefix update-check attempt.");

    internal static PrefixCheckScope StartCheck(
        TelemetryTrigger trigger = TelemetryTrigger.Unknown)
    {
        Activity? activity = IranDirectTelemetry.ActivitySource.StartActivity(
            IranDirectActivityNames.PrefixUpdateCheck,
            ActivityKind.Internal);

        activity?.SetTag(IranDirectTagNames.Operation,
            IranDirectTagValues.OperationPrefixUpdateCheck);
        activity?.SetTag(IranDirectTagNames.Source,
            IranDirectTagValues.SourceOfficial);
        activity?.SetTag(IranDirectTagNames.Trigger,
            ToTriggerString(trigger));

        s_checks.Add(1, new KeyValuePair<string, object?>(
            IranDirectTagNames.Operation,
            IranDirectTagValues.OperationPrefixUpdateCheck));

        return new PrefixCheckScope(activity);
    }

    private static string ToTriggerString(TelemetryTrigger trigger) =>
        trigger switch
        {
            TelemetryTrigger.Scheduled =>
                IranDirectTagValues.TriggerScheduled,
            TelemetryTrigger.Forced => IranDirectTagValues.TriggerForced,
            TelemetryTrigger.Startup => IranDirectTagValues.TriggerStartup,
            TelemetryTrigger.Repair => IranDirectTagValues.TriggerRepair,
            _ => IranDirectTagValues.TriggerUnknown,
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
                    IranDirectTagNames.Operation,
                    IranDirectTagValues.OperationPrefixUpdateCheck),
                new KeyValuePair<string, object?>(
                    IranDirectTagNames.Outcome, outcome),
                new KeyValuePair<string, object?>(
                    IranDirectTagNames.FailureCategory, failureCategory));
        }
        else
        {
            s_duration.Record(
                elapsedMs,
                new KeyValuePair<string, object?>(
                    IranDirectTagNames.Operation,
                    IranDirectTagValues.OperationPrefixUpdateCheck),
                new KeyValuePair<string, object?>(
                    IranDirectTagNames.Outcome, outcome));
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
                IranDirectActivityNames.PrefixHttpHead,
                IranDirectTagValues.OperationPrefixHttpHead);

        public PrefixChildScope StartGet() =>
            StartChild(
                IranDirectActivityNames.PrefixHttpGet,
                IranDirectTagValues.OperationPrefixHttpGet);

        public PrefixChildScope StartCompare() =>
            StartChild(
                IranDirectActivityNames.PrefixCompare,
                IranDirectTagValues.OperationPrefixCompare);

        internal PrefixChildScope StartChild(
            string name,
            string operation)
        {
            Activity? child = IranDirectTelemetry.ActivitySource
                .StartActivity(name, ActivityKind.Internal);
            child?.SetTag(IranDirectTagNames.Operation, operation);
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
                    IranDirectTagNames.Outcome, outcomeString);
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
                            IranDirectTagNames.FailureCategory,
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
                IranDirectTagNames.Outcome, IranDirectTagValues.Success);
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
                IranDirectTagNames.Outcome, IranDirectTagValues.Failure);
            _activity?.SetStatus(
                ActivityStatusCode.Error, categoryString);
            _activity?.SetTag(
                IranDirectTagNames.FailureCategory, categoryString);
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
                IranDirectTagNames.Outcome, IranDirectTagValues.OutcomeUnknown);
            _activity?.Dispose();
        }
    }
}
