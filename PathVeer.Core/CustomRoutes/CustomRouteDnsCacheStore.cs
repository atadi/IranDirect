using System.Text.Json;
using PathVeer.Core.Persistence;

namespace PathVeer.Core.CustomRoutes;

/// <summary>
/// Derived DNS cache for custom routes. This is pure cache/derived state: a
/// corrupt cache is safe to restore from the last known-good ".bak" (or simply
/// be rebuilt by the DNS path), so it uses backup rollback. It is never
/// authoritative for any network mutation: a restored prior-generation cache is
/// only a stale DNS fallback and can never override CustomRoute authoritative
/// configuration.
/// </summary>
public sealed class CustomRouteDnsCacheStore :
    JsonStore<CustomRouteDnsCacheCollection>
{
    public CustomRouteDnsCacheStore(string path)
        : base(
            path,
            JsonStoreRecoveryMode.BackupRollback,
            jsonOptions: CreateJsonOptions())
    {
    }

    /// <summary>
    /// Internal testing hook: lets a test wire recovery diagnostics without
    /// changing the public contract.
    /// </summary>
    // Public for Service composition (a different assembly) to wire recovery
    // diagnostics. The recovery mode is still fixed internally to BackupRollback
    // with the supplied diagnostics options, so the public API cannot opt into
    // an arbitrary mode.
    public CustomRouteDnsCacheStore(
        string path,
        JsonStoreRecoveryOptions? recoveryOptions = null)
        : base(
            path,
            JsonStoreRecoveryMode.BackupRollback,
            recoveryOptions,
            jsonOptions: CreateJsonOptions())
    {
    }

    private static JsonSerializerOptions CreateJsonOptions() =>
        new()
        {
            WriteIndented = true
        };
}
