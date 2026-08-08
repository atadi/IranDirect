using System.Diagnostics;
using PathVeer.Core.Ipc;

namespace PathVeer.Core.Observability.Telemetry;

/// <summary>
/// IPC server-dispatch telemetry: one independent <c>Ipc.Dispatch</c> Activity
/// per parsed request handled by the named-pipe command server.
///
/// This Activity is intentionally INDEPENDENT of the client
/// <c>IranDirect.IpcRequest</c> root: the server runs in a separate process
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
    public static IpcDispatchScope Start(IranDirectCommand command)
    {
        Activity? activity = IranDirectTelemetry.ActivitySource.StartActivity(
            IranDirectActivityNames.IpcDispatch,
            ActivityKind.Server);

        activity?.SetTag(
            IranDirectTagNames.Operation,
            IranDirectTagValues.OperationIpcDispatch);
        activity?.SetTag(
            IranDirectTagNames.IpcCommand,
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
                IranDirectTagNames.Outcome, IranDirectTagValues.Success);
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
                IranDirectTagNames.Outcome, IranDirectTagValues.Failure);
            _activity?.SetStatus(ActivityStatusCode.Error);
            _activity?.SetTag(
                IranDirectTagNames.FailureCategory, category);
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
