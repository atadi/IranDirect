namespace PathVeer.Core.Update;

using System.Text.Json;

/// <summary>
/// Parses and validates a release manifest. Rejects:
///   * unknown schemaVersion (no best-effort on future schemas)
///   * wrong product / platform / architecture
///   * missing required fields (version, installer.fileName, installer.sha256)
/// Harmless unknown OPTIONAL fields are ignored (forward compatible).
/// </summary>
public sealed class ReleaseManifestParser
{
    public const string ExpectedProduct = "PathVeer";
    public const string ExpectedPlatform = "windows";
    public const string ExpectedArchitecture = "x64";

    public ParseResult Parse(string json, string? expectedArchitecture = ExpectedArchitecture)
    {
        if (string.IsNullOrWhiteSpace(json))
            return ParseResult.Fail("Empty manifest content.");

        ReleaseManifest? m;
        try
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            };
            m = JsonSerializer.Deserialize<ReleaseManifest>(json, options);
        }
        catch (JsonException ex)
        {
            return ParseResult.Fail($"Manifest is not valid JSON: {ex.Message}");
        }

        if (m is null) return ParseResult.Fail("Manifest deserialized to null.");
        if (m.SchemaVersion != ReleaseManifest.CurrentSchemaVersion)
            return ParseResult.Fail($"Unsupported schemaVersion {m.SchemaVersion}; expected {ReleaseManifest.CurrentSchemaVersion}.");
        if (!string.Equals(m.Product, ExpectedProduct, StringComparison.Ordinal))
            return ParseResult.Fail($"Wrong product: '{m.Product}'.");
        if (!string.Equals(m.Platform, ExpectedPlatform, StringComparison.OrdinalIgnoreCase))
            return ParseResult.Fail($"Wrong platform: '{m.Platform}'.");
        if (!string.IsNullOrWhiteSpace(expectedArchitecture) &&
            !string.Equals(m.Architecture, expectedArchitecture, StringComparison.OrdinalIgnoreCase))
            return ParseResult.Fail($"Wrong architecture: '{m.Architecture}' (expected {expectedArchitecture}).");

        if (string.IsNullOrWhiteSpace(m.Version) || !SemanticVersion.TryParse(m.Version, out _))
            return ParseResult.Fail("Manifest version missing or malformed.");
        if (m.Installer is null)
            return ParseResult.Fail("Manifest missing installer artifact.");
        if (string.IsNullOrWhiteSpace(m.Installer.FileName))
            return ParseResult.Fail("Manifest installer missing fileName.");
        if (string.IsNullOrWhiteSpace(m.Installer.Sha256) || !IsValidSha256(m.Installer.Sha256))
            return ParseResult.Fail("Manifest installer missing or malformed sha256.");
        if (!IsSafeUrl(m.Installer.Url))
            return ParseResult.Fail("Manifest installer url is unsafe or missing.");

        return ParseResult.Ok(m);
    }

    public static bool IsValidSha256(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return false;
        if (s.Length != 64) return false;
        foreach (var c in s)
            if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'))) return false;
        return true;
    }

    private static bool IsSafeUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        // file:// allowed only for localhost/test feeds; https:// otherwise.
        if (uri.Scheme == Uri.UriSchemeHttps) return true;
        if (uri.Scheme == Uri.UriSchemeFile) return true;
        if (uri.Scheme == Uri.UriSchemeHttp && (uri.Host == "localhost" || uri.Host == "127.0.0.1")) return true;
        return false;
    }
}

public sealed class ParseResult
{
    public bool Success { get; }
    public ReleaseManifest? Manifest { get; }
    public string? Error { get; }
    private ParseResult(bool success, ReleaseManifest? m, string? error)
    {
        Success = success; Manifest = m; Error = error;
    }
    public static ParseResult Ok(ReleaseManifest m) => new(true, m, null);
    public static ParseResult Fail(string error) => new(false, null, error);
}
