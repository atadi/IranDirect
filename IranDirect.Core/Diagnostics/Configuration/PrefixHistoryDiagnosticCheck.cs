using IranDirect.Core.Diagnostics;
using IranDirect.Core.Prefixes;

namespace IranDirect.Core.Diagnostics.Configuration;

public sealed class PrefixHistoryDiagnosticCheck :
    IDiagnosticCheck
{
    private readonly IPrefixSourceUpdateHistoryRepository
        _historyRepository;
    private readonly PrefixSourceHistoryOptions _historyOptions;

    public PrefixHistoryDiagnosticCheck(
        IPrefixSourceUpdateHistoryRepository historyRepository,
        PrefixSourceHistoryOptions historyOptions)
    {
        _historyRepository = historyRepository;
        _historyOptions = historyOptions;
    }

    public string Id => "prefix-history";
    public string Title => "Prefix update history";

    public async Task<DiagnosticResult> CheckAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            PrefixSourceUpdateHistoryDocument document =
                await _historyRepository.LoadAsync(
                    cancellationToken);

            if (document.SchemaVersion != 1)
            {
                return new DiagnosticResult(
                    Id: Id,
                    Title: Title,
                    Status: DiagnosticStatus.Failed,
                    Severity: DiagnosticSeverity.Error,
                    Message:
                        $"Unsupported history schema " +
                        $"version: {document.SchemaVersion}.",
                    SuggestedAction:
                        "Update the history document to " +
                        "schema version 1.");
            }

            IReadOnlyList<PrefixSourceUpdateHistoryEntry>
                entries = document.Entries;

            if (entries.Count == 0)
            {
                return new DiagnosticResult(
                    Id: Id,
                    Title: Title,
                    Status: DiagnosticStatus.Passed,
                    Severity: DiagnosticSeverity.Info,
                    Message: "No history entries found.",
                    SuggestedAction: null);
            }

            for (int i = 1; i < entries.Count; i++)
            {
                if (entries[i].CompletedAt <
                    entries[i - 1].CompletedAt)
                {
                    return new DiagnosticResult(
                        Id: Id,
                        Title: Title,
                        Status: DiagnosticStatus.Failed,
                        Severity: DiagnosticSeverity.Error,
                        Message:
                            $"Timestamps are not monotonic. " +
                            $"Entry {i - 1} CompletedAt: " +
                            $"{entries[i - 1].CompletedAt}. " +
                            $"Entry {i} CompletedAt: " +
                            $"{entries[i].CompletedAt}.",
                        SuggestedAction:
                            "Ensure history entry timestamps " +
                            "are in ascending order.");
                }
            }

            if (entries.Count > _historyOptions.RetentionCount)
            {
                return new DiagnosticResult(
                    Id: Id,
                    Title: Title,
                    Status: DiagnosticStatus.Warning,
                    Severity: DiagnosticSeverity.Warning,
                    Message:
                        $"History entry count ({entries.Count}) " +
                        $"exceeds configured retention limit " +
                        $"({_historyOptions.RetentionCount}).",
                    SuggestedAction:
                        "Review the retention configuration or " +
                        "allow the system to prune old entries.");
            }

            return new DiagnosticResult(
                Id: Id,
                Title: Title,
                Status: DiagnosticStatus.Passed,
                Severity: DiagnosticSeverity.Info,
                Message:
                    $"{entries.Count} history entry(ies) " +
                    "valid.",
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
                    $"Could not read history: " +
                    $"{exception.Message}",
                SuggestedAction:
                    "Ensure the history document exists and " +
                    "is readable.");
        }
    }
}