using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace PathVeer.Core.Observability.Telemetry;

/// <summary>
/// Support export telemetry: one <c>PathVeer.SupportBundleExport</c> root
/// Activity per user-visible support export invocation, with
/// <c>Support.CaptureSnapshot</c>, <c>Support.Serialize</c>,
/// <c>Support.WriteJson</c>, and <c>Support.CreateZip</c> child Activities.
///
/// Exactly one <c>pathveer.support.bundles.exported</c> /
/// <c>pathveer.support.bundles.failed</c> counter increment and one
/// <c>pathveer.support.bundle.duration</c> histogram sample per export.
///
/// The JSON snapshot export and the ZIP bundle export are both user-visible
/// workflows; they are distinguished by the bounded <c>operation</c> tag
/// (<c>support_snapshot_export</c> vs <c>support_bundle_export</c>) and share
/// the single approved root Activity name. A bundle export internally invokes
/// the snapshot exporter through the explicit <c>ExportWithinBundleAsync</c>
/// nested path, which attaches its children to the enclosing bundle root
/// without starting a second root or recording a second terminal metric. The
/// decision is explicit (the nested path passes <c>createRootTelemetry:
/// false</c>), never derived from <c>Activity.Current</c>; sampling or disabled
/// listeners therefore cannot change orchestration or metric counts.
///
/// Privacy: no output path, filename, temporary path, ZIP entry name, payload
/// size, or support contents is attached. Only bounded <c>operation</c>,
/// <c>outcome</c>, and <c>failure_category</c> (on failure) tags appear.
/// </summary>
public static class SupportExportTelemetry
{
    private static readonly Counter<long> s_exported = PathVeerTelemetry
        .Meter.CreateCounter<long>(
            PathVeerMetricNames.SupportBundlesExported,
            unit: "{export}",
            description: "Support bundle/snapshot exports completed.");

    private static readonly Counter<long> s_failed = PathVeerTelemetry
        .Meter.CreateCounter<long>(
            PathVeerMetricNames.SupportBundlesFailed,
            unit: "{export}",
            description: "Support bundle/snapshot exports that failed.");

    private static readonly Histogram<double> s_duration = PathVeerTelemetry
        .Meter.CreateHistogram<double>(
            PathVeerMetricNames.SupportBundleDuration,
            unit: "ms",
            description: "Elapsed time of one support export.");

    /// <summary>
    /// Starts an owned support export root for a standalone snapshot or bundle
    /// export. Creates the <c>PathVeer.SupportBundleExport</c> root, records
    /// the terminal <c>exported</c>/<c>failed</c> counter and
    /// <c>duration</c> histogram on completion, and owns the Activity lifetime.
    /// </summary>
    internal static SupportExportScope Start(string operation)
    {
        Activity? activity = PathVeerTelemetry.ActivitySource.StartActivity(
            PathVeerActivityNames.SupportBundleExport,
            ActivityKind.Internal);
        activity?.SetTag(PathVeerTagNames.Operation, operation);

        return new SupportExportScope(activity, operation, ownsRoot: true);
    }

    /// <summary>
    /// Starts a non-root support export scope for a snapshot export driven by an
    /// enclosing bundle export. No root Activity is created and no terminal
    /// metric is recorded here; the children attach to the enclosing bundle
    /// root. The caller must already hold a root scope created via
    /// <see cref="Start"/>. This is the explicit nested path used by
    /// <c>SupportBundleExporter</c> — it never inspects
    /// <c>Activity.Current</c>.
    /// </summary>
    internal static SupportExportScope StartNested(string operation)
    {
        return new SupportExportScope(activity: null, operation, ownsRoot: false);
    }

