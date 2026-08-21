namespace PathVeer.Core.Cloud;

/// <summary>
/// Test-only protector that round-trips in memory without platform DPAPI.
///
/// It intentionally does NOT provide real at-rest protection (tests run on
/// non-Windows CI and must not depend on DPAPI). It exists so the
/// registration store, coordinator, and heartbeat can be unit-tested without
/// Windows. The plaintext is NOT persisted to disk by the store when this is
/// used — the store still stores only the "protected" blob, which here is a
/// reversible transform, never the literal credential field.
/// </summary>
public sealed class InMemoryCloudSecretProtector :
    ICloudSecretProtector
{
    public string Protect(string secret) =>
        // Reversible, non-secret transform: identifies the blob as test data
        // while still being distinct from the raw credential.
        "mem:" + Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes(secret));

    public string Unprotect(string protectedSecret)
    {
        if (!protectedSecret.StartsWith("mem:", StringComparison.Ordinal))
        {
            throw new FormatException(
                "Test protector received an unexpected blob.");
        }

        return System.Text.Encoding.UTF8.GetString(
            Convert.FromBase64String(protectedSecret["mem:".Length..]));
    }
}
