namespace PathVeer.Core.Update;

using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

/// <summary>
/// Phase 37.3 — release-metadata signature verification.
///
/// Signing uses a SEPARATE ECDsa P-256 (ES256) key from the Authenticode
/// code-signing certificate (see docs). Trust is key-set based and
/// rotation-aware: a manifest carries a keyId; verification succeeds if ANY
/// trusted key with that id validates the signature over the canonical payload.
///
/// The production trust root is the BUILT-IN public key set
/// (BuiltInReleaseTrust). The private key is never committed; only the public
/// key is embedded, so this is safe to ship. PATHVEER_TRUSTED_META_KEYS is an
/// OPTIONAL override/addition used for development, staging and controlled
/// rotations — it can add or replace keys but can NEVER weaken production trust
/// to accept unsigned manifests.
/// A dev/unsigned manifest carries Signature == null and is accepted only when
/// the verifier is constructed with allowUnsigned = true.
/// </summary>
public sealed class ReleaseSignatureVerifier
{
    private readonly Dictionary<string, byte[]> _trustedPublicKeys; // keyId -> Q.X||Q.Y (64 bytes)
    private readonly bool _allowUnsigned;

    public ReleaseSignatureVerifier(IEnumerable<KeyValuePair<string, byte[]>> trustedPublicKeys, bool allowUnsigned = false)
    {
        _trustedPublicKeys = trustedPublicKeys.ToDictionary(k => k.Key, k => k.Value, StringComparer.Ordinal);
        _allowUnsigned = allowUnsigned;
    }

    /// <summary>
    /// Production trust bootstrap. Uses ONLY the built-in release-metadata public
    /// keys (BuiltInReleaseTrust). The PATHVEER_TRUSTED_META_KEYS environment
    /// variable is a DEV/TEST/STAGING mechanism and is deliberately NOT merged
    /// here: a production binary must never let an environment override (e.g. a
    /// staging key) weaken or dilute its production trust root. Rotation overlap is
    /// achieved by embedding additional production keys in code, not by env.
    /// The result is always signed-only (allowUnsigned = false): an unsigned or
    /// unknown/staging-key manifest hard-fails.
    /// </summary>
    public static ReleaseSignatureVerifier ForProduction()
    {
        var keys = BuiltInReleaseTrust.All().ToDictionary(k => k.Key, k => k.Value, StringComparer.Ordinal);
        return new ReleaseSignatureVerifier(keys, allowUnsigned: false);
    }

    /// <summary>
    /// Development trust bootstrap. Uses ONLY the built-in development release-metadata
    /// public key (BuiltInReleaseTrust.Development). It is intentionally SEPARATE from
    /// <see cref="ForProduction"/>: a development-signed manifest (keyId
    /// pv-meta-dev-2026-01) is verified here, and a production-signed manifest is
    /// NOT accepted by this verifier (and vice versa). The result is always
    /// signed-only (allowUnsigned = false): an unsigned or unknown dev manifest
    /// hard-fails. Never consumes the production metadata private key.
    /// </summary>
    public static ReleaseSignatureVerifier ForDevelopment()
    {
        var keys = BuiltInReleaseTrust.Development().ToDictionary(k => k.Key, k => k.Value, StringComparer.Ordinal);
        return new ReleaseSignatureVerifier(keys, allowUnsigned: false);
    }

    /// <summary>
    /// Phase 37.5 — development / test trusted-key bootstrap from the
    /// PATHVEER_TRUSTED_META_KEYS environment variable. Kept for dev, staging and
    /// tests; it does NOT embed any production key, so production code must use
    /// <see cref="ForProduction"/> instead. When no keys are present and
    /// <paramref name="devAllowUnsigned"/> is true, unsigned dev manifests are
    /// tolerated.
    /// </summary>
    public static ReleaseSignatureVerifier FromEnvironment(bool devAllowUnsigned = true)
    {
        var keys = FromEnvironmentEntries();
        // When any trusted key is configured, unsigned manifests are rejected
        // (signed-only) regardless of devAllowUnsigned. Only with NO keys does the
        // dev flag permit unsigned dev feeds.
        var allowUnsigned = (!keys.Any()) && devAllowUnsigned;
        return new ReleaseSignatureVerifier(keys, allowUnsigned);
    }

    private static IEnumerable<KeyValuePair<string, byte[]>> FromEnvironmentEntries()
    {
        var entries = (Environment.GetEnvironmentVariable("PATHVEER_TRUSTED_META_KEYS") ?? "").Trim();
        if (string.IsNullOrWhiteSpace(entries))
            yield break;

        foreach (var raw in entries.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var sep = raw.IndexOf(':');
            if (sep < 0) continue;
            var keyId = raw[..sep];
            var bytes = Convert.FromBase64String(raw[(sep + 1)..]);
            if (bytes.Length != 64) continue; // Q.X||Q.Y only; reject malformed
            yield return new KeyValuePair<string, byte[]>(keyId, bytes);
        }
    }

    public SignatureVerificationResult Verify(ReleaseManifest manifest, string rawJson)
    {
        if (manifest.Signature is null || string.IsNullOrEmpty(manifest.Signature.Value))
        {
            return _allowUnsigned
                ? SignatureVerificationResult.Unsigned()
                : SignatureVerificationResult.Failed("Manifest is unsigned; production metadata requires a signature.");
        }

        if (!string.Equals(manifest.Signature.Algorithm, "ES256", StringComparison.OrdinalIgnoreCase))
            return SignatureVerificationResult.Failed($"Unsupported signature algorithm: {manifest.Signature.Algorithm}.");

        if (!_trustedPublicKeys.TryGetValue(manifest.Signature.KeyId, out var pubKey))
            return SignatureVerificationResult.Failed($"No trusted key for keyId '{manifest.Signature.KeyId}'.");

        try
        {
            var payload = ReleaseManifest.CanonicalizePayload(rawJson);
            var sig = Convert.FromBase64String(manifest.Signature.Value);
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportParameters(PublicKeyParameters(pubKey));
            bool ok = ecdsa.VerifyData(payload, sig, HashAlgorithmName.SHA256);
            return ok
                ? SignatureVerificationResult.Valid(manifest.Signature.KeyId)
                : SignatureVerificationResult.Failed("Signature does not match canonical payload.");
        }
        catch (Exception ex)
        {
            return SignatureVerificationResult.Failed($"Signature verification error: {ex.Message}");
        }
    }

    internal static ECParameters PublicKeyParameters(byte[] qxqy)
    {
        var x = new byte[32];
        var y = new byte[32];
        Array.Copy(qxqy, 0, x, 0, 32);
        Array.Copy(qxqy, 32, y, 0, 32);
        return new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = x, Y = y }
        };
    }
}

public sealed class SignatureVerificationResult
{
    public bool IsValid { get; }
    public bool IsUnsigned { get; }
    public string? KeyId { get; }
    public string? Error { get; }

    private SignatureVerificationResult(bool valid, bool unsigned, string? keyId, string? error)
    {
        IsValid = valid;
        IsUnsigned = unsigned;
        KeyId = keyId;
        Error = error;
    }

    public static SignatureVerificationResult Valid(string keyId) => new(true, false, keyId, null);
    public static SignatureVerificationResult Unsigned() => new(false, true, null, null);
    public static SignatureVerificationResult Failed(string error) => new(false, false, null, error);
}
