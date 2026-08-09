using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace PathVeer.Core.Observability.Telemetry;

/// <summary>
/// Custom-route DNS refresh telemetry: one <c>PathVeer.CustomRouteRefresh</c>
/// root Activity per <see cref="CustomRouteResolver.ResolveAsync"/> invocation,
/// with <c>Dns.CacheRead</c>, <c>Dns.Resolve</c>, and <c>Dns.CacheWrite</c>
/// child Activities. One <c>pathveer.dns.lookups</c> counter increment and one
/// <c>pathveer.dns.lookup.duration</c> histogram sample per real DNS lookup
/// attempt (fresh-cache hits do not increment the counter). No per-address
/// spans and no domain/IP/prefix tags anywhere.
/// </summary>
public static class CustomRouteRefreshTelemetry
{
    private static readonly Counter<long> s_lookups = PathVeerTelemetry
        .Meter.CreateCounter<long>(
            PathVeerMetricNames.DnsLookups,
            unit: "{lookup}",
            description: "Real DNS lookup attempts during custom-route refresh.");

    private static readonly Histogram<double> s_lookupDuration =
        PathVeerTelemetry.Meter.CreateHistogram<double>(
            PathVeerMetricNames.DnsLookupDuration,
            unit: "ms",
            description: "Elapsed time of one real DNS lookup.");

    internal static CustomRouteRefreshScope StartRefresh(
        TelemetryTrigger trigger = TelemetryTrigger.Unknown)
    {
        Activity? activity = PathVeerTelemetry.ActivitySource.StartActivity(
            PathVeerActivityNames.CustomRouteRefresh,
            ActivityKind.Internal);

        activity?.SetTag(
            PathVeerTagNames.Operation,
            PathVeerTagValues.OperationCustomRouteRefresh);

        return new CustomRouteRefreshScope(activity);
    }

    internal static void RecordLookup(
        double elapsedMs,
        string outcome,
        string? failureCategory)
    {
        if (failureCategory is not null)
        {
            s_lookups.Add(1, new KeyValuePair<string, object?>(
                PathVeerTagNames.Outcome, outcome));
            s_lookupDuration.Record(
                elapsedMs,
                new KeyValuePair<string, object?>(
                    PathVeerTagNames.Outcome, outcome),
                new KeyValuePair<string, object?>(
                    PathVeerTagNames.FailureCategory, failureCategory));
        }
        else
        {
            s_lookups.Add(1, new KeyValuePair<string, object?>(
                PathVeerTagNames.Outcome, outcome));
            s_lookupDuration.Record(
                elapsedMs,
                new KeyValuePair<string, object?>(
                    PathVeerTagNames.Outcome, outcome));
        }
    }

    /// <summary>
    /// Root scope for one custom-route refresh.
    /// </summary>
    public sealed class CustomRouteRefreshScope : IDisposable
    {
        private readonly Activity? _activity;
        private long _completed;

        internal CustomRouteRefreshScope(Activity? activity)
        {
            _activity = activity;
        }

        public DnsChildScope StartCacheRead() =>
            StartChild(
                PathVeerActivityNames.DnsCacheRead,
                PathVeerTagValues.OperationDnsCacheRead,
                PathVeerTagValues.SourceCache);

        public DnsChildScope StartResolve() =>
            StartChild(
                PathVeerActivityNames.DnsResolve,
                PathVeerTagValues.OperationDnsResolve,
                PathVeerTagValues.SourceCustom);

        public DnsChildScope StartCacheWrite() =>
            StartChild(
                PathVeerActivityNames.DnsCacheWrite,
                PathVeerTagValues.OperationDnsCacheWrite,
                PathVeerTagValues.SourceCache);

        internal DnsChildScope StartChild(
            string name,
            string operation,
            string source)
        {
            Activity? child = PathVeerTelemetry.ActivitySource
                .StartActivity(name, ActivityKind.Internal);
            child?.SetTag(PathVeerTagNames.Operation, operation);
            child?.SetTag(PathVeerTagNames.Source, source);
            return new DnsChildScope(child);
        }

