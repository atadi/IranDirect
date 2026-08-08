using System.Net;
using PathVeer.Core.Routing;

namespace PathVeer.Core.Diagnostics.Routing;

public sealed class WindowsRouteTableDiagnosticCheck :
    IDiagnosticCheck
{
    private readonly IRouteManager _routeManager;

    public WindowsRouteTableDiagnosticCheck(
        IRouteManager routeManager)
    {
        _routeManager = routeManager;
    }

    public string Id => "windows-route-table";
    public string Title => "Windows route table";

    public async Task<DiagnosticResult> CheckAsync(
        CancellationToken cancellationToken)
    {
        IReadOnlyList<SystemRoute> routes;

        try
        {
            routes = await _routeManager.GetIpv4RoutesAsync(
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

        var seenIdentities = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        var duplicateIdentities = new List<string>();

        foreach (SystemRoute route in routes)
        {
            if (route.NextHop is null ||
                IPAddress.IsLoopback(route.NextHop))
            {
                continue;
            }

            if (route.InterfaceIndex == 0)
            {
                return new DiagnosticResult(
                    Id: Id,
                    Title: Title,
                    Status: DiagnosticStatus.Warning,
                    Severity: DiagnosticSeverity.Warning,
                    Message:
                        $"Route '{route.DestinationPrefix}' has " +
                        $"InterfaceIndex 0.",
                    SuggestedAction:
                        "Investigate routes with zero interface index.");
            }

            if (string.IsNullOrWhiteSpace(route.DestinationPrefix))
            {
                return new DiagnosticResult(
                    Id: Id,
                    Title: Title,
                    Status: DiagnosticStatus.Warning,
                    Severity: DiagnosticSeverity.Warning,
                    Message: "A route has an empty destination prefix.",
                    SuggestedAction:
                        "Investigate routes with missing destination prefixes.");
            }

            string identity =
                $"{route.DestinationPrefix}|" +
                $"{route.NextHop}|" +
                $"{route.InterfaceIndex}";

            if (!seenIdentities.Add(identity))
            {
                duplicateIdentities.Add(identity);
            }
        }

        if (duplicateIdentities.Count > 0)
        {
            return new DiagnosticResult(
                Id: Id,
                Title: Title,
                Status: DiagnosticStatus.Warning,
                Severity: DiagnosticSeverity.Warning,
                Message:
                    $"Duplicate managed routes found: " +
                    $"{string.Join(", ", duplicateIdentities)}.",
                SuggestedAction:
                    "Remove duplicate managed route entries.");
        }

        return new DiagnosticResult(
            Id: Id,
            Title: Title,
            Status: DiagnosticStatus.Passed,
            Severity: DiagnosticSeverity.Info,
            Message:
                $"{routes.Count} route(s) in Windows table.",
            SuggestedAction: null);
    }
}
