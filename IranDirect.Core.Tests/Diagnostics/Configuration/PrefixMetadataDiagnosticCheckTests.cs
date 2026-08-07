using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using IranDirect.Core.Configuration;
using IranDirect.Core.Diagnostics;
using IranDirect.Core.Diagnostics.Configuration;
using IranDirect.Core.Prefixes;

namespace IranDirect.Core.Tests.Diagnostics.Configuration;

public sealed class PrefixMetadataDiagnosticCheckTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private static readonly JsonSerializerOptions SeedOptions = new()
    {
        WriteIndented = true
    };

    static PrefixMetadataDiagnosticCheckTests()
    {
        JsonOptions.Converters.Add(new JsonStringEnumConverter());
        SeedOptions.Converters.Add(new JsonStringEnumConverter());
    }

    private static PrefixSourceMetadataDocument ValidDocument()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return new()
        {
            SchemaVersion = 1,
            Current = new PrefixSourceMetadata
            {
                SourceId = "official",
                SourceDisplayName = "Official Iran Prefixes",
                Format = "text",
                ParserVersion = "1.0",
                LastAttemptedAt = now,
                LastSucceededAt = now,
                LastStatus = PrefixSourceUpdateStatus.Succeeded,
                ContentHash = new string('0', 64)
            }
        };
    }

    private static CountryPrefixStore SeedStore(
        PrefixSourceMetadataDocument document)
    {
        string dir = Path.Combine(
            Path.GetTempPath(),
            "IranDirect.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        CountryPrefixStore store = new(dir);
        string metadataFile = store.MetadataFileFor(DirectCountryCode.IR);
        Directory.CreateDirectory(Path.GetDirectoryName(metadataFile)!);
        File.WriteAllText(
            metadataFile,
            JsonSerializer.Serialize(document, SeedOptions));
        return store;
    }

    [Fact]
    public async Task CheckAsync_ValidMetadata_ReturnsPassed()
    {
        var check = new PrefixMetadataDiagnosticCheck(
            SeedStore(ValidDocument()),
            () => DirectCountryCode.IR);

        DiagnosticResult result = await check.CheckAsync(
            CancellationToken.None);

        Assert.Equal(DiagnosticStatus.Passed, result.Status);
    }

    [Fact]
    public async Task CheckAsync_UnsupportedSchemaVersion_ReturnsFailed()
    {
        var doc = ValidDocument() with { SchemaVersion = 2 };

        var check = new PrefixMetadataDiagnosticCheck(
            SeedStore(doc),
            () => DirectCountryCode.IR);

        DiagnosticResult result = await check.CheckAsync(
            CancellationToken.None);

        Assert.Equal(DiagnosticStatus.Failed, result.Status);
        Assert.Equal(DiagnosticSeverity.Error, result.Severity);
    }

    [Fact]
    public async Task CheckAsync_NullCurrent_ReturnsFailed()
    {
        var doc = ValidDocument() with { Current = null };

        var check = new PrefixMetadataDiagnosticCheck(
            SeedStore(doc),
            () => DirectCountryCode.IR);

        DiagnosticResult result = await check.CheckAsync(
            CancellationToken.None);

        Assert.Equal(DiagnosticStatus.Failed, result.Status);
        Assert.Equal(DiagnosticSeverity.Error, result.Severity);
    }

    [Fact]
    public async Task CheckAsync_EmptySourceId_ReturnsFailed()
    {
        var doc = ValidDocument();
        doc = doc with
        {
            Current = doc.Current! with { SourceId = "" }
        };

        var check = new PrefixMetadataDiagnosticCheck(
            SeedStore(doc),
            () => DirectCountryCode.IR);

        DiagnosticResult result = await check.CheckAsync(
            CancellationToken.None);

        Assert.Equal(DiagnosticStatus.Failed, result.Status);
        Assert.Equal(DiagnosticSeverity.Error, result.Severity);
    }

    [Fact]
    public async Task CheckAsync_FailedStatusNoLastError_ReturnsWarning()
    {
        var doc = ValidDocument();
        doc = doc with
        {
            Current = doc.Current! with
            {
                LastStatus = PrefixSourceUpdateStatus.Failed,
                LastError = null
            }
        };

        var check = new PrefixMetadataDiagnosticCheck(
            SeedStore(doc),
            () => DirectCountryCode.IR);

        DiagnosticResult result = await check.CheckAsync(
            CancellationToken.None);

        Assert.Equal(DiagnosticStatus.Warning, result.Status);
        Assert.Equal(DiagnosticSeverity.Warning, result.Severity);
    }

    [Fact]
    public async Task CheckAsync_RepoThrows_ReturnsFailed()
    {
        string dir = Path.Combine(
            Path.GetTempPath(),
            "IranDirect.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        var check = new PrefixMetadataDiagnosticCheck(
            new CountryPrefixStore(dir),
            () => DirectCountryCode.IR);

        DiagnosticResult result = await check.CheckAsync(
            CancellationToken.None);

        Assert.Equal(DiagnosticStatus.Failed, result.Status);
        Assert.Equal(DiagnosticSeverity.Error, result.Severity);
        Assert.NotNull(result.SuggestedAction);
    }
}