        public void Complete(bool allSucceeded)
        {
            CompleteOutcome(
                allSucceeded
                    ? TelemetryOutcome.Success
                    : TelemetryOutcome.Failure,
                allSucceeded ? null : PathVeerTagValues.FailureDns);
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

            string outcomeString =
                TelemetryOutcomeMapper.ToOutcomeString(outcome);

            if (_activity is not null)
            {
                _activity.SetTag(
                    PathVeerTagNames.Outcome, outcomeString);
                if (outcome == TelemetryOutcome.Failure)
                {
                    _activity.SetStatus(ActivityStatusCode.Error);
                    if (failureCategory is not null)
                    {
                        _activity.SetTag(
                            PathVeerTagNames.FailureCategory,
                            failureCategory);
                    }
                }
                else if (outcome != TelemetryOutcome.Cancelled)
                {
                    _activity.SetStatus(ActivityStatusCode.Ok);
                }
            }
        }

        public void Dispose()
        {
            CompleteOutcome(TelemetryOutcome.Unknown, null);
            _activity?.Dispose();
        }
    }

    /// <summary>
    /// Child scope for one DNS sub-operation (cache read / resolve / cache
    /// write). For a real DNS lookup the <see cref="DnsChildScope.Complete"/>
    /// call also records the <c>dns.lookups</c> counter and
    /// <c>dns.lookup.duration</c> histogram (one per real lookup).
    /// </summary>
    public sealed class DnsChildScope : IDisposable
    {
        private readonly Activity? _activity;
        private long _completed;
        private readonly long _startTimestamp = Stopwatch.GetTimestamp();

        internal DnsChildScope(Activity? activity)
        {
            _activity = activity;
        }

        public void SetCacheState(string cacheState)
        {
            _activity?.SetTag(
                PathVeerTagNames.CacheState, cacheState);
        }

        public void CompleteSuccess() =>
            Complete(TelemetryOutcome.Success, null);

        public void CompleteLookupSuccess() =>
            Complete(TelemetryOutcome.Success, null, isLookup: true);

        public void CompleteFailure(Exception exception) =>
            Complete(
                TelemetryOutcome.Failure,
                TelemetryFailureCategoryMapper.ToCategoryString(
                    TelemetryFailureCategoryMapper.Map(exception)));

        public void CompleteLookupFailure(Exception exception)
        {
            TelemetryFailureCategory category =
                TelemetryFailureCategoryMapper.Map(exception);
            Complete(
                TelemetryOutcome.Failure,
                TelemetryFailureCategoryMapper.ToCategoryString(category),
                isLookup: true);
        }

        /// <summary>
        /// Completes the child. When <paramref name="isLookup"/> is true this
        /// also increments <c>dns.lookups</c> and records
        /// <c>dns.lookup.duration</c> (one measurement per real DNS lookup).
        /// </summary>
        public void Complete(
            TelemetryOutcome outcome,
            string? failureCategory,
            bool isLookup = false)
        {
            if (Interlocked.Exchange(ref _completed, 1) == 1)
            {
                return;
            }

            string outcomeString =
                TelemetryOutcomeMapper.ToOutcomeString(outcome);

            if (_activity is not null)
            {
                _activity.SetTag(
                    PathVeerTagNames.Outcome, outcomeString);
                if (outcome == TelemetryOutcome.Failure)
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

            if (isLookup)
            {
                double elapsedMs = Stopwatch.GetElapsedTime(
                    _startTimestamp).TotalMilliseconds;
                CustomRouteRefreshTelemetry.RecordLookup(
                    elapsedMs, outcomeString, failureCategory);
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _completed, 1) == 1)
            {
                _activity?.Dispose();
                return;
            }

            _activity?.SetTag(
                PathVeerTagNames.Outcome,
                PathVeerTagValues.OutcomeUnknown);
            _activity?.Dispose();
        }
    }
}
