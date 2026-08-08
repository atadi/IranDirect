using PathVeer.Core.Observability;
using PathVeer.Core.Routing;

namespace PathVeer.Core.Diagnostics.Routing;

public sealed class ManagedRouteConsistencyDiagnosticCheck :
    IDiagnosticCheck
{
    private readonly IRouteManager _routeManager;
    private readonly IRouteInventoryPersistence _inventory;
    private readonly IRuntimeSnapshotProvider _snapshotProvider;

    public ManagedRouteConsistencyDiagnosticCheck(
        IRouteManager routeManager,
        IRouteInventoryPersistence inventory,
        IRuntimeSnapshotProvider snapshotProvider)
    {
        _routeManager = routeManager;
        _inventory = inventory;
        _snapshotProvider = snapshotProvider;
    }

    public string Id => "managed-route-consistency";
    public string Title => "Managed route consistency";

    public async Task<DiagnosticResult> CheckAsync(
        CancellationToken cancellationToken)
    {
        RuntimeSnapshot snapshot;

        try
        {
            snapshot = await _snapshotProvider.GetSnapshotAsync(
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
                    $"Could not read runtime snapshot: " +
                    $"{exception.Message}",
                SuggestedAction:
                    "Ensure the runtime snapshot provider is available.");
        }

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

        var windowsByPrefix =
            new Dictionary<string, List<SystemRoute>>(
                StringComparer.OrdinalIgnoreCase);

        foreach (SystemRoute route in windowsRoutes)
        {
            if (!windowsByPrefix.TryGetValue(
                    route.DestinationPrefix,
                    out List<SystemRoute>? list))
            {
                list = [];
                windowsByPrefix[route.DestinationPrefix] = list;
            }

            list.Add(route);
        }

        var inventoryByIdentity =
            new Dictionary<string, RouteInventoryItem>(
                StringComparer.OrdinalIgnoreCase);

        var duplicateInventory = new List<string>();

        foreach (RouteInventoryItem item in inventory.Routes)
        {
            if (!inventoryByIdentity.TryAdd(
                    item.Identity, item))
            {
                duplicateInventory.Add(item.Identity);
            }
        }

        var extraInInventory = new List<string>();
        var gatewayMismatches = new List<string>();
        var interfaceMismatches = new List<string>();

        foreach (RouteInventoryItem item in inventory.Routes)
        {
            if (!windowsByPrefix.TryGetValue(
                    item.DestinationPrefix,
                    out List<SystemRoute>? matching))
            {
                extraInInventory.Add(item.Identity);
                continue;
            }

            bool found = false;

            foreach (SystemRoute windows in matching)
            {
                string windowsGw =
                    windows.NextHop?.ToString() ?? "";

                if (string.Equals(
                        windowsGw, item.Gateway,
                        StringComparison.OrdinalIgnoreCase) &&
                    windows.InterfaceIndex == item.InterfaceIndex)
                {
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                foreach (SystemRoute windows in matching)
                {
                    string windowsGw =
                        windows.NextHop?.ToString() ?? "";

                    if (string.Equals(
                            windowsGw, item.Gateway,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        interfaceMismatches.Add(item.Identity);
                        found = true;
                        break;
                    }

                    if (windows.InterfaceIndex ==
                        item.InterfaceIndex)
                    {
                        gatewayMismatches.Add(item.Identity);
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    extraInInventory.Add(item.Identity);
                }
            }
        }

        var missingFromInventory = new List<string>();

        foreach (SystemRoute windows in windowsRoutes)
        {
            string gw =
                windows.NextHop?.ToString() ?? "";
            string identity =
                $"{windows.DestinationPrefix}|{gw}|" +
                $"{windows.InterfaceIndex}";

            if (!inventoryByIdentity.ContainsKey(identity))
            {
                missingFromInventory.Add(identity);
            }
        }

        int managedCount = snapshot.InstalledRouteCount ?? 0;
        int inventoryCount = inventory.Routes.Count;
        int windowsCount = windowsRoutes.Count;

        if (duplicateInventory.Count > 0)
        {
            return new DiagnosticResult(
                Id: Id,
                Title: Title,
                Status: DiagnosticStatus.Warning,
                Severity: DiagnosticSeverity.Warning,
                Message:
                    $"Duplicate inventory entries: " +
                    $"{string.Join(", ", duplicateInventory)}.",
                SuggestedAction:
                    "Remove duplicate inventory entries.");
        }

        if (gatewayMismatches.Count > 0)
        {
            return new DiagnosticResult(
                Id: Id,
                Title: Title,
                Status: DiagnosticStatus.Warning,
                Severity: DiagnosticSeverity.Warning,
                Message:
                    $"Gateway mismatches: " +
                    $"{string.Join(", ", gatewayMismatches)}.",
                SuggestedAction:
                    "Investigate unexpected gateway differences.");
        }

        if (interfaceMismatches.Count > 0)
        {
            return new DiagnosticResult(
                Id: Id,
                Title: Title,
                Status: DiagnosticStatus.Warning,
                Severity: DiagnosticSeverity.Warning,
                Message:
                    $"Interface mismatches: " +
                    $"{string.Join(", ", interfaceMismatches)}.",
                SuggestedAction:
                    "Investigate unexpected interface differences.");
        }

        var parts = new List<string>
        {
            $"Managed: {managedCount}",
            $"Inventory: {inventoryCount}",
            $"Windows: {windowsCount}"
        };

        if (missingFromInventory.Count > 0)
        {
            parts.Add(
                $"Missing from inventory: " +
                $"{missingFromInventory.Count}");
        }

        if (extraInInventory.Count > 0)
        {
            parts.Add(
                $"Extra in inventory: " +
                $"{extraInInventory.Count}");
        }

        string message = string.Join(", ", parts) + ".";

        return new DiagnosticResult(
            Id: Id,
            Title: Title,
            Status: DiagnosticStatus.Passed,
            Severity: DiagnosticSeverity.Info,
            Message: message,
            SuggestedAction: null);
    }
}
