using System.Diagnostics;
using PathVeer.Core.Ipc;

namespace PathVeer.Core.Observability.Telemetry;

/// <summary>
/// IPC server-dispatch telemetry: one independent <c>Ipc.Dispatch</c> Activity
/// per parsed request handled by the named-pipe command server.
///
/// This Activity is intentionally INDEPENDENT of the client
/// <c>PathVeer.IpcRequest</c> root: the server runs in a separate process
/// and Activity context cannot cross the named pipe without changing the wire
/// protocol (explicitly forbidden this phase). It is not parented to anything.
///
/// No metrics are recorded for dispatch (the Phase 32.2 catalog defines no
/// per-dispatch counters/histograms), and no per-handler dynamic span names
/// are created. Only bounded <c>operation</c> and <c>ipc_command</c> tags are
/// attached; no payload, handler name, or exception text is ever included.
/// </summary>
public static class IpcDispatchTelemetry
{
    public static IpcDispatchScope Start(PathVeerCommand command)
    {
        Activity? activity = PathVeerTelemetry.ActivitySource.StartActivity(
            PathVeerActivityNames.IpcDispatch,
            ActivityKind.Server);

        activity?.SetTag(
            PathVeerTagNames.Operation,
            PathVeerTagValues.OperationIpcDispatch);
        activity?.SetTag(
            PathVeerTagNames.IpcCommand,
            TelemetryOutcomeMapper.Map(command));

        return new IpcDispatchScope(activity);
    }

    /// <summary>
    /// Scope for one server-side request dispatch.
    /// </summary>
    public sealed class IpcDispatchScope : IDisposable
    {
        private readonly Activity? _activity;
        private long _completed;

        internal IpcDispatchScope(Activity? activity)
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
