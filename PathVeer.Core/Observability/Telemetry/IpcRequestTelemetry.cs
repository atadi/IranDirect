using System.Diagnostics;
using System.Diagnostics.Metrics;
using PathVeer.Core.Ipc;

namespace PathVeer.Core.Observability.Telemetry;

/// <summary>
/// IPC request/response telemetry: one <c>IranDirect.IpcRequest</c> client root
/// Activity per <see cref="IranDirectServiceClient.SendAsync"/> attempt, with
/// <c>Ipc.Connect</c>, <c>Ipc.Send</c>, and <c>Ipc.Receive</c> child Activities
/// created only around the operations that actually execute. Exactly one
/// <c>irandirect.ipc.requests</c> counter increment and one
/// <c>irandirect.ipc.request.duration</c> histogram sample per request attempt.
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
    private static readonly Counter<long> s_requests = IranDirectTelemetry
        .Meter.CreateCounter<long>(
            IranDirectMetricNames.IpcRequests,
            unit: "{request}",
            description: "IPC request attempts started by the client.");

    private static readonly Histogram<double> s_duration = IranDirectTelemetry
        .Meter.CreateHistogram<double>(
            IranDirectMetricNames.IpcRequestDuration,
            unit: "ms",
            description: "Elapsed client-owned time of one IPC request.");

    internal static IpcRequestScope Start(IranDirectCommand command)
    {
        Activity? activity = IranDirectTelemetry.ActivitySource.StartActivity(
            IranDirectActivityNames.IpcRequest,
            ActivityKind.Client);

        activity?.SetTag(
            IranDirectTagNames.Operation,
            IranDirectTagValues.OperationIpcRequest);
        activity?.SetTag(
            IranDirectTagNames.IpcCommand,
            TelemetryOutcomeMapper.Map(command));

        s_requests.Add(1,
            new KeyValuePair<string, object?>(
                IranDirectTagNames.Operation,
                IranDirectTagValues.OperationIpcRequest),
            new KeyValuePair<string, object?>(
                IranDirectTagNames.IpcCommand,
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
                    IranDirectTagNames.Outcome, outcome),
                new KeyValuePair<string, object?>(
                    IranDirectTagNames.FailureCategory, failureCategory));
        }
        else
        {
            s_duration.Record(
                elapsedMs,
                new KeyValuePair<string, object?>(
                    IranDirectTagNames.Outcome, outcome));
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
                IranDirectActivityNames.IpcConnect,
                IranDirectTagValues.OperationIpcConnect);

        public IpcChildScope StartSend() =>
            StartChild(
                IranDirectActivityNames.IpcSend,
                IranDirectTagValues.OperationIpcSend);

        public IpcChildScope StartReceive() =>
            StartChild(
                IranDirectActivityNames.IpcReceive,
                IranDirectTagValues.OperationIpcReceive);

        internal IpcChildScope StartChild(
            string name,
            string operation)
        {
            Activity? child = IranDirectTelemetry.ActivitySource
                .StartActivity(name, ActivityKind.Client);
            child?.SetTag(IranDirectTagNames.Operation, operation);
            return new IpcChildScope(child);
        }

        public void CompleteSuccess() =>
            Complete(TelemetryOutcome.Success, null);

        public void CompleteTimeout() =>
            Complete(
                TelemetryOutcome.Timeout,
                IranDirectTagValues.FailureTimeout);

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
                IranDirectTagValues.FailureTimeout);

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
                    IranDirectTagNames.Outcome, outcomeString);
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
                            IranDirectTagNames.FailureCategory,
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
                IranDirectTagNames.Outcome,
                IranDirectTagValues.OutcomeUnknown);
            _activity?.Dispose();
        }
    }
}
