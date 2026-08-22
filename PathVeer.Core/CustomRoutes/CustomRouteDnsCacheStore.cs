using System.Text.Json;
using PathVeer.Core.Persistence;

namespace PathVeer.Core.CustomRoutes;

/// <summary>
/// Derived DNS cache for custom routes. This is pure cache/derived state: a
/// corrupt cache is safe to restore from the last known-good ".bak" (or simply
/// be rebuilt by the DNS path), so it uses backup rollback. It is never
/// authoritative for any network mutation.
/// </summary>
public sealed class CustomRouteDnsCacheStore :
    JsonStore<CustomRouteDnsCacheCollection>
{
    public CustomRouteDnsCacheStore(string path)
        : base(
            path,
            CreateJsonOptions(),
            recoveryMode: JsonStoreRecoveryMode.BackupRollback)
    {
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        return new JsonSerializerOptions
        {
            WriteIndented = true
        };
    }
}
