namespace PathVeer.Core.Update;

using System.Reflection;

/// <summary>
/// Built-in release-metadata public-key trust anchors shipped inside the
/// PathVeer client. Only PUBLIC keys live here — the corresponding private keys
/// are machine-local protected secrets on the release workstation and are never
/// committed. Loading a manifest in a production build therefore verifies
/// against a key the binary already trusts, with no consumer configuration
/// required.
///
/// Staging keys (e.g. <c>pv-meta-staging-*</c>) are deliberately NOT included:
/// a production consumer must not accept staging-signed metadata merely because
/// both belong to PathVeer. Use PATHVEER_TRUSTED_META_KEYS only for explicit,
/// controlled staging/rotation overrides.
///
/// Rotation model: add the new production key here alongside the old one, sign
/// subsequent manifests with the new key, then drop the old entry in a later
/// release. Overlap keeps already-shipped clients able to verify.
/// </summary>
public static class BuiltInReleaseTrust
{
    /// <summary>Production trust anchor keyId (embedded, built-in).</summary>
    public const string ProductionKeyId = "pv-meta-prod-2026-01";

    /// <summary>Development-only trust anchor keyId (embedded, built-in).</summary>
    /// <remarks>
    /// Cryptographically and operationally SEPARATE from production. A dev-signed
    /// manifest carries this keyId and is verified only by the development trust
    /// set, never by <see cref="ForProduction"/>. The private key is a disposable,
    /// developer-only secret — never the production metadata key.
    /// </remarks>
    public const string DevelopmentKeyId = "pv-meta-dev-2026-01";

    private const string EmbeddedResourceName =
        "PathVeer.Core.Update.BuiltInReleaseTrust.pv-meta-prod-2026-01.json";

    private const string DevEmbeddedResourceName =
        "PathVeer.Core.Update.BuiltInReleaseTrust.pv-meta-dev-2026-01.json";

    /// <summary>
    /// Production trust anchors ONLY. This method is intentionally isolated from the
    /// development anchor: a production binary must never accept a dev-signed
    /// manifest. Do NOT add the dev key here.
    /// </summary>
    public static IReadOnlyDictionary<string, byte[]> All()
    {
        var keys = new Dictionary<string, byte[]>(StringComparer.Ordinal);

        // Compile-time fallback (always valid even if the embedded resource is
        // missing). Mirrors Update/BuiltInReleaseTrust/pv-meta-prod-2026-01.json.
        // Public key fingerprint (SHA-256 of the 64-byte Q.X||Q.Y):
        //   59704d9d42eb43884cc8b6c996eae65053f953564262e972f50644e132142ba9
        keys[ProductionKeyId] =
            Convert.FromBase64String("fN95fm+Do+CGr8RLvC+XBJIhtATr4D63gbpIJhL0w+c/9YKJ6DwcsWnU65Gfmu8OLFWg3VyMn2Mp1N6ZVpmWJg==");

        // Preferred source: the embedded JSON (single source of truth). It can
        // carry multiple anchors without code changes.
        try
        {
            var asm = typeof(BuiltInReleaseTrust).Assembly;
            using var stream = asm.GetManifestResourceStream(EmbeddedResourceName);
            if (stream is not null)
            {
                using var reader = new StreamReader(stream);
                var json = reader.ReadToEnd();
                var doc = System.Text.Json.JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("publicKey", out var pub) &&
                    doc.RootElement.TryGetProperty("keyId", out var id) &&
                    pub.ValueKind == System.Text.Json.JsonValueKind.String &&
                    id.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    var kid = id.GetString()!;
                    var bytes = Convert.FromBase64String(pub.GetString()!);
                    if (bytes.Length == 64)
                        keys[kid] = bytes; // embedded wins over the fallback
                }
            }
        }
        catch
        {
            // Fall back to the compile-time key on any parse/read failure.
        }

        return keys;
    }

    /// <summary>
    /// Development-only trust anchors (built-in public key for
    /// <see cref="DevelopmentKeyId"/>). Isolated from <see cref="All"/> (production)
    /// so the two trust roots never merge. A dev build uses this set via
    /// <c>ReleaseSignatureVerifier.ForDevelopment()</c>; a shipping production
    /// binary must continue to use <see cref="All"/> / <c>ForProduction()</c>.
    /// </summary>
    public static IReadOnlyDictionary<string, byte[]> Development()
    {
        var keys = new Dictionary<string, byte[]>(StringComparer.Ordinal);

        // Compile-time fallback mirroring pv-meta-dev-2026-01.json.
        // Public key fingerprint (SHA-256 of the 64-byte Q.X||Q.Y) is written
        // into the committed JSON; the fallback below is the same value.
        keys[DevelopmentKeyId] =
            Convert.FromBase64String("6XdfR5MMwotrORvcTlrNtKNLuFzUis2tFR4/rBBX/i1ZuJwO5CV9ZMYsbijyz8hDGSzHxmYoxomeZUlgbc8NmA==");

        try
        {
            var asm = typeof(BuiltInReleaseTrust).Assembly;
            using var stream = asm.GetManifestResourceStream(DevEmbeddedResourceName);
            if (stream is not null)
            {
                using var reader = new StreamReader(stream);
                var json = reader.ReadToEnd();
                var doc = System.Text.Json.JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("publicKey", out var pub) &&
                    doc.RootElement.TryGetProperty("keyId", out var id) &&
                    pub.ValueKind == System.Text.Json.JsonValueKind.String &&
                    id.ValueKind == System.Text.Json.JsonValueKind.String &&
                    id.GetString() == DevelopmentKeyId)
                {
                    var bytes = Convert.FromBase64String(pub.GetString()!);
                    if (bytes.Length == 64)
                        keys[DevelopmentKeyId] = bytes;
                }
            }
        }
        catch
        {
            // Fall back to the compile-time key on any parse/read failure.
        }

        return keys;
    }

    /// <summary>
    /// True when a keyId is a built-in production trust anchor (used by tests
    /// and diagnostics). Staging/test keyIds return false.
    /// </summary>
    public static bool Contains(string keyId) => All().ContainsKey(keyId);
}
