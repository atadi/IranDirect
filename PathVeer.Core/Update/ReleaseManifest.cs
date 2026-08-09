namespace PathVeer.Core.Update;

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;

/// <summary>
/// Phase 37.3 — versioned release manifest model (schemaVersion 1).
///
/// Client-facing update descriptor. Install/migration/state logic stays in
/// Install-PathVeer.ps1 + PathVeer.Core.Installation.
///
/// Canonicalization for signing: the signature node is excluded and the
/// remaining JSON is canonicalized (sorted keys, compact, UTF-8,
/// casing-preserving) via CanonicalizePayload. The PowerShell release signer
/// implements the identical canonicalization so signatures agree cross-language.
/// </summary>
public sealed class ReleaseManifest
{
    public const int CurrentSchemaVersion = 1;

    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    [JsonPropertyName("product")]
    public string Product { get; set; } = "PathVeer";

    [JsonPropertyName("version")]
    public string Version { get; set; } = "";

    [JsonPropertyName("channel")]
    public string Channel { get; set; } = "stable";

    [JsonPropertyName("platform")]
    public string Platform { get; set; } = "windows";

    [JsonPropertyName("architecture")]
    public string Architecture { get; set; } = "x64";

    [JsonPropertyName("publishedAtUtc")]
    public string? PublishedAtUtc { get; set; }

    [JsonPropertyName("minimumUpgradeVersion")]
    public string? MinimumUpgradeVersion { get; set; }

    [JsonPropertyName("installer")]
    public ManifestArtifact? Installer { get; set; }

    [JsonPropertyName("packageArchive")]
    public ManifestArtifact? PackageArchive { get; set; }

    [JsonPropertyName("signed")]
    public bool Signed { get; set; }

    [JsonPropertyName("signature")]
    public ManifestSignature? Signature { get; set; }

    [JsonPropertyName("releaseMode")]
    public string? ReleaseMode { get; set; }

    [JsonPropertyName("components")]
    public string[]? Components { get; set; }

    [JsonPropertyName("releaseNotesUrl")]
    public string? ReleaseNotesUrl { get; set; }

    public sealed class ManifestArtifact
    {
        [JsonPropertyName("fileName")]
        public string FileName { get; set; } = "";

        [JsonPropertyName("url")]
        public string Url { get; set; } = "";

        [JsonPropertyName("sha256")]
        public string Sha256 { get; set; } = "";

        [JsonPropertyName("size")]
        public long Size { get; set; }
    }

    public sealed class ManifestSignature
    {
        [JsonPropertyName("algorithm")]
        public string Algorithm { get; set; } = "ES256";

        [JsonPropertyName("keyId")]
        public string KeyId { get; set; } = "";

        [JsonPropertyName("value")]
        public string Value { get; set; } = "";
    }

    /// <summary>
    /// Canonical bytes of the manifest payload (signature node removed). This is
    /// what gets signed and verified. Casing is preserved; only key order and
    /// whitespace are normalized.
    /// </summary>
    public static byte[] CanonicalizePayload(string rawJson)
    {
        using var doc = JsonDocument.Parse(rawJson);
        var root = doc.RootElement.Clone();
        if (root.ValueKind == JsonValueKind.Object)
        {
            var obj = JsonNode.Parse(rawJson)!.AsObject();
            // Exclude envelope/status fields that are not part of the signed
            // content: signature (the output) and signed (a presentation flag
            // that flips to true only after signing).
            obj.Remove("signature");
            obj.Remove("signed");
            return JsonCanonicalizer.Canonicalize(obj);
        }
        return JsonCanonicalizer.Canonicalize(root);
    }

    public string ToJson(bool indented = false)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = indented,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        return JsonSerializer.Serialize(this, options);
    }
}
