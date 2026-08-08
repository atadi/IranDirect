using System.Net;
using PathVeer.Core.Routing;

namespace PathVeer.Core.Diagnostics.Routing;

public sealed class RouteOwnershipDiagnosticCheck :
    IDiagnosticCheck
{
    private readonly IRouteManager _routeManager;
    private readonly IRouteInventoryPersistence _inventory;

    public RouteOwnershipDiagnosticCheck(
        IRouteManager routeManager,
        IRouteInventoryPersistence inventory)
    {
        _routeManager = routeManager;
        _inventory = inventory;
    }

    public string Id => "route-ownership";
    public string Title => "Route ownership";

    public async Task<DiagnosticResult> CheckAsync(
        CancellationToken cancellationToken)
    {
        RouteInventory inventory;

        try
        {
            inventory = await _inventory.LoadAsync(
                cancellationToken);
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

        IReadOnlyList<SystemRoute> windowsRoutes;

        try
        {
            windowsRoutes =
                await _routeManager.GetIpv4RoutesAsync(
                    cancellationToken);
        }
        catch (Exception exception)
        {
            return new DiagnosticResult(
                Id: Id,
                Title: Title,
                Status: DiagnosticStatus.Failed,
                Severity: DiagnosticSeverity.Error,
                Message:
                    $"Could not read Windows route table: " +
                    $"{exception.Message}",
                SuggestedAction:
                    "Ensure the service has permission to read the route table.");
        }

        var seenOwnership = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        var duplicateOwnership = new List<string>();

        foreach (RouteInventoryItem item in inventory.Routes)
        {
            string ownershipKey =
                $"{item.DestinationPrefix}|{item.Gateway}";

            if (!seenOwnership.Add(ownershipKey))
            {
                duplicateOwnership.Add(ownershipKey);
            }
        }

        if (duplicateOwnership.Count > 0)
        {
            return new DiagnosticResult(
                Id: Id,
                Title: Title,
                Status: DiagnosticStatus.Warning,
                Severity: DiagnosticSeverity.Warning,
                Message:
                    $"Duplicate inventory ownership found: " +
                    $"{string.Join(", ", duplicateOwnership)}.",
                SuggestedAction:
                    "Remove duplicate inventory entries.");
        }

        var windowsIdentities = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        foreach (SystemRoute route in windowsRoutes)
        {
            string gw = route.NextHop?.ToString() ?? "";
            string identity =
                $"{route.DestinationPrefix}|{gw}|" +
                $"{route.InterfaceIndex}";
            windowsIdentities.Add(identity);
        }

        var missing = new List<string>();

        foreach (RouteInventoryItem item in inventory.Routes)
        {
            string identity =
                $"{item.DestinationPrefix}|{item.Gateway}|" +
                $"{item.InterfaceIndex}";

            if (!windowsIdentities.Contains(identity))
            {
                missing.Add(identity);
            }
        }

        if (missing.Count > 0)
        {
            return new DiagnosticResult(
                Id: Id,
                Title: Title,
                Status: DiagnosticStatus.Warning,
                Severity: DiagnosticSeverity.Warning,
                Message:
                    $"Inventory routes missing from Windows: " +
                    $"{string.Join(", ", missing)}.",
                SuggestedAction:
                    "Verify missing routes are still required.");
        }

        return new DiagnosticResult(
            Id: Id,
            Title: Title,
            Status: DiagnosticStatus.Passed,
            Severity: DiagnosticSeverity.Info,
            Message:
                $"{inventory.Routes.Count} inventory route(s) " +
                $"match Windows table.",
            SuggestedAction: null);
    }
}
