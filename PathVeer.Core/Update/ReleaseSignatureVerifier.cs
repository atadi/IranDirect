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
/// Production private key is supplied via PATHVEER_META_SIGN_KEY (base64), never
/// committed. A dev/unsigned manifest carries Signature == null and is accepted
/// only when the verifier is constructed with allowUnsigned = true.
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
