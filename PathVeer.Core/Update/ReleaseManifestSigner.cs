namespace PathVeer.Core.Update;

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

/// <summary>
/// Signs a release manifest payload with ECDsa P-256 (ES256) over its canonical
/// bytes and returns the final manifest JSON including the signature envelope.
/// Mirrors Sign-ReleaseManifest.ps1 so production signatures verify in the client.
/// </summary>
public sealed class ReleaseManifestSigner
{
    private readonly string _keyId;
    private readonly ECParameters _privateKey; // full parameters (includes D)

    public ReleaseManifestSigner(string keyId, ECParameters privateKey)
    {
        _keyId = keyId;
        _privateKey = privateKey;
    }

    /// <summary>Returns the final manifest JSON (with signature) as UTF-8 text.</summary>
    public string Sign(string rawManifestJson)
    {
        var payload = ReleaseManifest.CanonicalizePayload(rawManifestJson);
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportParameters(_privateKey);
        var sig = ecdsa.SignData(payload, HashAlgorithmName.SHA256);
        var envelope = JsonNode.Parse(rawManifestJson)!.AsObject();
        envelope["signature"] = JsonNode.Parse(JsonSerializer.Serialize(new
        {
            algorithm = "ES256",
            keyId = _keyId,
            value = Convert.ToBase64String(sig)
        }));
        var m = JsonSerializer.Deserialize<ReleaseManifest>(envelope.ToJsonString());
        m!.Signed = true;
        return m.ToJson();
    }

    public byte[] PublicKeyQxQy()
    {
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportParameters(_privateKey);
        var pub = ecdsa.ExportParameters(false);
        var outBuf = new byte[64];
        Array.Copy(pub.Q.X!, 0, outBuf, 0, 32);
        Array.Copy(pub.Q.Y!, 0, outBuf, 32, 32);
        return outBuf;
    }

    /// <summary>Generates a fresh P-256 key pair; returns (keyId, private params, public QxQy).</summary>
    public static (string keyId, ECParameters privateParams, byte[] publicQxQy) Generate(string keyId)
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var priv = ecdsa.ExportParameters(true);
        var pub = ecdsa.ExportParameters(false);
        var qxqy = new byte[64];
        Array.Copy(pub.Q.X!, 0, qxqy, 0, 32);
        Array.Copy(pub.Q.Y!, 0, qxqy, 32, 32);
        return (keyId, priv, qxqy);
    }
}
