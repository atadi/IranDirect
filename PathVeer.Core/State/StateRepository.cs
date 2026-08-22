using PathVeer.Core.Persistence;

namespace PathVeer.Core.State;

/// <summary>
/// Durable runtime state for the PathVeer engine. This is observed/recoverable
/// runtime state (not authoritative configuration or a mutation journal), so it
/// is marked for backup rollback: a corrupt primary is automatically restored
/// from the last known-good ".bak" instead of permanently bricking every future
/// repair/repair cycle. A missing file still yields a default; a corrupt primary
/// with no valid backup fails clearly (it is never silently reset to default).
/// </summary>
public sealed class StateRepository :
    JsonStore<PathVeerState>
{
    public StateRepository(string statePath)
        : base(
            statePath,
            recoveryMode: JsonStoreRecoveryMode.BackupRollback)
    {
    }
}