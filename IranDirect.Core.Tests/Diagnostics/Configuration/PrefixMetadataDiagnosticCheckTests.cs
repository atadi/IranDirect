using IranDirect.Core.Diagnostics;
using IranDirect.Core.Diagnostics.Configuration;
using IranDirect.Core.Prefixes;

namespace IranDirect.Core.Tests.Diagnostics.Configuration;

public sealed class PrefixMetadataDiagnosticCheckTests
{
    private static PrefixSourceMetadataDocument ValidDocument() =>
        new()
        {
            SchemaVersion = 1,
            Current = new PrefixSourceMetadata
            {
                SourceId = "official",
                SourceDisplayName = "Official Iran Prefixes",
                Format = "text",
                ParserVersion = "1.0",
                LastAttemptedAt = DateTimeOffset.UtcNow,
                LastSucceededAt = DateTimeOffset.UtcNow,
                LastStatus = PrefixSourceUpdateStatus.Succeeded,
                ContentHash = "a".PadRight(64, '0')
            }
        };

    private sealed class FakeMetadataRepository :
        IPrefixSourceMetadataRepository
    {
        private readonly PrefixSourceMetadataDocument _document;

        public FakeMetadataRepository(
            PrefixSourceMetadataDocument document)
        {
            _document = document;
        }

        public Task<PrefixSourceMetadataDocument> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_document);
        }

        public Task SaveAsync(
            PrefixSourceMetadataDocument document,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task CheckAsync_ValidMetadata_ReturnsPassed()
    {
        var check = new PrefixMetadataDiagnosticCheck(
            new FakeMetadataRepository(ValidDocument()));

        DiagnosticResult result = await check.CheckAsync(
            CancellationToken.None);

        Assert.Equal(DiagnosticStatus.Passed, result.Status);
    }

    [Fact]
    public async Task CheckAsync_UnsupportedSchemaVersion_ReturnsFailed()
    {
        var doc = ValidDocument() with { SchemaVersion = 2 };

        var check = new PrefixMetadataDiagnosticCheck(
            new FakeMetadataRepository(doc));

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
            new FakeMetadataRepository(doc));

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
            new FakeMetadataRepository(doc));

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
            new FakeMetadataRepository(doc));

        DiagnosticResult result = await check.CheckAsync(
            CancellationToken.None);

        Assert.Equal(DiagnosticStatus.Warning, result.Status);
        Assert.Equal(DiagnosticSeverity.Warning, result.Severity);
    }

    [Fact]
    public async Task CheckAsync_RepoThrows_ReturnsFailed()
    {
        var check = new PrefixMetadataDiagnosticCheck(
            new ThrowingMetadataRepository());

        DiagnosticResult result = await check.CheckAsync(
            CancellationToken.None);

        Assert.Equal(DiagnosticStatus.Failed, result.Status);
        Assert.Equal(DiagnosticSeverity.Error, result.Severity);
        Assert.NotNull(result.SuggestedAction);
    }

    private sealed class ThrowingMetadataRepository :
        IPrefixSourceMetadataRepository
    {
        public Task<PrefixSourceMetadataDocument> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            throw new IOException("file not found");
        }

        public Task SaveAsync(
            PrefixSourceMetadataDocument document,
            CancellationToken cancellationToken = default)
        {
            throw new IOException("file not found");
        }
    }
}