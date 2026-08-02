using IranDirect.Core.Prefixes;

namespace IranDirect.Core.Updates;

public sealed record PrefixUpdateNotificationTrackerState(
    PrefixUpdateCheckResult? Previous,
    PrefixUpdateCheckStatus? PreviousStatus,
    PrefixUpdateCheckRemoteMetadata? PreviousRemote,
    bool UpdateAvailableSeen,
    bool ManualCheckSinceReset);

public interface IPrefixUpdateNotificationTracker
{
    bool ShouldNotify { get; }
    void ProcessSnapshot(
        PrefixUpdateMonitorSnapshot snapshot,
        bool serviceAvailable,
        bool manualCheckSinceLastReset);
    void MarkNotified();
    void Reset();
    PrefixUpdateNotificationTrackerState GetState();
}

public sealed class PrefixUpdateNotificationTracker :
    IPrefixUpdateNotificationTracker
{
    private PrefixUpdateNotificationTrackerState _state =
        new(
            Previous: null,
            PreviousStatus: null,
            PreviousRemote: null,
            UpdateAvailableSeen: false,
            ManualCheckSinceReset: false);

    public bool ShouldNotify
    {
        get
        {
            if (!_state.UpdateAvailableSeen && _state.Previous is not null)
            {
                PrefixUpdateCheckStatus status = _state.Previous.Status;

                bool isUpdateAvailable =
                    status == PrefixUpdateCheckStatus.UpdateAvailable;

                if (isUpdateAvailable)
                {
                    return true;
                }
            }

            return false;
        }
    }

    public void ProcessSnapshot(
        PrefixUpdateMonitorSnapshot snapshot,
        bool serviceAvailable,
        bool manualCheckSinceLastReset)
    {
        PrefixUpdateCheckResult? current = snapshot.CurrentResult;
        PrefixUpdateCheckStatus? currentStatus = current?.Status;
        PrefixUpdateCheckRemoteMetadata? currentRemote = current?.RemoteMetadata;

        if (!serviceAvailable)
        {
            return;
        }

        if (_state.Previous is null)
        {
            _state = _state with
            {
                Previous = current,
                PreviousStatus = currentStatus,
                PreviousRemote = currentRemote,
                UpdateAvailableSeen =
                    currentStatus == PrefixUpdateCheckStatus.UpdateAvailable,
                ManualCheckSinceReset = manualCheckSinceLastReset
            };
            return;
        }

        bool isUpdateAvailable = currentStatus == PrefixUpdateCheckStatus.UpdateAvailable;

        if (isUpdateAvailable)
        {
            bool alreadySeen = _state.UpdateAvailableSeen;
            bool identityChanged = !RemoteIdentityEquals(
                _state.PreviousRemote,
                currentRemote,
                _state.PreviousStatus,
                currentStatus);

            if (identityChanged)
            {
                _state = _state with
                {
                    UpdateAvailableSeen = false,
                    Previous = current,
                    PreviousStatus = currentStatus,
                    PreviousRemote = currentRemote,
                    ManualCheckSinceReset = false
                };
            }
            else if (!alreadySeen)
            {
                _state = _state with
                {
                    Previous = current,
                    PreviousStatus = currentStatus,
                    PreviousRemote = currentRemote,
                    ManualCheckSinceReset = false
                };
            }
            else
            {
                _state = _state with
                {
                    Previous = current,
                    PreviousStatus = currentStatus,
                    PreviousRemote = currentRemote,
                    ManualCheckSinceReset = false
                };
            }
        }
        else if (currentStatus != PrefixUpdateCheckStatus.UpdateAvailable)
        {
            _state = _state with
            {
                UpdateAvailableSeen = false,
                Previous = current,
                PreviousStatus = currentStatus,
                PreviousRemote = currentRemote,
                ManualCheckSinceReset = false
            };
        }

        if (manualCheckSinceLastReset && isUpdateAvailable)
        {
            _state = _state with { UpdateAvailableSeen = true };
        }
    }

    public void MarkNotified()
    {
        _state = _state with { UpdateAvailableSeen = true };
    }

    public void Reset()
    {
        _state = new PrefixUpdateNotificationTrackerState(
            Previous: null,
            PreviousStatus: null,
            PreviousRemote: null,
            UpdateAvailableSeen: false,
            ManualCheckSinceReset: false);
    }

    public PrefixUpdateNotificationTrackerState GetState() => _state;

    private static bool RemoteIdentityEquals(
        PrefixUpdateCheckRemoteMetadata? a,
        PrefixUpdateCheckRemoteMetadata? b,
        PrefixUpdateCheckStatus? statusA,
        PrefixUpdateCheckStatus? statusB)
    {
        if (a is null && b is null) return true;
        if (a is null || b is null) return false;

        if (!string.IsNullOrEmpty(a.ETag) && !string.IsNullOrEmpty(b.ETag))
            return a.ETag.Equals(b.ETag, StringComparison.Ordinal);

        if (a.LastModified.HasValue && b.LastModified.HasValue)
            return a.LastModified.Equals(b.LastModified);

        if (a.ContentLength.HasValue && b.ContentLength.HasValue)
            return a.ContentLength.Equals(b.ContentLength);

        return statusA == statusB;
    }
}