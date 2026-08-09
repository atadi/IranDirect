using System.Diagnostics;
using System.Diagnostics.Metrics;
using PathVeer.Core.Ipc;

namespace PathVeer.Core.Observability.Telemetry;

/// <summary>
/// IPC request/response telemetry: one <c>PathVeer.IpcRequest</c> client root
/// Activity per <see cref="PathVeerServiceClient.SendAsync"/> attempt, with
/// <c>Ipc.Connect</c>, <c>Ipc.Send</c>, and <c>Ipc.Receive</c> child Activities
/// created only around the operations that actually execute. Exactly one
/// <c>pathveer.ipc.requests</c> counter increment and one
/// <c>pathveer.ipc.request.duration</c> histogram sample per request attempt.
///
/// Privacy: no pipe name, payload, serialized request/response, command
/// arguments, route identities, or exception message is attached. The only
/// tags are bounded <c>operation</c>, <c>ipc_command</c>, <c>outcome</c>, and
/// <c>failure_category</c> (on failure/timeout).
///
/// The named-pipe server runs in a separate process and cannot inherit
/// Activity context without changing the wire protocol; client and server
/// traces are intentionally independent (see <see cref="IpcDispatchTelemetry"/>).
/// </summary>
public static class IpcRequestTelemetry
{
    private static readonly Counter<long> s_requests = PathVeerTelemetry
        .Meter.CreateCounter<long>(
            PathVeerMetricNames.IpcRequests,
            unit: "{request}",
            description: "IPC request attempts started by the client.");

    private static readonly Histogram<double> s_duration = PathVeerTelemetry
        .Meter.CreateHistogram<double>(
            PathVeerMetricNames.IpcRequestDuration,
            unit: "ms",
            description: "Elapsed client-owned time of one IPC request.");

    internal static IpcRequestScope Start(PathVeerCommand command)
    {
        Activity? activity = PathVeerTelemetry.ActivitySource.StartActivity(
            PathVeerActivityNames.IpcRequest,
            ActivityKind.Client);

        activity?.SetTag(
            PathVeerTagNames.Operation,
            PathVeerTagValues.OperationIpcRequest);
        activity?.SetTag(
            PathVeerTagNames.IpcCommand,
            TelemetryOutcomeMapper.Map(command));

        s_requests.Add(1,
            new KeyValuePair<string, object?>(
                PathVeerTagNames.Operation,
                PathVeerTagValues.OperationIpcRequest),
            new KeyValuePair<string, object?>(
                PathVeerTagNames.IpcCommand,
                TelemetryOutcomeMapper.Map(command)));

        return new IpcRequestScope(activity);
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
    /// Root scope for one client IPC request attempt.
    /// </summary>
    public sealed class IpcRequestScope : IDisposable
    {
        private readonly Activity? _activity;
        private long _completed;

        internal IpcRequestScope(Activity? activity)
        {
            _activity = activity;
        }

        public IpcChildScope StartConnect() =>
            StartChild(
                PathVeerActivityNames.IpcConnect,
                PathVeerTagValues.OperationIpcConnect);

        public IpcChildScope StartSend() =>
            StartChild(
                PathVeerActivityNames.IpcSend,
                PathVeerTagValues.OperationIpcSend);

        public IpcChildScope StartReceive() =>
            StartChild(
                PathVeerActivityNames.IpcReceive,
                PathVeerTagValues.OperationIpcReceive);

        internal IpcChildScope StartChild(
            string name,
            string operation)
        {
            Activity? child = PathVeerTelemetry.ActivitySource
                .StartActivity(name, ActivityKind.Client);
            child?.SetTag(PathVeerTagNames.Operation, operation);
            return new IpcChildScope(child);
        }

        public void CompleteSuccess() =>
            Complete(TelemetryOutcome.Success, null);

        public void CompleteTimeout() =>
            Complete(
                TelemetryOutcome.Timeout,
                PathVeerTagValues.FailureTimeout);

        public void CompleteCancelled() =>
            Complete(TelemetryOutcome.Cancelled, null);

        public void CompleteFailure(Exception exception) =>
            Complete(
                TelemetryOutcome.Failure,
                TelemetryFailureCategoryMapper.ToCategoryString(
                    TelemetryFailureCategoryMapper.Map(exception)));

        public void Complete(
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

            RecordDuration(
                Stopwatch.GetElapsedTime(_startTimestamp)
                    .TotalMilliseconds,
                outcomeString,
                failureCategory);
        }

        private readonly long _startTimestamp = Stopwatch.GetTimestamp();

        public void Dispose()
        {
            // Safety net: if a terminal call was forgotten, classify as
            // unknown rather than a false success.
            Complete(TelemetryOutcome.Unknown, null);
            _activity?.Dispose();
        }
    }

    /// <summary>
    /// Child scope for one IPC sub-operation (connect / send / receive). Carries
    /// only a bounded <c>operation</c> tag; no counter or histogram of its own.
    /// </summary>
    public sealed class IpcChildScope : IDisposable
    {
        private readonly Activity? _activity;
        private long _completed;

        internal IpcChildScope(Activity? activity)
        {
            _activity = activity;
        }

        public void CompleteSuccess() =>
            Complete(TelemetryOutcome.Success, null);

        public void CompleteTimeout() =>
            Complete(
                TelemetryOutcome.Timeout,
                PathVeerTagValues.FailureTimeout);

        public void CompleteCancelled() =>
            Complete(TelemetryOutcome.Cancelled, null);

        public void CompleteFailure(Exception exception) =>
            Complete(
                TelemetryOutcome.Failure,
                TelemetryFailureCategoryMapper.ToCategoryString(
                    TelemetryFailureCategoryMapper.Map(exception)));

        public void Complete(
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
                if (outcome == TelemetryOutcome.Cancelled)
                {
                    // Cancellation is not an error: leave status Unset.
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
