using IranDirect.Core.Diagnostics;
using IranDirect.Core.Prefixes;

namespace IranDirect.Core.Diagnostics.Configuration;

public sealed class PrefixMetadataDiagnosticCheck :
    IDiagnosticCheck
{
    private readonly IPrefixSourceMetadataRepository
        _metadataRepository;

    public PrefixMetadataDiagnosticCheck(
        IPrefixSourceMetadataRepository metadataRepository)
    {
        _metadataRepository = metadataRepository;
    }

    public string Id => "prefix-metadata";
    public string Title => "Prefix metadata";

    public async Task<DiagnosticResult> CheckAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            PrefixSourceMetadataDocument document =
                await _metadataRepository.LoadAsync(
                    cancellationToken);

            if (document.SchemaVersion != 1)
            {
                return new DiagnosticResult(
                    Id: Id,
                    Title: Title,
                    Status: DiagnosticStatus.Failed,
                    Severity: DiagnosticSeverity.Error,
                    Message:
                        $"Unsupported metadata schema " +
                        $"version: {document.SchemaVersion}.",
                    SuggestedAction:
                        "Update the metadata document to " +
                        "schema version 1.");
            }

            if (document.Current is null)
            {
                return new DiagnosticResult(
                    Id: Id,
                    Title: Title,
                    Status: DiagnosticStatus.Failed,
                    Severity: DiagnosticSeverity.Error,
                    Message: "No current metadata available.",
                    SuggestedAction:
                        "Ensure the metadata document has a " +
                        "current entry.");
            }

            PrefixSourceMetadata current = document.Current;

            if (string.IsNullOrWhiteSpace(current.SourceId))
            {
                return new DiagnosticResult(
                    Id: Id,
                    Title: Title,
                    Status: DiagnosticStatus.Failed,
                    Severity: DiagnosticSeverity.Error,
                    Message: "SourceId is missing.",
                    SuggestedAction:
                        "Ensure the metadata document has a " +
                        "valid SourceId.");
            }

            if (string.IsNullOrWhiteSpace(current.Format))
            {
                return new DiagnosticResult(
                    Id: Id,
                    Title: Title,
                    Status: DiagnosticStatus.Failed,
                    Severity: DiagnosticSeverity.Error,
                    Message: "Format is missing.",
                    SuggestedAction:
                        "Ensure the metadata document has a " +
                        "valid Format.");
            }

            if (string.IsNullOrWhiteSpace(current.ParserVersion))
            {
                return new DiagnosticResult(
                    Id: Id,
                    Title: Title,
                    Status: DiagnosticStatus.Failed,
                    Severity: DiagnosticSeverity.Error,
                    Message: "ParserVersion is missing.",
                    SuggestedAction:
                        "Ensure the metadata document has a " +
                        "valid ParserVersion.");
            }

            if (current.LastAttemptedAt == default)
            {
                return new DiagnosticResult(
                    Id: Id,
                    Title: Title,
                    Status: DiagnosticStatus.Failed,
                    Severity: DiagnosticSeverity.Error,
                    Message: "LastAttemptedAt is missing.",
                    SuggestedAction:
                        "Ensure the metadata document has a " +
                        "valid LastAttemptedAt timestamp.");
            }

            if (current.LastSucceededAt.HasValue &&
                current.LastSucceededAt < current.LastAttemptedAt)
            {
                return new DiagnosticResult(
                    Id: Id,
                    Title: Title,
                    Status: DiagnosticStatus.Failed,
                    Severity: DiagnosticSeverity.Error,
                    Message:
                        "LastSucceededAt is before " +
                        "LastAttemptedAt.",
                    SuggestedAction:
                        "Ensure timestamps are consistent.");
            }

            if (current.ContentHash is not null &&
                current.ContentHash.Length != 64)
            {
                return new DiagnosticResult(
                    Id: Id,
                    Title: Title,
                    Status: DiagnosticStatus.Failed,
                    Severity: DiagnosticSeverity.Error,
                    Message:
                        $"ContentHash has invalid length: " +
                        $"{current.ContentHash.Length}. " +
                        "Expected 64 characters (SHA-256).",
                    SuggestedAction:
                        "Ensure the ContentHash is a valid " +
                        "SHA-256 hex string.");
            }

            if (current.ContentHash is not null &&
                !System.Text.RegularExpressions.Regex.IsMatch(
                    current.ContentHash,
                    "^[0-9a-fA-F]{64}$"))
            {
                return new DiagnosticResult(
                    Id: Id,
                    Title: Title,
                    Status: DiagnosticStatus.Failed,
                    Severity: DiagnosticSeverity.Error,
                    Message:
                        "ContentHash is not a valid hex string.",
                    SuggestedAction:
                        "Ensure the ContentHash is a valid " +
                        "SHA-256 hex string.");
            }

            PrefixSourceUpdateStatus status = current.LastStatus;

            if (status == PrefixSourceUpdateStatus.Failed &&
                string.IsNullOrWhiteSpace(current.LastError))
            {
                return new DiagnosticResult(
                    Id: Id,
                    Title: Title,
                    Status: DiagnosticStatus.Warning,
                    Severity: DiagnosticSeverity.Warning,
                    Message:
                        "Status is Failed but LastError is " +
                        "empty.",
                    SuggestedAction:
                        "Ensure Failed status includes an error " +
                        "message.");
            }

            if (status == PrefixSourceUpdateStatus.Succeeded &&
                current.LastSucceededAt is null)
            {
                return new DiagnosticResult(
                    Id: Id,
                    Title: Title,
                    Status: DiagnosticStatus.Warning,
                    Severity: DiagnosticSeverity.Warning,
                    Message:
                        "Status is Succeeded but " +
                        "LastSucceededAt is null.",
                    SuggestedAction:
                        "Ensure Succeeded status includes a " +
                        "LastSucceededAt timestamp.");
            }

            return new DiagnosticResult(
                Id: Id,
                Title: Title,
                Status: DiagnosticStatus.Passed,
                Severity: DiagnosticSeverity.Info,
                Message: "Metadata document is valid.",
                SuggestedAction: null);
        }
        catch (Exception exception)
        {
            return new DiagnosticResult(
                Id: Id,
                Title: Title,
                Status: DiagnosticStatus.Failed,
                Severity: DiagnosticSeverity.Error,
                Message:
                    $"Could not read metadata: " +
                    $"{exception.Message}",
                SuggestedAction:
                    "Ensure the metadata document exists and " +
                    "is readable.");
        }
    }
}