    private static void RecordDuration(
        double elapsedMs,
        string outcome,
        string? failureCategory)
    {
        if (failureCategory is not null)
        {
            s_duration.Record(
                elapsedMs,
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
                    PathVeerTagNames.Outcome, outcome));
        }
    }

    /// <summary>
    /// Root scope for one support export invocation.
    /// </summary>
    public sealed class SupportExportScope : IDisposable
    {
        private readonly Activity? _activity;
        private readonly string _operation;
        private readonly bool _ownsRoot;
        private long _completed;

        internal SupportExportScope(
            Activity? activity,
            string operation,
            bool ownsRoot)
        {
            _activity = activity;
            _operation = operation;
            _ownsRoot = ownsRoot;
        }

        public SupportChildScope StartCaptureSnapshot() =>
            StartChild(
                PathVeerActivityNames.SupportCaptureSnapshot,
                PathVeerTagValues.OperationSupportCaptureSnapshot);

        public SupportChildScope StartSerialize() =>
            StartChild(
                PathVeerActivityNames.SupportSerialize,
                PathVeerTagValues.OperationSupportSerialize);

        public SupportChildScope StartWriteJson() =>
            StartChild(
                PathVeerActivityNames.SupportWriteJson,
                PathVeerTagValues.OperationSupportWriteJson);

        public SupportChildScope StartCreateZip() =>
            StartChild(
                PathVeerActivityNames.SupportCreateZip,
                PathVeerTagValues.OperationSupportCreateZip);

        internal SupportChildScope StartChild(
            string name,
            string operation)
        {
            // Children auto-parent to Activity.Current (the enclosing root when
            // nested, or the JSON root otherwise).
            Activity? child = PathVeerTelemetry.ActivitySource
                .StartActivity(name, ActivityKind.Internal);
            child?.SetTag(PathVeerTagNames.Operation, operation);
            return new SupportChildScope(child);
        }

        public void CompleteSuccess()
        {
            if (Interlocked.Exchange(ref _completed, 1) == 1)
            {
                return;
            }

            if (_ownsRoot)
            {
                _activity?.SetTag(
                    PathVeerTagNames.Outcome, PathVeerTagValues.Success);
                _activity?.SetStatus(ActivityStatusCode.Ok);
                s_exported.Add(1,
                    new KeyValuePair<string, object?>(
                        PathVeerTagNames.Operation, _operation));
                RecordDuration(
                    Stopwatch.GetElapsedTime(_startTimestamp)
                        .TotalMilliseconds,
                    PathVeerTagValues.Success,
                    null);
            }
        }

        public void CompleteFailure(Exception exception)
        {
            if (Interlocked.Exchange(ref _completed, 1) == 1)
            {
                return;
            }

            if (!_ownsRoot)
            {
                return;
            }

            string category = TelemetryFailureCategoryMapper
                .ToCategoryString(
                    TelemetryFailureCategoryMapper.Map(exception));

            _activity?.SetTag(
                PathVeerTagNames.Outcome, PathVeerTagValues.Failure);
            _activity?.SetStatus(ActivityStatusCode.Error);
            _activity?.SetTag(
                PathVeerTagNames.FailureCategory, category);
            s_failed.Add(1,
                new KeyValuePair<string, object?>(
                    PathVeerTagNames.Operation, _operation),
                new KeyValuePair<string, object?>(
                    PathVeerTagNames.FailureCategory, category));
            RecordDuration(
                Stopwatch.GetElapsedTime(_startTimestamp)
                    .TotalMilliseconds,
                PathVeerTagValues.Failure,
                category);
        }

        public void CompleteCancelled()
        {
            if (Interlocked.Exchange(ref _completed, 1) == 1)
            {
                return;
            }

            if (!_ownsRoot)
            {
                return;
            }

            // Cancellation must not increment failed (Phase 32.1 contract);
            // the duration is still recorded exactly once for the started
            // export.
            _activity?.SetTag(
                PathVeerTagNames.Outcome,
                PathVeerTagValues.Cancelled);
            RecordDuration(
                Stopwatch.GetElapsedTime(_startTimestamp)
                    .TotalMilliseconds,
                PathVeerTagValues.Cancelled,
                null);
        }

        private readonly long _startTimestamp = Stopwatch.GetTimestamp();

        public void Dispose()
        {
            if (!_ownsRoot)
            {
                // Nested inside a bundle export: the enclosing root owns
                // outcome, status, and metrics. Do not touch it.
                return;
            }

            if (Interlocked.Exchange(ref _completed, 1) == 1)
            {
                _activity?.Dispose();
                return;
            }

            // Safety net: a forgotten terminal call is an unknown outcome.
            _activity?.SetTag(
                PathVeerTagNames.Outcome,
                PathVeerTagValues.OutcomeUnknown);
            RecordDuration(
                Stopwatch.GetElapsedTime(_startTimestamp)
                    .TotalMilliseconds,
                PathVeerTagValues.OutcomeUnknown,
                null);
            _activity?.Dispose();
        }
    }

    /// <summary>
    /// Child scope for one support sub-operation (capture / serialize / write /
    /// zip). Carries only a bounded <c>operation</c> tag; no counter or
    /// histogram of its own.
    /// </summary>
    public sealed class SupportChildScope : IDisposable
    {
        private readonly Activity? _activity;
        private long _completed;

        internal SupportChildScope(Activity? activity)
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

            string category = TelemetryFailureCategoryMapper
                .ToCategoryString(
                    TelemetryFailureCategoryMapper.Map(exception));

            _activity?.SetTag(
                PathVeerTagNames.Outcome, PathVeerTagValues.Failure);
            _activity?.SetStatus(ActivityStatusCode.Error);
            _activity?.SetTag(
                PathVeerTagNames.FailureCategory, category);
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
