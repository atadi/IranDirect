using PathVeer.Core.Persistence;

namespace PathVeer.Core.State;

/// <summary>
/// Durable runtime state for the PathVeer engine. This is observed/recoverable
/// runtime state (not authoritative configuration or a mutation journal), so it
/// is marked for backup rollback: a corrupt primary is automatically restored
/// from the last known-good ".bak" instead of permanently bricking every future
/// repair/repair cycle. A missing file still yields a default; a corrupt primary
/// with no valid backup fails clearly (it is never silently reset to default).
///
/// Restored historical runtime state is OBSERVATION/APPLIED state only: it is
/// never treated as DesiredConfiguration authority. The engine re-derives
/// desired network state from DesiredConfigurationStore (and live network
/// observation); PathVeerState.Enabled/Gateway/InterfaceIndex/PrefixCount are
/// applied/observed values, so recovering a backup that says Enabled=true does
/// NOT by itself cause route creation.
/// </summary>
public sealed class StateRepository :
    JsonStore<PathVeerState>
{
    public StateRepository(string statePath)
        : base(
            statePath,
            JsonStoreRecoveryMode.BackupRollback)
    {
    }

    /// <summary>
    /// Internal testing hook: lets a test wire recovery diagnostics without
    /// changing the public contract.
    /// </summary>
    internal StateRepository(
        string statePath,
        JsonStoreRecoveryOptions options)
        : base(
            statePath,
            JsonStoreRecoveryMode.BackupRollback,
            options)
    {
    }
}
