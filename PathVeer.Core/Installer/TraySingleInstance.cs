namespace PathVeer.Core.Installer;

/// <summary>
/// Per-interactive-user-session single-instance guard for the PathVeer Tray.
///
/// The Tray is per-USER UI/controller, NOT machine-authoritative (the Service
/// is). The guard is therefore scoped to the current Windows session via the
/// "Local\" mutex namespace prefix (NOT "Global\"), so:
///   * exactly one Tray per logged-in user session,
///   * separate Windows users each get their own Tray (no cross-user block),
///   * a mutex abandoned by a crashed previous owner does not permanently block
///     startup (AbandonedMutexException is treated as an ownership transfer),
///   * a duplicate launch detects the live owner and returns null so the caller
///     can exit cleanly (optionally signalling the existing instance to show).
///
/// This is the authoritative guard. Installer/autorun/Start-Menu launches are
/// NOT the authority — they may spawn the EXE freely; the Tray itself enforces
/// the contract.
/// </summary>
public sealed class TraySingleInstance : IDisposable
{
    /// <summary>
    /// Session-scoped (Local\, not Global\) mutex name. One Tray per session.
    /// </summary>
    public const string DefaultMutexName = @"Local\PathVeer.Tray.SingleInstance";

    /// <summary>
    /// Session-scoped event a duplicate launch sets to ask the existing Tray to
    /// surface itself (show balloon / restore). Auto-reset.
    /// </summary>
    public const string ShowEventName = @"Local\PathVeer.Tray.ShowExisting";

    private readonly string _mutexName;
    private Mutex? _owner;

    public TraySingleInstance(string? mutexName = null)
    {
        _mutexName = mutexName ?? DefaultMutexName;
    }

    /// <summary>
    /// Attempts to take ownership of the single-instance mutex. Returns a live
    /// guard holding the mutex when this process is the authoritative (first)
    /// instance; returns <c>null</c> when another instance already owns it.
    /// </summary>
    public static TraySingleInstance? TryAcquire(string? mutexName = null)
    {
        var guard = new TraySingleInstance(mutexName);
        Mutex? m = null;
        bool created = false;
        try
        {
            m = new Mutex(true, guard._mutexName, out created);
        }
        catch (AbandonedMutexException)
        {
            // Previous owner crashed without releasing; the runtime transferred
            // ownership to us. OpenExisting returns the (now ours) handle.
            try
            {
                m = Mutex.OpenExisting(guard._mutexName);
                created = true;
            }
            catch
            {
                m?.Dispose();
                return null;
            }
        }

        if (!created)
        {
            // A live instance owns the name.
            m?.Dispose();
            return null;
        }

        guard._owner = m;
        return guard;
    }

    /// <summary>Opens (or creates) the cross-instance "show existing" event.</summary>
    public static EventWaitHandle? OpenOrCreateShowEvent()
    {
        try
        {
            return new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName, out _);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Signals a running primary instance to surface itself. Best-effort: if no
    /// primary has created the event yet, this is a harmless no-op.
    /// </summary>
    public static void SignalExisting()
    {
        try
        {
            using var evt = EventWaitHandle.OpenExisting(ShowEventName);
            evt.Set();
        }
        catch
        {
            // Primary not ready or already gone; nothing to signal.
        }
    }

    public Mutex? Owner => _owner;

    public bool IsOwned => _owner is not null;

    public void Release()
    {
        if (_owner is null) return;
        try { _owner.ReleaseMutex(); }
        catch (ApplicationException) { /* already released by us */ }
        catch (ObjectDisposedException) { /* already disposed */ }
        finally
        {
            _owner.Dispose();
            _owner = null;
        }
    }

    public void Dispose() => Release();
}
