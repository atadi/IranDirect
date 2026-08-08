using PathVeer.Core.Diagnostics;
using PathVeer.Core.Routing;

namespace PathVeer.Core.Diagnostics.Runtime;

public sealed class RouteInventoryDiagnosticCheck :
    IDiagnosticCheck
{
    private readonly IRouteInventoryPersistence _routeInventory;

    public RouteInventoryDiagnosticCheck(
        IRouteInventoryPersistence routeInventory)
    {
        _routeInventory = routeInventory;
    }

    public string Id => "route-inventory";
    public string Title => "Route inventory";

    public async Task<DiagnosticResult> CheckAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            RouteInventory inventory =
                await _routeInventory.LoadAsync(
                    cancellationToken);

            if (inventory.SchemaVersion != 1)
            {
                return new DiagnosticResult(
                    Id: Id,
                    Title: Title,
                    Status: DiagnosticStatus.Failed,
                    Severity: DiagnosticSeverity.Error,
                    Message:
                        $"Unsupported schema version: " +
                        $"{inventory.SchemaVersion}.",
                    SuggestedAction:
                        "Update the route inventory to schema version 1.");
            }

            var seenPrefixes = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            var duplicatePrefixes = new List<string>();

            var seenOwnership = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            var duplicateOwnership = new List<string>();

            var seenIdentities = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            var duplicateIdentities = new List<string>();

            foreach (RouteInventoryItem route in inventory.Routes)
            {
                if (string.IsNullOrWhiteSpace(route.DestinationPrefix))
                {
                    return new DiagnosticResult(
                        Id: Id,
                        Title: Title,
                        Status: DiagnosticStatus.Failed,
                        Severity: DiagnosticSeverity.Error,
                        Message:
                            "Malformed entry: DestinationPrefix is empty.",
                        SuggestedAction:
                            "Ensure all route entries have a valid destination prefix.");
                }

                if (string.IsNullOrWhiteSpace(route.Gateway))
                {
                    return new DiagnosticResult(
                        Id: Id,
                        Title: Title,
                        Status: DiagnosticStatus.Failed,
                        Severity: DiagnosticSeverity.Error,
                        Message:
                            "Malformed entry: Gateway is empty.",
                        SuggestedAction:
                            "Ensure all route entries have a valid gateway.");
                }

                if (!seenPrefixes.Add(route.DestinationPrefix))
                {
                    duplicatePrefixes.Add(route.DestinationPrefix);
                }

                string ownershipKey =
                    $"{route.DestinationPrefix}|{route.Gateway}";

                if (!seenOwnership.Add(ownershipKey))
                {
                    duplicateOwnership.Add(ownershipKey);
                }

                string identity = route.Identity;

                if (!seenIdentities.Add(identity))
                {
                    duplicateIdentities.Add(identity);
                }
            }

            if (duplicatePrefixes.Count > 0)
            {
                return new DiagnosticResult(
                    Id: Id,
                    Title: Title,
                    Status: DiagnosticStatus.Warning,
                    Severity: DiagnosticSeverity.Warning,
                    Message:
                        $"Duplicate prefixes found: " +
                        $"{string.Join(", ", duplicatePrefixes)}.",
                    SuggestedAction:
                        "Remove duplicate prefix entries.");
            }

            if (duplicateOwnership.Count > 0)
            {
                return new DiagnosticResult(
                    Id: Id,
                    Title: Title,
                    Status: DiagnosticStatus.Warning,
                    Severity: DiagnosticSeverity.Warning,
                    Message:
                        $"Duplicate ownership entries found.",
                    SuggestedAction:
                        "Remove duplicate route entries with the same prefix and gateway.");
            }

            if (duplicateIdentities.Count > 0)
            {
                return new DiagnosticResult(
                    Id: Id,
                    Title: Title,
                    Status: DiagnosticStatus.Warning,
                    Severity: DiagnosticSeverity.Warning,
                    Message:
                        $"Duplicate route identities found.",
                    SuggestedAction:
                        "Remove duplicate route entries.");
            }

            return new DiagnosticResult(
                Id: Id,
                Title: Title,
                Status: DiagnosticStatus.Passed,
                Severity: DiagnosticSeverity.Info,
                Message:
                    $"{inventory.Routes.Count} route(s) valid.",
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
                    $"Could not read route inventory: " +
                    $"{exception.Message}",
                SuggestedAction:
                    "Ensure the route inventory file exists and is readable.");
        }
    }
}