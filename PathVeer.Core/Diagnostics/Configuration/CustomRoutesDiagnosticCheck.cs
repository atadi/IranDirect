using PathVeer.Core.CustomRoutes;
using PathVeer.Core.Diagnostics;

namespace PathVeer.Core.Diagnostics.Configuration;

public sealed class CustomRoutesDiagnosticCheck :
    IDiagnosticCheck
{
    private readonly ICustomRouteRepository _routeRepository;

    public CustomRoutesDiagnosticCheck(
        ICustomRouteRepository routeRepository)
    {
        _routeRepository = routeRepository;
    }

    public string Id => "custom-routes";
    public string Title => "Custom routes";

    public async Task<DiagnosticResult> CheckAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyList<CustomRouteEntry> entries =
                await _routeRepository.GetAllAsync(
                    cancellationToken);

            CustomRouteCollection collection =
                new() { Entries = entries };

            if (collection.SchemaVersion != 1)
            {
                return new DiagnosticResult(
                    Id: Id,
                    Title: Title,
                    Status: DiagnosticStatus.Failed,
                    Severity: DiagnosticSeverity.Error,
                    Message:
                        $"Unsupported schema version: " +
                        $"{collection.SchemaVersion}.",
                    SuggestedAction:
                        "Update the custom routes file to " +
                        "schema version 1.");
            }

            HashSet<Guid> seenIds = [];
            List<string> duplicateIds = [];

            foreach (CustomRouteEntry entry in entries)
            {
                if (!seenIds.Add(entry.Id))
                {
                    duplicateIds.Add(entry.Id.ToString());
                }
            }

            if (duplicateIds.Count > 0)
            {
                return new DiagnosticResult(
                    Id: Id,
                    Title: Title,
                    Status: DiagnosticStatus.Failed,
                    Severity: DiagnosticSeverity.Error,
                    Message:
                        $"Duplicate route IDs found: " +
                        $"{string.Join(", ", duplicateIds)}.",
                    SuggestedAction:
                        "Ensure all route entries have unique " +
                        "IDs.");
            }

            List<string> invalidEntries = [];
            foreach (CustomRouteEntry entry in entries)
            {
                if (string.IsNullOrWhiteSpace(entry.Value))
                {
                    invalidEntries.Add(entry.Id.ToString());
                }
            }

            if (invalidEntries.Count > 0)
            {
                return new DiagnosticResult(
                    Id: Id,
                    Title: Title,
                    Status: DiagnosticStatus.Failed,
                    Severity: DiagnosticSeverity.Error,
                    Message:
                        $"Invalid entries (empty value) found " +
                        $"for IDs: " +
                        $"{string.Join(", ", invalidEntries)}.",
                    SuggestedAction:
                        "Ensure all route entries have a non-empty value.");
            }

            HashSet<string> seenValues =
                new(StringComparer.OrdinalIgnoreCase);
            HashSet<string> duplicateValues = [];

            foreach (CustomRouteEntry entry in entries)
            {
                if (!entry.Enabled)
                {
                    continue;
                }

                if (!seenValues.Add(entry.Value))
                {
                    duplicateValues.Add(entry.Value);
                }
            }

            if (duplicateValues.Count > 0)
            {
                return new DiagnosticResult(
                    Id: Id,
                    Title: Title,
                    Status: DiagnosticStatus.Warning,
                    Severity: DiagnosticSeverity.Warning,
                    Message:
                        $"Duplicate normalized values found " +
                        $"for enabled entries: " +
                        $"{string.Join(", ", duplicateValues)}.",
                    SuggestedAction:
                        "Remove or update duplicate enabled " +
                        "route entries.");
            }

            return new DiagnosticResult(
                Id: Id,
                Title: Title,
                Status: DiagnosticStatus.Passed,
                Severity: DiagnosticSeverity.Info,
                Message:
                    $"{entries.Count} custom route(s) valid.",
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
                    $"Could not read custom routes: " +
                    $"{exception.Message}",
                SuggestedAction:
                    "Ensure the custom routes file exists and " +
                    "is readable.");
        }
    }
}