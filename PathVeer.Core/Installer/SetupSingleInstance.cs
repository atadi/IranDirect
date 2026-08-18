using System.Security.Principal;
using System.Threading;

namespace PathVeer.Core.Installer;

/// <summary>
/// Process-elevation detection. Extracted from <c>PathVeer.Setup.Program</c>
/// so the branch can be unit-tested without spawning real UAC prompts.
/// </summary>
public static class ProcessPrivileges
{
    public static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }
}

/// <summary>
/// Single-instance guard for the *elevated* PathVeer Setup process.
///
/// The named mutex MUST be owned only by the process that actually performs
/// install/migration work (the elevated instance). A non-elevated launcher must
/// NOT acquire it before relaunching elevated — otherwise the elevated child
/// sees the name already taken and exits, producing a dead installer that the
/// user can only unblock by manually "Run as administrator".
///
/// This wrapper exposes the two outcomes the bootstrapper needs so they can be
/// exercised directly in tests:
///   * <see cref="TryAcquire"/> returns <c>false</c> when another owner holds
///     the name (concurrent elevated installer is rejected);
///   * <see cref="Owner"/> carries the acquired <see cref="Mutex"/> and releases
///     it via <see cref="IDisposable.Dispose"/> so the launcher path can release
///     the name before relaunching.
/// </summary>
public sealed class SetupSingleInstance : IDisposable
{
    public const string DefaultMutexName = @"Global\PathVeer.Setup.SingleInstance";

    private readonly string _mutexName;
    private Mutex? _owner;

    public SetupSingleInstance(string? mutexName = null)
    {
        _mutexName = mutexName ?? DefaultMutexName;
    }

    /// <summary>
    /// Attempts to take ownership of the single-instance mutex. Returns a live
    /// <see cref="SetupSingleInstance"/> holding the mutex when this process is
    /// the authoritative (first) instance; returns <c>null</c> when another
    /// process already owns the name.
    /// </summary>
    public static SetupSingleInstance? TryAcquire(string? mutexName = null)
    {
        var guard = new SetupSingleInstance(mutexName);
        try
        {
            // initiallyOwned: true — we ask to become the owner.
            var mutex = new Mutex(true, guard._mutexName, out bool created);
            if (!created)
            {
                // Another instance owns the name; we must not hold a claim on it.
                mutex.Dispose();
                return null;
            }

            guard._owner = mutex;
            return guard;
        }
        catch (UnauthorizedAccessException)
        {
            // The mutex exists with a different ACL (e.g. created in a different
            // session/security context). Treat as "already running elsewhere".
            guard._owner?.Dispose();
            guard._owner = null;
            return null;
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            guard._owner?.Dispose();
            guard._owner = null;
            return null;
        }
    }

    /// <summary>
    /// The owning mutex. Non-null only while this process holds the claim.
    /// </summary>
    public Mutex? Owner => _owner;

    public bool IsOwned => _owner is not null;

    public void Release()
    {
        if (_owner is null) return;
        try { _owner.ReleaseMutex(); }
        finally
        {
            _owner.Dispose();
            _owner = null;
        }
    }

    public void Dispose() => Release();
}
